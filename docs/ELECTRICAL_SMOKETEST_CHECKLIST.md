# STING Electrical — Comprehensive Smoketest Checklist

**Purpose:** prove every electrical function runs and produces the correct kind of output in a live Revit session.
**Scope:** ~130 commands across the STING Electrical Panel (7 tabs) + schematics, validation, export/import, placement.
**Legend:** `[RO]` = read-only (safe, no model change) · `[M]` = modifies model (transaction) · **Pre** = what must exist/be selected first · **Expect** = pass criterion · **Note** = known caveat/stub so a "no output" isn't mis-scored as a bug.

Mark each: **P** (pass) / **F** (fail) / **B** (blocked — precondition missing) / **N** (n/a).

---

## STEP 0 — Build, deploy, load

| ✓ | Step | Expect | Note |
|---|---|---|---|
| ☐ | Build Release: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` | 0 errors (baseline 0/0 warnings) | This machine can build. |
| ☐ | Deploy: copy `StingTools.dll` + deps + `data/` to the folder your installed `.addin` loads from (your GOLD deploy target `C:\Dev\STING_PLACEMENT_GOLD`) | DLLs replaced | **Close Revit first.** Deploy copies DLLs only — it does **not** touch your `.rfa` tag families in GOLD. |
| ☐ | Launch Revit → open a test model → ribbon **⚡ Electrical → STING Electrical** | Dockable panel opens with tabs: PNLS · CIRCTS · CALCS · CABLE · SLD · LITE · RPRT | If panel missing: check `StingTools.log` next to the DLL. |
| ☐ | Header context strip on panel reads a Standard (BS7671/NEC) + shows no exceptions | populated | |

## STEP 1 — Seed test model (so commands have data)

Have these in the test model before running the tabs. Missing items → those commands score **B** (blocked), not **F**.

| ✓ | Seed item | Feeds |
|---|---|---|
| ☐ | ≥2 **Electrical Equipment** panels (one MDB "root", one sub-DB "fed from" MDB), 400/230 V | PNLS, CALCS, SLD, Fault, ArcFlash, Feeder |
| ☐ | ≥6 **power circuits** assigned to panels (mix of 1-pole & 3-pole), with loads | CIRCTS, VD, Breakers, Load, SLD annots |
| ☐ | A few **conduits/cable trays** (name one "Busbar 400A"), some routed | CABLE, Busbar, Tray fill, Conduit fill |
| ☐ | ≥3 **Rooms** with area, some with **windows**; ≥4 **lighting fixtures** (1 emergency) with wattage | LITE, Photometrics |
| ☐ | Run **`Elec_CalcSeed`** once early to confirm the model exports cleanly | sanity check |
| ☐ | Load shared params (Main panel → LoadSharedParams) so `ELC_*` / `LTG_*` bind | everything that stamps params |

> Tip: run the **`WORKFLOW_ElectricalQA.json`** / **`WORKFLOW_ElectricalDesignReview.json`** presets to exercise a chain at once after individual buttons pass.

---

## TAB: PNLS — Panel schedules

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Panel_BatchSchedules` | [M] | panels exist → **Expect** a `PanelScheduleView` per panel, `ELC_PNL_*` filled, drawing-type stamped, result panel shows per-template counts | skips panels matching registry skip patterns |
| ☐ | `Panel_Audit` | [RO] | schedules exist → **Expect** report of panels w/o schedule, template drift, missing `ELC_PNL_*` | reloads template registry |
| ☐ | `Panel_FillSlots` | [M] | a panel schedule → **Expect** empty slots filled w/ spares or spaces; counts of real/occupied/filled | uses GetCircuitByCell to spot real circuits |
| ☐ | `Panel_AddSpare` | [M] | schedule w/ empty slot → **Expect** spare added to first empty slot | active circuits skipped |
| ☐ | `Panel_AddSpace` | [M] | as above → **Expect** space marker added | |
| ☐ | `Panel_ConvertSpaceToSpare` | [M] | schedule w/ spaces → **Expect** spaces converted to spares | |
| ☐ | `Panel_ClearSlots` | [M] | schedule w/ spares/spaces → **Expect** all spares+spaces removed | active circuits untouched |
| ☐ | `Panel_ExcelExport` | [RO] | schedules exist → **Expect** `.xlsx` (INDEX + one sheet/panel, header/body/summary) | round-trips with Excel import |
| ☐ | `Panel_SyncParams` | [M] | panels exist → **Expect** `ELC_PNL_NAME/FED_FROM/VOLTAGE/LOAD`, `ASS_LOC_TXT`, MFR synced from native BIPs | skips unbound panels |
| ☐ | `Panel_WriteParams` | [M] | a panel selected + PANEL PARAMETERS card filled → **Expect** `ELC_MAIN_BRK`, `ELC_PNL_FED_FROM`, `ELC_IP_RATING`, `ELC_PNL_FAULT_KA` written | reads dock-panel snapshot |
| ☐ | `Panel_EditTemplateRules` | [M/UI] | → **Expect** template-rule editor opens; save persists | data file: `STING_PANEL_SCHEDULE_TEMPLATES.json` |
| ☐ | `Panel_PlaceOnSheets` | [M] | panels + sheet → **Expect** per-panel schedule placed via `Viewport.Create` | `PanelScheduleSheetInstance.Create` is broken in Revit 2024+ — uses viewport workaround; totals not Revit-computed |

## TAB: CIRCTS — Circuit manager

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Circuit_AutoDesc` | [M] | power circuits → **Expect** Load Name / description auto-built from chosen source fields | uses dock DescriptionAutoFill options |
| ☐ | `Circuit_PreviewDesc` | [RO] | → **Expect** preview of proposed descriptions (no write) | |
| ☐ | `Circuit_ApplyDesc` | [M] | after preview → **Expect** descriptions committed | |
| ☐ | `Circuit_Balance` | [M] | 3-phase panels → **Expect** greedy phase rebalance; before/after imbalance in dock; skip/reassign counts | **Phase param often read-only** → many skips expected; drag in schedule if so |
| ☐ | `Circuit_Renumber` | [M] | active view = PanelScheduleView → **Expect** `RBS_ELEC_CIRCUIT_NUMBER` resequenced (odd/even by pole) | auto-managed slots skipped |
| ☐ | `Circuit_Excel` | [RO] | circuits → **Expect** circuit export `.xlsx` | |
| ☐ | `Circuit_Create` | [M] | ≥1 panel w/ schedule → **Expect** spare slot added to first empty slot of picked panel | needs pre-existing schedule |
| ☐ | `Circuit_Delete` | [M] | schedules exist → **Expect** all spare/space slots removed project-wide; per-schedule counts | active circuits skipped |
| ☐ | `Circuit_Move` | [M] | an ElectricalSystem selected → **Expect** circuit moved to picked destination panel | rejects voltage/phase/pole mismatch |
| ☐ | `Circuit_Sort` | [M] | circuits assigned → **Expect** sort by load / name / compact gaps; `RBS_ELEC_CIRCUIT_NUMBER` rewritten | read-only slots skipped |
| ☐ | `Circuit_WizardPropose` | [RO] | unassigned circuits + panels → **Expect** wizard proposal preview | |
| ☐ | `Circuit_CreateWizard` | [M] | after propose → **Expect** circuits created/assigned per wizard | |
| ☐ | `Elec_CircuitFilter` | [M] | circuits + active view → **Expect** parameter filter created, blue highlight on matching by panel/phase; `STING_CIRCUIT_FILTER_TXT` stamped on view | |
| ☐ | `Elec_CircuitTrace` | [M] | one device/panel selected + SLD hierarchy built → **Expect** orange-downstream / blue-upstream overrides; `ELC_CIRCUIT_TRACE_ACTIVE=1` | returns if hierarchy missing |
| ☐ | `Circuit_ClearTrace` | [M] | traced elements exist → **Expect** overrides cleared, flag reset to 0 | |
| ☐ | `Elec_HomeRunAnnotate` | [M] | conduit run selected → **Expect** home-run arrow toward source panel | |
| ☐ | `Circuit_ClearHomeRuns` | [M] | home-run annots exist → **Expect** them cleared | |

## TAB: CALCS — Calculation engines

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Standard / Note |
|---|---|---|---|---|
| ☐ | `Calc_LoadSummary` | [RO] | load grid populated → **Expect** refreshes summary, invalidates compliance cache | |
| ☐ | `Calc_VoltageDrop` | [M] | power circuits → **Expect** `ELC_VLT_DROP_PCT`/`ELC_CKT_VD_PCT` on every circuit; over-limit count | BS7671/NEC; √3 for 3-ph; temp-corrected R |
| ☐ | `Calc_FlagVD` | [M] | after VD calc → **Expect** red overrides on circuits > limit in active view | visual only, no model write |
| ☐ | `Calc_SizeBreakers` | [RO] | power circuits → **Expect** breaker proposal list (design A, min, proposed) | BS 60898/60947-2/NEC; 1.25× if continuous |
| ☐ | `Calc_ApplyBreakers` | [M] | after SizeBreakers → **Expect** `RBS_ELEC_CIRCUIT_RATING` written | skips read-only |
| ☐ | `Calc_UpsizeWires` | [M] | after VD calc → **Expect** min CSA found; `ELC_CKT_CSA_MM2`+`ELC_CKT_VD_PCT` written; confirm prompt | native wire-size may be read-only (STING params still written) |
| ☐ | `Calc_FeederSize` | [M] | SLD hierarchy + sub-panels → **Expect** `ELC_FEEDER_CSA`, `ELC_FEEDER_RATING_A`, `ELC_CKT_VD_PCT` per panel | BS7671; applies diversity+derate |
| ☐ | `Calc_FaultCurrent` | [M] | SLD hierarchy + utility kA set → **Expect** `ELC_PNL_FAULT_KA` per panel; `LastResults` cached | IEC 60909 resistive method; feeder length falls back to 5 m if unrecorded |
| ☐ | `Calc_AicStamp` | [M] | after FaultCurrent → **Expect** `ELC_PNL_AIC_KA` per panel (next std tier) | falls back to fault value if no tier file |
| ☐ | `Calc_LoadDemandAudit` | [RO] | circuits + panels → **Expect** 5-sheet Excel (spare RAG, diversity matrix, PFC sizing) | data: `STING_DIVERSITY_FACTORS.json` |
| ☐ | `Elec_ArcFlash` | [M] | **FaultCurrent run first** → **Expect** `ELC_ARC_FLASH_IE/BD/PPE/WD/LABEL` + PPE colour overrides | IEEE 1584-2018; enclosure hardcoded VCB |
| ☐ | `Elec_ArcFlashLabels` | [M] | after ArcFlash → **Expect** label sheet created | |
| ☐ | `Elec_ArcFlashSched` | [M] | after ArcFlash → **Expect** arc-flash schedule view | |
| ☐ | `Elec_ArcFlashBoundary` | [M] | after ArcFlash → **Expect** boundary view/overlay | |
| ☐ | `Elec_SelectCoord` | [M] | SLD hierarchy + TCC db → **Expect** violations list; `ELC_SEL_COORD_OK` (1/0) on panels; dialog | IEEE 1584/BS7671; log-log interp; ZSI flag |
| ☐ | `Elec_TccPlot` | [RO] | TCC db → **Expect** SVG per breaker pair in `…/electrical/tcc/`; Explorer opens | data: `STING_TCC_DATABASE.json`; explorer launch is Windows-only |

## TAB: CABLE — Cable / conduit / busbar

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Standard / Note |
|---|---|---|---|---|
| ☐ | `Cable_Calculate` | [RO] | CABLE inputs filled (load, V, length, method, material, insulation, VD limit) → **Expect** RESULT: design A, CSA, actual VD%, proposed breaker | data: `STING_WIRE_TABLES.json` (falls back to hardcoded factors) |
| ☐ | `Cable_ApplyToCircuit` | [M] | after Calculate + active panel → **Expect** sized cable written back to circuit | |
| ☐ | `Cable_ConduitFill` | [M] | cable manifest + conduits → **Expect** `ELC_CONDUIT_FILL_PCT`; red overrides > limit; optional auto-upsize | BS7671 45% / NEC 40%; delegates to TrayFillCalculator |
| ☐ | `Cable_ValidateConduitFill` | [M] | as above → **Expect** validation + interactive auto-size prompt | persistent-fail flag if max size reached |
| ☐ | `Elec_AutoRoute` | [M] | manifest w/ unrouted cables + conduit type → **Expect** conduit created; `ELC_CONDUIT_ROUTE="AUTO:<id>"`, bend count | BS7671 §522.8.5 (>3 bends warns, doesn't block); no clash avoidance |
| ☐ | `Elec_BusbarModel` | [M] | tray named "Busbar…"/"Trunking" → **Expect** `ELC_BUSBAR_CSA/RATING/FILL_PCT`; red override if fill>80% | IEC 61439; static table max 2000 mm²/2000 A |

> Cable schedule / pull list live under **RPRT** (`Rprt_CablePullList`) and the routing `CableScheduleBuilderCommand`.

## TAB: SLD — Single line diagram

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `SLD_Generate` | [M] | ≥1 electrical equipment root → **Expect** new drafting view "STING - SLD - …" w/ symbols, busbars, branches; voltage-tier + feed-type stamps | multi-root + emergency/bus-coupler notation |
| ☐ | `SLD_GenerateOptions` | [M] | root exists → **Expect** standard picker (IEC/IEEE/BS/NFPA/CIBSE) then generate | |
| ☐ | `SLD_Update` / `SLD_Refresh` | [M] | ≥1 "STING - SLD*" view → **Expect** all SLD views rebuilt in place | |
| ☐ | `SLD_Validate` | [RO] | → **Expect** dialog: root count, node count, symbol count | audit only |
| ☐ | `SLD_SyncToggle` | [M] | project saved → **Expect** `sld_sync_enabled` flips in `project_config.json` | needs saved .rvt |
| ☐ | `SLD_MigrateLabels` | [M] | old SLD views w/ TextNotes → **Expect** `STING_SYMBOL_LABEL_ID` stamped by nearest note | idempotent |
| ☐ | `SLD_SwitchStandard` | [M] | SLD views exist → **Expect** standard picker; views rebuilt w/ new symbol families | per-view rebuild may error individually |
| ☐ | `SLD_Export` | [RO] | SLD views exist → **Expect** PDF/DWG/CSV in `…/SLD_Export/`; Explorer opens | native PDF (no print driver) |
| ☐ | `SLD_ZoomTo` | [RO] | schematic view active + element → **Expect** zoom to element bbox | |
| ☐ | `SLD_OpenSchedule` | [RO] | → **Expect** associated schedule opens | partial/context-dependent |
| ☐ | `SLD_RiserDiagram` | [M] | root exists → **Expect** new "STING - Riser Diagram - …" (boxes+feeders) | |
| ☐ | `SLD_UpdateRiser` | [M] | riser view exists → **Expect** redrawn in place | errors if none found |

### SLD annotations (active view must be schematic; elements need `ELC_CIR_*` params)

| ✓ | Button / tag | Mode | Expect | Note |
|---|---|---|---|---|
| ☐ | `SldAnnotate_All` | [M] | text note per element w/ all available fields; stamps annot kind/id/format | warns if no elements carry params |
| ☐ | `SldAnnotate_Voltage` | [M] | voltage labels | `ELC_CIR_VOLTAGE_TXT` |
| ☐ | `SldAnnotate_Current` | [M] | Ib labels | `ELC_CIR_DESIGN_CURRENT_TXT` |
| ☐ | `SldAnnotate_Fault` | [M] | fault-kA labels | `ELC_CIR_FAULT_LEVEL_TXT` |
| ☐ | `SldAnnotate_Cable` | [M] | cable/CSA (+A/breaker) labels | `ELC_CABLE_CSA_TXT` |
| ☐ | `SldAnnotate_Phase` | [M] | phase labels (3Ph/1Ph inferred from V) | |
| ☐ | `SldAnnotate_Load` | [M] | kW/kVA labels | `ELC_CIR_DESIGN_LOAD_TXT` |
| ☐ | `SldAnnotate_Reference` | [M] | panel—circuit ref labels | |
| ☐ | `SldAnnotate_Impedance` | [M] | Zs labels | `ELC_CIR_ZS_TXT` |
| ☐ | `SldAnnotate_Diversity` | [M] | diversity-factor labels | |
| ☐ | `SldAnnotate_Format` (+ _Compact/_Full/_Reference) | [RO] | sets session format; existing notes unchanged | run UpdateCalcs to re-render |
| ☐ | `SldAnnotate_UpdateCalcs` | [M] | existing notes refreshed from live params | |
| ☐ | `SldAnnotate_Toggle` | [M] | show/hide (halftone) existing notes | |
| ☐ | `SldAnnotate_Clear` | [M] | deletes STING SLD notes in view (confirm) | destructive |
| ☐ | `SldAnnotate_Audit` | [RO] | report: annotatable vs annotated vs missing | |

## TAB: LITE — Lighting & photometrics

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Standard / Note |
|---|---|---|---|---|
| ☐ | `Lite_Refresh` | [RO] | → refresh lighting grid | |
| ☐ | `Lite_CreateSchedule` | [M] | fixtures → lighting fixture schedule created | |
| ☐ | `Lite_UpdateTargets` | [M] | → lux/LPD targets updated | |
| ☐ | `Lite_LPD` | [M] | rooms + fixtures w/ wattage → `ELC_LPD_*` on rooms + green/amber/red overrides | ASHRAE 90.1 / Part L; data: `STING_LPD_LIMITS.json` |
| ☐ | `Lite_LpdColor` | [M] | rooms w/ `ELC_LPD_STATUS_TXT` → re-applies overrides (no recalc) | |
| ☐ | `Lite_QuickLuxEstimate` | [RO] | rooms + fixtures w/ lumens → `STING_QuickLux_*.xlsx` (lux vs target/room) | BS EN 12464-1; coarse (MF fixed) |
| ☐ | `Lite_LightingCalcSheet` | [RO] | rooms+fixtures → per-room A4 lumen-method sheets `.xlsx` w/ sign-off | |
| ☐ | `Lite_ControlZones` | [M] | rooms+fixtures (+windows) → `STING_LightingControlZones_*.xlsx`; `LTG_CTRL_TYPE_TXT` stamped | Part L §6.3 / ASHRAE 90.1 |
| ☐ | `Lite_LuminaireRegistry` | [M] | ≥1 fixture → mode1 scaffolds `_BIM_COORD/luminaire_registry.csv`; mode2 stamps type params from CSV | populate CSV in Excel between modes |
| ☐ | `Lite_EmergAudit` | [M] | rooms+fixtures → `ELC_EMERG_COVERED`; amber(same-circuit)/red(none) overrides | pattern-matches emergency fixtures |
| ☐ | `Lite_MarkEmerg` | [M] | fixtures in view → blue overrides on emergency fixtures | visual only |
| ☐ | `Lite_ComCheck` / `ComCheck_Export` | [RO] | rooms+fixtures → `STING_ComCheck_Lighting_*.csv` (PASS/FAIL vs LPD) | data: `STING_COMCHECK_SPACE_MAP.csv`; paste into COMcheck manually |
| ☐ | `Photo_Library` | [M/UI] | ≥1 .ies/.ldt in root dir → library dialog; first run asks for root | IES LM-63 / EULUMDAT |
| ☐ | `Photo_Assign` | [M] | ≥1 fixture type selected → stamps `ELC_PHOTO_*` on type (lumens/watts/efficacy/CCT/CRI/beam) | auto-binds from registry IES path if present |
| ☐ | `Photo_Preflight` | [RO] | rooms+fixtures → dialog: fixtures w/o IES, outside rooms, rooms w/o reflectance | advisory, non-blocking |
| ☐ | `Photo_IfcImport` | [M] | DIALux/ElumTools/Relux IFC → engine-specific lux + UGR + uniformity on rooms; last-engine/date | regex IFC parser; GUID match then name |
| ☐ | `Photo_Aggregator` | [RO] | rooms w/ results → `STING_PhotometricAggregator_*.xlsx` (all engines side-by-side, delta flags) | |
| ☐ | `Photo_RoundTrip` | [M/UI] | → step-through: preflight→export IFC→DIALux→import→review; opens export folder | orchestrator |
| ☐ | `Photo_DesignReview` | [M] | rooms w/ imported lux → `STING_PhotometricDesignReview_*.xlsx`; red/amber/green overrides | stale flag if >14 days |
| ☐ | `Elec_PhotoLink` | [M] | rooms → 3 modes: import IFC / estimate-from-watts / export guide; writes lux+UGR | CIBSE LG7; UGR binning is simplistic |

## TAB: RPRT — Reports, compliance, validation, export/import

### Reports
| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Rprt_Audit` | [RO] | electrical systems → findings (run length, bends, fill%) w/ codes | ElectricalStandardsValidator |
| ☐ | `Rprt_PDF` | [RO] | PanelScheduleViews → PDFs in `…/ElecPDF/` | native PDF export |
| ☐ | `Rprt_ExcelExport` | [RO] | circuits → demand-factor `.xlsx` per panel | data: `STING_DEMAND_FACTORS.json` |
| ☐ | `Rprt_ExcelImport` / `Rprt_ShowDiff` | [RO] | prior import → shows cached diff (50 lines) | needs a prior Excel import |
| ☐ | `Rprt_CircuitExport` | [RO] | circuits → CSV/XML/JSON in `_BIM_COORD/electrical/` | for Amtech/Trimble/EasyPower |
| ☐ | `Rprt_VDSchedule` | [M] | circuits → "STING - Voltage Drop Schedule" view; `ELC_CKT_VD_PCT` written | runs VD calc first |
| ☐ | `Rprt_FaultSchedule` | [M] | **FaultCurrent run first** → "STING - Fault Level Schedule" view | |
| ☐ | `Rprt_DemandFactors` | [RO] | circuits → demand-factor report | |
| ☐ | `Rprt_CablePullList` | [RO] | circuits → `STING_CablePullList_*.xlsx` (drum allocation FFD) | data: `STING_CABLE_DRUMS.json` |
| ☐ | `Rprt_EquipSchedule` | [RO] | equipment → `STING_EquipmentSchedule_*.xlsx` | |
| ☐ | `Rprt_COBie` / `Rprt_COBieStandalone` | [RO] | equipment → COBie electrical export | |
| ☐ | `Elec_DrawingLegend` | [M] | → electrical drawing legend created | |
| ☐ | `Elec_CarbonRollup` | [RO] | circuits+cables+panels+fixtures → `STING_ElectricalCarbon_*.xlsx` (operational+embodied+interventions) | BS EN 15978; data: `STING_CARBON_FACTORS.json` |

### Compliance (BS 7671)
| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Bs7671_Audit` | [RO] | circuits → `STING_BS7671_Compliance_*.xlsx` (RAG: Zs, adiabatic, RCD); caches results | data: `STING_BS7671_DISCONNECTION.json` |
| ☐ | `Bs7671_LoopCalcSheet` | [RO] | **Audit run first** → per-circuit A4 loop-calc workbook | depends on cached audit |
| ☐ | `Bs7671_Certificate` | [RO] | **Audit run first** → App-6 Initial Verification cert template (pre-filled) | tester fills measured values |
| ☐ | `Bs7671_WorkingClearance` | [RO] | panels/switchgear → `STING_WorkingClearance_*.xlsx` (intrusion verdicts) | defaults 1000/750/2000 mm |

### Validation
| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Elec_IPSValidate` | [M] | IPS panels → `IPS_BRANCH_COMPLIANT` (1/0) on circuits; dialog | NFPA 99; warns if `LIM_INSTALLED_BOOL` unset |
| ☐ | `Elec_ATEXCheck` | [RO] | elements w/ `ATEX_ZONE_TXT` → `ATEX_CLASSIFICATION_OK` (1/0) | IEC 60079-14 |
| ☐ | `Elec_DualSourceValidate` | [M] | generators/UPS → `ELC_TRANSFER_LOAD_OK` (1/0); dialog | gen 80% / UPS 100% loading |

### External export / import (produce intermediary files — actual load into target app is manual)
| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Elec_ExportEasyPower` | [RO] | circuits+panels → `STING_EasyPower_*.xml` | |
| ☐ | `Elec_ExportDIALux` | [RO] | fixtures+rooms → `STING_DIALux_*.ifc` (Psets, GUID-preserved) | round-trip via `Photo_IfcImport` |
| ☐ | `Elec_ExportEtap` | [RO] | circuits+panels → `STING_ETAP_CIM_*.xml` (CIM) | IEC 61968/61970 |
| ☐ | `Elec_AmtechImport` | [M] | pick Amtech `.aml/.xml` → stamps panels/circuits; dialog reports matched/unmatched | name-match |
| ☐ | `Elec_EasyPowerImport` | [M] | pick EasyPower `.xml/.epx` → stamps fault-kA/voltage | |
| ☐ | `Elec_TrimbleImport` | [M] | pick Trimble CSV/XML → stamps VD/CSA/rating on circuits | |
| ☐ | `Elec_CalcSeed` | [RO] | panels+circuits → seed CSV/JSON in `…/ElecCalcSeed/` | pre-fills external calc tools |

## SCHEMATICS (drafting-view generators)

| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `FireAlarm_Schematic` | [M] | fire-alarm devices w/ `ELC_FIRE_LOOP_REF` → "STING - Fire Alarm Schematic" view (loops+devices) | defaults "Zone 1" if blank |
| ☐ | `Earthing_Diagram` | [M] | ProjectInfo `ELC_EARTHING_SYSTEM_TXT` → "STING - Earthing Arrangement" (variant per TN-S/TN-C-S/IT) | no model devices needed |
| ☐ | `Panel_DoorDiagram` | [M] | panels → "STING - Panel Layout - <name>" (breaker columns, spares, earth bar) | slot count from `RBS_ELEC_NUMBER_OF_CIRCUITS` |
| ☐ | `LPS_Schematic` | [M] | elements w/ `LPS_COMPONENT_TYPE_TXT` → "STING - LPS Schematic" (air terminals/down conductors/electrodes + LPL badge) | placeholder drawn if none |
| ☐ | `MGPS_Schematic` | [M] | elements w/ `MGS_GAS_TYPE_TXT` → "STING - MGPS Schematic" (gas risers, zone valves, TUs, legend) | supports custom gas types |

## PLACEMENT

| ✓ | Button / tag | Mode | Pre → **Expect** | Note |
|---|---|---|---|---|
| ☐ | `Placement_PVArray` | [M] | roofs + PV family loaded → grid of PV panels over roofs | skips (reports) if no PV family; 400 Wp default |
| ☐ | `Placement_EVCharger` | [M] | parking spaces + EV family → one charger/space at wall; `EV_CHARGER_TYPE/KW` | dedup pre-stamped |
| ☐ | `Placement_MedGasOutlets` | [M] | rooms w/ `MGS_GAS_REQUIREMENT_TXT` + outlet families → outlet/gas/room at 1350 mm | BS HTM 02-01; pressure per gas |

---

## Cross-cutting checks (do once)

| ✓ | Check | Expect |
|---|---|---|
| ☐ | Every `[M]` command is undoable via Ctrl+Z | single undo reverts |
| ☐ | Run a command with an **empty** model | graceful "nothing to do" dialog, no crash |
| ☐ | Data-file edit + reload (e.g. edit `STING_WIRE_TABLES.json`, rerun `Cable_Calculate`) | new values take effect |
| ☐ | `StingTools.log` after a full pass | no unhandled exceptions / silent catches |
| ☐ | Output files land in `OutputLocationHelper` dir (`…/electrical/`, `_BIM_COORD/`) | present + openable |

## Known caveats — do NOT score these as failures
- **Phase params read-only** — `Circuit_Balance` / `Circuit_Sort` skips are expected; Revit manages phase from slot order.
- **`PanelScheduleSheetInstance.Create` broken (Revit 2024+)** — panel schedules on sheets use a `Viewport.Create` workaround; totals aren't Revit-computed.
- **Wire element annotations are compute-on-place** — they don't persist to the wire (Phase-2 refresh deferred).
- **Arc-flash enclosure hardcoded to VCB**; **cable-schedule weight = 0** (no density table); **feeder length falls back to 5 m** in fault calc when unrecorded.
- **External exports** (EasyPower/ETAP/DIALux, Amtech/Trimble imports) produce/consume intermediary files — the actual round-trip into the third-party app is manual.
- **Photometric estimates are coarse** (fixed MF/reflectance, simplistic UGR bins) — flagged in-tool as "use DIALux for compliance."
- **Commands needing a shared param that isn't loaded** silently fall back or no-op — that's **Blocked (B)**, not Fail: load shared params first (Step 1).
