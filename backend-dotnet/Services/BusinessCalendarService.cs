using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

/// <summary>
/// Vietnam business calendar (UTC+7, Mon-Fri, configurable fixed and recurring holidays)
/// used for accurate SLA elapsed business hours and reminder scheduling.
/// </summary>
public sealed class BusinessCalendarService
{
    private readonly string _connStr;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public BusinessCalendarService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
    }

    public async Task<List<BusinessHolidayDto>> ListHolidaysAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, TO_CHAR(holiday_date, 'YYYY-MM-DD'), name, is_recurring
            FROM sys_business_holidays
            ORDER BY holiday_date ASC
            """, conn);
        var list = new List<BusinessHolidayDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new BusinessHolidayDto
            {
                Id = r.GetString(0),
                HolidayDate = r.GetString(1),
                Name = r.GetString(2),
                IsRecurring = r.GetBoolean(3)
            });
        }
        return list;
    }

    public async Task<(BusinessHolidayDto? Item, string? Error)> UpsertHolidayAsync(UpsertBusinessHolidayRequest req)
    {
        if (!DateOnly.TryParseExact(req.HolidayDate?.Trim(), "yyyy-MM-dd", out var dt))
            return (null, "Ngày nghỉ lễ không đúng định dạng yyyy-MM-dd.");
        if (string.IsNullOrWhiteSpace(req.Name))
            return (null, "Tên ngày nghỉ lễ là bắt buộc.");

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"hol_{Guid.NewGuid().ToString("N")[..8]}" : req.Id.Trim();
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_business_holidays (id, holiday_date, name, is_recurring)
            VALUES (@id, @dt, @nm, @rec)
            ON CONFLICT (holiday_date) DO UPDATE SET
                name = EXCLUDED.name,
                is_recurring = EXCLUDED.is_recurring
            RETURNING id, TO_CHAR(holiday_date, 'YYYY-MM-DD'), name, is_recurring
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("dt", dt.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("nm", req.Name.Trim());
        cmd.Parameters.AddWithValue("rec", req.IsRecurring);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (new BusinessHolidayDto
        {
            Id = r.GetString(0),
            HolidayDate = r.GetString(1),
            Name = r.GetString(2),
            IsRecurring = r.GetBoolean(3)
        }, null);
    }

    public async Task<bool> DeleteHolidayAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_business_holidays WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(HashSet<DateOnly> Exact, HashSet<(int Month, int Day)> Recurring)> LoadHolidaySetsAsync(CancellationToken ct = default)
    {
        var items = await ListHolidaysAsync(ct);
        var exact = new HashSet<DateOnly>();
        var recurring = new HashSet<(int Month, int Day)>();
        foreach (var h in items)
        {
            if (!DateOnly.TryParseExact(h.HolidayDate, "yyyy-MM-dd", out var d)) continue;
            if (h.IsRecurring) recurring.Add((d.Month, d.Day));
            else exact.Add(d);
        }
        return (exact, recurring);
    }

    public static DateTime ToVietnamTime(DateTime utcOrUnspecified)
    {
        var utc = utcOrUnspecified.Kind == DateTimeKind.Local
            ? utcOrUnspecified.ToUniversalTime()
            : DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc);
        return utc + VietnamOffset;
    }

    public static bool IsVietnamWorkingDate(
        DateOnly vnDate,
        IReadOnlySet<DateOnly>? exactHolidays = null,
        IReadOnlySet<(int Month, int Day)>? recurringHolidays = null)
    {
        if (vnDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        if (exactHolidays != null && exactHolidays.Contains(vnDate))
            return false;
        if (recurringHolidays != null && recurringHolidays.Contains((vnDate.Month, vnDate.Day)))
            return false;
        return true;
    }

    /// <summary>
    /// Computes elapsed working hours (24h per VN working day Mon-Fri excluding public holidays)
    /// between startUtc and endUtc so weekend/holiday delays do not trigger false SLA escalations.
    /// </summary>
    public static double CalculateBusinessElapsedHours(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlySet<DateOnly>? exactHolidays = null,
        IReadOnlySet<(int Month, int Day)>? recurringHolidays = null)
    {
        var startVn = ToVietnamTime(startUtc);
        var endVn = ToVietnamTime(endUtc);
        if (endVn <= startVn) return 0d;

        var totalHours = 0d;
        var cursor = startVn;
        while (cursor < endVn)
        {
            var nextMidnight = cursor.Date.AddDays(1);
            var segEnd = endVn < nextMidnight ? endVn : nextMidnight;
            var dayOnly = DateOnly.FromDateTime(cursor);
            if (IsVietnamWorkingDate(dayOnly, exactHolidays, recurringHolidays))
            {
                totalHours += (segEnd - cursor).TotalHours;
            }
            cursor = nextMidnight;
        }
        return totalHours;
    }
}
