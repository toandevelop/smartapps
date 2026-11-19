using System.Text.Json;
using System.Text.Json.Serialization;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public record SubColSpec(
    string ColumnCode,
    string ColumnName,
    string DataType = "STRING",
    bool IsRequired = false,
    int Width = 140,
    string? DefaultValue = null,
    string? Formula = null);

public static partial class AppTemplateCatalog
{
    private static readonly JsonSerializerOptions SubTableJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SubTableJson(params SubColSpec[] cols) =>
        JsonSerializer.Serialize(cols.Select(c => new SubTableColumnDef
        {
            Code = c.ColumnCode,
            Name = c.ColumnName,
            DataType = c.DataType,
            Required = c.IsRequired,
            DefaultValue = c.DefaultValue,
            Formula = c.Formula
        }), SubTableJsonOpts);

    internal static FormRuleDto Rule(
        string id,
        string targetField,
        string property,
        string sourceField,
        string op,
        string? value,
        bool ruleValue,
        bool? elseValue = null) => new()
    {
        Id = id,
        TargetField = targetField,
        Property = property,
        Match = "ALL",
        Conditions =
        [
            new FormConditionDto
            {
                Field = sourceField,
                Operator = op,
                Value = value
            }
        ],
        Value = ruleValue,
        ElseValue = elseValue
    };

    internal static TemplateWorkflowSpec Wf2Step(
        string tableName,
        string name,
        string description,
        string step1Title = "Trưởng bộ phận kiểm tra",
        List<string>? step1Roles = null,
        string step2Title = "Ban Giám đốc phê duyệt") => new(
        Name: name,
        Description: description,
        TableName: tableName,
        TriggerType: "MANUAL_SUBMIT",
        Steps:
        [
            new WorkflowStepDto
            {
                StepOrder = 1,
                Name = step1Title,
                ApproverRoles = step1Roles ?? ["HR_MANAGER", "ASSET_LEAD"],
                SlaHours = 24,
                RequireCommentOnReject = true,
                ApprovalMode = "ANY",
                AllowReturnToPrevious = true
            },
            new WorkflowStepDto
            {
                StepOrder = 2,
                Name = step2Title,
                ApproverRoles = ["ADMIN"],
                SlaHours = 24,
                RequireCommentOnReject = true,
                ApprovalMode = "ANY",
                AllowReturnToPrevious = true
            }
        ]);

    internal static string PrintA4(
        string title,
        string subtitle,
        string codeField,
        (string Label, string Field)[] fields,
        string? subTableCode = null,
        (string Header, string Key, bool IsCurrency)[]? subCols = null,
        string? totalLabel = null,
        string? totalField = null,
        (string LeftSign, string MidSign, string RightSign)? signs = null)
    {
        var metaRows = string.Join("\n", fields.Select(f =>
            $"<tr><td class=\"lbl\">{f.Label}</td><td class=\"val\">{{{{{f.Field}}}}}</td></tr>"));

        var subHtml = "";
        if (!string.IsNullOrWhiteSpace(subTableCode) && subCols is { Length: > 0 })
        {
            var ths = string.Join("", subCols.Select(c => $"<th>{c.Header}</th>"));
            var tds = string.Join("", subCols.Select(c =>
                c.IsCurrency
                    ? $"<td class=\"num\">{{{{money {c.Key}}}}}</td>"
                    : $"<td>{{{{{c.Key}}}}}</td>"));
            var eachStart = $"{{{{#each {subTableCode}}}}}";
            var eachEnd = "{{/each}}";
            subHtml = $$$"""
                <table class="items">
                  <thead><tr><th>STT</th>{{{ths}}}</tr></thead>
                  <tbody>
                    {{{eachStart}}}
                    <tr><td class="center">{{@index}}</td>{{{tds}}}</tr>
                    {{{eachEnd}}}
                  </tbody>
                </table>
                """;
        }

        var totalHtml = "";
        if (!string.IsNullOrWhiteSpace(totalField))
        {
            var moneyTag = $"{{{{money {totalField}}}}}";
            var wordsTag = $"{{{{money_words {totalField}}}}}";
            totalHtml = $$$"""
                <div class="summary-box">
                  <div><strong>{{{totalLabel ?? "Tổng giá trị"}}}:</strong> <span class="total-amt">{{{moneyTag}}} đ</span></div>
                  <div class="words"><em>Bằng chữ:</em> {{{wordsTag}}}</div>
                </div>
                """;
        }

        var sig = signs ?? ("Người lập phiếu", "Kiểm soát / Trưởng bộ phận", "Phê duyệt");
        var codeTag = $"{{{{{codeField}}}}}";
        return $$$"""
            <div class="doc-a4">
              <div class="doc-header">
                <div>
                  <div class="brand">SMARTAPPS ENTERPRISE 2.0</div>
                  <div class="sub">{{{subtitle}}}</div>
                </div>
                <div class="doc-meta">
                  <div class="doc-code">Số: {{{codeTag}}}</div>
                  <div>Ngày in: {{print_date}}</div>
                </div>
              </div>
              <h1 class="doc-title">{{{title}}}</h1>
              <table class="meta-grid">{{{metaRows}}}</table>
              {{{subHtml}}}
              {{{totalHtml}}}
              <div class="sig-row">
                <div class="sig-col"><strong>{{{sig.LeftSign}}}</strong><span>(Ký, ghi rõ họ tên)</span></div>
                <div class="sig-col"><strong>{{{sig.MidSign}}}</strong><span>(Ký, ghi rõ họ tên)</span></div>
                <div class="sig-col"><strong>{{{sig.RightSign}}}</strong><span>(Ký, đóng dấu)</span></div>
              </div>
            </div>
            """;
    }

    internal const string DefaultA4Css = """
        .doc-a4 { font-family: 'Segoe UI', Arial, sans-serif; color: #1f2937; font-size: 13px; line-height: 1.5; }
        .doc-header { display: flex; justify-content: space-between; border-bottom: 2px solid #1a73e8; padding-bottom: 10px; margin-bottom: 14px; }
        .brand { font-size: 14px; font-weight: 700; color: #1a73e8; letter-spacing: 0.4px; }
        .sub { font-size: 11px; color: #5f6368; }
        .doc-meta { text-align: right; font-size: 12px; color: #374151; }
        .doc-code { font-weight: 700; font-size: 13px; }
        .doc-title { text-align: center; font-size: 18px; font-weight: 700; margin: 10px 0 14px; text-transform: uppercase; color: #111827; }
        .meta-grid { width: 100%; border-collapse: collapse; margin-bottom: 14px; }
        .meta-grid td { border: 1px solid #d1d5db; padding: 6px 10px; }
        .meta-grid td.lbl { width: 32%; background: #f8fafc; font-weight: 600; color: #374151; }
        .items { width: 100%; border-collapse: collapse; margin: 12px 0; }
        .items th { background: #1a73e8; color: #fff; padding: 7px 8px; text-align: left; font-size: 12px; }
        .items td { border: 1px solid #d1d5db; padding: 6px 8px; }
        .items td.num { text-align: right; font-variant-numeric: tabular-nums; }
        .items td.center { text-align: center; width: 42px; }
        .summary-box { margin-top: 10px; padding: 10px 12px; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; }
        .total-amt { font-size: 15px; font-weight: 700; color: #1a73e8; }
        .words { margin-top: 4px; font-size: 12px; color: #374151; }
        .sig-row { display: flex; justify-content: space-between; margin-top: 28px; text-align: center; gap: 16px; }
        .sig-col { flex: 1; }
        .sig-col span { display: block; margin-top: 4px; font-size: 11px; color: #6b7280; padding-bottom: 54px; }
        """;

    internal static string SavedViewAnd(string field, string op, string value) =>
        JsonSerializer.Serialize(new
        {
            logic = "AND",
            conditions = new[] { new { field, op, value } }
        });

    internal static TemplateSavedViewSpec ApprovalViewPending(string tableName, string name = "Chờ phê duyệt") =>
        new(tableName, name, false, true, SavedViewAnd("approvalStatus", "eq", "PENDING"));

    internal static TemplateSavedViewSpec ApprovalViewApproved(string tableName, string name = "Đã phê duyệt") =>
        new(tableName, name, false, true, SavedViewAnd("approvalStatus", "eq", "APPROVED"));

    private static List<AppTemplateDefinition> BuildV2Templates(List<AppTemplateDefinition> baseTemplates) =>
        baseTemplates.Select(EnrichTemplateToV2).ToList();

    private static AppTemplateDefinition EnrichTemplateToV2(AppTemplateDefinition tpl)
    {
        return tpl.TemplateKey switch
        {
            "CRM" => WithV2(tpl,
                extraCols: new()
                {
                    ["crm_khach_hang"] = [new() { ColumnCode = "ma_so_thue", ColumnName = "Mã số thuế", DataType = "STRING", Width = 140, OrderIndex = 9 }],
                    ["crm_co_hoi"] = [new() { ColumnCode = "ly_do_ket_thuc", ColumnName = "Lý do chưa chốt / Ghi chú", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 10 }]
                },
                quickCreateCols: new() { ["crm_co_hoi"] = ["ma_kh"] },
                rules: new()
                {
                    ["crm_khach_hang"] = [Rule("fr_crm_kh_email", "email_lh", "required", "dang_hop_tac", "EQUALS", "true", true, false)],
                    ["crm_co_hoi"] =
                    [
                        Rule("fr_crm_loss_vis", "ly_do_ket_thuc", "visible", "giai_doan", "EQUALS", "THAT_BAI", true, false),
                        Rule("fr_crm_won_prob_ro", "xac_suat", "readOnly", "giai_doan", "EQUALS", "CHOT_THANH_CONG", true, false)
                    ]
                },
                workflows: [Wf2Step("crm_co_hoi", "Thẩm định Deal Giá trị lớn (CRM)", "Phê duyệt chính sách chiết khấu và hợp đồng cho cơ hội B2B")],
                automations:
                [
                    new("Thông báo khi có Cơ hội bán hàng mới", "Gửi thông báo realtime cho phụ trách khi phát sinh cơ hội CRM mới", "crm_co_hoi", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Cơ hội mới {{ten_co_hoi}} ({{ma_deal}}) giá trị {{gia_tri}} đ đã được tạo.")
                ],
                prints:
                [
                    new("crm_co_hoi", "Phiếu Đề xuất Báo giá / Cơ hội (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU ĐỀ XUẤT CƠ HỘI KINH DOANH", "SmartApps CRM 2.0", "ma_deal",
                        [
                            ("Tên cơ hội / Dự án", "ten_co_hoi"),
                            ("Khách hàng", "ma_kh"),
                            ("Giai đoạn Pipeline", "giai_doan"),
                            ("Sale phụ trách", "phu_trach"),
                            ("Xác suất chốt (%)", "xac_suat"),
                            ("Ngày dự kiến chốt", "ngay_du_kien")
                        ], totalLabel: "Giá trị Cơ hội dự kiến", totalField: "gia_tri"), DefaultA4Css)
                ],
                views:
                [
                    new("crm_co_hoi", "Cơ hội Đã chốt thành công", false, true, SavedViewAnd("giai_doan", "eq", "CHOT_THANH_CONG"), "gia_tri", "DESC"),
                    new("crm_co_hoi", "Deal đang Đàm phán / Báo giá", false, true, SavedViewAnd("giai_doan", "eq", "DAM_PHAN"), "ngay_du_kien", "ASC"),
                    new("crm_khach_hang", "Khách hàng Thân thiết", false, true, SavedViewAnd("dang_hop_tac", "eq", "true"), "ten_cong_ty", "ASC")
                ],
                bindings: [new("zalo-zns", "crm_co_hoi", new() { ["customer"] = "ma_kh", ["deal_code"] = "ma_deal", ["amount"] = "gia_tri" })]),

            "FINANCE" => WithV2(tpl,
                extraCols: new()
                {
                    ["fin_de_nghi_thanh_toan"] =
                    [
                        new() { ColumnCode = "tai_lieu_giai_trinh", ColumnName = "Chứng từ hóa đơn VAT", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 11 },
                        new() { ColumnCode = "chu_ky_duyet", ColumnName = "Chữ ký xác nhận chi", DataType = "SIGNATURE", ShowInTable = false, Width = 170, OrderIndex = 12 },
                        new() { ColumnCode = "payment_status", ColumnName = "Trạng thái chi", DataType = "STRING", DefaultValue = "UNPAID", Width = 135, OrderIndex = 13 }
                    ],
                    ["fin_tam_ung"] = [new() { ColumnCode = "ghi_chu_hoan_ung", ColumnName = "Chứng từ hoàn ứng / Ghi chú", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 8 }]
                },
                rules: new()
                {
                    ["fin_de_nghi_thanh_toan"] =
                    [
                        Rule("fr_fin_doc_vis", "tai_lieu_giai_trinh", "visible", "thue_vat", "GREATER_THAN", "0", true, false),
                        Rule("fr_fin_sign_vis", "chu_ky_duyet", "visible", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)
                    ],
                    ["fin_tam_ung"] =
                    [
                        Rule("fr_fin_tu_ro", "han_hoan_ung", "readOnly", "da_hoan_ung", "EQUALS", "true", true, false),
                        Rule("fr_fin_tu_note", "ghi_chu_hoan_ung", "visible", "da_hoan_ung", "EQUALS", "true", true, false)
                    ]
                },
                workflows:
                [
                    Wf2Step("fin_de_nghi_thanh_toan", "Phê duyệt Đề nghị Thanh toán (2 bước)", "Kế toán trưởng kiểm tra chứng từ trước khi Ban Giám đốc duyệt chi"),
                    Wf2Step("fin_tam_ung", "Phê duyệt Tạm ứng Công tác", "Phê duyệt hạn mức tạm ứng và thời hạn quyết toán")
                ],
                automations:
                [
                    new("Thông báo Đề nghị Thanh toán được duyệt", "Báo realtime tới người đề nghị khi phiếu thanh toán được duyệt chi", "fin_de_nghi_thanh_toan", "RECORD_UPDATED", "trang_thai_duyet", "EQUALS", "DA_DUYET", null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Đề nghị thanh toán {{ma_phieu}} ({{noi_dung}}) đã được phê duyệt chi {{tong_cong}} đ.")
                ],
                prints:
                [
                    new("fin_de_nghi_thanh_toan", "Phiếu Đề nghị Thanh toán Chuẩn (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU ĐỀ NGHỊ THANH TOÁN", "SmartApps Finance 2.0", "ma_phieu",
                        [
                            ("Nội dung thanh toán", "noi_dung"),
                            ("Khoản mục chi phí", "loai_chi_phi"),
                            ("Người đề nghị", "nguoi_de_nghi"),
                            ("Ngày đề nghị", "ngay_de_nghi"),
                            ("Thuế VAT", "thue_vat"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "bang_ke",
                        [("Hạng mục chứng từ", "hang_muc", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "so_tien", true), ("Ghi chú", "ghi_chu", false)],
                        "Tổng tiền đề nghị thanh toán", "tong_cong", ("Người đề nghị", "Kế toán trưởng", "Giám đốc duyệt chi")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("fin_de_nghi_thanh_toan", "Phiếu Chờ duyệt chi"),
                    ApprovalViewApproved("fin_de_nghi_thanh_toan", "Phiếu Đã duyệt chi"),
                    new("fin_tam_ung", "Tạm ứng Chưa quyết toán", false, true, SavedViewAnd("da_hoan_ung", "eq", "false"), "han_hoan_ung", "ASC")
                ],
                bindings:
                [
                    new("vietqr", "fin_de_nghi_thanh_toan", new() { ["amount"] = "tong_cong", ["orderCode"] = "ma_phieu", ["description"] = "noi_dung", ["paymentStatus"] = "payment_status" }),
                    new("vnpt-invoice", "fin_de_nghi_thanh_toan", new() { ["code"] = "ma_phieu", ["total"] = "tong_cong" })
                ]),

            "PURCHASE" => WithV2(tpl,
                extraCols: new()
                {
                    ["pur_don_mua_hang"] =
                    [
                        new() { ColumnCode = "so_hd_vat", ColumnName = "Số Hóa đơn VAT NCC", DataType = "STRING", ShowInTable = false, Width = 160, OrderIndex = 10 },
                        new() { ColumnCode = "bien_ban_nghiem_thu", ColumnName = "Biên bản nghiệm thu (MULTI_FILE)", DataType = "MULTI_FILE", ShowInTable = false, Width = 210, OrderIndex = 11 }
                    ]
                },
                quickCreateCols: new() { ["pur_don_mua_hang"] = ["ma_ncc"] },
                rules: new()
                {
                    ["pur_nha_cung_cap"] = [Rule("fr_pur_ncc_email", "email_ncc", "required", "danh_gia", "GREATER_OR_EQUAL", "4", true, false)],
                    ["pur_don_mua_hang"] =
                    [
                        Rule("fr_pur_vat_vis", "so_hd_vat", "visible", "tien_do_po", "NOT_EQUALS", "MOI_TAO", true, false),
                        Rule("fr_pur_bbnt_vis", "bien_ban_nghiem_thu", "visible", "tien_do_po", "EQUALS", "DA_NHAP_KHO", true, false)
                    ]
                },
                workflows: [Wf2Step("pur_don_mua_hang", "Phê duyệt Đơn Đặt hàng (PO)", "Quản lý mua hàng rà soát báo giá trước khi Giám đốc duyệt PO")],
                automations:
                [
                    new("Cảnh báo Đơn mua hàng mới chờ duyệt", "Gửi thông báo ngay khi phát sinh Đơn đặt hàng PO mới", "pur_don_mua_hang", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Đơn đặt hàng {{ma_po}} ({{tieu_de}}) tổng trị giá {{tong_gia_tri}} đ đang chờ phê duyệt.")
                ],
                prints:
                [
                    new("pur_don_mua_hang", "Đơn Đặt Hàng Chuẩn (PO - A4)", "A4", "portrait", true, true, false,
                        PrintA4("ĐƠN ĐẶT HÀNG (PURCHASE ORDER)", "SmartApps Purchase 2.0", "ma_po",
                        [
                            ("Nội dung mua sắm", "tieu_de"),
                            ("Nhà cung cấp", "ma_ncc"),
                            ("Người đề xuất", "nguoi_yeu_cau"),
                            ("Ngày cần hàng", "ngay_can_hang"),
                            ("Tiến độ PO", "tien_do_po"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "chi_tiet_hang",
                        [("Tên mặt hàng", "ten_hang", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                        "Tổng giá trị PO", "tong_gia_tri", ("Bộ phận Mua hàng", "Kiểm soát tài chính", "Giám đốc ký duyệt")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("pur_don_mua_hang", "PO Chờ phê duyệt"),
                    new("pur_don_mua_hang", "PO Đã đặt hàng NCC", false, true, SavedViewAnd("tien_do_po", "eq", "DA_DAT_HANG"), "ngay_can_hang", "ASC"),
                    new("pur_nha_cung_cap", "NCC Đánh giá xuất sắc (5 sao)", false, true, SavedViewAnd("danh_gia", "eq", "5"), "ten_ncc", "ASC")
                ],
                bindings: [new("vietqr", "pur_don_mua_hang", new() { ["amount"] = "tong_gia_tri", ["orderCode"] = "ma_po", ["description"] = "tieu_de" })]),

            "WMS" => WithV2(tpl,
                extraCols: new()
                {
                    ["wms_phieu_kho"] =
                    [
                        new() { ColumnCode = "nha_cung_cap", ColumnName = "Nhà cung cấp giao hàng", DataType = "STRING", ShowInTable = false, Width = 200, OrderIndex = 8 },
                        new() { ColumnCode = "ly_do_xuat", ColumnName = "Lý do / Bộ phận nhận xuất kho", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 9 }
                    ]
                },
                rules: new()
                {
                    ["wms_hang_hoa"] = [Rule("fr_wms_min_req", "ton_toi_thieu", "required", "ton_kho", "GREATER_OR_EQUAL", "0", true, false)],
                    ["wms_phieu_kho"] =
                    [
                        Rule("fr_wms_in_ncc", "nha_cung_cap", "visible", "loai_phieu", "EQUALS", "NHAP_KHO", true, false),
                        Rule("fr_wms_out_lydo", "ly_do_xuat", "visible", "loai_phieu", "EQUALS", "XUAT_KHO", true, false)
                    ]
                },
                workflows: [Wf2Step("wms_phieu_kho", "Phê duyệt Chứng từ Nhập / Xuất kho", "Thủ kho kiểm đếm vật tư và Quản lý vận hành phê duyệt xuất/nhập", "Thủ kho kiểm đếm thực tế", ["ASSET_LEAD", "HR_MANAGER"])],
                automations:
                [
                    new("Thông báo lập Chứng từ Kho mới", "Thông báo cho thủ kho & quản lý khi có phiếu nhập/xuất mới", "wms_phieu_kho", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Chứng từ kho {{ma_phieu}} ({{loai_phieu}}) trị giá {{tong_gia_tri}} đ đã được lập.")
                ],
                prints:
                [
                    new("wms_phieu_kho", "Phiếu Nhập / Xuất Kho (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU NHẬP / XUẤT KHO VẬT TƯ", "SmartApps WMS 2.0", "ma_phieu",
                        [
                            ("Loại chứng từ", "loai_phieu"),
                            ("Ngày chứng từ", "ngay_chung_tu"),
                            ("Nhân viên lập phiếu", "nguoi_lap"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "chi_tiet_vt",
                        [("Mã SKU", "ma_sku", false), ("Tên vật tư", "ten_hang", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                        "Tổng giá trị chứng từ", "tong_gia_tri", ("Người lập phiếu", "Thủ kho kiểm nhận", "Quản lý phê duyệt")), DefaultA4Css)
                ],
                views:
                [
                    new("wms_hang_hoa", "Hàng hóa Cần nhập thêm", false, true, SavedViewAnd("canh_bao", "eq", "CAN_NHAP_THEM"), "ton_kho", "ASC"),
                    ApprovalViewPending("wms_phieu_kho", "Phiếu kho Chờ duyệt"),
                    new("wms_phieu_kho", "Phiếu Nhập kho", false, true, SavedViewAnd("loai_phieu", "eq", "NHAP_KHO"), "ngay_chung_tu", "DESC")
                ]),

            "PROJECT" => WithV2(tpl,
                extraCols: new()
                {
                    ["prj_du_an"] = [new() { ColumnCode = "bien_ban_nghiem_thu", ColumnName = "Biên bản nghiệm thu dự án", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 9 }],
                    ["prj_cong_viec"] = [new() { ColumnCode = "ket_qua_thuc_hien", ColumnName = "Báo cáo kết quả hoàn thành", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 9 }]
                },
                quickCreateCols: new() { ["prj_cong_viec"] = ["ma_du_an"] },
                rules: new()
                {
                    ["prj_du_an"] =
                    [
                        Rule("fr_prj_bbnt_vis", "bien_ban_nghiem_thu", "visible", "trang_thai", "EQUALS", "HOAN_THANH", true, false),
                        Rule("fr_prj_budget_ro", "ngan_sach", "readOnly", "trang_thai", "EQUALS", "HOAN_THANH", true, false)
                    ],
                    ["prj_cong_viec"] = [Rule("fr_prj_task_res", "ket_qua_thuc_hien", "visible", "trang_thai", "EQUALS", "DONE", true, false)]
                },
                workflows: [Wf2Step("prj_du_an", "Nghiệm thu & Chốt ngân sách Dự án", "PM và Ban Giám đốc nghiệm thu hồ sơ hoàn thành dự án")],
                automations:
                [
                    new("Thông báo giao Task Dự án mới", "Tự động báo cho người phụ trách khi phát sinh công việc dự án mới", "prj_cong_viec", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Task dự án mới {{ma_task}}: {{ten_cong_viec}} (Hạn {{han_hoan_thanh}}).")
                ],
                prints:
                [
                    new("prj_du_an", "Biên bản Hồ sơ Dự án (A4)", "A4", "portrait", true, false, false,
                        PrintA4("HỒ SƠ TRIỂN KHAI & NGHIỆM THU DỰ ÁN", "SmartApps Project 2.0", "ma_du_an",
                        [
                            ("Tên dự án", "ten_du_an"),
                            ("Quản lý dự án (PM)", "quan_ly_du_an"),
                            ("Ngày bắt đầu", "ngay_bat_dau"),
                            ("Hạn bàn giao", "han_ban_giao"),
                            ("Thời lượng (ngày)", "thoi_luong_ngay"),
                            ("Trạng thái", "trang_thai")
                        ], totalLabel: "Ngân sách dự kiến", totalField: "ngan_sach",
                        signs: ("PM Dự án", "Kiểm soát chất lượng", "Giám đốc duyệt")), DefaultA4Css)
                ],
                views:
                [
                    new("prj_du_an", "Dự án Đang triển khai", false, true, SavedViewAnd("trang_thai", "eq", "DANG_CHAY"), "han_ban_giao", "ASC"),
                    new("prj_cong_viec", "Task Đang thực hiện", false, true, SavedViewAnd("trang_thai", "eq", "IN_PROGRESS"), "han_hoan_thanh", "ASC"),
                    new("prj_cong_viec", "Task Chờ Review", false, true, SavedViewAnd("trang_thai", "eq", "REVIEW"))
                ]),

            "RECRUIT" => WithV2(tpl,
                extraCols: new()
                {
                    ["rec_dot_tuyen_dung"] = [new() { ColumnCode = "mo_ta_yeu_cau", ColumnName = "Yêu cầu năng lực (JD)", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 9 }],
                    ["rec_ung_vien"] =
                    [
                        new() { ColumnCode = "muc_luong_offer", ColumnName = "Mức lương Offer (VNĐ)", DataType = "CURRENCY", ShowInTable = false, Width = 175, OrderIndex = 11 },
                        new() { ColumnCode = "ly_do_loai", ColumnName = "Lý do không phù hợp", DataType = "TEXTAREA", ShowInTable = false, Width = 210, OrderIndex = 12 }
                    ]
                },
                quickCreateCols: new() { ["rec_ung_vien"] = ["dot_tuyen_dung"] },
                rules: new()
                {
                    ["rec_dot_tuyen_dung"] = [Rule("fr_rec_jd_vis", "mo_ta_yeu_cau", "visible", "so_luong", "GREATER_OR_EQUAL", "1", true, false)],
                    ["rec_ung_vien"] =
                    [
                        Rule("fr_rec_offer_vis", "muc_luong_offer", "visible", "vong_tuyen", "EQUALS", "OFFER", true, false),
                        Rule("fr_rec_reject_vis", "ly_do_loai", "visible", "vong_tuyen", "EQUALS", "LOAI", true, false)
                    ]
                },
                workflows: [Wf2Step("rec_dot_tuyen_dung", "Phê duyệt Đợt Tuyển dụng (HR -> GĐ)", "Trưởng phòng Nhân sự xác nhận định biên trước khi Giám đốc duyệt mở tuyển dụng", "Trưởng phòng Nhân sự xác nhận", ["HR_MANAGER"])],
                automations:
                [
                    new("Thông báo Ứng viên mới vào Pipeline", "Thông báo HR khi có hồ sơ ứng viên mới đăng ký", "rec_ung_vien", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Ứng viên mới {{ho_ten}} ({{ma_uv}}) vừa ứng tuyển vị trí {{dot_tuyen_dung}}.")
                ],
                prints:
                [
                    new("rec_ung_vien", "Phiếu Đánh giá & Phỏng vấn Ứng viên (A4)", "A4", "portrait", true, false, false,
                        PrintA4("PHIẾU ĐÁNH GIÁ PHỎNG VẤN ỨNG VIÊN", "SmartApps Recruit 2.0", "ma_uv",
                        [
                            ("Họ và Tên ứng viên", "ho_ten"),
                            ("Vị trí ứng tuyển", "dot_tuyen_dung"),
                            ("Email / Điện thoại", "email"),
                            ("Vòng tuyển dụng", "vong_tuyen"),
                            ("Ngày phỏng vấn", "ngay_pv"),
                            ("Nhận xét phỏng vấn", "nhan_xet")
                        ], totalLabel: "Mức lương đề xuất", totalField: "luong_mong_muon",
                        signs: ("Chuyên viên HR", "Trưởng bộ phận CM", "Giám đốc duyệt Offer")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("rec_dot_tuyen_dung", "Job Chờ duyệt mở tuyển"),
                    new("rec_ung_vien", "Ứng viên Vòng Phỏng vấn", false, true, SavedViewAnd("vong_tuyen", "eq", "PHONG_VAN"), "ngay_pv", "ASC"),
                    new("rec_ung_vien", "Ứng viên Đã gửi Offer", false, true, SavedViewAnd("vong_tuyen", "eq", "OFFER"))
                ]),

            "CONTRACT" => WithV2(tpl,
                extraCols: new()
                {
                    ["ctr_hop_dong"] =
                    [
                        new() { ColumnCode = "file_hop_dong", ColumnName = "Bản scan Hợp đồng đã ký", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 12 },
                        new() { ColumnCode = "chu_ky_dai_dien", ColumnName = "Chữ ký xác nhận nội bộ", DataType = "SIGNATURE", ShowInTable = false, Width = 175, OrderIndex = 13 }
                    ]
                },
                rules: new()
                {
                    ["ctr_hop_dong"] =
                    [
                        Rule("fr_ctr_file_vis", "file_hop_dong", "visible", "tinh_trang_hd", "NOT_EQUALS", "SOAN_THAO", true, false),
                        Rule("fr_ctr_sign_vis", "chu_ky_dai_dien", "visible", "tinh_trang_hd", "EQUALS", "DANG_HIEU_LUC", true, false),
                        Rule("fr_ctr_val_ro", "gia_tri", "readOnly", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)
                    ]
                },
                workflows: [Wf2Step("ctr_hop_dong", "Thẩm định Pháp chế & Ký Hợp đồng", "Pháp chế/Kế toán rà soát điều khoản trước khi Đại diện pháp luật ký kết")],
                automations:
                [
                    new("Thông báo Hợp đồng chuyển Chờ ký", "Thông báo cho người phụ trách khi hợp đồng chuyển sang trạng thái Chờ ký", "ctr_hop_dong", "RECORD_UPDATED", "tinh_trang_hd", "EQUALS", "CHO_KY", null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Hợp đồng {{so_hd}} ({{ten_hop_dong}}) với đối tác {{doi_tac}} đã sẵn sàng ký kết.")
                ],
                prints:
                [
                    new("ctr_hop_dong", "Phiếu Trình Ký Hợp đồng Kinh tế (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU TRÌNH KÝ HỢP ĐỒNG KINH TẾ", "SmartApps Contract 2.0", "so_hd",
                        [
                            ("Tên hợp đồng", "ten_hop_dong"),
                            ("Đối tác / Khách hàng", "doi_tac"),
                            ("Loại hợp đồng", "loai_hd"),
                            ("Người phụ trách", "nguoi_phu_trach"),
                            ("Ngày ký / Hết hạn", "ngay_ky"),
                            ("Tình trạng pháp lý", "tinh_trang_hd")
                        ], totalLabel: "Tổng giá trị hợp đồng", totalField: "gia_tri",
                        signs: ("Người trình ký", "Pháp chế thẩm định", "Ban Giám đốc")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("ctr_hop_dong", "HĐ Chờ phê duyệt Pháp chế"),
                    new("ctr_hop_dong", "Hợp đồng Đang hiệu lực", false, true, SavedViewAnd("tinh_trang_hd", "eq", "DANG_HIEU_LUC"), "ngay_het_han", "ASC"),
                    new("ctr_hop_dong", "Hợp đồng Chờ ký kết", false, true, SavedViewAnd("tinh_trang_hd", "eq", "CHO_KY"))
                ],
                bindings: [new("vnpt-smartca", "ctr_hop_dong", new() { ["documentCode"] = "so_hd", ["signer"] = "nguoi_phu_trach", ["signature"] = "chu_ky_dai_dien" })]),

            "HELPDESK" => WithV2(tpl,
                extraCols: new()
                {
                    ["cs_ticket"] = [new() { ColumnCode = "giai_phap_xu_ly", ColumnName = "Phương án & Kết quả xử lý", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 12 }]
                },
                rules: new()
                {
                    ["cs_ticket"] =
                    [
                        Rule("fr_hd_sol_vis", "giai_phap_xu_ly", "visible", "trang_thai", "EQUALS", "DA_GIAI_QUYET", true, false),
                        Rule("fr_hd_csat_vis", "diem_csat", "visible", "trang_thai", "EQUALS", "DA_GIAI_QUYET", true, false)
                    ]
                },
                workflows: [Wf2Step("cs_ticket", "Quy trình Escalate Ticket SLA Cao", "Xác nhận đóng ticket phức tạp hoặc hoàn phí khách hàng")],
                automations:
                [
                    new("Thông báo Ticket Hỗ trợ Khách hàng mới", "Cảnh báo CSKH ngay khi khách gửi yêu cầu qua Web Form hoặc Hotline", "cs_ticket", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Ticket mới {{ma_ticket}}: {{tieu_de}} từ khách {{ten_khach_hang}} (Hạn SLA: {{han_sla}}).")
                ],
                prints:
                [
                    new("cs_ticket", "Phiếu Biên nhận Xử lý Hỗ trợ (A4)", "A4", "portrait", true, false, false,
                        PrintA4("BIÊN BẢN TIẾP NHẬN & XỬ LÝ TICKET", "SmartApps Helpdesk 2.0", "ma_ticket",
                        [
                            ("Tiêu đề vấn đề", "tieu_de"),
                            ("Tên Khách hàng", "ten_khach_hang"),
                            ("Email liên hệ", "email_kh"),
                            ("Kênh tiếp nhận", "kenh_tiep_nhan"),
                            ("Chuyên viên CSKH", "nhan_vien_ho_tro"),
                            ("Trạng thái xử lý", "trang_thai"),
                            ("Nội dung chi tiết", "chi_tiet")
                        ], signs: ("Khách hàng", "Chuyên viên CSKH", "Trưởng bộ phận")), DefaultA4Css)
                ],
                views:
                [
                    new("cs_ticket", "Ticket Mới tiếp nhận", false, true, SavedViewAnd("trang_thai", "eq", "TIEP_NHAN"), "han_sla", "ASC"),
                    new("cs_ticket", "Ticket Đang xử lý", false, true, SavedViewAnd("trang_thai", "eq", "DANG_XU_LY"), "han_sla", "ASC"),
                    new("cs_ticket", "Ticket Đã giải quyết", false, true, SavedViewAnd("trang_thai", "eq", "DA_GIAI_QUYET"))
                ],
                bindings: [new("zalo-zns", "cs_ticket", new() { ["ticketCode"] = "ma_ticket", ["customer"] = "ten_khach_hang", ["status"] = "trang_thai" })]),

            "MEETING" => WithV2(tpl,
                extraCols: new()
                {
                    ["adm_dat_phong"] = [new() { ColumnCode = "yeu_cau_thiet_bi", ColumnName = "Chi tiết thiết bị / Tea-break", DataType = "STRING", ShowInTable = false, Width = 220, OrderIndex = 10 }],
                    ["adm_dieu_xe"] = [new() { ColumnCode = "bien_lai_cau_duong", ColumnName = "Ảnh vé cầu đường / Xăng dầu", DataType = "MULTI_FILE", ShowInTable = false, Width = 210, OrderIndex = 8 }]
                },
                rules: new()
                {
                    ["adm_dat_phong"] = [Rule("fr_mtg_eq_vis", "yeu_cau_thiet_bi", "visible", "can_may_chieu", "EQUALS", "true", true, false)],
                    ["adm_dieu_xe"] =
                    [
                        Rule("fr_mtg_car_ro", "chi_phi_du_kien", "readOnly", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false),
                        Rule("fr_mtg_rec_vis", "bien_lai_cau_duong", "visible", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)
                    ]
                },
                workflows: [Wf2Step("adm_dieu_xe", "Phê duyệt Lệnh Điều xe Công tác", "Hành chính bố trí xe & Quản lý phê duyệt lộ trình công tác")],
                automations:
                [
                    new("Thông báo Xác nhận Đặt phòng họp", "Gửi thông báo nhắc lịch khi đặt phòng họp mới", "adm_dat_phong", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Đã ghi nhận lịch họp {{tieu_de_hop}} tại {{phong_hop}} ngày {{ngay_hop}} ({{khung_gio}}).")
                ],
                prints:
                [
                    new("adm_dieu_xe", "Lệnh Điều xe Công tác (A4)", "A4", "portrait", true, true, false,
                        PrintA4("LỆNH ĐIỀU XE CÔNG TÁC", "SmartApps Meeting & Admin 2.0", "ma_chuyen",
                        [
                            ("Lộ trình công tác", "lo_trinh"),
                            ("Người đăng ký", "nguoi_di"),
                            ("Ngày khởi hành", "ngay_di"),
                            ("Quãng đường dự kiến (KM)", "so_km"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], totalLabel: "Chi phí xăng xe / cầu đường dự kiến", totalField: "chi_phi_du_kien",
                        signs: ("Người đăng ký", "Điều phối hành chính", "Lãnh đạo phê duyệt")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("adm_dieu_xe", "Chuyến xe Chờ duyệt"),
                    new("adm_dat_phong", "Lịch phòng Đã xác nhận", false, true, SavedViewAnd("trang_thai", "eq", "DA_XAC_NHAN"), "ngay_hop", "ASC")
                ]),

            "TRAINING" => WithV2(tpl,
                extraCols: new()
                {
                    ["ld_khoa_hoc"] = [new() { ColumnCode = "tai_lieu_dao_tao", ColumnName = "Tài liệu / Slide đào tạo", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 9 }],
                    ["ld_dang_ky_hoc"] = [new() { ColumnCode = "chung_chi_hoc_vien", ColumnName = "Chứng chỉ hoàn thành", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 8 }]
                },
                quickCreateCols: new() { ["ld_dang_ky_hoc"] = ["khoa_hoc"] },
                rules: new()
                {
                    ["ld_khoa_hoc"] = [Rule("fr_trn_mat_vis", "tai_lieu_dao_tao", "visible", "thoi_luong_gio", "GREATER_OR_EQUAL", "4", true, false)],
                    ["ld_dang_ky_hoc"] = [Rule("fr_trn_cert_vis", "chung_chi_hoc_vien", "visible", "trang_thai", "EQUALS", "HOAN_THANH", true, false)]
                },
                workflows: [Wf2Step("ld_khoa_hoc", "Phê duyệt Kế hoạch & Ngân sách Đào tạo", "HR L&D và Ban Giám đốc duyệt mở khóa học nội bộ", "Phòng Đào tạo & HR", ["HR_MANAGER"])],
                automations:
                [
                    new("Thông báo Đăng ký Khóa học mới", "Thông báo cho học viên khi ghi danh khóa đào tạo nội bộ", "ld_dang_ky_hoc", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Bạn đã ghi danh thành công khóa học {{khoa_hoc}} (Mã {{ma_dk}}).")
                ],
                prints:
                [
                    new("ld_dang_ky_hoc", "Phiếu Chứng nhận Kết quả Đào tạo (A4)", "A4", "portrait", true, false, false,
                        PrintA4("PHIẾU KẾT QUẢ ĐÀO TẠO NỘI BỘ", "SmartApps Learning 2.0", "ma_dk",
                        [
                            ("Khóa học", "khoa_hoc"),
                            ("Học viên", "hoc_vien"),
                            ("Ngày đăng ký", "ngay_dk"),
                            ("Điểm kiểm tra (/100)", "diem_thi"),
                            ("Kết quả đánh giá", "ket_qua"),
                            ("Tiến độ học", "trang_thai")
                        ], signs: ("Học viên", "Giảng viên hướng dẫn", "Giám đốc Nhân sự")), DefaultA4Css)
                ],
                views:
                [
                    new("ld_khoa_hoc", "Khóa học Bắt buộc", false, true, SavedViewAnd("bat_buoc", "eq", "true"), "ngay_khai_giang", "ASC"),
                    new("ld_dang_ky_hoc", "Học viên Đã hoàn thành", false, true, SavedViewAnd("trang_thai", "eq", "HOAN_THANH"))
                ]),

            "KPI_OKR" => WithV2(tpl,
                extraCols: new()
                {
                    ["okr_muc_tieu"] = [new() { ColumnCode = "ke_hoach_cai_thien", ColumnName = "Kế hoạch cải thiện KPI (<80%)", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 11 }]
                },
                rules: new()
                {
                    ["okr_muc_tieu"] =
                    [
                        Rule("fr_okr_target_req", "muc_tieu_so", "required", "chu_ky", "NOT_EMPTY", null, true, null),
                        Rule("fr_okr_plan_vis", "ke_hoach_cai_thien", "visible", "thuc_dat", "LESS_THAN", "80", true, false)
                    ]
                },
                workflows: [Wf2Step("okr_muc_tieu", "Đánh giá & Chốt Kết quả OKR cuối kỳ", "Trưởng phòng & Ban Giám đốc xác nhận kết quả KPI thực đạt")],
                automations:
                [
                    new("Thông báo Cập nhật Tiến độ OKR", "Thông báo khi mục tiêu OKR/KPI được cập nhật tiến độ thực đạt", "okr_muc_tieu", "RECORD_UPDATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Chỉ số {{ma_okr}} ({{muc_tieu}}) vừa cập nhật đạt {{ty_le_hoan_thanh}}% ({{xep_loai}}).")
                ],
                prints:
                [
                    new("okr_muc_tieu", "Phiếu Đánh giá Kết quả OKR / KPI (A4)", "A4", "portrait", true, false, false,
                        PrintA4("PHIẾU ĐÁNH GIÁ MỤC TIÊU OKR & KPI", "SmartApps OKR 2.0", "ma_okr",
                        [
                            ("Tên Mục tiêu / KPI", "muc_tieu"),
                            ("Chu kỳ đánh giá", "chu_ky"),
                            ("Phòng ban", "phong_ban"),
                            ("Người chịu trách nhiệm", "phu_trach"),
                            ("Chỉ tiêu kế hoạch", "muc_tieu_so"),
                            ("Kết quả thực đạt", "thuc_dat"),
                            ("Hoàn thành (%)", "ty_le_hoan_thanh"),
                            ("Xếp loại", "xep_loai")
                        ], signs: ("Nhân sự phụ trách", "Trưởng phòng", "Ban Giám đốc")), DefaultA4Css)
                ],
                views:
                [
                    new("okr_muc_tieu", "OKR Chu kỳ Q4/2026", false, true, SavedViewAnd("chu_ky", "eq", "Q4_2026"), "ty_le_hoan_thanh", "DESC"),
                    new("okr_muc_tieu", "OKR Chưa đạt chuẩn (<80%)", false, true, SavedViewAnd("xep_loai", "eq", "CAN_NO_LUC"))
                ]),

            "FLEET" => WithV2(tpl,
                extraCols: new()
                {
                    ["flt_phuong_tien"] = [new() { ColumnCode = "giay_dang_kiem", ColumnName = "Ảnh Giấy đăng kiểm / Bảo hiểm", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 7 }],
                    ["flt_bao_tri"] = [new() { ColumnCode = "hoa_don_garage", ColumnName = "Hóa đơn & Chứng từ Garage", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 8 }]
                },
                quickCreateCols: new() { ["flt_bao_tri"] = ["bien_so"] },
                rules: new()
                {
                    ["flt_phuong_tien"] = [Rule("fr_flt_dk_req", "han_dang_kiem", "required", "so_km", "GREATER_THAN", "0", true, false)],
                    ["flt_bao_tri"] = [Rule("fr_flt_inv_vis", "hoa_don_garage", "visible", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)]
                },
                workflows: [Wf2Step("flt_bao_tri", "Phê duyệt Chi phí Bảo dưỡng & Sửa chữa Xe", "Quản lý đội xe kiểm tra báo giá trước khi duyệt thanh toán garage", "Quản lý Đội xe kiểm tra", ["ASSET_LEAD"])],
                automations:
                [
                    new("Thông báo Lập Phiếu Bảo dưỡng Xe", "Cảnh báo đội xe & tài chính khi phát sinh phiếu sửa chữa/đăng kiểm", "flt_bao_tri", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Phiếu bảo dưỡng {{ma_phieu}} cho xe {{bien_so}} trị giá {{tong_chi_phi}} đ đang chờ duyệt.")
                ],
                prints:
                [
                    new("flt_bao_tri", "Phiếu Đề nghị Bảo dưỡng Đội xe (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU BẢO DƯỠNG & SỬA CHỮA PHƯƠNG TIỆN", "SmartApps Fleet 2.0", "ma_phieu",
                        [
                            ("Phương tiện", "bien_so"),
                            ("Loại bảo dưỡng", "loai_bao_tri"),
                            ("Ngày thực hiện", "ngay_thuc_hien"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "chi_tiet_phu_tung",
                        [("Hạng mục phụ tùng / công", "hang_muc", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                        "Tổng chi phí bảo dưỡng", "tong_chi_phi", ("Tài xế / Người đề xuất", "Quản lý đội xe", "Giám đốc duyệt chi")), DefaultA4Css)
                ],
                views:
                [
                    new("flt_phuong_tien", "Xe Sẵn sàng điều động", false, true, SavedViewAnd("tinh_trang", "eq", "SAN_SANG"), "bien_so", "ASC"),
                    ApprovalViewPending("flt_bao_tri", "Phiếu Bảo trì Chờ duyệt")
                ]),

            "PROCURE_VN" => WithV2(tpl,
                rules: new()
                {
                    ["proc_de_xuat"] =
                    [
                        Rule("fr_pvn_quote_vis", "bao_gia", "visible", "loai_mua", "NOT_EQUALS", "VPP", true, false),
                        Rule("fr_pvn_pay_ro", "payment_status", "readOnly", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)
                    ]
                },
                workflows: [Wf2Step("proc_de_xuat", "Phê duyệt Đề xuất Mua sắm VietQR (2 bước)", "Quản lý mua sắm & Kế toán rà soát báo giá trước khi Giám đốc duyệt chi")],
                automations:
                [
                    new("Thông báo Đề xuất Mua sắm VietQR được duyệt", "Thông báo cho người đề xuất khi phiếu mua sắm được duyệt và sẵn sàng quét VietQR", "proc_de_xuat", "RECORD_UPDATED", "trang_thai_duyet", "EQUALS", "DA_DUYET", null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Đề xuất {{ma_dx}} ({{tieu_de}}) tổng tiền {{tong_tien}} đ đã được phê duyệt!")
                ],
                prints:
                [
                    new("proc_de_xuat", "Phiếu Đề xuất Mua sắm & VietQR (A4)", "A4", "portrait", true, true, false,
                        PrintA4("PHIẾU ĐỀ XUẤT MUA SẮM HÀNG HÓA", "Giải pháp Doanh nghiệp VN 2.0", "ma_dx",
                        [
                            ("Nội dung đề xuất", "tieu_de"),
                            ("Loại mua sắm", "loai_mua"),
                            ("Người đề xuất", "nguoi_de_xuat"),
                            ("Trạng thái thanh toán", "payment_status"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "chi_tiet",
                        [("Tên hàng hóa / Dịch vụ", "ten_hang", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                        "Tổng tiền thanh toán", "tong_tien", ("Người đề xuất", "Kế toán kiểm tra", "Giám đốc phê duyệt")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("proc_de_xuat", "Đề xuất Chờ duyệt"),
                    new("proc_de_xuat", "Chờ thanh toán VietQR", false, true, SavedViewAnd("payment_status", "eq", "UNPAID"))
                ],
                bindings:
                [
                    new("vietqr", "proc_de_xuat", new() { ["amount"] = "tong_tien", ["orderCode"] = "ma_dx", ["description"] = "tieu_de", ["paymentStatus"] = "payment_status" }),
                    new("vnpt-invoice", "proc_de_xuat", new() { ["code"] = "ma_dx", ["total"] = "tong_tien" })
                ]),

            "SALES_VN" => WithV2(tpl,
                extraCols: new()
                {
                    ["sales_don_hang"] =
                    [
                        new() { ColumnCode = "dia_chi_giao", ColumnName = "Địa chỉ giao hàng", DataType = "STRING", ShowInTable = false, Width = 220, OrderIndex = 7 },
                        new() { ColumnCode = "ngay_giao_hang", ColumnName = "Ngày giao dự kiến", DataType = "DATE", ShowInTable = false, Width = 140, OrderIndex = 8 },
                        new() { ColumnCode = "chu_ky_khach", ColumnName = "Chữ ký khách nhận hàng", DataType = "SIGNATURE", ShowInTable = false, Width = 165, OrderIndex = 9 }
                    ]
                },
                rules: new()
                {
                    ["sales_don_hang"] =
                    [
                        Rule("fr_svn_ship_vis", "dia_chi_giao", "visible", "sdt", "NOT_EMPTY", null, true, false),
                        Rule("fr_svn_sign_vis", "chu_ky_khach", "visible", "payment_status", "EQUALS", "PAID", true, false)
                    ]
                },
                workflows: [Wf2Step("sales_don_hang", "Xác nhận Đơn hàng & Đối soát Thanh toán", "Xác nhận công nợ / thanh toán trước khi xuất kho giao hàng")],
                automations:
                [
                    new("Thông báo Đơn hàng Bán mới", "Gửi thông báo ngay khi phát sinh đơn hàng mới", "sales_don_hang", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Đơn hàng {{ma_don}} của {{khach_hang}} tổng tiền {{tong_tien}} đ vừa được tạo.")
                ],
                prints:
                [
                    new("sales_don_hang", "Hóa đơn Bán hàng Nhiệt K80", "K80", "portrait", true, false, false,
                        """
                        <div class="k80-doc">
                          <div class="k80-head"><strong>SMARTAPPS RETAIL 2.0</strong><div>PHIẾU TÍNH TIỀN</div><div>Số: {{ma_don}} | {{print_date}}</div></div>
                          <div class="k80-cust">KH: {{khach_hang}}<br/>SĐT: {{sdt}} | TT: {{payment_status}}</div>
                          <table class="k80-tbl"><thead><tr><th>SP</th><th>SL</th><th>T.Tiền</th></tr></thead><tbody>{{#each chi_tiet}}<tr><td>{{ten_hang}}</td><td>{{so_luong}}</td><td style="text-align:right">{{money thanh_tien}}</td></tr>{{/each}}</tbody></table>
                          <div class="k80-tot">TỔNG CỘNG: {{money tong_tien}} đ</div>
                          <div class="k80-words">Bằng chữ: {{money_words tong_tien}}</div>
                        </div>
                        """,
                        ".k80-doc{font-family:monospace;font-size:11px;width:100%;}.k80-head{text-align:center;border-bottom:1px dashed #333;padding-bottom:6px;margin-bottom:6px;}.k80-tbl{width:100%;border-collapse:collapse;margin:6px 0;}.k80-tbl th,.k80-tbl td{border-bottom:1px dotted #999;padding:3px 2px;font-size:11px;}.k80-tot{font-weight:700;font-size:13px;text-align:right;margin-top:6px;}.k80-words{font-size:10px;font-style:italic;margin-top:4px;}"),
                    new("sales_don_hang", "Phiếu Giao hàng & Hóa đơn A4", "A4", "portrait", false, true, false,
                        PrintA4("PHIẾU GIAO HÀNG & THANH TOÁN ĐƠN HÀNG", "SmartApps Sales VN 2.0", "ma_don",
                        [
                            ("Khách hàng", "khach_hang"),
                            ("Số điện thoại", "sdt"),
                            ("Địa chỉ giao hàng", "dia_chi_giao"),
                            ("Trạng thái thanh toán", "payment_status")
                        ], "chi_tiet",
                        [("Sản phẩm đặt mua", "ten_hang", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                        "Tổng tiền thanh toán", "tong_tien", ("Nhân viên bán hàng", "Thủ kho giao hàng", "Khách hàng ký nhận")), DefaultA4Css)
                ],
                views:
                [
                    new("sales_don_hang", "Đơn hàng Đã thanh toán", false, true, SavedViewAnd("payment_status", "eq", "PAID"), "ma_don", "DESC"),
                    new("sales_don_hang", "Đơn hàng Chưa thanh toán", false, true, SavedViewAnd("payment_status", "eq", "UNPAID"), "ma_don", "DESC")
                ],
                bindings: [new("vietqr", "sales_don_hang", new() { ["amount"] = "tong_tien", ["orderCode"] = "ma_don", ["description"] = "khach_hang", ["paymentStatus"] = "payment_status" })]),

            "HANDOVER_VN" => WithV2(tpl,
                rules: new()
                {
                    ["bg_tai_san"] =
                    [
                        Rule("fr_hvn_attach_vis", "ho_so_dinh_kem", "visible", "ngay_ban_giao", "NOT_EMPTY", null, true, false),
                        Rule("fr_hvn_sign_vis", "chu_ky_nhan", "visible", "trang_thai_duyet", "EQUALS", "DA_DUYET", true, false)
                    ]
                },
                workflows: [Wf2Step("bg_tai_san", "Phê duyệt Biên bản Bàn giao Tài sản", "Quản lý Tài sản kiểm tra serial & Nhân sự ký nhận tài sản", "Quản lý Tài sản IT xác nhận", ["ASSET_LEAD", "HR_MANAGER"])],
                automations:
                [
                    new("Thông báo Biên bản Bàn giao mới", "Gửi thông báo tới nhân sự khi có phiếu bàn giao tài sản chờ ký nhận", "bg_tai_san", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Biên bản bàn giao {{ma_phieu}} ngày {{ngay_ban_giao}} cho {{nguoi_nhan}} đã được tạo.")
                ],
                prints:
                [
                    new("bg_tai_san", "Biên bản Bàn giao Tài sản Kèm Chữ ký (A4)", "A4", "portrait", true, true, false,
                        PrintA4("BIÊN BẢN BÀN GIAO TÀI SẢN & THIẾT BỊ", "SmartApps Handover VN 2.0", "ma_phieu",
                        [
                            ("Nhân sự tiếp nhận", "nguoi_nhan"),
                            ("Ngày bàn giao", "ngay_ban_giao"),
                            ("Trạng thái duyệt", "trang_thai_duyet")
                        ], "chi_tiet_ts",
                        [("Tên tài sản / Thiết bị", "ten_ts", false), ("Số Serial / Mã TS", "serial", false), ("Tình trạng bàn giao", "tinh_trang", false)],
                        signs: ("Bộ phận Bàn giao IT", "Quản lý Tài sản", "Người nhận ký tên")), DefaultA4Css)
                ],
                views:
                [
                    ApprovalViewPending("bg_tai_san", "Biên bản Chờ duyệt"),
                    ApprovalViewApproved("bg_tai_san", "Biên bản Đã duyệt")
                ],
                bindings: [new("vnpt-smartca", "bg_tai_san", new() { ["documentCode"] = "ma_phieu", ["signer"] = "nguoi_nhan", ["signature"] = "chu_ky_nhan" })]),

            _ => tpl
        };
    }

    private static AppTemplateDefinition WithV2(
        AppTemplateDefinition tpl,
        Dictionary<string, List<TemplateColumnSpec>>? extraCols = null,
        Dictionary<string, string[]>? quickCreateCols = null,
        Dictionary<string, List<FormRuleDto>>? rules = null,
        List<TemplateWorkflowSpec>? workflows = null,
        List<TemplateAutomationSpec>? automations = null,
        List<TemplatePrintTemplateSpec>? prints = null,
        List<TemplateSavedViewSpec>? views = null,
        List<TemplatePluginBindingSpec>? bindings = null)
    {
        var upgradedModules = tpl.Modules.Select(m =>
        {
            var tables = m.Tables.Select(tb =>
            {
                var qcSet = quickCreateCols != null && quickCreateCols.TryGetValue(tb.TableName, out var qcs)
                    ? qcs.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : [];

                var cols = tb.Columns.Select(c =>
                    qcSet.Contains(c.ColumnCode) ? c with { AllowQuickCreate = true } : c).ToList();

                if (extraCols != null && extraCols.TryGetValue(tb.TableName, out var adds))
                {
                    foreach (var add in adds)
                    {
                        if (!cols.Any(c => c.ColumnCode.Equals(add.ColumnCode, StringComparison.OrdinalIgnoreCase)))
                            cols.Add(add);
                    }
                }

                var tbRules = rules != null && rules.TryGetValue(tb.TableName, out var rs) ? rs : tb.FormRules;
                return tb with
                {
                    Columns = cols,
                    FormRules = tbRules,
                    ApproverRoles = tb.ApproverRoles.Count > 0
                        ? tb.ApproverRoles
                        : ["ADMIN", "HR_MANAGER", "ASSET_LEAD"]
                };
            }).ToList();

            return m with { Tables = tables };
        }).ToList();

        var features = tpl.Features.ToList();
        if (!features.Any(f => f.Contains("2.0", StringComparison.OrdinalIgnoreCase)))
            features.Add("SmartApps 2.0");

        return tpl with
        {
            Version = "2.0.0",
            Features = features,
            Modules = upgradedModules,
            Workflows = workflows ?? [],
            Automations = automations ?? [],
            PrintTemplates = prints ?? [],
            SavedViews = views ?? [],
            PluginBindings = bindings ?? []
        };
    }

    public static IReadOnlyList<CoreAppV2Spec> GetCoreAppV2Specs() =>
    [
        new(
            AppId: "app_hrm",
            Code: "HRM",
            Version: "2.0.0",
            Categories: [],
            ExtraModules: [],
            ExtraColumnsByTable: new()
            {
                ["hrm_nghi_phep"] =
                [
                    new() { ColumnCode = "giay_xac_nhan_y_te", ColumnName = "Giấy xác nhận y tế (Nghỉ ốm)", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 8 },
                    new() { ColumnCode = "ban_giao_cv", ColumnName = "Nội dung bàn giao công việc", DataType = "TEXTAREA", ShowInTable = false, Width = 230, OrderIndex = 9 }
                ]
            },
            FormRulesByTable: new()
            {
                ["hrm_nhan_vien"] = [Rule("fr_hrm_nv_resign", "ngay_nghi_viec", "visible", "trang_thai_ns", "EQUALS", "DA_NGHI_VIEC", true, false)],
                ["hrm_hop_dong"] = [Rule("fr_hrm_hd_enddate", "den_ngay", "visible", "loai_hop_dong", "NOT_EQUALS", "KHONG_THOI_HAN", true, false)],
                ["hrm_nghi_phep"] = [Rule("fr_hrm_np_medfile", "giay_xac_nhan_y_te", "visible", "loai_nghi", "EQUALS", "OM", true, false)]
            },
            SubTableSchemaUpgrades: new(),
            Workflows:
            [
                Wf2Step("hrm_nghi_phep", "Quy trình Duyệt Nghỉ phép HRM (2 bước)", "Quản lý trực tiếp & HR xác nhận ngày phép trước khi phê duyệt", "Trưởng phòng Nhân sự duyệt", ["HR_MANAGER"]),
                Wf2Step("hrm_hop_dong", "Phê duyệt Hợp đồng Lao động", "HR soạn thảo & kiểm tra lương hợp đồng trước khi Ban Giám đốc ký", "Trưởng phòng Nhân sự kiểm tra", ["HR_MANAGER"])
            ],
            Automations:
            [
                new("Nhắc duyệt Đơn Nghỉ phép mới", "Thông báo HR khi nhân viên gửi đơn xin nghỉ phép mới", "hrm_nghi_phep", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Nhân viên {{ho_ten_nv}} vừa nộp đơn nghỉ {{loai_nghi}} ({{so_ngay}} ngày từ {{tu_ngay}}).")
            ],
            PrintTemplates:
            [
                new("hrm_nhan_vien", "Hồ sơ Sơ yếu Lý lịch Nhân sự (A4)", "A4", "portrait", true, false, false,
                    PrintA4("HỒ SƠ LÝ LỊCH NHÂN SỰ", "SmartApps HRM 2.0", "ma_nv",
                    [
                        ("Họ và Tên", "ho_ten"),
                        ("Email công ty", "email"),
                        ("Số điện thoại", "so_dien_thoai"),
                        ("Phòng ban", "phong_ban"),
                        ("Chức vụ", "chuc_vu"),
                        ("Ngày vào làm", "ngay_vao_lam"),
                        ("Trạng thái nhân sự", "trang_thai_ns")
                    ], signs: ("Nhân sự", "Phòng Nhân sự (HR)", "Ban Giám đốc")), DefaultA4Css),
                new("hrm_bang_luong", "Phiếu Lương Cá nhân Hàng tháng (A4)", "A4", "portrait", true, false, false,
                    PrintA4("PHIẾU LƯƠNG NHÂN VIÊN", "SmartApps Payroll 2.0", "ma_phieu_luong",
                    [
                        ("Kỳ lương", "ky_luong"),
                        ("Nhân viên", "nhan_vien"),
                        ("Lương cơ bản", "luong_co_ban"),
                        ("Tổng phụ cấp", "tong_phu_cap"),
                        ("Tiền tăng ca / Thưởng", "tien_ot"),
                        ("Khấu trừ BHXH (10.5%)", "khau_tru_bhxh"),
                        ("Thuế TNCN", "thue_tncn")
                    ], totalLabel: "Thực lĩnh chuyển khoản", totalField: "thuc_linh",
                    signs: ("Nhân viên nhận lương", "Kế toán tiền lương", "Giám đốc duyệt chi")), DefaultA4Css),
                new("hrm_nghi_phep", "Đơn xin Nghỉ phép (A4)", "A4", "portrait", true, true, false,
                    PrintA4("ĐƠN XIN NGHỈ PHÉP", "SmartApps HRM 2.0", "ma_don",
                    [
                        ("Nhân viên", "ho_ten_nv"),
                        ("Loại nghỉ phép", "loai_nghi"),
                        ("Từ ngày / Đến ngày", "tu_ngay"),
                        ("Tổng số ngày nghỉ", "so_ngay"),
                        ("Trạng thái duyệt", "trang_thai_duyet")
                    ], signs: ("Người làm đơn", "Trưởng bộ phận", "Phòng Nhân sự")), DefaultA4Css)
            ],
            SavedViews:
            [
                new("hrm_nhan_vien", "Nhân sự Chính thức Đang làm việc", false, true, SavedViewAnd("trang_thai_ns", "eq", "CHINH_THUC"), "ma_nv", "ASC"),
                ApprovalViewPending("hrm_nghi_phep", "Đơn Nghỉ phép Chờ duyệt"),
                ApprovalViewApproved("hrm_nghi_phep", "Đơn Nghỉ phép Đã duyệt"),
                new("hrm_hop_dong", "Hợp đồng Đang hiệu lực", false, true, SavedViewAnd("trang_thai_hd", "eq", "HIEU_LUC"), "ngay_ky", "DESC")
            ],
            ExtraWidgets: [],
            PluginBindings: [new("zalo-zns", "hrm_nghi_phep", new() { ["employee"] = "ho_ten_nv", ["status"] = "trang_thai_duyet" })]),

        new(
            AppId: "app_asset",
            Code: "ASSET",
            Version: "2.0.0",
            Categories: [],
            ExtraModules: [],
            ExtraColumnsByTable: new()
            {
                ["asset_danh_muc"] = [new() { ColumnCode = "ly_do_thanh_ly", ColumnName = "Lý do chờ thanh lý / Đánh giá", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 14 }],
                ["asset_cap_phat"] = [new() { ColumnCode = "ly_do_thu_hoi", ColumnName = "Lý do thu hồi / Điều chuyển", DataType = "TEXTAREA", ShowInTable = false, Width = 220, OrderIndex = 11 }]
            },
            FormRulesByTable: new()
            {
                ["asset_danh_muc"] = [Rule("fr_asset_liq_vis", "ly_do_thanh_ly", "visible", "tinh_trang", "EQUALS", "THANH_LY", true, false)],
                ["asset_cap_phat"] = [Rule("fr_asset_ret_vis", "ly_do_thu_hoi", "visible", "loai_phieu", "EQUALS", "THU_HOI", true, false)]
            },
            SubTableSchemaUpgrades: new(),
            Workflows: [], // wf_asset_alloc already seeded on tbl_asset_capphat
            Automations:
            [
                new("Thông báo Phiếu Bảo trì Tài sản mới", "Thông báo Quản lý Tài sản & IT khi ghi nhận thiết bị cần bảo trì", "asset_bao_tri", "RECORD_CREATED", null, "EQUALS", null, null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "Phiếu bảo trì {{ma_phieu_bt}} cho tài sản {{ma_ts}} vừa được tạo.")
            ],
            PrintTemplates:
            [
                new("asset_cap_phat", "Biên bản Cấp phát & Thu hồi Tài sản (A4)", "A4", "portrait", true, true, false,
                    PrintA4("BIÊN BẢN GIAO NHẬN TÀI SẢN", "SmartApps Asset 2.0", "ma_phieu",
                    [
                        ("Tài sản cấp phát", "ma_ts"),
                        ("Người tiếp nhận", "nguoi_nhan"),
                        ("Phòng ban sử dụng", "phong_ban"),
                        ("Loại phiếu", "loai_phieu"),
                        ("Ngày bàn giao", "ngay_cap"),
                        ("Số lượng", "so_luong_cap"),
                        ("Trạng thái bàn giao", "trang_thai_bg")
                    ], signs: ("Người bàn giao IT", "Quản lý Tài sản", "Người nhận ký tên")), DefaultA4Css),
                new("asset_danh_muc", "Phiếu Lý lịch Tài sản (A4)", "A4", "portrait", true, false, false,
                    PrintA4("PHIẾU LÝ LỊCH TÀI SẢN CỐ ĐỊNH", "SmartApps Asset 2.0", "ma_ts",
                    [
                        ("Tên Tài sản / Thiết bị", "ten_ts"),
                        ("Nhóm tài sản", "nhom_ts"),
                        ("Hãng / Model", "hang_san_xuat"),
                        ("Số lượng / Đơn giá", "don_gia"),
                        ("Ngày mua / Hạn BH", "ngay_mua"),
                        ("Vị trí lưu trữ", "vi_tri_luu_tru"),
                        ("Tình trạng", "tinh_trang")
                    ], totalLabel: "Tổng nguyên giá tài sản", totalField: "thanh_tien",
                    signs: ("Kế toán tài sản", "Quản lý Tài sản", "Giám đốc")), DefaultA4Css)
            ],
            SavedViews:
            [
                new("asset_danh_muc", "Tài sản Lưu kho sẵn sàng", false, true, SavedViewAnd("tinh_trang", "eq", "TRONG_KHO"), "ma_ts", "ASC"),
                new("asset_danh_muc", "Tài sản Đang bảo trì", false, true, SavedViewAnd("tinh_trang", "eq", "BAO_TRI")),
                ApprovalViewPending("asset_cap_phat", "Phiếu Cấp phát Chờ duyệt")
            ],
            ExtraWidgets: [],
            PluginBindings: [new("vnpt-smartca", "asset_cap_phat", new() { ["documentCode"] = "ma_phieu", ["signer"] = "nguoi_nhan", ["signature"] = "chu_ky_nhan" })]),

        new(
            AppId: "app_ops",
            Code: "OPS",
            Version: "2.0.0",
            Categories: [],
            ExtraModules: [],
            ExtraColumnsByTable: new()
            {
                ["ops_mua_sam"] =
                [
                    new() { ColumnCode = "bao_gia_ncc", ColumnName = "Báo giá Nhà cung cấp", DataType = "MULTI_FILE", ShowInTable = false, Width = 200, OrderIndex = 8 },
                    new() { ColumnCode = "payment_status", ColumnName = "Thanh toán VietQR", DataType = "STRING", DefaultValue = "UNPAID", Width = 135, OrderIndex = 9 }
                ]
            },
            FormRulesByTable: new()
            {
                ["ops_yeu_cau"] = [Rule("fr_ops_done_res", "ket_qua_xu_ly", "visible", "da_hoan_thanh", "EQUALS", "true", true, false)],
                ["ops_mua_sam"] = [Rule("fr_ops_ms_quote", "bao_gia_ncc", "visible", "ngay_can_hang", "NOT_EMPTY", null, true, false)]
            },
            SubTableSchemaUpgrades: new()
            {
                ["ops_mua_sam.chi_tiet_hang_muc"] = SubTableJson(
                    new("ten_hang", "Tên hàng hóa / Thiết bị", "STRING", true, 240),
                    new("so_luong", "Số lượng", "NUMBER", true, 95, "1"),
                    new("don_gia", "Đơn giá (VNĐ)", "CURRENCY", true, 150),
                    new("thanh_tien", "Thành tiền", "CURRENCY", false, 165, null, "so_luong * don_gia"))
            },
            Workflows:
            [
                Wf2Step("ops_mua_sam", "Phê duyệt Đề xuất Mua sắm OPS (2 bước)", "Hành chính / IT kiểm tra dự toán trước khi Ban Giám đốc phê duyệt")
            ],
            Automations:
            [
                new("Thông báo Ticket OPS Ưu tiên Cao", "Cảnh báo ngay cho bộ phận vận hành khi tạo ticket mức độ Ưu tiên Cao", "ops_yeu_cau", "RECORD_CREATED", "muc_do_uu_tien", "EQUALS", "HIGH", null, null, null, "SEND_NOTIFICATION", null, null, new(), "*", "KHẨN CẤP: Yêu cầu {{ma_ticket}} ({{tieu_de}}) cần xử lý trước {{han_xu_ly}}.")
            ],
            PrintTemplates:
            [
                new("ops_mua_sam", "Phiếu Đề xuất Mua sắm Nội bộ (A4)", "A4", "portrait", true, true, false,
                    PrintA4("PHIẾU ĐỀ XUẤT MUA SẮM NỘI BỘ", "SmartApps Operations 2.0", "ma_de_xuat",
                    [
                        ("Nội dung đề xuất", "tieu_de"),
                        ("Người đề xuất", "nguoi_de_xuat"),
                        ("Phòng ban", "phong_ban"),
                        ("Ngày cần hàng", "ngay_can_hang")
                    ], "chi_tiet_hang_muc",
                    [("Tên hàng hóa / Thiết bị", "ten_hang", false), ("SL", "so_luong", false), ("Đơn giá", "don_gia", true), ("Thành tiền", "thanh_tien", true)],
                    "Tổng dự kiến chi", "tong_du_kien", ("Người đề xuất", "Hành chính / Mua sắm", "Ban Giám đốc")), DefaultA4Css),
                new("ops_yeu_cau", "Phiếu Tiếp nhận Yêu cầu Nội bộ (A4)", "A4", "portrait", true, false, false,
                    PrintA4("PHIẾU TIẾP NHẬN YÊU CẦU NỘI BỘ", "SmartApps Operations 2.0", "ma_ticket",
                    [
                        ("Tiêu đề yêu cầu", "tieu_de"),
                        ("Phân loại", "loai_yeu_cau"),
                        ("Mức ưu tiên", "muc_do_uu_tien"),
                        ("Nhân viên đề xuất", "nguoi_tao"),
                        ("Hạn xử lý", "han_xu_ly"),
                        ("Kết quả xử lý", "ket_qua_xu_ly")
                    ], signs: ("Người đề xuất", "Người xử lý", "Trưởng bộ phận")), DefaultA4Css)
            ],
            SavedViews:
            [
                new("ops_yeu_cau", "Ticket Chưa hoàn thành", false, true, SavedViewAnd("da_hoan_thanh", "eq", "false"), "han_xu_ly", "ASC"),
                new("ops_yeu_cau", "Ticket Ưu tiên Cao", false, true, SavedViewAnd("muc_do_uu_tien", "eq", "HIGH")),
                ApprovalViewPending("ops_mua_sam", "Đề xuất Mua sắm Chờ duyệt")
            ],
            ExtraWidgets: [],
            PluginBindings: [new("vietqr", "ops_mua_sam", new() { ["amount"] = "tong_du_kien", ["orderCode"] = "ma_de_xuat", ["description"] = "tieu_de", ["paymentStatus"] = "payment_status" })]),

        new(
            AppId: "app_tasks",
            Code: "TASKS",
            Version: "2.0.0",
            Categories: [],
            ExtraModules: [],
            ExtraColumnsByTable: new()
            {
                ["app_tasks"] = [new() { ColumnCode = "completion_note", ColumnName = "Báo cáo kết quả hoàn thành", DataType = "TEXTAREA", ShowInTable = false, Width = 240, OrderIndex = 13 }]
            },
            FormRulesByTable: new()
            {
                ["app_tasks"] = [Rule("fr_tsk_done_note", "completion_note", "visible", "status", "EQUALS", "DONE", true, false)]
            },
            SubTableSchemaUpgrades: new(),
            Workflows: [],
            Automations:
            [
                new("Thông báo Khi hoàn thành Công việc", "Thông báo cho người giao việc khi task chuyển trạng thái DONE", "app_tasks", "RECORD_UPDATED", "status", "EQUALS", "DONE", null, null, null, "SEND_NOTIFICATION", null, null, new(), "{{created_by}}", "Công việc {{task_code}} ({{title}}) đã hoàn thành 100%.")
            ],
            PrintTemplates:
            [
                new("app_tasks", "Phiếu Giao việc & Tiến độ (A4)", "A4", "portrait", true, false, false,
                    PrintA4("PHIẾU GIAO VIỆC & THEO DÕI TIẾN ĐỘ", "SmartApps Tasks 2.0", "task_code",
                    [
                        ("Tiêu đề công việc", "title"),
                        ("Trạng thái", "status"),
                        ("Mức ưu tiên", "priority"),
                        ("Người thực hiện", "assignee_username"),
                        ("Người giao việc", "assigner_username"),
                        ("Hạn hoàn thành", "due_date"),
                        ("Tiến độ (%)", "progress")
                    ], signs: ("Người thực hiện", "Trưởng nhóm", "Người giao việc")), DefaultA4Css)
            ],
            SavedViews:
            [
                new("app_tasks", "Việc Cần thực hiện (TODO)", false, true, SavedViewAnd("status", "eq", "TODO"), "due_date", "ASC"),
                new("app_tasks", "Việc Đang thực hiện", false, true, SavedViewAnd("status", "eq", "IN_PROGRESS"), "due_date", "ASC"),
                new("app_tasks", "Việc Đã hoàn thành", false, true, SavedViewAnd("status", "eq", "DONE"))
            ],
            ExtraWidgets: [],
            PluginBindings: [new("zalo-zns", "app_tasks", new() { ["taskCode"] = "task_code", ["assignee"] = "assignee_username", ["dueDate"] = "due_date" })])
    ];
}
