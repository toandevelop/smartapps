using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class NotificationStreamPostgresTests(PriorityPostgresFixture fixture) : IClassFixture<PriorityPostgresFixture>
{
    [PostgresFact]
    public async Task Stream_DeliversConsecutiveEventsBeforeAndAfterHeartbeat()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var auth = fixture.Services.GetRequiredService<AuthService>();
        var user = (await auth.GetByUsernameAsync("admin"))!;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.CreateToken(user));
        using var response = await fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        async Task<string> Frame()
        {
            var lines = new List<string>();
            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token);
                Assert.NotNull(line);
                if (line.Length == 0) return string.Join("\n", lines);
                lines.Add(line);
            }
        }
        Assert.Contains("event: connected", await Frame());
        var realtime = fixture.Services.GetRequiredService<RealtimeEventService>();
        for (var i = 0; i < 3; i++)
        {
            realtime.Publish("form_test", recordId: $"before-{i}", targetUser: user.Username);
            var frame = await Frame();
            Assert.Contains("event: platform", frame);
            Assert.Contains($"\"recordId\":\"before-{i}\"", frame);
        }
        Assert.Equal(": ping", await Frame());
        realtime.Publish("form_test", recordId: "after-heartbeat", targetUser: user.Username);
        Assert.Contains("\"recordId\":\"after-heartbeat\"", await Frame());
        timeout.Cancel();
    }
}
