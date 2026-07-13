# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, BIM template automation, MEP engineering, photometrics, plumbing design, and full-lifecycle AEC/FM tooling for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **1,204+ source files** (1,204 C# + 14 XAML, ~572,000 lines of code) across 38+ command directories
- **1,580+ `IExternalCommand` classes** (commands) + 3 `IPanelCommand` classes + 1 `IExternalApplication` entry point + 1 `IExternalEventHandler` + 4 `IDockablePaneProvider`s + 4+ `IUpdater`s
- **100+ runtime / embedded data files** (CSV, JSON, TXT, XLSX, PY, MD, DOCX) — includes template engine v1.1 pack (16 templates + 5 workflow definitions), HVAC/climate/RTS/acoustic data, CSI/MasterFormat maps, CTF coefficients, IDU catalogues, Cx task library, and more
- **4 WPF dockable panels** (Main 9-tab, Electrical, Plumbing, HVAC) + 1 modeless Placement Center + BIM Coordination Center (13 tabs) + Document Management Center (8 tabs) + ribbon retained for legacy compat
- **Top-level workspace** ships 30+ directories: `StingTools/` · `Planscape/` · `Planscape.Server/` · `StingBIM.Server/` · `StingBridge/` · `GUIDES/` · `StingTools.ArchiCAD/` · `Planscape.Desktop/` · `Planscape.Edge/` · `StingTools.Clash.Tests/` · `StingTools.Tags.Tests/` · `StingTools.Routing.Tests/` · `StingTools.Boq.Tests/` · `StingTools.Connectivity.Tests/` · `StingTools.Dynamo/` · `StingTools.Headless/` · `StingTools.Standards/` · `Tests/` · `Families/` · `docs/` · `docs-site/` · `marketing-site/` · `marketing-site-cron/` · `tools/` · `shared/` · `stingtools-bonsai/` · `stingtools-core/` · `ifc_drop/` · `project-templates/` · `planscape-site/`

### Phase history

The codebase is currently at **Phase 194**. Per-phase history (Phase 179 onward) lives in [docs/CHANGELOG.md](docs/CHANGELOG.md). Notable reference facts: the family placeholders are **2D symbolic curves, not 3D** (when a real 3D family is missing, `FixturePlacementEngine.ResolveSymbol` returns null and placement is silently skipped — no synthetic geometry); the Family Conformance Checker (`StingTools/Tags/FamilyConformanceCheckCommand.cs` + `FamilyConformanceInspector`, command tag `FamilyConformanceCheck`) audits a vendor `.rfa` folder against the STING contract on a 100-point scale (PASS ≥ 85 / WARN 70-84 / BLOCK < 70) before bulk-stamping.

## Documentation Map

| File | Purpose | When to edit |
|---|---|---|
| `CLAUDE.md` (this file, **1 file · ~4,875 lines**) | **Stable reference** — architecture, directory layout, command catalogue, UI structure, build/deploy, conventions | When the codebase's structure or commands change |
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

- `STING_PLUMBING_DRAINAGE_TABLES.json`
- `STING_PLUMBING_SUPPLY_TABLES.json`
- `STING_PIPE_MATERIALS_HYDRAULIC.json`
- `STING_PLUMBING_MATERIAL_RULES.json`
- `STING_BS5422_INSULATION.csv`

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

- `WORKFLOW_OptionPackage.json`
- `WORKFLOW_OptionDecisionGate.json`

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

**Status**: `StingTools/BOQ/` (35 files · ~13,846 lines across root + 4 subdirectories + 3 UI files).

### Root files (`BOQ/` — 19 files · 8,776 lines)

| Class | Lines | Purpose |
|---|---|---|
| `BOQCostManager.cs` | 2,026 | Core cost management engine: rate lookup, cost aggregation, VAT, contingency, carbon, waste |
| `BOQProfessionalExportCommand.cs` | 1,871 | Professional-grade export with company branding + NRM2 compliance + Word merge |
| `BOQParagraphEnhancer.cs` | 850 | Natural-language paragraph generation (NBS / NRM2 style) |
| `BOQBccBridge.cs` | 637 | Bridge for BIM Coordination Center BOQ data refresh |
| `BOQExportCommand.cs` | 597 | Primary export: ClosedXML XLSX with NRM2 grouping + labour + carbon columns |
| `BOQTemplateLibraryExtensions.cs` | 586 | Client vocabulary substitution from `BOQ_CLIENT_VOCABULARY.json` |
| `BOQSupportCommands.cs` | 506 | `BOQ_RateAudit`, `BOQ_DeltaReport`, `BOQ_Validate` |
| `BOQModels.cs` | 354 | POCOs: BOQItem, BOQSection, BOQPackage, TenderConfig, LabourRate |
| `CostStamp.cs` | 232 | Writes `CST_*` + `ASS_BOQ_*` shared parameters onto modelled elements |
| `IfcQuantitySetWriter.cs` | 171 | Writes IFC `Qto_*` quantity sets from BOQ line items |
| `BOQByMaterialView.cs` | 170 | Pivots BOQ output by material classification |
| `BOQTenderConfig.cs` | 148 | Tender configuration POCO + project-scoped overrides |
| `CarbonFactorResolver.cs` | 132 | Resolves embodied carbon factors (ICE DB + project override) per material/category |
| `BOQRateSourceHeatMapCommand.cs` | 121 | Heatmap view showing rate-book vs. custom override coverage |
| `CarbonPivotByPhaseLevel.cs` | 91 | Pivots carbon output by RIBA stage and level |
| `BOQPrepForExportCommand.cs` | 93 | Pre-export data normalization |
| `WasteFactor.cs` | 75 | Per-material waste percentage lookup (NRM2 / project override) |
| `BiogenicCarbon.cs` | 61 | Biogenic carbon sequestration credit calculation |
| `MeasuredAddition.cs` | 55 | Manual measured-addition rows not backed by model elements |

### Subdirectory: `MeasurementStandard/` (3 files · 550 lines)

| File | Lines | Purpose |
|---|---|---|
| `MeasurementStandards.cs` | 303 | NRM2 / SMM7 / CESMM rule set implementations; resolves unit, description, grouping per item |
| `Icms3PhaseMap.cs` | 192 | Maps STING BOQ sections to ICMS 3rd edition cost breakdown structure |
| `IMeasurementStandard.cs` | 55 | Interface contract: `MeasureItem`, `GroupItems`, `ResolveParagraph` |

### Subdirectory: `Rates/` (7 files · 785 lines)

| File | Lines | Purpose |
|---|---|---|
| `RateProviders.cs` | 291 | Composite rate-provider pipeline: BCIS → project rate card → material library → manual override |
| `RateProviderRegistry.cs` | 209 | Per-document loader; layers project overrides over corporate baseline |
| `IRateProvider.cs` | 121 | Interface: `GetRate(boqItem)` → `RateResult` (unit rate, source label, confidence) |
| `MaterialLibraryRateProvider.cs` | 106 | Looks up rates from `MATERIAL_LOOKUP.csv` by material + category |
| `Providers/BcisHttpRateProvider.cs` | 161 | REST client for live BCIS (Building Cost Information Service) API rates |
| `Providers/ProjectRateCardProvider.cs` | 97 | Project-scoped rate card loaded from `_BIM_COORD/boq_rate_card.json` |

### Subdirectory: `Sync/` (2 files · 363 lines)

| File | Lines | Purpose |
|---|---|---|
| `BoqSyncCoordinator.cs` | 237 | Coordinates BOQ snapshot sync: change detection, conflict resolution, push/pull with Planscape Server |
| `BoqSnapshotHasher.cs` | 126 | SHA-256 content hash per BOQ line item for efficient change detection |

### Subdirectory: `Takeoff/` (1 file · 256 lines)

| File | Lines | Purpose |
|---|---|---|
| `TakeoffRule.cs` | 256 | Data-driven takeoff rules: category → quantity parameter → unit → NRM2 ref mapping |

### UI files (`UI/` — 3 files · 2,916 lines)

| File | Lines | Purpose |
|---|---|---|
| `UI/BOQCostManagerPanel.cs` | 2,186 | WPF dockable cost manager panel — rate grid, carbon chart, budget vs. actual, filter bar |
| `UI/BOQTenderDialog.cs` | 671 | WPF tender configuration dialog — VAT, contingency, prelims, profit, phasing |
| `UI/BOQCostManagerWindow.cs` | 59 | Lightweight window host for the cost manager panel |

### BOQ Data files (under `Data/`)

| File | Lines | Purpose |
|---|---|---|
| `BOQ_TEMPLATE.csv` | 268 | Bill of Quantities template structure (NRM2 section headings + default units) |
| `BOQ_DESCRIPTIONS.json` | 218 | NRM2 natural-language description library keyed by section code |
| `BOQ_CLIENT_VOCABULARY.json` | 42 | Client-specific term substitution map used by `BOQTemplateLibraryExtensions` |
| `WORKFLOW_BOQ_TenderPack.json` | 49 | Workflow preset: full tender-pack production chain |
| `WORKFLOW_BOQ_FullRefresh.json` | 44 | Workflow preset: rebuild BOQ from live model + re-export |
| `WORKFLOW_BOQ_QuickValuation.json` | 34 | Workflow preset: fast cost-check without full NRM2 paragraph resolution |

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

The **Family Converter** tab (after "Library") changes a family's host / placement type — P1 lossless checkbox toggle (Unhosted→Face-based) + P2 template rebuild — via `Core/Placement/FamilyHostConverter.cs` + `Data/Placement/STING_FAMILY_HOST_TEMPLATES.json`. See CHANGELOG "Family Converter".

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

- `StingTools/Data/STING_PANEL_SCHEDULE_TEMPLATES.json`
- `StingTools/Data/WORKFLOW_PanelScheduleProduction.json`

### API limits honoured

- `PanelScheduleSheetInstance.Create` is broken in Revit 2024-2026
- `PanelScheduleTemplate` cell layout / column order / formulas remain read-only
- Real-circuit detection uses `PanelScheduleView.GetCircuitByCell(r, c)`

---

## Electrical Engineering Module (Waves A–J)

A full electrical design and analysis suite built incrementally across Waves A–J.
Commands live under `StingTools/Commands/Electrical/` (with sub-directories per
discipline) and engines under `StingTools/Core/Electrical/`.

**Status**: Waves A–J landed across multiple branches. Build errors from Revit API
obsoletion and C# ambiguities were fixed (`Color`, `Grid` qualification; `CS0117`
enum removal; `CS4014` async warnings). Verify in Revit before merging to `main`.

### New folders

| Path | Purpose |
|---|---|
| `StingTools/Commands/Electrical/` | Top-level commands (cable, circuits, panels, standards, conduit) |
| `StingTools/Commands/Electrical/ArcFlash/` | Arc-flash hazard analysis + label sheets + schedules |
| `StingTools/Commands/Electrical/Busbar/` | Busbar modeling + sizer engine |
| `StingTools/Commands/Electrical/CableSizer/` | BS 7671 cable sizer engine |
| `StingTools/Commands/Electrical/CircuitWizard/` | Guided circuit creation wizard |
| `StingTools/Commands/Electrical/Coordination/` | Selective coordination checker |
| `StingTools/Commands/Electrical/Export/` | ETAP, EasyPower, DIALux export |
| `StingTools/Commands/Electrical/FaultCurrent/` | IEC 60909 fault-current calculator + AIC rating |
| `StingTools/Commands/Electrical/FeederSizing/` | Feeder sizing engine |
| `StingTools/Commands/Electrical/IfcResults/` | IFC simulation results import + multi-engine aggregator |
| `StingTools/Commands/Electrical/Lighting/` | Lighting power density + emergency lighting audit |
| `StingTools/Commands/Electrical/Photometric/` | IES/LDT file import, DIALux round-trip, design review, preflight |
| `StingTools/Commands/Electrical/Reports/` | Fault-current schedule, demand factor report, voltage-drop schedule |
| `StingTools/Commands/Electrical/Routing/` | Conduit auto-route, conduit consolidator, cable schedule builder |
| `StingTools/Commands/Electrical/VoltageDrop/` | Voltage-drop calculation + flagging |
| `StingTools/Core/Electrical/` | `CableManifest`, `CableRouter`, `CircuitScheduleExporter`, `TrayFillCalculator` |
| `StingTools/Core/SLD/` | SLD generator: `SLDGenerator`, `SLDCircuitTraverser`, `SLDLayoutEngine`, `SLDAnnotationPlacer`, `SLDSyncUpdater` |
| `StingTools/Core/Lightning/` | Lightning protection engine (`LpsEngine`) |
| `StingTools/Commands/SLD/` | SLD commands (8 — see Phase 175 above) |
| `StingTools/Commands/Lightning/` | 18 LPS commands |
| `StingTools/Photometrics/` | `IesParser`, `LdtParser`, `PhotometricFile`, `PhotometricLibrary` |
| `StingTools/IfcResults/` | `IfcSimpleParser`, `StingLightingPSet` |

### Electrical commands (55)

| Command | Class | Description |
|---|---|---|
| `AddCableCommand` | `AddCableCommand` | Add cable to manifest |
| `ListCablesCommand` | `ListCablesCommand` | List cables from manifest |
| `AutoUpsizeWiresCommand` | `AutoUpsizeWiresCommand` | Auto-upsize wires for voltage drop |
| `BreakerSizerCommand` | `BreakerSizerCommand` | Size breakers per NEC/BS 7671 |
| `BreakerSizerApplyCommand` | `BreakerSizerApplyCommand` | Apply breaker sizing to model |
| `BatchAssignCircuitsCommand` | `BatchAssignCircuitsCommand` | Batch-assign elements to circuits |
| `ConduitFillValidateCommand` | `ConduitFillValidateCommand` | Validate conduit fill % |
| `ExportCircuitsCommand` | `ExportCircuitsCommand` | Export circuit data to CSV/XLSX |
| `WireAnnotateCommand` | `WireAnnotateCommand` | Annotate wire sizes on single element |
| `WireAnnotateBatchCommand` | `WireAnnotateBatchCommand` | Batch wire size annotation |
| `HomeRunArrowCommand` | `HomeRunArrowCommand` | Place home-run arrows |
| `ClearWireAnnotationsCommand` | `ClearWireAnnotationsCommand` | Clear wire annotations |
| `PhaseBalanceCommand` | `PhaseBalanceCommand` | Phase balance report |
| `CircuitDescriptionCommand` | `CircuitDescriptionCommand` | Generate circuit descriptions |
| `ElectricalStandardsValidatorCommand` | `ElectricalStandardsValidatorCommand` | BS 7671 / NEC standards validation |
| `ShowTrayFillCommand` | `ShowTrayFillCommand` | Show cable tray fill % overlay |
| `ElecPanelParamSyncCommand` | `ElecPanelParamSyncCommand` | Sync panel parameters |
| `ElecPanelWriteParamsCommand` | `ElecPanelWriteParamsCommand` | Write calculated params to panels |
| `ElecCircuitRenumberCommand` | `ElecCircuitRenumberCommand` | Renumber circuits |
| `ElecLoadSummaryCommand` | `ElecLoadSummaryCommand` | Load summary report |
| `ElecLightingScheduleCommand` | `ElecLightingScheduleCommand` | Generate lighting schedule |
| `ArcFlashCommand` | `ArcFlashCommand` | IEEE 1584 arc-flash analysis |
| `ArcFlashLabelSheetCommand` | `ArcFlashLabelSheetCommand` | Create arc-flash label sheet |
| `ArcFlashScheduleCommand` | `ArcFlashScheduleCommand` | Arc-flash schedule |
| `BusbarModelingCommand` | `BusbarModelingCommand` | Busbar modeling |
| `CableSizerCommand` | `CableSizerCommand` | BS 7671 cable sizer |
| `CircuitWizardCommand` | `CircuitWizardCommand` | Guided circuit creation |
| `SelectiveCoordCommand` | `SelectiveCoordCommand` | Selective coordination check |
| `EtapExportCommand` | `EtapExportCommand` | Export to ETAP |
| `EasyPowerExportCommand` | `EasyPowerExportCommand` | Export to EasyPower |
| `DIALuxExportCommand` | `DIALuxExportCommand` | Export to DIALux |
| `FaultCurrentCommand` | `FaultCurrentCommand` | IEC 60909 fault-current calculation |
| `AicRatingCommand` | `AicRatingCommand` | AIC rating check |
| `FeederSizerCommand` | `FeederSizerCommand` | Feeder sizing |
| `IfcResultsImportCommand` | `IfcResultsImportCommand` | Import IFC simulation results |
| `MultiEngineAggregatorCommand` | `MultiEngineAggregatorCommand` | Multi-engine results aggregator |
| `LightingPowerDensityCommand` | `LightingPowerDensityCommand` | LPD calculation + code comparison |
| `LpdColorCommand` | `LpdColorCommand` | Color-code elements by LPD |
| `EmergencyLightingAuditCommand` | `EmergencyLightingAuditCommand` | Emergency lighting coverage audit |
| `EmergencyLightingMarkCommand` | `EmergencyLightingMarkCommand` | Mark emergency luminaires |
| `AssignPhotometricCommand` | `AssignPhotometricCommand` | Assign IES/LDT file to luminaire |
| `PhotometricLibraryCommand` | `PhotometricLibraryCommand` | Browse photometric library |
| `PhotometricDesignReviewCommand` | `PhotometricDesignReviewCommand` | Photometric design review |
| `PhotometricLinkCommand` | `PhotometricLinkCommand` | Link photometric file to family |
| `DialuxRoundTripCommand` | `DialuxRoundTripCommand` | DIALux round-trip import |
| `PhotometricPreflightCommand` | `PhotometricPreflightCommand` | Photometric preflight check |
| `FaultCurrentScheduleCommand` | `FaultCurrentScheduleCommand` | Fault-current schedule |
| `DemandFactorReportCommand` | `DemandFactorReportCommand` | Demand factor report |
| `VoltageDropScheduleCommand` | `VoltageDropScheduleCommand` | Voltage-drop schedule |
| `ConduitAutoRouteCommand` | `ConduitAutoRouteCommand` | Auto-route conduits |
| `CableScheduleBuilderCommand` | `CableScheduleBuilderCommand` | Build cable BOM + conduit BOM + box BOM |
| `ConduitConsolidatorCommand` | `ConduitConsolidatorCommand` | Consolidate parallel conduits |
| `VoltageDropCommand` | `VoltageDropCommand` | Voltage-drop analysis |
| `VoltageDropFlagCommand` | `VoltageDropFlagCommand` | Flag over-limit voltage drops |
| `PanelViewScheduleCommand` | `PanelViewScheduleCommand` | Panel view schedule |

### Lightning Protection (LPS) commands (18)

| Class | Description |
|---|---|
| `LpsClassSetupCommand` | Set up LPL class + risk assessment |
| `LpsComplianceCheckCommand` | BS EN 62305 compliance check |
| `LpsDownConductorCheckerCommand` | Down-conductor spacing + cross-section audit |
| `LpsEarthResistanceValidatorCommand` | Earth resistance measurement validation |
| `LpsBondingInventoryCommand` | Equipotential bonding inventory |
| `LpsRoomZoneTagCommand` | Tag rooms with LPZ classification |
| `LpsPlanViewVisualizerCommand` | Strike collection overlay on plan view |
| `LpsSeparationDistanceCheckerCommand` | Separation distance (s) calculation |
| `LpsInspectionSchedulerCommand` | Inspection interval schedule |
| `LpsFullReportCommand` | Full LPS compliance report |
| `LpsDashboardCommand` | LPS dashboard |
| `LpsMarkElementTypesCommand` | Mark element types (air terminal, conductor, earth) |
| `LpsRecalcKcFactorCommand` | Recalculate Kc factor |
| `LpsColourZonesCommand` | Colour-code LPZ zones |
| `LpsClearZoneColoursCommand` | Clear LPZ colour overrides |
| `LpsCreateRevitScheduleCommand` | Create Revit schedule from LPS data |
| `LpsSyncToServerCommand` | Push LPS data to Planscape Server |
| `LpsRollingSphere3DCommand` | 3D rolling sphere visualisation |

### Caveats (Electrical)

1. Built without `dotnet build` verification (Linux sandbox). Revit API obsoletion
   warnings (`IntegerValue` → `Value`, `ParameterType` → `ForgeTypeId`) addressed.
2. `DIALuxExportCommand`, `EtapExportCommand`, `EasyPowerExportCommand` produce
   intermediary files; actual import into the target application is manual.
3. `PhotometricPreflightCommand` and `DialuxRoundTripCommand` require luminaire
   families to carry `IES_FILE_PATH_TXT` shared parameter.

## Clash Detection Engine

**Status**: Full in-process clash kernel in `StingTools/Clash/`. Built without
`dotnet build` verification. Verify in Revit before merge.

### New folder

`StingTools/Clash/` — 30+ files including `ClashKernel`, `AabbSweep`, `MollerSat`
(Möller–Trumbore SAT triangle intersection), `ObbTree` (oriented bounding box),
`ClashGrouper`, `ClashRuleEngine`, `ClashHistory`, `ClashPersistence`,
`LiveClashHandler` + `LiveClashUpdater` (IUpdater-based live detection),
`ClashScheduler` (Hangfire-based periodic re-scan), `ClashSlaIntegration`,
`AccIssuesClient` (push to ACC Issues API).

**Commands (6)**:

| Class | Description |
|---|---|
| `ClashRunCommand` | Run full clash detection and store session |
| `ClashBcfExportCommand` | Export clash session to BCF 2.1 |
| `ClashXlsxExportCommand` | Export clash report to XLSX |
| `ClashSessionRefreshCommand` | Refresh existing clash session |
| `ClashSessionClearCommand` | Clear clash session |
| `ClashMatrixEditCommand` | Edit clash rule matrix |

**Live detection**: `LiveClashHandler` + `LiveClashWireup` register an IUpdater
that re-checks elements on geometry change and raises `ClashNotifications` via
SignalR. `ClashScheduler` can run batch re-scans on a schedule.

**V6 extensions** (`StingTools/V6/`): `ClashTriageEngine`, `ClashResolutionSuggester`,
`FederationLinkedWalker`, `AsBuiltReconciler`, `ExcelBidirectionalSync`,
`FourdGanttReader`, `HealthDashboardEngine`, `QRCommissioningWorkflow`,
`LabourHoursEngine` — 5 additional commands: `HealthDashboardExportHtmlCommand`,
`QRAdvanceCommissioningCommand`, `QRCommissioningReportCommand`,
`ApplyLabourHoursCommand`, `ExportLabourHoursCommand`.

## ExLink — Extended Data Exchange

**Status**: Located in `StingTools/ExLink/`. Full bidirectional model-data exchange
plus browser/automation suite.

### Commands (34)

| Class | Description |
|---|---|
| `ExLinkBrowserCommand` | Browse linked data sources |
| `ExLinkExportCommand` | Export to linked target |
| `ExLinkImportCommand` | Import from linked source |
| `ExLinkMultiExportCommand` | Multi-target export |
| `ExLinkQuickViewCommand` | Quick view of linked data |
| `ExLinkBatchExportCommand` | Batch export all links |
| `ExLinkCustomLinkCommand` | Define custom link |
| `ExLinkQTOCommand` | Quantity take-off via ExLink |
| `ExLinkDocIssuanceCommand` | Document issuance pipeline |
| `ExLinkCOBieSyncCommand` | COBie sync via ExLink |
| `ExLinkDynamicPDFCommand` | Dynamic PDF export |
| `ExLinkDynamicDWGCommand` | Dynamic DWG export |
| `ExLinkDynamicNWCCommand` | Dynamic NWC export |
| `ISBDoorScheduleCommand` | ISB door schedule |
| `ISBWindowScheduleCommand` | ISB window schedule |
| `ISBRoomFinishCommand` | ISB room finish schedule |
| `ISBWallTypeCommand` | ISB wall-type schedule |
| `ISBFloorTypeCommand` | ISB floor-type schedule |
| `ISBEquipmentScheduleCommand` | ISB equipment schedule |
| `ISBLightingScheduleCommand` | ISB lighting schedule |
| `ISBPlumbingScheduleCommand` | ISB plumbing schedule |
| `ISBElectricalScheduleCommand` | ISB electrical schedule |
| `ISBKeyPlanCommand` | ISB key plan |
| `FamilyBrowserCommand` | Family browser |
| `TypeBrowserCommand` | Type browser |
| `UnusedElementsCommand` | Detect unused elements |
| `CADImportDetectorCommand` | Detect CAD imports |
| `InPlaceFamilyDetectorCommand` | Detect in-place families |
| `BatchPDFExportCommand` | Batch PDF export |
| `BatchDWGExportCommand` | Batch DWG export |
| `BatchNWCExportCommand` | Batch NWC export |
| `BatchIFCExportCommand` | Batch IFC export |
| `AutomationModelAuditCommand` | Automated model audit |
| `AutomationModelCompactCommand` | Model compaction |
| `AutomationBackupCleanupCommand` | Backup file cleanup |
| `AutomationFamilyUpgradeCommand` | Family upgrade batch |
| `AutomationModelStatsCommand` | Model statistics report |
| `AutomationBatchParamExportCommand` | Batch parameter export |
| `StickyNoteCreateCommand` | Create sticky note |
| `StickyNoteDashboardCommand` | Sticky note dashboard |
| `StickyNoteExportCommand` | Export sticky notes |
| `StickyNoteBulkUpdateCommand` | Bulk update sticky notes |

## AVF Heatmap Visualization

**Status**: Located in `StingTools/Commands/Visualization/`. Analysis Visualization
Framework (AVF) heatmaps using Revit's built-in display style engine.

**Commands (5)**:

| Class | Description |
|---|---|
| `VisualiseComplianceHeatmapCommand` | Tag completeness heatmap by element position |
| `VisualiseFillHeatmapCommand` | Conduit/duct fill % heatmap |
| `VisualiseCarbonHeatmapCommand` | Embodied carbon heatmap |
| `VisualiseAcousticHeatmapCommand` | Acoustic performance heatmap |
| `ClearHeatmapCommand` | Clear all AVF heatmaps |

## Extensible Storage Migration

**Status**: Located in `StingTools/Commands/Storage/`.

| Class | Description |
|---|---|
| `MigrateToExtensibleStorageCommand` | Migrate project data to Revit Extensible Storage schemas |
| `EsStorageDiagnosticCommand` | Diagnose Extensible Storage entities in the project |

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

### Core Drawing classes (40+ files under `Core/Drawing/`)

`DrawingType.cs` · `DrawingTypePresentation.cs` · `DrawingTypeRegistry.cs` · `DrawingDispatcher.cs` · `DrawingTypeValidator.cs` · `DrawingTypeStamper.cs` · `DrawingDriftDetector.cs` · `DrawingCropApplier.cs` · `ViewStylePack.cs` · `ViewStylePackRegistry.cs` · `ViewStylePackApplier.cs` · `ManagedTemplateSyncer.cs` · `AecFilterDefinition.cs` · `AecFilterRegistry.cs` · `AecFilterFactory.cs` · `ScopeBoxBinder.cs` · `TitleBlockParamApplier.cs` · `TokenProfileApplier.cs` · `AnnotationRunner.cs` · `DrawingProducer.cs` · `DrawingPackageManager.cs` · `TitleBlockFactory.cs` · `TitleBlockSpec.cs` · `MatchLineEngine.cs` · `DrawingTokenContext.cs` · `DrawingProductionPreset.cs` · `ProductionPresetRegistry.cs` · `DrawingThumbnailService.cs` · `SheetPlacementBridge.cs` · `SheetSequenceStore.cs` · `Iso19650Vocabulary.cs` · plus dimensioning sub-engine (`GridDimensioner`, `MEPDimensioner`, `DrainageInvertDimensioner`, `DimensionStrategy`).

### Commands (20+)

`DrawingTypes_Inspect`, `DrawingTypes_Reload`, `DrawingTypes_BrowserOrganize`, `DrawingTypes_SyncStyles`, `DrawingTypes_FromScopeBoxes`, `DrawingTypes_Produce`, `DrawingTypes_Doctor`, `DrawingTypes_HealTitleBlocks`, `DrawingTypes_Renumber`, `DrawingTypes_BatchProduce`, `AecFilters_Create`, `AecFilters_Inspect`, `AecFilters_Reload`, `ManagedTemplates_Convert`, `ManagedTemplates_Detach`, `ManagedTemplates_Regenerate`, `TitleBlocks_Factory`, `TitleBlocks_MigrateCsv`, `TitleBlocks_Migrate`, `MatchLines_Create`, `PresentationStyle_Setup`.

### AEC/FM Corporate Filter Library (Phase 166 — 199 filters)

`Data/STING_AEC_FILTERS.json`: 47 Arch · 33 HVAC · 31 Struct · 30 Fire · 27 Elec · 18 Plumb · 11 FM/COBie · 8 ISO 19650 · 8 Coord/LOD · 5 VT · 5 QA. `ViewStylePackApplier.ApplyFilterRules` now lazy-creates missing filters from the registry.

---

Save button routes to the active tab: `drawing_types.json` (tab 0, existing) or `view_style_packs.json` (tab 1, new). Only project-origin entries are written — corporate baseline on disk stays pristine. Edits to corporate packs silently flip `origin` to `project` via `ViewStylePackRegistry.ComputeChecksums` drift detection, same mechanism Drawing Types use.

The tab is a pure UI layer on top of the Week 2 data model — no changes to `ViewStylePack` / `ViewStylePackRegistry` / `ViewStylePackApplier`.

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

---

## Directory Structure

```
STINGTOOLS/
├── CLAUDE.md  # AI assistant guide
├── StingTools.addin  # Revit addin manifest
├── build.bat / extract_plugin.sh  # Build and deployment scripts
│
└── StingTools/  # C# project root
    ├── StingTools.csproj  # .NET 8 project file
    ├── Properties/AssemblyInfo.cs  # Assembly metadata
    │
    ├── Core/  # Shared infrastructure
    │   ├── StingToolsApp.cs  # IExternalApplication
    │   ├── StingLog.cs  # Thread-safe file logger
    │   ├── ParamRegistry.cs  # Single source of truth for
    │   ├── ParameterHelpers.cs  # Parameter read/write +
    │   ├── SharedParamGuids.cs  # Backwards-compatible facade
    │   ├── TagConfig.cs  # ISO 19650 tag lookup tables
    │   ├── TagSchemeEngine.cs  # Phase 188
    │   ├── StingAutoTagger.cs  # IUpdater
    │   ├── WorkflowEngine.cs  # Workflow orchestration
    │   ├── ComplianceScan.cs  # Cached compliance scan with
    │   ├── OutputLocationHelper.cs  # Centralized output directory
    │   ├── IPanelCommand.cs  # Interface for WPF dockable
    │   ├── PerformanceTracker.cs  # Lightweight performance
    │   ├── WarningsManager.cs  # Comprehensive warnings
    │   ├── ProjectFolderEngine.cs  # ISO 19650 project folder
    │   ├── UgandaRegionalDefaults.cs  # Phase 189
    │   ├── StingLlmService.cs  # Phase 193
    │   ├── PluginSchemaVersion.cs  # Phase 193
    │   ├── PluginTelemetry.cs  # Phase 193
    │   ├── PluginUpdateChecker.cs  # Phase 193
    │   ├── StingOfflineConfig.cs  # Phase 193
    │   ├── Phase74Enhancements.cs  # ModelCreationValidator
    │   ├── Phase75Enhancements.cs  # 29 workflow/coordination
    │   ├── WorkflowMaturityEngine.cs  # Step dependency resolver
    │   │
    │   ├── Acoustic/  # Phase 187
    │   ├── Adjacency/  # RoomGraphBuilder +
    │   ├── Behavioural/  # Phase 192 KUT
    │   ├── Branding/  # BrandTokens + CorporateBrand
    │   ├── Cad/  # Phase 190
    │   ├── Calc/  # 15+ engineering calc engines
    │   ├── Classification/  # ClassificationReader +
    │   ├── Climate/  # Phase 187
    │   ├── CostPlan/  # Phase 191
    │   ├── DesignOptions/  # 7 design options classes
    │   ├── Drawing/  # Drawing Template Manager
    │   ├── Electrical/  # CableRouter + CableManifest
    │   ├── Evm/  # Phase 191
    │   ├── Fabrication/  # Assembly fabrication engine
    │   ├── Hvac/  # Phase 187
    │   ├── Lightning/  # LpsEngine
    │   ├── Materials/  # Phase 190
    │   ├── Mcp/  # McpToolDescriptorGenerator
    │   ├── MedGas/  # MgasNetwork + MgasFlowSolver +
    │   ├── Mep/  # SleeveEngine +
    │   ├── PaymentCert/  # Phase 191
    │   ├── Placement/  # 25+ fixture placement engine
    │   ├── Plumbing/  # 15 plumbing engineering engines
    │   ├── Radiation/  # AdvancedRadShield +
    │   ├── Refrigerant/  # Phase 187
    │   ├── Routing/  # 20+ auto-routing files
    │   ├── SLD/  # Single Line Diagram engine
    │   ├── Storage/  # Extensible Storage schemas
    │   ├── Symbols/  # Symbol Library engine
    │   ├── TemplateManager/  # Phase 193
    │   ├── Twin/  # IoTDeviceRegistry +
    │   ├── Validation/  # 18+ validators
    │   ├── Variation/  # Phase 191
    │   └── Visualization/  # AVF heatmap engine + metric
    │
    ├── Select/  # Element selection + color
    │   ├── CategorySelectCommands.cs  # 14 category selectors +
    │   ├── StateSelectCommands.cs  # 5 state selectors + 2 spatial +
    │   ├── ColorCommands.cs  # 5 color-by-parameter commands +
    │   └── TagSelectorCommands.cs  # Multi-criteria tag selector
    │
    ├── UI/  # WPF dockable panel UI + wizards
    │   ├── StingDockPanel.xaml  # WPF markup for 9-tab dockable
    │   ├── StingDockPanel.xaml.cs  # Code-behind: button dispatch
    │   ├── StingCommandHandler.cs  # IExternalEventHandler
    │   ├── StingDockPanelProvider.cs  # IDockablePaneProvider
    │   ├── StingProgressDialog.cs  # Reusable modeless WPF progress
    │   ├── StingListPicker.cs  # Reusable WPF list picker dialog
    │   ├── StingModePicker.cs  # Reusable WPF mode picker dialog
    │   ├── StingWizardDialog.cs  # Base multi-page WPF wizard
    │   ├── StingDataGridDialog.cs  # Reusable WPF data grid dialog
    │   ├── StingExportDialog.cs  # ExLink-style export dialog with
    │   ├── StingResultPanel.cs  # Reusable rich WPF result
    │   ├── BatchRenameDialog.cs  # Single-step WPF batch rename
    │   ├── ParameterLookupDialog.cs  # Enhanced WPF parameter lookup
    │   ├── BulkOperationDialog.cs  # Unified WPF dialog for bulk
    │   ├── CombineConfigDialog.cs  # Unified WPF dialog for Combine
    │   ├── HeadingStyleDialog.cs  # Unified WPF dialog for TAG7
    │   ├── COBieExportWizard.cs  # Multi-page COBie V2.4 export
    │   ├── ExcelExchangeWizard.cs  # Excel import/export wizard with
    │   ├── IssueWizard.cs  # BIM issue creation wizard with
    │   ├── SmartPlacementWizard.cs  # Smart tag placement
    │   ├── BEPWizard.xaml  # WPF markup for BEP generation
    │   ├── BEPWizard.xaml.cs  # BEP wizard code-behind
    │   ├── DocumentManagementDialog.cs  # ISO 19650 Document Management
    │   ├── DocAutomationDialog.cs  # 4-tab unified doc automation
    │   ├── ModelCreationDialog.cs  # Unified model creation dialog
    │   ├── ScheduleWizardDialog.cs  # Unified schedule wizard dialog
    │   ├── NewSheetDialog.cs  # WPF sheet creation dialog
    │   ├── StingDataExchangeDialog.cs  # Data exchange configuration
    │   ├── BIMCoordinationCenter.cs  # 13-tab unified BIM coordination
    │   ├── SheetManagerDialog.cs  # WPF dual-panel sheet manager
    │   ├── ThemeManager.cs  # WPF theme engine
    │   ├── ProjectSetupWizard.xaml  # WPF 7-page project setup wizard
    │   └── ProjectSetupWizard.xaml.cs  # Code-behind: presets
    │
    ├── Docs/  # Documentation commands
    │   ├── SheetOrganizerCommand.cs  # Group sheets by discipline
    │   ├── ViewOrganizerCommand.cs  # Organize views by type/level
    │   ├── SheetIndexCommand.cs  # Create sheet index schedule
    │   ├── TransmittalCommand.cs  # ISO 19650 transmittal report
    │   ├── ViewportCommands.cs  # Align, Renumber, TextCase
    │   ├── DocAutomationCommands.cs  # DeleteUnusedViews
    │   ├── DocAutomationExtCommands.cs  # Batch
    │   ├── ViewAutomationCommands.cs  # DuplicateView, BatchRename
    │   ├── HandoverExportCommands.cs  # FM/O&M handover: COBie 2.4
    │   ├── JournalParserCommand.cs  # Revit journal diagnostics
    │   ├── DocScheduleAutomation.cs  # DrawingRegisterSync
    │   ├── FamilyAuditCommands.cs  # Family audit and validation
    │   ├── PrintManagerCommands.cs  # Print queue management and
    │   ├── SpatialValidationCommands.cs  # Room connectivity audit
    │   ├── SheetManagerEngine.cs  # Core sheet manager: drawable
    │   ├── SheetManagerEngineExt.cs  # Extended: MaxRects bin packing
    │   ├── SheetManagerCommands.cs  # 8 commands: SheetManager
    │   ├── SheetSetCommands.cs  # 8 commands: MaxRectsLayout
    │   ├── SheetTemplateEngine.cs  # Sheet templates, ISO 19650
    │   └── SheetTemplateCommands.cs  # 8 commands: CreateFromTemplate
    │
    ├── Tags/  # Tagging commands
    │   ├── AutoTagCommand.cs  # Tag elements in active view +
    │   ├── BatchTagCommand.cs  # Tag all elements in project
    │   ├── TagAndCombineCommand.cs  # One-click: populate + tag +
    │   ├── PreTagAuditCommand.cs  # Dry-run audit: predict tags
    │   ├── FamilyStagePopulateCommand.cs  # Pre-populate all 7 tokens
    │   ├── CombineParametersCommand.cs  # Interactive multi-container
    │   ├── ConfigEditorCommand.cs  # View/edit/save
    │   ├── TagConfigCommand.cs  # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs  # Bind shared parameters
    │   ├── TokenWriterCommands.cs  # SetDisc, SetLoc, SetZone
    │   │  # BuildTags
    │   ├── ValidateTagsCommand.cs  # Validate tag completeness with
    │   ├── SmartTagPlacementCommand.cs  # 16 smart annotation commands +
    │   ├── TagFamilyCreatorCommand.cs  # 4 tag family commands: Create
    │   ├── SyncParameterSchemaCommand.cs  # 3 schema commands: Sync
    │   ├── LegendBuilderCommands.cs  # 31 legend commands
    │   ├── RichTagDisplayCommands.cs  # 6 rich display commands
    │   ├── SystemParamPushCommand.cs  # 3 MEP system push commands
    │   ├── ResolveAllIssuesCommand.cs  # 1 one-click ISO 19650
    │   ├── PresentationModeCommand.cs  # 4 presentation commands
    │   ├── ParagraphDepthCommand.cs  # 2 commands: SetParagraphDepth
    │   ├── TagStyleCommands.cs  # 10 tag style commands
    │   ├── TagStyleEngine.cs  # Tag style engine: style
    │   ├── Tag3DCommand.cs  # 1 command: Tag3D
    │   ├── RepairDuplicateSeqCommand.cs  # 1 command: RepairDuplicateSeq
    │   ├── FamilyParamCreatorCommand.cs  # 1 command: FamilyParamCreator +
    │   ├── NLPCommandProcessor.cs  # Natural language intent
    │   ├── TagIntelligenceCommands.cs  # 8 advanced tagging intelligence
    │   └── TagStyleEngineCommands.cs  # Rule-based tag family type
    │
    ├── Organise/  # Tag management commands
    │   └── TagOperationCommands.cs  # Tag Ops
    │
    ├── BIMManager/  # ISO 19650 BIM management +
    │   ├── BIMManagerCommands.cs  # 37 commands: BEP
    │   ├── ExcelLinkCommands.cs  # 6 commands: ExportToExcel
    │   ├── PlatformLinkCommands.cs  # 6 commands: ACCPublish
    │   ├── RevisionManagementCommands.cs  # 12 commands: CreateRevision
    │   ├── SchedulingCommands.cs  # 12 commands: AutoSchedule4D
    │   ├── GapFixCommands.cs  # Cross-system gap fixes: CDE
    │   ├── GapAnalysisFixCommands.cs  # COBie extended import, HTML
    │   ├── CoordinationCenterCommands.cs  # BIM Coordination Center data
    │   ├── CarbonTrackingCommands.cs  # Embodied carbon tracking and
    │   ├── LANCollaborationCommands.cs  # LAN-based model collaboration
    │   ├── LinkManagerCommands.cs  # Revit link management and
    │   ├── ParameterDiffCommands.cs  # Parameter comparison and diff
    │   ├── QualityAssuranceCommands.cs  # QA automation: naming
    │   └── WorksetAuditCommands.cs  # Workset audit and management
    │
    ├── Docs/  # Documentation commands
    │   ├── Templates/  # Template engine v1.1
    │   ├── Workflow/  # Workflow engine + audit +
    │   ├── Search/  # Lucene.NET document index
    │   ├── _template_sources/  # 16 embedded .docx/.xlsx
    │   └── _workflow_sources/  # 5 embedded workflow JSONs
    ├── Temp/  # Template commands
    ├── Model/  # Auto-modeling engine
    ├── BOQ/  # Bill of Quantities system (35
    ├── ExLink/  # External data link
    ├── Presets/  # Preset combination engine
    ├── Photometrics/  # IES/LDT photometric parsers
    ├── Clash/  # Clash detection UI viewmodels
    ├── V6/  # Next-gen prototype features
    ├── Commands/Architecture/  # Phase 192
    ├── Commands/Classification/  # Phase 192
    ├── Commands/Cost/  # Phase 191
    ├── Commands/FabricationExt/  # Fabrication extensions
    ├── Commands/IFC/  # Phase 186
    ├── Commands/Interop/  # Phase 194 StingBridge
    ├── Commands/Kpi/  # Phase 193
    ├── Commands/Materials/  # Phase 190
    ├── Commands/MepDesign/  # Phase 187
    ├── Commands/PlacementExt/  # Phase 185
    ├── Commands/RoutingExt/  # Phase 192
    ├── Commands/Standards/  # Phase 192 KUT
    ├── Commands/StandardsExt/  # Phase 189
    ├── Commands/StructuralExt/  # Phase 190
    ├── Commands/TagStudio/  # Phase 188
    ├── Commands/TemplateManager/  # Phase 193
    │
    ├── UI/  # WPF UI
    │   ├── StingDockPanel.xaml(.cs)  # Main 9-tab dockable panel
    │   ├── StingCommandHandler.cs  # IExternalEventHandler
    │   ├── StingDockPanelProvider.cs  # IDockablePaneProvider
    │   ├── StingElectricalPanel.xaml(.cs) (1,304+924) · StingElectricalCommandHandler.cs (570) · StingElectricalPanelProvider.cs
    │   ├── Plumbing/  # StingPlumbingPanel
    │   ├── PlacementCenter/  # Modeless Placement Center
    │   ├── Clash/  # ClashRowViewModel +
    │   ├── PhotometricLibraryDialog.xaml(.cs)  # Phase 180 photometric library
    │   ├── CircuitWizardDialog.xaml(.cs) · SelectiveCoordDialog.xaml(.cs)  # Electrical wizard dialogs
    │   ├── DrawingTypeEditorDialog.cs  # Two-tab Drawing Types + View
    │   ├── RevitVgEditor.cs  # Full Revit VG dialog replica
    │   ├── BIMCoordinationCenter.cs  # 13-tab BIM coordination center
    │   ├── DocumentManagementDialog.cs  # ISO 19650 Document Management
    │   └── (40+ other dialog/wizard files)
    │
    ├── Docs/Templates/  # Template engine v1.1
    ├── Docs/Workflow/  # Template engine v1.1
    ├── Docs/Search/  # Template engine v1.1
    ├── Docs/_template_sources/  # Template engine v1.1
    ├── Docs/_workflow_sources/  # Template engine v1.1
    │
    └── Data/  # Runtime data files
        ├── BLE_MATERIALS.csv  # 815 building-element materials
        ├── MEP_MATERIALS.csv  # 464 MEP materials
        ├── MR_PARAMETERS.txt  # Shared parameter file (3,330
        ├── MR_PARAMETERS.csv  # Parameter definitions (CSV
        ├── MR_SCHEDULES.csv  # 168 schedule definitions
        ├── MATERIAL_SCHEMA.json  # 77-column material schema
        ├── MATERIAL_LOOKUP.csv  # 237-row material reference
        ├── FORMULAS_WITH_DEPENDENCIES.csv  # 199 parameter formulas
        ├── SCHEDULE_FIELD_REMAP.csv  # 50+ field deprecation remaps
        ├── BINDING_COVERAGE_MATRIX.csv  # Parameter-category coverage
        ├── BOQ_TEMPLATE.csv  # Bill of Quantities template
        ├── CATEGORY_BINDINGS.csv  # 10,661 category bindings
        ├── FAMILY_PARAMETER_BINDINGS.csv  # 4,686 family bindings
        ├── PARAMETER_CATEGORIES.csv  # Parameter-category
        ├── PARAMETER_REGISTRY.json  # Master parameter registry
        ├── LABEL_DEFINITIONS.json  # Label/legend definition specs
        ├── TAG_CONFIG_v5_0_CONTAINERS.csv  # 122+ tag container definitions
        ├── TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv  # 179+ discipline/system/function
        ├── TAG_CONFIG_v5_0_VALIDATION.csv  # 180+ validation rules for tag
        ├── STING_TAG_CONFIG_v5_0_ARCH.csv  # Architectural tag family
        ├── STING_TAG_CONFIG_v5_0_GEN.csv  # General tag family definitions
        ├── STING_TAG_CONFIG_v5_0_MEP.csv  # MEP tag family definitions
        ├── STING_TAG_CONFIG_v5_0_STR.csv  # Structural tag family
        ├── STRUCTURAL_EXCEL_TEMPLATE.csv  # Structural Excel import
        ├── PROJECT_TEAM_TEMPLATE.json  # Project team role/discipline
        ├── PYREVIT_SCRIPT_MANIFEST.csv  # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx  # Tag reference guide
        ├── TAG GUIDE V2.xlsx  # Tag reference guide
        ├── TAG_GUIDE_V3.csv  # Tag reference guide
        ├── TAG_PLACEMENT_PRESETS_DEFAULT.json  # Default tag placement preset
        ├── TAG_STYLE_RULES.json  # Tag style engine rules
        ├── WORKFLOW_DailyQA_Enhanced.json  # Enhanced Daily QA workflow
        ├── WORKFLOW_MorningHealthCheck.json  # Morning health check workflow
        ├── WORKFLOW_WeeklyDataDrop.json  # Weekly ISO 19650 data drop
        ├── cost_rates_5d.csv  # 5D cost rates
        ├── VALIDAT_BIM_TEMPLATE.py  # BIM template validation
        ├── COBIE_TYPE_MAP.csv  # 70+ equipment types with STING
        ├── COBIE_SYSTEM_MAP.csv  # 31 building system mappings
        ├── COBIE_PICKLISTS.csv  # COBie V2.4 controlled
        ├── COBIE_ATTRIBUTE_TEMPLATES.csv  # Expected attributes per COBie
        ├── COBIE_JOB_TEMPLATES.csv  # SFG20/BS 8210 maintenance job
        ├── COBIE_SPARE_PARTS.csv  # Spare parts per equipment type
        ├── COBIE_DOCUMENT_TYPES.csv  # 28 O&M document types with
        ├── COBIE_ZONE_TYPES.csv  # 16 zone type classifications
        ├── project_bep.json  # Project-specific BEP
        ├── TAGGING_GUIDE.md  # Complete tagging guide
        ├── TAG_FAMILY_CREATION_GUIDE.md  # End-to-end tag family creation
        ├── BIM_COORDINATION_WORKFLOW_GUIDE.md  # Comprehensive BIM coordinator
        ├── DWG_TO_BIM_GUIDE.md  # DWG-to-structural BIM
        ├── STING_ELECTRICAL_LAYMANS_GUIDE.md  # End-to-end layman's guide for
        ├── STING_TAG_SCHEMES.json  # Phase 188
        ├── STING_MEP_SIZING_RULES.json  # Phase 180/187
        ├── STING_CLIMATE_DATA.json  # Phase 187
        ├── STING_LOAD_PROFILES.json  # Phase 187b
        ├── STING_CTF_COEFFICIENTS.json  # Phase 187d
        ├── STING_REFRIG_VENDOR_LIMITS.json  # Phase 187c — 7 VRF vendor
        ├── STING_REFRIG_CHARGE_TABLES.json  # Phase 187g — per-OD refrigerant
        ├── STING_IDU_CATALOGUE.json  # Phase 187h
        ├── STING_REFNET_JOINTS.json  # Phase 187h
        ├── STING_CX_TASKS.json  # Phase 187e
        ├── STING_RTS_REFERENCE_CASES.json  # Phase 187e
        ├── STING_DRAWING_TYPES.json  # Phases 113-184
        ├── STING_VIEW_STYLE_PACKS.json  # Phases 113-182
        ├── STING_AEC_FILTERS.json  # Phase 166/184f
        ├── STING_FAB_RULES.json  # v4 MVP
        ├── STING_FAB_RULES_EXT.json  # v4 MVP extension
        ├── STING_PANEL_SCHEDULE_TEMPLATES.json  # Phase 176 — 5 panel schedule
        └── STING_SLD_SYMBOLS.json  # Phase 175
```

## Ribbon UI Architecture (Legacy)

> The legacy ribbon is superseded by the WPF dockable panels (see **WPF Dockable Panels**). All commands are accessible from both surfaces; per-command tables live in **Command Count by File** and the panel sections.

## Command Count by File

> Per-file command/line inventory removed for brevity — the canonical file map is **Directory Structure** below; class/command names are in **Core Classes** and the per-subsystem sections. Project-wide totals: **~539 IExternalCommand classes · ~134,400 lines** across the catalogued files (1,580+ command classes total including subsystem packs — see Quick Stats).

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (418 lines)
- Entry point registered in `StingTools.addin`
- Static properties: `AssemblyPath`, `DataPath`
- Registers WPF dockable panel (`StingDockPanelProvider`)
- Registers `StingAutoTagger` IUpdater for real-time auto-tagging
- Registers `StingStaleMarker` IUpdater for stale element detection
- Subscribes to `DocumentOpened` event for quality gate
- Builds legacy ribbon tab "STING Tools" with `BuildSelectPanel`
- Provides `FindDataFile(fileName)`
- Provides `ParseCsvLine(line)`
- Contains `ToggleDockPanelCommand`

### `StingLog` (static) — `Core/StingLog.cs` (127 lines)
- Thread-safe file logger (`StingTools.log` alongside the DLL)
- Uses buffered `StreamWriter` with `FileShare.Read` for performance
- Methods: `Info(msg)`, `Warn(msg)`, `Error(msg, ex?)`, `Shutdown()`
- `Shutdown()` flushes and closes the log file
- Error recovery: disposes bad writer on IO failure so next call
- Used throughout the codebase for error tracing; replaces silent catch blocks
- Contains `EscapeChecker` utility

### `StingAutoTagger` (IUpdater) — `Core/StingAutoTagger.cs` (736 lines)
- Real-time auto-tagging engine via Revit `IUpdater` interface
- Triggers on `Element.GetChangeTypeElementAddition()` for 22 tagged categories
- Auto-populates tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) and builds
- **Visual tag placement**: optionally creates `IndependentTag` annotations when visual
- **Discipline filter**: restricts auto-tagging to user-specified discipline codes
- **Workset ownership check**: skips elements on worksets not owned
- **Type token inheritance**: calls `TypeTokenInherit(doc, el)` before `PopulateAll`
- Registered in `StingToolsApp.OnStartup()`, starts disabled
- Performance: suppresses redundant triggers via `HashSet<long>`
- Contains `AutoTaggerToggleCommand`
- Contains `AutoTaggerToggleVisualCommand`
- Contains `AutoTaggerConfigCommand`

### `StingStaleMarker` (IUpdater) — `Core/StingAutoTagger.cs`
- Detects geometry changes on tagged elements and marks them as stale
- Triggers on `Element.GetChangeTypeGeometry()` for same 22 categories
- Only marks elements that already have a non-empty ASS_TAG_1 and have
- Performance: 20-element-per-trigger guard
- Registered/unregistered alongside StingAutoTagger in `StingToolsApp`

### `WorkflowEngine` (static) — `Core/WorkflowEngine.cs` (583 lines)
- Workflow orchestration engine for chaining named command sequences
- JSON-based workflow presets loaded from `data/WORKFLOW_*.json` files
- Built-in presets: `ProjectKickoff` (26 steps), `DailyQA`
- Per-step progress reporting with timing, Escape key cancellation between steps
- Atomic `TransactionGroup` with rollback on failure
- `ResolveCommand(tag)` maps ~63 command tags to `IExternalCommand`
- **Conditional step execution**: `MinCompliancePct`, `MaxCompliancePct` (compliance threshold gates), `RequiresStaleElements`
- **Result persistence**: `WorkflowRunRecord` saved to `STING_WORKFLOW_LOG.json` alongside project
- Contains 4 commands: `WorkflowPresetCommand`

### `ComplianceScan` (static) — `Core/ComplianceScan.cs` (222 lines)
- Lightweight cached compliance scan for live dashboard/status bar display
- Thread-safe cached results with 30-second stale duration
- `Scan(doc)`
- `ComplianceResult`
- **Per-discipline breakdown**: `ByDisc` dictionary of `DiscComplianceData`
- **Dual metrics**: `CompliancePercent` (tagged/total) vs `StrictPercent` (fully resolved/total)
- **Revision tracking**: `RevisionComplete`, `RevisionMissing`, `RevisionPercent`, `RevisionDistribution`
- `StatusBarText`
- `InvalidateCache()`

### `StingProgressDialog` — `UI/StingProgressDialog.cs` (239 lines)
- Reusable modeless WPF progress window for batch operations
- Shows progress bar, element count (N/M), estimated time remaining
- Thread-safe updates via `Dispatcher.Invoke`
- Win32 `GetAsyncKeyState` for Escape key detection even when Revit has focus
- Usage: `var p = StingProgressDialog.Show("Title", total)

### `ParamRegistry` (static) — `Core/ParamRegistry.cs` (1,751 lines)
- **Single source of truth** for all parameter names, GUIDs, container
- Loads from `PARAMETER_REGISTRY.json` at runtime
- **Tag format configuration**: `Separator`, `NumPad`, `SegmentOrder`
- **Typed string constants** for all 8 source tokens
- **Phase 11 constants**: `STALE`/`STALE_GUID`, `CLUSTER_COUNT`/`CLUSTER_LABEL`, `DISPLAY_MODE`/`DISPLAY_TXT`, `TAG_POS`, `VIEW_TAG_STYLE`
- **Extended parameter constants**: ~97+ parameters across identity, spatial, BLE
- **GUID lookups**: `GetGuid(paramName)`, `GetParamName(guid)`, `AllParamGuids`
- **Container management**: `AllContainers`, `ContainersForCategory(categoryName)`, `GetContainerTuples()`
- **Token presets**: Named index arrays for partial tag strings
- **Tag assembly**: `AssembleContainer()`, `ReadTokenValues()`, `WriteContainers()`
- **Reload**: `Reload()` forces re-read from disk

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs` (2,009 lines)
- `GetString(el, paramName)`
- `GetInt(el, paramName, defaultValue)`
- `SetString(el, paramName, value, overwrite)`
- `SetInt(el, paramName, value)`
- `SetIfEmpty(el, paramName, value)`
- `CommandExecutionContext`
- `GetLevelCode(doc, el)`
- `GetCategoryName(el)`
- `GetFamilyName(el)`
- `GetFamilySymbolName(el)`
- `GetRoomAtElement(doc, el)`
- `GetSolidFillPattern(doc)`

### `SpatialAutoDetect` (static) — `Core/ParameterHelpers.cs`
- Auto-derives LOC from Room name/number/Project Info and ZONE
- `BuildRoomIndex(doc)`
- `DetectProjectLoc(doc)`
- `DetectLoc(doc, el, roomIndex, projectLoc)`
- `DetectZone(doc, el, roomIndex)`

### `PhaseAutoDetect` (static) — `Core/ParameterHelpers.cs`
- Auto-derives STATUS and REV from Revit phase data, worksets, and project info
- `DetectStatus(doc, el)`
- `DetectProjectRevision(doc)`

### `TokenAutoPopulator` (static) — `Core/ParameterHelpers.cs`
- Shared utility for batch token population across all tagging commands
- `PopulationContext.Build(doc)`
- `PopulateAll(doc, el, ctx, overwrite)`
- `TypeTokenInherit(doc, el)`
- `CopyTokensFromNearest(doc, el, tokensToCopy, candidatePool)`
- Returns `PopulationResult` with granular counts

### `NativeParamMapper` (static) — `Core/ParameterHelpers.cs`
- Maps 30+ Revit built-in parameters to STING shared parameters
- `MapAll(doc, el)`
- `MapSheets(doc)`
- Bridges native Revit data (Width, Height, Flow, etc.) into STING

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs` (228 lines)
- Backwards-compatible facade wrapping `ParamRegistry`
- `ParamGuids`
- `UniversalParams`
- `AllCategoryEnums`
- `DisciplineBindings`
- `BuildCategorySet(doc, enums)`
- `ValidateBindingsFromCsv()`
- `InvalidateCache()`

### `TagCollisionMode` (enum) — `Core/TagConfig.cs`
- Controls how tag collisions are handled: `Skip`, `Overwrite`, `AutoIncrement`
- Used by all tagging commands

### `TaggingStats` (class) — `Core/TagConfig.cs`
- Tracks batch tagging operation statistics for rich post-operation reporting
- Per-category, per-discipline, per-system, per-level breakdown
- Collision detail tracking (tag, depth), skipped/overwritten counts, warnings
- `BuildReport()` generates multi-line formatted report for TaskDialog

### `ISO19650Validator` (static) — `Core/TagConfig.cs`
- **Code validation**: `ValidDiscCodes`, `ValidSysCodes`, `ValidFuncCodes`
- **Token validation**: `ValidateToken(tokenName, value)`
- **Element validation**: `ValidateElement(el)`
- **Tag format validation**: `ValidateTagFormat(tag)`
- Used by `ValidateTagsCommand`, `BuildTagsCommand`

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (4,030 lines)
- **Lookup tables** (all configurable via `project_config.json`)
  - `DiscMap`
  - `SysMap`
  - `ProdMap`
  - `FuncMap`
  - `LocCodes`
  - `ZoneCodes`
- **Configuration management**: `LoadFromFile(path)`, `LoadDefaults()`, `ConfigSource`
- **Tag operations** (7 intelligence layers)
  - `TagIsComplete(tagValue, expectedTokens=8)`
  - `BuildAndWriteTag(doc, el, seqCounters, skipComplete, existingTags, collisionMode, stats)`
  - `GetExistingSequenceCounters(doc)`
  - `BuildExistingTagIndex(doc)`
  - `GetSysCode(categoryName)`, `GetFuncCode(sysCode)`
  - `GetMepSystemAwareSysCode(el, categoryName)`
  - `GetFamilyAwareProdCode(el, categoryName)`
  - `GetViewRelevantDisciplines(view)`
  - `FilterByViewDisciplines(elements, disciplines)`
- **Constants**: `NumPad = 4`, `Separator = "-"`

### `TagIntelligence` (static) — `Core/TagConfig.cs`
- Advanced tagging intelligence layer for complex inference
- `InferenceResult` class for structured inference outputs

### Internal Helper Classes

~110 `internal static` helper / engine classes provide shared logic to the commands in their respective files (e.g. `DocAutomationHelper`, `SheetManagerEngine`, `SheetTemplateEngine`, `CategorySelector`, `TokenWriter`, `CompoundTypeCreator`, `MaterialPropertyHelper`, `LeaderHelper`, `AnnotationColorHelper`, `ScheduleHelper`, `FormulaEngine`, `TemplateManager`, `LegendBuilder`, `StingColorRegistry`, `SystemParamPush`, `TagStyleEngine`, `ModelEngine`, `CADToModelEngine`, `BIMManagerEngine`, `Scheduling4DEngine`, `ExcelLinkEngine`, `PlatformLinkEngine`, `RevisionEngine`, `OutputLocationHelper`, `ColorHelper`, `TagPlacementEngine`, `TagPipelineHelper`, `NLPCommandProcessor`, `StandardsEngine`, plus the reusable WPF dialog/wizard hosts under `UI/`). Each is named with its source file and one-line purpose in the **Core Classes** descriptions above and the **Command Count by File** table; file locations follow the **Directory Structure** tree.

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

- **Standard** combo
- **Region** combo
- **Pressure class** combo
- **Air density** combo
- **Sizing strategy** radio
- **Scope** radio

Header state is snapshotted into static fields on `StingHvacCommandHandler` (`CurrentRegion`, `CurrentStandard`, `CurrentPressureClassId`, `CurrentAirDensityKgM3`, `CurrentSizingStrategyId`, `CurrentScope`) before each `ExternalEvent.Raise()` so the command running on the Revit API thread reads consistent values.

### Sizing registry

`STING_MEP_SIZING_RULES.json` is the corporate baseline; `<project>/_BIM_COORD/mep_sizing_rules.json` is the project-level override. Both load through `MepSizingRegistry.Get(doc)` (cached per-document path). Fields covered:

- **Duct**: `roles[]`
- **Pipe**: `services[]`
- **Conduit / cable tray**: `maxFillPct` (45% / 50% per BS 7671 + industry rule)
- **Sizing strategy**: 4 options
- **Balancing**: `maxIterations`, `tolerancePa`, `dampingFactor`, `minBranchFlowLs`
- **Acoustics**: `ncTargets[]`

Edit either JSON in a text editor and click **RPRT → Reload rules** to pick up changes without restarting Revit.

### Caveats

1. Built without `dotnet build` verification (Linux sandbox). Verify in Revit before merge.
2. Phase 181 wired the sizing engines (`MepAutoSizeDuctCommand`, `MepAutoSizePipeCommand`, `MepAutoSizeConduitCommand`), the balancing engine (`MEPBalancingEngine.BalanceSystem` with a new `Document`-aware overload) and the fitting-loss dictionary (`FittingLossCalculator` with a JSON-overlay path) through `MepSizingRegistry`. Hardcoded constants remain as `*Fallback` safety nets only. Per-element segment-role / pipe-service detection (rather than the project-wide `branch` / `chw` defaults) is still pending — the data path is in place.
3. `Hvac_RunLoads` (`Commands/Hvac/HvacRunLoadsCommand`) posts `PostableCommand.AnalyzeHeatingAndCoolingLoads` after an MEP-Spaces pre-flight. `Hvac_ExportGbxml` (`Commands/Hvac/HvacExportGbxmlCommand`) calls `Document.Export` with `GBXMLExportOptions` after a 3D-view check. Both are real, no TaskDialog stubs.
4. The EQPT / SYS / SpoolGrid / DriftGrid / WorkflowGrid `ObservableCollection`s start empty — commands push rows back into the panel singleton (`StingHvacPanel.Instance`) on completion (same pattern `StingElectricalPanel` uses).
5. PaneGuid `D7E8F9A0-B1C2-3D4E-5F60-1A2B3C4D5E6F` is stable from this point so users' Revit `UIState.dat` re-locates the panel between sessions.

## HVAC design engines (Phase 187 — competitive parity)

Built in response to the "what can we borrow from MagiCAD / TRACE /
HAP / IES VE / cove.tool / Daikin VRV tools" review. Adds five
calculation kernels that move STING from "Revit organiser" toward
"design engine":

### New folders + files

| Path | Purpose | LoC |
|---|---|---|
| `StingTools/Core/Climate/ClimateRegistry.cs` | ASHRAE 2021 / CIBSE Guide A design-day site registry, NASA ISA elevation-corrected air density, per-doc cache, project override at `<project>/_BIM_COORD/climate_data.json` | ~200 |
| `StingTools/Data/STING_CLIMATE_DATA.json` | 41 cities (UK + EU + US + AU + Asia + Africa) with cooling 0.4 % DB + MCWB, heating 99.6 % DB, HDD18, CDD10, elevation | — |
| `StingTools/Core/Hvac/Loads/LoadInputs.cs` | `LoadZone` / `EnvelopeSegment` / `ZoneLoadResult` / `BlockLoadResult` POCOs + ASHRAE 90.1 default schedules | ~120 |
| `StingTools/Core/Hvac/Loads/BlockLoadEngine.cs` | Hour-by-hour 24-h design-day load calc with peak-pick at the SYSTEM level (not Σ zone peaks). Conduction + solar (ASHRAE Clear Sky) + occupants + lighting + equipment + vent + infiltration. Reports diversity = block / Σpeaks. | ~250 |
| `StingTools/Core/Acoustic/OctaveBand.cs` | 8-band Lw / Lp container + NC curve evaluator (NC-15 → NC-65) | ~140 |
| `StingTools/Core/Acoustic/NcPredictionEngine.cs` | VDI 2081 / ASHRAE A48 attenuation tables (straight, lined, elbow, tee, end-reflection) + Bullock regenerated-noise correlations + direct + reverberant room model | ~250 |
| `StingTools/Core/Refrigerant/RefrigerantProperties.cs` | R410A / R32 / R134a / CO₂ saturation densities + viscosities + Hfg + oil-return velocity floors + Daikin VRV envelope limits | ~120 |
| `StingTools/Core/Refrigerant/RefrigerantPipeSolver.cs` | Darcy-Weisbach + Blasius f smooth-copper pipe sweep over ACR size list with oil-return velocity floor + ΔP budget + liquid-leg static head | ~140 |
| `StingTools/UI/RefrigerantSizingDialog.cs` | Minimal WPF input dialog: refrigerant + leg + capacity + length + lift + ΔP budget + riser flag | ~150 |

### Manufacturer + valve pack

Extended `STING_MEP_SIZING_RULES.json` with two new sections:
`duct.manufacturerFittings` (Lindab / Trox / Halton catalogue C
values keyed by product code) and `pipe.valveCv` (Belimo / Siemens
/ Danfoss Kvs values, m³/h at 1 bar). `MepSizingRules.GetManufacturerC`
and `GetValveKvs` give callers a typed lookup; fittings fall back to
the generic SMACNA C table from `DuctFrictionSolver.SmacnaCoefficients`
when no manufacturer match.

### New commands (7)

| Tag | Class | Description |
|---|---|---|
| `Hvac_BlockLoad` | `HvacBlockLoadCommand` | Runs `BlockLoadEngine` against Revit Spaces (or Rooms), stamps per-space `HVC_PEAK_SENS_W` / `HVC_PEAK_LAT_W` / `HVC_PEAK_HOUR` / `HVC_OA_LS`, reports building block, Σ peaks, diversity, per-system table, top-10 zones |
| `Hvac_NcPredict` | `HvacNcPredictionCommand` | Walks user duct selection, treats the upstream-most member as fan source (synthetic Lw from Q + ΔP), runs `NcPredictionEngine`, reports predicted NC + per-element attenuation/regen breakdown |
| `Hvac_RefrigSize` | `HvacRefrigerantSizeCommand` | Pops `RefrigerantSizingDialog`, runs `RefrigerantPipeSolver`, reports chosen OD + velocity + ΔP + vendor compliance with full size-sweep trace |
| `Hvac_ClimateInspect` | `HvacClimateInspectCommand` | Shows active climate site (resolved via `PRJ_CLIMATE_SITE_ID` or address fuzzy-match) + corporate catalogue |
| `Hvac_ClimateReload` | `HvacClimateReloadCommand` | Drops `ClimateRegistry` cache for all docs so an edit to the baseline / project override is picked up without restarting Revit |
| (existing) `Hvac_PressureClassAudit` | now reads air density from the climate registry (location-aware) instead of the hardcoded 1.20 kg/m³ |
| (existing) `Hvac_ReloadRules` | unchanged — flushes `MepSizingRegistry` |

LOADS tab gains a "Block load" primary button; CALCS tab gains "NC
predict" and "Refrigerant size" buttons; RPRT tab gains "Climate
inspect" + "Reload climate" buttons.

### Climate site resolution

Priority order: `PRJ_CLIMATE_SITE_ID` parameter on
`ProjectInformation` → fuzzy match of `ProjectInformation.Address`
against site labels → first site in the project override file → hard
fallback to `london`. Air density at the cooling design dry-bulb is
elevation-corrected via the standard atmosphere model and replaces
the previous hardcoded 1.20 kg/m³ in the pressure-class audit.

### Caveats (Phase 187 — what's still open)

1. Built without `dotnet build` verification (Linux sandbox). Verify in Revit before merge.
2. **`BlockLoadEngine` is sensible-load focused.** Latent is calculated but the design-day model is simplified (single sinusoid for outdoor temp, ASHRAE Clear Sky for solar, no thermal-mass storage / RTS lag). For comparison-grade results against TRACE / HAP, fold in a per-orientation Radiant Time Series — the input data structures already support per-segment orientation.
3. **`NcPredictionEngine` uses a *synthetic* fan source** derived from path Q + ΔP. Until a manufacturer Lw spectrum sidecar lands, NC predictions are indicative not certifiable. Silencer insertion-loss spectra are also defaults (12 dB midband) until the same sidecar pattern is wired for attenuators. Breakout (TL through duct walls) is NOT yet implemented — the engine's docstring previously claimed it; references are now phrased as attenuation + regen only.
4. **`RefrigerantPipeSolver` ships 4 refrigerants** (R410A, R32, R134a, CO₂). Saturation state-point pairs are spot-design from ASHRAE Handbook Fundamentals + Daikin VRV manuals — not a full EoS engine. The two-phase suction multiplier is a flat 10 % rather than a Lockhart-Martinelli calc. Negative-lift (liquid going DOWN) doesn't credit the recovered head back to the ΔP budget yet.
5. **Climate site list ships 41 cities.** Add more by appending to the corporate `STING_CLIMATE_DATA.json` (PR encouraged) or via a project override at `<project>/_BIM_COORD/climate_data.json` (additive, by `id`).
6. **Manufacturer fitting + valve packs are seed.** ~20 entries each across Lindab / Trox / Halton / Belimo / Siemens / Danfoss. Production deployments should add their actual catalogue via the project override.
7. **Block-load `HVC_PEAK_*` stamps are TEXT-typed.** Reads via SetString; future projects that want to drive Revit schedules with HVACPower-typed params will need a SetDouble path + matching MR_PARAMETERS rebinding.

---

## Phase 188 — Tag Scheme Engine

**Status**: `Core/TagSchemeEngine.cs`. Named, data-driven rendering of canonical STING tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ + STATUS/REV) into a second tag string written to its own container parameter.

### Concept

A **Tag Scheme** is a JSON-described rendering grammar that describes how to compose a project-specific tag string from canonical STING tokens. Distinct from the ISO 19650 `DISC-LOC-ZONE-…-SEQ` canonical format — it lets a project team publish a second tag in whatever naming convention the client or programme requires (e.g., NBS Uniclass, NHS SFT, Digi-twin asset IDs).

### Architecture

- **Segment kinds**: `token` (source token with optional value map)
- **Corporate baseline**: `Data/STING_TAG_SCHEMES.json`
- **Project override**: `<project>/_BIM_COORD/tag_schemes.json`
- **Render stamp**: `_BIM_COORD/.sting_tag_scheme_stamp.json`
- **Commands**: `TagScheme_Render`, `TagScheme_Audit`

### Integration points

`StingAutoTagger` calls `TagSchemeEngine.RenderAll(doc, el)` after `WriteTag7All` so auto-tagged elements carry scheme tags immediately. `BuildTagsCommand` and `BatchTagCommand` both invoke the engine after the standard pipeline. Drift is surfaced by `TagScheme_Audit` and healed by `TagScheme_Render`.

---

## Phase 189 — Uganda Regional Defaults

**Status**: `StingTools/Commands/Structural/SetUgandanDefaultsCommand.cs` + `Core/UgandaRegionalDefaults.cs`.

Command tag `StrSetUgandanDefaults`. User picks a Uganda region (Central, Eastern, Western, Northern, Kampala Metro); command writes regional structural defaults onto `ProjectInformation`: wind speed per EC1-1-4 / Uganda NA, seismic zone and PGA per Uganda Seismic Map, soil bearing capacity, design rainfall intensity (for roof drainage), and live load per the Uganda National Building Code. Defaults are stored in `Core/UgandaRegionalDefaults.cs` as a typed lookup table. Shared params include `PRJ_REGION_WIND_MS`, `PRJ_REGION_SEISMIC_PGA`, `PRJ_REGION_SOIL_KPA`, `PRJ_REGION_RAINFALL_MMHR`, `PRJ_REGION_LIVE_LOAD_KPA`.

---

## Phase 190 — Structural / Materials Extensions

**Status**: `Commands/StructuralExt/`, `Commands/MaterialGate/`, `Core/Materials/`.

Key additions:
- **Material Gate** (`Core/MaterialGateCommands.cs`): Locks material
- **Per-Family Tier Map** (`Core/PerFamilyTierMap.cs`): Resolves
- **Material Revision Cloud Job** (`Core/MaterialRevisionCloudJob.cs`)
- **Material Re-tag Job** (`Core/MaterialRetagJob.cs`): Batch re-tags

---

## Phase 191 — Cost Management (Phases 184–191)

**Status**: `Commands/Cost/` (6 files) + `Core/CostPlan/` + `Core/Evm/` + `Core/PaymentCert/` + `Core/Variation/`.

The Cost Management module extends the BOQ system into a full construction cost control platform:

### Cost Commands (6 files)

| File | Key Commands |
|---|---|
| `CostCommands.cs` | `Cost_ValidateAll`, `Cost_ClearStale`, `Cost_RunWorkflow`, `Cost_ToggleStaleMarker`, `Cost_MigrateCurrencyParams`, `Cost_ReloadRules` |
| `CostPlanCommands.cs` | `CostPlan_Create`, `CostPlan_Update`, `CostPlan_Export`, `CostPlan_CompareStages`, `CostPlan_SetBudget` |
| `IfcAndIcmsCommands.cs` | `ICMS_Export`, `IFC_CostIngest`, `ICMS_Validate`, `IFC_CostBridge` |
| `MeasurementStandardCommands.cs` | `Meas_SetNRM`, `Meas_SetSMM7`, `Meas_SetCIOS`, `Meas_Audit`, `Meas_RuleReport` |
| `PaymentCertCommands.cs` | `PayCert_Create`, `PayCert_Export`, `PayCert_Reconcile`, `PayCert_Sign` |
| `VariationAndEvmCommands.cs` | `Var_Create`, `Var_Approve`, `EVM_Dashboard`, `EVM_Export`, `EVM_Forecast` |

### Core Engines

| Path | Purpose |
|---|---|
| `Core/CostPlan/` | Cost plan POCO + rate engine + NRM2/SMM7/CESMM rule sets + ICMS mapping |
| `Core/Evm/` | Earned Value Management: PV / EV / AC / SPI / CPI + S-curve + forecast-at-completion |
| `Core/PaymentCert/` | Payment certificate lifecycle: draft → reviewed → certified → paid + retention calc |
| `Core/Variation/` | Variation order workflow: create → quote → assess → approve + budget impact |

### Extensible Storage Schemas

`StingCostRateOverrideSchema` (Phase 187) extended with `StingCostPlanSchema`, `StingEVMSnapshotSchema`, `StingPayCertSchema` for session-persistent cost data.

---

## Phase 192 — KUT, CSI, Fohlio, LOD, Multi-Building

### Phase 192A — KUT / Owner System

**Kampala Uganda Temple** is the first major project client using STING with an owner-standards overlay. KUT-specific tooling under `Commands/Kpi/`, `Core/`, and `GUIDES/`:

#### GUIDES/ Directory

| File | Purpose |
|---|---|
| `GUIDES/KUT_BEP_TEMPLATE.md` | ISO 19650-2 BIM Execution Plan template. Client = The Church; Lead Appointed Party = Symbion Consulting; Information Manager = Planscape Consulting Engineers Ltd — Mayanja Davis. Uses `[FILL: ...]` placeholder pattern. STINGTOOLS-generated artefacts are annotated. |
| `GUIDES/KUT_BIM_MANAGER_PLAYBOOK.md` | BIM Manager self-guide playbook for the KUT project — step-by-step checklist for weekly / monthly / milestone BIM management tasks |
| `GUIDES/KUT_MIDP_TEMPLATE.csv` | Master Information Delivery Plan CSV template pre-seeded with KUT deliverable codes |

#### KUT KPI Dashboard

`Commands/Kpi/KutKpiDashboardCommand.cs` — tag `KUT_KpiDashboard`. Read-only command generating a monthly BIM status report for the KUT project:
- Gathers: tag/naming compliance %, per-discipline breakdown
- Persists `KutKpiSnapshot` records to `<project>/_BIM_COORD/kpi/kut_kpi_log.jsonl`
- Exports HTML + CSV for monthly status report attachment
- Snapshot fields: `CompliancePct`, `StrictPct`, `RevisionPct`

### Phase 192B1 — LOD Verification Engine

`Core/LODValidationCommand.cs` + validation rules in `Core/Validation/`. Per-element LOD (Level of Detail / Level of Development) audit:
- Reads `STING_LOD_VERIFICATION_RULES.json`
- Commands: `LOD_Verify`, `LOD_SetTarget`, `LOD_Report`, `LOD_Colorize`
- Writes `LOD_TARGET_TXT` + `LOD_ACTUAL_TXT` + `LOD_PASS_BOOL` per element
- Integrates with `ComplianceScan` as an additional compliance dimension

### Phase 192C1 — Fohlio Room Finishes Integration

`StingTools/ExLink/FohlioFinishesCommands.cs`:
- **`Fohlio_ExportFinishes`**
- **`Fohlio_ImportFinishes`**

Project overlay at `<project>/_BIM_COORD/fohlio_map.json` for room-number → element-id pre-caching.

### Phase 192C2 — CSI MasterFormat / SpecLink Integration

`StingTools/Commands/Classification/CsiCommands.cs`:
- **`CSI_Assign`**
- **`SpecLink_Reconcile`**

### Phase 192D — Multi-Building Commands

`Core/MultiBuildingCommands.cs` + `Core/MultiBuildingExtraCommands.cs`. Commands for large multi-building campuses (hospital complexes, universities, KUT campus):
- `MultiBuilding_SetBldgCode`
- `MultiBuilding_AuditCodes`
- `MultiBuilding_SyncTags`
- `MultiBuilding_Export`

---

## Phase 193 — Plugin Infrastructure + LLM Service

### Plugin Infrastructure

Three new singleton classes in `Core/`:

| File | Purpose |
|---|---|
| `PluginSchemaVersion.cs` | Declares current internal schema version (`STING_SCHEMA_VERSION`). On document open, checks `STING_PLUGIN_SCHEMA_TXT` stamped on ProjectInformation — if older, triggers migration prompts. On schema bump, runs idempotent migration steps (param remap, ES schema upgrade). |
| `PluginTelemetry.cs` | Anonymous usage telemetry (command invocation counts, performance histograms, error rates). Posts to Planscape Server `/api/telemetry` when connected; buffers locally at `<user>/.sting/telemetry.jsonl` when offline. GDPR-compliant: no PII, opt-out via `Serilog__MinimumLevel__Default = Warning` |
| `PluginUpdateChecker.cs` | On startup, checks `https://api.planscape.build/plugin/version` for the latest plugin version. If newer than the running version, shows a dismissible notification in the status bar. Respects a 24-h cooldown between checks (stored in `StingOfflineConfig`). |

### StingLlmService

`Core/StingLlmService.cs` — integration layer for LLM-assisted BIM operations:
- `AskAsync(prompt, context)`
- Used by: `NLPCommandProcessor` (intent classification fallback)
- Falls back gracefully when offline or when the server has no LLM key configured

### StingOfflineConfig

`Core/StingOfflineConfig.cs` — lightweight JSON-backed settings store at `<user>/.sting/config.json`. Stores: last-known Planscape Server URL, telemetry opt-out flag, update-check cooldown, active tag scheme id, and per-project last-sync timestamp.

---

## Phase 194 — Final Alignment + Deployment

### render.yaml (Render.com Blueprint)

`render.yaml` at the **repository root** is the Infrastructure-as-Code blueprint for deploying Planscape Server to Render.com:

```yaml
services:
  - type: web
    name: planscape-api
    runtime: docker
    dockerfilePath: ./Planscape.Server/docker/Dockerfile
    dockerContext: .
    branch: main
    region: frankfurt
    plan: starter               # £6/mo

    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: ASPNETCORE_URLS
        value: http://+:8080
      - key: ConnectionStrings__Default
        fromDatabase:
          name: planscape-db
          property: connectionString
      - key: Jwt__Key
        sync: false             # Set in Render dashboard — never commit
      - key: Jwt__Issuer
        value: Planscape
      - key: Jwt__Audience
        value: Planscape.Client
      - key: PLANSCAPE_OWNER_EMAIL
        value: davis@planscape.build
      - key: PLANSCAPE_OWNER_PASSWORD
        sync: false             # Set in Render dashboard — never commit
      - key: Cors__Origins__0
        value: https://planscape.build
      - key: Cors__Origins__1
        value: https://app.planscape.build
      - key: Serilog__MinimumLevel__Default
        value: Warning

    healthCheckPath: /health
    autoDeploy: true

databases:
  - name: planscape-db
    databaseName: planscape
    user: planscape
    region: frankfurt
    plan: starter               # £6/mo
```

**Cost at launch**: API £6 + DB £6 = **£12/month**. Redis is optional — refresh-token tracking + JTI blacklist degrade gracefully without it.

**Deploy steps**:
1. Render dashboard → Blueprints → New Blueprint Instance → connect `beckykyomugisha/stingtools`
2. Set `Jwt__Key` (32+ byte random, e.g. `openssl rand -base64 48`) in service Environment tab
3. Set `PLANSCAPE_OWNER_PASSWORD` in service Environment tab
4. Add custom domain `api.planscape.build` (service → Settings → Custom Domains) + CNAME at registrar

### Production Domain

- **API**: `api.planscape.build` → Render planscape-api service
- **Web app**: `app.planscape.build` → Planscape React/Expo web build
- **Marketing**: `planscape.build` → `marketing-site/`
- **Platform owner**: `davis@planscape.build`

---

## StingBridge — ArchiCAD ↔ Planscape Connector

**Status**: `StingBridge/` directory — Python 3.11 package.

`StingBridge` is a Python CLI + library that synchronises ArchiCAD model data with the Planscape Server, mirroring what the Revit plugin does natively via the C# `TagSyncController`.

### Entry Point — `StingBridge/bridge.py`

| Command | Description |
|---|---|
| `sync` | One-shot: pull tagged elements from ArchiCAD via the ArchiCAD JSON API, push to Planscape Server |
| `watch` | Polling sync loop (interval controlled by `STING_WATCH_INTERVAL`, default 30 s) |
| `watch-ifc --drop-dir /path` | Watch a directory for new IFC files; process each on arrival |
| `process-ifc /path/to/model.ifc` | Parse a single IFC file (via ifcopenshell), extract STING pset values, push to Planscape |

### Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `STING_PLANSCAPE_URL` | — | Planscape Server base URL (e.g. `https://api.planscape.build`) |
| `STING_PLANSCAPE_EMAIL` | — | Login email for Planscape Server |
| `STING_PLANSCAPE_PASSWORD` | — | Login password |
| `STING_PLANSCAPE_PROJECT_ID` | — | Target project UUID |
| `STING_ARCHICAD_PORT` | `19723` | ArchiCAD JSON API port |
| `STING_WRITE_BACK` | `false` | Whether to write Planscape values back into ArchiCAD |
| `STING_WATCH_INTERVAL` | `30` | Polling interval in seconds for `watch` mode |
| `STING_IFC_DROP_DIR` | — | Directory to watch for IFC drops |

### Directory Structure

```
StingBridge/
├── bridge.py           # CLI entry point
├── config.py           # Settings + env-var loader
├── requirements.txt    # ifcopenshell, requests, watchdog
├── StingBridge.csproj  # (stub — for future .NET host integration)
├── archicad/           # ArchiCadClient — JSON API wrapper
├── planscape/          # PlanscapeClient — REST API wrapper
├── sync/               # SyncEngine — diff + push logic
├── watch/              # FileSystemEventHandler for IFC drop-dir
├── tests/              # pytest test suite
├── get_project_id.py   # Helper: list + select Planscape project IDs
└── make_test_ifc.py    # Helper: generate minimal IFC fixture for testing
```

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

- **Drawable zone detection**: Finds usable area within title block
- **Shelf packing**: Row-based bin packing for auto-layout with configurable margins
- **MaxRects packing**: Best Short Side Fit heuristic for optimal space utilisation
- **Collision detection**: 2D AABB overlap checking between viewports
- **Layout presets**: 6 built-in presets
- **Sheet templates**: 6 built-in templates
- **ISO 19650 compliance**: 10 rules
- **Grid alignment**: Configurable grid with snap-to-nearest
- **Edge alignment**: 6 modes
- **Batch PDF export**: By scope (all/discipline/selection) with sanitised filenames
- **Two-pass rename**: Avoids Revit sheet number conflicts during batch renumbering

## Model Auto-Modeling Engine

`Model/` directory (3 files, 3,260 lines) provides automated Revit element creation and DWG-to-BIM conversion with 16 commands.

### ModelEngine — `Model/ModelEngine.cs` (1,336 lines)
- Orchestrates creation of all architectural and MEP elements
- `CreateWall(doc, start, end, height, typeId)`
- `CreateFloor(doc, profile, typeId, levelId)`
- `CreateFloorInRoom(doc, room)`
- `CreateRoof(doc, profile, typeId, levelId)`
- `BuildingShell(doc, width, depth, height)`
- Contains `FamilyResolver`
- Contains `WorksetAssigner`
- Contains `FailureHandler`
- All dimensions in millimeters externally, converted to Revit internal

### CADToModelEngine — `Model/CADToModelEngine.cs` (823 lines)
- DWG-to-BIM conversion engine for automated model creation from CAD imports
- `LayerMapper`
- Geometry extraction: parallel line detection (→ walls), closed loop
- `ExtractGeometry(doc, importInstance)`
- `CreateElements(doc, extractionResult)`

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
- **Total combinations**: 4 sizes × 4 styles × 8 colors = **128 per tag**

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
# Windows — set Revit API path and build
dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"
```

### Deployment

1. Build to produce `StingTools.dll`
2. Copy `StingTools.addin` to `C:\ProgramData\Autodesk\Revit\Addins\2025\` (machine) or `%APPDATA%\Autodesk\Revit\Addins\2025\` (user)
3. Copy `StingTools.dll` + `Newtonsoft.Json.dll` + `ClosedXML.dll` + `data/` folder alongside
4. Restart Revit

### Branching

- The default branch is `main`
- Feature branches: `feature/<description>` or `claude/<session-id>`
- Always create feature branches from the latest `main`
- Never commit directly to local `main` — branch off it, PR back. Direct
  commits drift the local copy from origin and surface as "diverged" the
  next time you try to pull or push.

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
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` copied to output `data/` directory at build time

---

## Implemented Feature Spotlights

Two multi-feature subsystems from industry-tool surveys (GRAITEC, Naviate, BIMLOGiQ). Full tutorial-grade write-ups (API recipes, code samples, external tool links) live in [`docs/CHANGELOG.md`](docs/CHANGELOG.md); the automation-gap backlog is in [`docs/ROADMAP.md`](docs/ROADMAP.md).

- **Color By Parameter** (`Select/ColorCommands.cs`, ~907 lines + `ColorHelper`)
- **Smart Tag Placement** (`Tags/SmartTagPlacementCommand.cs`, ~1,939 lines + `TagPlacementEngine`)

## Planscape Server

### Overview

**Planscape Server** is a cloud backend in `Planscape.Server/` (ASP.NET Core 8 + EF Core + SignalR + Hangfire + PostgreSQL 16 + Redis 7 + MinIO). It transforms the single-machine Revit plugin into a multi-user, multi-tenant SaaS platform.

> **Gap Analysis**: See `Planscape.Server/docs/PLANSCAPE_GAPS.md` for the comprehensive gap analysis and prioritised implementation roadmap.

### Controllers (22+)

`AuthController` · `ProjectsController` · `ProjectMembersController` · `TagSyncController` · `ComplianceController` · `IssuesController` · `DocumentsController` · `NotificationsController` · `WorkflowsController` · `MeetingsController` · `SeqSyncController` · `TransmittalsController` · `WarningsController` · `SearchController` · `PlatformController` · `AdminController` · `MimController` · `HealthcareController` · `PenetrationsController` · `StatusController` · `HvacController` · `IfcController`

### Entities (27+)

`Tenant` · `AppUser` · `Project` · `ProjectMember` · `TaggedElement` · `BimIssue` · `IssueAttachment` · `DocumentRecord` · `DocumentApproval` · `PlatformConnection` · `ComplianceSnapshot` · `SeqCounter` · `Meeting` · `Transmittal` · `WorkflowRun` · `LicenseKey` · `AuditLog` · `DevicePushToken` · `HealthcarePressureLog` · `HealthcareMgasVerification` · `HealthcareAntiLigatureAudit` · `HealthcareRdsSnapshot` · `PenetrationSignoff` · `HvacLoadSnapshot` · `HvacNcSnapshot` · `HvacRefrigerantSizing` · `ExternalElementMapping`

### Running Locally

```bash
cd Planscape.Server/docker && docker compose up -d
# API: http://localhost:5000 · Swagger: http://localhost:5000/swagger
# Demo login: admin@planscape.demo / admin123

cd Planscape && npm install && npx expo start
```

