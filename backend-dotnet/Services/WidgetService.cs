using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class WidgetService
{
    private readonly string _connStr;
    private readonly MetadataService _metadata;
    private readonly RecordService _records;

    private static readonly string[] ChartPalette =
    [
        "#1a73e8", "#1e8e3e", "#ea8600", "#7b1fa2", "#00897b", "#d93025", "#0284c7"
    ];

    public WidgetService(IConfiguration config, MetadataService metadata, RecordService records)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _metadata = metadata;
        _records = records;
    }

    public async Task<List<AppWidgetDto>> GetAppWidgetsWithDataAsync(
        string appId,
        bool includeHidden = false,
        string? fromDate = null,
        string? toDate = null,
        SysUserDto? user = null,
        bool personalMode = false)
    {
        DateTime? fromTs = DateTime.TryParse(fromDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fd)
            ? DateTime.SpecifyKind(fd.Date, DateTimeKind.Utc)
            : null;
        DateTime? toTs = DateTime.TryParse(toDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var td)
            ? DateTime.SpecifyKind(td.Date.AddDays(1), DateTimeKind.Utc)
            : null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT w.id, w.app_id, ap.code, w.table_id, t.display_name, w.title, w.widget_type,
                   w.metric_field, w.group_field, w.filter_field, w.filter_value,
                   w.col_span, w.color, w.order_index, w.is_visible, COALESCE(t.row_scope, 'ALL')
            FROM app_widgets w
            LEFT JOIN sys_tables t ON t.id = w.table_id
            LEFT JOIN sys_apps ap ON ap.id = w.app_id
            WHERE w.app_id = @appId
              AND (@incHidden = TRUE OR w.is_visible = TRUE)
            ORDER BY w.order_index ASC, w.created_at ASC
            """;

        var rawList = new List<(AppWidgetDto Widget, string TableRowScope)>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("appId", appId);
            cmd.Parameters.AddWithValue("incHidden", includeHidden);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                rawList.Add((new AppWidgetDto
                {
                    Id = r.GetString(0),
                    AppId = r.GetString(1),
                    AppCode = r.IsDBNull(2) ? null : r.GetString(2),
                    TableId = r.GetString(3),
                    TableDisplayName = r.IsDBNull(4) ? null : r.GetString(4),
                    Title = r.GetString(5),
                    WidgetType = r.GetString(6),
                    MetricField = r.IsDBNull(7) ? null : r.GetString(7),
                    GroupField = r.IsDBNull(8) ? null : r.GetString(8),
                    FilterField = r.IsDBNull(9) ? null : r.GetString(9),
                    FilterValue = r.IsDBNull(10) ? null : r.GetString(10),
                    ColSpan = Math.Clamp(r.GetInt32(11), 1, 4),
                    Color = r.IsDBNull(12) ? "#1a73e8" : r.GetString(12),
                    OrderIndex = r.GetInt32(13),
                    IsVisible = r.GetBoolean(14)
                }, r.IsDBNull(15) ? "ALL" : r.GetString(15)));
            }
        }

        var colsByTable = new Dictionary<string, List<SysColumnDto>>();
        var visibleList = new List<AppWidgetDto>();
        var userRoles = user == null
            ? new List<string>()
            : (user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode });
        var isAdmin = AuthService.IsAdminUser(user);

        foreach (var (w, tblRowScope) in rawList)
        {
            if (user != null && !AuthService.CanAccessTable(user, w.AppCode, w.TableId, "view"))
                continue;

            if (!colsByTable.TryGetValue(w.TableId, out var cols))
            {
                cols = await _metadata.GetColumnsAsync(w.TableId);
                colsByTable[w.TableId] = cols;
            }

            // Hide widgets referencing fields hidden for this user's role (e.g., salary KPIs for STAFF)
            if (user != null && !isAdmin)
            {
                var hiddenCodes = cols
                    .Where(c => c.HiddenForRoles.Any(hr => userRoles.Contains(hr, StringComparer.OrdinalIgnoreCase)))
                    .Select(c => c.ColumnCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if ((!string.IsNullOrWhiteSpace(w.MetricField) && hiddenCodes.Contains(w.MetricField))
                    || (!string.IsNullOrWhiteSpace(w.GroupField) && hiddenCodes.Contains(w.GroupField))
                    || (!string.IsNullOrWhiteSpace(w.FilterField) && hiddenCodes.Contains(w.FilterField)))
                {
                    continue;
                }
            }

            var rowScope = user != null ? AuthService.ResolveRowScope(user, w.AppCode, w.TableId, tblRowScope, personalMode) : "ALL";
            var ownerUser = rowScope != "ALL" ? user : null;
            var ownerDept = rowScope == "DEPARTMENT"
                ? (string.IsNullOrWhiteSpace(user?.Department) ? "__UNASSIGNED__" : user!.Department)
                : null;

            var hasFilter = !string.IsNullOrWhiteSpace(w.FilterField) && !string.IsNullOrWhiteSpace(w.FilterValue);
            var wt = w.WidgetType.ToUpperInvariant();

            if (wt == "KPI_COUNT")
            {
                var countSql = $"""
                    SELECT COUNT(*) FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL
                      AND (@ff IS NULL OR LOWER(COALESCE(data ->> @ff, '')) = LOWER(@fv))
                      AND (@fromD IS NULL OR created_at >= @fromD)
                      AND (@toD IS NULL OR created_at < @toD)
                      AND {RecordService.OwnerFilterSqlClause}
                    """;
                await using var cmd = new NpgsqlCommand(countSql, conn);
                cmd.Parameters.AddWithValue("tbl", w.TableId);
                AddWidgetFilters(cmd, hasFilter, w.FilterField, w.FilterValue, fromTs, toTs, ownerUser, ownerDept);
                var cnt = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                w.NumericValue = cnt;
                w.FormattedValue = cnt.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
                w.SubLabel = hasFilter ? $"Lọc: {w.FilterField} = {w.FilterValue}" : $"Trên bảng {w.TableDisplayName}";
            }
            else if (wt is "KPI_SUM" or "KPI_AVG")
            {
                var col = cols.FirstOrDefault(c => c.ColumnCode == w.MetricField);
                var aggSql = $"""
                    SELECT COALESCE(SUM((data ->> @mf)::numeric), 0),
                           COALESCE(AVG((data ->> @mf)::numeric), 0),
                           COUNT(*)
                    FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL
                      AND (@ff IS NULL OR LOWER(COALESCE(data ->> @ff, '')) = LOWER(@fv))
                      AND (@fromD IS NULL OR created_at >= @fromD)
                      AND (@toD IS NULL OR created_at < @toD)
                      AND {RecordService.OwnerFilterSqlClause}
                      AND COALESCE(data ->> @mf, '') ~ '^-?[0-9]+(\.[0-9]+)?$'
                    """;
                await using var cmd = new NpgsqlCommand(aggSql, conn);
                cmd.Parameters.AddWithValue("tbl", w.TableId);
                cmd.Parameters.AddWithValue("mf", w.MetricField ?? "");
                AddWidgetFilters(cmd, hasFilter, w.FilterField, w.FilterValue, fromTs, toTs, ownerUser, ownerDept);

                decimal sum = 0m, avg = 0m;
                long matched = 0;
                await using (var r = await cmd.ExecuteReaderAsync())
                {
                    if (await r.ReadAsync())
                    {
                        sum = r.GetDecimal(0);
                        avg = Math.Round(r.GetDecimal(1), 0);
                        matched = r.GetInt64(2);
                    }
                }
                var val = wt == "KPI_SUM" ? sum : avg;
                w.NumericValue = val;
                var isCurrency = col?.DataType.Equals("CURRENCY", StringComparison.OrdinalIgnoreCase) == true;
                w.FormattedValue = isCurrency
                    ? $"{val.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} ₫"
                    : val.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
                w.SubLabel = $"{(wt == "KPI_SUM" ? "Tổng" : "Trung bình")} {col?.ColumnName ?? w.MetricField} ({matched} bản ghi)";
            }
            else if (wt is "BAR_CHART" or "DONUT_CHART")
            {
                var col = cols.FirstOrDefault(c => c.ColumnCode == w.GroupField);
                var catMap = new Dictionary<string, (string Name, string Color)>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(col?.RefCategoryCode))
                {
                    var catItems = await _metadata.GetCategoryItemsAsync(col.RefCategoryCode);
                    foreach (var ci in catItems) catMap[ci.ItemCode] = (ci.ItemName, ci.Color ?? "#1a73e8");
                }

                var grpSql = $"""
                    SELECT COALESCE(NULLIF(TRIM(data ->> @gf), ''), '(Khác)') AS grp_key, COUNT(*) AS cnt
                    FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL
                      AND (@ff IS NULL OR LOWER(COALESCE(data ->> @ff, '')) = LOWER(@fv))
                      AND (@fromD IS NULL OR created_at >= @fromD)
                      AND (@toD IS NULL OR created_at < @toD)
                      AND {RecordService.OwnerFilterSqlClause}
                    GROUP BY grp_key
                    ORDER BY cnt DESC
                    LIMIT 25
                    """;
                await using var cmd = new NpgsqlCommand(grpSql, conn);
                cmd.Parameters.AddWithValue("tbl", w.TableId);
                cmd.Parameters.AddWithValue("gf", w.GroupField ?? "");
                AddWidgetFilters(cmd, hasFilter, w.FilterField, w.FilterValue, fromTs, toTs, ownerUser, ownerDept);

                int colorIdx = 0;
                long totalCount = 0;
                await using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var key = r.GetString(0);
                        var cnt = Convert.ToInt32(r.GetInt64(1));
                        totalCount += cnt;

                        var label = key;
                        var color = ChartPalette[colorIdx++ % ChartPalette.Length];
                        if (col?.DataType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            label = key.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Có / Đúng" : "Không / Sai";
                            color = key.Equals("true", StringComparison.OrdinalIgnoreCase) ? "#1e8e3e" : "#ea8600";
                        }
                        else if (catMap.TryGetValue(key, out var info))
                        {
                            label = info.Name;
                            color = info.Color;
                        }
                        w.ChartItems.Add(new CategoryDistributionItem { Code = key, Label = label, Color = color, Count = cnt });
                    }
                }
                w.NumericValue = totalCount;
                w.FormattedValue = totalCount.ToString();
                w.SubLabel = $"Nhóm theo {col?.ColumnName ?? w.GroupField}";
            }
            else if (wt == "LINE_CHART")
            {
                var lineSql = $$"""
                    SELECT TO_CHAR(
                             CASE
                               WHEN @gf <> '' AND COALESCE(data ->> @gf, '') ~ '^\d{4}-\d{2}-\d{2}'
                                 THEN (data ->> @gf)::date
                               ELSE created_at::date
                             END,
                             'YYYY-MM-DD'
                           ) AS day_key,
                           COUNT(*) AS cnt
                    FROM app_records
                    WHERE table_id = @tbl AND deleted_at IS NULL
                      AND (@ff IS NULL OR LOWER(COALESCE(data ->> @ff, '')) = LOWER(@fv))
                      AND (@fromD IS NULL OR created_at >= @fromD)
                      AND (@toD IS NULL OR created_at < @toD)
                      AND {{RecordService.OwnerFilterSqlClause}}
                    GROUP BY day_key
                    ORDER BY day_key ASC
                    LIMIT 31
                    """;
                await using var cmd = new NpgsqlCommand(lineSql, conn);
                cmd.Parameters.AddWithValue("tbl", w.TableId);
                cmd.Parameters.AddWithValue("gf", w.GroupField ?? "");
                AddWidgetFilters(cmd, hasFilter, w.FilterField, w.FilterValue, fromTs, toTs, ownerUser, ownerDept);

                long totalCount = 0;
                await using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var key = r.GetString(0);
                        var cnt = Convert.ToInt32(r.GetInt64(1));
                        totalCount += cnt;
                        w.ChartItems.Add(new CategoryDistributionItem
                        {
                            Code = key,
                            Label = key.Length >= 10 ? $"{key[8..10]}/{key[5..7]}" : key,
                            Color = w.Color,
                            Count = cnt
                        });
                    }
                }
                w.NumericValue = totalCount;
                w.FormattedValue = totalCount.ToString();
                w.SubLabel = string.IsNullOrWhiteSpace(w.GroupField) ? "Xu hướng theo ngày tạo" : $"Xu hướng theo {w.GroupField}";
            }
            else if (wt == "RECENT_LIST")
            {
                var (items, total) = await _records.QueryRecordsAsync(
                    null, null, w.TableId, null, "ALL", 1, 5,
                    ownerUser?.Username, null, null, "DESC",
                    ownerUser?.Email, ownerUser?.EmployeeCode);
                w.RecentRows = items;
                w.NumericValue = total;
                w.FormattedValue = total.ToString();
                w.SubLabel = $"5 bản ghi gần nhất • {w.TableDisplayName}";
            }

            visibleList.Add(w);
        }

        return visibleList;
    }

    public async Task<List<AppWidgetDto>> GetPortalKpisAsync(SysUserDto user)
    {
        var apps = (await _metadata.GetAppsAsync())
            .Where(a => AuthService.CanAccessApp(user, a.Code))
            .ToList();

        var result = new List<AppWidgetDto>();
        foreach (var app in apps)
        {
            var appWidgets = await GetAppWidgetsWithDataAsync(app.Id, false, null, null, user, false);
            var kpis = appWidgets.Where(w => w.WidgetType.StartsWith("KPI_", StringComparison.OrdinalIgnoreCase)).Take(2);
            result.AddRange(kpis);
        }
        return result;
    }

    private static void AddWidgetFilters(
        NpgsqlCommand cmd,
        bool hasFilter,
        string? filterField,
        string? filterValue,
        DateTime? fromTs,
        DateTime? toTs,
        SysUserDto? ownerUser = null,
        string? ownerDepartment = null)
    {
        cmd.Parameters.Add(new NpgsqlParameter("ff", NpgsqlDbType.Varchar) { Value = hasFilter ? filterField! : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("fv", NpgsqlDbType.Varchar) { Value = hasFilter ? filterValue! : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("fromD", NpgsqlDbType.TimestampTz) { Value = fromTs.HasValue ? fromTs.Value : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("toD", NpgsqlDbType.TimestampTz) { Value = toTs.HasValue ? toTs.Value : DBNull.Value });
        RecordService.AddOwnerFilterParams(cmd, ownerUser?.Username, ownerUser?.Email, ownerUser?.EmployeeCode, ownerDepartment);
    }

    public async Task<AppWidgetDto> UpsertWidgetAsync(UpsertWidgetRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"wdg_{Guid.NewGuid().ToString("N")[..8]}" : req.Id.Trim();
        var sql = """
            INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field,
                filter_field, filter_value, col_span, color, order_index, is_visible)
            VALUES (@id, @app, @tbl, @title, @wt, @mf, @gf, @ff, @fv, @span, @col, @ord, @vis)
            ON CONFLICT (id) DO UPDATE SET
                table_id = EXCLUDED.table_id,
                title = EXCLUDED.title,
                widget_type = EXCLUDED.widget_type,
                metric_field = EXCLUDED.metric_field,
                group_field = EXCLUDED.group_field,
                filter_field = EXCLUDED.filter_field,
                filter_value = EXCLUDED.filter_value,
                col_span = EXCLUDED.col_span,
                color = EXCLUDED.color,
                order_index = EXCLUDED.order_index,
                is_visible = EXCLUDED.is_visible
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("app", req.AppId);
        cmd.Parameters.AddWithValue("tbl", req.TableId);
        cmd.Parameters.AddWithValue("title", req.Title.Trim());
        cmd.Parameters.AddWithValue("wt", req.WidgetType.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("mf", string.IsNullOrWhiteSpace(req.MetricField) ? DBNull.Value : req.MetricField.Trim());
        cmd.Parameters.AddWithValue("gf", string.IsNullOrWhiteSpace(req.GroupField) ? DBNull.Value : req.GroupField.Trim());
        cmd.Parameters.AddWithValue("ff", string.IsNullOrWhiteSpace(req.FilterField) ? DBNull.Value : req.FilterField.Trim());
        cmd.Parameters.AddWithValue("fv", string.IsNullOrWhiteSpace(req.FilterValue) ? DBNull.Value : req.FilterValue.Trim());
        cmd.Parameters.AddWithValue("span", Math.Clamp(req.ColSpan, 1, 4));
        cmd.Parameters.AddWithValue("col", string.IsNullOrWhiteSpace(req.Color) ? "#1a73e8" : req.Color.Trim());
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("vis", req.IsVisible);
        await cmd.ExecuteNonQueryAsync();

        return (await GetAppWidgetsWithDataAsync(req.AppId, true)).First(w => w.Id == id);
    }

    public async Task SaveLayoutOrderAsync(List<WidgetLayoutOrderItem> items)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        foreach (var it in items)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE app_widgets SET order_index = @ord, col_span = @span, is_visible = @vis WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("ord", it.OrderIndex);
            cmd.Parameters.AddWithValue("span", Math.Clamp(it.ColSpan, 1, 4));
            cmd.Parameters.AddWithValue("vis", it.IsVisible);
            cmd.Parameters.AddWithValue("id", it.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<bool> DeleteWidgetAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM app_widgets WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
