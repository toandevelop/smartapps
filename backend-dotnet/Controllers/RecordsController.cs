using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api/records")]
public class RecordsController : ControllerBase
{
    private readonly RecordService _records;
    private readonly MetadataService _metadata;
    private readonly AuthService _auth;
    private readonly AiService _ai;

    public RecordsController(RecordService records, MetadataService metadata, AuthService auth, AiService ai)
    {
        _records = records;
        _metadata = metadata;
        _auth = auth;
        _ai = ai;
    }

    private async Task<string?> ResolveAppCodeAsync(string? appId, string? tableId = null)
    {
        if (!string.IsNullOrWhiteSpace(appId))
        {
            var app = await _metadata.GetAppByCodeOrIdAsync(appId);
            if (app != null) return app.Code;
        }
        if (!string.IsNullOrWhiteSpace(tableId))
        {
            var tables = await _metadata.GetTablesAsync(null, null);
            var tbl = tables.FirstOrDefault(t => t.Id == tableId);
            if (tbl != null)
            {
                var app = await _metadata.GetAppByCodeOrIdAsync(tbl.AppId);
                return app?.Code;
            }
        }
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? appId,
        [FromQuery] string? moduleId,
        [FromQuery] string? tableId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? filters = null,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortDir = "DESC",
        [FromQuery] bool personalMode = false)
        => await QueryCoreAsync(appId, moduleId, tableId, search, status, page, limit,
            filters, sortField, sortDir, personalMode);

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string tableId,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? filters = null,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortDir = "DESC",
        [FromQuery] bool personalMode = false)
    {
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "TableId là bắt buộc." });
        return await QueryCoreAsync(null, null, tableId, search, status, 1, 20,
            filters, sortField, sortDir, personalMode, export: true);
    }

    private async Task<IActionResult> QueryCoreAsync(
        string? appId, string? moduleId, string? tableId, string? search, string? status,
        int page, int limit, string? filters, string? sortField, string? sortDir,
        bool personalMode, bool export = false)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var appCode = await ResolveAppCodeAsync(appId, tableId);
        if (export && string.IsNullOrWhiteSpace(appCode))
            return NotFound(new { success = false, message = "Không tìm thấy bảng dữ liệu." });
        if (!string.IsNullOrWhiteSpace(appCode))
        {
            if (!AuthService.CanAccessApp(user, appCode))
                return StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {appCode}." });
            if (!string.IsNullOrWhiteSpace(tableId))
            {
                if (!AuthService.CanAccessTable(user, appCode, tableId, "view"))
                    return StatusCode(403, new { success = false, message = $"Không có quyền xem bảng dữ liệu này trên App {appCode}." });
                if (export && !AuthService.CanAccessTable(user, appCode, tableId, "export"))
                    return StatusCode(403, new { success = false, message = "Không có quyền xuất dữ liệu bảng này." });
            }
            else if (!AuthService.HasAppPermission(user, appCode, "record.view"))
            {
                return StatusCode(403, new { success = false, message = $"Không có quyền xem dữ liệu trên App {appCode}." });
            }
        }
        else if (!AuthService.HasPermission(user, "record.view"))
        {
            return StatusCode(403, new { success = false, message = "Không có quyền xem dữ liệu." });
        }

        string? ownerFilter = null;
        string? ownerEmail = null;
        string? ownerEmpCode = null;
        string? ownerDepartment = null;
        if (!string.IsNullOrWhiteSpace(tableId))
        {
            var tables = await _metadata.GetTablesAsync(null, null);
            var tbl = tables.FirstOrDefault(t => t.Id == tableId);
            var rowScope = AuthService.ResolveRowScope(user, appCode, tableId, tbl?.RowScope, personalMode);
            if (rowScope == "OWNER_ONLY")
            {
                ownerFilter = user.Username;
                ownerEmail = user.Email;
                ownerEmpCode = user.EmployeeCode;
            }
            else if (rowScope == "DEPARTMENT")
            {
                ownerFilter = user.Username;
                ownerEmail = user.Email;
                ownerEmpCode = user.EmployeeCode;
                ownerDepartment = string.IsNullOrWhiteSpace(user.Department) ? "__UNASSIGNED__" : user.Department;
            }
        }

        var (items, total) = await _records.QueryRecordsAsync(
            appId, moduleId, tableId, search, status, page, limit,
            ownerFilter, filters, sortField, sortDir, ownerEmail, ownerEmpCode, ownerDepartment, exportAll: export);

        if (export)
        {
            var columns = await _metadata.GetColumnsAsync(tableId!);
            var userRoles = user.RoleCodes.Append(user.RoleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var visibleColumns = columns.Where(c => AuthService.IsAdminUser(user)
                || !c.HiddenForRoles.Any(userRoles.Contains)).ToList();
            var bytes = ExcelExportService.CreateWorkbook(visibleColumns, items);
            return File(bytes, ExcelExportService.ContentType, $"records-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
        }

        // Field-Level Security: strip hidden columns from record payloads for all user roles
        if (!string.IsNullOrWhiteSpace(tableId) && !AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            var cols = await _metadata.GetColumnsAsync(tableId);
            var hiddenCodes = cols
                .Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                .Select(c => c.ColumnCode)
                .ToList();
            if (hiddenCodes.Count > 0)
            {
                foreach (var item in items)
                {
                    foreach (var code in hiddenCodes) item.Data.Remove(code);
                }
            }
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, limit)));
        return Ok(new
        {
            success = true,
            data = new { items, total, page, limit, totalPages }
        });
    }

    [HttpGet("views")]
    public async Task<IActionResult> ListSavedViews([FromQuery] string tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "tableId là bắt buộc." });

        var list = await _records.ListSavedViewsAsync(tableId, user.Username);
        return Ok(new { success = true, data = list });
    }

    [HttpPost("views")]
    public async Task<IActionResult> SaveView([FromBody] UpsertSavedViewRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "AppId, TableId và Name là bắt buộc." });

        var saved = await _records.UpsertSavedViewAsync(req, user.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpDelete("views/{id}")]
    public async Task<IActionResult> DeleteView(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var ok = await _records.DeleteSavedViewAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("lookup-options")]
    public async Task<IActionResult> LookupOptions(
        [FromQuery] string tableId,
        [FromQuery] string? valueField,
        [FromQuery] string? displayField,
        [FromQuery] string? q,
        [FromQuery] string? filterField,
        [FromQuery] string? filterValue)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "tableId là bắt buộc." });

        var items = await _records.GetLookupOptionsAsync(tableId, valueField, displayField, q, user, filterField, filterValue);
        return Ok(new { success = true, data = items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var rec = await _records.GetRecordByIdAsync(id);
        if (rec == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        var appCode = await ResolveAppCodeAsync(rec.AppId, rec.TableId);
        if (appCode == null || !AuthService.CanAccessApp(user, appCode) || !AuthService.CanAccessTable(user, appCode, rec.TableId, "view"))
            return StatusCode(403, new { success = false, message = "Không có quyền xem bản ghi này." });

        var tbl = (await _metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, rec.TableId, tbl?.RowScope, false);
        if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
            return StatusCode(403, new { success = false, message = "Bản ghi nằm ngoài phạm vi truy cập của bạn." });

        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            var cols = await _metadata.GetColumnsAsync(rec.TableId);
            foreach (var hCol in cols.Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase))))
            {
                rec.Data.Remove(hCol.ColumnCode);
            }
        }

        return Ok(new { success = true, data = rec });
    }

    [HttpPost("{id}/ai-summary")]
    public async Task<IActionResult> AiSummary(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var rec = await _records.GetRecordByIdAsync(id);
        if (rec == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        var appCode = await ResolveAppCodeAsync(rec.AppId, rec.TableId);
        if (appCode == null || !AuthService.CanAccessApp(user, appCode) || !AuthService.CanAccessTable(user, appCode, rec.TableId, "view"))
            return StatusCode(403, new { success = false, message = "Không có quyền xem bản ghi này." });

        var tbl = (await _metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, rec.TableId, tbl?.RowScope, false);
        if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
            return StatusCode(403, new { success = false });

        var cols = await _metadata.GetColumnsAsync(rec.TableId);
        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            cols = cols.Where(c => !c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase))).ToList();
            foreach (var hCol in (await _metadata.GetColumnsAsync(rec.TableId)).Where(c => !cols.Any(vc => vc.Id == c.Id)))
                rec.Data.Remove(hCol.ColumnCode);
        }

        var (summary, engine) = await _ai.SummarizeRecordAsync(rec, tbl?.DisplayName ?? rec.TableId, cols, user.Username);
        return Ok(new { success = true, data = new { summary, engine } });
    }

    [HttpGet("{id}/relations")]
    public async Task<IActionResult> GetCrossAppRelations(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var groups = await _records.GetCrossAppRelationsAsync(id, user);
        return Ok(new { success = true, data = groups });
    }

    [HttpGet("table-relations")]
    public async Task<IActionResult> GetTableRelations([FromQuery] string tableId, [FromQuery] string? recordId = null, [FromQuery] bool forParentForm = true)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "tableId là bắt buộc." });

        var groups = await _records.GetTableChildRelationsAsync(tableId, recordId, user, forParentForm);
        return Ok(new { success = true, data = groups });
    }

    [HttpGet("{id}/audit")]
    public async Task<IActionResult> GetAuditHistory(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var logs = await _records.GetAuditLogsAsync(id);
        await StripHiddenAuditColumnsAsync(logs, user);
        return Ok(new { success = true, data = logs });
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetTableAnalytics([FromQuery] string tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "tableId là bắt buộc." });

        var stats = await _records.GetTableAnalyticsAsync(tableId);
        return Ok(new { success = true, data = stats });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertRecordRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.ModuleId) || string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "AppId, ModuleId và TableId là bắt buộc." });

        bool isNew = string.IsNullOrWhiteSpace(req.Id);
        var action = isNew ? "create" : "edit";
        var appCode = await ResolveAppCodeAsync(req.AppId, req.TableId);
        if (!string.IsNullOrWhiteSpace(appCode) && !AuthService.CanAccessApp(user, appCode))
            return StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {appCode}." });
        if (!AuthService.CanAccessTable(user, appCode, req.TableId, action))
            return StatusCode(403, new { success = false, message = $"Bạn không có quyền {(isNew ? "tạo mới" : "chỉnh sửa")} dữ liệu trên bảng này." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var tbl = tables.FirstOrDefault(t => t.Id == req.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, req.TableId, tbl?.RowScope, false);
        var serverManagedFields = new HashSet<string>(StringComparer.Ordinal);

        if (!isNew && rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(req.Id!, user, rowScope))
        {
            return StatusCode(403, new { success = false, message = rowScope == "DEPARTMENT" ? "Bạn chỉ được chỉnh sửa bản ghi thuộc phòng ban của mình." : "Bạn chỉ được chỉnh sửa bản ghi của chính mình." });
        }

        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            var cols = await _metadata.GetColumnsAsync(req.TableId);

            if (isNew && rowScope == "OWNER_ONLY" && !string.IsNullOrWhiteSpace(user.EmployeeCode))
            {
                foreach (var c in cols)
                {
                    if (string.Equals(c.RefTableId, "tbl_hrm_nhanvien", StringComparison.OrdinalIgnoreCase)
                        || c.ColumnCode is "ho_ten_nv" or "nhan_vien" or "nguoi_tao" or "nguoi_nhan")
                    {
                        req.Data[c.ColumnCode] = user.EmployeeCode;
                        serverManagedFields.Add(c.ColumnCode);
                    }
                }
            }

            var readOnlyCols = cols
                .Where(c => c.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            if (readOnlyCols.Count > 0)
            {
                if (isNew)
                {
                    foreach (var rc in readOnlyCols)
                    {
                        req.Data.Remove(rc.ColumnCode);
                        if (!string.IsNullOrWhiteSpace(rc.DefaultValue))
                            req.Data[rc.ColumnCode] = rc.DefaultValue;
                    }
                }
                else
                {
                    var existingRec = await _records.GetRecordByIdAsync(req.Id!);
                    if (existingRec != null)
                    {
                        foreach (var rc in readOnlyCols)
                        {
                            if (existingRec.Data.TryGetValue(rc.ColumnCode, out var prevVal))
                                req.Data[rc.ColumnCode] = prevVal;
                            else
                                req.Data.Remove(rc.ColumnCode);
                        }
                    }
                }
            }
        }

        try
        {
            var isAdmin = AuthService.IsAdminUser(user);
            var (record, triggered) = await _records.UpsertRecordAsync(req, user.Username, isAdmin, serverManagedFields);
            return Ok(new { success = true, data = record, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("with-relations")]
    public async Task<IActionResult> UpsertWithRelations([FromBody] UpsertRecordWithRelationsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.ModuleId) || string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "AppId, ModuleId và TableId là bắt buộc." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var isAdmin = AuthService.IsAdminUser(user);
        var (parentErr, parentManaged) = await ValidateAndSanitizeUpsertAsync(req, user, isAdmin, tables);
        if (parentErr != null) return parentErr;

        foreach (var mut in req.ChildMutations)
        {
            if (string.IsNullOrWhiteSpace(mut.AppId) || string.IsNullOrWhiteSpace(mut.ModuleId) || string.IsNullOrWhiteSpace(mut.TableId))
                return BadRequest(new { success = false, message = "Thông tin bảng phụ (AppId, ModuleId, TableId) không hợp lệ." });

            var childAppCode = await ResolveAppCodeAsync(mut.AppId, mut.TableId);
            if (!string.IsNullOrWhiteSpace(childAppCode) && !AuthService.CanAccessApp(user, childAppCode))
                return StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {childAppCode}." });

            var childTbl = tables.FirstOrDefault(t => t.Id == mut.TableId);
            var childScope = AuthService.ResolveRowScope(user, childAppCode, mut.TableId, childTbl?.RowScope, false);

            if (mut.DeleteIds.Count > 0)
            {
                if (!AuthService.CanAccessTable(user, childAppCode, mut.TableId, "delete"))
                    return StatusCode(403, new { success = false, message = $"Bạn không có quyền xóa dữ liệu trên bảng '{childTbl?.DisplayName ?? mut.TableId}'." });
                if (childScope != "ALL")
                {
                    foreach (var delId in mut.DeleteIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!await _records.IsRecordOwnedByUserAsync(delId, user, childScope))
                            return StatusCode(403, new { success = false, message = "Bản ghi phụ cần xóa nằm ngoài phạm vi truy cập của bạn." });
                    }
                }
            }

            foreach (var childReq in mut.Upserts)
            {
                childReq.AppId = mut.AppId;
                childReq.ModuleId = mut.ModuleId;
                childReq.TableId = mut.TableId;
                var (childErr, _) = await ValidateAndSanitizeUpsertAsync(childReq, user, isAdmin, tables);
                if (childErr != null) return childErr;
            }
        }

        try
        {
            var (record, triggered) = await _records.UpsertRecordWithRelationsAsync(req, user.Username, isAdmin, parentManaged);
            return Ok(new { success = true, data = record, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
    }

    private async Task<(IActionResult? Error, HashSet<string> ServerManagedFields)> ValidateAndSanitizeUpsertAsync(
        UpsertRecordRequest req,
        SysUserDto user,
        bool isAdmin,
        List<SysTableDto> tables)
    {
        bool isNew = string.IsNullOrWhiteSpace(req.Id);
        var action = isNew ? "create" : "edit";
        var appCode = await ResolveAppCodeAsync(req.AppId, req.TableId);
        if (!string.IsNullOrWhiteSpace(appCode) && !AuthService.CanAccessApp(user, appCode))
            return (StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {appCode}." }), new());
        if (!AuthService.CanAccessTable(user, appCode, req.TableId, action))
            return (StatusCode(403, new { success = false, message = $"Bạn không có quyền {(isNew ? "tạo mới" : "chỉnh sửa")} dữ liệu trên bảng này." }), new());

        var tbl = tables.FirstOrDefault(t => t.Id == req.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, req.TableId, tbl?.RowScope, false);
        var serverManagedFields = new HashSet<string>(StringComparer.Ordinal);

        if (!isNew && rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(req.Id!, user, rowScope))
        {
            return (StatusCode(403, new { success = false, message = rowScope == "DEPARTMENT" ? "Bạn chỉ được chỉnh sửa bản ghi thuộc phòng ban của mình." : "Bạn chỉ được chỉnh sửa bản ghi của chính mình." }), serverManagedFields);
        }

        if (!isAdmin)
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            var cols = await _metadata.GetColumnsAsync(req.TableId);

            if (isNew && rowScope == "OWNER_ONLY" && !string.IsNullOrWhiteSpace(user.EmployeeCode))
            {
                foreach (var c in cols)
                {
                    if (string.Equals(c.RefTableId, "tbl_hrm_nhanvien", StringComparison.OrdinalIgnoreCase)
                        || c.ColumnCode is "ho_ten_nv" or "nhan_vien" or "nguoi_tao" or "nguoi_nhan")
                    {
                        req.Data[c.ColumnCode] = user.EmployeeCode;
                        serverManagedFields.Add(c.ColumnCode);
                    }
                }
            }

            var readOnlyCols = cols
                .Where(c => c.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            if (readOnlyCols.Count > 0)
            {
                if (isNew)
                {
                    foreach (var rc in readOnlyCols)
                    {
                        req.Data.Remove(rc.ColumnCode);
                        if (!string.IsNullOrWhiteSpace(rc.DefaultValue))
                            req.Data[rc.ColumnCode] = rc.DefaultValue;
                    }
                }
                else
                {
                    var existingRec = await _records.GetRecordByIdAsync(req.Id!);
                    if (existingRec != null)
                    {
                        foreach (var rc in readOnlyCols)
                        {
                            if (existingRec.Data.TryGetValue(rc.ColumnCode, out var prevVal))
                                req.Data[rc.ColumnCode] = prevVal;
                            else
                                req.Data.Remove(rc.ColumnCode);
                        }
                    }
                }
            }
        }

        return (null, serverManagedFields);
    }

    [HttpPatch("{id}/cell")]
    public async Task<IActionResult> PatchCell(string id, [FromBody] PatchCellRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var loc = await _records.GetRecordLocationAsync(id);
        if (loc == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
        var appCode = await ResolveAppCodeAsync(loc.Value.AppId, loc.Value.TableId);
        if (!AuthService.CanAccessTable(user, appCode, loc.Value.TableId, "edit"))
            return StatusCode(403, new { success = false, message = $"Bạn không có quyền chỉnh sửa ô dữ liệu trên bảng này." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var tbl = tables.FirstOrDefault(t => t.Id == loc.Value.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, loc.Value.TableId, tbl?.RowScope, false);
        if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
        {
            return StatusCode(403, new { success = false, message = rowScope == "DEPARTMENT" ? "Bạn chỉ được chỉnh sửa bản ghi thuộc phòng ban của mình." : "Bạn chỉ được chỉnh sửa bản ghi của chính mình." });
        }

        if (string.IsNullOrWhiteSpace(req.Field))
            return BadRequest(new { success = false, message = "Field là bắt buộc." });

        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            var cols = await _metadata.GetColumnsAsync(loc.Value.TableId);
            var targetCol = cols.FirstOrDefault(c => c.ColumnCode.Equals(req.Field, StringComparison.OrdinalIgnoreCase));
            if (targetCol != null && targetCol.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase)))
            {
                return StatusCode(403, new { success = false, message = $"Bạn chỉ có quyền xem, không thể chỉnh sửa trường '{targetCol.ColumnName}'." });
            }
        }

        try
        {
            var isAdmin = AuthService.IsAdminUser(user);
            var (record, triggered) = await _records.PatchCellAsync(id, req, user.Username, isAdmin);
            if (record == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
            return Ok(new { success = true, data = record, triggeredAutomations = triggered });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/submit-approval")]
    public async Task<IActionResult> SubmitApproval(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var loc = await _records.GetRecordLocationAsync(id);
        if (loc == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
        var appCode = await ResolveAppCodeAsync(loc.Value.AppId, loc.Value.TableId);
        if (!AuthService.CanAccessTable(user, appCode, loc.Value.TableId, "edit")
            && !AuthService.CanAccessTable(user, appCode, loc.Value.TableId, "create"))
            return StatusCode(403, new { success = false, message = "Không có quyền trình duyệt." });

        try
        {
            var updated = await _records.SubmitApprovalAsync(id, user.Username);
            return updated != null ? Ok(new { success = true, data = updated }) : NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/recall-approval")]
    public async Task<IActionResult> RecallApproval(string id, [FromBody] ApprovalLifecycleRequest? req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        try
        {
            var (updated, err) = await _records.RecallApprovalAsync(id, user, req?.Reason, req?.ExpectedVersion);
            if (err != null) return updated == null && err.Contains("Không tìm thấy")
                ? NotFound(new { success = false, message = err })
                : StatusCode(403, new { success = false, message = err });
            return Ok(new { success = true, data = updated });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/cancel-approval")]
    public async Task<IActionResult> CancelApproval(string id, [FromBody] ApprovalLifecycleRequest? req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        try
        {
            var (updated, err) = await _records.CancelApprovalAsync(id, user, req?.Reason, req?.ExpectedVersion);
            if (err != null) return updated == null && err.Contains("Không tìm thấy")
                ? NotFound(new { success = false, message = err })
                : StatusCode(403, new { success = false, message = err });
            return Ok(new { success = true, data = updated });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ProcessApproval(string id, [FromBody] ApprovalActionRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        try
        {
            var (updated, err) = await _records.ProcessApprovalAsync(id, user, req.Approve, req.Reason, req.ExpectedVersion, req.ExpectedStepOrder);
            if (err != null) return StatusCode(403, new { success = false, message = err });
            return Ok(new { success = true, data = updated });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("pending-approvals")]
    public async Task<IActionResult> PendingApprovals()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _records.GetPendingApprovalsAsync(user);
        return Ok(new { success = true, data = items });
    }

    [HttpGet("{id}/comments")]
    public async Task<IActionResult> GetComments(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _records.GetCommentsAsync(id);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("{id}/comments")]
    public async Task<IActionResult> AddComment(string id, [FromBody] AddCommentRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new { success = false, message = "Nội dung bình luận không được trống." });

        var saved = await _records.AddCommentAsync(id, user.Username, req.Content);
        return saved != null ? Ok(new { success = true, data = saved }) : NotFound(new { success = false });
    }

    [HttpGet("trash")]
    public async Task<IActionResult> GetTrash([FromQuery] string? appId, [FromQuery] string? tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var appCode = await ResolveAppCodeAsync(appId, tableId);
        if (!AuthService.HasAppPermission(user, appCode, "record.view"))
            return StatusCode(403, new { success = false });

        var items = await _records.GetTrashRecordsAsync(appId, tableId);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var loc = await _records.GetRecordLocationAsync(id);
        if (loc == null) return NotFound(new { success = false });
        var appCode = await ResolveAppCodeAsync(loc.Value.AppId, loc.Value.TableId);
        if (!AuthService.HasAppPermission(user, appCode, "record.delete")
            && !AuthService.HasAppPermission(user, appCode, "record.create")
            && !AuthService.HasPermission(user, "studio.manage"))
        {
            return StatusCode(403, new { success = false, message = "Không có quyền khôi phục dữ liệu." });
        }

        var ok = await _records.RestoreRecordAsync(id, user.Username);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpDelete("{id}/permanent")]
    public async Task<IActionResult> PermanentDelete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Admin mới có quyền xóa vĩnh viễn." });

        var ok = await _records.PermanentDeleteAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GlobalAuditLogs(
        [FromQuery] string? appId,
        [FromQuery] string? tableId,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] string? search,
        [FromQuery] int limit = 100)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        bool hasGlobalAudit = AuthService.HasPermission(user, "studio.manage")
            || AuthService.HasPermission(user, "user.manage")
            || AuthService.HasPermission(user, "audit.view");

        string? resolvedAppId = appId;
        string? appCode = null;
        string? exactActor = null;

        if (!string.IsNullOrWhiteSpace(appId) || !string.IsNullOrWhiteSpace(tableId))
        {
            if (!string.IsNullOrWhiteSpace(appId))
            {
                var app = await _metadata.GetAppByCodeOrIdAsync(appId);
                if (app != null)
                {
                    resolvedAppId = app.Id;
                    appCode = app.Code;
                }
            }
            appCode ??= await ResolveAppCodeAsync(resolvedAppId, tableId);
        }

        if (!hasGlobalAudit)
        {
            if (string.IsNullOrWhiteSpace(appCode) || !AuthService.CanAccessApp(user, appCode))
                return StatusCode(403, new { success = false, message = "Không có quyền xem nhật ký hệ thống." });

            if (!string.IsNullOrWhiteSpace(tableId) && !AuthService.CanAccessTable(user, appCode, tableId, "view"))
                return StatusCode(403, new { success = false, message = $"Không có quyền xem lịch sử bảng dữ liệu này trên App {appCode}." });

            bool canViewAllAppAudit = AuthService.IsAdminUser(user)
                || AuthService.HasAppPermission(user, appCode, "app.admin")
                || AuthService.HasAppPermission(user, appCode, "record.view_all")
                || AuthService.HasAppPermission(user, appCode, "audit.view");

            if (!canViewAllAppAudit)
            {
                // Regular user only sees their own activity history inside this App
                exactActor = user.Username;
            }
        }

        var logs = await _records.QueryGlobalAuditLogsAsync(resolvedAppId, action, actor, limit, tableId, search, exactActor);

        if (!hasGlobalAudit && !string.IsNullOrWhiteSpace(appCode) && !AuthService.IsAdminUser(user))
        {
            logs = logs
                .Where(l => string.IsNullOrWhiteSpace(l.TableId) || AuthService.CanAccessTable(user, appCode, l.TableId, "view"))
                .ToList();
        }

        await StripHiddenAuditColumnsAsync(logs, user);
        return Ok(new { success = true, data = logs });
    }

    private async Task StripHiddenAuditColumnsAsync(List<AppAuditLogDto> logs, SysUserDto user)
    {
        if (logs.Count == 0 || AuthService.IsAdminUser(user)) return;
        var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
        var tableIds = logs.Select(l => l.TableId).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        var hiddenByTable = new Dictionary<string, List<string>>();
        foreach (var tid in tableIds)
        {
            var cols = await _metadata.GetColumnsAsync(tid);
            var hidden = cols
                .Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                .Select(c => c.ColumnCode)
                .ToList();
            if (hidden.Count > 0) hiddenByTable[tid] = hidden;
        }
        if (hiddenByTable.Count == 0) return;

        foreach (var log in logs)
        {
            if (!hiddenByTable.TryGetValue(log.TableId, out var hiddenCodes)) continue;
            foreach (var code in hiddenCodes)
            {
                log.OldData?.Remove(code);
                log.NewData?.Remove(code);
            }
        }
    }

    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var appIds = await _records.GetRecordsAppIdsAsync(req.Ids);
        foreach (var aid in appIds)
        {
            var appCode = await ResolveAppCodeAsync(aid);
            if (!AuthService.HasAppPermission(user, appCode, "record.delete"))
                return StatusCode(403, new { success = false, message = $"Bạn không có quyền xóa dữ liệu trên App {appCode}." });
        }

        try
        {
            var isAdmin = AuthService.IsAdminUser(user);
            var deleted = await _records.DeleteRecordsAsync(req.Ids, user.Username, isAdmin);
            return Ok(new { success = true, deleted });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSingle(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var loc = await _records.GetRecordLocationAsync(id);
        if (loc == null) return NotFound(new { success = false });
        var appCode = await ResolveAppCodeAsync(loc.Value.AppId, loc.Value.TableId);
        if (!AuthService.CanAccessTable(user, appCode, loc.Value.TableId, "delete"))
            return StatusCode(403, new { success = false, message = $"Bạn không có quyền xóa dữ liệu trên bảng này." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var tbl = tables.FirstOrDefault(t => t.Id == loc.Value.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, loc.Value.TableId, tbl?.RowScope, false);
        if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
        {
            return StatusCode(403, new { success = false, message = rowScope == "DEPARTMENT" ? "Bạn chỉ được xóa bản ghi thuộc phòng ban của mình." : "Bạn chỉ được xóa bản ghi của chính mình." });
        }

        try
        {
            var isAdmin = AuthService.IsAdminUser(user);
            var deleted = await _records.DeleteRecordsAsync(new List<string> { id }, user.Username, isAdmin);
            return deleted > 0 ? Ok(new { success = true }) : NotFound(new { success = false });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateTable([FromBody] ValidateTableRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "TableId là bắt buộc." });

        var appCode = await ResolveAppCodeAsync(null, req.TableId);
        if (!AuthService.CanAccessTable(user, appCode, req.TableId, "edit"))
            return StatusCode(403, new { success = false, message = "Không có quyền kiểm tra/cập nhật bảng." });

        var (total, validCount, errorCount) = await _records.ValidateTableRecordsAsync(req.TableId);
        return Ok(new
        {
            success = true,
            data = new { total, validCount, errorCount, engineUsed = "dotnet-compute" }
        });
    }

    [HttpPost("bulk-import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<IActionResult> BulkImport([FromBody] BulkImportRequest req) => HandleImportAsync(req, false);

    [HttpPost("bulk-import/preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<IActionResult> PreviewImport([FromBody] BulkImportRequest req) => HandleImportAsync(req, true);

    private async Task<IActionResult> HandleImportAsync(BulkImportRequest req, bool preview)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.ModuleId) || string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "AppId, ModuleId và TableId là bắt buộc." });

        try
        {
            if (preview) return Ok(new { success = true, data = await _records.PreviewImportAsync(req, user) });
            var (inserted, updated, errorRows) = await _records.BulkImportAsync(req, user);
            return Ok(new { success = true, data = new { inserted, updated, errorRows } });
        }
        catch (ImportRejectedException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, data = ex.Preview });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "40P01" or "23505")
        {
            return Conflict(new { success = false, message = "Có thay đổi đồng thời. Chưa nhập dữ liệu; vui lòng kiểm tra lại." });
        }
    }
}
