"""
ArchiCAD element type constants and their STING discipline/system mappings.

Source: ArchiCAD JSON API type strings (used in GetElementsByType).
"""
from __future__ import annotations

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
