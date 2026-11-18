using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Fun03And08PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Fun03And08PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task ApprovalLifecycle_SupportsRecallResubmitCancelAndWorkflowSnapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var workflows = _fx.Services.GetRequiredService<WorkflowService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_lc_{suffix}", Code = $"LC_{suffix}".ToUpperInvariant(), Name = "LC" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_lc_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_lc_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"lc_{suffix}",
            DisplayName = "LC",
            ApprovalEnabled = true,
            ApproverRoles = new List<string> { "ADMIN" }
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_lc_{suffix}",
            TableId = tbl.Id,
            ColumnCode = "tieu_de",
            ColumnName = "Tiêu đề",
            DataType = "STRING",
            OrderIndex = 1
        });

        await workflows.UpsertWorkflowAsync(new UpsertWorkflowRequest
        {
            Id = $"wf_lc_{suffix}",
            AppId = app.Id,
            TableId = tbl.Id,
            Name = "2-step",
            Steps = new List<WorkflowStepDto>
            {
                new() { StepOrder = 1, Name = "Bước 1", ApproverRoles = new List<string> { "ADMIN" } },
                new() { StepOrder = 2, Name = "Bước 2", ApproverRoles = new List<string> { "ADMIN" } }
            }
        });

        var (created, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            Data = new Dictionary<string, object?> { ["tieu_de"] = "Đề xuất mua sắm" }
        }, "staff1", false);

        var staffUser = new SysUserDto { Username = "staff1", RoleCode = "STAFF", RoleCodes = new List<string> { "STAFF" } };
        var adminUser = new SysUserDto { Username = "admin", RoleCode = "ADMIN", RoleCodes = new List<string> { "ADMIN" } };

        // 1. Submit -> PENDING, snapshot captured
        var submitted = await records.SubmitApprovalAsync(created.Id, "staff1");
        Assert.NotNull(submitted);
        Assert.Equal("PENDING", submitted!.ApprovalStatus);
        Assert.NotNull(submitted.WorkflowSnapshot);
        Assert.Equal(2, submitted.WorkflowSnapshot!.Count);

        // 2. Recall by submitter -> RECALLED
        var (recalled, recallErr) = await records.RecallApprovalAsync(created.Id, staffUser, "Sửa lại số lượng", submitted.Version);
        Assert.Null(recallErr);
        Assert.Equal("RECALLED", recalled!.ApprovalStatus);

        // 3. Resubmit -> PENDING
        var resubmitted = await records.SubmitApprovalAsync(created.Id, "staff1");
        Assert.Equal("PENDING", resubmitted!.ApprovalStatus);

        // 4. Modify workflow definition after submission: in-flight record still uses its snapshot (2 steps)!
        await workflows.UpsertWorkflowAsync(new UpsertWorkflowRequest
        {
            Id = $"wf_lc_{suffix}",
            AppId = app.Id,
            TableId = tbl.Id,
            Name = "1-step-edited",
            Steps = new List<WorkflowStepDto>
            {
                new() { StepOrder = 1, Name = "Bước mới", ApproverRoles = new List<string> { "ADMIN" } }
            }
        });

        var (afterStep1, err1) = await records.ProcessApprovalAsync(
            created.Id, adminUser, true, "OK step 1", resubmitted.Version, resubmitted.CurrentStepOrder);
        Assert.Null(err1);
        Assert.Equal("PENDING", afterStep1!.ApprovalStatus);
        Assert.Equal(2, afterStep1.CurrentStepOrder);

        // 5. Cancel -> CANCELLED, cannot submit after cancel
        var (cancelled, cancelErr) = await records.CancelApprovalAsync(created.Id, staffUser, "Không còn nhu cầu", afterStep1.Version);
        Assert.Null(cancelErr);
        Assert.Equal("CANCELLED", cancelled!.ApprovalStatus);
        await Assert.ThrowsAsync<RecordConflictException>(() => records.SubmitApprovalAsync(created.Id, "staff1"));
    }

    [PostgresFact]
    public async Task DeleteRecords_BlocksDeletingReferencedLookupParent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_ref_{suffix}", Code = $"REF_{suffix}".ToUpperInvariant(), Name = "REF" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_ref_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });

        var parentTbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_p_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"p_{suffix}",
            DisplayName = "Khách hàng"
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_p_{suffix}",
            TableId = parentTbl.Id,
            ColumnCode = "ma_kh",
            ColumnName = "Mã KH",
            DataType = "STRING",
            IsIdentityKey = true,
            OrderIndex = 1
        });

        var childTbl = await meta.UpsertTableAsync(new UpsertTableRequest
        {
            Id = $"tbl_c_{suffix}",
            AppId = app.Id,
            ModuleId = mod.Id,
            TableName = $"c_{suffix}",
            DisplayName = "Đơn hàng"
        });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_c_{suffix}",
            TableId = childTbl.Id,
            ColumnCode = "khach_hang",
            ColumnName = "Khách hàng",
            DataType = "LOOKUP",
            RefTableId = parentTbl.Id,
            RefValueField = "ma_kh",
            OrderIndex = 1
        });

        var (parentRec, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = parentTbl.Id,
            Data = new Dictionary<string, object?> { ["ma_kh"] = "KH01" }
        }, "admin", true);

        var (childRec, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = childTbl.Id,
            Data = new Dictionary<string, object?> { ["khach_hang"] = "KH01" }
        }, "admin", true);

        // Deleting parent while child references it -> RecordLockedException
        await Assert.ThrowsAsync<RecordLockedException>(() => records.DeleteRecordsAsync(new List<string> { parentRec.Id }, "admin", true));

        // Deleting child first, then parent -> succeeds!
        Assert.Equal(1, await records.DeleteRecordsAsync(new List<string> { childRec.Id }, "admin", true));
        Assert.Equal(1, await records.DeleteRecordsAsync(new List<string> { parentRec.Id }, "admin", true));
    }

    [PostgresFact]
    public async Task CascadingLookup_FiltersOptionsAndRejectsOutOfScopeWrite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_cas_{suffix}", Code = $"CAS_{suffix}".ToUpperInvariant(), Name = "CAS" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_cas_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });

        var empTbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_emp_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"emp_{suffix}", DisplayName = "Nhân viên" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { Id = $"col_e1_{suffix}", TableId = empTbl.Id, ColumnCode = "ma_nv", ColumnName = "Mã NV", DataType = "STRING", IsIdentityKey = true, OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { Id = $"col_e2_{suffix}", TableId = empTbl.Id, ColumnCode = "phong_ban", ColumnName = "Phòng ban", DataType = "STRING", OrderIndex = 2 });

        await records.UpsertRecordAsync(new UpsertRecordRequest { AppId = app.Id, ModuleId = mod.Id, TableId = empTbl.Id, Data = new() { ["ma_nv"] = "NV_IT", ["phong_ban"] = "IT" } }, "admin", true);
        await records.UpsertRecordAsync(new UpsertRecordRequest { AppId = app.Id, ModuleId = mod.Id, TableId = empTbl.Id, Data = new() { ["ma_nv"] = "NV_HR", ["phong_ban"] = "HR" } }, "admin", true);

        var reqTbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_req_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"req_{suffix}", DisplayName = "Đề xuất" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { Id = $"col_r1_{suffix}", TableId = reqTbl.Id, ColumnCode = "phong_ban", ColumnName = "Phòng ban", DataType = "STRING", OrderIndex = 1 });
        await meta.UpsertColumnAsync(new UpsertColumnRequest
        {
            Id = $"col_r2_{suffix}",
            TableId = reqTbl.Id,
            ColumnCode = "nhan_vien",
            ColumnName = "Nhân viên",
            DataType = "LOOKUP",
            RefTableId = empTbl.Id,
            RefValueField = "ma_nv",
            LookupFilterField = "phong_ban",
            LookupSourceField = "phong_ban",
            OrderIndex = 2
        });

        var itOpts = await records.GetLookupOptionsAsync(empTbl.Id, "ma_nv", "ma_nv", null, null, "phong_ban", "IT");
        Assert.Single(itOpts);
        Assert.Equal("NV_IT", itOpts[0].Value);

        // Out-of-scope lookup value (NV_HR with phong_ban=IT) is rejected
        await Assert.ThrowsAsync<FormRuleValidationException>(() => records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = reqTbl.Id,
            Data = new() { ["phong_ban"] = "IT", ["nhan_vien"] = "NV_HR" }
        }, "admin", true));

        // Matching lookup value succeeds
        var (saved, _) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = reqTbl.Id,
            Data = new() { ["phong_ban"] = "IT", ["nhan_vien"] = "NV_IT" }
        }, "admin", true);
        Assert.Equal("NV_IT", saved.Data["nhan_vien"]?.ToString());
    }

    [Fact]
    public void ProductionAuth_RejectsDefaultOrMissingSecret()
    {
        var badConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Database=unused;Username=postgres",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Auth:Secret"] = "SmartApps_Enterprise_Production_Secret_Change_Me"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new AuthService(badConfig));

        var goodConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Database=unused;Username=postgres",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Auth:Secret"] = "Custom_Strong_Secret_For_Unit_Test_2026!"
        }).Build();

        var auth = new AuthService(goodConfig);
        Assert.NotNull(auth);
    }
}
