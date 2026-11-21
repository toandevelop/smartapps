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

public sealed class ClusterNodeStatusDto
{
    public string InstanceId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string AppVersion { get; set; } = "3.0.0";
    public int ActiveSse { get; set; }
    public bool ListenerConnected { get; set; }
    public List<string> OnlineUsers { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public bool IsCurrent { get; set; }
}

public class RealtimeEventService
{
    private readonly string? _connStr;
    private readonly ILogger<RealtimeEventService>? _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..12];
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private volatile bool _listenerConnected;
    private DateTime? _lastListenerConnectedAt;
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

    public string InstanceId => _instanceId;
    public DateTime StartedAt => _startedAt;
    public bool IsListenerConnected => _listenerConnected;
    public DateTime? LastListenerConnectedAt => _lastListenerConnectedAt;

    internal void SetListenerState(bool connected)
    {
        _listenerConnected = connected;
        if (connected) _lastListenerConnectedAt = DateTime.UtcNow;
    }

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

    public async Task UpsertClusterHeartbeatAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return;
        try
        {
            var onlineUsers = GetOnlineUsernames().OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_cluster_nodes (
                    instance_id, host_name, process_id, app_version, active_sse,
                    listener_connected, online_users, started_at, last_heartbeat_at
                )
                VALUES (@id, @host, @pid, @ver, @sse, @lst, @users::jsonb, @started, CURRENT_TIMESTAMP)
                ON CONFLICT (instance_id) DO UPDATE SET
                    host_name = EXCLUDED.host_name,
                    process_id = EXCLUDED.process_id,
                    app_version = EXCLUDED.app_version,
                    active_sse = EXCLUDED.active_sse,
                    listener_connected = EXCLUDED.listener_connected,
                    online_users = EXCLUDED.online_users,
                    last_heartbeat_at = CURRENT_TIMESTAMP;
                DELETE FROM sys_cluster_nodes
                WHERE last_heartbeat_at < CURRENT_TIMESTAMP - INTERVAL '180 seconds';
                """, conn);
            cmd.Parameters.AddWithValue("id", _instanceId);
            cmd.Parameters.AddWithValue("host", Environment.MachineName);
            cmd.Parameters.AddWithValue("pid", Environment.ProcessId);
            cmd.Parameters.AddWithValue("ver", "3.0.0");
            cmd.Parameters.AddWithValue("sse", ActiveSubscriberCount);
            cmd.Parameters.AddWithValue("lst", _listenerConnected);
            cmd.Parameters.AddWithValue("users", JsonSerializer.Serialize(onlineUsers));
            cmd.Parameters.AddWithValue("started", _startedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Cluster heartbeat upsert skipped.");
        }
    }

    public async Task<List<ClusterNodeStatusDto>> GetClusterNodesAsync(CancellationToken ct = default)
    {
        var nodes = new List<ClusterNodeStatusDto>();
        if (!string.IsNullOrWhiteSpace(_connStr))
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connStr);
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand("""
                    SELECT instance_id, host_name, process_id, app_version, active_sse,
                           listener_connected, COALESCE(online_users, '[]'::jsonb)::text, started_at, last_heartbeat_at
                    FROM sys_cluster_nodes
                    WHERE last_heartbeat_at >= CURRENT_TIMESTAMP - INTERVAL '90 seconds'
                    ORDER BY last_heartbeat_at DESC
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var id = r.GetString(0);
                    List<string> users;
                    try { users = JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new(); }
                    catch { users = new(); }
                    nodes.Add(new ClusterNodeStatusDto
                    {
                        InstanceId = id,
                        HostName = r.GetString(1),
                        ProcessId = r.GetInt32(2),
                        AppVersion = r.GetString(3),
                        ActiveSse = r.GetInt32(4),
                        ListenerConnected = r.GetBoolean(5),
                        OnlineUsers = users,
                        StartedAt = r.GetDateTime(7),
                        LastHeartbeatAt = r.GetDateTime(8),
                        IsCurrent = string.Equals(id, _instanceId, StringComparison.Ordinal)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Cluster nodes query failed; returning current node only.");
            }
        }

        if (!nodes.Any(n => n.IsCurrent))
        {
            nodes.Insert(0, new ClusterNodeStatusDto
            {
                InstanceId = _instanceId,
                HostName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                AppVersion = "3.0.0",
                ActiveSse = ActiveSubscriberCount,
                ListenerConnected = _listenerConnected,
                OnlineUsers = GetOnlineUsernames().OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList(),
                StartedAt = _startedAt,
                LastHeartbeatAt = DateTime.UtcNow,
                IsCurrent = true
            });
        }
        return nodes;
    }

    public async Task<HashSet<string>> GetClusterOnlineUsernamesAsync(CancellationToken ct = default)
    {
        var merged = GetOnlineUsernames();
        if (string.IsNullOrWhiteSpace(_connStr)) return merged;
        try
        {
            var nodes = await GetClusterNodesAsync(ct);
            foreach (var node in nodes)
            {
                foreach (var u in node.OnlineUsers)
                {
                    if (!string.IsNullOrWhiteSpace(u)) merged.Add(u);
                }
            }
        }
        catch
        {
            // Fall back to local presence
        }
        return merged;
    }
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
                _realtime.SetListenerState(true);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _realtime.SetListenerState(false);
                break;
            }
            catch (Exception ex)
            {
                _realtime.SetListenerState(false);
                _logger.LogWarning(ex, "RealtimePostgresListenerService reconnecting in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

/// <summary>
/// Periodically writes this API instance's heartbeat & presence snapshot to PostgreSQL
/// so multi-instance HA clusters have unified online presence and cluster health telemetry.
/// </summary>
public sealed class ClusterHeartbeatService : BackgroundService
{
    private readonly RealtimeEventService _realtime;
    private readonly ILogger<ClusterHeartbeatService> _logger;

    public ClusterHeartbeatService(RealtimeEventService realtime, ILogger<ClusterHeartbeatService> logger)
    {
        _realtime = realtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _realtime.UpsertClusterHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ClusterHeartbeatService tick failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
