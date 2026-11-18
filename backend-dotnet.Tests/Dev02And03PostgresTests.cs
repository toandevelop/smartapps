using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Dev02And03PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Dev02And03PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    private async Task<(string AppId, string ModuleId, string TableId)> CreateAutoNumberTableAsync(string suffix)
    {
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var app = await meta.UpsertAppAsync(new UpsertAppRequest
        {
            Id = $"app_dev_{suffix}",
            Code = $"DEV_{suffix}".ToUpperInvariant(),
            Name = $"DEV {suffix}"
        });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest
        {
            Id = $"mod_dev_{suffix}",
            AppId = app.Id,
            Code = "MAIN",
            Name = "Main"
        });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_dev_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"dev_{suffix}",
            DisplayName = $"DEV {suffix}",
            ApprovalEnabled = true,
            ApproverRoles = new List<string> { "ADMIN" }
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_code_{suffix}",
            TableId = tbl.Id,
            ColumnCode = "ma_phieu",
            ColumnName = "Mã phiếu",
            DataType = "AUTO_NUMBER",
            DefaultValue = $"DEV{suffix}-",
            IsIdentityKey = true,
            OrderIndex = 1
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_title_{suffix}",
            TableId = tbl.Id,
            ColumnCode = "tieu_de",
            ColumnName = "Tiêu đề",
            DataType = "STRING",
            OrderIndex = 2
        });
        return (app.Id, mod.Id, tbl.Id);
    }

    [PostgresFact]
    public async Task AutoNumber_IsUniqueUnderConcurrency_AndDoesNotReuseAfterHardDelete()
    {
        var (appId, modId, tblId) = await CreateAutoNumberTableAsync(Guid.NewGuid().ToString("N")[..6]);
        var records = _fx.Services.GetRequiredService<RecordService>();

        var tasks = Enumerable.Range(0, 6).Select(i => records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = appId,
            ModuleId = modId,
            TableId = tblId,
            Data = new Dictionary<string, object?> { ["tieu_de"] = $"Item {i}" }
        }, "admin", true)).ToArray();

        var results = await Task.WhenAll(tasks);
        var keys = results.Select(r => r.Record.RecordKey).ToList();
        Assert.Equal(6, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Hard delete the last created record and create another; number must advance, not reuse.
        var lastId = results.OrderBy(r => r.Record.RecordKey).Last().Record.Id;
        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var del = new NpgsqlCommand("DELETE FROM app_records WHERE id = @id", conn);
            del.Parameters.AddWithValue("id", lastId);
            await del.ExecuteNonQueryAsync();
        }

        var (afterDelete, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = appId,
            ModuleId = modId,
            TableId = tblId,
            Data = new Dictionary<string, object?> { ["tieu_de"] = "After delete" }
        }, "admin", true);

        Assert.DoesNotContain(afterDelete.RecordKey, keys);
        Assert.EndsWith("0007", afterDelete.RecordKey);
    }

    [PostgresFact]
    public async Task OptimisticConcurrency_RejectsStaleUpdateAndConcurrentApproval()
    {
        var (appId, modId, tblId) = await CreateAutoNumberTableAsync(Guid.NewGuid().ToString("N")[..6]);
        var records = _fx.Services.GetRequiredService<RecordService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();

        await workflows.UpsertWorkflowAsync(new UpsertWorkflowRequest
        {
            Id = $"wf_{tblId}",
            AppId = appId,
            TableId = tblId,
            Name = "2-step",
            Steps = new List<WorkflowStepDto>
            {
                new() { StepOrder = 1, Name = "Bước 1", ApproverRoles = new List<string> { "ADMIN" } },
                new() { StepOrder = 2, Name = "Bước 2", ApproverRoles = new List<string> { "ADMIN" } }
            }
        });

        var (created, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = appId,
            ModuleId = modId,
            TableId = tblId,
            Data = new Dictionary<string, object?> { ["tieu_de"] = "V1" }
        }, "admin", true);
        Assert.Equal(1, created.Version);

        // Update with current version -> succeeds and increments to 2
        var (updated, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            Id = created.Id,
            AppId = appId,
            ModuleId = modId,
            TableId = tblId,
            Data = new Dictionary<string, object?> { ["tieu_de"] = "V2", ["ma_phieu"] = created.RecordKey },
            ExpectedVersion = created.Version
        }, "admin", true);
        Assert.Equal(2, updated.Version);

        // Update with stale version 1 -> throws RecordConflictException
        await Assert.ThrowsAsync<RecordConflictException>(() => records.UpsertRecordAsync(new UpsertRecordRequest
        {
            Id = created.Id,
            AppId = appId,
            ModuleId = modId,
            TableId = tblId,
            Data = new Dictionary<string, object?> { ["tieu_de"] = "Stale", ["ma_phieu"] = created.RecordKey },
            ExpectedVersion = 1
        }, "admin", true));

        // Submit for approval -> version 3, step 1
        var submitted = await records.SubmitApprovalAsync(created.Id, "admin");
        Assert.NotNull(submitted);
        Assert.Equal("PENDING", submitted!.ApprovalStatus);
        Assert.Equal(1, submitted.CurrentStepOrder);

        // First approver approves step 1
        var adminUser = new SysUserDto { Username = "admin", RoleCode = "ADMIN", RoleCodes = new List<string> { "ADMIN" } };
        var (step1Approved, err) = await records.ProcessApprovalAsync(
            created.Id, adminUser, true, "OK", submitted.Version, submitted.CurrentStepOrder);
        Assert.Null(err);
        Assert.NotNull(step1Approved);
        Assert.Equal(2, step1Approved!.CurrentStepOrder);

        // Second approver tries to approve step 1 with stale version/step -> conflict!
        await Assert.ThrowsAsync<RecordConflictException>(() => records.ProcessApprovalAsync(
            created.Id, adminUser, true, "Duplicate", submitted.Version, submitted.CurrentStepOrder));
    }

    [PostgresFact]
    public async Task Dev01_AutomationWritePipeline_EnforcesAutoNumberFormulaAuditVersionAndApprovalLock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_d1_{suffix}", Code = $"D1_{suffix}".ToUpperInvariant(), Name = $"D1 {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_d1_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });

        var srcTable = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_src_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"src_{suffix}", DisplayName = "Source" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = srcTable.Id, ColumnCode = "code", ColumnName = "Code", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = srcTable.Id, ColumnCode = "qty", ColumnName = "Qty", DataType = "NUMBER", OrderIndex = 2 });

        var tgtTable = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_tgt_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"tgt_{suffix}",
            DisplayName = "Target",
            ApprovalEnabled = true,
            ApproverRoles = new List<string> { "ADMIN" }
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tgtTable.Id, ColumnCode = "auto_no", ColumnName = "Auto No", DataType = "AUTO_NUMBER", DefaultValue = $"D1-{suffix}-", IsIdentityKey = true, OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tgtTable.Id, ColumnCode = "ref_code", ColumnName = "Ref Code", DataType = "STRING", OrderIndex = 2 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tgtTable.Id, ColumnCode = "qty", ColumnName = "Qty", DataType = "NUMBER", OrderIndex = 3 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tgtTable.Id, ColumnCode = "total", ColumnName = "Total", DataType = "NUMBER", Formula = "qty * 10", OrderIndex = 4 });

        var createRule = await automations.UpsertAutomationAsync(new UpsertAutomationRequest
        {
            Name = $"Create Target {suffix}",
            SourceAppId = app.Id,
            SourceTableId = srcTable.Id,
            TriggerEvent = "RECORD_CREATED",
            ActionType = "CREATE_RECORD",
            TargetAppId = app.Id,
            TargetModuleId = mod.Id,
            TargetTableId = tgtTable.Id,
            FieldMappings = new Dictionary<string, string> { ["ref_code"] = "{{code}}", ["qty"] = "{{qty}}" }
        });

        var updateRule = await automations.UpsertAutomationAsync(new UpsertAutomationRequest
        {
            Name = $"Update Target {suffix}",
            SourceAppId = app.Id,
            SourceTableId = srcTable.Id,
            TriggerEvent = "RECORD_UPDATED",
            ActionType = "UPDATE_RECORD",
            TargetAppId = app.Id,
            TargetModuleId = mod.Id,
            TargetTableId = tgtTable.Id,
            MatchSourceField = "code",
            MatchTargetField = "ref_code",
            FieldMappings = new Dictionary<string, string> { ["qty"] = "{{qty}}" }
        });

        // 1) Creating source triggers CREATE_RECORD -> auto_no generated, formula total=50, record_key set, audit logged
        var (srcCreated, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = srcTable.Id,
            Data = new Dictionary<string, object?> { ["code"] = "SRC-1", ["qty"] = 5 }
        }, "admin", true);

        var (tgtItems, _) = await records.QueryRecordsAsync(null, null, tgtTable.Id, null, null);
        var target = Assert.Single(tgtItems);
        Assert.StartsWith($"D1-{suffix}-", target.RecordKey);
        Assert.Equal(target.RecordKey, target.Data["auto_no"]?.ToString());
        Assert.Equal(50m, Convert.ToDecimal(target.Data["total"]));
        Assert.Equal(1, target.Version);
        var auditsAfterCreate = await records.GetAuditLogsAsync(target.Id);
        Assert.Contains(auditsAfterCreate, a => a.Action == "CREATE" && a.Actor == "automation");

        // 2) Updating source triggers UPDATE_RECORD -> formula recomputed (80), version incremented to 2, audit logged
        await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            Id = srcCreated.Id,
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = srcTable.Id,
            Data = new Dictionary<string, object?> { ["code"] = "SRC-1", ["qty"] = 8 },
            ExpectedVersion = srcCreated.Version
        }, "admin", true);

        var (tgtAfterUpdate, _) = await records.QueryRecordsAsync(null, null, tgtTable.Id, null, null);
        var updatedTarget = Assert.Single(tgtAfterUpdate);
        Assert.Equal(80m, Convert.ToDecimal(updatedTarget.Data["total"]));
        Assert.Equal(2, updatedTarget.Version);
        var auditsAfterUpdate = await records.GetAuditLogsAsync(target.Id);
        Assert.Contains(auditsAfterUpdate, a => a.Action == "UPDATE" && a.Actor == "automation");

        // 3) Approve target record -> locked; subsequent source update must fail automation without altering approved record
        var submitted = await records.SubmitApprovalAsync(updatedTarget.Id, "admin");
        var adminUser = new SysUserDto { Username = "admin", RoleCode = "ADMIN", RoleCodes = new List<string> { "ADMIN" } };
        var (approved, err) = await records.ProcessApprovalAsync(updatedTarget.Id, adminUser, true, "Lock", submitted!.Version, submitted.CurrentStepOrder);
        Assert.Null(err);
        Assert.Equal("APPROVED", approved!.ApprovalStatus);

        await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            Id = srcCreated.Id,
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = srcTable.Id,
            Data = new Dictionary<string, object?> { ["code"] = "SRC-1", ["qty"] = 99 },
            ExpectedVersion = srcCreated.Version + 1
        }, "admin", true);

        var (tgtAfterLock, _) = await records.QueryRecordsAsync(null, null, tgtTable.Id, null, null);
        var lockedTarget = Assert.Single(tgtAfterLock);
        Assert.Equal(80m, Convert.ToDecimal(lockedTarget.Data["total"]));
        var logs = await automations.GetLogsAsync();
        Assert.Contains(logs, l => l.AutomationId == updateRule.Id && l.Status == "FAILED");
        _ = createRule;
    }
}
