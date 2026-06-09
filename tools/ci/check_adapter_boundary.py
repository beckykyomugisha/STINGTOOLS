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

Run:  python tools/ci/check_adapter_boundary.py
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
        "Re-implemented IFC-class → code mapping. Use "
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


def main() -> int:
    violations: list[str] = []
    scanned = 0
    for path in sorted(iter_adapter_files()):
        scanned += 1
        text = path.read_text(encoding="utf-8")
        for vid, message, rx in BANNED:
            if rx.search(text):
                rel = path.relative_to(REPO)
                violations.append(f"{rel}: [{vid}] {message}")

    if violations:
        print("Adapter-boundary violations (logic must live in stingtools-core):\n")
        for v in violations:
            print("  ✗ " + v)
        print(
            f"\n{len(violations)} violation(s) across {scanned} adapter file(s). "
            "Move the logic into stingtools_core.hosts.inference and call it."
        )
        return 1

    print(f"✓ adapter boundary clean — {scanned} adapter file(s) scanned, no core "
          "logic re-implemented.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
