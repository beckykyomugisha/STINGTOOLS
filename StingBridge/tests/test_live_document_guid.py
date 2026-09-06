"""SB-1b — the live ArchiCAD sync sends a stable, non-null HostDocumentGuid.

A live session is one document per project, so a constant (scoped per-project by
the ProjectId already in the mapping key) is the right identity. Null left the
cross-host mapping's unique index unable to enforce for live rows (Postgres
treats NULLs as distinct); a stable value closes that. It must be non-empty and
fit the HostDocumentGuid column (<=64 chars).

Run from the repo root:  python StingBridge/tests/test_live_document_guid.py
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.sync.engine import LIVE_DOCUMENT_GUID  # noqa: E402


def test_live_document_guid_is_a_stable_nonempty_id():
    assert isinstance(LIVE_DOCUMENT_GUID, str)
    assert LIVE_DOCUMENT_GUID.strip(), "must be non-empty — null is exactly what SB-1b removes"
    assert len(LIVE_DOCUMENT_GUID) <= 64, "must fit the HostDocumentGuid column"
    # Stable constant, not a path/hash — the live doc identity must not change when
    # the .pln is moved or renamed (unlike the IFC-drop path's per-file id).
    assert LIVE_DOCUMENT_GUID == "archicad-live"


if __name__ == "__main__":
    test_live_document_guid_is_a_stable_nonempty_id()
    print("OK")
