using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class AppUpgradeService(IConfiguration config, MetadataService metadata)
{
    private readonly string _connStr = config.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing Postgres connection string");

    private static readonly JsonSerializerOptions CamelJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<string, (string Label, string DataType)> CommaColumnHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hang_muc"] = ("Hạng mục", "STRING"),
        ["ten_hang"] = ("Tên hàng hóa", "STRING"),
        ["ten_ts"] = ("Tên tài sản", "STRING"),
        ["ma_sku"] = ("Mã SKU", "STRING"),
        ["serial"] = ("Số Serial", "STRING"),
        ["tinh_trang"] = ("Tình trạng", "STRING"),
        ["ghi_chu"] = ("Ghi chú", "STRING"),
        ["so_luong"] = ("Số lượng", "NUMBER"),
        ["sl"] = ("SL", "NUMBER"),
        ["don_gia"] = ("Đơn giá", "CURRENCY"),
        ["gia"] = ("Đơn giá", "CURRENCY"),
        ["thanh_tien"] = ("Thành tiền", "CURRENCY"),
        ["so_tien"] = ("Số tiền", "CURRENCY")
    };

    public static async Task EnsureCoreAppsV2Async(string connStr)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connStr })
            .Build();
        var meta = new MetadataService(cfg);
        var svc = new AppUpgradeService(cfg, meta);
        await svc.UpgradeCoreAppsIfNeededAsync("system");
    }

    public async Task UpgradeCoreAppsIfNeededAsync(string actor = "system")
    {
        foreach (var core in AppTemplateCatalog.GetCoreAppV2Specs())
        {
            var existing = await metadata.GetAppByCodeOrIdAsync(core.AppId)
                ?? await metadata.GetAppByCodeOrIdAsync(core.Code);
            if (existing == null) continue;
            await UpgradeAppToV2Async(existing.Id, actor);
        }
    }

    public async Task<List<AppUpgradeCandidateDto>> PreviewUpgradeAsync()
    {
        var apps = await metadata.GetAppsAsync();
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var results = new List<AppUpgradeCandidateDto>(apps.Count);
        foreach (var app in apps.Where(a => a.IsActive))
        {
            results.Add(await AnalyzeAppAsync(conn, app));
        }
        return results;
    }

    public async Task<List<AppUpgradeExecutionResultDto>> UpgradeAllAppsToV2Async(IReadOnlyList<string>? appIds, string actor = "admin")
    {
        var apps = await metadata.GetAppsAsync();
        var targets = appIds is { Count: > 0 }
            ? apps.Where(a => a.IsActive && (appIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase) || appIds.Contains(a.Code, StringComparer.OrdinalIgnoreCase))).ToList()
            : apps.Where(a => a.IsActive).ToList();

        var results = new List<AppUpgradeExecutionResultDto>(targets.Count);
        foreach (var app in targets)
        {
            results.Add(await UpgradeAppToV2Async(app.Id, actor));
        }
        return results;
    }

    public async Task<AppUpgradeExecutionResultDto> UpgradeAppToV2Async(string codeOrId, string actor = "admin")
    {
        var app = await metadata.GetAppByCodeOrIdAsync(codeOrId)
            ?? throw new KeyNotFoundException($"Không tìm thấy ứng dụng '{codeOrId}'.");

        var tree = await metadata.GetAppRuntimeTreeAsync(app.Id)
            ?? throw new InvalidOperationException($"Không đọc được cấu trúc ứng dụng '{app.Code}'.");

        var (coreSpec, tplSpec) = ResolveSpec(app.Code);
        var result = new AppUpgradeExecutionResultDto
        {
            AppId = app.Id,
            AppCode = app.Code,
            AppName = app.Name,
            PreviousVersion = app.Version,
            NewVersion = app.Version,
            MatchedTemplateCode = coreSpec?.Code ?? tplSpec?.Code
        };

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        if (tplSpec != null)
            await EnsureCategoriesAsync(conn, tplSpec.Categories);
        if (coreSpec != null && coreSpec.Categories.Count > 0)
            await EnsureCategoriesAsync(conn, coreSpec.Categories);

        var tablesByName = tree.Modules
            .SelectMany(m => m.Tables)
            .ToDictionary(t => t.Table.TableName, t => t.Table, StringComparer.OrdinalIgnoreCase);

        var modulesByCode = tree.Modules
            .ToDictionary(m => m.Module.Code, m => m.Module, StringComparer.OrdinalIgnoreCase);

        // 1. Apply matched template or core modules/tables/columns/rules
        if (tplSpec != null)
        {
            await ApplyModuleSpecsAsync(conn, app.Id, tplSpec.Modules, modulesByCode, tablesByName, result);
        }
        else if (coreSpec != null)
        {
            if (coreSpec.ExtraModules.Count > 0)
                await ApplyModuleSpecsAsync(conn, app.Id, coreSpec.ExtraModules, modulesByCode, tablesByName, result);

            foreach (var (tblName, extraCols) in coreSpec.ExtraColumnsByTable)
            {
                if (!tablesByName.TryGetValue(tblName, out var tbl)) continue;
                var existingCols = await metadata.GetColumnsAsync(tbl.Id);
                await EnsureColumnsAsync(conn, app.Id, tbl, extraCols, existingCols, tablesByName, result);
            }

            foreach (var (tblName, rules) in coreSpec.FormRulesByTable)
            {
                if (!tablesByName.TryGetValue(tblName, out var tbl)) continue;
                result.FormRulesAttached += await MergeFormRulesAsync(tbl, rules);
            }

            foreach (var (colKey, jsonSchema) in coreSpec.SubTableSchemaUpgrades)
            {
                var parts = colKey.Split('.', 2);
                if (parts.Length != 2 || !tablesByName.TryGetValue(parts[0], out var tbl)) continue;
                result.ColumnsUpgraded += await UpgradeSubTableColumnAsync(conn, tbl.Id, parts[1], jsonSchema);
            }
        }

        // 2. Upgrade any remaining legacy comma-separated SUB_TABLE columns non-destructively across all tables
        foreach (var tbl in tablesByName.Values)
        {
            var cols = await metadata.GetColumnsAsync(tbl.Id);
            foreach (var subCol in cols.Where(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)))
            {
                var def = subCol.DefaultValue?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(def) && !def.StartsWith('['))
                {
                    var typedJson = ConvertCommaSubTableToTypedJson(def);
                    result.ColumnsUpgraded += await UpgradeSubTableColumnAsync(conn, tbl.Id, subCol.ColumnCode, typedJson);
                }
            }
        }

        // 3. Apply v2.0 ecosystem artifacts (Workflows, Automations, PrintTemplates, SavedViews, Widgets, Plugins)
        if (tplSpec != null)
        {
            var counts = await ApplyV2ArtifactsAsync(
                metadata, conn, app.Id, tablesByName,
                tplSpec.Workflows, tplSpec.Automations, tplSpec.PrintTemplates,
                tplSpec.SavedViews, tplSpec.Widgets, tplSpec.PluginBindings, actor);
            CopyArtifactCounts(counts, result);
        }
        else if (coreSpec != null)
        {
            var counts = await ApplyV2ArtifactsAsync(
                metadata, conn, app.Id, tablesByName,
                coreSpec.Workflows, coreSpec.Automations, coreSpec.PrintTemplates,
                coreSpec.SavedViews, coreSpec.ExtraWidgets, coreSpec.PluginBindings, actor);
            CopyArtifactCounts(counts, result);
        }
        else
        {
            var counts = await EnsureBaselineCustomV2ArtifactsAsync(conn, app, tablesByName, actor);
            CopyArtifactCounts(counts, result);
        }

        // 4. Bump version / create v2 release snapshot idempotently when needed
        var releaseCount = await CountReleasesAsync(conn, app.Id);
        var changed = result.ModulesAdded + result.TablesAdded + result.ColumnsUpgraded + result.FormRulesAttached
            + result.WorkflowsAdded + result.AutomationsAdded + result.PrintTemplatesAdded
            + result.SavedViewsAdded + result.WidgetsAdded + result.PluginBindingsAdded > 0;
        var needsVersionBump = ParseMajorVersion(app.Version) < 2 || releaseCount == 0 || changed;

        if (needsVersionBump)
        {
            var validation = await metadata.ValidateAppForPublishAsync(app.Id);
            if (validation.CanPublish)
            {
                var rel = await metadata.PublishAppAsync(app.Id, actor, "Nâng cấp ứng dụng lên phiên bản 2.0.0 đầy đủ tính năng");
                result.PublishedReleaseVersion = rel.VersionNumber;
                var refreshed = await metadata.GetAppByCodeOrIdAsync(app.Id);
                result.NewVersion = refreshed?.Version ?? "2.0.0";
            }
            else
            {
                var targetVer = ParseMajorVersion(app.Version) < 2 ? "2.0.0" : app.Version;
                await using var bumpCmd = new NpgsqlCommand(
                    "UPDATE sys_apps SET version = @v, updated_at = CURRENT_TIMESTAMP WHERE id = @id", conn);
                bumpCmd.Parameters.AddWithValue("v", targetVer);
                bumpCmd.Parameters.AddWithValue("id", app.Id);
                await bumpCmd.ExecuteNonQueryAsync();
                result.NewVersion = targetVer;
            }
        }
        else
        {
            result.NewVersion = app.Version;
        }

        return result;
    }

    public static async Task EnsureCategoriesAsync(NpgsqlConnection conn, IReadOnlyList<TemplateCategorySpec> categories)
    {
        foreach (var cat in categories)
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
    }

    public static async Task<AppUpgradeExecutionResultDto> ApplyV2ArtifactsAsync(
        MetadataService metadata,
        NpgsqlConnection conn,
        string appId,
        IReadOnlyDictionary<string, SysTableDto> tablesByName,
        IReadOnlyList<TemplateWorkflowSpec> workflows,
        IReadOnlyList<TemplateAutomationSpec> automations,
        IReadOnlyList<TemplatePrintTemplateSpec> printTemplates,
        IReadOnlyList<TemplateSavedViewSpec> savedViews,
        IReadOnlyList<TemplateWidgetSpec> widgets,
        IReadOnlyList<TemplatePluginBindingSpec> pluginBindings,
        string actor)
    {
        var counts = new AppUpgradeExecutionResultDto { AppId = appId };

        // Widgets (skip duplicates by (table_id, lower(title)))
        var existingWidgets = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(title) FROM app_widgets WHERE app_id = @app", appId);
        foreach (var w in widgets)
        {
            if (!tablesByName.TryGetValue(w.TableName, out var wTbl)) continue;
            if (!existingWidgets.Add((wTbl.Id, w.Title.Trim().ToLowerInvariant()))) continue;
            await using var wCmd = new NpgsqlCommand("""
                INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field,
                    filter_field, filter_value, col_span, color, order_index, is_visible)
                VALUES (@id, @app, @tbl, @title, @wt, @mf, @gf, @ff, @fv, @span, @col, @ord, TRUE)
                """, conn);
            wCmd.Parameters.AddWithValue("id", $"wdg_{Guid.NewGuid().ToString("N")[..8]}");
            wCmd.Parameters.AddWithValue("app", appId);
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
            counts.WidgetsAdded++;
        }

        // Workflows (skip duplicates by (table_id, lower(name)))
        var existingWfs = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(name) FROM app_workflows WHERE app_id = @app", appId);
        foreach (var wf in workflows)
        {
            if (!tablesByName.TryGetValue(wf.TableName, out var wfTbl)) continue;
            if (!existingWfs.Add((wfTbl.Id, wf.Name.Trim().ToLowerInvariant()))) continue;
            await using var wfCmd = new NpgsqlCommand("""
                INSERT INTO app_workflows (id, name, description, app_id, table_id, trigger_type, steps, is_active, updated_at)
                VALUES (@id, @n, @d, @app, @tbl, @tt, @steps::jsonb, TRUE, CURRENT_TIMESTAMP)
                """, conn);
            wfCmd.Parameters.AddWithValue("id", $"wf_{Guid.NewGuid().ToString("N")[..10]}");
            wfCmd.Parameters.AddWithValue("n", wf.Name.Trim());
            wfCmd.Parameters.AddWithValue("d", wf.Description);
            wfCmd.Parameters.AddWithValue("app", appId);
            wfCmd.Parameters.AddWithValue("tbl", wfTbl.Id);
            wfCmd.Parameters.AddWithValue("tt", wf.TriggerType);
            wfCmd.Parameters.AddWithValue("steps", JsonSerializer.Serialize(wf.Steps, CamelJsonOpts));
            await wfCmd.ExecuteNonQueryAsync();
            counts.WorkflowsAdded++;
        }

        // Automations (skip duplicates by (source_table_id, lower(name)))
        var existingAutos = await LoadStringPairsAsync(conn,
            "SELECT source_table_id, LOWER(name) FROM app_automations WHERE source_app_id = @app", appId);
        foreach (var auto in automations)
        {
            if (!tablesByName.TryGetValue(auto.TriggerTableName, out var srcTbl)) continue;
            if (!existingAutos.Add((srcTbl.Id, auto.Name.Trim().ToLowerInvariant()))) continue;

            string? targetAppId = null;
            string? targetModId = null;
            string? targetTblId = null;
            if (!string.IsNullOrWhiteSpace(auto.TargetTableName) && tablesByName.TryGetValue(auto.TargetTableName, out var tgtTbl))
            {
                targetAppId = tgtTbl.AppId;
                targetModId = tgtTbl.ModuleId;
                targetTblId = tgtTbl.Id;
            }

            await using var aCmd = new NpgsqlCommand("""
                INSERT INTO app_automations (id, name, description, source_app_id, source_table_id, trigger_event,
                    condition_field, condition_operator, condition_value,
                    target_app_id, target_module_id, target_table_id, action_type,
                    match_source_field, match_target_field,
                    notify_recipient, notify_template, field_mappings, is_active)
                VALUES (@id, @n, @d, @sa, @st, @ev, @cf, @co, @cv, @ta, @tm, @tt, @act, @msf, @mtf, @nr, @nt, @fm::jsonb, TRUE)
                """, conn);
            aCmd.Parameters.AddWithValue("id", $"auto_{Guid.NewGuid().ToString("N")[..10]}");
            aCmd.Parameters.AddWithValue("n", auto.Name.Trim());
            aCmd.Parameters.AddWithValue("d", auto.Description);
            aCmd.Parameters.AddWithValue("sa", appId);
            aCmd.Parameters.AddWithValue("st", srcTbl.Id);
            aCmd.Parameters.AddWithValue("ev", auto.TriggerEvent);
            aCmd.Parameters.AddWithValue("cf", (object?)auto.ConditionField ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("co", auto.ConditionOperator ?? "EQUALS");
            aCmd.Parameters.AddWithValue("cv", (object?)auto.ConditionValue ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("ta", (object?)targetAppId ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("tm", (object?)targetModId ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("tt", (object?)targetTblId ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("act", auto.ActionType);
            aCmd.Parameters.AddWithValue("msf", (object?)auto.MatchSourceField ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("mtf", (object?)auto.MatchTargetField ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("nr", (object?)auto.NotifyRecipient ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("nt", (object?)auto.NotifyTemplate ?? DBNull.Value);
            aCmd.Parameters.AddWithValue("fm", JsonSerializer.Serialize(auto.FieldMappings));
            await aCmd.ExecuteNonQueryAsync();
            counts.AutomationsAdded++;
        }

        // Print templates (skip duplicates by (table_id, lower(name)); preserve any existing custom default)
        foreach (var pt in printTemplates)
        {
            if (!tablesByName.TryGetValue(pt.TableName, out var ptTbl)) continue;
            var existing = await metadata.GetPrintTemplatesAsync(ptTbl.Id);
            if (existing.Any(e => e.Name.Equals(pt.Name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
            var makeDefault = pt.IsDefault && !existing.Any(e => e.IsDefault);
            await metadata.UpsertPrintTemplateAsync(new UpsertPrintTemplateRequest
            {
                TableId = ptTbl.Id,
                Name = pt.Name,
                PaperSize = pt.PaperSize,
                Orientation = pt.Orientation,
                HtmlTemplate = pt.HtmlTemplate,
                CssStyles = pt.CssStyles,
                IsDefault = makeDefault,
                WatermarkDraft = pt.WatermarkDraft,
                RequireApproved = pt.RequireApproved
            }, actor);
            counts.PrintTemplatesAdded++;
        }

        // Saved Views (skip duplicates by (table_id, lower(name)))
        var existingViews = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(name) FROM sys_saved_views WHERE app_id = @app", appId);
        foreach (var sv in savedViews)
        {
            if (!tablesByName.TryGetValue(sv.TableName, out var svTbl)) continue;
            if (!existingViews.Add((svTbl.Id, sv.Name.Trim().ToLowerInvariant()))) continue;
            await using var svCmd = new NpgsqlCommand("""
                INSERT INTO sys_saved_views (id, app_id, table_id, name, filters, sort_field, sort_dir, is_shared, created_by)
                VALUES (@id, @app, @tbl, @name, @flt::jsonb, @sf, @sd, @sh, @cb)
                """, conn);
            svCmd.Parameters.AddWithValue("id", $"view_{Guid.NewGuid().ToString("N")[..10]}");
            svCmd.Parameters.AddWithValue("app", appId);
            svCmd.Parameters.AddWithValue("tbl", svTbl.Id);
            svCmd.Parameters.AddWithValue("name", sv.Name.Trim());
            svCmd.Parameters.AddWithValue("flt", string.IsNullOrWhiteSpace(sv.FiltersJson) ? "{}" : sv.FiltersJson);
            svCmd.Parameters.AddWithValue("sf", (object?)sv.SortField ?? DBNull.Value);
            svCmd.Parameters.AddWithValue("sd", string.Equals(sv.SortDir, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC");
            svCmd.Parameters.AddWithValue("sh", sv.IsShared);
            svCmd.Parameters.AddWithValue("cb", actor);
            await svCmd.ExecuteNonQueryAsync();
            counts.SavedViewsAdded++;
        }

        // Plugin Bindings (ensure built-in catalog plugin row exists disabled-by-default so bindings surface cleanly)
        var existingBindings = await LoadStringSetAsync(conn,
            "SELECT LOWER(plugin_code) FROM sys_app_plugin_bindings WHERE app_id = @app", appId);
        var catalog = IntegrationService.GetPluginCatalog();
        foreach (var pb in pluginBindings)
        {
            if (!tablesByName.TryGetValue(pb.TableName, out var pbTbl)) continue;
            if (!existingBindings.Add(pb.PluginCode.Trim().ToLowerInvariant())) continue;

            var catItem = catalog.FirstOrDefault(c => c.Code.Equals(pb.PluginCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (catItem != null)
            {
                await using var ensurePluginCmd = new NpgsqlCommand("""
                    INSERT INTO sys_plugins (id, code, name, category, version, description, manifest, config, is_enabled, created_by)
                    VALUES (@pid, @code, @name, @cat, @ver, @desc, @man::jsonb, '{}'::jsonb, FALSE, @cb)
                    ON CONFLICT (code) DO NOTHING
                    """, conn);
                ensurePluginCmd.Parameters.AddWithValue("pid", $"plg_{Guid.NewGuid().ToString("N")[..10]}");
                ensurePluginCmd.Parameters.AddWithValue("code", catItem.Code);
                ensurePluginCmd.Parameters.AddWithValue("name", catItem.Name);
                ensurePluginCmd.Parameters.AddWithValue("cat", catItem.Category);
                ensurePluginCmd.Parameters.AddWithValue("ver", catItem.Version);
                ensurePluginCmd.Parameters.AddWithValue("desc", catItem.Description);
                ensurePluginCmd.Parameters.AddWithValue("man", JsonSerializer.Serialize(new
                {
                    requiredConfigKeys = catItem.RequiredConfigKeys,
                    supportedOperations = catItem.SupportedOperations
                }));
                ensurePluginCmd.Parameters.AddWithValue("cb", actor);
                await ensurePluginCmd.ExecuteNonQueryAsync();
            }

            await using var pbCmd = new NpgsqlCommand("""
                INSERT INTO sys_app_plugin_bindings (id, app_id, plugin_code, is_enabled, table_id, field_mapping, updated_by)
                VALUES (@id, @app, @pc, TRUE, @tbl, @fm::jsonb, @ub)
                ON CONFLICT (app_id, plugin_code) DO NOTHING
                """, conn);
            pbCmd.Parameters.AddWithValue("id", $"apb_{Guid.NewGuid().ToString("N")[..10]}");
            pbCmd.Parameters.AddWithValue("app", appId);
            pbCmd.Parameters.AddWithValue("pc", pb.PluginCode);
            pbCmd.Parameters.AddWithValue("tbl", pbTbl.Id);
            pbCmd.Parameters.AddWithValue("fm", JsonSerializer.Serialize(pb.FieldMapping));
            pbCmd.Parameters.AddWithValue("ub", actor);
            await pbCmd.ExecuteNonQueryAsync();
            counts.PluginBindingsAdded++;
        }

        return counts;
    }

    private async Task<AppUpgradeCandidateDto> AnalyzeAppAsync(NpgsqlConnection conn, SysAppDto app)
    {
        var tree = await metadata.GetAppRuntimeTreeAsync(app.Id);
        var (coreSpec, tplSpec) = ResolveSpec(app.Code);
        var missing = new List<string>();

        if (ParseMajorVersion(app.Version) < 2)
            missing.Add($"Phiên bản hiện tại v{app.Version} (chưa lên 2.0.0)");

        if (tree != null)
        {
            var tablesByName = tree.Modules.SelectMany(m => m.Tables)
                .ToDictionary(t => t.Table.TableName, t => t, StringComparer.OrdinalIgnoreCase);

            var legacySubTables = tree.Modules.SelectMany(m => m.Tables).SelectMany(t => t.Columns)
                .Count(c => c.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(c.DefaultValue)
                    && !c.DefaultValue.TrimStart().StartsWith('['));
            if (legacySubTables > 0)
                missing.Add($"{legacySubTables} bảng con SUB_TABLE cần nâng cấp Typed JSON");

            if (tplSpec != null)
            {
                var missingMods = tplSpec.Modules.Count(m => !tree.Modules.Any(em => em.Module.Code.Equals(m.Code, StringComparison.OrdinalIgnoreCase)));
                if (missingMods > 0) missing.Add($"{missingMods} phân hệ v2.0 mới");

                var missingCols = 0;
                var missingRules = 0;
                foreach (var tblSpec in tplSpec.Modules.SelectMany(m => m.Tables))
                {
                    if (!tablesByName.TryGetValue(tblSpec.TableName, out var curTbl))
                    {
                        missing.Add($"Bảng '{tblSpec.DisplayName}'");
                        continue;
                    }
                    missingCols += tblSpec.Columns.Count(c => !curTbl.Columns.Any(ec => ec.ColumnCode.Equals(c.ColumnCode, StringComparison.OrdinalIgnoreCase)));
                    missingRules += tblSpec.FormRules.Count(r => !HasEquivalentRule(curTbl.Table.FormRules, r));
                }
                if (missingCols > 0) missing.Add($"{missingCols} cột dữ liệu v2.0 mới");
                if (missingRules > 0) missing.Add($"{missingRules} quy tắc FormRules động");

                await CheckArtifactGapsAsync(conn, app.Id, tablesByName,
                    tplSpec.Workflows, tplSpec.Automations, tplSpec.PrintTemplates,
                    tplSpec.SavedViews, tplSpec.Widgets, tplSpec.PluginBindings, missing);
            }
            else if (coreSpec != null)
            {
                var missingCols = 0;
                foreach (var (tblName, cols) in coreSpec.ExtraColumnsByTable)
                {
                    if (!tablesByName.TryGetValue(tblName, out var curTbl)) continue;
                    missingCols += cols.Count(c => !curTbl.Columns.Any(ec => ec.ColumnCode.Equals(c.ColumnCode, StringComparison.OrdinalIgnoreCase)));
                }
                if (missingCols > 0) missing.Add($"{missingCols} cột dữ liệu v2.0 mới");

                var missingRules = 0;
                foreach (var (tblName, rules) in coreSpec.FormRulesByTable)
                {
                    if (!tablesByName.TryGetValue(tblName, out var curTbl)) continue;
                    missingRules += rules.Count(r => !HasEquivalentRule(curTbl.Table.FormRules, r));
                }
                if (missingRules > 0) missing.Add($"{missingRules} quy tắc FormRules động");

                await CheckArtifactGapsAsync(conn, app.Id, tablesByName,
                    coreSpec.Workflows, coreSpec.Automations, coreSpec.PrintTemplates,
                    coreSpec.SavedViews, coreSpec.ExtraWidgets, coreSpec.PluginBindings, missing);
            }
            else if (tablesByName.Count > 0)
            {
                var firstTbl = tablesByName.Values.First().Table;
                var printCount = (await metadata.GetPrintTemplatesAsync(firstTbl.Id)).Count;
                if (printCount == 0) missing.Add("Mẫu in A4 chuẩn v2.0");
                var views = await LoadStringPairsAsync(conn, "SELECT table_id, LOWER(name) FROM sys_saved_views WHERE app_id = @app", app.Id);
                if (views.Count == 0) missing.Add("Saved View (FUN-06)");
            }
        }

        if (await CountReleasesAsync(conn, app.Id) == 0)
            missing.Add("Snapshot phát hành v2.0");

        return new AppUpgradeCandidateDto
        {
            AppId = app.Id,
            AppCode = app.Code,
            AppName = app.Name,
            CurrentVersion = app.Version,
            TargetVersion = "2.0.0",
            MatchedTemplateCode = coreSpec?.Code ?? tplSpec?.Code,
            NeedsUpgrade = missing.Count > 0,
            MissingFeatures = missing,
            Summary = missing.Count == 0
                ? "Đã đầy đủ toàn bộ tính năng phiên bản 2.0.0."
                : $"Cần bổ sung {missing.Count} hạng mục để lên chuẩn v2.0.0."
        };
    }

    private async Task ApplyModuleSpecsAsync(
        NpgsqlConnection conn,
        string appId,
        IReadOnlyList<TemplateModuleSpec> moduleSpecs,
        Dictionary<string, SysModuleDto> modulesByCode,
        Dictionary<string, SysTableDto> tablesByName,
        AppUpgradeExecutionResultDto result)
    {
        for (int modIdx = 0; modIdx < moduleSpecs.Count; modIdx++)
        {
            var modSpec = moduleSpecs[modIdx];
            if (!modulesByCode.TryGetValue(modSpec.Code, out var mod))
            {
                mod = await metadata.UpsertModuleAsync(new UpsertModuleRequest
                {
                    AppId = appId,
                    Code = modSpec.Code,
                    Name = modSpec.Name,
                    Icon = modSpec.Icon,
                    Description = modSpec.Description,
                    OrderIndex = modulesByCode.Count + 1
                });
                modulesByCode[mod.Code] = mod;
                result.ModulesAdded++;
            }

            for (int tblIdx = 0; tblIdx < modSpec.Tables.Count; tblIdx++)
            {
                var tblSpec = modSpec.Tables[tblIdx];
                if (!tablesByName.TryGetValue(tblSpec.TableName, out var tbl))
                {
                    tbl = await metadata.UpsertTableAsync(new UpsertTableRequest
                    {
                        AppId = appId,
                        ModuleId = mod.Id,
                        TableName = tblSpec.TableName,
                        DisplayName = tblSpec.DisplayName,
                        Description = tblSpec.Description,
                        PrimaryView = tblSpec.PrimaryView,
                        RowScope = tblSpec.RowScope,
                        ApprovalEnabled = tblSpec.ApprovalEnabled,
                        ApprovalField = tblSpec.ApprovalField,
                        ApproverRoles = tblSpec.ApproverRoles,
                        PublicFormEnabled = tblSpec.PublicFormEnabled,
                        OrderIndex = tblIdx + 1
                    });
                    tablesByName[tbl.TableName] = tbl;
                    result.TablesAdded++;
                }
                else
                {
                    await FixLegacyTableMetadataAsync(conn, tbl, tblSpec);
                }

                var existingCols = await metadata.GetColumnsAsync(tbl.Id);
                await EnsureColumnsAsync(conn, appId, tbl, tblSpec.Columns, existingCols, tablesByName, result);

                if (tblSpec.FormRules.Count > 0)
                {
                    // Reload table to get latest formRules
                    var refreshed = (await metadata.GetTablesAsync(appId, mod.Id)).FirstOrDefault(t => t.Id == tbl.Id) ?? tbl;
                    result.FormRulesAttached += await MergeFormRulesAsync(refreshed, tblSpec.FormRules);
                }
            }
        }
    }

    private async Task EnsureColumnsAsync(
        NpgsqlConnection conn,
        string appId,
        SysTableDto tbl,
        IReadOnlyList<TemplateColumnSpec> specCols,
        List<SysColumnDto> existingCols,
        IReadOnlyDictionary<string, SysTableDto> tablesByName,
        AppUpgradeExecutionResultDto result)
    {
        foreach (var colSpec in specCols)
        {
            var existing = existingCols.FirstOrDefault(c => c.ColumnCode.Equals(colSpec.ColumnCode, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var refAppId = colSpec.RefAppId;
                var refTableId = colSpec.RefTableId;
                if (!string.IsNullOrWhiteSpace(colSpec.InternalRefTableName) &&
                    tablesByName.TryGetValue(colSpec.InternalRefTableName, out var targetTbl))
                {
                    refAppId = appId;
                    refTableId = targetTbl.Id;
                }

                var created = await metadata.UpsertColumnAsync(new UpsertColumnRequest
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
                    AllowQuickCreate = colSpec.AllowQuickCreate,
                    RefAppId = refAppId,
                    RefTableId = refTableId,
                    RefValueField = colSpec.RefValueField,
                    RefDisplayField = colSpec.RefDisplayField,
                    LookupFilterField = colSpec.LookupFilterField,
                    LookupSourceField = colSpec.LookupSourceField,
                    Formula = colSpec.Formula,
                    DefaultValue = colSpec.DefaultValue,
                    HiddenForRoles = colSpec.HiddenForRoles ?? [],
                    ReadOnlyForRoles = colSpec.ReadOnlyForRoles ?? [],
                    OrderIndex = colSpec.OrderIndex
                });
                existingCols.Add(created);
                result.ColumnsUpgraded++;
                continue;
            }

            if (colSpec.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase) &&
                existing.DataType.Equals("SUB_TABLE", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(colSpec.DefaultValue) &&
                colSpec.DefaultValue.TrimStart().StartsWith('[') &&
                (string.IsNullOrWhiteSpace(existing.DefaultValue) || !existing.DefaultValue.TrimStart().StartsWith('[')))
            {
                result.ColumnsUpgraded += await UpgradeSubTableColumnAsync(conn, tbl.Id, existing.ColumnCode, colSpec.DefaultValue);
            }

            if (colSpec.AllowQuickCreate && !existing.AllowQuickCreate)
            {
                await using var qcCmd = new NpgsqlCommand(
                    "UPDATE sys_columns SET allow_quick_create = TRUE WHERE id = @id", conn);
                qcCmd.Parameters.AddWithValue("id", existing.Id);
                await qcCmd.ExecuteNonQueryAsync();
                result.ColumnsUpgraded++;
            }
        }
    }

    private static async Task FixLegacyTableMetadataAsync(NpgsqlConnection conn, SysTableDto cur, TemplateTableSpec spec)
    {
        if (string.Equals(cur.PrimaryView, "ALL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cur.Description, "table", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmd = new NpgsqlCommand("""
                UPDATE sys_tables
                SET primary_view = CASE WHEN UPPER(primary_view) = 'ALL' THEN @pv ELSE primary_view END,
                    description = CASE WHEN LOWER(COALESCE(description, '')) = 'table' THEN @desc ELSE description END,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                """, conn);
            cmd.Parameters.AddWithValue("pv", spec.PrimaryView);
            cmd.Parameters.AddWithValue("desc", spec.Description);
            cmd.Parameters.AddWithValue("id", cur.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<int> MergeFormRulesAsync(SysTableDto tbl, IReadOnlyList<FormRuleDto> targetRules)
    {
        var merged = tbl.FormRules.ToList();
        var added = 0;
        foreach (var rule in targetRules)
        {
            if (HasEquivalentRule(merged, rule)) continue;
            merged.Add(rule);
            added++;
        }
        if (added > 0)
        {
            await metadata.SetFormRulesAsync(tbl.Id, merged);
        }
        return added;
    }

    private static bool HasEquivalentRule(IReadOnlyList<FormRuleDto> existing, FormRuleDto candidate) =>
        existing.Any(e =>
            e.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase) ||
            (e.TargetField.Equals(candidate.TargetField, StringComparison.OrdinalIgnoreCase) &&
             e.Property.Equals(candidate.Property, StringComparison.OrdinalIgnoreCase)));

    private static async Task<int> UpgradeSubTableColumnAsync(NpgsqlConnection conn, string tableId, string colCode, string typedJson)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE sys_columns
            SET default_value = @def
            WHERE table_id = @tbl
              AND LOWER(column_code) = LOWER(@code)
              AND UPPER(data_type) = 'SUB_TABLE'
              AND (default_value IS NULL OR LEFT(LTRIM(default_value), 1) <> '[')
            """, conn);
        cmd.Parameters.AddWithValue("def", typedJson);
        cmd.Parameters.AddWithValue("tbl", tableId);
        cmd.Parameters.AddWithValue("code", colCode);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<AppUpgradeExecutionResultDto> EnsureBaselineCustomV2ArtifactsAsync(
        NpgsqlConnection conn,
        SysAppDto app,
        IReadOnlyDictionary<string, SysTableDto> tablesByName,
        string actor)
    {
        var counts = new AppUpgradeExecutionResultDto { AppId = app.Id };
        if (tablesByName.Count == 0) return counts;

        var firstTbl = tablesByName.Values.First();
        var cols = await metadata.GetColumnsAsync(firstTbl.Id);
        if (cols.Count == 0) return counts;

        var existingPrints = await metadata.GetPrintTemplatesAsync(firstTbl.Id);
        if (existingPrints.Count == 0)
        {
            var codeCol = cols.FirstOrDefault(c => c.IsIdentityKey)?.ColumnCode ?? cols[0].ColumnCode;
            var bodyRows = string.Join("", cols.Where(c => c.DataType != "SUB_TABLE").Take(6)
                .Select(c => $"<tr><td style=\"padding:6px;border:1px solid #d1d5db;font-weight:600;background:#f8fafc\">{c.ColumnName}</td><td style=\"padding:6px;border:1px solid #d1d5db\">{{{{{c.ColumnCode}}}}}</td></tr>"));
            var html = $"<div style=\"font-family:Arial,sans-serif\"><h2 style=\"text-align:center\">{firstTbl.DisplayName.ToUpperInvariant()}</h2><div>Số: {{{{{codeCol}}}}} | Ngày in: {{{{print_date}}}}</div><table style=\"width:100%;border-collapse:collapse;margin-top:12px\">{bodyRows}</table></div>";
            await metadata.UpsertPrintTemplateAsync(new UpsertPrintTemplateRequest
            {
                TableId = firstTbl.Id,
                Name = $"Phiếu {firstTbl.DisplayName} (A4)",
                PaperSize = "A4",
                Orientation = "portrait",
                HtmlTemplate = html,
                IsDefault = true,
                WatermarkDraft = firstTbl.ApprovalEnabled
            }, actor);
            counts.PrintTemplatesAdded++;
        }

        var existingViews = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(name) FROM sys_saved_views WHERE app_id = @app", app.Id);
        if (!existingViews.Any(v => v.Item1 == firstTbl.Id))
        {
            var viewName = firstTbl.ApprovalEnabled ? "Hồ sơ Chờ phê duyệt" : "Dữ liệu gần nhất";
            var filterJson = firstTbl.ApprovalEnabled
                ? "{\"logic\":\"AND\",\"conditions\":[{\"field\":\"approvalStatus\",\"op\":\"eq\",\"value\":\"PENDING\"}]}"
                : "{\"logic\":\"AND\",\"conditions\":[]}";
            await using var svCmd = new NpgsqlCommand("""
                INSERT INTO sys_saved_views (id, app_id, table_id, name, filters, sort_dir, is_shared, created_by)
                VALUES (@id, @app, @tbl, @name, @flt::jsonb, 'DESC', TRUE, @cb)
                """, conn);
            svCmd.Parameters.AddWithValue("id", $"view_{Guid.NewGuid().ToString("N")[..10]}");
            svCmd.Parameters.AddWithValue("app", app.Id);
            svCmd.Parameters.AddWithValue("tbl", firstTbl.Id);
            svCmd.Parameters.AddWithValue("name", viewName);
            svCmd.Parameters.AddWithValue("flt", filterJson);
            svCmd.Parameters.AddWithValue("cb", actor);
            await svCmd.ExecuteNonQueryAsync();
            counts.SavedViewsAdded++;
        }

        return counts;
    }

    private async Task CheckArtifactGapsAsync(
        NpgsqlConnection conn,
        string appId,
        Dictionary<string, TableWithColumnsDto> tablesByName,
        IReadOnlyList<TemplateWorkflowSpec> workflows,
        IReadOnlyList<TemplateAutomationSpec> automations,
        IReadOnlyList<TemplatePrintTemplateSpec> printTemplates,
        IReadOnlyList<TemplateSavedViewSpec> savedViews,
        IReadOnlyList<TemplateWidgetSpec> widgets,
        IReadOnlyList<TemplatePluginBindingSpec> pluginBindings,
        List<string> missing)
    {
        var existingWfs = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(name) FROM app_workflows WHERE app_id = @app", appId);
        var missWfs = workflows.Count(w => tablesByName.TryGetValue(w.TableName, out var t) && !existingWfs.Contains((t.Table.Id, w.Name.Trim().ToLowerInvariant())));
        if (missWfs > 0) missing.Add($"{missWfs} quy trình phê duyệt");

        var existingAutos = await LoadStringPairsAsync(conn,
            "SELECT source_table_id, LOWER(name) FROM app_automations WHERE source_app_id = @app", appId);
        var missAutos = automations.Count(a => tablesByName.TryGetValue(a.TriggerTableName, out var t) && !existingAutos.Contains((t.Table.Id, a.Name.Trim().ToLowerInvariant())));
        if (missAutos > 0) missing.Add($"{missAutos} tự động hóa");

        var existingPrints = await LoadStringPairsAsync(conn,
            """
            SELECT pt.table_id, LOWER(pt.name)
            FROM sys_print_templates pt
            JOIN sys_tables t ON t.id = pt.table_id
            WHERE t.app_id = @app
            """, appId);
        var missPrints = printTemplates.Count(p => tablesByName.TryGetValue(p.TableName, out var t) && !existingPrints.Contains((t.Table.Id, p.Name.Trim().ToLowerInvariant())));
        if (missPrints > 0) missing.Add($"{missPrints} mẫu in A4/K80");

        var existingViews = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(name) FROM sys_saved_views WHERE app_id = @app", appId);
        var missViews = savedViews.Count(v => tablesByName.TryGetValue(v.TableName, out var t) && !existingViews.Contains((t.Table.Id, v.Name.Trim().ToLowerInvariant())));
        if (missViews > 0) missing.Add($"{missViews} Saved Views (FUN-06)");

        var existingWidgets = await LoadStringPairsAsync(conn,
            "SELECT table_id, LOWER(title) FROM app_widgets WHERE app_id = @app", appId);
        var missWidgets = widgets.Count(w => tablesByName.TryGetValue(w.TableName, out var t) && !existingWidgets.Contains((t.Table.Id, w.Title.Trim().ToLowerInvariant())));
        if (missWidgets > 0) missing.Add($"{missWidgets} Dashboard Widget");

        var existingBindings = await LoadStringSetAsync(conn,
            "SELECT LOWER(plugin_code) FROM sys_app_plugin_bindings WHERE app_id = @app", appId);
        var missBindings = pluginBindings.Count(b => !existingBindings.Contains(b.PluginCode.Trim().ToLowerInvariant()));
        if (missBindings > 0) missing.Add($"{missBindings} liên kết Plugin VN");
    }

    public static string ConvertCommaSubTableToTypedJson(string commaCsv)
    {
        var tokens = commaCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasQty = tokens.Any(t => t.Equals("so_luong", StringComparison.OrdinalIgnoreCase) || t.Equals("sl", StringComparison.OrdinalIgnoreCase));
        var hasPrice = tokens.Any(t => t.Equals("don_gia", StringComparison.OrdinalIgnoreCase));
        var qtyCol = tokens.FirstOrDefault(t => t.Equals("so_luong", StringComparison.OrdinalIgnoreCase) || t.Equals("sl", StringComparison.OrdinalIgnoreCase)) ?? "so_luong";

        var specs = new List<SubColSpec>(tokens.Count);
        foreach (var raw in tokens)
        {
            var code = Regex.Replace(raw.ToLowerInvariant(), @"[^a-z0-9_]", "_");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var (label, dtype) = CommaColumnHints.TryGetValue(code, out var hint)
                ? hint
                : (code.Replace('_', ' '), InferSubColType(code));
            string? formula = hasQty && hasPrice && (code == "thanh_tien" || code == "so_tien")
                ? $"{qtyCol} * don_gia"
                : null;
            string? def = code is "so_luong" or "sl" ? "1" : null;
            specs.Add(new SubColSpec(code, label, dtype, formula == null && code is "ten_hang" or "hang_muc" or "ten_ts" or "so_luong" or "don_gia", 150, def, formula));
        }
        return AppTemplateCatalog.SubTableJson(specs.ToArray());
    }

    private static string InferSubColType(string code)
    {
        if (code.Contains("ngay") || code.Contains("date")) return "DATE";
        if (code.Contains("gia") || code.Contains("tien")) return "CURRENCY";
        if (code.Contains("luong") || code == "sl" || code.Contains("so_")) return "NUMBER";
        return "STRING";
    }

    private static (CoreAppV2Spec? Core, AppTemplateDefinition? Template) ResolveSpec(string appCode)
    {
        var core = AppTemplateCatalog.GetCoreAppV2Specs()
            .FirstOrDefault(c => c.Code.Equals(appCode, StringComparison.OrdinalIgnoreCase));
        if (core != null) return (core, null);

        var direct = AppTemplateCatalog.Find(appCode);
        if (direct != null) return (null, direct);

        var stripped = Regex.Replace(appCode.Trim(), @"_\d{6}$", "");
        return (null, AppTemplateCatalog.Find(stripped));
    }

    private static int ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 1;
        var first = version.Trim().Split('.')[0];
        return int.TryParse(first, out var major) ? major : 1;
    }

    private static async Task<int> CountReleasesAsync(NpgsqlConnection conn, string appId)
    {
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_app_releases WHERE app_id = @app", conn);
        cmd.Parameters.AddWithValue("app", appId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<HashSet<(string, string)>> LoadStringPairsAsync(NpgsqlConnection conn, string sql, string appId)
    {
        var set = new HashSet<(string, string)>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("app", appId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            set.Add((r.GetString(0), r.GetString(1)));
        return set;
    }

    private static async Task<HashSet<string>> LoadStringSetAsync(NpgsqlConnection conn, string sql, string appId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("app", appId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            set.Add(r.GetString(0));
        return set;
    }

    private static void CopyArtifactCounts(AppUpgradeExecutionResultDto src, AppUpgradeExecutionResultDto dst)
    {
        dst.WidgetsAdded += src.WidgetsAdded;
        dst.WorkflowsAdded += src.WorkflowsAdded;
        dst.AutomationsAdded += src.AutomationsAdded;
        dst.PrintTemplatesAdded += src.PrintTemplatesAdded;
        dst.SavedViewsAdded += src.SavedViewsAdded;
        dst.PluginBindingsAdded += src.PluginBindingsAdded;
    }
}
