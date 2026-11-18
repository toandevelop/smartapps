using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api/public/forms")]
public class PublicFormsController : ControllerBase
{
    private readonly string _connStr;
    private readonly MetadataService _metadata;
    private readonly RecordService _records;
    private readonly NotificationService _notifications;
    private readonly FileManagementService _files;
    private readonly IFileStorageAdapter _storage;

    private static readonly ConcurrentDictionary<string, Queue<DateTime>> RateWindow = new();

    public PublicFormsController(
        IConfiguration config,
        MetadataService metadata,
        RecordService records,
        NotificationService notifications,
        FileManagementService files,
        IFileStorageAdapter storage)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _metadata = metadata;
        _records = records;
        _notifications = notifications;
        _files = files;
        _storage = storage;
    }

    private string ResolveClientIp()
    {
        var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd))
            return fwd.Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private bool CheckRateLimit(string bucket, int maxPerMinute)
    {
        var key = $"{bucket}:{ResolveClientIp()}";
        var q = RateWindow.GetOrAdd(key, _ => new Queue<DateTime>());
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        lock (q)
        {
            while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
            if (q.Count >= maxPerMinute) return false;
            q.Enqueue(DateTime.UtcNow);
            return true;
        }
    }

    private async Task<(SysTableDto? Table, IActionResult? ErrorResult)> ResolveAvailablePublicTableAsync(string tableId)
    {
        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (table == null || !table.PublicFormEnabled)
        {
            return (null, NotFound(new { success = false, message = "Biểu mẫu công khai không tồn tại hoặc chưa được kích hoạt." }));
        }
        if (table.PublicFormExpiresAt.HasValue && table.PublicFormExpiresAt.Value <= DateTime.UtcNow)
        {
            return (null, StatusCode(410, new { success = false, message = "Biểu mẫu công khai này đã hết hạn tiếp nhận hồ sơ." }));
        }
        if (table.PublicFormMaxSubmissions.HasValue && table.PublicFormMaxSubmissions.Value > 0 && table.RecordCount >= table.PublicFormMaxSubmissions.Value)
        {
            return (null, StatusCode(410, new { success = false, message = "Biểu mẫu đã đạt giới hạn số lượng hồ sơ tiếp nhận tối đa." }));
        }
        return (table, null);
    }

    [HttpGet("{tableId}")]
    public async Task<IActionResult> GetFormDefinition(string tableId)
    {
        var (table, err) = await ResolveAvailablePublicTableAsync(tableId);
        if (err != null || table == null) return err!;

        var app = await _metadata.GetAppByCodeOrIdAsync(table.AppId);
        var cols = (await _metadata.GetColumnsAsync(table.Id))
            .Where(c => c.ShowInForm && !c.HiddenForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase))
            .ToList();

        var categories = new Dictionary<string, List<SysCategoryItemDto>>();
        foreach (var code in cols.Where(c => !string.IsNullOrWhiteSpace(c.RefCategoryCode)).Select(c => c.RefCategoryCode!).Distinct())
        {
            categories[code] = await _metadata.GetCategoryItemsAsync(code);
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                app = app == null ? null : new { app.Id, app.Code, app.Name, app.Color, app.Department },
                table = new
                {
                    table.Id,
                    table.DisplayName,
                    table.Description,
                    table.AppId,
                    table.ModuleId,
                    table.FormRules,
                    table.PublicFormExpiresAt,
                    table.PublicFormMaxSubmissions,
                    table.RecordCount
                },
                columns = cols,
                categories
            }
        });
    }

    [HttpGet("{tableId}/lookup-options")]
    public async Task<IActionResult> GetPublicLookupOptions(
        string tableId,
        [FromQuery] string columnCode,
        [FromQuery] string? q = null,
        [FromQuery] string? filterValue = null)
    {
        var (table, err) = await ResolveAvailablePublicTableAsync(tableId);
        if (err != null || table == null) return err!;

        if (string.IsNullOrWhiteSpace(columnCode))
            return BadRequest(new { success = false, message = "columnCode là bắt buộc." });

        var cols = await _metadata.GetColumnsAsync(table.Id);
        var col = cols.FirstOrDefault(c =>
            c.ColumnCode.Equals(columnCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            c.ShowInForm &&
            !c.HiddenForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(c.RefTableId));

        if (col == null)
            return StatusCode(403, new { success = false, message = "Cột liên kết không khả dụng trên biểu mẫu công khai." });

        var items = await _records.GetLookupOptionsAsync(
            col.RefTableId!,
            col.RefValueField,
            col.RefDisplayField,
            q,
            user: null,
            filterField: col.LookupFilterField,
            filterValue: filterValue);
        return Ok(new { success = true, data = items });
    }

    [HttpGet("{tableId}/track/{recordKey}")]
    public async Task<IActionResult> TrackSubmission(string tableId, string recordKey)
    {
        if (!CheckRateLimit("track", 30))
            return StatusCode(429, new { success = false, message = "Bạn tra cứu quá nhanh. Vui lòng thử lại sau 1 phút." });

        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (table == null || !table.PublicFormEnabled)
            return NotFound(new { success = false, message = "Biểu mẫu công khai không tồn tại." });

        if (string.IsNullOrWhiteSpace(recordKey))
            return BadRequest(new { success = false, message = "Vui lòng nhập mã tiếp nhận." });

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT record_key, COALESCE(approval_status, 'DRAFT'), current_step_name, created_at, updated_at
            FROM app_records
            WHERE table_id = @tbl
              AND deleted_at IS NULL
              AND (UPPER(COALESCE(record_key, '')) = UPPER(@key) OR UPPER(id) = UPPER(@key))
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tbl", table.Id);
        cmd.Parameters.AddWithValue("key", recordKey.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return NotFound(new { success = false, message = "Không tìm thấy hồ sơ với mã tiếp nhận này." });

        return Ok(new
        {
            success = true,
            data = new
            {
                recordKey = r.IsDBNull(0) ? recordKey.Trim() : r.GetString(0),
                approvalStatus = r.GetString(1),
                currentStepName = r.IsDBNull(2) ? null : r.GetString(2),
                createdAt = r.GetDateTime(3),
                updatedAt = r.GetDateTime(4)
            }
        });
    }

    [HttpPost("{tableId}/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadPublicAttachment(string tableId, [FromForm] IFormFile file)
    {
        if (!CheckRateLimit("upload", 15))
            return StatusCode(429, new { success = false, message = "Quá giới hạn tải tệp cho phép. Vui lòng thử lại sau 1 phút." });

        var (table, err) = await ResolveAvailablePublicTableAsync(tableId);
        if (err != null || table == null) return err!;

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn tệp đính kèm." });
        if (AttachmentsController.IsBlockedExtension(file.FileName))
            return BadRequest(new { success = false, message = "Định dạng tệp không được phép tải lên vì lý do bảo mật." });

        try
        {
            await _files.EnforceQuotaAsync(table.AppId, "public_form", file.Length);
        }
        catch (StorageQuotaExceededException ex)
        {
            return StatusCode(413, new { success = false, message = ex.Message });
        }

        var safeExt = Path.GetExtension(file.FileName);
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
            VALUES (@id, @app, @tbl, NULL, @fn, @ct, @sz, @sp, 'public_form')
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", table.AppId);
        cmd.Parameters.AddWithValue("tbl", table.Id);
        cmd.Parameters.AddWithValue("fn", file.FileName);
        cmd.Parameters.AddWithValue("ct", string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        cmd.Parameters.AddWithValue("sz", file.Length);
        cmd.Parameters.AddWithValue("sp", storedName);
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

    [HttpPost("{tableId}/submit")]
    public async Task<IActionResult> SubmitPublicForm(string tableId, [FromBody] Dictionary<string, object?> data)
    {
        if (!CheckRateLimit("submit", 20))
            return StatusCode(429, new { success = false, message = "Bạn gửi quá nhiều yêu cầu liên tiếp. Vui lòng thử lại sau 1 phút." });

        var (table, err) = await ResolveAvailablePublicTableAsync(tableId);
        if (err != null || table == null) return err!;

        try
        {
            var allowed = (await _metadata.GetColumnsAsync(table.Id))
                .Where(c => c.ShowInForm && !c.HiddenForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase)
                    && !c.ReadOnlyForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(c.Formula) && c.DataType != "AUTO_NUMBER")
                .Select(c => c.ColumnCode).ToHashSet(StringComparer.Ordinal);
            if (data.Keys.Any(key => !allowed.Contains(key)))
                return BadRequest(new { success = false, message = "Dữ liệu chứa trường không được phép nhập qua biểu mẫu công khai." });
            var (saved, _) = await _records.UpsertRecordAsync(new UpsertRecordRequest
            {
                AppId = table.AppId,
                ModuleId = table.ModuleId,
                TableId = table.Id,
                Data = data ?? new Dictionary<string, object?>()
            }, "public_form", false);

            var app = await _metadata.GetAppByCodeOrIdAsync(table.AppId);
            await _notifications.CreateAsync(
                $"Biểu mẫu mới ({app?.Code ?? "FORM"})",
                $"Khách/Ứng viên vừa gửi phiếu '{saved.RecordKey ?? saved.Id}' qua Public Form '{table.DisplayName}'.",
                app?.Code,
                saved.Id,
                "*");

            return Ok(new { success = true, data = new { id = saved.Id, recordKey = saved.RecordKey } });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }
}
