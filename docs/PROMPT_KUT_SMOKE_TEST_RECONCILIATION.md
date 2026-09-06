# Implementation Prompt — KUT smoke-test reconciliation: make the checklist generated, gated, and owner-agnostic

> **Audience**: autonomous terminal coding agent on a Windows box with the
> Revit 2025 API and the .NET 8 SDK. You **can** build — `dotnet build` after
> every work item. Do not carry the "committed without build verification"
> caveat; it does not apply to you.
>
> **Repo**: STINGTOOLS — C# Revit 2025/2026/2027 plugin (`net8.0-windows`).
> Read `CLAUDE.md` (root) first: conventions, transaction rules, data-file
> conventions, the `StingPaths` rule, the deploy rule. Log finished work in
> `docs/CHANGELOG.md`; put newly-found gaps in `docs/ROADMAP.md`; only touch
> `CLAUDE.md` where directory/command structure actually changes.
>
> **Autonomy**: you own every decision below. Where this prompt says
> *"Recommended"*, that is a considered default from the review — adopt it
> unless the code tells you it is wrong, and if you deviate, say so in the
> CHANGELOG entry with the evidence that changed your mind. Do not stop to
> ask. Do not stop half-done: if one work item turns out to be blocked,
> finish every other one in full and state plainly what you left and why.

---

## 0. Start here — branch and baseline

```bash
git fetch origin
git checkout -b claude/kut-smoke-test-reconciliation origin/main
```

**Do not work from a stale base.** A prior session reviewed this exact topic
from a worktree sitting 18 commits behind `main` and produced two confidently
wrong findings. Before trusting any measurement, confirm:

```bash
git rev-parse --short HEAD; git rev-parse --short origin/main   # must match
```

Then take your own baseline so you can prove you did not regress anything:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary
pwsh tools/check_workflow_wiring.ps1
pwsh tools/check_path_discipline.ps1
```

Record the three results. The build baseline is **0 errors / 0 warnings** —
if it is not, stop and fix that first, because you cannot attribute a later
failure to your own work otherwise.

---

## 1. Context: what this is and why it is a mess

STING is deployed as the BIM Manager's toolset on the **Kampala Uganda Temple
(KUT)** project (Owner: LDS Church Special Projects; Lead Appointed Party:
Symbion; Information Manager: Planscape — Mayanja Davis). Phase 192 built a
KUT alignment pack: an Owner-standards rule pack, a LOD verification ladder,
CSI/SpecLink and Fohlio round-trips, Bluebeam review-comment tracking, and a
set of workflow presets for the proposal's delivery rhythms.

Phase 192 also produced a **27-step manual Revit smoke-test checklist**,
because none of that had ever been run inside Revit. That checklist now
exists in three states at once:

| Artefact | Where | State |
|---|---|---|
| `docs/examples/KUT/REVIT_SMOKE_TEST.md` | `main` | last touched at Phase 192 wrap-up; **stale in 4 places** |
| `KUT_Revit_Smoke_Test_Checklist.docx` | `origin/claude/session-8tl9ga` **only** | generated from the markdown by hand; unmerged |
| `tools/kut_preflight.py` (779 lines) + `.github/workflows/kut-preflight.yml` | same unmerged branch | asserts the wiring/data contracts behind 11 of the 27 steps |

`origin/claude/session-8tl9ga` is **~100 commits behind `origin/main`** and
predates three merged KUT PRs (#623 LOD ladder, #635 rung-500 consistency,
#638 gate presets). Its branch diff is exactly five files:

```
.github/workflows/kut-preflight.yml            103 +
StingTools/Core/WorkflowEngine.cs               20 +/-      command-tag alias fix
StingTools/Data/CATEGORY_BINDINGS.csv            6 +        LTG_HOIST_* scoping
docs/examples/KUT/KUT_Revit_Smoke_Test_Checklist.docx
project-templates/KUT/README.md                 16 +/-
tools/kut_preflight.py                         779 +
docs/CHANGELOG.md                              102 +
```

**Do not merge or rebase that branch.** Diff each of its five substantive
changes by *content* against current `main` and re-implement what still
applies. (Repo precedent: sibling sub-fixes get reconciled by content, not
cherry-picked — a partial subset is often already in the base.) Specifically:

- `CATEGORY_BINDINGS.csv` — on `main` the three `LTG_HOIST_*` params resolve
  to **Lighting Fixtures only** (`StingTools/Data/RESOLVED_BINDINGS.csv`).
  The branch adds Generic Models as well. Verify which is correct against
  `PARAMETER_REGISTRY.json` (`"binding": "LightingFixtures"`) and the
  sibling `LTG_FIX_*` rows, then apply the right one.
- `WorkflowEngine.cs` — read the alias fix, check whether #623/#630/#638
  already closed it, apply only the delta.
- `kut_preflight.py` — **do not port as-is.** See WI-2; it gets rebuilt on
  top of the new single source.

---

## 2. The organising idea — read this before writing anything

Every defect below is the same defect: **the same fact is written down in
more than one place, and the copies drift.**

- The checklist says a step exists; the code says the button does not.
- One overlay pack has the LOC codes; the other has the Owner rules.
- One Gate Audit preset says writers don't belong in a pre-gate check; the
  other Gate Audit preset, still shipped, contains two writers.
- The docx is a hand-copy of the markdown, which is a hand-copy of the code.

So the sustainable fix is **not** "correct the four stale steps". It is:
**make the checklist a projection of a single machine-readable source, and
gate that source in CI against the code it describes.** Then a step that
names a dead button fails a build instead of wasting a Revit session.

Concretely, the target shape:

```
docs/examples/_smoke_test_schema.md          the contract (what a step may declare)
docs/examples/KUT/smoke_test.json            THE SOURCE — 27+ steps, machine-readable
tools/build_smoke_test.py                    source -> REVIT_SMOKE_TEST.md + .docx
tools/check_smoke_test.py                    source -> validated against the codebase
.github/workflows/smoke-test-gate.yml        runs check + asserts the .md is regenerated
```

and the same shape works for the next owner engagement without a fork —
`docs/examples/<OWNER_CODE>/smoke_test.json`, nothing KUT-specific in the
tooling. That is the "most flexible and sustainable option" this task asks
for; the alternative (hand-edit three files and hope) is what produced the
current state.

**Design constraint that makes it honest**: the checker must resolve a
command tag through the *same three dispatch layers* the existing wiring
gate uses, because a one-layer check over-reports catastrophically (the repo
has been burned by this twice — the "141 silent buttons" figure was ~96%
false-positive). Read the Tier 4 doc comment in
`tools/check_workflow_wiring.ps1` before writing a line of the checker, and
reuse its logic rather than reinventing it:

```
L1  CommandRegistry     StingTools/UI/Modules/*CommandModule.cs  registry.Register("X", ...)
L2  Cmd_Click runners   StingTools/UI/StingDockPanel.xaml.cs     cmdTag == "X"
L3  case labels         the six *CommandHandler.cs files         case "X":
L4  workflow-only       StingTools/Core/WorkflowEngine.cs        case "X": return new ...
```

A step reachable **only** via L4 is legal but must declare itself as such —
that is exactly the `Seeds_Build` trap in WI-4.

---

## 3. Work items

Each item states the defect, the evidence, the recommended fix, and what
"done" means. Build after each. Commit after each, with a message that names
the defect rather than the file.

---

### WI-1 — One KUT overlay pack (blocks WI-2; do it first)

**Defect.** Two divergent KUT overlay packs exist, each missing files the
other has:

| File | `docs/examples/KUT/` | `project-templates/KUT/_BIM_COORD/` |
|---|---|---|
| `project_config.json` (six-building `LOC_CODES`) | yes | **no** |
| `climate_data.json` | yes | **no** |
| `tag_schemes.json` | yes | yes (text differs, behaviour identical) |
| `lod_matrix.json` | **no** | yes |
| `owner_standards.json` | **no** | yes |
| `fohlio_map.json` | **no** | yes |
| `sting_classification.json` | **no** | yes |

Smoke-test step 2 copies **only** from `docs/examples/KUT/`. So
`owner_standards.json`, `lod_matrix.json` and `fohlio_map.json` never reach
`<project>/_BIM_COORD/`, and steps 11, 12 and 16–18 then exercise the
**corporate baseline** while the checklist claims they prove the KUT Owner
profile. In the other direction, the "official" deployment pack lacks
`project_config.json`, which holds the `BLD1..BLD6` LOC codes that the tag
scheme's volume map depends on.

**Recommended fix.** `project-templates/KUT/_BIM_COORD/` becomes the single
deployable pack — it is the one the README's deployment checklist already
names, and it sits next to the project template rather than inside docs.
Move `project_config.json` and `climate_data.json` into it. Reduce
`docs/examples/KUT/` to the smoke-test source + generated outputs + the
`fohlio_connection.json.example` credential stub, and replace the removed
files with a one-line pointer. Add a `_BIM_COORD/manifest.json` listing each
file, what it overlays, and the corporate baseline it merges over — so the
next owner pack is a copy-and-edit, not an archaeology exercise.

**Watch for**: the two `tag_schemes.json` copies differ only in prose. Keep
the `project-templates` wording (it is the fuller of the two and explains the
render-vs-drift contract). Confirm both parse to the same object before
deleting either — `json.load` both and compare, do not eyeball it.

**Done when**: one pack; step 2 of the smoke test copies the whole
`_BIM_COORD/` folder; `docs/examples/KUT/README.md` and
`project-templates/KUT/README.md` agree on one deployment sequence, with one
of them a pointer to the other.

---

### WI-2 — The generated checklist (the core of this task)

**Defect.** Three hand-maintained copies of one list (markdown on `main`,
docx on a dead branch, pre-flight assertions in Python), which have already
drifted apart.

**Recommended fix.** Build the pipeline described in §2.

**`docs/examples/KUT/smoke_test.json`** — one entry per step. A step declares
at minimum:

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
  "artefact": null,
  "dependsOn": [],
  "preclearedOffline": true,
  "notes": ""
}
```

`reach` is the honest field: `button` (a dock/panel button exists),
`workflow` (reachable only through a preset — name it in `notes`), or
`manual` (a Revit-native action, e.g. creating a schedule). Everything the
markdown and the docx render must come from here; nothing may be typed twice.

**`tools/build_smoke_test.py`** — emits `REVIT_SMOKE_TEST.md` and
`KUT_Revit_Smoke_Test_Checklist.docx`. The docx must carry what the markdown
cannot: a tick box and expected outcome per step, a "pre-cleared offline"
marker on the steps the checker already asserts, dependency chains called out
inline, a project/tester header block, a failure log, and a sign-off table.
Use `python-docx` if it is available in this environment; if it is not,
generate OOXML directly rather than adding a dependency the CI runner may not
have — check first, decide, and say which you chose. **Verify the output
opens** — a prior session shipped this docx unrendered because LibreOffice
could not run in its sandbox; you are on Windows, so open it.

**`tools/check_smoke_test.py`** — CI-gradable, no Revit required:

1. every `commandTag` resolves through L1–L4 (reuse the wiring gate's
   parsing; do not write a fourth copy of it);
2. `reach: "button"` steps really have a `<Button Tag="X" Click="Cmd_Click">`
   in the declared panel XAML, and the declared `tab` / `panelSection` /
   `button` label match what the XAML actually says;
3. `reach: "workflow"` steps name a preset in `StingTools/Data/` that
   contains that tag;
4. every `fixture` path exists;
5. every parameter named in an `expected` string exists in
   `PARAMETER_REGISTRY.json` **and** resolves to a category binding (this is
   the check that would have caught the `LTG_HOIST_*` question);
6. every workflow preset named by a step parses and its steps resolve;
7. `dependsOn` ids exist and are lower than the depending step;
8. the committed `.md` is byte-identical to a fresh regeneration.

Port the *substance* of `tools/kut_preflight.py` from the dead branch into
this checker — it already knows several of these assertions — but drive it
from `smoke_test.json` instead of hard-coded step knowledge, and make it
owner-agnostic (glob `docs/examples/*/smoke_test.json`).

**`.github/workflows/smoke-test-gate.yml`** — runs the checker on PRs
touching `docs/examples/**`, `StingTools/UI/**`, `StingTools/Data/**`, or
`StingTools/Core/WorkflowEngine.cs`. Model it on the existing gates
(`stingtools-plugin.yml`, `contract-drift.yml`) for runner/setup shape.

**Done when**: `python tools/check_smoke_test.py` exits 0 on a tree where the
markdown was regenerated, and exits non-zero if you deliberately break a
`commandTag`, a panel section name, or the markdown's freshness. Prove both
directions — a gate you have only seen pass is not a gate.

---

### WI-3 — Correct the four stale steps (encode into `smoke_test.json`)

Verified against `origin/main` @ `ff054c77a`:

**3a — Step 27 is unperformable as written.** It says *"**Build Seeds**
(Symbols) → `STING_SEED_BaptismalFont` builds with DCW/DHW/SAN + recirc
connectors"*. Two errors:
- There is **no Build Seeds button**. `Seeds_Build` appears only at
  `StingTools/Core/WorkflowEngine.cs:1503` — no XAML button, no handler case.
  It is reachable solely inside `WORKFLOW_{PenetrationSweep,
  PenetrationRegister, PlumbingRoughIn, ElectricalRoughIn, SLDProduction}`.
- There is **no `STING_SEED_BaptismalFont.json`**. The font is a *symbol
  inside* `StingTools/Data/Seeds/STING_SEED_PlumbingFixture.json` (added
  Phase 192 E4: DCW + DHW supply, SAN drain, recirc supply/return pair
  carrying the `DomesticHot` systemType).

Encode as `reach: "workflow"` naming a preset, and correct the expected
outcome to the plumbing-fixture symbol. **Then decide**: is a seed-building
command that only a workflow can reach the intended design? Recommended: add
a `Seeds_Build` button next to the other `Symbols_*` buttons
(`StingDockPanel.xaml` ~L1971–1984) and flip the step to `reach: "button"`.
That is a two-line change that removes a whole class of "the checklist lied"
and makes the command discoverable. If you disagree, leave it as `workflow`
and say why.

**3b — Step 25 names a superseded preset.** It runs
`WORKFLOW_GateAudit.json`. See WI-5: that file should not survive. Repoint at
`WORKFLOW_KUT_GateAudit.json` and add steps for the three gate presets added
by #638 (`WORKFLOW_KUT_Deliverable{A,B,C}.json`), which no step currently
exercises.

**3c — The LOD ladder moved and nothing tests the new rungs.** On `main`
`STING_LOD_MATRIX.json` now carries **six** milestones — `construction` @ 400
was inserted and `deliverable-d` moved 400 → **500** — and rungs `100` through
`500` across 34 categories. The checklist only ever picks `"Deliverable B"`.
The newly-changed data is the highest-risk thing in the pack. Add steps that
run `LOD_Verify` at `construction` and at `deliverable-d`, and assert the
report states LOD 400 and LOD 500 respectively.

**3d — Step 3's binding claim.** It asserts the three `LTG_HOIST_*` params
appear after Load Params. Resolve the WI-0 question (Lighting Fixtures only,
or + Generic Models) and make the step assert whichever is true, naming the
categories explicitly. Step 26 (MEP Lighting Schedule columns) is the live
test of that binding — keep them consistent.

---

### WI-4 — Delete the duplicate Gate Audit preset

**Defect.** `StingTools/Data/WORKFLOW_GateAudit.json` ("Gate Audit") and
`StingTools/Data/WORKFLOW_KUT_GateAudit.json` ("KUT Gate Audit") both ship.
The new one's description argues that `ValidateTags`, `CompletenessDashboard`
and `DiscComplianceReport` are **writers** (they build legends inside
transactions) and therefore do not belong in a read-only pre-gate check — and
the old one still contains `ValidateTags` and `CompletenessDashboard`, and is
the one the checklist names.

The new preset's read-only claim was verified during review: all eight of its
steps (`PreTagAudit`, `TokenConfidenceAudit`, `OwnerStandards_Audit`,
`Program_Audit`, `DeviceCoord_Audit`, `LOD_Verify`, `ExportModelHealth`,
`FullComplianceDashboard`) are `[Transaction(TransactionMode.ReadOnly)]`.
**Re-verify this yourself** before relying on it, and add it as an assertion
to the checker (a preset that advertises read-only should be proven
read-only, not trusted) — that assertion generalises to every preset that
makes the claim in its description.

**Recommended fix.** Delete `WORKFLOW_GateAudit.json`. Grep first: `CLAUDE.md`
and the KUT READMEs reference it by name, and `WorkflowEngine` may resolve
"GateAudit" as a built-in preset name — check `GetBuiltInPreset` and the
preset-directory loader (`WorkflowEngine.cs` ~L2472) before deleting, and if
anything resolves it by name, add an alias rather than breaking it silently.

---

### WI-5 — Close the two integration gaps

**5a — `ACC_PullClashes` has no button.** The command exists
(`StingTools/Clash/AccPullClashesCommand.cs`), resolves at
`WorkflowEngine.cs:1876`, and is step 3 of `WORKFLOW_KUT_CoordinationCycle`
— but it cannot be run on its own from any panel. For a fortnightly triage
rhythm that is a real gap. Add a button beside `ClashRun` / `ClashBcfExport`
in the BIM tab's clash section, wire the handler case, and add a smoke-test
step (`optional`, since it needs
`%APPDATA%/Planscape/acc_credentials.json`).

**5b — Tier 4 of the wiring gate only scans `StingDockPanel.xaml`**
(`tools/check_workflow_wiring.ps1:182`). The Electrical, HVAC, Plumbing, LPS
and Sustainability panel buttons are ungated — and two smoke-test steps live
exactly there (21 `Lite_ComCheck` on `StingElectricalPanel.xaml`,
22 `Hvac_LifeCycleCompare` on `StingHvacPanel.xaml`). The handler side
already scans all six handlers; only the XAML side is narrow. Widen it to
every panel XAML + its code-behind, re-run, and **expect new findings** —
if the widened scan reports dead buttons, wire them or delete them; do not
grow `tools/button_wiring_baseline.txt`, which is deliberately empty and
documented as shrink-only.

---

### WI-6 — Flexibility: stop hard-coding one client

**6a — `KutKpiDashboardCommand` is generic logic behind a client-specific
surface.** 471 lines in `StingTools/Commands/Kpi/KutKpiDashboardCommand.cs`,
nothing temple-specific in any of them, but the command tag
(`KUT_KpiDashboard`), the output filenames (`STING_KUT_KPI_*.html/.csv`), the
snapshot log (`kut_kpi_log.jsonl`) and the dialog title all hard-code KUT. A
second owner engagement forks the file.

Recommended: rename to `Owner_KpiDashboard`, derive the code from
`PRJ_ORG_PROJECT_CODE_TXT` (fall back to `STING`), and keep
`KUT_KpiDashboard` as a dispatch alias so the existing button, the
`WORKFLOW_KUT_MonthlyReport` step and any muscle memory keep working. Handle
the existing `kut_kpi_log.jsonl`: read it if present, write to
`<code>_kpi_log.jsonl` going forward, and do not lose the history.

**6b — The KUT `lod_matrix.json` overlay pins two categories for no gain.**
It restates `Lighting Fixtures` and `Plumbing Fixtures` verbatim from
corporate, and the registry **replaces by category**
(`LodVerificationEngine.cs:156`). So every future corporate improvement to
those two categories is silently discarded for KUT, in exchange for nothing.
Recommended: delete both from the overlay (diff them against corporate first
to confirm they are still identical after #623/#635). If they have diverged,
keep them and add a comment saying the pin is deliberate and why.

---

### WI-7 — Logic: an empty LOD scope must not read as a green gate

**Defect.** `LodVerificationEngine.Verify` does
`if (check == null) continue;` **before** `result.Total++`. A category with
no rule and no `*` fallback is dropped from the denominator entirely. Today
corporate ships a `*` rule so it cannot bite — but an overlay supplying
`categoryRules` against a baseline that lost `*` would report **100% pass
over zero elements**, and `OverallPct` returns `100.0` when `Total == 0`.

This is the same failure class the repo already paid for: eleven workflow
presets executed zero steps and reported success (#630). An empty list
standing in for an error is the thing to design against.

**Recommended fix.** Count skipped elements by category into the result,
surface the count in the TaskDialog, the CSV and the JSON gate report, and
make a run whose `Total == 0` report explicitly as *"no elements in scope"*
rather than as a percentage. Cover it with a test in the appropriate existing
test project — this engine is Revit-typed, so check what
`StingTools.Tags.Tests` / `StingTools.Clash.Tests` do with `<Compile Include>`
+ Revit stubs and follow that pattern rather than inventing a new project.

---

### WI-8 — Reconcile the dead branch's remaining substance, then retire it

After WI-2 has absorbed the pre-flight and WI-0 has settled the bindings, the
only things left on `origin/claude/session-8tl9ga` are its CHANGELOG prose and
`project-templates/KUT/README.md` edits. Fold anything still true into your
own CHANGELOG entry and README. Then note in `docs/ROADMAP.md` that the branch
is superseded so nobody re-discovers it in three months and merges a
100-commit-stale checklist.

---

## 4. Verification — what "done" means

Run all of these, and report the actual output, not a claim:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary   # 0 errors, 0 warnings
pwsh tools/check_workflow_wiring.ps1                              # all 4 tiers pass, widened Tier 4
pwsh tools/check_path_discipline.ps1                              # unchanged
python tools/check_smoke_test.py                                  # exits 0
python tools/build_smoke_test.py && git diff --exit-code docs/examples/KUT/  # regeneration is a no-op
dotnet test StingTools.Tags.Tests                                 # plus whichever project WI-7 lands in
```

Plus, by hand:

- **Open the generated `.docx`.** Confirm 27+ numbered step rows in order,
  tick boxes, tables paginate, no `?`/placeholder leakage. A prior session
  shipped it unopened.
- **Break the gate on purpose** — point one step at a nonexistent
  `commandTag`, one at a wrong panel section, and leave the `.md` stale.
  Confirm three distinct failures with useful messages. Revert.

If any step fails, say so with the output. A green claim over a red run is
worse than no claim.

---

## 5. Do NOT

- **Do not merge or rebase `origin/claude/session-8tl9ga`.** Reconcile by
  content (§1).
- **Do not deploy to Revit** as part of this task. `build.bat` stages to this
  checkout only; `deploy.bat` re-points the live add-in manifest and would
  hijack the plugin slot from other sessions. Nothing here needs a live Revit.
- **Do not add to `tools/button_wiring_baseline.txt`.** It is empty and
  documented shrink-only. Wire the button or delete it.
- **Do not build a one-layer command-reachability check.** Four dispatch
  layers; a one-layer check over-reports by ~96%. Reuse the existing parsing.
- **Do not hand-edit `REVIT_SMOKE_TEST.md` or the `.docx`** once WI-2 lands.
  They are outputs. Edit `smoke_test.json`.
- **Do not run the actual Revit smoke test** or claim any of the 27 steps
  passed. Your job is to make the checklist trustworthy and its contracts
  machine-checked. The Revit session is the BIM Manager's, afterwards.
- **Do not widen scope** into the Owner-standards rule engine, the Fohlio REST
  tier, or the ACC read client. Log anything you find there in
  `docs/ROADMAP.md` and move on.

---

## 6. Deliverable

One PR against `main` from `claude/kut-smoke-test-reconciliation`:

- a single KUT overlay pack with a manifest (WI-1);
- `smoke_test.json` + generator + checker + CI gate, owner-agnostic (WI-2);
- a corrected, regenerated `REVIT_SMOKE_TEST.md` and `.docx` (WI-3);
- one Gate Audit preset (WI-4);
- `ACC_PullClashes` button + widened Tier 4 (WI-5);
- `Owner_KpiDashboard` with a `KUT_KpiDashboard` alias, unpinned LOD
  categories (WI-6);
- an LOD run over zero elements that reports "no elements in scope" (WI-7);
- `docs/CHANGELOG.md` entry, `docs/ROADMAP.md` updates, `docs/INDEX.md` rows
  for the new docs.

The PR body should state, in one paragraph each: what a step in
`smoke_test.json` may declare, what the gate proves, and — importantly —
**what it still cannot prove**, which is everything about real Revit
geometry. Wiring is already gated; the value of the Revit session is that it
tests judgement against a real model. Say so, so nobody mistakes a green CI
run for a tested pack.
