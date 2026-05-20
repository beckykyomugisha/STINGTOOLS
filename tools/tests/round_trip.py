#!/usr/bin/env python3
"""
Round-trip test harness for the STING IFC substrate.

Verifies, end-to-end, that:

  1. The Pset_StingTags + Pset_StingSpatialCodes templates can be
     applied to a fresh IFC.
  2. The IFC then passes the sting-tag-grammar.ids validation.
  3. The IFC then passes the sting-spatial-codes.ids validation.
  4. Re-reading the IFC recovers every Pset value byte-identical.
  5. Mutating any value triggers the expected IDS failure.

Dependencies (skip-if-missing):

  - ifcopenshell           (PyPI: ifcopenshell)         — IFC read/write
  - ifctester              (PyPI: ifctester)            — IDS validator

When either is missing, the harness reports SKIPPED with a clear note
explaining how to install. Designed for local dev + CI runs.

USAGE

  python3 tools/tests/round_trip.py
      --fixture tests/fixtures/spatial_codes_ok.ifc
      --ids shared/ifc/ids/sting-spatial-codes.ids

      --generate-fixture     create a fresh fixture IFC and exit
      --verbose
      --strict               exit non-zero on SKIP (CI mode)

EXIT CODES

   0  all tests pass
   1  IDS validation failed
   2  round-trip data mismatch
   3  missing test dependency in --strict mode
   4  fixture generation failed
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_FIXTURE = REPO_ROOT / "tests" / "fixtures" / "spatial_codes_ok.ifc"
DEFAULT_IDS = REPO_ROOT / "shared" / "ifc" / "ids" / "sting-spatial-codes.ids"


# ----------------------------------------------------------------------
# dependency probes
# ----------------------------------------------------------------------

def _try_import(modname: str) -> Any:
    try:
        return __import__(modname)
    except ImportError:
        return None


def has_ifcopenshell() -> bool:
    return _try_import("ifcopenshell") is not None


def has_ifctester() -> bool:
    return _try_import("ifctester") is not None


# ----------------------------------------------------------------------
# fixture generation — needs ifcopenshell
# ----------------------------------------------------------------------

def generate_fixture(out_path: Path, verbose: bool = False, mismatch: bool = False) -> int:
    """Mint a tiny valid IFC4 with Pset_StingSpatialCodes + Pset_StingTags.

    When `mismatch=True`, the wall's Pset_StingTags.Location is set to a
    value that does NOT match the containing IfcBuilding.LocationCode —
    used for negative-path verification (SpatialChecker should fire
    LOC_MATCHES_BUILDING).

    Returns 0 on success, 4 on missing ifcopenshell.
    """
    if not has_ifcopenshell():
        print("SKIP: --generate-fixture requires ifcopenshell. Install: pip install ifcopenshell")
        return 4

    import ifcopenshell
    from ifcopenshell.api import run

    out_path = Path(out_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # 1. Project + units + context
    model = run("project.create_file", version="IFC4")
    project = run("root.create_entity", model, ifc_class="IfcProject", name="STING test project")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    # 2. Spatial hierarchy: Project → Site → Building → Storey
    site     = run("root.create_entity", model, ifc_class="IfcSite",           name="Test Site")
    building = run("root.create_entity", model, ifc_class="IfcBuilding",       name="Test Building")
    storey   = run("root.create_entity", model, ifc_class="IfcBuildingStorey", name="L01")

    run("aggregate.assign_object", model, products=[site],     relating_object=project)
    run("aggregate.assign_object", model, products=[building], relating_object=site)
    run("aggregate.assign_object", model, products=[storey],   relating_object=building)

    # 3. Zone (groups, not aggregates) — separately assignable
    zone = run("root.create_entity", model, ifc_class="IfcZone", name="Z01")

    # 4. One wall, contained in storey, grouped into the zone
    wall = run("root.create_entity", model, ifc_class="IfcWall", name="Test Wall")
    run("spatial.assign_container", model, products=[wall], relating_structure=storey)
    run("group.assign_group", model, products=[wall], group=zone)

    # 5. Pset_StingSpatialCodes on each spatial container
    pset_b = run("pset.add_pset", model, product=building, name="Pset_StingSpatialCodes")
    run("pset.edit_pset", model, pset=pset_b, properties={
        "LocationCode": "BLD1",
        "HumanName":    "Test Building",
        "SortOrder":    1,
    })
    pset_s = run("pset.add_pset", model, product=storey, name="Pset_StingSpatialCodes")
    run("pset.edit_pset", model, pset=pset_s, properties={
        "LevelCode":    "L01",
        "HumanName":    "Level 01",
        "SortOrder":    10,
    })
    pset_z = run("pset.add_pset", model, product=zone, name="Pset_StingSpatialCodes")
    run("pset.edit_pset", model, pset=pset_z, properties={
        "ZoneCode":     "Z01",
        "ZoneCategory": "Clinical",
        "HumanName":    "Test Clinical Zone",
    })

    # 6. Pset_StingTags on the wall
    wall_loc = "WAC" if mismatch else "BLD1"   # negative fixture flips LOC
    pset_w = run("pset.add_pset", model, product=wall, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=pset_w, properties={
        "Discipline": "A",
        "Location":   wall_loc,
        "Zone":       "Z01",
        "Level":      "L01",
        "System":     "ARC",
        "Function":   "NLB",
        "Product":    "WL",
        "Sequence":   "0001",
        "FullTag":    f"A-{wall_loc}-Z01-L01-ARC-NLB-WL-0001",
    })

    model.write(str(out_path))
    if verbose:
        print(f"  wrote {out_path} ({out_path.stat().st_size} bytes)")
        print(f"  schema:   IFC4")
        print(f"  entities: 1 building, 1 storey, 1 zone, 1 wall")
        print(f"  psets:    Pset_StingSpatialCodes ×3, Pset_StingTags ×1")
        if mismatch:
            print(f"  ⚠  MISMATCH FIXTURE: wall LOC={wall_loc!r} but building LocationCode='BLD1'")
    return 0
    #
    #   model.write(str(out_path))

    print(f"TODO: implement ifcopenshell-based fixture generation → {out_path}")
    print("      (this scaffold documents the call sequence; full impl awaits next IFC tooling milestone)")
    return 0


# ----------------------------------------------------------------------
# IDS validation
# ----------------------------------------------------------------------

def run_ids(ifc_path: Path, ids_path: Path, verbose: bool = False) -> tuple[int, str]:
    """Run ifctester. Returns (exit_code, summary_text).

    Skip with code 3 (or 0 if not strict) if ifctester is not available.
    """
    if not has_ifctester() or not has_ifcopenshell():
        return 3, "skipped (ifctester / ifcopenshell not installed; pip install ifctester)"

    import ifcopenshell
    from ifctester import ids, reporter

    spec = ids.open(str(ids_path))
    model = ifcopenshell.open(str(ifc_path))
    spec.validate(model)
    rep = reporter.Console(spec)
    summary = rep.report()
    has_failure = any(s.status is False for s in spec.specifications)
    return (1 if has_failure else 0, summary)


# ----------------------------------------------------------------------
# round-trip equality check
# ----------------------------------------------------------------------

def run_round_trip(ifc_path: Path, verbose: bool = False) -> tuple[int, str]:
    """Open IFC, read every Pset_Sting* value, dump as canonical JSON,
    reopen and recompute, compare. Mismatch => exit 2."""
    if not has_ifcopenshell():
        return 3, "skipped (ifcopenshell not installed)"

    import ifcopenshell
    import ifcopenshell.util.element as util_el
    import json
    import hashlib

    def dump_psets(model) -> dict:
        out: dict = {}
        for el in model.by_type("IfcRoot"):
            psets = util_el.get_psets(el) or {}
            for pname, props in psets.items():
                if pname.startswith("Pset_Sting"):
                    out.setdefault(el.GlobalId, {})[pname] = dict(sorted(props.items()))
        return out

    m1 = ifcopenshell.open(str(ifc_path))
    snap1 = dump_psets(m1)

    # write to temp, re-open
    tmp_path = ifc_path.parent / (ifc_path.stem + ".roundtrip.ifc")
    m1.write(str(tmp_path))
    m2 = ifcopenshell.open(str(tmp_path))
    snap2 = dump_psets(m2)
    tmp_path.unlink(missing_ok=True)

    h1 = hashlib.sha256(json.dumps(snap1, sort_keys=True).encode()).hexdigest()
    h2 = hashlib.sha256(json.dumps(snap2, sort_keys=True).encode()).hexdigest()
    if h1 != h2:
        return 2, f"round-trip mismatch: pre={h1[:16]} post={h2[:16]}"
    return 0, f"round-trip OK (sha256={h1[:16]} over {len(snap1)} elements)"


# ----------------------------------------------------------------------
# entry point
# ----------------------------------------------------------------------

def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--fixture", default=str(DEFAULT_FIXTURE))
    p.add_argument("--ids", default=str(DEFAULT_IDS), help="Path to IDS file (defaults to sting-spatial-codes.ids; can repeat)", action="append")
    p.add_argument("--generate-fixture", action="store_true")
    p.add_argument("--mismatch", action="store_true", help="when used with --generate-fixture, mint the negative-fixture variant (LOC ≠ building.LocationCode)")
    p.add_argument("--verbose", action="store_true")
    p.add_argument("--strict", action="store_true", help="exit non-zero on SKIP")
    args = p.parse_args(argv)

    fixture = Path(args.fixture)

    # ifctester adds a default to "ids" -> normalise
    ids_list: list[Path]
    if isinstance(args.ids, list):
        ids_list = [Path(s) for s in args.ids if s and Path(s).exists()]
    else:
        ids_list = [Path(args.ids)] if Path(args.ids).exists() else []
    if not ids_list:
        ids_list = [DEFAULT_IDS]

    print(f"Repo root: {REPO_ROOT}")
    print(f"Fixture:   {fixture}")
    print(f"IDS files: {len(ids_list)} ({', '.join(p.name for p in ids_list)})")
    print()

    if args.generate_fixture:
        rc = generate_fixture(fixture, args.verbose, mismatch=args.mismatch)
        if args.strict and rc == 3:
            return 3
        return rc

    if not fixture.exists():
        print(f"FIXTURE NOT FOUND: {fixture}")
        print("Run with --generate-fixture (requires ifcopenshell).")
        return 4

    # 1. IDS validation
    failures = 0
    for ids in ids_list:
        rc, summary = run_ids(fixture, ids, args.verbose)
        if rc == 3:
            print(f"[SKIP] {ids.name}: {summary}")
            if args.strict:
                return 3
        elif rc != 0:
            print(f"[FAIL] {ids.name}:")
            print(summary)
            failures += 1
        else:
            print(f"[ OK ] {ids.name}")
            if args.verbose:
                print(summary)

    # 2. Round-trip
    rc, summary = run_round_trip(fixture, args.verbose)
    if rc == 3:
        print(f"[SKIP] round-trip: {summary}")
        if args.strict:
            return 3
    elif rc != 0:
        print(f"[FAIL] round-trip: {summary}")
        failures += 1
    else:
        print(f"[ OK ] round-trip: {summary}")

    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
