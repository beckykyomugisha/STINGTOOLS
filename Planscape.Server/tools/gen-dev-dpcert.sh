#!/usr/bin/env bash
#
# gen-dev-dpcert.sh — generate a self-signed cert for DEV DataProtection
# at-rest encryption, so the "No XML encryptor configured" startup warning
# disappears locally.
#
# Output: docker/certs/dp-dev.pfx (gitignored — never committed).
# docker-compose already points DP_CERT_PATH at /certs/dp-dev.pfx and
# DP_CERT_PASSWORD at "planscape-dev" by default, so after running this
# just rebuild the api/worker.
#
# DO NOT use this cert in production. For prod, provision a real cert and
# set DP_CERT_PATH / DP_CERT_PASSWORD in your .env (or a secrets store).
#
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."        # -> Planscape.Server
CERTDIR="docker/certs"
PFX="${CERTDIR}/dp-dev.pfx"
PASS="${DP_CERT_PASSWORD:-planscape-dev}"

command -v openssl >/dev/null 2>&1 || { echo "ERROR: openssl not found on PATH"; exit 1; }
mkdir -p "${CERTDIR}"

if [[ -f "${PFX}" ]]; then
  echo "Cert already exists: ${PFX}"
  echo "Delete it first if you want to regenerate."
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT

openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout "${tmp}/key.pem" -out "${tmp}/cert.pem" \
  -days 3650 -subj "/CN=Planscape DataProtection (dev)" >/dev/null 2>&1

openssl pkcs12 -export -out "${PFX}" \
  -inkey "${tmp}/key.pem" -in "${tmp}/cert.pem" \
  -passout "pass:${PASS}" >/dev/null 2>&1

echo "Wrote ${PFX}  (password: ${PASS})"
echo ""
echo "Restart the stack to pick it up:"
echo "  cd docker && docker compose up -d --build api worker"
echo ""
echo "On boot the 'No XML encryptor configured' warning should be gone."
