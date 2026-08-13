#!/usr/bin/env python3
"""
validate_command_context.py — a panel button gets a null ExternalCommandData.

WHY THIS EXISTS

StingCommandHandler.RunCommand<T> dispatches every dock-panel button like this:

    // Pass null for ExternalCommandData — commands use
    // StingCommandHandler.CurrentApp as fallback.
    cmd.Execute(null, ref message, elSet);

A command that reads commandData.Application therefore sees nothing. It either
throws NullReferenceException into RunCommand's catch, or takes its
"no document" branch and returns Result.Failed — and RunCommand DISCARDS the
`message`. Either way the user clicks and nothing happens, and the log says
only "start" then "done".

UniversalTagDiffCommand shipped with exactly that defect and was dead on its
first run in Revit. Measuring the rest of the plugin found 66 more.

WHY THE DISPATCH GATE DOES NOT COVER THIS

validate_dispatch_wiring.py proves a Tag= has a case. It cannot prove the case
does anything once entered. This is the same failure one layer further in, so it
needs its own check.

WHAT COUNTS AS SAFE

Any of the three fallback idioms already in the codebase:

    StingCommandHandler.CurrentApp      the raw static
    ParameterHelpers.GetApp(cd)         returns commandData.Application ?? CurrentApp
    ParameterHelpers.GetContext(cd)     the same, wrapped with UIDoc/Doc/View

GetApp is the one to reach for. A command in namespace StingTools.Core can call
it unqualified; anything else needs `using StingTools.Core;`.

An early version of this check reported 110 files. Forty-four of them routed
through GetContext, which falls back correctly — they were false positives, and
BuildTagsCommand was among them, a command in daily use. Hence SAFE_IDIOMS: a
gate that cries wolf gets ignored, which is the failure it exists to prevent.

USAGE
    python tools/validate_command_context.py            report + gate
    python tools/validate_command_context.py --list     name every finding
    python tools/validate_command_context.py --apply    re-baseline (deliberate)
"""

import argparse
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, "tools", "command_context_baseline.json")

SAFE_IDIOMS = ("CurrentApp", "GetContext(", "GetApp(")

DISPATCH_RE = re.compile(r"RunCommand(?:Public)?<([\w\.]+)>")
COMMANDDATA_RE = re.compile(r"commandData\s*\??\s*\.\s*Application")
CLASS_RE = re.compile(r"class\s+(\w+)\s*:\s*[^\{]*IExternalCommand")


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def dispatched_classes():
    """Every command class the panel runs with a null ExternalCommandData."""
    out = set()
    for path in glob.glob(os.path.join(ROOT, "StingTools", "UI", "**", "*.cs"), recursive=True):
        for m in DISPATCH_RE.finditer(read(path)):
            out.add(m.group(1).split(".")[-1])
    return out


def offenders(dispatched):
    """(class, file) for every panel-dispatched command with no fallback."""
    found = []
    for path in glob.glob(os.path.join(ROOT, "StingTools", "**", "*.cs"), recursive=True):
        if os.sep + "obj" + os.sep in path:
            continue
        text = read(path)
        if "IExternalCommand" not in text:
            continue
        if any(idiom in text for idiom in SAFE_IDIOMS):
            continue
        if not COMMANDDATA_RE.search(text):
            continue
        for m in CLASS_RE.finditer(text):
            if m.group(1) in dispatched:
                found.append((m.group(1), os.path.relpath(path, ROOT)))
    return sorted(found)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="re-baseline; use deliberately")
    ap.add_argument("--list", action="store_true", help="name every finding")
    args = ap.parse_args()

    dispatched = dispatched_classes()
    found = offenders(dispatched)

    print(f"  panel-dispatched command classes : {len(dispatched)}")
    print(f"  NO CONTEXT FALLBACK              : {len(found)}   <- gated")

    if args.list:
        for cls, path in found:
            print(f"    {cls:<46} {path}")

    if args.apply:
        with open(BASELINE, "w", encoding="utf-8") as fh:
            json.dump({"offenders": len(found)}, fh, indent=2)
        print(f"\n  baseline written: offenders = {len(found)}")
        return 0

    if not os.path.exists(BASELINE):
        print("\n  no baseline — run with --apply once to record the current count")
        return 0

    with open(BASELINE, "r", encoding="utf-8") as fh:
        ceiling = json.load(fh).get("offenders", 0)

    if len(found) > ceiling:
        print(f"\n  FAIL: {len(found) - ceiling} new command(s) with no context fallback "
              f"({len(found)} against a ceiling of {ceiling})")
        print("  A dock-panel button passes null for ExternalCommandData. Read the")
        print("  document through ParameterHelpers.GetApp(commandData) (or GetContext),")
        print("  not commandData.Application — otherwise the button does nothing.")
        for cls, path in found:
            print(f"    {cls:<46} {path}")
        return 1

    if len(found) < ceiling:
        print(f"\n  PASS — and the count fell from {ceiling} to {len(found)}.")
        print("  Run --apply to ratchet the ceiling down so it cannot drift back.")
        return 0

    print(f"\n  PASS at {len(found)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
