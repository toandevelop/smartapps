using System.Text.Json;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public sealed record ChatHistoryCursor(string ChannelId, DateTime CreatedAt, string Id)
{
    public static string Encode(string channelId, ChatMessageDto message) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new ChatHistoryCursor(channelId, message.CreatedAt, message.Id)));

    public static ChatHistoryCursor Decode(string value, string channelId)
    {
        const string error = "Con trỏ lịch sử hội thoại không hợp lệ.";
        if (value.Length > 1024) throw new ArgumentException(error);
        try
        {
            var cursor = JsonSerializer.Deserialize<ChatHistoryCursor>(Convert.FromBase64String(value));
            if (cursor == null || cursor.ChannelId != channelId || string.IsNullOrWhiteSpace(cursor.Id)
                || cursor.Id.Length > 64 || cursor.CreatedAt == default || cursor.CreatedAt.Kind != DateTimeKind.Utc)
                throw new ArgumentException(error);
            return cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ArgumentException(error, ex);
        }
    }
}

public sealed record ChatHistoryPage(List<ChatMessageDto> Items, bool HasMore, string? NextCursor);
