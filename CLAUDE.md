# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

### Quick Stats

- **31 C# source files** (~7,000 lines of code) across 7 directories
- **79 `IExternalCommand` classes** (commands) + 1 `IExternalApplication` entry point
- **15 runtime data files** (CSV, JSON, TXT, XLSX, PY)
- **5 ribbon panels** with 15+ pulldown groups

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
    ├── Core/                           # Shared infrastructure (5 files)
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI builder (5 panels)
    │   ├── StingLog.cs                 # Thread-safe file logger (Info/Warn/Error)
    │   ├── ParameterHelpers.cs         # Parameter read/write utilities
    │   ├── SharedParamGuids.cs         # GUID map for 200+ shared parameters
    │   └── TagConfig.cs                # ISO 19650 tag lookup tables + tag builder
    │
    ├── Select/                         # Element selection commands (2 files, 23 commands)
    │   ├── CategorySelectCommands.cs   # 15 category selectors + SelectAllTaggable + CategorySelector helper
    │   └── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite
    │
    ├── Docs/                           # Documentation commands (5 files, 8 commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   └── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │
    ├── Tags/                           # Tagging commands (9 files, 14 commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagAndCombineCommand.cs     # One-click: populate + tag + combine all
    │   ├── CombineParametersCommand.cs # Interactive multi-container combine (16 groups, 37 params)
    │   ├── ConfigEditorCommand.cs      # View/edit/save project_config.json
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   ├── TokenWriterCommands.cs      # SetLoc, SetZone, SetStatus, AssignNumbers,
    │   │                               #   BuildTags, CompletenessDashboard + TokenWriter helper
    │   └── ValidateTagsCommand.cs      # Validate tag completeness
    │
    ├── Organise/                       # Tag management commands (1 file, 11 commands)
    │   └── TagOperationCommands.cs     # TagSelected, DeleteTags, Renumber, AuditCSV,
    │                                   #   FindDuplicates, HighlightInvalid, ClearOverrides,
    │                                   #   CopyTags, SwapTags, SelectByDiscipline, TagStats
    │
    ├── Temp/                           # Template commands (8 files, 23 commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MasterSetupCommand.cs       # One-click full project setup (10 steps)
    │   ├── MaterialCommands.cs         # BLE + MEP material creation + MaterialPropertyHelper
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types + CompoundTypeCreator
    │   ├── ScheduleCommands.cs         # Batch schedules, auto-populate, CSV export
    │   ├── TemplateCommands.cs         # Filters, worksets, view templates
    │   └── TemplateExtCommands.cs      # Line patterns, phases, apply filters,
    │                                   #   cable trays, conduits, material schedules
    │
    └── Data/                           # Runtime data files
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
        ├── PYREVIT_SCRIPT_MANIFEST.csv # Legacy pyRevit script manifest
        ├── TAG_GUIDE.xlsx              # Tag reference guide
        └── VALIDAT_BIM_TEMPLATE.py     # BIM template validation (45 checks)
```

## Ribbon UI Architecture

The plugin creates a single **"STING Tools"** ribbon tab with five panels:

### Select Panel (3 pulldowns + 1 button)
| Group | Commands | Description |
|-------|----------|-------------|
| Category | 15 selectors (Lighting, Electrical, Mechanical, Plumbing, Air Terminals, Furniture, Doors, Windows, Rooms, Sprinklers, Pipes, Ducts, Conduits, Cable Trays, ALL Taggable) | Select elements by Revit category in active view |
| State | Untagged, Tagged, Empty Mark, Pinned, Unpinned | Select by tag/pin/mark state |
| Spatial | By Level, By Room | Select by spatial criteria |
| Bulk Param | `Select.BulkParamWriteCommand` | Write LOC/ZONE/STATUS values or clear tags on selected elements |

### Docs Panel (4 buttons + Viewports pulldown)
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

### Tags Panel (3 buttons + Setup/Tokens/QA pulldowns)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| Auto Tag | `Tags.AutoTagCommand` | Manual | Tag elements in active view (continues from max existing SEQ) |
| Batch Tag | `Tags.BatchTagCommand` | Manual | Tag all elements in entire project |
| Tag & Combine | `Tags.TagAndCombineCommand` | Manual | One-click: populate tokens + tag + combine all containers (view/selection/project scope) |

**Setup pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Config | `Tags.TagConfigCommand` | ReadOnly | Display/configure lookup tables |
| Load Params | `Tags.LoadSharedParamsCommand` | Manual | 2-pass shared parameter binding |
| Configure | `Tags.ConfigEditorCommand` | ReadOnly | View/edit/save/reload project_config.json |

**Tokens pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Set Location | `Tags.SetLocCommand` | Manual | Set LOC token (BLD1, BLD2, BLD3, EXT) |
| Set Zone | `Tags.SetZoneCommand` | Manual | Set ZONE token (Z01-Z04) |
| Set Status | `Tags.SetStatusCommand` | Manual | Set STATUS token (EXISTING, NEW, DEMOLISHED, TEMPORARY) |
| Assign Numbers | `Tags.AssignNumbersCommand` | Manual | Sequential numbering by DISC/SYS/LVL (continues from max existing) |
| Build Tags | `Tags.BuildTagsCommand` | Manual | Rebuild ASS_TAG_1 from existing tokens |
| Combine Parameters | `Tags.CombineParametersCommand` | Manual | Interactive multi-mode combine into all tag containers |

**QA pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Validate | `Tags.ValidateTagsCommand` | ReadOnly | Validate tag completeness (checks empty segments) |
| Find Duplicates | `Organise.FindDuplicateTagsCommand` | ReadOnly | Find duplicate tag values, select affected elements |
| Highlight Invalid | `Organise.HighlightInvalidCommand` | Manual | Colour-code missing (red) and incomplete (orange) tags |
| Clear Overrides | `Organise.ClearOverridesCommand` | Manual | Reset graphic overrides in active view |
| Completeness Dashboard | `Tags.CompletenessDashboardCommand` | ReadOnly | Per-discipline compliance dashboard with percentage |

### Organise Panel (2 pulldowns: Tag Ops + Analysis)
**Tag Ops pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Tag Selected | `Organise.TagSelectedCommand` | Manual | Tag selected elements only |
| Delete Tags | `Organise.DeleteTagsCommand` | Manual | Clear all 15 tag params from selection (with confirmation) |
| Renumber | `Organise.RenumberTagsCommand` | Manual | Re-sequence tags within (DISC, SYS, LVL) groups |
| Copy Tags | `Organise.CopyTagsCommand` | Manual | Copy tag values from first selected to all others (excludes SEQ) |
| Swap Tags | `Organise.SwapTagsCommand` | Manual | Swap all tag values between exactly 2 selected elements |

**Analysis pulldown:**
| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Audit to CSV | `Organise.AuditTagsCSVCommand` | ReadOnly | Export full tag audit to CSV file |
| Select by Discipline | `Organise.SelectByDisciplineCommand` | ReadOnly | Select all elements of a specific discipline code |
| Tag Statistics | `Organise.TagStatsCommand` | ReadOnly | Quick tag counts by discipline/system/level for active view |

### Temp Panel (5 pulldown groups, 23 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup** | Project setup + one-click automation (10-step workflow) |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes (FamilyCommands.cs), Cable Trays, Conduits (TemplateExtCommands.cs) | Type creation from CSV data (8 commands) |
| Schedules | Batch Create, Material Takeoffs (TemplateExtCommands.cs), Auto-Populate, Export CSV | Schedule management (168 definitions + 8 material takeoffs) |
| Templates | Create Filters, Apply Filters to Views, Create Worksets, View Templates, Line Patterns, Phases | 6 filters, 27 worksets, 7 view templates, 6 line patterns, 7 phases |

## Command Count by File

| File | Commands | Lines |
|------|----------|-------|
| `Select/CategorySelectCommands.cs` | 16 (15 category selectors + SelectAllTaggable) | 168 |
| `Select/StateSelectCommands.cs` | 8 (5 state + 2 spatial + BulkParamWrite) | 289 |
| `Docs/SheetOrganizerCommand.cs` | 1 | 100 |
| `Docs/ViewOrganizerCommand.cs` | 1 | 91 |
| `Docs/SheetIndexCommand.cs` | 1 | 75 |
| `Docs/TransmittalCommand.cs` | 1 | 93 |
| `Docs/ViewportCommands.cs` | 4 (Align, Renumber, TextCase, SumAreas) | 304 |
| `Tags/AutoTagCommand.cs` | 1 | 63 |
| `Tags/BatchTagCommand.cs` | 1 | 65 |
| `Tags/TagAndCombineCommand.cs` | 1 | 189 |
| `Tags/CombineParametersCommand.cs` | 1 | 511 |
| `Tags/ConfigEditorCommand.cs` | 1 | 194 |
| `Tags/TagConfigCommand.cs` | 1 | 72 |
| `Tags/LoadSharedParamsCommand.cs` | 1 | 158 |
| `Tags/TokenWriterCommands.cs` | 6 (SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, CompletenessDashboard) | 320 |
| `Tags/ValidateTagsCommand.cs` | 1 | 201 |
| `Organise/TagOperationCommands.cs` | 11 (TagSelected, DeleteTags, Renumber, AuditCSV, FindDuplicates, HighlightInvalid, ClearOverrides, CopyTags, SwapTags, SelectByDiscipline, TagStats) | 665 |
| `Temp/CreateParametersCommand.cs` | 1 | 27 |
| `Temp/CheckDataCommand.cs` | 1 | 91 |
| `Temp/MasterSetupCommand.cs` | 1 | 155 |
| `Temp/MaterialCommands.cs` | 2 (BLE, MEP) | 238 |
| `Temp/FamilyCommands.cs` | 6 (Walls, Floors, Ceilings, Roofs, Ducts, Pipes) | 654 |
| `Temp/ScheduleCommands.cs` | 3 (BatchSchedules, AutoPopulate, ExportCSV) | 358 |
| `Temp/TemplateCommands.cs` | 3 (Filters, Worksets, ViewTemplates) | 250 |
| `Temp/TemplateExtCommands.cs` | 6 (LinePatterns, Phases, ApplyFilters, CableTrays, Conduits, MaterialSchedules) | 277 |
| **Total** | **79 commands** | **~6,970** |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs` (572 lines)
- Entry point registered in `StingTools.addin` (FullClassName: `StingTools.Core.StingToolsApp`)
- Static properties: `AssemblyPath`, `DataPath` (set in `OnStartup`, relative to DLL location)
- Builds ribbon tab "STING Tools" with `BuildSelectPanel`, `BuildDocsPanel`, `BuildTagsPanel`, `BuildOrganisePanel`, `BuildTempPanel`
- Uses `PulldownButton` groups for all panel sub-menus
- Provides `FindDataFile(fileName)` — searches `DataPath` and subdirectories
- Provides `ParseCsvLine(line)` — CSV parser respecting quoted fields

### `StingLog` (static) — `Core/StingLog.cs`
- Thread-safe file logger (`StingTools.log` alongside the DLL)
- Methods: `Info(msg)`, `Warn(msg)`, `Error(msg, ex?)`
- Used throughout the codebase for error tracing; replaces silent catch blocks
- Last-resort: silently swallows its own IO failures

### `ParameterHelpers` (static) — `Core/ParameterHelpers.cs`
- `GetString(el, paramName)` — read text parameter, returns empty string on null
- `SetString(el, paramName, value, overwrite)` — write text parameter, skips read-only/non-empty unless overwrite
- `SetIfEmpty(el, paramName, value)` — set only when currently empty
- `GetLevelCode(doc, el)` — derives short level codes (L01, GF, B1, RF, XX)
- `GetCategoryName(el)` — safe category name retrieval

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs` (286 lines)
- `ParamGuids` dictionary — 50+ parameter name → `Guid` mappings from `MR_PARAMETERS.txt`
- `UniversalParams` — 17 ASS_MNG parameters for Pass 1 (bound to all 53 categories)
- `AllCategories` / `AllCategoryEnums` — 53 `OST_*` built-in category names/enums
- `DisciplineBindings` — maps discipline-specific params to category subsets (Pass 2)
- `BuildCategorySet(doc, enums)` — type-safe category set builder
- Also declares `NumPad = 4` and `Separator = "-"` constants (duplicated in `TagConfig`)

### `TagConfig` (static, singleton) — `Core/TagConfig.cs` (334 lines)
- **Lookup tables** (all configurable via `project_config.json`):
  - `DiscMap` — 41 category → discipline code mappings (M, E, P, A, S, FP, LV, G)
  - `SysMap` — 13 system codes → category lists
  - `ProdMap` — 41 category → product codes
  - `FuncMap` — 13 system → function code mappings
  - `LocCodes` — location codes (BLD1, BLD2, BLD3, EXT, XX)
  - `ZoneCodes` — zone codes (Z01-Z04, ZZ, XX)
- **Configuration management**: `LoadFromFile(path)`, `LoadDefaults()`, `ConfigSource`
- **Tag operations**:
  - `TagIsComplete(tagValue, expectedTokens=8)` — validates 8-segment tag completeness
  - `BuildAndWriteTag(doc, el, seqCounters, skipComplete)` — shared tagging logic for all tag commands
  - `GetExistingSequenceCounters(doc)` — scans project for highest SEQ per group
  - `GetSysCode(categoryName)`, `GetFuncCode(sysCode)` — reverse lookups
- **Constants**: `NumPad = 4`, `Separator = "-"`

### Internal Helper Classes

These `internal static` classes provide shared logic used by multiple commands within their respective files:

| Helper Class | Location | Purpose |
|--------------|----------|---------|
| `CategorySelector` | `Select/CategorySelectCommands.cs` | `SelectByCategory()` — shared logic for all 16 category selection commands |
| `TokenWriter` | `Tags/TokenWriterCommands.cs` | Encapsulates LOC/ZONE/STATUS token writing and number assignment logic |
| `CompoundTypeCreator` | `Temp/FamilyCommands.cs` | Creates compound wall/floor/ceiling/roof/duct/pipe types from CSV data; `ElementKind` enum |
| `MaterialPropertyHelper` | `Temp/MaterialCommands.cs` | Shared material property-setting logic for BLE and MEP material commands |

## ISO 19650 Tag Format

Tags follow the 8-segment format: `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`

| Segment | Parameter | Example | Description |
|---------|-----------|---------|-------------|
| DISC | ASS_DISCIPLINE_COD_TXT | M, E, P, A, S | Discipline code |
| LOC | ASS_LOC_TXT | BLD1, EXT | Location/building code |
| ZONE | ASS_ZONE_TXT | Z01, Z02 | Zone code |
| LVL | ASS_LVL_COD_TXT | L01, GF, B1 | Level code |
| SYS | ASS_SYSTEM_TYPE_TXT | HVAC, HWS, LV | System type |
| FUNC | ASS_FUNC_TXT | SUP, HTG, PWR | Function code |
| PROD | ASS_PRODCT_COD_TXT | AHU, DB, DR | Product code |
| SEQ | ASS_SEQ_NUM_TXT | 0001, 0042 | 4-digit sequence number |

Example tag: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`

### Tag Containers (37 parameters across 16 groups)
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
2. **Multiple classes per file** — for related simple commands (e.g., `CategorySelectCommands.cs` has 15 selectors, `TokenWriterCommands.cs` has 6 commands)

When adding new commands, follow the existing pattern for the directory. Use shared `internal static` helper classes (e.g., `CategorySelector`, `TokenWriter`, `CompoundTypeCreator`, `MaterialPropertyHelper`) to reduce duplication.

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

> **IMPLEMENTATION STATUS (verified 2026-02-26): NONE of the enhancements, proposed commands, or gap fixes described in this section have been implemented.** Everything below is a design specification and aspirational roadmap. The codebase contains only the 79 commands documented in the sections above. When implementing any item below, create the new files/classes from scratch — no partial implementations exist.

### Current Automation Gaps (all still present)

#### A. Gaps That Hinder Full Automation

| Gap | Location | Problem | Impact |
|-----|----------|---------|--------|
| **No tag collision detection** | `TagConfig.cs:133-175` | `BuildAndWriteTag` doesn't check if generated tag already exists — two elements can get identical tags | Critical |
| **No progress reporting** | `BatchTagCommand`, `MasterSetupCommand`, material creation | Long operations (10,000+ elements) run with no feedback, no ETA, no cancellation | High |
| **No cancellation support** | All batch commands | Once started, user must wait until completion — no abort mechanism | High |
| **Hardcoded category bindings** | `SharedParamGuids.cs:109-261` | 53 categories + discipline bindings hardcoded; adding a category requires code rebuild (BINDING_COVERAGE_MATRIX.csv exists but unused) | Medium |
| **SEQ collision across zones** | `TagConfig.cs:157-161` | Sequence groups by DISC-SYS-LVL only — different rooms on same level can get same tag | Medium |
| **No error recovery** | `MasterSetupCommand.cs:62-116` | 10-step workflow: if step 5 fails, steps 1-4 already committed with no rollback | Medium |
| **Fixed tag format** | `TagConfig.cs:16-18` | `NumPad=4`, `Separator="-"` hardcoded — can't change segment count, order, or separator | Medium |
| **Unused data files** | `Data/` directory | 6 files never loaded: FORMULAS_WITH_DEPENDENCIES.csv (199 rules), MATERIAL_SCHEMA.json, SCHEDULE_FIELD_REMAP.csv, BINDING_COVERAGE_MATRIX.csv, CATEGORY_BINDINGS.csv (10,661 entries), VALIDAT_BIM_TEMPLATE.py (45 checks) | Medium |

#### B. Enhancement Opportunities (none implemented)

| Enhancement | Why Needed | Effort | Priority |
|-------------|-----------|--------|----------|
| Pre-tagging audit ("Will create X tags, Y overwrites, Z collisions") | Prevents errors before they happen | Low | High |
| Tag collision auto-fix (increment SEQ on duplicate) | Data integrity | Low | High |
| Configurable tag format in project_config.json (separator, padding, segments) | Flexibility for different standards | Medium | Medium |
| Formula evaluation engine (reads FORMULAS_WITH_DEPENDENCIES.csv) | Auto-populate computed parameters (199 rules exist unused) | High | High |
| Port VALIDAT_BIM_TEMPLATE.py (45 checks) to C# ValidateTemplateCommand | Template compliance checking | Medium | Medium |
| Conditional parameter set ("Set LOC=BLD2 where DISC=E") | Bulk intelligent updates | Medium | Medium |
| Cross-parameter validation (SEQ uniqueness, impossible combos like E+DHW) | Data quality | Low | Medium |
| Batch command chaining / workflow presets | Queue: AutoTag → Validate → Export | Medium | Low |

---

### New Feature: Color By Parameter (Graitec Lookup Style) — NOT IMPLEMENTED

Inspired by GRAITEC PowerPack Element Lookup, Naviate Color Elements (Symetri), BIM One Color Splasher (open-source), DiRoots OneFilter Visualize, ModPlus mprColorizer, and Future BIM Colors by Parameters. Provides element coloring by any parameter value with full graphic control.

**Note**: "Symmetry" in the original request likely refers to **Symetri** (makers of Naviate), not GRAITEC. Naviate Color Elements (part of Naviate Accelerate) is the most feature-rich color-by-parameter tool, with: modeless dialog, live preview, `<No Value>` red highlighting for QA, one-click view filter creation, line/fill toggle, and "keep overrides on exit" option.

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
7. **`<No Value>` Detection**: Elements with empty/null parameter values highlighted in distinct color (red) for instant QA — critical for finding missing data
8. **Non-Modal Dialog**: Use `IExternalEventHandler` so the dialog doesn't block Revit interaction (modeless window pattern used by Naviate, Color Splasher, ModPlus)
9. **View Filter Generation**: One-click conversion of color scheme into persistent Revit `ParameterFilterElement` rules (survives dialog close, visible in View Templates)
10. **Selection Mode**: Click a parameter value in the dialog to select/isolate all matching elements in the model

#### Implementation Pattern

```csharp
// CRITICAL: Find the solid fill pattern ONCE before the loop
// Setting color without a pattern produces no visible result
FillPatternElement solidFill = new FilteredElementCollector(doc)
    .OfClass(typeof(FillPatternElement))
    .Cast<FillPatternElement>()
    .First(fp => fp.GetFillPattern().IsSolidFill);

// Build override settings per unique parameter value
OverrideGraphicSettings ogs = new OverrideGraphicSettings();
ogs.SetProjectionLineColor(color);
ogs.SetProjectionLineWeight(weight);
ogs.SetSurfaceForegroundPatternId(solidFill.Id);  // must set pattern + color
ogs.SetSurfaceForegroundPatternColor(fillColor);
ogs.SetCutForegroundPatternId(solidFill.Id);       // for section views
ogs.SetCutForegroundPatternColor(fillColor);
ogs.SetSurfaceTransparency(transparency);

// Apply in single transaction for performance
using (Transaction t = new Transaction(doc, "STING Color By Parameter"))
{
    t.Start();
    foreach (ElementId id in matchingElements)
        view.SetElementOverrides(id, ogs);
    t.Commit();
}
// Note: per-element overrides persist in view, visible to all users, not stored in view templates
```

---

### New Feature: Smart Tag Placement — NOT IMPLEMENTED

Inspired by BIMLOGiQ Smart Annotation, Naviate Tag from Template, and academic Automatic Label Placement (ALP) research. Goal: perfect, collision-free automated tag annotation.

#### Critical Distinction: Data Tagging vs Visual Tagging

The existing `AutoTagCommand` and `BatchTagCommand` perform **data tagging** — writing ISO 19650 parameter values (ASS_TAG_1_TXT, etc.) to elements. **Visual tagging** — creating `IndependentTag` annotation elements that display those values in views — is a separate layer that does not yet exist in StingTools. Both layers are needed for full automation.

| Layer | What It Does | Current State |
|-------|-------------|---------------|
| Data tagging | Writes DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ to element parameters | Implemented (AutoTag, BatchTag, TagAndCombine) |
| Visual tagging | Creates `IndependentTag` annotations in views displaying tag values | **Not implemented** — requires new commands |

#### Key Research Findings

**Naviate (Symetri) — Tag from Template approach:**
- Tags follow templates with priority positions per family type
- Parameters: `NV Priority` (1, 2, 3...), `NVLinearTranslation`, `NVVectorTranslation`
- If priority 1 fails, try priority 2, then 3, then linear/vector displacement
- `NVVectorMoveTolerance` controls when leader lines are added
- Templates stored in cloud, shareable across projects

**BIMLOGiQ Smart Annotation — AI-powered placement:**
- Scans available space around each element for optimal position
- Collision avoidance: tag-to-tag, tag-to-element, leader-to-tag
- Leader modes: Auto (recommended), Always On, Off
- Pin preferred side to prioritize placement direction
- Filter by: min length, direction (horizontal/vertical/offset), system type
- Batch: Tag and Arrange (single view), Add Job (multi-view)
- Standard vs PRO: PRO handles dense/complex views better

**Revit API limitations (critical constraints):**
- No built-in collision avoidance — must implement from scratch
- Tag bounding box includes leader line to host → inaccurate extents
- Workaround: use TransactionGroup commit/rollback to measure true tag dimensions
- Performance degrades with tag count — solution: disable annotations via temporary view properties during bulk placement
- `IndependentTag.Create(doc, viewId, reference, addLeader, tagMode, orientation, headPosition)`
- `TagHeadPosition` property controls placement point
- No native overlap detection between tags

#### Proposed Implementation Architecture

**Phase 1: Smart Tag Placement Engine** (new file `Tags/SmartTagPlacementCommand.cs`)

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
4. Batch optimization: disable annotation visibility during placement, re-enable after
```

**Phase 2: Tag Template System** (Naviate-inspired)

| Feature | Description |
|---------|-------------|
| Tag family priority | Each tag family gets priority positions (P1, P2, P3) as shared parameters |
| Category-specific rules | Different placement rules per category (ducts: above, pipes: left, equipment: right) |
| Leader line thresholds | Auto-add leader when displacement exceeds configurable distance |
| Alignment snapping | Snap tag heads to alignment grid (horizontal/vertical rows) |
| Template save/load | Save placement rules as project_config.json section |

**Phase 3: Advanced Features**

| Feature | Description | Technique |
|---------|-------------|-----------|
| Collision detection | Tag-to-tag and tag-to-element overlap checking | Bounding box intersection with R-tree spatial index |
| Tag extent measurement | Get true tag dimensions (not inflated bounding box) | TransactionGroup commit/rollback workaround |
| Force-directed refinement | Post-placement optimization to minimize overlaps | Simulated annealing or spring-force model |
| Multi-view batch | Tag all views in a set (floor plans, sections) | Process view list with progress reporting |
| Arrange existing tags | Reposition already-placed tags to resolve overlaps | Same scoring algorithm on existing tags |
| Dense view handling | Special logic for crowded views | Tag clustering, grouping nearby elements, shared leaders |
| Scale awareness | Adjust placement offset based on view scale | Scale factor × base offset distance |

#### Tag Placement Scoring Function

```
Score(candidate) =
    + ProximityBonus(distToElement)          // closer is better (0-100)
    - OverlapPenalty(tagOverlaps) × 1000     // heavy penalty for any overlap
    - ElementOverlapPenalty(elemOverlaps) × 500  // avoid covering elements
    + AlignmentBonus(gridSnap)               // reward alignment with neighbors (0-50)
    + PreferredSideBonus(matchesPref)        // bonus if matches category preference (0-30)
    - LeaderPenalty(needsLeader)             // small penalty for needing a leader (0-20)
```

#### Revit API Implementation Patterns

**Creating a tag with specific tag type (Revit 2019+):**
```csharp
IndependentTag tag = IndependentTag.Create(
    doc, tagTypeId, viewId,     // Document, FamilySymbol Id, View Id
    new Reference(element),      // element to tag
    addLeader,                   // bool — show leader line
    TagOrientation.Horizontal,   // or Vertical
    headPosition                 // XYZ — tag head (no leader) or leader end (with leader)
);
```

**Position semantics:** Without leader, `XYZ` is the tag head position. With leader, the tag gets a default-length leader, head placed near the point.

**Getting accurate tag extents (TransactionGroup workaround):**
```csharp
// Default BoundingBox includes invisible leader → too large
// Workaround: temporarily move tag to element, measure, then rollback
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
    double tagWidth = (bb.Max - bb.Min).DotProduct(view.RightDirection);
    double tagHeight = (bb.Max - bb.Min).DotProduct(view.UpDirection);
    tg.RollBack();  // restore original position
}
```

**View scale-aware offset calculation:**
```csharp
int viewScale = view.Scale;  // e.g., 100 for 1:100
double baseOffset = 0.01;    // ~3mm on paper (in feet, Revit internal units)
double modelOffset = baseOffset * viewScale;
```

**Candidate position generation:**
```csharp
XYZ[] GetCandidates(XYZ center, double offset)
{
    return new XYZ[]
    {
        center + new XYZ(0, offset, 0),         // Above (P1)
        center + new XYZ(offset, 0, 0),          // Right (P2)
        center + new XYZ(0, -offset, 0),         // Below (P3)
        center + new XYZ(-offset, 0, 0),         // Left (P4)
        center + new XYZ(offset, offset, 0),     // NE (P5)
        center + new XYZ(offset, -offset, 0),    // SE (P6)
        center + new XYZ(-offset, -offset, 0),   // SW (P7)
        center + new XYZ(-offset, offset, 0),    // NW (P8)
    };
}
```

**AABB overlap test (2D, for plan-view annotations):**
```csharp
bool Overlaps(BoundingBoxXYZ a, BoundingBoxXYZ b)
{
    return a.Min.X < b.Max.X && a.Max.X > b.Min.X
        && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;
}
```

**Performance: disable annotations during bulk placement:**
```csharp
// Prevents O(n²) slowdown (1st tag: 0.1s, 150th tag: 23s without this)
view.EnableTemporaryViewPropertiesMode(view.Id);
// ... place all tags ...
view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryViewProperties);
```

**Finding untagged elements in a view:**
```csharp
var existingTags = new FilteredElementCollector(doc, viewId)
    .OfClass(typeof(IndependentTag))
    .Cast<IndependentTag>().ToList();
var taggedIds = new HashSet<ElementId>(
    existingTags.Select(t => t.TaggedLocalElementId));
var untagged = new FilteredElementCollector(doc, viewId)
    .WhereElementIsNotElementType()
    .Where(e => !taggedIds.Contains(e.Id)).ToList();
```

#### Dense View Strategies

| Strategy | When to Use | Technique |
|----------|-------------|-----------|
| Tag clustering | Many identical elements nearby | Multi-reference tag (Revit 2022+) with shared leaders |
| Tag stacking | Column of tags outside dense area | Parallel leaders, vertical tag column (Bird Tools approach) |
| Priority suppression | Extremely dense areas | Tag every Nth element; suppress secondary elements |
| Leader fanning | Cluster of elements with individual tags | Fan leaders at consistent angles from tag column |
| Occupancy bitmap | 100+ elements in view | Discretize view into grid; O(1) overlap checks per cell |

#### Algorithm Selection Guide

| Element Count | View Density | Recommended Algorithm |
|---------------|-------------|----------------------|
| < 50 | Sparse | Greedy priority (8-position) — simple, fast |
| 50-200 | Moderate | Priority + greedy with AABB list — good quality |
| 200-500 | Dense | Priority + occupancy bitmap — O(1) overlap checks |
| 500+ | Very dense | Bitmap + tag clustering + priority suppression |

---

### Missing View Commands — NOT IMPLEMENTED

#### Proposed New Commands

| Command | Class | Transaction | Description |
|---------|-------|-------------|-------------|
| Batch Viewport Align | `Docs.BatchAlignViewportsCommand` | Manual | Align viewports across all/selected sheets |
| Batch Text Case | `Docs.BatchTextCaseCommand` | Manual | Apply text case conversion across all sheets |
| Delete Unused Views | `Docs.DeleteUnusedViewsCommand` | Manual | Remove views not placed on any sheet (with confirmation) |
| Duplicate View | `Docs.DuplicateViewCommand` | Manual | Copy view with filters, overrides, visibility state |
| Batch Rename Views | `Docs.BatchRenameViewsCommand` | Manual | Find/replace or pattern-based view renaming |
| Copy View Settings | `Docs.CopyViewSettingsCommand` | Manual | Copy filters + overrides from source view to targets |
| Auto-Place Viewports | `Docs.AutoPlaceViewportsCommand` | Manual | Grid-based intelligent viewport placement on sheets |
| View Crop to Content | `Docs.CropToContentCommand` | Manual | Auto-crop view boundaries to element extents |

#### Existing View Commands (for reference)

| Command | File | Current Capability |
|---------|------|--------------------|
| ViewOrganizerCommand | `Docs/ViewOrganizerCommand.cs` | List/report views by type, placed vs unplaced |
| AlignViewportsCommand | `Docs/ViewportCommands.cs` | Align viewports on single active sheet |
| RenumberViewportsCommand | `Docs/ViewportCommands.cs` | Renumber viewports on single active sheet |
| TextCaseCommand | `Docs/ViewportCommands.cs` | Convert text notes on single active sheet |
| SumAreasCommand | `Docs/ViewportCommands.cs` | Sum room areas in active view |
| ViewTemplatesCommand | `Temp/TemplateCommands.cs` | Create 7 discipline view templates |
| CreateFiltersCommand | `Temp/TemplateCommands.cs` | Create 6 discipline filters |
| ApplyFiltersToViewsCommand | `Temp/TemplateExtCommands.cs` | Apply discipline filters to views |

---

### Tagging Intelligence Improvements — NOT IMPLEMENTED

#### Current Tagging Logic (what exists)

| Aspect | Current State | Location |
|--------|---------------|----------|
| Token derivation | Category → DISC/SYS/PROD/FUNC via lookup tables | `TagConfig.cs` DiscMap, SysMap, ProdMap, FuncMap |
| Level code | Auto-derived from element's level (L01, GF, B1, RF) | `ParameterHelpers.GetLevelCode()` |
| Sequencing | Continues from max existing SEQ per DISC-SYS-LVL group | `TagConfig.GetExistingSequenceCounters()` |
| Completeness check | Validates 8-segment format, checks for empty segments | `TagConfig.TagIsComplete()` |
| Combine parameters | Writes assembled tag to 37 container parameters (16 groups) | `CombineParametersCommand.cs` |
| Duplicate detection | `FindDuplicateTagsCommand` finds but doesn't fix duplicates | `Organise/TagOperationCommands.cs` |

#### Intelligence Gaps to Address

| Gap | Problem | Proposed Solution |
|-----|---------|-------------------|
| **No collision prevention** | `BuildAndWriteTag` doesn't check existing tags before writing | Add pre-write check: query project for matching tag, auto-increment SEQ if collision found |
| **No system validation** | Can tag a lighting device as HVAC-SUP-AHU (nonsensical) | Cross-validate DISC against element category; warn on mismatches |
| **No spatial scoping** | Tags are globally unique but don't account for building/zone | Add optional namespace: LOC+ZONE prefix isolation for multi-building projects |
| **No family-aware product codes** | Uses category only, ignores family name | Add family name → product code overrides in project_config.json |
| **No quantity sanity checks** | 50 supply terminals + 1 return = no warning | Post-tagging analytics: flag system imbalances |
| **No annotation placement** | Tags are parameter values only — no physical annotation in views | Integrate with Smart Tag Placement (above) to auto-place annotation families |
| **No incremental tagging** | BatchTag re-processes all elements even if only 5 are new | Add "Tag New Only" mode: skip elements with complete ASS_TAG_1 |
| **No undo granularity** | Undo reverses entire batch, not individual elements | Per-element transaction isolation or selective undo |

#### Proposed New Commands

| Command | Class | Description |
|---------|-------|-------------|
| Smart Tag | `Tags.SmartTagCommand` | Tag with collision avoidance + smart annotation placement |
| Tag New Only | `Tags.TagNewOnlyCommand` | Only tag elements without existing ASS_TAG_1 |
| Fix Duplicates | `Tags.FixDuplicateTagsCommand` | Auto-resolve duplicate tags by incrementing SEQ |
| Tag Audit | `Tags.TagAuditCommand` | Pre-tagging audit: predict results, show collisions, report |
| Validate Systems | `Tags.ValidateSystemsCommand` | Cross-check DISC/SYS/PROD against element categories |

---

### Underutilized Data Files (still unused)

| File | Rows | Current Status | Proposed Usage |
|------|------|----------------|----------------|
| `FORMULAS_WITH_DEPENDENCIES.csv` | 199 | **Never loaded** | New FormulaEngine: auto-calculate derived parameters (weight, cost, etc.) |
| `MATERIAL_SCHEMA.json` | 77 cols | **Never loaded** | Validate BLE/MEP CSV columns match schema; data integrity checks |
| `SCHEDULE_FIELD_REMAP.csv` | 50 | **Never loaded** | Auto-remap deprecated field names when creating schedules |
| `BINDING_COVERAGE_MATRIX.csv` | Large | **Never loaded** | Replace hardcoded `SharedParamGuids.AllCategoryEnums` — load bindings dynamically |
| `CATEGORY_BINDINGS.csv` | 10,661 | **Never loaded** | Replace hardcoded `DisciplineBindings` — data-driven parameter binding |
| `FAMILY_PARAMETER_BINDINGS.csv` | 4,686 | **Never loaded** | Family-level parameter validation and auto-binding |
| `VALIDAT_BIM_TEMPLATE.py` | 45 checks | **Python, not ported** | Port to C# `ValidateTemplateCommand` for comprehensive template QA |

---

### Implementation Priority Matrix (none started)

#### Phase 1 — Critical Fixes (Low effort, high impact)

1. **Tag collision detection** — Check existing tags before writing in `BuildAndWriteTag`, auto-increment SEQ
2. **Pre-tagging audit** — New command showing predicted tag assignments before applying
3. **Tag New Only mode** — Skip already-tagged elements in BatchTag
4. **Fix Duplicates command** — Auto-resolve duplicate tags
5. **Cross-parameter validation** — Verify DISC/SYS/PROD consistency with element category

#### Phase 2 — Color By Parameter System

6. **ColorByParameterCommand** — Full graphic override by any parameter
7. **Color preset system** — 10 built-in palettes + save/load custom presets
8. **Clear/reset overrides** — Per-parameter or per-view clearing
9. **Legend generation** — Auto-create color key

#### Phase 3 — Smart Tag Placement

10. **Smart placement engine** — Priority-based positioning with collision avoidance
11. **Tag extent measurement** — TransactionGroup workaround for accurate bounding boxes
12. **Arrange existing tags** — Reposition already-placed tags
13. **Multi-view batch tagging** — Process multiple views with progress reporting

#### Phase 4 — View & Template Automation

14. **Batch viewport alignment** across all sheets
15. **Delete unused views** with safety confirmation
16. **Duplicate view** with full settings copy
17. **Auto-place viewports** with grid-based layout

#### Phase 5 — Data Pipeline Completion

18. **Formula evaluation engine** — Apply 199 calculation rules from CSV
19. **Dynamic category bindings** — Load from BINDING_COVERAGE_MATRIX.csv instead of hardcode
20. **Port VALIDAT_BIM_TEMPLATE.py** — 45 validation checks as C# command
21. **Schedule field remap** — Auto-apply 50 field name updates

### External Tool References

- [BIMLOGiQ Smart Annotation](https://bimlogiq.com/product/smart-annotation) — AI-powered tag placement with collision avoidance
- [Naviate Tag from Template](https://www.naviate.com/release-news/naviate-structure-may-release-2025-2/) — Priority-based tag positioning with templates
- [GRAITEC PowerPack Element Lookup](https://graitec.com/uk/resources/technical-support/documentation/powerpack-for-revit-technical-articles/element-lookup-powerpack-for-revit/) — Advanced element search and filtering
- [BIM One Color Splasher](https://github.com/bimone/addins-colorsplasher) — Open-source color by parameter (GitHub)
- [ModPlus mprColorizer](https://modplus.org/en/revitplugins/mprcolorizer) — Color by conditions with preset saving
- [The Building Coder — Tag Extents](https://thebuildingcoder.typepad.com/blog/2022/07/tag-extents-and-lazy-detail-components.html) — Getting accurate tag bounding boxes
- [Revit API — OverrideGraphicSettings](https://www.revitapidocs.com/2025/eb2bd6b6-b7b2-5452-2070-2dbadb9e068a.htm) — Complete graphic override API
- [Revit API — IndependentTag](https://www.revitapidocs.com/2025/e52073e2-9d98-6fb5-eb43-288cf9ed2e28.htm) — Tag creation and positioning API
