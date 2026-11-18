using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class FormRuleConfigurationException(string message) : Exception(message);
public class FormRuleValidationException(List<ValidationErrorItem> errors)
    : Exception("Dữ liệu không đáp ứng quy tắc biểu mẫu: " + string.Join("; ", errors.Select(e => e.Message)))
{
    public List<ValidationErrorItem> Errors { get; } = errors;
}
public sealed record FormFieldState(bool Visible, bool Required, bool ReadOnly);

public class FormRuleService(MetadataService metadata, DynamicComputeService compute)
{
    public static List<FormRuleDto> ReadRules(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("formRules", out var rules)) return new();
        if (rules.ValueKind != JsonValueKind.Array) throw new FormRuleConfigurationException("Cấu hình formRules phải là danh sách quy tắc.");
        try { return rules.Deserialize<List<FormRuleDto>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch (JsonException) { throw new FormRuleConfigurationException("Cấu hình quy tắc biểu mẫu không hợp lệ."); }
    }
    public static void ValidateDefinition(IReadOnlyList<FormRuleDto> rules, IReadOnlyList<SysColumnDto> columns)
    {
        void Fail(string message) => throw new FormRuleConfigurationException(message);
        if (rules.Count > 50) Fail("Tối đa 50 quy tắc trên mỗi bảng.");
        var byCode = columns.ToDictionary(c => c.ColumnCode);
        var ids = new HashSet<string>();
        var targets = new HashSet<string>();
        foreach (var rule in rules)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 64 || !ids.Add(rule.Id)) Fail("ID quy tắc phải hợp lệ và không trùng.");
            if (string.IsNullOrWhiteSpace(rule!.TargetField) || !byCode.ContainsKey(rule.TargetField)) Fail("Trường đích của quy tắc không tồn tại.");
            var target = byCode[rule.TargetField];
            if (rule.Property is not ("visible" or "required" or "readOnly")) Fail("Thuộc tính quy tắc không hợp lệ.");
            if (!targets.Add(rule.TargetField + ":" + rule.Property)) Fail("Mỗi trường chỉ có một quy tắc cho mỗi thuộc tính để tránh xung đột.");
            if (rule.Property == "readOnly" && (!string.IsNullOrWhiteSpace(target!.Formula) || target.DataType == "AUTO_NUMBER")) Fail("Cột tính/tự sinh đã do máy chủ quản lý, không cần quy tắc chỉ đọc.");
            if (rule.Match is not ("ALL" or "ANY") || rule.Conditions == null || rule.Conditions.Count is < 1 or > 10) Fail("Mỗi quy tắc cần 1–10 điều kiện và chế độ ALL/ANY.");
            foreach (var condition in rule.Conditions!)
            {
                if (condition == null || string.IsNullOrWhiteSpace(condition.Field) || !byCode.TryGetValue(condition.Field, out var source)) { Fail("Trường điều kiện không tồn tại."); continue; }
                if (condition.Field == rule.TargetField) Fail("Điều kiện phải dùng trường khác với trường đích.");
                if (!source.ShowInForm || source.HiddenForRoles.Count > 0 || !string.IsNullOrWhiteSpace(source.Formula)
                    || source.DataType is "SUB_TABLE" or "FILE" or "IMAGE" or "AUTO_NUMBER")
                    Fail("Trường điều kiện phải là trường nhập liệu có thể hiển thị cho người dùng, không phải cột ẩn/tính/tự sinh.");
                if (condition.Operator is not ("EQUALS" or "NOT_EQUALS" or "CONTAINS" or "GREATER_THAN" or "GREATER_OR_EQUAL" or "LESS_THAN" or "LESS_OR_EQUAL" or "IS_EMPTY" or "NOT_EMPTY")) Fail("Toán tử điều kiện không hợp lệ.");
                if (condition.Operator is "IS_EMPTY" or "NOT_EMPTY") continue;
                if (condition.Value == null || condition.Value.Length > 500) Fail("Giá trị so sánh bị thiếu hoặc quá dài.");
                var type = source.DataType.ToUpperInvariant();
                if (condition.Operator == "CONTAINS" && type is "NUMBER" or "CURRENCY" or "BOOLEAN" or "DATE") Fail("CONTAINS chỉ áp dụng cho trường văn bản.");
                if (condition.Operator is "GREATER_THAN" or "GREATER_OR_EQUAL" or "LESS_THAN" or "LESS_OR_EQUAL"
                    && type is not ("NUMBER" or "CURRENCY" or "DATE")) Fail("So sánh lớn/nhỏ cần trường số hoặc ngày.");
                if (type is "NUMBER" or "CURRENCY" && !TryNumber(condition.Value, out _)) Fail("Giá trị so sánh phải là số dùng dấu chấm thập phân.");
                if (type == "BOOLEAN" && condition.Value is not ("true" or "false")) Fail("Giá trị so sánh boolean phải là true hoặc false.");
                if (type == "DATE" && !DateOnly.TryParseExact(condition.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Fail("Giá trị ngày phải có định dạng yyyy-MM-dd.");
            }
        }
    }

    public static Dictionary<string, FormFieldState> Evaluate(IReadOnlyList<SysColumnDto> columns,
        IReadOnlyList<FormRuleDto> rules, IReadOnlyDictionary<string, object?> data)
    {
        var states = columns.ToDictionary(c => c.ColumnCode, c => new FormFieldState(c.ShowInForm, c.IsRequired, false));
        var byCode = columns.ToDictionary(c => c.ColumnCode);
        foreach (var rule in rules)
        {
            if (!states.TryGetValue(rule.TargetField, out var state)) continue;
            bool Check(FormConditionDto condition) => byCode.TryGetValue(condition.Field, out var source)
                && Matches(condition, source.DataType, data.GetValueOrDefault(condition.Field));
            var matches = rule.Match == "ANY" ? rule.Conditions.Any(Check) : rule.Conditions.All(Check);
            bool? value = matches ? rule.Value : rule.ElseValue;
            if (value == null) continue;
            states[rule.TargetField] = rule.Property switch {
                // Form rules never override the static form visibility baseline.
                "visible" => state with { Visible = byCode[rule.TargetField].ShowInForm && value.Value },
                "required" => state with { Required = value.Value },
                "readOnly" => state with { ReadOnly = value.Value },
                _ => state
            };
        }
        return states;
    }

    public static bool Matches(FormConditionDto condition, string dataType, object? value)
    {
        var raw = Text(value);
        var empty = string.IsNullOrWhiteSpace(raw);
        if (condition.Operator == "IS_EMPTY") return empty;
        if (condition.Operator == "NOT_EMPTY") return !empty;
        int comparison;
        var type = dataType.ToUpperInvariant();
        if (type is "NUMBER" or "CURRENCY")
        {
            if (!TryNumber(value, out var number) || !TryNumber(condition.Value, out var expected)) return false;
            comparison = number.CompareTo(expected);
        }
        else if (type == "BOOLEAN")
        {
            bool? Bool(string? text) => text?.ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };
            var actual = Bool(raw); var expected = Bool(condition.Value);
            if (actual == null || expected == null) return false;
            comparison = actual.Value.CompareTo(expected.Value);
        }
        else if (type == "DATE")
        {
            if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var actual)
                || !DateOnly.TryParseExact(condition.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expected)) return false;
            comparison = actual.CompareTo(expected);
        }
        else comparison = StringComparer.OrdinalIgnoreCase.Compare(raw ?? "", condition.Value ?? "");
        return condition.Operator switch {
            "EQUALS" => comparison == 0, "NOT_EQUALS" => comparison != 0,
            "CONTAINS" => (raw ?? "").Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "GREATER_THAN" => comparison > 0, "GREATER_OR_EQUAL" => comparison >= 0,
            "LESS_THAN" => comparison < 0, "LESS_OR_EQUAL" => comparison <= 0, _ => false
        };
    }

    public static Dictionary<string, object?> MergeWithDefaults(IReadOnlyList<SysColumnDto> columns,
        Dictionary<string, object?> submitted, Dictionary<string, object?>? original)
    {
        var data = original == null ? new Dictionary<string, object?>() : new(original);
        if (original == null)
            foreach (var column in columns.Where(c => !string.IsNullOrWhiteSpace(c.DefaultValue) && c.DataType is not ("AUTO_NUMBER" or "SUB_TABLE") && string.IsNullOrWhiteSpace(c.Formula)))
                try { data[column.ColumnCode] = ImportValueNormalizer.Normalize(column.DefaultValue, column.DataType, "invariant", "yyyy-MM-dd"); }
                catch (Exception ex) when (ex is FormatException or JsonException or OverflowException) { throw new FormRuleConfigurationException("Giá trị mặc định không hợp lệ: " + column.ColumnName); }
        foreach (var pair in submitted) data[pair.Key] = pair.Value;
        return data;
    }

    public static void EnforceWrite(IReadOnlyList<SysColumnDto> columns, IReadOnlyList<FormRuleDto> rules,
        Dictionary<string, object?> data, Dictionary<string, object?>? original, Dictionary<string, object?> submitted,
        Dictionary<string, HashSet<string>>? categories = null)
    {
        if (rules.Count == 0) return;
        ValidateDefinition(rules, columns);
        // Invalid typed source values must not turn a required/readonly condition into false.
        var sourceErrors = new List<ValidationErrorItem>();
        foreach (var column in columns.Where(c => rules.Any(r => r.Conditions.Any(condition => condition.Field == c.ColumnCode))))
        {
            var value = data.GetValueOrDefault(column.ColumnCode);
            var raw = Text(value);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var type = column.DataType.ToUpperInvariant();
            var invalid = value is JsonElement element && element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                || value is System.Collections.IDictionary || (value is System.Collections.IEnumerable && value is not string);
            if (type is "NUMBER" or "CURRENCY") invalid |= !TryNumber(value, out _);
            if (type == "BOOLEAN") invalid |= raw.ToLowerInvariant() is not ("true" or "false" or "1" or "0");
            if (type == "DATE") invalid |= !DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            if (!string.IsNullOrWhiteSpace(column.RefCategoryCode) && categories != null)
                invalid |= !categories.TryGetValue(column.RefCategoryCode, out var allowed) || !allowed.Contains(raw);
            if (invalid) sourceErrors.Add(new() { Severity = "ERROR", Field = column.ColumnCode, Message = $"Giá trị trường điều kiện '{column.ColumnName}' không hợp lệ." });
        }
        if (sourceErrors.Count > 0) throw new FormRuleValidationException(sourceErrors);
        var states = Evaluate(columns, rules, data);
        var oldStates = original == null ? null : Evaluate(columns, rules, original);
        var defaults = MergeWithDefaults(columns, new(), null);
        var errors = new List<ValidationErrorItem>();
        foreach (var column in columns)
        {
            var state = states[column.ColumnCode];
            if (state.Visible && state.Required && string.IsNullOrWhiteSpace(Text(data.GetValueOrDefault(column.ColumnCode))))
                errors.Add(new() { Severity = "ERROR", Field = column.ColumnCode, Message = $"Trường '{column.ColumnName}' là bắt buộc theo biểu mẫu." });
            var conditionallyHidden = rules.Any(r => r.TargetField == column.ColumnCode && r.Property == "visible") && !state.Visible;
            var readOnly = state.ReadOnly || oldStates?.GetValueOrDefault(column.ColumnCode)?.ReadOnly == true;
            if (submitted.TryGetValue(column.ColumnCode, out var value) && (readOnly || conditionallyHidden)
                && !SameValue(column, value, (original ?? defaults).GetValueOrDefault(column.ColumnCode)))
                errors.Add(new() { Severity = "ERROR", Field = column.ColumnCode, Message = $"Không được thay đổi trường '{column.ColumnName}' khi trường đang ẩn hoặc chỉ đọc theo quy tắc." });
        }
        if (errors.Count > 0) throw new FormRuleValidationException(errors);
    }

    public async Task<Dictionary<string, object?>> PrepareWriteAsync(string tableId, Dictionary<string, object?> submitted, Dictionary<string, object?>? original)
    {
        var table = (await metadata.GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == tableId);
        var rules = table?.FormRules ?? new();
        if (rules.Count == 0) return original == null ? submitted : original.Concat(submitted).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        var columns = await metadata.GetColumnsAsync(tableId);
        ValidateDefinition(rules, columns);
        var categories = new Dictionary<string, HashSet<string>>();
        foreach (var code in columns.Where(c => !string.IsNullOrWhiteSpace(c.RefCategoryCode)).Select(c => c.RefCategoryCode!).Distinct())
            categories[code] = (await metadata.GetCategoryItemsAsync(code)).Select(item => item.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = MergeWithDefaults(columns, submitted, original);
        var computed = compute.EvaluateAndValidate(columns, categories, [("rule_check", merged)], rules).Single().EvaluatedData;
        EnforceWrite(columns, rules, computed, original, submitted, categories);
        return computed;
    }

    private static string? Text(object? value) => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString();
    private static bool TryNumber(object? value, out decimal result) => decimal.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool SameValue(SysColumnDto column, object? left, object? right)
    {
        if (column.DataType is "NUMBER" or "CURRENCY" && TryNumber(left, out var a) && TryNumber(right, out var b)) return a == b;
        if (column.DataType == "BOOLEAN")
        {
            string? Normalize(object? value) => Text(value)?.ToLowerInvariant() switch { "true" or "1" => "true", "false" or "0" => "false", _ => null };
            var l = Normalize(left); var r = Normalize(right);
            if (l != null && r != null) return l == r;
        }
        if (string.IsNullOrEmpty(Text(left)) && string.IsNullOrEmpty(Text(right))) return true;
        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));
    }
}
