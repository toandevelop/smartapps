using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class AppTemplateCatalogTests
{
    private static readonly Dictionary<string, HashSet<string>> BaseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DM_PHONG_BAN"] = new(["TECH", "HR", "FIN", "OPS", "SALES"], StringComparer.OrdinalIgnoreCase),
        ["DM_CHUC_VU"] = new(["DEV", "LEAD", "MGR", "SPEC", "DIR"], StringComparer.OrdinalIgnoreCase),
        ["DM_LOAI_NGHI"] = new(["PHEP_NAM", "OM", "CONG_TAC", "KHONG_LUONG"], StringComparer.OrdinalIgnoreCase),
        ["DM_TRANG_THAI_DUYET"] = new(["CHO_DUYET", "DA_DUYET", "TU_CHOI"], StringComparer.OrdinalIgnoreCase),
        ["DM_NHOM_TAI_SAN"] = new(["LAPTOP", "SERVER", "NOI_THAT", "LICENSE"], StringComparer.OrdinalIgnoreCase),
        ["DM_TINH_TRANG_TS"] = new(["DANG_DUNG", "TRONG_KHO", "BAO_TRI", "THANH_LY"], StringComparer.OrdinalIgnoreCase),
        ["DM_LOAI_YEU_CAU"] = new(["IT_SUPPORT", "HANH_CHINH", "THANH_TOAN", "TUYEN_DUNG"], StringComparer.OrdinalIgnoreCase),
        ["DM_UU_TIEN"] = new(["HIGH", "MEDIUM", "LOW"], StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void Catalog_HasAtLeastTwelveUniqueEnterpriseTemplates()
    {
        var all = AppTemplateCatalog.GetAll();
        var summaries = AppTemplateCatalog.GetSummaries();

        Assert.True(all.Count >= 12);
        Assert.Equal(all.Count, summaries.Count);
        Assert.Equal(all.Count, all.Select(t => t.TemplateKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(all.Count, all.Select(t => t.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var tpl in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(tpl.Name));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Category));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Department));
            Assert.NotEmpty(tpl.Modules);
            Assert.NotEmpty(tpl.SampleRecords);
            Assert.NotEmpty(tpl.Widgets);
            Assert.Same(tpl, AppTemplateCatalog.Find(tpl.TemplateKey));
        }

        Assert.Null(AppTemplateCatalog.Find("NON_EXISTENT_TEMPLATE"));
    }

    [Fact]
    public void AllTemplates_HaveValidReferencesWidgetsAndEvaluatedSampleRecords()
    {
        var compute = new DynamicComputeService();

        foreach (var tpl in AppTemplateCatalog.GetAll())
        {
            var catSets = new Dictionary<string, HashSet<string>>(BaseCategories, StringComparer.OrdinalIgnoreCase);
            foreach (var c in tpl.Categories)
            {
                catSets[c.CategoryCode] = c.Items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var tablesByName = new Dictionary<string, TemplateTableSpec>(StringComparer.OrdinalIgnoreCase);
            var colsByTable = new Dictionary<string, List<SysColumnDto>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in tpl.Modules)
            {
                foreach (var tbl in mod.Tables)
                {
                    Assert.Contains(tbl.Columns, c => c.IsIdentityKey);

                    foreach (var col in tbl.Columns)
                    {
                        if (!string.IsNullOrWhiteSpace(col.RefCategoryCode))
                        {
                            Assert.True(catSets.ContainsKey(col.RefCategoryCode),
                                $"Template {tpl.Code}.{tbl.TableName}.{col.ColumnCode} references missing category {col.RefCategoryCode}");
                        }

                        if (!string.IsNullOrWhiteSpace(col.InternalRefTableName))
                        {
                            Assert.True(tablesByName.TryGetValue(col.InternalRefTableName, out var targetTbl),
                                $"Template {tpl.Code}.{tbl.TableName}.{col.ColumnCode} references unknown/later table {col.InternalRefTableName}");
                            Assert.Contains(targetTbl!.Columns, tc => tc.ColumnCode == col.RefValueField);
                            Assert.Contains(targetTbl.Columns, tc => tc.ColumnCode == col.RefDisplayField);
                        }
                    }

                    tablesByName[tbl.TableName] = tbl;
                    colsByTable[tbl.TableName] = tbl.Columns.Select(c => new SysColumnDto
                    {
                        ColumnCode = c.ColumnCode,
                        ColumnName = c.ColumnName,
                        DataType = c.DataType,
                        IsRequired = c.IsRequired,
                        IsIdentityKey = c.IsIdentityKey,
                        ValidationRegex = c.ValidationRegex,
                        RefCategoryCode = c.RefCategoryCode,
                        Formula = c.Formula,
                        DefaultValue = c.DefaultValue
                    }).ToList();
                }
            }

            foreach (var w in tpl.Widgets)
            {
                Assert.True(colsByTable.TryGetValue(w.TableName, out var tCols),
                    $"Widget '{w.Title}' in {tpl.Code} references missing table {w.TableName}");
                if (!string.IsNullOrWhiteSpace(w.MetricField))
                    Assert.Contains(tCols!, c => c.ColumnCode == w.MetricField);
                if (!string.IsNullOrWhiteSpace(w.GroupField))
                    Assert.Contains(tCols!, c => c.ColumnCode == w.GroupField);
                if (!string.IsNullOrWhiteSpace(w.FilterField))
                    Assert.Contains(tCols!, c => c.ColumnCode == w.FilterField);
            }

            foreach (var grp in tpl.SampleRecords.GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase))
            {
                Assert.True(colsByTable.TryGetValue(grp.Key, out var tCols),
                    $"SampleRecord in {tpl.Code} references missing table {grp.Key}");

                var batch = grp.Select((r, idx) => ($"rec_{idx}", new Dictionary<string, object?>(r.Data))).ToList();
                var results = compute.EvaluateAndValidate(tCols!, catSets, batch);

                foreach (var res in results)
                {
                    Assert.True(res.IsValid,
                        $"Template {tpl.Code}.{grp.Key} record {res.Id} invalid: {string.Join("; ", res.Errors.Select(e => e.Message))}");
                    Assert.Empty(res.Errors);

                    foreach (var formulaCol in tCols!.Where(c => !string.IsNullOrWhiteSpace(c.Formula)))
                    {
                        Assert.True(res.EvaluatedData.ContainsKey(formulaCol.ColumnCode),
                            $"Formula column {tpl.Code}.{grp.Key}.{formulaCol.ColumnCode} did not compute.");
                    }
                }
            }
        }
    }

    [Fact]
    public void AllTemplates_AndCoreApps_MeetV2Standards()
    {
        var compute = new DynamicComputeService();
        var catSets = new Dictionary<string, HashSet<string>>(BaseCategories, StringComparer.OrdinalIgnoreCase);
        var summaries = AppTemplateCatalog.GetSummaries();

        Assert.All(summaries, s =>
        {
            Assert.Equal("2.0.0", s.Version);
            Assert.True(s.FormRuleCount >= 1, $"Template {s.Code} missing FormRules");
            Assert.True(s.WorkflowCount >= 1, $"Template {s.Code} missing Workflows");
            Assert.True(s.AutomationCount >= 1, $"Template {s.Code} missing Automations");
            Assert.True(s.PrintTemplateCount >= 1, $"Template {s.Code} missing PrintTemplates");
            Assert.True(s.SavedViewCount >= 1, $"Template {s.Code} missing SavedViews");
        });

        foreach (var tpl in AppTemplateCatalog.GetAll())
        {
            Assert.Equal("2.0.0", tpl.Version);

            var localCats = new Dictionary<string, HashSet<string>>(catSets, StringComparer.OrdinalIgnoreCase);
            foreach (var cat in tpl.Categories)
                localCats[cat.CategoryCode] = cat.Items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var colsByTable = new Dictionary<string, List<SysColumnDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var tbl in tpl.Modules.SelectMany(m => m.Tables))
            {
                Assert.Contains(tbl.PrimaryView, new[] { "table", "kanban", "calendar" });
                Assert.NotEqual("table", tbl.Description);

                var dtos = tbl.Columns.Select((c, i) => new SysColumnDto
                {
                    Id = $"col_{tbl.TableName}_{i}",
                    TableId = tbl.TableName,
                    ColumnCode = c.ColumnCode,
                    ColumnName = c.ColumnName,
                    DataType = c.DataType,
                    IsRequired = c.IsRequired,
                    IsIdentityKey = c.IsIdentityKey,
                    ShowInTable = c.ShowInTable,
                    ShowInForm = c.ShowInForm,
                    Width = c.Width,
                    ValidationRegex = c.ValidationRegex,
                    RefCategoryCode = c.RefCategoryCode,
                    AllowQuickCreate = c.AllowQuickCreate,
                    RefAppId = c.RefAppId,
                    RefTableId = c.RefTableId ?? c.InternalRefTableName,
                    RefValueField = c.RefValueField,
                    RefDisplayField = c.RefDisplayField,
                    Formula = c.Formula,
                    DefaultValue = c.DefaultValue,
                    HiddenForRoles = c.HiddenForRoles ?? [],
                    ReadOnlyForRoles = c.ReadOnlyForRoles ?? [],
                    OrderIndex = c.OrderIndex
                }).ToList();

                DynamicComputeService.ValidateFormulaGraph(dtos);
                FormRuleService.ValidateDefinition(tbl.FormRules, dtos);

                foreach (var subCol in dtos.Where(c => c.DataType == "SUB_TABLE"))
                {
                    Assert.False(string.IsNullOrWhiteSpace(subCol.DefaultValue));
                    Assert.StartsWith("[", subCol.DefaultValue!.TrimStart());
                    var parsed = DynamicComputeService.ParseSubTableSchema(subCol.DefaultValue);
                    Assert.True(parsed.Count >= 2, $"SUB_TABLE {tpl.Code}.{tbl.TableName}.{subCol.ColumnCode} should define >= 2 typed columns");
                }

                colsByTable[tbl.TableName] = dtos;

                var matchingRecords = tpl.SampleRecords
                    .Where(r => r.TableName.Equals(tbl.TableName, StringComparison.OrdinalIgnoreCase))
                    .Select((r, idx) => ($"v2_{idx}", new Dictionary<string, object?>(r.Data)))
                    .ToList();
                if (matchingRecords.Count > 0)
                {
                    var results = compute.EvaluateAndValidate(dtos, localCats, matchingRecords, tbl.FormRules);
                    Assert.All(results, r => Assert.True(r.IsValid,
                        $"Template {tpl.Code}.{tbl.TableName} failed v2 FormRule/formula validation: {string.Join("; ", r.Errors.Select(e => e.Message))}"));
                }
            }

            foreach (var sv in tpl.SavedViews)
            {
                Assert.True(colsByTable.ContainsKey(sv.TableName));
                using var doc = System.Text.Json.JsonDocument.Parse(sv.FiltersJson);
                Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("conditions").ValueKind);
            }
        }

        var cores = AppTemplateCatalog.GetCoreAppV2Specs();
        Assert.Equal(4, cores.Count);
        Assert.All(cores, core =>
        {
            Assert.Equal("2.0.0", core.Version);
            Assert.NotEmpty(core.FormRulesByTable);
            Assert.NotEmpty(core.Automations);
            Assert.NotEmpty(core.PrintTemplates);
            Assert.NotEmpty(core.SavedViews);
            Assert.NotEmpty(core.PluginBindings);
        });
    }
}
