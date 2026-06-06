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

-- N2 — LiveKit Egress meeting recordings (table not in the discovered EF migration set).
CREATE TABLE IF NOT EXISTS "MeetingRecordings" (
  "Id" uuid NOT NULL PRIMARY KEY,
  "TenantId" uuid NOT NULL,
  "ProjectId" uuid NOT NULL,
  "SessionId" uuid NOT NULL,
  "MeetingId" uuid NULL,
  "EgressId" character varying(128) NOT NULL DEFAULT '',
  "Kind" character varying(24) NOT NULL DEFAULT 'room-composite',
  "Status" character varying(16) NOT NULL DEFAULT 'STARTING',
  "StorageKey" text NULL,
  "FileName" text NULL,
  "FileSizeBytes" bigint NULL,
  "DurationSeconds" double precision NULL,
  "Error" text NULL,
  "StartedBy" text NOT NULL DEFAULT '',
  "StartedByUserId" uuid NULL,
  "StartedAt" timestamp with time zone NOT NULL DEFAULT now(),
  "EndedAt" timestamp with time zone NULL
);
CREATE INDEX IF NOT EXISTS "IX_MeetingRecordings_TenantId"          ON "MeetingRecordings" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_MeetingRecordings_SessionId"         ON "MeetingRecordings" ("SessionId");
CREATE INDEX IF NOT EXISTS "IX_MeetingRecordings_ProjectId_MeetingId" ON "MeetingRecordings" ("ProjectId", "MeetingId");
CREATE INDEX IF NOT EXISTS "IX_MeetingRecordings_EgressId"          ON "MeetingRecordings" ("EgressId");
