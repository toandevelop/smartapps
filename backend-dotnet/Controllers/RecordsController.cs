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
                ownerDepartment = string.IsNullOrWhiteSpace(user.Department)
                    ? (string.IsNullOrWhiteSpace(user.OrgUnitId) ? "__UNASSIGNED__" : user.OrgUnitId)
                    : user.Department;
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

    [HttpGet("global-search")]
    public async Task<IActionResult> GlobalSearch([FromQuery] string q, [FromQuery] int limit = 6)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new { success = true, data = new GlobalSearchResultDto { Query = q ?? string.Empty } });

        var data = await _records.GlobalSearchAsync(user, q, limit);
        return Ok(new { success = true, data });
    }

    [HttpPost("{id}/restore-fields")]
    public async Task<IActionResult> RestoreRecordFields(string id, [FromBody] RestoreRecordFieldsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var loc = await _records.GetRecordLocationAsync(id);
        if (loc == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
        var appCode = await ResolveAppCodeAsync(loc.Value.AppId, loc.Value.TableId);
        if (!AuthService.CanAccessTable(user, appCode, loc.Value.TableId, "edit"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền chỉnh sửa bản ghi này." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var tbl = tables.FirstOrDefault(t => t.Id == loc.Value.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, loc.Value.TableId, tbl?.RowScope, false);
        if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
            return StatusCode(403, new { success = false, message = "Bản ghi nằm ngoài phạm vi truy cập của bạn." });

        var isAdmin = AuthService.IsAdminUser(user);
        var cols = await _metadata.GetColumnsAsync(loc.Value.TableId);
        var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
        var serverManaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedFields = new List<string>();

        foreach (var col in cols)
        {
            if (col.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(col.Formula))
                continue;
            if (!isAdmin && (
                    col.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)) ||
                    col.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase))))
            {
                serverManaged.Add(col.ColumnCode);
                continue;
            }
            if ((req.Fields ?? new()).Any(f => f.Equals(col.ColumnCode, StringComparison.OrdinalIgnoreCase)))
                allowedFields.Add(col.ColumnCode);
        }

        if (allowedFields.Count == 0)
            return BadRequest(new { success = false, message = "Không có trường hợp lệ hoặc được phép khôi phục." });

        try
        {
            var (updated, err) = await _records.RestoreRecordFieldsAsync(
                id,
                req.AuditId,
                allowedFields,
                req.ExpectedVersion,
                user.Username,
                isAdmin,
                serverManaged);
            if (err != null) return BadRequest(new { success = false, message = err });
            return Ok(new { success = true, data = updated });
        }
        catch (RecordLockedException ex)
        {
            return StatusCode(423, new { success = false, message = ex.Message });
        }
        catch (RecordConflictException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
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

    [HttpPost("ai-import-map")]
    public async Task<IActionResult> AiImportMap([FromBody] AiImportMapRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.TableId) || req.Headers.Count == 0)
            return BadRequest(new { success = false, message = "TableId và danh sách tiêu đề cột là bắt buộc." });

        var appCode = await ResolveAppCodeAsync(null, req.TableId);
        if (!AuthService.CanAccessTable(user, appCode, req.TableId, "create")
            && !AuthService.CanAccessTable(user, appCode, req.TableId, "edit"))
            return StatusCode(403, new { success = false, message = "Không có quyền nhập dữ liệu vào bảng này." });

        var cols = await FilterVisibleColumnsAsync(req.TableId, user);
        var result = await _ai.SuggestExcelMappingsAsync(req, cols, user.Username);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("ai-doc-ocr")]
    public async Task<IActionResult> AiDocOcr([FromBody] AiDocOcrRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "TableId là bắt buộc." });
        if (string.IsNullOrWhiteSpace(req.RawText) && string.IsNullOrWhiteSpace(req.ImageBase64))
            return BadRequest(new { success = false, message = "Vui lòng cung cấp ảnh/chứng từ hoặc nội dung văn bản để trích xuất." });

        var appCode = await ResolveAppCodeAsync(null, req.TableId);
        if (!AuthService.CanAccessTable(user, appCode, req.TableId, "create")
            && !AuthService.CanAccessTable(user, appCode, req.TableId, "edit"))
            return StatusCode(403, new { success = false, message = "Không có quyền thao tác trên bảng này." });

        var cols = await FilterVisibleColumnsAsync(req.TableId, user);
        var result = await _ai.ExtractDocumentFieldsAsync(req, cols, user.Username);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("{id}/ai-triage")]
    public async Task<IActionResult> AiWorkflowTriage(string id)
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

        var visibleCols = await FilterVisibleColumnsAsync(rec.TableId, user, rec);
        var (ownerFilter, ownerEmail, ownerEmpCode, ownerDept) = BuildOwnerScopeParams(user, rowScope);
        var (history, _) = await _records.QueryRecordsAsync(
            null, null, rec.TableId, null, "ALL", 1, 50,
            ownerFilter, null, "created_at", "DESC", ownerEmail, ownerEmpCode, ownerDept);

        var triage = await _ai.TriageWorkflowRecordAsync(rec, tbl?.DisplayName ?? rec.TableId, visibleCols, history, user.Username);
        return Ok(new { success = true, data = triage });
    }

    [HttpPost("ai-insights")]
    public async Task<IActionResult> AiDashboardInsights([FromBody] AiDashboardInsightsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(new { success = false, message = "TableId và Question là bắt buộc." });

        var appCode = await ResolveAppCodeAsync(null, req.TableId);
        if (appCode == null || !AuthService.CanAccessApp(user, appCode) || !AuthService.CanAccessTable(user, appCode, req.TableId, "view"))
            return StatusCode(403, new { success = false, message = "Không có quyền phân tích dữ liệu bảng này." });

        var tbl = (await _metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == req.TableId);
        var rowScope = AuthService.ResolveRowScope(user, appCode, req.TableId, tbl?.RowScope, false);
        var (ownerFilter, ownerEmail, ownerEmpCode, ownerDept) = BuildOwnerScopeParams(user, rowScope);
        var visibleCols = await FilterVisibleColumnsAsync(req.TableId, user);

        var (plan, planEngine) = await _ai.BuildNl2QueryPlanAsync(req.Question, visibleCols, user.Username);
        string? filtersJson = plan.Conditions.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(new
            {
                logic = plan.Logic,
                conditions = plan.Conditions.Select(c => new { field = c.Field, op = c.Op, value = c.Value, value2 = c.Value2 })
            })
            : null;

        var (items, total) = await _records.QueryRecordsAsync(
            null, null, req.TableId, plan.Search, plan.Status, 1, 200,
            ownerFilter, filtersJson, plan.SortField, plan.SortDir, ownerEmail, ownerEmpCode, ownerDept);

        if (!AuthService.IsAdminUser(user))
        {
            var visibleSet = visibleCols.Select(c => c.ColumnCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                foreach (var k in item.Data.Keys.Where(k => !visibleSet.Contains(k)).ToList())
                    item.Data.Remove(k);
            }
        }

        var result = await _ai.SummarizeDashboardQueryResultAsync(req.Question, plan, planEngine, visibleCols, items, total, user.Username);
        return Ok(new { success = true, data = result });
    }

    private async Task<List<SysColumnDto>> FilterVisibleColumnsAsync(string tableId, SysUserDto user, AppRecordDto? stripRecord = null)
    {
        var cols = await _metadata.GetColumnsAsync(tableId);
        if (AuthService.IsAdminUser(user)) return cols;
        var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
        var visible = new List<SysColumnDto>();
        foreach (var c in cols)
        {
            if (c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                stripRecord?.Data.Remove(c.ColumnCode);
            else
                visible.Add(c);
        }
        return visible;
    }

    private static (string? OwnerFilter, string? OwnerEmail, string? OwnerEmpCode, string? OwnerDepartment) BuildOwnerScopeParams(
        SysUserDto user,
        string rowScope)
    {
        if (rowScope == "OWNER_ONLY")
            return (user.Username, user.Email, user.EmployeeCode, null);
        if (rowScope == "DEPARTMENT")
        {
            var dept = string.IsNullOrWhiteSpace(user.Department)
                ? (string.IsNullOrWhiteSpace(user.OrgUnitId) ? "__UNASSIGNED__" : user.OrgUnitId)
                : user.Department;
            return (user.Username, user.Email, user.EmployeeCode, dept);
        }
        return (null, null, null, null);
    }

    private async Task<IActionResult> ExecuteIdempotentAsync(string actor, Func<Task<IActionResult>> operation)
    {
        var idempKey = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(idempKey))
        {
            var cached = await _records.TryGetIdempotentResultAsync(idempKey, actor);
            if (cached != null)
            {
                return new ContentResult
                {
                    StatusCode = cached.Value.Status,
                    Content = cached.Value.BodyJson,
                    ContentType = "application/json"
                };
            }
        }

        var result = await operation();
        if (!string.IsNullOrWhiteSpace(idempKey) && result is OkObjectResult ok && ok.Value != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                ok.Value,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            await _records.SaveIdempotentResultAsync(idempKey, actor, Request.Method, Request.Path.Value ?? string.Empty, 200, json);
        }
        return result;
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
            return await ExecuteIdempotentAsync(user.Username, async () =>
            {
                var isAdmin = AuthService.IsAdminUser(user);
                var (record, triggered) = await _records.UpsertRecordAsync(req, user.Username, isAdmin, serverManagedFields);
                return Ok(new { success = true, data = record, triggeredAutomations = triggered });
            });
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
            return await ExecuteIdempotentAsync(user.Username, async () =>
            {
                var (record, triggered) = await _records.UpsertRecordWithRelationsAsync(req, user.Username, isAdmin, parentManaged);
                return Ok(new { success = true, data = record, triggeredAutomations = triggered });
            });
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
            return await ExecuteIdempotentAsync(user.Username, async () =>
            {
                var isAdmin = AuthService.IsAdminUser(user);
                var (record, triggered) = await _records.PatchCellAsync(id, req, user.Username, isAdmin);
                if (record == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });
                return Ok(new { success = true, data = record, triggeredAutomations = triggered });
            });
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
            var (updated, err) = await _records.ProcessApprovalAsync(id, user, req.Approve, req.Reason, req.ExpectedVersion, req.ExpectedStepOrder, req.Action);
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

    [HttpPost("batch-patch")]
    public async Task<IActionResult> BatchPatch([FromBody] BatchPatchRecordsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        if (string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.ColumnCode))
            return BadRequest(new { success = false, message = "TableId và ColumnCode là bắt buộc." });

        var ids = (req.RecordIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn ít nhất một bản ghi." });
        if (ids.Count > 100)
            return BadRequest(new { success = false, message = "Chỉ hỗ trợ sửa hàng loạt tối đa 100 bản ghi mỗi lượt." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var tbl = tables.FirstOrDefault(t => t.Id == req.TableId);
        if (tbl == null)
            return NotFound(new { success = false, message = "Không tìm thấy bảng dữ liệu." });

        var appCode = await ResolveAppCodeAsync(tbl.AppId, tbl.Id);
        if (!AuthService.CanAccessTable(user, appCode, tbl.Id, "edit"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền chỉnh sửa dữ liệu trên bảng này." });

        var cols = await _metadata.GetColumnsAsync(tbl.Id);
        var targetCol = cols.FirstOrDefault(c => c.ColumnCode.Equals(req.ColumnCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (targetCol == null)
            return BadRequest(new { success = false, message = $"Không tìm thấy cột '{req.ColumnCode}'." });

        var dataType = (targetCol.DataType ?? "STRING").ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(targetCol.Formula) || dataType is "AUTO_NUMBER" or "SUB_TABLE" or "FILE" or "IMAGE")
            return BadRequest(new { success = false, message = $"Không thể sửa hàng loạt cho cột tự tính hoặc cột đặc biệt '{targetCol.ColumnName}'." });

        var isAdmin = AuthService.IsAdminUser(user);
        if (!isAdmin)
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            if (targetCol.ReadOnlyForRoles.Any(rr => userRoles.Contains(rr, StringComparer.OrdinalIgnoreCase))
                || targetCol.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
            {
                return StatusCode(403, new { success = false, message = $"Bạn không có quyền chỉnh sửa trường '{targetCol.ColumnName}'." });
            }
        }

        var rowScope = AuthService.ResolveRowScope(user, appCode, tbl.Id, tbl.RowScope, false);
        var result = new BatchOperationResultDto { Requested = ids.Count };

        foreach (var id in ids)
        {
            var loc = await _records.GetRecordLocationAsync(id);
            if (loc == null || !string.Equals(loc.Value.TableId, tbl.Id, StringComparison.Ordinal))
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = "Không tìm thấy bản ghi thuộc bảng này." });
                continue;
            }
            if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(id, user, rowScope))
            {
                result.Errors.Add(new BatchOperationItemError
                {
                    RecordId = id,
                    Message = rowScope == "DEPARTMENT"
                        ? "Bản ghi nằm ngoài phạm vi phòng ban của bạn."
                        : "Bạn chỉ được chỉnh sửa bản ghi của chính mình."
                });
                continue;
            }

            try
            {
                var (updated, _) = await _records.PatchCellAsync(
                    id,
                    new PatchCellRequest { Field = targetCol.ColumnCode, Value = req.Value },
                    user.Username,
                    isAdmin);
                if (updated == null)
                {
                    result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = "Không tìm thấy bản ghi." });
                }
                else
                {
                    result.SucceededIds.Add(id);
                }
            }
            catch (FormRuleValidationException ex)
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = ex.Message });
            }
            catch (DuplicateIdentityKeyException ex)
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = ex.Message });
            }
            catch (RecordConflictException ex)
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = ex.Message });
            }
            catch (RecordLockedException ex)
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = ex.Message });
            }
        }

        result.Succeeded = result.SucceededIds.Count;
        result.Failed = result.Errors.Count;
        return Ok(new { success = result.Succeeded > 0 || result.Failed == 0, data = result });
    }

    [HttpPost("batch-approve")]
    public async Task<IActionResult> BatchApprove([FromBody] BatchApproveRecordsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var ids = (req.RecordIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn ít nhất một hồ sơ." });
        if (ids.Count > 50)
            return BadRequest(new { success = false, message = "Chỉ hỗ trợ phê duyệt hàng loạt tối đa 50 hồ sơ mỗi lượt." });

        var result = new BatchOperationResultDto { Requested = ids.Count };
        foreach (var id in ids)
        {
            try
            {
                var (updated, err) = await _records.ProcessApprovalAsync(id, user, req.Approve, req.Reason, null, null, null);
                if (err != null || updated == null)
                {
                    result.Errors.Add(new BatchOperationItemError
                    {
                        RecordId = id,
                        Message = err ?? "Không tìm thấy hồ sơ hoặc không thể xử lý phê duyệt."
                    });
                }
                else
                {
                    result.SucceededIds.Add(id);
                }
            }
            catch (RecordConflictException ex)
            {
                result.Errors.Add(new BatchOperationItemError { RecordId = id, Message = ex.Message });
            }
        }

        result.Succeeded = result.SucceededIds.Count;
        result.Failed = result.Errors.Count;
        return Ok(new { success = result.Succeeded > 0 || result.Failed == 0, data = result });
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
