using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class IntegrationsAndPluginsPostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public IntegrationsAndPluginsPostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task<(SysAppDto App, SysModuleDto Mod, SysTableDto Table)> CreateIntegrationTableAsync(string suffix)
    {
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_int_{suffix}", Code = $"INT_{suffix}".ToUpperInvariant(), Name = $"INT {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_int_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_int_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"int_{suffix}", DisplayName = $"INT {suffix}" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "code", ColumnName = "Code", DataType = "STRING", IsIdentityKey = true, IsRequired = true, OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "qty", ColumnName = "Qty", DataType = "NUMBER", OrderIndex = 2 });
        return (app, mod, tbl);
    }

    [PostgresFact]
    public async Task ApiKey_SupportsRotationExpiryExternalUpsertAndDeltaTombstones()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (app, _, tbl) = await CreateIntegrationTableAsync(suffix);
        var integrations = _fx.Services.GetRequiredService<IntegrationService>();
        var records = _fx.Services.GetRequiredService<RecordService>();

        // 1) Create key, verify old key works, rotate, verify old key fails & new key works
        var (keyInfo, rawKey1) = await integrations.CreateApiKeyAsync(new CreateApiKeyRequest
        {
            Name = $"Sync Key {suffix}",
            AppId = app.Id,
            TableId = tbl.Id,
            Scopes = new List<string> { "records:read", "records:write" },
            ExpiresInDays = 30
        }, "admin");
        Assert.NotNull(keyInfo.ExpiresAt);

        using var req1 = new HttpRequestMessage(HttpMethod.Put, $"/api/external/tables/{tbl.Id}/records/EXT-01")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["code"] = "EXT-01", ["qty"] = 10 })
        };
        req1.Headers.Add("X-Api-Key", rawKey1);
        var res1 = await _fx.Client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        var rotated = await integrations.RotateApiKeyAsync(keyInfo.Id);
        Assert.NotNull(rotated);
        var rawKey2 = rotated!.Value.RawKey;
        Assert.NotEqual(rawKey1, rawKey2);

        using var staleReq = new HttpRequestMessage(HttpMethod.Get, $"/api/external/tables/{tbl.Id}/records");
        staleReq.Headers.Add("X-Api-Key", rawKey1);
        var staleRes = await _fx.Client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Unauthorized, staleRes.StatusCode);

        // 2) Upsert existing EXT-01 using new key, then soft-delete and verify delta sync returns tombstone
        var since = DateTime.UtcNow.AddMinutes(-1);
        using var req2 = new HttpRequestMessage(HttpMethod.Put, $"/api/external/tables/{tbl.Id}/records/EXT-01")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["qty"] = 25 })
        };
        req2.Headers.Add("X-Api-Key", rawKey2);
        var res2 = await _fx.Client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var (items, _) = await records.QueryRecordsAsync(app.Id, null, tbl.Id, "EXT-01", "ALL");
        var rec = Assert.Single(items);
        Assert.Equal(25m, Convert.ToDecimal(rec.Data["qty"]));
        Assert.Equal(2, rec.Version);

        await records.DeleteRecordsAsync(new List<string> { rec.Id }, "admin", true);

        using var deltaReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/external/tables/{tbl.Id}/changes?since={Uri.EscapeDataString(since.ToString("O"))}");
        deltaReq.Headers.Add("X-Api-Key", rawKey2);
        var deltaRes = await _fx.Client.SendAsync(deltaReq);
        Assert.Equal(HttpStatusCode.OK, deltaRes.StatusCode);
        var deltaJson = await deltaRes.Content.ReadFromJsonAsync<JsonElement>();
        var changes = deltaJson.GetProperty("data").GetProperty("changes").EnumerateArray().ToList();
        Assert.Contains(changes, c => c.GetProperty("id").GetString() == rec.Id && c.GetProperty("isDeleted").GetBoolean());
    }

    [PostgresFact]
    public async Task Plugin_SupportsRegistryMaskingTestAndHmacInboundWebhook()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (app, _, tbl) = await CreateIntegrationTableAsync(suffix);
        var integrations = _fx.Services.GetRequiredService<IntegrationService>();
        var records = _fx.Services.GetRequiredService<RecordService>();

        var secret = $"whsec_{suffix}";
        var plugin = await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = $"inbound-{suffix}",
            Name = $"Inbound {suffix}",
            Category = "WEBHOOK_INBOUND",
            Description = "Test inbound webhook",
            Config = new Dictionary<string, object?> { ["allowedTables"] = tbl.Id },
            SecretValue = secret,
            AppId = app.Id,
            IsEnabled = true
        }, "admin");

        Assert.True(plugin.HasSecret);
        var (testOk, _) = await integrations.TestPluginConnectionAsync(plugin.Id);
        Assert.True(testOk);

        var bodyJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["code"] = "PLG-01", ["qty"] = 42 });

        // Invalid HMAC -> 401
        using var badReq = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/inbound/{plugin.Code}/tables/{tbl.Id}")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        badReq.Headers.Add("X-SmartApps-Signature", "sha256=deadbeef");
        var badRes = await _fx.Client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);

        // Valid HMAC -> 200 and creates record + integration log
        var sigHex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(bodyJson))).ToLowerInvariant();
        using var goodReq = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/inbound/{plugin.Code}/tables/{tbl.Id}")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        goodReq.Headers.Add("X-SmartApps-Signature", $"sha256={sigHex}");
        var goodRes = await _fx.Client.SendAsync(goodReq);
        Assert.Equal(HttpStatusCode.OK, goodRes.StatusCode);

        var (items, _) = await records.QueryRecordsAsync(app.Id, null, tbl.Id, "PLG-01", "ALL");
        var created = Assert.Single(items);
        Assert.Equal(42m, Convert.ToDecimal(created.Data["qty"]));

        var logs = await integrations.ListIntegrationLogsAsync(20);
        Assert.Contains(logs, l => l.SourceId == plugin.Id && l.SourceType == "INBOUND_WEBHOOK" && l.Success);
    }
}
