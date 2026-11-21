using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/store")]
public class AppStoreController(AppStoreService store, AuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("manifest.json")]
    public IActionResult GetPublicManifest()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var manifest = store.GetPublicManifest(baseUrl);
        return Ok(manifest);
    }

    [HttpGet("apps")]
    public async Task<IActionResult> ListApps(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? registryId = null)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var apps = await store.ListStoreAppsAsync(category, search, registryId);
        return Ok(new { success = true, data = apps });
    }

    [HttpGet("suites")]
    public async Task<IActionResult> ListSolutionSuites()
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var suites = await store.ListSolutionSuitesAsync();
        return Ok(new { success = true, data = suites });
    }

    [HttpPost("suites/{suiteCode}/install")]
    public async Task<IActionResult> InstallSolutionSuite(string suiteCode, [FromBody] InstallStoreAppRequest? req = null)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được cài đặt Bộ giải pháp." });

        try
        {
            var result = await store.InstallSolutionSuiteAsync(
                suiteCode,
                req?.IncludeSampleData ?? true,
                user.Username);
            return Ok(new { success = true, data = result });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("apps/{code}")]
    public async Task<IActionResult> GetAppDetail(string code, [FromQuery] string? registryId = null)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var detail = await store.GetStoreAppDetailAsync(code, registryId);
        return detail != null
            ? Ok(new { success = true, data = detail })
            : NotFound(new { success = false, message = "Không tìm thấy gói ứng dụng." });
    }

    [HttpGet("apps/{code}/download")]
    public async Task<IActionResult> DownloadAppPackage(string code, [FromQuery] string? registryId = null)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        var downloaded = await store.DownloadPackageAsync(code, registryId, user?.Username ?? "anonymous");
        if (downloaded == null)
            return NotFound(new { success = false, message = "Không tìm thấy gói ứng dụng để tải về." });

        return File(downloaded.Value.ContentBytes, "application/json; charset=utf-8", downloaded.Value.FileName);
    }

    [HttpGet("export-package/{codeOrId}")]
    public async Task<IActionResult> ExportInstalledPackage(string codeOrId)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được đóng gói xuất App." });

        var pkg = await store.ExportInstalledAppAsPackageAsync(codeOrId);
        if (pkg == null) return NotFound(new { success = false, message = "Không tìm thấy ứng dụng." });

        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pkg, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        });
        var fileName = $"smartapp-{pkg.PackageCode.ToLowerInvariant()}-v{pkg.Version}.smartapp.json";
        return File(bytes, "application/json; charset=utf-8", fileName);
    }

    [HttpPost("apps/{code}/install")]
    public async Task<IActionResult> InstallFromStore(string code, [FromBody] InstallStoreAppRequest? req = null)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được cài App từ Kho Online." });

        try
        {
            var app = await store.InstallStoreAppAsync(code, req ?? new InstallStoreAppRequest(), user.Username);
            return Ok(new { success = true, data = app });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, message = $"Không tải được gói từ kho online: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("install-package")]
    public async Task<IActionResult> InstallUploadedPackage([FromBody] InstallStorePackageBodyRequest req)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được cài gói App." });

        try
        {
            var app = await store.InstallUploadedPayloadAsync(req, user.Username);
            return Ok(new { success = true, data = app });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("install-from-url")]
    public async Task<IActionResult> InstallFromUrl([FromBody] InstallStorePackageUrlRequest req)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được cài gói App từ URL." });

        try
        {
            var app = await store.InstallFromUrlAsync(req, user.Username);
            return Ok(new { success = true, data = app });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, message = $"Không thể tải gói từ URL: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("registries")]
    public async Task<IActionResult> ListRegistries()
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var list = await store.ListRegistriesAsync();
        return Ok(new { success = true, data = list });
    }

    [HttpPost("registries")]
    public async Task<IActionResult> UpsertRegistry([FromBody] UpsertStoreRegistryRequest req)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được cấu hình nguồn Kho Online." });

        try
        {
            var reg = await store.UpsertAndSyncRegistryAsync(req, user.Username);
            return Ok(new { success = true, data = reg });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("registries/{id}/sync")]
    public async Task<IActionResult> SyncRegistry(string id)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được đồng bộ Kho Online." });

        try
        {
            var reg = await store.SyncRegistryAsync(id);
            return reg != null
                ? Ok(new { success = true, data = reg })
                : NotFound(new { success = false, message = "Không tìm thấy nguồn Kho Online." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPatch("registries/{id}/toggle")]
    public async Task<IActionResult> ToggleRegistry(string id)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được bật/tắt nguồn Kho Online." });

        var reg = await store.ToggleRegistryAsync(id);
        return reg != null
            ? Ok(new { success = true, data = reg })
            : NotFound(new { success = false, message = "Không tìm thấy nguồn Kho Online." });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 40)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var items = await store.ListHistoryAsync(limit);
        return Ok(new { success = true, data = items });
    }

    [HttpDelete("registries/{id}")]
    public async Task<IActionResult> DeleteRegistry(string id)
    {
        var user = await auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false });

        return await store.DeleteRegistryAsync(id)
            ? Ok(new { success = true })
            : NotFound(new { success = false });
    }
}
