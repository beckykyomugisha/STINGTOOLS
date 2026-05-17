# Unreachable Commands Triage

Triage of `IExternalCommand` classes that are not referenced from the dock-panel
button-tag dispatcher in `StingTools/UI/StingCommandHandler.cs`.

- **Total IExternalCommand classes**: 1,288
- **Wired in `StingCommandHandler.cs`**: 1,162
- **Not in dock-panel dispatcher**: **126**

> **Phase 177 update (complete)**: All 23 original Category C commands have been
> resolved. Summary:
>
> - **Wired with new tags + XAML buttons** (10): 5 AVF heatmap (`Heatmap_*`), 5 V6
>   (`V6_*`), 3 AEC filter (`AecFilters_*`), `DrawingBrowserOrganizerCommand`
>   (`DrawingTypes_BrowserOrganize`), `BCFSyncCommand` (`BCFSync`),
>   `RevisionCloudAuditCommand` (`RevisionCloudAudit`),
>   `PluginOnboardingWizardCommand` (`PlanscapeOnboarding`)
> - **Wired with secondary alias tags only** (3): `DrawingSyncStylesCommand`
>   (`DrawingTypes_SyncStylesDirect`), `DrawingForceResyncCommand`
>   (`DrawingTypes_ForceResync`), `GenerateFromScopeBoxesCommand`
>   (`DrawingTypes_ScopeBoxesDirect`) — primary tags already had inline
>   implementations and XAML buttons
> - **Left as dead code** (3): `BatchPrintSheetsCommand`,
>   `ClashDetectionCommand` (Temp variant), `PanelScheduleCommand` (Temp
>   variant) — their dispatcher tags already route to newer commands; old
>   classes compile harmlessly and can be deleted in a future cleanup sprint
>   once confirmed no external tooling imports them directly

## Counts

| Category | Count |
|---|---|
| **A** — Wired via alternative entry points (NOT dead) | **103** |
| **A'** — Newly wired in Phase 177 | **20** |
| **B** — V4 MVP deferred (intentionally not wired) | **0** |
| **C** — Superseded dead code (compiler-visible, runtime-unreachable) | **3** |
| **Total** | **126** |

> Category B is zero because every V4 MVP command under
> `StingTools/Commands/{Placement,Routing,Validation,Fabrication}/` and
> `StingTools/Core/{Placement,Routing,Validation,Fabrication}/` is already
> reached from the dock-panel dispatcher (TAGS Studio Fixtures/Routing/
> Fabrication sub-tabs). The V4 MVP work has been promoted out of the
> "deferred" bucket and is no longer dead-code-shaped.

---

## Category A — Wired via alternative entry points (103)

Three alternative dispatch surfaces exist alongside the primary
`StingCommandHandler`:

1. **`UI/StingElectricalCommandHandler.cs`** — electrical dock sub-panel
2. **`UI/Plumbing/StingPlumbingCommandHandler.cs`** — plumbing dock sub-panel
3. **`Core/WorkflowEngine.cs`** + workflow JSON presets — `ResolveCommand(tag)` + 8 `WORKFLOW_*.json` step lists
4. **`Core/StingToolsApp.cs`** — ribbon `PushButton` registration via `typeof(X).FullName`
5. **Static-state dialog** — `UI/CircuitWizardDialog.xaml.cs` passes state to the command class

### A.1 Electrical + Plumbing sub-dispatchers (79)

| Class | Dispatcher |
|---|---|
| `AicRatingCommand` | Electrical |
| `ArcFlashLabelSheetCommand` | Electrical |
| `ArcFlashScheduleCommand` | Electrical |
| `AssignPhotometricCommand` | Electrical |
| `AutoSizeDrainageCommand` | Plumbing |
| `AutoUpsizeWiresCommand` | Electrical |
| `BackflowAuditCommand` | Plumbing |
| `BreakerSizerApplyCommand` | Electrical |
| `BreakerSizerCommand` | Electrical |
| `BusbarModelingCommand` | Electrical |
| `CableSizerCommand` | Electrical |
| `CircuitDescriptionCommand` | Electrical |
| `ConduitAutoRouteCommand` | Electrical |
| `ConduitFillValidateCommand` | Electrical |
| `CrossConnectionScanCommand` | Plumbing |
| `DIALuxExportCommand` | Electrical |
| `DeadLegScanCommand` | Plumbing |
| `DemandFactorReportCommand` | Electrical |
| `DialuxRoundTripCommand` | Electrical |
| `EasyPowerExportCommand` | Electrical |
| `ElecCircuitRenumberCommand` | Electrical |
| `ElecLightingScheduleCommand` | Electrical |
| `ElecLoadSummaryCommand` | Electrical |
| `ElecPanelParamSyncCommand` | Electrical |
| `ElecPanelWriteParamsCommand` | Electrical |
| `EmergencyLightingAuditCommand` | Electrical |
| `EmergencyLightingMarkCommand` | Electrical |
| `EtapExportCommand` | Electrical |
| `FaultCurrentCommand` | Electrical |
| `FaultCurrentScheduleCommand` | Electrical |
| `FeederSizerCommand` | Electrical |
| `IfcResultsImportCommand` | Electrical |
| `LightingPowerDensityCommand` | Electrical |
| `LpdColorCommand` | Electrical |
| `MaterialAuditCommand` | Plumbing |
| `MultiEngineAggregatorCommand` | Electrical |
| `PRVScheduleCommand` | Plumbing |
| `PanelViewScheduleCommand` | Electrical |
| `PhaseBalanceCommand` | Electrical |
| `PhotometricDesignReviewCommand` | Electrical |
| `PhotometricLibraryCommand` | Electrical |
| `PhotometricLinkCommand` | Electrical |
| `PhotometricPreflightCommand` | Electrical |
| `PlumbAutoRouteCommand` | Plumbing |
| `PlumbBOQCommand` | Plumbing |
| `PlumbCommPackCommand` | Plumbing |
| `PlumbExpVesselCommand` | Plumbing |
| `PlumbFixSlopesCommand` | Plumbing |
| `PlumbFullAuditCommand` | Plumbing |
| `PlumbInsertPTrapsCommand` | Plumbing |
| `PlumbInvertLevelsCommand` | Plumbing |
| `PlumbIsometricCommand` | Plumbing |
| `PlumbLoadSystemConfigCommand` | Plumbing |
| `PlumbManholeScheduleCommand` | Plumbing |
| `PlumbPipeScheduleCommand` | Plumbing |
| `PlumbPlaceHangersCommand` | Plumbing |
| `PlumbPlaceSleevesCommand` | Plumbing |
| `PlumbPressureCheckCommand` | Plumbing |
| `PlumbRoofDrainageCommand` | Plumbing |
| `PlumbRwhCommand` | Plumbing |
| `PlumbSaveSystemConfigCommand` | Plumbing |
| `PlumbScanFixturesCommand` | Plumbing |
| `PlumbSepticTankCommand` | Plumbing |
| `PlumbSizeDrainageCommand` | Plumbing |
| `PlumbSizeSupplyCommand` | Plumbing |
| `PlumbSoakawayCommand` | Plumbing |
| `PlumbSuDSCommand` | Plumbing |
| `PlumbTMVRegisterCommand` | Plumbing |
| `PlumbVentDesignCommand` | Plumbing |
| `RainwaterCalcCommand` | Plumbing |
| `RecircBalanceCommand` | Plumbing |
| `SLDRiserDiagramCommand` | Electrical |
| `SLDUpdateRiserCommand` | Electrical |
| `SelectiveCoordCommand` | Electrical |
| `StackCapacityCommand` | Plumbing |
| `TrapAndVentAuditCommand` | Plumbing |
| `VoltageDropCommand` | Electrical |
| `VoltageDropFlagCommand` | Electrical |
| `VoltageDropScheduleCommand` | Electrical |

### A.2 WorkflowEngine dispatch (8)

These are referenced by `WorkflowEngine.ResolveCommand(tag)` in
`Core/WorkflowEngine.cs` and/or invoked from `WORKFLOW_*.json` presets.

| Class | Entry point |
|---|---|
| `BatchAssignCircuitsCommand` | `WorkflowEngine.ResolveCommand` |
| `BuildSeedFamiliesCommand` | `WorkflowEngine.ResolveCommand` + `WORKFLOW_ElectricalQA.json` |
| `CableScheduleBuilderCommand` | `WorkflowEngine.ResolveCommand` |
| `ConduitConsolidatorCommand` | `WorkflowEngine.ResolveCommand` |
| `ElectricalStandardsValidatorCommand` | `WorkflowEngine.ResolveCommand` |
| `PlanscapeDisconnectCommand` | `WorkflowEngine.ResolveCommand` |
| `PlanscapeOpenWebCommand` | `WorkflowEngine.ResolveCommand` |
| `SwapToManufacturerCommand` | `WorkflowEngine.ResolveCommand` |

### A.3 Ribbon `PushButton` registrations (15)

These are registered as Revit ribbon push-buttons in
`Core/StingToolsApp.cs` via `typeof(X).FullName` and dispatched directly
by Revit (no tag-string lookup involved).

| Class | Entry point |
|---|---|
| `HubAutoTagCommand` | Ribbon `AutoTag` |
| `HubBIMCoordCenterCommand` | Ribbon `BIMCoordCenter_Open` |
| `HubBoqExportCostCommand` | Ribbon button (StingToolsApp.cs) |
| `HubCreateTagFamiliesCommand` | Ribbon button (StingToolsApp.cs) |
| `HubDocumentMgmtCommand` | Ribbon `DocumentMgmt_Open` |
| `HubDrawingTypesCommand` | Ribbon `DrawingTypes_Edit` |
| `HubFabricationCommand` | Ribbon `Fabrication_Open` |
| `HubPlacementCommand` | Ribbon `Placement_Open` |
| `HubSchedulingDashboardCommand` | Ribbon button (StingToolsApp.cs) |
| `HubSheetManagerCommand` | Ribbon button (StingToolsApp.cs) |
| `HubStructuralDwgWizardCommand` | Ribbon button (StingToolsApp.cs) |
| `HubTag3DCommand` | Ribbon `Tag3D` |
| `ToggleDockPanelCommand` | Ribbon `STING Panel` button |
| `ToggleElectricalPanelCommand` | Ribbon `btnToggleElectrical` button |
| `TogglePlumbingPanelCommand` | Ribbon `btnTogglePlumbing` button |

### A.4 Static-state dialog interaction (1)

| Class | Entry point |
|---|---|
| `CircuitWizardCommand` | `UI/CircuitWizardDialog.xaml.cs` writes `PendingCircuits`/`PendingPanelName` static fields, user then invokes the command from the dialog |

---

## Category B — V4 MVP deferred (0)

**No entries.** Every command in the V4 MVP folders
(`Commands/{Placement,Routing,Validation,Fabrication}/` and
`Core/{Placement,Routing,Validation,Fabrication}/`) is already reachable
from the dock-panel TAGS Studio sub-tabs (Fixtures / Routing / Fabrication).
The V4 work has been promoted out of the "deferred / not yet wired" bucket.

If new V4 work lands without dispatcher wiring, list it here.

| Class | Directory | TODO-VERIFY-API |
|---|---|---|
| — | — | — |

---

## Category C — Genuinely dead (23)

These commands are not referenced from any dispatcher, the
WorkflowEngine, an NLP pattern, a ribbon push-button, the
`CircuitWizardDialog`, or any workflow JSON preset. The only references
in the codebase are the class declaration plus (in a handful of cases)
free-text comments referring to the class by name.

**Deletion candidates — a future phase will decide.** Cross-reference
with `docs/CHANGELOG.md` before removing anything: several of these are
documented as the "engine class" for a feature whose tag now redirects
to a newer command.

- `AecFiltersCreateCommand` — `Commands/Drawing/AecFilterCommands.cs`. Mentioned in CLAUDE.md Phase 166 but no tag wired.
- `AecFiltersInspectCommand` — `Commands/Drawing/AecFilterCommands.cs`.
- `AecFiltersReloadCommand` — `Commands/Drawing/AecFilterCommands.cs`.
- `ApplyLabourHoursCommand` — `V6/LabourHoursCommands.cs`. V6 feature, no V6 dispatcher exists.
- `BCFSyncCommand` — `BIMManager/PlatformLinkCommands.cs`.
- `BatchPrintSheetsCommand` — `Docs/SheetTemplateCommands.cs`. **Replaced** — dispatcher tag `"BatchPrintSheets"` now redirects to `Docs.ExportCenterPdfCommand`.
- `ClashDetectionCommand` — `Temp/DataPipelineCommands.cs`. **Replaced** — dispatcher tag `"ClashDetection"` now resolves to `Core.Clash.ClashRunCommand`.
- `ClearHeatmapCommand` — `Commands/Visualization/AvfHeatmapCommands.cs`.
- `DrawingBrowserOrganizerCommand` — `Commands/Drawing/DrawingBrowserOrganizerCommand.cs`. Mentioned in CLAUDE.md Phase 113 Week 3, but the dock panel does not wire it.
- `DrawingForceResyncCommand` — `Commands/Drawing/DrawingSyncStylesCommand.cs`.
- `DrawingSyncStylesCommand` — `Commands/Drawing/DrawingSyncStylesCommand.cs`. **Bypassed** — dispatcher tag `"DrawingTypes_SyncStyles"` calls inline `DrawingTypesSyncStylesInline(app)` instead of the class.
- `ExportLabourHoursCommand` — `V6/LabourHoursCommands.cs`. V6 feature.
- `GenerateFromScopeBoxesCommand` — `Commands/Drawing/GenerateFromScopeBoxesCommand.cs`. **Bypassed** — dispatcher tag `"DrawingTypes_FromScopeBoxes"` calls inline `DrawingTypesFromScopeBoxesInline(app)` instead.
- `HealthDashboardExportHtmlCommand` — `V6/HealthDashboardEngine.cs`. V6 feature.
- `PanelScheduleCommand` — `Temp/MEPScheduleCommands.cs`. **Replaced** — dispatcher tag `"PanelSchedule"` now redirects to `Commands.Panels.BatchPanelSchedulesCommand`.
- `PluginOnboardingWizardCommand` — `BIMManager/PluginOnboardingWizardCommand.cs`.
- `QRAdvanceCommissioningCommand` — `V6/QRCommissioningCommands.cs`. Carries a `// TODO-VERIFY-API` comment but lives outside the V4 MVP tree, so technically Category C per the task spec.
- `QRCommissioningReportCommand` — `V6/QRCommissioningCommands.cs`.
- `RevisionCloudAuditCommand` — `BIMManager/RevisionManagementCommands.cs`. Dispatcher tag `"RevisionCloudAuto"` points at `Docs.RevisionCloudAutoCreateCommand` — the audit variant is dead.
- `VisualiseAcousticHeatmapCommand` — `Commands/Visualization/AvfHeatmapCommands.cs`.
- `VisualiseCarbonHeatmapCommand` — `Commands/Visualization/AvfHeatmapCommands.cs`.
- `VisualiseComplianceHeatmapCommand` — `Commands/Visualization/AvfHeatmapCommands.cs`.
- `VisualiseFillHeatmapCommand` — `Commands/Visualization/AvfHeatmapCommands.cs`.

### Sub-clusters worth noting

- **AVF Heatmap suite** (5): `ClearHeatmap`, `VisualiseAcoustic`, `VisualiseCarbon`, `VisualiseCompliance`, `VisualiseFill`. All five live in one file and form a coherent feature that simply has no dispatcher wiring. Either wire them or delete the file together.
- **AEC Filter library suite** (3): `AecFiltersCreate`/`Inspect`/`Reload`. CLAUDE.md describes Phase 166 commands by these names — wiring is the likely fix, not deletion.
- **V6 commands** (5): `ApplyLabourHours`, `ExportLabourHours`, `HealthDashboardExportHtml`, `QRAdvanceCommissioning`, `QRCommissioningReport`. The entire `StingTools/V6/` tree is unreachable; either build the V6 dispatcher or delete the tree.
- **Drawing helper commands bypassed by inline reimplementations** (3): `DrawingSyncStyles`, `DrawingForceResync`, `GenerateFromScopeBoxes`. The dispatcher does the same work inline; the standalone classes drifted out of use.
- **Replaced by newer commands** (4): `BatchPrintSheets`, `ClashDetection` (Temp variant), `PanelSchedule` (Temp variant), `RevisionCloudAudit`. These are now superseded; safe to remove once a downstream check confirms no tooling still imports them.

---

## Methodology

1. Extracted every `class X : IExternalCommand` declaration from `StingTools/**/*.cs` (1,288 classes total).
2. Extracted every `\bX[A-Za-z0-9_]*Command\b` token from `UI/StingCommandHandler.cs` (the dock-panel dispatcher) — 1,162 classes referenced.
3. Subtraction yielded **126** unreachable classes.
4. For each unreachable class, searched in priority order:
   - `UI/StingElectricalCommandHandler.cs`, `UI/Plumbing/StingPlumbingCommandHandler.cs`, `UI/CommandRegistry.cs`
   - `Core/WorkflowEngine.cs` (`ResolveCommand` + workflow definitions)
   - `Tags/NLPCommandProcessor.cs`
   - `Data/WORKFLOW_*.json` (10 preset files)
   - `Core/StingToolsApp.cs` (ribbon `PushButton` registrations via `typeof(X).FullName`)
   - Every other `.cs` file for `new X(`, `X.Execute(`, or `X.<static>` references
5. Verified that string-only comment references (`// ... X ...`) are NOT counted as wiring.
