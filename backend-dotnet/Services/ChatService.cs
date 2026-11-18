using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class ChatService
{
    private readonly string _connStr;
    private readonly TaskService _tasks;
    private readonly NotificationService _notifications;
    private readonly RealtimeEventService _realtime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ChatService(
        IConfiguration config,
        TaskService tasks,
        NotificationService notifications,
        RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _tasks = tasks;
        _notifications = notifications;
        _realtime = realtime;
    }

    public async Task<List<ChatUserSummaryDto>> ListChatUsersAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, username, full_name, department, role_code
            FROM sys_users
            WHERE is_active = TRUE
            ORDER BY full_name ASC
            """, conn);
        var list = new List<ChatUserSummaryDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ChatUserSummaryDto
            {
                Id = r.GetString(0),
                Username = r.GetString(1),
                FullName = r.GetString(2),
                Department = r.GetString(3),
                RoleCode = r.GetString(4)
            });
        }
        return list;
    }

    public async Task<List<ChatChannelDto>> ListChannelsAsync(string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Ensure current user has membership rows for all PUBLIC channels so unread tracking works accurately
        await using (var syncPub = new NpgsqlCommand("""
            INSERT INTO chat_channel_members (channel_id, username, role, last_read_at)
            SELECT id, @u, 'MEMBER', CURRENT_TIMESTAMP - INTERVAL '1 day'
            FROM chat_channels
            WHERE channel_type = 'PUBLIC' AND is_archived = FALSE
            ON CONFLICT (channel_id, username) DO NOTHING
            """, conn))
        {
            syncPub.Parameters.AddWithValue("u", username);
            await syncPub.ExecuteNonQueryAsync();
        }

        var sql = """
            SELECT c.id, c.name, c.description, c.channel_type, c.avatar_color, c.linked_app_code,
                   c.created_by, c.last_message_at, c.last_message_preview,
                   COALESCE(mem.is_muted, FALSE) AS is_muted,
                   (SELECT COUNT(*) FROM chat_channel_members m2 WHERE m2.channel_id = c.id) AS member_count,
                   (SELECT COUNT(*) FROM chat_messages msg
                    WHERE msg.channel_id = c.id
                      AND msg.deleted_at IS NULL
                      AND LOWER(msg.sender_username) <> LOWER(@u)
                      AND msg.created_at > COALESCE(mem.last_read_at, '1970-01-01'::timestamptz)) AS unread_count
            FROM chat_channels c
            LEFT JOIN chat_channel_members mem ON mem.channel_id = c.id AND LOWER(mem.username) = LOWER(@u)
            WHERE c.is_archived = FALSE
              AND (c.channel_type = 'PUBLIC' OR mem.username IS NOT NULL)
            ORDER BY c.last_message_at DESC
            """;

        var channels = new List<ChatChannelDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("u", username);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                channels.Add(new ChatChannelDto
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    ChannelType = r.GetString(3),
                    AvatarColor = r.GetString(4),
                    LinkedAppCode = r.IsDBNull(5) ? null : r.GetString(5),
                    CreatedBy = r.GetString(6),
                    LastMessageAt = r.GetDateTime(7),
                    LastMessagePreview = r.IsDBNull(8) ? null : r.GetString(8),
                    IsMuted = r.GetBoolean(9),
                    MemberCount = Convert.ToInt32(r.GetInt64(10)),
                    UnreadCount = Convert.ToInt32(r.GetInt64(11))
                });
            }
        }

        // Hydrate members for each channel (and resolve peer display name for DIRECT channels)
        if (channels.Count > 0)
        {
            var ids = channels.Select(c => c.Id).ToArray();
            var memberMap = new Dictionary<string, List<ChatUserSummaryDto>>();
            await using var memCmd = new NpgsqlCommand("""
                SELECT m.channel_id, COALESCE(u.id, m.username), m.username,
                       COALESCE(u.full_name, m.username), COALESCE(u.department, ''), COALESCE(u.role_code, 'STAFF')
                FROM chat_channel_members m
                LEFT JOIN sys_users u ON LOWER(u.username) = LOWER(m.username)
                WHERE m.channel_id = ANY(@ids)
                ORDER BY m.joined_at ASC
                """, conn);
            memCmd.Parameters.AddWithValue("ids", ids);
            await using var mr = await memCmd.ExecuteReaderAsync();
            while (await mr.ReadAsync())
            {
                var chId = mr.GetString(0);
                if (!memberMap.ContainsKey(chId)) memberMap[chId] = new();
                memberMap[chId].Add(new ChatUserSummaryDto
                {
                    Id = mr.GetString(1),
                    Username = mr.GetString(2),
                    FullName = mr.GetString(3),
                    Department = mr.GetString(4),
                    RoleCode = mr.GetString(5)
                });
            }

            foreach (var ch in channels)
            {
                if (memberMap.TryGetValue(ch.Id, out var mems))
                {
                    ch.Members = mems;
                    if (ch.ChannelType == "DIRECT")
                    {
                        var peer = mems.FirstOrDefault(m => !m.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                                   ?? mems.FirstOrDefault();
                        if (peer != null)
                        {
                            ch.Name = peer.FullName;
                            ch.Description = $"{peer.Department} • @{peer.Username}";
                        }
                    }
                }
            }
        }

        return channels;
    }

    public async Task<ChatChannelDto> CreateChannelAsync(CreateChannelRequest req, string creatorUsername)
    {
        var chId = $"ch_{Guid.NewGuid().ToString("N")[..10]}";
        var type = req.ChannelType?.Trim().ToUpperInvariant() is "PUBLIC" or "DEPARTMENT" ? req.ChannelType.Trim().ToUpperInvariant() : "GROUP";
        var color = string.IsNullOrWhiteSpace(req.AvatarColor) ? "#1a73e8" : req.AvatarColor.Trim();

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO chat_channels (id, name, description, channel_type, avatar_color, linked_app_code, created_by, last_message_preview)
            VALUES (@id, @n, @d, @t, @col, @app, @cb, @prev)
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", chId);
            cmd.Parameters.AddWithValue("n", req.Name.Trim());
            cmd.Parameters.AddWithValue("d", string.IsNullOrWhiteSpace(req.Description) ? DBNull.Value : req.Description.Trim());
            cmd.Parameters.AddWithValue("t", type);
            cmd.Parameters.AddWithValue("col", color);
            cmd.Parameters.AddWithValue("app", string.IsNullOrWhiteSpace(req.LinkedAppCode) ? DBNull.Value : req.LinkedAppCode.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("cb", creatorUsername);
            cmd.Parameters.AddWithValue("prev", $"Kênh được tạo bởi @{creatorUsername}");
            await cmd.ExecuteNonQueryAsync();
        }

        var members = req.MemberUsernames
            .Append(creatorUsername)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var m in members)
        {
            await using var mCmd = new NpgsqlCommand("""
                INSERT INTO chat_channel_members (channel_id, username, role)
                VALUES (@ch, @u, @r)
                ON CONFLICT DO NOTHING
                """, conn, tx);
            mCmd.Parameters.AddWithValue("ch", chId);
            mCmd.Parameters.AddWithValue("u", m);
            mCmd.Parameters.AddWithValue("r", m.Equals(creatorUsername, StringComparison.OrdinalIgnoreCase) ? "OWNER" : "MEMBER");
            await mCmd.ExecuteNonQueryAsync();
        }

        await using (var sysMsg = new NpgsqlCommand("""
            INSERT INTO chat_messages (id, channel_id, sender_username, message_type, content)
            VALUES (@id, @ch, @snd, 'SYSTEM', @cnt)
            """, conn, tx))
        {
            sysMsg.Parameters.AddWithValue("id", $"msg_{Guid.NewGuid().ToString("N")[..10]}");
            sysMsg.Parameters.AddWithValue("ch", chId);
            sysMsg.Parameters.AddWithValue("snd", creatorUsername);
            sysMsg.Parameters.AddWithValue("cnt", $"@{creatorUsername} đã tạo nhóm \"{req.Name.Trim()}\".");
            await sysMsg.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        foreach (var m in members.Where(x => !x.Equals(creatorUsername, StringComparison.OrdinalIgnoreCase)))
        {
            await _notifications.CreateAsync(
                $"Bạn được thêm vào nhóm chat",
                $"@{creatorUsername} đã thêm bạn vào kênh \"{req.Name.Trim()}\".",
                req.LinkedAppCode,
                null,
                m,
                "CHAT",
                $"/chat?channel={chId}",
                creatorUsername);
        }

        _realtime.Publish("chat_channel_created", null, null, chId, "*", new { channelId = chId });
        return (await ListChannelsAsync(creatorUsername)).First(c => c.Id == chId);
    }

    public async Task<ChatChannelDto> EnsureDirectChannelAsync(string username, string targetUsername)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var chId = await TaskService.EnsureDirectChannelInternalAsync(conn, null, username, targetUsername);
        return (await ListChannelsAsync(username)).First(c => c.Id == chId);
    }

    public async Task<bool> AddMemberAsync(string channelId, string targetUsername, string actorUsername)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO chat_channel_members (channel_id, username, role)
            VALUES (@ch, @u, 'MEMBER')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("ch", channelId);
        cmd.Parameters.AddWithValue("u", targetUsername.Trim());
        var added = await cmd.ExecuteNonQueryAsync() > 0;

        if (added)
        {
            await using var msgCmd = new NpgsqlCommand("""
                INSERT INTO chat_messages (id, channel_id, sender_username, message_type, content)
                VALUES (@id, @ch, @snd, 'SYSTEM', @cnt)
                """, conn);
            msgCmd.Parameters.AddWithValue("id", $"msg_{Guid.NewGuid().ToString("N")[..10]}");
            msgCmd.Parameters.AddWithValue("ch", channelId);
            msgCmd.Parameters.AddWithValue("snd", actorUsername);
            msgCmd.Parameters.AddWithValue("cnt", $"@{actorUsername} đã thêm @{targetUsername.Trim()} vào nhóm.");
            await msgCmd.ExecuteNonQueryAsync();

            await _notifications.CreateAsync(
                "Bạn được thêm vào nhóm chat",
                $"@{actorUsername} đã thêm bạn vào một cuộc trò chuyện nhóm.",
                null,
                null,
                targetUsername.Trim(),
                "CHAT",
                $"/chat?channel={channelId}",
                actorUsername);

            _realtime.Publish("chat_message", null, null, null, "*", new { channelId });
        }
        return true;
    }

    public async Task<List<ChatMessageDto>> ListMessagesAsync(string channelId, string username, int limit = 80)
        => (await ListMessagePageAsync(channelId, username, limit)).Items;

    public async Task<ChatHistoryPage> ListMessagePageAsync(string channelId, string username, int limit = 80, string? before = null)
    {
        var cursor = string.IsNullOrWhiteSpace(before) ? null : ChatHistoryCursor.Decode(before, channelId);
        var pageSize = Math.Clamp(limit, 1, 200);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        if (!await CanReadChannelAsync(conn, channelId, username))
            throw new UnauthorizedAccessException("Không có quyền đọc hội thoại này.");

        // Loading older history must not mark newly arrived messages as read.
        if (cursor == null) await MarkReadInternalAsync(conn, channelId, username);

        var sql = """
            SELECT * FROM (
                SELECT m.id, m.channel_id, m.sender_username, COALESCE(u.full_name, m.sender_username),
                       COALESCE(u.role_code, 'STAFF'), m.message_type, m.content,
                       m.reply_to_id, rep.content AS rep_content, rep.sender_username AS rep_sender,
                       m.task_id, m.attachments::text, m.mentions::text, m.reactions::text,
                       m.is_pinned, m.is_edited, m.created_at
                FROM chat_messages m
                LEFT JOIN sys_users u ON LOWER(u.username) = LOWER(m.sender_username)
                LEFT JOIN chat_messages rep ON rep.id = m.reply_to_id
                WHERE m.channel_id = @ch AND m.deleted_at IS NULL
                  AND (@beforeAt IS NULL OR (m.created_at, m.id) < (@beforeAt, @beforeId))
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT @lim
            ) sub
            ORDER BY sub.created_at ASC, sub.id ASC
            """;

        var list = new List<ChatMessageDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("ch", channelId);
            cmd.Parameters.AddWithValue("lim", pageSize + 1);
            cmd.Parameters.Add(new NpgsqlParameter("beforeAt", NpgsqlDbType.TimestampTz) { Value = (object?)cursor?.CreatedAt ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("beforeId", NpgsqlDbType.Varchar) { Value = (object?)cursor?.Id ?? DBNull.Value });
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadMessage(r));
        }

        var hasMore = list.Count > pageSize;
        if (hasMore) list.RemoveAt(0); // extra oldest row; the next request will fetch it
        var nextCursor = hasMore ? ChatHistoryCursor.Encode(channelId, list[0]) : null;
        var taskIds = list.Where(m => !string.IsNullOrWhiteSpace(m.TaskId)).Select(m => m.TaskId!).ToList();
        if (taskIds.Count > 0)
        {
            var taskMap = await _tasks.GetTasksByIdsAsync(taskIds);
            foreach (var msg in list)
            {
                if (!string.IsNullOrWhiteSpace(msg.TaskId) && taskMap.TryGetValue(msg.TaskId, out var t))
                    msg.Task = t;
            }
        }

        return new ChatHistoryPage(list, hasMore, nextCursor);
    }

    private static async Task<bool> CanReadChannelAsync(NpgsqlConnection conn, string channelId, string username)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT 1 FROM chat_channels c
            WHERE c.id = @ch AND c.is_archived = FALSE
              AND (c.channel_type = 'PUBLIC' OR EXISTS (
                  SELECT 1 FROM chat_channel_members m
                  WHERE m.channel_id = c.id AND LOWER(m.username) = LOWER(@u)))
            """, conn);
        cmd.Parameters.AddWithValue("ch", channelId);
        cmd.Parameters.AddWithValue("u", username);
        return await cmd.ExecuteScalarAsync() != null;
    }

    public async Task<(ChatMessageDto Message, AppTaskDto? CreatedTask)> SendMessageAsync(
        string channelId,
        string senderUsername,
        SendChatMessageRequest req)
    {
        var rawContent = req.Content?.Trim() ?? string.Empty;

        // Integrated Slash Command: /task @assignee Title  or  /giaoviec @assignee Title
        var slash = TaskService.TryParseSlashTaskCommand(rawContent, senderUsername);
        if (slash.IsSlashTask)
        {
            var (task, msgId) = await _tasks.CreateTaskAsync(new CreateTaskRequest
            {
                Title = slash.Title,
                AssigneeUsername = slash.Assignee,
                Priority = "MEDIUM",
                ChannelId = channelId,
                PostToChat = true
            }, senderUsername);

            var msgs = await ListMessagesAsync(channelId, senderUsername, 30);
            var cardMsg = msgs.LastOrDefault(m => m.Id == msgId) ?? msgs.Last();
            return (cardMsg, task);
        }

        var msgIdNew = $"msg_{Guid.NewGuid().ToString("N")[..10]}";
        var mentions = Regex.Matches(rawContent, @"@([a-zA-Z0-9_\.]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var attachments = req.Attachments ?? new List<ChatAttachmentDto>();
        var msgType = string.IsNullOrWhiteSpace(req.MessageType) ? "TEXT" : req.MessageType.Trim().ToUpperInvariant();
        if (attachments.Count > 0 && msgType == "TEXT" && string.IsNullOrWhiteSpace(rawContent))
        {
            rawContent = $"Đã gửi {attachments.Count} tệp đính kèm";
            msgType = "FILE";
        }

        var preview = rawContent.Length > 90 ? rawContent[..90] + "..." : rawContent;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Ensure sender is member & get channel info
        string channelName = "Chat";
        string channelType = "GROUP";
        await using (var chCmd = new NpgsqlCommand("SELECT name, channel_type FROM chat_channels WHERE id = @ch LIMIT 1", conn))
        {
            chCmd.Parameters.AddWithValue("ch", channelId);
            await using var r = await chCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                channelName = r.GetString(0);
                channelType = r.GetString(1);
            }
        }

        await using (var insCmd = new NpgsqlCommand("""
            INSERT INTO chat_channel_members (channel_id, username, role, last_read_at)
            VALUES (@ch, @snd, 'MEMBER', CURRENT_TIMESTAMP)
            ON CONFLICT (channel_id, username) DO UPDATE SET last_read_at = CURRENT_TIMESTAMP;

            INSERT INTO chat_messages (id, channel_id, sender_username, message_type, content, reply_to_id, attachments, mentions)
            VALUES (@id, @ch, @snd, @mt, @cnt, @rep, @att::jsonb, @mnt::jsonb);

            UPDATE chat_channels
            SET last_message_at = CURRENT_TIMESTAMP,
                last_message_preview = @prev
            WHERE id = @ch;
            """, conn))
        {
            insCmd.Parameters.AddWithValue("id", msgIdNew);
            insCmd.Parameters.AddWithValue("ch", channelId);
            insCmd.Parameters.AddWithValue("snd", senderUsername);
            insCmd.Parameters.AddWithValue("mt", msgType);
            insCmd.Parameters.AddWithValue("cnt", rawContent);
            insCmd.Parameters.AddWithValue("rep", string.IsNullOrWhiteSpace(req.ReplyToId) ? DBNull.Value : req.ReplyToId);
            insCmd.Parameters.AddWithValue("att", JsonSerializer.Serialize(attachments, JsonOpts));
            insCmd.Parameters.AddWithValue("mnt", JsonSerializer.Serialize(mentions, JsonOpts));
            insCmd.Parameters.AddWithValue("prev", $"{senderUsername}: {preview}");
            await insCmd.ExecuteNonQueryAsync();
        }

        // Notify mentioned users
        foreach (var target in mentions.Where(m => !m.Equals(senderUsername, StringComparison.OrdinalIgnoreCase)))
        {
            await _notifications.CreateAsync(
                $"@{senderUsername} nhắc tới bạn trong {channelName}",
                preview,
                null,
                null,
                target,
                "MENTION",
                $"/chat?channel={channelId}",
                senderUsername);
        }

        // Notify DM peer if not already mentioned
        if (channelType == "DIRECT")
        {
            await using var peerCmd = new NpgsqlCommand("""
                SELECT username FROM chat_channel_members
                WHERE channel_id = @ch AND LOWER(username) <> LOWER(@snd)
                """, conn);
            peerCmd.Parameters.AddWithValue("ch", channelId);
            peerCmd.Parameters.AddWithValue("snd", senderUsername);
            await using var pr = await peerCmd.ExecuteReaderAsync();
            var peers = new List<string>();
            while (await pr.ReadAsync()) peers.Add(pr.GetString(0));
            await pr.CloseAsync();

            foreach (var p in peers.Where(p => !mentions.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                await _notifications.CreateAsync(
                    $"Tin nhắn mới từ @{senderUsername}",
                    preview,
                    null,
                    null,
                    p,
                    "CHAT",
                    $"/chat?channel={channelId}",
                    senderUsername);
            }
        }

        _realtime.Publish("chat_message", null, null, msgIdNew, "*", new
        {
            channelId,
            messageId = msgIdNew,
            sender = senderUsername,
            preview
        });

        var msg = (await ListMessagesAsync(channelId, senderUsername, 30)).Last(m => m.Id == msgIdNew);
        return (msg, null);
    }

    public async Task<(AppTaskDto Task, ChatMessageDto Message)?> ConvertMessageToTaskAsync(
        string messageId,
        string actorUsername,
        ConvertMessageToTaskRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT channel_id, sender_username, content FROM chat_messages WHERE id = @id AND deleted_at IS NULL LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", messageId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var channelId = r.GetString(0);
        var origSender = r.GetString(1);
        var origContent = r.GetString(2);
        await r.CloseAsync();

        var title = string.IsNullOrWhiteSpace(req.Title)
            ? (origContent.Length > 120 ? origContent[..120] : origContent)
            : req.Title.Trim();
        var desc = string.IsNullOrWhiteSpace(req.Description)
            ? $"Tạo từ tin nhắn của @{origSender}: \"{origContent}\""
            : req.Description.Trim();
        var assignee = string.IsNullOrWhiteSpace(req.AssigneeUsername) ? origSender : req.AssigneeUsername.Trim();

        var (task, cardMsgId) = await _tasks.CreateTaskAsync(new CreateTaskRequest
        {
            Title = title,
            Description = desc,
            AssigneeUsername = assignee,
            Priority = req.Priority,
            DueDate = req.DueDate,
            ChannelId = channelId,
            PostToChat = true
        }, actorUsername);

        var msgs = await ListMessagesAsync(channelId, actorUsername, 30);
        var cardMsg = msgs.LastOrDefault(m => m.Id == cardMsgId) ?? msgs.Last();
        return (task, cardMsg);
    }

    public async Task<Dictionary<string, List<string>>?> ToggleReactionAsync(string messageId, string username, string emoji)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? channelId = null;
        string reactionsRaw = "{}";
        await using (var cmd = new NpgsqlCommand(
            "SELECT channel_id, reactions::text FROM chat_messages WHERE id = @id AND deleted_at IS NULL LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("id", messageId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            channelId = r.GetString(0);
            reactionsRaw = r.GetString(1);
        }

        var reactions = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(reactionsRaw, JsonOpts) ?? new();
        if (!reactions.TryGetValue(emoji, out var users))
        {
            users = new List<string>();
            reactions[emoji] = users;
        }

        var existingIdx = users.FindIndex(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0) users.RemoveAt(existingIdx);
        else users.Add(username);

        if (users.Count == 0) reactions.Remove(emoji);

        await using (var upCmd = new NpgsqlCommand("UPDATE chat_messages SET reactions = @r::jsonb WHERE id = @id", conn))
        {
            upCmd.Parameters.AddWithValue("id", messageId);
            upCmd.Parameters.AddWithValue("r", JsonSerializer.Serialize(reactions, JsonOpts));
            await upCmd.ExecuteNonQueryAsync();
        }

        _realtime.Publish("chat_reaction", null, null, messageId, "*", new { channelId, messageId, reactions });
        return reactions;
    }

    public async Task<bool?> TogglePinAsync(string messageId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE chat_messages SET is_pinned = NOT is_pinned
            WHERE id = @id AND deleted_at IS NULL
            RETURNING is_pinned, channel_id
            """, conn);
        cmd.Parameters.AddWithValue("id", messageId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var pinned = r.GetBoolean(0);
        var chId = r.GetString(1);
        _realtime.Publish("chat_message", null, null, messageId, "*", new { channelId = chId });
        return pinned;
    }

    public async Task<bool> DeleteMessageAsync(string messageId, string username, bool isAdmin)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = isAdmin
            ? "UPDATE chat_messages SET deleted_at = CURRENT_TIMESTAMP WHERE id = @id AND deleted_at IS NULL RETURNING channel_id"
            : "UPDATE chat_messages SET deleted_at = CURRENT_TIMESTAMP WHERE id = @id AND LOWER(sender_username) = LOWER(@u) AND deleted_at IS NULL RETURNING channel_id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", messageId);
        if (!isAdmin) cmd.Parameters.AddWithValue("u", username);
        var chId = await cmd.ExecuteScalarAsync() as string;
        if (chId != null)
        {
            _realtime.Publish("chat_message", null, null, messageId, "*", new { channelId = chId, deleted = true });
            return true;
        }
        return false;
    }

    public async Task MarkChannelReadAsync(string channelId, string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        if (!await CanReadChannelAsync(conn, channelId, username))
            throw new UnauthorizedAccessException("Không có quyền đọc hội thoại này.");
        await MarkReadInternalAsync(conn, channelId, username);
    }

    public async Task<(int UnreadChatCount, int PendingTaskCount, int UnreadNotifCount)> GetUnreadSummaryAsync(string username)
    {
        var channels = await ListChannelsAsync(username);
        var unreadChat = channels.Sum(c => c.UnreadCount);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using var taskCmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM app_tasks
            WHERE LOWER(assignee_username) = LOWER(@u) AND status IN ('TODO', 'IN_PROGRESS', 'REVIEW')
            """, conn);
        taskCmd.Parameters.AddWithValue("u", username);
        var pendingTasks = Convert.ToInt32(await taskCmd.ExecuteScalarAsync());

        await using var notiCmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM sys_notifications
            WHERE (recipient_username = '*' OR LOWER(recipient_username) = LOWER(@u)) AND is_read = FALSE
            """, conn);
        notiCmd.Parameters.AddWithValue("u", username);
        var unreadNotifs = Convert.ToInt32(await notiCmd.ExecuteScalarAsync());

        return (unreadChat, pendingTasks, unreadNotifs);
    }

    private static async Task MarkReadInternalAsync(NpgsqlConnection conn, string channelId, string username)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO chat_channel_members (channel_id, username, role, last_read_at)
            VALUES (@ch, @u, 'MEMBER', CURRENT_TIMESTAMP)
            ON CONFLICT (channel_id, username) DO UPDATE SET last_read_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("ch", channelId);
        cmd.Parameters.AddWithValue("u", username);
        await cmd.ExecuteNonQueryAsync();
    }

    private static ChatMessageDto ReadMessage(NpgsqlDataReader r)
    {
        var attRaw = r.IsDBNull(11) ? "[]" : r.GetString(11);
        var mntRaw = r.IsDBNull(12) ? "[]" : r.GetString(12);
        var rctRaw = r.IsDBNull(13) ? "{}" : r.GetString(13);

        return new ChatMessageDto
        {
            Id = r.GetString(0),
            ChannelId = r.GetString(1),
            SenderUsername = r.GetString(2),
            SenderFullName = r.GetString(3),
            SenderRoleCode = r.GetString(4),
            MessageType = r.GetString(5),
            Content = r.GetString(6),
            ReplyToId = r.IsDBNull(7) ? null : r.GetString(7),
            ReplyToPreview = r.IsDBNull(8) ? null : r.GetString(8),
            ReplyToSender = r.IsDBNull(9) ? null : r.GetString(9),
            TaskId = r.IsDBNull(10) ? null : r.GetString(10),
            Attachments = JsonSerializer.Deserialize<List<ChatAttachmentDto>>(attRaw, JsonOpts) ?? new(),
            Mentions = JsonSerializer.Deserialize<List<string>>(mntRaw, JsonOpts) ?? new(),
            Reactions = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(rctRaw, JsonOpts) ?? new(),
            IsPinned = r.GetBoolean(14),
            IsEdited = r.GetBoolean(15),
            CreatedAt = r.GetDateTime(16)
        };
    }
}
