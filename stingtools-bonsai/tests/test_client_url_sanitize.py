"""Regression: a Project ID pasted with stray whitespace must not crash the push.

Copying the project GUID out of a table/URL often prepends a TAB. Before the
fix, that reached urllib as "/api/projects/\t<guid>/ifc/data" and died with
http.client.InvalidURL ("URL can't contain control characters"). _request now
strips raw control chars from the URL, so any field (Project ID, Server URL)
carrying a stray tab/space still produces a valid request.
"""
from __future__ import annotations

import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]  # stingtools-bonsai/
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from planscape.client import PlanscapeClient  # noqa: E402


class _CaptureResp:
    def __enter__(self): return self
    def __exit__(self, *a): return False
    def read(self): return b"{}"


def _client_capturing(monkeypatch_holder):
    """Return (client, holder) where holder['url'] is the URL urllib received."""
    holder = {}

    def fake_urlopen(req, timeout=None):
        holder["url"] = req.full_url
        return _CaptureResp()

    urllib.request.urlopen = fake_urlopen  # module-level swap; restored per-test
    c = PlanscapeClient("https://planscape-api-free.onrender.com", token="tok")
    return c, holder


def test_tab_in_project_id_does_not_crash_and_url_is_clean():
    orig = urllib.request.urlopen
    try:
        c, holder = _client_capturing(orig)
        # The exact failure: a leading TAB on the Project ID.
        c.ingest_ifc("\t2e4a5fb9-a65d-4062-bc8e-a5e53e8cb462", "bonsai", [])
        url = holder["url"]
        assert "\t" not in url, f"tab leaked into URL: {url!r}"
        assert url == (
            "https://planscape-api-free.onrender.com"
            "/api/projects/2e4a5fb9-a65d-4062-bc8e-a5e53e8cb462/ifc/data"
        ), url
    finally:
        urllib.request.urlopen = orig


def test_whitespace_in_server_url_is_stripped():
    orig = urllib.request.urlopen
    try:
        holder = {}
        def fake(req, timeout=None):
            holder["url"] = req.full_url
            return _CaptureResp()
        urllib.request.urlopen = fake
        # Trailing spaces / newline on the server URL (a common paste artifact).
        c = PlanscapeClient("  https://planscape-api-free.onrender.com\n ", token="t")
        c.ingest_ifc("abc", "bonsai", [])
        assert holder["url"] == "https://planscape-api-free.onrender.com/api/projects/abc/ifc/data"
    finally:
        urllib.request.urlopen = orig


if __name__ == "__main__":
    import traceback
    fails = 0
    for name, fn in sorted((k, v) for k, v in globals().items() if k.startswith("test_")):
        try:
            fn(); print(f"  OK   {name}")
        except Exception:
            fails += 1; print(f"  FAIL {name}"); traceback.print_exc()
    sys.exit(1 if fails else 0)
