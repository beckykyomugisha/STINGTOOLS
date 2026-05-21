#!/usr/bin/env python3
"""
Audit shared/ifc/bsdd/publication_plan.json:

  - every enum in shared/ifc/enums has a plan entry
  - every plan entry's status appears in status_taxonomy
  - summary.by_status counts match the actual entry counts
  - every plan entry has a 'proposed' bool

Exit 1 on any failure. Used in CI.
"""

from __future__ import annotations
import json, sys
from pathlib import Path
from xml.etree import ElementTree as ET

ENUM_NS = "https://stingtools.io/schema/ifc/enums/v1"
REPO_ROOT = Path(__file__).resolve().parents[2]
ENUMS_DIR = REPO_ROOT / "shared" / "ifc" / "enums"
PLAN_PATH = REPO_ROOT / "shared" / "ifc" / "bsdd" / "publication_plan.json"


def main() -> int:
    enum_names = set()
    for x in ENUMS_DIR.glob("*.xml"):
        r = ET.parse(x).getroot()
        n = r.find(f"{{{ENUM_NS}}}Identity").find(f"{{{ENUM_NS}}}Name").text
        enum_names.add(n)

    plan = json.loads(PLAN_PATH.read_text())
    plan_names = {e["name"] for e in plan["enums"]}
    valid_status = set(plan["status_taxonomy"].keys())

    failures: list[str] = []

    missing = enum_names - plan_names
    ghosts = plan_names - enum_names
    if missing:
        failures.append(f"BSDD_PLAN_MISSING: {sorted(missing)}")
    if ghosts:
        failures.append(f"BSDD_PLAN_GHOST: {sorted(ghosts)}")

    actual_counts = {}
    for e in plan["enums"]:
        s = e.get("status")
        if s not in valid_status:
            failures.append(f"BAD_STATUS in {e.get('name')}: {s!r} not in status_taxonomy")
        actual_counts[s] = actual_counts.get(s, 0) + 1
        if "proposed" not in e:
            failures.append(f"MISSING_PROPOSED in {e.get('name')}")

    declared = plan["summary"]["by_status"]
    for status, n in actual_counts.items():
        if declared.get(status, 0) != n:
            failures.append(f"SUMMARY_DRIFT[{status}]: declared={declared.get(status, 0)} actual={n}")
    for status in declared:
        if declared[status] > 0 and status not in actual_counts:
            failures.append(f"SUMMARY_GHOST[{status}]: declared={declared[status]} but no entries")

    if plan["summary"]["total_enums_in_scope"] != len(plan["enums"]):
        failures.append(f"TOTAL_DRIFT: total_enums_in_scope={plan['summary']['total_enums_in_scope']} actual={len(plan['enums'])}")

    if failures:
        print("BSDD PLAN AUDIT FAILED:")
        for f in failures:
            print(f"  - {f}")
        return 1
    print(f"BSDD PLAN OK — {len(plan_names)} entries, all statuses + counts + proposed flags consistent.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
