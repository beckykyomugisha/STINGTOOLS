# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **58 source files** (55 C# + 2 XAML + 1 AssemblyInfo, ~56,030 lines of code) across 8 directories
- **234 `IExternalCommand` classes** (commands) + 1 `IExternalApplication` entry point + 1 `IExternalEventHandler` + 1 `IDockablePaneProvider`
- **18 runtime data files** (CSV, JSON, TXT, XLSX, PY)
- **6 ribbon panels** with 23 pulldown groups + 1 WPF dockable panel + 1 WPF project setup wizard

## Technology Stack

- **Platform**: Autodesk Revit 2025/2026/2027 (BIM software)
- **Language**: C# / .NET 8.0 (`net8.0-windows`)
- **Plugin type**: `IExternalApplication` + `IExternalEventHandler` + `IDockablePaneProvider` with `IExternalCommand` classes
- **Dependencies**: `Newtonsoft.Json` 13.0.3, Revit API assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`)
- **Data formats**: CSV and JSON files for configuration data (materials, parameters, schedules)
- **Deployment**: `StingTools.addin` (XML manifest) + `extract_plugin.sh` (Bash)

## Directory Structure

```
STINGTOOLS/
├── .gitignore                          # Build output, IDE files, NuGet, OS files
├── CLAUDE.md                           # AI assistant guide (this file)
├── StingTools.addin                    # Revit addin manifest (XML)
├── extract_plugin.sh                   # Plugin extraction/deployment script
│
└── StingTools/                         # C# project root
    ├── StingTools.csproj               # .NET 8 project file
    │
    ├── Properties/
    │   └── AssemblyInfo.cs             # Assembly metadata (v1.0.0.0)
    │
    ├── Core/                           # Shared infrastructure (6 files, ~6,758 lines)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI + dockable panel registration + ToggleDockPanelCommand
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error)
    │   ├── ParamRegistry.cs            # Single source of truth for parameter names, GUIDs, containers, bindings (loads from PARAMETER_REGISTRY.json)
    │   ├── ParameterHelpers.cs         # Parameter read/write + SpatialAutoDetect + NativeParamMapper + TokenAutoPopulator + PhaseAutoDetect
    │   ├── SharedParamGuids.cs         # GUID map for 200+ shared parameters
    │   └── TagConfig.cs               # ISO 19650 tag lookup tables, tag builder, TagIntelligence, TAG7 narrative builder
    │
    ├── Select/                         # Element selection + color commands (3 files, 28 commands)
    │   ├── CategorySelectCommands.cs   # 14 category selectors + SelectAllTaggable + CategorySelector helper
    │   ├── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite
    │   └── ColorCommands.cs            # 5 color-by-parameter commands + ColorHelper (10 palettes, presets, filter gen)
    │
    ├── UI/                             # WPF dockable panel UI + project wizard (5 C# files + 2 XAML, ~7,731 lines)
    │   ├── StingDockPanel.xaml         # WPF markup for 6-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW)
    │   ├── StingDockPanel.xaml.cs      # Code-behind: button dispatch, colour swatches, status bar
    │   ├── StingCommandHandler.cs      # IExternalEventHandler — dispatches 400+ button tags to 234 command classes + inline helpers
    │   ├── StingDockPanelProvider.cs   # IDockablePaneProvider — registers panel with Revit
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
    │   ├── DocAutomationExtCommands.cs # Batch views/sheets/sections/elevations, doc package, scope boxes, templates, drawing register, browser organizer
    │   └── ViewAutomationCommands.cs   # DuplicateView, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign
    │
    ├── Tags/                           # Tagging commands (20 files, 83 commands)
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
    │   │                               #   BuildTags, CompletenessDashboard + TokenWriter helper
    │   ├── ValidateTagsCommand.cs      # Validate tag completeness with ISO 19650 codes
    │   ├── SmartTagPlacementCommand.cs # 9 smart annotation commands + TagPlacementEngine (collision avoidance, templates, text/leader controls)
    │   ├── TagFamilyCreatorCommand.cs  # 4 tag family commands: Create, Load, Configure Labels, Audit + TagFamilyConfig
    │   ├── SyncParameterSchemaCommand.cs # 3 schema commands: Sync, AddParamRemap, Audit + ParamRegistry propagation
    │   ├── LegendBuilderCommands.cs    # 31 legend commands: discipline/system/tag/color/material/equipment/fire rating legends + LegendEngine
    │   ├── RichTagDisplayCommands.cs   # 6 rich display commands: RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments
    │   ├── SystemParamPushCommand.cs   # 3 MEP system push commands: SystemParamPush, BatchSystemPush, SelectSystemElements
    │   ├── ResolveAllIssuesCommand.cs  # 1 one-click ISO 19650 compliance resolution
    │   ├── PresentationModeCommand.cs  # 4 presentation commands: SetMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle
    │   └── ParagraphDepthCommand.cs    # 2 commands: SetParagraphDepth, ToggleWarningVisibility
    │
    ├── Organise/                       # Tag management commands (1 file, 39 commands)
    │   └── TagOperationCommands.cs     # Tag Ops (7), Leaders (14), Analysis (4), Annotation Color (5), Tag Appearance (7), Tag Type (1) + LeaderHelper
    │
    ├── Temp/                           # Template commands (13 files, 57 commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MasterSetupCommand.cs       # One-click full project setup (15 steps)
    │   ├── ProjectSetupCommand.cs      # 7-page WPF project setup wizard (4 commands: Setup, PrePopulate, PreCheck, Configure)
    │   ├── MaterialCommands.cs         # BLE + MEP material creation + MaterialPropertyHelper
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types + CompoundTypeCreator
    │   ├── ScheduleCommands.cs         # FullAutoPopulate, BatchSchedules, AutoPopulate, ExportCSV + ScheduleHelper
    │   ├── ScheduleEnhancementCommands.cs # 9 schedule mgmt: Audit, Compare, Duplicate, Refresh, FieldMgr, Color, Stats, Delete, Report
    │   ├── FormulaEvaluatorCommand.cs  # Formula engine (199 formulas) + FormulaEngine + ExpressionParser
    │   ├── TemplateCommands.cs         # Filters, worksets, view templates (23 template defs + VG configuration)
    │   ├── TemplateExtCommands.cs      # Line patterns, phases, apply filters, cable trays, conduits, material schedules
    │   ├── TemplateManagerCommands.cs  # 19 template intelligence commands + TemplateManager engine (~3,415 lines)
    │   └── DataPipelineCommands.cs     # ValidateTemplate (45 checks), DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit
    │
    └── Data/                           # Runtime data files (18 files)
        ├── BLE_MATERIALS.csv           # 815 building-element materials
        ├── MEP_MATERIALS.csv           # 464 MEP materials
        ├── MR_PARAMETERS.txt           # Shared parameter file (200+ params)
        ├── MR_PARAMETERS.csv           # Parameter definitions
        ├── MR_SCHEDULES.csv            # 168 schedule definitions
        ├── MATERIAL_SCHEMA.json        # 77-column material schema (v2.3)
        ├── FORMULAS_WITH_DEPENDENCIES.csv  # 199 parameter formulas
        ├── SCHEDULE_FIELD_REMAP.csv    # 50 field deprecation remaps
        ├── BINDING_COVERAGE_MATRIX.csv # Parameter-category coverage
        ├── CATEGORY_BINDINGS.csv       # 10,661 category bindings
        ├── FAMILY_PARAMETER_BINDINGS.csv   # 4,686 family bindings
        ├── PARAMETER__CATEGORIES.csv   # Parameter-category cross-reference
        ├── PARAMETER_REGISTRY.json     # Master parameter registry (v4.3) — single source of truth for ParamRegistry.cs
        ├── LABEL_DEFINITIONS.json      # 3,623-line label/legend definition specs for all tag containers and display styles
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide (original)
        ├── TAG GUIDE V2.xlsx           # Tag reference guide (comprehensive update)
        └── VALIDAT_BIM_TEMPLATE.py     # BIM template validation (45 checks)
```

## Ribbon UI Architecture (Legacy)

> **Note:** The ribbon panels have been superseded by the WPF dockable panel (see [WPF Dockable Panel](#wpf-dockable-panel-primary-ui) section). The ribbon is retained for compatibility but the dockable panel is the primary user interface. All commands are accessible from both surfaces.

### Select Panel (3 pulldowns + 1 button + Color pulldown)
| Group | Commands | Description |
|-------|----------|-------------|
| Category | 15 selectors (Lighting, Electrical, Mechanical, Plumbing, Air Terminals, Furniture, Doors, Windows, Rooms, Sprinklers, Pipes, Ducts, Conduits, Cable Trays, ALL Taggable) | Select elements by Revit category in active view |
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

### Docs Panel (4 buttons + Viewports pulldown + Automation pulldown)
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

**View Automation pulldown (6 commands — NEW):**
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

**Smart Tag Placement pulldown (4 commands — NEW):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Smart Place Tags | `Tags.SmartPlaceTagsCommand` | Manual | Priority-based `IndependentTag` placement with 8-position collision avoidance |
| Arrange Tags | `Tags.ArrangeTagsCommand` | Manual | Auto-arrange placed tags into aligned grid patterns |
| Remove Annotation Tags | `Tags.RemoveAnnotationTagsCommand` | Manual | Remove all `IndependentTag` annotations from view |
| Batch Place Tags | `Tags.BatchPlaceTagsCommand` | Manual | Place visual annotation tags across multiple views |

### Organise Panel (4 pulldowns: Tag Ops + Leaders + Analysis + Annotation Color)
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

**Leaders pulldown (12 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Toggle Leaders | `Organise.ToggleLeadersCommand` | Manual | Toggle leaders on/off for selected tags (or all tags in view) |
| Add Leaders | `Organise.AddLeadersCommand` | Manual | Add leaders to all selected annotation tags |
| Remove Leaders | `Organise.RemoveLeadersCommand` | Manual | Remove leaders from all selected annotation tags |
| Align Tags | `Organise.AlignTagsCommand` | Manual | Align tag heads horizontally, vertically, or in a row |
| Reset Tag Positions | `Organise.ResetTagPositionsCommand` | Manual | Move tags back to element centers (remove manual offsets) |
| Toggle Orientation | `Organise.ToggleTagOrientationCommand` | Manual | Switch selected tags between horizontal and vertical |
| Snap Leader Elbows | `Organise.SnapLeaderElbowCommand` | Manual | Snap leader elbows to 45 or 90 degree angles for clean layout |
| Flip Tags | `Organise.FlipTagsCommand` | Manual | Mirror tag position across element center (left/right, up/down) |
| Align Tag Text | `Organise.AlignTagTextCommand` | Manual | Align annotation text (left/center/right) for tags and text notes |
| Pin/Unpin Tags | `Organise.PinTagsCommand` | Manual | Lock tags in place to prevent accidental movement (or unlock) |
| Attach/Free Leader | `Organise.AttachLeaderCommand` | Manual | Attach leader end to host element or set free |
| Select Tags By Leader | `Organise.SelectTagsWithLeadersCommand` | ReadOnly | Select tags with or without leaders in active view |

**Analysis pulldown (4 commands):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Audit to CSV | `Organise.AuditTagsCSVCommand` | ReadOnly | Export full tag audit to CSV file |
| Select by Discipline | `Organise.SelectByDisciplineCommand` | ReadOnly | Select all elements of a specific discipline code |
| Tag Statistics | `Organise.TagStatsCommand` | ReadOnly | Quick tag counts by discipline/system/level for active view |
| Tag Register Export | `Organise.TagRegisterExportCommand` | ReadOnly | Comprehensive asset register export (40+ columns: tags, identity, spatial, MEP, cost, validation) |

**Annotation Color pulldown (5 commands — NEW):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Color Tags by Discipline | `Organise.ColorTagsByDisciplineCommand` | Manual | Colour-code annotation tags by discipline |
| Set Tag Text Color | `Organise.SetTagTextColorCommand` | Manual | Set text color for selected annotation tags |
| Set Leader Color | `Organise.SetLeaderColorCommand` | Manual | Set leader line color for selected tags |
| Split Tag/Leader Color | `Organise.SplitTagLeaderColorCommand` | Manual | Apply different colors to leader vs tag text |
| Clear Annotation Colors | `Organise.ClearAnnotationColorsCommand` | Manual | Clear all annotation color overrides in view |

### Temp Panel (9 pulldown groups, 54 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup** | Project setup + one-click automation (15-step workflow) |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes (FamilyCommands.cs), Cable Trays, Conduits (TemplateExtCommands.cs) | Type creation from CSV data (8 commands) |
| Schedules | **Full Auto-Populate**, Batch Create, Material Takeoffs, Auto-Populate (Tokens Only), Evaluate Formulas, Export CSV | Schedule creation + zero-input automation (6 commands) |
| **Schedule Mgr** | Audit, Compare, Duplicate, Refresh, Field Manager, Colors, Stats, Delete, Report | Deep schedule management + formatting (9 commands — NEW) |
| Templates | **★ Template Setup Wizard**, Create Filters, Apply Filters to Views, Create Worksets, View Templates, Line Patterns, Phases | One-click template pipeline + 28 filters, 35 worksets, 23 templates, 10 line patterns, 6 phases (7 commands) |
| **Template Mgr** | Auto-Assign Templates, Template Audit, Template Diff, Compliance Scores, Auto-Fix Templates, Sync VG Overrides, Apply VG Overrides, Clone Template, Batch VG Reset, Batch Family Params, Template Schedules | 5-layer intelligence template management (11 commands) |
| **Styles** | Fill Patterns, Line Styles, Object Styles, Text Styles, Dimension Styles | ISO-standard style creation (5 commands) |
| **Data Pipeline** | Validate Template, Dynamic Bindings, Schema Validate | Data integrity validation + dynamic CSV-driven bindings (3 commands — NEW) |

### Panel Panel (1 button)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| STING Panel | `Core.ToggleDockPanelCommand` | ReadOnly | Show/hide the STING Tools WPF dockable panel |

## Command Count by File

| File | Commands | Lines |
|------|----------|-------|
| `Core/StingToolsApp.cs` | 1 (ToggleDockPanelCommand) + IExternalApplication | 202 |
| `Core/ParamRegistry.cs` | 0 (parameter registry infrastructure) | 1,048 |
| `Core/ParameterHelpers.cs` | 0 (helpers + TokenAutoPopulator + PhaseAutoDetect) | 1,725 |
| `Core/TagConfig.cs` | 0 (tag config + ISO validator + TAG7 builder) | 3,507 |
| `Select/CategorySelectCommands.cs` | 15 (14 category selectors + SelectAllTaggable) | 168 |
| `Select/StateSelectCommands.cs` | 8 (5 state + 2 spatial + BulkParamWrite) | 613 |
| `Select/ColorCommands.cs` | 5 (ColorByParameter, ClearOverrides, SavePreset, LoadPreset, CreateFilters) + ColorHelper | 907 |
| `Docs/SheetOrganizerCommand.cs` | 1 | 101 |
| `Docs/ViewOrganizerCommand.cs` | 1 | 91 |
| `Docs/SheetIndexCommand.cs` | 1 | 75 |
| `Docs/TransmittalCommand.cs` | 1 | 93 |
| `Docs/ViewportCommands.cs` | 4 (Align, Renumber, TextCase, SumAreas) | 405 |
| `Docs/DocAutomationCommands.cs` | 3 (DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets) | 439 |
| `Docs/DocAutomationExtCommands.cs` | 10 (BatchViews, BatchSheets, DependentViews, ScopeBox, ViewTemplate, DocPackage, Sections, Elevations, DrawingRegister, BrowserOrganizer) | 2,383 |
| `Docs/ViewAutomationCommands.cs` | 6 (DuplicateView, BatchRename, CopySettings, AutoPlace, CropToContent, BatchAlign) | 812 |
| `Tags/AutoTagCommand.cs` | 2 (AutoTag, TagNewOnly) | 310 |
| `Tags/BatchTagCommand.cs` | 1 | 225 |
| `Tags/TagAndCombineCommand.cs` | 1 | 274 |
| `Tags/PreTagAuditCommand.cs` | 1 | 441 |
| `Tags/FamilyStagePopulateCommand.cs` | 1 | 344 |
| `Tags/CombineParametersCommand.cs` | 2 (CombineParameters, CombinePreFlight) | 423 |
| `Tags/ConfigEditorCommand.cs` | 1 | 194 |
| `Tags/TagConfigCommand.cs` | 1 | 63 |
| `Tags/LoadSharedParamsCommand.cs` | 1 | 169 |
| `Tags/TokenWriterCommands.cs` | 7 (SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, CompletenessDashboard) | 582 |
| `Tags/ValidateTagsCommand.cs` | 1 | 471 |
| `Tags/SmartTagPlacementCommand.cs` | 9 (SmartPlace, Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight) | 1,939 |
| `Tags/TagFamilyCreatorCommand.cs` | 4 (CreateTagFamilies, LoadTagFamilies, ConfigureTagLabels, AuditTagFamilies) | 1,296 |
| `Tags/SyncParameterSchemaCommand.cs` | 3 (SyncParameterSchema, AddParamRemap, AuditParameterSchema) | 574 |
| `Tags/LegendBuilderCommands.cs` | 31 (color/tag/discipline/system/material/equipment/fire/template legends + master pipeline) + LegendEngine | 7,045 |
| `Tags/RichTagDisplayCommands.cs` | 6 (RichTagNote, ExportReport, ViewSections, SwitchPreset, SegmentNote, ViewSegments) | 1,092 |
| `Tags/SystemParamPushCommand.cs` | 3 (SystemParamPush, BatchSystemPush, SelectSystemElements) | 886 |
| `Tags/ResolveAllIssuesCommand.cs` | 1 (one-click ISO 19650 resolution) | 274 |
| `Tags/PresentationModeCommand.cs` | 4 (SetPresentationMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle) | 924 |
| `Tags/ParagraphDepthCommand.cs` | 2 (SetParagraphDepth, ToggleWarningVisibility) | 209 |
| `Organise/TagOperationCommands.cs` | 39 (7 Tag Ops + 14 Leaders + 4 Analysis + 7 Annotation Color + 4 Tag Appearance + 1 SwapTagType + LeaderHelper + AnnotationColorHelper) | 4,111 |
| `Temp/CreateParametersCommand.cs` | 1 | 27 |
| `Temp/CheckDataCommand.cs` | 1 | 101 |
| `Temp/MasterSetupCommand.cs` | 1 | 280 |
| `Temp/ProjectSetupCommand.cs` | 4 (ProjectSetup, PrePopulate, PreCheck, Configure) | 1,109 |
| `Temp/MaterialCommands.cs` | 2 (BLE, MEP) + MaterialPropertyHelper | 429 |
| `Temp/FamilyCommands.cs` | 6 (Walls, Floors, Ceilings, Roofs, Ducts, Pipes) + CompoundTypeCreator | 683 |
| `Temp/ScheduleCommands.cs` | 4 (FullAutoPopulate, BatchSchedules, AutoPopulate, ExportCSV) + ScheduleHelper | 1,165 |
| `Temp/ScheduleEnhancementCommands.cs` | 9 (Audit, Compare, Duplicate, Refresh, FieldMgr, Color, Stats, Delete, Report) | 1,579 |
| `Temp/FormulaEvaluatorCommand.cs` | 1 (+ FormulaEngine + ExpressionParser) | 771 |
| `Temp/TemplateCommands.cs` | 3 (Filters, Worksets, ViewTemplates) | 1,299 |
| `Temp/TemplateExtCommands.cs` | 6 (LinePatterns, Phases, ApplyFilters, CableTrays, Conduits, MaterialSchedules) | 304 |
| `Temp/TemplateManagerCommands.cs` | 19 (AutoAssign, Audit, Diff, Compliance, AutoFix, SyncOverrides, FillPatterns, LineStyles, ObjectStyles, TextStyles, DimStyles, VGOverrides, BatchFamilyParams, TemplateSchedules, SetupWizard, CloneTemplate, BatchVGReset, + 2 new) + TemplateManager engine | 3,415 |
| `Temp/DataPipelineCommands.cs` | 5 (ValidateTemplate, DynamicBindings, SchemaValidate, BOQExport, TemplateVGAudit) | 2,007 |
| `UI/StingCommandHandler.cs` | 0 (IExternalEventHandler — dispatches 400+ button tags to 234 commands + 64 inline helpers) | 4,382 |
| `UI/StingDockPanel.xaml.cs` | 0 (WPF code-behind) | 188 |
| `UI/StingDockPanelProvider.cs` | 0 (IDockablePaneProvider) | 37 |
| `UI/ProjectSetupWizard.xaml.cs` | 0 (WPF wizard code-behind: 7 pages, presets, discipline config) | 1,124 |
| `UI/StingDockPanel.xaml` | — (WPF markup, 6-tab panel with ~413 buttons) | 1,634 |
| `UI/ProjectSetupWizard.xaml` | — (WPF markup, 7-page wizard dialog) | 793 |
| **Total** | **234 commands** | **~56,030** |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (202 lines)
- Entry point registered in `StingTools.addin` (FullClassName: `StingTools.Core.StingToolsApp`)
- Static properties: `AssemblyPath`, `DataPath` (set in `OnStartup`, relative to DLL location)
- Registers WPF dockable panel (`StingDockPanelProvider`) — the primary user interface
- Builds legacy ribbon tab "STING Tools" with `BuildSelectPanel`, `BuildDocsPanel`, `BuildTagsPanel`, `BuildOrganisePanel`, `BuildTempPanel` (retained for compatibility)
- Provides `FindDataFile(fileName)` — searches `DataPath` and subdirectories
- Provides `ParseCsvLine(line)` — CSV parser respecting quoted fields
- Contains `ToggleDockPanelCommand` — toggles the WPF dockable panel visibility

### `StingLog` (static) — `Core/StingLog.cs` (93 lines)
- Thread-safe file logger (`StingTools.log` alongside the DLL)
- Uses buffered `StreamWriter` with `FileShare.Read` for performance (replaces `File.AppendAllText`)
- Methods: `Info(msg)`, `Warn(msg)`, `Error(msg, ex?)`, `Shutdown()`
- `Shutdown()` flushes and closes the log file — wired to `OnShutdown` in `StingToolsApp`
- Error recovery: disposes bad writer on IO failure so next call retries with fresh stream
- Used throughout the codebase for error tracing; replaces silent catch blocks

### `ParamRegistry` (static) — `Core/ParamRegistry.cs` (1,048 lines)
- **Single source of truth** for all parameter names, GUIDs, container definitions, and category bindings
- Loads from `PARAMETER_REGISTRY.json` at runtime (thread-safe lazy initialization via `EnsureLoaded()` with lock); falls back to hardcoded defaults if JSON not found
- **Tag format configuration**: `Separator`, `NumPad`, `SegmentOrder` — data-driven rather than hardcoded
- **Typed string constants** for all 8 source tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ) + universal containers (TAG1-TAG7, TAG7A-TAG7F)
- **Extended parameter constants**: ~60+ parameters across identity, spatial, BLE dimensional, electrical, lighting, HVAC, and plumbing groups
- **GUID lookups**: `GetGuid(paramName)`, `GetParamName(guid)`, `AllParamGuids`
- **Container management**: `AllContainers`, `ContainersForCategory(categoryName)`, `GetContainerTuples()`
- **Token presets**: Named index arrays for partial tag strings
- **Tag assembly**: `AssembleContainer()`, `ReadTokenValues()`, `WriteContainers()`
- **Reload**: `Reload()` forces re-read from disk for live editing workflows

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs` (1,725 lines)
- `GetString(el, paramName)` — read text parameter, returns empty string on null
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
- `PopulateAll(doc, el, ctx, overwrite)` — populates all 9 tokens on a single element with guaranteed defaults
- Returns `PopulationResult` with granular counts (TokensSet, LocDetected, ZoneDetected, StatusDetected, RevSet, FamilyProdUsed)

### `NativeParamMapper` (static) — `Core/ParameterHelpers.cs`
- Maps 30+ Revit built-in parameters to STING shared parameters
- `MapAll(doc, el)` — comprehensive parameter mapping (dimensions, MEP data, identity)
- Bridges native Revit data (Width, Height, Flow, etc.) into STING parameter schema

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs` (500 lines)
- `ParamGuids` dictionary — 50+ parameter name to `Guid` mappings from `MR_PARAMETERS.txt`
- `UniversalParams` — 17 ASS_MNG parameters for Pass 1 (bound to all 53 categories)
- `AllCategories` / `AllCategoryEnums` — 53 `OST_*` built-in category names/enums
- `DisciplineBindings` — maps discipline-specific params to category subsets (Pass 2)
- `BuildCategorySet(doc, enums)` — type-safe category set builder
- Also declares `NumPad = 4` and `Separator = "-"` constants (duplicated in `TagConfig`)

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

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (1,707 lines)
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
| `StingCommandHandler` | `UI/StingCommandHandler.cs` | `IExternalEventHandler` — dispatches 400+ dockable panel button tags to 234 command classes + 64 inline helpers on the Revit API thread |
| `StingDockPanel` | `UI/StingDockPanel.xaml.cs` | WPF code-behind for 6-tab dockable panel (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW) with colour swatches and status bar |
| `StingDockPanelProvider` | `UI/StingDockPanelProvider.cs` | `IDockablePaneProvider` — registers dockable panel with Revit; PaneGuid for panel identification |
| `ColorHelper` | `Select/ColorCommands.cs` | 10 built-in colour palettes, `OverrideGraphicSettings` builder, solid fill pattern finder, preset save/load |
| `TagPlacementEngine` | `Tags/SmartTagPlacementCommand.cs` | 8-position candidate offset generation, scale-aware placement, 2D AABB collision detection, leader auto-generation |
| `TagPlacementPresets` | `Tags/SmartTagPlacementCommand.cs` | Per-category placement rules (`CategoryRule`), named presets (`PlacementPreset`), `LearnFromView` analysis |

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

1. **Load Parameters** (`LoadSharedParamsCommand`) — Bind 200+ shared parameters to 53 categories (2-pass: universal + discipline-specific)
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
| `StingDockPanel.xaml` | `UI/StingDockPanel.xaml` (1,357 lines) | WPF markup: 4-tab layout (SELECT/ORGANISE/CREATE/VIEW), colour swatches, bulk parameter controls |
| `StingDockPanel.xaml.cs` | `UI/StingDockPanel.xaml.cs` (178 lines) | Code-behind: button dispatch via `IExternalEventHandler`, colour swatch builder, status bar |
| `StingCommandHandler` | `UI/StingCommandHandler.cs` (1,341 lines) | `IExternalEventHandler` — maps 140+ button Tag strings to command classes, ensures Revit API calls run on the main thread |
| `StingDockPanelProvider` | `UI/StingDockPanelProvider.cs` (37 lines) | `IDockablePaneProvider` — registers panel with Revit, sets initial dock position (Right, 320×400 min) |
| `ToggleDockPanelCommand` | `Core/StingToolsApp.cs` (line 825) | `IExternalCommand` — toggles panel visibility from ribbon button |

### Panel Tabs

| Tab | Content | Mirrors Ribbon Panel |
|-----|---------|---------------------|
| SELECT | Category selectors, state selectors, spatial selectors, bulk parameter write | Select |
| ORGANISE | Tag operations, leader management, analysis/QA | Organise + Tags QA |
| CREATE | Tagging commands, token writers, combine, setup | Tags + Temp |
| VIEW | Document commands, viewports, template management | Docs + Temp Templates |

### Thread Safety Pattern
All button clicks dispatch through `IExternalEventHandler` to ensure Revit API calls execute on the correct thread:
```
Button Click → StingDockPanel.Cmd_Click → StingCommandHandler.SetCommand(tag) → ExternalEvent.Raise()
→ Revit calls StingCommandHandler.Execute(UIApplication) → RunCommand<T>(app)
```

## Template Manager Intelligence Engine

`TemplateManagerCommands.cs` (3,393 lines) provides a deep template automation engine with 17 commands and an `internal static class TemplateManager` (~867 lines) intelligence core.

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

### Template Manager Commands (17)

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
3. Copy `StingTools.dll` + `Newtonsoft.Json.dll` + `data/` folder to the assembly path
4. Restart Revit to load the plugin

### Branching

- The default branch is `main`
- Feature branches: `feature/<description>` or `claude/<session-id>`
- Always create feature branches from the latest `main`

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
2. **Multiple classes per file** — for related simple commands (e.g., `CategorySelectCommands.cs` has 15 selectors, `TokenWriterCommands.cs` has 7 commands, `TagOperationCommands.cs` has 26 commands)

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

- **Revit API**: `RevitAPI.dll`, `RevitAPIUI.dll` (referenced via `$(RevitApiPath)` — not distributed, `Private=false`)
- **Newtonsoft.Json**: v13.0.3 (NuGet package)
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
| **No cancellation support** | All batch commands | Once started, user must wait until completion — no abort mechanism | High |
| **Hardcoded category bindings** | `SharedParamGuids.cs:109-261` | 53 categories + discipline bindings hardcoded; adding a category requires code rebuild (BINDING_COVERAGE_MATRIX.csv exists but unused) | Medium |
| ~~**No error recovery**~~ | `MasterSetupCommand.cs` | **DONE** — Wrapped in `TransactionGroup` for atomic rollback. If critical step 1 (Load Params) fails, user can rollback immediately. Per-step timing reported. | Done |
| **Fixed tag format** | `TagConfig.cs:16-18` | `NumPad=4`, `Separator="-"` hardcoded — can't change segment count, order, or separator | Medium |
| **Partially unused data files** | `Data/` directory | Most files now loaded. MATERIAL_SCHEMA.json used by `SchemaValidateCommand`. BINDING_COVERAGE_MATRIX.csv used by `DynamicBindingsCommand`. VALIDAT_BIM_TEMPLATE.py ported to C# (`ValidateTemplateCommand`). Remaining unused: CATEGORY_BINDINGS.csv (10,661 entries), FAMILY_PARAMETER_BINDINGS.csv (4,686 entries) | Low |

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
| ~~**No dockable panel UI**~~ | **DONE** — WPF dockable panel (`UI/` directory, 4 files) with 4-tab interface (SELECT/ORGANISE/CREATE/VIEW), `IExternalEventHandler` dispatch for thread safety, colour swatches, bulk parameter controls. | Done |
| ~~Cross-parameter validation~~ | **DONE** — `ISO19650Validator` validates all tokens, cross-validates DISC/SYS against category, validates tag format. `FixDuplicateTagsCommand` auto-resolves duplicates. | Done |
| ~~Formula evaluation engine~~ | **DONE** — `FormulaEvaluatorCommand` + `FormulaEngine` reads 199 formulas from CSV, evaluates in dependency order (levels 0-6), supports arithmetic, conditionals, string concat, and Revit geometry inputs. | Done |
| ~~Family-stage pre-population~~ | **DONE** — `FamilyStagePopulateCommand` pre-populates all 7 tokens before tagging (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD). | Done |
| ~~Leader management commands~~ | **DONE** — 12 leader management commands: Toggle/Add/Remove Leaders, Align Tags, Reset Positions, Toggle Orientation, Snap Elbows, Flip Tags, Align Text, Pin/Unpin, Attach/Free, Select by Leader. | Done |
| ~~Tag register export~~ | **DONE** — `TagRegisterExportCommand` exports comprehensive 40+ column asset register (tags, identity, spatial, MEP, cost, validation) to CSV. | Done |
| ~~Full auto-populate pipeline~~ | **DONE** — `FullAutoPopulateCommand` runs Tokens, Dimensions, MEP, Formulas, Tags, Combine, Grid in one click with zero manual input. | Done |
| ~~Native parameter mapping~~ | **DONE** — `NativeParamMapper` maps 30+ Revit built-in parameters to STING shared parameters. | Done |
| ~~Document automation~~ | **DONE** — `DeleteUnusedViewsCommand`, `SheetNamingCheckCommand`, `AutoNumberSheetsCommand` for view cleanup and ISO 19650 sheet compliance. | Done |
| ~~Schedule field remapping~~ | **DONE** — `ScheduleHelper.LoadFieldRemaps()` loads SCHEDULE_FIELD_REMAP.csv; `BatchSchedulesCommand` auto-remaps deprecated field names. | Done |
| ~~Port VALIDAT_BIM_TEMPLATE.py (45 checks) to C# ValidateTemplateCommand~~ | **DONE** — `ValidateTemplateCommand` in `DataPipelineCommands.cs` performs 45 validation checks (data file inventory, parameter consistency, material completeness, formula dependencies, schedule definitions, cross-references). | Done |
| ~~Dynamic category bindings from BINDING_COVERAGE_MATRIX.csv~~ | **DONE** — `DynamicBindingsCommand` in `DataPipelineCommands.cs` loads bindings from CSV, replacing hardcoded `SharedParamGuids.AllCategoryEnums`. | Done |
| ~~Color By Parameter system~~ | **DONE** — `ColorCommands.cs` with 5 commands: ColorByParameter (10 palettes, `<No Value>` detection), ClearOverrides, SavePreset, LoadPreset, CreateFiltersFromColors. Full `OverrideGraphicSettings` support. | Done |
| ~~Smart Tag Placement~~ | **DONE** — `SmartTagPlacementCommand.cs` with 4 commands: SmartPlace (8-position collision avoidance), Arrange, RemoveAnnotation, BatchPlace. `TagPlacementEngine` with scale-aware offsets and 2D AABB collision detection. | Done |
| ~~View automation commands~~ | **DONE** — `ViewAutomationCommands.cs` with 6 commands: DuplicateView, BatchRename, CopyViewSettings, AutoPlaceViewports, CropToContent, BatchAlignViewports. | Done |
| ~~Annotation color management~~ | **DONE** — 5 commands in `TagOperationCommands.cs`: ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors. | Done |
| ~~Schema validation~~ | **DONE** — `SchemaValidateCommand` validates BLE/MEP CSV columns match MATERIAL_SCHEMA.json (77-column schema). | Done |
| ~~Schedule management system~~ | **DONE** — `ScheduleEnhancementCommands.cs` (1,579 lines) with 9 commands: Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report. Plus ScheduleAutoFit, MatchWidest (functional), ToggleHidden inline operations. `ScheduleAuditHelper` engine loads CSV definitions for cross-reference. | Done |
| Configurable tag format in project_config.json (separator, padding, segments) | Flexibility for different standards | Medium |
| Batch command chaining / workflow presets | Queue: AutoTag, Validate, Export | Low |

---

### Color By Parameter (Graitec Lookup Style) — IMPLEMENTED

Inspired by GRAITEC PowerPack Element Lookup, Naviate Color Elements (Symetri), BIM One Color Splasher (open-source), DiRoots OneFilter Visualize, ModPlus mprColorizer, and Future BIM Colors by Parameters. Provides element coloring by any parameter value with full graphic control. Implemented in `Select/ColorCommands.cs` (863 lines) with `ColorHelper` engine and 5 commands.

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

Inspired by BIMLOGiQ Smart Annotation, Naviate Tag from Template, and academic Automatic Label Placement (ALP) research. Provides collision-free automated tag annotation. Implemented in `Tags/SmartTagPlacementCommand.cs` (911 lines) with `TagPlacementEngine` engine and 4 commands.

#### Critical Distinction: Data Tagging vs Visual Tagging

| Layer | What It Does | Current State |
|-------|-------------|---------------|
| Data tagging | Writes DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ to element parameters | Implemented (AutoTag, BatchTag, TagAndCombine, FullAutoPopulate) |
| Visual tagging | Creates `IndependentTag` annotations in views displaying tag values | **Implemented** (SmartPlaceTags, ArrangeTags, RemoveAnnotationTags, BatchPlaceTags) |

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
| `CATEGORY_BINDINGS.csv` | 10,661 | **Not yet loaded** | Replace hardcoded `DisciplineBindings` — data-driven parameter binding |
| `FAMILY_PARAMETER_BINDINGS.csv` | 4,686 | **Not yet loaded** | Family-level parameter validation and auto-binding |
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
10. **Leader management** — 12 annotation leader commands
11. **Tag register export** — 40+ column comprehensive CSV export
12. **Full auto-populate pipeline** — Zero-input one-click automation
13. **Native parameter mapping** — 30+ Revit built-in to STING parameter mappings
14. **Family-stage pre-population** — All 7 tokens from category/spatial/family data
15. **Schedule field remapping** — Auto-remap deprecated field names from CSV
16. **WPF dockable panel** — 4-tab panel (SELECT/ORGANISE/CREATE/VIEW) replicating pyRevit interface with IExternalEventHandler dispatch
17. **Template Manager intelligence engine** — 5-layer auto-assignment, compliance scoring, VG diff, 17 template automation commands
18. **View templates expanded** — 23 template definitions with full VG configuration (discipline plans, coordination, RCP, presentation, sections, 3D, elevations)
19. **Style definition commands** — Fill patterns, line styles, text styles, dimension styles, object styles created programmatically

#### Completed (Phase 4)

20. **Color By Parameter system** — `ColorCommands.cs`: 5 commands, 10 palettes, preset management, filter generation
21. **Smart Tag Placement** — `SmartTagPlacementCommand.cs`: 4 commands, `TagPlacementEngine` with 8-position collision avoidance
22. **Dynamic category bindings** — `DynamicBindingsCommand` loads from BINDING_COVERAGE_MATRIX.csv
23. **Port VALIDAT_BIM_TEMPLATE.py** — `ValidateTemplateCommand`: 45 validation checks ported to C#
24. **View automation** — `ViewAutomationCommands.cs`: 6 commands (Duplicate, BatchRename, CopySettings, AutoPlace, Crop, BatchAlign)
25. **Annotation color management** — 5 new commands in `TagOperationCommands.cs` (ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors)
26. **Schema validation** — `SchemaValidateCommand` validates MATERIAL_SCHEMA.json against CSV data

#### Completed (Phase 5)

27. **Schedule management system** — `ScheduleEnhancementCommands.cs` with 9 commands (Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report) + `ScheduleAuditHelper` engine + enhanced MatchWidest, AutoFit, ToggleHidden inline operations

#### Next Priorities

28. **Configurable tag format** — Separator, padding, segments via project_config.json
29. **Cancellation support** — Background worker with abort for batch operations
30. **Dynamic discipline bindings** — Load CATEGORY_BINDINGS.csv (10,661 entries) to replace hardcoded `DisciplineBindings`
31. **Family parameter auto-binding** — Load FAMILY_PARAMETER_BINDINGS.csv (4,686 entries) for family-level validation
32. **Batch command chaining / workflow presets** — Queue: AutoTag, Validate, Export

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
- [The Building Coder — Tag Extents](https://thebuildingcoder.typepad.com/blog/2022/07/tag-extents-and-lazy-detail-components.html) — Getting accurate tag bounding boxes
- [Revit API — OverrideGraphicSettings](https://www.revitapidocs.com/2025/eb2bd6b6-b7b2-5452-2070-2dbadb9e068a.htm) — Complete graphic override API
- [Revit API — IndependentTag](https://www.revitapidocs.com/2025/e52073e2-9d98-6fb5-eb43-288cf9ed2e28.htm) — Tag creation and positioning API
