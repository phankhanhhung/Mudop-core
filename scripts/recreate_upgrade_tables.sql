-- Drop and recreate upgrade tables with all columns

DROP TABLE IF EXISTS registry.upgrade_sync_statuses CASCADE;
DROP TABLE IF EXISTS registry.upgrade_windows CASCADE;

-- Upgrade Windows table (full)
CREATE TABLE registry.upgrade_windows (
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
    "V1ReadonlyAfter" TIMESTAMPTZ,
    "V2PrimaryAfter" TIMESTAMPTZ,
    "V1CleanupAfter" TIMESTAMPTZ,
    "RollbackAvailableUntil" TIMESTAMPTZ,
    "RollbackReason" TEXT,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "CreatedBy" VARCHAR(255),
    FOREIGN KEY ("TenantId") REFERENCES registry.tenants("Id") ON DELETE CASCADE,
    FOREIGN KEY ("ModuleId") REFERENCES registry.modules("Id") ON DELETE CASCADE
);

-- Upgrade Sync Statuses table (full)  
CREATE TABLE registry.upgrade_sync_statuses (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "WindowId" UUID NOT NULL,
    "EntityName" VARCHAR(255) NOT NULL,
    "TotalRecords" BIGINT NOT NULL DEFAULT 0,
    "MigratedRecords" BIGINT NOT NULL DEFAULT 0,
    "SyncErrors" BIGINT NOT NULL DEFAULT 0,
    "MigrationStartedAt" TIMESTAMPTZ,
    "MigrationCompletedAt" TIMESTAMPTZ,
    "LastSyncAt" TIMESTAMPTZ,
    "PendingDeltas" BIGINT NOT NULL DEFAULT 0,
    "DeltaLagSeconds" INT,
    "Phase" VARCHAR(20) NOT NULL DEFAULT 'Pending',
    "LastError" TEXT,
    "SyncTriggerSql" TEXT,
    "IsSyncTriggerActive" BOOLEAN NOT NULL DEFAULT FALSE,
    FOREIGN KEY ("WindowId") REFERENCES registry.upgrade_windows("Id") ON DELETE CASCADE,
    UNIQUE ("WindowId", "EntityName")
);

-- Indexes
CREATE INDEX idx_upgrade_windows_tenant ON registry.upgrade_windows("TenantId", "ModuleId", "Status");
CREATE INDEX idx_upgrade_sync_phase ON registry.upgrade_sync_statuses("Phase");
