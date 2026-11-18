using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public partial class AiService
{
    private static readonly Regex EmailPiiRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    private static readonly Regex PhonePiiRegex = new(
        @"(?:\+84[\s.\-]?|(?<!\d)0)(?:[1-9])(?:[\s.\-]?\d){8,9}(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex LabeledIdPiiRegex = new(
        @"(?i)\b(CCCD|CMND|Căn cước|Số định danh)\s*[:#\-]?\s*\d{9,12}\b",
        RegexOptions.Compiled);

    private static readonly Regex Cccd12Regex = new(
        @"(?<!\d)0\d{11}(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex BankPiiRegex = new(
        @"(?i)\b(STK|Số TK|Tài khoản|IBAN)\s*[:#\-]?\s*\d{8,18}\b",
        RegexOptions.Compiled);

    private static bool IsLocalProvider(AiProviderInternal provider)
    {
        if (provider.ProviderType == "OPENAI_COMPATIBLE") return true;
        return provider.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("host.docker.internal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Redacts Vietnamese citizen IDs (CCCD/CMND), phone numbers, emails, and labeled bank account numbers
    /// before forwarding user prompts to external cloud LLM providers.
    /// </summary>
    public static string MaskPiiText(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var text = EmailPiiRegex.Replace(input, "***@masked.email");
        text = LabeledIdPiiRegex.Replace(text, m => $"{m.Groups[1].Value}: [ID_MASKED]");
        text = Cccd12Regex.Replace(text, "[CCCD_MASKED]");
        text = BankPiiRegex.Replace(text, m => $"{m.Groups[1].Value}: [BANK_MASKED]");
        text = PhonePiiRegex.Replace(text, "[PHONE_MASKED]");
        return text;
    }

    private async Task<long> GetMonthlyTokensUsedAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT COALESCE(SUM(prompt_tokens + completion_tokens), 0)
                FROM sys_ai_logs
                WHERE status = 'SUCCESS'
                  AND created_at >= date_trunc('month', CURRENT_TIMESTAMP)
                """, conn);
            var scalar = await cmd.ExecuteScalarAsync();
            return scalar is null or DBNull ? 0 : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    public static (bool HasMedia, string Base64Data, string MimeType) NormalizeMediaData(string? rawBase64, string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(rawBase64)) return (false, string.Empty, "image/png");
        var trimmed = rawBase64.Trim();
        var resolvedMime = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIdx = trimmed.IndexOf(',');
            if (commaIdx > 5)
            {
                var header = trimmed[5..commaIdx];
                var semiIdx = header.IndexOf(';');
                if (semiIdx > 0) resolvedMime = header[..semiIdx].Trim().ToLowerInvariant();
                trimmed = trimmed[(commaIdx + 1)..];
            }
        }
        return (!string.IsNullOrWhiteSpace(trimmed), trimmed, resolvedMime);
    }

    private static object BuildGeminiMultimodalPayload(
        string systemPrompt,
        string userPrompt,
        string base64Data,
        string mimeType,
        double temperature,
        int maxTokens,
        bool jsonMode)
    {
        var generationConfig = jsonMode
            ? (object)new { temperature, maxOutputTokens = maxTokens, responseMimeType = "application/json" }
            : new { temperature, maxOutputTokens = maxTokens };
        return new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = userPrompt },
                        new { inline_data = new { mime_type = mimeType, data = base64Data } }
                    }
                }
            },
            generationConfig
        };
    }

    private static object BuildOpenAiMultimodalPayload(
        string model,
        string systemPrompt,
        string userPrompt,
        string base64Data,
        string mimeType,
        double temperature,
        int maxTokens,
        bool jsonMode,
        bool includeResponseFormat)
    {
        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = userPrompt },
                    new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Data}" } }
                }
            }
        };
        if (jsonMode && includeResponseFormat)
        {
            return new { model, temperature, max_tokens = maxTokens, response_format = new { type = "json_object" }, messages };
        }
        return new { model, temperature, max_tokens = maxTokens, messages };
    }

    private static object BuildAnthropicMultimodalPayload(
        string model,
        string systemPrompt,
        string userPrompt,
        string base64Data,
        string mimeType,
        double temperature,
        int maxTokens) => new
    {
        model,
        max_tokens = maxTokens,
        temperature,
        system = systemPrompt,
        messages = new[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "image", source = new { type = "base64", media_type = mimeType, data = base64Data } },
                    new { type = "text", text = userPrompt }
                }
            }
        }
    };

    // =========================================================================
    // 1. AI_IMPORT_MAPPER: Smart Excel Header Mapping & Normalization
    // =========================================================================

    public async Task<AiImportMapResult> SuggestExcelMappingsAsync(
        AiImportMapRequest req,
        List<SysColumnDto> columns,
        string username)
    {
        var fallback = SuggestExcelMappingsRuleBased(req.Headers, req.SampleRows, columns);
        var status = await GetAiStatusAsync();
        if (!status.IsConfigured || req.Headers.Count == 0 || columns.Count == 0)
            return new AiImportMapResult { Mappings = fallback, Engine = "SmartApps Excel Mapper Rule Engine" };

        var colDesc = columns
            .Where(c => !c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                     && !c.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"- {c.ColumnCode} ({c.ColumnName}, type={c.DataType})")
            .ToList();
        var sampleLines = req.SampleRows.Take(3)
            .Select((r, idx) => $"Row {idx + 1}: {string.Join(" | ", r.Take(req.Headers.Count).Select(v => v ?? ""))}")
            .ToList();

        const string sysPrompt = """
            Bạn là trợ lý ánh xạ dữ liệu Excel vào hệ thống SmartApps.
            Trả về JSON đúng schema:
            {"mappings":[{"excelHeader":"...","columnCode":"... hoặc null nếu bỏ qua","confidence":0.95,"transformHint":"NONE|DATE_DMY|VND_INT|PHONE_VN|UPPERCASE|CATEGORY_FUZZY","reason":"Ngắn gọn tiếng Việt"}]}
            Chỉ dùng columnCode có trong danh sách cột đích.
            """;
        var userPrompt = $"Các tiêu đề cột Excel:\n{string.Join(", ", req.Headers)}\n\nMẫu dữ liệu:\n{string.Join("\n", sampleLines)}\n\nCác cột đích:\n{string.Join("\n", colDesc)}";

        var (ok, content, providerName, model, _) = await CompleteAsync("AI_IMPORT_MAPPER", sysPrompt, userPrompt, username, jsonMode: true);
        if (ok && !string.IsNullOrWhiteSpace(content))
        {
            var merged = ParseExcelMappingResponse(content, req.Headers, columns, fallback);
            if (merged.Count > 0)
                return new AiImportMapResult { Mappings = merged, Engine = $"{providerName} ({model})" };
        }

        return new AiImportMapResult { Mappings = fallback, Engine = "SmartApps Excel Mapper Rule Engine" };
    }

    public static List<AiImportMappingSuggestion> SuggestExcelMappingsRuleBased(
        List<string> headers,
        List<List<string?>> sampleRows,
        List<SysColumnDto> columns)
    {
        var eligibleCols = columns
            .Where(c => !c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                     && !c.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase)
                     && string.IsNullOrWhiteSpace(c.Formula))
            .ToList();
        var usedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AiImportMappingSuggestion>();

        for (int i = 0; i < headers.Count; i++)
        {
            var rawHeader = headers[i] ?? string.Empty;
            var normHeader = NormalizeLookupKey(rawHeader);
            var samples = sampleRows
                .Select(r => i < r.Count ? r[i] : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToList();

            SysColumnDto? bestCol = null;
            double bestScore = 0;

            foreach (var col in eligibleCols)
            {
                if (usedCols.Contains(col.ColumnCode)) continue;
                var codeNorm = NormalizeLookupKey(col.ColumnCode);
                var nameNorm = NormalizeLookupKey(col.ColumnName);
                var score = Math.Max(ScoreKeySimilarity(normHeader, codeNorm), ScoreKeySimilarity(normHeader, nameNorm));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCol = col;
                }
            }

            if (bestCol != null && bestScore >= 0.45)
            {
                usedCols.Add(bestCol.ColumnCode);
                var hint = InferTransformHint(bestCol, samples);
                result.Add(new AiImportMappingSuggestion
                {
                    ExcelHeader = rawHeader,
                    ColumnCode = bestCol.ColumnCode,
                    ColumnName = bestCol.ColumnName,
                    Confidence = Math.Round(Math.Min(0.99, bestScore), 2),
                    TransformHint = hint,
                    Reason = bestScore >= 0.9 ? "Khớp chính xác tên/mã cột" : "Khớp tương đồng ngữ nghĩa tiếng Việt"
                });
            }
            else
            {
                result.Add(new AiImportMappingSuggestion
                {
                    ExcelHeader = rawHeader,
                    ColumnCode = null,
                    Confidence = 0,
                    TransformHint = "NONE",
                    Reason = "Không tìm thấy cột tương ứng — bỏ qua hoặc tạo cột mới"
                });
            }
        }
        return result;
    }

    private static List<AiImportMappingSuggestion> ParseExcelMappingResponse(
        string json,
        List<string> headers,
        List<SysColumnDto> columns,
        List<AiImportMappingSuggestion> fallback)
    {
        var colByCode = columns.ToDictionary(c => c.ColumnCode, StringComparer.OrdinalIgnoreCase);
        var fbMap = fallback.ToDictionary(m => m.ExcelHeader, StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("mappings", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return fallback;

            var byHeader = new Dictionary<string, AiImportMappingSuggestion>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arr.EnumerateArray())
            {
                var h = item.TryGetProperty("excelHeader", out var hEl) ? hEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(h)) continue;
                var code = item.TryGetProperty("columnCode", out var cEl) && cEl.ValueKind == JsonValueKind.String
                    ? cEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(code) && !colByCode.ContainsKey(code))
                    code = null;

                var conf = item.TryGetProperty("confidence", out var cfEl) && cfEl.TryGetDouble(out var cv) ? Math.Clamp(cv, 0, 1) : 0.8;
                var hint = item.TryGetProperty("transformHint", out var thEl) ? thEl.GetString() ?? "NONE" : "NONE";
                var reason = item.TryGetProperty("reason", out var rEl) ? rEl.GetString() : null;

                byHeader[h] = new AiImportMappingSuggestion
                {
                    ExcelHeader = h,
                    ColumnCode = code,
                    ColumnName = code != null && colByCode.TryGetValue(code, out var cDto) ? cDto.ColumnName : null,
                    Confidence = conf,
                    TransformHint = hint,
                    Reason = reason
                };
            }

            return headers.Select(h => byHeader.TryGetValue(h, out var s)
                ? s
                : (fbMap.TryGetValue(h, out var fb) ? fb : new AiImportMappingSuggestion { ExcelHeader = h })).ToList();
        }
        catch
        {
            return fallback;
        }
    }

    private static string InferTransformHint(SysColumnDto col, List<string> samples)
    {
        var dt = col.DataType.ToUpperInvariant();
        if (dt is "DATE" or "DATETIME")
        {
            return samples.Any(s => Regex.IsMatch(s, @"^\d{1,2}/\d{1,2}/\d{4}$")) ? "DATE_DMY" : "NONE";
        }
        if (dt is "NUMBER" or "CURRENCY")
        {
            return samples.Any(s => s.Contains('.') || s.Contains(',') || s.Contains("đ", StringComparison.OrdinalIgnoreCase))
                ? "VND_INT"
                : "NONE";
        }
        if (col.ColumnCode.Contains("phone", StringComparison.OrdinalIgnoreCase) || col.ColumnCode.Contains("sdt", StringComparison.OrdinalIgnoreCase))
        {
            return "PHONE_VN";
        }
        if (dt is "SELECT" or "MULTI_SELECT" or "LOOKUP")
        {
            return "CATEGORY_FUZZY";
        }
        return "NONE";
    }

    // =========================================================================
    // 2. AI_DOC_OCR: Document / Invoice / ID Extraction (Multimodal + Rule)
    // =========================================================================

    public async Task<AiDocOcrResult> ExtractDocumentFieldsAsync(
        AiDocOcrRequest req,
        List<SysColumnDto> columns,
        string username)
    {
        var ruleResult = ExtractDocumentFieldsRuleBased(req.RawText ?? string.Empty, columns);
        var status = await GetAiStatusAsync();
        bool hasInput = !string.IsNullOrWhiteSpace(req.ImageBase64) || !string.IsNullOrWhiteSpace(req.RawText);

        if (status.IsConfigured && hasInput)
        {
            var scalarDesc = columns
                .Where(c => !c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                         && !c.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase))
                .Select(c => $"- {c.ColumnCode} ({c.ColumnName}, {c.DataType})")
                .ToList();
            var subDesc = columns
                .Where(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    var subCols = DynamicComputeService.ParseSubTableSchema(c.DefaultValue);
                    var subText = string.Join(", ", subCols.Select(sc => $"{sc.Code}:{sc.Name}({sc.DataType})"));
                    return $"- {c.ColumnCode} ({c.ColumnName}): [{subText}]";
                })
                .ToList();

            const string sysPrompt = """
                Bạn là AI OCR trích xuất dữ liệu chứng từ doanh nghiệp Việt Nam (Hóa đơn VAT, Phiếu thu/chi, Hợp đồng, CCCD).
                Trả về JSON chuẩn:
                {"fields":{"<columnCode>":"<value>"},"subTables":{"<subTableCode>":[{"<subColCode>":"<value>"}]},"confidence":0.92,"warnings":[]}
                Ngày tháng định dạng yyyy-MM-dd, số tiền trả số nguyên không có dấu chấm/phẩy. Chỉ điền các trường có trong chứng từ.
                """;
            var userPrompt = $"Tệp: {req.FileName ?? "document"}\nCột chính:\n{string.Join("\n", scalarDesc)}" +
                             (subDesc.Count > 0 ? $"\nBảng chi tiết SUB_TABLE:\n{string.Join("\n", subDesc)}" : string.Empty) +
                             (string.IsNullOrWhiteSpace(req.RawText) ? string.Empty : $"\nNội dung văn bản chứng từ:\n{req.RawText}");

            var (ok, content, providerName, model, _) = await CompleteWithMediaAsync(
                "AI_DOC_OCR", sysPrompt, userPrompt, req.ImageBase64, req.MimeType, sensitive: false, username, jsonMode: true);
            if (ok && !string.IsNullOrWhiteSpace(content))
            {
                var parsed = ParseOcrJsonResponse(content, columns, $"{providerName} ({model})");
                if (parsed.Fields.Count > 0 || parsed.SubTables.Count > 0)
                    return parsed;
            }
        }

        if (string.IsNullOrWhiteSpace(req.RawText) && !string.IsNullOrWhiteSpace(req.ImageBase64))
        {
            ruleResult.Warnings.Add("Chưa cấu hình Cloud/Local Vision LLM để đọc ảnh trực tiếp — hãy dán nội dung OCR hoặc bật Gemini/OpenAI API Key.");
        }
        return ruleResult;
    }

    public static AiDocOcrResult ExtractDocumentFieldsRuleBased(string rawText, List<SysColumnDto> columns)
    {
        var fields = new Dictionary<string, object?>();
        var subTables = new Dictionary<string, List<Dictionary<string, object?>>>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new AiDocOcrResult
            {
                Fields = fields,
                SubTables = subTables,
                Confidence = 0,
                Warnings = warnings,
                Engine = "SmartApps OCR Rule Extractor"
            };
        }

        var taxMatch = Regex.Match(rawText, @"(?i)(?:MST|Mã số thuế)\s*[:#\-]?\s*([0-9\-]{10,14})");
        var invNoMatch = Regex.Match(rawText, @"(?i)(?:Số hóa đơn|Số HĐ|Invoice No|Số chứng từ)\s*[:#\-]?\s*([A-Z0-9\-\/]{3,20})");
        var dateMatch = Regex.Match(rawText, @"(?i)(?:Ngày|Date)\s*[:\-]?\s*(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})");
        var isoDateMatch = Regex.Match(rawText, @"\b(20\d{2})-(\d{2})-(\d{2})\b");
        var amountMatch = Regex.Match(rawText, @"(?i)(?:Tổng tiền thanh toán|Tổng cộng|Tổng tiền|Thành tiền|Total)\s*[:\-]?\s*([\d.,]{4,20})");
        var vendorMatch = Regex.Match(rawText, @"(?im)(?:Đơn vị bán|Nhà cung cấp|Người bán|Công ty|Vendor)\s*[:\-]\s*(.+)$");
        var emailMatch = EmailPiiRegex.Match(rawText);
        var phoneMatch = PhonePiiRegex.Match(rawText);

        string? isoDate = null;
        if (dateMatch.Success && int.TryParse(dateMatch.Groups[1].Value, out int d)
            && int.TryParse(dateMatch.Groups[2].Value, out int m)
            && int.TryParse(dateMatch.Groups[3].Value, out int y)
            && m is >= 1 and <= 12 && d is >= 1 and <= 31)
        {
            isoDate = $"{y:D4}-{m:D2}-{d:D2}";
        }
        else if (isoDateMatch.Success)
        {
            isoDate = isoDateMatch.Value;
        }

        decimal? parsedAmount = null;
        if (amountMatch.Success)
        {
            var digits = Regex.Replace(amountMatch.Groups[1].Value, @"[^\d]", "");
            if (decimal.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                parsedAmount = amt;
        }

        foreach (var col in columns)
        {
            if (col.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                || col.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase)) continue;
            var key = NormalizeLookupKey($"{col.ColumnCode} {col.ColumnName}");

            if (isoDate != null && (col.DataType.Equals("DATE", StringComparison.OrdinalIgnoreCase) || col.DataType.Equals("DATETIME", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ngay_", StringComparison.Ordinal)))
                fields[col.ColumnCode] = isoDate;
            else if (parsedAmount.HasValue && (col.DataType is "CURRENCY" or "NUMBER") && (key.Contains("tong") || key.Contains("thanh_tien") || key.Contains("so_tien") || key.Contains("amount")))
                fields[col.ColumnCode] = parsedAmount.Value;
            else if (taxMatch.Success && (key.Contains("mst") || key.Contains("ma_so_thue") || key.Contains("tax")))
                fields[col.ColumnCode] = taxMatch.Groups[1].Value.Trim();
            else if (invNoMatch.Success && (key.Contains("so_hoa_don") || key.Contains("so_hd") || key.Contains("invoice") || key.Contains("so_chung_tu")))
                fields[col.ColumnCode] = invNoMatch.Groups[1].Value.Trim();
            else if (vendorMatch.Success && (key.Contains("ncc") || key.Contains("nha_cung_cap") || key.Contains("don_vi") || key.Contains("vendor") || key.Contains("khach_hang")))
                fields[col.ColumnCode] = vendorMatch.Groups[1].Value.Trim();
            else if (emailMatch.Success && (col.DataType.Equals("EMAIL", StringComparison.OrdinalIgnoreCase) || key.Contains("email")))
                fields[col.ColumnCode] = emailMatch.Value.Trim();
            else if (phoneMatch.Success && (col.DataType.Equals("TEL", StringComparison.OrdinalIgnoreCase) || key.Contains("sdt") || key.Contains("dien_thoai") || key.Contains("phone")))
                fields[col.ColumnCode] = phoneMatch.Value.Trim();
        }

        var firstSub = columns.FirstOrDefault(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase));
        if (firstSub != null)
        {
            var subCols = DynamicComputeService.ParseSubTableSchema(firstSub.DefaultValue);
            if (subCols.Count > 0)
            {
                var rows = ParseOcrTableLines(rawText, subCols);
                if (rows.Count > 0) subTables[firstSub.ColumnCode] = rows;
            }
        }

        if (fields.Count == 0 && subTables.Count == 0)
            warnings.Add("Không nhận diện được cặp trường khóa-giá trị chuẩn từ văn bản đầu vào.");

        return new AiDocOcrResult
        {
            Fields = fields,
            SubTables = subTables,
            Confidence = fields.Count > 0 ? 0.78 : 0.25,
            Warnings = warnings,
            Engine = "SmartApps OCR Rule Extractor"
        };
    }

    private static List<Dictionary<string, object?>> ParseOcrTableLines(string rawText, List<SubTableColumnDef> subCols)
    {
        var list = new List<Dictionary<string, object?>>();
        var descCol = subCols.FirstOrDefault(c => c.DataType is "TEXT" or "STRING");
        var qtyCol = subCols.FirstOrDefault(c => NormalizeLookupKey(c.Code + " " + c.Name).Contains("so_luong") || NormalizeLookupKey(c.Code).Contains("qty"));
        var numCols = subCols.Where(c => c.DataType is "NUMBER" or "CURRENCY" && c.Code != qtyCol?.Code).ToList();
        var priceCol = numCols.FirstOrDefault();
        var totalCol = numCols.Skip(1).FirstOrDefault() ?? numCols.FirstOrDefault();

        foreach (var line in rawText.Split('\n'))
        {
            if (!line.Contains('|')) continue;
            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            // Skip header rows
            if (Regex.IsMatch(parts[0], @"(?i)stt|ten|mô tả|description|item")) continue;

            var row = new Dictionary<string, object?>();
            var textPart = parts.FirstOrDefault(p => Regex.IsMatch(p, @"[a-zA-ZÀ-ỹ]"));
            var numbers = parts
                .Select(p => Regex.Replace(p, @"[^\d]", ""))
                .Where(d => d.Length > 0 && decimal.TryParse(d, out _))
                .Select(d => decimal.Parse(d, CultureInfo.InvariantCulture))
                .ToList();

            if (textPart == null || numbers.Count == 0) continue;
            if (descCol != null) row[descCol.Code] = textPart;
            if (qtyCol != null && numbers.Count >= 1) row[qtyCol.Code] = numbers.Count > 1 ? numbers[0] : 1m;
            if (priceCol != null && numbers.Count >= 2 && priceCol.Code != totalCol?.Code) row[priceCol.Code] = numbers[1];
            if (totalCol != null) row[totalCol.Code] = numbers[^1];
            list.Add(row);
        }
        return list;
    }

    private static AiDocOcrResult ParseOcrJsonResponse(string json, List<SysColumnDto> columns, string engine)
    {
        var validCols = columns.Select(c => c.ColumnCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new AiDocOcrResult { Engine = engine, Confidence = 0.9 };
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("fields", out var fEl) && fEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fEl.EnumerateObject())
                {
                    if (!validCols.Contains(prop.Name) || prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                    result.Fields[prop.Name] = ConvertJsonElement(prop.Value);
                }
            }
            if (doc.RootElement.TryGetProperty("subTables", out var stEl) && stEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in stEl.EnumerateObject())
                {
                    var subColDef = columns.FirstOrDefault(c => c.ColumnCode.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));
                    if (subColDef == null || prop.Value.ValueKind != JsonValueKind.Array) continue;
                    var validSub = DynamicComputeService.ParseSubTableSchema(subColDef.DefaultValue)
                        .Select(sc => sc.Code)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var rows = new List<Dictionary<string, object?>>();
                    foreach (var rowEl in prop.Value.EnumerateArray())
                    {
                        if (rowEl.ValueKind != JsonValueKind.Object) continue;
                        var dict = new Dictionary<string, object?>();
                        foreach (var cell in rowEl.EnumerateObject())
                        {
                            if (validSub.Contains(cell.Name))
                                dict[cell.Name] = ConvertJsonElement(cell.Value);
                        }
                        if (dict.Count > 0) rows.Add(dict);
                    }
                    result.SubTables[prop.Name] = rows;
                }
            }
            if (doc.RootElement.TryGetProperty("confidence", out var cfEl) && cfEl.TryGetDouble(out var cf))
                result.Confidence = Math.Clamp(cf, 0, 1);
            if (doc.RootElement.TryGetProperty("warnings", out var wEl) && wEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in wEl.EnumerateArray())
                    if (w.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(w.GetString()))
                        result.Warnings.Add(w.GetString()!);
            }
        }
        catch
        {
            // caller falls back to rule result
        }
        return result;
    }

    // =========================================================================
    // 3. AI_WORKFLOW_TRIAGE: Approval Risk Scoring & Historical Comparison
    // =========================================================================

    public async Task<AiWorkflowTriageResult> TriageWorkflowRecordAsync(
        AppRecordDto record,
        string tableDisplayName,
        List<SysColumnDto> columns,
        List<AppRecordDto> historicalRecords,
        string username)
    {
        var triage = ComputeWorkflowTriageRuleBased(record, tableDisplayName, columns, historicalRecords);
        var status = await GetAiStatusAsync();
        if (!status.IsConfigured)
            return triage;

        var fieldsSummary = columns.Take(12)
            .Where(c => record.Data.TryGetValue(c.ColumnCode, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString()))
            .Select(c => $"- {c.ColumnName}: {record.Data[c.ColumnCode]}")
            .ToList();

        const string sysPrompt = """
            Bạn là AI kiểm soát rủi ro phê duyệt doanh nghiệp trên SmartApps.
            Trả về JSON: {"recommendation":"APPROVE_FAST|REVIEW_CAREFULLY|REQUEST_CLARIFICATION","summaryBullets":["..."],"anomalies":["..."]}
            Viết tiếng Việt súc tích, chuyên nghiệp.
            """;
        var userPrompt = $"Bảng: {tableDisplayName}\nMã hồ sơ: {record.RecordKey ?? record.Id}\nĐiểm rủi ro sơ bộ: {triage.RiskScore}/100 ({triage.RiskLevel})\n" +
                         $"Dữ liệu:\n{string.Join("\n", fieldsSummary)}\n" +
                         $"Bất thường thống kê ({historicalRecords.Count} hồ sơ lịch sử):\n{(triage.Anomalies.Count > 0 ? string.Join("\n", triage.Anomalies) : "Không có bất thường lớn.")}";

        var (ok, content, providerName, model, _) = await CompleteWithMediaAsync(
            "AI_WORKFLOW_TRIAGE", sysPrompt, userPrompt, sensitive: true, username: username, jsonMode: true);
        if (ok && !string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("recommendation", out var rEl))
                {
                    var rec = rEl.GetString()?.Trim().ToUpperInvariant();
                    if (rec is "APPROVE_FAST" or "REVIEW_CAREFULLY" or "REQUEST_CLARIFICATION")
                    {
                        triage.Recommendation = rec;
                        triage.RecommendationLabel = RecommendationLabelFor(rec);
                    }
                }
                if (doc.RootElement.TryGetProperty("summaryBullets", out var sbEl) && sbEl.ValueKind == JsonValueKind.Array)
                {
                    var bullets = sbEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (bullets.Count > 0) triage.SummaryBullets = bullets;
                }
                if (doc.RootElement.TryGetProperty("anomalies", out var anEl) && anEl.ValueKind == JsonValueKind.Array)
                {
                    var ans = anEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (ans.Count > 0) triage.Anomalies = ans;
                }
                triage.Engine = $"{providerName} ({model})";
            }
            catch
            {
                // keep deterministic triage
            }
        }
        return triage;
    }

    public static AiWorkflowTriageResult ComputeWorkflowTriageRuleBased(
        AppRecordDto record,
        string tableDisplayName,
        List<SysColumnDto> columns,
        List<AppRecordDto> historicalRecords)
    {
        var anomalies = new List<string>();
        var bullets = new List<string>
        {
            $"Hồ sơ **{record.RecordKey ?? record.Id}** ({tableDisplayName}) đang ở trạng thái **{record.ApprovalStatus}** (v{record.Version})."
        };
        int riskScore = 10;

        if (record.ValidationErrors.Count > 0)
        {
            riskScore += Math.Min(35, record.ValidationErrors.Count * 15);
            anomalies.Add($"Có {record.ValidationErrors.Count} cảnh báo kiểm tra dữ liệu chưa xử lý.");
        }

        int missingRequired = columns.Count(c => c.IsRequired && (!record.Data.TryGetValue(c.ColumnCode, out var v) || string.IsNullOrWhiteSpace(v?.ToString())));
        if (missingRequired > 0)
        {
            riskScore += Math.Min(30, missingRequired * 15);
            anomalies.Add($"Thiếu {missingRequired} trường bắt buộc trên biểu mẫu.");
        }

        var numericCols = columns.Where(c => c.DataType is "NUMBER" or "CURRENCY").ToList();
        foreach (var col in numericCols)
        {
            if (!TryGetDecimal(record.Data, col.ColumnCode, out var currentVal)) continue;
            var histVals = historicalRecords
                .Where(h => h.Id != record.Id)
                .Select(h => TryGetDecimal(h.Data, col.ColumnCode, out var hv) ? (decimal?)hv : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (histVals.Count >= 3)
            {
                var avg = histVals.Average();
                if (avg > 0 && currentVal > avg * 2.5m)
                {
                    var ratio = Math.Round(currentVal / avg, 1);
                    riskScore += ratio >= 4m ? 35 : 22;
                    anomalies.Add($"{col.ColumnName} = {currentVal:N0} cao gấp {ratio}x mức trung bình lịch sử ({avg:N0}).");
                }
                else
                {
                    bullets.Add($"{col.ColumnName}: {currentVal:N0} (trong ngưỡng ổn định so với TB {avg:N0}).");
                }
            }
            else
            {
                bullets.Add($"{col.ColumnName}: {currentVal:N0}.");
            }
        }

        riskScore = Math.Clamp(riskScore, 0, 100);
        var riskLevel = riskScore >= 60 ? "HIGH" : riskScore >= 30 ? "MEDIUM" : "LOW";
        var rec = riskScore >= 60 ? "REQUEST_CLARIFICATION" : riskScore >= 30 ? "REVIEW_CAREFULLY" : "APPROVE_FAST";

        return new AiWorkflowTriageResult
        {
            RiskLevel = riskLevel,
            RiskScore = riskScore,
            Recommendation = rec,
            RecommendationLabel = RecommendationLabelFor(rec),
            SummaryBullets = bullets,
            Anomalies = anomalies,
            HistoricalSampleSize = historicalRecords.Count,
            Engine = "SmartApps Risk Triage Engine"
        };
    }

    private static string RecommendationLabelFor(string rec) => rec switch
    {
        "APPROVE_FAST" => "Đề xuất: Duyệt nhanh (Rủi ro thấp)",
        "REVIEW_CAREFULLY" => "Đề xuất: Kiểm tra kỹ số liệu trước khi duyệt",
        _ => "Đề xuất: Yêu cầu giải trình / bổ sung hồ sơ"
    };

    // =========================================================================
    // 4. AI_CHAT_COPILOT: Summarize Channel & Extract Action Items
    // =========================================================================

    public async Task<AiChatCopilotResult> SummarizeChatThreadAsync(
        string channelName,
        List<ChatMessageDto> messages,
        string username)
    {
        var recent = messages.TakeLast(40).ToList();
        var fallback = SummarizeChatThreadRuleBased(channelName, recent);
        var status = await GetAiStatusAsync();
        if (!status.IsConfigured || recent.Count == 0)
            return fallback;

        var transcript = string.Join("\n", recent.Select(m =>
            $"[{m.Id}] {m.SenderUsername} ({m.CreatedAt.ToLocalTime():dd/MM HH:mm}): {m.Content}"));

        const string sysPrompt = """
            Bạn là AI Chat Copilot trên SmartApps. Hãy tóm tắt cuộc hội thoại và trích xuất các đầu việc cần làm (Action Items).
            Trả về JSON chuẩn:
            {"summary":"Tóm tắt ngắn gọn 2-4 câu tiếng Việt","actionItems":[{"title":"...","assigneeUsername":"... hoặc null","dueDate":"yyyy-MM-dd hoặc null","priority":"LOW|MEDIUM|HIGH|URGENT","sourceMessageId":"..."}]}
            """;
        var userPrompt = $"Kênh: {channelName}\nLịch sử tin nhắn gần đây:\n{transcript}";

        var (ok, content, providerName, model, _) = await CompleteAsync("AI_CHAT_COPILOT", sysPrompt, userPrompt, username, jsonMode: true);
        if (ok && !string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var res = new AiChatCopilotResult
                {
                    Summary = doc.RootElement.TryGetProperty("summary", out var sEl) ? sEl.GetString() ?? fallback.Summary : fallback.Summary,
                    AnalyzedMessages = recent.Count,
                    Engine = $"{providerName} ({model})"
                };
                if (doc.RootElement.TryGetProperty("actionItems", out var aiEl) && aiEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in aiEl.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        res.ActionItems.Add(new AiChatActionItem
                        {
                            Title = title.Trim(),
                            AssigneeUsername = item.TryGetProperty("assigneeUsername", out var aEl) && aEl.ValueKind == JsonValueKind.String ? aEl.GetString() : null,
                            DueDate = item.TryGetProperty("dueDate", out var dEl) && dEl.ValueKind == JsonValueKind.String ? dEl.GetString() : null,
                            Priority = item.TryGetProperty("priority", out var pEl) ? pEl.GetString() ?? "MEDIUM" : "MEDIUM",
                            SourceMessageId = item.TryGetProperty("sourceMessageId", out var mEl) && mEl.ValueKind == JsonValueKind.String ? mEl.GetString() : null
                        });
                    }
                }
                return res;
            }
            catch
            {
                // keep fallback
            }
        }
        return fallback;
    }

    public static AiChatCopilotResult SummarizeChatThreadRuleBased(string channelName, List<ChatMessageDto> messages)
    {
        var actionItems = new List<AiChatActionItem>();
        var participants = messages.Select(m => m.SenderUsername).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content)) continue;
            var lower = msg.Content.ToLowerInvariant();
            bool looksActionable = lower.Contains("cần ") || lower.Contains("nhờ ") || lower.Contains("giao ")
                || lower.Contains("hạn ") || lower.Contains("deadline") || lower.Contains("kiểm tra")
                || lower.Contains("phê duyệt") || lower.Contains("cập nhật") || lower.Contains("gửi ");
            if (!looksActionable) continue;

            var mention = Regex.Match(msg.Content, @"@([a-zA-Z0-9_.\-]+)");
            var priority = (lower.Contains("gấp") || lower.Contains("urgent") || lower.Contains("khẩn")) ? "URGENT"
                : lower.Contains("quan trọng") ? "HIGH" : "MEDIUM";
            var cleanTitle = msg.Content.Length > 110 ? msg.Content[..107] + "..." : msg.Content;

            actionItems.Add(new AiChatActionItem
            {
                Title = cleanTitle.Trim(),
                AssigneeUsername = mention.Success ? mention.Groups[1].Value : msg.SenderUsername,
                Priority = priority,
                SourceMessageId = msg.Id
            });
        }

        var latestPoints = messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)).TakeLast(3)
            .Select(m => $"- {m.SenderUsername}: {(m.Content.Length > 80 ? m.Content[..77] + "..." : m.Content)}");
        var summary = messages.Count == 0
            ? $"Kênh **{channelName}** chưa có tin nhắn để phân tích."
            : $"Đã quét {messages.Count} tin nhắn từ {participants.Count} thành viên ({string.Join(", ", participants.Take(4))}). Trọng tâm gần nhất:\n{string.Join("\n", latestPoints)}";

        return new AiChatCopilotResult
        {
            Summary = summary,
            ActionItems = actionItems.Take(6).ToList(),
            AnalyzedMessages = messages.Count,
            Engine = "SmartApps Chat Copilot Rule Engine"
        };
    }

    // =========================================================================
    // 5. AI_DASHBOARD_INSIGHTS: Safe NL2StructuredQuery (RBAC/RLS/FLS enforced)
    // =========================================================================

    public async Task<(AiNlFilterSpec Plan, string Engine)> BuildNl2QueryPlanAsync(
        string question,
        List<SysColumnDto> visibleColumns,
        string username)
    {
        var fallbackPlan = BuildNl2QueryPlanRuleBased(question, visibleColumns);
        var status = await GetAiStatusAsync();
        if (!status.IsConfigured || visibleColumns.Count == 0)
            return (fallbackPlan, "SmartApps NL2Query Rule Engine");

        var colDesc = visibleColumns.Select(c => $"- {c.ColumnCode} ({c.ColumnName}, {c.DataType})").ToList();
        const string sysPrompt = """
            Chuyển câu hỏi tiếng Việt thành kế hoạch lọc & thống kê JSON an toàn (KHÔNG viết SQL).
            Chỉ trả về JSON:
            {"search":null,"status":null,"logic":"AND","conditions":[{"field":"column_code","op":"eq|neq|contains|gt|gte|lt|lte|date_preset","value":"..."}],"aggregateMetric":"COUNT|SUM|AVG","metricField":null,"groupByField":null,"sortField":null,"sortDir":"DESC"}
            Chỉ dùng field có trong schema được cung cấp (hoặc approvalStatus: DRAFT|PENDING|APPROVED|REJECTED).
            """;
        var userPrompt = $"Câu hỏi: {question}\nSchema bảng:\n{string.Join("\n", colDesc)}";

        var (ok, content, providerName, model, _) = await CompleteAsync("AI_DASHBOARD_INSIGHTS", sysPrompt, userPrompt, username, jsonMode: true);
        if (ok && !string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AiNlFilterSpec>(content, JsonOpts);
                if (parsed != null)
                {
                    return (SanitizeNlFilterPlan(parsed, visibleColumns), $"{providerName} ({model})");
                }
            }
            catch
            {
                // keep fallback
            }
        }
        return (fallbackPlan, "SmartApps NL2Query Rule Engine");
    }

    public static AiNlFilterSpec BuildNl2QueryPlanRuleBased(string question, List<SysColumnDto> visibleColumns)
    {
        var lower = question.ToLowerInvariant();
        var normQ = NormalizeLookupKey(question);
        var plan = new AiNlFilterSpec();

        if (lower.Contains("tổng ") || lower.Contains("chi phí") || lower.Contains("doanh thu") || lower.Contains("giá trị"))
            plan.AggregateMetric = "SUM";
        else if (lower.Contains("trung bình") || lower.Contains("bình quân"))
            plan.AggregateMetric = "AVG";
        else
            plan.AggregateMetric = "COUNT";

        if (lower.Contains("chờ duyệt") || lower.Contains("pending"))
            plan.Conditions.Add(new AiNlFilterCondition { Field = "approvalStatus", Op = "eq", Value = "PENDING" });
        else if (lower.Contains("đã duyệt") || lower.Contains("approved"))
            plan.Conditions.Add(new AiNlFilterCondition { Field = "approvalStatus", Op = "eq", Value = "APPROVED" });
        else if (lower.Contains("từ chối") || lower.Contains("rejected"))
            plan.Conditions.Add(new AiNlFilterCondition { Field = "approvalStatus", Op = "eq", Value = "REJECTED" });

        var metricCol = visibleColumns.FirstOrDefault(c =>
            c.DataType is "NUMBER" or "CURRENCY" &&
            (normQ.Contains(NormalizeLookupKey(c.ColumnCode)) || normQ.Contains(NormalizeLookupKey(c.ColumnName))))
            ?? visibleColumns.FirstOrDefault(c => c.DataType is "CURRENCY" or "NUMBER");
        if (plan.AggregateMetric is "SUM" or "AVG" && metricCol != null)
            plan.MetricField = metricCol.ColumnCode;

        var groupCol = visibleColumns.FirstOrDefault(c =>
            c.DataType is "SELECT" or "LOOKUP" or "USER" or "TEXT" &&
            (normQ.Contains(NormalizeLookupKey(c.ColumnCode)) || normQ.Contains(NormalizeLookupKey(c.ColumnName))));
        if (groupCol == null && (lower.Contains("theo ") || lower.Contains("bộ phận") || lower.Contains("trạng thái") || lower.Contains("từng ")))
        {
            groupCol = visibleColumns.FirstOrDefault(c => c.DataType is "SELECT" or "LOOKUP") ?? visibleColumns.FirstOrDefault();
        }
        plan.GroupByField = groupCol?.ColumnCode;
        plan.SortField = metricCol?.ColumnCode;
        return SanitizeNlFilterPlan(plan, visibleColumns);
    }

    public static AiNlFilterSpec SanitizeNlFilterPlan(AiNlFilterSpec raw, List<SysColumnDto> visibleColumns)
    {
        var allowed = visibleColumns.Select(c => c.ColumnCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clean = new AiNlFilterSpec
        {
            Search = string.IsNullOrWhiteSpace(raw.Search) ? null : raw.Search.Trim(),
            Status = string.IsNullOrWhiteSpace(raw.Status) ? null : raw.Status.Trim(),
            Logic = string.Equals(raw.Logic, "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND",
            AggregateMetric = raw.AggregateMetric?.ToUpperInvariant() is "SUM" or "AVG" ? raw.AggregateMetric.ToUpperInvariant() : "COUNT",
            MetricField = raw.MetricField != null && allowed.Contains(raw.MetricField) ? raw.MetricField : null,
            GroupByField = raw.GroupByField != null && (allowed.Contains(raw.GroupByField) || raw.GroupByField.Equals("approvalStatus", StringComparison.OrdinalIgnoreCase)) ? raw.GroupByField : null,
            SortField = raw.SortField != null && allowed.Contains(raw.SortField) ? raw.SortField : null,
            SortDir = string.Equals(raw.SortDir, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC"
        };

        foreach (var c in raw.Conditions ?? new List<AiNlFilterCondition>())
        {
            if (string.IsNullOrWhiteSpace(c.Field)) continue;
            if (!allowed.Contains(c.Field) && !c.Field.Equals("approvalStatus", StringComparison.OrdinalIgnoreCase)) continue;
            var op = c.Op?.Trim().ToLowerInvariant() ?? "contains";
            if (op is not ("eq" or "neq" or "contains" or "gt" or "gte" or "lt" or "lte" or "in" or "between" or "date_preset" or "is_empty" or "not_empty"))
                op = "contains";
            clean.Conditions.Add(new AiNlFilterCondition { Field = c.Field, Op = op, Value = c.Value ?? string.Empty, Value2 = c.Value2 });
        }
        return clean;
    }

    public async Task<AiDashboardInsightsResult> SummarizeDashboardQueryResultAsync(
        string question,
        AiNlFilterSpec plan,
        string planEngine,
        List<SysColumnDto> visibleColumns,
        List<AppRecordDto> matchingRecords,
        int totalMatches,
        string username)
    {
        decimal? aggregateVal = null;
        if (plan.AggregateMetric is "SUM" or "AVG" && !string.IsNullOrWhiteSpace(plan.MetricField))
        {
            var vals = matchingRecords
                .Select(r => TryGetDecimal(r.Data, plan.MetricField, out var v) ? (decimal?)v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (vals.Count > 0)
                aggregateVal = plan.AggregateMetric == "AVG" ? Math.Round(vals.Average(), 2) : vals.Sum();
        }
        else
        {
            aggregateVal = totalMatches;
        }

        var breakdown = new List<AiDashboardGroupStat>();
        if (!string.IsNullOrWhiteSpace(plan.GroupByField))
        {
            breakdown = matchingRecords
                .GroupBy(r =>
                {
                    if (plan.GroupByField.Equals("approvalStatus", StringComparison.OrdinalIgnoreCase))
                        return r.ApprovalStatus;
                    return r.Data.TryGetValue(plan.GroupByField, out var v) && !string.IsNullOrWhiteSpace(v?.ToString())
                        ? v.ToString()! : "(Trống)";
                })
                .Select(g => new AiDashboardGroupStat
                {
                    Group = g.Key,
                    Count = g.Count(),
                    MetricValue = !string.IsNullOrWhiteSpace(plan.MetricField)
                        ? g.Sum(x => TryGetDecimal(x.Data, plan.MetricField, out var mv) ? mv : 0m)
                        : g.Count()
                })
                .OrderByDescending(x => x.MetricValue)
                .Take(8)
                .ToList();
        }

        var metricLabel = !string.IsNullOrWhiteSpace(plan.MetricField)
            ? visibleColumns.FirstOrDefault(c => c.ColumnCode == plan.MetricField)?.ColumnName ?? plan.MetricField
            : "Số lượng hồ sơ";

        var answer = $"Tìm thấy **{totalMatches}** hồ sơ phù hợp" +
                     (aggregateVal.HasValue && plan.MetricField != null ? $" | **{plan.AggregateMetric} ({metricLabel})**: **{aggregateVal.Value:N0}**." : ".");
        if (breakdown.Count > 0)
        {
            answer += " Nhóm nổi bật: " + string.Join(", ", breakdown.Take(3).Select(b => $"{b.Group} ({b.MetricValue:N0})")) + ".";
        }

        var finalEngine = planEngine;
        var status = await GetAiStatusAsync();
        if (status.IsConfigured && matchingRecords.Count > 0)
        {
            var statsPrompt = $"Câu hỏi: {question}\nTổng hồ sơ khớp: {totalMatches}\n" +
                              $"Chỉ số {plan.AggregateMetric} ({metricLabel}): {aggregateVal}\n" +
                              $"Phân bổ nhóm:\n{string.Join("\n", breakdown.Select(b => $"- {b.Group}: {b.Count} hồ sơ, giá trị {b.MetricValue:N0}"))}";
            var (ok, llmText, providerName, model, _) = await CompleteAsync(
                "AI_DASHBOARD_INSIGHTS",
                "Bạn là nhà phân tích BI của SmartApps. Trả lời câu hỏi người dùng trong 2-3 câu tiếng Việt dựa chính xác trên số liệu tổng hợp được cung cấp, không bịa đặt thêm số.",
                statsPrompt,
                username);
            if (ok && !string.IsNullOrWhiteSpace(llmText))
            {
                answer = llmText.Trim();
                finalEngine = $"{providerName} ({model})";
            }
        }

        return new AiDashboardInsightsResult
        {
            Question = question,
            Answer = answer,
            AppliedPlan = plan,
            MatchingRecords = totalMatches,
            AggregateValue = aggregateVal,
            Breakdown = breakdown,
            SampleRecords = matchingRecords.Take(5).ToList(),
            Engine = finalEngine
        };
    }

    // =========================================================================
    // Shared Vietnamese Text & Numeric Normalization Helpers
    // =========================================================================

    private static string NormalizeLookupKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var replaced = input.Replace('đ', 'd').Replace('Đ', 'D');
        var decomposed = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return Regex.Replace(ascii, @"[^a-z0-9]+", "_").Trim('_');
    }

    private static double ScoreKeySimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        if (a == b) return 1.0;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) return 0.85;
        var ta = a.Split('_', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = b.Split('_', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var intersect = ta.Intersect(tb).Count();
        return intersect == 0 ? 0 : (2.0 * intersect) / (ta.Count + tb.Count);
    }

    private static bool TryGetDecimal(Dictionary<string, object?> data, string key, out decimal value)
    {
        value = 0;
        if (!data.TryGetValue(key, out var raw) || raw == null) return false;
        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out value)) return true;
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }
        return decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static object? ConvertJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetDecimal(out var dec) ? dec : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
