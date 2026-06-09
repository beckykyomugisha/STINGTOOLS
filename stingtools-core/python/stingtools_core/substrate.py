"""Substrate drift-check (Phase A4) — host side.

Every host computes a single SHA-256 over the corporate substrate manifest
(``shared/ifc/enums/_manifest.json``, which itself carries the per-enum
checksums) and compares it against the value the Planscape Server reports for
the project. A mismatch means this host is reading a different (stale or forked)
copy of the enum/pset substrate than the rest of the federation — surfaced as a
warning so coordination doesn't silently run on divergent vocabularies.

The matching server endpoint (``GET /api/substrate/manifest`` returning the same
hash) is the remaining .NET half of A4.
"""

from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Optional

from .paths import find_shared_ifc

MANIFEST_RELATIVE = Path("enums") / "_manifest.json"


def substrate_manifest_path(shared_ifc: Optional[Path] = None) -> Path:
    """Absolute path to the substrate manifest file."""
    base = shared_ifc or find_shared_ifc()
    return base / MANIFEST_RELATIVE


def substrate_manifest_sha256(shared_ifc: Optional[Path] = None) -> str:
    """SHA-256 (hex) of this host's substrate manifest. The cross-host drift key."""
    path = substrate_manifest_path(shared_ifc)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare(local_hash: str, remote_hash: Optional[str]) -> bool:
    """True when the host substrate matches the server's (or the server has none yet)."""
    if not remote_hash:
        return True  # server hasn't pinned a substrate hash — nothing to drift against
    return local_hash == remote_hash
