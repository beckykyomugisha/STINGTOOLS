# Drawing types: review-and-fix worklog

The standing loop for drawing production: research, log, fix, build, test, merge, repeat.
It covers drawing-type data, templates, producers, scope boxes, match lines, QA tools,
presets, binding files and docs. This file is the handover: a fresh session continues from
**Resume here** alone.

## Resume here

1. Merge the running fix branches one at a time into `fix/drawing-review`, re-running build,
   tests and `tools/run_ci_gates.py --quick` after each: `fix/dt-qa` (DTW-2..19),
   `fix/dt-producers` (DTW-20..31, 40..54), `fix/dt-ui` (DTW-32..38), `fix/dt-bindings`
   (DTW-55..59).
2. After `fix/dt-qa` merges, fix the queued JSON items DTW-60..62 and 65..72 on
   `STING_DRAWING_TYPES.json` (re-stamp checksums). After `fix/dt-producers` merges, fix
   DTW-63. Then update guide §C7 with the preset params from `fix/review-headless`
   (params table in its commit 20f2782c6) once `fix/dt-ui` has merged, since it edits the
   guide.
3. Open the PR, merge it, and redeploy. The live plugin is `C:\Dev\STING_KUT_LIVE`, owned by
   the ACC session: merge `main` into `claude/kut-combined-acc-tags`, or ask that session to.

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
| DTW-1 | Scope-box planner | Medium | Offers Plan/RCP types only; coordination and section types have no route through area boxes | Coordination is already an area candidate (D-7). Section types follow the box as DTW-52 | In progress |
| DTW-2 | ManagedTemplateSyncer.cs:319 | High | Cached managed template returned without a checksum compare; Reloads never invalidate, so pack edits never reach STING:* templates | Agent `fix/dt-qa` | In progress |
| DTW-3 | BatchProduceCommands.cs:901 | High | Regenerate templates uses raw packs (no extends merge), losing inherited overrides/filters | Agent `fix/dt-qa` | In progress |
| DTW-4 | DrawingSyncStylesCommand.cs:94 | High | Sheet drift reported but never healed (no ViewSheet branch) | Agent `fix/dt-qa` | In progress |
| DTW-5 | STING_DRAWING_TYPES.json titleBlockParams + TitleBlockParamApplier.cs:139 | High | "Sheet Number" in six types rewrites the Revit sheet number, bypassing SheetNumbering / ISO / locks | Agent `fix/dt-qa` | In progress |
| DTW-6 | TitleBlockParamApplier.cs:113 | High | Lock check ignores a type-level title-block lock | Agent `fix/dt-qa` | In progress |
| DTW-7 | DrawingRenumberCommand.cs:63 | High | Profile-policy renumber destroys ISO identifiers | Agent `fix/dt-qa` | In progress |
| DTW-8 | DrawingTypePresentation.cs:74 / DrawingTypeRegistry.cs:233 | Med-High | Negative template and pack caches never cleared (InvalidateResolvedCache has no callers) | Agent `fix/dt-qa` | In progress |
| DTW-9 | ManagedTemplateCommands.cs:95 | Med | ConvertToManaged renames before a save that can silently fail; duplicate filters | Agent `fix/dt-qa` | In progress |
| DTW-10 | MergeRecoveryStubs.cs:492 | Med | Filter-id cache never validated; InvalidateCache is an empty stub | Agent `fix/dt-qa` | In progress |
| DTW-11 | SheetNumberFromIsoCommand.cs:187 | Med | Own two-pass rename can leave ~STINGTMP~ numbers; ignores locks; blocks presets | Agent `fix/dt-qa` | In progress |
| DTW-12 | DrawingTypesInspectCommand.cs:239 | Med | Title-block readiness compares logical names against loaded families, so always shows ✗ | Agent `fix/dt-qa` | In progress |
| DTW-13 | DrawingTypesInspectCommand.cs:303 | Low | TB params line mislabelled and counted per sheet | Agent `fix/dt-qa` | In progress |
| DTW-14 | DrawingSyncStylesCommand.cs:44 | Med | Re-applies suppressed-only reports; raw dialogs block presets | Agent `fix/dt-qa` | In progress |
| DTW-15 | TitleBlockParamApplier.cs:163 | Low-Med | Culture-sensitive parse and raw Set on Double; project-info doubles printed in feet | Agent `fix/dt-qa` | In progress |
| DTW-16 | DrawingHealTitleBlocksCommand.cs:88 | Low | Counts unchanged writes; unknown-type sheets dropped silently | Agent `fix/dt-qa` | In progress |
| DTW-17 | DrawingHealTitleBlocksCommand.cs:110 | Low | Wrong-family check ignores variant rules | Agent `fix/dt-qa` | In progress |
| DTW-18 | STING_DRAWING_TYPES.json titleBlockParams | Low | Hard-coded P01 / S2 / WIP reset real revision/suitability where a family carries them | Agent `fix/dt-qa` | In progress |
| DTW-19 | SheetNumberEngine.cs:311 | Low | O(n³) move lookup | Agent `fix/dt-qa` | In progress |
| DTW-20 | DrawingProductionConfigDialog.cs:616 / DrawingProducer.cs:624 | High | VG edits saved under "*" but the producer reads only dt.Id | Agent `fix/dt-producers` | In progress |
| DTW-21 | DrawingProductionConfigDialog.cs:308,311 | High | Scale / detail-level override combos read by nothing | Agent `fix/dt-producers` | In progress |
| DTW-22 | DrawingProductionConfigDialog.cs:184 | High | Preset combo has no handler; Save Preset always appends a new unnamed preset | Agent `fix/dt-producers` | In progress |
| DTW-23 | BatchProduceCommands.cs:682 | High | Produce Sections default (Manual) and Per room produce nothing, silently | Agent `fix/dt-producers` | In progress |
| DTW-24 | BatchProduceCommands.cs:692 | High | Produce Sections ignores ticked grids | Agent `fix/dt-producers` | In progress |
| DTW-25 | DrawingProductionConfigDialog.cs:438 | Med | Section direction/angle/spacing/segmented/show/output options unread | Agent `fix/dt-producers` | In progress |
| DTW-26 | BatchProduceCommands.cs:446 | High | From Scope Boxes produces unticked drawing types | Agent `fix/dt-producers` | In progress |
| DTW-27 | BatchProduceCommands.cs:815 | High | Exterior Elevations: no sheets, 1+4 option ignored, not idempotent (4 new markers each run) | Agent `fix/dt-producers` | In progress |
| DTW-28 | DrawingProductionConfigDialog.cs:366 | Med | Annotation sub-checkboxes unread | Agent `fix/dt-producers` | In progress |
| DTW-29 | DrawingProductionConfigDialog.cs:298,315 | Med | Hide-unwanted / skip-empty-levels / create-package options unread | Agent `fix/dt-producers` | In progress |
| DTW-30 | DrawingProductionConfigDialog.cs:284 | Med | All/Selected levels radios unread | Agent `fix/dt-producers` | In progress |
| DTW-31 | ScopeBoxCommands.cs:220 | Med | Area-box production has no interactive Dependent option | Agent `fix/dt-producers` | In progress |
| DTW-32 | StingDockPanel.xaml tooltips 1446/1455/1477/1490/1491/2209 | Med | Tooltips promise behaviour the code lacks (planner, Sync Styles, pre-flight, counts) | Merged 796a5a041 | Done |
| DTW-33 | StingDockPanel.xaml:1483/1493, DrawingTypeEditorDialog.cs:467 | Med | Three confusable scope-box producers | Merged 796a5a041 | Done |
| DTW-34 | ProjectSetupCommand.cs:538 | Med | Wizard dependents/sections/elevations use legacy unstamped commands | Merged 796a5a041 | Done |
| DTW-35 | StingHvacPanel.xaml:503 | Low | HVAC Produce views makes unstamped views | Merged 796a5a041 | Done |
| DTW-36 | DrawingTypeEditorDialog.cs:1875,1887 | Low | Duplicate swap / variant buttons | Merged 796a5a041 | Done |
| DTW-37 | StingCommandHandler.cs:6587, StingDockPanel.xaml:1452/2206 | Low | Mislabelled result title; one command under two labels | Merged 796a5a041 | Done |
| DTW-38 | MEP_DRAWING_PRODUCTION_GUIDE.md A1/A6/A7 | Low-Med | Guide places Produce From Areas, Dependent and Rename scope boxes wrongly | Merged 796a5a041 | Done |
| DTW-39 | ScopeBoxPlannerService.cs:355 | High | No saved plan: NullReferenceException in area production (headless preset fails) | Fixed f91f8c42c | Done |
| DTW-40 | BatchProduceCommands.cs:514 / ScopeBoxBinder | High | STING:: level segment matched to Level.Name only; "Level 1" unaddressable | fix/dt-producers | In progress |
| DTW-41 | GenerateFromScopeBoxesCommand.cs | High | Legacy duplicate producer: blocks presets, duplicate views, L1 matches L10 | fix/dt-producers | In progress |
| DTW-42 | DrawingProducer.cs:1422 | High | Identity keyed on level/box names; a rename re-mints views, sheets and numbers | fix/dt-producers | In progress |
| DTW-43 | SheetNumberPolicy / DrawingTokenContext | Med | ISO LVL from SafeShort(name) disagrees with IsoLevelCode | fix/dt-producers | In progress |
| DTW-44 | SheetNumberPolicy.cs:77 | Med | ISO pattern embeds frozen S2-P01 | fix/dt-producers | In progress |
| DTW-45 | DrawingProducer.cs:1745 | Med | Batch caches not rolled back; false -A duplicates | fix/dt-producers | In progress |
| DTW-46 | MatchLineEngine.cs:555 | Med | Generate keeps stale lines after a box moves | fix/dt-producers | In progress |
| DTW-47 | MatchLineEngine.cs:1111 | Med | Orphan curves of deleted or retyped views never pruned | fix/dt-producers | In progress |
| DTW-48 | MatchLineEngine.cs:756 | Low-Med | Unbound stamp param makes every run add curves, silently | fix/dt-producers | In progress |
| DTW-49 | MepLevelViewProducer | Med | Host-only presence; linked MEP reads as nothing modelled | fix/dt-producers | In progress |
| DTW-50 | SheetNumbering.cs:139 | Low-Med | Commit status ignored | fix/dt-producers | In progress |
| DTW-51 | DrawingProducer CreateSheet | Low | Area sheets on one level share a name | fix/dt-producers | In progress |
| DTW-52 | DrawingProducer.cs:725/891 | Low | Section in box context is a fixed 10 m cut at the origin; planner gap 9 | fix/dt-producers | In progress |
| DTW-53 | DrawingProducer.cs:1547 | Low | Unique-name cap at 99; dead lock check | fix/dt-producers | In progress |
| DTW-54 | DrawingProducer.cs:743 | Low-Med | Interior elevations: one face, owner plan from any level | fix/dt-producers | In progress |
| DTW-55 | RESOLVED_BINDINGS (STING_DRAWING_TYPE_ID_TXT + 11 view stamps) | High | <ALL> binds to the core set (no Views): every stamp on a view is a no-op, so re-runs cannot find their views | fix/dt-bindings | In progress |
| DTW-56 | STING_MATCH_* params | High | Not bound to Lines: match-line pair keys never stored, re-runs duplicate curves | fix/dt-bindings | In progress |
| DTW-57 | TAG_SEG_MASK_TXT | High | No binding row: token-profile segment masks of 18 types do nothing | fix/dt-bindings | In progress |
| DTW-58 | STING_AEC_FILTERS.json healthcare (46) | High | Rule params not bound to the filters' categories, so filter creation fails | fix/dt-bindings | In progress |
| DTW-59 | STING_DEFAULT_TAG_STYLE_TXT | Med | Written to templates but unregistered | fix/dt-bindings | In progress |
| DTW-60 | titleBlockParams keys (114 types) | High | Keys are display labels no title-block family has; ~10-13 warnings per sheet, nothing written | After fix/dt-qa merges (same JSON) | Queued |
| DTW-61 | mep-coord-A1-1to50 | Med-High | 3 production rules, 1 slot: ISO and section stacked on the plan | After fix/dt-qa | Queued |
| DTW-62 | spool / mep-coord / pres-3d / clar-markup | Med | One view template for mixed view kinds, so it throws and falls back | After fix/dt-qa | Queued |
| DTW-63 | DrawingProducer.cs:1277 SLOT-3 | Med | Raw string view-type compare: spurious mismatch warning on most sheets | After fix/dt-producers (same file) | Queued |
| DTW-64 | STING_MATCH_LINES.json caption text type / line style | Med | Created by nothing: captions silently skipped | TemplateManager style defs | Queued |
| DTW-65 | sectionMarker on 14 types | Med | Families created by nothing; markPrefix/bubble/farClip read by nothing | Decide: ship/author, or drop the dead fields | Queued |
| DTW-66 | legend-A3 | Low-Med | Routed type can never be produced | Document place-existing, or add rules | Queued |
| DTW-67 | DocAutomationExtCommands.cs:1763,1882 | Low-Med | Resolve("*",…,Section/Elevation) matches no rule | Add * * SECTION / ELEVATION rules or pass a discipline | Queued |
| DTW-68 | Pack viewTemplate / textStyleName | Low | Names created and read by nothing; dead effectiveTemplateName | Align or delete | Queued |
| DTW-69 | Routing semantics | Low | S DETAIL → rebar detail; P PLAN → drainage; E/P SECTION → M types | Review, dedicated types | Queued |
| DTW-70 | Sheet-number codes | Low | SCH / PR / EL mean two things; possible profile collisions | Normalise | Queued |
| DTW-71 | Id convention | Low | Ids missing paper/scale suffix; inconsistent prefixes | Aliases if renamed | Queued |
| DTW-72 | {mark} in per-level type names | Low | Prints XX | Use {lvl} | Queued |
| DTW-73 | CLAUDE.md catalogue counts | Low | Says 93 types / 113 rules; data has 114 / 141 | Update | Queued |
| DTW-74 | ProjectSetupCommand.CreateTwoSectionsPerScopeBox | Med | Wizard "two building sections per scope box" still makes unstamped sections | After fix/dt-producers (section-from-box helper, DTW-52) | Queued |

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

- **Legacy scope-box producer (DTW-41).** Delegate `DrawingTypes_FromScopeBoxes` to the
  ProduceFromScopeBoxes engine instead of deleting the tag, so existing workflows keep working
  (backward-compatible). The duplicate DOCS button goes.
- **View/sheet identity (DTW-42).** Key it on Level.Id and scope-box UniqueId, falling back to
  the old name-keyed tag and re-stamping it. A rename then stops minting duplicates, and existing
  projects migrate on their next run.
- **ISO number (DTW-44).** Suitability and revision leave the container id (ISO 19650 keeps them
  as metadata). Only new sheets use the new pattern; existing numbers are not rewritten.

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
