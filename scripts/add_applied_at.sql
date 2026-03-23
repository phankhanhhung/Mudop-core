-- Add missing column to object_versions table
ALTER TABLE registry.object_versions ADD COLUMN IF NOT EXISTS "AppliedAt" TIMESTAMPTZ;
