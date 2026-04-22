# STING Tools — Dynamo Node Library

Phase 109 — 48+ ZeroTouch nodes across 9 categories surfacing every STING
automation in a Dynamo graph.

## Install

1. Build `StingTools.Dynamo.dll` on Windows (see below).
2. Copy the build output + `pkg.json` + `Samples/` into
   `%APPDATA%\Dynamo\Dynamo Revit\<version>\packages\STING Tools\`:

   ```
   STING Tools/
   ├─ bin/
   │  └─ StingTools.Dynamo.dll
   ├─ extra/
   ├─ samples/
   │  ├─ 01_DailyQA.dyn
   │  ├─ 02_FullFabricationRun.dyn
   │  ├─ 03_BOQSnapshot.dyn
   │  ├─ 04_MEPSystemAudit.dyn
   │  └─ 05_DeliverableReadiness.dyn
   └─ pkg.json
   ```

3. Restart Revit and open Dynamo. The nodes appear under **STING Tools** in
   the Library panel.

## Build

```bat
cd StingTools.Dynamo
dotnet restore
dotnet build -c Release
```

The csproj gates DynamoVisualProgramming NuGet packages on Windows only so
the repository still authorises on Linux / macOS / CI without Dynamo runtime
installed.

## Requirements

- Revit 2025 (or 2026 / 2027)
- StingTools.dll deployed as a Revit add-in (same machine, same Revit version)
- Dynamo 2.17+ (ships with Revit 2025)

## Node categories

| Category | Nodes | Dispatcher tags |
|----------|-------|-----------------|
| `Tag` | 9 | AutoTag, TagNewOnly, BatchTag, TagSelected, ReTag, ValidateTags, FixDuplicates, CompletenessDashboard, TagRegisterExport |
| `Placement` | 3 | Placement_PlaceFixtures, Placement_LightingGrid, Placement_Learn |
| `Routing` | 3 | Routing_AutoDrop, Routing_GenerateLayout, Routing_ValidateFills |
| `Validation` | 1 | Validation_RunAll |
| `MEP` | 13 | Mep_PressureDrop, Mep_FittingLoss, Mep_Balance, Mep_VibroAcoustic, Mep_SystemAnalyse, Mep_SystemTracer, Mep_AutoSleeve, Mep_AutoSizePipe/Duct/Conduit/All, Mep_FillLiveCalc, Mep_NamingAudit |
| `Fabrication` | 4 | Fabrication_GeneratePackage, Fabrication_ExportCutList, Fabrication_ExportIsometrics, Fabrication_ExportWeldMap |
| `BOQ` | 6 | BOQRefresh, BOQSaveSnapshot, BOQCompareSnapshots, BOQExport, BOQImport, BOQReconcile |
| `Sheets` | 6 | AutoLayout, PlaceUnplaced, CloneSheet, SheetComplianceCheck, ExportSheetRegister, BatchPrint |
| `Export` | 6 | ExportToExcel, ImportFromExcel, COBieExport, IFCExport, BCFExport, COBieHandoverExport |
| `Standards` | 5 | ISO19650Deep, CibseVelocity, BS7671, UniclassValidator, PartL |

## Architecture

Dynamo nodes never call Revit API directly. Each node resolves the
`StingCommandHandler` class in the loaded `StingTools` assembly via
reflection, sets the command tag, raises the ExternalEvent, and returns
control to the graph. The same code path as the dock panel buttons —
transaction boundary, rollback semantics, result panel, and logging
behaviour are all identical.

## Example — Daily QA graph

```
   File.Path                              ← path to a .rvt
       │
       ▼
   Tag.AutoTag                            ← one click daily morning
       │
       ▼
   Validation.RunAll                      ← Connectivity / Fill / Spec / Termination / Slope
       │
       ▼
   Tag.CompletenessDashboard              ← Compliance RAG panel
```

Chain `Std.ISO19650Audit → BOQ.Refresh → Export.COBie → Export.BCF` for
a full data-drop readiness graph.

## Troubleshooting

- **Nodes fail with "STING Tools not loaded"** — ensure StingTools.dll
  is loaded by opening the dock panel once before running the graph.
- **Commands return immediately** — the dispatcher polls a bounded 200ms
  wait for completion; long-running commands (Fabrication_GeneratePackage,
  AutoSleeve) surface their own progress dialog on the Revit side.
- **"Deployment ready" without nodes** — delete the Dynamo package cache
  at `%LOCALAPPDATA%\Dynamo\packages-cache\` and restart Revit.

## License

Proprietary — Planscape / STING Tools.
