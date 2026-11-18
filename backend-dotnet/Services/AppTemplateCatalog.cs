using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public record TemplateCategoryItemSpec(string ItemCode, string ItemName, string Color, int OrderIndex);

public record TemplateCategorySpec(
    string CategoryCode,
    string CategoryName,
    string Description,
    List<TemplateCategoryItemSpec> Items);

public record TemplateColumnSpec
{
    public string ColumnCode { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public string DataType { get; init; } = "STRING";
    public bool IsRequired { get; init; }
    public bool IsIdentityKey { get; init; }
    public bool ShowInTable { get; init; } = true;
    public bool ShowInForm { get; init; } = true;
    public int Width { get; init; } = 170;
    public string? ValidationRegex { get; init; }
    public string? RefCategoryCode { get; init; }
    public string? RefAppId { get; init; }
    public string? RefTableId { get; init; }
    public string? InternalRefTableName { get; init; }
    public string? RefValueField { get; init; }
    public string? RefDisplayField { get; init; }
    public string? Formula { get; init; }
    public string? DefaultValue { get; init; }
    public bool ShowInParentForm { get; init; }
    public int OrderIndex { get; init; } = 1;
}

public record TemplateTableSpec(
    string TableName,
    string DisplayName,
    string Description,
    string PrimaryView,
    bool ApprovalEnabled,
    string ApprovalField,
    bool PublicFormEnabled,
    List<TemplateColumnSpec> Columns);

public record TemplateModuleSpec(
    string Code,
    string Name,
    string Icon,
    string Description,
    List<TemplateTableSpec> Tables);

public record TemplateRecordSpec(string TableName, Dictionary<string, object?> Data);

public record TemplateWidgetSpec(
    string TableName,
    string Title,
    string WidgetType,
    string? MetricField,
    string? GroupField,
    string? FilterField,
    string? FilterValue,
    int ColSpan,
    string Color,
    int OrderIndex);

public record AppTemplateDefinition(
    string TemplateKey,
    string Code,
    string Name,
    string Description,
    string Department,
    string Category,
    string Icon,
    string Color,
    string BgColor,
    int OrderIndex,
    List<string> Highlights,
    List<string> Features,
    List<TemplateCategorySpec> Categories,
    List<TemplateModuleSpec> Modules,
    List<TemplateRecordSpec> SampleRecords,
    List<TemplateWidgetSpec> Widgets);

/// <summary>
/// Built-in catalog of commonly used enterprise sample application templates.
/// </summary>
public static class AppTemplateCatalog
{
    private static readonly IReadOnlyList<AppTemplateDefinition> AllTemplates = BuildTemplates();

    public static IReadOnlyList<AppTemplateDefinition> GetAll() => AllTemplates;

    public static AppTemplateDefinition? Find(string templateKey)
    {
        if (string.IsNullOrWhiteSpace(templateKey)) return null;
        var key = templateKey.Trim();
        return AllTemplates.FirstOrDefault(t =>
            t.TemplateKey.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            t.Code.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static List<AppTemplateSummaryDto> GetSummaries() =>
        AllTemplates.Select(t => new AppTemplateSummaryDto
        {
            TemplateKey = t.TemplateKey,
            Code = t.Code,
            Name = t.Name,
            Description = t.Description,
            Department = t.Department,
            Category = t.Category,
            Icon = t.Icon,
            Color = t.Color,
            BgColor = t.BgColor,
            Highlights = t.Highlights,
            Features = t.Features,
            ModuleCount = t.Modules.Count,
            TableCount = t.Modules.Sum(m => m.Tables.Count),
            SampleRecordCount = t.SampleRecords.Count,
            WidgetCount = t.Widgets.Count
        }).ToList();

    private static TemplateColumnSpec HrmLookupCol(string code, string name, int order, bool required = true) => new()
    {
        ColumnCode = code,
        ColumnName = name,
        DataType = "LOOKUP",
        IsRequired = required,
        Width = 195,
        RefAppId = "app_hrm",
        RefTableId = "tbl_hrm_nhanvien",
        RefValueField = "ma_nv",
        RefDisplayField = "ho_ten",
        OrderIndex = order
    };

    private static List<AppTemplateDefinition> BuildTemplates() =>
    [
        BuildCrmTemplate(),
        BuildFinanceTemplate(),
        BuildPurchaseTemplate(),
        BuildWmsTemplate(),
        BuildProjectTemplate(),
        BuildRecruitTemplate(),
        BuildContractTemplate(),
        BuildHelpdeskTemplate(),
        BuildMeetingTemplate(),
        BuildTrainingTemplate(),
        BuildKpiOkrTemplate(),
        BuildFleetTemplate(),
        BuildProcurementVnSolution(),
        BuildSalesInventoryVnSolution(),
        BuildHandoverVnSolution()
    ];

    private static AppTemplateDefinition BuildCrmTemplate() => new(
        TemplateKey: "CRM",
        Code: "CRM",
        Name: "Quản lý Khách hàng & Cơ hội (CRM)",
        Description: "Quản lý danh sách khách hàng B2B, theo dõi pipeline cơ hội bán hàng dạng Kanban và tự tính doanh thu kỳ vọng.",
        Department: "Khối Kinh doanh",
        Category: "Kinh doanh & CSKH",
        Icon: "Handshake",
        Color: "#0284c7",
        BgColor: "#e0f2fe",
        OrderIndex: 10,
        Highlights:
        [
            "Pipeline cơ hội dạng Kanban kéo-thả theo giai đoạn",
            "Tự động tính doanh_thu_ky_vong = ROUND(gia_tri * xac_suat / 100, 0)",
            "Liên kết nhân viên Sale phụ trách trực tiếp từ App HRM"
        ],
        Features: ["Kanban", "Liên kết HRM", "Công thức"],
        Categories:
        [
            new("DM_NGANH_NGHE", "Ngành nghề Khách hàng", "Phân loại lĩnh vực kinh doanh của đối tác",
            [
                new("CNTT", "Công nghệ Thông tin", "#1a73e8", 1),
                new("SAN_XUAT", "Sản xuất & Công nghiệp", "#ea8600", 2),
                new("BAN_LE", "Bán lẻ & Thương mại", "#1e8e3e", 3),
                new("TAI_CHINH", "Tài chính & Ngân hàng", "#7b1fa2", 4),
                new("BAT_DONG_SAN", "Bất động sản & Xây dựng", "#0284c7", 5)
            ]),
            new("DM_GIAI_DOAN_CRM", "Giai đoạn Cơ hội CRM", "Pipeline bán hàng chuẩn B2B",
            [
                new("TIEP_CAN", "Tiếp cận mới", "#5f6368", 1),
                new("TU_VAN", "Tư vấn & Demo", "#1a73e8", 2),
                new("BAO_GIA", "Đã gửi Báo giá", "#ea8600", 3),
                new("DAM_PHAN", "Đàm phán Hợp đồng", "#7b1fa2", 4),
                new("CHOT_THANH_CONG", "Chốt thành công (Won)", "#1e8e3e", 5),
                new("THAT_BAI", "Không thành công (Lost)", "#d93025", 6)
            ])
        ],
        Modules:
        [
            new("KHACHHANG", "Khách hàng Doanh nghiệp", "BusinessCenter", "Hồ sơ khách hàng & đầu mối liên hệ",
            [
                new("crm_khach_hang", "Danh sách Khách hàng", "Lưu trữ thông tin doanh nghiệp và nhân sự Sale chăm sóc", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_kh", ColumnName = "Mã Khách hàng", DataType = "AUTO_NUMBER", DefaultValue = "KH-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "ten_cong_ty", ColumnName = "Tên Công ty / Khách hàng", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    new() { ColumnCode = "nganh_nghe", ColumnName = "Ngành nghề", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_NGANH_NGHE", Width = 185, OrderIndex = 3 },
                    new() { ColumnCode = "nguoi_lien_he", ColumnName = "Người liên hệ", DataType = "STRING", IsRequired = true, Width = 175, OrderIndex = 4 },
                    new() { ColumnCode = "email_lh", ColumnName = "Email", DataType = "EMAIL", Width = 200, OrderIndex = 5 },
                    new() { ColumnCode = "so_dt", ColumnName = "Số điện thoại", DataType = "TEL", Width = 140, OrderIndex = 6 },
                    HrmLookupCol("sale_phu_trach", "Sale phụ trách (HRM)", 7),
                    new() { ColumnCode = "dang_hop_tac", ColumnName = "Khách hàng thân thiết", DataType = "BOOLEAN", DefaultValue = "true", Width = 145, OrderIndex = 8 }
                ])
            ]),
            new("PIPELINE", "Cơ hội Bán hàng", "Handshake", "Theo dõi cơ hội và doanh thu dự kiến",
            [
                new("crm_co_hoi", "Pipeline Cơ hội Bán hàng", "Theo dõi tiến trình chốt đơn từ tiếp cận đến ký hợp đồng", "kanban", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_deal", ColumnName = "Mã Cơ hội", DataType = "AUTO_NUMBER", DefaultValue = "DEAL-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "ten_co_hoi", ColumnName = "Tên Cơ hội / Dự án", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    new() { ColumnCode = "ma_kh", ColumnName = "Khách hàng", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "crm_khach_hang", RefValueField = "ma_kh", RefDisplayField = "ten_cong_ty", ShowInParentForm = true, Width = 210, OrderIndex = 3 },
                    new() { ColumnCode = "giai_doan", ColumnName = "Giai đoạn Pipeline", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_GIAI_DOAN_CRM", DefaultValue = "TIEP_CAN", Width = 185, OrderIndex = 4 },
                    HrmLookupCol("phu_trach", "Sale phụ trách (HRM)", 5),
                    new() { ColumnCode = "gia_tri", ColumnName = "Giá trị HĐ (VNĐ)", DataType = "CURRENCY", IsRequired = true, Width = 165, OrderIndex = 6 },
                    new() { ColumnCode = "xac_suat", ColumnName = "Xác suất (%)", DataType = "NUMBER", DefaultValue = "50", Width = 120, OrderIndex = 7 },
                    new() { ColumnCode = "doanh_thu_ky_vong", ColumnName = "Doanh thu kỳ vọng", DataType = "CURRENCY", Formula = "ROUND(gia_tri * xac_suat / 100, 0)", Width = 180, OrderIndex = 8 },
                    new() { ColumnCode = "ngay_du_kien", ColumnName = "Ngày dự kiến chốt", DataType = "DATE", Width = 145, OrderIndex = 9 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("crm_khach_hang", new() { ["ma_kh"] = "KH-2026-0001", ["ten_cong_ty"] = "Công ty CP Công nghệ Sao Việt", ["nganh_nghe"] = "CNTT", ["nguoi_lien_he"] = "Phạm Minh Tuấn", ["email_lh"] = "tuan.pm@saoviet.vn", ["so_dt"] = "0903456789", ["sale_phu_trach"] = "NV001", ["dang_hop_tac"] = true }),
            new("crm_khach_hang", new() { ["ma_kh"] = "KH-2026-0002", ["ten_cong_ty"] = "Tập đoàn Sản xuất Hòa Bình", ["nganh_nghe"] = "SAN_XUAT", ["nguoi_lien_he"] = "Nguyễn Thu Hà", ["email_lh"] = "ha.nt@hoabinhgroup.vn", ["so_dt"] = "0918765432", ["sale_phu_trach"] = "NV002", ["dang_hop_tac"] = true }),
            new("crm_khach_hang", new() { ["ma_kh"] = "KH-2026-0003", ["ten_cong_ty"] = "Chuỗi Bán lẻ WinMart Pro", ["nganh_nghe"] = "BAN_LE", ["nguoi_lien_he"] = "Trần Quốc Bảo", ["email_lh"] = "bao.tq@winmartpro.vn", ["so_dt"] = "0988112233", ["sale_phu_trach"] = "NV001", ["dang_hop_tac"] = false }),
            new("crm_co_hoi", new() { ["ma_deal"] = "DEAL-2026-0001", ["ten_co_hoi"] = "Triển khai ERP & Cổng dữ liệu nội bộ", ["ma_kh"] = "KH-2026-0001", ["giai_doan"] = "DAM_PHAN", ["phu_trach"] = "NV001", ["gia_tri"] = 650000000m, ["xac_suat"] = 80, ["ngay_du_kien"] = "2026-11-28" }),
            new("crm_co_hoi", new() { ["ma_deal"] = "DEAL-2026-0002", ["ten_co_hoi"] = "Hệ thống Quản lý Kho & Bảo trì nhà máy", ["ma_kh"] = "KH-2026-0002", ["giai_doan"] = "BAO_GIA", ["phu_trach"] = "NV002", ["gia_tri"] = 420000000m, ["xac_suat"] = 60, ["ngay_du_kien"] = "2026-12-10" }),
            new("crm_co_hoi", new() { ["ma_deal"] = "DEAL-2026-0003", ["ten_co_hoi"] = "Gói License 200 user SmartApps Cloud", ["ma_kh"] = "KH-2026-0003", ["giai_doan"] = "CHOT_THANH_CONG", ["phu_trach"] = "NV001", ["gia_tri"] = 280000000m, ["xac_suat"] = 100, ["ngay_du_kien"] = "2026-11-15" }),
            new("crm_co_hoi", new() { ["ma_deal"] = "DEAL-2026-0004", ["ten_co_hoi"] = "Tư vấn chuyển đổi số Khối Tài chính", ["ma_kh"] = "KH-2026-0002", ["giai_doan"] = "TU_VAN", ["phu_trach"] = "NV003", ["gia_tri"] = 190000000m, ["xac_suat"] = 35, ["ngay_du_kien"] = "2026-12-20" })
        ],
        Widgets:
        [
            new("crm_co_hoi", "Tổng Giá trị Pipeline", "KPI_SUM", "gia_tri", null, null, null, 1, "#0284c7", 1),
            new("crm_co_hoi", "Doanh thu Kỳ vọng", "KPI_SUM", "doanh_thu_ky_vong", null, null, null, 1, "#1e8e3e", 2),
            new("crm_khach_hang", "Khách hàng Doanh nghiệp", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 3),
            new("crm_co_hoi", "Cơ hội đã Chốt (Won)", "KPI_COUNT", null, null, "giai_doan", "CHOT_THANH_CONG", 1, "#ea8600", 4),
            new("crm_co_hoi", "Phân bổ Pipeline theo Giai đoạn", "DONUT_CHART", null, "giai_doan", null, null, 2, "#0284c7", 5),
            new("crm_khach_hang", "Khách hàng theo Ngành nghề", "BAR_CHART", null, "nganh_nghe", null, null, 2, "#1e8e3e", 6)
        ]);

    private static AppTemplateDefinition BuildFinanceTemplate() => new(
        TemplateKey: "FINANCE",
        Code: "FINANCE",
        Name: "Đề nghị Thanh toán & Tạm ứng",
        Description: "Số hóa phiếu đề nghị thanh toán, bảng kê chứng từ chi tiết, tạm ứng công tác và luồng phê duyệt tài chính.",
        Department: "Tài chính - Kế toán",
        Category: "Tài chính & Mua sắm",
        Icon: "Payments",
        Color: "#ea8600",
        BgColor: "#fef7e0",
        OrderIndex: 11,
        Highlights:
        [
            "Bảng kê chứng từ con (SUB_TABLE) tự tổng hợp tổng tiền + VAT",
            "Bật sẵn Luồng Phê duyệt & Form thu thập công khai",
            "Gồm cả Phiếu Đề nghị Thanh toán & Tạm ứng Hoàn ứng"
        ],
        Features: ["Phê duyệt", "Sub-table", "Form công khai", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_LOAI_CHI_PHI", "Loại Chi phí Tài chính", "Phân loại khoản mục chi phí hạch toán",
            [
                new("VAN_PHONG", "Chi phí Văn phòng & Tiếp khách", "#1a73e8", 1),
                new("CONG_TAC", "Công tác phí & Lưu trú", "#1e8e3e", 2),
                new("MARKETING", "Marketing & Sự kiện", "#ea8600", 3),
                new("HA_TANG_IT", "Hạ tầng IT & Phần mềm", "#7b1fa2", 4),
                new("NCC", "Thanh toán Nhà cung cấp", "#0284c7", 5)
            ])
        ],
        Modules:
        [
            new("THANHTOAN", "Thanh toán & Hoàn ứng", "Payments", "Quản lý chứng từ chi phí và tạm ứng nội bộ",
            [
                new("fin_de_nghi_thanh_toan", "Phiếu Đề nghị Thanh toán", "Bảng kê chi tiết chứng từ, tự động tính VAT và trình duyệt", "table", true, "trang_thai_duyet", true,
                [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã Phiếu", DataType = "AUTO_NUMBER", DefaultValue = "DNTT-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "noi_dung", ColumnName = "Nội dung thanh toán", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    new() { ColumnCode = "loai_chi_phi", ColumnName = "Khoản mục chi phí", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_CHI_PHI", Width = 195, OrderIndex = 3 },
                    HrmLookupCol("nguoi_de_nghi", "Người đề nghị (HRM)", 4),
                    new() { ColumnCode = "ngay_de_nghi", ColumnName = "Ngày đề nghị", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 5 },
                    new() { ColumnCode = "bang_ke", ColumnName = "Bảng kê hóa đơn / chứng từ", DataType = "SUB_TABLE", DefaultValue = "hang_muc,so_tien,ghi_chu", ShowInTable = false, Width = 260, OrderIndex = 6 },
                    new() { ColumnCode = "tong_tien_hang", ColumnName = "Tiền hàng (Auto)", DataType = "CURRENCY", Formula = "SUM(bang_ke.so_tien)", Width = 160, OrderIndex = 7 },
                    new() { ColumnCode = "thue_vat", ColumnName = "Thuế VAT (VNĐ)", DataType = "CURRENCY", DefaultValue = "0", Width = 140, OrderIndex = 8 },
                    new() { ColumnCode = "tong_cong", ColumnName = "Tổng thanh toán (Auto)", DataType = "CURRENCY", Formula = "tong_tien_hang + thue_vat", Width = 175, OrderIndex = 9 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 10 }
                ]),
                new("fin_tam_ung", "Phiếu Tạm ứng & Hoàn ứng", "Theo dõi khoản tạm ứng công tác và hạn hoàn chứng từ", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_tu", ColumnName = "Mã Tạm ứng", DataType = "AUTO_NUMBER", DefaultValue = "TU-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "muc_dich", ColumnName = "Mục đích tạm ứng", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    HrmLookupCol("nhan_vien", "Nhân viên nhận ứng", 3),
                    new() { ColumnCode = "so_tien", ColumnName = "Số tiền tạm ứng", DataType = "CURRENCY", IsRequired = true, Width = 165, OrderIndex = 4 },
                    new() { ColumnCode = "han_hoan_ung", ColumnName = "Hạn hoàn ứng", DataType = "DATE", IsRequired = true, Width = 140, OrderIndex = 5 },
                    new() { ColumnCode = "da_hoan_ung", ColumnName = "Đã quyết toán", DataType = "BOOLEAN", DefaultValue = "false", Width = 135, OrderIndex = 6 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 7 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("fin_de_nghi_thanh_toan", new()
            {
                ["ma_phieu"] = "DNTT-2026-0001",
                ["noi_dung"] = "Gia hạn Cloud Server & Tên miền Q4/2026",
                ["loai_chi_phi"] = "HA_TANG_IT",
                ["nguoi_de_nghi"] = "NV001",
                ["ngay_de_nghi"] = "2026-11-10",
                ["bang_ke"] = new List<Dictionary<string, object?>>
                {
                    new() { ["hang_muc"] = "AWS RDS PostgreSQL Production", ["so_tien"] = 18500000m, ["ghi_chu"] = "Hóa đơn tháng 11" },
                    new() { ["hang_muc"] = "Cloudflare Enterprise DNS", ["so_tien"] = 4500000m, ["ghi_chu"] = "Gia hạn quý 4" }
                },
                ["thue_vat"] = 2300000m,
                ["trang_thai_duyet"] = "DA_DUYET"
            }),
            new("fin_de_nghi_thanh_toan", new()
            {
                ["ma_phieu"] = "DNTT-2026-0002",
                ["noi_dung"] = "Chi phí tổ chức Workshop Khách hàng Hà Nội",
                ["loai_chi_phi"] = "MARKETING",
                ["nguoi_de_nghi"] = "NV002",
                ["ngay_de_nghi"] = "2026-11-14",
                ["bang_ke"] = new List<Dictionary<string, object?>>
                {
                    new() { ["hang_muc"] = "Thuê hội trường & Âm thanh", ["so_tien"] = 15000000m, ["ghi_chu"] = "Khách sạn Melia" },
                    new() { ["hang_muc"] = "Tea-break & Tài liệu in ấn", ["so_tien"] = 6200000m, ["ghi_chu"] = "60 khách mời" }
                },
                ["thue_vat"] = 1696000m,
                ["trang_thai_duyet"] = "CHO_DUYET"
            }),
            new("fin_de_nghi_thanh_toan", new()
            {
                ["ma_phieu"] = "DNTT-2026-0003",
                ["noi_dung"] = "Mua văn phòng phẩm & Nước uống tháng 11",
                ["loai_chi_phi"] = "VAN_PHONG",
                ["nguoi_de_nghi"] = "NV003",
                ["ngay_de_nghi"] = "2026-11-15",
                ["bang_ke"] = new List<Dictionary<string, object?>>
                {
                    new() { ["hang_muc"] = "Giấy in A4 & Mực máy in", ["so_tien"] = 3400000m, ["ghi_chu"] = "Kho văn phòng" }
                },
                ["thue_vat"] = 340000m,
                ["trang_thai_duyet"] = "CHO_DUYET"
            }),
            new("fin_tam_ung", new()
            {
                ["ma_tu"] = "TU-2026-0001",
                ["muc_dich"] = "Tạm ứng công tác triển khai dự án Đà Nẵng",
                ["nhan_vien"] = "NV001",
                ["so_tien"] = 12000000m,
                ["han_hoan_ung"] = "2026-11-30",
                ["da_hoan_ung"] = false,
                ["trang_thai_duyet"] = "DA_DUYET"
            }),
            new("fin_tam_ung", new()
            {
                ["ma_tu"] = "TU-2026-0002",
                ["muc_dich"] = "Tạm ứng chi phí kiểm toán nội bộ cuối năm",
                ["nhan_vien"] = "NV003",
                ["so_tien"] = 8500000m,
                ["han_hoan_ung"] = "2026-12-05",
                ["da_hoan_ung"] = false,
                ["trang_thai_duyet"] = "CHO_DUYET"
            })
        ],
        Widgets:
        [
            new("fin_de_nghi_thanh_toan", "Tổng Giá trị Đề nghị Chi", "KPI_SUM", "tong_cong", null, null, null, 1, "#ea8600", 1),
            new("fin_de_nghi_thanh_toan", "Phiếu Thanh toán Chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#d93025", 2),
            new("fin_tam_ung", "Tổng Tiền Tạm ứng", "KPI_SUM", "so_tien", null, null, null, 1, "#1a73e8", 3),
            new("fin_de_nghi_thanh_toan", "Đã Phê duyệt Chi", "KPI_COUNT", null, null, "trang_thai_duyet", "DA_DUYET", 1, "#1e8e3e", 4),
            new("fin_de_nghi_thanh_toan", "Cơ cấu Chi phí theo Khoản mục", "DONUT_CHART", null, "loai_chi_phi", null, null, 2, "#ea8600", 5),
            new("fin_de_nghi_thanh_toan", "Phiếu Thanh toán gần nhất", "RECENT_LIST", null, null, null, null, 2, "#1a73e8", 6)
        ]);

    private static AppTemplateDefinition BuildPurchaseTemplate() => new(
        TemplateKey: "PURCHASE",
        Code: "PURCHASE",
        Name: "Quản lý Mua hàng & Nhà cung cấp",
        Description: "Quản lý hồ sơ nhà cung cấp, lập yêu cầu & đơn đặt hàng (PO) kèm bảng hàng hóa chi tiết và duyệt ngân sách.",
        Department: "Cung ứng & Mua hàng",
        Category: "Tài chính & Mua sắm",
        Icon: "Storefront",
        Color: "#0d9488",
        BgColor: "#ccfbf1",
        OrderIndex: 12,
        Highlights:
        [
            "Quản lý tập trung Nhà cung cấp kèm xếp hạng chất lượng",
            "Đơn đặt hàng (PO) có bảng hàng hóa SUB_TABLE tự tính tổng giá trị",
            "Trình duyệt đơn mua hàng & liên kết người đề xuất từ HRM"
        ],
        Features: ["Phê duyệt", "Sub-table", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_NHOM_NCC", "Nhóm Nhà cung cấp", "Phân loại đối tác cung ứng",
            [
                new("IT_HW", "Thiết bị CNTT & Máy chủ", "#1a73e8", 1),
                new("VPP_NOITHAT", "Văn phòng phẩm & Nội thất", "#ea8600", 2),
                new("DICH_VU", "Dịch vụ & Thuê ngoài", "#7b1fa2", 3),
                new("NGUYEN_LIEU", "Nguyên vật liệu sản xuất", "#1e8e3e", 4)
            ]),
            new("DM_TRANG_THAI_PO", "Tiến độ Đơn mua hàng (PO)", "Theo dõi giao nhận đơn đặt hàng",
            [
                new("MOI_TAO", "Mới tạo / Chờ báo giá", "#5f6368", 1),
                new("DA_DAT_HANG", "Đã gửi PO cho NCC", "#1a73e8", 2),
                new("DANG_GIAO", "Đang giao hàng", "#ea8600", 3),
                new("DA_NHAP_KHO", "Đã nhận đủ hàng", "#1e8e3e", 4)
            ])
        ],
        Modules:
        [
            new("NCC", "Nhà cung cấp", "Storefront", "Danh bạ đối tác cung ứng đã thẩm định",
            [
                new("pur_nha_cung_cap", "Danh sách Nhà cung cấp", "Lưu thông tin liên hệ, nhóm ngành hàng và đánh giá NCC", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_ncc", ColumnName = "Mã NCC", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 125, OrderIndex = 1 },
                    new() { ColumnCode = "ten_ncc", ColumnName = "Tên Nhà cung cấp", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    new() { ColumnCode = "nhom_ncc", ColumnName = "Nhóm hàng hóa", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_NHOM_NCC", Width = 185, OrderIndex = 3 },
                    new() { ColumnCode = "nguoi_lien_he", ColumnName = "Người liên hệ", DataType = "STRING", Width = 165, OrderIndex = 4 },
                    new() { ColumnCode = "email_ncc", ColumnName = "Email", DataType = "EMAIL", Width = 195, OrderIndex = 5 },
                    new() { ColumnCode = "so_dt", ColumnName = "Điện thoại", DataType = "TEL", Width = 135, OrderIndex = 6 },
                    new() { ColumnCode = "danh_gia", ColumnName = "Điểm đánh giá (1-5)", DataType = "NUMBER", DefaultValue = "5", Width = 140, OrderIndex = 7 }
                ])
            ]),
            new("DONMUA", "Đơn mua hàng (PO)", "Assignment", "Đề xuất mua sắm và theo dõi giao hàng",
            [
                new("pur_don_mua_hang", "Đơn Đặt hàng (PO)", "Lập đơn mua hàng nhiều mặt hàng, tự tính tổng giá trị và phê duyệt", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_po", ColumnName = "Mã PO", DataType = "AUTO_NUMBER", DefaultValue = "PO-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "tieu_de", ColumnName = "Nội dung mua sắm", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    new() { ColumnCode = "ma_ncc", ColumnName = "Nhà cung cấp", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "pur_nha_cung_cap", RefValueField = "ma_ncc", RefDisplayField = "ten_ncc", Width = 210, OrderIndex = 3 },
                    HrmLookupCol("nguoi_yeu_cau", "Người đề xuất (HRM)", 4),
                    new() { ColumnCode = "ngay_can_hang", ColumnName = "Ngày cần hàng", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 5 },
                    new() { ColumnCode = "chi_tiet_hang", ColumnName = "Danh sách mặt hàng", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,don_gia,thanh_tien", ShowInTable = false, Width = 260, OrderIndex = 6 },
                    new() { ColumnCode = "tong_gia_tri", ColumnName = "Tổng giá trị PO", DataType = "CURRENCY", Formula = "SUM(chi_tiet_hang.thanh_tien)", Width = 170, OrderIndex = 7 },
                    new() { ColumnCode = "tien_do_po", ColumnName = "Tiến độ giao hàng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_PO", DefaultValue = "MOI_TAO", Width = 175, OrderIndex = 8 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 9 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("pur_nha_cung_cap", new() { ["ma_ncc"] = "NCC-001", ["ten_ncc"] = "Công ty Tin học Phong Vũ Pro", ["nhom_ncc"] = "IT_HW", ["nguoi_lien_he"] = "Vũ Đức Anh", ["email_ncc"] = "b2b@phongvupro.vn", ["so_dt"] = "0909123456", ["danh_gia"] = 5 }),
            new("pur_nha_cung_cap", new() { ["ma_ncc"] = "NCC-002", ["ten_ncc"] = "Nội thất Văn phòng Hòa Phát", ["nhom_ncc"] = "VPP_NOITHAT", ["nguoi_lien_he"] = "Lê Thị Lan", ["email_ncc"] = "sales@hoaphatcorp.vn", ["so_dt"] = "0913456789", ["danh_gia"] = 4 }),
            new("pur_nha_cung_cap", new() { ["ma_ncc"] = "NCC-003", ["ten_ncc"] = "CloudTech Infrastructure JSC", ["nhom_ncc"] = "DICH_VU", ["nguoi_lien_he"] = "Hoàng Gia Huy", ["email_ncc"] = "huy.hg@cloudtech.vn", ["so_dt"] = "0977889900", ["danh_gia"] = 5 }),
            new("pur_don_mua_hang", new()
            {
                ["ma_po"] = "PO-2026-0001",
                ["tieu_de"] = "Mua 5 màn hình Dell UltraSharp 27 inch cho team Dev",
                ["ma_ncc"] = "NCC-001",
                ["nguoi_yeu_cau"] = "NV001",
                ["ngay_can_hang"] = "2026-11-22",
                ["chi_tiet_hang"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_hang"] = "Màn hình Dell U2723QE 4K", ["so_luong"] = 5, ["don_gia"] = 13500000m, ["thanh_tien"] = 67500000m },
                    new() { ["ten_hang"] = "Cáp USB-C Thunderbolt", ["so_luong"] = 5, ["don_gia"] = 650000m, ["thanh_tien"] = 3250000m }
                },
                ["tien_do_po"] = "DA_DAT_HANG",
                ["trang_thai_duyet"] = "DA_DUYET"
            }),
            new("pur_don_mua_hang", new()
            {
                ["ma_po"] = "PO-2026-0002",
                ["tieu_de"] = "Trang bị 10 ghế công thái học phòng họp mới",
                ["ma_ncc"] = "NCC-002",
                ["nguoi_yeu_cau"] = "NV002",
                ["ngay_can_hang"] = "2026-11-29",
                ["chi_tiet_hang"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_hang"] = "Ghế Ergonomic Pro Mesh", ["so_luong"] = 10, ["don_gia"] = 4200000m, ["thanh_tien"] = 42000000m }
                },
                ["tien_do_po"] = "MOI_TAO",
                ["trang_thai_duyet"] = "CHO_DUYET"
            })
        ],
        Widgets:
        [
            new("pur_don_mua_hang", "Tổng Giá trị Đơn mua (PO)", "KPI_SUM", "tong_gia_tri", null, null, null, 1, "#0d9488", 1),
            new("pur_don_mua_hang", "PO Chờ phê duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#ea8600", 2),
            new("pur_nha_cung_cap", "Số Nhà cung cấp", "KPI_COUNT", null, null, null, null, 1, "#1a73e8", 3),
            new("pur_don_mua_hang", "Tổng số Đơn mua hàng", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 4),
            new("pur_don_mua_hang", "Đơn mua theo Tiến độ", "DONUT_CHART", null, "tien_do_po", null, null, 2, "#0d9488", 5),
            new("pur_nha_cung_cap", "NCC theo Nhóm hàng", "BAR_CHART", null, "nhom_ncc", null, null, 2, "#1a73e8", 6)
        ]);

    private static AppTemplateDefinition BuildWmsTemplate() => new(
        TemplateKey: "WMS",
        Code: "WMS",
        Name: "Quản lý Kho & Nhập Xuất Vật tư",
        Description: "Theo dõi danh mục hàng hóa SKU, cảnh báo tồn kho tối thiểu bằng công thức và quản lý phiếu nhập xuất kho.",
        Department: "Kho vận & Chuỗi cung ứng",
        Category: "Kho vận & Vận hành",
        Icon: "Inventory",
        Color: "#1e8e3e",
        BgColor: "#e6f4ea",
        OrderIndex: 13,
        Highlights:
        [
            "Tự tính giá trị tồn kho (ton_kho * don_gia) & cảnh báo định mức",
            "Phiếu nhập/xuất kho dạng Master-Detail (SUB_TABLE) có duyệt",
            "Tem mã vạch SKU SVG tích hợp sẵn khi xem/in phiếu"
        ],
        Features: ["Phê duyệt", "Sub-table", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_NHOM_HANG_KHO", "Nhóm Hàng hóa Kho", "Phân loại vật tư, hàng hóa lưu kho",
            [
                new("LINH_KIEN", "Linh kiện & Phụ kiện IT", "#1a73e8", 1),
                new("VPP", "Văn phòng phẩm & Tiêu hao", "#ea8600", 2),
                new("THANH_PHAM", "Thành phẩm kinh doanh", "#1e8e3e", 3),
                new("BAO_BI", "Bao bì & Đóng gói", "#7b1fa2", 4)
            ]),
            new("DM_DON_VI_TINH", "Đơn vị tính", "Đơn vị đo lường hàng hóa",
            [
                new("CAI", "Cái / Chiếc", "#1a73e8", 1),
                new("HOP", "Hộp / Thùng", "#1e8e3e", 2),
                new("BO", "Bộ", "#7b1fa2", 3),
                new("RAM", "Ram / Cuộn", "#ea8600", 4)
            ]),
            new("DM_LOAI_PHIEU_KHO", "Loại Phiếu Kho", "Nghiệp vụ nhập xuất điều chuyển",
            [
                new("NHAP_KHO", "Phiếu Nhập kho", "#1e8e3e", 1),
                new("XUAT_KHO", "Phiếu Xuất kho", "#ea8600", 2),
                new("KIEM_KE", "Phiếu Kiểm kê / Điều chỉnh", "#1a73e8", 3)
            ])
        ],
        Modules:
        [
            new("TONKHO", "Danh mục & Tồn kho", "Inventory", "Theo dõi số lượng tồn và giá trị hàng hóa",
            [
                new("wms_hang_hoa", "Danh mục Hàng hóa Tồn kho", "Tự động tính giá trị tồn và cảnh báo khi chạm ngưỡng tối thiểu", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_sku", ColumnName = "Mã SKU", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "ten_hang", ColumnName = "Tên hàng hóa / Vật tư", DataType = "STRING", IsRequired = true, Width = 230, OrderIndex = 2 },
                    new() { ColumnCode = "nhom_hang", ColumnName = "Nhóm hàng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_NHOM_HANG_KHO", Width = 185, OrderIndex = 3 },
                    new() { ColumnCode = "dvt", ColumnName = "ĐVT", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_DON_VI_TINH", Width = 115, OrderIndex = 4 },
                    new() { ColumnCode = "ton_kho", ColumnName = "Tồn hiện tại", DataType = "NUMBER", IsRequired = true, DefaultValue = "0", Width = 120, OrderIndex = 5 },
                    new() { ColumnCode = "ton_toi_thieu", ColumnName = "Định mức tối thiểu", DataType = "NUMBER", DefaultValue = "5", Width = 135, OrderIndex = 6 },
                    new() { ColumnCode = "don_gia", ColumnName = "Đơn giá vốn", DataType = "CURRENCY", IsRequired = true, Width = 150, OrderIndex = 7 },
                    new() { ColumnCode = "gia_tri_ton", ColumnName = "Giá trị tồn (Auto)", DataType = "CURRENCY", Formula = "ton_kho * don_gia", Width = 170, OrderIndex = 8 },
                    new() { ColumnCode = "canh_bao", ColumnName = "Trạng thái tồn (Auto)", DataType = "STRING", Formula = "IF(ton_kho <= ton_toi_thieu, 'CAN_NHAP_THEM', 'AN_TOAN')", Width = 165, OrderIndex = 9 }
                ])
            ]),
            new("PHIEUKHO", "Phiếu Nhập / Xuất kho", "SwapHoriz", "Chứng từ giao nhận vật tư kho",
            [
                new("wms_phieu_kho", "Chứng từ Nhập / Xuất Kho", "Lập phiếu nhập xuất nhiều mặt hàng kèm quy trình duyệt thủ kho", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã Phiếu kho", DataType = "AUTO_NUMBER", DefaultValue = "PK-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "loai_phieu", ColumnName = "Loại chứng từ", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_PHIEU_KHO", Width = 175, OrderIndex = 2 },
                    new() { ColumnCode = "ngay_chung_tu", ColumnName = "Ngày chứng từ", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 3 },
                    HrmLookupCol("nguoi_lap", "Nhân viên lập phiếu", 4),
                    new() { ColumnCode = "chi_tiet_vt", ColumnName = "Chi tiết vật tư", DataType = "SUB_TABLE", DefaultValue = "ma_sku,ten_hang,so_luong,thanh_tien", ShowInTable = false, Width = 260, OrderIndex = 5 },
                    new() { ColumnCode = "tong_gia_tri", ColumnName = "Tổng giá trị phiếu", DataType = "CURRENCY", Formula = "SUM(chi_tiet_vt.thanh_tien)", Width = 170, OrderIndex = 6 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 7 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("wms_hang_hoa", new() { ["ma_sku"] = "SKU-SSD-1T", ["ten_hang"] = "Ổ cứng NVMe SSD Samsung 1TB", ["nhom_hang"] = "LINH_KIEN", ["dvt"] = "CAI", ["ton_kho"] = 18, ["ton_toi_thieu"] = 5, ["don_gia"] = 2450000m }),
            new("wms_hang_hoa", new() { ["ma_sku"] = "SKU-RAM-32G", ["ten_hang"] = "Thanh RAM DDR5 32GB ECC", ["nhom_hang"] = "LINH_KIEN", ["dvt"] = "CAI", ["ton_kho"] = 4, ["ton_toi_thieu"] = 6, ["don_gia"] = 3200000m }),
            new("wms_hang_hoa", new() { ["ma_sku"] = "SKU-A4-DL", ["ten_hang"] = "Giấy in Double A A4 80gsm", ["nhom_hang"] = "VPP", ["dvt"] = "RAM", ["ton_kho"] = 45, ["ton_toi_thieu"] = 15, ["don_gia"] = 85000m }),
            new("wms_hang_hoa", new() { ["ma_sku"] = "SKU-BOX-L", ["ten_hang"] = "Thùng carton đóng gói 5 lớp", ["nhom_hang"] = "BAO_BI", ["dvt"] = "HOP", ["ton_kho"] = 12, ["ton_toi_thieu"] = 20, ["don_gia"] = 22000m }),
            new("wms_phieu_kho", new()
            {
                ["ma_phieu"] = "PK-2026-0001",
                ["loai_phieu"] = "NHAP_KHO",
                ["ngay_chung_tu"] = "2026-11-12",
                ["nguoi_lap"] = "NV001",
                ["chi_tiet_vt"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ma_sku"] = "SKU-SSD-1T", ["ten_hang"] = "Ổ cứng NVMe SSD Samsung 1TB", ["so_luong"] = 10, ["thanh_tien"] = 24500000m }
                },
                ["trang_thai_duyet"] = "DA_DUYET"
            }),
            new("wms_phieu_kho", new()
            {
                ["ma_phieu"] = "PK-2026-0002",
                ["loai_phieu"] = "XUAT_KHO",
                ["ngay_chung_tu"] = "2026-11-15",
                ["nguoi_lap"] = "NV003",
                ["chi_tiet_vt"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ma_sku"] = "SKU-A4-DL", ["ten_hang"] = "Giấy in Double A A4 80gsm", ["so_luong"] = 10, ["thanh_tien"] = 850000m }
                },
                ["trang_thai_duyet"] = "CHO_DUYET"
            })
        ],
        Widgets:
        [
            new("wms_hang_hoa", "Tổng Giá trị Tồn kho", "KPI_SUM", "gia_tri_ton", null, null, null, 1, "#1e8e3e", 1),
            new("wms_hang_hoa", "Tổng Mã hàng (SKU)", "KPI_COUNT", null, null, null, null, 1, "#1a73e8", 2),
            new("wms_hang_hoa", "Mặt hàng Cần nhập thêm", "KPI_COUNT", null, null, "canh_bao", "CAN_NHAP_THEM", 1, "#d93025", 3),
            new("wms_phieu_kho", "Phiếu Kho Chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#ea8600", 4),
            new("wms_hang_hoa", "Hàng hóa theo Nhóm", "DONUT_CHART", null, "nhom_hang", null, null, 2, "#1e8e3e", 5),
            new("wms_phieu_kho", "Phiếu Kho theo Loại chứng từ", "BAR_CHART", null, "loai_phieu", null, null, 2, "#1a73e8", 6)
        ]);

    private static AppTemplateDefinition BuildProjectTemplate() => new(
        TemplateKey: "PROJECT",
        Code: "PROJECT",
        Name: "Quản lý Dự án & Công việc (PM)",
        Description: "Điều phối danh mục dự án công ty, phân rã công việc dạng Kanban kéo-thả, hạn hoàn thành và ngân sách.",
        Department: "Điều hành & Dự án",
        Category: "Dự án & Kỹ thuật",
        Icon: "Engineering",
        Color: "#4f46e5",
        BgColor: "#eef2ff",
        OrderIndex: 14,
        Highlights:
        [
            "Tự tính thời lượng dự án bằng công thức DATEDIF(ngay_bat_dau, han_ban_giao)",
            "Bảng Task Kanban trực quan theo trạng thái Cần làm → Đang làm → Hoàn thành",
            "Gán PM & Người thực hiện liên kết trực tiếp từ HRM"
        ],
        Features: ["Kanban", "Lịch", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_TRANG_THAI_DU_AN", "Trạng thái Dự án", "Vòng đời dự án doanh nghiệp",
            [
                new("CHUAN_BI", "Lập kế hoạch", "#5f6368", 1),
                new("DANG_CHAY", "Đang triển khai", "#1a73e8", 2),
                new("TAM_DUNG", "Tạm hoãn", "#ea8600", 3),
                new("HOAN_THANH", "Đã nghiệm thu", "#1e8e3e", 4)
            ]),
            new("DM_TRANG_THAI_TASK", "Trạng thái Công việc (Kanban)", "Bảng trạng thái thực thi task",
            [
                new("TODO", "Cần thực hiện", "#5f6368", 1),
                new("IN_PROGRESS", "Đang thực hiện", "#1a73e8", 2),
                new("REVIEW", "Chờ kiểm tra / Review", "#ea8600", 3),
                new("DONE", "Hoàn thành", "#1e8e3e", 4)
            ])
        ],
        Modules:
        [
            new("DUAN", "Danh mục Dự án", "Engineering", "Thông tin tổng quan, ngân sách và tiến độ dự án",
            [
                new("prj_du_an", "Danh sách Dự án", "Quản lý tiến độ tổng thể, PM phụ trách và ngân sách", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_du_an", ColumnName = "Mã Dự án", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 130, OrderIndex = 1 },
                    new() { ColumnCode = "ten_du_an", ColumnName = "Tên Dự án", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    HrmLookupCol("quan_ly_du_an", "Quản lý dự án (PM)", 3),
                    new() { ColumnCode = "ngay_bat_dau", ColumnName = "Ngày bắt đầu", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 4 },
                    new() { ColumnCode = "han_ban_giao", ColumnName = "Hạn bàn giao", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 5 },
                    new() { ColumnCode = "thoi_luong_ngay", ColumnName = "Thời lượng (ngày)", DataType = "NUMBER", Formula = "DATEDIF(ngay_bat_dau, han_ban_giao)", Width = 145, OrderIndex = 6 },
                    new() { ColumnCode = "ngan_sach", ColumnName = "Ngân sách dự kiến", DataType = "CURRENCY", Width = 165, OrderIndex = 7 },
                    new() { ColumnCode = "trang_thai", ColumnName = "Trạng thái", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DU_AN", DefaultValue = "DANG_CHAY", Width = 165, OrderIndex = 8 }
                ])
            ]),
            new("TASK", "Công việc & Sprint (Kanban)", "Assignment", "Phân công công việc chi tiết theo từng dự án",
            [
                new("prj_cong_viec", "Bảng Công việc Dự án", "Kéo-thả công việc trên Kanban hoặc xem theo Lịch deadline", "kanban", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_task", ColumnName = "Mã Task", DataType = "AUTO_NUMBER", DefaultValue = "TSK-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "ten_cong_viec", ColumnName = "Tên công việc", DataType = "STRING", IsRequired = true, Width = 240, OrderIndex = 2 },
                    new() { ColumnCode = "ma_du_an", ColumnName = "Thuộc Dự án", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "prj_du_an", RefValueField = "ma_du_an", RefDisplayField = "ten_du_an", ShowInParentForm = true, Width = 210, OrderIndex = 3 },
                    HrmLookupCol("nguoi_thuc_hien", "Người thực hiện", 4),
                    new() { ColumnCode = "trang_thai", ColumnName = "Trạng thái", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_TASK", DefaultValue = "TODO", Width = 175, OrderIndex = 5 },
                    new() { ColumnCode = "uu_tien", ColumnName = "Ưu tiên", DataType = "SELECT", RefCategoryCode = "DM_UU_TIEN", DefaultValue = "MEDIUM", Width = 135, OrderIndex = 6 },
                    new() { ColumnCode = "han_hoan_thanh", ColumnName = "Deadline", DataType = "DATE", Width = 135, OrderIndex = 7 },
                    new() { ColumnCode = "gio_uoc_tinh", ColumnName = "Giờ công (h)", DataType = "NUMBER", DefaultValue = "8", Width = 120, OrderIndex = 8 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("prj_du_an", new() { ["ma_du_an"] = "PRJ-ERP-01", ["ten_du_an"] = "Nâng cấp Cổng Dữ liệu Doanh nghiệp 2026", ["quan_ly_du_an"] = "NV001", ["ngay_bat_dau"] = "2026-10-01", ["han_ban_giao"] = "2026-12-15", ["ngan_sach"] = 350000000m, ["trang_thai"] = "DANG_CHAY" }),
            new("prj_du_an", new() { ["ma_du_an"] = "PRJ-ISO-02", ["ten_du_an"] = "Chuẩn hóa Quy trình ISO 27001", ["quan_ly_du_an"] = "NV002", ["ngay_bat_dau"] = "2026-11-01", ["han_ban_giao"] = "2026-12-30", ["ngan_sach"] = 120000000m, ["trang_thai"] = "DANG_CHAY" }),
            new("prj_cong_viec", new() { ["ma_task"] = "TSK-2026-0001", ["ten_cong_viec"] = "Thiết kế kiến trúc Multi-App & phân quyền RBAC", ["ma_du_an"] = "PRJ-ERP-01", ["nguoi_thuc_hien"] = "NV001", ["trang_thai"] = "DONE", ["uu_tien"] = "HIGH", ["han_hoan_thanh"] = "2026-11-10", ["gio_uoc_tinh"] = 24 }),
            new("prj_cong_viec", new() { ["ma_task"] = "TSK-2026-0002", ["ten_cong_viec"] = "Tích hợp Webhook thông báo Slack & Email", ["ma_du_an"] = "PRJ-ERP-01", ["nguoi_thuc_hien"] = "NV001", ["trang_thai"] = "IN_PROGRESS", ["uu_tien"] = "HIGH", ["han_hoan_thanh"] = "2026-11-22", ["gio_uoc_tinh"] = 16 }),
            new("prj_cong_viec", new() { ["ma_task"] = "TSK-2026-0003", ["ten_cong_viec"] = "Rà soát chính sách bảo mật tài khoản nội bộ", ["ma_du_an"] = "PRJ-ISO-02", ["nguoi_thuc_hien"] = "NV002", ["trang_thai"] = "REVIEW", ["uu_tien"] = "MEDIUM", ["han_hoan_thanh"] = "2026-11-25", ["gio_uoc_tinh"] = 12 }),
            new("prj_cong_viec", new() { ["ma_task"] = "TSK-2026-0004", ["ten_cong_viec"] = "Đào tạo sử dụng hệ thống cho phòng Kế toán", ["ma_du_an"] = "PRJ-ERP-01", ["nguoi_thuc_hien"] = "NV003", ["trang_thai"] = "TODO", ["uu_tien"] = "LOW", ["han_hoan_thanh"] = "2026-11-30", ["gio_uoc_tinh"] = 8 })
        ],
        Widgets:
        [
            new("prj_du_an", "Tổng Ngân sách Dự án", "KPI_SUM", "ngan_sach", null, null, null, 1, "#4f46e5", 1),
            new("prj_du_an", "Dự án Đang triển khai", "KPI_COUNT", null, null, "trang_thai", "DANG_CHAY", 1, "#1a73e8", 2),
            new("prj_cong_viec", "Công việc Chưa xong (Todo/InProgress)", "KPI_COUNT", null, null, "trang_thai", "IN_PROGRESS", 1, "#ea8600", 3),
            new("prj_cong_viec", "Tổng Giờ công Ước tính", "KPI_SUM", "gio_uoc_tinh", null, null, null, 1, "#1e8e3e", 4),
            new("prj_cong_viec", "Công việc theo Trạng thái Kanban", "DONUT_CHART", null, "trang_thai", null, null, 2, "#4f46e5", 5),
            new("prj_cong_viec", "Phân bổ Mức ưu tiên Task", "BAR_CHART", null, "uu_tien", null, null, 2, "#ea8600", 6)
        ]);

    private static AppTemplateDefinition BuildRecruitTemplate() => new(
        TemplateKey: "RECRUIT",
        Code: "RECRUIT",
        Name: "Tuyển dụng & Hồ sơ Ứng viên (ATS)",
        Description: "Quản lý yêu cầu tuyển dụng theo phòng ban, nhận CV qua Public Form và theo dõi vòng phỏng vấn trên Kanban.",
        Department: "Phòng Nhân sự (HR)",
        Category: "Nhân sự & Hành chính",
        Icon: "Groups",
        Color: "#9333ea",
        BgColor: "#f3e8ff",
        OrderIndex: 15,
        Highlights:
        [
            "Bật sẵn Public Form cho ứng viên tự nộp hồ sơ trực tuyến",
            "Pipeline phỏng vấn Kanban: Nộp CV → Sàng lọc → Phỏng vấn → Offer → Nhận việc",
            "Phê duyệt yêu cầu định biên tuyển dụng giữa Trưởng bộ phận & HR"
        ],
        Features: ["Kanban", "Phê duyệt", "Form công khai", "Liên kết HRM"],
        Categories:
        [
            new("DM_VONG_TUYEN_DUNG", "Vòng Tuyển dụng Ứng viên", "Các bước sàng lọc và phỏng vấn",
            [
                new("NOP_CV", "Mới nộp CV", "#5f6368", 1),
                new("SANG_LOC", "Đã sàng lọc / Test", "#1a73e8", 2),
                new("PHONG_VAN", "Phỏng vấn chuyên môn", "#7b1fa2", 3),
                new("OFFER", "Đã gửi Offer", "#ea8600", 4),
                new("NHAN_VIEC", "Nhận việc (Onboard)", "#1e8e3e", 5),
                new("LOAI", "Không phù hợp", "#d93025", 6)
            ]),
            new("DM_NGUON_UNG_VIEN", "Nguồn Hồ sơ Ứng viên", "Kênh thu hút nhân tài",
            [
                new("LINKEDIN", "LinkedIn", "#0284c7", 1),
                new("TOPCV", "TopCV / VietnamWorks", "#1e8e3e", 2),
                new("NOI_BO", "Giới thiệu nội bộ", "#7b1fa2", 3),
                new("WEBSITE", "Website Tuyển dụng", "#ea8600", 4)
            ])
        ],
        Modules:
        [
            new("YEUCAU_TD", "Kế hoạch Tuyển dụng", "FactCheck", "Đề xuất định biên nhân sự từ các phòng ban",
            [
                new("rec_dot_tuyen_dung", "Đợt Tuyển dụng", "Quản lý chỉ tiêu, mức lương dự kiến và phê duyệt mở job", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_dot", ColumnName = "Mã Đợt tuyển", DataType = "AUTO_NUMBER", DefaultValue = "TD-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "vi_tri", ColumnName = "Vị trí cần tuyển", DataType = "STRING", IsRequired = true, Width = 225, OrderIndex = 2 },
                    new() { ColumnCode = "phong_ban", ColumnName = "Phòng ban", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_PHONG_BAN", Width = 190, OrderIndex = 3 },
                    new() { ColumnCode = "so_luong", ColumnName = "Số lượng", DataType = "NUMBER", IsRequired = true, DefaultValue = "1", Width = 110, OrderIndex = 4 },
                    new() { ColumnCode = "muc_luong_du_kien", ColumnName = "Ngân sách lương/tháng", DataType = "CURRENCY", Width = 175, OrderIndex = 5 },
                    new() { ColumnCode = "han_tuyen", ColumnName = "Hạn đóng job", DataType = "DATE", Width = 135, OrderIndex = 6 },
                    HrmLookupCol("hr_phu_trach", "HR Phụ trách", 7),
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 8 }
                ])
            ]),
            new("UNGVIEN", "Hồ sơ Ứng viên (ATS)", "Groups", "Theo dõi ứng viên và lịch phỏng vấn",
            [
                new("rec_ung_vien", "Pipeline Ứng viên", "Thu thập CV qua form công khai và kéo-thả qua từng vòng phỏng vấn", "kanban", false, "trang_thai_duyet", true,
                [
                    new() { ColumnCode = "ma_uv", ColumnName = "Mã Ứng viên", DataType = "AUTO_NUMBER", DefaultValue = "UV-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "ho_ten", ColumnName = "Họ và Tên ứng viên", DataType = "STRING", IsRequired = true, Width = 200, OrderIndex = 2 },
                    new() { ColumnCode = "email", ColumnName = "Email", DataType = "EMAIL", IsRequired = true, Width = 200, OrderIndex = 3 },
                    new() { ColumnCode = "so_dt", ColumnName = "Số điện thoại", DataType = "TEL", Width = 135, OrderIndex = 4 },
                    new() { ColumnCode = "dot_tuyen_dung", ColumnName = "Vị trí ứng tuyển", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "rec_dot_tuyen_dung", RefValueField = "ma_dot", RefDisplayField = "vi_tri", ShowInParentForm = true, Width = 210, OrderIndex = 5 },
                    new() { ColumnCode = "vong_tuyen", ColumnName = "Vòng tuyển dụng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_VONG_TUYEN_DUNG", DefaultValue = "NOP_CV", Width = 180, OrderIndex = 6 },
                    new() { ColumnCode = "nguon_cv", ColumnName = "Nguồn hồ sơ", DataType = "SELECT", RefCategoryCode = "DM_NGUON_UNG_VIEN", Width = 165, OrderIndex = 7 },
                    new() { ColumnCode = "ngay_pv", ColumnName = "Ngày phỏng vấn", DataType = "DATE", Width = 140, OrderIndex = 8 },
                    new() { ColumnCode = "luong_mong_muon", ColumnName = "Lương đề xuất", DataType = "CURRENCY", Width = 155, OrderIndex = 9 },
                    new() { ColumnCode = "nhan_xet", ColumnName = "Đánh giá phỏng vấn", DataType = "TEXTAREA", Width = 230, OrderIndex = 10 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("rec_dot_tuyen_dung", new() { ["ma_dot"] = "TD-2026-0001", ["vi_tri"] = "Senior Fullstack Engineer (.NET / Next.js)", ["phong_ban"] = "TECH", ["so_luong"] = 2, ["muc_luong_du_kien"] = 38000000m, ["han_tuyen"] = "2026-12-15", ["hr_phu_trach"] = "NV002", ["trang_thai_duyet"] = "DA_DUYET" }),
            new("rec_dot_tuyen_dung", new() { ["ma_dot"] = "TD-2026-0002", ["vi_tri"] = "Chuyên viên Kinh doanh B2B", ["phong_ban"] = "SALES", ["so_luong"] = 3, ["muc_luong_du_kien"] = 22000000m, ["han_tuyen"] = "2026-12-20", ["hr_phu_trach"] = "NV002", ["trang_thai_duyet"] = "CHO_DUYET" }),
            new("rec_ung_vien", new() { ["ma_uv"] = "UV-2026-0001", ["ho_ten"] = "Đặng Quang Vinh", ["email"] = "vinh.dq@gmail.com", ["so_dt"] = "0905112233", ["dot_tuyen_dung"] = "TD-2026-0001", ["vong_tuyen"] = "OFFER", ["nguon_cv"] = "LINKEDIN", ["ngay_pv"] = "2026-11-12", ["luong_mong_muon"] = 36000000m, ["nhan_xet"] = "Nắm vững kiến trúc PostgreSQL JSONB và React." }),
            new("rec_ung_vien", new() { ["ma_uv"] = "UV-2026-0002", ["ho_ten"] = "Nguyễn Ngọc Ánh", ["email"] = "anh.nn@outlook.com", ["so_dt"] = "0912998877", ["dot_tuyen_dung"] = "TD-2026-0001", ["vong_tuyen"] = "PHONG_VAN", ["nguon_cv"] = "TOPCV", ["ngay_pv"] = "2026-11-18", ["luong_mong_muon"] = 32000000m, ["nhan_xet"] = "Điểm bài test thuật toán 9/10." }),
            new("rec_ung_vien", new() { ["ma_uv"] = "UV-2026-0003", ["ho_ten"] = "Bùi Thanh Tùng", ["email"] = "tung.bt@gmail.com", ["so_dt"] = "0987334455", ["dot_tuyen_dung"] = "TD-2026-0002", ["vong_tuyen"] = "SANG_LOC", ["nguon_cv"] = "NOI_BO", ["ngay_pv"] = "2026-11-20", ["luong_mong_muon"] = 20000000m, ["nhan_xet"] = "Có 3 năm kinh nghiệm bán phần mềm SaaS." }),
            new("rec_ung_vien", new() { ["ma_uv"] = "UV-2026-0004", ["ho_ten"] = "Phạm Thảo Vy", ["email"] = "vy.pham@gmail.com", ["so_dt"] = "0934556677", ["dot_tuyen_dung"] = "TD-2026-0002", ["vong_tuyen"] = "NOP_CV", ["nguon_cv"] = "WEBSITE", ["luong_mong_muon"] = 18000000m, ["nhan_xet"] = "CV mới gửi qua form tuyển dụng." })
        ],
        Widgets:
        [
            new("rec_ung_vien", "Tổng Hồ sơ Ứng viên", "KPI_COUNT", null, null, null, null, 1, "#9333ea", 1),
            new("rec_ung_vien", "Ứng viên đang Offer", "KPI_COUNT", null, null, "vong_tuyen", "OFFER", 1, "#ea8600", 2),
            new("rec_dot_tuyen_dung", "Đợt tuyển Đã duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "DA_DUYET", 1, "#1e8e3e", 3),
            new("rec_dot_tuyen_dung", "Đề xuất tuyển Chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#d93025", 4),
            new("rec_ung_vien", "Ứng viên theo Vòng Tuyển dụng", "DONUT_CHART", null, "vong_tuyen", null, null, 2, "#9333ea", 5),
            new("rec_ung_vien", "Hiệu quả theo Nguồn CV", "BAR_CHART", null, "nguon_cv", null, null, 2, "#0284c7", 6)
        ]);

    private static AppTemplateDefinition BuildContractTemplate() => new(
        TemplateKey: "CONTRACT",
        Code: "CONTRACT",
        Name: "Quản lý Hợp đồng & Pháp lý",
        Description: "Lưu trữ hợp đồng kinh tế, hợp đồng dịch vụ & NDA, tự tính thời hạn hiệu lực và trình duyệt ký kết.",
        Department: "Pháp chế & Kinh doanh",
        Category: "Pháp chế & Quản trị",
        Icon: "FactCheck",
        Color: "#7b1fa2",
        BgColor: "#f3e5f5",
        OrderIndex: 16,
        Highlights:
        [
            "Tự tính số ngày hiệu lực DATEDIF(ngay_ky, ngay_het_han)",
            "Theo dõi trạng thái ký kết, gia hạn & quy trình phê duyệt pháp chế",
            "Đính kèm bản scan PDF hợp đồng & in phiếu tóm tắt A4"
        ],
        Features: ["Phê duyệt", "Lịch", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_LOAI_HOP_DONG", "Loại Hợp đồng Doanh nghiệp", "Phân loại văn bản pháp lý",
            [
                new("BAN_HANG", "Hợp đồng Bán hàng / Dịch vụ", "#1a73e8", 1),
                new("MUA_SAM", "Hợp đồng Mua sắm / NCC", "#0d9488", 2),
                new("THUE_VAN_PHONG", "Hợp đồng Thuê mặt bằng", "#ea8600", 3),
                new("NDA_BAO_MAT", "Thỏa thuận Bảo mật (NDA)", "#7b1fa2", 4)
            ]),
            new("DM_TRANG_THAI_HD", "Tình trạng Hợp đồng", "Vòng đời thực thi hợp đồng",
            [
                new("SOAN_THAO", "Đang soạn thảo", "#5f6368", 1),
                new("CHO_KY", "Chờ ký kết", "#ea8600", 2),
                new("DANG_HIEU_LUC", "Đang hiệu lực", "#1e8e3e", 3),
                new("HET_HAN", "Đã hết hạn / Thanh lý", "#d93025", 4)
            ])
        ],
        Modules:
        [
            new("HOPDONG", "Hồ sơ Hợp đồng", "FactCheck", "Quản lý hợp đồng kinh tế và theo dõi gia hạn",
            [
                new("ctr_hop_dong", "Danh mục Hợp đồng Kinh tế", "Theo dõi thời hạn hiệu lực, giá trị và trạng thái pháp lý", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "so_hd", ColumnName = "Số Hợp đồng", DataType = "AUTO_NUMBER", DefaultValue = "HD-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "ten_hop_dong", ColumnName = "Tên Hợp đồng", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    new() { ColumnCode = "doi_tac", ColumnName = "Đối tác / Khách hàng", DataType = "STRING", IsRequired = true, Width = 215, OrderIndex = 3 },
                    new() { ColumnCode = "loai_hd", ColumnName = "Loại hợp đồng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_HOP_DONG", Width = 190, OrderIndex = 4 },
                    HrmLookupCol("nguoi_phu_trach", "Người phụ trách (HRM)", 5),
                    new() { ColumnCode = "ngay_ky", ColumnName = "Ngày ký", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 6 },
                    new() { ColumnCode = "ngay_het_han", ColumnName = "Ngày hết hạn", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 7 },
                    new() { ColumnCode = "so_ngay_hieu_luc", ColumnName = "Hiệu lực (ngày)", DataType = "NUMBER", Formula = "DATEDIF(ngay_ky, ngay_het_han)", Width = 140, OrderIndex = 8 },
                    new() { ColumnCode = "gia_tri", ColumnName = "Giá trị HĐ (VNĐ)", DataType = "CURRENCY", IsRequired = true, Width = 165, OrderIndex = 9 },
                    new() { ColumnCode = "tinh_trang_hd", ColumnName = "Tình trạng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_HD", DefaultValue = "SOAN_THAO", Width = 160, OrderIndex = 10 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Phê duyệt Pháp chế", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 11 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("ctr_hop_dong", new() { ["so_hd"] = "HD-2026-0001", ["ten_hop_dong"] = "Hợp đồng Cung cấp Nền tảng SmartApps Enterprise", ["doi_tac"] = "Công ty CP Công nghệ Sao Việt", ["loai_hd"] = "BAN_HANG", ["nguoi_phu_trach"] = "NV001", ["ngay_ky"] = "2026-01-15", ["ngay_het_han"] = "2027-01-15", ["gia_tri"] = 480000000m, ["tinh_trang_hd"] = "DANG_HIEU_LUC", ["trang_thai_duyet"] = "DA_DUYET" }),
            new("ctr_hop_dong", new() { ["so_hd"] = "HD-2026-0002", ["ten_hop_dong"] = "Hợp đồng Thuê Văn phòng Tầng 12 Tower A", ["doi_tac"] = "Công ty Quản lý BĐS Capital Land", ["loai_hd"] = "THUE_VAN_PHONG", ["nguoi_phu_trach"] = "NV002", ["ngay_ky"] = "2026-03-01", ["ngay_het_han"] = "2028-03-01", ["gia_tri"] = 1440000000m, ["tinh_trang_hd"] = "DANG_HIEU_LUC", ["trang_thai_duyet"] = "DA_DUYET" }),
            new("ctr_hop_dong", new() { ["so_hd"] = "HD-2026-0003", ["ten_hop_dong"] = "Hợp đồng Bảo trì Hạ tầng Cloud 2026-2027", ["doi_tac"] = "CloudTech Infrastructure JSC", ["loai_hd"] = "MUA_SAM", ["nguoi_phu_trach"] = "NV001", ["ngay_ky"] = "2026-11-10", ["ngay_het_han"] = "2027-11-10", ["gia_tri"] = 220000000m, ["tinh_trang_hd"] = "CHO_KY", ["trang_thai_duyet"] = "CHO_DUYET" })
        ],
        Widgets:
        [
            new("ctr_hop_dong", "Tổng Giá trị Hợp đồng", "KPI_SUM", "gia_tri", null, null, null, 1, "#7b1fa2", 1),
            new("ctr_hop_dong", "Hợp đồng Đang hiệu lực", "KPI_COUNT", null, null, "tinh_trang_hd", "DANG_HIEU_LUC", 1, "#1e8e3e", 2),
            new("ctr_hop_dong", "Chờ duyệt Pháp chế", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#ea8600", 3),
            new("ctr_hop_dong", "Giá trị HĐ Trung bình", "KPI_AVG", "gia_tri", null, null, null, 1, "#1a73e8", 4),
            new("ctr_hop_dong", "Phân bổ theo Loại Hợp đồng", "DONUT_CHART", null, "loai_hd", null, null, 2, "#7b1fa2", 5),
            new("ctr_hop_dong", "Tình trạng Ký kết & Hiệu lực", "BAR_CHART", null, "tinh_trang_hd", null, null, 2, "#1e8e3e", 6)
        ]);

    private static AppTemplateDefinition BuildHelpdeskTemplate() => new(
        TemplateKey: "HELPDESK",
        Code: "HELPDESK",
        Name: "Chăm sóc Khách hàng & Ticket CSKH",
        Description: "Cổng tiếp nhận khiếu nại, bảo hành và hỗ trợ khách hàng đa kênh kèm Kanban xử lý SLA & khảo sát hài lòng.",
        Department: "Dịch vụ Khách hàng",
        Category: "Kinh doanh & CSKH",
        Icon: "SupportAgent",
        Color: "#0369a1",
        BgColor: "#e0f2fe",
        OrderIndex: 17,
        Highlights:
        [
            "Form công khai (/forms/...) để khách hàng gửi ticket không cần đăng nhập",
            "Theo dõi tiến trình xử lý Kanban & hạn SLA trên Lịch",
            "Ghi nhận điểm CSAT (1-5 sao) sau khi đóng yêu cầu"
        ],
        Features: ["Kanban", "Form công khai", "Lịch", "Liên kết HRM"],
        Categories:
        [
            new("DM_TRANG_THAI_TICKET_CS", "Trạng thái Ticket CSKH", "Luồng xử lý yêu cầu khách hàng",
            [
                new("TIEP_NHAN", "Mới tiếp nhận", "#5f6368", 1),
                new("DANG_XU_LY", "Đang xử lý", "#1a73e8", 2),
                new("CHO_KHACH", "Chờ phản hồi khách", "#ea8600", 3),
                new("DA_GIAI_QUYET", "Đã giải quyết", "#1e8e3e", 4)
            ]),
            new("DM_KENH_TIEP_NHAN", "Kênh Tiếp nhận CSKH", "Nguồn gửi yêu cầu hỗ trợ",
            [
                new("HOTLINE", "Tổng đài Hotline", "#d93025", 1),
                new("EMAIL", "Email Hỗ trợ", "#1a73e8", 2),
                new("ZALO_OA", "Zalo OA / Chat", "#0284c7", 3),
                new("WEB_FORM", "Public Web Form", "#1e8e3e", 4)
            ])
        ],
        Modules:
        [
            new("CSKH", "Ticket Hỗ trợ Khách hàng", "SupportAgent", "Tiếp nhận, phân công và giám sát SLA CSKH",
            [
                new("cs_ticket", "Danh sách Ticket CSKH", "Quản lý yêu cầu hỗ trợ từ khách hàng bên ngoài", "kanban", false, "trang_thai_duyet", true,
                [
                    new() { ColumnCode = "ma_ticket", ColumnName = "Mã Ticket", DataType = "AUTO_NUMBER", DefaultValue = "CS-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "tieu_de", ColumnName = "Vấn đề cần hỗ trợ", DataType = "STRING", IsRequired = true, Width = 240, OrderIndex = 2 },
                    new() { ColumnCode = "ten_khach_hang", ColumnName = "Tên Khách hàng", DataType = "STRING", IsRequired = true, Width = 195, OrderIndex = 3 },
                    new() { ColumnCode = "email_kh", ColumnName = "Email Khách", DataType = "EMAIL", Width = 195, OrderIndex = 4 },
                    new() { ColumnCode = "trang_thai", ColumnName = "Trạng thái xử lý", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_TICKET_CS", DefaultValue = "TIEP_NHAN", Width = 175, OrderIndex = 5 },
                    new() { ColumnCode = "kenh_tiep_nhan", ColumnName = "Kênh tiếp nhận", DataType = "SELECT", RefCategoryCode = "DM_KENH_TIEP_NHAN", DefaultValue = "WEB_FORM", Width = 165, OrderIndex = 6 },
                    new() { ColumnCode = "uu_tien", ColumnName = "Ưu tiên", DataType = "SELECT", RefCategoryCode = "DM_UU_TIEN", DefaultValue = "MEDIUM", Width = 135, OrderIndex = 7 },
                    HrmLookupCol("nhan_vien_ho_tro", "Chuyên viên CSKH", 8),
                    new() { ColumnCode = "han_sla", ColumnName = "Hạn SLA", DataType = "DATE", Width = 135, OrderIndex = 9 },
                    new() { ColumnCode = "diem_csat", ColumnName = "Điểm CSAT (1-5)", DataType = "NUMBER", Width = 130, OrderIndex = 10 },
                    new() { ColumnCode = "chi_tiet", ColumnName = "Nội dung chi tiết", DataType = "TEXTAREA", Width = 240, OrderIndex = 11 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("cs_ticket", new() { ["ma_ticket"] = "CS-2026-0001", ["tieu_de"] = "Hướng dẫn kết nối API đồng bộ đơn hàng", ["ten_khach_hang"] = "Công ty CP Sao Việt", ["email_kh"] = "tech@saoviet.vn", ["trang_thai"] = "DANG_XU_LY", ["kenh_tiep_nhan"] = "EMAIL", ["uu_tien"] = "HIGH", ["nhan_vien_ho_tro"] = "NV001", ["han_sla"] = "2026-11-18", ["diem_csat"] = 5, ["chi_tiet"] = "Khách hàng cần tài liệu xác thực X-Api-Key." }),
            new("cs_ticket", new() { ["ma_ticket"] = "CS-2026-0002", ["tieu_de"] = "Yêu cầu xuất lại hóa đơn VAT tháng 10", ["ten_khach_hang"] = "Tập đoàn Hòa Bình", ["email_kh"] = "ketoan@hoabinh.vn", ["trang_thai"] = "CHO_KHACH", ["kenh_tiep_nhan"] = "HOTLINE", ["uu_tien"] = "MEDIUM", ["nhan_vien_ho_tro"] = "NV003", ["han_sla"] = "2026-11-19", ["diem_csat"] = 4, ["chi_tiet"] = "Đã gửi bản nháp điều chỉnh qua email." }),
            new("cs_ticket", new() { ["ma_ticket"] = "CS-2026-0003", ["tieu_de"] = "Nâng cấp thêm 20 tài khoản nhân sự mới", ["ten_khach_hang"] = "WinMart Pro", ["email_kh"] = "it@winmartpro.vn", ["trang_thai"] = "DA_GIAI_QUYET", ["kenh_tiep_nhan"] = "ZALO_OA", ["uu_tien"] = "LOW", ["nhan_vien_ho_tro"] = "NV002", ["han_sla"] = "2026-11-15", ["diem_csat"] = 5, ["chi_tiet"] = "Đã kích hoạt license bổ sung hoàn tất." }),
            new("cs_ticket", new() { ["ma_ticket"] = "CS-2026-0004", ["tieu_de"] = "Báo lỗi hiển thị biểu đồ trên màn hình mobile", ["ten_khach_hang"] = "Công ty Logistics Á Châu", ["email_kh"] = "support@achau.vn", ["trang_thai"] = "TIEP_NHAN", ["kenh_tiep_nhan"] = "WEB_FORM", ["uu_tien"] = "HIGH", ["nhan_vien_ho_tro"] = "NV001", ["han_sla"] = "2026-11-17", ["chi_tiet"] = "Gửi từ biểu mẫu Public Form." })
        ],
        Widgets:
        [
            new("cs_ticket", "Tổng Ticket CSKH", "KPI_COUNT", null, null, null, null, 1, "#0369a1", 1),
            new("cs_ticket", "Ticket Ưu tiên Cao", "KPI_COUNT", null, null, "uu_tien", "HIGH", 1, "#d93025", 2),
            new("cs_ticket", "Đã giải quyết xong", "KPI_COUNT", null, null, "trang_thai", "DA_GIAI_QUYET", 1, "#1e8e3e", 3),
            new("cs_ticket", "Điểm Hài lòng (CSAT TB)", "KPI_AVG", "diem_csat", null, null, null, 1, "#ea8600", 4),
            new("cs_ticket", "Ticket theo Trạng thái xử lý", "DONUT_CHART", null, "trang_thai", null, null, 2, "#0369a1", 5),
            new("cs_ticket", "Ticket theo Kênh tiếp nhận", "BAR_CHART", null, "kenh_tiep_nhan", null, null, 2, "#7b1fa2", 6)
        ]);

    private static AppTemplateDefinition BuildMeetingTemplate() => new(
        TemplateKey: "MEETING",
        Code: "MEETING",
        Name: "Đặt Phòng họp & Điều xe Công tác",
        Description: "Đăng ký lịch phòng họp theo chế độ xem Lịch trực quan và phê duyệt phiếu điều xe đi công tác.",
        Department: "Hành chính Văn phòng",
        Category: "Nhân sự & Hành chính",
        Icon: "EventAvailable",
        Color: "#0f766e",
        BgColor: "#ccfbf1",
        OrderIndex: 18,
        Highlights:
        [
            "Mặc định hiển thị dạng Lịch (Calendar) giúp tránh trùng phòng họp",
            "Kèm phân hệ Điều xe Công tác có quy trình phê duyệt hành chính",
            "Form đăng ký nhanh cho nhân viên toàn công ty"
        ],
        Features: ["Lịch", "Phê duyệt", "Form công khai", "Liên kết HRM"],
        Categories:
        [
            new("DM_PHONG_HOP", "Phòng họp Công ty", "Danh sách phòng họp & hội trường",
            [
                new("PH_SAO_MAI", "Phòng Sao Mai (Tầng 5 - 8 chỗ)", "#1a73e8", 1),
                new("PH_HOI_NGHI", "Phòng Hội nghị lớn (25 chỗ)", "#7b1fa2", 2),
                new("PH_VIP", "Phòng Tiếp khách VIP (6 chỗ)", "#ea8600", 3),
                new("STUDIO_ONLINE", "Phòng Họp Online / Studio", "#1e8e3e", 4)
            ]),
            new("DM_TRANG_THAI_LICH", "Trạng thái Lịch họp", "Xác nhận giữ phòng họp",
            [
                new("DA_XAC_NHAN", "Đã giữ phòng", "#1e8e3e", 1),
                new("CHO_XAC_NHAN", "Chờ xác nhận", "#ea8600", 2),
                new("DA_HUY", "Đã hủy lịch", "#d93025", 3)
            ])
        ],
        Modules:
        [
            new("PHONGHOP", "Đặt Phòng họp", "EventAvailable", "Lịch sử dụng phòng họp và trang thiết bị trình chiếu",
            [
                new("adm_dat_phong", "Lịch Đặt Phòng họp", "Xem lịch đặt phòng theo tháng/ngày để điều phối hợp lý", "calendar", false, "trang_thai_duyet", true,
                [
                    new() { ColumnCode = "ma_lich", ColumnName = "Mã Đặt lịch", DataType = "AUTO_NUMBER", DefaultValue = "PH-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "tieu_de_hop", ColumnName = "Nội dung cuộc họp", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    new() { ColumnCode = "phong_hop", ColumnName = "Phòng họp", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_PHONG_HOP", Width = 215, OrderIndex = 3 },
                    HrmLookupCol("nguoi_dat", "Người đặt phòng", 4),
                    new() { ColumnCode = "ngay_hop", ColumnName = "Ngày họp", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 5 },
                    new() { ColumnCode = "khung_gio", ColumnName = "Khung giờ", DataType = "STRING", IsRequired = true, DefaultValue = "09:00 - 10:30", Width = 140, OrderIndex = 6 },
                    new() { ColumnCode = "so_nguoi", ColumnName = "Số người", DataType = "NUMBER", DefaultValue = "6", Width = 110, OrderIndex = 7 },
                    new() { ColumnCode = "can_may_chieu", ColumnName = "Cần Máy chiếu / Mic", DataType = "BOOLEAN", DefaultValue = "true", Width = 145, OrderIndex = 8 },
                    new() { ColumnCode = "trang_thai", ColumnName = "Trạng thái", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_LICH", DefaultValue = "DA_XAC_NHAN", Width = 155, OrderIndex = 9 }
                ])
            ]),
            new("DIEUXE", "Điều xe Công tác", "LocalShipping", "Đăng ký xe đưa đón công tác ngoại tỉnh / gặp khách hàng",
            [
                new("adm_dieu_xe", "Phiếu Điều xe Công tác", "Trình duyệt lộ trình và chi phí cầu đường", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_chuyen", ColumnName = "Mã Chuyến xe", DataType = "AUTO_NUMBER", DefaultValue = "XE-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "lo_trinh", ColumnName = "Lộ trình công tác", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    HrmLookupCol("nguoi_di", "Người đăng ký", 3),
                    new() { ColumnCode = "ngay_di", ColumnName = "Ngày khởi hành", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 4 },
                    new() { ColumnCode = "so_km", ColumnName = "Quãng đường (KM)", DataType = "NUMBER", DefaultValue = "40", Width = 135, OrderIndex = 5 },
                    new() { ColumnCode = "chi_phi_du_kien", ColumnName = "Chi phí xăng/cầu đường", DataType = "CURRENCY", DefaultValue = "0", Width = 170, OrderIndex = 6 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 7 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("adm_dat_phong", new() { ["ma_lich"] = "PH-2026-0001", ["tieu_de_hop"] = "Họp Giao ban Ban Giám đốc Tuần 46", ["phong_hop"] = "PH_HOI_NGHI", ["nguoi_dat"] = "NV002", ["ngay_hop"] = "2026-11-16", ["khung_gio"] = "08:30 - 10:00", ["so_nguoi"] = 18, ["can_may_chieu"] = true, ["trang_thai"] = "DA_XAC_NHAN" }),
            new("adm_dat_phong", new() { ["ma_lich"] = "PH-2026-0002", ["tieu_de_hop"] = "Review Kiến trúc Module Thanh toán", ["phong_hop"] = "PH_SAO_MAI", ["nguoi_dat"] = "NV001", ["ngay_hop"] = "2026-11-17", ["khung_gio"] = "14:00 - 15:30", ["so_nguoi"] = 6, ["can_may_chieu"] = true, ["trang_thai"] = "DA_XAC_NHAN" }),
            new("adm_dat_phong", new() { ["ma_lich"] = "PH-2026-0003", ["tieu_de_hop"] = "Tiếp đón Đối tác Chiến lược Sao Việt", ["phong_hop"] = "PH_VIP", ["nguoi_dat"] = "NV001", ["ngay_hop"] = "2026-11-19", ["khung_gio"] = "10:00 - 11:30", ["so_nguoi"] = 5, ["can_may_chieu"] = false, ["trang_thai"] = "CHO_XAC_NHAN" }),
            new("adm_dieu_xe", new() { ["ma_chuyen"] = "XE-2026-0001", ["lo_trinh"] = "Trụ sở chính → Khu Công nghiệp Bắc Thăng Long", ["nguoi_di"] = "NV001", ["ngay_di"] = "2026-11-18", ["so_km"] = 65, ["chi_phi_du_kien"] = 450000m, ["trang_thai_duyet"] = "DA_DUYET" }),
            new("adm_dieu_xe", new() { ["ma_chuyen"] = "XE-2026-0002", ["lo_trinh"] = "Trụ sở chính → Chi nhánh Hải Phòng", ["nguoi_di"] = "NV003", ["ngay_di"] = "2026-11-21", ["so_km"] = 220, ["chi_phi_du_kien"] = 1200000m, ["trang_thai_duyet"] = "CHO_DUYET" })
        ],
        Widgets:
        [
            new("adm_dat_phong", "Tổng Lượt Đặt phòng", "KPI_COUNT", null, null, null, null, 1, "#0f766e", 1),
            new("adm_dat_phong", "Lịch Đã xác nhận", "KPI_COUNT", null, null, "trang_thai", "DA_XAC_NHAN", 1, "#1e8e3e", 2),
            new("adm_dieu_xe", "Chuyến xe Chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#ea8600", 3),
            new("adm_dieu_xe", "Tổng Chi phí Điều xe", "KPI_SUM", "chi_phi_du_kien", null, null, null, 1, "#1a73e8", 4),
            new("adm_dat_phong", "Tần suất sử dụng Phòng họp", "DONUT_CHART", null, "phong_hop", null, null, 2, "#0f766e", 5),
            new("adm_dat_phong", "Lịch họp Sắp tới", "RECENT_LIST", null, null, null, null, 2, "#1a73e8", 6)
        ]);

    private static AppTemplateDefinition BuildTrainingTemplate() => new(
        TemplateKey: "TRAINING",
        Code: "TRAINING",
        Name: "Đào tạo Nội bộ & Chứng chỉ (L&D)",
        Description: "Quản lý danh mục khóa học đào tạo nhân sự, ghi danh học viên qua Form công khai và tự chấm Đạt/Chưa đạt.",
        Department: "Nhân sự & Đào tạo (L&D)",
        Category: "Nhân sự & Hành chính",
        Icon: "Badge",
        Color: "#2563eb",
        BgColor: "#dbeafe",
        OrderIndex: 19,
        Highlights:
        [
            "Tự động xét kết quả khóa học: IF(diem_thi >= 70, 'DAT', 'CHUA_DAT')",
            "Liên kết khóa học và học viên trực tiếp từ App HRM",
            "Form đăng ký khóa học công khai cho toàn thể nhân viên"
        ],
        Features: ["Form công khai", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_NHOM_KHOA_HOC", "Nhóm Khóa học Đào tạo", "Phân loại chương trình L&D",
            [
                new("ONBOARDING", "Hội nhập Nhân viên mới", "#1a73e8", 1),
                new("KY_THUAT", "Chuyên môn & Công nghệ", "#7b1fa2", 2),
                new("KY_NANG_MEM", "Kỹ năng mềm & Quản lý", "#1e8e3e", 3),
                new("AN_TOAN_BM", "An toàn thông tin & Tuân thủ", "#d93025", 4)
            ]),
            new("DM_TRANG_THAI_HOC", "Trạng thái Học viên", "Tiến độ hoàn thành khóa học",
            [
                new("DA_DANG_KY", "Đã đăng ký", "#5f6368", 1),
                new("DANG_HOC", "Đang theo học", "#1a73e8", 2),
                new("HOAN_THANH", "Đã hoàn thành", "#1e8e3e", 3)
            ])
        ],
        Modules:
        [
            new("DAOTAO", "Chương trình & Học viên", "Badge", "Quản lý khóa học và kết quả đánh giá năng lực",
            [
                new("ld_khoa_hoc", "Danh mục Khóa học Nội bộ", "Thông tin giảng viên, thời lượng và ngân sách đào tạo", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_khoa", ColumnName = "Mã Khóa học", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "ten_khoa", ColumnName = "Tên Khóa học", DataType = "STRING", IsRequired = true, Width = 235, OrderIndex = 2 },
                    new() { ColumnCode = "nhom_ky_nang", ColumnName = "Nhóm chương trình", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_NHOM_KHOA_HOC", Width = 190, OrderIndex = 3 },
                    new() { ColumnCode = "giang_vien", ColumnName = "Giảng viên / Đơn vị", DataType = "STRING", Width = 185, OrderIndex = 4 },
                    new() { ColumnCode = "ngay_khai_giang", ColumnName = "Ngày khai giảng", DataType = "DATE", Width = 140, OrderIndex = 5 },
                    new() { ColumnCode = "thoi_luong_gio", ColumnName = "Thời lượng (giờ)", DataType = "NUMBER", DefaultValue = "4", Width = 130, OrderIndex = 6 },
                    new() { ColumnCode = "chi_phi", ColumnName = "Chi phí tổ chức", DataType = "CURRENCY", DefaultValue = "0", Width = 155, OrderIndex = 7 },
                    new() { ColumnCode = "bat_buoc", ColumnName = "Bắt buộc toàn công ty", DataType = "BOOLEAN", DefaultValue = "false", Width = 150, OrderIndex = 8 }
                ]),
                new("ld_dang_ky_hoc", "Danh sách Học viên & Kết quả", "Đăng ký tham gia và tự động đánh giá kết quả bài kiểm tra", "table", false, "trang_thai_duyet", true,
                [
                    new() { ColumnCode = "ma_dk", ColumnName = "Mã Ghi danh", DataType = "AUTO_NUMBER", DefaultValue = "LD-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "khoa_hoc", ColumnName = "Khóa học", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "ld_khoa_hoc", RefValueField = "ma_khoa", RefDisplayField = "ten_khoa", ShowInParentForm = true, Width = 225, OrderIndex = 2 },
                    HrmLookupCol("hoc_vien", "Học viên (HRM)", 3),
                    new() { ColumnCode = "ngay_dk", ColumnName = "Ngày đăng ký", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 4 },
                    new() { ColumnCode = "diem_thi", ColumnName = "Điểm kiểm tra (/100)", DataType = "NUMBER", Width = 145, OrderIndex = 5 },
                    new() { ColumnCode = "ket_qua", ColumnName = "Kết quả (Auto)", DataType = "STRING", Formula = "IF(diem_thi >= 70, 'DAT', 'CHUA_DAT')", Width = 140, OrderIndex = 6 },
                    new() { ColumnCode = "trang_thai", ColumnName = "Tiến độ học", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_HOC", DefaultValue = "DA_DANG_KY", Width = 160, OrderIndex = 7 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("ld_khoa_hoc", new() { ["ma_khoa"] = "SEC-101", ["ten_khoa"] = "Nhận thức An toàn Thông tin ISO 27001", ["nhom_ky_nang"] = "AN_TOAN_BM", ["giang_vien"] = "Ban An ninh mạng", ["ngay_khai_giang"] = "2026-11-10", ["thoi_luong_gio"] = 4, ["chi_phi"] = 5000000m, ["bat_buoc"] = true }),
            new("ld_khoa_hoc", new() { ["ma_khoa"] = "TECH-202", ["ten_khoa"] = "Kiến trúc PostgreSQL JSONB & Hiệu năng cao", ["nhom_ky_nang"] = "KY_THUAT", ["giang_vien"] = "Nguyễn Văn An (Tech Lead)", ["ngay_khai_giang"] = "2026-11-20", ["thoi_luong_gio"] = 8, ["chi_phi"] = 0m, ["bat_buoc"] = false }),
            new("ld_khoa_hoc", new() { ["ma_khoa"] = "MGR-301", ["ten_khoa"] = "Kỹ năng Quản trị Mục tiêu OKR cho Quản lý", ["nhom_ky_nang"] = "KY_NANG_MEM", ["giang_vien"] = "Học viện PACE", ["ngay_khai_giang"] = "2026-11-25", ["thoi_luong_gio"] = 12, ["chi_phi"] = 24000000m, ["bat_buoc"] = false }),
            new("ld_dang_ky_hoc", new() { ["ma_dk"] = "LD-2026-0001", ["khoa_hoc"] = "SEC-101", ["hoc_vien"] = "NV001", ["ngay_dk"] = "2026-11-08", ["diem_thi"] = 92, ["trang_thai"] = "HOAN_THANH" }),
            new("ld_dang_ky_hoc", new() { ["ma_dk"] = "LD-2026-0002", ["khoa_hoc"] = "SEC-101", ["hoc_vien"] = "NV002", ["ngay_dk"] = "2026-11-08", ["diem_thi"] = 85, ["trang_thai"] = "HOAN_THANH" }),
            new("ld_dang_ky_hoc", new() { ["ma_dk"] = "LD-2026-0003", ["khoa_hoc"] = "SEC-101", ["hoc_vien"] = "NV003", ["ngay_dk"] = "2026-11-09", ["diem_thi"] = 62, ["trang_thai"] = "DANG_HOC" }),
            new("ld_dang_ky_hoc", new() { ["ma_dk"] = "LD-2026-0004", ["khoa_hoc"] = "TECH-202", ["hoc_vien"] = "NV003", ["ngay_dk"] = "2026-11-15", ["diem_thi"] = 0, ["trang_thai"] = "DA_DANG_KY" })
        ],
        Widgets:
        [
            new("ld_khoa_hoc", "Tổng Số Khóa học", "KPI_COUNT", null, null, null, null, 1, "#2563eb", 1),
            new("ld_dang_ky_hoc", "Lượt Học viên Ghi danh", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 2),
            new("ld_dang_ky_hoc", "Học viên Đạt chứng chỉ", "KPI_COUNT", null, null, "ket_qua", "DAT", 1, "#1e8e3e", 3),
            new("ld_khoa_hoc", "Tổng Ngân sách Đào tạo", "KPI_SUM", "chi_phi", null, null, null, 1, "#ea8600", 4),
            new("ld_khoa_hoc", "Khóa học theo Nhóm năng lực", "DONUT_CHART", null, "nhom_ky_nang", null, null, 2, "#2563eb", 5),
            new("ld_dang_ky_hoc", "Tiến độ Học viên", "BAR_CHART", null, "trang_thai", null, null, 2, "#1e8e3e", 6)
        ]);

    private static AppTemplateDefinition BuildKpiOkrTemplate() => new(
        TemplateKey: "KPI_OKR",
        Code: "KPI_OKR",
        Name: "Quản trị Mục tiêu OKR & KPI",
        Description: "Thiết lập mục tiêu OKR theo quý/năm cho từng phòng ban, tự động tính % hoàn thành và xếp loại kết quả.",
        Department: "Ban Điều hành & Nhân sự",
        Category: "Pháp chế & Quản trị",
        Icon: "BusinessCenter",
        Color: "#dc2626",
        BgColor: "#fee2e2",
        OrderIndex: 20,
        Highlights:
        [
            "Công thức đa tầng: tự tính ty_le_hoan_thanh (%) rồi xếp loại Đạt / Cần cải thiện",
            "Theo dõi KPI theo Chu kỳ Quý & từng Phòng ban công ty",
            "Gắn trách nhiệm cá nhân trực tiếp với hồ sơ nhân viên HRM"
        ],
        Features: ["Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_CHU_KY_OKR", "Chu kỳ Đánh giá OKR/KPI", "Kỳ đánh giá hiệu suất công việc",
            [
                new("Q4_2026", "Quý 4 / 2026", "#1a73e8", 1),
                new("Q1_2027", "Quý 1 / 2027", "#7b1fa2", 2),
                new("NAM_2026", "Cả năm 2026", "#1e8e3e", 3)
            ])
        ],
        Modules:
        [
            new("OKR", "Mục tiêu & Chỉ số KPI", "BusinessCenter", "Theo dõi thực hiện chỉ tiêu kinh doanh và vận hành",
            [
                new("okr_muc_tieu", "Bảng Theo dõi OKR & KPI", "Tự động tính tỷ lệ hoàn thành (%) và đánh giá đạt chuẩn (>= 80%)", "table", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_okr", ColumnName = "Mã OKR", DataType = "AUTO_NUMBER", DefaultValue = "OKR-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "muc_tieu", ColumnName = "Tên Mục tiêu / Chỉ số KPI", DataType = "STRING", IsRequired = true, Width = 245, OrderIndex = 2 },
                    new() { ColumnCode = "chu_ky", ColumnName = "Chu kỳ", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_CHU_KY_OKR", DefaultValue = "Q4_2026", Width = 145, OrderIndex = 3 },
                    new() { ColumnCode = "phong_ban", ColumnName = "Phòng ban", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_PHONG_BAN", Width = 190, OrderIndex = 4 },
                    HrmLookupCol("phu_trach", "Người chịu trách nhiệm", 5),
                    new() { ColumnCode = "muc_tieu_so", ColumnName = "Chỉ tiêu kế hoạch", DataType = "NUMBER", IsRequired = true, Width = 145, OrderIndex = 6 },
                    new() { ColumnCode = "thuc_dat", ColumnName = "Kết quả thực đạt", DataType = "NUMBER", IsRequired = true, DefaultValue = "0", Width = 145, OrderIndex = 7 },
                    new() { ColumnCode = "ty_le_hoan_thanh", ColumnName = "Hoàn thành (%)", DataType = "NUMBER", Formula = "ROUND(thuc_dat * 100 / muc_tieu_so, 1)", Width = 135, OrderIndex = 8 },
                    new() { ColumnCode = "xep_loai", ColumnName = "Xếp loại (Auto)", DataType = "STRING", Formula = "IF(ty_le_hoan_thanh >= 80, 'DAT', 'CAN_CAI_THIEN')", Width = 150, OrderIndex = 9 },
                    new() { ColumnCode = "ghi_chu", ColumnName = "Nhận xét / Hành động", DataType = "TEXTAREA", Width = 225, OrderIndex = 10 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("okr_muc_tieu", new() { ["ma_okr"] = "OKR-2026-0001", ["muc_tieu"] = "Đạt SLA Uptime Hệ thống 99.9% (điểm hiệu suất)", ["chu_ky"] = "Q4_2026", ["phong_ban"] = "TECH", ["phu_trach"] = "NV001", ["muc_tieu_so"] = 100, ["thuc_dat"] = 98, ["ghi_chu"] = "Vận hành ổn định liên tục trong tháng 11." }),
            new("okr_muc_tieu", new() { ["ma_okr"] = "OKR-2026-0002", ["muc_tieu"] = "Tuyển đủ 10 kỹ sư & nhân sự chủ chốt Q4", ["chu_ky"] = "Q4_2026", ["phong_ban"] = "HR", ["phu_trach"] = "NV002", ["muc_tieu_so"] = 10, ["thuc_dat"] = 8, ["ghi_chu"] = "Đã chốt 8 offer, còn 2 vị trí đang phỏng vấn vòng cuối." }),
            new("okr_muc_tieu", new() { ["ma_okr"] = "OKR-2026-0003", ["muc_tieu"] = "Hoàn thành quyết toán chứng từ đúng hạn (%)", ["chu_ky"] = "Q4_2026", ["phong_ban"] = "FIN", ["phu_trach"] = "NV003", ["muc_tieu_so"] = 100, ["thuc_dat"] = 92, ["ghi_chu"] = "Giảm thời gian xử lý phiếu thanh toán xuống 24h." }),
            new("okr_muc_tieu", new() { ["ma_okr"] = "OKR-2026-0004", ["muc_tieu"] = "Mở rộng 25 Khách hàng Doanh nghiệp mới", ["chu_ky"] = "Q4_2026", ["phong_ban"] = "SALES", ["phu_trach"] = "NV001", ["muc_tieu_so"] = 25, ["thuc_dat"] = 17, ["ghi_chu"] = "Đang đẩy mạnh pipeline tháng 12." })
        ],
        Widgets:
        [
            new("okr_muc_tieu", "Tổng Mục tiêu OKR", "KPI_COUNT", null, null, null, null, 1, "#dc2626", 1),
            new("okr_muc_tieu", "Tỷ lệ Hoàn thành Trung bình (%)", "KPI_AVG", "ty_le_hoan_thanh", null, null, null, 1, "#1a73e8", 2),
            new("okr_muc_tieu", "Số Mục tiêu Đạt (>=80%)", "KPI_COUNT", null, null, "xep_loai", "DAT", 1, "#1e8e3e", 3),
            new("okr_muc_tieu", "Cần tăng tốc (<80%)", "KPI_COUNT", null, null, "xep_loai", "CAN_CAI_THIEN", 1, "#ea8600", 4),
            new("okr_muc_tieu", "Phân bổ OKR theo Phòng ban", "BAR_CHART", null, "phong_ban", null, null, 2, "#dc2626", 5),
            new("okr_muc_tieu", "Tỷ lệ Xếp loại Đạt", "DONUT_CHART", null, "xep_loai", null, null, 2, "#1e8e3e", 6)
        ]);

    private static AppTemplateDefinition BuildFleetTemplate() => new(
        TemplateKey: "FLEET",
        Code: "FLEET",
        Name: "Quản lý Đội xe, Bảo dưỡng & Đăng kiểm",
        Description: "Theo dõi danh sách phương tiện công ty, hạn đăng kiểm trên Lịch và duyệt chi phí bảo trì phụ tùng.",
        Department: "Vận hành & Hậu cần",
        Category: "Kho vận & Vận hành",
        Icon: "LocalShipping",
        Color: "#0891b2",
        BgColor: "#cffafe",
        OrderIndex: 21,
        Highlights:
        [
            "Theo dõi hạn đăng kiểm phương tiện trực quan theo Lịch (Calendar)",
            "Phiếu bảo dưỡng có bảng phụ tùng SUB_TABLE tự cộng tổng chi phí",
            "Gắn tài xế phụ trách từ HRM & quy trình duyệt chi phí bảo trì"
        ],
        Features: ["Phê duyệt", "Sub-table", "Lịch", "Công thức", "Liên kết HRM"],
        Categories:
        [
            new("DM_TRANG_THAI_XE", "Tình trạng Phương tiện", "Trạng thái vận hành xe công ty",
            [
                new("SAN_SANG", "Sẵn sàng điều động", "#1e8e3e", 1),
                new("DANG_CONG_TAC", "Đang đi công tác", "#1a73e8", 2),
                new("DANG_BAO_DUONG", "Đang bảo dưỡng", "#ea8600", 3)
            ]),
            new("DM_LOAI_BAO_TRI", "Hình thức Bảo dưỡng Xe", "Phân loại bảo trì phương tiện",
            [
                new("DINH_KY", "Bảo dưỡng định kỳ", "#1a73e8", 1),
                new("SUA_CHUA", "Sửa chữa đột xuất", "#d93025", 2),
                new("DANG_KIEM", "Đăng kiểm & Bảo hiểm", "#7b1fa2", 3)
            ])
        ],
        Modules:
        [
            new("DOIXE", "Danh sách Phương tiện", "LocalShipping", "Quản lý biển số, ODO, tài xế và hạn đăng kiểm",
            [
                new("flt_phuong_tien", "Phương tiện Công ty", "Theo dõi tình trạng vận hành và lịch hết hạn đăng kiểm", "calendar", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "bien_so", ColumnName = "Biển số xe", DataType = "STRING", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "hang_xe", ColumnName = "Hãng / Dòng xe", DataType = "STRING", IsRequired = true, Width = 215, OrderIndex = 2 },
                    HrmLookupCol("tai_xe", "Tài xế / Phụ trách (HRM)", 3),
                    new() { ColumnCode = "so_km", ColumnName = "Số ODO (km)", DataType = "NUMBER", DefaultValue = "0", Width = 130, OrderIndex = 4 },
                    new() { ColumnCode = "han_dang_kiem", ColumnName = "Hạn đăng kiểm", DataType = "DATE", IsRequired = true, Width = 145, OrderIndex = 5 },
                    new() { ColumnCode = "tinh_trang", ColumnName = "Tình trạng xe", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_XE", DefaultValue = "SAN_SANG", Width = 175, OrderIndex = 6 }
                ])
            ]),
            new("BAOTRI", "Lịch & Chi phí Bảo dưỡng", "Engineering", "Phiếu bảo dưỡng phụ tùng và duyệt thanh toán garage",
            [
                new("flt_bao_tri", "Phiếu Bảo dưỡng & Sửa chữa", "Lập bảng kê phụ tùng thay thế, tự tính tổng chi phí và trình duyệt", "table", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã Phiếu", DataType = "AUTO_NUMBER", DefaultValue = "BT-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 135, OrderIndex = 1 },
                    new() { ColumnCode = "bien_so", ColumnName = "Phương tiện", DataType = "LOOKUP", IsRequired = true, InternalRefTableName = "flt_phuong_tien", RefValueField = "bien_so", RefDisplayField = "hang_xe", ShowInParentForm = true, Width = 210, OrderIndex = 2 },
                    new() { ColumnCode = "loai_bao_tri", ColumnName = "Loại bảo dưỡng", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_BAO_TRI", Width = 175, OrderIndex = 3 },
                    new() { ColumnCode = "ngay_thuc_hien", ColumnName = "Ngày thực hiện", DataType = "DATE", IsRequired = true, Width = 140, OrderIndex = 4 },
                    new() { ColumnCode = "chi_tiet_phu_tung", ColumnName = "Chi tiết phụ tùng / công", DataType = "SUB_TABLE", DefaultValue = "hang_muc,so_luong,thanh_tien", ShowInTable = false, Width = 260, OrderIndex = 5 },
                    new() { ColumnCode = "tong_chi_phi", ColumnName = "Tổng chi phí (Auto)", DataType = "CURRENCY", Formula = "SUM(chi_tiet_phu_tung.thanh_tien)", Width = 175, OrderIndex = 6 },
                    new() { ColumnCode = "trang_thai_duyet", ColumnName = "Trạng thái duyệt", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_TRANG_THAI_DUYET", DefaultValue = "CHO_DUYET", Width = 155, OrderIndex = 7 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("flt_phuong_tien", new() { ["bien_so"] = "29A-886.99", ["hang_xe"] = "Toyota Camry 2.5Q (Xe Ban GĐ)", ["tai_xe"] = "NV001", ["so_km"] = 42500, ["han_dang_kiem"] = "2026-12-20", ["tinh_trang"] = "SAN_SANG" }),
            new("flt_phuong_tien", new() { ["bien_so"] = "29B-512.34", ["hang_xe"] = "Ford Transit 16 chỗ (Đưa đón)", ["tai_xe"] = "NV003", ["so_km"] = 86200, ["han_dang_kiem"] = "2026-11-28", ["tinh_trang"] = "DANG_CONG_TAC" }),
            new("flt_phuong_tien", new() { ["bien_so"] = "29C-304.55", ["hang_xe"] = "Ford Ranger XLS (Giao nhận)", ["tai_xe"] = "NV002", ["so_km"] = 61000, ["han_dang_kiem"] = "2027-03-15", ["tinh_trang"] = "DANG_BAO_DUONG" }),
            new("flt_bao_tri", new()
            {
                ["ma_phieu"] = "BT-2026-0001",
                ["bien_so"] = "29C-304.55",
                ["loai_bao_tri"] = "DINH_KY",
                ["ngay_thuc_hien"] = "2026-11-14",
                ["chi_tiet_phu_tung"] = new List<Dictionary<string, object?>>
                {
                    new() { ["hang_muc"] = "Thay dầu động cơ & Lọc nhớt", ["so_luong"] = 1, ["thanh_tien"] = 2400000m },
                    new() { ["hang_muc"] = "Thay má phanh trước", ["so_luong"] = 2, ["thanh_tien"] = 3800000m }
                },
                ["trang_thai_duyet"] = "DA_DUYET"
            }),
            new("flt_bao_tri", new()
            {
                ["ma_phieu"] = "BT-2026-0002",
                ["bien_so"] = "29B-512.34",
                ["loai_bao_tri"] = "DANG_KIEM",
                ["ngay_thuc_hien"] = "2026-11-24",
                ["chi_tiet_phu_tung"] = new List<Dictionary<string, object?>>
                {
                    new() { ["hang_muc"] = "Phí đăng kiểm & Bảo trì đường bộ 12 tháng", ["so_luong"] = 1, ["thanh_tien"] = 4650000m }
                },
                ["trang_thai_duyet"] = "CHO_DUYET"
            })
        ],
        Widgets:
        [
            new("flt_phuong_tien", "Tổng Số Phương tiện", "KPI_COUNT", null, null, null, null, 1, "#0891b2", 1),
            new("flt_phuong_tien", "Xe Sẵn sàng điều động", "KPI_COUNT", null, null, "tinh_trang", "SAN_SANG", 1, "#1e8e3e", 2),
            new("flt_bao_tri", "Tổng Chi phí Bảo dưỡng", "KPI_SUM", "tong_chi_phi", null, null, null, 1, "#ea8600", 3),
            new("flt_bao_tri", "Phiếu Bảo trì Chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#d93025", 4),
            new("flt_phuong_tien", "Tình trạng Đội xe", "DONUT_CHART", null, "tinh_trang", null, null, 2, "#0891b2", 5),
            new("flt_bao_tri", "Chi phí theo Loại bảo dưỡng", "BAR_CHART", null, "loai_bao_tri", null, null, 2, "#7b1fa2", 6)
        ]);

    private static AppTemplateDefinition BuildProcurementVnSolution() => new(
        TemplateKey: "PROCURE_VN",
        Code: "PROCURE_VN",
        Name: "Đề xuất Mua sắm – Duyệt – Thanh toán VietQR",
        Description: "Giải pháp chạy ngay: Lập đề xuất mua sắm có bảng con chi tiết, duyệt nhiều bước, thanh toán VietQR & xuất HĐĐT.",
        Department: "Tài chính & Mua hàng",
        Category: "Giải pháp Doanh nghiệp VN",
        Icon: "ShoppingCartCheckout",
        Color: "#1a73e8",
        BgColor: "#e8f0fe",
        OrderIndex: 130,
        Highlights:
        [
            "Tự động sinh mã Đề xuất DX-{YYYY}-XXXX & cộng tổng bảng con",
            "Bật sẵn quy trình phê duyệt & thanh toán VietQR / HĐĐT 1-Click",
            "Đính kèm báo giá nhiều tệp (MULTI_FILE) & chữ ký xác nhận"
        ],
        Features: ["Giải pháp VN", "VietQR", "Phê duyệt", "Bảng con"],
        Categories:
        [
            new("DM_LOAI_MUA_SAM", "Loại Mua sắm", "Phân loại đề xuất mua sắm",
            [
                new("THIET_BI", "Thiết bị / Tài sản", "#1a73e8", 1),
                new("VPP", "Văn phòng phẩm", "#1e8e3e", 2),
                new("DICH_VU", "Dịch vụ / Phần mềm", "#7b1fa2", 3)
            ])
        ],
        Modules:
        [
            new("MUA_SAM", "Đề xuất & Thanh toán", "ReceiptLong", "Quy trình mua sắm khép kín",
            [
                new("proc_de_xuat", "Đề xuất Mua sắm", "table", "ALL", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_dx", ColumnName = "Mã Đề xuất", DataType = "AUTO_NUMBER", DefaultValue = "DX-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "tieu_de", ColumnName = "Nội dung đề xuất", DataType = "STRING", IsRequired = true, Width = 240, OrderIndex = 2 },
                    new() { ColumnCode = "loai_mua", ColumnName = "Loại mua sắm", DataType = "SELECT", IsRequired = true, RefCategoryCode = "DM_LOAI_MUA_SAM", Width = 170, OrderIndex = 3 },
                    new() { ColumnCode = "nguoi_de_xuat", ColumnName = "Người đề xuất", DataType = "USER", IsRequired = true, Width = 160, OrderIndex = 4 },
                    new() { ColumnCode = "chi_tiet", ColumnName = "Chi tiết hàng hóa", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,don_gia,thanh_tien", ShowInTable = false, Width = 260, OrderIndex = 5 },
                    new() { ColumnCode = "tong_tien", ColumnName = "Tổng tiền (VNĐ)", DataType = "CURRENCY", Formula = "SUM(chi_tiet.thanh_tien)", Width = 165, OrderIndex = 6 },
                    new() { ColumnCode = "payment_status", ColumnName = "Thanh toán", DataType = "STRING", DefaultValue = "UNPAID", Width = 135, OrderIndex = 7 },
                    new() { ColumnCode = "bao_gia", ColumnName = "Báo giá đính kèm", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 8 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("proc_de_xuat", new()
            {
                ["ma_dx"] = "DX-2026-0001",
                ["tieu_de"] = "Mua 02 Laptop cho phòng Kỹ thuật",
                ["loai_mua"] = "THIET_BI",
                ["nguoi_de_xuat"] = "admin",
                ["chi_tiet"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_hang"] = "Laptop Dell Latitude 5450", ["so_luong"] = 2, ["don_gia"] = 22500000m, ["thanh_tien"] = 45000000m }
                },
                ["payment_status"] = "UNPAID"
            })
        ],
        Widgets:
        [
            new("proc_de_xuat", "Tổng Đề xuất", "KPI_COUNT", null, null, null, null, 1, "#1a73e8", 1),
            new("proc_de_xuat", "Tổng Giá trị Mua sắm", "KPI_SUM", "tong_tien", null, null, null, 1, "#1e8e3e", 2)
        ]);

    private static AppTemplateDefinition BuildSalesInventoryVnSolution() => new(
        TemplateKey: "SALES_VN",
        Code: "SALES_VN",
        Name: "Quản lý Đơn hàng – Kho – Hóa đơn K80/A4",
        Description: "Giải pháp chạy ngay: Tạo đơn hàng, trừ tồn kho, in phiếu K80/A4 có tiền bằng chữ và đối soát thanh toán.",
        Department: "Bán hàng & Kho vận",
        Category: "Giải pháp Doanh nghiệp VN",
        Icon: "PointOfSale",
        Color: "#0f766e",
        BgColor: "#ccfbf1",
        OrderIndex: 131,
        Highlights:
        [
            "Đơn hàng có bảng con chi tiết & tự tính tổng tiền",
            "Sẵn sàng in hóa đơn K80 / phiếu giao hàng A4 đọc tiền bằng chữ",
            "Tích hợp thanh toán VNPay / MoMo / VietQR IPN tự động"
        ],
        Features: ["Giải pháp VN", "In K80/A4", "Bảng con", "Thanh toán"],
        Categories: [],
        Modules:
        [
            new("BAN_HANG", "Đơn hàng & Tồn kho", "Inventory2", "Quản lý bán hàng và kho",
            [
                new("sales_don_hang", "Đơn hàng Bán", "table", "ALL", false, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_don", ColumnName = "Mã Đơn hàng", DataType = "AUTO_NUMBER", DefaultValue = "DH-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "khach_hang", ColumnName = "Khách hàng", DataType = "STRING", IsRequired = true, Width = 210, OrderIndex = 2 },
                    new() { ColumnCode = "sdt", ColumnName = "Số điện thoại", DataType = "TEL", Width = 135, OrderIndex = 3 },
                    new() { ColumnCode = "chi_tiet", ColumnName = "Sản phẩm đặt mua", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,thanh_tien", ShowInTable = false, Width = 260, OrderIndex = 4 },
                    new() { ColumnCode = "tong_tien", ColumnName = "Tổng thanh toán", DataType = "CURRENCY", Formula = "SUM(chi_tiet.thanh_tien)", Width = 165, OrderIndex = 5 },
                    new() { ColumnCode = "payment_status", ColumnName = "Trạng thái TT", DataType = "STRING", DefaultValue = "UNPAID", Width = 135, OrderIndex = 6 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("sales_don_hang", new()
            {
                ["ma_don"] = "DH-2026-0001",
                ["khach_hang"] = "Công ty TNHH Minh Long",
                ["sdt"] = "0901234567",
                ["chi_tiet"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_hang"] = "Máy in hóa đơn K80", ["so_luong"] = 2, ["thanh_tien"] = 4200000m }
                },
                ["payment_status"] = "PAID"
            })
        ],
        Widgets:
        [
            new("sales_don_hang", "Tổng Số Đơn hàng", "KPI_COUNT", null, null, null, null, 1, "#0f766e", 1),
            new("sales_don_hang", "Doanh thu Đơn hàng", "KPI_SUM", "tong_tien", null, null, null, 1, "#1a73e8", 2)
        ]);

    private static AppTemplateDefinition BuildHandoverVnSolution() => new(
        TemplateKey: "HANDOVER_VN",
        Code: "HANDOVER_VN",
        Name: "Nhân sự – Bàn giao Tài sản (Chữ ký tay)",
        Description: "Giải pháp chạy ngay: Quản lý bàn giao tài sản, đính kèm biên bản và ký nhận trực tiếp trên màn hình.",
        Department: "Hành chính – Nhân sự",
        Category: "Giải pháp Doanh nghiệp VN",
        Icon: "Draw",
        Color: "#7b1fa2",
        BgColor: "#f3e8ff",
        OrderIndex: 132,
        Highlights:
        [
            "Trường Chữ ký tay (SIGNATURE) & Chọn nhân sự (USER)",
            "Đính kèm nhiều tệp biên bản (MULTI_FILE) có chia sẻ link",
            "Quy trình duyệt & in biên bản bàn giao A4"
        ],
        Features: ["Giải pháp VN", "Chữ ký tay", "Multi-File", "Phê duyệt"],
        Categories: [],
        Modules:
        [
            new("BAN_GIAO", "Bàn giao Tài sản", "AssignmentInd", "Biên bản giao nhận tài sản",
            [
                new("bg_tai_san", "Phiếu Bàn giao", "table", "ALL", true, "trang_thai_duyet", false,
                [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã Biên bản", DataType = "AUTO_NUMBER", DefaultValue = "BG-{YYYY}-", IsRequired = true, IsIdentityKey = true, Width = 140, OrderIndex = 1 },
                    new() { ColumnCode = "nguoi_nhan", ColumnName = "Nhân sự nhận", DataType = "USER", IsRequired = true, Width = 175, OrderIndex = 2 },
                    new() { ColumnCode = "ngay_ban_giao", ColumnName = "Ngày bàn giao", DataType = "DATE", IsRequired = true, Width = 135, OrderIndex = 3 },
                    new() { ColumnCode = "chi_tiet_ts", ColumnName = "Danh sách tài sản", DataType = "SUB_TABLE", DefaultValue = "ten_ts,serial,tinh_trang", ShowInTable = false, Width = 260, OrderIndex = 4 },
                    new() { ColumnCode = "ho_so_dinh_kem", ColumnName = "Ảnh / Biên bản đính kèm", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 5 },
                    new() { ColumnCode = "chu_ky_nhan", ColumnName = "Chữ ký người nhận", DataType = "SIGNATURE", ShowInTable = true, Width = 160, OrderIndex = 6 }
                ])
            ])
        ],
        SampleRecords:
        [
            new("bg_tai_san", new()
            {
                ["ma_phieu"] = "BG-2026-0001",
                ["nguoi_nhan"] = "admin",
                ["ngay_ban_giao"] = "2026-11-17",
                ["chi_tiet_ts"] = new List<Dictionary<string, object?>>
                {
                    new() { ["ten_ts"] = "Laptop Dell Latitude 5450", ["serial"] = "DL-5450-8899", ["tinh_trang"] = "Mới 100%" }
                }
            })
        ],
        Widgets:
        [
            new("bg_tai_san", "Tổng Biên bản Bàn giao", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 1)
        ]);
}
