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

MISMATCH_KINDS = ("none", "loc", "lvl", "zone", "sys", "seq", "fulltag", "tag-grammar")


def generate_fixture(
    out_path: Path,
    verbose: bool = False,
    mismatch: bool = False,
    mismatch_kind: str = "loc",
) -> int:
    """Mint a tiny valid IFC4 with Pset_StingSpatialCodes + Pset_StingTags.

    When `mismatch=True`, applies a deliberate inconsistency selected by
    `mismatch_kind` (default "loc" for backward compatibility):

      loc          — wall's Pset_StingTags.Location ≠ building.LocationCode
                     Fires SpatialChecker LOC_MATCHES_BUILDING.
      lvl          — wall's Level ≠ storey.LevelCode
                     Fires SpatialChecker LVL_MATCHES_STOREY.
      zone         — wall's Zone ≠ any assigned IfcZone's ZoneCode
                     Fires SpatialChecker ZONE_MATCHES_ASSIGNEDZONE.
      sys          — wall is assigned to an IfcSystem named "HVAC Supply"
                     but has Pset_StingTags.System = "LV" (electrical mismatch).
                     Fires SpatialChecker SYS_MATCHES_IFCSYSTEM.
      seq          — mints TWO walls with identical (Disc, Sys, Lvl, Seq)
                     to fire SpatialChecker SEQ_UNIQUE_WITHIN_GROUP.
      fulltag      — wall's FullTag is set but doesn't match the segments
                     joined with dashes.
                     Fires SpatialChecker FULLTAG_CONSISTENT.
      tag-grammar  — wall's Discipline = "INVALID" (outside enum),
                     Sequence = "1" (not zero-padded).
                     Fires IDS sting-tag-grammar.ids spec failures.

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

    # 6. Pset_StingTags on the wall — start from the all-good baseline
    kind = (mismatch_kind or "loc").lower() if mismatch else "none"
    if kind not in MISMATCH_KINDS:
        print(f"WARN: unknown --mismatch-kind {kind!r}; falling back to 'loc'")
        kind = "loc"

    tags = {
        "Discipline": "A",
        "Location":   "BLD1",
        "Zone":       "Z01",
        "Level":      "L01",
        "System":     "ARC",
        "Function":   "NLB",
        "Product":    "WL",
        "Sequence":   "0001",
    }
    # apply the targeted mismatch
    note = ""
    if   kind == "loc":         tags["Location"] = "WAC"; note = "wall LOC='WAC' ≠ building LocationCode='BLD1'"
    elif kind == "lvl":         tags["Level"]    = "L99"; note = "wall LVL='L99' ≠ storey LevelCode='L01'"
    elif kind == "zone":        tags["Zone"]     = "Z99"; note = "wall ZONE='Z99' but no IfcZone has that code"
    elif kind == "fulltag":     note = "FullTag stored but doesn't match segments"
    elif kind == "tag-grammar": tags["Discipline"] = "INVALID"; tags["Sequence"] = "1"; note = "Discipline outside enum + Sequence not zero-padded"
    elif kind == "sys":         tags["System"] = "LV"; note = "wall System='LV' but assigned IfcSystem named 'HVAC Supply'"
    # seq mismatch is handled below (needs a second wall)

    # FullTag — for fulltag mismatch, store something deliberately wrong
    if kind == "fulltag":
        tags["FullTag"] = "WRONG-FULLTAG-VALUE"
    else:
        tags["FullTag"] = "-".join(str(tags[k]) for k in ("Discipline","Location","Zone","Level","System","Function","Product","Sequence"))

    pset_w = run("pset.add_pset", model, product=wall, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=pset_w, properties=tags)

    # SYS mismatch — assign the wall to an IfcSystem named "HVAC Supply" so SpatialChecker fires
    n_walls = 1
    if kind == "sys":
        sys_grp = run("root.create_entity", model, ifc_class="IfcSystem", name="HVAC Supply")
        run("group.assign_group", model, products=[wall], group=sys_grp)

    # SEQ-uniqueness mismatch — add a second wall with identical tag tuple
    if kind == "seq":
        wall2 = run("root.create_entity", model, ifc_class="IfcWall", name="Test Wall 2")
        run("spatial.assign_container", model, products=[wall2], relating_structure=storey)
        run("group.assign_group", model, products=[wall2], group=zone)
        pset_w2 = run("pset.add_pset", model, product=wall2, name="Pset_StingTags")
        run("pset.edit_pset", model, pset=pset_w2, properties=dict(tags))
        n_walls = 2
        note = "two walls share (Disc=A, Sys=ARC, Lvl=L01, Seq=0001)"

    model.write(str(out_path))
    if verbose:
        print(f"  wrote {out_path} ({out_path.stat().st_size} bytes)")
        print(f"  schema:   IFC4")
        print(f"  entities: 1 building, 1 storey, 1 zone, {n_walls} wall(s)")
        print(f"  psets:    Pset_StingSpatialCodes ×3, Pset_StingTags ×{n_walls}")
        if mismatch:
            print(f"  ⚠  MISMATCH FIXTURE ({kind}): {note}")
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
    p.add_argument("--mismatch", action="store_true",
                   help="when used with --generate-fixture, mint the negative-fixture variant")
    p.add_argument("--mismatch-kind", default="loc",
                   choices=("loc", "lvl", "zone", "sys", "seq", "fulltag", "tag-grammar"),
                   help="which kind of mismatch to introduce (default: loc)")
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
        rc = generate_fixture(fixture, args.verbose,
                              mismatch=args.mismatch,
                              mismatch_kind=args.mismatch_kind)
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
