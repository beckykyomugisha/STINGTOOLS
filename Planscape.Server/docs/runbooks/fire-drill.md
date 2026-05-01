# Fire-drill runbook

> **Purpose**: this is the playbook a single on-call engineer follows when
> something goes wrong at 02:00. It assumes you've never seen the
> codebase before. Every command below is copy-paste safe.

## Tier 0 — The first 60 seconds

1. Open <https://status.planscape.app>. If it's already saying "down",
   skip to Tier 2.
2. Hit `/health` directly: `curl -fsS https://api.planscape.app/health`.
3. Check Hangfire dashboard at `/hangfire`. Look for queues backed up,
   recurring jobs stuck.

If `/health` is 200 and Hangfire is moving, the problem is **probably**
contained — go to Tier 1.

## Tier 1 — Single-component degradation

| Symptom                                | Action                                              |
|----------------------------------------|-----------------------------------------------------|
| API slow but responding                | `docker compose restart api` (zero-downtime if 2 replicas) |
| Postgres CPU pinned                    | `pgbouncer` admin: `SHOW POOLS;` — kick stuck txns  |
| Redis OOM                              | `docker compose restart redis` (cache is rebuildable) |
| Hangfire stuck on a job                | Hangfire UI → Jobs → Failing → "Requeue"            |
| Push notifications not sending         | Check FCM project quota; rotate service-account key |
| Dunning emails not landing             | Postmark dashboard → bounces; verify SPF/DKIM       |

## Tier 2 — Postgres primary down

The replica from S3.4's docker-compose is the lifeline:

```bash
# 1. Stop the API so it doesn't keep retrying writes
docker compose stop api worker

# 2. Promote the replica to primary
ssh standby
sudo -u postgres pg_ctl promote -D /var/lib/postgresql/data

# 3. Update the connection string
docker compose exec api sh -c 'printf "Host=standby;..." > /tmp/conn'
# (or restart with new env)

# 4. Bring API back up
docker compose start api worker
```

RTO target: 15 min from page to fully back up.

## Tier 3 — Storage outage (S3 / MinIO unreachable)

Reads degrade gracefully — the viewer shows the "Loading model..." state
and retries. Writes need to fail-fast so the mobile offline queue picks
them up. Steps:

1. Confirm: `aws s3 ls s3://planscape-prod` from the API host.
2. If MinIO local: `docker compose restart minio` and check disk space.
3. If S3 region outage: flip `Storage:S3:Region` to a secondary region
   bucket (we replicate nightly). Restart API.

## Tier 4 — Tenant isolation alarm

If a tenant reports seeing another tenant's data:

1. **Stop accepting writes**. `kubectl scale deployment api --replicas=0`
   (or docker compose stop api).
2. Snapshot the database — `pg_dump` to an offline volume.
3. Run `SELECT verify_audit_chain('<tenant_uuid>');` for each tenant
   listed in the report. Look for chain breaks indicating a write that
   crossed boundaries.
4. Page the founder + the tenant's primary contact. Don't restart
   writes until forensics is done.
5. Post a public incident on the status page.

## Tier 5 — Total dataloss / catastrophic

```bash
# Restore latest WAL-shipped backup from Backblaze B2
aws s3 sync s3://planscape-backups/postgres/$(date +%Y-%m) /var/restore --endpoint-url https://s3.us-west-002.backblazeb2.com
# pg_basebackup-style restore
sudo -u postgres pg_ctl stop
sudo cp -R /var/restore/* /var/lib/postgresql/data
sudo -u postgres pg_ctl start
```

RPO target: 30 s (replica) → 24 h (B2 fallback).

## Quarterly drill schedule

Run a planned drill once per quarter, on a Saturday:

| Quarter | Scenario                              | Owner   |
|---------|---------------------------------------|---------|
| Q1      | Promote replica → primary             | Founder |
| Q2      | Restore from B2 to a fresh box        | Founder |
| Q3      | Tenant-isolation forensics simulation | Founder |
| Q4      | Full DR: rebuild from off-site backup | Founder |

Each drill closes with a 10-minute write-up: what worked, what didn't,
what to fix before next quarter. Append to `docs/runbooks/drills/`.
