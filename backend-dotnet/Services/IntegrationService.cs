using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class IntegrationService
{
    private readonly string _connStr;
    private readonly IHttpClientFactory _httpFactory;

    public IntegrationService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _httpFactory = httpFactory;
    }

    private static string HashKey(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

    public async Task<List<SysApiKeyDto>> ListApiKeysAsync(string? appId = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT k.id, k.name, k.key_prefix, k.app_id, ap.code, k.table_id,
                   k.scopes::text, k.is_active, k.last_used_at, k.created_by, k.created_at,
                   k.expires_at, k.rotated_at
            FROM sys_api_keys k
            LEFT JOIN sys_apps ap ON ap.id = k.app_id
            WHERE (@appId IS NULL OR k.app_id = @appId)
            ORDER BY k.created_at DESC
            """;
        var list = new List<SysApiKeyDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId
        });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(ReadApiKey(r));
        }
        return list;
    }

    public async Task<(SysApiKeyDto KeyInfo, string RawKey)> CreateApiKeyAsync(CreateApiKeyRequest req, string createdBy)
    {
        var rawSecret = $"sk_live_{Guid.NewGuid():N}";
        var prefix = rawSecret[..14] + "...";
        var hash = HashKey(rawSecret);
        var id = $"key_{Guid.NewGuid().ToString("N")[..10]}";
        var scopes = req.Scopes?.Count > 0 ? req.Scopes : new List<string> { "records:read", "records:write" };
        DateTime? expiresAt = req.ExpiresInDays is > 0 ? DateTime.UtcNow.AddDays(req.ExpiresInDays.Value) : null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_api_keys (id, name, key_prefix, key_hash, app_id, table_id, scopes, created_by, expires_at)
            VALUES (@id, @name, @pref, @hash, @app, @tbl, @sc::jsonb, @cb, @exp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("pref", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("app", req.AppId);
        cmd.Parameters.AddWithValue("tbl", string.IsNullOrWhiteSpace(req.TableId) ? DBNull.Value : req.TableId.Trim());
        cmd.Parameters.AddWithValue("sc", JsonSerializer.Serialize(scopes));
        cmd.Parameters.AddWithValue("cb", createdBy);
        cmd.Parameters.Add(new NpgsqlParameter("exp", NpgsqlDbType.TimestampTz) { Value = (object?)expiresAt ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();

        var info = (await ListApiKeysAsync(req.AppId)).First(k => k.Id == id);
        return (info, rawSecret);
    }

    public async Task<(SysApiKeyDto KeyInfo, string RawKey)?> RotateApiKeyAsync(string id)
    {
        var rawSecret = $"sk_live_{Guid.NewGuid():N}";
        var prefix = rawSecret[..14] + "...";
        var hash = HashKey(rawSecret);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_api_keys
            SET key_prefix = @pref,
                key_hash = @hash,
                rotated_at = CURRENT_TIMESTAMP
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("pref", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        if (await cmd.ExecuteNonQueryAsync() == 0) return null;

        var info = (await ListApiKeysAsync()).First(k => k.Id == id);
        return (info, rawSecret);
    }

    public async Task<bool> ToggleKeyAsync(string id, bool isActive)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sys_api_keys SET is_active = @a WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("a", isActive);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteKeyAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_api_keys WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<SysApiKeyDto?> ValidateRawKeyAsync(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        var hash = HashKey(rawKey.Trim());

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        SysApiKeyDto? found = null;
        await using (var cmd = new NpgsqlCommand("""
            SELECT k.id, k.name, k.key_prefix, k.app_id, ap.code, k.table_id,
                   k.scopes::text, k.is_active, k.last_used_at, k.created_by, k.created_at,
                   k.expires_at, k.rotated_at
            FROM sys_api_keys k
            LEFT JOIN sys_apps ap ON ap.id = k.app_id
            WHERE k.key_hash = @h
              AND k.is_active = TRUE
              AND (k.expires_at IS NULL OR k.expires_at > CURRENT_TIMESTAMP)
            LIMIT 1
            """, conn))
        {
            cmd.Parameters.AddWithValue("h", hash);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) found = ReadApiKey(r);
        }

        if (found != null)
        {
            await using var up = new NpgsqlCommand("UPDATE sys_api_keys SET last_used_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
            up.Parameters.AddWithValue("id", found.Id);
            await up.ExecuteNonQueryAsync();
        }
        return found;
    }

    public async Task<List<ExternalDeltaRecordDto>> GetDeltaRecordsAsync(string tableId, DateTime? since, int limit = 100)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, record_key, data::text, status, COALESCE(approval_status, 'DRAFT'),
                   COALESCE(version, 1), deleted_at IS NOT NULL AS is_deleted, updated_at, deleted_at
            FROM app_records
            WHERE table_id = @tbl
              AND (@since IS NULL OR updated_at >= @since OR (deleted_at IS NOT NULL AND deleted_at >= @since))
            ORDER BY updated_at ASC, id ASC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = (object?)since ?? DBNull.Value });
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 500));

        var list = new List<ExternalDeltaRecordDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ExternalDeltaRecordDto
            {
                Id = r.GetString(0),
                RecordKey = r.IsDBNull(1) ? null : r.GetString(1),
                Data = RecordWriteService.ParseJsonDict(r.GetString(2)),
                Status = r.GetString(3),
                ApprovalStatus = r.GetString(4),
                Version = r.GetInt32(5),
                IsDeleted = r.GetBoolean(6),
                UpdatedAt = r.GetDateTime(7),
                DeletedAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
            });
        }
        return list;
    }

    // ─── Plugin / Connector Registry ───────────────────────────────────────────

    public static IReadOnlyList<PluginCatalogItemDto> GetPluginCatalog() =>
    [
        // General / Core
        new()
        {
            Code = "inbound-webhook",
            Name = "Inbound Webhook Receiver",
            Category = "WEBHOOK_INBOUND",
            Version = "1.0.0",
            Description = "Nhận dữ liệu JSON từ hệ thống ngoài qua HMAC-SHA256 hoặc Bearer/Plugin Token.",
            RequiredConfigKeys = ["allowedTables"],
            SupportedOperations = ["inbound_record"]
        },
        new()
        {
            Code = "rest-connector",
            Name = "Generic REST API Connector",
            Category = "ERP_CRM",
            Version = "1.0.0",
            Description = "Kết nối đồng bộ 2 chiều với ERP, CRM, Kế toán qua REST/JSON.",
            RequiredConfigKeys = ["baseUrl", "healthPath"],
            SupportedOperations = ["http_request"]
        },
        new()
        {
            Code = "slack-teams-webhook",
            Name = "Slack / MS Teams Notifier",
            Category = "NOTIFICATION",
            Version = "1.0.0",
            Description = "Gửi cảnh báo phê duyệt và thông báo tự động hóa tới kênh Slack hoặc Teams.",
            RequiredConfigKeys = ["webhookUrl"],
            SupportedOperations = ["send_notification"]
        },
        new()
        {
            Code = "zalo-oa-connector",
            Name = "Zalo OA Messaging Plugin",
            Category = "NOTIFICATION",
            Version = "1.0.0",
            Description = "Gửi thông báo ZNS / OA tới nhân sự và khách hàng qua Zalo Official Account.",
            RequiredConfigKeys = ["oaId", "baseUrl"],
            SupportedOperations = ["send_zns"]
        },
        // Hóa đơn điện tử Việt Nam (E-Invoice)
        new()
        {
            Code = "misa-meinvoice",
            Name = "MISA meInvoice (Hóa đơn điện tử)",
            Category = "EINVOICE",
            Version = "1.0.0",
            Description = "Phát hành, tra cứu và tải PDF/XML hóa đơn điện tử MISA meInvoice (TT78/NĐ123).",
            RequiredConfigKeys = ["taxCode", "appId", "baseUrl", "environment"],
            SupportedOperations = ["issue_einvoice"]
        },
        new()
        {
            Code = "viettel-sinvoice",
            Name = "Viettel S-Invoice (Hóa đơn điện tử)",
            Category = "EINVOICE",
            Version = "1.0.0",
            Description = "Phát hành và đồng bộ mã cơ quan thuế qua Viettel S-Invoice API.",
            RequiredConfigKeys = ["taxCode", "username", "baseUrl", "environment"],
            SupportedOperations = ["issue_einvoice"]
        },
        new()
        {
            Code = "vnpt-invoice",
            Name = "VNPT-Invoice (Hóa đơn điện tử)",
            Category = "EINVOICE",
            Version = "1.0.0",
            Description = "Tích hợp phát hành hóa đơn điện tử VNPT-Invoice qua Portal/PublishService.",
            RequiredConfigKeys = ["taxCode", "account", "baseUrl", "environment"],
            SupportedOperations = ["issue_einvoice"]
        },
        new()
        {
            Code = "bkav-ehoadon",
            Name = "BKAV eHoadon",
            Category = "EINVOICE",
            Version = "1.0.0",
            Description = "Phát hành và tra cứu hóa đơn điện tử BKAV eHoadon.",
            RequiredConfigKeys = ["partnerGuid", "taxCode", "baseUrl", "environment"],
            SupportedOperations = ["issue_einvoice"]
        },
        // Chữ ký số từ xa (Digital Signature / Remote Signing)
        new()
        {
            Code = "vnpt-smartca",
            Name = "VNPT SmartCA (Chữ ký số từ xa)",
            Category = "DIGITAL_SIGN",
            Version = "1.0.0",
            Description = "Ký số từ xa (Remote Signing) tài liệu PDF/XML qua VNPT SmartCA.",
            RequiredConfigKeys = ["clientId", "signerUid", "baseUrl", "environment"],
            SupportedOperations = ["request_sign", "complete_local_sign"]
        },
        new()
        {
            Code = "viettel-mysign",
            Name = "Viettel MySign (Chữ ký số từ xa)",
            Category = "DIGITAL_SIGN",
            Version = "1.0.0",
            Description = "Xác thực ký số PAdES/XAdES qua ứng dụng Viettel MySign.",
            RequiredConfigKeys = ["clientId", "userId", "baseUrl", "environment"],
            SupportedOperations = ["request_sign", "complete_local_sign"]
        },
        new()
        {
            Code = "misa-esign",
            Name = "MISA eSign (Chữ ký số)",
            Category = "DIGITAL_SIGN",
            Version = "1.0.0",
            Description = "Ký số hợp đồng, chứng từ từ xa qua MISA eSign.",
            RequiredConfigKeys = ["appId", "taxCode", "baseUrl", "environment"],
            SupportedOperations = ["request_sign", "complete_local_sign"]
        },
        // Ngân hàng & Cổng thanh toán Việt Nam
        new()
        {
            Code = "vnpay",
            Name = "VNPay Payment Gateway",
            Category = "PAYMENT",
            Version = "1.0.0",
            Description = "Tạo URL thanh toán VNPay và xác thực IPN (HMAC-SHA512) tự động đối soát.",
            RequiredConfigKeys = ["tmnCode", "baseUrl", "returnUrl", "environment"],
            SupportedOperations = ["create_payment", "verify_ipn"]
        },
        new()
        {
            Code = "momo",
            Name = "MoMo Wallet Payment",
            Category = "PAYMENT",
            Version = "1.0.0",
            Description = "Tạo giao dịch ví MoMo / QR và xác thực IPN (HMAC-SHA256).",
            RequiredConfigKeys = ["partnerCode", "accessKey", "baseUrl", "environment"],
            SupportedOperations = ["create_payment", "verify_ipn"]
        },
        new()
        {
            Code = "zalopay",
            Name = "ZaloPay Gateway",
            Category = "PAYMENT",
            Version = "1.0.0",
            Description = "Tạo đơn thanh toán ZaloPay và xác thực callback MAC (HMAC-SHA256).",
            RequiredConfigKeys = ["appId", "baseUrl", "environment"],
            SupportedOperations = ["create_payment", "verify_ipn"]
        },
        new()
        {
            Code = "vietqr",
            Name = "VietQR / Napas 247",
            Category = "BANKING",
            Version = "1.0.0",
            Description = "Sinh mã VietQR động theo số tài khoản ngân hàng, số tiền và nội dung đối soát.",
            RequiredConfigKeys = ["bankBin", "accountNo", "accountName"],
            SupportedOperations = ["create_payment"]
        },
        new()
        {
            Code = "vietinbank-api",
            Name = "VietinBank iPay / Open API",
            Category = "BANKING",
            Version = "1.0.0",
            Description = "Kết nối thu hộ, tạo QR và nhận biến động số dư / đối soát VietinBank.",
            RequiredConfigKeys = ["clientId", "merchantId", "accountNo", "baseUrl", "environment"],
            SupportedOperations = ["create_payment", "verify_ipn"]
        },
        new()
        {
            Code = "vietcombank-api",
            Name = "Vietcombank VCB Digibank / API",
            Category = "BANKING",
            Version = "1.0.0",
            Description = "Kết nối thanh toán, QR định danh và đối soát giao dịch Vietcombank.",
            RequiredConfigKeys = ["merchantId", "accountNo", "baseUrl", "environment"],
            SupportedOperations = ["create_payment", "verify_ipn"]
        },
        // SMS Brandname & ZNS Việt Nam
        new()
        {
            Code = "viettel-sms",
            Name = "Viettel SMS Brandname",
            Category = "SMS_VN",
            Version = "1.0.0",
            Description = "Gửi tin nhắn OTP/CSKH qua đầu số thương hiệu Viettel SMS Brandname.",
            RequiredConfigKeys = ["brandname", "cpCode", "baseUrl", "environment"],
            SupportedOperations = ["send_sms"]
        },
        new()
        {
            Code = "vinaphone-sms",
            Name = "VNPT / VinaPhone SMS Brandname",
            Category = "SMS_VN",
            Version = "1.0.0",
            Description = "Gửi tin nhắn thương hiệu qua hạ tầng VNPT / VinaPhone SMS.",
            RequiredConfigKeys = ["brandname", "agentId", "baseUrl", "environment"],
            SupportedOperations = ["send_sms"]
        },
        new()
        {
            Code = "zalo-zns",
            Name = "Zalo ZNS (Thông báo giao dịch)",
            Category = "SMS_VN",
            Version = "1.0.0",
            Description = "Gửi thông báo ZNS theo template đã duyệt tới số điện thoại khách hàng.",
            RequiredConfigKeys = ["oaId", "templateId", "baseUrl", "environment"],
            SupportedOperations = ["send_sms"]
        }
    ];

    public async Task<List<SysPluginDto>> ListPluginsAsync(string? appId = null)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, code, name, category, version, description, manifest::text, config::text,
                   secret_value IS NOT NULL AND secret_value <> '' AS has_secret,
                   app_id, is_enabled, last_tested_at, last_test_status, last_test_message,
                   created_by, created_at, updated_at
            FROM sys_plugins
            WHERE (@appId IS NULL OR app_id = @appId OR app_id IS NULL)
            ORDER BY updated_at DESC, name ASC
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(appId) ? DBNull.Value : appId
        });
        var list = new List<SysPluginDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadPlugin(r));
        return list;
    }

    public async Task<SysPluginDto?> GetPluginByCodeAsync(string code)
    {
        var loaded = await GetPluginRuntimeConfigAsync(code);
        return loaded?.Plugin;
    }

    public async Task<(SysPluginDto Plugin, string? Secret)?> GetPluginRuntimeConfigAsync(string code)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, code, name, category, version, description, manifest::text, config::text,
                   secret_value IS NOT NULL AND secret_value <> '' AS has_secret,
                   app_id, is_enabled, last_tested_at, last_test_status, last_test_message,
                   created_by, created_at, updated_at, secret_value
            FROM sys_plugins
            WHERE LOWER(code) = LOWER(@code)
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("code", code.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (ReadPlugin(r), r.IsDBNull(17) ? null : r.GetString(17));
    }

    public async Task<SysPluginDto> UpsertPluginAsync(UpsertPluginRequest req, string actor = "admin")
    {
        var code = req.Code.Trim().ToLowerInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"plg_{code}_{Guid.NewGuid().ToString("N")[..6]}" : req.Id.Trim();
        var hasNewSecret = !string.IsNullOrWhiteSpace(req.SecretValue);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_plugins (id, code, name, category, version, description, manifest, config, secret_value, app_id, is_enabled, created_by, updated_at)
            VALUES (@id, @code, @name, @cat, @ver, @desc, @man::jsonb, @cfg::jsonb, @sec, @app, @en, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (code) DO UPDATE SET
                name = EXCLUDED.name,
                category = EXCLUDED.category,
                version = EXCLUDED.version,
                description = EXCLUDED.description,
                manifest = EXCLUDED.manifest,
                config = EXCLUDED.config,
                secret_value = CASE WHEN @hasSec THEN EXCLUDED.secret_value ELSE sys_plugins.secret_value END,
                app_id = EXCLUDED.app_id,
                is_enabled = EXCLUDED.is_enabled,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("cat", string.IsNullOrWhiteSpace(req.Category) ? "CUSTOM" : req.Category.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("ver", string.IsNullOrWhiteSpace(req.Version) ? "1.0.0" : req.Version.Trim());
        cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("man", JsonSerializer.Serialize(req.Manifest ?? new()));
        cmd.Parameters.AddWithValue("cfg", JsonSerializer.Serialize(req.Config ?? new()));
        cmd.Parameters.Add(new NpgsqlParameter("sec", NpgsqlDbType.Text) { Value = hasNewSecret ? req.SecretValue!.Trim() : DBNull.Value });
        cmd.Parameters.AddWithValue("hasSec", hasNewSecret);
        cmd.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(req.AppId) ? DBNull.Value : req.AppId.Trim() });
        cmd.Parameters.AddWithValue("en", req.IsEnabled);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();

        return (await GetPluginByCodeAsync(code))!;
    }

    public async Task<List<AppPluginBindingDto>> ListAppPluginBindingsAsync(string appId, bool enabledOnly = false)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT p.id, p.code, p.name, p.category, p.description,
                   p.is_enabled AS system_enabled,
                   (p.secret_value IS NOT NULL AND p.secret_value <> '') AS has_secret,
                   p.last_test_status,
                   p.app_id AS plugin_app_id,
                   b.id AS binding_id,
                   b.is_enabled AS binding_enabled,
                   b.table_id,
                   b.field_mapping::text,
                   b.updated_at
            FROM sys_plugins p
            LEFT JOIN sys_app_plugin_bindings b
              ON LOWER(b.plugin_code) = LOWER(p.code) AND b.app_id = @appId
            WHERE (p.app_id IS NULL OR p.app_id = @appId)
            ORDER BY p.is_enabled DESC, p.name ASC
            """, conn);
        cmd.Parameters.AddWithValue("appId", appId.Trim());

        var list = new List<AppPluginBindingDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var sysEnabled = r.GetBoolean(5);
            var pluginAppId = r.IsDBNull(8) ? null : r.GetString(8);
            var bindingId = r.IsDBNull(9) ? null : r.GetString(9);
            var isAppEnabled = !r.IsDBNull(10)
                ? r.GetBoolean(10)
                : (!string.IsNullOrWhiteSpace(pluginAppId) && string.Equals(pluginAppId, appId.Trim(), StringComparison.OrdinalIgnoreCase) && sysEnabled);

            if (enabledOnly && (!sysEnabled || !isAppEnabled))
                continue;

            var mappingJson = r.IsDBNull(12) ? "{}" : r.GetString(12);
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson) ?? new();

            list.Add(new AppPluginBindingDto
            {
                Id = bindingId ?? r.GetString(0),
                AppId = appId.Trim(),
                PluginCode = r.GetString(1),
                PluginName = r.GetString(2),
                Category = r.GetString(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4),
                IsSystemEnabled = sysEnabled,
                HasSecret = r.GetBoolean(6),
                LastTestStatus = r.IsDBNull(7) ? null : r.GetString(7),
                IsAppEnabled = isAppEnabled,
                TableId = r.IsDBNull(11) ? null : r.GetString(11),
                FieldMapping = mapping,
                UpdatedAt = r.IsDBNull(13) ? null : r.GetDateTime(13),
            });
        }
        return list;
    }

    public async Task<AppPluginBindingDto?> UpsertAppPluginBindingAsync(
        string appId, string pluginCode, UpsertAppPluginBindingRequest req, string actor = "admin")
    {
        var plugin = await GetPluginByCodeAsync(pluginCode);
        if (plugin == null) return null;

        var id = $"apb_{Guid.NewGuid().ToString("N")[..10]}";
        var normalizedCode = plugin.Code;
        var mappingJson = JsonSerializer.Serialize(req.FieldMapping ?? new Dictionary<string, string>());

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_app_plugin_bindings (id, app_id, plugin_code, is_enabled, table_id, field_mapping, updated_by, updated_at)
            VALUES (@id, @appId, @code, @en, @tableId, @map::jsonb, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (app_id, plugin_code) DO UPDATE SET
                is_enabled = EXCLUDED.is_enabled,
                table_id = EXCLUDED.table_id,
                field_mapping = EXCLUDED.field_mapping,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("appId", appId.Trim());
        cmd.Parameters.AddWithValue("code", normalizedCode);
        cmd.Parameters.AddWithValue("en", req.IsEnabled);
        cmd.Parameters.Add(new NpgsqlParameter("tableId", NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(req.TableId) ? DBNull.Value : req.TableId.Trim()
        });
        cmd.Parameters.AddWithValue("map", mappingJson);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();

        var all = await ListAppPluginBindingsAsync(appId);
        return all.FirstOrDefault(x => string.Equals(x.PluginCode, normalizedCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> TogglePluginAsync(string id, bool isEnabled)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE sys_plugins SET is_enabled = @en, updated_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("en", isEnabled);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeletePluginAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_plugins WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(bool Ok, string Message)> TestPluginConnectionAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? code = null, configJson = null;
        bool hasSecret = false;
        await using (var cmd = new NpgsqlCommand("SELECT code, config::text, secret_value IS NOT NULL AND secret_value <> '' FROM sys_plugins WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return (false, "Không tìm thấy plugin.");
            code = r.GetString(0);
            configJson = r.GetString(1);
            hasSecret = r.GetBoolean(2);
        }

        var cfg = RecordWriteService.ParseJsonDict(configJson!);
        var url = (cfg.GetValueOrDefault("baseUrl") ?? cfg.GetValueOrDefault("webhookUrl"))?.ToString();
        bool ok;
        string msg;

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var healthPath = cfg.GetValueOrDefault("healthPath")?.ToString();
                var targetUrl = string.IsNullOrWhiteSpace(healthPath) ? url : $"{url.TrimEnd('/')}/{healthPath.TrimStart('/')}";
                var client = _httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var resp = await client.GetAsync(targetUrl);
                ok = (int)resp.StatusCode < 500;
                msg = $"HTTP {(int)resp.StatusCode} từ {targetUrl}";
            }
            catch (Exception ex)
            {
                ok = false;
                msg = $"Lỗi kết nối {url}: {ex.Message}";
            }
        }
        else
        {
            ok = hasSecret || code == "inbound-webhook";
            msg = ok
                ? "Cấu hình nội bộ hợp lệ (sẵn sàng nhận sự kiện)."
                : "Thiếu baseUrl/webhookUrl hoặc secret cấu hình.";
        }

        await using (var up = new NpgsqlCommand("""
            UPDATE sys_plugins
            SET last_tested_at = CURRENT_TIMESTAMP,
                last_test_status = @st,
                last_test_message = @msg
            WHERE id = @id
            """, conn))
        {
            up.Parameters.AddWithValue("id", id);
            up.Parameters.AddWithValue("st", ok ? "OK" : "FAILED");
            up.Parameters.AddWithValue("msg", msg);
            await up.ExecuteNonQueryAsync();
        }

        await LogIntegrationAsync("OUTBOUND", "PLUGIN_TEST", id, null, null, ok ? 200 : 502, ok, msg);
        return (ok, msg);
    }

    public async Task<(SysPluginDto? Plugin, bool Authorized)> VerifyPluginInboundAuthAsync(
        string pluginCode, string? tokenHeader, string? hmacSignatureHeader, string rawBody)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, code, name, category, version, description, manifest::text, config::text,
                   secret_value IS NOT NULL AND secret_value <> '' AS has_secret,
                   app_id, is_enabled, last_tested_at, last_test_status, last_test_message,
                   created_by, created_at, updated_at, secret_value
            FROM sys_plugins
            WHERE LOWER(code) = LOWER(@code)
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("code", pluginCode.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (null, false);

        var plugin = ReadPlugin(r);
        var secret = r.IsDBNull(17) ? null : r.GetString(17);
        if (!plugin.IsEnabled) return (plugin, false);
        if (string.IsNullOrWhiteSpace(secret)) return (plugin, false);

        if (!string.IsNullOrWhiteSpace(tokenHeader) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokenHeader.Trim()), Encoding.UTF8.GetBytes(secret)))
        {
            return (plugin, true);
        }

        if (!string.IsNullOrWhiteSpace(hmacSignatureHeader))
        {
            var expectedHex = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
            var provided = hmacSignatureHeader.Trim();
            if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                provided = provided[7..];
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided.ToLowerInvariant()), Encoding.UTF8.GetBytes(expectedHex)))
            {
                return (plugin, true);
            }
        }

        return (plugin, false);
    }

    public async Task LogIntegrationAsync(
        string direction, string sourceType, string sourceId,
        string? appId, string? tableId, int statusCode, bool success, string summary)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_integration_logs (id, direction, source_type, source_id, app_id, table_id, status_code, success, summary)
            VALUES (@id, @dir, @st, @sid, @app, @tbl, @code, @ok, @sum)
            """, conn);
        cmd.Parameters.AddWithValue("id", $"ilg_{Guid.NewGuid().ToString("N")[..12]}");
        cmd.Parameters.AddWithValue("dir", direction);
        cmd.Parameters.AddWithValue("st", sourceType);
        cmd.Parameters.AddWithValue("sid", sourceId);
        cmd.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Varchar) { Value = (object?)appId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("tbl", NpgsqlDbType.Varchar) { Value = (object?)tableId ?? DBNull.Value });
        cmd.Parameters.AddWithValue("code", statusCode);
        cmd.Parameters.AddWithValue("ok", success);
        cmd.Parameters.AddWithValue("sum", summary);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<IntegrationLogDto>> ListIntegrationLogsAsync(int limit = 100)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, direction, source_type, source_id, app_id, table_id, status_code, success, summary, created_at
            FROM app_integration_logs
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 500));
        var list = new List<IntegrationLogDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new IntegrationLogDto
            {
                Id = r.GetString(0),
                Direction = r.GetString(1),
                SourceType = r.GetString(2),
                SourceId = r.GetString(3),
                AppId = r.IsDBNull(4) ? null : r.GetString(4),
                TableId = r.IsDBNull(5) ? null : r.GetString(5),
                StatusCode = r.GetInt32(6),
                Success = r.GetBoolean(7),
                Summary = r.IsDBNull(8) ? null : r.GetString(8),
                CreatedAt = r.GetDateTime(9)
            });
        }
        return list;
    }

    private static SysApiKeyDto ReadApiKey(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        KeyPrefix = r.GetString(2),
        AppId = r.GetString(3),
        AppCode = r.IsDBNull(4) ? null : r.GetString(4),
        TableId = r.IsDBNull(5) ? null : r.GetString(5),
        Scopes = JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new(),
        IsActive = r.GetBoolean(7),
        LastUsedAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
        CreatedBy = r.IsDBNull(9) ? "admin" : r.GetString(9),
        CreatedAt = r.GetDateTime(10),
        ExpiresAt = r.FieldCount > 11 && !r.IsDBNull(11) ? r.GetDateTime(11) : null,
        RotatedAt = r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetDateTime(12) : null
    };

    private static SysPluginDto ReadPlugin(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        Category = r.GetString(3),
        Version = r.GetString(4),
        Description = r.IsDBNull(5) ? null : r.GetString(5),
        Manifest = RecordWriteService.ParseJsonDict(r.GetString(6)),
        Config = RecordWriteService.ParseJsonDict(r.GetString(7)),
        HasSecret = r.GetBoolean(8),
        AppId = r.IsDBNull(9) ? null : r.GetString(9),
        IsEnabled = r.GetBoolean(10),
        LastTestedAt = r.IsDBNull(11) ? null : r.GetDateTime(11),
        LastTestStatus = r.IsDBNull(12) ? null : r.GetString(12),
        LastTestMessage = r.IsDBNull(13) ? null : r.GetString(13),
        CreatedBy = r.IsDBNull(14) ? "admin" : r.GetString(14),
        CreatedAt = r.GetDateTime(15),
        UpdatedAt = r.GetDateTime(16)
    };
}
