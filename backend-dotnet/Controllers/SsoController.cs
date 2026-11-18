using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/auth/sso")]
public class SsoController : ControllerBase
{
    private readonly SsoService _sso;
    private readonly AuthService _auth;

    public SsoController(SsoService sso, AuthService auth)
    {
        _sso = sso;
        _auth = auth;
    }

    [HttpGet("providers")]
    public async Task<IActionResult> ListPublicProviders()
    {
        var list = await _sso.ListPublicProvidersAsync();
        return Ok(new { success = true, data = list });
    }

    [HttpGet("admin/providers")]
    public async Task<IActionResult> ListAdminProviders()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageSso(user))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình SSO hệ thống." });

        var list = await _sso.ListAdminProvidersAsync();
        return Ok(new { success = true, data = list });
    }

    [HttpPost("admin/providers")]
    public async Task<IActionResult> UpsertProvider([FromBody] UpsertSsoProviderRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageSso(user))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị hệ thống mới được cấu hình SSO." });

        var (saved, err) = await _sso.UpsertProviderAsync(req);
        if (saved == null) return BadRequest(new { success = false, message = err });
        return Ok(new { success = true, data = saved });
    }

    [HttpDelete("admin/providers/{id}")]
    public async Task<IActionResult> DeleteProvider(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageSso(user))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa nhà cung cấp SSO." });

        var (ok, err) = await _sso.DeleteProviderAsync(id);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = err });
    }

    [HttpPost("admin/discover")]
    public async Task<IActionResult> DiscoverOidc([FromBody] OidcDiscoveryRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (!CanManageSso(user))
            return StatusCode(403, new { success = false, message = "Không có quyền dò cấu hình OIDC." });

        var (result, err) = await _sso.DiscoverOidcAsync(req.IssuerUrl);
        if (result == null) return BadRequest(new { success = false, message = err });
        return Ok(new { success = true, data = result });
    }

    [HttpGet("start/{code}")]
    public async Task<IActionResult> Start(
        string code,
        [FromQuery] string? returnUrl = "/",
        [FromQuery] string? redirectOrigin = null)
    {
        var origin = ResolveSafeOrigin(redirectOrigin);
        var redirectUri = $"{origin}/api/auth/sso/callback/{Uri.EscapeDataString(code.Trim().ToUpperInvariant())}";

        var (authorizeUrl, err) = await _sso.BeginAuthorizationAsync(code, redirectUri, returnUrl);
        if (authorizeUrl == null)
        {
            return Redirect($"/login?sso_error={Uri.EscapeDataString(err ?? "Không thể khởi tạo đăng nhập SSO.")}");
        }
        return Redirect(authorizeUrl);
    }

    [HttpGet("callback/{code}")]
    public async Task<IActionResult> Callback(
        string code,
        [FromQuery(Name = "code")] string? authCode,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        var (user, token, returnUrl, err) = await _sso.CompleteCallbackAsync(code, authCode, state, error, errorDescription);
        if (user == null || token == null)
        {
            return Redirect($"/login?sso_error={Uri.EscapeDataString(err ?? "Xác thực SSO thất bại.")}");
        }

        Response.Cookies.Append("smartapps_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = _auth.GetTokenExpiration(token)
        });

        return Redirect(string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl);
    }

    private static bool CanManageSso(SysUserDto user) =>
        AuthService.IsAdminUser(user)
        || AuthService.HasPermission(user, "studio.manage")
        || AuthService.HasPermission(user, "role.manage")
        || AuthService.HasPermission(user, "user.manage");

    private string ResolveSafeOrigin(string? requestedOrigin)
    {
        var fwdHost = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var fwdProto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(requestedOrigin)
            && Uri.TryCreate(requestedOrigin.Trim().TrimEnd('/'), UriKind.Absolute, out var reqUri)
            && (reqUri.Scheme == Uri.UriSchemeHttp || reqUri.Scheme == Uri.UriSchemeHttps))
        {
            var isLocal = reqUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                          || reqUri.Host.Equals("127.0.0.1");
            var matchesHost = reqUri.Host.Equals(Request.Host.Host, StringComparison.OrdinalIgnoreCase)
                              || (!string.IsNullOrWhiteSpace(fwdHost) && fwdHost.Contains(reqUri.Host, StringComparison.OrdinalIgnoreCase));
            if (isLocal || matchesHost)
            {
                return $"{reqUri.Scheme}://{reqUri.Authority}";
            }
        }

        if (!string.IsNullOrWhiteSpace(fwdHost))
        {
            var proto = string.IsNullOrWhiteSpace(fwdProto) ? Request.Scheme : fwdProto;
            return $"{proto}://{fwdHost}";
        }

        return $"{Request.Scheme}://{Request.Host}";
    }
}
