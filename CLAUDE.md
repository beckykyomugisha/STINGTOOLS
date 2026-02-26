# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **39 C# source files** (~17,900 lines of code) across 7 directories
- **120 `IExternalCommand` classes** (commands) + 1 `IExternalApplication` entry point
- **15 runtime data files** (CSV, JSON, TXT, XLSX, PY)
- **5 ribbon panels** with 24 pulldown groups

## Technology Stack

- **Platform**: Autodesk Revit 2025/2026/2027 (BIM software)
- **Language**: C# / .NET 8.0 (`net8.0-windows`)
- **Plugin type**: `IExternalApplication` with `IExternalCommand` classes
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
    ├── Core/                           # Shared infrastructure (5 files, ~3,800 lines)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI builder (5 panels, 19 pulldowns)
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error)
    │   ├── ParameterHelpers.cs         # Parameter read/write utilities + SpatialAutoDetect + NativeParamMapper
    │   ├── SharedParamGuids.cs         # GUID map for 200+ shared parameters
    │   └── TagConfig.cs                # ISO 19650 tag lookup tables + tag builder + TagIntelligence + ISO19650Validator
    │
    ├── Select/                         # Element selection commands (3 files, 28 commands)
    │   ├── CategorySelectCommands.cs   # 14 category selectors + SelectAllTaggable + CategorySelector helper
    │   ├── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite
    │   └── ColorByParameterCommands.cs # ColorByParameter, ClearColorOverrides, SavePreset,
    │                                   #   LoadPreset, CreateFiltersFromColors + helper classes
    │
    ├── Docs/                           # Documentation commands (7 files, 17 commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   ├── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │   ├── DocAutomationCommands.cs    # DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets
    │   └── ViewAutomationCommands.cs   # BatchAlignViewports, DuplicateView, BatchRenameViews,
    │                                   #   CopyViewSettings, AutoPlaceViewports, CropToContent
    │
    ├── Tags/                           # Tagging commands (12 files, 22 commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view + TagNewOnly
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagAndCombineCommand.cs     # One-click: populate + tag + combine all
    │   ├── CombineParametersCommand.cs # Interactive multi-container combine (16 groups, 36 params)
    │   ├── ConfigEditorCommand.cs      # View/edit/save project_config.json
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   ├── TokenWriterCommands.cs      # SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers,
    │   │                               #   BuildTags, CompletenessDashboard + TokenWriter helper
    │   ├── ValidateTagsCommand.cs      # Validate tag completeness with ISO 19650 codes
    │   ├── PreTagAuditCommand.cs       # Dry-run audit: predict tags, collisions, ISO violations
    │   ├── FamilyStagePopulateCommand.cs # Pre-populate all 7 tokens from category/spatial data
    │   └── SmartTagPlacementCommand.cs # SmartTagPlacement, ArrangeTags, BatchTagViews
    │                                   #   + SmartPlacementEngine (scoring, collision avoidance)
    │
    ├── Organise/                       # Tag management commands (1 file, 26 commands)
    │   └── TagOperationCommands.cs     # TagSelected, ReTag, FixDuplicates, DeleteTags, Renumber,
    │                                   #   AuditCSV, FindDuplicates, HighlightInvalid, ClearOverrides,
    │                                   #   CopyTags, SwapTags, SelectByDiscipline, TagStats,
    │                                   #   TagRegisterExport, AddLeaders, RemoveLeaders, ToggleLeaders,
    │                                   #   AlignTags, ResetTagPositions, ToggleTagOrientation,
    │                                   #   SelectTagsWithLeaders, SnapLeaderElbow, FlipTags,
    │                                   #   AlignTagText, PinTags, AttachLeader
    │
    ├── Temp/                           # Template commands (10 files, 31 commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MasterSetupCommand.cs       # One-click full project setup (10 steps)
    │   ├── MaterialCommands.cs         # BLE + MEP material creation + MaterialPropertyHelper
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types + CompoundTypeCreator
    │   ├── ScheduleCommands.cs         # Batch schedules, FullAutoPopulate, auto-populate, CSV export
    │   ├── TemplateCommands.cs         # Filters, worksets, view templates
    │   ├── TemplateExtCommands.cs      # Line patterns, phases, apply filters,
    │   │                               #   cable trays, conduits, material schedules
    │   ├── FormulaEvaluatorCommand.cs  # Evaluate 199 formulas from CSV + FormulaEngine
    │   └── DataPipelineCommands.cs     # ValidateTemplate (45 checks), ValidateBindings,
    │                                   #   ConfigureTagFormat + TemplateValidator + DynamicBindingLoader
    │
    └── Data/                           # Runtime data files (15 files)
        ├── BLE_MATERIALS.csv           # 815 building-element materials
        ├── MEP_MATERIALS.csv           # 464 MEP materials
        ├── MR_PARAMETERS.txt           # Shared parameter file (200+ params)
        ├── MR_PARAMETERS.csv           # Parameter definitions
        ├── MR_SCHEDULES.csv            # 168 schedule definitions
        ├── MATERIAL_SCHEMA.json        # 77-column material schema (v2.3)
        ├── FORMULAS_WITH_DEPENDENCIES.csv  # 199 parameter formulas (now consumed by FormulaEvaluator)
        ├── SCHEDULE_FIELD_REMAP.csv    # 50 field deprecation remaps (consumed by BatchSchedules)
        ├── BINDING_COVERAGE_MATRIX.csv # Parameter-category coverage
        ├── CATEGORY_BINDINGS.csv       # 10,661 category bindings
        ├── FAMILY_PARAMETER_BINDINGS.csv   # 4,686 family bindings
        ├── PARAMETER__CATEGORIES.csv   # Parameter-category cross-reference
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide
        └── VALIDAT_BIM_TEMPLATE.py     # BIM template validation (45 checks)
```

## Ribbon UI Architecture

The plugin creates a single **"STING Tools"** ribbon tab with five panels and 24 pulldown groups:

### Select Panel (4 pulldowns + 1 button)
| Group | Commands | Description |
|-------|----------|-------------|
| Category | 15 selectors (Lighting, Electrical, Mechanical, Plumbing, Air Terminals, Furniture, Doors, Windows, Rooms, Sprinklers, Pipes, Ducts, Conduits, Cable Trays, ALL Taggable) | Select elements by Revit category in active view |
| State | Untagged, Tagged, Empty Mark, Pinned, Unpinned | Select by tag/pin/mark state |
| Spatial | By Level, By Room | Select by spatial criteria |
| Bulk Param | `Select.BulkParamWriteCommand` | Multi-page bulk operations: set LOC/ZONE/STATUS, auto-populate all tokens, clear tags, or re-tag with overwrite |
| Color | ColorByParameter, ClearColorOverrides, SavePreset, LoadPreset, CreateFiltersFromColors | Color elements by parameter value with palettes, presets, and view filter generation |

### Docs Panel (4 buttons + 3 pulldowns)
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

**Views pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Duplicate View | `Docs.DuplicateViewCommand` | Manual | Duplicate active view with filters, overrides, and visibility |
| Batch Rename Views | `Docs.BatchRenameViewsCommand` | Manual | Add/remove prefixes or change case on all view names |
| Copy View Settings | `Docs.CopyViewSettingsCommand` | Manual | Copy filters and overrides from active view to all same-type views |
| Batch Align Viewports | `Docs.BatchAlignViewportsCommand` | Manual | Align viewports across ALL sheets (top/left/center) |
| Auto-Place Viewports | `Docs.AutoPlaceViewportsCommand` | Manual | Grid-based intelligent viewport placement on active sheet |
| Crop to Content | `Docs.CropToContentCommand` | Manual | Auto-crop view boundaries to element extents with padding |

**Automation pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Delete Unused Views | `Docs.DeleteUnusedViewsCommand` | Manual | Remove views not placed on any sheet (with confirmation and protection) |
| Sheet Naming Check | `Docs.SheetNamingCheckCommand` | ReadOnly | ISO 19650 sheet naming compliance audit with correction suggestions |
| Auto-Number Sheets | `Docs.AutoNumberSheetsCommand` | Manual | Sequentially renumber sheets within discipline groups |

### Tags Panel (3 buttons + 5 pulldowns)
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
| Pre-Tag Audit | `Tags.PreTagAuditCommand` | ReadOnly | Dry-run audit: predict tag assignments, collisions, ISO violations before committing |
| Family-Stage Populate | `Tags.FamilyStagePopulateCommand` | Manual | Pre-populate all 7 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) from category and spatial data |

**Placement pulldown (Smart Tag Placement):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Smart Tag Placement | `Tags.SmartTagPlacementCommand` | Manual | Place annotation tags with collision avoidance (8-quadrant scoring) |
| Arrange Tags | `Tags.ArrangeTagsCommand` | Manual | Reposition existing tags to resolve overlaps |
| Batch Tag Views | `Tags.BatchTagViewsCommand` | Manual | Place annotation tags across all floor plan views |

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

### Organise Panel (3 pulldowns)
**Tag Ops pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Selected | `Organise.TagSelectedCommand` | Manual | Tag selected elements only |
| Delete Tags | `Organise.DeleteTagsCommand` | Manual | Clear all 15 tag params from selection (with confirmation) |
| Renumber | `Organise.RenumberTagsCommand` | Manual | Re-sequence tags within (DISC, SYS, LVL) groups |
| Copy Tags | `Organise.CopyTagsCommand` | Manual | Copy tag values from first selected to all others (excludes SEQ) |
| Swap Tags | `Organise.SwapTagsCommand` | Manual | Swap all tag values between exactly 2 selected elements |
| Re-Tag | `Organise.ReTagCommand` | Manual | Force re-derive and overwrite all tag tokens on selected elements |
| Fix Duplicates | `Organise.FixDuplicateTagsCommand` | Manual | Auto-resolve duplicate tags by incrementing SEQ numbers |

**Leaders pulldown (tag annotation/placement):**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Toggle Leaders | `Organise.ToggleLeadersCommand` | Manual | Toggle leaders on/off for selected tags (or all in view) |
| Add Leaders | `Organise.AddLeadersCommand` | Manual | Add leaders to selected annotation tags |
| Remove Leaders | `Organise.RemoveLeadersCommand` | Manual | Remove leaders from selected annotation tags |
| Align Tags | `Organise.AlignTagsCommand` | Manual | Align tag heads horizontally, vertically, or in a row |
| Reset Tag Positions | `Organise.ResetTagPositionsCommand` | Manual | Move tags back to element centers |
| Toggle Orientation | `Organise.ToggleTagOrientationCommand` | Manual | Switch tags between horizontal and vertical |
| Snap Leader Elbows | `Organise.SnapLeaderElbowCommand` | Manual | Snap leader elbows to 45/90 degree angles |
| Flip Tags | `Organise.FlipTagsCommand` | Manual | Mirror tag position across element center |
| Align Tag Text | `Organise.AlignTagTextCommand` | Manual | Align annotation text (left/center/right) |
| Pin/Unpin Tags | `Organise.PinTagsCommand` | Manual | Lock/unlock tags to prevent accidental movement |
| Attach/Free Leader | `Organise.AttachLeaderCommand` | Manual | Attach leader end to host element or set free |
| Select Tags By Leader | `Organise.SelectTagsWithLeadersCommand` | ReadOnly | Select tags with or without leaders in view |

**Analysis pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Audit to CSV | `Organise.AuditTagsCSVCommand` | ReadOnly | Export full tag audit to CSV file |
| Select by Discipline | `Organise.SelectByDisciplineCommand` | ReadOnly | Select all elements of a specific discipline code |
| Tag Statistics | `Organise.TagStatsCommand` | ReadOnly | Quick tag counts by discipline/system/level for active view |
| Tag Register Export | `Organise.TagRegisterExportCommand` | ReadOnly | Comprehensive asset register export (40+ columns: tags, identity, spatial, MEP, cost, validation) |

### Temp Panel (6 pulldown groups, 31 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup** | Project setup + one-click automation (10-step workflow) |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes (FamilyCommands.cs), Cable Trays, Conduits (TemplateExtCommands.cs) | Type creation from CSV data (8 commands) |
| Schedules | **Full Auto-Populate**, Batch Create, Material Takeoffs, Auto-Populate (Tokens Only), Evaluate Formulas, Export CSV | Schedule management + zero-input automation pipeline (6 commands) |
| Validate | **Validate BIM Template** (45 checks), Validate Bindings, Configure Tag Format | Data pipeline validation and tag format configuration (3 commands) |
| Templates | Create Filters, Apply Filters to Views, Create Worksets, View Templates, Line Patterns, Phases | 10 multi-category discipline filters, 32 AEC UK worksets, 15 view templates, 10 ISO 128 line patterns, 6 phases |

## Command Count by File

| File | Commands | Lines |
|------|----------|-------|
| `Select/CategorySelectCommands.cs` | 15 (14 category selectors + SelectAllTaggable) | 168 |
| `Select/StateSelectCommands.cs` | 8 (5 state + 2 spatial + BulkParamWrite) | 407 |
| `Select/ColorByParameterCommands.cs` | 5 (ColorByParam, ClearOverrides, SavePreset, LoadPreset, CreateFilters) | 887 |
| `Docs/SheetOrganizerCommand.cs` | 1 | 100 |
| `Docs/ViewOrganizerCommand.cs` | 1 | 91 |
| `Docs/SheetIndexCommand.cs` | 1 | 75 |
| `Docs/TransmittalCommand.cs` | 1 | 93 |
| `Docs/ViewportCommands.cs` | 4 (Align, Renumber, TextCase, SumAreas) | 304 |
| `Docs/DocAutomationCommands.cs` | 3 (DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets) | 436 |
| `Docs/ViewAutomationCommands.cs` | 6 (BatchAlignVP, DuplicateView, BatchRename, CopySettings, AutoPlace, CropToContent) | 622 |
| `Tags/AutoTagCommand.cs` | 2 (AutoTag, TagNewOnly) | 304 |
| `Tags/BatchTagCommand.cs` | 1 | 199 |
| `Tags/TagAndCombineCommand.cs` | 1 | 324 |
| `Tags/CombineParametersCommand.cs` | 1 | 511 |
| `Tags/ConfigEditorCommand.cs` | 1 | 194 |
| `Tags/TagConfigCommand.cs` | 1 | 72 |
| `Tags/LoadSharedParamsCommand.cs` | 1 | 158 |
| `Tags/TokenWriterCommands.cs` | 7 (SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, CompletenessDashboard) | 327 |
| `Tags/ValidateTagsCommand.cs` | 1 | 249 |
| `Tags/PreTagAuditCommand.cs` | 1 | 351 |
| `Tags/FamilyStagePopulateCommand.cs` | 1 | 275 |
| `Tags/SmartTagPlacementCommand.cs` | 3 (SmartTagPlacement, ArrangeTags, BatchTagViews) + SmartPlacementEngine | 728 |
| `Organise/TagOperationCommands.cs` | 26 (13 tag ops + 13 leader/annotation) | 2,096 |
| `Temp/CreateParametersCommand.cs` | 1 | 27 |
| `Temp/CheckDataCommand.cs` | 1 | 101 |
| `Temp/MasterSetupCommand.cs` | 1 | 220 |
| `Temp/MaterialCommands.cs` | 2 (BLE, MEP) | 239 |
| `Temp/FamilyCommands.cs` | 6 (Walls, Floors, Ceilings, Roofs, Ducts, Pipes) | 658 |
| `Temp/ScheduleCommands.cs` | 4 (BatchSchedules, FullAutoPopulate, AutoPopulate, ExportCSV) | 1,266 |
| `Temp/TemplateCommands.cs` | 3 (Filters, Worksets, ViewTemplates) | 570 |
| `Temp/TemplateExtCommands.cs` | 6 (LinePatterns, Phases, ApplyFilters, CableTrays, Conduits, MaterialSchedules) | 302 |
| `Temp/FormulaEvaluatorCommand.cs` | 1 + FormulaEngine helper | 765 |
| `Temp/DataPipelineCommands.cs` | 3 (ValidateTemplate, ValidateBindings, ConfigureTagFormat) + TemplateValidator + DynamicBindingLoader | 863 |
| **Total** | **120 commands** | **~17,900** |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (678 lines)
- Entry point registered in `StingTools.addin` (FullClassName: `StingTools.Core.StingToolsApp`)
- Static properties: `AssemblyPath`, `DataPath` (set in `OnStartup`, relative to DLL location)
- Builds ribbon tab "STING Tools" with `BuildSelectPanel`, `BuildDocsPanel`, `BuildTagsPanel`, `BuildOrganisePanel`, `BuildTempPanel`
- Uses `PulldownButton` groups for all panel sub-menus (19 pulldowns)
- Provides `FindDataFile(fileName)` — searches `DataPath` and subdirectories
- Provides `ParseCsvLine(line)` — CSV parser respecting quoted fields

### `StingLog` (static) — `Core/StingLog.cs`
- Thread-safe file logger (`StingTools.log` alongside the DLL)
- Methods: `Info(msg)`, `Warn(msg)`, `Error(msg, ex?)`
- Used throughout the codebase for error tracing; replaces silent catch blocks
- Last-resort: silently swallows its own IO failures

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs` (972 lines)
- `GetString(el, paramName)` — read text parameter, returns empty string on null
- `SetString(el, paramName, value, overwrite)` — write text parameter, skips read-only/non-empty unless overwrite
- `SetIfEmpty(el, paramName, value)` — set only when currently empty
- `GetLevelCode(doc, el)` — derives short level codes (L01, GF, B1, RF, XX)
- `GetCategoryName(el)` — safe category name retrieval
- `GetFamilyName(el)` — safe family name retrieval

### `SpatialAutoDetect` (internal static) — `Core/ParameterHelpers.cs`
- `BuildRoomIndex(doc)` — builds spatial index of rooms for O(1) element-to-room lookups
- `DetectProjectLoc(doc)` — detects default LOC from Project Information
- `DetectLoc(doc, el, roomIndex, projectLoc)` — auto-derives LOC from room name/number/project info
- `DetectZone(doc, el, roomIndex)` — auto-derives ZONE from room department/name patterns
- Integrated into TagAndCombine, AutoPopulate, TagNewOnly, FamilyStagePopulate, PreTagAudit

### `NativeParamMapper` (internal static) — `Core/ParameterHelpers.cs`
- Maps Revit built-in parameters to custom parameter names for compatibility and fallback logic
- Used by FullAutoPopulate for dimension and MEP parameter mapping

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs` (426 lines)
- `ParamGuids` dictionary — 50+ parameter name → `Guid` mappings from `MR_PARAMETERS.txt`
- `UniversalParams` — 17 ASS_MNG parameters for Pass 1 (bound to all 53 categories)
- `AllCategories` / `AllCategoryEnums` — 53 `OST_*` built-in category names/enums
- `DisciplineBindings` — maps discipline-specific params to category subsets (Pass 2)
- `BuildCategorySet(doc, enums)` — type-safe category set builder
- Also declares `NumPad = 4` and `Separator = "-"` constants (duplicated in `TagConfig`)

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (~1,700 lines)
- **Lookup tables** (all configurable via `project_config.json`):
  - `DiscMap` — 41 category → discipline code mappings (M, E, P, A, S, FP, LV, G)
  - `SysMap` — 17 system codes → category lists (HVAC, DCW, DHW, HWS, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN)
  - `ProdMap` — 41 category → product codes
  - `FuncMap` — 16 system → function code mappings
  - `LocCodes` — location codes (BLD1, BLD2, BLD3, EXT, XX)
  - `ZoneCodes` — zone codes (Z01-Z04, ZZ, XX)
- **Configuration management**: `LoadFromFile(path)`, `LoadDefaults()`, `ConfigSource`
- **Tag operations** (7 intelligence layers):
  - `TagIsComplete(tagValue, expectedTokens=8)` — validates 8-segment tag completeness
  - `BuildAndWriteTag(doc, el, seqCounters, skipComplete, existingTags, collisionMode, stats)` — shared tagging logic with collision mode, stats tracking, and cross-validation
  - `GetExistingSequenceCounters(doc)` — scans project for highest SEQ per group
  - `BuildExistingTagIndex(doc)` — builds HashSet of all existing tags for O(1) collision detection
  - `GetSysCode(categoryName)`, `GetFuncCode(sysCode)` — reverse lookups
  - `GetMepSystemAwareSysCode(el, categoryName)` — derives SYS from connected MEP system name (supply air, hot water, etc.) before falling back to category
  - `GetFamilyAwareProdCode(el, categoryName)` — family-name-aware PROD code resolution (35+ specific codes)
  - `GetViewRelevantDisciplines(view)` — inspects view name, template, and VG to determine which disciplines to tag
  - `FilterByViewDisciplines(elements, disciplines)` — filters elements to only view-relevant disciplines
- **Constants**: `NumPad = 4`, `Separator = "-"`

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

### `TagIntelligence` (static) — `Core/TagConfig.cs`
- Advanced tagging logic layer providing cross-parameter validation
- DISC/SYS/PROD/FUNC consistency checking
- System imbalance detection (e.g., supply vs return terminal count mismatches)
- Tag namespace isolation for multi-building projects

### `FormulaEngine` (internal static) — `Temp/FormulaEvaluatorCommand.cs`
- **FormulaDefinition** — parsed formula with discipline, parameter name, data type, expression, dependency level, builtin inputs
- **ExpressionParser** — recursive descent parser supporting:
  - Arithmetic: `+`, `-`, `*`, `/`, `^` (power)
  - Functions: `if(condition, trueValue, falseValue)`, `log()`
  - Comparison: `<`, `<=`, `>`, `>=`
  - String concatenation: `"literal" + paramName`
  - Builtin geometry: Width, Height, Length, Diameter, Thickness
- `LoadFormulas(csvPath)` — loads 199 formulas from FORMULAS_WITH_DEPENDENCIES.csv
- `BuildContext(el, formula)` — builds evaluation context from element parameters
- `EvaluateText()` / `EvaluateNumeric()` — evaluates expressions
- `WriteNumericResult()` — writes to empty parameters only (safe mode)

### Internal Helper Classes

| Helper Class | Location | Purpose |
|--------------|----------|---------|
| `CategorySelector` | `Select/CategorySelectCommands.cs` | `SelectByCategory()` — shared logic for all 15 category selection commands |
| `TokenWriter` | `Tags/TokenWriterCommands.cs` | Encapsulates LOC/ZONE/STATUS token writing and number assignment logic |
| `CompoundTypeCreator` | `Temp/FamilyCommands.cs` | Creates compound wall/floor/ceiling/roof/duct/pipe types from CSV data; `ElementKind` enum; applies material properties |
| `MaterialPropertyHelper` | `Temp/MaterialCommands.cs` | Shared material property-setting logic for BLE and MEP material commands |
| `SpatialAutoDetect` | `Core/ParameterHelpers.cs` | Auto-derives LOC from Room name/number/Project Info and ZONE from Room Department/name patterns |
| `NativeParamMapper` | `Core/ParameterHelpers.cs` | Maps Revit built-in parameters to custom parameter names for compatibility |
| `FormulaEngine` | `Temp/FormulaEvaluatorCommand.cs` | Expression parser and evaluator for 199 CSV-defined formulas |
| `SmartPlacementEngine` | `Tags/SmartTagPlacementCommand.cs` | Priority-based tag placement with AABB collision detection |
| `ColorByParameterHelper` | `Select/ColorByParameterCommands.cs` | Parameter collection, value grouping, preset management |
| `ColorPalettes` | `Select/ColorByParameterCommands.cs` | 9 built-in color palettes (Discipline, RAG, Spectral, etc.) |
| `TemplateValidator` | `Temp/DataPipelineCommands.cs` | 45-check BIM template validation engine (port of Python script) |
| `DynamicBindingLoader` | `Temp/DataPipelineCommands.cs` | Loads CATEGORY_BINDINGS.csv and BINDING_COVERAGE_MATRIX.csv |

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
- For new commands, use the shared helpers: `TagConfig.BuildAndWriteTag()`, `ParameterHelpers.SetIfEmpty()`, `CategorySelector.SelectByCategory()`

### Multi-file Command Patterns

The codebase uses two patterns for organising commands:
1. **One class per file** — for complex commands (e.g., `CombineParametersCommand.cs`, `MasterSetupCommand.cs`)
2. **Multiple classes per file** — for related simple commands (e.g., `CategorySelectCommands.cs` has 15 selectors, `TokenWriterCommands.cs` has 7 commands, `TagOperationCommands.cs` has 26 commands)

When adding new commands, follow the existing pattern for the directory. Use shared `internal static` helper classes (e.g., `CategorySelector`, `TokenWriter`, `CompoundTypeCreator`, `MaterialPropertyHelper`, `SpatialAutoDetect`, `NativeParamMapper`, `FormulaEngine`) to reduce duplication.

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
- **WPF**: Enabled (`UseWPF=true` in csproj) for `System.Windows.Media.Imaging`
- **Output**: Library (DLL), `AppendTargetFrameworkToOutputPath=false`, `CopyLocalLockFileAssemblies=true`
- **Assembly**: v1.0.0.0, GUID `A1B2C3D4-5678-9ABC-DEF0-123456789ABC`, Vendor: StingBIM
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` copied to output `data/` directory at build time

---

## Automation Gap Analysis & Feature Roadmap

### Current Automation Gaps

#### A. Gaps That Hinder Full Automation

| Gap | Location | Problem | Impact |
|-----|----------|---------|--------|
| ~~**No tag collision detection**~~ | `TagConfig.cs` | **FIXED** — `BuildAndWriteTag` now accepts an `existingTags` HashSet for O(1) collision detection; auto-increments SEQ on duplicate. | Done |
| ~~**No progress reporting**~~ | `BatchTagCommand`, `MasterSetupCommand` | **FIXED** — BatchTag shows element count upfront, logs every 500 elements, reports duration. MasterSetup reports per-step timing. | Done |
| **No cancellation support** | All batch commands | Once started, user must wait until completion — no abort mechanism | High |
| **Hardcoded category bindings** | `SharedParamGuids.cs:109-261` | 53 categories + discipline bindings hardcoded; adding a category requires code rebuild (BINDING_COVERAGE_MATRIX.csv exists but unused) | Medium |
| ~~**No error recovery**~~ | `MasterSetupCommand.cs` | **FIXED** — Wrapped in `TransactionGroup` for atomic rollback. | Done |
| **Fixed tag format** | `TagConfig.cs:16-18` | `NumPad=4`, `Separator="-"` hardcoded — can't change segment count, order, or separator | Medium |
| **Unused data files** | `Data/` directory | MATERIAL_SCHEMA.json, BINDING_COVERAGE_MATRIX.csv, CATEGORY_BINDINGS.csv (10,661 entries), VALIDAT_BIM_TEMPLATE.py (45 checks) still unused | Medium |

#### B. Enhancement Opportunities

| Enhancement | Status | Notes |
|-------------|--------|-------|
| Pre-tagging audit | **Done** | `PreTagAuditCommand` — full dry-run with CSV export |
| Tag collision auto-fix | **Done** | `BuildAndWriteTag` auto-increments SEQ; `TagCollisionMode` enum |
| LOC/ZONE auto-detection | **Done** | `SpatialAutoDetect` class in `ParameterHelpers.cs` |
| Family-aware PROD codes | **Done** | `TagConfig.GetFamilyAwareProdCode()` — 35+ specific codes |
| TagAndCombine all 36 containers | **Done** | Writes all 36 containers (6 universal + 30 discipline-specific) |
| Incremental tagging mode | **Done** | `TagNewOnlyCommand` pre-filters to empty ASS_TAG_1_TXT |
| CompoundTypeCreator material properties | **Done** | Applies color, transparency, smoothness, shininess from CSV |
| Cross-parameter validation | **Done** | `ISO19650Validator` + `TagIntelligence` classes |
| Family-stage pre-population | **Done** | `FamilyStagePopulateCommand` — all 7 tokens before tagging |
| Formula evaluation engine | **Done** | `FormulaEvaluatorCommand` — 199 formulas from CSV with expression parser |
| Full auto-populate pipeline | **Done** | `FullAutoPopulateCommand` — zero-input 6-step pipeline |
| Tag register export | **Done** | `TagRegisterExportCommand` — 40+ column comprehensive export |
| Tag leader management | **Done** | 12 commands for annotation tag leaders and positioning |
| Delete unused views | **Done** | `DeleteUnusedViewsCommand` with safety confirmation |
| ISO 19650 sheet naming | **Done** | `SheetNamingCheckCommand` with correction suggestions |
| Auto-number sheets | **Done** | `AutoNumberSheetsCommand` by discipline groups |
| Schedule field remap | **Done** | `BatchSchedulesCommand` now loads SCHEDULE_FIELD_REMAP.csv |
| Configurable tag format | Planned | Separator, padding, segments in project_config.json |
| Port VALIDAT_BIM_TEMPLATE.py | Planned | 45 validation checks as C# command |
| Dynamic category bindings from CSV | Planned | Replace hardcoded `SharedParamGuids.AllCategoryEnums` |
| Batch command chaining / workflow presets | Planned | Queue: AutoTag → Validate → Export |

---

### New Feature: Color By Parameter (Graitec Lookup Style)

Inspired by GRAITEC PowerPack Element Lookup, Naviate Color Elements (Symetri), BIM One Color Splasher (open-source), DiRoots OneFilter Visualize, ModPlus mprColorizer, and Future BIM Colors by Parameters. Provides element coloring by any parameter value with full graphic control.

#### Proposed Commands

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
| Warm | Red→Orange→Yellow→Cream | Heat/load mapping |
| Cool | Navy→Blue→Cyan→Mint | Cooling/flow mapping |
| Pastel | Soft muted tones | Presentation views |
| High Contrast | Saturated primaries + black outlines | QA/checking |
| Accessible | Colorblind-safe (viridis-like) | Universal accessibility |
| Custom | User-defined | Project-specific |

5. **Preset Management**: Save/load/delete named presets, export/import between projects
6. **Legend Generation**: Auto-create a color legend (drafting view or schedule) showing parameter value → color mapping
7. **`<No Value>` Detection**: Elements with empty/null parameter values highlighted in distinct color (red) for instant QA
8. **View Filter Generation**: One-click conversion of color scheme into persistent Revit `ParameterFilterElement` rules

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

### New Feature: Smart Tag Placement

Inspired by BIMLOGiQ Smart Annotation, Naviate Tag from Template, and academic Automatic Label Placement (ALP) research. Goal: perfect, collision-free automated tag annotation.

#### Critical Distinction: Data Tagging vs Visual Tagging

| Layer | What It Does | Current State |
|-------|-------------|---------------|
| Data tagging | Writes DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ to element parameters | **Implemented** (AutoTag, BatchTag, TagAndCombine) |
| Visual tagging | Creates `IndependentTag` annotations in views displaying tag values | **Partially implemented** (leader management commands exist in Organise panel) |

The 12 leader/annotation commands in `TagOperationCommands.cs` provide the foundation for visual tag management. The next step is automated placement with collision avoidance.

---

### Implementation Priority Matrix

#### Phase 1 — Critical Fixes (COMPLETED)

All Phase 1 items have been implemented:
- Tag collision detection with auto-increment SEQ
- Pre-tagging audit (PreTagAuditCommand)
- Tag New Only mode (TagNewOnlyCommand)
- Fix Duplicates command (FixDuplicateTagsCommand)
- Cross-parameter validation (ISO19650Validator)
- Family-stage pre-population (FamilyStagePopulateCommand)
- Full auto-populate pipeline (FullAutoPopulateCommand)
- Formula evaluation engine (FormulaEvaluatorCommand)
- Tag register export (TagRegisterExportCommand)
- Leader management (12 annotation commands)
- Document automation (DeleteUnusedViews, SheetNamingCheck, AutoNumberSheets)

#### Phase 2 — Color By Parameter System (COMPLETED)

- **ColorByParameterCommand** — Full graphic override by any parameter (text or numeric)
- **ClearColorOverridesCommand** — Per-element or per-view override clearing
- **SaveColorPresetCommand** / **LoadColorPresetCommand** — Preset save/load to COLOR_PRESETS.json
- **CreateFiltersFromColorsCommand** — Convert overrides into persistent Revit view filters
- **9 built-in palettes**: STING Discipline, RAG Status, Spectral, Warm, Cool, Pastel, High Contrast, Accessible, Monochrome
- **`<No Value>` QA detection** — Elements with empty values highlighted in red

#### Phase 3 — Smart Tag Placement (COMPLETED)

- **SmartTagPlacementCommand** — Priority-based 8-quadrant positioning with AABB collision avoidance
- **ArrangeTagsCommand** — Reposition existing tags to resolve overlaps
- **BatchTagViewsCommand** — Place annotation tags across all floor plan views
- **SmartPlacementEngine** — Shared scoring engine with scale-aware offsets and leader auto-insertion
- **Leader modes**: Auto (add only when displaced), Always, Never

#### Phase 4 — View & Template Automation (COMPLETED)

- **BatchAlignViewportsCommand** — Align viewports across ALL sheets (top/left/center)
- **DuplicateViewCommand** — Duplicate/WithDetailing/AsDependent with filter copy
- **BatchRenameViewsCommand** — Add/remove STING prefix, UPPERCASE conversion
- **CopyViewSettingsCommand** — Copy filters and overrides from source to all same-type views
- **AutoPlaceViewportsCommand** — Grid-based intelligent viewport placement
- **CropToContentCommand** — Auto-crop view boundaries to element extents with 5% padding

#### Phase 5 — Data Pipeline Completion (COMPLETED)

- **ValidateTemplateCommand** — C# port of VALIDAT_BIM_TEMPLATE.py with 45 validation checks
- **ValidateBindingsCommand** — Compare CATEGORY_BINDINGS.csv and BINDING_COVERAGE_MATRIX.csv against model
- **ConfigureTagFormatCommand** — View/configure tag separator, padding, segment order
- **TemplateValidator** — Full validation engine: GUID consistency, param coverage, formula well-formedness, file integrity
- **DynamicBindingLoader** — Loads 10,661 category bindings and coverage matrix from CSV

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
