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
  1. The extraction window is sound. ``ResolveCommand`` is located by its signature and
     brace-matched to its end (string literals and comments are blanked first, so a brace
     inside ``$"{x}"`` cannot end the method early), and the body MUST contain exactly one
     ``switch (tag)`` reaching ``default: return null;``. If the dispatcher is restructured,
     this aborts rather than scraping ``case`` labels from somewhere else and reporting a
     HIGHER resolve rate than reality. A checker that widens its own view rather than
     failing is worse than no checker.
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

# ResolveCommand's body is LOCATED, not hard-coded.
#
# It used to be the literal line range 1395-2244, verified to hold exactly one switch.
# That was safe but it cried wolf: adding a single `case` to ResolveCommand pushes its
# closing `default: return null;` past the end of the window, and the checker then refused
# to run - correctly, but on a change that was perfectly fine. A gate that goes red on
# unrelated edits gets switched off, and this one now runs in CI
# (.github/workflows/kut-workflow-tags.yml).
#
# So the window is derived: find the method by its SIGNATURE, brace-match to its end, and
# then apply exactly the same three assertions to whatever that region turns out to be.
# Anchoring on the signature is strictly stronger than a line range - a line range can
# drift onto a neighbouring method, a signature cannot.
RESOLVE_SIGNATURE = 'private static IExternalCommand ResolveCommand('

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


def strip_strings_and_comments(text: str) -> str:
    """Blank out string/char literals and comments, keeping length and newlines.

    Brace-matching a C# method has to ignore braces inside `$"{x}"` and inside a comment,
    or the method's end is found in the wrong place - and a window that ends in the wrong
    place is exactly the silent mis-measurement this gate exists to prevent.
    """
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if c == "/" and nxt == "*":
            while i < n and not (text[i] == "*" and i + 1 < n and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        if c == "@" and nxt == '"':                       # verbatim string
            out.append("  ")
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':   # "" escape
                        out.append("  ")
                        i += 2
                        continue
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        if c in ('"', "'"):                                # regular string / char literal
            quote = c
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == quote:
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def locate_resolve_command(text: str) -> tuple[int, int]:
    """Character offsets of ResolveCommand's body, found by signature + brace match."""
    blank = strip_strings_and_comments(text)
    hits = blank.count(RESOLVE_SIGNATURE)
    if hits != 1:
        fail(
            f"expected exactly 1 declaration of '{RESOLVE_SIGNATURE}...', found {hits}. "
            "Refusing to guess which one is the dispatcher."
        )
    start = blank.index(RESOLVE_SIGNATURE)
    brace = blank.index("{", start)
    depth = 0
    for i in range(brace, len(blank)):
        if blank[i] == "{":
            depth += 1
        elif blank[i] == "}":
            depth -= 1
            if depth == 0:
                return brace, i + 1
    fail("ResolveCommand's opening brace is never closed -- the file is truncated or unbalanced.")


def extract_case_labels(engine_path: Path) -> set[str]:
    text = engine_path.read_text(encoding="utf-8", errors="replace")
    lo, hi = locate_resolve_command(text)
    window = text[lo:hi]
    first_line = text.count("\n", 0, lo) + 1
    last_line = text.count("\n", 0, hi) + 1
    print(f"ResolveCommand located at {engine_path.name}:{first_line}-{last_line} "
          f"(by signature + brace match, not a hard-coded range)")

    # Check 1: exactly one switch in the located body.
    switches = SWITCH_RE.findall(window)
    if len(switches) != 1:
        fail(
            f"ResolveCommand's body ({engine_path.name}:{first_line}-{last_line}) contains "
            f"{len(switches)} 'switch(' statements, expected exactly 1. A second switch "
            "inside it would inflate the resolvable set with case labels that are not "
            "command tags. Refusing to report a resolve rate from a body I cannot vouch for."
        )
    # Check 1b: it is the RIGHT switch, and the body really is the whole dispatcher.
    # Kept from the hard-coded-window version: locating by signature makes drift onto a
    # neighbouring method impossible, but these still catch a dispatcher that has been
    # restructured into a shape this checker no longer measures correctly.
    if "switch (tag)" not in window and "switch(tag)" not in window:
        fail(
            f"the single switch in ResolveCommand ({engine_path.name}:{first_line}-{last_line}) "
            "is not 'switch (tag)'. The dispatcher has been restructured; re-read it before "
            "trusting any resolve rate."
        )
    if "default: return null;" not in window:
        fail(
            f"ResolveCommand's body ({engine_path.name}:{first_line}-{last_line}) has no "
            "'default: return null;'. Either the method has been restructured, or the brace "
            "match ended early -- either way the case labels below may be incomplete."
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
    print(f"ResolveCommand case labels: {len(labels)}\n")
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
