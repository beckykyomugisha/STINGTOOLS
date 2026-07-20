"""Maps ArchiCAD element property bags to STING ISO 19650 tokens.

BACK-COMPAT SHIM (Phase 205, SB-3). The rules now live in
``stingtools_core.hosts.archicad`` — and level derivation, which the IFC path
and this one had drifted into answering differently, lives in
``stingtools_core.hosts.inference``. Everything is re-exported so existing
imports keep working; add new rules in core, not here.

Input:  raw property dict from ArchiCadClient.get_property_values()
Output: token dict ready for PlanscapeClient.build_ifc_element()
"""
from __future__ import annotations

from .._core import archicad as _core, inference as _inference

# The main mapper.
map_element_to_tokens = _core.map_element_to_tokens

# LOC / ZONE derivation (ArchiCAD-specific).
derive_loc = _core.derive_loc
derive_zone = _core.derive_zone

# Level derivation is NOT ArchiCAD-specific — the IFC path asks the same
# question. Both now call the one implementation in core. Kept under its old
# name here for callers that imported it directly.
derive_level_code = _inference.level_for_storey_name

__all__ = [
    "map_element_to_tokens",
    "derive_loc",
    "derive_zone",
    "derive_level_code",
]
