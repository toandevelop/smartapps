using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Data;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class Dev04Dev06Fun09PostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public Dev04Dev06Fun09PostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task Dev06_Migrations_AreRecordedIdempotentlyWithChecksums()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var applied = await DatabaseMigrationRunner.GetAppliedMigrationsAsync(conn);
        Assert.Contains(applied, m => m.Version == "0001_baseline_schema");
        Assert.Contains(applied, m => m.Version == "0002_outbox_events");
        Assert.Contains(applied, m => m.Version == "0003_app_metadata_publishing");

        // Re-running MigrateAsync must be a no-op
        var secondRun = await DatabaseMigrationRunner.MigrateAsync(conn);
        Assert.Empty(secondRun);
    }

    [PostgresFact]
    public async Task Dev04_Outbox_EnqueuesWebhookRetriesAndSupportsManualRetry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();
        var automations = _fx.Services.GetRequiredService<AutomationService>();
        var outbox = _fx.Services.GetRequiredService<OutboxService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_obx_{suffix}", Code = $"OBX_{suffix}".ToUpperInvariant(), Name = $"OBX {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_obx_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_obx_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"obx_{suffix}", DisplayName = "Outbox Test" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "title", ColumnName = "Title", DataType = "STRING", OrderIndex = 1 });

        await automations.UpsertAutomationAsync(new UpsertAutomationRequest
        {
            Name = $"Webhook {suffix}",
            SourceAppId = app.Id,
            SourceTableId = tbl.Id,
            TriggerEvent = "RECORD_CREATED",
            ActionType = "CALL_WEBHOOK",
            WebhookUrl = "http://127.0.0.1:1/unreachable-webhook",
            WebhookSecret = "sec_123"
        });

        var (created, triggered) = await records.UpsertRecordAsync(new UpsertRecordRequest
        {
            AppId = app.Id,
            ModuleId = mod.Id,
            TableId = tbl.Id,
            Data = new Dictionary<string, object?> { ["title"] = "Hello Outbox" }
        }, "admin", true);

        Assert.Contains($"Webhook {suffix}", triggered);

        var queued = await outbox.GetEventsAsync("PENDING");
        var ev = Assert.Single(queued, e => e.AggregateId == created.Id);
        Assert.Equal("WEBHOOK_CALL", ev.EventType);

        // Process batch -> fails against unreachable port -> moves to FAILED with attempt_count = 1
        var processed = await outbox.ProcessBatchAsync(10);
        Assert.True(processed >= 1);

        var failed = await outbox.GetEventsAsync("FAILED");
        var failedEv = Assert.Single(failed, e => e.Id == ev.Id);
        Assert.Equal(1, failedEv.AttemptCount);
        Assert.False(string.IsNullOrWhiteSpace(failedEv.LastError));

        // Manual retry resets back to PENDING with attempt_count = 0
        var retried = await outbox.RetryAsync(ev.Id);
        Assert.True(retried);

        var pendingAgain = await outbox.GetEventsAsync("PENDING");
        var resetEv = Assert.Single(pendingAgain, e => e.Id == ev.Id);
        Assert.Equal(0, resetEv.AttemptCount);
    }

    [PostgresFact]
    public async Task Fun09_AppPublishing_ValidatesDraftPublishesImmutableReleaseAndRollsBackSafely()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_pub_{suffix}", Code = $"PUB_{suffix}".ToUpperInvariant(), Name = $"PUB {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_pub_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_pub_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"pub_{suffix}", DisplayName = "Empty Table" });

        // App becomes DRAFT and fails validation because table has 0 columns
        var draftApp = await meta.GetAppByCodeOrIdAsync(app.Id);
        Assert.Equal("DRAFT", draftApp!.PublishStatus);
        var invalid = await meta.ValidateAppForPublishAsync(app.Id);
        Assert.False(invalid.CanPublish);
        await Assert.ThrowsAsync<InvalidOperationException>(() => meta.PublishAppAsync(app.Id, "admin", "Should fail"));

        // Add 1 column -> validation passes -> Publish v2
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "name", ColumnName = "Name", DataType = "STRING", OrderIndex = 1 });
        var valid = await meta.ValidateAppForPublishAsync(app.Id);
        Assert.True(valid.CanPublish);

        var rel1 = await meta.PublishAppAsync(app.Id, "admin", "Initial release");
        Assert.Equal(2, rel1.VersionNumber); // Default published_version is 1 -> increments to 2
        Assert.Equal(1, rel1.TableCount);
        Assert.Equal(1, rel1.ColumnCount);

        var publishedApp = await meta.GetAppByCodeOrIdAsync(app.Id);
        Assert.Equal("PUBLISHED", publishedApp!.PublishStatus);

        // Add another table -> Publish v3
        var tbl2 = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_pub2_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"pub2_{suffix}", DisplayName = "Second Table" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl2.Id, ColumnCode = "code", ColumnName = "Code", DataType = "STRING", OrderIndex = 1 });
        var rel2 = await meta.PublishAppAsync(app.Id, "admin", "Added second table");
        Assert.Equal(3, rel2.VersionNumber);
        Assert.Equal(2, rel2.TableCount);

        // Rollback to v2 -> creates v4 with only 1 active table
        var rolledBack = await meta.RollbackAppReleaseAsync(app.Id, rel1.VersionNumber, "admin");
        Assert.NotNull(rolledBack);
        Assert.Equal(4, rolledBack!.VersionNumber);
        Assert.Equal(1, rolledBack.TableCount);

        var releases = await meta.GetAppReleasesAsync(app.Id);
        Assert.Equal(3, releases.Count);
    }

    [PostgresFact]
    public async Task PrintTemplates_SupportsCrudAndSingleDefaultPerTable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var meta = _fx.Services.GetRequiredService<MetadataService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_prt_{suffix}", Code = $"PRT_{suffix}".ToUpperInvariant(), Name = $"PRT {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_prt_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_prt_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"prt_{suffix}", DisplayName = "Print Test" });

        var t1 = await meta.UpsertPrintTemplateAsync(new UpsertPrintTemplateRequest
        {
            TableId = tbl.Id,
            Name = "Mẫu A4",
            PaperSize = "A4",
            HtmlTemplate = "<div>{{record_key}}</div>",
            IsDefault = true,
            WatermarkDraft = true
        }, "admin");
        Assert.True(t1.IsDefault);

        var t2 = await meta.UpsertPrintTemplateAsync(new UpsertPrintTemplateRequest
        {
            TableId = tbl.Id,
            Name = "Mẫu K80",
            PaperSize = "K80",
            HtmlTemplate = "<div>K80 {{record_key}}</div>",
            IsDefault = true,
            RequireApproved = true
        }, "admin");
        Assert.True(t2.IsDefault);

        var list = await meta.GetPrintTemplatesAsync(tbl.Id);
        Assert.Equal(2, list.Count);
        Assert.False(list.First(x => x.Id == t1.Id).IsDefault);
        Assert.True(list.First(x => x.Id == t2.Id).IsDefault);
        Assert.True(list.First(x => x.Id == t2.Id).RequireApproved);

        Assert.True(await meta.DeletePrintTemplateAsync(t1.Id));
        Assert.Single(await meta.GetPrintTemplatesAsync(tbl.Id));
    }
}
