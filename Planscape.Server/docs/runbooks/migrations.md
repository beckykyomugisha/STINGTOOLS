# Database migrations runbook

> EF Core 8 migrations against Postgres 16. Connect direct (not via
> PgBouncer) — transaction-mode pooling kills DDL statements.

## TL;DR

```bash
source /etc/planscape/.env
./docker/migrate.sh           # apply pending migrations
./docker/migrate.sh --list    # show applied + pending
```

## Pending list (as of 2026-05)

The 12 migrations introduced this sprint pair land in this order; the
operator runs `migrate.sh` once and EF picks the unapplied ones:

| Order | Id | Sprint | Effect |
|---|---|---|---|
| 1 | `20260501000000_AddTenantIdToAllScopedEntities` | S1.1 | Adds `TenantId` column + index to 24 entities; backfills from parents; promotes to NOT NULL |
| 2 | `20260501010000_AddBillingPlanToTenant` | S1.3 | Plan / Currency / BillingCycle on Tenant |
| 3 | `20260501020000_AddTrialReminderSentDays` | S1.6 | Trial-reminder bitmask |
| 4 | `20260501030000_AddAuditLogHashChainAndPartitions` | S1.8 | `pgcrypto` extension + `audit_log_hash_chain` trigger + `verify_audit_chain` function |
| 5 | `20260501040000_AddBillingEntities` | S2.1 | Subscriptions / Invoices / Payments tables + composite indexes |
| 6 | `20260502000000_AddTaggedElementUpsertKey` | S3.1 | `(ProjectId, RevitElementId)` UNIQUE for ON CONFLICT |
| 7 | `20260502010000_AddOutboxMessages` | S3.2 | OutboxMessages table |
| 8 | `20260502020000_AddHotPathCompositeIndexes` | S3.3 | 11 composite indexes on hot read paths |
| 9 | `20260502030000_AddSceneNodes` | S5.1 | SceneNodes table for federated chunk index |
| 10 | `20260502040000_AddIssueAudioNotes` | S6.1 | IssueAudioNotes table |
| 11 | `20260502050000_AddModelMarkups` | S6.2 | ModelMarkups table |
| 12 | `20260502060000_AddPinCrdtUpdates` | S6.3 | PinCrdtUpdates table |
| 13 | `20260502070000_AddPendingErasureAt` | S7.4 | PendingErasureAt timestamp on Tenant |

(13 migrations actually — the count grew by one when the GDPR/POPIA
column landed.)

## Pre-flight checks

Before running on production:

1. **Backup**. Always.
   ```bash
   pg_dump -h $DB_HOST -U $DB_USER planscape > /var/backups/planscape-$(date +%Y%m%d-%H%M).sql
   ```
2. **Confirm direct-connect string**. `migrate.sh` looks for
   `ConnectionStrings__Migrations` first, falls back to
   `ConnectionStrings__Default`. The fallback must point at port 5432
   (Postgres direct) NOT 6432 (PgBouncer) — DDL through transaction-
   mode pooling breaks.
3. **Spare disk**. `20260501000000_AddTenantIdToAllScopedEntities`
   adds a column to 24 tables; the AlterColumn promotion to NOT NULL
   rewrites each table once. Need ~2× peak table size as free space
   (Postgres MVCC shadow copy).
4. **Confirm orphan rows are clean**. The S1.1 migration's NOT NULL
   promotion fails loudly if any row in a child table can't resolve
   its tenant from the parent. Run before applying:
   ```sql
   SELECT 'TaggedElements' AS t, COUNT(*) FROM "TaggedElements" e
     LEFT JOIN "Projects" p ON p."Id" = e."ProjectId"
     WHERE p."TenantId" IS NULL;
   -- repeat for the other 23 tables; expect zero everywhere.
   ```

## Apply

```bash
source /etc/planscape/.env
cd Planscape.Server
./docker/migrate.sh           # one shot, idempotent
```

If the API is running, drain it first to avoid a write hitting the
half-applied schema:

```bash
docker compose stop api worker
./docker/migrate.sh
docker compose start api worker
```

For a smooth rolling deploy use `--bundle` to emit a self-contained
binary then run it from the deploy host (no `dotnet ef` install
needed on the target):

```bash
./docker/migrate.sh --bundle
scp Planscape.Server/migration-bundle/efbundle prod-host:/tmp/
ssh prod-host '/tmp/efbundle --connection "$ConnectionStrings__Default"'
```

## Rollback

```bash
./docker/migrate.sh --rollback 20260501030000_AddAuditLogHashChainAndPartitions
```

Reverts everything **after** the named migration. The `Down` methods
are written and tested for each — but rolling back the S1.1 NOT NULL
column drop mid-production is irreversible if a write has already
landed on a row whose parent was deleted. Prefer fixing forward.

## After apply

```bash
# Sanity: every migration row in __EFMigrationsHistory present?
psql -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'

# Audit chain works?
psql -c "SELECT verify_audit_chain('00000000-0000-0000-0000-000000000000');"
# Returns NULL when the chain is intact (or a row id at the first break).

# Background jobs picking up?
curl -fsS https://api.planscape.app/hangfire/recurring-jobs | jq '.[] | {Id, NextExecution}'
```

## Common failures

| Symptom | Cause | Fix |
|---|---|---|
| `column "TenantId" contains null values` on AlterColumn | Orphan child rows whose parent's tenant can't resolve | Run the orphan-detection query above; clean offending rows; rerun |
| `permission denied to create extension "pgcrypto"` | Postgres user isn't superuser | Ask DBA to run `CREATE EXTENSION pgcrypto;` once, then re-run migrate |
| `error from server (502)` after migrate | API came back up while a worker was mid-migration | Stop both, finish migrate, start both |
| `relation "X" already exists` | Previous half-applied migration | Check `__EFMigrationsHistory`; if missing the latest row, drop the half-created table manually then rerun |
