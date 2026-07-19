"""Make ``stingtools_core`` importable, however this checkout is arranged.

StingBridge depends on core for the wire contract (Drift 1 / Drift 5) and, since
Phase 205, for token inference too (SB-3). Core is normally pip-installed, but in
the monorepo dev checkout it is a sibling source tree that was never installed —
so a bare ``import stingtools_core`` fails there.

``client.py`` already carried this fallback inline for one symbol. Factoring it
out means the path fix lives in exactly one place instead of being re-pasted
every time another module needs core.

Usage:

    from .._core import archicad as _core     # or: from ._core import archicad
"""
from __future__ import annotations

import sys
from pathlib import Path


def _ensure_importable() -> None:
    try:
        import stingtools_core  # noqa: F401
        return
    except ImportError:
        pass
    # repo root = StingBridge/_core.py -> parents[1]
    core_src = Path(__file__).resolve().parents[1] / "stingtools-core" / "python"
    if core_src.is_dir() and str(core_src) not in sys.path:
        sys.path.insert(0, str(core_src))


_ensure_importable()

# Re-exported so callers can write `from .._core import archicad`.
from stingtools_core.hosts import archicad, inference  # noqa: E402

__all__ = ["archicad", "inference"]
