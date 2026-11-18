-- SmartApps Multi-App Dynamic Enterprise Platform Schema (PostgreSQL 16+)
-- Supports Cross-App Lookups, Automations, RBAC, Audit Trail, Dynamic Tables & JSONB Records

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
    approval_enabled BOOLEAN DEFAULT FALSE,
    approval_field VARCHAR(128) DEFAULT 'trang_thai_duyet',
    approver_roles JSONB NOT NULL DEFAULT '["ADMIN","HR_MANAGER","ASSET_LEAD"]'::jsonb,
    public_form_enabled BOOLEAN DEFAULT FALSE,
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
    order_index INT DEFAULT 1,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(table_id, column_code)
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
    sso_provider VARCHAR(64),
    sso_subject VARCHAR(255),
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

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
    workflow_snapshot JSONB,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(128),
    validation_errors JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_by VARCHAR(128) DEFAULT 'system',
    updated_by VARCHAR(128) DEFAULT 'system',
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

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

CREATE TABLE IF NOT EXISTS sys_notifications (
    id VARCHAR(64) PRIMARY KEY,
    recipient_username VARCHAR(64) DEFAULT '*',
    title VARCHAR(255) NOT NULL,
    message TEXT,
    app_code VARCHAR(64),
    record_id VARCHAR(64),
    category VARCHAR(32) DEFAULT 'SYSTEM',
    link_url VARCHAR(512),
    sender_username VARCHAR(64),
    priority VARCHAR(16) DEFAULT 'NORMAL',
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_read BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

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
    updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS sys_system_settings (
    setting_key VARCHAR(128) PRIMARY KEY,
    setting_value JSONB NOT NULL DEFAULT '{}'::jsonb,
    description TEXT,
    updated_by VARCHAR(128),
    updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

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

CREATE INDEX IF NOT EXISTS idx_app_comments_rec ON app_record_comments(record_id, created_at ASC);

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
CREATE INDEX IF NOT EXISTS idx_sys_noti_rec ON sys_notifications(recipient_username, is_read, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_chat_msg_ch ON chat_messages(channel_id, created_at ASC);
CREATE INDEX IF NOT EXISTS idx_chat_msg_history ON chat_messages(channel_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_chat_mem_user ON chat_channel_members(username);
CREATE INDEX IF NOT EXISTS idx_app_tasks_assignee ON app_tasks(assignee_username, status);
CREATE INDEX IF NOT EXISTS idx_app_tasks_channel ON app_tasks(channel_id, created_at DESC);

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
