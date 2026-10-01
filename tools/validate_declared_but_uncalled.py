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
  python3 tools/validate_declared_but_uncalled.py --self-test   # prove the checks fire

  Add --wide to any of the first three for the WIDE scope (below).

TWO SCOPES, TWO RATCHETS (DSCH-40)
----------------------------------
NARROW (the original, baseline tools/declared_uncalled_baseline.txt, now 0):
  public static members in StingTools/Core/ whose name starts Is / Has / Can /
  Should / Try / Get / Resolve / Lookup / Map / Find / Validate / Check, with a
  plain (non-tuple) type.

WIDE (baseline tools/declared_uncalled_wide_baseline.txt) closes the three blind
spots of the narrow scan:
  1. tuple return types - `public static (int Min, int Max)? GetSeqRange(` was
     invisible, because the type pattern did not admit parentheses;
  2. members outside Core/ - the whole of StingTools/ (minus obj/ bin/ Data/);
  3. Load* / Build* names - loaders and builders that nothing calls.
A member already in the narrow scope is not counted again here, so each ratchet
moves on its own: the narrow baseline stays at zero.

Limitation (both scopes): uses are matched by NAME, not by symbol. Two members
with the same name in different classes share one caller count, so a dead one
can hide behind a live namesake. The gate under-reports; it never over-reports.

TEST ORACLES (DSCH-27)
----------------------
Some predicates exist so a test can hold shipped data to a rule (IsGeneric,
IsRequested, IsCanonical ...). Production never calls them, by design. Mark one
in the comment block directly above its declaration (above a /// doc is fine):

    // D1: test-oracle - StingTools.Tags.Tests/ProdResolverSourceTotalityTests.cs

The marker is checked, not trusted: the named test file must exist and call the
member, and the member must still have no production caller. A stale marker
fails the gate. Counting test projects as callers instead would hide predicates
that are tested but never used by the product - the defect this gate exists for.

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
WIDE_BASELINE = os.path.join(REPO, "tools", "declared_uncalled_wide_baseline.txt")

SKIP_DIRS = {"obj", "bin", ".git", "Data", "_template_sources", "_workflow_sources"}

NARROW_PREFIXES = r"Is|Has|Can|Should|Try|Get|Resolve|Lookup|Map|Find|Validate|Check"
WIDE_PREFIXES = NARROW_PREFIXES + r"|Load|Build"

# A public static method or property that looks like a MAP or a PREDICATE —
# the two shapes that silently do nothing when uncalled. Constructors, Execute and
# event handlers are excluded: they are invoked by Revit, not by our code.
# DSCH-12: the prefix must be followed by an upper-case letter or digit, so `Is`
# no longer matches IsoSizes / IsoTextTiers and `Resolve` no longer matches
# ResolvedUniversalExtras. The character after the name says what it is:
# `(` a method (needs a call), `{` / `=` a property or field (needs a use).
DECL = re.compile(
    r'^\s*public\s+static\s+(?:readonly\s+)?[\w<>,\[\]\?\. ]+\s+'
    r'(?P<name>(?:' + NARROW_PREFIXES + r')(?=[A-Z0-9_])\w+)'
    r'\s*(?P<kind>[\(\{=])'
)

# DSCH-40 wide form: the type may contain a tuple `( ... )` (also inside a generic,
# `Dictionary<string, (int, int)>`), and Load* / Build* names count. The type is
# matched lazily up to the last whitespace before the name.
WIDE_DECL = re.compile(
    r'^\s*public\s+static\s+(?:readonly\s+)?[\w<>,\[\]\?\.\(\) ]+?\s+'
    r'(?P<name>(?:' + WIDE_PREFIXES + r')(?=[A-Z0-9_])\w+)'
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


# A reviewed exemption: "// D1: test-oracle - <repo-relative test file>".
MARKER = re.compile(r'^\s*//\s*D1:\s*test-oracle\s*[-–—]+\s*(?P<file>\S+\.cs)\s*$')


def marker_above(lines, idx):
    """The test file named by a D1 marker in the comment / attribute block directly
    above line index idx (0-based), or None."""
    j = idx - 1
    while j >= 0:
        m = MARKER.match(lines[j])
        if m:
            return m.group("file")
        s = lines[j].strip()
        if s.startswith("//") or s.startswith("["):
            j -= 1
            continue
        return None
    return None


def check_marker(name, is_meth, test_rel, prod_callers, read_text):
    """None when the marker is valid, else why it is stale."""
    if prod_callers > 0:
        return f"has {prod_callers} production caller(s) - remove the marker"
    text = read_text(test_rel)
    if text is None:
        return f"names {test_rel}, which does not exist"
    pat = re.compile(r'\b' + re.escape(name) + (r'\s*\(' if is_meth else r'\b'))
    code = "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("//"))
    if not pat.search(code):
        return f"names {test_rel}, which does not use it"
    return None


def _read_repo(rel):
    try:
        with open(os.path.join(REPO, rel), encoding="utf-8", errors="replace") as fh:
            return fh.read()
    except OSError:
        return None


def scope(wide):
    """(declaration regex, root directory) for a scope."""
    return (WIDE_DECL, PLUGIN) if wide else (DECL, CORE)


def collect(decl_re, sources, skip=frozenset()):
    """Declarations matched by decl_re in sources: an iterable of (relpath, lines).
    Returns (declared, is_method, markers). Names in skip are left out."""
    declared, is_method, markers = {}, {}, {}
    for rel, lines in sources:
        for i, line in enumerate(lines, 1):
            m = decl_re.match(line)
            if not m:
                continue
            name = m.group("name")
            if name in EXCLUDE_NAMES or name in declared or name in skip:
                continue
            declared[name] = (rel, i)
            is_method[name] = m.group("kind") == "("
            test_rel = marker_above(lines, i - 1)
            if test_rel:
                markers[name] = test_rel
    return declared, is_method, markers


def count_callers(declared, is_method, sources):
    """Uses of each declared name in sources, ONE tokenised pass (DSCH-12). A method
    needs `Name(`; a property or field needs `Name`. Prose and the declaration line
    itself do not count."""
    callers = collections.Counter()
    for rel, lines in sources:
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
    return callers


def read_sources(root):
    out = []
    for full in cs_files(root):
        rel = os.path.relpath(full, REPO).replace("\\", "/")
        try:
            out.append((rel, open(full, encoding="utf-8", errors="replace").readlines()))
        except OSError:
            continue
    return out


def self_test():
    """Prove the checks can fail - a gate that cannot fail reports green forever."""
    files = {
        "T/OracleTests.cs": 'class X { void T() { Assert.True(Foo.IsThing("a")); } }',
        "T/Other.cs": "// Foo.IsThing(\"a\") mentioned only in a comment\nclass Y { }",
    }
    rd = files.get
    decl = ["    // D1: test-oracle - T/OracleTests.cs\n",
            "    /// <summary>doc</summary>\n",
            "    public static bool IsThing(string s) => true;\n"]
    failures = []
    if marker_above(decl, 2) != "T/OracleTests.cs":
        failures.append("a marker above a /// doc block was not found")
    if marker_above(["    int x;\n", decl[2]], 1) is not None:
        failures.append("a marker was found where there is none")
    if check_marker("IsThing", True, "T/OracleTests.cs", 0, rd) is not None:
        failures.append("a valid marker was rejected")
    if check_marker("IsThing", True, "T/Missing.cs", 0, rd) is None:
        failures.append("a marker naming a missing test file was accepted")
    if check_marker("IsThing", True, "T/Other.cs", 0, rd) is None:
        failures.append("a marker naming a test that only mentions it in a comment was accepted")
    if check_marker("IsThing", True, "T/OracleTests.cs", 2, rd) is None:
        failures.append("a marker on a member with production callers was accepted")

    # DSCH-40: each blind spot of the narrow scan is seen by the wide one, and
    # the narrow scan is unchanged (it still does not see them).
    blind = {
        "tuple return type": "    public static (int Min, int Max)? GetSeqRange(string d) => null;\n",
        "tuple inside a generic": "    public static Dictionary<string, (int, int)> GetRanges() => null;\n",
        "Load* name": "    public static List<Row> LoadRows(string path) => null;\n",
        "Build* name": "    public static string BuildKey(string a) => a;\n",
    }
    for label, line in blind.items():
        if not WIDE_DECL.match(line):
            failures.append(f"wide scan does not see a {label}")
        if DECL.match(line):
            failures.append(f"narrow scan now sees a {label} - its ratchet would move")
    plain = "    public static bool IsThing(string s) => true;\n"
    if not (DECL.match(plain) and WIDE_DECL.match(plain)):
        failures.append("a plain predicate is not seen by both scans")
    if WIDE_DECL.match("    public static void Loader() { }\n") or \
       WIDE_DECL.match("    public static int Buildings = 3;\n"):
        failures.append("Load / Build matched without a following capital (Loader, Buildings)")
    root_wide, root_narrow = scope(True)[1], scope(False)[1]
    if os.path.normpath(root_wide) != os.path.normpath(PLUGIN) or \
       os.path.normpath(root_narrow) != os.path.normpath(CORE):
        failures.append("scan roots are wrong (wide must be StingTools/, narrow Core/)")
    # A member outside Core/ with no caller is counted by the wide scope only.
    srcs = [("StingTools/Docs/X.cs", ["    public static string BuildThing(int n) => \"\";\n"]),
            ("StingTools/Core/Y.cs", ["    public static bool IsUsed() => true;\n",
                                      "    void F() { if (IsUsed()) { } }\n"])]
    d, im, _ = collect(WIDE_DECL, srcs)
    c = count_callers(d, im, srcs)
    if sorted(n for n in d if c[n] == 0) != ["BuildThing"]:
        failures.append("an uncalled member outside Core/ was not counted, or a called one was")
    d2, _, _ = collect(WIDE_DECL, srcs, skip={"IsUsed"})
    if "IsUsed" in d2:
        failures.append("a narrow-scope member was counted again by the wide scope")

    if failures:
        print("SELF-TEST FAILED:")
        for f in failures:
            print("  [FAIL] " + f)
        return 1
    print("OK - self-test: all 6 marker cases and all 12 wide-scope cases behave")
    return 0


def read_baseline(path):
    base, names = 0, set()
    try:
        for line in open(path, encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("- "):
                names.add(line[2:].strip())
            elif base == 0:
                base = int(line)
    except (OSError, ValueError):
        pass
    return base, names


def main():
    if "--self-test" in sys.argv:
        return self_test()

    wide = "--wide" in sys.argv
    plugin_sources = read_sources(PLUGIN)
    core_prefix = os.path.relpath(CORE, REPO).replace("\\", "/") + "/"

    # 1. Collect candidate declarations. The wide scope leaves out every member the
    #    narrow scope already owns, so the two ratchets are independent.
    narrow_sources = [(r, l) for r, l in plugin_sources if r.startswith(core_prefix)]
    narrow_decl, _, _ = collect(DECL, narrow_sources)
    if wide:
        declared, is_method, markers = collect(WIDE_DECL, plugin_sources, skip=set(narrow_decl))
    else:
        declared, is_method, markers = collect(DECL, narrow_sources)

    # 2. Count uses across the WHOLE plugin.
    callers = count_callers(declared, is_method, plugin_sources)

    # 3. Reviewed test oracles leave the count only while the marker is still true.
    stale, oracles = [], set()
    for name, test_rel in sorted(markers.items()):
        why = check_marker(name, is_method[name], test_rel, callers[name], _read_repo)
        if why:
            stale.append((name, why))
        else:
            oracles.add(name)

    uncalled = sorted(n for n in declared if callers[n] == 0 and n not in oracles)

    label = "WIDE: tuples, all of StingTools/, Load*/Build*" if wide else "Core map/predicate"
    where = "StingTools/ (outside the narrow scope)" if wide else "Core"
    print("=" * 72)
    print(f"Declared-but-uncalled gate (D.1) - {label}")
    print("=" * 72)
    print(f"  public static declarations in {where:40}: {len(declared)}")
    print(f"  with ZERO callers anywhere in the plugin{'':15}: {len(uncalled)}")
    print(f"  reviewed test oracles (D1 marker, verified){'':12}: {len(oracles)}")
    if stale:
        print("\nFAIL: stale D1 test-oracle marker(s):")
        for name, why in stale:
            rel, ln = declared[name]
            print(f"  {name:34} {rel}:{ln} - {why}")
        return 1

    if "--report" in sys.argv:
        for n in sorted(oracles):
            rel, ln = declared[n]
            print(f"  oracle: {n:34} {rel}:{ln} <- {markers[n]}")
        print("\n--- UNCALLED ---")
        for n in uncalled:
            rel, ln = declared[n]
            print(f"  {n:34} {rel}:{ln}")

    baseline = WIDE_BASELINE if wide else BASELINE
    if "--write-baseline" in sys.argv:
        with open(baseline, "w", encoding="utf-8", newline="\n") as fh:
            if wide:
                fh.write("# D.1 WIDE declared-but-uncalled ceiling (DSCH-40). RATCHET: may fall, never rise.\n")
                fh.write("# Public static members anywhere in StingTools/ (tuple types, Load*/Build*\n")
                fh.write("# names included) that nothing calls, outside the narrow Core scope.\n")
            else:
                fh.write("# D.1 declared-but-uncalled ceiling. RATCHET: may fall, never rise.\n")
                fh.write("# A public static map or predicate in Core that nothing calls is a\n")
                fh.write("# mechanism that cannot fire. Seven such defects this workstream.\n")
            fh.write(f"{len(uncalled)}\n")
            # The names, so "which ones are new" is a diff, not archaeology.
            for n in uncalled:
                fh.write(f"- {n}\n")
        print(f"\nwrote baseline = {len(uncalled)} ({os.path.relpath(baseline, REPO)})")
        return 0

    base, base_names = read_baseline(baseline)
    print(f"  baseline{'':47}: {base}")
    if len(uncalled) > base:
        print(f"\nFAIL: {len(uncalled) - base} new uncalled declaration(s). Run with --report"
              + (" --wide." if wide else "."))
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
