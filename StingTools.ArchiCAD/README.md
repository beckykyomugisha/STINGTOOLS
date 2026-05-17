# StingTools for ArchiCAD

Native ArchiCAD Add-On that connects ArchiCAD models to the Planscape coordination platform — the same ISO 19650 workflow available to Revit users via StingTools, now available to ArchiCAD firms without any manual IFC export step.

## Status

**Scaffold** — the project structure, API client interface, and menu registration skeleton are in place. Full implementation is in progress. See implementation checklist below.

## What it will do

| Feature | Status |
|---|---|
| Login to Planscape from ArchiCAD | Scaffold |
| One-click IFC export + upload to Planscape | Scaffold |
| Read STING tags from `Planscape_Asset` property set and sync | Scaffold |
| Download open issues as BCF → auto-import into BCF Manager | Scaffold |
| Push resolved BCF topics back to Planscape | Scaffold |
| Compliance dashboard panel inside ArchiCAD | Planned |
| Auto-sync on Publish (Publisher workflow hook) | Planned |

## In the meantime — manual workflow

ArchiCAD firms can already use Planscape today without this Add-On. See the [ArchiCAD IFC Workflow guide](../docs-site/docs/howto/archicad-ifc-workflow.md).

## Build requirements

- **ArchiCAD API DevKit** — free download from [archicadapi.graphisoft.com](https://archicadapi.graphisoft.com)
  - Match the DevKit version to your target ArchiCAD version (22–28)
- **Windows**: Visual Studio 2022, MSVC v143, x64
- **macOS**: Xcode 15, targeting arm64 + x86_64 universal binary
- **CMake** 3.24+
- No third-party HTTP library needed — WinHTTP on Windows, libcurl (system) on macOS

## Build

```bash
# 1. Unpack ArchiCAD DevKit, set env var
export ARCHICAD_API_DEVKIT=/path/to/ArchiCAD_API_DevKit_26

# 2. Configure
cmake -B build -DARCHICAD_API_DEVKIT=$ARCHICAD_API_DEVKIT

# 3. Build
cmake --build build --config Release

# 4. Install
# Copy build/StingPlanscape.apx to:
#   Windows: %APPDATA%\Graphisoft\ArchiCAD <version>\Add-Ons\
#   macOS:   ~/Library/Application Support/Graphisoft/ArchiCAD <version>/Add-Ons/
```

## Implementation checklist

### Phase 1 — Auth + IFC upload (target: 4 weeks)
- [ ] `PlanscapeClient.cpp` — WinHTTP + libcurl HTTP implementation
- [ ] JWT token storage (Windows Credential Store / macOS Keychain)
- [ ] Login dialog (DG::Dialog)
- [ ] IFC export via ACAPI + upload to `/api/projects/{id}/models`
- [ ] Settings dialog (server URL, project selector)

### Phase 2 — Tag sync (target: 2 weeks after Phase 1)
- [ ] Read `Planscape_Asset` property set from all elements
- [ ] Fall back to `AC_Pset_*` native properties using STING_IFC_PSET_MAPPING.json rules
- [ ] POST to `/api/tagsync/sync` (same endpoint as Revit plugin)
- [ ] Progress dialog during bulk sync

### Phase 3 — BCF round-trip (target: 2 weeks after Phase 2)
- [ ] GET `/api/projects/{id}/bcf/export?source=archicad` → write .bcfzip
- [ ] Auto-invoke ArchiCAD BCF Manager import (`ACAPI_Interoperability_ImportBCF`, v26+)
- [ ] Fallback for v22–25: write file + show path in dialog
- [ ] Upload resolved BCF back via POST `/api/projects/{id}/bcf/import`

### Phase 4 — Compliance panel (target: 3 weeks after Phase 3)
- [ ] Modeless DG panel showing compliance % per discipline
- [ ] Pull from `/api/projects/{id}/compliance`
- [ ] Register with ArchiCAD palette system

## Property set setup in ArchiCAD

To pre-populate STING coordination tags in ArchiCAD before export:

1. Open **Options → Element Attributes → Properties**
2. Click **New Property Group** → name it `Planscape_Asset`
3. Add the following properties (all type: String):

| Property name | Maps to |
|---|---|
| `Discipline` | `ASS_DISCIPLINE_COD_TXT` (M / E / P / A / S) |
| `LocationCode` | `ASS_LOC_TXT` (BLD1, BLD2, EXT…) |
| `ZoneCode` | `ASS_ZONE_TXT` (Z01, Z02…) |
| `SystemType` | `ASS_SYSTEM_TYPE_TXT` (HVAC, DCW, SAN…) |
| `Tag` | `ASS_TAG_1` (full ISO 19650 tag if pre-assembled) |

4. Assign values to elements
5. These properties export automatically under the `Planscape_Asset` IFC pset and are read by Planscape on upload

## Architecture

```
StingTools.ArchiCAD/
├── CMakeLists.txt                  # Build configuration
├── include/
│   └── PlanscapeClient.hpp         # HTTP client interface (mirrors PluginSync)
├── src/
│   ├── StingPlanscapeAddon.cpp     # Add-On entry point + menu handler
│   ├── PlanscapeClient.cpp         # TODO: HTTP implementation
│   ├── LoginDialog.cpp             # TODO: DG login dialog
│   └── SettingsDialog.cpp          # TODO: DG settings dialog
└── resources/
    ├── StingPlanscape.grc          # TODO: string resources (menu names)
    └── StingPlanscape.grd          # TODO: dialog resources
```
