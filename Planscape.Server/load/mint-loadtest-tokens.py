#!/usr/bin/env python3
"""Mint JWTs for the seeded load-test users, bypassing the login endpoint.

WHY NOT JUST LOG IN: the "auth" rate-limit policy allows 5 logins per 5 minutes
PER IP (Program.cs, AddPolicy("auth")). A load test needs dozens of distinct
users -- distinct users matter because the "api" policy partitions its 100
req/min budget by the `sub` claim, so driving load through one account measures
the rate limiter rather than the server. Logging them all in from one host is
impossible by design. Signing tokens directly with the app's own key is the
honest way around it: the tokens are indistinguishable from real ones.

This is a LOCAL LOAD-TEST UTILITY. It needs the signing key, so it only ever
runs against a dev stack whose JWT_KEY you already hold. Never point it at a
production key.

Usage (from Planscape.Server/):
    JWT_KEY=$(grep '^JWT_KEY=' .env.local | cut -d= -f2-) \\
    python load/mint-loadtest-tokens.py > load/loadtest-tokens.json
"""
import base64
import hashlib
import hmac
import json
import os
import subprocess
import sys
import time
import uuid

PG_CONTAINER = os.environ.get("PG_CONTAINER", "docker-postgres-1")
EMAIL_PREFIX = os.environ.get("EMAIL_PREFIX", "loadtest")
TTL_SECONDS = int(os.environ.get("TOKEN_TTL", "7200"))

jwt_key = os.environ.get("JWT_KEY")
if not jwt_key:
    sys.exit("JWT_KEY env var is required (read it from Planscape.Server/.env.local)")


def b64url(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).decode().rstrip("=")


def sign(payload: dict) -> str:
    # Header must mirror what the API issues: kid="current" selects the primary
    # signing key registered in Program.cs (the "previous" key exists for
    # rotation overlap).
    header = {"alg": "HS256", "kid": "current", "typ": "JWT"}
    signing_input = "{}.{}".format(
        b64url(json.dumps(header, separators=(",", ":")).encode()),
        b64url(json.dumps(payload, separators=(",", ":")).encode()),
    )
    sig = hmac.new(jwt_key.encode("utf-8"), signing_input.encode(), hashlib.sha256).digest()
    return "{}.{}".format(signing_input, b64url(sig))


def fetch_users() -> list:
    sql = (
        'SELECT u."Id", u."Email", u."TenantId", t."Slug", u."DisplayName" '
        'FROM "Users" u JOIN "Tenants" t ON t."Id" = u."TenantId" '
        "WHERE u.\"Email\" LIKE '{}%' ORDER BY u.\"Email\";".format(EMAIL_PREFIX)
    )
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "planscape", "-d", "planscape", "-tAF|", "-c", sql],
        capture_output=True, text=True, check=True,
    ).stdout
    rows = [line.split("|") for line in out.strip().splitlines() if line.strip()]
    if not rows:
        sys.exit(
            "No users matching '{}%' -- seed them first (see "
            "docs/DEPLOY_RUNBOOK.md, Measuring tier capacity).".format(EMAIL_PREFIX))
    return rows


def main() -> None:
    now = int(time.time())
    tokens = []
    for user_id, email, tenant_id, tenant_slug, display_name in fetch_users():
        tokens.append(sign({
            "sub": user_id,
            "jti": uuid.uuid4().hex,
            "user_id": user_id,
            "email": email,
            "iat": now,
            "tenant_id": tenant_id,
            "tenant_slug": tenant_slug,
            "tier": "Premium",
            "role": "Admin",
            "iso_role": "A",
            "display_name": display_name,
            "exp": now + TTL_SECONDS,
            "iss": os.environ.get("JWT_ISSUER", "Planscape"),
            "aud": os.environ.get("JWT_AUDIENCE", "Planscape.Client"),
        }))
    json.dump(tokens, sys.stdout)
    print("minted {} tokens".format(len(tokens)), file=sys.stderr)


if __name__ == "__main__":
    main()
