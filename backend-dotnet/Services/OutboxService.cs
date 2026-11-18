using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SmartApps.Api.Services;

public sealed class OutboxEventDto
{
    public string Id { get; set; } = "";
    public string EventType { get; set; } = "";
    public string AggregateType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Transactional outbox dispatcher with SKIP LOCKED claiming, exponential backoff retry,
/// dead-lettering, and idempotency delivery headers for outbound webhooks.
/// </summary>
public sealed class OutboxService
{
    private readonly string _connStr;
    private readonly IHttpClientFactory _httpFactory;

    public OutboxService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _httpFactory = httpFactory;
    }

    public async Task<string> EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string eventType,
        string aggregateType,
        string aggregateId,
        object payload,
        int maxAttempts = 5)
    {
        var id = $"obx_{Guid.NewGuid().ToString("N")[..12]}";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_outbox_events (id, event_type, aggregate_type, aggregate_id, payload, status, max_attempts)
            VALUES (@id, @ev, @at, @aid, @pl::jsonb, 'PENDING', @max)
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ev", eventType);
        cmd.Parameters.AddWithValue("at", aggregateType);
        cmd.Parameters.AddWithValue("aid", aggregateId);
        cmd.Parameters.Add(new NpgsqlParameter("pl", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(payload) });
        cmd.Parameters.AddWithValue("max", Math.Clamp(maxAttempts, 1, 20));
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<int> ProcessBatchAsync(int batchSize = 20, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var claimed = new List<(string Id, string EventType, string PayloadJson, int AttemptCount, int MaxAttempts)>();
        await using (var claimCmd = new NpgsqlCommand("""
            WITH next_batch AS (
                SELECT id FROM app_outbox_events
                WHERE status IN ('PENDING', 'FAILED')
                  AND next_attempt_at <= CURRENT_TIMESTAMP
                ORDER BY created_at ASC
                LIMIT @lim
                FOR UPDATE SKIP LOCKED
            )
            UPDATE app_outbox_events e
            SET status = 'PROCESSING',
                locked_at = CURRENT_TIMESTAMP
            FROM next_batch nb
            WHERE e.id = nb.id
            RETURNING e.id, e.event_type, e.payload::text, e.attempt_count, e.max_attempts
            """, conn))
        {
            claimCmd.Parameters.AddWithValue("lim", batchSize);
            await using var r = await claimCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                claimed.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
            }
        }

        foreach (var item in claimed)
        {
            var nextAttempt = item.AttemptCount + 1;
            try
            {
                if (item.EventType == "WEBHOOK_CALL")
                {
                    await DispatchWebhookAsync(item.Id, item.PayloadJson, ct);
                }

                await using var okCmd = new NpgsqlCommand("""
                    UPDATE app_outbox_events
                    SET status = 'SUCCEEDED',
                        attempt_count = @att,
                        last_error = NULL,
                        processed_at = CURRENT_TIMESTAMP
                    WHERE id = @id
                    """, conn);
                okCmd.Parameters.AddWithValue("id", item.Id);
                okCmd.Parameters.AddWithValue("att", nextAttempt);
                await okCmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                var exhausted = nextAttempt >= item.MaxAttempts;
                var newStatus = exhausted ? "DEAD_LETTER" : "FAILED";
                var delaySeconds = Math.Min(300, (int)Math.Pow(2, nextAttempt) * 5);

                await using var failCmd = new NpgsqlCommand("""
                    UPDATE app_outbox_events
                    SET status = @st,
                        attempt_count = @att,
                        last_error = @err,
                        next_attempt_at = CURRENT_TIMESTAMP + (@delay * INTERVAL '1 second')
                    WHERE id = @id
                    """, conn);
                failCmd.Parameters.AddWithValue("id", item.Id);
                failCmd.Parameters.AddWithValue("st", newStatus);
                failCmd.Parameters.AddWithValue("att", nextAttempt);
                failCmd.Parameters.AddWithValue("err", ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message);
                failCmd.Parameters.AddWithValue("delay", delaySeconds);
                await failCmd.ExecuteNonQueryAsync(ct);
            }
        }

        return claimed.Count;
    }

    private async Task DispatchWebhookAsync(string outboxId, string payloadJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Missing webhook URL in outbox payload.");

        var secret = root.TryGetProperty("secret", out var s) ? s.GetString() : null;
        var body = root.TryGetProperty("body", out var b) ? b.GetRawText() : payloadJson;

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Content = new StringContent(body, Encoding.UTF8, "application/json");
        reqMsg.Headers.TryAddWithoutValidation("X-SmartApps-Delivery-Id", outboxId);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            reqMsg.Headers.TryAddWithoutValidation("X-SmartApps-Signature", $"sha256={sig}");
        }

        var resp = await client.SendAsync(reqMsg, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Webhook returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).");
        }
    }

    public async Task<List<OutboxEventDto>> GetEventsAsync(string? status = null, int limit = 100)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, event_type, aggregate_type, aggregate_id, status, attempt_count, max_attempts,
                   next_attempt_at, last_error, processed_at, created_at
            FROM app_outbox_events
            WHERE (@st IS NULL OR status = @st)
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("st", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim().ToUpperInvariant() });
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 500));
        var list = new List<OutboxEventDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new OutboxEventDto
            {
                Id = r.GetString(0),
                EventType = r.GetString(1),
                AggregateType = r.GetString(2),
                AggregateId = r.GetString(3),
                Status = r.GetString(4),
                AttemptCount = r.GetInt32(5),
                MaxAttempts = r.GetInt32(6),
                NextAttemptAt = r.GetDateTime(7),
                LastError = r.IsDBNull(8) ? null : r.GetString(8),
                ProcessedAt = r.IsDBNull(9) ? null : r.GetDateTime(9),
                CreatedAt = r.GetDateTime(10)
            });
        }
        return list;
    }

    public async Task<bool> RetryAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE app_outbox_events
            SET status = 'PENDING',
                attempt_count = 0,
                next_attempt_at = CURRENT_TIMESTAMP,
                last_error = NULL
            WHERE id = @id AND status IN ('FAILED', 'DEAD_LETTER')
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}

public sealed class OutboxWorkerService : BackgroundService
{
    private readonly OutboxService _outbox;
    private readonly ILogger<OutboxWorkerService> _logger;

    public OutboxWorkerService(OutboxService outbox, ILogger<OutboxWorkerService> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _outbox.ProcessBatchAsync(20, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox worker batch failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
