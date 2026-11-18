using SmartApps.Api.Data;
using SmartApps.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<RealtimeEventService>();
builder.Services.AddSingleton<AiService>();
builder.Services.AddSingleton<MetadataService>();
builder.Services.AddSingleton<DynamicComputeService>();
builder.Services.AddSingleton<RecordWriteService>();
builder.Services.AddSingleton<ExternalNotificationService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<OutboxService>();
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddSingleton<WorkflowService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<SsoService>();
builder.Services.AddSingleton<RecordService>();
builder.Services.AddSingleton<WidgetService>();
builder.Services.AddSingleton<IntegrationService>();
builder.Services.AddSingleton<VietnamIntegrationService>();
builder.Services.AddSingleton<FileManagementService>();
builder.Services.AddSingleton<IFileStorageAdapter>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var provider = cfg["Storage:Provider"];
    if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        return new S3CompatibleStorageAdapter(cfg, sp.GetRequiredService<IHttpClientFactory>());
    return new LocalFileStorageAdapter(sp.GetRequiredService<IWebHostEnvironment>());
});
builder.Services.AddSingleton<OrgUnitService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<TaskService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddHostedService<ScheduledReminderService>();
builder.Services.AddHostedService<OutboxWorkerService>();
builder.Services.AddHostedService<BackupSchedulerService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

var connStr = app.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing Postgres connection string");
var isMigrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
var seedDemoData = !isMigrateOnly && (app.Configuration.GetValue<bool?>("Database:SeedDemoData") ?? !app.Environment.IsProduction());
await DbInitializer.InitializeAsync(connStr, seedDemoData);
if (isMigrateOnly)
{
    app.Logger.LogInformation("Database migrations completed (--migrate-only). Exiting.");
    return;
}

app.MapOpenApi();
app.UseCors("AllowAll");
app.Use(async (context, next) =>
{
    var traceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(traceId))
        traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
    context.Response.Headers["X-Trace-Id"] = traceId;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestTrace");
    using (logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId }))
    {
        await next();
    }
});
app.UseAuthorization();

app.MapGet("/health", () => Results.Json(new
{
    name = "SmartApps Core Platform API (.NET 9)",
    status = "Online",
    port = 5200
}));

app.MapGet("/health/ready", async () =>
{
    try
    {
        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
        return Results.Json(new { status = "Ready", database = "Connected" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "Unavailable", database = "Error", detail = ex.Message }, statusCode: 503);
    }
});

app.MapGet("/", () => Results.Json(new
{
    name = "SmartApps Core Platform API (.NET 9)",
    openApi = "/openapi/v1.json",
    endpoints = new[] { "/api/apps", "/api/modules", "/api/tables", "/api/columns", "/api/categories", "/api/records", "/api/automations" }
}));

app.MapControllers();

app.Run();
