#!/usr/bin/env python3
"""
K-11 — every parameter name a C# consumer READS must be one the binder SHIPS.

WHY THIS EXISTS
---------------
Four separate defects in one review pass were the same shape: a consumer reads a
parameter name that the binding data does not carry, so the read returns null
forever and nothing says a word.

  * WARN_VISIBLE_BOOL            — missing TAG_ prefix; real name TAG_WARN_VISIBLE_BOOL
  * PRJ_ORG_PROJECT_CODE         — missing _TXT suffix; 0 rows in MR_PARAMETERS.txt
  * TAG_PARA_STATE_*_BOOL        — declared, but 0 rows in CATEGORY_BINDINGS.csv
  * 41 PRJ_* (K-11)              — declared and read, 0 binding rows

Each was found by hand, months apart. Nothing validates the two sides against
each other, so the class recurs. This does.

WHAT IT CHECKS
--------------
For every STING-shaped parameter-name literal read from C#:

  1. it must be DECLARED in MR_PARAMETERS.txt, and
  2. it must have a BINDING — a row in CATEGORY_BINDINGS.csv, RESOLVED_BINDINGS.csv
     or FAMILY_PARAMETER_BINDINGS.csv — or be listed in CODE_BOUND below.

Violation class 1 (UNDECLARED) is always a defect: the name cannot exist.
Violation class 2 (UNBOUND) is a defect unless a code path binds it, which is why
CODE_BOUND is explicit and small — an escape hatch that must be justified, not a
silent allowance.

BASELINE
--------
The tree has pre-existing violations. Rather than block on all of them, the gate
holds the line at a recorded count: it FAILS when violations exceed the baseline
in tools/param_readership_baseline.txt. Lower the baseline when you fix some.
That is the same shape as check_path_discipline.ps1.

USAGE
  python3 tools/validate_param_readership.py              # gate against baseline
  python3 tools/validate_param_readership.py --report     # full violation listing
  python3 tools/validate_param_readership.py --self-test  # prove the matcher works
  python3 tools/validate_param_readership.py --write-baseline
"""

import os
import re
import sys
import collections

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGIN = os.path.join(REPO, "StingTools")
DATA = os.path.join(PLUGIN, "Data")
BASELINE = os.path.join(REPO, "tools", "param_readership_baseline.txt")

# Parameter-name shape: STING params are SCREAMING_SNAKE with at least one
# underscore and a known prefix. Restricting to known prefixes keeps ordinary
# C# constants (MAX_RETRIES, OST_Walls) out of the result.
PREFIXES = (
    "ASS_", "PRJ_", "TAG_", "CST_", "PLM_", "HVC_", "ELC_", "SUS_", "PER_",
    "MGS_", "CLN_", "RAD_", "CEQ_", "LIG_", "COM_", "TPL_", "PEN_", "LOD_",
    "STING_", "WARN_", "MNT_", "PLACE_",
)
NAME_RE = re.compile(r'"([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)"')

# Parameters bound by a CODE PATH rather than a data row. Each entry must name
# the binding site, so this list can be audited rather than trusted.
CODE_BOUND = {
    # LoadSharedParamsCommand.cs:867-890 — Phase 91 BOQ/sustainability override
    # binds these to OST_ProjectInformation directly.
    # (Empty today: the K-11 pass moved the PRJ_* set into CATEGORY_BINDINGS.csv
    #  so they are data-bound and visible to this gate. Add entries here ONLY
    #  with a file:line justification.)
}

# Directories that are not plugin consumer code.
SKIP_DIRS = {"obj", "bin", ".git", "Data", "_template_sources", "_workflow_sources"}

# A literal in one of these contexts is NOT a parameter name, so it is correctly
# absent from MR_PARAMETERS.txt and must not be counted as a violation. Triage of
# the original 603 found these two patterns accounted for most of the inflation:
#
#   prefix test   paramName.StartsWith("ASS_LOC")            <- a namespace, not a name
#   alias key     _extendedParams["ELC_BUSBAR_RATING"]        <- short alias ...
#                     = "ELC_BUSBAR_RATING_A"                 <- ... mapping to the real name
#                 Ext("ELC_BUSBAR_RATING")                    <- the accessor for it
#
# A gate set at an inflated number trains people to ignore it, so these are
# excluded at the scan rather than absorbed into the baseline.
NOT_A_PARAM_CONTEXT = (
    "StartsWith(", "EndsWith(", "Contains(", "IndexOf(", "Replace(",
    "_extendedParams[", "Ext(",
)


def load_declared():
    names = set()
    path = os.path.join(DATA, "MR_PARAMETERS.txt")
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            f = line.rstrip("\n").split("\t")
            if len(f) > 2 and f[0] == "PARAM":
                names.add(f[2])
    return names


def load_bound():
    bound = set()

    cb = os.path.join(DATA, "CATEGORY_BINDINGS.csv")
    with open(cb, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#") or line.startswith("Parameter_Name"):
                continue
            bound.add(line.split(",")[0].strip())

    rb = os.path.join(DATA, "RESOLVED_BINDINGS.csv")
    with open(rb, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            bound.add(line.split(",")[0].strip())

    fb = os.path.join(DATA, "FAMILY_PARAMETER_BINDINGS.csv")
    with open(fb, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            c = line.split(",")
            if len(c) > 1:
                bound.add(c[1].strip())

    return bound


def scan_code(root=PLUGIN):
    """name -> [(relpath, lineno), ...] for every STING-shaped literal in C#."""
    hits = collections.defaultdict(list)
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, REPO).replace("\\", "/")
            try:
                with open(full, encoding="utf-8", errors="replace") as fh:
                    lines = fh.readlines()
            except OSError:
                continue
            for i, line in enumerate(lines, 1):
                s = line.lstrip()
                # Skip comment-only lines: a name in prose is not a read.
                if s.startswith("//") or s.startswith("///") or s.startswith("*"):
                    continue
                # Skip prefix tests and alias-key maps — see NOT_A_PARAM_CONTEXT.
                if any(c in line for c in NOT_A_PARAM_CONTEXT):
                    continue
                for m in NAME_RE.finditer(line):
                    name = m.group(1)
                    if name.startswith(PREFIXES):
                        hits[name].append((rel, i))
    return hits


def analyse(root=PLUGIN, declared=None, bound=None):
    declared = load_declared() if declared is None else declared
    bound = load_bound() if bound is None else bound
    hits = scan_code(root)

    undeclared, unbound = {}, {}
    for name, sites in hits.items():
        if name in CODE_BOUND:
            continue
        if name not in declared:
            undeclared[name] = sites
        elif name not in bound:
            unbound[name] = sites
    return hits, undeclared, unbound


# --- 2.2d: near-duplicate NAME detection -------------------------------------
# Phase 112 minted a whole PRJ_ORG_* set alongside an existing PRJ_* set with the
# same meaning and a different GUID scheme (hand-minted a1b2c3d4-... vs UUIDv5),
# and nothing flagged it. The result was two live spellings per concept, each read
# by a different layer, and a binder that shipped whichever half nobody read.
#
# GUIDs cannot detect this — that is exactly the point: two spellings of one
# concept have DIFFERENT GUIDs by construction. Only the names collide.

AFFIXES = ("_TXT", "_BOOL", "_INT", "_DBL", "_COD", "_CODE", "_NR", "_NUM", "_ID")
INFIXES = ("ORG_", "PRJ_", "STING_")


def normalise_concept(name):
    """Reduce a parameter name to its concept, so spellings of one idea collide."""
    n = name
    for a in AFFIXES:
        while n.endswith(a):
            n = n[: -len(a)]
    for i in INFIXES:
        n = n.replace(i, "")
    return n.replace("_", "").upper()


def find_near_duplicates(declared):
    groups = collections.defaultdict(set)
    for name in declared:
        groups[normalise_concept(name)].add(name)
    return {k: sorted(v) for k, v in groups.items() if len(v) > 1}


# --- 1.4b: affix check -------------------------------------------------------
# The affix bug is the single most repeated defect on this codebase. Five so far:
#   WARN_VISIBLE_BOOL          missing TAG_ prefix
#   PRJ_ORG_PROJECT_CODE       missing _TXT suffix
#   PRJ_TB_SHOW_KEYPLAN_BOOL   missing underscore  (x3: KEYPLAN/NORTHARROW/SCALEBAR)
#   PRJ_ORIGINATOR_COD_TXT     ORG_ infix split
#   TAG_TEXT_COLOUR_TEXT       _TEXT where every sibling is _TXT
#
# Two distinct shapes, so two checks:
#   PAIRS     two names collapse to the same key under a suffix/underscore/plural
#             variation -> one concept, two spellings
#   OUTLIERS  a name uses a minority suffix spelling (_TEXT) where the codebase
#             convention is overwhelmingly _TXT -> no pair yet, but the next
#             consumer that guesses the conventional spelling gets a silent null

def _affix_key(name):
    k = name.replace("_", "").upper()
    if k.endswith("TEXT"):
        k = k[:-4] + "TXT"
    if k.endswith("S") and not k.endswith("SS"):
        k = k[:-1]
    return k


def find_affix_pairs(names):
    groups = collections.defaultdict(set)
    for n in names:
        groups[_affix_key(n)].add(n)
    return {k: sorted(v) for k, v in groups.items() if len(v) > 1}


def find_affix_outliers(declared):
    """Names using a minority suffix spelling where a dominant convention exists."""
    txt = sum(1 for n in declared if n.endswith("_TXT"))
    text = [n for n in declared if n.endswith("_TEXT")]
    # Only report when _TXT is clearly the convention.
    if txt > 10 * max(1, len(text)):
        return sorted(text), txt, len(text)
    return [], txt, len(text)


def read_baseline():
    try:
        with open(BASELINE, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if line and not line.startswith("#"):
                    return int(line)
    except (OSError, ValueError):
        pass
    return 0


def self_test():
    """Prove the matcher rejects the four known real defects.

    A gate whose matching has broken reports green over broken data forever, so
    the shapes this exists to catch are asserted against synthetic input.
    """
    import tempfile

    declared = {"TAG_WARN_VISIBLE_BOOL", "PRJ_ORG_PROJECT_CODE_TXT", "ASS_TAG_1_TXT"}
    bound = {"TAG_WARN_VISIBLE_BOOL", "ASS_TAG_1_TXT"}

    src = '''
class T {
    void M() {
        Get(el, "WARN_VISIBLE_BOOL");            // undeclared: missing TAG_ prefix
        Get(el, "PRJ_ORG_PROJECT_CODE");         // undeclared: missing _TXT
        Get(el, "PRJ_ORG_PROJECT_CODE_TXT");     // declared but unbound
        Get(el, "ASS_TAG_1_TXT");                // fine
        // Get(el, "TAG_IN_A_COMMENT_TXT");      // comment: must be ignored
        Get(el, "MAX_RETRY_COUNT");              // not a STING prefix: ignored
    }
}
'''
    failures = []
    with tempfile.TemporaryDirectory() as td:
        with open(os.path.join(td, "T.cs"), "w", encoding="utf-8") as fh:
            fh.write(src)
        hits, undeclared, unbound = analyse(td, declared, bound)

        for want in ("WARN_VISIBLE_BOOL", "PRJ_ORG_PROJECT_CODE"):
            if want not in undeclared:
                failures.append(f"MISS: {want} should be flagged UNDECLARED")
        if "PRJ_ORG_PROJECT_CODE_TXT" not in unbound:
            failures.append("MISS: PRJ_ORG_PROJECT_CODE_TXT should be flagged UNBOUND")
        if "ASS_TAG_1_TXT" in undeclared or "ASS_TAG_1_TXT" in unbound:
            failures.append("FALSE POSITIVE: ASS_TAG_1_TXT is declared and bound")
        if "TAG_IN_A_COMMENT_TXT" in hits:
            failures.append("FALSE POSITIVE: a name in a comment was treated as a read")
        if "MAX_RETRY_COUNT" in hits:
            failures.append("FALSE POSITIVE: non-STING constant matched")

    if failures:
        print("SELF-TEST FAILED")
        for f in failures:
            print("   ", f)
        return 1
    print("SELF-TEST PASSED — all four known defect shapes are caught, no false positives.")
    return 0


def main():
    if "--self-test" in sys.argv:
        return self_test()

    hits, undeclared, unbound = analyse()
    total = len(undeclared) + len(unbound)
    dupes = find_near_duplicates(load_declared())

    print("=" * 72)
    print("Parameter readership gate")
    print("=" * 72)
    print(f"  distinct STING param literals read from C# : {len(hits)}")
    print(f"  UNDECLARED (not in MR_PARAMETERS.txt)      : {len(undeclared)}")
    print(f"  UNBOUND    (declared, no binding row)      : {len(unbound)}")
    print(f"  total violations                            : {total}")
    print(f"  near-duplicate concepts (reported only)     : {len(dupes)}")

    declared_all = load_declared()
    universe = declared_all | set(hits)
    pairs = find_affix_pairs(universe)
    outliers, n_txt, n_text = find_affix_outliers(declared_all)
    print(f"  affix pairs (suffix/underscore/plural)      : {len(pairs)}")
    print(f"  affix convention outliers (_TEXT vs _TXT)   : {len(outliers)}  "
          f"[_TXT {n_txt} / _TEXT {n_text}]")

    if "--affix" in sys.argv:
        print("\n--- AFFIX PAIRS: one concept, two spellings ---")
        for k in sorted(pairs):
            print(f"  {k}")
            for n in pairs[k]:
                mark = "" if n in declared_all else "   <-- NOT DECLARED"
                print(f"      {n}{mark}")
        print("\n--- AFFIX CONVENTION OUTLIERS ---")
        print(f"  _TXT is the convention ({n_txt} params). These use _TEXT:")
        for n in outliers:
            print(f"      {n}")

    if "--duplicates" in sys.argv:
        print("\n--- NEAR-DUPLICATE NAMES: one concept, several spellings ---")
        print("Standing rule: a new subsystem must NOT mint a shared parameter for a")
        print("concept that already has one, regardless of GUID scheme.\n")
        for concept in sorted(dupes):
            print(f"  {concept}")
            for n in dupes[concept]:
                print(f"      {n}")

    if "--report" in sys.argv:
        for title, group in (("UNDECLARED", undeclared), ("UNBOUND", unbound)):
            print(f"\n--- {title} ({len(group)}) ---")
            for name in sorted(group):
                sites = group[name]
                print(f"\n{name}   ({len(sites)} site(s))")
                for f, l in sites[:5]:
                    print(f"    {f}:{l}")
                if len(sites) > 5:
                    print(f"    ... +{len(sites) - 5} more")

    if "--write-baseline" in sys.argv:
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("# K-11 parameter-readership violation baseline.\n")
            fh.write("# The gate fails when the violation count EXCEEDS this number.\n")
            fh.write("# Lower it when you fix violations; never raise it without saying why.\n")
            fh.write(f"{total}\n")
        print(f"\nwrote baseline = {total}")
        return 0

    base = read_baseline()
    print(f"  baseline                                    : {base}")
    if total > base:
        print(f"\nFAIL: {total - base} new violation(s) above baseline.")
        print("Run with --report to see them.")
        return 1
    if total < base:
        print(f"\nPASS — and {base - total} below baseline. Lower it with --write-baseline.")
        return 0
    print("\nPASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
