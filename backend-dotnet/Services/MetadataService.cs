using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class MetadataService
{
    private readonly string _connStr;
    private readonly AiService? _ai;

    private static readonly HashSet<string> AllowedAiColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "STRING", "NUMBER", "CURRENCY", "DATE", "BOOLEAN", "SELECT",
        "EMAIL", "TEL", "TEXTAREA", "FILE", "IMAGE", "SUB_TABLE", "AUTO_NUMBER"
    };

    public MetadataService(IConfiguration config, AiService? ai = null)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _ai = ai;
    }

    public async Task<List<SysAppDto>> GetAppsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT a.id, a.code, a.name, a.description, a.department, a.icon, a.color, a.bg_color,
                   a.version, a.order_index, a.is_active, a.settings::text, a.created_at, a.updated_at,
                   (SELECT COUNT(*) FROM sys_modules m WHERE m.app_id = a.id AND m.is_active = TRUE) AS module_count,
                   (SELECT COUNT(*) FROM sys_tables t WHERE t.app_id = a.id AND t.is_active = TRUE) AS table_count,
                   CASE WHEN UPPER(a.code) = 'TASKS' THEN (SELECT COUNT(*) FROM app_tasks)
                        ELSE (SELECT COUNT(*) FROM app_records r WHERE r.app_id = a.id AND r.deleted_at IS NULL)
                   END AS record_count,
                   COALESCE(a.published_version, 1), COALESCE(a.publish_status, 'PUBLISHED'), a.published_at, a.published_by
            FROM sys_apps a
            ORDER BY a.order_index ASC, a.created_at ASC
            """;

        var list = new List<SysAppDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(ReadApp(r));
        }
        return list;
    }

    public async Task<SysAppDto?> GetAppByCodeOrIdAsync(string codeOrId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT a.id, a.code, a.name, a.description, a.department, a.icon, a.color, a.bg_color,
                   a.version, a.order_index, a.is_active, a.settings::text, a.created_at, a.updated_at,
                   (SELECT COUNT(*) FROM sys_modules m WHERE m.app_id = a.id AND m.is_active = TRUE) AS module_count,
                   (SELECT COUNT(*) FROM sys_tables t WHERE t.app_id = a.id AND t.is_active = TRUE) AS table_count,
                   CASE WHEN UPPER(a.code) = 'TASKS' THEN (SELECT COUNT(*) FROM app_tasks)
                        ELSE (SELECT COUNT(*) FROM app_records r WHERE r.app_id = a.id AND r.deleted_at IS NULL)
                   END AS record_count,
                   COALESCE(a.published_version, 1), COALESCE(a.publish_status, 'PUBLISHED'), a.published_at, a.published_by
            FROM sys_apps a
            WHERE UPPER(a.code) = UPPER(@key) OR a.id = @key
            LIMIT 1
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", codeOrId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadApp(r) : null;
    }

    public async Task<AppRuntimeTreeDto?> GetAppRuntimeTreeAsync(string codeOrId)
    {
        var app = await GetAppByCodeOrIdAsync(codeOrId);
        if (app == null) return null;

        var modules = await GetModulesAsync(app.Id);
        var tables = await GetTablesAsync(app.Id, null);

        var tree = new AppRuntimeTreeDto { App = app };
        foreach (var mod in modules)
        {
            var modNode = new ModuleWithTablesDto { Module = mod };
            var modTables = tables.Where(t => t.ModuleId == mod.Id).ToList();
            foreach (var tbl in modTables)
            {
                var cols = await GetColumnsAsync(tbl.Id);
                modNode.Tables.Add(new TableWithColumnsDto { Table = tbl, Columns = cols });
            }
            tree.Modules.Add(modNode);
        }
        return tree;
    }

    public async Task<SysAppDto> UpsertAppAsync(UpsertAppRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var code = req.Code.Trim().ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"app_{code.ToLowerInvariant()}" : req.Id.Trim();

        var sql = """
            INSERT INTO sys_apps (id, code, name, description, department, icon, color, bg_color, version, order_index, is_active, updated_at)
            VALUES (@id, @code, @name, @desc, @dept, @icon, @color, @bg, @ver, @ord, @act, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                code = EXCLUDED.code,
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                department = EXCLUDED.department,
                icon = EXCLUDED.icon,
                color = EXCLUDED.color,
                bg_color = EXCLUDED.bg_color,
                version = EXCLUDED.version,
                order_index = EXCLUDED.order_index,
                is_active = EXCLUDED.is_active,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dept", string.IsNullOrWhiteSpace(req.Department) ? "Toàn công ty" : req.Department.Trim());
        cmd.Parameters.AddWithValue("icon", string.IsNullOrWhiteSpace(req.Icon) ? "Apps" : req.Icon.Trim());
        cmd.Parameters.AddWithValue("color", string.IsNullOrWhiteSpace(req.Color) ? "#1a73e8" : req.Color.Trim());
        cmd.Parameters.AddWithValue("bg", string.IsNullOrWhiteSpace(req.BgColor) ? "#e8f0fe" : req.BgColor.Trim());
        cmd.Parameters.AddWithValue("ver", string.IsNullOrWhiteSpace(req.Version) ? "1.0.0" : req.Version.Trim());
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("act", req.IsActive);
        await cmd.ExecuteNonQueryAsync();

        return (await GetAppByCodeOrIdAsync(id))!;
    }

    public async Task<bool> DeleteAppAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_apps WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<SysModuleDto>> GetModulesAsync(string? appId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT m.id, m.app_id, m.code, m.name, m.icon, m.order_index, m.is_active, m.description,
                   (SELECT COUNT(*) FROM sys_tables t WHERE t.module_id = m.id AND t.is_active = TRUE) AS table_count,
                   (SELECT COUNT(*) FROM app_records r WHERE r.module_id = m.id) AS record_count
            FROM sys_modules m
            WHERE (@appId IS NULL OR m.app_id = @appId)
            ORDER BY m.order_index ASC, m.created_at ASC
            """;

        var list = new List<SysModuleDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)appId ?? DBNull.Value });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysModuleDto
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                Code = r.GetString(2),
                Name = r.GetString(3),
                Icon = r.IsDBNull(4) ? "Layers" : r.GetString(4),
                OrderIndex = r.GetInt32(5),
                IsActive = r.GetBoolean(6),
                Description = r.IsDBNull(7) ? null : r.GetString(7),
                TableCount = Convert.ToInt32(r.GetInt64(8)),
                RecordCount = Convert.ToInt32(r.GetInt64(9))
            });
        }
        return list;
    }

    public async Task<SysModuleDto> UpsertModuleAsync(UpsertModuleRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var code = req.Code.Trim().ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"mod_{req.AppId.Replace("app_", "")}_{code.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..4]}"
            : req.Id.Trim();

        var sql = """
            INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, is_active, description, updated_at)
            VALUES (@id, @appId, @code, @name, @icon, @ord, @act, @desc, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                code = EXCLUDED.code,
                name = EXCLUDED.name,
                icon = EXCLUDED.icon,
                order_index = EXCLUDED.order_index,
                is_active = EXCLUDED.is_active,
                description = EXCLUDED.description,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("appId", req.AppId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.AddWithValue("icon", string.IsNullOrWhiteSpace(req.Icon) ? "Layers" : req.Icon.Trim());
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("act", req.IsActive);
        cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        await MarkAppDraftAsync(conn, req.AppId);

        return (await GetModulesAsync(req.AppId)).First(m => m.Id == id);
    }

    public async Task<bool> DeleteModuleAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_modules WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<SysTableDto>> GetTablesAsync(string? appId, string? moduleId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT t.id, t.app_id, t.module_id, t.table_name, t.display_name, t.description,
                   t.primary_view, COALESCE(t.row_scope, 'ALL'), t.order_index, t.is_active, t.ui_config::text,
                   (SELECT COUNT(*) FROM sys_columns c WHERE c.table_id = t.id AND c.is_active = TRUE) AS col_count,
                   (SELECT COUNT(*) FROM app_records r WHERE r.table_id = t.id AND r.deleted_at IS NULL) AS rec_count,
                   COALESCE(t.approval_enabled, FALSE), COALESCE(t.approval_field, 'trang_thai_duyet'),
                   COALESCE(t.approver_roles::text, '["ADMIN","HR_MANAGER","ASSET_LEAD"]'),
                   COALESCE(t.public_form_enabled, FALSE)
            FROM sys_tables t
            WHERE t.is_active = TRUE
              AND (@appId IS NULL OR t.app_id = @appId)
              AND (@moduleId IS NULL OR t.module_id = @moduleId)
            ORDER BY t.order_index ASC, t.created_at ASC
            """;

        var list = new List<SysTableDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("appId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)appId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("moduleId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)moduleId ?? DBNull.Value });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysTableDto
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                ModuleId = r.GetString(2),
                TableName = r.GetString(3),
                DisplayName = r.GetString(4),
                Description = r.IsDBNull(5) ? null : r.GetString(5),
                PrimaryView = r.IsDBNull(6) ? "table" : r.GetString(6),
                RowScope = r.IsDBNull(7) ? "ALL" : r.GetString(7),
                OrderIndex = r.GetInt32(8),
                IsActive = r.GetBoolean(9),
                UiConfig = JsonDocument.Parse(r.GetString(10)).RootElement,
                FormRules = FormRuleService.ReadRules(JsonDocument.Parse(r.GetString(10)).RootElement),
                ColumnCount = Convert.ToInt32(r.GetInt64(11)),
                RecordCount = Convert.ToInt32(r.GetInt64(12)),
                ApprovalEnabled = r.GetBoolean(13),
                ApprovalField = r.IsDBNull(14) ? "trang_thai_duyet" : r.GetString(14),
                ApproverRoles = JsonSerializer.Deserialize<List<string>>(r.GetString(15)) ?? new() { "ADMIN", "HR_MANAGER", "ASSET_LEAD" },
                PublicFormEnabled = r.GetBoolean(16)
            });
        }
        return list;
    }

    public async Task<SysTableDto> UpsertTableAsync(UpsertTableRequest req)
    {
        if (req.FormRules != null)
            FormRuleService.ValidateDefinition(req.FormRules, string.IsNullOrWhiteSpace(req.Id) ? new List<SysColumnDto>() : await GetColumnsAsync(req.Id));
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var tableName = req.TableName.Trim().ToLowerInvariant().Replace(" ", "_");
        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"tbl_{tableName}_{Guid.NewGuid().ToString("N")[..4]}"
            : req.Id.Trim();

        var sql = """
            INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope,
                approval_enabled, approval_field, approver_roles, public_form_enabled, order_index, is_active, ui_config, updated_at)
            VALUES (@id, @appId, @modId, @tn, @dn, @desc, @pv, @rs, @ae, @af, @ar::jsonb, @pfe, @ord, @act, COALESCE(@ui::jsonb,'{}'::jsonb), CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                table_name = EXCLUDED.table_name,
                display_name = EXCLUDED.display_name,
                description = EXCLUDED.description,
                primary_view = EXCLUDED.primary_view,
                row_scope = EXCLUDED.row_scope,
                approval_enabled = EXCLUDED.approval_enabled,
                approval_field = EXCLUDED.approval_field,
                approver_roles = EXCLUDED.approver_roles,
                public_form_enabled = EXCLUDED.public_form_enabled,
                order_index = EXCLUDED.order_index,
                is_active = EXCLUDED.is_active,
                ui_config = CASE WHEN @ui IS NULL THEN sys_tables.ui_config ELSE sys_tables.ui_config || EXCLUDED.ui_config END,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("appId", req.AppId);
        cmd.Parameters.AddWithValue("modId", req.ModuleId);
        cmd.Parameters.AddWithValue("tn", tableName);
        cmd.Parameters.AddWithValue("dn", req.DisplayName.Trim());
        cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pv", string.IsNullOrWhiteSpace(req.PrimaryView) ? "table" : req.PrimaryView);
        cmd.Parameters.AddWithValue("rs", string.IsNullOrWhiteSpace(req.RowScope) ? "ALL" : req.RowScope);
        cmd.Parameters.AddWithValue("ae", req.ApprovalEnabled);
        cmd.Parameters.AddWithValue("af", string.IsNullOrWhiteSpace(req.ApprovalField) ? "trang_thai_duyet" : req.ApprovalField.Trim());
        cmd.Parameters.AddWithValue("ar", JsonSerializer.Serialize(req.ApproverRoles?.Count > 0 ? req.ApproverRoles : new List<string> { "ADMIN", "HR_MANAGER", "ASSET_LEAD" }));
        cmd.Parameters.AddWithValue("pfe", req.PublicFormEnabled);
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("act", req.IsActive);
        cmd.Parameters.Add(new NpgsqlParameter("ui", NpgsqlDbType.Jsonb) { Value = req.FormRules == null ? DBNull.Value : JsonSerializer.Serialize(new { formRules = req.FormRules }) });
        await cmd.ExecuteNonQueryAsync();
        await MarkAppDraftAsync(conn, req.AppId);

        return (await GetTablesAsync(req.AppId, req.ModuleId)).First(t => t.Id == id);
    }

    public async Task<bool> DeleteTableAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_tables WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task SetFormRulesAsync(string tableId, List<FormRuleDto> rules)
    {
        FormRuleService.ValidateDefinition(rules, await GetColumnsAsync(tableId));
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sys_tables SET ui_config=jsonb_set(ui_config,'{formRules}',@rules::jsonb), updated_at=CURRENT_TIMESTAMP WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("rules", JsonSerializer.Serialize(rules));
        cmd.Parameters.AddWithValue("id", tableId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<SysColumnDto>> GetColumnsAsync(string tableId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT id, table_id, column_code, column_name, excel_header, data_type,
                   is_required, is_identity_key, show_in_table, show_in_form, width,
                   validation_regex, ref_category_code, ref_app_id, ref_table_id, ref_value_field, ref_display_field,
                   formula, default_value, COALESCE(hidden_for_roles, '[]'::jsonb)::text,
                   order_index, is_active, COALESCE(read_only_for_roles, '[]'::jsonb)::text,
                   lookup_filter_field, lookup_source_field, COALESCE(show_in_parent_form, FALSE),
                   COALESCE(allow_quick_create, FALSE)
            FROM sys_columns
            WHERE table_id = @tableId AND is_active = TRUE
            ORDER BY order_index ASC, created_at ASC
            """;

        var list = new List<SysColumnDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableId", tableId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysColumnDto
            {
                Id = r.GetString(0),
                TableId = r.GetString(1),
                ColumnCode = r.GetString(2),
                ColumnName = r.GetString(3),
                ExcelHeader = r.GetString(4),
                DataType = r.GetString(5),
                IsRequired = r.GetBoolean(6),
                IsIdentityKey = r.GetBoolean(7),
                ShowInTable = r.GetBoolean(8),
                ShowInForm = r.GetBoolean(9),
                Width = r.GetInt32(10),
                ValidationRegex = r.IsDBNull(11) ? null : r.GetString(11),
                RefCategoryCode = r.IsDBNull(12) ? null : r.GetString(12),
                RefAppId = r.IsDBNull(13) ? null : r.GetString(13),
                RefTableId = r.IsDBNull(14) ? null : r.GetString(14),
                RefValueField = r.IsDBNull(15) ? null : r.GetString(15),
                RefDisplayField = r.IsDBNull(16) ? null : r.GetString(16),
                Formula = r.IsDBNull(17) ? null : r.GetString(17),
                DefaultValue = r.IsDBNull(18) ? null : r.GetString(18),
                HiddenForRoles = JsonSerializer.Deserialize<List<string>>(r.GetString(19)) ?? new(),
                OrderIndex = r.GetInt32(20),
                IsActive = r.GetBoolean(21),
                ReadOnlyForRoles = JsonSerializer.Deserialize<List<string>>(r.GetString(22)) ?? new(),
                LookupFilterField = r.FieldCount > 23 && !r.IsDBNull(23) ? r.GetString(23) : null,
                LookupSourceField = r.FieldCount > 24 && !r.IsDBNull(24) ? r.GetString(24) : null,
                ShowInParentForm = r.FieldCount > 25 && !r.IsDBNull(25) && r.GetBoolean(25),
                AllowQuickCreate = r.FieldCount > 26 && !r.IsDBNull(26) && r.GetBoolean(26)
            });
        }
        return list;
    }

    public async Task<SysColumnDto> UpsertColumnAsync(UpsertColumnRequest req)
    {
        var tableRules = (await GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == req.TableId)?.FormRules ?? new();
        if (tableRules.Count > 0)
        {
            var proposed = await GetColumnsAsync(req.TableId);
            proposed.RemoveAll(c => c.Id == req.Id);
            proposed.Add(new SysColumnDto { Id = req.Id ?? "new", ColumnCode = req.ColumnCode.Trim().ToLowerInvariant().Replace(" ", "_"),
                DataType = req.DataType.Trim().ToUpperInvariant(), ShowInForm = req.ShowInForm, Formula = req.Formula,
                HiddenForRoles = req.HiddenForRoles ?? new() });
            FormRuleService.ValidateDefinition(tableRules, proposed);
        }
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var colCode = req.ColumnCode.Trim().ToLowerInvariant().Replace(" ", "_");
        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"col_{colCode}_{Guid.NewGuid().ToString("N")[..5]}"
            : req.Id.Trim();

        var sql = """
            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type,
                is_required, is_identity_key, show_in_table, show_in_form, width,
                validation_regex, ref_category_code, ref_app_id, ref_table_id, ref_value_field, ref_display_field,
                lookup_filter_field, lookup_source_field, show_in_parent_form, allow_quick_create,
                formula, default_value, hidden_for_roles, read_only_for_roles, order_index, updated_at)
            VALUES (@id, @tbl, @code, @name, @excel, @dt, @req, @key, @sit, @sif, @w, @rx, @ref, @ra, @rt, @rv, @rd, @lff, @lsf, @sipf, @aqc, @fm, @def, @hfr::jsonb, @ror::jsonb, @ord, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                column_code = EXCLUDED.column_code,
                column_name = EXCLUDED.column_name,
                excel_header = EXCLUDED.excel_header,
                data_type = EXCLUDED.data_type,
                is_required = EXCLUDED.is_required,
                is_identity_key = EXCLUDED.is_identity_key,
                show_in_table = EXCLUDED.show_in_table,
                show_in_form = EXCLUDED.show_in_form,
                width = EXCLUDED.width,
                validation_regex = EXCLUDED.validation_regex,
                ref_category_code = EXCLUDED.ref_category_code,
                ref_app_id = EXCLUDED.ref_app_id,
                ref_table_id = EXCLUDED.ref_table_id,
                ref_value_field = EXCLUDED.ref_value_field,
                ref_display_field = EXCLUDED.ref_display_field,
                lookup_filter_field = EXCLUDED.lookup_filter_field,
                lookup_source_field = EXCLUDED.lookup_source_field,
                show_in_parent_form = EXCLUDED.show_in_parent_form,
                allow_quick_create = EXCLUDED.allow_quick_create,
                formula = EXCLUDED.formula,
                default_value = EXCLUDED.default_value,
                hidden_for_roles = EXCLUDED.hidden_for_roles,
                read_only_for_roles = EXCLUDED.read_only_for_roles,
                order_index = EXCLUDED.order_index,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tbl", req.TableId);
        cmd.Parameters.AddWithValue("code", colCode);
        cmd.Parameters.AddWithValue("name", req.ColumnName.Trim());
        cmd.Parameters.AddWithValue("excel", string.IsNullOrWhiteSpace(req.ExcelHeader) ? req.ColumnName.Trim() : req.ExcelHeader.Trim());
        cmd.Parameters.AddWithValue("dt", req.DataType.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("req", req.IsRequired);
        cmd.Parameters.AddWithValue("key", req.IsIdentityKey);
        cmd.Parameters.AddWithValue("sit", req.ShowInTable);
        cmd.Parameters.AddWithValue("sif", req.ShowInForm);
        cmd.Parameters.AddWithValue("w", req.Width <= 0 ? 170 : req.Width);
        cmd.Parameters.AddWithValue("rx", string.IsNullOrWhiteSpace(req.ValidationRegex) ? DBNull.Value : req.ValidationRegex.Trim());
        cmd.Parameters.AddWithValue("ref", string.IsNullOrWhiteSpace(req.RefCategoryCode) ? DBNull.Value : req.RefCategoryCode.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("ra", string.IsNullOrWhiteSpace(req.RefAppId) ? DBNull.Value : req.RefAppId.Trim());
        cmd.Parameters.AddWithValue("rt", string.IsNullOrWhiteSpace(req.RefTableId) ? DBNull.Value : req.RefTableId.Trim());
        cmd.Parameters.AddWithValue("rv", string.IsNullOrWhiteSpace(req.RefValueField) ? DBNull.Value : req.RefValueField.Trim());
        cmd.Parameters.AddWithValue("rd", string.IsNullOrWhiteSpace(req.RefDisplayField) ? DBNull.Value : req.RefDisplayField.Trim());
        cmd.Parameters.AddWithValue("lff", string.IsNullOrWhiteSpace(req.LookupFilterField) ? DBNull.Value : req.LookupFilterField.Trim());
        cmd.Parameters.AddWithValue("lsf", string.IsNullOrWhiteSpace(req.LookupSourceField) ? DBNull.Value : req.LookupSourceField.Trim());
        cmd.Parameters.AddWithValue("sipf", req.ShowInParentForm);
        cmd.Parameters.AddWithValue("aqc", req.AllowQuickCreate);
        cmd.Parameters.AddWithValue("fm", string.IsNullOrWhiteSpace(req.Formula) ? DBNull.Value : req.Formula.Trim());
        cmd.Parameters.AddWithValue("def", string.IsNullOrWhiteSpace(req.DefaultValue) ? DBNull.Value : req.DefaultValue.Trim());
        cmd.Parameters.AddWithValue("hfr", JsonSerializer.Serialize(req.HiddenForRoles ?? new List<string>()));
        cmd.Parameters.AddWithValue("ror", JsonSerializer.Serialize(req.ReadOnlyForRoles ?? new List<string>()));
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        await cmd.ExecuteNonQueryAsync();
        await MarkAppDraftByTableAsync(conn, req.TableId);

        return (await GetColumnsAsync(req.TableId)).First(c => c.Id == id);
    }

    public async Task<bool> DeleteColumnAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using (var location = new NpgsqlCommand("SELECT table_id FROM sys_columns WHERE id=@id", conn))
        {
            location.Parameters.AddWithValue("id", id);
            var tableId = await location.ExecuteScalarAsync() as string;
            if (tableId != null)
            {
                var column = (await GetColumnsAsync(tableId)).FirstOrDefault(c => c.Id == id);
                var rules = (await GetTablesAsync(null, null)).FirstOrDefault(t => t.Id == tableId)?.FormRules ?? new();
                if (column != null && rules.Any(r => r.TargetField == column.ColumnCode || r.Conditions.Any(c => c.Field == column.ColumnCode)))
                    throw new FormRuleConfigurationException("Cột đang được dùng trong quy tắc biểu mẫu. Hãy sửa/xóa quy tắc trước.");
            }
        }
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_columns WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<SysCategoryDto>> GetCategoriesAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT c.category_code, c.category_name, c.app_id, c.description,
                   (SELECT COUNT(*) FROM sys_category_items i WHERE i.category_code = c.category_code AND i.is_active = TRUE) AS item_count
            FROM sys_categories c
            ORDER BY c.category_code ASC
            """;

        var list = new List<SysCategoryDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysCategoryDto
            {
                CategoryCode = r.GetString(0),
                CategoryName = r.GetString(1),
                AppId = r.IsDBNull(2) ? null : r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                ItemCount = Convert.ToInt32(r.GetInt64(4))
            });
        }
        return list;
    }

    public async Task<SysCategoryDto> UpsertCategoryAsync(UpsertCategoryRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var code = req.CategoryCode.Trim().ToUpperInvariant().Replace(" ", "_");
        var sql = """
            INSERT INTO sys_categories (category_code, category_name, app_id, description)
            VALUES (@c, @n, @app, @d)
            ON CONFLICT (category_code) DO UPDATE SET
                category_name = EXCLUDED.category_name,
                app_id = EXCLUDED.app_id,
                description = EXCLUDED.description
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("c", code);
        cmd.Parameters.AddWithValue("n", req.CategoryName.Trim());
        cmd.Parameters.AddWithValue("app", (object?)req.AppId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("d", (object?)req.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return (await GetCategoriesAsync()).First(x => x.CategoryCode == code);
    }

    public async Task<List<SysCategoryItemDto>> GetCategoryItemsAsync(string? categoryCode)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            SELECT id, category_code, item_code, item_name, color, order_index
            FROM sys_category_items
            WHERE (@code IS NULL OR category_code = @code) AND is_active = TRUE
            ORDER BY category_code ASC, order_index ASC, item_name ASC
            """;

        var list = new List<SysCategoryItemDto>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("code", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(categoryCode) ? DBNull.Value : categoryCode.Trim().ToUpperInvariant() });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysCategoryItemDto
            {
                Id = r.GetString(0),
                CategoryCode = r.GetString(1),
                ItemCode = r.GetString(2),
                ItemName = r.GetString(3),
                Color = r.IsDBNull(4) ? null : r.GetString(4),
                OrderIndex = r.GetInt32(5)
            });
        }
        return list;
    }

    private static string SlugifyCategoryItemCode(string input)
    {
        var normalized = input.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var ascii = Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        if (ascii.Length > 48) ascii = ascii[..48].Trim('_');
        return string.IsNullOrWhiteSpace(ascii) ? $"ITEM_{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}" : ascii;
    }

    public async Task<SysCategoryItemDto> UpsertCategoryItemAsync(UpsertCategoryItemRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var cat = req.CategoryCode.Trim().ToUpperInvariant();
        var itemName = req.ItemName.Trim();

        // Chống trùng lặp theo tên hiển thị (không phân biệt hoa/thường) khi thêm mới
        if (string.IsNullOrWhiteSpace(req.Id))
        {
            await using var dupCmd = new NpgsqlCommand("""
                SELECT id, category_code, item_code, item_name, color, order_index
                FROM sys_category_items
                WHERE category_code = @cat AND is_active = TRUE AND LOWER(TRIM(item_name)) = LOWER(@name)
                LIMIT 1
                """, conn);
            dupCmd.Parameters.AddWithValue("cat", cat);
            dupCmd.Parameters.AddWithValue("name", itemName);
            await using var dr = await dupCmd.ExecuteReaderAsync();
            if (await dr.ReadAsync())
            {
                return new SysCategoryItemDto
                {
                    Id = dr.GetString(0),
                    CategoryCode = dr.GetString(1),
                    ItemCode = dr.GetString(2),
                    ItemName = dr.GetString(3),
                    Color = dr.IsDBNull(4) ? null : dr.GetString(4),
                    OrderIndex = dr.GetInt32(5)
                };
            }
        }

        var itemCode = string.IsNullOrWhiteSpace(req.ItemCode)
            ? SlugifyCategoryItemCode(itemName)
            : req.ItemCode.Trim().ToUpperInvariant().Replace(" ", "_");
        var id = string.IsNullOrWhiteSpace(req.Id)
            ? $"ci_{cat.ToLowerInvariant()}_{itemCode.ToLowerInvariant()}"
            : req.Id.Trim();

        var sql = """
            INSERT INTO sys_category_items (id, category_code, item_code, item_name, color, order_index)
            VALUES (@id, @cat, @code, @name, @col, @ord)
            ON CONFLICT (id) DO UPDATE SET
                item_code = EXCLUDED.item_code,
                item_name = EXCLUDED.item_name,
                color = EXCLUDED.color,
                order_index = EXCLUDED.order_index
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("cat", cat);
        cmd.Parameters.AddWithValue("code", itemCode);
        cmd.Parameters.AddWithValue("name", itemName);
        cmd.Parameters.AddWithValue("col", (object?)req.Color ?? "#1a73e8");
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        await cmd.ExecuteNonQueryAsync();

        return (await GetCategoryItemsAsync(cat)).First(i => i.Id == id);
    }

    public async Task<bool> DeleteCategoryAsync(string categoryCode)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var code = categoryCode.Trim().ToUpperInvariant();
        await using var delItems = new NpgsqlCommand("DELETE FROM sys_category_items WHERE category_code = @code", conn);
        delItems.Parameters.AddWithValue("code", code);
        await delItems.ExecuteNonQueryAsync();

        await using var delCat = new NpgsqlCommand("DELETE FROM sys_categories WHERE category_code = @code", conn);
        delCat.Parameters.AddWithValue("code", code);
        return await delCat.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteCategoryItemAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_category_items WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id.Trim());
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<SysAppDto> CreateAppFromExcelAsync(CreateAppFromExcelRequest req)
    {
        var app = await UpsertAppAsync(new UpsertAppRequest
        {
            Code = req.AppCode,
            Name = req.AppName,
            Description = $"Ứng dụng tạo tự động từ Excel ({req.Sheets.Count} bảng)",
            Department = string.IsNullOrWhiteSpace(req.Department) ? "Toàn công ty" : req.Department!,
            Icon = string.IsNullOrWhiteSpace(req.Icon) ? "Storage" : req.Icon!,
            Color = string.IsNullOrWhiteSpace(req.Color) ? "#1a73e8" : req.Color!,
            BgColor = string.IsNullOrWhiteSpace(req.BgColor) ? "#e8f0fe" : req.BgColor!,
            Version = "1.0.0",
            OrderIndex = 99
        });

        var mod = await UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "DATA",
            Name = "Dữ liệu chính",
            Icon = "Layers",
            OrderIndex = 1,
            Description = "Phân hệ khởi tạo tự động từ Excel"
        });

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        int tblIdx = 1;
        foreach (var sheet in req.Sheets)
        {
            if (sheet.Rows.Count == 0) continue;
            var headers = sheet.Rows.SelectMany(r => r.Keys).Distinct().ToList();
            if (headers.Count == 0) continue;

            var tblSlug = Slugify(sheet.SheetName);
            if (string.IsNullOrEmpty(tblSlug)) tblSlug = $"sheet_{tblIdx}";
            var tbl = await UpsertTableAsync(new UpsertTableRequest
            {
                AppId = app.Id,
                ModuleId = mod.Id,
                TableName = $"{app.Code.ToLowerInvariant()}_{tblSlug}",
                DisplayName = sheet.SheetName,
                Description = $"Nhập tự động {sheet.Rows.Count} dòng từ sheet {sheet.SheetName}",
                PrimaryView = "table",
                OrderIndex = tblIdx++
            });

            var colMap = new Dictionary<string, string>();
            int colIdx = 1;
            foreach (var h in headers)
            {
                var code = Slugify(h);
                if (string.IsNullOrEmpty(code)) code = $"col_{colIdx}";
                while (colMap.ContainsValue(code)) code = $"{code}_{colIdx}";
                colMap[h] = code;

                var inferredType = InferColumnType(h, sheet.Rows.Select(r => r.TryGetValue(h, out var v) ? v : null));
                await UpsertColumnAsync(new UpsertColumnRequest
                {
                    TableId = tbl.Id,
                    ColumnCode = code,
                    ColumnName = h,
                    ExcelHeader = h,
                    DataType = inferredType,
                    IsRequired = colIdx == 1,
                    IsIdentityKey = colIdx == 1,
                    Width = inferredType == "TEXTAREA" ? 240 : 170,
                    OrderIndex = colIdx++
                });
            }

            var firstKeyCode = colMap.Values.FirstOrDefault();
            foreach (var row in sheet.Rows)
            {
                var normalized = new Dictionary<string, object?>();
                foreach (var kv in row)
                {
                    if (colMap.TryGetValue(kv.Key, out var cCode))
                    {
                        normalized[cCode] = UnwrapElement(kv.Value);
                    }
                }
                var recKey = firstKeyCode != null && normalized.TryGetValue(firstKeyCode, out var kvVal) ? kvVal?.ToString() : null;
                await using var ins = new NpgsqlCommand("""
                    INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status)
                    VALUES (@id, @app, @mod, @tbl, @key, @data::jsonb, 'ACTIVE')
                    """, conn);
                ins.Parameters.AddWithValue("id", $"rec_{Guid.NewGuid().ToString("N")[..12]}");
                ins.Parameters.AddWithValue("app", app.Id);
                ins.Parameters.AddWithValue("mod", mod.Id);
                ins.Parameters.AddWithValue("tbl", tbl.Id);
                ins.Parameters.AddWithValue("key", (object?)recKey ?? DBNull.Value);
                ins.Parameters.AddWithValue("data", JsonSerializer.Serialize(normalized));
                await ins.ExecuteNonQueryAsync();
            }
        }

        return (await GetAppByCodeOrIdAsync(app.Id))!;
    }

    public async Task<SysAppDto> CreateAppFromTemplateAsync(CreateAppFromTemplateRequest req)
    {
        var tpl = AppTemplateCatalog.Find(req.TemplateKey)
            ?? throw new ArgumentException($"Không tìm thấy App mẫu '{req.TemplateKey}'.");

        var code = string.IsNullOrWhiteSpace(req.CustomCode)
            ? tpl.Code
            : req.CustomCode!.Trim().ToUpperInvariant().Replace(" ", "_");
        if (await GetAppByCodeOrIdAsync(code) != null)
        {
            code = $"{code}_{DateTime.UtcNow:HHmmss}";
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // 1. Ensure shared/template categories exist without overwriting user-customized items
        foreach (var cat in tpl.Categories)
        {
            await using var catCmd = new NpgsqlCommand("""
                INSERT INTO sys_categories (category_code, category_name, description)
                VALUES (@c, @n, @d) ON CONFLICT (category_code) DO NOTHING
                """, conn);
            catCmd.Parameters.AddWithValue("c", cat.CategoryCode);
            catCmd.Parameters.AddWithValue("n", cat.CategoryName);
            catCmd.Parameters.AddWithValue("d", cat.Description);
            await catCmd.ExecuteNonQueryAsync();

            foreach (var item in cat.Items)
            {
                await using var itemCmd = new NpgsqlCommand("""
                    INSERT INTO sys_category_items (id, category_code, item_code, item_name, color, order_index)
                    VALUES (@id, @cat, @code, @name, @col, @ord) ON CONFLICT DO NOTHING
                    """, conn);
                itemCmd.Parameters.AddWithValue("id", $"ci_{cat.CategoryCode.ToLowerInvariant()}_{item.ItemCode.ToLowerInvariant()}");
                itemCmd.Parameters.AddWithValue("cat", cat.CategoryCode);
                itemCmd.Parameters.AddWithValue("code", item.ItemCode);
                itemCmd.Parameters.AddWithValue("name", item.ItemName);
                itemCmd.Parameters.AddWithValue("col", item.Color);
                itemCmd.Parameters.AddWithValue("ord", item.OrderIndex);
                await itemCmd.ExecuteNonQueryAsync();
            }
        }

        // 2. Create App
        var app = await UpsertAppAsync(new UpsertAppRequest
        {
            Code = code,
            Name = string.IsNullOrWhiteSpace(req.CustomName) ? tpl.Name : req.CustomName!.Trim(),
            Description = tpl.Description,
            Department = tpl.Department,
            Icon = tpl.Icon,
            Color = tpl.Color,
            BgColor = tpl.BgColor,
            Version = "1.0.0",
            OrderIndex = tpl.OrderIndex
        });

        // 3. Create Modules, Tables, and Columns
        var createdTables = new Dictionary<string, SysTableDto>(StringComparer.OrdinalIgnoreCase);
        var columnsByTable = new Dictionary<string, List<SysColumnDto>>(StringComparer.OrdinalIgnoreCase);

        for (int modIdx = 0; modIdx < tpl.Modules.Count; modIdx++)
        {
            var modSpec = tpl.Modules[modIdx];
            var mod = await UpsertModuleAsync(new UpsertModuleRequest
            {
                AppId = app.Id,
                Code = modSpec.Code,
                Name = modSpec.Name,
                Icon = modSpec.Icon,
                Description = modSpec.Description,
                OrderIndex = modIdx + 1
            });

            for (int tblIdx = 0; tblIdx < modSpec.Tables.Count; tblIdx++)
            {
                var tblSpec = modSpec.Tables[tblIdx];
                var tbl = await UpsertTableAsync(new UpsertTableRequest
                {
                    AppId = app.Id,
                    ModuleId = mod.Id,
                    TableName = tblSpec.TableName,
                    DisplayName = tblSpec.DisplayName,
                    Description = tblSpec.Description,
                    PrimaryView = tblSpec.PrimaryView,
                    ApprovalEnabled = tblSpec.ApprovalEnabled,
                    ApprovalField = tblSpec.ApprovalField,
                    PublicFormEnabled = tblSpec.PublicFormEnabled,
                    OrderIndex = tblIdx + 1
                });
                createdTables[tblSpec.TableName] = tbl;

                var savedCols = new List<SysColumnDto>();
                foreach (var colSpec in tblSpec.Columns)
                {
                    var refAppId = colSpec.RefAppId;
                    var refTableId = colSpec.RefTableId;
                    if (!string.IsNullOrWhiteSpace(colSpec.InternalRefTableName) &&
                        createdTables.TryGetValue(colSpec.InternalRefTableName, out var targetTbl))
                    {
                        refAppId = app.Id;
                        refTableId = targetTbl.Id;
                    }

                    var col = await UpsertColumnAsync(new UpsertColumnRequest
                    {
                        TableId = tbl.Id,
                        ColumnCode = colSpec.ColumnCode,
                        ColumnName = colSpec.ColumnName,
                        ExcelHeader = colSpec.ColumnName,
                        DataType = colSpec.DataType,
                        IsRequired = colSpec.IsRequired,
                        IsIdentityKey = colSpec.IsIdentityKey,
                        ShowInTable = colSpec.ShowInTable,
                        ShowInForm = colSpec.ShowInForm,
                        ShowInParentForm = colSpec.ShowInParentForm,
                        Width = colSpec.Width,
                        ValidationRegex = colSpec.ValidationRegex,
                        RefCategoryCode = colSpec.RefCategoryCode,
                        RefAppId = refAppId,
                        RefTableId = refTableId,
                        RefValueField = colSpec.RefValueField,
                        RefDisplayField = colSpec.RefDisplayField,
                        Formula = colSpec.Formula,
                        DefaultValue = colSpec.DefaultValue,
                        OrderIndex = colSpec.OrderIndex
                    });
                    savedCols.Add(col);
                }
                columnsByTable[tblSpec.TableName] = savedCols;
            }
        }

        // 4. Seed Dashboard Widgets
        foreach (var w in tpl.Widgets)
        {
            if (!createdTables.TryGetValue(w.TableName, out var wTbl)) continue;
            await using var wCmd = new NpgsqlCommand("""
                INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field,
                    filter_field, filter_value, col_span, color, order_index, is_visible)
                VALUES (@id, @app, @tbl, @title, @wt, @mf, @gf, @ff, @fv, @span, @col, @ord, TRUE)
                """, conn);
            wCmd.Parameters.AddWithValue("id", $"wdg_{Guid.NewGuid().ToString("N")[..8]}");
            wCmd.Parameters.AddWithValue("app", app.Id);
            wCmd.Parameters.AddWithValue("tbl", wTbl.Id);
            wCmd.Parameters.AddWithValue("title", w.Title);
            wCmd.Parameters.AddWithValue("wt", w.WidgetType);
            wCmd.Parameters.AddWithValue("mf", (object?)w.MetricField ?? DBNull.Value);
            wCmd.Parameters.AddWithValue("gf", (object?)w.GroupField ?? DBNull.Value);
            wCmd.Parameters.AddWithValue("ff", (object?)w.FilterField ?? DBNull.Value);
            wCmd.Parameters.AddWithValue("fv", (object?)w.FilterValue ?? DBNull.Value);
            wCmd.Parameters.AddWithValue("span", w.ColSpan);
            wCmd.Parameters.AddWithValue("col", w.Color);
            wCmd.Parameters.AddWithValue("ord", w.OrderIndex);
            await wCmd.ExecuteNonQueryAsync();
        }

        // 5. Seed evaluated sample records so the app is immediately ready to use
        if (req.IncludeSampleData && tpl.SampleRecords.Count > 0)
        {
            var compute = new DynamicComputeService();
            var catSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var neededCats = columnsByTable.Values
                .SelectMany(cs => cs)
                .Select(c => c.RefCategoryCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var catCode in neededCats)
            {
                var items = await GetCategoryItemsAsync(catCode);
                catSets[catCode!] = items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var group in tpl.SampleRecords.GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase))
            {
                if (!createdTables.TryGetValue(group.Key, out var tbl) ||
                    !columnsByTable.TryGetValue(group.Key, out var cols)) continue;

                var identityCol = cols.FirstOrDefault(c => c.IsIdentityKey);
                var rawBatch = group.Select(r => ($"rec_{Guid.NewGuid().ToString("N")[..12]}", new Dictionary<string, object?>(r.Data))).ToList();
                var computed = compute.EvaluateAndValidate(cols, catSets, rawBatch);

                foreach (var res in computed)
                {
                    var recKey = identityCol != null && res.EvaluatedData.TryGetValue(identityCol.ColumnCode, out var kv)
                        ? kv?.ToString()
                        : null;

                    var approvalStatus = "DRAFT";
                    string? approvedBy = null;
                    if (tbl.ApprovalEnabled && !string.IsNullOrWhiteSpace(tbl.ApprovalField) &&
                        res.EvaluatedData.TryGetValue(tbl.ApprovalField, out var appVal))
                    {
                        var valStr = appVal?.ToString()?.ToUpperInvariant();
                        if (valStr == "DA_DUYET") { approvalStatus = "APPROVED"; approvedBy = "admin"; }
                        else if (valStr == "TU_CHOI") { approvalStatus = "REJECTED"; approvedBy = "admin"; }
                        else if (valStr == "CHO_DUYET") { approvalStatus = "PENDING"; }
                    }

                    await using var ins = new NpgsqlCommand("""
                        INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status,
                            approval_status, approved_by, approved_at, validation_errors, created_by, updated_by)
                        VALUES (@id, @app, @mod, @tbl, @key, @data::jsonb, 'ACTIVE',
                            @appSt, @appBy, CASE WHEN @appBy IS NOT NULL THEN CURRENT_TIMESTAMP ELSE NULL END,
                            @errs::jsonb, 'admin', 'admin')
                        """, conn);
                    ins.Parameters.AddWithValue("id", res.Id);
                    ins.Parameters.AddWithValue("app", app.Id);
                    ins.Parameters.AddWithValue("mod", tbl.ModuleId);
                    ins.Parameters.AddWithValue("tbl", tbl.Id);
                    ins.Parameters.AddWithValue("key", (object?)recKey ?? DBNull.Value);
                    ins.Parameters.AddWithValue("data", JsonSerializer.Serialize(res.EvaluatedData));
                    ins.Parameters.AddWithValue("appSt", approvalStatus);
                    ins.Parameters.AddWithValue("appBy", (object?)approvedBy ?? DBNull.Value);
                    ins.Parameters.AddWithValue("errs", JsonSerializer.Serialize(res.Errors));
                    await ins.ExecuteNonQueryAsync();
                }
            }
        }

        return (await GetAppByCodeOrIdAsync(app.Id))!;
    }

    private static string Slugify(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                var mapped = ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch;
                sb.Append(char.IsLetterOrDigit(mapped) ? char.ToLowerInvariant(mapped) : '_');
            }
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    }

    private static string InferColumnType(string header, IEnumerable<object?> values)
    {
        var h = header.ToLowerInvariant();
        if (h.Contains("email")) return "EMAIL";
        if (h.Contains("điện thoại") || h.Contains("sdt") || h.Contains("phone")) return "TEL";
        if (h.Contains("ngày") || h.Contains("date")) return "DATE";
        if (h.Contains("tiền") || h.Contains("giá") || h.Contains("lương") || h.Contains("chi phí")) return "CURRENCY";

        var nonNull = values.Select(UnwrapElement).Where(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())).Take(30).ToList();
        if (nonNull.Count > 0)
        {
            if (nonNull.All(v => v is bool || v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || v?.ToString()?.Equals("false", StringComparison.OrdinalIgnoreCase) == true))
                return "BOOLEAN";
            if (nonNull.All(v => decimal.TryParse(v?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)))
                return "NUMBER";
        }
        return "STRING";
    }

    private static object? UnwrapElement(object? val)
    {
        if (val is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => el.ToString()
            };
        }
        return val;
    }

    private static SysAppDto ReadApp(NpgsqlDataReader r)
    {
        return new SysAppDto
        {
            Id = r.GetString(0),
            Code = r.GetString(1),
            Name = r.GetString(2),
            Description = r.IsDBNull(3) ? null : r.GetString(3),
            Department = r.IsDBNull(4) ? "Toàn công ty" : r.GetString(4),
            Icon = r.IsDBNull(5) ? "Apps" : r.GetString(5),
            Color = r.IsDBNull(6) ? "#1a73e8" : r.GetString(6),
            BgColor = r.IsDBNull(7) ? "#e8f0fe" : r.GetString(7),
            Version = r.IsDBNull(8) ? "1.0.0" : r.GetString(8),
            OrderIndex = r.GetInt32(9),
            IsActive = r.GetBoolean(10),
            Settings = JsonDocument.Parse(r.GetString(11)).RootElement,
            CreatedAt = r.GetDateTime(12),
            UpdatedAt = r.GetDateTime(13),
            ModuleCount = Convert.ToInt32(r.GetInt64(14)),
            TableCount = Convert.ToInt32(r.GetInt64(15)),
            RecordCount = Convert.ToInt32(r.GetInt64(16)),
            PublishedVersion = r.FieldCount > 17 && !r.IsDBNull(17) ? r.GetInt32(17) : 1,
            PublishStatus = r.FieldCount > 18 && !r.IsDBNull(18) ? r.GetString(18) : "PUBLISHED",
            PublishedAt = r.FieldCount > 19 && !r.IsDBNull(19) ? r.GetDateTime(19) : null,
            PublishedBy = r.FieldCount > 20 && !r.IsDBNull(20) ? r.GetString(20) : null
        };
    }

    private static async Task MarkAppDraftAsync(NpgsqlConnection conn, string appId)
    {
        await using var cmd = new NpgsqlCommand("UPDATE sys_apps SET publish_status = 'DRAFT', updated_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", appId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MarkAppDraftByTableAsync(NpgsqlConnection conn, string tableId)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_apps SET publish_status = 'DRAFT', updated_at = CURRENT_TIMESTAMP
            WHERE id = (SELECT app_id FROM sys_tables WHERE id = @tbl LIMIT 1)
            """, conn);
        cmd.Parameters.AddWithValue("tbl", tableId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<AppValidationResultDto> ValidateAppForPublishAsync(string appId)
    {
        var tree = await GetAppRuntimeTreeAsync(appId);
        var result = new AppValidationResultDto();
        if (tree == null)
        {
            result.Errors.Add("Không tìm thấy ứng dụng.");
            result.Summary = "Không thể phát hành.";
            return result;
        }

        if (tree.Modules.Count == 0)
            result.Errors.Add("Ứng dụng chưa có phân hệ nào.");

        var allTables = await GetTablesAsync(null, null);
        var activeTableIds = allTables.Where(t => t.IsActive).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var appTableCount = 0;

        foreach (var mod in tree.Modules)
        {
            if (mod.Tables.Count == 0)
                result.Warnings.Add($"Phân hệ '{mod.Module.Name}' chưa có bảng dữ liệu nào.");

            foreach (var tbl in mod.Tables)
            {
                appTableCount++;
                if (tbl.Columns.Count == 0)
                    result.Errors.Add($"Bảng '{tbl.Table.DisplayName}' chưa có cột dữ liệu nào.");

                if (tbl.Table.FormRules.Count > 0)
                {
                    try { FormRuleService.ValidateDefinition(tbl.Table.FormRules, tbl.Columns); }
                    catch (FormRuleConfigurationException ex) { result.Errors.Add($"Bảng '{tbl.Table.DisplayName}': {ex.Message}"); }
                }

                foreach (var col in tbl.Columns.Where(c => !string.IsNullOrWhiteSpace(c.RefTableId)))
                {
                    if (!activeTableIds.Contains(col.RefTableId!))
                        result.Errors.Add($"Cột '{col.ColumnName}' ({tbl.Table.DisplayName}) trỏ tới bảng tham chiếu '{col.RefTableId}' không tồn tại.");
                }

                if (tbl.Table.ApprovalEnabled && tbl.Table.ApproverRoles.Count == 0)
                    result.Warnings.Add($"Bảng '{tbl.Table.DisplayName}' bật phê duyệt nhưng chưa cấu hình vai trò duyệt mặc định.");
            }
        }

        if (appTableCount == 0)
            result.Errors.Add("Ứng dụng chưa có bảng dữ liệu hoạt động nào.");

        result.CanPublish = result.Errors.Count == 0;
        result.Summary = result.CanPublish
            ? (result.Warnings.Count == 0 ? "Sẵn sàng phát hành." : $"Hợp lệ để phát hành ({result.Warnings.Count} cảnh báo).")
            : $"Phát hiện {result.Errors.Count} lỗi cần khắc phục trước khi phát hành.";
        return result;
    }

    public async Task<AppReleaseDto> PublishAppAsync(string appId, string actor, string? notes = null)
    {
        var validation = await ValidateAppForPublishAsync(appId);
        if (!validation.CanPublish)
            throw new InvalidOperationException(string.Join(" ", validation.Errors));

        var blueprint = await ExportAppBlueprintAsync(appId)
            ?? throw new InvalidOperationException("Không thể tạo snapshot ứng dụng.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int nextVersion;
        await using (var verCmd = new NpgsqlCommand("""
            UPDATE sys_apps
            SET published_version = COALESCE(published_version, 0) + 1,
                publish_status = 'PUBLISHED',
                published_at = CURRENT_TIMESTAMP,
                published_by = @actor,
                version = CONCAT((COALESCE(published_version, 0) + 1)::text, '.0.0'),
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id OR UPPER(code) = UPPER(@id)
            RETURNING id, published_version
            """, conn, tx))
        {
            verCmd.Parameters.AddWithValue("id", appId);
            verCmd.Parameters.AddWithValue("actor", actor);
            await using var r = await verCmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                throw new InvalidOperationException("Không tìm thấy ứng dụng để phát hành.");
            appId = r.GetString(0);
            nextVersion = r.GetInt32(1);
        }

        var releaseId = $"rel_{Guid.NewGuid().ToString("N")[..12]}";
        var snapshotJson = JsonSerializer.Serialize(blueprint);
        await using (var relCmd = new NpgsqlCommand("""
            INSERT INTO sys_app_releases (id, app_id, version_number, notes, snapshot, published_by)
            VALUES (@id, @appId, @ver, @notes, @snap::jsonb, @actor)
            """, conn, tx))
        {
            relCmd.Parameters.AddWithValue("id", releaseId);
            relCmd.Parameters.AddWithValue("appId", appId);
            relCmd.Parameters.AddWithValue("ver", nextVersion);
            relCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
            relCmd.Parameters.AddWithValue("snap", snapshotJson);
            relCmd.Parameters.AddWithValue("actor", actor);
            await relCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new AppReleaseDto
        {
            Id = releaseId,
            AppId = appId,
            VersionNumber = nextVersion,
            Notes = notes,
            PublishedBy = actor,
            PublishedAt = DateTime.UtcNow,
            TableCount = blueprint.Modules.Sum(m => m.Tables.Count),
            ColumnCount = blueprint.Modules.SelectMany(m => m.Tables).Sum(t => t.Columns.Count)
        };
    }

    public async Task<List<AppReleaseDto>> GetAppReleasesAsync(string appId)
    {
        var app = await GetAppByCodeOrIdAsync(appId);
        if (app == null) return new();

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, app_id, version_number, notes, published_by, published_at, snapshot::text
            FROM sys_app_releases
            WHERE app_id = @appId
            ORDER BY version_number DESC
            """, conn);
        cmd.Parameters.AddWithValue("appId", app.Id);

        var list = new List<AppReleaseDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var snap = JsonSerializer.Deserialize<AppBlueprintDto>(r.GetString(6),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            list.Add(new AppReleaseDto
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                VersionNumber = r.GetInt32(2),
                Notes = r.IsDBNull(3) ? null : r.GetString(3),
                PublishedBy = r.GetString(4),
                PublishedAt = r.GetDateTime(5),
                TableCount = snap?.Modules.Sum(m => m.Tables.Count) ?? 0,
                ColumnCount = snap?.Modules.SelectMany(m => m.Tables).Sum(t => t.Columns.Count) ?? 0
            });
        }
        return list;
    }

    public async Task<AppReleaseDto?> RollbackAppReleaseAsync(string appId, int versionNumber, string actor)
    {
        var app = await GetAppByCodeOrIdAsync(appId);
        if (app == null) return null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT snapshot::text FROM sys_app_releases
            WHERE app_id = @appId AND version_number = @ver
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("appId", app.Id);
        cmd.Parameters.AddWithValue("ver", versionNumber);
        var snapJson = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrWhiteSpace(snapJson)) return null;

        var blueprint = JsonSerializer.Deserialize<AppBlueprintDto>(snapJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (blueprint == null) return null;

        // Restore metadata safely without deleting existing app_records
        var snapTableIds = blueprint.Modules.SelectMany(m => m.Tables).Select(t => t.Table.Id).ToHashSet(StringComparer.Ordinal);
        await using (var deact = new NpgsqlCommand("UPDATE sys_tables SET is_active = FALSE WHERE app_id = @appId", conn))
        {
            deact.Parameters.AddWithValue("appId", app.Id);
            await deact.ExecuteNonQueryAsync();
        }

        foreach (var modNode in blueprint.Modules)
        {
            await UpsertModuleAsync(new UpsertModuleRequest
            {
                Id = modNode.Module.Id,
                AppId = app.Id,
                Code = modNode.Module.Code,
                Name = modNode.Module.Name,
                Icon = modNode.Module.Icon,
                OrderIndex = modNode.Module.OrderIndex,
                Description = modNode.Module.Description
            });

            foreach (var tblNode in modNode.Tables)
            {
                await UpsertTableAsync(new UpsertTableRequest
                {
                    Id = tblNode.Table.Id,
                    AppId = app.Id,
                    ModuleId = modNode.Module.Id,
                    TableName = tblNode.Table.TableName,
                    DisplayName = tblNode.Table.DisplayName,
                    Description = tblNode.Table.Description,
                    PrimaryView = tblNode.Table.PrimaryView,
                    RowScope = tblNode.Table.RowScope,
                    OrderIndex = tblNode.Table.OrderIndex,
                    ApprovalEnabled = tblNode.Table.ApprovalEnabled,
                    ApprovalField = tblNode.Table.ApprovalField,
                    ApproverRoles = tblNode.Table.ApproverRoles,
                    PublicFormEnabled = tblNode.Table.PublicFormEnabled,
                    FormRules = tblNode.Table.FormRules
                });

                foreach (var col in tblNode.Columns)
                {
                    await UpsertColumnAsync(new UpsertColumnRequest
                    {
                        Id = col.Id,
                        TableId = tblNode.Table.Id,
                        ColumnCode = col.ColumnCode,
                        ColumnName = col.ColumnName,
                        ExcelHeader = col.ExcelHeader,
                        DataType = col.DataType,
                        IsRequired = col.IsRequired,
                        IsIdentityKey = col.IsIdentityKey,
                        DefaultValue = col.DefaultValue,
                        Formula = col.Formula,
                        Width = col.Width,
                        RefAppId = col.RefAppId,
                        RefTableId = col.RefTableId,
                        RefValueField = col.RefValueField,
                        RefDisplayField = col.RefDisplayField,
                        RefCategoryCode = col.RefCategoryCode,
                        LookupFilterField = col.LookupFilterField,
                        LookupSourceField = col.LookupSourceField,
                        HiddenForRoles = col.HiddenForRoles,
                        ReadOnlyForRoles = col.ReadOnlyForRoles,
                        OrderIndex = col.OrderIndex
                    });
                }
            }
        }

        return await PublishAppAsync(app.Id, actor, $"Khôi phục về phiên bản v{versionNumber}");
    }

    // ─── Print Templates ───────────────────────────────────────────────────────

    public async Task<List<SysPrintTemplateDto>> GetPrintTemplatesAsync(string tableId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, table_id, name, paper_size, orientation, html_template, css_styles,
                   is_default, watermark_draft, require_approved, created_by, created_at, updated_at
            FROM sys_print_templates
            WHERE table_id = @tbl
            ORDER BY is_default DESC, updated_at DESC
            """, conn);
        cmd.Parameters.AddWithValue("tbl", tableId);

        var list = new List<SysPrintTemplateDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysPrintTemplateDto
            {
                Id = r.GetString(0),
                TableId = r.GetString(1),
                Name = r.GetString(2),
                PaperSize = r.GetString(3),
                Orientation = r.GetString(4),
                HtmlTemplate = r.GetString(5),
                CssStyles = r.IsDBNull(6) ? null : r.GetString(6),
                IsDefault = r.GetBoolean(7),
                WatermarkDraft = r.GetBoolean(8),
                RequireApproved = r.GetBoolean(9),
                CreatedBy = r.IsDBNull(10) ? "admin" : r.GetString(10),
                CreatedAt = r.GetDateTime(11),
                UpdatedAt = r.GetDateTime(12)
            });
        }
        return list;
    }

    public async Task<SysPrintTemplateDto> UpsertPrintTemplateAsync(UpsertPrintTemplateRequest req, string actor = "admin")
    {
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"prt_{Guid.NewGuid().ToString("N")[..10]}" : req.Id.Trim();
        var paper = string.IsNullOrWhiteSpace(req.PaperSize) ? "A4" : req.PaperSize.Trim().ToUpperInvariant();
        var orient = string.Equals(req.Orientation, "landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        if (req.IsDefault)
        {
            await using var clearCmd = new NpgsqlCommand(
                "UPDATE sys_print_templates SET is_default = FALSE WHERE table_id = @tbl AND id <> @id", conn, tx);
            clearCmd.Parameters.AddWithValue("tbl", req.TableId);
            clearCmd.Parameters.AddWithValue("id", id);
            await clearCmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO sys_print_templates (id, table_id, name, paper_size, orientation, html_template, css_styles, is_default, watermark_draft, require_approved, created_by, updated_at)
            VALUES (@id, @tbl, @name, @ps, @or, @html, @css, @def, @wm, @req, @actor, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                paper_size = EXCLUDED.paper_size,
                orientation = EXCLUDED.orientation,
                html_template = EXCLUDED.html_template,
                css_styles = EXCLUDED.css_styles,
                is_default = EXCLUDED.is_default,
                watermark_draft = EXCLUDED.watermark_draft,
                require_approved = EXCLUDED.require_approved,
                updated_at = CURRENT_TIMESTAMP
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tbl", req.TableId);
            cmd.Parameters.AddWithValue("name", req.Name.Trim());
            cmd.Parameters.AddWithValue("ps", paper);
            cmd.Parameters.AddWithValue("or", orient);
            cmd.Parameters.AddWithValue("html", req.HtmlTemplate);
            cmd.Parameters.AddWithValue("css", (object?)req.CssStyles ?? DBNull.Value);
            cmd.Parameters.AddWithValue("def", req.IsDefault);
            cmd.Parameters.AddWithValue("wm", req.WatermarkDraft);
            cmd.Parameters.AddWithValue("req", req.RequireApproved);
            cmd.Parameters.AddWithValue("actor", actor);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return (await GetPrintTemplatesAsync(req.TableId)).First(t => t.Id == id);
    }

    public async Task<bool> DeletePrintTemplateAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_print_templates WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<AppBlueprintDto?> ExportAppBlueprintAsync(string codeOrId)
    {
        var tree = await GetAppRuntimeTreeAsync(codeOrId);
        if (tree == null) return null;

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var widgets = new List<AppWidgetDto>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id, app_id, table_id, title, widget_type, metric_field, group_field,
                   filter_field, filter_value, col_span, color, order_index, is_visible
            FROM app_widgets WHERE app_id = @appId ORDER BY order_index
            """, conn))
        {
            cmd.Parameters.AddWithValue("appId", tree.App.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                widgets.Add(new AppWidgetDto
                {
                    Id = r.GetString(0),
                    AppId = r.GetString(1),
                    TableId = r.GetString(2),
                    Title = r.GetString(3),
                    WidgetType = r.GetString(4),
                    MetricField = r.IsDBNull(5) ? null : r.GetString(5),
                    GroupField = r.IsDBNull(6) ? null : r.GetString(6),
                    FilterField = r.IsDBNull(7) ? null : r.GetString(7),
                    FilterValue = r.IsDBNull(8) ? null : r.GetString(8),
                    ColSpan = r.GetInt32(9),
                    Color = r.GetString(10),
                    OrderIndex = r.GetInt32(11),
                    IsVisible = r.GetBoolean(12)
                });
            }
        }

        return new AppBlueprintDto
        {
            BlueprintVersion = "1.0",
            ExportedAt = DateTime.UtcNow,
            App = tree.App,
            Modules = tree.Modules,
            Widgets = widgets
        };
    }

    public async Task<SysAppDto> ImportAppBlueprintAsync(AppBlueprintDto bp, string? overrideCode = null, string? overrideName = null)
    {
        var code = string.IsNullOrWhiteSpace(overrideCode) ? bp.App.Code : overrideCode.Trim().ToUpperInvariant();
        if (await GetAppByCodeOrIdAsync(code) != null)
        {
            code = $"{code}_{DateTime.UtcNow:HHmmss}";
        }

        var newApp = await UpsertAppAsync(new UpsertAppRequest
        {
            Code = code,
            Name = string.IsNullOrWhiteSpace(overrideName) ? $"{bp.App.Name} (Clone)" : overrideName.Trim(),
            Description = bp.App.Description,
            Department = bp.App.Department,
            Icon = bp.App.Icon,
            Color = bp.App.Color,
            BgColor = bp.App.BgColor,
            Version = bp.App.Version,
            OrderIndex = bp.App.OrderIndex
        });

        var tableIdMap = new Dictionary<string, string>();
        foreach (var modNode in bp.Modules)
        {
            var newMod = await UpsertModuleAsync(new UpsertModuleRequest
            {
                AppId = newApp.Id,
                Code = modNode.Module.Code,
                Name = modNode.Module.Name,
                Icon = modNode.Module.Icon,
                OrderIndex = modNode.Module.OrderIndex,
                Description = modNode.Module.Description
            });

            foreach (var tblNode in modNode.Tables)
            {
                var newTbl = await UpsertTableAsync(new UpsertTableRequest
                {
                    AppId = newApp.Id,
                    ModuleId = newMod.Id,
                    TableName = $"{code.ToLowerInvariant()}_{tblNode.Table.TableName}",
                    DisplayName = tblNode.Table.DisplayName,
                    Description = tblNode.Table.Description,
                    PrimaryView = tblNode.Table.PrimaryView,
                    RowScope = tblNode.Table.RowScope,
                    ApprovalEnabled = tblNode.Table.ApprovalEnabled,
                    ApprovalField = tblNode.Table.ApprovalField,
                    ApproverRoles = tblNode.Table.ApproverRoles,
                    PublicFormEnabled = tblNode.Table.PublicFormEnabled,
                    OrderIndex = tblNode.Table.OrderIndex
                });
                tableIdMap[tblNode.Table.Id] = newTbl.Id;

                foreach (var col in tblNode.Columns)
                {
                    await UpsertColumnAsync(new UpsertColumnRequest
                    {
                        TableId = newTbl.Id,
                        ColumnCode = col.ColumnCode,
                        ColumnName = col.ColumnName,
                        ExcelHeader = col.ExcelHeader,
                        DataType = col.DataType,
                        IsRequired = col.IsRequired,
                        IsIdentityKey = col.IsIdentityKey,
                        ShowInTable = col.ShowInTable,
                        ShowInForm = col.ShowInForm,
                        Width = col.Width,
                        ValidationRegex = col.ValidationRegex,
                        RefCategoryCode = col.RefCategoryCode,
                        RefAppId = col.RefAppId,
                        RefTableId = col.RefTableId,
                        RefValueField = col.RefValueField,
                        RefDisplayField = col.RefDisplayField,
                        Formula = col.Formula,
                        DefaultValue = col.DefaultValue,
                        HiddenForRoles = col.HiddenForRoles,
                        ReadOnlyForRoles = col.ReadOnlyForRoles,
                        OrderIndex = col.OrderIndex
                    });
                }
                await SetFormRulesAsync(newTbl.Id, tblNode.Table.FormRules);
            }
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        foreach (var w in bp.Widgets)
        {
            if (!tableIdMap.TryGetValue(w.TableId, out var newTblId)) continue;
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field,
                    filter_field, filter_value, col_span, color, order_index, is_visible)
                VALUES (@id, @app, @tbl, @title, @wt, @mf, @gf, @ff, @fv, @span, @col, @ord, @vis)
                """, conn);
            cmd.Parameters.AddWithValue("id", $"wdg_{Guid.NewGuid().ToString("N")[..8]}");
            cmd.Parameters.AddWithValue("app", newApp.Id);
            cmd.Parameters.AddWithValue("tbl", newTblId);
            cmd.Parameters.AddWithValue("title", w.Title);
            cmd.Parameters.AddWithValue("wt", w.WidgetType);
            cmd.Parameters.AddWithValue("mf", (object?)w.MetricField ?? DBNull.Value);
            cmd.Parameters.AddWithValue("gf", (object?)w.GroupField ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ff", (object?)w.FilterField ?? DBNull.Value);
            cmd.Parameters.AddWithValue("fv", (object?)w.FilterValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("span", w.ColSpan);
            cmd.Parameters.AddWithValue("col", w.Color);
            cmd.Parameters.AddWithValue("ord", w.OrderIndex);
            cmd.Parameters.AddWithValue("vis", w.IsVisible);
            await cmd.ExecuteNonQueryAsync();
        }

        return (await GetAppByCodeOrIdAsync(newApp.Id))!;
    }

    public async Task<BlueprintUpgradeResultDto?> UpgradeAppFromBlueprintAsync(string codeOrId, AppBlueprintDto bp)
    {
        var existingApp = await GetAppByCodeOrIdAsync(codeOrId);
        if (existingApp == null) return null;

        var existingMods = await GetModulesAsync(existingApp.Id);
        var existingTbls = await GetTablesAsync(existingApp.Id, null);

        var result = new BlueprintUpgradeResultDto { AppId = existingApp.Id, AppCode = existingApp.Code };
        var tableIdMap = new Dictionary<string, string>();

        foreach (var modNode in bp.Modules)
        {
            var matchedMod = existingMods.FirstOrDefault(m =>
                m.Code.Equals(modNode.Module.Code, StringComparison.OrdinalIgnoreCase));
            var savedMod = await UpsertModuleAsync(new UpsertModuleRequest
            {
                Id = matchedMod?.Id,
                AppId = existingApp.Id,
                Code = modNode.Module.Code,
                Name = modNode.Module.Name,
                Icon = modNode.Module.Icon,
                OrderIndex = modNode.Module.OrderIndex,
                Description = modNode.Module.Description
            });
            result.ModulesUpserted++;

            foreach (var tblNode in modNode.Tables)
            {
                var matchedTbl = existingTbls.FirstOrDefault(t =>
                    t.TableName.Equals(tblNode.Table.TableName, StringComparison.OrdinalIgnoreCase));
                var savedTbl = await UpsertTableAsync(new UpsertTableRequest
                {
                    Id = matchedTbl?.Id,
                    AppId = existingApp.Id,
                    ModuleId = savedMod.Id,
                    TableName = matchedTbl?.TableName ?? tblNode.Table.TableName,
                    DisplayName = tblNode.Table.DisplayName,
                    Description = tblNode.Table.Description,
                    PrimaryView = tblNode.Table.PrimaryView,
                    RowScope = tblNode.Table.RowScope,
                    ApprovalEnabled = tblNode.Table.ApprovalEnabled,
                    ApprovalField = tblNode.Table.ApprovalField,
                    ApproverRoles = tblNode.Table.ApproverRoles,
                    PublicFormEnabled = tblNode.Table.PublicFormEnabled,
                    OrderIndex = tblNode.Table.OrderIndex
                });
                tableIdMap[tblNode.Table.Id] = savedTbl.Id;
                result.TablesUpserted++;

                var existingCols = await GetColumnsAsync(savedTbl.Id);
                foreach (var col in tblNode.Columns)
                {
                    var matchedCol = existingCols.FirstOrDefault(c =>
                        c.ColumnCode.Equals(col.ColumnCode, StringComparison.OrdinalIgnoreCase));
                    await UpsertColumnAsync(new UpsertColumnRequest
                    {
                        Id = matchedCol?.Id,
                        TableId = savedTbl.Id,
                        ColumnCode = col.ColumnCode,
                        ColumnName = col.ColumnName,
                        ExcelHeader = col.ExcelHeader,
                        DataType = col.DataType,
                        IsRequired = col.IsRequired,
                        IsIdentityKey = col.IsIdentityKey,
                        ShowInTable = col.ShowInTable,
                        ShowInForm = col.ShowInForm,
                        Width = col.Width,
                        ValidationRegex = col.ValidationRegex,
                        RefCategoryCode = col.RefCategoryCode,
                        RefAppId = col.RefAppId,
                        RefTableId = col.RefTableId,
                        RefValueField = col.RefValueField,
                        RefDisplayField = col.RefDisplayField,
                        Formula = col.Formula,
                        DefaultValue = col.DefaultValue,
                        HiddenForRoles = col.HiddenForRoles,
                        ReadOnlyForRoles = col.ReadOnlyForRoles,
                        OrderIndex = col.OrderIndex
                    });
                    result.ColumnsUpserted++;
                }
                await SetFormRulesAsync(savedTbl.Id, tblNode.Table.FormRules);
            }
        }

        return result;
    }

    public sealed record AiAppSpec(
        string Code,
        string Name,
        string Department,
        string Icon,
        string Color,
        string BgColor,
        List<(string TableName, string DisplayName, bool Approval, bool PublicForm, List<UpsertColumnRequest> Cols)> Tables);

    public static bool TryParseAiGeneratedAppSpec(
        string rawJson,
        AiGenerateAppRequest req,
        out AiAppSpec? spec)
    {
        spec = null;
        if (string.IsNullOrWhiteSpace(rawJson)) return false;

        try
        {
            var clean = AiService.StripMarkdownJsonFence(rawJson);
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var codeRaw = !string.IsNullOrWhiteSpace(req.AppCode)
                ? req.AppCode
                : (root.TryGetProperty("code", out var cp) ? cp.GetString() : null);
            var code = SanitizeCode(codeRaw, $"AI{DateTime.UtcNow:mmss}");
            var name = root.TryGetProperty("name", out var np) && !string.IsNullOrWhiteSpace(np.GetString())
                ? np.GetString()!.Trim()
                : "Ứng dụng (AI Generated)";
            var dept = !string.IsNullOrWhiteSpace(req.Department)
                ? req.Department!.Trim()
                : (root.TryGetProperty("department", out var dp) && !string.IsNullOrWhiteSpace(dp.GetString())
                    ? dp.GetString()!.Trim()
                    : "Toàn công ty");
            var icon = root.TryGetProperty("icon", out var ip) && !string.IsNullOrWhiteSpace(ip.GetString())
                ? ip.GetString()!.Trim()
                : "AutoAwesome";
            var color = root.TryGetProperty("color", out var colP) && !string.IsNullOrWhiteSpace(colP.GetString())
                ? colP.GetString()!.Trim()
                : "#1a73e8";
            var bg = root.TryGetProperty("bgColor", out var bgP) && !string.IsNullOrWhiteSpace(bgP.GetString())
                ? bgP.GetString()!.Trim()
                : "#e8f0fe";

            if (!root.TryGetProperty("tables", out var tablesEl) || tablesEl.ValueKind != JsonValueKind.Array)
                return false;

            var tables = new List<(string TableName, string DisplayName, bool Approval, bool PublicForm, List<UpsertColumnRequest> Cols)>();
            int tIdx = 0;
            foreach (var tEl in tablesEl.EnumerateArray())
            {
                tIdx++;
                if (tEl.ValueKind != JsonValueKind.Object) continue;
                var tblNameRaw = tEl.TryGetProperty("tableName", out var tnP) ? tnP.GetString() : null;
                var dispName = tEl.TryGetProperty("displayName", out var dnP) ? dnP.GetString() : null;
                if (string.IsNullOrWhiteSpace(dispName)) dispName = $"Bảng dữ liệu {tIdx}";
                var tblName = SanitizeIdentifier(tblNameRaw, $"{code.ToLowerInvariant()}_table_{tIdx}");
                var approval = tEl.TryGetProperty("approvalEnabled", out var apP) && apP.ValueKind == JsonValueKind.True;
                var publicForm = tEl.TryGetProperty("publicFormEnabled", out var pfP) && pfP.ValueKind == JsonValueKind.True;

                var cols = new List<UpsertColumnRequest>();
                if (tEl.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
                {
                    int cIdx = 0;
                    var seenCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var cEl in colsEl.EnumerateArray())
                    {
                        if (cEl.ValueKind != JsonValueKind.Object) continue;
                        var colName = cEl.TryGetProperty("columnName", out var cnP) ? cnP.GetString()?.Trim() : null;
                        var colCodeRaw = cEl.TryGetProperty("columnCode", out var ccP) ? ccP.GetString() : null;
                        if (string.IsNullOrWhiteSpace(colName)) continue;
                        cIdx++;
                        var colCode = SanitizeIdentifier(colCodeRaw, $"col_{cIdx}");
                        if (!seenCols.Add(colCode)) continue;

                        var dtRaw = cEl.TryGetProperty("dataType", out var dtP) ? dtP.GetString()?.Trim().ToUpperInvariant() : "STRING";
                        var dt = !string.IsNullOrWhiteSpace(dtRaw) && AllowedAiColumnTypes.Contains(dtRaw) ? dtRaw : "STRING";
                        var isReq = cEl.TryGetProperty("isRequired", out var reqP) && reqP.ValueKind == JsonValueKind.True;
                        var isKey = cEl.TryGetProperty("isIdentityKey", out var keyP) && keyP.ValueKind == JsonValueKind.True;
                        var defVal = cEl.TryGetProperty("defaultValue", out var dvP) && dvP.ValueKind == JsonValueKind.String
                            ? dvP.GetString()
                            : null;
                        var formula = cEl.TryGetProperty("formula", out var fP) && fP.ValueKind == JsonValueKind.String
                            ? fP.GetString()
                            : null;

                        cols.Add(new UpsertColumnRequest
                        {
                            ColumnCode = colCode,
                            ColumnName = colName,
                            DataType = dt,
                            IsRequired = isReq,
                            IsIdentityKey = isKey,
                            DefaultValue = defVal,
                            Formula = formula,
                            OrderIndex = cIdx
                        });
                    }
                }

                if (cols.Count > 0)
                    tables.Add((tblName, dispName.Trim(), approval, publicForm, cols));
            }

            if (tables.Count == 0) return false;
            spec = new AiAppSpec(code, name, dept, icon, color, bg, tables);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeCode(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var chars = raw.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(s) ? fallback : (s.Length > 24 ? s[..24] : s);
    }

    private static string SanitizeIdentifier(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var chars = raw.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = string.Join("_", new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(s) ? fallback : (s.Length > 48 ? s[..48] : s);
    }

    public async Task<SysAppDto> GenerateAppFromPromptAsync(AiGenerateAppRequest req, string? username = null)
    {
        var prompt = req.Prompt.Trim();
        var lower = prompt.ToLowerInvariant();

        string code, name, dept, icon, color, bg;
        List<(string TableName, string DisplayName, bool Approval, bool PublicForm, List<UpsertColumnRequest> Cols)> specs;
        string? aiEngineLabel = null;

        if (_ai != null)
        {
            var aiStatus = await _ai.GetAiStatusAsync();
            if (aiStatus.IsConfigured)
            {
                const string sysPrompt = """
                    Bạn là kiến trúc sư Low-Code của nền tảng SmartApps Enterprise.
                    Hãy thiết kế một ứng dụng quản lý từ mô tả tiếng Việt của người dùng và CHỈ trả về JSON hợp lệ theo schema sau:
                    {
                      "code": "MÃ_APP_VIẾT_HOA_KHÔNG_DẤU",
                      "name": "Tên ứng dụng tiếng Việt",
                      "department": "Phòng ban phụ trách",
                      "icon": "AutoAwesome | LocalShipping | Description | Inventory2 | People | Payments | SupportAgent | Build | Assignment",
                      "color": "#1a73e8",
                      "bgColor": "#e8f0fe",
                      "tables": [
                        {
                          "tableName": "ten_bang_snake_case",
                          "displayName": "Tên hiển thị của bảng",
                          "approvalEnabled": true,
                          "publicFormEnabled": false,
                          "columns": [
                            {
                              "columnCode": "ma_cot_snake_case",
                              "columnName": "Tên cột tiếng Việt",
                              "dataType": "AUTO_NUMBER | STRING | NUMBER | CURRENCY | DATE | BOOLEAN | EMAIL | TEL | TEXTAREA | SUB_TABLE",
                              "isRequired": true,
                              "isIdentityKey": false,
                              "defaultValue": "VD: HD-{YYYY}- cho AUTO_NUMBER hoặc col1,col2,col3 cho SUB_TABLE",
                              "formula": "VD: SUM(chi_tiet.thanh_tien) hoặc so_luong * don_gia hoặc DATEDIF(tu_ngay, den_ngay)"
                            }
                          ]
                        }
                      ]
                    }
                    Thiết kế 1-3 bảng thực tế, mỗi bảng 4-8 cột rõ ràng, có mã tự sinh AUTO_NUMBER làm identity key nếu là phiếu/hồ sơ.
                    """;

                var userPrompt = $"Mô tả nghiệp vụ: {prompt}\nMã App ưu tiên (nếu có): {req.AppCode ?? "tự đề xuất"}\nPhòng ban (nếu có): {req.Department ?? "tự đề xuất"}";
                var (ok, content, provName, modName, err) = await _ai.CompleteAsync(
                    "AI_APP_COPILOT",
                    sysPrompt,
                    userPrompt,
                    username,
                    jsonMode: true);

                if (ok && TryParseAiGeneratedAppSpec(content, req, out var aiSpec) && aiSpec != null)
                {
                    code = aiSpec.Code;
                    name = aiSpec.Name;
                    dept = aiSpec.Department;
                    icon = aiSpec.Icon;
                    color = aiSpec.Color;
                    bg = aiSpec.BgColor;
                    specs = aiSpec.Tables;
                    aiEngineLabel = $"{provName} ({modName})";
                    goto CreateAppFromSpecs;
                }

                if (!aiStatus.FallbackToRuleEngine)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(err)
                            ? "AI API không trả về cấu trúc ứng dụng hợp lệ."
                            : err);
                }
            }
        }

        if (lower.Contains("xe") || lower.Contains("fleet") || lower.Contains("vận tải") || lower.Contains("bảo trì"))
        {
            code = string.IsNullOrWhiteSpace(req.AppCode) ? "FLEET" : req.AppCode.Trim().ToUpperInvariant();
            name = "Quản lý Đội xe & Bảo trì (AI)";
            dept = req.Department ?? "Khối Vận hành & Hậu cần";
            icon = "LocalShipping";
            color = "#0284c7";
            bg = "#e0f2fe";
            specs =
            [
                ("fleet_phuong_tien", "Danh sách Phương tiện", false, false, [
                    new() { ColumnCode = "bien_so", ColumnName = "Biển số xe", DataType = "STRING", IsRequired = true, IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "hang_xe", ColumnName = "Hãng / Dòng xe", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "so_km", ColumnName = "Số KM hiện tại", DataType = "NUMBER", OrderIndex = 3 },
                    new() { ColumnCode = "ngay_dang_kiem", ColumnName = "Hạn đăng kiểm", DataType = "DATE", OrderIndex = 4 },
                    new() { ColumnCode = "tai_xe", ColumnName = "Tài xế phụ trách", DataType = "STRING", OrderIndex = 5 },
                ]),
                ("fleet_bao_tri", "Lịch & Chi phí Bảo dưỡng", true, false, [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã phiếu bảo trì", DataType = "AUTO_NUMBER", DefaultValue = "BT-{YYYY}-", IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "bien_so", ColumnName = "Biển số xe", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "ngay_bao_tri", ColumnName = "Ngày thực hiện", DataType = "DATE", IsRequired = true, OrderIndex = 3 },
                    new() { ColumnCode = "hang_muc_chi_tiet", ColumnName = "Chi tiết phụ tùng", DataType = "SUB_TABLE", DefaultValue = "hang_muc,so_luong,thanh_tien", OrderIndex = 4 },
                    new() { ColumnCode = "tong_chi_phi", ColumnName = "Tổng chi phí", DataType = "CURRENCY", Formula = "SUM(hang_muc_chi_tiet.thanh_tien)", OrderIndex = 5 },
                ])
            ];
        }
        else if (lower.Contains("hợp đồng") || lower.Contains("contract") || lower.Contains("khách hàng"))
        {
            code = string.IsNullOrWhiteSpace(req.AppCode) ? "CONTRACT" : req.AppCode.Trim().ToUpperInvariant();
            name = "Quản lý Hợp đồng & Đối tác (AI)";
            dept = req.Department ?? "Khối Pháp chế & Kinh doanh";
            icon = "Description";
            color = "#7b1fa2";
            bg = "#f3e8ff";
            specs =
            [
                ("contract_hop_dong", "Hợp đồng Kinh tế", true, false, [
                    new() { ColumnCode = "so_hd", ColumnName = "Số Hợp đồng", DataType = "AUTO_NUMBER", DefaultValue = "HD-{YYYY}-", IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "ten_khach_hang", ColumnName = "Đối tác / Khách hàng", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "ngay_ky", ColumnName = "Ngày ký", DataType = "DATE", IsRequired = true, OrderIndex = 3 },
                    new() { ColumnCode = "ngay_het_han", ColumnName = "Ngày hết hạn", DataType = "DATE", OrderIndex = 4 },
                    new() { ColumnCode = "gia_tri_hd", ColumnName = "Giá trị hợp đồng", DataType = "CURRENCY", IsRequired = true, OrderIndex = 5 },
                    new() { ColumnCode = "thoi_han_ngay", ColumnName = "Số ngày hiệu lực", DataType = "NUMBER", Formula = "DATEDIF(ngay_ky, ngay_het_han)", OrderIndex = 6 },
                ])
            ];
        }
        else if (lower.Contains("kho") || lower.Contains("vật tư") || lower.Contains("inventory") || lower.Contains("mua"))
        {
            code = string.IsNullOrWhiteSpace(req.AppCode) ? "WMS" : req.AppCode.Trim().ToUpperInvariant();
            name = "Quản lý Kho & Nhập xuất Vật tư (AI)";
            dept = req.Department ?? "Khối Cung ứng & Kho vận";
            icon = "Inventory2";
            color = "#1e8e3e";
            bg = "#e6f4ea";
            specs =
            [
                ("wms_hang_hoa", "Danh mục Hàng hóa Tồn kho", false, false, [
                    new() { ColumnCode = "ma_sku", ColumnName = "Mã SKU", DataType = "STRING", IsRequired = true, IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "ten_hang", ColumnName = "Tên hàng hóa", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "ton_kho", ColumnName = "Tồn kho", DataType = "NUMBER", OrderIndex = 3 },
                    new() { ColumnCode = "don_gia", ColumnName = "Đơn giá", DataType = "CURRENCY", OrderIndex = 4 },
                    new() { ColumnCode = "gia_tri_ton", ColumnName = "Giá trị tồn", DataType = "CURRENCY", Formula = "ton_kho * don_gia", OrderIndex = 5 },
                ]),
                ("wms_phieu_nhap", "Phiếu Nhập / Đề xuất Mua", true, true, [
                    new() { ColumnCode = "ma_phieu", ColumnName = "Mã phiếu", DataType = "AUTO_NUMBER", DefaultValue = "PN-{YYYY}-", IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "nha_cung_cap", ColumnName = "Nhà cung cấp", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "ngay_nhap", ColumnName = "Ngày lập", DataType = "DATE", IsRequired = true, OrderIndex = 3 },
                    new() { ColumnCode = "chi_tiet_hang", ColumnName = "Danh sách vật tư", DataType = "SUB_TABLE", DefaultValue = "ten_hang,so_luong,thanh_tien", OrderIndex = 4 },
                    new() { ColumnCode = "tong_tien", ColumnName = "Tổng giá trị", DataType = "CURRENCY", Formula = "SUM(chi_tiet_hang.thanh_tien)", OrderIndex = 5 },
                ])
            ];
        }
        else
        {
            var shortTitle = prompt.Length > 36 ? prompt[..36] + "..." : prompt;
            code = string.IsNullOrWhiteSpace(req.AppCode) ? $"AI{DateTime.UtcNow:mmss}" : req.AppCode.Trim().ToUpperInvariant();
            name = $"{shortTitle} (AI Generated)";
            dept = req.Department ?? "Toàn công ty";
            icon = "AutoAwesome";
            color = "#1a73e8";
            bg = "#e8f0fe";
            specs =
            [
                ("ai_ho_so_chinh", "Dữ liệu Nghiệp vụ Chính", true, true, [
                    new() { ColumnCode = "ma_ho_so", ColumnName = "Mã hồ sơ", DataType = "AUTO_NUMBER", DefaultValue = $"{code}-{{YYYY}}-", IsIdentityKey = true, OrderIndex = 1 },
                    new() { ColumnCode = "tieu_de", ColumnName = "Tiêu đề / Tên đối tượng", DataType = "STRING", IsRequired = true, OrderIndex = 2 },
                    new() { ColumnCode = "nguoi_phu_trach", ColumnName = "Người phụ trách", DataType = "STRING", OrderIndex = 3 },
                    new() { ColumnCode = "ngay_thuc_hien", ColumnName = "Ngày thực hiện", DataType = "DATE", OrderIndex = 4 },
                    new() { ColumnCode = "chi_tiet", ColumnName = "Bảng chi tiết hạng mục", DataType = "SUB_TABLE", DefaultValue = "hang_muc,so_luong,don_gia", OrderIndex = 5 },
                    new() { ColumnCode = "gia_tri_du_kien", ColumnName = "Giá trị dự kiến", DataType = "CURRENCY", OrderIndex = 6 },
                    new() { ColumnCode = "ghi_chu", ColumnName = "Ghi chú", DataType = "TEXTAREA", OrderIndex = 7 },
                ])
            ];
        }

        if (await GetAppByCodeOrIdAsync(code) != null)
        {
            code = $"{code}_{DateTime.UtcNow:HHmmss}";
        }

        CreateAppFromSpecs:
        if (await GetAppByCodeOrIdAsync(code) != null)
        {
            code = $"{code}_{DateTime.UtcNow:HHmmss}";
        }

        var app = await UpsertAppAsync(new UpsertAppRequest
        {
            Code = code,
            Name = name,
            Description = aiEngineLabel != null
                ? $"Sinh tự động bởi {aiEngineLabel}: \"{prompt}\""
                : $"Sinh tự động từ yêu cầu AI (Mẫu hệ thống): \"{prompt}\"",
            Department = dept,
            Icon = icon,
            Color = color,
            BgColor = bg,
            Version = "1.0.0",
            OrderIndex = 10
        });

        var mod = await UpsertModuleAsync(new UpsertModuleRequest
        {
            AppId = app.Id,
            Code = "MAIN",
            Name = "Nghiệp vụ Chính",
            Icon = icon,
            OrderIndex = 1,
            Description = "Phân hệ được thiết kế tự động bởi AI Copilot"
        });

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var firstTableId = string.Empty;
        for (int i = 0; i < specs.Count; i++)
        {
            var sp = specs[i];
            var tbl = await UpsertTableAsync(new UpsertTableRequest
            {
                AppId = app.Id,
                ModuleId = mod.Id,
                TableName = sp.TableName,
                DisplayName = sp.DisplayName,
                ApprovalEnabled = sp.Approval,
                PublicFormEnabled = sp.PublicForm,
                OrderIndex = i + 1
            });
            if (i == 0) firstTableId = tbl.Id;

            foreach (var col in sp.Cols)
            {
                col.TableId = tbl.Id;
                await UpsertColumnAsync(col);
            }

            // Auto-create KPI_COUNT + LINE_CHART widgets for each generated table
            await using var wCmd = new NpgsqlCommand("""
                INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, col_span, color, order_index, is_visible)
                VALUES (@id1, @app, @tbl, @t1, 'KPI_COUNT', 1, @col, @o1, TRUE),
                       (@id2, @app, @tbl, @t2, 'LINE_CHART', 2, @col, @o2, TRUE)
                """, conn);
            wCmd.Parameters.AddWithValue("id1", $"wdg_{Guid.NewGuid().ToString("N")[..8]}");
            wCmd.Parameters.AddWithValue("id2", $"wdg_{Guid.NewGuid().ToString("N")[..8]}");
            wCmd.Parameters.AddWithValue("app", app.Id);
            wCmd.Parameters.AddWithValue("tbl", tbl.Id);
            wCmd.Parameters.AddWithValue("t1", $"Tổng {sp.DisplayName}");
            wCmd.Parameters.AddWithValue("t2", $"Xu hướng {sp.DisplayName}");
            wCmd.Parameters.AddWithValue("col", color);
            wCmd.Parameters.AddWithValue("o1", i * 2 + 1);
            wCmd.Parameters.AddWithValue("o2", i * 2 + 2);
            await wCmd.ExecuteNonQueryAsync();
        }

        return (await GetAppByCodeOrIdAsync(app.Id))!;
    }
}
