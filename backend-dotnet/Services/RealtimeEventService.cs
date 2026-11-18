using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Npgsql;

namespace SmartApps.Api.Services;

public record PlatformRealtimeEvent(
    string Type,
    string? AppCode,
    string? TableId,
    string? RecordId,
    string? TargetUser,
    object? Data,
    DateTime Timestamp);

public class RealtimeEventService
{
    private readonly string? _connStr;
    private readonly ILogger<RealtimeEventService>? _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..12];
    private readonly ConcurrentDictionary<Guid, (string Username, Channel<PlatformRealtimeEvent> Channel)> _subs = new();
    private readonly ConcurrentDictionary<string, DateTime> _presence = new(StringComparer.OrdinalIgnoreCase);

    internal const string NotifyChannelName = "smartapps_realtime";

    private static readonly JsonSerializerOptions CamelOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RealtimeEventService()
    {
    }

    [ActivatorUtilitiesConstructor]
    public RealtimeEventService(IConfiguration config, ILogger<RealtimeEventService> logger)
    {
        _connStr = config.GetConnectionString("Postgres");
        _logger = logger;
    }

    internal string InstanceId => _instanceId;

    public int ActiveSubscriberCount => _subs.Count;

    public (Guid SubId, ChannelReader<PlatformRealtimeEvent> Reader) Subscribe(string username)
    {
        TouchPresence(username);
        var id = Guid.NewGuid();
        var ch = Channel.CreateBounded<PlatformRealtimeEvent>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _subs[id] = (username, ch);
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid subId)
    {
        if (_subs.TryRemove(subId, out var entry))
        {
            entry.Channel.Writer.TryComplete();
            var stillConnected = _subs.Values.Any(v => v.Username.Equals(entry.Username, StringComparison.OrdinalIgnoreCase));
            if (!stillConnected)
            {
                _presence.TryRemove(entry.Username, out _);
                Publish("PRESENCE_CHANGED");
            }
        }
    }

    public void TouchPresence(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        var added = !_presence.ContainsKey(username);
        _presence[username] = DateTime.UtcNow;
        if (added)
        {
            Publish("PRESENCE_CHANGED");
        }
    }

    public HashSet<string> GetOnlineUsernames()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-90);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _subs.Values)
        {
            if (!string.IsNullOrWhiteSpace(entry.Username))
                result.Add(entry.Username);
        }
        foreach (var kv in _presence)
        {
            if (kv.Value >= cutoff)
                result.Add(kv.Key);
            else
                _presence.TryRemove(kv.Key, out _);
        }
        return result;
    }

    public void Publish(string type, string? appCode = null, string? tableId = null, string? recordId = null, string? targetUser = null, object? data = null)
    {
        var ev = new PlatformRealtimeEvent(type, appCode, tableId, recordId, targetUser, data, DateTime.UtcNow);
        DispatchLocal(ev);

        if (!string.IsNullOrWhiteSpace(_connStr))
        {
            _ = NotifyPeersAsync(ev);
        }
    }

    internal void DispatchLocal(PlatformRealtimeEvent ev)
    {
        foreach (var kv in _subs)
        {
            var subUser = kv.Value.Username;
            if (string.IsNullOrWhiteSpace(ev.TargetUser) ||
                ev.TargetUser == "*" ||
                ev.TargetUser.Equals(subUser, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value.Channel.Writer.TryWrite(ev);
            }
        }
    }

    private sealed class ClusterNotifyEnvelope
    {
        public string SourceInstance { get; set; } = string.Empty;
        public PlatformRealtimeEvent? Event { get; set; }
    }

    private async Task NotifyPeersAsync(PlatformRealtimeEvent ev)
    {
        try
        {
            var envelopeJson = JsonSerializer.Serialize(new ClusterNotifyEnvelope
            {
                SourceInstance = _instanceId,
                Event = ev
            }, CamelOpts);

            // PostgreSQL NOTIFY payloads must stay under 8000 bytes; strip bulky Data when needed.
            if (envelopeJson.Length > 7200)
            {
                envelopeJson = JsonSerializer.Serialize(new ClusterNotifyEnvelope
                {
                    SourceInstance = _instanceId,
                    Event = ev with { Data = null }
                }, CamelOpts);
            }

            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"SELECT pg_notify('{NotifyChannelName}', @payload)", conn);
            cmd.Parameters.AddWithValue("payload", envelopeJson);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "PostgreSQL NOTIFY publish skipped.");
        }
    }

    internal void HandleRemoteNotificationPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;
        try
        {
            var env = JsonSerializer.Deserialize<ClusterNotifyEnvelope>(payload, CamelOpts);
            if (env?.Event == null) return;
            if (string.Equals(env.SourceInstance, _instanceId, StringComparison.Ordinal)) return;
            DispatchLocal(env.Event);
        }
        catch
        {
            // Ignore malformed cross-instance notification payload
        }
    }

    public static string SerializeEvent(PlatformRealtimeEvent ev) =>
        JsonSerializer.Serialize(ev, CamelOpts);
}

/// <summary>
/// Listens on PostgreSQL LISTEN/NOTIFY so SSE events published by one API instance
/// reach browsers connected to any peer API instance.
/// </summary>
public sealed class RealtimePostgresListenerService : BackgroundService
{
    private readonly string _connStr;
    private readonly RealtimeEventService _realtime;
    private readonly ILogger<RealtimePostgresListenerService> _logger;

    public RealtimePostgresListenerService(
        IConfiguration config,
        RealtimeEventService realtime,
        ILogger<RealtimePostgresListenerService> logger)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _realtime = realtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connStr);
                await conn.OpenAsync(stoppingToken);
                conn.Notification += (_, e) =>
                {
                    if (e.Channel == RealtimeEventService.NotifyChannelName)
                    {
                        _realtime.HandleRemoteNotificationPayload(e.Payload);
                    }
                };
                await using (var listenCmd = new NpgsqlCommand($"LISTEN {RealtimeEventService.NotifyChannelName}", conn))
                {
                    await listenCmd.ExecuteNonQueryAsync(stoppingToken);
                }
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RealtimePostgresListenerService reconnecting in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
