using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

/// <summary>Small Open XML writer: exports values without evaluating user-supplied formulas.</summary>
public static class ExcelExportService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static byte[] CreateWorkbook(IReadOnlyList<SysColumnDto> columns, IReadOnlyList<AppRecordDto> records)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetCount = Math.Max(1, (int)Math.Ceiling(records.Count / 1048575d));
            WritePart(zip, "[Content_Types].xml", $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  {string.Concat(Enumerable.Range(1, sheetCount).Select(n => $"<Override PartName=\"/xl/worksheets/sheet{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))}
                </Types>
                """);
            WritePart(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """
            );
            WritePart(zip, "xl/workbook.xml", $"""
                <workbook xmlns="{SpreadsheetNs}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>
                {string.Concat(Enumerable.Range(1, sheetCount).Select(n => $"<sheet name=\"Data{n}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>"))}
                </sheets></workbook>
                """
            );
            WritePart(zip, "xl/_rels/workbook.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                {string.Concat(Enumerable.Range(1, sheetCount).Select(n => $"<Relationship Id=\"rId{n}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{n}.xml\"/>"))}
                </Relationships>
                """
            );
            for (var sheet = 0; sheet < sheetCount; sheet++)
            {
                using var stream = zip.CreateEntry($"xl/worksheets/sheet{sheet + 1}.xml").Open();
                using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
                xml.WriteStartElement("worksheet", SpreadsheetNs);
                xml.WriteStartElement("sheetData", SpreadsheetNs);
                xml.WriteStartElement("row", SpreadsheetNs);
                foreach (var col in columns) WriteCell(xml, col.ColumnCode, "STRING");
                xml.WriteEndElement();
                var end = Math.Min(records.Count, (sheet + 1) * 1048575);
                for (var i = sheet * 1048575; i < end; i++)
                {
                    xml.WriteStartElement("row", SpreadsheetNs);
                    foreach (var col in columns)
                    {
                        records[i].Data.TryGetValue(col.ColumnCode, out var value);
                        WriteCell(xml, value, col.DataType);
                    }
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
        }
        return output.ToArray();
    }

    private static void WritePart(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteCell(XmlWriter xml, object? value, string dataType)
    {
        var text = value is JsonElement el ? el.ToString() : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        xml.WriteStartElement("c", SpreadsheetNs);
        if ((dataType.ToUpperInvariant() is "NUMBER" or "CURRENCY")
            && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            xml.WriteElementString("v", SpreadsheetNs, number.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            xml.WriteAttributeString("t", "inlineStr");
            xml.WriteStartElement("is", SpreadsheetNs);
            xml.WriteStartElement("t", SpreadsheetNs);
            xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            // Excel cells accept at most 32,767 UTF-16 code units; discard invalid XML controls.
            var safe = string.Concat(text.EnumerateRunes()
                .Where(r => r.Value is 9 or 10 or 13 or >= 32 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
                .Select(r => r.ToString()));
            var length = Math.Min(safe.Length, 32767);
            if (length > 0 && char.IsHighSurrogate(safe[length - 1])) length--;
            xml.WriteString(safe[..length]);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }
}
