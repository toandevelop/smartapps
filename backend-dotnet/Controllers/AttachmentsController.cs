using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

public sealed class UpsertQuotaRequest
{
    public string ScopeType { get; set; } = "GLOBAL";
    public string ScopeId { get; set; } = "*";
    public int QuotaMb { get; set; } = 1024;
}

public sealed class CreateShareRequest
{
    public int ExpiresInHours { get; set; } = 168;
    public bool AllowDownload { get; set; } = true;
}

[ApiController]
[Route("api/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly string _connStr;
    private readonly AuthService _auth;
    private readonly FileManagementService _files;
    private readonly IFileStorageAdapter _storage;

    public AttachmentsController(
        IConfiguration config,
        AuthService auth,
        FileManagementService files,
        IFileStorageAdapter storage)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _auth = auth;
        _files = files;
        _storage = storage;
    }

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".sh", ".html", ".htm", ".svg", ".dll"
    };

    [HttpPost]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string? appId,
        [FromForm] string? tableId,
        [FromForm] string? recordId)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn tệp đính kèm." });

        var safeExt = Path.GetExtension(file.FileName);
        if (!string.IsNullOrEmpty(safeExt) && BlockedExtensions.Contains(safeExt))
            return BadRequest(new { success = false, message = $"Định dạng tệp '{safeExt}' không được phép tải lên vì lý do bảo mật." });

        try
        {
            await _files.EnforceQuotaAsync(appId, user.Username, file.Length);
        }
        catch (StorageQuotaExceededException ex)
        {
            return StatusCode(413, new { success = false, message = ex.Message });
        }

        var id = $"att_{Guid.NewGuid().ToString("N")[..12]}";
        var storedName = $"{id}{safeExt}";
        await using (var stream = file.OpenReadStream())
        {
            await _storage.SaveAsync(storedName, stream, file.ContentType);
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_attachments (id, app_id, table_id, record_id, file_name, content_type, size_bytes, storage_path, uploaded_by)
            VALUES (@id, @app, @tbl, @rec, @fn, @ct, @sz, @sp, @ub)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", (object?)appId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tbl", (object?)tableId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rec", (object?)recordId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fn", file.FileName);
        cmd.Parameters.AddWithValue("ct", string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        cmd.Parameters.AddWithValue("sz", file.Length);
        cmd.Parameters.AddWithValue("sp", storedName);
        cmd.Parameters.AddWithValue("ub", user.Username);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                id,
                fileName = file.FileName,
                contentType = file.ContentType,
                sizeBytes = file.Length,
                url = $"/api/attachments/{id}"
            }
        });
    }

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? appId = null, [FromQuery] string? q = null, [FromQuery] int limit = 100)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false });

        var items = await _files.ListAttachmentsAsync(appId, q, limit);
        return Ok(new { success = true, data = items });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false });

        var stats = await _files.GetStorageStatsAsync();
        return Ok(new { success = true, data = stats });
    }

    [HttpPost("quotas")]
    public async Task<IActionResult> UpsertQuota([FromBody] UpsertQuotaRequest req)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Không có quyền cấu hình dung lượng." });

        var saved = await _files.UpsertQuotaAsync(req.ScopeType, req.ScopeId, req.QuotaMb, user.Username);
        return Ok(new { success = true, data = saved });
    }

    [HttpPost("cleanup-orphans")]
    public async Task<IActionResult> CleanupOrphans([FromQuery] int minAgeHours = 24)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });
        if (!AuthService.HasPermission(user, "studio.manage") && !AuthService.IsAdminUser(user))
            return StatusCode(403, new { success = false, message = "Không có quyền dọn tệp." });

        var (cleaned, freed) = await _files.CleanupOrphanAttachmentsAsync(_storage, minAgeHours);
        return Ok(new { success = true, data = new { cleanedCount = cleaned, freedBytes = freed, storageProvider = _storage.ProviderName } });
    }

    [HttpPost("{id}/share")]
    public async Task<IActionResult> CreateShare(string id, [FromBody] CreateShareRequest? req = null)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false });

        var share = await _files.CreateShareLinkAsync(id, req?.ExpiresInHours ?? 168, req?.AllowDownload ?? true, user.Username);
        return share != null ? Ok(new { success = true, data = share }) : NotFound(new { success = false });
    }

    [HttpGet("shared/{token}")]
    public async Task<IActionResult> DownloadShared(string token)
    {
        var resolved = await _files.ResolveShareTokenAsync(token);
        if (resolved == null) return NotFound(new { success = false, message = "Liên kết chia sẻ không tồn tại hoặc đã hết hạn." });

        var (fileName, contentType, storedName, allowDownload) = resolved.Value;
        var stream = await _storage.OpenReadAsync(storedName);
        if (stream == null) return NotFound();

        if (allowDownload)
            return File(stream, contentType, fileName);

        return File(stream, contentType);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Download(string id)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để tải tệp." });

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT a.file_name, a.content_type, a.storage_path, ap.code, a.table_id
            FROM app_attachments a
            LEFT JOIN sys_apps ap ON ap.id = a.app_id
            WHERE a.id = @id AND COALESCE(a.is_deleted, FALSE) = FALSE
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();

        var fileName = r.GetString(0);
        var contentType = r.GetString(1);
        var storedName = r.GetString(2);
        var appCode = r.IsDBNull(3) ? null : r.GetString(3);
        var tableId = r.IsDBNull(4) ? null : r.GetString(4);

        if (!string.IsNullOrWhiteSpace(appCode))
        {
            if (!AuthService.CanAccessApp(user, appCode))
                return StatusCode(403, new { success = false, message = $"Không có quyền tải tệp của App {appCode}." });
            if (!string.IsNullOrWhiteSpace(tableId) && !AuthService.CanAccessTable(user, appCode, tableId, "view"))
                return StatusCode(403, new { success = false, message = "Không có quyền tải tệp của bảng này." });
        }

        var stream = await _storage.OpenReadAsync(storedName);
        if (stream == null) return NotFound();

        return File(stream, contentType, fileName);
    }
}
