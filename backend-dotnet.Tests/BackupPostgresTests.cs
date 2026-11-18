using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public sealed class BackupPostgresTests : IClassFixture<PriorityPostgresFixture>
{
    private readonly PriorityPostgresFixture _fx;

    public BackupPostgresTests(PriorityPostgresFixture fx) => _fx = fx;

    [PostgresFact]
    public async Task BackupService_CreatesPortableZipArchiveMasksSecretAndEnforcesRetention()
    {
        var backups = _fx.Services.GetRequiredService<BackupService>();
        var tempDir = Path.Combine(Path.GetTempPath(), $"smartapps_bkp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var cfg = await backups.UpsertConfigAsync(new UpsertBackupConfigRequest
            {
                IsEnabled = true,
                IntervalHours = 12,
                RetentionCount = 1,
                DestinationType = "LOCAL",
                DestinationSecret = "super_secret_token",
                IncludeUploads = false
            }, "admin");

            Assert.True(cfg.IsEnabled);
            Assert.True(cfg.HasSecret);
            Assert.Equal(12, cfg.IntervalHours);

            var run1 = await backups.RunBackupAsync("MANUAL", "admin", tempDir);
            Assert.Equal("SUCCEEDED", run1.Status);
            Assert.True(run1.SizeBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(run1.ChecksumSha256));
            Assert.True(File.Exists(run1.LocalPath!));

            using (var zip = ZipFile.OpenRead(run1.LocalPath!))
            {
                Assert.NotNull(zip.GetEntry("manifest.json"));
                Assert.NotNull(zip.GetEntry("db/sys_apps.json"));
                Assert.NotNull(zip.GetEntry("db/app_records.json"));
            }

            // Second run with RetentionCount = 1 deletes the first local zip
            await Task.Delay(50);
            var run2 = await backups.RunBackupAsync("MANUAL", "admin", tempDir);
            Assert.Equal("SUCCEEDED", run2.Status);
            Assert.Single(Directory.GetFiles(tempDir, "smartapps_backup_*.zip"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
