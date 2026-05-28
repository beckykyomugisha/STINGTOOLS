-- Phase 189 / Pillar B — convert TelemetryPoints to a TimescaleDB hypertable.
-- Run ONCE, AFTER the table exists (EF migration OR PlatformSchemaPatcher),
-- on a Postgres with the timescaledb extension available. Idempotent.
--
-- Why this is a separate script: EF cannot emit Timescale DDL, and a hypertable
-- requires the partitioning column (Ts) in every unique index — so the Id-only
-- primary key must become composite (Id, Ts) before conversion.

CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Swap the Id-only PK for (Id, Ts). Handles both the EF constraint name and
-- the Postgres auto-name from the patcher's inline PRIMARY KEY.
ALTER TABLE "TelemetryPoints" DROP CONSTRAINT IF EXISTS "PK_TelemetryPoints";
ALTER TABLE "TelemetryPoints" DROP CONSTRAINT IF EXISTS "TelemetryPoints_pkey";
ALTER TABLE "TelemetryPoints" ADD PRIMARY KEY ("Id", "Ts");

SELECT create_hypertable('"TelemetryPoints"', 'Ts', if_not_exists => TRUE, migrate_data => TRUE);

-- Retention — drop raw points older than 365 days (tune per project).
SELECT add_retention_policy('"TelemetryPoints"', INTERVAL '365 days', if_not_exists => TRUE);

-- Continuous aggregate — hourly mean/min/max per device+metric for trend views.
CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT "ProjectId", "DeviceId", "Metric",
       time_bucket(INTERVAL '1 hour', "Ts") AS bucket,
       avg("Value") AS avg_value,
       min("Value") AS min_value,
       max("Value") AS max_value
FROM "TelemetryPoints"
GROUP BY "ProjectId", "DeviceId", "Metric", bucket
WITH NO DATA;

SELECT add_continuous_aggregate_policy('telemetry_hourly',
    start_offset      => INTERVAL '3 days',
    end_offset        => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour',
    if_not_exists     => TRUE);
