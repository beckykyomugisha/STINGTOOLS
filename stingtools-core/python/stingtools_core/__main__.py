"""Command-line interface for stingtools-core.

Usage:
    python -m stingtools_core info
        Print substrate inventory + drift status.

    python -m stingtools_core validate-tag TAG_STRING [--stage STAGE]
        Validate an 8-segment tag string. STAGE defaults to Stage_3.
        Example:
            python -m stingtools_core validate-tag M-BLD1-Z01-L02-HVAC-SUP-AHU-0042
            python -m stingtools_core validate-tag M-XX-XX-L02-XX-XX-XX-0000 --stage Stage_2

    python -m stingtools_core check-ifc PATH [--ids IDS_PATH ...]
        Run SpatialChecker on an IFC file + optionally validate against
        one or more IDS files via ifctester.
        Requires: pip install ifcopenshell ifctester
        Example:
            python -m stingtools_core check-ifc model.ifc \
                --ids shared/ifc/ids/sting-tag-grammar.ids \
                --ids shared/ifc/ids/sting-spatial-codes.ids

    python -m stingtools_core verify-audit-log PATH
        Walk a JSONL audit log file and verify the SHA-256 chain.

Exit codes:
    0  success / validation passed
    1  validation failure (bad data)
    2  dependency missing (ifcopenshell / ifctester / etc.)
    3  file not found
    4  CLI usage error
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import (
    EnumRegistry, PsetRegistry, TagGrammar, Tag,
    __version__,
)
from .planscape import AuditLog


def _cmd_info(args: argparse.Namespace) -> int:
    """info — substrate inventory + drift status."""
    print(f"stingtools-core v{__version__}")
    print()
    enums = EnumRegistry().load()
    psets = PsetRegistry().load()
    print(f"Enums: {len(enums)}")
    print(f"Psets: {len(psets)}")
    print()
    if enums.drift:
        print(f"⚠ Drift detected in {len(enums.drift)} enum(s):")
        for name, stored, computed in enums.drift:
            print(f"  {name}: stored={stored[:16]}... computed={computed[:16]}...")
        return 1
    print("✓ All corporate locks match (drift-free)")
    print()
    referenced = psets.referenced_enums()
    missing = [r for r in referenced if r not in enums]
    if missing:
        print(f"⚠ Psets reference missing enums: {missing}")
        return 1
    print(f"✓ All {len(referenced)} pset→enum references resolve")
    print()
    if args.verbose:
        print("Enums by scope:")
        from .enums import EnumScope
        corp = [e for e in enums if e.scope is EnumScope.CORPORATE]
        proj = [e for e in enums if e.scope is EnumScope.PROJECT_TEMPLATE]
        print(f"  corporate:        {len(corp)}")
        print(f"  project_template: {len(proj)} ({', '.join(e.name for e in proj)})")
        print()
        print("Psets:")
        for p in psets:
            print(f"  {p.name:32}  {len(p.properties)} props, {len(p.rules)} rules")
    return 0


def _cmd_validate_tag(args: argparse.Namespace) -> int:
    """validate-tag — check an 8-segment tag against current enums."""
    try:
        tag = Tag.from_full_tag(args.tag)
    except ValueError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 4
    reg = EnumRegistry().load()
    grammar = TagGrammar(reg)
    res = grammar.validate(tag, stage=args.stage)
    print(f"Tag: {tag.to_full_tag()}")
    print(f"Stage: {args.stage}")
    print(f"Valid: {res.is_valid}")
    if res.sentinel_segments:
        print(f"Sentinel segments: {', '.join(res.sentinel_segments)}")
    if res.deprecated_codes:
        print("Deprecated codes:")
        for seg, code in res.deprecated_codes:
            print(f"  - {seg}: {code}")
    if res.warnings:
        print("Warnings:")
        for w in res.warnings:
            print(f"  - {w}")
    if res.errors:
        print("Errors:")
        for e in res.errors:
            print(f"  - {e}")
        return 1
    return 0


def _cmd_check_ifc(args: argparse.Namespace) -> int:
    """check-ifc — run SpatialChecker + optionally IDS validation."""
    try:
        import ifcopenshell  # type: ignore
    except ImportError:
        print("ERROR: ifcopenshell not installed. Run: pip install ifcopenshell",
              file=sys.stderr)
        return 2
    ifc_path = Path(args.ifc)
    if not ifc_path.exists():
        print(f"ERROR: {ifc_path} not found", file=sys.stderr)
        return 3

    from .spatial import SpatialChecker
    model = ifcopenshell.open(str(ifc_path))
    print(f"Loaded: {ifc_path.name}")
    print(f"Schema: {model.schema}")
    print(f"Elements: {len(model.by_type('IfcElement'))}")
    print()

    print("Running SpatialChecker...")
    mismatches = SpatialChecker(model).check_all_elements()
    if mismatches:
        print(f"  ⚠ {len(mismatches)} mismatch(es):")
        for m in mismatches:
            print(f"    [{m.rule_id}] {m.message}")
    else:
        print(f"  ✓ no mismatches")
    print()

    failures = 1 if mismatches else 0

    if args.ids:
        try:
            from .ids import IdsRunner
            runner = IdsRunner()
            if not runner.available:
                print("⚠ ifctester not installed — skipping IDS validation")
            else:
                for ids_path in args.ids:
                    print(f"Validating against {Path(ids_path).name}...")
                    result = runner.run(ids_path, ifc_path)
                    print(f"  {result.summary()}")
                    if not result.all_passed:
                        failures = 1
                        for spec in result.specs:
                            if not spec.passed:
                                print(f"    FAIL: {spec.spec_name} ({spec.identifier})")
                                for msg in spec.failure_messages[:3]:
                                    print(f"      - {msg}")
        except Exception as e:
            print(f"⚠ IDS runner error: {e}")

    return failures


def _cmd_verify_audit_log(args: argparse.Namespace) -> int:
    """verify-audit-log — walk JSONL chain, check SHA-256 integrity."""
    log_path = Path(args.log)
    if not log_path.exists():
        print(f"ERROR: {log_path} not found", file=sys.stderr)
        return 3
    log = AuditLog(log_path)
    valid, errors = log.verify_chain()
    if valid:
        # Count entries
        with log_path.open() as f:
            n = sum(1 for line in f if line.strip())
        print(f"✓ chain valid ({n} entries)")
        return 0
    print("✗ chain broken:")
    for e in errors:
        print(f"  - {e}")
    return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="python -m stingtools_core",
        description="STING IFC substrate command-line interface",
    )
    parser.add_argument("--version", action="version", version=f"stingtools-core {__version__}")
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_info = sub.add_parser("info", help="substrate inventory + drift status")
    p_info.add_argument("-v", "--verbose", action="store_true")
    p_info.set_defaults(func=_cmd_info)

    p_tag = sub.add_parser("validate-tag", help="validate an 8-segment tag")
    p_tag.add_argument("tag", help="dash-separated tag string e.g. M-BLD1-Z01-L02-HVAC-SUP-AHU-0042")
    p_tag.add_argument("--stage", default="Stage_3",
                       choices=["Stage_0", "Stage_1", "Stage_2", "Stage_3", "Stage_4", "Stage_5", "Stage_6", "Stage_7"])
    p_tag.set_defaults(func=_cmd_validate_tag)

    p_ifc = sub.add_parser("check-ifc", help="run SpatialChecker + IDS validation on an IFC")
    p_ifc.add_argument("ifc", help="path to IFC file")
    p_ifc.add_argument("--ids", action="append", default=[], help="IDS file (can repeat)")
    p_ifc.set_defaults(func=_cmd_check_ifc)

    p_log = sub.add_parser("verify-audit-log", help="verify a JSONL audit log's SHA-256 chain")
    p_log.add_argument("log", help="path to audit JSONL")
    p_log.set_defaults(func=_cmd_verify_audit_log)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
