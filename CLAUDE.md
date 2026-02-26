# CLAUDE.md — AI Assistant Guide for STINGTOOLS

## Repository Overview

**StingTools** is a unified **C# Revit plugin** (.addin + .dll) that consolidates three pyRevit extensions (STINGDocs, STINGTags, STINGTemp) into a single compiled assembly. It provides ISO 19650-compliant asset tagging, document management, and BIM template automation for Autodesk Revit 2025/2026/2027.

This file provides guidance for AI assistants (Claude Code, etc.) working in this repository.

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
    ├── Select/                         # Element selection commands (2 files, ~23 commands)
    │   ├── CategorySelectCommands.cs   # 15 category selectors + SelectAllTaggable
    │   └── StateSelectCommands.cs      # 5 state selectors + 2 spatial + BulkParamWrite
    │
    ├── Docs/                           # Documentation commands (5 files, 8 commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   ├── TransmittalCommand.cs       # ISO 19650 transmittal report
    │   └── ViewportCommands.cs         # Align, Renumber, TextCase, SumAreas
    │
    ├── Tags/                           # Tagging commands (9 files, ~20 commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagAndCombineCommand.cs     # One-click: populate + tag + combine all
    │   ├── CombineParametersCommand.cs # Interactive multi-container combine (16 groups, 37 params)
    │   ├── ConfigEditorCommand.cs      # View/edit/save project_config.json
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   ├── TokenWriterCommands.cs      # SetLoc, SetZone, SetStatus, AssignNumbers,
    │   │                               #   BuildTags, CompletenessDashboard
    │   └── ValidateTagsCommand.cs      # Validate tag completeness
    │
    ├── Organise/                       # Tag management commands (1 file, ~10 commands)
    │   └── TagOperationCommands.cs     # TagSelected, DeleteTags, Renumber, AuditCSV,
    │                                   #   FindDuplicates, HighlightInvalid, ClearOverrides,
    │                                   #   CopyTags, SwapTags, SelectByDiscipline, TagStats
    │
    ├── Temp/                           # Template commands (8 files, ~25 commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MasterSetupCommand.cs       # One-click full project setup (10 steps)
    │   ├── MaterialCommands.cs         # BLE + MEP material creation
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types
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

### Temp Panel (5 pulldown groups, ~25 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup** | Project setup + one-click automation (10-step workflow) |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes, Cable Trays, Conduits | Type creation from CSV data (8 commands) |
| Schedules | Batch Create, Material Takeoffs, Auto-Populate, Export CSV | Schedule management (168 definitions + 8 material takeoffs) |
| Templates | Create Filters, Apply Filters to Views, Create Worksets, View Templates, Line Patterns, Phases | 6 filters, 27 worksets, 7 view templates, 6 line patterns, 7 phases |

## Core Classes

### `StingToolsApp` (IExternalApplication) — `Core/StingToolsApp.cs`
- Entry point registered in `StingTools.addin`
- Sets `AssemblyPath` and `DataPath` (relative to DLL location)
- Builds ribbon tab with `BuildSelectPanel`, `BuildDocsPanel`, `BuildTagsPanel`, `BuildOrganisePanel`, `BuildTempPanel`
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

### `SharedParamGuids` (static) — `Core/SharedParamGuids.cs`
- 50+ parameter GUID mappings from `MR_PARAMETERS.txt`
- `UniversalParams` — 17 ASS_MNG parameters for Pass 1
- `AllCategories` / `AllCategoryEnums` — 53 `OST_*` built-in category names/enums
- `DisciplineBindings` — maps discipline-specific params to category subsets
- `BuildCategorySet(doc, enums)` — type-safe category set builder

### `TagConfig` (static, singleton) — `Core/TagConfig.cs`
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

When adding new commands, follow the existing pattern for the directory. Use shared static helper classes (e.g., `CategorySelector`, `TokenWriter`) to reduce duplication.

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

## Dependencies

- **Revit API**: `RevitAPI.dll`, `RevitAPIUI.dll` (referenced via `$(RevitApiPath)` — not distributed)
- **Newtonsoft.Json**: v13.0.3 (NuGet package)
- **Target framework**: .NET 8.0 Windows (Revit 2025+)
- **WPF**: Enabled (`UseWPF=true` in csproj) for `System.Windows.Media.Imaging`
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` are required at runtime
