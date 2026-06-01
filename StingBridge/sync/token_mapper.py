"""
Maps ArchiCAD element property bags to STING ISO 19650 tokens.

Input:  raw property dict from ArchiCadClient.get_property_values()
Output: token dict ready for PlanscapeClient.build_ifc_element()
"""
from __future__ import annotations

import re
from typing import Any

from ..archicad.element_types import (
    DISC_MAP,
    SYS_MAP,
    infer_disc_from_library_part,
    infer_sys_from_library_part,
)

# ── renovation status → STING STATUS ─────────────────────────────────────────

_RENOV_STATUS_MAP: dict[str, str] = {
    "new":               "NEW",
    "existing":          "EXISTING",
    "demolished":        "DEMOLISHED",
    "tobe demolished":   "DEMOLISHED",
    "to be demolished":  "DEMOLISHED",
    "tobe reconstructed":"NEW",
    "tobeconstructed":   "NEW",
    "temporary":         "TEMPORARY",
}

# ── storey name → STING LVL ───────────────────────────────────────────────────

_GROUND_PATTERNS = re.compile(
    r"ground|gr\.?\s*fl|gf|00|g/f|rez[- ]de[- ]chauss",
    re.IGNORECASE,
)
_BASEMENT_PATTERN = re.compile(r"basement|below|b\.?\s*(\d+)|sous[- ]sol", re.IGNORECASE)
_ROOF_PATTERN = re.compile(r"roof|top|penthouse|attic", re.IGNORECASE)
_FLOOR_DIGIT = re.compile(r"(\d+)", re.IGNORECASE)


def derive_level_code(storey_name: str, elevation_m: float | None = None) -> str:
    """Convert an ArchiCAD storey name / elevation to a STING LVL code."""
    name = (storey_name or "").strip()

    if _ROOF_PATTERN.search(name):
        return "RF"

    if _GROUND_PATTERNS.search(name):
        return "GF"

    m = _BASEMENT_PATTERN.search(name)
    if m:
        n = m.group(1) if m.lastindex else "1"
        return f"B{n}"

    # Try to extract a numeric floor number
    dm = _FLOOR_DIGIT.search(name)
    if dm:
        n = int(dm.group(1))
        return f"L{n:02d}"

    # Fall back to elevation
    if elevation_m is not None:
        if elevation_m < -0.5:
            return "B1"
        if abs(elevation_m) <= 0.5:
            return "GF"
        floor_n = max(1, round(elevation_m / 3.0))
        return f"L{floor_n:02d}"

    return "XX"


# ── zone / building → STING LOC / ZONE ───────────────────────────────────────

def derive_loc(building_name: str | None) -> str:
    """Map a building name to a STING LOC code."""
    if not building_name:
        return "BLD1"
    name = building_name.strip().upper()
    # If name contains a number, use it
    m = re.search(r"\d+", name)
    if m:
        return f"BLD{m.group()}"
    # Single letter suffix: Block A → BLDA
    m = re.search(r"\b([A-Z])\b$", name)
    if m:
        return f"BLD{m.group(1)}"
    return "BLD1"


def derive_zone(zone_name: str | None) -> str:
    """Map a zone/space name to a STING ZONE code."""
    if not zone_name:
        return "ZZ"
    name = zone_name.strip().upper()
    m = re.search(r"\d+", name)
    if m:
        n = int(m.group())
        return f"Z{n:02d}"
    return "ZZ"


# ── main mapper ───────────────────────────────────────────────────────────────

def map_element_to_tokens(
    element_type: str,
    props: dict[str, Any],
    storey_name: str = "",
    storey_elevation_m: float | None = None,
    building_name: str | None = None,
    zone_name: str | None = None,
    library_part_name: str = "",
) -> dict[str, str]:
    """
    Derive STING ISO 19650 tokens from an ArchiCAD element.

    Returns a dict with keys: disc, loc, zone, lvl, sys, func, prod, status
    """
    et = element_type.strip()

    # DISC
    if et == "Object" and library_part_name:
        disc = infer_disc_from_library_part(library_part_name)
    else:
        disc = DISC_MAP.get(et, "A")

    # SYS
    if et == "Object" and library_part_name:
        sys = infer_sys_from_library_part(library_part_name)
    else:
        sys = SYS_MAP.get(et, "GEN")

    # LVL from storey
    lvl = derive_level_code(storey_name, storey_elevation_m)

    # LOC from building
    loc = derive_loc(building_name)

    # ZONE from space/zone
    zone = derive_zone(zone_name)

    # FUNC — derive from SYS (simplified mapping)
    _sys_func_map: dict[str, str] = {
        "HVAC": "SUP",
        "DCW":  "DCW",
        "SAN":  "SAN",
        "DHW":  "DHW",
        "FP":   "FP",
        "LV":   "LV",
        "STR":  "STR",
        "ARC":  "ARC",
        "GEN":  "GEN",
        "ICT":  "ICT",
        "SEC":  "SEC",
    }
    func = _sys_func_map.get(sys, "GEN")

    # PROD — use element type abbreviation
    _type_prod_map: dict[str, str] = {
        "Wall":        "WL",
        "Column":      "COL",
        "Beam":        "BM",
        "Slab":        "SLB",
        "Roof":        "RF",
        "Shell":       "SHL",
        "CurtainWall": "CW",
        "Window":      "WIN",
        "Door":        "DR",
        "Skylight":    "SKL",
        "Stair":       "ST",
        "Railing":     "RL",
        "Opening":     "OPN",
        "Morph":       "MPH",
        "Mesh":        "MSH",
        "Object":      "OBJ",
        "Lamp":        "LMP",
        "Zone":        "ZN",
    }
    prod = _type_prod_map.get(et, "OBJ")

    # STATUS — from AC renovation status property
    status = "NEW"
    for key in (
        "AC_Pset_RenovationInfo.RenovationStatus",
        "Pset_ManufacturerTypeInformation.Status",
        "Status",
    ):
        raw = props.get(key, "")
        if raw:
            cleaned = raw.strip(".").strip().lower()
            status = _RENOV_STATUS_MAP.get(cleaned, "NEW")
            break

    return {
        "disc": disc,
        "loc":  loc,
        "zone": zone,
        "lvl":  lvl,
        "sys":  sys,
        "func": func,
        "prod": prod,
        "status": status,
    }
