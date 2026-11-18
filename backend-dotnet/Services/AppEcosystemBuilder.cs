using System.Text.RegularExpressions;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

/// <summary>
/// Stateless builders for per-App Dynamic OpenAPI 3.0.3 specs and Model Context Protocol (MCP) tool schemas.
/// </summary>
public static class AppEcosystemBuilder
{
    public static Dictionary<string, object?> BuildOpenApiDocument(
        SysAppDto app,
        IReadOnlyList<(SysTableDto Table, List<SysColumnDto> Columns)> tables,
        string serverUrl)
    {
        var schemas = new Dictionary<string, object?>();
        var paths = new Dictionary<string, object?>();

        foreach (var (tbl, cols) in tables)
        {
            var schemaName = SanitizeSchemaName(tbl.TableName, tbl.Id);
            var props = new Dictionary<string, object?>();
            var required = new List<string>();

            foreach (var col in cols)
            {
                props[col.ColumnCode] = MapColumnToJsonSchema(col);
                if (col.IsRequired) required.Add(col.ColumnCode);
            }

            schemas[schemaName] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["description"] = tbl.DisplayName,
                ["properties"] = props,
                ["required"] = required
            };

            var collectionPath = $"/api/external/tables/{tbl.Id}/records";
            paths[collectionPath] = new Dictionary<string, object?>
            {
                ["get"] = new
                {
                    tags = new[] { tbl.DisplayName },
                    summary = $"Truy vấn danh sách bản ghi — {tbl.DisplayName}",
                    parameters = new object[]
                    {
                        new { name = "page", @in = "query", schema = new { type = "integer", @default = 1 } },
                        new { name = "limit", @in = "query", schema = new { type = "integer", @default = 50 } }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = $"Danh sách {tbl.DisplayName}" }
                    }
                },
                ["post"] = new
                {
                    tags = new[] { tbl.DisplayName },
                    summary = $"Tạo bản ghi mới — {tbl.DisplayName}",
                    requestBody = new
                    {
                        required = true,
                        content = new Dictionary<string, object>
                        {
                            ["application/json"] = new { schema = new Dictionary<string, string> { ["$ref"] = $"#/components/schemas/{schemaName}" } }
                        }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Bản ghi đã lưu" },
                        ["409"] = new { description = "Trùng khóa định danh hoặc xung đột phiên bản" }
                    }
                }
            };

            paths[$"/api/external/tables/{tbl.Id}/records/{{keyOrId}}"] = new Dictionary<string, object?>
            {
                ["put"] = new
                {
                    tags = new[] { tbl.DisplayName },
                    summary = $"Tạo hoặc cập nhật (Upsert) theo RecordKey/ID — {tbl.DisplayName}",
                    parameters = new[]
                    {
                        new { name = "keyOrId", @in = "path", required = true, schema = new { type = "string" } }
                    },
                    requestBody = new
                    {
                        required = true,
                        content = new Dictionary<string, object>
                        {
                            ["application/json"] = new { schema = new Dictionary<string, string> { ["$ref"] = $"#/components/schemas/{schemaName}" } }
                        }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Upsert thành công" }
                    }
                }
            };

            paths[$"/api/external/tables/{tbl.Id}/changes"] = new Dictionary<string, object?>
            {
                ["get"] = new
                {
                    tags = new[] { tbl.DisplayName },
                    summary = $"Đồng bộ thay đổi theo mốc thời gian (Delta Sync) — {tbl.DisplayName}",
                    parameters = new object[]
                    {
                        new { name = "since", @in = "query", schema = new { type = "string", format = "date-time" } },
                        new { name = "limit", @in = "query", schema = new { type = "integer", @default = 100 } }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Danh sách bản ghi thay đổi" }
                    }
                }
            };
        }

        paths[$"/api/mcp/{app.Code}"] = new Dictionary<string, object?>
        {
            ["post"] = new
            {
                tags = new[] { "MCP Gateway" },
                summary = $"Model Context Protocol (JSON-RPC 2.0) cho App {app.Code}",
                responses = new Dictionary<string, object> { ["200"] = new { description = "JSON-RPC 2.0 response" } }
            }
        };

        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.0.3",
            ["info"] = new
            {
                title = $"SmartApps API — {app.Name} ({app.Code})",
                version = "1.0.0",
                description = app.Description ?? $"OpenAPI spec tự động sinh từ metadata các bảng của ứng dụng {app.Name}."
            },
            ["servers"] = string.IsNullOrWhiteSpace(serverUrl) ? Array.Empty<object>() : new[] { new { url = serverUrl } },
            ["security"] = new[] { new Dictionary<string, object> { ["ApiKeyAuth"] = Array.Empty<string>() } },
            ["paths"] = paths,
            ["components"] = new
            {
                securitySchemes = new Dictionary<string, object>
                {
                    ["ApiKeyAuth"] = new { type = "apiKey", @in = "header", name = "X-Api-Key" }
                },
                schemas
            }
        };
    }

    public static List<object> BuildMcpTools(
        SysAppDto app,
        IReadOnlyList<(SysTableDto Table, List<SysColumnDto> Columns)> tables)
    {
        var tableSummary = string.Join(", ", tables.Select(t => $"{t.Table.Id} ({t.Table.DisplayName})"));
        var tableIds = tables.Select(t => t.Table.Id).ToArray();

        return new List<object>
        {
            new
            {
                name = "list_records",
                description = $"Truy vấn bản ghi trong ứng dụng {app.Name} ({app.Code}). Các bảng khả dụng: {tableSummary}",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        tableId = new { type = "string", @enum = tableIds, description = "ID bảng cần truy vấn" },
                        search = new { type = "string", description = "Từ khóa tìm kiếm (tùy chọn)" },
                        status = new { type = "string", description = "Lọc trạng thái bản ghi hoặc ALL" },
                        limit = new { type = "integer", description = "Số bản ghi tối đa (1-100)" }
                    },
                    required = new[] { "tableId" }
                }
            },
            new
            {
                name = "get_record",
                description = $"Lấy chi tiết một bản ghi trong ứng dụng {app.Name} theo ID.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        recordId = new { type = "string", description = "ID bản ghi cần xem" }
                    },
                    required = new[] { "recordId" }
                }
            },
            new
            {
                name = "upsert_record",
                description = $"Tạo mới hoặc cập nhật bản ghi vào một bảng của {app.Name}.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        tableId = new { type = "string", @enum = tableIds, description = "ID bảng đích" },
                        recordId = new { type = "string", description = "ID bản ghi nếu cập nhật" },
                        data = new { type = "object", description = "Cặp khóa-giá trị theo columnCode của bảng" }
                    },
                    required = new[] { "tableId", "data" }
                }
            },
            new
            {
                name = "submit_or_approve_record",
                description = $"Trình duyệt hoặc phê duyệt/từ chối hồ sơ trong quy trình của {app.Name}.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        recordId = new { type = "string", description = "ID hồ sơ" },
                        action = new { type = "string", @enum = new[] { "submit", "approve", "reject", "return" }, description = "Hành động workflow" },
                        reason = new { type = "string", description = "Ghi chú hoặc lý do (tùy chọn)" }
                    },
                    required = new[] { "recordId", "action" }
                }
            }
        };
    }

    private static Dictionary<string, object?> MapColumnToJsonSchema(SysColumnDto col) =>
        col.DataType.ToUpperInvariant() switch
        {
            "NUMBER" or "CURRENCY" => new Dictionary<string, object?> { ["type"] = "number", ["description"] = col.ColumnName },
            "BOOLEAN" => new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = col.ColumnName },
            "DATE" => new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date", ["description"] = col.ColumnName },
            "DATETIME" => new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = col.ColumnName },
            "SUB_TABLE" => new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, string> { ["type"] = "object" },
                ["description"] = col.ColumnName
            },
            _ => new Dictionary<string, object?> { ["type"] = "string", ["description"] = col.ColumnName }
        };

    private static string SanitizeSchemaName(string tableName, string tableId)
    {
        var raw = string.IsNullOrWhiteSpace(tableName) ? tableId : tableName;
        var clean = Regex.Replace(raw, @"[^a-zA-Z0-9_]", "_");
        return string.IsNullOrWhiteSpace(clean) ? "Record" : clean;
    }
}
