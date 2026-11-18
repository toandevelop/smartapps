using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class ExcelImportPostgresTests(PriorityPostgresFixture fixture) : IClassFixture<PriorityPostgresFixture>
{
    private sealed record Context(string AppId, string ModuleId, string TableId, string LookupId);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<Context> CreateContextAsync()
    {
        var metadata = fixture.Services.GetRequiredService<MetadataService>();
        var app = (await metadata.GetAppByCodeOrIdAsync("PRIORITY"))!;
        var module = (await metadata.GetModulesAsync(app.Id)).First();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var lookup = await metadata.UpsertTableAsync(new() { AppId = app.Id, ModuleId = module.Id, TableName = "lookup_" + suffix, DisplayName = "Lookup", RowScope = "OWNER_ONLY" });
        await metadata.UpsertColumnAsync(new() { TableId = lookup.Id, ColumnCode = "code", ColumnName = "Code", IsIdentityKey = true });
        var table = await metadata.UpsertTableAsync(new() { AppId = app.Id, ModuleId = module.Id, TableName = "import_" + suffix, DisplayName = "Import", RowScope = "OWNER_ONLY" });
        var category = "IMPORT_" + suffix;
        await metadata.UpsertCategoryAsync(new() { CategoryCode = category, CategoryName = "Import category", AppId = app.Id });
        await metadata.UpsertCategoryItemAsync(new() { CategoryCode = category, ItemCode = "A", ItemName = "Category A" });
        foreach (var column in new[] {
            new UpsertColumnRequest { ColumnCode = "code", ColumnName = "Code", IsIdentityKey = true, IsRequired = true },
            new UpsertColumnRequest { ColumnCode = "title", ColumnName = "Title", IsRequired = true },
            new UpsertColumnRequest { ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER", IsRequired = true },
            new UpsertColumnRequest { ColumnCode = "email", ColumnName = "Email", DataType = "EMAIL" },
            new UpsertColumnRequest { ColumnCode = "date", ColumnName = "Date", DataType = "DATE" },
            new UpsertColumnRequest { ColumnCode = "flag", ColumnName = "Flag", DataType = "BOOLEAN" },
            new UpsertColumnRequest { ColumnCode = "category", ColumnName = "Category", DataType = "SELECT", RefCategoryCode = category },
            new UpsertColumnRequest { ColumnCode = "ref", ColumnName = "Reference", DataType = "LOOKUP", RefTableId = lookup.Id, RefValueField = "code" },
            new UpsertColumnRequest { ColumnCode = "total", ColumnName = "Total", DataType = "NUMBER", Formula = "amount * 2" },
            new UpsertColumnRequest { ColumnCode = "secret", ColumnName = "Secret", HiddenForRoles = ["TEST_IMPORT", "TEST_CREATE"] },
            new UpsertColumnRequest { ColumnCode = "protected", ColumnName = "Protected", ReadOnlyForRoles = ["TEST_IMPORT", "TEST_CREATE"] }
        }) { column.TableId = table.Id; await metadata.UpsertColumnAsync(column); }
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var seed = new NpgsqlCommand("""
            INSERT INTO sys_users(id,username,full_name,role_code,role_codes,password_hash,allowed_apps,permissions)
            VALUES ('importer','importer','Importer','TEST_IMPORT','["TEST_IMPORT"]','unused','["PRIORITY"]','["record.view","record.create","record.edit"]'),
                   ('creator','creator','Creator','TEST_CREATE','["TEST_CREATE"]','unused','["PRIORITY"]','["record.view","record.create"]')
            ON CONFLICT(username) DO UPDATE SET permissions=EXCLUDED.permissions;
            INSERT INTO app_records(id,app_id,module_id,table_id,record_key,data,status,created_by,approval_status)
            VALUES (@tbl || '_old',@app,@mod,@tbl,'OLD','{"code":"OLD","title":"Keep existing","amount":5,"secret":"hidden-secret","protected":"keep-protected"}','ACTIVE','importer','DRAFT'),
                   (@tbl || '_locked',@app,@mod,@tbl,'LOCKED','{"code":"LOCKED","title":"Locked","amount":7}','ACTIVE','importer','APPROVED'),
                   (@tbl || '_foreign',@app,@mod,@tbl,'FOREIGN','{"code":"FOREIGN","title":"private-outside-title","amount":8,"secret":"outside-secret"}','ACTIVE','outsider','DRAFT'),
                   (@lookup || '_own',@app,@mod,@lookup,'L1','{"code":"L1"}','ACTIVE','importer','DRAFT'),
                   (@lookup || '_foreign',@app,@mod,@lookup,'LX','{"code":"LX"}','ACTIVE','outsider','DRAFT');
            """, conn);
        seed.Parameters.AddWithValue("tbl", table.Id); seed.Parameters.AddWithValue("app", app.Id);
        seed.Parameters.AddWithValue("mod", module.Id); seed.Parameters.AddWithValue("lookup", lookup.Id);
        await seed.ExecuteNonQueryAsync();
        return new Context(app.Id, module.Id, table.Id, lookup.Id);
    }

    private static Dictionary<string, object?> Row(string code, params (string Field, object? Value)[] changes)
    {
        var row = new Dictionary<string, object?> { ["code"] = code, ["title"] = "New title", ["amount"] = 10 };
        foreach (var change in changes) row[change.Field] = change.Value;
        return row;
    }
    private static BulkImportRequest Request(Context c, params Dictionary<string, object?>[] rows) =>
        new() { AppId = c.AppId, ModuleId = c.ModuleId, TableId = c.TableId, Rows = rows.ToList() };

    private async Task<ImportPreviewDto> PreviewAsync(BulkImportRequest request, string user = "importer")
    {
        using var response = await fixture.SendAsAsync(user, "/api/records/bulk-import/preview", HttpMethod.Post, request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("data").Deserialize<ImportPreviewDto>(JsonOptions)!;
    }
    private async Task<long> CountAsync(Context c)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString); await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM app_records WHERE table_id=@tbl", conn);
        cmd.Parameters.AddWithValue("tbl", c.TableId); return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [PostgresFact]
    public async Task Preview_DoesNotWrite_AndCommitPreservesUnmappedFieldsAndAudit()
    {
        var c = await CreateContextAsync();
        var request = Request(c, Row("NEW", ("amount", "1.234,50"), ("date", "17/11/2026"), ("flag", "có"), ("category", "A"), ("ref", "L1")),
            new Dictionary<string, object?> { ["code"] = "OLD", ["amount"] = "2.000,00" });
        request.RowNumbers = [5, 9]; request.NumberFormat = "vi-VN"; request.DateFormat = "dd/MM/yyyy";
        var preview = await PreviewAsync(request);
        Assert.True(preview.CanImport, JsonSerializer.Serialize(preview));
        Assert.Equal(1, preview.Inserted); Assert.Equal(1, preview.Updated);
        Assert.Equal(new[] { 5, 9 }, preview.Rows.Select(r => r.SourceRow));
        Assert.Equal(3, await CountAsync(c));
        Assert.Equal("2026-11-17", preview.Rows[0].Data["date"]!.ToString());
        Assert.Equal("Keep existing", preview.Rows[1].Data["title"]!.ToString());
        Assert.False(preview.Rows[1].Data.ContainsKey("secret"));
        request.PreviewFingerprint = preview.Fingerprint;
        using var commit = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.True(commit.IsSuccessStatusCode, await commit.Content.ReadAsStringAsync());
        Assert.Equal(4, await CountAsync(c));
        var records = fixture.Services.GetRequiredService<RecordService>();
        var old = (await records.GetRecordByIdAsync(c.TableId + "_old"))!;
        Assert.Equal("hidden-secret", old.Data["secret"]!.ToString());
        Assert.Equal("keep-protected", old.Data["protected"]!.ToString());
        Assert.Equal("4000", old.Data["total"]!.ToString());
        Assert.Single(await records.GetAuditLogsAsync(old.Id));
        using var replay = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(4, await CountAsync(c));
    }

    [PostgresFact]
    public async Task InvalidRows_AreReportedAtSourceLines_AndEntireBatchIsBlocked()
    {
        var c = await CreateContextAsync();
        var request = Request(c, Row("VALID"), Row("MISSING", ("title", "")), Row("NUMBER", ("amount", "one")), Row("EMAIL", ("email", "bad")),
            Row("DATE", ("date", "31/02/2026")), Row("CATEGORY", ("category", "BAD")), Row("LOOKUP", ("ref", "LX")),
            Row("DUPLICATE"), Row("duplicate"), Row("LOCKED"), Row("FOREIGN"), Row("PROTECTED", ("protected", "overwrite")),
            Row("SECRET", ("secret", "overwrite")), Row("UNKNOWN", ("missing_column", "x")), Row("BOOL", ("flag", "maybe")));
        request.RowNumbers = Enumerable.Range(0, request.Rows.Count).Select(i => 10 + i * 2).ToList();
        request.DateFormat = "dd/MM/yyyy";
        var preview = await PreviewAsync(request);
        Assert.False(preview.CanImport);
        Assert.Equal(14, preview.ErrorRows);
        Assert.Equal(10, preview.Rows[0].SourceRow);
        Assert.All(preview.Rows.Skip(1), r => Assert.Contains(r.Errors, e => e.Severity == "ERROR"));
        Assert.DoesNotContain("private-outside-title", JsonSerializer.Serialize(preview));
        Assert.DoesNotContain("outside-secret", JsonSerializer.Serialize(preview));
        request.PreviewFingerprint = preview.Fingerprint;
        using var commit = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
        Assert.Equal(3, await CountAsync(c));
    }

    [PostgresFact]
    public async Task Commit_RejectsChangedDataChangedSchemaAndMissingPreview()
    {
        var c = await CreateContextAsync();
        var request = Request(c, Row("OLD"));
        using var noPreview = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.BadRequest, noPreview.StatusCode);
        request.PreviewFingerprint = (await PreviewAsync(request)).Fingerprint;
        await fixture.ExecuteAsync($"UPDATE app_records SET data=data || '{{\"amount\":6}}'::jsonb, updated_at=CURRENT_TIMESTAMP WHERE id='{c.TableId}_old'");
        using var stale = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        request = Request(c, Row("NEW"));
        request.PreviewFingerprint = (await PreviewAsync(request)).Fingerprint;
        await fixture.Services.GetRequiredService<MetadataService>().UpsertColumnAsync(new() { TableId = c.TableId, ColumnCode = "new_required", ColumnName = "New required", IsRequired = true });
        using var schemaChanged = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.BadRequest, schemaChanged.StatusCode);
        Assert.Equal(3, await CountAsync(c));
    }

    [PostgresFact]
    public async Task Import_ValidatesActualTablePermissionsAndUpdateMode()
    {
        var c = await CreateContextAsync();
        var request = Request(c, Row("OLD"));
        Assert.False((await PreviewAsync(request, "creator")).CanImport);
        request.UpsertByIdentityKey = false;
        Assert.False((await PreviewAsync(request)).CanImport);
        request = Request(c, Row("NEW"));
        request.PreviewFingerprint = (await PreviewAsync(request)).Fingerprint;
        await fixture.ExecuteAsync("UPDATE sys_users SET permissions='[\"record.view\"]'::jsonb WHERE username='importer'");
        using var revoked = await fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using var anonymous = await fixture.Client.PostAsync("/api/records/bulk-import/preview", System.Net.Http.Json.JsonContent.Create(request));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(3, await CountAsync(c));
    }

    [PostgresFact]
    public async Task Preview_RejectsDefaultIdentityCollisionAndForgedTableLocation()
    {
        var c = await CreateContextAsync();
        var metadata = fixture.Services.GetRequiredService<MetadataService>();
        var identity = (await metadata.GetColumnsAsync(c.TableId)).Single(col => col.ColumnCode == "code");
        await metadata.UpsertColumnAsync(new() { Id = identity.Id, TableId = c.TableId, ColumnCode = "code", ColumnName = "Code", IsIdentityKey = true, IsRequired = true, DefaultValue = "OLD" });
        var data = Row("NEW"); data.Remove("code");
        var request = Request(c, data);
        var preview = await PreviewAsync(request);
        Assert.False(preview.CanImport);
        Assert.Contains(preview.Rows[0].Errors, e => e.Field == "code" && e.Severity == "ERROR");
        request.ModuleId = "wrong-module";
        using var forged = await fixture.SendAsAsync("importer", "/api/records/bulk-import/preview", HttpMethod.Post, request);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(3, await CountAsync(c));
    }

    [PostgresFact]
    public async Task AutoNumberPreview_IsDeterministic_AndConcurrentCommitsDoNotDuplicateBatch()
    {
        var c = await CreateContextAsync();
        var metadata = fixture.Services.GetRequiredService<MetadataService>();
        var identity = (await metadata.GetColumnsAsync(c.TableId)).Single(col => col.ColumnCode == "code");
        await metadata.UpsertColumnAsync(new() { Id = identity.Id, TableId = c.TableId, ColumnCode = "code", ColumnName = "Code", DataType = "AUTO_NUMBER", IsIdentityKey = true, IsRequired = true, DefaultValue = "IM-{YYYY}-" });
        var request = Request(c, Row("", ("code", null)), Row("", ("code", null)));
        var preview = await PreviewAsync(request);
        Assert.True(preview.CanImport, JsonSerializer.Serialize(preview));
        Assert.NotEqual(preview.Rows[0].RecordKey, preview.Rows[1].RecordKey);
        Assert.Equal(preview.Fingerprint, (await PreviewAsync(request)).Fingerprint);
        request.PreviewFingerprint = preview.Fingerprint;
        var responses = await Task.WhenAll(
            fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request),
            fixture.SendAsAsync("importer", "/api/records/bulk-import", HttpMethod.Post, request));
        try
        {
            Assert.Single(responses, r => r.IsSuccessStatusCode);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(5, await CountAsync(c));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
