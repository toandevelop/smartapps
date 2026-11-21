using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SmartApps.Api.Services;

public sealed class BackupConfigDto
{
    public string Id { get; set; } = "default";
    public bool IsEnabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public int RetentionCount { get; set; } = 7;
    public string DestinationType { get; set; } = "LOCAL";
    public Dictionary<string, object?> DestinationConfig { get; set; } = new();
    public bool HasSecret { get; set; }
    public bool IncludeUploads { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public string UpdatedBy { get; set; } = "admin";
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpsertBackupConfigRequest
{
    public bool IsEnabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public int RetentionCount { get; set; } = 7;
    public string DestinationType { get; set; } = "LOCAL";
    public Dictionary<string, object?>? DestinationConfig { get; set; }
    public string? DestinationSecret { get; set; }
    public bool IncludeUploads { get; set; } = true;
}

public sealed class BackupRunDto
{
    public string Id { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "MANUAL";
    public string DestinationType { get; set; } = "LOCAL";
    public string FileName { get; set; } = string.Empty;
    public string? LocalPath { get; set; }
    public string? RemoteUri { get; set; }
    public long SizeBytes { get; set; }
    public string? ChecksumSha256 { get; set; }
    public string Status { get; set; } = "SUCCEEDED";
    public string? Message { get; set; }
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Creates portable compressed backups (metadata + records + optional uploads)
/// and pushes them to Local storage, S3/MinIO, Google Drive, or Custom Webhook.
/// </summary>
public sealed class BackupService
{
    private readonly string _connStr;
    private readonly string _backupDir;
    private readonly IFileStorageAdapter _storage;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly string[] BackupTables =
    [
        "sys_apps", "sys_modules", "sys_tables", "sys_columns",
        "sys_categories", "sys_category_items", "sys_users", "sys_roles",
        "sys_org_units", "sys_print_templates", "sys_plugins",
        "app_records", "app_workflows", "app_automations", "app_widgets",
        "app_audit_logs", "app_attachments"
    ];

    public BackupService(
        IConfiguration config,
        IFileStorageAdapter storage,
        IHttpClientFactory httpFactory,
        IWebHostEnvironment env)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _storage = storage;
        _httpFactory = httpFactory;
        _backupDir = Path.Combine(env.ContentRootPath, "backups");
        Directory.CreateDirectory(_backupDir);
    }

    public async Task<BackupConfigDto> GetConfigAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, is_enabled, interval_hours, retention_count, destination_type,
                   destination_config::text, destination_secret IS NOT NULL AND destination_secret <> '',
                   include_uploads, last_run_at, updated_by, updated_at
            FROM sys_backup_configs
            WHERE id = 'default'
            LIMIT 1
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new BackupConfigDto();

        return new BackupConfigDto
        {
            Id = r.GetString(0),
            IsEnabled = r.GetBoolean(1),
            IntervalHours = r.GetInt32(2),
            RetentionCount = r.GetInt32(3),
            DestinationType = r.GetString(4),
            DestinationConfig = RecordWriteService.ParseJsonDict(r.GetString(5)),
            HasSecret = r.GetBoolean(6),
            IncludeUploads = r.GetBoolean(7),
            LastRunAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
            UpdatedBy = r.IsDBNull(9) ? "admin" : r.GetString(9),
            UpdatedAt = r.GetDateTime(10)
        };
    }

    public async Task<BackupConfigDto> UpsertConfigAsync(UpsertBackupConfigRequest req, string actor)
    {
        var destType = string.IsNullOrWhiteSpace(req.DestinationType) ? "LOCAL" : req.DestinationType.Trim().ToUpperInvariant();
        var hasNewSec = !string.IsNullOrWhiteSpace(req.DestinationSecret);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_backup_configs (
                id, is_enabled, interval_hours, retention_count, destination_type,
                destination_config, destination_secret, include_uploads, updated_by, updated_at
            )
            VALUES ('default', @en, @hrs, @ret, @dt, @cfg::jsonb, @sec, @inc, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                is_enabled = EXCLUDED.is_enabled,
                interval_hours = EXCLUDED.interval_hours,
                retention_count = EXCLUDED.retention_count,
                destination_type = EXCLUDED.destination_type,
                destination_config = EXCLUDED.destination_config,
                destination_secret = CASE WHEN @hasSec THEN EXCLUDED.destination_secret ELSE sys_backup_configs.destination_secret END,
                include_uploads = EXCLUDED.include_uploads,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("en", req.IsEnabled);
        cmd.Parameters.AddWithValue("hrs", Math.Clamp(req.IntervalHours, 1, 720));
        cmd.Parameters.AddWithValue("ret", Math.Clamp(req.RetentionCount, 1, 100));
        cmd.Parameters.AddWithValue("dt", destType);
        cmd.Parameters.AddWithValue("cfg", JsonSerializer.Serialize(req.DestinationConfig ?? new()));
        cmd.Parameters.Add(new NpgsqlParameter("sec", NpgsqlDbType.Text) { Value = hasNewSec ? req.DestinationSecret!.Trim() : DBNull.Value });
        cmd.Parameters.AddWithValue("hasSec", hasNewSec);
        cmd.Parameters.AddWithValue("inc", req.IncludeUploads);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();

        return await GetConfigAsync();
    }

    public async Task<List<BackupRunDto>> ListRunsAsync(int limit = 30)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, trigger_type, destination_type, file_name, local_path, remote_uri,
                   size_bytes, checksum_sha256, status, message, created_by, created_at
            FROM app_backup_runs
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 200));

        var list = new List<BackupRunDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new BackupRunDto
            {
                Id = r.GetString(0),
                TriggerType = r.GetString(1),
                DestinationType = r.GetString(2),
                FileName = r.GetString(3),
                LocalPath = r.IsDBNull(4) ? null : r.GetString(4),
                RemoteUri = r.IsDBNull(5) ? null : r.GetString(5),
                SizeBytes = r.GetInt64(6),
                ChecksumSha256 = r.IsDBNull(7) ? null : r.GetString(7),
                Status = r.GetString(8),
                Message = r.IsDBNull(9) ? null : r.GetString(9),
                CreatedBy = r.IsDBNull(10) ? "admin" : r.GetString(10),
                CreatedAt = r.GetDateTime(11)
            });
        }
        return list;
    }

    public async Task<BackupRunDto?> GetRunByIdAsync(string id)
    {
        var runs = await ListRunsAsync(200);
        return runs.FirstOrDefault(r => r.Id == id);
    }

    public async Task<BackupRunDto> RunBackupAsync(string triggerType, string actor, string? overrideBackupDir = null, CancellationToken ct = default)
    {
        var cfg = await GetConfigAsync();
        var secret = await LoadSecretAsync();
        var runId = $"bkp_{Guid.NewGuid().ToString("N")[..12]}";
        var fileName = $"smartapps_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{runId[^4..]}.zip";
        var targetDir = string.IsNullOrWhiteSpace(overrideBackupDir) ? _backupDir : overrideBackupDir;
        Directory.CreateDirectory(targetDir);
        var localPath = Path.Combine(targetDir, fileName);

        string status = "SUCCEEDED";
        string? remoteUri = null;
        string message;
        long sizeBytes = 0;
        string checksum = "";

        try
        {
            await BuildArchiveAsync(localPath, cfg.IncludeUploads, ct);
            var fileInfo = new FileInfo(localPath);
            sizeBytes = fileInfo.Length;
            await using (var fs = File.OpenRead(localPath))
            {
                checksum = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            }

            if (cfg.DestinationType == "S3")
            {
                remoteUri = await UploadToS3Async(cfg, secret, fileName, localPath, ct);
                message = $"Đã sao lưu và đẩy lên S3 ({remoteUri}).";
            }
            else if (cfg.DestinationType == "GOOGLE_DRIVE")
            {
                remoteUri = await UploadToGoogleDriveAsync(cfg, secret, fileName, localPath, ct);
                message = $"Đã sao lưu và đẩy lên Google Drive ({remoteUri}).";
            }
            else if (cfg.DestinationType == "CUSTOM_WEBHOOK")
            {
                remoteUri = await UploadToWebhookAsync(cfg, secret, fileName, localPath, ct);
                message = $"Đã gửi thông báo/archive sao lưu tới Webhook ({remoteUri}).";
            }
            else
            {
                message = $"Đã tạo bản sao lưu cục bộ ({sizeBytes:N0} bytes).";
            }

            EnforceLocalRetention(targetDir, cfg.RetentionCount);
        }
        catch (Exception ex)
        {
            status = "FAILED";
            message = $"Lỗi sao lưu ({cfg.DestinationType}): {ex.Message}";
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO app_backup_runs (id, trigger_type, destination_type, file_name, local_path, remote_uri, size_bytes, checksum_sha256, status, message, created_by)
            VALUES (@id, @tt, @dt, @fn, @lp, @ru, @sz, @cs, @st, @msg, @cb)
            """, conn))
        {
            ins.Parameters.AddWithValue("id", runId);
            ins.Parameters.AddWithValue("tt", triggerType);
            ins.Parameters.AddWithValue("dt", cfg.DestinationType);
            ins.Parameters.AddWithValue("fn", fileName);
            ins.Parameters.Add(new NpgsqlParameter("lp", NpgsqlDbType.Text) { Value = File.Exists(localPath) ? localPath : DBNull.Value });
            ins.Parameters.Add(new NpgsqlParameter("ru", NpgsqlDbType.Text) { Value = (object?)remoteUri ?? DBNull.Value });
            ins.Parameters.AddWithValue("sz", sizeBytes);
            ins.Parameters.Add(new NpgsqlParameter("cs", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(checksum) ? DBNull.Value : checksum });
            ins.Parameters.AddWithValue("st", status);
            ins.Parameters.AddWithValue("msg", message);
            ins.Parameters.AddWithValue("cb", actor);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await using (var upCfg = new NpgsqlCommand("UPDATE sys_backup_configs SET last_run_at = CURRENT_TIMESTAMP WHERE id = 'default'", conn))
        {
            await upCfg.ExecuteNonQueryAsync(ct);
        }

        return (await GetRunByIdAsync(runId))!;
    }

    private async Task BuildArchiveAsync(string zipPath, bool includeUploads, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        await using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        var tableCounts = new Dictionary<string, int>();
        foreach (var table in BackupTables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COALESCE(json_agg(t), '[]'::json)::text FROM {table} t", conn);
            var json = (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "[]";
            using var doc = JsonDocument.Parse(json);
            tableCounts[table] = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;

            var entry = zip.CreateEntry($"db/{table}.json", CompressionLevel.Optimal);
            await using var es = entry.Open();
            await es.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
        }

        if (includeUploads)
        {
            await using var attCmd = new NpgsqlCommand(
                "SELECT storage_path FROM app_attachments WHERE COALESCE(is_deleted, FALSE) = FALSE", conn);
            await using var r = await attCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var storagePath = r.GetString(0);
                try
                {
                    await using var blob = await _storage.OpenReadAsync(storagePath, ct);
                    if (blob == null) continue;
                    var entry = zip.CreateEntry($"uploads/{storagePath}", CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await blob.CopyToAsync(es, ct);
                }
                catch
                {
                    // Skip missing physical file without aborting DB backup
                }
            }
        }

        var manifest = JsonSerializer.Serialize(new
        {
            platform = "SmartApps",
            createdAtUtc = DateTime.UtcNow,
            storageProvider = _storage.ProviderName,
            tableCounts
        }, new JsonSerializerOptions { WriteIndented = true });
        var mEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var ms = mEntry.Open();
        await ms.WriteAsync(Encoding.UTF8.GetBytes(manifest), ct);
    }

    private async Task<string> UploadToS3Async(BackupConfigDto cfg, string? secret, string fileName, string localPath, CancellationToken ct)
    {
        var endpoint = (cfg.DestinationConfig.GetValueOrDefault("endpoint")?.ToString() ?? "").TrimEnd('/');
        var bucket = cfg.DestinationConfig.GetValueOrDefault("bucket")?.ToString() ?? "smartapps-backups";
        var accessKey = cfg.DestinationConfig.GetValueOrDefault("accessKey")?.ToString() ?? "";
        var region = cfg.DestinationConfig.GetValueOrDefault("region")?.ToString() ?? "us-east-1";
        var prefix = (cfg.DestinationConfig.GetValueOrDefault("prefix")?.ToString() ?? "backups").Trim('/');
        var objectKey = string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Fallback to system S3 adapter if configured at platform level
            if (_storage.ProviderName == "S3")
            {
                await using var stream = File.OpenRead(localPath);
                await _storage.SaveAsync(objectKey, stream, "application/zip", ct);
                return $"s3://platform-default/{objectKey}";
            }
            throw new InvalidOperationException("Thiếu S3 endpoint trong cấu hình backup.");
        }

        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        var payloadHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var url = $"{endpoint}/{Uri.EscapeDataString(bucket)}/{objectKey}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(bytes) };
        req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/zip");
        var headers = S3CompatibleStorageAdapter.BuildSigV4Headers("PUT", req.RequestUri!, accessKey, secret ?? "", region, payloadHash, DateTime.UtcNow);
        foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        var resp = await _httpFactory.CreateClient().SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return $"s3://{bucket}/{objectKey}";
    }

    private async Task<string> UploadToGoogleDriveAsync(BackupConfigDto cfg, string? accessToken, string fileName, string localPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Thiếu Google Drive OAuth Access Token trong cấu hình backup.");

        var folderId = cfg.DestinationConfig.GetValueOrDefault("folderId")?.ToString();
        var metadata = string.IsNullOrWhiteSpace(folderId)
            ? JsonSerializer.Serialize(new { name = fileName, mimeType = "application/zip" })
            : JsonSerializer.Serialize(new { name = fileName, mimeType = "application/zip", parents = new[] { folderId } });

        using var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        var fileBytes = await File.ReadAllBytesAsync(localPath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        multipart.Add(fileContent);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart")
        {
            Content = multipart
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var resp = await _httpFactory.CreateClient().SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Drive HTTP {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var driveId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : fileName;
        return $"gdrive://{driveId}";
    }

    private async Task<string> UploadToWebhookAsync(BackupConfigDto cfg, string? secret, string fileName, string localPath, CancellationToken ct)
    {
        var url = cfg.DestinationConfig.GetValueOrDefault("webhookUrl")?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Thiếu webhookUrl trong cấu hình backup.");

        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/zip");
        req.Headers.TryAddWithoutValidation("X-SmartApps-Backup-File", fileName);
        if (!string.IsNullOrWhiteSpace(secret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var resp = await _httpFactory.CreateClient().SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return url;
    }

    private static void EnforceLocalRetention(string dir, int retentionCount)
    {
        if (retentionCount <= 0 || !Directory.Exists(dir)) return;
        var files = new DirectoryInfo(dir)
            .GetFiles("smartapps_backup_*.zip")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(retentionCount)
            .ToList();
        foreach (var old in files)
        {
            try { old.Delete(); } catch { /* ignore */ }
        }
    }

    private async Task<string?> LoadSecretAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT destination_secret FROM sys_backup_configs WHERE id = 'default' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return val == null || val == DBNull.Value ? null : val.ToString();
    }
}

public sealed class BackupSchedulerService : BackgroundService
{
    private readonly string _connStr;
    private readonly BackupService _backups;
    private readonly ILogger<BackupSchedulerService> _logger;

    public BackupSchedulerService(IConfiguration config, BackupService backups, ILogger<BackupSchedulerService> logger)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _backups = backups;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLockedBackupCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled backup check failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    public async Task<bool> RunLockedBackupCheckAsync(CancellationToken ct = default)
    {
        await using var lockConn = new NpgsqlConnection(_connStr);
        await lockConn.OpenAsync(ct);

        await using var tryCmd = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended('smartapps_backup_scheduler_tick', 0))", lockConn);
        var acquired = Convert.ToBoolean(await tryCmd.ExecuteScalarAsync(ct));
        if (!acquired)
        {
            _logger.LogDebug("BackupSchedulerService advisory lock held by peer instance; skipping check.");
            return false;
        }

        try
        {
            var cfg = await _backups.GetConfigAsync();
            if (cfg.IsEnabled)
            {
                var due = !cfg.LastRunAt.HasValue || cfg.LastRunAt.Value.AddHours(cfg.IntervalHours) <= DateTime.UtcNow;
                if (due)
                {
                    await _backups.RunBackupAsync("SCHEDULED", "scheduler", ct: ct);
                }
            }
            return true;
        }
        finally
        {
            await using var unlockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended('smartapps_backup_scheduler_tick', 0))", lockConn);
            await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
