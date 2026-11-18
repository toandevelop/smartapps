using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public sealed class PluginExecutionResult
{
    public bool Success { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object?> Data { get; set; } = new();
}

/// <summary>
/// Execution & IPN verification layer for Vietnamese enterprise integrations:
/// E-Invoice (MISA/Viettel/VNPT/BKAV), Digital Signature (SmartCA/MySign/eSign),
/// Payment/Banking (VNPay/MoMo/ZaloPay/VietQR/VietinBank/Vietcombank), and SMS Brandname (Viettel/VinaPhone/ZNS).
/// </summary>
public sealed class VietnamIntegrationService
{
    private readonly string _connStr;
    private readonly IntegrationService _integrations;
    private readonly RecordWriteService _writer;
    private readonly IHttpClientFactory _httpFactory;

    public VietnamIntegrationService(
        IConfiguration config,
        IntegrationService integrations,
        RecordWriteService writer,
        IHttpClientFactory httpFactory)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _integrations = integrations;
        _writer = writer;
        _httpFactory = httpFactory;
    }

    public async Task<PluginExecutionResult> ExecutePluginAsync(
        string pluginCode,
        string operation,
        Dictionary<string, object?> input,
        string actor = "system")
    {
        var loaded = await _integrations.GetPluginRuntimeConfigAsync(pluginCode);
        if (loaded == null)
            throw new InvalidOperationException($"Không tìm thấy plugin '{pluginCode}'.");

        var (plugin, secret) = loaded.Value;
        if (!plugin.IsEnabled)
            throw new InvalidOperationException($"Plugin '{plugin.Name}' chưa được kích hoạt.");

        var env = plugin.Config.GetValueOrDefault("environment")?.ToString()?.ToLowerInvariant() == "production"
            ? "production"
            : "sandbox";

        var op = operation.Trim().ToLowerInvariant();
        PluginExecutionResult result = op switch
        {
            "create_payment" => await CreatePaymentAsync(plugin, secret, env, input),
            "issue_einvoice" => await IssueEInvoiceAsync(plugin, secret, env, input),
            "draft_einvoice" => await IssueEInvoiceAsync(plugin, secret, env, input, isDraft: true),
            "cancel_einvoice" => await CancelEInvoiceAsync(plugin, secret, env, input),
            "parse_einvoice_xml" => ParseEInvoiceXmlOperation(plugin, env, input),
            "request_sign" => await RequestDigitalSignAsync(plugin, secret, env, input),
            "complete_local_sign" => CompleteLocalDigitalSign(plugin, env, input),
            "send_sms" => await SendSmsAsync(plugin, secret, env, input),
            _ => throw new InvalidOperationException($"Thao tác '{operation}' chưa được hỗ trợ cho plugin '{plugin.Code}'.")
        };

        await _integrations.LogIntegrationAsync(
            "OUTBOUND",
            $"PLUGIN_{op.ToUpperInvariant()}",
            plugin.Id,
            plugin.AppId,
            input.GetValueOrDefault("tableId")?.ToString(),
            result.Success ? 200 : 502,
            result.Success,
            $"{plugin.Code} ({env}): {result.Message}");

        return result;
    }

    private async Task<PluginExecutionResult> CreatePaymentAsync(
        SysPluginDto plugin, string? secret, string env, Dictionary<string, object?> input)
    {
        var amountStr = Convert.ToString(input.GetValueOrDefault("amount") ?? "0", CultureInfo.InvariantCulture) ?? "0";
        var amount = decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? (long)Math.Round(a) : 0L;
        var orderRef = input.GetValueOrDefault("orderRef")?.ToString()
            ?? input.GetValueOrDefault("recordId")?.ToString()
            ?? $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var description = input.GetValueOrDefault("description")?.ToString() ?? $"Thanh toan {orderRef}";

        if (plugin.Code.Equals("vietqr", StringComparison.OrdinalIgnoreCase))
        {
            var bankBin = plugin.Config.GetValueOrDefault("bankBin")?.ToString() ?? "970415";
            var accountNo = plugin.Config.GetValueOrDefault("accountNo")?.ToString() ?? "0000000000";
            var accountName = plugin.Config.GetValueOrDefault("accountName")?.ToString() ?? "SMARTAPPS";
            var qrUrl = $"https://img.vietqr.io/image/{Uri.EscapeDataString(bankBin)}-{Uri.EscapeDataString(accountNo)}-compact2.png" +
                        $"?amount={amount}&addInfo={Uri.EscapeDataString(orderRef)}&accountName={Uri.EscapeDataString(accountName)}";

            return new PluginExecutionResult
            {
                Success = true,
                ProviderCode = plugin.Code,
                Operation = "create_payment",
                Environment = env,
                Message = $"Đã tạo mã VietQR cho {orderRef} ({amount:N0} VND).",
                Data = new Dictionary<string, object?>
                {
                    ["transactionRef"] = orderRef,
                    ["amount"] = amount,
                    ["bankBin"] = bankBin,
                    ["accountNo"] = accountNo,
                    ["qrUrl"] = qrUrl
                }
            };
        }

        if (plugin.Code.Equals("vnpay", StringComparison.OrdinalIgnoreCase))
        {
            var tmnCode = plugin.Config.GetValueOrDefault("tmnCode")?.ToString() ?? "SANDBOX_TMN";
            var returnUrl = plugin.Config.GetValueOrDefault("returnUrl")?.ToString() ?? "https://localhost:4100/payment-return";
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString() ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
            var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["vnp_Amount"] = (amount * 100).ToString(CultureInfo.InvariantCulture),
                ["vnp_Command"] = "pay",
                ["vnp_CreateDate"] = DateTime.UtcNow.AddHours(7).ToString("yyyyMMddHHmmss"),
                ["vnp_CurrCode"] = "VND",
                ["vnp_IpAddr"] = "127.0.0.1",
                ["vnp_Locale"] = "vn",
                ["vnp_OrderInfo"] = description,
                ["vnp_OrderType"] = "other",
                ["vnp_ReturnUrl"] = returnUrl,
                ["vnp_TmnCode"] = tmnCode,
                ["vnp_TxnRef"] = orderRef,
                ["vnp_Version"] = "2.1.0"
            };
            var queryData = string.Join("&", vnpParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            var secureHash = ComputeHmacSha512Hex(secret ?? "sandbox_secret", queryData);
            var paymentUrl = $"{baseUrl}?{queryData}&vnp_SecureHash={secureHash}";

            return new PluginExecutionResult
            {
                Success = true,
                ProviderCode = plugin.Code,
                Operation = "create_payment",
                Environment = env,
                Message = $"Đã tạo URL thanh toán VNPay cho {orderRef}.",
                Data = new Dictionary<string, object?>
                {
                    ["transactionRef"] = orderRef,
                    ["amount"] = amount,
                    ["paymentUrl"] = paymentUrl,
                    ["secureHash"] = secureHash
                }
            };
        }

        // MoMo, ZaloPay, VietinBank, Vietcombank
        var sigPayload = $"{plugin.Code}|{orderRef}|{amount}|{description}";
        var sig = ComputeHmacSha256Hex(secret ?? "sandbox_secret", sigPayload);
        if (env == "production")
        {
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString();
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var remote = await PostJsonAsync(baseUrl, secret, new { orderRef, amount, description, signature = sig });
                return new PluginExecutionResult
                {
                    Success = remote.Ok,
                    ProviderCode = plugin.Code,
                    Operation = "create_payment",
                    Environment = env,
                    Message = remote.Ok ? $"Đã tạo giao dịch {plugin.Code} ({orderRef})." : remote.Body,
                    Data = new Dictionary<string, object?> { ["transactionRef"] = orderRef, ["rawResponse"] = remote.Body }
                };
            }
        }

        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "create_payment",
            Environment = env,
            Message = $"Đã tạo giao dịch {plugin.Code} ({env}) cho {orderRef}.",
            Data = new Dictionary<string, object?>
            {
                ["transactionRef"] = orderRef,
                ["amount"] = amount,
                ["paymentUrl"] = $"https://sandbox.{plugin.Code}.vn/pay?ref={Uri.EscapeDataString(orderRef)}&sig={sig[..16]}",
                ["signature"] = sig
            }
        };
    }

    private async Task<PluginExecutionResult> IssueEInvoiceAsync(
        SysPluginDto plugin, string? secret, string env, Dictionary<string, object?> input, bool isDraft = false)
    {
        var invoiceRef = input.GetValueOrDefault("orderRef")?.ToString()
            ?? input.GetValueOrDefault("recordId")?.ToString()
            ?? $"INV-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var buyerTaxCode = input.GetValueOrDefault("buyerTaxCode")?.ToString() ?? "";
        var totalAmount = input.GetValueOrDefault("amount") ?? 0;
        var opName = isDraft ? "draft_einvoice" : "issue_einvoice";

        if (env == "production")
        {
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"Plugin '{plugin.Code}' thiếu cấu hình baseUrl cho môi trường production.");
            var remote = await PostJsonAsync(baseUrl, secret, new { invoiceRef, buyerTaxCode, totalAmount, isDraft, payload = input });
            return new PluginExecutionResult
            {
                Success = remote.Ok,
                ProviderCode = plugin.Code,
                Operation = opName,
                Environment = env,
                Message = remote.Ok
                    ? $"Đã {(isDraft ? "lưu nháp" : "phát hành")} hóa đơn {plugin.Code} cho {invoiceRef}."
                    : remote.Body,
                Data = new Dictionary<string, object?> { ["invoiceRef"] = invoiceRef, ["rawResponse"] = remote.Body }
            };
        }

        var invoiceNo = isDraft ? $"DRAFT-{Random.Shared.Next(1000, 9999)}" : Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var lookupCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = opName,
            Environment = env,
            Message = isDraft
                ? $"Đã tạo hóa đơn nháp {plugin.Code} (Sandbox) mã {invoiceNo}."
                : $"Đã phát hành hóa đơn điện tử {plugin.Code} (Sandbox) số {invoiceNo}.",
            Data = new Dictionary<string, object?>
            {
                ["invoiceNo"] = invoiceNo,
                ["invoiceSeries"] = "1C26TSA",
                ["lookupCode"] = lookupCode,
                ["cqtStatus"] = isDraft ? "DRAFT_SANDBOX" : "ISSUED_SANDBOX",
                ["invoiceRef"] = invoiceRef,
                ["issueDate"] = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["pdfUrl"] = $"https://sandbox.{plugin.Code}.vn/einvoice/pdf/{Uri.EscapeDataString(lookupCode)}"
            }
        };
    }

    private async Task<PluginExecutionResult> CancelEInvoiceAsync(
        SysPluginDto plugin, string? secret, string env, Dictionary<string, object?> input)
    {
        var invoiceNo = input.GetValueOrDefault("invoiceNo")?.ToString()
            ?? input.GetValueOrDefault("orderRef")?.ToString()
            ?? input.GetValueOrDefault("recordId")?.ToString()
            ?? throw new InvalidOperationException("Thiếu số hóa đơn (invoiceNo) để hủy theo NĐ 123.");
        var reason = input.GetValueOrDefault("reason")?.ToString() ?? "Hủy hóa đơn điện tử";

        if (env == "production")
        {
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"Plugin '{plugin.Code}' thiếu cấu hình baseUrl cho môi trường production.");
            var remote = await PostJsonAsync(baseUrl, secret, new { action = "cancel_einvoice", invoiceNo, reason });
            return new PluginExecutionResult
            {
                Success = remote.Ok,
                ProviderCode = plugin.Code,
                Operation = "cancel_einvoice",
                Environment = env,
                Message = remote.Ok ? $"Đã hủy hóa đơn {invoiceNo} trên {plugin.Code}." : remote.Body,
                Data = new Dictionary<string, object?> { ["invoiceNo"] = invoiceNo, ["rawResponse"] = remote.Body }
            };
        }

        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "cancel_einvoice",
            Environment = env,
            Message = $"Đã hủy hóa đơn điện tử {plugin.Code} số {invoiceNo} (Sandbox).",
            Data = new Dictionary<string, object?>
            {
                ["invoiceNo"] = invoiceNo,
                ["cqtStatus"] = "CANCELLED_SANDBOX",
                ["cancelledAt"] = DateTime.UtcNow.ToString("O")
            }
        };
    }

    public static (Dictionary<string, object?> Fields, Dictionary<string, List<Dictionary<string, object?>>> SubTables) MapVnInvoiceXmlToTableColumns(
        Dictionary<string, object?> parsed,
        List<SysColumnDto> columns)
    {
        var fields = new Dictionary<string, object?>();
        var subTables = new Dictionary<string, List<Dictionary<string, object?>>>();

        foreach (var col in columns)
        {
            if (col.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                || col.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase)) continue;
            var key = $"{col.ColumnCode} {col.ColumnName}".ToLowerInvariant();
            if (col.DataType.Equals("DATE", StringComparison.OrdinalIgnoreCase) || key.Contains("ngay"))
            {
                var d = parsed.GetValueOrDefault("issueDate")?.ToString();
                if (!string.IsNullOrWhiteSpace(d)) fields[col.ColumnCode] = d.Length >= 10 ? d[..10] : d;
            }
            else if ((col.DataType is "CURRENCY" or "NUMBER") && (key.Contains("tong") || key.Contains("thanh_tien") || key.Contains("amount")))
            {
                fields[col.ColumnCode] = parsed.GetValueOrDefault("totalAmount");
            }
            else if (key.Contains("mst") || key.Contains("ma_so_thue") || key.Contains("tax"))
            {
                var mst = parsed.GetValueOrDefault("sellerTaxCode")?.ToString();
                if (!string.IsNullOrWhiteSpace(mst)) fields[col.ColumnCode] = mst;
            }
            else if (key.Contains("so_hoa_don") || key.Contains("so_hd") || key.Contains("invoice"))
            {
                var inv = parsed.GetValueOrDefault("invoiceNo")?.ToString();
                if (!string.IsNullOrWhiteSpace(inv)) fields[col.ColumnCode] = inv;
            }
            else if (key.Contains("ncc") || key.Contains("nha_cung_cap") || key.Contains("don_vi") || key.Contains("vendor"))
            {
                var seller = parsed.GetValueOrDefault("sellerName")?.ToString();
                if (!string.IsNullOrWhiteSpace(seller)) fields[col.ColumnCode] = seller;
            }
        }

        var firstSub = columns.FirstOrDefault(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase));
        if (firstSub != null && parsed.GetValueOrDefault("items") is List<Dictionary<string, object?>> xmlItems && xmlItems.Count > 0)
        {
            var schema = DynamicComputeService.ParseSubTableSchema(firstSub.DefaultValue);
            var descCol = schema.FirstOrDefault(s => s.DataType is "STRING" or "TEXT");
            var qtyCol = schema.FirstOrDefault(s => s.Code.Contains("so_luong", StringComparison.OrdinalIgnoreCase) || s.Code.Contains("qty", StringComparison.OrdinalIgnoreCase));
            var numCols = schema.Where(s => s.DataType is "NUMBER" or "CURRENCY" && s.Code != qtyCol?.Code).ToList();
            var priceCol = numCols.FirstOrDefault(s => s.Code.Contains("gia", StringComparison.OrdinalIgnoreCase) || s.Code.Contains("price", StringComparison.OrdinalIgnoreCase));
            var totalCol = numCols.FirstOrDefault(s => s.Code != priceCol?.Code) ?? numCols.FirstOrDefault();

            var mappedRows = new List<Dictionary<string, object?>>();
            foreach (var item in xmlItems)
            {
                var row = new Dictionary<string, object?>();
                if (descCol != null) row[descCol.Code] = item.GetValueOrDefault("ten_hang");
                if (qtyCol != null) row[qtyCol.Code] = item.GetValueOrDefault("so_luong");
                if (priceCol != null) row[priceCol.Code] = item.GetValueOrDefault("don_gia");
                if (totalCol != null) row[totalCol.Code] = item.GetValueOrDefault("thanh_tien");
                if (row.Count > 0) mappedRows.Add(row);
            }
            if (mappedRows.Count > 0) subTables[firstSub.ColumnCode] = mappedRows;
        }

        return (fields, subTables);
    }

    private static PluginExecutionResult ParseEInvoiceXmlOperation(
        SysPluginDto plugin, string env, Dictionary<string, object?> input)
    {
        var xml = input.GetValueOrDefault("xml")?.ToString() ?? string.Empty;
        var parsed = ParseVnInvoiceXml(xml);
        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "parse_einvoice_xml",
            Environment = env,
            Message = $"Đã bóc tách XML Hóa đơn NĐ 123 số {parsed.GetValueOrDefault("invoiceNo") ?? "(không rõ)"}.",
            Data = parsed
        };
    }

    /// <summary>
    /// Safely parses Vietnamese Decree 123/2020/ND-CP electronic invoice XML with XXE protection.
    /// </summary>
    public static Dictionary<string, object?> ParseVnInvoiceXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("Nội dung XML hóa đơn NĐ 123 không được để trống.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using var sr = new StringReader(xml.Trim());
        using var reader = XmlReader.Create(sr, settings);
        var xdoc = XDocument.Load(reader);
        var root = xdoc.Root ?? throw new InvalidOperationException("XML không có phần tử gốc hợp lệ.");

        static string? TagVal(XContainer container, string localName) =>
            container.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

        static decimal ParseDec(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;
            return decimal.TryParse(raw.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
        }

        var sellerNode = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("NBan", StringComparison.OrdinalIgnoreCase));
        var buyerNode = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("NMua", StringComparison.OrdinalIgnoreCase));

        var items = new List<Dictionary<string, object?>>();
        foreach (var hh in root.Descendants().Where(e => e.Name.LocalName.Equals("HHDVu", StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new Dictionary<string, object?>
            {
                ["ten_hang"] = TagVal(hh, "THHDVu") ?? "",
                ["don_vi"] = TagVal(hh, "DVTinh") ?? "",
                ["so_luong"] = ParseDec(TagVal(hh, "SLuong")),
                ["don_gia"] = ParseDec(TagVal(hh, "DGia")),
                ["thue_suat"] = TagVal(hh, "TSuat") ?? "",
                ["thanh_tien"] = ParseDec(TagVal(hh, "ThTien"))
            });
        }

        return new Dictionary<string, object?>
        {
            ["invoiceForm"] = TagVal(root, "KHMSHDon") ?? "",
            ["invoiceSeries"] = TagVal(root, "KHHDon") ?? "",
            ["invoiceNo"] = TagVal(root, "SHDon") ?? "",
            ["issueDate"] = TagVal(root, "NLap") ?? "",
            ["currency"] = TagVal(root, "DVTTe") ?? "VND",
            ["sellerName"] = sellerNode != null ? TagVal(sellerNode, "Ten") ?? "" : "",
            ["sellerTaxCode"] = sellerNode != null ? TagVal(sellerNode, "MST") ?? "" : "",
            ["sellerAddress"] = sellerNode != null ? TagVal(sellerNode, "DChi") ?? "" : "",
            ["buyerName"] = buyerNode != null ? TagVal(buyerNode, "Ten") ?? "" : "",
            ["buyerTaxCode"] = buyerNode != null ? TagVal(buyerNode, "MST") ?? "" : "",
            ["subtotal"] = ParseDec(TagVal(root, "TgTCThue")),
            ["vatAmount"] = ParseDec(TagVal(root, "TgTThue")),
            ["totalAmount"] = ParseDec(TagVal(root, "TgTTTBSo")),
            ["totalInWords"] = TagVal(root, "TgTTTBChu") ?? "",
            ["items"] = items
        };
    }

    private async Task<PluginExecutionResult> RequestDigitalSignAsync(
        SysPluginDto plugin, string? secret, string env, Dictionary<string, object?> input)
    {
        var docId = input.GetValueOrDefault("documentId")?.ToString()
            ?? input.GetValueOrDefault("recordId")?.ToString()
            ?? $"DOC-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var rawContent = input.GetValueOrDefault("documentContent")?.ToString() ?? docId;
        var docHash = input.GetValueOrDefault("documentHash")?.ToString()
            ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent))).ToLowerInvariant();

        if (env == "production")
        {
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"Plugin '{plugin.Code}' thiếu cấu hình baseUrl cho môi trường production.");
            var remote = await PostJsonAsync(baseUrl, secret, new { docId, docHash, signerUid = plugin.Config.GetValueOrDefault("signerUid") });
            return new PluginExecutionResult
            {
                Success = remote.Ok,
                ProviderCode = plugin.Code,
                Operation = "request_sign",
                Environment = env,
                Message = remote.Ok ? $"Đã gửi yêu cầu ký số {plugin.Code}." : remote.Body,
                Data = new Dictionary<string, object?> { ["documentHash"] = docHash, ["rawResponse"] = remote.Body }
            };
        }

        var signTxId = $"SIGN-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "request_sign",
            Environment = env,
            Message = $"Đã gửi yêu cầu ký số từ xa {plugin.Code} (Sandbox).",
            Data = new Dictionary<string, object?>
            {
                ["signTransactionId"] = signTxId,
                ["documentHash"] = docHash,
                ["signStatus"] = "PENDING_USER_CONFIRM"
            }
        };
    }

    private static PluginExecutionResult CompleteLocalDigitalSign(
        SysPluginDto plugin, string env, Dictionary<string, object?> input)
    {
        var sigBase64 = input.GetValueOrDefault("signatureBase64")?.ToString()?.Trim();
        var docHash = input.GetValueOrDefault("documentHash")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(sigBase64) || string.IsNullOrWhiteSpace(docHash))
            throw new InvalidOperationException("Thiếu dữ liệu chữ ký số (signatureBase64) hoặc documentHash.");

        try
        {
            var sigBytes = Convert.FromBase64String(sigBase64);
            if (sigBytes.Length == 0)
                throw new InvalidOperationException("Chữ ký số Base64 rỗng.");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Chữ ký số (signatureBase64) không đúng định dạng Base64.");
        }

        var signTxId = input.GetValueOrDefault("signTransactionId")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(signTxId))
            signTxId = $"USB-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

        var certSubject = input.GetValueOrDefault("certificateSubject")?.ToString()?.Trim() ?? "USB Token CA";
        var certSerial = input.GetValueOrDefault("certificateSerial")?.ToString()?.Trim() ?? "";
        var signedAt = input.GetValueOrDefault("signedAt")?.ToString()?.Trim() ?? DateTime.UtcNow.ToString("O");

        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "complete_local_sign",
            Environment = env,
            Message = $"Đã xác nhận chữ ký USB Token ({certSubject}) cho giao dịch {signTxId}.",
            Data = new Dictionary<string, object?>
            {
                ["signTransactionId"] = signTxId,
                ["documentHash"] = docHash,
                ["signStatus"] = "SIGNED_LOCAL_TOKEN",
                ["certificateSubject"] = certSubject,
                ["certificateSerial"] = certSerial,
                ["signedAt"] = signedAt
            }
        };
    }

    private async Task<PluginExecutionResult> SendSmsAsync(
        SysPluginDto plugin, string? secret, string env, Dictionary<string, object?> input)
    {
        var phone = input.GetValueOrDefault("phone")?.ToString()?.Trim();
        var message = input.GetValueOrDefault("message")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Thiếu số điện thoại (phone) hoặc nội dung tin nhắn (message).");

        var brandname = plugin.Config.GetValueOrDefault("brandname")?.ToString()
            ?? plugin.Config.GetValueOrDefault("oaId")?.ToString()
            ?? "SMARTAPPS";

        if (env == "production")
        {
            var baseUrl = plugin.Config.GetValueOrDefault("baseUrl")?.ToString();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"Plugin '{plugin.Code}' thiếu cấu hình baseUrl cho môi trường production.");
            var remote = await PostJsonAsync(baseUrl, secret, new { brandname, phone, message });
            return new PluginExecutionResult
            {
                Success = remote.Ok,
                ProviderCode = plugin.Code,
                Operation = "send_sms",
                Environment = env,
                Message = remote.Ok ? $"Đã gửi SMS {brandname} tới {phone}." : remote.Body,
                Data = new Dictionary<string, object?> { ["phone"] = phone, ["brandname"] = brandname, ["rawResponse"] = remote.Body }
            };
        }

        var msgId = $"SMS-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        return new PluginExecutionResult
        {
            Success = true,
            ProviderCode = plugin.Code,
            Operation = "send_sms",
            Environment = env,
            Message = $"Đã gửi SMS {plugin.Code} ({brandname}) tới {phone} (Sandbox).",
            Data = new Dictionary<string, object?>
            {
                ["messageId"] = msgId,
                ["brandname"] = brandname,
                ["phone"] = phone,
                ["deliveryStatus"] = "SENT_SANDBOX"
            }
        };
    }

    public async Task<(bool Authorized, bool Updated, string Message)> ProcessPaymentIpnAsync(
        string providerCode,
        Dictionary<string, object?> payload,
        string? rawBody,
        string? signatureHeader)
    {
        var loaded = await _integrations.GetPluginRuntimeConfigAsync(providerCode);
        if (loaded == null || !loaded.Value.Plugin.IsEnabled || string.IsNullOrWhiteSpace(loaded.Value.Secret))
            return (false, false, "Plugin thanh toán chưa được cấu hình hoặc đang tắt.");

        var (plugin, secret) = loaded.Value;
        var verified = VerifyIpnSignature(providerCode, secret!, payload, rawBody ?? "", signatureHeader);
        if (!verified)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "PAYMENT_IPN", plugin.Id, plugin.AppId, null, 401, false, $"Sai chữ ký IPN từ {providerCode}");
            return (false, false, "Chữ ký IPN không hợp lệ.");
        }

        var orderRef = (payload.GetValueOrDefault("vnp_TxnRef")
            ?? payload.GetValueOrDefault("orderId")
            ?? payload.GetValueOrDefault("orderRef")
            ?? payload.GetValueOrDefault("recordId"))?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            var bankMemo = (payload.GetValueOrDefault("content")
                ?? payload.GetValueOrDefault("description")
                ?? payload.GetValueOrDefault("transferContent"))?.ToString();
            if (!string.IsNullOrWhiteSpace(bankMemo))
            {
                var memoMatch = Regex.Match(bankMemo, @"\b([A-Z]{2,10}-[A-Z0-9\-]{2,20}|rec_[a-zA-Z0-9]+)\b", RegexOptions.IgnoreCase);
                if (memoMatch.Success) orderRef = memoMatch.Groups[1].Value;
            }
        }
        var txId = (payload.GetValueOrDefault("vnp_TransactionNo")
            ?? payload.GetValueOrDefault("transId")
            ?? payload.GetValueOrDefault("transactionId")
            ?? payload.GetValueOrDefault("referenceCode")
            ?? payload.GetValueOrDefault("id")
            ?? orderRef)?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(orderRef))
            return (true, false, "Thiếu mã đơn hàng/bản ghi trong IPN.");

        var statusField = plugin.Config.GetValueOrDefault("statusField")?.ToString() ?? "payment_status";
        var txField = plugin.Config.GetValueOrDefault("txField")?.ToString() ?? "payment_tx_id";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var findCmd = new NpgsqlCommand("""
            SELECT id, table_id, data::text
            FROM app_records
            WHERE deleted_at IS NULL
              AND (@appId IS NULL OR app_id = @appId)
              AND (id = @ref OR LOWER(record_key) = LOWER(@ref))
            LIMIT 1
            """, conn);
        findCmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)plugin.AppId ?? DBNull.Value });
        findCmd.Parameters.AddWithValue("ref", orderRef);

        string? recId = null, tblId = null;
        Dictionary<string, object?>? currentData = null;
        await using (var r = await findCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                recId = r.GetString(0);
                tblId = r.GetString(1);
                currentData = RecordWriteService.ParseJsonDict(r.GetString(2));
            }
        }

        if (recId == null)
        {
            await _integrations.LogIntegrationAsync("INBOUND", "PAYMENT_IPN", plugin.Id, plugin.AppId, null, 404, false, $"Không tìm thấy bản ghi khớp '{orderRef}'");
            return (true, false, $"Không tìm thấy bản ghi khớp '{orderRef}'.");
        }

        // Idempotent: if already PAID with same/existing tx, do not re-write
        if (string.Equals(currentData?.GetValueOrDefault(statusField)?.ToString(), "PAID", StringComparison.OrdinalIgnoreCase))
        {
            await _integrations.LogIntegrationAsync("INBOUND", "PAYMENT_IPN", plugin.Id, plugin.AppId, tblId, 200, true, $"IPN trùng (đã PAID) cho {orderRef}");
            return (true, false, "Giao dịch đã được ghi nhận trước đó (idempotent).");
        }

        await _writer.PatchRecordCoreAsync(
            recId,
            new Dictionary<string, object?>
            {
                [statusField] = "PAID",
                [txField] = txId,
                ["payment_provider"] = providerCode,
                ["paid_at"] = DateTime.UtcNow.ToString("O")
            },
            actor: $"ipn:{providerCode}",
            isAdmin: true,
            auditNote: $"Xác nhận thanh toán IPN từ {providerCode} (Mã GD: {txId})");

        await _integrations.LogIntegrationAsync("INBOUND", "PAYMENT_IPN", plugin.Id, plugin.AppId, tblId, 200, true, $"Xác nhận PAID cho {orderRef} (TX: {txId})");
        return (true, true, $"Đã cập nhật trạng thái thanh toán PAID cho {orderRef}.");
    }

    public static bool VerifyIpnSignature(
        string providerCode,
        string secret,
        Dictionary<string, object?> payload,
        string rawBody,
        string? signatureHeader)
    {
        if (providerCode.Equals("vnpay", StringComparison.OrdinalIgnoreCase))
        {
            var provided = payload.GetValueOrDefault("vnp_SecureHash")?.ToString();
            if (string.IsNullOrWhiteSpace(provided)) return false;
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in payload)
            {
                if (kv.Key.StartsWith("vnp_", StringComparison.Ordinal)
                    && kv.Key is not ("vnp_SecureHash" or "vnp_SecureHashType")
                    && kv.Value != null)
                {
                    sorted[kv.Key] = kv.Value.ToString() ?? "";
                }
            }
            var data = string.Join("&", sorted.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            var expected = ComputeHmacSha512Hex(secret, data);
            return FixedTimeEqualsIgnoreCase(provided, expected);
        }

        if (!string.IsNullOrWhiteSpace(signatureHeader) &&
            signatureHeader.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase))
        {
            var providedApiKey = signatureHeader[7..].Trim();
            return FixedTimeEqualsIgnoreCase(providedApiKey, secret);
        }

        var sig = payload.GetValueOrDefault("signature")?.ToString()
            ?? payload.GetValueOrDefault("mac")?.ToString()
            ?? signatureHeader;
        if (string.IsNullOrWhiteSpace(sig)) return false;
        if (sig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) sig = sig[7..];

        var expectedHmac = ComputeHmacSha256Hex(secret, rawBody);
        return FixedTimeEqualsIgnoreCase(sig, expectedHmac);
    }

    private async Task<(bool Ok, string Body)> PostJsonAsync(string url, string? secret, object body)
    {
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(secret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var resp = await client.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        return (resp.IsSuccessStatusCode, text);
    }

    public static string ComputeHmacSha256Hex(string secret, string data) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    public static Dictionary<string, object?> BuildWriteBackPatch(
        Dictionary<string, string>? fieldMapping,
        PluginExecutionResult result)
    {
        var written = new Dictionary<string, object?>();
        if (!result.Success || fieldMapping == null || fieldMapping.Count == 0)
            return written;

        static string? FirstNonEmpty(Dictionary<string, object?> data, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (data.TryGetValue(k, out var val) && val != null)
                {
                    var s = val.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return null;
        }

        if (fieldMapping.TryGetValue("writeBackRefField", out var refCol) && !string.IsNullOrWhiteSpace(refCol))
        {
            var refVal = FirstNonEmpty(result.Data, "invoiceNo", "signTransactionId", "transactionRef", "messageId", "invoiceRef");
            if (refVal != null) written[refCol.Trim()] = refVal;
        }
        if (fieldMapping.TryGetValue("writeBackLookupCodeField", out var lookupCol) && !string.IsNullOrWhiteSpace(lookupCol))
        {
            var val = FirstNonEmpty(result.Data, "lookupCode", "documentHash");
            if (val != null) written[lookupCol.Trim()] = val;
        }
        if (fieldMapping.TryGetValue("writeBackUrlField", out var urlCol) && !string.IsNullOrWhiteSpace(urlCol))
        {
            var urlVal = FirstNonEmpty(result.Data, "qrUrl", "paymentUrl", "pdfUrl");
            if (urlVal != null) written[urlCol.Trim()] = urlVal;
        }
        if (fieldMapping.TryGetValue("writeBackStatusField", out var statusCol) && !string.IsNullOrWhiteSpace(statusCol))
        {
            var stVal = FirstNonEmpty(result.Data, "cqtStatus", "signStatus", "deliveryStatus") ?? "EXECUTED";
            written[statusCol.Trim()] = stVal;
        }

        return written;
    }

    public async Task<(AppRecordDto? UpdatedRecord, Dictionary<string, object?> WrittenFields, string? Warning)> TryWriteBackPluginResultAsync(
        string recordId,
        AppPluginBindingDto? binding,
        PluginExecutionResult result,
        string actor,
        bool isAdmin)
    {
        if (!result.Success || binding == null || string.IsNullOrWhiteSpace(recordId))
            return (null, new Dictionary<string, object?>(), null);

        var written = BuildWriteBackPatch(binding.FieldMapping, result);
        if (written.Count == 0)
            return (null, written, null);

        try
        {
            var updated = await _writer.PatchRecordCoreAsync(
                recordId,
                written,
                actor: actor,
                isAdmin: isAdmin,
                auditNote: $"Ghi ngược tự động từ plugin {result.ProviderCode} ({result.Operation})");
            return (updated, written, null);
        }
        catch (Exception ex)
        {
            return (null, new Dictionary<string, object?>(), ex.Message);
        }
    }

    public static string ComputeHmacSha512Hex(string secret, string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    private static bool FixedTimeEqualsIgnoreCase(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a.Trim().ToLowerInvariant());
        var bb = Encoding.UTF8.GetBytes(b.Trim().ToLowerInvariant());
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
