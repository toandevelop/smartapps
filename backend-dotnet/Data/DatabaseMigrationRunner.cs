using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace SmartApps.Api.Data;

public sealed record MigrationStep(string Version, string Name, string Sql);

public sealed record AppliedMigrationDto(string Version, string Name, string Checksum, DateTime AppliedAt);

/// <summary>
/// Executes versioned, checksum-verified PostgreSQL schema migrations under an advisory lock.
/// </summary>
public static class DatabaseMigrationRunner
{
    public static IReadOnlyList<MigrationStep> GetMigrations() =>
    [
        new("0001_baseline_schema", "Baseline SmartApps schema", DbInitializer.BaselineSchemaDdl),
        new("0002_outbox_events", "Transactional outbox table for webhooks and external notifications", """
            CREATE TABLE IF NOT EXISTS app_outbox_events (
                id VARCHAR(64) PRIMARY KEY,
                event_type VARCHAR(64) NOT NULL,
                aggregate_type VARCHAR(64) NOT NULL,
                aggregate_id VARCHAR(64) NOT NULL,
                payload JSONB NOT NULL,
                status VARCHAR(24) NOT NULL DEFAULT 'PENDING',
                attempt_count INT NOT NULL DEFAULT 0,
                max_attempts INT NOT NULL DEFAULT 5,
                next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_error TEXT,
                locked_at TIMESTAMPTZ,
                processed_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_outbox_pending
                ON app_outbox_events(status, next_attempt_at)
                WHERE status IN ('PENDING', 'FAILED');
            """),
        new("0003_app_metadata_publishing", "App draft, validation, and release publishing ledger", """
            ALTER TABLE sys_apps ADD COLUMN IF NOT EXISTS published_version INT NOT NULL DEFAULT 1;
            ALTER TABLE sys_apps ADD COLUMN IF NOT EXISTS publish_status VARCHAR(24) NOT NULL DEFAULT 'PUBLISHED';
            ALTER TABLE sys_apps ADD COLUMN IF NOT EXISTS published_at TIMESTAMPTZ;
            ALTER TABLE sys_apps ADD COLUMN IF NOT EXISTS published_by VARCHAR(64);
            ALTER TABLE sys_apps ADD COLUMN IF NOT EXISTS draft_snapshot JSONB;

            CREATE TABLE IF NOT EXISTS sys_app_releases (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                version_number INT NOT NULL,
                notes TEXT,
                snapshot JSONB NOT NULL,
                published_by VARCHAR(64) NOT NULL,
                published_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(app_id, version_number)
            );
            CREATE INDEX IF NOT EXISTS idx_app_releases_app
                ON sys_app_releases(app_id, version_number DESC);
            """),
        new("0004_integrations_plugins", "API key expiry/rotation, plugin registry, and integration logs", """
            ALTER TABLE sys_api_keys ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;
            ALTER TABLE sys_api_keys ADD COLUMN IF NOT EXISTS rotated_at TIMESTAMPTZ;

            CREATE TABLE IF NOT EXISTS sys_plugins (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                name VARCHAR(255) NOT NULL,
                category VARCHAR(64) NOT NULL DEFAULT 'CUSTOM',
                version VARCHAR(32) NOT NULL DEFAULT '1.0.0',
                description TEXT,
                manifest JSONB NOT NULL DEFAULT '{}'::jsonb,
                config JSONB NOT NULL DEFAULT '{}'::jsonb,
                secret_value TEXT,
                app_id VARCHAR(64),
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                last_tested_at TIMESTAMPTZ,
                last_test_status VARCHAR(32),
                last_test_message TEXT,
                created_by VARCHAR(64) DEFAULT 'admin',
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_integration_logs (
                id VARCHAR(64) PRIMARY KEY,
                direction VARCHAR(16) NOT NULL,
                source_type VARCHAR(32) NOT NULL,
                source_id VARCHAR(64) NOT NULL,
                app_id VARCHAR(64),
                table_id VARCHAR(64),
                status_code INT NOT NULL DEFAULT 200,
                success BOOLEAN NOT NULL DEFAULT TRUE,
                summary TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_integration_logs_created ON app_integration_logs(created_at DESC);
            """),
        new("0005_print_templates", "Customizable per-table print templates", """
            CREATE TABLE IF NOT EXISTS sys_print_templates (
                id VARCHAR(64) PRIMARY KEY,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                name VARCHAR(255) NOT NULL,
                paper_size VARCHAR(32) NOT NULL DEFAULT 'A4',
                orientation VARCHAR(16) NOT NULL DEFAULT 'portrait',
                html_template TEXT NOT NULL,
                css_styles TEXT,
                is_default BOOLEAN NOT NULL DEFAULT FALSE,
                watermark_draft BOOLEAN NOT NULL DEFAULT TRUE,
                require_approved BOOLEAN NOT NULL DEFAULT FALSE,
                created_by VARCHAR(64) DEFAULT 'admin',
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_print_templates_tbl ON sys_print_templates(table_id, is_default DESC);
            """),
        new("0006_file_management_quota", "File quotas, share links, and record linkage", """
            ALTER TABLE app_attachments ADD COLUMN IF NOT EXISTS record_id VARCHAR(64);
            ALTER TABLE app_attachments ADD COLUMN IF NOT EXISTS is_deleted BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE app_attachments ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;

            CREATE TABLE IF NOT EXISTS sys_storage_quotas (
                id VARCHAR(64) PRIMARY KEY,
                scope_type VARCHAR(16) NOT NULL,
                scope_id VARCHAR(64) NOT NULL,
                quota_mb INT NOT NULL DEFAULT 1024,
                updated_by VARCHAR(64) DEFAULT 'admin',
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(scope_type, scope_id)
            );

            CREATE TABLE IF NOT EXISTS app_file_shares (
                id VARCHAR(64) PRIMARY KEY,
                attachment_id VARCHAR(64) NOT NULL REFERENCES app_attachments(id) ON DELETE CASCADE,
                share_token VARCHAR(128) UNIQUE NOT NULL,
                expires_at TIMESTAMPTZ,
                allow_download BOOLEAN NOT NULL DEFAULT TRUE,
                created_by VARCHAR(64) NOT NULL,
                access_count INT NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_file_shares_token ON app_file_shares(share_token);
            """),
        new("0007_org_units", "Hierarchical organization units and manager mapping", """
            CREATE TABLE IF NOT EXISTS sys_org_units (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                name VARCHAR(255) NOT NULL,
                parent_id VARCHAR(64) REFERENCES sys_org_units(id) ON DELETE SET NULL,
                manager_username VARCHAR(64),
                order_index INT NOT NULL DEFAULT 1,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE sys_users ADD COLUMN IF NOT EXISTS org_unit_id VARCHAR(64);
            """),
        new("0008_backups", "Automated backup configuration and run ledger", """
            CREATE TABLE IF NOT EXISTS sys_backup_configs (
                id VARCHAR(64) PRIMARY KEY,
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                interval_hours INT NOT NULL DEFAULT 24,
                retention_count INT NOT NULL DEFAULT 7,
                destination_type VARCHAR(32) NOT NULL DEFAULT 'LOCAL',
                destination_config JSONB NOT NULL DEFAULT '{}'::jsonb,
                destination_secret TEXT,
                include_uploads BOOLEAN NOT NULL DEFAULT TRUE,
                last_run_at TIMESTAMPTZ,
                updated_by VARCHAR(64) DEFAULT 'admin',
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_backup_runs (
                id VARCHAR(64) PRIMARY KEY,
                trigger_type VARCHAR(24) NOT NULL DEFAULT 'MANUAL',
                destination_type VARCHAR(32) NOT NULL DEFAULT 'LOCAL',
                file_name VARCHAR(255) NOT NULL,
                local_path TEXT,
                remote_uri TEXT,
                size_bytes BIGINT NOT NULL DEFAULT 0,
                checksum_sha256 VARCHAR(64),
                status VARCHAR(24) NOT NULL DEFAULT 'SUCCEEDED',
                message TEXT,
                created_by VARCHAR(64) DEFAULT 'admin',
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_backup_runs_created ON app_backup_runs(created_at DESC);
            """),
        new("0009_app_plugin_bindings", "Per-app plugin activation and field mapping", """
            CREATE TABLE IF NOT EXISTS sys_app_plugin_bindings (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL,
                plugin_code VARCHAR(64) NOT NULL,
                is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                table_id VARCHAR(64),
                field_mapping JSONB NOT NULL DEFAULT '{}'::jsonb,
                updated_by VARCHAR(64) DEFAULT 'admin',
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(app_id, plugin_code)
            );
            CREATE INDEX IF NOT EXISTS idx_app_plugin_bindings_app ON sys_app_plugin_bindings(app_id);
            """),
        new("0010_column_quick_create", "Allow inline quick-create for category-linked columns", """
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS allow_quick_create BOOLEAN NOT NULL DEFAULT FALSE;
            """),
        new("0011_app_records_approval_idx", "Index approval_status on active app_records", """
            CREATE INDEX IF NOT EXISTS idx_app_records_approval ON app_records(table_id, approval_status) WHERE deleted_at IS NULL;
            """),
        new("0012_phase2_enterprise_hardening", "DB-backed SSO states, business holidays, workflow delegations, parallel approvals, and public form limits", """
            CREATE TABLE IF NOT EXISTS sys_sso_states (
                state VARCHAR(128) PRIMARY KEY,
                provider_code VARCHAR(64) NOT NULL,
                redirect_uri TEXT NOT NULL,
                code_verifier TEXT NOT NULL,
                nonce VARCHAR(128) NOT NULL,
                return_url TEXT NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_sso_states_expires ON sys_sso_states(expires_at);

            CREATE TABLE IF NOT EXISTS sys_business_holidays (
                id VARCHAR(64) PRIMARY KEY,
                holiday_date DATE NOT NULL UNIQUE,
                name VARCHAR(255) NOT NULL,
                is_recurring BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            INSERT INTO sys_business_holidays (id, holiday_date, name, is_recurring)
            VALUES
                ('hol_vn_0101', '2026-01-01', 'Tết Dương lịch', TRUE),
                ('hol_vn_0430', '2026-04-30', 'Ngày Giải phóng miền Nam', TRUE),
                ('hol_vn_0501', '2026-05-01', 'Ngày Quốc tế Lao động', TRUE),
                ('hol_vn_0902', '2026-09-02', 'Ngày Quốc khánh', TRUE)
            ON CONFLICT (holiday_date) DO NOTHING;

            CREATE TABLE IF NOT EXISTS sys_workflow_delegations (
                id VARCHAR(64) PRIMARY KEY,
                delegator_username VARCHAR(64) NOT NULL,
                delegate_username VARCHAR(64) NOT NULL,
                app_id VARCHAR(64),
                starts_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                reason TEXT,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_wf_delegations_delegate ON sys_workflow_delegations(LOWER(delegate_username), is_active, starts_at, expires_at);

            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS step_approvals JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS public_form_expires_at TIMESTAMPTZ;
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS public_form_max_submissions INT;
            """),
        new(
            "0013",
            "0013_phase3_ai_ecosystem",
            """
            CREATE TABLE IF NOT EXISTS app_idempotency_keys (
                idempotency_key VARCHAR(128) PRIMARY KEY,
                actor VARCHAR(64) NOT NULL,
                method VARCHAR(16) NOT NULL DEFAULT 'POST',
                path VARCHAR(255) NOT NULL DEFAULT '',
                response_status INT NOT NULL DEFAULT 200,
                response_body JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_app_idempotency_created ON app_idempotency_keys(created_at);
            """)
    ];

    public static string ComputeChecksum(string sql)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n").Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static async Task<IReadOnlyList<string>> MigrateAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('smartapps_schema_migrations', 0))", conn))
        {
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await using (var ledgerCmd = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS sys_schema_migrations (
                    version VARCHAR(64) PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    checksum VARCHAR(64) NOT NULL,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """, conn))
            {
                await ledgerCmd.ExecuteNonQueryAsync(ct);
            }

            var applied = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var readCmd = new NpgsqlCommand("SELECT version, checksum FROM sys_schema_migrations", conn))
            await using (var r = await readCmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    applied[r.GetString(0)] = r.GetString(1);
                }
            }

            var newlyApplied = new List<string>();
            foreach (var step in GetMigrations())
            {
                var checksum = ComputeChecksum(step.Sql);
                if (applied.TryGetValue(step.Version, out var existingChecksum))
                {
                    if (!string.Equals(existingChecksum, checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Migration '{step.Version}' checksum mismatch. Expected '{existingChecksum}', found '{checksum}'.");
                    }
                    continue;
                }

                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var sqlCmd = new NpgsqlCommand(step.Sql, conn, tx))
                {
                    await sqlCmd.ExecuteNonQueryAsync(ct);
                }

                await using (var insCmd = new NpgsqlCommand("""
                    INSERT INTO sys_schema_migrations (version, name, checksum)
                    VALUES (@v, @n, @c)
                    """, conn, tx))
                {
                    insCmd.Parameters.AddWithValue("v", step.Version);
                    insCmd.Parameters.AddWithValue("n", step.Name);
                    insCmd.Parameters.AddWithValue("c", checksum);
                    await insCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                newlyApplied.Add(step.Version);
            }

            return newlyApplied;
        }
        finally
        {
            await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('smartapps_schema_migrations', 0))", conn);
            await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public static async Task<List<AppliedMigrationDto>> GetAppliedMigrationsAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        var list = new List<AppliedMigrationDto>();
        await using var cmd = new NpgsqlCommand(
            "SELECT version, name, checksum, applied_at FROM sys_schema_migrations ORDER BY version", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AppliedMigrationDto(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDateTime(3)));
        }
        return list;
    }
}
