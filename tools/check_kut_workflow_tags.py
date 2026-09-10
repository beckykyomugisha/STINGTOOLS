#!/usr/bin/env python3
"""Every step of every shipped KUT workflow must resolve to a real command.

WHY
A workflow step whose ``commandTag`` is not a ``case`` label in
``WorkflowEngine.ResolveCommand`` parses perfectly, runs, and does NOTHING. So does a
step that spells its keys ``command`` / ``name`` instead of ``commandTag`` / ``label``.
Neither produces an error, a warning or a log line: the run reports success having
skipped the step. On the KUT project those workflows are the fortnightly coordination
rhythm and the deliverable gates, so a silently-skipped step is a coordination cycle
nobody performed.

This is the house failure mode CLAUDE.md describes -- an absent side effect that looks
exactly like a completed one -- in the place where it costs a delivery gate.

WHAT IT CHECKS
  1. The extraction window is sound. ``ResolveCommand`` lives at
     WorkflowEngine.cs:1476-2356 and MUST contain exactly one ``switch``. If a future
     edit moves or splits the method, the window would silently start scraping ``case``
     labels from a neighbouring switch and report a HIGHER resolve rate than reality.
     A checker that widens its own view rather than failing is worse than no checker,
     so this aborts instead.
  2. It discriminates. A known-good tag must be found and a nonsense tag must not.
     Every gate on this project was wrong on its first run; this one says so out loud
     before it reports anything.
  3. Field names. Steps must use ``commandTag`` + ``label``. ``command`` / ``name``
     parse and go runtime-dead.
  4. Resolve rate. Every step's tag must appear as a ``case "Tag":`` label.

Usage:
    python tools/check_kut_workflow_tags.py [repo-root]      # default: cwd
Exit code 0 = every step of every KUT workflow resolves. Non-zero = something is
silently dead.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

# ResolveCommand's body. Deliberately hard-coded and then VERIFIED (check 1 below)
# rather than located by a regex that could drift onto another method.
RESOLVE_START = 1476
RESOLVE_END = 2356

WORKFLOW_GLOB = "StingTools/Data/WORKFLOW_KUT_*.json"
ENGINE = "StingTools/Core/WorkflowEngine.cs"

# Self-test fixtures. The good tag is a real, long-standing KUT command; the bad one
# is deliberately unspellable by accident.
SELFTEST_GOOD = "Fohlio_Export"
SELFTEST_BAD = "Fohlio_ExportZZZ_NOT_A_REAL_TAG"

CASE_RE = re.compile(r'^\s*case\s+"([^"]+)"\s*:', re.MULTILINE)
SWITCH_RE = re.compile(r'\bswitch\s*\(')


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(2)


def extract_case_labels(engine_path: Path) -> set[str]:
    lines = engine_path.read_text(encoding="utf-8", errors="replace").splitlines()
    if len(lines) < RESOLVE_END:
        fail(
            f"{engine_path} has only {len(lines)} lines; the ResolveCommand window "
            f"{RESOLVE_START}-{RESOLVE_END} no longer exists. The method has moved -- "
            "re-locate it and update RESOLVE_START/RESOLVE_END rather than widening the window."
        )
    window = "\n".join(lines[RESOLVE_START - 1 : RESOLVE_END])

    # Check 1: exactly one switch in the window.
    switches = SWITCH_RE.findall(window)
    if len(switches) != 1:
        fail(
            f"the window {engine_path}:{RESOLVE_START}-{RESOLVE_END} contains "
            f"{len(switches)} 'switch(' statements, expected exactly 1. Either "
            "ResolveCommand has moved, or a second switch is now inside the window and "
            "its case labels would inflate the resolvable set. Refusing to report a "
            "resolve rate from a window I cannot vouch for."
        )
    # Check 1b: it is the RIGHT switch, and the window still reaches its end. A window
    # that has drifted off the bottom would clip real cases and under-report; one that
    # has drifted off the top would scrape a neighbouring method. Both must be loud.
    if "switch (tag)" not in window and "switch(tag)" not in window:
        fail(
            f"the single switch in {engine_path}:{RESOLVE_START}-{RESOLVE_END} is not "
            "'switch (tag)'. The window is no longer looking at ResolveCommand."
        )
    if "default: return null;" not in window:
        fail(
            f"the window {engine_path}:{RESOLVE_START}-{RESOLVE_END} does not reach "
            "ResolveCommand's 'default: return null;'. It has drifted and would clip real "
            "case labels, reporting tags as unresolvable that in fact resolve -- or worse, "
            "the reverse after a later edit. Re-locate the method."
        )
    return set(CASE_RE.findall(window))


def selftest(labels: set[str]) -> None:
    good_ok = SELFTEST_GOOD in labels
    bad_ok = SELFTEST_BAD not in labels
    print("GATE SELF-TEST")
    print(f"  known-good {SELFTEST_GOOD!r:40} found : {good_ok}   (expect True)")
    print(f"  nonsense   {SELFTEST_BAD!r:40} found : {not bad_ok}  (expect False)")
    if not good_ok:
        fail(
            f"self-test: {SELFTEST_GOOD} was NOT found among {len(labels)} case labels. "
            "The extraction is broken, so a 100% resolve rate would be meaningless."
        )
    if not bad_ok:
        fail(f"self-test: the nonsense tag {SELFTEST_BAD} was 'found'. The extraction matches anything.")
    print("  gate discriminates correctly\n")


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    engine = root / ENGINE
    if not engine.is_file():
        fail(f"{engine} not found (run from the repo root, or pass the root as argv[1])")

    labels = extract_case_labels(engine)
    print(f"ResolveCommand case labels in {ENGINE}:{RESOLVE_START}-{RESOLVE_END}: {len(labels)}\n")
    selftest(labels)

    files = sorted((root / "StingTools" / "Data").glob("WORKFLOW_KUT_*.json"))
    if not files:
        fail(f"no files matched {WORKFLOW_GLOB} under {root} -- the glob or the tree has moved")

    problems: list[str] = []
    total_steps = 0
    total_resolved = 0

    print(f"{'File':<44} {'Steps':>6} {'Resolved':>9}")
    print("-" * 62)
    for path in files:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001 - a malformed workflow is a real failure
            problems.append(f"{path.name}: is not valid JSON ({exc})")
            continue

        steps = data.get("steps")
        if not isinstance(steps, list):
            problems.append(f"{path.name}: no 'steps' array")
            continue

        resolved = 0
        for i, step in enumerate(steps, start=1):
            if not isinstance(step, dict):
                problems.append(f"{path.name} step {i}: not an object")
                continue
            # Check 3: field names. 'command'/'name' parse and do nothing.
            for wrong, right in (("command", "commandTag"), ("name", "label")):
                if wrong in step and right not in step:
                    problems.append(
                        f"{path.name} step {i}: uses '{wrong}' where the engine reads "
                        f"'{right}' -- this step parses and silently does nothing"
                    )
            tag = step.get("commandTag")
            if not tag:
                problems.append(f"{path.name} step {i}: no 'commandTag'")
                continue
            if "label" not in step:
                problems.append(f"{path.name} step {i} ({tag}): no 'label'")
            # Check 4: the tag resolves.
            if tag in labels:
                resolved += 1
            else:
                problems.append(
                    f"{path.name} step {i}: commandTag '{tag}' is not a case label in "
                    "ResolveCommand -- the step would be skipped silently"
                )
        total_steps += len(steps)
        total_resolved += resolved
        pct = (100.0 * resolved / len(steps)) if steps else 0.0
        print(f"{path.name:<44} {len(steps):>6} {resolved:>6}/{len(steps)} ({pct:.0f}%)")

    print("-" * 62)
    pct = (100.0 * total_resolved / total_steps) if total_steps else 0.0
    print(f"{'TOTAL':<44} {total_steps:>6} {total_resolved:>6}/{total_steps} ({pct:.0f}%)")
    print(f"Files checked: {len(files)}")

    if problems:
        print(f"\n{len(problems)} problem(s):")
        for p in problems:
            print(f"  - {p}")
        print("\nFAIL")
        return 1

    print("\nOK: every step of every KUT workflow resolves to a real command.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
