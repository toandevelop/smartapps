using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Phase2EnginePostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Phase2EnginePostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task AuthenticateAsync(string username, string password = "123456")
    {
        using var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [PostgresFact]
    public async Task FormLayout_AutoJsonbIndex_VietnameseSearch_AndVnFormulas_WorkEndToEnd()
    {
        await AuthenticateAsync("admin");
        var metadata = _fx.Services.GetRequiredService<MetadataService>();

        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = $"P2ENG{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Name = "Kiểm thử Động cơ Dữ liệu Đợt 2",
            Department = "IT"
        });
        var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "CORE",
            Name = "Phân hệ chính"
        });
        var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"p2_eng_{Guid.NewGuid().ToString("N")[..5]}",
            DisplayName = "Hợp đồng Kiểm thử",
            FormLayout = new FormLayoutConfigDto
            {
                Mode = "wizard",
                Sections =
                [
                    new FormLayoutSectionDto
                    {
                        Id = "sec_general",
                        Title = "Thông tin chung",
                        Columns = 2,
                        CalloutText = "Điền đầy đủ trước khi qua bước 2",
                        Fields =
                        [
                            new FormLayoutFieldItemDto { ColumnCode = "ten_khach", ColSpan = 2 },
                            new FormLayoutFieldItemDto { ColumnCode = "tu_ngay", ColSpan = 1 },
                            new FormLayoutFieldItemDto { ColumnCode = "den_ngay", ColSpan = 1 }
                        ]
                    },
                    new FormLayoutSectionDto
                    {
                        Id = "sec_money",
                        Title = "Giá trị & Công thức VN",
                        Columns = 2,
                        Fields =
                        [
                            new FormLayoutFieldItemDto { ColumnCode = "so_tien", ColSpan = 1 },
                            new FormLayoutFieldItemDto { ColumnCode = "so_ngay_lam", ColSpan = 1 },
                            new FormLayoutFieldItemDto { ColumnCode = "bang_chu", ColSpan = 2 }
                        ]
                    }
                ]
            }
        });

        // Verify FormLayout persisted cleanly in ui_config
        Assert.NotNull(tbl.FormLayout);
        Assert.Equal("wizard", tbl.FormLayout!.Mode);
        Assert.Equal(2, tbl.FormLayout.Sections.Count);

        // Columns including IsIndexed + WORKDAYS + READ_MONEY_VN
        var nameCol = await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "ten_khach",
            ColumnName = "Tên khách hàng",
            DataType = "STRING",
            IsRequired = true,
            IsIndexed = true,
            OrderIndex = 1
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "tu_ngay",
            ColumnName = "Từ ngày",
            DataType = "DATE",
            OrderIndex = 2
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "den_ngay",
            ColumnName = "Đến ngày",
            DataType = "DATE",
            OrderIndex = 3
        });
        var amountCol = await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "so_tien",
            ColumnName = "Số tiền",
            DataType = "CURRENCY",
            IsIndexed = true,
            OrderIndex = 4
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "so_ngay_lam",
            ColumnName = "Số ngày làm việc",
            DataType = "NUMBER",
            Formula = "WORKDAYS(tu_ngay, den_ngay)",
            OrderIndex = 5
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "bang_chu",
            ColumnName = "Bằng chữ",
            DataType = "STRING",
            Formula = "READ_MONEY_VN(so_tien)",
            OrderIndex = 6
        });

        Assert.True(nameCol.IsIndexed);
        Assert.True(amountCol.IsIndexed);

        // Verify PostgreSQL functional expression indexes exist in pg_indexes
        var nameIdx = MetadataService.BuildColumnExpressionIndexName(tbl.Id, "ten_khach");
        var amountIdx = MetadataService.BuildColumnExpressionIndexName(tbl.Id, "so_tien");
        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var idxCmd = new NpgsqlCommand(
                "SELECT indexname FROM pg_indexes WHERE tablename = 'app_records' AND indexname = ANY(@names)", conn);
            idxCmd.Parameters.AddWithValue("names", new[] { nameIdx, amountIdx });
            var found = new List<string>();
            await using var r = await idxCmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) found.Add(r.GetString(0));
            Assert.Contains(nameIdx, found);
            Assert.Contains(amountIdx, found);
        }

        // Create a record with Vietnamese accents & check WORKDAYS (Mon 2026-11-16..Sun 2026-11-22 => 5 workdays) + READ_MONEY_VN
        using var createRes = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId = app.Id,
            moduleId = mod.Id,
            tableId = tbl.Id,
            data = new Dictionary<string, object?>
            {
                ["ten_khach"] = "Công ty Cổ phần Đổi mới Sáng tạo Nguyễn Văn Đức",
                ["tu_ngay"] = "2026-11-16",
                ["den_ngay"] = "2026-11-22",
                ["so_tien"] = 1550000
            }
        });
        createRes.EnsureSuccessStatusCode();
        using (var cJson = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()))
        {
            var data = cJson.RootElement.GetProperty("data").GetProperty("data");
            Assert.Equal(5, data.GetProperty("so_ngay_lam").GetInt32());
            Assert.Equal("Một triệu năm trăm năm mươi nghìn đồng chẵn", data.GetProperty("bang_chu").GetString());
        }

        // Verify Vietnamese accent-insensitive search in both table query and global search
        using var queryRes = await _fx.Client.GetAsync($"/api/records?tableId={Uri.EscapeDataString(tbl.Id)}&search={Uri.EscapeDataString("nguyen van duc")}");
        queryRes.EnsureSuccessStatusCode();
        using (var qJson = JsonDocument.Parse(await queryRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, qJson.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        }

        using var gsRes = await _fx.Client.GetAsync($"/api/records/global-search?q={Uri.EscapeDataString("doi moi sang tao")}");
        gsRes.EnsureSuccessStatusCode();
        using (var gsJson = JsonDocument.Parse(await gsRes.Content.ReadAsStringAsync()))
        {
            Assert.True(gsJson.RootElement.GetProperty("data").GetProperty("records").GetArrayLength() >= 1);
        }
    }

    [PostgresFact]
    public async Task WorkflowStepBranching_AndAutomationChainSteps_ExecuteAccurately()
    {
        await AuthenticateAsync("admin");
        var metadata = _fx.Services.GetRequiredService<MetadataService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();

        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = $"P2WFA{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Name = "Workflow Branch & Chain App",
            Department = "OPS"
        });
        var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest { AppId = app.Id, Code = "M1", Name = "Main" });
        var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"p2_wf_{Guid.NewGuid().ToString("N")[..5]}",
            DisplayName = "Đề nghị chi nhánh",
            ApprovalEnabled = true
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "tieu_de", ColumnName = "Tiêu đề", DataType = "STRING", IsRequired = true, OrderIndex = 1 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "gia_tri", ColumnName = "Giá trị", DataType = "NUMBER", OrderIndex = 2 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "buoc1_ok", ColumnName = "Bước 1", DataType = "STRING", OrderIndex = 3 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "buoc2_ok", ColumnName = "Bước 2", DataType = "STRING", OrderIndex = 4 });

        // 2-step workflow where Step 2 only applies when gia_tri > 20000000
        await workflows.UpsertWorkflowAsync(new UpsertWorkflowRequest
        {
            Name = "Quy trình rẽ nhánh theo hạn mức",
            AppId = app.Id,
            TableId = tbl.Id,
            IsActive = true,
            Steps =
            [
                new WorkflowStepDto { StepOrder = 1, Name = "Trưởng bộ phận", ApproverRoles = ["ADMIN"], UseOrgManager = true },
                new WorkflowStepDto
                {
                    StepOrder = 2,
                    Name = "Ban Giám đốc (>20tr)",
                    ApproverRoles = ["ADMIN"],
                    ConditionField = "gia_tri",
                    ConditionOperator = "GREATER_THAN",
                    ConditionValue = "20000000"
                }
            ]
        });

        // Multi-step automation chain on RECORD_CREATED: primary UPDATE_SELF sets buoc1_ok, chain step sets buoc2_ok
        await automations.UpsertAutomationAsync(new UpsertAutomationRequest
        {
            Name = "Chuỗi tự động 2 bước",
            SourceAppId = app.Id,
            SourceTableId = tbl.Id,
            TriggerEvent = "RECORD_CREATED",
            ActionType = "UPDATE_SELF",
            FieldMappings = new Dictionary<string, string> { ["buoc1_ok"] = "DONE_1" },
            ChainSteps =
            [
                new AutomationStepDto
                {
                    Id = "chain_1",
                    ActionType = "UPDATE_SELF",
                    FieldMappings = new Dictionary<string, string> { ["buoc2_ok"] = "DONE_2" }
                }
            ],
            IsActive = true
        });

        // Create low-value record (5,000,000 <= 20,000,000) -> automation chain sets both buoc1_ok & buoc2_ok
        using var createRes = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId = app.Id,
            moduleId = mod.Id,
            tableId = tbl.Id,
            data = new Dictionary<string, object?> { ["tieu_de"] = "Mua văn phòng phẩm", ["gia_tri"] = 5000000 }
        });
        createRes.EnsureSuccessStatusCode();
        using var cJson = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var recId = cJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        using (var getAfterCreate = await _fx.Client.GetAsync($"/api/records/{recId}"))
        {
            getAfterCreate.EnsureSuccessStatusCode();
            using var gJson = JsonDocument.Parse(await getAfterCreate.Content.ReadAsStringAsync());
            var d = gJson.RootElement.GetProperty("data").GetProperty("data");
            Assert.Equal("DONE_1", d.GetProperty("buoc1_ok").GetString());
            Assert.Equal("DONE_2", d.GetProperty("buoc2_ok").GetString());
        }

        // Submit & approve Step 1 -> Step 2 (>20M) should be AUTO_SKIPPED and record immediately APPROVED
        using (var sub = await _fx.Client.PostAsync($"/api/records/{recId}/submit-approval", null))
        {
            sub.EnsureSuccessStatusCode();
        }
        using var approveRes = await _fx.Client.PostAsJsonAsync($"/api/records/{recId}/approve", new { approve = true, reason = "OK" });
        approveRes.EnsureSuccessStatusCode();
        using var aJson = JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync());
        var recEl = aJson.RootElement.GetProperty("data");
        Assert.Equal("APPROVED", recEl.GetProperty("approvalStatus").GetString());
        var actions = recEl.GetProperty("workflowHistory").EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToList();
        Assert.Contains("AUTO_SKIPPED", actions);
    }
}
