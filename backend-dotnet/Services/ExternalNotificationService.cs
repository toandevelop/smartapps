using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class ExternalNotificationService
{
    private static readonly string[] SecretKeys = { "password", "botToken", "accessToken", "secretValue", "secretHeader" };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _connStr;
    private readonly IHttpClientFactory _httpFactory;

    public ExternalNotificationService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _httpFactory = httpFactory;
    }

    public async Task<List<NotificationChannelDto>> ListChannelsForUserAsync(string userId, bool isAdmin)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = isAdmin
            ? """
              SELECT id, owner_scope, owner_user_id, channel_type, name, is_enabled,
                     COALESCE(event_categories::text, '["ALL"]'), COALESCE(config::text, '{}'), updated_at
              FROM sys_notification_channels
              WHERE owner_scope = 'SYSTEM' OR owner_user_id = @uid
              ORDER BY owner_scope ASC, created_at ASC
              """
            : """
              SELECT id, owner_scope, owner_user_id, channel_type, name, is_enabled,
                     COALESCE(event_categories::text, '["ALL"]'), COALESCE(config::text, '{}'), updated_at
              FROM sys_notification_channels
              WHERE owner_user_id = @uid
              ORDER BY created_at ASC
              """;

        var list = new List<NotificationChannelDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("uid", userId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(ReadChannelMasked(r));
        }
        return list;
    }

    public async Task<(NotificationChannelDto? Channel, string? Error)> UpsertChannelAsync(
        UpsertNotificationChannelRequest req,
        string userId,
        bool isAdmin)
    {
        var scope = req.OwnerScope?.Trim().ToUpperInvariant() == "USER" ? "USER" : "SYSTEM";
        if (scope == "SYSTEM" && !isAdmin)
            return (null, "Chỉ Quản trị viên mới được cấu hình kênh gửi chung toàn hệ thống.");

        if (string.IsNullOrWhiteSpace(req.Name))
            return (null, "Tên kênh thông báo là bắt buộc.");

        var channelType = string.IsNullOrWhiteSpace(req.ChannelType)
            ? "WEBHOOK"
            : req.ChannelType.Trim().ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"nch_{channelType.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..6]}"
            : req.Id.Trim();

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Load existing config to verify ownership & preserve secrets when blank
        var existingConfig = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using (var chk = new NpgsqlCommand(
            "SELECT owner_scope, owner_user_id, COALESCE(config::text, '{}') FROM sys_notification_channels WHERE id = @id LIMIT 1", conn))
        {
            chk.Parameters.AddWithValue("id", id);
            await using var rr = await chk.ExecuteReaderAsync();
            if (await rr.ReadAsync())
            {
                var exScope = rr.GetString(0);
                var exOwner = rr.IsDBNull(1) ? null : rr.GetString(1);
                if (!isAdmin && (exScope == "SYSTEM" || !string.Equals(exOwner, userId, StringComparison.OrdinalIgnoreCase)))
                    return (null, "Bạn không có quyền chỉnh sửa kênh thông báo này.");

                existingConfig = ParseConfigDict(rr.GetString(2));
            }
        }

        var mergedConfig = MergeConfigPreservingSecrets(existingConfig, req.Config ?? new());
        var categories = (req.EventCategories ?? new List<string> { "ALL" })
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        if (categories.Count == 0) categories.Add("ALL");

        var sql = """
            INSERT INTO sys_notification_channels (
                id, owner_scope, owner_user_id, channel_type, name, is_enabled, event_categories, config, updated_at
            )
            VALUES (
                @id, @sc, @uid, @ct, @nm, @en, @cats::jsonb, @cfg::jsonb, CURRENT_TIMESTAMP
            )
            ON CONFLICT (id) DO UPDATE SET
                channel_type = EXCLUDED.channel_type,
                name = EXCLUDED.name,
                is_enabled = EXCLUDED.is_enabled,
                event_categories = EXCLUDED.event_categories,
                config = EXCLUDED.config,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("sc", scope);
            cmd.Parameters.AddWithValue("uid", scope == "USER" ? userId : DBNull.Value);
            cmd.Parameters.AddWithValue("ct", channelType);
            cmd.Parameters.AddWithValue("nm", req.Name.Trim());
            cmd.Parameters.AddWithValue("en", req.IsEnabled);
            cmd.Parameters.AddWithValue("cats", JsonSerializer.Serialize(categories, JsonOpts));
            cmd.Parameters.AddWithValue("cfg", JsonSerializer.Serialize(mergedConfig, JsonOpts));
            await cmd.ExecuteNonQueryAsync();
        }

        await using var fetch = new NpgsqlCommand("""
            SELECT id, owner_scope, owner_user_id, channel_type, name, is_enabled,
                   COALESCE(event_categories::text, '["ALL"]'), COALESCE(config::text, '{}'), updated_at
            FROM sys_notification_channels WHERE id = @id LIMIT 1
            """, conn);
        fetch.Parameters.AddWithValue("id", id);
        await using var r = await fetch.ExecuteReaderAsync();
        await r.ReadAsync();
        return (ReadChannelMasked(r), null);
    }

    public async Task<(bool Ok, string? Error)> DeleteChannelAsync(string id, string userId, bool isAdmin)
    {
        if (id is "nch_gmail_sys" or "nch_telegram_sys" or "nch_zalo_sys")
            return (false, "Không thể xóa kênh mặc định hệ thống. Bạn có thể tắt kích hoạt nếu không dùng.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = isAdmin
            ? "DELETE FROM sys_notification_channels WHERE id = @id"
            : "DELETE FROM sys_notification_channels WHERE id = @id AND owner_scope = 'USER' AND owner_user_id = @uid";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        if (!isAdmin) cmd.Parameters.AddWithValue("uid", userId);
        return await cmd.ExecuteNonQueryAsync() > 0
            ? (true, null)
            : (false, "Không tìm thấy hoặc không có quyền xóa kênh thông báo.");
    }

    public async Task<(bool Ok, string Detail)> TestChannelAsync(string id, string userId, bool isAdmin)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, owner_scope, owner_user_id, channel_type, name, COALESCE(config::text, '{}')
            FROM sys_notification_channels
            WHERE id = @id
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (false, "Không tìm thấy kênh thông báo.");

        var scope = r.GetString(1);
        var owner = r.IsDBNull(2) ? null : r.GetString(2);
        if (!isAdmin && (scope == "SYSTEM" || !string.Equals(owner, userId, StringComparison.OrdinalIgnoreCase)))
            return (false, "Không có quyền gửi thử kênh thông báo này.");

        var type = r.GetString(3);
        var name = r.GetString(4);
        var cfg = ParseConfigDict(r.GetString(5));
        await r.CloseAsync();

        var title = "Kiểm tra kết nối thông báo SmartApps Enterprise";
        var msg = $"Đây là tin nhắn thử nghiệm từ kênh \"{name}\" lúc {DateTime.Now:HH:mm:ss dd/MM/yyyy}. Kết nối hoạt động bình thường!";

        var (ok, detail) = await SendSingleChannelAsync(type, cfg, title, msg, "SYSTEM", null, "/settings");
        await WriteDeliveryLogAsync(conn, id, type, name, title, ok, detail);
        return (ok, detail);
    }

    public async Task<List<NotificationDeliveryLogDto>> ListDeliveryLogsAsync(int limit = 25)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var list = new List<NotificationDeliveryLogDto>();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, channel_id, channel_type, channel_name, title, status, detail, created_at
            FROM sys_notification_delivery_logs
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 100));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new NotificationDeliveryLogDto
            {
                Id = r.GetString(0),
                ChannelId = r.GetString(1),
                ChannelType = r.GetString(2),
                ChannelName = r.GetString(3),
                Title = r.GetString(4),
                Status = r.GetString(5),
                Detail = r.IsDBNull(6) ? null : r.GetString(6),
                CreatedAt = r.GetDateTime(7)
            });
        }
        return list;
    }

    public void DispatchInBackground(
        string title,
        string message,
        string? appCode,
        string recipient,
        string category,
        string? linkUrl)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DispatchForNotificationAsync(title, message, appCode, recipient, category, linkUrl);
            }
            catch
            {
                // External delivery failures must never crash background task
            }
        });
    }

    public async Task DispatchForNotificationAsync(
        string title,
        string message,
        string? appCode,
        string recipient,
        string category,
        string? linkUrl)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // 1) Resolve target user personal settings & email if targeted
        string? targetUserId = null;
        string? targetEmail = null;
        bool userNotifyEmail = false;
        bool userNotifyTelegram = false;
        string? userTelegramChatId = null;
        bool userNotifyZalo = false;
        string? userZaloUserId = null;

        if (!string.IsNullOrWhiteSpace(recipient) && recipient != "*")
        {
            await using var uCmd = new NpgsqlCommand("""
                SELECT u.id, u.email,
                       COALESCE(s.notify_email, FALSE),
                       COALESCE(s.notify_telegram, FALSE), s.telegram_chat_id,
                       COALESCE(s.notify_zalo, FALSE), s.zalo_user_id
                FROM sys_users u
                LEFT JOIN sys_user_settings s ON s.user_id = u.id
                WHERE LOWER(u.username) = LOWER(@u) AND u.is_active = TRUE
                LIMIT 1
                """, conn);
            uCmd.Parameters.AddWithValue("u", recipient.Trim());
            await using var ur = await uCmd.ExecuteReaderAsync();
            if (await ur.ReadAsync())
            {
                targetUserId = ur.GetString(0);
                targetEmail = ur.IsDBNull(1) ? null : ur.GetString(1);
                userNotifyEmail = ur.GetBoolean(2);
                userNotifyTelegram = ur.GetBoolean(3);
                userTelegramChatId = ur.IsDBNull(4) ? null : ur.GetString(4);
                userNotifyZalo = ur.GetBoolean(5);
                userZaloUserId = ur.IsDBNull(6) ? null : ur.GetString(6);
            }
        }

        // 2) Load active SYSTEM channels + recipient's USER channels
        var channels = new List<(string Id, string Scope, string? OwnerId, string Type, string Name, bool Enabled, List<string> Cats, Dictionary<string, object?> Config)>();
        await using (var cCmd = new NpgsqlCommand("""
            SELECT id, owner_scope, owner_user_id, channel_type, name, is_enabled,
                   COALESCE(event_categories::text, '["ALL"]'), COALESCE(config::text, '{}')
            FROM sys_notification_channels
            WHERE owner_scope = 'SYSTEM'
               OR (@uid IS NOT NULL AND owner_user_id = @uid)
            """, conn))
        {
            cCmd.Parameters.AddWithValue("uid", (object?)targetUserId ?? DBNull.Value);
            await using var cr = await cCmd.ExecuteReaderAsync();
            while (await cr.ReadAsync())
            {
                channels.Add((
                    cr.GetString(0),
                    cr.GetString(1),
                    cr.IsDBNull(2) ? null : cr.GetString(2),
                    cr.GetString(3),
                    cr.GetString(4),
                    cr.GetBoolean(5),
                    JsonSerializer.Deserialize<List<string>>(cr.GetString(6)) ?? new(),
                    ParseConfigDict(cr.GetString(7))
                ));
            }
        }

        foreach (var ch in channels)
        {
            if (!ch.Enabled || !IsCategoryMatched(ch.Cats, category)) continue;

            var cfgCopy = new Dictionary<string, object?>(ch.Config, StringComparer.OrdinalIgnoreCase);
            if (ch.Type == "GMAIL_SMTP"
                && GetBool(cfgCopy, "useRecipientEmail")
                && !string.IsNullOrWhiteSpace(targetEmail)
                && string.IsNullOrWhiteSpace(GetString(cfgCopy, "toEmails")))
            {
                cfgCopy["toEmails"] = targetEmail;
            }

            var (ok, detail) = await SendSingleChannelAsync(ch.Type, cfgCopy, title, message, category, appCode, linkUrl);
            await WriteDeliveryLogAsync(conn, ch.Id, ch.Type, ch.Name, title, ok, detail);
        }

        // 3) Personal user opt-in delivery piggybacking on configured company SMTP / Telegram Bot / Zalo relay
        if (targetUserId != null)
        {
            if (userNotifyEmail && !string.IsNullOrWhiteSpace(targetEmail))
            {
                var gmailRelay = channels.FirstOrDefault(x => x.Id == "nch_gmail_sys" || (x.Scope == "SYSTEM" && x.Type == "GMAIL_SMTP"));
                if (gmailRelay.Config != null && !string.IsNullOrWhiteSpace(GetString(gmailRelay.Config, "username")))
                {
                    var pCfg = new Dictionary<string, object?>(gmailRelay.Config, StringComparer.OrdinalIgnoreCase)
                    {
                        ["toEmails"] = targetEmail
                    };
                    var (ok, detail) = await SendSingleChannelAsync("GMAIL_SMTP", pCfg, title, message, category, appCode, linkUrl);
                    await WriteDeliveryLogAsync(conn, "personal_gmail", "GMAIL_SMTP", $"Gmail cá nhân ({targetEmail})", title, ok, detail);
                }
            }

            if (userNotifyTelegram && !string.IsNullOrWhiteSpace(userTelegramChatId))
            {
                var tgRelay = channels.FirstOrDefault(x => x.Id == "nch_telegram_sys" || (x.Scope == "SYSTEM" && x.Type == "TELEGRAM"));
                if (tgRelay.Config != null && !string.IsNullOrWhiteSpace(GetString(tgRelay.Config, "botToken")))
                {
                    var pCfg = new Dictionary<string, object?>(tgRelay.Config, StringComparer.OrdinalIgnoreCase)
                    {
                        ["chatId"] = userTelegramChatId
                    };
                    var (ok, detail) = await SendSingleChannelAsync("TELEGRAM", pCfg, title, message, category, appCode, linkUrl);
                    await WriteDeliveryLogAsync(conn, "personal_telegram", "TELEGRAM", $"Telegram cá nhân ({userTelegramChatId})", title, ok, detail);
                }
            }

            if (userNotifyZalo && !string.IsNullOrWhiteSpace(userZaloUserId))
            {
                var zaloRelay = channels.FirstOrDefault(x => x.Id == "nch_zalo_sys" || (x.Scope == "SYSTEM" && x.Type == "ZALO"));
                if (zaloRelay.Config != null &&
                    (!string.IsNullOrWhiteSpace(GetString(zaloRelay.Config, "accessToken"))
                     || !string.IsNullOrWhiteSpace(GetString(zaloRelay.Config, "webhookUrl"))))
                {
                    var pCfg = new Dictionary<string, object?>(zaloRelay.Config, StringComparer.OrdinalIgnoreCase)
                    {
                        ["targetId"] = userZaloUserId
                    };
                    var (ok, detail) = await SendSingleChannelAsync("ZALO", pCfg, title, message, category, appCode, linkUrl);
                    await WriteDeliveryLogAsync(conn, "personal_zalo", "ZALO", $"Zalo cá nhân ({userZaloUserId})", title, ok, detail);
                }
            }
        }
    }

    public async Task<(bool Ok, string Detail)> SendSingleChannelAsync(
        string channelType,
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode,
        string? linkUrl)
    {
        try
        {
            switch (channelType.Trim().ToUpperInvariant())
            {
                case "GMAIL_SMTP":
                    return await SendSmtpEmailAsync(cfg, title, message, category, appCode, linkUrl);
                case "TELEGRAM":
                    return await SendTelegramAsync(cfg, title, message, category, appCode);
                case "ZALO":
                    return await SendZaloAsync(cfg, title, message, category, appCode, linkUrl);
                case "TEAMS":
                    return await SendTeamsAsync(cfg, title, message, category, appCode);
                case "SLACK":
                    return await SendSlackAsync(cfg, title, message, category, appCode);
                default:
                    return await SendGenericWebhookAsync(cfg, title, message, category, appCode, linkUrl);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi gửi ({channelType}): {ex.Message}");
        }
    }

    private async Task<(bool Ok, string Detail)> SendSmtpEmailAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode,
        string? linkUrl)
    {
        var host = GetString(cfg, "smtpHost") ?? "smtp.gmail.com";
        var port = GetInt(cfg, "smtpPort", 587);
        var useSsl = GetBool(cfg, "useSsl", true);
        var username = GetString(cfg, "username");
        var password = GetString(cfg, "password");
        var fromName = GetString(cfg, "fromName") ?? "SmartApps Enterprise";
        var toRaw = GetString(cfg, "toEmails");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Thiếu Tài khoản Gmail / SMTP hoặc App Password.");
        if (string.IsNullOrWhiteSpace(toRaw))
            return (false, "Chưa có địa chỉ Email người nhận.");

        var recipients = toRaw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Contains('@'))
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
            return (false, "Danh sách email nhận không hợp lệ.");

        using var mail = new MailMessage
        {
            From = new MailAddress(username, fromName, Encoding.UTF8),
            Subject = $"[SmartApps{(string.IsNullOrWhiteSpace(appCode) ? "" : $" - {appCode}")}] {title}",
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            Body = $"""
                <div style="font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:0 auto;border:1px solid #e0e0e0;border-radius:12px;overflow:hidden;">
                  <div style="background:#1a73e8;color:#fff;padding:16px 20px;font-weight:700;font-size:16px;">
                    SmartApps Enterprise • {WebUtility.HtmlEncode(category)}
                  </div>
                  <div style="padding:20px;background:#ffffff;color:#202124;">
                    <h2 style="margin:0 0 10px;font-size:17px;">{WebUtility.HtmlEncode(title)}</h2>
                    <p style="margin:0 0 14px;font-size:14px;line-height:1.55;color:#3c4043;">{WebUtility.HtmlEncode(message)}</p>
                    {(string.IsNullOrWhiteSpace(appCode) ? "" : $"<div style=\"font-size:12px;color:#5f6368;\">Ứng dụng: <strong>{WebUtility.HtmlEncode(appCode)}</strong></div>")}
                    {(string.IsNullOrWhiteSpace(linkUrl) ? "" : $"<div style=\"margin-top:8px;font-size:12px;color:#1a73e8;\">Đường dẫn trong hệ thống: {WebUtility.HtmlEncode(linkUrl)}</div>")}
                  </div>
                </div>
                """
        };
        foreach (var rcpt in recipients) mail.To.Add(rcpt);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl,
            Credentials = new NetworkCredential(username, password),
            Timeout = 15000
        };
        await client.SendMailAsync(mail);
        return (true, $"Đã gửi Email thành công tới: {string.Join(", ", recipients)}");
    }

    private async Task<(bool Ok, string Detail)> SendTelegramAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode)
    {
        var botToken = GetString(cfg, "botToken");
        var chatId = GetString(cfg, "chatId");
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Thiếu Bot Token hoặc Chat ID / Group ID Telegram.");

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var url = $"https://api.telegram.org/bot{Uri.EscapeDataString(botToken)}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text = FormatTelegramHtml(title, message, category, appCode),
            parse_mode = "HTML",
            disable_web_page_preview = true
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(url, content);
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode
            ? (true, $"Đã gửi tới Telegram Chat ID {chatId}.")
            : (false, $"Telegram API trả lỗi ({(int)resp.StatusCode}): {Truncate(body, 220)}");
    }

    private async Task<(bool Ok, string Detail)> SendZaloAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode,
        string? linkUrl)
    {
        var mode = (GetString(cfg, "zaloMode") ?? "WEBHOOK").ToUpperInvariant();
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        if (mode == "OA_API")
        {
            var accessToken = GetString(cfg, "accessToken");
            var targetId = GetString(cfg, "targetId");
            var endpoint = GetString(cfg, "endpointUrl") ?? "https://openapi.zalo.me/v3.0/oa/message/cs";
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(targetId))
                return (false, "Thiếu Zalo OA Access Token hoặc User/Group ID.");

            var isGroupEndpoint = endpoint.Contains("group", StringComparison.OrdinalIgnoreCase);
            var recipientObj = isGroupEndpoint
                ? (object)new { group_id = targetId }
                : new { user_id = targetId };
            var payload = new
            {
                recipient = recipientObj,
                message = new { text = $"[{category}{(string.IsNullOrWhiteSpace(appCode) ? "" : $"/{appCode}")}] {title}\n{message}" }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("access_token", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode
                ? (true, $"Đã gửi qua Zalo OA API tới {targetId}.")
                : (false, $"Zalo OA API lỗi ({(int)resp.StatusCode}): {Truncate(body, 220)}");
        }
        else
        {
            var webhookUrl = GetString(cfg, "webhookUrl");
            var targetId = GetString(cfg, "targetId");
            var secret = GetString(cfg, "secretHeader");
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return (false, "Thiếu Webhook URL của nhóm Zalo / Zalo Bot Bridge.");

            var payload = new
            {
                channel = "ZALO",
                targetId,
                title,
                message,
                text = $"📢 [{category}] {title}\n{message}",
                category,
                appCode,
                linkUrl,
                createdAt = DateTime.UtcNow
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
            if (!string.IsNullOrWhiteSpace(secret))
                req.Headers.TryAddWithoutValidation("X-Webhook-Secret", secret);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? (true, $"Đã gửi qua Zalo Group Webhook ({(int)resp.StatusCode}).")
                : (false, $"Zalo Webhook trả mã lỗi {(int)resp.StatusCode}.");
        }
    }

    private async Task<(bool Ok, string Detail)> SendTeamsAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode)
    {
        var webhookUrl = GetString(cfg, "webhookUrl");
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return (false, "Thiếu Incoming Webhook URL của Microsoft Teams.");

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var payload = new Dictionary<string, object?>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "http://schema.org/extensions",
            ["themeColor"] = "1a73e8",
            ["summary"] = title,
            ["title"] = $"[{category}{(string.IsNullOrWhiteSpace(appCode) ? "" : $" • {appCode}")}] {title}",
            ["text"] = message
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(webhookUrl, content);
        return resp.IsSuccessStatusCode
            ? (true, "Đã gửi tới kênh Microsoft Teams.")
            : (false, $"Teams Webhook lỗi ({(int)resp.StatusCode}).");
    }

    private async Task<(bool Ok, string Detail)> SendSlackAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode)
    {
        var webhookUrl = GetString(cfg, "webhookUrl");
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return (false, "Thiếu Incoming Webhook URL của Slack.");

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var payload = new
        {
            text = $"*[{category}{(string.IsNullOrWhiteSpace(appCode) ? "" : $" • {appCode}")}] {title}*\n{message}"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(webhookUrl, content);
        return resp.IsSuccessStatusCode
            ? (true, "Đã gửi tới kênh Slack.")
            : (false, $"Slack Webhook lỗi ({(int)resp.StatusCode}).");
    }

    private async Task<(bool Ok, string Detail)> SendGenericWebhookAsync(
        Dictionary<string, object?> cfg,
        string title,
        string message,
        string category,
        string? appCode,
        string? linkUrl)
    {
        var webhookUrl = GetString(cfg, "webhookUrl");
        var secret = GetString(cfg, "secretValue");
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return (false, "Thiếu Webhook URL.");

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var payload = new
        {
            @event = "smartapps.notification",
            title,
            message,
            category,
            appCode,
            linkUrl,
            timestamp = DateTime.UtcNow
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
        if (!string.IsNullOrWhiteSpace(secret))
            req.Headers.TryAddWithoutValidation("X-Webhook-Secret", secret);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(req);
        return resp.IsSuccessStatusCode
            ? (true, $"Webhook trả về HTTP {(int)resp.StatusCode}.")
            : (false, $"Webhook trả lỗi HTTP {(int)resp.StatusCode}.");
    }

    private static string EscapeTelegramHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string FormatTelegramHtml(string title, string message, string category, string? appCode)
    {
        var tag = string.IsNullOrWhiteSpace(appCode) ? category : $"{category} • {appCode}";
        return $"<b>🔔 [{EscapeTelegramHtml(tag)}] {EscapeTelegramHtml(title)}</b>\n{EscapeTelegramHtml(message)}";
    }

    public static bool IsCategoryMatched(IReadOnlyCollection<string> configuredCategories, string category)
    {
        if (configuredCategories == null || configuredCategories.Count == 0) return true;
        return configuredCategories.Any(c =>
            c.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            || c.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    public static (Dictionary<string, object?> Masked, bool HasSecret) MaskConfigSecrets(Dictionary<string, object?> config)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        bool hasSecret = false;
        foreach (var kv in config)
        {
            if (SecretKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var raw = kv.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(raw)) hasSecret = true;
                result[kv.Key] = "";
            }
            else
            {
                result[kv.Key] = kv.Value;
            }
        }
        return (result, hasSecret);
    }

    private static Dictionary<string, object?> MergeConfigPreservingSecrets(
        Dictionary<string, object?> existing,
        Dictionary<string, object?> incoming)
    {
        var merged = new Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in incoming)
        {
            if (SecretKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var incomingVal = kv.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(incomingVal) && incomingVal != "••••••••")
                {
                    merged[kv.Key] = incomingVal;
                }
            }
            else
            {
                merged[kv.Key] = kv.Value;
            }
        }
        return merged;
    }

    private static NotificationChannelDto ReadChannelMasked(NpgsqlDataReader r)
    {
        var rawConfig = ParseConfigDict(r.GetString(7));
        var (masked, hasSecret) = MaskConfigSecrets(rawConfig);
        return new NotificationChannelDto
        {
            Id = r.GetString(0),
            OwnerScope = r.GetString(1),
            OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
            ChannelType = r.GetString(3),
            Name = r.GetString(4),
            IsEnabled = r.GetBoolean(5),
            EventCategories = JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new() { "ALL" },
            Config = masked,
            HasSecret = hasSecret,
            UpdatedAt = r.GetDateTime(8)
        };
    }

    private static Dictionary<string, object?> ParseConfigDict(string rawJson)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                map[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => p.Value.ToString()
                };
            }
        }
        catch
        {
            // Ignore malformed JSON
        }
        return map;
    }

    private static string? GetString(Dictionary<string, object?> cfg, string key)
    {
        if (!cfg.TryGetValue(key, out var val) || val == null) return null;
        var s = val.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int GetInt(Dictionary<string, object?> cfg, string key, int fallback)
    {
        if (!cfg.TryGetValue(key, out var val) || val == null) return fallback;
        return int.TryParse(val.ToString(), out var i) ? i : fallback;
    }

    private static bool GetBool(Dictionary<string, object?> cfg, string key, bool fallback = false)
    {
        if (!cfg.TryGetValue(key, out var val) || val == null) return fallback;
        return bool.TryParse(val.ToString(), out var b) ? b : fallback;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";

    private static async Task WriteDeliveryLogAsync(
        NpgsqlConnection conn,
        string channelId,
        string channelType,
        string channelName,
        string title,
        bool ok,
        string detail)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_notification_delivery_logs (id, channel_id, channel_type, channel_name, title, status, detail)
            VALUES (@id, @cid, @ct, @cn, @t, @st, @det)
            """, conn);
        cmd.Parameters.AddWithValue("id", $"ndl_{Guid.NewGuid().ToString("N")[..10]}");
        cmd.Parameters.AddWithValue("cid", channelId);
        cmd.Parameters.AddWithValue("ct", channelType);
        cmd.Parameters.AddWithValue("cn", channelName);
        cmd.Parameters.AddWithValue("t", Truncate(title, 240));
        cmd.Parameters.AddWithValue("st", ok ? "SUCCESS" : "FAILED");
        cmd.Parameters.AddWithValue("det", Truncate(detail, 500));
        await cmd.ExecuteNonQueryAsync();
    }
}
