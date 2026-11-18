using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace SmartApps.Api.Services;

public sealed class StorageQuotaExceededException(string message) : Exception(message);

public sealed class AttachmentInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? TableId { get; set; }
    public string? RecordId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class StorageQuotaDto
{
    public string Id { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "GLOBAL";
    public string ScopeId { get; set; } = "*";
    public int QuotaMb { get; set; }
    public string UpdatedBy { get; set; } = "admin";
    public DateTime UpdatedAt { get; set; }
}

public sealed class StorageStatsDto
{
    public long TotalUsedBytes { get; set; }
    public int TotalFiles { get; set; }
    public List<StorageQuotaDto> Quotas { get; set; } = new();
    public Dictionary<string, long> UsedByApp { get; set; } = new();
    public Dictionary<string, long> UsedByUser { get; set; } = new();
}

public sealed class FileShareDto
{
    public string Id { get; set; } = string.Empty;
    public string AttachmentId { get; set; } = string.Empty;
    public string ShareToken { get; set; } = string.Empty;
    public string ShareUrl { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public bool AllowDownload { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public int AccessCount { get; set; }
}

public sealed class FileManagementService
{
    private readonly string _connStr;

    public FileManagementService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
    }

    public async Task EnforceQuotaAsync(string? appId, string username, long incomingBytes)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Check USER quota, then APP quota, then GLOBAL quota
        var checks = new List<(string ScopeType, string ScopeId, string WhereSql)>
        {
            ("USER", username, "uploaded_by = @sid"),
        };
        if (!string.IsNullOrWhiteSpace(appId))
            checks.Add(("APP", appId, "app_id = @sid"));
        checks.Add(("GLOBAL", "*", "1=1"));

        foreach (var (scopeType, scopeId, whereSql) in checks)
        {
            await using var qCmd = new NpgsqlCommand(
                "SELECT quota_mb FROM sys_storage_quotas WHERE scope_type = @st AND scope_id = @sid LIMIT 1", conn);
            qCmd.Parameters.AddWithValue("st", scopeType);
            qCmd.Parameters.AddWithValue("sid", scopeId);
            var qVal = await qCmd.ExecuteScalarAsync();
            if (qVal == null || qVal == DBNull.Value) continue;

            var quotaBytes = Convert.ToInt64(qVal) * 1024L * 1024L;
            await using var uCmd = new NpgsqlCommand(
                $"SELECT COALESCE(SUM(size_bytes), 0) FROM app_attachments WHERE COALESCE(is_deleted, FALSE) = FALSE AND {whereSql}", conn);
            if (whereSql.Contains("@sid")) uCmd.Parameters.AddWithValue("sid", scopeId);
            var usedBytes = Convert.ToInt64(await uCmd.ExecuteScalarAsync());

            if (usedBytes + incomingBytes > quotaBytes)
            {
                var limitMb = Convert.ToInt64(qVal);
                throw new StorageQuotaExceededException(
                    $"Vượt quá hạn mức dung lượng lưu trữ ({scopeType}:{scopeId} giới hạn {limitMb} MB).");
            }
        }
    }

    public async Task<StorageQuotaDto> UpsertQuotaAsync(string scopeType, string scopeId, int quotaMb, string actor)
    {
        var st = scopeType.Trim().ToUpperInvariant();
        var sid = string.IsNullOrWhiteSpace(scopeId) ? "*" : scopeId.Trim();
        var id = $"qta_{Guid.NewGuid().ToString("N")[..10]}";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_storage_quotas (id, scope_type, scope_id, quota_mb, updated_by, updated_at)
            VALUES (@id, @st, @sid, @mb, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (scope_type, scope_id) DO UPDATE SET
                quota_mb = EXCLUDED.quota_mb,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            RETURNING id, scope_type, scope_id, quota_mb, updated_by, updated_at
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("st", st);
        cmd.Parameters.AddWithValue("sid", sid);
        cmd.Parameters.AddWithValue("mb", Math.Max(1, quotaMb));
        cmd.Parameters.AddWithValue("actor", actor);

        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new StorageQuotaDto
        {
            Id = r.GetString(0),
            ScopeType = r.GetString(1),
            ScopeId = r.GetString(2),
            QuotaMb = r.GetInt32(3),
            UpdatedBy = r.GetString(4),
            UpdatedAt = r.GetDateTime(5)
        };
    }

    public async Task<StorageStatsDto> GetStorageStatsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var stats = new StorageStatsDto();
        await using (var sumCmd = new NpgsqlCommand(
            "SELECT COALESCE(SUM(size_bytes), 0), COUNT(*) FROM app_attachments WHERE COALESCE(is_deleted, FALSE) = FALSE", conn))
        await using (var r = await sumCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                stats.TotalUsedBytes = r.GetInt64(0);
                stats.TotalFiles = Convert.ToInt32(r.GetInt64(1));
            }
        }

        await using (var appCmd = new NpgsqlCommand(
            "SELECT COALESCE(app_id, 'global'), COALESCE(SUM(size_bytes), 0) FROM app_attachments WHERE COALESCE(is_deleted, FALSE) = FALSE GROUP BY 1", conn))
        await using (var r = await appCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) stats.UsedByApp[r.GetString(0)] = r.GetInt64(1);
        }

        await using (var usrCmd = new NpgsqlCommand(
            "SELECT COALESCE(uploaded_by, 'unknown'), COALESCE(SUM(size_bytes), 0) FROM app_attachments WHERE COALESCE(is_deleted, FALSE) = FALSE GROUP BY 1", conn))
        await using (var r = await usrCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) stats.UsedByUser[r.GetString(0)] = r.GetInt64(1);
        }

        await using (var qCmd = new NpgsqlCommand(
            "SELECT id, scope_type, scope_id, quota_mb, updated_by, updated_at FROM sys_storage_quotas ORDER BY scope_type, scope_id", conn))
        await using (var r = await qCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                stats.Quotas.Add(new StorageQuotaDto
                {
                    Id = r.GetString(0),
                    ScopeType = r.GetString(1),
                    ScopeId = r.GetString(2),
                    QuotaMb = r.GetInt32(3),
                    UpdatedBy = r.IsDBNull(4) ? "admin" : r.GetString(4),
                    UpdatedAt = r.GetDateTime(5)
                });
            }
        }

        return stats;
    }

    public async Task<List<AttachmentInfoDto>> ListAttachmentsAsync(string? appId = null, string? search = null, int limit = 100)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, app_id, table_id, record_id, file_name, content_type, size_bytes, uploaded_by, created_at
            FROM app_attachments
            WHERE COALESCE(is_deleted, FALSE) = FALSE
              AND (@appId IS NULL OR app_id = @appId)
              AND (@q IS NULL OR LOWER(file_name) LIKE '%' || LOWER(@q) || '%' OR LOWER(uploaded_by) LIKE '%' || LOWER(@q) || '%')
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId });
        cmd.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim() });
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 500));

        var list = new List<AttachmentInfoDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AttachmentInfoDto
            {
                Id = r.GetString(0),
                AppId = r.IsDBNull(1) ? null : r.GetString(1),
                TableId = r.IsDBNull(2) ? null : r.GetString(2),
                RecordId = r.IsDBNull(3) ? null : r.GetString(3),
                FileName = r.GetString(4),
                ContentType = r.IsDBNull(5) ? "application/octet-stream" : r.GetString(5),
                SizeBytes = r.GetInt64(6),
                UploadedBy = r.IsDBNull(7) ? "system" : r.GetString(7),
                CreatedAt = r.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<FileShareDto?> CreateShareLinkAsync(string attachmentId, int expiresInHours, bool allowDownload, string actor)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using (var chk = new NpgsqlCommand("SELECT 1 FROM app_attachments WHERE id = @id AND COALESCE(is_deleted, FALSE) = FALSE", conn))
        {
            chk.Parameters.AddWithValue("id", attachmentId);
            if (await chk.ExecuteScalarAsync() == null) return null;
        }

        var id = $"shr_{Guid.NewGuid().ToString("N")[..10]}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        DateTime? expiresAt = expiresInHours > 0 ? DateTime.UtcNow.AddHours(expiresInHours) : null;

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_file_shares (id, attachment_id, share_token, expires_at, allow_download, created_by)
            VALUES (@id, @att, @tok, @exp, @dl, @actor)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("att", attachmentId);
        cmd.Parameters.AddWithValue("tok", token);
        cmd.Parameters.Add(new NpgsqlParameter("exp", NpgsqlDbType.TimestampTz) { Value = (object?)expiresAt ?? DBNull.Value });
        cmd.Parameters.AddWithValue("dl", allowDownload);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();

        return new FileShareDto
        {
            Id = id,
            AttachmentId = attachmentId,
            ShareToken = token,
            ShareUrl = $"/api/attachments/shared/{token}",
            ExpiresAt = expiresAt,
            AllowDownload = allowDownload,
            CreatedBy = actor,
            AccessCount = 0
        };
    }

    public async Task<(string FileName, string ContentType, string StoragePath, bool AllowDownload)?> ResolveShareTokenAsync(string shareToken)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("""
            SELECT a.file_name, a.content_type, a.storage_path, s.allow_download, s.id
            FROM app_file_shares s
            JOIN app_attachments a ON a.id = s.attachment_id
            WHERE s.share_token = @tok
              AND COALESCE(a.is_deleted, FALSE) = FALSE
              AND (s.expires_at IS NULL OR s.expires_at > CURRENT_TIMESTAMP)
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tok", shareToken.Trim());

        string? fileName = null, contentType = null, storagePath = null, shareId = null;
        bool allowDownload = true;
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            fileName = r.GetString(0);
            contentType = r.IsDBNull(1) ? "application/octet-stream" : r.GetString(1);
            storagePath = r.GetString(2);
            allowDownload = r.GetBoolean(3);
            shareId = r.GetString(4);
        }

        await using (var up = new NpgsqlCommand("UPDATE app_file_shares SET access_count = access_count + 1 WHERE id = @id", conn))
        {
            up.Parameters.AddWithValue("id", shareId!);
            await up.ExecuteNonQueryAsync();
        }

        return (fileName!, contentType!, storagePath!, allowDownload);
    }

    public Task<(int CleanedCount, long FreedBytes)> CleanupOrphanAttachmentsAsync(string uploadDir, int minAgeHours = 24)
        => CleanupOrphanAttachmentsAsync(new LocalFileStorageAdapter(uploadDir), minAgeHours);

    public async Task<(int CleanedCount, long FreedBytes)> CleanupOrphanAttachmentsAsync(IFileStorageAdapter storage, int minAgeHours = 24)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var orphans = new List<(string Id, string StoragePath, long SizeBytes)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.storage_path, a.size_bytes
            FROM app_attachments a
            WHERE COALESCE(a.is_deleted, FALSE) = FALSE
              AND a.created_at <= CURRENT_TIMESTAMP - (@hrs * INTERVAL '1 hour')
              AND NOT EXISTS (
                  SELECT 1 FROM app_records r
                  WHERE r.deleted_at IS NULL
                    AND r.data::text LIKE '%' || a.id || '%'
              )
            """, conn))
        {
            cmd.Parameters.AddWithValue("hrs", Math.Max(0, minAgeHours));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                orphans.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
            }
        }

        long freed = 0;
        var cleaned = 0;
        foreach (var (id, storagePath, sizeBytes) in orphans)
        {
            await storage.DeleteAsync(storagePath);

            await using var del = new NpgsqlCommand(
                "UPDATE app_attachments SET is_deleted = TRUE, deleted_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
            del.Parameters.AddWithValue("id", id);
            await del.ExecuteNonQueryAsync();
            freed += sizeBytes;
            cleaned++;
        }

        return (cleaned, freed);
    }
}
