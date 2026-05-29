# STING UI Dedupe Audit (Phase 2)

**Branch:** `fix/dedupe-ui-commands` (off `main`)
**Status:** DO set (option a) executed — see **EXECUTION OUTCOME** at the end. Build 0/0.

## Method (derived, not from hints)

- Parsed the 1,438 `RunCommand<T>(app)` dispatch cases in `UI/StingCommandHandler.cs`,
  mapping each button **tag → command class**.
- **Verified dispatch mechanics:** `RunCommand<T>(app)` calls `new T().Execute(null, …)`
  and passes **no tag/param** to the command. Therefore any two *plain* single-line
  cases (`case "X": RunCommand<T>(app); break;`) that hit the same `T` run **byte-identical
  behaviour**. Cases that `SetExtraParam(...)` before dispatch (e.g. `WorkflowPreset_*`,
  `RunWorkflow_*`) are **parameterised dispatch, NOT duplicates** — excluded.
- Cross-checked `UI/StingDockPanel.xaml` for the same `Tag` on multiple buttons/tabs.
- Token-scanned all 1,543 `IExternalCommand` classes for zero code references (dead code).

**Totals found:** 82 same-command tag clusters · 106 tags repeated in XAML (120 extra
buttons) · 25 unreferenced command classes.

---

## Group 1 — SAFE MERGE: synonym entry points (identical action, recommend remove alias)

Two+ tags that are plain synonyms for one action. Keep the canonical (clearer) tag;
remove the alias case + its button. **Confidence: HIGH** unless noted.

| Command | Keep tag | Remove alias tag(s) | Confidence | Note |
|---|---|---|---|---|
| `Docs.SumAreasCommand` | `SumAreas` | `MeasureRoomAreas` | High | |
| `Docs.RenumberViewportsCommand` | `RenumberViewports` | `VPNumTB` | High | |
| `Docs.SheetIndexCommand` | `SheetIndex` | `GenSheetIndex`, `SheetRegister` | High | 3→1 |
| `Docs.SheetNamingCheckCommand` | `SheetNamingCheck` | `NamingConventionAudit` | High | |
| `Docs.AssetHealthReportCommand` | `AssetHealthReport` | `AssetHealth` | High | |
| `Docs.HandoverManualCommand` | `HandoverManual` | `FMHandover` | High | |
| `Docs.MaintenanceScheduleExportCommand` | `MaintenanceScheduleExport` | `MaintenanceSchedule` | High | |
| `Docs.OAndMManualExportCommand` | `OAndMManualExport` | `OMManual` | High | |
| `Docs.SpaceHandoverReportCommand` | `SpaceHandoverReport` | `SpaceHandover` | High | |
| `Docs.DrawingRegisterCommand` | `DrawingRegister` | `DocsDrawingRegister` | High | |
| `Docs.TransmittalCommand` | `Transmittal` | `ComplianceGateTransmittal` | Med | alias implies a gate step — confirm it isn't meant to chain |
| `BIMManager.MilestoneRegisterCommand` | `MilestoneRegister` | `ExportMilestones` | High | |
| `BIMManager.CashFlow5DCommand` | `CashFlow5D` | `ExportCashFlow` | High | |
| `BIMManager.RevisionExportCommand` | `RevisionExport` | `RevisionExportXlsx` | High | |
| `BIMManager.StageComplianceGateCommand` | `StageComplianceGate` | `StageGate` | High | |
| `BIMManager.WorkingCalendarCommand` | `WorkingCalendar` | `SaveWorkingCalendar` | Med | alias implies save-only |
| `BIMManager.ExportPermissionMatrixCommand` | `ExportPermissionMatrix` | `TeamReport` | Med | label differs |
| `Core.Clash.ClashRunCommand` | `ClashRun` | `ClashDetect`, `ClashDetection` | High | 3→1 (also 4 XAML buttons) |
| `Core.ListWorkflowPresetsCommand` | `ListWorkflowPresets` | `ListWorkflows` | High | |
| `Commands.Drawing.DrawingTypesInspectCommand` | `DrawingTypes_Inspect` | `DrawingTypesBrowse` | High | |
| `Commands.Panels.BatchPanelSchedulesCommand` | `Panel_BatchSchedules` | `PanelSchedule` | High | |
| `Tags.BatchTagCommand` | `BatchTag` | `TagAll` | High | |
| `Tags.TagAndCombineCommand` | `TagAndCombine` | `BuildAll` | High | |
| `Tags.CombineParametersCommand` | `CombineParameters` | `MatTags` | Med | `MatTags` label implies material tags |
| `Tags.ConfigEditorCommand` | `ConfigEditor` | `ConfigureTagFormat` | High | |
| `Tags.LoadSharedParamsCommand` | `LoadSharedParams` | `FixContainers` | Med | alias label differs |
| `Tags.BuildTagsCommand` | `BuildTags` | `T3Tags` | High | |
| `Organise.AuditTagsCSVCommand` | `AuditTagsCSV` | `AuditTags`, `AnomalyExport` | Med | `AnomalyExport` label differs |
| `Organise.ResetTagPositionsCommand` | `ResetTagPositions` | `OrgReset` | High | |
| `Organise.TagSelectedCommand` | `TagSelected` | `TagCat` | High | |
| `Organise.FixDuplicateTagsCommand` | `FixDuplicates` | `HealthFixAll` | Med | `HealthFixAll` implies broader fix |
| `Select.SelectTagsByDisciplineCodeCommand` | `SelectTagsByDisciplineCode` | `SelectTagsByDiscipline` | High | |
| `Temp.MechanicalEquipmentScheduleCommand` | `MechanicalEquipmentSchedule` | `MechEquipSchedule`, `MEPScheduleHVAC` | Med | see Group 3 (MEPSchedule* family) |
| `Temp.COBieAttributeTemplatesCommand` | `COBieAttributeTemplates` | `COBieAttributes` | High | |
| `Temp.COBieDocumentTypesCommand` | `COBieDocumentTypes` | `COBieDocTypes` | High | |
| `Temp.RoomBasedParamPushCommand` | `RoomBasedParamPush` | `RoomParamPush` | High | |
| `Temp.ExcelBOQImportCommand` | `ExcelBOQImport` | `ExcelImport` | Med | `ExcelImport` is generic-sounding |
| `BIMManager.COBieExportCommand` | `COBieExport` | `ExportCOBie` | High | (block case — verify) |

---

## Group 2 — CROSS-TAB / WORKSPACE RE-EXPOSURE (same command on a second surface)

These tags re-expose a core command inside a curated workspace (the **TAGS "Tag Studio"**
sub-tabs, a **"Bot"/"Brain"/"Org"** assistant surface, or the **STING Hub** ribbon tiles).
They are *deliberate* alternate entry points, not accidental dupes. The user asked to flag
"buttons on multiple tabs pointing at the same command."

**Recommendation: KEEP by default** (removing them guts the workspace). Merge only if you
want a single entry point per feature.

| Command | Canonical tag | Workspace alias(es) |
|---|---|---|
| `Tags.SmartPlaceTagsCommand` | `SmartPlaceTags` | `TagStudio_SmartPlace`, `BotSmartPlace`, `OrgBrainSp` |
| `Tags.ArrangeTagsCommand` | `ArrangeTags` | `TagStudio_Arrange`, `BrainTidy`, `BrainUncross`, `SmartOrganise` |
| `Tags.ApplyColorSchemeCommand` | `ApplyColorScheme` | `TagStudio_ApplyScheme` |
| `Tags.ClearColorSchemeCommand` | `ClearColorScheme` | `TagStudio_ClearOverrides` |
| `Tags.ApplyTagStyleCommand` | `ApplyTagStyle` | `TagStudio_ApplyStyle` |
| `Tags.AlignTagBandsCommand` | `AlignTagBands` | `TagStudio_AlignBands` |
| `Tags.FamilyStagePopulateCommand` | `FamilyStagePopulate` | `TagStudio_Generate`, `TagStudioGenerate` |
| `Tags.ResolveAllIssuesCommand` | `ResolveAllIssues` | `TagStudio_GapReview`, `TagStudioGapReview` |
| `Tags.PreTagAuditCommand` | `PreTagAudit` | `TagStudio_APIGaps`, `TagStudioAPIGaps` |
| `Tags.ValidateTagsCommand` | `ValidateTags` | `TagStudio_Explain`, `AnomalyScan` |
| `Tags.CompletenessDashboardCommand` | `CompletenessDashboard` | `TagStudio_Pipeline`, `TagStudioPipeline`, `ComplianceScan`, `DiscComplianceReport`, `HealthScore` |
| `Tags.TagOverlapAnalysisCommand` | `TagOverlapAnalysis` | `BotDensityMap`, `ClashingDetect` |
| `Tags.TagConfigCommand` | `TagConfig` | `BotOptions` |
| `Tags.LearnTagPlacementCommand` | `LearnTagPlacement` | `PatternLearn` |
| `Tags.ApplyTagTemplateCommand` | `ApplyTagTemplate` | `PatternApplyLearned` |
| `Tags.BatchPlaceTagsCommand` | `BatchPlaceTags` | `MultiView` |
| `Organise.AlignTagsCommand` | (Align) | `ArrangeRadial`, `BrainSmAl`, `LeaderSpacing` |
| `Organise.ToggleTagOrientationCommand` | `ToggleTagOrientation` | `BrainSmHV`, `BrainSmOr` |
| `Organise.AddLeadersCommand` | `AddLeaders` | `LeaderAdd`, `LeaderCombine` |
| `Organise.TagStatsCommand` | `TagStats` | `AnalyseScore`, `HealthReport` |

> ⚠️ Several "alias" names here imply *distinct* analyses (e.g. `AnalyseClashes` vs
> `AnalyseDensity` vs `AnalyseCrossings` all → `TagOverlapAnalysis`; `ComplianceScan`
> vs `HealthScore` vs `DiscComplianceReport` all → `CompletenessDashboard`). At runtime
> they do the **same** thing. These straddle Group 2/Group 3 — see Group 3.

---

## Group 3 — DO NOT MERGE: distinct-intent tags mis-wired to one command (bug/stub, NOT redundancy)

Multiple tags whose **names promise different features** but all hit one command (no
param differentiation), i.e. the "feature" was never implemented and the button is a
placeholder/mis-wire. **Removing these hides a gap; do NOT auto-merge.** Recommend: leave
as-is and log as a separate "missing feature / mis-wire" backlog for you to triage.

| Command (actually runs) | Tags that promise something else |
|---|---|
| `BIMManager.ExportCoordLogCommand` | `MeetingTemplates`, `PlanscapeExportConfig`, `PlanscapeExportTeam`, `ViewPlatformLogs` (only `ExportCoordLogXlsx` is on-label) |
| `BIMManager.PlanscapeConnectCommand` | `PlanscapeAddMember`, `PlanscapeLinkProject`, `PlanscapeRemoveMember`, `PlanscapeTestConnection` (distinct Planscape actions all → Connect) |
| `BIMManager.RaiseIssueCommand` | `AssignIssues`, `CreateIssuesFromWarnings` (distinct from `RaiseIssue`) |
| `BIMManager.DocumentRegisterCommand` | `ApprovalWorkflow` (distinct from register) |
| `BIMManager.BCFExportCommand` | `WebhookPayload` (unrelated) |
| `BIMManager.GenerateDashboardCommand` | `PlanscapeShareReport` (distinct) |
| `Core.DeliverableMatrixCommand` | `BulkDeliverableStatus`, `ExportDeliverablesRegister` |
| `Tags.QRCodeCommand` | `GenerateQRSheet`, `PrintQRTags`, `PlanscapeQR`, `PlanscapeQRCode` (sheet vs tag vs single QR — likely meant to differ) |
| `Temp.UniclassClassifyCommand` | `UnicodeValidator` (almost certainly a typo/mis-wire — Unicode ≠ Uniclass), `UniclassValidator`, `ClassificationAudit` |
| `Temp.*ScheduleCommand` (MEP family) | `MEPScheduleAll`, `MEPScheduleElec`, `MEPScheduleFire`, `MEPScheduleHVAC`, `MEPSchedulePlumb` map to per-discipline schedule commands — these ARE distinct (keep); flagged only because the canonical names also exist |
| `Temp.AssetConditionCommand` | `IoTSensorLink` (distinct from asset condition) |
| `Temp.DigitalTwinExportCommand` | `IoTHistoryExport` (distinct) |
| `Temp.EnergyAnalysisCommand` | `IoTAlertConfig` (distinct) |
| `Temp.Bs7671ComplianceCommand` | `BS1192Checker` (BS 1192 ≠ BS 7671 — mis-wire) |
| `Temp.Iso19650DeepComplianceCommand` | `ISO19650Checker` (could be alias OR distinct depth) |
| `Temp.COBieDataSummaryCommand` | `COBieValidator` (summary vs validate — likely meant to differ) |
| `Docs.ExportCenterPdfCommand` | `BatchPrintSheets` (also note `BatchPrintSheets` appears as its own feature elsewhere) |
| `Docs.AlignViewportsCommand` | `VPAlignRight` (implies right-align specifically) |
| `Docs.BatchRenameViewsCommand` | `SheetFindReplace` (sheets vs views) |
| `Select.ColorByParameterCommand` | `GradientApply` (gradient vs categorical) |
| `Organise.SelectByDisciplineCommand` | `SaveExtendedBaseline` (completely unrelated — mis-wire) |

---

## Group 4 — CROSS-COMMAND functional duplicates (different classes, same job)

Different command **classes** offering the same feature via different surfaces.

| Feature | Entry points | Recommendation |
|---|---|---|
| Material management | `MaterialManager` → `Temp.StingMaterialManagerCommand` (modal quick dialog) · `MaterialManagerFull` → `Temp.MaterialManagerCommand` (7-tab dialog) · **Material Hub** dockable pane (`ToggleMaterialHub`) | **Confirm intent.** Three surfaces for one job. If the Hub pane supersedes both dialogs, retire `MaterialManager` (quick dialog) and keep `MaterialManagerFull` + Hub; or keep all three as quick/full/dockable. Needs your call. |

*(No other clean cross-command dupes detected with high confidence in this pass.)*

---

## Group 5 — UNREFERENCED command classes (no button/handler — dead or unwired)

25 `IExternalCommand` classes with **zero** code references (only their own declaration).
**These are almost all UNWIRED FEATURES (missing entry point), not redundant dupes** —
removing them would delete a feature. Recommend: **wire them up or confirm removal**, do
not silently delete.

```
Plumbing engines (10):  PlumbBoosterSetCommand, PlumbBuildNetworkCommand,
  PlumbGenerateSpoolsCommand, PlumbLegionellaReportCommand, PlumbNetworkPressureCommand,
  PlumbPumpSelectCommand, PlumbSlopeAutomationCommand, PlumbSpoolScheduleCommand,
  PlumbTMVEngineCommand, PlumbWaterSafetyPlanCommand
AVF heatmaps (4):       VisualiseAcousticHeatmapCommand, VisualiseCarbonHeatmapCommand,
  VisualiseComplianceHeatmapCommand, VisualiseFillHeatmapCommand
Compliance gates (5):   CoverageGateCommand, FireWallGateCommand, HealthcareGateCommand,
  SustainabilityGateCommand  (+ DrawingForceResyncCommand)
Drawing/AEC (3):        AecFiltersCreateCommand, AecFiltersReloadCommand,
  DrawingBrowserOrganizerCommand
Onboarding (1):         PluginOnboardingWizardCommand
ORPHANED DUPES (2):     HubHvacPanelCommand, HubMaterialHubCommand
```

**`HubHvacPanelCommand` + `HubMaterialHubCommand` are TRUE dead code** — their Hub tiles
were removed in the ribbon-consolidation work, leaving the classes unreferenced. These two
are safe to delete (the live toggles are `ToggleHvacPanelCommand` / `ToggleMaterialHubCommand`).
The other 23 are unwired features — your call.

---

## Group 6 — XAML duplicate tags (multi-tab buttons)

106 tags appear on 2+ buttons in `StingDockPanel.xaml` (120 extra buttons). The bulk are
Group 2 workspace re-exposures (Tag Studio sub-tabs, Bot/Brain panels) plus a few
intentional convenience placements (`ClashDetect` ×4, `TagAndCombine` ×3, `COBieExport` ×3,
`BOQExport` ×3, `Placement_OpenCentre` ×3, `Fabrication_OpenWorkspace` ×3,
`FamilyStagePopulate` ×3). When a Group 1 alias is removed, its duplicate XAML buttons are
removed with it; the rest stay (deliberate placement).

---

## Recommended action set (for your confirmation)

- **DO:** Group 1 synonym merges marked **High** confidence (~28 alias tags + their
  buttons). Keep canonical case, remove alias case + alias XAML button(s).
- **DO:** Delete the 2 orphaned dead classes `HubHvacPanelCommand`, `HubMaterialHubCommand`.
- **ASK FIRST:** Group 1 **Med**-confidence aliases (label implies a difference).
- **ASK FIRST:** Group 4 material-management consolidation.
- **DON'T (without triage):** Group 2 workspace re-exposures (intentional UX), Group 3
  mis-wires (hide gaps), Group 5 unwired features (except the 2 orphans above).
- **NOT TOUCHED:** Scale sliders (per instruction).

**Net if you approve the "DO" set:** ~28 alias tags + ~30 duplicate buttons removed,
2 dead classes removed; 0 features lost (every removed entry has a surviving canonical).

---

## EXECUTION OUTCOME (DO set, option a)

During execution the dispatch architecture was found to be richer than the
switch-only audit modeled: `StingCommandHandler.Execute` tries
**`CommandRegistry` (UI/Modules/*CommandModule.cs) FIRST**, falling back to the
big switch; and the BIM Coordination Center / Document Management dialogs build
their **own** code buttons using these tags, while `NLPCommandProcessor`,
`WorkflowEngine`, `WarningsManager` and `OperationRegistry` reference some too.

A tag was treated as a **true redundant dock button** (safe to remove) ONLY when:
its surfaces were limited to a `StingDockPanel.xaml` button (+ its switch case,
+ at most one `*CommandModule` registration) AND the **canonical** had its own
dock button so the feature stays reachable. Aliases that also appear in BCC /
dialogs / NLP / WorkflowEngine / WarningsManager are **deliberate cross-surface
entry points** (Group 2), and aliases whose canonical had *no* dock button were
the **sole** surface for that feature — both classes were left untouched.

### Merged / removed (9 alias entry points + 2 dead classes)

| Removed alias | Kept canonical | Removed surfaces |
|---|---|---|
| `MeasureRoomAreas` | `SumAreas` | dock button + switch case |
| `NamingConventionAudit` | `SheetNamingCheck` | dock button + switch case |
| `TagAll` | `BatchTag` | dock button + switch case |
| `BuildAll` | `TagAndCombine` | dock button + switch case |
| `T3Tags` | `BuildTags` | dock button + switch case |
| `GenSheetIndex` | `SheetIndex` | dock button + switch case + DocsCommandModule reg |
| `ConfigureTagFormat` | `ConfigEditor` | dock button + switch case + TagsCommandModule reg |
| `TagCat` | `TagSelected` | dock button + switch case + OrganiseCommandModule reg |
| `OrgReset` | `ResetTagPositions` | dock button + switch case + OrganiseCommandModule reg |
| `HubHvacPanelCommand` (class) | `ToggleHvacPanelCommand` | dead class deleted |
| `HubMaterialHubCommand` (class) | `ToggleMaterialHubCommand` | dead class deleted |

**Counts:** kept 9 canonical features (0 features lost) · merged-away 9 duplicate
dock buttons (+9 switch cases, +4 module regs) · removed 2 dead classes.
Build: 0 errors / 0 warnings.

### Deferred from the original "DO" list (with reason) — for a module-aware round

- **Canonical has no dock button → alias is the SOLE surface (not a duplicate):**
  `OMManual`, `COBieAttributes`, `COBieDocTypes`, `RoomParamPush`,
  `SelectTagsByDiscipline`. Removing would delete the feature's only button.
- **Cross-surface deliberate entries (BCC / dialog / NLP / WarningsManager / Workflow):**
  `SheetRegister`, `FMHandover`, `ExportMilestones`, `ExportCashFlow`,
  `RevisionExportXlsx`, `StageGate`, `MaintenanceSchedule`, `SpaceHandover`,
  `DocsDrawingRegister`, `ClashDetection`, `ListWorkflows`, `AssetHealth`,
  `DrawingTypesBrowse`, `VPNumTB`, `PanelSchedule`, `ClashDetect` (×4 dock +
  pipeline ref), `ExportCOBie` (block case). These are Group 2 (intentional
  multi-surface) or need coordinated module+BCC+NLP edits — a dedicated round.
