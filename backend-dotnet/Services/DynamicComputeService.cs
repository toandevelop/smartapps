using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class SubTableColumnDef
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public bool Required { get; set; }
    public string? Formula { get; set; }
    public string? RefCategoryCode { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValidationRegex { get; set; }
}

public class ComputedRecordResult
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, object?> EvaluatedData { get; set; } = new();
    public List<ValidationErrorItem> Errors { get; set; } = new();
    public bool IsValid { get; set; }
}

public class DynamicComputeService
{
    public List<ComputedRecordResult> EvaluateAndValidate(
        List<SysColumnDto> columns,
        Dictionary<string, HashSet<string>> categorySets,
        List<(string Id, Dictionary<string, object?> Data)> records,
        IReadOnlyList<FormRuleDto>? formRules = null)
    {
        var results = new List<ComputedRecordResult>(records.Count);

        foreach (var rec in records)
        {
            var data = new Dictionary<string, object?>(rec.Data);
            var errors = new List<ValidationErrorItem>();

            // 0. Normalize and evaluate formulas inside SUB_TABLE rows before parent rollups
            var subTableSchemas = new Dictionary<string, List<SubTableColumnDef>>(StringComparer.OrdinalIgnoreCase);
            foreach (var subCol in columns.Where(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)))
            {
                var subSchema = ParseSubTableSchema(subCol.DefaultValue);
                subTableSchemas[subCol.ColumnCode] = subSchema;
                if (data.TryGetValue(subCol.ColumnCode, out var rawSub) && rawSub != null)
                {
                    var rows = NormalizeSubTableRows(rawSub);
                    if (rows != null)
                    {
                        var subFormulaCols = subSchema.Where(sc => !string.IsNullOrWhiteSpace(sc.Formula)).ToList();
                        foreach (var row in rows)
                        {
                            if (!row.TryGetValue("_rowId", out var rid) || string.IsNullOrWhiteSpace(rid?.ToString()))
                                row["_rowId"] = Guid.NewGuid().ToString("N")[..8];
                            for (int pass = 0; pass < 2; pass++)
                            {
                                foreach (var sfc in subFormulaCols)
                                {
                                    var val = EvaluateFormulaValue(sfc.Formula!, row);
                                    if (val != null) row[sfc.Code] = val;
                                }
                            }
                        }
                        data[subCol.ColumnCode] = rows;
                    }
                }
            }

            // 1. Evaluate formulas (2 passes to resolve dependencies between calculated columns)
            var formulaCols = columns.Where(c => !string.IsNullOrWhiteSpace(c.Formula)).ToList();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var col in formulaCols)
                {
                    var val = EvaluateFormulaValue(col.Formula!, data);
                    if (val != null)
                    {
                        data[col.ColumnCode] = val;
                    }
                }
            }

            var ruleStates = formRules?.Count > 0 ? FormRuleService.Evaluate(columns, formRules, data) : null;
            // 2. Validate fields
            foreach (var col in columns)
            {
                data.TryGetValue(col.ColumnCode, out var raw);
                var str = InvariantText(raw)?.Trim() ?? string.Empty;

                var required = ruleStates != null ? ruleStates[col.ColumnCode].Visible && ruleStates[col.ColumnCode].Required : col.IsRequired;
                if (required && string.IsNullOrEmpty(str))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = col.ColumnCode,
                        Message = $"Trường '{col.ColumnName}' là bắt buộc."
                    });
                    continue;
                }

                if (string.IsNullOrEmpty(str)) continue;

                var dt = col.DataType.ToUpperInvariant();
                if (dt == "SUB_TABLE")
                {
                    if (raw is List<Dictionary<string, object?>> subRows &&
                        subTableSchemas.TryGetValue(col.ColumnCode, out var subSchema))
                    {
                        ValidateSubTableRows(col, subSchema, subRows, categorySets, errors);
                    }
                    continue;
                }
                if ((dt == "NUMBER" || dt == "CURRENCY") &&
                    !decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = col.ColumnCode,
                        Message = $"Trường '{col.ColumnName}' phải là số hợp lệ."
                    });
                }

                if (dt == "EMAIL" && !Regex.IsMatch(str, @"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$"))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = col.ColumnCode,
                        Message = $"Email '{col.ColumnName}' không đúng định dạng."
                    });
                }

                if (!string.IsNullOrWhiteSpace(col.ValidationRegex))
                {
                    try
                    {
                        if (!Regex.IsMatch(str, col.ValidationRegex))
                        {
                            errors.Add(new ValidationErrorItem
                            {
                                Severity = "ERROR",
                                Field = col.ColumnCode,
                                Message = $"Giá trị '{col.ColumnName}' không khớp mẫu quy định."
                            });
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(col.RefCategoryCode) &&
                    categorySets.TryGetValue(col.RefCategoryCode, out var allowed) &&
                    allowed.Count > 0 &&
                    !allowed.Contains(str))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "WARNING",
                        Field = col.ColumnCode,
                        Message = $"Mã '{str}' chưa thuộc danh mục {col.RefCategoryCode}."
                    });
                }
            }

            results.Add(new ComputedRecordResult
            {
                Id = rec.Id,
                EvaluatedData = data,
                Errors = errors,
                IsValid = errors.All(e => e.Severity != "ERROR")
            });
        }

        return results;
    }

    public object CompileSchema(SysTableDto table, List<SysColumnDto> columns)
    {
        var sqlCols = columns.Select(c =>
        {
            var sqlType = c.DataType.ToUpperInvariant() switch
            {
                "NUMBER" or "CURRENCY" => "NUMERIC(18,2)",
                "BOOLEAN" => "BOOLEAN",
                "DATE" => "DATE",
                _ => "TEXT"
            };
            var req = c.IsRequired ? " NOT NULL" : "";
            var refNote = !string.IsNullOrWhiteSpace(c.RefTableId) ? $" /* LOOKUP -> {c.RefTableId}.{c.RefValueField} */" : "";
            return $"  {c.ColumnCode} {sqlType}{req}{refNote}";
        });
        var sqlDdl = $"CREATE TABLE {table.TableName} (\n  id VARCHAR(64) PRIMARY KEY,\n{string.Join(",\n", sqlCols)}\n);";

        var tsFields = columns.Select(c =>
        {
            var tsType = c.DataType.ToUpperInvariant() switch
            {
                "NUMBER" or "CURRENCY" => "number",
                "BOOLEAN" => "boolean",
                _ => "string"
            };
            var opt = c.IsRequired ? "" : "?";
            var extra = !string.IsNullOrWhiteSpace(c.Formula) ? $" (formula: {c.Formula})" : "";
            return $"  {c.ColumnCode}{opt}: {tsType}; // {c.ColumnName}{extra}";
        });
        var tsInterface = $"export interface {ToPascal(table.TableName)} {{\n{string.Join("\n", tsFields)}\n}}";

        return new { sqlDdl, tsInterface };
    }

    private static object? EvaluateFormulaValue(string formula, Dictionary<string, object?> data)
    {
        // Pre-resolve SUB_TABLE rollup calls: SUM(sub_col.field) & COUNT(sub_col)
        var resolvedFormula = ResolveSubTableAggregates(formula.Trim(), data);
        var trimmed = resolvedFormula.Trim();

        if (trimmed.StartsWith("IF(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            var inner = trimmed[3..^1];
            var args = SplitTopLevelArgs(inner);
            if (args.Count == 3)
            {
                var cond = EvaluateConditionExpr(args[0], data);
                var chosen = cond ? args[1].Trim() : args[2].Trim();
                if ((chosen.StartsWith('\'') && chosen.EndsWith('\'')) || (chosen.StartsWith('"') && chosen.EndsWith('"')))
                    return chosen[1..^1];
                var num = EvaluateExpression(chosen, data);
                return num.HasValue ? num.Value : chosen;
            }
        }
        if (trimmed.StartsWith("ROUND(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            var args = SplitTopLevelArgs(trimmed[6..^1]);
            if (args.Count is 1 or 2)
            {
                var num = EvaluateExpression(args[0], data);
                var digits = args.Count == 2 && int.TryParse(args[1].Trim(), out var d) ? Math.Clamp(d, 0, 6) : 0;
                if (num.HasValue) return Math.Round(num.Value, digits);
            }
        }
        if (trimmed.StartsWith("DATEDIF(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            var args = SplitTopLevelArgs(trimmed[8..^1]);
            if (args.Count == 2)
            {
                var d1 = ResolveDate(args[0].Trim(), data);
                var d2 = ResolveDate(args[1].Trim(), data);
                if (d1.HasValue && d2.HasValue)
                    return (int)Math.Round((d2.Value - d1.Value).TotalDays);
            }
        }
        return EvaluateExpression(trimmed, data);
    }

    private static string ResolveSubTableAggregates(string expr, Dictionary<string, object?> data)
    {
        var withSum = Regex.Replace(expr, @"SUM\(\s*([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)\s*\)", m =>
        {
            var subCol = m.Groups[1].Value;
            var subField = m.Groups[2].Value;
            decimal sum = 0m;
            if (data.TryGetValue(subCol, out var raw) && raw is System.Collections.IEnumerable seq && raw is not string)
            {
                foreach (var item in seq)
                {
                    if (item is IDictionary<string, object?> dict && dict.TryGetValue(subField, out var v) &&
                        v != null && decimal.TryParse(InvariantText(v), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                    {
                        sum += num;
                    }
                    else if (item is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object &&
                             el.TryGetProperty(subField, out var prop) &&
                             decimal.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var jnum))
                    {
                        sum += jnum;
                    }
                }
            }
            return sum.ToString(CultureInfo.InvariantCulture);
        }, RegexOptions.IgnoreCase);

        return Regex.Replace(withSum, @"COUNT\(\s*([a-zA-Z0-9_]+)\s*\)", m =>
        {
            var subCol = m.Groups[1].Value;
            int cnt = 0;
            if (data.TryGetValue(subCol, out var raw) && raw is System.Collections.IEnumerable seq && raw is not string)
            {
                foreach (var _ in seq) cnt++;
            }
            return cnt.ToString(CultureInfo.InvariantCulture);
        }, RegexOptions.IgnoreCase);
    }

    private static DateTime? ResolveDate(string token, Dictionary<string, object?> data)
    {
        if (token.Equals("TODAY", StringComparison.OrdinalIgnoreCase) || token.Equals("TODAY()", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow.Date;
        var raw = data.TryGetValue(token, out var v) ? InvariantText(v) : token.Trim('\'', '"');
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt.Date : null;
    }

    private static bool EvaluateConditionExpr(string cond, Dictionary<string, object?> data)
    {
        string[] ops = [">=", "<=", "!=", "==", ">", "<"];
        foreach (var op in ops)
        {
            var idx = cond.IndexOf(op, StringComparison.Ordinal);
            if (idx <= 0) continue;
            var leftRaw = cond[..idx].Trim();
            var rightRaw = cond[(idx + op.Length)..].Trim();

            var leftNum = EvaluateExpression(leftRaw, data);
            var rightNum = EvaluateExpression(rightRaw, data);
            if (leftNum.HasValue && rightNum.HasValue)
            {
                return op switch
                {
                    ">=" => leftNum.Value >= rightNum.Value,
                    "<=" => leftNum.Value <= rightNum.Value,
                    ">" => leftNum.Value > rightNum.Value,
                    "<" => leftNum.Value < rightNum.Value,
                    "==" => leftNum.Value == rightNum.Value,
                    "!=" => leftNum.Value != rightNum.Value,
                    _ => false
                };
            }

            var leftStr = ResolveStringValue(leftRaw, data);
            var rightStr = ResolveStringValue(rightRaw, data);
            return op switch
            {
                "==" => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        return false;
    }

    private static string ResolveStringValue(string token, Dictionary<string, object?> data)
    {
        if ((token.StartsWith('\'') && token.EndsWith('\'')) || (token.StartsWith('"') && token.EndsWith('"')))
            return token[1..^1];
        if (data.TryGetValue(token, out var v)) return InvariantText(v)?.Trim() ?? string.Empty;
        return token;
    }

    private static List<string> SplitTopLevelArgs(string input)
    {
        var args = new List<string>();
        int depth = 0, start = 0;
        bool inQuotes = false;
        char quoteChar = '\0';
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (inQuotes)
            {
                if (c == quoteChar) inQuotes = false;
                continue;
            }
            if (c is '\'' or '"') { inQuotes = true; quoteChar = c; }
            else if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                args.Add(input[start..i]);
                start = i + 1;
            }
        }
        args.Add(input[start..]);
        return args;
    }

    private static decimal? EvaluateExpression(string formula, Dictionary<string, object?> data)
    {
        try
        {
            var tokens = Tokenize(formula, data);
            if (tokens == null || tokens.Count == 0) return null;
            int pos = 0;
            var val = ParseExpr(tokens, ref pos);
            if (pos == tokens.Count && val.HasValue)
            {
                return Math.Round(val.Value, 2);
            }
        }
        catch { }
        return null;
    }

    private enum TokType { Num, Op, LParen, RParen }
    private record Tok(TokType Type, decimal Num = 0, char Op = '\0');

    private static List<Tok>? Tokenize(string expr, Dictionary<string, object?> data)
    {
        var list = new List<Tok>();
        int i = 0;
        while (i < expr.Length)
        {
            var c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { list.Add(new Tok(TokType.LParen)); i++; }
            else if (c == ')') { list.Add(new Tok(TokType.RParen)); i++; }
            else if (c is '+' or '-' or '*' or '/') { list.Add(new Tok(TokType.Op, Op: c)); i++; }
            else if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                if (!decimal.TryParse(expr[start..i], NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                    return null;
                list.Add(new Tok(TokType.Num, Num: num));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
                var key = expr[start..i];
                if (!TryGetNumber(key, data, out var num)) return null;
                list.Add(new Tok(TokType.Num, Num: num));
            }
            else return null;
        }
        return list;
    }

    private static decimal? ParseExpr(List<Tok> tokens, ref int pos)
    {
        var left = ParseTerm(tokens, ref pos);
        if (!left.HasValue) return null;
        while (pos < tokens.Count && tokens[pos].Type == TokType.Op && tokens[pos].Op is '+' or '-')
        {
            var op = tokens[pos++].Op;
            var right = ParseTerm(tokens, ref pos);
            if (!right.HasValue) return null;
            left = op == '+' ? left.Value + right.Value : left.Value - right.Value;
        }
        return left;
    }

    private static decimal? ParseTerm(List<Tok> tokens, ref int pos)
    {
        var left = ParseFactor(tokens, ref pos);
        if (!left.HasValue) return null;
        while (pos < tokens.Count && tokens[pos].Type == TokType.Op && tokens[pos].Op is '*' or '/')
        {
            var op = tokens[pos++].Op;
            var right = ParseFactor(tokens, ref pos);
            if (!right.HasValue) return null;
            if (op == '/' && right.Value == 0) return null;
            left = op == '*' ? left.Value * right.Value : left.Value / right.Value;
        }
        return left;
    }

    private static decimal? ParseFactor(List<Tok> tokens, ref int pos)
    {
        if (pos >= tokens.Count) return null;
        var t = tokens[pos];
        if (t.Type == TokType.Num) { pos++; return t.Num; }
        if (t.Type == TokType.Op && t.Op == '-')
        {
            pos++;
            var inner = ParseFactor(tokens, ref pos);
            return inner.HasValue ? -inner.Value : null;
        }
        if (t.Type == TokType.LParen)
        {
            pos++;
            var val = ParseExpr(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos].Type == TokType.RParen) { pos++; return val; }
            return null;
        }
        return null;
    }

    private static bool TryGetNumber(string key, Dictionary<string, object?> data, out decimal val)
    {
        if (data.TryGetValue(key, out var raw) && raw != null &&
            decimal.TryParse(InvariantText(raw), NumberStyles.Any, CultureInfo.InvariantCulture, out val))
            return true;
        val = 0;
        return false;
    }

    private static string? InvariantText(object? value) => value is IFormattable formattable
        ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString();

    private static string ToPascal(string raw) =>
        string.Concat(raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    public static List<SubTableColumnDef> ParseSubTableSchema(string? defaultValue)
    {
        var raw = (defaultValue ?? string.Empty).Trim();
        if (raw.StartsWith("["))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<SubTableColumnDef>>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null && parsed.Count > 0)
                {
                    return parsed
                        .Where(c => !string.IsNullOrWhiteSpace(c.Code))
                        .Select(c => new SubTableColumnDef
                        {
                            Code = c.Code.Trim(),
                            Name = string.IsNullOrWhiteSpace(c.Name) ? c.Code.Trim() : c.Name.Trim(),
                            DataType = string.IsNullOrWhiteSpace(c.DataType) ? "STRING" : c.DataType.Trim().ToUpperInvariant(),
                            Required = c.Required,
                            Formula = string.IsNullOrWhiteSpace(c.Formula) ? null : c.Formula.Trim(),
                            RefCategoryCode = string.IsNullOrWhiteSpace(c.RefCategoryCode) ? null : c.RefCategoryCode.Trim(),
                            DefaultValue = c.DefaultValue,
                            ValidationRegex = string.IsNullOrWhiteSpace(c.ValidationRegex) ? null : c.ValidationRegex.Trim(),
                        })
                        .ToList();
                }
            }
            catch { }
        }
        var fallback = string.IsNullOrWhiteSpace(raw) ? "hang_muc,so_luong,ghi_chu" : raw;
        return fallback.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => new SubTableColumnDef { Code = s, Name = s, DataType = "STRING" })
            .ToList();
    }

    private static List<Dictionary<string, object?>>? NormalizeSubTableRows(object raw)
    {
        if (raw is List<Dictionary<string, object?>> list)
            return list.Select(r => new Dictionary<string, object?>(r)).ToList();

        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var text = el.GetString();
                if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("[")) return null;
                try { el = JsonDocument.Parse(text).RootElement; } catch { return null; }
            }
            if (el.ValueKind == JsonValueKind.Array)
            {
                var res = new List<Dictionary<string, object?>>();
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in item.EnumerateObject())
                        dict[prop.Name] = UnwrapElement(prop.Value);
                    res.Add(dict);
                }
                return res;
            }
        }
        else if (raw is string str && str.TrimStart().StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(str);
                return NormalizeSubTableRows(doc.RootElement);
            }
            catch { return null; }
        }
        else if (raw is System.Collections.IEnumerable seq && raw is not string)
        {
            var res = new List<Dictionary<string, object?>>();
            foreach (var item in seq)
            {
                if (item is IDictionary<string, object?> d)
                    res.Add(new Dictionary<string, object?>(d));
                else if (item is JsonElement jel && jel.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in jel.EnumerateObject())
                        dict[prop.Name] = UnwrapElement(prop.Value);
                    res.Add(dict);
                }
            }
            return res;
        }
        return null;
    }

    private static object? UnwrapElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => el.ToString()
    };

    private static void ValidateSubTableRows(
        SysColumnDto parentCol,
        List<SubTableColumnDef> subSchema,
        List<Dictionary<string, object?>> rows,
        Dictionary<string, HashSet<string>> categorySets,
        List<ValidationErrorItem> errors)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var subCol in subSchema)
            {
                row.TryGetValue(subCol.Code, out var raw);
                var str = InvariantText(raw)?.Trim() ?? string.Empty;
                var cellPath = $"{parentCol.ColumnCode}[{i}].{subCol.Code}";
                var rowLabel = $"{parentCol.ColumnName} (dòng {i + 1}) - {subCol.Name}";

                if (subCol.Required && string.IsNullOrEmpty(str))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = cellPath,
                        Message = $"Trường '{rowLabel}' là bắt buộc."
                    });
                    continue;
                }
                if (string.IsNullOrEmpty(str)) continue;

                var dt = subCol.DataType.ToUpperInvariant();
                if ((dt == "NUMBER" || dt == "CURRENCY") &&
                    !decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = cellPath,
                        Message = $"Trường '{rowLabel}' phải là số hợp lệ."
                    });
                }
                else if (dt == "DATE" && !DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = cellPath,
                        Message = $"Trường '{rowLabel}' phải là ngày hợp lệ."
                    });
                }
                else if (dt == "EMAIL" && !Regex.IsMatch(str, @"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$"))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "ERROR",
                        Field = cellPath,
                        Message = $"Email '{rowLabel}' không đúng định dạng."
                    });
                }

                if (!string.IsNullOrWhiteSpace(subCol.ValidationRegex))
                {
                    try
                    {
                        if (!Regex.IsMatch(str, subCol.ValidationRegex))
                        {
                            errors.Add(new ValidationErrorItem
                            {
                                Severity = "ERROR",
                                Field = cellPath,
                                Message = $"Giá trị '{rowLabel}' không khớp mẫu quy định."
                            });
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(subCol.RefCategoryCode) &&
                    categorySets.TryGetValue(subCol.RefCategoryCode, out var allowed) &&
                    allowed.Count > 0 &&
                    !allowed.Contains(str))
                {
                    errors.Add(new ValidationErrorItem
                    {
                        Severity = "WARNING",
                        Field = cellPath,
                        Message = $"Mã '{str}' tại '{rowLabel}' chưa thuộc danh mục {subCol.RefCategoryCode}."
                    });
                }
            }
        }
    }
}
