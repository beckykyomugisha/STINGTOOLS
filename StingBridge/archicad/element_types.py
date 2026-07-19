"""ArchiCAD element type constants and their STING discipline/system mappings.

BACK-COMPAT SHIM (Phase 205, SB-3). The tables and helpers now live in
``stingtools_core.hosts.archicad`` so core owns every host vocabulary in one
place — the same drift class the wire contract hit after Drift 5. Everything is
re-exported here so existing imports keep working; add new rules in core, not
here.
"""
from __future__ import annotations

from .._core import archicad as _core

# Element type lists
ALL_ELEMENT_TYPES = _core.ALL_ELEMENT_TYPES
SYNCABLE_TYPES = _core.SYNCABLE_TYPES

# Vocabulary tables
DISC_MAP = _core.DISC_MAP
SYS_MAP = _core.SYS_MAP
OBJECT_DISC_HINTS = _core.OBJECT_DISC_HINTS
OBJECT_SYS_HINTS = _core.OBJECT_SYS_HINTS

# Helpers
infer_disc_from_library_part = _core.infer_disc_from_library_part
infer_sys_from_library_part = _core.infer_sys_from_library_part

__all__ = [
    "ALL_ELEMENT_TYPES", "SYNCABLE_TYPES",
    "DISC_MAP", "SYS_MAP", "OBJECT_DISC_HINTS", "OBJECT_SYS_HINTS",
    "infer_disc_from_library_part", "infer_sys_from_library_part",
]
