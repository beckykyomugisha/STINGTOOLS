# -*- coding: utf-8 -*-
"""MOB-11 -- every parameter NAMED IN SHIPPED CONFIGURATION must exist.

THE SHAPE THIS CLOSES
Code names a parameter as a string literal. ParameterHelpers.SetString returns
false when the parameter is absent from the element, not bound to its category,
or not TEXT. The caller does not check the return. The value is discarded with no
log line and no error.

Every symptom is an ABSENCE. A missing value looks exactly like a value nobody
supplied, so it gets attributed to incomplete source data rather than to the
writer. It also survives a round-trip test, because a map compared only against
itself is consistent whether or not either side exists.

WHAT THIS CHECKS, AND WHAT IT DELIBERATELY DOES NOT
Checked: every parameter named in the shipped configuration below EXISTS in
MR_PARAMETERS.txt. A name that is not in the shared-parameter file can never be
written, whatever the writer does, so this is the one assertion that holds for
every source regardless of how the name is used.

NOT checked: that the DATATYPE is TEXT. The ROADMAP row proposed it, and it is
wrong as a blanket rule -- NativeParamMapper writes BLE_CEILING_HEIGHT_MM
(LENGTH) and HVC_EFF_RATIO_NR (NUMBER) through MapDimension/MapLookup, which are
numeric writers. TEXT is required of SetString targets, not of named parameters
in general, and this gate cannot tell which writer a name reaches.

NOT checked: that the binding is right. Narrow bindings are REPORTED and must be
declared in the baseline, so the narrow case is visible rather than silent -- but
widening a binding is a registry decision, not this gate's.

THE BASELINE ONLY SHRINKS
The count is NOT written here. It was, as "seventeen", in this docstring and in
.github/workflows/param-name-targets.yml, while the baseline held sixteen -- one
fact in three places, two of them prose nothing could check. The number is now
printed at runtime from the baseline itself, and _no_hardcoded_count() below
fails this gate if either file starts stating one again.

The baselined names mostly have several plausible corrections that
differ by unit or by which element carries the value -- ELC_POWER could be
ELC_PWR_KW or ELC_PWR_TXT; HVC_PRESSURE could be HVC_PRESSURE_DROP_PA or
HVC_PIPE_PRESSURE_KPA. Choosing one is an authoring decision, and choosing wrong
writes a number into the wrong field, which is worse than writing none. So they
are baselined with what is known about each, not silently repaired.

Run:  python tools/check_param_name_targets.py
"""
from __future__ import annotations

import csv
import io
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "StingTools" / "Data"
BASELINE = Path(__file__).resolve().parent / "param_name_targets_baseline.txt"


# ── the authority ────────────────────────────────────────────────────────────

def load_txt_params():
    """name -> DATATYPE, from the Revit shared-parameter file."""
    out = {}
    for line in io.open(DATA / "MR_PARAMETERS.txt", encoding="utf-8", errors="replace"):
        f = line.rstrip("\n").split("\t")
        if f and f[0] == "PARAM" and len(f) >= 4:
            out[f[2].strip()] = f[3].strip()
    return out


def load_bindings():
    """name -> categories string ('<ALL>' for universal), from the deployable spec."""
    out = {}
    p = DATA / "RESOLVED_BINDINGS.csv"
    for row in csv.reader(io.open(p, encoding="utf-8")):
        if not row or row[0].startswith("#") or len(row) < 2:
            continue
        out[row[0].strip()] = row[1].strip()
    return out


# ── the sources that name parameters ─────────────────────────────────────────
#
# Each returns (source label, [(parameter name, where it was found)]).
# Extraction is by CALL SHAPE, never by a loose "looks like a parameter" regex --
# a broad pattern picks up SourceType = "STING_SHARED" and reports a defect that
# is not there, which is how a gate loses its readers.

def src_cobie_attribute_templates():
    rows = []
    p = DATA / "COBIE_ATTRIBUTE_TEMPLATES.csv"
    with io.open(p, encoding="utf-8-sig", newline="") as fh:
        for i, r in enumerate(csv.DictReader(fh), start=2):
            key = (r.get("StingParamKey") or "").strip()
            if key:
                rows.append((key, "%s:%d" % (p.name, i)))
    return "COBIE_ATTRIBUTE_TEMPLATES.csv (StingParamKey)", rows


def _cs_calls(rel, pattern):
    rows = []
    p = ROOT / rel
    if not p.exists():
        return rows
    for i, line in enumerate(io.open(p, encoding="utf-8", errors="replace"), start=1):
        for m in pattern.finditer(line):
            rows.append((m.group(1), "%s:%d" % (os.path.basename(rel), i)))
    return rows


def src_exlink_default_links():
    # PropertyDef Sting(string paramName, ...) -- the parameter an ExLink profile
    # exports from and imports into.
    pat = re.compile(r'\bSting\(\s*"([A-Za-z0-9_]+)"')
    return ("ExLinkDefaultLinks.cs (Sting profile properties)",
            _cs_calls("StingTools/ExLink/ExLinkDefaultLinks.cs", pat))


def src_native_param_mapper():
    # MapDimension(el, bip, "TARGET", ...) / MapLookup(el, "source", "TARGET", ...)
    # / MapStringParam(el, "source", "TARGET")
    pats = [
        re.compile(r'\bMapDimension\(\s*el\s*,\s*[^,]+,\s*"([A-Za-z0-9_]+)"'),
        re.compile(r'\bMapLookup\(\s*el\s*,\s*"[^"]*"\s*,\s*"([A-Za-z0-9_]+)"'),
        re.compile(r'\bMapStringParam\(\s*el\s*,\s*"[^"]*"\s*,\s*"([A-Za-z0-9_]+)"'),
    ]
    rows = []
    for pat in pats:
        rows += _cs_calls("StingTools/Core/ParameterHelpers.cs", pat)
    return "NativeParamMapper (ParameterHelpers.cs)", rows


def src_fohlio_map():
    rel = "project-templates/KUT/_BIM_COORD/fohlio_map.json"
    p = ROOT / rel
    if not p.exists():
        return "fohlio_map.json", []
    doc = json.load(io.open(p, encoding="utf-8"))
    found = []

    def walk(node, path):
        if isinstance(node, dict):
            for k, v in node.items():
                walk(v, path + "/" + str(k))
        elif isinstance(node, list):
            for v in node:
                walk(v, path)
        elif isinstance(node, str) and re.fullmatch(r"[A-Z][A-Z0-9]{1,7}_[A-Z0-9_]{2,}", node):
            found.append((node, "fohlio_map.json%s" % path))

    walk(doc, "")
    return "fohlio_map.json", found


SOURCES = [
    src_cobie_attribute_templates,
    src_exlink_default_links,
    src_native_param_mapper,
    src_fohlio_map,
]


# ── baseline ─────────────────────────────────────────────────────────────────

def load_baseline():
    """name -> note. Blank lines and '#' comments ignored; format is NAME<TAB>note."""
    out = {}
    if not BASELINE.exists():
        return out
    for line in io.open(BASELINE, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        name, _, note = line.partition("\t")
        out[name.strip()] = note.strip()
    return out


COUNT_IN_PROSE = re.compile(
    r"\b(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|"
    r"thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|"
    r"\d{1,3})\s+names?\s+(?:are|is)\s+(?:wrong|bad|baselined)",
    re.IGNORECASE)


def _no_hardcoded_count() -> list:
    """Refuse a second copy of a number this gate already knows.

    A spelled-out count of them sat in this file and in the workflow while
    the baseline held sixteen. Prose cannot be gated, so the fix is not to correct
    it to sixteen -- it is to stop writing it down anywhere but the baseline, and
    to fail when someone writes it down again.
    """
    here = Path(__file__).resolve()
    targets = [
        here,
        here.parent.parent / ".github" / "workflows" / "param-name-targets.yml",
    ]
    bad = []
    for p in targets:
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for i, line in enumerate(text.splitlines(), 1):
            if COUNT_IN_PROSE.search(line):
                bad.append("%s:%d: %s" % (p.name, i, line.strip()))
    return bad


def main() -> int:
    txt = load_txt_params()
    bindings = load_bindings()
    baseline = load_baseline()

    # Guard the instrument before trusting a single finding. A checker whose
    # authority failed to parse reports every name as missing, which is a broken
    # checker wearing a finding's clothes.
    if len(txt) < 3000:
        print("MR_PARAMETERS.txt parsed only %d parameters -- the reader is wrong, "
              "not the data." % len(txt), file=sys.stderr)
        return 2
    if len(bindings) < 2000:
        print("RESOLVED_BINDINGS.csv parsed only %d rows -- the reader is wrong."
              % len(bindings), file=sys.stderr)
        return 2

    missing = []          # (name, source, where)
    narrow = []           # (name, source, where, categories)
    named_total = 0
    seen_names = set()

    for fn in SOURCES:
        label, rows = fn()
        for name, where in rows:
            named_total += 1
            seen_names.add(name)
            if name not in txt:
                missing.append((name, label, where))
            else:
                cats = bindings.get(name)
                if cats is not None and cats != "<ALL>":
                    narrow.append((name, label, where, cats))

    unexpected = [(n, s, w) for (n, s, w) in missing if n not in baseline]
    fixed = sorted(set(baseline) - seen_names)
    now_present = sorted(n for n in baseline if n in seen_names and n in txt)

    print("Parameter-name target gate")
    print("  names found in shipped configuration : %d (%d distinct)"
          % (named_total, len(seen_names)))
    print("  absent from MR_PARAMETERS.txt        : %d" % len(missing))
    print("  of those, baselined                  : %d" % (len(missing) - len(unexpected)))
    print("  narrowly bound (declared, not failed): %d" % len(narrow))
    print()

    rc = 0

    restated = _no_hardcoded_count()
    if restated:
        rc = 1
        print("FAIL: the baseline size is written in prose as well as in the baseline.")
        for line in restated:
            print("    " + line)
        print()
        print("  This gate already knows how many names are baselined and prints it")
        print("  below. A second copy in a comment cannot be checked, and the last one")
        print("  said seventeen while the file held sixteen. Delete the number; say")
        print("  \"the names that are wrong today are baselined in ...\" instead.")
        print()

    if unexpected:
        rc = 1
        print("FAIL: %d parameter name(s) do not exist in MR_PARAMETERS.txt and are not "
              "baselined:" % len(unexpected))
        for n, s, w in sorted(unexpected):
            print("    %-34s %s   [%s]" % (n, w, s))
        print()
        print("  A name that is not in the shared-parameter file can never be written.")
        print("  Add the parameter to MR_PARAMETERS.txt (and regenerate the CSV with")
        print("  tools/sync_csv_from_txt.py), or correct the name at the call site.")
        print("  Do NOT add it to the baseline to make this pass -- the baseline records")
        print("  what was already broken when the gate was written, and only shrinks.")
        print()

    if now_present:
        rc = 1
        print("FAIL: %d baselined name(s) now EXIST. Delete them from" % len(now_present))
        print("      tools/param_name_targets_baseline.txt in the same commit that fixed")
        print("      them, so the baseline can only shrink:")
        for n in now_present:
            print("    %s" % n)
        print()

    if fixed:
        rc = 1
        print("FAIL: %d baselined name(s) no longer appear in any source. Delete them"
              % len(fixed))
        print("      from the baseline -- a baseline entry for a name nobody uses is a")
        print("      claim about code that is gone:")
        for n in fixed:
            print("    %s" % n)
        print()

    if narrow:
        print("Narrowly-bound targets (reported, not failed -- widening a binding is a")
        print("registry decision). A value written to one of these on an element outside")
        print("its categories is discarded exactly as silently as a missing parameter:")
        for n, s, w, cats in sorted(set(narrow)):
            print("    %-34s %s" % (n, cats[:88]))
        print()

    if rc == 0:
        print("Parameter-name target gate OK.")
        print("  Every name in shipped configuration exists, or is baselined with a reason.")
        print("  Baselined names awaiting a correction: %d" % len(baseline))
    return rc


if __name__ == "__main__":
    sys.exit(main())
