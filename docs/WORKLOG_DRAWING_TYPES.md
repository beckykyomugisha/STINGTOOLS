# Drawing types: review-and-fix worklog

The standing loop for drawing production: research, log, fix, build, test, merge, repeat.
It covers drawing-type data, templates, producers, scope boxes, match lines, QA tools,
presets, binding files and docs. This file is the handover: a fresh session continues from
**Resume here** alone.

## Resume here

1. Merge `fix/dt-review4` (DTW-103, 105..108, 110) into `fix/drawing-review` when its agent
   reports. Then re-run build, tests, `tools/run_ci_gates.py --quick` and the checksum check,
   update this file and ROADMAP, and push. PR #1021 is a draft.
2. Next research pass, round 5. Not yet reviewed in depth: placement (`Core/Placement/**`), the
   plumbing schematic code merged this loop, `MergeRecoveryStubs` filter indexes,
   `PresetStepInputs`, and linked-level handling in `DrawingProducer`. Then edge cases:
   multi-building, re-runs after model edits, and both sheet-number policies end to end.
3. When nothing high or medium is open: mark PR #1021 ready, merge it once CI is green, and
   redeploy. The live plugin is `C:\Dev\STING_KUT_LIVE`, owned by the ACC session on
   `claude/kut-combined-acc-tags`: merge `main` into that branch, or ask that session to, and
   run its `deploy.bat`. Never deploy a build without its work.

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
| 9 | Planner offers only Plan/RCP; coordination types need a route | Closed: coordination is an area candidate; section types cut along the box, 3D types are boxed (DTW-52) |
| 10 | ZONE from STING-ZONE:: boxes | Closed: `ScopeBoxNames`, `ParameterHelpers` |

## Findings

| Id | Where | Sev | Finding | Plan | Status |
|---|---|---|---|---|---|
| DTW-1 | Scope-box planner | Medium | Offers Plan/RCP types only; coordination and section types have no route through area boxes | Coordination types are area candidates (D-7); section/3D types follow the box (DTW-52, merged) | Done |
| DTW-2 | ManagedTemplateSyncer.cs:319 | High | Cached managed template returned without a checksum compare; Reloads never invalidate, so pack edits never reach STING:* templates | Merged 2a8a1845b | Done |
| DTW-3 | BatchProduceCommands.cs:901 | High | Regenerate templates uses raw packs (no extends merge), losing inherited overrides/filters | Merged 2a8a1845b | Done |
| DTW-4 | DrawingSyncStylesCommand.cs:94 | High | Sheet drift reported but never healed (no ViewSheet branch) | Merged 2a8a1845b | Done |
| DTW-5 | STING_DRAWING_TYPES.json titleBlockParams + TitleBlockParamApplier.cs:139 | High | "Sheet Number" in six types rewrites the Revit sheet number, bypassing SheetNumbering / ISO / locks | Merged 2a8a1845b | Done |
| DTW-6 | TitleBlockParamApplier.cs:113 | High | Lock check ignores a type-level title-block lock | Merged 2a8a1845b | Done |
| DTW-7 | DrawingRenumberCommand.cs:63 | High | Profile-policy renumber destroys ISO identifiers | Merged 2a8a1845b | Done |
| DTW-8 | DrawingTypePresentation.cs:74 / DrawingTypeRegistry.cs:233 | Med-High | Negative template and pack caches never cleared (InvalidateResolvedCache has no callers) | Merged 2a8a1845b | Done |
| DTW-9 | ManagedTemplateCommands.cs:95 | Med | ConvertToManaged renames before a save that can silently fail; duplicate filters | Merged 2a8a1845b | Done |
| DTW-10 | MergeRecoveryStubs.cs:492 | Med | Filter-id cache never validated; InvalidateCache is an empty stub | Merged 2a8a1845b | Done |
| DTW-11 | SheetNumberFromIsoCommand.cs:187 | Med | Own two-pass rename can leave ~STINGTMP~ numbers; ignores locks; blocks presets | Merged 2a8a1845b | Done |
| DTW-12 | DrawingTypesInspectCommand.cs:239 | Med | Title-block readiness compares logical names against loaded families, so always shows ✗ | Merged 2a8a1845b | Done |
| DTW-13 | DrawingTypesInspectCommand.cs:303 | Low | TB params line mislabelled and counted per sheet | Merged 2a8a1845b | Done |
| DTW-14 | DrawingSyncStylesCommand.cs:44 | Med | Re-applies suppressed-only reports; raw dialogs block presets | Merged 2a8a1845b | Done |
| DTW-15 | TitleBlockParamApplier.cs:163 | Low-Med | Culture-sensitive parse and raw Set on Double; project-info doubles printed in feet | Merged 2a8a1845b | Done |
| DTW-16 | DrawingHealTitleBlocksCommand.cs:88 | Low | Counts unchanged writes; unknown-type sheets dropped silently | Merged 2a8a1845b | Done |
| DTW-17 | DrawingHealTitleBlocksCommand.cs:110 | Low | Wrong-family check ignores variant rules | Merged 2a8a1845b | Done |
| DTW-18 | STING_DRAWING_TYPES.json titleBlockParams | Low | Hard-coded P01 / S2 / WIP reset real revision/suitability where a family carries them | Merged 2a8a1845b | Done |
| DTW-19 | SheetNumberEngine.cs:311 | Low | O(n³) move lookup | Merged 2a8a1845b | Done |
| DTW-20 | DrawingProductionConfigDialog.cs:616 / DrawingProducer.cs:624 | High | VG edits saved under "*" but the producer reads only dt.Id | Merged (fix/dt-producers) | Done |
| DTW-21 | DrawingProductionConfigDialog.cs:308,311 | High | Scale / detail-level override combos read by nothing | Merged (fix/dt-producers) | Done |
| DTW-22 | DrawingProductionConfigDialog.cs:184 | High | Preset combo has no handler; Save Preset always appends a new unnamed preset | Merged (fix/dt-producers) | Done |
| DTW-23 | BatchProduceCommands.cs:682 | High | Produce Sections default (Manual) and Per room produce nothing, silently | Merged (fix/dt-producers) | Done |
| DTW-24 | BatchProduceCommands.cs:692 | High | Produce Sections ignores ticked grids | Merged (fix/dt-producers) | Done |
| DTW-25 | DrawingProductionConfigDialog.cs:438 | Med | Section direction/angle/spacing/segmented/show/output options unread | Merged (fix/dt-producers) | Done |
| DTW-26 | BatchProduceCommands.cs:446 | High | From Scope Boxes produces unticked drawing types | Merged (fix/dt-producers) | Done |
| DTW-27 | BatchProduceCommands.cs:815 | High | Exterior Elevations: no sheets, 1+4 option ignored, not idempotent (4 new markers each run) | Merged (fix/dt-producers) | Done |
| DTW-28 | DrawingProductionConfigDialog.cs:366 | Med | Annotation sub-checkboxes unread | Merged (fix/dt-producers) | Done |
| DTW-29 | DrawingProductionConfigDialog.cs:298,315 | Med | Hide-unwanted / skip-empty-levels / create-package options unread | Merged (fix/dt-producers) | Done |
| DTW-30 | DrawingProductionConfigDialog.cs:284 | Med | All/Selected levels radios unread | Merged (fix/dt-producers) | Done |
| DTW-31 | ScopeBoxCommands.cs:220 | Med | Area-box production has no interactive Dependent option | Merged (fix/dt-producers) | Done |
| DTW-32 | StingDockPanel.xaml tooltips 1446/1455/1477/1490/1491/2209 | Med | Tooltips promise behaviour the code lacks (planner, Sync Styles, pre-flight, counts) | Merged 796a5a041 | Done |
| DTW-33 | StingDockPanel.xaml:1483/1493, DrawingTypeEditorDialog.cs:467 | Med | Three confusable scope-box producers | Merged 796a5a041 | Done |
| DTW-34 | ProjectSetupCommand.cs:538 | Med | Wizard dependents/sections/elevations use legacy unstamped commands | Merged 796a5a041 | Done |
| DTW-35 | StingHvacPanel.xaml:503 | Low | HVAC Produce views makes unstamped views | Merged 796a5a041 | Done |
| DTW-36 | DrawingTypeEditorDialog.cs:1875,1887 | Low | Duplicate swap / variant buttons | Merged 796a5a041 | Done |
| DTW-37 | StingCommandHandler.cs:6587, StingDockPanel.xaml:1452/2206 | Low | Mislabelled result title; one command under two labels | Merged 796a5a041 | Done |
| DTW-38 | MEP_DRAWING_PRODUCTION_GUIDE.md A1/A6/A7 | Low-Med | Guide places Produce From Areas, Dependent and Rename scope boxes wrongly | Merged 796a5a041 | Done |
| DTW-39 | ScopeBoxPlannerService.cs:355 | High | No saved plan: NullReferenceException in area production (headless preset fails) | Fixed f91f8c42c | Done |
| DTW-40 | BatchProduceCommands.cs:514 / ScopeBoxBinder | High | STING:: level segment matched to Level.Name only; "Level 1" unaddressable | Merged (fix/dt-producers) | Done |
| DTW-41 | GenerateFromScopeBoxesCommand.cs | High | Legacy duplicate producer: blocks presets, duplicate views, L1 matches L10 | Merged (fix/dt-producers) | Done |
| DTW-42 | DrawingProducer.cs:1422 | High | Identity keyed on level/box names; a rename re-mints views, sheets and numbers | Merged (fix/dt-producers) | Done |
| DTW-43 | SheetNumberPolicy / DrawingTokenContext | Med | ISO LVL from SafeShort(name) disagrees with IsoLevelCode | Merged (fix/dt-producers) | Done |
| DTW-44 | SheetNumberPolicy.cs:77 | Med | ISO pattern embeds frozen S2-P01 | Merged (fix/dt-producers) | Done |
| DTW-45 | DrawingProducer.cs:1745 | Med | Batch caches not rolled back; false -A duplicates | Merged (fix/dt-producers) | Done |
| DTW-46 | MatchLineEngine.cs:555 | Med | Generate keeps stale lines after a box moves | Merged (fix/dt-producers) | Done |
| DTW-47 | MatchLineEngine.cs:1111 | Med | Orphan curves of deleted or retyped views never pruned | Merged (fix/dt-producers) | Done |
| DTW-48 | MatchLineEngine.cs:756 | Low-Med | Unbound stamp param makes every run add curves, silently | Merged (fix/dt-producers) | Done |
| DTW-49 | MepLevelViewProducer | Med | Host-only presence; linked MEP reads as nothing modelled | Merged (fix/dt-producers) | Done |
| DTW-50 | SheetNumbering.cs:139 | Low-Med | Commit status ignored | Merged (fix/dt-producers) | Done |
| DTW-51 | DrawingProducer CreateSheet | Low | Area sheets on one level share a name | Merged (fix/dt-producers) | Done |
| DTW-52 | DrawingProducer.cs:725/891 | Low | Section in box context is a fixed 10 m cut at the origin; planner gap 9 | Merged (fix/dt-producers) | Done |
| DTW-53 | DrawingProducer.cs:1547 | Low | Unique-name cap at 99; dead lock check | Merged (fix/dt-producers) | Done |
| DTW-54 | DrawingProducer.cs:743 | Low-Med | Interior elevations: one face, owner plan from any level | Merged (fix/dt-producers) | Done |
| DTW-55 | RESOLVED_BINDINGS (STING_DRAWING_TYPE_ID_TXT + 11 view stamps) | High | <ALL> binds to the core set (no Views): every stamp on a view is a no-op, so re-runs cannot find their views | Merged ea43a06ab | Done |
| DTW-56 | STING_MATCH_* params | High | Not bound to Lines: match-line pair keys never stored, re-runs duplicate curves | Merged ea43a06ab | Done |
| DTW-57 | TAG_SEG_MASK_TXT | High | No binding row: token-profile segment masks of 18 types do nothing | Merged ea43a06ab | Done |
| DTW-58 | STING_AEC_FILTERS.json healthcare (46) | High | Rule params not bound to the filters' categories, so filter creation fails | Merged ea43a06ab | Done |
| DTW-59 | STING_DEFAULT_TAG_STYLE_TXT | Med | Written to templates but unregistered | Merged ea43a06ab | Done |
| DTW-60 | titleBlockParams keys (114 types) | High | Keys are display labels no title-block family has; ~10-13 warnings per sheet, nothing written | Merged | Done |
| DTW-61 | mep-coord-A1-1to50 | Med-High | 3 production rules, 1 slot: ISO and section stacked on the plan | Merged | Done |
| DTW-62 | spool / mep-coord / pres-3d / clar-markup | Med | One view template for mixed view kinds, so it throws and falls back | Merged | Done |
| DTW-63 | DrawingProducer.cs:1277 SLOT-3 | Med | Raw string view-type compare: spurious mismatch warning on most sheets | Merged | Done |
| DTW-64 | STING_MATCH_LINES.json caption text type / line style | Med | Created by nothing: captions silently skipped | Merged | Done |
| DTW-65 | sectionMarker on 14 types | Med | Families created by nothing; markPrefix/bubble/farClip read by nothing | Merged | Done |
| DTW-66 | legend-A3 | Low-Med | Routed type can never be produced | Merged | Done |
| DTW-67 | DocAutomationExtCommands.cs:1763,1882 | Low-Med | Resolve("*",…,Section/Elevation) matches no rule | Merged | Done |
| DTW-68 | Pack viewTemplate / textStyleName | Low | Names created and read by nothing; dead effectiveTemplateName | Merged | Done |
| DTW-69 | Routing semantics | Low | S DETAIL → rebar detail; P PLAN → drainage; E/P SECTION → M types | Merged | Done |
| DTW-70 | Sheet-number codes | Low | SCH / PR / EL mean two things; possible profile collisions | Merged | Done |
| DTW-71 | Id convention | Low | Ids missing paper/scale suffix; inconsistent prefixes | Merged | Done |
| DTW-72 | {mark} in per-level type names | Low | Prints XX | Merged | Done |
| DTW-73 | CLAUDE.md catalogue counts | Low | Says 93 types / 113 rules; data has 114 / 141 | Merged | Done |
| DTW-74 | ProjectSetupCommand.CreateTwoSectionsPerScopeBox | Med | Wizard "two building sections per scope box" still makes unstamped sections | Merged | Done |
| DTW-75 | STING_AUTO_PLACED_BOOL | Med | Written to viewports and schedule instances, not bound there | Merged | Done |
| DTW-76 | STING_PLACER_* | Med | <ALL> but written to detail lines and annotations; the group override is unreachable when spec-driven | Merged | Done |
| DTW-77 | WARN_STING_PACK_DRIFT | Low | View warning bound to elements | Merged | Done |
| DTW-78 | ManagedTemplateSyncer.cs:576 | Low | Literal parameter name instead of the ParamRegistry constant | 5da278b68 | Done |
| DTW-79 | DrawingTokenContext.BuildForExistingSheet | Med | Heal fills {lvl} with the level name under the ISO policy, disagreeing with the number | Merged | Done |
| DTW-80 | ProjectSetupCommand elevations | Med | Wizard looks for the raw exterior::face:: tag; the producer re-stamps it as Exterior-<Face> | Merged | Done |
| DTW-81 | DrawingProducer.AdoptView | Med | Reported adoption even when the stamp failed; cache failure left a stale index | 08fe22fc9 | Done |
| DTW-83 | AnnotationRunner.cs:356 / TagCategory | High | Room/space/area rules use IndependentTag, so each throws or duplicates; existing room tags unseen | Merged | Done |
| DTW-84 | MEPDimensioner.cs:152 | Med | Chains never cross fittings; witness lines parallel to their references; no idempotency | Merged | Done |
| DTW-85 | AnnotationRunner / MEPDimensioner collectors | Med | Host-only: linked MEP and linked grids get no annotation, silently | Merged | Done |
| DTW-86 | AnnotationRunner.cs:686 | Low-Med | Grid chains assume world-axis grids | Merged | Done |
| DTW-87 | DrawingProduceAndExportCommand.cs:572 / DrawingPackageManager.cs:122 | High | PDFExportOptions.FileName without Combine: files reported missing, nothing registered | Merged | Done |
| DTW-88 | DrawingProduceAndExportCommand.cs:570 | Med | PDF named number_name with no revision; P02 overwrites P01; differs from the Export Centre | Merged | Done |
| DTW-89 | STING_DRAWING_TYPES.json (9 patterns) | Med | Own ISO patterns still freeze -{suit}-{rev} | Merged | Done |
| DTW-90 | ScopeBoxPlannerService.cs:279 / ScopeBoxPlanner.cs:343 | Med-High | Area-box levels keyed by name-derived code; a rename orphans boxes and plans | Merged | Done |
| DTW-91 | ScopeBoxPlanner.cs:239 | Med | Re-plan after growth renumbers and moves existing boxes | Merged | Done |
| DTW-92 | ScopeBoxRevit.cs:143 | Low-Med | Rotated LOC box uses its bounding box | Merged | Done |
| DTW-93 | ScopeBoxBinder / ScopeBoxNames / ParameterHelpers LOC | Low | Name grammar rules differ by prefix (case, trim, spaces) | Merged | Done |
| DTW-94 | ShopDrawingComposer.cs:474 | Med | Spool sheets ignore the sheet-number policy | Merged | Done |
| DTW-95 | WORKFLOW_MEPDrawingProduction.json | Med | STING:: projects run both scope-box and per-level production: duplicate drawings | Merged | Done |
| DTW-96 | DocAutomationExtCommands.cs:478 | Low | BatchCreateSheets / DocumentationPackage bypass SheetNumbering and the policy; unstamped | Merged (fix/dt-batchsheets) | Done |
| DTW-97 | DrawingProducer apply options | Low | Type far clip never applied (opt-in had no caller) | Wired on new views without CustomBounds | Done |
| DTW-98 | DrawingProducer placement | Low | STING_AUTO_PLACED_BOOL writes to viewports never land | Extensible Storage via MarkAutoPlaced | Done |
| DTW-99 | WORKFLOW_MEPDrawingProduction / per-level producer | Med | no_production_boxes suppresses all per-level MEP plans when any STING:: box exists; should skip only covered (type, level) pairs | Merged | Done |
| DTW-100 | ShopDrawingComposer | Low-Med | ISO spool {lvl} from ASS_LVL_COD_TXT, not the producer's ISO level code | Merged | Done |
| DTW-101 | ElementDimensioner.RunColumnToGrid | Low-Med | Column-to-grid uses host grids only | Merged | Done |
| DTW-102 | AnnotationRunner MEP dimension passes | Low | Linked MEP runs are reported, not dimensioned | Accepted limit: dimensioning through a link needs link references on pipe geometry, unverified off-Revit | Open |
| DTW-103 | DrawingProducer.FindExistingSheet ~1314/1340 | Med | Legacy name-only sheet stamp accepted with no level check (the view lookup has one), so after a level rename and reuse the new plan lands on the old level's sheet | fix/dt-review4 | In progress |
| DTW-104 | SheetPlacementBridge.ResolveDrawableForFamily ~357 | Med | A failed title-block spec load is cached for the session | Merged | Done |
| DTW-105 | DrawingProducer.BuildIsoLevelMap ~2377 | Low-Med | ISO sheet level ignores project-declared level codes (spatial_codes.json), unlike tags, boxes and project-pattern sheets | fix/dt-review4 | In progress |
| DTW-106 | ProjectSetupCommand ~1850 vs BatchProduceCommands ~849 | Low | Wizard grid sections carry no package id, so a second, empty sheet is minted when DOCS uses a package | fix/dt-review4 | In progress |
| DTW-107 | BatchProduceCommands ~666 | Low | Unguarded RollBack in a catch can abort the whole TransactionGroup | fix/dt-review4 | In progress |
| DTW-108 | DrawingProducer PrimeBatchScope ~156/231 | Low | A nested scope resets the outer batch's caches, including the sheet-claim table | fix/dt-review4 | In progress |
| DTW-109 | PanelDoorDiagramCommand ~225 | Low | Drafting view named by board name; a rename orphans the old view | Merged | Done |
| DTW-110 | BatchProduceCommands ~991-1004 | Low | Exterior job loop uses placed/legacy snapshots taken once | fix/dt-review4 | In progress |

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
- **Binding scope (DTW-55, bindings agent).** View-stamp parameters are scoped to Views
  (and Sheets where written there), not left on every element too. Nothing writes them to
  elements, and existing projects keep what is already bound; Load Shared Parameters only adds.
  A hand-authored Yes row now replaces an `<ALL>` binding in the resolver.
- **Lying-catch gate.** `PresetDialog.Show` counts as reporting the outcome (it shows the dialog
  or fills `message`). Baseline ratcheted 151 → 145.
- **Routing semantics / codes / ids (DTW-69..71, data agent).** No data change. There is no
  general structural-detail or plumbing-plan type to route to, the ambiguous codes cannot
  collide (different segment positions, template-keyed counters), and ids are never renamed.
- **WARN_STING_PACK_DRIFT (DTW-77).** Left `<ALL>`: nothing writes it, and its only reader is a
  tag label on elements.
- **STING_AUTO_PLACED_BOOL (DTW-75/98).** Moved to Extensible Storage: Revit binds no shared
  parameter to viewports or schedule instances.
- **Far clip (DTW-65/97).** Applied only to new views without their own bounds, so a depth
  someone adjusted by hand survives Sync Styles.
- **Batch sheets (DTW-96).** Numbered by the project pattern at creation, as the Sheet
  Manager does, not by the drawing-type policy. The sheets are stamped when every view on them
  shares one drawing type, so `DrawingTypes_Renumber` can move them onto the type's pattern.
  This keeps a single creation rule for hand-assembled sheets.
- **ISO number (DTW-44).** Suitability and revision leave the container id (ISO 19650 keeps them
  as metadata). Only new sheets use the new pattern; existing numbers are not rewritten.

## NEEDS REVIT CHECK

Round 3 (DTW-83..96):
- **Room/space/area tags.** One room tag per room at its location; a re-run places nothing; a
  manual tag is left alone.
- **Linked MEP plan.** Linked pipes and fixtures are tagged and follow the link; linked grids
  are dimensioned when the host has none.
- **Rotated grids at 30°.** Two chains, perpendicular to their sets.
- **MEP run chain.** One chain per straight through fitting centres; check that
  `Line.GetEndPointReference` gives usable references.
- **Produce & Export.**
  - PDFs exist, named by the ISO template, and are registered.
  - Changing the revision does not overwrite the earlier PDF.
- **Re-plan after the model grows.** Existing area boxes keep their positions and names.
- **Planner after a level rename or clash.** Boxes still resolve to their own level, and
  `levelIds` is in the plan file.
- **Turned LOC box.** Tiles are turned with it, with no extra tiles.
- **ISO spool numbers.** No clash with produced sheets.
- **Batch Create Sheets / Documentation Package.** Numbered by the project pattern and stamped
  per drawing type.


- **Title-block master path (research, unverified).** Build one master-path title-block family
  from a master that already has a revision table. Expect one revision schedule, not two.
- **PDF Combine default (DTW-87).** Export one sheet with Produce & Export and confirm the file
  name Revit wrote.

Producer checks (DTW-20..54):
- Rename "Level 1" to "Ground Floor" and re-run per-level production: expect no new views,
  sheets or numbers.
- A `STING::<type>::L01` box on a level named "Level 1" produces on that level.
- ISO policy: new numbers have no `-S2-P01` tail; existing sheets are unchanged; the counter
  continues.
- Sections: only ticked grids are cut. A section type on a box cuts through the box; a 3D
  type is boxed by it.
- Interior elevations: four compass-named views share one marker, on the room's own plan.
- Exterior elevations: run twice with no duplicates; 1+4 puts four faces on one sheet.
- Match lines: move a box and Generate moves the lines; delete a plan and its curves are pruned.
- Linked MEP: per-level default produces plans when MEP is in a link.
- Presets: save, reload and overwrite by name; VG, scale and annotation options apply; Skip
  empty levels lists the skipped levels.

Binding checks (DTW-55..59), after Load Shared Parameters:
- `STING_DRAWING_TYPE_ID_TXT` shows in a view's properties.
- Re-produce and expect no duplicate views.
- `STING_MATCH_*` resolve on Lines: the log shows 1/1, not 0/1. 0/1 means Revit refuses Lines,
  and the key moves to Extensible Storage.
- Healthcare filters create without warnings.


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
