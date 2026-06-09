"""Phase A4 — substrate drift-check (host side)."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from stingtools_core import substrate


def test_manifest_hash_is_stable_hex():
    h = substrate.substrate_manifest_sha256()
    assert isinstance(h, str) and len(h) == 64
    assert h == substrate.substrate_manifest_sha256()  # deterministic


def test_compare_matches_and_mismatches():
    h = substrate.substrate_manifest_sha256()
    assert substrate.compare(h, h) is True
    assert substrate.compare(h, "deadbeef") is False
    # No server hash yet → treat as no-drift (nothing to compare against).
    assert substrate.compare(h, None) is True
    assert substrate.compare(h, "") is True
