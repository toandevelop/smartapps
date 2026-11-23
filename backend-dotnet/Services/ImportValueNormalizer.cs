using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartApps.Api.Services;

public static class ImportValueNormalizer
{
    public static object? Normalize(object? value, string dataType, string numberFormat, string dateFormat)
    {
        if (value is JsonElement element)
            value = element.ValueKind switch {
                JsonValueKind.Null => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.Clone()
            };
        if (value == null) return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (text.Length == 0) return null;
        switch (dataType.ToUpperInvariant())
        {
            case "NUMBER": case "CURRENCY": case "RATING": case "PROGRESS":
                if (value is decimal or int or long or double or float) return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                var pattern = numberFormat == "vi-VN"
                    ? @"^[+-]?(?:\d+|\d{1,3}(?:\.\d{3})+)(?:,\d+)?$"
                    : @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$";
                if (!Regex.IsMatch(text, pattern)) throw new FormatException("Số không đúng định dạng đã chọn.");
                text = numberFormat == "vi-VN" ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
                return decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            case "DATE":
                if (!DateOnly.TryParseExact(text, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new FormatException($"Ngày không hợp lệ; dùng định dạng {dateFormat}.");
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "BOOLEAN":
                return text.ToLowerInvariant() switch {
                    "true" or "1" or "yes" or "có" or "co" => true,
                    "false" or "0" or "no" or "không" or "khong" => false,
                    _ => throw new FormatException("Giá trị phải là true/false, 1/0 hoặc có/không.")
                };
            case "SUB_TABLE":
                using (var doc = JsonDocument.Parse(text))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))
                        throw new FormatException("Bảng con phải là mảng JSON gồm các đối tượng.");
                    return doc.RootElement.Clone();
                }
            default: return text;
        }
    }
}
