using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class TaskServiceTests
{
    [Fact]
    public void TryParseSlashTaskCommand_ParsesTaskWithMention()
    {
        var (ok, assignee, title) = TaskService.TryParseSlashTaskCommand("/task @asset_lead Kiểm tra laptop phòng IT", "admin");
        Assert.True(ok);
        Assert.Equal("asset_lead", assignee);
        Assert.Equal("Kiểm tra laptop phòng IT", title);
    }

    [Fact]
    public void TryParseSlashTaskCommand_ParsesGiaoviecWithoutMentionUsingFallback()
    {
        var (ok, assignee, title) = TaskService.TryParseSlashTaskCommand("/giaoviec Tổng hợp báo cáo tuần", "hr_manager");
        Assert.True(ok);
        Assert.Equal("hr_manager", assignee);
        Assert.Equal("Tổng hợp báo cáo tuần", title);
    }

    [Fact]
    public void TryParseSlashTaskCommand_IgnoresNormalMessages()
    {
        var (ok, _, _) = TaskService.TryParseSlashTaskCommand("Xin chào mọi người @admin", "staff");
        Assert.False(ok);
    }
}
