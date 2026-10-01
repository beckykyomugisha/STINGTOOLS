#!/usr/bin/env python3
"""
D.1 / D.2 — the mechanism exists, but does anything call it?

WHY THIS EXISTS
---------------
Seven times this workstream, a defect turned out to be a working mechanism that
nothing invoked. Not broken code — CORRECT code, wired to the wrong side or to
nothing at all:

  TagIsComplete        read at 8 sites, enforced at 0        (G-42)
  AnnotationRunner     writing parameters bound to nothing   (removed)
  PRJ_SHEET_*          12 bound parameters, 11 with no writer (K-12)
  Binding_Type         honoured by 1 binder of 2             (G-8)
  GetFuncCode          2 callers, neither the tag pipeline
  ASS_TAG_1_TXT        two writers, opposite blank-handling  (G-44)
  ResolveLpsFunc       defined, ZERO callers                 (B.1 gate failure)

The last one killed a decision that had already been made: D8 keyed the rate tier
on FUNC, on the stated basis that the pipeline wrote LPS sub-functions. It does
not — ResolveLpsFunc would, and nothing calls it.

A grep for the symbol finds the definition and reads as "present". This counts
CALLERS, which is the question that actually matters.

  python3 tools/validate_declared_but_uncalled.py            # gate against baseline
  python3 tools/validate_declared_but_uncalled.py --report   # full listing
  python3 tools/validate_declared_but_uncalled.py --write-baseline

RATCHET, like the readership gate: the count may fall, never rise.
"""

import os
import re
import sys
import collections

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGIN = os.path.join(REPO, "StingTools")
CORE = os.path.join(PLUGIN, "Core")
BASELINE = os.path.join(REPO, "tools", "declared_uncalled_baseline.txt")

SKIP_DIRS = {"obj", "bin", ".git", "Data", "_template_sources", "_workflow_sources"}

# A public static method or property in Core that looks like a MAP or a PREDICATE —
# the two shapes that silently do nothing when uncalled. Constructors, Execute and
# event handlers are excluded: they are invoked by Revit, not by our code.
# DSCH-12: the prefix must be followed by an upper-case letter or digit, so `Is`
# no longer matches IsoSizes / IsoTextTiers and `Resolve` no longer matches
# ResolvedUniversalExtras. The character after the name says what it is:
# `(` a method (needs a call), `{` / `=` a property or field (needs a use).
DECL = re.compile(
    r'^\s*public\s+static\s+(?:readonly\s+)?[\w<>,\[\]\?\. ]+\s+'
    r'(?P<name>(?:Is|Has|Can|Should|Try|Get|Resolve|Lookup|Map|Find|Validate|Check)(?=[A-Z0-9_])\w+)'
    r'\s*(?P<kind>[\(\{=])'
)
IDENT = re.compile(r'\b([A-Za-z_]\w*)\b(\s*\()?')

EXCLUDE_NAMES = {"GetHashCode", "GetType", "GetEnumerator", "Execute", "GetString",
                 "GetInt", "GetDouble", "TryParse", "ToString"}


def cs_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.endswith(".cs"):
                yield os.path.join(dirpath, fn)


def main():
    # 1. Collect candidate declarations in Core.
    declared = {}   # name -> (relpath, lineno)
    is_method = {}  # name -> True for a method, False for a property/field
    for full in cs_files(CORE):
        rel = os.path.relpath(full, REPO).replace("\\", "/")
        try:
            lines = open(full, encoding="utf-8", errors="replace").readlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            m = DECL.match(line)
            if not m:
                continue
            name = m.group("name")
            if name in EXCLUDE_NAMES or name in declared:
                continue
            declared[name] = (rel, i)
            is_method[name] = m.group("kind") == "("

    # 2. Count uses across the WHOLE plugin in ONE tokenised pass (DSCH-12). The
    #    old loop ran every declaration's regex over every line - ~3e8 searches,
    #    eight minutes. A method needs `Name(`; a property or field needs `Name`.
    callers = collections.Counter()
    for full in cs_files(PLUGIN):
        rel = os.path.relpath(full, REPO).replace("\\", "/")
        try:
            lines = open(full, encoding="utf-8", errors="replace").readlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            s = line.lstrip()
            if s.startswith(("//", "///", "*")):
                continue          # a mention in prose is not a caller
            seen = set()
            for m in IDENT.finditer(line):
                name = m.group(1)
                if name not in declared or name in seen:
                    continue
                if declared[name] == (rel, i):
                    continue      # the declaration line itself
                if is_method[name] and not m.group(2):
                    continue      # a method name not followed by ( is not a call
                seen.add(name)
                callers[name] += 1

    uncalled = sorted(n for n in declared if callers[n] == 0)

    print("=" * 72)
    print("Declared-but-uncalled gate (D.1)")
    print("=" * 72)
    print(f"  public static map/predicate declarations in Core : {len(declared)}")
    print(f"  with ZERO callers anywhere in the plugin         : {len(uncalled)}")

    if "--report" in sys.argv:
        print("\n--- UNCALLED ---")
        for n in uncalled:
            rel, ln = declared[n]
            print(f"  {n:34} {rel}:{ln}")

    if "--write-baseline" in sys.argv:
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("# D.1 declared-but-uncalled ceiling. RATCHET: may fall, never rise.\n")
            fh.write("# A public static map or predicate in Core that nothing calls is a\n")
            fh.write("# mechanism that cannot fire. Seven such defects this workstream.\n")
            fh.write(f"{len(uncalled)}\n")
            # The names, so "which ones are new" is a diff, not archaeology.
            for n in uncalled:
                fh.write(f"- {n}\n")
        print(f"\nwrote baseline = {len(uncalled)}")
        return 0

    base, base_names = 0, set()
    try:
        for line in open(BASELINE, encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("- "):
                base_names.add(line[2:].strip())
            elif base == 0:
                base = int(line)
    except (OSError, ValueError):
        pass

    print(f"  baseline                                         : {base}")
    if len(uncalled) > base:
        print(f"\nFAIL: {len(uncalled) - base} new uncalled declaration(s). Run with --report.")
        if base_names:
            for n in uncalled:
                if n not in base_names:
                    rel, ln = declared[n]
                    print(f"  new: {n:34} {rel}:{ln}")
        return 1
    print("\nPASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
