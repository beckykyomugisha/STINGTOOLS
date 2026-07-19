"""ArchiCAD element vocabulary → STING token inference.

Pure, host-agnostic rules for the ArchiCAD side, moved here in Phase 205 from
``StingBridge/archicad/element_types.py`` + ``StingBridge/sync/token_mapper.py``
so core owns every host vocabulary in one place. The bridge now imports from
here; those modules are back-compat shims.

Note the input domain: this maps **ArchiCAD JSON-API type strings** ("Wall",
"Object", "Lamp") and library-part names. ``inference.py`` maps **IFC classes**
("IfcWall"). They are two vocabularies for the same target grammar, not
duplicates — but level derivation IS the same question, so both call
``inference.level_for_storey_name``.

Boundary rule (Phase A6): adapters must NOT re-implement these — they call here.
"""

from __future__ import annotations

import re
from typing import Any

from .inference import level_for_storey_name


# All element types the JSON API accepts for GetElementsByType
ALL_ELEMENT_TYPES = [
    "Wall", "Column", "Beam", "Window", "Door", "Object", "Lamp",
    "Slab", "Roof", "Mesh", "Dimension", "RadialDimension",
    "LevelDimension", "AngleDimension", "Text", "Label",
    "Zone", "Hatch", "Line", "PolyLine", "Arc", "Circle",
    "Spline", "Hotspot", "CutPlane", "Camera", "CamSet",
    "DrawingElement", "Detail", "Elevation", "InteriorElevation",
    "Worksheet", "Hotlink", "Morph", "Stair", "Railing",
    "Opening", "Skylight", "CurtainWall", "Shell",
]

# Types we actually want to sync to Planscape (physical building elements)
SYNCABLE_TYPES = [
    "Wall", "Column", "Beam", "Window", "Door", "Object", "Lamp",
    "Slab", "Roof", "Mesh", "Zone", "Morph", "Stair", "Railing",
    "Opening", "Skylight", "CurtainWall", "Shell",
]

# ArchiCAD element type → STING DISC code
DISC_MAP: dict[str, str] = {
    "Wall":          "A",
    "Column":        "S",
    "Beam":          "S",
    "Slab":          "S",
    "Roof":          "A",
    "Shell":         "A",
    "CurtainWall":   "A",
    "Window":        "A",
    "Door":          "A",
    "Skylight":      "A",
    "Stair":         "A",
    "Railing":       "A",
    "Opening":       "A",
    "Morph":         "A",
    "Mesh":          "A",
    "Object":        "A",   # overridden by library part name if MEP/electrical
    "Lamp":          "E",
    "Zone":          "A",
}

# ArchiCAD element type → STING SYS code (best-effort default)
SYS_MAP: dict[str, str] = {
    "Wall":        "ARC",
    "Column":      "STR",
    "Beam":        "STR",
    "Slab":        "STR",
    "Roof":        "ARC",
    "Shell":       "ARC",
    "CurtainWall": "ARC",
    "Window":      "ARC",
    "Door":        "ARC",
    "Skylight":    "ARC",
    "Stair":       "ARC",
    "Railing":     "ARC",
    "Opening":     "ARC",
    "Morph":       "GEN",
    "Mesh":        "GEN",
    "Object":      "GEN",
    "Lamp":        "LV",
    "Zone":        "ARC",
}

# Library-part name fragments that indicate MEP/electrical objects
# (overrides Object → A to the correct discipline)
OBJECT_DISC_HINTS: list[tuple[str, str]] = [
    # fragment (lowercase)       DISC
    ("hvac",                     "M"),
    ("duct",                     "M"),
    ("air terminal",             "M"),
    ("fan",                      "M"),
    ("chiller",                  "M"),
    ("boiler",                   "M"),
    ("radiator",                 "M"),
    ("valve",                    "M"),
    ("pipe",                     "P"),
    ("sanitary",                 "P"),
    ("toilet",                   "P"),
    ("basin",                    "P"),
    ("sink",                     "P"),
    ("urinal",                   "P"),
    ("shower",                   "P"),
    ("bathtub",                  "P"),
    ("water heater",             "P"),
    ("pump",                     "P"),
    ("fire",                     "FP"),
    ("sprinkler",                "FP"),
    ("panel",                    "E"),
    ("socket",                   "E"),
    ("switch",                   "E"),
    ("light",                    "E"),
    ("luminaire",                "E"),
    ("lamp",                     "E"),
    ("distribution board",       "E"),
    ("switchgear",               "E"),
    ("cable tray",               "E"),
    ("conduit",                  "E"),
    ("cctv",                     "LV"),
    ("access control",           "LV"),
    ("data",                     "LV"),
    ("telephone",                "LV"),
]

OBJECT_SYS_HINTS: list[tuple[str, str]] = [
    ("hvac",           "HVAC"),
    ("duct",           "HVAC"),
    ("air terminal",   "HVAC"),
    ("fan",            "HVAC"),
    ("chiller",        "HVAC"),
    ("boiler",         "HVAC"),
    ("radiator",       "HVAC"),
    ("pipe",           "DCW"),
    ("sanitary",       "SAN"),
    ("toilet",         "SAN"),
    ("basin",          "SAN"),
    ("sink",           "SAN"),
    ("urinal",         "SAN"),
    ("shower",         "SAN"),
    ("bathtub",        "SAN"),
    ("water heater",   "DHW"),
    ("pump",           "DCW"),
    ("fire",           "FP"),
    ("sprinkler",      "FP"),
    ("panel",          "LV"),
    ("socket",         "LV"),
    ("switch",         "LV"),
    ("light",          "LV"),
    ("luminaire",      "LV"),
    ("lamp",           "LV"),
    ("distribution",   "LV"),
    ("switchgear",     "LV"),
    ("cctv",           "SEC"),
    ("access control", "SEC"),
    ("data",           "ICT"),
    ("telephone",      "ICT"),
]


def infer_disc_from_library_part(library_part_name: str) -> str:
    lp = library_part_name.lower()
    for fragment, disc in OBJECT_DISC_HINTS:
        if fragment in lp:
            return disc
    return "A"


def infer_sys_from_library_part(library_part_name: str) -> str:
    lp = library_part_name.lower()
    for fragment, sys in OBJECT_SYS_HINTS:
        if fragment in lp:
            return sys
    return "GEN"


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
    lvl = level_for_storey_name(storey_name, storey_elevation_m)

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
