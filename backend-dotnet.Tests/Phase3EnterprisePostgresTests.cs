using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Phase3EnterprisePostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Phase3EnterprisePostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task AuthenticateAsync(string username, string password = "123456")
    {
        using var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [PostgresFact]
    public async Task AiCopilot_EnforcesRbacFls_SupportsAccentInsensitiveSearch_AndHumanInTheLoopProposal()
    {
        await AuthenticateAsync("admin");
        var metadata = _fx.Services.GetRequiredService<MetadataService>();

        var appCode = $"P3AI{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = appCode,
            Name = "Ứng dụng Kiểm thử AI Copilot Đợt 3",
            Department = "IT"
        });
        var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "MAIN",
            Name = "Nghiệp vụ chính"
        });
        var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"p3_ai_{Guid.NewGuid().ToString("N")[..5]}",
            DisplayName = "Hồ sơ Đề xuất AI"
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "ma_hs",
            ColumnName = "Mã hồ sơ",
            DataType = "STRING",
            IsRequired = true,
            IsIdentityKey = true,
            OrderIndex = 1
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "tieu_de",
            ColumnName = "Tiêu đề hồ sơ",
            DataType = "STRING",
            IsRequired = true,
            OrderIndex = 2
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "bi_mat_luong",
            ColumnName = "Mức thưởng mật",
            DataType = "STRING",
            HiddenForRoles = ["TEST_EXPORT"],
            OrderIndex = 3
        });

        using var saveRes = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId = app.Id,
            moduleId = mod.Id,
            tableId = tbl.Id,
            data = new Dictionary<string, object?>
            {
                ["ma_hs"] = "AI-COPILOT-01",
                ["tieu_de"] = "Đầu tư Máy chủ Trí tuệ Nhân tạo Nội bộ",
                ["bi_mat_luong"] = "SECRET_BONUS_999M"
            }
        });
        saveRes.EnsureSuccessStatusCode();

        // 1. GET /api/ai/copilot/tools returns 5 tools
        var toolsJson = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/ai/copilot/tools");
        Assert.True(toolsJson.GetProperty("success").GetBoolean());
        var toolsArr = toolsJson.GetProperty("data").GetProperty("tools");
        Assert.Equal(5, toolsArr.GetArrayLength());

        // 2. Accent-insensitive search via POST /api/ai/copilot/ask
        using var askSearchRes = await _fx.Client.PostAsJsonAsync("/api/ai/copilot/ask", new
        {
            message = "Tìm hồ sơ dau tu may chu tri tue nhan tao",
            appCode
        });
        askSearchRes.EnsureSuccessStatusCode();
        var askSearchJson = await askSearchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(askSearchJson.GetProperty("success").GetBoolean());
        var citations = askSearchJson.GetProperty("data").GetProperty("citations");
        Assert.Contains(citations.EnumerateArray(), c =>
            c.GetProperty("title").GetString()!.Contains("AI-COPILOT-01", StringComparison.OrdinalIgnoreCase));

        // 3. Human-in-the-Loop draft proposal does NOT insert a record automatically
        var beforeList = await _fx.Client.GetFromJsonAsync<JsonElement>($"/api/records?tableId={tbl.Id}");
        var beforeTotal = beforeList.GetProperty("data").GetProperty("total").GetInt32();
        Assert.Equal(1, beforeTotal);

        using var askDraftRes = await _fx.Client.PostAsJsonAsync("/api/ai/copilot/ask", new
        {
            message = "Tạo hồ sơ Nâng cấp cụm lưu trữ MinIO HA",
            appCode,
            tableId = tbl.Id
        });
        askDraftRes.EnsureSuccessStatusCode();
        var askDraftJson = await askDraftRes.Content.ReadFromJsonAsync<JsonElement>();
        var proposed = askDraftJson.GetProperty("data").GetProperty("proposedAction");
        Assert.Equal(JsonValueKind.Object, proposed.ValueKind);
        Assert.Equal("CREATE_RECORD", proposed.GetProperty("actionType").GetString());
        Assert.Equal(tbl.Id, proposed.GetProperty("tableId").GetString());

        var afterList = await _fx.Client.GetFromJsonAsync<JsonElement>($"/api/records?tableId={tbl.Id}");
        Assert.Equal(1, afterList.GetProperty("data").GetProperty("total").GetInt32());

        // 4. Non-admin user without HiddenForRoles access cannot search by hidden column secret via Copilot
        using var nvAskSecret = await _fx.SendAsAsync("exporter", "/api/ai/copilot/ask", HttpMethod.Post, new
        {
            message = "Tìm SECRET_BONUS_999M"
        });
        nvAskSecret.EnsureSuccessStatusCode();
        var nvAskJson = await nvAskSecret.Content.ReadFromJsonAsync<JsonElement>();
        var nvCitations = nvAskJson.GetProperty("data").GetProperty("citations");
        Assert.DoesNotContain(nvCitations.EnumerateArray(), c =>
            c.GetProperty("title").GetString()!.Contains("AI-COPILOT-01", StringComparison.OrdinalIgnoreCase));
    }

    [PostgresFact]
    public async Task HaCluster_UpsertsHeartbeats_MergesOnlineUsers_LocksBackupScheduler_AndExposesClusterMetrics()
    {
        var realtime = _fx.Services.GetRequiredService<RealtimeEventService>();
        var config = _fx.Services.GetRequiredService<IConfiguration>();

        // 1. Touch local presence & upsert current node heartbeat
        realtime.TouchPresence("admin");
        await realtime.UpsertClusterHeartbeatAsync();

        // 2. Simulate a second peer API instance writing its heartbeat & presence into sys_cluster_nodes
        var peerInstance = new RealtimeEventService(config, NullLogger<RealtimeEventService>.Instance);
        peerInstance.TouchPresence("exporter");
        await peerInstance.UpsertClusterHeartbeatAsync();

        var clusterNodes = await realtime.GetClusterNodesAsync();
        Assert.True(clusterNodes.Count >= 2, $"Expected >= 2 active cluster nodes, got {clusterNodes.Count}");

        var clusterOnline = await realtime.GetClusterOnlineUsernamesAsync();
        Assert.Contains("admin", clusterOnline);
        Assert.Contains("exporter", clusterOnline);

        // 3. Non-admin gets 403 on /api/settings/cluster-status; Admin gets 200 with nodes & metrics
        using var forbiddenRes = await _fx.SendAsAsync("exporter", "/api/settings/cluster-status");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        await AuthenticateAsync("admin");
        var statusJson = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/settings/cluster-status");
        Assert.True(statusJson.GetProperty("success").GetBoolean());
        var statusData = statusJson.GetProperty("data");
        Assert.Equal(realtime.InstanceId, statusData.GetProperty("currentInstanceId").GetString());
        Assert.True(statusData.GetProperty("nodes").GetArrayLength() >= 2);

        // 4. Prometheus /metrics includes smartapps_cluster_nodes_active & smartapps_realtime_listener_connected
        var promText = await _fx.Client.GetStringAsync("/metrics?format=prometheus");
        Assert.Contains("smartapps_cluster_nodes_active", promText);
        Assert.Contains("smartapps_realtime_listener_connected", promText);

        // 5. BackupSchedulerService advisory lock blocks concurrent peer execution when lock is held
        var backupService = _fx.Services.GetRequiredService<BackupService>();
        var scheduler = new BackupSchedulerService(config, backupService, NullLogger<BackupSchedulerService>.Instance);

        await using var holdConn = new NpgsqlConnection(_fx.ConnectionString);
        await holdConn.OpenAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended('smartapps_backup_scheduler_tick', 0))", holdConn))
        {
            var locked = Convert.ToBoolean(await lockCmd.ExecuteScalarAsync());
            Assert.True(locked);
        }

        var ranWhileLocked = await scheduler.RunLockedBackupCheckAsync();
        Assert.False(ranWhileLocked);

        await using (var unlockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(hashtextextended('smartapps_backup_scheduler_tick', 0))", holdConn))
        {
            await unlockCmd.ExecuteNonQueryAsync();
        }

        var ranAfterUnlock = await scheduler.RunLockedBackupCheckAsync();
        Assert.True(ranAfterUnlock);
    }

    [PostgresFact]
    public async Task StoreSuites_AndDmsVanBanPackage_InstallCleanlyAndSkipExistingApps()
    {
        await AuthenticateAsync("admin");

        // 1. Verify DMS_VAN_BAN is in the built-in catalog (>= 21 packages)
        var store = _fx.Services.GetRequiredService<AppStoreService>();
        var packages = store.GetBuiltInPackages();
        Assert.True(packages.Count >= 21, $"Expected >= 21 built-in packages, got {packages.Count}");
        var dmsPkg = Assert.Single(packages, p => p.PackageCode == "DMS_VAN_BAN");
        Assert.Equal(dmsPkg.ChecksumSha256, AppStoreService.ComputeCanonicalChecksum(dmsPkg));

        // 2. GET /api/store/suites returns 4 curated solution suites
        var suitesJson = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/store/suites");
        Assert.True(suitesJson.GetProperty("success").GetBoolean());
        var suitesArr = suitesJson.GetProperty("data");
        Assert.Equal(4, suitesArr.GetArrayLength());
        Assert.Contains(suitesArr.EnumerateArray(), s => s.GetProperty("suiteCode").GetString() == "SUITE_SME_OPS");

        // 3. Install SUITE_SME_OPS (installs DMS_VAN_BAN, FINANCE, CONTRACT, MEETING)
        using var installRes1 = await _fx.Client.PostAsJsonAsync("/api/store/suites/SUITE_SME_OPS/install", new
        {
            includeSampleData = true
        });
        installRes1.EnsureSuccessStatusCode();
        var installJson1 = await installRes1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(installJson1.GetProperty("success").GetBoolean());
        var data1 = installJson1.GetProperty("data");
        Assert.Equal(0, data1.GetProperty("failedCount").GetInt32());
        Assert.True(data1.GetProperty("installedCount").GetInt32() >= 1);

        // 4. Re-installing SUITE_SME_OPS skips all 4 already-installed apps gracefully
        using var installRes2 = await _fx.Client.PostAsJsonAsync("/api/store/suites/SUITE_SME_OPS/install", new
        {
            includeSampleData = true
        });
        installRes2.EnsureSuccessStatusCode();
        var installJson2 = await installRes2.Content.ReadFromJsonAsync<JsonElement>();
        var data2 = installJson2.GetProperty("data");
        Assert.Equal(0, data2.GetProperty("installedCount").GetInt32());
        Assert.Equal(4, data2.GetProperty("skippedCount").GetInt32());
        Assert.Equal(0, data2.GetProperty("failedCount").GetInt32());
    }

    [PostgresFact]
    public async Task MondayWorkOsDataTypes_ValidateNormalizeAndSupportFormRulesEndToEnd()
    {
        await AuthenticateAsync("admin");
        var metadata = _fx.Services.GetRequiredService<MetadataService>();

        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = $"MND{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Name = "Monday Work OS Board Test",
            Department = "PMO"
        });
        var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "BOARD",
            Name = "Work OS Board"
        });
        var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"monday_board_{Guid.NewGuid().ToString("N")[..5]}",
            DisplayName = "Bảng Công việc Monday"
        });

        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "task_code", ColumnName = "Mã việc", DataType = "STRING", IsRequired = true, IsIdentityKey = true, OrderIndex = 1 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "work_status", ColumnName = "Trạng thái màu", DataType = "STATUS", DefaultValue = "WORKING", OrderIndex = 2 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "priority", ColumnName = "Ưu tiên", DataType = "PRIORITY", DefaultValue = "HIGH", OrderIndex = 3 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "progress_pct", ColumnName = "Tiến độ %", DataType = "PROGRESS", IsIndexed = true, DefaultValue = "50", OrderIndex = 4 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "rating_stars", ColumnName = "Đánh giá sao", DataType = "RATING", IsIndexed = true, DefaultValue = "4", OrderIndex = 5 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "tags", ColumnName = "Hashtags", DataType = "TAGS", OrderIndex = 6 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "color_hex", ColumnName = "Mã màu", DataType = "COLOR", DefaultValue = "#00c875", OrderIndex = 7 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "doc_link", ColumnName = "Liên kết tài liệu", DataType = "LINK", OrderIndex = 8 });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "bonus_note", ColumnName = "Ghi chú thưởng xuất sắc", DataType = "STRING", OrderIndex = 9 });

        // Configure a FormRule using numeric GREATER_OR_EQUAL on RATING
        await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            Id = tbl.Id,
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = tbl.TableName,
            DisplayName = tbl.DisplayName,
            FormRules =
            [
                new FormRuleDto
                {
                    Id = "rule_high_rating_note",
                    TargetField = "bonus_note",
                    Property = "required",
                    Value = true,
                    ElseValue = false,
                    Match = "ALL",
                    Conditions =
                    [
                        new FormConditionDto { Field = "rating_stars", Operator = "GREATER_OR_EQUAL", Value = "5" }
                    ]
                }
            ]
        });

        // 1. Valid Monday.com record with rating = 4 saves cleanly
        using var okRes = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId = app.Id,
            moduleId = mod.Id,
            tableId = tbl.Id,
            data = new Dictionary<string, object?>
            {
                ["task_code"] = "MND-001",
                ["work_status"] = "DONE",
                ["priority"] = "CRITICAL",
                ["progress_pct"] = 100,
                ["rating_stars"] = 4,
                ["tags"] = new[] { "workos", "monday" },
                ["color_hex"] = "#00c875",
                ["doc_link"] = "https://monday.com"
            }
        });
        okRes.EnsureSuccessStatusCode();

        // 2. When rating_stars == 5, FormRule requires bonus_note
        using var ruleFailRes = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId = app.Id,
            moduleId = mod.Id,
            tableId = tbl.Id,
            data = new Dictionary<string, object?>
            {
                ["task_code"] = "MND-002",
                ["rating_stars"] = 5,
                ["progress_pct"] = 80
            }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ruleFailRes.StatusCode);
    }
}
