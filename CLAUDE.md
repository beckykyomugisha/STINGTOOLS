# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **99 source files** (96 C# + 3 XAML, ~120,900 lines of code) across 10 directories
- **515 `IExternalCommand` classes** (commands) + 3 `IPanelCommand` classes + 1 `IExternalApplication` entry point + 1 `IExternalEventHandler` + 1 `IDockablePaneProvider` + 2 `IUpdater`s
- **43 runtime data files** (CSV, JSON, TXT, XLSX, PY, MD)
- **6 ribbon panels** with 23 pulldown groups + 1 WPF dockable panel (9 tabs) + 1 WPF project setup wizard

## Technology Stack

- **Platform**: Autodesk Revit 2025/2026/2027 (BIM software)
- **Language**: C# / .NET 8.0 (`net8.0-windows`)
- **Plugin type**: `IExternalApplication` + `IExternalEventHandler` + `IDockablePaneProvider` + `IUpdater` with `IExternalCommand` classes
- **Dependencies**: `Newtonsoft.Json` 13.0.3, `ClosedXML` 0.104.2 (XLSX/BOQ export), Revit API assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`)
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
    ├── Core/                           # Shared infrastructure (12 files, ~12,000 lines)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI + dockable panel registration + ToggleDockPanelCommand + DocumentOpened quality gate
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error) + EscapeChecker utility
    │   ├── ParamRegistry.cs            # Single source of truth for parameter names, GUIDs, containers, bindings (loads from PARAMETER_REGISTRY.json) + stale/cluster/display/position + COBie/asset/style constants
    │   ├── ParameterHelpers.cs         # Parameter read/write + SpatialAutoDetect + NativeParamMapper + TokenAutoPopulator + PhaseAutoDetect + TypeTokenInherit + CopyTokensFromNearest + GetInt + SetInt + CommandExecutionContext
    │   ├── SharedParamGuids.cs         # Backwards-compatible facade wrapping ParamRegistry (GUID lookups, category bindings)
    │   ├── TagConfig.cs               # ISO 19650 tag lookup tables, tag builder, TagIntelligence, TAG7 narrative builder + SeqScheme variants + BuildDisplayTag
    │   ├── StingAutoTagger.cs          # IUpdater — real-time auto-tagging + visual tag placement + discipline filter + StingStaleMarker IUpdater
    │   ├── WorkflowEngine.cs           # Workflow orchestration — JSON preset command chaining + conditional steps + result persistence + WorkflowTrendCommand
    │   ├── ComplianceScan.cs           # Cached compliance scan with per-discipline breakdown (DiscComplianceData)
    │   ├── OutputLocationHelper.cs     # Centralized output directory management with fallback chain + timestamped paths
    │   ├── IPanelCommand.cs            # Interface for WPF dockable panel commands + SafeApp/SafeDoc/SafeUIDoc extension methods
    │   └── PerformanceTracker.cs       # Lightweight performance profiling engine for batch operations (per-element timing, LRU slowest tracking, CSV export)
    │
    ├── Select/                         # Element selection + color commands (4 files, ~30+ commands)
    │   ├── CategorySelectCommands.cs   # 14 category selectors + SelectAllTaggable + CategorySelector helper
    │   ├── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite + SelectStale + QuickTagPreview
    │   ├── ColorCommands.cs            # 5 color-by-parameter commands + ColorHelper (10 palettes, presets, filter gen)
    │   └── TagSelectorCommands.cs      # Multi-criteria tag selector (text, size, arrowhead, leader, family, host category, orientation, discipline)
    │
    ├── UI/                             # WPF dockable panel UI + project wizard + theme engine (7 C# files + 3 XAML, ~12,770 lines)
    │   ├── StingDockPanel.xaml         # WPF markup for 9-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM/TAGS)
    │   ├── StingDockPanel.xaml.cs      # Code-behind: button dispatch, colour swatches, status bar
    │   ├── StingCommandHandler.cs      # IExternalEventHandler — dispatches 590+ button tags to 376 command classes + inline helpers
    │   ├── StingDockPanelProvider.cs   # IDockablePaneProvider — registers panel with Revit
    │   ├── StingProgressDialog.cs      # Reusable modeless WPF progress window for batch operations (cancel, ETA, progress bar)
    │   ├── StingListPicker.cs          # Reusable WPF list picker dialog with search/filter, replacing paginated TaskDialogs
    │   ├── ThemeManager.cs             # WPF theme engine — Dark/Light/Grey/Corporate themes with 13 color resource keys
    │   ├── ProjectSetupWizard.xaml     # WPF 7-page project setup wizard dialog
    │   └── ProjectSetupWizard.xaml.cs  # Code-behind: presets, validation, discipline config, review summary
    │
    ├── Docs/                           # Documentation commands (10 files, ~35+ commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   ├── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │   ├── DocAutomationCommands.cs    # DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets
    │   ├── DocAutomationExtCommands.cs # Batch views/sheets/sections/elevations, doc package, scope boxes, templates, drawing register, browser organizer, handover manual
    │   ├── ViewAutomationCommands.cs   # DuplicateView, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign, MagicRename, ViewTabColour
    │   ├── HandoverExportCommands.cs   # FM/O&M handover: COBie 2.4 export (11 sheets), maintenance schedule, O&M manual, asset health report, space handover report
    │   └── JournalParserCommand.cs     # Revit journal diagnostics: parse journal files for errors, crashes, command timeline, memory usage
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
    ├── BIMManager/                     # ISO 19650 BIM management + 4D/5D scheduling + Excel/Platform/Revision (5 files, 73 commands, ~14,343 lines)
    │   ├── BIMManagerCommands.cs       # 37 commands: BEP (Create/Update/Export/Generate), Issues (Raise/Dashboard/Update/SelectElements), Documents (Register/Add/ValidateNaming/Briefcase), COBie export, Transmittals, CDE status, Reviews, ISO reference, BulkExport, StickyNotes (Create/Export/Select), ModelHealth (Dashboard/Export), MidpTracker, Export4DTimeline, Export5DCostData, FullComplianceDashboard, LinkPredecessors, AssignPhaseDates, MeasuredQuantities, ElementCountSummary, SetOutputDirectory, StageComplianceGate + BIMManagerEngine
    │   ├── ExcelLinkCommands.cs        # 6 commands: ExportToExcel, ImportFromExcel, ExcelRoundTrip, ExportSchedulesToExcel, ImportSchedulesFromExcel, ExportTemplate + ExcelLinkEngine
    │   ├── PlatformLinkCommands.cs     # 6 commands: ACCPublish, CDEPackage, BCFExport, BCFImport, PlatformSync, SharePointExport + PlatformLinkEngine
    │   ├── RevisionManagementCommands.cs # 12 commands: CreateRevision, RevisionDashboard, AutoRevisionCloud, RevisionSchedule, TrackElementRevisions, RevisionCompare, IssueSheetsForRevision, RevisionNamingEnforce, RevisionTagIntegration, RevisionExport, BulkRevisionStamp, AutoRevisionOnTagChange + RevisionEngine
    │   └── SchedulingCommands.cs       # 12 commands: AutoSchedule4D, ImportMSProject, ViewTimeline4D, ExportSchedule4D, AutoCost5D, ImportCostRates, CostReport5D, CashFlow5D, PhaseFilter, PhaseSummary, MilestoneRegister, WorkingCalendar + Scheduling4DEngine
    │
    ├── Model/                          # Auto-modeling engine (3 files, 16 commands)
    │   ├── ModelCommands.cs            # 16 model commands: Wall, Room, Floor, Ceiling, Roof, Door, Window, Column, ColumnGrid, Beam, Duct, Pipe, Fixture, BuildingShell, DWGToModel, DWGPreview
    │   ├── ModelEngine.cs              # Model creation engine: walls, floors, roofs, columns, beams, MEP, rooms, building shell + FamilyResolver
    │   └── CADToModelEngine.cs         # DWG-to-BIM conversion engine: layer mapping, geometry extraction, element auto-detection
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
    └── Data/                           # Runtime data files (42 files)
        ├── BLE_MATERIALS.csv           # 815 building-element materials
        ├── MEP_MATERIALS.csv           # 464 MEP materials
        ├── MR_PARAMETERS.txt           # Shared parameter file (2,307 params, 18 groups, all data files cross-referenced)
        ├── MR_PARAMETERS.csv           # Parameter definitions
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
        ├── LABEL_DEFINITIONS.json      # 10,775-line label/legend definition specs (v5.5, 126 categories, warnings aligned to TAG7)
        ├── TAG_CONFIG_v5_0_CONTAINERS.csv    # 122+ tag container definitions (v5.0) + Section 13: tie-in point containers (10 params + 4 TAG7 containers + 6 tag families)
        ├── TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv # 179+ discipline/system/function code mappings (v5.0) + Section 7: 14 tie-in system mappings
        ├── TAG_CONFIG_v5_0_VALIDATION.csv    # 180+ validation rules for tag tokens (v5.0) + Section 13: 13 tie-in validation rules
        ├── STING_TAG_CONFIG_v5_0_ARCH.csv    # Architectural tag family definitions (v5.0, 111 warnings across 33 tag families)
        ├── STING_TAG_CONFIG_v5_0_GEN.csv     # General tag family definitions (v5.0)
        ├── STING_TAG_CONFIG_v5_0_MEP.csv     # MEP tag family definitions (v5.0, 183 warnings across 51 tag families including 6 tie-in point tags #46-#51)
        ├── STING_TAG_CONFIG_v5_0_STR.csv     # Structural tag family definitions (v5.0)
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide (original)
        ├── TAG GUIDE V2.xlsx           # Tag reference guide (comprehensive update)
        ├── TAG_GUIDE_V3.csv            # Tag reference guide (newest CSV version)
        ├── TAG_PLACEMENT_PRESETS_DEFAULT.json  # Default tag placement preset (12 category rules)
        ├── TAG_STYLE_RULES.json        # Tag style engine rules (v2.0, 128 type combinations, discipline presets)
        ├── WORKFLOW_DailyQA_Enhanced.json     # Enhanced Daily QA workflow (8 conditional steps)
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
        ├── TAGGING_GUIDE.md            # Complete tagging guide documentation
        └── TAG_FAMILY_CREATION_GUIDE.md # End-to-end tag family creation workflow guide
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
| Batch Rename Views | `Docs.BatchRenameViewsCommand` | Manual | Bulk rename views with pattern templates |
| Copy View Settings | `Docs.CopyViewSettingsCommand` | Manual | Copy filters, graphic overrides, and template from source view |
| Auto-Place Viewports | `Docs.AutoPlaceViewportsCommand` | Manual | Auto-place and scale viewports on sheets |
| Crop to Content | `Docs.CropToContentCommand` | Manual | Smart crop region generation based on element extents |
| Batch Align Viewports | `Docs.BatchAlignViewportsCommand` | Manual | Multi-view alignment on sheets |

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
| `UI/ThemeManager.cs` | 0 (WPF theme engine — Dark/Light/Grey/Corporate themes) | 149 |
| `UI/ProjectSetupWizard.xaml.cs` | 0 (WPF wizard code-behind: 7 pages, presets, discipline config) | 1,124 |
| `UI/StingDockPanel.xaml` | — (WPF markup, 9-tab panel with ~610 buttons) | 2,949 |
| `UI/ProjectSetupWizard.xaml` | — (WPF markup, 7-page wizard dialog) | 793 |
| `UI/StingDockPanel_TagStudio.xaml` | — (WPF markup, Tag Studio compass/controls) | 1,376 |
| **Total** | **~515 commands** | **~120,900** |

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
| DOCS | Sheet/view organization, viewports, document automation | Docs |
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

- The default branch is `master`
- Feature branches: `feature/<description>` or `claude/<session-id>`
- Always create feature branches from the latest `master`

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
- **Assembly**: v1.0.0.0, GUID `A1B2C3D4-5678-9ABC-DEF0-123456789ABC`, Vendor: StingBIM
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` copied to output `data/` directory at build time

---

## Automation Gap Analysis & Feature Roadmap

### Current Automation Gaps

#### A. Gaps That Hinder Full Automation

| Gap | Location | Problem | Impact |
|-----|----------|---------|--------|
| ~~**No tag collision detection**~~ | `TagConfig.cs` | **DONE** — `BuildAndWriteTag` accepts `existingTags` HashSet for O(1) collision detection; auto-increments SEQ on duplicate. `BuildExistingTagIndex()` builds the index once per batch. All callers updated. | Done |
| ~~**No progress reporting**~~ | `BatchTagCommand`, `MasterSetupCommand` | **DONE** — BatchTag shows element count upfront, logs every 500 elements, reports duration. MasterSetup reports per-step timing. | Done |
| ~~**No cancellation support**~~ | All batch commands | **DONE** — `StingProgressDialog` provides modeless progress window with Cancel button and Escape key detection. `EscapeChecker` utility for Win32 key state. `WorkflowEngine` checks cancellation between steps. | Done |
| ~~**Hardcoded category bindings**~~ | `SharedParamGuids.cs`, `ParamRegistry.cs` | **DONE** — Discipline bindings derived from `PARAMETER_REGISTRY.json` container_groups (data-driven). `CATEGORY_BINDINGS.csv` loaded by `TemplateManager.LoadCategoryBindings()` and used by `LoadSharedParamsCommand` Pass 2 to augment JSON bindings. `FAMILY_PARAMETER_BINDINGS.csv` loaded by `BatchAddFamilyParamsCommand`. | Done |
| ~~**No error recovery**~~ | `MasterSetupCommand.cs` | **DONE** — Wrapped in `TransactionGroup` for atomic rollback. If critical step 1 (Load Params) fails, user can rollback immediately. Per-step timing reported. | Done |
| ~~**Fixed tag format**~~ | `ParamRegistry.cs`, `TagConfig.cs` | **DONE** — Tag format (separator, num_pad, segment_order) loaded from `PARAMETER_REGISTRY.json`, with project-level overrides via `project_config.json` TAG_FORMAT section. `ConfigEditorCommand` displays and saves tag format settings. | Done |
| ~~**Partially unused data files**~~ | `Data/` directory | **DONE** — All data files now loaded: CATEGORY_BINDINGS.csv (LoadSharedParams Pass 2), FAMILY_PARAMETER_BINDINGS.csv (BatchAddFamilyParams), MATERIAL_SCHEMA.json (SchemaValidate), BINDING_COVERAGE_MATRIX.csv (DynamicBindings), VALIDAT_BIM_TEMPLATE.py (ported to ValidateTemplate). | Done |

#### B. Enhancement Opportunities

| Enhancement | Why Needed | Status |
|-------------|-----------|--------|
| ~~Pre-tagging audit~~ | **DONE** — `PreTagAuditCommand` performs complete dry-run predicting tags, collisions, ISO violations, spatial detection, and family PROD codes. Exports CSV. | Done |
| ~~Tag collision auto-fix~~ | **DONE** — `BuildAndWriteTag` auto-increments SEQ on collision. User can choose Skip/Overwrite/AutoIncrement via `TagCollisionMode` enum. | Done |
| ~~LOC/ZONE auto-detection~~ | **DONE** — `SpatialAutoDetect` class auto-derives LOC and ZONE from room data and project info. Integrated into TagAndCombine, AutoPopulate, TagNewOnly, FamilyStagePopulate. | Done |
| ~~Family-aware PROD codes~~ | **DONE** — `TagConfig.GetFamilyAwareProdCode()` inspects family name for 35+ specific PROD codes (Mechanical, Electrical, Lighting, Plumbing, Fire Alarm). | Done |
| ~~TagAndCombine writes only 6 containers~~ | **DONE** — Now writes ALL 36 containers (6 universal + 30 discipline-specific). | Done |
| ~~No incremental tagging~~ | **DONE** — `TagNewOnlyCommand` pre-filters to untagged elements. Much faster for adding new elements. | Done |
| ~~CompoundTypeCreator material properties~~ | **DONE** — Applies color, transparency, smoothness, shininess from CSV. | Done |
| ~~**No template automation**~~ | **DONE** — `TemplateManagerCommands.cs` with 17 commands and `TemplateManager` intelligence engine: 5-layer auto-assignment, compliance scoring, VG diff, style definitions. `ViewTemplatesCommand` expanded to 23 template definitions with VG configuration. | Done |
| ~~**No dockable panel UI**~~ | **DONE** — WPF dockable panel (`UI/` directory, 6 files) with 7-tab interface (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL), `IExternalEventHandler` dispatch for thread safety, ~521 buttons, colour swatches, bulk parameter controls. | Done |
| ~~Cross-parameter validation~~ | **DONE** — `ISO19650Validator` validates all tokens, cross-validates DISC/SYS against category, validates tag format. `FixDuplicateTagsCommand` auto-resolves duplicates. | Done |
| ~~Formula evaluation engine~~ | **DONE** — `FormulaEvaluatorCommand` + `FormulaEngine` reads 199 formulas from CSV, evaluates in dependency order (levels 0-6), supports arithmetic, conditionals, string concat, and Revit geometry inputs. | Done |
| ~~Family-stage pre-population~~ | **DONE** — `FamilyStagePopulateCommand` pre-populates all 7 tokens before tagging (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD). | Done |
| ~~Leader management commands~~ | **DONE** — 14 leader management commands: Toggle/Add/Remove Leaders, Align Tags, Reset Positions, Toggle Orientation, Snap Elbows, Auto-Align Leader Text, Flip Tags, Align Text, Pin/Unpin, Nudge, Attach/Free, Select by Leader. | Done |
| ~~Tag register export~~ | **DONE** — `TagRegisterExportCommand` exports comprehensive 40+ column asset register (tags, identity, spatial, MEP, cost, validation) to CSV. | Done |
| ~~Full auto-populate pipeline~~ | **DONE** — `FullAutoPopulateCommand` runs Tokens, Dimensions, MEP, Formulas, Tags, Combine, Grid in one click with zero manual input. | Done |
| ~~Native parameter mapping~~ | **DONE** — `NativeParamMapper` maps 30+ Revit built-in parameters to STING shared parameters. | Done |
| ~~Document automation~~ | **DONE** — `DeleteUnusedViewsCommand`, `SheetNamingCheckCommand`, `AutoNumberSheetsCommand` for view cleanup and ISO 19650 sheet compliance. | Done |
| ~~Schedule field remapping~~ | **DONE** — `ScheduleHelper.LoadFieldRemaps()` loads SCHEDULE_FIELD_REMAP.csv; `BatchSchedulesCommand` auto-remaps deprecated field names. | Done |
| ~~Port VALIDAT_BIM_TEMPLATE.py (45 checks) to C# ValidateTemplateCommand~~ | **DONE** — `ValidateTemplateCommand` in `DataPipelineCommands.cs` performs 45 validation checks (data file inventory, parameter consistency, material completeness, formula dependencies, schedule definitions, cross-references). | Done |
| ~~Dynamic category bindings from BINDING_COVERAGE_MATRIX.csv~~ | **DONE** — `DynamicBindingsCommand` in `DataPipelineCommands.cs` loads bindings from CSV, replacing hardcoded `SharedParamGuids.AllCategoryEnums`. | Done |
| ~~Color By Parameter system~~ | **DONE** — `ColorCommands.cs` with 5 commands: ColorByParameter (10 palettes, `<No Value>` detection), ClearOverrides, SavePreset, LoadPreset, CreateFiltersFromColors. Full `OverrideGraphicSettings` support. | Done |
| ~~Smart Tag Placement~~ | **DONE** — `SmartTagPlacementCommand.cs` with 9 commands: SmartPlace (8-position collision avoidance), Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight. `TagPlacementEngine` with scale-aware offsets and 2D AABB collision detection. | Done |
| ~~View automation commands~~ | **DONE** — `ViewAutomationCommands.cs` with 6 commands: DuplicateView, BatchRename, CopyViewSettings, AutoPlaceViewports, CropToContent, BatchAlignViewports. | Done |
| ~~Annotation color management~~ | **DONE** — 5 commands in `TagOperationCommands.cs`: ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors. | Done |
| ~~Schema validation~~ | **DONE** — `SchemaValidateCommand` validates BLE/MEP CSV columns match MATERIAL_SCHEMA.json (77-column schema). | Done |
| ~~Schedule management system~~ | **DONE** — `ScheduleEnhancementCommands.cs` (1,579 lines) with 9 commands: Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report. Plus ScheduleAutoFit, MatchWidest (functional), ToggleHidden inline operations. `ScheduleAuditHelper` engine loads CSV definitions for cross-reference. | Done |
| ~~Configurable tag format in project_config.json (separator, padding, segments)~~ | **DONE** — TAG_FORMAT section in project_config.json with `ParamRegistry.ApplyTagFormatOverrides()`. ConfigEditorCommand displays and saves tag format. | Done |
| ~~Batch command chaining / workflow presets~~ | **DONE** — `WorkflowEngine` with JSON presets, 3 built-in workflows, cancellation, TransactionGroup rollback | Done |
| ~~Cancellation support~~ | **DONE** — `StingProgressDialog` + `EscapeChecker` for batch operations | Done |
| ~~Real-time auto-tagging~~ | **DONE** — `StingAutoTagger` IUpdater for zero-touch tagging on element placement | Done |
| ~~Live compliance dashboard~~ | **DONE** — `ComplianceScan` cached RAG status for status bar | Done |
| ~~IFC/BEP/Clash pipeline~~ | **DONE** — 6 new DataPipeline commands (IFC, BEP, clash, Excel import, keynote sync) | Done |

---

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

### Remaining Underutilized Data Files

| File | Rows | Current Status | Proposed Usage |
|------|------|----------------|----------------|
| ~~`MATERIAL_SCHEMA.json`~~ | 77 cols | **DONE** — loaded by `SchemaValidateCommand` | Validates BLE/MEP CSV columns match schema |
| ~~`BINDING_COVERAGE_MATRIX.csv`~~ | Large | **DONE** — loaded by `DynamicBindingsCommand` | Replaces hardcoded category bindings |
| ~~`CATEGORY_BINDINGS.csv`~~ | 10,661 | **DONE** — loaded by `TemplateManager.LoadCategoryBindings()`, used in `LoadSharedParamsCommand` Pass 2 | Augments JSON-derived discipline bindings with CSV-based category mappings |
| ~~`FAMILY_PARAMETER_BINDINGS.csv`~~ | 4,686 | **DONE** — loaded by `TemplateManager.LoadFamilyParameterBindings()`, used in `BatchAddFamilyParamsCommand` | Data-driven family parameter binding with GUID validation |
| ~~`VALIDAT_BIM_TEMPLATE.py`~~ | 45 checks | **DONE** — ported to C# `ValidateTemplateCommand` | 45 validation checks now in `DataPipelineCommands.cs` |

---

### Implementation Priority Matrix

#### Completed (Phases 1-3)

1. **Tag collision detection** — O(1) lookup via `BuildExistingTagIndex`
2. **Pre-tagging audit** — `PreTagAuditCommand` with full dry-run prediction
3. **Tag New Only mode** — `TagNewOnlyCommand` for incremental tagging
4. **Fix Duplicates command** — `FixDuplicateTagsCommand` auto-resolves via SEQ increment
5. **Cross-parameter validation** — `ISO19650Validator` with DISC/SYS/category cross-check
6. **LOC/ZONE auto-detection** — `SpatialAutoDetect` from room and project data
7. **Family-aware PROD codes** — `GetFamilyAwareProdCode()` with 35+ mappings
8. **Formula evaluation engine** — `FormulaEvaluatorCommand` with recursive descent parser
9. **Document automation** — DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets
10. **Leader management** — 14 annotation leader commands
11. **Tag register export** — 40+ column comprehensive CSV export
12. **Full auto-populate pipeline** — Zero-input one-click automation
13. **Native parameter mapping** — 30+ Revit built-in to STING parameter mappings
14. **Family-stage pre-population** — All 7 tokens from category/spatial/family data
15. **Schedule field remapping** — Auto-remap deprecated field names from CSV
16. **WPF dockable panel** — 7-tab panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL) replicating pyRevit interface with IExternalEventHandler dispatch
17. **Template Manager intelligence engine** — 5-layer auto-assignment, compliance scoring, VG diff, 17 template automation commands
18. **View templates expanded** — 23 template definitions with full VG configuration (discipline plans, coordination, RCP, presentation, sections, 3D, elevations)
19. **Style definition commands** — Fill patterns, line styles, text styles, dimension styles, object styles created programmatically

#### Completed (Phase 4)

20. **Color By Parameter system** — `ColorCommands.cs`: 5 commands, 10 palettes, preset management, filter generation
21. **Smart Tag Placement** — `SmartTagPlacementCommand.cs`: 9 commands, `TagPlacementEngine` with 8-position collision avoidance
22. **Dynamic category bindings** — `DynamicBindingsCommand` loads from BINDING_COVERAGE_MATRIX.csv
23. **Port VALIDAT_BIM_TEMPLATE.py** — `ValidateTemplateCommand`: 45 validation checks ported to C#
24. **View automation** — `ViewAutomationCommands.cs`: 6 commands (Duplicate, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign)
25. **Annotation color management** — 5 new commands in `TagOperationCommands.cs` (ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors)
26. **Schema validation** — `SchemaValidateCommand` validates MATERIAL_SCHEMA.json against CSV data

#### Completed (Phase 5)

27. **Schedule management system** — `ScheduleEnhancementCommands.cs` with 9 commands (Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report) + `ScheduleAuditHelper` engine + enhanced MatchWidest, AutoFit, ToggleHidden inline operations
28. **BOQ Export** — `BOQExportCommand` with ClosedXML-based Excel export (6-column format, section headings, subtotals)
29. **Template VG Audit** — `TemplateVGAuditCommand` for Visual Graphics override analysis
30. **Tag appearance controls** — 5 commands in `TagOperationCommands.cs` (TagAppearance, SetTagBoxAppearance, QuickTagStyle, SetTagLineWeight, ColorTagsByParameter)
31. **Tag type management** — `SwapTagTypeCommand` for swapping tag family types on annotations

#### Completed (Phase 6)

32. **Cancellation support** — `StingProgressDialog` modeless WPF progress window with Cancel + Escape key. `EscapeChecker` Win32 utility.
33. **Batch command chaining / workflow presets** — `WorkflowEngine` with JSON presets, 3 built-in workflows (ProjectKickoff, DailyQA, DocumentPackage), TransactionGroup rollback
34. **Real-time auto-tagging** — `StingAutoTagger` IUpdater for zero-touch BIM: auto-tags elements on placement
35. **Live compliance dashboard** — `ComplianceScan` cached RAG status for WPF status bar display
36. **IFC/BEP/Clash pipeline** — 6 new DataPipeline commands: IFC export, IFC property map, BEP compliance, clash detection, Excel BOQ import, keynote sync
37. **Tag anomaly detection** — `AnomalyAutoFixCommand` in TagOperationCommands.cs
38. **Tag format migration** — `TagFormatMigrationCommand` + `TagChangedCommand` in BatchTagCommand.cs
39. **Corporate schedules** — `CorporateTitleBlockScheduleCommand` + `DrawingRegisterScheduleCommand`
40. **Revision cloud automation** — `RevisionCloudAutoCreateCommand` in DocAutomationExtCommands.cs
41. **FM Handover Manual** — `HandoverManualCommand` generates comprehensive FM handover documentation
42. **Custom category selector** — `SelectCustomCategoryCommand` for selecting elements from any category present in the active view

#### Completed (Phase 7 — Stability & Bug Fixes)

43. **Crash fixes** — Fixed native Revit crashes from rapid-fire transactions, TransactionGroup.Assimilate(), infinite recursion in ParamRegistry, and startup crashes
44. **Transaction safety** — Eliminated all `TransactionGroup` and `SilentWarningSwallower` usage; consolidated rapid-fire transactions into single transactions
45. **TaskDialog deadlock fix** — Prevented TaskDialog.Show() inside active transactions causing deadlock
46. **Data file access** — Eager load data files, crash-resistant logging, defensive guards
47. **LoadSharedParams** — Auto-set MR_PARAMETERS.txt path and skip already-bound parameters
48. **Parameter binding** — Fixed binding counts, dropdown population, export save location, dialog buttons
49. **Tagging pipeline** — Fixed LVL handling, level code parsing, SetString safety, TAG7 writing, key format, overflow guards

#### Completed (Phase 8 — Data Integration & Configuration)

50. **Configurable tag format** — Separator, padding, segment order configurable via `project_config.json` TAG_FORMAT section with `ParamRegistry.ApplyTagFormatOverrides()`. `ConfigEditorCommand` displays and saves tag format settings.
51. **Dynamic discipline bindings** — `CATEGORY_BINDINGS.csv` (10,661 entries) loaded by `TemplateManager.LoadCategoryBindings()` and used in `LoadSharedParamsCommand` Pass 2 to augment JSON-derived bindings.
52. **Family parameter auto-binding** — `FAMILY_PARAMETER_BINDINGS.csv` (4,686 entries) loaded by `BatchAddFamilyParamsCommand` for data-driven family parameter binding with GUID validation.

#### Completed (Phase 9 — Model Engine, Tag Styles & Tag Family Expansion)

53. **Auto-modeling engine** — `Model/` directory with 16 commands: walls, floors, roofs, columns, beams, MEP, rooms, building shell, DWG-to-BIM conversion. `ModelEngine` + `CADToModelEngine` with `LayerMapper` for 18 DWG layer categories.
54. **Tag style engine** — `TagStyleEngine.cs` + `TagStyleCommands.cs` with 9 commands: 128 style combinations via `TAG_{SIZE}{STYLE}_{COLOR}_BOOL` parameter matrix. 8 built-in color schemes (Discipline, Warm, Cool, Red, Yellow, Blue, Monochrome, Dark).
55. **Tag family expansion to 124 categories** — `TagFamilyCreatorCommand.cs` expanded to v5.0 with comprehensive category coverage, TAG7 sub-sections, and label configuration.
56. **LABEL_DEFINITIONS.json v5.0** — Expanded to complete tiers, TAG7, and warnings for all 123 categories.
57. **TAG7 natural language** — Enhanced TAG7 narrative with natural language connecting words throughout all sections.
58. **Family parameter processor** — `FamilyParameterProcessorCommand` for batch .rfa file parameter processing.
59. **Material data fixes** — Fixed density, thermal, patterns, textures for 997 material rows across BLE/MEP CSV files.

#### Completed (Phase 10 — COBie V2.4 & BEP Integration)

60. **22 COBie project type presets** — `COBiePreset` class with project-specific COBie configurations: Commercial Office, Healthcare NHS, Healthcare Private, Education School, Education University, Residential Standard, Residential High-Rise, Retail, Hotel, Data Centre, Industrial, Transport Station, Transport Airport, Defence MOD, Heritage, Mixed-Use, Laboratory, Sports/Leisure, Cultural, Modular/Off-Site, Infrastructure Civil, Infrastructure Water, Fit-Out Interior.
61. **COBie Type worksheet expanded** — Full COBie V2.4 Type fields: WarrantyGuarantorParts/Labor, WarrantyDuration, NominalLength/Width/Height, Shape, Size, Color, Finish, Grade, Material, Constituents, Features, AccessibilityPerformance, CodePerformance, SustainabilityPerformance — all populated from STING shared parameters.
62. **COBie Component worksheet expanded** — SerialNumber, InstallationDate, WarrantyStartDate, BarCode populated from STING commissioning and identity parameters.
63. **COBie Attribute worksheet expanded** — Export of 70+ STING parameters per component: source tokens, identity, spatial, lifecycle, commissioning, maintenance, regulatory, sustainability, MEP performance, BLE dimensions, TAG7 narratives, classification codes. Auto-categorized by parameter group.
64. **COBie Instruction worksheet added** — First worksheet per COBie V2.4 standard with generation metadata, preset info, tag format reference, and column colour coding guidance.
65. **COBie System worksheet fixed** — Groups by actual SYS parameter values from tagged elements instead of name string matching.
66. **COBie PickLists expanded** — STING-specific pick lists: DisciplineCode, LocationCode, ZoneCode, SystemCode, FunctionCode, StatusCode, CDEStatus, SuitabilityCode, ConditionGrade, CriticalityRating.
67. **COBie Connection classification** — Connector direction (Supply/Return/Bidirectional) and domain type (HVAC/Piping/Electrical/CableTray) classification.
68. **COBie Assembly enriched** — Wall/floor compositions include total thickness and fire rating from STING parameters.
69. **BEP presets expanded to 22** — Healthcare, Education, Retail, Hotel, Data Centre, Industrial, Transport, Defence, Heritage, Mixed-Use, Laboratory, Sports, Cultural, Modular, Residential High-Rise added.
70. **BEP Handover & Asset Management** — Enhanced with detailed COBie data drop schedule (DD1-DD4) per-stage: COBie sheets required, STING commands to use, tag completeness targets, responsible parties, and validation commands. Asset management strategy, CAFM integration, Golden Thread compliance, TIDP content.
71. **BEP Risk Register enhanced** — 10 BIM-specific risks with likelihood, impact, mitigation, and owner. Auto-enrichment propagates tag completeness to risk register entries.
72. **BEP Training and Competency Plan** — Section 17 added: role-based competency requirements and training schedule.

#### Completed (Phase 11 — STING Extended Prompt V2: 20 Enhancements)

73. **Type token inheritance** — `TypeTokenInherit` copies non-empty token values from element TYPE to instance before population. Called in both `PopulateAll` and `StingAutoTagger.Execute`.
74. **Cross-element token copy** — `CopyTokensFromNearest` finds nearest tagged element of same category within 10 ft and copies specified tokens to empty parameters.
75. **Stale element detection** — `StingStaleMarker` IUpdater detects geometry/level changes on tagged elements and sets `STING_STALE_BOOL = 1` for re-tagging.
76. **Visual auto-tagging** — `StingAutoTagger` optionally creates `IndependentTag` annotations alongside data tags. Toggled via `AutoTaggerToggleVisualCommand`.
77. **Auto-tagger discipline filter** — Restrict auto-tagging to specific discipline codes. Configured via `AutoTaggerConfigCommand`.
78. **Auto-tagger workset safety** — Skips elements on worksets not owned by current user in worksharing environments.
79. **Linked model tag placement** — `PlaceTagsInLinkedViews` in `TagPlacementEngine` creates annotations for elements in linked Revit models via `Reference.CreateLinkReference()`.
80. **Tag clustering** — `ClusterTagsCommand` groups nearby tags by category+discipline, keeps representative, writes `CLUSTER_COUNT`/`CLUSTER_LABEL`. `DeclusterTagsCommand` reverses.
81. **Display mode variants** — `SetDisplayModeCommand` sets `STING_DISPLAY_MODE` (5 modes: SEQ only, PROD-SEQ, DISC-SYS-SEQ, DISC-PROD-SEQ, full 8-segment) and writes `ASS_DISPLAY_TXT`.
82. **Per-view tag style routing** — `SetViewTagStyleCommand` writes `STING_VIEW_TAG_STYLE` parameter on active view (Discipline, Monochrome, Warm, Cool schemes).
83. **Sequence numbering variants** — `SeqScheme` enum (Numeric/Alpha/ZonePrefix/DiscPrefix), `SetSeqSchemeCommand`, zone-based SEQ resets via `SeqIncludeZone`/`SeqLevelReset`.
84. **Family parameter injection** — `FamilyParamCreatorCommand` + `FamilyParamEngine`: injects shared parameters, tag position formulas, and position types into .rfa family documents.
85. **Tag position switching** — `SwitchTagPositionCommand` (4 positions: Above/Right/Below/Left), `AlignTagBandsCommand` (grid-align tags by Y coordinate), `ExportTagPositionsCommand` (CSV export).
86. **Conditional workflow steps** — `WorkflowStep` extended with `MinCompliancePct`, `MaxCompliancePct`, `RequiresStaleElements` for intelligent step skipping.
87. **Workflow result persistence** — `WorkflowRunRecord` saved to `STING_WORKFLOW_LOG.json` (capped at 100). `WorkflowTrendCommand` displays compliance trend analysis.
88. **Document-open quality gate** — `StingToolsApp` subscribes to `DocumentOpened` event, runs `ComplianceScan`, updates WPF status bar with RAG status.
89. **Per-discipline compliance** — `ComplianceScan.ByDisc` dictionary of `DiscComplianceData`. `DisciplineComplianceReportCommand` displays tabular breakdown with CSV export.
90. **Pre-tag audit auto-fix chain** — `PreTagAuditCommand` offers one-click auto-fix: runs `AnomalyAutoFixCommand` → `ResolveAllIssuesCommand` → shows before/after compliance improvement.
91. **Retag stale elements** — `RetagStaleCommand` finds elements with `STING_STALE_BOOL = 1`, re-derives tags, clears stale flag.
92. **New data files** — `TAG_PLACEMENT_PRESETS_DEFAULT.json` (12 category placement rules), `WORKFLOW_DailyQA_Enhanced.json` (8 conditional steps).

#### Completed (Phase 12 — Deep Review: Bug Fixes, Logic Corrections, Automation Enhancements)

93. **SEQ counter key warning** — `_seqSchemeChanged` flag detects mid-project SEQ scheme changes, logs warning once per session.
94. **NativeParamMapper SYS overwrite removed** — SYS derivation now exclusively via `TokenAutoPopulator.PopulateAll`.
95. **BuildAndWriteTag seqKey drift fix** — seqKey now uses actual stored token values to prevent counter/tag namespace drift.
96. **ValidateStrictMode** — `TagConfig.ValidateStrictMode` (default false). When false, LOC/ZONE validation uses format checks instead of code-list membership.
97. **LRU eviction for auto-tagger** — `Queue<long>`-based LRU eviction replacing `Clear()` at 10K entries.
98. **WriteTag7All warning dedup** — Fixed guard to prevent duplicate warning accumulation on repeated overwrites.
99. **MapBuiltIn zero-value fix** — Removed `val == "0"` filter so valid zero values are no longer silently dropped.
100. **_paramCache invalidation** — `ClearParamCache()` called after LoadSharedParams, SyncParameterSchema, and on DocumentClosed.
101. **FromAlpha SEQ parser** — Added `FromAlpha(string)` inverse of `ToAlpha` for Alpha SEQ scheme high-water-mark parsing.
102. **CopyTokensFromNearest wired** — `PopulateAll` calls `CopyTokensFromNearest` for SYS/FUNC when MEP detection yields empty/generic defaults.
103. **Formula cycle detection** — Kahn's algorithm topological sort in `FormulaEngine.LoadFormulas`.
104. **AutoTagger context caching** — `PopulationContext` and `TagIndex` cached across IUpdater triggers with `_contextInvalid` flag.
105. **GetSolidFillPattern cached** — `Dictionary<int, ElementId>` cache keyed by `doc.GetHashCode()`.
106. **ResolveAllIssues batched** — Refactored to 500-element batches with `StingProgressDialog` and cancellation support.
107. **ValidationError typed enum** — `ValidationErrorType` enum and `ValidationError` class replace string pattern matching.
108. **ComplianceScan split metrics** — `StatusBarText` shows both `StrictPercent` and `CompliancePercent`. `RAGStatus` uses weighted tag + revision compliance.
109. **Visual tag visibility check** — `BoundingBox(view)` null check before `IndependentTag.Create` in auto-tagger.
110. **Linked model manifest export** — `ExportLinkedModelManifestCommand` derives tokens and exports `_LINKED_TOKENS.json` sidecar file.
111. **SEQ migration guard** — `ConfigEditorCommand` snapshots SEQ settings before edits, warns if changed with existing tags.

#### Completed (Phase 13 — Tag Studio, 16-Position Pipeline & Full Automation)

112. **16-position tag placement** — `InjectPositionTypes()` expanded to 16 FamilyType entries aligned to ring 1 (cardinal/diagonal) + ring 2 (far).
113. **Tag Studio WPF tab** — 9th panel tab with 6 sub-tabs: Placement/Leader/Style/Tokens/Tools/Scale.
114. **Tag Studio 16-position compass** — 4x4 RadioButton grid for P1-P16 with directional override.
115. **Tag Studio collision weights** — Sliders for overlap penalty, proximity, preferred bonus, align bonus, crop edge.
116. **Tag Studio leader/elbow controls** — Auto/Always/Never/Smart modes + Straight/45/90/Free elbows.
117. **AdjustElbowsCommand** — New command for elbow type control via `tag.SetLeaderElbow`.
118. **SetArrowheadStyleCommand** — ObjectStyles annotation arrowhead control.
119. **TAG_SEG_MASK_TXT** — Per-element token segment visibility mask (8-char "10110101" format).
120. **BuildDisplayTag()** — 5 display modes wired to `ASS_DISPLAY_TXT`.
121. **BuildSeqKey() helper** — Normalises all SEQ counter keys to `{disc}_{sys}_{func}_{prod}` format.
122. **5-tier scale rules** — `GetModelOffset()` with configurable offset cap per scale tier (1:50/100/200/500+).
123. **ComplianceScan revision tracking** — `RevisionComplete`, `RevisionMissing`, `RevisionPercent`, `RevisionDistribution` added. RAG status uses weighted 70% tag + 30% revision.

#### Completed (Phase 14 — Excel Link, Platform Integration, Revision Management)

155. **Bidirectional Excel link** — `ExcelLinkCommands.cs` (2,055 lines, 6 commands): ExportToExcel (30+ column export with tags, identity, spatial, MEP data), ImportFromExcel (validation, audit trail, change preview), ExcelRoundTrip (one-click export→edit→import), ExportSchedulesToExcel, ImportSchedulesFromExcel, ExportTemplate. `ExcelLinkEngine` with ChangeRecord tracking and ValidationWarning collection.
156. **Platform integration** — `PlatformLinkCommands.cs` (1,598 lines, 6 commands): ACCPublish (ACC/BIM 360 packaging), CDEPackage (ISO 19650 CDE folder structure), BCFExport (BCF 2.1 with viewpoints), BCFImport (with dedup detection), PlatformSync (bidirectional delta sync), SharePointExport (corporate SharePoint/Teams). `PlatformLinkEngine` with ISO 19650 file naming validator and deliverable collector.
157. **Revision management** — `RevisionManagementCommands.cs` (1,590 lines, 12 commands): CreateRevision (ISO 19650 naming), RevisionDashboard, AutoRevisionCloud, RevisionSchedule, TrackElementRevisions (tag snapshot), RevisionCompare (change deltas), IssueSheetsForRevision, RevisionNamingEnforce, RevisionTagIntegration (auto-stamp on tag changes), RevisionExport, BulkRevisionStamp, AutoRevisionOnTagChange. `RevisionEngine` with snapshot management and revision sequence tracking.
158. **Centralized output management** — `OutputLocationHelper.cs` (222 lines): 4-level fallback chain for export paths (preferred → project → documents → temp), timestamped path generation, config persistence via project_config.json.
159. **WPF list picker dialog** — `StingListPicker.cs` (323 lines): Reusable searchable list picker replacing paginated TaskDialogs, supports 100+ items with instant filtering, single/multi-select modes, corporate styling.
160. **Stage compliance gate** — `StageComplianceGateCommand`: RIBA stage-aware compliance assessment with stage-specific tag completeness thresholds.
161. **BEP enrichment enhanced** — ComplianceScan-based BEP auto-enrichment with per-discipline breakdown, stage-gated compliance, and BEP allowed code cross-validation.
162. **COBie Component enriched** — AssetType mapping from discipline codes, phase-derived installation dates, expanded fields (Category, Discipline, Location, Zone, Level, System, Function, ProductCode, SequenceNumber, Status).
163. **LoadSharedParams crash-proofed** — Group-per-transaction binding, targeted category filtering, crash recovery with parameter file restoration, 6 additional crash vector fixes.

#### Completed (Phase 15 — Deep Gap Analysis Fix)

164. **Unified tagging pipeline** — `TagPipelineHelper.RunFullPipeline()` centralises per-element pipeline (TagHistory → TypeTokenInherit → PopulateAll → CategoryForceSys → NativeParamMapper → FormulaEngine → BuildAndWriteTag → WriteContainers → WriteTag7All → GetGridRef). All 7 tagging callers (AutoTag, TagNewOnly, BatchTag, TagAndCombine, RetagStale, StingAutoTagger, FullAutoPopulate) use the same pipeline.
165. **SEQ sidecar persistence** — `TagConfig.SaveSeqSidecar()` / `LoadSeqSidecar()` / `MergeSeqSidecar()` persist sequence counters to `.sting_seq.json` alongside the `.rvt` file. Merged via max-per-key strategy in `BuildTagIndexAndCounters()`.
166. **Tag config extensions** — TAG_PREFIX, TAG_SUFFIX, CATEGORY_SKIP, CATEGORY_FORCE_SYS loaded from `project_config.json`. Prefix/suffix applied in both `BuildAndWriteTag` and `BuildTagsCommand`.
167. **Project-adjacent config loading** — `OnDocumentOpened` prefers `project_config.json` next to the `.rvt` file over the plugin data directory, preventing config bleed between projects.
168. **Delta sync expansion** — `TagChangedCommand` now detects 6 token types (LVL/LOC/ZONE + SYS/FUNC/PROD) for comprehensive staleness detection.
169. **Adaptive workflow conditions** — `WorkflowEngine` supports `maxCompliancePct`, `minCompliancePct`, and `requiresStaleElements` for conditional step execution. 8 new command tag mappings added to `ResolveCommand()`.
170. **Enhanced DailyQA workflow** — `WORKFLOW_DailyQA_Enhanced.json` expanded to 11 steps with conditional fields for adaptive execution.
171. **Compliance scan expansion** — `ComplianceScan.ComplianceResult` tracks `StatusMissing`, `ContainersMissing`, and `DataCompletenessPercent` (weighted across tags/STATUS/containers).
172. **Type token inheritance** — `TokenAutoPopulator.TypeTokenInherit()` copies DISC/SYS/FUNC/PROD from family type to instance elements.
173. **Grid reference auto-detect** — `SpatialAutoDetect.GetGridRef()` finds nearest X/Y grid intersection and writes to ASS_GRID_REF_TXT.
174. **Auto-tagger stability** — `StingAutoTagger.InvalidateContext()` clears `_recentlyProcessed` cache to prevent stale skip bugs.

#### Completed (Phase 15b — Workflow Efficiency Review)

175. **SEQ sidecar completeness** — Added `SaveSeqSidecar` after `tx.Commit()` in `TagFormatMigrationCommand` and `TagChangedCommand`, preventing SEQ counter loss on session reload.
176. **Dead counter cleanup** — Removed unincremented `populated`, `statusDetected`, `revSet`, `locDetected`, `zoneDetected`, `combined` variables from AutoTag, TagNewOnly, BatchTag, and TagAndCombine commands that always reported zeros. Replaced with `TaggingStats.BuildReport()` for accurate stats.
177. **Null-safe population context** — Added `PopulationContext.Build(doc)` null checks in all 5 tagging commands (AutoTag, TagNewOnly, BatchTag, TagAndCombine, RetagStale) preventing null reference crashes on corrupt documents.
178. **MSB3277 suppression** — Suppressed benign ClosedXML transitive dependency assembly version warnings in `StingTools.csproj`.

#### Completed (Phases 16-20 — Bug Fixes, Logic Fixes, Enhancements, New Features)

179. **Branch consolidation** — Merged PR #32 and PR #33 into unified branch, resolving 5 merge conflicts.
180. **Duplicate definition fixes** — Removed duplicate constants and methods in `ParamRegistry.cs` and `TagConfig.cs`.
181. **16 build error resolution** — Fixed missing variables, properties, and method references across core files.
182. **BUG-01 through BUG-06** — Fixed 6 critical build/runtime bugs across core files.
183. **LOG-01 through LOG-13** — SYS detection layer tracking, formula cache, ComplianceScan torn-read fix, TAG7 rebuild gating, TransactionGroup rollback, separator history, DisplayModeDefault, temp fallback warning, WorkflowEngine JSONL log rotation.
184. **TW-01 through TW-03** — Placement tab restructure, configurable SEQ pad width, tag prefix/suffix properties.
185. **DATA-01/DATA-03** — Schema version headers on CSV files, unit conversion for formula evaluation.
186. **UI-01/UI-03** — ThemeManager (Dark/Light/Grey/Corporate), Tags status strip.
187. **BIM-02/BIM-03** — Stage compliance gate, COBie duration normalisation.
188. **ORF-01 through ORF-06** — 18 new parameter constants with GUIDs for operational readiness.
189. **Type-level LOC/ZONE overrides** — `PopulateAll` checks type-level LOC/ZONE before spatial auto-detect.
190. **ConnectorInherit** — MEP token inheritance from connected elements via connector graph traversal.
191. **NF-01: Tag3DCommand** — Tags elements in 3D views with spatial auto-detect.
192. **NF-02: RepairDuplicateSeqCommand** — Smart duplicate SEQ repair with spatial proximity analysis.
193. **ENH-03: Leader elbow path avoidance** — `AdjustLeaderElbow()` shifts leader elbows to avoid overlapping placed tags.

#### Completed (Phase 21 — Gap Analysis v3 Pipeline Fixes)

194. **StingAutoTagger enhanced logging** — Context rebuild now logs formula and grid line counts for diagnostics.
195. **Stale debounce timer** — 500ms time-based throttle in `OnDocumentChanged` prevents thundering-herd stale-mark transactions during bulk operations.
196. **SheetRemovePrefix/Suffix** — `SheetRemovePrefixOrSuffix` method operates on multi-sheet selection; XAML buttons added to DOCS tab.
197. **WriteContainers pipeline consistency** — Added `ParamRegistry.WriteContainers()` after `WriteTag7All` in TagSelected, ReTag, TagFormatMigration, TagChanged, BulkParamWrite retag, and SystemParamPush (both locations).
198. **LoadDefaults SEQ resets** — `CurrentSeqScheme`, `SeqIncludeZone`, `SeqLevelReset`, `_seqSchemeChanged`, `_seqSchemeWarned`, `_activePresetName` reset in `LoadDefaults()` to prevent cross-project bleed.
199. **FullAutoPopulate pipeline refactor** — Delegates to `TagPipelineHelper.RunFullPipeline()` with `LoadFormulas()`/`LoadGridLines()` for canonical pipeline consistency.
200. **Post-tag compliance gate** — `ComplianceGatePct` loaded from `COMPLIANCE_GATE_PCT` config key; `CheckComplianceGate()` called after AutoTag, TagNewOnly, BatchTag, TagAndCombine.
201. **Tag history audit trail** — `ASS_TAG_PREV_TXT` + `ASS_TAG_MODIFIED_DT` written at start of `RunFullPipeline`; parameters added to `MR_PARAMETERS.csv`.
202. **SeparatorHistory persistence** — `SEPARATOR_HISTORY` key in `project_config.json`; loaded/saved/reset. Old separator tracked before override in `ApplyTagFormatOverrides()`.
203. **AUTO_RUN_WORKFLOW_ON_OPEN** — Config key logged on `DocumentOpened` for workflow automation awareness.

#### Completed (Phase 22 — Efficiency & Automation Enhancements)

204. **Tag3DCommand FindTagFamily fix** — Removed memory-leaking temporary `FamilyInstance` creation; now checks family name directly on `FamilySymbol` without instantiation.
205. **Dead code removal** — Removed unused `GetNearestGridRef()` method from `ScheduleCommands.cs` (superseded by `SpatialAutoDetect.GetGridRef` in unified pipeline).
206. **TagFormatMigration single-pass** — Eliminated double-read of `ReadTokenValues` in preview; merged sample display and change count into single loop.
207. **SelectStaleElementsCommand** — New command: selects elements with stale tags where LVL/SYS/PROD no longer match current context. Enables targeted re-tagging of only moved/changed elements.
208. **QuickTagPreviewCommand** — New command: shows predicted tag for selected elements in read-only mode without making changes. Displays current vs predicted tag, gap count, and format settings.
209. **ContainerPreCheckCommand** — New command: verifies all container parameters are bound and writable before running Combine Parameters. Reports per-group status, unbound parameters, and read-only fields.
210. **TAG tab enhanced** — Added Select Stale, Container Check, and Quick Tag Preview buttons to CREATE tab QA and TOKEN INSPECTOR sections.

#### Completed (Phase 23 — v4 Gap Analysis Merge Fixes)

211. **Tag3DCommand full pipeline** — Enhanced with RunFullPipeline on source elements, WriteContainers + WriteTag7All + NativeMapper on placed 3D tag instances, LoadTagFamilyFromConfig from project_config.json, GetElementCenter helper, and RepairDuplicateSeqCommand with full container writes.
212. **ComplianceScan EmptyTokenCounts** — Per-token empty/placeholder count dictionary in ComplianceResult for granular compliance reporting (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ).
213. **CATEGORY_TOKEN_OVERRIDES** — Full per-category token overrides from project_config.json, applied in RunFullPipeline after PopulateAll. Supports SKIP flag to exclude categories entirely.
214. **Token lock infrastructure** — ASS_TOKEN_LOCK_TXT parameter registered in MR_PARAMETERS.csv and read in RunFullPipeline to skip locked tokens during population.
215. **PreviewTagCommand** — Dry-run tag preview in StingCommandHandler: runs full pipeline in rolled-back transaction, shows predicted tag + token breakdown. XAML button added to TEMP tab.
216. **Config schema validation** — TagConfig.LoadFromFile warns on unknown config keys via knownKeys HashSet to catch typos.
217. **ResolveAllIssues expanded** — TypeTokenInherit before PopulateAll and NativeParamMapper.MapAll per element.
218. **FamilyStagePopulate + BulkAutoPopulate NativeMapper** — Added NativeParamMapper.MapAll after token population in both commands.
219. **FullAutoPopulate SEQ sidecar** — SaveSeqSidecar after tx.Commit for sequence continuity across sessions.

#### Completed (Phase 24 — Tagging Workflow Gap Fix & Cross-Check)

220. **FIX-01: TagSelectedCommand SEQ sidecar** — Added `SaveSeqSidecar` + `StingAutoTagger.InvalidateContext()` after `tx.Commit()` to prevent counter drift between sessions.
221. **FIX-02: AutoTagQueueHandler pipeline gaps** — Added `NativeParamMapper.MapAll` + inline formula evaluation (matching `TagPipelineHelper.RunFullPipeline` pattern) + `SaveSeqSidecar` after commit. Fixed undeclared `enqueued` variable (CS0103). Context rebuild now also reloads `_formulas` and `_gridLines`.
222. **FIX-03: SystemParamPush completeness** — (A) Added `SaveSeqSidecar` + `ComplianceScan.InvalidateCache` + `StingAutoTagger.InvalidateContext` after `BatchSystemPushCommand` commit. (B) Added `NativeParamMapper.MapAll` after token writes in `ExecutePush`.
223. **FIX-04: TagChangedCommand NativeMapper** — Added `NativeParamMapper.MapAll` after delta token update in the stale element loop.
224. **FIX-05: RepairDuplicateSeq pre-enrichment** — Added `TypeTokenInherit` + `PopulateAll` + `NativeParamMapper.MapAll` before `BuildAndWriteTag` to ensure spatial/system data is current before SEQ reassignment. Added `SaveSeqSidecar` + `InvalidateContext` after commit.
225. **FIX-06: ViewActivated handler** — Added `application.ViewActivated += OnViewActivated` to detect document switches and invalidate auto-tagger cache + compliance scan. Prevents stale context when users switch between open documents.
226. **FIX-07: StingStaleMarker LOC/ZONE** — Extended stale detection from LVL-only to LVL + LOC + ZONE. Uses `SpatialAutoDetect.DetectLoc` / `DetectZone` to compare stored vs current spatial values.
227. **FIX-08: TagNewOnlyCommand scope** — Added scope selection dialog (Active view only / Entire project). Uses `FilteredElementCollector(doc, doc.ActiveView.Id)` for view-scoped collection.
228. **FIX-09: FamilyStagePopulate formulas** — Added formula evaluation after `NativeParamMapper.MapAll` using the same inline pattern as `TagPipelineHelper.RunFullPipeline`.
229. **FIX-10: ExcelLinkImport NativeMapper** — Added `NativeParamMapper.MapAll` in both `ImportFromExcel` and `ExcelRoundTrip` tag rebuild loops before `BuildAndWriteTag`.
230. **FIX-11: Tag3DCommand target fix** — Changed `WriteContainers` / `WriteTag7All` / `NativeParamMapper.MapAll` to target source element (`el`) instead of placed 3D tag instance (`fi`). The source element holds actual tag tokens; `fi` is just a visual marker.
231. **FIX-12: ComplianceScan StaleCount** — Added `StaleCount` property to `ComplianceResult`, scanning for `STING_STALE_BOOL = 1`. Included in `StatusBarText` when > 0.
232. **FIX-13: InvalidateContext coverage** — Added `StingAutoTagger.InvalidateContext()` after `ComplianceScan.InvalidateCache()` in: `TagAndCombineCommand`, `BatchTagCommand`, `ResolveAllIssuesCommand` (both cancelled and normal paths), `AutoTagCommand`, `TagNewOnlyCommand`, `ReTagCommand`, `FixDuplicateTagsCommand`, `DeleteTagsCommand`, `CopyTagsCommand`, `SwapTagsCommand`, `RetagStaleCommand`, `FullAutoPopulateCommand`.
233. **FIX-14: TagFormatMigration caches** — Added `ComplianceScan.InvalidateCache()` + `StingAutoTagger.InvalidateContext()` after commit. Added `NativeParamMapper.MapAll` before tag rebuild.
234. **FIX-15: Duplicate subscription** — Removed duplicate `application.ControlledApplication.DocumentOpened += OnDocumentOpened` (was subscribed twice: BUG-05 and ENH-06).
235. **Phase 2 verification: BulkRetag gaps** — Added `NativeParamMapper.MapAll` + `SaveSeqSidecar` + `ComplianceScan.InvalidateCache` + `StingAutoTagger.InvalidateContext` to `BulkRetag` in `StateSelectCommands.cs`.
236. **Phase 2 verification: ResolveAllIssues SEQ** — Added `SaveSeqSidecar` after all batch commits in `ResolveAllIssuesCommand`.

#### Completed (Phase 25 — Pipeline Unification & Deep Review)

237. **Build error fixes** — Removed 4 duplicate member definitions (CS0111/CS0102) from incomplete merge conflict resolution: `TypeTokenInherit` in ParameterHelpers.cs, `ConvertToInternalUnits` in FormulaEvaluatorCommand.cs, `SeparatorHistory` in TagConfig.cs, `BuildDisplayTag` in TagConfig.cs.
238. **GAP-01: CombineParametersCommand cache invalidation** — Added `ComplianceScan.InvalidateCache()` + `StingAutoTagger.InvalidateContext()` after `tx.Commit()` to ensure compliance dashboard and auto-tagger reflect container changes.
239. **GAP-02: ValidateTagsCommand STATUS/REV check** — Added STATUS and REV population as required criteria for `fullyValid` count. Elements missing STATUS or REV are no longer counted as 100% compliant.
240. **GAP-03: ResolveAllIssuesCommand pipeline unification** — Replaced manual 7-step pipeline (TypeTokenInherit → PopulateAll → ISO validation → NativeMapper → BuildAndWriteTag → WriteTag7All → WriteContainers) with `TagPipelineHelper.RunFullPipeline()`. Now executes all 11 canonical steps including TokenLock (FE-01), CategoryForceSys, CategoryTokenOverrides (FE-06), FormulaEngine, GridRef, and AuditTrail (AL-06). Retained post-pipeline ISO cross-validation fix as a secondary cleanup pass.
241. **GAP-04: BulkRetag pipeline unification** — Replaced manual 4-step pipeline (NativeMapper → BuildAndWriteTag → WriteTag7All → WriteContainers) with `TagPipelineHelper.RunFullPipeline()`. Now executes all 11 canonical steps including TypeTokenInherit, PopulateAll, TokenLock, FormulaEngine, GridRef, and AuditTrail — previously missing entirely from BulkRetag.

#### Completed (Phase 26 — Build Error Fixes & Pipeline Convergence)

242. **18 build errors fixed** — CS7036 (OutputLocationHelper JToken.Value), CS0030+CS1061 (CombineParametersCommand ContainerParamDef/GroupDef types), CS0103 (TagConfig TaskDialog, ParagraphDepthCommand StingCommandHandler namespace), CS0117 (ParameterHelpers invalid BuiltInParameter constants), CS0029+CS1503 (StingAutoTagger Dictionary type mismatch), CS0128 (LoadSharedParamsCommand duplicate iter), CS0122 (Tag3DCommand StructuralType accessibility), CS0103 (SystemParamPushCommand seqCounters scope), CS0103 (ParameterHelpers SetIfEmpty/GetString unqualified calls in NativeParamMapper).
243. **MR_PARAMETERS.txt structure fix** — Moved GROUP 18 (Warning Thresholds) from mid-file `*GROUP` header syntax inside PARAM section to proper GROUP section before `*PARAM` header. Validated against reference file (StingD85/transfer).
244. **GAP-AQ: AutoTagQueueHandler pipeline unification** — Replaced 80-line inline pipeline (missing CategorySkipList, CategoryForceSys, CategoryTokenOverrides, TokenLock, AuditTrail; NativeMapper in wrong order; GridRef result discarded) with single `TagPipelineHelper.RunFullPipeline()` call. Now executes all 11 canonical steps in correct order.
245. **GAP-BA: BulkAutoPopulate enhancement** — Added TypeTokenInherit before PopulateAll, formula evaluation after NativeMapper, and ComplianceScan.InvalidateCache + StingAutoTagger.InvalidateContext after commit.
246. **GAP-FS: FamilyStagePopulate TypeTokenInherit** — Added `TokenAutoPopulator.TypeTokenInherit()` before `PopulateAll()` so type-level DISC/SYS/FUNC/PROD values are inherited to instances.
247. **Double-write elimination** — Removed redundant TAG7 + WriteContainers calls after RunFullPipeline in TagSelectedCommand and ReTagCommand (RunFullPipeline already handles both steps).
248. **Thread safety: StingStaleMarker** — Changed `_elementVersionHash` from `Dictionary<long, string>` to `ConcurrentDictionary<long, string>` to prevent race conditions in `OnDocumentChanged` event handler.

#### Completed (Phase 27 — SEQ Sidecar, Cache Invalidation & TAG_PREFIX/SUFFIX Consistency)

249. **BuildTagsCommand SEQ sidecar + cache** — Added `TagConfig.SaveSeqSidecar()` + `ComplianceScan.InvalidateCache()` + `StingAutoTagger.InvalidateContext()` after tx.Commit() so sequence counters persist between sessions and dashboards reflect changes.
250. **AssignNumbersCommand SEQ sidecar + cache** — Added sidecar save and cache invalidation after sequence assignment.
251. **TokenWriter.WriteToken cache invalidation** — Added `ComplianceScan.InvalidateCache()` + `StingAutoTagger.InvalidateContext()` after all manual token writes (SetDisc, SetLoc, SetZone, SetStatus, etc.) so live dashboard/auto-tagger reflect changes immediately.
252. **RenumberTagsCommand SEQ sidecar + PREFIX/SUFFIX** — Added SEQ sidecar save, cache invalidation, and TAG_PREFIX/TAG_SUFFIX to both initial tag assembly and collision resolution loop.
253. **FixDuplicateTagsCommand SEQ sidecar + PREFIX/SUFFIX** — Added SEQ sidecar save after duplicate fix and TAG_PREFIX/TAG_SUFFIX to new tag assembly in collision loop.
254. **CopyTagsCommand PREFIX/SUFFIX** — Added TAG_PREFIX/TAG_SUFFIX to rebuilt TAG1 so copied tags match project tag format.
255. **StingCommandHandler dispatch wiring** — Added missing button dispatch cases: SetSeqScheme → `SetSeqSchemeCommand`, MapSheets → `MapSheetsCommand`, RetagStale → `RetagStaleCommand`, ComplianceScan → `CompletenessDashboardCommand`.

#### Completed (Phase 28 — GAP FIX IMPLEMENTATION v3: 40+ Fixes Across 18 Files)

256. **FIX-CRIT01 A-F: GridRef result capture** — Fixed 6 locations where `SpatialAutoDetect.GetGridRef()` was called without capturing the return value. All now assign to `string gridRef` and write via `SetIfEmpty`. Files: BatchTagCommand.cs (TagFormatMigration, TagChanged), RepairDuplicateSeqCommand.cs, SystemParamPushCommand.cs, ExcelLinkCommands.cs (Import, RoundTrip).
257. **FIX-V01: TagFormatMigration scope dialog** — Added 3-scope dialog (active view / selected elements / entire project) instead of silent project-wide scan. Adds TypeTokenInherit → PopulateAll → NativeMapper → FormulaEngine before tag rebuild so stale tokens are corrected, not just reformatted.
258. **FIX-NEW02: ExcelLink TypeTokenInherit** — Added `TypeTokenInherit` before `NativeParamMapper.MapAll` in both `ImportFromExcel` and `ExcelRoundTrip` tag rebuild paths.
259. **FIX-DEEP01: Locked token enforcement** — `ASS_TOKEN_LOCK_TXT` values snapshot before TypeTokenInherit/PopulateAll/CategoryForceSys/CategoryTokenOverrides and restored afterward in `RunFullPipeline`, preventing pipeline overrides from changing user-locked tokens.
260. **FIX-DEEP02: WorkflowEngine cache pairing** — `StingAutoTagger.InvalidateContext()` now paired with `ComplianceScan.InvalidateCache()` after workflow chain completes.
261. **FIX-DEEP03: CopyTagsCommand SEQ persistence** — `SaveSeqSidecar` after tag copy so rebuilt TAG1 values are reflected in sequence counters.
262. **FIX-DEEP06: FullAutoPopulate API filtering** — `ElementMulticategoryFilter` applied to `FilteredElementCollector` using `SharedParamGuids.AllCategoryEnums`, reducing element iteration on large models.
263. **FIX-DEEP07: RenumberTags canonical counters** — Uses `BuildTagIndexAndCounters` (canonical, merges sidecar data) instead of `GetExistingSequenceCounters`, preventing counter divergence.
264. **FIX-UI01: 12 missing dispatch entries** — ClusterTags, DeclusterTags, SetDisplayMode, SetViewTagStyle, AlignTagBands, BatchPlaceLinkedTags, ExportLinkedManifest, FamilyParamCreator, DiscComplianceReport, AutoTagVisual, AutoTaggerConfig, ListWorkflowPresets wired to command classes.
265. **FIX-UI02: 5 TagStudio AI stubs** — TagStudioAPIGaps, TagStudioExplain, TagStudioPipeline, TagStudioGenerate, TagStudioGapReview dispatch entries with informational messages.
266. **FIX-UI03: Tag Studio freeze fix** — `NotifyCommandComplete()` static method on `StingDockPanel` called from `StingCommandHandler.Execute()` finally block, ensuring `UnfreezeTagSubTabs()` runs after every command.
267. **FIX-UI04: ResolveAllIssues progress UX** — `StingProgressDialog.Show()` moved before `SmartSortElements()` with status messages during sort and context build phases.
268. **FIX-B01: BuildTagsCommand → RunFullPipeline** — Replaced 130+ line inline pipeline with single `TagPipelineHelper.RunFullPipeline()` call for all 11 canonical steps.
269. **FIX-B02: FixDuplicates BuildSeqKey** — Replaced inline `$"{disc}_{sys}_{lvl}"` with `TagConfig.BuildSeqKey(disc, sys, func, prod, lvl, zone)` for canonical key format. Added string-parameter overload to TagConfig.
270. **FIX-B04: ClusterTags member positions** — Added `CLUSTER_MEMBER_POS` constant to ParamRegistry. ClusterTagsCommand now stores member bounding box centers as pipe-delimited string for decluster position restoration.
271. **FIX-B05: MEP short-circuit in PopulateAll** — Added `_mepConnectorCategories` HashSet (28 MEP categories). Non-MEP elements skip expensive `ConnectorInherit()` and 6-layer `GetMepSystemAwareSysCodeWithLayer()`, using direct category fallback instead.
272. **FIX-B06: ComplianceScan full container check** — Removed `Math.Min(3, containers.Length)` limit so ALL applicable containers are checked for accurate compliance reporting.
273. **FIX-B07: AlignDirection skip-dialog** — Split AlignTagsH/V/Stack dispatch to set `ExtraParam("AlignDirection")` before RunCommand. AlignTagsCommand reads ExtraParam and skips TaskDialog when direction is pre-set.
274. **FIX-B08: ResolveToAnnotationTags bridge** — Added `LeaderHelper.ResolveToAnnotationTags()` method that converts host element selection to `IndependentTag` annotations via reverse lookup in the active view.
275. **FIX-B09: CheckComplianceGate coverage** — Added `TagConfig.CheckComplianceGate()` calls after ResolveAllIssuesCommand, Tag3DCommand, and WorkflowEngine chain completion (already present in AutoTag, TagNewOnly, BatchTag, TagAndCombine).
276. **FIX-B10: AutoTagger state persistence** — Auto-tagger enabled/visual/stale-marker state persisted to `project_config.json` via `PersistAutoTaggerConfig()`. State restored on DocumentOpened from TagConfig loaded values.
277. **FIX-B13: StingListPicker window owner** — Added `WindowInteropHelper` owner assignment using `Process.GetCurrentProcess().MainWindowHandle` for correct modality.
278. **FIX-C01: SelectionScope reset** — Added `SelectionScopeHelper.SetScope(false)` in `OnDocumentOpened` to prevent stale project-wide scope from carrying over between documents.
279. **FIX-C02: CopyTags SEQ via BuildAndWriteTag** — Replaced manual tag concatenation with `TagConfig.BuildAndWriteTag()` for proper SEQ collision detection via AutoIncrement mode.
280. **FIX-C03: RenumberTags spatial sort** — Elements within each `(DISC, SYS, LVL)` group now sorted spatially (by LVL, then X, then Y) before renumbering for deterministic SEQ assignments.
281. **FIX-C04: BatchRenameViews custom find/replace** — Added mode 5 ("Custom find/replace") with WPF input dialog for find and replace strings.

#### Completed (Phase 28 — STING_FINAL_PROMPT: Crash Fixes, Theme System, Pipeline Completion & New Commands)

224. **Bulk null-ref crash fix** — Replaced all 105 occurrences of `commandData.Application.ActiveUIDocument` across 15 files (OperationsCommands, ModelCommands, ModelCreationCommands, TagStyleEngineCommands, TagIntelligenceCommands, RevisionManagementCommands, IoTMaintenanceCommands, AutoModelCommands, DWGImportCommands, MEPCreationCommands, MEPScheduleCommands, StandardsEngine, RoomSpaceCommands, DataPipelineEnhancementCommands, NLPCommandProcessor) with `ParameterHelpers.GetContext(commandData)` null-safe pattern.
225. **ExcelLink pipeline completion** — Added `TokenAutoPopulator.TypeTokenInherit()` before `NativeParamMapper.MapAll` in both Import and RoundTrip paths. Fixed `GetGridRef` to capture return value and write to `ASS_GRID_REF_TXT` via `ParameterHelpers.SetIfEmpty`.
226. **Theme DynamicResource system** — Added `ThemeManager.InitialiseResources()` to seed theme resource keys at startup. Converted all hardcoded hex colors in `StingDockPanel.xaml` Page.Resources to `{DynamicResource}` bindings (AccentBrush, BorderColor, ButtonBg, ButtonFg, PanelFg, SecondaryBg, PrimaryBg, HeaderBg, HeaderFg). Theme switching via `CycleTheme()` now works.
227. **Leader/Elbow slider connections** — Added `SetLeaderElbowParams()` and `SetTagStyleParams()` helper methods to `StingDockPanel.xaml.cs` that read 15 slider/radio/combo values and pass as ExtraParams. `AdjustElbowsCommand` now checks ExtraParams before showing dialog.
228. **Per-export folder navigation** — Added `OutputLocationHelper.PromptForExportPath()` with session-level folder memory per export type. Replaced hardcoded Desktop paths in PDF, IFC, COBie, Quantities, Clashes, BatchParams exports. Tag Register export also uses folder navigation.
229. **IoT/Standards/DataPipeline/MEP dispatch** — Wired 30+ new dispatch entries in `StingCommandHandler.cs` for IoT Maintenance (AssetCondition, MaintenanceSchedule, DigitalTwinExport, etc.), Standards (ISO19650Deep, CibseVelocity, BS7671, Uniclass, BS8300, PartL), DataPipeline validation, and MEP Schedule commands. Added XAML buttons in BIM tab.
230. **NLP functional execution** — Replaced stub `NLPCommandProcessorCommand` with functional command browser using `StingListPicker`. Supports Browse All, Quick Commands, and BIM Knowledge Base modes. Executes selected commands via `WorkflowEngine.ResolveCommandPublic()`. Added 20 missing command tags to `ResolveCommand`.
231. **PurgeSharedParamsCommand** — New command in `LoadSharedParamsCommand.cs` with 3 modes: Audit (count bound vs MR file), Purge orphaned (remove params not in MR_PARAMETERS.txt), Purge all STING (remove all ASS_*/STING_* bindings). Dispatch + XAML button added.
232. **FamilyParamCreator folder picker + purge** — Added `PurgeFirst` option to `ProcessOptions`. 4-mode dialog (single/batch × add/purge+inject). Replaced hardcoded DataPath with actual file/folder browser dialogs. Purge step removes existing STING params before fresh injection.
233. **AutoTagger settings persistence** — `SetVisualTagging()` now persists `AUTO_TAGGER_VISUAL` to `project_config.json`. Restored on config load in `TagConfig.LoadFromFile()`.
234. **GuidedDataEditorCommand** — New command for editing STING data files (project_config.json, MR_PARAMETERS.txt, MATERIAL_SCHEMA.json, PARAMETER_REGISTRY.json, LABEL_DEFINITIONS.json, TAG_PLACEMENT_PRESETS_DEFAULT.json, WORKFLOW_DailyQA_Enhanced.json) with system editor launch and sync/reload.
235. **MR_PARAMETERS.txt expansion** — Appended 63 new parameter definitions (1384→1447 PARAM lines) covering ASS_*, BLE_*, COM_*, MEP_*, MNT_*, PER_*, RGL_*, STR_*, VIEW_*, TAG_* groups.
236. **ParamRegistry GUID supplement** — `LoadFromFile()` now supplements `_guidByName` dictionary from MR_PARAMETERS.txt at load time, bridging the gap between PARAMETER_REGISTRY.json (638 params) and MR file (1447+ params).
237. **cost_rates_5d.csv** — Created with 7-column format (Category, MAT_CODE, MAT_DISCIPLINE, Unit_Rate_USD, Unit_Rate_UGX, Unit, Description) covering all Revit categories with STING DISC codes.
238. **New command classes** — StingParamManagerCommand (browse/add/stats shared params), StingMaterialManagerCommand (browse/create/export materials), PrintSheetsCommand (PDF export with scope), MagicRenameCommand (universal rename with prefix/suffix/find-replace/case/numbering), ViewTabColourCommand (discipline view analysis), RibbonPanelStylerCommand (ribbon config info). All dispatch entries wired.

#### Completed (Phase 28 — Module Expansion: FM Handover, MEP, CAD, Standards, Operations)

256. **IPanelCommand interface** — `Core/IPanelCommand.cs` (64 lines): Interface for WPF dockable panel commands with `SafeApp()`, `SafeDoc()`, `SafeUIDoc()` extension methods preventing Revit crashes from ExternalCommandData reflection hacks.
257. **Performance profiling** — `Core/PerformanceTracker.cs` (267 lines): Lightweight per-operation/per-element timing, session aggregation, slowest-element tracking (100-entry LRU), CSV export, thread-safe `ConcurrentDictionary`.
258. **FM/O&M handover export** — `Docs/HandoverExportCommands.cs` (1,316 lines, 5+ commands): COBie 2.4 spreadsheet generation (11 sheets), maintenance schedule (PPM + ASTM E2018), O&M manual, asset health report (0-100 scoring), space handover report.
259. **Revit journal diagnostics** — `Docs/JournalParserCommand.cs` (494 lines): Parse journal files for addin load status, errors, crashes, command timeline, memory usage with CSV export.
260. **Multi-criteria tag selector** — `Select/TagSelectorCommands.cs` (1,119 lines): Select annotation tags by text, size, arrowhead, leader, family, host category, orientation, discipline via 3-page wizard.
261. **NLP command processor** — `Tags/NLPCommandProcessor.cs` (453 lines): Natural language intent recognition mapping queries to STING commands with 50+ patterns and confidence scoring.
262. **Tag intelligence commands** — `Tags/TagIntelligenceCommands.cs` (1,615 lines, 8+ commands): Configurable tag rule engine, deep quality analysis, batch command chains, tag version control, propagation, analytics dashboard, smart suggestion.
263. **Tag style engine commands** — `Tags/TagStyleEngineCommands.cs` (1,870 lines, 7+ commands): Rule-based tag family type switching with 128 style combinations via JSON-driven TAG_STYLE_RULES.json.
264. **DWG-to-BIM automation** — `Temp/AutoModelCommands.cs` (1,462 lines, 2+ commands): Link DWG/DXF with auto-level matching, tracing geometry extraction, batch import with progress.
265. **COBie data management** — `Temp/COBieDataCommands.cs` (1,533 lines, 2+ commands): Browse COBie type map (70+ equipment types), picklists, job templates, spare parts, attribute templates with pagination.
266. **CAD import with layer mapping** — `Temp/DWGImportCommands.cs` (1,612 lines, 2+ commands): Preview layer mappings, 18-category pattern recognition, auto-detect, mapping preview before commit.
267. **Cross-validation pipeline** — `Temp/DataPipelineEnhancementCommands.cs` (645 lines, 5+ commands): Registry vs CSV drift detection, parameter coverage analysis, field remapping validation.
268. **IoT & maintenance** — `Temp/IoTMaintenanceCommands.cs` (745 lines, 4+ commands): Asset condition assessment (ISO 15686), maintenance scheduling, digital twin sync, energy analysis, commissioning.
269. **MEP equipment placement** — `Temp/MEPCreationCommands.cs` (601 lines, 2+ commands): Programmatic MEP creation covering HVAC, electrical, plumbing, fire, conduit, cable tray, data/IT, security, gas, solar, EV.
270. **MEP schedules** — `Temp/MEPScheduleCommands.cs` (705 lines, 7 commands): Panel, fixture, device, equipment, system, takeoff, sizing check schedules with discipline-specific field population.
271. **Programmatic BIM creation** — `Temp/ModelCreationCommands.cs` (980 lines, 5+ commands): Walls (generic/curtain/stacked/compound), floors, ceilings, roofs, doors, windows, columns, beams, stairs, rooms.
272. **Workflow & batch operations** — `Temp/OperationsCommands.cs` (1,005 lines, 5+ commands): Preset sequences (Full Setup, Tag Pipeline, Export Package, QA, MEP Audit), PDF/IFC/COBie export, clash detection.
273. **Room & space management** — `Temp/RoomSpaceCommands.cs` (623 lines, 3+ commands): Room audit (unnamed/unplaced/unbounded/zero-area), department auto-assignment, room schedule with tag integration.
274. **Standards compliance engine** — `Temp/StandardsEngine.cs` (795 lines): ISO 19650, CIBSE velocity limits, BS 7671 electrical circuit protection, Uniclass 2015 classification, BS 8300 accessibility, Part L energy compliance.
275. **COBie reference data files** — 8 new CSV files (COBIE_TYPE_MAP, COBIE_SYSTEM_MAP, COBIE_PICKLISTS, COBIE_ATTRIBUTE_TEMPLATES, COBIE_JOB_TEMPLATES, COBIE_SPARE_PARTS, COBIE_DOCUMENT_TYPES, COBIE_ZONE_TYPES) totalling ~444 rows of structured reference data.
276. **Material lookup database** — `MATERIAL_LOOKUP.csv` (237 rows): Comprehensive material reference with density, thermal, fire rating, acoustic, embodied carbon, cost properties.
277. **Tag style rules** — `TAG_STYLE_RULES.json`: 128 type catalog with discipline presets and top-down rule evaluation for automated tag family type switching.

#### Completed (Phase 29 — Data Alignment, Tie-In Points & Warning Expansion)

278. **MR_PARAMETERS.txt alignment** — 113 missing parameters added from ParamRegistry constants to MR_PARAMETERS.txt (1,447→1,560+ PARAM lines). 13 datatype fixes (INTEGER→TEXT for flag/code parameters that store string values).
279. **ARCH tag config warning expansion** — 56 new warnings added (55→111) across 33 architectural tag families in STING_TAG_CONFIG_v5_0_ARCH.csv.
280. **MEP tag config warning expansion** — 126 new warnings added (57→183) across 51 MEP tag families in STING_TAG_CONFIG_v5_0_MEP.csv, including 6 new tie-in point tag families (#46-#51).
281. **Tie-in point containers** — TAG_CONFIG_v5_0_CONTAINERS.csv expanded with Section 13: 10 tie-in point container parameters + 4 TAG7 containers + 6 tag family definitions.
282. **Tie-in validation rules** — TAG_CONFIG_v5_0_VALIDATION.csv expanded with Section 13: 13 tie-in-specific validation rules.
283. **Tie-in system mappings** — TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv expanded with Section 7: 14 tie-in system mappings.
284. **ParamRegistry constants expansion** — 30 new COBie, asset management, and style constants added to ParamRegistry.cs.
285. **ParameterHelpers enhancement** — Added `SetInt()` method for integer parameter writing. `CommandExecutionContext` class encapsulates null-safe command data access.
286. **Build fixes** — 33+82+4 build errors resolved across merge phases (duplicate definitions, missing references, type mismatches).

#### Completed (Phase 30 — Light Theme System & Merge Consolidation)

287. **Light theme system** — All 4 themes redesigned to match TAGS sub-tabs: Light (white, orange accents), Warm (cream tint, brown header), Cool (blue-grey tint, navy header), Corporate (light grey, slate header). All use light content areas, dark text, subtle borders.
288. **ThemeManager dual-write** — Resources applied to both Page.Resources and Application.Current.Resources for reliable DynamicResource resolution in Revit's hosted WPF.
289. **Tab styling** — TabItem uses DynamicResource for Foreground/Background with selected tab matching content area colour.
290. **Theme toggle** — CycleTheme handled directly in WPF click handler (no ExternalEvent round-trip needed).

#### Completed (Phase 31 — Data Alignment, Command Wiring & UI Completion)

291. **20 parameter name mismatches fixed** — Deep cross-reference audit found 20 WARN_ parameters in tag config CSVs that didn't match MR_PARAMETERS.txt (wrong prefix ASS_→BLE_, typo REDCTION, RISE→RISER, CST_S_REI→STR_REBAR, missing _CO2_M2 segment). All fixed in ARCH/MEP/STR CSVs.
292. **47 missing parameters added to MR_PARAMETERS.txt** — 3 STR_TAG_7_PARA_ (BOLT/WELD/WIRE), 8 validation warnings (tie-in, circuit, velocity), 36 formula input params. Total: 2,307 parameters.
293. **MR_PARAMETERS.csv regenerated** — Rebuilt from MR_PARAMETERS.txt with proper CSV quoting (was 35% incomplete with malformed rows).
294. **2 missing formula params** — RGL_PARKING_SPACES_NR, RGL_PLOT_FAR_NR added for parking/FAR formulas.
295. **Tag config version bump** — All 4 STING_TAG_CONFIG_v5_0 files updated to v5.1 with fix annotations.
296. **111 undispatched commands wired** — All IExternalCommand classes now have dispatch entries in StingCommandHandler.cs: 5 Docs, 13 Select, 11 Tags, 2 Organise, 77 Temp (COBie, DWG, MEP, Standards, IoT, Room, Model, Data).
297. **3 missing XAML buttons added** — PrintSheets "All Sheets" button, MagicRename button, ViewTabColour button in dockable panel.
298. **Empty tag family detection** — VerifyFamilyHasParams() checks existing .rfa files for STING params; empty families from failed runs are deleted and recreated.

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
- [The Building Coder — Tag Extents](https://thebuildingcoder.typepad.com/blog/2022/07/tag-extents-and-lazy-detail-components.html) — Getting accurate tag bounding boxes
- [Revit API — OverrideGraphicSettings](https://www.revitapidocs.com/2025/eb2bd6b6-b7b2-5452-2070-2dbadb9e068a.htm) — Complete graphic override API
- [Revit API — IndependentTag](https://www.revitapidocs.com/2025/e52073e2-9d98-6fb5-eb43-288cf9ed2e28.htm) — Tag creation and positioning API
