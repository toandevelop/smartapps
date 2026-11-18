using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class NotificationService
{
    private readonly string _connStr;
    private readonly RealtimeEventService _realtime;
    private readonly ExternalNotificationService _external;

    public NotificationService(
        IConfiguration config,
        RealtimeEventService realtime,
        ExternalNotificationService external)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _realtime = realtime;
        _external = external;
    }

    public async Task<string> CreateAsync(
        string title,
        string message,
        string? appCode = null,
        string? recordId = null,
        string recipient = "*",
        string category = "SYSTEM",
        string? linkUrl = null,
        string? senderUsername = null,
        string priority = "NORMAL")
    {
        var id = $"noti_{Guid.NewGuid().ToString("N")[..10]}";
        var resolvedLink = linkUrl ?? (!string.IsNullOrWhiteSpace(appCode) ? $"/apps/{appCode}" : null);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_notifications (id, recipient_username, title, message, app_code, record_id, category, link_url, sender_username, priority)
            VALUES (@id, @rec, @t, @m, @ac, @rid, @cat, @lnk, @snd, @pri)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("rec", recipient);
        cmd.Parameters.AddWithValue("t", title);
        cmd.Parameters.AddWithValue("m", message);
        cmd.Parameters.AddWithValue("ac", (object?)appCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rid", (object?)recordId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cat", category);
        cmd.Parameters.AddWithValue("lnk", (object?)resolvedLink ?? DBNull.Value);
        cmd.Parameters.AddWithValue("snd", (object?)senderUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pri", priority);
        await cmd.ExecuteNonQueryAsync();

        _realtime.Publish("notification", appCode, null, recordId, recipient, new
        {
            id,
            title,
            message,
            category,
            linkUrl = resolvedLink,
            senderUsername,
            priority
        });

        _external.DispatchInBackground(title, message, appCode, recipient, category, resolvedLink);
        return id;
    }

    public async Task<(List<SysNotificationDto> Items, int UnreadCount)> ListForUserAsync(
        string username,
        int limit = 25,
        string? category = null,
        bool unreadOnly = false)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var catFilter = string.IsNullOrWhiteSpace(category) || category.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : category.Trim().ToUpperInvariant();

        var sql = """
            SELECT id, recipient_username, title, message, app_code, record_id,
                   COALESCE(category, 'SYSTEM'), link_url, sender_username, COALESCE(priority, 'NORMAL'),
                   is_read, created_at
            FROM sys_notifications
            WHERE (recipient_username = '*' OR LOWER(recipient_username) = LOWER(@u))
              AND (@cat IS NULL OR UPPER(COALESCE(category, 'SYSTEM')) = @cat)
              AND (@unreadOnly = FALSE OR is_read = FALSE)
            ORDER BY created_at DESC
            LIMIT @lim
            """;
        var list = new List<SysNotificationDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.Add(new NpgsqlParameter("cat", NpgsqlDbType.Varchar) { Value = (object?)catFilter ?? DBNull.Value });
            cmd.Parameters.AddWithValue("unreadOnly", unreadOnly);
            cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 200));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new SysNotificationDto
                {
                    Id = r.GetString(0),
                    RecipientUsername = r.GetString(1),
                    Title = r.GetString(2),
                    Message = r.IsDBNull(3) ? null : r.GetString(3),
                    AppCode = r.IsDBNull(4) ? null : r.GetString(4),
                    RecordId = r.IsDBNull(5) ? null : r.GetString(5),
                    Category = r.GetString(6),
                    LinkUrl = r.IsDBNull(7) ? null : r.GetString(7),
                    SenderUsername = r.IsDBNull(8) ? null : r.GetString(8),
                    Priority = r.GetString(9),
                    IsRead = r.GetBoolean(10),
                    CreatedAt = r.GetDateTime(11)
                });
            }
        }

        await using var cntCmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM sys_notifications
            WHERE (recipient_username = '*' OR LOWER(recipient_username) = LOWER(@u))
              AND is_read = FALSE
            """, conn);
        cntCmd.Parameters.AddWithValue("u", username);
        var unreadCount = Convert.ToInt32(await cntCmd.ExecuteScalarAsync());

        return (list, unreadCount);
    }

    public async Task MarkReadAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sys_notifications SET is_read = TRUE WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkAllReadAsync(string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE sys_notifications SET is_read = TRUE WHERE recipient_username = '*' OR LOWER(recipient_username) = LOWER(@u)", conn);
        cmd.Parameters.AddWithValue("u", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteAsync(string id, string username, bool isAdmin)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = isAdmin
            ? "DELETE FROM sys_notifications WHERE id = @id"
            : "DELETE FROM sys_notifications WHERE id = @id AND LOWER(recipient_username) = LOWER(@u)";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        if (!isAdmin) cmd.Parameters.AddWithValue("u", username);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> ClearReadAsync(string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM sys_notifications WHERE LOWER(recipient_username) = LOWER(@u) AND is_read = TRUE", conn);
        cmd.Parameters.AddWithValue("u", username);
        return await cmd.ExecuteNonQueryAsync();
    }
}
