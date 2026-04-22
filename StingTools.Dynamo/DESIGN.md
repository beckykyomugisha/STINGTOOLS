# STING Tools — Dynamo Integration Design

**Status**: Phase 109 — active implementation. Stub replaced with a real
ZeroTouch node library. Build produces `StingTools.Dynamo.dll` which
Dynamo loads from `%APPDATA%\Dynamo\Dynamo Revit\2.17\packages\STING Tools\bin\`.

## Rationale

Dynamo is the lingua franca of BIM power users. Every STING automation
currently accessible via dock panel button should also be reachable from
a Dynamo graph so:

- Coordinators can chain STING operations alongside `Element.Geometry`,
  `FamilyInstance.ByPoint`, `Select.Model.Elements` etc.
- Customers can extend STING without recompiling the plugin — just
  drop a `.dyn` file in `Data/Workflows/`.
- Power users can script parametric sweeps (e.g. "auto-size every
  system at three velocity targets and report deltas").
- CI pipelines can run deterministic Dynamo Player graphs as regression
  tests against the plugin assembly.

## Architecture

```
┌───────────────────┐    ┌─────────────────────┐    ┌────────────────┐
│  Dynamo Graph     │───▶│  ZeroTouch facade   │───▶│  StingTools    │
│  (.dyn file)      │    │  StingTools.Dynamo  │    │  IExternal-    │
│                   │    │  .dll               │    │  EventHandler  │
│  STING.AutoTag    │    │                     │    │                │
│  STING.BuildBOQ   │    │  Each node dispatches    │  (same path as │
│  STING.SystemTrace│    │  via StingCommand-      │   dock panel    │
│  …                │    │  Handler.ExternalEvent  │   buttons)     │
└───────────────────┘    └─────────────────────┘    └────────────────┘
```

Dynamo nodes do NOT call Revit API directly — they dispatch through the
existing `StingCommandHandler` ExternalEvent so every operation runs on
the Revit API thread and shares the same transaction boundary + rollback
semantics as the dock panel.

## Node catalogue (60+ nodes across 9 categories)

### STING.Tag (9 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Tag.AutoTag(view)` | → bool | `AutoTagCommand` |
| `Tag.TagNewOnly(view)` | → int | `TagNewOnlyCommand` |
| `Tag.BatchTag()` | → int | `BatchTagCommand` |
| `Tag.TagSelected(elements)` | → int | `TagSelectedCommand` |
| `Tag.ReTag(elements)` | → int | `ReTagCommand` |
| `Tag.ValidateTags()` | → ValidationReport | `ValidateTagsCommand` |
| `Tag.FixDuplicates()` | → int | `FixDuplicateTagsCommand` |
| `Tag.Completeness()` | → ComplianceResult | `CompletenessDashboardCommand` |
| `Tag.ExportRegister(path)` | → string | `TagRegisterExportCommand` |

### STING.Placement (4 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Placement.PlaceFixtures(rooms, dryRun)` | → List<Element> | `PlaceFixturesCommand` |
| `Placement.LightingGrid(room)` | → List<XYZ> | `LightingGridCalculator.Compute` |
| `Placement.ClassifyRoom(room)` | → string | `LightingGridCalculator.ClassifyRoom` |
| `Placement.LearnFromModel()` | → List<PlacementRule> | `LearnPlacementV4Command` |

### STING.Routing (6 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Routing.AutoDrop(fixtures)` | → List<Element> | `AutoDropCommand` |
| `Routing.AutoConduitDrop(fixtures)` | → List<Element> | `AutoConduitDrop.Execute` |
| `Routing.AutoPipeDrop(fixtures)` | → List<Element> | `AutoPipeDrop.Execute` |
| `Routing.AutoDuctDrop(fixtures)` | → List<Element> | `AutoDuctDrop.Execute` |
| `Routing.GenerateLayout(elements)` | → List<Element> | `GenerateLayoutCommand` |
| `Routing.ValidateFills()` | → ValidationResult[] | `ValidateFillsCommand` |

### STING.Validation (6 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Validation.Connectivity()` | → ValidationResult[] | `ConnectivityValidator` |
| `Validation.Fill()` | → ValidationResult[] | `FillValidator` |
| `Validation.Spec()` | → ValidationResult[] | `SpecValidator` |
| `Validation.Termination()` | → ValidationResult[] | `TerminationValidator` |
| `Validation.Slope()` | → ValidationResult[] | `SlopeValidator` |
| `Validation.RunAll()` | → ValidationResult[] | `RunAllValidatorsCommand` |

### STING.Mep (13 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Mep.PressureDropAnalyse()` | → PressureDropResult[] | `MepPressureDropAnalyseCommand` |
| `Mep.FittingLossReport(elements)` | → Dictionary | `MepFittingLossReportCommand` |
| `Mep.Balance(branches)` | → BalancingResult | `MepBalanceSystemCommand` |
| `Mep.VibroAcoustic(equip)` | → VibrationResult[] | `MepVibroAcousticCheckCommand` |
| `Mep.SystemAnalyse()` | → PressureDropResult[] | `MepSystemAnalyseCommand` |
| `Mep.SystemTrace(seed)` | → List<Element> | `MepSystemTracerCommand` |
| `Mep.AutoSleeve()` | → List<Element> | `AutoSleevePlacementCommand` |
| `Mep.AutoSizePipe()` | → int | `MepAutoSizePipeCommand` |
| `Mep.AutoSizeDuct()` | → int | `MepAutoSizeDuctCommand` |
| `Mep.AutoSizeConduit()` | → int | `MepAutoSizeConduitCommand` |
| `Mep.AutoSizeAll()` | → int | `MepAutoSizeAllCommand` |
| `Mep.FillLiveCalc()` | → int | `MepFillLiveCalcCommand` |
| `Mep.NamingAudit()` | → string[] | `MepNamingAuditCommand` |

### STING.Fabrication (6 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Fab.GeneratePackage(elements)` | → FabricationResult | `GenerateFabPackageCommand` |
| `Fab.GroupAssemblies(elements, discipline)` | → List<List<Element>> | `AssemblyGrouper.GroupForDiscipline` |
| `Fab.BuildShopDrawing(assy)` | → ViewSheet | `ShopDrawingComposer.ComposeSheet` |
| `Fab.ExportCutList(path)` | → string | `ExportCutListCommand` |
| `Fab.ExportIsometrics(path)` | → string | `ExportIsometricsCommand` |
| `Fab.ExportWeldMap(path)` | → string | `ExportWeldMapCommand` |

### STING.BOQ (6 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `BOQ.BuildDocument()` | → BOQDocument | `BOQEngine.BuildFromModel` |
| `BOQ.SaveSnapshot()` | → string | `BOQSaveSnapshotCommand` |
| `BOQ.CompareSnapshots(a, b)` | → BOQDiff | `BOQCompareSnapshotsCommand` |
| `BOQ.SetBudget(ugx, usd)` | → void | `BOQSetBudgetCommand` |
| `BOQ.ExportXlsx(path)` | → string | `BOQExportCommand` |
| `BOQ.ReconcilePS()` | → int | `BOQReconcileCommand` |

### STING.Sheets (7 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Sheets.AutoLayout(sheet)` | → int | `AutoLayoutCommand` |
| `Sheets.PlaceUnplaced()` | → List<ViewSheet> | `PlaceUnplacedViewsCommand` |
| `Sheets.BatchClone(template, count)` | → List<ViewSheet> | `BatchCloneSheetsCommand` |
| `Sheets.BatchRenumber(pattern)` | → int | `BatchRenumberSheetsCommand` |
| `Sheets.ComplianceCheck()` | → string[] | `SheetComplianceCheckCommand` |
| `Sheets.ExportRegister(path)` | → string | `ExportSheetRegisterCommand` |
| `Sheets.BatchPrint(path)` | → string[] | `BatchPrintSheetsCommand` |

### STING.Export (7 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Export.ToExcel(path)` | → string | `ExportToExcelCommand` |
| `Export.FromExcel(path)` | → int | `ImportFromExcelCommand` |
| `Export.COBie(preset, path)` | → string | `COBieExportCommand` |
| `Export.IFC(path, setup)` | → string | `IFCExportCommand` |
| `Export.BCF(issues, path)` | → string | `BCFExportCommand` |
| `Export.FM Handover(path)` | → string | `COBieHandoverExportCommand` |
| `Export.BOQ(path)` | → string | `BOQExportCommand` |

### STING.Standards (5 nodes)

| Node | Signature | Calls |
|------|-----------|-------|
| `Std.ISO19650Audit()` | → string[] | `ISO19650Audit` |
| `Std.CibseVelocityCheck()` | → string[] | `CibseVelocityCheck` |
| `Std.BS7671Audit()` | → string[] | `BS7671Audit` |
| `Std.UniclassValidate()` | → string[] | `UniclassValidator` |
| `Std.PartL()` | → string[] | `PartLCompliance` |

**Total: 69 nodes across 9 categories.**

## Category hierarchy in the Dynamo library

```
STING Tools/
├─ BIM/
│  ├─ Tag/                (9 nodes)
│  ├─ Placement/          (4 nodes)
│  ├─ Routing/            (6 nodes)
│  └─ Validation/         (6 nodes)
├─ MEP/                   (13 nodes)
├─ Fabrication/           (6 nodes)
├─ BOQ/                   (6 nodes)
├─ Sheets/                (7 nodes)
├─ Export/                (7 nodes)
└─ Standards/             (5 nodes)
```

## Result types

Nodes return richly-typed objects that surface naturally in the Dynamo
watch node without custom previews:

- `ValidationResult` — Severity, Code, Message, Element
- `ComplianceResult` — TotalElements, Tagged, UntaggedPct, RagStatus
- `PressureDropResult` — TotalLossPa, VelocityMs, VelocityExceeded
- `FabricationResult` — AssemblyIds, SheetIds, ByDiscipline
- `BOQDocument` — Sections, LineItems, Totals
- `BOQDiff` — RateChanged, QtyChanged, NewItems, RemovedItems

## Example graphs

Five sample graphs ship under `StingTools.Dynamo/Samples/`:

1. **01_DailyQA.dyn** — select all rooms → AutoTag → Validate → report
2. **02_FullFabricationRun.dyn** — select services → auto-size → auto-drop
   → auto-sleeve → fabrication package → export cut list
3. **03_BOQSnapshot.dyn** — build BOQ → save snapshot → compare to
   last → export diff Excel
4. **04_MEPSystemAudit.dyn** — naming audit → live fill → pressure drop
   analyse → report
5. **05_DeliverableReadiness.dyn** — ISO19650 audit → tag completeness
   → sheet compliance → COBie export → pass/fail gate

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `DynamoVisualProgramming.ZeroTouchLibrary` | 2.17+ | ZeroTouch attribute model |
| `DynamoVisualProgramming.Core` | 2.17+ | NodeCategory, IsVisibleInDynamoLibrary |
| `RevitAPI` / `RevitAPIUI` | 2025 | Revit element types |
| `StingTools` | current | IExternalCommand implementations |

## Build + deploy

```bat
rem Windows only — Linux sandbox cannot resolve Dynamo NuGet
cd StingTools.Dynamo
dotnet build -c Release

rem Deploy
xcopy /Y bin\Release\net48\StingTools.Dynamo.dll ^
  "%APPDATA%\Dynamo\Dynamo Revit\2.17\packages\STING Tools\bin\"
xcopy /Y pkg.json ^
  "%APPDATA%\Dynamo\Dynamo Revit\2.17\packages\STING Tools\"
xcopy /Y /E Samples\*.* ^
  "%APPDATA%\Dynamo\Dynamo Revit\2.17\packages\STING Tools\samples\"
```

## Integration contract

Every node follows the same pattern:

```csharp
[IsVisibleInDynamoLibrary(true)]
[NodeCategory("STING Tools.BIM.Tag")]
public static TagResult AutoTag(Revit.Elements.View view)
{
    var evHandler = new StingCommandHandler.AutoTagDelegate(view);
    StingCommandHandler.ExternalEvent.Raise();
    WaitForCompletion();
    return new TagResult(StingCommandHandler.LastResult);
}
```

1. Node receives Dynamo-wrapped types (`Revit.Elements.*`)
2. Unwraps to raw `Autodesk.Revit.DB.Element`
3. Sets a delegate + raises `StingCommandHandler.ExternalEvent`
4. Waits for completion on a `ManualResetEvent`
5. Wraps the result in a Dynamo-friendly type
