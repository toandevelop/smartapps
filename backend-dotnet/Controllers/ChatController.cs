using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ChatService _chat;
    private readonly TaskService _tasks;
    private readonly AuthService _auth;

    public ChatController(ChatService chat, TaskService tasks, AuthService auth)
    {
        _chat = chat;
        _tasks = tasks;
        _auth = auth;
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var list = await _chat.ListChatUsersAsync();
        return Ok(new { success = true, data = list });
    }

    [HttpGet("unread-summary")]
    public async Task<IActionResult> UnreadSummary()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var (unreadChatCount, pendingTaskCount, unreadNotifCount) = await _chat.GetUnreadSummaryAsync(user.Username);
        return Ok(new
        {
            success = true,
            data = new { unreadChatCount, pendingTaskCount, unreadNotifCount }
        });
    }

    [HttpGet("channels")]
    public async Task<IActionResult> ListChannels()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var list = await _chat.ListChannelsAsync(user.Username);
        return Ok(new { success = true, data = list });
    }

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateChannelRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "Tên nhóm chat là bắt buộc." });

        var created = await _chat.CreateChannelAsync(req, user.Username);
        return Ok(new { success = true, data = created });
    }

    [HttpPost("channels/direct")]
    public async Task<IActionResult> EnsureDirect([FromBody] CreateDirectChannelRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.TargetUsername))
            return BadRequest(new { success = false, message = "Vui lòng chọn người nhận." });

        var ch = await _chat.EnsureDirectChannelAsync(user.Username, req.TargetUsername.Trim());
        return Ok(new { success = true, data = ch });
    }

    [HttpPost("channels/{id}/members")]
    public async Task<IActionResult> AddMember(string id, [FromBody] Dictionary<string, string> body)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!body.TryGetValue("username", out var target) || string.IsNullOrWhiteSpace(target))
            return BadRequest(new { success = false, message = "username là bắt buộc." });

        await _chat.AddMemberAsync(id, target, user.Username);
        return Ok(new { success = true });
    }

    [HttpGet("channels/{id}/messages")]
    public async Task<IActionResult> ListMessages(string id, [FromQuery] int limit = 80, [FromQuery] string? before = null)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        try
        {
            var page = await _chat.ListMessagePageAsync(id, user.Username, limit, before);
            return Ok(new { success = true, data = page.Items, pagination = new { page.HasMore, page.NextCursor } });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("channels/{id}/messages")]
    public async Task<IActionResult> SendMessage(string id, [FromBody] SendChatMessageRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.Content) && (req.Attachments == null || req.Attachments.Count == 0))
            return BadRequest(new { success = false, message = "Nội dung tin nhắn không được trống." });

        var (msg, createdTask) = await _chat.SendMessageAsync(id, user.Username, req);
        return Ok(new { success = true, data = msg, createdTask });
    }

    [HttpPost("channels/{id}/tasks")]
    public async Task<IActionResult> AssignTaskInChannel(string id, [FromBody] CreateTaskRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, message = "Tiêu đề công việc là bắt buộc." });

        req.ChannelId = id;
        req.PostToChat = true;
        var (task, msgId) = await _tasks.CreateTaskAsync(req, user.Username);
        return Ok(new { success = true, data = new { task, messageId = msgId } });
    }

    [HttpPost("channels/{id}/read")]
    public async Task<IActionResult> MarkChannelRead(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        try { await _chat.MarkChannelReadAsync(id, user.Username); }
        catch (UnauthorizedAccessException ex)
        { return StatusCode(403, new { success = false, message = ex.Message }); }
        return Ok(new { success = true });
    }

    [HttpPost("messages/{messageId}/convert-to-task")]
    public async Task<IActionResult> ConvertToTask(string messageId, [FromBody] ConvertMessageToTaskRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var result = await _chat.ConvertMessageToTaskAsync(messageId, user.Username, req);
        if (result == null) return NotFound(new { success = false, message = "Không tìm thấy tin nhắn." });
        return Ok(new { success = true, data = new { task = result.Value.Task, message = result.Value.Message } });
    }

    [HttpPost("messages/{messageId}/reactions")]
    public async Task<IActionResult> ToggleReaction(string messageId, [FromBody] ToggleReactionRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (string.IsNullOrWhiteSpace(req.Emoji))
            return BadRequest(new { success = false });

        var reactions = await _chat.ToggleReactionAsync(messageId, user.Username, req.Emoji.Trim());
        return reactions != null ? Ok(new { success = true, data = reactions }) : NotFound(new { success = false });
    }

    [HttpPatch("messages/{messageId}/pin")]
    public async Task<IActionResult> TogglePin(string messageId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var pinned = await _chat.TogglePinAsync(messageId);
        return pinned.HasValue ? Ok(new { success = true, data = new { isPinned = pinned.Value } }) : NotFound(new { success = false });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(string messageId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var isAdmin = AuthService.IsAdminUser(user);
        var ok = await _chat.DeleteMessageAsync(messageId, user.Username, isAdmin);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false });
    }
}
