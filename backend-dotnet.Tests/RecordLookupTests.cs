using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class RecordLookupTests
{
    [Fact]
    public void BuildLookupOption_IncludesMultiFieldSearchAndExcludesHiddenColumns()
    {
        var visibleCols = new List<SysColumnDto>
        {
            new() { ColumnCode = "ma_nv", ColumnName = "Mã Nhân viên", DataType = "STRING", IsIdentityKey = true },
            new() { ColumnCode = "ho_ten", ColumnName = "Họ và Tên", DataType = "STRING" },
            new() { ColumnCode = "so_dien_thoai", ColumnName = "Số điện thoại", DataType = "TEL" },
            new() { ColumnCode = "so_cccd", ColumnName = "Số CCCD / CMND", DataType = "STRING" },
            new() { ColumnCode = "email", ColumnName = "Email Công ty", DataType = "EMAIL" },
            new() { ColumnCode = "phong_ban", ColumnName = "Phòng ban", DataType = "SELECT" }
        };

        var record = new AppRecordDto
        {
            Id = "rec_nv_1",
            RecordKey = "NV001",
            Data = new Dictionary<string, object?>
            {
                ["ma_nv"] = "NV001",
                ["ho_ten"] = "Nguyễn Văn An",
                ["so_dien_thoai"] = "0901234567",
                ["so_cccd"] = "079092001234",
                ["email"] = "an.nguyen@company.vn",
                ["phong_ban"] = "TECH",
                ["luong_co_ban"] = 32000000 // hidden from visibleCols (e.g. STAFF FLS)
            }
        };

        var opt = RecordService.BuildLookupOption(record, "ma_nv", "ho_ten", visibleCols);

        Assert.Equal("NV001", opt.Value);
        Assert.Equal("NV001 — Nguyễn Văn An", opt.Label);
        Assert.Contains("0901234567", opt.SubLabel);
        Assert.Contains("079092001234", opt.SubLabel);
        Assert.Contains("0901234567", opt.SearchText);
        Assert.Contains("079092001234", opt.SearchText);
        Assert.Contains("Nguyễn Văn An", opt.SearchText);
        Assert.DoesNotContain("32000000", opt.SearchText);
        Assert.NotNull(opt.Data);
        Assert.False(opt.Data!.ContainsKey("luong_co_ban"));
        Assert.Contains(opt.SummaryFields, f => f.Code == "so_cccd" && f.Value == "079092001234");
        Assert.Contains(opt.SummaryFields, f => f.Code == "so_dien_thoai" && f.Value == "0901234567");
    }
}
