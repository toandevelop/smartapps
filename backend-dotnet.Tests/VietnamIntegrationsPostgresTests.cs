using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class VietnamIntegrationsPostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public VietnamIntegrationsPostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [Fact]
    public void PluginCatalog_IncludesVietnameseEnterpriseProviders()
    {
        var codes = IntegrationService.GetPluginCatalog().Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[]
        {
            "misa-meinvoice", "viettel-sinvoice", "vnpt-invoice", "bkav-ehoadon",
            "vnpt-smartca", "viettel-mysign", "misa-esign",
            "vnpay", "momo", "zalopay", "vietqr", "vietinbank-api", "vietcombank-api",
            "viettel-sms", "vinaphone-sms", "zalo-zns"
        })
        {
            Assert.Contains(expected, codes);
        }
    }

    [Fact]
    public void BuildWriteBackPatch_MapsConfiguredOutputFieldsSafely()
    {
        var mapping = new Dictionary<string, string>
        {
            ["writeBackRefField"] = "so_hoa_don",
            ["writeBackLookupCodeField"] = "ma_tra_cuu",
            ["writeBackUrlField"] = "link_qr",
            ["writeBackStatusField"] = "trang_thai_hd"
        };
        var okResult = new PluginExecutionResult
        {
            Success = true,
            ProviderCode = "misa-meinvoice",
            Operation = "issue_einvoice",
            Data = new()
            {
                ["invoiceNo"] = "000123",
                ["lookupCode"] = "CQT-XYZ",
                ["cqtStatus"] = "ISSUED_SANDBOX"
            }
        };

        var patch = VietnamIntegrationService.BuildWriteBackPatch(mapping, okResult);
        Assert.Equal("000123", patch["so_hoa_don"]);
        Assert.Equal("CQT-XYZ", patch["ma_tra_cuu"]);
        Assert.Equal("ISSUED_SANDBOX", patch["trang_thai_hd"]);
        Assert.False(patch.ContainsKey("link_qr"));

        var failedPatch = VietnamIntegrationService.BuildWriteBackPatch(mapping, new PluginExecutionResult { Success = false });
        Assert.Empty(failedPatch);
    }

    [PostgresFact]
    public async Task VietnamPlugins_ExecuteEInvoiceSignSmsVietQrAndVerifyVnpayIpn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var integrations = _fx.Services.GetRequiredService<IntegrationService>();
        var vietnam = _fx.Services.GetRequiredService<VietnamIntegrationService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_vn_{suffix}", Code = $"VN_{suffix}".ToUpperInvariant(), Name = $"VN {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_vn_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_vn_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"vn_{suffix}", DisplayName = "Orders" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "order_code", ColumnName = "Order Code", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "phone", ColumnName = "Phone", DataType = "STRING", OrderIndex = 2 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER", OrderIndex = 3 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "payment_status", ColumnName = "Payment Status", DataType = "STRING", OrderIndex = 4 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "payment_tx_id", ColumnName = "Payment TX", DataType = "STRING", OrderIndex = 5 });

        // Install & enable VietQR, MISA meInvoice, VNPT SmartCA, Viettel SMS, VNPay
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "vietqr",
            Name = "VietQR",
            Category = "BANKING",
            Config = new() { ["bankBin"] = "970415", ["accountNo"] = "113366668888", ["accountName"] = "CONG TY SMARTAPPS" },
            AppId = app.Id,
            IsEnabled = true
        });
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "misa-meinvoice",
            Name = "MISA meInvoice",
            Category = "EINVOICE",
            Config = new() { ["taxCode"] = "0101234567", ["environment"] = "sandbox" },
            AppId = app.Id,
            IsEnabled = true
        });
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "vnpt-smartca",
            Name = "VNPT SmartCA",
            Category = "DIGITAL_SIGN",
            Config = new() { ["signerUid"] = "0101234567", ["environment"] = "sandbox" },
            AppId = app.Id,
            IsEnabled = true
        });
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "viettel-sms",
            Name = "Viettel SMS",
            Category = "SMS_VN",
            Config = new() { ["brandname"] = "SMARTAPPS", ["environment"] = "sandbox" },
            AppId = app.Id,
            IsEnabled = true
        });
        var vnpaySecret = $"VNPAY_SEC_{suffix}";
        await integrations.UpsertPluginAsync(new UpsertPluginRequest
        {
            Code = "vnpay",
            Name = "VNPay",
            Category = "PAYMENT",
            Config = new() { ["tmnCode"] = "TMN001", ["environment"] = "sandbox" },
            SecretValue = vnpaySecret,
            AppId = app.Id,
            IsEnabled = true
        });

        // 1) Execute VietQR, E-Invoice, Digital Sign
        var qrRes = await vietnam.ExecutePluginAsync("vietqr", "create_payment", new() { ["orderRef"] = $"ORD-{suffix}", ["amount"] = 250000 });
        Assert.True(qrRes.Success);
        Assert.Contains("img.vietqr.io/image/970415-113366668888", qrRes.Data["qrUrl"]?.ToString());

        var invRes = await vietnam.ExecutePluginAsync("misa-meinvoice", "issue_einvoice", new() { ["orderRef"] = $"ORD-{suffix}", ["amount"] = 250000 });
        Assert.True(invRes.Success);
        Assert.NotNull(invRes.Data["invoiceNo"]);

        var signRes = await vietnam.ExecutePluginAsync("vnpt-smartca", "request_sign", new() { ["documentId"] = $"HD-{suffix}" });
        Assert.True(signRes.Success);
        Assert.Equal("PENDING_USER_CONFIRM", signRes.Data["signStatus"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vietnam.ExecutePluginAsync("vnpt-smartca", "complete_local_sign", new()
            {
                ["documentHash"] = signRes.Data["documentHash"]!,
                ["signatureBase64"] = "not!valid!base64"
            }));

        var localSigned = await vietnam.ExecutePluginAsync("vnpt-smartca", "complete_local_sign", new()
        {
            ["signTransactionId"] = signRes.Data["signTransactionId"]!,
            ["documentHash"] = signRes.Data["documentHash"]!,
            ["signatureBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("sig-bytes")),
            ["certificateSubject"] = "CONG TY SMARTAPPS",
            ["certificateSerial"] = $"SER-{suffix}"
        });
        Assert.True(localSigned.Success);
        Assert.Equal("SIGNED_LOCAL_TOKEN", localSigned.Data["signStatus"]);

        // 2) Automation RUN_PLUGIN sends Viettel SMS on RECORD_CREATED
        await automations.UpsertAutomationAsync(new UpsertAutomationRequest
        {
            Name = $"Send SMS {suffix}",
            SourceAppId = app.Id,
            SourceTableId = tbl.Id,
            TriggerEvent = "RECORD_CREATED",
            ActionType = "RUN_PLUGIN",
            FieldMappings = new Dictionary<string, string>
            {
                ["pluginCode"] = "viettel-sms",
                ["operation"] = "send_sms",
                ["phone"] = "{{phone}}",
                ["message"] = "Don hang {{order_code}} da duoc tiep nhan"
            }
        });

        var (order, triggered) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            Data = new Dictionary<string, object?>
            {
                ["order_code"] = $"ORD-{suffix}",
                ["phone"] = "0988123456",
                ["amount"] = 250000,
                ["payment_status"] = "UNPAID"
            }
        }, "admin", true);

        Assert.Contains($"Send SMS {suffix}", triggered);
        var logs = await integrations.ListIntegrationLogsAsync(30);
        Assert.Contains(logs, l => l.SourceType == "PLUGIN_SEND_SMS" && l.Success);

        // 3) VNPay IPN: invalid hash -> 401; valid HMAC-SHA512 -> 200 & updates PAID; duplicate -> idempotent
        var ipnFields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = "25000000",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = "TMN001",
            ["vnp_TransactionNo"] = $"VNP{suffix}",
            ["vnp_TxnRef"] = order.RecordKey!
        };
        var signData = string.Join("&", ipnFields.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var validHash = VietnamIntegrationService.ComputeHmacSha512Hex(vnpaySecret, signData);

        var badPayload = ipnFields.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        badPayload["vnp_SecureHash"] = "0000badhash";
        var badRes = await _fx.Client.PostAsJsonAsync("/api/integrations/payments/vnpay/ipn", badPayload);
        Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);

        var goodPayload = ipnFields.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        goodPayload["vnp_SecureHash"] = validHash;
        var goodRes = await _fx.Client.PostAsJsonAsync("/api/integrations/payments/vnpay/ipn", goodPayload);
        Assert.Equal(HttpStatusCode.OK, goodRes.StatusCode);
        var goodJson = await goodRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(goodJson.GetProperty("updated").GetBoolean());

        var updatedOrder = await records.GetRecordByIdAsync(order.Id);
        Assert.Equal("PAID", updatedOrder!.Data["payment_status"]?.ToString());
        Assert.Equal($"VNP{suffix}", updatedOrder.Data["payment_tx_id"]?.ToString());

        // Duplicate IPN is idempotent (updated == false)
        var dupRes = await _fx.Client.PostAsJsonAsync("/api/integrations/payments/vnpay/ipn", goodPayload);
        Assert.Equal(HttpStatusCode.OK, dupRes.StatusCode);
        var dupJson = await dupRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(dupJson.GetProperty("updated").GetBoolean());
    }
}
