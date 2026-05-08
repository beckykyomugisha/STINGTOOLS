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
#   ./Planscape.Server/dev-up.sh init           # one-step first-run bootstrap
#   ./Planscape.Server/dev-up.sh migrate        # apply pending migrations
#   ./Planscape.Server/dev-up.sh run            # dotnet run the API
#   ./Planscape.Server/dev-up.sh build          # dotnet build only
#   ./Planscape.Server/dev-up.sh shell          # spawn a sub-shell with env loaded
#   ./Planscape.Server/dev-up.sh check          # validate env without doing anything
#
# Bootstrap (first time):
#   ./Planscape.Server/dev-up.sh init
#   # — that copies .env.local.example to .env.local AND generates a real
#   #   Jwt__Key in one step. Edit ConnectionStrings__Default afterwards
#   #   if your Postgres differs from the docker-compose default.

set -euo pipefail

# Resolve script dir even when invoked from elsewhere (Git Bash + WSL).
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env.local"
EXAMPLE="$SCRIPT_DIR/.env.local.example"
PLACEHOLDER="REPLACE_WITH_OPENSSL_RAND_BASE64_48"
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

# ── init handled before the .env.local existence check ─────────────
# (running `init` when .env.local already exists must NOT clobber it)
if [[ "${1:-}" == "init" ]]; then
  if [[ -f "$ENV_FILE" ]]; then
    warn "$ENV_FILE already exists — refusing to overwrite"
    info "Run './dev-up.sh check' to validate it, or delete it first."
    exit 1
  fi
  [[ -f "$EXAMPLE" ]] || die "Template missing: $EXAMPLE"
  # Generate a 32+ char base64 secret. Prefer openssl, fall back to
  # /dev/urandom which is always available on Git Bash + WSL.
  if command -v openssl >/dev/null 2>&1; then
    new_key="$(openssl rand -base64 48)"
  else
    new_key="$(head -c 48 /dev/urandom | base64)"
  fi
  cp "$EXAMPLE" "$ENV_FILE"
  # Substitute the placeholder. The key contains '/' and '+' so use a
  # delimiter (|) that doesn't appear in base64 output.
  if sed --version >/dev/null 2>&1; then
    # GNU sed — supports -i without an arg.
    sed -i "s|$PLACEHOLDER|$new_key|" "$ENV_FILE"
  else
    # BSD sed (macOS, some MINGW builds) — needs a backup arg.
    sed -i.bak "s|$PLACEHOLDER|$new_key|" "$ENV_FILE" && rm -f "$ENV_FILE.bak"
  fi
  ok "Created $ENV_FILE with a fresh Jwt__Key (${#new_key} chars)"
  info "Review ConnectionStrings__Default in the file if your Postgres"
  info "differs from the docker-compose default, then run:"
  info "  ./dev-up.sh check     # validate the env"
  info "  ./dev-up.sh migrate   # apply migrations"
  info "  ./dev-up.sh run       # boot the API on :5000"
  exit 0
fi

# ── load .env.local ──────────────────────────────────────────────────
if [[ ! -f "$ENV_FILE" ]]; then
  warn ".env.local not found at $ENV_FILE"
  if [[ -f "$EXAMPLE" ]]; then
    info "Run the one-step bootstrap:"
    info "  ./dev-up.sh init"
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

# ── tool-manifest restore (idempotent) ───────────────────────────────
# `dotnet ef` is pinned in Planscape.Server/.config/dotnet-tools.json as
# a local tool. The discovery logic walks UP from the current directory
# looking for .config/dotnet-tools.json, so all dotnet calls below run
# from $SCRIPT_DIR to make sure the manifest is found regardless of
# where the user invoked the script. ensure_tools() restores the local
# tools the first time (or any time the .NET CLI says they're missing),
# so users don't have to remember a separate `dotnet tool restore` step.
ensure_tools() {
  pushd "$SCRIPT_DIR" >/dev/null
  if ! dotnet tool list 2>/dev/null | grep -qi 'dotnet-ef'; then
    info "Installing pinned local tools (dotnet-ef)…"
    dotnet tool restore
  fi
  popd >/dev/null
}

# ── dispatch ─────────────────────────────────────────────────────────
cmd="${1:-migrate}"
case "$cmd" in
  check)
    ok "Jwt__Key OK (${#Jwt__Key} chars)"
    ok "ConnectionStrings__Default OK"
    ok "Environment is good — try './dev-up.sh migrate' or './dev-up.sh run'"
    ;;

  migrate)
    ensure_tools
    info "Applying EF Core migrations…"
    # Use the direct (non-pooled) connection string for DDL — same trick
    # docker/migrate.sh uses in production.
    if [[ -n "${ConnectionStrings__Migrations:-}" ]]; then
      export ConnectionStrings__Default="$ConnectionStrings__Migrations"
    fi
    pushd "$SCRIPT_DIR" >/dev/null
    dotnet ef database update \
      --project "$INFRA" \
      --startup-project "$API"
    popd >/dev/null
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
    die "Unknown command: $cmd  (try: init | check | migrate | run | build | shell | help)"
    ;;
esac
