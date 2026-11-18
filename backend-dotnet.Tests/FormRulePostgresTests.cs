using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class FormRulePostgresTests(PriorityPostgresFixture fixture) : IClassFixture<PriorityPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Context(SysTableDto Table, List<FormRuleDto> Rules);
    private MetadataService Metadata => fixture.Services.GetRequiredService<MetadataService>();

    private static UpsertTableRequest TableRequest(Context c, List<FormRuleDto>? rules) => new() {
        Id = c.Table.Id, AppId = c.Table.AppId, ModuleId = c.Table.ModuleId, TableName = c.Table.TableName,
        DisplayName = c.Table.DisplayName, RowScope = "OWNER_ONLY", ApprovalEnabled = true, PublicFormEnabled = true, FormRules = rules
    };
    private async Task<Context> CreateContextAsync()
    {
        var app = (await Metadata.GetAppByCodeOrIdAsync("PRIORITY"))!;
        var module = (await Metadata.GetModulesAsync(app.Id)).First();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = await Metadata.UpsertTableAsync(new() { AppId = app.Id, ModuleId = module.Id, TableName = "rules_" + suffix,
            DisplayName = "Rules " + suffix, RowScope = "OWNER_ONLY", PublicFormEnabled = true, ApprovalEnabled = true });
        var columns = FormRuleTests.Columns();
        columns.Add(new() { ColumnCode = "code", ColumnName = "Code", IsIdentityKey = true, IsRequired = true });
        columns.Add(new() { ColumnCode = "details", ColumnName = "Details", DefaultValue = "DEFAULT" });
        columns.Add(new() { ColumnCode = "roleOnly", ColumnName = "Role only", DefaultValue = "ROLE", ReadOnlyForRoles = ["RULE_USER"] });
        foreach (var col in columns)
            await Metadata.UpsertColumnAsync(new() { TableId = table.Id, ColumnCode = col.ColumnCode, ColumnName = col.ColumnName,
                DataType = col.DataType, IsIdentityKey = col.IsIdentityKey, IsRequired = col.IsRequired,
                DefaultValue = col.DefaultValue, ReadOnlyForRoles = col.ReadOnlyForRoles });
        List<FormRuleDto> rules = [
            FormRuleTests.Rule("details", "visible", "kind", "EQUALS", "BUSINESS", false),
            FormRuleTests.Rule("note", "required", "amount", "GREATER_OR_EQUAL", "100", false),
            FormRuleTests.Rule("protected", "readOnly", "locked", "EQUALS", "true", false)
        ];
        await Metadata.SetFormRulesAsync(table.Id, rules);
        await fixture.ExecuteAsync("""
            INSERT INTO sys_users(id,username,full_name,role_code,role_codes,password_hash,allowed_apps,permissions)
            VALUES ('rule_user','rule_user','Rule user','RULE_USER','["RULE_USER"]','unused','["PRIORITY"]','["record.view","record.create","record.edit"]')
            ON CONFLICT(username) DO UPDATE SET permissions=EXCLUDED.permissions;
            """);
        return new Context((await Metadata.GetTablesAsync(app.Id, module.Id)).Single(t => t.Id == table.Id), rules);
    }
    private static Dictionary<string, object?> Data(string code, params (string Field, object? Value)[] changes)
    {
        var result = new Dictionary<string, object?> { ["code"] = code, ["kind"] = "PERSONAL", ["amount"] = 0, ["locked"] = false };
        foreach (var change in changes) result[change.Field] = change.Value;
        return result;
    }
    private async Task<(HttpStatusCode Status, JsonElement Json)> SendAsync(string path, object? body = null, string user = "rule_user", HttpMethod? method = null)
    {
        using var response = await fixture.SendAsAsync(user, path, method ?? HttpMethod.Post, body);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }
    private Task<(HttpStatusCode Status, JsonElement Json)> SaveAsync(Context c, Dictionary<string, object?> data, string? id = null) => SendAsync("/api/records",
        new UpsertRecordRequest { AppId = c.Table.AppId, ModuleId = c.Table.ModuleId, TableId = c.Table.Id, Id = id, Data = data });
    private async Task<long> CountAsync(Context c)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString); await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM app_records WHERE table_id=@table", conn);
        cmd.Parameters.AddWithValue("table", c.Table.Id); return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [PostgresFact]
    public async Task Defaults_AreNormalizedBeforeRequiredAndReadOnlyChecksOnEveryPublicAndRuntimeWrite()
    {
        var c = await CreateContextAsync();
        var source = (await Metadata.GetColumnsAsync(c.Table.Id)).Single(col => col.ColumnCode == "locked");
        await Metadata.UpsertColumnAsync(new() { Id = source.Id, TableId = c.Table.Id, ColumnCode = source.ColumnCode,
            ColumnName = source.ColumnName, DataType = "BOOLEAN", DefaultValue = "1" });
        c.Rules[1] = FormRuleTests.Rule("note", "required", "locked", "EQUALS", "true", false);
        await Metadata.SetFormRulesAsync(c.Table.Id, c.Rules);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, new() { ["code"] = "DEFAULT-BAD" })).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, new() { ["code"] = "DEFAULT-TAMPER", ["note"] = "OK", ["protected"] = "HACK" })).Status);
        var saved = await SaveAsync(c, new() { ["code"] = "DEFAULT-RUNTIME", ["note"] = "OK" });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.True(saved.Json.GetProperty("data").GetProperty("data").GetProperty("locked").GetBoolean());
        for (var i = 0; i < 2; i++)
        {
            using var invalid = await fixture.Client.PostAsJsonAsync($"/api/public/forms/{c.Table.Id}/submit", new { code = $"DEFAULT-PUBLIC-{i}" });
            Assert.Equal((HttpStatusCode)422, invalid.StatusCode);
            using var tampered = await fixture.Client.PostAsJsonAsync($"/api/public/forms/{c.Table.Id}/submit",
                new Dictionary<string, object?> { ["code"] = $"DEFAULT-PUBLIC-{i}", ["note"] = "OK", ["protected"] = "HACK" });
            Assert.Equal((HttpStatusCode)422, tampered.StatusCode);
            using var valid = await fixture.Client.PostAsJsonAsync($"/api/public/forms/{c.Table.Id}/submit", new { code = $"DEFAULT-PUBLIC-{i}", note = "OK" });
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            using var result = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            var record = (await fixture.Services.GetRequiredService<RecordService>().GetRecordByIdAsync(result.RootElement.GetProperty("data").GetProperty("id").GetString()!))!;
            Assert.Equal("True", record.Data["locked"]!.ToString());
            Assert.Equal("SERVER", record.Data["protected"]!.ToString());
        }
        Assert.Equal(3, await CountAsync(c));
    }

    [PostgresFact]
    public async Task StudioRules_PersistPreserveOtherConfigAndRejectBrokenDefinitions()
    {
        var c = await CreateContextAsync();
        await fixture.ExecuteAsync($"UPDATE sys_tables SET ui_config=ui_config || '{{\"layout\":{{\"columns\":2}}}}'::jsonb WHERE id='{c.Table.Id}'");
        var saved = await SendAsync("/api/tables", TableRequest(c, c.Rules), "admin");
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(3, saved.Json.GetProperty("data").GetProperty("formRules").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("/api/tables", TableRequest(c, []))).Status);
        var invalid = c.Rules.Concat([FormRuleTests.Rule("note", "required", "kind", "EQUALS", "BUSINESS")]).ToList();
        invalid[^1].Id = "duplicate-property";
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("/api/tables", TableRequest(c, invalid), "admin")).Status);
        await Metadata.UpsertTableAsync(TableRequest(c, null));
        var table = (await Metadata.GetTablesAsync(null, null)).Single(t => t.Id == c.Table.Id);
        Assert.Equal(3, table.FormRules.Count);
        Assert.Equal(2, table.UiConfig.GetProperty("layout").GetProperty("columns").GetInt32());
        var source = (await Metadata.GetColumnsAsync(table.Id)).Single(col => col.ColumnCode == "kind");
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("/api/columns/" + source.Id, user: "admin", method: HttpMethod.Delete)).Status);
        await Metadata.SetFormRulesAsync(table.Id, []);
        Assert.Empty((await Metadata.GetTablesAsync(null, null)).Single(t => t.Id == table.Id).FormRules);
    }

    [PostgresFact]
    public async Task RecordWrites_EnforceConditionalRequiredHiddenAndReadOnlyInBothStates()
    {
        var c = await CreateContextAsync();
        var invalid = await SaveAsync(c, Data("R1", ("kind", "BUSINESS"), ("amount", 150)));
        Assert.Equal((HttpStatusCode)422, invalid.Status); Assert.Equal(0, await CountAsync(c));
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, Data("BAD-NUMBER", ("amount", "1,000")))).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, Data("BAD-BOOLEAN", ("locked", "unknown"), ("protected", "HACK")))).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, Data("BAD-OBJECT", ("kind", new Dictionary<string, object?> { ["value"] = "BUSINESS" })))).Status);
        Assert.Equal(0, await CountAsync(c));
        var created = await SaveAsync(c, Data("R1", ("kind", "BUSINESS"), ("amount", 150), ("note", "OK"), ("details", "Keep this")));
        Assert.Equal(HttpStatusCode.OK, created.Status);
        var id = created.Json.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, Data("R1"), id)).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, new() { ["details"] = "HACK" }, id)).Status);
        Assert.Equal((HttpStatusCode)422, (await SendAsync($"/api/records/{id}/cell", new { field = "details", value = "HACK" }, method: HttpMethod.Patch)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, new() { ["locked"] = true }, id)).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, new() { ["locked"] = false, ["protected"] = "HACK" }, id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, new() { ["locked"] = false }, id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, new() { ["protected"] = "EDIT" }, id)).Status);
        Assert.Equal((HttpStatusCode)422, (await SaveAsync(c, new() { ["locked"] = true, ["protected"] = "HACK" }, id)).Status);
        var record = (await fixture.Services.GetRequiredService<RecordService>().GetRecordByIdAsync(id))!;
        Assert.Equal("Keep this", record.Data["details"]!.ToString());
        Assert.Equal("EDIT", record.Data["protected"]!.ToString());
        await fixture.ExecuteAsync($"UPDATE app_records SET data=data || '{{\"amount\":150,\"note\":null}}'::jsonb WHERE id='{id}'");
        Assert.Equal((HttpStatusCode)422, (await SendAsync($"/api/records/{id}/submit-approval")).Status);
        // A form using cached rules may omit fields after Studio has cleared those rules.
        await Metadata.SetFormRulesAsync(c.Table.Id, []);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, new() { ["amount"] = 1 }, id)).Status);
        var preserved = (await fixture.Services.GetRequiredService<RecordService>().GetRecordByIdAsync(id))!;
        Assert.Equal("Keep this", preserved.Data["details"]!.ToString());
        Assert.Equal("EDIT", preserved.Data["protected"]!.ToString());
    }

    [PostgresFact]
    public async Task PublicAndExternalForms_UseTheSameValidation()
    {
        var c = await CreateContextAsync();
        using var publicDefinition = await fixture.Client.GetAsync($"/api/public/forms/{c.Table.Id}");
        using var definition = JsonDocument.Parse(await publicDefinition.Content.ReadAsStringAsync());
        Assert.Equal(3, definition.RootElement.GetProperty("data").GetProperty("table").GetProperty("formRules").GetArrayLength());
        using var publicBad = await fixture.Client.PostAsJsonAsync($"/api/public/forms/{c.Table.Id}/submit", Data("PUBLIC-BAD", ("amount", 200)));
        Assert.Equal((HttpStatusCode)422, publicBad.StatusCode);
        using var publicGood = await fixture.Client.PostAsJsonAsync($"/api/public/forms/{c.Table.Id}/submit", Data("PUBLIC-GOOD"));
        Assert.Equal(HttpStatusCode.OK, publicGood.StatusCode);
        var (_, rawKey) = await fixture.Services.GetRequiredService<IntegrationService>().CreateApiKeyAsync(new() { Name = "Rules", AppId = c.Table.AppId, TableId = c.Table.Id }, "admin");
        using var externalRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/external/tables/{c.Table.Id}/records") {
            Content = JsonContent.Create(Data("EXTERNAL-BAD", ("locked", true), ("protected", "HACK")))
        };
        externalRequest.Headers.Add("X-Api-Key", rawKey);
        using var externalBad = await fixture.Client.SendAsync(externalRequest);
        Assert.Equal((HttpStatusCode)422, externalBad.StatusCode);
        Assert.Equal(1, await CountAsync(c));
    }

    [PostgresFact]
    public async Task ImportAndRolePermissions_CannotBypassRules()
    {
        var c = await CreateContextAsync();
        var request = new BulkImportRequest { AppId = c.Table.AppId, ModuleId = c.Table.ModuleId, TableId = c.Table.Id, Rows = [Data("IMPORT", ("amount", 200))] };
        var bad = await SendAsync("/api/records/bulk-import/preview", request);
        Assert.Equal(1, bad.Json.GetProperty("data").GetProperty("errorRows").GetInt32());
        request.Rows[0]["note"] = "OK";
        var good = await SendAsync("/api/records/bulk-import/preview", request);
        Assert.True(good.Json.GetProperty("data").GetProperty("canImport").GetBoolean());
        request.PreviewFingerprint = good.Json.GetProperty("data").GetProperty("fingerprint").GetString();
        c.Rules[1].Conditions[0].Value = "50";
        await Metadata.SetFormRulesAsync(c.Table.Id, c.Rules);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("/api/records/bulk-import", request)).Status);
        Assert.Equal(0, await CountAsync(c));
        var roleRule = FormRuleTests.Rule("roleonly", "readOnly", "kind", "EQUALS", "PERSONAL", false);
        roleRule.Value = false;
        await Metadata.SetFormRulesAsync(c.Table.Id, c.Rules.Concat([roleRule]).ToList());
        var role = await SaveAsync(c, Data("ROLE", ("roleonly", "HACK")));
        Assert.Equal(HttpStatusCode.OK, role.Status);
        Assert.Equal("ROLE", role.Json.GetProperty("data").GetProperty("data").GetProperty("roleonly").GetString());
    }

    [PostgresFact]
    public async Task ServerBoundPersonalFields_RemainAuthoritativeWhenConditionallyHidden()
    {
        var c = await CreateContextAsync();
        await Metadata.UpsertColumnAsync(new() { TableId = c.Table.Id, ColumnCode = "nhan_vien", ColumnName = "Nhân viên" });
        c.Rules.Add(FormRuleTests.Rule("nhan_vien", "visible", "kind", "EQUALS", "BUSINESS", false));
        await Metadata.SetFormRulesAsync(c.Table.Id, c.Rules);
        await fixture.ExecuteAsync("UPDATE sys_users SET employee_code='EMP-RULE' WHERE username='rule_user'");
        var saved = await SaveAsync(c, Data("OWN", ("nhan_vien", "SPOOF")));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal("EMP-RULE", saved.Json.GetProperty("data").GetProperty("data").GetProperty("nhan_vien").GetString());
    }

    [PostgresFact]
    public async Task Blueprint_CloningPreservesRulesAndStaticReadOnlyRoles()
    {
        var c = await CreateContextAsync();
        var blueprint = (await Metadata.ExportAppBlueprintAsync(c.Table.AppId))!;
        var clone = await Metadata.ImportAppBlueprintAsync(blueprint, "RULECLONE" + Guid.NewGuid().ToString("N")[..5]);
        var clonedTable = (await Metadata.GetTablesAsync(clone.Id, null)).Single(t => t.DisplayName == c.Table.DisplayName);
        Assert.Equal(3, clonedTable.FormRules.Count);
        Assert.Contains("RULE_USER", (await Metadata.GetColumnsAsync(clonedTable.Id)).Single(col => col.ColumnCode == "roleonly").ReadOnlyForRoles);
    }

    [PostgresFact]
    public async Task Automation_FailsInvalidTargetAndReadonlyPatchWithoutMisreportingSourceSave()
    {
        var c = await CreateContextAsync();
        var sourceTable = await Metadata.UpsertTableAsync(new() { AppId = c.Table.AppId, ModuleId = c.Table.ModuleId,
            TableName = "source_" + Guid.NewGuid().ToString("N")[..8], DisplayName = "Source" });
        await Metadata.UpsertColumnAsync(new() { TableId = sourceTable.Id, ColumnCode = "target", ColumnName = "Target" });
        var automation = fixture.Services.GetRequiredService<AutomationService>();
        var ruleRequest = new UpsertAutomationRequest { Name = "Rules target", SourceAppId = c.Table.AppId, SourceTableId = sourceTable.Id,
            TargetAppId = c.Table.AppId, TargetModuleId = c.Table.ModuleId, TargetTableId = c.Table.Id,
            ActionType = "CREATE_RECORD", FieldMappings = new() { ["code"] = "AUTO", ["amount"] = "200", ["kind"] = "BUSINESS" } };
        var rule = await automation.UpsertAutomationAsync(ruleRequest);
        var records = fixture.Services.GetRequiredService<RecordService>();
        async Task Trigger() => await records.UpsertRecordAsync(new() { AppId = c.Table.AppId, ModuleId = c.Table.ModuleId,
            TableId = sourceTable.Id, Data = new() { ["target"] = "AUTO" } }, "admin", true);
        await Trigger();
        Assert.Equal(0, await CountAsync(c));
        Assert.Contains(await automation.GetLogsAsync(), log => log.AutomationId == rule.Id && log.Status == "FAILED");
        ruleRequest.Id = rule.Id; ruleRequest.FieldMappings["note"] = "OK";
        await automation.UpsertAutomationAsync(ruleRequest); await Trigger();
        Assert.Equal(1, await CountAsync(c));
        await automation.ToggleAutomationAsync(rule.Id, false);
        await fixture.ExecuteAsync($"UPDATE app_records SET data=data || '{{\"locked\":true}}'::jsonb WHERE table_id='{c.Table.Id}'");
        var patch = await automation.UpsertAutomationAsync(new() { Name = "Blocked patch", SourceAppId = c.Table.AppId, SourceTableId = sourceTable.Id,
            TargetTableId = c.Table.Id, TargetAppId = c.Table.AppId, ActionType = "UPDATE_RECORD", MatchSourceField = "target", MatchTargetField = "code",
            FieldMappings = new() { ["protected"] = "HACK" } });
        await Trigger();
        Assert.Contains(await automation.GetLogsAsync(), log => log.AutomationId == patch.Id && log.Status == "FAILED");
        var (items, _) = await records.QueryRecordsAsync(null, null, c.Table.Id, null, null);
        Assert.Equal("SERVER", items.Single().Data["protected"]!.ToString());
    }
}
