-- Add all missing columns to breaking_changes table
ALTER TABLE registry.breaking_changes ADD COLUMN IF NOT EXISTS "Category" VARCHAR(20) NOT NULL DEFAULT 'Major';
ALTER TABLE registry.breaking_changes ADD COLUMN IF NOT EXISTS "ImpactAnalysis" TEXT;
ALTER TABLE registry.breaking_changes ADD COLUMN IF NOT EXISTS "SuggestedAction" TEXT;
ALTER TABLE registry.breaking_changes ADD COLUMN IF NOT EXISTS "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW();
