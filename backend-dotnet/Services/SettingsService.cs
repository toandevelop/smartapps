using System.Text.Json;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class SettingsService
{
    private readonly string _connStr;
    private readonly AuthService _auth;
    private readonly RealtimeEventService _realtime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(IConfiguration config, AuthService auth, RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _auth = auth;
        _realtime = realtime;
    }

    public async Task<UserSettingsDto> GetUserSettingsAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("""
            SELECT user_id, theme_mode, accent_color, compact_mode, language,
                   notify_in_app, notify_sound, notify_desktop, notify_chat,
                   notify_task_assigned, notify_approval, notify_mentions,
                   email_digest, chat_enter_to_send,
                   COALESCE(notify_email, FALSE), COALESCE(notify_telegram, FALSE), telegram_chat_id,
                   COALESCE(notify_zalo, FALSE), zalo_user_id, updated_at
            FROM sys_user_settings
            WHERE user_id = @uid
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("uid", userId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new UserSettingsDto
            {
                UserId = r.GetString(0),
                ThemeMode = r.GetString(1),
                AccentColor = r.GetString(2),
                CompactMode = r.GetBoolean(3),
                Language = r.GetString(4),
                NotifyInApp = r.GetBoolean(5),
                NotifySound = r.GetBoolean(6),
                NotifyDesktop = r.GetBoolean(7),
                NotifyChat = r.GetBoolean(8),
                NotifyTaskAssigned = r.GetBoolean(9),
                NotifyApproval = r.GetBoolean(10),
                NotifyMentions = r.GetBoolean(11),
                EmailDigest = r.GetString(12),
                ChatEnterToSend = r.GetBoolean(13),
                NotifyEmail = r.GetBoolean(14),
                NotifyTelegram = r.GetBoolean(15),
                TelegramChatId = r.IsDBNull(16) ? null : r.GetString(16),
                NotifyZalo = r.GetBoolean(17),
                ZaloUserId = r.IsDBNull(18) ? null : r.GetString(18),
                UpdatedAt = r.GetDateTime(19)
            };
        }

        await r.CloseAsync();
        var def = new UserSettingsDto { UserId = userId };
        await using var ins = new NpgsqlCommand("""
            INSERT INTO sys_user_settings (user_id)
            VALUES (@uid)
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        ins.Parameters.AddWithValue("uid", userId);
        await ins.ExecuteNonQueryAsync();
        return def;
    }

    public async Task<UserSettingsDto> UpdateUserSettingsAsync(string userId, UpdateUserSettingsRequest req)
    {
        var theme = req.ThemeMode is "dark" or "auto" ? req.ThemeMode : "light";
        var accent = string.IsNullOrWhiteSpace(req.AccentColor) ? "#1a73e8" : req.AccentColor.Trim();
        var lang = req.Language == "en" ? "en" : "vi";
        var digest = req.EmailDigest is "DAILY" or "OFF" ? req.EmailDigest : "INSTANT";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_user_settings (
                user_id, theme_mode, accent_color, compact_mode, language,
                notify_in_app, notify_sound, notify_desktop, notify_chat,
                notify_task_assigned, notify_approval, notify_mentions,
                email_digest, chat_enter_to_send,
                notify_email, notify_telegram, telegram_chat_id, notify_zalo, zalo_user_id, updated_at
            )
            VALUES (
                @uid, @tm, @ac, @cm, @lang,
                @nia, @ns, @nd, @nc,
                @nt, @na, @nm,
                @ed, @ce,
                @nem, @ntel, @tcid, @nzal, @zuid, CURRENT_TIMESTAMP
            )
            ON CONFLICT (user_id) DO UPDATE SET
                theme_mode = EXCLUDED.theme_mode,
                accent_color = EXCLUDED.accent_color,
                compact_mode = EXCLUDED.compact_mode,
                language = EXCLUDED.language,
                notify_in_app = EXCLUDED.notify_in_app,
                notify_sound = EXCLUDED.notify_sound,
                notify_desktop = EXCLUDED.notify_desktop,
                notify_chat = EXCLUDED.notify_chat,
                notify_task_assigned = EXCLUDED.notify_task_assigned,
                notify_approval = EXCLUDED.notify_approval,
                notify_mentions = EXCLUDED.notify_mentions,
                email_digest = EXCLUDED.email_digest,
                chat_enter_to_send = EXCLUDED.chat_enter_to_send,
                notify_email = EXCLUDED.notify_email,
                notify_telegram = EXCLUDED.notify_telegram,
                telegram_chat_id = EXCLUDED.telegram_chat_id,
                notify_zalo = EXCLUDED.notify_zalo,
                zalo_user_id = EXCLUDED.zalo_user_id,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tm", theme);
        cmd.Parameters.AddWithValue("ac", accent);
        cmd.Parameters.AddWithValue("cm", req.CompactMode);
        cmd.Parameters.AddWithValue("lang", lang);
        cmd.Parameters.AddWithValue("nia", req.NotifyInApp);
        cmd.Parameters.AddWithValue("ns", req.NotifySound);
        cmd.Parameters.AddWithValue("nd", req.NotifyDesktop);
        cmd.Parameters.AddWithValue("nc", req.NotifyChat);
        cmd.Parameters.AddWithValue("nt", req.NotifyTaskAssigned);
        cmd.Parameters.AddWithValue("na", req.NotifyApproval);
        cmd.Parameters.AddWithValue("nm", req.NotifyMentions);
        cmd.Parameters.AddWithValue("ed", digest);
        cmd.Parameters.AddWithValue("ce", req.ChatEnterToSend);
        cmd.Parameters.AddWithValue("nem", req.NotifyEmail);
        cmd.Parameters.AddWithValue("ntel", req.NotifyTelegram);
        cmd.Parameters.AddWithValue("tcid", string.IsNullOrWhiteSpace(req.TelegramChatId) ? DBNull.Value : req.TelegramChatId.Trim());
        cmd.Parameters.AddWithValue("nzal", req.NotifyZalo);
        cmd.Parameters.AddWithValue("zuid", string.IsNullOrWhiteSpace(req.ZaloUserId) ? DBNull.Value : req.ZaloUserId.Trim());
        await cmd.ExecuteNonQueryAsync();

        return await GetUserSettingsAsync(userId);
    }

    public async Task<SysUserDto?> UpdateProfileAsync(string userId, string username, UpdateProfileRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_users
            SET full_name = @fn,
                email = @em,
                department = @dept
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("fn", req.FullName.Trim());
        cmd.Parameters.AddWithValue("em", string.IsNullOrWhiteSpace(req.Email) ? DBNull.Value : req.Email.Trim());
        cmd.Parameters.AddWithValue("dept", string.IsNullOrWhiteSpace(req.Department) ? "Toàn công ty" : req.Department.Trim());
        await cmd.ExecuteNonQueryAsync();

        return await _auth.GetByUsernameAsync(username);
    }

    public async Task<SystemSettingsDto> GetSystemSettingsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT setting_value::text FROM sys_system_settings WHERE setting_key = 'workspace_config' LIMIT 1", conn);
        var raw = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrWhiteSpace(raw)) return new SystemSettingsDto();
        return JsonSerializer.Deserialize<SystemSettingsDto>(raw, JsonOpts) ?? new SystemSettingsDto();
    }

    private static string SanitizeMediaUrl(string? raw)
    {
        var val = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(val)) return string.Empty;
        if (val.StartsWith("/", StringComparison.Ordinal)
            || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return val;
        }
        return string.Empty;
    }

    public async Task<SystemSettingsDto> UpdateSystemSettingsAsync(SystemSettingsDto req, string updatedBy)
    {
        var normalized = new SystemSettingsDto
        {
            CompanyName = string.IsNullOrWhiteSpace(req.CompanyName) ? "SmartApps Enterprise" : req.CompanyName.Trim(),
            WorkspaceTitle = string.IsNullOrWhiteSpace(req.WorkspaceTitle) ? "SmartApps Enterprise — Không gian làm việc Đa ứng dụng" : req.WorkspaceTitle.Trim(),
            LogoUrl = SanitizeMediaUrl(req.LogoUrl),
            BrandIcon = req.BrandIcon?.Trim() ?? string.Empty,
            FaviconUrl = SanitizeMediaUrl(req.FaviconUrl),
            AnnouncementBanner = req.AnnouncementBanner?.Trim() ?? string.Empty,
            AllowChatFileUpload = req.AllowChatFileUpload,
            AllowTaskFromChat = req.AllowTaskFromChat,
            DefaultTaskAppCode = string.IsNullOrWhiteSpace(req.DefaultTaskAppCode) ? "OPS" : req.DefaultTaskAppCode.Trim().ToUpperInvariant(),
            SessionTimeoutDays = Math.Clamp(req.SessionTimeoutDays, 1, 30)
        };

        var json = JsonSerializer.Serialize(normalized, JsonOpts);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_system_settings (setting_key, setting_value, description, updated_by, updated_at)
            VALUES ('workspace_config', @val::jsonb, 'Cấu hình chung toàn hệ thống SmartApps', @ub, CURRENT_TIMESTAMP)
            ON CONFLICT (setting_key) DO UPDATE SET
                setting_value = EXCLUDED.setting_value,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("val", json);
        cmd.Parameters.AddWithValue("ub", updatedBy);
        await cmd.ExecuteNonQueryAsync();

        _realtime.Publish("system_settings_updated", null, null, null, "*", normalized);
        return normalized;
    }
}
