#!/usr/bin/env bash
# bootstrap-dev.sh — idempotent dev environment bootstrap for Planscape server.
#
# Creates ./.env from ./.env.template and fills in the secrets that
# `docker compose up` requires (JWT_KEY) or strongly recommends
# (DB_PASSWORD, CONVERTER_TOKEN, GRAFANA_ADMIN_PASSWORD).
#
# Idempotent: keeps any value already set in .env, only fills empty keys.
# Re-run safely after pulling new keys into .env.template.
#
# Usage:
#   ./bootstrap-dev.sh             # write .env, generate missing secrets
#   ./bootstrap-dev.sh --force     # rotate every managed secret
#   ./bootstrap-dev.sh --print     # show resolved values without writing
#
# Requires: openssl. Falls back to /dev/urandom + base64 if openssl is absent.

set -euo pipefail

cd "$(dirname "$0")"

TEMPLATE=".env.template"
ENV_FILE=".env"
FORCE=0
PRINT=0

for arg in "$@"; do
    case "$arg" in
        --force) FORCE=1 ;;
        --print) PRINT=1 ;;
        -h|--help)
            sed -n '2,18p' "$0"
            exit 0
            ;;
        *)
            echo "Unknown argument: $arg" >&2
            exit 2
            ;;
    esac
done

if [[ ! -f "$TEMPLATE" ]]; then
    echo "ERROR: $TEMPLATE not found in $(pwd)" >&2
    exit 1
fi

# Generate a strong random secret. Length is in raw bytes, base64 expands ~33%.
gen_secret() {
    local bytes="${1:-48}"
    if command -v openssl >/dev/null 2>&1; then
        openssl rand -base64 "$bytes" | tr -d '\n'
    else
        head -c "$bytes" /dev/urandom | base64 | tr -d '\n'
    fi
}

# Read the current value of KEY from .env (empty if missing or unset).
get_value() {
    local key="$1" file="$2"
    [[ -f "$file" ]] || return 0
    # Match `KEY=...` (allow leading whitespace), strip surrounding quotes.
    sed -n -E "s/^[[:space:]]*${key}=(.*)$/\1/p" "$file" | tail -n1 | sed -E 's/^"(.*)"$/\1/'
}

# Set KEY=VALUE in FILE. Adds the line if missing, replaces it if present.
# Uses a temp file + mv to stay safe with shared FS.
set_value() {
    local key="$1" value="$2" file="$3"
    local tmp
    tmp="$(mktemp)"
    if grep -qE "^[[:space:]]*${key}=" "$file"; then
        # Escape backslashes and pipe (our sed delimiter) in value.
        local safe
        safe=$(printf '%s' "$value" | sed -e 's/[\\|]/\\&/g')
        sed -E "s|^[[:space:]]*${key}=.*$|${key}=${safe}|" "$file" > "$tmp"
    else
        cat "$file" > "$tmp"
        printf '\n%s=%s\n' "$key" "$value" >> "$tmp"
    fi
    mv "$tmp" "$file"
    chmod 600 "$file" 2>/dev/null || true
}

# Ensure .env exists, seeded from the template.
if [[ ! -f "$ENV_FILE" ]]; then
    echo "Creating $ENV_FILE from $TEMPLATE"
    cp "$TEMPLATE" "$ENV_FILE"
    chmod 600 "$ENV_FILE" 2>/dev/null || true
fi

# Keys this script manages. JWT_KEY is required by compose; the others
# default to weak fallbacks in docker-compose.yml so we set them too.
declare -a MANAGED_KEYS=(JWT_KEY DB_PASSWORD CONVERTER_TOKEN GRAFANA_ADMIN_PASSWORD)
declare -A SECRET_BYTES=(
    [JWT_KEY]=48
    [DB_PASSWORD]=24
    [CONVERTER_TOKEN]=32
    [GRAFANA_ADMIN_PASSWORD]=18
)

declare -a UPDATED=()
declare -a KEPT=()

for key in "${MANAGED_KEYS[@]}"; do
    current="$(get_value "$key" "$ENV_FILE")"
    if [[ -n "$current" && "$FORCE" -eq 0 ]]; then
        KEPT+=("$key")
        continue
    fi
    new_value="$(gen_secret "${SECRET_BYTES[$key]}")"
    if [[ "$PRINT" -eq 1 ]]; then
        printf '%s=%s\n' "$key" "$new_value"
    else
        set_value "$key" "$new_value" "$ENV_FILE"
    fi
    UPDATED+=("$key")
done

if [[ "$PRINT" -eq 1 ]]; then
    exit 0
fi

echo
echo "Bootstrap complete: $ENV_FILE"
if (( ${#UPDATED[@]} )); then
    echo "  Generated: ${UPDATED[*]}"
fi
if (( ${#KEPT[@]} )); then
    echo "  Kept existing: ${KEPT[*]}"
fi
echo
echo "Next:  docker compose up -d --build"
