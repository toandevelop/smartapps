using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class DynamicComputeServiceTests
{
    private readonly DynamicComputeService _svc = new();

    [Fact]
    public void DecimalFormulas_AreIndependentOfMachineLocale()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            var columns = new List<SysColumnDto> {
                new() { ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER" },
                new() { ColumnCode = "total", ColumnName = "Total", DataType = "NUMBER", Formula = "amount * 2" }
            };
            var result = _svc.EvaluateAndValidate(columns, new(), [("r", new() { ["amount"] = 2000.00m })]).Single();
            Assert.True(result.IsValid);
            Assert.Equal(4000m, result.EvaluatedData["total"]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EvaluateAndValidate_ComputesArithmeticAndMultiPassFormulas()
    {
        var cols = new List<SysColumnDto>
        {
            new() { ColumnCode = "so_luong", ColumnName = "Số lượng", DataType = "NUMBER", IsRequired = true },
            new() { ColumnCode = "don_gia", ColumnName = "Đơn giá", DataType = "CURRENCY", IsRequired = true },
            new() { ColumnCode = "thanh_tien", ColumnName = "Thành tiền", DataType = "CURRENCY", Formula = "so_luong * don_gia" },
            new() { ColumnCode = "muc_duyet", ColumnName = "Mức duyệt", DataType = "STRING", Formula = "IF(thanh_tien >= 1000000, 'GIAM_DOC', 'TRUONG_PHONG')" }
        };

        var data = new Dictionary<string, object?> { ["so_luong"] = 3, ["don_gia"] = 500000 };
        var res = _svc.EvaluateAndValidate(cols, new(), [("r1", data)]).Single();

        Assert.True(res.IsValid);
        Assert.Equal(1500000m, Convert.ToDecimal(res.EvaluatedData["thanh_tien"]));
        Assert.Equal("GIAM_DOC", res.EvaluatedData["muc_duyet"]?.ToString());
    }

    [Fact]
    public void EvaluateAndValidate_ComputesSubTableRollupSumAndCount()
    {
        var cols = new List<SysColumnDto>
        {
            new() { ColumnCode = "chi_tiet", ColumnName = "Chi tiết", DataType = "SUB_TABLE" },
            new() { ColumnCode = "tong_tien", ColumnName = "Tổng tiền", DataType = "CURRENCY", Formula = "SUM(chi_tiet.thanh_tien)" },
            new() { ColumnCode = "so_dong", ColumnName = "Số dòng", DataType = "NUMBER", Formula = "COUNT(chi_tiet)" }
        };

        var subRows = new List<object?>
        {
            new Dictionary<string, object?> { ["ten"] = "RAM", ["thanh_tien"] = 1200000 },
            new Dictionary<string, object?> { ["ten"] = "SSD", ["thanh_tien"] = 1800000 }
        };
        var data = new Dictionary<string, object?> { ["chi_tiet"] = subRows };

        var res = _svc.EvaluateAndValidate(cols, new(), [("r1", data)]).Single();

        Assert.Equal(3000000m, Convert.ToDecimal(res.EvaluatedData["tong_tien"]));
        Assert.Equal(2m, Convert.ToDecimal(res.EvaluatedData["so_dong"]));
    }

    [Fact]
    public void EvaluateAndValidate_ComputesDateDifAndRound()
    {
        var cols = new List<SysColumnDto>
        {
            new() { ColumnCode = "tu_ngay", ColumnName = "Từ ngày", DataType = "DATE" },
            new() { ColumnCode = "den_ngay", ColumnName = "Đến ngày", DataType = "DATE" },
            new() { ColumnCode = "so_ngay", ColumnName = "Số ngày", DataType = "NUMBER", Formula = "DATEDIF(tu_ngay, den_ngay)" },
            new() { ColumnCode = "tb", ColumnName = "Trung bình", DataType = "NUMBER", Formula = "ROUND(100 / 3, 2)" }
        };

        var data = new Dictionary<string, object?> { ["tu_ngay"] = "2026-11-01", ["den_ngay"] = "2026-11-16" };
        var res = _svc.EvaluateAndValidate(cols, new(), [("r1", data)]).Single();

        Assert.Equal(15, Convert.ToInt32(res.EvaluatedData["so_ngay"]));
        Assert.Equal(33.33m, Convert.ToDecimal(res.EvaluatedData["tb"]));
    }

    [Fact]
    public void EvaluateAndValidate_FlagsMissingRequiredAndBadEmail()
    {
        var cols = new List<SysColumnDto>
        {
            new() { ColumnCode = "ho_ten", ColumnName = "Họ tên", DataType = "STRING", IsRequired = true },
            new() { ColumnCode = "email", ColumnName = "Email", DataType = "EMAIL" }
        };

        var data = new Dictionary<string, object?> { ["email"] = "not-an-email" };
        var res = _svc.EvaluateAndValidate(cols, new(), [("r1", data)]).Single();

        Assert.False(res.IsValid);
        Assert.Contains(res.Errors, e => e.Field == "ho_ten");
        Assert.Contains(res.Errors, e => e.Field == "email");
    }
}
