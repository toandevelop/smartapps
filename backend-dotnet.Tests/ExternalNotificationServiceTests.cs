using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class ExternalNotificationServiceTests
{
    [Fact]
    public void FormatTelegramHtml_EscapesHtmlAndIncludesAppBadge()
    {
        var text = ExternalNotificationService.FormatTelegramHtml(
            "Đơn <Nghỉ phép> mới",
            "Nhân viên A & B gửi đơn",
            "APPROVAL",
            "HRM");

        Assert.Contains("<b>", text);
        Assert.Contains("[APPROVAL • HRM]", text);
        Assert.Contains("&lt;Nghỉ phép&gt;", text);
        Assert.Contains("A &amp; B", text);
    }

    [Fact]
    public void IsCategoryMatched_HandlesAllAndSpecificCategories()
    {
        Assert.True(ExternalNotificationService.IsCategoryMatched(new[] { "ALL" }, "TASK"));
        Assert.True(ExternalNotificationService.IsCategoryMatched(new[] { "TASK", "APPROVAL" }, "approval"));
        Assert.False(ExternalNotificationService.IsCategoryMatched(new[] { "TASK", "APPROVAL" }, "CHAT"));
    }

    [Fact]
    public void MaskConfigSecrets_HidesPasswordsAndTokensWhilePreservingNonSecrets()
    {
        var raw = new Dictionary<string, object?>
        {
            ["smtpHost"] = "smtp.gmail.com",
            ["username"] = "bot@company.vn",
            ["password"] = "app-pass-123",
            ["botToken"] = "999:secret-tg",
            ["chatId"] = "-100123"
        };

        var (masked, hasSecret) = ExternalNotificationService.MaskConfigSecrets(raw);
        Assert.True(hasSecret);
        Assert.Equal("smtp.gmail.com", masked["smtpHost"]);
        Assert.Equal("bot@company.vn", masked["username"]);
        Assert.Equal("-100123", masked["chatId"]);
        Assert.Equal("", masked["password"]);
        Assert.Equal("", masked["botToken"]);
    }
}
