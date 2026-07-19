"""Tests for transparent re-authentication on token expiry.

A drop-folder watcher logs in once and then runs for days, so the server token
expires long before the process does. These tests pin the behaviour that every
ingest after that point still succeeds, via a fake session — no live server.

Run from the repo root:  python StingBridge/tests/test_auth_refresh.py
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


class _ExpiringSession:
    """Fake session: the token issued by /auth/login is only valid until the
    next login. Any request bearing a superseded token answers 401 — the same
    shape a real expiry presents to the client."""

    def __init__(self, *, logins_allowed=99):
        self.headers = {}
        self.current_token = None
        self.login_count = 0
        self.posts = []          # (url, token_used) for non-login posts
        self._logins_allowed = logins_allowed

    def post(self, url, json=None, timeout=None, **kw):
        if url.endswith("/api/auth/login"):
            self.login_count += 1
            if self.login_count > self._logins_allowed:
                return _Resp(401)
            self.current_token = f"tok-{self.login_count}"
            return _Resp(200, {"token": self.current_token})

        sent = self.headers.get("Authorization", "")
        self.posts.append((url, sent))
        if sent != f"Bearer {self.current_token}":
            return _Resp(401)
        return _Resp(200, {"newMappings": 1, "updatedMappings": 0,
                           "newElements": 1, "updatedElements": 0, "skipped": 0})


def _client(session):
    c = PlanscapeClient(base_url="http://srv", project_id="proj-1")
    c._session = session
    c.login("user@example.com", "pw")
    return c


def _element():
    return PlanscapeClient.build_ifc_element(ifc_global_id="1Abc", disc="M")


def test_ingest_succeeds_after_server_side_expiry():
    """The 401 path: server rejects the token, client re-logs in and retries."""
    s = _ExpiringSession()
    c = _client(s)

    # Simulate the server expiring the issued token mid-run.
    s.current_token = "something-else"
    resp = c.ingest_ifc_data([_element()])

    assert resp["newMappings"] == 1, "ingest must succeed after re-auth"
    assert s.login_count == 2, f"expected exactly one re-login, got {s.login_count - 1}"
    # Two ingest attempts: the rejected one, then the retry with a fresh token.
    assert len(s.posts) == 2
    assert s.posts[-1][1] == f"Bearer {s.current_token}"


def test_local_expiry_refreshes_proactively_without_a_401():
    """The proactive path: the locally tracked expiry has passed, so the client
    refreshes before spending a round-trip on a doomed request."""
    s = _ExpiringSession()
    c = _client(s)
    c._token_expiry = time.time() - 1  # pretend 55 minutes elapsed

    c.ingest_ifc_data([_element()])

    assert s.login_count == 2
    assert len(s.posts) == 1, "proactive refresh should avoid the wasted 401 attempt"


def test_repeated_expiry_keeps_working_over_a_long_run():
    """A watcher processes many files across many expiries."""
    s = _ExpiringSession()
    c = _client(s)
    for i in range(5):
        s.current_token = f"expired-{i}"  # expire before each ingest
        assert c.ingest_ifc_data([_element()])["newMappings"] == 1
    assert s.login_count == 6  # 1 initial + 5 refreshes


def test_auth_error_raised_when_relogin_also_fails():
    """Credentials revoked outright: surface the 401 rather than loop."""
    s = _ExpiringSession(logins_allowed=1)
    c = _client(s)
    s.current_token = "expired"

    try:
        c.ingest_ifc_data([_element()])
        raise AssertionError("should raise when re-auth fails")
    except PlanscapeAuthError:
        pass
    assert s.login_count == 2, "must attempt re-auth exactly once, not spin"


def test_login_stores_credentials_for_refresh():
    s = _ExpiringSession()
    c = _client(s)
    assert c._email == "user@example.com"
    assert c._password == "pw"


def test_client_without_login_does_not_attempt_refresh():
    """ingest before login must still fail fast, not silently re-auth."""
    c = PlanscapeClient(base_url="http://srv", project_id="proj-1")
    c._session = _ExpiringSession()
    try:
        c.ingest_ifc_data([_element()])
        raise AssertionError("should require login")
    except PlanscapeAuthError:
        pass


def test_upload_model_retries_after_expiry(tmp_path=None):
    """upload_model is the other long-run write path — it must refresh too, and
    re-open the file so the retry uploads real bytes rather than an empty read."""
    import tempfile

    class _UploadSession(_ExpiringSession):
        def __init__(self):
            super().__init__()
            self.uploaded_bytes = []
            self.sent_content_types = []

        def post(self, url, json=None, timeout=None, files=None, headers=None, **kw):
            if url.endswith("/api/auth/login"):
                return super().post(url, json=json, timeout=timeout, **kw)
            sent = self.headers.get("Authorization", "")
            self.posts.append((url, sent))
            self.sent_content_types.append((headers or {}).get("Content-Type", "unset"))
            if files:
                self.uploaded_bytes.append(files["file"][1].read())
            if sent != f"Bearer {self.current_token}":
                return _Resp(401)
            return _Resp(200, {"id": "model-1"})

    s = _UploadSession()
    c = _client(s)
    with tempfile.TemporaryDirectory() as d:
        glb = Path(d) / "model.glb"
        glb.write_bytes(b"GLB-PAYLOAD")
        s.current_token = "expired"
        result = c.upload_model("proj-1", glb)

    assert result["id"] == "model-1"
    assert s.login_count == 2
    # Both attempts must carry the full payload — a reused, already-consumed
    # handle would make the retry upload zero bytes.
    assert s.uploaded_bytes == [b"GLB-PAYLOAD", b"GLB-PAYLOAD"]
    # The session-level application/json must be suppressed so requests can set
    # its own multipart boundary.
    assert all(ct is None for ct in s.sent_content_types), s.sent_content_types


if __name__ == "__main__":
    import traceback
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    fails = 0
    for t in tests:
        try:
            t()
            print(f"  OK   {t.__name__}")
        except Exception:
            fails += 1
            print(f"  FAIL {t.__name__}")
            traceback.print_exc()
    print(f"\n{len(tests)} tests, {fails} failures")
    sys.exit(1 if fails else 0)
