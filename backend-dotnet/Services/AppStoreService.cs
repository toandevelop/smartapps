using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class AppStoreService(
    IConfiguration config,
    MetadataService metadata,
    WorkflowService workflows,
    AutomationService automations,
    RecordService records,
    IntegrationService integrations,
    IHttpClientFactory httpClientFactory)
{
    private readonly string _connStr = config.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing Postgres connection string");

    private static readonly JsonSerializerOptions StoreJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CanonicalHashOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Lazy<IReadOnlyList<StorePackageEntry>> BuiltInStore = new(BuildBuiltInPackages);

    private sealed record StorePackageEntry(StoreAppPackageDto Package, StoreAppSummaryDto Summary, string JsonPayload);

    public IReadOnlyList<StoreAppPackageDto> GetBuiltInPackages() =>
        BuiltInStore.Value.Select(e => e.Package).ToList();

    private sealed record SuiteDefinition(
        string SuiteCode,
        string Name,
        string Tagline,
        string Description,
        string TargetAudience,
        string Icon,
        string Color,
        string BgColor,
        List<string> Highlights,
        List<string> PackageCodes);

    private static readonly IReadOnlyList<SuiteDefinition> BuiltInSuites =
    [
        new(
            SuiteCode: "SUITE_SME_OPS",
            Name: "Bộ Giải pháp Vận hành & Hành chính Số (SME Core)",
            Tagline: "Chuẩn hóa Công văn NĐ30, Hợp đồng, Tạm ứng & Lịch họp",
            Description: "Triển khai tức thì trục hành chính - văn phòng không giấy tờ: Quản lý công văn đi/đến & ký số nội bộ, kiểm soát hợp đồng, đề nghị thanh toán và điều phối phòng họp.",
            TargetAudience: "Ban Giám đốc, Văn thư Hành chính, Kế toán Nội bộ",
            Icon: "VerifiedUser",
            Color: "#1d4ed8",
            BgColor: "#dbeafe",
            Highlights:
            [
                "Quản lý Văn bản Đi/Đến & Trình ký số chuẩn Nghị định 30/2020/NĐ-CP",
                "Liên thông Hợp đồng - Đề nghị thanh toán - Phê duyệt đa cấp theo sơ đồ tổ chức",
                "Sẵn sàng dữ liệu mẫu và biểu mẫu in A4 doanh nghiệp Việt Nam"
            ],
            PackageCodes: ["DMS_VAN_BAN", "FINANCE", "CONTRACT", "MEETING"]),
        new(
            SuiteCode: "SUITE_COMMERCE_360",
            Name: "Bộ Giải pháp Kinh doanh – Mua sắm – Kho vận (O2O ERP)",
            Tagline: "Khép kín chuỗi Cung ứng từ Cơ hội Bán hàng tới Tồn kho WMS",
            Description: "Kết nối xuyên suốt đội ngũ Kinh doanh B2B/O2O, Đặt hàng Nhà cung cấp và Quản lý Xuất Nhập Tồn kho đa điểm theo thời gian thực.",
            TargetAudience: "Khối Kinh doanh, Phòng Mua hàng, Quản lý Kho vận",
            Icon: "Storefront",
            Color: "#ea580c",
            BgColor: "#ffedd5",
            Highlights:
            [
                "Quản lý phễu CRM, Báo giá & Đơn bán hàng tích hợp VietQR",
                "Đề nghị mua sắm, so sánh báo giá NCC và đối soát công nợ",
                "Kiểm soát phiếu nhập/xuất kho, cảnh báo tồn tối thiểu & quét QR"
            ],
            PackageCodes: ["CRM", "SALES_VN", "PROCURE_VN", "WMS"]),
        new(
            SuiteCode: "SUITE_HR_TALENT",
            Name: "Bộ Giải pháp Quản trị Nhân tài & Hiệu suất (HRM & OKR)",
            Tagline: "Tuyển dụng, Đào tạo, Đánh giá KPI/OKR và Bàn giao Nhân sự",
            Description: "Số hóa trọn vòng đời phát triển nhân sự: từ thu thập hồ sơ ứng viên, lộ trình onboarding/đào tạo, chấm điểm KPI/OKR định kỳ đến bàn giao tài sản.",
            TargetAudience: "Phòng Nhân sự (HR), Trưởng bộ phận, Ban Giám đốc",
            Icon: "Groups",
            Color: "#7c3aed",
            BgColor: "#ede9fe",
            Highlights:
            [
                "Public Form tiếp nhận CV ứng viên & quy trình phỏng vấn nhiều vòng",
                "Theo dõi kế hoạch đào tạo nội bộ, ngân sách và chứng chỉ sau khóa học",
                "Đánh giá KPI/OKR theo quý kèm checklist bàn giao công việc & tài sản"
            ],
            PackageCodes: ["RECRUIT", "TRAINING", "KPI_OKR", "HANDOVER_VN"]),
        new(
            SuiteCode: "SUITE_TECH_SERVICES",
            Name: "Bộ Giải pháp Dự án, CNTT & Tài sản Vận hành",
            Tagline: "Quản trị Dự án, IT Helpdesk SLA, Mua sắm & Đội xe Công vụ",
            Description: "Tối ưu nguồn lực kỹ thuật và tài sản doanh nghiệp: theo dõi tiến độ dự án, xử lý sự cố IT theo SLA, điều phối xe công vụ và mua sắm thiết bị.",
            TargetAudience: "PMO, Phòng CNTT, Hành chính Thiết bị & Đội xe",
            Icon: "Engineering",
            Color: "#0f766e",
            BgColor: "#ccfbf1",
            Highlights:
            [
                "Quản lý tiến độ dự án, phân bổ nguồn lực và cảnh báo trễ hạn",
                "IT Helpdesk tự động phân loại sự cố và theo dõi cam kết SLA",
                "Điều lệnh xe công vụ, định mức nhiên liệu & mua sắm trang thiết bị"
            ],
            PackageCodes: ["PROJECT", "HELPDESK", "FLEET", "PURCHASE"])
    ];

    public async Task<List<StoreSolutionSuiteDto>> ListSolutionSuitesAsync()
    {
        var installedApps = await metadata.GetAppsAsync();
        var installedByCode = installedApps
            .GroupBy(a => a.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var builtInByCode = BuiltInStore.Value
            .ToDictionary(e => e.Package.PackageCode, e => e.Summary, StringComparer.OrdinalIgnoreCase);

        var result = new List<StoreSolutionSuiteDto>();
        foreach (var def in BuiltInSuites)
        {
            var appItems = new List<StoreSolutionSuiteAppItemDto>();
            foreach (var code in def.PackageCodes)
            {
                builtInByCode.TryGetValue(code, out var sum);
                installedByCode.TryGetValue(code, out var inst);
                appItems.Add(new StoreSolutionSuiteAppItemDto
                {
                    PackageCode = code,
                    Name = sum?.Name ?? code,
                    Icon = sum?.Icon ?? "Apps",
                    Color = sum?.Color ?? def.Color,
                    IsInstalled = inst != null,
                    InstalledAppCode = inst?.Code
                });
            }

            result.Add(new StoreSolutionSuiteDto
            {
                SuiteCode = def.SuiteCode,
                Name = def.Name,
                Tagline = def.Tagline,
                Description = def.Description,
                TargetAudience = def.TargetAudience,
                Icon = def.Icon,
                Color = def.Color,
                BgColor = def.BgColor,
                Highlights = def.Highlights,
                Apps = appItems,
                InstalledCount = appItems.Count(a => a.IsInstalled),
                TotalCount = appItems.Count
            });
        }
        return result;
    }

    public async Task<InstallSuiteResultDto> InstallSolutionSuiteAsync(
        string suiteCode,
        bool includeSampleData,
        string actor)
    {
        var def = BuiltInSuites.FirstOrDefault(s => s.SuiteCode.Equals(suiteCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Không tìm thấy Bộ giải pháp '{suiteCode}'.");

        var installedApps = await metadata.GetAppsAsync();
        var installedByCode = installedApps
            .GroupBy(a => a.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var builtInByCode = BuiltInStore.Value
            .ToDictionary(e => e.Package.PackageCode, e => e.Summary, StringComparer.OrdinalIgnoreCase);

        var res = new InstallSuiteResultDto
        {
            SuiteCode = def.SuiteCode,
            SuiteName = def.Name
        };

        foreach (var pkgCode in def.PackageCodes)
        {
            var pkgName = builtInByCode.TryGetValue(pkgCode, out var s) ? s.Name : pkgCode;
            if (installedByCode.TryGetValue(pkgCode, out var existing))
            {
                res.SkippedCount++;
                res.Outcomes.Add(new InstallSuiteAppOutcomeDto
                {
                    PackageCode = pkgCode,
                    Name = pkgName,
                    Status = "ALREADY_INSTALLED",
                    AppId = existing.Id,
                    AppCode = existing.Code,
                    Message = "Ứng dụng đã có sẵn trong không gian làm việc."
                });
                continue;
            }

            try
            {
                var app = await InstallStoreAppAsync(pkgCode, new InstallStoreAppRequest
                {
                    IncludeSampleData = includeSampleData
                }, actor);
                res.InstalledCount++;
                res.Outcomes.Add(new InstallSuiteAppOutcomeDto
                {
                    PackageCode = pkgCode,
                    Name = app.Name,
                    Status = "INSTALLED",
                    AppId = app.Id,
                    AppCode = app.Code,
                    Message = "Cài đặt thành công."
                });
            }
            catch (Exception ex)
            {
                res.FailedCount++;
                res.Outcomes.Add(new InstallSuiteAppOutcomeDto
                {
                    PackageCode = pkgCode,
                    Name = pkgName,
                    Status = "FAILED",
                    Message = ex.Message
                });
            }
        }

        return res;
    }

    public static string ComputeCanonicalChecksum(StoreAppPackageDto pkg)
    {
        var copy = new StoreAppPackageDto
        {
            PackageFormat = pkg.PackageFormat,
            ExportedAt = DateTime.UnixEpoch,
            ChecksumSha256 = string.Empty,
            PackageCode = pkg.PackageCode,
            Name = pkg.Name,
            Description = pkg.Description,
            Department = pkg.Department,
            Category = pkg.Category,
            Version = pkg.Version,
            Publisher = pkg.Publisher,
            Icon = pkg.Icon,
            Color = pkg.Color,
            BgColor = pkg.BgColor,
            Tags = pkg.Tags,
            Highlights = pkg.Highlights,
            Features = pkg.Features,
            Categories = pkg.Categories,
            Modules = pkg.Modules,
            Widgets = pkg.Widgets,
            Workflows = pkg.Workflows,
            Automations = pkg.Automations,
            PrintTemplates = pkg.PrintTemplates,
            SavedViews = pkg.SavedViews,
            PluginBindings = pkg.PluginBindings,
            SampleRecords = pkg.SampleRecords
        };
        var json = JsonSerializer.Serialize(copy, CanonicalHashOpts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public StoreManifestDto GetPublicManifest(string baseUrl)
    {
        var cleanBase = baseUrl.TrimEnd('/');
        var summaries = BuiltInStore.Value.Select(e =>
        {
            var s = CloneSummary(e.Summary);
            s.DownloadUrl = $"{cleanBase}/api/store/apps/{Uri.EscapeDataString(s.PackageCode)}/download";
            return s;
        }).ToList();

        return new StoreManifestDto
        {
            StoreName = "SmartApps Online Store Hub",
            StoreVersion = "2.0.0",
            GeneratedAt = DateTime.UtcNow,
            Apps = summaries
        };
    }

    public async Task<List<StoreAppSummaryDto>> ListStoreAppsAsync(
        string? category = null,
        string? search = null,
        string? registryId = null)
    {
        var installedApps = await metadata.GetAppsAsync();
        var installedByCode = installedApps
            .GroupBy(a => a.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var telemetry = await LoadInstallStatsAsync(conn);
        var remoteEntries = await LoadEnabledRegistryItemsAsync(conn, registryId);

        var combined = new List<StoreAppSummaryDto>();
        if (string.IsNullOrWhiteSpace(registryId) || string.Equals(registryId, "OFFICIAL", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in BuiltInStore.Value)
            {
                var item = CloneSummary(entry.Summary);
                item.DownloadUrl = $"/api/store/apps/{Uri.EscapeDataString(item.PackageCode)}/download";
                EnrichSummaryState(item, installedByCode, telemetry);
                combined.Add(item);
            }
        }

        foreach (var rem in remoteEntries)
        {
            EnrichSummaryState(rem, installedByCode, telemetry);
            combined.Add(rem);
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cat = category.Trim();
            combined = combined.Where(a => a.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            combined = combined.Where(a =>
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.PackageCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Department.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return combined;
    }

    public async Task<StoreAppDetailDto?> GetStoreAppDetailAsync(string code, string? registryId = null)
    {
        var pkg = await ResolvePackageAsync(code, registryId);
        if (pkg == null) return null;

        var list = await ListStoreAppsAsync(null, null, registryId);
        var summary = list.FirstOrDefault(a =>
            a.PackageCode.Equals(pkg.PackageCode, StringComparison.OrdinalIgnoreCase))
            ?? BuildSummaryFromPackage(pkg, Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(pkg, StoreJsonOpts)));

        return new StoreAppDetailDto
        {
            Summary = summary,
            Modules = pkg.Modules,
            Workflows = pkg.Workflows,
            Automations = pkg.Automations,
            PrintTemplates = pkg.PrintTemplates,
            SavedViews = pkg.SavedViews,
            PluginBindings = pkg.PluginBindings
        };
    }

    public async Task<(string FileName, byte[] ContentBytes, StoreAppPackageDto Package)?> DownloadPackageAsync(
        string code,
        string? registryId = null,
        string? actor = null)
    {
        var pkg = await ResolvePackageAsync(code, registryId);
        if (pkg == null) return null;

        if (!string.IsNullOrWhiteSpace(actor))
        {
            await RecordStoreEventAsync(pkg.PackageCode, registryId, null, pkg.Version, pkg.ChecksumSha256, "DOWNLOAD", actor);
        }

        var json = JsonSerializer.Serialize(pkg, StoreJsonOpts);
        var safeCode = pkg.PackageCode.ToLowerInvariant();
        return ($"smartapp-{safeCode}-v{pkg.Version}.smartapp.json", Encoding.UTF8.GetBytes(json), pkg);
    }

    public async Task<SysAppDto> InstallStoreAppAsync(string code, InstallStoreAppRequest req, string actor)
    {
        var pkg = await ResolvePackageAsync(code, req.RegistryId)
            ?? throw new KeyNotFoundException($"Không tìm thấy gói ứng dụng '{code}' trên Kho Apps Online.");

        return await InstallPackageAsync(pkg, req.CustomCode, req.CustomName, req.IncludeSampleData, actor, req.RegistryId);
    }

    public async Task<SysAppDto> InstallFromUrlAsync(InstallStorePackageUrlRequest req, string actor)
    {
        var pkg = await FetchRemotePackageAsync(req.PackageUrl);
        return await InstallPackageAsync(pkg, req.CustomCode, req.CustomName, req.IncludeSampleData, actor, null);
    }

    public async Task<SysAppDto> InstallUploadedPayloadAsync(InstallStorePackageBodyRequest req, string actor)
    {
        if (req.Package.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Dữ liệu gói .smartapp.json không hợp lệ.");

        var rawText = req.Package.GetRawText();
        if (req.Package.TryGetProperty("packageCode", out _) || req.Package.TryGetProperty("PackageCode", out _))
        {
            var pkg = JsonSerializer.Deserialize<StoreAppPackageDto>(rawText, StoreJsonOpts)
                ?? throw new ArgumentException("Không đọc được nội dung StoreAppPackageDto.");
            VerifyOptionalChecksum(pkg);
            return await InstallPackageAsync(pkg, req.CustomCode, req.CustomName, req.IncludeSampleData, actor, null);
        }

        // Backward compatible with legacy AppBlueprintDto (.blueprint.json)
        if (req.Package.TryGetProperty("app", out _) || req.Package.TryGetProperty("App", out _))
        {
            var bp = JsonSerializer.Deserialize<AppBlueprintDto>(rawText, StoreJsonOpts)
                ?? throw new ArgumentException("Không đọc được nội dung AppBlueprintDto.");
            var imported = await metadata.ImportAppBlueprintAsync(bp, req.CustomCode, req.CustomName);
            await RecordStoreEventAsync(imported.Code, null, imported.Id, imported.Version, null, "INSTALL_FILE", actor);
            return imported;
        }

        throw new ArgumentException("Định dạng tệp không được hỗ trợ (cần smartapps-package/2.0 hoặc App Blueprint).");
    }

    public async Task<SysAppDto> InstallPackageAsync(
        StoreAppPackageDto pkg,
        string? customCode,
        string? customName,
        bool includeSampleData,
        string actor,
        string? registryId)
    {
        if (string.IsNullOrWhiteSpace(pkg.PackageCode) || pkg.Modules.Count == 0)
            throw new ArgumentException("Gói ứng dụng không hợp lệ (thiếu PackageCode hoặc Modules).");

        var code = string.IsNullOrWhiteSpace(customCode)
            ? pkg.PackageCode.Trim().ToUpperInvariant().Replace(" ", "_")
            : customCode.Trim().ToUpperInvariant().Replace(" ", "_");
        if (await metadata.GetAppByCodeOrIdAsync(code) != null)
        {
            code = $"{code}_{DateTime.UtcNow:HHmmss}";
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var catSpecs = pkg.Categories.Select(c => new TemplateCategorySpec(
            c.CategoryCode,
            c.CategoryName,
            c.Description,
            c.Items.Select(i => new TemplateCategoryItemSpec(i.ItemCode, i.ItemName, i.Color, i.OrderIndex)).ToList()
        )).ToList();
        await AppUpgradeService.EnsureCategoriesAsync(conn, catSpecs);

        var app = await metadata.UpsertAppAsync(new UpsertAppRequest
        {
            Code = code,
            Name = string.IsNullOrWhiteSpace(customName) ? pkg.Name : customName.Trim(),
            Description = pkg.Description,
            Department = pkg.Department,
            Icon = pkg.Icon,
            Color = pkg.Color,
            BgColor = pkg.BgColor,
            Version = string.IsNullOrWhiteSpace(pkg.Version) ? "2.0.0" : pkg.Version,
            OrderIndex = 120
        });

        var createdTables = new Dictionary<string, SysTableDto>(StringComparer.OrdinalIgnoreCase);
        var createdTableSpecs = new List<(SysTableDto Table, StorePackageTableDto Spec)>();
        var columnsByTable = new Dictionary<string, List<SysColumnDto>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Pass 1: create all modules & tables first so forward-referencing InternalRefTableName lookups resolve cleanly.
            for (int modIdx = 0; modIdx < pkg.Modules.Count; modIdx++)
            {
                var modSpec = pkg.Modules[modIdx];
                var mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
                {
                    AppId = app.Id,
                    Code = modSpec.Code,
                    Name = modSpec.Name,
                    Icon = modSpec.Icon,
                    Description = modSpec.Description,
                    OrderIndex = modIdx + 1
                });

                for (int tblIdx = 0; tblIdx < modSpec.Tables.Count; tblIdx++)
                {
                    var tblSpec = modSpec.Tables[tblIdx];
                    var tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
                    {
                        AppId = app.Id,
                        ModuleId = mod.Id,
                        TableName = tblSpec.TableName,
                        DisplayName = tblSpec.DisplayName,
                        Description = tblSpec.Description,
                        PrimaryView = tblSpec.PrimaryView,
                        RowScope = tblSpec.RowScope,
                        ApprovalEnabled = tblSpec.ApprovalEnabled,
                        ApprovalField = tblSpec.ApprovalField,
                        ApproverRoles = tblSpec.ApproverRoles.Count > 0 ? tblSpec.ApproverRoles : ["ADMIN", "HR_MANAGER", "ASSET_LEAD"],
                        PublicFormEnabled = tblSpec.PublicFormEnabled,
                        OrderIndex = tblIdx + 1
                    });
                    createdTables[tblSpec.TableName] = tbl;
                    createdTableSpecs.Add((tbl, tblSpec));
                }
            }

            // Pass 2: create columns across all tables with full table lookup mapping and attach FormRules.
            foreach (var (tbl, tblSpec) in createdTableSpecs)
            {
                var savedCols = new List<SysColumnDto>();
                foreach (var colSpec in tblSpec.Columns)
                {
                    var refAppId = colSpec.RefAppId;
                    var refTableId = colSpec.RefTableId;
                    if (!string.IsNullOrWhiteSpace(colSpec.InternalRefTableName) &&
                        createdTables.TryGetValue(colSpec.InternalRefTableName, out var targetTbl))
                    {
                        refAppId = app.Id;
                        refTableId = targetTbl.Id;
                    }

                    var col = await metadata.UpsertColumnAsync(new UpsertColumnRequest
                    {
                        TableId = tbl.Id,
                        ColumnCode = colSpec.ColumnCode,
                        ColumnName = colSpec.ColumnName,
                        ExcelHeader = colSpec.ColumnName,
                        DataType = colSpec.DataType,
                        IsRequired = colSpec.IsRequired,
                        IsIdentityKey = colSpec.IsIdentityKey,
                        ShowInTable = colSpec.ShowInTable,
                        ShowInForm = colSpec.ShowInForm,
                        ShowInParentForm = colSpec.ShowInParentForm,
                        Width = colSpec.Width,
                        ValidationRegex = colSpec.ValidationRegex,
                        RefCategoryCode = colSpec.RefCategoryCode,
                        AllowQuickCreate = colSpec.AllowQuickCreate,
                        RefAppId = refAppId,
                        RefTableId = refTableId,
                        RefValueField = colSpec.RefValueField,
                        RefDisplayField = colSpec.RefDisplayField,
                        LookupFilterField = colSpec.LookupFilterField,
                        LookupSourceField = colSpec.LookupSourceField,
                        Formula = colSpec.Formula,
                        DefaultValue = colSpec.DefaultValue,
                        HiddenForRoles = colSpec.HiddenForRoles ?? [],
                        ReadOnlyForRoles = colSpec.ReadOnlyForRoles ?? [],
                        OrderIndex = colSpec.OrderIndex
                    });
                    savedCols.Add(col);
                }
                columnsByTable[tblSpec.TableName] = savedCols;

                if (tblSpec.FormRules.Count > 0)
                {
                    await metadata.SetFormRulesAsync(tbl.Id, tblSpec.FormRules);
                }
            }

            var wfSpecs = pkg.Workflows.Select(w => new TemplateWorkflowSpec(w.Name, w.Description, w.TableName, w.TriggerType, w.Steps)).ToList();
            var autoSpecs = pkg.Automations.Select(a => new TemplateAutomationSpec(
                a.Name, a.Description, a.TriggerTableName, a.TriggerEvent, a.ConditionField,
                a.ConditionOperator, a.ConditionValue, null, null, a.TargetTableName,
                a.ActionType, a.MatchTargetField, a.MatchSourceField, a.FieldMappings,
                a.NotifyRecipient, a.NotifyTemplate)).ToList();
            var printSpecs = pkg.PrintTemplates.Select(p => new TemplatePrintTemplateSpec(
                p.TableName, p.Name, p.PaperSize, p.Orientation, p.IsDefault,
                p.WatermarkDraft, p.RequireApproved, p.HtmlTemplate, p.CssStyles)).ToList();
            var viewSpecs = pkg.SavedViews.Select(v => new TemplateSavedViewSpec(
                v.TableName, v.Name, v.IsDefault, v.IsShared, v.FiltersJson, v.SortField, v.SortDir)).ToList();
            var widgetSpecs = pkg.Widgets.Select(w => new TemplateWidgetSpec(
                w.TableName, w.Title, w.WidgetType, w.MetricField, w.GroupField,
                w.FilterField, w.FilterValue, w.ColSpan, w.Color, w.OrderIndex)).ToList();
            var bindingSpecs = pkg.PluginBindings.Select(b => new TemplatePluginBindingSpec(
                b.PluginCode, b.TableName, b.FieldMapping)).ToList();

            await AppUpgradeService.ApplyV2ArtifactsAsync(
                metadata, conn, app.Id, createdTables,
                wfSpecs, autoSpecs, printSpecs, viewSpecs, widgetSpecs, bindingSpecs, actor);

            if (includeSampleData && pkg.SampleRecords.Count > 0)
            {
                await SeedPackageSampleRecordsAsync(conn, app.Id, createdTables, columnsByTable, pkg.SampleRecords, actor);
            }

            var validation = await metadata.ValidateAppForPublishAsync(app.Id);
            if (validation.CanPublish)
            {
                await metadata.PublishAppAsync(app.Id, actor, $"Cài đặt từ Kho Apps Online: {pkg.PackageCode} v{pkg.Version}");
            }

            await RecordStoreEventAsync(pkg.PackageCode, registryId, app.Id, pkg.Version, pkg.ChecksumSha256, "INSTALL", actor);

            return (await metadata.GetAppByCodeOrIdAsync(app.Id))!;
        }
        catch
        {
            // Compensating rollback so failed installs never leave partial orphan apps
            try
            {
                await metadata.DeleteAppAsync(app.Id);
            }
            catch
            {
                // Ignore cleanup failures and rethrow original error
            }
            throw;
        }
    }

    public async Task<StoreAppPackageDto?> ExportInstalledAppAsPackageAsync(string codeOrId)
    {
        var tree = await metadata.GetAppRuntimeTreeAsync(codeOrId);
        if (tree == null) return null;

        var bp = await metadata.ExportAppBlueprintAsync(tree.App.Id);
        if (bp == null) return null;

        var tblIdToName = tree.Modules.SelectMany(m => m.Tables)
            .ToDictionary(t => t.Table.Id, t => t.Table.TableName, StringComparer.OrdinalIgnoreCase);

        var catCodes = tree.Modules.SelectMany(m => m.Tables).SelectMany(t => t.Columns)
            .Select(c => c.RefCategoryCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catDtos = new List<StorePackageCategoryDto>();
        var allCats = catCodes.Count > 0 ? await metadata.GetCategoriesAsync() : [];
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        foreach (var catCode in catCodes)
        {
            if (catCode == null) continue;
            var cat = allCats.FirstOrDefault(c => c.CategoryCode.Equals(catCode, StringComparison.OrdinalIgnoreCase));
            var items = await metadata.GetCategoryItemsAsync(catCode);
            catDtos.Add(new StorePackageCategoryDto
            {
                CategoryCode = catCode,
                CategoryName = cat?.CategoryName ?? catCode,
                Description = cat?.Description ?? string.Empty,
                Items = items.Select(i => new StorePackageCategoryItemDto
                {
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    Color = i.Color ?? "#1a73e8",
                    OrderIndex = i.OrderIndex
                }).ToList()
            });
        }

        var modDtos = tree.Modules.Select(m => new StorePackageModuleDto
        {
            Code = m.Module.Code,
            Name = m.Module.Name,
            Icon = m.Module.Icon,
            Description = m.Module.Description ?? string.Empty,
            Tables = m.Tables.Select(t => new StorePackageTableDto
            {
                TableName = t.Table.TableName,
                DisplayName = t.Table.DisplayName,
                Description = t.Table.Description ?? string.Empty,
                PrimaryView = t.Table.PrimaryView,
                RowScope = t.Table.RowScope,
                ApprovalEnabled = t.Table.ApprovalEnabled,
                ApprovalField = t.Table.ApprovalField ?? "trang_thai_duyet",
                ApproverRoles = t.Table.ApproverRoles,
                PublicFormEnabled = t.Table.PublicFormEnabled,
                FormRules = t.Table.FormRules,
                Columns = t.Columns.Select(c =>
                {
                    string? internalName = null;
                    var isInternal = !string.IsNullOrWhiteSpace(c.RefTableId) && tblIdToName.TryGetValue(c.RefTableId, out internalName);
                    return new StorePackageColumnDto
                    {
                        ColumnCode = c.ColumnCode,
                        ColumnName = c.ColumnName,
                        DataType = c.DataType,
                        IsRequired = c.IsRequired,
                        IsIdentityKey = c.IsIdentityKey,
                        ShowInTable = c.ShowInTable,
                        ShowInForm = c.ShowInForm,
                        ShowInParentForm = c.ShowInParentForm,
                        Width = c.Width,
                        ValidationRegex = c.ValidationRegex,
                        RefCategoryCode = c.RefCategoryCode,
                        AllowQuickCreate = c.AllowQuickCreate,
                        RefAppId = isInternal ? null : c.RefAppId,
                        RefTableId = isInternal ? null : c.RefTableId,
                        InternalRefTableName = isInternal ? internalName : null,
                        RefValueField = c.RefValueField,
                        RefDisplayField = c.RefDisplayField,
                        LookupFilterField = c.LookupFilterField,
                        LookupSourceField = c.LookupSourceField,
                        Formula = c.Formula,
                        DefaultValue = c.DefaultValue,
                        HiddenForRoles = c.HiddenForRoles,
                        ReadOnlyForRoles = c.ReadOnlyForRoles,
                        OrderIndex = c.OrderIndex
                    };
                }).ToList()
            }).ToList()
        }).ToList();

        var printDtos = new List<StorePackagePrintTemplateDto>();
        foreach (var t in tree.Modules.SelectMany(m => m.Tables))
        {
            var pts = await metadata.GetPrintTemplatesAsync(t.Table.Id);
            printDtos.AddRange(pts.Select(p => new StorePackagePrintTemplateDto
            {
                TableName = t.Table.TableName,
                Name = p.Name,
                PaperSize = p.PaperSize,
                Orientation = p.Orientation,
                IsDefault = p.IsDefault,
                WatermarkDraft = p.WatermarkDraft,
                RequireApproved = p.RequireApproved,
                HtmlTemplate = p.HtmlTemplate,
                CssStyles = p.CssStyles
            }));
        }

        var wfDtos = (await workflows.GetWorkflowsAsync(tree.App.Id))
            .Where(w => tblIdToName.ContainsKey(w.TableId))
            .Select(w => new StorePackageWorkflowDto
            {
                Name = w.Name,
                Description = w.Description ?? string.Empty,
                TableName = tblIdToName[w.TableId],
                TriggerType = w.TriggerType,
                Steps = w.Steps
            }).ToList();

        var autoDtos = (await automations.GetAutomationsAsync())
            .Where(a => a.SourceAppId == tree.App.Id && tblIdToName.ContainsKey(a.SourceTableId))
            .Select(a => new StorePackageAutomationDto
            {
                Name = a.Name,
                Description = a.Description ?? string.Empty,
                TriggerTableName = tblIdToName[a.SourceTableId],
                TriggerEvent = a.TriggerEvent,
                ConditionField = a.ConditionField,
                ConditionOperator = a.ConditionOperator,
                ConditionValue = a.ConditionValue,
                TargetTableName = !string.IsNullOrWhiteSpace(a.TargetTableId) && tblIdToName.TryGetValue(a.TargetTableId, out var tt) ? tt : null,
                ActionType = a.ActionType,
                MatchSourceField = a.MatchSourceField,
                MatchTargetField = a.MatchTargetField,
                FieldMappings = a.FieldMappings,
                NotifyRecipient = a.NotifyRecipient,
                NotifyTemplate = a.NotifyTemplate
            }).ToList();

        var viewDtos = new List<StorePackageSavedViewDto>();
        await using (var vCmd = new NpgsqlCommand(
            "SELECT table_id, name, filters::text, sort_field, sort_dir, is_shared FROM sys_saved_views WHERE app_id = @app AND is_shared = TRUE", conn))
        {
            vCmd.Parameters.AddWithValue("app", tree.App.Id);
            await using var r = await vCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var tid = r.GetString(0);
                if (!tblIdToName.TryGetValue(tid, out var tName)) continue;
                viewDtos.Add(new StorePackageSavedViewDto
                {
                    TableName = tName,
                    Name = r.GetString(1),
                    FiltersJson = r.GetString(2),
                    SortField = r.IsDBNull(3) ? null : r.GetString(3),
                    SortDir = r.IsDBNull(4) ? "DESC" : r.GetString(4),
                    IsShared = r.GetBoolean(5)
                });
            }
        }

        var bindingDtos = (await integrations.ListAppPluginBindingsAsync(tree.App.Id))
            .Where(b => !string.IsNullOrWhiteSpace(b.TableId) && tblIdToName.ContainsKey(b.TableId))
            .Select(b => new StorePackagePluginBindingDto
            {
                PluginCode = b.PluginCode,
                TableName = tblIdToName[b.TableId!],
                FieldMapping = b.FieldMapping
            }).ToList();

        var widgetDtos = bp.Widgets
            .Where(w => tblIdToName.ContainsKey(w.TableId))
            .Select(w => new StorePackageWidgetDto
            {
                TableName = tblIdToName[w.TableId],
                Title = w.Title,
                WidgetType = w.WidgetType,
                MetricField = w.MetricField,
                GroupField = w.GroupField,
                FilterField = w.FilterField,
                FilterValue = w.FilterValue,
                ColSpan = w.ColSpan,
                Color = w.Color,
                OrderIndex = w.OrderIndex
            }).ToList();

        var sampleDtos = new List<StorePackageRecordDto>();
        foreach (var t in tree.Modules.SelectMany(m => m.Tables))
        {
            var (tblItems, _) = await records.QueryRecordsAsync(tree.App.Id, null, t.Table.Id, null, null, page: 1, limit: 10);
            sampleDtos.AddRange(tblItems.Select(item => new StorePackageRecordDto
            {
                TableName = t.Table.TableName,
                Data = item.Data
            }));
        }

        var pkg = new StoreAppPackageDto
        {
            PackageFormat = "smartapps-package/2.0",
            ExportedAt = DateTime.UtcNow,
            PackageCode = tree.App.Code,
            Name = tree.App.Name,
            Description = tree.App.Description ?? string.Empty,
            Department = tree.App.Department ?? "Doanh nghiệp",
            Category = "Ứng dụng Tùy chỉnh",
            Version = tree.App.Version,
            Publisher = "Workspace Export",
            Icon = tree.App.Icon,
            Color = tree.App.Color,
            BgColor = tree.App.BgColor,
            Tags = [tree.App.Code.ToLowerInvariant(), "v2.0", "exported"],
            Features = ["SmartApps 2.0", "Portable Package"],
            Categories = catDtos,
            Modules = modDtos,
            Widgets = widgetDtos,
            Workflows = wfDtos,
            Automations = autoDtos,
            PrintTemplates = printDtos,
            SavedViews = viewDtos,
            PluginBindings = bindingDtos,
            SampleRecords = sampleDtos
        };
        pkg.ChecksumSha256 = ComputeCanonicalChecksum(pkg);
        return pkg;
    }

    public async Task<List<StoreRegistryDto>> ListRegistriesAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, name, registry_url, description, is_enabled,
                   jsonb_array_length(cached_manifest), last_synced_at, last_error, created_at
            FROM sys_store_registries
            ORDER BY created_at DESC
            """, conn);
        var list = new List<StoreRegistryDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new StoreRegistryDto
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                RegistryUrl = r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                IsEnabled = r.GetBoolean(4),
                AppCount = r.GetInt32(5),
                LastSyncedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
                LastError = r.IsDBNull(7) ? null : r.GetString(7),
                CreatedAt = r.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<StoreRegistryDto> UpsertAndSyncRegistryAsync(UpsertStoreRegistryRequest req, string actor)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Tên nguồn kho online là bắt buộc.");
        var validatedUri = ValidateStoreUri(req.RegistryUrl);

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"reg_{Guid.NewGuid().ToString("N")[..10]}" : req.Id.Trim();
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO sys_store_registries (id, name, registry_url, description, is_enabled, created_by, updated_at)
            VALUES (@id, @name, @url, @desc, @en, @cb, CURRENT_TIMESTAMP)
            ON CONFLICT (registry_url) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                is_enabled = EXCLUDED.is_enabled,
                updated_at = CURRENT_TIMESTAMP
            RETURNING id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", req.Name.Trim());
            cmd.Parameters.AddWithValue("url", validatedUri.ToString());
            cmd.Parameters.Add(new NpgsqlParameter("desc", NpgsqlDbType.Text) { Value = (object?)req.Description?.Trim() ?? DBNull.Value });
            cmd.Parameters.AddWithValue("en", req.IsEnabled);
            cmd.Parameters.AddWithValue("cb", actor);
            id = (string)(await cmd.ExecuteScalarAsync())!;
        }

        return (await SyncRegistryAsync(id))!;
    }

    public async Task<StoreRegistryDto?> SyncRegistryAsync(string registryId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        string? url = null, name = null;
        await using (var readCmd = new NpgsqlCommand("SELECT registry_url, name FROM sys_store_registries WHERE id = @id", conn))
        {
            readCmd.Parameters.AddWithValue("id", registryId);
            await using var r = await readCmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            url = r.GetString(0);
            name = r.GetString(1);
        }

        var baseUri = ValidateStoreUri(url!);
        string manifestJson = "[]";
        string? lastError = null;
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var resp = await client.GetAsync(baseUri);
            if (resp.RequestMessage?.RequestUri != null) ValidateStoreUri(resp.RequestMessage.RequestUri.ToString());
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            var items = ParseRemoteManifest(raw, baseUri, registryId, name!);
            manifestJson = JsonSerializer.Serialize(items, StoreJsonOpts);
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        await using (var upCmd = new NpgsqlCommand("""
            UPDATE sys_store_registries
            SET cached_manifest = @man::jsonb,
                last_synced_at = CASE WHEN @err IS NULL THEN CURRENT_TIMESTAMP ELSE last_synced_at END,
                last_error = @err,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id
            """, conn))
        {
            upCmd.Parameters.AddWithValue("id", registryId);
            upCmd.Parameters.AddWithValue("man", manifestJson);
            upCmd.Parameters.Add(new NpgsqlParameter("err", NpgsqlDbType.Text) { Value = (object?)lastError ?? DBNull.Value });
            await upCmd.ExecuteNonQueryAsync();
        }

        return (await ListRegistriesAsync()).FirstOrDefault(x => x.Id == registryId);
    }

    public async Task<StoreRegistryDto?> ToggleRegistryAsync(string registryId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_store_registries
            SET is_enabled = NOT is_enabled,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", registryId);
        if (await cmd.ExecuteNonQueryAsync() == 0) return null;
        return (await ListRegistriesAsync()).FirstOrDefault(x => x.Id == registryId);
    }

    public async Task<List<StoreInstallHistoryDto>> ListHistoryAsync(int limit = 40)
    {
        var capped = Math.Clamp(limit, 1, 200);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, package_code, registry_id, app_id, installed_version, package_checksum, action_type, actor, created_at
            FROM sys_store_installs
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("lim", capped);
        var list = new List<StoreInstallHistoryDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new StoreInstallHistoryDto
            {
                Id = r.GetString(0),
                PackageCode = r.GetString(1),
                RegistryId = r.IsDBNull(2) ? null : r.GetString(2),
                AppId = r.IsDBNull(3) ? null : r.GetString(3),
                InstalledVersion = r.GetString(4),
                PackageChecksum = r.IsDBNull(5) ? null : r.GetString(5),
                ActionType = r.GetString(6),
                Actor = r.GetString(7),
                CreatedAt = r.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<bool> DeleteRegistryAsync(string registryId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_store_registries WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", registryId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private async Task<StoreAppPackageDto?> ResolvePackageAsync(string code, string? registryId)
    {
        var key = code.Trim();
        if (string.IsNullOrWhiteSpace(registryId) || registryId.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase))
        {
            var builtIn = BuiltInStore.Value.FirstOrDefault(e =>
                e.Package.PackageCode.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (builtIn != null) return builtIn.Package;
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var remoteItems = await LoadEnabledRegistryItemsAsync(conn, registryId);
        var matched = remoteItems.FirstOrDefault(r =>
            r.PackageCode.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (matched == null || string.IsNullOrWhiteSpace(matched.DownloadUrl))
            return null;

        return await FetchRemotePackageAsync(matched.DownloadUrl, matched.ChecksumSha256);
    }

    private async Task<StoreAppPackageDto> FetchRemotePackageAsync(string packageUrl, string? expectedChecksum = null)
    {
        var uri = ValidateStoreUri(packageUrl);
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var resp = await client.GetAsync(uri);
        if (resp.RequestMessage?.RequestUri != null) ValidateStoreUri(resp.RequestMessage.RequestUri.ToString());
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync();
        if (Encoding.UTF8.GetByteCount(raw) > 10 * 1024 * 1024)
            throw new ArgumentException("Kích thước gói ứng dụng vượt quá giới hạn 10 MB.");

        var pkg = JsonSerializer.Deserialize<StoreAppPackageDto>(raw, StoreJsonOpts)
            ?? throw new ArgumentException("Không đọc được nội dung gói ứng dụng từ URL.");

        if (!string.IsNullOrWhiteSpace(expectedChecksum) &&
            !string.Equals(ComputeCanonicalChecksum(pkg), expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Checksum SHA-256 của gói tải về không khớp manifest kho ứng dụng.");
        }
        VerifyOptionalChecksum(pkg);
        return pkg;
    }

    private static void VerifyOptionalChecksum(StoreAppPackageDto pkg)
    {
        if (string.IsNullOrWhiteSpace(pkg.ChecksumSha256)) return;
        var actual = ComputeCanonicalChecksum(pkg);
        if (!string.Equals(actual, pkg.ChecksumSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Checksum SHA-256 trong gói .smartapp.json không khớp (tệp có thể đã bị chỉnh sửa).");
    }

    private static Uri ValidateStoreUri(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl?.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("URL nguồn kho hoặc gói ứng dụng không hợp lệ.");
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Chỉ hỗ trợ giao thức HTTP hoặc HTTPS cho Kho Apps Online.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("URL không được chứa thông tin đăng nhập nhúng sẵn.");
        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        if (host is "169.254.169.254" or "metadata.google.internal" or "metadata.goog" or "0.0.0.0" or "::")
            throw new ArgumentException("Host URL không được phép truy cập.");
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                throw new ArgumentException("Địa chỉ IP URL không được phép truy cập.");
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254)
                throw new ArgumentException("Địa chỉ link-local metadata không được phép truy cập.");
        }
        return uri;
    }

    private static List<StoreAppSummaryDto> ParseRemoteManifest(string rawJson, Uri baseUri, string registryId, string registryName)
    {
        using var doc = JsonDocument.Parse(rawJson);
        JsonElement arrEl;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            arrEl = doc.RootElement;
        else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                 (doc.RootElement.TryGetProperty("apps", out arrEl) || doc.RootElement.TryGetProperty("Apps", out arrEl)))
        { }
        else
            throw new ArgumentException("Manifest từ Registry không đúng chuẩn ({ apps: [...] } hoặc [...]).");

        var list = JsonSerializer.Deserialize<List<StoreAppSummaryDto>>(arrEl.GetRawText(), StoreJsonOpts) ?? [];
        foreach (var item in list)
        {
            item.SourceType = "REMOTE";
            item.RegistryId = registryId;
            item.RegistryName = registryName;
            if (!string.IsNullOrWhiteSpace(item.DownloadUrl) && !Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out _))
            {
                item.DownloadUrl = new Uri(baseUri, item.DownloadUrl).ToString();
            }
            else if (string.IsNullOrWhiteSpace(item.DownloadUrl))
            {
                item.DownloadUrl = new Uri(baseUri, $"/api/store/apps/{Uri.EscapeDataString(item.PackageCode)}/download").ToString();
            }
        }
        return list;
    }

    private async Task<List<StoreAppSummaryDto>> LoadEnabledRegistryItemsAsync(NpgsqlConnection conn, string? registryIdFilter)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, name, cached_manifest::text
            FROM sys_store_registries
            WHERE is_enabled = TRUE
              AND (@reg IS NULL OR id = @reg)
            ORDER BY created_at DESC
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("reg", NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(registryIdFilter) || registryIdFilter.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase)
                ? DBNull.Value
                : registryIdFilter
        });

        var result = new List<StoreAppSummaryDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var regId = r.GetString(0);
            var regName = r.GetString(1);
            var json = r.GetString(2);
            var parsed = JsonSerializer.Deserialize<List<StoreAppSummaryDto>>(json, StoreJsonOpts) ?? [];
            foreach (var item in parsed)
            {
                item.SourceType = "REMOTE";
                item.RegistryId = regId;
                item.RegistryName = regName;
                result.Add(item);
            }
        }
        return result;
    }

    private async Task<Dictionary<string, (int Downloads, int Installs)>> LoadInstallStatsAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT UPPER(package_code),
                   COUNT(*) FILTER (WHERE action_type = 'DOWNLOAD')::int,
                   COUNT(*) FILTER (WHERE action_type LIKE 'INSTALL%')::int
            FROM sys_store_installs
            GROUP BY UPPER(package_code)
            """, conn);
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            map[r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2));
        }
        return map;
    }

    private async Task RecordStoreEventAsync(
        string packageCode,
        string? registryId,
        string? appId,
        string version,
        string? checksum,
        string actionType,
        string actor)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_store_installs (id, package_code, registry_id, app_id, installed_version, package_checksum, action_type, actor)
            VALUES (@id, @code, @reg, @app, @ver, @chk, @act, @actor)
            """, conn);
        cmd.Parameters.AddWithValue("id", $"sti_{Guid.NewGuid().ToString("N")[..10]}");
        cmd.Parameters.AddWithValue("code", packageCode.ToUpperInvariant());
        cmd.Parameters.Add(new NpgsqlParameter("reg", NpgsqlDbType.Varchar) { Value = (object?)registryId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Varchar) { Value = (object?)appId ?? DBNull.Value });
        cmd.Parameters.AddWithValue("ver", version);
        cmd.Parameters.Add(new NpgsqlParameter("chk", NpgsqlDbType.Varchar) { Value = (object?)checksum ?? DBNull.Value });
        cmd.Parameters.AddWithValue("act", actionType);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedPackageSampleRecordsAsync(
        NpgsqlConnection conn,
        string appId,
        Dictionary<string, SysTableDto> createdTables,
        Dictionary<string, List<SysColumnDto>> columnsByTable,
        List<StorePackageRecordDto> sampleRecords,
        string actor)
    {
        var compute = new DynamicComputeService();
        var catSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var neededCats = columnsByTable.Values.SelectMany(cs => cs)
            .Select(c => c.RefCategoryCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var catCode in neededCats)
        {
            var items = await metadata.GetCategoryItemsAsync(catCode);
            catSets[catCode!] = items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var group in sampleRecords.GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (!createdTables.TryGetValue(group.Key, out var tbl) ||
                !columnsByTable.TryGetValue(group.Key, out var cols)) continue;

            var identityCol = cols.FirstOrDefault(c => c.IsIdentityKey);
            var rawBatch = group.Select(r => ($"rec_{Guid.NewGuid().ToString("N")[..12]}", new Dictionary<string, object?>(r.Data))).ToList();
            var computed = compute.EvaluateAndValidate(cols, catSets, rawBatch, tbl.FormRules);

            foreach (var res in computed)
            {
                var recKey = identityCol != null && res.EvaluatedData.TryGetValue(identityCol.ColumnCode, out var kv)
                    ? kv?.ToString()
                    : null;

                var approvalStatus = "DRAFT";
                string? approvedBy = null;
                if (tbl.ApprovalEnabled && !string.IsNullOrWhiteSpace(tbl.ApprovalField) &&
                    res.EvaluatedData.TryGetValue(tbl.ApprovalField, out var appVal))
                {
                    var valStr = appVal?.ToString()?.ToUpperInvariant();
                    if (valStr == "DA_DUYET") { approvalStatus = "APPROVED"; approvedBy = actor; }
                    else if (valStr == "TU_CHOI") { approvalStatus = "REJECTED"; approvedBy = actor; }
                    else if (valStr == "CHO_DUYET") { approvalStatus = "PENDING"; }
                }

                await using var ins = new NpgsqlCommand("""
                    INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status,
                        approval_status, approved_by, approved_at, validation_errors, created_by, updated_by)
                    VALUES (@id, @app, @mod, @tbl, @key, @data::jsonb, 'ACTIVE',
                        @appSt, @appBy, CASE WHEN @appBy IS NOT NULL THEN CURRENT_TIMESTAMP ELSE NULL END,
                        @errs::jsonb, @actor, @actor)
                    """, conn);
                ins.Parameters.AddWithValue("id", res.Id);
                ins.Parameters.AddWithValue("app", appId);
                ins.Parameters.AddWithValue("mod", tbl.ModuleId);
                ins.Parameters.AddWithValue("tbl", tbl.Id);
                ins.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Varchar) { Value = (object?)recKey ?? DBNull.Value });
                ins.Parameters.AddWithValue("data", JsonSerializer.Serialize(res.EvaluatedData));
                ins.Parameters.AddWithValue("appSt", approvalStatus);
                ins.Parameters.Add(new NpgsqlParameter("appBy", NpgsqlDbType.Varchar) { Value = (object?)approvedBy ?? DBNull.Value });
                ins.Parameters.AddWithValue("errs", JsonSerializer.Serialize(res.Errors));
                ins.Parameters.AddWithValue("actor", actor);
                await ins.ExecuteNonQueryAsync();
            }
        }
    }

    private static void EnrichSummaryState(
        StoreAppSummaryDto item,
        Dictionary<string, SysAppDto> installedByCode,
        Dictionary<string, (int Downloads, int Installs)> stats)
    {
        if (installedByCode.TryGetValue(item.PackageCode.ToUpperInvariant(), out var app))
        {
            item.IsInstalled = true;
            item.InstalledAppCode = app.Code;
            item.InstalledAppId = app.Id;
            item.InstalledVersion = app.Version;
            item.HasUpdateAvailable = IsNewerVersion(item.Version, app.Version);
        }
        if (stats.TryGetValue(item.PackageCode.ToUpperInvariant(), out var st))
        {
            item.DownloadCount = st.Downloads;
            item.InstallCount = st.Installs;
        }
    }

    private static bool IsNewerVersion(string? storeVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(storeVersion) || string.IsNullOrWhiteSpace(installedVersion)) return false;
        var sClean = storeVersion.Trim().TrimStart('v', 'V').Split(['-', '+'])[0];
        var iClean = installedVersion.Trim().TrimStart('v', 'V').Split(['-', '+'])[0];
        if (Version.TryParse(sClean, out var sv) && Version.TryParse(iClean, out var iv))
            return sv > iv;
        return false;
    }

    private static StoreAppSummaryDto CloneSummary(StoreAppSummaryDto s) => new()
    {
        PackageCode = s.PackageCode,
        Name = s.Name,
        Description = s.Description,
        Department = s.Department,
        Category = s.Category,
        Version = s.Version,
        Publisher = s.Publisher,
        Icon = s.Icon,
        Color = s.Color,
        BgColor = s.BgColor,
        Tags = [.. s.Tags],
        Highlights = [.. s.Highlights],
        Features = [.. s.Features],
        SourceType = s.SourceType,
        RegistryId = s.RegistryId,
        RegistryName = s.RegistryName,
        DownloadUrl = s.DownloadUrl,
        ChecksumSha256 = s.ChecksumSha256,
        PackageSizeBytes = s.PackageSizeBytes,
        ModuleCount = s.ModuleCount,
        TableCount = s.TableCount,
        SampleRecordCount = s.SampleRecordCount,
        WidgetCount = s.WidgetCount,
        FormRuleCount = s.FormRuleCount,
        WorkflowCount = s.WorkflowCount,
        AutomationCount = s.AutomationCount,
        PrintTemplateCount = s.PrintTemplateCount,
        SavedViewCount = s.SavedViewCount,
        PluginBindingCount = s.PluginBindingCount
    };

    private static StoreAppSummaryDto BuildSummaryFromPackage(StoreAppPackageDto pkg, long bytes) => new()
    {
        PackageCode = pkg.PackageCode,
        Name = pkg.Name,
        Description = pkg.Description,
        Department = pkg.Department,
        Category = pkg.Category,
        Version = pkg.Version,
        Publisher = pkg.Publisher,
        Icon = pkg.Icon,
        Color = pkg.Color,
        BgColor = pkg.BgColor,
        Tags = pkg.Tags,
        Highlights = pkg.Highlights,
        Features = pkg.Features,
        SourceType = "OFFICIAL",
        RegistryName = "SmartApps Official Store",
        ChecksumSha256 = pkg.ChecksumSha256,
        PackageSizeBytes = bytes,
        ModuleCount = pkg.Modules.Count,
        TableCount = pkg.Modules.Sum(m => m.Tables.Count),
        SampleRecordCount = pkg.SampleRecords.Count,
        WidgetCount = pkg.Widgets.Count,
        FormRuleCount = pkg.Modules.SelectMany(m => m.Tables).Sum(t => t.FormRules.Count),
        WorkflowCount = pkg.Workflows.Count,
        AutomationCount = pkg.Automations.Count,
        PrintTemplateCount = pkg.PrintTemplates.Count,
        SavedViewCount = pkg.SavedViews.Count,
        PluginBindingCount = pkg.PluginBindings.Count
    };

    private static IReadOnlyList<StorePackageEntry> BuildBuiltInPackages()
    {
        var defs = new List<AppTemplateDefinition>(AppTemplateCatalog.GetAll());
        defs.AddRange(BuildOnlineExclusiveApps());

        return defs.Select(def =>
        {
            var pkg = ToPackageDto(def);
            pkg.ChecksumSha256 = ComputeCanonicalChecksum(pkg);
            var json = JsonSerializer.Serialize(pkg, StoreJsonOpts);
            var summary = BuildSummaryFromPackage(pkg, Encoding.UTF8.GetByteCount(json));
            return new StorePackageEntry(pkg, summary, json);
        }).ToList();
    }

    private static StoreAppPackageDto ToPackageDto(AppTemplateDefinition def) => new()
    {
        PackageFormat = "smartapps-package/2.0",
        ExportedAt = DateTime.UnixEpoch,
        PackageCode = def.Code,
        Name = def.Name,
        Description = def.Description,
        Department = def.Department,
        Category = def.Category,
        Version = def.Version,
        Publisher = "SmartApps Official Hub",
        Icon = def.Icon,
        Color = def.Color,
        BgColor = def.BgColor,
        Tags = [def.Code.ToLowerInvariant(), def.Category.ToLowerInvariant(), "v2.0"],
        Highlights = def.Highlights,
        Features = def.Features,
        Categories = def.Categories.Select(c => new StorePackageCategoryDto
        {
            CategoryCode = c.CategoryCode,
            CategoryName = c.CategoryName,
            Description = c.Description,
            Items = c.Items.Select(i => new StorePackageCategoryItemDto
            {
                ItemCode = i.ItemCode,
                ItemName = i.ItemName,
                Color = i.Color,
                OrderIndex = i.OrderIndex
            }).ToList()
        }).ToList(),
        Modules = def.Modules.Select(m => new StorePackageModuleDto
        {
            Code = m.Code,
            Name = m.Name,
            Icon = m.Icon,
            Description = m.Description,
            Tables = m.Tables.Select(t => new StorePackageTableDto
            {
                TableName = t.TableName,
                DisplayName = t.DisplayName,
                Description = t.Description,
                PrimaryView = t.PrimaryView,
                RowScope = t.RowScope,
                ApprovalEnabled = t.ApprovalEnabled,
                ApprovalField = t.ApprovalField,
                ApproverRoles = t.ApproverRoles.Count > 0 ? t.ApproverRoles : ["ADMIN", "HR_MANAGER", "ASSET_LEAD"],
                PublicFormEnabled = t.PublicFormEnabled,
                FormRules = t.FormRules,
                Columns = t.Columns.Select(c => new StorePackageColumnDto
                {
                    ColumnCode = c.ColumnCode,
                    ColumnName = c.ColumnName,
                    DataType = c.DataType,
                    IsRequired = c.IsRequired,
                    IsIdentityKey = c.IsIdentityKey,
                    ShowInTable = c.ShowInTable,
                    ShowInForm = c.ShowInForm,
                    ShowInParentForm = c.ShowInParentForm,
                    Width = c.Width,
                    ValidationRegex = c.ValidationRegex,
                    RefCategoryCode = c.RefCategoryCode,
                    AllowQuickCreate = c.AllowQuickCreate,
                    RefAppId = c.RefAppId,
                    RefTableId = c.RefTableId,
                    InternalRefTableName = c.InternalRefTableName,
                    RefValueField = c.RefValueField,
                    RefDisplayField = c.RefDisplayField,
                    LookupFilterField = c.LookupFilterField,
                    LookupSourceField = c.LookupSourceField,
                    Formula = c.Formula,
                    DefaultValue = c.DefaultValue,
                    HiddenForRoles = c.HiddenForRoles ?? [],
                    ReadOnlyForRoles = c.ReadOnlyForRoles ?? [],
                    OrderIndex = c.OrderIndex
                }).ToList()
            }).ToList()
        }).ToList(),
        Widgets = def.Widgets.Select(w => new StorePackageWidgetDto
        {
            TableName = w.TableName,
            Title = w.Title,
            WidgetType = w.WidgetType,
            MetricField = w.MetricField,
            GroupField = w.GroupField,
            FilterField = w.FilterField,
            FilterValue = w.FilterValue,
            ColSpan = w.ColSpan,
            Color = w.Color,
            OrderIndex = w.OrderIndex
        }).ToList(),
        Workflows = def.Workflows.Select(w => new StorePackageWorkflowDto
        {
            Name = w.Name,
            Description = w.Description,
            TableName = w.TableName,
            TriggerType = w.TriggerType,
            Steps = w.Steps
        }).ToList(),
        Automations = def.Automations.Select(a => new StorePackageAutomationDto
        {
            Name = a.Name,
            Description = a.Description,
            TriggerTableName = a.TriggerTableName,
            TriggerEvent = a.TriggerEvent,
            ConditionField = a.ConditionField,
            ConditionOperator = a.ConditionOperator ?? "EQUALS",
            ConditionValue = a.ConditionValue,
            TargetTableName = a.TargetTableName,
            ActionType = a.ActionType,
            MatchSourceField = a.MatchSourceField,
            MatchTargetField = a.MatchTargetField,
            FieldMappings = a.FieldMappings,
            NotifyRecipient = a.NotifyRecipient,
            NotifyTemplate = a.NotifyTemplate
        }).ToList(),
        PrintTemplates = def.PrintTemplates.Select(p => new StorePackagePrintTemplateDto
        {
            TableName = p.TableName,
            Name = p.Name,
            PaperSize = p.PaperSize,
            Orientation = p.Orientation,
            IsDefault = p.IsDefault,
            WatermarkDraft = p.WatermarkDraft,
            RequireApproved = p.RequireApproved,
            HtmlTemplate = p.HtmlTemplate,
            CssStyles = p.CssStyles
        }).ToList(),
        SavedViews = def.SavedViews.Select(v => new StorePackageSavedViewDto
        {
            TableName = v.TableName,
            Name = v.Name,
            IsDefault = v.IsDefault,
            IsShared = v.IsShared,
            FiltersJson = v.FiltersJson,
            SortField = v.SortField,
            SortDir = v.SortDir
        }).ToList(),
        PluginBindings = def.PluginBindings.Select(b => new StorePackagePluginBindingDto
        {
            PluginCode = b.PluginCode,
            TableName = b.TableName,
            FieldMapping = b.FieldMapping
        }).ToList(),
        SampleRecords = def.SampleRecords.Select(r => new StorePackageRecordDto
        {
            TableName = r.TableName,
            Data = r.Data
        }).ToList()
    };

    private static List<AppTemplateDefinition> BuildOnlineExclusiveApps() =>
    [
        new(
            TemplateKey: "ECOMMERCE_VN",
            Code: "ECOMMERCE_VN",
            Name: "Quản lý Đơn hàng TMĐT & Sàn O2O",
            Description: "Giải pháp Thương mại Điện tử đa kênh: Đồng bộ đơn Shopee/Lazada/TikTok, bảng con SKU, theo dõi vận đơn & thanh toán VietQR.",
            Department: "Thương mại Điện tử",
            Category: "Kho Apps Online Đặc thù",
            Icon: "Storefront",
            Color: "#ea580c",
            BgColor: "#ffedd5",
            OrderIndex: 201,
            Highlights:
            [
                "Quản lý đơn hàng đa kênh Shopee / TikTok Shop / Website tập trung",
                "Bảng con sản phẩm tự tính tổng tiền hàng + phí ship, in bill K80 & A4",
                "Form động tự hiện mã vận đơn khi giao hàng và lý do khi hoàn trả"
            ],
            Features: ["Kho Online", "VietQR", "In K80/A4", "TMĐT O2O"],
            Categories:
            [
                new("DM_KENH_ECOM", "Kênh bán TMĐT", "Sàn và kênh bán hàng trực tuyến",
                [
                    new("SHOPEE", "Shopee Mall", "#ea580c", 1),
                    new("TIKTOK_SHOP", "TikTok Shop", "#111827", 2),
                    new("LAZADA", "Lazada VN", "#1d4ed8", 3),
                    new("WEB_STORE", "Website / ZNS", "#0f766e", 4)
                ]),
                new("DM_TT_ECOM", "Trạng thái Đơn TMĐT", "Quy trình xử lý đơn hàng TMĐT",
                [
                    new("MOI", "Chờ xác nhận", "#64748b", 1),
                    new("DONG_GOI", "Đang đóng gói", "#f59e0b", 2),
                    new("DANG_GIAO", "Đang vận chuyển", "#1a73e8", 3),
                    new("HOAN_TAT", "Giao thành công", "#10b981", 4),
                    new("HOAN_TRA", "Hoàn trả / Khiếu nại", "#ef4444", 5)
                ])
            ],
            Modules:
            [
                new("DON_TMĐT", "Đơn hàng Đa kênh", "ShoppingBag", "Tiếp nhận, đóng gói và đối soát đơn TMĐT",
                [
                    new("ec_don_hang", "Đơn hàng TMĐT", "Quản lý đơn hàng sàn TMĐT, bảng con sản phẩm và vận chuyển", "table", false, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_don", ColumnName = "Mã Đơn hàng", DataType = "AUTO_NUMBER", DefaultValue = "EC-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "kenh_ban", ColumnName = "Kênh bán", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_KENH_ECOM", Width = 155, OrderIndex = 2 },
                        new() { ColumnCode = "khach_hang", ColumnName = "Khách hàng", DataType = "STRING", IsRequired = true, Width = 190, OrderIndex = 3 },
                        new() { ColumnCode = "sdt", ColumnName = "Điện thoại", DataType = "TEL", Width = 135, OrderIndex = 4 },
                        new()
                        {
                            ColumnCode = "chi_tiet_sp", ColumnName = "Sản phẩm đặt mua", DataType = "SUB_TABLE",
                            DefaultValue = AppTemplateCatalog.SubTableJson(
                                new("ten_sp", "Tên sản phẩm", "STRING", true, 230),
                                new("so_luong", "SL", "NUMBER", true, 85, "1"),
                                new("don_gia", "Đơn giá", "CURRENCY", true, 145),
                                new("thanh_tien", "Thành tiền", "CURRENCY", false, 160, null, "so_luong * don_gia")
                            ),
                            ShowInTable = false, Width = 260, OrderIndex = 5
                        },
                        new() { ColumnCode = "phi_ship", ColumnName = "Phí vận chuyển", DataType = "CURRENCY", DefaultValue = "0", Width = 135, OrderIndex = 6 },
                        new() { ColumnCode = "tong_tien", ColumnName = "Tổng thu hộ (Auto)", DataType = "CURRENCY", Formula = "SUM(chi_tiet_sp.thanh_tien) + phi_ship", Width = 175, OrderIndex = 7 },
                        new() { ColumnCode = "trang_thai_don", ColumnName = "Trạng thái đơn", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TT_ECOM", DefaultValue = "MOI", Width = 165, OrderIndex = 8 },
                        new() { ColumnCode = "ma_van_don", ColumnName = "Mã vận đơn GHN/GHTK", DataType = "STRING", ShowInTable = false, Width = 165, OrderIndex = 9 },
                        new() { ColumnCode = "ly_do_hoan", ColumnName = "Lý do hoàn trả", DataType = "TEXTAREA", ShowInTable = false, Width = 210, OrderIndex = 10 },
                        new() { ColumnCode = "payment_status", ColumnName = "Đối soát TT", DataType = "STRING", DefaultValue = "UNPAID", Width = 125, OrderIndex = 11 }
                    ])
                    {
                        FormRules =
                        [
                            AppTemplateCatalog.Rule("fr_ec_tracking", "ma_van_don", "visible", "trang_thai_don", "NOT_EQUALS", "MOI", true, false),
                            AppTemplateCatalog.Rule("fr_ec_return", "ly_do_hoan", "visible", "trang_thai_don", "EQUALS", "HOAN_TRA", true, false)
                        ]
                    }
                ])
            ],
            SampleRecords:
            [
                new("ec_don_hang", new()
                {
                    ["ma_don"] = "EC-2026-0001",
                    ["kenh_ban"] = "SHOPEE",
                    ["khach_hang"] = "Nguyễn Hoàng Nam",
                    ["sdt"] = "0908112233",
                    ["chi_tiet_sp"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["ten_sp"] = "Bàn phím cơ Wireless Pro", ["so_luong"] = 1, ["don_gia"] = 1850000m, ["thanh_tien"] = 1850000m }
                    },
                    ["phi_ship"] = 30000m,
                    ["trang_thai_don"] = "DANG_GIAO",
                    ["ma_van_don"] = "GHN-VN99281",
                    ["payment_status"] = "PAID"
                })
            ],
            Widgets:
            [
                new("ec_don_hang", "Tổng Doanh thu TMĐT", "KPI_SUM", "tong_tien", null, null, null, 1, "#ea580c", 1),
                new("ec_don_hang", "Đơn hàng Theo Kênh bán", "DONUT_CHART", null, "kenh_ban", null, null, 2, "#1a73e8", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("ec_don_hang", "Xử lý Khiếu nại & Hoàn tiền TMĐT", "Xác nhận đổi trả hàng trước khi hoàn tiền qua sàn")],
            Automations = [new("Báo đơn TMĐT mới về kho", "Gửi cảnh báo đóng gói khi phát sinh đơn TMĐT mới", "ec_don_hang", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Đơn sàn mới {{ma_don}} ({{kenh_ban}}) - {{khach_hang}} tổng {{tong_tien}} đ.")],
            PrintTemplates =
            [
                new("ec_don_hang", "Phiếu Đóng gói & Giao hàng TMĐT (A4)", "A4", "portrait", true, false, false,
                    AppTemplateCatalog.PrintA4("PHIẾU ĐÓNG GÓI ĐƠN THƯƠNG MẠI ĐIỆN TỬ", "SmartApps E-Commerce 2.0", "ma_don",
                    [("Kênh bán", "kenh_ban"), ("Khách hàng", "khach_hang"), ("SĐT", "sdt"), ("Mã vận đơn", "ma_van_don"), ("Trạng thái", "trang_thai_don")],
                    "chi_tiet_sp", [("Sản phẩm", "ten_sp", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                    "Tổng thu hộ COD", "tong_tien"), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("ec_don_hang", "Đơn Đang vận chuyển", false, true, AppTemplateCatalog.SavedViewAnd("trang_thai_don", "eq", "DANG_GIAO")),
                new("ec_don_hang", "Đơn Đã thanh toán", false, true, AppTemplateCatalog.SavedViewAnd("payment_status", "eq", "PAID"))
            ],
            PluginBindings = [new("vietqr", "ec_don_hang", new() { ["amount"] = "tong_tien", ["orderCode"] = "ma_don", ["paymentStatus"] = "payment_status" })]
        },

        new(
            TemplateKey: "REAL_ESTATE",
            Code: "REAL_ESTATE",
            Name: "Quản lý Bất động sản & Hợp đồng Thuê",
            Description: "Quản lý quỹ căn hộ/văn phòng cho thuê theo Kanban, hợp đồng thuê, bảng kê phí dịch vụ SUB_TABLE và phiếu thu VietQR.",
            Department: "Quản lý Tài sản BĐS",
            Category: "Kho Apps Online Đặc thù",
            Icon: "Apartment",
            Color: "#0284c7",
            BgColor: "#e0f2fe",
            OrderIndex: 202,
            Highlights:
            [
                "Theo dõi trạng thái Trống / Đặt cọc / Đang thuê trên Kanban",
                "Hợp đồng thuê liên kết trực tiếp mã căn hộ + Quick-Create",
                "In phụ lục hợp đồng A4 & đối soát thu tiền hàng tháng"
            ],
            Features: ["Kho Online", "Kanban", "Phê duyệt", "In A4"],
            Categories:
            [
                new("DM_TT_BDS", "Trạng thái BĐS", "Tình trạng khai thác bất động sản",
                [
                    new("TRONG", "Sẵn sàng cho thuê", "#10b981", 1),
                    new("DAT_COC", "Đã đặt cọc giữ chỗ", "#f59e0b", 2),
                    new("DANG_THUE", "Đang có khách thuê", "#1a73e8", 3),
                    new("BAO_TRI", "Đang sửa chữa", "#ef4444", 4)
                ])
            ],
            Modules:
            [
                new("QUY_CAN", "Quỹ Căn & Hợp đồng Thuê", "HomeWork", "Quản lý bất động sản và hợp đồng thuê",
                [
                    new("re_bat_dong_san", "Quỹ Căn hộ / Mặt bằng", "Danh mục bất động sản và trạng thái khai thác", "kanban", false, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_can", ColumnName = "Mã Căn / Mặt bằng", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "ten_du_an", ColumnName = "Dự án / Tòa nhà", DataType = "STRING", IsRequired = true, Width = 220, OrderIndex = 2 },
                        new() { ColumnCode = "dien_tich", ColumnName = "Diện tích (m2)", DataType = "NUMBER", IsRequired = true, DefaultValue = "50", Width = 125, OrderIndex = 3 },
                        new() { ColumnCode = "gia_thue", ColumnName = "Giá thuê / tháng", DataType = "CURRENCY", IsRequired = true, Width = 165, OrderIndex = 4 },
                        new() { ColumnCode = "trang_thai_thue", ColumnName = "Trạng thái", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TT_BDS", DefaultValue = "TRONG", Width = 165, OrderIndex = 5 },
                        new() { ColumnCode = "ghi_chu_dat_coc", ColumnName = "Thông tin giữ chỗ / Đặt cọc", DataType = "TEXTAREA", ShowInTable = false, Width = 210, OrderIndex = 6 }
                    ])
                    {
                        FormRules = [AppTemplateCatalog.Rule("fr_re_deposit", "ghi_chu_dat_coc", "visible", "trang_thai_thue", "EQUALS", "DAT_COC", true, false)]
                    },
                    new("re_hop_dong_thue", "Hợp đồng Thuê & Phí dịch vụ", "Lập hợp đồng thuê căn kèm bảng kê dịch vụ hàng tháng", "table", true, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_hd", ColumnName = "Số Hợp đồng", DataType = "AUTO_NUMBER", DefaultValue = "HD-BDS-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 145, OrderIndex = 1 },
                        new() { ColumnCode = "ma_can", ColumnName = "Căn hộ / Mặt bằng", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "re_bat_dong_san", RefValueField = "ma_can", RefDisplayField = "ten_du_an", AllowQuickCreate = true, Width = 210, OrderIndex = 2 },
                        new() { ColumnCode = "khach_thue", ColumnName = "Khách thuê", DataType = "STRING", IsRequired = true, Width = 195, OrderIndex = 3 },
                        new() { ColumnCode = "sdt_khach", ColumnName = "SĐT Khách", DataType = "TEL", Width = 135, OrderIndex = 4 },
                        new()
                        {
                            ColumnCode = "chi_phi_dich_vu", ColumnName = "Chi tiết tiền thuê & Dịch vụ", DataType = "SUB_TABLE",
                            DefaultValue = AppTemplateCatalog.SubTableJson(
                                new("khoan_muc", "Khoản mục thuê / Phí", "STRING", true, 220),
                                new("so_luong", "SL", "NUMBER", true, 85, "1"),
                                new("don_gia", "Đơn giá", "CURRENCY", true, 145),
                                new("thanh_tien", "Thành tiền", "CURRENCY", false, 160, null, "so_luong * don_gia")
                            ),
                            ShowInTable = false, Width = 260, OrderIndex = 5
                        },
                        new() { ColumnCode = "tong_thu_thang", ColumnName = "Tổng tiền tháng (Auto)", DataType = "CURRENCY", Formula = "SUM(chi_phi_dich_vu.thanh_tien)", Width = 175, OrderIndex = 6 },
                        new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 7 }
                    ])
                ])
            ],
            SampleRecords:
            [
                new("re_bat_dong_san", new() { ["ma_can"] = "VP-T12-08", ["ten_du_an"] = "Tòa nhà SmartTower Q1 (120m2)", ["dien_tich"] = 120, ["gia_thue"] = 45000000m, ["trang_thai_thue"] = "DANG_THUE" }),
                new("re_hop_dong_thue", new()
                {
                    ["ma_hd"] = "HD-BDS-2026-0001",
                    ["ma_can"] = "VP-T12-08",
                    ["khach_thue"] = "Công ty Phần mềm Á Châu",
                    ["sdt_khach"] = "0903888999",
                    ["chi_phi_dich_vu"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["khoan_muc"] = "Tiền thuê văn phòng tháng 11", ["so_luong"] = 1, ["don_gia"] = 45000000m, ["thanh_tien"] = 45000000m },
                        new() { ["khoan_muc"] = "Phí quản lý & Bãi xe", ["so_luong"] = 1, ["don_gia"] = 3500000m, ["thanh_tien"] = 3500000m }
                    },
                    ["trang_thai_duyet"] = "DA_DUYET"
                })
            ],
            Widgets:
            [
                new("re_bat_dong_san", "Tổng Quỹ Căn hộ", "KPI_COUNT", null, null, null, null, 1, "#0284c7", 1),
                new("re_hop_dong_thue", "Tổng Doanh thu Thuê/tháng", "KPI_SUM", "tong_thu_thang", null, null, null, 1, "#10b981", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("re_hop_dong_thue", "Phê duyệt Hợp đồng Thuê BĐS", "Pháp chế kiểm tra điều khoản cọc trước khi Giám đốc ký")],
            Automations = [new("Thông báo Hợp đồng Thuê mới", "Gửi thông báo ngay khi lập hợp đồng thuê bất động sản mới", "re_hop_dong_thue", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Hợp đồng thuê {{ma_hd}} căn {{ma_can}} cho {{khach_thue}} trị giá {{tong_thu_thang}} đ/tháng.")],
            PrintTemplates =
            [
                new("re_hop_dong_thue", "Phiếu Thanh toán Hợp đồng Thuê BĐS (A4)", "A4", "portrait", true, true, false,
                    AppTemplateCatalog.PrintA4("BIÊN BẢN HỢP ĐỒNG & THU PHÍ BẤT ĐỘNG SẢN", "SmartApps Real Estate 2.0", "ma_hd",
                    [("Mã Căn / Mặt bằng", "ma_can"), ("Khách thuê", "khach_thue"), ("SĐT Khách", "sdt_khach"), ("Trạng thái duyệt", "trang_thai_duyet")],
                    "chi_phi_dich_vu", [("Khoản mục", "khoan_muc", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                    "Tổng thu hàng tháng", "tong_thu_thang"), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("re_bat_dong_san", "Căn Sẵn sàng cho thuê", false, true, AppTemplateCatalog.SavedViewAnd("trang_thai_thue", "eq", "TRONG")),
                AppTemplateCatalog.ApprovalViewPending("re_hop_dong_thue", "HĐ Thuê Chờ duyệt")
            ],
            PluginBindings = [new("vietqr", "re_hop_dong_thue", new() { ["amount"] = "tong_thu_thang", ["orderCode"] = "ma_hd" })]
        },

        new(
            TemplateKey: "CLINIC_HIS",
            Code: "CLINIC_HIS",
            Name: "Phòng khám & Hồ sơ Bệnh án (HIS Mini)",
            Description: "Tiếp nhận bệnh nhân qua Public Form, quản lý lịch sử khám, kê đơn thuốc SUB_TABLE tính viện phí tự động và in đơn thuốc A4.",
            Department: "Y tế & Phòng khám",
            Category: "Kho Apps Online Đặc thù",
            Icon: "LocalHospital",
            Color: "#0d9488",
            BgColor: "#ccfbf1",
            OrderIndex: 203,
            Highlights:
            [
                "Public Form đăng ký khám bệnh trực tuyến từ QR Code phòng chờ",
                "Đơn thuốc SUB_TABLE tự cộng tiền thuốc + công khám bác sĩ",
                "In đơn thuốc & hóa đơn viện phí A4 tức thì"
            ],
            Features: ["Kho Online", "Public Form", "SUB_TABLE", "In A4"],
            Categories: [],
            Modules:
            [
                new("KHAM_BENH", "Tiếp nhận & Kê đơn Khám", "MedicalServices", "Hồ sơ bệnh nhân và phiếu khám",
                [
                    new("his_benh_nhan", "Danh sách Bệnh nhân", "Tiếp nhận bệnh nhân mới tại quầy hoặc Public Web Form", "table", false, "trang_thai_duyet", true,
                    [
                        new() { ColumnCode = "ma_bn", ColumnName = "Mã Bệnh nhân", DataType = "AUTO_NUMBER", DefaultValue = "BN-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "ho_ten", ColumnName = "Họ và Tên", DataType = "STRING", IsRequired = true, Width = 200, OrderIndex = 2 },
                        new() { ColumnCode = "sdt", ColumnName = "Số điện thoại", DataType = "TEL", IsRequired = true, Width = 135, OrderIndex = 3 },
                        new() { ColumnCode = "ngay_sinh", ColumnName = "Ngày sinh", DataType = "DATE", Width = 135, OrderIndex = 4 },
                        new() { ColumnCode = "tien_su", ColumnName = "Tiền sử dị ứng / Lưu ý", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 5 }
                    ]),
                    new("his_phieu_kham", "Phiếu Khám & Đơn thuốc", "Chẩn đoán lâm sàng, kê đơn thuốc SUB_TABLE và viện phí", "table", false, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_pk", ColumnName = "Mã Phiếu khám", DataType = "AUTO_NUMBER", DefaultValue = "PKB-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "ma_bn", ColumnName = "Bệnh nhân", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "his_benh_nhan", RefValueField = "ma_bn", RefDisplayField = "ho_ten", AllowQuickCreate = true, Width = 200, OrderIndex = 2 },
                        new() { ColumnCode = "chan_doan", ColumnName = "Chẩn đoán bác sĩ", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 3 },
                        new()
                        {
                            ColumnCode = "don_thuoc", ColumnName = "Đơn thuốc / Chỉ định", DataType = "SUB_TABLE",
                            DefaultValue = AppTemplateCatalog.SubTableJson(
                                new("ten_thuoc", "Tên thuốc / Liều dùng", "STRING", true, 240),
                                new("so_luong", "SL", "NUMBER", true, 85, "1"),
                                new("don_gia", "Đơn giá", "CURRENCY", true, 140),
                                new("thanh_tien", "Thành tiền", "CURRENCY", false, 155, null, "so_luong * don_gia")
                            ),
                            ShowInTable = false, Width = 260, OrderIndex = 4
                        },
                        new() { ColumnCode = "tien_kham", ColumnName = "Phí khám bác sĩ", DataType = "CURRENCY", DefaultValue = "200000", Width = 145, OrderIndex = 5 },
                        new() { ColumnCode = "tong_vien_phi", ColumnName = "Tổng viện phí (Auto)", DataType = "CURRENCY", Formula = "SUM(don_thuoc.thanh_tien) + tien_kham", Width = 170, OrderIndex = 6 },
                        new() { ColumnCode = "tai_kham", ColumnName = "Cần hẹn tái khám", DataType = "BOOLEAN", DefaultValue = "false", Width = 135, OrderIndex = 7 },
                        new() { ColumnCode = "ngay_hen_tai_kham", ColumnName = "Ngày hẹn tái khám", DataType = "DATE", ShowInTable = false, Width = 140, OrderIndex = 8 }
                    ])
                    {
                        FormRules = [AppTemplateCatalog.Rule("fr_his_followup", "ngay_hen_tai_kham", "visible", "tai_kham", "EQUALS", "true", true, false)]
                    }
                ])
            ],
            SampleRecords:
            [
                new("his_benh_nhan", new() { ["ma_bn"] = "BN-2026-0001", ["ho_ten"] = "Lê Minh Châu", ["sdt"] = "0912345678", ["ngay_sinh"] = "1992-05-14" }),
                new("his_phieu_kham", new()
                {
                    ["ma_pk"] = "PKB-2026-0001",
                    ["ma_bn"] = "BN-2026-0001",
                    ["chan_doan"] = "Viêm họng cấp tính - Theo dõi ngoại trú",
                    ["don_thuoc"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["ten_thuoc"] = "Amoxicillin 500mg (2v/ngày x 5 ngày)", ["so_luong"] = 10, ["don_gia"] = 5000m, ["thanh_tien"] = 50000m }
                    },
                    ["tien_kham"] = 200000m,
                    ["tai_kham"] = true,
                    ["ngay_hen_tai_kham"] = "2026-11-25"
                })
            ],
            Widgets:
            [
                new("his_benh_nhan", "Tổng Hồ sơ Bệnh nhân", "KPI_COUNT", null, null, null, null, 1, "#0d9488", 1),
                new("his_phieu_kham", "Tổng Doanh thu Viện phí", "KPI_SUM", "tong_vien_phi", null, null, null, 1, "#1a73e8", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("his_phieu_kham", "Kiểm duyệt Đơn thuốc BHYT / Miễn giảm", "Trưởng khoa chuyên môn & Quản lý duyệt miễn giảm viện phí")],
            Automations = [new("Nhắc lịch Tái khám Bệnh nhân", "Tạo thông báo nhắc CSKH khi phiếu khám có hẹn tái khám", "his_phieu_kham", "RECORD_CREATED", "tai_kham", "EQUALS", "true", null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Bệnh nhân {{ma_bn}} ({{ma_pk}}) có lịch hẹn tái khám ngày {{ngay_hen_tai_kham}}.")],
            PrintTemplates =
            [
                new("his_phieu_kham", "Đơn thuốc & Phiếu Thanh toán Viện phí (A4)", "A4", "portrait", true, false, false,
                    AppTemplateCatalog.PrintA4("ĐƠN THUỐC & PHIẾU VIỆN PHÍ PHÒNG KHÁM", "SmartApps Clinic 2.0", "ma_pk",
                    [("Mã Bệnh nhân", "ma_bn"), ("Chẩn đoán lâm sàng", "chan_doan"), ("Phí khám bác sĩ", "tien_kham"), ("Ngày hẹn tái khám", "ngay_hen_tai_kham")],
                    "don_thuoc", [("Tên thuốc & Liều dùng", "ten_thuoc", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                    "Tổng chi phí khám & thuốc", "tong_vien_phi", ("Bệnh nhân", "Dược sĩ phát thuốc", "Bác sĩ khám bệnh")), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("his_phieu_kham", "Phiếu Có hẹn Tái khám", false, true, AppTemplateCatalog.SavedViewAnd("tai_kham", "eq", "true"), "ngay_hen_tai_kham", "ASC")
            ],
            PluginBindings = [new("vietqr", "his_phieu_kham", new() { ["amount"] = "tong_vien_phi", ["orderCode"] = "ma_pk" })]
        },

        new(
            TemplateKey: "EVENT_CHECKIN",
            Code: "EVENT_CHECKIN",
            Name: "Sự kiện Hội thảo & Check-in Khách mời",
            Description: "Mở form đăng ký sự kiện công khai, quản lý danh sách khách VIP/Standard, check-in tại sảnh và in thẻ mời A4.",
            Department: "Marketing & Sự kiện",
            Category: "Kho Apps Online Đặc thù",
            Icon: "Celebration",
            Color: "#9333ea",
            BgColor: "#f3e8ff",
            OrderIndex: 204,
            Highlights:
            [
                "Public Form đăng ký tham dự hội thảo không cần đăng nhập",
                "Form rule tự hiện trường Quà tặng / Tài liệu khi khách Check-in",
                "In thẻ đại biểu & giấy chứng nhận tham dự A4"
            ],
            Features: ["Kho Online", "Public Form", "Check-in", "Zalo ZNS"],
            Categories:
            [
                new("DM_HANG_VE_EVT", "Hạng vé Khách mời", "Phân loại khách tham dự sự kiện",
                [
                    new("VIP", "Khách mời VIP", "#9333ea", 1),
                    new("SPEAKER", "Diễn giả / Báo cáo viên", "#1a73e8", 2),
                    new("STANDARD", "Khách tham dự Standard", "#10b981", 3)
                ])
            ],
            Modules:
            [
                new("SU_KIEN", "Sự kiện & Khách mời", "EventAvailable", "Tổ chức sự kiện và check-in đại biểu",
                [
                    new("evt_su_kien", "Danh sách Sự kiện", "Kế hoạch tổ chức sự kiện và địa điểm", "calendar", false, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_sk", ColumnName = "Mã Sự kiện", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "ten_su_kien", ColumnName = "Tên Sự kiện / Hội thảo", DataType = "STRING", IsRequired = true, Width = 240, OrderIndex = 2 },
                        new() { ColumnCode = "ngay_to_chuc", ColumnName = "Ngày tổ chức", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 3 },
                        new() { ColumnCode = "dia_diem", ColumnName = "Địa điểm", DataType = "STRING", Width = 210, OrderIndex = 4 }
                    ]),
                    new("evt_khach_moi", "Khách mời & Check-in", "Đăng ký Public Form và xác nhận tham dự tại quầy", "table", false, "trang_thai_duyet", true,
                    [
                        new() { ColumnCode = "ma_ve", ColumnName = "Mã Vé / QR", DataType = "AUTO_NUMBER", DefaultValue = "EVT-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "su_kien", ColumnName = "Sự kiện", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "evt_su_kien", RefValueField = "ma_sk", RefDisplayField = "ten_su_kien", AllowQuickCreate = true, Width = 220, OrderIndex = 2 },
                        new() { ColumnCode = "ho_ten", ColumnName = "Họ tên Đại biểu", DataType = "STRING", IsRequired = true, Width = 195, OrderIndex = 3 },
                        new() { ColumnCode = "email", ColumnName = "Email", DataType = "EMAIL", Width = 190, OrderIndex = 4 },
                        new() { ColumnCode = "hang_ve", ColumnName = "Hạng vé", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_HANG_VE_EVT", DefaultValue = "STANDARD", Width = 165, OrderIndex = 5 },
                        new() { ColumnCode = "da_checkin", ColumnName = "Đã Check-in", DataType = "BOOLEAN", DefaultValue = "false", Width = 125, OrderIndex = 6 },
                        new() { ColumnCode = "qua_tang", ColumnName = "Gói quà / Tài liệu đã phát", DataType = "STRING", ShowInTable = false, Width = 185, OrderIndex = 7 }
                    ])
                    {
                        FormRules = [AppTemplateCatalog.Rule("fr_evt_gift", "qua_tang", "visible", "da_checkin", "EQUALS", "true", true, false)]
                    }
                ])
            ],
            SampleRecords:
            [
                new("evt_su_kien", new() { ["ma_sk"] = "EVT-SUMMIT-26", ["ten_su_kien"] = "Enterprise AI & Low-Code Summit 2026", ["ngay_to_chuc"] = "2026-12-12", ["dia_diem"] = "Trung tâm Hội nghị Gem Center TP.HCM" }),
                new("evt_khach_moi", new() { ["ma_ve"] = "EVT-2026-0001", ["su_kien"] = "EVT-SUMMIT-26", ["ho_ten"] = "Trần Quốc Tuấn", ["email"] = "tuan.tq@partner.vn", ["hang_ve"] = "VIP", ["da_checkin"] = true, ["qua_tang"] = "Bộ VIP Giftset & Sách AI" })
            ],
            Widgets:
            [
                new("evt_khach_moi", "Tổng Khách đăng ký", "KPI_COUNT", null, null, null, null, 1, "#9333ea", 1),
                new("evt_khach_moi", "Khách mời theo Hạng vé", "DONUT_CHART", null, "hang_ve", null, null, 2, "#1a73e8", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("evt_su_kien", "Phê duyệt Tổ chức & Ngân sách Sự kiện", "Marketing Lead & Ban Giám đốc duyệt kế hoạch sự kiện")],
            Automations = [new("Gửi vé mời khi Đăng ký Sự kiện", "Thông báo BTC ngay khi đại biểu đăng ký tham dự qua Web Form", "evt_khach_moi", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Khách {{ho_ten}} ({{hang_ve}}) đã đăng ký vé {{ma_ve}} cho sự kiện {{su_kien}}.")],
            PrintTemplates =
            [
                new("evt_khach_moi", "Thẻ Đại biểu & Giấy Mời Sự kiện (A4)", "A4", "portrait", true, false, false,
                    AppTemplateCatalog.PrintA4("THẺ ĐẠI BIỂU THAM DỰ SỰ KIỆN", "SmartApps Events 2.0", "ma_ve",
                    [("Sự kiện", "su_kien"), ("Họ tên Đại biểu", "ho_ten"), ("Email", "email"), ("Hạng vé", "hang_ve"), ("Trạng thái Check-in", "da_checkin"), ("Quà tặng đã phát", "qua_tang")],
                    signs: ("Đại biểu", "Lễ tân Check-in", "Trưởng Ban Tổ chức")), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("evt_khach_moi", "Khách VIP Chưa Check-in", false, true, AppTemplateCatalog.SavedViewAnd("da_checkin", "eq", "false")),
                new("evt_khach_moi", "Đã Check-in tại sảnh", false, true, AppTemplateCatalog.SavedViewAnd("da_checkin", "eq", "true"))
            ],
            PluginBindings = [new("zalo-zns", "evt_khach_moi", new() { ["ticketCode"] = "ma_ve", ["guestName"] = "ho_ten", ["tier"] = "hang_ve" })]
        },

        new(
            TemplateKey: "MANUFACTURE_MES",
            Code: "MANUFACTURE_MES",
            Name: "Quản lý Lệnh Sản xuất (MES) & QC",
            Description: "Lập lệnh sản xuất có bảng định mức vật tư BOM SUB_TABLE, theo dõi chuyền sản xuất Kanban, kiểm tra KCS/QC và phê duyệt xuất xưởng.",
            Department: "Sản xuất & KCS",
            Category: "Kho Apps Online Đặc thù",
            Icon: "PrecisionManufacturing",
            Color: "#b45309",
            BgColor: "#fef3c7",
            OrderIndex: 205,
            Highlights:
            [
                "Bảng định mức nguyên vật liệu BOM SUB_TABLE tính tổng giá thành",
                "Form Rule tự mở biên bản lỗi QC khi phát hiện sản phẩm không đạt",
                "Quy trình duyệt xuất xưởng 2 bước & in Phiếu Lệnh Sản xuất A4"
            ],
            Features: ["Kho Online", "BOM SUB_TABLE", "Kanban", "Phê duyệt"],
            Categories:
            [
                new("DM_TT_MES", "Tiến độ Sản xuất MES", "Các công đoạn chuyền sản xuất & QC",
                [
                    new("CHUAN_BI", "Chuẩn bị Vật tư", "#64748b", 1),
                    new("DANG_SX", "Đang gia công trên chuyền", "#1a73e8", 2),
                    new("QC_KIEM_TRA", "Đang kiểm tra QC / KCS", "#f59e0b", 3),
                    new("NHAP_KHO", "Đạt QC - Nhập kho TP", "#10b981", 4),
                    new("LOI_QC", "Không đạt QC - Cần xử lý", "#ef4444", 5)
                ])
            ],
            Modules:
            [
                new("SAN_XUAT", "Lệnh Sản xuất & Định mức BOM", "Build", "Kế hoạch sản xuất và kiểm soát chất lượng",
                [
                    new("mes_lenh_san_xuat", "Lệnh Sản xuất (Work Order)", "Bảng lệnh sản xuất kèm định mức BOM và kết quả KCS", "kanban", true, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "ma_lsx", ColumnName = "Mã Lệnh SX", DataType = "AUTO_NUMBER", DefaultValue = "LSX-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "ten_san_pham", ColumnName = "Thành phẩm sản xuất", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                        new() { ColumnCode = "so_luong_ke_hoach", ColumnName = "SL Kế hoạch", DataType = "NUMBER", IsRequired = true, DefaultValue = "100", Width = 125, OrderIndex = 3 },
                        new()
                        {
                            ColumnCode = "dinh_muc_bom", ColumnName = "Định mức Nguyên vật liệu (BOM)", DataType = "SUB_TABLE",
                            DefaultValue = AppTemplateCatalog.SubTableJson(
                                new("ten_vat_tu", "Nguyên vật liệu / Bán thành phẩm", "STRING", true, 230),
                                new("so_luong", "SL Định mức", "NUMBER", true, 100, "1"),
                                new("don_gia", "Đơn giá", "CURRENCY", true, 145),
                                new("thanh_tien", "Chi phí VL", "CURRENCY", false, 160, null, "so_luong * don_gia")
                            ),
                            ShowInTable = false, Width = 260, OrderIndex = 4
                        },
                        new() { ColumnCode = "tong_chi_phi_vl", ColumnName = "Tổng giá thành VL (Auto)", DataType = "CURRENCY", Formula = "SUM(dinh_muc_bom.thanh_tien)", Width = 180, OrderIndex = 5 },
                        new() { ColumnCode = "trang_thai_sx", ColumnName = "Công đoạn hiện tại", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TT_MES", DefaultValue = "CHUAN_BI", Width = 175, OrderIndex = 6 },
                        new() { ColumnCode = "ghi_chu_qc", ColumnName = "Biên bản Lỗi QC / Khắc phục", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 7 },
                        new() { ColumnCode = "trang_thai_duyet", ColumnName = "Duyệt lệnh SX", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 8 }
                    ])
                    {
                        FormRules = [AppTemplateCatalog.Rule("fr_mes_qc_fail", "ghi_chu_qc", "visible", "trang_thai_sx", "EQUALS", "LOI_QC", true, false)]
                    }
                ])
            ],
            SampleRecords:
            [
                new("mes_lenh_san_xuat", new()
                {
                    ["ma_lsx"] = "LSX-2026-0001",
                    ["ten_san_pham"] = "Bộ Điều khiển IoT SmartHub V2",
                    ["so_luong_ke_hoach"] = 500,
                    ["dinh_muc_bom"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["ten_vat_tu"] = "Bo mạch PCB ESP32-S3", ["so_luong"] = 500, ["don_gia"] = 145000m, ["thanh_tien"] = 72500000m },
                        new() { ["ten_vat_tu"] = "Vỏ nhôm CNC tản nhiệt", ["so_luong"] = 500, ["don_gia"] = 65000m, ["thanh_tien"] = 32500000m }
                    },
                    ["trang_thai_sx"] = "DANG_SX",
                    ["trang_thai_duyet"] = "DA_DUYET"
                })
            ],
            Widgets:
            [
                new("mes_lenh_san_xuat", "Tổng Chi phí Nguyên vật liệu BOM", "KPI_SUM", "tong_chi_phi_vl", null, null, null, 1, "#b45309", 1),
                new("mes_lenh_san_xuat", "Lệnh SX theo Công đoạn", "BAR_CHART", null, "trang_thai_sx", null, null, 2, "#1a73e8", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("mes_lenh_san_xuat", "Phê duyệt Lệnh Sản xuất & Cấp phát Vật tư", "Quản đốc phân xưởng & Giám đốc sản xuất duyệt lệnh BOM")],
            Automations = [new("Cảnh báo Lỗi QC trên chuyền", "Thông báo khẩn tới KCS/Quản đốc khi lệnh SX bị đánh dấu LOI_QC", "mes_lenh_san_xuat", "RECORD_UPDATED", "trang_thai_sx", "EQUALS", "LOI_QC", null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "CẢNH BÁO QC: Lệnh {{ma_lsx}} ({{ten_san_pham}}) phát sinh lỗi chất lượng cần xử lý.")],
            PrintTemplates =
            [
                new("mes_lenh_san_xuat", "Phiếu Lệnh Sản xuất & Định mức BOM (A4)", "A4", "portrait", true, true, false,
                    AppTemplateCatalog.PrintA4("LỆNH SẢN XUẤT & ĐỊNH MỨC VẬT TƯ (BOM)", "SmartApps MES 2.0", "ma_lsx",
                    [("Thành phẩm sản xuất", "ten_san_pham"), ("SL Kế hoạch", "so_luong_ke_hoach"), ("Công đoạn hiện tại", "trang_thai_sx"), ("Trạng thái duyệt", "trang_thai_duyet")],
                    "dinh_muc_bom", [("Nguyên vật liệu", "ten_vat_tu", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                    "Tổng chi phí nguyên vật liệu", "tong_chi_phi_vl", ("Kế hoạch sản xuất", "Quản đốc chuyền / KCS", "Giám đốc Nhà máy")), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("mes_lenh_san_xuat", "Lệnh Đang sản xuất", false, true, AppTemplateCatalog.SavedViewAnd("trang_thai_sx", "eq", "DANG_SX")),
                AppTemplateCatalog.ApprovalViewPending("mes_lenh_san_xuat", "Lệnh SX Chờ duyệt")
            ]
        },
        new(
            TemplateKey: "DMS_VAN_BAN",
            Code: "DMS_VAN_BAN",
            Name: "Quản lý Văn bản Đi/Đến & Trình Ký số (NĐ30)",
            Description: "Số hóa Văn thư Lưu trữ chuẩn Nghị định 30/2020/NĐ-CP: Sổ công văn đến, dự thảo tờ trình/công văn đi, phê duyệt ký số nội bộ và mẫu in thể thức hành chính A4.",
            Department: "Văn phòng & Hành chính",
            Category: "Kho Apps Online Đặc thù",
            Icon: "Description",
            Color: "#1d4ed8",
            BgColor: "#dbeafe",
            OrderIndex: 206,
            Highlights:
            [
                "Sổ đăng ký Văn bản Đến & Văn bản Đi chuẩn thể thức Nghị định 30/2020/NĐ-CP",
                "Luồng trình ký 2 cấp (Trưởng đơn vị -> Ban Giám đốc) tích hợp ký số nội bộ",
                "Tự động cảnh báo công văn Hỏa tốc/Khẩn và in phôi Công văn hành chính A4"
            ],
            Features: ["Kho Online", "Nghị định 30", "Ký số Nội bộ", "In Công văn A4"],
            Categories:
            [
                new("DM_LOAI_VB", "Loại Văn bản Hành chính", "Phân loại văn bản theo NĐ 30/2020/NĐ-CP",
                [
                    new("CONG_VAN", "Công văn", "#1d4ed8", 1),
                    new("TO_TRINH", "Tờ trình", "#7c3aed", 2),
                    new("QUYET_DINH", "Quyết định", "#dc2626", 3),
                    new("THONG_BAO", "Thông báo", "#0284c7", 4),
                    new("BIEN_BAN", "Biên bản", "#0f766e", 5)
                ]),
                new("DM_DO_KHAN", "Độ khẩn Văn bản", "Mức độ khẩn cấp xử lý công văn",
                [
                    new("THUONG", "Thường", "#64748b", 1),
                    new("KHAN", "Khẩn", "#f59e0b", 2),
                    new("HOA_TOC", "Hỏa tốc", "#dc2626", 3)
                ]),
                new("DM_TT_VB", "Trạng thái Xử lý Văn bản", "Tiến độ xử lý và phát hành văn bản",
                [
                    new("DU_THAO", "Dự thảo", "#64748b", 1),
                    new("CHO_KY", "Chờ duyệt & Ký số", "#f59e0b", 2),
                    new("DA_KY_SO", "Đã ký số & Ban hành", "#10b981", 3),
                    new("THU_HOI", "Thu hồi / Hủy", "#ef4444", 4)
                ])
            ],
            Modules:
            [
                new("VAN_THU_SO", "Sổ Văn bản Đi/Đến & Trình ký", "Description", "Tiếp nhận công văn đến, trình ký số và phát hành văn bản đi",
                [
                    new("dms_van_ban_di", "Văn bản Đi & Hồ sơ Trình ký số", "Quản lý dự thảo, trình duyệt, ký số và phát hành văn bản đi", "table", true, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "so_ky_hieu", ColumnName = "Số & Ký hiệu", DataType = "AUTO_NUMBER", DefaultValue = "CV-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 145, OrderIndex = 1 },
                        new() { ColumnCode = "loai_vb", ColumnName = "Loại văn bản", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_VB", DefaultValue = "CONG_VAN", Width = 150, OrderIndex = 2 },
                        new() { ColumnCode = "trich_yeu", ColumnName = "Trích yếu nội dung", DataType = "STRING", IsRequired = true, Width = 260, OrderIndex = 3 },
                        new() { ColumnCode = "do_khan", ColumnName = "Độ khẩn", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_DO_KHAN", DefaultValue = "THUONG", Width = 130, OrderIndex = 4 },
                        new() { ColumnCode = "noi_nhan", ColumnName = "Nơi nhận / Cơ quan đích", DataType = "STRING", IsRequired = true, Width = 210, OrderIndex = 5 },
                        new() { ColumnCode = "ngay_ban_hanh", ColumnName = "Ngày ban hành", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 6 },
                        new() { ColumnCode = "nguoi_ky", ColumnName = "Người thẩm quyền ký", DataType = "STRING", DefaultValue = "Tổng Giám đốc", Width = 175, OrderIndex = 7 },
                        new() { ColumnCode = "noi_dung_chinh", ColumnName = "Nội dung chi tiết / Căn cứ", DataType = "TEXTAREA", ShowInTable = false, Width = 260, OrderIndex = 8 },
                        new() { ColumnCode = "ly_do_hoa_toc", ColumnName = "Lý do trình Hỏa tốc", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 9 },
                        new() { ColumnCode = "ma_chung_thu_so", ColumnName = "Mã Chứng thư Ký số (SHA-256)", DataType = "STRING", DefaultValue = "VNPT-CA-VERIFIED-2026", Width = 200, OrderIndex = 10 },
                        new() { ColumnCode = "trang_thai_vb", ColumnName = "Trạng thái phát hành", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TT_VB", DefaultValue = "CHO_KY", Width = 175, OrderIndex = 11 },
                        new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 12 }
                    ])
                    {
                        FormRules =
                        [
                            AppTemplateCatalog.Rule("fr_dms_urgent_reason", "ly_do_hoa_toc", "visible", "do_khan", "EQUALS", "HOA_TOC", true, false),
                            AppTemplateCatalog.Rule("fr_dms_cert_ro", "ma_chung_thu_so", "readOnly", "trang_thai_vb", "EQUALS", "DA_KY_SO", false, true)
                        ]
                    },
                    new("dms_van_ban_den", "Sổ Đăng ký Văn bản Đến", "Tiếp nhận, phân luồng và theo dõi hạn xử lý công văn đến", "table", false, "trang_thai_duyet", false,
                    [
                        new() { ColumnCode = "so_den", ColumnName = "Số đến", DataType = "AUTO_NUMBER", DefaultValue = "VBD-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                        new() { ColumnCode = "so_hieu_goc", ColumnName = "Số ký hiệu gốc", DataType = "STRING", IsRequired = true, Width = 155, OrderIndex = 2 },
                        new() { ColumnCode = "co_quan_ban_hanh", ColumnName = "Cơ quan ban hành", DataType = "STRING", IsRequired = true, Width = 210, OrderIndex = 3 },
                        new() { ColumnCode = "trich_yeu_den", ColumnName = "Trích yếu văn bản đến", DataType = "STRING", IsRequired = true, Width = 260, OrderIndex = 4 },
                        new() { ColumnCode = "do_khan", ColumnName = "Độ khẩn", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_DO_KHAN", DefaultValue = "THUONG", Width = 130, OrderIndex = 5 },
                        new() { ColumnCode = "han_xu_ly", ColumnName = "Hạn xử lý", DataType = "DATE", Width = 135, OrderIndex = 6 },
                        new() { ColumnCode = "lanh_dao_but_phe", ColumnName = "Ý kiến Bút phê Lãnh đạo", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 7 }
                    ])
                ])
            ],
            SampleRecords:
            [
                new("dms_van_ban_di", new()
                {
                    ["so_ky_hieu"] = "CV-2026-0001",
                    ["loai_vb"] = "QUYET_DINH",
                    ["trich_yeu"] = "Quyết định ban hành Quy chế Chuyển đổi số & Văn phòng Không giấy tờ 2026",
                    ["do_khan"] = "KHAN",
                    ["noi_nhan"] = "Toàn thể Các Khối / Phòng ban trực thuộc",
                    ["ngay_ban_hanh"] = "2026-03-01",
                    ["nguoi_ky"] = "Tổng Giám đốc Nguyễn Minh Quân",
                    ["noi_dung_chinh"] = "Áp dụng chữ ký số nội bộ và phê duyệt điện tử trên toàn hệ thống SmartApps 3.0.",
                    ["ma_chung_thu_so"] = "VNPT-CA-SHA256-9F8A21C0",
                    ["trang_thai_vb"] = "DA_KY_SO",
                    ["trang_thai_duyet"] = "DA_DUYET"
                }),
                new("dms_van_ban_den", new()
                {
                    ["so_den"] = "VBD-2026-0001",
                    ["so_hieu_goc"] = "142/BTTTT-CĐSQG",
                    ["co_quan_ban_hanh"] = "Cục Chuyển đổi số Quốc gia",
                    ["trich_yeu_den"] = "Hướng dẫn kết nối trục liên thông văn bản và lưu trữ hồ sơ điện tử",
                    ["do_khan"] = "KHAN",
                    ["han_xu_ly"] = "2026-03-15",
                    ["lanh_dao_but_phe"] = "Giao Phòng Hành chính & CNTT phối hợp triển khai trước hạn."
                })
            ],
            Widgets:
            [
                new("dms_van_ban_di", "Tổng Văn bản Đi & Trình ký", "KPI_COUNT", null, null, null, null, 1, "#1d4ed8", 1),
                new("dms_van_ban_di", "Phân bố Theo Loại Văn bản", "DONUT_CHART", null, "loai_vb", null, null, 2, "#7c3aed", 2)
            ])
        {
            Workflows = [AppTemplateCatalog.Wf2Step("dms_van_ban_di", "Trình duyệt & Ký số Phát hành Văn bản (NĐ30)", "Chánh Văn phòng kiểm tra thể thức -> Ban Giám đốc ký số ban hành")],
            Automations = [new("Cảnh báo Văn bản Hỏa tốc trình ký", "Thông báo khẩn toàn hệ thống khi có văn bản Hỏa tốc mới", "dms_van_ban_di", "RECORD_CREATED", "do_khan", "EQUALS", "HOA_TOC", null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "HỎA TỐC: Văn bản {{so_ky_hieu}} ({{trich_yeu}}) vừa trình ký khẩn tới {{noi_nhan}}.")],
            PrintTemplates =
            [
                new("dms_van_ban_di", "Mẫu Công văn Hành chính Chuẩn Nghị định 30 (A4)", "A4", "portrait", true, true, false,
                    AppTemplateCatalog.PrintA4("VĂN BẢN HÀNH CHÍNH & XÁC THỰC KÝ SỐ (NĐ 30/2020/NĐ-CP)", "SmartApps e-Document & Digital Signature 3.0", "so_ky_hieu",
                    [("Loại văn bản", "loai_vb"), ("Trích yếu", "trich_yeu"), ("Độ khẩn", "do_khan"), ("Nơi nhận", "noi_nhan"), ("Ngày ban hành", "ngay_ban_hanh"), ("Người ký", "nguoi_ky"), ("Chứng thư số", "ma_chung_thu_so"), ("Trạng thái", "trang_thai_vb")],
                    null, [], null, null, ("Người soạn thảo", "Chánh Văn phòng / Kiểm soát thể thức", "Thủ trưởng Cơ quan (Ký số & Đóng dấu)")), AppTemplateCatalog.DefaultA4Css)
            ],
            SavedViews =
            [
                new("dms_van_ban_di", "Văn bản Đã ký số Ban hành", false, true, AppTemplateCatalog.SavedViewAnd("trang_thai_vb", "eq", "DA_KY_SO")),
                AppTemplateCatalog.ApprovalViewPending("dms_van_ban_di", "Văn bản Chờ Ký duyệt")
            ]
        }
    ];
}
