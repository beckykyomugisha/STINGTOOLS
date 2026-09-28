#!/usr/bin/env python3
"""Every shipped shared-parameter file must agree with MR_PARAMETERS.txt.

WHY THIS EXISTS

Revit keys a shared parameter on its GUID and gives each GUID exactly one data type.
StingTools ships several shared-parameter files (MR_PARAMETERS.txt, which Load Shared
Parameters binds, plus STING_PARAMS_V4/V6, the hanger, sleeve, wire, LPS and title-block
files used to author families or loaded by hand). On 2026-09-28 ten parameters had the
same GUID but a different type in two of them -- ASS_LENGTH_TOTAL_MM was NUMBER in one
and LENGTH in another. A family authored from one file then will not load into a project
bound from the other ("LoadFamily back into project failed", see
Core/SharedParamTypeConflict.cs), and a writer that stores millimetres is right for one
type and 304.8 times wrong for the other.

WHAT IT CHECKS

  * a name defined in two files has the same GUID and the same type in both;
  * a GUID defined in two files has the same name in both.

MR_PARAMETERS.txt is the reference: a side file that disagrees is the one to fix.

    python tools/check_shared_param_files.py      # exit 1 on any conflict
"""
import collections
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "StingTools" / "Data"
REFERENCE = DATA / "MR_PARAMETERS.txt"


def read(path):
    raw = path.read_bytes()
    text = raw.decode("utf-16") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else raw.decode("utf-8-sig", errors="replace")
    for line in text.splitlines():
        c = line.split("\t")
        if c[0] == "PARAM" and len(c) > 3:
            yield c[2], c[1].lower(), c[3]


def main():
    files = sorted(DATA.rglob("*.txt"), key=lambda p: (p != REFERENCE, str(p)))
    by_name = collections.defaultdict(list)
    by_guid = collections.defaultdict(list)
    for f in files:
        rel = f.relative_to(DATA).as_posix()
        seen = set()
        for name, guid, dtype in read(f):
            if (name, guid) in seen:
                continue
            seen.add((name, guid))
            by_name[name].append((rel, guid, dtype))
            by_guid[guid].append((rel, name))
    problems = []
    for name, defs in sorted(by_name.items()):
        if len({(g, t) for _, g, t in defs}) > 1:
            problems.append("%s: %s" % (name, "; ".join("%s %s %s" % d for d in defs)))
    for guid, defs in sorted(by_guid.items()):
        if len({n for _, n in defs}) > 1:
            problems.append("GUID %s: %s" % (guid, "; ".join("%s %s" % d for d in defs)))
    if problems:
        print("Shared-parameter files disagree (%d). MR_PARAMETERS.txt is the reference:" % len(problems))
        for p in problems:
            print("  " + p)
        return 1
    print("shared-parameter files agree (%d files, %d names)" % (len(files), len(by_name)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
