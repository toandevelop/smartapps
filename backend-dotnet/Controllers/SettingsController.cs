using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;
    private readonly AuthService _auth;
    private readonly ExternalNotificationService _external;
    private readonly AiService _ai;
    private readonly RealtimeEventService _realtime;
    private readonly PlatformMetricsService _metrics;

    public SettingsController(
        SettingsService settings,
        AuthService auth,
        ExternalNotificationService external,
        AiService ai,
        RealtimeEventService realtime,
        PlatformMetricsService metrics)
    {
        _settings = settings;
        _auth = auth;
        _external = external;
        _ai = ai;
        _realtime = realtime;
        _metrics = metrics;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMySettings()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var settings = await _settings.GetUserSettingsAsync(user.Id);
        return Ok(new { success = true, data = new { user, settings } });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMySettings([FromBody] UpdateUserSettingsRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var saved = await _settings.UpdateUserSettingsAsync(user.Id, req);
        return Ok(new { success = true, data = saved });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (string.IsNullOrWhiteSpace(req.FullName))
            return BadRequest(new { success = false, message = "Họ và tên không được để trống." });

        var updated = await _settings.UpdateProfileAsync(user.Id, user.Username, req);
        return Ok(new { success = true, data = updated });
    }

    [HttpGet("system")]
    public async Task<IActionResult> GetSystemSettings()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var sys = await _settings.GetSystemSettingsAsync();
        return Ok(new { success = true, data = sys });
    }

    [HttpPut("system")]
    public async Task<IActionResult> UpdateSystemSettings([FromBody] SystemSettingsDto req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được thay đổi cấu hình hệ thống." });

        var saved = await _settings.UpdateSystemSettingsAsync(req, user.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpGet("notification-channels")]
    public async Task<IActionResult> ListNotificationChannels()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "studio.manage");
        var channels = await _external.ListChannelsForUserAsync(user.Id, isAdmin);
        return Ok(new { success = true, data = channels });
    }

    [HttpPost("notification-channels")]
    public async Task<IActionResult> UpsertNotificationChannel([FromBody] UpsertNotificationChannelRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "studio.manage");
        var (saved, err) = await _external.UpsertChannelAsync(req, user.Id, isAdmin);
        if (saved == null) return BadRequest(new { success = false, message = err });
        return Ok(new { success = true, data = saved });
    }

    [HttpDelete("notification-channels/{id}")]
    public async Task<IActionResult> DeleteNotificationChannel(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "studio.manage");
        var (ok, err) = await _external.DeleteChannelAsync(id, user.Id, isAdmin);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpPost("notification-channels/{id}/test")]
    public async Task<IActionResult> TestNotificationChannel(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var isAdmin = AuthService.IsAdminUser(user) || AuthService.HasPermission(user, "studio.manage");
        var (ok, detail) = await _external.TestChannelAsync(id, user.Id, isAdmin);
        return ok
            ? Ok(new { success = true, message = detail })
            : BadRequest(new { success = false, message = detail });
    }

    [HttpGet("notification-channels/logs")]
    public async Task<IActionResult> ListDeliveryLogs([FromQuery] int limit = 20)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var logs = await _external.ListDeliveryLogsAsync(limit);
        return Ok(new { success = true, data = logs });
    }

    [HttpGet("ai/status")]
    public async Task<IActionResult> GetAiStatus()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var status = await _ai.GetAiStatusAsync();
        var capabilities = AiService.GetCapabilityCatalog();
        return Ok(new { success = true, data = new { status, capabilities } });
    }

    [HttpGet("ai")]
    public async Task<IActionResult> GetAiConfig()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được xem cấu hình API AI." });

        var settings = await _ai.GetAiSystemSettingsAsync();
        var providers = await _ai.ListProvidersAsync();
        var capabilities = AiService.GetCapabilityCatalog();
        var logs = await _ai.ListLogsAsync(25);
        return Ok(new { success = true, data = new { settings, providers, capabilities, logs } });
    }

    [HttpPut("ai")]
    public async Task<IActionResult> UpdateAiSystemSettings([FromBody] AiSystemSettingsDto req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được thay đổi cấu hình AI hệ thống." });

        var saved = await _ai.UpdateAiSystemSettingsAsync(req, user.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpPost("ai/providers")]
    public async Task<IActionResult> UpsertAiProvider([FromBody] UpsertAiProviderRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được cấu hình nhà cung cấp AI." });

        var (saved, err) = await _ai.UpsertProviderAsync(req, user.Username);
        if (saved == null) return BadRequest(new { success = false, message = err });
        return Ok(new { success = true, data = saved });
    }

    [HttpPost("ai/providers/{id}/default")]
    public async Task<IActionResult> SetDefaultAiProvider(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được đặt nhà cung cấp AI mặc định." });

        var (ok, err) = await _ai.SetDefaultProviderAsync(id, user.Username);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpDelete("ai/providers/{id}")]
    public async Task<IActionResult> DeleteAiProvider(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được xóa cấu hình AI." });

        var (ok, err) = await _ai.DeleteProviderAsync(id);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpPost("ai/providers/{id}/test")]
    public async Task<IActionResult> TestAiProvider(string id, [FromBody] AiTestProviderRequest? req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được kiểm tra kết nối API AI." });

        var result = await _ai.TestProviderAsync(id, req, user.Username);
        return result.Ok
            ? Ok(new { success = true, data = result, message = result.Message })
            : BadRequest(new { success = false, data = result, message = result.Message });
    }

    [HttpGet("ai/logs")]
    public async Task<IActionResult> ListAiLogs([FromQuery] int limit = 25)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được xem nhật ký AI." });

        var logs = await _ai.ListLogsAsync(limit);
        return Ok(new { success = true, data = logs });
    }

    [HttpGet("cluster-status")]
    public async Task<IActionResult> GetClusterStatus(CancellationToken ct)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được xem trạng thái cụm HA." });

        await _realtime.UpsertClusterHeartbeatAsync(ct);
        var nodes = await _realtime.GetClusterNodesAsync(ct);
        var metrics = await _metrics.GetSnapshotAsync(ct);
        return Ok(new
        {
            success = true,
            data = new
            {
                currentInstanceId = _realtime.InstanceId,
                listenerConnected = _realtime.IsListenerConnected,
                lastListenerConnectedAt = _realtime.LastListenerConnectedAt,
                nodes,
                metrics
            }
        });
    }
}
