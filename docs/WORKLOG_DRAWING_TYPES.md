# Drawing types: review-and-fix worklog

The standing loop for drawing production: research, log, fix, build, test, merge, repeat.
It covers drawing-type data, templates, producers, scope boxes, match lines, QA tools,
presets, binding files and docs. This file is the handover: a fresh session continues from
**Resume here** alone.

## Resume here

Round 8 (the deeper pass) started 2026-10-02 on branch `fix/drawing-review-2`, from main
ad42c8bda, which includes PR #1021. The earlier rounds' PR #1021 is merged and deployed
(STING_KUT_LIVE c73ded891).

1. Four read-only researchers are running:
   - view graphics: packs, filters, templates, crop
   - sheets and title blocks: factory, slots, placement
   - authoring and config: editor, Excel round-trip, registries, JSON loaders
   - end-to-end edge cases: worksharing, scale, phases, design options, odd levels and boxes,
     re-runs after user edits, policy switch, undo

   Log their findings as DTW-149 onwards. Fix them on disjoint file sets, merging one branch at
   a time with build, tests and gates in between.
2. Open a PR for `fix/drawing-review-2` when it has merged work; merge it once CI is green. Ask
   the ACC session to redeploy STING_KUT_LIVE from its integration branch.
3. Still open from before: DTW-82 (in-Revit checks) and DTW-102 (linked MEP runs reported, not
   dimensioned).

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
| DTW-82 | Whole loop | High | Nothing merged in this loop has been run in Revit | Run the NEEDS REVIT CHECK list | Open (needs Revit) |
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
| DTW-103 | DrawingProducer.FindExistingSheet ~1314/1340 | Med | Legacy name-only sheet stamp accepted with no level check (the view lookup has one), so after a level rename and reuse the new plan lands on the old level's sheet | Merged | Done |
| DTW-104 | SheetPlacementBridge.ResolveDrawableForFamily ~357 | Med | A failed title-block spec load is cached for the session | Merged | Done |
| DTW-105 | DrawingProducer.BuildIsoLevelMap ~2377 | Low-Med | ISO sheet level ignores project-declared level codes (spatial_codes.json), unlike tags, boxes and project-pattern sheets | Merged | Done |
| DTW-106 | ProjectSetupCommand ~1850 vs BatchProduceCommands ~849 | Low | Wizard grid sections carry no package id, so a second, empty sheet is minted when DOCS uses a package | Merged | Done |
| DTW-107 | BatchProduceCommands ~666 | Low | Unguarded RollBack in a catch can abort the whole TransactionGroup | Merged | Done |
| DTW-108 | DrawingProducer PrimeBatchScope ~156/231 | Low | A nested scope resets the outer batch's caches, including the sheet-claim table | Merged | Done |
| DTW-109 | PanelDoorDiagramCommand ~225 | Low | Drafting view named by board name; a rename orphans the old view | Merged | Done |
| DTW-110 | BatchProduceCommands ~991-1004 | Low | Exterior job loop uses placed/legacy snapshots taken once | Merged | Done |
| DTW-111 | PipeNetworkGraph.ClassifyStacks ~705 | High | Every vertical drop is a stack: fake stacks, and fixtures behind tails never counted | Merged | Done |
| DTW-112 | DwgFixtureBridge ~287 | Med-High | No idempotency: a re-run places everything twice | Merged | Done |
| DTW-113 | SeedEnsurer ~128 / DwgFixtureBridge ~403 | Med | Any loaded family marks a category served, so the seed is never built and the bridge skips the category | Merged | Done |
| DTW-114 | DrawingProducer refresh ~481 | Med | Re-runs never tag or dimension elements added since; no command re-annotates produced views | Merged | Done |
| DTW-115 | LinkLevelMapper ~26 | Med | 3 mm at-or-below tolerance maps an MEP SSL level to the storey below | Merged | Done |
| DTW-116 | IsoLevelCode.BuildMap ~56 | Med | Coincident levels each take a number and shift ISO codes | Merged | Done |
| DTW-117 | DrawingTokenContext ~71 | Med | Multi-building ISO: {vol} never carries the building | Merged | Done |
| DTW-118 | DrawingRenumberCommand ~236 | Med | Renumber reads the stamp's level name, not its id: wrong ISO level after a rename | Merged | Done |
| DTW-119 | Drainage/Supply schematic views | Med-Low | A new view every run, orphans accumulate | Merged | Done |
| DTW-120 | Drainage/Supply layout | Med-Low | True-Z / per-element columns overflow the sheet on large models | Merged | Done |
| DTW-121 | DrainageSchematicGenerator labels | Low-Med | Labels collide with neighbouring stacks and levels | Merged | Done |
| DTW-122 | BatchProduceCommands.LevelHasModel ~236 | Low-Med | Skip-empty-levels ignores links for non-MEP plans | Merged | Done |
| DTW-123 | Doctor | Low | Views and sheets of deleted boxes and levels are never reported | Merged | Done |
| DTW-124 | PlumbingVisualisationCommands ~117/742, DwgFixtureBridge ~357 | Low | Commit status ignored | Merged | Done |
| DTW-125 | ViewStylePackApplier ~625 | Low | Material-class filter cache never invalidated | Merged | Done |
| DTW-126 | MergeRecoveryStubs ~520 | Low | Index miss not revalidated | Merged | Done |
| DTW-127 | ViewStylePackApplier ~549 | Low | Link overrides matched on the instance name | Merged | Done |
| DTW-128 | SupplySchematicGenerator ~197/~109 | Low | DN printed twice per pipe; rank-3 source not marked assumed | Merged | Done |
| DTW-129 | ParameterHelpers.DeriveSheetLevel ~4590 | Med | Sheet level stamp uses the elevation code; after DTW-105 the number uses the declared code | Merged | Done |
| DTW-130 | AnnotationRunner matchline frame | Low | Four plain detail lines with no provenance, so a re-run adds another frame; refresh skips the whole decorative pass when a pack sets matchlineOffsetMm | Stamped frame; hold-back removed | Done |
| DTW-131 | DwgCaptureDedup / DwgFixtureBridge | High | Dedup ignores level: stacked identical floors get no fixtures after the first | Merged | Done |
| DTW-132 | ProjectSetupCommand MatchLevels ~1696 | Med-High | Wizard dependents match level name only (area/STING:: code boxes skipped); area boxes get every plan type | Merged | Done |
| DTW-133 | IsoLevelCode tolerance 50 mm | Med | SSL levels 50-150 mm below FFL become separate storeys | Merged | Done |
| DTW-134 | DrawingTokenContext.BuildForExistingSheet ~262 | Med | Heal takes {lvl} from the stamp's level name (stale after a rename) | Merged | Done |
| DTW-135 | PrintManager / SheetTemplateEngine / AutomationEngine PDFs | Med | FileName without Combine; exported path recorded without checking it exists | Merged | Done |
| DTW-136 | AutoNumberSheetsCommand | Med | Renumbers all sheets as XX-NNN, bypassing SheetNumbering (ISO, locks, history) | Merged | Done |
| DTW-137 | ExportCenterEngine.ResolveProducedFile ~1523 | Low-Med | Stale earlier PDF counted when the re-export failed | Merged | Done |
| DTW-138 | ParameterHelpers _levelMap vs SheetNumbering | Low-Med | Session-long level map cache used by retag after level edits | Merged | Done |
| DTW-139 | DrawingPackageManager ~119 | Low | Package PDF names carry no revision | Merged | Done |
| DTW-140 | AnnotationRunner.DimGrids | Low | No coincident-grid filter: an offset linked grid can beat the host | Merged | Done |
| DTW-141 | DrawingTokenContext.ApplyContextVolume | Low | Expensive lookups before checking the pattern uses {vol} | Merged | Done |
| DTW-142 | BatchScopeDepth across documents | Low | Nested scope on another document wipes the outer batch's claims | Merged | Done |
| DTW-143 | ScopeBoxStyle / RenamePattern / MatchLineEngine | Low | Box names parsed by hand, not through ScopeBoxNames | Merged | Done |
| DTW-144 | LOC index (ParameterHelpers ~1735) | Low-Med | Stricter grammar drops user-typed LOC names silently (log only) | Merged | Done |
| DTW-145 | TaggingModels scope-box report | Low | The report copies the latest box-name audit; it can be stale if tagging reuses a cached setup or ran on another model in between | Cache hit sets the report's audit from the context | Done |
| DTW-146 | ParameterHelpers level-map key | Med-Low | spatial_codes.json edits not seen: the registry kept its old cache | Reload the registry on a key miss | Done |
| DTW-147 | ResolveAllIssuesCommand ~122 | Low-Med | Report showed another run's scope-box audit | Take the model's own audit after the context builds | Done |
| DTW-148 | AnnotationRunner matchline frame | Low-Med | Stamped frame never followed a crop change | Redraw when incomplete or not matching the crop | Done |
| DTW-149 | TitleBlockSpec.Resolve ~466-497 | High | Scalars fill-if-blank root-first: 12 working A0/A2/A3 families resolve the A1 template (.rft); COVER/A2 data errors too | fix/dt-r8-tb | In progress |
| DTW-150 | SheetPlacementBridge ~274 | Med-High | Fit-to-slot overrides the type's scale in both directions; should only coarsen when it does not fit | fix/dt-r8-tb | In progress |
| DTW-151 | DrawingProducer ~1911 / SheetPlacementBridge ~454 | Med-High | Schedules placed at the slot centre (their point is the top-left), so they run off the slot | fix/dt-r8-tb | In progress |
| DTW-152 | TitleBlockResolver.ResolveMode ~94 | Med | Reads PRJ_SHEET_BIM_MODE_TXT from Project Information, but it is bound to Sheets only, so NONBIM is never chosen | fix/dt-r8-tb | In progress |
| DTW-153 | TitleBlockSwap / Set Variant / Toggle BIM / MigrateLegacy | Med | Family swaps ignore the title-block lock | fix/dt-r8-tb | In progress |
| DTW-154 | TitleBlockCommands Populate / Sheet Count / Transmittal | Med | Instance-only lock check, missing type and sheet locks | fix/dt-r8-tb | In progress |
| DTW-155 | STING_TITLE_BLOCKS.json COVER_A2/A3, CLARIFICATION_A3 | Med | Inherit A1 drawable and slots on A2/A3 paper | fix/dt-r8-tb | In progress |
| DTW-156 | TitleBlockFactory master path ~276/297/671 | Med (Revit) | Revision schedule placed before master propagation, with no ScheduleSheetInstance exclusion: possible duplicate revision table | fix/dt-r8-tb | In progress |
| DTW-157 | SheetPlacementBridge fit ~256 | Med-Low | Fit ignores annotation extents; a failed scale set is swallowed; overflow never reported | fix/dt-r8-tb | In progress |
| DTW-158 | TitleBlock_AutoPlaceViewports ~141 | Med-Low | Ignores the title-block origin | fix/dt-r8-tb | In progress |
| DTW-159 | SheetManagerEngine Clone ~739 | Low | Copies the title block's revision schedule instance | fix/dt-r8-tb | In progress |
| DTW-160 | Toggle BIM / Migrate ~301,168 | Low | Takes the first type rather than the same-named type | fix/dt-r8-tb | In progress |
| DTW-161 | SheetPlacementBridge _drawableCache | Low | Session cache ignores edits to the title-block JSON | fix/dt-r8-tb | In progress |
| DTW-162 | SheetSequenceStore.SeedFromExistingSheets ~323 | Low | Seeds ignore discipline and vol: numbering gap on first use | fix/dt-r8-tb | In progress |
| DTW-163 | ManagedTemplateSyncer ~625 | High | Managed packs release V/G: produced views get no pack overrides or filters | fix/dt-r8-vg | In progress |
| DTW-164 | STING_AEC_FILTERS.json enum values | High | Structural material/usage and wall-function integers wrong (concrete and steel swapped, etc.) | fix/dt-r8-vg | In progress |
| DTW-165 | ViewStylePackApplier / ResolveFillPattern / MEP system filters | High | 'Solid fill' never resolves; other pattern names uncreated; colour with no pattern draws nothing; misses silent | fix/dt-r8-vg | In progress |
| DTW-166 | AecFilterFactory phase rules | Med-High | PHASE_DEMOLISHED notEquals 'None' makes a null rule; corp-base filter never created | fix/dt-r8-vg | In progress |
| DTW-167 | AecFilterFactory.FindOrCreate | Med | Existing filters never updated, so data fixes never reach projects that already ran | fix/dt-r8-vg | In progress |
| DTW-168 | TemplateManagerCommands.LoadViewFiltersFromCsv | Med | CSV rule prose unparsed: every row becomes a category-wide filter | fix/dt-r8-vg | In progress |
| DTW-169 | ViewStylePackApplier.ApplyPresetOverrides | Med | Subcategory rows dropped; most VG fields ignored; raw weights throw | fix/dt-r8-vg | In progress |
| DTW-170 | Managed packs 'scale' | Med (Revit) | Template may lock every view to the seed's scale | fix/dt-r8-vg | In progress |
| DTW-171 | AecFilterFactory compound/values | Med-Low | AND drops a failed child (broader filter); unparseable value becomes 0 | fix/dt-r8-vg | In progress |
| DTW-172 | DrawingCropApplier tight/room | Low-Med | View-scoped collector can only shrink the crop; includes datums | fix/dt-r8-vg | In progress |
| DTW-173 | ViewStylePackApplier ~65 | Low | Overrides written to a view whose template masks them; misleading message | fix/dt-r8-vg | In progress |
| DTW-174 | ViewStylePackRegistry ~296 | Low | Child's default line-weight scale overwrites the parent's | fix/dt-r8-vg | In progress |
| DTW-175 | CreateVGOverridesCommand | Low | Unreachable branch; adds every STING filter | fix/dt-r8-vg | In progress |
| DTW-176 | ManagedTemplateSyncer ~292 | Low | Schedules excluded from templates | fix/dt-r8-vg | In progress |
| DTW-177 | ApplyMaterialClassOverrides | Low | Dead code; adds a filter with no override | fix/dt-r8-vg | In progress |
| DTW-178 | DrawingTypeEditorDialog pack load/save | Critical | Editor loads corporate packs only; every Save erases previously saved project packs | fix/dt-r8-auth | In progress |
| DTW-179 | DrawingTypeExcelCommands validation | High | Shipped catalogue cannot round-trip (NA scale, Schematic/Coordination slots, ByDiscipline) | fix/dt-r8-auth | In progress |
| DTW-180 | Excel routing sheet | High | Predicate fields lost (rules become catch-alls); corporate rules written to the override | fix/dt-r8-auth | In progress |
| DTW-181 | Excel change detection / POCOs | High | Unedited import freezes the whole catalogue in the override and drops fields | fix/dt-r8-auth | In progress |
| DTW-182 | Excel decimals | Med | Comma-decimal cultures fail import | fix/dt-r8-auth | In progress |
| DTW-183 | Excel ApplyImport packs | Med | Corporate pack routing written to the project file | fix/dt-r8-auth | In progress |
| DTW-184 | DrawingTypeRegistry ES path | Med | After ES_Migrate, file edits are ignored | fix/dt-r8-auth | In progress |
| DTW-185 | DrawingTypeRegistry routing de-dup | Med | OptionMatches not in the signature | fix/dt-r8-auth | In progress |
| DTW-186 | Editor ids | Med | Renamed corporate types dropped; duplicate clone ids; no pre-save validation | fix/dt-r8-auth | In progress |
| DTW-187 | Registry load failure + non-atomic writes | Med | Malformed override then Save means project data lost; non-atomic writers | fix/dt-r8-auth | In progress |
| DTW-188 | ProductionPresetRegistry | Med-Low | Save reports success on failure; failed load erases presets on the next save | fix/dt-r8-auth | In progress |
| DTW-189 | Excel import unsaved model | Low-Med | Writes to a folder the registry never reads | fix/dt-r8-auth | In progress |
| DTW-190 | Reload on close / Reload JSON | Low-Med | Packs and match-line config never reloaded | fix/dt-r8-auth | In progress |
| DTW-191 | Legacy routing without origin | Low | Frozen corporate rules load as project rules | fix/dt-r8-auth | In progress |
| DTW-192 | TitleBlockMigrateCsvToRecipe | Low | Corporate CSV preferred; hand-built path; wrong instruction | fix/dt-r8-auth | In progress |
| DTW-193 | Editor push template | Low | Keeps the corporate checksum on a promoted entry | fix/dt-r8-auth | In progress |

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
- **Declared level codes (DTW-105/129).** A project-declared level code (spatial_codes.json) wins
  in the ISO map. One map, `DrawingProducer.BuildIsoLevelMap`, feeds the sheet number, spool
  sheets, title-block heal and the sheet level stamp.
- **Package sheets (DTW-106).** A reused view's existing sheet is reused across packages, with a
  warning. The sheet key is unchanged, so existing per-package sheets behave as before.
- **Stack rule (DTW-111).** A drainage stack is a vertical run at one plan position, at least
  2 m long, that crosses a level. This is the agent's threshold, not a standard; tune it in
  `SchematicLayoutMath.StackRuns` if a project needs otherwise.
- **Link level band (DTW-115).** A link level maps to the nearest host level within
  min(300 mm, half the storey); outside that band, to the level at or below.
- **Volume (DTW-117).** `{vol}` comes from `_BIM_COORD/sheet_volumes.json` (LOC → volume), else
  the LOC code. It applies only when the pattern names `{vol}`.
- **Auto-number in presets (DTW-136).** Plan-only unless the step sets `apply`. The built-in
  Document Package, Weekly Data Drop and Residential presets set `apply = true`, because
  numbering is their purpose. Locked and ISO-identifier sheets are always left alone;
  drawing-type numbers follow the project pattern.
- **Nested batches across documents (DTW-142).** The outer batch's caches are set aside and
  restored, not wiped.
- **Storey band (DTW-133).** One helper, `LevelSnapBand`: levels within min(300 mm, half the
  storey) are one storey, both for ISO codes and for link-level mapping.
- **ISO number (DTW-44).** Suitability and revision leave the container id (ISO 19650 keeps them
  as metadata). Only new sheets use the new pattern; existing numbers are not rewritten.

## NEEDS REVIT CHECK

Round 8 view graphics:
- Can PHASE_CREATED and PHASE_DEMOLISHED be filter rules?
- Is STRUCTURAL_MATERIAL_TYPE accepted on walls and floors?
- Which scale parameter id does GetTemplateParameterIds return?
- Does a view-scoped collector exclude elements outside the crop?
- After the fix, does a produced view from a managed pack show the pack's overrides and
  filter fills?


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
