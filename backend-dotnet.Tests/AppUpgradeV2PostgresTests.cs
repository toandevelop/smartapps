using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class AppUpgradeV2PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public AppUpgradeV2PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task AuthenticateAdminAsync()
    {
        using var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "123456" });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [PostgresFact]
    public async Task CoreApps_AreUpgradedToV2_WithFullEcosystemFeatures()
    {
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();
        var upgrader = _fx.Services.GetRequiredService<AppUpgradeService>();

        var allAutomations = await automations.GetAutomationsAsync();

        foreach (var code in new[] { "HRM", "ASSET", "OPS", "TASKS" })
        {
            var app = await meta.GetAppByCodeOrIdAsync(code);
            Assert.NotNull(app);
            Assert.Equal("2.0.0", app!.Version);
            Assert.Equal("PUBLISHED", app.PublishStatus);
            Assert.True(app.PublishedVersion >= 2, $"{code} should have published_version >= 2");

            var releases = await meta.GetAppReleasesAsync(app.Id);
            Assert.NotEmpty(releases);

            Assert.Contains(allAutomations, a => a.SourceAppId == app.Id);

            var tree = await meta.GetAppRuntimeTreeAsync(app.Id);
            Assert.NotNull(tree);
            Assert.Contains(tree!.Modules.SelectMany(m => m.Tables), t => t.Table.FormRules.Count > 0);
        }

        // OPS purchase table SUB_TABLE must be Typed JSON array
        var opsTree = await meta.GetAppRuntimeTreeAsync("app_ops");
        var muasam = opsTree!.Modules.SelectMany(m => m.Tables).First(t => t.Table.TableName == "ops_mua_sam");
        var subCol = muasam.Columns.First(c => c.ColumnCode == "chi_tiet_hang_muc");
        Assert.StartsWith("[", subCol.DefaultValue?.TrimStart());
        Assert.True(DynamicComputeService.ParseSubTableSchema(subCol.DefaultValue).Count >= 4);

        // HRM leave request must have 2-step workflow & A4 print template
        var hrmWfs = await workflows.GetWorkflowsAsync("app_hrm");
        Assert.NotEmpty(hrmWfs);
        var hrmTree = await meta.GetAppRuntimeTreeAsync("app_hrm");
        var nghiPhep = hrmTree!.Modules.SelectMany(m => m.Tables).First(t => t.Table.TableName == "hrm_nghi_phep");
        Assert.NotEmpty(await meta.GetPrintTemplatesAsync(nghiPhep.Table.Id));

        // Preview must report NeedsUpgrade = false for core apps
        var preview = await upgrader.PreviewUpgradeAsync();
        foreach (var code in new[] { "HRM", "ASSET", "OPS", "TASKS" })
        {
            var cand = preview.First(c => c.AppCode.Equals(code, StringComparison.OrdinalIgnoreCase));
            Assert.False(cand.NeedsUpgrade, $"{code} missing: {string.Join(", ", cand.MissingFeatures)}");
        }
    }

    [PostgresFact]
    public async Task InstallingV2Template_CreatesPublishedV2AppWithRulesWorkflowsPrintsAndViews()
    {
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();
        var integrations = _fx.Services.GetRequiredService<IntegrationService>();

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        foreach (var key in new[] { "PROCURE_VN", "SALES_VN", "HANDOVER_VN" })
        {
            var app = await meta.CreateAppFromTemplateAsync(new CreateAppFromTemplateRequest
            {
                TemplateKey = key,
                CustomCode = $"{key}_{suffix}",
                IncludeSampleData = true
            });

            Assert.Equal("2.0.0", app.Version);
            Assert.Equal("PUBLISHED", app.PublishStatus);
            Assert.True(app.PublishedVersion >= 2);
            Assert.True(app.RecordCount >= 1);

            var tree = await meta.GetAppRuntimeTreeAsync(app.Id);
            Assert.NotNull(tree);
            var primaryTbl = tree!.Modules.SelectMany(m => m.Tables).First();
            Assert.Equal("table", primaryTbl.Table.PrimaryView);
            Assert.NotEmpty(primaryTbl.Table.FormRules);
            Assert.NotEmpty(await meta.GetPrintTemplatesAsync(primaryTbl.Table.Id));
            Assert.NotEmpty(await workflows.GetWorkflowsAsync(app.Id));
            Assert.Contains(await automations.GetAutomationsAsync(), a => a.SourceAppId == app.Id);
            Assert.NotEmpty(await integrations.ListAppPluginBindingsAsync(app.Id));
        }
    }

    [PostgresFact]
    public async Task UpgradeV2Endpoints_NonDestructivelyUpgradeLegacyV1App_AndAreIdempotent()
    {
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // Create legacy 1.0 app with comma-separated SUB_TABLE and user record
        var legacyApp = await meta.UpsertAppAsync(new UpsertAppRequest
        {
            Code = $"LEGACY_{suffix}",
            Name = "Legacy V1 Purchase",
            Version = "1.0.0"
        });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = legacyApp.Id,
            Code = "MAIN",
            Name = "Main"
        });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = legacyApp.Id,
            ModuleId = mod.Id,
            TableName = $"legacy_order_{suffix.ToLowerInvariant()}",
            DisplayName = "Đơn cũ v1",
            ApprovalEnabled = true
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "ma_don",
            ColumnName = "Mã đơn",
            DataType = "STRING",
            IsRequired = true,
            IsIdentityKey = true,
            OrderIndex = 1
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "chi_tiet",
            ColumnName = "Chi tiết",
            DataType = "SUB_TABLE",
            DefaultValue = "ten_hang,so_luong,don_gia,thanh_tien",
            OrderIndex = 2
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "ghi_chu_rieng",
            ColumnName = "Ghi chú tùy biến của User",
            DataType = "STRING",
            OrderIndex = 3
        });

        var (userRec, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = legacyApp.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            Data = new()
            {
                ["ma_don"] = "OLD-001",
                ["ghi_chu_rieng"] = "Dữ liệu quan trọng v1",
                ["chi_tiet"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_hang"] = "Switch 24 Port", ["so_luong"] = 2, ["don_gia"] = 3500000m }
                }
            }
        }, "admin", true);

        await AuthenticateAdminAsync();

        // 1. Preview identifies missing v2 items
        var previewRes = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/apps/upgrade-v2/preview");
        Assert.True(previewRes.GetProperty("success").GetBoolean());
        var targetCand = previewRes.GetProperty("data").EnumerateArray()
            .First(x => x.GetProperty("appId").GetString() == legacyApp.Id);
        Assert.True(targetCand.GetProperty("needsUpgrade").GetBoolean());
        Assert.Equal("1.0.0", targetCand.GetProperty("currentVersion").GetString());

        // 2. Execute upgrade via REST API
        var upRes = await _fx.Client.PostAsJsonAsync($"/api/apps/{legacyApp.Id}/upgrade-v2", new { });
        upRes.EnsureSuccessStatusCode();
        var upJson = await upRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(upJson.GetProperty("success").GetBoolean());
        var upData = upJson.GetProperty("data");
        Assert.Equal("1.0.0", upData.GetProperty("previousVersion").GetString());
        Assert.Equal("2.0.0", upData.GetProperty("newVersion").GetString());
        Assert.Equal(1, upData.GetProperty("columnsUpgraded").GetInt32());
        Assert.Equal(1, upData.GetProperty("printTemplatesAdded").GetInt32());
        Assert.Equal(1, upData.GetProperty("savedViewsAdded").GetInt32());

        // 3. Verify SUB_TABLE converted to Typed JSON, custom column & user record preserved
        var cols = await meta.GetColumnsAsync(tbl.Id);
        var upgradedSub = cols.First(c => c.ColumnCode == "chi_tiet");
        Assert.StartsWith("[", upgradedSub.DefaultValue?.TrimStart());
        var parsedSub = DynamicComputeService.ParseSubTableSchema(upgradedSub.DefaultValue);
        Assert.Equal(4, parsedSub.Count);
        Assert.Contains(parsedSub, s => s.Code == "thanh_tien" && s.Formula == "so_luong * don_gia");
        Assert.Contains(cols, c => c.ColumnCode == "ghi_chu_rieng");

        var fetched = await records.GetRecordByIdAsync(userRec.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Dữ liệu quan trọng v1", fetched!.Data["ghi_chu_rieng"]?.ToString());

        // 4. Idempotent second upgrade run -> zero new changes
        var secondRes = await _fx.Client.PostAsJsonAsync($"/api/apps/{legacyApp.Id}/upgrade-v2", new { });
        secondRes.EnsureSuccessStatusCode();
        var secondData = (await secondRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(0, secondData.GetProperty("columnsUpgraded").GetInt32());
        Assert.Equal(0, secondData.GetProperty("printTemplatesAdded").GetInt32());
        Assert.Equal(0, secondData.GetProperty("savedViewsAdded").GetInt32());
        Assert.Equal("2.0.0", secondData.GetProperty("newVersion").GetString());
    }
}
