"""Tests for personal-access-token authentication.

A PAT is the only credential an account provisioned through the planscape.build
identity handoff can use: those accounts are created with a deliberately
unusable password hash, so there is no password to give the bridge.

These use a fake session — no live server.

Run from the repo root:  python StingBridge/tests/test_token_auth.py
(or via pytest).
"""
from __future__ import annotations

import sys
import time
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.planscape.client import PlanscapeClient, PlanscapeAuthError  # noqa: E402


class _Resp:
    def __init__(self, status_code=200, payload=None):
        self.status_code = status_code
        self._payload = payload if payload is not None else {}

    def raise_for_status(self):
        if self.status_code >= 400:
            raise AssertionError(f"unexpected raise_for_status on {self.status_code}")

    def json(self):
        return self._payload


class _TokenSession:
    """Fake session where /api/auth/token/exchange trades a known PAT for a
    session token. Requests bearing a superseded token answer 401, which is
    what a real expiry looks like to the client."""

    VALID_PAT = "psat_valid-token-value"

    def __init__(self, *, exchange_status=200, exchanges_allowed=99):
        self.headers = {}
        self.current_token = None
        self.exchange_count = 0
        self.login_count = 0
        self.exchanged_bodies = []
        self.posts = []
        self._exchange_status = exchange_status
        self._exchanges_allowed = exchanges_allowed

    def post(self, url, json=None, timeout=None, **kw):
        if url.endswith("/api/auth/token/exchange"):
            self.exchange_count += 1
            self.exchanged_bodies.append(json)
            if self._exchange_status != 200:
                return _Resp(self._exchange_status)
            if self.exchange_count > self._exchanges_allowed:
                return _Resp(401)
            if (json or {}).get("token") != self.VALID_PAT:
                return _Resp(401)
            self.current_token = f"sess-{self.exchange_count}"
            return _Resp(200, {"accessToken": self.current_token})

        if url.endswith("/api/auth/login"):
            self.login_count += 1
            self.current_token = f"pwsess-{self.login_count}"
            return _Resp(200, {"accessToken": self.current_token})

        sent = self.headers.get("Authorization", "")
        self.posts.append((url, sent))
        if sent != f"Bearer {self.current_token}":
            return _Resp(401)
        return _Resp(200, {"ok": True})

    def get(self, url, **kw):
        sent = self.headers.get("Authorization", "")
        if sent != f"Bearer {self.current_token}":
            return _Resp(401)
        return _Resp(200, {"ok": True})


def _client(session):
    c = PlanscapeClient(base_url="http://fake", project_id="p1")
    c._session = session
    return c


# ── exchange ─────────────────────────────────────────────────────────────────

def test_login_with_token_sets_bearer_header():
    s = _TokenSession()
    c = _client(s)
    c.login_with_token(_TokenSession.VALID_PAT)

    assert s.exchange_count == 1
    assert s.headers["Authorization"] == "Bearer sess-1"
    # The PAT itself must never become the bearer token.
    assert _TokenSession.VALID_PAT not in s.headers["Authorization"]


def test_login_with_token_sends_pat_in_body_only():
    s = _TokenSession()
    _client(s).login_with_token(_TokenSession.VALID_PAT)
    assert s.exchanged_bodies == [{"token": _TokenSession.VALID_PAT}]


def test_login_with_token_strips_whitespace():
    # Copy-paste from a web UI very often brings a trailing newline.
    s = _TokenSession()
    _client(s).login_with_token(f"  {_TokenSession.VALID_PAT}\n")
    assert s.exchanged_bodies == [{"token": _TokenSession.VALID_PAT}]


def test_empty_token_raises_without_calling_the_server():
    s = _TokenSession()
    c = _client(s)
    try:
        c.login_with_token("   ")
    except PlanscapeAuthError:
        pass
    else:
        raise AssertionError("expected PlanscapeAuthError")
    assert s.exchange_count == 0


def test_rejected_token_raises_auth_error():
    s = _TokenSession()
    c = _client(s)
    try:
        c.login_with_token("psat_wrong")
    except PlanscapeAuthError as e:
        assert "revoked" in str(e) or "rejected" in str(e)
    else:
        raise AssertionError("expected PlanscapeAuthError")


def test_old_server_without_the_endpoint_gives_an_actionable_message():
    s = _TokenSession(exchange_status=404)
    c = _client(s)
    try:
        c.login_with_token(_TokenSession.VALID_PAT)
    except PlanscapeAuthError as e:
        # Must name the fallback, not just surface a bare 404.
        assert "STING_PLANSCAPE_EMAIL" in str(e)
    else:
        raise AssertionError("expected PlanscapeAuthError")


# ── re-auth ──────────────────────────────────────────────────────────────────

def test_expired_session_is_refreshed_via_the_pat_not_a_password():
    s = _TokenSession()
    c = _client(s)
    c.login_with_token(_TokenSession.VALID_PAT)

    # Force the proactive-refresh path.
    c._token_expiry = time.time() - 1
    resp = c._send("post", "http://fake/api/anything", json={})

    assert resp.status_code == 200
    assert s.exchange_count == 2      # refreshed by exchanging the PAT again
    assert s.login_count == 0         # and never touched password login


def test_reactive_401_is_retried_once_via_the_pat():
    s = _TokenSession()
    c = _client(s)
    c.login_with_token(_TokenSession.VALID_PAT)

    # Server-side revocation: the held token stops working without local expiry.
    s.current_token = "something-else"
    resp = c._send("post", "http://fake/api/anything", json={})

    assert resp.status_code == 200
    assert s.exchange_count == 2
    assert s.login_count == 0


def test_relogin_returns_false_when_no_credential_is_held():
    c = _client(_TokenSession())
    assert c._relogin() is False


# ── credential separation ────────────────────────────────────────────────────

def test_token_login_clears_any_password_credentials():
    s = _TokenSession()
    c = _client(s)
    c.login("someone@example.com", "hunter2")
    assert c._email == "someone@example.com"

    c.login_with_token(_TokenSession.VALID_PAT)
    # The two paths must never be mixed: after switching to a PAT, a refresh
    # must not silently fall back to a stale password.
    assert c._email == ""
    assert c._password == ""
    assert c._pat == _TokenSession.VALID_PAT


def test_password_login_clears_any_token():
    s = _TokenSession()
    c = _client(s)
    c.login_with_token(_TokenSession.VALID_PAT)
    c.login("someone@example.com", "hunter2")
    assert c._pat == ""


# ── expiry window ────────────────────────────────────────────────────────────

def test_token_expiry_matches_the_servers_30_minute_lifetime():
    # Regression guard: this used to assume a 60-minute token against a server
    # issuing 30-minute ones, so the proactive refresh only fired ~25 minutes
    # after the token was already dead.
    s = _TokenSession()
    c = _client(s)

    before = time.time()
    c.login_with_token(_TokenSession.VALID_PAT)
    window = c._token_expiry - before

    assert 20 * 60 <= window <= 26 * 60, f"refresh window {window/60:.1f} min is wrong"


if __name__ == "__main__":
    passed = failed = 0
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            try:
                fn()
                passed += 1
                print(f"  PASS  {name}")
            except Exception as e:  # noqa: BLE001
                failed += 1
                print(f"  FAIL  {name}: {e}")
    print(f"\n{passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)
