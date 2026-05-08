#!/usr/bin/env bash
# Planscape.Server/dev-up.sh — local-dev convenience wrapper.
#
# What it does:
#   1. Sources Planscape.Server/.env.local (gitignored secrets)
#   2. Validates the required env vars are present + non-trivial
#   3. Runs the action you ask for: migrate / run / build / shell
#
# Usage:
#   ./Planscape.Server/dev-up.sh                # default: migrate
#   ./Planscape.Server/dev-up.sh migrate        # apply pending migrations
#   ./Planscape.Server/dev-up.sh run            # dotnet run the API
#   ./Planscape.Server/dev-up.sh build          # dotnet build only
#   ./Planscape.Server/dev-up.sh shell          # spawn a sub-shell with env loaded
#   ./Planscape.Server/dev-up.sh check          # validate env without doing anything
#
# Bootstrap (first time):
#   cp Planscape.Server/.env.local.example Planscape.Server/.env.local
#   # edit Planscape.Server/.env.local — set Jwt__Key + ConnectionStrings__Default
#   ./Planscape.Server/dev-up.sh check

set -euo pipefail

# Resolve script dir even when invoked from elsewhere (Git Bash + WSL).
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env.local"
EXAMPLE="$SCRIPT_DIR/.env.local.example"
INFRA="$SCRIPT_DIR/src/Planscape.Infrastructure"
API="$SCRIPT_DIR/src/Planscape.API"

# ── colours ──────────────────────────────────────────────────────────
if [[ -t 1 ]]; then
  RED=$'\033[31m'; YEL=$'\033[33m'; GRN=$'\033[32m'; DIM=$'\033[2m'; OFF=$'\033[0m'
else
  RED=''; YEL=''; GRN=''; DIM=''; OFF=''
fi
die()  { echo "${RED}✗${OFF} $*" >&2; exit 1; }
warn() { echo "${YEL}!${OFF} $*" >&2; }
ok()   { echo "${GRN}✓${OFF} $*"; }
info() { echo "${DIM}→${OFF} $*"; }

# ── load .env.local ──────────────────────────────────────────────────
if [[ ! -f "$ENV_FILE" ]]; then
  warn ".env.local not found at $ENV_FILE"
  if [[ -f "$EXAMPLE" ]]; then
    info "Copy the template and edit it:"
    info "  cp '$EXAMPLE' '$ENV_FILE'"
    info "  # then fill in Jwt__Key and ConnectionStrings__Default"
  fi
  die "Cannot continue without .env.local"
fi
# shellcheck disable=SC1090
source "$ENV_FILE"

# ── validate required env ────────────────────────────────────────────
errors=0
if [[ -z "${Jwt__Key:-}" ]]; then
  warn "Jwt__Key not set"; errors=$((errors+1))
elif [[ "${#Jwt__Key}" -lt 32 ]]; then
  warn "Jwt__Key is only ${#Jwt__Key} chars (need 32+)"; errors=$((errors+1))
elif [[ "$Jwt__Key" == "REPLACE_WITH_OPENSSL_RAND_BASE64_48" ]]; then
  warn "Jwt__Key still has the placeholder value — generate a real one:"
  warn "  openssl rand -base64 48        # or: head -c 48 /dev/urandom | base64"
  errors=$((errors+1))
fi
if [[ -z "${ConnectionStrings__Default:-}" ]]; then
  warn "ConnectionStrings__Default not set"; errors=$((errors+1))
fi
if [[ "$errors" -gt 0 ]]; then
  die "$errors required env var(s) missing or invalid — edit $ENV_FILE"
fi

# ── dispatch ─────────────────────────────────────────────────────────
cmd="${1:-migrate}"
case "$cmd" in
  check)
    ok "Jwt__Key OK (${#Jwt__Key} chars)"
    ok "ConnectionStrings__Default OK"
    ok "Environment is good — try './dev-up.sh migrate' or './dev-up.sh run'"
    ;;

  migrate)
    info "Applying EF Core migrations…"
    # Use the direct (non-pooled) connection string for DDL — same trick
    # docker/migrate.sh uses in production.
    if [[ -n "${ConnectionStrings__Migrations:-}" ]]; then
      export ConnectionStrings__Default="$ConnectionStrings__Migrations"
    fi
    dotnet ef database update \
      --project "$INFRA" \
      --startup-project "$API"
    ok "Migrations applied"
    ;;

  run)
    info "Starting API on http://localhost:5000 (Ctrl+C to stop)…"
    dotnet run --project "$API"
    ;;

  build)
    info "Building solution…"
    dotnet build "$SCRIPT_DIR/Planscape.sln"
    ok "Build complete"
    ;;

  shell)
    info "Spawning sub-shell with .env.local loaded. Type 'exit' to leave."
    exec "${SHELL:-bash}"
    ;;

  -h|--help|help)
    sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'
    ;;

  *)
    die "Unknown command: $cmd  (try: check | migrate | run | build | shell | help)"
    ;;
esac
