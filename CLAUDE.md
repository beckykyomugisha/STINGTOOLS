# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **72 source files** (70 C# + 2 XAML, ~83,841 lines of code) across 10 directories
- **343 `IExternalCommand` classes** (commands) + 1 `IExternalApplication` entry point + 1 `IExternalEventHandler` + 1 `IDockablePaneProvider` + 2 `IUpdater`s
- **25 runtime data files** (CSV, JSON, TXT, XLSX, PY)
- **6 ribbon panels** with 23 pulldown groups + 1 WPF dockable panel (8 tabs) + 1 WPF project setup wizard

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
    ├── Core/                           # Shared infrastructure (9 files, ~10,550 lines)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI + dockable panel registration + ToggleDockPanelCommand + DocumentOpened quality gate
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error) + EscapeChecker utility
    │   ├── ParamRegistry.cs            # Single source of truth for parameter names, GUIDs, containers, bindings (loads from PARAMETER_REGISTRY.json) + stale/cluster/display/position constants
    │   ├── ParameterHelpers.cs         # Parameter read/write + SpatialAutoDetect + NativeParamMapper + TokenAutoPopulator + PhaseAutoDetect + TypeTokenInherit + CopyTokensFromNearest
    │   ├── SharedParamGuids.cs         # Backwards-compatible facade wrapping ParamRegistry (GUID lookups, category bindings)
    │   ├── TagConfig.cs               # ISO 19650 tag lookup tables, tag builder, TagIntelligence, TAG7 narrative builder + SeqScheme variants + BuildDisplayTag
    │   ├── StingAutoTagger.cs          # IUpdater — real-time auto-tagging + visual tag placement + discipline filter + StingStaleMarker IUpdater
    │   ├── WorkflowEngine.cs           # Workflow orchestration — JSON preset command chaining + conditional steps + result persistence + WorkflowTrendCommand
    │   └── ComplianceScan.cs           # Cached compliance scan with per-discipline breakdown (DiscComplianceData)
    │
    ├── Select/                         # Element selection + color commands (3 files, 29 commands)
    │   ├── CategorySelectCommands.cs   # 14 category selectors + SelectAllTaggable + CategorySelector helper
    │   ├── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite
    │   └── ColorCommands.cs            # 5 color-by-parameter commands + ColorHelper (10 palettes, presets, filter gen)
    │
    ├── UI/                             # WPF dockable panel UI + project wizard (5 C# files + 2 XAML, ~9,247 lines)
    │   ├── StingDockPanel.xaml         # WPF markup for 8-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM)
    │   ├── StingDockPanel.xaml.cs      # Code-behind: button dispatch, colour swatches, status bar
    │   ├── StingCommandHandler.cs      # IExternalEventHandler — dispatches 543+ button tags to 325 command classes + inline helpers
    │   ├── StingDockPanelProvider.cs   # IDockablePaneProvider — registers panel with Revit
    │   ├── StingProgressDialog.cs      # Reusable modeless WPF progress window for batch operations (cancel, ETA, progress bar)
    │   ├── ProjectSetupWizard.xaml     # WPF 7-page project setup wizard dialog
    │   └── ProjectSetupWizard.xaml.cs  # Code-behind: presets, validation, discipline config, review summary
    │
    ├── Docs/                           # Documentation commands (8 files, 27 commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   ├── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │   ├── DocAutomationCommands.cs    # DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets
    │   ├── DocAutomationExtCommands.cs # Batch views/sheets/sections/elevations, doc package, scope boxes, templates, drawing register, browser organizer, handover manual
    │   └── ViewAutomationCommands.cs   # DuplicateView, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign
    │
    ├── Tags/                           # Tagging commands (23 files, 111 commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view + TagNewOnly
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagAndCombineCommand.cs     # One-click: populate + tag + combine all
    │   ├── PreTagAuditCommand.cs       # Dry-run audit: predict tags, collisions, ISO violations + auto-fix chain
    │   ├── FamilyStagePopulateCommand.cs # Pre-populate all 7 tokens before tagging
    │   ├── FamilyParamCreatorCommand.cs # Family parameter injection engine + FamilyParamEngine (shared param binding into .rfa files)
    │   ├── CombineParametersCommand.cs # Interactive multi-container combine + CombinePreFlight
    │   ├── ConfigEditorCommand.cs      # View/edit/save project_config.json
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   ├── TokenWriterCommands.cs      # SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers,
    │   │                               #   BuildTags, CompletenessDashboard, SetSeqScheme + TokenWriter helper
    │   ├── ValidateTagsCommand.cs      # Validate tag completeness with ISO 19650 codes
    │   ├── SmartTagPlacementCommand.cs # 13 smart annotation commands + TagPlacementEngine (collision avoidance, templates, linked views, band alignment, position export)
    │   ├── TagFamilyCreatorCommand.cs  # 4 tag family commands: Create, Load, Configure Labels, Audit + TagFamilyConfig
    │   ├── SyncParameterSchemaCommand.cs # 3 schema commands: Sync, AddParamRemap, Audit + ParamRegistry propagation
    │   ├── LegendBuilderCommands.cs    # 31 legend commands: discipline/system/tag/color/material/equipment/fire rating legends + LegendEngine
    │   ├── RichTagDisplayCommands.cs   # 6 rich display commands: RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments
    │   ├── SystemParamPushCommand.cs   # 3 MEP system push commands: SystemParamPush, BatchSystemPush, SelectSystemElements
    │   ├── ResolveAllIssuesCommand.cs  # 1 one-click ISO 19650 compliance resolution
    │   ├── PresentationModeCommand.cs  # 4 presentation commands: SetMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle
    │   ├── ParagraphDepthCommand.cs    # 2 commands: SetParagraphDepth, ToggleWarningVisibility
    │   ├── TagStyleCommands.cs         # 10 tag style commands: ApplyTagStyle, ApplyColorScheme, ClearColorScheme, SetParagraphDepthExt, TagStyleReport, SwitchTagStyleByDisc, BatchApplyColorScheme, ColorByVariable, SetBoxColor, SetViewTagStyle
    │   └── TagStyleEngine.cs           # Tag style engine: style presets, color schemes, paragraph depth control (128 style combinations)
    │
    ├── Organise/                       # Tag management commands (1 file, 47 commands)
    │   └── TagOperationCommands.cs     # Tag Ops (7), Leaders (14), Analysis (7), Annotation Color (5), Tag Appearance (5), Tag Type (1), AnomalyAutoFix (1), Clustering (2), DisplayMode (1), DiscCompliance (1), RetagStale (1), DeclusterTags (1) + LeaderHelper + AnnotationColorHelper
    │
    ├── BIMManager/                     # ISO 19650 BIM management + 4D/5D scheduling (2 files, 47 commands)
    │   ├── BIMManagerCommands.cs       # 35 commands: BEP (Create/Update/Export/Generate), Issues (Raise/Dashboard/Update/SelectElements), Documents (Register/Add/ValidateNaming/Briefcase), COBie export, Transmittals, CDE status, Reviews, ISO reference, BulkExport, StickyNotes (Create/Export/Select), ModelHealth (Dashboard/Export), MidpTracker, Export4DTimeline, Export5DCostData, FullComplianceDashboard, LinkPredecessors, AssignPhaseDates, MeasuredQuantities, ElementCountSummary + BIMManagerEngine
    │   └── SchedulingCommands.cs       # 12 commands: AutoSchedule4D, ImportMSProject, ViewTimeline4D, ExportSchedule4D, AutoCost5D, ImportCostRates, CostReport5D, CashFlow5D, PhaseFilter, PhaseSummary, MilestoneRegister, WorkingCalendar + Scheduling4DEngine
    │
    ├── Model/                          # Auto-modeling engine (3 files, 16 commands)
    │   ├── ModelCommands.cs            # 16 model commands: Wall, Room, Floor, Ceiling, Roof, Door, Window, Column, ColumnGrid, Beam, Duct, Pipe, Fixture, BuildingShell, DWGToModel, DWGPreview
    │   ├── ModelEngine.cs              # Model creation engine: walls, floors, roofs, columns, beams, MEP, rooms, building shell + FamilyResolver
    │   └── CADToModelEngine.cs         # DWG-to-BIM conversion engine: layer mapping, geometry extraction, element auto-detection
    │
    ├── Temp/                           # Template commands (13 files, 66 commands)
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
    │   └── DataPipelineCommands.cs     # ValidateTemplate (45 checks), DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit
    │
    └── Data/                           # Runtime data files (25 files)
        ├── BLE_MATERIALS.csv           # 815 building-element materials
        ├── MEP_MATERIALS.csv           # 464 MEP materials
        ├── MR_PARAMETERS.txt           # Shared parameter file (1,549 params, 21 groups)
        ├── MR_PARAMETERS.csv           # Parameter definitions
        ├── MR_SCHEDULES.csv            # 168 schedule definitions
        ├── MATERIAL_SCHEMA.json        # 77-column material schema (v2.3)
        ├── FORMULAS_WITH_DEPENDENCIES.csv  # 199 parameter formulas
        ├── SCHEDULE_FIELD_REMAP.csv    # 50 field deprecation remaps
        ├── BINDING_COVERAGE_MATRIX.csv # Parameter-category coverage
        ├── BOQ_TEMPLATE.csv            # Bill of Quantities template structure
        ├── CATEGORY_BINDINGS.csv       # 10,661 category bindings
        ├── FAMILY_PARAMETER_BINDINGS.csv   # 4,686 family bindings
        ├── PARAMETER__CATEGORIES.csv   # Parameter-category cross-reference
        ├── PARAMETER_REGISTRY.json     # Master parameter registry (v4.3) — single source of truth for ParamRegistry.cs
        ├── LABEL_DEFINITIONS.json      # 10,775-line label/legend definition specs (v5.5, 126 categories, warnings aligned to TAG7)
        ├── TAG_CONFIG_v5_0_CONTAINERS.csv    # 122 tag container definitions (v5.0)
        ├── TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv # 179 discipline/system/function code mappings (v5.0)
        ├── TAG_CONFIG_v5_0_VALIDATION.csv    # 180 validation rules for tag tokens (v5.0)
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide (original)
        ├── TAG GUIDE V2.xlsx           # Tag reference guide (comprehensive update)
        ├── TAG_GUIDE_V3.csv            # Tag reference guide (newest CSV version)
        ├── VALIDAT_BIM_TEMPLATE.py     # BIM template validation (45 checks, ported to C#)
        ├── TAG_PLACEMENT_PRESETS_DEFAULT.json  # Default tag placement preset (12 category rules)
        └── WORKFLOW_DailyQA_Enhanced.json     # Enhanced Daily QA workflow (8 conditional steps)
```

## Ribbon UI Architecture (Legacy)

> **Note:** The ribbon panels have been superseded by the WPF dockable panel (see [WPF Dockable Panel](#wpf-dockable-panel-primary-ui) section). The ribbon is retained for compatibility but the dockable panel is the primary user interface. All commands are accessible from both surfaces.

### Select Panel (3 pulldowns + 1 button + Color pulldown)
| Group | Commands | Description |
|-------|----------|-------------|
| Category | 16 selectors (Lighting, Electrical, Mechanical, Plumbing, Air Terminals, Furniture, Doors, Windows, Rooms, Sprinklers, Pipes, Ducts, Conduits, Cable Trays, ALL Taggable, Custom Category) | Select elements by Revit category in active view |
| State | Untagged, Tagged, Empty Mark, Pinned, Unpinned | Select by tag/pin/mark state |
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

**Smart Tag Placement pulldown (13 commands):**
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
| Align Tag Bands | `Tags.AlignTagBandsCommand` | Manual | Snap tags into horizontal grid bands by Y coordinate |
| Switch Tag Position | `Tags.SwitchTagPositionCommand` | Manual | Set tag position preference (Above/Right/Below/Left) via `STING_TAG_POS` |
| Export Tag Positions | `Tags.ExportTagPositionsCommand` | ReadOnly | Export all tag positions in active view to CSV |
| Batch Place Linked Tags | `Tags.BatchPlaceLinkedTagsCommand` | Manual | Place annotation tags on elements in linked Revit models |

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
| `Core/StingToolsApp.cs` | 1 (ToggleDockPanelCommand) + IExternalApplication + DocumentOpened quality gate | 457 |
| `Core/StingLog.cs` | 0 (logger + EscapeChecker utility) | 127 |
| `Core/ParamRegistry.cs` | 0 (parameter registry infrastructure + stale/cluster/display/position constants) | 1,603 |
| `Core/ParameterHelpers.cs` | 0 (helpers + TokenAutoPopulator + PhaseAutoDetect + TypeTokenInherit + CopyTokensFromNearest + GetInt) | 2,056 |
| `Core/SharedParamGuids.cs` | 0 (backwards-compatible facade) | 228 |
| `Core/TagConfig.cs` | 0 (tag config + ISO validator + TAG7 builder + SeqScheme + BuildDisplayTag) | 4,684 |
| `Core/StingAutoTagger.cs` | 3 (AutoTaggerToggle, AutoTaggerToggleVisual, AutoTaggerConfig) + StingAutoTagger IUpdater + StingStaleMarker IUpdater | 596 |
| `Core/WorkflowEngine.cs` | 4 (WorkflowPreset, ListPresets, CreatePreset, WorkflowTrend) + WorkflowEngine + WorkflowRunRecord | 814 |
| `Core/ComplianceScan.cs` | 0 (cached compliance scan + per-discipline DiscComplianceData) | 197 |
| `Select/CategorySelectCommands.cs` | 16 (14 category selectors + SelectAllTaggable + SelectCustomCategory) | 322 |
| `Select/StateSelectCommands.cs` | 8 (5 state + 2 spatial + BulkParamWrite) | 625 |
| `Select/ColorCommands.cs` | 5 (ColorByParameter, ClearOverrides, SavePreset, LoadPreset, CreateFilters) + ColorHelper | 922 |
| `Docs/SheetOrganizerCommand.cs` | 1 | 103 |
| `Docs/ViewOrganizerCommand.cs` | 1 | 93 |
| `Docs/SheetIndexCommand.cs` | 1 | 78 |
| `Docs/TransmittalCommand.cs` | 1 | 124 |
| `Docs/ViewportCommands.cs` | 4 (Align, Renumber, TextCase, SumAreas) | 412 |
| `Docs/DocAutomationCommands.cs` | 3 (DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets) | 459 |
| `Docs/DocAutomationExtCommands.cs` | 12 (BatchViews, BatchSheets, DependentViews, ScopeBox, ViewTemplate, DocPackage, Sections, Elevations, DrawingRegister, BrowserOrganizer, RevisionCloudAutoCreate, HandoverManual) | 3,168 |
| `Docs/ViewAutomationCommands.cs` | 6 (DuplicateView, BatchRename, CopySettings, AutoPlace, CropToContent, BatchAlign) | 825 |
| `Tags/AutoTagCommand.cs` | 2 (AutoTag, TagNewOnly) | 355 |
| `Tags/BatchTagCommand.cs` | 3 (BatchTag, TagFormatMigration, TagChanged) | 596 |
| `Tags/TagAndCombineCommand.cs` | 1 | 235 |
| `Tags/PreTagAuditCommand.cs` | 1 (+ auto-fix chain: AnomalyAutoFix → ResolveAllIssues) | 480 |
| `Tags/FamilyStagePopulateCommand.cs` | 1 | 379 |
| `Tags/CombineParametersCommand.cs` | 2 (CombineParameters, CombinePreFlight) | 426 |
| `Tags/ConfigEditorCommand.cs` | 1 | 211 |
| `Tags/TagConfigCommand.cs` | 1 | 63 |
| `Tags/LoadSharedParamsCommand.cs` | 1 | 344 |
| `Tags/TokenWriterCommands.cs` | 8 (SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, CompletenessDashboard, SetSeqScheme) | 630 |
| `Tags/ValidateTagsCommand.cs` | 1 | 499 |
| `Tags/SmartTagPlacementCommand.cs` | 13 (SmartPlace, Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight, AlignTagBands, SwitchTagPosition, ExportTagPositions, BatchPlaceLinkedTags) | 2,386 |
| `Tags/TagFamilyCreatorCommand.cs` | 4 (CreateTagFamilies, LoadTagFamilies, ConfigureTagLabels, AuditTagFamilies) | 1,729 |
| `Tags/SyncParameterSchemaCommand.cs` | 3 (SyncParameterSchema, AddParamRemap, AuditParameterSchema) | 661 |
| `Tags/LegendBuilderCommands.cs` | 31 (color/tag/discipline/system/material/equipment/fire/template legends + master pipeline) + LegendEngine | 7,110 |
| `Tags/RichTagDisplayCommands.cs` | 6 (RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments) | 1,105 |
| `Tags/SystemParamPushCommand.cs` | 3 (SystemParamPush, BatchSystemPush, SelectSystemElements) | 916 |
| `Tags/ResolveAllIssuesCommand.cs` | 1 (one-click ISO 19650 resolution) | 291 |
| `Tags/PresentationModeCommand.cs` | 4 (SetPresentationMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle) | 926 |
| `Tags/ParagraphDepthCommand.cs` | 2 (SetParagraphDepth, ToggleWarningVisibility) | 213 |
| `Tags/TagStyleCommands.cs` | 10 (ApplyTagStyle, ApplyColorScheme, ClearColorScheme, SetParagraphDepthExt, TagStyleReport, SwitchTagStyleByDisc, BatchApplyColorScheme, ColorByVariable, SetBoxColor, SetViewTagStyle) | 817 |
| `Tags/TagStyleEngine.cs` | 0 (tag style engine: presets, color schemes, paragraph depth) | 1,007 |
| `Organise/TagOperationCommands.cs` | 47 (7 Tag Ops + 14 Leaders + 7 Analysis + 5 Annotation Color + 5 Tag Appearance + 1 SwapTagType + 1 AnomalyAutoFix + 2 Clustering + 1 DisplayMode + 1 DiscCompliance + 1 RetagStale + 1 DeclusterTags + LeaderHelper + AnnotationColorHelper) | 5,070 |
| `Model/ModelCommands.cs` | 16 (Wall, Room, Floor, Ceiling, Roof, Door, Window, Column, ColumnGrid, Beam, Duct, Pipe, Fixture, BuildingShell, DWGToModel, DWGPreview) | 1,101 |
| `Model/ModelEngine.cs` | 0 (model creation engine + FamilyResolver + WorksetAssigner + FailureHandler) | 1,349 |
| `Model/CADToModelEngine.cs` | 0 (DWG-to-BIM conversion engine + LayerMapper) | 824 |
| `BIMManager/BIMManagerCommands.cs` | 35 (CreateBEP, UpdateBEP, ExportBEP, GenerateBEP, ProjectDashboard, RaiseIssue, IssueDashboard, UpdateIssue, DocumentRegister, AddDocument, COBieExport, CreateTransmittal, CDEStatus, ValidateDocNaming, ReviewTracker, SelectIssueElements, ISO19650Reference, BulkBIMExport, BriefcaseView, BriefcaseRead, BriefcaseAddFile, DocumentBriefcase, ElementStickyNote, ExportStickyNotes, SelectStickyElements, ModelHealthDashboard, ExportModelHealth, MidpTracker, Export4DTimeline, Export5DCostData, FullComplianceDashboard, LinkPredecessors, AssignPhaseDates, MeasuredQuantities, ElementCountSummary) + BIMManagerEngine + COBiePreset (22 presets) | 6,266 |
| `BIMManager/SchedulingCommands.cs` | 12 (AutoSchedule4D, ImportMSProject, ViewTimeline4D, ExportSchedule4D, AutoCost5D, ImportCostRates, CostReport5D, CashFlow5D, PhaseFilter, PhaseSummary, MilestoneRegister, WorkingCalendar) + Scheduling4DEngine | 1,713 |
| `Temp/CreateParametersCommand.cs` | 1 | 27 |
| `Temp/CheckDataCommand.cs` | 1 | 297 |
| `Temp/MasterSetupCommand.cs` | 1 | 306 |
| `Temp/ProjectSetupCommand.cs` | 1 (ProjectSetup — launches 7-page WPF wizard) | 1,104 |
| `Temp/MaterialCommands.cs` | 2 (BLE, MEP) + MaterialPropertyHelper | 783 |
| `Temp/FamilyCommands.cs` | 6 (Walls, Floors, Ceilings, Roofs, Ducts, Pipes) + CompoundTypeCreator | 723 |
| `Temp/ScheduleCommands.cs` | 6 (FullAutoPopulate, BatchSchedules, AutoPopulate, ExportCSV, CorporateTitleBlockSchedule, DrawingRegisterSchedule) + ScheduleHelper | 2,004 |
| `Temp/ScheduleEnhancementCommands.cs` | 9 (Audit, Compare, Duplicate, Refresh, FieldMgr, Color, Stats, Delete, Report) | 1,634 |
| `Temp/FormulaEvaluatorCommand.cs` | 1 (+ FormulaEngine + ExpressionParser) | 878 |
| `Temp/TemplateCommands.cs` | 3 (Filters, Worksets, ViewTemplates) | 1,304 |
| `Temp/TemplateExtCommands.cs` | 6 (LinePatterns, Phases, ApplyFilters, CableTrays, Conduits, MaterialSchedules) | 316 |
| `Temp/TemplateManagerCommands.cs` | 18 (AutoAssign, Audit, Diff, Compliance, AutoFix, SyncOverrides, FillPatterns, LineStyles, ObjectStyles, TextStyles, DimStyles, VGOverrides, BatchFamilyParams, TemplateSchedules, SetupWizard, CloneTemplate, BatchVGReset, FamilyParameterProcessor) + TemplateManager engine | 3,893 |
| `Temp/DataPipelineCommands.cs` | 11 (ValidateTemplate, DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit, ExportIfcPropertyMap, ValidateBepCompliance, ClashDetection, IFCExport, ExcelBOQImport, KeynoteSync) | 3,013 |
| `Tags/FamilyParamCreatorCommand.cs` | 1 (FamilyParamCreator) + FamilyParamEngine | 576 |
| `UI/StingCommandHandler.cs` | 1 (IExternalEventHandler — dispatches 562+ button tags to 343 commands + ~96 inline helpers) | 4,827 |
| `UI/StingDockPanel.xaml.cs` | 0 (WPF code-behind) | 377 |
| `UI/StingDockPanelProvider.cs` | 0 (IDockablePaneProvider) | 37 |
| `UI/StingProgressDialog.cs` | 0 (reusable modeless WPF progress window) | 238 |
| `UI/ProjectSetupWizard.xaml.cs` | 0 (WPF wizard code-behind: 7 pages, presets, discipline config) | 1,124 |
| `UI/StingDockPanel.xaml` | — (WPF markup, 8-tab panel with ~580 buttons) | 2,047 |
| `UI/ProjectSetupWizard.xaml` | — (WPF markup, 7-page wizard dialog) | 793 |
| **Total** | **343 commands** | **~83,841** |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (457 lines)
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

### `StingAutoTagger` (IUpdater) — `Core/StingAutoTagger.cs` (596 lines)
- Real-time auto-tagging engine via Revit `IUpdater` interface
- Triggers on `Element.GetChangeTypeElementAddition()` for 22 tagged categories
- Auto-populates tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) and builds ISO 19650 tags on newly placed elements
- **Visual tag placement**: optionally creates `IndependentTag` annotations when visual tagging is enabled
- **Discipline filter**: restricts auto-tagging to user-specified discipline codes
- **Workset ownership check**: skips elements on worksets not owned by the current user (worksharing safety)
- **Type token inheritance**: calls `TypeTokenInherit(doc, el)` before `PopulateAll` to inherit from element type
- Registered in `StingToolsApp.OnStartup()`, starts disabled — user toggles via `AutoTaggerToggleCommand`
- Performance: suppresses redundant triggers via `HashSet<long>` of processed element IDs (clears at 10,000 entries)
- Contains `AutoTaggerToggleCommand` — toggles data auto-tagger on/off
- Contains `AutoTaggerToggleVisualCommand` — toggles visual tag placement on/off
- Contains `AutoTaggerConfigCommand` — configure discipline filter for auto-tagging

### `StingStaleMarker` (IUpdater) — `Core/StingAutoTagger.cs`
- Detects geometry changes on tagged elements and marks them as stale (`STING_STALE_BOOL = 1`)
- Triggers on `Element.GetChangeTypeGeometry()` for same 22 categories as auto-tagger
- Only marks elements that already have a non-empty ASS_TAG_1 and have changed level
- Performance: 20-element-per-trigger guard
- Registered/unregistered alongside StingAutoTagger in `StingToolsApp`

### `WorkflowEngine` (static) — `Core/WorkflowEngine.cs` (814 lines)
- Workflow orchestration engine for chaining named command sequences
- JSON-based workflow presets loaded from `data/WORKFLOW_*.json` files
- Built-in presets: `ProjectKickoff` (26 steps), `DailyQA` (9 steps with conditionals), `DocumentPackage` (6 steps)
- Per-step progress reporting with timing, Escape key cancellation between steps
- Atomic `TransactionGroup` with rollback on failure — user can keep or rollback partial results
- `ResolveCommand(tag)` maps ~63 command tags to `IExternalCommand` instances
- **Conditional step execution**: `MinCompliancePct`, `MaxCompliancePct` (compliance threshold gates), `RequiresStaleElements` (skip if no stale elements), workshared-only steps
- **Result persistence**: `WorkflowRunRecord` saved to `STING_WORKFLOW_LOG.json` alongside project file (capped at 100 records)
- Contains 4 commands: `WorkflowPresetCommand`, `ListWorkflowPresetsCommand`, `CreateWorkflowPresetCommand`, `WorkflowTrendCommand`

### `ComplianceScan` (static) — `Core/ComplianceScan.cs` (197 lines)
- Lightweight cached compliance scan for live dashboard/status bar display
- Thread-safe cached results with 30-second stale duration
- `Scan(doc)` — quick tag completeness stats (tagged complete/incomplete/untagged)
- `ComplianceResult` — RAG status (Red <50%, Amber 50-80%, Green >80%), top 5 issues
- **Per-discipline breakdown**: `ByDisc` dictionary of `DiscComplianceData` (Total, Tagged, Untagged, MissingLoc, MissingSys, MissingProd, CompliancePct)
- `StatusBarText` — formatted string for WPF status bar display
- `InvalidateCache()` — called after tagging operations to force refresh

### `StingProgressDialog` — `UI/StingProgressDialog.cs` (239 lines)
- Reusable modeless WPF progress window for batch operations
- Shows progress bar, element count (N/M), estimated time remaining, and cancel button
- Thread-safe updates via `Dispatcher.Invoke` (updates every 50 elements for performance)
- Win32 `GetAsyncKeyState` for Escape key detection even when Revit has focus
- Usage: `var p = StingProgressDialog.Show("Title", total); p.Increment("status"); p.Close();`

### `ParamRegistry` (static) — `Core/ParamRegistry.cs` (1,603 lines)
- **Single source of truth** for all parameter names, GUIDs, container definitions, and category bindings
- Loads from `PARAMETER_REGISTRY.json` at runtime (thread-safe lazy initialization via `EnsureLoaded()` with lock); falls back to hardcoded defaults if JSON not found
- **Tag format configuration**: `Separator`, `NumPad`, `SegmentOrder` — data-driven rather than hardcoded
- **Typed string constants** for all 8 source tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ) + universal containers (TAG1-TAG7, TAG7A-TAG7F)
- **Phase 11 constants**: `STALE`/`STALE_GUID`, `CLUSTER_COUNT`/`CLUSTER_LABEL`, `DISPLAY_MODE`/`DISPLAY_TXT`, `TAG_POS`, `VIEW_TAG_STYLE` — system parameters for stale detection, clustering, display modes, and view-level tag style routing
- **Extended parameter constants**: ~67+ parameters across identity, spatial, BLE dimensional, electrical, lighting, HVAC, and plumbing groups
- **GUID lookups**: `GetGuid(paramName)`, `GetParamName(guid)`, `AllParamGuids`
- **Container management**: `AllContainers`, `ContainersForCategory(categoryName)`, `GetContainerTuples()`
- **Token presets**: Named index arrays for partial tag strings
- **Tag assembly**: `AssembleContainer()`, `ReadTokenValues()`, `WriteContainers()`
- **Reload**: `Reload()` forces re-read from disk for live editing workflows

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs` (2,056 lines)
- `GetString(el, paramName)` — read text parameter, returns empty string on null
- `GetInt(el, paramName, defaultValue)` — read integer parameter with fallback (handles Integer, Double, String storage)
- `SetString(el, paramName, value, overwrite)` — write text parameter, skips read-only/non-empty unless overwrite
- `SetIfEmpty(el, paramName, value)` — set only when currently empty
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

### `SeqScheme` (enum) — `Core/TagConfig.cs`
- Controls sequence numbering format: `Numeric` (0001), `Alpha` (AAAB), `ZonePrefix` (Z01-0001), `DiscPrefix` (M-0001)
- `TagConfig.CurrentSeqScheme` — current active scheme (default: Numeric)
- `TagConfig.SeqIncludeZone` — whether to include zone in the SEQ grouping key
- `TagConfig.SeqLevelReset` — whether to reset numbering per level
- `TagConfig.BuildSeqString(counter, scheme, context)` — formats sequence number per active scheme

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (4,684 lines)
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
- **Display tag**: `BuildDisplayTag(el, mode)` — generates shortened tag strings for display modes (SEQ only, PROD-SEQ, DISC-SYS-SEQ, DISC-PROD-SEQ, full 8-segment)
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
| `StingCommandHandler` | `UI/StingCommandHandler.cs` | `IExternalEventHandler` — dispatches 562+ dockable panel button tags to 343 command classes + ~96 inline helpers on the Revit API thread |
| `StingDockPanel` | `UI/StingDockPanel.xaml.cs` | WPF code-behind for 8-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM) with colour swatches and status bar |
| `StingDockPanelProvider` | `UI/StingDockPanelProvider.cs` | `IDockablePaneProvider` — registers dockable panel with Revit; PaneGuid for panel identification |
| `ColorHelper` | `Select/ColorCommands.cs` | 10 built-in colour palettes, `OverrideGraphicSettings` builder, solid fill pattern finder, preset save/load |
| `TagPlacementEngine` | `Tags/SmartTagPlacementCommand.cs` | 8-position candidate offset generation, scale-aware placement, 2D AABB collision detection, leader auto-generation |
| `TagPlacementPresets` | `Tags/SmartTagPlacementCommand.cs` | Per-category placement rules (`CategoryRule`), named presets (`PlacementPreset`), `LearnFromView` analysis |
| `WorkflowEngine` | `Core/WorkflowEngine.cs` | Workflow preset orchestration: command tag resolution, step execution, cancellation, TransactionGroup rollback |
| `ComplianceScan` | `Core/ComplianceScan.cs` | Cached compliance scan: RAG status, issue tracking, status bar text generation |
| `StingAutoTagger` | `Core/StingAutoTagger.cs` | IUpdater-based real-time auto-tagging on element addition with visual tag placement, discipline filter, workset ownership check |
| `StingStaleMarker` | `Core/StingAutoTagger.cs` | IUpdater-based stale element detection on geometry changes — marks elements with `STING_STALE_BOOL = 1` |
| `FamilyParamEngine` | `Tags/FamilyParamCreatorCommand.cs` | Family parameter injection: DetectFamilyCategory, InjectSharedParams, InjectTagPosFormulas, InjectPositionTypes, ProcessFamily |
| `WorkflowRunRecord` | `Core/WorkflowEngine.cs` | JSON-serializable workflow execution record: timestamp, preset, step counts, duration, compliance before/after |
| `StingProgressDialog` | `UI/StingProgressDialog.cs` | Modeless WPF progress window: progress bar, ETA, cancel button, Escape key detection |
| `EscapeChecker` | `Core/StingLog.cs` | Win32 `GetAsyncKeyState` wrapper for Escape key cancellation detection |

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

The recommended tagging workflow is a multi-layered pipeline:

1. **Load Parameters** (`LoadSharedParamsCommand`) — Bind 1,527 shared parameters (21 groups) to 53 categories (2-pass: universal + discipline-specific)
2. **Family-Stage Populate** (`FamilyStagePopulateCommand`) — Pre-populate all 7 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) from category, spatial, and family data
3. **Pre-Tag Audit** (`PreTagAuditCommand`) — Dry-run: predict tag assignments, collisions, ISO violations before committing
4. **Tag** (`AutoTagCommand` / `BatchTagCommand` / `TagAndCombineCommand`) — Assign SEQ numbers and assemble final 8-segment tags with collision handling
5. **Validate** (`ValidateTagsCommand`) — ISO 19650 compliance check on all tokens and tag format
6. **Combine** (`CombineParametersCommand`) — Write assembled tag to all 36 discipline-specific containers

Alternatively, use **Tag & Combine** or **Full Auto-Populate** for one-click automation that executes the entire pipeline.

## WPF Dockable Panel (Primary UI)

The plugin's primary user interface is a **WPF dockable panel** that consolidates all commands from the original ribbon into a single tabbed panel docked to the right side of Revit. The ribbon panels still exist for backwards compatibility but the dockable panel is the main interaction surface.

### Architecture

| Component | File | Purpose |
|-----------|------|---------|
| `StingDockPanel.xaml` | `UI/StingDockPanel.xaml` (2,047 lines) | WPF markup: 8-tab layout (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL/BIM), ~580 buttons, colour swatches, bulk parameter controls |
| `StingDockPanel.xaml.cs` | `UI/StingDockPanel.xaml.cs` (377 lines) | Code-behind: button dispatch via `IExternalEventHandler`, colour swatch builder, status bar |
| `StingCommandHandler` | `UI/StingCommandHandler.cs` (4,827 lines) | `IExternalEventHandler` — maps 562+ button Tag strings to 343 command classes + ~96 inline helpers, ensures Revit API calls run on the main thread |
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
| BIM | ISO 19650 BIM management: BEP, issues, documents, COBie, transmittals, CDE, reviews, briefcase, 4D/5D scheduling | BIMManager |

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

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
- [The Building Coder — Tag Extents](https://thebuildingcoder.typepad.com/blog/2022/07/tag-extents-and-lazy-detail-components.html) — Getting accurate tag bounding boxes
- [Revit API — OverrideGraphicSettings](https://www.revitapidocs.com/2025/eb2bd6b6-b7b2-5452-2070-2dbadb9e068a.htm) — Complete graphic override API
- [Revit API — IndependentTag](https://www.revitapidocs.com/2025/e52073e2-9d98-6fb5-eb43-288cf9ed2e28.htm) — Tag creation and positioning API
