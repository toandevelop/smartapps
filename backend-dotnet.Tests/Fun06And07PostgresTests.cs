using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Fun06And07PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Fun06And07PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task Fun07_OrgUnits_SupportsHierarchyCycleGuardAndRecursiveSubtreeUsers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var orgs = _fx.Services.GetRequiredService<OrgUnitService>();

        var root = await orgs.UpsertOrgUnitAsync(new UpsertOrgUnitRequest
        {
            Code = $"ROOT_{suffix}",
            Name = $"Khối Kinh doanh {suffix}",
            ManagerUsername = "admin"
        });
        var child = await orgs.UpsertOrgUnitAsync(new UpsertOrgUnitRequest
        {
            Code = $"SUB_{suffix}",
            Name = $"Phòng Bán hàng {suffix}",
            ParentId = root.Id
        });

        // Cycle guard: root cannot become a child of its own descendant
        await Assert.ThrowsAsync<InvalidOperationException>(() => orgs.UpsertOrgUnitAsync(new UpsertOrgUnitRequest
        {
            Id = root.Id,
            Code = root.Code,
            Name = root.Name,
            ParentId = child.Id
        }));

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO sys_users (id, username, full_name, role_code, password_hash, org_unit_id, is_active)
            VALUES (@id1, @u1, 'User 1', 'STAFF', 'x', @o1, TRUE),
                   (@id2, @u2, 'User 2', 'STAFF', 'x', @o2, TRUE)
            """, conn))
        {
            ins.Parameters.AddWithValue("id1", $"u1_{suffix}");
            ins.Parameters.AddWithValue("u1", $"user1_{suffix}");
            ins.Parameters.AddWithValue("o1", root.Id);
            ins.Parameters.AddWithValue("id2", $"u2_{suffix}");
            ins.Parameters.AddWithValue("u2", $"user2_{suffix}");
            ins.Parameters.AddWithValue("o2", child.Id);
            await ins.ExecuteNonQueryAsync();
        }

        var subtreeUsers = await orgs.GetSubtreeUsernamesAsync(root.Id);
        Assert.Contains($"user1_{suffix}", subtreeUsers);
        Assert.Contains($"user2_{suffix}", subtreeUsers);
    }

    [PostgresFact]
    public async Task Fun06_QueryBuilder_SupportsOrLogicNumericComparisonDatePresetAndAiSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var ai = _fx.Services.GetRequiredService<AiService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_q_{suffix}", Code = $"Q_{suffix}".ToUpperInvariant(), Name = $"Q {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_q_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_q_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"q_{suffix}", DisplayName = "Orders" });
        var col1 = await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "code", ColumnName = "Code", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 });
        var col2 = await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER", OrderIndex = 2 });
        var col3 = await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "order_date", ColumnName = "Order Date", DataType = "DATE", OrderIndex = 3 });

        var todayStr = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd");
        var (r1, _) = await records.UpsertRecordAsync(new UpsertRecordRequest { AppId = app.Id, ModuleId = mod.Id, TableId = tbl.Id, Data = new() { ["code"] = "A1", ["amount"] = 100, ["order_date"] = todayStr } }, "admin", true);
        await records.UpsertRecordAsync(new UpsertRecordRequest { AppId = app.Id, ModuleId = mod.Id, TableId = tbl.Id, Data = new() { ["code"] = "B2", ["amount"] = 500, ["order_date"] = "2020-01-01" } }, "admin", true);
        await records.UpsertRecordAsync(new UpsertRecordRequest { AppId = app.Id, ModuleId = mod.Id, TableId = tbl.Id, Data = new() { ["code"] = "C3", ["amount"] = 50, ["order_date"] = "2020-01-01" } }, "admin", true);

        // OR filter: amount >= 500 OR order_date = TODAY -> matches A1 and B2 (2 records, excludes C3)
        var orFilter = JsonSerializer.Serialize(new
        {
            logic = "OR",
            conditions = new object[]
            {
                new { field = "amount", op = "gte", value = "500" },
                new { field = "order_date", op = "date_preset", value = "TODAY" }
            }
        });
        var (matched, total) = await records.QueryRecordsAsync(app.Id, null, tbl.Id, null, "ALL", 1, 20, filtersJson: orFilter);
        Assert.Equal(2, total);
        Assert.Contains(matched, r => r.RecordKey == "A1");
        Assert.Contains(matched, r => r.RecordKey == "B2");

        // AI Summary fallback produces structured Vietnamese summary
        var (summary, engine) = await ai.SummarizeRecordAsync(r1, tbl.DisplayName, new List<SysColumnDto> { col1, col2, col3 }, "admin");
        Assert.Contains("A1", summary);
        Assert.False(string.IsNullOrWhiteSpace(engine));
    }
}
