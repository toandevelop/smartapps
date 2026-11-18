using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/widgets")]
public class WidgetsController : ControllerBase
{
    private readonly WidgetService _widgets;
    private readonly AuthService _auth;
    private readonly MetadataService _metadata;

    public WidgetsController(WidgetService widgets, AuthService auth, MetadataService metadata)
    {
        _widgets = widgets;
        _auth = auth;
        _metadata = metadata;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string appId,
        [FromQuery] bool includeHidden = false,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] bool personalMode = false)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(appId))
            return BadRequest(new { success = false, message = "appId là bắt buộc." });

        var app = await _metadata.GetAppByCodeOrIdAsync(appId);
        if (app != null && !AuthService.CanAccessApp(user, app.Code))
            return StatusCode(403, new { success = false, message = $"Không có quyền truy cập App {app.Code}." });

        var items = await _widgets.GetAppWidgetsWithDataAsync(appId, includeHidden, fromDate, toDate, user, personalMode);
        return Ok(new { success = true, data = items });
    }

    [HttpGet("portal")]
    public async Task<IActionResult> PortalSummary()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var items = await _widgets.GetPortalKpisAsync(user);
        return Ok(new { success = true, data = items });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertWidgetRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.TableId) || string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, message = "AppId, TableId và Title là bắt buộc." });

        var app = await _metadata.GetAppByCodeOrIdAsync(req.AppId);
        if (!AuthService.HasPermission(user, "studio.manage")
            && !AuthService.HasAppPermission(user, app?.Code, "widget.manage")
            && !AuthService.HasAppPermission(user, app?.Code, "record.edit"))
        {
            return StatusCode(403, new { success = false, message = "Không có quyền tùy chỉnh widget." });
        }

        var saved = await _widgets.UpsertWidgetAsync(req);
        return Ok(new { success = true, data = saved });
    }

    [HttpPost("layout")]
    public async Task<IActionResult> SaveLayout([FromBody] List<WidgetLayoutOrderItem> items)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage")
            && !AuthService.HasPermission(user, "widget.manage")
            && !AuthService.HasPermission(user, "record.edit"))
            return StatusCode(403, new { success = false });

        await _widgets.SaveLayoutOrderAsync(items);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage")
            && !AuthService.HasPermission(user, "widget.manage")
            && !AuthService.HasPermission(user, "record.edit"))
            return StatusCode(403, new { success = false });

        var ok = await _widgets.DeleteWidgetAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }
}
