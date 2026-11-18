using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class WorkflowServiceTests
{
    [Fact]
    public void MatchesCondition_EvaluatesNumericAndStringOperators()
    {
        var data = new Dictionary<string, object?>
        {
            ["so_tien"] = 15000000,
            ["phong_ban"] = "TECH",
            ["ghi_chu"] = "Cần mua thiết bị máy chủ"
        };

        Assert.True(WorkflowService.MatchesCondition("so_tien", "GREATER_THAN", "10000000", data));
        Assert.False(WorkflowService.MatchesCondition("so_tien", "LESS_THAN", "10000000", data));
        Assert.True(WorkflowService.MatchesCondition("phong_ban", "EQUALS", "tech", data));
        Assert.True(WorkflowService.MatchesCondition("ghi_chu", "CONTAINS", "máy chủ", data));
        Assert.True(WorkflowService.MatchesCondition("ghi_chu", "NOT_EMPTY", null, data));
        Assert.False(WorkflowService.MatchesCondition("thieu_truong", "NOT_EMPTY", null, data));
    }

    [Fact]
    public void MatchesCondition_EvaluatesScheduledDateOperators()
    {
        var today = DateTime.UtcNow.Date;
        var data = new Dictionary<string, object?>
        {
            ["het_han_10d"] = today.AddDays(10).ToString("yyyy-MM-dd"),
            ["het_han_45d"] = today.AddDays(45).ToString("yyyy-MM-dd"),
            ["qua_han_5d"] = today.AddDays(-5).ToString("yyyy-MM-dd"),
            ["hom_nay"] = today.ToString("yyyy-MM-dd")
        };

        Assert.True(WorkflowService.MatchesCondition("het_han_10d", "DAYS_BEFORE", "30", data));
        Assert.False(WorkflowService.MatchesCondition("het_han_45d", "DAYS_BEFORE", "30", data));
        Assert.False(WorkflowService.MatchesCondition("qua_han_5d", "DAYS_BEFORE", "30", data));

        Assert.True(WorkflowService.MatchesCondition("qua_han_5d", "DAYS_AFTER", "7", data));
        Assert.False(WorkflowService.MatchesCondition("qua_han_5d", "DAYS_AFTER", "3", data));

        Assert.True(WorkflowService.MatchesCondition("hom_nay", "EQUALS_TODAY", null, data));
        Assert.False(WorkflowService.MatchesCondition("het_han_10d", "EQUALS_TODAY", null, data));
    }
}
