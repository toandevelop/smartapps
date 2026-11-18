using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class SubTableTests
{
    private readonly DynamicComputeService _compute = new();

    [Fact]
    public void EvaluateAndValidate_ComputesSubTableFormulasBeforeParentRollup_AndAssignsRowIds()
    {
        var schemaJson = """
            [
              {"code":"san_pham","name":"Sản phẩm","dataType":"STRING","required":true},
              {"code":"so_luong","name":"Số lượng","dataType":"NUMBER","required":true},
              {"code":"don_gia","name":"Đơn giá","dataType":"CURRENCY","required":true},
              {"code":"thanh_tien","name":"Thành tiền","dataType":"CURRENCY","formula":"so_luong * don_gia"}
            ]
            """;

        var columns = new List<SysColumnDto>
        {
            new() { ColumnCode = "chi_tiet", ColumnName = "Chi tiết", DataType = "SUB_TABLE", DefaultValue = schemaJson },
            new() { ColumnCode = "tong_tien", ColumnName = "Tổng tiền", DataType = "CURRENCY", Formula = "SUM(chi_tiet.thanh_tien)" }
        };

        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["san_pham"] = "Bút", ["so_luong"] = 3, ["don_gia"] = 10000 },
            new() { ["san_pham"] = "Sổ", ["so_luong"] = 2, ["don_gia"] = 25000 }
        };

        var result = _compute.EvaluateAndValidate(
            columns,
            new Dictionary<string, HashSet<string>>(),
            [("rec_1", new Dictionary<string, object?> { ["chi_tiet"] = rows })]
        ).Single();

        Assert.True(result.IsValid);
        Assert.Equal(80000m, Convert.ToDecimal(result.EvaluatedData["tong_tien"]));

        var evaluatedRows = Assert.IsType<List<Dictionary<string, object?>>>(result.EvaluatedData["chi_tiet"]);
        Assert.Equal(2, evaluatedRows.Count);
        Assert.All(evaluatedRows, r => Assert.False(string.IsNullOrWhiteSpace(r["_rowId"]?.ToString())));
        Assert.Equal(30000m, Convert.ToDecimal(evaluatedRows[0]["thanh_tien"]));
        Assert.Equal(50000m, Convert.ToDecimal(evaluatedRows[1]["thanh_tien"]));
    }

    [Fact]
    public void EvaluateAndValidate_ReportsSubTableCellLevelErrors()
    {
        var schemaJson = """
            [
              {"code":"san_pham","name":"Sản phẩm","dataType":"STRING","required":true},
              {"code":"so_luong","name":"Số lượng","dataType":"NUMBER","required":true},
              {"code":"email_ncc","name":"Email NCC","dataType":"EMAIL"}
            ]
            """;

        var columns = new List<SysColumnDto>
        {
            new() { ColumnCode = "chi_tiet", ColumnName = "Chi tiết", DataType = "SUB_TABLE", DefaultValue = schemaJson }
        };

        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["san_pham"] = "", ["so_luong"] = "khong_phai_so", ["email_ncc"] = "bad" },
            new() { ["san_pham"] = "Giấy A4", ["so_luong"] = 5, ["email_ncc"] = "ncc@example.com" }
        };

        var result = _compute.EvaluateAndValidate(
            columns,
            new Dictionary<string, HashSet<string>>(),
            [("rec_2", new Dictionary<string, object?> { ["chi_tiet"] = rows })]
        ).Single();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "chi_tiet[0].san_pham" && e.Severity == "ERROR");
        Assert.Contains(result.Errors, e => e.Field == "chi_tiet[0].so_luong" && e.Severity == "ERROR");
        Assert.Contains(result.Errors, e => e.Field == "chi_tiet[0].email_ncc" && e.Severity == "ERROR");
        Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("chi_tiet[1]."));
    }
}
