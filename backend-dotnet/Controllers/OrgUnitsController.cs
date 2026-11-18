using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/org-units")]
public class OrgUnitsController : ControllerBase
{
    private readonly OrgUnitService _orgUnits;
    private readonly AuthService _auth;

    public OrgUnitsController(OrgUnitService orgUnits, AuthService auth)
    {
        _orgUnits = orgUnits;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _orgUnits.GetOrgUnitsAsync();
        return Ok(new { success = true, data = items });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertOrgUnitRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình cơ cấu tổ chức." });

        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "Mã đơn vị và Tên đơn vị là bắt buộc." });

        try
        {
            var saved = await _orgUnits.UpsertOrgUnitAsync(req);
            return Ok(new { success = true, data = saved });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false });

        var ok = await _orgUnits.DeleteOrgUnitAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("{id}/subtree-users")]
    public async Task<IActionResult> GetSubtreeUsers(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var users = await _orgUnits.GetSubtreeUsernamesAsync(id);
        return Ok(new { success = true, data = users });
    }
}
