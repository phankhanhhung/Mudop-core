-- Add missing columns to upgrade_windows table
ALTER TABLE registry.upgrade_windows ADD COLUMN IF NOT EXISTS "V1ReadonlyAfter" TIMESTAMPTZ;
ALTER TABLE registry.upgrade_windows ADD COLUMN IF NOT EXISTS "V2PrimaryAfter" TIMESTAMPTZ;
ALTER TABLE registry.upgrade_windows ADD COLUMN IF NOT EXISTS "V1CleanupAfter" TIMESTAMPTZ;
ALTER TABLE registry.upgrade_windows ADD COLUMN IF NOT EXISTS "RollbackAvailableUntil" TIMESTAMPTZ;
ALTER TABLE registry.upgrade_windows ADD COLUMN IF NOT EXISTS "RollbackReason" TEXT;
