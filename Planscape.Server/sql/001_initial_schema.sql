-- ============================================================================
-- StingBIM Server — Initial PostgreSQL Schema
-- Version: 001
-- Generated: 2026-04-06
-- Compatible: PostgreSQL 15+
--
-- Apply with: psql -h HOST -U stingbim -d stingbim -f 001_initial_schema.sql
-- Or via Docker: docker exec -i stingbim-postgres psql -U stingbim -d stingbim < 001_initial_schema.sql
-- ============================================================================

BEGIN;

-- ── Extensions ──────────────────────────────────────────────────────────────
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";  -- For LIKE/ILIKE index optimisation

-- ── Enums ───────────────────────────────────────────────────────────────────
CREATE TYPE license_tier   AS ENUM ('Starter', 'Professional', 'Premium', 'Enterprise');
CREATE TYPE mim_tier       AS ENUM ('None', 'MimStarter', 'MimProfessional', 'MimEnterprise');
CREATE TYPE user_role      AS ENUM ('Viewer', 'Contributor', 'Coordinator', 'Manager', 'Admin', 'Owner');
CREATE TYPE project_status AS ENUM ('Active', 'Archived', 'Handed_Over');
CREATE TYPE task_type      AS ENUM ('Preventive', 'Corrective', 'Condition', 'Statutory', 'Emergency');
CREATE TYPE task_status_e  AS ENUM ('Scheduled', 'InProgress', 'Completed', 'Overdue', 'Cancelled');

-- ── Tenants ─────────────────────────────────────────────────────────────────
CREATE TABLE tenants (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name                VARCHAR(200) NOT NULL,
    slug                VARCHAR(50)  NOT NULL UNIQUE,
    contact_email       VARCHAR(256),
    tier                license_tier NOT NULL DEFAULT 'Starter',
    mim_enabled         BOOLEAN      NOT NULL DEFAULT FALSE,
    mim_tier            mim_tier     NOT NULL DEFAULT 'None',
    max_users           INT          NOT NULL DEFAULT 5,
    max_projects        INT          NOT NULL DEFAULT 1,
    storage_limit_bytes BIGINT       NOT NULL DEFAULT 524288000,  -- 500MB
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    trial_expires_at    TIMESTAMPTZ,
    stripe_customer_id  VARCHAR(64),
    stripe_sub_id       VARCHAR(64)
);

CREATE INDEX idx_tenants_slug ON tenants (slug);

-- ── Users ────────────────────────────────────────────────────────────────────
CREATE TABLE app_users (
    id                       UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                UUID         NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    email                    VARCHAR(256) NOT NULL UNIQUE,
    display_name             VARCHAR(200) NOT NULL,
    password_hash            TEXT         NOT NULL,
    role                     user_role    NOT NULL DEFAULT 'Contributor',
    iso19650_role            VARCHAR(10)  NOT NULL DEFAULT 'M',
    is_active                BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_login_at            TIMESTAMPTZ,
    refresh_token            TEXT,
    refresh_token_expires_at TIMESTAMPTZ
);

CREATE INDEX idx_users_tenant   ON app_users (tenant_id);
CREATE INDEX idx_users_email    ON app_users (email);

-- ── License Keys ─────────────────────────────────────────────────────────────
CREATE TABLE license_keys (
    id                   UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id            UUID         NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    key                  VARCHAR(64)  NOT NULL UNIQUE,
    tier                 license_tier NOT NULL DEFAULT 'Starter',
    mim_enabled          BOOLEAN      NOT NULL DEFAULT FALSE,
    is_active            BOOLEAN      NOT NULL DEFAULT TRUE,
    max_activations      INT          NOT NULL DEFAULT 1,
    current_activations  INT          NOT NULL DEFAULT 0,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    expires_at           TIMESTAMPTZ,
    activated_machine_ids JSONB,
    last_activated_by    VARCHAR(256),
    last_activated_at    TIMESTAMPTZ
);

-- ── Projects ─────────────────────────────────────────────────────────────────
CREATE TABLE projects (
    id                           UUID           PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                    UUID           NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name                         VARCHAR(200)   NOT NULL,
    code                         VARCHAR(50)    NOT NULL,
    description                  TEXT,
    phase                        VARCHAR(100)   DEFAULT 'Design',
    status                       project_status NOT NULL DEFAULT 'Active',
    created_at                   TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
    last_sync_at                 TIMESTAMPTZ,
    -- Tag config
    tag_separator                VARCHAR(5)     DEFAULT '-',
    seq_num_pad                  INT            DEFAULT 4,
    tag_prefix                   VARCHAR(20),
    tag_suffix                   VARCHAR(20),
    config_json                  JSONB,
    -- Cached compliance metrics (updated on each sync)
    compliance_percent           DOUBLE PRECISION DEFAULT 0,
    container_compliance_percent DOUBLE PRECISION DEFAULT 0,
    total_elements               INT            DEFAULT 0,
    tagged_elements              INT            DEFAULT 0,
    warning_count                INT            DEFAULT 0,
    rag_status                   VARCHAR(10)    DEFAULT 'RED'
);

CREATE UNIQUE INDEX idx_projects_tenant_code ON projects (tenant_id, code);
CREATE INDEX idx_projects_tenant_status ON projects (tenant_id, status);

-- ── Project Members ───────────────────────────────────────────────────────────
CREATE TABLE project_members (
    id            UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id    UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id       UUID        NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    project_role  VARCHAR(50) NOT NULL DEFAULT 'Contributor',
    iso19650_role VARCHAR(10) NOT NULL DEFAULT 'M',
    is_active     BOOLEAN     NOT NULL DEFAULT TRUE,
    joined_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    invited_by    VARCHAR(256)
);

CREATE UNIQUE INDEX idx_project_members_proj_user ON project_members (project_id, user_id);
CREATE INDEX idx_project_members_user ON project_members (user_id);

-- ── Tagged Elements ───────────────────────────────────────────────────────────
CREATE TABLE tagged_elements (
    id                UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id        UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    revit_element_id  BIGINT      NOT NULL,
    unique_id         VARCHAR(100),
    -- STING tag tokens
    disc              VARCHAR(10),
    loc               VARCHAR(20),
    zone              VARCHAR(20),
    lvl               VARCHAR(10),
    sys               VARCHAR(20),
    func              VARCHAR(20),
    prod              VARCHAR(20),
    seq               VARCHAR(10),
    -- Assembled tags
    tag1              VARCHAR(100),
    tag7              VARCHAR(200),
    tag7a             VARCHAR(100),
    tag7b             VARCHAR(100),
    tag7c             VARCHAR(100),
    tag7d             VARCHAR(100),
    tag7e             VARCHAR(100),
    tag7f             VARCHAR(100),
    -- Context
    category_name     VARCHAR(100),
    family_name       VARCHAR(100),
    type_name         VARCHAR(100),
    status            VARCHAR(50),
    rev               VARCHAR(20),
    grid_ref          VARCHAR(50),
    room_name         VARCHAR(100),
    level             VARCHAR(50),
    -- Compliance flags
    is_stale          BOOLEAN DEFAULT FALSE,
    is_complete       BOOLEAN DEFAULT FALSE,
    is_fully_resolved BOOLEAN DEFAULT FALSE,
    validation_errors TEXT,
    -- Audit
    previous_tag      VARCHAR(100),
    tag_modified_at   TIMESTAMPTZ,
    synced_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    synced_by         VARCHAR(256)
);

CREATE UNIQUE INDEX idx_elements_proj_revitid ON tagged_elements (project_id, revit_element_id);
CREATE INDEX idx_elements_tag1   ON tagged_elements (tag1)   WHERE tag1 IS NOT NULL;
CREATE INDEX idx_elements_disc   ON tagged_elements (project_id, disc);
CREATE INDEX idx_elements_stale  ON tagged_elements (project_id, is_stale) WHERE is_stale = TRUE;
CREATE INDEX idx_elements_synced ON tagged_elements (synced_at);

-- ── BIM Issues ────────────────────────────────────────────────────────────────
CREATE TABLE bim_issues (
    id                  UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id          UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    issue_code          VARCHAR(30) NOT NULL,
    type                VARCHAR(20) NOT NULL DEFAULT 'RFI',  -- RFI/NCR/SI/TQ/CLASH/SAFETY
    title               VARCHAR(500) NOT NULL,
    description         TEXT,
    priority            VARCHAR(20) NOT NULL DEFAULT 'MEDIUM',
    status              VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    assignee            VARCHAR(256),
    created_by          VARCHAR(256) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    due_date            TIMESTAMPTZ,
    resolved_at         TIMESTAMPTZ,
    discipline          VARCHAR(10),
    revision            VARCHAR(20),
    linked_element_ids  JSONB,
    bcf_guid            VARCHAR(36)
);

CREATE UNIQUE INDEX idx_issues_proj_code ON bim_issues (project_id, issue_code);
CREATE INDEX idx_issues_status      ON bim_issues (project_id, status);
CREATE INDEX idx_issues_due         ON bim_issues (due_date) WHERE status != 'CLOSED';
CREATE INDEX idx_issues_assignee    ON bim_issues (assignee) WHERE assignee IS NOT NULL;

-- ── Documents ─────────────────────────────────────────────────────────────────
CREATE TABLE document_records (
    id               UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id       UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    file_name        VARCHAR(500) NOT NULL,
    file_path        TEXT,
    document_type    VARCHAR(10),   -- DR / SH / SP / CA etc.
    cde_status       VARCHAR(20) NOT NULL DEFAULT 'WIP',
    suitability_code VARCHAR(5),
    revision         VARCHAR(20),
    discipline       VARCHAR(10),
    file_size_bytes  BIGINT      DEFAULT 0,
    content_hash     VARCHAR(64),   -- SHA-256
    uploaded_by      VARCHAR(256) NOT NULL,
    uploaded_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status_history   JSONB
);

CREATE INDEX idx_docs_proj_status ON document_records (project_id, cde_status);
CREATE INDEX idx_docs_revision    ON document_records (project_id, revision);

-- ── Transmittals ──────────────────────────────────────────────────────────────
CREATE TABLE transmittals (
    id               UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id       UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    transmittal_code VARCHAR(50) NOT NULL,
    recipient        VARCHAR(256),
    status           VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    notes            TEXT,
    document_ids     JSONB,
    created_by       VARCHAR(256) NOT NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at          TIMESTAMPTZ
);

CREATE UNIQUE INDEX idx_transmittals_proj_code ON transmittals (project_id, transmittal_code);

-- ── Meetings ──────────────────────────────────────────────────────────────────
CREATE TABLE meetings (
    id            UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id    UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    title         VARCHAR(300) NOT NULL,
    meeting_type  VARCHAR(100),
    scheduled_at  TIMESTAMPTZ NOT NULL,
    minutes       TEXT,
    agenda        JSONB,
    attendees     JSONB,
    created_by    VARCHAR(256) NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_meetings_proj_scheduled ON meetings (project_id, scheduled_at);

-- ── Meeting Action Items ──────────────────────────────────────────────────────
CREATE TABLE meeting_action_items (
    id               UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    meeting_id       UUID        NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    description      TEXT        NOT NULL,
    assignee         VARCHAR(256),
    due_date         TIMESTAMPTZ,
    status           VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    linked_issue_id  UUID        REFERENCES bim_issues(id) ON DELETE SET NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_actions_meeting  ON meeting_action_items (meeting_id);
CREATE INDEX idx_actions_assignee ON meeting_action_items (assignee) WHERE assignee IS NOT NULL;
CREATE INDEX idx_actions_status   ON meeting_action_items (status) WHERE status != 'COMPLETE';

-- ── Seq Counters ──────────────────────────────────────────────────────────────
CREATE TABLE seq_counters (
    id            UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id    UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    counter_key   VARCHAR(100) NOT NULL,
    current_value INT          NOT NULL DEFAULT 0,
    updated_by    VARCHAR(256),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_seq_proj_key ON seq_counters (project_id, counter_key);

-- ── Compliance Snapshots ──────────────────────────────────────────────────────
CREATE TABLE compliance_snapshots (
    id                   UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id           UUID          NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    captured_by          VARCHAR(256)  NOT NULL,
    captured_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    total_elements       INT           NOT NULL DEFAULT 0,
    tagged_complete      INT           NOT NULL DEFAULT 0,
    tagged_incomplete    INT           NOT NULL DEFAULT 0,
    untagged             INT           NOT NULL DEFAULT 0,
    fully_resolved       INT           NOT NULL DEFAULT 0,
    stale_count          INT           NOT NULL DEFAULT 0,
    placeholder_count    INT           NOT NULL DEFAULT 0,
    warning_count        INT           NOT NULL DEFAULT 0,
    warning_health_score DOUBLE PRECISION DEFAULT 100,
    tag_percent          DOUBLE PRECISION DEFAULT 0,
    strict_percent       DOUBLE PRECISION DEFAULT 0,
    container_percent    DOUBLE PRECISION DEFAULT 0,
    rag_status           VARCHAR(10)   NOT NULL DEFAULT 'RED',
    by_discipline_json   JSONB,
    by_phase_json        JSONB,
    empty_token_counts   JSONB
);

CREATE INDEX idx_snapshots_proj_time ON compliance_snapshots (project_id, captured_at);

-- ── Workflow Runs ─────────────────────────────────────────────────────────────
CREATE TABLE workflow_runs (
    id                UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id        UUID        NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    preset_name       VARCHAR(100) NOT NULL,
    user_name         VARCHAR(256),
    steps_passed      INT         NOT NULL DEFAULT 0,
    steps_failed      INT         NOT NULL DEFAULT 0,
    steps_skipped     INT         NOT NULL DEFAULT 0,
    duration_ms       INT         NOT NULL DEFAULT 0,
    compliance_before DOUBLE PRECISION DEFAULT 0,
    compliance_after  DOUBLE PRECISION DEFAULT 0,
    step_results_json JSONB,
    executed_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_workflows_proj_time ON workflow_runs (project_id, executed_at);

-- ── Audit Log ─────────────────────────────────────────────────────────────────
CREATE TABLE audit_logs (
    id           UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    UUID        NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    project_id   UUID        REFERENCES projects(id) ON DELETE SET NULL,
    user_id      UUID        REFERENCES app_users(id) ON DELETE SET NULL,
    action       VARCHAR(100) NOT NULL,
    entity_type  VARCHAR(100),
    entity_id    VARCHAR(100),
    details_json JSONB,
    ip_address   VARCHAR(50),
    timestamp    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_tenant_time  ON audit_logs (tenant_id, timestamp DESC);
CREATE INDEX idx_audit_project_time ON audit_logs (project_id, timestamp DESC) WHERE project_id IS NOT NULL;

-- ── StingMIM Assets ───────────────────────────────────────────────────────────
CREATE TABLE mim_assets (
    id                       UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id               UUID          NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    tagged_element_id        UUID          REFERENCES tagged_elements(id) ON DELETE SET NULL,
    asset_tag                VARCHAR(100)  NOT NULL,
    asset_name               VARCHAR(300),
    manufacturer             VARCHAR(200),
    model_number             VARCHAR(100),
    serial_number            VARCHAR(100),
    bar_code                 VARCHAR(100),
    -- Classification
    category_name            VARCHAR(100),
    family_name              VARCHAR(100),
    uniclass_code            VARCHAR(30),
    omni_class_code          VARCHAR(30),
    cobie_type               VARCHAR(100),
    cobie_space              VARCHAR(100),
    -- STING tokens
    discipline               VARCHAR(10),
    system_code              VARCHAR(20),
    function_code            VARCHAR(20),
    product_code             VARCHAR(20),
    location                 VARCHAR(50),
    level                    VARCHAR(20),
    lifecycle_status         VARCHAR(30)   NOT NULL DEFAULT 'OPERATIONAL',
    criticality_rating       VARCHAR(20),
    -- Lifecycle (ISO 15686)
    installation_date        DATE,
    commissioning_date       DATE,
    expected_life_years      INT,
    expected_replacement_date DATE,
    condition_grade          VARCHAR(1)    DEFAULT 'A',
    condition_score          DOUBLE PRECISION,
    -- Warranty
    warranty_provider        VARCHAR(200),
    warranty_start           DATE,
    warranty_end             DATE,
    warranty_duration_months INT,
    -- Cost
    capital_cost             DECIMAL(14,2),
    replacement_cost         DECIMAL(14,2),
    annual_maintenance_cost  DECIMAL(14,2),
    cost_currency            VARCHAR(5)    DEFAULT 'GBP',
    -- Spatial
    building                 VARCHAR(100),
    floor                    VARCHAR(50),
    room                     VARCHAR(100),
    zone                     VARCHAR(50),
    -- IoT / Digital Twin
    sensor_id                VARCHAR(100),
    digital_twin_id          VARCHAR(100),
    last_sensor_reading      TIMESTAMPTZ,
    sensor_data_json         JSONB,
    -- Energy
    energy_consumption_kwh   DOUBLE PRECISION,
    embodied_carbon_kg_co2   DOUBLE PRECISION,
    -- Metadata
    document_refs_json       JSONB,
    spare_parts_json         JSONB,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at               TIMESTAMPTZ
);

CREATE UNIQUE INDEX idx_assets_proj_tag ON mim_assets (project_id, asset_tag);
CREATE INDEX idx_assets_lifecycle ON mim_assets (lifecycle_status);
CREATE INDEX idx_assets_warranty  ON mim_assets (warranty_end) WHERE warranty_end IS NOT NULL;

-- ── StingMIM Maintenance Tasks ────────────────────────────────────────────────
CREATE TABLE mim_maintenance_tasks (
    id                   UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    asset_id             UUID          NOT NULL REFERENCES mim_assets(id) ON DELETE CASCADE,
    task_code            VARCHAR(50),
    title                VARCHAR(300)  NOT NULL,
    description          TEXT,
    type                 task_type     NOT NULL DEFAULT 'Preventive',
    priority             VARCHAR(20)   NOT NULL DEFAULT 'MEDIUM',
    status               task_status_e NOT NULL DEFAULT 'Scheduled',
    assigned_to          VARCHAR(256),
    frequency_days       INT,
    scheduled_date       DATE,
    completed_date       DATE,
    next_due_date        DATE,
    standard_reference   VARCHAR(100),
    is_statutory         BOOLEAN       NOT NULL DEFAULT FALSE,
    regulatory_body      VARCHAR(100),
    estimated_cost       DECIMAL(14,2),
    actual_cost          DECIMAL(14,2),
    estimated_hours      DOUBLE PRECISION,
    actual_hours         DOUBLE PRECISION,
    created_at           TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_tasks_asset     ON mim_maintenance_tasks (asset_id);
CREATE INDEX idx_tasks_due       ON mim_maintenance_tasks (next_due_date) WHERE status != 'Completed';
CREATE INDEX idx_tasks_statutory ON mim_maintenance_tasks (is_statutory) WHERE is_statutory = TRUE;

-- ── Schema version tracking ───────────────────────────────────────────────────
CREATE TABLE schema_versions (
    version     INT         PRIMARY KEY,
    description VARCHAR(200) NOT NULL,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO schema_versions (version, description) VALUES (1, 'Initial schema — all core tables');

COMMIT;

-- ============================================================================
-- Usage Notes:
-- 1. This schema is auto-applied by EF Core's EnsureCreated() in development.
-- 2. For production, run this script once before starting the API.
-- 3. For Render.com: run via the Render Shell after first deploy.
-- 4. The API uses EF Core entity names — EF maps PascalCase properties to
--    snake_case columns via Npgsql's naming convention.
-- ============================================================================
