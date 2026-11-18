using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowService _workflows;
    private readonly BusinessCalendarService _calendar;
    private readonly AuthService _auth;

    public WorkflowsController(WorkflowService workflows, BusinessCalendarService calendar, AuthService auth)
    {
        _workflows = workflows;
        _calendar = calendar;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? appId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        var items = await _workflows.GetWorkflowsAsync(appId);
        return Ok(new { success = true, data = items });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertWorkflowRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "workflow.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền thiết kế quy trình Workflow." });

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.AppId) || string.IsNullOrWhiteSpace(req.TableId))
            return BadRequest(new { success = false, message = "Tên quy trình, Ứng dụng và Bảng dữ liệu là bắt buộc." });
        if (req.Steps == null || req.Steps.Count == 0)
            return BadRequest(new { success = false, message = "Workflow phải có ít nhất 1 bước phê duyệt." });

        var saved = await _workflows.UpsertWorkflowAsync(req);
        return Ok(new { success = true, data = saved });
    }

    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, [FromBody] Dictionary<string, bool> body)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "workflow.manage"))
            return StatusCode(403, new { success = false });

        var active = body.TryGetValue("isActive", out var v) && v;
        var ok = await _workflows.ToggleWorkflowAsync(id, active);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "workflow.manage"))
            return StatusCode(403, new { success = false });

        var ok = await _workflows.DeleteWorkflowAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }

    [HttpGet("delegations")]
    public async Task<IActionResult> ListDelegations()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var filter = AuthService.IsAdminUser(user) ? null : user.Username;
        var items = await _workflows.ListDelegationsAsync(filter);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("delegations")]
    public async Task<IActionResult> UpsertDelegation([FromBody] UpsertWorkflowDelegationRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "workflow.manage");
        var (item, err) = await _workflows.UpsertDelegationAsync(req, user.Username, isAdmin);
        return err != null ? BadRequest(new { success = false, message = err }) : Ok(new { success = true, data = item });
    }

    [HttpDelete("delegations/{id}")]
    public async Task<IActionResult> DeleteDelegation(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "workflow.manage");
        var ok = await _workflows.DeleteDelegationAsync(id, user.Username, isAdmin);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "Không tìm thấy hoặc không có quyền xóa ủy quyền." });
    }

    [HttpGet("holidays")]
    public async Task<IActionResult> ListHolidays()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var items = await _calendar.ListHolidaysAsync();
        return Ok(new { success = true, data = items });
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> UpsertHoliday([FromBody] UpsertBusinessHolidayRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "workflow.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình lịch nghỉ lễ." });

        var (item, err) = await _calendar.UpsertHolidayAsync(req);
        return err != null ? BadRequest(new { success = false, message = err }) : Ok(new { success = true, data = item });
    }

    [HttpDelete("holidays/{id}")]
    public async Task<IActionResult> DeleteHoliday(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.HasPermission(user, "workflow.manage"))
            return StatusCode(403, new { success = false });

        var ok = await _calendar.DeleteHolidayAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }
}
