-- PostgreSQL 15+. Modelo proposto; o protótipo atual NÃO executa este arquivo.
-- Criar migrações e persistência antes de uso em produção.

CREATE TABLE admin_tenants (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE admin_departments (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    external_group_id TEXT,
    name TEXT NOT NULL,
    UNIQUE (tenant_id, external_group_id)
);

CREATE TABLE admin_users (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    issuer TEXT NOT NULL,
    subject TEXT NOT NULL,
    display_name TEXT NOT NULL,
    admin_role TEXT NOT NULL CHECK (admin_role IN ('Reader', 'PolicyEditor', 'Admin')),
    UNIQUE (tenant_id, issuer, subject)
);

CREATE TABLE browser_policy_drafts (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    scope_kind TEXT NOT NULL CHECK (scope_kind IN ('global', 'department')),
    department_id UUID REFERENCES admin_departments(id),
    name TEXT NOT NULL,
    revision INTEGER NOT NULL DEFAULT 0 CHECK (revision >= 0),
    allowed_urls JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(allowed_urls) = 'array'),
    blocked_urls JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(blocked_urls) = 'array'),
    extensions JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(extensions) = 'array'),
    dlp JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(dlp) = 'object'),
    branding JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(branding) = 'object'),
    updated_by UUID REFERENCES admin_users(id),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK ((scope_kind = 'global' AND department_id IS NULL) OR
           (scope_kind = 'department' AND department_id IS NOT NULL))
);
CREATE UNIQUE INDEX browser_policy_global_unique ON browser_policy_drafts (tenant_id)
    WHERE scope_kind = 'global';
CREATE UNIQUE INDEX browser_policy_department_unique ON browser_policy_drafts (tenant_id, department_id)
    WHERE scope_kind = 'department';

-- Versões assinadas/publicadas só poderão ser usadas quando existir distribuição segura.
CREATE TABLE browser_policy_versions (
    id UUID PRIMARY KEY,
    draft_id UUID NOT NULL REFERENCES browser_policy_drafts(id),
    version INTEGER NOT NULL CHECK (version > 0),
    snapshot JSONB NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
    published_by UUID NOT NULL REFERENCES admin_users(id),
    published_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (draft_id, version)
);

CREATE TABLE managed_devices (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    department_id UUID REFERENCES admin_departments(id),
    browser_version TEXT NOT NULL,
    last_seen_at TIMESTAMPTZ,
    enrolled_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE policy_delivery (
    device_id UUID NOT NULL REFERENCES managed_devices(id),
    policy_version_id UUID NOT NULL REFERENCES browser_policy_versions(id),
    state TEXT NOT NULL CHECK (state IN ('queued', 'received', 'applied', 'failed')),
    reported_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    error_code TEXT,
    PRIMARY KEY (device_id, policy_version_id)
);

CREATE TABLE device_health_samples (
    device_id UUID NOT NULL REFERENCES managed_devices(id),
    recorded_at TIMESTAMPTZ NOT NULL,
    cpu_percent DOUBLE PRECISION NOT NULL CHECK (cpu_percent BETWEEN 0 AND 100),
    browser_working_set_bytes BIGINT NOT NULL CHECK (browser_working_set_bytes >= 0),
    PRIMARY KEY (device_id, recorded_at)
);

CREATE TABLE security_block_events (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    device_id UUID NOT NULL REFERENCES managed_devices(id),
    rule_id TEXT NOT NULL,
    destination_host TEXT NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX security_block_tenant_time ON security_block_events (tenant_id, occurred_at DESC);

CREATE TABLE admin_audit_events (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES admin_tenants(id),
    actor_id UUID REFERENCES admin_users(id),
    action TEXT NOT NULL,
    entity_id UUID,
    previous_revision INTEGER,
    new_revision INTEGER,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX admin_audit_tenant_time ON admin_audit_events (tenant_id, occurred_at DESC);
