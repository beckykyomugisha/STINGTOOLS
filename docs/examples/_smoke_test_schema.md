# Smoke-test source schema

A manual Revit smoke test is a checklist. A checklist that names a button which
does not exist wastes a Revit session, and one maintained as three hand-copies
(markdown, Word, a Python pre-flight) drifts apart within a phase. So the
checklist is **generated from one machine-readable source, and that source is
gated in CI against the code it describes.**

```
docs/examples/<OWNER_CODE>/smoke_test.json      THE SOURCE — edit this
docs/examples/<OWNER_CODE>/REVIT_SMOKE_TEST.md  generated
docs/examples/<OWNER_CODE>/<...>.docx           generated
tools/build_smoke_test.py                       source -> .md + .docx
tools/check_smoke_test.py                       source -> validated vs the codebase
tools/smoke_test_lib.py                         the parsing both share
.github/workflows/smoke-test-gate.yml           runs the checker on PRs
```

Nothing in the tooling is owner-specific: it globs `docs/examples/*/smoke_test.json`,
so a second engagement is a new folder, not a fork.

**Do not hand-edit the `.md` or the `.docx`.** Both are enforced, by different
means, because they can only be checked in different ways:

| Output | How it is proved current | Catches |
|---|---|---|
| `REVIT_SMOKE_TEST.md` | re-rendered in memory and byte-compared | any edit to either the markdown or the source |
| `<OWNER>_Revit_Smoke_Test_Checklist.docx` | two digests stamped into `docProps/core.xml`, read back with `zipfile` | `inputs-sha256` — source or generator changed and the document did not; `parts-sha256` — the document body was edited after generation |

The `.docx` gets stamps rather than a re-render because rendering needs
`python-docx` and the checker is deliberately stdlib-only so it runs on a bare
CI runner. Writing the document needs the library; proving it is current does
not. This matters more than it sounds: the `.docx` is the copy the tester
physically carries into the Revit session, so it is the copy whose staleness
actually costs a session.

A `.docx` that is missing, unstamped or corrupt is reported the same way —
regenerate it. Regeneration is byte-deterministic (fixed epoch in the core
properties, fixed zip entry timestamps), so a rebuild with no content change
produces no diff.

---

## Document shape

```json
{
  "owner": "KUT",
  "title": "KUT — Revit smoke-test checklist",
  "intro": "…markdown paragraph(s) rendered above the steps…",
  "outro": "…markdown paragraph(s) rendered below the steps…",
  "steps": [ { … } ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `owner` | yes | Owner / project code. Must equal the containing folder name. |
| `title` | yes | Heading of both outputs. |
| `intro` / `outro` | no | Markdown prose around the step list. |
| `steps` | yes | The checklist, in order. |

## Step shape

```json
{
  "id": 14,
  "section": "Platform round-trips (Part C)",
  "title": "CSI Assign — fill empty only",
  "commandTag": "CSI_Assign",
  "reach": "button",
  "panel": "STING",
  "tab": "BIM",
  "panelSection": "CSI / SPECLINK",
  "button": "CSI Assign",
  "fixture": null,
  "expected": "CSI_SECTION_TXT / CSI_TITLE_TXT written; unmapped-category list reported",
  "artefact": "STING_CSI_Assign_<date>.csv",
  "dependsOn": [],
  "preclearedOffline": true,
  "optional": false,
  "notes": ""
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Unique positive integer. Steps render in `id` order. |
| `section` | yes | Groups steps under a heading in both outputs. |
| `title` | yes | One line: what the tester does. |
| `commandTag` | when `reach` is `button` or `workflow` | The dispatch name. Must resolve through L1–L4. `null` for `manual`. |
| `reach` | yes | `button` \| `workflow` \| `manual` — see below. |
| `panel` | when `reach: "button"` | One of `STING`, `ELECTRICAL`, `HVAC`, `PLUMBING`, `LPS`, `SUSTAINABILITY`. |
| `tab` / `panelSection` / `button` | when `reach: "button"` | Must match what the panel XAML actually says. |
| `preset` | when `reach: "workflow"` | `WORKFLOW_*.json` file name in `StingTools/Data/`, which must contain `commandTag`. |
| `fixture` | no | Repo-relative path a tester is asked to pick. Must exist. |
| `expected` | yes | The observable outcome. Every `SCREAMING_SNAKE` token in it is checked against the parameter registry and its bindings. |
| `artefact` | no | File the step should produce. Free text — `<date>` placeholders are fine. |
| `dependsOn` | no | Step ids that must run first. Each must exist and be lower than this id. |
| `preclearedOffline` | no | `true` when `check_smoke_test.py` already asserts this step's contract, so a Revit failure here is a real surprise. |
| `optional` | no | `true` when the step needs something a tester may not have (credentials, a linked prototype). |
| `notes` | no | Free text carried into both outputs. |

## `reach` — the honest field

| Value | Means | Checked as |
|---|---|---|
| `button` | A panel button exists | The XAML really has `<Button Tag="X" Click="Cmd_Click">` in the declared panel, and the declared `tab` / `panelSection` / `button` match it |
| `workflow` | Reachable **only** through a workflow preset | The named preset exists, parses, and contains `commandTag`; every step in it resolves |
| `manual` | A Revit-native action with no STING command | `commandTag` must be `null` |

`reach` exists because the old checklist told a tester to press a **Build Seeds**
button that did not exist — `Seeds_Build` was reachable solely from inside five
workflow presets. That step is now `reach: "button"` because the button was
added; had it not been, it would have had to say `workflow` and name the preset.

## What the checker asserts

1. Every `commandTag` resolves through L1–L4 (`CommandRegistry` modules,
   `Cmd_Click` runners, the six handler `case` labels, `WorkflowEngine`).
   **Four layers, not one** — a single-layer check over-reports by ~96%.
2. `reach: "button"` steps have a real button, in the declared panel, under the
   declared tab and section, with the declared label.
3. `reach: "workflow"` steps name a preset that exists and contains the tag.
4. Every `fixture` path exists.
5. Every parameter named in an `expected` string exists in
   `PARAMETER_REGISTRY.json` **and** resolves to a category binding in
   `RESOLVED_BINDINGS.csv`.
6. Every named preset parses and each of its steps resolves.
7. `dependsOn` ids exist and are lower than the depending step.
8. The committed `.md` is byte-identical to a fresh regeneration.
9. Bonus, not tied to any one step: **a workflow preset whose description claims
   it is read-only is proven read-only** — every command it resolves to carries
   `[Transaction(TransactionMode.ReadOnly)]`. A preset that advertises a property
   should have to keep it.

## What the checker CANNOT prove

Everything about real Revit geometry. It proves the checklist's *wiring* — that
each step names a command that exists, a button that is there, a fixture on
disk, a parameter that binds. It cannot open a model, so it cannot tell you
whether the tags are right, whether the LOD verdict is fair, or whether a
mis-placed switch behind a door swing is actually flagged. **A green CI run is
not a tested pack.** The value of the Revit session is that it tests judgement
against a real model; this gate only stops that session being wasted on a
checklist that was wrong before it started.
