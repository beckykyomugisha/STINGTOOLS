"""A push whose access token expired mid-session must self-heal via the refresh
token — not die with a bare 401 that forces a manual re-login."""
from __future__ import annotations

import io
import json
import pathlib
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]  # stingtools-bonsai/
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from planscape.client import PlanscapeClient, PlanscapeError  # noqa: E402


class _Resp:
    def __init__(self, payload): self._p = json.dumps(payload).encode()
    def __enter__(self): return self
    def __exit__(self, *a): return False
    def read(self): return self._p


def _http_error(code):
    return urllib.error.HTTPError("u", code, "err", {}, io.BytesIO(b'{"message":"x"}'))


def test_expired_access_token_refreshes_and_retries():
    """First ingest → 401 (expired). Client refreshes with the stored refresh
    token, gets a new access token, retries → 200. No exception surfaces."""
    calls = {"ingest": 0, "refresh": 0}

    def fake_urlopen(req, timeout=None):
        url = req.full_url
        if url.endswith("/api/auth/refresh"):
            calls["refresh"] += 1
            return _Resp({"accessToken": "fresh-token"})
        if url.endswith("/ifc/data"):
            calls["ingest"] += 1
            if calls["ingest"] == 1:
                raise _http_error(401)          # expired
            # retry must carry the refreshed token
            assert req.get_header("Authorization") == "Bearer fresh-token"
            return _Resp({"newElements": 3})
        raise AssertionError(url)

    orig = urllib.request.urlopen
    urllib.request.urlopen = fake_urlopen
    try:
        c = PlanscapeClient("https://x", token="stale", refresh_token="rt-1")
        resp = c.ingest_ifc("proj", "bonsai", [])
        assert resp["newElements"] == 3
        assert calls == {"ingest": 2, "refresh": 1}, calls
        assert c.token == "fresh-token"
    finally:
        urllib.request.urlopen = orig


def test_401_without_refresh_token_still_raises():
    """No refresh token → the 401 surfaces (nothing to self-heal with)."""
    def fake_urlopen(req, timeout=None):
        raise _http_error(401)
    orig = urllib.request.urlopen
    urllib.request.urlopen = fake_urlopen
    try:
        c = PlanscapeClient("https://x", token="stale")  # no refresh_token
        try:
            c.ingest_ifc("proj", "bonsai", [])
            raise AssertionError("should raise")
        except PlanscapeError as e:
            assert e.status == 401
    finally:
        urllib.request.urlopen = orig


def test_login_captures_refresh_token():
    def fake_urlopen(req, timeout=None):
        return _Resp({"accessToken": "a", "refreshToken": "r", "userName": "Davis"})
    orig = urllib.request.urlopen
    urllib.request.urlopen = fake_urlopen
    try:
        c = PlanscapeClient("https://x")
        c.login("e@x", "pw")
        assert c.refresh_token == "r"
    finally:
        urllib.request.urlopen = orig


if __name__ == "__main__":
    import traceback
    fails = 0
    for n, f in sorted((k, v) for k, v in globals().items() if k.startswith("test_")):
        try:
            f(); print(f"  OK   {n}")
        except Exception:
            fails += 1; print(f"  FAIL {n}"); traceback.print_exc()
    sys.exit(1 if fails else 0)
