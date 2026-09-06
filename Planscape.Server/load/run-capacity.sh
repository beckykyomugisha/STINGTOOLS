#!/usr/bin/env bash
#
# run-capacity.sh — seed, measure, and ALWAYS clean up.
#
# LOCAL DEV ONLY. Run from Planscape.Server/:
#
#   ./load/run-capacity.sh --project-id <guid> [--peak-rps 150]
#
# ── WHY THIS SCRIPT EXISTS ───────────────────────────────────────────────────
# The capacity run used to be four commands pasted from the runbook, with
# cleanup documented as a fifth that people were expected to remember. Two
# things went wrong with that:
#
#   1. The documented cleanup could not work. FK_ProjectMembers_Users_UserId is
#      RESTRICT, so `DELETE FROM "Users" WHERE "Email" LIKE 'loadtest%'` aborts
#      on a foreign-key violation and every user stays.
#   2. Nothing ran it anyway.
#
# The result was 400 abandoned accounts in a demo tenant, read later as
# "426 users against a cap of 50" — a live onboarding blocker that was not one,
# which cost two investigations. Exceeding the cap is what a capacity fixture is
# FOR; leaving the residue behind is the defect.
#
# So cleanup is wired to an EXIT trap: it runs after a pass, after a failed k6
# run, and after Ctrl-C. If cleanup itself fails, this script exits non-zero and
# says so — a quiet failure here is what caused the original problem.
#
# It deliberately does NOT raise the tenant's user cap to make room for the
# fixture. That would hide the seeder's own cap guard, which exists to catch
# exactly this class of overrun.

set -euo pipefail

PROJECT_ID=""
PEAK_RPS="${PEAK_RPS:-150}"
BASE_URL="${BASE_URL:-http://localhost:5000}"
PG_CONTAINER="${PG_CONTAINER:-docker-postgres-1}"
PG_USER="${PG_USER:-planscape}"
PG_DB="${PG_DB:-planscape}"
ENV_FILE="${ENV_FILE:-.env.local}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project-id) PROJECT_ID="$2"; shift 2 ;;
    --peak-rps)   PEAK_RPS="$2";   shift 2 ;;
    --base-url)   BASE_URL="$2";   shift 2 ;;
    -h|--help)    sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$PROJECT_ID" ]]; then
  echo "ERROR: --project-id <guid> is required." >&2
  exit 2
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

psql_file() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" < "$1"; }
psql_q()    { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc "$1"; }

# ── Cleanup, on every exit path ──────────────────────────────────────────────
# Preserves the original exit code so a k6 failure is still reported as one,
# but upgrades a clean run to a failure if the database was left dirty.
cleanup() {
  local rc=$?
  echo
  echo "── Cleaning up load-test fixture data ──────────────────────────────"

  if ! psql_file "$here/cleanup-loadtest-data.sql"; then
    echo >&2
    echo "FAILED: load-test fixture data could NOT be removed." >&2
    echo "The database still holds loadtest* accounts. Leaving them behind is" >&2
    echo "what makes a demo tenant read as over its user cap. Investigate" >&2
    echo "before running anything else against this database:" >&2
    echo "  docker exec -i $PG_CONTAINER psql -U $PG_USER -d $PG_DB < load/cleanup-loadtest-data.sql" >&2
    exit 1
  fi

  # Belt and braces: the SQL asserts and rolls back on residue, but assert here
  # too, so a future edit that drops the in-transaction check cannot make this
  # script silently start passing with rows left over.
  local remaining
  remaining="$(psql_q "SELECT count(*) FROM \"Users\" WHERE \"Email\" LIKE 'loadtest%';")"
  if [[ "$remaining" != "0" ]]; then
    echo "FAILED: cleanup reported success but $remaining loadtest users remain." >&2
    exit 1
  fi

  echo "Cleanup verified: 0 loadtest accounts remain."
  exit "$rc"
}
trap cleanup EXIT
# Ctrl-C and `docker stop`-style TERM do not run an EXIT trap on their own — the
# shell is killed instead. Converting them into an explicit exit makes the EXIT
# trap fire exactly once, so an interrupted run still cleans up. An interrupted
# run is the likeliest way residue accumulates in the first place.
trap 'exit 130' INT
trap 'exit 143' TERM

# ── 1. Seed ──────────────────────────────────────────────────────────────────
echo "── Seeding fixture data ────────────────────────────────────────────"
psql_file "$here/seed-loadtest-data.sql"

# ── 2. Mint tokens ───────────────────────────────────────────────────────────
# Bulk login is impossible by design (the auth policy allows 5 logins per 5
# minutes per IP), so tokens are signed directly with the dev key.
echo "── Minting tokens ──────────────────────────────────────────────────"
# An already-exported JWT_KEY wins, so a stack started from something other than
# .env.local (compose defaults, a different env file, a secret manager) does not
# have to invent one just to satisfy this script.
if [[ -z "${JWT_KEY:-}" ]]; then
  if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: JWT_KEY is not set and $ENV_FILE does not exist." >&2
    echo "Either export JWT_KEY, or point ENV_FILE at a file containing it." >&2
    exit 2
  fi
  JWT_KEY="$(grep '^JWT_KEY=' "$ENV_FILE" | cut -d= -f2-)"
fi
JWT_KEY="$JWT_KEY" python "$here/mint-loadtest-tokens.py" > "$here/loadtest-tokens.json"

# ── 3. Measure ───────────────────────────────────────────────────────────────
echo "── Running k6 (PEAK_RPS=$PEAK_RPS) ─────────────────────────────────"
docker run --rm --network host -v "$here:/load" \
  -e BASE_URL="$BASE_URL" \
  -e PROJECT_ID="$PROJECT_ID" \
  -e PEAK_RPS="$PEAK_RPS" \
  grafana/k6 run /load/tier-capacity.js

# Cleanup runs from the EXIT trap.
