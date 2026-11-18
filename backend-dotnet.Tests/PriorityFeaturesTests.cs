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

    [Fact]
    public void RemapBlueprintColumnLookup_UpdatesInternalTableAndAppAndPreservesExternal()
    {
        var tableMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tbl_old_vendors"] = "tbl_new_vendors"
        };

        var internalLookup = new SysColumnDto
        {
            ColumnCode = "vendor_code",
            RefAppId = "app_old",
            RefTableId = "tbl_old_vendors"
        };
        var (mappedApp, mappedTbl) = MetadataService.RemapBlueprintColumnLookup(internalLookup, "app_old", "app_cloned", tableMap);
        Assert.Equal("app_cloned", mappedApp);
        Assert.Equal("tbl_new_vendors", mappedTbl);

        var externalLookup = new SysColumnDto
        {
            ColumnCode = "emp_code",
            RefAppId = "app_hrm",
            RefTableId = "tbl_hrm_nhanvien"
        };
        var (extApp, extTbl) = MetadataService.RemapBlueprintColumnLookup(externalLookup, "app_old", "app_cloned", tableMap);
        Assert.Equal("app_hrm", extApp);
        Assert.Equal("tbl_hrm_nhanvien", extTbl);
    }

    [Fact]
    public void ComputeBlueprintSchemaDiff_DetectsAddedRemovedAndModifiedColumns()
    {
        var draft = new AppRuntimeTreeDto
        {
            App = new SysAppDto { Id = "app_1", Code = "SALES", Name = "Sales", PublishStatus = "DRAFT" },
            Modules =
            [
                new ModuleWithTablesDto
                {
                    Module = new SysModuleDto { Id = "m1", Code = "MAIN", Name = "Main" },
                    Tables =
                    [
                        new TableWithColumnsDto
                        {
                            Table = new SysTableDto { Id = "t1", TableName = "orders", DisplayName = "Đơn hàng" },
                            Columns =
                            [
                                new SysColumnDto { ColumnCode = "code", ColumnName = "Mã", DataType = "STRING", IsRequired = true },
                                new SysColumnDto { ColumnCode = "total", ColumnName = "Tổng", DataType = "CURRENCY", Formula = "qty * price" },
                                new SysColumnDto { ColumnCode = "note", ColumnName = "Ghi chú", DataType = "TEXTAREA" }
                            ]
                        },
                        new TableWithColumnsDto
                        {
                            Table = new SysTableDto { Id = "t2", TableName = "items", DisplayName = "Mặt hàng" },
                            Columns = [new SysColumnDto { ColumnCode = "sku", ColumnName = "SKU", DataType = "STRING" }]
                        }
                    ]
                }
            ]
        };

        var published = new AppBlueprintDto
        {
            App = new SysAppDto { Id = "app_1", Code = "SALES", Name = "Sales" },
            Modules =
            [
                new ModuleWithTablesDto
                {
                    Module = new SysModuleDto { Id = "m1", Code = "MAIN", Name = "Main" },
                    Tables =
                    [
                        new TableWithColumnsDto
                        {
                            Table = new SysTableDto { Id = "t1", TableName = "orders", DisplayName = "Đơn hàng" },
                            Columns =
                            [
                                new SysColumnDto { ColumnCode = "code", ColumnName = "Mã", DataType = "STRING", IsRequired = false },
                                new SysColumnDto { ColumnCode = "total", ColumnName = "Tổng", DataType = "NUMBER" },
                                new SysColumnDto { ColumnCode = "legacy", ColumnName = "Cũ", DataType = "STRING" }
                            ]
                        }
                    ]
                }
            ]
        };

        var diff = MetadataService.ComputeBlueprintSchemaDiff(draft, published, 2);
        Assert.True(diff.HasPublishedRelease);
        Assert.Equal(2, diff.PublishedVersion);
        Assert.Single(diff.AddedTables);
        Assert.Contains(diff.AddedColumns, c => c.Contains("note"));
        Assert.Contains(diff.RemovedColumns, c => c.Contains("legacy"));
        Assert.Contains(diff.ModifiedColumns, c => c.Contains("orders") || c.Contains(".code") || c.Contains(".total"));
    }

    [Fact]
    public void DynamicCompute_EvaluatesDeepDagChainInTopologicalOrder_AndRejectsCycles()
    {
        var svc = new DynamicComputeService();
        // Define dependent columns intentionally in reverse order (d -> c -> b -> a) with 4 hops
        var columns = new List<SysColumnDto>
        {
            new() { ColumnCode = "d", ColumnName = "D", DataType = "NUMBER", Formula = "c + 1" },
            new() { ColumnCode = "c", ColumnName = "C", DataType = "NUMBER", Formula = "b * 2" },
            new() { ColumnCode = "b", ColumnName = "B", DataType = "NUMBER", Formula = "a + 5" },
            new() { ColumnCode = "a", ColumnName = "A", DataType = "NUMBER", Formula = "base_qty * 10" },
            new() { ColumnCode = "base_qty", ColumnName = "Qty", DataType = "NUMBER" }
        };

        var results = svc.EvaluateAndValidate(
            columns,
            new(),
            new() { ("r1", new Dictionary<string, object?> { ["base_qty"] = 3m }) });

        var eval = results.Single().EvaluatedData;
        Assert.Equal(30m, Convert.ToDecimal(eval["a"]));
        Assert.Equal(35m, Convert.ToDecimal(eval["b"]));
        Assert.Equal(70m, Convert.ToDecimal(eval["c"]));
        Assert.Equal(71m, Convert.ToDecimal(eval["d"]));

        var cyclicCols = new List<SysColumnDto>
        {
            new() { ColumnCode = "x", ColumnName = "X", DataType = "NUMBER", Formula = "y + 1" },
            new() { ColumnCode = "y", ColumnName = "Y", DataType = "NUMBER", Formula = "z + 1" },
            new() { ColumnCode = "z", ColumnName = "Z", DataType = "NUMBER", Formula = "x * 2" }
        };
        var cycleEx = Assert.Throws<FormulaCycleException>(() => DynamicComputeService.ValidateFormulaGraph(cyclicCols));
        Assert.Contains("x", cycleEx.CycleColumns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BusinessCalendar_SkipsWeekendsAndVietnamHolidays_AndWorkflowResolvesQuorum()
    {
        var recurring = new HashSet<(int Month, int Day)> { (4, 30), (5, 1) };
        var exact = new HashSet<DateOnly>();

        // 2026-04-30 (Thu holiday), 2026-05-01 (Fri holiday), 2026-05-02 (Sat), 2026-05-03 (Sun) -> 4 non-working days
        // From 2026-04-29 10:00 VN (03:00Z) to 2026-05-04 10:00 VN (03:00Z): only 29th (14h) + 4th (10h) = 24 business hours
        var startUtc = new DateTime(2026, 4, 29, 3, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 5, 4, 3, 0, 0, DateTimeKind.Utc);
        var bizHours = BusinessCalendarService.CalculateBusinessElapsedHours(startUtc, endUtc, exact, recurring);
        Assert.Equal(24d, bizHours, 2);

        Assert.Equal(1, WorkflowService.ResolveRequiredApprovals(new WorkflowStepDto { ApprovalMode = "ANY", QuorumCount = 3 }, 5));
        Assert.Equal(3, WorkflowService.ResolveRequiredApprovals(new WorkflowStepDto { ApprovalMode = "QUORUM", QuorumCount = 3 }, 5));
        Assert.Equal(4, WorkflowService.ResolveRequiredApprovals(new WorkflowStepDto { ApprovalMode = "ALL" }, 4));
    }

    [Fact]
    public void AiService_MaskPiiText_RedactsVietnamSensitiveFieldsPreservingAmounts()
    {
        var input = "Khach hang CCCD: 079201001234, SDT +84 901 234 567, email an.nguyen@corp.vn, STK: 190312345678, ngay 2026-11-18, tong tien 15000000.";
        var masked = AiService.MaskPiiText(input);

        Assert.DoesNotContain("079201001234", masked);
        Assert.DoesNotContain("an.nguyen@corp.vn", masked);
        Assert.DoesNotContain("190312345678", masked);
        Assert.DoesNotContain("901 234 567", masked);
        Assert.Contains("2026-11-18", masked);
        Assert.Contains("15000000", masked);
    }

    [Fact]
    public void AiService_Phase3Engines_ProduceStructuredOutputsAndSanitizeNl2Query()
    {
        var cols = new List<SysColumnDto>
        {
            new() { ColumnCode = "so_hd", ColumnName = "Số hóa đơn", DataType = "STRING" },
            new() { ColumnCode = "ngay_ct", ColumnName = "Ngày chứng từ", DataType = "DATE" },
            new() { ColumnCode = "tong_tien", ColumnName = "Tổng tiền", DataType = "CURRENCY" },
            new() { ColumnCode = "bo_phan", ColumnName = "Bộ phận", DataType = "SELECT" },
            new() { ColumnCode = "chi_tiet", ColumnName = "Chi tiết", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,thanh_tien" }
        };

        var mapSuggestions = AiService.SuggestExcelMappingsRuleBased(
            new() { "Ngay Chung Tu", "Tong Tien", "Cot La" },
            new() { new() { "18/11/2026", "1.250.000", "x" } },
            cols);
        Assert.Equal("ngay_ct", mapSuggestions[0].ColumnCode);
        Assert.Equal("DATE_DMY", mapSuggestions[0].TransformHint);
        Assert.Equal("tong_tien", mapSuggestions[1].ColumnCode);
        Assert.Equal("VND_INT", mapSuggestions[1].TransformHint);
        Assert.Null(mapSuggestions[2].ColumnCode);

        var ocr = AiService.ExtractDocumentFieldsRuleBased(
            "Số hóa đơn: HD-2026-99\nNgày: 18/11/2026\nTổng tiền thanh toán: 5.500.000\nLaptop Pro | 2 | 5500000",
            cols);
        Assert.Equal("HD-2026-99", ocr.Fields["so_hd"]);
        Assert.Equal("2026-11-18", ocr.Fields["ngay_ct"]);
        Assert.Single(ocr.SubTables["chi_tiet"]);

        var pendingRec = new AppRecordDto { Id = "r_new", RecordKey = "EXP-100", ApprovalStatus = "PENDING", Data = new() { ["tong_tien"] = 50000000m } };
        var history = new List<AppRecordDto>
        {
            new() { Id = "h1", Data = new() { ["tong_tien"] = 10000000m } },
            new() { Id = "h2", Data = new() { ["tong_tien"] = 12000000m } },
            new() { Id = "h3", Data = new() { ["tong_tien"] = 8000000m } },
        };
        var triage = AiService.ComputeWorkflowTriageRuleBased(pendingRec, "Chi phí", cols, history);
        Assert.Equal("MEDIUM", triage.RiskLevel);
        Assert.NotEmpty(triage.Anomalies);

        var copilot = AiService.SummarizeChatThreadRuleBased("Kế toán", new()
        {
            new() { Id = "m1", SenderUsername = "linh", Content = "Nhờ @hung kiểm tra gấp hóa đơn trước deadline thứ 6", CreatedAt = DateTime.UtcNow }
        });
        Assert.Single(copilot.ActionItems);
        Assert.Equal("hung", copilot.ActionItems[0].AssigneeUsername);
        Assert.Equal("URGENT", copilot.ActionItems[0].Priority);

        var sanitized = AiService.SanitizeNlFilterPlan(new AiNlFilterSpec
        {
            AggregateMetric = "SUM",
            MetricField = "tong_tien",
            GroupByField = "bo_phan",
            Conditions = new()
            {
                new() { Field = "approvalStatus", Op = "eq", Value = "PENDING" },
                new() { Field = "secret_hidden_col; DROP TABLE app_records", Op = "eq", Value = "1" }
            }
        }, cols);
        Assert.Single(sanitized.Conditions);
        Assert.Equal("approvalStatus", sanitized.Conditions[0].Field);
    }

    [Fact]
    public void Ecosystem_Nd123XmlParser_And_OpenApiMcpBuilders_WorkSafely()
    {
        const string sampleXml = """
            <HDon>
              <DLHDon>
                <TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26TAA</KHHDon><SHDon>0000123</SHDon><NLap>2026-11-18</NLap><DVTTe>VND</DVTTe></TTChung>
                <NDHDon>
                  <NBan><Ten>Cong ty ABC</Ten><MST>0311112222</MST></NBan>
                  <NMua><Ten>SmartApps VN</Ten><MST>0109998888</MST></NMua>
                  <DSHHDVu><HHDVu><THHDVu>Gói Cloud Enterprise</THHDVu><DVTinh>Gói</DVTinh><SLuong>1</SLuong><DGia>10000000</DGia><ThTien>10000000</ThTien></HHDVu></DSHHDVu>
                  <TToan><TgTCThue>10000000</TgTCThue><TgTThue>1000000</TgTThue><TgTTTBSo>11000000</TgTTTBSo></TToan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;

        var parsed = VietnamIntegrationService.ParseVnInvoiceXml(sampleXml);
        Assert.Equal("0000123", parsed["invoiceNo"]);
        Assert.Equal("0311112222", parsed["sellerTaxCode"]);
        Assert.Equal(11000000m, parsed["totalAmount"]);

        Assert.ThrowsAny<Exception>(() => VietnamIntegrationService.ParseVnInvoiceXml("<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><HDon/>"));

        var app = new SysAppDto { Id = "app_fin", Code = "FIN", Name = "Tài chính" };
        var tables = new List<(SysTableDto Table, List<SysColumnDto> Columns)>
        {
            (new SysTableDto { Id = "tbl_inv", TableName = "invoices", DisplayName = "Hóa đơn" },
             new List<SysColumnDto> { new() { ColumnCode = "so_hd", ColumnName = "Số HĐ", DataType = "STRING", IsRequired = true } })
        };

        var openapi = AppEcosystemBuilder.BuildOpenApiDocument(app, tables, "https://app.smartapps.local");
        Assert.Equal("3.0.3", openapi["openapi"]);
        var mcpTools = AppEcosystemBuilder.BuildMcpTools(app, tables);
        Assert.Equal(4, mcpTools.Count);

        Assert.True(VietnamIntegrationService.VerifyIpnSignature(
            "vietqr", "my_sepay_token", new Dictionary<string, object?>(), "{}", "Apikey my_sepay_token"));
        Assert.False(VietnamIntegrationService.VerifyIpnSignature(
            "vietqr", "my_sepay_token", new Dictionary<string, object?>(), "{}", "Apikey wrong_token"));

        var mappedXml = VietnamIntegrationService.MapVnInvoiceXmlToTableColumns(parsed, tables[0].Columns);
        Assert.Equal("0000123", mappedXml.Fields["so_hd"]);

        var promText = PlatformMetricsService.ToPrometheusText(new PlatformMetricsSnapshot
        {
            HttpRequestsTotal = 42,
            ActiveSseSubscribers = 3,
            DbProbeLatencyMs = 1.5
        });
        Assert.Contains("smartapps_http_requests_total 42", promText);
        Assert.Contains("smartapps_sse_subscribers_active 3", promText);
    }
}
