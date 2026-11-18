using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class WorkflowService
{
    private readonly string _connStr;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public WorkflowService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
    }

    public async Task<List<AppWorkflowDto>> GetWorkflowsAsync(string? appId = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT w.id, w.name, w.description, w.app_id, ap.code, ap.name,
                   w.table_id, t.display_name, w.trigger_type,
                   w.condition_field, w.condition_operator, w.condition_value,
                   w.steps::text, w.is_active, w.created_at, w.updated_at,
                   (SELECT COUNT(*) FROM app_records r
                    WHERE r.table_id = w.table_id AND r.deleted_at IS NULL AND r.approval_status = 'PENDING') AS pending_cnt
            FROM app_workflows w
            LEFT JOIN sys_apps ap ON ap.id = w.app_id
            LEFT JOIN sys_tables t ON t.id = w.table_id
            WHERE (@app IS NULL OR w.app_id = @app)
            ORDER BY w.updated_at DESC
            """;

        var list = new List<AppWorkflowDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId
        });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadWorkflow(r));
        return list;
    }

    public async Task<AppWorkflowDto?> GetByIdAsync(string id)
    {
        var all = await GetWorkflowsAsync();
        return all.FirstOrDefault(w => w.Id == id);
    }

    public async Task<AppWorkflowDto?> ResolveWorkflowForRecordAsync(string tableId, Dictionary<string, object?> data, string? preferredWorkflowId = null)
    {
        var all = await GetWorkflowsAsync();
        if (!string.IsNullOrWhiteSpace(preferredWorkflowId))
        {
            var exact = all.FirstOrDefault(w => w.Id == preferredWorkflowId);
            if (exact != null) return exact;
        }

        var candidates = all.Where(w => w.TableId == tableId && w.IsActive).ToList();
        foreach (var wf in candidates)
        {
            if (string.IsNullOrWhiteSpace(wf.ConditionField)) return wf;
            if (MatchesCondition(wf.ConditionField, wf.ConditionOperator, wf.ConditionValue, data))
                return wf;
        }
        return candidates.FirstOrDefault(w => string.IsNullOrWhiteSpace(w.ConditionField));
    }

    public async Task<AppWorkflowDto> UpsertWorkflowAsync(UpsertWorkflowRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"wf_{Guid.NewGuid().ToString("N")[..8]}" : req.Id.Trim();
        var normalizedSteps = (req.Steps ?? new())
            .Select((s, idx) => new WorkflowStepDto
            {
                StepOrder = idx + 1,
                Name = string.IsNullOrWhiteSpace(s.Name) ? $"Bước {idx + 1}" : s.Name.Trim(),
                ApproverRoles = (s.ApproverRoles ?? new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ApproverUsers = (s.ApproverUsers ?? new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SlaHours = s.SlaHours > 0 ? s.SlaHours : 24,
                RequireCommentOnReject = s.RequireCommentOnReject
            })
            .ToList();

        var sql = """
            INSERT INTO app_workflows (id, name, description, app_id, table_id, trigger_type,
                condition_field, condition_operator, condition_value, steps, is_active, updated_at)
            VALUES (@id, @n, @d, @app, @tbl, @trig, @cf, @co, @cv, @steps::jsonb, @act, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                app_id = EXCLUDED.app_id,
                table_id = EXCLUDED.table_id,
                trigger_type = EXCLUDED.trigger_type,
                condition_field = EXCLUDED.condition_field,
                condition_operator = EXCLUDED.condition_operator,
                condition_value = EXCLUDED.condition_value,
                steps = EXCLUDED.steps,
                is_active = EXCLUDED.is_active,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", req.Name.Trim());
        cmd.Parameters.AddWithValue("d", (object?)req.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("app", req.AppId);
        cmd.Parameters.AddWithValue("tbl", req.TableId);
        cmd.Parameters.AddWithValue("trig", string.IsNullOrWhiteSpace(req.TriggerType) ? "MANUAL_SUBMIT" : req.TriggerType);
        cmd.Parameters.AddWithValue("cf", string.IsNullOrWhiteSpace(req.ConditionField) ? DBNull.Value : req.ConditionField.Trim());
        cmd.Parameters.AddWithValue("co", string.IsNullOrWhiteSpace(req.ConditionOperator) ? "EQUALS" : req.ConditionOperator);
        cmd.Parameters.AddWithValue("cv", string.IsNullOrWhiteSpace(req.ConditionValue) ? DBNull.Value : req.ConditionValue.Trim());
        cmd.Parameters.AddWithValue("steps", JsonSerializer.Serialize(normalizedSteps));
        cmd.Parameters.AddWithValue("act", req.IsActive);
        await cmd.ExecuteNonQueryAsync();

        // Ensure target table has approval_enabled = TRUE and union of step roles synced
        if (req.IsActive && normalizedSteps.Count > 0)
        {
            var allRoles = normalizedSteps.SelectMany(s => s.ApproverRoles).Append("ADMIN").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await using var syncTbl = new NpgsqlCommand("""
                UPDATE sys_tables
                SET approval_enabled = TRUE, approver_roles = @roles::jsonb
                WHERE id = @tbl
                """, conn);
            syncTbl.Parameters.AddWithValue("roles", JsonSerializer.Serialize(allRoles));
            syncTbl.Parameters.AddWithValue("tbl", req.TableId);
            await syncTbl.ExecuteNonQueryAsync();
        }

        return (await GetWorkflowsAsync()).First(w => w.Id == id);
    }

    public async Task<bool> ToggleWorkflowAsync(string id, bool isActive)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE app_workflows SET is_active = @a, updated_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("a", isActive);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteWorkflowAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM app_workflows WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public static bool MatchesCondition(string field, string op, string? expected, Dictionary<string, object?> data)
    {
        data.TryGetValue(field, out var raw);
        var actual = raw?.ToString()?.Trim() ?? string.Empty;
        var exp = expected?.Trim() ?? string.Empty;

        return op.ToUpperInvariant() switch
        {
            "EQUALS" => string.Equals(actual, exp, StringComparison.OrdinalIgnoreCase),
            "NOT_EQUALS" => !string.Equals(actual, exp, StringComparison.OrdinalIgnoreCase),
            "CONTAINS" => actual.Contains(exp, StringComparison.OrdinalIgnoreCase),
            "NOT_EMPTY" => !string.IsNullOrEmpty(actual),
            "GREATER_THAN" => decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var aNum) &&
                              decimal.TryParse(exp, NumberStyles.Any, CultureInfo.InvariantCulture, out var eNum) &&
                              aNum > eNum,
            "LESS_THAN" => decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var aNum2) &&
                           decimal.TryParse(exp, NumberStyles.Any, CultureInfo.InvariantCulture, out var eNum2) &&
                           aNum2 < eNum2,
            "DAYS_BEFORE" => TryParseDate(actual, out var dtBefore) &&
                             int.TryParse(exp, out var daysBefore) &&
                             (dtBefore - DateTime.UtcNow.Date).TotalDays >= 0 &&
                             (dtBefore - DateTime.UtcNow.Date).TotalDays <= daysBefore,
            "DAYS_AFTER" => TryParseDate(actual, out var dtAfter) &&
                            int.TryParse(exp, out var daysAfter) &&
                            (DateTime.UtcNow.Date - dtAfter).TotalDays >= 0 &&
                            (DateTime.UtcNow.Date - dtAfter).TotalDays <= daysAfter,
            "EQUALS_TODAY" => TryParseDate(actual, out var dtToday) && dtToday == DateTime.UtcNow.Date,
            _ => true
        };
    }

    private static bool TryParseDate(string raw, out DateTime dateOnly)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            dateOnly = parsed.Date;
            return true;
        }
        dateOnly = default;
        return false;
    }

    private static AppWorkflowDto ReadWorkflow(NpgsqlDataReader r)
    {
        var steps = JsonSerializer.Deserialize<List<WorkflowStepDto>>(r.GetString(12), JsonOpts) ?? new();
        return new AppWorkflowDto
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            Description = r.IsDBNull(2) ? null : r.GetString(2),
            AppId = r.GetString(3),
            AppCode = r.IsDBNull(4) ? null : r.GetString(4),
            AppName = r.IsDBNull(5) ? null : r.GetString(5),
            TableId = r.GetString(6),
            TableDisplayName = r.IsDBNull(7) ? null : r.GetString(7),
            TriggerType = r.IsDBNull(8) ? "MANUAL_SUBMIT" : r.GetString(8),
            ConditionField = r.IsDBNull(9) ? null : r.GetString(9),
            ConditionOperator = r.IsDBNull(10) ? "EQUALS" : r.GetString(10),
            ConditionValue = r.IsDBNull(11) ? null : r.GetString(11),
            Steps = steps.OrderBy(s => s.StepOrder).ToList(),
            IsActive = r.GetBoolean(13),
            CreatedAt = r.GetDateTime(14),
            UpdatedAt = r.GetDateTime(15),
            PendingCount = Convert.ToInt32(r.GetInt64(16))
        };
    }
}
