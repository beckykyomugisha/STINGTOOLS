#!/usr/bin/env python3
"""
D.2 — two owners for one quantity. THREE NARROW CHECKS, not one heuristic.

WHY THREE
---------
Declined last pass on the grounds that the five known instances were each found
by different reasoning, and a single heuristic would miss most of them. That
still holds, so this reports three independent checks with three separate counts.
Narrow and honest beats broad and wrong — a validator that lumps them produces a
number nobody can act on.

The five found by accident, and which check would have caught each:

  G-15  masonry mortar: formula vs CompoundTakeoffBuilder      (c)
  G-34  two block-count formulas, 15.8% apart                  (a)
  G-35  mortar's third owner, 6.6x out                         (a)
  G-44  ASS_TAG_1_TXT written by two C# paths                  (b)
  --    Binding_Type honoured by one binder of two             (b)

  (a) two formulas computing the same output parameter
  (b) two C# call sites writing the same parameter name
  (c) a formula output that a C# path also writes

USAGE
  python3 tools/validate_dual_owner.py            # gate against baseline
  python3 tools/validate_dual_owner.py --report   # full listing per check
  python3 tools/validate_dual_owner.py --write-baseline

RATCHET: each count may fall, never rise.

KNOWN LIMITATION — checks (b) and (c) see writes whose parameter name is a STRING
LITERAL. A write through a constant (ParameterHelpers.SetString(el,
ParamRegistry.TAG1, ...)) is invisible to them. G-44 is exactly that shape, so
this validator does NOT catch the very defect that motivated check (b). Resolving
constants would need real symbol resolution, not a regex. Stated rather than
papered over: the count is a floor, not a total.
"""

import os
import re
import sys
import csv
import collections

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGIN = os.path.join(REPO, "StingTools")
FORMULAS = os.path.join(PLUGIN, "Data", "FORMULAS_WITH_DEPENDENCIES.csv")
BASELINE = os.path.join(REPO, "tools", "dual_owner_baseline.txt")

SKIP_DIRS = {"obj", "bin", ".git", "Data", "_template_sources", "_workflow_sources"}

# A write to a named parameter. Deliberately narrow: these are the setters that
# actually persist a value, not every mention of a name.
WRITE = re.compile(
    r'(?:SetString|SetInt|SetDouble|SetYesNo|SetIfEmpty|TrySetString|TrySetInteger|TrySetLengthMm)'
    r'\s*\(\s*[A-Za-z_][A-Za-z0-9_.\[\]\(\)]*\s*,\s*"([A-Z][A-Z0-9_.]{3,})"')


def formula_rows():
    rows = []
    try:
        with open(FORMULAS, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                if line.startswith("#") or "," not in line:
                    continue
                r = next(csv.reader([line]))
                if len(r) > 3 and r[1] and r[1] != "Parameter_Name":
                    rows.append((r[1], r[3]))
    except OSError:
        pass
    return rows


def cs_writes():
    """param name -> [(relpath, lineno), ...] for every persisting write."""
    hits = collections.defaultdict(list)
    for dirpath, dirnames, filenames in os.walk(PLUGIN):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, REPO).replace("\\", "/")
            try:
                lines = open(full, encoding="utf-8", errors="replace").readlines()
            except OSError:
                continue
            for i, line in enumerate(lines, 1):
                s = line.lstrip()
                if s.startswith(("//", "///", "*")):
                    continue
                for m in WRITE.finditer(line):
                    hits[m.group(1)].append((rel, i))
    return hits


def main():
    rows = formula_rows()
    writes = cs_writes()

    # (a) two formulas computing the SAME output parameter.
    out_counts = collections.Counter(name for name, _ in rows)
    a = {n: c for n, c in out_counts.items() if c > 1}

    # (b) two C# call sites writing the same parameter, in DIFFERENT files.
    # Same-file repeats are usually one code path with branches, so they are not
    # counted — that is the difference between a dual owner and a loop.
    b = {}
    for name, sites in writes.items():
        files = {f for f, _ in sites}
        if len(files) > 1:
            b[name] = sorted(files)

    # (c) a formula output that a C# path also writes.
    formula_outputs = set(out_counts)
    c = {n: sorted({f for f, _ in writes[n]}) for n in formula_outputs if n in writes}

    print("=" * 72)
    print("Dual-owner gate (D.2) — three narrow checks")
    print("=" * 72)
    print(f"  (a) one output parameter, two+ formulas        : {len(a)}")
    print(f"  (b) one parameter, writes in two+ C# files     : {len(b)}")
    print(f"  (c) formula output ALSO written by C#          : {len(c)}")

    if "--report" in sys.argv:
        print("\n--- (a) two formulas, same output ---")
        for n, cnt in sorted(a.items()):
            print(f"  {n}  ({cnt} formulas)")
        print("\n--- (b) same parameter written from two+ files ---")
        for n, files in sorted(b.items()):
            print(f"  {n}")
            for f in files:
                print(f"      {f}")
        print("\n--- (c) formula output also written by C# ---")
        for n, files in sorted(c.items()):
            print(f"  {n}")
            for f in files:
                print(f"      {f}")

    counts = (len(a), len(b), len(c))
    if "--write-baseline" in sys.argv:
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("# D.2 dual-owner ceilings, one per check. RATCHET: may fall, never rise.\n")
            fh.write("# a = two formulas computing one output parameter\n")
            fh.write("# b = one parameter written from two or more C# files\n")
            fh.write("# c = a formula output that a C# path also writes\n")
            fh.write(f"{counts[0]} {counts[1]} {counts[2]}\n")
        print(f"\nwrote baseline = a:{counts[0]} b:{counts[1]} c:{counts[2]}")
        return 0

    base = (0, 0, 0)
    try:
        for line in open(BASELINE, encoding="utf-8"):
            line = line.strip()
            if line and not line.startswith("#"):
                base = tuple(int(x) for x in line.split())
                break
    except (OSError, ValueError):
        pass

    print(f"  baseline                                       : a:{base[0]} b:{base[1]} c:{base[2]}")
    over = [f"({k}) {n} > {o}" for k, n, o in zip("abc", counts, base) if n > o]
    if over:
        print("\nFAIL: " + "; ".join(over) + ". Run with --report.")
        return 1
    print("\nPASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
