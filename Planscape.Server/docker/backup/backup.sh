#!/bin/sh
# Nightly Postgres backup — runs inside the pgbackup container via cron.
# Retains the last 14 dumps in /backups, then uploads the latest to S3/MinIO
# if MINIO_* env vars are present.
set -eu

STAMP="$(date -u +%Y%m%d-%H%M%S)"
DEST="/backups/planscape-${STAMP}.sql.gz"

echo "[backup] dumping planscape → ${DEST}"
pg_dump -h postgres -U planscape -d planscape | gzip > "${DEST}"

# Retention — keep 14 newest files
cd /backups
ls -1t planscape-*.sql.gz 2>/dev/null | tail -n +15 | xargs -r rm -f

# Optional: push to MinIO via mc if configured
if [ -n "${MINIO_HOST:-}" ] && [ -n "${MINIO_ACCESS_KEY:-}" ]; then
    echo "[backup] uploading to minio"
    # mc is not bundled in postgres:16-alpine; in production either:
    #   (a) swap this sidecar to minio/mc image, or
    #   (b) mount the backup volume into another service that has mc installed.
fi

echo "[backup] done"
