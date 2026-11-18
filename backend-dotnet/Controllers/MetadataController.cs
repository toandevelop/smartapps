using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api")]
public class MetadataController : ControllerBase
{
    private readonly MetadataService _metadata;
    private readonly DynamicComputeService _compute;
    private readonly AuthService _auth;

    public MetadataController(MetadataService metadata, DynamicComputeService compute, AuthService auth)
    {
        _metadata = metadata;
        _compute = compute;
        _auth = auth;
    }

    [HttpGet("modules")]
    public async Task<IActionResult> GetModules([FromQuery] string? appId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _metadata.GetModulesAsync(appId);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("modules")]
    public async Task<IActionResult> UpsertModule([FromBody] UpsertModuleRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền sửa phân hệ." });

        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "AppId, Code và Name là bắt buộc." });

        var item = await _metadata.UpsertModuleAsync(req);
        return Ok(new { success = true, data = item });
    }

    [HttpDelete("modules/{id}")]
    public async Task<IActionResult> DeleteModule(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa phân hệ." });

        var ok = await _metadata.DeleteModuleAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("tables")]
    public async Task<IActionResult> GetTables([FromQuery] string? appId, [FromQuery] string? moduleId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _metadata.GetTablesAsync(appId, moduleId);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("tables")]
    public async Task<IActionResult> UpsertTable([FromBody] UpsertTableRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền sửa bảng." });

        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.ModuleId) ||
            string.IsNullOrWhiteSpace(req.TableName) || string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest(new { success = false, message = "Thiếu thông tin bảng bắt buộc." });

        var item = await _metadata.UpsertTableAsync(req);
        return Ok(new { success = true, data = item });
    }

    [HttpDelete("tables/{id}")]
    public async Task<IActionResult> DeleteTable(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa bảng." });

        var ok = await _metadata.DeleteTableAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("tables/{tableId}/print-templates")]
    public async Task<IActionResult> GetPrintTemplates(string tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var items = await _metadata.GetPrintTemplatesAsync(tableId);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("print-templates")]
    public async Task<IActionResult> UpsertPrintTemplate([FromBody] UpsertPrintTemplateRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình mẫu in." });

        if (string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.HtmlTemplate))
            return BadRequest(new { success = false, message = "Bảng, tên mẫu và nội dung HTML là bắt buộc." });

        var saved = await _metadata.UpsertPrintTemplateAsync(req, user.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpDelete("print-templates/{id}")]
    public async Task<IActionResult> DeletePrintTemplate(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa mẫu in." });

        var ok = await _metadata.DeletePrintTemplateAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("tables/{tableId}/compile")]
    public async Task<IActionResult> CompileTableSchema(string tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false });

        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == tableId);
        if (table == null) return NotFound(new { success = false, message = "Không tìm thấy bảng." });

        var cols = await _metadata.GetColumnsAsync(tableId);
        var compiled = _compute.CompileSchema(table, cols);
        return Ok(new { success = true, engine = "dotnet-compiler", data = compiled });
    }

    [HttpGet("columns")]
    public async Task<IActionResult> GetColumns([FromQuery] string tableId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(tableId))
            return BadRequest(new { success = false, message = "tableId là bắt buộc." });

        var items = await _metadata.GetColumnsAsync(tableId);
        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            items = items
                .Where(c => !c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }
        return Ok(new { success = true, data = items });
    }

    [HttpPost("columns")]
    public async Task<IActionResult> UpsertColumn([FromBody] UpsertColumnRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền sửa cột động." });

        if (string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.ColumnCode) || string.IsNullOrWhiteSpace(req.ColumnName))
            return BadRequest(new { success = false, message = "TableId, ColumnCode và ColumnName là bắt buộc." });

        try
        {
            var item = await _metadata.UpsertColumnAsync(req);
            return Ok(new { success = true, data = item });
        }
        catch (FormulaCycleException ex)
        {
            return BadRequest(new { success = false, message = ex.Message, cycleColumns = ex.CycleColumns });
        }
    }

    [HttpDelete("columns/{id}")]
    public async Task<IActionResult> DeleteColumn(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa cột động." });

        var ok = await _metadata.DeleteColumnAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }
}
