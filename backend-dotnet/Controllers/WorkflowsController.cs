using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowService _workflows;
    private readonly AuthService _auth;

    public WorkflowsController(WorkflowService workflows, AuthService auth)
    {
        _workflows = workflows;
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
}
