using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class DuplicateIdentityKeyException : Exception
{
    public DuplicateIdentityKeyException(string message) : base(message) { }
}

public class RecordLockedException : Exception
{
    public RecordLockedException(string message) : base(message) { }
}

public class RecordConflictException : Exception
{
    public RecordConflictException(string message) : base(message) { }
}

public partial class RecordService
{
    private readonly string _connStr;
    private readonly MetadataService _metadata;
    private readonly DynamicComputeService _compute;
    private readonly AutomationService _automations;
    private readonly NotificationService _notifications;
    private readonly RealtimeEventService _realtime;
    private readonly WorkflowService _workflows;
    private readonly RecordWriteService _writer;

    private const string SelectColumns = """
        id, app_id, module_id, table_id, record_key, data::text, status, validation_errors::text,
        created_by, updated_by, created_at, updated_at,
        COALESCE(approval_status, 'DRAFT'), approved_by, approved_at, rejection_reason, deleted_at, deleted_by,
        workflow_id, COALESCE(current_step_order, 0), current_step_name, COALESCE(workflow_history::text, '[]'),
        COALESCE(version, 1), workflow_snapshot::text
        """;

    public RecordService(
        IConfiguration config,
        MetadataService metadata,
        DynamicComputeService compute,
        AutomationService automations,
        NotificationService notifications,
        RealtimeEventService realtime,
        WorkflowService workflows,
        RecordWriteService writer)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _metadata = metadata;
        _compute = compute;
        _automations = automations;
        _notifications = notifications;
        _realtime = realtime;
        _workflows = workflows;
        _writer = writer;
    }

    public const string OwnerFilterSqlClause = """
        ((@owner IS NULL AND @ownerDept IS NULL)
         OR (@ownerDept IS NOT NULL AND (
                LOWER(created_by) = LOWER(COALESCE(@owner, ''))
             OR LOWER(COALESCE(data ->> 'phong_ban', '')) = LOWER(@ownerDept)
             OR LOWER(COALESCE(data ->> 'department', '')) = LOWER(@ownerDept)
             OR LOWER(COALESCE(data ->> 'bo_phan', '')) = LOWER(@ownerDept)
             OR (@ownerEmp IS NOT NULL AND (
                    LOWER(COALESCE(record_key, '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'ma_nv', '')) = LOWER(@ownerEmp)
             ))
             OR EXISTS (
                 SELECT 1 FROM sys_category_items ci
                 WHERE ci.category_code = 'DM_PHONG_BAN'
                   AND LOWER(ci.item_name) = LOWER(@ownerDept)
                   AND LOWER(COALESCE(app_records.data ->> 'phong_ban', '')) = LOWER(ci.item_code)
             )
             OR EXISTS (
                 SELECT 1 FROM sys_users su
                 WHERE LOWER(COALESCE(su.department, '')) = LOWER(@ownerDept)
                   AND (
                        LOWER(su.username) = LOWER(app_records.created_by)
                     OR (su.employee_code IS NOT NULL AND (
                            LOWER(COALESCE(app_records.data ->> 'ma_nv', '')) = LOWER(su.employee_code)
                         OR LOWER(COALESCE(app_records.data ->> 'nhan_vien', '')) = LOWER(su.employee_code)
                         OR LOWER(COALESCE(app_records.data ->> 'ho_ten_nv', '')) = LOWER(su.employee_code)
                         OR LOWER(COALESCE(app_records.data ->> 'nguoi_nhan', '')) = LOWER(su.employee_code)
                         OR LOWER(COALESCE(app_records.data ->> 'nguoi_tao', '')) = LOWER(su.employee_code)
                     ))
                   )
             )
         ))
         OR (@ownerDept IS NULL AND @owner IS NOT NULL AND (
                LOWER(created_by) = LOWER(@owner)
             OR (@ownerEmp IS NOT NULL AND (
                    LOWER(COALESCE(record_key, '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'ma_nv', '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'ho_ten_nv', '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'nhan_vien', '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'nguoi_nhan', '')) = LOWER(@ownerEmp)
                 OR LOWER(COALESCE(data ->> 'nguoi_tao', '')) = LOWER(@ownerEmp)
             ))
             OR (@ownerEmail IS NOT NULL AND LOWER(COALESCE(data ->> 'email', '')) = LOWER(@ownerEmail))
             OR LOWER(COALESCE(data ->> 'username', '')) = LOWER(@owner)
             OR LOWER(COALESCE(data ->> 'nguoi_tao', '')) = LOWER(@owner)
         )))
        """;

    public static void AddOwnerFilterParams(
        NpgsqlCommand cmd,
        string? ownerFilter,
        string? ownerEmail,
        string? ownerEmployeeCode,
        string? ownerDepartment = null)
    {
        cmd.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(ownerFilter) ? DBNull.Value : ownerFilter.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("ownerEmail", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(ownerEmail) ? DBNull.Value : ownerEmail.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("ownerEmp", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(ownerEmployeeCode) ? DBNull.Value : ownerEmployeeCode.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("ownerDept", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(ownerDepartment) ? DBNull.Value : ownerDepartment.Trim() });
    }

    public async Task<AppRecordDto?> GetRecordByIdAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        return await GetRecordByIdInternalAsync(conn, id);
    }

    public async Task<bool> IsRecordOwnedByUserAsync(string recordId, SysUserDto user, string effectiveRowScope = "OWNER_ONLY")
    {
        if (effectiveRowScope.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return true;
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            SELECT 1 FROM app_records
            WHERE id = @id AND deleted_at IS NULL AND {OwnerFilterSqlClause}
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", recordId);
        var dept = effectiveRowScope.Equals("DEPARTMENT", StringComparison.OrdinalIgnoreCase) ? user.Department : null;
        AddOwnerFilterParams(cmd, user.Username, user.Email, user.EmployeeCode, dept);
        return await cmd.ExecuteScalarAsync() != null;
    }

    public async Task<(List<AppRecordDto> Items, int Total)> QueryRecordsAsync(
        string? appId,
        string? moduleId,
        string? tableId,
        string? search,
        string? status,
        int page = 1,
        int limit = 20,
        string? ownerFilter = null,
        string? filtersJson = null,
        string? sortField = null,
        string? sortDir = "DESC",
        string? ownerEmail = null,
        string? ownerEmployeeCode = null,
        string? ownerDepartment = null,
        bool exportAll = false)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 500);
        var offset = (page - 1) * limit;

        var tableCols = !string.IsNullOrWhiteSpace(tableId)
            ? await _metadata.GetColumnsAsync(tableId)
            : new List<SysColumnDto>();

        var where = new System.Text.StringBuilder($"""
            WHERE deleted_at IS NULL
              AND (@appId IS NULL OR app_id = @appId)
              AND (@modId IS NULL OR module_id = @modId)
              AND (@tblId IS NULL OR table_id = @tblId)
              AND (@status IS NULL OR @status = 'ALL' OR status = @status)
              AND {OwnerFilterSqlClause}
              AND (@search IS NULL OR record_key ILIKE '%' || @search || '%' OR data::text ILIKE '%' || @search || '%')
            """);

        var dynamicParams = new List<(string Name, object Value)>();
        if (!string.IsNullOrWhiteSpace(filtersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(filtersJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("conditions", out var condsEl) &&
                    condsEl.ValueKind == JsonValueKind.Array)
                {
                    // FUN-06: Structured AND / OR query group
                    var logic = doc.RootElement.TryGetProperty("logic", out var lEl) &&
                                string.Equals(lEl.GetString(), "OR", StringComparison.OrdinalIgnoreCase)
                        ? " OR "
                        : " AND ";
                    var clauses = new List<string>();
                    int idx = 0;
                    foreach (var cond in condsEl.EnumerateArray())
                    {
                        var field = cond.TryGetProperty("field", out var fEl) ? fEl.GetString()?.Trim() : null;
                        var op = (cond.TryGetProperty("op", out var oEl) ? oEl.GetString() : "contains")?.Trim().ToLowerInvariant() ?? "contains";
                        var val = cond.TryGetProperty("value", out var vEl) ? vEl.ToString() : "";
                        var val2 = cond.TryGetProperty("value2", out var v2El) ? v2El.ToString() : "";
                        if (string.IsNullOrWhiteSpace(field) || !Regex.IsMatch(field, "^[a-zA-Z0-9_]+$")) continue;

                        if (field == "approvalStatus")
                        {
                            clauses.Add($"COALESCE(approval_status, 'DRAFT') = @fv_{idx}");
                            dynamicParams.Add(($"fv_{idx}", val.Trim().ToUpperInvariant()));
                        }
                        else if (op == "is_empty")
                        {
                            clauses.Add($"(data ->> @fk_{idx} IS NULL OR TRIM(data ->> @fk_{idx}) = '')");
                            dynamicParams.Add(($"fk_{idx}", field));
                        }
                        else if (op == "not_empty")
                        {
                            clauses.Add($"(data ->> @fk_{idx} IS NOT NULL AND TRIM(data ->> @fk_{idx}) <> '')");
                            dynamicParams.Add(($"fk_{idx}", field));
                        }
                        else if (op == "date_preset")
                        {
                            var today = DateTime.UtcNow.AddHours(7).Date;
                            var (start, end) = val.ToUpperInvariant() switch
                            {
                                "TODAY" => (today, today),
                                "THIS_WEEK" => (today.AddDays(-((int)today.DayOfWeek + 6) % 7), today.AddDays(6 - (((int)today.DayOfWeek + 6) % 7))),
                                "THIS_MONTH" => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
                                _ => (today, today)
                            };
                            clauses.Add($"LEFT(COALESCE(data ->> @fk_{idx}, ''), 10) BETWEEN @fv_{idx} AND @fv2_{idx}");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fv_{idx}", start.ToString("yyyy-MM-dd")));
                            dynamicParams.Add(($"fv2_{idx}", end.ToString("yyyy-MM-dd")));
                        }
                        else if (op is "gt" or "gte" or "lt" or "lte" && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                        {
                            var sqlOp = op switch { "gt" => ">", "gte" => ">=", "lt" => "<", _ => "<=" };
                            clauses.Add($"(COALESCE(data ->> @fk_{idx}, '') ~ '^-?[0-9]+(\\.[0-9]+)?$' AND (data ->> @fk_{idx})::numeric {sqlOp} @fn_{idx})");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fn_{idx}", numVal));
                        }
                        else if (op == "between" && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n1)
                            && decimal.TryParse(val2, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n2))
                        {
                            clauses.Add($"(COALESCE(data ->> @fk_{idx}, '') ~ '^-?[0-9]+(\\.[0-9]+)?$' AND (data ->> @fk_{idx})::numeric BETWEEN @fn1_{idx} AND @fn2_{idx})");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fn1_{idx}", n1));
                            dynamicParams.Add(($"fn2_{idx}", n2));
                        }
                        else if (op == "in")
                        {
                            var arr = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant()).ToArray();
                            if (arr.Length > 0)
                            {
                                clauses.Add($"LOWER(COALESCE(data ->> @fk_{idx}, '')) = ANY(@farr_{idx})");
                                dynamicParams.Add(($"fk_{idx}", field));
                                dynamicParams.Add(($"farr_{idx}", arr));
                            }
                        }
                        else if (op == "neq")
                        {
                            clauses.Add($"LOWER(COALESCE(data ->> @fk_{idx}, '')) <> LOWER(@fv_{idx})");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fv_{idx}", val.Trim()));
                        }
                        else if (op == "eq")
                        {
                            clauses.Add($"LOWER(COALESCE(data ->> @fk_{idx}, '')) = LOWER(@fv_{idx})");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fv_{idx}", val.Trim()));
                        }
                        else
                        {
                            clauses.Add($"COALESCE(data ->> @fk_{idx}, '') ILIKE '%' || @fv_{idx} || '%'");
                            dynamicParams.Add(($"fk_{idx}", field));
                            dynamicParams.Add(($"fv_{idx}", val.Trim()));
                        }
                        idx++;
                    }
                    if (clauses.Count > 0)
                        where.Append($" AND ({string.Join(logic, clauses)})");
                }
                else
                {
                    var filterDict = JsonSerializer.Deserialize<Dictionary<string, string>>(filtersJson);
                    if (filterDict != null)
                    {
                        int idx = 0;
                        foreach (var kv in filterDict)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                            if (!Regex.IsMatch(kv.Key, "^[a-zA-Z0-9_]+$")) continue;

                            if (kv.Key == "approvalStatus")
                            {
                                where.Append($" AND COALESCE(approval_status, 'DRAFT') = @fv_{idx}");
                                dynamicParams.Add(($"fv_{idx}", kv.Value.Trim().ToUpperInvariant()));
                            }
                            else
                            {
                                var col = tableCols.FirstOrDefault(c => c.ColumnCode.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                                var dt = col?.DataType.ToUpperInvariant() ?? "STRING";
                                var exact = dt is "SELECT" or "BOOLEAN" or "LOOKUP" || !string.IsNullOrWhiteSpace(col?.RefCategoryCode);
                                where.Append(exact
                                    ? $" AND LOWER(COALESCE(data ->> @fk_{idx}, '')) = LOWER(@fv_{idx})"
                                    : $" AND COALESCE(data ->> @fk_{idx}, '') ILIKE '%' || @fv_{idx} || '%'");
                                dynamicParams.Add(($"fk_{idx}", kv.Key));
                                dynamicParams.Add(($"fv_{idx}", kv.Value.Trim()));
                            }
                            idx++;
                        }
                    }
                }
            }
            catch { }
        }

        var countSql = $"SELECT COUNT(*) FROM app_records {where}";
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        AddFilterParams(countCmd, appId, moduleId, tableId, search, status, ownerFilter, ownerEmail, ownerEmployeeCode, ownerDepartment);
        foreach (var p in dynamicParams) countCmd.Parameters.AddWithValue(p.Name, p.Value);
        var total = exportAll ? 0 : Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        var dir = string.Equals(sortDir, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var orderBy = "ORDER BY updated_at DESC, created_at DESC";
        if (!string.IsNullOrWhiteSpace(sortField) && Regex.IsMatch(sortField, "^[a-zA-Z0-9_]+$"))
        {
            if (sortField is "created_at" or "updated_at")
            {
                orderBy = $"ORDER BY {sortField} {dir}";
            }
            else
            {
                var sortCol = tableCols.FirstOrDefault(c => c.ColumnCode.Equals(sortField, StringComparison.OrdinalIgnoreCase));
                if (sortCol != null)
                {
                    var isNum = sortCol.DataType.ToUpperInvariant() is "NUMBER" or "CURRENCY";
                    orderBy = isNum
                        ? $"ORDER BY (CASE WHEN COALESCE(data ->> @sf, '') ~ '^-?[0-9]+(\\.[0-9]+)?$' THEN (data ->> @sf)::numeric ELSE NULL END) {dir} NULLS LAST"
                        : $"ORDER BY COALESCE(data ->> @sf, '') {dir}";
                    dynamicParams.Add(("sf", sortCol.ColumnCode));
                }
            }
        }

        // Stable ties for both normal paging and exports; exports use one SELECT snapshot.
        orderBy += ", id ASC";
        var paginationSql = exportAll ? "" : "LIMIT @limit OFFSET @offset";
        var dataSql = $"""
            SELECT {SelectColumns}
            FROM app_records
            {where}
            {orderBy}
            {paginationSql}
            """;

        var items = new List<AppRecordDto>();
        await using var cmd = new NpgsqlCommand(dataSql, conn);
        AddFilterParams(cmd, appId, moduleId, tableId, search, status, ownerFilter, ownerEmail, ownerEmployeeCode, ownerDepartment);
        foreach (var p in dynamicParams) cmd.Parameters.AddWithValue(p.Name, p.Value);
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("offset", offset);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) items.Add(ReadRecord(r));

        if (!exportAll && items.Any(x => x.ApprovalStatus == "PENDING"))
        {
            var allWfs = await _workflows.GetWorkflowsAsync(appId);
            foreach (var item in items.Where(x => x.ApprovalStatus == "PENDING"))
            {
                var wf = !string.IsNullOrWhiteSpace(item.WorkflowId)
                    ? allWfs.FirstOrDefault(w => w.Id == item.WorkflowId)
                    : allWfs.FirstOrDefault(w => w.TableId == item.TableId && w.IsActive);
                if (wf != null && wf.Steps.Count > 0)
                {
                    item.TotalWorkflowSteps = wf.Steps.Count;
                    var step = wf.Steps.FirstOrDefault(s => s.StepOrder == item.CurrentStepOrder) ?? wf.Steps[0];
                    item.CurrentStepApproverRoles = step.ApproverRoles;
                    if (string.IsNullOrWhiteSpace(item.CurrentStepName))
                        item.CurrentStepName = step.Name;
                }
            }
        }
        return (items, exportAll ? items.Count : total);
    }

    public async Task<List<AppRecordDto>> GetTrashRecordsAsync(string? appId, string? tableId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = $"""
            SELECT {SelectColumns}
            FROM app_records
            WHERE deleted_at IS NOT NULL
              AND (@appId IS NULL OR app_id = @appId)
              AND (@tblId IS NULL OR table_id = @tblId)
            ORDER BY deleted_at DESC
            LIMIT 100
            """;
        var items = new List<AppRecordDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId });
        cmd.Parameters.Add(new NpgsqlParameter("tblId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(tableId) ? DBNull.Value : tableId });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) items.Add(ReadRecord(r));
        return items;
    }

    public async Task<(AppRecordDto Record, List<string> TriggeredAutomations)> UpsertRecordAsync(
        UpsertRecordRequest req,
        string actor = "system",
        bool isAdmin = false,
        IReadOnlySet<string>? serverManagedFields = null)
    {
        var (saved, isNew) = await _writer.UpsertCoreAsync(req, actor, isAdmin, serverManagedFields);
        var triggered = await _automations.ExecuteTriggersAsync(isNew ? "RECORD_CREATED" : "RECORD_UPDATED", saved);
        return (saved, triggered);
    }

    public async Task<(AppRecordDto Record, List<string> TriggeredAutomations)> UpsertRecordWithRelationsAsync(
        UpsertRecordWithRelationsRequest req,
        string actor = "system",
        bool isAdmin = false,
        IReadOnlySet<string>? serverManagedFields = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var (parentSaved, parentIsNew) = await _writer.UpsertCoreInTxAsync(
            conn, tx, req, actor, isAdmin, serverManagedFields);

        var childEvents = new List<(AppRecordDto Record, bool IsNew)>();
        var deletedTables = new List<(string TableId, string Id)>();

        foreach (var mut in req.ChildMutations)
        {
            if (string.IsNullOrWhiteSpace(mut.TableId) || string.IsNullOrWhiteSpace(mut.ViaColumnCode))
                continue;

            var matchValue = !string.IsNullOrWhiteSpace(mut.RefValueField)
                && parentSaved.Data.TryGetValue(mut.RefValueField, out var mv)
                && !string.IsNullOrWhiteSpace(mv?.ToString())
                ? mv!.ToString()!
                : (!string.IsNullOrWhiteSpace(parentSaved.RecordKey) ? parentSaved.RecordKey! : parentSaved.Id);

            foreach (var delId in mut.DeleteIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                await _writer.SoftDeleteInTxAsync(conn, tx, delId, mut.TableId, actor, isAdmin);
                deletedTables.Add((mut.TableId, delId));
            }

            foreach (var childReq in mut.Upserts)
            {
                childReq.AppId = mut.AppId;
                childReq.ModuleId = mut.ModuleId;
                childReq.TableId = mut.TableId;
                childReq.Data[mut.ViaColumnCode] = matchValue;

                var (childSaved, childIsNew) = await _writer.UpsertCoreInTxAsync(
                    conn, tx, childReq, actor, isAdmin);
                childEvents.Add((childSaved, childIsNew));
            }
        }

        await tx.CommitAsync();

        _realtime.Publish("record_changed", null, parentSaved.TableId, parentSaved.Id, "*",
            new { action = parentIsNew ? "CREATE" : "UPDATE" });
        foreach (var (childSaved, childIsNew) in childEvents)
        {
            _realtime.Publish("record_changed", null, childSaved.TableId, childSaved.Id, "*",
                new { action = childIsNew ? "CREATE" : "UPDATE" });
        }
        foreach (var (tblId, delId) in deletedTables)
        {
            _realtime.Publish("record_changed", null, tblId, delId, "*", new { action = "DELETE" });
        }

        var triggered = await _automations.ExecuteTriggersAsync(parentIsNew ? "RECORD_CREATED" : "RECORD_UPDATED", parentSaved);
        foreach (var (childSaved, childIsNew) in childEvents)
        {
            var childTriggered = await _automations.ExecuteTriggersAsync(childIsNew ? "RECORD_CREATED" : "RECORD_UPDATED", childSaved);
            triggered.AddRange(childTriggered);
        }

        return (parentSaved, triggered.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<(AppRecordDto? Record, List<string> TriggeredAutomations)> PatchCellAsync(
        string id,
        PatchCellRequest req,
        string actor = "system",
        bool isAdmin = false)
    {
        var saved = await _writer.PatchRecordCoreAsync(
            id,
            new Dictionary<string, object?> { [req.Field] = UnwrapJsonValue(req.Value) },
            actor,
            isAdmin,
            req.ExpectedVersion);
        if (saved == null) return (null, new List<string>());
        var triggered = await _automations.ExecuteTriggersAsync("RECORD_UPDATED", saved);
        return (saved, triggered);
    }

    public async Task<AppRecordDto?> SubmitApprovalAsync(string id, string actor)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var rec = await GetRecordByIdInternalAsync(conn, id);
        if (rec == null) return null;
        if (rec.ApprovalStatus is "PENDING" or "APPROVED" or "CANCELLED")
        {
            throw new RecordConflictException(rec.ApprovalStatus switch
            {
                "APPROVED" => "Hồ sơ đã được phê duyệt.",
                "CANCELLED" => "Hồ sơ đã bị hủy.",
                _ => "Hồ sơ đang ở trạng thái chờ duyệt."
            });
        }

        var table = (await _metadata.GetTablesAsync(rec.AppId, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var wf = await _workflows.ResolveWorkflowForRecordAsync(rec.TableId, rec.Data);
        var firstStep = wf?.Steps.OrderBy(s => s.StepOrder).FirstOrDefault();

        var field = table?.ApprovalField;
        if (!string.IsNullOrWhiteSpace(field) && rec.Data.ContainsKey(field))
        {
            rec.Data[field] = "CHO_DUYET";
        }
        if (table?.FormRules.Count > 0)
            await new FormRuleService(_metadata, _compute).PrepareWriteAsync(rec.TableId, new(), rec.Data);

        var history = new List<WorkflowHistoryEntryDto>(rec.WorkflowHistory)
        {
            new WorkflowHistoryEntryDto
            {
                StepOrder = 0,
                StepName = "Trình duyệt hồ sơ",
                Action = "SUBMIT",
                Actor = actor,
                Note = wf != null ? $"Áp dụng quy trình: {wf.Name}" : "Trình duyệt tiêu chuẩn",
                At = DateTime.UtcNow
            }
        };

        var stepOrder = firstStep?.StepOrder ?? 1;
        var stepName = firstStep?.Name ?? "Phê duyệt";
        var snapshotJson = wf?.Steps != null ? JsonSerializer.Serialize(wf.Steps) : null;

        await using var up = new NpgsqlCommand($"""
            UPDATE app_records
            SET approval_status = 'PENDING', rejection_reason = NULL, data = @data::jsonb,
                workflow_id = @wfid, current_step_order = @so, current_step_name = @sn,
                workflow_history = @wh::jsonb, workflow_snapshot = @wfs::jsonb,
                version = COALESCE(version, 1) + 1, updated_by = @actor, updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND deleted_at IS NULL
              AND COALESCE(approval_status, 'DRAFT') = @expectedStatus
              AND COALESCE(version, 1) = @expectedVer
            RETURNING {SelectColumns}
            """, conn);
        up.Parameters.AddWithValue("data", JsonSerializer.Serialize(rec.Data));
        up.Parameters.AddWithValue("wfid", (object?)wf?.Id ?? DBNull.Value);
        up.Parameters.AddWithValue("so", stepOrder);
        up.Parameters.AddWithValue("sn", stepName);
        up.Parameters.AddWithValue("wh", JsonSerializer.Serialize(history));
        up.Parameters.Add(new NpgsqlParameter("wfs", NpgsqlDbType.Jsonb) { Value = (object?)snapshotJson ?? DBNull.Value });
        up.Parameters.AddWithValue("actor", actor);
        up.Parameters.AddWithValue("id", id);
        up.Parameters.AddWithValue("expectedStatus", rec.ApprovalStatus);
        up.Parameters.AddWithValue("expectedVer", rec.Version);

        AppRecordDto updated;
        await using (var r = await up.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync())
            {
                throw new RecordConflictException("Hồ sơ đã được cập nhật hoặc trình duyệt ở phiên khác. Vui lòng tải lại.");
            }
            updated = ReadRecord(r);
        }

        if (wf != null)
        {
            updated.TotalWorkflowSteps = wf.Steps.Count;
            updated.CurrentStepApproverRoles = firstStep?.ApproverRoles ?? new();
        }

        await WriteAuditAsync(conn, updated.AppId, updated.TableId, updated.Id, "SUBMIT_APPROVAL", actor,
            rec.Data, updated.Data, $"Trình duyệt bản ghi {updated.RecordKey ?? updated.Id} ({stepName})");

        var app = await _metadata.GetAppByCodeOrIdAsync(updated.AppId);
        await _notifications.CreateAsync(
            $"Yêu cầu phê duyệt ({app?.Code ?? "APP"})",
            $"{actor} vừa trình duyệt '{updated.RecordKey ?? updated.Id}' — Bước {stepOrder}: {stepName}.",
            app?.Code,
            updated.Id,
            "*",
            "APPROVAL",
            app != null ? $"/apps/{app.Code}" : "/",
            actor,
            "HIGH");

        return updated;
    }

    public async Task<(string AppId, string TableId)?> GetRecordLocationAsync(string recordId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT app_id, table_id FROM app_records WHERE id = @id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", recordId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.GetString(1));
    }

    public async Task<List<string>> GetRecordsAppIdsAsync(IEnumerable<string> ids)
    {
        var idArr = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (idArr.Length == 0) return new List<string>();
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT DISTINCT app_id FROM app_records WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", idArr);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    public async Task<(AppRecordDto? Record, string? Error)> ProcessApprovalAsync(
        string id,
        SysUserDto actorUser,
        bool approve,
        string? reason,
        int? expectedVersion = null,
        int? expectedStepOrder = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var rec = await GetRecordByIdInternalAsync(conn, id);
        if (rec == null) return (null, "Không tìm thấy bản ghi.");
        if (!string.Equals(rec.ApprovalStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecordConflictException("Hồ sơ không ở trạng thái chờ duyệt (có thể đã được xử lý bởi người khác).");
        }
        if (expectedVersion.HasValue && expectedVersion.Value != rec.Version)
        {
            throw new RecordConflictException("Hồ sơ đã bị thay đổi trước khi phê duyệt. Vui lòng tải lại.");
        }
        if (expectedStepOrder.HasValue && expectedStepOrder.Value != rec.CurrentStepOrder)
        {
            throw new RecordConflictException("Bước phê duyệt hiện tại đã thay đổi. Vui lòng tải lại.");
        }

        var appMeta = await _metadata.GetAppByCodeOrIdAsync(rec.AppId);
        var appCode = appMeta?.Code;
        if (!AuthService.IsAdminUser(actorUser) && !AuthService.CanAccessApp(actorUser, appCode ?? ""))
        {
            return (null, $"Bạn không có quyền truy cập App {appCode}.");
        }

        var table = (await _metadata.GetTablesAsync(rec.AppId, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var wf = rec.WorkflowSnapshot is { Count: > 0 }
            ? null
            : await _workflows.ResolveWorkflowForRecordAsync(rec.TableId, rec.Data, rec.WorkflowId);
        var orderedSteps = (rec.WorkflowSnapshot is { Count: > 0 } ? rec.WorkflowSnapshot : wf?.Steps)
            ?.OrderBy(s => s.StepOrder).ToList() ?? new List<WorkflowStepDto>();
        var currentStep = orderedSteps.FirstOrDefault(s => s.StepOrder == rec.CurrentStepOrder)
                          ?? orderedSteps.FirstOrDefault();

        var fallbackRoles = table?.ApproverRoles ?? new List<string>();
        var isAuthorized = CanUserApproveStep(actorUser, appCode, currentStep, fallbackRoles);

        if (!isAuthorized)
        {
            var reqRoles = currentStep?.ApproverRoles.Count > 0 ? currentStep.ApproverRoles : fallbackRoles;
            return (null, $"Vai trò {actorUser.RoleCode} không có quyền phê duyệt bước hiện tại (yêu cầu: {string.Join(", ", reqRoles)}).");
        }

        var actor = actorUser.Username;
        var actorRole = actorUser.RoleCode;

        var stepName = currentStep?.Name ?? rec.CurrentStepName ?? "Phê duyệt";
        var stepOrder = currentStep?.StepOrder ?? Math.Max(1, rec.CurrentStepOrder);
        var nextStep = approve && orderedSteps.Count > 0
            ? orderedSteps.FirstOrDefault(s => s.StepOrder > stepOrder)
            : null;

        var history = new List<WorkflowHistoryEntryDto>(rec.WorkflowHistory)
        {
            new WorkflowHistoryEntryDto
            {
                StepOrder = stepOrder,
                StepName = stepName,
                Action = approve ? "APPROVE" : "REJECT",
                Actor = actor,
                ActorRole = actorRole,
                Note = reason,
                At = DateTime.UtcNow
            }
        };

        var isFinalApproval = approve && nextStep == null;
        var nextStatus = !approve ? "REJECTED" : (nextStep != null ? "PENDING" : "APPROVED");
        var nextStepOrder = !approve ? 0 : (nextStep != null ? nextStep.StepOrder : stepOrder);
        var nextStepName = !approve ? null : (nextStep != null ? nextStep.Name : stepName);

        var field = table?.ApprovalField;
        if (!string.IsNullOrWhiteSpace(field) && rec.Data.ContainsKey(field))
        {
            rec.Data[field] = !approve ? "TU_CHOI" : (nextStep != null ? "CHO_DUYET" : "DA_DUYET");
        }
        if (table?.FormRules.Count > 0)
            await new FormRuleService(_metadata, _compute).PrepareWriteAsync(rec.TableId, new(), rec.Data);

        await using var up = new NpgsqlCommand($"""
            UPDATE app_records
            SET approval_status = @st,
                approved_by = CASE WHEN @isFinal THEN @actor ELSE approved_by END,
                approved_at = CASE WHEN @isFinal THEN CURRENT_TIMESTAMP ELSE approved_at END,
                rejection_reason = @rsn,
                current_step_order = @nso,
                current_step_name = @nsn,
                workflow_history = @wh::jsonb,
                data = @data::jsonb,
                version = COALESCE(version, 1) + 1,
                updated_by = @actor,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND deleted_at IS NULL
              AND COALESCE(approval_status, 'DRAFT') = 'PENDING'
              AND COALESCE(current_step_order, 0) = @expectedStepOrder
              AND COALESCE(version, 1) = @expectedVer
            RETURNING {SelectColumns}
            """, conn);
        up.Parameters.AddWithValue("st", nextStatus);
        up.Parameters.AddWithValue("isFinal", isFinalApproval);
        up.Parameters.AddWithValue("actor", actor);
        up.Parameters.Add(new NpgsqlParameter("rsn", NpgsqlDbType.Text) { Value = approve || string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim() });
        up.Parameters.AddWithValue("nso", nextStepOrder);
        up.Parameters.AddWithValue("nsn", (object?)nextStepName ?? DBNull.Value);
        up.Parameters.AddWithValue("wh", JsonSerializer.Serialize(history));
        up.Parameters.AddWithValue("data", JsonSerializer.Serialize(rec.Data));
        up.Parameters.AddWithValue("id", id);
        up.Parameters.AddWithValue("expectedStepOrder", rec.CurrentStepOrder);
        up.Parameters.AddWithValue("expectedVer", rec.Version);

        AppRecordDto updated;
        await using (var r = await up.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync())
            {
                throw new RecordConflictException("Bước phê duyệt đã được xử lý bởi phiên khác. Vui lòng tải lại.");
            }
            updated = ReadRecord(r);
        }

        await WriteAuditAsync(conn, updated.AppId, updated.TableId, updated.Id,
            approve ? "APPROVE" : "REJECT", actor, rec.Data, updated.Data,
            !approve
                ? $"Từ chối {updated.RecordKey ?? updated.Id} tại {stepName}: {reason ?? "Không nêu lý do"}"
                : nextStep != null
                    ? $"Đã duyệt bước {stepOrder} ({stepName}) → Chuyển tiếp bước {nextStep.StepOrder} ({nextStep.Name})"
                    : $"Đã phê duyệt hoàn tất {updated.RecordKey ?? updated.Id}");

        var app = await _metadata.GetAppByCodeOrIdAsync(updated.AppId);
        if (!approve)
        {
            await _notifications.CreateAsync(
                $"Bị từ chối ({app?.Code})",
                $"Bản ghi '{updated.RecordKey ?? updated.Id}' bị {actor} từ chối tại {stepName}: {reason ?? "Vui lòng kiểm tra lại"}.",
                app?.Code,
                updated.Id,
                string.IsNullOrWhiteSpace(updated.CreatedBy) ? "*" : updated.CreatedBy,
                "APPROVAL",
                app != null ? $"/apps/{app.Code}" : "/",
                actor);
            await _automations.ExecuteTriggersAsync("RECORD_REJECTED", updated);
        }
        else if (nextStep != null)
        {
            await _notifications.CreateAsync(
                $"Chờ duyệt bước {nextStep.StepOrder}: {nextStep.Name} ({app?.Code})",
                $"{actor} đã duyệt bước {stepOrder}. Hồ sơ '{updated.RecordKey ?? updated.Id}' đang chờ duyệt bước tiếp theo ({string.Join(", ", nextStep.ApproverRoles)}).",
                app?.Code,
                updated.Id,
                "*",
                "APPROVAL",
                app != null ? $"/apps/{app.Code}" : "/",
                actor,
                "HIGH");
        }
        else
        {
            await _notifications.CreateAsync(
                $"Đã phê duyệt ({app?.Code})",
                $"Bản ghi '{updated.RecordKey ?? updated.Id}' đã hoàn tất phê duyệt bởi {actor}.",
                app?.Code,
                updated.Id,
                string.IsNullOrWhiteSpace(updated.CreatedBy) ? "*" : updated.CreatedBy,
                "APPROVAL",
                app != null ? $"/apps/{app.Code}" : "/",
                actor);
            await _automations.ExecuteTriggersAsync("RECORD_APPROVED", updated);
        }

        _realtime.Publish("record_changed", app?.Code, updated.TableId, updated.Id, "*", new { action = nextStatus });
        return (updated, null);
    }

    public async Task<(AppRecordDto? Record, string? Error)> RecallApprovalAsync(
        string id,
        SysUserDto actorUser,
        string? reason = null,
        int? expectedVersion = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var rec = await GetRecordByIdInternalAsync(conn, id);
        if (rec == null) return (null, "Không tìm thấy bản ghi.");
        if (!string.Equals(rec.ApprovalStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
            throw new RecordConflictException("Chỉ có thể thu hồi hồ sơ đang ở trạng thái chờ duyệt (PENDING).");
        if (expectedVersion.HasValue && expectedVersion.Value != rec.Version)
            throw new RecordConflictException("Hồ sơ đã bị thay đổi ở phiên khác. Vui lòng tải lại.");

        var isAdmin = AuthService.IsAdminUser(actorUser);
        var lastSubmitIdx = rec.WorkflowHistory.FindLastIndex(h => h.Action == "SUBMIT");
        var lastSubmitActor = lastSubmitIdx >= 0 ? rec.WorkflowHistory[lastSubmitIdx].Actor : rec.CreatedBy;
        if (!isAdmin && !string.Equals(actorUser.Username, lastSubmitActor, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(actorUser.Username, rec.CreatedBy, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Chỉ người trình duyệt, người tạo hoặc quản trị viên mới có quyền thu hồi.");
        }

        if (!isAdmin && lastSubmitIdx >= 0 && rec.WorkflowHistory.Skip(lastSubmitIdx + 1).Any(h => h.Action == "APPROVE"))
        {
            throw new RecordConflictException("Hồ sơ đã có bước được duyệt, không thể tự thu hồi.");
        }

        var table = (await _metadata.GetTablesAsync(rec.AppId, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var field = table?.ApprovalField;
        if (!string.IsNullOrWhiteSpace(field) && rec.Data.ContainsKey(field))
        {
            rec.Data[field] = "NHAP";
        }

        var history = new List<WorkflowHistoryEntryDto>(rec.WorkflowHistory)
        {
            new WorkflowHistoryEntryDto
            {
                StepOrder = 0,
                StepName = "Thu hồi trình duyệt",
                Action = "RECALL",
                Actor = actorUser.Username,
                ActorRole = actorUser.RoleCode,
                Note = string.IsNullOrWhiteSpace(reason) ? "Người gửi thu hồi hồ sơ" : reason.Trim(),
                At = DateTime.UtcNow
            }
        };

        await using var up = new NpgsqlCommand($"""
            UPDATE app_records
            SET approval_status = 'RECALLED',
                rejection_reason = @rsn,
                workflow_id = NULL,
                workflow_snapshot = NULL,
                current_step_order = 0,
                current_step_name = NULL,
                workflow_history = @wh::jsonb,
                data = @data::jsonb,
                version = COALESCE(version, 1) + 1,
                updated_by = @actor,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND deleted_at IS NULL
              AND COALESCE(approval_status, 'DRAFT') = 'PENDING'
              AND COALESCE(version, 1) = @expectedVer
            RETURNING {SelectColumns}
            """, conn);
        up.Parameters.Add(new NpgsqlParameter("rsn", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim() });
        up.Parameters.AddWithValue("wh", JsonSerializer.Serialize(history));
        up.Parameters.AddWithValue("data", JsonSerializer.Serialize(rec.Data));
        up.Parameters.AddWithValue("actor", actorUser.Username);
        up.Parameters.AddWithValue("id", id);
        up.Parameters.AddWithValue("expectedVer", rec.Version);

        AppRecordDto updated;
        await using (var r = await up.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync())
                throw new RecordConflictException("Hồ sơ đã thay đổi trạng thái trước khi thu hồi. Vui lòng tải lại.");
            updated = ReadRecord(r);
        }

        await WriteAuditAsync(conn, updated.AppId, updated.TableId, updated.Id, "RECALL_APPROVAL", actorUser.Username,
            rec.Data, updated.Data, $"Thu hồi trình duyệt bản ghi {updated.RecordKey ?? updated.Id}");
        var app = await _metadata.GetAppByCodeOrIdAsync(updated.AppId);
        _realtime.Publish("record_changed", app?.Code, updated.TableId, updated.Id, "*", new { action = "RECALLED" });
        return (updated, null);
    }

    public async Task<(AppRecordDto? Record, string? Error)> CancelApprovalAsync(
        string id,
        SysUserDto actorUser,
        string? reason = null,
        int? expectedVersion = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var rec = await GetRecordByIdInternalAsync(conn, id);
        if (rec == null) return (null, "Không tìm thấy bản ghi.");
        if (rec.ApprovalStatus is "APPROVED" or "CANCELLED")
            throw new RecordConflictException(rec.ApprovalStatus == "APPROVED"
                ? "Không thể hủy hồ sơ đã được phê duyệt."
                : "Hồ sơ đã ở trạng thái hủy.");
        if (expectedVersion.HasValue && expectedVersion.Value != rec.Version)
            throw new RecordConflictException("Hồ sơ đã bị thay đổi ở phiên khác. Vui lòng tải lại.");

        var isAdmin = AuthService.IsAdminUser(actorUser);
        if (!isAdmin && !string.Equals(actorUser.Username, rec.CreatedBy, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Chỉ người tạo hoặc quản trị viên mới có quyền hủy hồ sơ.");
        }

        var table = (await _metadata.GetTablesAsync(rec.AppId, null)).FirstOrDefault(t => t.Id == rec.TableId);
        var field = table?.ApprovalField;
        if (!string.IsNullOrWhiteSpace(field) && rec.Data.ContainsKey(field))
        {
            rec.Data[field] = "DA_HUY";
        }

        var cancelReason = string.IsNullOrWhiteSpace(reason) ? "Hủy hồ sơ" : reason.Trim();
        var history = new List<WorkflowHistoryEntryDto>(rec.WorkflowHistory)
        {
            new WorkflowHistoryEntryDto
            {
                StepOrder = 0,
                StepName = "Hủy hồ sơ",
                Action = "CANCEL",
                Actor = actorUser.Username,
                ActorRole = actorUser.RoleCode,
                Note = cancelReason,
                At = DateTime.UtcNow
            }
        };

        await using var up = new NpgsqlCommand($"""
            UPDATE app_records
            SET approval_status = 'CANCELLED',
                rejection_reason = @rsn,
                workflow_id = NULL,
                workflow_snapshot = NULL,
                current_step_order = 0,
                current_step_name = NULL,
                workflow_history = @wh::jsonb,
                data = @data::jsonb,
                version = COALESCE(version, 1) + 1,
                updated_by = @actor,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND deleted_at IS NULL
              AND COALESCE(approval_status, 'DRAFT') = @expectedStatus
              AND COALESCE(version, 1) = @expectedVer
            RETURNING {SelectColumns}
            """, conn);
        up.Parameters.AddWithValue("rsn", cancelReason);
        up.Parameters.AddWithValue("wh", JsonSerializer.Serialize(history));
        up.Parameters.AddWithValue("data", JsonSerializer.Serialize(rec.Data));
        up.Parameters.AddWithValue("actor", actorUser.Username);
        up.Parameters.AddWithValue("id", id);
        up.Parameters.AddWithValue("expectedStatus", rec.ApprovalStatus);
        up.Parameters.AddWithValue("expectedVer", rec.Version);

        AppRecordDto updated;
        await using (var r = await up.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync())
                throw new RecordConflictException("Hồ sơ đã thay đổi trạng thái trước khi hủy. Vui lòng tải lại.");
            updated = ReadRecord(r);
        }

        await WriteAuditAsync(conn, updated.AppId, updated.TableId, updated.Id, "CANCEL_APPROVAL", actorUser.Username,
            rec.Data, updated.Data, $"Hủy hồ sơ {updated.RecordKey ?? updated.Id}: {cancelReason}");
        var app = await _metadata.GetAppByCodeOrIdAsync(updated.AppId);
        _realtime.Publish("record_changed", app?.Code, updated.TableId, updated.Id, "*", new { action = "CANCELLED" });
        return (updated, null);
    }

    public async Task<int> DeleteRecordsAsync(List<string> ids, string actor = "system", bool isAdmin = false)
    {
        if (ids.Count == 0) return 0;
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        await using (var fetch = new NpgsqlCommand(
            "SELECT id, app_id, table_id, record_key, data::text, COALESCE(approval_status, 'DRAFT') FROM app_records WHERE id = ANY(@ids) AND deleted_at IS NULL", conn))
        {
            fetch.Parameters.AddWithValue("ids", ids.ToArray());
            var rows = new List<(string Id, string AppId, string TableId, string? Key, string Data, string ApprovalStatus)>();
            await using (var r = await fetch.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5)));
                }
            }
            if (!isAdmin && rows.Any(x => x.ApprovalStatus == "APPROVED"))
            {
                throw new RecordLockedException("Không thể xóa bản ghi đã được phê duyệt (APPROVED).");
            }
            var idArray = ids.ToArray();
            foreach (var tblGroup in rows.GroupBy(x => x.TableId))
            {
                await using var refColsCmd = new NpgsqlCommand("""
                    SELECT c.table_id, t.display_name, c.column_code, c.column_name, c.ref_value_field
                    FROM sys_columns c
                    JOIN sys_tables t ON t.id = c.table_id
                    WHERE c.ref_table_id = @tblId
                    """, conn);
                refColsCmd.Parameters.AddWithValue("tblId", tblGroup.Key);
                var refCols = new List<(string TableId, string TableName, string ColCode, string ColName, string? RefValField)>();
                await using (var rr = await refColsCmd.ExecuteReaderAsync())
                {
                    while (await rr.ReadAsync())
                        refCols.Add((rr.GetString(0), rr.GetString(1), rr.GetString(2), rr.GetString(3), rr.IsDBNull(4) ? null : rr.GetString(4)));
                }
                foreach (var rc in refCols)
                {
                    var candidateValues = new List<string>();
                    foreach (var row in tblGroup)
                    {
                        candidateValues.Add(row.Id);
                        if (!string.IsNullOrWhiteSpace(row.Key)) candidateValues.Add(row.Key!);
                        if (!string.IsNullOrWhiteSpace(rc.RefValField))
                        {
                            var parsed = ParseJsonDict(row.Data);
                            if (parsed.TryGetValue(rc.RefValField!, out var rv) && rv != null && !string.IsNullOrWhiteSpace(rv.ToString()))
                                candidateValues.Add(rv.ToString()!);
                        }
                    }
                    var distinctVals = candidateValues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (distinctVals.Length == 0) continue;
                    await using var usedCmd = new NpgsqlCommand("""
                        SELECT 1 FROM app_records
                        WHERE table_id = @refTbl AND deleted_at IS NULL
                          AND NOT (id = ANY(@deletingIds))
                          AND (data ->> @colCode) = ANY(@vals)
                        LIMIT 1
                        """, conn);
                    usedCmd.Parameters.AddWithValue("refTbl", rc.TableId);
                    usedCmd.Parameters.AddWithValue("deletingIds", idArray);
                    usedCmd.Parameters.AddWithValue("colCode", rc.ColCode);
                    usedCmd.Parameters.AddWithValue("vals", distinctVals);
                    if (await usedCmd.ExecuteScalarAsync() != null)
                    {
                        throw new RecordLockedException($"Không thể xóa bản ghi vì đang được tham chiếu tại '{rc.TableName} ({rc.ColName})'.");
                    }
                }
            }
            foreach (var row in rows)
            {
                await WriteAuditAsync(conn, row.AppId, row.TableId, row.Id, "SOFT_DELETE", actor,
                    ParseJsonDict(row.Data), null, $"Chuyển vào thùng rác: {row.Key ?? row.Id}");
            }
        }

        await using var cmd = new NpgsqlCommand(
            "UPDATE app_records SET deleted_at = CURRENT_TIMESTAMP, deleted_by = @actor WHERE id = ANY(@ids) AND deleted_at IS NULL", conn);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        var count = await cmd.ExecuteNonQueryAsync();
        if (count > 0) _realtime.Publish("record_changed", null, null, ids.FirstOrDefault(), "*", new { action = "DELETE" });
        return count;
    }

    public async Task<bool> RestoreRecordAsync(string id, string actor)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE app_records
            SET deleted_at = NULL, deleted_by = NULL, updated_by = @actor, updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND deleted_at IS NOT NULL
            RETURNING app_id, table_id, record_key
            """, conn);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return false;
        var appId = r.GetString(0);
        var tblId = r.GetString(1);
        var key = r.IsDBNull(2) ? id : r.GetString(2);
        await r.CloseAsync();
        await WriteAuditAsync(conn, appId, tblId, id, "RESTORE", actor, null, null, $"Khôi phục bản ghi {key} từ thùng rác");
        _realtime.Publish("record_changed", null, tblId, id, "*", new { action = "RESTORE" });
        return true;
    }

    public async Task<List<PendingApprovalItemDto>> GetPendingApprovalsAsync(SysUserDto user)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var allWfs = await _workflows.GetWorkflowsAsync();
        var sql = """
            SELECT r.id, r.record_key, r.app_id, ap.code, ap.name, ap.color,
                   r.table_id, t.display_name, r.created_by, r.created_at,
                   r.data::text, COALESCE(t.approver_roles::text, '["ADMIN","HR_MANAGER","ASSET_LEAD"]'),
                   r.workflow_id, COALESCE(r.current_step_order, 1), r.current_step_name
            FROM app_records r
            JOIN sys_apps ap ON ap.id = r.app_id
            JOIN sys_tables t ON t.id = r.table_id
            WHERE r.deleted_at IS NULL AND r.approval_status = 'PENDING'
            ORDER BY r.updated_at DESC
            LIMIT 40
            """;

        var list = new List<PendingApprovalItemDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var appCode = r.GetString(3);
            if (!AuthService.CanAccessApp(user, appCode)) continue;

            var tblId = r.GetString(6);
            var wfId = r.IsDBNull(12) ? null : r.GetString(12);
            var stepOrder = r.GetInt32(13);
            var stepName = r.IsDBNull(14) ? null : r.GetString(14);
            var wf = !string.IsNullOrWhiteSpace(wfId)
                ? allWfs.FirstOrDefault(w => w.Id == wfId)
                : allWfs.FirstOrDefault(w => w.TableId == tblId && w.IsActive);
            var activeStep = wf?.Steps.FirstOrDefault(s => s.StepOrder == stepOrder) ?? wf?.Steps.FirstOrDefault();

            var fallbackRoles = JsonSerializer.Deserialize<List<string>>(r.GetString(11)) ?? new();
            var canApprove = CanUserApproveStep(user, appCode, activeStep, fallbackRoles);
            if (!canApprove) continue;

            var dict = ParseJsonDict(r.GetString(10));
            var previewParts = dict.Values
                .Where(v => v != null && v is not System.Collections.IEnumerable or string)
                .Select(v => v!.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(3);

            list.Add(new PendingApprovalItemDto
            {
                RecordId = r.GetString(0),
                RecordKey = r.IsDBNull(1) ? null : r.GetString(1),
                AppId = r.GetString(2),
                AppCode = appCode,
                AppName = r.GetString(4),
                AppColor = r.IsDBNull(5) ? "#1a73e8" : r.GetString(5),
                TableId = tblId,
                TableDisplayName = r.GetString(7),
                CreatedBy = r.IsDBNull(8) ? "system" : r.GetString(8),
                CreatedAt = r.GetDateTime(9),
                SummaryText = string.Join(" • ", previewParts),
                CurrentStepOrder = activeStep?.StepOrder ?? stepOrder,
                CurrentStepName = stepName ?? activeStep?.Name ?? "Phê duyệt",
                TotalSteps = wf?.Steps.Count > 0 ? wf.Steps.Count : 1
            });
        }
        return list;
    }

    private static bool CanUserApproveStep(
        SysUserDto user,
        string? appCode,
        WorkflowStepDto? activeStep,
        List<string> fallbackRoles)
    {
        if (AuthService.IsAdminUser(user)) return true;
        var stepRoles = activeStep?.ApproverRoles ?? new List<string>();
        var stepUsers = activeStep?.ApproverUsers ?? new List<string>();
        if (stepUsers.Contains(user.Username, StringComparer.OrdinalIgnoreCase)) return true;
        if (stepRoles.Count > 0) return AuthService.HasAnyRole(user, stepRoles);
        return AuthService.HasAppPermission(user, appCode, "record.approve")
            && (fallbackRoles.Count == 0 || AuthService.HasAnyRole(user, fallbackRoles));
    }

    public async Task<List<RecordCommentDto>> GetCommentsAsync(string recordId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT id, app_id, table_id, record_id, author, content, mentions::text, created_at
            FROM app_record_comments
            WHERE record_id = @rid
            ORDER BY created_at ASC
            LIMIT 100
            """;
        var list = new List<RecordCommentDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("rid", recordId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new RecordCommentDto
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                TableId = r.GetString(2),
                RecordId = r.GetString(3),
                Author = r.GetString(4),
                Content = r.GetString(5),
                Mentions = JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new(),
                CreatedAt = r.GetDateTime(7)
            });
        }
        return list;
    }

    public async Task<RecordCommentDto?> AddCommentAsync(string recordId, string author, string content)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var rec = await GetRecordByIdInternalAsync(conn, recordId);
        if (rec == null) return null;

        var mentions = Regex.Matches(content, @"@([a-zA-Z0-9_.-]+)")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        var id = $"cmt_{Guid.NewGuid().ToString("N")[..10]}";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_record_comments (id, app_id, table_id, record_id, author, content, mentions)
            VALUES (@id, @app, @tbl, @rec, @aut, @cnt, @men::jsonb)
            RETURNING created_at
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", rec.AppId);
        cmd.Parameters.AddWithValue("tbl", rec.TableId);
        cmd.Parameters.AddWithValue("rec", recordId);
        cmd.Parameters.AddWithValue("aut", author);
        cmd.Parameters.AddWithValue("cnt", content.Trim());
        cmd.Parameters.AddWithValue("men", JsonSerializer.Serialize(mentions));
        var createdAt = Convert.ToDateTime(await cmd.ExecuteScalarAsync());

        var app = await _metadata.GetAppByCodeOrIdAsync(rec.AppId);
        foreach (var targetUser in mentions)
        {
            await _notifications.CreateAsync(
                $"{author} đã nhắc tới bạn ({app?.Code ?? "APP"})",
                $"Trên bản ghi '{rec.RecordKey ?? rec.Id}': {content.Trim()}",
                app?.Code,
                rec.Id,
                targetUser,
                "MENTION",
                app != null ? $"/apps/{app.Code}" : "/",
                author);
        }
        _realtime.Publish("comment_added", app?.Code, rec.TableId, rec.Id, "*", new { id, author });

        return new RecordCommentDto
        {
            Id = id,
            AppId = rec.AppId,
            TableId = rec.TableId,
            RecordId = recordId,
            Author = author,
            Content = content.Trim(),
            Mentions = mentions,
            CreatedAt = createdAt
        };
    }

    public async Task<bool> PermanentDeleteAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM app_records WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(int Total, int ValidCount, int ErrorCount)> ValidateTableRecordsAsync(string tableId)
    {
        var columns = await _metadata.GetColumnsAsync(tableId);
        var categorySets = await LoadCategorySetsAsync(columns);
        var (items, _) = await QueryRecordsAsync(null, null, tableId, null, "ALL", 1, 500);
        if (items.Count == 0) return (0, 0, 0);

        var inputs = items.Select(i => (i.Id, i.Data)).ToList();
        var rules = (await _metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == tableId)?.FormRules ?? new();
        FormRuleService.ValidateDefinition(rules, columns);
        var results = _compute.EvaluateAndValidate(columns, categorySets, inputs, rules);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        int validCount = 0;
        int errorCount = 0;
        foreach (var res in results)
        {
            var hasErr = res.Errors.Any(e => e.Severity == "ERROR");
            if (hasErr) errorCount++; else validCount++;
            var status = hasErr ? "ERROR" : "ACTIVE";

            await using var cmd = new NpgsqlCommand("""
                UPDATE app_records
                SET data = @data::jsonb, status = @st, validation_errors = @errs::jsonb, updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                """, conn);
            cmd.Parameters.AddWithValue("id", res.Id);
            cmd.Parameters.AddWithValue("data", JsonSerializer.Serialize(res.EvaluatedData));
            cmd.Parameters.AddWithValue("st", status);
            cmd.Parameters.AddWithValue("errs", JsonSerializer.Serialize(res.Errors));
            await cmd.ExecuteNonQueryAsync();
        }

        return (results.Count, validCount, errorCount);
    }

    public async Task<List<LookupOptionDto>> GetLookupOptionsAsync(
        string tableId,
        string? valueField,
        string? displayField,
        string? search = null,
        SysUserDto? user = null,
        string? filterField = null,
        string? filterValue = null)
    {
        var cols = await _metadata.GetColumnsAsync(tableId);
        var visibleCols = cols;
        if (user != null && !AuthService.IsAdminUser(user))
        {
            var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
            visibleCols = cols
                .Where(c => !c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }

        var valKey = !string.IsNullOrWhiteSpace(valueField)
            ? valueField!
            : (visibleCols.FirstOrDefault(c => c.IsIdentityKey)?.ColumnCode ?? visibleCols.FirstOrDefault()?.ColumnCode ?? "id");
        var dispKey = !string.IsNullOrWhiteSpace(displayField)
            ? displayField!
            : (visibleCols.Skip(1).FirstOrDefault()?.ColumnCode ?? valKey);

        string? filtersJson = null;
        if (!string.IsNullOrWhiteSpace(filterField) && !string.IsNullOrWhiteSpace(filterValue))
        {
            filtersJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [filterField.Trim()] = filterValue.Trim()
            });
        }
        var (items, _) = await QueryRecordsAsync(null, null, tableId, search, "ALL", 1, 200, filtersJson: filtersJson);
        var built = items.Select(rec => BuildLookupOption(rec, valKey, dispKey, visibleCols)).ToList();
        if (!string.IsNullOrWhiteSpace(search) && user != null && !AuthService.IsAdminUser(user))
        {
            var term = search.Trim();
            built = built
                .Where(o => o.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || o.Value.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || (!string.IsNullOrWhiteSpace(o.SearchText) && o.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        return built;
    }

    public static LookupOptionDto BuildLookupOption(
        AppRecordDto rec,
        string valKey,
        string dispKey,
        IReadOnlyList<SysColumnDto> visibleCols)
    {
        var v = rec.Data.TryGetValue(valKey, out var rv) ? rv?.ToString() ?? rec.Id : rec.Id;
        var d = rec.Data.TryGetValue(dispKey, out var rd) ? rd?.ToString() ?? v : v;

        var safeData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var searchParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rec.RecordKey)) searchParts.Add(rec.RecordKey!);

        var summaryFields = new List<LookupSummaryFieldDto>();
        foreach (var col in visibleCols)
        {
            if (!rec.Data.TryGetValue(col.ColumnCode, out var raw) || raw == null) continue;
            var str = raw.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(str)) continue;

            safeData[col.ColumnCode] = raw;

            var dt = (col.DataType ?? "STRING").ToUpperInvariant();
            if (dt is "FILE" or "IMAGE" or "SUB_TABLE") continue;

            searchParts.Add(str);

            if (summaryFields.Count < 8)
            {
                summaryFields.Add(new LookupSummaryFieldDto
                {
                    Code = col.ColumnCode,
                    Label = col.ColumnName,
                    Value = dt == "BOOLEAN"
                        ? (str.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Có / Đang hoạt động" : "Không")
                        : str
                });
            }
        }

        // Build compact multi-field subLabel (phone / CCCD / email / department) for instant dropdown scan
        var priorityHints = new List<string>();
        foreach (var sf in summaryFields)
        {
            if (sf.Code.Equals(valKey, StringComparison.OrdinalIgnoreCase) ||
                sf.Code.Equals(dispKey, StringComparison.OrdinalIgnoreCase)) continue;
            var lc = sf.Code.ToLowerInvariant();
            var isKeyContact = lc.Contains("dien_thoai") || lc.Contains("sdt") || lc.Contains("phone")
                || lc.Contains("cccd") || lc.Contains("cmnd") || lc.Contains("email")
                || lc.Contains("phong_ban") || lc.Contains("chuc_vu");
            if (isKeyContact)
            {
                priorityHints.Add(sf.Value);
                if (priorityHints.Count >= 3) break;
            }
        }
        var subLabel = priorityHints.Count > 0 ? string.Join(" • ", priorityHints) : d;

        return new LookupOptionDto
        {
            RecordId = rec.Id,
            Value = v,
            Label = v == d ? v : $"{v} — {d}",
            SubLabel = subLabel,
            SearchText = string.Join(" | ", searchParts.Distinct()),
            Data = safeData,
            SummaryFields = summaryFields
        };
    }

    public async Task<List<RelatedTableGroupDto>> GetCrossAppRelationsAsync(string recordId, SysUserDto? user = null, bool forParentForm = false)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var source = await GetRecordByIdInternalAsync(conn, recordId);
        if (source == null) return new();

        var linkCols = new List<(string AppId, string AppCode, string AppName, string AppColor, string ModuleId, string TableId, string TableDisplay, string RowScope, string ColCode, string ColName, string ValueField)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.code, a.name, a.color, t.module_id, t.id, t.display_name,
                   COALESCE(t.row_scope, 'ALL'), c.column_code, c.column_name, COALESCE(c.ref_value_field, '')
            FROM sys_columns c
            JOIN sys_tables t ON t.id = c.table_id
            JOIN sys_apps a ON a.id = t.app_id
            WHERE c.ref_table_id = @srcTbl AND c.is_active = TRUE AND t.is_active = TRUE AND a.is_active = TRUE
              AND (@forParentForm = FALSE OR COALESCE(c.show_in_parent_form, FALSE) = TRUE)
            ORDER BY (CASE WHEN a.id = @srcApp THEN 0 ELSE 1 END), a.order_index, t.order_index
            """, conn))
        {
            cmd.Parameters.AddWithValue("srcTbl", source.TableId);
            cmd.Parameters.AddWithValue("srcApp", source.AppId);
            cmd.Parameters.AddWithValue("forParentForm", forParentForm);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                linkCols.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                              r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                              r.GetString(8), r.GetString(9), r.GetString(10)));
            }
        }

        var userRoles = user != null
            ? (user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode })
            : new List<string>();
        var isAdmin = user == null || AuthService.IsAdminUser(user);

        var groups = new List<RelatedTableGroupDto>();
        foreach (var link in linkCols)
        {
            if (user != null)
            {
                if (!AuthService.CanAccessApp(user, link.AppCode)) continue;
                if (!AuthService.CanAccessTable(user, link.AppCode, link.TableId, "view")) continue;
            }

            var matchValue = !string.IsNullOrWhiteSpace(link.ValueField) && source.Data.TryGetValue(link.ValueField, out var v)
                ? v?.ToString()
                : source.RecordKey;
            if (string.IsNullOrWhiteSpace(matchValue)) continue;

            var matchedRecords = new List<AppRecordDto>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT {SelectColumns}
                FROM app_records
                WHERE table_id = @tbl AND deleted_at IS NULL AND data ->> @col = @val
                ORDER BY updated_at DESC
                """, conn))
            {
                cmd.Parameters.AddWithValue("tbl", link.TableId);
                cmd.Parameters.AddWithValue("col", link.ColCode);
                cmd.Parameters.AddWithValue("val", matchValue);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) matchedRecords.Add(ReadRecord(r));
            }

            if (user != null && !isAdmin)
            {
                var relScope = AuthService.ResolveRowScope(user, link.AppCode, link.TableId, link.RowScope, false);
                if (relScope == "OWNER_ONLY")
                {
                    var emp = user.EmployeeCode?.Trim();
                    var uname = user.Username.Trim();
                    var em = user.Email?.Trim();
                    matchedRecords = matchedRecords.Where(mr =>
                    {
                        if (mr.CreatedBy.Equals(uname, StringComparison.OrdinalIgnoreCase)) return true;
                        foreach (var dv in mr.Data.Values)
                        {
                            var s = dv?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(s)) continue;
                            if (s.Equals(uname, StringComparison.OrdinalIgnoreCase)) return true;
                            if (!string.IsNullOrWhiteSpace(emp) && s.Equals(emp, StringComparison.OrdinalIgnoreCase)) return true;
                            if (!string.IsNullOrWhiteSpace(em) && s.Equals(em, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        return false;
                    }).ToList();
                }
                else if (relScope == "DEPARTMENT")
                {
                    var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var mr in matchedRecords)
                    {
                        if (await IsRecordOwnedByUserAsync(mr.Id, user, "DEPARTMENT")) allowedIds.Add(mr.Id);
                    }
                    matchedRecords = matchedRecords.Where(mr => allowedIds.Contains(mr.Id)).ToList();
                }
            }

            var cols = await _metadata.GetColumnsAsync(link.TableId);
            if (user != null && !isAdmin)
            {
                var hiddenCodes = cols
                    .Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                    .Select(c => c.ColumnCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (hiddenCodes.Count > 0)
                {
                    cols = cols.Where(c => !hiddenCodes.Contains(c.ColumnCode)).ToList();
                    foreach (var mr in matchedRecords)
                    {
                        foreach (var hc in hiddenCodes) mr.Data.Remove(hc);
                    }
                }
            }

            groups.Add(new RelatedTableGroupDto
            {
                AppId = link.AppId,
                AppCode = link.AppCode,
                AppName = link.AppName,
                AppColor = link.AppColor,
                ModuleId = link.ModuleId,
                TableId = link.TableId,
                TableDisplayName = link.TableDisplay,
                ViaColumnCode = link.ColCode,
                ViaColumnName = link.ColName,
                RefValueField = link.ValueField,
                TargetMatchValue = matchValue,
                CanCreate = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "create"),
                CanEdit = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "edit"),
                CanDelete = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "delete"),
                Columns = cols,
                Records = matchedRecords
            });
        }

        return groups;
    }

    public async Task<List<RelatedTableGroupDto>> GetTableChildRelationsAsync(string tableId, string? recordId = null, SysUserDto? user = null, bool forParentForm = true)
    {
        if (!string.IsNullOrWhiteSpace(recordId))
        {
            return await GetCrossAppRelationsAsync(recordId, user, forParentForm);
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? srcAppId = null;
        await using (var tblCmd = new NpgsqlCommand("SELECT app_id FROM sys_tables WHERE id = @tbl AND is_active = TRUE", conn))
        {
            tblCmd.Parameters.AddWithValue("tbl", tableId);
            var obj = await tblCmd.ExecuteScalarAsync();
            srcAppId = obj?.ToString();
        }
        if (string.IsNullOrWhiteSpace(srcAppId)) return new();

        var linkCols = new List<(string AppId, string AppCode, string AppName, string AppColor, string ModuleId, string TableId, string TableDisplay, string ColCode, string ColName, string ValueField)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.code, a.name, a.color, t.module_id, t.id, t.display_name,
                   c.column_code, c.column_name, COALESCE(c.ref_value_field, '')
            FROM sys_columns c
            JOIN sys_tables t ON t.id = c.table_id
            JOIN sys_apps a ON a.id = t.app_id
            WHERE c.ref_table_id = @srcTbl AND c.is_active = TRUE AND t.is_active = TRUE AND a.is_active = TRUE
              AND (@forParentForm = FALSE OR COALESCE(c.show_in_parent_form, FALSE) = TRUE)
            ORDER BY (CASE WHEN a.id = @srcApp THEN 0 ELSE 1 END), a.order_index, t.order_index
            """, conn))
        {
            cmd.Parameters.AddWithValue("srcTbl", tableId);
            cmd.Parameters.AddWithValue("srcApp", srcAppId);
            cmd.Parameters.AddWithValue("forParentForm", forParentForm);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                linkCols.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                              r.GetString(4), r.GetString(5), r.GetString(6),
                              r.GetString(7), r.GetString(8), r.GetString(9)));
            }
        }

        var userRoles = user != null
            ? (user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode })
            : new List<string>();
        var isAdmin = user == null || AuthService.IsAdminUser(user);

        var groups = new List<RelatedTableGroupDto>();
        foreach (var link in linkCols)
        {
            if (user != null)
            {
                if (!AuthService.CanAccessApp(user, link.AppCode)) continue;
                if (!AuthService.CanAccessTable(user, link.AppCode, link.TableId, "view")) continue;
            }

            var cols = await _metadata.GetColumnsAsync(link.TableId);
            if (user != null && !isAdmin)
            {
                var hiddenCodes = cols
                    .Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                    .Select(c => c.ColumnCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (hiddenCodes.Count > 0)
                {
                    cols = cols.Where(c => !hiddenCodes.Contains(c.ColumnCode)).ToList();
                }
            }

            groups.Add(new RelatedTableGroupDto
            {
                AppId = link.AppId,
                AppCode = link.AppCode,
                AppName = link.AppName,
                AppColor = link.AppColor,
                ModuleId = link.ModuleId,
                TableId = link.TableId,
                TableDisplayName = link.TableDisplay,
                ViaColumnCode = link.ColCode,
                ViaColumnName = link.ColName,
                RefValueField = link.ValueField,
                TargetMatchValue = string.Empty,
                CanCreate = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "create"),
                CanEdit = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "edit"),
                CanDelete = user == null || AuthService.CanAccessTable(user, link.AppCode, link.TableId, "delete"),
                Columns = cols,
                Records = new List<AppRecordDto>()
            });
        }

        return groups;
    }

    public async Task<List<AppAuditLogDto>> GetAuditLogsAsync(string recordId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT id, app_id, table_id, record_id, action, actor, old_data::text, new_data::text, summary, created_at
            FROM app_audit_logs
            WHERE record_id = @id
            ORDER BY created_at DESC
            LIMIT 50
            """;
        var list = new List<AppAuditLogDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", recordId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadAuditRow(r));
        return list;
    }

    public async Task<List<AppAuditLogDto>> QueryGlobalAuditLogsAsync(
        string? appId,
        string? action,
        string? actor,
        int limit = 80,
        string? tableId = null,
        string? search = null,
        string? exactActor = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT id, app_id, table_id, record_id, action, actor, old_data::text, new_data::text, summary, created_at
            FROM app_audit_logs
            WHERE (@appId IS NULL OR app_id = @appId)
              AND (@tblId IS NULL OR table_id = @tblId)
              AND (@act IS NULL OR action = @act)
              AND (@actor IS NULL OR LOWER(actor) LIKE '%' || LOWER(@actor) || '%')
              AND (@exactActor IS NULL OR LOWER(actor) = LOWER(@exactActor))
              AND (@q IS NULL
                   OR LOWER(COALESCE(summary, '')) LIKE '%' || LOWER(@q) || '%'
                   OR LOWER(record_id) LIKE '%' || LOWER(@q) || '%'
                   OR LOWER(COALESCE(actor, '')) LIKE '%' || LOWER(@q) || '%')
            ORDER BY created_at DESC
            LIMIT @lim
            """;
        var list = new List<AppAuditLogDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("tblId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(tableId) ? DBNull.Value : tableId.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("act", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(action) ? DBNull.Value : action.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(actor) ? DBNull.Value : actor.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("exactActor", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(exactActor) ? DBNull.Value : exactActor.Trim() });
        cmd.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim() });
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 300));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadAuditRow(r));
        return list;
    }

    public async Task<TableAnalyticsDto> GetTableAnalyticsAsync(string tableId)
    {
        var columns = await _metadata.GetColumnsAsync(tableId);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var dto = new TableAnalyticsDto { TableId = tableId };
        await using (var cntCmd = new NpgsqlCommand("""
            SELECT COUNT(*),
                   COUNT(*) FILTER (WHERE status <> 'ERROR'),
                   COUNT(*) FILTER (WHERE status = 'ERROR'),
                   COUNT(*) FILTER (WHERE COALESCE(approval_status, 'DRAFT') = 'DRAFT'),
                   COUNT(*) FILTER (WHERE approval_status = 'PENDING'),
                   COUNT(*) FILTER (WHERE approval_status = 'APPROVED'),
                   COUNT(*) FILTER (WHERE approval_status = 'REJECTED')
            FROM app_records WHERE table_id = @tbl AND deleted_at IS NULL
            """, conn))
        {
            cntCmd.Parameters.AddWithValue("tbl", tableId);
            await using var r = await cntCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                dto.TotalRecords = Convert.ToInt32(r.GetInt64(0));
                dto.ValidRecords = Convert.ToInt32(r.GetInt64(1));
                dto.ErrorRecords = Convert.ToInt32(r.GetInt64(2));
                dto.ApprovalBreakdown = new Dictionary<string, int>
                {
                    ["DRAFT"] = Convert.ToInt32(r.GetInt64(3)),
                    ["PENDING"] = Convert.ToInt32(r.GetInt64(4)),
                    ["APPROVED"] = Convert.ToInt32(r.GetInt64(5)),
                    ["REJECTED"] = Convert.ToInt32(r.GetInt64(6)),
                };
            }
        }

        foreach (var col in columns.Where(c =>
            c.DataType.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
            c.DataType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(c.RefCategoryCode)))
        {
            var catMap = new Dictionary<string, (string Name, string Color)>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(col.RefCategoryCode))
            {
                var catItems = await _metadata.GetCategoryItemsAsync(col.RefCategoryCode);
                foreach (var ci in catItems) catMap[ci.ItemCode] = (ci.ItemName, ci.Color ?? "#1a73e8");
            }

            var grouped = new List<CategoryDistributionItem>();
            await using (var grpCmd = new NpgsqlCommand("""
                SELECT COALESCE(NULLIF(TRIM(data ->> @col), ''), '(Trống)') AS grp_key, COUNT(*) AS cnt
                FROM app_records
                WHERE table_id = @tbl AND deleted_at IS NULL
                GROUP BY grp_key
                ORDER BY cnt DESC
                LIMIT 25
                """, conn))
            {
                grpCmd.Parameters.AddWithValue("tbl", tableId);
                grpCmd.Parameters.AddWithValue("col", col.ColumnCode);
                await using var r = await grpCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var key = r.GetString(0);
                    var count = Convert.ToInt32(r.GetInt64(1));
                    var label = key;
                    var color = "#1a73e8";
                    if (col.DataType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase))
                    {
                        label = key.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Có / Đúng" : "Không / Sai";
                        color = key.Equals("true", StringComparison.OrdinalIgnoreCase) ? "#1e8e3e" : "#ea8600";
                    }
                    else if (catMap.TryGetValue(key, out var info))
                    {
                        label = info.Name;
                        color = info.Color;
                    }
                    grouped.Add(new CategoryDistributionItem { Code = key, Label = label, Color = color, Count = count });
                }
            }

            dto.Distributions.Add(new CategoryGroupStatsDto
            {
                ColumnCode = col.ColumnCode,
                ColumnName = col.ColumnName,
                Items = grouped
            });
        }

        foreach (var col in columns.Where(c =>
            c.DataType.Equals("NUMBER", StringComparison.OrdinalIgnoreCase) ||
            c.DataType.Equals("CURRENCY", StringComparison.OrdinalIgnoreCase)))
        {
            await using var numCmd = new NpgsqlCommand("""
                SELECT COALESCE(SUM((data ->> @col)::numeric), 0),
                       COALESCE(AVG((data ->> @col)::numeric), 0),
                       COALESCE(MIN((data ->> @col)::numeric), 0),
                       COALESCE(MAX((data ->> @col)::numeric), 0),
                       COUNT(*)
                FROM app_records
                WHERE table_id = @tbl AND deleted_at IS NULL
                  AND COALESCE(data ->> @col, '') ~ '^-?[0-9]+(\.[0-9]+)?$'
                """, conn);
            numCmd.Parameters.AddWithValue("tbl", tableId);
            numCmd.Parameters.AddWithValue("col", col.ColumnCode);
            await using var r = await numCmd.ExecuteReaderAsync();
            if (await r.ReadAsync() && r.GetInt64(4) > 0)
            {
                dto.NumericSummaries.Add(new NumericFieldStatsDto
                {
                    ColumnCode = col.ColumnCode,
                    ColumnName = col.ColumnName,
                    DataType = col.DataType,
                    Sum = Math.Round(r.GetDecimal(0), 2),
                    Avg = Math.Round(r.GetDecimal(1), 2),
                    Min = r.GetDecimal(2),
                    Max = r.GetDecimal(3)
                });
            }
        }

        return dto;
    }

    public async Task<List<SysSavedViewDto>> ListSavedViewsAsync(string tableId, string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT id, app_id, table_id, name, filters::text, sort_field, sort_dir, is_shared, created_by
            FROM sys_saved_views
            WHERE table_id = @tbl AND (is_shared = TRUE OR LOWER(created_by) = LOWER(@u))
            ORDER BY created_at ASC
            """;
        var list = new List<SysSavedViewDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.AddWithValue("u", username);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysSavedViewDto
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                TableId = r.GetString(2),
                Name = r.GetString(3),
                Filters = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(4)) ?? new(),
                SortField = r.IsDBNull(5) ? null : r.GetString(5),
                SortDir = r.IsDBNull(6) ? "DESC" : r.GetString(6),
                IsShared = r.GetBoolean(7),
                CreatedBy = r.IsDBNull(8) ? "admin" : r.GetString(8)
            });
        }
        return list;
    }

    public async Task<SysSavedViewDto> UpsertSavedViewAsync(UpsertSavedViewRequest req, string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"view_{Guid.NewGuid().ToString("N")[..8]}" : req.Id.Trim();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_saved_views (id, app_id, table_id, name, filters, sort_field, sort_dir, is_shared, created_by)
            VALUES (@id, @app, @tbl, @name, @flt::jsonb, @sf, @sd, @sh, @cb)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                filters = EXCLUDED.filters,
                sort_field = EXCLUDED.sort_field,
                sort_dir = EXCLUDED.sort_dir,
                is_shared = EXCLUDED.is_shared
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", req.AppId);
        cmd.Parameters.AddWithValue("tbl", req.TableId);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("flt", JsonSerializer.Serialize(req.Filters ?? new()));
        cmd.Parameters.AddWithValue("sf", string.IsNullOrWhiteSpace(req.SortField) ? DBNull.Value : req.SortField.Trim());
        cmd.Parameters.AddWithValue("sd", string.Equals(req.SortDir, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC");
        cmd.Parameters.AddWithValue("sh", req.IsShared);
        cmd.Parameters.AddWithValue("cb", username);
        await cmd.ExecuteNonQueryAsync();
        return (await ListSavedViewsAsync(req.TableId, username)).First(v => v.Id == id);
    }

    public async Task<bool> DeleteSavedViewAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_saved_views WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task EvaluateCrossAppFormulasAsync(
        NpgsqlConnection conn,
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
                    """, conn);
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
                    """, conn);
                cmd.Parameters.AddWithValue("tbl", mSum.Groups[1].Value);
                cmd.Parameters.AddWithValue("fk", mSum.Groups[2].Value);
                cmd.Parameters.AddWithValue("sf", mSum.Groups[3].Value);
                cmd.Parameters.AddWithValue("val", recordKey.Trim());
                data[col.ColumnCode] = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
            }
        }
    }

    private static async Task<AppRecordDto?> GetRecordByIdInternalAsync(NpgsqlConnection conn, string id)
    {
        await using var cmd = new NpgsqlCommand($"SELECT {SelectColumns} FROM app_records WHERE id = @id AND deleted_at IS NULL LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadRecord(r) : null;
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadCategorySetsAsync(List<SysColumnDto> columns)
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

    private static void AddFilterParams(
        NpgsqlCommand cmd,
        string? appId,
        string? moduleId,
        string? tableId,
        string? search,
        string? status,
        string? ownerFilter = null,
        string? ownerEmail = null,
        string? ownerEmployeeCode = null,
        string? ownerDepartment = null)
    {
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId });
        cmd.Parameters.Add(new NpgsqlParameter("modId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(moduleId) ? DBNull.Value : moduleId });
        cmd.Parameters.Add(new NpgsqlParameter("tblId", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(tableId) ? DBNull.Value : tableId });
        cmd.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(status) ? "ALL" : status });
        AddOwnerFilterParams(cmd, ownerFilter, ownerEmail, ownerEmployeeCode, ownerDepartment);
        cmd.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim() });
    }

    private static AppRecordDto ReadRecord(NpgsqlDataReader r)
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

    private static AppAuditLogDto ReadAuditRow(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        AppId = r.GetString(1),
        TableId = r.GetString(2),
        RecordId = r.GetString(3),
        Action = r.GetString(4),
        Actor = r.IsDBNull(5) ? "system" : r.GetString(5),
        OldData = r.IsDBNull(6) ? null : ParseJsonDict(r.GetString(6)),
        NewData = r.IsDBNull(7) ? null : ParseJsonDict(r.GetString(7)),
        Summary = r.IsDBNull(8) ? null : r.GetString(8),
        CreatedAt = r.GetDateTime(9)
    };

    private static Dictionary<string, object?> ParseJsonDict(string json)
    {
        var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject()) dict[prop.Name] = UnwrapJsonValue(prop.Value);
        return dict;
    }

    private static Dictionary<string, object?> NormalizeDict(Dictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in source) result[kv.Key] = UnwrapJsonValue(kv.Value);
        return result;
    }

    public static async Task WriteAuditAsync(
        NpgsqlConnection conn, string appId, string tableId, string recordId,
        string action, string actor, Dictionary<string, object?>? oldData, Dictionary<string, object?>? newData, string summary)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_audit_logs (id, app_id, table_id, record_id, action, actor, old_data, new_data, summary)
            VALUES (@id, @app, @tbl, @rec, @act, @actor, @old::jsonb, @new::jsonb, @sum)
            """, conn);
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

    private static object? UnwrapJsonValue(object? val)
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

    internal static string ResolveAutoNumberPrefix(string? defaultValue)
    {
        var prefix = string.IsNullOrWhiteSpace(defaultValue) ? "REC-{YYYY}-" : defaultValue!;
        return prefix
            .Replace("{YYYY}", DateTime.UtcNow.ToString("yyyy"))
            .Replace("{MM}", DateTime.UtcNow.ToString("MM"));
    }

    internal static async Task<string> NextAutoNumberAsync(
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

    internal static async Task AdvanceAutoNumberCounterAsync(
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
