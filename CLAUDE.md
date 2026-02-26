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
    ├── Core/                           # Shared infrastructure
    │   ├── StingToolsApp.cs            # IExternalApplication — ribbon UI builder
    │   ├── ParameterHelpers.cs         # Parameter read/write utilities
    │   ├── SharedParamGuids.cs         # GUID map for 200+ shared parameters
    │   └── TagConfig.cs                # ISO 19650 tag lookup tables
    │
    ├── Docs/                           # Documentation commands (4 commands)
    │   ├── SheetOrganizerCommand.cs    # Group sheets by discipline prefix
    │   ├── ViewOrganizerCommand.cs     # Organize views by type/level
    │   ├── SheetIndexCommand.cs        # Create sheet index schedule
    │   └── TransmittalCommand.cs       # ISO 19650 transmittal report
    │
    ├── Tags/                           # Tagging commands (5 commands)
    │   ├── AutoTagCommand.cs           # Tag elements in active view
    │   ├── BatchTagCommand.cs          # Tag all elements in project
    │   ├── TagConfigCommand.cs         # Display tag configuration
    │   ├── LoadSharedParamsCommand.cs   # Bind shared parameters (2-pass)
    │   └── ValidateTagsCommand.cs      # Validate tag completeness
    │
    ├── Temp/                           # Template commands (17 commands)
    │   ├── CreateParametersCommand.cs  # Delegates to LoadSharedParams
    │   ├── CheckDataCommand.cs         # Data file inventory with SHA-256
    │   ├── MaterialCommands.cs         # BLE + MEP material creation
    │   ├── FamilyCommands.cs           # Wall/Floor/Ceiling/Roof/Duct/Pipe types
    │   ├── ScheduleCommands.cs         # Batch schedules, auto-populate, CSV export
    │   └── TemplateCommands.cs         # Filters, worksets, view templates
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
| Category | 15 selectors (Lighting, Electrical, Mechanical, etc.) | Select elements by Revit category |
| State | Untagged, Tagged, Empty Mark, Pinned, Unpinned | Select by tag/pin/mark state |
| Spatial | By Level, By Room | Select by spatial criteria |
| Bulk Param | BulkParamWriteCommand | Write values to all selected elements |

### Docs Panel (4 buttons + Viewports pulldown)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| Sheet Organizer | `Docs.SheetOrganizerCommand` | Manual | Group sheets by discipline prefix |
| View Organizer | `Docs.ViewOrganizerCommand` | ReadOnly | Organize views by type, report placed/unplaced |
| Sheet Index | `Docs.SheetIndexCommand` | Manual | Create "STING - Sheet Index" schedule |
| Document Transmittal | `Docs.TransmittalCommand` | ReadOnly | ISO 19650 transmittal report |
| Viewports | Align, Renumber, Text Case, Sum Areas | Viewport and annotation tools |

### Tags Panel (2 buttons + Setup/Tokens/QA pulldowns)
| Button | Command Class | Transaction | Description |
|--------|--------------|-------------|-------------|
| Auto Tag | `Tags.AutoTagCommand` | Manual | Tag elements in active view (continues from max existing SEQ) |
| Batch Tag | `Tags.BatchTagCommand` | Manual | Tag all elements in entire project |
| Setup > Tag Config | `Tags.TagConfigCommand` | ReadOnly | Display/configure lookup tables |
| Setup > Load Params | `Tags.LoadSharedParamsCommand` | Manual | 2-pass shared parameter binding |
| Tokens > Set Location | `Tags.SetLocCommand` | Manual | Set LOC token on selected elements |
| Tokens > Set Zone | `Tags.SetZoneCommand` | Manual | Set ZONE token on selected elements |
| Tokens > Set Status | `Tags.SetStatusCommand` | Manual | Set STATUS token |
| Tokens > Assign Numbers | `Tags.AssignNumbersCommand` | Manual | Sequential numbering by DISC/SYS/LVL |
| Tokens > Build Tags | `Tags.BuildTagsCommand` | Manual | Rebuild ASS_TAG_1 from existing tokens |
| Tokens > Combine Parameters | `Tags.CombineParametersCommand` | Manual | Populate ALL tag containers (ASS_TAG_1-6 + discipline tags) |
| QA > Validate | `Tags.ValidateTagsCommand` | ReadOnly | Validate tag completeness (checks empty segments) |
| QA > Find Duplicates | `Organise.FindDuplicateTagsCommand` | ReadOnly | Find duplicate tag values |
| QA > Highlight Invalid | `Organise.HighlightInvalidCommand` | Manual | Colour-code missing/incomplete tags |
| QA > Completeness Dashboard | `Tags.CompletenessDashboardCommand` | ReadOnly | Per-discipline compliance dashboard |

### Organise Panel (Tag Ops pulldown)
| Command | Class | Description |
|---------|-------|-------------|
| Tag Selected | `Organise.TagSelectedCommand` | Tag selected elements only |
| Delete Tags | `Organise.DeleteTagsCommand` | Clear all tag params from selection |
| Renumber | `Organise.RenumberTagsCommand` | Re-sequence tags for selection |
| Audit to CSV | `Organise.AuditTagsCSVCommand` | Export full tag audit to CSV |

### Temp Panel (5 pulldown groups, 19 commands)
| Group | Commands | Description |
|-------|----------|-------------|
| Setup | Create Parameters, Check Data Files, **Master Setup** | Project setup + one-click automation |
| Materials | Create BLE Materials, Create MEP Materials | Material creation from CSV (815 + 464) |
| Families | Walls, Floors, Ceilings, Roofs, Ducts, Pipes | Type creation from CSV data |
| Schedules | Batch Create, Auto-Populate, Export CSV | Schedule management (168 definitions) |
| Templates | Create Filters, Create Worksets, View Templates | 6 filters, 27 worksets, 7 view templates |

## Core Classes

### `StingToolsApp` (IExternalApplication)
- Entry point registered in `StingTools.addin`
- Sets `AssemblyPath` and `DataPath` (relative to DLL location)
- Builds ribbon tab with `BuildDocsPanel`, `BuildTagsPanel`, `BuildTempPanel`
- Uses `PulldownButton` groups for the Temp panel

### `ParameterHelpers` (static)
- `GetString/SetString/SetIfEmpty` — parameter read/write with null safety
- `GetLevelCode` — derives short level codes (L01, GF, B1, RF, XX)
- `GetCategoryName` — safe category name retrieval

### `SharedParamGuids` (static)
- 50+ parameter GUID mappings from `MR_PARAMETERS.txt`
- `UniversalParams` — 17 ASS_MNG parameters for Pass 1
- `AllCategories` — 53 `OST_*` built-in category names

### `TagConfig` (static, singleton)
- `DiscMap` — 41 category → discipline code mappings (M, E, P, A, S, FP, LV, G)
- `SysMap` — 10 system codes → category lists
- `ProdMap` — 41 category → product codes
- `FuncMap` — 10 system → function code mappings
- Supports loading from `project_config.json` or built-in defaults

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
- Always wrap DB modifications in `Transaction` blocks with descriptive names
- Use `[Transaction(TransactionMode.Manual)]` for state-changing commands
- Use `[Transaction(TransactionMode.ReadOnly)]` for query-only commands
- Use `TaskDialog` for user-facing messages (not `MessageBox`)
- Dispose of Revit API objects properly
- Handle `OperationCanceledException` for user-cancelled operations
- Use `FilteredElementCollector` with appropriate filters for performance

### Data File Conventions

- CSV files use standard comma-separated format with quoted fields
- JSON files should be well-formatted with consistent indentation
- When modifying data files, preserve the existing structure and column order
- Data files are read at runtime from the `data/` directory alongside the DLL

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
- **Data files**: CSV/JSON/TXT files in `StingTools/Data/` are required at runtime
