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

  Add --wide to any of the first three for the WIDE scope (below), --instance for
  instance methods, --fields for const / static readonly fields (DSCH-46b).

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

INSTANCE (baseline tools/declared_uncalled_instance_baseline.txt, DSCH-46b):
  public / internal INSTANCE methods anywhere in StingTools/ with the wide name
  prefixes. Two sources of false positives are handled, not baselined:
    - an override, or a member of a type whose base list names an interface the
      plugin does not declare (Revit's IDirectContext3DServer, WPF's ICommand ...)
      - the host calls those, by contract;
    - a method used as a method group (`.Where(IsExcluded)`, `+= OnX`) or named in
      XAML - so ANY use of the name counts, not only `Name(`.
FIELDS (baseline tools/declared_unread_fields_baseline.txt, DSCH-46b):
  public / internal const and static readonly fields anywhere in StingTools/ that
  nothing reads - the shape of the 253 ParamRegistry *_GUID constants deleted in
  DSCH-46b. Any use of the name counts, XAML (x:Static) included.
Both use the same D1 test-oracle markers and the same ratchet rule.

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
INSTANCE_BASELINE = os.path.join(REPO, "tools", "declared_uncalled_instance_baseline.txt")
FIELDS_BASELINE = os.path.join(REPO, "tools", "declared_unread_fields_baseline.txt")

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

# DSCH-46b instance form: not static, not an override / abstract member.
INSTANCE_DECL = re.compile(
    r'^\s*(?:public|internal)\s+(?!static\b)(?!override\b)(?!abstract\b)(?!const\b)(?!readonly\b)'
    r'(?:(?:virtual|async|new|sealed|unsafe|extern)\s+)*'
    r'[\w<>,\[\]\?\.\(\) ]+?\s+'
    r'(?P<name>(?:' + WIDE_PREFIXES + r')(?=[A-Z0-9_])\w+)'
    r'\s*(?P<kind>\()'
)

# DSCH-46b fields form: const or static readonly, any name.
FIELD_DECL = re.compile(
    r'^\s*(?:public|internal)\s+(?:const|static\s+readonly|readonly\s+static)\s+'
    r'[\w<>,\[\]\?\.\(\) ]+?\s+'
    r'(?P<name>[A-Za-z_]\w*)\s*(?P<kind>=)'
)

# A type declaration and its base list, for the external-interface skip.
TYPE_DECL = re.compile(r'^\s*(?:[\w\s]*\s)?(?:class|struct|record)\s+\w+(?:<[^>]*>)?\s*(?::\s*(?P<bases>[^{/]*))?')
INTERFACE_DECL = re.compile(r'\binterface\s+(?P<name>I[A-Z]\w*)')

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


def declared_interfaces(sources):
    """Interface names the plugin itself declares."""
    out = set()
    for _rel, lines in sources:
        for line in lines:
            m = INTERFACE_DECL.search(line)
            if m:
                out.add(m.group("name"))
    return out


def external_interface_members(decl_re, sources, own_interfaces):
    """Names declared (by decl_re) inside a type whose base list names an
    interface the plugin does not declare - Revit / WPF / .NET call those by
    contract. The enclosing type is the nearest type declaration above, which
    is right except after a nested type; that can only HIDE a member, never
    report one falsely."""
    skip = set()
    for _rel, lines in sources:
        external = False
        for line in lines:
            t = TYPE_DECL.match(line)
            if t and ("class " in line or "struct " in line or "record " in line):
                bases = t.group("bases") or ""
                names = re.findall(r'\b(I[A-Z]\w*)', bases)
                external = any(n not in own_interfaces for n in names)
                continue
            if external:
                m = decl_re.match(line)
                if m:
                    skip.add(m.group("name"))
    return skip


def count_uses(declared, sources, extra_tokens=None):
    """Any use of the name - a call, a method group, a field read - outside the
    declaration line and prose. extra_tokens: a Counter of names used elsewhere
    (XAML)."""
    uses = collections.Counter()
    for rel, lines in sources:
        for i, line in enumerate(lines, 1):
            s = line.lstrip()
            if s.startswith(("//", "///", "*")):
                continue
            seen = set()
            for m in IDENT.finditer(line):
                name = m.group(1)
                if name not in declared or name in seen or declared[name] == (rel, i):
                    continue
                seen.add(name)
                uses[name] += 1
    if extra_tokens:
        for name in declared:
            uses[name] += extra_tokens.get(name, 0)
    return uses


def xaml_tokens(root):
    c = collections.Counter()
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.endswith(".xaml"):
                try:
                    c.update(re.findall(r'\b[A-Za-z_]\w*\b',
                                        open(os.path.join(dirpath, fn), encoding="utf-8", errors="replace").read()))
                except OSError:
                    pass
    return c


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

    # DSCH-46b: instance methods and fields.
    inst_src = [("StingTools/A.cs", [
        "    public class Engine\n",
        "    {\n",
        "        public bool IsDead(int x) => x > 0;\n",
        "        public bool IsUsedAsGroup(int x) => x > 0;\n",
        "        public override string GetHashText() => \"\";\n",
        "        void F(System.Collections.Generic.List<int> l) { l.Where(IsUsedAsGroup); }\n",
        "    }\n",
        "    internal class Server : IDirectContext3DServer\n",
        "    {\n",
        "        public string GetVendorId() => \"x\";\n",
        "    }\n",
        "    public interface IOwn { bool IsOwn(); }\n",
        "    public class Impl : IOwn\n",
        "    {\n",
        "        public bool IsOwn() => true;\n",
        "    }\n",
        "    public static class Consts\n",
        "    {\n",
        "        public const string DEAD_GUID = \"x\";\n",
        "        public static readonly int[] UsedSizes = { 1 };\n",
        "        public const string XAML_KEY = \"k\";\n",
        "        static int G() => UsedSizes[0];\n",
        "    }\n",
    ])]
    if INSTANCE_DECL.match("    public static bool IsThing() => true;\n"):
        failures.append("instance scan matched a static method")
    if INSTANCE_DECL.match("    public override bool IsThing() => true;\n"):
        failures.append("instance scan matched an override")
    d, _, _ = collect(INSTANCE_DECL, inst_src)
    d = {n: v for n, v in d.items()
         if n not in external_interface_members(INSTANCE_DECL, inst_src, declared_interfaces(inst_src))}
    u = count_uses(d, inst_src)
    dead_inst = sorted(n for n in d if u[n] == 0)
    if "GetVendorId" in d:
        failures.append("a member of a type implementing an external interface was scanned")
    if "IsUsedAsGroup" in dead_inst:
        failures.append("a method used as a method group was reported uncalled")
    if "IsDead" not in dead_inst:
        failures.append("an uncalled instance method was not reported")
    if "IsOwn" not in d:
        failures.append("a member of a plugin-declared interface was skipped (only external ones are)")
    fd, _, _ = collect(FIELD_DECL, inst_src)
    fu = count_uses(fd, inst_src, collections.Counter({"XAML_KEY": 1}))
    dead_f = sorted(n for n in fd if fu[n] == 0)
    if dead_f != ["DEAD_GUID"]:
        failures.append(f"fields scan reported {dead_f}, expected only DEAD_GUID (read and XAML-used fields are uses)")

    if failures:
        print("SELF-TEST FAILED:")
        for f in failures:
            print("  [FAIL] " + f)
        return 1
    print("OK - self-test: all 6 marker cases, all 12 wide-scope cases and all 8 instance/field cases behave")
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
    instance = "--instance" in sys.argv
    fields = "--fields" in sys.argv
    plugin_sources = read_sources(PLUGIN)
    core_prefix = os.path.relpath(CORE, REPO).replace("\\", "/") + "/"

    # 1. Collect candidate declarations. The wide scope leaves out every member the
    #    narrow scope already owns, so the two ratchets are independent.
    narrow_sources = [(r, l) for r, l in plugin_sources if r.startswith(core_prefix)]
    narrow_decl, _, _ = collect(DECL, narrow_sources)
    if instance:
        declared, is_method, markers = collect(INSTANCE_DECL, plugin_sources)
        ext = external_interface_members(INSTANCE_DECL, plugin_sources, declared_interfaces(plugin_sources))
        declared = {n: v for n, v in declared.items() if n not in ext}
    elif fields:
        declared, is_method, markers = collect(FIELD_DECL, plugin_sources)
    elif wide:
        declared, is_method, markers = collect(WIDE_DECL, plugin_sources, skip=set(narrow_decl))
    else:
        declared, is_method, markers = collect(DECL, narrow_sources)

    # 2. Count uses across the WHOLE plugin. Instance methods and fields count any
    #    use of the name (method groups, field reads, XAML); the static scopes keep
    #    their original call-shaped rule so their ratchets do not move.
    if instance or fields:
        callers = count_uses(declared, plugin_sources, xaml_tokens(PLUGIN))
    else:
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

    if instance:
        label, where = "INSTANCE methods, all of StingTools/", "StingTools/ (instance)"
    elif fields:
        label, where = "const / static readonly FIELDS, all of StingTools/", "StingTools/ (fields)"
    else:
        label = "WIDE: tuples, all of StingTools/, Load*/Build*" if wide else "Core map/predicate"
        where = "StingTools/ (outside the narrow scope)" if wide else "Core"
    print("=" * 72)
    print(f"Declared-but-uncalled gate (D.1) - {label}")
    print("=" * 72)
    print(f"  {'declarations' if (instance or fields) else 'public static declarations'} in {where:40}: {len(declared)}")
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

    baseline = (INSTANCE_BASELINE if instance else FIELDS_BASELINE if fields
                else WIDE_BASELINE if wide else BASELINE)
    if "--write-baseline" in sys.argv:
        with open(baseline, "w", encoding="utf-8", newline="\n") as fh:
            if instance:
                fh.write("# D.1 INSTANCE declared-but-uncalled ceiling (DSCH-46b). RATCHET: may fall, never rise.\n")
                fh.write("# Public/internal instance methods (wide name prefixes) anywhere in StingTools/\n")
                fh.write("# that nothing uses; overrides and external-interface members excluded.\n")
            elif fields:
                fh.write("# D.1 FIELDS declared-but-unread ceiling (DSCH-46b). RATCHET: may fall, never rise.\n")
                fh.write("# Public/internal const and static readonly fields anywhere in StingTools/\n")
                fh.write("# that nothing reads (XAML included).\n")
            elif wide:
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
              + (" --instance." if instance else " --fields." if fields else " --wide." if wide else "."))
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
