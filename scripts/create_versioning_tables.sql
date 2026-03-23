-- Versioning tables for BMMDL Registry
-- Creates object_versions, breaking_changes, upgrade_windows, upgrade_sync_statuses

-- Object Versions table
CREATE TABLE IF NOT EXISTS registry.object_versions (
    "Id" UUID PRIMARY KEY,
    "TenantId" UUID NOT NULL,
    "ModuleId" UUID NOT NULL,
    "ObjectType" VARCHAR(50) NOT NULL,
    "ObjectName" VARCHAR(255) NOT NULL,
    "Version" VARCHAR(20) NOT NULL,
    "VersionMajor" INT NOT NULL DEFAULT 1,
    "VersionMinor" INT NOT NULL DEFAULT 0,
    "VersionPatch" INT NOT NULL DEFAULT 0,
    "DefinitionHash" VARCHAR(64) NOT NULL,
    "DefinitionSnapshot" JSONB,
    "ChangeCategory" VARCHAR(20),
    "IsBreaking" BOOLEAN NOT NULL DEFAULT FALSE,
    "ChangeDescription" TEXT,
    "Status" VARCHAR(20) NOT NULL DEFAULT 'Draft',
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "CreatedBy" VARCHAR(255),
    "ApprovedAt" TIMESTAMPTZ,
    "ApprovedBy" VARCHAR(255),
    FOREIGN KEY ("TenantId") REFERENCES registry.tenants("Id") ON DELETE CASCADE,
    FOREIGN KEY ("ModuleId") REFERENCES registry.modules("Id") ON DELETE CASCADE
);

-- Breaking Changes table
CREATE TABLE IF NOT EXISTS registry.breaking_changes (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "ObjectVersionId" UUID NOT NULL,
    "ChangeType" VARCHAR(50) NOT NULL,
    "TargetName" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "OldValue" TEXT,
    "NewValue" TEXT,
    "Status" VARCHAR(20) NOT NULL DEFAULT 'Pending',
    "ReviewedAt" TIMESTAMPTZ,
    "ReviewedBy" VARCHAR(255),
    "ReviewNotes" TEXT,
    FOREIGN KEY ("ObjectVersionId") REFERENCES registry.object_versions("Id") ON DELETE CASCADE
);

-- Upgrade Windows table
CREATE TABLE IF NOT EXISTS registry.upgrade_windows (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ModuleId" UUID NOT NULL,
    "FromVersion" VARCHAR(20) NOT NULL,
    "ToVersion" VARCHAR(20) NOT NULL,
    "Status" VARCHAR(20) NOT NULL DEFAULT 'Scheduled',
    "ScheduledStart" TIMESTAMPTZ,
    "ScheduledEnd" TIMESTAMPTZ,
    "ActualStart" TIMESTAMPTZ,
    "ActualEnd" TIMESTAMPTZ,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "CreatedBy" VARCHAR(255),
    "ErrorMessage" TEXT,
    FOREIGN KEY ("TenantId") REFERENCES registry.tenants("Id") ON DELETE CASCADE,
    FOREIGN KEY ("ModuleId") REFERENCES registry.modules("Id") ON DELETE CASCADE
);

-- Upgrade Sync Statuses table
CREATE TABLE IF NOT EXISTS registry.upgrade_sync_statuses (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "WindowId" UUID NOT NULL,
    "EntityName" VARCHAR(255) NOT NULL,
    "Phase" VARCHAR(20) NOT NULL DEFAULT 'NotStarted',
    "TotalRecords" BIGINT NOT NULL DEFAULT 0,
    "MigratedRecords" BIGINT NOT NULL DEFAULT 0,
    "SyncErrors" BIGINT NOT NULL DEFAULT 0,
    "LastSyncAt" TIMESTAMPTZ,
    "ErrorDetails" TEXT,
    FOREIGN KEY ("WindowId") REFERENCES registry.upgrade_windows("Id") ON DELETE CASCADE,
    UNIQUE ("WindowId", "EntityName")
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_object_versions_tenant_module ON registry.object_versions("TenantId", "ModuleId", "ObjectType", "ObjectName");
CREATE INDEX IF NOT EXISTS idx_object_versions_hash ON registry.object_versions("DefinitionHash");
CREATE INDEX IF NOT EXISTS idx_breaking_changes_status ON registry.breaking_changes("ObjectVersionId", "Status");
CREATE INDEX IF NOT EXISTS idx_upgrade_windows_tenant ON registry.upgrade_windows("TenantId", "ModuleId", "Status");
