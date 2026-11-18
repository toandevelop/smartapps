using Npgsql;

namespace SmartApps.Api.Services;

/// <summary>
/// Background hosted job checking pending approvals and due/overdue tasks
/// across the ecosystem and pushing deduplicated daily reminders.
/// </summary>
public class ScheduledReminderService : BackgroundService
{
    private readonly string _connStr;
    private readonly NotificationService _notifications;
    private readonly AutomationService _automations;
    private readonly ILogger<ScheduledReminderService> _logger;

    public ScheduledReminderService(
        IConfiguration config,
        NotificationService notifications,
        AutomationService automations,
        ILogger<ScheduledReminderService> logger)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _notifications = notifications;
        _automations = automations;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay briefly after boot so DbInitializer completes first
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckPendingApprovalsAsync(stoppingToken);
                await CheckDueTasksAsync(stoppingToken);
                await _automations.RunScheduledDateTriggersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "ScheduledReminderService tick skipped");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task CheckPendingApprovalsAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        // Group pending records by workflow/step to notify the actual assigned approvers
        var pendingList = new List<(string TableId, string? WorkflowId, int StepOrder, long Cnt)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT table_id, workflow_id, COALESCE(current_step_order, 1), COUNT(*)
            FROM app_records
            WHERE deleted_at IS NULL AND approval_status = 'PENDING'
            GROUP BY table_id, workflow_id, COALESCE(current_step_order, 1)
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                pendingList.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt32(2), r.GetInt64(3)));
            }
        }
        if (pendingList.Count == 0) return;

        var wfStepsByKey = new Dictionary<string, (HashSet<string> Roles, HashSet<string> Users, int SlaHours)>();
        await using (var wCmd = new NpgsqlCommand("SELECT id, table_id, steps::text FROM app_workflows WHERE is_active = TRUE", conn))
        await using (var wr = await wCmd.ExecuteReaderAsync(ct))
        {
            while (await wr.ReadAsync(ct))
            {
                var wfId = wr.GetString(0);
                var tblId = wr.GetString(1);
                var stepsJson = wr.GetString(2);
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(stepsJson);
                    foreach (var st in doc.RootElement.EnumerateArray())
                    {
                        var ord = st.TryGetProperty("stepOrder", out var op) && op.TryGetInt32(out var oi) ? oi : 1;
                        var sla = st.TryGetProperty("slaHours", out var sp) && sp.TryGetInt32(out var si) && si > 0 ? si : 24;
                        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (st.TryGetProperty("approverRoles", out var rp))
                            foreach (var el in rp.EnumerateArray()) { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) roles.Add(s.Trim().ToUpperInvariant()); }
                        if (st.TryGetProperty("approverUsers", out var up))
                            foreach (var el in up.EnumerateArray()) { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) users.Add(s.Trim()); }
                        wfStepsByKey[$"{wfId}:{ord}"] = (roles, users, sla);
                        wfStepsByKey.TryAdd($"tbl:{tblId}:{ord}", (roles, users, sla));
                    }
                }
                catch { }
            }
        }

        // Load active users with their role codes
        var usersByRole = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using (var uCmd = new NpgsqlCommand("SELECT username, role_code, COALESCE(role_codes::text, '[]') FROM sys_users WHERE is_active = TRUE", conn))
        await using (var ur = await uCmd.ExecuteReaderAsync(ct))
        {
            while (await ur.ReadAsync(ct))
            {
                var uname = ur.GetString(0);
                var rc = ur.GetString(1);
                var rcs = System.Text.Json.JsonSerializer.Deserialize<List<string>>(ur.GetString(2)) ?? new();
                if (!rcs.Contains(rc, StringComparer.OrdinalIgnoreCase)) rcs.Add(rc);
                foreach (var roleCode in rcs)
                {
                    if (!usersByRole.TryGetValue(roleCode, out var list)) usersByRole[roleCode] = list = new();
                    if (!list.Contains(uname, StringComparer.OrdinalIgnoreCase)) list.Add(uname);
                }
            }
        }

        var countByApprover = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var overdueApprovers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pendingList)
        {
            var matched = (!string.IsNullOrWhiteSpace(item.WorkflowId) && wfStepsByKey.TryGetValue($"{item.WorkflowId}:{item.StepOrder}", out var stepA))
                ? stepA
                : (wfStepsByKey.TryGetValue($"tbl:{item.TableId}:{item.StepOrder}", out var stepB) ? stepB : (Roles: new HashSet<string>(), Users: new HashSet<string>(), SlaHours: 24));

            var targets = new HashSet<string>(matched.Users, StringComparer.OrdinalIgnoreCase);
            foreach (var roleCode in matched.Roles)
            {
                if (usersByRole.TryGetValue(roleCode, out var rUsers))
                    foreach (var u in rUsers) targets.Add(u);
            }
            if (targets.Count == 0) targets.Add("admin");

            await using var slaCmd = new NpgsqlCommand("""
                SELECT COUNT(*) FROM app_records
                WHERE table_id = @tbl
                  AND deleted_at IS NULL
                  AND approval_status = 'PENDING'
                  AND COALESCE(current_step_order, 1) = @step
                  AND updated_at + make_interval(hours => @sla) < CURRENT_TIMESTAMP
                """, conn);
            slaCmd.Parameters.AddWithValue("tbl", item.TableId);
            slaCmd.Parameters.AddWithValue("step", item.StepOrder);
            slaCmd.Parameters.AddWithValue("sla", matched.SlaHours);
            var overdueCnt = Convert.ToInt64(await slaCmd.ExecuteScalarAsync(ct));

            foreach (var t in targets)
            {
                countByApprover[t] = countByApprover.GetValueOrDefault(t) + item.Cnt;
                if (overdueCnt > 0)
                    overdueApprovers[t] = overdueApprovers.GetValueOrDefault(t) + overdueCnt;
            }
        }

        if (overdueApprovers.Count > 0)
        {
            overdueApprovers.TryAdd("admin", overdueApprovers.Values.Sum());
            const string slaTitle = "Cảnh báo quá hạn SLA phê duyệt";
            foreach (var (recp, odCnt) in overdueApprovers)
            {
                await using var chkSla = new NpgsqlCommand("""
                    SELECT 1 FROM sys_notifications
                    WHERE LOWER(recipient_username) = LOWER(@u)
                      AND title = @t
                      AND created_at::date = CURRENT_DATE
                    LIMIT 1
                    """, conn);
                chkSla.Parameters.AddWithValue("u", recp);
                chkSla.Parameters.AddWithValue("t", slaTitle);
                if (await chkSla.ExecuteScalarAsync(ct) != null) continue;

                await _notifications.CreateAsync(
                    slaTitle,
                    $"Có {odCnt} phiếu chờ duyệt đã vượt thời hạn SLA cam kết — cần xử lý gấp.",
                    null,
                    null,
                    recp,
                    "APPROVAL",
                    "/");
            }
        }

        const string title = "Nhắc nhở phê duyệt định kỳ";
        foreach (var (recipient, count) in countByApprover)
        {
            await using var chk = new NpgsqlCommand("""
                SELECT 1 FROM sys_notifications
                WHERE LOWER(recipient_username) = LOWER(@u)
                  AND title = @t
                  AND created_at::date = CURRENT_DATE
                LIMIT 1
                """, conn);
            chk.Parameters.AddWithValue("u", recipient);
            chk.Parameters.AddWithValue("t", title);
            if (await chk.ExecuteScalarAsync(ct) != null) continue;

            await _notifications.CreateAsync(
                title,
                $"Bạn có {count} phiếu/bản ghi đang chờ đến lượt bạn phê duyệt.",
                null,
                null,
                recipient,
                "APPROVAL",
                "/");
        }
    }

    private async Task CheckDueTasksAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var dueList = new List<(string Assignee, int Count)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT assignee_username, COUNT(*)
            FROM app_tasks
            WHERE status NOT IN ('DONE', 'CANCELLED')
              AND due_date IS NOT NULL
              AND due_date <= CURRENT_DATE
            GROUP BY assignee_username
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                dueList.Add((r.GetString(0), Convert.ToInt32(r.GetInt64(1))));
            }
        }

        foreach (var (assignee, count) in dueList)
        {
            const string title = "Nhắc việc đến hạn / quá hạn";
            await using var chk = new NpgsqlCommand("""
                SELECT 1 FROM sys_notifications
                WHERE LOWER(recipient_username) = LOWER(@u)
                  AND category = 'TASK'
                  AND title = @t
                  AND created_at::date = CURRENT_DATE
                LIMIT 1
                """, conn);
            chk.Parameters.AddWithValue("u", assignee);
            chk.Parameters.AddWithValue("t", title);
            if (await chk.ExecuteScalarAsync(ct) != null) continue;

            await _notifications.CreateAsync(
                title,
                $"Bạn có {count} công việc đã đến hạn hoặc quá hạn cần xử lý.",
                "TASKS",
                null,
                assignee,
                "TASK",
                "/apps/TASKS");
        }
    }
}
