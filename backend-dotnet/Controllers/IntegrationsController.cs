using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
public class IntegrationsController : ControllerBase
{
    private readonly IntegrationService _integrations;
    private readonly VietnamIntegrationService _vietnam;
    private readonly AuthService _auth;
    private readonly MetadataService _metadata;
    private readonly RecordService _records;

    public IntegrationsController(
        IntegrationService integrations,
        VietnamIntegrationService vietnam,
        AuthService auth,
        MetadataService metadata,
        RecordService records)
    {
        _integrations = integrations;
        _vietnam = vietnam;
        _auth = auth;
        _metadata = metadata;
        _records = records;
    }

    private async Task<(SysUserDto? User, IActionResult? Err)> RequireIntegrationAdminAsync()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return (null, Unauthorized(new { success = false }));
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "integration.manage"))
            return (null, StatusCode(403, new { success = false, message = "Không có quyền quản lý tích hợp/plugin." }));
        return (user, null);
    }

    // ─── API Keys ──────────────────────────────────────────────────────────────

    [HttpGet("api/integrations/keys")]
    public async Task<IActionResult> ListKeys([FromQuery] string? appId)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var keys = await _integrations.ListApiKeysAsync(appId);
        return Ok(new { success = true, data = keys });
    }

    [HttpPost("api/integrations/keys")]
    public async Task<IActionResult> CreateKey([FromBody] CreateApiKeyRequest req)
    {
        var (user, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.AppId))
            return BadRequest(new { success = false, message = "Tên khóa và App là bắt buộc." });

        var (info, rawKey) = await _integrations.CreateApiKeyAsync(req, user!.Username);
        return Ok(new { success = true, data = new { key = info, rawKey } });
    }

    [HttpPost("api/integrations/keys/{id}/rotate")]
    public async Task<IActionResult> RotateKey(string id)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var rotated = await _integrations.RotateApiKeyAsync(id);
        return rotated != null
            ? Ok(new { success = true, data = new { key = rotated.Value.KeyInfo, rawKey = rotated.Value.RawKey } })
            : NotFound(new { success = false });
    }

    [HttpPatch("api/integrations/keys/{id}/toggle")]
    public async Task<IActionResult> ToggleKey(string id, [FromBody] Dictionary<string, bool> body)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var active = body.TryGetValue("isActive", out var v) && v;
        var ok = await _integrations.ToggleKeyAsync(id, active);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpDelete("api/integrations/keys/{id}")]
    public async Task<IActionResult> DeleteKey(string id)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var ok = await _integrations.DeleteKeyAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    // ─── Plugins & Connectors ──────────────────────────────────────────────────

    [HttpGet("api/integrations/plugins/catalog")]
    public async Task<IActionResult> GetPluginCatalog()
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;
        return Ok(new { success = true, data = IntegrationService.GetPluginCatalog() });
    }

    [HttpGet("api/integrations/plugins")]
    public async Task<IActionResult> ListPlugins([FromQuery] string? appId = null)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var plugins = await _integrations.ListPluginsAsync(appId);
        return Ok(new { success = true, data = plugins });
    }

    [HttpGet("api/integrations/apps/{appId}/plugins")]
    public async Task<IActionResult> ListAppPlugins(string appId, [FromQuery] bool enabledOnly = false)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var bindings = await _integrations.ListAppPluginBindingsAsync(appId, enabledOnly);
        return Ok(new { success = true, data = bindings });
    }

    [HttpPut("api/integrations/apps/{appId}/plugins/{code}")]
    public async Task<IActionResult> UpsertAppPluginBinding(string appId, string code, [FromBody] UpsertAppPluginBindingRequest req)
    {
        var (user, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var saved = await _integrations.UpsertAppPluginBindingAsync(appId, code, req, user!.Username);
        if (saved == null)
            return NotFound(new { success = false, message = $"Không tìm thấy plugin '{code}' ở cấp hệ thống." });

        return Ok(new { success = true, data = saved });
    }

    [HttpPost("api/integrations/plugins")]
    public async Task<IActionResult> UpsertPlugin([FromBody] UpsertPluginRequest req)
    {
        var (user, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "Mã plugin và tên plugin là bắt buộc." });

        var saved = await _integrations.UpsertPluginAsync(req, user!.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpPatch("api/integrations/plugins/{id}/toggle")]
    public async Task<IActionResult> TogglePlugin(string id, [FromBody] Dictionary<string, bool> body)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var enabled = body.TryGetValue("isEnabled", out var v) && v;
        var ok = await _integrations.TogglePluginAsync(id, enabled);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpPost("api/integrations/plugins/{id}/test")]
    public async Task<IActionResult> TestPlugin(string id)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var (ok, message) = await _integrations.TestPluginConnectionAsync(id);
        return Ok(new { success = ok, message });
    }

    [HttpPost("api/integrations/plugins/{code}/execute")]
    public async Task<IActionResult> ExecutePlugin(string code, [FromBody] Dictionary<string, object?> body)
    {
        var (user, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var op = body.GetValueOrDefault("operation")?.ToString();
        if (string.IsNullOrWhiteSpace(op))
            return BadRequest(new { success = false, message = "Thiếu trường 'operation'." });

        try
        {
            var result = await _vietnam.ExecutePluginAsync(code, op, body, user!.Username);
            return Ok(new { success = result.Success, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("api/integrations/payments/{providerCode}/ipn")]
    public async Task<IActionResult> ReceivePaymentIpn(string providerCode)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();
        var sigHeader = Request.Headers["X-SmartApps-Signature"].FirstOrDefault();

        Dictionary<string, object?> payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(rawBody)
                ? new Dictionary<string, object?>()
                : (JsonSerializer.Deserialize<Dictionary<string, object?>>(rawBody) ?? new());
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "IPN JSON không hợp lệ." });
        }

        var (authorized, updated, message) = await _vietnam.ProcessPaymentIpnAsync(providerCode, payload, rawBody, sigHeader);
        if (!authorized)
            return Unauthorized(new { success = false, message });

        return Ok(new { success = true, updated, message });
    }

    [HttpDelete("api/integrations/plugins/{id}")]
    public async Task<IActionResult> DeletePlugin(string id)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var ok = await _integrations.DeletePluginAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("api/integrations/logs")]
    public async Task<IActionResult> ListLogs([FromQuery] int limit = 100)
    {
        var (_, err) = await RequireIntegrationAdminAsync();
        if (err != null) return err;

        var logs = await _integrations.ListIntegrationLogsAsync(limit);
        return Ok(new { success = true, data = logs });
    }

    [HttpPost("api/integrations/inbound/{pluginCode}/tables/{tableId}")]
    public async Task<IActionResult> InboundPluginWebhook(string pluginCode, string tableId)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();
        var tokenHeader = Request.Headers["X-Plugin-Token"].FirstOrDefault();
        var sigHeader = Request.Headers["X-SmartApps-Signature"].FirstOrDefault();

        var (plugin, authorized) = await _integrations.VerifyPluginInboundAuthAsync(pluginCode, tokenHeader, sigHeader, rawBody);
        if (plugin == null)
            return NotFound(new { success = false, message = "Không tìm thấy plugin." });
        if (!authorized)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "INBOUND_WEBHOOK", plugin.Id, plugin.AppId, tableId, 401, false, "Xác thực plugin thất bại hoặc plugin đang tắt.");
            return Unauthorized(new { success = false, message = "Plugin chưa kích hoạt hoặc sai chữ ký/token." });
        }

        var tbl = (await _metadata.GetTablesAsync(plugin.AppId, null))
            .FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (tbl == null)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "INBOUND_WEBHOOK", plugin.Id, plugin.AppId, tableId, 404, false, "Bảng dữ liệu không tồn tại hoặc ngoài phạm vi App của plugin.");
            return NotFound(new { success = false, message = "Không tìm thấy bảng dữ liệu trong phạm vi plugin." });
        }

        Dictionary<string, object?> payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(rawBody)
                ? new Dictionary<string, object?>()
                : (JsonSerializer.Deserialize<Dictionary<string, object?>>(rawBody) ?? new());
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "Payload JSON không hợp lệ." });
        }

        try
        {
            var (saved, triggered) = await _records.UpsertRecordAsync(new UpsertRecordRequest
            {
                AppId = tbl.AppId,
                ModuleId = tbl.ModuleId,
                TableId = tbl.Id,
                Data = payload
            }, $"plugin:{plugin.Code}", false);

            await _integrations.LogIntegrationAsync("INBOUND", "INBOUND_WEBHOOK", plugin.Id, tbl.AppId, tbl.Id, 200, true, $"Đã tiếp nhận bản ghi {saved.RecordKey ?? saved.Id}");
            return Ok(new { success = true, data = saved, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "INBOUND_WEBHOOK", plugin.Id, tbl.AppId, tbl.Id, 409, false, ex.Message);
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    // ─── External API (X-Api-Key) ──────────────────────────────────────────────

    private async Task<(SysApiKeyDto? Key, SysTableDto? Table, IActionResult? Err)> AuthorizeExternalAsync(string tableId, string requiredScope)
    {
        var rawKey = Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? Request.Headers.Authorization.FirstOrDefault()?.Replace("ApiKey ", "", StringComparison.OrdinalIgnoreCase);
        var key = await _integrations.ValidateRawKeyAsync(rawKey);
        if (key == null)
            return (null, null, Unauthorized(new { success = false, message = "Thiếu, hết hạn hoặc sai X-Api-Key." }));

        if (!key.Scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
            return (null, null, StatusCode(403, new { success = false, message = $"API Key không có quyền '{requiredScope}'." }));

        if (!string.IsNullOrWhiteSpace(key.TableId) && !key.TableId.Equals(tableId, StringComparison.OrdinalIgnoreCase))
            return (null, null, StatusCode(403, new { success = false, message = "API Key bị giới hạn cho bảng khác." }));

        var tbl = (await _metadata.GetTablesAsync(key.AppId, null)).FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (tbl == null)
            return (null, null, StatusCode(403, new { success = false, message = "Bảng không thuộc App được cấp cho API Key này." }));

        return (key, tbl, null);
    }

    [HttpGet("api/external/tables/{tableId}/records")]
    public async Task<IActionResult> ExternalQueryRecords(string tableId, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var (key, tbl, err) = await AuthorizeExternalAsync(tableId, "records:read");
        if (err != null) return err;

        var (items, total) = await _records.QueryRecordsAsync(key!.AppId, tbl!.ModuleId, tbl.Id, null, "ALL", page, limit);
        return Ok(new { success = true, data = new { items, total, page, limit } });
    }

    [HttpGet("api/external/tables/{tableId}/changes")]
    public async Task<IActionResult> ExternalDeltaChanges(string tableId, [FromQuery] DateTime? since = null, [FromQuery] int limit = 100)
    {
        var (key, tbl, err) = await AuthorizeExternalAsync(tableId, "records:read");
        if (err != null) return err;

        var changes = await _integrations.GetDeltaRecordsAsync(tbl!.Id, since, limit);
        await _integrations.LogIntegrationAsync("OUTBOUND", "API_KEY_DELTA", key!.Id, tbl.AppId, tbl.Id, 200, true, $"Delta sync trả {changes.Count} thay đổi");
        return Ok(new { success = true, data = new { changes, serverTime = DateTime.UtcNow } });
    }

    [HttpPost("api/external/tables/{tableId}/records")]
    public async Task<IActionResult> ExternalIngestRecord(string tableId, [FromBody] Dictionary<string, object?> data)
    {
        var (key, tbl, err) = await AuthorizeExternalAsync(tableId, "records:write");
        if (err != null) return err;

        try
        {
            var (saved, triggered) = await _records.UpsertRecordAsync(new UpsertRecordRequest
            {
                AppId = tbl!.AppId,
                ModuleId = tbl.ModuleId,
                TableId = tbl.Id,
                Data = data ?? new Dictionary<string, object?>()
            }, $"api_key:{key!.Name}", false);
            await _integrations.LogIntegrationAsync("INBOUND", "API_KEY", key.Id, tbl.AppId, tbl.Id, 200, true, $"Tạo bản ghi {saved.RecordKey ?? saved.Id}");
            return Ok(new { success = true, data = saved, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "API_KEY", key!.Id, tbl!.AppId, tbl.Id, 409, false, ex.Message);
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpPut("api/external/tables/{tableId}/records/{keyOrId}")]
    public async Task<IActionResult> ExternalUpsertByKeyOrId(string tableId, string keyOrId, [FromBody] Dictionary<string, object?> data)
    {
        var (key, tbl, err) = await AuthorizeExternalAsync(tableId, "records:write");
        if (err != null) return err;

        var existing = await _records.GetRecordByIdAsync(keyOrId);
        if (existing != null && existing.TableId != tbl!.Id) existing = null;

        if (existing == null)
        {
            var (items, _) = await _records.QueryRecordsAsync(tbl!.AppId, tbl.ModuleId, tbl.Id, keyOrId, "ALL", 1, 10);
            existing = items.FirstOrDefault(r => string.Equals(r.RecordKey, keyOrId, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            var mergedData = existing != null
                ? new Dictionary<string, object?>(existing.Data)
                : new Dictionary<string, object?>();
            foreach (var kv in data ?? new Dictionary<string, object?>())
                mergedData[kv.Key] = kv.Value;

            var (saved, triggered) = await _records.UpsertRecordAsync(new UpsertRecordRequest
            {
                Id = existing?.Id,
                AppId = tbl!.AppId,
                ModuleId = tbl.ModuleId,
                TableId = tbl.Id,
                RecordKey = existing?.RecordKey ?? keyOrId,
                Data = mergedData,
                ExpectedVersion = existing?.Version
            }, $"api_key:{key!.Name}", false);

            await _integrations.LogIntegrationAsync("INBOUND", "API_KEY", key.Id, tbl.AppId, tbl.Id, 200, true, $"{(existing == null ? "Tạo" : "Cập nhật")} bản ghi {saved.RecordKey ?? saved.Id}");
            return Ok(new { success = true, data = saved, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }
}
