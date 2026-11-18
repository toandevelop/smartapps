using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class TaskService
{
    private readonly string _connStr;
    private readonly NotificationService _notifications;
    private readonly RealtimeEventService _realtime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TaskService(IConfiguration config, NotificationService notifications, RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _notifications = notifications;
        _realtime = realtime;
    }

    public static (bool IsSlashTask, string Assignee, string Title) TryParseSlashTaskCommand(string content, string fallbackAssignee)
    {
        var trimmed = content.Trim();
        string? body = null;
        if (trimmed.StartsWith("/task ", StringComparison.OrdinalIgnoreCase))
            body = trimmed[6..].Trim();
        else if (trimmed.StartsWith("/giaoviec ", StringComparison.OrdinalIgnoreCase))
            body = trimmed[10..].Trim();

        if (string.IsNullOrWhiteSpace(body))
            return (false, fallbackAssignee, string.Empty);

        var assignee = fallbackAssignee;
        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var titleTokens = new List<string>();
        bool pickedAssignee = false;
        foreach (var tok in tokens)
        {
            if (!pickedAssignee && tok.StartsWith('@') && tok.Length > 1)
            {
                assignee = tok[1..].Trim().TrimEnd(',', '.', ':', ';');
                pickedAssignee = true;
            }
            else
            {
                titleTokens.Add(tok);
            }
        }

        var title = string.Join(' ', titleTokens).Trim();
        if (string.IsNullOrWhiteSpace(title)) title = body;
        return (true, assignee, title);
    }

    public async Task<List<AppTaskDto>> ListTasksAsync(
        string currentUsername,
        string? scope = "all",
        string? status = null,
        string? priority = null,
        string? channelId = null,
        string? search = null,
        bool ownerOnly = false)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var scopeNorm = (scope ?? "all").Trim().ToLowerInvariant();
        var statusNorm = string.IsNullOrWhiteSpace(status) || status.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? null : status.Trim().ToUpperInvariant();
        var priorityNorm = string.IsNullOrWhiteSpace(priority) || priority.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? null : priority.Trim().ToUpperInvariant();

        var sql = """
            SELECT t.id, t.task_code, t.title, t.description, t.status, t.priority,
                   t.assignee_username, ua.full_name,
                   t.assigner_username, ub.full_name,
                   t.watchers::text, t.due_date, t.progress, t.checklist::text,
                   t.channel_id, ch.name, t.message_id, t.linked_app_code, t.linked_record_id,
                   t.completed_at, t.created_at, t.updated_at
            FROM app_tasks t
            LEFT JOIN sys_users ua ON LOWER(ua.username) = LOWER(t.assignee_username)
            LEFT JOIN sys_users ub ON LOWER(ub.username) = LOWER(t.assigner_username)
            LEFT JOIN chat_channels ch ON ch.id = t.channel_id
            WHERE (@ownerOnly = FALSE
                   OR LOWER(t.assignee_username) = LOWER(@me)
                   OR LOWER(t.assigner_username) = LOWER(@me)
                   OR EXISTS (
                       SELECT 1 FROM jsonb_array_elements_text(COALESCE(t.watchers, '[]'::jsonb)) w
                       WHERE LOWER(w) = LOWER(@me)
                   ))
              AND (@scope <> 'assigned_to_me' OR LOWER(t.assignee_username) = LOWER(@me))
              AND (@scope <> 'assigned_by_me' OR LOWER(t.assigner_username) = LOWER(@me))
              AND (@st IS NULL OR UPPER(t.status) = @st)
              AND (@pri IS NULL OR UPPER(t.priority) = @pri)
              AND (@chId IS NULL OR t.channel_id = @chId)
              AND (@q IS NULL OR LOWER(t.title) LIKE '%' || LOWER(@q) || '%'
                   OR LOWER(t.task_code) LIKE '%' || LOWER(@q) || '%'
                   OR LOWER(COALESCE(t.description, '')) LIKE '%' || LOWER(@q) || '%')
            ORDER BY
                CASE WHEN t.status = 'DONE' OR t.status = 'CANCELLED' THEN 1 ELSE 0 END ASC,
                CASE t.priority WHEN 'URGENT' THEN 0 WHEN 'HIGH' THEN 1 WHEN 'MEDIUM' THEN 2 ELSE 3 END ASC,
                t.created_at DESC
            LIMIT 200
            """;

        var list = new List<AppTaskDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ownerOnly", ownerOnly);
        cmd.Parameters.AddWithValue("scope", scopeNorm);
        cmd.Parameters.AddWithValue("me", currentUsername);
        cmd.Parameters.Add(new NpgsqlParameter("st", NpgsqlDbType.Varchar) { Value = (object?)statusNorm ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("pri", NpgsqlDbType.Varchar) { Value = (object?)priorityNorm ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("chId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(channelId) ? DBNull.Value : channelId });
        cmd.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim() });

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadTask(r));
        return list;
    }

    public async Task<AppTaskDto?> GetTaskByIdAsync(string id)
    {
        var dict = await GetTasksByIdsAsync(new[] { id });
        return dict.TryGetValue(id, out var t) ? t : null;
    }

    public async Task<Dictionary<string, AppTaskDto>> GetTasksByIdsAsync(IEnumerable<string> ids)
    {
        var idList = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var map = new Dictionary<string, AppTaskDto>();
        if (idList.Length == 0) return map;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT t.id, t.task_code, t.title, t.description, t.status, t.priority,
                   t.assignee_username, ua.full_name,
                   t.assigner_username, ub.full_name,
                   t.watchers::text, t.due_date, t.progress, t.checklist::text,
                   t.channel_id, ch.name, t.message_id, t.linked_app_code, t.linked_record_id,
                   t.completed_at, t.created_at, t.updated_at
            FROM app_tasks t
            LEFT JOIN sys_users ua ON LOWER(ua.username) = LOWER(t.assignee_username)
            LEFT JOIN sys_users ub ON LOWER(ub.username) = LOWER(t.assigner_username)
            LEFT JOIN chat_channels ch ON ch.id = t.channel_id
            WHERE t.id = ANY(@ids)
            """, conn);
        cmd.Parameters.AddWithValue("ids", idList);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var t = ReadTask(r);
            map[t.Id] = t;
        }
        return map;
    }

    public async Task<(AppTaskDto Task, string? ChatMessageId)> CreateTaskAsync(CreateTaskRequest req, string assignerUsername)
    {
        var taskId = $"tsk_{Guid.NewGuid().ToString("N")[..10]}";
        var priority = NormalizePriority(req.Priority);
        var assignee = string.IsNullOrWhiteSpace(req.AssigneeUsername) ? assignerUsername : req.AssigneeUsername.Trim();
        var checklist = (req.Checklist ?? new List<ChecklistItemDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => new ChecklistItemDto
            {
                Id = string.IsNullOrWhiteSpace(c.Id) ? $"chk_{Guid.NewGuid().ToString("N")[..6]}" : c.Id,
                Text = c.Text.Trim(),
                Done = c.Done
            }).ToList();

        DateTime? dueDate = null;
        if (!string.IsNullOrWhiteSpace(req.DueDate) && DateTime.TryParse(req.DueDate, out var parsedDue))
            dueDate = parsedDue.Date;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Generate sequential TASK-XXX code
        await using var seqCmd = new NpgsqlCommand("SELECT COUNT(*) + 1 FROM app_tasks", conn, tx);
        var seq = Convert.ToInt32(await seqCmd.ExecuteScalarAsync());
        var taskCode = $"TASK-{seq:D3}";
        // Ensure uniqueness even after deletions
        await using (var chkCode = new NpgsqlCommand("SELECT 1 FROM app_tasks WHERE task_code = @c LIMIT 1", conn, tx))
        {
            chkCode.Parameters.AddWithValue("c", taskCode);
            if (await chkCode.ExecuteScalarAsync() != null)
                taskCode = $"TASK-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000:D5}";
        }

        var targetChannelId = string.IsNullOrWhiteSpace(req.ChannelId) ? null : req.ChannelId.Trim();
        if (targetChannelId == null && req.PostToChat && !assignee.Equals(assignerUsername, StringComparison.OrdinalIgnoreCase))
        {
            targetChannelId = await EnsureDirectChannelInternalAsync(conn, tx, assignerUsername, assignee);
        }

        string? msgId = null;
        if (targetChannelId != null && req.PostToChat)
        {
            msgId = $"msg_{Guid.NewGuid().ToString("N")[..10]}";
        }

        var progress = checklist.Count > 0
            ? (int)Math.Round(checklist.Count(c => c.Done) * 100.0 / checklist.Count)
            : 0;

        await using (var insCmd = new NpgsqlCommand("""
            INSERT INTO app_tasks (
                id, task_code, title, description, status, priority,
                assignee_username, assigner_username, watchers, due_date, progress,
                checklist, channel_id, message_id, linked_app_code, linked_record_id
            )
            VALUES (
                @id, @code, @title, @desc, 'TODO', @pri,
                @assignee, @assigner, @watchers::jsonb, @due, @prog,
                @chk::jsonb, @chId, @msgId, @appCode, @recId
            )
            """, conn, tx))
        {
            insCmd.Parameters.AddWithValue("id", taskId);
            insCmd.Parameters.AddWithValue("code", taskCode);
            insCmd.Parameters.AddWithValue("title", req.Title.Trim());
            insCmd.Parameters.AddWithValue("desc", string.IsNullOrWhiteSpace(req.Description) ? DBNull.Value : req.Description.Trim());
            insCmd.Parameters.AddWithValue("pri", priority);
            insCmd.Parameters.AddWithValue("assignee", assignee);
            insCmd.Parameters.AddWithValue("assigner", assignerUsername);
            insCmd.Parameters.AddWithValue("watchers", JsonSerializer.Serialize(req.Watchers ?? new List<string>(), JsonOpts));
            insCmd.Parameters.Add(new NpgsqlParameter("due", NpgsqlDbType.Date) { Value = dueDate.HasValue ? dueDate.Value : DBNull.Value });
            insCmd.Parameters.AddWithValue("prog", progress);
            insCmd.Parameters.AddWithValue("chk", JsonSerializer.Serialize(checklist, JsonOpts));
            insCmd.Parameters.AddWithValue("chId", (object?)targetChannelId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("msgId", (object?)msgId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("appCode", string.IsNullOrWhiteSpace(req.LinkedAppCode) ? DBNull.Value : req.LinkedAppCode.Trim().ToUpperInvariant());
            insCmd.Parameters.AddWithValue("recId", string.IsNullOrWhiteSpace(req.LinkedRecordId) ? DBNull.Value : req.LinkedRecordId.Trim());
            await insCmd.ExecuteNonQueryAsync();
        }

        if (msgId != null && targetChannelId != null)
        {
            var cardContent = $"Giao việc cho @{assignee}: {req.Title.Trim()}";
            var mentionsJson = JsonSerializer.Serialize(new[] { assignee });
            await using var msgCmd = new NpgsqlCommand("""
                INSERT INTO chat_messages (id, channel_id, sender_username, message_type, content, task_id, mentions)
                VALUES (@id, @ch, @snd, 'TASK_CARD', @cnt, @tid, @mnt::jsonb);

                UPDATE chat_channels
                SET last_message_at = CURRENT_TIMESTAMP,
                    last_message_preview = @preview
                WHERE id = @ch;
                """, conn, tx);
            msgCmd.Parameters.AddWithValue("id", msgId);
            msgCmd.Parameters.AddWithValue("ch", targetChannelId);
            msgCmd.Parameters.AddWithValue("snd", assignerUsername);
            msgCmd.Parameters.AddWithValue("cnt", cardContent);
            msgCmd.Parameters.AddWithValue("tid", taskId);
            msgCmd.Parameters.AddWithValue("mnt", mentionsJson);
            msgCmd.Parameters.AddWithValue("preview", $"📌 [{taskCode}] {req.Title.Trim()}");
            await msgCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        var created = (await GetTaskByIdAsync(taskId))!;
        var linkUrl = targetChannelId != null ? $"/chat?channel={targetChannelId}" : "/apps/TASKS";

        if (!assignee.Equals(assignerUsername, StringComparison.OrdinalIgnoreCase))
        {
            await _notifications.CreateAsync(
                $"Công việc mới: {taskCode}",
                $"@{assignerUsername} đã giao cho bạn: \"{created.Title}\"",
                created.LinkedAppCode,
                created.LinkedRecordId,
                assignee,
                "TASK",
                linkUrl,
                assignerUsername,
                priority is "URGENT" or "HIGH" ? "HIGH" : "NORMAL");
        }

        if (targetChannelId != null)
        {
            _realtime.Publish("chat_message", null, null, msgId, "*", new
            {
                channelId = targetChannelId,
                messageId = msgId,
                taskId,
                sender = assignerUsername
            });
        }

        await using (var auditConn = new NpgsqlConnection(_connStr))
        {
            await auditConn.OpenAsync();
            var tasksAppId = await ResolveTasksAppIdAsync(auditConn);
            await RecordService.WriteAuditAsync(
                auditConn,
                tasksAppId,
                "tbl_tasks_congviec",
                created.Id,
                "CREATE",
                assignerUsername,
                null,
                TaskToAuditDict(created),
                $"Giao việc {created.TaskCode}: {created.Title}");
        }

        _realtime.Publish("task_updated", created.LinkedAppCode, null, taskId, "*", created);

        return (created, msgId);
    }

    public async Task<AppTaskDto?> UpdateTaskAsync(string taskId, UpdateTaskRequest req, string actorUsername)
    {
        var existing = await GetTaskByIdAsync(taskId);
        if (existing == null) return null;

        var nextTitle = string.IsNullOrWhiteSpace(req.Title) ? existing.Title : req.Title.Trim();
        var nextDesc = req.Description != null ? req.Description.Trim() : existing.Description;
        var nextStatus = string.IsNullOrWhiteSpace(req.Status) ? existing.Status : NormalizeStatus(req.Status);
        var nextPriority = string.IsNullOrWhiteSpace(req.Priority) ? existing.Priority : NormalizePriority(req.Priority);
        var nextAssignee = string.IsNullOrWhiteSpace(req.AssigneeUsername) ? existing.AssigneeUsername : req.AssigneeUsername.Trim();
        var nextChecklist = req.Checklist ?? existing.Checklist;

        int nextProgress = req.Progress ?? existing.Progress;
        if (req.Checklist != null && req.Checklist.Count > 0 && req.Progress == null)
        {
            nextProgress = (int)Math.Round(nextChecklist.Count(c => c.Done) * 100.0 / nextChecklist.Count);
        }
        if (nextStatus == "DONE") nextProgress = 100;
        else if (nextStatus == "TODO" && req.Status == "TODO" && req.Progress == null && req.Checklist == null) nextProgress = 0;

        DateTime? nextDue = null;
        if (req.DueDate != null)
        {
            if (DateTime.TryParse(req.DueDate, out var d)) nextDue = d.Date;
        }
        else if (!string.IsNullOrWhiteSpace(existing.DueDate) && DateTime.TryParse(existing.DueDate, out var d0))
        {
            nextDue = d0.Date;
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE app_tasks
            SET title = @t,
                description = @d,
                status = @st,
                priority = @pri,
                assignee_username = @asg,
                due_date = @due,
                progress = @prog,
                checklist = @chk::jsonb,
                completed_at = CASE WHEN @st = 'DONE' THEN COALESCE(completed_at, CURRENT_TIMESTAMP) ELSE NULL END,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", taskId);
        cmd.Parameters.AddWithValue("t", nextTitle);
        cmd.Parameters.AddWithValue("d", (object?)nextDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("st", nextStatus);
        cmd.Parameters.AddWithValue("pri", nextPriority);
        cmd.Parameters.AddWithValue("asg", nextAssignee);
        cmd.Parameters.Add(new NpgsqlParameter("due", NpgsqlDbType.Date) { Value = nextDue.HasValue ? nextDue.Value : DBNull.Value });
        cmd.Parameters.AddWithValue("prog", Math.Clamp(nextProgress, 0, 100));
        cmd.Parameters.AddWithValue("chk", JsonSerializer.Serialize(nextChecklist, JsonOpts));
        await cmd.ExecuteNonQueryAsync();

        var updated = (await GetTaskByIdAsync(taskId))!;
        var linkUrl = updated.ChannelId != null ? $"/chat?channel={updated.ChannelId}" : "/apps/TASKS";

        // Notify relevant counterpart on status changes or reassignment
        if (nextStatus != existing.Status)
        {
            var notifyTarget = actorUsername.Equals(updated.AssignerUsername, StringComparison.OrdinalIgnoreCase)
                ? updated.AssigneeUsername
                : updated.AssignerUsername;
            if (!string.IsNullOrWhiteSpace(notifyTarget) && !notifyTarget.Equals(actorUsername, StringComparison.OrdinalIgnoreCase))
            {
                var statusLabel = nextStatus switch
                {
                    "IN_PROGRESS" => "Đang thực hiện",
                    "REVIEW" => "Chờ kiểm tra/duyệt",
                    "DONE" => "Hoàn thành",
                    "CANCELLED" => "Đã hủy",
                    _ => "Cần làm"
                };
                await _notifications.CreateAsync(
                    $"Cập nhật {updated.TaskCode}: {statusLabel}",
                    $"@{actorUsername} đã chuyển \"{updated.Title}\" sang trạng thái {statusLabel}.",
                    updated.LinkedAppCode,
                    updated.LinkedRecordId,
                    notifyTarget,
                    "TASK",
                    linkUrl,
                    actorUsername);
            }
        }
        else if (!nextAssignee.Equals(existing.AssigneeUsername, StringComparison.OrdinalIgnoreCase) &&
                 !nextAssignee.Equals(actorUsername, StringComparison.OrdinalIgnoreCase))
        {
            await _notifications.CreateAsync(
                $"Chuyển giao {updated.TaskCode}",
                $"@{actorUsername} đã giao lại công việc \"{updated.Title}\" cho bạn.",
                updated.LinkedAppCode,
                updated.LinkedRecordId,
                nextAssignee,
                "TASK",
                linkUrl,
                actorUsername,
                "HIGH");
        }

        var tasksAppId = await ResolveTasksAppIdAsync(conn);
        await RecordService.WriteAuditAsync(
            conn,
            tasksAppId,
            "tbl_tasks_congviec",
            updated.Id,
            "UPDATE",
            actorUsername,
            TaskToAuditDict(existing),
            TaskToAuditDict(updated),
            $"Cập nhật {updated.TaskCode}: {updated.Title}");

        _realtime.Publish("task_updated", updated.LinkedAppCode, null, taskId, "*", updated);
        return updated;
    }

    public async Task<bool> DeleteTaskAsync(string id, string actorUsername = "system")
    {
        var existing = await GetTaskByIdAsync(id);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM app_tasks WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        var ok = await cmd.ExecuteNonQueryAsync() > 0;
        if (ok)
        {
            if (existing != null)
            {
                var tasksAppId = await ResolveTasksAppIdAsync(conn);
                await RecordService.WriteAuditAsync(
                    conn,
                    tasksAppId,
                    "tbl_tasks_congviec",
                    id,
                    "DELETE",
                    actorUsername,
                    TaskToAuditDict(existing),
                    null,
                    $"Xóa công việc {existing.TaskCode}: {existing.Title}");
            }
            _realtime.Publish("task_updated", null, null, id, "*", new { id, deleted = true });
        }
        return ok;
    }

    private static async Task<string> ResolveTasksAppIdAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT id FROM sys_apps WHERE UPPER(code) = 'TASKS' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return val?.ToString() ?? "app_tasks";
    }

    private static Dictionary<string, object?> TaskToAuditDict(AppTaskDto t) => new()
    {
        ["taskCode"] = t.TaskCode,
        ["title"] = t.Title,
        ["status"] = t.Status,
        ["priority"] = t.Priority,
        ["assigneeUsername"] = t.AssigneeUsername,
        ["dueDate"] = t.DueDate,
        ["progress"] = t.Progress
    };

    public static async Task<string> EnsureDirectChannelInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string userA,
        string userB)
    {
        // Find existing DIRECT channel containing both users
        await using (var findCmd = new NpgsqlCommand("""
            SELECT c.id
            FROM chat_channels c
            JOIN chat_channel_members m1 ON m1.channel_id = c.id AND LOWER(m1.username) = LOWER(@ua)
            JOIN chat_channel_members m2 ON m2.channel_id = c.id AND LOWER(m2.username) = LOWER(@ub)
            WHERE c.channel_type = 'DIRECT'
            LIMIT 1
            """, conn, tx))
        {
            findCmd.Parameters.AddWithValue("ua", userA);
            findCmd.Parameters.AddWithValue("ub", userB);
            var existing = await findCmd.ExecuteScalarAsync() as string;
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
        }

        var chId = $"dm_{Guid.NewGuid().ToString("N")[..10]}";
        var sorted = new[] { userA.Trim(), userB.Trim() }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var name = $"{sorted[0]} & {sorted[1]}";

        await using (var insCh = new NpgsqlCommand("""
            INSERT INTO chat_channels (id, name, description, channel_type, avatar_color, created_by, last_message_preview)
            VALUES (@id, @name, 'Tin nhắn trực tiếp 1-1', 'DIRECT', '#0284c7', @cb, 'Bắt đầu cuộc trò chuyện')
            """, conn, tx))
        {
            insCh.Parameters.AddWithValue("id", chId);
            insCh.Parameters.AddWithValue("name", name);
            insCh.Parameters.AddWithValue("cb", userA);
            await insCh.ExecuteNonQueryAsync();
        }

        var members = new[] { userA.Trim(), userB.Trim() }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var m in members)
        {
            await using var insMem = new NpgsqlCommand("""
                INSERT INTO chat_channel_members (channel_id, username, role)
                VALUES (@ch, @u, 'MEMBER')
                ON CONFLICT DO NOTHING
                """, conn, tx);
            insMem.Parameters.AddWithValue("ch", chId);
            insMem.Parameters.AddWithValue("u", m);
            await insMem.ExecuteNonQueryAsync();
        }

        return chId;
    }

    private static string NormalizeStatus(string status) => status.Trim().ToUpperInvariant() switch
    {
        "TODO" or "IN_PROGRESS" or "REVIEW" or "DONE" or "CANCELLED" => status.Trim().ToUpperInvariant(),
        _ => "TODO"
    };

    private static string NormalizePriority(string priority) => priority.Trim().ToUpperInvariant() switch
    {
        "LOW" or "MEDIUM" or "HIGH" or "URGENT" => priority.Trim().ToUpperInvariant(),
        _ => "MEDIUM"
    };

    private static AppTaskDto ReadTask(NpgsqlDataReader r)
    {
        var watchersRaw = r.IsDBNull(10) ? "[]" : r.GetString(10);
        var checklistRaw = r.IsDBNull(13) ? "[]" : r.GetString(13);

        return new AppTaskDto
        {
            Id = r.GetString(0),
            TaskCode = r.GetString(1),
            Title = r.GetString(2),
            Description = r.IsDBNull(3) ? null : r.GetString(3),
            Status = r.GetString(4),
            Priority = r.GetString(5),
            AssigneeUsername = r.GetString(6),
            AssigneeFullName = r.IsDBNull(7) ? r.GetString(6) : r.GetString(7),
            AssignerUsername = r.GetString(8),
            AssignerFullName = r.IsDBNull(9) ? r.GetString(8) : r.GetString(9),
            Watchers = JsonSerializer.Deserialize<List<string>>(watchersRaw, JsonOpts) ?? new(),
            DueDate = r.IsDBNull(11) ? null : r.GetDateTime(11).ToString("yyyy-MM-dd"),
            Progress = r.GetInt32(12),
            Checklist = JsonSerializer.Deserialize<List<ChecklistItemDto>>(checklistRaw, JsonOpts) ?? new(),
            ChannelId = r.IsDBNull(14) ? null : r.GetString(14),
            ChannelName = r.IsDBNull(15) ? null : r.GetString(15),
            MessageId = r.IsDBNull(16) ? null : r.GetString(16),
            LinkedAppCode = r.IsDBNull(17) ? null : r.GetString(17),
            LinkedRecordId = r.IsDBNull(18) ? null : r.GetString(18),
            CompletedAt = r.IsDBNull(19) ? null : r.GetDateTime(19),
            CreatedAt = r.GetDateTime(20),
            UpdatedAt = r.GetDateTime(21)
        };
    }
}
