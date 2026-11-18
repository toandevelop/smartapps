using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public AuthController(AuthService auth, IWebHostEnvironment env, IConfiguration config)
    {
        _auth = auth;
        _env = env;
        _config = config;
    }

    private bool IsDemoModeAllowed() =>
        _env.IsDevelopment() || _config.GetValue<bool>("Auth:AllowDemoSwitch");

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { success = false, message = "Vui lòng nhập tên đăng nhập và mật khẩu." });

        var (user, token, err) = await _auth.LoginAsync(req.Username, req.Password);
        if (user == null || token == null)
            return Unauthorized(new { success = false, message = err ?? "Sai tên đăng nhập hoặc mật khẩu." });

        Response.Cookies.Append("smartapps_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = _auth.GetTokenExpiration(token)
        });

        return Ok(new { success = true, data = new { user, token } });
    }

    [HttpPost("demo-switch")]
    public async Task<IActionResult> DemoSwitch([FromBody] Dictionary<string, string> body)
    {
        if (!IsDemoModeAllowed())
            return StatusCode(403, new { success = false, message = "Chế độ Demo Switch đã bị vô hiệu hóa trên môi trường Production." });

        var username = body.TryGetValue("username", out var u) ? u : "admin";
        var user = await _auth.GetByUsernameAsync(username);
        if (user == null) return NotFound(new { success = false, message = "Không tìm thấy tài khoản." });

        var token = await _auth.CreateTokenAsync(user);
        Response.Cookies.Append("smartapps_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = _auth.GetTokenExpiration(token)
        });
        return Ok(new { success = true, data = new { user, token } });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies["smartapps_token"]
            ?? Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
        await _auth.RevokeTokenAsync(token);
        Response.Cookies.Delete("smartapps_token", new CookieOptions { Path = "/", Secure = Request.IsHttps });
        return Ok(new { success = true });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var (ok, err) = await _auth.ChangePasswordAsync(user.Id, req.CurrentPassword, req.NewPassword);
        if (!ok) return BadRequest(new { success = false, message = err });
        return Ok(new { success = true, message = "Đã đổi mật khẩu thành công!" });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null)
            return Unauthorized(new { success = false, message = "Phiên đăng nhập chưa hợp lệ hoặc đã hết hạn." });
        return Ok(new { success = true, data = user });
    }

    [HttpGet("demo-accounts")]
    public async Task<IActionResult> DemoAccounts()
    {
        if (!IsDemoModeAllowed())
            return Ok(new { success = true, data = Array.Empty<object>(), disabled = true });

        var users = await _auth.ListUsersAsync();
        return Ok(new
        {
            success = true,
            data = users.Where(u => u.IsActive).Select(u => new
            {
                u.Username,
                u.FullName,
                u.RoleCode,
                u.RoleCodes,
                u.RoleNames,
                u.Department,
                u.AllowedApps
            })
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(current, "studio.manage") && !AuthService.HasPermission(current, "user.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xem danh sách người dùng." });

        var users = await _auth.ListUsersAsync();
        return Ok(new { success = true, data = users });
    }

    [HttpPost("users")]
    public async Task<IActionResult> UpsertUser([FromBody] UpsertUserRequest req)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(current, "studio.manage") && !AuthService.HasPermission(current, "user.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền quản lý người dùng." });

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.FullName))
            return BadRequest(new { success = false, message = "Username và Họ tên là bắt buộc." });

        var saved = await _auth.UpsertUserAsync(req);
        return Ok(new { success = true, data = saved });
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] Dictionary<string, string>? body)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(current, "studio.manage") && !AuthService.HasPermission(current, "user.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền đặt lại mật khẩu." });

        var newPw = body != null && body.TryGetValue("newPassword", out var p) && !string.IsNullOrWhiteSpace(p) ? p : "123456";
        var ok = await _auth.ResetPasswordAsync(id, newPw);
        return ok ? Ok(new { success = true, message = $"Đã đặt lại mật khẩu về '{newPw}'." }) : NotFound(new { success = false });
    }

    [HttpPatch("users/{id}/status")]
    public async Task<IActionResult> SetUserStatus(string id, [FromBody] Dictionary<string, bool> body)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(current, "studio.manage") && !AuthService.HasPermission(current, "user.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền quản lý người dùng." });

        var active = body.TryGetValue("isActive", out var v) && v;
        var (ok, err) = await _auth.SetUserActiveAsync(id, active, current.Id);
        return ok ? Ok(new { success = true, message = active ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản." })
                  : BadRequest(new { success = false, message = err });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var current = await _auth.ResolveRequestUserAsync(Request);
        if (current == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!AuthService.HasPermission(current, "studio.manage") && !AuthService.HasPermission(current, "user.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa người dùng." });

        var (ok, err) = await _auth.DeleteUserAsync(id, current.Id);
        return ok ? Ok(new { success = true, message = "Đã xóa tài khoản." })
                  : BadRequest(new { success = false, message = err });
    }
}
