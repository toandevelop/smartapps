using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/backups")]
public class BackupsController : ControllerBase
{
    private readonly BackupService _backups;
    private readonly AuthService _auth;

    public BackupsController(BackupService backups, AuthService auth)
    {
        _backups = backups;
        _auth = auth;
    }

    private async Task<(SysUserDto? User, IActionResult? Err)> RequireAdminAsync()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return (null, Unauthorized(new { success = false }));
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return (null, StatusCode(403, new { success = false, message = "Chỉ Quản trị viên mới được quản lý sao lưu." }));
        return (user, null);
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        var (_, err) = await RequireAdminAsync();
        if (err != null) return err;
        return Ok(new { success = true, data = await _backups.GetConfigAsync() });
    }

    [HttpPost("config")]
    public async Task<IActionResult> UpsertConfig([FromBody] UpsertBackupConfigRequest req)
    {
        var (user, err) = await RequireAdminAsync();
        if (err != null) return err;
        var saved = await _backups.UpsertConfigAsync(req, user!.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery] int limit = 30)
    {
        var (_, err) = await RequireAdminAsync();
        if (err != null) return err;
        return Ok(new { success = true, data = await _backups.ListRunsAsync(limit) });
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunNow()
    {
        var (user, err) = await RequireAdminAsync();
        if (err != null) return err;
        var run = await _backups.RunBackupAsync("MANUAL", user!.Username);
        return Ok(new { success = run.Status == "SUCCEEDED", data = run });
    }

    [HttpGet("runs/{id}/download")]
    public async Task<IActionResult> Download(string id)
    {
        var (_, err) = await RequireAdminAsync();
        if (err != null) return err;

        var run = await _backups.GetRunByIdAsync(id);
        if (run == null || string.IsNullOrWhiteSpace(run.LocalPath) || !System.IO.File.Exists(run.LocalPath))
            return NotFound(new { success = false, message = "Không tìm thấy tệp sao lưu cục bộ." });

        return PhysicalFile(run.LocalPath, "application/zip", run.FileName);
    }
}
