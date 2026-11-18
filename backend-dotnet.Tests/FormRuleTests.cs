using System.Text.Json;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class FormRuleTests
{
    public static IEnumerable<object[]> SharedDefaults()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "form-default-cases.json")));
        foreach (var item in doc.RootElement.EnumerateArray()) yield return [item.GetProperty("name").GetString()!, item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(SharedDefaults))]
    public void Defaults_MatchFrontendBeforeEvaluatingConditions(string name, string json)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var column = new SysColumnDto { ColumnCode = "source", DataType = root.GetProperty("type").GetString()!, DefaultValue = root.GetProperty("raw").GetString() };
        var data = FormRuleService.MergeWithDefaults([column], new(), null);
        if (root.TryGetProperty("omitted", out var omitted) && omitted.GetBoolean()) Assert.False(data.ContainsKey("source"));
        else Assert.Equal(root.GetProperty("expected").GetRawText(), JsonSerializer.Serialize(data["source"]));
    }

    public static IEnumerable<object[]> SharedCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "form-rule-cases.json")));
        foreach (var item in doc.RootElement.EnumerateArray()) yield return [item.GetProperty("name").GetString()!, item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void Conditions_MatchSharedFrontendCases(string name, string json)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var condition = new FormConditionDto { Field = "source", Operator = root.GetProperty("operator").GetString()!,
            Value = root.TryGetProperty("expectedValue", out var expected) ? expected.GetString() : null };
        Assert.Equal(root.GetProperty("expected").GetBoolean(), FormRuleService.Matches(condition, root.GetProperty("type").GetString()!, root.GetProperty("actual")));
    }

    internal static List<SysColumnDto> Columns() => [
        new() { ColumnCode = "kind", ColumnName = "Kind", DataType = "STRING" },
        new() { ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER" },
        new() { ColumnCode = "locked", ColumnName = "Locked", DataType = "BOOLEAN" },
        new() { ColumnCode = "note", ColumnName = "Note", DataType = "STRING" },
        new() { ColumnCode = "protected", ColumnName = "Protected", DataType = "STRING", DefaultValue = "SERVER" }
    ];
    internal static FormRuleDto Rule(string target, string property, string source, string op, string value, bool? otherwise = null) =>
        new() { Id = target + property, TargetField = target, Property = property, Value = true, ElseValue = otherwise,
            Conditions = [new() { Field = source, Operator = op, Value = value }] };

    [Fact]
    public void Evaluation_ResetsToBaselineSupportsAnyAndMasksHiddenRequired()
    {
        var columns = Columns();
        var required = Rule("note", "required", "amount", "GREATER_OR_EQUAL", "100", false);
        required.Match = "ANY";
        required.Conditions.Add(new() { Field = "kind", Operator = "EQUALS", Value = "BUSINESS" });
        var visible = Rule("note", "visible", "kind", "EQUALS", "BUSINESS", false);
        List<FormRuleDto> rules = [required, visible];
        FormRuleService.ValidateDefinition(rules, columns);
        var data = new Dictionary<string, object?> { ["amount"] = 200, ["kind"] = "PERSONAL" };
        var hidden = FormRuleService.Evaluate(columns, rules, data)["note"];
        Assert.False(hidden.Visible); Assert.True(hidden.Required);
        var result = new DynamicComputeService().EvaluateAndValidate(columns, new(), [("r", data)], rules).Single();
        Assert.True(result.IsValid);
        data["kind"] = "BUSINESS";
        Assert.Throws<FormRuleValidationException>(() => FormRuleService.EnforceWrite(columns, rules, data, null, data));
        data["amount"] = 1; data["kind"] = "PERSONAL";
        Assert.False(FormRuleService.Evaluate(columns, rules, data)["note"].Required);
    }

    [Fact]
    public void ReadOnly_ProtectsOldAndNewStateAndAllowsServerDefault()
    {
        var columns = Columns();
        List<FormRuleDto> rules = [Rule("protected", "readOnly", "locked", "EQUALS", "true", false)];
        var old = new Dictionary<string, object?> { ["locked"] = true, ["protected"] = "SERVER" };
        var tampered = new Dictionary<string, object?> { ["locked"] = false, ["protected"] = "changed" };
        Assert.Throws<FormRuleValidationException>(() => FormRuleService.EnforceWrite(columns, rules, tampered, old, tampered));
        var same = new Dictionary<string, object?> { ["locked"] = true, ["protected"] = "SERVER" };
        FormRuleService.EnforceWrite(columns, rules, same, null, same);
        var merged = FormRuleService.MergeWithDefaults(columns, new() { ["locked"] = false }, old);
        Assert.Equal("SERVER", merged["protected"]);
    }

    [Fact]
    public void Definitions_RejectUnknownSelfConflictingAndProtectedDependencies()
    {
        var columns = Columns();
        var rule = Rule("note", "required", "missing", "EQUALS", "x");
        Assert.Throws<FormRuleConfigurationException>(() => FormRuleService.ValidateDefinition([rule], columns));
        rule.Conditions[0].Field = "note";
        Assert.Throws<FormRuleConfigurationException>(() => FormRuleService.ValidateDefinition([rule], columns));
        rule.Conditions[0].Field = "kind";
        var duplicate = Rule("note", "required", "amount", "GREATER_THAN", "10"); duplicate.Id = "other";
        Assert.Throws<FormRuleConfigurationException>(() => FormRuleService.ValidateDefinition([rule, duplicate], columns));
        columns[0].HiddenForRoles = ["STAFF"];
        Assert.Throws<FormRuleConfigurationException>(() => FormRuleService.ValidateDefinition([rule], columns));
    }
}
