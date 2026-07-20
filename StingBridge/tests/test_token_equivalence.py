"""Equivalence tests for the token-inference move to core (SB-3, Phase 205).

The refactor moved the ArchiCAD vocabulary out of StingBridge into
``stingtools_core.hosts.archicad``, and merged two divergent level-derivation
functions into one. The risk in any such move is silent output drift, so these
pin the token output for a fixture matrix covering every element type and a
spread of library-part names.

Three of the fixtures assert DELIBERATE changes (the basement-collision bug and
`"0"` → GF). They are called out individually below — if you are reading this
because one failed, check you meant to change level derivation.

Run from the repo root:  python StingBridge/tests/test_token_equivalence.py
(or via pytest).
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

# Import through the SHIMS — that is the path existing callers use, so this also
# proves the shims re-export correctly.
from StingBridge.sync.token_mapper import (  # noqa: E402
    map_element_to_tokens, derive_level_code, derive_loc, derive_zone,
)
from StingBridge.archicad.element_types import (  # noqa: E402
    DISC_MAP, SYS_MAP, SYNCABLE_TYPES,
    infer_disc_from_library_part, infer_sys_from_library_part,
)


# ── the shims still expose everything ────────────────────────────────────────

def test_shims_reexport_the_vocabulary():
    assert len(SYNCABLE_TYPES) == 18
    assert DISC_MAP["Wall"] == "A"
    assert DISC_MAP["Lamp"] == "E"
    assert SYS_MAP["Column"] == "STR"


def test_shim_and_core_are_the_same_objects():
    # Not merely equal — the same object, so there is genuinely one table.
    sys.path.insert(0, str(_REPO_ROOT / "stingtools-core" / "python"))
    from stingtools_core.hosts import archicad as core
    assert DISC_MAP is core.DISC_MAP
    assert SYS_MAP is core.SYS_MAP
    assert map_element_to_tokens is core.map_element_to_tokens


# ── full element-type matrix: token output must not drift ────────────────────

# Captured from the pre-refactor implementation. Every syncable ArchiCAD type,
# with a fixed storey/building/zone so only the type varies.
_EXPECTED_BY_TYPE = {
    "Wall":        ("A", "ARC", "ARC", "WL"),
    "Column":      ("S", "STR", "STR", "COL"),
    "Beam":        ("S", "STR", "STR", "BM"),
    "Window":      ("A", "ARC", "ARC", "WIN"),
    "Door":        ("A", "ARC", "ARC", "DR"),
    "Object":      ("A", "GEN", "GEN", "OBJ"),
    "Lamp":        ("E", "LV",  "LV",  "LMP"),
    "Slab":        ("S", "STR", "STR", "SLB"),
    "Roof":        ("A", "ARC", "ARC", "RF"),
    "Mesh":        ("A", "GEN", "GEN", "MSH"),
    "Zone":        ("A", "ARC", "ARC", "ZN"),
    "Morph":       ("A", "GEN", "GEN", "MPH"),
    "Stair":       ("A", "ARC", "ARC", "ST"),
    "Railing":     ("A", "ARC", "ARC", "RL"),
    "Opening":     ("A", "ARC", "ARC", "OPN"),
    "Skylight":    ("A", "ARC", "ARC", "SKL"),
    "CurtainWall": ("A", "ARC", "ARC", "CW"),
    "Shell":       ("A", "ARC", "ARC", "SHL"),
}


def test_every_syncable_type_produces_the_expected_tokens():
    assert set(_EXPECTED_BY_TYPE) == set(SYNCABLE_TYPES), \
        "fixture matrix and SYNCABLE_TYPES have diverged"

    for et, (disc, sys_, func, prod) in _EXPECTED_BY_TYPE.items():
        t = map_element_to_tokens(
            element_type=et, props={},
            storey_name="Level 2", storey_elevation_m=6.0,
            building_name="Block 3", zone_name="Zone 7",
        )
        assert t["disc"] == disc, f"{et}: disc {t['disc']} != {disc}"
        assert t["sys"] == sys_,  f"{et}: sys {t['sys']} != {sys_}"
        assert t["func"] == func, f"{et}: func {t['func']} != {func}"
        assert t["prod"] == prod, f"{et}: prod {t['prod']} != {prod}"
        # Spatial tokens are type-independent.
        assert t["lvl"] == "L02"
        assert t["loc"] == "BLD3"
        assert t["zone"] == "Z07"


def test_unknown_element_type_falls_back_the_same_way():
    t = map_element_to_tokens("SomethingNew", {}, "Level 1")
    assert (t["disc"], t["sys"], t["func"], t["prod"]) == ("A", "GEN", "GEN", "OBJ")


# ── library-part inference matrix ────────────────────────────────────────────

_LIB_PART_CASES = [
    ("HVAC Air Terminal 01",     "M",  "HVAC"),
    ("Round Duct Segment",       "M",  "HVAC"),
    ("Centrifugal Fan",          "M",  "HVAC"),
    ("Air Cooled Chiller",       "M",  "HVAC"),
    ("Gas Boiler 24kW",          "M",  "HVAC"),
    ("Panel Radiator",           "M",  "HVAC"),
    ("Copper Pipe 22mm",         "P",  "DCW"),
    ("Sanitary WC Pan",          "P",  "SAN"),
    ("Wash Basin",               "P",  "SAN"),
    ("Kitchen Sink",             "P",  "SAN"),
    ("Urinal Bowl",              "P",  "SAN"),
    ("Shower Tray",              "P",  "SAN"),
    ("Bathtub 1700",             "P",  "SAN"),
    ("Water Heater 200L",        "P",  "DHW"),
    ("Circulation Pump",         "P",  "DCW"),
    ("Fire Extinguisher",        "FP", "FP"),
    ("Sprinkler Head Pendant",   "FP", "FP"),
    ("Distribution Board 12way", "E",  "LV"),
    ("Double Socket Outlet",     "E",  "LV"),
    ("Light Switch 1G",          "E",  "LV"),
    ("LED Luminaire 600x600",    "E",  "LV"),
    ("Cable Tray 300mm",         "E",  "GEN"),
    ("Conduit 20mm",             "E",  "GEN"),
    ("CCTV Dome Camera",         "LV", "SEC"),
    ("Access Control Reader",    "LV", "SEC"),
    ("Data Outlet RJ45",         "LV", "ICT"),
    ("Telephone Point",          "LV", "ICT"),
    ("Plain Furniture Chair",    "A",  "GEN"),
    ("",                         "A",  "GEN"),
]


def test_library_part_inference_matrix():
    for name, disc, sys_ in _LIB_PART_CASES:
        assert infer_disc_from_library_part(name) == disc, f"{name!r} disc"
        assert infer_sys_from_library_part(name) == sys_, f"{name!r} sys"


def test_object_type_uses_library_part_inference():
    # An "Object" is discipline-A by default; the library-part name overrides it.
    t = map_element_to_tokens("Object", {}, "Level 1",
                              library_part_name="LED Luminaire 600x600")
    assert t["disc"] == "E"
    assert t["sys"] == "LV"

    plain = map_element_to_tokens("Object", {}, "Level 1", library_part_name="Chair")
    assert plain["disc"] == "A"
    assert plain["sys"] == "GEN"


def test_non_object_types_ignore_the_library_part_name():
    # A Wall stays architectural even if its library part mentions a pump.
    t = map_element_to_tokens("Wall", {}, "Level 1", library_part_name="Pump Housing")
    assert t["disc"] == "A"


# ── LOC / ZONE ───────────────────────────────────────────────────────────────

def test_loc_derivation_matrix():
    assert derive_loc(None) == "BLD1"
    assert derive_loc("") == "BLD1"
    assert derive_loc("Block 2") == "BLD2"
    assert derive_loc("Building 12") == "BLD12"
    assert derive_loc("Block A") == "BLDA"
    assert derive_loc("Main") == "BLD1"


def test_zone_derivation_matrix():
    assert derive_zone(None) == "ZZ"
    assert derive_zone("") == "ZZ"
    assert derive_zone("Zone 3") == "Z03"
    assert derive_zone("Office 12") == "Z12"
    assert derive_zone("Lobby") == "ZZ"


# ── STATUS ───────────────────────────────────────────────────────────────────

def test_renovation_status_matrix():
    def status(raw):
        return map_element_to_tokens(
            "Wall", {"AC_Pset_RenovationInfo.RenovationStatus": raw}, "Level 1"
        )["status"]

    assert status("New") == "NEW"
    assert status("Existing") == "EXISTING"
    assert status("Demolished") == "DEMOLISHED"
    assert status("To Be Demolished") == "DEMOLISHED"
    assert status("Temporary") == "TEMPORARY"
    assert status("Nonsense") == "NEW"
    assert map_element_to_tokens("Wall", {}, "Level 1")["status"] == "NEW"


# ── level derivation ─────────────────────────────────────────────────────────

def test_level_derivation_is_unchanged_for_the_cases_that_already_worked():
    unchanged = {
        "Ground Floor": "GF", "GF": "GF", "G/F": "GF",
        "Level 1": "L01", "Level 01": "L01", "1st Floor": "L01",
        "Floor 2": "L02", "Level 10": "L10", "L05": "L05",
        "Basement": "B1", "B1": "B1", "B2": "B2",
        "Roof": "RF", "Rooftop": "RF", "Penthouse": "RF", "Attic": "RF", "Top": "RF",
        "Rez-de-chaussee": "GF", "Sous-sol": "B1",
        "Unnamed": "XX", "Storey": "XX",
    }
    for name, expected in unchanged.items():
        assert derive_level_code(name) == expected, f"{name!r} drifted"


def test_level_derivation_from_elevation_is_unchanged():
    assert derive_level_code("", 0.0) == "GF"
    assert derive_level_code("", 0.2) == "GF"
    assert derive_level_code("", -3.0) == "B1"
    assert derive_level_code("", 3.0) == "L01"
    assert derive_level_code("", 6.0) == "L02"


def test_numbered_basements_no_longer_collide():
    # DELIBERATE CHANGE. The old ArchiCAD regex put the bare word "basement"
    # before the digit group, so it matched first and every numbered basement
    # fell through to the B1 default — "Basement 2" was silently tagged as
    # Basement 1.
    assert derive_level_code("Basement 2") == "B2"
    assert derive_level_code("Basement 3") == "B3"
    assert derive_level_code("Basement") == "B1"      # unnumbered still B1


def test_storey_named_zero_is_the_ground_floor():
    # DELIBERATE CHANGE: previously "L00", which is not a level anyone means.
    assert derive_level_code("0") == "GF"


def test_names_that_used_to_be_unknown_now_resolve():
    # Coverage the IFC path had and the ArchiCAD path did not. XX means
    # "unknown", so XX -> a real code is a gain, never a regression.
    assert derive_level_code("Mezzanine") == "MZ"
    assert derive_level_code("Plant Room") == "PR"
    assert derive_level_code("Plant") == "PR"


def test_roof_wins_over_a_digit_in_the_same_name():
    assert derive_level_code("Roof 2") == "RF"


# ── both hosts now agree ─────────────────────────────────────────────────────

def test_ifc_and_archicad_paths_derive_levels_identically():
    # The point of the exercise: one question, one answer, whichever host asks.
    sys.path.insert(0, str(_REPO_ROOT / "stingtools-core" / "python"))
    from stingtools_core.hosts.inference import level_for_storey_name

    for name in ["Ground Floor", "Level 3", "Basement 2", "Roof", "Mezzanine",
                 "Plant Room", "G/F", "1st Floor", "Sous-sol", "Unnamed", "0"]:
        assert derive_level_code(name) == level_for_storey_name(name), \
            f"{name!r}: hosts disagree again"


if __name__ == "__main__":
    passed = failed = 0
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            try:
                fn()
                passed += 1
                print(f"  PASS  {name}")
            except Exception as e:  # noqa: BLE001
                failed += 1
                print(f"  FAIL  {name}: {e}")
    print(f"\n{passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)
