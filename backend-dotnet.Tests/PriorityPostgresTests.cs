using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartApps.Api.Controllers;
using SmartApps.Api.Data;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

[assembly: CollectionBehavior(MaxParallelThreads = 4)]

namespace SmartApps.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMARTAPPS_TEST_POSTGRES")))
            Skip = "Set SMARTAPPS_TEST_POSTGRES to an isolated PostgreSQL server with CREATE DATABASE permission.";
    }
}

public sealed class PriorityPostgresFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _adminConnection;
    private readonly string _databaseName = "smartapps_priority_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => _app!.Services;
    public string TableId { get; private set; } = "";
    public const string ChannelId = "priority_history";

    public static AuthService CreateAuth() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Database=unused;Username=postgres",
        ["Auth:Secret"] = "priority-test-secret"
    }).Build());

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("SMARTAPPS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(source)) return;
        var adminCs = new NpgsqlConnectionStringBuilder(source) { Database = "postgres", Pooling = false };
        _adminConnection = adminCs.ConnectionString;
        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        var appCs = new NpgsqlConnectionStringBuilder(source) { Database = _databaseName, Pooling = true, MaxPoolSize = 16 };
        ConnectionString = appCs.ConnectionString;
        await DbInitializer.InitializeAsync(ConnectionString);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Postgres"] = ConnectionString, ["Auth:Secret"] = "priority-test-secret"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(RecordsController).Assembly);
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<SsoService>();
        builder.Services.AddSingleton<AiService>();
        builder.Services.AddSingleton<RealtimeEventService>();
        builder.Services.AddSingleton<PlatformMetricsService>();
        builder.Services.AddSingleton<BusinessCalendarService>();
        builder.Services.AddSingleton<MetadataService>();
        builder.Services.AddSingleton<AppUpgradeService>();
        builder.Services.AddSingleton<AppStoreService>();
        builder.Services.AddSingleton<DynamicComputeService>();
        builder.Services.AddSingleton<RecordWriteService>();
        builder.Services.AddSingleton<ExternalNotificationService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<TaskService>();
        builder.Services.AddSingleton<OutboxService>();
        builder.Services.AddSingleton<AutomationService>();
        builder.Services.AddSingleton<WorkflowService>();
        builder.Services.AddSingleton<RecordService>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<IntegrationService>();
        builder.Services.AddSingleton<VietnamIntegrationService>();
        builder.Services.AddSingleton<FileManagementService>();
        builder.Services.AddSingleton<IFileStorageAdapter, LocalFileStorageAdapter>();
        builder.Services.AddSingleton<OrgUnitService>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<AiCopilotService>();
        _app = builder.Build();
        _app.MapControllers();
        _app.MapGet("/metrics", async (Microsoft.AspNetCore.Http.HttpRequest req, PlatformMetricsService metrics, CancellationToken ct) =>
        {
            var snap = await metrics.GetSnapshotAsync(ct);
            var wantProm = string.Equals(req.Query["format"], "prometheus", StringComparison.OrdinalIgnoreCase);
            return wantProm
                ? Microsoft.AspNetCore.Http.Results.Text(PlatformMetricsService.ToPrometheusText(snap), "text/plain; version=0.0.4; charset=utf-8")
                : Microsoft.AspNetCore.Http.Results.Json(snap);
        });
        await _app.StartAsync();
        Client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(_app.Urls.Single()) };

        var metadata = Services.GetRequiredService<MetadataService>();
        var app = await metadata.UpsertAppAsync(new() { Code = "PRIORITY", Name = "Priority tests" });
        var module = await metadata.UpsertModuleAsync(new() { AppId = app.Id, Code = "DATA", Name = "Data" });
        var table = await metadata.UpsertTableAsync(new() { AppId = app.Id, ModuleId = module.Id, TableName = "priority_records", DisplayName = "Test records", RowScope = "OWNER_ONLY" });
        TableId = table.Id;
        foreach (var col in new[] {
            new UpsertColumnRequest { ColumnCode = "name", ColumnName = "Name", DataType = "STRING", OrderIndex = 1 },
            new UpsertColumnRequest { ColumnCode = "category", ColumnName = "Category", DataType = "SELECT", OrderIndex = 2 },
            new UpsertColumnRequest { ColumnCode = "amount", ColumnName = "Amount", DataType = "NUMBER", OrderIndex = 3 },
            new UpsertColumnRequest { ColumnCode = "secret", ColumnName = "Secret", DataType = "STRING", HiddenForRoles = ["TEST_EXPORT"], OrderIndex = 4 }
        }) { col.TableId = table.Id; await metadata.UpsertColumnAsync(col); }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var seed = new NpgsqlCommand("""
            INSERT INTO sys_users(id, username, full_name, role_code, role_codes, password_hash, allowed_apps, permissions)
            VALUES ('priority_exporter', 'exporter', 'Exporter', 'TEST_EXPORT', '["TEST_EXPORT"]', 'unused', '["PRIORITY"]', '["record.view","record.export"]'),
                   ('priority_viewer', 'viewer', 'Viewer', 'TEST_VIEWER', '["TEST_VIEWER"]', 'unused', '["PRIORITY"]', '["record.view"]');
            INSERT INTO app_records(id, app_id, module_id, table_id, record_key, data, status, created_by, deleted_at)
            SELECT 'priority_' || i, @app, @mod, @tbl, 'KEY-' || i,
                jsonb_build_object('name', 'match-' || lpad(i::text,5,'0'), 'category', CASE WHEN i % 2 = 0 THEN 'A' ELSE 'B' END, 'amount', i, 'secret', 'hidden-value'),
                'ACTIVE', CASE WHEN i <= 1200 THEN 'exporter' ELSE 'outsider' END,
                CASE WHEN i = 1200 THEN CURRENT_TIMESTAMP ELSE NULL END
            FROM generate_series(1,1230) i;
            INSERT INTO chat_channels(id, name, channel_type, created_by) VALUES ('priority_history','History','GROUP','exporter');
            INSERT INTO chat_channel_members(channel_id,username,role) VALUES ('priority_history','exporter','OWNER');
            INSERT INTO chat_messages(id,channel_id,sender_username,content,created_at,deleted_at)
            SELECT 'history_' || lpad(i::text,5,'0'), 'priority_history', 'exporter', 'Message ' || i,
                '2026-11-17T01:02:03.123456Z'::timestamptz, CASE WHEN i=10 THEN CURRENT_TIMESTAMP ELSE NULL END
            FROM generate_series(1,305) i;
            """, conn);
        seed.Parameters.AddWithValue("app", app.Id);
        seed.Parameters.AddWithValue("mod", module.Id);
        seed.Parameters.AddWithValue("tbl", table.Id);
        await seed.ExecuteNonQueryAsync();
    }

    public async Task<HttpResponseMessage> SendAsAsync(string username, string url, HttpMethod? method = null, object? body = null)
    {
        var auth = Services.GetRequiredService<AuthService>();
        var user = await auth.GetByUsernameAsync(username);
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        if (body != null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.CreateToken(user!));
        return await Client.SendAsync(request);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            await using var poolTarget = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(poolTarget);
        }
        if (_adminConnection != null)
        {
            await using var conn = new NpgsqlConnection(_adminConnection);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public class PriorityPostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fixture;
    public PriorityPostgresTests(PriorityPostgresFixture fixture) => _fixture = fixture;

    [PostgresFact]
    public async Task Export_ReturnsEveryFilteredOwnedRowAndEnforcesExportAndFieldPermissions()
    {
        var query = $"tableId={_fixture.TableId}&search=match&status=ACTIVE&sortField=amount&sortDir=DESC&filters="
            + Uri.EscapeDataString("{\"category\":\"A\"}");
        using var page = await _fixture.SendAsAsync("exporter", "/api/records?" + query + "&limit=20");
        page.EnsureSuccessStatusCode();
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal(599, pageJson.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(20, pageJson.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());

        using var export = await _fixture.SendAsAsync("exporter", "/api/records/export?" + query);
        export.EnsureSuccessStatusCode();
        Assert.Equal(ExcelExportService.ContentType, export.Content.Headers.ContentType!.MediaType);
        using var zip = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = xml.Descendants(ns + "row").ToList();
        Assert.Equal(600, rows.Count);
        Assert.DoesNotContain("secret", rows[0].Value);
        Assert.DoesNotContain("hidden-value", xml.ToString());
        Assert.Equal("1198", rows[1].Descendants(ns + "v").Single().Value);
        Assert.Equal("2", rows[^1].Descendants(ns + "v").Single().Value);
        using var forbidden = await _fixture.SendAsAsync("viewer", "/api/records/export?" + query);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = await _fixture.Client.GetAsync("/api/records/export?" + query);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [PostgresFact]
    public async Task ChatHistory_PagesTiedTimestampsWithoutDuplicatesOrJoiningPrivateChannels()
    {
        var ids = new HashSet<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/chat/channels/{PriorityPostgresFixture.ChannelId}/messages?limit=100"
                + (cursor == null ? "" : "&before=" + Uri.EscapeDataString(cursor));
            using var response = await _fixture.SendAsAsync("exporter", url);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");
            Assert.InRange(data.GetArrayLength(), 1, 100);
            foreach (var msg in data.EnumerateArray()) Assert.True(ids.Add(msg.GetProperty("id").GetString()!));
            var pagination = json.RootElement.GetProperty("pagination");
            cursor = pagination.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor != null, pagination.GetProperty("hasMore").GetBoolean());
            if (++pages == 1)
            {
                using var forbidden = await _fixture.SendAsAsync("viewer", url);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                using var markRead = await _fixture.SendAsAsync("viewer", $"/api/chat/channels/{PriorityPostgresFixture.ChannelId}/read", HttpMethod.Post);
                Assert.Equal(HttpStatusCode.Forbidden, markRead.StatusCode);
                using var malformed = await _fixture.SendAsAsync("exporter", url + "&before=invalid");
                Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
                await _fixture.ExecuteAsync("""
                    INSERT INTO chat_messages(id,channel_id,sender_username,content,created_at)
                    VALUES ('history_new', 'priority_history', 'exporter', 'Arrived between pages', CURRENT_TIMESTAMP);
                    """);
            }
            Assert.True(pages < 10);
        } while (cursor != null);
        Assert.Equal(304, ids.Count);
        Assert.Equal(4, pages);
        Assert.DoesNotContain("history_new", ids);
        Assert.DoesNotContain("history_00010", ids);
        Assert.Contains("history_00001", ids);
    }

    [PostgresFact]
    public async Task Sessions_UsePersistedSettingInLoginDemoSsoAndRevocation()
    {
        var auth = _fixture.Services.GetRequiredService<AuthService>();
        var admin = (await auth.GetByUsernameAsync("admin"))!;
        var oldToken = auth.CreateToken(admin);
        var oldExpiry = auth.GetTokenExpiration(oldToken);
        var settings = _fixture.Services.GetRequiredService<SettingsService>();
        await settings.UpdateSystemSettingsAsync(new SystemSettingsDto { SessionTimeoutDays = 30 }, "admin");
        using var login = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "123456" });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        var expiry = auth.GetTokenExpiration(token);
        Assert.InRange(expiry, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(1));
        var cookies = new CookieContainer();
        foreach (var header in login.Headers.GetValues("Set-Cookie")) cookies.SetCookies(_fixture.Client.BaseAddress!, header);
        Assert.Equal(expiry.UtcDateTime, cookies.GetCookies(_fixture.Client.BaseAddress!)["smartapps_token"]!.Expires.ToUniversalTime());
        using var demo = await _fixture.Client.PostAsJsonAsync("/api/auth/demo-switch", new { username = "admin" });
        demo.EnsureSuccessStatusCode();
        using var demoJson = JsonDocument.Parse(await demo.Content.ReadAsStringAsync());
        var demoExpiry = auth.GetTokenExpiration(demoJson.RootElement.GetProperty("data").GetProperty("token").GetString()!);
        Assert.InRange(demoExpiry, expiry.AddSeconds(-5), expiry.AddSeconds(5));
        var (ssoUser, ssoToken, error) = await auth.ResolveOrProvisionSsoUserAsync("TEST", "priority-admin", "admin@company.vn", "Admin", "admin", true, "STAFF");
        Assert.Null(error);
        Assert.NotNull(ssoUser);
        Assert.InRange(auth.GetTokenExpiration(ssoToken!), expiry.AddSeconds(-5), expiry.AddSeconds(5));
        await auth.RevokeTokenAsync(token);
        Assert.Null(await auth.ValidateTokenAsync(token));
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT MAX(expires_at) FROM sys_revoked_tokens", conn);
        Assert.Equal(expiry.UtcDateTime, (DateTime)(await cmd.ExecuteScalarAsync())!);
        await settings.UpdateSystemSettingsAsync(new SystemSettingsDto { SessionTimeoutDays = 1 }, "admin");
        var shortExpiry = auth.GetTokenExpiration(await auth.CreateTokenAsync(admin));
        Assert.InRange(shortExpiry, DateTimeOffset.UtcNow.AddDays(1).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(1));
        Assert.Equal(oldExpiry, auth.GetTokenExpiration(oldToken));
        Assert.NotNull(await auth.ValidateTokenAsync(oldToken));
    }
}
