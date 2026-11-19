using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class AppStorePostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public AppStorePostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task AuthenticateAdminAsync()
    {
        using var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "123456" });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [PostgresFact]
    public void BuiltInStoreCatalog_Has20ValidV2PackagesWithDeterministicChecksums()
    {
        var store = _fx.Services.GetRequiredService<AppStoreService>();
        var pkgs = store.GetBuiltInPackages();
        Assert.True(pkgs.Count >= 20, $"Expected >= 20 store apps, got {pkgs.Count}");

        foreach (var pkg in pkgs)
        {
            Assert.Equal("2.0.0", pkg.Version);
            Assert.False(string.IsNullOrWhiteSpace(pkg.ChecksumSha256));
            Assert.Equal(pkg.ChecksumSha256, AppStoreService.ComputeCanonicalChecksum(pkg));

            var catSets = pkg.Categories.ToDictionary(
                c => c.CategoryCode,
                c => c.Items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            catSets["DM_TRANG_THAI_DUYET"] = new(["CHO_DUYET", "DA_DUYET", "TU_CHOI"], StringComparer.OrdinalIgnoreCase);
            catSets["DM_PHONG_BAN"] = new(["TECH", "HR", "FIN", "OPS", "SALES"], StringComparer.OrdinalIgnoreCase);
            catSets["DM_TRANG_THAI_TASK"] = new(["TODO", "IN_PROGRESS", "REVIEW", "DONE"], StringComparer.OrdinalIgnoreCase);

            var compute = new DynamicComputeService();
            foreach (var tbl in pkg.Modules.SelectMany(m => m.Tables))
            {
                var cols = tbl.Columns.Select((c, i) => new SysColumnDto
                {
                    Id = $"sc_{tbl.TableName}_{i}",
                    TableId = tbl.TableName,
                    ColumnCode = c.ColumnCode,
                    ColumnName = c.ColumnName,
                    DataType = c.DataType,
                    IsRequired = c.IsRequired,
                    IsIdentityKey = c.IsIdentityKey,
                    ShowInTable = c.ShowInTable,
                    ShowInForm = c.ShowInForm,
                    Width = c.Width,
                    RefCategoryCode = c.RefCategoryCode,
                    Formula = c.Formula,
                    DefaultValue = c.DefaultValue,
                    HiddenForRoles = c.HiddenForRoles,
                    ReadOnlyForRoles = c.ReadOnlyForRoles,
                    OrderIndex = c.OrderIndex
                }).ToList();

                DynamicComputeService.ValidateFormulaGraph(cols);
                FormRuleService.ValidateDefinition(tbl.FormRules, cols);

                var sampleBatch = pkg.SampleRecords
                    .Where(r => r.TableName.Equals(tbl.TableName, StringComparison.OrdinalIgnoreCase))
                    .Select((r, idx) => ($"s_{idx}", new Dictionary<string, object?>(r.Data)))
                    .ToList();
                if (sampleBatch.Count > 0)
                {
                    var res = compute.EvaluateAndValidate(cols, catSets, sampleBatch, tbl.FormRules);
                    Assert.All(res, r => Assert.True(r.IsValid,
                        $"Store package {pkg.PackageCode}.{tbl.TableName} failed validation: {string.Join("; ", r.Errors.Select(e => e.Message))}"));
                }
            }
        }
    }

    [PostgresFact]
    public async Task StoreEndpoints_SupportManifestDownloadInstallExportAndRemoteRegistrySync()
    {
        await AuthenticateAdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

        // 1. Anonymous manifest.json works and lists all >= 20 apps with download URLs
        using var anonClient = new HttpClient { BaseAddress = _fx.Client.BaseAddress };
        var manifestRes = await anonClient.GetAsync("/api/store/manifest.json");
        manifestRes.EnsureSuccessStatusCode();
        var manifestJson = await manifestRes.Content.ReadFromJsonAsync<JsonElement>();
        var manifestApps = manifestJson.GetProperty("apps").EnumerateArray().ToList();
        Assert.True(manifestApps.Count >= 20);
        Assert.Contains(manifestApps, a => a.GetProperty("packageCode").GetString() == "ECOMMERCE_VN");

        // 2. Download package .smartapp.json file and verify attachment header + SHA-256 + counter
        var dlRes = await _fx.Client.GetAsync("/api/store/apps/ECOMMERCE_VN/download");
        dlRes.EnsureSuccessStatusCode();
        Assert.Contains("smartapp-ecommerce_vn", dlRes.Content.Headers.ContentDisposition?.FileName ?? string.Empty);
        var dlText = await dlRes.Content.ReadAsStringAsync();
        var dlPkg = JsonSerializer.Deserialize<StoreAppPackageDto>(dlText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("ECOMMERCE_VN", dlPkg.PackageCode);
        Assert.Equal(dlPkg.ChecksumSha256, AppStoreService.ComputeCanonicalChecksum(dlPkg));

        // Tampered package must be rejected
        var tampered = JsonSerializer.Deserialize<StoreAppPackageDto>(dlText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        tampered.Name = "Tampered Evil App";
        var badRes = await _fx.Client.PostAsJsonAsync("/api/store/install-package", new
        {
            package = tampered,
            customCode = $"EVIL_{suffix}"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

        // 3. 1-Click Install Store Exclusive App (CLINIC_HIS) with internal lookup + FormRule + Print + View
        var installRes = await _fx.Client.PostAsJsonAsync("/api/store/apps/CLINIC_HIS/install", new
        {
            customCode = $"CLINIC_{suffix}",
            customName = $"Phòng khám Test {suffix}",
            includeSampleData = true
        });
        installRes.EnsureSuccessStatusCode();
        var installedApp = (await installRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal($"CLINIC_{suffix}", installedApp.GetProperty("code").GetString());
        Assert.Equal("2.0.0", installedApp.GetProperty("version").GetString());
        Assert.Equal("PUBLISHED", installedApp.GetProperty("publishStatus").GetString());
        Assert.True(installedApp.GetProperty("recordCount").GetInt32() >= 2);

        // 4. Export installed multi-table app as .smartapp.json and install onto fresh code
        var expRes = await _fx.Client.GetAsync($"/api/store/export-package/CLINIC_{suffix}");
        expRes.EnsureSuccessStatusCode();
        var expText = await expRes.Content.ReadAsStringAsync();
        var expDoc = JsonDocument.Parse(expText);
        var cloneRes = await _fx.Client.PostAsJsonAsync("/api/store/install-package", new
        {
            package = expDoc.RootElement,
            customCode = $"CLINIC_CLONE_{suffix}",
            includeSampleData = true
        });
        cloneRes.EnsureSuccessStatusCode();
        var clonedApp = (await cloneRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal($"CLINIC_CLONE_{suffix}", clonedApp.GetProperty("code").GetString());
        Assert.Equal("2.0.0", clonedApp.GetProperty("version").GetString());

        // 5. Connect & sync a live Remote Online Store Registry pointing to manifest endpoint
        var registryUrl = $"{_fx.Client.BaseAddress}api/store/manifest.json";
        var regRes = await _fx.Client.PostAsJsonAsync("/api/store/registries", new
        {
            name = $"Community Store Mirror {suffix}",
            registryUrl,
            description = "Live HTTP mirror test",
            isEnabled = true
        });
        regRes.EnsureSuccessStatusCode();
        var regData = (await regRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var regId = regData.GetProperty("id").GetString()!;
        Assert.True(regData.GetProperty("appCount").GetInt32() >= 20);
        Assert.True(regData.TryGetProperty("lastError", out var errProp) && errProp.ValueKind == JsonValueKind.Null);

        // Fetch remote apps filtered by registryId and install ECOMMERCE_VN from remote registry
        var remList = await _fx.Client.GetFromJsonAsync<JsonElement>($"/api/store/apps?registryId={Uri.EscapeDataString(regId)}");
        Assert.Contains(remList.GetProperty("data").EnumerateArray(), x => x.GetProperty("sourceType").GetString() == "REMOTE");

        var remInstall = await _fx.Client.PostAsJsonAsync("/api/store/apps/ECOMMERCE_VN/install", new
        {
            registryId = regId,
            customCode = $"ECOM_REM_{suffix}",
            includeSampleData = true
        });
        remInstall.EnsureSuccessStatusCode();
        var remApp = (await remInstall.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal($"ECOM_REM_{suffix}", remApp.GetProperty("code").GetString());
        Assert.Equal("2.0.0", remApp.GetProperty("version").GetString());

        // Cleanup registry
        var delReg = await _fx.Client.DeleteAsync($"/api/store/registries/{regId}");
        delReg.EnsureSuccessStatusCode();
    }

    [Fact]
    public void DatabaseSchemaFile_IncludesEveryMigrationTable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? schemaPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "db", "schema.sql");
            if (File.Exists(candidate))
            {
                schemaPath = candidate;
                break;
            }
            dir = dir.Parent;
        }
        Assert.NotNull(schemaPath);
        var schemaText = File.ReadAllText(schemaPath!);

        var createdTables = System.Text.RegularExpressions.Regex.Matches(
                string.Join("\n", SmartApps.Api.Data.DatabaseMigrationRunner.GetMigrations().Select(m => m.Sql)),
                @"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+([a-zA-Z0-9_]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !string.Equals(t, "sys_schema_migrations", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(createdTables);
        foreach (var tbl in createdTables)
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {tbl}", schemaText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [PostgresFact]
    public async Task StoreHardening_HistoryToggleForwardLookupUpdateDetectionAndRollbackWork()
    {
        await AuthenticateAdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

        // 1. Two-pass forward-referencing lookup package (table1 looks up table2 defined after table1)
        var twoPassPkg = new StoreAppPackageDto
        {
            PackageFormat = "smartapps-package/2.0",
            PackageCode = $"TWOPASS_{suffix}",
            Name = $"Two Pass Test {suffix}",
            Version = "2.0.0",
            Modules =
            [
                new StorePackageModuleDto
                {
                    Code = "MAIN",
                    Name = "Main",
                    Tables =
                    [
                        new StorePackageTableDto
                        {
                            TableName = "tbl_child_first",
                            DisplayName = "Child First",
                            Columns =
                            [
                                new StorePackageColumnDto { ColumnCode = "code", ColumnName = "Code", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 },
                                new StorePackageColumnDto { ColumnCode = "parent_ref", ColumnName = "Parent Ref", DataType = "LOOKUP", InternalRefTableName = "tbl_parent_second", RefValueField = "code", RefDisplayField = "title", OrderIndex = 2 }
                            ]
                        },
                        new StorePackageTableDto
                        {
                            TableName = "tbl_parent_second",
                            DisplayName = "Parent Second",
                            Columns =
                            [
                                new StorePackageColumnDto { ColumnCode = "code", ColumnName = "Code", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 },
                                new StorePackageColumnDto { ColumnCode = "title", ColumnName = "Title", DataType = "STRING", OrderIndex = 2 }
                            ]
                        }
                    ]
                }
            ]
        };
        twoPassPkg.ChecksumSha256 = AppStoreService.ComputeCanonicalChecksum(twoPassPkg);

        var twoPassRes = await _fx.Client.PostAsJsonAsync("/api/store/install-package", new { package = twoPassPkg, includeSampleData = false });
        twoPassRes.EnsureSuccessStatusCode();
        var twoPassApp = (await twoPassRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var twoPassCode = twoPassApp.GetProperty("code").GetString()!;
        var twoPassId = twoPassApp.GetProperty("id").GetString()!;

        var runtimeRes = await _fx.Client.GetFromJsonAsync<JsonElement>($"/api/apps/{Uri.EscapeDataString(twoPassCode)}?previewDraft=true");
        var tablesArr = runtimeRes.GetProperty("data").GetProperty("modules")[0].GetProperty("tables").EnumerateArray().ToList();
        var childTbl = tablesArr.First(t => t.GetProperty("table").GetProperty("tableName").GetString() == "tbl_child_first");
        var parentTbl = tablesArr.First(t => t.GetProperty("table").GetProperty("tableName").GetString() == "tbl_parent_second");
        var refCol = childTbl.GetProperty("columns").EnumerateArray().First(c => c.GetProperty("columnCode").GetString() == "parent_ref");
        Assert.Equal(twoPassId, refCol.GetProperty("refAppId").GetString());
        Assert.Equal(parentTbl.GetProperty("table").GetProperty("id").GetString(), refCol.GetProperty("refTableId").GetString());

        // 2. Compensating rollback when mid-install circular formula fails in pass 2
        var badPkgCode = $"ROLLBACK_{suffix}";
        var badPkg = new StoreAppPackageDto
        {
            PackageFormat = "smartapps-package/2.0",
            PackageCode = badPkgCode,
            Name = $"Rollback Test {suffix}",
            Version = "2.0.0",
            Modules =
            [
                new StorePackageModuleDto
                {
                    Code = "MAIN",
                    Name = "Main",
                    Tables =
                    [
                        new StorePackageTableDto
                        {
                            TableName = "tbl_bad_cycle",
                            DisplayName = "Bad Cycle",
                            Columns =
                            [
                                new StorePackageColumnDto { ColumnCode = "a", ColumnName = "A", DataType = "NUMBER", Formula = "b + 1", OrderIndex = 1 },
                                new StorePackageColumnDto { ColumnCode = "b", ColumnName = "B", DataType = "NUMBER", Formula = "a + 1", OrderIndex = 2 }
                            ]
                        }
                    ]
                }
            ]
        };
        badPkg.ChecksumSha256 = AppStoreService.ComputeCanonicalChecksum(badPkg);
        var failRes = await _fx.Client.PostAsJsonAsync("/api/store/install-package", new { package = badPkg, includeSampleData = false });
        Assert.Equal(HttpStatusCode.BadRequest, failRes.StatusCode);

        var metadata = _fx.Services.GetRequiredService<MetadataService>();
        Assert.Null(await metadata.GetAppByCodeOrIdAsync(badPkgCode));

        // 3. Semantic update detection (existing v1.0.0 app matching v2.0.0 store package HELPDESK vs custom)
        await metadata.UpsertAppAsync(new UpsertAppRequest { Code = "FINANCE", Name = "Old Finance", Version = "1.0.0" });
        var catalogAfterLegacy = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/store/apps?search=FINANCE");
        var finSummary = catalogAfterLegacy.GetProperty("data").EnumerateArray().First(a => a.GetProperty("packageCode").GetString() == "FINANCE");
        Assert.True(finSummary.GetProperty("isInstalled").GetBoolean());
        Assert.Equal("1.0.0", finSummary.GetProperty("installedVersion").GetString());
        Assert.True(finSummary.GetProperty("hasUpdateAvailable").GetBoolean());
        var legacyFin = await metadata.GetAppByCodeOrIdAsync("FINANCE");
        if (legacyFin != null) await metadata.DeleteAppAsync(legacyFin.Id);

        // 4. Registry toggle disables and re-enables remote listings + Store History logs installs
        var regUrl = $"{_fx.Client.BaseAddress}api/store/manifest.json";
        var createReg = await _fx.Client.PostAsJsonAsync("/api/store/registries", new { name = $"ToggleHub_{suffix}", registryUrl = regUrl, isEnabled = true });
        createReg.EnsureSuccessStatusCode();
        var regId = (await createReg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString()!;

        var toggleOff = await _fx.Client.PatchAsync($"/api/store/registries/{regId}/toggle", null);
        toggleOff.EnsureSuccessStatusCode();
        Assert.False((await toggleOff.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("isEnabled").GetBoolean());
        var listWhileOff = await _fx.Client.GetFromJsonAsync<JsonElement>($"/api/store/apps?registryId={Uri.EscapeDataString(regId)}");
        Assert.Empty(listWhileOff.GetProperty("data").EnumerateArray());

        var toggleOn = await _fx.Client.PatchAsync($"/api/store/registries/{regId}/toggle", null);
        toggleOn.EnsureSuccessStatusCode();
        Assert.True((await toggleOn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("isEnabled").GetBoolean());

        var histRes = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/store/history?limit=20");
        Assert.Contains(histRes.GetProperty("data").EnumerateArray(), x => x.GetProperty("packageCode").GetString() == $"TWOPASS_{suffix}");

        await _fx.Client.DeleteAsync($"/api/store/registries/{regId}");
        await metadata.DeleteAppAsync(twoPassId);
    }
}
