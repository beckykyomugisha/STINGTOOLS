-- ─────────────────────────────────────────────────────────────────────
--  Dev-database schema patch
--  Applies the columns added by entity changes that the EnsureCreated
--  path missed on older dev DBs. Idempotent — safe to re-run.
--
--  Apply to a running docker stack:
--    docker compose -f Planscape.Server/docker/docker-compose.yml \
--      exec -T postgres psql -U planscape -d planscape \
--      < Planscape.Server/docker/dev-schema-patch.sql
--
--  The same patches run automatically on API startup via
--  Program.cs > PatchDevSchemaAsync, but that requires a Docker image
--  rebuild (compose up -d --build api). This file is the manual
--  fallback when you only want to heal the DB.
-- ─────────────────────────────────────────────────────────────────────

-- Phase 169 — project location + cover image + pin flag.
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "Latitude"      double precision;
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "Longitude"     double precision;
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "City"          text;
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "Country"       text;
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "CoverImageUrl" text;
ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "IsPinned"      boolean NOT NULL DEFAULT false;
