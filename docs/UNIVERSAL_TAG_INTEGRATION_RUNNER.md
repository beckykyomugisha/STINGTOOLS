# RUNNER — Universal-Tag badge/gate integration with Drawing Types, Styles & Workflows

**You are an autonomous terminal coding agent working in the STINGTOOLS repo.** Implement the
tasks below end to end, verify with a build, and commit. This is **repo data/code work only** —
do NOT attempt any Revit family authoring (that is done by a human in the Family Editor).

Read this whole file first. Then read the referenced source files before editing them — match
their existing schema exactly; do not invent fields.

---

## 0. Context (what already exists — do not rebuild)

The "universal tag" is one hand-built discipline-agnostic Revit tag label, propagated to all 206
tag families. It carries two **status badges** (left = data-completeness gate, right = QA / sign-off
gate), each a traffic-light of 3 coloured glyphs on the annotation **subcategory `STING_TagStatus`**,
gated by family Yes/No params whose formulas read shared INT params.

Already implemented and committed (verify by grepping, don't redo):
- Shared params in `StingTools/Data/MR_PARAMETERS.txt` (group 17 `STINGTags_ISO19650`):
  - `STING_GATE_DATA_STATUS_INT` — instance INTEGER, **0=red / 1=amber / 2=green** (data gate)
  - `STING_GATE_QA_STATUS_INT` — instance INTEGER, 0/1/2 (QA gate)
  - `STING_GATE_DATA_MSG_TXT` / `STING_GATE_QA_MSG_TXT` — instance TEXT, terse reason (blank when green)
  - `TAG_WARN_VISIBLE_BOOL` — instance master on/off for the badges
- `ComplianceScan.ComputeElementGates` computes the two ints + two messages.
- `Gate_StampStatus` command (`StingTools/Commands/TagStudio/StampGateStatusCommand.cs`) binds +
  stamps all four params on every taggable element.
- These are mirrored in `StingTools/Data/PARAMETER_REGISTRY.json`.

**The gap you are closing:** the badge/gate system is functionally complete but has **zero
integration** with the drawing-production pipeline — the `STING_TagStatus` subcategory and the
`STING_GATE_*` params are referenced in **no** View Style Pack, Drawing Type, filter, or workflow.
So badges aren't auto-hidden on issued drawings, there's no QA view that colours by gate, and gates
go stale because nothing runs `Gate_StampStatus`.

---

## HARD CONSTRAINTS (violating any of these is a failure)

1. **No Revit family authoring / no computer-use.** Repo files only.
2. **Do NOT delete the legacy bespoke-tier machinery** (`FamilyLabelAuthor`, `TagConfigPlanResolver`,
   the v5.0 `STING_TAG_CONFIG_v5_0_*.csv`, the old `qa-*` filters, colour-scheme commands). They have
   live callers and their removal is gated on a Revit "Duct smoke test" that has NOT passed
   (see `docs/ROADMAP.md`). You may ADD and DEPRECATE-IN-DOCS, never DELETE.
3. **Match existing JSON schema exactly.** Read a sample entry in each file first. Validate every
   edited JSON with `python -c "import json;json.load(open(...))"` AND sanity-check that field TYPES
   match what the C# loader (Newtonsoft) expects — a green `json.load` is not proof the loader binds
   it (a string where the loader wants a list is runtime-dead). Grep the loader for the field.
4. **Any workflow step's `commandTag` MUST resolve in `WorkflowEngine.ResolveCommand`** (in
   `StingTools/Core/WorkflowEngine.cs`). Workflow steps use fields `commandTag` + `label` (not
   `command`/`name`). If a tag you add isn't in `ResolveCommand`, add the mapping.
5. **Preserve corporate-baseline vs project-override semantics.** New entries you add to the
   corporate JSONs get `"origin": "corporate"` if that field exists in siblings.
6. **Work on the current git branch. Never commit to `main`.** One logical commit per task. End
   commit messages with the Co-Authored-By line the repo uses.
7. **Verify before claiming done:** `dotnet build StingTools/StingTools.csproj -c Release` must show
   0 errors; run `StingTools.Tags.Tests` if present; note the pre-existing-warning baseline.

---

## TASK 1 — Gate-based AEC filters

File: `StingTools/Data/STING_AEC_FILTERS.json` (root keys: `version, schemaUri, namespace,
description, colorSchemes, filters`). Each filter looks like:
```json
{"id":"qa-untagged","name":"STING - QA: Untagged Elements","categories":[...OST_*...],
 "rule":{"param":"ASS_TAG_1_TXT","kind":"shared","op":"hasNoValue"},
 "override":{"projColor":"#FF0000","cutColor":"#FF0000","surfFgColor":"#FFA0A0",
             "surfFgPattern":"Solid fill","transparency":40},
 "tags":["qa","tag"],"notes":"..."}
```

**First, read `AecFilterDefinition.cs` / `AecFilterFactory.cs` under `StingTools/Core/Drawing/` to
confirm the exact `rule.op` vocabulary** (there is `hasNoValue`; find the equality op name — likely
`equals`/`notEquals`; the `value` field for an integer comparison). Do not guess the op string.

Add **4 filters** (these target tags/elements by the stamped gate ints):
| id | name | rule | override |
|---|---|---|---|
| `qa-gate-data-red` | STING - QA: Data Gate — Red | `STING_GATE_DATA_STATUS_INT` == 0 | red (mirror `qa-untagged`) |
| `qa-gate-data-amber` | STING - QA: Data Gate — Amber | `STING_GATE_DATA_STATUS_INT` == 1 | amber (`#FFC000` fg, `#FFE9B0` surf) |
| `qa-gate-qa-red` | STING - QA: QA Gate — Red | `STING_GATE_QA_STATUS_INT` == 0 | red |
| `qa-gate-qa-amber` | STING - QA: QA Gate — Amber | `STING_GATE_QA_STATUS_INT` == 1 | amber |

Use the **same `categories` list** as `qa-untagged`. `kind` = `shared`. `tags`: `["qa","gate"]`.
Acceptance: JSON valid; `AecFilterRegistry` (grep its loader) reads them; 4 new ids present.

---

## TASK 2 — View Style Packs: make badges view-driven

File: `StingTools/Data/STING_VIEW_STYLE_PACKS.json` (root: `schemaVersion, name, description,
namespace, lastUpdated, stylePacks, routing`). Each pack: `id, name, description, extends, origin,
appearance, filterRules[], vgOverrides{}`.

**Read `ViewStylePack.cs` + `ViewStylePackApplier.cs`** under `StingTools/Core/Drawing/` to learn:
- the exact shape of `vgOverrides` (how a **subcategory visibility** is expressed — you need to turn
  the annotation subcategory `STING_TagStatus` OFF/ON), and
- how `filterRules` reference filter ids (so you can attach the Task-1 filters).

Do two things:
1. **Turn `STING_TagStatus` OFF in every issue/print pack** (packs whose id/name implies issued,
   print, handover, or presentation output). Add a `vgOverrides` subcategory-hidden rule for
   `STING_TagStatus`. This makes the status badges + warning labels never appear on issued drawings.
2. **Add ONE new pack** `id: "coord-qa"`, `name: "STING - Coordination / QA"`, `origin: "corporate"`:
   - `vgOverrides`: `STING_TagStatus` **ON** (visible).
   - `filterRules`: attach the 4 filters from Task 1 (`qa-gate-data-red/amber`, `qa-gate-qa-red/amber`)
     so a coordination view auto-recolours non-compliant tags.
   - Add a `routing` entry if the file's `routing[]` maps purpose/phase → pack, so a
     `COORDINATION`/`QA` view resolves to this pack.

Acceptance: JSON valid; `ViewStylePackRegistry` loads (grep loader); issue packs hide
`STING_TagStatus`; `coord-qa` exists, shows it, and references the 4 filters.

---

## TASK 3 — Refresh gates automatically in QA workflows

Files: `StingTools/Data/WORKFLOW_DailyQA_Enhanced.json`, `StingTools/Data/WORKFLOW_MorningHealthCheck.json`.

**Read one existing workflow JSON to match the step schema** (steps use `commandTag` + `label`;
possibly `minCompliancePct` etc.). Add a `Gate_StampStatus` step to each — place it EARLY (right
after any tag/populate step, before compliance reporting) so downstream steps see fresh gates.

**Verify `"Gate_StampStatus"` is a case in `WorkflowEngine.ResolveCommand`** (grep). If missing, add:
`case "Gate_StampStatus": return new Commands.TagStudio.StampGateStatusCommand();` (match the file's
existing return style/namespaces).

Acceptance: both workflow JSONs valid; each has a `Gate_StampStatus` step with a `label`;
`ResolveCommand` resolves the tag; build green.

---

## TASK 4 — Rationalize the old QA filters (DOCS ONLY — do not delete)

The old completeness filters (`qa-untagged`, `qa-incomplete`, `qa-missing-disc`, `qa-missing-loc`,
`qa-stale`, …) now overlap the **data gate** (which computes the same thing, stamped). Do NOT delete
them (live). Instead add a short section to `docs/ROADMAP.md` under the universal-tag area:
"QA-filter rationalization — once `coord-qa` + gate filters are proven in Revit, deprecate the
overlapping `qa-*` completeness filters in favour of the gate-based ones (single source of truth =
the stamped gate)." List the specific overlapping ids.

---

## TASK 5 — Audit Drawing-Type tag bindings (report + safe repoint only)

File: `StingTools/Data/STING_DRAWING_TYPES.json`. Its annotation rule packs bind `tagFamilies` per
category; today many reference `"STING - Generic Tag"`. Read `DrawingType.cs` / `AnnotationRulePack`
to understand the field. **Report** (in the commit body + a ROADMAP note) whether `STING - Generic
Tag` is the universal master or a placeholder. Only if it is unambiguously safe (the universal tag
is loaded per-category and the names resolve) repoint bindings; **otherwise change nothing** and log
the decision for the human. Do NOT guess-rename tag families.

---

## TASK 6 — Docs consistency

1. `docs/UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` + `docs/UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md`:
   - Rename the visibility params to **UPPERCASE** everywhere: `VIS_DATA_GREEN/AMBER/RED`,
     `VIS_QA_GREEN/AMBER/RED` (family-local Yes/No params — clarify they are NOT shared / not in
     MR_PARAMETERS). Keep the `and(TAG_WARN_VISIBLE_BOOL, STING_GATE_x_STATUS_INT = n)` formulas.
   - Document the two **message labels** beside each badge:
     `if(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_MSG_TXT, "")` (left) /
     `if(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_MSG_TXT, "")` (right), stamped by `Gate_StampStatus`
     (blank when green). List the message vocabulary (data: UNTAGGED / TAG INCOMPLETE / NO STATUS /
     EMPTY CONTAINER / ISO ERRORS; QA: NO QA / NO SIGN-OFF / QA PENDING).
   - Add a "View-driven control" note: badges/warnings are hidden on issued drawings via the
     `STING_TagStatus` subcategory OFF in issue View Style Packs, and shown + colour-coded in the
     new `coord-qa` pack (whole-tag status colour lives at the view level, not in the family).
2. `docs/CHANGELOG.md`: append a `#### Completed` block summarising Tasks 1–3 + 6.
3. `docs/ROADMAP.md`: Tasks 4 + 5 notes; keep the smoke-test survival item (verify the 6 `VIS_*`
   params + `STING_TagStatus` subcategory + glyphs survive recategorise-propagation).

---

## TASK 7 — Build, test, commit

- `dotnet build StingTools/StingTools.csproj -c Release` → 0 errors (record the warning count).
- Run `StingTools.Tags.Tests` if the project builds tests.
- Re-validate every edited JSON with `json.load`.
- Commit per task (or logically grouped): filters; style packs; workflow+ResolveCommand; docs.
  Each message imperative, one logical change, ending with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

## Done criteria (self-check before finishing)
- [ ] 4 gate filters in `STING_AEC_FILTERS.json`, loader-valid, correct `op`.
- [ ] Issue/print style packs hide `STING_TagStatus`; new `coord-qa` pack shows it + attaches the 4 filters.
- [ ] `Gate_StampStatus` step in both QA workflows; tag resolves in `ResolveCommand`.
- [ ] Old `qa-*` filters untouched; deprecation logged in ROADMAP.
- [ ] Drawing-type tag binding audited + reported (changed only if unambiguously safe).
- [ ] Guides use UPPERCASE `VIS_*`; message labels + view-driven control documented; CHANGELOG/ROADMAP updated.
- [ ] Build 0 errors; all edited JSON valid.
- [ ] Nothing from the legacy bespoke-tier path deleted. Committed on a non-main branch.
