#!/usr/bin/env python3
"""
validate_dispatch_wiring.py — a command must be reachable, and a button must do something.

WHY THIS EXISTS

Three commands shipped with handler cases and no buttons (Tags_RepairPolluted,
TagStudio_SetTierDefaults, TagStudio_CategoryDryRun). The operator went looking
for them and found nothing. That is the eighth instance of the same shape: wired
on one side only, and reading as "implemented" to anyone grepping the dispatcher.

It has two directions and they are NOT equally serious:

  BUTTON WITHOUT CASE   the user clicks and nothing happens. Unambiguous defect.
                        HARD GATE, ratcheted.

  CASE WITHOUT BUTTON   the command is not on the panel. Often legitimate — it
                        may be reachable from the ribbon, a context menu, the
                        NLP processor, or WorkflowEngine.ResolveCommand. REPORT
                        ONLY until those surfaces are enumerated. Baselining a
                        number that is mostly noise trains people to ignore it,
                        which is what happened to the 603 readership figure
                        before bucket C was excluded at the scan.

WHAT COUNTS AS A COMMAND BUTTON

Only <Button …> elements that wire Click="Cmd_Click". Bare Tag= attributes are
used for data binding on ComboBoxItem, RadioButton and friends — an early
version of this check counted them and reported 298 dead buttons, of which most
were the literals 0, 1, 10, 100. The Click binding is what makes it a command.

USAGE
    python tools/validate_dispatch_wiring.py            report + gate
    python tools/validate_dispatch_wiring.py --list     also list every finding
    python tools/validate_dispatch_wiring.py --apply    re-baseline (deliberate)
"""

import argparse
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, "tools", "dispatch_wiring_baseline.json")

CASE_RE = re.compile(r'case\s+"([A-Za-z0-9_]+)"')
BUTTON_RE = re.compile(r"<Button\b[^>]*?>", re.S)
TAG_RE = re.compile(r'Tag="([A-Za-z0-9_]+)"')


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def collect_cases():
    """Every case label across every command handler."""
    out = {}
    for path in sorted(glob.glob(os.path.join(ROOT, "StingTools", "UI", "*CommandHandler.cs"))):
        for name in CASE_RE.findall(read(path)):
            out.setdefault(name, os.path.basename(path))
    return out


def collect_buttons():
    """Every <Button> that wires Click=Cmd_Click and carries a Tag."""
    out = {}
    pattern = os.path.join(ROOT, "StingTools", "UI", "**", "*.xaml")
    for path in sorted(glob.glob(pattern, recursive=True)):
        text = read(path)
        for match in BUTTON_RE.finditer(text):
            element = match.group(0)
            if "Cmd_Click" not in element:
                continue
            tag = TAG_RE.search(element)
            if tag:
                out.setdefault(tag.group(1), os.path.basename(path))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="re-baseline; use deliberately")
    ap.add_argument("--list", action="store_true", help="list every finding, not just counts")
    args = ap.parse_args()

    cases = collect_cases()
    buttons = collect_buttons()

    dead_buttons = sorted(set(buttons) - set(cases))       # click does nothing
    unreachable = sorted(set(cases) - set(buttons))        # not on the panel

    print(f"  case labels                 : {len(cases)}")
    print(f"  command buttons (Cmd_Click) : {len(buttons)}")
    print(f"  BUTTON WITHOUT CASE         : {len(dead_buttons)}   <- gated")
    print(f"  CASE WITHOUT BUTTON         : {len(unreachable)}   <- report only")

    if args.list:
        print("\n  dead buttons (clicking does nothing):")
        for name in dead_buttons:
            print(f"    {name:44} {buttons[name]}")
        print("\n  cases with no panel button (may be reachable elsewhere):")
        for name in unreachable:
            print(f"    {name:44} {cases[name]}")

    baseline = {"dead_buttons": len(dead_buttons)}
    if args.apply:
        with open(BASELINE, "w", encoding="utf-8") as fh:
            json.dump(baseline, fh, indent=2)
        print(f"\n  baseline written: dead_buttons = {len(dead_buttons)}")
        return 0

    if not os.path.exists(BASELINE):
        print("\n  no baseline — run with --apply once to record the current count")
        return 0

    with open(BASELINE, "r", encoding="utf-8") as fh:
        ceiling = json.load(fh).get("dead_buttons", 0)

    if len(dead_buttons) > ceiling:
        added = len(dead_buttons) - ceiling
        print(f"\n  FAIL: {added} new dead button(s) — {len(dead_buttons)} against a ceiling of {ceiling}")
        print("  A button whose Tag has no case in any command handler does nothing when clicked.")
        print("  Add the case, or remove the button. Re-baselining is not the fix.")
        return 1

    if len(dead_buttons) < ceiling:
        print(f"\n  PASS — and the count fell from {ceiling} to {len(dead_buttons)}.")
        print("  Run --apply to ratchet the ceiling down so it cannot drift back.")
        return 0

    print(f"\n  PASS at {len(dead_buttons)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
