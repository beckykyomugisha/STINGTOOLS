#!/usr/bin/env bash
# Planscape — apply EF Core migrations against the production database.
#
# Idempotent: running twice is a no-op for already-applied migrations.
# Connects DIRECT to Postgres (port 5432) — bypasses PgBouncer because
# transaction-mode pooling breaks EF migrations (DDL needs session state
# that pgbouncer doesn't preserve).
#
# Usage:
#   ./migrate.sh                  — apply pending migrations
#   ./migrate.sh --list           — show applied + pending
#   ./migrate.sh --rollback <id>  — revert to a specific migration
#   ./migrate.sh --bundle         — emit a self-contained binary bundle
#                                   (no .NET SDK on the target host)

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/Planscape.API/Planscape.API.csproj"
INFRA="$ROOT/src/Planscape.Infrastructure/Planscape.Infrastructure.csproj"

# Use the direct-connect string (Migrations) not the pooled one (Default).
export ConnectionStrings__Default="${ConnectionStrings__Migrations:-${ConnectionStrings__Default:-}}"

if [[ -z "${ConnectionStrings__Default:-}" ]]; then
  echo "ERROR: ConnectionStrings__Default not set. Source /etc/planscape/.env first." >&2
  exit 2
fi

cmd="${1:---update}"
case "$cmd" in
  --list)
    dotnet ef migrations list --project "$INFRA" --startup-project "$PROJECT"
    ;;
  --update|"")
    echo "→ Applying pending migrations…"
    dotnet ef database update --project "$INFRA" --startup-project "$PROJECT" --verbose
    echo "✓ Done."
    ;;
  --rollback)
    target="${2:-}"
    if [[ -z "$target" ]]; then
      echo "Usage: $0 --rollback <MigrationId>" >&2
      exit 2
    fi
    read -p "About to rollback to '$target' on $(echo "$ConnectionStrings__Default" | grep -oE 'Host=[^;]+'). Confirm [y/N]: " yn
    case "$yn" in
      y|Y) dotnet ef database update "$target" --project "$INFRA" --startup-project "$PROJECT" ;;
      *) echo "Aborted."; exit 1 ;;
    esac
    ;;
  --bundle)
    out="$ROOT/migration-bundle"
    mkdir -p "$out"
    dotnet ef migrations bundle --project "$INFRA" --startup-project "$PROJECT" --output "$out/efbundle"
    echo "✓ Bundle: $out/efbundle (run with: ./efbundle --connection \"\$ConnectionStrings__Default\")"
    ;;
  *)
    echo "Usage: $0 [--list | --update | --rollback <id> | --bundle]" >&2
    exit 2
    ;;
esac
