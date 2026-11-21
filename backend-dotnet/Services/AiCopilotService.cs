using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public sealed class AiCopilotAskRequest
{
    public string Message { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string? TableId { get; set; }
    public List<AiCopilotHistoryMessage>? History { get; set; }
}

public sealed class AiCopilotHistoryMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public sealed class AiCopilotCitationDto
{
    public string Kind { get; set; } = "RECORD"; // RECORD | APP | TASK | APPROVAL
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Badge { get; set; }
}

public sealed class AiCopilotToolTraceDto
{
    public string Tool { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public long DurationMs { get; set; }
}

public sealed class AiCopilotProposedActionDto
{
    public string ActionType { get; set; } = "CREATE_RECORD";
    public string Label { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string TableDisplayName { get; set; } = string.Empty;
    public Dictionary<string, object?> DraftData { get; set; } = new();
    public string Note { get; set; } = string.Empty;
}

public sealed class AiCopilotAskResponse
{
    public string Answer { get; set; } = string.Empty;
    public string Engine { get; set; } = "SmartApps Enterprise Agent";
    public List<AiCopilotCitationDto> Citations { get; set; } = new();
    public List<AiCopilotToolTraceDto> ToolTraces { get; set; } = new();
    public AiCopilotProposedActionDto? ProposedAction { get; set; }
}

public sealed class AiCopilotToolInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Mode { get; set; } = "READ"; // READ | PROPOSE
}

/// <summary>
/// Global Multi-App AI Agent Copilot with RBAC, FLS, RowScope enforcement,
/// Vietnamese accent-insensitive tool execution, Human-in-the-Loop proposals,
/// and optional LLM synthesis (with automatic PII masking for cloud providers).
/// </summary>
public sealed class AiCopilotService
{
    private readonly string _connStr;
    private readonly MetadataService _metadata;
    private readonly RecordService _records;
    private readonly TaskService _tasks;
    private readonly AiService _ai;

    public AiCopilotService(
        IConfiguration config,
        MetadataService metadata,
        RecordService records,
        TaskService tasks,
        AiService ai)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _metadata = metadata;
        _records = records;
        _tasks = tasks;
        _ai = ai;
    }

    public async Task<object> GetCapabilitiesAsync(SysUserDto user)
    {
        var aiStatus = await _ai.GetAiStatusAsync();
        var apps = (await _metadata.GetAppsAsync())
            .Where(a => AuthService.CanAccessApp(user, a.Code))
            .Select(a => new { a.Id, a.Code, a.Name, a.Department })
            .ToList();

        var tools = new List<AiCopilotToolInfoDto>
        {
            new() { Name = "search_records", Title = "Tìm kiếm hồ sơ không dấu", Description = "Tra cứu liên ứng dụng bằng smartapps_vn_norm theo đúng quyền FLS/RowScope", Mode = "READ" },
            new() { Name = "list_pending_approvals", Title = "Hồ sơ chờ phê duyệt", Description = "Liệt kê hồ sơ đang ở trạng thái chờ duyệt trong phạm vi quyền của bạn", Mode = "READ" },
            new() { Name = "list_my_tasks", Title = "Công việc & tiến độ cá nhân", Description = "Tổng hợp công việc đang mở hoặc quá hạn được giao cho bạn", Mode = "READ" },
            new() { Name = "get_app_stats", Title = "Thống kê tổng quan ứng dụng", Description = "Đếm bản ghi hoạt động và hồ sơ chờ duyệt trên các ứng dụng bạn được phép xem", Mode = "READ" },
            new() { Name = "draft_record_proposal", Title = "Đề xuất tạo hồ sơ mới", Description = "Soạn sẵn dữ liệu hồ sơ để bạn kiểm tra và xác nhận (Human-in-the-Loop)", Mode = "PROPOSE" },
        };

        return new
        {
            aiConfigured = aiStatus.IsConfigured,
            providerName = aiStatus.ActiveProviderName,
            model = aiStatus.ActiveModel,
            accessibleAppCount = apps.Count,
            accessibleApps = apps,
            tools
        };
    }

    public async Task<AiCopilotAskResponse> AskAsync(AiCopilotAskRequest req, SysUserDto user, CancellationToken ct = default)
    {
        var rawMsg = (req.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawMsg))
        {
            return new AiCopilotAskResponse
            {
                Answer = "Vui lòng nhập câu hỏi hoặc yêu cầu (ví dụ: *\"Hồ sơ nào đang chờ tôi duyệt?\"*, *\"Việc quá hạn của tôi\"*, *\"Thống kê ứng dụng\"* hoặc *\"Tìm hồ sơ ...\"*)."
            };
        }

        var norm = RecordService.NormalizeVietnamese(rawMsg);
        var traces = new List<AiCopilotToolTraceDto>();
        var citations = new List<AiCopilotCitationDto>();
        var factSections = new List<string>();
        AiCopilotProposedActionDto? proposedAction = null;

        var wantsApprovals = ContainsAny(norm, "cho duyet", "phe duyet", "trinh ky", "duyet gi", "can duyet", "approval", "pending");
        var wantsTasks = ContainsAny(norm, "cong viec", "viec cua toi", "qua han", "deadline", "task", "nhiem vu");
        var wantsStats = ContainsAny(norm, "thong ke", "tong quan", "bao nhieu", "so lieu", "bao cao", "summary", "stats");
        var wantsDraft = ContainsAny(norm, "tao moi", "tao ho so", "tao phieu", "lap phieu", "lap van ban", "de xuat tao", "tao yeu cau", "create record");
        var wantsSearch = ContainsAny(norm, "tim ", "tra cuu", "tim kiem", "ho so ", "ma ", "van ban ", "hop dong ", "search ")
            || (!wantsApprovals && !wantsTasks && !wantsStats && !wantsDraft);

        if (wantsApprovals)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (text, hits) = await RunPendingApprovalsToolAsync(user, req.AppCode, ct);
            sw.Stop();
            traces.Add(new AiCopilotToolTraceDto { Tool = "list_pending_approvals", Summary = $"Tìm thấy {hits.Count} hồ sơ chờ duyệt", ResultCount = hits.Count, DurationMs = sw.ElapsedMilliseconds });
            citations.AddRange(hits);
            factSections.Add(text);
        }

        if (wantsTasks)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (text, hits) = await RunMyTasksToolAsync(user);
            sw.Stop();
            traces.Add(new AiCopilotToolTraceDto { Tool = "list_my_tasks", Summary = $"Tìm thấy {hits.Count} công việc liên quan", ResultCount = hits.Count, DurationMs = sw.ElapsedMilliseconds });
            citations.AddRange(hits);
            factSections.Add(text);
        }

        if (wantsStats)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (text, hits) = await RunAppStatsToolAsync(user, req.AppCode, ct);
            sw.Stop();
            traces.Add(new AiCopilotToolTraceDto { Tool = "get_app_stats", Summary = $"Thống kê {hits.Count} ứng dụng trong phạm vi quyền", ResultCount = hits.Count, DurationMs = sw.ElapsedMilliseconds });
            citations.AddRange(hits);
            factSections.Add(text);
        }

        if (wantsDraft)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (text, proposal) = await RunDraftProposalToolAsync(user, rawMsg, req.AppCode, req.TableId);
            sw.Stop();
            traces.Add(new AiCopilotToolTraceDto
            {
                Tool = "draft_record_proposal",
                Summary = proposal != null ? $"Đã soạn đề xuất cho bảng {proposal.TableDisplayName}" : "Chưa xác định được bảng đích có quyền tạo mới",
                ResultCount = proposal != null ? 1 : 0,
                DurationMs = sw.ElapsedMilliseconds
            });
            proposedAction = proposal;
            factSections.Add(text);
        }

        if (wantsSearch)
        {
            var keyword = ExtractSearchKeyword(rawMsg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (text, hits) = await RunSearchToolAsync(user, keyword);
            sw.Stop();
            traces.Add(new AiCopilotToolTraceDto { Tool = "search_records", Summary = $"Tra cứu \"{keyword}\": {hits.Count} kết quả", ResultCount = hits.Count, DurationMs = sw.ElapsedMilliseconds });
            citations.AddRange(hits);
            factSections.Add(text);
        }

        // Deduplicate citations by Url + Id
        var uniqueCitations = citations
            .GroupBy(c => $"{c.Kind}:{c.Id}:{c.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        var deterministicAnswer = string.Join("\n\n", factSections.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(deterministicAnswer))
        {
            deterministicAnswer = "Mình chưa tìm thấy dữ liệu khớp trong phạm vi quyền truy cập của bạn. Hãy thử nhập từ khóa cụ thể hơn (mã phiếu, tên đối tác, hoặc hỏi *\"Hồ sơ chờ duyệt\"* / *\"Thống kê ứng dụng\"*).";
        }

        var engine = "SmartApps Enterprise Agent (Built-in RBAC Engine)";
        var finalAnswer = deterministicAnswer;

        var aiStatus = await _ai.GetAiStatusAsync();
        if (aiStatus.IsConfigured)
        {
            const string sysPrompt = """
                Bạn là Trợ lý AI Doanh nghiệp SmartApps Copilot.
                QUY TẮC BẮT BUỘC:
                1. Chỉ trả lời dựa trên dữ liệu công cụ đã truy xuất bên dưới (không bịa số liệu hay bản ghi).
                2. Trả lời ngắn gọn, súc tích bằng tiếng Việt chuyên nghiệp dạng Markdown (dùng gạch đầu dòng rõ ràng).
                3. Nếu có đề xuất tạo hồ sơ mới, nhắc người dùng kiểm tra và bấm nút Xác nhận bên dưới.
                """;
            var userPrompt = $"Người dùng ({user.FullName}, vai trò {user.RoleCode}) hỏi:\n{rawMsg}\n\nKết quả truy xuất thực tế từ hệ thống (đã lọc quyền RBAC/FLS):\n{deterministicAnswer}";
            var (ok, content, providerName, model, _) = await _ai.CompleteAsync("AI_COPILOT_AGENT", sysPrompt, userPrompt, user.Username, jsonMode: false);
            if (ok && !string.IsNullOrWhiteSpace(content))
            {
                finalAnswer = content.Trim();
                engine = $"{providerName} ({model}) + SmartApps Tools";
            }
        }

        return new AiCopilotAskResponse
        {
            Answer = finalAnswer,
            Engine = engine,
            Citations = uniqueCitations,
            ToolTraces = traces,
            ProposedAction = proposedAction
        };
    }

    private async Task<(string Text, List<AiCopilotCitationDto> Citations)> RunSearchToolAsync(SysUserDto user, string keyword)
    {
        var search = await _records.GlobalSearchAsync(user, keyword, limitPerKind: 6);
        var citations = new List<AiCopilotCitationDto>();
        var sb = new StringBuilder();

        foreach (var r in search.Records)
        {
            citations.Add(new AiCopilotCitationDto
            {
                Kind = "RECORD",
                Id = r.Id,
                Title = r.Title,
                Subtitle = r.Subtitle,
                Url = r.Url,
                Badge = r.Badge
            });
        }
        foreach (var a in search.Apps.Take(3))
        {
            citations.Add(new AiCopilotCitationDto
            {
                Kind = "APP",
                Id = a.Id,
                Title = a.Title,
                Subtitle = a.Subtitle,
                Url = a.Url,
                Badge = a.Badge
            });
        }

        if (search.Records.Count == 0 && search.Apps.Count == 0)
        {
            sb.Append($"Không tìm thấy hồ sơ hoặc ứng dụng nào khớp với **\"{keyword}\"** trong phạm vi quyền của bạn.");
            return (sb.ToString(), citations);
        }

        sb.AppendLine($"### Kết quả tra cứu cho **\"{keyword}\"**");
        if (search.Records.Count > 0)
        {
            sb.AppendLine($"- Tìm thấy **{search.Records.Count}** hồ sơ dữ liệu:");
            foreach (var rec in search.Records)
                sb.AppendLine($"  - **{rec.Title}** — {rec.Subtitle}");
        }
        if (search.Apps.Count > 0)
        {
            sb.AppendLine($"- Ứng dụng liên quan ({search.Apps.Count}): {string.Join(", ", search.Apps.Select(a => a.Title))}");
        }
        return (sb.ToString().Trim(), citations);
    }

    private async Task<(string Text, List<AiCopilotCitationDto> Citations)> RunPendingApprovalsToolAsync(
        SysUserDto user, string? appCodeFilter, CancellationToken ct)
    {
        var apps = (await _metadata.GetAppsAsync())
            .Where(a => AuthService.CanAccessApp(user, a.Code))
            .Where(a => string.IsNullOrWhiteSpace(appCodeFilter) || a.Code.Equals(appCodeFilter, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        if (apps.Count == 0)
            return ("Bạn chưa có quyền truy cập ứng dụng nào phù hợp để xem hồ sơ chờ duyệt.", new());

        var tablesById = (await _metadata.GetTablesAsync(null, null))
            .Where(t => apps.ContainsKey(t.AppId) && AuthService.CanAccessTable(user, apps[t.AppId].Code, t.Id, "view"))
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var citations = new List<AiCopilotCitationDto>();
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, app_id, table_id, COALESCE(record_key, id), current_step_order, updated_at, created_by
            FROM app_records
            WHERE deleted_at IS NULL AND approval_status = 'PENDING'
            ORDER BY updated_at DESC
            LIMIT 40
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var recId = r.GetString(0);
            var appId = r.GetString(1);
            var tableId = r.GetString(2);
            if (!apps.TryGetValue(appId, out var app) || !tablesById.TryGetValue(tableId, out var tbl)) continue;

            var rowScope = AuthService.ResolveRowScope(user, app.Code, tbl.Id, tbl.RowScope, false);
            if (rowScope != "ALL" && !await _records.IsRecordOwnedByUserAsync(recId, user, rowScope))
                continue;

            var key = r.GetString(3);
            var step = r.IsDBNull(4) ? 1 : r.GetInt32(4);
            var createdBy = r.IsDBNull(6) ? "" : r.GetString(6);
            citations.Add(new AiCopilotCitationDto
            {
                Kind = "APPROVAL",
                Id = recId,
                Title = $"{key} · {tbl.DisplayName}",
                Subtitle = $"App {app.Name} · Bước {step} · Người tạo: {createdBy}",
                Url = $"/apps/{app.Code}?tableId={tbl.Id}&recordId={recId}",
                Badge = "CHỜ DUYỆT"
            });
            if (citations.Count >= 8) break;
        }

        if (citations.Count == 0)
            return ("Hiện **không có hồ sơ nào đang chờ phê duyệt** trong phạm vi quyền của bạn.", citations);

        var sb = new StringBuilder();
        sb.AppendLine($"### Hồ sơ đang chờ phê duyệt ({citations.Count})");
        foreach (var c in citations)
            sb.AppendLine($"- **{c.Title}** — {c.Subtitle}");
        return (sb.ToString().Trim(), citations);
    }

    private async Task<(string Text, List<AiCopilotCitationDto> Citations)> RunMyTasksToolAsync(SysUserDto user)
    {
        var tasks = await _tasks.ListTasksAsync(user.Username, scope: "assigned", ownerOnly: true);
        var todayIso = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var openTasks = tasks
            .Where(t => !string.Equals(t.Status, "DONE", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => string.IsNullOrWhiteSpace(t.DueDate) ? "9999-12-31" : t.DueDate)
            .Take(8)
            .ToList();

        var citations = openTasks.Select(t =>
        {
            var isOverdue = !string.IsNullOrWhiteSpace(t.DueDate) && string.CompareOrdinal(t.DueDate, todayIso) < 0;
            return new AiCopilotCitationDto
            {
                Kind = "TASK",
                Id = t.Id,
                Title = $"{t.TaskCode} · {t.Title}",
                Subtitle = $"Trạng thái: {t.Status} · Ưu tiên: {t.Priority}" + (!string.IsNullOrWhiteSpace(t.DueDate) ? $" · Hạn: {t.DueDate}" : ""),
                Url = "/tasks",
                Badge = isOverdue ? "QUÁ HẠN" : t.Status
            };
        }).ToList();

        if (citations.Count == 0)
            return ("Bạn hiện **không có công việc mở nào** đang được giao.", citations);

        var overdueCnt = openTasks.Count(t => !string.IsNullOrWhiteSpace(t.DueDate) && string.CompareOrdinal(t.DueDate, todayIso) < 0);
        var sb = new StringBuilder();
        sb.AppendLine($"### Công việc của bạn ({citations.Count} đang mở, {overdueCnt} quá hạn)");
        foreach (var c in citations)
            sb.AppendLine($"- **{c.Title}** — {c.Subtitle}");
        return (sb.ToString().Trim(), citations);
    }

    private async Task<(string Text, List<AiCopilotCitationDto> Citations)> RunAppStatsToolAsync(
        SysUserDto user, string? appCodeFilter, CancellationToken ct)
    {
        var apps = (await _metadata.GetAppsAsync())
            .Where(a => AuthService.CanAccessApp(user, a.Code))
            .Where(a => string.IsNullOrWhiteSpace(appCodeFilter) || a.Code.Equals(appCodeFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (apps.Count == 0)
            return ("Không có ứng dụng nào trong phạm vi quyền truy cập.", new());

        var countsByApp = new Dictionary<string, (long Total, long Pending)>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT app_id,
                   COUNT(*) AS total_cnt,
                   COUNT(*) FILTER (WHERE approval_status = 'PENDING') AS pending_cnt
            FROM app_records
            WHERE deleted_at IS NULL
            GROUP BY app_id
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            countsByApp[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2));
        }

        var citations = new List<AiCopilotCitationDto>();
        var sb = new StringBuilder();
        long sumTotal = 0;
        long sumPending = 0;
        sb.AppendLine($"### Tổng quan dữ liệu ({apps.Count} ứng dụng được cấp quyền)");
        foreach (var app in apps.Take(10))
        {
            var (tot, pend) = countsByApp.TryGetValue(app.Id, out var v) ? v : (0L, 0L);
            sumTotal += tot;
            sumPending += pend;
            sb.AppendLine($"- **{app.Name} (`{app.Code}`)**: {tot} bản ghi ({pend} chờ duyệt)");
            citations.Add(new AiCopilotCitationDto
            {
                Kind = "APP",
                Id = app.Id,
                Title = app.Name,
                Subtitle = $"{tot} bản ghi · {pend} chờ duyệt · {app.Department}",
                Url = $"/apps/{app.Code}",
                Badge = app.Code
            });
        }
        sb.AppendLine($"\n**Tổng cộng:** {sumTotal} bản ghi hoạt động, {sumPending} hồ sơ đang chờ duyệt.");
        return (sb.ToString().Trim(), citations);
    }

    private async Task<(string Text, AiCopilotProposedActionDto? Proposal)> RunDraftProposalToolAsync(
        SysUserDto user, string rawMessage, string? appCodeHint, string? tableIdHint)
    {
        var apps = (await _metadata.GetAppsAsync())
            .Where(a => AuthService.CanAccessApp(user, a.Code))
            .ToList();

        SysAppDto? targetApp = null;
        if (!string.IsNullOrWhiteSpace(appCodeHint))
            targetApp = apps.FirstOrDefault(a => a.Code.Equals(appCodeHint, StringComparison.OrdinalIgnoreCase));

        var normMsg = RecordService.NormalizeVietnamese(rawMessage);
        if (targetApp == null)
        {
            targetApp = apps.FirstOrDefault(a =>
                normMsg.Contains(RecordService.NormalizeVietnamese(a.Code), StringComparison.Ordinal) ||
                normMsg.Contains(RecordService.NormalizeVietnamese(a.Name), StringComparison.Ordinal));
        }
        targetApp ??= apps.FirstOrDefault();
        if (targetApp == null)
            return ("Không tìm thấy ứng dụng nào bạn có quyền tạo mới dữ liệu.", null);

        var tables = await _metadata.GetTablesAsync(targetApp.Id, null);
        SysTableDto? targetTable = null;
        if (!string.IsNullOrWhiteSpace(tableIdHint))
            targetTable = tables.FirstOrDefault(t => t.Id.Equals(tableIdHint, StringComparison.OrdinalIgnoreCase));
        targetTable ??= tables.FirstOrDefault(t => AuthService.CanAccessTable(user, targetApp.Code, t.Id, "create"));
        if (targetTable == null || !AuthService.CanAccessTable(user, targetApp.Code, targetTable.Id, "create"))
            return ($"Bạn chưa có quyền tạo mới bản ghi trong ứng dụng **{targetApp.Name}**.", null);

        var cols = await _metadata.GetColumnsAsync(targetTable.Id);
        var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
        var isAdmin = AuthService.IsAdminUser(user);
        var draft = new Dictionary<string, object?>();

        var cleanedTitle = Regex.Replace(rawMessage, @"(?i)^(hãy\s+|vui lòng\s+)?(tạo mới|tạo hồ sơ|tạo phiếu|lập phiếu|lập văn bản|đề xuất tạo|tạo yêu cầu|create record)\s*", "").Trim();
        if (string.IsNullOrWhiteSpace(cleanedTitle)) cleanedTitle = $"Đề xuất từ AI Copilot ({DateTime.Now:dd/MM/yyyy})";

        bool titleAssigned = false;
        foreach (var col in cols)
        {
            if (col.DataType is "SUB_TABLE" or "AUTO_NUMBER" or "FILE" or "IMAGE") continue;
            if (!string.IsNullOrWhiteSpace(col.Formula)) continue;
            if (!isAdmin && col.HiddenForRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase))) continue;
            if (!isAdmin && col.ReadOnlyForRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase))) continue;

            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                draft[col.ColumnCode] = col.DefaultValue;
            }
            else if (!titleAssigned && col.DataType is "STRING" or "TEXTAREA" && !col.IsIdentityKey)
            {
                draft[col.ColumnCode] = cleanedTitle;
                titleAssigned = true;
            }
            else if (col.IsRequired && col.DataType == "DATE")
            {
                draft[col.ColumnCode] = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            else if (col.IsRequired && col.DataType is "STRING" or "TEXTAREA")
            {
                draft[col.ColumnCode] = col.IsIdentityKey
                    ? $"AI-{DateTime.UtcNow:yyyyMMddHHmm}"
                    : cleanedTitle;
            }
            else if (col.IsRequired && col.DataType is "NUMBER" or "CURRENCY")
            {
                draft[col.ColumnCode] = 0;
            }
        }

        var proposal = new AiCopilotProposedActionDto
        {
            ActionType = "CREATE_RECORD",
            Label = $"Tạo bản ghi mới trong {targetTable.DisplayName}",
            AppId = targetApp.Id,
            AppCode = targetApp.Code,
            ModuleId = targetTable.ModuleId,
            TableId = targetTable.Id,
            TableDisplayName = targetTable.DisplayName,
            DraftData = draft,
            Note = "Bản ghi chưa được lưu vào CSDL. Hãy kiểm tra và bấm Xác nhận để ghi nhận theo đúng quy tắc kiểm duyệt."
        };

        return ($"### Đề xuất tạo hồ sơ mới (Chờ xác nhận)\nMình đã soạn sẵn bản nháp cho bảng **{targetTable.DisplayName}** (ứng dụng `{targetApp.Code}`). Hãy kiểm tra nội dung bên dưới và bấm **Xác nhận tạo bản ghi**.", proposal);
    }

    private static bool ContainsAny(string normText, params string[] needles) =>
        needles.Any(n => normText.Contains(n, StringComparison.Ordinal));

    private static string ExtractSearchKeyword(string raw)
    {
        var cleaned = Regex.Replace(
            raw.Trim(),
            @"(?i)^(hãy\s+|vui lòng\s+)?(tìm kiếm|tra cứu|tìm giúp tôi|tìm giúp|tìm|search|liệt kê|xem)\s*(hồ sơ|bản ghi|văn bản|công văn|hợp đồng|phiếu|dữ liệu)?\s*(có\s+|chứa\s+|tên\s+|mã\s+)?",
            "").Trim(' ', '?', '.', '"', '\'');
        return string.IsNullOrWhiteSpace(cleaned) ? raw.Trim() : cleaned;
    }
}
