-- Add all missing columns to object_versions table
ALTER TABLE registry.object_versions ADD COLUMN IF NOT EXISTS "MigrationUpSql" TEXT;
ALTER TABLE registry.object_versions ADD COLUMN IF NOT EXISTS "MigrationDownSql" TEXT;
