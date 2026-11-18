using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

/// <summary>
/// Unified record write pipeline shared by UI/API upserts and automation actions.
/// Enforces auto-numbering, formulas, form rules, cascading lookups, identity uniqueness,
/// approval locks, optimistic concurrency, atomic audit logging, and realtime notifications.
/// </summary>
public sealed class RecordWriteService
{
    private readonly string _connStr;
    private readonly MetadataService _metadata;
    private readonly DynamicComputeService _compute;
    private readonly RealtimeEventService _realtime;

    public const string SelectColumns = """
        id, app_id, module_id, table_id, record_key, data::text, status, validation_errors::text,
        created_by, updated_by, created_at, updated_at,
        COALESCE(approval_status, 'DRAFT'), approved_by, approved_at, rejection_reason, deleted_at, deleted_by,
        workflow_id, COALESCE(current_step_order, 0), current_step_name, COALESCE(workflow_history::text, '[]'),
        COALESCE(version, 1), workflow_snapshot::text
        """;

    public RecordWriteService(
        IConfiguration config,
        MetadataService metadata,
        DynamicComputeService compute,
        RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _metadata = metadata;
        _compute = compute;
        _realtime = realtime;
    }

    public async Task<(AppRecordDto Saved, bool IsNew)> UpsertCoreAsync(
        UpsertRecordRequest req,
        string actor = "system",
        bool isAdmin = false,
        IReadOnlySet<string>? serverManagedFields = null,
        string? auditNote = null,
        Dictionary<string, object?>? submittedOverride = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var result = await UpsertCoreInTxAsync(conn, tx, req, actor, isAdmin, serverManagedFields, auditNote, submittedOverride);

        await tx.CommitAsync();
        _realtime.Publish("record_changed", null, result.Saved.TableId, result.Saved.Id, "*", new { action = result.IsNew ? "CREATE" : "UPDATE" });
        return result;
    }

    public async Task<(AppRecordDto Saved, bool IsNew)> UpsertCoreInTxAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        UpsertRecordRequest req,
        string actor = "system",
        bool isAdmin = false,
        IReadOnlySet<string>? serverManagedFields = null,
        string? auditNote = null,
        Dictionary<string, object?>? submittedOverride = null)
    {
        var columns = await _metadata.GetColumnsAsync(req.TableId);
        var categorySets = await LoadCategorySetsAsync(columns);
        var rules = (await _metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == req.TableId)?.FormRules ?? new();
        if (rules.Count > 0) FormRuleService.ValidateDefinition(rules, columns);

        bool isNew = string.IsNullOrWhiteSpace(req.Id);
        var id = isNew ? $"rec_{Guid.NewGuid().ToString("N")[..12]}" : req.Id!.Trim();
        var normalizedData = NormalizeDict(req.Data);
        var submittedData = submittedOverride ?? normalizedData
            .Where(pair => serverManagedFields?.Contains(pair.Key) != true)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Dictionary<string, object?>? oldData = null;
        var currentVersion = 1;
        if (!isNew)
        {
            await using var oldCmd = new NpgsqlCommand(
                "SELECT data::text, COALESCE(approval_status, 'DRAFT'), COALESCE(version, 1) FROM app_records WHERE id = @id AND deleted_at IS NULL FOR UPDATE",
                conn, tx);
            oldCmd.Parameters.AddWithValue("id", id);
            await using var r = await oldCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var appStatus = r.GetString(1);
                currentVersion = r.GetInt32(2);
                if (req.ExpectedVersion.HasValue && req.ExpectedVersion.Value != currentVersion)
                {
                    throw new RecordConflictException("Bản ghi đã bị thay đổi bởi phiên khác. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
                }
                if (appStatus == "APPROVED" && !isAdmin)
                {
                    throw new RecordLockedException("Bản ghi đã được phê duyệt (APPROVED) và khóa chỉnh sửa.");
                }
                oldData = ParseJsonDict(r.GetString(0));
            }
        }
        else
        {
            foreach (var autoCol in columns.Where(c => c.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase)))
            {
                if (!normalizedData.TryGetValue(autoCol.ColumnCode, out var cur) || string.IsNullOrWhiteSpace(cur?.ToString()))
                {
                    normalizedData[autoCol.ColumnCode] = await NextAutoNumberAsync(conn, tx, req.TableId, autoCol.ColumnCode, autoCol.DefaultValue);
                }
            }
        }

        // Preserve omitted values even if Studio removed rules while a form was already open.
        if (rules.Count > 0 || oldData != null) normalizedData = FormRuleService.MergeWithDefaults(columns, normalizedData, oldData);
        var computed = _compute.EvaluateAndValidate(
            columns,
            categorySets,
            new List<(string, Dictionary<string, object?>)> { (id, normalizedData) }, rules).First();

        var finalData = computed.EvaluatedData;
        var identityCol = columns.FirstOrDefault(c => c.IsIdentityKey);
        var recordKey = req.RecordKey;
        if (string.IsNullOrWhiteSpace(recordKey) && identityCol != null && finalData.TryGetValue(identityCol.ColumnCode, out var keyVal))
        {
            recordKey = keyVal?.ToString();
        }
        await EvaluateCrossAppFormulasAsync(conn, tx, columns, finalData, recordKey);
        FormRuleService.EnforceWrite(columns, rules, finalData, oldData, submittedData, categorySets);

        foreach (var lCol in columns.Where(c => !string.IsNullOrWhiteSpace(c.RefTableId)
            && !string.IsNullOrWhiteSpace(c.LookupFilterField)
            && !string.IsNullOrWhiteSpace(c.LookupSourceField)))
        {
            var lookupVal = finalData.GetValueOrDefault(lCol.ColumnCode)?.ToString()?.Trim();
            var sourceVal = finalData.GetValueOrDefault(lCol.LookupSourceField!)?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(lookupVal) && !string.IsNullOrWhiteSpace(sourceVal))
            {
                var refCols = await _metadata.GetColumnsAsync(lCol.RefTableId!);
                var valKey = !string.IsNullOrWhiteSpace(lCol.RefValueField)
                    ? lCol.RefValueField!
                    : (refCols.FirstOrDefault(c => c.IsIdentityKey)?.ColumnCode ?? refCols.FirstOrDefault()?.ColumnCode ?? "id");
                await using var checkCmd = new NpgsqlCommand("""
                    SELECT 1 FROM app_records
                    WHERE table_id = @rtbl AND deleted_at IS NULL
                      AND COALESCE(data ->> @valKey, id) = @lval
                      AND LOWER(COALESCE(data ->> @fKey, '')) = LOWER(@sval)
                    LIMIT 1
                    """, conn, tx);
                checkCmd.Parameters.AddWithValue("rtbl", lCol.RefTableId!);
                checkCmd.Parameters.AddWithValue("valKey", valKey);
                checkCmd.Parameters.AddWithValue("lval", lookupVal);
                checkCmd.Parameters.AddWithValue("fKey", lCol.LookupFilterField!);
                checkCmd.Parameters.AddWithValue("sval", sourceVal);
                if (await checkCmd.ExecuteScalarAsync() == null)
                {
                    throw new FormRuleValidationException(new List<ValidationErrorItem>
                    {
                        new()
                        {
                            Field = lCol.ColumnCode,
                            Severity = "ERROR",
                            Message = $"Giá trị '{lookupVal}' của '{lCol.ColumnName}' không thuộc phạm vi đã chọn ở '{lCol.LookupSourceField}'."
                        }
                    });
                }
            }
        }

        var errors = computed.Errors;
        var status = !string.IsNullOrWhiteSpace(req.Status)
            ? req.Status!
            : (errors.Any(e => e.Severity == "ERROR") ? "ERROR" : "ACTIVE");

        if (identityCol != null && !string.IsNullOrWhiteSpace(recordKey))
        {
            await using var dupCmd = new NpgsqlCommand(
                "SELECT id FROM app_records WHERE table_id = @tbl AND deleted_at IS NULL AND LOWER(record_key) = LOWER(@key) AND id <> @id LIMIT 1",
                conn, tx);
            dupCmd.Parameters.AddWithValue("tbl", req.TableId);
            dupCmd.Parameters.AddWithValue("key", recordKey.Trim());
            dupCmd.Parameters.AddWithValue("id", id);
            var existingId = await dupCmd.ExecuteScalarAsync() as string;
            if (!string.IsNullOrWhiteSpace(existingId))
            {
                throw new DuplicateIdentityKeyException($"Mã định danh '{recordKey}' ({identityCol.ColumnName}) đã tồn tại trong bảng!");
            }
        }

        var expectedVersionForUpdate = req.ExpectedVersion ?? currentVersion;
        var sql = $"""
            INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status, version, validation_errors, created_by, updated_by, updated_at)
            VALUES (@id, @app, @mod, @tbl, @key, @data::jsonb, @st, 1, @errs::jsonb, @actor, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                record_key = EXCLUDED.record_key,
                data = EXCLUDED.data,
                status = EXCLUDED.status,
                version = COALESCE(app_records.version, 1) + 1,
                validation_errors = EXCLUDED.validation_errors,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            WHERE app_records.deleted_at IS NULL AND COALESCE(app_records.version, 1) = @expectedVer
            RETURNING {SelectColumns}
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", req.AppId);
        cmd.Parameters.AddWithValue("mod", req.ModuleId);
        cmd.Parameters.AddWithValue("tbl", req.TableId);
        cmd.Parameters.AddWithValue("key", (object?)recordKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("data", JsonSerializer.Serialize(finalData));
        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("errs", JsonSerializer.Serialize(errors));
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("expectedVer", expectedVersionForUpdate);

        AppRecordDto saved;
        try
        {
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                throw new RecordConflictException("Bản ghi đã bị thay đổi bởi phiên khác. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
            }
            saved = ReadRecord(r);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var colLabel = identityCol?.ColumnName ?? "Mã định danh";
            throw new DuplicateIdentityKeyException($"Mã định danh '{recordKey}' ({colLabel}) đã tồn tại trong bảng!");
        }

        var summary = auditNote ?? (isNew ? $"Tạo mới bản ghi {saved.RecordKey ?? saved.Id}" : $"Cập nhật bản ghi {saved.RecordKey ?? saved.Id}");
        await WriteAuditAsync(conn, saved.AppId, saved.TableId, saved.Id,
            isNew ? "CREATE" : "UPDATE", actor, oldData, finalData, summary, tx);

        return (saved, isNew);
    }

    public async Task SoftDeleteInTxAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string id,
        string tableId,
        string actor = "system",
        bool isAdmin = false)
    {
        await using var fetch = new NpgsqlCommand(
            "SELECT app_id, table_id, record_key, data::text, COALESCE(approval_status, 'DRAFT') FROM app_records WHERE id = @id AND table_id = @tbl AND deleted_at IS NULL FOR UPDATE",
            conn, tx);
        fetch.Parameters.AddWithValue("id", id);
        fetch.Parameters.AddWithValue("tbl", tableId);

        string appId;
        string? recKey;
        string rawData;
        string appStatus;
        await using (var r = await fetch.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return;
            appId = r.GetString(0);
            recKey = r.IsDBNull(2) ? null : r.GetString(2);
            rawData = r.GetString(3);
            appStatus = r.GetString(4);
        }

        if (!isAdmin && appStatus == "APPROVED")
        {
            throw new RecordLockedException("Không thể xóa bản ghi con đã được phê duyệt (APPROVED).");
        }

        await using (var del = new NpgsqlCommand(
            "UPDATE app_records SET deleted_at = CURRENT_TIMESTAMP, updated_by = @actor, updated_at = CURRENT_TIMESTAMP WHERE id = @id AND table_id = @tbl AND deleted_at IS NULL",
            conn, tx))
        {
            del.Parameters.AddWithValue("actor", actor);
            del.Parameters.AddWithValue("id", id);
            del.Parameters.AddWithValue("tbl", tableId);
            await del.ExecuteNonQueryAsync();
        }

        await WriteAuditAsync(conn, appId, tableId, id, "DELETE", actor,
            ParseJsonDict(rawData), null, $"Xóa bản ghi liên kết {recKey ?? id}", tx);
    }

    public async Task<AppRecordDto?> PatchRecordCoreAsync(
        string id,
        Dictionary<string, object?> patch,
        string actor = "system",
        bool isAdmin = false,
        int? expectedVersion = null,
        string? auditNote = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using var fetchCmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM app_records WHERE id = @id AND deleted_at IS NULL", conn);
        fetchCmd.Parameters.AddWithValue("id", id);

        AppRecordDto? existing = null;
        await using (var r = await fetchCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync()) existing = ReadRecord(r);
        }
        if (existing == null) return null;

        var normalizedPatch = NormalizeDict(patch);
        foreach (var kv in normalizedPatch)
        {
            existing.Data[kv.Key] = kv.Value;
        }

        var (saved, _) = await UpsertCoreAsync(
            new UpsertRecordRequest
            {
                Id = existing.Id,
                AppId = existing.AppId,
                ModuleId = existing.ModuleId,
                TableId = existing.TableId,
                RecordKey = existing.RecordKey,
                Data = existing.Data,
                ExpectedVersion = expectedVersion ?? existing.Version
            },
            actor,
            isAdmin,
            auditNote: auditNote,
            submittedOverride: normalizedPatch);

        return saved;
    }

    public async Task<Dictionary<string, HashSet<string>>> LoadCategorySetsAsync(List<SysColumnDto> columns)
    {
        var result = new Dictionary<string, HashSet<string>>();
        var catCodes = columns.Where(c => !string.IsNullOrWhiteSpace(c.RefCategoryCode)).Select(c => c.RefCategoryCode!).Distinct().ToList();
        foreach (var code in catCodes)
        {
            var items = await _metadata.GetCategoryItemsAsync(code);
            result[code] = items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return result;
    }

    public static async Task EvaluateCrossAppFormulasAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        List<SysColumnDto> columns,
        Dictionary<string, object?> data,
        string? recordKey)
    {
        if (string.IsNullOrWhiteSpace(recordKey)) return;
        foreach (var col in columns.Where(c => !string.IsNullOrWhiteSpace(c.Formula)))
        {
            var f = col.Formula!.Trim();
            var mCnt = Regex.Match(f, @"^CROSS_COUNT\(\s*([a-zA-Z0-9_]+)\s*,\s*([a-zA-Z0-9_]+)\s*\)$", RegexOptions.IgnoreCase);
            if (mCnt.Success)
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT COUNT(*) FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL AND LOWER(COALESCE(data ->> @fk, '')) = LOWER(@val)
                    """, conn, tx);
                cmd.Parameters.AddWithValue("tbl", mCnt.Groups[1].Value);
                cmd.Parameters.AddWithValue("fk", mCnt.Groups[2].Value);
                cmd.Parameters.AddWithValue("val", recordKey.Trim());
                data[col.ColumnCode] = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                continue;
            }

            var mSum = Regex.Match(f, @"^CROSS_SUM\(\s*([a-zA-Z0-9_]+)\s*,\s*([a-zA-Z0-9_]+)\s*,\s*([a-zA-Z0-9_]+)\s*\)$", RegexOptions.IgnoreCase);
            if (mSum.Success)
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT COALESCE(SUM((data ->> @sf)::numeric), 0)
                    FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL
                      AND LOWER(COALESCE(data ->> @fk, '')) = LOWER(@val)
                      AND COALESCE(data ->> @sf, '') ~ '^-?[0-9]+(\.[0-9]+)?$'
                    """, conn, tx);
                cmd.Parameters.AddWithValue("tbl", mSum.Groups[1].Value);
                cmd.Parameters.AddWithValue("fk", mSum.Groups[2].Value);
                cmd.Parameters.AddWithValue("sf", mSum.Groups[3].Value);
                cmd.Parameters.AddWithValue("val", recordKey.Trim());
                data[col.ColumnCode] = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
            }
        }
    }

    public static AppRecordDto ReadRecord(NpgsqlDataReader r)
    {
        var rawData = r.GetString(5);
        var rawErrs = r.GetString(7);
        return new AppRecordDto
        {
            Id = r.GetString(0),
            AppId = r.GetString(1),
            ModuleId = r.GetString(2),
            TableId = r.GetString(3),
            RecordKey = r.IsDBNull(4) ? null : r.GetString(4),
            Data = ParseJsonDict(rawData),
            Status = r.GetString(6),
            ValidationErrors = JsonSerializer.Deserialize<List<ValidationErrorItem>>(rawErrs,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(),
            CreatedBy = r.GetString(8),
            UpdatedBy = r.GetString(9),
            CreatedAt = r.GetDateTime(10),
            UpdatedAt = r.GetDateTime(11),
            ApprovalStatus = r.IsDBNull(12) ? "DRAFT" : r.GetString(12),
            ApprovedBy = r.IsDBNull(13) ? null : r.GetString(13),
            ApprovedAt = r.IsDBNull(14) ? null : r.GetDateTime(14),
            RejectionReason = r.IsDBNull(15) ? null : r.GetString(15),
            DeletedAt = r.IsDBNull(16) ? null : r.GetDateTime(16),
            DeletedBy = r.IsDBNull(17) ? null : r.GetString(17),
            WorkflowId = r.IsDBNull(18) ? null : r.GetString(18),
            CurrentStepOrder = r.IsDBNull(19) ? 0 : r.GetInt32(19),
            CurrentStepName = r.IsDBNull(20) ? null : r.GetString(20),
            WorkflowHistory = r.IsDBNull(21)
                ? new()
                : (JsonSerializer.Deserialize<List<WorkflowHistoryEntryDto>>(r.GetString(21),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()),
            Version = r.FieldCount > 22 && !r.IsDBNull(22) ? r.GetInt32(22) : 1,
            WorkflowSnapshot = r.FieldCount > 23 && !r.IsDBNull(23)
                ? JsonSerializer.Deserialize<List<WorkflowStepDto>>(r.GetString(23),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null
        };
    }

    public static Dictionary<string, object?> ParseJsonDict(string json)
    {
        var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject()) dict[prop.Name] = UnwrapJsonValue(prop.Value);
        return dict;
    }

    public static Dictionary<string, object?> NormalizeDict(Dictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in source) result[kv.Key] = UnwrapJsonValue(kv.Value);
        return result;
    }

    public static object? UnwrapJsonValue(object? val)
    {
        if (val is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.Array => el.EnumerateArray().Select(item => UnwrapJsonValue(item)).ToList(),
                JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => UnwrapJsonValue(p.Value)),
                _ => el.ToString()
            };
        }
        return val;
    }

    public static async Task WriteAuditAsync(
        NpgsqlConnection conn, string appId, string tableId, string recordId,
        string action, string actor, Dictionary<string, object?>? oldData, Dictionary<string, object?>? newData,
        string summary, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_audit_logs (id, app_id, table_id, record_id, action, actor, old_data, new_data, summary)
            VALUES (@id, @app, @tbl, @rec, @act, @actor, @old::jsonb, @new::jsonb, @sum)
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", $"aud_{Guid.NewGuid().ToString("N")[..12]}");
        cmd.Parameters.AddWithValue("app", appId);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.AddWithValue("rec", recordId);
        cmd.Parameters.AddWithValue("act", action);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.Add(new NpgsqlParameter("old", NpgsqlDbType.Jsonb) { Value = oldData != null ? JsonSerializer.Serialize(oldData) : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("new", NpgsqlDbType.Jsonb) { Value = newData != null ? JsonSerializer.Serialize(newData) : DBNull.Value });
        cmd.Parameters.AddWithValue("sum", summary);
        await cmd.ExecuteNonQueryAsync();
    }

    public static string ResolveAutoNumberPrefix(string? defaultValue)
    {
        var prefix = string.IsNullOrWhiteSpace(defaultValue) ? "REC-{YYYY}-" : defaultValue!;
        return prefix
            .Replace("{YYYY}", DateTime.UtcNow.ToString("yyyy"))
            .Replace("{MM}", DateTime.UtcNow.ToString("MM"));
    }

    public static async Task<string> NextAutoNumberAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string tableId,
        string columnCode,
        string? defaultValue)
    {
        var prefix = ResolveAutoNumberPrefix(defaultValue);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_auto_number_counters (table_id, column_code, scope_key, current_value, updated_at)
            VALUES (
                @tbl,
                @col,
                @scope,
                COALESCE((
                    SELECT MAX(SUBSTRING(data->>@col FROM LENGTH(@scope) + 1)::bigint)
                    FROM app_records
                    WHERE table_id = @tbl
                      AND (data->>@col) IS NOT NULL
                      AND LEFT(data->>@col, LENGTH(@scope)) = @scope
                      AND SUBSTRING(data->>@col FROM LENGTH(@scope) + 1) ~ '^[0-9]+$'
                ), 0) + 1,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT (table_id, column_code, scope_key) DO UPDATE SET
                current_value = sys_auto_number_counters.current_value + 1,
                updated_at = CURRENT_TIMESTAMP
            RETURNING current_value
            """, conn, tx);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.AddWithValue("col", columnCode);
        cmd.Parameters.AddWithValue("scope", prefix);
        var seq = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return $"{prefix}{seq:0000}";
    }

    public static async Task AdvanceAutoNumberCounterAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string tableId,
        string columnCode,
        string scopeKey,
        long minValue)
    {
        if (minValue <= 0) return;
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_auto_number_counters (table_id, column_code, scope_key, current_value, updated_at)
            VALUES (@tbl, @col, @scope, @val, CURRENT_TIMESTAMP)
            ON CONFLICT (table_id, column_code, scope_key) DO UPDATE SET
                current_value = GREATEST(sys_auto_number_counters.current_value, EXCLUDED.current_value),
                updated_at = CURRENT_TIMESTAMP
            """, conn, tx);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.AddWithValue("col", columnCode);
        cmd.Parameters.AddWithValue("scope", scopeKey);
        cmd.Parameters.AddWithValue("val", minValue);
        await cmd.ExecuteNonQueryAsync();
    }
}
