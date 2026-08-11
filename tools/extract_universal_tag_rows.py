#!/usr/bin/env python3
"""
extract_universal_tag_rows.py — the 65 universal-tag label rows, in one place.

WHY THIS EXISTS

docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md is the authoritative human authoring
reference for the ONE universal STING tag label: 65 rows, each a calculated
value whose formula gates its tier on TAG_PARA_STATE_n_BOOL. It is a Family
Editor build guide, written for a person.

The plugin needs the same 65 rows as data — to diff a live family against the
spec, and (later) to author the formulas rather than have them typed. But
StingTools.csproj deliberately does NOT deploy Data/**/*.md ("reference docs …
are never loaded by code", csproj:262). So the rows have to reach the plugin as
a CSV.

That is two copies of the same facts, which is the failure mode this codebase
keeps hitting. So the CSV is GENERATED from the markdown and CI re-derives it:

    python tools/extract_universal_tag_rows.py            # regenerate the CSV
    python tools/extract_universal_tag_rows.py --check    # gate: CSV == markdown

The markdown stays the single source a human edits. Edit the table, re-run,
commit both. --check fails if you edit one and not the other.

WHAT IS AND IS NOT EXTRACTED

Only the STEP 2 table ("Full universal row list"). Row 1 is T1 — the primary
ASS_DISPLAY_TXT row, which is a plain label row with no formula — and it is
emitted with an empty formula so the row numbering in the CSV matches the
build sheet exactly. Everything downstream keys on the row number, so an
off-by-one here would be silent.

The STEP 4 badge params are NOT rows in the label; they are visibility formulas
on nested symbols. They stay out of this file.
"""

import argparse
import csv
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = os.path.join(ROOT, "docs", "UNIVERSAL_TAG_LABEL_BUILD_SHEET.md")
CSV_OUT = os.path.join(ROOT, "StingTools", "Data", "STING_UNIVERSAL_TAG_ROWS.csv")

HEADER = ["Row", "Tier", "Name", "Formula", "Prefix", "Suffix", "Break"]

# A build-sheet body row: | 8 | T4 | Show T4 - … | `if(…)` | Comm: |  | no |
ROW_RE = re.compile(r"^\|\s*(\d+)\s*\|\s*(T\d+)\s*\|(.*)$")

# The formula cell is fenced in backticks. Pulling it out first means a comma
# inside the formula cannot be mistaken for a cell boundary.
FORMULA_RE = re.compile(r"`([^`]*)`")

# if(GATE, SOURCE, "") — SOURCE is the parameter the row displays.
SOURCE_RE = re.compile(r'^if\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*,\s*""\s*\)$')


def parse_sheet(path):
    """Return the STEP 2 table as a list of dicts, in file order."""
    with io.open(path, "r", encoding="utf-8") as fh:
        lines = fh.read().splitlines()

    rows = []
    for line in lines:
        m = ROW_RE.match(line.strip())
        if not m:
            continue
        num, tier, rest = m.group(1), m.group(2), m.group(3)

        cells = [c.strip() for c in rest.split("|")]
        # rest is: Name | Formula | Prefix | Suffix | Break |   → 5 cells + tail
        if len(cells) < 5:
            raise ValueError(f"row {num}: expected 5 cells after the tier, got {len(cells)}: {line}")

        name = cells[0]
        formula_cell = cells[1]
        prefix, suffix, brk = cells[2], cells[3], cells[4]

        fm = FORMULA_RE.search(formula_cell)
        formula = fm.group(1) if fm else ""
        # Row 1 (T1) is the plain primary row: name "---", formula "-".
        if formula in ("-", "—"):
            formula = ""
        if name in ("---", "—"):
            name = ""

        rows.append(
            {
                "Row": num,
                "Tier": tier,
                "Name": name,
                "Formula": formula,
                "Prefix": prefix,
                "Suffix": suffix,
                # The sheet writes YES / no. Normalise to true / false so the
                # C# side does not carry the sheet's shouting.
                "Break": "true" if brk.strip().upper() == "YES" else "false",
            }
        )
    return rows


def validate(rows):
    """Fail loudly on the mistakes that would be silent downstream."""
    problems = []

    if not rows:
        problems.append("no rows parsed — did the STEP 2 table move or change shape?")

    # Row numbers must be 1..N with no gaps: everything keys on them.
    for i, r in enumerate(rows, start=1):
        if int(r["Row"]) != i:
            problems.append(f"row numbering breaks at position {i}: sheet says {r['Row']}")
            break

    seen_names = {}
    for r in rows:
        n, f, num = r["Name"], r["Formula"], r["Row"]
        if not f:
            continue  # T1 primary row

        m = SOURCE_RE.match(f)
        if not m:
            problems.append(f"row {num}: formula is not the if(gate, source, \"\") shape: {f}")
            continue
        gate, source = m.group(1), m.group(2)

        expected_gate = "TAG_PARA_STATE_" + r["Tier"][1:] + "_BOOL"
        if gate != expected_gate:
            problems.append(f"row {num}: tier {r['Tier']} gated on {gate}, expected {expected_gate}")

        if not n:
            problems.append(f"row {num}: has a formula but no calculated-value name")
        elif n == source:
            # The display parameter carrying the formula must NOT be the source
            # parameter it reads, or the formula is self-referential and Revit
            # rejects it (or worse, accepts it and eats the source value).
            problems.append(f"row {num}: calc-value name equals its source parameter ({n}) — self-referential")

        if n in seen_names:
            problems.append(f"row {num}: duplicate calc-value name '{n}' (also row {seen_names[n]})")
        else:
            seen_names[n] = num

    return problems


def write_csv(rows, path):
    with io.open(path, "w", encoding="utf-8", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=HEADER, lineterminator="\n")
        w.writeheader()
        for r in rows:
            w.writerow(r)


def read_csv(path):
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8", newline="") as fh:
        return list(csv.DictReader(fh))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="verify the CSV matches the build sheet; do not write")
    args = ap.parse_args()

    rows = parse_sheet(SHEET)
    problems = validate(rows)

    print(f"  build sheet : {os.path.relpath(SHEET, ROOT)}")
    print(f"  rows parsed : {len(rows)}")
    tiers = {}
    for r in rows:
        tiers[r["Tier"]] = tiers.get(r["Tier"], 0) + 1
    print("  by tier     : " + ", ".join(f"{k}={tiers[k]}" for k in sorted(tiers, key=lambda t: int(t[1:]))))

    if problems:
        print(f"\n  FAIL: {len(problems)} problem(s) in the build sheet:")
        for p in problems:
            print(f"    {p}")
        return 1

    if not args.check:
        write_csv(rows, CSV_OUT)
        print(f"\n  written: {os.path.relpath(CSV_OUT, ROOT)}")
        return 0

    existing = read_csv(CSV_OUT)
    if existing is None:
        print(f"\n  FAIL: {os.path.relpath(CSV_OUT, ROOT)} does not exist — run without --check")
        return 1

    if existing != rows:
        print(f"\n  FAIL: {os.path.relpath(CSV_OUT, ROOT)} is out of date.")
        print("  The build sheet and the shipped CSV disagree. Re-run without --check")
        print("  and commit both — the markdown is the source, the CSV is derived.")
        by_num = {r["Row"]: r for r in existing}
        shown = 0
        for r in rows:
            other = by_num.get(r["Row"])
            if other != r and shown < 5:
                print(f"    row {r['Row']}: sheet={r} csv={other}")
                shown += 1
        if len(rows) != len(existing):
            print(f"    row count: sheet={len(rows)} csv={len(existing)}")
        return 1

    print(f"\n  PASS — CSV matches the build sheet ({len(rows)} rows)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
