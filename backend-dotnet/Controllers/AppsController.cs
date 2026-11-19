using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api/apps")]
public class AppsController : ControllerBase
{
    private readonly MetadataService _metadata;
    private readonly AuthService _auth;
    private readonly AppUpgradeService _upgrader;

    public AppsController(MetadataService metadata, AuthService auth, AppUpgradeService upgrader)
    {
        _metadata = metadata;
        _auth = auth;
        _upgrader = upgrader;
    }

    [HttpGet]
    public async Task<IActionResult> ListApps()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var apps = await _metadata.GetAppsAsync();
        var visible = apps.Where(a => AuthService.CanAccessApp(user, a.Code)).ToList();
        return Ok(new { success = true, data = visible });
    }

    [HttpGet("templates")]
    public async Task<IActionResult> ListTemplates()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        return Ok(new { success = true, data = AppTemplateCatalog.GetSummaries() });
    }

    [HttpGet("upgrade-v2/preview")]
    public async Task<IActionResult> PreviewUpgradeV2()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được xem trước nâng cấp v2.0." });

        var list = await _upgrader.PreviewUpgradeAsync();
        return Ok(new { success = true, data = list });
    }

    [HttpPost("upgrade-v2")]
    public async Task<IActionResult> UpgradeAllV2([FromBody] AppUpgradeRequest? req = null)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được nâng cấp ứng dụng lên v2.0." });

        var results = await _upgrader.UpgradeAllAppsToV2Async(req?.AppIds, user.Username);
        return Ok(new { success = true, data = results });
    }

    [HttpPost("{codeOrId}/upgrade-v2")]
    public async Task<IActionResult> UpgradeSingleV2(string codeOrId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được nâng cấp ứng dụng lên v2.0." });

        try
        {
            var res = await _upgrader.UpgradeAppToV2Async(codeOrId, user.Username);
            return Ok(new { success = true, data = res });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{codeOrId}")]
    public async Task<IActionResult> GetAppRuntime(string codeOrId, [FromQuery] bool previewDraft = false)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var allowDraft = previewDraft && AuthService.HasPermission(user, "studio.manage");
        var tree = await _metadata.GetAppRuntimeTreeAsync(codeOrId, usePublishedSnapshot: !allowDraft);
        if (tree == null) return NotFound(new { success = false, message = "Không tìm thấy ứng dụng." });
        if (!AuthService.CanAccessApp(user, tree.App.Code))
            return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập ứng dụng này." });

        // Per-Table visibility & Field-Level Security for non-admin users
        if (!AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            foreach (var mod in tree.Modules)
            {
                mod.Tables = mod.Tables
                    .Where(t => AuthService.CanAccessTable(user, tree.App.Code, t.Table.Id, "view"))
                    .ToList();
                foreach (var tbl in mod.Tables)
                {
                    tbl.Columns = tbl.Columns
                        .Where(c => !c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                        .ToList();
                }
            }
            tree.Modules = tree.Modules.Where(m => m.Tables.Count > 0).ToList();
        }

        return Ok(new { success = true, data = tree });
    }

    [HttpPost]
    public async Task<IActionResult> UpsertApp([FromBody] UpsertAppRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được tạo hoặc sửa App." });

        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { success = false, message = "Mã App và Tên App là bắt buộc." });

        var app = await _metadata.UpsertAppAsync(req);
        return Ok(new { success = true, data = app });
    }

    [HttpPost("{appId}/validate-publish")]
    public async Task<IActionResult> ValidatePublish(string appId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false });

        var validation = await _metadata.ValidateAppForPublishAsync(appId);
        return Ok(new { success = true, data = validation });
    }

    [HttpPost("{appId}/publish")]
    public async Task<IActionResult> PublishApp(string appId, [FromBody] PublishAppRequest? req = null)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được phát hành App." });

        try
        {
            var release = await _metadata.PublishAppAsync(appId, user.Username, req?.Notes);
            return Ok(new { success = true, data = release });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{appId}/releases")]
    public async Task<IActionResult> ListReleases(string appId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false });

        var releases = await _metadata.GetAppReleasesAsync(appId);
        return Ok(new { success = true, data = releases });
    }

    [HttpGet("{appId}/releases/diff")]
    public async Task<IActionResult> GetReleaseDiff(string appId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false });

        var diff = await _metadata.ComputeSchemaDiffAsync(appId);
        return diff != null ? Ok(new { success = true, data = diff }) : NotFound(new { success = false });
    }

    [HttpPost("{appId}/releases/{versionNumber:int}/rollback")]
    public async Task<IActionResult> RollbackRelease(string appId, int versionNumber)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Chỉ Quản trị/Studio mới được khôi phục phiên bản App." });

        var release = await _metadata.RollbackAppReleaseAsync(appId, versionNumber, user.Username);
        return release != null ? Ok(new { success = true, data = release }) : NotFound(new { success = false });
    }

    [HttpPost("from-excel")]
    public async Task<IActionResult> CreateFromExcel([FromBody] CreateAppFromExcelRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền tạo App từ Excel." });

        if (string.IsNullOrWhiteSpace(req.AppCode) || string.IsNullOrWhiteSpace(req.AppName) || req.Sheets.Count == 0)
            return BadRequest(new { success = false, message = "Thiếu Mã App, Tên App hoặc dữ liệu sheet Excel." });

        var app = await _metadata.CreateAppFromExcelAsync(req);
        return Ok(new { success = true, data = app });
    }

    [HttpPost("from-template")]
    public async Task<IActionResult> CreateFromTemplate([FromBody] CreateAppFromTemplateRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền cài App mẫu." });

        if (string.IsNullOrWhiteSpace(req.TemplateKey))
            return BadRequest(new { success = false, message = "TemplateKey là bắt buộc." });

        try
        {
            var app = await _metadata.CreateAppFromTemplateAsync(req);
            return Ok(new { success = true, data = app });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{codeOrId}/export")]
    public async Task<IActionResult> ExportBlueprint(string codeOrId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xuất Blueprint." });

        var bp = await _metadata.ExportAppBlueprintAsync(codeOrId);
        if (bp == null) return NotFound(new { success = false });
        return Ok(new { success = true, data = bp });
    }

    [HttpPost("import-blueprint")]
    public async Task<IActionResult> ImportBlueprint([FromBody] AppBlueprintDto blueprint, [FromQuery] string? code, [FromQuery] string? name)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền nhập Blueprint." });

        var app = await _metadata.ImportAppBlueprintAsync(blueprint, code, name);
        return Ok(new { success = true, data = app });
    }

    [HttpPost("{codeOrId}/upgrade-blueprint")]
    public async Task<IActionResult> UpgradeBlueprint(string codeOrId, [FromBody] AppBlueprintDto blueprint)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền nâng cấp Blueprint." });

        var res = await _metadata.UpgradeAppFromBlueprintAsync(codeOrId, blueprint);
        if (res == null) return NotFound(new { success = false, message = "Không tìm thấy Ứng dụng đích để nâng cấp." });
        return Ok(new { success = true, data = res });
    }

    [HttpPost("ai-generate")]
    public async Task<IActionResult> GenerateFromAiPrompt([FromBody] AiGenerateAppRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền sinh App tự động." });

        if (string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { success = false, message = "Vui lòng nhập mô tả nghiệp vụ cần tạo App." });

        try
        {
            var app = await _metadata.GenerateAppFromPromptAsync(req, user.Username);
            return Ok(new { success = true, data = app });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteApp(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage"))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa App." });

        var ok = await _metadata.DeleteAppAsync(id);
        return ok
            ? Ok(new { success = true })
            : NotFound(new { success = false, message = "Không tìm thấy ứng dụng." });
    }

    [HttpGet("engine-status")]
    public IActionResult EngineStatus()
    {
        return Ok(new
        {
            success = true,
            data = new
            {
                dotnet = new { online = true, version = "9.0", service = "SmartApps.Api" },
                desktop = new { role = "Rust Tauri Desktop App", path = "desktop-rust/" }
            }
        });
    }
}
