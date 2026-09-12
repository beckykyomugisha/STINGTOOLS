#!/usr/bin/env python3
"""Nothing in the KUT coordination cycle may block on a person without a reason on record.

WHY
The fortnightly cycle is meant to run without a human clicking through it. Four dialogs
stood in the way; they were replaced by a policy (V6/AccOperatingPolicy.cs) that decides
whether to prompt. "I removed the dialogs I knew about" is exactly the kind of claim that
needs a check rather than a memory -- a fifth one can arrive in any command in the chain,
and nothing about a modal dialog fails a test: the scheduled run simply stops, silently,
until somebody notices the window.

WHAT THIS IS -- AND, MORE IMPORTANTLY, WHAT IT IS NOT

This is a SOURCE-LEVEL SMOKE CHECK, not a proof. It reads each command's own file and
nothing else, so:
  * it cannot follow a call into a helper -- a prompt one frame down is invisible to it;
  * it cannot tell a GATED prompt from an ungated one. A dialog that AccOperatingPolicy
    correctly bypasses in unattended mode looks identical here to one that always shows;
  * it sees only the command CLASS's own body, so a command whose UI lives elsewhere reads
    as clean when it is not.

Scanning the class body rather than the whole file matters more than it sounds. Three of
these commands live in god-files -- TagOperationCommands.cs holds ~70 commands and 108
uses of TaskDialogResult, almost none of them in the one this cycle runs. A file-wide count
would go red on any edit to a neighbouring command and say nothing at all about the command
in the cycle, and a gate that is noisy for reasons unrelated to what it guards is a gate
that gets switched off.

So it does not, and must not, produce a pass/fail on a count alone. It produces a REVIEWED
INVENTORY: every hit is either removed, or written into the baseline with a one-line reason
somebody had to type. The baseline only shrinks -- a NEW hit fails, and so does a baselined
hit whose code has gone, so an entry cannot linger claiming something about code that no
longer exists. Modelled on tools/param_name_targets_baseline.txt.

A PROMPT IS NOT A MESSAGE. Only constructs that ask for a DECISION are counted:

    StingListPicker.Show   a modal list the run waits on
    OpenFileDialog         a modal file chooser
    AddCommandLink         a TaskDialog offering choices, whose answer is then read
    .ShowDialog()          any modal WPF window
    TaskDialogResult       the answer of a dialog being compared, i.e. a branch on a click

`TaskDialog.Show(title, text)` on its own is deliberately NOT counted. It reports a result
and its return value is discarded; nothing branches on it. Counting it would fill the
baseline with entries that are not gates, and a baseline full of non-gates is one nobody
reads. (In unattended mode those messages are additionally routed to the log rather than
shown -- see AccPullClashesCommand.Report -- but that is a nicety, not what this gate is
about.)

Usage:
    python tools/check_unattended_cycle.py [repo-root]      # default: cwd
Exit 0 = every interactive construct in the cycle is either gone or baselined with a reason.
"""
from __future__ import annotations

import importlib.util
import json
import re
import sys
from pathlib import Path

WORKFLOW = "StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json"
ENGINE = "StingTools/Core/WorkflowEngine.cs"
BASELINE = "tools/unattended_cycle_baseline.txt"
SOURCE_ROOT = "StingTools"

# Constructs that ask for a decision. See the module docstring for what is deliberately
# absent from this list and why.
INTERACTIVE = {
    "StingListPicker.Show": re.compile(r"\bStingListPicker\s*\.\s*Show\b"),
    "OpenFileDialog": re.compile(r"\bOpenFileDialog\b"),
    "AddCommandLink": re.compile(r"\bAddCommandLink\b"),
    "ShowDialog": re.compile(r"\.\s*ShowDialog\s*\("),
    "TaskDialogResult": re.compile(r"\bTaskDialogResult\s*\."),
}

# `case "Tag":` labels may stack before one `return new X();`.
CASE_RE = re.compile(r'^\s*case\s+"([^"]+)"\s*:', re.MULTILINE)
RETURN_RE = re.compile(r"^\s*case\s+\"[^\"]+\"\s*:\s*return\s+new\s+([A-Za-z0-9_.]+)\s*\(")
BARE_RETURN_RE = re.compile(r"^\s*return\s+new\s+([A-Za-z0-9_.]+)\s*\(")


def fail(msg: str) -> int:
    print("FAIL: " + msg)
    return 2


def load_tag_checker(root: Path):
    """Reuse the extraction from check_kut_workflow_tags.py rather than copying it.

    Two copies of "where does ResolveCommand start and end" would drift, and the copy that
    drifted would be the one reporting health.
    """
    path = root / "tools" / "check_kut_workflow_tags.py"
    if not path.is_file():
        raise SystemExit(fail(f"{path} not found -- this tool reuses its ResolveCommand extraction."))
    spec = importlib.util.spec_from_file_location("check_kut_workflow_tags", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    for needed in ("locate_resolve_command", "strip_strings_and_comments"):
        if not hasattr(mod, needed):
            raise SystemExit(fail(f"check_kut_workflow_tags.py no longer exposes {needed}()."))
    return mod


def tag_to_class(root: Path, mod) -> dict[str, str]:
    """Map every command tag in ResolveCommand to the type it constructs."""
    text = (root / ENGINE).read_text(encoding="utf-8", errors="replace")
    lo, hi = mod.locate_resolve_command(text)
    body = text[lo:hi]

    mapping: dict[str, str] = {}
    pending: list[str] = []
    for line in body.splitlines():
        m = RETURN_RE.match(line)
        if m:
            for t in pending:
                mapping[t] = m.group(1)
            pending = []
            case = CASE_RE.match(line)
            if case:
                mapping[case.group(1)] = m.group(1)
            continue
        case = CASE_RE.match(line)
        if case:
            pending.append(case.group(1))
            continue
        bare = BARE_RETURN_RE.match(line)
        if bare and pending:
            for t in pending:
                mapping[t] = bare.group(1)
            pending = []
    return mapping


def find_source(root: Path, type_name: str) -> Path | None:
    """The file declaring a type. `X.Y.ZCommand` is matched on `ZCommand`."""
    simple = type_name.rsplit(".", 1)[-1]
    decl = re.compile(r"\b(class|record|struct)\s+" + re.escape(simple) + r"\b")
    for path in sorted((root / SOURCE_ROOT).rglob("*.cs")):
        parts = set(path.parts)
        if "obj" in parts or "bin" in parts:
            continue
        try:
            if decl.search(path.read_text(encoding="utf-8", errors="replace")):
                return path
        except OSError:
            continue
    return None


def class_body(text: str, simple_name: str, mod) -> tuple[int, int] | None:
    """Character offsets of one class's body, by declaration + brace match.

    String literals and comments are blanked before matching, so a brace inside `$"{x}"`
    cannot end the class early -- a body that ends in the wrong place is a silent
    mis-measurement, which is the thing this whole family of gates exists to prevent.
    """
    blank = mod.strip_strings_and_comments(text)
    decl = re.compile(r"\b(?:class|record|struct)\s+" + re.escape(simple_name) + r"\b")
    m = decl.search(blank)
    if m is None:
        return None
    brace = blank.find("{", m.end())
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(blank)):
        if blank[i] == "{":
            depth += 1
        elif blank[i] == "}":
            depth -= 1
            if depth == 0:
                return brace, i + 1
    return None


def scan(root: Path, path: Path, simple_name: str, mod) -> tuple[dict[str, int], str]:
    """Count interactive constructs inside ONE class body, ignoring comments and strings.

    A construct named inside a comment explaining why it was removed is not a construct;
    counting it would make the baseline grow every time somebody documented a fix.
    """
    text = path.read_text(encoding="utf-8", errors="replace")
    span = class_body(text, simple_name, mod)
    if span is None:
        raise SystemExit(fail(f"could not bound the body of class {simple_name} in {path} -- "
                              f"refusing to report a count from a region I cannot delimit."))
    lo, hi = span
    where = f"{text.count(chr(10), 0, lo) + 1}-{text.count(chr(10), 0, hi) + 1}"
    code = mod.strip_strings_and_comments(text)[lo:hi]
    return {name: len(rx.findall(code)) for name, rx in INTERACTIVE.items() if rx.search(code)}, where


def read_baseline(path: Path) -> dict[tuple[str, str], tuple[int, str]]:
    out: dict[tuple[str, str], tuple[int, str]] = {}
    if not path.is_file():
        return out
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "\t" not in line:
            raise SystemExit(fail(f"baseline line has no TAB before its reason: {raw!r}"))
        key, reason = line.split("\t", 1)
        bits = key.split("|")
        if len(bits) != 3:
            raise SystemExit(fail(f"baseline key must be file::Class|construct|count, found {key!r}"))
        f, construct, count = bits
        if not reason.strip():
            raise SystemExit(fail(f"baseline entry {key!r} has no reason. Every entry needs one."))
        out[(f, construct)] = (int(count), reason.strip())
    return out


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    mod = load_tag_checker(root)

    wf_path = root / WORKFLOW
    if not wf_path.is_file():
        return fail(f"{wf_path} not found")
    steps = json.loads(wf_path.read_text(encoding="utf-8")).get("steps", [])
    tags = [s.get("commandTag") for s in steps if isinstance(s, dict) and s.get("commandTag")]
    if not tags:
        return fail(f"{WORKFLOW} declares no commandTags -- refusing to report a clean cycle from nothing.")

    mapping = tag_to_class(root, mod)
    # Self-test: the extraction must actually resolve the tags this cycle uses. A mapping
    # that silently resolved nothing would report a cycle with no dialogs in it at all.
    unresolved = [t for t in tags if t not in mapping]
    if unresolved:
        return fail("these cycle tags do not resolve to a command class: " + ", ".join(unresolved) +
                    ". tools/check_kut_workflow_tags.py should have caught this first.")

    print(f"KUT coordination cycle: {len(tags)} step(s)\n")
    findings: dict[tuple[str, str], int] = {}
    print(f"{'Step tag':<22} {'Command class':<42} Source (class body)")
    print("-" * 112)
    for tag in tags:
        cls = mapping[tag]
        simple = cls.rsplit(".", 1)[-1]
        src = find_source(root, cls)
        if src is None:
            return fail(f"no source file declares {cls} (tag {tag}) -- cannot vouch for what it does.")
        rel = src.relative_to(root).as_posix()
        counts, where = scan(root, src, simple, mod)
        print(f"{tag:<22} {cls:<42} {rel}:{where}")
        # Keyed by CLASS, not by file: three of these share a god-file with dozens of
        # other commands, and a per-file key would blame this cycle for their dialogs.
        for construct, n in counts.items():
            findings[(f"{rel}::{simple}", construct)] = findings.get((f"{rel}::{simple}", construct), 0) + n

    print()
    baseline = read_baseline(root / BASELINE)
    problems: list[str] = []

    print("Interactive constructs found (a decision is asked for, not merely reported):")
    if not findings:
        print("  none")
    for (f, construct), n in sorted(findings.items()):
        based = baseline.get((f, construct))
        if based is None:
            print(f"  NEW      {f}  {construct} x{n}")
            problems.append(f"NEW interactive construct: {f} {construct} x{n}. Either remove it, or "
                            f"add '{f}|{construct}|{n}<TAB>reason' to {BASELINE} with a one-line reason.")
        elif n > based[0]:
            print(f"  GREW     {f}  {construct} x{n}  (baselined at {based[0]})")
            problems.append(f"{f} {construct} rose from {based[0]} to {n}. The baseline only shrinks.")
        elif n < based[0]:
            print(f"  SHRANK   {f}  {construct} x{n}  (baselined at {based[0]})")
            problems.append(f"{f} {construct} fell from {based[0]} to {n} -- good, now lower the "
                            f"baseline in the same commit so it stops over-claiming.")
        else:
            print(f"  baseline {f}  {construct} x{n}  — {based[1]}")

    # A baselined entry whose code is gone must not linger.
    for (f, construct), (n, reason) in sorted(baseline.items()):
        if (f, construct) not in findings:
            print(f"  STALE    {f}  {construct}  (baselined at {n}, now absent)")
            problems.append(f"baseline entry '{f}|{construct}' no longer matches anything. Delete the "
                            f"line -- a baseline that describes code that is gone claims health it "
                            f"has not checked.")

    print()
    if problems:
        print(f"{len(problems)} problem(s):")
        for p in problems:
            print("  - " + p)
        print("\nFAIL")
        return 1

    print("OK: every interactive construct in the KUT coordination cycle is baselined with a reason.")
    print("    Remember what this does NOT prove — see the docstring.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
