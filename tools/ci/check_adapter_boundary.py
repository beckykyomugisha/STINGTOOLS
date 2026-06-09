#!/usr/bin/env python3
"""Phase A6 — host-adapter boundary lint.

The sustainability rule from docs/MULTI_HOST_INTEGRATION_PLAN.md §1.1:

    Adapters contain ONLY the host-adapter contract glue. Tag grammar, enum,
    IDS, token-inference, and coordinate-alignment LOGIC live in
    `stingtools-core` and are called by adapters — never re-implemented in them.

This script scans the adapter files and fails (exit 1) if any of them
re-implement core logic, by signature. It is deliberately narrow (targets the
specific logic that was extracted to core in Phase A3) to avoid false positives
on legitimate Blender UI / glue code.

Run:        python tools/ci/check_adapter_boundary.py
Self-test:  python tools/ci/check_adapter_boundary.py --self-test
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

# Files that are host adapters (glue only). Add future ArchiCAD/Tekla adapter
# modules here as they land.
ADAPTER_GLOBS = [
    "stingtools-bonsai/ops/*.py",
]

# (id, message, compiled-regex). A match anywhere in an adapter file = violation.
BANNED = [
    (
        "IFC_CLASS_MAP",
        "Re-implemented IFC-class -> code mapping. Use "
        "stingtools_core.hosts.inference.discipline_for_class / infer_discipline.",
        # 3+ "IfcXxx": "Y" entries strongly imply a discipline/product class map.
        re.compile(r'(?:["\']Ifc\w+["\']\s*:\s*["\'][A-Z]{1,2}["\']\s*,\s*){3,}', re.S),
    ),
    (
        "SYSTEM_GROUP_INFERENCE",
        "Re-implemented IfcSystem inference. Use "
        "stingtools_core.hosts.inference.infer_system / system_for_name.",
        re.compile(r"IfcRelAssignsToGroup"),
    ),
    (
        "PRODUCT_CODE_INFERENCE",
        "Re-implemented product-code extraction. Use "
        "stingtools_core.hosts.inference.infer_product / product_for_type_name.",
        re.compile(r"\.isalpha\(\)"),
    ),
    (
        "SEQUENCE_COUNTER",
        "Re-implemented module-global sequence counter. Use "
        "stingtools_core.hosts.inference.SequenceAllocator.",
        # The extracted symbol was a module-global `_SEQ_COUNTERS` cache. A local
        # grouping dict inside one operator is acceptable glue and not flagged.
        re.compile(r"^_SEQ_COUNTERS\b", re.M),
    ),
]


def iter_adapter_files():
    for pattern in ADAPTER_GLOBS:
        yield from REPO.glob(pattern)


def scan_text(text: str) -> list[str]:
    """Return the ids of every banned signature present in `text`."""
    return [vid for vid, _msg, rx in BANNED if rx.search(text)]


def _self_test() -> int:
    """Prove the lint catches a deliberate violation AND clears real adapters.

    A regression that re-introduces core logic into an adapter must be caught,
    so we assert each banned signature fires against a synthetic fixture, then
    assert the live adapter files are clean.
    """
    failures: list[str] = []

    # 1. A synthetic adapter that re-implements every piece of core logic must
    #    trip every rule.
    violating = (
        "DISC = {\n"
        '    "IfcWall": "A",\n'
        '    "IfcDuctSegment": "M",\n'
        '    "IfcCableSegment": "E",\n'
        '    "IfcPipeSegment": "P",\n'
        "}\n"
        "rel = model.by_type('IfcRelAssignsToGroup')\n"
        "code = ''.join(c for c in name if c.isalpha())\n"
        "_SEQ_COUNTERS = {}\n"
    )
    hit = set(scan_text(violating))
    expected = {vid for vid, _m, _rx in BANNED}
    missing = expected - hit
    if missing:
        failures.append(f"self-test fixture did NOT trip rules: {sorted(missing)}")

    # 2. A clean glue file (legit Blender UI enum + local dict) must NOT trip.
    clean = (
        "_PRIORITY_ITEMS = [\n"
        '    ("LOW", "Low", ""),\n'
        '    ("HIGH", "High", ""),\n'
        "]\n"
        "counters = {}  # local grouping dict, acceptable glue\n"
    )
    false_pos = scan_text(clean)
    if false_pos:
        failures.append(f"self-test clean fixture false-positived: {false_pos}")

    # 3. The real adapter tree must currently be clean.
    real = main(verbose=False)
    if real != 0:
        failures.append("real adapter files are NOT clean")

    if failures:
        print("SELF-TEST FAILED:")
        for f in failures:
            print("  [X] " + f)
        return 1
    print("[OK] self-test passed: violations detected, clean files pass, real adapters clean.")
    return 0


def main(verbose: bool = True) -> int:
    violations: list[str] = []
    scanned = 0
    by_id = {vid: msg for vid, msg, _rx in BANNED}
    for path in sorted(iter_adapter_files()):
        scanned += 1
        text = path.read_text(encoding="utf-8")
        for vid in scan_text(text):
            rel = path.relative_to(REPO)
            violations.append(f"{rel}: [{vid}] {by_id[vid]}")

    if violations:
        if verbose:
            print("Adapter-boundary violations (logic must live in stingtools-core):\n")
            for v in violations:
                print("  [X] " + v)
            print(
                f"\n{len(violations)} violation(s) across {scanned} adapter file(s). "
                "Move the logic into stingtools_core.hosts.inference and call it."
            )
        return 1

    if verbose:
        print(f"[OK] adapter boundary clean - {scanned} adapter file(s) scanned, no core "
              "logic re-implemented.")
    return 0


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(_self_test())
    sys.exit(main())
