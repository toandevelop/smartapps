using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public partial class AutomationService
{
    private readonly string _connStr;
    private readonly NotificationService _notifications;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AuthService _auth;
    private readonly TaskService _tasks;
    private readonly FormRuleService _formRules;
    private readonly RecordWriteService _writer;
    private readonly OutboxService _outbox;
    private readonly VietnamIntegrationService _vietnam;

    public AutomationService(
        IConfiguration config,
        NotificationService notifications,
        IHttpClientFactory httpFactory,
        AuthService auth,
        TaskService tasks,
        MetadataService metadata,
        DynamicComputeService compute,
        RecordWriteService writer,
        OutboxService outbox,
        VietnamIntegrationService vietnam)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _notifications = notifications;
        _httpFactory = httpFactory;
        _auth = auth;
        _tasks = tasks;
        _formRules = new FormRuleService(metadata, compute);
        _writer = writer;
        _outbox = outbox;
        _vietnam = vietnam;
    }

    public async Task<List<AppAutomationDto>> GetAutomationsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT id, name, description, source_app_id, source_table_id, trigger_event,
                   condition_field, condition_operator, condition_value,
                   target_app_id, target_module_id, target_table_id, action_type,
                   match_source_field, match_target_field,
                   webhook_url, webhook_secret, notify_recipient, notify_template,
                   field_mappings::text, is_active, run_count, last_run_at
            FROM app_automations
            ORDER BY created_at DESC
            """;

        var list = new List<AppAutomationDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(ReadAutomation(r));
        }
        return list;
    }

    public async Task<AppAutomationDto> UpsertAutomationAsync(UpsertAutomationRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"auto_{Guid.NewGuid().ToString("N")[..8]}" : req.Id.Trim();
        var sql = """
            INSERT INTO app_automations (id, name, description, source_app_id, source_table_id, trigger_event,
                condition_field, condition_operator, condition_value,
                target_app_id, target_module_id, target_table_id, action_type,
                match_source_field, match_target_field,
                webhook_url, webhook_secret, notify_recipient, notify_template,
                field_mappings, is_active)
            VALUES (@id, @name, @desc, @sa, @st, @trig, @cf, @co, @cv, @ta, @tm, @tt, @act, @msf, @mtf,
                    @wu, @ws, @nr, @nt, @map::jsonb, @active)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                source_app_id = EXCLUDED.source_app_id,
                source_table_id = EXCLUDED.source_table_id,
                trigger_event = EXCLUDED.trigger_event,
                condition_field = EXCLUDED.condition_field,
                condition_operator = EXCLUDED.condition_operator,
                condition_value = EXCLUDED.condition_value,
                target_app_id = EXCLUDED.target_app_id,
                target_module_id = EXCLUDED.target_module_id,
                target_table_id = EXCLUDED.target_table_id,
                action_type = EXCLUDED.action_type,
                match_source_field = EXCLUDED.match_source_field,
                match_target_field = EXCLUDED.match_target_field,
                webhook_url = EXCLUDED.webhook_url,
                webhook_secret = EXCLUDED.webhook_secret,
                notify_recipient = EXCLUDED.notify_recipient,
                notify_template = EXCLUDED.notify_template,
                field_mappings = EXCLUDED.field_mappings,
                is_active = EXCLUDED.is_active
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("sa", req.SourceAppId);
        cmd.Parameters.AddWithValue("st", req.SourceTableId);
        cmd.Parameters.AddWithValue("trig", req.TriggerEvent);
        cmd.Parameters.AddWithValue("cf", string.IsNullOrWhiteSpace(req.ConditionField) ? DBNull.Value : req.ConditionField.Trim());
        cmd.Parameters.AddWithValue("co", string.IsNullOrWhiteSpace(req.ConditionOperator) ? "EQUALS" : req.ConditionOperator);
        cmd.Parameters.AddWithValue("cv", string.IsNullOrWhiteSpace(req.ConditionValue) ? DBNull.Value : req.ConditionValue.Trim());
        cmd.Parameters.AddWithValue("ta", string.IsNullOrWhiteSpace(req.TargetAppId) ? DBNull.Value : req.TargetAppId);
        cmd.Parameters.AddWithValue("tm", string.IsNullOrWhiteSpace(req.TargetModuleId) ? DBNull.Value : req.TargetModuleId);
        cmd.Parameters.AddWithValue("tt", string.IsNullOrWhiteSpace(req.TargetTableId) ? DBNull.Value : req.TargetTableId);
        cmd.Parameters.AddWithValue("act", string.IsNullOrWhiteSpace(req.ActionType) ? "CREATE_RECORD" : req.ActionType);
        cmd.Parameters.AddWithValue("msf", string.IsNullOrWhiteSpace(req.MatchSourceField) ? DBNull.Value : req.MatchSourceField.Trim());
        cmd.Parameters.AddWithValue("mtf", string.IsNullOrWhiteSpace(req.MatchTargetField) ? DBNull.Value : req.MatchTargetField.Trim());
        cmd.Parameters.AddWithValue("wu", string.IsNullOrWhiteSpace(req.WebhookUrl) ? DBNull.Value : req.WebhookUrl.Trim());
        cmd.Parameters.AddWithValue("ws", string.IsNullOrWhiteSpace(req.WebhookSecret) ? DBNull.Value : req.WebhookSecret.Trim());
        cmd.Parameters.AddWithValue("nr", string.IsNullOrWhiteSpace(req.NotifyRecipient) ? DBNull.Value : req.NotifyRecipient.Trim());
        cmd.Parameters.AddWithValue("nt", string.IsNullOrWhiteSpace(req.NotifyTemplate) ? DBNull.Value : req.NotifyTemplate.Trim());
        cmd.Parameters.AddWithValue("map", JsonSerializer.Serialize(req.FieldMappings ?? new()));
        cmd.Parameters.AddWithValue("active", req.IsActive);
        await cmd.ExecuteNonQueryAsync();

        return (await GetAutomationsAsync()).First(a => a.Id == id);
    }

    public async Task<bool> ToggleAutomationAsync(string id, bool isActive)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE app_automations SET is_active = @a WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("a", isActive);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAutomationAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM app_automations WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<AppAutomationLogDto>> GetLogsAsync(int limit = 30)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT l.id, l.automation_id, a.name, l.source_record_id, l.target_record_id, l.status, l.message, l.created_at
            FROM app_automation_logs l
            LEFT JOIN app_automations a ON a.id = l.automation_id
            ORDER BY l.created_at DESC
            LIMIT @lim
            """;
        var list = new List<AppAutomationLogDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("lim", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AppAutomationLogDto
            {
                Id = r.GetString(0),
                AutomationId = r.GetString(1),
                AutomationName = r.IsDBNull(2) ? null : r.GetString(2),
                SourceRecordId = r.IsDBNull(3) ? null : r.GetString(3),
                TargetRecordId = r.IsDBNull(4) ? null : r.GetString(4),
                Status = r.GetString(5),
                Message = r.IsDBNull(6) ? null : r.GetString(6),
                CreatedAt = r.GetDateTime(7)
            });
        }
        return list;
    }

    public async Task<object?> DryRunRuleAsync(string ruleId)
    {
        var rule = (await GetAutomationsAsync()).FirstOrDefault(r => r.Id == ruleId);
        if (rule == null) return null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, app_id, module_id, table_id, record_key, data::text, created_by
            FROM app_records
            WHERE table_id = @tbl AND deleted_at IS NULL
            ORDER BY updated_at DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tbl", rule.SourceTableId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
        {
            return new
            {
                matched = false,
                reason = "Bảng nguồn chưa có bản ghi nào để chạy thử.",
                actionType = rule.ActionType
            };
        }

        var sampleData = new Dictionary<string, object?>();
        using (var doc = JsonDocument.Parse(r.GetString(5)))
        {
            foreach (var p in doc.RootElement.EnumerateObject())
                sampleData[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        }
        var sample = new AppRecordDto
        {
            Id = r.GetString(0),
            AppId = r.GetString(1),
            ModuleId = r.GetString(2),
            TableId = r.GetString(3),
            RecordKey = r.IsDBNull(4) ? null : r.GetString(4),
            Data = sampleData,
            CreatedBy = r.GetString(6)
        };

        var matched = EvaluateCondition(rule, sample.Data);
        object? previewPayload = rule.ActionType.ToUpperInvariant() switch
        {
            "SEND_NOTIFICATION" => new
            {
                recipient = rule.NotifyRecipient == "{{created_by}}" ? sample.CreatedBy : (rule.NotifyRecipient ?? "*"),
                message = ResolveTemplate(rule.NotifyTemplate ?? "Cập nhật {{record_key}}", sample)
            },
            "CALL_WEBHOOK" => new { url = rule.WebhookUrl, eventName = rule.TriggerEvent, sampleRecordKey = sample.RecordKey },
            "CREATE_USER" => BuildCreateUserPreview(rule.FieldMappings, sample),
            "UPDATE_USER_STATUS" => BuildMappedPayload(rule.FieldMappings, sample),
            "CREATE_TASK" => BuildMappedPayload(rule.FieldMappings, sample),
            _ => BuildMappedPayload(rule.FieldMappings, sample)
        };

        return new
        {
            matched,
            actionType = rule.ActionType,
            sourceRecordKey = sample.RecordKey ?? sample.Id,
            reason = matched
                ? $"Khớp điều kiện trên bản ghi '{sample.RecordKey ?? sample.Id}' — sẵn sàng thực thi {rule.ActionType}."
                : $"Bản ghi mới nhất '{sample.RecordKey ?? sample.Id}' không thỏa điều kiện ({rule.ConditionField} = {rule.ConditionValue}).",
            previewPayload
        };
    }

    public async Task<List<string>> ExecuteTriggersAsync(string triggerEvent, AppRecordDto sourceRecord, int depth = 0)
    {
        var executed = new List<string>();
        if (depth >= 3) return executed; // Infinite loop recursion guard

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT id, name, description, source_app_id, source_table_id, trigger_event,
                   condition_field, condition_operator, condition_value,
                   target_app_id, target_module_id, target_table_id, action_type,
                   match_source_field, match_target_field,
                   webhook_url, webhook_secret, notify_recipient, notify_template,
                   field_mappings::text, is_active, run_count, last_run_at
            FROM app_automations
            WHERE source_table_id = @tbl
              AND is_active = TRUE
              AND (trigger_event = @ev OR trigger_event = 'ANY_SAVE')
            """;

        var rules = new List<AppAutomationDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("tbl", sourceRecord.TableId);
            cmd.Parameters.AddWithValue("ev", triggerEvent);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) rules.Add(ReadAutomation(r));
        }

        foreach (var rule in rules)
        {
            if (!EvaluateCondition(rule, sourceRecord.Data)) continue;
            try
            {
                if (await ExecuteRuleActionAsync(conn, rule, sourceRecord, triggerEvent)) executed.Add(rule.Name);
            }
            catch (Exception ex) when (ex is FormRuleValidationException or FormRuleConfigurationException or RecordLockedException or DuplicateIdentityKeyException or RecordConflictException)
            { await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, null, "FAILED", ex.Message); }
        }

        return executed;
    }

    public async Task<int> RunScheduledDateTriggersAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var rules = new List<AppAutomationDto>();
        await using (var rCmd = new NpgsqlCommand("""
            SELECT id, name, description, source_app_id, source_table_id, trigger_event,
                   condition_field, condition_operator, condition_value,
                   target_app_id, target_module_id, target_table_id, action_type,
                   match_source_field, match_target_field,
                   webhook_url, webhook_secret, notify_recipient, notify_template,
                   field_mappings::text, is_active, run_count, last_run_at
            FROM app_automations
            WHERE is_active = TRUE
              AND trigger_event = 'SCHEDULED_DATE'
              AND condition_field IS NOT NULL AND condition_field <> ''
            """, conn))
        await using (var rr = await rCmd.ExecuteReaderAsync(ct))
        {
            while (await rr.ReadAsync(ct)) rules.Add(ReadAutomation(rr));
        }
        if (rules.Count == 0) return 0;

        int fired = 0;
        foreach (var rule in rules)
        {
            var records = new List<AppRecordDto>();
            await using (var recCmd = new NpgsqlCommand("""
                SELECT id, app_id, module_id, table_id, record_key, data::text, created_by
                FROM app_records
                WHERE table_id = @tbl AND deleted_at IS NULL
                ORDER BY updated_at DESC
                LIMIT 500
                """, conn))
            {
                recCmd.Parameters.AddWithValue("tbl", rule.SourceTableId);
                await using var r = await recCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var sampleData = new Dictionary<string, object?>();
                    using (var doc = JsonDocument.Parse(r.GetString(5)))
                    {
                        foreach (var p in doc.RootElement.EnumerateObject())
                            sampleData[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                    }
                    records.Add(new AppRecordDto
                    {
                        Id = r.GetString(0),
                        AppId = r.GetString(1),
                        ModuleId = r.GetString(2),
                        TableId = r.GetString(3),
                        RecordKey = r.IsDBNull(4) ? null : r.GetString(4),
                        Data = sampleData,
                        CreatedBy = r.GetString(6)
                    });
                }
            }

            foreach (var rec in records)
            {
                if (!EvaluateCondition(rule, rec.Data)) continue;

                // Deduplicate: run at most once per day per (rule, record)
                await using var chkCmd = new NpgsqlCommand("""
                    SELECT 1 FROM app_automation_logs
                    WHERE automation_id = @aid
                      AND source_record_id = @sid
                      AND status = 'SUCCESS'
                      AND created_at::date = CURRENT_DATE
                    LIMIT 1
                    """, conn);
                chkCmd.Parameters.AddWithValue("aid", rule.Id);
                chkCmd.Parameters.AddWithValue("sid", rec.Id);
                if (await chkCmd.ExecuteScalarAsync(ct) != null) continue;

                try
                {
                    if (await ExecuteRuleActionAsync(conn, rule, rec, "SCHEDULED_DATE")) fired++;
                }
                catch (Exception ex) when (ex is FormRuleValidationException or FormRuleConfigurationException or RecordLockedException or DuplicateIdentityKeyException or RecordConflictException)
                { await RecordExecutionAsync(conn, rule.Id, rec.Id, null, "FAILED", ex.Message); }
            }
        }
        return fired;
    }

    private async Task<bool> ExecuteRuleActionAsync(NpgsqlConnection conn, AppAutomationDto rule, AppRecordDto sourceRecord, string triggerEvent)
    {
        var executedAny = false;
        var targetAppCode = await ResolveAppCodeAsync(conn, rule.TargetAppId ?? rule.SourceAppId);

        if (rule.ActionType.Equals("CREATE_RECORD", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(rule.TargetAppId) &&
                !string.IsNullOrWhiteSpace(rule.TargetModuleId) &&
                !string.IsNullOrWhiteSpace(rule.TargetTableId))
            {
                var (created, _) = await _writer.UpsertCoreAsync(
                    new UpsertRecordRequest
                    {
                        AppId = rule.TargetAppId,
                        ModuleId = rule.TargetModuleId,
                        TableId = rule.TargetTableId,
                        Data = BuildMappedPayload(rule.FieldMappings, sourceRecord)
                    },
                    actor: "automation",
                    isAdmin: false,
                    auditNote: $"Tự động tạo bởi quy tắc '{rule.Name}' từ bản ghi {sourceRecord.RecordKey ?? sourceRecord.Id}");

                await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, created.Id, "SUCCESS",
                    $"Tự động tạo bản ghi {created.Id} tại bảng {rule.TargetTableId}");
                await _notifications.CreateAsync($"Tự động hóa: {rule.Name}",
                    $"Đã tự động tạo bản ghi mới ({created.RecordKey ?? created.Id}).", targetAppCode, created.Id, "*");
                executedAny = true;
            }
            else if (rule.ActionType.Equals("UPDATE_RECORD", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(rule.TargetTableId) &&
                     !string.IsNullOrWhiteSpace(rule.MatchSourceField) &&
                     !string.IsNullOrWhiteSpace(rule.MatchTargetField))
            {
                if (!sourceRecord.Data.TryGetValue(rule.MatchSourceField, out var matchRaw) || matchRaw == null)
                    return false;
                var matchVal = matchRaw.ToString()!;
                var patchData = BuildMappedPayload(rule.FieldMappings, sourceRecord);

                var updatedIds = await ApplyValidatedPatchAsync(conn, rule.TargetTableId, patchData,
                    rule.MatchTargetField, matchVal, null, $"Tự động cập nhật bởi quy tắc '{rule.Name}'");

                if (updatedIds.Count > 0)
                {
                    await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, updatedIds.First(), "SUCCESS",
                        $"Tự động cập nhật {updatedIds.Count} bản ghi ({rule.MatchTargetField}={matchVal}) tại {rule.TargetTableId}");
                    await _notifications.CreateAsync($"Tự động hóa: {rule.Name}",
                        $"Đã cập nhật {updatedIds.Count} bản ghi liên kết ({matchVal}).", targetAppCode, updatedIds.First(), "*");
                    executedAny = true;
                }
            }
            else if (rule.ActionType.Equals("UPDATE_SELF", StringComparison.OrdinalIgnoreCase))
            {
                var patchData = BuildMappedPayload(rule.FieldMappings, sourceRecord);
                if (patchData.Count > 0)
                {
                    await ApplyValidatedPatchAsync(conn, sourceRecord.TableId, patchData, null, null, sourceRecord.Id,
                        $"Tự động cập nhật chính bản ghi bởi quy tắc '{rule.Name}'");
                    await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, sourceRecord.Id, "SUCCESS",
                        $"Tự động cập nhật chính bản ghi {sourceRecord.RecordKey ?? sourceRecord.Id}");
                    executedAny = true;
                }
            }
            else if (rule.ActionType.Equals("SEND_NOTIFICATION", StringComparison.OrdinalIgnoreCase))
            {
                var recipient = string.IsNullOrWhiteSpace(rule.NotifyRecipient) ? "*" : rule.NotifyRecipient.Trim();
                if (recipient.Equals("{{created_by}}", StringComparison.OrdinalIgnoreCase))
                    recipient = sourceRecord.CreatedBy;

                var msg = ResolveTemplate(
                    string.IsNullOrWhiteSpace(rule.NotifyTemplate) ? "Cập nhật từ bản ghi {{record_key}}" : rule.NotifyTemplate,
                    sourceRecord);
                await _notifications.CreateAsync($"Tự động hóa: {rule.Name}", msg, targetAppCode, sourceRecord.Id, recipient);
                await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, null, "SUCCESS",
                    $"Đã gửi thông báo tới '{recipient}': {msg}");
                executedAny = true;
            }
            else if (rule.ActionType.Equals("CALL_WEBHOOK", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(rule.WebhookUrl))
            {
                var body = new
                {
                    @event = triggerEvent,
                    ruleId = rule.Id,
                    ruleName = rule.Name,
                    record = sourceRecord,
                    timestamp = DateTime.UtcNow
                };
                var outboxId = await _outbox.EnqueueAsync(
                    conn,
                    null,
                    "WEBHOOK_CALL",
                    "app_record",
                    sourceRecord.Id,
                    new
                    {
                        url = rule.WebhookUrl,
                        secret = rule.WebhookSecret,
                        ruleId = rule.Id,
                        sourceRecordId = sourceRecord.Id,
                        body
                    });
                await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, outboxId, "QUEUED",
                    $"Đã đưa Webhook {rule.WebhookUrl} vào hàng đợi Outbox ({outboxId})");
                executedAny = true;
            }
            else if (rule.ActionType.Equals("CREATE_USER", StringComparison.OrdinalIgnoreCase))
            {
                var mapped = BuildMappedPayload(rule.FieldMappings, sourceRecord);
                var email = GetMappedString(mapped, "email");
                var empCode = GetMappedString(mapped, "employeeCode") ?? sourceRecord.RecordKey;
                var fullName = GetMappedString(mapped, "fullName") ?? empCode ?? "Nhân sự mới";
                var dept = GetMappedString(mapped, "department") ?? "Toàn công ty";
                var roleRaw = GetMappedString(mapped, "roleCode") ?? "STAFF";
                var roleCodes = roleRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(r => r.ToUpperInvariant()).Distinct().ToList();
                if (roleCodes.Count == 0) roleCodes.Add("STAFF");
                var password = GetMappedString(mapped, "password") ?? "123456";
                var rawUsername = GetMappedString(mapped, "username");
                var username = DeriveUsername(rawUsername, email, empCode, fullName);

                var existingId = await FindExistingUserAsync(conn, username, empCode, email);
                if (!string.IsNullOrWhiteSpace(existingId))
                {
                    await using var linkCmd = new NpgsqlCommand("""
                        UPDATE sys_users
                        SET employee_code = COALESCE(NULLIF(employee_code, ''), @ec),
                            email = COALESCE(NULLIF(email, ''), @em)
                        WHERE id = @id
                        """, conn);
                    linkCmd.Parameters.AddWithValue("id", existingId);
                    linkCmd.Parameters.AddWithValue("ec", (object?)empCode?.ToUpperInvariant() ?? DBNull.Value);
                    linkCmd.Parameters.AddWithValue("em", (object?)email ?? DBNull.Value);
                    await linkCmd.ExecuteNonQueryAsync();

                    await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, existingId, "SUCCESS",
                        $"Tài khoản '{existingId}' đã tồn tại — đã đồng bộ mã nhân viên ({empCode ?? "—"}).");
                    executedAny = true;
                }
                else
                {
                    var createdUser = await _auth.UpsertUserAsync(new UpsertUserRequest
                    {
                        Username = username,
                        FullName = fullName,
                        Email = email,
                        EmployeeCode = empCode,
                        Department = dept,
                        RoleCode = roleCodes[0],
                        RoleCodes = roleCodes,
                        Password = password,
                        IsActive = true
                    });

                    await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, createdUser.Id, "SUCCESS",
                        $"Đã tự động tạo tài khoản @{createdUser.Username} (Vai trò: {string.Join(", ", createdUser.RoleCodes)}, Mã NV: {createdUser.EmployeeCode ?? "—"}).");
                    await _notifications.CreateAsync(
                        $"Tự động tạo User: @{createdUser.Username}",
                        $"Hệ thống đã tự tạo tài khoản cho {createdUser.FullName} ({createdUser.EmployeeCode ?? createdUser.Email ?? createdUser.Username}) từ quy tắc \"{rule.Name}\".",
                        targetAppCode,
                        createdUser.Id,
                        "admin",
                        "AUTOMATION",
                        "/admin");
                    executedAny = true;
                }
            }
            else if (rule.ActionType.Equals("UPDATE_USER_STATUS", StringComparison.OrdinalIgnoreCase))
            {
                var mapped = BuildMappedPayload(rule.FieldMappings, sourceRecord);
                var matchBy = (GetMappedString(mapped, "matchBy") ?? "employeeCode").ToLowerInvariant();
                var matchVal = GetMappedString(mapped, "matchValue") ?? sourceRecord.RecordKey;
                var activeVal = !string.Equals(GetMappedString(mapped, "isActive"), "true", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : true;

                if (!string.IsNullOrWhiteSpace(matchVal))
                {
                    var whereSql = matchBy switch
                    {
                        "email" => "LOWER(email) = LOWER(@v)",
                        "username" => "LOWER(username) = LOWER(@v)",
                        _ => "UPPER(employee_code) = UPPER(@v)"
                    };
                    await using var upUser = new NpgsqlCommand($"""
                        UPDATE sys_users
                        SET is_active = @act
                        WHERE {whereSql} AND id <> 'usr_admin'
                        RETURNING id, username
                        """, conn);
                    upUser.Parameters.AddWithValue("act", activeVal);
                    upUser.Parameters.AddWithValue("v", matchVal.Trim());

                    string? uid = null;
                    string? uname = null;
                    await using (var ur = await upUser.ExecuteReaderAsync())
                    {
                        if (await ur.ReadAsync())
                        {
                            uid = ur.GetString(0);
                            uname = ur.GetString(1);
                        }
                    }

                    if (uid != null)
                    {
                        await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, uid, "SUCCESS",
                            $"Đã {(activeVal ? "mở khóa" : "tạm khóa")} tài khoản @{uname} ({matchBy}={matchVal}).");
                        await _notifications.CreateAsync(
                            $"Tự động {(activeVal ? "mở khóa" : "khóa")} User @{uname}",
                            $"Quy tắc \"{rule.Name}\" đã {(activeVal ? "kích hoạt lại" : "khóa")} tài khoản @{uname}.",
                            targetAppCode,
                            uid,
                            "admin",
                            "AUTOMATION",
                            "/admin");
                        executedAny = true;
                    }
                    else
                    {
                        await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, null, "FAILED",
                            $"Không tìm thấy User khớp {matchBy}='{matchVal}'.");
                    }
                }
            }
            else if (rule.ActionType.Equals("CREATE_TASK", StringComparison.OrdinalIgnoreCase))
            {
                var mapped = BuildMappedPayload(rule.FieldMappings, sourceRecord);
                var title = GetMappedString(mapped, "title") ?? $"Công việc từ {sourceRecord.RecordKey ?? sourceRecord.Id}";
                var desc = GetMappedString(mapped, "description");
                var assignee = GetMappedString(mapped, "assigneeUsername") ?? "admin";
                var priority = GetMappedString(mapped, "priority") ?? "MEDIUM";
                var due = GetMappedString(mapped, "dueDate");

                var (task, _) = await _tasks.CreateTaskAsync(new CreateTaskRequest
                {
                    Title = title,
                    Description = desc,
                    AssigneeUsername = assignee,
                    Priority = priority,
                    DueDate = due,
                    LinkedAppCode = targetAppCode,
                    LinkedRecordId = sourceRecord.Id,
                    PostToChat = false
                }, string.IsNullOrWhiteSpace(sourceRecord.CreatedBy) ? "automation" : sourceRecord.CreatedBy);

                await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, task.Id, "SUCCESS",
                    $"Đã tự động giao việc {task.TaskCode} cho @{task.AssigneeUsername}: {task.Title}");
                executedAny = true;
            }
            else if (rule.ActionType.Equals("RUN_PLUGIN", StringComparison.OrdinalIgnoreCase))
            {
                var mapped = BuildMappedPayload(rule.FieldMappings, sourceRecord);
                var pluginCode = GetMappedString(mapped, "pluginCode") ?? rule.MatchTargetField;
                var operation = GetMappedString(mapped, "operation") ?? "send_sms";
                if (!string.IsNullOrWhiteSpace(pluginCode))
                {
                    mapped["recordId"] = sourceRecord.Id;
                    mapped["tableId"] = sourceRecord.TableId;
                    mapped["orderRef"] = mapped.GetValueOrDefault("orderRef") ?? sourceRecord.RecordKey ?? sourceRecord.Id;
                    try
                    {
                        var res = await _vietnam.ExecutePluginAsync(pluginCode, operation, mapped, "automation");
                        await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, null,
                            res.Success ? "SUCCESS" : "FAILED", res.Message);
                        executedAny = res.Success;
                    }
                    catch (Exception ex)
                    {
                        await RecordExecutionAsync(conn, rule.Id, sourceRecord.Id, null, "FAILED", ex.Message);
                    }
                }
            }
        return executedAny;
    }

    private static async Task<string?> ResolveAppCodeAsync(NpgsqlConnection conn, string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        await using var cmd = new NpgsqlCommand("SELECT code FROM sys_apps WHERE id = @id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", appId);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private static bool EvaluateCondition(AppAutomationDto rule, Dictionary<string, object?> data)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionField)) return true;
        return WorkflowService.MatchesCondition(
            rule.ConditionField,
            rule.ConditionOperator,
            rule.ConditionValue,
            data);
    }

    private async Task<List<string>> ApplyValidatedPatchAsync(NpgsqlConnection conn, string tableId,
        Dictionary<string, object?> patch, string? matchField, string? matchValue, string? recordId, string? auditNote = null)
    {
        var ids = new List<string>();
        await using (var query = new NpgsqlCommand("""
            SELECT id FROM app_records WHERE table_id=@tbl AND deleted_at IS NULL
              AND (@id IS NULL OR id=@id)
              AND (@field IS NULL OR data ->> @field=@value)
            ORDER BY id
            """, conn))
        {
            query.Parameters.AddWithValue("tbl", tableId);
            query.Parameters.Add(new NpgsqlParameter("id", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)recordId ?? DBNull.Value });
            query.Parameters.Add(new NpgsqlParameter("field", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)matchField ?? DBNull.Value });
            query.Parameters.Add(new NpgsqlParameter("value", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)matchValue ?? DBNull.Value });
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        }

        var updated = new List<string>();
        foreach (var id in ids)
        {
            var saved = await _writer.PatchRecordCoreAsync(id, patch, actor: "automation", isAdmin: false, auditNote: auditNote);
            if (saved != null) updated.Add(saved.Id);
        }
        return updated;
    }

    public static string ResolveTemplate(string template, AppRecordDto src)
    {
        var resolved = DateAddRegex().Replace(template, m =>
        {
            var days = int.TryParse(m.Groups[1].Value, out var d) ? d : 0;
            return DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");
        });

        resolved = EmailPrefixRegex().Replace(resolved, m =>
        {
            var field = m.Groups[1].Value.Trim();
            var raw = src.Data.TryGetValue(field, out var v) ? v?.ToString() ?? "" : "";
            var at = raw.IndexOf('@');
            return at > 0 ? raw[..at].Trim() : raw.Trim();
        });

        resolved = SlugRegex().Replace(resolved, m =>
        {
            var field = m.Groups[1].Value.Trim();
            var raw = src.Data.TryGetValue(field, out var v) ? v?.ToString() ?? "" : "";
            return ToAsciiSlug(raw);
        });

        resolved = resolved
            .Replace("{{TODAY}}", DateTime.UtcNow.ToString("yyyy-MM-dd"))
            .Replace("{{AUTO_KEY}}", $"AUTO-{DateTime.UtcNow:HHmmss}")
            .Replace("{{record_key}}", src.RecordKey ?? "")
            .Replace("{{created_by}}", src.CreatedBy);
        foreach (var kv in src.Data)
        {
            resolved = resolved.Replace($"{{{{{kv.Key}}}}}", kv.Value?.ToString() ?? "");
        }
        return resolved;
    }

    public static string DeriveUsername(string? explicitUsername, string? email, string? employeeCode, string? fullName)
    {
        var candidate = explicitUsername;
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(email))
        {
            var at = email.IndexOf('@');
            candidate = at > 0 ? email[..at] : email;
        }
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = employeeCode;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = fullName;

        var slug = ToAsciiSlug(candidate ?? "");
        return string.IsNullOrWhiteSpace(slug) ? $"user_{DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000}" : slug;
    }

    private static string ToAsciiSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var replaced = input.Replace('đ', 'd').Replace('Đ', 'D');
        var normalized = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
        var clean = Regex.Replace(ascii, @"[^a-z0-9._-]+", "");
        return clean.Trim('.', '_', '-');
    }

    private static Dictionary<string, object?> BuildCreateUserPreview(Dictionary<string, string> mappings, AppRecordDto src)
    {
        var mapped = BuildMappedPayload(mappings, src);
        var email = GetMappedString(mapped, "email");
        var empCode = GetMappedString(mapped, "employeeCode") ?? src.RecordKey;
        var fullName = GetMappedString(mapped, "fullName") ?? empCode ?? "Nhân sự mới";
        mapped["resolvedUsername"] = DeriveUsername(GetMappedString(mapped, "username"), email, empCode, fullName);
        return mapped;
    }

    private static string? GetMappedString(Dictionary<string, object?> map, string key)
    {
        foreach (var kv in map)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var s = kv.Value?.ToString()?.Trim();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }
        return null;
    }

    private static async Task<string?> FindExistingUserAsync(NpgsqlConnection conn, string username, string? empCode, string? email)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id FROM sys_users
            WHERE LOWER(username) = LOWER(@u)
               OR (@ec IS NOT NULL AND UPPER(employee_code) = UPPER(@ec))
               OR (@em IS NOT NULL AND LOWER(email) = LOWER(@em))
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("u", username);
        cmd.Parameters.AddWithValue("ec", string.IsNullOrWhiteSpace(empCode) ? DBNull.Value : empCode.Trim());
        cmd.Parameters.AddWithValue("em", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    [GeneratedRegex(@"\{\{DATE_ADD\((-?\d+)\)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex DateAddRegex();

    [GeneratedRegex(@"\{\{EMAIL_PREFIX\(([^)]+)\)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPrefixRegex();

    [GeneratedRegex(@"\{\{SLUG\(([^)]+)\)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex SlugRegex();

    private static Dictionary<string, object?> BuildMappedPayload(Dictionary<string, string> mappings, AppRecordDto src)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in mappings)
        {
            var resolved = ResolveTemplate(kv.Value ?? string.Empty, src);
            if (resolved.Equals("true", StringComparison.OrdinalIgnoreCase)) result[kv.Key] = true;
            else if (resolved.Equals("false", StringComparison.OrdinalIgnoreCase)) result[kv.Key] = false;
            else result[kv.Key] = resolved;
        }
        return result;
    }

    private static async Task RecordExecutionAsync(NpgsqlConnection conn, string autoId, string srcId, string? targetId, string status, string msg)
    {
        await using var up = new NpgsqlCommand(
            "UPDATE app_automations SET run_count = run_count + 1, last_run_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
        up.Parameters.AddWithValue("id", autoId);
        await up.ExecuteNonQueryAsync();

        await using var log = new NpgsqlCommand("""
            INSERT INTO app_automation_logs (id, automation_id, source_record_id, target_record_id, status, message)
            VALUES (@id, @aid, @sid, @tid, @st, @msg)
            """, conn);
        log.Parameters.AddWithValue("id", $"alog_{Guid.NewGuid().ToString("N")[..10]}");
        log.Parameters.AddWithValue("aid", autoId);
        log.Parameters.AddWithValue("sid", srcId);
        log.Parameters.AddWithValue("tid", (object?)targetId ?? DBNull.Value);
        log.Parameters.AddWithValue("st", status);
        log.Parameters.AddWithValue("msg", msg);
        await log.ExecuteNonQueryAsync();
    }

    private static AppAutomationDto ReadAutomation(NpgsqlDataReader r)
    {
        return new AppAutomationDto
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            Description = r.IsDBNull(2) ? null : r.GetString(2),
            SourceAppId = r.GetString(3),
            SourceTableId = r.GetString(4),
            TriggerEvent = r.GetString(5),
            ConditionField = r.IsDBNull(6) ? null : r.GetString(6),
            ConditionOperator = r.IsDBNull(7) ? "EQUALS" : r.GetString(7),
            ConditionValue = r.IsDBNull(8) ? null : r.GetString(8),
            TargetAppId = r.IsDBNull(9) ? null : r.GetString(9),
            TargetModuleId = r.IsDBNull(10) ? null : r.GetString(10),
            TargetTableId = r.IsDBNull(11) ? null : r.GetString(11),
            ActionType = r.GetString(12),
            MatchSourceField = r.IsDBNull(13) ? null : r.GetString(13),
            MatchTargetField = r.IsDBNull(14) ? null : r.GetString(14),
            WebhookUrl = r.IsDBNull(15) ? null : r.GetString(15),
            WebhookSecret = r.IsDBNull(16) ? null : r.GetString(16),
            NotifyRecipient = r.IsDBNull(17) ? null : r.GetString(17),
            NotifyTemplate = r.IsDBNull(18) ? null : r.GetString(18),
            FieldMappings = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(19)) ?? new(),
            IsActive = r.GetBoolean(20),
            RunCount = r.GetInt32(21),
            LastRunAt = r.IsDBNull(22) ? null : r.GetDateTime(22)
        };
    }
}
