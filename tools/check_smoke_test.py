#!/usr/bin/env python3
"""Gate the smoke-test source against the codebase it describes.

    python tools/check_smoke_test.py            # exits 0 or 1
    python tools/check_smoke_test.py --verbose  # also print what passed

Owner-agnostic: globs `docs/examples/*/smoke_test.json`.

WHY THIS EXISTS
A manual checklist is data that names commands, buttons, files and parameters by
string, and nothing checked that those strings resolved. The KUT checklist named
a **Build Seeds** button that did not exist, a `STING_SEED_BaptismalFont.json`
that was never a file, and a `WORKFLOW_GateAudit.json` that had been superseded —
each of which costs a Revit session to discover. It also existed as three
hand-maintained copies (markdown, a Word document on a dead branch, and a Python
pre-flight), which had already drifted apart.

This absorbs the substance of the pre-flight from `claude/session-8tl9ga`
(`tools/kut_preflight.py`, 779 lines) but drives every assertion from
`smoke_test.json` instead of hard-coded step knowledge.

WHAT IT PROVES — and what it cannot
It proves WIRING: the tag resolves, the button is really there with that label
under that tab and section, the fixture is on disk, the parameter is registered
and bound, the preset parses and its steps resolve. It cannot open Revit, so it
proves nothing about geometry, about whether a tag is right, or whether an LOD
verdict is fair. A green run here is not a tested pack.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import smoke_test_lib as L  # noqa: E402

VALID_REACH = {"button", "workflow", "manual"}


class Findings:
    def __init__(self):
        self.errors: list[str] = []
        self.checked = 0

    def fail(self, where: str, msg: str):
        self.errors.append(f"{where}: {msg}")

    def ok(self):
        self.checked += 1


# ── 1-7: per-step assertions ──────────────────────────────────────────────────

def check_source(root: Path, owner: str, path: Path, doc: dict, ctx: dict, f: Findings):
    rel = path.relative_to(root).as_posix()

    if doc.get("owner") != owner:
        f.fail(rel, f'"owner" is {doc.get("owner")!r} but the folder is {owner!r} — the tooling keys off the folder')
    steps = doc.get("steps") or []
    if not steps:
        f.fail(rel, "no steps — an empty checklist is not a passing checklist")
        return

    ids: set[int] = set()
    for s in steps:
        sid = s.get("id")
        where = f"{rel} step {sid}"

        if not isinstance(sid, int) or sid <= 0:
            f.fail(where, f"id must be a positive integer, got {sid!r}")
            continue
        if sid in ids:
            f.fail(where, "duplicate id")
        ids.add(sid)

        for req in ("section", "title", "expected", "reach"):
            if not s.get(req):
                f.fail(where, f'missing required field "{req}"')

        reach = s.get("reach")
        if reach not in VALID_REACH:
            f.fail(where, f"reach {reach!r} is not one of {sorted(VALID_REACH)}")
            continue

        tag = s.get("commandTag")

        # ── 1. the tag resolves through L1-L4 ──
        if reach == "manual":
            if tag:
                f.fail(where, f'reach "manual" must have commandTag null, got {tag!r}')
            else:
                f.ok()
        else:
            if not tag:
                f.fail(where, f'reach "{reach}" needs a commandTag')
            else:
                layers = L.resolve_tag(ctx["layers"], tag)
                if not layers:
                    f.fail(where, f'commandTag "{tag}" resolves through NONE of L1 (CommandRegistry), '
                                  f"L2 (Cmd_Click runners), L3 (handler cases) or L4 (WorkflowEngine)")
                else:
                    f.ok()

        # ── 2. reach: button — the button really exists, where it says ──
        if reach == "button":
            panel = s.get("panel")
            if panel not in L.PANELS:
                f.fail(where, f"panel {panel!r} is not one of {sorted(L.PANELS)}")
            else:
                cands = [b for b in ctx["buttons"][panel] if b.tag == tag]
                if not cands:
                    f.fail(where, f'no <Button Tag="{tag}" Click="Cmd_Click"> in {L.PANELS[panel][0]} — '
                                  f'if it is only reachable from a preset, declare reach "workflow"')
                else:
                    want = (s.get("tab", ""), s.get("panelSection", ""), s.get("button", ""))
                    got = [(b.tab, b.section, b.label) for b in cands]
                    if want in got:
                        f.ok()
                    else:
                        shown = "; ".join(f"tab={t!r} section={sec!r} label={lab!r}" for t, sec, lab in got)
                        f.fail(where, f'declares tab={want[0]!r} section={want[1]!r} button={want[2]!r}, '
                                      f"but {L.PANELS[panel][0]} says {shown}")

        # ── 3. reach: workflow — the preset exists and contains the tag ──
        if reach == "workflow":
            preset = s.get("preset")
            if not preset:
                f.fail(where, 'reach "workflow" must name a preset')
            elif preset not in ctx["presets"]:
                f.fail(where, f"preset {preset} not found in {L.DATA_DIR}/")
            else:
                pj = ctx["presets"][preset]
                if "__parse_error__" in pj:
                    f.fail(where, f"preset {preset} does not parse: {pj['__parse_error__']}")
                else:
                    tags = {st.get("commandTag") for st in (pj.get("steps") or [])}
                    # WorkflowPreset is the launcher, not a step inside the preset.
                    if tag in tags or tag == "WorkflowPreset":
                        f.ok()
                    else:
                        f.fail(where, f'preset {preset} does not contain commandTag "{tag}"')

        # ── 4. fixtures exist ──
        fixture = s.get("fixture")
        if fixture:
            if (root / fixture).exists():
                f.ok()
            else:
                f.fail(where, f"fixture {fixture} does not exist")

        # ── 5. parameters named in `expected` are registered AND bound ──
        for token in L.PARAM_TOKEN_RX.findall(s.get("expected") or ""):
            if token in ctx["ignore_tokens"]:
                continue
            if token not in ctx["registry"]:
                f.fail(where, f'expected outcome names "{token}", which is not in PARAMETER_REGISTRY.json')
            elif token not in ctx["bindings"]:
                f.fail(where, f'expected outcome names "{token}", which is in the registry but has no row in '
                              f"RESOLVED_BINDINGS.csv — SharedParamGuids treats an absent param as intentionally "
                              f"UNBOUND, so the step cannot observe it")
            else:
                f.ok()

        # ── 7. dependsOn ids exist and come earlier ──
        for dep in s.get("dependsOn") or []:
            if not isinstance(dep, int):
                f.fail(where, f"dependsOn entry {dep!r} is not an integer")
            elif dep >= sid:
                f.fail(where, f"dependsOn {dep} is not lower than this step's id {sid}")
            elif dep not in {x.get("id") for x in steps}:
                f.fail(where, f"dependsOn {dep} names no step")
            else:
                f.ok()


# ── 6. every named preset parses and its steps resolve ────────────────────────

def check_named_presets(root: Path, ctx: dict, f: Findings):
    named = set()
    for _owner, _p, doc in ctx["sources"]:
        for s in doc.get("steps") or []:
            if s.get("preset"):
                named.add(s["preset"])
    for name in sorted(named):
        pj = ctx["presets"].get(name)
        if pj is None:
            continue  # already reported per-step
        if "__parse_error__" in pj:
            continue
        for i, st in enumerate(pj.get("steps") or [], 1):
            t = st.get("commandTag")
            if not t:
                f.fail(f"{name} step {i}", 'no commandTag (a step keyed "tag" deserialises to null and is skipped)')
            elif t not in ctx["layers"]["L4"]:
                f.fail(f"{name} step {i}", f'commandTag "{t}" has no case in WorkflowEngine.ResolveCommand')
            else:
                f.ok()


# ── 9. a preset that advertises read-only must be read-only ───────────────────

_TXMODE_RX = re.compile(r"\[Transaction\(\s*TransactionMode\.(\w+)\s*\)\]")


def _command_transaction_modes(root: Path) -> dict[str, str]:
    """class name -> TransactionMode, over every .cs under StingTools/."""
    modes: dict[str, str] = {}
    for p in (root / "StingTools").rglob("*.cs"):
        if "\\obj\\" in str(p) or "/obj/" in p.as_posix():
            continue
        try:
            text = p.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        if "TransactionMode" not in text:
            continue
        pending = None
        for line in text.splitlines():
            m = _TXMODE_RX.search(line)
            if m:
                pending = m.group(1)
                continue
            c = re.search(r"\bclass\s+(\w+)\s*:\s*[^{]*IExternalCommand", line)
            if c and pending:
                modes[c.group(1)] = pending
                pending = None
    return modes


_RESOLVE_RX = re.compile(r'case\s+"([^"]+)"\s*:\s*(?:\r?\n\s*)?return\s+new\s+([\w.]+)\s*\(')


_SENTENCE_SPLIT_RX = re.compile(r"(?<=[.;])\s+")
_READONLY_RX = re.compile(r"read[- ]only", re.I)
_NEGATION_RX = re.compile(r"\b(not|never|isn't|is not|nothing about|despite)\b", re.I)


def claims_read_only(preset: dict) -> bool:
    """A preset claims read-only by declaring `"readOnly": true`. Nothing else.

    The first version of this grepped the description for "read-only", which is
    how a human states the claim — and it was wrong in both directions within
    minutes of being written:

      - it failed WORKFLOW_PlumbingAudit *after* the description was corrected to
        say "NOT read-only, despite the name", i.e. it punished the honest fix;
      - it failed WORKFLOW_KUT_MonthlyReport for the phrase "chains the read-only
        metrics", which describes the metrics, not the workflow.

    Sentence-level negation handling did not rescue it: "for a genuinely
    read-only pre-gate look, use WORKFLOW_KUT_GateAudit" is a positive sentence
    about a different preset. A claim that CI enforces has to be a field, not a
    turn of phrase — otherwise the enforcement makes the prose worse.

    Prose is still surfaced, as an advisory (see `readonly_prose_hints`), so a
    preset that reads as read-only and has not declared it gets noticed by a
    human rather than silently escaping the check.
    """
    return preset.get("readOnly") is True


def readonly_prose_hints(ctx: dict) -> list[str]:
    """Presets whose prose sounds read-only but carry no `readOnly` field.

    Advisory only — printed, never fatal. Turning this into a failure is what
    produced the two false positives documented in `claims_read_only`.
    """
    hints = []
    for name, pj in sorted(ctx["presets"].items()):
        if "__parse_error__" in pj or "readOnly" in pj:
            continue
        for sentence in _SENTENCE_SPLIT_RX.split(pj.get("description") or ""):
            if _READONLY_RX.search(sentence) and not _NEGATION_RX.search(sentence):
                hints.append(name)
                break
    return hints


def check_readonly_claims(root: Path, ctx: dict, f: Findings, verbose: bool):
    """A preset whose description claims read-only should have to keep it.

    Generalised deliberately: it is not a KUT rule. The old WORKFLOW_GateAudit
    preset was replaced precisely because it read as a pre-gate check while
    containing two writers.
    """
    engine = L.read(root, L.WORKFLOW_ENGINE)
    # Fold multi-label cases: `case "A":\ncase "B": return new X();`
    tag_to_class: dict[str, str] = {}
    for m in re.finditer(r'((?:case\s+"[^"]+"\s*:\s*)+)return\s+new\s+([\w.]+)\s*\(', engine):
        cls = m.group(2).rsplit(".", 1)[-1]
        for t in re.findall(r'case\s+"([^"]+)"', m.group(1)):
            tag_to_class[t] = cls

    modes = ctx["tx_modes"]
    for name, pj in sorted(ctx["presets"].items()):
        if "__parse_error__" in pj:
            f.fail(name, f"preset does not parse: {pj['__parse_error__']}")
            continue
        if not claims_read_only(pj):
            continue
        offenders = []
        for st in pj.get("steps") or []:
            t = st.get("commandTag")
            cls = tag_to_class.get(t)
            mode = modes.get(cls) if cls else None
            if mode and mode != "ReadOnly":
                offenders.append(f"{t} -> {cls} [{mode}]")
        if offenders:
            f.fail(name, "description claims read-only but these steps are not: " + ", ".join(offenders))
        else:
            f.ok()
            if verbose:
                print(f"  read-only claim proven: {name}")


# ── 8. the committed markdown is a fresh regeneration ─────────────────────────

def check_markdown_freshness(root: Path, ctx: dict, f: Findings):
    import build_smoke_test as B

    for owner, path, doc in ctx["sources"]:
        md_path = path.parent / "REVIT_SMOKE_TEST.md"
        want = B.render_markdown(doc)
        if not md_path.exists():
            f.fail(md_path.relative_to(root).as_posix(),
                   "missing — run `python tools/build_smoke_test.py`")
            continue
        got = md_path.read_text(encoding="utf-8")
        if got.replace("\r\n", "\n") != want:
            f.fail(md_path.relative_to(root).as_posix(),
                   "is stale — it is not a regeneration of smoke_test.json. "
                   "Run `python tools/build_smoke_test.py`; do not hand-edit the markdown.")
        else:
            f.ok()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo-root", default=None)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    root = Path(args.repo_root).resolve() if args.repo_root else Path(__file__).resolve().parent.parent

    sources = L.load_sources(root)
    if not sources:
        print("Smoke-test gate FAILED — no docs/examples/*/smoke_test.json found.")
        print("If the source was renamed or removed, fix this gate rather than deleting it.")
        return 1

    ctx = {
        "sources": sources,
        "buttons": L.parse_panel_buttons(root),
        "layers": L.dispatch_layers(root),
        "presets": L.workflow_presets(root),
        "registry": L.registry_params(root),
        "bindings": L.bound_params(root),
        "tx_modes": _command_transaction_modes(root),
        # SCREAMING_SNAKE tokens in `expected` that are file names, folders or
        # enum values rather than parameters.
        "ignore_tokens": {"STING_SEED_BaptismalFont", "STING_SEED_PlumbingFixture"},
    }

    if not ctx["layers"]["L4"]:
        print("Smoke-test gate FAILED — no case labels parsed from WorkflowEngine.cs.")
        print("The switch shape changed; fix this gate rather than deleting it.")
        return 1

    f = Findings()
    for owner, path, doc in sources:
        check_source(root, owner, path, doc, ctx, f)
    check_named_presets(root, ctx, f)
    check_readonly_claims(root, ctx, f, args.verbose)
    check_markdown_freshness(root, ctx, f)

    if f.errors:
        print(f"Smoke-test gate FAILED — {len(f.errors)} problem(s):")
        for e in f.errors:
            print(f"  {e}")
        print()
        print("Edit docs/examples/<OWNER>/smoke_test.json — never the generated .md or .docx —")
        print("then run `python tools/build_smoke_test.py`. Schema: docs/examples/_smoke_test_schema.md")
        return 1

    total_steps = sum(len(d.get("steps") or []) for _o, _p, d in sources)
    print("Smoke-test gate OK.")
    print(f"  Owner sources scanned                : {len(sources)} ({', '.join(o for o, _p, _d in sources)})")
    print(f"  Steps scanned                        : {total_steps}")
    print(f"  Assertions passed                    : {f.checked}")
    print(f"  Panel buttons indexed                : {sum(len(v) for v in ctx['buttons'].values())} across {len(ctx['buttons'])} panels")
    print(f"  Dispatch names L1/L2/L3/L4           : "
          f"{len(ctx['layers']['L1'])}/{len(ctx['layers']['L2'])}/{len(ctx['layers']['L3'])}/{len(ctx['layers']['L4'])}")
    print(f"  Workflow presets parsed              : {len(ctx['presets'])}")
    print(f"  Registered params / bound params     : {len(ctx['registry'])}/{len(ctx['bindings'])}")
    print("  Markdown regeneration                : byte-identical")
    hints = readonly_prose_hints(ctx)
    if hints:
        print()
        print(f"  Advisory ({len(hints)}): these presets read as read-only but do not declare "
              f'"readOnly": true, so the claim is not enforced. Declare it (and let CI prove it),')
        print("  or reword. Advisory, not a failure — see claims_read_only() for why this is not fatal:")
        for h in hints:
            print(f"    {h}")
    print()
    print("  This gate proves WIRING only. It cannot open Revit, so it proves nothing")
    print("  about geometry, tag correctness or whether an LOD verdict is fair.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
