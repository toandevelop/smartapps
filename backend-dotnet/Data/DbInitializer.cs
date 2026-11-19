using Npgsql;
using System.Text.Json;
using SmartApps.Api.Services;

namespace SmartApps.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(string connectionString, bool seedDemoData = true)
    {
        var csBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDb = csBuilder.Database ?? "smartapps_db";

        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };
        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @db", adminConn);
            checkCmd.Parameters.AddWithValue("db", targetDb);
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists == null)
            {
                await using var createDbCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\"", adminConn);
                await createDbCmd.ExecuteNonQueryAsync();
            }
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await DatabaseMigrationRunner.MigrateAsync(conn);

        if (!seedDemoData)
        {
            return;
        }

        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_apps", conn);
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        if (count == 0)
        {
            await SeedPlatformAsync(conn);
        }

        await EnsureCrossAppLinksAndAutomationsAsync(conn);
        await AppUpgradeService.EnsureCoreAppsV2Async(connectionString);
    }

    internal const string BaselineSchemaDdl = """
            CREATE TABLE IF NOT EXISTS sys_apps (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                department VARCHAR(128) DEFAULT 'Toàn công ty',
                icon VARCHAR(64) DEFAULT 'Apps',
                color VARCHAR(32) DEFAULT '#1a73e8',
                bg_color VARCHAR(32) DEFAULT '#e8f0fe',
                version VARCHAR(32) DEFAULT '1.0.0',
                order_index INT DEFAULT 1,
                is_active BOOLEAN DEFAULT TRUE,
                settings JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_modules (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                code VARCHAR(64) NOT NULL,
                name VARCHAR(255) NOT NULL,
                icon VARCHAR(64) DEFAULT 'Layers',
                order_index INT DEFAULT 1,
                is_active BOOLEAN DEFAULT TRUE,
                description TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(app_id, code)
            );

            CREATE TABLE IF NOT EXISTS sys_tables (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                module_id VARCHAR(64) NOT NULL REFERENCES sys_modules(id) ON DELETE CASCADE,
                table_name VARCHAR(128) NOT NULL,
                display_name VARCHAR(255) NOT NULL,
                description TEXT,
                primary_view VARCHAR(32) DEFAULT 'table',
                row_scope VARCHAR(32) DEFAULT 'ALL',
                order_index INT DEFAULT 1,
                is_active BOOLEAN DEFAULT TRUE,
                ui_config JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_columns (
                id VARCHAR(64) PRIMARY KEY,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                column_code VARCHAR(128) NOT NULL,
                column_name VARCHAR(255) NOT NULL,
                excel_header VARCHAR(255) NOT NULL,
                data_type VARCHAR(32) NOT NULL DEFAULT 'STRING',
                is_required BOOLEAN DEFAULT FALSE,
                is_identity_key BOOLEAN DEFAULT FALSE,
                show_in_table BOOLEAN DEFAULT TRUE,
                show_in_form BOOLEAN DEFAULT TRUE,
                width INT DEFAULT 170,
                validation_regex VARCHAR(255),
                ref_category_code VARCHAR(128),
                ref_app_id VARCHAR(64),
                ref_table_id VARCHAR(64),
                ref_value_field VARCHAR(128),
                ref_display_field VARCHAR(128),
                lookup_filter_field VARCHAR(128),
                lookup_source_field VARCHAR(128),
                formula VARCHAR(512),
                default_value TEXT,
                hidden_for_roles JSONB NOT NULL DEFAULT '[]'::jsonb,
                read_only_for_roles JSONB NOT NULL DEFAULT '[]'::jsonb,
                order_index INT DEFAULT 1,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(table_id, column_code)
            );

            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS row_scope VARCHAR(32) DEFAULT 'ALL';
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS approval_enabled BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS approval_field VARCHAR(128) DEFAULT 'trang_thai_duyet';
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS approver_roles JSONB NOT NULL DEFAULT '["ADMIN","HR_MANAGER","ASSET_LEAD"]'::jsonb;
            ALTER TABLE sys_tables ADD COLUMN IF NOT EXISTS public_form_enabled BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS ref_app_id VARCHAR(64);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS ref_table_id VARCHAR(64);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS ref_value_field VARCHAR(128);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS ref_display_field VARCHAR(128);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS lookup_filter_field VARCHAR(128);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS lookup_source_field VARCHAR(128);
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS hidden_for_roles JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS read_only_for_roles JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_columns ADD COLUMN IF NOT EXISTS show_in_parent_form BOOLEAN NOT NULL DEFAULT FALSE;

            CREATE TABLE IF NOT EXISTS sys_notifications (
                id VARCHAR(64) PRIMARY KEY,
                recipient_username VARCHAR(64) DEFAULT '*',
                title VARCHAR(255) NOT NULL,
                message TEXT,
                app_code VARCHAR(64),
                record_id VARCHAR(64),
                is_read BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_attachments (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64),
                table_id VARCHAR(64),
                file_name VARCHAR(255) NOT NULL,
                content_type VARCHAR(128) NOT NULL,
                size_bytes BIGINT NOT NULL,
                storage_path VARCHAR(512) NOT NULL,
                uploaded_by VARCHAR(128) DEFAULT 'system',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_categories (
                category_code VARCHAR(128) PRIMARY KEY,
                category_name VARCHAR(255) NOT NULL,
                app_id VARCHAR(64) REFERENCES sys_apps(id) ON DELETE SET NULL,
                description TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_category_items (
                id VARCHAR(64) PRIMARY KEY,
                category_code VARCHAR(128) NOT NULL REFERENCES sys_categories(category_code) ON DELETE CASCADE,
                item_code VARCHAR(128) NOT NULL,
                item_name VARCHAR(255) NOT NULL,
                color VARCHAR(32),
                order_index INT DEFAULT 1,
                is_active BOOLEAN DEFAULT TRUE,
                UNIQUE(category_code, item_code)
            );

            CREATE TABLE IF NOT EXISTS sys_roles (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                color VARCHAR(32) DEFAULT '#1a73e8',
                is_system BOOLEAN DEFAULT FALSE,
                is_active BOOLEAN DEFAULT TRUE,
                all_apps_access BOOLEAN DEFAULT FALSE,
                default_app_permissions JSONB NOT NULL DEFAULT '[]'::jsonb,
                system_permissions JSONB NOT NULL DEFAULT '[]'::jsonb,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_role_app_permissions (
                id VARCHAR(64) PRIMARY KEY,
                role_id VARCHAR(64) NOT NULL REFERENCES sys_roles(id) ON DELETE CASCADE,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                permissions JSONB NOT NULL DEFAULT '[]'::jsonb,
                row_scope VARCHAR(32) DEFAULT 'INHERIT',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(role_id, app_id)
            );

            CREATE TABLE IF NOT EXISTS sys_role_table_permissions (
                id VARCHAR(64) PRIMARY KEY,
                role_id VARCHAR(64) NOT NULL REFERENCES sys_roles(id) ON DELETE CASCADE,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                can_view BOOLEAN DEFAULT TRUE,
                can_create BOOLEAN DEFAULT FALSE,
                can_edit BOOLEAN DEFAULT FALSE,
                can_delete BOOLEAN DEFAULT FALSE,
                can_export BOOLEAN DEFAULT FALSE,
                can_approve BOOLEAN DEFAULT FALSE,
                row_scope VARCHAR(32) DEFAULT 'INHERIT',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(role_id, table_id)
            );

            CREATE TABLE IF NOT EXISTS sys_users (
                id VARCHAR(64) PRIMARY KEY,
                username VARCHAR(64) UNIQUE NOT NULL,
                full_name VARCHAR(255) NOT NULL,
                email VARCHAR(255),
                employee_code VARCHAR(64),
                department VARCHAR(128) DEFAULT 'Toàn công ty',
                role_code VARCHAR(64) NOT NULL DEFAULT 'STAFF',
                role_codes JSONB NOT NULL DEFAULT '[]'::jsonb,
                password_hash VARCHAR(255) NOT NULL,
                allowed_apps JSONB NOT NULL DEFAULT '["*"]'::jsonb,
                permissions JSONB NOT NULL DEFAULT '["record.view","record.create","record.edit"]'::jsonb,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE sys_users ALTER COLUMN role_code TYPE VARCHAR(64);
            ALTER TABLE sys_users ADD COLUMN IF NOT EXISTS role_codes JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_users ADD COLUMN IF NOT EXISTS employee_code VARCHAR(64);
            ALTER TABLE sys_users ADD COLUMN IF NOT EXISTS sso_provider VARCHAR(64);
            ALTER TABLE sys_users ADD COLUMN IF NOT EXISTS sso_subject VARCHAR(255);

            CREATE TABLE IF NOT EXISTS sys_sso_providers (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                provider_type VARCHAR(32) NOT NULL DEFAULT 'CUSTOM_OIDC',
                display_name VARCHAR(150) NOT NULL,
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                client_id VARCHAR(255),
                client_secret VARCHAR(500),
                tenant_id VARCHAR(128),
                issuer_url VARCHAR(500),
                authorization_url VARCHAR(500),
                token_url VARCHAR(500),
                userinfo_url VARCHAR(500),
                scopes VARCHAR(300) NOT NULL DEFAULT 'openid email profile',
                allowed_domains JSONB NOT NULL DEFAULT '[]'::jsonb,
                auto_provision BOOLEAN NOT NULL DEFAULT TRUE,
                default_role_code VARCHAR(64) NOT NULL DEFAULT 'STAFF',
                button_color VARCHAR(32) DEFAULT '#1a73e8',
                order_index INT NOT NULL DEFAULT 1,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_sys_users_sso ON sys_users(sso_provider, sso_subject);
            CREATE INDEX IF NOT EXISTS idx_sys_users_email ON sys_users(LOWER(email));

            CREATE TABLE IF NOT EXISTS app_records (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                module_id VARCHAR(64) NOT NULL REFERENCES sys_modules(id) ON DELETE CASCADE,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                record_key VARCHAR(128),
                data JSONB NOT NULL DEFAULT '{}'::jsonb,
                status VARCHAR(32) DEFAULT 'ACTIVE',
                version INT NOT NULL DEFAULT 1,
                approval_status VARCHAR(32) DEFAULT 'DRAFT',
                approved_by VARCHAR(128),
                approved_at TIMESTAMPTZ,
                rejection_reason TEXT,
                deleted_at TIMESTAMPTZ,
                deleted_by VARCHAR(128),
                validation_errors JSONB NOT NULL DEFAULT '[]'::jsonb,
                created_by VARCHAR(128) DEFAULT 'system',
                updated_by VARCHAR(128) DEFAULT 'system',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS approval_status VARCHAR(32) DEFAULT 'DRAFT';
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS approved_by VARCHAR(128);
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ;
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS rejection_reason TEXT;
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS deleted_by VARCHAR(128);
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS version INT NOT NULL DEFAULT 1;
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS workflow_snapshot JSONB;

            CREATE TABLE IF NOT EXISTS app_audit_logs (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL,
                table_id VARCHAR(64) NOT NULL,
                record_id VARCHAR(64) NOT NULL,
                action VARCHAR(32) NOT NULL,
                actor VARCHAR(128) DEFAULT 'system',
                old_data JSONB,
                new_data JSONB,
                summary TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_automations (
                id VARCHAR(64) PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                source_app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                source_table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                trigger_event VARCHAR(32) NOT NULL DEFAULT 'RECORD_CREATED',
                condition_field VARCHAR(128),
                condition_operator VARCHAR(32) DEFAULT 'EQUALS',
                condition_value TEXT,
                target_app_id VARCHAR(64) REFERENCES sys_apps(id) ON DELETE CASCADE,
                target_module_id VARCHAR(64) REFERENCES sys_modules(id) ON DELETE CASCADE,
                target_table_id VARCHAR(64) REFERENCES sys_tables(id) ON DELETE CASCADE,
                action_type VARCHAR(32) NOT NULL DEFAULT 'CREATE_RECORD',
                match_source_field VARCHAR(128),
                match_target_field VARCHAR(128),
                webhook_url TEXT,
                webhook_secret VARCHAR(255),
                notify_recipient VARCHAR(128),
                notify_template TEXT,
                field_mappings JSONB NOT NULL DEFAULT '{}'::jsonb,
                is_active BOOLEAN DEFAULT TRUE,
                run_count INT DEFAULT 0,
                last_run_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE app_automations ALTER COLUMN target_app_id DROP NOT NULL;
            ALTER TABLE app_automations ALTER COLUMN target_module_id DROP NOT NULL;
            ALTER TABLE app_automations ALTER COLUMN target_table_id DROP NOT NULL;
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS match_source_field VARCHAR(128);
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS match_target_field VARCHAR(128);
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS webhook_url TEXT;
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS webhook_secret VARCHAR(255);
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS notify_recipient VARCHAR(128);
            ALTER TABLE app_automations ADD COLUMN IF NOT EXISTS notify_template TEXT;

            CREATE TABLE IF NOT EXISTS sys_revoked_tokens (
                token_hash VARCHAR(128) PRIMARY KEY,
                expires_at TIMESTAMPTZ NOT NULL,
                revoked_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_record_comments (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL,
                table_id VARCHAR(64) NOT NULL,
                record_id VARCHAR(64) NOT NULL REFERENCES app_records(id) ON DELETE CASCADE,
                author VARCHAR(128) NOT NULL,
                content TEXT NOT NULL,
                mentions JSONB NOT NULL DEFAULT '[]'::jsonb,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_app_comments_rec ON app_record_comments(record_id, created_at ASC);

            CREATE TABLE IF NOT EXISTS app_automation_logs (
                id VARCHAR(64) PRIMARY KEY,
                automation_id VARCHAR(64) NOT NULL REFERENCES app_automations(id) ON DELETE CASCADE,
                source_record_id VARCHAR(64),
                target_record_id VARCHAR(64),
                status VARCHAR(32) DEFAULT 'SUCCESS',
                message TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_widgets (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                title VARCHAR(255) NOT NULL,
                widget_type VARCHAR(32) NOT NULL DEFAULT 'KPI_COUNT',
                metric_field VARCHAR(128),
                group_field VARCHAR(128),
                filter_field VARCHAR(128),
                filter_value TEXT,
                col_span INT DEFAULT 1,
                color VARCHAR(32) DEFAULT '#1a73e8',
                order_index INT DEFAULT 1,
                is_visible BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_sys_modules_app ON sys_modules(app_id, order_index);
            CREATE INDEX IF NOT EXISTS idx_sys_tables_mod ON sys_tables(module_id, order_index);
            CREATE INDEX IF NOT EXISTS idx_sys_columns_tbl ON sys_columns(table_id, order_index);
            CREATE INDEX IF NOT EXISTS idx_app_records_data_gin ON app_records USING GIN (data jsonb_path_ops);
            CREATE INDEX IF NOT EXISTS idx_app_records_active_tbl ON app_records(table_id, updated_at DESC) WHERE deleted_at IS NULL;
            CREATE INDEX IF NOT EXISTS idx_app_records_lookup ON app_records(app_id, module_id, table_id, status);
            CREATE INDEX IF NOT EXISTS idx_app_records_key ON app_records(table_id, record_key);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_app_records_unique_key
                ON app_records(table_id, LOWER(record_key))
                WHERE deleted_at IS NULL AND record_key IS NOT NULL AND BTRIM(record_key) <> '';

            CREATE TABLE IF NOT EXISTS sys_auto_number_counters (
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                column_code VARCHAR(64) NOT NULL,
                scope_key VARCHAR(128) NOT NULL,
                current_value BIGINT NOT NULL DEFAULT 0,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (table_id, column_code, scope_key)
            );
            CREATE INDEX IF NOT EXISTS idx_app_audit_rec ON app_audit_logs(record_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_app_automations_src ON app_automations(source_table_id, trigger_event, is_active);

            CREATE TABLE IF NOT EXISTS sys_api_keys (
                id VARCHAR(64) PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                key_prefix VARCHAR(32) NOT NULL,
                key_hash VARCHAR(128) UNIQUE NOT NULL,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                table_id VARCHAR(64),
                scopes JSONB NOT NULL DEFAULT '["records:read","records:write"]'::jsonb,
                is_active BOOLEAN DEFAULT TRUE,
                last_used_at TIMESTAMPTZ,
                created_by VARCHAR(128) DEFAULT 'admin',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_saved_views (
                id VARCHAR(64) PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                name VARCHAR(255) NOT NULL,
                filters JSONB NOT NULL DEFAULT '{}'::jsonb,
                sort_field VARCHAR(128),
                sort_dir VARCHAR(8) DEFAULT 'DESC',
                is_shared BOOLEAN DEFAULT TRUE,
                created_by VARCHAR(128) DEFAULT 'admin',
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            UPDATE sys_tables SET approval_enabled = TRUE WHERE id IN ('tbl_asset_capphat', 'tbl_ops_yeucau');
            UPDATE sys_tables SET public_form_enabled = TRUE WHERE id = 'tbl_ops_yeucau';

            ALTER TABLE sys_notifications ADD COLUMN IF NOT EXISTS category VARCHAR(32) DEFAULT 'SYSTEM';
            ALTER TABLE sys_notifications ADD COLUMN IF NOT EXISTS link_url VARCHAR(512);
            ALTER TABLE sys_notifications ADD COLUMN IF NOT EXISTS sender_username VARCHAR(64);
            ALTER TABLE sys_notifications ADD COLUMN IF NOT EXISTS priority VARCHAR(16) DEFAULT 'NORMAL';
            ALTER TABLE sys_notifications ADD COLUMN IF NOT EXISTS metadata JSONB NOT NULL DEFAULT '{}'::jsonb;

            CREATE TABLE IF NOT EXISTS sys_user_settings (
                user_id VARCHAR(64) PRIMARY KEY REFERENCES sys_users(id) ON DELETE CASCADE,
                theme_mode VARCHAR(16) DEFAULT 'light',
                accent_color VARCHAR(32) DEFAULT '#1a73e8',
                compact_mode BOOLEAN DEFAULT FALSE,
                language VARCHAR(16) DEFAULT 'vi',
                notify_in_app BOOLEAN DEFAULT TRUE,
                notify_sound BOOLEAN DEFAULT TRUE,
                notify_desktop BOOLEAN DEFAULT FALSE,
                notify_chat BOOLEAN DEFAULT TRUE,
                notify_task_assigned BOOLEAN DEFAULT TRUE,
                notify_approval BOOLEAN DEFAULT TRUE,
                notify_mentions BOOLEAN DEFAULT TRUE,
                email_digest VARCHAR(16) DEFAULT 'INSTANT',
                chat_enter_to_send BOOLEAN DEFAULT TRUE,
                notify_email BOOLEAN DEFAULT FALSE,
                notify_telegram BOOLEAN DEFAULT FALSE,
                telegram_chat_id VARCHAR(128),
                notify_zalo BOOLEAN DEFAULT FALSE,
                zalo_user_id VARCHAR(128),
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE sys_user_settings ADD COLUMN IF NOT EXISTS notify_email BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_user_settings ADD COLUMN IF NOT EXISTS notify_telegram BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_user_settings ADD COLUMN IF NOT EXISTS telegram_chat_id VARCHAR(128);
            ALTER TABLE sys_user_settings ADD COLUMN IF NOT EXISTS notify_zalo BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_user_settings ADD COLUMN IF NOT EXISTS zalo_user_id VARCHAR(128);

            CREATE TABLE IF NOT EXISTS sys_notification_channels (
                id VARCHAR(64) PRIMARY KEY,
                owner_scope VARCHAR(16) NOT NULL DEFAULT 'SYSTEM',
                owner_user_id VARCHAR(64),
                channel_type VARCHAR(32) NOT NULL,
                name VARCHAR(150) NOT NULL,
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                event_categories JSONB NOT NULL DEFAULT '["ALL"]'::jsonb,
                config JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_notification_delivery_logs (
                id VARCHAR(64) PRIMARY KEY,
                channel_id VARCHAR(64) NOT NULL,
                channel_type VARCHAR(32) NOT NULL,
                channel_name VARCHAR(150) NOT NULL,
                title VARCHAR(255) NOT NULL,
                status VARCHAR(16) NOT NULL,
                detail TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_system_settings (
                setting_key VARCHAR(128) PRIMARY KEY,
                setting_value JSONB NOT NULL DEFAULT '{}'::jsonb,
                description TEXT,
                updated_by VARCHAR(128),
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_ai_providers (
                id VARCHAR(64) PRIMARY KEY,
                code VARCHAR(64) UNIQUE NOT NULL,
                provider_type VARCHAR(32) NOT NULL DEFAULT 'OPENAI',
                display_name VARCHAR(150) NOT NULL,
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                is_default BOOLEAN NOT NULL DEFAULT FALSE,
                api_key TEXT,
                base_url VARCHAR(500) NOT NULL,
                model VARCHAR(150) NOT NULL,
                temperature DOUBLE PRECISION,
                max_tokens INT,
                timeout_seconds INT NOT NULL DEFAULT 45,
                organization_id VARCHAR(150),
                order_index INT NOT NULL DEFAULT 10,
                updated_by VARCHAR(128),
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS sys_ai_logs (
                id VARCHAR(64) PRIMARY KEY,
                provider_id VARCHAR(64) NOT NULL,
                provider_name VARCHAR(150) NOT NULL,
                feature_code VARCHAR(64) NOT NULL,
                model VARCHAR(150) NOT NULL,
                username VARCHAR(128) DEFAULT 'system',
                status VARCHAR(24) NOT NULL,
                latency_ms BIGINT NOT NULL DEFAULT 0,
                prompt_tokens INT NOT NULL DEFAULT 0,
                completion_tokens INT NOT NULL DEFAULT 0,
                message TEXT,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_sys_ai_logs_created ON sys_ai_logs(created_at DESC);

            CREATE TABLE IF NOT EXISTS chat_channels (
                id VARCHAR(64) PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                channel_type VARCHAR(24) NOT NULL DEFAULT 'GROUP',
                avatar_color VARCHAR(32) DEFAULT '#1a73e8',
                linked_app_code VARCHAR(64),
                created_by VARCHAR(64) NOT NULL,
                last_message_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                last_message_preview TEXT,
                is_archived BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS chat_channel_members (
                channel_id VARCHAR(64) NOT NULL REFERENCES chat_channels(id) ON DELETE CASCADE,
                username VARCHAR(64) NOT NULL,
                role VARCHAR(24) DEFAULT 'MEMBER',
                last_read_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                is_muted BOOLEAN DEFAULT FALSE,
                joined_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (channel_id, username)
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id VARCHAR(64) PRIMARY KEY,
                channel_id VARCHAR(64) NOT NULL REFERENCES chat_channels(id) ON DELETE CASCADE,
                sender_username VARCHAR(64) NOT NULL,
                message_type VARCHAR(24) NOT NULL DEFAULT 'TEXT',
                content TEXT NOT NULL,
                reply_to_id VARCHAR(64),
                task_id VARCHAR(64),
                attachments JSONB NOT NULL DEFAULT '[]'::jsonb,
                mentions JSONB NOT NULL DEFAULT '[]'::jsonb,
                reactions JSONB NOT NULL DEFAULT '{}'::jsonb,
                is_pinned BOOLEAN DEFAULT FALSE,
                is_edited BOOLEAN DEFAULT FALSE,
                deleted_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS app_tasks (
                id VARCHAR(64) PRIMARY KEY,
                task_code VARCHAR(32) UNIQUE NOT NULL,
                title VARCHAR(255) NOT NULL,
                description TEXT,
                status VARCHAR(24) NOT NULL DEFAULT 'TODO',
                priority VARCHAR(16) NOT NULL DEFAULT 'MEDIUM',
                assignee_username VARCHAR(64) NOT NULL,
                assigner_username VARCHAR(64) NOT NULL,
                watchers JSONB NOT NULL DEFAULT '[]'::jsonb,
                due_date DATE,
                progress INT DEFAULT 0,
                checklist JSONB NOT NULL DEFAULT '[]'::jsonb,
                channel_id VARCHAR(64) REFERENCES chat_channels(id) ON DELETE SET NULL,
                message_id VARCHAR(64),
                linked_app_code VARCHAR(64),
                linked_record_id VARCHAR(64),
                completed_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT TRUE;
            ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS all_apps_access BOOLEAN DEFAULT FALSE;
            ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS default_app_permissions JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS system_permissions JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;

            CREATE TABLE IF NOT EXISTS app_workflows (
                id VARCHAR(64) PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                app_id VARCHAR(64) NOT NULL REFERENCES sys_apps(id) ON DELETE CASCADE,
                table_id VARCHAR(64) NOT NULL REFERENCES sys_tables(id) ON DELETE CASCADE,
                trigger_type VARCHAR(32) DEFAULT 'MANUAL_SUBMIT',
                condition_field VARCHAR(128),
                condition_operator VARCHAR(32) DEFAULT 'EQUALS',
                condition_value TEXT,
                steps JSONB NOT NULL DEFAULT '[]'::jsonb,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS workflow_id VARCHAR(64);
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS current_step_order INT DEFAULT 0;
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS current_step_name VARCHAR(255);
            ALTER TABLE app_records ADD COLUMN IF NOT EXISTS workflow_history JSONB NOT NULL DEFAULT '[]'::jsonb;

            CREATE INDEX IF NOT EXISTS idx_sys_noti_rec ON sys_notifications(recipient_username, is_read, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_msg_ch ON chat_messages(channel_id, created_at ASC);
            CREATE INDEX IF NOT EXISTS idx_chat_msg_history ON chat_messages(channel_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
            CREATE INDEX IF NOT EXISTS idx_chat_mem_user ON chat_channel_members(username);
            CREATE INDEX IF NOT EXISTS idx_app_tasks_assignee ON app_tasks(assignee_username, status);
            CREATE INDEX IF NOT EXISTS idx_app_tasks_channel ON app_tasks(channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_app_workflows_tbl ON app_workflows(table_id, is_active);
        """;

    private static async Task EnsureCrossAppLinksAndAutomationsAsync(NpgsqlConnection conn)
    {
        // Upgrade cross-app LOOKUP columns so HRM, ASSET, OPS share master data seamlessly
        var upgradeSql = """
            UPDATE sys_columns
            SET data_type = 'LOOKUP', ref_app_id = 'app_hrm', ref_table_id = 'tbl_hrm_nhanvien',
                ref_value_field = 'ma_nv', ref_display_field = 'ho_ten'
            WHERE id IN ('col_cp_nguoi', 'col_np_nv') AND (ref_table_id IS NULL OR data_type <> 'LOOKUP');

            UPDATE sys_columns
            SET data_type = 'LOOKUP', ref_app_id = 'app_asset', ref_table_id = 'tbl_asset_danhmuc',
                ref_value_field = 'ma_ts', ref_display_field = 'ten_ts'
            WHERE id = 'col_cp_ts' AND (ref_table_id IS NULL OR data_type <> 'LOOKUP');

            UPDATE sys_columns
            SET data_type = 'LOOKUP', ref_app_id = 'app_hrm', ref_table_id = 'tbl_hrm_nhanvien',
                ref_value_field = 'ma_nv', ref_display_field = 'ho_ten', column_name = 'Nhân viên đề xuất'
            WHERE id = 'col_op_nguoi';

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, order_index)
            VALUES ('col_op_mats', 'tbl_ops_yeucau', 'ma_ts', 'Tài sản liên quan', 'Tài sản liên quan', 'LOOKUP', FALSE, 195, 'app_asset', 'tbl_asset_danhmuc', 'ma_ts', 'ten_ts', 6)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            -- Normalize sample records to link by ma_nv / ma_ts
            UPDATE app_records SET data = jsonb_set(data, '{nguoi_nhan}', '"NV001"')
            WHERE id = 'rec_cp_1' AND data->>'nguoi_nhan' = 'Nguyễn Văn An';

            UPDATE app_records SET data = jsonb_set(data, '{ho_ten_nv}', '"NV001"')
            WHERE id = 'rec_np_1' AND data->>'ho_ten_nv' = 'Nguyễn Văn An';

            UPDATE app_records SET data = jsonb_set(data, '{ho_ten_nv}', '"NV003"')
            WHERE id = 'rec_np_2' AND data->>'ho_ten_nv' = 'Lê Hoàng Nam';

            UPDATE app_records SET data = jsonb_set(jsonb_set(data, '{nguoi_tao}', '"NV001"'), '{ma_ts}', '"TS-SRV-01"')
            WHERE id = 'rec_op_1' AND data->>'nguoi_tao' = 'Nguyễn Văn An';

            UPDATE app_records SET data = jsonb_set(data, '{nguoi_tao}', '"NV002"')
            WHERE id = 'rec_op_2' AND data->>'nguoi_tao' = 'Trần Thị Minh';
            """;

        await using (var cmd = new NpgsqlCommand(upgradeSql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed sample multi-step workflows if empty
        await using var wfCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM app_workflows", conn);
        if (Convert.ToInt32(await wfCountCmd.ExecuteScalarAsync()) == 0)
        {
            var opsSteps = JsonSerializer.Serialize(new[]
            {
                new { stepOrder = 1, name = "Trưởng bộ phận / HR rà soát", approverRoles = new[] { "HR_MANAGER", "ASSET_LEAD" }, approverUsers = Array.Empty<string>(), slaHours = 24, requireCommentOnReject = true },
                new { stepOrder = 2, name = "Ban Quản trị Phê duyệt cuối", approverRoles = new[] { "ADMIN" }, approverUsers = Array.Empty<string>(), slaHours = 48, requireCommentOnReject = true }
            });
            var assetSteps = JsonSerializer.Serialize(new[]
            {
                new { stepOrder = 1, name = "Quản lý Tài sản IT xác nhận thiết bị", approverRoles = new[] { "ASSET_LEAD" }, approverUsers = Array.Empty<string>(), slaHours = 12, requireCommentOnReject = false },
                new { stepOrder = 2, name = "Quản trị Hệ thống / Kế toán chốt bàn giao", approverRoles = new[] { "ADMIN" }, approverUsers = Array.Empty<string>(), slaHours = 24, requireCommentOnReject = true }
            });

            await using var wfIns = new NpgsqlCommand("""
                INSERT INTO app_workflows (id, name, description, app_id, table_id, trigger_type, steps, is_active)
                VALUES
                ('wf_ops_approval', 'Quy trình Duyệt Yêu cầu Nội bộ (2 bước)', 'Rà soát cấp bộ phận trước khi Ban Quản trị phê duyệt thực hiện.', 'app_ops', 'tbl_ops_yeucau', 'MANUAL_SUBMIT', @s1::jsonb, TRUE),
                ('wf_asset_alloc', 'Quy trình Cấp phát Tài sản (2 bước)', 'Quản lý Tài sản xác nhận kho trước khi Admin chốt cấp phát.', 'app_asset', 'tbl_asset_capphat', 'MANUAL_SUBMIT', @s2::jsonb, TRUE)
                ON CONFLICT DO NOTHING
                """, conn);
            wfIns.Parameters.AddWithValue("s1", opsSteps);
            wfIns.Parameters.AddWithValue("s2", assetSteps);
            await wfIns.ExecuteNonQueryAsync();
        }

        // Seed default cross-app automations if empty
        await using var autoCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM app_automations", conn);
        var autoCount = Convert.ToInt32(await autoCountCmd.ExecuteScalarAsync());
        if (autoCount == 0)
        {
            var auto1Map = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ma_ticket"] = "{{AUTO_KEY}}",
                ["tieu_de"] = "Onboarding: Cấp Laptop & Email cho {{ho_ten}} ({{ma_nv}})",
                ["loai_yeu_cau"] = "IT_SUPPORT",
                ["muc_do_uu_tien"] = "HIGH",
                ["nguoi_tao"] = "{{ma_nv}}",
                ["han_xu_ly"] = "{{TODAY}}",
                ["da_hoan_thanh"] = "false",
                ["mo_ta"] = "Tự động tạo từ App HRM khi tiếp nhận nhân sự mới {{ho_ten}} ({{email}})."
            });

            var auto2Map = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ma_ticket"] = "{{AUTO_KEY}}",
                ["tieu_de"] = "Thông báo: Đã duyệt đơn nghỉ phép {{ma_don}} ({{so_ngay}} ngày)",
                ["loai_yeu_cau"] = "HANH_CHINH",
                ["muc_do_uu_tien"] = "LOW",
                ["nguoi_tao"] = "{{ho_ten_nv}}",
                ["han_xu_ly"] = "{{tu_ngay}}",
                ["da_hoan_thanh"] = "true",
                ["mo_ta"] = "Tự động ghi nhận bàn giao công việc trong thời gian nghỉ từ {{tu_ngay}} đến {{den_ngay}}."
            });

            await using var ins = new NpgsqlCommand("""
                INSERT INTO app_automations (id, name, description, source_app_id, source_table_id, trigger_event,
                    condition_field, condition_operator, condition_value,
                    target_app_id, target_module_id, target_table_id, action_type, field_mappings, is_active)
                VALUES
                ('auto_hrm_onboarding', 'Onboarding: Nhân viên mới → Tạo Ticket IT (HRM → OPS)',
                 'Khi thêm nhân viên mới ở HRM, tự động sinh yêu cầu cấp phát thiết bị & tài khoản bên OPS.',
                 'app_hrm', 'tbl_hrm_nhanvien', 'RECORD_CREATED', NULL, 'EQUALS', NULL,
                 'app_ops', 'mod_ops_ticket', 'tbl_ops_yeucau', 'CREATE_RECORD', @m1::jsonb, TRUE),
                ('auto_hrm_leave_notify', 'Duyệt Nghỉ phép → Ghi nhận Vận hành (HRM → OPS)',
                 'Khi đơn nghỉ phép được chuyển sang DA_DUYET, tự động tạo ghi nhận bàn giao bên OPS.',
                 'app_hrm', 'tbl_hrm_nghiphep', 'RECORD_UPDATED', 'trang_thai_duyet', 'EQUALS', 'DA_DUYET',
                 'app_ops', 'mod_ops_ticket', 'tbl_ops_yeucau', 'CREATE_RECORD', @m2::jsonb, TRUE)
                ON CONFLICT DO NOTHING
                """, conn);
            ins.Parameters.AddWithValue("m1", auto1Map);
            ins.Parameters.AddWithValue("m2", auto2Map);
            await ins.ExecuteNonQueryAsync();
        }

        // Seed 3rd automation (UPDATE_RECORD when allocating asset) if missing
        var auto3Map = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["tinh_trang"] = "DANG_DUNG"
        });
        await using (var ins3 = new NpgsqlCommand("""
            INSERT INTO app_automations (id, name, description, source_app_id, source_table_id, trigger_event,
                target_app_id, target_module_id, target_table_id, action_type, match_source_field, match_target_field, field_mappings, is_active)
            VALUES ('auto_asset_status', 'Cấp phát Tài sản → Tự động chuyển trạng thái Đang sử dụng',
                'Khi lập phiếu cấp phát thiết bị, tự động cập nhật trạng thái tài sản tương ứng bên Danh mục sang DANG_DUNG.',
                'app_asset', 'tbl_asset_capphat', 'RECORD_CREATED',
                'app_asset', 'mod_asset_dm', 'tbl_asset_danhmuc', 'UPDATE_RECORD', 'ma_ts', 'ma_ts', @m3::jsonb, TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            ins3.Parameters.AddWithValue("m3", auto3Map);
            await ins3.ExecuteNonQueryAsync();
        }

        // Seed default enterprise roles & per-app permissions if not yet initialized
        await using var rapCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_role_app_permissions", conn);
        var rapCount = Convert.ToInt32(await rapCountCmd.ExecuteScalarAsync());
        await using var roleCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_roles", conn);
        var roleCount = Convert.ToInt32(await roleCountCmd.ExecuteScalarAsync());

        if (roleCount == 0 || rapCount == 0)
        {
            var allAppPerms = JsonSerializer.Serialize(new[]
            {
                "record.view", "record.view_all", "record.create", "record.edit",
                "record.delete", "record.export", "record.approve", "widget.manage"
            });
            var adminSysPerms = JsonSerializer.Serialize(new[]
            {
                "studio.manage", "user.manage", "role.manage",
                "workflow.manage", "automation.manage", "audit.view", "integration.manage"
            });

            var defaultRoles = new (string Id, string Code, string Name, string Desc, string Color, bool IsSys, bool AllApps, string DefAppPerms, string SysPerms)[]
            {
                ("role_admin", "ADMIN", "Quản trị Hệ thống", "Toàn quyền thiết kế App Studio, quản lý User, Role và mọi ứng dụng.", "#d93025", true, true, allAppPerms, adminSysPerms),
                ("role_hr_manager", "HR_MANAGER", "Quản lý Nhân sự", "Toàn quyền trên HRM (bao gồm phê duyệt), tạo & xem yêu cầu trên OPS.", "#1a73e8", false, false, "[]", "[]"),
                ("role_asset_lead", "ASSET_LEAD", "Quản lý Tài sản & IT", "Toàn quyền trên ASSET (bao gồm phê duyệt), xử lý ticket trên OPS.", "#00897b", false, false, "[]", "[]"),
                ("role_staff", "STAFF", "Nhân viên", "Xem và tạo mới phiếu cá nhân trên OPS & HRM (tuân thủ OWNER_ONLY).", "#5f6368", false, false, "[]", "[]")
            };

            foreach (var r in defaultRoles)
            {
                await using var rCmd = new NpgsqlCommand("""
                    INSERT INTO sys_roles (id, code, name, description, color, is_system, all_apps_access, default_app_permissions, system_permissions)
                    VALUES (@id, @c, @n, @d, @col, @sys, @all, @dap::jsonb, @sp::jsonb)
                    ON CONFLICT (code) DO UPDATE SET
                        is_system = EXCLUDED.is_system,
                        all_apps_access = EXCLUDED.all_apps_access,
                        default_app_permissions = CASE WHEN sys_roles.default_app_permissions = '[]'::jsonb THEN EXCLUDED.default_app_permissions ELSE sys_roles.default_app_permissions END,
                        system_permissions = CASE WHEN sys_roles.system_permissions = '[]'::jsonb THEN EXCLUDED.system_permissions ELSE sys_roles.system_permissions END
                    """, conn);
                rCmd.Parameters.AddWithValue("id", r.Id);
                rCmd.Parameters.AddWithValue("c", r.Code);
                rCmd.Parameters.AddWithValue("n", r.Name);
                rCmd.Parameters.AddWithValue("d", r.Desc);
                rCmd.Parameters.AddWithValue("col", r.Color);
                rCmd.Parameters.AddWithValue("sys", r.IsSys);
                rCmd.Parameters.AddWithValue("all", r.AllApps);
                rCmd.Parameters.AddWithValue("dap", r.DefAppPerms);
                rCmd.Parameters.AddWithValue("sp", r.SysPerms);
                await rCmd.ExecuteNonQueryAsync();
            }

            // Seed per-app permissions for each non-wildcard role (lookup actual role id by code)
            var roleAppMatrix = new (string RoleCode, string AppId, string[] Perms, string RowScope)[]
            {
                ("HR_MANAGER", "app_hrm", new[] { "app.admin", "record.view", "record.view_all", "record.create", "record.edit", "record.delete", "record.export", "record.approve", "widget.manage" }, "ALL"),
                ("HR_MANAGER", "app_ops", new[] { "record.view", "record.view_all", "record.create", "record.export" }, "ALL"),
                ("ASSET_LEAD", "app_asset", new[] { "app.admin", "record.view", "record.view_all", "record.create", "record.edit", "record.delete", "record.export", "record.approve", "widget.manage" }, "ALL"),
                ("ASSET_LEAD", "app_ops", new[] { "record.view", "record.view_all", "record.create", "record.edit", "record.export" }, "ALL"),
                ("STAFF", "app_ops", new[] { "record.view", "record.create" }, "OWNER_ONLY"),
                ("STAFF", "app_hrm", new[] { "record.view", "record.create" }, "OWNER_ONLY")
            };

            foreach (var ra in roleAppMatrix)
            {
                await using var checkApp = new NpgsqlCommand("SELECT 1 FROM sys_apps WHERE id = @aid LIMIT 1", conn);
                checkApp.Parameters.AddWithValue("aid", ra.AppId);
                if (await checkApp.ExecuteScalarAsync() == null) continue;

                await using var getRid = new NpgsqlCommand("SELECT id FROM sys_roles WHERE UPPER(code) = UPPER(@rc) LIMIT 1", conn);
                getRid.Parameters.AddWithValue("rc", ra.RoleCode);
                var roleId = (await getRid.ExecuteScalarAsync())?.ToString();
                if (string.IsNullOrWhiteSpace(roleId)) continue;

                await using var raCmd = new NpgsqlCommand("""
                    INSERT INTO sys_role_app_permissions (id, role_id, app_id, permissions, row_scope)
                    VALUES (@id, @rid, @aid, @perms::jsonb, @rs)
                    ON CONFLICT (role_id, app_id) DO NOTHING
                    """, conn);
                raCmd.Parameters.AddWithValue("id", $"rap_{roleId}_{ra.AppId}");
                raCmd.Parameters.AddWithValue("rid", roleId);
                raCmd.Parameters.AddWithValue("aid", ra.AppId);
                raCmd.Parameters.AddWithValue("perms", JsonSerializer.Serialize(ra.Perms));
                raCmd.Parameters.AddWithValue("rs", ra.RowScope);
                await raCmd.ExecuteNonQueryAsync();
            }
        }

        // Seed Lịch công tác (tbl_hrm_congtac) in HRM and per-table RBAC defaults if not yet initialized
        await using (var hrmCheck = new NpgsqlCommand("SELECT 1 FROM sys_modules WHERE id = 'mod_hrm_nghiphep' LIMIT 1", conn))
        {
            if (await hrmCheck.ExecuteScalarAsync() != null)
            {
                await using var ctCmd = new NpgsqlCommand("""
                    INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
                    VALUES ('tbl_hrm_congtac', 'app_hrm', 'mod_hrm_nghiphep', 'hrm_lich_cong_tac', 'Lịch công tác', 'Theo dõi lịch trình công tác và làm việc bên ngoài của nhân sự', 'table', 'OWNER_ONLY', TRUE, 2)
                    ON CONFLICT (id) DO NOTHING;

                    INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, default_value, order_index)
                    VALUES
                    ('col_ct_ma', 'tbl_hrm_congtac', 'ma_lich', 'Mã lịch', 'Mã lịch', 'STRING', TRUE, TRUE, 125, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                    ('col_ct_nv', 'tbl_hrm_congtac', 'nhan_vien', 'Nhân viên công tác', 'Nhân viên công tác', 'LOOKUP', TRUE, FALSE, 195, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 2),
                    ('col_ct_nd', 'tbl_hrm_congtac', 'noi_dung', 'Nội dung công tác', 'Nội dung công tác', 'STRING', TRUE, FALSE, 240, NULL, NULL, NULL, NULL, NULL, NULL, 3),
                    ('col_ct_dd', 'tbl_hrm_congtac', 'dia_diem', 'Địa điểm', 'Địa điểm', 'STRING', TRUE, FALSE, 170, NULL, NULL, NULL, NULL, NULL, NULL, 4),
                    ('col_ct_tu', 'tbl_hrm_congtac', 'tu_ngay', 'Từ ngày', 'Từ ngày', 'DATE', TRUE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                    ('col_ct_den', 'tbl_hrm_congtac', 'den_ngay', 'Đến ngày', 'Đến ngày', 'DATE', TRUE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, 6),
                    ('col_ct_st', 'tbl_hrm_congtac', 'trang_thai', 'Trạng thái', 'Trạng thái', 'SELECT', FALSE, FALSE, 150, NULL, NULL, NULL, NULL, 'DM_TRANG_THAI_DUYET', 'CHO_DUYET', 7)
                    ON CONFLICT (table_id, column_code) DO NOTHING;

                    INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status, approval_status, created_by)
                    VALUES
                    ('rec_ct_1', 'app_hrm', 'mod_hrm_nghiphep', 'tbl_hrm_congtac', 'CT-2026-01', '{"ma_lich":"CT-2026-01","nhan_vien":"NV001","noi_dung":"Triển khai hạ tầng Cloud chi nhánh Đà Nẵng","dia_diem":"Đà Nẵng","tu_ngay":"2026-11-18","den_ngay":"2026-11-20","trang_thai":"DA_DUYET"}'::jsonb, 'ACTIVE', 'APPROVED', 'hr_manager'),
                    ('rec_ct_2', 'app_hrm', 'mod_hrm_nghiphep', 'tbl_hrm_congtac', 'CT-2026-02', '{"ma_lich":"CT-2026-02","nhan_vien":"NV003","noi_dung":"Kiểm toán & đối soát sổ sách đối tác Q4","dia_diem":"Hà Nội","tu_ngay":"2026-11-21","den_ngay":"2026-11-23","trang_thai":"DA_DUYET"}'::jsonb, 'ACTIVE', 'APPROVED', 'hr_manager'),
                    ('rec_ct_3', 'app_hrm', 'mod_hrm_nghiphep', 'tbl_hrm_congtac', 'CT-2026-03', '{"ma_lich":"CT-2026-03","nhan_vien":"NV002","noi_dung":"Hội thảo Nhân sự Enterprise Summit 2026","dia_diem":"TP. Hồ Chí Minh","tu_ngay":"2026-11-27","den_ngay":"2026-11-28","trang_thai":"DA_DUYET"}'::jsonb, 'ACTIVE', 'APPROVED', 'hr_manager')
                    ON CONFLICT (id) DO NOTHING;
                    """, conn);
                await ctCmd.ExecuteNonQueryAsync();
            }
        }

        await using var rtpCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_role_table_permissions", conn);
        var rtpCount = Convert.ToInt32(await rtpCountCmd.ExecuteScalarAsync());
        if (rtpCount == 0)
        {
            // Ensure app.admin is present for ADMIN, HR_MANAGER (HRM), ASSET_LEAD (ASSET)
            await using (var admUpgrade = new NpgsqlCommand("""
                UPDATE sys_roles
                SET default_app_permissions = default_app_permissions || '["app.admin"]'::jsonb
                WHERE UPPER(code) = 'ADMIN'
                  AND NOT (default_app_permissions ? 'app.admin');

                UPDATE sys_role_app_permissions rap
                SET permissions = rap.permissions || '["app.admin"]'::jsonb
                FROM sys_roles r
                WHERE rap.role_id = r.id
                  AND ((UPPER(r.code) = 'HR_MANAGER' AND rap.app_id = 'app_hrm')
                    OR (UPPER(r.code) = 'ASSET_LEAD' AND rap.app_id = 'app_asset'))
                  AND NOT (rap.permissions ? 'app.admin');

                UPDATE sys_tables
                SET row_scope = 'OWNER_ONLY'
                WHERE id IN ('tbl_hrm_nhanvien', 'tbl_hrm_nghiphep', 'tbl_hrm_congtac');
                """, conn))
            {
                await admUpgrade.ExecuteNonQueryAsync();
            }

            // Seed granular per-table rules for STAFF role:
            // - HRM Hồ sơ Nhân viên: View own only (no create/edit/delete)
            // - HRM Đơn xin Nghỉ phép: View own + Create own (OWNER_ONLY)
            // - HRM Lịch công tác: View own only (OWNER_ONLY)
            // - OPS Yêu cầu Nội bộ: View own + Create own (OWNER_ONLY)
            var staffTableRules = new (string AppId, string TableId, bool View, bool Create, bool Edit, bool Delete, bool Export, bool Approve, string Scope)[]
            {
                ("app_hrm", "tbl_hrm_nhanvien", true, false, false, false, false, false, "OWNER_ONLY"),
                ("app_hrm", "tbl_hrm_nghiphep", true, true, false, false, false, false, "OWNER_ONLY"),
                ("app_hrm", "tbl_hrm_congtac", true, false, false, false, false, false, "OWNER_ONLY"),
                ("app_ops", "tbl_ops_yeucau", true, true, false, false, false, false, "OWNER_ONLY")
            };

            await using var getStaffRid = new NpgsqlCommand("SELECT id FROM sys_roles WHERE UPPER(code) = 'STAFF' LIMIT 1", conn);
            var staffRoleId = (await getStaffRid.ExecuteScalarAsync())?.ToString();
            if (!string.IsNullOrWhiteSpace(staffRoleId))
            {
                foreach (var rule in staffTableRules)
                {
                    await using var chkTbl = new NpgsqlCommand("SELECT 1 FROM sys_tables WHERE id = @tid LIMIT 1", conn);
                    chkTbl.Parameters.AddWithValue("tid", rule.TableId);
                    if (await chkTbl.ExecuteScalarAsync() == null) continue;

                    await using var insRtp = new NpgsqlCommand("""
                        INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
                        VALUES (@id, @rid, @aid, @tid, @v, @c, @e, @d, @ex, @ap, @rs)
                        ON CONFLICT (role_id, table_id) DO NOTHING
                        """, conn);
                    insRtp.Parameters.AddWithValue("id", $"rtp_{staffRoleId}_{rule.TableId}");
                    insRtp.Parameters.AddWithValue("rid", staffRoleId);
                    insRtp.Parameters.AddWithValue("aid", rule.AppId);
                    insRtp.Parameters.AddWithValue("tid", rule.TableId);
                    insRtp.Parameters.AddWithValue("v", rule.View);
                    insRtp.Parameters.AddWithValue("c", rule.Create);
                    insRtp.Parameters.AddWithValue("e", rule.Edit);
                    insRtp.Parameters.AddWithValue("d", rule.Delete);
                    insRtp.Parameters.AddWithValue("ex", rule.Export);
                    insRtp.Parameters.AddWithValue("ap", rule.Approve);
                    insRtp.Parameters.AddWithValue("rs", rule.Scope);
                    await insRtp.ExecuteNonQueryAsync();
                }
            }
        }

        // Ensure "Giao việc & Tiến độ" (TASKS) is registered as a first-class Company App & RBAC-managed
        await using (var tasksAppCmd = new NpgsqlCommand("""
            INSERT INTO sys_apps (id, code, name, description, department, icon, color, bg_color, order_index)
            VALUES ('app_tasks', 'TASKS', 'Giao việc & Tiến độ', 'Theo dõi tiến độ giao việc đa phòng ban, bảng Kanban, Checklist & tích hợp Chat nội bộ.', 'Điều hành & Công việc', 'FactCheck', '#ea8600', '#fef7e0', 4)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, description)
            VALUES ('mod_tasks_board', 'app_tasks', 'CONGVIEC', 'Quản lý Công việc & Tiến độ', 'Assignment', 1, 'Bảng điều phối công việc liên phòng ban')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
            VALUES ('tbl_tasks_congviec', 'app_tasks', 'mod_tasks_board', 'app_tasks', 'Danh sách Công việc & Tiến độ', 'Công việc giao qua App hoặc Chat nội bộ', 'kanban', 'OWNER_ONLY', FALSE, 1)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, order_index)
            VALUES
            ('col_tsk_code', 'tbl_tasks_congviec', 'task_code', 'Mã việc', 'Mã việc', 'STRING', TRUE, TRUE, 120, 1),
            ('col_tsk_title', 'tbl_tasks_congviec', 'title', 'Tiêu đề công việc', 'Tiêu đề công việc', 'STRING', TRUE, FALSE, 260, 2),
            ('col_tsk_status', 'tbl_tasks_congviec', 'status', 'Trạng thái', 'Trạng thái', 'STRING', TRUE, FALSE, 140, 3),
            ('col_tsk_priority', 'tbl_tasks_congviec', 'priority', 'Mức ưu tiên', 'Mức ưu tiên', 'STRING', FALSE, FALSE, 120, 4),
            ('col_tsk_assignee', 'tbl_tasks_congviec', 'assignee_username', 'Người nhận', 'Người nhận', 'STRING', TRUE, FALSE, 160, 5),
            ('col_tsk_assigner', 'tbl_tasks_congviec', 'assigner_username', 'Người giao', 'Người giao', 'STRING', FALSE, FALSE, 160, 6),
            ('col_tsk_due', 'tbl_tasks_congviec', 'due_date', 'Hạn hoàn thành', 'Hạn hoàn thành', 'DATE', FALSE, FALSE, 130, 7),
            ('col_tsk_progress', 'tbl_tasks_congviec', 'progress', 'Tiến độ (%)', 'Tiến độ (%)', 'NUMBER', FALSE, FALSE, 120, 8)
            ON CONFLICT (table_id, column_code) DO NOTHING;
            """, conn))
        {
            await tasksAppCmd.ExecuteNonQueryAsync();
        }

        await using (var chkTasksPerm = new NpgsqlCommand("SELECT 1 FROM sys_role_app_permissions WHERE app_id = 'app_tasks' LIMIT 1", conn))
        {
            if (await chkTasksPerm.ExecuteScalarAsync() == null)
            {
                var tasksRoleRules = new (string RoleCode, string[] AppPerms, string Scope, bool View, bool Create, bool Edit, bool Delete, bool Export)[]
                {
                    ("HR_MANAGER", new[] { "record.view", "record.view_all", "record.create", "record.edit", "record.delete", "record.export" }, "ALL", true, true, true, true, true),
                    ("ASSET_LEAD", new[] { "record.view", "record.view_all", "record.create", "record.edit", "record.delete", "record.export" }, "ALL", true, true, true, true, true),
                    ("STAFF", new[] { "record.view", "record.create", "record.edit" }, "OWNER_ONLY", true, true, true, false, false)
                };

                foreach (var tr in tasksRoleRules)
                {
                    await using var getRid = new NpgsqlCommand("SELECT id FROM sys_roles WHERE UPPER(code) = UPPER(@rc) LIMIT 1", conn);
                    getRid.Parameters.AddWithValue("rc", tr.RoleCode);
                    var rid = (await getRid.ExecuteScalarAsync())?.ToString();
                    if (string.IsNullOrWhiteSpace(rid)) continue;

                    await using var rapCmd = new NpgsqlCommand("""
                        INSERT INTO sys_role_app_permissions (id, role_id, app_id, permissions, row_scope)
                        VALUES (@id, @rid, 'app_tasks', @perms::jsonb, @rs)
                        ON CONFLICT (role_id, app_id) DO NOTHING;

                        INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
                        VALUES (@tid, @rid, 'app_tasks', 'tbl_tasks_congviec', @v, @c, @e, @d, @ex, FALSE, @rs)
                        ON CONFLICT (role_id, table_id) DO NOTHING;
                        """, conn);
                    rapCmd.Parameters.AddWithValue("id", $"rap_{rid}_app_tasks");
                    rapCmd.Parameters.AddWithValue("tid", $"rtp_{rid}_tbl_tasks_congviec");
                    rapCmd.Parameters.AddWithValue("rid", rid);
                    rapCmd.Parameters.AddWithValue("perms", JsonSerializer.Serialize(tr.AppPerms));
                    rapCmd.Parameters.AddWithValue("rs", tr.Scope);
                    rapCmd.Parameters.AddWithValue("v", tr.View);
                    rapCmd.Parameters.AddWithValue("c", tr.Create);
                    rapCmd.Parameters.AddWithValue("e", tr.Edit);
                    rapCmd.Parameters.AddWithValue("d", tr.Delete);
                    rapCmd.Parameters.AddWithValue("ex", tr.Export);
                    await rapCmd.ExecuteNonQueryAsync();
                }

                await using var updDemoApps = new NpgsqlCommand("""
                    UPDATE sys_users
                    SET allowed_apps = allowed_apps || '["TASKS"]'::jsonb
                    WHERE LOWER(username) IN ('hr_manager', 'asset_lead', 'staff')
                      AND NOT (allowed_apps ? 'TASKS')
                      AND NOT (allowed_apps ? '*');
                    """, conn);
                await updDemoApps.ExecuteNonQueryAsync();
            }
        }

        // Seed standard Enterprise SSO providers (Google, Microsoft Entra ID, Custom OIDC SSO)
        await using (var ssoSeedCmd = new NpgsqlCommand("""
            INSERT INTO sys_sso_providers (
                id, code, provider_type, display_name, is_enabled, tenant_id, issuer_url,
                authorization_url, token_url, userinfo_url, scopes, allowed_domains,
                auto_provision, default_role_code, button_color, order_index
            )
            VALUES
            (
                'sso_google', 'GOOGLE', 'GOOGLE', 'Đăng nhập với Google', FALSE, NULL,
                'https://accounts.google.com',
                'https://accounts.google.com/o/oauth2/v2/auth',
                'https://oauth2.googleapis.com/token',
                'https://openidconnect.googleapis.com/v1/userinfo',
                'openid email profile', '[]'::jsonb, TRUE, 'STAFF', '#ea4335', 1
            ),
            (
                'sso_microsoft', 'MICROSOFT', 'MICROSOFT', 'Đăng nhập với Microsoft', FALSE, 'common',
                'https://login.microsoftonline.com/common/v2.0',
                'https://login.microsoftonline.com/common/oauth2/v2.0/authorize',
                'https://login.microsoftonline.com/common/oauth2/v2.0/token',
                'https://graph.microsoft.com/oidc/userinfo',
                'openid email profile User.Read', '[]'::jsonb, TRUE, 'STAFF', '#0078d4', 2
            ),
            (
                'sso_custom', 'CUSTOM_SSO', 'CUSTOM_OIDC', 'Đăng nhập SSO Riêng (OIDC / OAuth2)', FALSE, NULL,
                NULL, NULL, NULL, NULL,
                'openid email profile', '[]'::jsonb, TRUE, 'STAFF', '#00897b', 3
            )
            ON CONFLICT (id) DO NOTHING;
            """, conn))
        {
            await ssoSeedCmd.ExecuteNonQueryAsync();
        }

        // Seed standard External Notification Channels (Gmail SMTP, Telegram, Zalo)
        await using (var nchSeedCmd = new NpgsqlCommand("""
            INSERT INTO sys_notification_channels (id, owner_scope, owner_user_id, channel_type, name, is_enabled, event_categories, config)
            VALUES
            (
                'nch_gmail_sys', 'SYSTEM', NULL, 'GMAIL_SMTP', 'Gmail / Email SMTP Công ty', FALSE,
                '["ALL"]'::jsonb,
                '{"smtpHost":"smtp.gmail.com","smtpPort":587,"useSsl":true,"username":"","password":"","fromName":"SmartApps Enterprise","toEmails":"","useRecipientEmail":true}'::jsonb
            ),
            (
                'nch_telegram_sys', 'SYSTEM', NULL, 'TELEGRAM', 'Bot & Nhóm Telegram Công ty', FALSE,
                '["TASK","APPROVAL","AUTOMATION"]'::jsonb,
                '{"botToken":"","chatId":""}'::jsonb
            ),
            (
                'nch_zalo_sys', 'SYSTEM', NULL, 'ZALO', 'Nhóm Zalo / Zalo OA Doanh nghiệp', FALSE,
                '["TASK","APPROVAL","AUTOMATION"]'::jsonb,
                '{"zaloMode":"WEBHOOK","webhookUrl":"","secretHeader":"","endpointUrl":"https://openapi.zalo.me/v3.0/oa/message/cs","accessToken":"","targetId":""}'::jsonb
            )
            ON CONFLICT (id) DO NOTHING;
            """, conn))
        {
            await nchSeedCmd.ExecuteNonQueryAsync();
        }

        // Seed default enterprise users if sys_users is empty
        await using var userCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_users", conn);
        var userCount = Convert.ToInt32(await userCountCmd.ExecuteScalarAsync());
        if (userCount == 0)
        {
            var users = new (string Id, string User, string Name, string Email, string? EmpCode, string Dept, string Role, string AppsJson, string PermsJson)[]
            {
                ("usr_admin", "admin", "Quản trị Hệ thống (IT Admin)", "admin@company.vn", null, "Khối Công nghệ & Kỹ thuật", "ADMIN",
                    "[\"*\"]", "[\"studio.manage\",\"record.view\",\"record.create\",\"record.edit\",\"record.delete\",\"record.export\",\"automation.manage\",\"user.manage\",\"role.manage\",\"audit.view\",\"integration.manage\"]"),
                ("usr_hr", "hr_manager", "Trần Thị Minh (Trưởng phòng HR)", "minh.tran@company.vn", "NV002", "Phòng Nhân sự (HR)", "HR_MANAGER",
                    "[\"HRM\",\"OPS\",\"TASKS\"]", "[\"record.view\",\"record.create\",\"record.edit\",\"record.delete\",\"record.export\"]"),
                ("usr_asset", "asset_lead", "Nguyễn Văn An (Quản lý Tài sản & IT)", "an.nguyen@company.vn", "NV001", "Khối Công nghệ & Kỹ thuật", "ASSET_LEAD",
                    "[\"ASSET\",\"OPS\",\"TASKS\"]", "[\"record.view\",\"record.create\",\"record.edit\",\"record.delete\",\"record.export\"]"),
                ("usr_staff", "staff", "Lê Hoàng Nam (Nhân viên)", "nam.le@company.vn", "NV003", "Phòng Tài chính - Kế toán", "STAFF",
                    "[\"OPS\",\"HRM\",\"TASKS\"]", "[\"record.view\",\"record.create\"]")
            };
            var defaultHash = AuthService.HashPassword("123456");
            foreach (var u in users)
            {
                await using var uCmd = new NpgsqlCommand("""
                    INSERT INTO sys_users (id, username, full_name, email, employee_code, department, role_code, role_codes, password_hash, allowed_apps, permissions)
                    VALUES (@id, @u, @n, @em, @ec, @dept, @rc, jsonb_build_array(@rc), @ph, @apps::jsonb, @perms::jsonb)
                    ON CONFLICT DO NOTHING
                    """, conn);
                uCmd.Parameters.AddWithValue("id", u.Id);
                uCmd.Parameters.AddWithValue("u", u.User);
                uCmd.Parameters.AddWithValue("n", u.Name);
                uCmd.Parameters.AddWithValue("em", u.Email);
                uCmd.Parameters.AddWithValue("ec", (object?)u.EmpCode ?? DBNull.Value);
                uCmd.Parameters.AddWithValue("dept", u.Dept);
                uCmd.Parameters.AddWithValue("rc", u.Role);
                uCmd.Parameters.AddWithValue("ph", defaultHash);
                uCmd.Parameters.AddWithValue("apps", u.AppsJson);
                uCmd.Parameters.AddWithValue("perms", u.PermsJson);
                await uCmd.ExecuteNonQueryAsync();
            }
        }
        else
        {
            // Backfill role_codes & employee_code for existing seeded users
            await using var bfCmd = new NpgsqlCommand("""
                UPDATE sys_users SET role_codes = jsonb_build_array(role_code) WHERE role_codes = '[]'::jsonb OR role_codes IS NULL;
                UPDATE sys_users SET employee_code = 'NV001' WHERE LOWER(username) = 'asset_lead' AND employee_code IS NULL;
                UPDATE sys_users SET employee_code = 'NV002' WHERE LOWER(username) = 'hr_manager' AND employee_code IS NULL;
                UPDATE sys_users SET employee_code = 'NV003' WHERE LOWER(username) = 'staff' AND employee_code IS NULL;
                """, conn);
            await bfCmd.ExecuteNonQueryAsync();
        }

        await using (var autoUserChk = new NpgsqlCommand("SELECT 1 FROM app_automations WHERE action_type = 'CREATE_USER' LIMIT 1", conn))
        {
            if (await autoUserChk.ExecuteScalarAsync() == null)
            {
                await using var checkHrmTbl = new NpgsqlCommand("SELECT 1 FROM sys_tables WHERE id = 'tbl_hrm_nhanvien' LIMIT 1", conn);
                if (await checkHrmTbl.ExecuteScalarAsync() != null)
                {
                    var createUserMap = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["username"] = "{{EMAIL_PREFIX(email)}}",
                        ["fullName"] = "{{ho_ten}}",
                        ["email"] = "{{email}}",
                        ["employeeCode"] = "{{ma_nv}}",
                        ["department"] = "{{phong_ban}}",
                        ["roleCode"] = "STAFF",
                        ["password"] = "123456"
                    });
                    await using var insAutoUser = new NpgsqlCommand("""
                        INSERT INTO app_automations (
                            id, name, description, source_app_id, source_table_id, trigger_event,
                            action_type, field_mappings, is_active
                        )
                        VALUES (
                            'auto_hrm_create_user',
                            'HRM: Thêm Nhân viên mới → Tự tạo Tài khoản User',
                            'Khi thêm mới nhân sự trong App HRM, tự động tạo tài khoản đăng nhập hệ thống (sys_users) gắn sẵn Mã NV và vai trò STAFF.',
                            'app_hrm', 'tbl_hrm_nhanvien', 'RECORD_CREATED',
                            'CREATE_USER', @map::jsonb, TRUE
                        )
                        ON CONFLICT (id) DO NOTHING
                        """, conn);
                    insAutoUser.Parameters.AddWithValue("map", createUserMap);
                    await insAutoUser.ExecuteNonQueryAsync();
                }
            }
        }

        // Seed default Customizable App Dashboard widgets if empty
        await using var wCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM app_widgets", conn);
        var wCount = Convert.ToInt32(await wCountCmd.ExecuteScalarAsync());
        if (wCount == 0)
        {
            var widgets = new (string Id, string AppId, string TblId, string Title, string Type, string? Metric, string? Group, string? FField, string? FVal, int Span, string Color, int Ord)[]
            {
                // HRM Dashboard
                ("wdg_hrm_1", "app_hrm", "tbl_hrm_nhanvien", "Nhân sự đang làm việc", "KPI_COUNT", null, null, "dang_lam_viec", "true", 1, "#1a73e8", 1),
                ("wdg_hrm_2", "app_hrm", "tbl_hrm_nhanvien", "Tổng Quỹ lương Cơ bản", "KPI_SUM", "luong_co_ban", null, null, null, 1, "#1e8e3e", 2),
                ("wdg_hrm_3", "app_hrm", "tbl_hrm_nghiphep", "Đơn nghỉ phép chờ duyệt", "KPI_COUNT", null, null, "trang_thai_duyet", "CHO_DUYET", 1, "#ea8600", 3),
                ("wdg_hrm_4", "app_hrm", "tbl_hrm_nhanvien", "Mức lương trung bình", "KPI_AVG", "luong_co_ban", null, null, null, 1, "#7b1fa2", 4),
                ("wdg_hrm_5", "app_hrm", "tbl_hrm_nhanvien", "Cơ cấu Nhân sự theo Phòng ban", "DONUT_CHART", null, "phong_ban", null, null, 2, "#1a73e8", 5),
                ("wdg_hrm_6", "app_hrm", "tbl_hrm_nhanvien", "Phân bổ theo Chức vụ", "BAR_CHART", null, "chuc_vu", null, null, 2, "#0284c7", 6),

                // ASSET Dashboard
                ("wdg_as_1", "app_asset", "tbl_asset_danhmuc", "Tổng Giá trị Tài sản", "KPI_SUM", "thanh_tien", null, null, null, 1, "#00897b", 1),
                ("wdg_as_2", "app_asset", "tbl_asset_danhmuc", "Tổng Số lượng Thiết bị", "KPI_SUM", "so_luong", null, null, null, 1, "#1a73e8", 2),
                ("wdg_as_3", "app_asset", "tbl_asset_danhmuc", "Thiết bị đang sử dụng", "KPI_COUNT", null, null, "tinh_trang", "DANG_DUNG", 1, "#1e8e3e", 3),
                ("wdg_as_4", "app_asset", "tbl_asset_capphat", "Phiếu Cấp phát đã lập", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 4),
                ("wdg_as_5", "app_asset", "tbl_asset_danhmuc", "Tài sản theo Nhóm thiết bị", "DONUT_CHART", null, "nhom_ts", null, null, 2, "#00897b", 5),
                ("wdg_as_6", "app_asset", "tbl_asset_danhmuc", "Danh mục Tài sản gần nhất", "RECENT_LIST", null, null, null, null, 2, "#1a73e8", 6),

                // OPS Dashboard
                ("wdg_op_1", "app_ops", "tbl_ops_yeucau", "Tổng Yêu cầu Nội bộ", "KPI_COUNT", null, null, null, null, 1, "#7b1fa2", 1),
                ("wdg_op_2", "app_ops", "tbl_ops_yeucau", "Ticket Chưa hoàn thành", "KPI_COUNT", null, null, "da_hoan_thanh", "false", 1, "#d93025", 2),
                ("wdg_op_3", "app_ops", "tbl_ops_yeucau", "Ticket Ưu tiên Cao", "KPI_COUNT", null, null, "muc_do_uu_tien", "HIGH", 1, "#ea8600", 3),
                ("wdg_op_4", "app_ops", "tbl_ops_yeucau", "Đã xử lý xong", "KPI_COUNT", null, null, "da_hoan_thanh", "true", 1, "#1e8e3e", 4),
                ("wdg_op_5", "app_ops", "tbl_ops_yeucau", "Yêu cầu theo Phân loại", "BAR_CHART", null, "loai_yeu_cau", null, null, 2, "#7b1fa2", 5),
                ("wdg_op_6", "app_ops", "tbl_ops_yeucau", "Phân bổ Mức độ Ưu tiên", "DONUT_CHART", null, "muc_do_uu_tien", null, null, 2, "#ea8600", 6)
            };

            foreach (var w in widgets)
            {
                await using var wCmd = new NpgsqlCommand("""
                    INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field, filter_field, filter_value, col_span, color, order_index)
                    VALUES (@id, @app, @tbl, @title, @wt, @mf, @gf, @ff, @fv, @span, @col, @ord)
                    ON CONFLICT DO NOTHING
                    """, conn);
                wCmd.Parameters.AddWithValue("id", w.Id);
                wCmd.Parameters.AddWithValue("app", w.AppId);
                wCmd.Parameters.AddWithValue("tbl", w.TblId);
                wCmd.Parameters.AddWithValue("title", w.Title);
                wCmd.Parameters.AddWithValue("wt", w.Type);
                wCmd.Parameters.AddWithValue("mf", (object?)w.Metric ?? DBNull.Value);
                wCmd.Parameters.AddWithValue("gf", (object?)w.Group ?? DBNull.Value);
                wCmd.Parameters.AddWithValue("ff", (object?)w.FField ?? DBNull.Value);
                wCmd.Parameters.AddWithValue("fv", (object?)w.FVal ?? DBNull.Value);
                wCmd.Parameters.AddWithValue("span", w.Span);
                wCmd.Parameters.AddWithValue("col", w.Color);
                wCmd.Parameters.AddWithValue("ord", w.Ord);
                await wCmd.ExecuteNonQueryAsync();
            }
        }

        // Field-Level Security default: hide base salary from STAFF role
        await using (var flsCmd = new NpgsqlCommand(
            "UPDATE sys_columns SET hidden_for_roles = '[\"STAFF\"]'::jsonb WHERE id = 'col_nv_luong' AND hidden_for_roles = '[]'::jsonb", conn))
        {
            await flsCmd.ExecuteNonQueryAsync();
        }

        // Seed welcome notification if empty
        await using var nCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM sys_notifications", conn);
        var nCount = Convert.ToInt32(await nCountCmd.ExecuteScalarAsync());
        if (nCount == 0)
        {
            await using var nCmd = new NpgsqlCommand("""
                INSERT INTO sys_notifications (id, recipient_username, title, message, app_code, category, link_url, sender_username)
                VALUES ('noti_welcome', '*', 'Chào mừng tới SmartApps Enterprise', 'Hệ thống RBAC, Chat nội bộ, Giao việc trực tuyến, Bảng động & Tự động hóa đã sẵn sàng.', 'HRM', 'SYSTEM', '/chat', 'admin')
                ON CONFLICT DO NOTHING
                """, conn);
            await nCmd.ExecuteNonQueryAsync();
        }

        // Seed default system settings
        var defaultSysCfg = JsonSerializer.Serialize(new
        {
            companyName = "SmartApps Enterprise",
            workspaceTitle = "SmartApps Enterprise — Không gian làm việc Đa ứng dụng",
            announcementBanner = "",
            allowChatFileUpload = true,
            allowTaskFromChat = true,
            defaultTaskAppCode = "OPS",
            sessionTimeoutDays = 7
        });
        await using (var cfgCmd = new NpgsqlCommand("""
            INSERT INTO sys_system_settings (setting_key, setting_value, description, updated_by)
            VALUES ('workspace_config', @val::jsonb, 'Cấu hình chung toàn hệ thống SmartApps', 'admin')
            ON CONFLICT (setting_key) DO NOTHING
            """, conn))
        {
            cfgCmd.Parameters.AddWithValue("val", defaultSysCfg);
            await cfgCmd.ExecuteNonQueryAsync();
        }

        // Seed standard AI API providers & shared AI system policy
        await using (var aiSeedCmd = new NpgsqlCommand("""
            INSERT INTO sys_ai_providers (
                id, code, provider_type, display_name, is_enabled, is_default,
                base_url, model, timeout_seconds, order_index, updated_by
            )
            VALUES
            (
                'ai_gemini', 'GEMINI', 'GEMINI', 'Google Gemini', FALSE, TRUE,
                'https://generativelanguage.googleapis.com/v1beta', 'gemini-2.5-flash', 45, 1, 'admin'
            ),
            (
                'ai_openai', 'OPENAI', 'OPENAI', 'OpenAI (GPT)', FALSE, FALSE,
                'https://api.openai.com/v1', 'gpt-4o-mini', 45, 2, 'admin'
            ),
            (
                'ai_anthropic', 'ANTHROPIC', 'ANTHROPIC', 'Anthropic Claude', FALSE, FALSE,
                'https://api.anthropic.com/v1', 'claude-3-5-haiku-latest', 45, 3, 'admin'
            ),
            (
                'ai_custom', 'LOCAL_LLM', 'OPENAI_COMPATIBLE', 'OpenAI-Compatible / Local LLM (Ollama, vLLM, OpenRouter)', FALSE, FALSE,
                'http://localhost:11434/v1', 'qwen2.5:7b', 60, 4, 'admin'
            )
            ON CONFLICT (id) DO NOTHING
            """, conn))
        {
            await aiSeedCmd.ExecuteNonQueryAsync();
        }

        var defaultAiCfg = JsonSerializer.Serialize(new
        {
            isEnabled = true,
            defaultProviderId = "ai_gemini",
            fallbackToRuleEngine = true,
            temperature = 0.2,
            maxTokens = 2048,
            timeoutSeconds = 45,
            systemPromptPrefix = "Bạn là trợ lý AI doanh nghiệp của nền tảng SmartApps. Trả lời chính xác, súc tích, ưu tiên tiếng Việt chuẩn.",
            enabledFeatures = new[]
            {
                "AI_APP_COPILOT",
                "AI_RECORD_ASSISTANT",
                "AI_CHAT_COPILOT",
                "AI_WORKFLOW_TRIAGE",
                "AI_DASHBOARD_INSIGHTS",
                "AI_IMPORT_MAPPER",
                "AI_DOC_OCR"
            }
        });
        await using (var aiCfgCmd = new NpgsqlCommand("""
            INSERT INTO sys_system_settings (setting_key, setting_value, description, updated_by)
            VALUES ('ai_config', @val::jsonb, 'Cấu hình AI API dùng chung toàn hệ thống SmartApps', 'admin')
            ON CONFLICT (setting_key) DO NOTHING
            """, conn))
        {
            aiCfgCmd.Parameters.AddWithValue("val", defaultAiCfg);
            await aiCfgCmd.ExecuteNonQueryAsync();
        }

        // Seed default internal chat channels & integrated tasks
        await using var chCountCmd = new NpgsqlCommand("SELECT COUNT(*) FROM chat_channels", conn);
        var chCount = Convert.ToInt32(await chCountCmd.ExecuteScalarAsync());
        if (chCount == 0)
        {
            await using var seedChatCmd = new NpgsqlCommand("""
                INSERT INTO chat_channels (id, name, description, channel_type, avatar_color, linked_app_code, created_by, last_message_preview)
                VALUES
                ('ch_general', 'Toàn công ty', 'Kênh thông báo chung, trao đổi và phối hợp liên phòng ban.', 'PUBLIC', '#1a73e8', NULL, 'admin', '📌 [TASK-001] Rà soát danh sách cấp phát thiết bị Q4'),
                ('ch_it_ops', 'Kỹ thuật & Vận hành (IT/OPS)', 'Thảo luận ticket hỗ trợ CNTT, hạ tầng và giao việc xử lý sự cố.', 'GROUP', '#7b1fa2', 'OPS', 'admin', '📌 [TASK-002] Cấp quyền VPN & PostgreSQL Staging'),
                ('ch_hr_admin', 'Nhân sự & Hành chính', 'Phối hợp tuyển dụng, onboarding nhân sự mới và phê duyệt đơn từ.', 'GROUP', '#1e8e3e', 'HRM', 'hr_manager', 'Chào mừng các thành viên phòng Nhân sự & Hành chính!')
                ON CONFLICT DO NOTHING;

                INSERT INTO chat_channel_members (channel_id, username, role)
                VALUES
                ('ch_general', 'admin', 'OWNER'),
                ('ch_general', 'hr_manager', 'MEMBER'),
                ('ch_general', 'asset_lead', 'MEMBER'),
                ('ch_general', 'staff', 'MEMBER'),
                ('ch_it_ops', 'admin', 'OWNER'),
                ('ch_it_ops', 'asset_lead', 'MEMBER'),
                ('ch_it_ops', 'staff', 'MEMBER'),
                ('ch_hr_admin', 'hr_manager', 'OWNER'),
                ('ch_hr_admin', 'admin', 'MEMBER'),
                ('ch_hr_admin', 'staff', 'MEMBER')
                ON CONFLICT DO NOTHING;

                INSERT INTO app_tasks (id, task_code, title, description, status, priority, assignee_username, assigner_username, due_date, progress, checklist, channel_id, message_id, linked_app_code)
                VALUES
                ('tsk_seed_1', 'TASK-001', 'Rà soát danh sách cấp phát thiết bị Q4',
                 'Kiểm tra đối chiếu phiếu bàn giao MacBook & thiết bị văn phòng cho nhân sự mới.',
                 'IN_PROGRESS', 'HIGH', 'asset_lead', 'admin', CURRENT_DATE + INTERVAL '3 days', 50,
                 '[{"id":"c1","text":"Đối chiếu kho thiết bị ASSET","done":true},{"id":"c2","text":"Xác nhận chữ ký bàn giao nhân viên","done":false}]'::jsonb,
                 'ch_general', 'msg_seed_task1', 'ASSET'),
                ('tsk_seed_2', 'TASK-002', 'Cấp quyền VPN & PostgreSQL Staging',
                 'Mở quyền truy cập staging cho đội kiểm thử và cập nhật tài liệu hướng dẫn.',
                 'TODO', 'URGENT', 'staff', 'admin', CURRENT_DATE + INTERVAL '1 days', 0,
                 '[{"id":"c3","text":"Tạo tài khoản VPN nội bộ","done":false},{"id":"c4","text":"Gửi thông tin kết nối cho @staff","done":false}]'::jsonb,
                 'ch_it_ops', 'msg_seed_task2', 'OPS')
                ON CONFLICT DO NOTHING;

                INSERT INTO chat_messages (id, channel_id, sender_username, message_type, content, task_id, mentions, reactions, created_at)
                VALUES
                ('msg_seed_1', 'ch_general', 'admin', 'TEXT', 'Chào mọi người! Từ hôm nay chúng ta có thể trao đổi trực tiếp và giao việc ngay trong khung chat nội bộ này nhé. Dùng nút "Giao việc" hoặc gõ /task @username Nội dung.', NULL, '[]'::jsonb, '{"👍":["hr_manager","asset_lead"]}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '2 hours'),
                ('msg_seed_task1', 'ch_general', 'admin', 'TASK_CARD', 'Giao việc mới cho @asset_lead: Rà soát danh sách cấp phát thiết bị Q4', 'tsk_seed_1', '["asset_lead"]'::jsonb, '{"🔥":["asset_lead"]}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '90 minutes'),
                ('msg_seed_2', 'ch_general', 'asset_lead', 'TEXT', 'Đã nhận @admin! Mình đang đối chiếu kho ASSET và sẽ cập nhật tiến độ trực tiếp trên thẻ công việc.', NULL, '["admin"]'::jsonb, '{}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '75 minutes'),
                ('msg_seed_3', 'ch_it_ops', 'admin', 'TEXT', 'Nhóm IT/OPS theo dõi các ticket ưu tiên cao tại đây.', NULL, '[]'::jsonb, '{}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '1 hour'),
                ('msg_seed_task2', 'ch_it_ops', 'admin', 'TASK_CARD', 'Giao việc khẩn cho @staff: Cấp quyền VPN & PostgreSQL Staging', 'tsk_seed_2', '["staff"]'::jsonb, '{}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '40 minutes'),
                ('msg_seed_4', 'ch_hr_admin', 'hr_manager', 'TEXT', 'Chào mừng các thành viên phòng Nhân sự & Hành chính! Mọi đề xuất duyệt đơn nghỉ phép có thể trao đổi tại đây.', NULL, '[]'::jsonb, '{"❤️":["admin"]}'::jsonb, CURRENT_TIMESTAMP - INTERVAL '50 minutes')
                ON CONFLICT DO NOTHING;
                """, conn);
            await seedChatCmd.ExecuteNonQueryAsync();
        }

        // Seed so_cccd on tbl_hrm_nhanvien and linked HRM child tables (Phụ cấp, BHXH/BHYT, Khen thưởng, Kỷ luật)
        await using (var hrmCheck = new NpgsqlCommand("SELECT 1 FROM sys_tables WHERE id = 'tbl_hrm_nhanvien' LIMIT 1", conn))
        {
            if (await hrmCheck.ExecuteScalarAsync() != null)
            {
                await using var hrmChildCmd = new NpgsqlCommand("""
                    INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, order_index)
                    VALUES ('col_nv_cccd', 'tbl_hrm_nhanvien', 'so_cccd', 'Số CCCD / CMND', 'Số CCCD', 'STRING', FALSE, FALSE, 150, 4)
                    ON CONFLICT (table_id, column_code) DO NOTHING;

                    UPDATE app_records SET data = data || '{"so_cccd":"079092001234"}'::jsonb
                    WHERE id = 'rec_nv_1' AND NOT (data ? 'so_cccd');
                    UPDATE app_records SET data = data || '{"so_cccd":"079095005678"}'::jsonb
                    WHERE id = 'rec_nv_2' AND NOT (data ? 'so_cccd');
                    UPDATE app_records SET data = data || '{"so_cccd":"001098009012"}'::jsonb
                    WHERE id = 'rec_nv_3' AND NOT (data ? 'so_cccd');

                    INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, description)
                    VALUES ('mod_hrm_chedo', 'app_hrm', 'CHEDO', 'Chế độ, Bảo hiểm & Thi đua', 'shield', 3, 'Quản lý phụ cấp, BHXH/BHYT, khen thưởng và kỷ luật nhân sự')
                    ON CONFLICT (id) DO NOTHING;

                    INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
                    VALUES
                    ('tbl_hrm_phucap', 'app_hrm', 'mod_hrm_chedo', 'hrm_phu_cap', 'Phụ cấp Nhân sự', 'Theo dõi các khoản phụ cấp ăn trưa, xăng xe, trách nhiệm theo nhân sự', 'table', 'OWNER_ONLY', FALSE, 1),
                    ('tbl_hrm_baohiem', 'app_hrm', 'mod_hrm_chedo', 'hrm_bao_hiem', 'Bảo hiểm Xã hội & BHYT', 'Thông tin sổ BHXH, thẻ BHYT và nơi đăng ký khám chữa bệnh ban đầu', 'table', 'OWNER_ONLY', FALSE, 2),
                    ('tbl_hrm_khenthuong', 'app_hrm', 'mod_hrm_chedo', 'hrm_khen_thuong', 'Khen thưởng & Thành tích', 'Ghi nhận quyết định khen thưởng, thưởng dự án và danh hiệu thi đua', 'table', 'OWNER_ONLY', FALSE, 3),
                    ('tbl_hrm_kyluat', 'app_hrm', 'mod_hrm_chedo', 'hrm_ky_luat', 'Kỷ luật & Vi phạm', 'Theo dõi biên bản nhắc nhở, kỷ luật và xử lý vi phạm nội quy', 'table', 'OWNER_ONLY', FALSE, 4)
                    ON CONFLICT (id) DO NOTHING;

                    INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, default_value, order_index)
                    VALUES
                    ('col_pc_ma', 'tbl_hrm_phucap', 'ma_phieu', 'Mã phụ cấp', 'Mã phụ cấp', 'STRING', TRUE, TRUE, 130, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                    ('col_pc_nv', 'tbl_hrm_phucap', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 2),
                    ('col_pc_loai', 'tbl_hrm_phucap', 'loai_phu_cap', 'Loại phụ cấp', 'Loại phụ cấp', 'STRING', TRUE, FALSE, 210, NULL, NULL, NULL, NULL, NULL, NULL, 3),
                    ('col_pc_tien', 'tbl_hrm_phucap', 'so_tien', 'Số tiền / tháng (VNĐ)', 'Số tiền', 'CURRENCY', TRUE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, '0', 4),
                    ('col_pc_ngay', 'tbl_hrm_phucap', 'tu_ngay', 'Áp dụng từ ngày', 'Từ ngày', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                    ('col_pc_act', 'tbl_hrm_phucap', 'dang_huong', 'Đang hiệu lực', 'Hiệu lực', 'BOOLEAN', FALSE, FALSE, 120, NULL, NULL, NULL, NULL, NULL, 'true', 6),

                    ('col_bh_so', 'tbl_hrm_baohiem', 'so_so_bhxh', 'Số sổ BHXH', 'Số sổ BHXH', 'STRING', TRUE, TRUE, 145, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                    ('col_bh_nv', 'tbl_hrm_baohiem', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 2),
                    ('col_bh_bhyt', 'tbl_hrm_baohiem', 'ma_the_bhyt', 'Số thẻ BHYT', 'Số thẻ BHYT', 'STRING', TRUE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, NULL, 3),
                    ('col_bh_bv', 'tbl_hrm_baohiem', 'noi_kham_benh', 'Nơi ĐK KCB ban đầu', 'Nơi ĐK KCB', 'STRING', FALSE, FALSE, 230, NULL, NULL, NULL, NULL, NULL, NULL, 4),
                    ('col_bh_muc', 'tbl_hrm_baohiem', 'muc_dong', 'Mức lương đóng BH (VNĐ)', 'Mức đóng BH', 'CURRENCY', FALSE, FALSE, 175, NULL, NULL, NULL, NULL, NULL, '0', 5),
                    ('col_bh_ngay', 'tbl_hrm_baohiem', 'ngay_tham_gia', 'Ngày tham gia', 'Ngày tham gia', 'DATE', FALSE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, 6),
                    ('col_bh_act', 'tbl_hrm_baohiem', 'dang_tham_gia', 'Đang tham gia', 'Đang tham gia', 'BOOLEAN', FALSE, FALSE, 125, NULL, NULL, NULL, NULL, NULL, 'true', 7),

                    ('col_kt_so', 'tbl_hrm_khenthuong', 'so_quyet_dinh', 'Số quyết định', 'Số QĐ', 'STRING', TRUE, TRUE, 140, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                    ('col_kt_nv', 'tbl_hrm_khenthuong', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 2),
                    ('col_kt_ht', 'tbl_hrm_khenthuong', 'hinh_thuc', 'Hình thức / Danh hiệu', 'Hình thức', 'STRING', TRUE, FALSE, 220, NULL, NULL, NULL, NULL, NULL, NULL, 3),
                    ('col_kt_tien', 'tbl_hrm_khenthuong', 'so_tien_thuong', 'Tiền thưởng (VNĐ)', 'Tiền thưởng', 'CURRENCY', FALSE, FALSE, 160, NULL, NULL, NULL, NULL, NULL, '0', 4),
                    ('col_kt_ngay', 'tbl_hrm_khenthuong', 'ngay_quyet_dinh', 'Ngày quyết định', 'Ngày QĐ', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                    ('col_kt_ld', 'tbl_hrm_khenthuong', 'ly_do', 'Thành tích / Lý do', 'Lý do', 'TEXTAREA', FALSE, FALSE, 260, NULL, NULL, NULL, NULL, NULL, NULL, 6),

                    ('col_kl_so', 'tbl_hrm_kyluat', 'so_quyet_dinh', 'Số biên bản / QĐ', 'Số QĐ', 'STRING', TRUE, TRUE, 145, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                    ('col_kl_nv', 'tbl_hrm_kyluat', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 2),
                    ('col_kl_ht', 'tbl_hrm_kyluat', 'hinh_thuc', 'Hình thức xử lý', 'Hình thức', 'STRING', TRUE, FALSE, 200, NULL, NULL, NULL, NULL, NULL, NULL, 3),
                    ('col_kl_phat', 'tbl_hrm_kyluat', 'muc_phat', 'Khấu trừ / Phạt (VNĐ)', 'Mức phạt', 'CURRENCY', FALSE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, '0', 4),
                    ('col_kl_ngay', 'tbl_hrm_kyluat', 'ngay_quyet_dinh', 'Ngày lập', 'Ngày lập', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                    ('col_kl_nd', 'tbl_hrm_kyluat', 'noi_dung', 'Nội dung chi tiết', 'Nội dung', 'TEXTAREA', FALSE, FALSE, 260, NULL, NULL, NULL, NULL, NULL, NULL, 6)
                    ON CONFLICT (table_id, column_code) DO NOTHING;

                    INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status, created_by)
                    VALUES
                    ('rec_pc_1', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_phucap', 'PC-001', '{"ma_phieu":"PC-001","nhan_vien":"NV001","loai_phu_cap":"Phụ cấp Trách nhiệm Trưởng nhóm & Ăn trưa","so_tien":2500000,"tu_ngay":"2026-01-01","dang_huong":true}'::jsonb, 'ACTIVE', 'hr_manager'),
                    ('rec_pc_2', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_phucap', 'PC-002', '{"ma_phieu":"PC-002","nhan_vien":"NV002","loai_phu_cap":"Phụ cấp Quản lý & Tiếp khách","so_tien":3000000,"tu_ngay":"2026-01-01","dang_huong":true}'::jsonb, 'ACTIVE', 'hr_manager'),
                    ('rec_bh_1', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_baohiem', 'BH-7912345601', '{"so_so_bhxh":"BH-7912345601","nhan_vien":"NV001","ma_the_bhyt":"DN4797912345601","noi_kham_benh":"BV Đại học Y Dược TP.HCM","muc_dong":32000000,"ngay_tham_gia":"2023-04-01","dang_tham_gia":true}'::jsonb, 'ACTIVE', 'hr_manager'),
                    ('rec_bh_2', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_baohiem', 'BH-7912345602', '{"so_so_bhxh":"BH-7912345602","nhan_vien":"NV002","ma_the_bhyt":"DN4797912345602","noi_kham_benh":"BV Chợ Rẫy","muc_dong":28000000,"ngay_tham_gia":"2022-09-01","dang_tham_gia":true}'::jsonb, 'ACTIVE', 'hr_manager'),
                    ('rec_kt_1', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_khenthuong', 'QD-KT-2026-01', '{"so_quyet_dinh":"QD-KT-2026-01","nhan_vien":"NV001","hinh_thuc":"Nhân viên Xuất sắc Quý III/2026","so_tien_thuong":5000000,"ngay_quyet_dinh":"2026-10-05","ly_do":"Hoàn thành vượt tiến độ triển khai hệ thống SmartApps Core."}'::jsonb, 'ACTIVE', 'hr_manager'),
                    ('rec_kl_1', 'app_hrm', 'mod_hrm_chedo', 'tbl_hrm_kyluat', 'QD-KL-2026-01', '{"so_quyet_dinh":"QD-KL-2026-01","nhan_vien":"NV003","hinh_thuc":"Nhắc nhở bằng văn bản","muc_phat":0,"ngay_quyet_dinh":"2026-09-12","noi_dung":"Đi muộn quá 3 lần trong tháng 09/2026 không báo trước."}'::jsonb, 'ACTIVE', 'hr_manager')
                    ON CONFLICT (id) DO NOTHING;

                    INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
                    SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_hrm', t.id, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, 'OWNER_ONLY'
                    FROM sys_roles r
                    CROSS JOIN (VALUES ('tbl_hrm_phucap'), ('tbl_hrm_baohiem'), ('tbl_hrm_khenthuong'), ('tbl_hrm_kyluat')) AS t(id)
                    WHERE UPPER(r.code) = 'STAFF'
                    ON CONFLICT (role_id, table_id) DO NOTHING;
                    """, conn);
                await hrmChildCmd.ExecuteNonQueryAsync();
            }
        }

        await UpgradeHrmSchemaAndSeedAsync(conn);
        await UpgradeOtherAppsSchemaAndSeedAsync(conn);
    }

    private static async Task UpgradeHrmSchemaAndSeedAsync(NpgsqlConnection conn)
    {
        await using var checkApp = new NpgsqlCommand("SELECT COUNT(*) FROM sys_apps WHERE id = 'app_hrm'", conn);
        if (Convert.ToInt64(await checkApp.ExecuteScalarAsync()) == 0) return;

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_categories (category_code, category_name, app_id, description)
            VALUES
            ('DM_TRANG_THAI_NS', 'Trạng thái nhân sự', 'app_hrm', 'Trạng thái hợp đồng và làm việc của nhân viên'),
            ('DM_GIOI_TINH', 'Giới tính', 'app_hrm', 'Danh mục giới tính nhân sự'),
            ('DM_LOAI_HD', 'Loại hợp đồng lao động', 'app_hrm', 'Phân loại hợp đồng lao động'),
            ('DM_TRANG_THAI_HD', 'Trạng thái hợp đồng', 'app_hrm', 'Tình trạng hiệu lực hợp đồng'),
            ('DM_LOAI_PHU_CAP', 'Loại phụ cấp', 'app_hrm', 'Danh mục các khoản phụ cấp nhân sự'),
            ('DM_QUAN_HE_NPT', 'Quan hệ người phụ thuộc', 'app_hrm', 'Quan hệ gia đình giảm trừ gia cảnh'),
            ('DM_LOAI_TANG_CA', 'Loại tăng ca / giải trình', 'app_hrm', 'Phân loại tăng ca OT và giải trình công')
            ON CONFLICT (category_code) DO NOTHING;

            INSERT INTO sys_category_items (id, category_code, item_code, item_name, color, order_index)
            VALUES
            ('ci_dm_ttns_1', 'DM_TRANG_THAI_NS', 'THU_VIEC', 'Thử việc', '#f59e0b', 1),
            ('ci_dm_ttns_2', 'DM_TRANG_THAI_NS', 'CHINH_THUC', 'Chính thức', '#10b981', 2),
            ('ci_dm_ttns_3', 'DM_TRANG_THAI_NS', 'TAM_HOAN', 'Tạm hoãn / Nghỉ dài hạn', '#6366f1', 3),
            ('ci_dm_ttns_4', 'DM_TRANG_THAI_NS', 'DA_NGHI_VIEC', 'Đã nghỉ việc', '#ef4444', 4),
            ('ci_dm_gt_1', 'DM_GIOI_TINH', 'NAM', 'Nam', '#3b82f6', 1),
            ('ci_dm_gt_2', 'DM_GIOI_TINH', 'NU', 'Nữ', '#ec4899', 2),
            ('ci_dm_gt_3', 'DM_GIOI_TINH', 'KHAC', 'Khác', '#64748b', 3),
            ('ci_dm_lhd_1', 'DM_LOAI_HD', 'THU_VIEC', 'Hợp đồng thử việc (2 tháng)', '#f59e0b', 1),
            ('ci_dm_lhd_2', 'DM_LOAI_HD', 'CO_THOI_HAN_12', 'Hợp đồng 12 tháng', '#3b82f6', 2),
            ('ci_dm_lhd_3', 'DM_LOAI_HD', 'CO_THOI_HAN_24', 'Hợp đồng 24 tháng', '#6366f1', 3),
            ('ci_dm_lhd_4', 'DM_LOAI_HD', 'KHONG_THOI_HAN', 'Không xác định thời hạn', '#10b981', 4),
            ('ci_dm_tthd_1', 'DM_TRANG_THAI_HD', 'HIEU_LUC', 'Đang hiệu lực', '#10b981', 1),
            ('ci_dm_tthd_2', 'DM_TRANG_THAI_HD', 'HET_HAN', 'Hết hạn', '#f59e0b', 2),
            ('ci_dm_tthd_3', 'DM_TRANG_THAI_HD', 'THANH_LY', 'Đã thanh lý', '#64748b', 3),
            ('ci_dm_lpc_1', 'DM_LOAI_PHU_CAP', 'AN_TRUA', 'Phụ cấp Ăn trưa', '#10b981', 1),
            ('ci_dm_lpc_2', 'DM_LOAI_PHU_CAP', 'XANG_XE', 'Phụ cấp Xăng xe / Đi lại', '#3b82f6', 2),
            ('ci_dm_lpc_3', 'DM_LOAI_PHU_CAP', 'DIEN_THOAI', 'Phụ cấp Điện thoại', '#6366f1', 3),
            ('ci_dm_lpc_4', 'DM_LOAI_PHU_CAP', 'TRACH_NHIEM', 'Phụ cấp Trách nhiệm', '#f59e0b', 4),
            ('ci_dm_lpc_5', 'DM_LOAI_PHU_CAP', 'DOC_HAI', 'Phụ cấp Độc hại', '#ef4444', 5),
            ('ci_dm_qhnpt_1', 'DM_QUAN_HE_NPT', 'CON', 'Con ruột / Con nuôi', '#3b82f6', 1),
            ('ci_dm_qhnpt_2', 'DM_QUAN_HE_NPT', 'VO_CHONG', 'Vợ / Chồng', '#ec4899', 2),
            ('ci_dm_qhnpt_3', 'DM_QUAN_HE_NPT', 'CHA_ME', 'Cha / Mẹ', '#10b981', 3),
            ('ci_dm_lot_1', 'DM_LOAI_TANG_CA', 'OT_THUONG', 'Tăng ca ngày thường (150%)', '#3b82f6', 1),
            ('ci_dm_lot_2', 'DM_LOAI_TANG_CA', 'OT_CUOI_TUAN', 'Tăng ca cuối tuần (200%)', '#f59e0b', 2),
            ('ci_dm_lot_3', 'DM_LOAI_TANG_CA', 'OT_LE', 'Tăng ca ngày lễ (300%)', '#ef4444', 3),
            ('ci_dm_lot_4', 'DM_LOAI_TANG_CA', 'GIAI_TRINH_CONG', 'Giải trình quên chấm công', '#6366f1', 4)
            ON CONFLICT (category_code, item_code) DO NOTHING;

            INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, description)
            VALUES ('mod_hrm_luong', 'app_hrm', 'LUONG', 'Tiền lương & Thuế', 'Payments', 4, 'Tổng hợp bảng lương tháng, khấu trừ BHXH và thuế TNCN')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, show_in_table, show_in_form, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, default_value, hidden_for_roles, order_index)
            VALUES
            ('col_nv_ttns', 'tbl_hrm_nhanvien', 'trang_thai_ns', 'Trạng thái nhân sự', 'Trạng thái NS', 'SELECT', TRUE, FALSE, TRUE, TRUE, 155, NULL, NULL, NULL, NULL, 'DM_TRANG_THAI_NS', 'CHINH_THUC', '[]'::jsonb, 10),
            ('col_nv_qlt', 'tbl_hrm_nhanvien', 'quan_ly_truc_tiep', 'Quản lý trực tiếp', 'Quản lý trực tiếp', 'LOOKUP', FALSE, FALSE, FALSE, TRUE, 190, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, '[]'::jsonb, 11),
            ('col_nv_ns', 'tbl_hrm_nhanvien', 'ngay_sinh', 'Ngày sinh', 'Ngày sinh', 'DATE', FALSE, FALSE, FALSE, TRUE, 130, NULL, NULL, NULL, NULL, NULL, NULL, '[]'::jsonb, 12),
            ('col_nv_gt', 'tbl_hrm_nhanvien', 'gioi_tinh', 'Giới tính', 'Giới tính', 'SELECT', FALSE, FALSE, FALSE, TRUE, 110, NULL, NULL, NULL, NULL, 'DM_GIOI_TINH', NULL, '[]'::jsonb, 13),
            ('col_nv_dc', 'tbl_hrm_nhanvien', 'dia_chi', 'Địa chỉ thường trú', 'Địa chỉ', 'STRING', FALSE, FALSE, FALSE, TRUE, 240, NULL, NULL, NULL, NULL, NULL, NULL, '[]'::jsonb, 14),
            ('col_nv_mst', 'tbl_hrm_nhanvien', 'ma_so_thue', 'Mã số thuế cá nhân', 'MST cá nhân', 'STRING', FALSE, FALSE, FALSE, TRUE, 150, NULL, NULL, NULL, NULL, NULL, NULL, '["STAFF"]'::jsonb, 15),
            ('col_nv_stk', 'tbl_hrm_nhanvien', 'so_tai_khoan', 'Số tài khoản ngân hàng', 'Số TK ngân hàng', 'STRING', FALSE, FALSE, FALSE, TRUE, 175, NULL, NULL, NULL, NULL, NULL, NULL, '["STAFF"]'::jsonb, 16),
            ('col_nv_nh', 'tbl_hrm_nhanvien', 'ngan_hang', 'Ngân hàng nhận lương', 'Ngân hàng', 'STRING', FALSE, FALSE, FALSE, TRUE, 185, NULL, NULL, NULL, NULL, NULL, NULL, '["STAFF"]'::jsonb, 17),
            ('col_nv_nnv', 'tbl_hrm_nhanvien', 'ngay_nghi_viec', 'Ngày nghỉ việc', 'Ngày nghỉ việc', 'DATE', FALSE, FALSE, FALSE, TRUE, 135, NULL, NULL, NULL, NULL, NULL, NULL, '[]'::jsonb, 18)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
            VALUES
            ('tbl_hrm_hopdong', 'app_hrm', 'mod_hrm_hoso', 'hrm_hop_dong', 'Hợp đồng Lao động', 'Quản lý hợp đồng thử việc, có thời hạn, không thời hạn và mức lương theo hợp đồng', 'table', 'OWNER_ONLY', TRUE, 2),
            ('tbl_hrm_nguoiphuthuoc', 'app_hrm', 'mod_hrm_hoso', 'hrm_nguoi_phu_thuoc', 'Người phụ thuộc (Giảm trừ)', 'Danh sách người phụ thuộc kê khai giảm trừ gia cảnh thuế TNCN', 'table', 'OWNER_ONLY', FALSE, 3),
            ('tbl_hrm_tangca', 'app_hrm', 'mod_hrm_nghiphep', 'hrm_tang_ca', 'Tăng ca & Giải trình công', 'Đăng ký làm thêm giờ (OT) và giải trình chấm công', 'table', 'OWNER_ONLY', TRUE, 3),
            ('tbl_hrm_bangcong', 'app_hrm', 'mod_hrm_nghiphep', 'hrm_bang_cong', 'Tổng hợp Công tháng', 'Bảng tổng hợp ngày công chuẩn, công thực tế, nghỉ phép và giờ tăng ca theo tháng', 'table', 'OWNER_ONLY', FALSE, 4),
            ('tbl_hrm_bangluong', 'app_hrm', 'mod_hrm_luong', 'hrm_bang_luong', 'Bảng lương & Phiếu lương', 'Tính lương tháng, phụ cấp, tăng ca, khấu trừ BHXH, thuế TNCN và thực lĩnh', 'table', 'OWNER_ONLY', FALSE, 1)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, formula, default_value, show_in_parent_form, order_index)
            VALUES
            ('col_hd_so', 'tbl_hrm_hopdong', 'so_hop_dong', 'Số hợp đồng', 'Số HĐ', 'STRING', TRUE, TRUE, 145, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
            ('col_hd_nv', 'tbl_hrm_hopdong', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, TRUE, 2),
            ('col_hd_loai', 'tbl_hrm_hopdong', 'loai_hop_dong', 'Loại hợp đồng', 'Loại HĐ', 'SELECT', TRUE, FALSE, 210, NULL, NULL, NULL, NULL, 'DM_LOAI_HD', NULL, 'CO_THOI_HAN_12', FALSE, 3),
            ('col_hd_ky', 'tbl_hrm_hopdong', 'ngay_ky', 'Ngày ký', 'Ngày ký', 'DATE', TRUE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 4),
            ('col_hd_tu', 'tbl_hrm_hopdong', 'tu_ngay', 'Hiệu lực từ ngày', 'Từ ngày', 'DATE', TRUE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 5),
            ('col_hd_den', 'tbl_hrm_hopdong', 'den_ngay', 'Đến ngày', 'Đến ngày', 'DATE', FALSE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 6),
            ('col_hd_luong', 'tbl_hrm_hopdong', 'luong_hop_dong', 'Mức lương HĐ (VNĐ)', 'Lương HĐ', 'CURRENCY', TRUE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 7),
            ('col_hd_st', 'tbl_hrm_hopdong', 'trang_thai_hd', 'Trạng thái HĐ', 'Trạng thái HĐ', 'SELECT', TRUE, FALSE, 150, NULL, NULL, NULL, NULL, 'DM_TRANG_THAI_HD', NULL, 'HIEU_LUC', FALSE, 8),
            ('col_hd_file', 'tbl_hrm_hopdong', 'tep_hop_dong', 'Bản scan hợp đồng', 'File HĐ', 'FILE', FALSE, FALSE, 180, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 9),

            ('col_npt_ma', 'tbl_hrm_nguoiphuthuoc', 'ma_npt', 'Mã hồ sơ NPT', 'Mã NPT', 'STRING', TRUE, TRUE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
            ('col_npt_nv', 'tbl_hrm_nguoiphuthuoc', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, TRUE, 2),
            ('col_npt_ten', 'tbl_hrm_nguoiphuthuoc', 'ho_ten_npt', 'Họ tên người phụ thuộc', 'Họ tên NPT', 'STRING', TRUE, FALSE, 210, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 3),
            ('col_npt_qh', 'tbl_hrm_nguoiphuthuoc', 'quan_he', 'Quan hệ', 'Quan hệ', 'SELECT', TRUE, FALSE, 160, NULL, NULL, NULL, NULL, 'DM_QUAN_HE_NPT', NULL, 'CON', FALSE, 4),
            ('col_npt_cccd', 'tbl_hrm_nguoiphuthuoc', 'so_cccd_npt', 'Số CCCD / Giấy KS', 'Số CCCD NPT', 'STRING', FALSE, FALSE, 160, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 5),
            ('col_npt_tu', 'tbl_hrm_nguoiphuthuoc', 'tu_thang', 'Giảm trừ từ tháng', 'Từ tháng', 'STRING', TRUE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 6),
            ('col_npt_act', 'tbl_hrm_nguoiphuthuoc', 'dang_giam_tru', 'Đang giảm trừ', 'Đang giảm trừ', 'BOOLEAN', FALSE, FALSE, 125, NULL, NULL, NULL, NULL, NULL, NULL, 'true', FALSE, 7),

            ('col_tc_ma', 'tbl_hrm_tangca', 'ma_phieu', 'Mã phiếu OT', 'Mã phiếu OT', 'STRING', TRUE, TRUE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
            ('col_tc_nv', 'tbl_hrm_tangca', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, FALSE, 2),
            ('col_tc_loai', 'tbl_hrm_tangca', 'loai_de_xuat', 'Loại đề xuất', 'Loại đề xuất', 'SELECT', TRUE, FALSE, 210, NULL, NULL, NULL, NULL, 'DM_LOAI_TANG_CA', NULL, 'OT_THUONG', FALSE, 3),
            ('col_tc_ngay', 'tbl_hrm_tangca', 'ngay_ap_dung', 'Ngày áp dụng', 'Ngày áp dụng', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 4),
            ('col_tc_gio', 'tbl_hrm_tangca', 'so_gio', 'Số giờ OT', 'Số giờ OT', 'NUMBER', TRUE, FALSE, 110, NULL, NULL, NULL, NULL, NULL, NULL, '2', FALSE, 5),
            ('col_tc_ld', 'tbl_hrm_tangca', 'ly_do', 'Lý do / Công việc thực hiện', 'Lý do', 'TEXTAREA', FALSE, FALSE, 250, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 6),

            ('col_bc_ma', 'tbl_hrm_bangcong', 'ma_cong', 'Mã bảng công', 'Mã công', 'STRING', TRUE, TRUE, 140, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
            ('col_bc_ky', 'tbl_hrm_bangcong', 'ky_cong', 'Kỳ công (YYYY-MM)', 'Kỳ công', 'STRING', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 2),
            ('col_bc_nv', 'tbl_hrm_bangcong', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, FALSE, 3),
            ('col_bc_chuan', 'tbl_hrm_bangcong', 'cong_chuan', 'Công chuẩn', 'Công chuẩn', 'NUMBER', TRUE, FALSE, 115, NULL, NULL, NULL, NULL, NULL, NULL, '22', FALSE, 4),
            ('col_bc_tt', 'tbl_hrm_bangcong', 'cong_thuc_te', 'Công thực tế', 'Công thực tế', 'NUMBER', TRUE, FALSE, 120, NULL, NULL, NULL, NULL, NULL, NULL, '22', FALSE, 5),
            ('col_bc_phep', 'tbl_hrm_bangcong', 'nghi_co_luong', 'Nghỉ có lương', 'Nghỉ phép', 'NUMBER', FALSE, FALSE, 120, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 6),
            ('col_bc_ot', 'tbl_hrm_bangcong', 'gio_ot', 'Tổng giờ OT', 'Giờ OT', 'NUMBER', FALSE, FALSE, 110, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 7),

            ('col_bl_ma', 'tbl_hrm_bangluong', 'ma_phieu_luong', 'Mã phiếu lương', 'Mã phiếu lương', 'STRING', TRUE, TRUE, 150, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
            ('col_bl_ky', 'tbl_hrm_bangluong', 'ky_luong', 'Kỳ lương (YYYY-MM)', 'Kỳ lương', 'STRING', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 2),
            ('col_bl_nv', 'tbl_hrm_bangluong', 'nhan_vien', 'Nhân viên', 'Nhân viên', 'LOOKUP', TRUE, FALSE, 210, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, FALSE, 3),
            ('col_bl_lcb', 'tbl_hrm_bangluong', 'luong_co_ban', 'Lương cơ bản (VNĐ)', 'Lương cơ bản', 'CURRENCY', TRUE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 4),
            ('col_bl_pc', 'tbl_hrm_bangluong', 'tong_phu_cap', 'Phụ cấp (VNĐ)', 'Phụ cấp', 'CURRENCY', FALSE, FALSE, 150, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 5),
            ('col_bl_ot', 'tbl_hrm_bangluong', 'tien_ot', 'Tiền tăng ca / Thưởng', 'Tăng ca / Thưởng', 'CURRENCY', FALSE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 6),
            ('col_bl_bh', 'tbl_hrm_bangluong', 'khau_tru_bhxh', 'Khấu trừ BHXH (10.5%)', 'BHXH', 'CURRENCY', FALSE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, 'luong_co_ban * 0.105', NULL, FALSE, 7),
            ('col_bl_thue', 'tbl_hrm_bangluong', 'thue_tncn', 'Thuế TNCN (VNĐ)', 'Thuế TNCN', 'CURRENCY', FALSE, FALSE, 150, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 8),
            ('col_bl_tl', 'tbl_hrm_bangluong', 'thuc_linh', 'Thực lĩnh (Auto)', 'Thực lĩnh', 'CURRENCY', FALSE, FALSE, 175, NULL, NULL, NULL, NULL, NULL, 'luong_co_ban + tong_phu_cap + tien_ot - khau_tru_bhxh - thue_tncn', NULL, FALSE, 9)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            UPDATE sys_columns
            SET show_in_parent_form = TRUE
            WHERE id IN ('col_hd_nv', 'col_npt_nv', 'col_pc_nv', 'col_bh_nv');

            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_hrm', t.id, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, 'ALL'
            FROM sys_roles r
            CROSS JOIN (VALUES ('tbl_hrm_hopdong'), ('tbl_hrm_nguoiphuthuoc'), ('tbl_hrm_tangca'), ('tbl_hrm_bangcong'), ('tbl_hrm_bangluong')) AS t(id)
            WHERE UPPER(r.code) = 'HR_MANAGER'
            ON CONFLICT (role_id, table_id) DO NOTHING;

            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_hrm', t.id, TRUE,
                   (CASE WHEN t.id = 'tbl_hrm_tangca' THEN TRUE ELSE FALSE END),
                   (CASE WHEN t.id = 'tbl_hrm_tangca' THEN TRUE ELSE FALSE END),
                   FALSE, FALSE, FALSE, 'OWNER_ONLY'
            FROM sys_roles r
            CROSS JOIN (VALUES ('tbl_hrm_hopdong'), ('tbl_hrm_nguoiphuthuoc'), ('tbl_hrm_tangca'), ('tbl_hrm_bangcong'), ('tbl_hrm_bangluong')) AS t(id)
            WHERE UPPER(r.code) = 'STAFF'
            ON CONFLICT (role_id, table_id) DO NOTHING;

            INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status, created_by)
            VALUES
            ('rec_hd_1', 'app_hrm', 'mod_hrm_hoso', 'tbl_hrm_hopdong', 'HDLD-2024-001', '{"so_hop_dong":"HDLD-2024-001","nhan_vien":"NV001","loai_hop_dong":"KHONG_THOI_HAN","ngay_ky":"2024-04-01","tu_ngay":"2024-04-01","luong_hop_dong":32000000,"trang_thai_hd":"HIEU_LUC"}'::jsonb, 'ACTIVE', 'hr_manager'),
            ('rec_hd_2', 'app_hrm', 'mod_hrm_hoso', 'tbl_hrm_hopdong', 'HDLD-2025-002', '{"so_hop_dong":"HDLD-2025-002","nhan_vien":"NV002","loai_hop_dong":"CO_THOI_HAN_24","ngay_ky":"2025-09-01","tu_ngay":"2025-09-01","den_ngay":"2027-09-01","luong_hop_dong":28000000,"trang_thai_hd":"HIEU_LUC"}'::jsonb, 'ACTIVE', 'hr_manager'),
            ('rec_npt_1', 'app_hrm', 'mod_hrm_hoso', 'tbl_hrm_nguoiphuthuoc', 'NPT-001', '{"ma_npt":"NPT-001","nhan_vien":"NV001","ho_ten_npt":"Nguyễn Minh Khang","quan_he":"CON","so_cccd_npt":"079218001122","tu_thang":"2025-01","dang_giam_tru":true}'::jsonb, 'ACTIVE', 'hr_manager'),
            ('rec_tc_1', 'app_hrm', 'mod_hrm_nghiphep', 'tbl_hrm_tangca', 'OT-2026-01', '{"ma_phieu":"OT-2026-01","nhan_vien":"NV001","loai_de_xuat":"OT_THUONG","ngay_ap_dung":"2026-11-10","so_gio":3,"ly_do":"Triển khai nâng cấp hệ thống SmartApps HRM."}'::jsonb, 'ACTIVE', 'hr_manager'),
            ('rec_bc_1', 'app_hrm', 'mod_hrm_nghiphep', 'tbl_hrm_bangcong', 'CC-202611-NV001', '{"ma_cong":"CC-202611-NV001","ky_cong":"2026-11","nhan_vien":"NV001","cong_chuan":22,"cong_thuc_te":22,"nghi_co_luong":0,"gio_ot":3}'::jsonb, 'ACTIVE', 'hr_manager'),
            ('rec_bl_1', 'app_hrm', 'mod_hrm_luong', 'tbl_hrm_bangluong', 'PL-202611-NV001', '{"ma_phieu_luong":"PL-202611-NV001","ky_luong":"2026-11","nhan_vien":"NV001","luong_co_ban":32000000,"tong_phu_cap":2500000,"tien_ot":850000,"khau_tru_bhxh":3360000,"thue_tncn":1250000,"thuc_linh":30740000}'::jsonb, 'ACTIVE', 'hr_manager')
            ON CONFLICT (id) DO NOTHING;
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpgradeOtherAppsSchemaAndSeedAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_categories (category_code, category_name, app_id, description)
            VALUES
            ('DM_LOAI_PHIEU_CP', 'Loại phiếu cấp phát tài sản', 'app_asset', 'Phân loại cấp mới, điều chuyển hoặc thu hồi tài sản'),
            ('DM_TRANG_THAI_BG', 'Trạng thái bàn giao tài sản', 'app_asset', 'Trạng thái xác nhận bàn giao / thu hồi thiết bị'),
            ('DM_LOAI_BAO_TRI', 'Loại bảo trì thiết bị', 'app_asset', 'Bảo dưỡng định kỳ, sửa chữa hoặc nâng cấp'),
            ('DM_TRANG_THAI_BT', 'Trạng thái bảo trì', 'app_asset', 'Tiến độ xử lý phiếu bảo trì tài sản'),
            ('DM_KET_LUAN_KK', 'Kết luận kiểm kê tài sản', 'app_asset', 'Kết quả đối chiếu số lượng sổ sách và thực tế'),
            ('DM_TRANG_THAI_XL', 'Trạng thái xử lý yêu cầu', 'app_ops', 'Luồng xử lý ticket nội bộ (Kanban)'),
            ('DM_TAI_NGUYEN_DAT', 'Tài nguyên đặt lịch', 'app_ops', 'Danh sách phòng họp và xe công tác công ty'),
            ('DM_TRANG_THAI_TASK', 'Trạng thái công việc', 'app_tasks', 'Trạng thái Kanban công việc')
            ON CONFLICT (category_code) DO NOTHING;

            INSERT INTO sys_category_items (id, category_code, item_code, item_name, color, order_index)
            VALUES
            ('ci_dm_lpcp_1', 'DM_LOAI_PHIEU_CP', 'CAP_MOI', 'Cấp phát mới', '#10b981', 1),
            ('ci_dm_lpcp_2', 'DM_LOAI_PHIEU_CP', 'DIEU_CHUYEN', 'Điều chuyển bộ phận', '#3b82f6', 2),
            ('ci_dm_lpcp_3', 'DM_LOAI_PHIEU_CP', 'THU_HOI', 'Thu hồi về kho', '#f59e0b', 3),
            ('ci_dm_ttbg_1', 'DM_TRANG_THAI_BG', 'CHO_NHAN', 'Chờ ký nhận', '#f59e0b', 1),
            ('ci_dm_ttbg_2', 'DM_TRANG_THAI_BG', 'DA_BAN_GIAO', 'Đã bàn giao', '#10b981', 2),
            ('ci_dm_ttbg_3', 'DM_TRANG_THAI_BG', 'DA_THU_HOI', 'Đã thu hồi', '#64748b', 3),
            ('ci_dm_lbt_1', 'DM_LOAI_BAO_TRI', 'BAO_DUONG', 'Bảo dưỡng định kỳ', '#3b82f6', 1),
            ('ci_dm_lbt_2', 'DM_LOAI_BAO_TRI', 'SUA_CHUA', 'Sửa chữa hư hỏng', '#ef4444', 2),
            ('ci_dm_lbt_3', 'DM_LOAI_BAO_TRI', 'NANG_CAP', 'Nâng cấp linh kiện', '#10b981', 3),
            ('ci_dm_ttbt_1', 'DM_TRANG_THAI_BT', 'CHO_XU_LY', 'Chờ xử lý', '#f59e0b', 1),
            ('ci_dm_ttbt_2', 'DM_TRANG_THAI_BT', 'DANG_BAO_TRI', 'Đang thực hiện', '#3b82f6', 2),
            ('ci_dm_ttbt_3', 'DM_TRANG_THAI_BT', 'HOAN_THANH', 'Hoàn thành', '#10b981', 3),
            ('ci_dm_klkk_1', 'DM_KET_LUAN_KK', 'KHOP', 'Khớp số liệu', '#10b981', 1),
            ('ci_dm_klkk_2', 'DM_KET_LUAN_KK', 'THIEU', 'Thiếu / Thất lạc', '#ef4444', 2),
            ('ci_dm_klkk_3', 'DM_KET_LUAN_KK', 'HONG', 'Hỏng chờ thanh lý', '#f59e0b', 3),
            ('ci_dm_ttxl_1', 'DM_TRANG_THAI_XL', 'MOI', 'Mới tiếp nhận', '#3b82f6', 1),
            ('ci_dm_ttxl_2', 'DM_TRANG_THAI_XL', 'DANG_XU_LY', 'Đang xử lý', '#f59e0b', 2),
            ('ci_dm_ttxl_3', 'DM_TRANG_THAI_XL', 'CHO_PHAN_HOI', 'Chờ phản hồi', '#6366f1', 3),
            ('ci_dm_ttxl_4', 'DM_TRANG_THAI_XL', 'HOAN_THANH', 'Đã hoàn thành', '#10b981', 4),
            ('ci_dm_ttxl_5', 'DM_TRANG_THAI_XL', 'DA_HUY', 'Đã hủy', '#64748b', 5),
            ('ci_dm_tnd_1', 'DM_TAI_NGUYEN_DAT', 'PHONG_HOP_A', 'Phòng họp Lớn (Tầng 5 - 20 chỗ)', '#1a73e8', 1),
            ('ci_dm_tnd_2', 'DM_TAI_NGUYEN_DAT', 'PHONG_HOP_B', 'Phòng họp Nhỏ (Tầng 3 - 8 chỗ)', '#00897b', 2),
            ('ci_dm_tnd_3', 'DM_TAI_NGUYEN_DAT', 'XE_7_CHO', 'Xe công tác 7 chỗ (51A-888.99)', '#ea8600', 3),
            ('ci_dm_tnd_4', 'DM_TAI_NGUYEN_DAT', 'XE_4_CHO', 'Xe công tác 4 chỗ (51F-666.88)', '#7b1fa2', 4),
            ('ci_dm_ttt_1', 'DM_TRANG_THAI_TASK', 'TODO', 'Cần làm', '#64748b', 1),
            ('ci_dm_ttt_2', 'DM_TRANG_THAI_TASK', 'IN_PROGRESS', 'Đang thực hiện', '#3b82f6', 2),
            ('ci_dm_ttt_3', 'DM_TRANG_THAI_TASK', 'REVIEW', 'Chờ kiểm tra', '#f59e0b', 3),
            ('ci_dm_ttt_4', 'DM_TRANG_THAI_TASK', 'DONE', 'Hoàn thành', '#10b981', 4)
            ON CONFLICT (category_code, item_code) DO NOTHING;

            -- 1. APP ASSET: Bổ sung cột danh mục tài sản & cấp phát + Thêm Bảo trì & Kiểm kê
            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, show_in_table, show_in_form, width, ref_category_code, default_value, order_index)
            SELECT 'col_as_sn', 'tbl_asset_danhmuc', 'serial_number', 'Số Serial / Service Tag', 'Số Serial', 'STRING', FALSE, FALSE, TRUE, TRUE, 165, NULL, NULL, 9
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = 'tbl_asset_danhmuc')
            ON CONFLICT (table_id, column_code) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, show_in_table, show_in_form, width, ref_category_code, default_value, order_index)
            SELECT v.id, v.tbl, v.code, v.name, v.name, v.dt, FALSE, FALSE, v.sit, TRUE, v.w, v.cat, v.def, v.ord
            FROM (VALUES
                ('col_as_hang', 'tbl_asset_danhmuc', 'hang_san_xuat', 'Hãng / Model', 'STRING', FALSE, 170, NULL, NULL, 10),
                ('col_as_bh', 'tbl_asset_danhmuc', 'han_bao_hanh', 'Hạn bảo hành', 'DATE', TRUE, 135, NULL, NULL, 11),
                ('col_as_ncc', 'tbl_asset_danhmuc', 'nha_cung_cap', 'Nhà cung cấp', 'STRING', FALSE, 185, NULL, NULL, 12),
                ('col_as_vitri', 'tbl_asset_danhmuc', 'vi_tri_luu_tru', 'Vị trí lưu trữ', 'STRING', FALSE, 175, NULL, NULL, 13),
                ('col_cp_loai', 'tbl_asset_capphat', 'loai_phieu', 'Loại phiếu', 'SELECT', TRUE, 155, 'DM_LOAI_PHIEU_CP', 'CAP_MOI', 7),
                ('col_cp_sl', 'tbl_asset_capphat', 'so_luong_cap', 'Số lượng', 'NUMBER', TRUE, 105, NULL, '1', 8),
                ('col_cp_ttbg', 'tbl_asset_capphat', 'trang_thai_bg', 'Trạng thái bàn giao', 'SELECT', TRUE, 155, 'DM_TRANG_THAI_BG', 'DA_BAN_GIAO', 9),
                ('col_cp_chuky', 'tbl_asset_capphat', 'chu_ky_nhan', 'Chữ ký người nhận', 'SIGNATURE', FALSE, 180, NULL, NULL, 10)
            ) AS v(id, tbl, code, name, dt, sit, w, cat, def, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = v.tbl)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
            SELECT v.id, 'app_asset', v.mod_id, v.tname, v.dname, v.descr, 'table', 'ALL', FALSE, v.ord
            FROM (VALUES
                ('tbl_asset_baotri', 'mod_asset_dm', 'asset_bao_tri', 'Bảo trì & Sửa chữa Thiết bị', 'Theo dõi lịch sử bảo dưỡng, sửa chữa và chi phí bảo trì tài sản', 2),
                ('tbl_asset_kiemke', 'mod_asset_capphat', 'asset_kiem_ke', 'Kiểm kê Tài sản định kỳ', 'Đối chiếu số lượng sổ sách với thực tế kiểm kê tại phòng ban/kho', 2)
            ) AS v(id, mod_id, tname, dname, descr, ord)
            WHERE EXISTS (SELECT 1 FROM sys_modules WHERE id = v.mod_id)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, formula, default_value, show_in_parent_form, order_index)
            SELECT v.id, v.tbl, v.code, v.name, v.name, v.dt, v.req, v.ikey, v.w, v.ra, v.rt, v.rv, v.rd, v.rc, v.fm, v.def, v.sipf, v.ord
            FROM (VALUES
                ('col_bt_ma', 'tbl_asset_baotri', 'ma_phieu_bt', 'Mã phiếu bảo trì', 'STRING', TRUE, TRUE, 140, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
                ('col_bt_ts', 'tbl_asset_baotri', 'ma_ts', 'Tài sản / Thiết bị', 'LOOKUP', TRUE, FALSE, 210, 'app_asset', 'tbl_asset_danhmuc', 'ma_ts', 'ten_ts', NULL, NULL, NULL, TRUE, 2),
                ('col_bt_loai', 'tbl_asset_baotri', 'loai_bao_tri', 'Loại bảo trì', 'SELECT', TRUE, FALSE, 175, NULL, NULL, NULL, NULL, 'DM_LOAI_BAO_TRI', NULL, 'BAO_DUONG', FALSE, 3),
                ('col_bt_ngay', 'tbl_asset_baotri', 'ngay_thuc_hien', 'Ngày thực hiện', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 4),
                ('col_bt_cp', 'tbl_asset_baotri', 'chi_phi', 'Chi phí (VNĐ)', 'CURRENCY', FALSE, FALSE, 155, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 5),
                ('col_bt_dv', 'tbl_asset_baotri', 'don_vi_thuc_hien', 'Đơn vị thực hiện', 'STRING', FALSE, FALSE, 185, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 6),
                ('col_bt_st', 'tbl_asset_baotri', 'trang_thai_bt', 'Trạng thái', 'SELECT', TRUE, FALSE, 145, NULL, NULL, NULL, NULL, 'DM_TRANG_THAI_BT', NULL, 'CHO_XU_LY', FALSE, 7),

                ('col_kk_ma', 'tbl_asset_kiemke', 'ma_kiem_ke', 'Mã phiếu kiểm kê', 'STRING', TRUE, TRUE, 145, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 1),
                ('col_kk_ky', 'tbl_asset_kiemke', 'ky_kiem_ke', 'Kỳ kiểm kê', 'STRING', TRUE, FALSE, 125, NULL, NULL, NULL, NULL, NULL, NULL, NULL, FALSE, 2),
                ('col_kk_ts', 'tbl_asset_kiemke', 'ma_ts', 'Tài sản', 'LOOKUP', TRUE, FALSE, 210, 'app_asset', 'tbl_asset_danhmuc', 'ma_ts', 'ten_ts', NULL, NULL, NULL, FALSE, 3),
                ('col_kk_nv', 'tbl_asset_kiemke', 'nguoi_kiem_ke', 'Người kiểm kê', 'LOOKUP', TRUE, FALSE, 195, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, FALSE, 4),
                ('col_kk_ss', 'tbl_asset_kiemke', 'so_luong_so_sach', 'SL Sổ sách', 'NUMBER', TRUE, FALSE, 115, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 5),
                ('col_kk_tt', 'tbl_asset_kiemke', 'so_luong_thuc_te', 'SL Thực tế', 'NUMBER', TRUE, FALSE, 115, NULL, NULL, NULL, NULL, NULL, NULL, '0', FALSE, 6),
                ('col_kk_cl', 'tbl_asset_kiemke', 'chenh_lech', 'Chênh lệch (Auto)', 'NUMBER', FALSE, FALSE, 130, NULL, NULL, NULL, NULL, NULL, 'so_luong_thuc_te - so_luong_so_sach', NULL, FALSE, 7),
                ('col_kk_kl', 'tbl_asset_kiemke', 'ket_luan', 'Kết luận', 'SELECT', TRUE, FALSE, 155, NULL, NULL, NULL, NULL, 'DM_KET_LUAN_KK', NULL, 'KHOP', FALSE, 8)
            ) AS v(id, tbl, code, name, dt, req, ikey, w, ra, rt, rv, rd, rc, fm, def, sipf, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = v.tbl)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            -- 2. APP OPS: Nâng cấp Ticket + Thêm Phân hệ Hành chính & Chi phí (Mua sắm + Đặt phòng/xe)
            INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, description)
            SELECT 'mod_ops_hanhchinh', 'app_ops', 'HANHCHINH', 'Hành chính & Chi phí', 'ReceiptLong', 2, 'Đề xuất mua sắm nội bộ và đặt lịch phòng họp, xe công tác'
            WHERE EXISTS (SELECT 1 FROM sys_apps WHERE id = 'app_ops')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, show_in_table, show_in_form, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, default_value, order_index)
            SELECT v.id, 'tbl_ops_yeucau', v.code, v.name, v.name, v.dt, FALSE, FALSE, v.sit, TRUE, v.w, v.ra, v.rt, v.rv, v.rd, v.rc, v.def, v.ord
            FROM (VALUES
                ('col_op_ttxl', 'trang_thai_xl', 'Trạng thái xử lý', 'SELECT', TRUE, 150, NULL, NULL, NULL, NULL, 'DM_TRANG_THAI_XL', 'MOI', 10),
                ('col_op_nxl', 'nguoi_xu_ly', 'Người phụ trách xử lý', 'LOOKUP', TRUE, 195, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, 11),
                ('col_op_pb', 'phong_ban_tiep_nhan', 'Bộ phận tiếp nhận', 'SELECT', FALSE, 175, NULL, NULL, NULL, NULL, 'DM_PHONG_BAN', NULL, 12),
                ('col_op_file', 'tep_dinh_kem', 'Tệp đính kèm', 'FILE', FALSE, 170, NULL, NULL, NULL, NULL, NULL, NULL, 13),
                ('col_op_kq', 'ket_qua_xu_ly', 'Kết quả / Phản hồi xử lý', 'TEXTAREA', FALSE, 240, NULL, NULL, NULL, NULL, NULL, NULL, 14)
            ) AS v(id, code, name, dt, sit, w, ra, rt, rv, rd, rc, def, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = 'tbl_ops_yeucau')
            ON CONFLICT (table_id, column_code) DO NOTHING;

            INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, row_scope, approval_enabled, order_index)
            SELECT v.id, 'app_ops', 'mod_ops_hanhchinh', v.tname, v.dname, v.descr, v.pview, v.rscope, TRUE, v.ord
            FROM (VALUES
                ('tbl_ops_muasam', 'ops_mua_sam', 'Đề xuất Mua sắm & Chi phí', 'Đề xuất mua văn phòng phẩm, thiết bị, chi phí hành chính kèm bảng kê chi tiết', 'table', 'OWNER_ONLY', 1),
                ('tbl_ops_datlich', 'ops_dat_lich', 'Đặt Phòng họp & Điều xe', 'Đăng ký sử dụng phòng họp và xe công tác theo lịch biểu', 'calendar', 'ALL', 2)
            ) AS v(id, tname, dname, descr, pview, rscope, ord)
            WHERE EXISTS (SELECT 1 FROM sys_modules WHERE id = 'mod_ops_hanhchinh')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, ref_app_id, ref_table_id, ref_value_field, ref_display_field, ref_category_code, formula, default_value, order_index)
            SELECT v.id, v.tbl, v.code, v.name, v.name, v.dt, v.req, v.ikey, v.w, v.ra, v.rt, v.rv, v.rd, v.rc, v.fm, v.def, v.ord
            FROM (VALUES
                ('col_ms_ma', 'tbl_ops_muasam', 'ma_de_xuat', 'Mã đề xuất', 'STRING', TRUE, TRUE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                ('col_ms_td', 'tbl_ops_muasam', 'tieu_de', 'Nội dung đề xuất', 'STRING', TRUE, FALSE, 240, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2),
                ('col_ms_nv', 'tbl_ops_muasam', 'nguoi_de_xuat', 'Người đề xuất', 'LOOKUP', TRUE, FALSE, 195, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, 3),
                ('col_ms_pb', 'tbl_ops_muasam', 'phong_ban', 'Phòng ban', 'SELECT', TRUE, FALSE, 175, NULL, NULL, NULL, NULL, 'DM_PHONG_BAN', NULL, NULL, 4),
                ('col_ms_ngay', 'tbl_ops_muasam', 'ngay_can_hang', 'Ngày cần hàng', 'DATE', FALSE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                ('col_ms_ct', 'tbl_ops_muasam', 'chi_tiet_hang_muc', 'Chi tiết hạng mục mua sắm', 'SUB_TABLE', FALSE, FALSE, 300, NULL, NULL, NULL, NULL, NULL, NULL, 'ten_hang,so_luong,don_gia,thanh_tien', 6),
                ('col_ms_tong', 'tbl_ops_muasam', 'tong_du_kien', 'Tổng dự kiến (Auto)', 'CURRENCY', FALSE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, 'SUM(chi_tiet_hang_muc.thanh_tien)', NULL, 7),

                ('col_dl_ma', 'tbl_ops_datlich', 'ma_dat', 'Mã đặt lịch', 'STRING', TRUE, TRUE, 130, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1),
                ('col_dl_tn', 'tbl_ops_datlich', 'loai_tai_nguyen', 'Phòng họp / Xe công tác', 'SELECT', TRUE, FALSE, 230, NULL, NULL, NULL, NULL, 'DM_TAI_NGUYEN_DAT', NULL, NULL, 2),
                ('col_dl_nv', 'tbl_ops_datlich', 'nguoi_dat', 'Người đăng ký', 'LOOKUP', TRUE, FALSE, 195, 'app_hrm', 'tbl_hrm_nhanvien', 'ma_nv', 'ho_ten', NULL, NULL, NULL, 3),
                ('col_dl_td', 'tbl_ops_datlich', 'tieu_de', 'Mục đích sử dụng', 'STRING', TRUE, FALSE, 230, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 4),
                ('col_dl_tu', 'tbl_ops_datlich', 'tu_ngay', 'Ngày sử dụng', 'DATE', TRUE, FALSE, 135, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 5),
                ('col_dl_gio', 'tbl_ops_datlich', 'khung_gio', 'Khung giờ (VD: 09:00-11:00)', 'STRING', TRUE, FALSE, 165, NULL, NULL, NULL, NULL, NULL, NULL, '09:00 - 11:00', 6),
                ('col_dl_sn', 'tbl_ops_datlich', 'so_nguoi', 'Số người tham gia', 'NUMBER', FALSE, FALSE, 120, NULL, NULL, NULL, NULL, NULL, NULL, '4', 7)
            ) AS v(id, tbl, code, name, dt, req, ikey, w, ra, rt, rv, rd, rc, fm, def, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = v.tbl)
            ON CONFLICT (table_id, column_code) DO NOTHING;

            -- 3. APP TASKS: Chuẩn hóa SELECT trạng thái/ưu tiên & bổ sung trường + Widgets
            UPDATE sys_columns
            SET data_type = 'SELECT', ref_category_code = 'DM_TRANG_THAI_TASK', default_value = COALESCE(default_value, 'TODO')
            WHERE id = 'col_tsk_status' AND data_type = 'STRING';

            UPDATE sys_columns
            SET data_type = 'SELECT', ref_category_code = 'DM_UU_TIEN', default_value = COALESCE(default_value, 'MEDIUM')
            WHERE id = 'col_tsk_priority' AND data_type = 'STRING';

            INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, default_value, order_index)
            SELECT v.id, 'tbl_tasks_congviec', v.code, v.name, v.name, v.dt, FALSE, FALSE, v.w, v.def, v.ord
            FROM (VALUES
                ('col_tsk_proj', 'project_name', 'Dự án / Nhóm việc', 'STRING', 185, NULL, 9),
                ('col_tsk_est', 'estimated_hours', 'Giờ ước tính', 'NUMBER', 115, '0', 10),
                ('col_tsk_act', 'actual_hours', 'Giờ thực tế', 'NUMBER', 115, '0', 11),
                ('col_tsk_desc', 'description', 'Mô tả chi tiết', 'TEXTAREA', 260, NULL, 12)
            ) AS v(id, code, name, dt, w, def, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = 'tbl_tasks_congviec')
            ON CONFLICT (table_id, column_code) DO NOTHING;

            -- Permissions for new tables
            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_asset', t.id, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, 'ALL'
            FROM sys_roles r
            CROSS JOIN (VALUES ('tbl_asset_baotri'), ('tbl_asset_kiemke')) AS t(id)
            WHERE UPPER(r.code) = 'ASSET_LEAD'
            ON CONFLICT (role_id, table_id) DO NOTHING;

            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_tbl_asset_capphat', r.id, 'app_asset', 'tbl_asset_capphat', TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, 'OWNER_ONLY'
            FROM sys_roles r
            WHERE UPPER(r.code) = 'STAFF'
            ON CONFLICT (role_id, table_id) DO NOTHING;

            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_ops', t.id, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, 'ALL'
            FROM sys_roles r
            CROSS JOIN (VALUES ('tbl_ops_muasam'), ('tbl_ops_datlich')) AS t(id)
            WHERE UPPER(r.code) IN ('HR_MANAGER', 'ASSET_LEAD')
            ON CONFLICT (role_id, table_id) DO NOTHING;

            INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope)
            SELECT 'rtp_' || r.id || '_' || t.id, r.id, 'app_ops', t.id, TRUE, TRUE, TRUE, FALSE, FALSE, FALSE, 'OWNER_ONLY'
            FROM sys_roles r
            CROSS JOIN (VALUES ('tbl_ops_muasam'), ('tbl_ops_datlich')) AS t(id)
            WHERE UPPER(r.code) = 'STAFF'
            ON CONFLICT (role_id, table_id) DO NOTHING;

            -- Sample records for new Asset & Ops tables
            INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status, created_by)
            SELECT v.id, v.app, v.mod, v.tbl, v.rkey, v.data::jsonb, 'ACTIVE', 'admin'
            FROM (VALUES
                ('rec_bt_1', 'app_asset', 'mod_asset_dm', 'tbl_asset_baotri', 'BT-2026-01', '{"ma_phieu_bt":"BT-2026-01","ma_ts":"TS-SRV-01","loai_bao_tri":"BAO_DUONG","ngay_thuc_hien":"2026-11-15","chi_phi":3500000,"don_vi_thuc_hien":"Trung tâm Dell ProSupport","trang_thai_bt":"HOAN_THANH"}'),
                ('rec_kk_1', 'app_asset', 'mod_asset_capphat', 'tbl_asset_kiemke', 'KK-2026Q4-01', '{"ma_kiem_ke":"KK-2026Q4-01","ky_kiem_ke":"2026-Q4","ma_ts":"TS-LAP-01","nguoi_kiem_ke":"NV001","so_luong_so_sach":5,"so_luong_thuc_te":5,"chenh_lech":0,"ket_luan":"KHOP"}'),
                ('rec_ms_1', 'app_ops', 'mod_ops_hanhchinh', 'tbl_ops_muasam', 'MS-2026-01', '{"ma_de_xuat":"MS-2026-01","tieu_de":"Mua bổ sung 05 màn hình Dell 27 inch","nguoi_de_xuat":"NV001","phong_ban":"TECH","ngay_can_hang":"2026-11-25","tong_du_kien":27500000,"chi_tiet_hang_muc":[{"ten_hang":"Màn hình Dell U2724D","so_luong":5,"don_gia":5500000,"thanh_tien":27500000}]}'),
                ('rec_dl_1', 'app_ops', 'mod_ops_hanhchinh', 'tbl_ops_datlich', 'DL-2026-01', '{"ma_dat":"DL-2026-01","loai_tai_nguyen":"PHONG_HOP_A","nguoi_dat":"NV002","tieu_de":"Họp giao ban nhân sự & vận hành tuần","tu_ngay":"2026-11-18","khung_gio":"09:00 - 10:30","so_nguoi":12}')
            ) AS v(id, app, mod, tbl, rkey, data)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = v.tbl)
            ON CONFLICT (id) DO NOTHING;

            -- Dashboard widgets for app_tasks & new tables
            INSERT INTO app_widgets (id, app_id, table_id, title, widget_type, metric_field, group_field, filter_field, filter_value, col_span, color, order_index)
            SELECT v.id, v.app, v.tbl, v.title, v.wtype, v.mfield, v.gfield, v.ffield, v.fval, v.cspan, v.color, v.ord
            FROM (VALUES
                ('wdg_tsk_1', 'app_tasks', 'tbl_tasks_congviec', 'Tổng Công việc', 'KPI_COUNT', NULL, NULL, NULL, NULL, 1, '#ea8600', 1),
                ('wdg_tsk_2', 'app_tasks', 'tbl_tasks_congviec', 'Đang thực hiện', 'KPI_COUNT', NULL, NULL, 'status', 'IN_PROGRESS', 1, '#1a73e8', 2),
                ('wdg_tsk_3', 'app_tasks', 'tbl_tasks_congviec', 'Đã hoàn thành', 'KPI_COUNT', NULL, NULL, 'status', 'DONE', 1, '#1e8e3e', 3),
                ('wdg_tsk_4', 'app_tasks', 'tbl_tasks_congviec', 'Phân bổ theo Trạng thái', 'DONUT_CHART', NULL, 'status', NULL, NULL, 2, '#ea8600', 4),
                ('wdg_as_7', 'app_asset', 'tbl_asset_baotri', 'Tổng Chi phí Bảo trì', 'KPI_SUM', 'chi_phi', NULL, NULL, NULL, 1, '#ef4444', 7),
                ('wdg_op_7', 'app_ops', 'tbl_ops_muasam', 'Tổng Dự kiến Mua sắm', 'KPI_SUM', 'tong_du_kien', NULL, NULL, NULL, 1, '#7b1fa2', 7)
            ) AS v(id, app, tbl, title, wtype, mfield, gfield, ffield, fval, cspan, color, ord)
            WHERE EXISTS (SELECT 1 FROM sys_tables WHERE id = v.tbl)
            ON CONFLICT (id) DO NOTHING;
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlatformAsync(NpgsqlConnection conn)
    {
        await using var tx = await conn.BeginTransactionAsync();

        var categories = new (string Code, string Name, string Desc)[]
        {
            ("DM_PHONG_BAN", "Phòng ban công ty", "Danh mục khối/phòng ban dùng chung toàn công ty"),
            ("DM_CHUC_VU", "Chức vụ nhân sự", "Chức danh công việc trong doanh nghiệp"),
            ("DM_LOAI_NGHI", "Loại nghỉ phép", "Các hình thức nghỉ phép, công tác"),
            ("DM_TRANG_THAI_DUYET", "Trạng thái phê duyệt", "Luồng duyệt đơn từ nội bộ"),
            ("DM_NHOM_TAI_SAN", "Nhóm tài sản", "Phân loại tài sản thiết bị công ty"),
            ("DM_TINH_TRANG_TS", "Tình trạng tài sản", "Trạng thái sử dụng thiết bị"),
            ("DM_LOAI_YEU_CAU", "Loại yêu cầu nội bộ", "Phân loại ticket hỗ trợ IT / Hành chính / Nhân sự"),
            ("DM_UU_TIEN", "Mức độ ưu tiên", "Mức độ ưu tiên xử lý công việc")
        };

        foreach (var (code, name, desc) in categories)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO sys_categories (category_code, category_name, description) VALUES (@c, @n, @d) ON CONFLICT DO NOTHING",
                conn, tx);
            cmd.Parameters.AddWithValue("c", code);
            cmd.Parameters.AddWithValue("n", name);
            cmd.Parameters.AddWithValue("d", desc);
            await cmd.ExecuteNonQueryAsync();
        }

        var catItems = new (string Cat, string Code, string Name, string Color, int Ord)[]
        {
            ("DM_PHONG_BAN", "TECH", "Khối Công nghệ & Kỹ thuật", "#1a73e8", 1),
            ("DM_PHONG_BAN", "HR", "Phòng Nhân sự (HR)", "#1e8e3e", 2),
            ("DM_PHONG_BAN", "FIN", "Phòng Tài chính - Kế toán", "#ea8600", 3),
            ("DM_PHONG_BAN", "OPS", "Phòng Vận hành & Hành chính", "#7b1fa2", 4),
            ("DM_PHONG_BAN", "SALES", "Khối Kinh doanh", "#0284c7", 5),

            ("DM_CHUC_VU", "DEV", "Kỹ sư Phần mềm", "#1a73e8", 1),
            ("DM_CHUC_VU", "LEAD", "Trưởng nhóm / Tech Lead", "#7b1fa2", 2),
            ("DM_CHUC_VU", "MGR", "Trưởng phòng", "#ea8600", 3),
            ("DM_CHUC_VU", "SPEC", "Chuyên viên", "#1e8e3e", 4),
            ("DM_CHUC_VU", "DIR", "Giám đốc khối", "#d93025", 5),

            ("DM_LOAI_NGHI", "PHEP_NAM", "Nghỉ phép năm", "#1a73e8", 1),
            ("DM_LOAI_NGHI", "OM", "Nghỉ ốm / Bảo hiểm", "#ea8600", 2),
            ("DM_LOAI_NGHI", "CONG_TAC", "Đi công tác", "#1e8e3e", 3),
            ("DM_LOAI_NGHI", "KHONG_LUONG", "Nghỉ không lương", "#5f6368", 4),

            ("DM_TRANG_THAI_DUYET", "CHO_DUYET", "Chờ duyệt", "#ea8600", 1),
            ("DM_TRANG_THAI_DUYET", "DA_DUYET", "Đã phê duyệt", "#1e8e3e", 2),
            ("DM_TRANG_THAI_DUYET", "TU_CHOI", "Từ chối", "#d93025", 3),

            ("DM_NHOM_TAI_SAN", "LAPTOP", "Laptop & Workstation", "#1a73e8", 1),
            ("DM_NHOM_TAI_SAN", "SERVER", "Máy chủ & Thiết bị mạng", "#7b1fa2", 2),
            ("DM_NHOM_TAI_SAN", "NOI_THAT", "Bàn ghế & Nội thất", "#ea8600", 3),
            ("DM_NHOM_TAI_SAN", "LICENSE", "Bản quyền Phần mềm", "#1e8e3e", 4),

            ("DM_TINH_TRANG_TS", "DANG_DUNG", "Đang sử dụng", "#1e8e3e", 1),
            ("DM_TINH_TRANG_TS", "TRONG_KHO", "Lưu kho sẵn sàng", "#1a73e8", 2),
            ("DM_TINH_TRANG_TS", "BAO_TRI", "Đang bảo trì", "#ea8600", 3),
            ("DM_TINH_TRANG_TS", "THANH_LY", "Chờ thanh lý", "#d93025", 4),

            ("DM_LOAI_YEU_CAU", "IT_SUPPORT", "Hỗ trợ CNTT / Cấp quyền", "#1a73e8", 1),
            ("DM_LOAI_YEU_CAU", "HANH_CHINH", "Văn phòng phẩm & Hành chính", "#ea8600", 2),
            ("DM_LOAI_YEU_CAU", "THANH_TOAN", "Đề nghị thanh toán", "#1e8e3e", 3),
            ("DM_LOAI_YEU_CAU", "TUYEN_DUNG", "Đề xuất tuyển dụng", "#7b1fa2", 4),

            ("DM_UU_TIEN", "HIGH", "Cao (Khẩn cấp)", "#d93025", 1),
            ("DM_UU_TIEN", "MEDIUM", "Trung bình", "#ea8600", 2),
            ("DM_UU_TIEN", "LOW", "Thấp", "#1e8e3e", 3)
        };

        foreach (var (cat, code, name, color, ord) in catItems)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO sys_category_items (id, category_code, item_code, item_name, color, order_index) VALUES (@id, @cat, @c, @n, @col, @o) ON CONFLICT DO NOTHING",
                conn, tx);
            cmd.Parameters.AddWithValue("id", $"ci_{cat.ToLower()}_{code.ToLower()}");
            cmd.Parameters.AddWithValue("cat", cat);
            cmd.Parameters.AddWithValue("c", code);
            cmd.Parameters.AddWithValue("n", name);
            cmd.Parameters.AddWithValue("col", color);
            cmd.Parameters.AddWithValue("o", ord);
            await cmd.ExecuteNonQueryAsync();
        }

        var apps = new (string Id, string Code, string Name, string Desc, string Dept, string Icon, string Color, string Bg, int Ord)[]
        {
            ("app_hrm", "HRM", "Quản trị Nhân sự (HRM)", "Quản lý hồ sơ nhân viên, hợp đồng lao động, nghỉ phép & lương thưởng nội bộ.", "Phòng Nhân sự", "Groups", "#1a73e8", "#e8f0fe", 1),
            ("app_asset", "ASSET", "Quản lý Tài sản & Thiết bị", "Theo dõi danh mục tài sản cố định, tự động tính giá trị và cấp phát liên kết HRM.", "Hành chính & IT", "Inventory", "#00897b", "#e0f2f1", 2),
            ("app_ops", "OPS", "Yêu cầu & Vận hành Nội bộ", "Cổng tiếp nhận đề xuất công việc, hỗ trợ IT, duyệt chi phí liên kết đa phòng ban.", "Văn phòng & Vận hành", "Assignment", "#7b1fa2", "#f3e5f5", 3)
        };

        foreach (var a in apps)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_apps (id, code, name, description, department, icon, color, bg_color, order_index)
                VALUES (@id, @c, @n, @d, @dept, @ic, @col, @bg, @o) ON CONFLICT DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", a.Id);
            cmd.Parameters.AddWithValue("c", a.Code);
            cmd.Parameters.AddWithValue("n", a.Name);
            cmd.Parameters.AddWithValue("d", a.Desc);
            cmd.Parameters.AddWithValue("dept", a.Dept);
            cmd.Parameters.AddWithValue("ic", a.Icon);
            cmd.Parameters.AddWithValue("col", a.Color);
            cmd.Parameters.AddWithValue("bg", a.Bg);
            cmd.Parameters.AddWithValue("o", a.Ord);
            await cmd.ExecuteNonQueryAsync();
        }

        var modules = new (string Id, string AppId, string Code, string Name, string Icon, int Ord, string Desc)[]
        {
            ("mod_hrm_hoso", "app_hrm", "HOSO", "Hồ sơ Nhân sự", "Badge", 1, "Danh sách cán bộ nhân viên toàn công ty"),
            ("mod_hrm_nghiphep", "app_hrm", "NGHIPHEP", "Chấm công & Nghỉ phép", "EventAvailable", 2, "Quản lý đơn xin nghỉ và phê duyệt"),
            ("mod_asset_dm", "app_asset", "TAISAN", "Tài sản Cố định", "Devices", 1, "Quản lý thiết bị, bản quyền, tự động tính thành tiền"),
            ("mod_asset_capphat", "app_asset", "CAPPHAT", "Cấp phát & Bàn giao", "SwapHoriz", 2, "Lịch sử cấp phát tài sản cho nhân viên"),
            ("mod_ops_ticket", "app_ops", "YEUCAU", "Tiếp nhận Yêu cầu", "SupportAgent", 1, "Theo dõi ticket & yêu cầu hỗ trợ nội bộ")
        };

        foreach (var m in modules)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_modules (id, app_id, code, name, icon, order_index, description)
                VALUES (@id, @app, @c, @n, @ic, @o, @d) ON CONFLICT DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", m.Id);
            cmd.Parameters.AddWithValue("app", m.AppId);
            cmd.Parameters.AddWithValue("c", m.Code);
            cmd.Parameters.AddWithValue("n", m.Name);
            cmd.Parameters.AddWithValue("ic", m.Icon);
            cmd.Parameters.AddWithValue("o", m.Ord);
            cmd.Parameters.AddWithValue("d", m.Desc);
            await cmd.ExecuteNonQueryAsync();
        }

        var tables = new (string Id, string AppId, string ModId, string Name, string Display, string Desc, string View, int Ord)[]
        {
            ("tbl_hrm_nhanvien", "app_hrm", "mod_hrm_hoso", "hrm_nhan_vien", "Hồ sơ Nhân viên", "Danh sách nhân sự chính thức và thử việc (Master Data)", "table", 1),
            ("tbl_hrm_nghiphep", "app_hrm", "mod_hrm_nghiphep", "hrm_nghi_phep", "Đơn xin Nghỉ phép", "Theo dõi ngày nghỉ và trạng thái duyệt", "table", 1),
            ("tbl_asset_danhmuc", "app_asset", "mod_asset_dm", "asset_danh_muc", "Danh mục Tài sản & Thiết bị", "Quản lý số lượng, đơn giá và tự động tính thành tiền", "table", 1),
            ("tbl_asset_capphat", "app_asset", "mod_asset_capphat", "asset_cap_phat", "Phiếu Cấp phát Thiết bị", "Bàn giao tài sản liên kết nhân viên HRM", "table", 1),
            ("tbl_ops_yeucau", "app_ops", "mod_ops_ticket", "ops_yeu_cau", "Danh sách Yêu cầu Nội bộ", "Quản lý ticket hỗ trợ liên kết nhân viên & tài sản", "table", 1)
        };

        foreach (var t in tables)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_tables (id, app_id, module_id, table_name, display_name, description, primary_view, order_index)
                VALUES (@id, @app, @mod, @tn, @dn, @desc, @pv, @o) ON CONFLICT DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", t.Id);
            cmd.Parameters.AddWithValue("app", t.AppId);
            cmd.Parameters.AddWithValue("mod", t.ModId);
            cmd.Parameters.AddWithValue("tn", t.Name);
            cmd.Parameters.AddWithValue("dn", t.Display);
            cmd.Parameters.AddWithValue("desc", t.Desc);
            cmd.Parameters.AddWithValue("pv", t.View);
            cmd.Parameters.AddWithValue("o", t.Ord);
            await cmd.ExecuteNonQueryAsync();
        }

        var cols = new (string Id, string TblId, string Code, string Name, string Type, bool Req, bool Key, int Width, string? Regex, string? RefCat, string? Formula, string? Def, int Ord)[]
        {
            ("col_nv_ma", "tbl_hrm_nhanvien", "ma_nv", "Mã Nhân viên", "STRING", true, true, 130, "^NV[0-9]{3,6}$", null, null, null, 1),
            ("col_nv_ten", "tbl_hrm_nhanvien", "ho_ten", "Họ và Tên", "STRING", true, false, 200, null, null, null, null, 2),
            ("col_nv_email", "tbl_hrm_nhanvien", "email", "Email Công ty", "EMAIL", true, false, 220, null, null, null, null, 3),
            ("col_nv_sdt", "tbl_hrm_nhanvien", "so_dien_thoai", "Số điện thoại", "TEL", false, false, 140, "^(0|\\+84)[0-9]{9,10}$", null, null, null, 4),
            ("col_nv_pb", "tbl_hrm_nhanvien", "phong_ban", "Phòng ban", "SELECT", true, false, 200, null, "DM_PHONG_BAN", null, null, 5),
            ("col_nv_cv", "tbl_hrm_nhanvien", "chuc_vu", "Chức vụ", "SELECT", true, false, 180, null, "DM_CHUC_VU", null, null, 6),
            ("col_nv_ngay", "tbl_hrm_nhanvien", "ngay_vao_lam", "Ngày vào làm", "DATE", false, false, 140, null, null, null, null, 7),
            ("col_nv_luong", "tbl_hrm_nhanvien", "luong_co_ban", "Lương cơ bản (VNĐ)", "CURRENCY", false, false, 165, null, null, null, null, 8),
            ("col_nv_active", "tbl_hrm_nhanvien", "dang_lam_viec", "Đang làm việc", "BOOLEAN", false, false, 130, null, null, null, "true", 9),

            ("col_np_ma", "tbl_hrm_nghiphep", "ma_don", "Mã Đơn", "STRING", true, true, 120, null, null, null, null, 1),
            ("col_np_nv", "tbl_hrm_nghiphep", "ho_ten_nv", "Nhân viên", "STRING", true, false, 195, null, null, null, null, 2),
            ("col_np_loai", "tbl_hrm_nghiphep", "loai_nghi", "Loại nghỉ", "SELECT", true, false, 170, null, "DM_LOAI_NGHI", null, null, 3),
            ("col_np_tu", "tbl_hrm_nghiphep", "tu_ngay", "Từ ngày", "DATE", true, false, 135, null, null, null, null, 4),
            ("col_np_den", "tbl_hrm_nghiphep", "den_ngay", "Đến ngày", "DATE", true, false, 135, null, null, null, null, 5),
            ("col_np_songay", "tbl_hrm_nghiphep", "so_ngay", "Số ngày nghỉ", "NUMBER", true, false, 120, null, null, null, "1", 6),
            ("col_np_duyet", "tbl_hrm_nghiphep", "trang_thai_duyet", "Trạng thái duyệt", "SELECT", true, false, 160, null, "DM_TRANG_THAI_DUYET", null, "CHO_DUYET", 7),

            ("col_as_ma", "tbl_asset_danhmuc", "ma_ts", "Mã Tài sản", "STRING", true, true, 130, null, null, null, null, 1),
            ("col_as_ten", "tbl_asset_danhmuc", "ten_ts", "Tên Tài sản / Thiết bị", "STRING", true, false, 230, null, null, null, null, 2),
            ("col_as_nhom", "tbl_asset_danhmuc", "nhom_ts", "Nhóm tài sản", "SELECT", true, false, 190, null, "DM_NHOM_TAI_SAN", null, null, 3),
            ("col_as_sl", "tbl_asset_danhmuc", "so_luong", "Số lượng", "NUMBER", true, false, 110, null, null, null, "1", 4),
            ("col_as_dg", "tbl_asset_danhmuc", "don_gia", "Đơn giá (VNĐ)", "CURRENCY", true, false, 160, null, null, null, "0", 5),
            ("col_as_tt", "tbl_asset_danhmuc", "thanh_tien", "Thành tiền (Auto)", "CURRENCY", false, false, 175, null, null, "so_luong * don_gia", null, 6),
            ("col_as_ngay", "tbl_asset_danhmuc", "ngay_mua", "Ngày mua", "DATE", false, false, 135, null, null, null, null, 7),
            ("col_as_st", "tbl_asset_danhmuc", "tinh_trang", "Tình trạng", "SELECT", true, false, 160, null, "DM_TINH_TRANG_TS", null, "DANG_DUNG", 8),

            ("col_cp_ma", "tbl_asset_capphat", "ma_phieu", "Mã Phiếu", "STRING", true, true, 125, null, null, null, null, 1),
            ("col_cp_ts", "tbl_asset_capphat", "ma_ts", "Tài sản cấp phát", "STRING", true, false, 195, null, null, null, null, 2),
            ("col_cp_nguoi", "tbl_asset_capphat", "nguoi_nhan", "Người tiếp nhận", "STRING", true, false, 195, null, null, null, null, 3),
            ("col_cp_pb", "tbl_asset_capphat", "phong_ban", "Phòng ban sử dụng", "SELECT", true, false, 190, null, "DM_PHONG_BAN", null, null, 4),
            ("col_cp_ngay", "tbl_asset_capphat", "ngay_cap", "Ngày bàn giao", "DATE", true, false, 140, null, null, null, null, 5),
            ("col_cp_note", "tbl_asset_capphat", "ghi_chu", "Ghi chú bàn giao", "TEXTAREA", false, false, 220, null, null, null, null, 6),

            ("col_op_ma", "tbl_ops_yeucau", "ma_ticket", "Mã Yêu cầu", "STRING", true, true, 130, null, null, null, null, 1),
            ("col_op_tieude", "tbl_ops_yeucau", "tieu_de", "Tiêu đề yêu cầu", "STRING", true, false, 240, null, null, null, null, 2),
            ("col_op_loai", "tbl_ops_yeucau", "loai_yeu_cau", "Phân loại", "SELECT", true, false, 185, null, "DM_LOAI_YEU_CAU", null, null, 3),
            ("col_op_uutien", "tbl_ops_yeucau", "muc_do_uu_tien", "Mức ưu tiên", "SELECT", true, false, 140, null, "DM_UU_TIEN", null, "MEDIUM", 4),
            ("col_op_nguoi", "tbl_ops_yeucau", "nguoi_tao", "Nhân viên đề xuất", "STRING", true, false, 195, null, null, null, null, 5),
            ("col_op_han", "tbl_ops_yeucau", "han_xu_ly", "Hạn xử lý", "DATE", false, false, 135, null, null, null, null, 7),
            ("col_op_done", "tbl_ops_yeucau", "da_hoan_thanh", "Đã hoàn thành", "BOOLEAN", false, false, 135, null, null, null, "false", 8),
            ("col_op_mota", "tbl_ops_yeucau", "mo_ta", "Mô tả chi tiết", "TEXTAREA", false, false, 240, null, null, null, null, 9)
        };

        foreach (var c in cols)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sys_columns (id, table_id, column_code, column_name, excel_header, data_type, is_required, is_identity_key, width, validation_regex, ref_category_code, formula, default_value, order_index)
                VALUES (@id, @tbl, @code, @name, @name, @dt, @req, @key, @w, @rx, @ref, @fm, @def, @ord) ON CONFLICT DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", c.Id);
            cmd.Parameters.AddWithValue("tbl", c.TblId);
            cmd.Parameters.AddWithValue("code", c.Code);
            cmd.Parameters.AddWithValue("name", c.Name);
            cmd.Parameters.AddWithValue("dt", c.Type);
            cmd.Parameters.AddWithValue("req", c.Req);
            cmd.Parameters.AddWithValue("key", c.Key);
            cmd.Parameters.AddWithValue("w", c.Width);
            cmd.Parameters.AddWithValue("rx", (object?)c.Regex ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ref", (object?)c.RefCat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("fm", (object?)c.Formula ?? DBNull.Value);
            cmd.Parameters.AddWithValue("def", (object?)c.Def ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ord", c.Ord);
            await cmd.ExecuteNonQueryAsync();
        }

        var sampleRecords = new (string Id, string AppId, string ModId, string TblId, string Key, object Data)[]
        {
            ("rec_nv_1", "app_hrm", "mod_hrm_hoso", "tbl_hrm_nhanvien", "NV001", new { ma_nv = "NV001", ho_ten = "Nguyễn Văn An", email = "an.nguyen@company.vn", so_dien_thoai = "0901234567", phong_ban = "TECH", chuc_vu = "LEAD", ngay_vao_lam = "2023-03-15", luong_co_ban = 32000000, dang_lam_viec = true }),
            ("rec_nv_2", "app_hrm", "mod_hrm_hoso", "tbl_hrm_nhanvien", "NV002", new { ma_nv = "NV002", ho_ten = "Trần Thị Minh", email = "minh.tran@company.vn", so_dien_thoai = "0912345678", phong_ban = "HR", chuc_vu = "MGR", ngay_vao_lam = "2022-08-01", luong_co_ban = 28000000, dang_lam_viec = true }),
            ("rec_nv_3", "app_hrm", "mod_hrm_hoso", "tbl_hrm_nhanvien", "NV003", new { ma_nv = "NV003", ho_ten = "Lê Hoàng Nam", email = "nam.le@company.vn", so_dien_thoai = "0987654321", phong_ban = "FIN", chuc_vu = "SPEC", ngay_vao_lam = "2024-01-10", luong_co_ban = 19500000, dang_lam_viec = true }),

            ("rec_np_1", "app_hrm", "mod_hrm_nghiphep", "tbl_hrm_nghiphep", "NP-2026-01", new { ma_don = "NP-2026-01", ho_ten_nv = "NV001", loai_nghi = "PHEP_NAM", tu_ngay = "2026-11-20", den_ngay = "2026-11-22", so_ngay = 3, trang_thai_duyet = "DA_DUYET" }),
            ("rec_np_2", "app_hrm", "mod_hrm_nghiphep", "tbl_hrm_nghiphep", "NP-2026-02", new { ma_don = "NP-2026-02", ho_ten_nv = "NV003", loai_nghi = "CONG_TAC", tu_ngay = "2026-11-25", den_ngay = "2026-11-26", so_ngay = 2, trang_thai_duyet = "CHO_DUYET" }),

            ("rec_as_1", "app_asset", "mod_asset_dm", "tbl_asset_danhmuc", "TS-LAP-01", new { ma_ts = "TS-LAP-01", ten_ts = "MacBook Pro 14 M4 Pro", nhom_ts = "LAPTOP", so_luong = 5, don_gia = 48000000, thanh_tien = 240000000, ngay_mua = "2026-02-14", tinh_trang = "DANG_DUNG" }),
            ("rec_as_2", "app_asset", "mod_asset_dm", "tbl_asset_danhmuc", "TS-SRV-01", new { ma_ts = "TS-SRV-01", ten_ts = "Dell PowerEdge R750xs", nhom_ts = "SERVER", so_luong = 2, don_gia = 115000000, thanh_tien = 230000000, ngay_mua = "2026-05-09", tinh_trang = "DANG_DUNG" }),
            ("rec_as_3", "app_asset", "mod_asset_dm", "tbl_asset_danhmuc", "TS-LIC-01", new { ma_ts = "TS-LIC-01", ten_ts = "JetBrains All Products Pack", nhom_ts = "LICENSE", so_luong = 15, don_gia = 6500000, thanh_tien = 97500000, ngay_mua = "2026-01-05", tinh_trang = "DANG_DUNG" }),

            ("rec_cp_1", "app_asset", "mod_asset_capphat", "tbl_asset_capphat", "CP-001", new { ma_phieu = "CP-001", ma_ts = "TS-LAP-01", nguoi_nhan = "NV001", phong_ban = "TECH", ngay_cap = "2026-02-20", ghi_chu = "Bàn giao máy trạm phát triển hệ thống Rust/C#" }),

            ("rec_op_1", "app_ops", "mod_ops_ticket", "tbl_ops_yeucau", "REQ-101", new { ma_ticket = "REQ-101", tieu_de = "Cấp quyền VPN & PostgreSQL Production", loai_yeu_cau = "IT_SUPPORT", muc_do_uu_tien = "HIGH", nguoi_tao = "NV001", ma_ts = "TS-SRV-01", han_xu_ly = "2026-11-18", da_hoan_thanh = false, mo_ta = "Cần mở quyền truy cập DB staging để kiểm thử module mới." }),
            ("rec_op_2", "app_ops", "mod_ops_ticket", "tbl_ops_yeucau", "REQ-102", new { ma_ticket = "REQ-102", tieu_de = "Đặt mua màn hình 27 inch cho nhân sự mới", loai_yeu_cau = "HANH_CHINH", muc_do_uu_tien = "MEDIUM", nguoi_tao = "NV002", han_xu_ly = "2026-11-22", da_hoan_thanh = true, mo_ta = "Đã duyệt ngân sách Q4." })
        };

        foreach (var r in sampleRecords)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO app_records (id, app_id, module_id, table_id, record_key, data, status)
                VALUES (@id, @app, @mod, @tbl, @key, @data::jsonb, 'ACTIVE') ON CONFLICT DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", r.Id);
            cmd.Parameters.AddWithValue("app", r.AppId);
            cmd.Parameters.AddWithValue("mod", r.ModId);
            cmd.Parameters.AddWithValue("tbl", r.TblId);
            cmd.Parameters.AddWithValue("key", r.Key);
            cmd.Parameters.AddWithValue("data", JsonSerializer.Serialize(r.Data));
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
