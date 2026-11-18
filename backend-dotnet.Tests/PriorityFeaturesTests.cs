using System.IO.Compression;
using System.Xml.Linq;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class PriorityFeaturesTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(0, 1)]
    [InlineData(100, 30)]
    public void SessionExpiration_UsesBoundedDurationAndRejectsTampering(int configured, int expected)
    {
        var auth = PriorityPostgresFixture.CreateAuth();
        var now = DateTimeOffset.UtcNow;
        var token = auth.CreateToken(new SysUserDto { Id = "u", Username = "test" }, configured);
        Assert.InRange(auth.GetTokenExpiration(token), now.AddDays(expected).AddSeconds(-1), now.AddDays(expected).AddSeconds(1));
        Assert.Throws<ArgumentException>(() => auth.GetTokenExpiration(token + "bad"));
    }

    [Fact]
    public void ChatCursor_PreservesMicrosecondsAndBindsChannel()
    {
        var message = new ChatMessageDto { Id = "m1", CreatedAt = new DateTime(2026, 11, 17, 1, 2, 3, DateTimeKind.Utc).AddTicks(1234560) };
        var encoded = ChatHistoryCursor.Encode("private-channel", message);
        Assert.Equal(message.CreatedAt, ChatHistoryCursor.Decode(encoded, "private-channel").CreatedAt);
        Assert.Throws<ArgumentException>(() => ChatHistoryCursor.Decode(encoded, "other-channel"));
        Assert.Throws<ArgumentException>(() => ChatHistoryCursor.Decode("invalid", "private-channel"));
    }

    [Fact]
    public void ExcelWorkbook_ExportsAllRowsAndKeepsFormulaLikeTextAsValues()
    {
        var columns = new List<SysColumnDto> {
            new() { ColumnCode = "name", DataType = "STRING" },
            new() { ColumnCode = "amount", DataType = "NUMBER" }
        };
        var records = Enumerable.Range(0, 650).Select(i => new AppRecordDto {
            Data = new() { ["name"] = i == 0 ? "=SUM(1,2) & <test> 😀\u0001" : $"row-{i}", ["amount"] = i }
        }).ToList();
        using var zip = new ZipArchive(new MemoryStream(ExcelExportService.CreateWorkbook(columns, records)));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal(651, xml.Descendants(ns + "row").Count());
        Assert.Empty(xml.Descendants(ns + "f"));
        Assert.Equal("=SUM(1,2) & <test> 😀", xml.Descendants(ns + "row").Skip(1).First().Descendants(ns + "t").First().Value);
        Assert.Equal("649", xml.Descendants(ns + "row").Last().Descendants(ns + "v").Single().Value);
    }
}
