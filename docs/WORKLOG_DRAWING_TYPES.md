# Drawing types: review-and-fix worklog

The standing loop for drawing production: research, log, fix, build, test, merge, repeat.
It covers drawing-type data, templates, producers, scope boxes, match lines, QA tools,
presets, binding files and docs. This file is the handover: a fresh session continues from
**Resume here** alone.

## Resume here

1. Merge `fix/review-headless` (preset-safe placement / calc / BOQ / COBie commands) into
   `fix/drawing-review` when its agent reports. Then rewrite the "still open a dialog"
   block in the guide (§C7, about line 567).
2. Triage the four research reports (data consistency, producers, QA tools, UI) into the
   **Findings** table. Fix the high-severity ones first, on disjoint file sets.
3. Open the PR for `fix/drawing-review` with all gates green, merge it, then redeploy. The live
   plugin is `C:\Dev\STING_KUT_LIVE`, owned by the ACC session on
   `claude/kut-combined-acc-tags`. Merge `main` into that branch, or ask that session to,
   so its work stays live.

## State (2026-10-01)

- Working branch: `fix/drawing-review`, from `origin/main` 72a2cd6ce, in worktree
  `.claude/worktrees/kut-integration-readiness-497572`.
- Merged into it so far:
  - `fix/review-presets`: revision issue on sheet clouds, the `baseline` param, preset-safe
    TagAndCombine, MEP system colouring step, quick-workflow combo, guide C7.
  - `fix/review-perf`: match-line sweep cache, single-pass MEP presence, tag-symbol index,
    placement-rule cache, DWG parallel-pair overlap, DWG seed hooks.
  - `fix/review-plumbing`: drainage and supply schematics draw only what is modelled, the
    system picker, the DXF path, O(1) pressure lookup.
  - `fix/review-electrical`: `SchematicViewFactory` (no orphan views), `BoardNaming`, preset-safe
    panel checks, three missing drawing types, `P-DRN-SCH` prefix.
  - My own commits: PARAMETER_CATEGORIES header regenerated (fixes `binding-spec-drift` on
    main, caused by #1019), DXF export route, SheetNumberPolicy comment.
- Combined branch: build 0/0, Tags.Tests 4,658 passed.

## Known gaps from the brief, checked against the code (2026-10-01)

| # | Gap | Status |
|---|---|---|
| 1 | MEP view templates named by types are created by nothing | Closed: `DrawingTemplateCatalogue` creates every `viewTemplateName` in the JSON (74 names) |
| 2 | Missing MEP drawing types (MDP-2) | Closed: 114 types now, including data/comms, security, containment, CHW/LTHW, schematics, fire riser, sections, details |
| 3 | `E / PLAN` routes to a riser section (MDP-3) | Closed: routes to `elec-power-A1-1to100` |
| 4 | "Duplicate as Dependent" ignored | Closed: `DependentViewPlanner` |
| 5 | `STING_SCOPE_BOX_TAG_TXT` unregistered | Closed: in MR_PARAMETERS.txt |
| 6 | Match lines pair across disciplines; area boxes get none | Closed: `MatchLineGeometry` pairs by type and level, area boxes included |
| 7 | Producers not headless; three MEP presets | Closed: `RunInWorkflow` paths; the three presets exist |
| 8 | Setup Wizard: level names, unstamped duplicates, rename pattern | Closed: `LevelNameAdvice`, `ScopeBoxRenamePattern`, wizard goes through `DrawingProducer` |
| 9 | Planner offers only Plan/RCP; coordination types need a route | **Open**: see Findings |
| 10 | ZONE from STING-ZONE:: boxes | Closed: `ScopeBoxNames`, `ParameterHelpers` |

## Findings

| Id | Where | Sev | Finding | Plan | Status |
|---|---|---|---|---|---|
| DTW-1 | Scope-box planner | Medium | Offers Plan/RCP types only; coordination and section types have no route through area boxes | Design in progress (research report) | Open |

## Decisions

- **Worklog location.** Put in `docs/` and indexed, not the repo root: every doc under `docs/`
  must be indexed (`every-doc-is-indexed` gate), and the root is already crowded.
- **DXF route.** DXF goes to Models, like DWG, in all three layouts. It is backward-compatible
  (it only adds a key) and consistent with the existing DWG route.
- **Door-diagram sheet key** (electrical agent): keyed by element id, with a legacy sheet keyed
  by panel name adopted and re-tagged. A board rename then no longer mints a duplicate sheet,
  and existing projects migrate without manual work.
- **AutoRevisionCloud baseline** (presets agent): a step param (`latest` default, `previous` in
  the RevisionIssue preset), not a reordering. Standalone use stays unchanged.

## NEEDS REVIT CHECK

Steps to run in a real model. None of these can be tested headlessly.

1. **Schematic re-run.** Run Fire Alarm Schematic twice. Expect one view "STING - Fire Alarm
   Schematic", still on its sheet, with no "Drafting N" view.
2. **Drainage schematic.** In a model with a sanitary stack and a vent, run Drainage Schematic.
   Expect the stack at its real levels (level names), the vent drawn only where a vent pipe
   connects, and no invented branches. In a model with no stack, expect no view and a message
   saying why.
3. **Supply schematic.** On a model with a water meter, expect the source to be the meter.
   Expect no kPa labels unless `plumbing_system_config.json` exists. Expect glyphs about
   3 mm on paper.
4. **Revision issue on sheet clouds.** Draw a cloud directly on a sheet, run IssueSheets, and
   expect only that sheet to be issued.
5. **MEP coordination step.** Run MEPDrawingProduction and expect produced MEP plans coloured
   by system. Where a view template controls filters, expect the template coloured once.
6. **Match-line sweep.** On a 2x2 area-box grid, run MatchLine_Generate twice. Expect no
   duplicate lines or captions, and sheet refs on the captions.
7. **DWG picker.** Pick a wall's two faces where one is split by a door. Expect a wall spanning
   only the overlap, with the measured thickness type.
8. **DWG to seed fixtures.** Place medical-gas seeds from a DWG. Expect the gas and product
   code stamped.
9. **Panel door diagram.** Rename a board, re-run, and expect the same sheet (no duplicate).
10. **Photometric binding (MDP-6).** Load a seed luminaire and read `ELC_PHOTO_*` from an
    instance.
