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
