using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api/automations")]
public class AutomationsController : ControllerBase
{
    private readonly AutomationService _automations;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;

    public AutomationsController(AutomationService automations, AuthService auth, OutboxService outbox)
    {
        _automations = automations;
        _auth = auth;
        _outbox = outbox;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _automations.GetAutomationsAsync();
        return Ok(new { success = true, data = items });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertAutomationRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình tự động hóa." });

        var needsTargetTable = string.Equals(req.ActionType, "CREATE_RECORD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(req.ActionType, "UPDATE_RECORD", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.SourceTableId) || (needsTargetTable && string.IsNullOrWhiteSpace(req.TargetTableId)))
            return BadRequest(new { success = false, message = "Tên quy tắc, bảng nguồn và bảng đích là bắt buộc." });

        var saved = await _automations.UpsertAutomationAsync(req);
        return Ok(new { success = true, data = saved });
    }

    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, [FromBody] Dictionary<string, bool> body)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false });

        var active = body.TryGetValue("isActive", out var val) && val;
        var ok = await _automations.ToggleAutomationAsync(id, active);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false });

        var ok = await _automations.DeleteAutomationAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int limit = 30)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var logs = await _automations.GetLogsAsync(limit);
        return Ok(new { success = true, data = logs });
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> DryRun(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false });

        var result = await _automations.DryRunRuleAsync(id);
        return result != null ? Ok(new { success = true, data = result }) : NotFound(new { success = false });
    }

    [HttpGet("outbox")]
    public async Task<IActionResult> ListOutbox([FromQuery] string? status = null, [FromQuery] int limit = 100)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false });

        var items = await _outbox.GetEventsAsync(status, limit);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("outbox/{id}/retry")]
    public async Task<IActionResult> RetryOutbox(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "automation.manage"))
            return StatusCode(403, new { success = false });

        var ok = await _outbox.RetryAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "Không tìm thấy sự kiện lỗi/dead-letter để thử lại." });
    }
}
