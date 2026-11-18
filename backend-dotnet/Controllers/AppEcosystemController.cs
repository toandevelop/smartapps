using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
public class AppEcosystemController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly MetadataService _metadata;
    private readonly RecordService _records;
    private readonly IntegrationService _integrations;

    public AppEcosystemController(
        AuthService auth,
        MetadataService metadata,
        RecordService records,
        IntegrationService integrations)
    {
        _auth = auth;
        _metadata = metadata;
        _records = records;
        _integrations = integrations;
    }

    private async Task<(SysUserDto? User, SysApiKeyDto? ApiKey, SysAppDto? App, IActionResult? Err)> ResolveAppAuthAsync(string appCode)
    {
        var app = await _metadata.GetAppByCodeOrIdAsync(appCode);
        if (app == null)
            return (null, null, null, NotFound(new { success = false, message = $"Không tìm thấy ứng dụng '{appCode}'." }));

        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user != null)
        {
            if (!AuthService.CanAccessApp(user, app.Code))
                return (null, null, null, StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {app.Code}." }));
            return (user, null, app, null);
        }

        var rawKey = Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? Request.Headers.Authorization.FirstOrDefault()?.Replace("ApiKey ", "", StringComparison.OrdinalIgnoreCase);
        var key = await _integrations.ValidateRawKeyAsync(rawKey);
        if (key != null && string.Equals(key.AppId, app.Id, StringComparison.OrdinalIgnoreCase))
            return (null, key, app, null);

        return (null, null, null, Unauthorized(new { success = false, message = "Cần đăng nhập hoặc truyền header X-Api-Key hợp lệ cho ứng dụng." }));
    }

    private async Task<List<(SysTableDto Table, List<SysColumnDto> Columns)>> LoadAppTablesAsync(
        SysAppDto app,
        SysUserDto? user,
        SysApiKeyDto? apiKey)
    {
        var tables = await _metadata.GetTablesAsync(app.Id, null);
        if (apiKey != null && !string.IsNullOrWhiteSpace(apiKey.TableId))
            tables = tables.Where(t => t.Id.Equals(apiKey.TableId, StringComparison.OrdinalIgnoreCase)).ToList();

        var isAdmin = user != null && AuthService.IsAdminUser(user);
        var userRoles = user == null
            ? new List<string>()
            : (user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode });

        var result = new List<(SysTableDto, List<SysColumnDto>)>();
        foreach (var tbl in tables)
        {
            if (user != null && !AuthService.CanAccessTable(user, app.Code, tbl.Id, "view")) continue;
            var cols = await _metadata.GetColumnsAsync(tbl.Id);
            if (user != null && !isAdmin)
            {
                cols = cols.Where(c => !c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase))).ToList();
            }
            result.Add((tbl, cols));
        }
        return result;
    }

    [HttpGet("api/apps/{appCode}/openapi.json")]
    public async Task<IActionResult> GetAppOpenApi(string appCode)
    {
        var (user, apiKey, app, err) = await ResolveAppAuthAsync(appCode);
        if (err != null) return err;

        var tables = await LoadAppTablesAsync(app!, user, apiKey);
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var doc = AppEcosystemBuilder.BuildOpenApiDocument(app!, tables, serverUrl);
        return Ok(doc);
    }

    [HttpPost("api/mcp/{appCode}")]
    public async Task<IActionResult> HandleMcpRpc(string appCode, [FromBody] JsonElement body)
    {
        var rpcId = body.TryGetProperty("id", out var idEl) ? JsonSerializer.Deserialize<object?>(idEl.GetRawText()) : null;
        var method = body.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(method))
            return BadRequest(RpcError(rpcId, -32600, "Invalid JSON-RPC 2.0 request: missing method."));

        var (user, apiKey, app, authErr) = await ResolveAppAuthAsync(appCode);
        if (authErr != null)
            return Unauthorized(RpcError(rpcId, -32001, $"Authentication required for app '{appCode}'."));

        if (method == "initialize")
        {
            return Ok(RpcOk(rpcId, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { listChanged = false } },
                serverInfo = new { name = $"smartapps-{app!.Code.ToLowerInvariant()}", version = "1.0.0" }
            }));
        }

        if (method == "notifications/initialized")
            return Ok(RpcOk(rpcId, new { }));

        var tables = await LoadAppTablesAsync(app!, user, apiKey);

        if (method == "tools/list")
        {
            return Ok(RpcOk(rpcId, new { tools = AppEcosystemBuilder.BuildMcpTools(app!, tables) }));
        }

        if (method != "tools/call")
            return Ok(RpcError(rpcId, -32601, $"Method '{method}' not supported."));

        var @params = body.TryGetProperty("params", out var pEl) && pEl.ValueKind == JsonValueKind.Object ? pEl : default;
        var toolName = @params.ValueKind == JsonValueKind.Object && @params.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
        var args = @params.ValueKind == JsonValueKind.Object && @params.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.Object
            ? aEl
            : default;

        if (string.IsNullOrWhiteSpace(toolName))
            return Ok(RpcError(rpcId, -32602, "Missing params.name for tools/call."));

        try
        {
            var payload = await ExecuteMcpToolAsync(app!, user, apiKey, tables, toolName, args);
            return Ok(RpcOk(rpcId, new
            {
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload) } }
            }));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(RpcError(rpcId, -32003, ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(RpcError(rpcId, -32000, ex.Message));
        }
    }

    private async Task<object> ExecuteMcpToolAsync(
        SysAppDto app,
        SysUserDto? user,
        SysApiKeyDto? apiKey,
        List<(SysTableDto Table, List<SysColumnDto> Columns)> tables,
        string toolName,
        JsonElement args)
    {
        static string? GetStr(JsonElement el, string prop) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        switch (toolName)
        {
            case "list_records":
            {
                var tableId = GetStr(args, "tableId") ?? throw new InvalidOperationException("tableId là bắt buộc.");
                var pair = tables.FirstOrDefault(t => t.Table.Id.Equals(tableId, StringComparison.OrdinalIgnoreCase));
                if (pair.Table == null) throw new UnauthorizedAccessException("Bảng không tồn tại hoặc không thuộc quyền truy cập.");
                if (apiKey != null && !apiKey.Scopes.Contains("records:read", StringComparer.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("API Key thiếu scope records:read.");

                var search = GetStr(args, "search");
                var status = GetStr(args, "status") ?? "ALL";
                var limit = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("limit", out var lEl) && lEl.TryGetInt32(out var lv)
                    ? Math.Clamp(lv, 1, 100) : 20;

                var (ownerFilter, ownerEmail, ownerEmpCode, ownerDept) = ResolveMcpOwnerScope(user, app.Code, pair.Table);
                var (items, total) = await _records.QueryRecordsAsync(
                    app.Id, pair.Table.ModuleId, pair.Table.Id, search, status, 1, limit,
                    ownerFilter, null, "updated_at", "DESC", ownerEmail, ownerEmpCode, ownerDept);
                StripHiddenForUser(items, pair.Columns, user);
                return new { tableId = pair.Table.Id, total, items };
            }

            case "get_record":
            {
                var recId = GetStr(args, "recordId") ?? throw new InvalidOperationException("recordId là bắt buộc.");
                var rec = await _records.GetRecordByIdAsync(recId) ?? throw new InvalidOperationException("Không tìm thấy bản ghi.");
                if (!rec.AppId.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Bản ghi không thuộc ứng dụng này.");
                var pair = tables.FirstOrDefault(t => t.Table.Id.Equals(rec.TableId, StringComparison.OrdinalIgnoreCase));
                if (pair.Table == null) throw new UnauthorizedAccessException("Không có quyền xem bảng chứa bản ghi này.");
                if (apiKey != null && !apiKey.Scopes.Contains("records:read", StringComparer.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("API Key thiếu scope records:read.");
                if (user != null)
                {
                    var rowScope = AuthService.ResolveRowScope(user, app.Code, pair.Table.Id, pair.Table.RowScope, false);
                    if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(rec.Id, user, rowScope))
                        throw new UnauthorizedAccessException("Bản ghi nằm ngoài phạm vi row scope của bạn.");
                }
                StripHiddenForUser(new[] { rec }, pair.Columns, user);
                return rec;
            }

            case "upsert_record":
            {
                var tableId = GetStr(args, "tableId") ?? throw new InvalidOperationException("tableId là bắt buộc.");
                var recordId = GetStr(args, "recordId");
                var pair = tables.FirstOrDefault(t => t.Table.Id.Equals(tableId, StringComparison.OrdinalIgnoreCase));
                if (pair.Table == null) throw new UnauthorizedAccessException("Bảng không hợp lệ.");
                if (apiKey != null && !apiKey.Scopes.Contains("records:write", StringComparer.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("API Key thiếu scope records:write.");
                if (user != null && !AuthService.CanAccessTable(user, app.Code, pair.Table.Id, string.IsNullOrWhiteSpace(recordId) ? "create" : "edit"))
                    throw new UnauthorizedAccessException("Không có quyền ghi dữ liệu bảng này.");

                var dataDict = new Dictionary<string, object?>();
                int? expectedVer = null;
                if (!string.IsNullOrWhiteSpace(recordId))
                {
                    var existing = await _records.GetRecordByIdAsync(recordId);
                    if (existing != null && existing.AppId.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (user != null)
                        {
                            var rowScope = AuthService.ResolveRowScope(user, app.Code, pair.Table.Id, pair.Table.RowScope, false);
                            if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(existing.Id, user, rowScope))
                                throw new UnauthorizedAccessException("Bản ghi nằm ngoài phạm vi truy cập của bạn.");
                        }
                        foreach (var kv in existing.Data) dataDict[kv.Key] = kv.Value;
                        expectedVer = existing.Version;
                    }
                }
                var writableCols = pair.Columns.ToDictionary(c => c.ColumnCode, StringComparer.OrdinalIgnoreCase);
                var userRoles = user == null ? new List<string>() : (user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode });
                var isAdmin = user != null && AuthService.IsAdminUser(user);
                if (args.TryGetProperty("data", out var dEl) && dEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in dEl.EnumerateObject())
                    {
                        if (writableCols.TryGetValue(p.Name, out var colDef) && !isAdmin && user != null
                            && colDef.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase)))
                            continue;
                        dataDict[p.Name] = p.Value.ValueKind switch
                        {
                            JsonValueKind.String => p.Value.GetString(),
                            JsonValueKind.Number => p.Value.TryGetDecimal(out var dec) ? dec : p.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => p.Value.GetRawText()
                        };
                    }
                }
                var actor = user?.Username ?? $"mcp_apikey:{apiKey!.Name}";
                var (saved, _) = await _records.UpsertRecordAsync(new UpsertRecordRequest
                {
                    Id = recordId,
                    AppId = app.Id,
                    ModuleId = pair.Table.ModuleId,
                    TableId = pair.Table.Id,
                    ExpectedVersion = expectedVer,
                    Data = dataDict
                }, actor, isAdmin);
                return saved;
            }

            case "submit_or_approve_record":
            {
                if (user == null)
                    throw new UnauthorizedAccessException("Hành động phê duyệt qua MCP yêu cầu xác thực bằng tài khoản người dùng.");
                var recId = GetStr(args, "recordId") ?? throw new InvalidOperationException("recordId là bắt buộc.");
                var action = (GetStr(args, "action") ?? "submit").Trim().ToLowerInvariant();
                var reason = GetStr(args, "reason");
                if (action == "submit")
                {
                    var updated = await _records.SubmitApprovalAsync(recId, user.Username);
                    return updated ?? throw new InvalidOperationException("Không tìm thấy hồ sơ.");
                }
                var approveFlag = action == "approve";
                var wfAction = action == "return" ? "RETURN" : null;
                var (resRec, err) = await _records.ProcessApprovalAsync(recId, user, approveFlag, reason, null, null, wfAction);
                if (err != null) throw new UnauthorizedAccessException(err);
                return resRec ?? throw new InvalidOperationException("Không tìm thấy hồ sơ.");
            }

            default:
                throw new InvalidOperationException($"Tool '{toolName}' không tồn tại.");
        }
    }

    private static (string? OwnerFilter, string? OwnerEmail, string? OwnerEmpCode, string? OwnerDept) ResolveMcpOwnerScope(
        SysUserDto? user,
        string appCode,
        SysTableDto tbl)
    {
        if (user == null) return (null, null, null, null);
        var rowScope = AuthService.ResolveRowScope(user, appCode, tbl.Id, tbl.RowScope, false);
        if (rowScope == "OWNER_ONLY") return (user.Username, user.Email, user.EmployeeCode, null);
        if (rowScope == "DEPARTMENT")
        {
            var dept = string.IsNullOrWhiteSpace(user.Department)
                ? (string.IsNullOrWhiteSpace(user.OrgUnitId) ? "__UNASSIGNED__" : user.OrgUnitId)
                : user.Department;
            return (user.Username, user.Email, user.EmployeeCode, dept);
        }
        return (null, null, null, null);
    }

    private static void StripHiddenForUser(IEnumerable<AppRecordDto> items, List<SysColumnDto> visibleCols, SysUserDto? user)
    {
        if (user == null || AuthService.IsAdminUser(user)) return;
        var allowed = visibleCols.Select(c => c.ColumnCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var k in item.Data.Keys.Where(k => !allowed.Contains(k)).ToList())
                item.Data.Remove(k);
        }
    }

    private static object RpcOk(object? id, object result) => new { jsonrpc = "2.0", id, result };
    private static object RpcError(object? id, int code, string message) => new { jsonrpc = "2.0", id, error = new { code, message } };
}
