# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, BIM template automation, MEP engineering, photometrics, plumbing design, and full-lifecycle AEC/FM tooling for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

> Last refreshed after Phase 179 — Plumbing Center enhancement (8 tabs · 37 commands · 15 engines), Phase 180/181 — Photometric Library + DIALux Round-Trip, and Phases 178d/e/f — penetration coverage / seeds / damper+UL+acoustic.

- **Repository total**: 2,064 tracked files · 44 MB working tree (216 MB including `.git`)
- **Plugin assembly** `StingTools/`: 823 C# source files · 431,115 lines · 22 Core sub-directories · 30 Commands sub-directories
- **C# across the whole solution** (plugin + server + tests + tooling): 1,252 `.cs` files
- **Server backend** `Planscape.Server/`: 280 C# files, 38,581 lines (ASP.NET Core 8 + EF Core + SignalR + Hangfire)
- **Mobile app** `Planscape/`: 93 TS/TSX files, 17,019 lines (React Native + Expo SDK 52)
- **Documentation**: 68+ `.md` files (CLAUDE.md + docs/CHANGELOG.md + docs/ROADMAP.md + per-feature guides)
- **`IExternalCommand` class implementations**: 1,385+ across the plugin
- **Runtime / embedded data files** under `StingTools/Data/`: 348 files (CSV, JSON, TXT, XLSX, PY, MD, DOCX, RFA)
- **Dockable panels**: 3 standalone panels — Main (9 tabs), Electrical (dedicated), Plumbing (8 tabs) — plus BIM Coordination Center (13 tabs), Material Manager (7 tabs), Document Management Center (8 tabs), Placement Center (modeless)
- **Top-level workspace** ships 15 directories: `StingTools/` · `Planscape/` · `Planscape.Server/` · `StingBIM.Server/` · `StingTools.Clash.Tests/` · `StingTools.Dynamo/` · `StingTools.Headless/` · `StingTools.Standards/` · `Tests/` · `Families/` · `CompiledPlugin/` · `docs/` · `docs-site/` · `marketing-site/` · `tools/`

### Phase 179 — Plumbing Center

Branch: `claude/plumbing-enhancements-phase-17-UVb6z` (merged to master via PR #214).

Lifts the STING Plumbing Center from the Phase 178c 6-tab / 8-button prototype to a consultant-grade 8-tab / 37-command workflow that wires every existing engine to the panel and fills foundation gaps. Covers BS EN 806 / BS 8558 / BS 6700 / SFA Guide H / Approved Document H / CIBSE Guide G / BS 8515 / CIRIA C753 / HTM 04-01 / BS EN 12056 / BS EN 752.

| Sub-phase | Scope |
|---|---|
| **179a** | SYSTEM tab · `PlumbingSystemConfig` POCO + `PlumbingSystemConfigDialog` (modal WPF) · 3 JSON tables (`STING_PLUMBING_DRAINAGE_TABLES.json`, `STING_PLUMBING_SUPPLY_TABLES.json`, `STING_PIPE_MATERIALS_HYDRAULIC.json`) · `PlumbingTables` loader · 31 net-new `PLM_*` shared params · 2 commands (`Plumb_SaveSystemConfig`, `Plumb_LoadSystemConfig`) |
| **179b** | SUPPLY / DRAINAGE tabs · `FixtureUnitScanner` (per-type histogram + writebacks) · `WaterSupplySizer` (Hazen-Williams + Hunter / BS EN 806-3) · `ExpansionVesselSizer` (BS 7074-1) · 6 commands |
| **179c** | ROUTE tab · `PTrapInserter` (idempotent connector-graph trap detector + STING_SEED-style family resolver) · 5 wrapper commands routing into existing engines |
| **179d** | DRAINAGE tab extension · `InvertLevelEngine` (US/DS invert mAOD + cover depth + `PLM_DRN_INV_*` writeback) · 2 commands |
| **179e** | AUDIT + STORM tabs · `PlumbingComplianceScanner` (aggregate RAG dashboard) · `PlumbingSustainabilityCalc` (BS 8515 / CIRIA C753 / BRE 365 / BS EN 12566-1 / BS EN 12056-3) · 6 commands |
| **179f** | DOCS tab · `PlumbingBOQBuilder` (pipes-by-system+DN+material, fittings, accessories) · 5 commands |
| **+ Workflow** | `WORKFLOW_PlumbingDesign.json` (12-step end-to-end) · `WORKFLOW_PlumbingAudit.json` (6-step read-only) |
| **+ Wiring** | `StingPlumbingPanel.cs` rebuilt to 8 tabs (SYSTEM / SUPPLY / DRAINAGE / ROUTE / STORM / SPECIALTY / AUDIT / DOCS) · `StingPlumbingCommandHandler` covers all 37 tags · `WorkflowEngine.ResolveCommand` extended |

**Engines reused without modification**: `SleeveEngine`, `SlopeAutoCorrector`, `AutoPipeDrop`, `HangerPlacementEngine`, `BackflowClassifier`, `CrossConnectionChecker`, `DeadLegDetector`, `RecircLoopBalancer`, `StackCapacityValidator`, `PlumbingMaterialValidator`, `RainwaterHarvestingCalc`, `TrapDesigner`, `VentDesigner`, `DrainageSizer`, `FixtureUnitAggregator`.

Built without `dotnet build` verification (Linux sandbox). Verify in Revit before merge to `master`.

### Phase 180/181 — Photometric Library + DIALux Round-Trip

Closes the lighting design loop: model luminaires in Revit → export IFC 4 to DIALux evo → calculate → import IFC results back → colour-code rooms by pass / fail → emit Excel design review with "add N more fixtures" recommendations per room.

**New files**:

| File | Lines | Purpose |
|---|---|---|
| `Photometrics/IesParser.cs` | 236 | IESNA LM-63 (1986–2002) pure parser; beam/field angles, symmetry |
| `Photometrics/LdtParser.cs` | 189 | EULUMDAT pure parser; converts mm → metres; Isym tokens |
| `Photometrics/PhotometricFile.cs` | 70 | Common DTO (manufacturer, lumens, watts, efficacy, beam/field, CCT, CRI) |
| `Photometrics/PhotometricLibrary.cs` | 138 | Directory-scoped scanner + lazy cache |
| `Commands/Electrical/Photometric/PhotometricLibraryCommand.cs` | — | Modal viewer over IES / LDT library |
| `Commands/Electrical/Photometric/AssignPhotometricCommand.cs` | — | Stamps luminaire type with all photometric params |
| `Commands/Electrical/Photometric/PhotometricPreflightCommand.cs` | — | Read-only audit: missing IES bindings / reflectances / fixture placement |
| `Commands/Electrical/Photometric/DialuxRoundTripCommand.cs` | — | One-button orchestrator: preflight → IFC export → folder launch → dialog |
| `Commands/Electrical/Photometric/PhotometricDesignReviewCommand.cs` | — | Imports lux vs. BS EN 12464-1 / CIBSE LG7 / ASHRAE 90.1; colour-codes rooms; Excel report |
| `Commands/Electrical/IfcResults/IfcResultsImportCommand.cs` | — | Imports engine-specific lux from IFC file; matches by `UniqueId` or room-name |
| `Commands/Electrical/IfcResults/MultiEngineAggregatorCommand.cs` | — | Side-by-side DIALux / ElumTools / Relux / STING-estimate Excel comparison |
| `UI/PhotometricLibraryDialog.xaml(.cs)` | ~980 | Dark-theme 980×640 modal: search-filter, DataGrid, live detail, Assign button |

**14 new shared parameters** (`ELC_PHOTO_FILE_PATH_TXT`, `_LUMENS_NR`, `_WATTS_NR`, `_EFFICACY_LM_W`, `_BEAM_ANGLE_DEG`, `_CCT_K`, `_CRI_NR`, `_SYMMETRY_TXT`, `ELC_PHOTO_LUX_DIALUX_NR`, `_ELUMTOOLS_NR`, `_RELUX_NR`, `ELC_PHOTO_UNIFORMITY_NR`, `_LAST_ENGINE_TXT`, `_LAST_CALC_DATE_TXT`).

### Phases 178d/e/f — Penetration Coverage, Seeds & Deferred Items

**178d — Penetration coverage (floors + walls + beams)**:
- Group 33 `PEN_PENETRATION`: 19 new shared params (`PEN_FIRE_RATING_TXT`, `PEN_OD_MM`, `PEN_CONTROL_NUMBER_TXT`, `PEN_PFV_UUID_TXT`, `PEN_BEAM_DEPTH_RATIO`, etc.)
- New `WallPenetrationDetector` (fire-rated compartment walls via 2-D segment intersection)
- New `BeamPenetrationDetector` (3-D shortest-distance, AISC DG2 + BS EN 1992 classification)
- `FrpPenetrationPlacer` generalised to dispatch on `HostKind` (floor / wall / beam)
- `PenetrationRecord` UUIDv5 keyed on host+member for cross-pipeline pairing with `SleeveEngine`
- New commands: `Penetrations_DetectAndPlace`, `Validation_PenetrationCoverage`
- `WORKFLOW_PenetrationSweep.json` (5 steps), `WORKFLOW_PlumbingRoughIn.json` (8 steps)

**178e — Multi-service drops + seeds**:
- `DropEngineBase` gains `MultiServiceMode` + per-connector driver → a basin now drops cold + hot + waste in one pass
- Three new tier-3 seeds: `STING_SEED_PlumbingEquipment.json` (7 variants), `STING_SEED_MedGasOutlet.json` (7 HTM 02-01 variants), `STING_SEED_LabFixture.json` (8 BS EN 14056 variants)
- New `PlumbingConnectorCompletenessValidator` (audits expected connector counts; codes `PLM.CONN.*`)

**178f — Deferred-list completion**:
- Formula injection into symbol families via `SymbolDefinition.FormulaBindings`
- Section symbology auto-generation from elevation views
- Two new seeds: `STING_SEED_FireDamper.json` (BS EN 1366-2 / BS EN 15650) + `STING_SEED_AcousticSeal.json` (BS 8233 / ADE)
- `PenetrationProductSelector` dispatches on member-category + host-rating → correct seed family
- `ULSystemMatcher` resolves UL / EN-1366-3 system against `PEN_FIRE_RATING_TXT` + `PEN_OD_MM`
- Mobile `Planscape/app/penetrations/` flow (QR scan, GPS, photo, status chips, offline queue)
- Server: `PenetrationSignoff` entity + `PenetrationsController` (PUT signoff, GET dashboard)

## Documentation Map

| File | Purpose | When to edit |
|---|---|---|
| `CLAUDE.md` (this file) | **Stable reference** — architecture, directory layout, command catalogue, UI structure, build/deploy, conventions | When the codebase's structure or commands change |
| `docs/CHANGELOG.md` | **Phase-by-phase history** — every `Completed (Phase X)` block in chronological order | When a new phase of work lands; append a new `#### Completed (Phase N — …)` section |
| `docs/ROADMAP.md` | **Open gaps & future work** — automation-gap tables, future-enhancement lists, deep-review findings | When new gaps are identified or an item is closed (move it to `CHANGELOG.md`) |

When you finish a piece of work, log it in `docs/CHANGELOG.md` rather than extending this file. When you identify a new gap, add it to `docs/ROADMAP.md` — that keeps this file focused on what the code **is** rather than what it has been or might become.

---

## v4 MVP

**Status**: Phase 1 (parameters) → Phase 5 (fabrication) implemented. Code committed without `dotnet build` verification (Linux sandbox, no .NET / Revit API). Verify in Revit before merge.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Core/Placement/` | Fixture placement engine (rule + scorer + candidate + lighting grid + 20+ helpers) |
| `StingTools/Core/Routing/` | Auto-drop engines (DropEngineBase + AutoConduitDrop / AutoPipeDrop / AutoDuctDrop + penetration detectors + junction box + A* solver + voxel grid) |
| `StingTools/Core/Validation/` | Validators (connectivity / fill / spec / termination / slope + healthcare suite + penetration coverage) + ValidationResult record |
| `StingTools/Core/Fabrication/` | Fabrication coordinator + AssemblyGrouper + AssemblyBuilder + AssemblyViewBuilder + ShopDrawingComposer + IsoSymbolPlacer + per-discipline subfolders |
| `StingTools/Commands/Placement/` | PlaceFixturesCommand, LightingGridCommand, LearnPlacementV4Command + Placement Center commands |
| `StingTools/Commands/Routing/` | AutoDropCommand, GenerateLayoutCommand, ValidateFillsCommand + PenetrationsDetectAndPlaceCommand + HangerPlacement + CalcCommands |
| `StingTools/Commands/Validation/` | RunAllValidatorsCommand, PenetrationCoverageCommand |
| `StingTools/Commands/Fabrication/` | GenerateFabPackageCommand, ExportCutListCommand, ExportIsometricsCommand, ExportWeldMapCommand |
| `StingTools/Data/Placement/` | 18 STING_PLACEMENT_RULES.\*.json files + STING_HEIGHT_STANDARDS.json + STING_MANUFACTURER_CATALOGUE.json |
| `StingTools/Data/Fabrication/` | STING_FAB_RULES.json (6 disciplines) + STING_FAB_RULES_EXT.json + STING_ISO_SYMBOLS_INDEX.csv (180+ symbols) |
| `StingTools/Data/Parameters/` | STING_PARAMS_V4.txt · STING_PARAMS_V6.txt · STING_ELEC_WIRE_PARAMS.txt · STING_HANGER_PARAMS.txt · STING_SLEEVE_PARAMS.txt |
| `StingTools/Data/Seeds/` | 16 seed JSON specs |
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
| `Fabrication_PlaceISOSymbols` | inline TaskDialog | Fabrication |

---

## Electrical Panel (Standalone Dockable Panel)

The **STING Electrical Panel** (`UI/StingElectricalPanel.xaml` — 1,304 lines · `StingElectricalPanel.xaml.cs` — 924 lines · `StingElectricalCommandHandler.cs` — 570 lines · `StingElectricalPanelProvider.cs`) is a dedicated second dockable panel for MEP electrical engineering, backed by 54 command files (~10,676 lines) under `Commands/Electrical/` and the `Core/Electrical/` + `Core/Calc/` + `Core/SLD/` support engines.

### Electrical sub-systems

| Sub-system | Key files | What it does |
|---|---|---|
| **Cable Sizing** | `CableSizer/CableSizerCommand.cs` + `CableSizerEngine.cs` | BS 7671 / IEC 60364 cable sizing with derating; writes `ELC_CABLE_SIZE_TXT` |
| **Voltage Drop** | `VoltageDrop/VoltageDropCommand.cs` + `VoltageDropSolver.cs` + `VoltageDropScheduleCommand.cs` | Calculates and schedules voltage drop per circuit |
| **Feeder Sizing** | `FeederSizing/FeederSizerCommand.cs` + `FeederSizerEngine.cs` | Feeder cable sizing with diversity factor |
| **Fault Current** | `FaultCurrent/FaultCurrentCommand.cs` + `FaultCurrentEngine.cs` + `FaultCurrentScheduleCommand.cs` | Prospective fault current calculation; PSC / PSCC schedules |
| **Arc Flash** | `ArcFlash/ArcFlashCommand.cs` + `ArcFlashEngine.cs` + `ArcFlashLabelSheetCommand.cs` + `ArcFlashScheduleCommand.cs` | IEEE 1584 / NFPA 70E arc flash energy; label sheets; schedules |
| **Busbar Sizing** | `Busbar/BusbarModelingCommand.cs` + `BusbarSizerEngine.cs` | Busbar sizing + Revit modeling |
| **Conduit Routing** | `Routing/ConduitAutoRouteCommand.cs` + `ConduitRouteEngine.cs` + `ConduitConsolidator.cs` | Auto-route conduit with A* + ACO; consolidate into trays |
| **Cable Routing** | `Routing/CableScheduleBuilderCommand.cs` · `Core/Electrical/CableRouter.cs` + `CableManifest.cs` | Build cable schedules + route manifest |
| **Circuit Wizard** | `CircuitWizard/CircuitWizardCommand.cs` + `CircuitWizardEngine.cs` · `UI/CircuitWizardDialog.xaml(.cs)` | Step-by-step circuit assignment wizard |
| **Selective Coordination** | `Coordination/SelectiveCoordCommand.cs` + `SelectiveCoordEngine.cs` + `TccDatabaseLoader.cs` · `UI/SelectiveCoordDialog.xaml(.cs)` | TCC-based upstream/downstream breaker coordination |
| **Tray Fill** | `ShowTrayFillCommand.cs` · `Core/Electrical/TrayFillCalculator.cs` · `UI/TrayFillWindow.xaml.cs` | NEC 392 / BS EN 61537 tray fill visualisation |
| **Conduit Fill** | `ConduitFillValidateCommand.cs` · `Core/Calc/ConduitFillSolver.cs` | Conduit fill validation |
| **Phase Balance** | `PhaseBalanceCommand.cs` | Phase load balancing across panels |
| **Demand Factor Report** | `Reports/DemandFactorReportCommand.cs` | NEC / BS demand factor report |
| **Lighting** | `Lighting/LightingPowerDensityCommand.cs` + `EmergencyLightingAuditCommand.cs` | LPD calc (ASHRAE 90.1 / Part L) + emergency lighting audit |
| **Photometrics** | See Phase 180/181 section | IES/LDT library + DIALux round-trip |
| **SLD** | See SLD section below | Single Line Diagram generation |
| **Standards Validation** | `ElectricalStandardsValidatorCommand.cs` + `Core/Validation/ElectricalStandardsValidator.cs` | BS 7671 / NEC code compliance |
| **External Exports** | `Export/DIALuxExportCommand.cs` + `EtapExportCommand.cs` + `EasyPowerExportCommand.cs` · `ExternalExportEngine.cs` | Export IFC 4 / CSV to lighting / power analysis tools |
| **Panel Commands** | `ElectricalPanelCommands.cs` (321 lines) + `PanelViewScheduleCommand.cs` | Panel schedule creation and management |
| **Hanger Placement** | `Core/Calc/HangerPlacementEngine.cs` + related calc files | Cable hanger / support placement |
| **Cable Segregation** | `Commands/Routing/CableSegregationCommand.cs` · `Core/Calc/CableSegregationValidator.cs` | BS 7671 §528 / CENELEC segregation audit |

---

## SLD — Single Line Diagram

**Status**: Landed in `StingTools/Core/SLD/` + `StingTools/Commands/SLD/`.

### Files

| File | Lines | Purpose |
|---|---|---|
| `Core/SLD/SLDGenerator.cs` | 335 | Traverses circuit graph; emits Revit drafting-view SLD |
| `Core/SLD/SLDCircuitTraverser.cs` | 184 | BFS circuit traversal building `SLDNode` tree |
| `Core/SLD/SLDAnnotationPlacer.cs` | 152 | Places text notes + detail lines for bus bars, feeders, loads |
| `Core/SLD/SLDLayoutEngine.cs` | 86 | Column / row layout solver |
| `Core/SLD/SLDSyncUpdater.cs` | 115 | IUpdater-based live SLD refresh on parameter changes |
| `Commands/SLD/SLDGeneratorCommands.cs` | — | `SLD_Generate` + `SLD_ExportDXF` commands |
| `Commands/SLD/SLDRiserDiagramCommand.cs` | — | `SLD_Riser` — vertical riser diagram in drafting view |
| `Data/Symbols/STING_SLD_SYMBOLS.json` | — | SLD symbol catalogue (breakers, transformers, meters, buses) |

---

## Plumbing Center (Phase 179)

The **STING Plumbing Center** is a third standalone dockable panel (`UI/Plumbing/StingPlumbingPanel.cs` — 297 lines + `StingPlumbingPanelProvider.cs` + `StingPlumbingCommandHandler.cs` — 159 lines) with 8 tabs, 37 commands, and 15 engines across `Commands/Plumbing/` and `Core/Plumbing/`.

### Core Plumbing Engines (15)

| Engine | File (lines) | Standards |
|---|---|---|
| `WaterSupplySizer` | `Core/Plumbing/WaterSupplySizer.cs` (298) | Hazen-Williams + Hunter's method + BS EN 806-3; velocity / Pa-per-m audit |
| `DrainageSizer` | `Core/Plumbing/DrainageSizer.cs` (234) | Maguire formula + BS EN 12056-2 / BS EN 752 |
| `FixtureUnitScanner` | `Core/Plumbing/FixtureUnitScanner.cs` (136) | Per-type histogram; writes `PLM_DRN_DU` / `PLM_SUP_LU` / `PLM_SUP_WSFU` |
| `FixtureUnitAggregator` | `Core/Plumbing/FixtureUnitAggregator.cs` (202) | System-level DU/LU rollup |
| `ExpansionVesselSizer` | `Core/Plumbing/ExpansionVesselSizer.cs` (76) | BS 7074-1 |
| `InvertLevelEngine` | `Core/Plumbing/InvertLevelEngine.cs` (135) | US/DS invert mAOD + cover depth; writes `PLM_DRN_INV_*` |
| `PTrapInserter` | `Core/Plumbing/PTrapInserter.cs` (174) | Idempotent connector-graph trap detector + seed family resolver |
| `VentDesigner` | `Core/Plumbing/VentDesigner.cs` (121) | BS EN 12056-2 vent sizing |
| `TrapDesigner` | `Core/Plumbing/TrapDesigner.cs` (105) | Trap depth / anti-siphon rules (BS EN 274) |
| `BackflowClassifier` | `Core/Plumbing/BackflowClassifier.cs` (196) | BS EN 1717 fluid category classification |
| `DeadLegDetector` | `Core/Plumbing/DeadLegDetector.cs` (178) | HTM 04-01 / BS 8558 dead-leg audit |
| `RecircLoopBalancer` | `Core/Plumbing/RecircLoopBalancer.cs` (111) | DHW recirculation loop balancing |
| `StackCapacityValidator` | `Core/Plumbing/StackCapacityValidator.cs` (86) | BS EN 12056-2 stack capacity audit |
| `RainwaterHarvestingCalc` | `Core/Plumbing/RainwaterHarvestingCalc.cs` (102) | BS 8515 / CIRIA C753 / BRE 365 |
| `PlumbingComplianceScanner` | `Core/Plumbing/PlumbingComplianceScanner.cs` (130) | Aggregate RAG compliance result covering all sub-systems |

### Plumbing Commands (37 across 7 files)

| File (lines) | Commands |
|---|---|
| `PlumbingSystemCommands.cs` (87) | `Plumb_SaveSystemConfig`, `Plumb_LoadSystemConfig` |
| `PlumbingSizingCommands.cs` (282) | `Plumb_ScanFixtures`, `Plumb_SizeSupply`, `Plumb_SizeDrainage`, `Plumb_PressureCheck`, `Plumb_ExpVessel`, `Plumb_TMVRegister` |
| `PlumbingRoutingCommands.cs` (238) | `Plumb_AutoRoute`, `Plumb_FixSlopes`, `Plumb_InsertPTraps`, `Plumb_PlaceSleeves`, `Plumb_PlaceHangers` |
| `PlumbingDrainageDetailCommands.cs` (91) | `Plumb_VentDesign`, `Plumb_InvertLevels` |
| `PlumbingStormAndAuditCommands.cs` (146) | `Plumb_RWH`, `Plumb_SuDS`, `Plumb_Soakaway`, `Plumb_SepticTank`, `Plumb_RoofDrainage`, `Plumb_FullAudit` |
| `PlumbingDocsCommands.cs` (183) | `Plumb_PipeSchedule`, `Plumb_BOQ`, `Plumb_ManholeSchedule`, `Plumb_Isometric`, `Plumb_CommPack` |
| `PlumbingCommands.cs` (390) | 10 legacy Phase 178c commands (`Plumbing_*`) retained |

### Plumbing Data files (under `Data/Plumbing/`)

- `STING_PLUMBING_DRAINAGE_TABLES.json` — SFA / Maguire DU tables, stack capacity, inspection chamber sizing
- `STING_PLUMBING_SUPPLY_TABLES.json` — Hunter / BS EN 806-3 LU tables, pipe sizing, velocity limits
- `STING_PIPE_MATERIALS_HYDRAULIC.json` — Hazen-Williams C + roughness ε + max velocity per material
- `STING_PLUMBING_MATERIAL_RULES.json` — application rules (CWS / DHW / SAN / RWD / GAS per material)
- `STING_BS5422_INSULATION.csv` — BS 5422 pipe insulation thickness tables

### New shared parameters (31 `PLM_*`)

`PLM_DRN_DU`, `PLM_SUP_LU`, `PLM_SUP_WSFU`, `PLM_SUP_VEL_MS`, `PLM_SUP_DP_PA_M`, `PLM_SUP_FLOW_LS`, `PLM_SUP_DN`, `PLM_SUP_MATERIAL_TXT`, `PLM_PRES_STATIC_KPA`, `PLM_PRES_DYNAMIC_KPA`, `PLM_PRES_RESIDUAL_KPA`, `PLM_TMV_SET_TEMP_C`, `PLM_EXP_VESSEL_L`, `PLM_DRN_INV_US_M`, `PLM_DRN_INV_DS_M`, `PLM_DRN_SLOPE_PCT`, `PLM_DRN_DN`, `PLM_VENT_DN`, `PLM_TRAP_TYPE_TXT`, `PLM_TRAP_DEPTH_MM`, `PLM_BACKFLOW_CAT`, `PLM_DEAD_LEG_M`, `PLM_RECIRC_FLOW_LS`, `PLM_RWH_YIELD_M3_YR`, `PLM_STORM_ROOF_M2`, `PLM_AUDIT_STATUS_TXT`, `PLM_AUDIT_DATE`, `PLM_FIX_TYPE_TXT`, `PLM_FIX_COUNT`, `PLM_BOQ_ITEM_TXT`, `PLM_COMM_PACK_REF_TXT`

---

## Design Options

**Status**: Landed in `StingTools/Commands/DesignOptions/` (10 commands · 1,345 lines) + `StingTools/Core/DesignOptions/` (6 classes · 1,062 lines).

### Commands (10)

| Tag | Class | Description |
|---|---|---|
| `DesignOptions_Audit` | `AuditOptionsCommand` | Read-only audit: detect shared / option-hosted elements + cost/carbon summary |
| `DesignOptions_Dashboard` | `OptionsDashboardCommand` | WPF dashboard: all option sets + element counts + cost/carbon by option |
| `DesignOptions_Inspect` | `InspectCommand` | Read-only inspect: list option membership for selection |
| `DesignOptions_MoveToOption` | `MoveToOptionCommand` | Move selected elements to a specified design option |
| `DesignOptions_Clone` | `ClonePerOptionScheduleCommand` | Clone a schedule to each option for side-by-side comparison |
| `DesignOptions_CreateIsolationView` | `CreateIsolationViewCommand` | Create a view showing only a specified design option's elements |
| `DesignOptions_LockView` | `LockViewToOptionCommand` | Lock a view to display a specific design option |
| `DesignOptions_BatchSetLinkVisibility` | `BatchSetLinkOptionVisibilityCommand` | Batch-set visibility of linked models per option |
| `DesignOptions_ExportComparison` | `ExportOptionComparisonCommand` | Export option cost/carbon comparison to Excel |
| `DesignOptions_ClashView` | `CreatePrimaryOnlyClashViewCommand` | Create a view isolating primary option only for clash detection |

### Core classes

`DesignOptionRegistry` · `DesignOptionMetadata` · `DesignOptionParams` · `OptionCostCarbonCalculator` · `CascadeDeleteAnalyzer` · `OptionContext` / `OptionFolderManager` · `DesignOptionDashboardData`

### Workflows

- `WORKFLOW_OptionPackage.json` — 8-step package: audit → isolate → cost/carbon → clash → export comparison
- `WORKFLOW_OptionDecisionGate.json` — 5-step gate check before finalising design option decision

---

## Symbol Library + Seed Families

**Status**: Landed in `StingTools/Core/Symbols/` (15 classes · 3,925 lines) + `StingTools/Commands/Symbols/` (6 command files · 1,862 lines) + `StingTools/Data/Seeds/` (16 JSON specs) + `StingTools/Data/Symbols/` (9 JSON catalogues).

The Symbol Library is a data-driven engine that creates, maintains, and swaps parametric Revit families using `FamilyManager` + `FamilyItemFactory` APIs. All family geometry, connectors, parameters, variants, and formula bindings are declared in JSON.

### Core classes (15)

| Class | Purpose |
|---|---|
| `SymbolDefinition.cs` | POCO: geometry, connectors, type variants, formula bindings, section symbology |
| `SymbolConceptRegistry.cs` | Loads + indexes `STING_SYMBOL_CONCEPTS.json`; `GetById` / `GetByUniclass` |
| `SymbolStandardRegistry.cs` | Loads + indexes `STING_SYMBOL_STANDARDS.json`; resolves standard-vs-concept mapping |
| `SymbolStandardResolver.cs` | Resolves the "best" standard for a given context (country code + discipline + project tier) |
| `SymbolLibraryCreator.cs` | Core factory: `CreateFamilyDocument`, `DrawGeometry`, `DrawSectionGeometry`, `AddConnectors`, `AddParameters`, `AddFormulaBindings`, `MintTypeVariants` |
| `FamilyAugmentationEngine.cs` | Adds parameters/formulas to an existing loaded family |
| `CompoundSymbolPlacer.cs` (350) | Places multi-connector compound symbols |
| `SymbolAnnotationEngine.cs` | Places discipline-appropriate tag families on symbols |
| `SymbolCoverageAuditor.cs` | Audits what symbols are missing from the project vs. the concept registry |
| `SymbolDriftDetector.cs` | Detects symbols whose geometry / parameters have drifted from the JSON spec |
| `SymbolOrphanHealer.cs` | Re-links orphaned instances to the correct seed family |
| `SymbolOrientationEngine.cs` | Auto-orients symbols based on connected MEP elements |
| `SymbolViewContextResolver.cs` | Picks the correct symbol variant (plan / section / elevation) for a view type |
| `SymbolScaleEngine.cs` | Scale-aware symbol visibility and detail level resolution |
| `SymbolOverlayManager.cs` (194) | Manages overlay detail items placed on top of model symbols |
| `ULSystemMatcher.cs` | Resolves UL / EN-1366-3 certification reference for penetration seals |

### Seed Families (16 JSON specs, `Data/Seeds/`)

| Seed | Variants | Standards |
|---|---|---|
| `STING_SEED_PlumbingFixture.json` | WC / basin / shower / sink / urinal / bidet / bath | BS 6465-2; DCW+DHW+SAN connectors |
| `STING_SEED_PlumbingEquipment.json` | Calorifier / DHW cylinder / electric WH / booster set / manifold / expansion vessel / inline pump | BS 8558 / BS 6700; 4-connector union |
| `STING_SEED_MedGasOutlet.json` | O₂ / N₂O / Med Air / Surg Air / Vac terminal units + AVSU + alarm panel | HTM 02-01 / EN ISO 7396-1 / BS 5682 |
| `STING_SEED_LabFixture.json` | Fume hood / low-flow hood / BSL3 cabinet / eyewash / emergency shower / combo / lab gas tap / DI water tap | BS EN 14056 / ANSI Z358.1 / BS 7258 |
| `STING_SEED_FireDamper.json` | FR60 / FR90 / FR120 / motorised / combined-smoke (5) | BS EN 1366-2 / BS EN 15650 |
| `STING_SEED_AcousticSeal.json` | Rw45 / Rw55 / Rw63 / flexible-boot (4) | BS 8233 / Approved Doc E |
| `STING_SEED_AirTerminal.json` | Supply / return / exhaust / transfer (4) | BS EN 13779 |
| `STING_SEED_Sprinkler.json` | Pendant / upright / sidewall / ESFR (4) | BS EN 12845 |
| `STING_SEED_MechanicalEquipment.json` | AHU / FCU / DOAS / chiller / boiler / cooling tower (6) | CIBSE AM4 |
| `STING_SEED_ElectricalEquipment.json` | MDB / DB / SDB / MCB / MCCB / ACB / RCD (7) | BS 7671 |
| `STING_SEED_ElectricalFixture.json` | Switched socket / unswitched socket / MK grid / RCBO socket (4) | BS 7671 / BS 1363 |
| `STING_SEED_LightingFixture.json` | Recessed LED / surface LED / pendant / emergency / external (5) | BS EN 12464-1 |
| `STING_SEED_FireAlarmDevice.json` | Smoke detector / heat detector / manual call point / sounder / beacon (5) | BS 5839-1 |
| `STING_SEED_CommunicationDevice.json` | Data outlet / telephone / CCTV / access point / intercom (5) | BS EN 50173 |
| `STING_SEED_JunctionBox.json` | Standard / weatherproof / fire-rated / ATEX (4) | BS 7671 §522.8.5 |
| `STING_SEED_SpecialityEquipment.json` | Generic penetration seal (+ FireDamper / AcousticSeal via dispatcher) | EN 1366-3; UUIDv5 |

### Symbol Commands (6 files)

| File | Key commands |
|---|---|
| `BuildSeedFamiliesCommand.cs` | `Symbols_BuildSeeds` — builds all 16 seed families from JSON; tier-1/2/3 build order; post-build connector audit |
| `SwapToManufacturerCommand.cs` | `Symbols_SwapToManufacturer` — swaps seed instances to manufacturer families via `STING_FAMILY_SWAP_REGISTRY.json`; stamps `PEN_CERTIFICATION_TXT` with UL system |
| `SymbolLibraryCommands.cs` (327) | `Symbols_Audit`, `Symbols_CoverageScan`, `Symbols_HealOrphans`, `Symbols_DriftDetect` |
| `SymbolMaintenanceCommands.cs` | `Symbols_Sync`, `Symbols_SyncAll` |
| `SymbolStandardCommands.cs` | `Symbols_SetStandard`, `Symbols_InspectStandard` |
| `SymbolAugmentationCommands.cs` (153) | `Symbols_Augment`, `Symbols_BatchAugment` |

---

## BOQ — Bill of Quantities (Enhanced)

**Status**: `StingTools/BOQ/` (11 files · 7,228 lines).

| Class | Lines | Purpose |
|---|---|---|
| `BOQCostManager.cs` | ~800 | Core cost management engine: rate lookup, cost aggregation, VAT, contingency |
| `BOQParagraphEnhancer.cs` | 850 | Natural-language paragraph generation (NBS / NRM2 style) |
| `BOQModels.cs` | ~400 | POCOs: BOQItem, BOQSection, BOQPackage, TenderConfig, LabourRate |
| `BOQExportCommand.cs` | ~300 | Primary export: ClosedXML XLSX with NRM2 grouping + labour columns |
| `BOQProfessionalExportCommand.cs` | ~250 | PDF-quality export via Word merge |
| `BOQTemplateLibraryExtensions.cs` | ~200 | Client vocabulary substitution from `BOQ_CLIENT_VOCABULARY.json` |
| `BOQRateSourceHeatMapCommand.cs` | ~150 | Heatmap view showing rate-book vs. custom override coverage |
| `BOQSupportCommands.cs` | ~200 | `BOQ_RateAudit`, `BOQ_DeltaReport`, `BOQ_Validate` |
| `BOQTenderConfig.cs` | ~120 | Tender configuration POCO |
| `BOQPrepForExportCommand.cs` | 93 | Pre-export data normalization |
| `UI/BOQCostManagerPanel.cs` + `BOQCostManagerWindow.cs` + `BOQTenderDialog.cs` | ~600 | WPF cost manager panel + tender configuration dialog |

---

## Placement Center (Modeless)

**Status**: `StingTools/UI/PlacementCenter/` (5 CS files + `StingPlacementCenter.xaml(.cs)` — 1,878 lines + `StingPlacementCenter.xaml` — 2,825 lines · total 4,703 lines).

| File | Purpose |
|---|---|
| `StingPlacementCenter.xaml(.cs)` | Modeless MVVM window: rule browser, family selector, room/zone filter, placement history |
| `PlacementRulesViewModel.cs` | Exposes `PlacementRule` list from 18 JSON rule files |
| `PlacementRuleViewModel.cs` | Per-rule MVVM wrapper with pass/fail validation display |
| `PlacementCenterBridge.cs` | Bridges the modeless window to the Revit API thread via `IExternalEventHandler` |
| `FamilyHintsBridge.cs` | Provides family-name suggestions from `STING_MANUFACTURER_CATALOGUE.json` |
| `HistoryBridge.cs` | Persists placement history to `_BIM_COORD/placement_history.json` |
| `PlacementCentreCommands.cs` (32) | `Placement_OpenCenter` — opens the modeless window |
| `PlacementExcelCommands.cs` | `Placement_ExportRules`, `Placement_ImportRules` — Excel round-trip for placement rules |

---

## ExLink (External Data Link)

**Status**: `StingTools/ExLink/` (8 files · 5,723 lines).

| File | Lines | Purpose |
|---|---|---|
| `AutomationEngine.cs` | 855 | Automated data-push/pull scheduler (file-watcher + timer) |
| `ISBAppsCommands.cs` | 487 | ISB Apps integration: push/pull to ISB-compatible CAFM |
| `ExLinkEngine.cs` | ~600 | Core engine: property discovery, data normalisation, column mapping |
| `ExLinkCommands.cs` | ~400 | `ExLink_Export`, `ExLink_Import`, `ExLink_Audit`, `ExLink_BrowseData` |
| `ExLinkDefaultLinks.cs` | ~300 | 12 preconfigured link profiles (Archibus, Maximo, Planon, etc.) |
| `ExLinkPropertyDiscovery.cs` | ~400 | Auto-discovers Revit parameters + IFC properties for mapping |
| `ExplorerCommands.cs` | ~250 | `ExLink_BrowserLaunch` — launches `UI/ExLinkBrowserDialog.cs` |
| `StickyNotesEngine.cs` | ~350 | Persistent element-attached sticky notes (stored in ExtensibleStorage) |

---

## V6 — Next-Generation Features (Prototype)

**Status**: `StingTools/V6/` (15 files · 2,424 lines).

| File | Lines | Purpose |
|---|---|---|
| `ClashTriageEngine.cs` | 216 | AI-assisted clash triage: prioritises by discipline pair + element size |
| `ClashResolutionSuggester.cs` | 172 | Suggests resolution strategies (raise tray, re-route pipe, offset duct) |
| `ExcelBidirectionalSync.cs` | 218 | Full bidirectional Excel sync with conflict detection + merge UI |
| `SheetMatrixGenerator.cs` | 156 | Auto-generates sheet set matrix from project deliverable list |
| `FourdGanttReader.cs` | 156 | Reads MS Project .mpp / Primavera P6 XML into Revit 4D phase assignments |
| `HealthDashboardEngine.cs` | 157 | Composite model health score (12 metrics → single 0–100 score) |
| `CarbonStageTracker.cs` | 200 | RIBA stage carbon tracking with EC3 integration hooks |
| `AccIssueSync.cs` | 200 | Autodesk Construction Cloud issue bidirectional sync |
| `QRCommissioningCommands.cs` | 138 | QR code commissioning workflow commands |
| `QRCommissioningWorkflow.cs` | 139 | Mobile QR scan → Revit element → commissioning sign-off engine |
| `FederationLinkedWalker.cs` | 171 | Walks all Revit links recursively for cross-model validation |
| `LabourHoursCommands.cs` | 121 | Labour hours tracking from `Data/Labour/STING_LABOUR_RATES.csv` |
| `LabourHoursEngine.cs` | 128 | Labour hours aggregation by trade / level / system |
| `IfcPsetMapping.cs` | 120 | Advanced IFC property set mapping beyond standard IFC4 schema |

---

## Extensible Storage (ES) Layer

**Status**: `StingTools/Core/Storage/` (16 schema files · 2,093 lines) provides a typed ExtensibleStorage layer removing the dependency on shared parameters for internal plugin state.

| Schema | Purpose |
|---|---|
| `StingTagHistorySchema` | Per-element tag history: timestamp + value snapshots |
| `StingProvenanceSchema` | Data source provenance tracking |
| `StingClusterSchema` | Element clustering metadata (cluster ID + label + count) |
| `StingPositionSchema` | Normalised tag/annotation position relative to element |
| `StingComplianceBaselineSchema` | Per-element compliance baseline snapshot |
| `StingDrawingTypesSchema` | Per-view DrawingType ID + checksum + lock flag |
| `StingPackVersionSchema` | Plugin pack version stamp |
| `StingTagLearnedSchema` | ML-style learned placement preferences |
| `StingViewPresetSchema` | Per-view visual preset overrides |
| `StingWorkflowStateSchema` | Per-document active workflow state |
| `StingConnectorMetaSchema` | Connector MEP service metadata |
| `StingCostRateOverrideSchema` | Per-element cost rate overrides |
| `StingTokenLineageSchema` | Token derivation lineage |
| `StingValidatorSuppressionSchema` | Suppressed validator codes + suppression reason + expiry |
| `StingEsHelpers` | `GetOrCreate`, `Read<T>`, `Write<T>` generic helpers |
| `StingSchemaBuilder` | Declarative schema registration + migration strategy |

---

## Healthcare Pack (H-1..H-30)

**Status**: Full pack landed on `claude/research-hospital-design-0Uxbi`. See [`docs/HEALTHCARE_PACK_DESIGN.md`](docs/HEALTHCARE_PACK_DESIGN.md).

### What it adds at a glance

- **5 new shared-parameter groups** (28 `CLN_CLINICAL`, 29 `MGS_SYSTEMS`, 30 `RAD_PROTECTION`, 31 `CEQ_CLINICAL`, 32 `LIG_BEHAVIOURAL`); ~100 net-new shared parameters
- **3 new disciplines** (`H` Healthcare, `MG` Medical Gas, `RP` Radiation Protection); ~30 healthcare PROD codes; 60 tag families in `STING_TAG_CONFIG_v5_0_HEALTH.csv`
- **16 healthcare validators** under `Core/Validation/Healthcare/` gated through `HealthcareValidatorGate` against `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` (FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH)
- **7 standards modules** under `StingTools.Standards/{HTM, HBN, FGI, NFPA99, NCRP147, ASHRAE170, USP797800}` — stateless lookup tables + checklist generators + NCRP 147 W·U·T → mm-Pb calculator
- **22 corporate Drawing Types** with routing rules; 8 ViewStylePacks; 58 healthcare filters in `STING_AEC_FILTERS.json`
- **MGPS package** (`Core/MedGas/`) — `MgasNetwork` graph builder, `MgasFlowSolver` (NFPA 99 §5.1.13), `MgasVerificationLog` (12-step NFPA 99 §5.1.12)
- **RDS engine** (`Docs/Templates/Rds*`) — token-context builder + MiniWord renderer
- **40+ commands** under `Commands/Healthcare/`, `Commands/MedGas/`, `Commands/Adjacency/`, `Commands/Twin/`, `Commands/Radiation/`
- **8 workflow JSON presets** (HealthcareCommissioning, MgasVerification, PressureRegimeAudit, RdsIssue, HTM-04-01-Annual, AntiLigatureAudit, NFPA110-GeneratorTest, HTM-01-06-EndoReprocess)
- **COBie healthcare overlay** — 50 clinical equipment types, 16 systems, 70 picklist values, 35 PPM templates, 12 doc types, 26 spare-part templates
- **Mobile commissioning app** — `Planscape/app/healthcare/` (6 screens) + `Planscape/app/penetrations/` (QR + GPS + sign-off)
- **Server APIs** — `HealthcareController` + 4 entities + `PenetrationsController` + `PenetrationSignoff`
- **NLP processor** — 19 healthcare patterns added to `Tags/NLPCommandProcessor.cs`

### Caveats

1. `healthcare_rds.docx` template ships only as a README authoring guide
2. MGS family stubs ship parameter specs only — real `.rfa` files come from manufacturers
3. `TwinReadback` BACnet / OPC-UA transports are abstract stubs
4. `RAD_QE_NAME_TXT` sign-off remains mandatory before radiation calculators are treated as authoritative
5. EF migration not run yet — `dotnet ef migrations add HealthcarePack` is required
6. No dedicated Healthcare tab in the dock panel — commands dispatch via `WorkflowEngine.ResolveCommand` and `StingCommandHandler` button tags

---

## Electrical Panel Schedules (Phase 176)

**Status**: Landed on `claude/research-panel-schedules-J0qqo`.

### New folder

`StingTools/Commands/Panels/` — `PanelScheduleTemplateRegistry.cs`, `BatchPanelSchedulesCommand.cs`, `PanelScheduleAuditCommand.cs`, `PanelScheduleExcelCommands.cs`, `PanelScheduleSlotCommands.cs`.

### Commands (9)

| Tag | Class | Description |
|---|---|---|
| `Panel_BatchSchedules` | `BatchPanelSchedulesCommand` | Rule-based per-panel `PanelScheduleView.CreateInstanceView` with multi-template fallback, drawing-type stamp, ELC_PNL_* fill, circuit back-refs |
| `Panel_Audit` | `PanelScheduleAuditCommand` | Read-only audit: panels without schedules, template drift, missing PNL params |
| `Panel_ExportToExcel` | `ExportPanelSchedulesToExcelCommand` | Header + Body + Summary to `.xlsx` |
| `Panel_ImportFromExcel` | `ImportPanelSchedulesFromExcelCommand` | Round-trip Body cells via `TableSectionData.SetCellText` with empty-cell guard + load-delta diff |
| `Panel_FillSpares` | `FillEmptySlotsWithSparesCommand` | `AddSpare` on empty slots of active schedule |
| `Panel_FillSpaces` | `FillEmptySlotsWithSpacesCommand` | `AddSpace` variant |
| `Panel_FillSparesAll` | `FillSparesAllSchedulesCommand` | Project-wide `AddSpare` with `TransactionGroup` |
| `Panel_SpacesToSpares` | `ConvertSpacesToSparesCommand` | `RemoveSpace` + `AddSpare` |
| `Panel_ClearSparesSpaces` | `ClearSparesAndSpacesCommand` | Wipe spares and spaces |

### Data files

- `StingTools/Data/STING_PANEL_SCHEDULE_TEMPLATES.json` — 5 priority-ordered rules + skip patterns + `globalFallback`
- `StingTools/Data/WORKFLOW_PanelScheduleProduction.json` — workflow preset chaining Audit → BatchSchedules → FillSparesAll → ExportToExcel → re-Audit

### API limits honoured

- `PanelScheduleSheetInstance.Create` is broken in Revit 2024-2026 — STING does NOT attempt programmatic sheet placement
- `PanelScheduleTemplate` cell layout / column order / formulas remain read-only
- Real-circuit detection uses `PanelScheduleView.GetCircuitByCell(r, c)`

---

## Drawing Template Manager (Phase 113–138)

**Status**: Foundation through Phase 138 all landed. See CHANGELOG for the full week-by-week breakdown.

### Concept

A **DrawingType** is a named JSON bundle that answers every presentation question for a single produced drawing (paper size, title block, scale, view template, annotation rules, crop strategy, section marker, sheet numbering, viewport slots, title-block param binding).

The corporate catalogue ships in `Data/STING_DRAWING_TYPES.json` (40+ drawing types), `Data/STING_VIEW_STYLE_PACKS.json` (11+ packs), `Data/STING_AEC_FILTERS.json` (199 filters).

### Core Drawing classes (40+ files under `Core/Drawing/`)

`DrawingType.cs` · `DrawingTypePresentation.cs` · `DrawingTypeRegistry.cs` · `DrawingDispatcher.cs` · `DrawingTypeValidator.cs` · `DrawingTypeStamper.cs` · `DrawingDriftDetector.cs` · `DrawingCropApplier.cs` · `ViewStylePack.cs` · `ViewStylePackRegistry.cs` · `ViewStylePackApplier.cs` · `ManagedTemplateSyncer.cs` · `AecFilterDefinition.cs` · `AecFilterRegistry.cs` · `AecFilterFactory.cs` · `ScopeBoxBinder.cs` · `TitleBlockParamApplier.cs` · `TokenProfileApplier.cs` · `AnnotationRunner.cs` · `DrawingProducer.cs` · `DrawingPackageManager.cs` · `TitleBlockFactory.cs` · `TitleBlockSpec.cs` · `MatchLineEngine.cs` · `DrawingTokenContext.cs` · `DrawingProductionPreset.cs` · `ProductionPresetRegistry.cs` · `DrawingThumbnailService.cs` · `SheetPlacementBridge.cs` · `SheetSequenceStore.cs` · `Iso19650Vocabulary.cs` · plus dimensioning sub-engine (`GridDimensioner`, `MEPDimensioner`, `DrainageInvertDimensioner`, `DimensionStrategy`).

### Pipeline order (final)

`DrawingTypePresentation.Apply(doc, view, dt)`:
1. Lock check → 2. Stamp `STING_DRAWING_TYPE_ID_TXT` → 3. Scale → 4. Detail level → 5. View template → 6. Crop strategy → 7. View style pack → 7.5. Token profile → 8. Annotation pass.

For sheet creation: 9. DrawingType stamp → 10. Title-block param binding.

### Commands (20+)

`DrawingTypes_Inspect`, `DrawingTypes_Reload`, `DrawingTypes_BrowserOrganize`, `DrawingTypes_SyncStyles`, `DrawingTypes_FromScopeBoxes`, `DrawingTypes_Produce`, `DrawingTypes_Doctor`, `DrawingTypes_HealTitleBlocks`, `DrawingTypes_Renumber`, `DrawingTypes_BatchProduce`, `AecFilters_Create`, `AecFilters_Inspect`, `AecFilters_Reload`, `ManagedTemplates_Convert`, `ManagedTemplates_Detach`, `ManagedTemplates_Regenerate`, `TitleBlocks_Factory`, `TitleBlocks_MigrateCsv`, `TitleBlocks_Migrate`, `MatchLines_Create`, `PresentationStyle_Setup`.

### AEC/FM Corporate Filter Library (Phase 166 — 199 filters)

`Data/STING_AEC_FILTERS.json`: 47 Arch · 33 HVAC · 31 Struct · 30 Fire · 27 Elec · 18 Plumb · 11 FM/COBie · 8 ISO 19650 · 8 Coord/LOD · 5 VT · 5 QA. `ViewStylePackApplier.ApplyFilterRules` now lazy-creates missing filters from the registry.

---

## Template Engine v1.1 (Phase 112)

**Status**: S01–S18 landed. S19 (signature provider) and S20 (AI metadata extraction) deferred to v1.2.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Docs/Templates/` | MiniWord + ClosedXML render pipeline (14 files) |
| `StingTools/Docs/Workflow/` | WorkflowEngine + AuditLog + DistributionGroups (6 files) |
| `StingTools/Docs/Search/` | Lucene.NET document index + saved searches (2 files) |
| `StingTools/Docs/_template_sources/` | 16 embedded `.docx` / `.xlsx` templates (EmbeddedResource) |
| `StingTools/Docs/_workflow_sources/` | 5 embedded workflow JSONs (EmbeddedResource) |

### New commands (8)

`IssueDeliverable`, `ReIssueDeliverable`, `PublishDeliverable`, `CancelDeliverable`, `SupersedeDeliverable`, `ReplaceDeliverable`, `CreateTransmittalOrchestrated`, `BulkIssueDeliverables`.

### Runtime artefacts (per project, under `<project>/_BIM_COORD/`)

`templates/manifest.json` · `templates/*.docx/.xlsx` (16) · `workflows/*.json` (5) · `generated/` · `doc_sequences.json` · `deliverables.json` · `transmittals.json` · `workflow_state.json` · `audit_log_{yyyy}_{MM}.jsonl` · `distribution_groups.json` · `saved_searches.json` · `search_index/`

---

## Technology Stack

- **Platform**: Autodesk Revit 2025/2026/2027 (BIM software)
- **Language**: C# / .NET 8.0 (`net8.0-windows`), `LangVersion=latest`
- **Plugin type**: `IExternalApplication` + `IExternalEventHandler` + `IDockablePaneProvider` + `IUpdater` (×2) with `IExternalCommand` classes
- **Dependencies**: `Newtonsoft.Json` 13.0.3 · `ClosedXML` 0.104.2 · `ZXing.Net` 0.16.9 · `MiniWord` 0.9.0 · `Lucene.Net` 4.8.0-beta00016 + `Lucene.Net.Analysis.Common` · Revit API assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`)
- **Data formats**: CSV, JSON, TXT, XLSX, RFA seed specs for configuration and runtime data
- **Deployment**: `StingTools.addin` (XML manifest) + `extract_plugin.sh` (Bash)

---

## Directory Structure

```
STINGTOOLS/
├── CLAUDE.md                           # AI assistant guide (this file)
├── StingTools.addin                    # Revit addin manifest (XML)
├── build.bat / extract_plugin.sh       # Build and deployment scripts
│
└── StingTools/                         # C# project root (823 .cs · 431,115 lines)
    ├── StingTools.csproj               # .NET 8 project file
    ├── Properties/AssemblyInfo.cs      # Assembly metadata (v1.0.0.0)
    │
    ├── Core/                           # Shared infrastructure (22 sub-directories)
    │   ├── StingToolsApp.cs            # IExternalApplication — 3 dockable panels + events + ribbon
    │   ├── StingLog.cs                 # Thread-safe file logger + EscapeChecker
    │   ├── ParamRegistry.cs            # Single source of truth for parameters (PARAMETER_REGISTRY.json)
    │   ├── ParameterHelpers.cs         # Parameter read/write + SpatialAutoDetect + TokenAutoPopulator
    │   ├── SharedParamGuids.cs         # Backwards-compatible facade wrapping ParamRegistry
    │   ├── TagConfig.cs                # ISO 19650 tag lookup tables + TagIntelligence + TAG7 builder
    │   ├── StingAutoTagger.cs          # IUpdater — real-time auto-tagging + StingStaleMarker IUpdater
    │   ├── WorkflowEngine.cs           # Workflow orchestration — JSON preset chaining + 19 conditional operators
    │   ├── ComplianceScan.cs           # Cached compliance scan with per-discipline breakdown
    │   ├── WarningsManager.cs          # 150+ classification rules, 16 auto-fix strategies, SLA tracking
    │   ├── ProjectFolderEngine.cs      # ISO 19650 project folder structure + CDE folder generation
    │   ├── WorkflowMaturityEngine.cs   # Step dependency resolver + commissioning workflows
    │   │
    │   ├── Adjacency/                  # RoomGraphBuilder + CleanDirtyFlowSolver (healthcare)
    │   ├── Branding/                   # BrandTokens + CorporateBrand (STING_CORPORATE_BRAND.json)
    │   ├── Calc/                       # 15 engineering calc engines (voltage drop, conduit fill, Hardy Cross, hanger, slope, duct friction, cable segregation, circuit topology, network extractor, routing support)
    │   ├── Classification/             # ClassificationReader + IfcPropertyMapper
    │   ├── DesignOptions/              # 7 design options classes (registry, metadata, params, cost/carbon calc, cascade delete, context, dashboard data)
    │   ├── Drawing/                    # Drawing Template Manager — 40+ files
    │   ├── Electrical/                 # CableRouter + CableManifest + CircuitScheduleExporter + TrayFillCalculator
    │   ├── Fabrication/                # Assembly fabrication engine (10 files)
    │   ├── Lightning/                  # LpsEngine (IEC 62305-3 rolling sphere / mesh / LPZ)
    │   ├── Mcp/                        # McpToolDescriptorGenerator (AI/MCP integration)
    │   ├── MedGas/                     # MgasNetwork + MgasFlowSolver + MgasVerificationLog
    │   ├── Mep/                        # SleeveEngine + SleeveParamRegistry + SleeveSizingRules
    │   ├── Placement/                  # 25+ fixture placement engine files
    │   ├── Plumbing/                   # 15 plumbing engineering engines (see Plumbing Center section)
    │   ├── Radiation/                  # AdvancedRadShield + MriZoneEngine
    │   ├── Routing/                    # 20+ auto-routing files (A*, ACO, 3-opt, voxel grid, 3 penetration detectors, FrpPenetrationPlacer, PenetrationProductSelector, JunctionBoxAutoPlacer)
    │   ├── SLD/                        # Single Line Diagram engine (5 files · 872 lines)
    │   ├── Storage/                    # Extensible Storage schemas (16 files · 2,093 lines)
    │   ├── Symbols/                    # Symbol Library engine (15+ files · 3,925 lines)
    │   ├── Twin/                       # IoTDeviceRegistry + TwinReadback (BACnet/OPC-UA stubs)
    │   ├── Validation/                 # 18+ validators (v4 core + penetration + plumbing + structural + healthcare suite)
    │   └── Visualization/              # AVF heatmap engine + metric adapters + preview sources
    │
    ├── Commands/                       # Command files (30 sub-directories)
    │   ├── Adjacency/                  # AdjacencyAuditCommand
    │   ├── Architecture/               # ArchitectureCommands
    │   ├── DesignOptions/              # 10 commands · 1,345 lines
    │   ├── Drawing/                    # 20+ drawing type / title block / match line commands
    │   ├── Electrical/                 # 54 files · 10,676 lines (cable, voltage drop, feeder, fault, arc flash, busbar, conduit routing, cable routing, circuit wizard, selective coord, tray fill, conduit fill, phase balance, lighting, photometrics, SLD, standards, external exports, panel, hanger, cable segregation)
    │   ├── Fabrication/                # 4 fabrication commands + FabricationExt/
    │   ├── Healthcare/                 # IssueRoomDataSheetCommand + 9 Specialist/ audit commands
    │   ├── Lightning/                  # LightningProtectionCommands + LpsRollingSphere3DCommand
    │   ├── MedGas/                     # MgasNetworkAuditCommand + MgasVerifyCommand
    │   ├── Mep/                        # 9 MEP commands (sleeve, clash, autosize, PFV IFC export, etc.)
    │   ├── MepDesign/                  # MepDesignCommands (duct/pipe sizing + coordination)
    │   ├── Panels/                     # 9 Panel Schedule commands (Phase 176)
    │   ├── Placement/                  # 10 placement commands + Placement Center
    │   ├── PlacementExt/               # Extended placement commands
    │   ├── Plumbing/                   # 37 plumbing commands · 7 files · 1,417 lines (Phase 179)
    │   ├── Radiation/                  # MriZoneAuditCommand + 3 RadCalc commands
    │   ├── Routing/                    # AutoDropCommand + CalcCommands + HardyCrossCommand + PenetrationsDetectAndPlaceCommand + PlaceHangersCommand + RoutingStubCommands + CableSegregationCommand
    │   ├── RoutingExt/                 # Extended routing commands
    │   ├── SLD/                        # SLDGeneratorCommands + SLDRiserDiagramCommand
    │   ├── Standards/                  # StandardsCommands
    │   ├── StandardsExt/               # StandardsBulkWrappers + StandardsComplianceCommand + StandardsExtCommands
    │   ├── Storage/                    # EsStorageDiagnosticCommand + MigrateToExtensibleStorageCommand
    │   ├── StructuralExt/              # StructuralExtCommands
    │   ├── Symbols/                    # 6 symbol library command files · 1,862 lines
    │   ├── TagStudio/                  # MigrateTagFamiliesCommand + StyleAuditCommand
    │   ├── Twin/                       # IoTRegistryCommand
    │   ├── Validation/                 # RunAllValidatorsCommand + PenetrationCoverageCommand
    │   └── Visualization/              # AvfHeatmapCommands
    │
    ├── Select/                         # Element selection + color commands (4 files)
    ├── Organise/                       # Tag management commands (47 commands)
    ├── Tags/                           # Tagging commands (28 files, 140+ commands + NLP processor)
    ├── Docs/                           # Documentation commands (20 files, 55+ commands)
    │   ├── Templates/                  # Template engine v1.1 — 14 files
    │   ├── Workflow/                   # Workflow engine + audit + distribution — 6 files
    │   ├── Search/                     # Lucene.NET document index — 2 files
    │   ├── _template_sources/          # 16 embedded .docx/.xlsx templates
    │   └── _workflow_sources/          # 5 embedded workflow JSONs
    ├── Temp/                           # Template commands (22 files, 120+ commands)
    ├── Model/                          # Auto-modeling engine (26 files, 130+ commands)
    ├── BIMManager/                     # ISO 19650 BIM management (14 files, 120+ commands)
    ├── BOQ/                            # Bill of Quantities system (11 files · 7,228 lines)
    ├── ExLink/                         # External data link (8 files · 5,723 lines)
    ├── Presets/                        # Preset combination engine (2 files)
    ├── Photometrics/                   # IES/LDT photometric parsers (4 files · 633 lines)
    ├── Clash/                          # Clash detection UI viewmodels
    ├── V6/                             # Next-gen prototype features (15 files · 2,424 lines)
    │
    ├── UI/                             # WPF UI (90+ files)
    │   ├── StingDockPanel.xaml(.cs)    # Main 9-tab dockable panel
    │   ├── StingCommandHandler.cs      # IExternalEventHandler — dispatches 1,100+ button tags
    │   ├── StingDockPanelProvider.cs   # IDockablePaneProvider — main panel
    │   ├── StingElectricalPanel.xaml(.cs) (1,304+924) · StingElectricalCommandHandler.cs (570) · StingElectricalPanelProvider.cs
    │   ├── Plumbing/                   # StingPlumbingPanel (297) · StingPlumbingCommandHandler (159) · StingPlumbingPanelProvider (31) · PlumbingSystemConfigDialog (218) · SlopeFixPreviewDialog (134)
    │   ├── PlacementCenter/            # Modeless Placement Center (4,703 lines total)
    │   ├── Clash/                      # ClashRowViewModel + ClashTab_xaml
    │   ├── PhotometricLibraryDialog.xaml(.cs) # Phase 180 photometric library dialog
    │   ├── CircuitWizardDialog.xaml(.cs) · SelectiveCoordDialog.xaml(.cs) # Electrical wizard dialogs
    │   ├── DrawingTypeEditorDialog.cs  # Two-tab Drawing Types + View Style Packs editor
    │   ├── RevitVgEditor.cs            # Full Revit VG dialog replica embedded inline
    │   ├── BIMCoordinationCenter.cs    # 13-tab BIM coordination center
    │   ├── DocumentManagementDialog.cs # ISO 19650 Document Management Center (8 tabs)
    │   └── (40+ other dialog/wizard files)
    │
    └── Data/                           # Runtime data files (348 files)
        ├── MR_PARAMETERS.txt           # Shared parameter file (2,555+ params, 33 groups)
        ├── MR_PARAMETERS.csv           # Parameter definitions (CSV mirror)
        ├── PARAMETER_REGISTRY.json     # Master parameter registry
        ├── STING_DRAWING_TYPES.json    # 40+ corporate drawing types
        ├── STING_VIEW_STYLE_PACKS.json # 11+ view style packs
        ├── STING_AEC_FILTERS.json      # 199 AEC/FM filter definitions
        ├── STING_PANEL_SCHEDULE_TEMPLATES.json
        ├── Fabrication/                # FAB_RULES + ISO_SYMBOLS_INDEX
        ├── Parameters/                 # 5 shared parameter fragment .txt files
        ├── Placement/                  # 18 STING_PLACEMENT_RULES.*.json + HEIGHT_STANDARDS + MANUFACTURER_CATALOGUE
        ├── Seeds/                      # 16 seed family JSON specs
        ├── Symbols/                    # 9 symbol catalogue JSONs
        ├── Plumbing/                   # 5 plumbing data files
        ├── Healthcare/                 # 9 specialist healthcare rule JSONs
        ├── MedGas/                     # HTM 02-01 terminal units + pipe sizing + fab rules
        ├── LPS/                        # Lightning protection class tables + flash density + risk factors
        ├── IFC/                        # STING_IFC_PSET_MAPPING.json
        ├── Routing/                    # MEP insulation + separation rules + service corridors + sleeve rules
        ├── SectorPacks/                # HEALTHCARE, EDUCATION, DATACENTRE, RESIDENTIAL packs
        ├── Labour/                     # STING_LABOUR_RATES.csv
        ├── GenerativeDesign/           # STING_FIXTURE_PLACEMENT.dyn (Dynamo)
        ├── Templates/                  # STING_CORPORATE_BRAND.json
        ├── TagFamilies/Seeds/          # 100+ seed .rfa tag family files
        ├── DISCIPLINE_NOTES/           # Per-discipline note templates (9 CSVs)
        ├── COBIE_*.csv (8 files)       # COBie V2.4 reference data
        ├── WORKFLOW_*.json (20+ files) # Workflow preset definitions
        └── (BLE/MEP materials, schedules, formulas, tag configs, guides)
```

---

## ISO 19650 Tag Format

Tags follow the 8-segment format: `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`

| Segment | Parameter | Example | Description |
|---------|-----------|---------|-------------|
| DISC | ASS_DISCIPLINE_COD_TXT | M, E, P, A, S, H, MG, RP | Discipline code |
| LOC | ASS_LOC_TXT | BLD1, EXT | Location/building code |
| ZONE | ASS_ZONE_TXT | Z01, Z02 | Zone code |
| LVL | ASS_LVL_COD_TXT | L01, GF, B1 | Level code |
| SYS | ASS_SYSTEM_TYPE_TXT | HVAC, DCW, SAN, HWS, LV | System type |
| FUNC | ASS_FUNC_TXT | SUP, HTG, DCW, SAN, PWR | Function code |
| PROD | ASS_PRODCT_COD_TXT | AHU, DB, DR | Product code |
| SEQ | ASS_SEQ_NUM_TXT | 0001, 0042 | 4-digit sequence number |

Example: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`

---

## WPF Dockable Panels (Primary UI — 3 + 1 Modeless)

| Panel | Provider | Tabs / Sections | Dispatcher |
|---|---|---|---|
| **Main STING Panel** | `StingDockPanelProvider` | 9 (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM/TAGS) | `StingCommandHandler` — 1,100+ button tags |
| **STING Electrical Panel** | `StingElectricalPanelProvider` | Multi-section (Circuits/Cables/Calculations/Coordination/Reports/SLD) | `StingElectricalCommandHandler` — 54 command files |
| **STING Plumbing Panel** | `StingPlumbingPanelProvider` | 8 (SYSTEM/SUPPLY/DRAINAGE/ROUTE/STORM/SPECIALTY/AUDIT/DOCS) | `StingPlumbingCommandHandler` — 37 commands |
| **Placement Center** | IDockablePaneProvider (modeless) | Single (rule browser + history + family picker) | `PlacementCenterBridge` |

---

## Tagging Workflow

All tagging commands delegate to `TagPipelineHelper.RunFullPipeline()` — a guaranteed 9-step sequence:

1. **Category filter** — skips `TagConfig.CategorySkipList`; applies `CategoryTokenOverrides`
2. **TypeTokenInherit** — copies token values from family type to instance
3. **PopulateAll** — derives all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
4. **NativeParamMapper.MapAll** — bridges 30+ Revit native params to STING shared params
5. **FormulaEngine** — evaluates 199 dependency-ordered formulas
6. **BuildAndWriteTag** — assembles ISO 19650 8-segment tag with collision detection
7. **WriteContainers** — writes tag to all 53 discipline-specific containers
8. **WriteTag7All** — builds TAG7 rich narrative (A-F sub-sections)
9. **GetGridRef** — auto-detects nearest grid intersection

---

## Workflow Presets (20+ JSON files)

| File | Steps | Purpose |
|---|---|---|
| `WORKFLOW_DailyQA_Enhanced.json` | 8 | Enhanced daily QA with conditional steps |
| `WORKFLOW_MorningHealthCheck.json` | 10 | Morning health check with adaptive steps |
| `WORKFLOW_WeeklyDataDrop.json` | 10 | ISO 19650 weekly data drop |
| `WORKFLOW_ElectricalQA.json` | — | Electrical quality assurance chain |
| `WORKFLOW_PenetrationSweep.json` | 5 | Build seeds → detect → place → coverage audit → schedule |
| `WORKFLOW_PlumbingDesign.json` | 12 | End-to-end plumbing design |
| `WORKFLOW_PlumbingAudit.json` | 6 | Read-only plumbing audit |
| `WORKFLOW_PlumbingRoughIn.json` | 8 | Plumbing rough-in + multi-service drops + firestop |
| `WORKFLOW_PanelScheduleProduction.json` | — | Panel schedule production chain |
| `WORKFLOW_OptionPackage.json` | 8 | Design option decision package |
| `WORKFLOW_OptionDecisionGate.json` | 5 | Pre-decision compliance gate |
| `WORKFLOW_HealthcareCommissioning.json` | — | Healthcare commissioning chain |
| `WORKFLOW_MgasVerification.json` | — | HTM 02-01 MGPS verification |
| `WORKFLOW_PressureRegimeAudit.json` | — | Healthcare pressure regime audit |
| `WORKFLOW_RdsIssue.json` | — | Room Data Sheet issue workflow |
| `WORKFLOW_HTM-04-01-Annual.json` | — | HTM 04-01 annual water hygiene |
| `WORKFLOW_AntiLigatureAudit.json` | — | Anti-ligature compliance audit |
| `WORKFLOW_NFPA110-GeneratorTest.json` | — | NFPA 110 generator test sequence |
| `WORKFLOW_HTM-01-06-EndoReprocess.json` | — | HTM 01-06 endoscope reprocessing |
| `WORKFLOW_TierConversionHandover.json` | — | Data centre tier conversion handover |

---

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs`
- Entry point registered in `StingTools.addin`
- Registers **three** WPF dockable panels: Main (`StingDockPanelProvider`), Electrical (`StingElectricalPanelProvider`), Plumbing (`StingPlumbingPanelProvider`)
- Registers `StingAutoTagger` IUpdater (real-time auto-tagging, disabled by default) + `StingStaleMarker` IUpdater
- Subscribes to `DocumentOpened` event for quality gate (runs `ComplianceScan`, updates status bar)
- Builds legacy ribbon tab "STING Tools" (retained for compatibility)
- Provides `FindDataFile(fileName)` + `ParseCsvLine(line)`

### `ParamRegistry` (static) — `Core/ParamRegistry.cs`
- **Single source of truth** for all parameter names, GUIDs, container definitions, and category bindings
- Loads from `PARAMETER_REGISTRY.json` at runtime (thread-safe lazy init); falls back to hardcoded defaults
- 2,555+ parameters across 33 groups (including new Group 33 `PEN_PENETRATION`)
- `GetGuid(paramName)` · `GetParamName(guid)` · `AllParamGuids` · `AllContainers` · `ContainersForCategory`

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs`
- `GetString` / `GetInt` / `SetString` / `SetInt` / `SetIfEmpty` — parameter read/write helpers
- `CommandExecutionContext` — encapsulates `UIApplication` / `UIDocument` / `Document`
- `SpatialAutoDetect` — auto-derives LOC from Room name/number + ZONE from Room Department
- `TokenAutoPopulator` — shared batch token population (9-step pipeline for every element)
- `TypeTokenInherit` — copies non-empty tokens from element TYPE to instance
- `NativeParamMapper` — maps 30+ Revit built-in params to STING shared params
- `TagPipelineHelper.RunFullPipeline()` — the canonical 9-step tagging pipeline

### `TagConfig` (static) — `Core/TagConfig.cs`
- Lookup tables: `DiscMap` (41 entries) · `SysMap` (17 entries) · `ProdMap` (41) · `FuncMap` (16) · `LocCodes` · `ZoneCodes`
- `BuildAndWriteTag()` — shared tagging logic with collision mode, stats tracking, cross-validation
- `GetMepSystemAwareSysCode()` — derives SYS from connected MEP system name before falling back to category
- `GetFamilyAwareProdCode()` — family-name-aware PROD code resolution (35+ specific codes)

### `WorkflowEngine` (static) — `Core/WorkflowEngine.cs`
- JSON-based workflow presets loaded from `data/WORKFLOW_*.json` files
- `ResolveCommand(tag)` maps command tags to `IExternalCommand` instances (extended to cover all 37 plumbing + electrical tags)
- Conditional step execution: `MinCompliancePct` / `MaxCompliancePct` / `RequiresStaleElements`
- Result persistence to `STING_WORKFLOW_LOG.json` alongside project file (capped at 100 records)

---

## Development Workflow

### Building

```bash
# Windows — set Revit API path and build
dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"
```

### Deployment

1. Build to produce `StingTools.dll`
2. Copy `StingTools.addin` to `C:\ProgramData\Autodesk\Revit\Addins\2025\` (machine) or `%APPDATA%\Autodesk\Revit\Addins\2025\` (user)
3. Copy `StingTools.dll` + `Newtonsoft.Json.dll` + `ClosedXML.dll` + `data/` folder alongside
4. Restart Revit

### Branching

- Default branch: `master`
- Feature branches: `feature/<description>` or `claude/<session-id>`

### Commits

- Clear, concise commit messages in imperative mood
- One logical change per commit
- No secrets, credentials, `.env` files, or API keys

---

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
- Add `[Regeneration(RegenerationOption.Manual)]` for commands that modify the model
- Use `TaskDialog` for user-facing messages (not `MessageBox`)
- Use `StingLog.Info/Warn/Error` for all logging — never use silent catch blocks
- Handle `OperationCanceledException` for user-cancelled operations
- Use `FilteredElementCollector` with appropriate filters for performance
- For new commands, use shared helpers: `TagConfig.BuildAndWriteTag()`, `ParameterHelpers.SetIfEmpty()`, `SpatialAutoDetect.DetectLoc()/DetectZone()`

### Multi-file Command Patterns

1. **One class per file** — complex commands
2. **Multiple classes per file** — related simple commands (e.g., `PlumbingRoutingCommands.cs`, `CategorySelectCommands.cs`)

Use shared `internal static` helper classes to reduce duplication.

### Data File Conventions

- CSV files: standard comma-separated format with quoted fields
- JSON files: well-formatted with consistent indentation
- Preserve existing structure and column order when modifying data files
- Use `StingToolsApp.FindDataFile(fileName)` to locate data files at runtime
- Use `StingToolsApp.ParseCsvLine(line)` to parse CSV lines with quoted fields

### Testing

- Revit plugins run inside Revit — test by loading the plugin and exercising each command
- Validate changes in Revit with a test project before committing
- Ensure commands handle missing/null elements gracefully
- **Note**: Most feature branches are committed without `dotnet build` verification (Linux sandbox, no .NET / Revit API) — note this caveat clearly in commit messages and CHANGELOG entries

### Git Safety

- Never force-push without explicit permission
- Never run destructive git commands without confirmation
- Always commit to the correct branch — verify before pushing

---

## Dependencies and Build Configuration

- **Revit API**: `RevitAPI.dll`, `RevitAPIUI.dll` (referenced via `$(RevitApiPath)` — not distributed, `Private=false`; auto-detects Revit 2025 → 2026 → 2027)
- **Newtonsoft.Json**: v13.0.3
- **ClosedXML**: v0.104.2 (XLSX/BOQ export)
- **ZXing.Net**: v0.16.9 (QR code generation)
- **MiniWord**: v0.9.0 (template engine v1.1 DOCX renderer)
- **Lucene.Net**: 4.8.0-beta00016 + `Lucene.Net.Analysis.Common` (document index v1.1)
- **Target framework**: `net8.0-windows` (Revit 2025+)
- **WPF**: Enabled (`UseWPF=true`)
- **Output**: Library (DLL), `AppendTargetFrameworkToOutputPath=false`, `CopyLocalLockFileAssemblies=true`
- **Assembly**: v1.0.0.0, GUID `A1B2C3D4-5678-9ABC-DEF0-123456789ABC`, Vendor: Planscape
- **Data files**: Copied to output `data/` directory at build time

---

## Planscape Server

### Overview

**Planscape Server** is a cloud backend in `Planscape.Server/` (ASP.NET Core 8 + EF Core + SignalR + Hangfire + PostgreSQL 16 + Redis 7 + MinIO). It transforms the single-machine Revit plugin into a multi-user, multi-tenant SaaS platform.

> **Gap Analysis**: See `Planscape.Server/docs/PLANSCAPE_GAPS.md` for the comprehensive gap analysis and prioritised implementation roadmap.

### Controllers (19+)

`AuthController` · `ProjectsController` · `ProjectMembersController` · `TagSyncController` · `ComplianceController` · `IssuesController` · `DocumentsController` · `NotificationsController` · `WorkflowsController` · `MeetingsController` · `SeqSyncController` · `TransmittalsController` · `WarningsController` · `SearchController` · `PlatformController` · `AdminController` · `MimController` · `HealthcareController` · `PenetrationsController`

### Entities (23+)

`Tenant` · `AppUser` · `Project` · `ProjectMember` · `TaggedElement` · `BimIssue` · `IssueAttachment` · `DocumentRecord` · `DocumentApproval` · `PlatformConnection` · `ComplianceSnapshot` · `SeqCounter` · `Meeting` · `Transmittal` · `WorkflowRun` · `LicenseKey` · `AuditLog` · `DevicePushToken` · `HealthcarePressureLog` · `HealthcareMgasVerification` · `HealthcareAntiLigatureAudit` · `HealthcareRdsSnapshot` · `PenetrationSignoff`

### Running Locally

```bash
cd Planscape.Server/docker && docker compose up -d
# API: http://localhost:5000 · Swagger: http://localhost:5000/swagger
# Demo login: admin@planscape.demo / admin123

cd Planscape && npm install && npx expo start
```

---

## Planscape Mobile App

React Native + Expo SDK 52, located in `Planscape/`.

**Key screens**: `app/(tabs)/` — Dashboard · Issues (GPS/photo) · Documents (CDE) · Scanner (QR) · Settings

**Healthcare** (`app/healthcare/`): 6 commissioning screens — overview / mgas-checklist / pressure-live / water-flush / anti-ligature-audit / rds-viewer (Phase H-21)

**Penetrations** (`app/penetrations/`): QR scan → GPS → photo → status chips (DRAFT / INSTALLED / INSPECTED / SIGNED-OFF / REWORK) → offline-queue sign-off (Phase 178f)

**Other screens**: `app/inbox/` · `app/diary/` · `app/conflicts/` · `app/heatmap/` · `app/stages/` · `app/project-settings/` · `app/meetings/` · `app/transmittals/` · `app/warnings/` · `app/workflows/` · `app/models/`

---

*For historical implementation notes, see [`docs/CHANGELOG.md`](docs/CHANGELOG.md). For open gaps and future work, see [`docs/ROADMAP.md`](docs/ROADMAP.md).*
