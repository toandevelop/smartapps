using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class UiUxV3PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public UiUxV3PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task AuthenticateAsync(string username, string password = "123456")
    {
        using var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [PostgresFact]
    public async Task SettingsV3_PersistsBrandColorDensityModeAndPinnedApps()
    {
        await AuthenticateAsync("admin");

        // 1. Update system brandColor (and verify invalid color falls back cleanly)
        using var sysPut = await _fx.Client.PutAsJsonAsync("/api/settings/system", new
        {
            companyName = "SmartApps V3 Co",
            workspaceTitle = "SmartApps V3 Workspace",
            brandColor = "#00897B",
            sessionTimeoutDays = 7
        });
        sysPut.EnsureSuccessStatusCode();
        using (var sysJson = JsonDocument.Parse(await sysPut.Content.ReadAsStringAsync()))
        {
            Assert.Equal("#00897b", sysJson.RootElement.GetProperty("data").GetProperty("brandColor").GetString());
        }

        // 2. Update user densityMode + pinnedApps + accentColor="brand"
        using var mePut = await _fx.Client.PutAsJsonAsync("/api/settings/me", new
        {
            themeMode = "dark",
            accentColor = "brand",
            compactMode = false,
            densityMode = "spacious",
            pinnedApps = new[] { "hrm", "OPS", "HRM", "  asset  " },
            language = "vi"
        });
        mePut.EnsureSuccessStatusCode();
        using (var meJson = JsonDocument.Parse(await mePut.Content.ReadAsStringAsync()))
        {
            var data = meJson.RootElement.GetProperty("data");
            Assert.Equal("brand", data.GetProperty("accentColor").GetString());
            Assert.Equal("spacious", data.GetProperty("densityMode").GetString());
            Assert.False(data.GetProperty("compactMode").GetBoolean());
            var pinned = data.GetProperty("pinnedApps").EnumerateArray().Select(x => x.GetString()!).ToList();
            Assert.Equal(new[] { "HRM", "OPS", "ASSET" }, pinned);
        }

        // 3. Reload /api/settings/me to verify DB persistence across requests
        using var meGet = await _fx.Client.GetAsync("/api/settings/me");
        meGet.EnsureSuccessStatusCode();
        using (var reloadJson = JsonDocument.Parse(await meGet.Content.ReadAsStringAsync()))
        {
            var s = reloadJson.RootElement.GetProperty("data").GetProperty("settings");
            Assert.Equal("spacious", s.GetProperty("densityMode").GetString());
            var pinned = s.GetProperty("pinnedApps").EnumerateArray().Select(x => x.GetString()!).ToList();
            Assert.Equal(new[] { "HRM", "OPS", "ASSET" }, pinned);
        }

        // 4. Setting densityMode = "compact" syncs compactMode = true
        using var compactPut = await _fx.Client.PutAsJsonAsync("/api/settings/me", new
        {
            themeMode = "light",
            accentColor = "#1a73e8",
            compactMode = false,
            densityMode = "compact",
            language = "vi"
        });
        compactPut.EnsureSuccessStatusCode();
        using (var compactJson = JsonDocument.Parse(await compactPut.Content.ReadAsStringAsync()))
        {
            var data = compactJson.RootElement.GetProperty("data");
            Assert.Equal("compact", data.GetProperty("densityMode").GetString());
            Assert.True(data.GetProperty("compactMode").GetBoolean());
            // Omitting pinnedApps preserves existing pins
            var pinned = data.GetProperty("pinnedApps").EnumerateArray().Select(x => x.GetString()!).ToList();
            Assert.Equal(new[] { "HRM", "OPS", "ASSET" }, pinned);
        }
    }

    [PostgresFact]
    public async Task BatchPatchAndBatchApprove_RespectBusinessRulesAndReturnPerRowResults()
    {
        await AuthenticateAsync("admin");
        var metadata = _fx.Services.GetRequiredService<MetadataService>();

        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = $"V3BULK{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Name = "Batch Operations Verification App",
            Department = "IT"
        });
        var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "MAIN",
            Name = "Main Module"
        });
        var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"v3_bulk_{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Phiếu kiểm thử hàng loạt",
            ApprovalEnabled = true
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "tieu_de",
            ColumnName = "Tiêu đề",
            DataType = "STRING",
            IsRequired = true,
            OrderIndex = 1
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "ghi_chu",
            ColumnName = "Ghi chú",
            DataType = "STRING",
            OrderIndex = 2
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "so_luong",
            ColumnName = "Số lượng",
            DataType = "NUMBER",
            OrderIndex = 3
        });
        await metadata.UpsertColumnAsync(new UpsertColumnRequest
        {
            TableId = tbl.Id,
            ColumnCode = "thanh_tien",
            ColumnName = "Thành tiền",
            DataType = "NUMBER",
            Formula = "so_luong * 1000",
            OrderIndex = 4
        });

        // Create 2 records
        var r1Id = await CreateRecordAsync(app.Id, mod.Id, tbl.Id, "Phiếu 1", 2);
        var r2Id = await CreateRecordAsync(app.Id, mod.Id, tbl.Id, "Phiếu 2", 5);

        // 1. Batch-patch editable column (so_luong -> 10) recalculates formula thanh_tien -> 10000
        using var patchRes = await _fx.Client.PostAsJsonAsync("/api/records/batch-patch", new
        {
            tableId = tbl.Id,
            recordIds = new[] { r1Id, r2Id, "rec_nonexistent_id" },
            columnCode = "so_luong",
            value = 10
        });
        patchRes.EnsureSuccessStatusCode();
        using (var pJson = JsonDocument.Parse(await patchRes.Content.ReadAsStringAsync()))
        {
            var data = pJson.RootElement.GetProperty("data");
            Assert.Equal(3, data.GetProperty("requested").GetInt32());
            Assert.Equal(2, data.GetProperty("succeeded").GetInt32());
            Assert.Equal(1, data.GetProperty("failed").GetInt32());
        }

        // Verify formula recalculated on r1
        using (var getR1 = await _fx.Client.GetAsync($"/api/records/{r1Id}"))
        {
            getR1.EnsureSuccessStatusCode();
            using var r1Json = JsonDocument.Parse(await getR1.Content.ReadAsStringAsync());
            Assert.Equal(10000, r1Json.RootElement.GetProperty("data").GetProperty("data").GetProperty("thanh_tien").GetDouble());
        }

        // 2. Reject batch-patch on formula column
        using var formulaPatch = await _fx.Client.PostAsJsonAsync("/api/records/batch-patch", new
        {
            tableId = tbl.Id,
            recordIds = new[] { r1Id },
            columnCode = "thanh_tien",
            value = 999
        });
        Assert.Equal(HttpStatusCode.BadRequest, formulaPatch.StatusCode);

        // 3. Submit r1 for approval, leave r2 as DRAFT -> batch-approve should approve r1 and report error for r2
        using (var sub = await _fx.Client.PostAsync($"/api/records/{r1Id}/submit-approval", null))
        {
            sub.EnsureSuccessStatusCode();
        }

        using var batchApprove = await _fx.Client.PostAsJsonAsync("/api/records/batch-approve", new
        {
            recordIds = new[] { r1Id, r2Id },
            approve = true,
            reason = "Duyệt hàng loạt Đợt 1"
        });
        batchApprove.EnsureSuccessStatusCode();
        using (var aJson = JsonDocument.Parse(await batchApprove.Content.ReadAsStringAsync()))
        {
            var data = aJson.RootElement.GetProperty("data");
            Assert.Equal(2, data.GetProperty("requested").GetInt32());
            Assert.Equal(1, data.GetProperty("succeeded").GetInt32());
            Assert.Equal(1, data.GetProperty("failed").GetInt32());
            var okIds = data.GetProperty("succeededIds").EnumerateArray().Select(x => x.GetString()!).ToList();
            Assert.Equal(new[] { r1Id }, okIds);
        }

        // Verify r1 is now APPROVED
        using (var getR1After = await _fx.Client.GetAsync($"/api/records/{r1Id}"))
        {
            getR1After.EnsureSuccessStatusCode();
            using var r1Json = JsonDocument.Parse(await getR1After.Content.ReadAsStringAsync());
            Assert.Equal("APPROVED", r1Json.RootElement.GetProperty("data").GetProperty("approvalStatus").GetString());
        }
    }

    private async Task<string> CreateRecordAsync(string appId, string moduleId, string tableId, string title, int qty)
    {
        using var res = await _fx.Client.PostAsJsonAsync("/api/records", new
        {
            appId,
            moduleId,
            tableId,
            data = new Dictionary<string, object?>
            {
                ["tieu_de"] = title,
                ["so_luong"] = qty,
                ["ghi_chu"] = "Init"
            }
        });
        res.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}
