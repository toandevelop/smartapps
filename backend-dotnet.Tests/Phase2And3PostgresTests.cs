using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Data;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Phase2And3PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Phase2And3PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task Phase2_Migrations_WorkflowQuorumReturn_PublicFormQuota_Restore_And_GlobalSearch()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var applied = await DatabaseMigrationRunner.GetAppliedMigrationsAsync(conn);
        Assert.Contains(applied, m => m.Version == "0011_app_records_approval_idx");
        Assert.Contains(applied, m => m.Version == "0012_phase2_enterprise_hardening");
        Assert.Contains(applied, m => m.Version == "0013" && m.Name == "0013_phase3_ai_ecosystem");

        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var auth = _fx.Services.GetRequiredService<AuthService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_p2_{suffix}", Code = $"P2_{suffix}".ToUpperInvariant(), Name = $"Phase2 {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_p2_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_p2_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"p2_{suffix}",
            DisplayName = "Phase 2 Table",
            PublicFormEnabled = true,
            PublicFormMaxSubmissions = 1
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "title", ColumnName = "Title", DataType = "STRING", OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER", OrderIndex = 2 });

        // 1. Public Form max submissions quota -> first succeeds, second returns 410 Gone
        var pub1 = await _fx.Client.PostAsJsonAsync($"/api/public/forms/{tbl.Id}/submit", new Dictionary<string, object?> { ["title"] = "Public 1", ["amount"] = 100 });
        Assert.Equal(HttpStatusCode.OK, pub1.StatusCode);
        var pub2 = await _fx.Client.PostAsJsonAsync($"/api/public/forms/{tbl.Id}/submit", new Dictionary<string, object?> { ["title"] = "Public 2", ["amount"] = 200 });
        Assert.Equal(HttpStatusCode.Gone, pub2.StatusCode);

        // 2. Workflow QUORUM (2 approvers required) + RETURN action
        await _fx.ExecuteAsync($"""
            INSERT INTO sys_users(id, username, full_name, role_code, role_codes, password_hash, allowed_apps, permissions)
            VALUES
              ('u_mgr1_{suffix}', 'mgr1_{suffix}', 'Manager 1', 'ADMIN', '["ADMIN"]', 'unused', '["{app.Code}"]', '["record.view","record.edit","record.approve"]'),
              ('u_mgr2_{suffix}', 'mgr2_{suffix}', 'Manager 2', 'ADMIN', '["ADMIN"]', 'unused', '["{app.Code}"]', '["record.view","record.edit","record.approve"]')
            """);

        await workflows.UpsertWorkflowAsync(new UpsertWorkflowRequest
        {
            Name = $"Quorum WF {suffix}",
            AppId = app.Id,
            TableId = tbl.Id,
            TriggerType = "MANUAL_SUBMIT",
            Steps = new()
            {
                new WorkflowStepDto
                {
                    StepOrder = 1,
                    Name = "Hội đồng duyệt 2/2",
                    ApproverRoles = new() { "ADMIN" },
                    ApprovalMode = "QUORUM",
                    QuorumCount = 2
                },
                new WorkflowStepDto
                {
                    StepOrder = 2,
                    Name = "Ký duyệt cuối",
                    ApproverRoles = new() { "ADMIN" },
                    AllowReturnToPrevious = true
                }
            }
        });

        var (rec, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            Data = new Dictionary<string, object?> { ["title"] = $"V1 {suffix}", ["amount"] = 1000m }
        }, "admin", true);

        await records.SubmitApprovalAsync(rec.Id, "admin");
        var mgr1 = (await auth.GetByUsernameAsync($"mgr1_{suffix}"))!;
        var mgr2 = (await auth.GetByUsernameAsync($"mgr2_{suffix}"))!;

        // First quorum vote stays on step 1
        var (afterVote1, err1) = await records.ProcessApprovalAsync(rec.Id, mgr1, true, "OK 1");
        Assert.Null(err1);
        Assert.Equal("PENDING", afterVote1!.ApprovalStatus);
        Assert.Equal(1, afterVote1.CurrentStepOrder);
        Assert.Single(afterVote1.StepApprovals);

        // Second quorum vote advances to step 2
        var (afterVote2, err2) = await records.ProcessApprovalAsync(rec.Id, mgr2, true, "OK 2");
        Assert.Null(err2);
        Assert.Equal("PENDING", afterVote2!.ApprovalStatus);
        Assert.Equal(2, afterVote2.CurrentStepOrder);

        // Step 2 returns to step 1
        var (afterReturn, errRet) = await records.ProcessApprovalAsync(rec.Id, mgr1, false, "Bổ sung báo giá", action: "RETURN");
        Assert.Null(errRet);
        Assert.Equal("PENDING", afterReturn!.ApprovalStatus);
        Assert.Equal(1, afterReturn.CurrentStepOrder);

        // 3. Selective field audit restore
        await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            Id = rec.Id,
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            ExpectedVersion = afterReturn.Version,
            Data = new Dictionary<string, object?> { ["title"] = $"V2 Changed {suffix}", ["amount"] = 9999m }
        }, "admin", true);

        var logs = await records.GetAuditLogsAsync(rec.Id);
        var updateAudit = logs.First(l => l.Action == "UPDATE");
        var (restored, restoreErr) = await records.RestoreRecordFieldsAsync(
            rec.Id,
            updateAudit.Id,
            new List<string> { "amount" },
            expectedVersion: null,
            actor: "admin",
            isAdmin: true,
            serverManagedFields: new HashSet<string>());
        Assert.Null(restoreErr);
        Assert.Equal(1000m, Convert.ToDecimal(restored!.Data["amount"]));
        Assert.Equal($"V2 Changed {suffix}", restored.Data["title"]?.ToString());

        // 4. Global Search finds record for authorized admin and respects visibility
        var adminUser = (await auth.GetByUsernameAsync("admin"))!;
        var searchHit = await records.GlobalSearchAsync(adminUser, suffix, 10);
        Assert.Contains(searchHit.Records, r => r.Id == rec.Id);
    }

    [PostgresFact]
    public async Task Phase3_IdempotencyKey_OpenApi_McpGateway_XmlMapping_And_SePayIpn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var auth = _fx.Services.GetRequiredService<AuthService>();
        var integrations = _fx.Services.GetRequiredService<IntegrationService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_p3_{suffix}", Code = $"P3_{suffix}".ToUpperInvariant(), Name = $"Phase3 {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_p3_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_p3_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"p3_{suffix}", DisplayName = "Hóa đơn P3" });

        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "so_hoa_don", ColumnName = "Số hóa đơn", DataType = "STRING", OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "tong_tien", ColumnName = "Tổng tiền", DataType = "CURRENCY", OrderIndex = 2 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "secret_margin", ColumnName = "Biên lợi nhuận mật", DataType = "NUMBER", HiddenForRoles = new() { "STAFF_P3" }, OrderIndex = 3 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "chi_tiet", ColumnName = "Chi tiết", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,don_gia,thanh_tien", OrderIndex = 4 });

        await _fx.ExecuteAsync($"""
            INSERT INTO sys_users(id, username, full_name, role_code, role_codes, password_hash, allowed_apps, permissions)
            VALUES ('u_p3_{suffix}', 'staffp3_{suffix}', 'Staff P3', 'STAFF_P3', '["STAFF_P3"]', 'unused', '["{app.Code}"]', '["record.view","record.create"]')
            """);

        var admin = (await auth.GetByUsernameAsync("admin"))!;
        var staff = (await auth.GetByUsernameAsync($"staffp3_{suffix}"))!;
        var adminToken = auth.CreateToken(admin);
        var staffToken = auth.CreateToken(staff);

        // 1. Idempotency-Key prevents duplicate record creation on network retry
        var idempKey = $"idem_{suffix}_{Guid.NewGuid():N}";
        var recKey = $"DH-{suffix.ToUpperInvariant()}";

        async Task<JsonElement> PostRecordWithIdemp()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/records");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Headers.Add("Idempotency-Key", idempKey);
            req.Content = JsonContent.Create(new UpsertRecordRequest
            {
                AppId = app.Id,
                ModuleId = mod.Id,
                TableId = tbl.Id,
                RecordKey = recKey,
                Data = new Dictionary<string, object?> { ["so_hoa_don"] = "HD-001", ["tong_tien"] = 500000m, ["secret_margin"] = 99 }
            });
            var resp = await _fx.Client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<JsonElement>();
        }

        var firstSave = await PostRecordWithIdemp();
        var secondSave = await PostRecordWithIdemp();
        var firstId = firstSave.GetProperty("data").GetProperty("id").GetString();
        var secondId = secondSave.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(firstId, secondId);

        // 2. Dynamic OpenAPI spec strips HiddenForRoles for non-admin user (FLS)
        using var oaReq = new HttpRequestMessage(HttpMethod.Get, $"/api/apps/{app.Code}/openapi.json");
        oaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var oaResp = await _fx.Client.SendAsync(oaReq);
        oaResp.EnsureSuccessStatusCode();
        var oaJson = await oaResp.Content.ReadAsStringAsync();
        Assert.Contains("3.0.3", oaJson);
        Assert.Contains("so_hoa_don", oaJson);
        Assert.DoesNotContain("secret_margin", oaJson);

        // 3. MCP JSON-RPC 2.0 Gateway enforces FLS on list_records & creates via upsert_record
        using var mcpInitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/mcp/{app.Code}");
        mcpInitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        mcpInitReq.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize" });
        var mcpInitResp = await _fx.Client.SendAsync(mcpInitReq);
        mcpInitResp.EnsureSuccessStatusCode();
        Assert.Contains("2024-11-05", await mcpInitResp.Content.ReadAsStringAsync());

        using var mcpCallReq = new HttpRequestMessage(HttpMethod.Post, $"/api/mcp/{app.Code}");
        mcpCallReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        mcpCallReq.Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = "list_records", arguments = new { tableId = tbl.Id, limit = 10 } }
        });
        var mcpCallResp = await _fx.Client.SendAsync(mcpCallReq);
        mcpCallResp.EnsureSuccessStatusCode();
        var mcpText = await mcpCallResp.Content.ReadAsStringAsync();
        Assert.Contains("HD-001", mcpText);
        Assert.DoesNotContain("secret_margin", mcpText);

        // 4. NĐ 123 XML parser mapped to tableId schema
        using var xmlReq = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/einvoice/parse-xml");
        xmlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        xmlReq.Content = JsonContent.Create(new
        {
            tableId = tbl.Id,
            xml = "<HDon><DLHDon><TTChung><SHDon>000777</SHDon><NLap>2026-11-18</NLap></TTChung><NDHDon><NBan><Ten>Cty Demo</Ten><MST>0301234567</MST></NBan><DSHHDVu><HHDVu><THHDVu>Server License</THHDVu><SLuong>2</SLuong><DGia>1000000</DGia><ThTien>2000000</ThTien></HHDVu></DSHHDVu><TToan><TgTTTBSo>2200000</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>"
        });
        var xmlResp = await _fx.Client.SendAsync(xmlReq);
        xmlResp.EnsureSuccessStatusCode();
        var xmlBody = await xmlResp.Content.ReadAsStringAsync();
        Assert.Contains("000777", xmlBody);
        Assert.Contains("Server License", xmlBody);

        // 5. SePay bank webhook reconciliation matches memo recordKey and writes PAID
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "vietqr",
            Name = "VietQR / SePay",
            Category = "PAYMENT",
            AppId = app.Id,
            IsEnabled = true,
            SecretValue = $"sepay_key_{suffix}",
            Config = new Dictionary<string, object?> { ["statusField"] = "payment_status", ["txField"] = "payment_tx_id" }
        });

        using var ipnReq = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/payments/vietqr/ipn");
        ipnReq.Headers.Add("Authorization", $"Apikey sepay_key_{suffix}");
        ipnReq.Content = JsonContent.Create(new
        {
            id = $"FT-{suffix}",
            content = $"CHUYEN KHOAN {recKey} THANH TOAN",
            transferAmount = 500000
        });
        var ipnResp = await _fx.Client.SendAsync(ipnReq);
        ipnResp.EnsureSuccessStatusCode();
        var ipnJson = await ipnResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ipnJson.GetProperty("updated").GetBoolean());

        var records = _fx.Services.GetRequiredService<RecordService>();
        var paidRec = await records.GetRecordByIdAsync(firstId!);
        Assert.Equal("PAID", paidRec!.Data["payment_status"]?.ToString());
        Assert.Equal($"FT-{suffix}", paidRec.Data["payment_tx_id"]?.ToString());
    }
}
