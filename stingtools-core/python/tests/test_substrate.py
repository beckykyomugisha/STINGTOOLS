"""Phase A4 — substrate drift-check (host side)."""

from __future__ import annotations

import hashlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from stingtools_core import substrate
from stingtools_core.planscape.client import check_substrate_drift


def test_manifest_hash_is_stable_hex():
    h = substrate.substrate_manifest_sha256()
    assert isinstance(h, str) and len(h) == 64
    assert h == substrate.substrate_manifest_sha256()  # deterministic


def test_manifest_hash_is_newline_normalized():
    """The hash must be identical whether the on-disk manifest is CRLF or LF.

    Guards the cross-platform drift bug: a Windows (Bonsai) checkout with CRLF
    must hash the same as a Linux (server) checkout with LF, or the two halves
    drift forever on byte-identical content.
    """
    raw = substrate.substrate_manifest_path().read_bytes()
    lf = raw.replace(b"\r\n", b"\n")
    crlf = lf.replace(b"\n", b"\r\n")
    assert hashlib.sha256(substrate._normalize_newlines(crlf)).hexdigest() \
        == hashlib.sha256(substrate._normalize_newlines(lf)).hexdigest() \
        == substrate.substrate_manifest_sha256()


def test_compare_matches_and_mismatches():
    h = substrate.substrate_manifest_sha256()
    assert substrate.compare(h, h) is True
    assert substrate.compare(h, "deadbeef") is False
    # No server hash yet → treat as no-drift (nothing to compare against).
    assert substrate.compare(h, None) is True
    assert substrate.compare(h, "") is True


# ----------------------------------------------------------------------
# check_substrate_drift — mocked client (Phase A4-V)
# ----------------------------------------------------------------------

class _FakeClient:
    """Duck-typed Planscape client returning a canned substrate manifest."""

    def __init__(self, response=None, raises: Exception | None = None):
        self._response = response
        self._raises = raises

    def get_substrate_manifest(self) -> dict:
        if self._raises is not None:
            raise self._raises
        return self._response


def test_drift_ok_when_hashes_match():
    local = substrate.substrate_manifest_sha256()
    client = _FakeClient({"sha256": local, "schemaVersion": 2, "totalEnums": 52})
    ok, msg = check_substrate_drift(client)
    assert ok is True
    assert "in sync" in msg


def test_drift_detected_when_hashes_differ():
    client = _FakeClient({"sha256": "0" * 64, "schemaVersion": 2, "totalEnums": 52})
    ok, msg = check_substrate_drift(client)
    assert ok is False
    assert "DRIFT" in msg


def test_drift_ok_when_server_has_no_hash():
    # Server hasn't pinned a substrate yet → nothing to drift against.
    ok, msg = check_substrate_drift(_FakeClient({"sha256": None}))
    assert ok is True


def test_drift_never_blocks_on_transport_error():
    # A network hiccup must not stop login.
    ok, msg = check_substrate_drift(_FakeClient(raises=RuntimeError("boom")))
    assert ok is True
    assert "skipped" in msg
