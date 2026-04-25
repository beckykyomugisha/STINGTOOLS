#!/usr/bin/env bash
# Generate a self-signed TLS cert for local dev / staging.
# For production, use Let's Encrypt via certbot against nginx's
# /.well-known/acme-challenge/ path (already wired in nginx.conf).
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)/certs"
mkdir -p "$DIR"

CN="${1:-planscape.local}"

openssl req -x509 -nodes -newkey rsa:2048 -days 365 \
    -keyout "$DIR/server.key" \
    -out    "$DIR/server.crt" \
    -subj   "/CN=${CN}" \
    -addext "subjectAltName=DNS:${CN},DNS:localhost,IP:127.0.0.1"

chmod 600 "$DIR/server.key"
echo "Self-signed cert written to $DIR (CN=${CN})."
echo "Trust server.crt on the mobile device / simulator keychain for HTTPS testing."
