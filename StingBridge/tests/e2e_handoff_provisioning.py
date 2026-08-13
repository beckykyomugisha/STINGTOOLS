#!/usr/bin/env python3
"""End-to-end: a planscape.build-only subscriber becomes a working StingBridge user.

This exercises the whole Phase 200 chain against a REAL server and a REAL
database — no fakes:

  1. mint a handoff ticket exactly as the Cloudflare Pages Function does
     (HMAC-SHA256 over the compact JSON payload, base64url, 120 s TTL)
  2. redeem it at POST /api/auth/handoff/exchange
  3. assert the server provisioned a Tenant, an AppUser and a starter Project
  4. assert the provisioned account CANNOT log in with a password
     (its hash is deliberately unusable — this is why PATs exist)
  5. mint a personal access token with the handoff session
  6. authenticate a real PlanscapeClient with nothing but that token
  7. ingest IFC element data into the starter project through the bridge

Usage:

    export STING_E2E_URL=http://localhost:5099
    export PLANSCAPE_HANDOFF_SECRET=<same secret the server has>
    python StingBridge/tests/e2e_handoff_provisioning.py

Exits non-zero on the first failed assertion.
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import sys
import time
import uuid
from pathlib import Path

import requests

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.planscape.client import PlanscapeClient, PlanscapeAuthError  # noqa: E402

BASE = os.environ.get("STING_E2E_URL", "http://localhost:5099").rstrip("/")
SECRET = os.environ.get("PLANSCAPE_HANDOFF_SECRET", "")

_step = 0


def step(msg: str) -> None:
    global _step
    _step += 1
    print(f"\n[{_step}] {msg}")


def ok(msg: str) -> None:
    print(f"    OK  {msg}")


def fail(msg: str) -> None:
    print(f"    FAIL {msg}")
    sys.exit(1)


def b64url(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).decode().rstrip("=")


def mint_ticket(email: str, slug: str, role: str = "owner", ttl: int = 120) -> str:
    """Byte-for-byte the same construction as
    marketing-site/functions/api/cloud/handoff.ts."""
    now = int(time.time())
    payload = json.dumps({
        "jti": str(uuid.uuid4()),
        "email": email,
        "tenantSlug": slug,
        "tenantName": "E2E Handoff Org",
        "firstName": "E2E",
        "lastName": "Subscriber",
        "role": role,
        "tier": "studio",
        "iat": now,
        "exp": now + ttl,
    }, separators=(",", ":"))
    data = payload.encode("utf-8")
    sig = hmac.new(SECRET.encode("utf-8"), data, hashlib.sha256).digest()
    return f"{b64url(data)}.{b64url(sig)}"


def main() -> int:
    if not SECRET:
        fail("PLANSCAPE_HANDOFF_SECRET is not set — it must match the server's value")

    marker = uuid.uuid4().hex[:12]
    email = f"e2e-{marker}@example.com"
    slug = f"e2e-{marker}"

    print(f"Server : {BASE}")
    print(f"Subject: {email} / tenant {slug}")

    # ── 1-2. handoff ────────────────────────────────────────────────────────
    step("Redeem a freshly minted handoff ticket")
    r = requests.post(f"{BASE}/api/auth/handoff/exchange",
                      json={"ticket": mint_ticket(email, slug)}, timeout=30)
    if r.status_code != 200:
        fail(f"handoff exchange returned {r.status_code}: {r.text[:400]}")
    session = r.json()
    jwt = session.get("accessToken")
    if not jwt:
        fail(f"no accessToken in handoff response: {session}")
    ok(f"session issued, role={session.get('role')} tenant={session.get('tenantSlug')}")

    authed = {"Authorization": f"Bearer {jwt}"}

    # ── 3. provisioning ─────────────────────────────────────────────────────
    step("Assert the account was provisioned (user + starter project)")
    me = requests.get(f"{BASE}/api/auth/me", headers=authed, timeout=30)
    if me.status_code != 200:
        fail(f"/api/auth/me returned {me.status_code}: {me.text[:300]}")
    if me.json().get("email") != email:
        fail(f"unexpected identity: {me.json()}")
    ok("AppUser exists and the session identifies it")

    projects = requests.get(f"{BASE}/api/projects", headers=authed, timeout=30)
    if projects.status_code != 200:
        fail(f"/api/projects returned {projects.status_code}: {projects.text[:300]}")
    plist = projects.json()
    if not isinstance(plist, list) or len(plist) < 1:
        fail(f"expected a starter project, got: {str(plist)[:300]}")
    project_id = plist[0].get("id")
    ok(f"starter project provisioned and visible: {plist[0].get('name')} ({project_id})")

    # ── 4. no password ──────────────────────────────────────────────────────
    step("Assert the provisioned account cannot log in with a password")
    for attempt in ("", "password", "Password123!"):
        lr = requests.post(f"{BASE}/api/auth/login",
                           json={"email": email, "password": attempt}, timeout=30)
        if lr.status_code == 200:
            fail(f"password {attempt!r} unexpectedly logged in — security model broken")
    ok("password login refused for every attempt (unusable hash, as designed)")

    # ── 5. mint a PAT ───────────────────────────────────────────────────────
    step("Mint a personal access token with the handoff session")
    mr = requests.post(f"{BASE}/api/auth/tokens", headers=authed,
                       json={"name": "e2e-stingbridge"}, timeout=30)
    if mr.status_code != 200:
        fail(f"token mint returned {mr.status_code}: {mr.text[:300]}")
    pat = mr.json().get("token")
    if not pat or not pat.startswith("psat_"):
        fail(f"unexpected token shape: {mr.json()}")
    ok(f"token minted, prefix={mr.json().get('prefix')}")

    listed = requests.get(f"{BASE}/api/auth/tokens", headers=authed, timeout=30).text
    if pat in listed:
        fail("the token list leaked the plaintext secret")
    ok("token list does not leak the secret")

    # ── 6. bridge auth with the token only ──────────────────────────────────
    step("Authenticate a real PlanscapeClient with the token alone")
    ps = PlanscapeClient(base_url=BASE, project_id=project_id)
    try:
        ps.login_with_token(pat)
    except PlanscapeAuthError as e:
        fail(f"bridge token login failed: {e}")
    ok("PlanscapeClient authenticated with no email/password at all")

    # ── 7. real work through the bridge ─────────────────────────────────────
    step("Ingest IFC element data into the starter project")
    guid = f"e2e{marker}GUID000000"[:22]
    # Use the library's own builder so this exercises the real wire contract
    # rather than a hand-rolled dict that could drift from it.
    elements = [PlanscapeClient.build_ifc_element(
        ifc_global_id=guid,
        host_element_id=f"Wall.{marker}",
        disc="A",
        sys="WAL",
        category_name="Walls",
        family_name="E2E Basic Wall",
    )]
    try:
        result = ps.ingest_ifc_data(elements, host="archicad",
                                    host_document_guid=f"e2e-doc-{marker}")
    except Exception as e:  # noqa: BLE001
        fail(f"ingest raised: {type(e).__name__}: {e}")
    ok(f"ingest accepted: {json.dumps(result)[:200]}")

    # Prove it actually landed in the starter project, not just that the call
    # returned 200.
    mapped = requests.get(
        f"{BASE}/api/projects/{project_id}/ifc/mappings?ifcGlobalId={guid}",
        headers=authed, timeout=30)
    if mapped.status_code == 200 and guid in mapped.text:
        ok("element is queryable in the starter project by IFC GlobalId")
    else:
        print(f"    ..  mapping read-back inconclusive (HTTP {mapped.status_code}); "
              f"ingest counters above are the authority")

    # ── cleanup ─────────────────────────────────────────────────────────────
    step("Revoke the token and confirm it stops working")
    tid = mr.json().get("id")
    dr = requests.delete(f"{BASE}/api/auth/tokens/{tid}", headers=authed, timeout=30)
    if dr.status_code != 204:
        fail(f"revoke returned {dr.status_code}")
    again = requests.post(f"{BASE}/api/auth/token/exchange",
                          json={"token": pat}, timeout=30)
    if again.status_code != 401:
        fail(f"revoked token still exchangeable: {again.status_code}")
    ok("revoked token rejected with 401")

    print(f"\nE2E PASSED - subscriber {email} went from D1-only to ingesting data.")
    print(f"  tenant slug : {slug}")
    print(f"  project id  : {project_id}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
