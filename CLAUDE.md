# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **215 source files** (212 C# + 3 XAML, ~254,000 lines of code) across 13 directories
- **763+ `IExternalCommand` classes** (commands) + 3 `IPanelCommand` classes + 1 `IExternalApplication` entry point + 1 `IExternalEventHandler` + 1 `IDockablePaneProvider` + 2 `IUpdater`s
- **72 runtime / embedded data files** (CSV, JSON, TXT, XLSX, PY, MD, DOCX) — includes the template engine v1.1 pack (16 templates + 5 workflow definitions + `manifest.json`)
- **WPF dockable panel** (9 tabs, primary UI) + 1 BIM Coordination Center (13 tabs) + 1 Material Manager (7 tabs) + 1 Document Management Center (8 tabs) + ribbon retained for legacy compat
- **Top-level workspace** ships 15 directories: `StingTools/` · `Planscape/` · `Planscape.Server/` · `StingBIM.Server/` · `StingTools.Clash.Tests/` · `StingTools.Dynamo/` · `StingTools.Headless/` · `StingTools.Standards/` · `Tests/` · `Families/` · `CompiledPlugin/` · `docs/` · `docs-site/` · `marketing-site/` · `tools/`

### Phase 174 — Branch consolidation

This branch (`claude/merge-branches-update-docs-SvA31`) absorbs every
outstanding `claude/*` feature branch that had unique commits not yet in
HEAD. Seventy-five branches were merged in a single pass, ranging from
single-commit fix branches up to the 1,148-commit
`document-missing-parameters-9tTAr` history. Conflict policy was
`-X ours` (keep the Phase 168–173 baseline, take new files from the
incoming branch); for `modify/delete` and `rename/delete` collisions the
HEAD copy was kept. Sixty-four branches fast-forwarded or auto-merged
cleanly; eleven required `ours` conflict resolution. After consolidation
`git branch -r --no-merged HEAD` reports zero remote branches with
unique commits not in HEAD. Verify in Revit before merging to `main`.

## Documentation Map

This repository's AI-assistant documentation is split across three files to keep each one small and easy to update:

| File | Purpose | When to edit |
|---|---|---|
| `CLAUDE.md` (this file) | **Stable reference** — architecture, directory layout, command catalogue, UI structure, build/deploy, conventions | When the codebase's structure or commands change |
| `docs/CHANGELOG.md` | **Phase-by-phase history** — every `Completed (Phase X)` block in chronological order | When a new phase of work lands; append a new `#### Completed (Phase N — …)` section |
| `docs/ROADMAP.md` | **Open gaps & future work** — automation-gap tables, future-enhancement lists, deep-review findings | When new gaps are identified or an item is closed (move it to `CHANGELOG.md`) |

When you finish a piece of work, log it in `docs/CHANGELOG.md` rather than extending this file. When you identify a new gap, add it to `docs/ROADMAP.md` — that keeps this file focused on what the code **is** rather than what it has been or might become.

---

## v4 MVP

**Status**: Phase 1 (parameters) → Phase 5 (fabrication) implemented across ~50 commits on branch `claude/sting-tools-v4-mvp-SiPGw`. Code committed without `dotnet build` verification (Linux sandbox, no .NET / Revit API). Verify in Revit before merge.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Core/Placement/` | Fixture placement engine (rule + scorer + candidate + lighting grid) |
| `StingTools/Core/Routing/` | Auto-drop engines (DropEngineBase + AutoConduitDrop / AutoPipeDrop / AutoDuctDrop) |
| `StingTools/Core/Validation/` | Five v4 validators (connectivity / fill / spec / termination / slope) + ValidationResult record |
| `StingTools/Core/Fabrication/` | Fabrication coordinator + AssemblyGrouper + AssemblyBuilder + AssemblyViewBuilder + ShopDrawingComposer + IsoSymbolPlacer + per-discipline subfolders (Electrical / Pipe / Duct) |
| `StingTools/Commands/Placement/` | PlaceFixturesCommand, LightingGridCommand, LearnPlacementV4Command |
| `StingTools/Commands/Routing/` | AutoDropCommand, GenerateLayoutCommand, ValidateFillsCommand |
| `StingTools/Commands/Validation/` | RunAllValidatorsCommand |
| `StingTools/Commands/Fabrication/` | GenerateFabPackageCommand, ExportCutListCommand, ExportIsometricsCommand, ExportWeldMapCommand |
| `StingTools/Data/Placement/` | STING_PLACEMENT_RULES.json (43 rules) |
| `StingTools/Data/Fabrication/` | STING_FAB_RULES.json (6 disciplines), STING_ISO_SYMBOLS_INDEX.csv (180+ symbols) |
| `StingTools/Data/Parameters/` | STING_PARAMS_V4.txt shared-parameter fragment |
| `Families/AssemblyTitleBlocks/` | 7 title block parameter spec stubs + README |

### New namespaces

`StingTools.Core.Placement`, `StingTools.Core.Routing`, `StingTools.Core.Validation`, `StingTools.Core.Fabrication`, `StingTools.Core.Fabrication.Electrical`, `StingTools.Core.Fabrication.Pipe`, `StingTools.Core.Fabrication.Duct`, `StingTools.Commands.Placement`, `StingTools.Commands.Routing`, `StingTools.Commands.Validation`, `StingTools.Commands.Fabrication`.

### New commands (12)

| Command tag | Class | Tab |
|---|---|---|
| `Placement_PlaceFixtures` | `PlaceFixturesCommand` | Fixtures |
| `Placement_LightingGrid` | `LightingGridCommand` | Fixtures |
| `Placement_Learn` | `LearnPlacementV4Command` | Fixtures |
| `Routing_AutoDrop` | `AutoDropCommand` | Routing |
| `Routing_GenerateLayout` | `GenerateLayoutCommand` | Routing |
| `Routing_ValidateFills` | `ValidateFillsCommand` | Routing |
| `Validation_RunAll` | `RunAllValidatorsCommand` | (called from Routing) |
| `Fabrication_GeneratePackage` | `GenerateFabPackageCommand` | Fabrication |
| `Fabrication_ExportCutList` | `ExportCutListCommand` | Fabrication |
| `Fabrication_ExportIsometrics` | `ExportIsometricsCommand` | Fabrication |
| `Fabrication_ExportWeldMap` | `ExportWeldMapCommand` | Fabrication |
| `Fabrication_PlaceISOSymbols` | inline TaskDialog (placement runs in GeneratePackage) | Fabrication |

### New tags Studio sub-tabs (3)

`Fabrication`, `Routing`, `Fixtures` — added to `tagStudioTabs` inside the existing TAGS top-tab. Each follows the proven DockPanel + pinned WrapPanel + ScrollViewer pattern.

### New parameter count

- **6 net-new shared parameters** (S1.1 + S1.2): PLACE_ANCHOR / PLACE_OFFSET_X_MM / PLACE_SIDE / CPC_SZ_MM / PPE_INSULATION_THK_MM / PLM_SLOPE_PCT_V4
- **46 fabrication / LPS / pricing constants** (S1.3) in `StingTools.Core.Fabrication.{AssyParams,LpsParams,CostParams}` with placeholder `v4-YYYY-xxxx` GUIDs awaiting family library authoring.
- **Total**: 52 new constants. The 6 net-new ones use stable GUIDs; the 46 placeholders need real GUIDs assigned during family-library authoring.

### Caveats

1. Built without `dotnet build` verification — every Revit API call uses the documented signature but has not been compile-checked. `// TODO-VERIFY-API` comments mark the most uncertain spots (`AssemblyInstance.Create` category arg, `Conduit.Create` / `Pipe.Create` / `Duct.Create` overloads, ISO 6412 axonometric section transform).
2. The 7 title block `.rfa` families are NOT shipped — only their parameter specs. `ShopDrawingComposer.ResolveTitleBlock` falls back to the first available title block in the project (now warns loudly via `FabricationResult.Warnings` and `StingLog`).
3. ISO 6412 detail families (180 entries) are referenced by name in `STING_ISO_SYMBOLS_INDEX.csv`; `IsoSymbolPlacer` lazy-loads from `Families/ISO6412/` and warns once per missing family.

## Electrical Panel Schedules (Phase 176)

**Status**: Landed on branch `claude/research-panel-schedules-J0qqo`. Built without `dotnet build` verification — the Revit API surface for `PanelScheduleTemplate.GetPanelConfiguration` is invoked via reflection so the call compiles regardless of Revit version. Verify in Revit before merge.

### New folder

`StingTools/Commands/Panels/` — `PanelScheduleTemplateRegistry.cs` (rule loader + project override merge + multi-candidate `ResolveCandidates` for AUTO-2 fallback), `BatchPanelSchedulesCommand.cs` (rule-based create + drawing-type stamp + ELC_PNL_* fill + circuit back-refs), `PanelScheduleAuditCommand.cs` (read-only drift report), `PanelScheduleExcelCommands.cs` (round-trip with empty-cell guard + load-delta diff), `PanelScheduleSlotCommands.cs` (5 slot-management commands using `GetCircuitByCell` for real-circuit detection).

### New namespace

`StingTools.Commands.Panels`.

### Commands (9)

| Tag | Class | Description |
|---|---|---|
| `Panel_BatchSchedules` | `BatchPanelSchedulesCommand` | Rule-based per-panel `PanelScheduleView.CreateInstanceView` with multi-template fallback, drawing-type stamp, ELC_PNL_* fill, circuit back-refs. The legacy `"PanelSchedule"` tag now redirects here. |
| `Panel_Audit` | `PanelScheduleAuditCommand` | Read-only audit: panels without schedules, template drift, missing PNL params |
| `Panel_ExportToExcel` | `ExportPanelSchedulesToExcelCommand` | Header + Body + Summary to `.xlsx` (one worksheet per panel + INDEX) |
| `Panel_ImportFromExcel` | `ImportPanelSchedulesFromExcelCommand` | Round-trip Body cells via `TableSectionData.SetCellText` with empty-cell guard + load-delta diff |
| `Panel_FillSpares` | `FillEmptySlotsWithSparesCommand` | `AddSpare` on empty slots of active schedule |
| `Panel_FillSpaces` | `FillEmptySlotsWithSpacesCommand` | `AddSpace` variant |
| `Panel_FillSparesAll` | `FillSparesAllSchedulesCommand` | Project-wide `AddSpare` with `TransactionGroup` so per-panel failures don't roll back others |
| `Panel_SpacesToSpares` | `ConvertSpacesToSparesCommand` | `RemoveSpace` + `AddSpare` |
| `Panel_ClearSparesSpaces` | `ClearSparesAndSpacesCommand` | Wipe spares and spaces |

### Data files

- `StingTools/Data/STING_PANEL_SCHEDULE_TEMPLATES.json` — 5 priority-ordered rules (switchboard / 3-phase DB / 1-phase consumer unit / data panel / catch-all) + skip patterns + `globalFallback`. Project override at `<project>/_BIM_COORD/panel_schedule_templates.json`.
- `StingTools/Data/WORKFLOW_PanelScheduleProduction.json` — workflow preset chaining Audit → BatchSchedules → FillSparesAll → ExportToExcel → re-Audit.

### Drawing Type

`elec-panel-schedule-A3` added to `STING_DRAWING_TYPES.json`, routed by `(discipline=Electrical, docType=PANEL_SCHEDULE)`. `BatchPanelSchedules` stamps every created `PanelScheduleView` with this id so Browser Organizer + drift detection + SyncStyles work on panel schedules.

### API limits honoured

- `PanelScheduleSheetInstance.Create` is broken in Revit 2024-2026 — STING does NOT attempt programmatic sheet placement. The result panel surfaces "drag onto sheet manually" guidance.
- `PanelScheduleTemplate` cell layout / column order / formulas remain read-only. The registry chooses *which* template to apply, never edits structure.
- Read-only Revit-managed cells (computed loads, totals) reject `SetCellText` silently — caught and reported as `cellsRejected` in import.
- Real-circuit detection uses `PanelScheduleView.GetCircuitByCell(r, c)` (returns the `ElectricalSystem` or null) — `IsCircuitRow` does not exist on PanelScheduleView.

### Caveats

1. The 5 named templates in the JSON (`STING - Switchboard Schedule`, etc.) must exist in the host `.rvt`. Without them the registry falls back to "first available template".
2. `PanelTypeMatches` in `PanelScheduleTemplateRegistry` invokes `GetPanelConfiguration` via reflection. If the method signature differs between Revit versions, configuration-aware fallback degrades to no-op (rule-name + global-fallback still work).
3. Excel import preserves Revit data when the xlsx cell is blank but the Revit cell is non-empty (anti-erasure guard). To intentionally clear a cell, use the Revit Panel Schedule UI directly.

## Drawing Template Manager (Phase 113)

**Status**: Foundation landed on `claude/fix-text-visibility-layouts-OsiY9`. A single-source engine for how every drawing is produced — sheet size, title block, scale, view template, slot layout, annotation rule pack, crop strategy, section-marker family, viewport type, sheet numbering — replacing four scattered configuration surfaces (SheetTemplateEngine, ShopDrawingComposer hard-codes, DocAutomation TaskDialog prompts, SheetManager presets).

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Core/Drawing/` | Drawing Type engine: POCO model, loader with 15 built-ins, routing dispatcher, pre-flight validator |
| `StingTools/Commands/Drawing/` | Diagnostic commands (Inspect + Reload) |
| `StingTools/Data/STING_DRAWING_TYPES.json` | Corporate baseline: 15 drawing types + routing table, extracted to `data/` at build |

### New namespaces

`StingTools.Core.Drawing`, `StingTools.Commands.Drawing`.

### Concept

A **DrawingType** is a named JSON bundle that answers every presentation question for a single produced drawing:

| Field | Purpose |
|---|---|
| `id` / `name` | Stable identifier + human label |
| `purpose` | Plan / RCP / Section / Elevation / Detail / Schedule / Spool / Coordination / Legend / 3D |
| `discipline`, `phase` | Routing keys (wildcarded with `*`) |
| `paperSize`, `titleBlockFamily`, `orientation` | Sheet identity |
| `scale`, `detailLevel` | View appearance basics |
| `viewTemplateName`, `viewportTypeName` | Graphic standards binding |
| `sheetNumberPattern`, `sheetNamePattern` | Token-substituted (see below) |
| `crop` | `ScopeBox` / `ScopeBoxOrBbox` / `TightBbox` / `RoomBoundary` / `None` + margin |
| `sectionMarker` | Family + mark prefix + bubble style + far-clip offset |
| `slots[]` | Normalised 0..1 positions on the drawable zone (paper-size independent) |
| `annotation` | `AnnotationRulePack` — what to auto-dim / auto-tag, dimension strategy, per-category tag families, `denseUntilScale` modifier |
| `print` | Colour scheme, line-weight scale, halftone links |
| `origin` | `corporate` (SHA-256 checksum-locked) vs `project` (editable) |

Rules live in the same JSON as `routing[]` — first-match-wins rules of the shape `(discipline, phase, docType) → drawingTypeId`.

### Token patterns

Sheet number and sheet name patterns are substituted by `ShopDrawingComposer.SubstituteTokens`:

| Token | Replaced with |
|---|---|
| `{spool}` | Spool number from `AssyParams.SPOOL_NR_TXT` |
| `{disc}` | ISO single-letter discipline code |
| `{discipline}` | Full discipline name (e.g. "Pipe", "Electrical") |
| `{sys}` | Sanitised system code |
| `{lvl}` | Level code |
| `{mark}` | Section / elevation / detail mark |
| `{seq}` | Zero-padded 4-digit sequence (default) |
| `{seq:D2}` / `{seq:D3}` / `{seq:D4}` | Zero-padded sequence with explicit width |

### New classes

| Class | Purpose |
|---|---|
| `Core.Drawing.DrawingType` | Root POCO — all 15 fields above |
| `Core.Drawing.DrawingSlot` | Normalised viewport slot with optional per-slot scale / detailLevel / viewTemplate / viewportType override |
| `Core.Drawing.AnnotationRulePack` | Auto-dim / auto-tag flags, dimension style, per-category `tagFamilies`, scale-aware density |
| `Core.Drawing.DrawingCropStrategy` | Crop mode + margin + optional scope-box name |
| `Core.Drawing.SectionMarkerSpec` | Section / elevation / callout marker family + mark prefix |
| `Core.Drawing.PrintOverride` | Colour scheme + line-weight scale + halftone links |
| `Core.Drawing.DrawingRoutingRule` | `(discipline, phase, docType) → drawingTypeId` |
| `Core.Drawing.DrawingTypeLibrary` | Root JSON document (`drawingTypes[]` + `routing[]`) |
| `Core.Drawing.DrawingTypeRegistry` | Loader + SHA-256 corporate-lock + project-override merge; cached per-document |
| `Core.Drawing.DrawingDispatcher` | `Resolve(doc, disc, phase, docType)` + `CandidatesForDiscipline` |
| `Core.Drawing.DrawingTypeValidator` | Pre-flight: missing title block / view template / viewport type / section-marker / tag family + slot geometry sanity |

### New commands (2)

| Tag | Class | Purpose |
|---|---|---|
| `DrawingTypes_Inspect` | `DrawingTypesInspectCommand` | Read-only diagnostic: lists all types + routing + validation issues |
| `DrawingTypes_Reload` | `DrawingTypesReloadCommand` | Force registry cache refresh after editing JSON on disk |

### Built-in corporate catalogue (40)

Shipped in `Data/STING_DRAWING_TYPES.json`. Core 15 (phase 113 foundation): `arch-plan-A1-1to100`, `arch-rcp-A1-1to100`, `arch-section-A1-1to50`, `arch-elev-A1-1to100`, `arch-detail-A3-1to20`, `struct-plan-A1-1to100`, `struct-section-A1-1to50`, `mep-plan-A1-1to100`, `mep-coord-A1-1to50`, `pipe-spool-A1-1to50`, `duct-spool-A1-1to50`, `elec-riser-A2-1to100`, `door-schedule-A2`, `handover-A1`, `legend-A2`.

Week 1 expansion adds 17 production types covering the AEC/FM lifecycle:

| Discipline | Added types |
|---|---|
| Architectural | `arch-site-A1-1to500`, `arch-roof-A1-1to100`, `arch-floor-finishes-A1-1to100`, `arch-fire-strategy-A1-1to100`, `arch-accessibility-A1-1to100`, `arch-interior-elev-A1-1to50`, `arch-window-schedule-A2` |
| Structural    | `struct-foundation-A1-1to100`, `struct-rebar-detail-A3-1to20` |
| Mechanical    | `mep-hvac-duct-A1-1to100`, `mep-plantroom-A1-1to50` |
| Electrical    | `elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100` |
| Public Health | `plumb-drainage-A1-1to100` |
| FM / handover | `fm-asset-location-A1-1to100` |
| Coordination  | `coord-clash-A1-1to50` |

Presentation + clarification pack adds 8 client-facing types (all print with `colourScheme: PresentationRich` or `ClarificationRed`, lighter line weights, minimal grid/room tagging):

| Purpose | Types |
|---|---|
| Client presentation | `pres-3d-axon-A1` (3D + key plan + caption), `pres-perspective-A1` (full-bleed perspective), `pres-exterior-elev-A1` (material callouts, mono halftone), `pres-render-board-A1` (4-up renders), `pres-context-site-A1` (aerial + legend + caption) |
| Clarification       | `clar-markup-A1` (plan + query log + revision strip), `clar-rfi-A3` (single-issue A3 sketch + question + revision), `clar-design-intent-A1` (plan + 3D + narrative + materials strip) |

Routing table grew to 43 rules covering doc types: `SITE`, `ROOF_PLAN`, `FLOOR_FINISHES`, `FIRE_STRATEGY`, `ACCESSIBILITY`, `INTERIOR_ELEVATION`, `WIN_SCHEDULE`, `FOUNDATION`, `REBAR_DETAIL`, `HVAC_DUCT`, `PLANTROOM`, `POWER`, `LIGHTING`, `FIRE_ALARM`, `DRAINAGE`, `ASSET_LOCATION`, `CLASH`, `PERSPECTIVE`, `RENDER_BOARD`, `CONTEXT`, `DESIGN_INTENT`, `CLARIFICATION`, `RFI`; presentation rules match on `phase: PRESENTATION` so the same discipline can dispatch to production vs presentation types by phase.

### Project-scoped overrides

Registry layers a project override from `<project>/_BIM_COORD/drawing_types.json` on top of the corporate baseline. Project entries win by `id`; project routing rules are **prepended** (first-match-wins). Mutating a corporate entry on disk flips its `origin` to `project` via SHA-256 checksum drift detection (see `DrawingTypeRegistry.ComputeChecksums`).

### Wiring — fabrication is the Phase I proof point

`ShopDrawingComposer` consults the registry via a 3-tier fallback chain (no regression):

1. User-picked `FabricationOptions.ShopDrawing.TitleBlockSymbolId` / `SheetNumberPattern` / `SheetNamePattern` (win)
2. Drawing Type resolved by `DrawingDispatcher.Resolve(doc, disc, "*", Spool)` — `pipe-spool-A1-1to50` / `duct-spool-A1-1to50`
3. Historic hard-coded per-discipline dict + session sequence counter

Phase II (complete, commit `384a374b`) wired `DocAutomationExtCommands.BatchSectionsCommand` / `BatchElevationsCommand` / `BatchSheetsCommand` into the registry via `DrawingTypePresentation.Apply`; each falls back to its historic template search when no profile matches. `SheetManagerCommands.CreateFromTemplateCommand` still uses the hard-coded 6 C# templates in `SheetTemplateEngine.cs` — Phase III follow-up.

### Week 2–6 enhancements (complete)

**Week 2 — ViewStylePack shared-style layer** (`Core/Drawing/ViewStylePack.cs`, `ViewStylePackRegistry.cs`, `ViewStylePackApplier.cs`). Factors graphic overrides / filters / VG overrides / text+dim styles / tag-family maps out of `DrawingType` so the 40 profiles share ~11 visual packs. `DrawingType.ViewStylePackId` references a pack by id; `Extends` field on packs supports inheritance chains. Shipped as `Data/STING_VIEW_STYLE_PACKS.json`: `corp-base`, `corp-standard-plan`, `corp-standard-rcp`, `corp-standard-section`, `corp-standard-elevation`, `corp-standard-detail`, `corp-fabrication-shop`, `corp-presentation-rich`, `corp-presentation-mono`, `corp-coordination`, `corp-clarification`. Applier walks the extends chain, resolves category names / BIC strings / subcategory `<brackets>` → ElementId, writes `OverrideGraphicSettings` on categories + filters.

**Week 3 — Parameter stamping + browser organizer** (`Core/Drawing/DrawingTypeStamper.cs`, `Commands/Drawing/DrawingBrowserOrganizerCommand.cs`). Two new shared parameters written onto every view/sheet a generator creates: `STING_DRAWING_TYPE_ID_TXT` and `STING_STYLE_LOCKED_BOOL`. Stamper is workshared-safe (`WorksharingUtils.GetCheckoutStatus` gate). Browser Organizer creates `'STING - by Drawing Type'` organizations for views + sheets, keyed off the shared param GUID via `FolderItemsParameter`. `DrawingTypes_BrowserOrganize` button in DOCS.

**Week 4 — SyncStyles + drift detection** (`Core/Drawing/DrawingDriftDetector.cs`, `Commands/Drawing/DrawingSyncStylesCommand.cs`). Detector scans every stamped view, reports SCALE / DETAIL / TEMPLATE drift. SyncStyles shows first-10 preview, on confirm re-runs `DrawingTypePresentation.Apply` (annotation off to avoid re-dimensioning) against each drifted view. Skips `STING_STYLE_LOCKED_BOOL == 1`. Inspect command surfaces drift count in its headline. `DrawingTypes_SyncStyles` button in DOCS.

**Week 5 — Scope-box auto-binding** (`Core/Drawing/ScopeBoxBinder.cs`, `Commands/Drawing/GenerateFromScopeBoxesCommand.cs`). Scope boxes named `STING::<drawing-type-id>::<level-code?>::<tag?>` auto-bind to a profile. One command creates views + applies the profile + crops to the scope box. Idempotent — re-run finds existing stamped views and re-applies rather than duplicating. `DrawingTypes_FromScopeBoxes` button in DOCS.

**Week 6 — Conditional routing + profile inheritance**. `DrawingRoutingRule` gains 5 optional regex predicates: `disciplineMatches`, `phaseMatches`, `docTypeMatches`, `levelMatches`, `projectCodeMatches`; all set predicates must match (logical AND). `DrawingDispatcher.Resolve(doc, disc, phase, docType, levelCode)` new level-aware overload; existing 4-arg form delegates. `DrawingType.Extends` field + `DrawingTypeRegistry.ResolveExtends` fold parent → child at lookup time with loop detection, mirror of the pack extends chain.

**Bonus — DrawingCropApplier**. Executes the previously-declarative `DrawingType.Crop`: `ScopeBox`, `ScopeBoxOrBbox`, `TightBbox`, `RoomBoundary`, `None`. mm margins, element-bbox unions, scope-box resolution by name. Wired into `DrawingTypePresentation.Apply` between view-template and style-pack steps.

**Bonus (4) — Title-block parameter binding**. `DrawingType.TitleBlockParams` (Dictionary<string,string>) binds title-block instance parameters declaratively. `Core/Drawing/TitleBlockParamApplier.cs` resolves the value template with two substitution kinds: `${PRJ_ORG_xxx}` (reads from `ProjectInformation` by parameter name) and `{disc}/{lvl}/{sys}/{spool}/{mark}/{seq:Dn}` (caller-supplied token dict). Unknown tokens pass through literal. Handles String / Integer / Double storage types; warns on unparsable numeric input. Wired into `ShopDrawingComposer.ApplySheetMetadata` so fabrication sheets get both hard-coded fab cells (spool, weight, FAB_LOC, status, BOM rev) and declarative corporate cells (client, project code, suitability, revision, sheet number). Editor gets a new 'Title-block parameter binding' card — row-per-param, rename-key-preserves-value. Three shipped profiles seeded with examples: `arch-plan-A1-1to100` (10 params), `pipe-spool-A1-1to50` (12 params), `pres-3d-axon-A1` (9 params).

**Phase III — SheetManager integration (complete)**. `SheetManager.CreateFromTemplateCommand` picker now lists Drawing Type profiles alongside the 6 built-in SheetTemplates + user-saved ones. `Docs/DrawingTypeSheetAdapter.cs` converts `DrawingType → SheetTemplate` on pick (slot coords pass through verbatim; both use 0..1-over-drawable-zone). Title-block resolution prefers the profile's declared family when loaded, else falls through to the historic picker. Post-create step stamps `STING_DRAWING_TYPE_ID_TXT` and runs `TitleBlockParamApplier` so every sheet from the profile path is registry-tracked and title-block-populated. Retires the last non-registry drawing-production path — every sheet-creation entry point now routes through the same profile catalogue.

### Pipeline order (final)

`DrawingTypePresentation.Apply(doc, view, dt)` runs:

1. **Lock check** — `DrawingTypeStamper.IsLocked` → skip
2. **Stamp** `STING_DRAWING_TYPE_ID_TXT`
3. **Scale** `view.Scale = dt.Scale`
4. **Detail level** `view.DetailLevel = dt.DetailLevel`
5. **View template** lookup by name + apply
6. **Crop strategy** via `DrawingCropApplier`
7. **View style pack** resolve + `ViewStylePackApplier.Apply`
8. **Annotation pass** via `AnnotationRunner` (auto-dim + auto-tag)

For sheet creation (fabrication composer, SheetManager CreateFromTemplate), an additional pair runs after the sheet is minted:

9. **DrawingType stamp** via `DrawingTypeStamper.Stamp(sheet, dt.Id)`
10. **Title-block param binding** via `TitleBlockParamApplier.Apply(doc, sheet, dt, tokens)` — `${PRJ_ORG_xxx}` + `{disc}/{lvl}/{sys}/{spool}/{mark}/{seq:Dn}` resolved against ProjectInformation + caller's token dict.

Every step is try/catch-wrapped; warnings collect into `ApplyResult.Warnings`, the run continues on failures.

### Week 7 — ViewStylePack editor tab

`DrawingTypeEditorDialog` is now a two-tab editor. Tab 1 "Drawing Types" is the original content unchanged. Tab 2 "View Style Packs" mirrors the same shape — search-filtered list on the left with `＋ New / Clone / Delete`, scrollable form on the right with five cards: Identity (id / name / description / **extends parent id combo** / origin), Appearance (lineWeightScale, textStyle, dimensionStyle, hatchPalette), Filter rules (row-per-rule grid: name / Visible / Halftone / projection colour+weight / cut colour+weight / transparency / × delete), VG overrides (per-category row grid with same cells + rename-key-preserves-value), Tag families (category → family-name map).

Save button routes to the active tab: `drawing_types.json` (tab 0, existing) or `view_style_packs.json` (tab 1, new). Only project-origin entries are written — corporate baseline on disk stays pristine. Edits to corporate packs silently flip `origin` to `project` via `ViewStylePackRegistry.ComputeChecksums` drift detection, same mechanism Drawing Types use.

The tab is a pure UI layer on top of the Week 2 data model — no changes to `ViewStylePack` / `ViewStylePackRegistry` / `ViewStylePackApplier`.

### Phase 135 — DrawingType TokenProfile

`DrawingType.TokenProfile` adds per-profile tag depth, style preset, segment mask, and colour scheme. `Core/Drawing/TokenProfileApplier.cs` runs as Step 7.5 between the View Style Pack apply and the Annotation pass so any auto-tags emitted by `AnnotationRunner` inherit the active profile's appearance. Drift detection has a `TOKEN_PROFILE_DRIFT` kind in `DrawingDriftDetector` that compares `STING_VIEW_TAG_STYLE` + `TAG_SEG_MASK_TXT` on stamped views against the resolved profile/pack pair; SyncStyles heals automatically.

### Phase 136 — Editor: ViewStylePack dropdown + full Revit VG editor + fallback chain

Editor gains a `ViewStylePackId` dropdown on Drawing Types > Views card and a full Revit-style 4-tab VG editor on the View Style Packs tab (Model / Annotation / Imported / Filters) with sub-dialogs for line + pattern overrides. `ViewStylePack` extended with `ViewTemplate`, `DetailLevel`, `ScaleHint`, `ColorScheme` so the runtime can read the same fields the editor writes; `DrawingTypePresentation.Apply` resolves the pack early and uses pack settings as fallback when the DrawingType doesn't set its own (DrawingType always wins). Bidirectional template copy: View Style Packs tab gains "Push template → bound types"; Drawing Types tab gains "↑ Push to pack" and "Use pack template" links. `docs/AEC_PRODUCTION_SET_STRATEGY.md` lays out an 11-pack × 80+ DrawingType strategy indexed by RIBA stage × discipline × output.

### Phase 137 — STING-Managed View Templates (Architecture C — Hybrid)

Each `ViewStylePack` now carries a `templateMode` field (`managed | external`). In **managed mode** STING auto-generates and maintains Revit view templates named `STING:<pack-id>:<ViewType>` from the pack JSON. `DrawingTypePresentation.Apply` Step 7 routes through `Core/Drawing/ManagedTemplateSyncer.cs` — `EnsureTemplate(doc, pack, viewType)` is idempotent (absent → copy seed of the right ViewType + rename; present + checksum match → no-op; present + drift → re-apply pack settings + restamp). Managed fields whitelist: `vg`, `filters`, `detailLevel`, `discipline`, `visualStyle`, `phaseFilter`, `phaseName`, `annotationCrop`, `farClipMm`, `viewRange`, `underlay`. Two shared parameters stamp the template for drift detection: `STING_PACK_ID_TXT`, `STING_PACK_CHECKSUM_TXT`. Three migration commands in `Commands/Drawing/ManagedTemplateCommands.cs` — `ConvertPackToManagedCommand` / `DetachFromManagedCommand` / `RegeneratePackTemplatesCommand` — wired into the DRAWING TYPES wrap-panel and the editor toolbar. `MANAGED_TEMPLATE` drift kind added to `DrawingDriftDetector`. Editor's View Style Packs tab gains a `templateMode` toggle plus managed-mode-only fields (Visual style / View discipline / Phase filter / Phase / View range sub-card / Far clip / Annotation crop / managed-fields multi-select). `displayOptions` (shadows / sketchy lines / ambient shadows) flagged as warnings — no public Revit API.

### Phase 138 — Revit VG editor parity + branch consolidation

The Phase 137 entry shipped a compact VG editor and noted the
Revit-VG-cell-style grid as deferred. That follow-up has now landed
and every outstanding feature branch has been consolidated into the
working tree.

**New UI files** under `StingTools/UI/`:

| File | Lines | Purpose |
|---|---|---|
| `RevitVgEditor.cs` | 1,194 | Full Revit VG dialog replica embedded inline in the View Style Pack form. Backed by `RevitCategoryTree.TaggableCategories` + `CategoriesWithCut`. Exposes Cut Fg/Bg / Projection Fg/Bg / Halftone / Transparency / Detail-Level cells + filter-rule rows + line-styles tab + working chevron expanders + modeless editor + dispatch surfacing. |
| `VgFillPatternDialog.cs` | 197 | Fill Pattern Graphics popup mirroring Revit's Override… cell. Resolves `FillPatternElement` ids by name with solid-fill fallback. |
| `VgLineGraphicsDialog.cs` | 157 | Line Graphics Override popup with line-pattern dropdown + colour picker + weight spinner. |
| `VgColorPicker.cs` | 120 | Windows colour picker shell with recent-colours strip and discipline-palette swatches sourced from `ColorHelper`. |
| `TitleBlockSlotLoader.cs` | 146 | Slot-dropdown helper that lazy-loads `<project>/Title Blocks/*.rfa` slot definitions. Exposes `GetSlotsForFamily(...)`. Replaces hardcoded slot index in `DrawingProductionConfigDialog`. |

Typo-prevention pass replaces free-text TextBoxes with dropdowns for fill pattern / line pattern / colour / filter-rule names; override cells render the resolved colour swatch in-grid; per-row swatches update live on edit.

**Branches consolidated**: `claude/implementation-prompt-phase-137-OrI6I` (fast-forward), `claude/merge-branches-main-HB2FF` (April-17 work — twelve content conflicts resolved with `--ours` to keep newer state), `claude/review-template-manager-JEiuF` (Phase 92 reference palettes — one trivial variable-name conflict). After consolidation `git for-each-ref` reports zero remote branches with unique commits not in HEAD. Full per-file resolution log lives in `docs/CHANGELOG.md` Phase 138 entry.

### Caveats (Drawing Template Manager)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 40 corporate drawing types + 11 style packs reference title-block / view-template / tag-family names that projects must supply; the validator reports missing assets as Warnings (not Errors) so the JSON ships usable on a stock project.
3. Crop strategy `RoomBoundary` falls back to `TightBbox` when no rooms are in the view; plan views that should be room-bounded need rooms placed first.
4. `IUpdater`-based live style propagation (automatic re-apply when a profile / pack changes in-session) is not implemented — the manual `SyncStyles` command covers the same ground on demand. Runtime cost vs. always-on drift-zero trade-off means this stays deferred.
5. `TitleBlockParamApplier` writes the first title-block instance found on a sheet. Sheets hosting multiple title blocks (unusual) only get the first one populated — the applier warns and moves on.

### Phase 166 — AEC/FM Corporate Filter Library (199 filters)

A complete corporate-baseline `ParameterFilterElement` library for the
Drawing Template Manager. Until this phase, `ViewStylePackApplier` could
*reference* filters by name but couldn't *create* them — the only filter
factory in the codebase was the hard-coded ~40-filter
`TemplateCommands.CreateFiltersCommand`. Phase 166 adds a full
JSON-defined registry with 199 filter definitions covering every
discipline an AEC/FM firm produces drawings for, plus matching
`OverrideGraphicSettings` recipes per BS 1192 / ISO 19650 / Uniclass 2015 /
BS 1710 / ASME A13.1 / GSA MEP / CIBSE-SDE / BS 9999 / BS 8300 /
BIMForum LOD.

**New files**

| Path | Role |
|---|---|
| `StingTools/Data/STING_AEC_FILTERS.json` | 199 definitions: 47 Arch · 33 HVAC · 31 Struct · 30 Fire · 27 Elec · 18 Plumb · 11 FM/COBie · 8 ISO 19650 · 8 Coord/LOD · 5 VT · 5 QA |
| `StingTools/Core/Drawing/AecFilterDefinition.cs` | POCO + rule grammar (`leaf | compound`, 14 operators, `kind=builtin/shared/phase/workset/level`) |
| `StingTools/Core/Drawing/AecFilterRegistry.cs` | Per-document loader; layers `<project>/_BIM_COORD/aec_filters.json` over corporate; `Get` / `GetByName` / `ListAll` / `ListByTag` / `Reload` |
| `StingTools/Core/Drawing/AecFilterFactory.cs` | JSON rule tree → `LogicalAnd`/`OrFilter` + `ElementParameterFilter` + `ParameterFilterElement.Create`; resolves built-in / shared / phase / workset / level params; sniffs storage type via `Definition.GetDataType()` |
| `StingTools/Commands/Drawing/AecFilterCommands.cs` | `AecFiltersCreate` (mint all into doc, idempotent) / `AecFiltersInspect` (read-only diagnostic) / `AecFiltersReload` (cache invalidate) |
| `docs/AEC_FILTER_LIBRARY.md` | Reference doc — rule grammar, override recipe, lazy-create flow, standards table, per-drawing-type filter sets |

**Wiring**

`ViewStylePackApplier.ApplyFilterRules` now lazy-creates missing filters
from the registry under the active transaction (was: warned "create it
first" and skipped). Field-by-field merge: pack `StyleFilterRule` field
wins → registry `defaultOverride` fills nulls when `inheritDefaults != false`
→ Revit default. `StyleFilterRule` extended with 11 fields covering surface
foreground/background patterns + line patterns + `detailLevel` to support
fire-rated wall washes and CIBSE-SDE pipe colour fills properly. Schema-key
drift fixed: `ViewStylePackLibrary` now accepts both `stylePacks` and
`viewStylePacks`; `ViewStylePack.filters` ↔ `filterRules`; short-form
field names (`name`/`projColor`/`projWeight`/`cutColor`/`cutWeight`)
deserialise alongside long-form. The corporate `STING_VIEW_STYLE_PACKS.json`
is now actually consumed at runtime for the first time.

**Curated filter references**

Four major packs populated with 64 filter references using
`inheritDefaults: true` — they pull the corporate-baseline override
styling without redefining it: `corp-coordination` (21 — MEP services +
clash + insulation), `corp-standard-plan` (19 — phase + fire-rated walls
/ doors + accessibility + escape), `corp-structural-plan` (20 — material
+ steel sections + foundations + bracing + rebar bands),
`corp-demolition-phase` (4 — phase rules with full corporate styling).

**Caveats**

1. Built without `dotnet build` verification (Linux sandbox). API calls
   target Revit 2025/2026/2027: `paramId.Value` (Int64) replaces
   deprecated `IntegerValue`; `Definition.GetDataType()` (ForgeTypeId)
   replaces `ParameterType`.
2. Categories that don't exist in the target Revit version are silently
   dropped with a warning; if all drop, the filter creation is skipped.
3. Shared parameters referenced by `kind: "shared"` must be bound on the
   project before the filter can be created — the factory warns + skips
   gracefully rather than failing the whole batch.

### Phase 182 — Drawing Type / Style Pack alignment audit

Closed the alignment gaps surfaced by the drawing-types/style-packs
consistency audit on branch `claude/review-drawing-types-styles-WKpW1`.

**STING_VIEW_STYLE_PACKS.json — 11 packs added** (11 → 22 total):

| Pack | Extends | Purpose |
|---|---|---|
| `corp-demolition-phase` | `corp-standard-plan` | Demolition drawings — existing halftoned, demolished bold red dashed, new construction hidden. Phase filter `Show Demo + New`. |
| `corp-healthcare-clinical` | `corp-standard-plan` | Generic clinical plan / RCP / equipment. Bedhead/pendant/scrub/anti-lig fittings + BS 8300 access in NHS-blue palette. `templateMode=managed`. |
| `corp-healthcare-mgs` | `corp-coordination` | HTM 02-01 medical gas. O2 white, N2O blue, AIR-4/7, VAC yellow, AGSS purple. Manifolds + AVSUs + terminal units. |
| `corp-healthcare-pressure` | `corp-standard-plan` | HTM 03-01 ventilation pressure cascade. Positive blue, negative red, neutral grey, isolation cubicle purple. |
| `corp-healthcare-ees` | `corp-coordination` | HTM 06-01 / NFPA 99 essential services. Type A red, Type B orange, Type C yellow, generator/UPS/IPS/ATS highlighted. |
| `corp-healthcare-fire` | `corp-standard-plan` | HTM 05-02 / BS 9999 fire compartmentation. 30/60/90/120-min walls colour-coded, smoke barriers green dashed. |
| `corp-healthcare-shielding` | `corp-standard-plan` | NCRP 147 / IPEM 75 radiation shielding. Lead-mm walls magenta, controlled/supervised zones, MRI Zone II/III/IV (5G line). |
| `corp-healthcare-ligature` | `corp-standard-plan` | Mental health anti-ligature. Compliant fittings purple, non-compliant red, observation lines from staff base. |
| `corp-healthcare-water` | `corp-coordination` | HTM 04-01 water safety / Legionella. DCW/DHW/DHWR, TMV, dead-leg risk, augmented-care outlets, temperature sensors. |
| `pres-burgund-green` | `corp-presentation-rich` | Client-facing presentation — burgundy walls + dark-green topo on cream, hand-rendered hatch. |
| `pres-interior-sage` | `corp-presentation-rich` | Interior elevation presentation — sage walls, warm-wood casework, soft ambient palette, suppressed grids/dimensions. |

All 8 healthcare packs ship with `templateMode: "managed"` and explicit
`managedFields` whitelists (scale / detailLevel / discipline / visualStyle /
phaseFilter / vgOverrides / filters — clinical also adds tagColorScheme /
defaultTagStyle) so Phase 137's `ManagedTemplateSyncer` mints + maintains
matching `STING:{packId}:{ViewType}` templates and pack-level
filterRules / vgOverrides drift gets surfaced + healed automatically.

**STING_DRAWING_TYPES.json fixes**:

- `arch-screed-buildup-A3-1to10` — `titleBlockFamily` corrected from
  `STING_TB_SHEET_A1` to `STING_TB_SHEET_A3` (paper-size / family
  mismatch).
- `elec-riser-A2-1to100` — `purpose` corrected from `Plan` to `Section`
  (slot viewType was already `Section`; purpose intent now matches).
- `clar-markup-A1` + `clar-rfi-A3` — `purpose` aligned to
  `Clarification` and explicitly bound to `corp-clarification` (was
  falling through purpose-based routing without an own pack id).
- All 22 healthcare drawing types — `titleBlockParams` populated with
  the corporate 11-cell set (`Client Name`, `Project Code`,
  `Originator`, `Company Name`, `Company Address`, `Appointing Party`,
  `Lead Appointed Party`, `Discipline=Healthcare`, `Suitability=S2`,
  `Sheet Status=WIP`, `Revision=P01`). Previously all empty, so
  healthcare sheets shipped without corporate metadata stamping.

**DrawingDriftDetector — CROP_DRIFT detection added**
(`StingTools/Core/Drawing/DrawingDriftDetector.cs`):

- New `AppendCropDrift` method fires when a profile's
  `crop.scopeBoxName` is set (kind=`ScopeBox` or `ScopeBoxOrBbox`) and
  the view's bound `VIEWER_VOLUME_OF_INTEREST_CROP` doesn't match.
- ScopeBox kind reports drift even when scope box missing from
  document (view will fail to crop); ScopeBoxOrBbox demotes to
  Suppressed (bbox fallback) when scope box absent.
- TightBbox / RoomBoundary / None are not comparable post-hoc and are
  left alone.

**Final state**: 0 missing pack references, 0 orphaned packs (every
pack now reachable via either an explicit `viewStylePackId` binding or
the `STING_VIEW_STYLE_PACKS.json` `routing[]` purpose-based fallback),
0 healthcare types with empty `titleBlockParams`. 50 of 90 drawing
types now carry an explicit pack id; the remaining 40 rely on
purpose-based routing fallback (intentional — keeps the JSON DRY).

### Caveats (Phase 182)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 11 new packs reference view templates and filter names that
   projects must supply (e.g. `STING - Healthcare Clinical`, `Fire
   Wall - 60 min`). The validator surfaces missing assets as Warnings,
   not Errors, so the JSON ships usable on a stock project.
3. Filter rule inheritance through pack `extends` chains is NOT
   cascaded — only the root `corp-base` and `corp-clarification`
   define filterRules in the corporate baseline; child packs either
   redeclare or inherit nothing. Phase 166 wires `inheritDefaults:
   true` for individual rules but full pack-chain inheritance of the
   filter list itself stays an explicit per-pack opt-in.

### Phase 183 — Closing the Phase 182 deferred items

Three gaps the Phase 182 caveats flagged are now closed.

**LiveProfileSync — registry-reload diff**
(`StingTools/Core/Drawing/LiveProfileSync.cs`):

- Per-document snapshot of SHA-256 hashes for every DrawingType +
  ViewStylePack id. `OnRegistryReloaded(doc)` (called automatically
  by `DrawingTypeRegistry.Reload` and `ViewStylePackRegistry.Reload`)
  computes the new snapshot, diffs against the prior, and stages the
  changed id set for the document.
- `GetChangedProfileIds(doc)` / `GetChangedPackIds(doc)` /
  `GetAffectedViewIds(doc)` — read-only accessors used by the
  Inspect + SyncStyles commands. Pack-change set is expanded to the
  profile ids that reference each changed pack, so editing
  `corp-coordination` flags every profile bound to it.
- `ConsumeStagedDiff(doc)` clears the staged set after SyncStyles
  has re-applied every affected view. Document close invalidates the
  cache via `LiveProfileSync.InvalidateCache(doc)` wired into
  `StingToolsApp.OnDocumentClosing`.
- `DrawingSyncStylesCommand` merges `LiveProfileSync.GetAffectedViewIds`
  into the drift-report set so an on-disk pack/profile edit
  re-applies even when the live view VG state hasn't drifted yet.
- `DrawingTypesInspectCommand` surfaces "X profile(s) + Y pack(s)
  edited since last load — Z view(s) affected" as a one-liner in the
  Inspect dialog.

**VG + filter drift detection**
(`StingTools/Core/Drawing/DrawingDriftDetector.cs`):

- New `AppendVgAndFilterDrift` compares the live view's per-category
  `OverrideGraphicSettings` (halftone / projection line weight / cut
  line weight / transparency) and per-filter attachment + visibility +
  overrides against the resolved pack's `VgOverrides` + `Filters`.
- Guarded to non-managed packs only — managed packs already get
  template-level checksum drift via `AppendManagedTemplateDrift`, and
  double-detecting would surface every view that uses the managed
  template as drifted on every pack edit.
- Output is intentionally coarse: one drift entry per category /
  filter, listing the first mismatching attribute. SyncStyles
  re-applies the pack and heals.

**Bbox-derived crop drift via stamp + diff**
(`DrawingTypeStamper.cs` + `DrawingCropApplier.cs` +
`DrawingDriftDetector.cs`):

- Two new shared parameters: `STING_CROP_KIND_TXT` (text) +
  `STING_CROP_MARGIN_MM_TXT` (text — decimal as string, no new
  storage-type handling needed). Both declared in `MR_PARAMETERS.txt`
  with stable GUIDs and mirrored in `MR_PARAMETERS.csv`.
- `DrawingTypeStamper.StampCrop(el, kind, marginMm)` /
  `ReadCrop(el)` — symmetric write + read helpers; no-op when the
  shared parameters aren't bound on the project (graceful
  degradation, no functional regression on unmigrated projects).
- `DrawingCropApplier.Apply` calls `StampCrop` after every successful
  crop write so the view carries its as-of-apply kind + margin.
- `AppendCropDrift` extended: for `TightBbox` / `RoomBoundary` /
  `ScopeBoxOrBbox` profiles, compares the stamped kind + margin to
  the profile's current values. Margin tolerance is 1 mm. Catches
  profile edits like "marginMm: 150 → 300" that the view hasn't
  been re-cropped to honour.

**Final state**: the Phase 182 caveats 1 (IUpdater for live profile
changes — closed by LiveProfileSync), 3 (bbox-derived crop drift —
closed by stamp + diff), and the VG/filter drift gap implicit in the
Phase 137 caveats are all closed. The Inspect command's headline
now reports both live-disk edits and live-view drift in one summary.

### Caveats (Phase 183)

1. Built without `dotnet build` verification (Linux sandbox).

### Phase 184 — Closing the Phase 183 caveats

The three Phase 183 caveats (in-memory-only LiveProfileSync, shared-
param dependency on crop stamps, single-mismatch VG drift reporting)
are now all closed.

**LiveProfileSync disk persistence** (`Core/Drawing/LiveProfileSync.cs`):

- Snapshot file at `<project>/_BIM_COORD/.sting_live_profile_sync.json`
  (hidden filename so it doesn't clutter pickers) carries the SHA-256
  hashes of every DrawingType + ViewStylePack id between sessions.
- On the first `OnRegistryReloaded(doc)` of a new session, the
  in-memory prior is empty, so `LoadDiskSnapshot(doc)` hydrates the
  pre-edit baseline from disk. The diff then correctly surfaces every
  on-disk edit the user made while Revit was closed.
- After every diff computation the new snapshot is written back to
  disk via `SaveDiskSnapshot`, so the chain stays unbroken across
  arbitrary numbers of sessions / edits.
- File I/O is performed outside the dictionary lock so a slow disk
  doesn't block the registry-reload pipeline.

**Crop stamp via Extensible Storage** (`Core/Storage/StingViewCropSchema.cs`):

- New ES schema `StingViewCropSchema` (`E1A7B2C4-1011-1244-8411-F6E5D4C3B2CC`)
  with three fields: `Kind` (string), `MarginMm` (double),
  `StampedUtcTicks` (long).
- `DrawingTypeStamper.StampCrop` now writes to ES as the primary
  surface; the shared parameters (`STING_CROP_KIND_TXT`,
  `STING_CROP_MARGIN_MM_TXT`) are still written as a secondary surface
  when bound so schedules / filters / Dynamo consumers keep working.
- `DrawingTypeStamper.ReadCrop` prefers ES; falls back to the shared
  params for legacy stamped views.
- Removes the `LoadSharedParams` dependency — pre-Phase-183 projects
  now get full crop-drift coverage with no migration step.

**VG drift — all mismatches per category** (`DrawingDriftDetector.AppendVgAndFilterDrift`):

- The Phase 183 implementation walked the field list with `if/else if`
  and stopped at the first mismatch, hiding the rest until SyncStyles
  re-applied. Refactored to collect every mismatch into a list and
  emit one drift entry per category / filter joining all of them with
  `; ` — e.g.
  `VG_OVERRIDE: 'Walls' halftone False vs True; projWeight 5 vs 6; transparency 0 vs 50`.
- Filter-rule drift gets the same treatment (visibility + halftone +
  projection weight + cut weight + transparency all rolled into one
  entry per filter).
- SyncStyles still heals everything in a single pack re-apply —
  Phase 184 is purely a reporting fix.

### Phase 184c — Save-As snapshot migration + transaction guard

The Phase 184 caveats covering Save-As snapshot loss and the implicit
transaction requirement on `StampCrop` are now closed.

**Save-As snapshot migration** (`Core/StingToolsApp.cs`):

- New event subscriptions: `DocumentSavingAs` (captures the
  destination path before save) and `DocumentSavedAs` (copies the
  snapshot once the save succeeds).
- `_savingAsPaths` ConcurrentDictionary keyed by document hash holds
  the `(oldPath, newPath)` pair between the two events, so concurrent
  Save As of multiple open projects can't cross-pollute.
- `MigrateLiveProfileSyncSnapshot(oldRvt, newRvt)` copies
  `<oldDir>/_BIM_COORD/.sting_live_profile_sync.json` to the new
  `_BIM_COORD/` directory beside the saved-as path. Won't clobber an
  existing snapshot in the destination (treats Save As over an
  existing STING-touched project as "destination wins").
- Cross-session profile-drift detection now keeps working after Save
  As without the user having to repeat a registry reload.

**Transaction-state guard on `StampCrop`**
(`Core/Drawing/DrawingTypeStamper.cs`):

- Early check on `el.Document.IsModifiable` — when no Revit
  transaction is active, log a warning and return `false` instead of
  letting the ES `SetEntity` / shared-param `Set` throw. Makes the
  caller contract explicit and eliminates the throw-and-catch
  overhead on the (currently-impossible) path where a caller forgets
  to wrap.
- All in-tree callers (`DrawingCropApplier.Apply` →
  `DrawingTypePresentation.Apply`) already run inside a transaction,
  so this is a defensive guard, not a behavioural change.

### Caveats (Phase 184c)

1. Built without `dotnet build` verification (Linux sandbox).
2. `DocumentSavingAs` requires the Revit API to expose
   `DocumentSavingAsEventArgs.PathName` — true for Revit 2025/2026/2027
   per the addin manifest. Older Revit versions would need a different
   pattern; STING's `net8.0-windows` target rules them out anyway.

### Phase 184d — Final alignment + completeness sweep

Post-Phase-184c audit surfaced four remaining configuration gaps;
all are now closed in `STING_DRAWING_TYPES.json`.

**Schema fix — `crop.mode` → `crop.kind` (23 entries)**

The 22 healthcare profiles (plus `plumb-drainage-schematic-A1`) declared
`"crop": { "mode": "ScopeBoxOrBbox", ... }`. The `DrawingCropStrategy`
POCO marks the JSON field as `[JsonProperty("kind")]`, so the `mode`
key was silently ignored and the deserialiser fell back to the
default value (`"ScopeBoxOrBbox"`). Result: functionally correct but
inconsistent with the schema, and any author writing `"mode":
"TightBbox"` would have been silently overridden. Renamed all 23 to
`"kind"`.

**Slot defaults on 18 view-purpose profiles**

Every healthcare drawing type (`health-eqp-pln-*`, `health-medgas-pln-*`,
`health-pressure-pln-*`, etc.) plus `health-mep-coord-A1-1to50` shipped
with `"slots": []`. View-creation pipelines (`DrawingProducer`,
`SheetManager.PlaceFromProfile`) iterate slot definitions to place
views; empty slots mean no view ever lands on the sheet. Added a
single full-bleed slot per profile (label `Main {Purpose}`, viewType
matching the profile purpose, `normX=0.03 normY=0.05 normW=0.94
normH=0.90`, `required=true`). Profiles that want multi-view layouts
(key plan inset, legend, notes) override the default in their JSON.

**titleBlockParams on 54 non-Schedule profiles**

`arch-rcp-A1-1to100`, `arch-section-A1-1to50`, `arch-elev-A1-1to100`,
`struct-plan-A1-1to100`, `mep-plan-A1-1to100`, `mep-coord-A1-1to50`,
`elec-riser-A2-1to100`, `handover-A1`, all `arch-site-A1-1to500` …
through every Plan / RCP / Section / Elevation / Coordination / 3D /
Clarification profile that lacked the corporate metadata binding. All
54 now carry the 11-cell corporate set (Client Name / Project Code /
Originator / Company Name / Company Address / Appointing Party / Lead
Appointed Party / Discipline / Suitability=S2 / Sheet Status=WIP /
Revision=P01) with the `Discipline` value mapped from the profile's
discipline code (A → Architectural, S → Structural, M → Mechanical,
E → Electrical, P/Plumbing → Plumbing, H/Healthcare → Healthcare,
MG → Medical Gas, RP → Radiation Protection, FP → Fire Protection,
LV → Comms / LV, G → Civil, `*` → Multi-Discipline). Spool / Schedule
/ Legend / Detail profiles are intentionally excluded — they have
their own metadata conventions.

**Discipline value normalised (1 entry)**

`plumb-drainage-schematic-A1` declared `"discipline": "Public Health"`
which isn't in the canonical list (A / S / M / E / P / Plumbing / FP /
LV / G / H / MG / RP / Healthcare / *). Normalised to `"Plumbing"` to
match every other plumb-* profile and the routing-table convention.

**Final tally** (programmatically verified): 0 missing `crop.kind`,
0 stray `crop.mode`, 0 empty-slot view-purpose profiles, 0 missing
`titleBlockParams` on view-purpose profiles, 0 non-canonical
discipline values, 0 missing pack references, 0 orphaned packs.

### Caveats (Phase 184d)

1. Built without `dotnet build` verification (Linux sandbox).
2. The default slot layout added to the 18 healthcare profiles is a
   single full-bleed view. Projects that want multi-view healthcare
   sheets (e.g. RDS-style "plan + elev + equipment list + signatures"
   panels) need to override the slot array via project-scoped
   `_BIM_COORD/drawing_types.json`. The shipped layout is correct
   for the dominant "one plan per sheet" use case.
3. The 4 healthcare A2 drawing types (`health-rds-A2`,
   `health-mortuary-pln-A2-1to50`, `health-bedhead-elev-A2-1to20`,
   `health-or-ceiling-A2-1to20`) use `STING - Healthcare Title Block`
   which is a non-size-specific name. The family is assumed to ship
   in both A1 and A2 flavours; verify before merge if not.

### Phase 184e — 100% field-level completeness sweep

Audit pass surfaced six secondary fields with low population (e.g.
`description` at 14%, `viewportTypeName` at 28%, `sectionMarker` on
only 8/90 profiles). Each gap traced to omitted defaults rather than
broken schema. Every drawing type in `STING_DRAWING_TYPES.json` now
has all 19 audit fields populated.

**Fields synthesised/defaulted across the 90 profiles**:

| Field | Filled | Default applied |
|---|---|---|
| `description` | 77 | `"{Discipline} {purpose} on {paperSize} at 1:{scale}"` |
| `viewportTypeName` | 65 | `"STING - Standard Viewport"` |
| `isoNaming` | 59 | `{ volume:"01", type:"DR", role:<by discipline>, suitability:"S2", revision:"P01" }` |
| `phase` | 50 | `"*"` (wildcard, per POCO default) |
| `orientation` | 43 | `"Landscape"` (Portrait for Schedule purposes + A4) |
| `viewTemplateName` | 36 | `"STING - {Discipline} {purpose}"` |
| `titleBlockParams` | 11 | Corporate 11-cell set on remaining Detail/Spool/Schedule/Legend profiles |
| `slots` | 4 | Single full-bleed slot for Schedule / Schematic profiles |
| `scale` | 3 | `"NA"` sentinel for 3D presentation views |
| `detailLevel` | 3 | `"Medium"` for Schedule / Legend |
| `sectionMarker` | 2 | `{ family:"STING_ELEV_MARK"\|"STING_SECTION_MARK", markPrefix:"E"\|"S", bubbleStyle:"Filled", farClipMm:3000 }` for the 2 Section/Elevation profiles that were missing markers (`elec-riser-A2-1to100`, `health-bedhead-elev-A2-1to20`) |
| `annotation` | 1 | Empty rules + tagFamilies (`plumb-drainage-schematic-A1`) |

**Final tally (programmatically verified)**:

| Check | 90 / 90 |
|---|---|
| id / name / origin / purpose | ✓ 100% |
| description | ✓ 100% |
| discipline / phase / paperSize / orientation | ✓ 100% |
| titleBlockFamily / scale / detailLevel | ✓ 100% |
| viewTemplateName / viewportTypeName | ✓ 100% |
| sheetNumberPattern / sheetNamePattern | ✓ 100% |
| crop.kind / slots / annotation / titleBlockParams | ✓ 100% |
| isoNaming | ✓ 100% |
| sectionMarker (where required) | ✓ 100% |

And the 22 packs:

| Check | 22 / 22 |
|---|---|
| id / name / description / extends / origin | ✓ 100% |
| vgOverrides | ✓ 100% |
| managed packs declare vgOverrides + filters | ✓ 100% |
| pack references resolve | ✓ 100% |
| no orphaned packs | ✓ 100% |

### Caveats (Phase 184e)

1. Built without `dotnet build` verification (Linux sandbox).
2. The defaults are corporate baseline values; project teams will
   override `viewTemplateName` (to match their actual `.rvt` template
   names), `viewportTypeName` (to match their loaded viewport
   types), and `isoNaming.volume` / `revision` per project. The
   validator surfaces missing assets as Warnings (not Errors) so the
   JSON ships usable on a stock project.

### Phase 184f — Filter refs resolve + healthcare TB naming aligned

Closed the last two deployment caveats from Phase 184d/e.

**70/70 pack filter references now resolve** (`STING_AEC_FILTERS.json`):

The 8 healthcare packs + `corp-base` + `corp-demolition-phase` + 
`corp-clarification` referenced 75 filter names by pretty-name 
convention (`MGPS - Oxygen (O2)`, `Fire Wall - 60 min`, 
`Anti-Ligature - Compliant`). The AEC filter library uses the 
`STING - <Domain>: <Type>` naming convention. Two changes:

- **54 references remapped** to existing library entries (e.g.
  `MGPS - Oxygen (O2)` → `STING - MGS: O2`, `Fire Wall - 60 min` →
  `STING - Arch: Fire 60 min Walls`, `Pressure - Negative (-5 Pa)` →
  `STING - Clin: Pressure Negative`, `EES - Type A (Life Safety)` →
  `STING - EES: Life Safety`).
- **24 new filter definitions added** to `STING_AEC_FILTERS.json`
  (filter count 265 → 289) covering: Out of Scope, RFI Query, Design
  Intent, Bedhead/Pendant/Scrub/Accessible Routes, Room Department ×
  3, MGPS Manifold/Terminal Unit/Entonox, EES Generator/ATS,
  Shielding Lead 1mm/Borated Poly, Water Dead Leg/Temp
  Sensor/Calorifier, Anti-Bind Door, Observation Direct/Indirect/None.
  Each new filter keys off documented healthcare shared parameters
  (`MGS_GAS_TYPE_TXT`, `LIG_AREA_OBS_LOS_TXT`, `RAD_LEAD_MM_NR`,
  `CLN_ROOM_CLASS_TXT`, etc.) with sensible categories + inline
  override defaults that the pack's per-rule overrides layer on top
  of (Phase 166 `inheritDefaults` pattern).

The pack inline-override styling is preserved verbatim — only the
filter-name LOOKUP key changed. Visual identity is intact; the
`ViewStylePackApplier` now finds a real `ParameterFilterElement`
to attach to the view (via `AecFilterRegistry.LazyCreate`) instead
of warning "filter not in document".

**Healthcare title block naming standardised** (`STING_DRAWING_TYPES.json`):

All 22 healthcare drawing types now use size-suffixed title-block
family names matching the corporate convention:

- A1 profiles (18) → `"STING - Healthcare Title Block A1"`
- A2 profiles (4)  → `"STING - Healthcare Title Block A2"`

Previously all 22 referenced the non-size-specific
`"STING - Healthcare Title Block"`, which forced the
`TitleBlockRouter` to fall back to "first available" matching on a
stock project. The split mirrors the `STING_TB_SHEET_A1` /
`STING_TB_SHEET_A2` corporate baseline; projects with a single
multi-size healthcare family can override via project-scoped
`drawing_types.json`.

**Final integrity** (programmatically verified):

| Check | Result |
|---|---|
| Drawing types | 90 |
| Routing rules | 101 |
| Style packs | 22 |
| AEC filters | 289 |
| Pack filter refs resolved | ✓ 70/70 |
| Healthcare TB ↔ paper size aligned | ✓ |
| All other Phase 184e checks (17 fields) | ✓ 90/90 |

### Caveats (Phase 184f)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 24 new filter definitions use sensible category + rule
   defaults but assume the documented healthcare shared parameters
   (`MGS_GAS_TYPE_TXT`, `LIG_AREA_OBS_LOS_TXT`, `RAD_LEAD_MM_NR`,
   `CLN_ROOM_CLASS_TXT`, `MGS_TU_BS5682_BOOL`, `RAD_BARRIER_TYPE_TXT`,
   `PLM_DEAD_LEG_BOOL`, `LIG_PRODUCT_RATING_TXT`) are bound on the
   project. The `AecFilterFactory` warns + skips gracefully when a
   referenced shared param isn't bound, so the JSON ships usable on
   a stock project — projects bind the params via `LoadSharedParams`
   on first use.
3. Some pack→library remappings collapse fine-grained pack categories
   to coarser library entries (e.g. `Pressure - Positive (+15 Pa)` and
   `Pressure - Positive (+5 Pa)` both map to `STING - Clin: Pressure
   Positive`; `Fire Door` maps to `STING - Arch: FD60 Doors`).
   Pack-side `transparency` / `projColor` overrides still differentiate
   the visual outcome on the view; projects that need separate filters
   per band can fork the library entry.

### Phase 184g — A2 paper-size consolidation to A3

The A2 paper size is no longer part of STING's corporate baseline.
All 10 drawing types that previously used A2 have been migrated to
A3 with their IDs, names, descriptions, title-block families, and
routing references updated in lockstep.

**Drawing-type ID renames** (10 total):

| Before | After |
|---|---|
| `elec-riser-A2-1to100` | `elec-riser-A3-1to100` |
| `door-schedule-A2` | `door-schedule-A3` |
| `legend-A2` | `legend-A3` |
| `arch-window-schedule-A2` | `arch-window-schedule-A3` |
| `health-rds-A2` | `health-rds-A3` |
| `health-mortuary-pln-A2-1to50` | `health-mortuary-pln-A3-1to50` |
| `health-bedhead-elev-A2-1to20` | `health-bedhead-elev-A3-1to20` |
| `health-or-ceiling-A2-1to20` | `health-or-ceiling-A3-1to20` |
| `plumb-vent-riser-A2-NTS` | `plumb-vent-riser-A3-NTS` |
| `plumb-pressure-schedule-A2` | `plumb-pressure-schedule-A3` |

**Field updates per profile** (across all 10):

- `paperSize`: `"A2"` → `"A3"`
- `titleBlockFamily`: `STING_TB_SHEET_A2` → `STING_TB_SHEET_A3` (6 corporate); `STING - Healthcare Title Block A2` → `STING - Healthcare Title Block A3` (4 healthcare)
- `name` / `description` / `sheetNamePattern` text occurrences of "A2" rewritten to "A3"

**Cross-reference updates**:

- `STING_DRAWING_TYPES.json`: 15 routing rules referencing the renamed IDs updated
- `HEALTHCARE_PACK_PROFILES.json`: 3 healthcare-pack profile references to `health-rds-A2` updated to `health-rds-A3`
- `StingTools/Core/Drawing/DrawingTypeRegistry.cs`: 3 fallback built-in entries (`elec-riser`, `door-schedule`, `legend`) + 3 fallback routing rules updated
- `StingTools/Commands/SLD/SLDRiserDiagramCommand.cs`: `RiserDrawingTypeId` constant updated

**Final paper-size distribution** (programmatically verified):

| Size | Count |
|---|---|
| A1 | 76 |
| A3 | 14 |
| **A2** | **0** ✓ |

A2 remains a valid Revit paper-size string elsewhere in the codebase
(sheet-size dictionaries, paper-size dropdowns in editors, CDE
acceptance codes like `A2 — Approved with Comments`) — only the
drawing-type corporate baseline has dropped it. Projects that need
A2 profiles can override via project-scoped `drawing_types.json`.

### Caveats (Phase 184g)

1. Built without `dotnet build` verification (Linux sandbox).

### Phase 184h — A3 scale rebalancing

A3 has roughly half the printable area of A2, so the 4 model-view
profiles migrated in Phase 184g had their scales halved (denominator
doubled) to keep their content fitting on the smaller sheet. IDs and
names updated in lockstep so the convention "id encodes scale" stays
true.

| Phase 184g (A2 scale on A3 paper) | Phase 184h (A3 scale on A3 paper) |
|---|---|
| `elec-riser-A3-1to100` | `elec-riser-A3-1to200` |
| `health-mortuary-pln-A3-1to50` | `health-mortuary-pln-A3-1to100` |
| `health-bedhead-elev-A3-1to20` | `health-bedhead-elev-A3-1to50` |
| `health-or-ceiling-A3-1to20` | `health-or-ceiling-A3-1to50` |

Profiles unchanged: schedules (`door-schedule-A3`,
`arch-window-schedule-A3`, `plumb-pressure-schedule-A3`,
`health-rds-A3`), legends (`legend-A3`), schematics
(`plumb-vent-riser-A3-NTS`) — none rely on a model-view scale.

**Per-profile field updates** (4 profiles):

- `id`: scale suffix renamed (e.g. `-1to20` → `-1to50`)
- `scale`: numeric value doubled (e.g. 20 → 50)
- `name` / `description`: scale text rewritten (e.g. "A3 @ 1:100" → "A3 @ 1:200")

**Cross-reference updates**:

- `STING_DRAWING_TYPES.json`: 8 routing rules pointing at the renamed IDs updated
- `DrawingTypeRegistry.cs`: built-in `elec-riser` fallback entry +
  2 routing rules updated; name and scale corrected to "1:200" / 200
- `SLDRiserDiagramCommand.cs`: `RiserDrawingTypeId` constant updated
  to `elec-riser-A3-1to200`

**Final A3 scale distribution** (14 A3 profiles):

| Scale | Count | Profiles |
|---|---|---|
| 1:10  | 1 | screed build-up detail |
| 1:20  | 2 | arch detail, struct rebar detail |
| 1:50  | 4 | RFI, bedhead, OR ceiling, struct rebar |
| 1:100 | 4 | door / window schedule, legend, mortuary plan |
| 1:200 | 1 | elec riser |
| NA / NTS | 2 | RDS, vent riser |

### Caveats (Phase 184h)

1. Built without `dotnet build` verification (Linux sandbox).

### Phase 184i — discipline-code normalisation (`Plumbing` → `P`)

Post-184h audit caught one real string-equality bug. The
`DrawingDispatcher.MatchesWildcard` matches discipline values via
`string.Equals(..., OrdinalIgnoreCase)`, so `"P"` and `"Plumbing"`
do NOT match. After Phase 184d's `"Public Health"` → `"Plumbing"`
fix and Phase 184e's routing `"P"` → `"Plumbing"` sweep, every
plumb-* routing rule used the long form `"Plumbing"` while every
plumb-* drawing-type declared the short form `"P"` (matching the
A / S / M / E / H / MG / RP convention). The result: 14 plumb
routing rules silently couldn't resolve any plumb drawing type.

Normalised everything to the short code `"P"`:
- 14 routing rules: `"Plumbing"` → `"P"`
- 1 drawing type (`plumb-drainage-schematic-A1`): `"Plumbing"` → `"P"`

`titleBlockParams.Discipline` cell still reads `"Plumbing"` —
that's the human-readable display value on the sheet, not a
routing key, so it stays the long form.

**Omnibus alignment check (programmatically verified, 8/8 pass)**:

| Check | Result |
|---|---|
| Discipline values DT ↔ routing | ✓ 0 orphans |
| Routing target ids resolve | ✓ 101/101 |
| Pack references resolve | ✓ 22/22 |
| Pack filter references resolve | ✓ 70/70 |
| A2 paper-size residue | ✓ 0 |
| `tokenProfile` as string | ✓ 0 |
| Scale ↔ ID encoding match | ✓ 90/90 |
| Paper size ↔ title-block family match | ✓ 90/90 |

Final counts: **90 drawing types · 22 style packs · 101 routing
rules · 289 AEC filters · 70 pack filter references — all
references resolve**.

### Caveats (Phase 184i)

1. Built without `dotnet build` verification (Linux sandbox).

### Phase 184j — `isoNaming` shape + role normalisation

Deep-probe audit caught two more real bugs:

**`isoNaming: true` (boolean) → object on 22 healthcare profiles**

The 22 healthcare profiles shipped from the original healthcare pack
author carried `"isoNaming": true` — a bool that originally meant
"use ISO 19650 naming yes/no". But `DrawingType.IsoNaming` is typed
as the `IsoNaming` POCO class (`Volume` / `Type` / `Role` /
`Suitability` / `Revision` strings), so Newtonsoft silently set the
field to null on deserialisation. Phase 184e's synthesis-defaults
pass also skipped them because `not dt.get('isoNaming')` evaluates
False against truthy `True`.

Converted all 22 to proper objects with discipline-aware role:
`{ volume:"01", type:"DR", role:<H/MG/RP from discipline>,
suitability:"S2", revision:"P01" }`.

**`isoNaming.role` ↔ discipline alignment**

`pres-3d-axon-A1` had `discipline:"*"` but `role:"A"`. For wildcard
discipline, the canonical ISO 19650-2 role is `"Z"` (multi /
undefined). Normalised across every profile so `role` always
matches the discipline-code map (A/S/M/E/P/H/MG/RP/Z).

**Final omnibus (programmatically verified, 11/11 pass)**:

| Check | Result |
|---|---|
| Discipline DT ↔ routing orphans | ✓ |
| Routing target ids resolve | ✓ 101/101 |
| Pack references resolve | ✓ 22/22 |
| Pack filter references resolve | ✓ 70/70 |
| A2 paper-size residue | ✓ 0 |
| `tokenProfile` as string | ✓ 0 |
| `isoNaming` shape (dict only) | ✓ 90/90 |
| `isoNaming.role` ↔ discipline | ✓ 90/90 |
| Scale ↔ ID encoding | ✓ 90/90 |
| Paper size ↔ TB family | ✓ 90/90 |
| Slot bbox geometric sanity | ✓ |

### Caveats (Phase 184j)

1. Built without `dotnet build` verification (Linux sandbox).
### Phase 186 — Bonsai integration foundation (multi-host substrate)

**Status**: Landed on `claude/stingtools-bim-research-8Kkwv` across
six commits. Substrate is verified drift-free; Day-1 Bonsai scaffold
+ Planscape server endpoint are unit-verified but not end-to-end
tested. See [`docs/PHASE_186_BONSAI_INTEGRATION.md`](docs/PHASE_186_BONSAI_INTEGRATION.md)
for the full architectural narrative + decisions log + verification
matrix + forward roadmap.

This phase turns STING from a Revit-only plugin into the data-layer
spine of a multi-host BIM coordination platform. The IFC substrate
becomes the contract every host plugin reads from; the Planscape
Server federates across hosts via cross-host element-identity
mapping.

**Net-new top-level folders**:
- `shared/ifc/` — IFC substrate: 52 enums, 5 psets, 5 IDS files,
  bSDD publication plan. SHA-256 corporate locks; project-overlay
  resolver for the 3 project-scoped enums (`StingLocationCodes`,
  `StingZoneCodes`, `StingLevelCodes`).
- `stingtools-core/python/` — Python package re-packaging the
  substrate as a programmatic API (`EnumRegistry`, `PsetRegistry`,
  `TagGrammar`, `SpatialChecker`, `PlanscapeClient`, `AuditLog`,
  `IdsRunner`). 7/7 smoke tests pass against the live `shared/ifc/`.
  Eventually paired with a `dotnet/` half for Revit/Tekla consumption.
- `stingtools-bonsai/` — Bonsai (formerly BlenderBIM) extension.
  Day-1 scaffold: blender_manifest.toml (Blender 4.2+ extension
  schema 1.0.0), `BonsaiBridge` coexistence layer (`core/bonsai.py`),
  3 diagnostic operators (`sting.about`, `sting.reload_substrate`,
  `sting.bonsai_probe`), `STING_PT_main` N-panel. MVP operators
  (16 commands) deferred to Path B of the recommendation —
  estimated 8 weeks single-dev.
- `tools/enums/` — `compute_checksums.py` (drift detection +
  manifest generator), `audit_bsdd.py` (publication-plan summary
  check).
- `tools/converters/` — `sting_to_psd.py` (STING XML → buildingSMART
  PropertySetDef format), `sting_to_revit_params.py` (STING psets →
  Revit shared-parameter file fragment with deterministic UUID v5
  GUIDs).
- `tools/tests/round_trip.py` — IDS + Pset round-trip test harness
  scaffold. `--generate-fixture` documents the ifcopenshell.api
  call sequence; implementation deferred.
- `.github/workflows/ifc-substrate.yml` — CI: checksum drift +
  bSDD audit + XSD validation + IDS well-formedness + Pset reference
  integrity + IfdGuid uniqueness on every PR.

**Net-new server entities + endpoints**:
- `Planscape.Server/src/Planscape.Core/Entities/ExternalElementMapping.cs`
  — cross-host element identity table. Composite-unique on
  `(ProjectId, IfcGlobalId, Host, HostDocumentGuid)`.
- `Planscape.Server/src/Planscape.Core/DTOs/IfcIngestDtos.cs` —
  `IfcIngestRequest` + `IfcElementDto` + `IfcIngestResponse`.
- `Planscape.Server/src/Planscape.API/Controllers/IfcController.cs`:
    - `POST /api/projects/{projectId}/ifc/data` — host-agnostic IFC
      element ingest. Upserts mappings + TaggedElement projection in
      500-element batches with stale-write protection.
    - `GET /api/projects/{projectId}/ifc/mappings?ifc_guid=...` —
      cross-host lookup (issue raised in Bonsai on IFC GUID X →
      Revit ElementId).
- `PlanscapeDbContext`: `+ DbSet<ExternalElementMapping>` with 3
  indexes; `Entity<TaggedElement>` unique constraints converted to
  filtered uniques to support both Revit (`RevitElementId > 0`) and
  non-Revit (`UniqueId <> ''`) ingest paths.

**Substrate inventory at Phase 186 close** (programmatically verified):

| Layer | Count | Detail |
|---|---|---|
| Enum XMLs | 52 | 49 corporate-locked + 3 project-template |
| Pset XMLs | 5 | `Pset_StingTags` (12 props, 9 rules), `Pset_StingSpatialCodes` (6 props, 5 rules), `Pset_StingTag7` (10 props, 3 rules), `Pset_StingDrawing` (12 props, 3 rules), `Pset_StingProjectOrg` (13 props, 3 rules) |
| SpatialChecker static rules | 12 | LOC/LVL/ZONE/SYS/SEQ/FullTag (Tier-1) + DISC_NOT_EMPTY + DRAWING_TYPE_RESOLVABLE + 2 PROJECTORG_* + BUILDING_LOC_UNIQUE + STOREY_LVL_UNIQUE_WITHIN_BUILDING (Phase 186b) — 100% of declared statically-enforceable rules |
| SpatialChecker behavioural rules | 8 | `enforced-by="host"` — TOKEN_LOCK / TAG_HISTORY / PROJECTORG_SINGLETON / CROP_KIND_MATCHES_PROFILE / PACK_CHECKSUM_MATCHES / TAG7_NARRATIVE_CONSISTENT / TAG7_PARAGRAPH_STATE_EXCLUSIVE / TAG7_TECHNICAL_SPECS_BY_DISCIPLINE |
| IDS files | 5 | `sting-tag-grammar.ids` (11 specs), `sting-spatial-codes.ids` (8 specs), `sting-drawing.ids` (6 specs), `sting-tag7.ids` (7 specs), `sting-project-org.ids` (6 specs) — 38 specs total across all 5 psets |
| Project-overlay examples | 3 | LOC / LVL / ZONE worked examples |
| bSDD publication entries | 52 | 24 ready · 1 draft · 6 external_already · 2 skip_external · 16 private · 3 project_scoped |
| Python core modules | 13 | enums + psets + tag_grammar + spatial + ids + planscape |
| Bonsai add-on Python files | 9 | manifest + bl_info + core/bonsai + 3 ops + 1 panel + 2 __init__ |
| Server entities (new) | 1 | `ExternalElementMapping` |
| Server controllers (new) | 1 | `IfcController` (2 endpoints) |
| Tooling scripts (new) | 5 | checksums, bsdd_audit, sting_to_psd, sting_to_revit_params, round_trip |
| CI workflows (new) | 1 | `ifc-substrate.yml` (6 validation steps) |
| Top-level READMEs (new) | 7 | enums, psets, ids, bsdd, examples, bonsai, core |

**5 enum tiers** covering: tag grammar (DISC/SYS/FUNC/PROD + spatial
+ status/suitability/CDE/revision); drawing engine (purpose/tier/
paper/orientation/detail/colour/crop); workflow (issue/RIBA/workflow/
signoff/maintenance/asset); engineering (HVAC pressure/sizing/density
+ acoustic NC + pipe services/materials + duct + fire + cable + steel
+ concrete + insulation + hangers + welds); healthcare pack (facility
profiles + MGS gases + pressure regimes + EES + MRI + radiation +
ligature + observation + HTM water + HBN departments + theatres).

### Caveats (Phase 186)

1. **Path-A verification not run** in dev sandbox. The C# IfcController
   compiles in theory (follows existing controller conventions) but
   has never seen `dotnet build`. The Bonsai add-on syntax-checks
   clean but has never been loaded in actual Blender. The 2 IDS
   files parse as well-formed XML but have never been run through
   `ifctester`. Five working days of local verification (see
   `docs/PHASE_186_BONSAI_INTEGRATION.md § Forward roadmap → Path A`)
   flip every ❌ in the verification matrix to ✅.
2. **EF migration not generated.** `dotnet ef migrations add
   IfcIngestSubstrate` against `Planscape.Server` is the next
   deployment step. Schema diff: 1 new table + 2 new filtered
   uniques on `TaggedElements`.
3. **Round-trip test harness is a scaffold.** `tools/tests/round_trip.py
   --generate-fixture` documents the ifcopenshell.api call sequence
   but doesn't yet mint a real IFC. Real fixture generation is
   estimated 1 day once ifcopenshell is installed locally.
4. **bSDD entries all carry `proposed: true`.** No actual publication
   to bSDD has happened. The 22 "ready" entries carry proposed IRIs
   that DO NOT resolve in bSDD until status flips to `posted` /
   `verified` via `tools/bsdd/publish.py` (also future).
5. **MVP operators not built.** Day-1 ships diagnostic ops only
   (`sting.about`, `sting.reload_substrate`, `sting.bonsai_probe`).
   The 16 production operators from the MVP scope are estimated
   8 weeks single-dev. Scope doc lives in commit history of this
   branch.
6. **Healthcare Pset bundle not yet authored.** The 5 healthcare
   Psets (`Pset_StingHealthcareClinical/MGS/Radiation/
   ClinicalEquipment/Ligature`) referenced in the bSDD plan are
   Phase 186 work. Healthcare enumerations (Tier 5) shipped this
   phase; the consuming Psets did not.
7. **ArchiCAD + Tekla plugins are forward roadmap.** The
   substrate is host-agnostic so the work is incremental, but
   neither plugin folder exists yet. Phase 187 (ArchiCAD, ~12 weeks)
   and Phase 188 (Tekla server-side connector, ~2 weeks) per the
   architecture doc.

## Template Engine v1.1 (Phase 112)

**Status**: S01–S18 landed on `claude/implement-template-engine-COd9n`
(commits `e92a504f` + `a37c4c61`). Everything from the
`20260423_planscape_template_engine_runner_v1.1.pdf` runner is
implemented against the nested repo layout — the runner assumed a flat
root, but `.cs` files live under `StingTools/Docs/{Templates,Workflow,Search}/`.
S19 (signature provider) and S20 (AI metadata extraction) are design-
complete and deferred to v1.2 per the runner.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Docs/Templates/` | MiniWord + ClosedXML render pipeline (14 files) |
| `StingTools/Docs/Workflow/` | WorkflowEngine + AuditLog + DistributionGroups (6 files) |
| `StingTools/Docs/Search/` | Lucene.NET document index + saved searches (2 files) |
| `StingTools/Docs/_template_sources/` | 16 embedded `.docx` / `.xlsx` templates (EmbeddedResource) |
| `StingTools/Docs/_workflow_sources/` | 5 embedded workflow JSONs (EmbeddedResource) |

### New namespaces

`Planscape.Docs.Templates`, `Planscape.Docs.Workflow`,
`Planscape.Docs.Search`. The plugin assembly remains `StingTools` — these
namespaces coexist with the existing `StingTools.*` tree.

### New parameters (13)

All `PRJ_ORG_*` scoped to `ProjectInformation`, UUIDv5 in Planscape docs
namespace `a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a`. Constants on
`ParamRegistry`: `ORG_PROJECT_CODE`, `ORG_ORIGINATOR_CODE`,
`ORG_COMPANY_NAME`, `ORG_COMPANY_ADDRESS`, `ORG_CLIENT_NAME`,
`ORG_APPOINTING_PARTY`, `ORG_LEAD_APPOINTED_PARTY`, `ORG_PARTICIPANTS`,
`ORG_PHASE`, `ORG_CLASS`, `ORG_WORKFLOW_PROFILE`,
`ORG_SIGNATURE_PROVIDER`, `ORG_AI_EXTRACT_ENABLED`. Exposed as
`AllOrganisationParams[]` + `OrganisationDefaults{}` (seeds `"PLNS"` /
`"Planscape Limited"` / `"Kampala, Uganda"` / phase `DE` / class `2` /
workflow profile `default`).

### New commands (8)

Registered in the BIM tab of the dock panel and in `StingCommandHandler`:

| Tag | Class | Transaction | Description |
|---|---|---|---|
| `IssueDeliverable` | `Planscape.Docs.Templates.IssueDeliverableCommand` | Manual | Render A01, write revision history, start `deliverable_issue_default` workflow, audit |
| `ReIssueDeliverable` | `ReIssueDeliverableCommand` | Manual | Bump revision + re-issue |
| `PublishDeliverable` | `PublishDeliverableCommand` | Manual | Promote to S4 / `PUBLISHED` CDE container |
| `CancelDeliverable` | `CancelDeliverableCommand` | Manual | Render A02 cancellation notice, archive |
| `SupersedeDeliverable` | `SupersedeDeliverableCommand` | Manual | Mint new number, render A03 |
| `ReplaceDeliverable` | `ReplaceDeliverableCommand` | Manual | Render A04 replacing notice, cross-link |
| `CreateTransmittalOrchestrated` | `CreateTransmittalOrchestratedCommand` | Manual | Full pipeline: mint id → render B06 → start `transmittal_default` workflow → audit |
| `BulkIssueDeliverables` | `BulkIssueDeliverablesCommand` | Manual | Issue all currently-selected deliverables in one `TransactionGroup` |

Existing `DocumentManagementDialog.QuickTransmittal` and
`BIMManagerCommands.CreateTransmittalCommand` also delegate to
`TransmittalOrchestrator.Create` after their classic JSON write — both
entry points produce rendered docx + workflow instance + audit entry,
while keeping their existing UI rows (`delivery_tracking`,
`recipient_count`, `status_history`) unchanged for backwards compat.

### Runtime artefacts (per project)

Written under `<project>/_BIM_COORD/`:

| File | Purpose |
|---|---|
| `templates/manifest.json` | Seeded from `ProjectInformation` + `PRJ_ORG_*` on first open |
| `templates/*.docx` / `*.xlsx` | 16 default templates extracted from embedded resources |
| `workflows/*.json` | 5 default workflow definitions extracted from embedded resources |
| `generated/YYYYMMDD_{doc_number}_{template_id}.{ext}` | Rendered output |
| `doc_sequences.json` | Atomic counter store per `(type\|role\|fb\|sb)` key |
| `deliverables.json` | Lifecycle state + revision history per deliverable |
| `transmittals.json` | Enriched with `template_id`, `rendered_file_path`, `workflow_instance_id` |
| `workflow_state.json` | `WorkflowInstance` rows per open workflow |
| `audit_log_{yyyy}_{MM}.jsonl` | Append-only SHA-256 tamper-evidence chain |
| `distribution_groups.json` | Type/role/suitability-scored recipient groups |
| `saved_searches.json` | Per-user saved document search queries |
| `search_index/` | Lucene FSDirectory index over register + deliverables |

### Extraction lifecycle

`StingToolsApp.OnDocumentOpened` calls
`EmbeddedTemplates.ExtractIfMissing(doc)` on first open per project.
This streams the 16 templates + 5 workflow JSONs onto disk and writes
a default `manifest.json` seeded from `ProjectInformation` and
`PRJ_ORG_*`. Idempotent — re-opens are no-ops.

### Caveats (template engine)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 16 `.docx` / `.xlsx` templates ship as professional-quality stubs
   — proper Word tables, banded header, footer `PAGE`/`NUMPAGES` fields,
   loop tables, signature blocks — but designers can still expand
   bespoke layouts in Word without breaking the `{{token}}` contract.
3. S19 (signature provider) and S20 (AI extraction) are deferred to v1.2
   per the runner PDF. `PRJ_ORG_SIGNATURE_PROVIDER_TXT` and
   `PRJ_ORG_AI_EXTRACT_ENABLED_BOOL` are already defined so enabling
   them is additive only.

## Healthcare Pack (H-1..H-30)

**Status**: full pack landed on branch `claude/research-hospital-design-0Uxbi` across ~22 commits. Built clean on Windows with 0 warnings. See [`docs/HEALTHCARE_PACK_DESIGN.md`](docs/HEALTHCARE_PACK_DESIGN.md) for the full spec; [`docs/CHANGELOG.md`](docs/CHANGELOG.md) for the per-phase summary.

### What it adds at a glance

- **5 new shared-parameter groups** (28 `CLN_CLINICAL`, 29 `MGS_SYSTEMS`, 30 `RAD_PROTECTION`, 31 `CEQ_CLINICAL`, 32 `LIG_BEHAVIOURAL`) plus extensions to existing groups 4 / 5 / 6 / 8 / 13 / 25; ~100 net-new shared parameters in `MR_PARAMETERS.txt`.
- **3 new disciplines** in the tag taxonomy (`H` Healthcare, `MG` Medical Gas, `RP` Radiation Protection); ~30 healthcare PROD codes; 60 tag families in `STING_TAG_CONFIG_v5_0_HEALTH.csv`.
- **16 healthcare validators** under `Core/Validation/Healthcare/` (Pressure / MGPS / EES / Water / Radiation / Adjacency / Anti-Ligature / RDS / IoT-staleness / Structural / Acoustic / Advanced-Rad / Endoscope / EES-Resilience / RTLS / Waste) gated through `HealthcareValidatorGate` against `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` (FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH).
- **7 standards modules** under `StingTools.Standards/{HTM, HBN, FGI, NFPA99, NCRP147, ASHRAE170, USP797800}` — stateless lookup tables + checklist generators + an NCRP 147 W·U·T → mm-Pb calculator (Archer α/β/γ digitised for 70/100/125/150/200 kVp lead).
- **22 corporate Drawing Types** (RDS / EQP / MGPS / pressure / EES riser / IPS / decon / mortuary / fire / radiation / MRI / ligature / bedhead / OR-RCP / water-safety / acoustic / structural / RTLS / waste / nuclear-medicine) with routing rules; 8 ViewStylePacks; 58 healthcare filters in `STING_AEC_FILTERS.json`.
- **MGPS package** (`Core/MedGas/`) — `MgasNetwork` graph builder, `MgasFlowSolver` (NFPA 99 §5.1.13 diversity), `MgasVerificationLog` (persists 12-step NFPA 99 §5.1.12 records to `_BIM_COORD/healthcare/mgas_verifications/`).
- **RDS engine** (`Docs/Templates/Rds*`) — token-context builder + MiniWord renderer; one .docx per clinical room; mandatory parameter set enforced by `RdsCompletenessValidator`.
- **40+ commands** under `Commands/Healthcare/`, `Commands/MedGas/`, `Commands/Adjacency/`, `Commands/Twin/`, `Commands/Radiation/` — 14 thin validator wrappers plus 9 specialist audits (anti-lig / hybrid OR / USP / behavioural / mortuary / maternity / HSDU / dialysis / HBO).
- **8 workflow JSON presets** (`WORKFLOW_HealthcareCommissioning`, `MgasVerification`, `PressureRegimeAudit`, `RdsIssue`, `HTM-04-01-Annual`, `AntiLigatureAudit`, `NFPA110-GeneratorTest`, `HTM-01-06-EndoReprocess`) — auto-discovered by `WorkflowEngine.AppendUserPresets`.
- **COBie healthcare overlay** — 50 clinical equipment types, 16 systems, 70 picklist values, 35 PPM templates, 12 doc types, 26 spare-part templates added to the existing `COBIE_*.csv` files; existing `HEALTHCARE_NHS` and `HEALTHCARE_PRIVATE` presets updated with the correct Speciality Equipment focus category.
- **Mobile commissioning app** — new `Planscape/app/healthcare/` tab with 6 screens (overview / mgas-checklist / pressure-live / water-flush / anti-ligature-audit / rds-viewer) calling typed wrappers in `Planscape/src/api/endpoints.ts`.
- **Server APIs** — `Planscape.Server/src/Planscape.API/Controllers/HealthcareController.cs` + 4 entities (`HealthcarePressureLog`, `HealthcareMgasVerification`, `HealthcareAntiLigatureAudit`, `HealthcareRdsSnapshot`) all `ITenantScoped`. Run `dotnet ef migrations add HealthcarePack` against `Planscape.Server` once before deploying.
- **NLP processor** — 19 healthcare patterns added to `Tags/NLPCommandProcessor.cs` so users can type "run pressure audit" / "mgps verify" / "rds completeness" etc.
- **Integration with the unified `RunAllValidatorsCommand`** — the v4 validator chain now runs `RunAllHealthcareValidators` after the 8 v4 validators (gated on facility-type so non-healthcare projects pay 0 cost).

### Caveats

1. `healthcare_rds.docx` template ships only as a README authoring guide; the .docx itself is authored separately.
2. MGS family stubs (Manifold / VIE / ZVB / AAP / MAP / TU) ship parameter specs only — real `.rfa` files come from manufacturers (Beaconmedaes, Pattons, GCE, Wandsworth, Static Systems).
3. `TwinReadback` BACnet / OPC-UA transports are abstract stubs — real transports plug in behind the same interface (no third-party libs bundled).
4. `RAD_QE_NAME_TXT` sign-off remains mandatory before the radiation calculators are treated as authoritative — STING does not certify shielding.
5. EF migration not run yet — `dotnet ef migrations add HealthcarePack` is the next deployment step.
6. Dock-panel UI does not yet have a Healthcare tab — commands dispatch via `WorkflowEngine.ResolveCommand` and `StingCommandHandler` button tags only. A WPF Healthcare tab is the natural next phase.

## v4 MVP

**Status**: Phase 1 (parameters) → Phase 5 (fabrication) implemented across ~50 commits on branch `claude/sting-tools-v4-mvp-SiPGw`. Code committed without `dotnet build` verification (Linux sandbox, no .NET / Revit API). Verify in Revit before merge.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Core/Placement/` | Fixture placement engine (rule + scorer + candidate + lighting grid) |
| `StingTools/Core/Routing/` | Auto-drop engines (DropEngineBase + AutoConduitDrop / AutoPipeDrop / AutoDuctDrop) |
| `StingTools/Core/Validation/` | Five v4 validators (connectivity / fill / spec / termination / slope) + ValidationResult record |
| `StingTools/Core/Fabrication/` | Fabrication coordinator + AssemblyGrouper + AssemblyBuilder + AssemblyViewBuilder + ShopDrawingComposer + IsoSymbolPlacer + per-discipline subfolders (Electrical / Pipe / Duct) |
| `StingTools/Commands/Placement/` | PlaceFixturesCommand, LightingGridCommand, LearnPlacementV4Command |
| `StingTools/Commands/Routing/` | AutoDropCommand, GenerateLayoutCommand, ValidateFillsCommand |
| `StingTools/Commands/Validation/` | RunAllValidatorsCommand |
| `StingTools/Commands/Fabrication/` | GenerateFabPackageCommand, ExportCutListCommand, ExportIsometricsCommand, ExportWeldMapCommand |
| `StingTools/Data/Placement/` | STING_PLACEMENT_RULES.json (43 rules) |
| `StingTools/Data/Fabrication/` | STING_FAB_RULES.json (6 disciplines), STING_ISO_SYMBOLS_INDEX.csv (180+ symbols) |
| `StingTools/Data/Parameters/` | STING_PARAMS_V4.txt shared-parameter fragment |
| `Families/AssemblyTitleBlocks/` | 7 title block parameter spec stubs + README |

### New namespaces

`StingTools.Core.Placement`, `StingTools.Core.Routing`, `StingTools.Core.Validation`, `StingTools.Core.Fabrication`, `StingTools.Core.Fabrication.Electrical`, `StingTools.Core.Fabrication.Pipe`, `StingTools.Core.Fabrication.Duct`, `StingTools.Commands.Placement`, `StingTools.Commands.Routing`, `StingTools.Commands.Validation`, `StingTools.Commands.Fabrication`.

### New commands (12)

| Command tag | Class | Tab |
|---|---|---|
| `Placement_PlaceFixtures` | `PlaceFixturesCommand` | Fixtures |
| `Placement_LightingGrid` | `LightingGridCommand` | Fixtures |
| `Placement_Learn` | `LearnPlacementV4Command` | Fixtures |
| `Routing_AutoDrop` | `AutoDropCommand` | Routing |
| `Routing_GenerateLayout` | `GenerateLayoutCommand` | Routing |
| `Routing_ValidateFills` | `ValidateFillsCommand` | Routing |
| `Validation_RunAll` | `RunAllValidatorsCommand` | (called from Routing) |
| `Fabrication_GeneratePackage` | `GenerateFabPackageCommand` | Fabrication |
| `Fabrication_ExportCutList` | `ExportCutListCommand` | Fabrication |
| `Fabrication_ExportIsometrics` | `ExportIsometricsCommand` | Fabrication |
| `Fabrication_ExportWeldMap` | `ExportWeldMapCommand` | Fabrication |
| `Fabrication_PlaceISOSymbols` | inline TaskDialog (placement runs in GeneratePackage) | Fabrication |

### New tags Studio sub-tabs (3)

`Fabrication`, `Routing`, `Fixtures` — added to `tagStudioTabs` inside the existing TAGS top-tab. Each follows the proven DockPanel + pinned WrapPanel + ScrollViewer pattern.

### New parameter count

- **6 net-new shared parameters** (S1.1 + S1.2): PLACE_ANCHOR / PLACE_OFFSET_X_MM / PLACE_SIDE / CPC_SZ_MM / PPE_INSULATION_THK_MM / PLM_SLOPE_PCT_V4
- **46 fabrication / LPS / pricing constants** (S1.3) in `StingTools.Core.Fabrication.{AssyParams,LpsParams,CostParams}` with placeholder `v4-YYYY-xxxx` GUIDs awaiting family library authoring.
- **Total**: 52 new constants. The 6 net-new ones use stable GUIDs; the 46 placeholders need real GUIDs assigned during family-library authoring.

### Caveats

1. Built without `dotnet build` verification — every Revit API call uses the documented signature but has not been compile-checked. `// TODO-VERIFY-API` comments mark the most uncertain spots (`AssemblyInstance.Create` category arg, `Conduit.Create` / `Pipe.Create` / `Duct.Create` overloads, ISO 6412 axonometric section transform).
2. The 7 title block `.rfa` families are NOT shipped — only their parameter specs. `ShopDrawingComposer.ResolveTitleBlock` falls back to the first available title block in the project (now warns loudly via `FabricationResult.Warnings` and `StingLog`).
3. ISO 6412 detail families (180 entries) are referenced by name in `STING_ISO_SYMBOLS_INDEX.csv`; `IsoSymbolPlacer` lazy-loads from `Families/ISO6412/` and warns once per missing family.

## Drawing Template Manager (Phase 113)

**Status**: Foundation landed on `claude/fix-text-visibility-layouts-OsiY9`. A single-source engine for how every drawing is produced — sheet size, title block, scale, view template, slot layout, annotation rule pack, crop strategy, section-marker family, viewport type, sheet numbering — replacing four scattered configuration surfaces (SheetTemplateEngine, ShopDrawingComposer hard-codes, DocAutomation TaskDialog prompts, SheetManager presets).

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Core/Drawing/` | Drawing Type engine: POCO model, loader with 15 built-ins, routing dispatcher, pre-flight validator |
| `StingTools/Commands/Drawing/` | Diagnostic commands (Inspect + Reload) |
| `StingTools/Data/STING_DRAWING_TYPES.json` | Corporate baseline: 15 drawing types + routing table, extracted to `data/` at build |

### New namespaces

`StingTools.Core.Drawing`, `StingTools.Commands.Drawing`.

### Concept

A **DrawingType** is a named JSON bundle that answers every presentation question for a single produced drawing:

| Field | Purpose |
|---|---|
| `id` / `name` | Stable identifier + human label |
| `purpose` | Plan / RCP / Section / Elevation / Detail / Schedule / Spool / Coordination / Legend / 3D |
| `discipline`, `phase` | Routing keys (wildcarded with `*`) |
| `paperSize`, `titleBlockFamily`, `orientation` | Sheet identity |
| `scale`, `detailLevel` | View appearance basics |
| `viewTemplateName`, `viewportTypeName` | Graphic standards binding |
| `sheetNumberPattern`, `sheetNamePattern` | Token-substituted (see below) |
| `crop` | `ScopeBox` / `ScopeBoxOrBbox` / `TightBbox` / `RoomBoundary` / `None` + margin |
| `sectionMarker` | Family + mark prefix + bubble style + far-clip offset |
| `slots[]` | Normalised 0..1 positions on the drawable zone (paper-size independent) |
| `annotation` | `AnnotationRulePack` — what to auto-dim / auto-tag, dimension strategy, per-category tag families, `denseUntilScale` modifier |
| `print` | Colour scheme, line-weight scale, halftone links |
| `origin` | `corporate` (SHA-256 checksum-locked) vs `project` (editable) |

Rules live in the same JSON as `routing[]` — first-match-wins rules of the shape `(discipline, phase, docType) → drawingTypeId`.

### Token patterns

Sheet number and sheet name patterns are substituted by `ShopDrawingComposer.SubstituteTokens`:

| Token | Replaced with |
|---|---|
| `{spool}` | Spool number from `AssyParams.SPOOL_NR_TXT` |
| `{disc}` | ISO single-letter discipline code |
| `{discipline}` | Full discipline name (e.g. "Pipe", "Electrical") |
| `{sys}` | Sanitised system code |
| `{lvl}` | Level code |
| `{mark}` | Section / elevation / detail mark |
| `{seq}` | Zero-padded 4-digit sequence (default) |
| `{seq:D2}` / `{seq:D3}` / `{seq:D4}` | Zero-padded sequence with explicit width |

### New classes

| Class | Purpose |
|---|---|
| `Core.Drawing.DrawingType` | Root POCO — all 15 fields above |
| `Core.Drawing.DrawingSlot` | Normalised viewport slot with optional per-slot scale / detailLevel / viewTemplate / viewportType override |
| `Core.Drawing.AnnotationRulePack` | Auto-dim / auto-tag flags, dimension style, per-category `tagFamilies`, scale-aware density |
| `Core.Drawing.DrawingCropStrategy` | Crop mode + margin + optional scope-box name |
| `Core.Drawing.SectionMarkerSpec` | Section / elevation / callout marker family + mark prefix |
| `Core.Drawing.PrintOverride` | Colour scheme + line-weight scale + halftone links |
| `Core.Drawing.DrawingRoutingRule` | `(discipline, phase, docType) → drawingTypeId` |
| `Core.Drawing.DrawingTypeLibrary` | Root JSON document (`drawingTypes[]` + `routing[]`) |
| `Core.Drawing.DrawingTypeRegistry` | Loader + SHA-256 corporate-lock + project-override merge; cached per-document |
| `Core.Drawing.DrawingDispatcher` | `Resolve(doc, disc, phase, docType)` + `CandidatesForDiscipline` |
| `Core.Drawing.DrawingTypeValidator` | Pre-flight: missing title block / view template / viewport type / section-marker / tag family + slot geometry sanity |

### New commands (2)

| Tag | Class | Purpose |
|---|---|---|
| `DrawingTypes_Inspect` | `DrawingTypesInspectCommand` | Read-only diagnostic: lists all types + routing + validation issues |
| `DrawingTypes_Reload` | `DrawingTypesReloadCommand` | Force registry cache refresh after editing JSON on disk |

### Built-in corporate catalogue (40)

Shipped in `Data/STING_DRAWING_TYPES.json`. Core 15 (phase 113 foundation): `arch-plan-A1-1to100`, `arch-rcp-A1-1to100`, `arch-section-A1-1to50`, `arch-elev-A1-1to100`, `arch-detail-A3-1to20`, `struct-plan-A1-1to100`, `struct-section-A1-1to50`, `mep-plan-A1-1to100`, `mep-coord-A1-1to50`, `pipe-spool-A1-1to50`, `duct-spool-A1-1to50`, `elec-riser-A2-1to100`, `door-schedule-A2`, `handover-A1`, `legend-A2`.

Week 1 expansion adds 17 production types covering the AEC/FM lifecycle:

| Discipline | Added types |
|---|---|
| Architectural | `arch-site-A1-1to500`, `arch-roof-A1-1to100`, `arch-floor-finishes-A1-1to100`, `arch-fire-strategy-A1-1to100`, `arch-accessibility-A1-1to100`, `arch-interior-elev-A1-1to50`, `arch-window-schedule-A2` |
| Structural    | `struct-foundation-A1-1to100`, `struct-rebar-detail-A3-1to20` |
| Mechanical    | `mep-hvac-duct-A1-1to100`, `mep-plantroom-A1-1to50` |
| Electrical    | `elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100` |
| Public Health | `plumb-drainage-A1-1to100` |
| FM / handover | `fm-asset-location-A1-1to100` |
| Coordination  | `coord-clash-A1-1to50` |

Presentation + clarification pack adds 8 client-facing types (all print with `colourScheme: PresentationRich` or `ClarificationRed`, lighter line weights, minimal grid/room tagging):

| Purpose | Types |
|---|---|
| Client presentation | `pres-3d-axon-A1` (3D + key plan + caption), `pres-perspective-A1` (full-bleed perspective), `pres-exterior-elev-A1` (material callouts, mono halftone), `pres-render-board-A1` (4-up renders), `pres-context-site-A1` (aerial + legend + caption) |
| Clarification       | `clar-markup-A1` (plan + query log + revision strip), `clar-rfi-A3` (single-issue A3 sketch + question + revision), `clar-design-intent-A1` (plan + 3D + narrative + materials strip) |

Routing table grew to 43 rules covering doc types: `SITE`, `ROOF_PLAN`, `FLOOR_FINISHES`, `FIRE_STRATEGY`, `ACCESSIBILITY`, `INTERIOR_ELEVATION`, `WIN_SCHEDULE`, `FOUNDATION`, `REBAR_DETAIL`, `HVAC_DUCT`, `PLANTROOM`, `POWER`, `LIGHTING`, `FIRE_ALARM`, `DRAINAGE`, `ASSET_LOCATION`, `CLASH`, `PERSPECTIVE`, `RENDER_BOARD`, `CONTEXT`, `DESIGN_INTENT`, `CLARIFICATION`, `RFI`; presentation rules match on `phase: PRESENTATION` so the same discipline can dispatch to production vs presentation types by phase.

### Project-scoped overrides

Registry layers a project override from `<project>/_BIM_COORD/drawing_types.json` on top of the corporate baseline. Project entries win by `id`; project routing rules are **prepended** (first-match-wins). Mutating a corporate entry on disk flips its `origin` to `project` via SHA-256 checksum drift detection (see `DrawingTypeRegistry.ComputeChecksums`).

### Wiring — fabrication is the Phase I proof point

`ShopDrawingComposer` consults the registry via a 3-tier fallback chain (no regression):

1. User-picked `FabricationOptions.ShopDrawing.TitleBlockSymbolId` / `SheetNumberPattern` / `SheetNamePattern` (win)
2. Drawing Type resolved by `DrawingDispatcher.Resolve(doc, disc, "*", Spool)` — `pipe-spool-A1-1to50` / `duct-spool-A1-1to50`
3. Historic hard-coded per-discipline dict + session sequence counter

Phase II (complete, commit `384a374b`) wired `DocAutomationExtCommands.BatchSectionsCommand` / `BatchElevationsCommand` / `BatchSheetsCommand` into the registry via `DrawingTypePresentation.Apply`; each falls back to its historic template search when no profile matches. `SheetManagerCommands.CreateFromTemplateCommand` still uses the hard-coded 6 C# templates in `SheetTemplateEngine.cs` — Phase III follow-up.

### Week 2–6 enhancements (complete)

**Week 2 — ViewStylePack shared-style layer** (`Core/Drawing/ViewStylePack.cs`, `ViewStylePackRegistry.cs`, `ViewStylePackApplier.cs`). Factors graphic overrides / filters / VG overrides / text+dim styles / tag-family maps out of `DrawingType` so the 40 profiles share ~11 visual packs. `DrawingType.ViewStylePackId` references a pack by id; `Extends` field on packs supports inheritance chains. Shipped as `Data/STING_VIEW_STYLE_PACKS.json`: `corp-base`, `corp-standard-plan`, `corp-standard-rcp`, `corp-standard-section`, `corp-standard-elevation`, `corp-standard-detail`, `corp-fabrication-shop`, `corp-presentation-rich`, `corp-presentation-mono`, `corp-coordination`, `corp-clarification`. Applier walks the extends chain, resolves category names / BIC strings / subcategory `<brackets>` → ElementId, writes `OverrideGraphicSettings` on categories + filters.

**Week 3 — Parameter stamping + browser organizer** (`Core/Drawing/DrawingTypeStamper.cs`, `Commands/Drawing/DrawingBrowserOrganizerCommand.cs`). Two new shared parameters written onto every view/sheet a generator creates: `STING_DRAWING_TYPE_ID_TXT` and `STING_STYLE_LOCKED_BOOL`. Stamper is workshared-safe (`WorksharingUtils.GetCheckoutStatus` gate). Browser Organizer creates `'STING - by Drawing Type'` organizations for views + sheets, keyed off the shared param GUID via `FolderItemsParameter`. `DrawingTypes_BrowserOrganize` button in DOCS.

**Week 4 — SyncStyles + drift detection** (`Core/Drawing/DrawingDriftDetector.cs`, `Commands/Drawing/DrawingSyncStylesCommand.cs`). Detector scans every stamped view, reports SCALE / DETAIL / TEMPLATE drift. SyncStyles shows first-10 preview, on confirm re-runs `DrawingTypePresentation.Apply` (annotation off to avoid re-dimensioning) against each drifted view. Skips `STING_STYLE_LOCKED_BOOL == 1`. Inspect command surfaces drift count in its headline. `DrawingTypes_SyncStyles` button in DOCS.

**Week 5 — Scope-box auto-binding** (`Core/Drawing/ScopeBoxBinder.cs`, `Commands/Drawing/GenerateFromScopeBoxesCommand.cs`). Scope boxes named `STING::<drawing-type-id>::<level-code?>::<tag?>` auto-bind to a profile. One command creates views + applies the profile + crops to the scope box. Idempotent — re-run finds existing stamped views and re-applies rather than duplicating. `DrawingTypes_FromScopeBoxes` button in DOCS.

**Week 6 — Conditional routing + profile inheritance**. `DrawingRoutingRule` gains 5 optional regex predicates: `disciplineMatches`, `phaseMatches`, `docTypeMatches`, `levelMatches`, `projectCodeMatches`; all set predicates must match (logical AND). `DrawingDispatcher.Resolve(doc, disc, phase, docType, levelCode)` new level-aware overload; existing 4-arg form delegates. `DrawingType.Extends` field + `DrawingTypeRegistry.ResolveExtends` fold parent → child at lookup time with loop detection, mirror of the pack extends chain.

**Bonus — DrawingCropApplier**. Executes the previously-declarative `DrawingType.Crop`: `ScopeBox`, `ScopeBoxOrBbox`, `TightBbox`, `RoomBoundary`, `None`. mm margins, element-bbox unions, scope-box resolution by name. Wired into `DrawingTypePresentation.Apply` between view-template and style-pack steps.

**Bonus (4) — Title-block parameter binding**. `DrawingType.TitleBlockParams` (Dictionary<string,string>) binds title-block instance parameters declaratively. `Core/Drawing/TitleBlockParamApplier.cs` resolves the value template with two substitution kinds: `${PRJ_ORG_xxx}` (reads from `ProjectInformation` by parameter name) and `{disc}/{lvl}/{sys}/{spool}/{mark}/{seq:Dn}` (caller-supplied token dict). Unknown tokens pass through literal. Handles String / Integer / Double storage types; warns on unparsable numeric input. Wired into `ShopDrawingComposer.ApplySheetMetadata` so fabrication sheets get both hard-coded fab cells (spool, weight, FAB_LOC, status, BOM rev) and declarative corporate cells (client, project code, suitability, revision, sheet number). Editor gets a new 'Title-block parameter binding' card — row-per-param, rename-key-preserves-value. Three shipped profiles seeded with examples: `arch-plan-A1-1to100` (10 params), `pipe-spool-A1-1to50` (12 params), `pres-3d-axon-A1` (9 params).

**Phase III — SheetManager integration (complete)**. `SheetManager.CreateFromTemplateCommand` picker now lists Drawing Type profiles alongside the 6 built-in SheetTemplates + user-saved ones. `Docs/DrawingTypeSheetAdapter.cs` converts `DrawingType → SheetTemplate` on pick (slot coords pass through verbatim; both use 0..1-over-drawable-zone). Title-block resolution prefers the profile's declared family when loaded, else falls through to the historic picker. Post-create step stamps `STING_DRAWING_TYPE_ID_TXT` and runs `TitleBlockParamApplier` so every sheet from the profile path is registry-tracked and title-block-populated. Retires the last non-registry drawing-production path — every sheet-creation entry point now routes through the same profile catalogue.

### Pipeline order (final)

`DrawingTypePresentation.Apply(doc, view, dt)` runs:

1. **Lock check** — `DrawingTypeStamper.IsLocked` → skip
2. **Stamp** `STING_DRAWING_TYPE_ID_TXT`
3. **Scale** `view.Scale = dt.Scale`
4. **Detail level** `view.DetailLevel = dt.DetailLevel`
5. **View template** lookup by name + apply
6. **Crop strategy** via `DrawingCropApplier`
7. **View style pack** resolve + `ViewStylePackApplier.Apply`
8. **Annotation pass** via `AnnotationRunner` (auto-dim + auto-tag)

For sheet creation (fabrication composer, SheetManager CreateFromTemplate), an additional pair runs after the sheet is minted:

9. **DrawingType stamp** via `DrawingTypeStamper.Stamp(sheet, dt.Id)`
10. **Title-block param binding** via `TitleBlockParamApplier.Apply(doc, sheet, dt, tokens)` — `${PRJ_ORG_xxx}` + `{disc}/{lvl}/{sys}/{spool}/{mark}/{seq:Dn}` resolved against ProjectInformation + caller's token dict.

Every step is try/catch-wrapped; warnings collect into `ApplyResult.Warnings`, the run continues on failures.

### Week 7 — ViewStylePack editor tab

`DrawingTypeEditorDialog` is now a two-tab editor. Tab 1 "Drawing Types" is the original content unchanged. Tab 2 "View Style Packs" mirrors the same shape — search-filtered list on the left with `＋ New / Clone / Delete`, scrollable form on the right with five cards: Identity (id / name / description / **extends parent id combo** / origin), Appearance (lineWeightScale, textStyle, dimensionStyle, hatchPalette), Filter rules (row-per-rule grid: name / Visible / Halftone / projection colour+weight / cut colour+weight / transparency / × delete), VG overrides (per-category row grid with same cells + rename-key-preserves-value), Tag families (category → family-name map).

Save button routes to the active tab: `drawing_types.json` (tab 0, existing) or `view_style_packs.json` (tab 1, new). Only project-origin entries are written — corporate baseline on disk stays pristine. Edits to corporate packs silently flip `origin` to `project` via `ViewStylePackRegistry.ComputeChecksums` drift detection, same mechanism Drawing Types use.

The tab is a pure UI layer on top of the Week 2 data model — no changes to `ViewStylePack` / `ViewStylePackRegistry` / `ViewStylePackApplier`.

### Caveats (Drawing Template Manager)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 40 corporate drawing types + 11 style packs reference title-block / view-template / tag-family names that projects must supply; the validator reports missing assets as Warnings (not Errors) so the JSON ships usable on a stock project.
3. Crop strategy `RoomBoundary` falls back to `TightBbox` when no rooms are in the view; plan views that should be room-bounded need rooms placed first.
4. `IUpdater`-based live style propagation (automatic re-apply when a profile / pack changes in-session) is not implemented — the manual `SyncStyles` command covers the same ground on demand. Runtime cost vs. always-on drift-zero trade-off means this stays deferred.
5. `TitleBlockParamApplier` writes the first title-block instance found on a sheet. Sheets hosting multiple title blocks (unusual) only get the first one populated — the applier warns and moves on.

## Template Engine v1.1 (Phase 112)

**Status**: S01–S18 landed on `claude/implement-template-engine-COd9n`
(commits `e92a504f` + `a37c4c61`). Everything from the
`20260423_planscape_template_engine_runner_v1.1.pdf` runner is
implemented against the nested repo layout — the runner assumed a flat
root, but `.cs` files live under `StingTools/Docs/{Templates,Workflow,Search}/`.
S19 (signature provider) and S20 (AI metadata extraction) are design-
complete and deferred to v1.2 per the runner.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Docs/Templates/` | MiniWord + ClosedXML render pipeline (14 files) |
| `StingTools/Docs/Workflow/` | WorkflowEngine + AuditLog + DistributionGroups (6 files) |
| `StingTools/Docs/Search/` | Lucene.NET document index + saved searches (2 files) |
| `StingTools/Docs/_template_sources/` | 16 embedded `.docx` / `.xlsx` templates (EmbeddedResource) |
| `StingTools/Docs/_workflow_sources/` | 5 embedded workflow JSONs (EmbeddedResource) |

### New namespaces

`Planscape.Docs.Templates`, `Planscape.Docs.Workflow`,
`Planscape.Docs.Search`. The plugin assembly remains `StingTools` — these
namespaces coexist with the existing `StingTools.*` tree.

### New parameters (13)

All `PRJ_ORG_*` scoped to `ProjectInformation`, UUIDv5 in Planscape docs
namespace `a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a`. Constants on
`ParamRegistry`: `ORG_PROJECT_CODE`, `ORG_ORIGINATOR_CODE`,
`ORG_COMPANY_NAME`, `ORG_COMPANY_ADDRESS`, `ORG_CLIENT_NAME`,
`ORG_APPOINTING_PARTY`, `ORG_LEAD_APPOINTED_PARTY`, `ORG_PARTICIPANTS`,
`ORG_PHASE`, `ORG_CLASS`, `ORG_WORKFLOW_PROFILE`,
`ORG_SIGNATURE_PROVIDER`, `ORG_AI_EXTRACT_ENABLED`. Exposed as
`AllOrganisationParams[]` + `OrganisationDefaults{}` (seeds `"PLNS"` /
`"Planscape Limited"` / `"Kampala, Uganda"` / phase `DE` / class `2` /
workflow profile `default`).

### New commands (8)

Registered in the BIM tab of the dock panel and in `StingCommandHandler`:

| Tag | Class | Transaction | Description |
|---|---|---|---|
| `IssueDeliverable` | `Planscape.Docs.Templates.IssueDeliverableCommand` | Manual | Render A01, write revision history, start `deliverable_issue_default` workflow, audit |
| `ReIssueDeliverable` | `ReIssueDeliverableCommand` | Manual | Bump revision + re-issue |
| `PublishDeliverable` | `PublishDeliverableCommand` | Manual | Promote to S4 / `PUBLISHED` CDE container |
| `CancelDeliverable` | `CancelDeliverableCommand` | Manual | Render A02 cancellation notice, archive |
| `SupersedeDeliverable` | `SupersedeDeliverableCommand` | Manual | Mint new number, render A03 |
| `ReplaceDeliverable` | `ReplaceDeliverableCommand` | Manual | Render A04 replacing notice, cross-link |
| `CreateTransmittalOrchestrated` | `CreateTransmittalOrchestratedCommand` | Manual | Full pipeline: mint id → render B06 → start `transmittal_default` workflow → audit |
| `BulkIssueDeliverables` | `BulkIssueDeliverablesCommand` | Manual | Issue all currently-selected deliverables in one `TransactionGroup` |

Existing `DocumentManagementDialog.QuickTransmittal` and
`BIMManagerCommands.CreateTransmittalCommand` also delegate to
`TransmittalOrchestrator.Create` after their classic JSON write — both
entry points produce rendered docx + workflow instance + audit entry,
while keeping their existing UI rows (`delivery_tracking`,
`recipient_count`, `status_history`) unchanged for backwards compat.

### Runtime artefacts (per project)

Written under `<project>/_BIM_COORD/`:

| File | Purpose |
|---|---|
| `templates/manifest.json` | Seeded from `ProjectInformation` + `PRJ_ORG_*` on first open |
| `templates/*.docx` / `*.xlsx` | 16 default templates extracted from embedded resources |
| `workflows/*.json` | 5 default workflow definitions extracted from embedded resources |
| `generated/YYYYMMDD_{doc_number}_{template_id}.{ext}` | Rendered output |
| `doc_sequences.json` | Atomic counter store per `(type\|role\|fb\|sb)` key |
| `deliverables.json` | Lifecycle state + revision history per deliverable |
| `transmittals.json` | Enriched with `template_id`, `rendered_file_path`, `workflow_instance_id` |
| `workflow_state.json` | `WorkflowInstance` rows per open workflow |
| `audit_log_{yyyy}_{MM}.jsonl` | Append-only SHA-256 tamper-evidence chain |
| `distribution_groups.json` | Type/role/suitability-scored recipient groups |
| `saved_searches.json` | Per-user saved document search queries |
| `search_index/` | Lucene FSDirectory index over register + deliverables |

### Extraction lifecycle

`StingToolsApp.OnDocumentOpened` calls
`EmbeddedTemplates.ExtractIfMissing(doc)` on first open per project.
This streams the 16 templates + 5 workflow JSONs onto disk and writes
a default `manifest.json` seeded from `ProjectInformation` and
`PRJ_ORG_*`. Idempotent — re-opens are no-ops.

### Caveats (template engine)

1. Built without `dotnet build` verification (Linux sandbox).
2. The 16 `.docx` / `.xlsx` templates ship as professional-quality stubs
   — proper Word tables, banded header, footer `PAGE`/`NUMPAGES` fields,
   loop tables, signature blocks — but designers can still expand
   bespoke layouts in Word without breaking the `{{token}}` contract.
3. S19 (signature provider) and S20 (AI extraction) are deferred to v1.2
   per the runner PDF. `PRJ_ORG_SIGNATURE_PROVIDER_TXT` and
   `PRJ_ORG_AI_EXTRACT_ENABLED_BOOL` are already defined so enabling
   them is additive only.

## Technology Stack

- **Platform**: Autodesk Revit 2025/2026/2027 (BIM software)
- **Language**: C# / .NET 8.0 (`net8.0-windows`)
- **Plugin type**: `IExternalApplication` + `IExternalEventHandler` + `IDockablePaneProvider` + `IUpdater` with `IExternalCommand` classes
- **Dependencies**: `Newtonsoft.Json` 13.0.3, `ClosedXML` 0.104.2 (XLSX/BOQ export), `ZXing.Net` 0.16.9 (QR code generation), `MiniWord` 0.9.0 (template engine v1.1 DOCX renderer), `Lucene.Net` 4.8.0-beta00016 + `Lucene.Net.Analysis.Common` 4.8.0-beta00016 (document index v1.1), Revit API assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`)
- **Data formats**: CSV and JSON files for configuration data (materials, parameters, schedules)
- **Deployment**: `StingTools.addin` (XML manifest) + `extract_plugin.sh` (Bash)

## Directory Structure

```
STINGTOOLS/
├── .gitignore                          # Build output, IDE files, NuGet, OS files
├── CLAUDE.md                           # AI assistant guide (this file)
├── StingTools.addin                    # Revit addin manifest (XML)
├── build.bat                           # Windows build script
├── extract_plugin.sh                   # Plugin extraction/deployment script
├── open_git_bash.bat                   # Dev environment helper (Git Bash)
├── open_vs.bat                         # Visual Studio launcher
├── open_vs.sh                          # VS Code/shell launcher
│
└── StingTools/                         # C# project root
    ├── StingTools.csproj               # .NET 8 project file
    │
    ├── Properties/
    │   └── AssemblyInfo.cs             # Assembly metadata (v1.0.0.0)
    │
    ├── Core/                           # Shared infrastructure (17 files, ~25,000 lines)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI + dockable panel registration + ToggleDockPanelCommand + DocumentOpened quality gate + morning briefing
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error) + EscapeChecker utility
    │   ├── ParamRegistry.cs            # Single source of truth for parameter names, GUIDs, containers, bindings (loads from PARAMETER_REGISTRY.json) + stale/cluster/display/position + COBie/asset/style constants
    │   ├── ParameterHelpers.cs         # Parameter read/write + SpatialAutoDetect + NativeParamMapper + TokenAutoPopulator + PhaseAutoDetect + TypeTokenInherit + CopyTokensFromNearest + GetInt + SetInt + CommandExecutionContext
    │   ├── SharedParamGuids.cs         # Backwards-compatible facade wrapping ParamRegistry (GUID lookups, category bindings)
    │   ├── TagConfig.cs               # ISO 19650 tag lookup tables, tag builder, TagIntelligence, TAG7 narrative builder + SeqScheme variants + BuildDisplayTag
    │   ├── StingAutoTagger.cs          # IUpdater — real-time auto-tagging + visual tag placement + discipline filter + StingStaleMarker IUpdater + deferred queue
    │   ├── WorkflowEngine.cs           # Workflow orchestration — JSON preset command chaining + 19 conditional operators + result persistence + WorkflowTrendCommand + sector-specific presets
    │   ├── ComplianceScan.cs           # Cached compliance scan with per-discipline/phase breakdown + incremental update + compliance trend tracker
    │   ├── OutputLocationHelper.cs     # Centralized output directory management with fallback chain + timestamped paths
    │   ├── IPanelCommand.cs            # Interface for WPF dockable panel commands + SafeApp/SafeDoc/SafeUIDoc extension methods
    │   ├── PerformanceTracker.cs       # Lightweight performance profiling engine for batch operations (per-element timing, LRU slowest tracking, CSV export)
    │   ├── WarningsManager.cs          # Comprehensive warnings management: 150+ classification rules, 16 auto-fix strategies, SLA tracking, deliverable impact analysis, baseline/trend + StingWarningHandler IFailuresPreprocessor
    │   ├── ProjectFolderEngine.cs      # ISO 19650 project folder structure management, CDE folder generation, file monitor
    │   ├── Phase74Enhancements.cs      # ModelCreationValidator, WarningPredictionEngine, DeliverableTracker, CoordinatorDailyPlanner, ComplianceFallDetector, ActionAuditLog
    │   ├── Phase75Enhancements.cs      # 29 workflow/coordination enhancements: WorkflowScheduler, cross-system automation, SLA monitoring, team activity tracking, role-based access, CDE state machine
    │   └── WorkflowMaturityEngine.cs   # Step dependency resolver (DAG), partial rollback manager, commissioning workflows, workflow validator + metrics
    │
    ├── Select/                         # Element selection + color commands (4 files, ~30+ commands)
    │   ├── CategorySelectCommands.cs   # 14 category selectors + SelectAllTaggable + CategorySelector helper
    │   ├── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite + SelectStale + QuickTagPreview
    │   ├── ColorCommands.cs            # 5 color-by-parameter commands + ColorHelper (10 palettes, presets, filter gen)
    │   └── TagSelectorCommands.cs      # Multi-criteria tag selector (text, size, arrowhead, leader, family, host category, orientation, discipline)
    │
    ├── UI/                             # WPF dockable panel UI + wizards + theme engine (40 C# files + 3 XAML, ~35,000 lines)
    │   ├── StingDockPanel.xaml         # WPF markup for 9-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM/TAGS)
    │   ├── StingDockPanel.xaml.cs      # Code-behind: button dispatch, colour swatches, status bar
    │   ├── StingCommandHandler.cs      # IExternalEventHandler — dispatches 1100+ button tags to 750+ command classes + inline helpers
    │   ├── StingDockPanelProvider.cs   # IDockablePaneProvider — registers panel with Revit
    │   ├── StingProgressDialog.cs      # Reusable modeless WPF progress window for batch operations (cancel, ETA, progress bar)
    │   ├── StingListPicker.cs          # Reusable WPF list picker dialog with search/filter, replacing paginated TaskDialogs
    │   ├── StingModePicker.cs          # Reusable WPF mode picker dialog for command mode selection
    │   ├── StingWizardDialog.cs        # Base multi-page WPF wizard framework (448 lines) — reusable page navigation, validation, summary
    │   ├── StingDataGridDialog.cs      # Reusable WPF data grid dialog for tabular data display with search/filter
    │   ├── StingExportDialog.cs        # ExLink-style export dialog with column mapping, preview, and format selection
    │   ├── StingResultPanel.cs         # Reusable rich WPF result display: sections, metrics, RAG bars, tables, action buttons, CSV export
    │   ├── BatchRenameDialog.cs        # Single-step WPF batch rename dialog with live preview, 7 operations
    │   ├── ParameterLookupDialog.cs    # Enhanced WPF parameter lookup with 11-operator condition builder
    │   ├── BulkOperationDialog.cs      # Unified WPF dialog for bulk parameter operations (replaces 5-step TaskDialog)
    │   ├── CombineConfigDialog.cs      # Unified WPF dialog for Combine Parameters configuration
    │   ├── HeadingStyleDialog.cs       # Unified WPF dialog for TAG7 heading style
    │   ├── COBieExportWizard.cs        # Multi-page COBie V2.4 export wizard with preset selection and sheet configuration
    │   ├── ExcelExchangeWizard.cs      # Excel import/export wizard with column mapping and validation
    │   ├── IssueWizard.cs              # BIM issue creation wizard with BCF integration
    │   ├── SmartPlacementWizard.cs     # Smart tag placement configuration wizard
    │   ├── BEPWizard.xaml              # WPF markup for BEP generation wizard
    │   ├── BEPWizard.xaml.cs           # BEP wizard code-behind
    │   ├── DocumentManagementDialog.cs  # ISO 19650 Document Management Center — 8-tab action bar, code legend, meeting manager, quick workflows
    │   ├── DocAutomationDialog.cs      # 4-tab unified doc automation dialog (Sheets/Views/Viewports/Export)
    │   ├── ModelCreationDialog.cs      # Unified model creation dialog with element type selector + dynamic options
    │   ├── ScheduleWizardDialog.cs     # Unified schedule wizard dialog (create/populate/audit/export/manage)
    │   ├── NewSheetDialog.cs           # WPF sheet creation dialog with discipline/numbering/title block options
    │   ├── StingDataExchangeDialog.cs  # Data exchange configuration dialog
    │   ├── BIMCoordinationCenter.cs    # 13-tab unified BIM coordination center: overview, model health, warnings, issues, revisions, platform, workflows, QA, 4D/5D, meetings, permissions
    │   ├── SheetManagerDialog.cs       # WPF dual-panel sheet manager dialog with TreeView navigation and context-sensitive detail views
    │   ├── ThemeManager.cs             # WPF theme engine — Light/Warm/Cool/Corporate themes with 13 color resource keys
    │   ├── ProjectSetupWizard.xaml     # WPF 7-page project setup wizard dialog
    │   └── ProjectSetupWizard.xaml.cs  # Code-behind: presets, validation, discipline config, review summary
    │
    ├── Docs/                           # Documentation commands (20 files, ~55+ commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   ├── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │   ├── DocAutomationCommands.cs    # DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets
    │   ├── DocAutomationExtCommands.cs # Batch views/sheets/sections/elevations, doc package, scope boxes, templates, drawing register, browser organizer, handover manual
    │   ├── ViewAutomationCommands.cs   # DuplicateView, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign, MagicRename, ViewTabColour
    │   ├── HandoverExportCommands.cs   # FM/O&M handover: COBie 2.4 export (18 sheets), maintenance schedule, O&M manual, asset health report, space handover report
    │   ├── JournalParserCommand.cs     # Revit journal diagnostics: parse journal files for errors, crashes, command timeline, memory usage
    │   ├── DocScheduleAutomation.cs    # DrawingRegisterSync, CrossScheduleValidator, PrintQueueManager, DocumentPackageBuilder
    │   ├── FamilyAuditCommands.cs      # Family audit and validation commands
    │   ├── PrintManagerCommands.cs     # Print queue management and batch print commands
    │   ├── SpatialValidationCommands.cs # Room connectivity audit, spatial analysis, area validation (BS 6465, BCO Guide)
    │   ├── SheetManagerEngine.cs       # Core sheet manager: drawable zone detection, scale calculation, shelf packing, collision detection, viewport placement, sheet cloning, naming/numbering, auto-arrange
    │   ├── SheetManagerEngineExt.cs    # Extended: MaxRects bin packing, layout presets (JSON), viewport type rules, batch clone/renumber, overflow handling
    │   ├── SheetManagerCommands.cs     # 8 commands: SheetManager, AutoLayout, CloneSheet, PlaceUnplaced, OptimalScale, SheetAudit, BatchArrange, MoveViewport
    │   ├── SheetSetCommands.cs         # 8 commands: MaxRectsLayout, SaveLayoutPreset, ApplyLayoutPreset, BatchClone, BatchRenumber, AutoAssignVPTypes, ExportSheetSet, PlaceWithOverflow
    │   ├── SheetTemplateEngine.cs      # Sheet templates, ISO 19650 compliance (10 rules), viewport grid alignment, edge alignment, distribution, batch PDF export
    │   └── SheetTemplateCommands.cs    # 8 commands: CreateFromTemplate, SaveSheetTemplate, SheetComplianceCheck, GridAlignViewports, AlignViewportEdges, DistributeViewports, BatchPrintSheets, ExportSheetRegister
    │
    ├── Tags/                           # Tagging commands (28 files, ~140+ commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view + TagNewOnly
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagAndCombineCommand.cs     # One-click: populate + tag + combine all
    │   ├── PreTagAuditCommand.cs       # Dry-run audit: predict tags, collisions, ISO violations
    │   ├── FamilyStagePopulateCommand.cs # Pre-populate all 7 tokens before tagging
    │   ├── CombineParametersCommand.cs # Interactive multi-container combine + CombinePreFlight
    │   ├── ConfigEditorCommand.cs      # View/edit/save project_config.json
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   ├── TokenWriterCommands.cs      # SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers,
    │   │                               #   BuildTags, CompletenessDashboard, SetSeqScheme + TokenWriter helper
    │   ├── ValidateTagsCommand.cs      # Validate tag completeness with ISO 19650 codes
    │   ├── SmartTagPlacementCommand.cs # 16 smart annotation commands + TagPlacementEngine (collision avoidance, templates, linked views, band alignment, position export, leader elbow avoidance)
    │   ├── TagFamilyCreatorCommand.cs  # 4 tag family commands: Create, Load, Configure Labels, Audit + TagFamilyConfig
    │   ├── SyncParameterSchemaCommand.cs # 3 schema commands: Sync, AddParamRemap, Audit + ParamRegistry propagation
    │   ├── LegendBuilderCommands.cs    # 31 legend commands: discipline/system/tag/color/material/equipment/fire rating legends + LegendEngine
    │   ├── RichTagDisplayCommands.cs   # 6 rich display commands: RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments
    │   ├── SystemParamPushCommand.cs   # 3 MEP system push commands: SystemParamPush, BatchSystemPush, SelectSystemElements
    │   ├── ResolveAllIssuesCommand.cs  # 1 one-click ISO 19650 compliance resolution
    │   ├── PresentationModeCommand.cs  # 4 presentation commands: SetMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle
    │   ├── ParagraphDepthCommand.cs    # 2 commands: SetParagraphDepth, ToggleWarningVisibility
    │   ├── TagStyleCommands.cs         # 10 tag style commands: ApplyTagStyle, ApplyColorScheme, ClearColorScheme, SetParagraphDepthExt, TagStyleReport, SwitchTagStyleByDisc, BatchApplyColorScheme, ColorByVariable, SetBoxColor, SetViewTagStyle
    │   ├── TagStyleEngine.cs           # Tag style engine: style presets, color schemes, paragraph depth control (128 style combinations)
    │   ├── Tag3DCommand.cs             # 1 command: Tag3D — tags elements in 3D views with spatial auto-detect
    │   ├── RepairDuplicateSeqCommand.cs # 1 command: RepairDuplicateSeq — smart duplicate SEQ repair with spatial proximity
    │   ├── FamilyParamCreatorCommand.cs # 1 command: FamilyParamCreator + FamilyParamEngine (shared param injection into .rfa files)
    │   ├── NLPCommandProcessor.cs      # Natural language intent recognition engine — maps queries to STING commands (50+ patterns)
    │   ├── TagIntelligenceCommands.cs  # 8 advanced tagging intelligence commands: rule engine, quality analysis, batch chain, version control, propagation, analytics, smart suggestion
    │   └── TagStyleEngineCommands.cs   # Rule-based tag family type switching: 128 style combinations via JSON-driven TAG_STYLE_RULES.json
    │
    ├── Organise/                       # Tag management commands (1 file, 47 commands)
    │   └── TagOperationCommands.cs     # Tag Ops (7), Leaders (14), Analysis (7), Annotation Color (5), Tag Appearance (5), Tag Type (1), AnomalyAutoFix (1), Clustering (2), DisplayMode (1), DiscCompliance (1), RetagStale (1), DeclusterTags (1) + LeaderHelper + AnnotationColorHelper
    │
    ├── BIMManager/                     # ISO 19650 BIM management + 4D/5D scheduling + Excel/Platform/Revision + gap analysis (14 files, 120+ commands, ~22,000 lines)
    │   ├── BIMManagerCommands.cs       # 37 commands: BEP (Create/Update/Export/Generate), Issues (Raise/Dashboard/Update/SelectElements), Documents (Register/Add/ValidateNaming/Briefcase), COBie export, Transmittals, CDE status, Reviews, ISO reference, BulkExport, StickyNotes (Create/Export/Select), ModelHealth (Dashboard/Export), MidpTracker, Export4DTimeline, Export5DCostData, FullComplianceDashboard, LinkPredecessors, AssignPhaseDates, MeasuredQuantities, ElementCountSummary, SetOutputDirectory, StageComplianceGate + BIMManagerEngine
    │   ├── ExcelLinkCommands.cs        # 6 commands: ExportToExcel, ImportFromExcel, ExcelRoundTrip, ExportSchedulesToExcel, ImportSchedulesFromExcel, ExportTemplate + ExcelLinkEngine
    │   ├── PlatformLinkCommands.cs     # 6 commands: ACCPublish, CDEPackage, BCFExport, BCFImport, PlatformSync, SharePointExport + PlatformLinkEngine
    │   ├── RevisionManagementCommands.cs # 12 commands: CreateRevision, RevisionDashboard, AutoRevisionCloud, RevisionSchedule, TrackElementRevisions, RevisionCompare, IssueSheetsForRevision, RevisionNamingEnforce, RevisionTagIntegration, RevisionExport, BulkRevisionStamp, AutoRevisionOnTagChange + RevisionEngine
    │   ├── SchedulingCommands.cs       # 12 commands: AutoSchedule4D, ImportMSProject, ViewTimeline4D, ExportSchedule4D, AutoCost5D, ImportCostRates, CostReport5D, CashFlow5D, PhaseFilter, PhaseSummary, MilestoneRegister, WorkingCalendar + Scheduling4DEngine
    │   ├── GapFixCommands.cs           # Cross-system gap fixes: CDE approval, entity linking, streaming COBie, coordination data refresh, compliance forecasting
    │   ├── GapAnalysisFixCommands.cs   # COBie extended import, HTML dashboard export, BEP stage validation, issue-revision linking, auto meeting minutes, tag revision diff
    │   ├── CoordinationCenterCommands.cs # BIM Coordination Center data assembly + action processing + 3D zoom
    │   ├── CarbonTrackingCommands.cs   # Embodied carbon tracking and reporting commands
    │   ├── LANCollaborationCommands.cs # LAN-based model collaboration and sync commands
    │   ├── LinkManagerCommands.cs      # Revit link management and federated model commands
    │   ├── ParameterDiffCommands.cs    # Parameter comparison and diff between models/versions
    │   ├── QualityAssuranceCommands.cs # QA automation: naming convention audit, MEP clearance, IFC property validation
    │   └── WorksetAuditCommands.cs     # Workset audit and management commands
    │
    ├── Model/                          # Auto-modeling engine (26 files, 130+ commands)
    │   ├── ModelCommands.cs            # 16 model commands: Wall, Room, Floor, Ceiling, Roof, Door, Window, Column, ColumnGrid, Beam, Duct, Pipe, Fixture, BuildingShell, DWGToModel, DWGPreview (all auto-tag via RunFullPipeline)
    │   ├── ModelEngine.cs              # Model creation engine + AutoTagCreatedElements + MEPRoutingEngine + RoomLayoutEngine + FamilyResolver
    │   ├── CADToModelEngine.cs         # DWG-to-BIM conversion engine: layer mapping, geometry extraction, element auto-detection + ISO 13567 patterns
    │   ├── ArchitecturalCreationEngine.cs # StairEngine (BS 5395), RailingEngine (BS 6180), CurtainWallEngine (BS EN 13830), OpeningEngine, CoveringFireRating, CoveringMoistureRisk, CoveringThermalBridge, FullModelAutomation
    │   ├── PlasteringCommands.cs       # 9 plastering/covering commands (BS EN 13914): material browser, substrate analysis, paint system, coverage calc, smart/batch apply, room schedule, quality check, export
    │   ├── PlasteringEngine.cs         # Plastering algorithm engine: 10 material types, drying times, coverage rates, substrate compatibility, BS EN 998 mortar classification
    │   ├── StructuralModelingCommands.cs # 76 structural commands: beams, columns, slabs, foundations, trusses, bracing, retaining walls, CAD pipeline, design suite, analysis
    │   ├── StructuralModelingEngine.cs # Structural creation engine: pad/strip footings, structural slabs/walls, beam systems, grid frames, bay detection
    │   ├── StructuralAnalysisEngine.cs # 20+ analysis algorithms: load path tracing, frame analysis, deflection check, fire resistance, vibration, wind load, seismic, SSI, progressive collapse
    │   ├── StructuralDesignSuite.cs    # Design intelligence: connection design, punching shear, crack width, rebar estimation, code compliance (BS EN 1992/1993/1997)
    │   ├── StructuralAdvancedDesign.cs # Fatigue assessment, torsion design, robustness checks, composite beam/slab design, partial factors
    │   ├── StructuralAdvancedDesignExt.cs # Deep beam STM, topology optimization, carbon assessment, thermal movement, construction sequence
    │   ├── StructuralIntelligenceEngine.cs # Smart sizing: adaptive beam/column/foundation factories, Voronoi load areas, BIM validation scoring
    │   ├── StructuralPrecisionEngine.cs # Precision: column load takedown, slab edge beams, bracing optimization, stability checks, constraint validation
    │   ├── StructuralCADPipeline.cs    # CAD-to-structural pipeline: wall detection, junction analysis, member classification, full automation
    │   ├── StructuralCADWizard.cs      # Scrollable WPF wizard for structural CAD import: layer analysis, Graitec numbering, construction logic
    │   ├── StructuralDWGWizard.cs     # Enhanced 7-page WPF wizard: layer mapping, properties, joining, tagging (~1,675 lines)
    │   ├── StructuralDWGEngine.cs     # Precision DWG-to-BIM engine: detection, creation, joining, type creation, quality scoring (~1,457 lines)
    │   ├── StructuralDWGCommands.cs   # 2 commands: StructuralDWGWizard + QuickStructuralDWG
    │   ├── StructuralDeepEngine.cs     # AutoTorsionDetector, LateralTorsionalBuckling (EC3), ConnectionDetailingEngine (SCI P358), CreepDeflectionAnalysis (EC2), FabricationToleranceChecker (BS EN 1090-2)
    │   ├── EnhancedStructuralPipeline.cs # UK steel section database (20 UB + 13 UC), StructuralAutoSizer (EC2/EC3/EC7), StructuralOptimizer (carbon), international DWG patterns
    │   ├── ExcelStructuralEngine.cs    # Excel-to-structural import (6 sheet formats), RebarEngine (EC2 BS EN 1992-1-1), UK rebar database (BS 4449), bar bending schedule (BS 8666)
    │   ├── AcousticAnalysisEngine.cs   # Sound insulation (BS EN 12354), reverberation (Sabine/Eyring), flanking path analysis, impact sound (Approved Document E), duct attenuation (CIBSE Guide B3)
    │   ├── MEPIntelligenceEngine.cs    # Fitting loss calculator (26 types), Darcy-Weisbach pressure drop, Hardy Cross flow balancing, vibro-acoustic analysis (CIBSE TG6)
    │   ├── SustainabilityEngine.cs     # BREEAM v6.0 assessment (10 categories), BS EN 15978 lifecycle carbon (A1-C4+D), ICE Database v3.0, circularity scoring
    │   └── StructuralTypeFactory.cs    # Intelligent type catalog: beam/column/slab type selection from span, load, material
    │
    ├── Temp/                           # Template commands (22 files, ~120+ commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MasterSetupCommand.cs       # One-click full project setup (15 steps)
    │   ├── ProjectSetupCommand.cs      # 7-page WPF project setup wizard
    │   ├── MaterialCommands.cs         # BLE + MEP material creation + MaterialPropertyHelper
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types + CompoundTypeCreator
    │   ├── ScheduleCommands.cs         # FullAutoPopulate, BatchSchedules, AutoPopulate, ExportCSV + ScheduleHelper
    │   ├── ScheduleEnhancementCommands.cs # 9 schedule mgmt: Audit, Compare, Duplicate, Refresh, FieldMgr, Color, Stats, Delete, Report
    │   ├── FormulaEvaluatorCommand.cs  # Formula engine (199 formulas) + FormulaEngine + ExpressionParser
    │   ├── TemplateCommands.cs         # Filters, worksets, view templates (23 template defs + VG configuration)
    │   ├── TemplateExtCommands.cs      # Line patterns, phases, apply filters, cable trays, conduits, material schedules
    │   ├── TemplateManagerCommands.cs  # 18 template intelligence commands + TemplateManager engine (~3,892 lines)
    │   ├── DataPipelineCommands.cs     # ValidateTemplate (45 checks), DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit
    │   ├── DataPipelineEnhancementCommands.cs # Cross-validation: registry vs CSV drift detection, parameter coverage, field remapping
    │   ├── AutoModelCommands.cs        # DWG-to-BIM automation: link DWG/DXF, tracing geometry extraction, batch import
    │   ├── COBieDataCommands.cs        # COBie reference data management: type map browser, picklists, job templates, spare parts
    │   ├── DWGImportCommands.cs        # CAD import with layer mapping: preview, auto-detect, 18-category pattern recognition
    │   ├── IoTMaintenanceCommands.cs   # Asset condition (ISO 15686), maintenance scheduling, digital twin sync, energy analysis
    │   ├── MEPCreationCommands.cs      # MEP equipment placement: HVAC, electrical, plumbing, fire, conduit, cable tray
    │   ├── MEPScheduleCommands.cs      # 7 MEP schedules: panel, fixture, device, equipment, system, takeoff, sizing check
    │   ├── ModelCreationCommands.cs    # Programmatic BIM element creation: walls, floors, ceilings, roofs, columns, beams, stairs
    │   ├── OperationsCommands.cs       # Workflow & batch operations: preset sequences, PDF/IFC/COBie export, clash detection
    │   ├── RoomSpaceCommands.cs        # Room audit, department assignment, room schedule with tag integration
    │   └── StandardsEngine.cs          # Standards compliance: ISO 19650, CIBSE, BS 7671, Uniclass 2015, BS 8300, Part L
    │
    ├── Docs/Templates/                  # Template engine v1.1 — MiniWord + ClosedXML pipeline (14 files, ~1,900 lines)
    │   ├── TemplateManifest.cs         # POCOs + TemplateManifestIO (Load/Save/CreateDefault) + Validator
    │   ├── DocumentIdentityGenerator.cs # Next / Preview / PeekNext / Reserve (bulk) over doc_sequences.json
    │   ├── TokenContext.cs             # Doc/Project/People/Transmittal/Loops flattener + TransmittalRequest DTOs
    │   ├── TokenResolver.cs            # FindAllTokens + Resolve + EvaluateIf + loop helpers
    │   ├── MiniWordAdapter.cs          # Pre-process {{#if}} → MiniWord → post-process {{link:}} + core props
    │   ├── LegacyDocxRenderer.cs       # OpenXml safety-net used when manifest.use_legacy_renderer=true
    │   ├── XlsxTemplateRenderer.cs     # ClosedXML row-loop expansion with style preservation
    │   ├── TemplateRegistry.cs         # ResolveById / ResolveByPurpose / ValidateAll
    │   ├── TemplateEngine.cs           # Façade: .docx→MiniWord, .xlsx→ClosedXML, writes _BIM_COORD/generated/
    │   ├── DeliverableLifecycle.cs     # Issue/ReIssue/Publish/Cancel/Supersede/Replace state machine
    │   ├── TransmittalOrchestrator.cs  # Id → context → render → persist → workflow start → audit
    │   ├── EmbeddedTemplates.cs        # Extracts 16 templates + 5 workflows + manifest on first open
    │   ├── DeliverableLifecycleCommands.cs # 6 IExternalCommands (Issue/ReIssue/Publish/Cancel/Supersede/Replace)
    │   └── TransmittalCommands.cs      # CreateTransmittalOrchestratedCommand + BulkIssueDeliverablesCommand
    │
    ├── Docs/Workflow/                   # Template engine v1.1 — workflow + audit + distribution (6 files, ~700 lines)
    │   ├── WorkflowDefinition.cs       # POCOs: WorkflowDefinition/State/Transition/Escalation/Instance/HistoryRow
    │   ├── WorkflowRegistry.cs         # Loads every JSON under _BIM_COORD/workflows/
    │   ├── WorkflowEngine.cs           # Start / Transition / GetInstance / GetMyQueue / CheckSlaBreaches
    │   ├── SlaScanner.cs               # Opportunistic SLA checker (BCC open / tab switch / dispatch)
    │   ├── AuditLog.cs                 # Append-only JSONL with SHA-256 tamper chain + VerifyChain
    │   └── DistributionGroups.cs       # LoadAll/Save/SuggestFor(deliverable) via type/role/suit matching
    │
    ├── Docs/Search/                     # Template engine v1.1 — Lucene document index (2 files, ~360 lines)
    │   ├── DocumentIndex.cs            # Lucene.NET 4.8 FSDirectory over document_register + deliverables
    │   └── SearchQueryBuilder.cs       # Fluent facet builder + SavedSearch + SavedSearchStore
    │
    ├── Docs/_template_sources/          # Template engine v1.1 — 16 embedded templates (EmbeddedResource)
    │   ├── deliverable_standard.docx   # A01 — default deliverable cover sheet
    │   ├── deliverable_cancelled.docx  # A02 — cancellation notice
    │   ├── deliverable_superseded.docx # A03 — superseded notice
    │   ├── deliverable_replacing.docx  # A04 — replacing notice
    │   ├── deliverable_tabular.xlsx    # A05 — tabular deliverable (styled xlsx)
    │   ├── transmittal.docx            # B06 — transmittal memo
    │   ├── technical_query.docx        # B07 — TQ
    │   ├── rfi.docx                    # B08 — RFI
    │   ├── technical_response.docx     # B09 — response cross-linked via doc.supersedes
    │   ├── material_requisition.docx   # C10 — MR (largest, scope_items + supplier_docs loops)
    │   ├── submittal_cover.docx        # C11 — submittal cover
    │   ├── variation.docx              # C12 — variation / change order
    │   ├── letter_transmittal.docx     # C13 — formal letter of transmittal
    │   ├── meeting_minutes.docx        # D14 — attendees / agenda / discussion / actions
    │   ├── progress_report.docx        # D15 — period summary + holds + queries + SLA
    │   └── handover_certificate.docx   # D16 — tri-party signature block
    │
    ├── Docs/_workflow_sources/          # Template engine v1.1 — 5 embedded workflow JSONs (EmbeddedResource)
    │   ├── transmittal_default.json    # Draft → Issued → Acknowledged (24/72/∞ hr SLA)
    │   ├── rfi_default.json            # Open → Reviewed → Responded → Closed
    │   ├── tq_default.json             # Open → Answered
    │   ├── mr_default.json             # Draft → Submitted → Approved/Rejected
    │   └── deliverable_issue_default.json # WIP → Shared → Published → Archived
    │
    └── Data/                           # Runtime data files (49 files)
        ├── BLE_MATERIALS.csv           # 815 building-element materials
        ├── MEP_MATERIALS.csv           # 464 MEP materials
        ├── MR_PARAMETERS.txt           # Shared parameter file (3,236 params, 34 groups, all data files cross-referenced — Phase 187-190 alignment incl. EC1-1-4 wind + EC8 seismic regional defaults + black-cotton drawing warning)
        ├── MR_PARAMETERS.csv           # Parameter definitions (CSV mirror of TXT, v6.6 — 3,236 rows, identical param set as TXT)
        ├── MR_SCHEDULES.csv            # 168 schedule definitions
        ├── MATERIAL_SCHEMA.json        # 77-column material schema (v2.3)
        ├── MATERIAL_LOOKUP.csv         # 237-row material reference database (density, thermal, fire rating, acoustic, embodied carbon, cost)
        ├── FORMULAS_WITH_DEPENDENCIES.csv  # 199 parameter formulas
        ├── SCHEDULE_FIELD_REMAP.csv    # 50+ field deprecation remaps
        ├── BINDING_COVERAGE_MATRIX.csv # Parameter-category coverage
        ├── BOQ_TEMPLATE.csv            # Bill of Quantities template structure
        ├── CATEGORY_BINDINGS.csv       # 10,661 category bindings
        ├── FAMILY_PARAMETER_BINDINGS.csv   # 4,686 family bindings
        ├── PARAMETER_CATEGORIES.csv    # Parameter-category cross-reference
        ├── PARAMETER_REGISTRY.json     # Master parameter registry — single source of truth for ParamRegistry.cs
        ├── LABEL_DEFINITIONS.json      # Label/legend definition specs (v5.6, 149 categories, 1:1 aligned with TagFamilyCreator — Phase 187)
        ├── TAG_CONFIG_v5_0_CONTAINERS.csv    # 122+ tag container definitions (v5.0) + Section 13: tie-in point containers (10 params + 4 TAG7 containers + 6 tag families)
        ├── TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv # 179+ discipline/system/function code mappings (v5.0) + Section 7: 14 tie-in system mappings
        ├── TAG_CONFIG_v5_0_VALIDATION.csv    # 180+ validation rules for tag tokens (v5.0) + Section 13: 13 tie-in validation rules
        ├── STING_TAG_CONFIG_v5_0_ARCH.csv    # Architectural tag family definitions (v5.0, 111 warnings across 33 tag families)
        ├── STING_TAG_CONFIG_v5_0_GEN.csv     # General tag family definitions (v5.0)
        ├── STING_TAG_CONFIG_v5_0_MEP.csv     # MEP tag family definitions (v5.0, 183 warnings across 51 tag families including 6 tie-in point tags #46-#51)
        ├── STING_TAG_CONFIG_v5_0_STR.csv     # Structural tag family definitions (v5.0)
        ├── STRUCTURAL_EXCEL_TEMPLATE.csv # Structural Excel import template (6 sheet formats: columns, beams, slabs, foundations, walls, rebar)
        ├── PROJECT_TEAM_TEMPLATE.json  # Project team role/discipline template
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide (original)
        ├── TAG GUIDE V2.xlsx           # Tag reference guide (comprehensive update)
        ├── TAG_GUIDE_V3.csv            # Tag reference guide (newest CSV version)
        ├── TAG_PLACEMENT_PRESETS_DEFAULT.json  # Default tag placement preset (12 category rules)
        ├── TAG_STYLE_RULES.json        # Tag style engine rules (v2.0, 128 type combinations, discipline presets)
        ├── WORKFLOW_DailyQA_Enhanced.json     # Enhanced Daily QA workflow (8 conditional steps)
        ├── WORKFLOW_MorningHealthCheck.json   # Morning health check workflow (10 adaptive steps)
        ├── WORKFLOW_WeeklyDataDrop.json       # Weekly ISO 19650 data drop workflow (10 steps)
        ├── cost_rates_5d.csv            # 5D cost rates (7-col: Category, MAT_CODE, MAT_DISCIPLINE, rates, Unit, Description)
        ├── VALIDAT_BIM_TEMPLATE.py     # BIM template validation (45 checks, ported to C#)
        ├── COBIE_TYPE_MAP.csv          # 70+ equipment types with STING token mapping
        ├── COBIE_SYSTEM_MAP.csv        # 31 building system mappings with Uniclass/CIBSE codes
        ├── COBIE_PICKLISTS.csv         # COBie V2.4 controlled vocabularies (124 entries)
        ├── COBIE_ATTRIBUTE_TEMPLATES.csv # Expected attributes per COBie entity type (82 entries)
        ├── COBIE_JOB_TEMPLATES.csv     # SFG20/BS 8210 maintenance job templates (47 entries)
        ├── COBIE_SPARE_PARTS.csv       # Spare parts per equipment type (38 entries)
        ├── COBIE_DOCUMENT_TYPES.csv    # 28 O&M document types with regulatory references
        ├── COBIE_ZONE_TYPES.csv        # 16 zone type classifications (fire, HVAC, lighting, acoustic, etc.)
        ├── project_bep.json            # Project-specific BEP configuration template
        ├── TAGGING_GUIDE.md            # Complete tagging guide documentation (1,300+ lines)
        ├── TAG_FAMILY_CREATION_GUIDE.md # End-to-end tag family creation workflow guide
        ├── BIM_COORDINATION_WORKFLOW_GUIDE.md # Comprehensive BIM coordinator workflow guide (2,000+ lines, 22 sections)
        └── DWG_TO_BIM_GUIDE.md        # DWG-to-structural BIM conversion guide
```

## Ribbon UI Architecture (Legacy)

> **Note:** The ribbon panels have been superseded by the WPF dockable panel (see [WPF Dockable Panel](#wpf-dockable-panel-primary-ui) section). The ribbon is retained for compatibility but the dockable panel is the primary user interface. All commands are accessible from both surfaces.

### Select Panel (3 pulldowns + 1 button + Color pulldown)
| Group | Commands | Description |
|-------|----------|-------------|
| Category | 16 selectors (Lighting, Electrical, Mechanical, Plumbing, Air Terminals, Furniture, Doors, Windows, Rooms, Sprinklers, Pipes, Ducts, Conduits, Cable Trays, ALL Taggable, Custom Category) | Select elements by Revit category in active view |
| State | Untagged, Tagged, Empty Mark, Pinned, Unpinned, **Stale**, **Quick Tag Preview** | Select by tag/pin/mark state + stale detection + tag preview |
| Spatial | By Level, By Room | Select by spatial criteria |
| Bulk Param | `Select.BulkParamWriteCommand` | Multi-page bulk operations: set LOC/ZONE/STATUS, auto-populate all tokens, clear tags, or re-tag with overwrite |

**Color pulldown (5 commands — NEW):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Color By Parameter | `Select.ColorByParameterCommand` | Manual | Color elements by any parameter value with 10 built-in palettes, `<No Value>` detection |
| Clear Color Overrides | `Select.ClearColorOverridesCommand` | Manual | Remove all graphic overrides from active view |
| Save Color Preset | `Select.SaveColorPresetCommand` | ReadOnly | Save current color scheme as JSON preset to `COLOR_PRESETS.json` |
| Load Color Preset | `Select.LoadColorPresetCommand` | Manual | Load and apply a saved color preset |
| Create Filters from Colors | `Select.CreateFiltersFromColorsCommand` | Manual | Convert color scheme into persistent Revit `ParameterFilterElement` rules |

### Docs Panel (4 buttons + Viewports pulldown + Automation pulldown + View Automation pulldown)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| Sheet Organizer | `Docs.SheetOrganizerCommand` | Manual | Group sheets by discipline prefix |
| View Organizer | `Docs.ViewOrganizerCommand` | ReadOnly | Organize views by type, report placed/unplaced |
| Sheet Index | `Docs.SheetIndexCommand` | Manual | Create "STING - Sheet Index" schedule |
| Document Transmittal | `Docs.TransmittalCommand` | ReadOnly | ISO 19650 transmittal report |

**Viewports pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Align Viewports | `Docs.AlignViewportsCommand` | Manual | Align viewports on sheet (top/left/center H/V) |
| Renumber Viewports | `Docs.RenumberViewportsCommand` | Manual | Renumber viewports left-to-right, top-to-bottom |
| Text Case | `Docs.TextCaseCommand` | Manual | Convert text notes to UPPER/lower/Title case (preserves BIM acronyms) |
| Sum Areas | `Docs.SumAreasCommand` | ReadOnly | Calculate total area of selected/all rooms |

**Automation pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Delete Unused Views | `Docs.DeleteUnusedViewsCommand` | Manual | Remove views not placed on any sheet (with confirmation and protection) |
| Sheet Naming Check | `Docs.SheetNamingCheckCommand` | ReadOnly | ISO 19650 sheet naming compliance audit with correction suggestions |
| Auto-Number Sheets | `Docs.AutoNumberSheetsCommand` | Manual | Sequentially renumber sheets within discipline groups |
| FM Handover Manual | `Docs.HandoverManualCommand` | ReadOnly | Generate FM handover manual with asset register, spatial summary, system descriptions, and compliance report |

**View Automation pulldown (6 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Duplicate View | `Docs.DuplicateViewCommand` | Manual | Duplicate view with Detailing, View-only, or Dependent mode |
| Batch Rename Views | `Docs.BatchRenameViewsCommand` | Manual | Single-step dialog with category/family filters, 7 operations, live preview |
| Copy View Settings | `Docs.CopyViewSettingsCommand` | Manual | Copy filters, graphic overrides, and template from source view |
| Auto-Place Viewports | `Docs.AutoPlaceViewportsCommand` | Manual | Auto-place and scale viewports on sheets |
| Crop to Content | `Docs.CropToContentCommand` | Manual | Smart crop region generation based on element extents |
| Batch Align Viewports | `Docs.BatchAlignViewportsCommand` | Manual | Multi-view alignment on sheets |

**Sheet Manager pulldown (24 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Sheet Manager | `Docs.SheetManagerCommand` | Manual | Open dual-panel WPF sheet manager dialog |
| Auto Layout | `Docs.AutoLayoutCommand` | Manual | Auto-arrange viewports using shelf-packing |
| Clone Sheet | `Docs.CloneSheetCommand` | Manual | Clone sheet with viewports |
| Place Unplaced | `Docs.PlaceUnplacedViewsCommand` | Manual | Place unplaced views on sheets |
| Optimal Scale | `Docs.OptimalScaleCommand` | Manual | Calculate optimal viewport scale |
| Sheet Audit | `Docs.SheetAuditCommand` | ReadOnly | Audit sheets for issues |
| Batch Arrange | `Docs.BatchArrangeCommand` | Manual | Auto-arrange across multiple sheets |
| Move Viewport | `Docs.MoveViewportCommand` | Manual | Move viewport between sheets |
| MaxRects Layout | `Docs.MaxRectsLayoutCommand` | Manual | Layout using MaxRects bin packing |
| Save Layout Preset | `Docs.SaveLayoutPresetCommand` | ReadOnly | Save layout as named preset |
| Apply Layout Preset | `Docs.ApplyLayoutPresetCommand` | Manual | Apply saved layout preset |
| Batch Clone | `Docs.BatchCloneSheetsCommand` | Manual | Clone multiple sheets |
| Batch Renumber | `Docs.BatchRenumberSheetsCommand` | Manual | Two-pass renumber within discipline |
| Auto VP Types | `Docs.AutoAssignVPTypesCommand` | Manual | Auto-assign viewport types |
| Export Sheet Set | `Docs.ExportSheetSetCommand` | ReadOnly | Export sheet set to CSV |
| Place With Overflow | `Docs.PlaceWithOverflowCommand` | Manual | Place views with overflow sheets |
| Create From Template | `Docs.CreateFromTemplateCommand` | Manual | Create sheet from template |
| Save Template | `Docs.SaveSheetTemplateCommand` | ReadOnly | Save sheet as template |
| ISO Compliance | `Docs.SheetComplianceCheckCommand` | ReadOnly | ISO 19650 compliance audit |
| Grid Align | `Docs.GridAlignViewportsCommand` | Manual | Snap viewports to grid |
| Align Edges | `Docs.AlignViewportEdgesCommand` | Manual | Align viewport edges |
| Distribute | `Docs.DistributeViewportsCommand` | Manual | Distribute viewports evenly |
| Batch Print | `Docs.BatchPrintSheetsCommand` | ReadOnly | Export sheets to PDF |
| Sheet Register | `Docs.ExportSheetRegisterCommand` | ReadOnly | Export sheet register CSV |

### Tags Panel (3 buttons + More/Setup/Tokens/QA pulldowns)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| Auto Tag | `Tags.AutoTagCommand` | Manual | Tag elements in active view with spatial auto-detect, collision mode selection (skip/overwrite/increment) |
| Batch Tag | `Tags.BatchTagCommand` | Manual | Tag all elements in entire project with collision mode selection and spatial auto-detect |
| Tag & Combine | `Tags.TagAndCombineCommand` | Manual | One-click: auto-detect LOC/ZONE + populate tokens + tag + combine ALL 36 containers (view/selection/project scope) |

**More pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag New Only | `Tags.TagNewOnlyCommand` | Manual | Tag only new/untagged elements with spatial auto-detect and family-aware PROD codes |
| Re-Tag Selected | `Organise.ReTagCommand` | Manual | Force re-derive and overwrite tags on selected elements |
| Fix Duplicates | `Organise.FixDuplicateTagsCommand` | Manual | Auto-resolve duplicate tags by assigning new unique SEQ numbers |
| Pre-Tag Audit | `Tags.PreTagAuditCommand` | ReadOnly | Dry-run audit: predict tag assignments, collisions, ISO violations BEFORE committing |
| Family-Stage Populate | `Tags.FamilyStagePopulateCommand` | Manual | Pre-populate all 7 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) from category and spatial data |

**Setup pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Config | `Tags.TagConfigCommand` | ReadOnly | Display/configure lookup tables |
| Load Params | `Tags.LoadSharedParamsCommand` | Manual | 2-pass shared parameter binding |
| Configure | `Tags.ConfigEditorCommand` | ReadOnly | View/edit/save/reload project_config.json |

**Tokens pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Set Discipline | `Tags.SetDiscCommand` | Manual | Set DISC token (M, E, P, A) |
| Set Location | `Tags.SetLocCommand` | Manual | Set LOC token (BLD1, BLD2, BLD3, EXT) |
| Set Zone | `Tags.SetZoneCommand` | Manual | Set ZONE token (Z01-Z04) |
| Set Status | `Tags.SetStatusCommand` | Manual | Set STATUS token (EXISTING, NEW, DEMOLISHED, TEMPORARY) |
| Assign Numbers | `Tags.AssignNumbersCommand` | Manual | Sequential numbering by DISC/SYS/LVL (continues from max existing) |
| Build Tags | `Tags.BuildTagsCommand` | Manual | Rebuild ASS_TAG_1 from existing tokens |
| Combine Parameters | `Tags.CombineParametersCommand` | Manual | Interactive multi-mode combine into all tag containers |

**QA pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Validate | `Tags.ValidateTagsCommand` | ReadOnly | Validate tag completeness with ISO 19650 code validation (DISC, LOC, ZONE, SYS, FUNC, PROD, SEQ) |
| Find Duplicates | `Organise.FindDuplicateTagsCommand` | ReadOnly | Find duplicate tag values, select affected elements |
| Highlight Invalid | `Organise.HighlightInvalidCommand` | Manual | Colour-code missing (red) and incomplete (orange) tags |
| Clear Overrides | `Organise.ClearOverridesCommand` | Manual | Reset graphic overrides in active view |
| Completeness Dashboard | `Tags.CompletenessDashboardCommand` | ReadOnly | Per-discipline compliance dashboard with percentage |

**Smart Tag Placement pulldown (9 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Smart Place Tags | `Tags.SmartPlaceTagsCommand` | Manual | Priority-based `IndependentTag` placement with 8-position collision avoidance |
| Arrange Tags | `Tags.ArrangeTagsCommand` | Manual | Auto-arrange placed tags into aligned grid patterns |
| Remove Annotation Tags | `Tags.RemoveAnnotationTagsCommand` | Manual | Remove all `IndependentTag` annotations from view |
| Batch Place Tags | `Tags.BatchPlaceTagsCommand` | Manual | Place visual annotation tags across multiple views |
| Learn Placement | `Tags.LearnTagPlacementCommand` | ReadOnly | Analyze existing tag placements in view to learn rules |
| Apply Tag Template | `Tags.ApplyTagTemplateCommand` | Manual | Apply saved placement template to view |
| Tag Overlap Analysis | `Tags.TagOverlapAnalysisCommand` | ReadOnly | Detect and report overlapping tags in view |
| Batch Tag Text Size | `Tags.BatchTagTextSizeCommand` | Manual | Set text size for all tags in view/selection |
| Set Category Line Weight | `Tags.SetTagCategoryLineWeightCommand` | Manual | Set line weight for tag annotations by category |

### Organise Panel (6 pulldowns: Tag Ops + Leaders + Analysis + Annotation Color + Tag Appearance + Tag Type)
**Tag Ops pulldown (7 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Selected | `Organise.TagSelectedCommand` | Manual | Tag selected elements only |
| Delete Tags | `Organise.DeleteTagsCommand` | Manual | Clear all 15 tag params from selection (with confirmation) |
| Renumber | `Organise.RenumberTagsCommand` | Manual | Re-sequence tags within (DISC, SYS, LVL) groups |
| Copy Tags | `Organise.CopyTagsCommand` | Manual | Copy tag values from first selected to all others (excludes SEQ) |
| Swap Tags | `Organise.SwapTagsCommand` | Manual | Swap all tag values between exactly 2 selected elements |
| Re-Tag | `Organise.ReTagCommand` | Manual | Force re-derive and overwrite all tag tokens on selected elements |
| Fix Duplicates | `Organise.FixDuplicateTagsCommand` | Manual | Auto-resolve duplicate tags by incrementing SEQ numbers |

**Leaders pulldown (14 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Toggle Leaders | `Organise.ToggleLeadersCommand` | Manual | Toggle leaders on/off for selected tags (or all tags in view) |
| Add Leaders | `Organise.AddLeadersCommand` | Manual | Add leaders to all selected annotation tags |
| Remove Leaders | `Organise.RemoveLeadersCommand` | Manual | Remove leaders from all selected annotation tags |
| Align Tags | `Organise.AlignTagsCommand` | Manual | Align tag heads horizontally, vertically, or in a row |
| Reset Tag Positions | `Organise.ResetTagPositionsCommand` | Manual | Move tags back to element centers (remove manual offsets) |
| Toggle Orientation | `Organise.ToggleTagOrientationCommand` | Manual | Switch selected tags between horizontal and vertical |
| Snap Leader Elbows | `Organise.SnapLeaderElbowCommand` | Manual | Snap leader elbows to 45 or 90 degree angles for clean layout |
| Auto-Align Leader Text | `Organise.AutoAlignLeaderTextCommand` | Manual | Auto-align leader text to consistent positions |
| Flip Tags | `Organise.FlipTagsCommand` | Manual | Mirror tag position across element center (left/right, up/down) |
| Align Tag Text | `Organise.AlignTagTextCommand` | Manual | Align annotation text (left/center/right) for tags and text notes |
| Pin/Unpin Tags | `Organise.PinTagsCommand` | Manual | Lock tags in place to prevent accidental movement (or unlock) |
| Nudge Tags | `Organise.NudgeTagsCommand` | Manual | Fine-adjust tag positions by small increments |
| Attach/Free Leader | `Organise.AttachLeaderCommand` | Manual | Attach leader end to host element or set free |
| Select Tags By Leader | `Organise.SelectTagsWithLeadersCommand` | ReadOnly | Select tags with or without leaders in active view |

**Analysis pulldown (7 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Audit to CSV | `Organise.AuditTagsCSVCommand` | ReadOnly | Export full tag audit to CSV file |
| Find Duplicates | `Organise.FindDuplicateTagsCommand` | ReadOnly | Find duplicate tag values, select affected elements |
| Highlight Invalid | `Organise.HighlightInvalidCommand` | Manual | Colour-code missing (red) and incomplete (orange) tags |
| Clear Overrides | `Organise.ClearOverridesCommand` | Manual | Reset graphic overrides in active view |
| Select by Discipline | `Organise.SelectByDisciplineCommand` | ReadOnly | Select all elements of a specific discipline code |
| Tag Statistics | `Organise.TagStatsCommand` | ReadOnly | Quick tag counts by discipline/system/level for active view |
| Tag Register Export | `Organise.TagRegisterExportCommand` | ReadOnly | Comprehensive asset register export (40+ columns: tags, identity, spatial, MEP, cost, validation) |

**Annotation Color pulldown (5 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Color Tags by Discipline | `Organise.ColorTagsByDisciplineCommand` | Manual | Colour-code annotation tags by discipline |
| Set Tag Text Color | `Organise.SetTagTextColorCommand` | Manual | Set text color for selected annotation tags |
| Set Leader Color | `Organise.SetLeaderColorCommand` | Manual | Set leader line color for selected tags |
| Split Tag/Leader Color | `Organise.SplitTagLeaderColorCommand` | Manual | Apply different colors to leader vs tag text |
| Clear Annotation Colors | `Organise.ClearAnnotationColorsCommand` | Manual | Clear all annotation color overrides in view |

**Tag Appearance pulldown (5 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Appearance | `Organise.TagAppearanceCommand` | Manual | Configure overall tag visual appearance |
| Set Tag Box Appearance | `Organise.SetTagBoxAppearanceCommand` | Manual | Set tag box/border appearance settings |
| Quick Tag Style | `Organise.QuickTagStyleCommand` | Manual | Apply quick tag style presets |
| Set Tag Line Weight | `Organise.SetTagLineWeightCommand` | Manual | Set line weight for tag annotations |
| Color Tags by Parameter | `Organise.ColorTagsByParameterCommand` | Manual | Color-code tags by any parameter value |

**Tag Type pulldown (1 command):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Swap Tag Type | `Organise.SwapTagTypeCommand` | Manual | Swap tag family type on selected annotation tags |

### Temp Panel (9 pulldown groups, 73 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup**, **★ Project Setup Wizard** | Project setup + one-click automation (15-step workflow) + 7-page WPF wizard (4 commands) |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes (FamilyCommands.cs), Cable Trays, Conduits (TemplateExtCommands.cs) | Type creation from CSV data (8 commands) |
| Schedules | **Full Auto-Populate**, Batch Create, Auto-Populate (Tokens Only), Evaluate Formulas, Export CSV, Corporate Title Block Schedule, Drawing Register Schedule | Schedule creation + zero-input automation (7 commands) |
| **Schedule Mgr** | Audit, Compare, Duplicate, Refresh, Field Manager, Colors, Stats, Delete, Report | Deep schedule management + formatting (9 commands) |
| Templates | Create Filters, Apply Filters to Views, Create Worksets, View Templates, Line Patterns, Phases, Material Schedules | Template pipeline + 28 filters, 35 worksets, 23 templates, 10 line patterns, 6 phases (7 commands) |
| **Template Mgr** | Auto-Assign Templates, Template Audit, Template Diff, Compliance Scores, Auto-Fix Templates, Sync VG Overrides, Clone Template, Batch VG Reset, Batch Family Params, Template Schedules, Template Setup Wizard, Family Parameter Processor | 5-layer intelligence template management (12 commands) |
| **Styles** | Fill Patterns, Line Styles, Object Styles, Text Styles, Dimension Styles, VG Overrides | ISO-standard style creation (6 commands) |
| **Data Pipeline** | Validate Template, Dynamic Bindings, Schema Validate, BOQ Export, Template VG Audit, IFC Export, IFC Property Map, BEP Compliance, Clash Detection, Excel BOQ Import, Keynote Sync | Data integrity + IFC + BEP + clash detection + Excel import (11 commands) |

### Panel Panel (1 button)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| STING Panel | `Core.ToggleDockPanelCommand` | ReadOnly | Show/hide the STING Tools WPF dockable panel |

## Command Count by File

| File | Commands | Lines |
|------|----------|-------|
| `Core/StingToolsApp.cs` | 1 (ToggleDockPanelCommand) + IExternalApplication + DocumentOpened quality gate | 418 |
| `Core/StingLog.cs` | 0 (logger + EscapeChecker utility) | 127 |
| `Core/ParamRegistry.cs` | 0 (parameter registry infrastructure + stale/cluster/display/position constants) | 1,908 |
| `Core/ParameterHelpers.cs` | 0 (helpers + TokenAutoPopulator + PhaseAutoDetect + TypeTokenInherit + CopyTokensFromNearest + ConnectorInherit + GetInt) | 2,202 |
| `Core/SharedParamGuids.cs` | 0 (backwards-compatible facade) | 228 |
| `Core/TagConfig.cs` | 0 (tag config + ISO validator + TAG7 builder + SeqScheme + BuildDisplayTag + ValidationError enum) | 5,193 |
| `Core/StingAutoTagger.cs` | 3 (AutoTaggerToggle, AutoTaggerToggleVisual, AutoTaggerConfig) + StingAutoTagger IUpdater + StingStaleMarker IUpdater | 736 |
| `Core/WorkflowEngine.cs` | 4 (WorkflowPreset, ListPresets, CreatePreset, WorkflowTrend) + WorkflowEngine + WorkflowRunRecord + JSONL log rotation | 880 |
| `Core/ComplianceScan.cs` | 0 (cached compliance scan + per-discipline DiscComplianceData + torn-read fix) | 219 |
| `Core/OutputLocationHelper.cs` | 0 (centralized output directory management with fallback chain) | 222 |
| `Core/IPanelCommand.cs` | 0 (interface + SafeApp/SafeDoc/SafeUIDoc extension methods) | 64 |
| `Core/PerformanceTracker.cs` | 0 (performance profiling engine, per-element timing, CSV export) | 267 |
| `Select/CategorySelectCommands.cs` | 16 (14 category selectors + SelectAllTaggable + SelectCustomCategory) | 322 |
| `Select/StateSelectCommands.cs` | 10 (5 state + 2 spatial + BulkParamWrite + SelectStale + QuickTagPreview) | 835 |
| `Select/ColorCommands.cs` | 5 (ColorByParameter, ClearOverrides, SavePreset, LoadPreset, CreateFilters) + ColorHelper | 922 |
| `Select/TagSelectorCommands.cs` | 1+ (multi-criteria tag selector: text, size, arrowhead, leader, family, host, orientation, discipline) | 1,119 |
| `Docs/SheetOrganizerCommand.cs` | 1 | 103 |
| `Docs/ViewOrganizerCommand.cs` | 1 | 93 |
| `Docs/SheetIndexCommand.cs` | 1 | 78 |
| `Docs/TransmittalCommand.cs` | 1 | 124 |
| `Docs/ViewportCommands.cs` | 4 (Align, Renumber, TextCase, SumAreas) | 412 |
| `Docs/DocAutomationCommands.cs` | 3 (DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets) | 459 |
| `Docs/DocAutomationExtCommands.cs` | 12 (BatchViews, BatchSheets, DependentViews, ScopeBox, ViewTemplate, DocPackage, Sections, Elevations, DrawingRegister, BrowserOrganizer, RevisionCloudAutoCreate, HandoverManual) | 3,168 |
| `Docs/ViewAutomationCommands.cs` | 8 (DuplicateView, BatchRename, CopySettings, AutoPlace, CropToContent, BatchAlign, MagicRename, ViewTabColour) | 1,200 |
| `Docs/HandoverExportCommands.cs` | 5+ (COBie 2.4 export, maintenance schedule, O&M manual, asset health report, space handover) | 1,316 |
| `Docs/JournalParserCommand.cs` | 1 (Revit journal diagnostics: error/crash/command/memory analysis) | 494 |
| `Docs/SheetManagerEngine.cs` | 0 (core engine: drawable zone, scale calc, shelf packing, collision, viewport placement, sheet cloning, auto-arrange) | 1,041 |
| `Docs/SheetManagerEngineExt.cs` | 0 (MaxRects packer, layout presets, viewport type rules, batch clone/renumber, overflow) | 943 |
| `Docs/SheetManagerCommands.cs` | 8 (SheetManager, AutoLayout, CloneSheet, PlaceUnplaced, OptimalScale, SheetAudit, BatchArrange, MoveViewport) | 849 |
| `Docs/SheetManagerDialog.cs` | 0 (WPF dual-panel sheet manager dialog with TreeView and detail views) | 830 |
| `Docs/SheetSetCommands.cs` | 8 (MaxRectsLayout, SaveLayoutPreset, ApplyLayoutPreset, BatchClone, BatchRenumber, AutoAssignVPTypes, ExportSheetSet, PlaceWithOverflow) | 548 |
| `Docs/SheetTemplateEngine.cs` | 0 (sheet templates, ISO 19650 compliance, grid alignment, distribution, PDF export) | 858 |
| `Docs/SheetTemplateCommands.cs` | 8 (CreateFromTemplate, SaveSheetTemplate, SheetComplianceCheck, GridAlignViewports, AlignViewportEdges, DistributeViewports, BatchPrintSheets, ExportSheetRegister) | 419 |
| `Tags/AutoTagCommand.cs` | 2 (AutoTag, TagNewOnly) | 355 |
| `Tags/BatchTagCommand.cs` | 3 (BatchTag, TagFormatMigration, TagChanged) | 621 |
| `Tags/TagAndCombineCommand.cs` | 1 | 235 |
| `Tags/PreTagAuditCommand.cs` | 1 (+ auto-fix chain: AnomalyAutoFix → ResolveAllIssues) | 530 |
| `Tags/FamilyStagePopulateCommand.cs` | 1 | 379 |
| `Tags/CombineParametersCommand.cs` | 3 (CombineParameters, CombinePreFlight, ContainerPreCheck) | 536 |
| `Tags/ConfigEditorCommand.cs` | 1 | 211 |
| `Tags/TagConfigCommand.cs` | 1 | 63 |
| `Tags/LoadSharedParamsCommand.cs` | 1 | 344 |
| `Tags/TokenWriterCommands.cs` | 8 (SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, CompletenessDashboard, SetSeqScheme) | 708 |
| `Tags/ValidateTagsCommand.cs` | 1 | 499 |
| `Tags/SmartTagPlacementCommand.cs` | 16 (SmartPlace, Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight, AlignTagBands, SwitchTagPosition, ExportTagPositions, BatchPlaceLinkedTags, ExportLinkedManifest, AdjustElbows, SetArrowheadStyle) + TagPlacementEngine + leader elbow avoidance | 3,194 |
| `Tags/TagFamilyCreatorCommand.cs` | 4 (CreateTagFamilies, LoadTagFamilies, ConfigureTagLabels, AuditTagFamilies) | 1,729 |
| `Tags/SyncParameterSchemaCommand.cs` | 3 (SyncParameterSchema, AddParamRemap, AuditParameterSchema) | 661 |
| `Tags/LegendBuilderCommands.cs` | 31 (color/tag/discipline/system/material/equipment/fire/template legends + master pipeline) + LegendEngine | 7,110 |
| `Tags/RichTagDisplayCommands.cs` | 6 (RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments) | 1,105 |
| `Tags/SystemParamPushCommand.cs` | 3 (SystemParamPush, BatchSystemPush, SelectSystemElements) | 916 |
| `Tags/ResolveAllIssuesCommand.cs` | 1 (one-click ISO 19650 resolution, 500-element batched) | 382 |
| `Tags/PresentationModeCommand.cs` | 4 (SetPresentationMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle) | 926 |
| `Tags/ParagraphDepthCommand.cs` | 2 (SetParagraphDepth, ToggleWarningVisibility) | 213 |
| `Tags/TagStyleCommands.cs` | 10 (ApplyTagStyle, ApplyColorScheme, ClearColorScheme, SetParagraphDepthExt, TagStyleReport, SwitchTagStyleByDisc, BatchApplyColorScheme, ColorByVariable, SetBoxColor, SetViewTagStyle) | 817 |
| `Tags/FamilyParamCreatorCommand.cs` | 1 (FamilyParamCreator) + FamilyParamEngine | 934 |
| `Tags/TagStyleEngine.cs` | 0 (tag style engine: presets, color schemes, paragraph depth) | 1,007 |
| `Organise/TagOperationCommands.cs` | 47 (7 Tag Ops + 14 Leaders + 7 Analysis + 5 Annotation Color + 5 Tag Appearance + 1 SwapTagType + 1 AnomalyAutoFix + 2 Clustering + 1 DisplayMode + 1 DiscCompliance + 1 RetagStale + 1 DeclusterTags + LeaderHelper + AnnotationColorHelper) | 5,298 |
| `Model/ModelCommands.cs` | 16 (Wall, Room, Floor, Ceiling, Roof, Door, Window, Column, ColumnGrid, Beam, Duct, Pipe, Fixture, BuildingShell, DWGToModel, DWGPreview) | 1,101 |
| `Model/ModelEngine.cs` | 0 (model creation engine + FamilyResolver + WorksetAssigner + FailureHandler) | 1,349 |
| `Model/CADToModelEngine.cs` | 0 (DWG-to-BIM conversion engine + LayerMapper) | 824 |
| `BIMManager/BIMManagerCommands.cs` | 37 (CreateBEP, UpdateBEP, ExportBEP, GenerateBEP, ProjectDashboard, RaiseIssue, IssueDashboard, UpdateIssue, DocumentRegister, AddDocument, COBieExport, CreateTransmittal, CDEStatus, ValidateDocNaming, ReviewTracker, SelectIssueElements, ISO19650Reference, BulkBIMExport, BriefcaseView, BriefcaseRead, BriefcaseAddFile, DocumentBriefcase, ElementStickyNote, ExportStickyNotes, SelectStickyElements, ModelHealthDashboard, ExportModelHealth, MidpTracker, Export4DTimeline, Export5DCostData, FullComplianceDashboard, LinkPredecessors, AssignPhaseDates, MeasuredQuantities, ElementCountSummary, SetOutputDirectory, StageComplianceGate) + BIMManagerEngine + COBiePreset (22 presets) | 7,147 |
| `BIMManager/ExcelLinkCommands.cs` | 6 (ExportToExcel, ImportFromExcel, ExcelRoundTrip, ExportSchedulesToExcel, ImportSchedulesFromExcel, ExportTemplate) + ExcelLinkEngine | 2,055 |
| `BIMManager/PlatformLinkCommands.cs` | 6 (ACCPublish, CDEPackage, BCFExport, BCFImport, PlatformSync, SharePointExport) + PlatformLinkEngine | 1,598 |
| `BIMManager/RevisionManagementCommands.cs` | 12 (CreateRevision, RevisionDashboard, AutoRevisionCloud, RevisionSchedule, TrackElementRevisions, RevisionCompare, IssueSheetsForRevision, RevisionNamingEnforce, RevisionTagIntegration, RevisionExport, BulkRevisionStamp, AutoRevisionOnTagChange) + RevisionEngine | 1,590 |
| `BIMManager/SchedulingCommands.cs` | 12 (AutoSchedule4D, ImportMSProject, ViewTimeline4D, ExportSchedule4D, AutoCost5D, ImportCostRates, CostReport5D, CashFlow5D, PhaseFilter, PhaseSummary, MilestoneRegister, WorkingCalendar) + Scheduling4DEngine | 1,713 |
| `Temp/CreateParametersCommand.cs` | 1 | 27 |
| `Temp/CheckDataCommand.cs` | 1 | 297 |
| `Temp/MasterSetupCommand.cs` | 1 | 306 |
| `Temp/ProjectSetupCommand.cs` | 1 (ProjectSetup — launches 7-page WPF wizard) | 1,104 |
| `Temp/MaterialCommands.cs` | 2 (BLE, MEP) + MaterialPropertyHelper | 783 |
| `Temp/FamilyCommands.cs` | 6 (Walls, Floors, Ceilings, Roofs, Ducts, Pipes) + CompoundTypeCreator | 723 |
| `Temp/ScheduleCommands.cs` | 6 (FullAutoPopulate, BatchSchedules, AutoPopulate, ExportCSV, CorporateTitleBlockSchedule, DrawingRegisterSchedule) + ScheduleHelper | 2,004 |
| `Temp/ScheduleEnhancementCommands.cs` | 9 (Audit, Compare, Duplicate, Refresh, FieldMgr, Color, Stats, Delete, Report) | 1,634 |
| `Temp/FormulaEvaluatorCommand.cs` | 1 (+ FormulaEngine + ExpressionParser + formula cache + unit conversion) | 1,085 |
| `Temp/TemplateCommands.cs` | 3 (Filters, Worksets, ViewTemplates) | 1,304 |
| `Temp/TemplateExtCommands.cs` | 6 (LinePatterns, Phases, ApplyFilters, CableTrays, Conduits, MaterialSchedules) | 316 |
| `Temp/TemplateManagerCommands.cs` | 18 (AutoAssign, Audit, Diff, Compliance, AutoFix, SyncOverrides, FillPatterns, LineStyles, ObjectStyles, TextStyles, DimStyles, VGOverrides, BatchFamilyParams, TemplateSchedules, SetupWizard, CloneTemplate, BatchVGReset, FamilyParameterProcessor) + TemplateManager engine | 3,893 |
| `Temp/DataPipelineCommands.cs` | 11 (ValidateTemplate, DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit, ExportIfcPropertyMap, ValidateBepCompliance, ClashDetection, IFCExport, ExcelBOQImport, KeynoteSync) | 3,013 |
| `Temp/DataPipelineEnhancementCommands.cs` | 5+ (cross-validation: registry vs CSV drift, parameter coverage, field remapping) | 645 |
| `Temp/AutoModelCommands.cs` | 2+ (DWG-to-BIM automation, link DWG/DXF, batch import) | 1,462 |
| `Temp/COBieDataCommands.cs` | 2+ (COBie reference data management, type map browser, picklists) | 1,533 |
| `Temp/DWGImportCommands.cs` | 2+ (CAD import with layer mapping, 18-category pattern recognition) | 1,612 |
| `Temp/IoTMaintenanceCommands.cs` | 4+ (asset condition ISO 15686, maintenance scheduling, digital twin, energy) | 745 |
| `Temp/MEPCreationCommands.cs` | 2+ (MEP equipment placement: HVAC, electrical, plumbing, fire) | 601 |
| `Temp/MEPScheduleCommands.cs` | 7 (panel, fixture, device, equipment, system, takeoff, sizing check schedules) | 705 |
| `Temp/ModelCreationCommands.cs` | 5+ (programmatic BIM creation: walls, floors, ceilings, roofs, columns, beams, stairs) | 980 |
| `Temp/OperationsCommands.cs` | 5+ (workflow presets, PDF/IFC/COBie export, clash detection, batch operations) | 1,005 |
| `Temp/RoomSpaceCommands.cs` | 3+ (room audit, department assignment, room schedule with tag integration) | 623 |
| `Temp/StandardsEngine.cs` | 0 (standards compliance: ISO 19650, CIBSE, BS 7671, Uniclass, BS 8300, Part L) | 795 |
| `Tags/NLPCommandProcessor.cs` | 0 (NLP intent recognition engine, 50+ patterns) | 453 |
| `Tags/TagIntelligenceCommands.cs` | 8+ (rule engine, quality analysis, batch chain, version control, propagation, analytics, smart suggestion) | 1,615 |
| `Tags/TagStyleEngineCommands.cs` | 7+ (rule-based tag family type switching, 128 style combinations, JSON-driven rules) | 1,870 |
| `Tags/Tag3DCommand.cs` | 1 (Tag3D — tags elements in 3D views) | 139 |
| `Tags/RepairDuplicateSeqCommand.cs` | 1 (RepairDuplicateSeq — smart duplicate SEQ repair) | 124 |
| `UI/StingCommandHandler.cs` | 1 (IExternalEventHandler — dispatches 590+ button tags to 376 commands + ~96 inline helpers) | 4,826 |
| `UI/StingDockPanel.xaml.cs` | 0 (WPF code-behind) | 394 |
| `UI/StingDockPanelProvider.cs` | 0 (IDockablePaneProvider) | 37 |
| `UI/StingProgressDialog.cs` | 0 (reusable modeless WPF progress window) | 238 |
| `UI/StingListPicker.cs` | 0 (reusable WPF list picker dialog with search/filter) | 323 |
| `UI/StingModePicker.cs` | 0 (reusable WPF mode picker dialog) | 200 |
| `UI/StingWizardDialog.cs` | 0 (base multi-page WPF wizard framework) | 448 |
| `UI/StingDataGridDialog.cs` | 0 (reusable WPF data grid dialog with search/filter) | 295 |
| `UI/DocumentManagementDialog.cs` | 0 (ISO 19650 Document Management Center: 7-tab action bar, code legend, 14 data loaders, quick transmittal/issue creation, keyboard shortcuts) | 3,100+ |
| `UI/StingExportDialog.cs` | 0 (ExLink-style export dialog with column mapping) | 1,020 |
| `UI/BatchRenameDialog.cs` | 0 (single-step batch rename dialog with live preview) | 693 |
| `UI/ParameterLookupDialog.cs` | 0 (enhanced parameter lookup with conditions) | 590 |
| `UI/BulkOperationDialog.cs` | 0 (unified bulk parameter operations dialog) | 891 |
| `UI/CombineConfigDialog.cs` | 0 (combine parameters configuration dialog) | 551 |
| `UI/HeadingStyleDialog.cs` | 0 (TAG7 heading style dialog) | 391 |
| `UI/COBieExportWizard.cs` | 0 (multi-page COBie V2.4 export wizard) | 521 |
| `UI/ExcelExchangeWizard.cs` | 0 (Excel import/export wizard) | 336 |
| `UI/IssueWizard.cs` | 0 (BIM issue creation wizard with BCF) | 544 |
| `UI/SmartPlacementWizard.cs` | 0 (smart tag placement configuration wizard) | 267 |
| `UI/ThemeManager.cs` | 0 (WPF theme engine — Light/Warm/Cool/Corporate themes) | 149 |
| `UI/ProjectSetupWizard.xaml.cs` | 0 (WPF wizard code-behind: 7 pages, presets, discipline config) | 1,124 |
| `UI/BEPWizard.xaml.cs` | 0 (BEP generation wizard code-behind) | 300 |
| `UI/StingDockPanel.xaml` | — (WPF markup, 9-tab panel with ~610 buttons) | 2,949 |
| `UI/ProjectSetupWizard.xaml` | — (WPF markup, 7-page wizard dialog) | 793 |
| `UI/BEPWizard.xaml` | — (WPF markup, BEP wizard dialog) | 400 |
| `UI/StingDockPanel_TagStudio.xaml` | — (WPF markup, Tag Studio compass/controls) | 1,376 |
| **Total** | **~539 commands** | **~134,400** |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (418 lines)
- Entry point registered in `StingTools.addin` (FullClassName: `StingTools.Core.StingToolsApp`)
- Static properties: `AssemblyPath`, `DataPath` (set in `OnStartup`, relative to DLL location)
- Registers WPF dockable panel (`StingDockPanelProvider`) — the primary user interface
- Registers `StingAutoTagger` IUpdater for real-time auto-tagging (disabled by default)
- Registers `StingStaleMarker` IUpdater for stale element detection on geometry changes
- Subscribes to `DocumentOpened` event for quality gate (runs `ComplianceScan` on open, updates status bar)
- Builds legacy ribbon tab "STING Tools" with `BuildSelectPanel`, `BuildDocsPanel`, `BuildTagsPanel`, `BuildOrganisePanel`, `BuildTempPanel` (retained for compatibility)
- Provides `FindDataFile(fileName)` — searches `DataPath` and subdirectories
- Provides `ParseCsvLine(line)` — CSV parser respecting quoted fields
- Contains `ToggleDockPanelCommand` — toggles the WPF dockable panel visibility

### `StingLog` (static) — `Core/StingLog.cs` (127 lines)
- Thread-safe file logger (`StingTools.log` alongside the DLL)
- Uses buffered `StreamWriter` with `FileShare.Read` for performance (replaces `File.AppendAllText`)
- Methods: `Info(msg)`, `Warn(msg)`, `Error(msg, ex?)`, `Shutdown()`
- `Shutdown()` flushes and closes the log file — wired to `OnShutdown` in `StingToolsApp`
- Error recovery: disposes bad writer on IO failure so next call retries with fresh stream
- Used throughout the codebase for error tracing; replaces silent catch blocks
- Contains `EscapeChecker` utility — Win32 `GetAsyncKeyState` wrapper for cancellation support in batch operations

### `StingAutoTagger` (IUpdater) — `Core/StingAutoTagger.cs` (736 lines)
- Real-time auto-tagging engine via Revit `IUpdater` interface
- Triggers on `Element.GetChangeTypeElementAddition()` for 22 tagged categories
- Auto-populates tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) and builds ISO 19650 tags on newly placed elements
- **Visual tag placement**: optionally creates `IndependentTag` annotations when visual tagging is enabled
- **Discipline filter**: restricts auto-tagging to user-specified discipline codes
- **Workset ownership check**: skips elements on worksets not owned by the current user (worksharing safety)
- **Type token inheritance**: calls `TypeTokenInherit(doc, el)` before `PopulateAll` to inherit from element type
- Registered in `StingToolsApp.OnStartup()`, starts disabled — user toggles via `AutoTaggerToggleCommand`
- Performance: suppresses redundant triggers via `HashSet<long>` of processed element IDs (LRU eviction at 10,000 entries)
- Contains `AutoTaggerToggleCommand` — toggles data auto-tagger on/off
- Contains `AutoTaggerToggleVisualCommand` — toggles visual tag placement on/off
- Contains `AutoTaggerConfigCommand` — configure discipline filter for auto-tagging

### `StingStaleMarker` (IUpdater) — `Core/StingAutoTagger.cs`
- Detects geometry changes on tagged elements and marks them as stale (`STING_STALE_BOOL = 1`)
- Triggers on `Element.GetChangeTypeGeometry()` for same 22 categories as auto-tagger
- Only marks elements that already have a non-empty ASS_TAG_1 and have changed level
- Performance: 20-element-per-trigger guard
- Registered/unregistered alongside StingAutoTagger in `StingToolsApp`

### `WorkflowEngine` (static) — `Core/WorkflowEngine.cs` (583 lines)
- Workflow orchestration engine for chaining named command sequences
- JSON-based workflow presets loaded from `data/WORKFLOW_*.json` files
- Built-in presets: `ProjectKickoff` (26 steps), `DailyQA` (9 steps with conditionals), `DocumentPackage` (6 steps)
- Per-step progress reporting with timing, Escape key cancellation between steps
- Atomic `TransactionGroup` with rollback on failure — user can keep or rollback partial results
- `ResolveCommand(tag)` maps ~63 command tags to `IExternalCommand` instances
- **Conditional step execution**: `MinCompliancePct`, `MaxCompliancePct` (compliance threshold gates), `RequiresStaleElements` (skip if no stale elements), workshared-only steps
- **Result persistence**: `WorkflowRunRecord` saved to `STING_WORKFLOW_LOG.json` alongside project file (capped at 100 records)
- Contains 4 commands: `WorkflowPresetCommand`, `ListWorkflowPresetsCommand`, `CreateWorkflowPresetCommand`, `WorkflowTrendCommand`

### `ComplianceScan` (static) — `Core/ComplianceScan.cs` (222 lines)
- Lightweight cached compliance scan for live dashboard/status bar display
- Thread-safe cached results with 30-second stale duration
- `Scan(doc)` — quick tag completeness stats (tagged complete/incomplete/untagged)
- `ComplianceResult` — RAG status (Red <50%, Amber 50-80%, Green >80%), top 5 issues
- **Per-discipline breakdown**: `ByDisc` dictionary of `DiscComplianceData` (Total, Tagged, Untagged, MissingLoc, MissingSys, MissingProd, CompliancePct)
- **Dual metrics**: `CompliancePercent` (tagged/total) vs `StrictPercent` (fully resolved/total)
- **Revision tracking**: `RevisionComplete`, `RevisionMissing`, `RevisionPercent`, `RevisionDistribution`
- `StatusBarText` — shows RAG status, tag%, REV%, untagged count
- `InvalidateCache()` — called after tagging operations to force refresh

### `StingProgressDialog` — `UI/StingProgressDialog.cs` (239 lines)
- Reusable modeless WPF progress window for batch operations
- Shows progress bar, element count (N/M), estimated time remaining, and cancel button
- Thread-safe updates via `Dispatcher.Invoke` (updates every 50 elements for performance)
- Win32 `GetAsyncKeyState` for Escape key detection even when Revit has focus
- Usage: `var p = StingProgressDialog.Show("Title", total); p.Increment("status"); p.Close();`

### `ParamRegistry` (static) — `Core/ParamRegistry.cs` (1,751 lines)
- **Single source of truth** for all parameter names, GUIDs, container definitions, and category bindings
- Loads from `PARAMETER_REGISTRY.json` at runtime (thread-safe lazy initialization via `EnsureLoaded()` with lock); falls back to hardcoded defaults if JSON not found
- **Tag format configuration**: `Separator`, `NumPad`, `SegmentOrder` — data-driven rather than hardcoded
- **Typed string constants** for all 8 source tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ) + universal containers (TAG1-TAG7, TAG7A-TAG7F)
- **Phase 11 constants**: `STALE`/`STALE_GUID`, `CLUSTER_COUNT`/`CLUSTER_LABEL`, `DISPLAY_MODE`/`DISPLAY_TXT`, `TAG_POS`, `VIEW_TAG_STYLE` — system parameters for stale detection, clustering, display modes, and view-level tag style routing
- **Extended parameter constants**: ~97+ parameters across identity, spatial, BLE dimensional, electrical, lighting, HVAC, plumbing, COBie, asset management, and style groups (30 additional constants added in Phase 29)
- **GUID lookups**: `GetGuid(paramName)`, `GetParamName(guid)`, `AllParamGuids`
- **Container management**: `AllContainers`, `ContainersForCategory(categoryName)`, `GetContainerTuples()`
- **Token presets**: Named index arrays for partial tag strings
- **Tag assembly**: `AssembleContainer()`, `ReadTokenValues()`, `WriteContainers()`
- **Reload**: `Reload()` forces re-read from disk for live editing workflows

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs` (2,009 lines)
- `GetString(el, paramName)` — read text parameter, returns empty string on null
- `GetInt(el, paramName, defaultValue)` — read integer parameter with fallback (handles Integer, Double, String storage)
- `SetString(el, paramName, value, overwrite)` — write text parameter, skips read-only/non-empty unless overwrite
- `SetInt(el, paramName, value)` — write integer parameter (handles Integer and Double storage types)
- `SetIfEmpty(el, paramName, value)` — set only when currently empty
- `CommandExecutionContext` — encapsulates `UIApplication`, `UIDocument`, `Document` from `ExternalCommandData` with null-safe access
- `GetLevelCode(doc, el)` — derives short level codes (L01, GF, B1, RF, XX)
- `GetCategoryName(el)` — safe category name retrieval
- `GetFamilyName(el)` — element family name retrieval
- `GetFamilySymbolName(el)` — element type/symbol name retrieval
- `GetRoomAtElement(doc, el)` — spatial lookup for room context
- `GetSolidFillPattern(doc)` — shared solid fill pattern finder (replaces 8 inline collectors)

### `SpatialAutoDetect` (static) — `Core/ParameterHelpers.cs`
- Auto-derives LOC from Room name/number/Project Info and ZONE from Room Department/name
- `BuildRoomIndex(doc)` — builds spatial room lookup for batch operations
- `DetectProjectLoc(doc)` — extracts LOC from Project Information
- `DetectLoc(doc, el, roomIndex, projectLoc)` — per-element LOC detection
- `DetectZone(doc, el, roomIndex)` — per-element ZONE detection

### `PhaseAutoDetect` (static) — `Core/ParameterHelpers.cs`
- Auto-derives STATUS and REV from Revit phase data, worksets, and project info
- `DetectStatus(doc, el)` — derives NEW/EXISTING/DEMOLISHED/TEMPORARY from element phase
- `DetectProjectRevision(doc)` — extracts current revision code from Project Information

### `TokenAutoPopulator` (static) — `Core/ParameterHelpers.cs`
- Shared utility for batch token population across all tagging commands (DRY replacement for inline code)
- `PopulationContext.Build(doc)` — builds reusable context (room index, project LOC, project REV, known categories)
- `PopulateAll(doc, el, ctx, overwrite)` — populates all 9 tokens on a single element with guaranteed defaults; calls `TypeTokenInherit` first
- `TypeTokenInherit(doc, el)` — copies non-empty token values from element TYPE to instance (runs before PopulateAll so inherited values are not overwritten)
- `CopyTokensFromNearest(doc, el, tokensToCopy, candidatePool)` — finds nearest already-tagged element of same category within 10 ft, copies specified tokens
- Returns `PopulationResult` with granular counts (TokensSet, LocDetected, ZoneDetected, StatusDetected, RevSet, FamilyProdUsed)

### `NativeParamMapper` (static) — `Core/ParameterHelpers.cs`
- Maps 30+ Revit built-in parameters to STING shared parameters
- `MapAll(doc, el)` — comprehensive parameter mapping (dimensions, MEP data, identity)
- `MapSheets(doc)` — maps native sheet parameters (number, name) to STING shared parameters
- Bridges native Revit data (Width, Height, Flow, etc.) into STING parameter schema

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs` (228 lines)
- Backwards-compatible facade wrapping `ParamRegistry` — delegates all lookups to the single source of truth
- `ParamGuids` — delegates to `ParamRegistry.AllParamGuids`
- `UniversalParams` — cached lazy property delegating to `ParamRegistry`
- `AllCategoryEnums` — cached lazy property delegating to `ParamRegistry`
- `DisciplineBindings` — cached lazy property for Pass 2 discipline-specific bindings
- `BuildCategorySet(doc, enums)` — type-safe category set builder
- `ValidateBindingsFromCsv()` — compares CATEGORY_BINDINGS.csv against registry bindings (10,661 entries)
- `InvalidateCache()` — called by `ParamRegistry.Reload()` to clear cached properties

### `TagCollisionMode` (enum) — `Core/TagConfig.cs`
- Controls how tag collisions are handled: `Skip`, `Overwrite`, `AutoIncrement`
- Used by all tagging commands (AutoTag, BatchTag, TagSelected, ReTag, TagAndCombine)

### `TaggingStats` (class) — `Core/TagConfig.cs`
- Tracks batch tagging operation statistics for rich post-operation reporting
- Per-category, per-discipline, per-system, per-level breakdown
- Collision detail tracking (tag, depth), skipped/overwritten counts, warnings
- `BuildReport()` generates multi-line formatted report for TaskDialog display

### `ISO19650Validator` (static) — `Core/TagConfig.cs`
- **Code validation**: `ValidDiscCodes`, `ValidSysCodes`, `ValidFuncCodes` — CIBSE / Uniclass 2015 code lists
- **Token validation**: `ValidateToken(tokenName, value)` — validates individual token values against allowed lists
- **Element validation**: `ValidateElement(el)` — validates all 8 tokens + cross-validates DISC/SYS against element category
- **Tag format validation**: `ValidateTagFormat(tag)` — validates complete 8-segment tag string format and all segments
- Used by `ValidateTagsCommand`, `BuildTagsCommand`, and `PreTagAuditCommand` for ISO 19650 enforcement

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (4,030 lines)
- **Lookup tables** (all configurable via `project_config.json`):
  - `DiscMap` — 41 category to discipline code mappings (M, E, P, A, S, FP, LV, G)
  - `SysMap` — 17 system codes to category lists (HVAC, DCW, DHW, HWS, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN)
  - `ProdMap` — 41 category to product codes
  - `FuncMap` — 16 system to function code mappings
  - `LocCodes` — location codes (BLD1, BLD2, BLD3, EXT, XX)
  - `ZoneCodes` — zone codes (Z01-Z04, ZZ, XX)
- **Configuration management**: `LoadFromFile(path)`, `LoadDefaults()`, `ConfigSource`
- **Tag operations** (7 intelligence layers):
  - `TagIsComplete(tagValue, expectedTokens=8)` — validates 8-segment tag completeness
  - `BuildAndWriteTag(doc, el, seqCounters, skipComplete, existingTags, collisionMode, stats)` — shared tagging logic with collision mode, stats tracking, and cross-validation
  - `GetExistingSequenceCounters(doc)` — scans project for highest SEQ per group
  - `BuildExistingTagIndex(doc)` — builds HashSet of all existing tags for O(1) collision detection
  - `GetSysCode(categoryName)`, `GetFuncCode(sysCode)` — reverse lookups
  - `GetMepSystemAwareSysCode(el, categoryName)` — derives SYS from connected MEP system name before falling back to category
  - `GetFamilyAwareProdCode(el, categoryName)` — family-name-aware PROD code resolution (35+ specific codes)
  - `GetViewRelevantDisciplines(view)` — inspects view name, template, and VG to determine which disciplines to tag
  - `FilterByViewDisciplines(elements, disciplines)` — filters elements to only view-relevant disciplines
- **Constants**: `NumPad = 4`, `Separator = "-"`

### `TagIntelligence` (static) — `Core/TagConfig.cs`
- Advanced tagging intelligence layer for complex inference
- `InferenceResult` class for structured inference outputs

### Internal Helper Classes

These `internal static` classes provide shared logic used by multiple commands within their respective files:

| Helper Class | Location | Purpose |
|--------------|----------|---------|
| `DocAutomationHelper` | `Docs/DocAutomationExtCommands.cs` | Shared documentation automation engine: discipline defs, sheet numbering, 7-layer template matching, view creation by family, scope box utilities, name caching |
| `SheetManagerEngine` | `Docs/SheetManagerEngine.cs` | Core sheet manager: drawable zone detection, scale calculation, shelf packing, collision detection, viewport placement, sheet cloning, naming/numbering, auto-arrange, batch operations |
| `SheetManagerEngineExt` | `Docs/SheetManagerEngineExt.cs` | MaxRects bin packing (BSSF), layout presets (JSON), viewport type rules, batch clone/renumber, overflow handling |
| `SheetTemplateEngine` | `Docs/SheetTemplateEngine.cs` | Sheet templates (6 built-in), ISO 19650 compliance (10 rules), viewport grid alignment, edge alignment, distribution, batch PDF export, sheet register |
| `SheetManagerDialog` | `Docs/SheetManagerDialog.cs` | Dual-panel WPF sheet manager dialog: TreeView navigation (sheets by discipline), context-sensitive detail panel, search/filter |
| `CategorySelector` | `Select/CategorySelectCommands.cs` | `SelectByCategory()` — shared logic for all 15 category selection commands |
| `TokenWriter` | `Tags/TokenWriterCommands.cs` | Encapsulates LOC/ZONE/STATUS token writing and number assignment logic |
| `CompoundTypeCreator` | `Temp/FamilyCommands.cs` | Creates compound wall/floor/ceiling/roof/duct/pipe types from CSV data; `ElementKind` enum; applies material properties |
| `MaterialPropertyHelper` | `Temp/MaterialCommands.cs` | Shared CSV material pipeline (`CreateMaterialsFromCsv`), fill pattern cache, base material duplication, full property application for BLE and MEP commands |
| `SpatialAutoDetect` | `Core/ParameterHelpers.cs` | Auto-derives LOC from Room name/number/Project Info and ZONE from Room Department/name patterns |
| `NativeParamMapper` | `Core/ParameterHelpers.cs` | Maps Revit built-in parameters to STING shared parameters (30+ mappings) |
| `TokenAutoPopulator` | `Core/ParameterHelpers.cs` | Shared batch token population: PopulationContext (room/project/category indexes), PopulateAll for 9-token fill with guaranteed defaults |
| `PhaseAutoDetect` | `Core/ParameterHelpers.cs` | Auto-derives STATUS from Revit phases/worksets and REV from project revision data |
| `LeaderHelper` | `Organise/TagOperationCommands.cs` | Shared logic for annotation tag leader operations (toggle, align, snap, auto-align text) |
| `AnnotationColorHelper` | `Organise/TagOperationCommands.cs` | Discipline-to-Color map, quick-pick colors, solid fill finder, annotation `OverrideGraphicSettings` builder |
| `ScheduleHelper` | `Temp/ScheduleCommands.cs` | Schedule creation utilities and field remap loading from SCHEDULE_FIELD_REMAP.csv |
| `ScheduleAuditHelper` | `Temp/ScheduleEnhancementCommands.cs` | CSV definition loader, ScheduleDefinition model, shared infrastructure for schedule management commands |
| `FormulaEngine` | `Temp/FormulaEvaluatorCommand.cs` | Formula parsing, context building, text/numeric evaluation, includes `ExpressionParser` recursive descent parser |
| `TemplateManager` | `Temp/TemplateManagerCommands.cs` | Deep template intelligence engine: 5-layer auto-assignment, compliance scoring, VG diff, style definitions |
| `TagFamilyConfig` | `Tags/TagFamilyCreatorCommand.cs` | Configuration for tag family creation: 50 `BuiltInCategory` to `.rft` template mappings, seed family lookup, output directory management |
| `LegendBuilder` | `Tags/LegendBuilderCommands.cs` | Legend creation engine: drafting view legends with FilledRegion swatches and TextNote labels, multi-column grid layout |
| `StingColorRegistry` | `Tags/LegendBuilderCommands.cs` | Central color registry for all colorization schemes (discipline, status, system, parameter-based) |
| `LegendSyncEngine` | `Tags/LegendBuilderCommands.cs` | Legend synchronization: updates existing legends when element data changes |
| `LegendIntelligence` | `Tags/LegendBuilderCommands.cs` | Smart legend placement and sheet context determination |
| `SystemParamPush` | `Tags/SystemParamPushCommand.cs` | MEP system parameter propagation: 3-layer traversal (MEP System API → Connector graph → Spatial proximity) |
| `LabelDefinitionHelper` | `Tags/PresentationModeCommand.cs` | Load/parse presentation modes from LABEL_DEFINITIONS.json |
| `TagStyleEngine` | `Tags/TagStyleEngine.cs` | Tag style control engine: `StylePreset` (size/style/color → BOOL param), `ColorScheme` (8 built-in schemes), paragraph depth tiers (1-10), 128 style combinations |
| `ModelEngine` | `Model/ModelEngine.cs` | Auto-modeling engine: walls, floors, roofs, columns, beams, MEP, rooms, building shell + `FamilyResolver`, `WorksetAssigner`, `FailureHandler` |
| `CADToModelEngine` | `Model/CADToModelEngine.cs` | DWG-to-BIM conversion: `LayerMapper` (18 category patterns), geometry extraction (parallel lines → walls, closed loops → floors, blocks → doors/windows) |
| `BIMManagerEngine` | `BIMManager/BIMManagerCommands.cs` | ISO 19650 BIM management engine: BEP generation (22 presets, template-driven), issue tracker (BCF-compatible), document register, COBie V2.4 export (22 project type presets, 19 worksheets, full tag integration), transmittals, CDE status codes, briefcase viewer, asset management strategy, training plan + sequential ID generation |
| `Scheduling4DEngine` | `BIMManager/SchedulingCommands.cs` | 4D/5D scheduling engine: 32-trade construction sequences, cost rates, MS Project import/export, cash flow forecasting, Gantt timeline generation |
| `ExcelLinkEngine` | `BIMManager/ExcelLinkCommands.cs` | Bidirectional Excel data exchange: 30+ column export, validation rules, change tracking with audit trail, ChangeRecord/ValidationWarning models |
| `PlatformLinkEngine` | `BIMManager/PlatformLinkCommands.cs` | Platform integration engine: BCF 2.1 XML generation, ISO 19650 file naming validator, deliverable collector, platform sync with delta detection |
| `RevisionEngine` | `BIMManager/RevisionManagementCommands.cs` | Revision management: tag snapshot/compare, revision sequence tracking, change delta computation, element tracking across revisions |
| `OutputLocationHelper` | `Core/OutputLocationHelper.cs` | Centralized export path management: 4-level fallback chain (preferred → project → documents → temp), timestamped paths, config persistence |
| `StingListPicker` | `UI/StingListPicker.cs` | Reusable WPF list picker dialog: search/filter, single/multi-select, corporate styling, replaces paginated TaskDialog workflows |
| `BatchRenameDialog` | `UI/BatchRenameDialog.cs` | Single-step WPF batch rename dialog: category/family/type filters, 7 rename operations (find/replace with regex, prefix/suffix, case, sequential, level standardisation), live before/after preview with green highlight, Select All/None |
| `ParameterLookupDialog` | `UI/ParameterLookupDialog.cs` | Enhanced WPF parameter lookup: category picker, searchable parameter list with priority sorting, value display with element counts, 11-operator condition builder, Select Matching/Color By Value/Apply Filter actions |
| `BulkOperationDialog` | `UI/BulkOperationDialog.cs` | Unified WPF dialog for bulk parameter operations: operation selector, dynamic token/value tile picker, element preview |
| `CombineConfigDialog` | `UI/CombineConfigDialog.cs` | Unified WPF dialog for Combine Parameters: mode selector, searchable container group tree with checkbox multi-select |
| `HeadingStyleDialog` | `UI/HeadingStyleDialog.cs` | Unified WPF dialog for TAG7 heading style: 4 visual style cards with live text preview |
| `COBieExportWizard` | `UI/COBieExportWizard.cs` | Multi-page COBie V2.4 export wizard with preset selection and sheet configuration |
| `ExcelExchangeWizard` | `UI/ExcelExchangeWizard.cs` | Excel import/export wizard with column mapping and validation |
| `IssueWizard` | `UI/IssueWizard.cs` | BIM issue creation wizard with BCF integration |
| `SmartPlacementWizard` | `UI/SmartPlacementWizard.cs` | Smart tag placement configuration wizard |
| `StingWizardDialog` | `UI/StingWizardDialog.cs` | Base multi-page WPF wizard framework with page navigation, validation, summary |
| `StingDataGridDialog` | `UI/StingDataGridDialog.cs` | Reusable WPF data grid dialog for tabular data display with search/filter |
| `DocumentManagementDialog` | `UI/DocumentManagementDialog.cs` | ISO 19650 Document Management Center: 7-tab action bar (FILE/BULK, DOCS/CDE, ISSUES, REVISIONS, COORDINATION, HANDOVER, NOTES/BEP), code legend (all ISO 19650 codes), dashboard strip with clickable RAG metrics, navigator tree (17 grouping modes), 14 data loaders, quick transmittal/issue creation, bulk CDE/status operations, keyboard shortcuts, VirtualizingStackPanel, drag-drop import |
| `StingExportDialog` | `UI/StingExportDialog.cs` | ExLink-style export dialog with column mapping, preview, and format selection |
| `StingCommandHandler` | `UI/StingCommandHandler.cs` | `IExternalEventHandler` — dispatches 590+ dockable panel button tags to 374 command classes + ~96 inline helpers on the Revit API thread |
| `StingDockPanel` | `UI/StingDockPanel.xaml.cs` | WPF code-behind for 8-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM) with colour swatches and status bar |
| `StingDockPanelProvider` | `UI/StingDockPanelProvider.cs` | `IDockablePaneProvider` — registers dockable panel with Revit; PaneGuid for panel identification |
| `ColorHelper` | `Select/ColorCommands.cs` | 10 built-in colour palettes, `OverrideGraphicSettings` builder, solid fill pattern finder, preset save/load |
| `TagPlacementEngine` | `Tags/SmartTagPlacementCommand.cs` | 8-position candidate offset generation, scale-aware placement, 2D AABB collision detection, leader auto-generation |
| `TagPlacementPresets` | `Tags/SmartTagPlacementCommand.cs` | Per-category placement rules (`CategoryRule`), named presets (`PlacementPreset`), `LearnFromView` analysis |
| `WorkflowEngine` | `Core/WorkflowEngine.cs` | Workflow preset orchestration: command tag resolution, step execution, cancellation, TransactionGroup rollback |
| `ComplianceScan` | `Core/ComplianceScan.cs` | Cached compliance scan: RAG status, issue tracking, STATUS/container completeness, status bar text generation |
| `TagPipelineHelper` | `Core/ParameterHelpers.cs` | Unified per-element tagging pipeline: TypeTokenInherit → PopulateAll → CategoryForceSys → NativeParamMapper → FormulaEngine → BuildAndWriteTag → WriteContainers → WriteTag7All → GetGridRef |
| `StingAutoTagger` | `Core/StingAutoTagger.cs` | IUpdater-based real-time auto-tagging on element addition with visual tag placement, discipline filter, workset ownership check |
| `StingStaleMarker` | `Core/StingAutoTagger.cs` | IUpdater-based stale element detection on geometry changes — marks elements with `STING_STALE_BOOL = 1` |
| `FamilyParamEngine` | `Tags/FamilyParamCreatorCommand.cs` | Family parameter injection: DetectFamilyCategory, InjectSharedParams, InjectTagPosFormulas, InjectPositionTypes, ProcessFamily |
| `WorkflowRunRecord` | `Core/WorkflowEngine.cs` | JSON-serializable workflow execution record: timestamp, preset, step counts, duration, compliance before/after |
| `StingProgressDialog` | `UI/StingProgressDialog.cs` | Modeless WPF progress window: progress bar, ETA, cancel button, Escape key detection |
| `EscapeChecker` | `Core/StingLog.cs` | Win32 `GetAsyncKeyState` wrapper for Escape key cancellation detection |
| `ThemeManager` | `UI/ThemeManager.cs` | WPF theme engine: 4 themes (Dark/Light/Grey/Corporate), 13 color resource keys, `ApplyTheme`, `CycleTheme`, `ApplyCorporateOverrides` |
| `IPanelCommand` | `Core/IPanelCommand.cs` | Interface for WPF dockable panel commands; `SafeApp()`, `SafeDoc()`, `SafeUIDoc()` extension methods for robust UIApplication resolution |
| `PerformanceTracker` | `Core/PerformanceTracker.cs` | Lightweight performance profiling: per-operation/element timing, LRU slowest tracking (100 entries), CSV export, thread-safe `ConcurrentDictionary` |
| `NLPCommandProcessor` | `Tags/NLPCommandProcessor.cs` | Natural language intent recognition engine: 50+ patterns mapping queries to STING command classes with confidence scoring |
| `StandardsEngine` | `Temp/StandardsEngine.cs` | Standards compliance framework: ISO 19650, CIBSE velocity limits, BS 7671 electrical, Uniclass 2015, BS 8300 accessibility, Part L energy |

## ISO 19650 Tag Format

Tags follow the 8-segment format: `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`

| Segment | Parameter | Example | Description |
|---------|-----------|---------|-------------|
| DISC | ASS_DISCIPLINE_COD_TXT | M, E, P, A, S | Discipline code |
| LOC | ASS_LOC_TXT | BLD1, EXT | Location/building code |
| ZONE | ASS_ZONE_TXT | Z01, Z02 | Zone code |
| LVL | ASS_LVL_COD_TXT | L01, GF, B1 | Level code |
| SYS | ASS_SYSTEM_TYPE_TXT | HVAC, DCW, SAN, HWS, LV | System type (CIBSE/Uniclass) |
| FUNC | ASS_FUNC_TXT | SUP, HTG, DCW, SAN, PWR | Function code (CIBSE/Uniclass) |
| PROD | ASS_PRODCT_COD_TXT | AHU, DB, DR | Product code |
| SEQ | ASS_SEQ_NUM_TXT | 0001, 0042 | 4-digit sequence number |

Example tag: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`

### Tag Containers (36 parameters across 16 groups)
- **Universal**: ASS_TAG_1 (full 8-segment) through ASS_TAG_6 (multi-line bottom)
- **HVAC**: HVC_EQP_TAG, HVC_DCT_TAG, HVC_FLX_TAG
- **Electrical**: ELC_EQP_TAG, ELE_FIX_TAG, LTG_FIX_TAG, ELC_CDT_TAG, ELC_CTR_TAG
- **Plumbing**: PLM_EQP_TAG
- **Fire/Safety**: FLS_DEV_TAG
- **Comms/LV**: COM_DEV_TAG, SEC_DEV_TAG, NCL_DEV_TAG, ICT_DEV_TAG
- **Material**: MAT_TAG_1 through MAT_TAG_6

### TAG7 — Rich Descriptive Narrative

TAG7 is a comprehensive human-readable tag parameter with 6 sub-sections (A-F), each stored as a separate parameter:

| Parameter | Section | Content | Styling |
|-----------|---------|---------|---------|
| `ASS_TAG_7_TXT` | Full | Complete narrative with markup tokens | Multi-style |
| `ASS_TAG_7A_TXT` | A: Identity Header | Asset name, product code, manufacturer, model | **Bold**, Blue |
| `ASS_TAG_7B_TXT` | B: System & Function | System type description, function code | *Italic*, Green |
| `ASS_TAG_7C_TXT` | C: Spatial Context | Room, department, grid reference | Normal, Orange |
| `ASS_TAG_7D_TXT` | D: Lifecycle & Status | Status, revision, origin, maintenance | Normal, Red |
| `ASS_TAG_7E_TXT` | E: Technical Specs | Discipline-specific performance data (capacity, flow, voltage) | **Bold**, Purple |
| `ASS_TAG_7F_TXT` | F: Classification | Uniformat, OmniClass, keynote, cost, ISO tag | *Italic*, Grey |

TAG7 uses pipe (`|`) separators between sections and supports paragraph depth control via `TAG_PARA_STATE_1/2/3_BOOL` parameters. Presentation modes: Compact, Technical, Full Specification, Presentation, BOQ.

Additionally, 39 **paragraph container parameters** exist for category-specific TAG7 narratives (e.g., `ARCH_TAG_7_PARA_WALL_TXT`, `HVC_TAG_7_PARA_SPEC_TXT`).

## Tagging Workflow

All tagging commands delegate to `TagPipelineHelper.RunFullPipeline()` in `Core/ParameterHelpers.cs`, which guarantees the following 9-step sequence for every element:

1. **Category filter** — skips elements in `TagConfig.CategorySkipList`; applies `CategoryTokenOverrides` post-population
2. **TypeTokenInherit** — copies token values from family type to instance
3. **PopulateAll** — derives all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
4. **NativeParamMapper.MapAll** — bridges 30+ Revit native params to STING shared params
5. **FormulaEngine** — evaluates 199 dependency-ordered formulas (cost/flow/area/env)
6. **BuildAndWriteTag** — assembles ISO 19650 8-segment tag with collision detection
7. **WriteContainers** — writes tag to all 53 discipline-specific containers
8. **WriteTag7All** — builds TAG7 rich narrative (A-F sub-sections)
9. **GetGridRef** — auto-detects nearest grid intersection (GRID_REF)

After transaction commit, `TagConfig.SaveSeqSidecar()` persists SEQ counters to a `.sting_seq.json` sidecar file alongside the `.rvt`, ensuring sequence continuity between sessions.

**Commands using RunFullPipeline (full pipeline)**: AutoTagCommand, BatchTagCommand, TagAndCombineCommand, TagNewOnlyCommand, RetagStaleCommand, TagSelectedCommand, ReTagCommand, StingAutoTagger (IUpdater), Tag3DCommand

**Commands using partial pipeline** (not tagged via RunFullPipeline but include containers/TAG7): TagFormatMigrationCommand, TagChangedCommand, RepairDuplicateSeqCommand, SystemParamPushCommand, BulkParamWrite retag, FullAutoPopulateCommand

**Token-only commands** (populate tokens, no tag assembly): FamilyStagePopulateCommand, BulkAutoPopulate

**Config keys** (`project_config.json`): `TAG_PREFIX`, `TAG_SUFFIX`, `CATEGORY_SKIP`, `CATEGORY_FORCE_SYS`, `CATEGORY_TOKEN_OVERRIDES`, `SEQ_SCHEME`, `SEQ_INCLUDE_ZONE`, `COMPLIANCE_GATE_PCT`, `AUTO_RUN_WORKFLOW_ON_OPEN`, `TAG_FORMAT`

**Quality features**:
- `ASS_TAG_PREV_TXT` + `ASS_TAG_MODIFIED_DT` — tag history audit trail written per element
- `ComplianceScan.ComplianceResult.EmptyTokenCounts` — per-token granular compliance breakdown
- `TagConfig.CategoryTokenOverrides` — per-category token value enforcement from config
- `ASS_TOKEN_LOCK_TXT` — per-element token lock parameter (infrastructure in place; PopulateAll skipFields pending)
- `PreviewTagCommand` — dry-run tag preview for selected element
- `AUTO_RUN_WORKFLOW_ON_OPEN` — notifies user of configured workflow on document open

### Delta Sync (`TagChangedCommand`)

Detects 6 token types that have become stale: LVL, LOC, ZONE, SYS, FUNC, PROD. Re-derives current values from element context and reports mismatches for selective re-tagging.

### Adaptive Workflows

`WorkflowEngine` supports conditional step execution via JSON workflow presets (e.g., `WORKFLOW_DailyQA_Enhanced.json`):
- `maxCompliancePct` — Skip step if compliance already exceeds threshold
- `minCompliancePct` — Skip step if compliance is below threshold
- `requiresStaleElements` — Skip step if no stale elements detected

## WPF Dockable Panel (Primary UI)

The plugin's primary user interface is a **WPF dockable panel** that consolidates all commands from the original ribbon into a single tabbed panel docked to the right side of Revit. The ribbon panels still exist for backwards compatibility but the dockable panel is the main interaction surface.

### Architecture

| Component | File | Purpose |
|-----------|------|---------|
| `StingDockPanel.xaml` | `UI/StingDockPanel.xaml` (2,163 lines) | WPF markup: 9-tab layout (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM/TAGS), ~610 buttons, colour swatches, bulk parameter controls |
| `StingDockPanel.xaml.cs` | `UI/StingDockPanel.xaml.cs` (377 lines) | Code-behind: button dispatch via `IExternalEventHandler`, colour swatch builder, status bar |
| `StingCommandHandler` | `UI/StingCommandHandler.cs` (4,817 lines) | `IExternalEventHandler` — maps 590+ button Tag strings to 374 command classes + ~96 inline helpers, ensures Revit API calls run on the main thread |
| `StingDockPanelProvider` | `UI/StingDockPanelProvider.cs` (37 lines) | `IDockablePaneProvider` — registers panel with Revit, sets initial dock position (Right, 320×400 min) |
| `ToggleDockPanelCommand` | `Core/StingToolsApp.cs` (line 825) | `IExternalCommand` — toggles panel visibility from ribbon button |

### Panel Tabs

| Tab | Content | Mirrors Ribbon Panel |
|-----|---------|---------------------|
| SELECT | Category selectors, state selectors, spatial selectors, bulk parameter write | Select |
| ORGANISE | Tag operations, leader management, analysis/QA, annotation colors, tag appearance | Organise + Tags QA |
| DOCS | Sheet/view organization, viewports, document automation, sheet manager (24 commands), templates, ISO compliance | Docs |
| TEMP | Materials, families, schedules, template setup, data pipeline | Temp |
| CREATE | Tagging commands, token writers, combine, setup, legends | Tags |
| VIEW | View templates, template manager, styles, presentation modes | Temp Templates |
| MODEL | Auto-modeling (walls, floors, roofs, columns, beams, MEP), DWG-to-BIM conversion | Model |
| BIM | ISO 19650 BIM management: BEP, issues, documents, COBie, transmittals, CDE, reviews, briefcase, 4D/5D scheduling, Excel link, platform integration, revision management | BIMManager |
| TAGS | Tag Studio: 6 sub-tabs (Placement/Leader/Style/Tokens/Tools/Scale), 16-position compass, collision scoring, leader/elbow, color schemes, scale tiers | Tags + Core |

### Thread Safety Pattern
All button clicks dispatch through `IExternalEventHandler` to ensure Revit API calls execute on the correct thread:
```
Button Click → StingDockPanel.Cmd_Click → StingCommandHandler.SetCommand(tag) → ExternalEvent.Raise()
→ Revit calls StingCommandHandler.Execute(UIApplication) → RunCommand<T>(app)
```

## STING HVAC Center (Phase 180)

Sibling dockable panel to `StingElectricalPanel` and `StingPlumbingPanel` — same dock target (Tabbed behind `PropertiesPalette`), same `IDockablePaneProvider` + `IExternalEventHandler` shape, same tag-keyed dispatch pattern. **Toggled from ribbon "❄ HVAC" panel → "STING HVAC" button** (`ToggleHvacPanelCommand`). Visible-by-default = false.

### Files

| File | Lines | Purpose |
|---|---|---|
| `UI/StingHvacPanelProvider.cs` | ~37 | `IDockablePaneProvider`; PaneGuid `D7E8F9A0-B1C2-3D4E-5F60-1A2B3C4D5E6F` |
| `UI/StingHvacCommandHandler.cs` | ~250 | `IExternalEventHandler`; tag switch + `Run<T>(app)` for 40+ HVAC tags; unknown tags fall through to `StingCommandHandler` |
| `UI/StingHvacPanel.xaml` | ~660 | 7-tab WPF page (EQPT · SYS · CALCS · DUCT · LOADS · FAB · RPRT) |
| `UI/StingHvacPanel.xaml.cs` | ~280 | Code-behind: 10 `ObservableCollection<T>` grids, header combo handlers, `Cmd_Click`, `SeedSizingRolesFromRegistry()` |
| `Data/STING_MEP_SIZING_RULES.json` | — | Single source of truth for HVAC sizing constants |
| `Core/Mep/MepSizingRegistry.cs` | ~340 | JSON loader + project-override layer (`<project>/_BIM_COORD/mep_sizing_rules.json`) |

### Tab map

| Tab | Purpose | Wires to |
|---|---|---|
| EQPT | AHU/FCU/VAV/Chiller/Boiler/HP inventory with Identity / Performance / Acoustics / Connections / COBie expanders | `PlaceHvacEquipmentCommand`, `MechanicalEquipmentScheduleCommand`, `MEPSystemAuditCommand`, `MEPConnectionAuditCommand`, `MEPSizingCheckCommand` |
| SYS | Mechanical systems (Supply / Return / Exhaust / OA / Relief × Air / CHW / HW / Refrigerant / Condensate) + fan-pressure budget + zones + fire dampers | `MEPSystemAuditCommand`, `AutoFireDamperCommand`, `HardyCrossCommand`, `Mep_PressureDrop`, `Mep_SystemTracer` |
| CALCS | Sizing strategy + editable per-role velocity/friction/aspect targets + live-results panel + issues grid | `MepAutoSizeDuctCommand`, `CalcDuctFrictionCommand`, `DuctStaticRegainCommand`, `DuctEqualFrictionCommand`, `HardyCrossCommand`, `RunAllValidatorsCommand` |
| DUCT | Duct types + per-region standard-size table + gauge/seam breakpoints + insulation + fab defaults | `CreateDuctsCommand`, `ModelCreateDuctCommand`, `AutoDropCommand`, `GenerateLayoutCommand`, `DuctSeamAuditCommand`, `PlaceHangersCommand`, `ValidateFillsCommand` |
| LOADS | Spaces × envelope × internal gains × ventilation × computed loads; engine + code pickers | TaskDialog stubs for `Hvac_RunLoads` / `Hvac_ExportGbxml` (Phase 181 Loads + gbXML wizard target); `MEPSpaceAnalysisCommand`, `VentilationCommand` |
| FAB | Spool grid + Assembly / Hangers / Outputs expanders | `Fabrication_OpenWorkspace`, `ExportCutListCommand`, `ExportIsometricsCommand`, `ExportWeldMapCommand`, `HangerTakedownCommand`, `FlangeRatingCommand`, `SpoolWeightCommand`, `ExportNCCommand` |
| RPRT | Health KPIs + drift + workflow-run grid + export action row | `Hvac_ReloadRules`, `Mep_SystemAnalyse`, `V6Carbon`, `DocPackage`, `PlatformSync` |

### Header context strip

The header is the project-wide context that drives every calc:

- **Standard** combo — CIBSE Guide B/C · ASHRAE 90.1 · EN 1505/1506 · DIN 4740 · SS-EN
- **Region** combo — UK_SI · US_IP · EU_SI · DE_SI · SE_SI (selects `standardSizesMm` / `standardBoreMm` from the registry)
- **Pressure class** combo — DW/144 A (≤ 500 Pa) · B (≤ 1000 Pa) · C (≤ 2500 Pa) · D (≤ 7500 Pa)
- **Air density** combo — 1.20 kg/m³ default + warm / hot / cold presets
- **Sizing strategy** radio — Velocity · Equal friction · Static regain · Constant pressure (replaces the three separate `MepAutoSizeDuct` / `DuctStaticRegain` / `DuctEqualFriction` commands)
- **Scope** radio — All · Selection · Active view (every action button respects this without re-prompting)

Header state is snapshotted into static fields on `StingHvacCommandHandler` (`CurrentRegion`, `CurrentStandard`, `CurrentPressureClassId`, `CurrentAirDensityKgM3`, `CurrentSizingStrategyId`, `CurrentScope`) before each `ExternalEvent.Raise()` so the command running on the Revit API thread reads consistent values.

### Sizing registry

`STING_MEP_SIZING_RULES.json` is the corporate baseline; `<project>/_BIM_COORD/mep_sizing_rules.json` is the project-level override. Both load through `MepSizingRegistry.Get(doc)` (cached per-document path). Fields covered:

- **Duct**: `roles[]` (main / branch / runout / outdoor_air / exhaust / kitchen / smoke) with `maxVelocityMs` / `maxFrictionPaPerM` / `aspectMax` / `source`; `pressureClasses[]` (DW/144 A–D); `standardSizesMm` per region; `gaugeBreakpoints[]` (width → thickness + seam code); `defaultAspect` (1.5)
- **Pipe**: `services[]` (chw / hws / lhw / dcw / dhw / dhw_circ / condensate / refrig_gas / refrig_liq / steam / natural_gas) with per-service `maxVelocityMs` and `maxPaPerM`; `standardBoreMm` per region
- **Conduit / cable tray**: `maxFillPct` (45% / 50% per BS 7671 + industry rule)
- **Sizing strategy**: 4 options (velocity / equal_friction / static_regain / constant_pressure)
- **Balancing**: `maxIterations`, `tolerancePa`, `dampingFactor`, `minBranchFlowLs` (consumed by `MEPBalancingEngine` once refactored to read from registry — currently still hardcoded)
- **Acoustics**: `ncTargets[]` (office 35, meeting 30, patient 30, OR 35, classroom 30, library 30, restaurant 40, plantroom 75) per CIBSE TG6 / ASHRAE A48

Edit either JSON in a text editor and click **RPRT → Reload rules** to pick up changes without restarting Revit.

### Caveats

1. Built without `dotnet build` verification (Linux sandbox). Verify in Revit before merge.
2. Phase 181 wired the sizing engines (`MepAutoSizeDuctCommand`, `MepAutoSizePipeCommand`, `MepAutoSizeConduitCommand`), the balancing engine (`MEPBalancingEngine.BalanceSystem` with a new `Document`-aware overload) and the fitting-loss dictionary (`FittingLossCalculator` with a JSON-overlay path) through `MepSizingRegistry`. Hardcoded constants remain as `*Fallback` safety nets only. Per-element segment-role / pipe-service detection (rather than the project-wide `branch` / `chw` defaults) is still pending — the data path is in place.
3. `Hvac_RunLoads` (`Commands/Hvac/HvacRunLoadsCommand`) posts `PostableCommand.AnalyzeHeatingAndCoolingLoads` after an MEP-Spaces pre-flight. `Hvac_ExportGbxml` (`Commands/Hvac/HvacExportGbxmlCommand`) calls `Document.Export` with `GBXMLExportOptions` after a 3D-view check. Both are real, no TaskDialog stubs.
4. The EQPT / SYS / SpoolGrid / DriftGrid / WorkflowGrid `ObservableCollection`s start empty — commands push rows back into the panel singleton (`StingHvacPanel.Instance`) on completion (same pattern `StingElectricalPanel` uses).
5. PaneGuid `D7E8F9A0-B1C2-3D4E-5F60-1A2B3C4D5E6F` is stable from this point so users' Revit `UIState.dat` re-locates the panel between sessions.

## Template Manager Intelligence Engine

`TemplateManagerCommands.cs` (3,892 lines) provides a deep template automation engine with 18 commands and an `internal static class TemplateManager` (~867 lines) intelligence core.

### TemplateManager Engine

**5-Layer Auto-Assignment Algorithm** (evaluated in order, first match wins):
1. **Name pattern matching** — 28 rules matching view name keywords to STING templates (e.g., "Mechanical" → "STING - Mechanical Plan")
2. **Level-aware overrides** — Level name keywords override template (e.g., "Basement" → Structural Plan, "Plant Room" → Mechanical Plan)
3. **Phase-aware mapping** — Phase name → template (e.g., "Existing" → As-Built Plan, "Demolition" → Demolition Plan)
4. **Scope box inference** — Falls back to scope box name if view name fails
5. **View type default** — Per-ViewType fallback (FloorPlan → Architectural, CeilingPlan → Ceiling RCP, etc.)

**Compliance Scoring** (10-point weighted scale):
HasTemplate, IsStingTemplate, HasFilters, FilterOverrides, DetailLevel, CorrectDiscipline, PhaseCorrect, VGConsistent, NoOrphans, ScaleAppropriate

**Style Definition Tables**: Fill patterns, line styles, text styles, dimension styles, object styles — all created programmatically from hardcoded AEC/ISO standard definitions.

### Template Manager Commands (18)

| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Auto-Assign Templates | `AutoAssignTemplatesCommand` | Manual | Apply 5-layer matching to all unassigned views |
| Template Audit | `TemplateAuditCommand` | ReadOnly | Audit all views for template assignment status |
| Template Diff | `TemplateDiffCommand` | ReadOnly | Compare VG settings between two templates |
| Compliance Score | `TemplateComplianceScoreCommand` | ReadOnly | Score all views on 10-point compliance scale |
| Auto-Fix Template | `AutoFixTemplateCommand` | Manual | Auto-repair template issues (missing filters, wrong detail level) |
| Sync Template Overrides | `SyncTemplateOverridesCommand` | Manual | Push VG overrides from template to all views using it |
| Create Fill Patterns | `CreateFillPatternsCommand` | Manual | Standard AEC fill patterns (hatching, crosshatch, etc.) |
| Create Line Styles | `CreateLineStylesCommand` | Manual | ISO-compliant line styles |
| Create Object Styles | `CreateObjectStylesCommand` | Manual | Standard object style definitions |
| Create Text Styles | `CreateTextStylesCommand` | Manual | AEC standard text styles |
| Create Dimension Styles | `CreateDimensionStylesCommand` | Manual | AEC standard dimension styles |
| Create VG Overrides | `CreateVGOverridesCommand` | Manual | Apply comprehensive VG overrides to templates |
| Batch Add Family Params | `BatchAddFamilyParamsCommand` | Manual | Add shared parameters to family documents in batch |
| Create Template Schedules | `CreateTemplateSchedulesCommand` | Manual | Create standard schedule templates |
| Template Setup Wizard | `TemplateSetupWizardCommand` | Manual | Multi-step guided template setup |
| Clone Template | `CloneTemplateCommand` | Manual | Deep clone template with VG, filters, and overrides |
| Batch VG Reset | `BatchVGResetCommand` | Manual | Reset VG settings across multiple views |
| Family Parameter Processor | `FamilyParameterProcessorCommand` | Manual | Batch process .rfa family files to add/update shared parameters |

## Sheet Manager System

`Docs/SheetManager*.cs` + `Docs/SheetTemplate*.cs` + `Docs/SheetSetCommands.cs` (7 files, ~4,488 lines) provides comprehensive automated sheet and viewport management with 24 commands across 3 phases.

### Architecture

| Component | File | Lines | Description |
|-----------|------|-------|-------------|
| Core Engine | `SheetManagerEngine.cs` | 1,041 | Drawable zone detection, scale calculation, shelf packing, collision detection, viewport placement, sheet cloning, naming/numbering, auto-arrange, batch operations |
| Extended Engine | `SheetManagerEngineExt.cs` | 943 | MaxRects bin packing (BSSF heuristic), layout presets (JSON persistence), viewport type rules, batch clone/renumber, overflow handling |
| Template Engine | `SheetTemplateEngine.cs` | 858 | 6 built-in sheet templates, create/save templates, ISO 19650 compliance (10 rules), viewport grid alignment, edge alignment, distribution, batch PDF export, sheet register |
| WPF Dialog | `SheetManagerDialog.cs` | 830 | Dual-panel dialog: TreeView (sheets grouped by discipline, viewport children, unplaced views) + context-sensitive detail panel |
| Phase 1 Commands | `SheetManagerCommands.cs` | 849 | 8 commands for core sheet management operations |
| Phase 2 Commands | `SheetSetCommands.cs` | 548 | 8 commands for advanced layout, presets, and batch operations |
| Phase 3 Commands | `SheetTemplateCommands.cs` | 419 | 8 commands for templates, compliance, alignment, and export |

### Sheet Manager Commands (24)

| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Sheet Manager | `SheetManagerCommand` | Manual | Open dual-panel WPF sheet manager dialog |
| Auto Layout | `AutoLayoutCommand` | Manual | Auto-arrange viewports using shelf-packing algorithm |
| Clone Sheet | `CloneSheetCommand` | Manual | Clone sheet with viewports (delete+recreate pattern) |
| Place Unplaced | `PlaceUnplacedViewsCommand` | Manual | Place unplaced views on new or existing sheets |
| Optimal Scale | `OptimalScaleCommand` | Manual | Calculate optimal viewport scale for drawable zone |
| Sheet Audit | `SheetAuditCommand` | ReadOnly | Audit sheets for empty/missing viewports |
| Batch Arrange | `BatchArrangeCommand` | Manual | Auto-arrange viewports across multiple sheets |
| Move Viewport | `MoveViewportCommand` | Manual | Move viewport between sheets (delete+recreate) |
| MaxRects Layout | `MaxRectsLayoutCommand` | Manual | Layout viewports using MaxRects bin packing (BSSF) |
| Save Layout Preset | `SaveLayoutPresetCommand` | ReadOnly | Save current sheet layout as named JSON preset |
| Apply Layout Preset | `ApplyLayoutPresetCommand` | Manual | Apply saved layout preset to active sheet |
| Batch Clone | `BatchCloneSheetsCommand` | Manual | Clone multiple sheets at once |
| Batch Renumber | `BatchRenumberSheetsCommand` | Manual | Two-pass renumber sheets within discipline groups |
| Auto VP Types | `AutoAssignVPTypesCommand` | Manual | Auto-assign viewport types by 7 built-in rules |
| Export Sheet Set | `ExportSheetSetCommand` | ReadOnly | Export sheet set to CSV |
| Place With Overflow | `PlaceWithOverflowCommand` | Manual | Place views with auto-overflow to continuation sheets |
| Create From Template | `CreateFromTemplateCommand` | Manual | Create sheet from built-in or saved template |
| Save Sheet Template | `SaveSheetTemplateCommand` | ReadOnly | Save current sheet as reusable template |
| Sheet Compliance | `SheetComplianceCheckCommand` | ReadOnly | ISO 19650 sheet compliance audit (10 rules) |
| Grid Align | `GridAlignViewportsCommand` | Manual | Snap viewport centres to alignment grid |
| Align Edges | `AlignViewportEdgesCommand` | Manual | Align viewport edges (left/right/top/bottom/center) |
| Distribute | `DistributeViewportsCommand` | Manual | Distribute viewports evenly across sheet |
| Batch Print | `BatchPrintSheetsCommand` | ReadOnly | Export sheets to PDF (all/discipline/selection) |
| Sheet Register | `ExportSheetRegisterCommand` | ReadOnly | Export comprehensive sheet register CSV with compliance |

### Key Engine Capabilities

- **Drawable zone detection**: Finds usable area within title block (excluding revision schedule, title strip)
- **Shelf packing**: Row-based bin packing for auto-layout with configurable margins
- **MaxRects packing**: Best Short Side Fit heuristic for optimal space utilisation
- **Collision detection**: 2D AABB overlap checking between viewports
- **Layout presets**: 6 built-in presets (Single View, Side by Side, Stacked, Plan+2 Sections, 4-Up Grid, Plan+Legend+Detail) + user-saved JSON presets
- **Sheet templates**: 6 built-in templates (Single Plan, Plan+Sections, Elevations 4-Up, MEP Plan, Detail Sheet, Coordination Sheet) with normalised slot positions
- **ISO 19650 compliance**: 10 rules (empty number/name, duplicates, format, title block, viewport count, case, special chars)
- **Grid alignment**: Configurable grid with snap-to-nearest for consistent viewport positioning
- **Edge alignment**: 6 modes (left, right, top, bottom, center horizontal, center vertical)
- **Batch PDF export**: By scope (all/discipline/selection) with sanitised filenames
- **Two-pass rename**: Avoids Revit sheet number conflicts during batch renumbering

## Model Auto-Modeling Engine

`Model/` directory (3 files, 3,260 lines) provides automated Revit element creation and DWG-to-BIM conversion with 16 commands.

### ModelEngine — `Model/ModelEngine.cs` (1,336 lines)
- Orchestrates creation of all architectural and MEP elements
- `CreateWall(doc, start, end, height, typeId)` — creates straight wall between two points
- `CreateFloor(doc, profile, typeId, levelId)` — creates floor slab from boundary profile
- `CreateFloorInRoom(doc, room)` — auto-creates floor matching room boundary
- `CreateRoof(doc, profile, typeId, levelId)` — creates roof element
- `BuildingShell(doc, width, depth, height)` — one-click building enclosure (4 walls + floor + roof)
- Contains `FamilyResolver` — resolves family types by name/category from document
- Contains `WorksetAssigner` — assigns elements to appropriate worksets
- Contains `FailureHandler` — `IFailuresPreprocessor` for suppressing non-critical warnings
- All dimensions in millimeters externally, converted to Revit internal feet via `Units` constants

### CADToModelEngine — `Model/CADToModelEngine.cs` (823 lines)
- DWG-to-BIM conversion engine for automated model creation from CAD imports
- `LayerMapper` — maps DWG layer names to Revit categories using 18 pattern groups (wall, door, window, column, beam, slab, roof, stair, ceiling, furniture, plumbing, duct, pipe, electrical, grid, dimension, text, annotation)
- Geometry extraction: parallel line detection (→ walls), closed loop detection (→ floors), block recognition (→ doors/windows)
- `ExtractGeometry(doc, importInstance)` — extracts categorized geometry from imported DWG
- `CreateElements(doc, extractionResult)` — creates Revit elements from extracted geometry

### Model Commands (16)

| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Create Wall | `ModelCreateWallCommand` | Manual | Create straight wall between 2 picked points |
| Create Room | `ModelCreateRoomCommand` | Manual | Create rectangular room enclosure (4 walls + floor + Room) |
| Create Floor | `ModelCreateFloorCommand` | Manual | Create floor slab from size preset or room boundary |
| Create Ceiling | `ModelCreateCeilingCommand` | Manual | Create rectangular ceiling slab |
| Create Roof | `ModelCreateRoofCommand` | Manual | Create roof element |
| Place Door | `ModelPlaceDoorCommand` | Manual | Place door families at picked locations |
| Place Window | `ModelPlaceWindowCommand` | Manual | Place window families in walls |
| Place Column | `ModelPlaceColumnCommand` | Manual | Place structural columns at picked points |
| Column Grid | `ModelColumnGridCommand` | Manual | Create array of columns in grid pattern |
| Create Beam | `ModelCreateBeamCommand` | Manual | Create structural beam between two points |
| Create Duct | `ModelCreateDuctCommand` | Manual | Create HVAC duct run along picked path |
| Create Pipe | `ModelCreatePipeCommand` | Manual | Create plumbing/mechanical pipe run |
| Place Fixture | `ModelPlaceFixtureCommand` | Manual | Place MEP fixtures (HVAC units, panels, receptacles) |
| Building Shell | `ModelBuildingShellCommand` | Manual | One-click building enclosure (walls + floor + roof) |
| DWG to Model | `ModelDWGToModelCommand` | Manual | Auto-convert imported DWG to Revit elements |
| DWG Preview | `ModelDWGPreviewCommand` | ReadOnly | Preview extracted geometry from DWG before conversion |

## Tag Style Engine

`Tags/TagStyleEngine.cs` (1,007 lines) + `Tags/TagStyleCommands.cs` (752 lines) provide comprehensive tag visual appearance control through a parameter-driven style matrix with 9 commands.

### Style Matrix
Tag families contain label rows bound to `TAG_{SIZE}{STYLE}_{COLOR}_BOOL` parameters. Exactly one BOOL parameter is set to true per element type, making that label row visible:
- **Sizes**: 2, 2.5, 3, 3.5 (mm text height)
- **Styles**: NOM (normal), BOLD, ITALIC
- **Colors**: BLACK, BLUE, GREEN, RED
- **Total combinations**: 4 sizes × 4 styles × 8 colors = **128 per tag** (all now registered in MR_PARAMETERS.txt)

### Built-in Color Schemes
Discipline, Warm, Cool, Red, Yellow, Blue, Monochrome, Dark — each scheme maps discipline codes to specific element graphic overrides and optionally switches tag text styles to match.

### Tag Style Commands (9)

| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Apply Tag Style | `ApplyTagStyleCommand` | Manual | Multi-step dialog (size → style → color) to apply specific tag style |
| Apply Color Scheme | `ApplyColorSchemeCommand` | Manual | Apply named color scheme to active view (elements + optional tag styles) |
| Clear Color Scheme | `ClearColorSchemeCommand` | Manual | Remove all graphic overrides from active view |
| Set Paragraph Depth Ext | `SetParagraphDepthExtCommand` | Manual | Extended paragraph depth control (1-10 tiers) |
| Tag Style Report | `TagStyleReportCommand` | ReadOnly | Report current tag style status per element type |
| Switch Tag Style by Disc | `SwitchTagStyleByDiscCommand` | Manual | Discipline-aware tag style switching (M→Blue, E→Gold, P→Green, etc.) |
| Batch Apply Color Scheme | `BatchApplyColorSchemeCommand` | Manual | Apply color scheme across all views in project |
| Color by Variable | `ColorByVariableCommand` | Manual | Color elements by any parameter value (numeric range or categorical) |
| Set Box Color | `SetBoxColorCommand` | Manual | Set individual tag box/border color properties |

## Development Workflow

### Building

```bash
# Set Revit API path and build
dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"
```

### Deployment

1. Build the project to produce `StingTools.dll`
2. Copy `StingTools.addin` to Revit addins directory:
   - Per-machine: `C:\ProgramData\Autodesk\Revit\Addins\2025\`
   - Per-user: `%APPDATA%\Autodesk\Revit\Addins\2025\`
3. Copy `StingTools.dll` + `Newtonsoft.Json.dll` + `ClosedXML.dll` + `data/` folder to the assembly path
4. Restart Revit to load the plugin

### Branching

- The default branch is `main`
- Feature branches: `feature/<description>` or `claude/<session-id>`
- Always create feature branches from the latest `main`
- Never commit directly to local `main` — branch off it, PR back. Direct
  commits drift the local copy from origin and surface as "diverged" the
  next time you try to pull or push.

### Commits

- Write clear, concise commit messages in imperative mood
- Keep commits focused — one logical change per commit
- Do not commit secrets, credentials, `.env` files, or API keys

### Pull Requests

- PRs should have a descriptive title and summary
- Include a test plan when applicable

## Conventions for AI Assistants

### General Rules

1. **Read before editing** — Always read a file before modifying it
2. **Prefer edits over rewrites** — Use targeted edits instead of rewriting entire files
3. **Don't over-engineer** — Keep changes minimal and focused on what was requested
4. **No unnecessary files** — Don't create documentation, config, or helper files unless explicitly asked
5. **Security first** — Never commit secrets; protect any API keys

### C# / Revit API Style

- Follow existing naming conventions: `PascalCase` for public members, `camelCase` for locals
- Always wrap DB modifications in `Transaction` blocks with descriptive names (prefix with "STING")
- Use `[Transaction(TransactionMode.Manual)]` for state-changing commands
- Use `[Transaction(TransactionMode.ReadOnly)]` for query-only commands
- Add `[Regeneration(RegenerationOption.Manual)]` for commands that modify the model (used on most commands)
- Use `TaskDialog` for user-facing messages (not `MessageBox`)
- Use `StingLog.Info/Warn/Error` for all logging — never use silent catch blocks
- Dispose of Revit API objects properly
- Handle `OperationCanceledException` for user-cancelled operations
- Use `FilteredElementCollector` with appropriate filters for performance
- For selection commands, use `uidoc.Selection.SetElementIds()` to set the selection
- For new commands, use the shared helpers: `TagConfig.BuildAndWriteTag()`, `ParameterHelpers.SetIfEmpty()`, `CategorySelector.SelectByCategory()`, `SpatialAutoDetect.DetectLoc()/DetectZone()`

### Multi-file Command Patterns

The codebase uses two patterns for organising commands:
1. **One class per file** — for complex commands (e.g., `CombineParametersCommand.cs`, `MasterSetupCommand.cs`, `PreTagAuditCommand.cs`)
2. **Multiple classes per file** — for related simple commands (e.g., `CategorySelectCommands.cs` has 15 selectors, `TokenWriterCommands.cs` has 7 commands, `TagOperationCommands.cs` has 39 commands)

When adding new commands, follow the existing pattern for the directory. Use shared `internal static` helper classes (e.g., `CategorySelector`, `TokenWriter`, `CompoundTypeCreator`, `MaterialPropertyHelper`, `LeaderHelper`, `ScheduleHelper`, `FormulaEngine`) to reduce duplication.

### Data File Conventions

- CSV files use standard comma-separated format with quoted fields
- JSON files should be well-formatted with consistent indentation
- When modifying data files, preserve the existing structure and column order
- Data files are read at runtime from the `data/` directory alongside the DLL
- Use `StingToolsApp.FindDataFile(fileName)` to locate data files
- Use `StingToolsApp.ParseCsvLine(line)` to parse CSV lines with quoted fields

### Testing

- Revit plugins run inside Revit — test by loading the plugin and exercising each command
- Validate changes in Revit with a test project before committing
- Ensure commands handle missing/null elements gracefully (Revit models vary widely)
- Do not mark a task as complete if the code has syntax errors or does not compile

### Git Safety

- Never force-push without explicit permission
- Never run destructive git commands (`reset --hard`, `clean -f`, `branch -D`) without confirmation
- Always commit to the correct branch — verify before pushing

## Dependencies and Build Configuration

- **Revit API**: `RevitAPI.dll`, `RevitAPIUI.dll` (referenced via `$(RevitApiPath)` — not distributed, `Private=false`; auto-detects Revit 2025 → 2026 → 2027)
- **Newtonsoft.Json**: v13.0.3 (NuGet package)
- **ClosedXML**: v0.104.2 (NuGet package — XLSX/BOQ export)
- **Target framework**: `net8.0-windows` (Revit 2025+), `LangVersion=latest`
- **WPF**: Enabled (`UseWPF=true` in csproj) for dockable panel UI and `System.Windows.Media.Imaging`
- **Output**: Library (DLL), `AppendTargetFrameworkToOutputPath=false`, `CopyLocalLockFileAssemblies=true`
- **Assembly**: v1.0.0.0, GUID `A1B2C3D4-5678-9ABC-DEF0-123456789ABC`, Vendor: Planscape
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` copied to output `data/` directory at build time

---

## Implemented Feature Spotlights

Deep-dive documentation for two multi-feature subsystems that grew out of
industry-tool surveys (GRAITEC, Naviate, BIMLOGiQ, etc.). For the
automation-gap backlog and future-enhancement list, see
[`docs/ROADMAP.md`](docs/ROADMAP.md); for historical implementation notes,
see [`docs/CHANGELOG.md`](docs/CHANGELOG.md).


### Color By Parameter (Graitec Lookup Style) — IMPLEMENTED

Inspired by GRAITEC PowerPack Element Lookup, Naviate Color Elements (Symetri), BIM One Color Splasher (open-source), DiRoots OneFilter Visualize, ModPlus mprColorizer, and Future BIM Colors by Parameters. Provides element coloring by any parameter value with full graphic control. Implemented in `Select/ColorCommands.cs` (907 lines) with `ColorHelper` engine and 5 commands.

#### Commands (Implemented)

| Command | Class | Transaction | Panel |
|---------|-------|-------------|-------|
| Color By Parameter | `Select.ColorByParameterCommand` | Manual | Select |
| Clear Color Overrides | `Select.ClearColorOverridesCommand` | Manual | Select |
| Save Color Preset | `Select.SaveColorPresetCommand` | ReadOnly | Select |
| Load Color Preset | `Select.LoadColorPresetCommand` | Manual | Select |

#### Core Capabilities

1. **Parameter Selection**: Color by ANY parameter (instance or type, text or numeric) — user picks from parameter list filtered by selected categories
2. **Scope**: Active view, selected elements, or entire project
3. **Graphic Override Options** (via `OverrideGraphicSettings`):

| Property | API Method | Description |
|----------|-----------|-------------|
| Projection line color | `SetProjectionLineColor(Color)` | Outline/edge color |
| Projection line weight | `SetProjectionLineWeight(int)` | Outline thickness (1-16) |
| Projection line pattern | `SetProjectionLinePatternId(ElementId)` | Dash, dot, etc. |
| Surface foreground color | `SetSurfaceForegroundPatternColor(Color)` | Fill color |
| Surface foreground pattern | `SetSurfaceForegroundPatternId(ElementId)` | Solid, crosshatch, etc. |
| Surface background color | `SetSurfaceBackgroundPatternColor(Color)` | Background fill |
| Surface transparency | `SetSurfaceTransparency(int)` | 0-100% transparency |
| Cut line color | `SetCutLineColor(Color)` | Section cut outline |
| Cut line weight | `SetCutLineWeight(int)` | Section cut thickness |
| Cut foreground color | `SetCutForegroundPatternColor(Color)` | Section cut fill |
| Halftone | `SetHalftone(bool)` | Greyed-out appearance |

4. **Color Palette Presets** (saved as JSON in `Data/COLOR_PRESETS.json`):

| Preset | Colors | Use Case |
|--------|--------|----------|
| STING Discipline | M=Blue, E=Yellow, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple, G=Brown | Discipline isolation |
| RAG Status | Red/Amber/Green | Compliance checking |
| Monochrome | Black/DarkGrey/MedGrey/LightGrey/White | Print-friendly |
| Spectral | Rainbow gradient (8-12 steps) | Continuous value ranges |
| Warm | Red to Orange to Yellow to Cream | Heat/load mapping |
| Cool | Navy to Blue to Cyan to Mint | Cooling/flow mapping |
| Pastel | Soft muted tones | Presentation views |
| High Contrast | Saturated primaries + black outlines | QA/checking |
| Accessible | Colorblind-safe (viridis-like) | Universal accessibility |
| Custom | User-defined | Project-specific |

5. **Preset Management**: Save/load/delete named presets, export/import between projects
6. **Legend Generation**: Auto-create a color legend (drafting view or schedule) showing parameter value to color mapping
7. **`<No Value>` Detection**: Elements with empty/null parameter values highlighted in distinct color (red) for instant QA
8. **Non-Modal Dialog**: Use `IExternalEventHandler` for modeless window pattern
9. **View Filter Generation**: One-click conversion of color scheme into persistent Revit `ParameterFilterElement` rules
10. **Selection Mode**: Click a parameter value in the dialog to select/isolate all matching elements

#### Implementation Pattern

```csharp
// CRITICAL: Find the solid fill pattern ONCE before the loop
FillPatternElement solidFill = new FilteredElementCollector(doc)
    .OfClass(typeof(FillPatternElement))
    .Cast<FillPatternElement>()
    .First(fp => fp.GetFillPattern().IsSolidFill);

// Build override settings per unique parameter value
OverrideGraphicSettings ogs = new OverrideGraphicSettings();
ogs.SetProjectionLineColor(color);
ogs.SetProjectionLineWeight(weight);
ogs.SetSurfaceForegroundPatternId(solidFill.Id);
ogs.SetSurfaceForegroundPatternColor(fillColor);
ogs.SetCutForegroundPatternId(solidFill.Id);
ogs.SetCutForegroundPatternColor(fillColor);
ogs.SetSurfaceTransparency(transparency);

// Apply in single transaction
using (Transaction t = new Transaction(doc, "STING Color By Parameter"))
{
    t.Start();
    foreach (ElementId id in matchingElements)
        view.SetElementOverrides(id, ogs);
    t.Commit();
}
```

---

### Smart Tag Placement — IMPLEMENTED

Inspired by BIMLOGiQ Smart Annotation, Naviate Tag from Template, and academic Automatic Label Placement (ALP) research. Provides collision-free automated tag annotation. Implemented in `Tags/SmartTagPlacementCommand.cs` (1,939 lines) with `TagPlacementEngine` engine and 9 commands.

#### Critical Distinction: Data Tagging vs Visual Tagging

| Layer | What It Does | Current State |
|-------|-------------|---------------|
| Data tagging | Writes DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ to element parameters | Implemented (AutoTag, BatchTag, TagAndCombine, FullAutoPopulate) |
| Visual tagging | Creates `IndependentTag` annotations in views displaying tag values | **Implemented** (SmartPlaceTags, ArrangeTags, RemoveAnnotationTags, BatchPlaceTags, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight) |

#### Implementation Architecture

**Smart Tag Placement Engine** (`Tags/SmartTagPlacementCommand.cs`)

```
Algorithm: Priority-Based Placement with Collision Avoidance
1. Collect all elements to tag in scope (view/selection/project)
2. For each element:
   a. Calculate element center point and bounding box in view
   b. Generate candidate positions (8 quadrants: N, NE, E, SE, S, SW, W, NW)
   c. Score each candidate:
      - Distance from element center (closer = better)
      - Overlap with existing tags (any overlap = penalty)
      - Overlap with other elements (penalty)
      - Alignment with nearby tags (bonus for grid alignment)
      - Preferred side bias (configurable per category)
   d. Place tag at highest-scoring position
   e. If all positions overlap, add leader line and extend search radius
   f. Register placed tag bounding box for future collision checks
3. Performance: use R-tree or grid-based spatial index for O(log n) overlap queries
```

**Phase 2: Tag Template System** (Naviate-inspired)

| Feature | Description |
|---------|-------------|
| Tag family priority | Each tag family gets priority positions (P1, P2, P3) |
| Category-specific rules | Different placement rules per category |
| Leader line thresholds | Auto-add leader when displacement exceeds distance |
| Alignment snapping | Snap tag heads to alignment grid |
| Template save/load | Save placement rules as project_config.json section |

#### Revit API Implementation Patterns

**Creating a tag:**
```csharp
IndependentTag tag = IndependentTag.Create(
    doc, tagTypeId, viewId,
    new Reference(element),
    addLeader,
    TagOrientation.Horizontal,
    headPosition
);
```

**Accurate tag extents (TransactionGroup workaround):**
```csharp
using (TransactionGroup tg = new TransactionGroup(doc, "MeasureTag"))
{
    tg.Start();
    using (Transaction t = new Transaction(doc, "Move"))
    {
        t.Start();
        tag.LeaderEndCondition = LeaderEndCondition.Free;
        XYZ leaderEnd = tag.GetLeaderEnd(reference);
        tag.TagHeadPosition = leaderEnd;
        tag.SetLeaderElbow(reference, leaderEnd);
        t.Commit();
    }
    BoundingBoxXYZ bb = tag.get_BoundingBox(view);  // NOW accurate
    tg.RollBack();
}
```

**Performance: disable annotations during bulk placement:**
```csharp
view.EnableTemporaryViewPropertiesMode(view.Id);
// ... place all tags ...
view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryViewProperties);
```

---

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
- [The Building Coder — Tag Extents](https://thebuildingcoder.typepad.com/blog/2022/07/tag-extents-and-lazy-detail-components.html) — Getting accurate tag bounding boxes
- [Revit API — OverrideGraphicSettings](https://www.revitapidocs.com/2025/eb2bd6b6-b7b2-5452-2070-2dbadb9e068a.htm) — Complete graphic override API
- [Revit API — IndependentTag](https://www.revitapidocs.com/2025/e52073e2-9d98-6fb5-eb43-288cf9ed2e28.htm) — Tag creation and positioning API

---

## Planscape Server

### Overview

**Planscape Server** is a cloud backend that transforms the single-machine Revit plugin into a multi-user, multi-tenant SaaS platform. Located in `Planscape.Server/`.

> **Gap Analysis**: See `Planscape.Server/docs/PLANSCAPE_GAPS.md` (655 lines) for comprehensive gap analysis covering mobile collaboration, server API, plugin sync integration, on-site readiness assessment, and prioritised implementation roadmap with cost estimates.

### Technology Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 8.0 (net8.0) |
| Database | PostgreSQL 16 + EF Core 8 |
| Cache | Redis 7 (caching + SignalR backplane) |
| Real-time | SignalR (ComplianceHub, TagSyncHub, NotificationHub) |
| Auth | JWT + Refresh tokens (BCrypt password hashing) |
| File Storage | MinIO (S3-compatible) |
| Email | MailKit SMTP (IEmailService) |
| Push | Firebase Cloud Messaging (FCM HTTP v1) |
| Background | Hangfire (4 recurring jobs) |
| Container | Docker Compose |

### Project Structure

```
Planscape.Server/
├── Planscape.sln                         # Solution file
├── docs/
│   └── PLANSCAPE_GAPS.md                 # Gap analysis: mobile, server, integration, readiness
├── docker/
│   ├── docker-compose.yml                # API + Postgres + Redis
│   └── Dockerfile                        # Multi-stage API build
└── src/
    ├── Planscape.API/                    # ASP.NET Core Web API (17 controllers)
    │   ├── Controllers/
    │   │   ├── AuthController.cs         # Login, register, refresh, change/forgot/reset password, /me, license
    │   │   ├── ProjectsController.cs     # CRUD + PUT settings + dashboard
    │   │   ├── ProjectMembersController.cs # Invite/remove/list team members with ISO 19650 roles
    │   │   ├── TagSyncController.cs      # Bulk tag sync from plugin (batch upsert, Redis caching)
    │   │   ├── ComplianceController.cs   # Snapshot push/pull + trend
    │   │   ├── IssuesController.cs       # CRUD + SLA tracking + attachments (upload/list/delete/link)
    │   │   ├── DocumentsController.cs    # CDE state machine + file upload/download + approval gates
    │   │   ├── NotificationsController.cs # Push token subscribe/list/delete + test push
    │   │   ├── WorkflowsController.cs    # Run logging + trend
    │   │   ├── MeetingsController.cs     # Agenda + action items
    │   │   ├── SeqSyncController.cs      # Max-per-key merge
    │   │   ├── TransmittalsController.cs # ISO 19650 transmittals
    │   │   ├── WarningsController.cs     # Warning reports + baseline
    │   │   ├── SearchController.cs       # Cross-project global search (tags, issues, docs, meetings)
    │   │   ├── PlatformController.cs     # BIM platform integrations (ACC, Procore, Aconex, Trimble)
    │   │   ├── AdminController.cs        # Org + user + audit management
    │   │   └── MimController.cs          # Planscape MIM asset lifecycle
    │   ├── Middleware/
    │   │   └── TenantResolutionMiddleware.cs  # Multi-tenant resolution with Redis-backed caching
    │   ├── SeedData.cs                   # Demo tenant + project + issues
    │   └── Program.cs                    # DI, middleware, SignalR hubs, Hangfire
    │
    ├── Planscape.Core/                   # Domain entities + DTOs (18 entities)
    │   ├── Entities/
    │   │   ├── Tenant.cs                 # Multi-tenant org
    │   │   ├── AppUser.cs                # JWT user with ISO 19650 role (UserRole enum)
    │   │   ├── Project.cs                # BIM project container
    │   │   ├── ProjectMember.cs          # User↔Project join with role
    │   │   ├── TaggedElement.cs          # 8-segment tag data
    │   │   ├── BimIssue.cs               # RFI/NCR/SI issue with SLA tracking
    │   │   ├── IssueAttachment.cs        # Join entity linking BimIssue to DocumentRecord
    │   │   ├── DocumentRecord.cs         # CDE document with state (CDEState enum)
    │   │   ├── DocumentApproval.cs       # ISO 19650-2 §5.6 approval workflow
    │   │   ├── PlatformConnection.cs     # External BIM platform connection config
    │   │   ├── ComplianceSnapshot.cs     # Point-in-time compliance
    │   │   ├── SeqCounter.cs             # Sequence number counter
    │   │   ├── Meeting.cs                # Meeting + action items
    │   │   ├── Transmittal.cs            # Document transmittal
    │   │   ├── WorkflowRun.cs            # Workflow execution record
    │   │   ├── LicenseKey.cs             # License with tier + seats (LicenseTier enum)
    │   │   ├── AuditLog.cs               # Write operation audit trail
    │   │   └── DevicePushToken.cs        # FCM/APNs device token (PushPlatform enum)
    │   ├── DTOs/SyncDtos.cs              # Sync request/response models
    │   └── Interfaces/
    │       ├── IRepository.cs
    │       ├── IEmailService.cs          # Email abstraction (SMTP/null fallback)
    │       ├── INotificationService.cs   # SignalR notification dispatch
    │       ├── IPlatformConnector.cs     # BIM platform integration (4 async methods)
    │       └── IPushNotificationService.cs  # Push notification abstraction (FCM/APNs/Web)
    │
    ├── Planscape.Infrastructure/         # EF Core + SignalR + Services
    │   ├── Data/PlanscapeDbContext.cs     # 21 DbSets, indexes, relationships
    │   ├── Data/Migrations/
    │   │   ├── 20250407000000_InitialCreate.cs      # Hand-written initial migration (all tables)
    │   │   └── PlanscapeDbContextModelSnapshot.cs   # EF Core model snapshot
    │   ├── Services/
    │   │   ├── TenantContext.cs           # ITenantContext with Redis-backed caching
    │   │   ├── NotificationService.cs     # SignalR + push notification dispatch
    │   │   ├── FirebasePushService.cs     # FCM HTTP v1 push (JWT auth, exponential retry)
    │   │   ├── SmtpEmailService.cs        # MailKit SMTP + NullEmailService fallback
    │   │   ├── PlatformConnectors.cs      # IPlatformConnector implementations (ACC, Procore, etc.)
    │   │   └── BackgroundJobs.cs          # Hangfire: compliance, SLA escalation, cleanup, platform sync
    │   └── SignalR/
    │       ├── ComplianceHub.cs           # Real-time compliance updates
    │       └── NotificationHub.cs         # Real-time notification delivery
    │
    ├── Planscape.MIM/                    # Model Information Management
    │   ├── Entities/Asset.cs             # 40+ field asset entity
    │   ├── Entities/MaintenanceTask.cs   # PPM scheduling per BS 8210
    │   └── Services/AssetService.cs
    │
    ├── Planscape.Shared/                 # Cross-cutting (plugin + server)
    │   ├── Constants/ISO19650Codes.cs    # DISC/SYS/FUNC/PROD/LOC/ZONE codes
    │   ├── Models/SyncModels.cs          # PluginSyncPayload DTOs
    │   └── Helpers/TagFormatHelper.cs    # Tag validation/parsing
    │
    └── Planscape.PluginSync/            # Plugin-side sync client (UNUSED — see Integration note)
        ├── SyncClient.cs                # HTTP + JWT auth client (dead code)
        ├── OfflineQueue.cs              # File-backed offline queue (dead code)
        └── SyncScheduler.cs             # 5-min periodic sync (dead code)
```

### API Endpoints Summary

| Area | Endpoint | Methods |
|---|---|---|
| Auth | `/api/auth/login`, `/register`, `/refresh`, `/change-password`, `/forgot-password`, `/reset-password`, `/me`, `/license/activate` | POST, GET |
| Projects | `/api/projects`, `/api/projects/{id}` | GET, POST, PUT |
| Project Members | `/api/projects/{id}/members` | GET, POST invite, DELETE remove |
| Dashboard | `/api/projects/{id}/dashboard` | GET |
| Tag Sync | `/api/tagsync/sync`, `/elements/{id}`, `/compliance/{id}` | POST, GET |
| Compliance | `/api/projects/{id}/compliance` | POST, GET (latest/history/trend) |
| Issues | `/api/projects/{id}/issues` | GET, POST, PUT + SLA report |
| Issue Attachments | `/api/projects/{pid}/issues/{iid}/attachments` | POST upload, GET list, DELETE, POST link |
| Documents | `/api/projects/{id}/documents` | GET, POST upload + CDE transition + download |
| Workflows | `/api/projects/{id}/workflows` | POST run, GET history/trend |
| Meetings | `/api/projects/{id}/meetings` | GET, POST + actions + open |
| SEQ Sync | `/api/projects/{id}/seq` | POST sync, GET counters |
| Transmittals | `/api/projects/{id}/transmittals` | GET, POST, PUT send |
| Warnings | `/api/projects/{id}/warnings` | POST report/baseline, GET trend |
| Notifications | `/api/notifications/subscribe`, `/tokens`, `/test` | POST, GET, DELETE |
| Search | `/api/search?q=` | GET (cross-project: tags, issues, docs, meetings) |
| Platform | `/api/projects/{id}/platform` | GET connections, POST connect/sync |
| MIM | `/api/projects/{id}/mim/assets`, `/maintenance`, `/dashboard` | GET, POST, bulk |
| Admin | `/api/admin/org`, `/users`, `/audit`, `/licenses` | GET, POST, PUT |
| SignalR | `/hubs/compliance`, `/hubs/tagsync`, `/hubs/notifications` | WebSocket |
| Health | `/health` | GET |

### Pricing Tiers

| Tier | Users | Price | Projects |
|---|---|---|---|
| Starter | 1 | Free | 1, local only |
| Professional | 1-5 | $15/user/mo | 5, cloud sync |
| Premium | 6-100 | $25/user/mo | Unlimited |
| Enterprise | 100+ | Custom | SSO, on-prem |
| Planscape MIM | Add-on | $10-17/user/mo | FM, digital twin |

### Plugin ↔ Server Sync Architecture

> **NOTE**: Two HTTP layers coexist. Both are wired into the plugin; consolidating
> them onto one client is INT-01 in `docs/ROADMAP.md`.

| System | Location | Role | Mechanism |
|---|---|---|---|
| `Planscape.PluginSync` | `Planscape.Server/src/Planscape.PluginSync/` | Background scheduler — wired to dock-panel sync chip and `PlatformSyncCommand` lazy-start (since Phase 90s); fires every 5 min once logged in | `SyncScheduler.SyncNow()` + `OfflineQueue` for store-and-forward |
| `PlanscapeServerClient` | `StingTools/BIMManager/PlanscapeServerClient.cs` | Manual on-demand sync triggered by BIM Coordination Center buttons; thread-safe singleton | Direct HTTP POST/GET to specific endpoints |

The `PlanscapeServerClient` (now ≈970 lines) covers:
- JWT login/token refresh via `/api/auth/login` and `/api/auth/refresh`
- Tag sync via `/api/tagsync/sync` (bulk POST)
- Compliance push via `/api/projects/{id}/compliance` (POST snapshot)
- Issue sync via `/api/projects/{id}/issues` (GET/POST + comments)
- Document register via `/api/projects/{id}/documents` (GET/POST + CDE transition)
- SEQ counter sync via `/api/projects/{id}/seq` (POST max-per-key merge)
- Workflow run logging via `/api/projects/{id}/workflows` (POST — auto-pushed by `WorkflowEngine` after every preset run, Phase 141)
- Warnings push via `/api/projects/{id}/warnings`
- Transmittal create / send via `/api/projects/{id}/transmittals` + bulk via `/transmittals/bulk` (Phase 142)
- Meeting create via `/api/projects/{id}/meetings` + bulk via `/meetings/bulk` (Phase 142)
- MIM asset bulk push via `/api/projects/{id}/mim/assets/bulk` (Phase 141)
- Model upload via `/api/projects/{id}/models`
- Platform connection listing
- All requests carry `X-Client-Type: plugin` for audit-source classification (Phase 141)

Bulk transmittal + bulk meeting endpoints landed in Phase 142, so the
batch sync paths the offline queue and workflow flush rely on are now
all single-round-trip. See `PLANSCAPE_GAPS.md` for the up-to-date open
list.

## Planscape Mobile App

### Overview

**Planscape Mobile** is a React Native / Expo cross-platform mobile app for on-site BIM coordination, issue management, and document access. Located in `Planscape/`.

### Technology Stack

| Layer | Technology |
|---|---|
| Framework | React Native + Expo SDK 52 |
| Router | expo-router v4 (file-based routing) |
| Language | TypeScript |
| Camera | expo-camera (QR/barcode scanning) |
| Storage | @react-native-async-storage (offline queue) |
| HTTP | Fetch API via `src/api/client.ts` |

### Mobile App Structure

```
Planscape/
├── app.json                              # Expo config
├── package.json                          # Dependencies
├── tsconfig.json                         # TypeScript config
├── app/
│   ├── _layout.tsx                       # Root layout (root navigation, push register on auth)
│   ├── login.tsx                         # Auth screen
│   ├── accept-invitation.tsx             # Tenant invite accept flow
│   ├── (tabs)/
│   │   ├── _layout.tsx                   # Tab bar layout (6 primary tabs)
│   │   ├── index.tsx                     # Dashboard (compliance gauge + sparkline)
│   │   ├── issues.tsx                    # Issues list + create with photo/GPS/assignee picker
│   │   ├── issue-detail.tsx              # Issue detail with comments, attachments, timeline
│   │   ├── documents.tsx                 # Documents list + CDE status + version history
│   │   ├── scanner.tsx                   # QR scanner — element lookup → issue pre-fill
│   │   └── settings.tsx                  # Settings (push toggle, biometric lock, theme)
│   ├── meetings/                         # Meeting list + agenda + actions
│   ├── transmittals/                     # Transmittal list + create + send
│   ├── warnings/                         # Warnings dashboard
│   ├── workflows/                        # Workflow run history
│   ├── models/                           # 3D model viewer (issue pin overlay)
│   ├── inbox/                            # "My Actions" aggregator (Phase 142)
│   ├── diary/                            # Daily site diary (Phase 142)
│   ├── conflicts/                        # Sync-conflict triage (Phase 143)
│   ├── heatmap/                          # Tag completeness heatmap (Phase 144, raw-SQL aggregator since Phase 145)
│   ├── stages/                           # RIBA stage gates + MIDP deliverables (Phase 144) + criterion sign-off (Phase 145, normalised table since Phase 146)
│   └── project-settings/                 # Project admin settings (Phase 144) + custom state machine (Phase 145)
└── src/
    ├── api/
    │   ├── client.ts                     # HTTP client (JWT, auto-refresh, X-Client-Type=mobile)
    │   └── endpoints.ts                  # Typed endpoint wrappers
    ├── components/                       # MemberPicker, TenantSwitcher, ErrorBoundary, …
    ├── hooks/                            # useAuth, useOfflineQueue, useQrScan
    ├── i18n/                             # FLEX-15 i18n loader
    ├── services/                         # apiClient, biometricLock, crashReporter,
    │                                     # documentCache, imageService, locationService,
    │                                     # notificationService, notificationTapRouter,
    │                                     # ocr, qrParser, realtimeClient (SignalR),
    │                                     # secureStorage, offlineQueue
    ├── stores/                           # Zustand: auth, issue, project, tenant, notification
    ├── theme/                            # Theme constants
    ├── types/                            # API DTO types
    └── utils/                            # offlineQueue (legacy), connectivity, theme
```

### Mobile Offline Queue

The mobile app supports 3 offline action types via `src/utils/offlineQueue.ts`:
- `CREATE_ISSUE` — Create new RFI/NCR/SI with photos
- `UPDATE_ISSUE` — Update existing issue status/priority
- `TRANSITION_CDE` — Change document CDE state

Queue is persisted to `AsyncStorage` and flushed when connectivity is restored.

### Mobile Gap Summary

> **2026-04-27 update:** the previous "prototype/demo stage" framing is out of
> date. The mobile app now ships production-grade implementations of every
> on-site critical path — camera + GPS + offline queue + SignalR + push
> notifications + biometric lock + image compression + document viewer +
> dashboard + meetings + transmittals + warnings + 3D viewer. See
> `Planscape.Server/docs/PLANSCAPE_GAPS.md` § 2.2 for the per-row status
> table; only MOB-11 (dark mode) and a partial MOB-12 (accessibility audit)
> remain open.

Phase 141 closed the last critical mobile gap: the Expo push token was
retrieved but never POSTed to the server, so the `DevicePushToken` table
stayed empty and pushes were dropped. `useAuth.login`, `useAuth.restoreSession`,
and `_layout.checkAuth` now all call `notificationService.register()` so the
token reaches the server on every cold start and after every login.

### Running Locally

```bash
# Server
cd Planscape.Server/docker
docker compose up -d
# API: http://localhost:5000
# Swagger: http://localhost:5000/swagger
# Demo login: admin@planscape.demo / admin123

# Mobile (requires Node.js + Expo CLI)
cd Planscape
npm install
npx expo start
```

