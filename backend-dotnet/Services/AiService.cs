using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public partial class AiService
{
    private readonly string _connStr;
    private readonly IHttpClientFactory _httpFactory;
    private readonly RealtimeEventService _realtime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AiService(IConfiguration config, IHttpClientFactory httpFactory, RealtimeEventService realtime)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _httpFactory = httpFactory;
        _realtime = realtime;
    }

    private sealed class AiProviderInternal : AiProviderDto
    {
        public string? RawApiKey { get; set; }
    }

    public static string NormalizeProviderType(string? providerType)
    {
        var t = (providerType ?? "OPENAI").Trim().ToUpperInvariant();
        return t is "GEMINI" or "OPENAI" or "ANTHROPIC" or "OPENAI_COMPATIBLE" ? t : "OPENAI_COMPATIBLE";
    }

    public static string DefaultBaseUrlFor(string providerType) => NormalizeProviderType(providerType) switch
    {
        "GEMINI" => "https://generativelanguage.googleapis.com/v1beta",
        "OPENAI" => "https://api.openai.com/v1",
        "ANTHROPIC" => "https://api.anthropic.com/v1",
        _ => "http://localhost:11434/v1"
    };

    public static string DefaultModelFor(string providerType) => NormalizeProviderType(providerType) switch
    {
        "GEMINI" => "gemini-2.5-flash",
        "OPENAI" => "gpt-4o-mini",
        "ANTHROPIC" => "claude-3-5-haiku-latest",
        _ => "qwen2.5:7b"
    };

    public static List<string> SuggestedModelsFor(string providerType) => NormalizeProviderType(providerType) switch
    {
        "GEMINI" => new List<string> { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash" },
        "OPENAI" => new List<string> { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1", "o3-mini" },
        "ANTHROPIC" => new List<string> { "claude-3-5-haiku-latest", "claude-sonnet-4-20250514", "claude-3-7-sonnet-latest" },
        _ => new List<string> { "qwen2.5:7b", "llama3.1:8b", "deepseek-chat", "meta-llama/llama-3.3-70b-instruct" }
    };

    public static string? MaskApiKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length <= 8) return "••••••••";
        return $"{trimmed[..4]}••••{trimmed[^4..]}";
    }

    public static bool IsProviderConfigured(string providerType, string? baseUrl, string? model, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model)) return false;
        var pt = NormalizeProviderType(providerType);
        if (pt == "OPENAI_COMPATIBLE") return true; // Local Ollama / vLLM may not require an API key
        return !string.IsNullOrWhiteSpace(apiKey);
    }

    public static string StripMarkdownJsonFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0) s = s[(firstNewline + 1)..];
            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) s = s[..lastFence];
        }
        return s.Trim();
    }

    public static object BuildOpenAiChatPayload(
        string model,
        string systemPrompt,
        string userPrompt,
        double temperature,
        int maxTokens,
        bool jsonMode,
        bool includeResponseFormat = true)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = userPrompt });

        if (jsonMode && includeResponseFormat)
        {
            return new
            {
                model,
                messages,
                temperature,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" }
            };
        }

        return new
        {
            model,
            messages,
            temperature,
            max_tokens = maxTokens
        };
    }

    public static object BuildGeminiGeneratePayload(
        string systemPrompt,
        string userPrompt,
        double temperature,
        int maxTokens,
        bool jsonMode)
    {
        var generationConfig = jsonMode
            ? (object)new
            {
                temperature,
                maxOutputTokens = maxTokens,
                responseMimeType = "application/json"
            }
            : new
            {
                temperature,
                maxOutputTokens = maxTokens
            };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            return new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                generationConfig
            };
        }

        return new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig
        };
    }

    public static object BuildAnthropicMessagesPayload(
        string model,
        string systemPrompt,
        string userPrompt,
        double temperature,
        int maxTokens)
    {
        return new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        };
    }

    public static (string Text, int PromptTokens, int CompletionTokens) ExtractTextFromOpenAiResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = string.Empty;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                text = content.GetString() ?? string.Empty;
        }
        int pt = 0, ct = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv)) pt = pv;
            if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv)) ct = cv;
        }
        return (text, pt, ct);
    }

    public static (string Text, int PromptTokens, int CompletionTokens) ExtractTextFromGeminiResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t))
                        sb.Append(t.GetString());
                }
            }
        }
        int pt = 0, ct = 0;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt32(out var pv)) pt = pv;
            if (usage.TryGetProperty("candidatesTokenCount", out var c) && c.TryGetInt32(out var cv)) ct = cv;
        }
        return (sb.ToString(), pt, ct);
    }

    public static (string Text, int PromptTokens, int CompletionTokens) ExtractTextFromAnthropicResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content))
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var t))
                    sb.Append(t.GetString());
            }
        }
        int pt = 0, ct = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var p) && p.TryGetInt32(out var pv)) pt = pv;
            if (usage.TryGetProperty("output_tokens", out var c) && c.TryGetInt32(out var cv)) ct = cv;
        }
        return (sb.ToString(), pt, ct);
    }

    public async Task<AiSystemSettingsDto> GetAiSystemSettingsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT setting_value::text, updated_at FROM sys_system_settings WHERE setting_key = 'ai_config' LIMIT 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new AiSystemSettingsDto();
        var raw = r.IsDBNull(0) ? null : r.GetString(0);
        var updatedAt = r.IsDBNull(1) ? DateTime.UtcNow : r.GetDateTime(1);
        if (string.IsNullOrWhiteSpace(raw)) return new AiSystemSettingsDto { UpdatedAt = updatedAt };
        var dto = JsonSerializer.Deserialize<AiSystemSettingsDto>(raw, JsonOpts) ?? new AiSystemSettingsDto();
        dto.UpdatedAt = updatedAt;
        return dto;
    }

    public async Task<AiSystemSettingsDto> UpdateAiSystemSettingsAsync(AiSystemSettingsDto req, string updatedBy)
    {
        var features = (req.EnabledFeatures ?? new List<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var normalized = new AiSystemSettingsDto
        {
            IsEnabled = req.IsEnabled,
            DefaultProviderId = string.IsNullOrWhiteSpace(req.DefaultProviderId) ? "ai_gemini" : req.DefaultProviderId.Trim(),
            FallbackToRuleEngine = req.FallbackToRuleEngine,
            MaskPiiBeforeCloudLlm = req.MaskPiiBeforeCloudLlm,
            MonthlyTokenBudget = Math.Max(0, req.MonthlyTokenBudget),
            PreferLocalForSensitive = req.PreferLocalForSensitive,
            Temperature = Math.Clamp(req.Temperature, 0.0, 2.0),
            MaxTokens = Math.Clamp(req.MaxTokens, 128, 32768),
            TimeoutSeconds = Math.Clamp(req.TimeoutSeconds, 5, 180),
            SystemPromptPrefix = string.IsNullOrWhiteSpace(req.SystemPromptPrefix)
                ? "Bạn là trợ lý AI doanh nghiệp của nền tảng SmartApps. Trả lời chính xác, súc tích, ưu tiên tiếng Việt chuẩn."
                : req.SystemPromptPrefix.Trim(),
            EnabledFeatures = features,
            UpdatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(normalized, JsonOpts);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_system_settings (setting_key, setting_value, description, updated_by, updated_at)
            VALUES ('ai_config', @val::jsonb, 'Cấu hình AI API dùng chung toàn hệ thống SmartApps', @ub, CURRENT_TIMESTAMP)
            ON CONFLICT (setting_key) DO UPDATE SET
                setting_value = EXCLUDED.setting_value,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("val", json);
        cmd.Parameters.AddWithValue("ub", updatedBy);
        await cmd.ExecuteNonQueryAsync();

        if (!string.IsNullOrWhiteSpace(normalized.DefaultProviderId))
        {
            await using var defCmd = new NpgsqlCommand("""
                UPDATE sys_ai_providers
                SET is_default = (id = @id)
                WHERE is_default = TRUE OR id = @id
                """, conn);
            defCmd.Parameters.AddWithValue("id", normalized.DefaultProviderId);
            await defCmd.ExecuteNonQueryAsync();
        }

        _realtime.Publish("ai_settings_updated", null, null, null, "*", await GetAiStatusAsync());
        return normalized;
    }

    public async Task<List<AiProviderDto>> ListProvidersAsync()
    {
        var list = await LoadProvidersInternalAsync();
        return list.Cast<AiProviderDto>().ToList();
    }

    public async Task<(AiProviderDto? Provider, string? Error)> UpsertProviderAsync(UpsertAiProviderRequest req, string updatedBy)
    {
        var code = (req.Code ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(code))
            return (null, "Mã nhà cung cấp AI không được để trống.");
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return (null, "Tên hiển thị cấu hình AI là bắt buộc.");

        var providerType = NormalizeProviderType(req.ProviderType);
        var baseUrl = string.IsNullOrWhiteSpace(req.BaseUrl)
            ? DefaultBaseUrlFor(providerType)
            : req.BaseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, "Base URL phải bắt đầu bằng http:// hoặc https://.");
        }

        var model = string.IsNullOrWhiteSpace(req.Model)
            ? DefaultModelFor(providerType)
            : req.Model.Trim();

        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"ai_{code.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..4]}"
            : req.Id.Trim();

        var hasNewKey = !string.IsNullOrWhiteSpace(req.ApiKey);
        var timeout = Math.Clamp(req.TimeoutSeconds <= 0 ? 45 : req.TimeoutSeconds, 5, 180);
        double? temp = req.Temperature.HasValue ? Math.Clamp(req.Temperature.Value, 0.0, 2.0) : null;
        int? maxTok = req.MaxTokens.HasValue ? Math.Clamp(req.MaxTokens.Value, 128, 32768) : null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        if (req.IsDefault)
        {
            await using var clearDef = new NpgsqlCommand("UPDATE sys_ai_providers SET is_default = FALSE WHERE is_default = TRUE", conn);
            await clearDef.ExecuteNonQueryAsync();
        }

        var sql = """
            INSERT INTO sys_ai_providers (
                id, code, provider_type, display_name, is_enabled, is_default,
                api_key, base_url, model, temperature, max_tokens, timeout_seconds,
                organization_id, order_index, updated_by, updated_at
            )
            VALUES (
                @id, @code, @pt, @dn, @en, @def,
                @key, @url, @mod, @temp, @maxt, @to,
                @org, @ord, @ub, CURRENT_TIMESTAMP
            )
            ON CONFLICT (id) DO UPDATE SET
                provider_type = EXCLUDED.provider_type,
                display_name = EXCLUDED.display_name,
                is_enabled = EXCLUDED.is_enabled,
                is_default = EXCLUDED.is_default,
                api_key = CASE
                    WHEN @clr THEN NULL
                    WHEN @hasKey THEN EXCLUDED.api_key
                    ELSE sys_ai_providers.api_key
                END,
                base_url = EXCLUDED.base_url,
                model = EXCLUDED.model,
                temperature = EXCLUDED.temperature,
                max_tokens = EXCLUDED.max_tokens,
                timeout_seconds = EXCLUDED.timeout_seconds,
                organization_id = EXCLUDED.organization_id,
                order_index = EXCLUDED.order_index,
                updated_by = EXCLUDED.updated_by,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("pt", providerType);
        cmd.Parameters.AddWithValue("dn", req.DisplayName.Trim());
        cmd.Parameters.AddWithValue("en", req.IsEnabled);
        cmd.Parameters.AddWithValue("def", req.IsDefault);
        cmd.Parameters.AddWithValue("key", hasNewKey ? req.ApiKey!.Trim() : DBNull.Value);
        cmd.Parameters.AddWithValue("hasKey", hasNewKey);
        cmd.Parameters.AddWithValue("clr", req.ClearApiKey);
        cmd.Parameters.AddWithValue("url", baseUrl);
        cmd.Parameters.AddWithValue("mod", model);
        cmd.Parameters.AddWithValue("temp", (object?)temp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxt", (object?)maxTok ?? DBNull.Value);
        cmd.Parameters.AddWithValue("to", timeout);
        cmd.Parameters.AddWithValue("org", string.IsNullOrWhiteSpace(req.OrganizationId) ? DBNull.Value : req.OrganizationId.Trim());
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("ub", updatedBy);
        await cmd.ExecuteNonQueryAsync();

        if (req.IsDefault)
        {
            var sysCfg = await GetAiSystemSettingsAsync();
            sysCfg.DefaultProviderId = id;
            await UpdateAiSystemSettingsAsync(sysCfg, updatedBy);
        }

        _realtime.Publish("ai_settings_updated", null, null, null, "*", await GetAiStatusAsync());
        var saved = (await ListProvidersAsync()).FirstOrDefault(p => p.Id == id);
        return (saved, null);
    }

    public async Task<(bool Ok, string? Error)> SetDefaultProviderAsync(string id, string updatedBy)
    {
        var providers = await LoadProvidersInternalAsync();
        if (providers.All(p => p.Id != id))
            return (false, "Không tìm thấy cấu hình AI.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_ai_providers
            SET is_default = (id = @id), updated_by = @ub, updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ub", updatedBy);
        await cmd.ExecuteNonQueryAsync();

        var sysCfg = await GetAiSystemSettingsAsync();
        sysCfg.DefaultProviderId = id;
        await UpdateAiSystemSettingsAsync(sysCfg, updatedBy);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteProviderAsync(string id)
    {
        if (id is "ai_gemini" or "ai_openai" or "ai_anthropic" or "ai_custom")
            return (false, "Không thể xóa nhà cung cấp AI mặc định. Bạn có thể tắt kích hoạt nếu không sử dụng.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_ai_providers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0
            ? (true, null)
            : (false, "Không tìm thấy cấu hình AI.");
    }

    public async Task<AiStatusDto> GetAiStatusAsync()
    {
        var sys = await GetAiSystemSettingsAsync();
        var providers = await LoadProvidersInternalAsync();
        var active = SelectActiveProvider(providers, sys, null);
        var usedTokens = await GetMonthlyTokensUsedAsync();
        return new AiStatusDto
        {
            IsEnabled = sys.IsEnabled,
            IsConfigured = sys.IsEnabled && active != null,
            ActiveProviderId = active?.Id,
            ActiveProviderName = active?.DisplayName,
            ActiveProviderType = active?.ProviderType,
            ActiveModel = active?.Model,
            FallbackToRuleEngine = sys.FallbackToRuleEngine,
            MaskPiiBeforeCloudLlm = sys.MaskPiiBeforeCloudLlm,
            MonthlyTokenBudget = sys.MonthlyTokenBudget,
            MonthlyTokensUsed = usedTokens,
            PreferLocalForSensitive = sys.PreferLocalForSensitive,
            EnabledFeatures = sys.EnabledFeatures
        };
    }

    public async Task<AiTestResultDto> TestProviderAsync(string id, AiTestProviderRequest? req, string username)
    {
        var providers = await LoadProvidersInternalAsync();
        var provider = providers.FirstOrDefault(p => p.Id == id);
        if (provider == null)
        {
            return new AiTestResultDto
            {
                Ok = false,
                ProviderName = id,
                Model = req?.Model ?? string.Empty,
                Message = "Không tìm thấy cấu hình AI."
            };
        }

        var pt = NormalizeProviderType(req?.ProviderType ?? provider.ProviderType);
        var baseUrl = string.IsNullOrWhiteSpace(req?.BaseUrl) ? provider.BaseUrl : req!.BaseUrl!.Trim().TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(req?.Model) ? provider.Model : req!.Model!.Trim();
        var apiKey = string.IsNullOrWhiteSpace(req?.ApiKey) ? provider.RawApiKey : req!.ApiKey!.Trim();
        var prompt = string.IsNullOrWhiteSpace(req?.Prompt)
            ? "Trả lời đúng 1 câu tiếng Việt: Kết nối AI API cho SmartApps Enterprise đã sẵn sàng."
            : req!.Prompt!.Trim();

        if (!IsProviderConfigured(pt, baseUrl, model, apiKey))
        {
            return new AiTestResultDto
            {
                Ok = false,
                ProviderName = provider.DisplayName,
                Model = model,
                Message = "Thiếu API Key, Base URL hoặc Model để kiểm tra kết nối."
            };
        }

        var sw = Stopwatch.StartNew();
        var (ok, text, ptTokens, ctTokens, err) = await ExecuteHttpAsync(
            pt,
            baseUrl,
            model,
            apiKey,
            provider.OrganizationId,
            "Bạn là trợ lý kiểm thử kết nối API.",
            prompt,
            0.1,
            200,
            provider.TimeoutSeconds,
            jsonMode: false);
        sw.Stop();

        await WriteLogAsync(
            provider.Id,
            provider.DisplayName,
            "AI_CONNECTION_TEST",
            model,
            username,
            ok ? "SUCCESS" : "FAILED",
            sw.ElapsedMilliseconds,
            ptTokens,
            ctTokens,
            ok ? "Kiểm tra kết nối thành công" : err);

        return new AiTestResultDto
        {
            Ok = ok,
            ProviderName = provider.DisplayName,
            Model = model,
            LatencyMs = sw.ElapsedMilliseconds,
            Message = ok ? $"Kết nối thành công ({sw.ElapsedMilliseconds} ms)" : (err ?? "Lỗi gọi API AI"),
            ReplyPreview = ok ? text.Trim() : null
        };
    }

    public Task<(bool Ok, string Content, string ProviderName, string Model, string? Error)> CompleteAsync(
        string featureCode,
        string systemPrompt,
        string userPrompt,
        string? username = null,
        bool jsonMode = false,
        string? overrideProviderId = null)
        => CompleteWithMediaAsync(featureCode, systemPrompt, userPrompt, null, null, sensitive: false, username, jsonMode, overrideProviderId);

    public async Task<(bool Ok, string Content, string ProviderName, string Model, string? Error)> CompleteWithMediaAsync(
        string featureCode,
        string systemPrompt,
        string userPrompt,
        string? imageBase64 = null,
        string? imageMimeType = null,
        bool sensitive = false,
        string? username = null,
        bool jsonMode = false,
        string? overrideProviderId = null)
    {
        var sys = await GetAiSystemSettingsAsync();
        if (!sys.IsEnabled)
            return (false, string.Empty, "Disabled", string.Empty, "Hệ thống AI dùng chung đang tắt trong Cài đặt.");

        if (sys.EnabledFeatures.Count > 0 &&
            !sys.EnabledFeatures.Contains("ALL", StringComparer.OrdinalIgnoreCase) &&
            !sys.EnabledFeatures.Contains(featureCode, StringComparer.OrdinalIgnoreCase))
        {
            return (false, string.Empty, "Disabled", string.Empty, $"Chức năng {featureCode} đang bị tắt trong Cài đặt AI.");
        }

        if (sys.MonthlyTokenBudget > 0)
        {
            var monthlyUsed = await GetMonthlyTokensUsedAsync();
            if (monthlyUsed >= sys.MonthlyTokenBudget)
            {
                return (false, string.Empty, "BudgetExceeded", string.Empty,
                    $"Đã vượt hạn mức AI tháng ({monthlyUsed:N0}/{sys.MonthlyTokenBudget:N0} tokens).");
            }
        }

        var providers = await LoadProvidersInternalAsync();
        AiProviderInternal? provider = null;
        if (sensitive && sys.PreferLocalForSensitive && string.IsNullOrWhiteSpace(overrideProviderId))
        {
            provider = providers.FirstOrDefault(p => p.IsEnabled && p.IsConfigured && p.ProviderType == "OPENAI_COMPATIBLE");
        }
        provider ??= SelectActiveProvider(providers, sys, overrideProviderId);
        if (provider == null)
        {
            return (false, string.Empty, "Unconfigured", string.Empty,
                "Chưa có nhà cung cấp AI nào được bật và cấu hình API Key.");
        }

        var effectivePrompt = (!IsLocalProvider(provider) && sys.MaskPiiBeforeCloudLlm)
            ? MaskPiiText(userPrompt)
            : userPrompt;

        var fullSystemPrompt = string.IsNullOrWhiteSpace(sys.SystemPromptPrefix)
            ? systemPrompt
            : $"{sys.SystemPromptPrefix}\n\n{systemPrompt}".Trim();

        var temp = provider.Temperature ?? sys.Temperature;
        var maxTok = provider.MaxTokens ?? sys.MaxTokens;
        var timeout = provider.TimeoutSeconds > 0 ? provider.TimeoutSeconds : sys.TimeoutSeconds;

        var sw = Stopwatch.StartNew();
        var (ok, text, ptTokens, ctTokens, err) = await ExecuteHttpAsync(
            provider.ProviderType,
            provider.BaseUrl,
            provider.Model,
            provider.RawApiKey,
            provider.OrganizationId,
            fullSystemPrompt,
            effectivePrompt,
            temp,
            maxTok,
            timeout,
            jsonMode,
            imageBase64,
            imageMimeType);
        sw.Stop();

        await WriteLogAsync(
            provider.Id,
            provider.DisplayName,
            featureCode,
            provider.Model,
            string.IsNullOrWhiteSpace(username) ? "system" : username,
            ok ? "SUCCESS" : "FAILED",
            sw.ElapsedMilliseconds,
            ptTokens,
            ctTokens,
            ok ? "OK" : err);

        return (ok, jsonMode ? StripMarkdownJsonFence(text) : text, provider.DisplayName, provider.Model, err);
    }

    public async Task<List<AiUsageLogDto>> ListLogsAsync(int limit = 25)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, provider_id, provider_name, feature_code, model, username, status,
                   latency_ms, prompt_tokens, completion_tokens, message, created_at
            FROM sys_ai_logs
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 100));

        var list = new List<AiUsageLogDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AiUsageLogDto
            {
                Id = r.GetString(0),
                ProviderId = r.GetString(1),
                ProviderName = r.GetString(2),
                FeatureCode = r.GetString(3),
                Model = r.GetString(4),
                Username = r.GetString(5),
                Status = r.GetString(6),
                LatencyMs = r.GetInt64(7),
                PromptTokens = r.GetInt32(8),
                CompletionTokens = r.GetInt32(9),
                Message = r.IsDBNull(10) ? null : r.GetString(10),
                CreatedAt = r.GetDateTime(11)
            });
        }
        return list;
    }

    public static List<AiCapabilityDto> GetCapabilityCatalog() => new()
    {
        new AiCapabilityDto
        {
            Code = "AI_APP_COPILOT",
            Name = "AI App Builder Copilot",
            Module = "App Builder Studio (/studio)",
            Status = "INTEGRATED",
            Description = "Thiết kế tự động App, Module, Bảng dữ liệu, Trường (AUTO_NUMBER, SUB_TABLE, Công thức) và Dashboard Widget từ mô tả tiếng Việt.",
            Benefit = "Rút ngắn thời gian dựng ứng dụng mới từ vài giờ xuống vài giây với cấu trúc chuẩn nghiệp vụ.",
            TargetRoute = "/studio",
            RecommendedTier = "Balanced / Reasoning"
        },
        new AiCapabilityDto
        {
            Code = "AI_RECORD_ASSISTANT",
            Name = "Trợ lý Hồ sơ 360° & Tóm tắt thông minh",
            Module = "Dynamic Runtime (/apps/[appCode])",
            Status = "INTEGRATED",
            Description = "Tóm tắt lịch sử hồ sơ 360°, trao đổi @mention, đề xuất nội dung/phân loại khi tạo mới bản ghi và phát hiện bất thường số liệu.",
            Benefit = "Giảm thao tác nhập liệu lặp lại và giúp lãnh đạo nắm bắt nhanh bối cảnh từng hồ sơ trước khi duyệt.",
            TargetRoute = "/apps",
            RecommendedTier = "Fast"
        },
        new AiCapabilityDto
        {
            Code = "AI_WORKFLOW_TRIAGE",
            Name = "AI Tóm tắt & Đề xuất Phê duyệt Đa cấp",
            Module = "Approval Workflow & Cổng Điều hành (/)",
            Status = "INTEGRATED",
            Description = "Tự động chấm điểm rủi ro/ưu tiên cho hồ sơ chờ duyệt, tóm tắt chênh lệch ngân sách hoặc điều khoản cần lưu ý cho người phê duyệt.",
            Benefit = "Tăng tốc độ phê duyệt trên Cổng Điều hành mà vẫn kiểm soát chặt sai sót.",
            TargetRoute = "/admin",
            RecommendedTier = "Reasoning"
        },
        new AiCapabilityDto
        {
            Code = "AI_CHAT_COPILOT",
            Name = "AI Chat Copilot & Tách việc tự động",
            Module = "Chat Nội bộ & Giao việc (/chat, /tasks)",
            Status = "INTEGRATED",
            Description = "Tóm tắt cuộc hội thoại dài trong kênh Chat, trích xuất Action Items thành thẻ công việc (/task) kèm người phụ trách và hạn hoàn thành.",
            Benefit = "Không bỏ sót đầu việc trong các kênh trao đổi đông thành viên.",
            TargetRoute = "/chat",
            RecommendedTier = "Fast"
        },
        new AiCapabilityDto
        {
            Code = "AI_DASHBOARD_INSIGHTS",
            Name = "Hỏi đáp Dữ liệu & Phân tích Dashboard (NL2Query)",
            Module = "Analytics & Dashboard Widgets (/apps/[appCode])",
            Status = "INTEGRATED",
            Description = "Đặt câu hỏi tiếng Việt (VD: 'Chi phí bảo trì quý này tăng ở bộ phận nào?') để AI tổng hợp nhận định, cảnh báo xu hướng từ app_records.",
            Benefit = "Lãnh đạo khai thác dữ liệu tức thì mà không cần tạo bộ lọc thủ công.",
            TargetRoute = "/apps",
            RecommendedTier = "Balanced"
        },
        new AiCapabilityDto
        {
            Code = "AI_IMPORT_MAPPER",
            Name = "Ánh xạ & Chuẩn hóa Dữ liệu Nhập Excel thông minh",
            Module = "Excel Import & Excel-to-App",
            Status = "INTEGRATED",
            Description = "Tự động ghép cột file Excel có tên không đồng nhất vào sys_columns, gợi ý chuẩn hóa ngày tháng, tiền tệ, mã danh mục trước khi lưu.",
            Benefit = "Giảm lỗi dữ liệu khi chuyển đổi từ file Excel rời rạc của các phòng ban.",
            TargetRoute = "/studio",
            RecommendedTier = "Fast"
        },
        new AiCapabilityDto
        {
            Code = "AI_DOC_OCR",
            Name = "Trích xuất Hóa đơn / Chứng từ đính kèm (OCR + LLM)",
            Module = "Attachments & Public Intake Forms (/forms/[tableId])",
            Status = "INTEGRATED",
            Description = "Đọc ảnh/PDF hóa đơn, hợp đồng, CCCD đính kèm để tự động điền vào các cột và dòng chi tiết SUB_TABLE.",
            Benefit = "Tự động hóa khâu nhập chứng từ kế toán, nhân sự và tiếp nhận biểu mẫu bên ngoài.",
            TargetRoute = "/forms",
            RecommendedTier = "Multimodal / Vision"
        }
    };

    private static AiProviderInternal? SelectActiveProvider(
        List<AiProviderInternal> providers,
        AiSystemSettingsDto sys,
        string? overrideProviderId)
    {
        if (!string.IsNullOrWhiteSpace(overrideProviderId))
        {
            var specific = providers.FirstOrDefault(p => p.Id == overrideProviderId && p.IsEnabled && p.IsConfigured);
            if (specific != null) return specific;
        }

        if (!string.IsNullOrWhiteSpace(sys.DefaultProviderId))
        {
            var bySysDef = providers.FirstOrDefault(p => p.Id == sys.DefaultProviderId && p.IsEnabled && p.IsConfigured);
            if (bySysDef != null) return bySysDef;
        }

        return providers.FirstOrDefault(p => p.IsDefault && p.IsEnabled && p.IsConfigured)
            ?? providers.FirstOrDefault(p => p.IsEnabled && p.IsConfigured);
    }

    private async Task<List<AiProviderInternal>> LoadProvidersInternalAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, code, provider_type, display_name, is_enabled, is_default,
                   api_key, base_url, model, temperature, max_tokens, timeout_seconds,
                   organization_id, order_index, updated_at
            FROM sys_ai_providers
            ORDER BY order_index ASC, updated_at ASC
            """, conn);

        var list = new List<AiProviderInternal>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var pt = NormalizeProviderType(r.GetString(2));
            var rawKey = r.IsDBNull(6) ? null : r.GetString(6);
            var baseUrl = r.GetString(7);
            var model = r.GetString(8);

            list.Add(new AiProviderInternal
            {
                Id = r.GetString(0),
                Code = r.GetString(1),
                ProviderType = pt,
                DisplayName = r.GetString(3),
                IsEnabled = r.GetBoolean(4),
                IsDefault = r.GetBoolean(5),
                RawApiKey = rawKey,
                HasApiKey = !string.IsNullOrWhiteSpace(rawKey),
                ApiKeyPreview = MaskApiKey(rawKey),
                BaseUrl = baseUrl,
                Model = model,
                SuggestedModels = SuggestedModelsFor(pt),
                Temperature = r.IsDBNull(9) ? null : r.GetDouble(9),
                MaxTokens = r.IsDBNull(10) ? null : r.GetInt32(10),
                TimeoutSeconds = r.GetInt32(11),
                OrganizationId = r.IsDBNull(12) ? null : r.GetString(12),
                OrderIndex = r.GetInt32(13),
                IsConfigured = IsProviderConfigured(pt, baseUrl, model, rawKey),
                UpdatedAt = r.GetDateTime(14)
            });
        }
        return list;
    }

    private async Task<(bool Ok, string Text, int PromptTokens, int CompletionTokens, string? Error)> ExecuteHttpAsync(
        string providerType,
        string baseUrl,
        string model,
        string? apiKey,
        string? organizationId,
        string systemPrompt,
        string userPrompt,
        double temperature,
        int maxTokens,
        int timeoutSeconds,
        bool jsonMode,
        string? imageBase64 = null,
        string? imageMimeType = null)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180));
            var cleanBase = baseUrl.Trim().TrimEnd('/');
            var (hasMedia, mediaData, mediaMime) = NormalizeMediaData(imageBase64, imageMimeType);

            if (providerType == "GEMINI")
            {
                var url = $"{cleanBase}/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey ?? string.Empty)}";
                var payload = hasMedia
                    ? BuildGeminiMultimodalPayload(systemPrompt, userPrompt, mediaData, mediaMime, temperature, maxTokens, jsonMode)
                    : BuildGeminiGeneratePayload(systemPrompt, userPrompt, temperature, maxTokens, jsonMode);
                using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
                };
                var resp = await client.SendAsync(reqMsg);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return (false, string.Empty, 0, 0, ParseProviderError(resp.StatusCode, body));
                var (text, pt, ct) = ExtractTextFromGeminiResponse(body);
                return string.IsNullOrWhiteSpace(text)
                    ? (false, string.Empty, pt, ct, "Gemini trả về phản hồi rỗng.")
                    : (true, text, pt, ct, null);
            }

            if (providerType == "ANTHROPIC")
            {
                var url = $"{cleanBase}/messages";
                var payload = hasMedia && mediaMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? BuildAnthropicMultimodalPayload(model, systemPrompt, userPrompt, mediaData, mediaMime, temperature, maxTokens)
                    : BuildAnthropicMessagesPayload(model, systemPrompt, userPrompt, temperature, maxTokens);
                using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
                };
                reqMsg.Headers.Add("x-api-key", apiKey ?? string.Empty);
                reqMsg.Headers.Add("anthropic-version", "2023-06-01");
                var resp = await client.SendAsync(reqMsg);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return (false, string.Empty, 0, 0, ParseProviderError(resp.StatusCode, body));
                var (text, pt, ct) = ExtractTextFromAnthropicResponse(body);
                return string.IsNullOrWhiteSpace(text)
                    ? (false, string.Empty, pt, ct, "Claude trả về phản hồi rỗng.")
                    : (true, text, pt, ct, null);
            }

            // OPENAI or OPENAI_COMPATIBLE
            var openAiUrl = cleanBase.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? cleanBase
                : $"{cleanBase}/chat/completions";
            var openAiPayload = hasMedia && mediaMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? BuildOpenAiMultimodalPayload(model, systemPrompt, userPrompt, mediaData, mediaMime, temperature, maxTokens, jsonMode, providerType == "OPENAI")
                : BuildOpenAiChatPayload(
                    model,
                    systemPrompt,
                    userPrompt,
                    temperature,
                    maxTokens,
                    jsonMode,
                    includeResponseFormat: providerType == "OPENAI");

            using var oaiReq = new HttpRequestMessage(HttpMethod.Post, openAiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(openAiPayload, JsonOpts), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
                oaiReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (!string.IsNullOrWhiteSpace(organizationId))
                oaiReq.Headers.Add("OpenAI-Organization", organizationId);

            var oaiResp = await client.SendAsync(oaiReq);
            var oaiBody = await oaiResp.Content.ReadAsStringAsync();
            if (!oaiResp.IsSuccessStatusCode)
                return (false, string.Empty, 0, 0, ParseProviderError(oaiResp.StatusCode, oaiBody));
            var (oaiText, oaiPt, oaiCt) = ExtractTextFromOpenAiResponse(oaiBody);
            return string.IsNullOrWhiteSpace(oaiText)
                ? (false, string.Empty, oaiPt, oaiCt, "AI API trả về phản hồi rỗng.")
                : (true, oaiText, oaiPt, oaiCt, null);
        }
        catch (TaskCanceledException)
        {
            return (false, string.Empty, 0, 0, $"Quá thời gian chờ phản hồi ({timeoutSeconds}s).");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, 0, 0, $"Lỗi kết nối AI API: {ex.Message}");
        }
    }

    private static string ParseProviderError(System.Net.HttpStatusCode statusCode, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return $"HTTP {(int)statusCode}: {msg.GetString()}";
                if (err.ValueKind == JsonValueKind.String)
                    return $"HTTP {(int)statusCode}: {err.GetString()}";
            }
        }
        catch
        {
            // ignore non-json error body
        }
        var snippet = body.Length > 240 ? body[..240] + "..." : body;
        return $"HTTP {(int)statusCode}: {snippet}";
    }

    private async Task WriteLogAsync(
        string providerId,
        string providerName,
        string featureCode,
        string model,
        string username,
        string status,
        long latencyMs,
        int promptTokens,
        int completionTokens,
        string? message)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_ai_logs (
                    id, provider_id, provider_name, feature_code, model, username,
                    status, latency_ms, prompt_tokens, completion_tokens, message
                )
                VALUES (@id, @pid, @pname, @fc, @mod, @un, @st, @lat, @pt, @ct, @msg)
                """, conn);
            cmd.Parameters.AddWithValue("id", $"ailog_{Guid.NewGuid().ToString("N")[..10]}");
            cmd.Parameters.AddWithValue("pid", providerId);
            cmd.Parameters.AddWithValue("pname", providerName);
            cmd.Parameters.AddWithValue("fc", featureCode);
            cmd.Parameters.AddWithValue("mod", model);
            cmd.Parameters.AddWithValue("un", username);
            cmd.Parameters.AddWithValue("st", status);
            cmd.Parameters.AddWithValue("lat", latencyMs);
            cmd.Parameters.AddWithValue("pt", promptTokens);
            cmd.Parameters.AddWithValue("ct", completionTokens);
            cmd.Parameters.AddWithValue("msg", (object?)message ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Logging must never break AI execution
        }
    }

    public async Task<(string Summary, string Engine)> SummarizeRecordAsync(
        AppRecordDto record,
        string tableDisplayName,
        List<SysColumnDto> columns,
        string username)
    {
        var lines = new List<string>();
        foreach (var col in columns.Take(12))
        {
            if (record.Data.TryGetValue(col.ColumnCode, out var val) && val != null && !string.IsNullOrWhiteSpace(val.ToString()))
            {
                var displayVal = val is System.Collections.ICollection colArr ? $"{colArr.Count} mục" : val.ToString();
                lines.Add($"- {col.ColumnName}: {displayVal}");
            }
        }

        var status = await GetAiStatusAsync();
        if (status.IsConfigured)
        {
            const string sysPrompt = """
                Bạn là trợ lý điều hành doanh nghiệp trên nền tảng SmartApps.
                Hãy tóm tắt ngắn gọn (3-4 gạch đầu dòng tiếng Việt) hồ sơ sau để lãnh đạo nắm bắt nhanh thông tin chính, giá trị tài chính (nếu có) và trạng thái phê duyệt.
                """;
            var userPrompt = $"Bảng: {tableDisplayName}\nMã hồ sơ: {record.RecordKey ?? record.Id}\nTrạng thái duyệt: {record.ApprovalStatus}\nNgười tạo: {record.CreatedBy}\nDữ liệu:\n{string.Join("\n", lines)}";
            var (ok, content, providerName, model, _) = await CompleteAsync("AI_RECORD_ASSISTANT", sysPrompt, userPrompt, username);
            if (ok && !string.IsNullOrWhiteSpace(content))
            {
                return (content.Trim(), $"{providerName} ({model})");
            }
        }

        // Deterministic structured executive summary fallback
        var sb = new StringBuilder();
        sb.AppendLine($"Hồ sơ **{record.RecordKey ?? record.Id}** thuộc bảng **{tableDisplayName}** (Trạng thái duyệt: **{record.ApprovalStatus}**, Phiên bản v{record.Version}).");
        if (lines.Count > 0)
        {
            sb.AppendLine("Thông tin trọng tâm:");
            foreach (var l in lines.Take(6)) sb.AppendLine(l);
        }
        if (record.ValidationErrors.Count > 0)
        {
            sb.AppendLine($"Lưu ý: Còn {record.ValidationErrors.Count} cảnh báo/lỗi kiểm tra dữ liệu.");
        }
        return (sb.ToString().Trim(), "SmartApps Rule Summarizer");
    }
}
