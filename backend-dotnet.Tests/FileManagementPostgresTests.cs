using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class FileManagementPostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public FileManagementPostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task FileManagement_EnforcesQuotaCreatesShareLinksAndCleansOrphans()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var files = _fx.Services.GetRequiredService<FileManagementService>();
        var meta = _fx.Services.GetRequiredService<MetadataService>();
        var records = _fx.Services.GetRequiredService<RecordService>();

        var app = await meta.UpsertAppAsync(new UpsertAppRequest { Id = $"app_fm_{suffix}", Code = $"FM_{suffix}".ToUpperInvariant(), Name = $"FM {suffix}" });
        var mod = await meta.UpsertModuleAsync(new UpsertModuleRequest { Id = $"mod_fm_{suffix}", AppId = app.Id, Code = "MAIN", Name = "Main" });
        var tbl = await meta.UpsertTableAsync(new UpsertTableRequest { Id = $"tbl_fm_{suffix}", AppId = app.Id, ModuleId = mod.Id, TableName = $"fm_{suffix}", DisplayName = "Files" });
        await meta.UpsertColumnAsync(new UpsertColumnRequest { TableId = tbl.Id, ColumnCode = "files", ColumnName = "Files", DataType = "MULTI_FILE", OrderIndex = 1 });

        // 1) Set 1 MB quota for app -> uploading 2 MB fails
        await files.UpsertQuotaAsync("APP", app.Id, 1, "admin");
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() =>
            files.EnforceQuotaAsync(app.Id, "admin", 2 * 1024 * 1024));

        // 2) Insert two attachments (one linked to a record, one orphan)
        var tempDir = Path.Combine(Path.GetTempPath(), $"smartapps_fm_{suffix}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var attLinked = $"att_lnk_{suffix}";
            var attOrphan = $"att_orp_{suffix}";
            File.WriteAllText(Path.Combine(tempDir, $"{attLinked}.txt"), "linked");
            File.WriteAllText(Path.Combine(tempDir, $"{attOrphan}.txt"), "orphan");

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            await using (var ins = new NpgsqlCommand("""
                INSERT INTO app_attachments (id, app_id, table_id, file_name, content_type, size_bytes, storage_path, uploaded_by)
                VALUES (@id1, @app, @tbl, 'linked.txt', 'text/plain', 100, @p1, 'admin'),
                       (@id2, @app, @tbl, 'orphan.txt', 'text/plain', 200, @p2, 'admin')
                """, conn))
            {
                ins.Parameters.AddWithValue("id1", attLinked);
                ins.Parameters.AddWithValue("id2", attOrphan);
                ins.Parameters.AddWithValue("app", app.Id);
                ins.Parameters.AddWithValue("tbl", tbl.Id);
                ins.Parameters.AddWithValue("p1", $"{attLinked}.txt");
                ins.Parameters.AddWithValue("p2", $"{attOrphan}.txt");
                await ins.ExecuteNonQueryAsync();
            }

            await records.UpsertRecordAsync(new UpsertRecordRequest
            {
                AppId = app.Id,
                ModuleId = mod.Id,
                TableId = tbl.Id,
                Data = new Dictionary<string, object?>
                {
                    ["files"] = new List<object> { new { id = attLinked, fileName = "linked.txt", url = $"/api/attachments/{attLinked}" } }
                }
            }, "admin", true);

            // 3) Share link creation & resolution
            var share = await files.CreateShareLinkAsync(attLinked, 24, true, "admin");
            Assert.NotNull(share);
            var resolved = await files.ResolveShareTokenAsync(share!.ShareToken);
            Assert.NotNull(resolved);
            Assert.Equal("linked.txt", resolved!.Value.FileName);

            // 4) Orphan cleanup deletes attOrphan from disk & DB, keeps attLinked
            var (cleaned, freed) = await files.CleanupOrphanAttachmentsAsync(tempDir, 0);
            Assert.True(cleaned >= 1);
            Assert.True(freed >= 200);
            Assert.False(File.Exists(Path.Combine(tempDir, $"{attOrphan}.txt")));
            Assert.True(File.Exists(Path.Combine(tempDir, $"{attLinked}.txt")));

            var stats = await files.GetStorageStatsAsync();
            Assert.True(stats.TotalFiles >= 1);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StorageAdapters_LocalRoundTripAndS3SigV4HeadersWork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"smartapps_sa_{Guid.NewGuid():N}");
        try
        {
            var local = new LocalFileStorageAdapter(tempDir);
            await using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello-s3")))
                await local.SaveAsync("test.txt", ms, "text/plain");

            await using (var readStream = await local.OpenReadAsync("test.txt"))
            {
                Assert.NotNull(readStream);
                using var sr = new StreamReader(readStream!);
                Assert.Equal("hello-s3", await sr.ReadToEndAsync());
            }

            await local.DeleteAsync("test.txt");
            Assert.Null(await local.OpenReadAsync("test.txt"));

            var headers = S3CompatibleStorageAdapter.BuildSigV4Headers(
                "PUT",
                new Uri("http://minio:9000/smartapps-uploads/test.txt"),
                "minioadmin",
                "minioadmin",
                "us-east-1",
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));

            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=minioadmin/20261117/us-east-1/s3/aws4_request", headers["Authorization"]);
            Assert.Equal("20261117T090000Z", headers["x-amz-date"]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
