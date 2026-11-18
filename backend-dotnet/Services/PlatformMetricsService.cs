using System.Globalization;
using System.Text;
using Npgsql;

namespace SmartApps.Api.Services;

/// <summary>
/// Thread-safe lightweight runtime observability metrics collector for Prometheus /metrics & JSON diagnostics.
/// </summary>
public sealed class PlatformMetricsService
{
    private long _httpRequestsTotal;
    private long _httpErrors4xx;
    private long _httpErrors5xx;
    private long _httpDurationMsTotal;
    private readonly string _connStr;
    private readonly RealtimeEventService _realtime;

    public PlatformMetricsService(IConfiguration config, RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _realtime = realtime;
    }

    public void RecordHttpRequest(int statusCode, long elapsedMs)
    {
        Interlocked.Increment(ref _httpRequestsTotal);
        Interlocked.Add(ref _httpDurationMsTotal, Math.Max(0, elapsedMs));
        if (statusCode >= 500) Interlocked.Increment(ref _httpErrors5xx);
        else if (statusCode >= 400) Interlocked.Increment(ref _httpErrors4xx);
    }

    public async Task<PlatformMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        long outboxPending = 0;
        long outboxDeadLetter = 0;
        long aiTokensMonth = 0;
        long totalRecords = 0;
        double dbLatencyMs = -1;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM app_outbox_events WHERE status = 'PENDING'),
                    (SELECT COUNT(*) FROM app_outbox_events WHERE status = 'DEAD_LETTER'),
                    (SELECT COALESCE(SUM(prompt_tokens + completion_tokens), 0) FROM sys_ai_logs WHERE status = 'SUCCESS' AND created_at >= date_trunc('month', CURRENT_TIMESTAMP)),
                    (SELECT COUNT(*) FROM app_records WHERE deleted_at IS NULL)
                """, conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                outboxPending = r.IsDBNull(0) ? 0 : r.GetInt64(0);
                outboxDeadLetter = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                aiTokensMonth = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                totalRecords = r.IsDBNull(3) ? 0 : r.GetInt64(3);
            }
            sw.Stop();
            dbLatencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        }
        catch
        {
            dbLatencyMs = -1;
        }

        return new PlatformMetricsSnapshot
        {
            HttpRequestsTotal = Interlocked.Read(ref _httpRequestsTotal),
            HttpErrors4xx = Interlocked.Read(ref _httpErrors4xx),
            HttpErrors5xx = Interlocked.Read(ref _httpErrors5xx),
            HttpAvgLatencyMs = _httpRequestsTotal > 0
                ? Math.Round(Interlocked.Read(ref _httpDurationMsTotal) / (double)Math.Max(1, Interlocked.Read(ref _httpRequestsTotal)), 2)
                : 0,
            ActiveSseSubscribers = _realtime.ActiveSubscriberCount,
            OutboxPending = outboxPending,
            OutboxDeadLetter = outboxDeadLetter,
            AiTokensUsedMonth = aiTokensMonth,
            ActiveRecordsTotal = totalRecords,
            DbProbeLatencyMs = dbLatencyMs
        };
    }

    public static string ToPrometheusText(PlatformMetricsSnapshot m)
    {
        var sb = new StringBuilder();
        void Gauge(string name, string help, object val)
        {
            sb.AppendLine($"# HELP {name} {help}");
            sb.AppendLine($"# TYPE {name} gauge");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name} {val}"));
        }
        void Counter(string name, string help, long val)
        {
            sb.AppendLine($"# HELP {name} {help}");
            sb.AppendLine($"# TYPE {name} counter");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name} {val}"));
        }

        Counter("smartapps_http_requests_total", "Total HTTP requests processed", m.HttpRequestsTotal);
        Counter("smartapps_http_errors_4xx_total", "Total 4xx HTTP responses", m.HttpErrors4xx);
        Counter("smartapps_http_errors_5xx_total", "Total 5xx HTTP responses", m.HttpErrors5xx);
        Gauge("smartapps_http_avg_latency_ms", "Average HTTP latency in ms", m.HttpAvgLatencyMs);
        Gauge("smartapps_sse_subscribers_active", "Active real-time SSE subscriber streams", m.ActiveSseSubscribers);
        Gauge("smartapps_outbox_pending", "Pending webhook outbox deliveries", m.OutboxPending);
        Gauge("smartapps_outbox_dead_letter", "Dead-letter webhook outbox events", m.OutboxDeadLetter);
        Gauge("smartapps_ai_tokens_month", "AI tokens consumed in the current calendar month", m.AiTokensUsedMonth);
        Gauge("smartapps_records_active_total", "Total non-deleted dynamic application records", m.ActiveRecordsTotal);
        Gauge("smartapps_db_probe_latency_ms", "PostgreSQL telemetry query latency in ms", m.DbProbeLatencyMs);
        return sb.ToString();
    }
}

public sealed class PlatformMetricsSnapshot
{
    public long HttpRequestsTotal { get; set; }
    public long HttpErrors4xx { get; set; }
    public long HttpErrors5xx { get; set; }
    public double HttpAvgLatencyMs { get; set; }
    public int ActiveSseSubscribers { get; set; }
    public long OutboxPending { get; set; }
    public long OutboxDeadLetter { get; set; }
    public long AiTokensUsedMonth { get; set; }
    public long ActiveRecordsTotal { get; set; }
    public double DbProbeLatencyMs { get; set; }
}
