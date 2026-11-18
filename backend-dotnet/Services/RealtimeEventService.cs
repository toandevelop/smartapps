using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

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
    private readonly ConcurrentDictionary<Guid, (string Username, Channel<PlatformRealtimeEvent> Channel)> _subs = new();

    public (Guid SubId, ChannelReader<PlatformRealtimeEvent> Reader) Subscribe(string username)
    {
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
        }
    }

    public void Publish(string type, string? appCode = null, string? tableId = null, string? recordId = null, string? targetUser = null, object? data = null)
    {
        var ev = new PlatformRealtimeEvent(type, appCode, tableId, recordId, targetUser, data, DateTime.UtcNow);
        foreach (var kv in _subs)
        {
            var subUser = kv.Value.Username;
            if (string.IsNullOrWhiteSpace(targetUser) ||
                targetUser == "*" ||
                targetUser.Equals(subUser, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value.Channel.Writer.TryWrite(ev);
            }
        }
    }

    public static string SerializeEvent(PlatformRealtimeEvent ev) =>
        JsonSerializer.Serialize(ev, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
