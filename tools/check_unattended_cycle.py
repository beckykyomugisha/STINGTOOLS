#!/usr/bin/env python3
"""Nothing in the KUT workflows may block on a person without a reason on record.

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

WHAT IS COUNTED -- every construct that makes a run WAIT on a person:

    StingListPicker.Show   a modal list the run waits on
    OpenFileDialog         a modal file chooser
    AddCommandLink         a TaskDialog offering choices, whose answer is then read
    .ShowDialog()          any modal WPF window
    TaskDialogResult       the answer of a dialog being compared, i.e. a branch on a click
    TaskDialog.Show(       a modal message (A8, 2026-10-01)
    new TaskDialog(        the same window built by hand (A8)

Until 2026-10-01 `TaskDialog.Show(title, text)` was NOT counted, on the argument that it
asks for nothing. That let two ACC commands (the lifecycle-gap push and ACCPublish's
unattended branch) pass this gate while opening modal windows on an unattended project - a
scheduled run waits on an OK button exactly as long as on a Yes/No. It is counted now, and
the ACC commands route messages through AccPullClashesCommand.Report (logged when
unattended). The one exception is the first-line `if (ctx == null) { TaskDialog.Show(...) }`
guard: WorkflowEngine refuses to start a workflow without a document context, so that branch
cannot run inside one.

SCOPE (A8): every StingTools/Data/WORKFLOW_KUT_*.json, not only the coordination cycle. A
class run by several workflows or tags is counted once.

Usage:
    python tools/check_unattended_cycle.py [repo-root]      # default: cwd
Exit 0 = every interactive construct in the KUT workflows is gone or baselined with a reason.
"""
from __future__ import annotations

import importlib.util
import json
import re
import sys
from pathlib import Path

WORKFLOW_GLOB = "WORKFLOW_KUT_*.json"
WORKFLOW_DIR = "StingTools/Data"
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
    # A8 (2026-10-01): a modal message is a wait too. TaskDialog.Show(title, text) asks for
    # nothing, but a scheduled run still sits on it until somebody clicks OK - so it is
    # counted, and the commands route messages through AccPullClashesCommand.Report (which
    # logs when unattended) instead. `new TaskDialog(` is the same window built by hand.
    "TaskDialog.Show": re.compile(r"\bTaskDialog\s*\.\s*Show\s*\("),
    "new TaskDialog": re.compile(r"\bnew\s+TaskDialog\s*\("),
}

NO_DOC_GUARD = re.compile(
    r"\bif\s*\(\s*(?:ctx|doc|uidoc|uiDoc|uiapp|uiApp)\s*==\s*null\s*\)\s*\{?\s*TaskDialog\s*\.\s*Show\s*\(")

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
    counts = {name: len(rx.findall(code)) for name, rx in INTERACTIVE.items() if rx.search(code)}
    # The one TaskDialog.Show that is NOT counted: the first-line "no document" guard,
    # `if (ctx == null) { TaskDialog.Show(...); return ...; }`. WorkflowEngine.ExecutePresetCore
    # refuses to start a workflow without a document context, so inside a workflow that branch
    # cannot run. Matched on the blanked code, so a guard inside a comment is not subtracted.
    guards = len(NO_DOC_GUARD.findall(code))
    if guards and "TaskDialog.Show" in counts:
        counts["TaskDialog.Show"] -= min(guards, counts["TaskDialog.Show"])
        if counts["TaskDialog.Show"] == 0:
            del counts["TaskDialog.Show"]
    return counts, where


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

    # A8: EVERY KUT workflow, not only the coordination cycle. The lifecycle-gap push and
    # ACCPublish's fortnightly-issue step both create or publish into ACC and lived in other
    # workflows, so a cycle-only scan passed while they opened modal windows.
    wf_files = sorted((root / WORKFLOW_DIR).glob(WORKFLOW_GLOB))
    if not wf_files:
        return fail(f"no files matched {WORKFLOW_DIR}/{WORKFLOW_GLOB} -- refusing to report a clean set from nothing.")
    tags: list[str] = []
    for wf_path in wf_files:
        steps = json.loads(wf_path.read_text(encoding="utf-8")).get("steps", [])
        wf_tags = [s.get("commandTag") for s in steps if isinstance(s, dict) and s.get("commandTag")]
        if not wf_tags:
            return fail(f"{wf_path.name} declares no commandTags -- refusing to report a clean workflow from nothing.")
        for t in wf_tags:
            if t not in tags:
                tags.append(t)

    mapping = tag_to_class(root, mod)
    # Self-test: the extraction must actually resolve the tags the workflows use. A mapping
    # that silently resolved nothing would report workflows with no dialogs in them at all.
    unresolved = [t for t in tags if t not in mapping]
    if unresolved:
        return fail("these KUT workflow tags do not resolve to a command class: " + ", ".join(unresolved) +
                    ". tools/check_kut_workflow_tags.py should have caught this first.")

    print(f"KUT workflows: {len(wf_files)} file(s), {len(tags)} distinct step tag(s)\n")
    findings: dict[tuple[str, str], int] = {}
    scanned: set[str] = set()
    print(f"{'Step tag':<34} {'Command class':<52} Source (class body)")
    print("-" * 132)
    for tag in tags:
        cls = mapping[tag]
        simple = cls.rsplit(".", 1)[-1]
        src = find_source(root, cls)
        if src is None:
            return fail(f"no source file declares {cls} (tag {tag}) -- cannot vouch for what it does.")
        rel = src.relative_to(root).as_posix()
        key = f"{rel}::{simple}"
        if key in scanned:   # two tags (or two workflows) running one class count it once
            print(f"{tag:<34} {cls:<52} (same class as above)")
            continue
        scanned.add(key)
        counts, where = scan(root, src, simple, mod)
        print(f"{tag:<34} {cls:<52} {rel}:{where}")
        # Keyed by CLASS, not by file: several of these share a god-file with dozens of
        # other commands, and a per-file key would blame these workflows for their dialogs.
        for construct, n in counts.items():
            findings[(key, construct)] = findings.get((key, construct), 0) + n

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

    print("OK: every interactive construct in the KUT workflows is baselined with a reason.")
    print("    Remember what this does NOT prove — see the docstring.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
