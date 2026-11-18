using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly MetadataService _metadata;

    public RolesController(AuthService auth, MetadataService metadata)
    {
        _auth = auth;
        _metadata = metadata;
    }

    private static bool CanManageRoles(SysUserDto user) =>
        AuthService.IsAdminUser(user)
        || AuthService.HasPermission(user, "role.manage")
        || AuthService.HasPermission(user, "studio.manage");

    private async Task<bool> CanManageAppPermissionsAsync(SysUserDto user, string appIdOrCode)
    {
        if (CanManageRoles(user)) return true;
        var app = await _metadata.GetAppByCodeOrIdAsync(appIdOrCode);
        return app != null && AuthService.HasAppPermission(user, app.Code, "app.admin");
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog()
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        return Ok(new { success = true, data = AuthService.GetPermissionCatalog() });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        var roles = await _auth.ListRolesAsync();
        return Ok(new { success = true, data = roles });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertRoleRequest req)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageRoles(current))
            return StatusCode(403, new { success = false, message = "Không có quyền quản lý vai trò & phân quyền." });

        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "Mã vai trò và Tên vai trò là bắt buộc." });

        var (saved, err) = await _auth.UpsertRoleAsync(req);
        if (saved == null) return BadRequest(new { success = false, message = err ?? "Không thể lưu vai trò." });
        return Ok(new { success = true, data = saved });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageRoles(current))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa vai trò." });

        var (ok, err) = await _auth.DeleteRoleAsync(id);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpGet("by-app/{appIdOrCode}")]
    public async Task<IActionResult> GetByApp(string appIdOrCode)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!await CanManageAppPermissionsAsync(current, appIdOrCode))
            return StatusCode(403, new { success = false, message = "Không có quyền xem ma trận phân quyền App." });

        var matrix = await _auth.GetAppRoleMatrixAsync(appIdOrCode);
        if (matrix == null) return NotFound(new { success = false, message = "Không tìm thấy ứng dụng." });
        return Ok(new { success = true, data = matrix });
    }

    [HttpPut("by-app/{appIdOrCode}")]
    public async Task<IActionResult> SaveByApp(string appIdOrCode, [FromBody] UpdateAppRoleMatrixRequest req)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!await CanManageAppPermissionsAsync(current, appIdOrCode))
            return StatusCode(403, new { success = false, message = "Không có quyền phân quyền App." });

        var (ok, err) = await _auth.SaveAppRoleMatrixAsync(appIdOrCode, req);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpGet("by-app/{appIdOrCode}/tables")]
    public async Task<IActionResult> GetTableMatrixByApp(string appIdOrCode)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!await CanManageAppPermissionsAsync(current, appIdOrCode))
            return StatusCode(403, new { success = false, message = "Không có quyền xem phân quyền bảng trong App." });

        var matrix = await _auth.GetAppTableRoleMatrixAsync(appIdOrCode);
        if (matrix == null) return NotFound(new { success = false, message = "Không tìm thấy ứng dụng." });
        return Ok(new { success = true, data = matrix });
    }

    [HttpPut("by-app/{appIdOrCode}/tables")]
    public async Task<IActionResult> SaveTableMatrixByApp(string appIdOrCode, [FromBody] UpdateAppTableRoleMatrixRequest req)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!await CanManageAppPermissionsAsync(current, appIdOrCode))
            return StatusCode(403, new { success = false, message = "Không có quyền phân quyền bảng trong App." });

        var (ok, err) = await _auth.SaveAppTableRoleMatrixAsync(appIdOrCode, req);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }
}
