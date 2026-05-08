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
#   ./Planscape.Server/dev-up.sh init               # one-step first-run bootstrap
#   ./Planscape.Server/dev-up.sh stack up           # ← DAILY WORK MODE
#                                                     full stack in docker (api, db, redis, worker)
#                                                     auto-restarts on reboot
#   ./Planscape.Server/dev-up.sh stack down         # stop the stack (keep volumes)
#   ./Planscape.Server/dev-up.sh stack reset        # wipe volumes + recreate
#   ./Planscape.Server/dev-up.sh stack db-only      # only postgres+redis (for dev-mode run)
#   ./Planscape.Server/dev-up.sh stack rebuild      # force --no-cache rebuild of api+worker
#   ./Planscape.Server/dev-up.sh status             # show container health
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

# ── help handled before the .env.local existence check ────────────
# (running `help` on a fresh checkout must work without bootstrapping)
case "${1:-}" in
  -h|--help|help)
    sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;
esac

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

# PERMANENT FIX: unset DB_PASSWORD after sourcing. If the user's parent
# shell had DB_PASSWORD exported (a stale value from some prior session),
# it would leak into `docker compose up` via the
# ${DB_PASSWORD:-Planscape2026!} default, baking the wrong password into
# the freshly-initialised Postgres volume. The 'stack' subcommand sets
# DB_PASSWORD explicitly from .env.local's connection string when it
# needs to; clearing it here means everything else (direct docker
# invocations, dotnet run, etc.) gets a clean slate.
unset DB_PASSWORD


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

# Permanent fix for the recurring "MSB3026 file locked by Planscape.API
# (PID xxxxx)" build failures. When a Git Bash window gets closed without
# Ctrl+C, dotnet.exe lives on as a parent-less process that holds the
# DLLs and port 5000 hostage. This pre-flight finds the offender via
# `netstat` and kills it before `dotnet run` tries to bind.
kill_stale_api() {
  local pids
  pids="$(cmd //c "netstat -ano | findstr LISTENING | findstr :5000" 2>/dev/null \
    | awk '{print $NF}' | tr -d '\r' | sort -u)"
  if [[ -z "$pids" ]]; then return 0; fi
  for pid in $pids; do
    [[ "$pid" =~ ^[0-9]+$ ]] || continue
    info "Killing stale process on :5000 (PID $pid)"
    cmd //c "taskkill /F /PID $pid /T" >/dev/null 2>&1 || true
  done
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
    kill_stale_api
    info "Starting API on http://localhost:5000 (Ctrl+C to stop)…"
    dotnet run --project "$API"
    ;;

  kill)
    kill_stale_api
    ok "Stale API processes cleared"
    ;;

  build)
    info "Building solution…"
    dotnet build "$SCRIPT_DIR/Planscape.sln"
    ok "Build complete"
    ;;

  stack)
    # Brings up the full Planscape stack via docker-compose:
    #   api · worker · postgres · redis · converter · otel-collector
    # restart: unless-stopped is set on every service, so the API
    # survives terminal close, log-out, and OS reboot. THIS is the
    # daily-work mode — `dev-up.sh run` is for foreground debugging
    # only. The recurring "Authentication failed: nothing is listening
    # on http://localhost:5000" Revit dialog is what happens when
    # daily work runs `dev-up.sh run` and then closes the terminal.
    #
    # Subcommands:
    #   up         — full stack (api on :5000, postgres :5432, redis :6379)
    #   down       — stop everything, keep volumes
    #   reset      — nuke volumes + recreate (clean slate)
    #   db-only    — postgres + redis only (for dev-mode `dotnet run`)
    sub="${2:-up}"
    pw_field="${ConnectionStrings__Default##*Password=}"
    pw="${pw_field%%;*}"
    if [[ -z "$pw" ]]; then
      die "Could not parse Password=... out of ConnectionStrings__Default"
    fi
    if [[ -z "${Jwt__Key:-}" ]]; then
      die "Jwt__Key not loaded from .env.local — run ./dev-up.sh init"
    fi
    info "Using DB_PASSWORD (${#pw} chars) and JWT_KEY (${#Jwt__Key} chars) from .env.local"
    pushd "$SCRIPT_DIR/docker" >/dev/null
    # Both compose-required vars exported inline so they win over any
    # stale shell exports (DB_PASSWORD leak fix from commit d209e9f).
    case "$sub" in
      up)
        info "Bringing up full stack with --build (incremental — only rebuilds changed layers)…"
        # --build is essential: `docker compose up -d` alone does NOT
        # rebuild even when source files have changed since the last
        # build. With --build, Docker rebuilds only the layers whose
        # inputs changed (typically the dotnet publish step picking up
        # new .cs files); unchanged layers are reused from cache.
        # First run on a fresh checkout takes 3-8 min; subsequent
        # incremental rebuilds after a `git pull` take 30-90 s.
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose up -d --build
        ok "Stack up. API will be reachable at http://localhost:5000 once healthy."
        info "  ./dev-up.sh status        # check container health"
        info "  ./dev-up.sh stack rebuild # force a clean rebuild (--no-cache)"
        info "  ./dev-up.sh stack down    # stop"
        ;;
      down)
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose down
        ok "Stack down (volumes preserved)"
        ;;
      reset)
        warn "Wiping volumes — all demo data will be lost."
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose down -v
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose up -d
        ok "Stack reset — full stack recreated with a fresh DB."
        ;;
      db-only)
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose up -d postgres redis
        ok "DB stack up (postgres + redis only). Now you can './dev-up.sh run' against it."
        ;;
      rebuild)
        # Force a clean image rebuild (--no-cache). Useful when:
        #  * the dotnet publish layer cached a broken state
        #  * a NuGet package version bump didn't pick up
        #  * Dockerfile changes added new system packages
        # 3-8 min just like the very first build.
        warn "Force rebuilding api + worker images with --no-cache (3-8 min)…"
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose build --no-cache api worker
        DB_PASSWORD="$pw" JWT_KEY="$Jwt__Key" docker compose up -d
        ok "Rebuild complete. Run './dev-up.sh status' to confirm."
        ;;
      *)
        popd >/dev/null
        die "Unknown stack subcommand: $sub  (try: up | down | reset | db-only | rebuild)"
        ;;
    esac
    popd >/dev/null
    ;;

  status)
    # Show health of every container so the user can see at a glance
    # whether the API is up + which port it's serving on.
    pushd "$SCRIPT_DIR/docker" >/dev/null
    docker compose ps
    popd >/dev/null
    ;;

  shell)
    info "Spawning sub-shell with .env.local loaded. Type 'exit' to leave."
    exec "${SHELL:-bash}"
    ;;

  -h|--help|help)
    sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'
    ;;

  *)
    die "Unknown command: $cmd  (try: init | stack | status | check | migrate | run | kill | build | shell | help)"
    ;;
esac
