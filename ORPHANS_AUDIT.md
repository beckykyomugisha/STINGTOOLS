# Orphaned Commands Audit (re-derived) — real working `IExternalCommand`s with NO button

**Branch:** `surface/orphan-commands` (off `main`).
**Status:** AUDIT-FIRST — **PAUSED for your confirmation.** No code changed yet.

## Method (all dispatch surfaces, not switch-only)

1. Enumerated **1,540** `IExternalCommand` classes.
2. Built a reference index of every place a command is **instantiated/dispatched**:
   `RunCommand<T>` · `RunCommandPublic<T>` (the `*CommandModule` registry path) ·
   `new XCommand(` · `typeof(XCommand)`. → 1,494 reachable.
3. Set difference → **51 never instantiated anywhere.** Added **3 more** that ARE
   registered in a `*CommandModule` but have **no button carrying the tag** (registered
   yet unsurfaced). → **54 candidates.**
4. Read every candidate's `Execute()` (4 parallel passes) to grade
   **COMPLETE / STUB / SUPERSEDED** and verified the tricky reachability cases by hand.

**This expands the DEDUPE Group-5 hypothesis (~25) to 54**, and corrects it: the Group-5
"compliance gates" and "AVF heatmaps" and "Plumb* engines" are all **real and complete**,
plus many more orphans Group-5 missed (P6 link, BCF sync, folder/cloud sync, BOQ/cost
sync, labour hours, QR commissioning, health-dashboard HTML, electrical clears, clash
xlsx, revision-cloud audit, tag-style migration, HBN auto-populate).

---

## A. SURFACE THESE — real, complete, currently unreachable (49)

### A1 · Plumbing engines → **STING Plumbing panel** (13)
| Class | Does what |
|---|---|
| `PlumbPumpSelectCommand` | Derives duty point, sizes pump from catalogue |
| `PlumbBoosterSetCommand` | Break-tank volume + booster-set duty sizing |
| `PlumbBuildNetworkCommand` | Builds pipe network graph, accumulates DFU/pressure, critical path |
| `PlumbNetworkPressureCommand` | Colours pipes by pressure zone, writes kPa params |
| `PlumbSlopeAutomationCommand` | Applies BS EN 12056-2 min slopes to drainage |
| `PlumbDrainageSchematicCommand` | Generates drainage riser schematic drafting view |
| `PlumbNetworkStatsCommand` | Reports network stats by system / DFU / fixtures |
| `PlumbPressureZoneCommand` | Colours supply pipes by pressure zone |
| `PlumbGenerateSpoolsCommand` | Groups pipes into spools, stamps numbers, builds assemblies |
| `PlumbSpoolScheduleCommand` | Creates/refreshes pipe-spool schedule view |
| `PlumbTMVEngineCommand` | Scans TMVs, exports CSV register |
| `PlumbLegionellaReportCommand` | ACOP L8 Legionella risk report (docx/text) |
| `PlumbWaterSafetyPlanCommand` | RAG dashboard: dead legs / TMV / backflow |

### A2 · AVF heatmaps → **main panel VIEW tab (Visualization)** (5)
`VisualiseAcousticHeatmapCommand`, `VisualiseCarbonHeatmapCommand`,
`VisualiseComplianceHeatmapCommand`, `VisualiseFillHeatmapCommand` (paint AVF heat-map on
active view) + `ClearHeatmapCommand` (clears it).

### A3 · Drawing / AEC filters → **DOCS tab** (6)
| Class | Does what |
|---|---|
| `AecFiltersCreateCommand` | Mints `ParameterFilterElement`s from JSON registry |
| `AecFiltersInspectCommand` | Lists filter defs + presence in active doc (read-only) |
| `AecFiltersReloadCommand` | Invalidates filter-registry cache |
| `DrawingBrowserOrganizerCommand` | Reports/sets browser-organization by Drawing Type |
| `GenerateFromScopeBoxesCommand` | Creates+crops views from `STING::` scope boxes |
| `DrawingForceResyncCommand` | Force-resyncs ALL stamped views (incl template-suppressed) |

### A4 · Material/QA compliance gates → **BIM tab (QA)** (4)
Thin adapters over `MatActions.Run*Gate` — verified those methods are called by **nothing
else** (the existing "Coverage"/"Sustainability" buttons are *different* features).
`CoverageGateCommand`, `FireWallGateCommand`, `HealthcareGateCommand`,
`SustainabilityGateCommand`.

### A5 · BIM coordination / platform / scheduling → **BIM tab** (11)
| Class | Does what | Note |
|---|---|---|
| `ExportFor4DViewerCommand` | Exports elements + phases to 4D-viewer JSON | — |
| `P6LiveLinkConfigCommand` | Saves Primavera P6 connection settings | needs server |
| `P6SyncNowCommand` | Triggers immediate P6 activity sync | needs P6 config |
| `P6WritebackCommand` | Writes P6 actual dates/% back to params | needs P6 server |
| `BCFSyncCommand` | Bidirectional STING issues ↔ BCF topics | — |
| `FolderCloudMirrorNowCommand` | One-shot mirror project folder to cloud | needs cloud cfg |
| `FolderCloudSyncSettingsCommand` | Dialog: configure cloud provider + auto-mirror | tag `Folder_CloudSync` **already registered**, no button |
| `PushBoqSnapshotCommand` | Pushes BOQ cost/qty snapshot to Planscape | needs Planscape |
| `CostFileBrowserCommand` | Browse/validate cost CSV, save override path | — |
| `RevisionCloudAuditCommand` | Audits revision clouds, groups by revision/sheet | — |
| `PlatformEventDrainCommand` | Manual trigger for the platform event-drainer poll | ⚠️ advanced/internal — confirm you want a user button |

### A6 · V6 next-gen → **BIM tab** (5)
`ApplyLabourHoursCommand` (apply labour hrs to selection), `ExportLabourHoursCommand`
(labour-hours CSV), `QRAdvanceCommissioningCommand` (advance commissioning state),
`QRCommissioningReportCommand` (commissioning progress CSV), `HealthDashboardExportHtmlCommand`
(model-health dashboard → HTML).

### A7 · Electrical clears → **STING Electrical panel** (2)
`ClearCircuitTraceCommand` (remove circuit-trace overrides), `ClearHomeRunAnnotationsCommand`
(remove home-run annotations).

### A8 · Clash → **BIM tab (Clash)** (1)
`ClashXlsxExportCommand` — exports `clashes.json` to filtered Excel.

### A9 · Tags / Healthcare (registered-but-no-button) (2)
| Class | Does what | Note |
|---|---|---|
| `MigrateTagStyleCodeCommand` | Migrates `TAG_STYLE_CODE_BOOL` params → text | tag `Tags_MigrateStyleCode` **already registered**, no button → **TAGS/CREATE tab** |
| `HbnRoomAutoPopulatorCommand` | Auto-populates HTM/HBN room design params | tag `HC_HbnAutoPopulate` **already registered**, no button → **Healthcare tab** |

> For A9 + `FolderCloudSyncSettingsCommand` (A5), surfacing = **just add a button** with
> the already-registered tag (no handler change). For A1–A8 I'll add a switch case (or
> module registration) + a button.

---

## B. DO NOT SURFACE — flagged (5) → also logged in ORPHANS_TODO.md

| Class | Verdict | Reason |
|---|---|---|
| `PluginOnboardingWizardCommand` | **STUB** | License-key + project-picker steps are stubs; not complete. |
| `DrawingSyncStylesCommand` | **SUPERSEDED** | Feature already surfaced — button "Sync Styles" (`DrawingTypes_SyncStyles`) runs the inline `DrawingTypesSyncStylesInline`. Surfacing the class would duplicate. |
| `BatchPrintSheetsCommand` | **SUPERSEDED** | Tag `BatchPrintSheets` deliberately dispatches to `ExportCenterPdfCommand` (PDF preset). See MISWIRE_AUDIT. |
| `CircuitWizardCommand` | **ALREADY SURFACED** | Reached via electrical-panel button "▶ Launch Wizard…" (`Circuit_CreateWizard` → dialog → command). Not an orphan at the UX level. |
| `ClashDetectionCommand` | **VERIFY/OVERLAP** | Real single-model MEP-vs-structure clash, but tags `ClashDetect`/`ClashDetection` go to `ClashRunCommand`. Likely overlaps the live clash feature. **Your call:** surface as a distinct entry, or leave (treat as superseded by `ClashRunCommand`). Default: leave. |

---

## Counts
- Candidates audited: **54** (51 never-instantiated + 3 registered-but-buttonless).
- **Surface (real & complete): 49** across A1–A9.
- Flagged (stub / superseded / already-surfaced / overlap): **5** (Section B).

## Proposed homes (summary)
Plumbing→**Plumbing panel** · heatmaps→**VIEW/Visualization** · AEC filters/drawing→**DOCS**
· compliance gates→**BIM/QA** · P6/BCF/folder/BOQ/cost/4D/revision/labour/QR/health→**BIM**
· electrical clears→**Electrical panel** · clash xlsx→**BIM/Clash** · tag-style migrate→**TAGS**
· HBN→**Healthcare**.

All new buttons will use the shared button style (`ActionBtn`/`GreenBtn`/panel-native
styles). TAGS → Scale sub-tab untouched.

**On your confirmation:** I add a button per A1–A9 item in its proposed location, build
0/0, no CompiledPlugin churn. Section-B items stay in ORPHANS_TODO.md (no surfacing)
unless you direct otherwise (e.g. greenlight `ClashDetectionCommand` or
`PlatformEventDrainCommand`).

---

## IMPLEMENTED (Group 5 surfacing)

Per the standing decision: surfaced the 48 (49 minus `PlatformEventDrainCommand`), **minus
1 more correction found during build** (`GenerateFromScopeBoxesCommand`, below) →
**47 surfaced**. Build 0 warnings / 0 errors (clean rebuild, Revit 2025). No CompiledPlugin
churn. TAGS → Scale untouched.

### Correction found during implementation (verifying live dispatch, not just instantiation)
- **`GenerateFromScopeBoxesCommand` is SUPERSEDED — not surfaced.** The tag
  `DrawingTypes_FromScopeBoxes` already dispatches to an inline method
  (`DrawingTypesFromScopeBoxesInline`), and `DrawingTypes_ProduceFromScopeBoxes` is its own
  command — the "create views from scope boxes" feature is already reachable, so a button
  would duplicate. Same false-positive class as `DrawingSyncStylesCommand` (the
  instantiation-based audit can't see inline reimplementations). Moved to TODO. **A3 = 5.**
- `DrawingBrowserOrganizerCommand` was **kept** — it actually *applies* a Project Browser
  organization (distinct from the `DrawingTypes_GroupBrowser` inline's group-report).

### Surfaced (47) — buttons added with shared styles

| Group | Count | Home | Tags |
|---|---|---|---|
| A1 Plumbing engines | 13 | **Plumbing panel** DOCS tab "Advanced engines"; cases in `StingPlumbingCommandHandler` | `Plumb_PumpSelect/BoosterSet/BuildNetwork/NetworkPressure/NetworkStats/PressureZone/SlopeAutomation/GenerateSpools/SpoolSchedule/DrainageSchematic/TMVEngine/LegionellaReport/WaterSafetyPlan` |
| A2 AVF heatmaps | 5 | **VIEW** tab "ANALYSIS HEATMAPS" | `Heatmap_Compliance/Fill/Carbon/Acoustic/Clear` |
| A3 Drawing / AEC | 5 | **DOCS** tab "AEC FILTERS + DRAWING TYPES" | `AecFilters_Create/Inspect/Reload`, `Drawing_BrowserOrganize`, `DrawingTypes_ForceResync` |
| A4 Compliance gates | 4 | **BIM** tab "COMPLIANCE GATES" | `Gate_Coverage/FireWall/Healthcare/Sustainability` |
| A5 Platform/sched/cost | 10 | **BIM** tab "PLATFORM · SCHEDULING · COST" | `Export4DViewer`, `P6_LinkConfig/SyncNow/Writeback`, `BCF_Sync`, `Folder_CloudSync` (reg) + `Folder_CloudMirrorNow`, `BOQ_PushSnapshot`, `Cost_FileBrowser`, `Revision_CloudAudit` |
| A6 V6 + A8 Clash | 6 | **BIM** tab "LABOUR · QR · HEALTH" | `Labour_Apply/Export`, `QR_AdvanceCommission`, `QR_CommissionReport`, `Health_DashboardHtml`, `Clash_XlsxExport` |
| A7 Electrical clears | 2 | **Electrical panel** circuit-tools row; cases in `StingElectricalCommandHandler` | `Circuit_ClearTrace`, `Circuit_ClearHomeRuns` |
| A9 Tag-style migrate | 1 | **TAGS → Tools** "MAINTENANCE" (tag pre-registered) | `Tags_MigrateStyleCode` |
| A9 HBN auto-populate | 1 | **Healthcare → Rooms / RDS** (tag pre-registered) | `HC_HbnAutoPopulate` |

Dispatch added: 29 cases in `StingCommandHandler` (one consolidated Group-5 block) + 13 in
`StingPlumbingCommandHandler` + 2 in `StingElectricalCommandHandler`. The 3 pre-registered
tags (`Folder_CloudSync`, `Tags_MigrateStyleCode`, `HC_HbnAutoPopulate`) got buttons only.
Gate classes are nested in `MaterialGateCommands` (fixed during build).

### Left as TODO (not surfaced)
- `PlatformEventDrainCommand` — internal manual event-drainer trigger (standing decision).
- `ClashDetectionCommand` — overlaps live `ClashRunCommand` (standing decision).
- `GenerateFromScopeBoxesCommand` — superseded by inline (correction above).
- Section-B items unchanged (`PluginOnboardingWizardCommand` stub; `DrawingSyncStylesCommand`
  / `BatchPrintSheetsCommand` / `CircuitWizardCommand` superseded / already-surfaced).

### Net
48 intended → **47 surfaced** (1 found superseded during build) · 3 TODO + Section-B flagged
· build 0/0 · no CompiledPlugin churn · Scale untouched.
