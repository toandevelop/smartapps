using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;
    private readonly AuthService _auth;
    private readonly RealtimeEventService _realtime;

    public NotificationsController(
        NotificationService notifications,
        AuthService auth,
        RealtimeEventService realtime)
    {
        _notifications = notifications;
        _auth = auth;
        _realtime = realtime;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 25,
        [FromQuery] string? category = null,
        [FromQuery] bool unreadOnly = false)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var (items, unreadCount) = await _notifications.ListForUserAsync(user.Username, limit, category, unreadOnly);
        return Ok(new { success = true, data = new { items, unreadCount } });
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (subId, reader) = _realtime.Subscribe(user.Username);
        try
        {
            await Response.WriteAsync("event: connected\ndata: {\"ok\":true}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeatCts.CancelAfter(TimeSpan.FromSeconds(25));
                bool hasData;
                try
                {
                    hasData = await reader.WaitToReadAsync(heartbeatCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await Response.WriteAsync(": ping\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }

                if (!hasData) break;
                while (reader.TryRead(out var ev))
                {
                    var json = RealtimeEventService.SerializeEvent(ev);
                    await Response.WriteAsync($"event: platform\ndata: {json}\n\n", ct);
                }
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal client disconnect
        }
        catch (IOException)
        {
            // Client aborted connection mid-stream
        }
        finally
        {
            _realtime.Unsubscribe(subId);
        }
    }

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        await _notifications.MarkReadAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        await _notifications.MarkAllReadAsync(user.Username);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var isAdmin = AuthService.IsAdminUser(user);
        var ok = await _notifications.DeleteAsync(id, user.Username, isAdmin);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "Không thể xóa thông báo chung hoặc không tìm thấy." });
    }

    [HttpPost("clear-read")]
    public async Task<IActionResult> ClearRead()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        var removed = await _notifications.ClearReadAsync(user.Username);
        return Ok(new { success = true, removed });
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastNotificationRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được gửi thông báo toàn hệ thống." });

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, message = "Tiêu đề thông báo là bắt buộc." });

        var id = await _notifications.CreateAsync(
            req.Title.Trim(),
            req.Message?.Trim() ?? string.Empty,
            null,
            null,
            string.IsNullOrWhiteSpace(req.RecipientUsername) ? "*" : req.RecipientUsername.Trim(),
            string.IsNullOrWhiteSpace(req.Category) ? "SYSTEM" : req.Category.Trim().ToUpperInvariant(),
            req.LinkUrl,
            user.Username,
            string.IsNullOrWhiteSpace(req.Priority) ? "NORMAL" : req.Priority.Trim().ToUpperInvariant());

        return Ok(new { success = true, data = new { id } });
    }
}
