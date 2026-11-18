using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private const string TasksAppCode = "TASKS";
    private const string TasksTableId = "tbl_tasks_congviec";

    private readonly TaskService _tasks;
    private readonly AuthService _auth;

    public TasksController(TaskService tasks, AuthService auth)
    {
        _tasks = tasks;
        _auth = auth;
    }

    private static bool IsTaskMember(AppTaskDto task, string username) =>
        task.AssigneeUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
        || task.AssignerUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
        || task.Watchers.Any(w => w.Equals(username, StringComparison.OrdinalIgnoreCase));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? scope = "all",
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? channelId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool personalMode = false)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.CanAccessTable(user, TasksAppCode, TasksTableId, "view"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập App Giao việc & Tiến độ." });

        var ownerOnly = AuthService.IsOwnerScopedForTable(user, TasksAppCode, TasksTableId, "OWNER_ONLY", personalMode);
        var list = await _tasks.ListTasksAsync(user.Username, scope, status, priority, channelId, search, ownerOnly);
        return Ok(new { success = true, data = list });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.CanAccessTable(user, TasksAppCode, TasksTableId, "view"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xem công việc." });

        var task = await _tasks.GetTaskByIdAsync(id);
        if (task == null) return NotFound(new { success = false });
        if (AuthService.IsOwnerScopedForTable(user, TasksAppCode, TasksTableId, "OWNER_ONLY", false)
            && !IsTaskMember(task, user.Username))
        {
            return StatusCode(403, new { success = false, message = "Bạn chỉ được xem công việc liên quan đến mình." });
        }
        return Ok(new { success = true, data = task });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.CanAccessTable(user, TasksAppCode, TasksTableId, "create"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền giao việc hoặc tạo công việc mới." });
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, message = "Tiêu đề công việc là bắt buộc." });

        var (task, msgId) = await _tasks.CreateTaskAsync(req, user.Username);
        return Ok(new { success = true, data = task, chatMessageId = msgId });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTaskRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.CanAccessTable(user, TasksAppCode, TasksTableId, "edit"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền cập nhật công việc." });

        var existing = await _tasks.GetTaskByIdAsync(id);
        if (existing == null) return NotFound(new { success = false });
        if (AuthService.IsOwnerScopedForTable(user, TasksAppCode, TasksTableId, "OWNER_ONLY", false)
            && !IsTaskMember(existing, user.Username))
        {
            return StatusCode(403, new { success = false, message = "Bạn chỉ được cập nhật công việc liên quan đến mình." });
        }

        var updated = await _tasks.UpdateTaskAsync(id, req, user.Username);
        return updated != null ? Ok(new { success = true, data = updated }) : NotFound(new { success = false });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.CanAccessTable(user, TasksAppCode, TasksTableId, "delete"))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xóa công việc trên App Giao việc & Tiến độ." });

        var existing = await _tasks.GetTaskByIdAsync(id);
        if (existing == null) return NotFound(new { success = false });

        var canDeleteAny = AuthService.IsAdminUser(user)
            || AuthService.HasAppPermission(user, TasksAppCode, "app.admin")
            || !AuthService.IsOwnerScopedForTable(user, TasksAppCode, TasksTableId, "OWNER_ONLY", false);
        if (!canDeleteAny && !existing.AssignerUsername.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { success = false, message = "Chỉ người giao việc hoặc Quản trị mới được xóa công việc này." });
        }

        var ok = await _tasks.DeleteTaskAsync(id, user.Username);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }
}
