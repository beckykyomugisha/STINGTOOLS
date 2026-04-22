# STING Tools — Design & Modeling Automation Roadmap

_Last refresh: Phase 110._

This roadmap captures automation opportunities across the design and
modeling workflow that are **enabled but not yet built** now that the
`StingTools.Standards` calculation library is wired in.

Every item is scoped so a single focused runner can deliver it, with
the target IExternalCommand class name, the Standards / Engine API
to consume, the dispatch tag, and the suggested Dynamo node path.

---

## How this roadmap is organised

Each table row has five columns:

| Field | Meaning |
|---|---|
| ID | Stable runner ID used in commit messages + issue tracking |
| Workflow | One-line description of the coordinator action |
| Backed by | Existing StingTools engine or StingTools.Standards API call |
| Target dispatch tag | What the dock-panel button will emit |
| Suggested Dynamo node | Category + method name for the Dynamo facade |

Priority column: **P0** = blocked handover today; **P1** = measurable
daily time saving for BIM coordinators; **P2** = valuable polish.

---

## 1. Architecture & shell automation

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| ARCH-01 | Auto-stair (picked points → EC8 + Part K code-compliant stair + landings) | `ArchitecturalCreationEngine.StairEngine` | `Arch_AutoStair` | `Model.Shell.AutoStair` | P1 |
| ARCH-02 | Auto-railing from floor edge (BS 6180 + Part K height + infill) | `ArchitecturalCreationEngine.RailingEngine` | `Arch_AutoRailing` | `Model.Shell.AutoRailing` | P1 |
| ARCH-03 | Curtain wall from rectangle (BS EN 13830 + grid / mullion spacing) | `ArchitecturalCreationEngine.CurtainWallEngine` | `Arch_AutoCurtainWall` | `Model.Shell.AutoCurtainWall` | P1 |
| ARCH-04 | Opening auto-cut (wall + opening rect → composite cut solid) | `ArchitecturalCreationEngine.OpeningEngine` | `Arch_AutoOpening` | `Model.Shell.AutoOpening` | P2 |
| ARCH-05 | Plaster + paint system from covering material (BS EN 13914) | `PlasteringEngine` | `Arch_AutoPlaster` | `Model.Shell.AutoPlaster` | P2 |
| ARCH-06 | Cover fire rating + moisture risk + thermal bridge audit | `ArchitecturalCreationEngine.CoveringFireRating` | `Arch_CoverAudit` | `Model.Shell.CoverAudit` | P1 |

---

## 2. Structural automation (gaps beyond Phase 108)

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| STR-01 | Slab rebar auto-detail from moment/shear envelope | `StructuralDesignSuite` + `RebarEngine` | `Str_AutoSlabRebar` | `Structural.Design.AutoSlabRebar` | P0 |
| STR-02 | Column load takedown across whole model (not just selected) | `StructuralPrecisionEngine.ColumnLoadTakedown` | `Str_FullColumnTakedown` | `Structural.Analysis.FullTakedown` | P1 |
| STR-03 | Wind load auto-applied to all faces (ASCE 7 / EC) | `StandardsAPI.CalculateWindLoad` + `Structural.WindLoad` | `Str_WindAutoApply` | `Structural.Analysis.WindApply` | P0 |
| STR-04 | Seismic shear distribution to lateral system (EC 8 response spectrum) | `StructuralAnalysisEngine.Seismic` | `Str_SeismicAutoApply` | `Structural.Analysis.SeismicApply` | P0 |
| STR-05 | Pile group design (caps + layout + capacity, EC7 + BS 8004) | `StructuralDesignSuite.PileDesign` | `Str_PileGroupDesign` | `Structural.Design.PileGroup` | P1 |
| STR-06 | Retaining wall stability + bearing + sliding + overturning (EC7) | `StructuralAdvancedDesignExt.RetainingWall` | `Str_RetainingWallCheck` | `Structural.Design.RetainingWallCheck` | P1 |
| STR-07 | Steel connection auto-design from member end forces (SCI P358) | `StructuralDeepEngine.ConnectionDetailingEngine` | `Str_AutoConnection` | `Structural.Design.AutoConnection` | P0 |
| STR-08 | Composite beam design (BS 5950-3 / EC4) with shear stud count | `StructuralAdvancedDesign.CompositeBeam` | `Str_CompositeBeamDesign` | `Structural.Design.CompositeBeam` | P1 |
| STR-09 | Fabrication tolerance check (BS EN 1090-2) | `StructuralDeepEngine.FabricationToleranceChecker` | `Str_TolerenceCheck` | `Structural.Design.FabTolerances` | P2 |
| STR-10 | Creep + shrinkage deflection (EC2 Annex B) with pre-camber | `StructuralDeepEngine.CreepDeflectionAnalysis` | `Str_CreepDeflection` | `Structural.Analysis.Creep` | P1 |

---

## 3. MEP design automation (gaps beyond Phase 109)

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| MEP-A-01 | Cable sizing auto-apply to every circuit (BS 7671 / IEC 60364) | `StandardsAPI.CalculateCableSize` | `Mep_CableSizeApply` | `Mep.AutoSize.Cable` | P0 |
| MEP-A-02 | Panel schedule auto-build + demand factor | `StandardsAPI` PanelScheduleResult + existing `PanelSchedule` | `Mep_PanelScheduleBuild` | `Mep.Electrical.PanelSchedule` | P1 |
| MEP-A-03 | Circuit breaker auto-size per load (IEC 60947-2 / NEC 240) | `StandardsAPI.VerifyCircuitBreaker` | `Mep_BreakerAutoSize` | `Mep.Electrical.BreakerSize` | P1 |
| MEP-A-04 | Conduit fill auto-size from ELC_CBL_TOTAL_AREA_MM2 already in Phase 109; extend to full project in one click | extends `MepAutoSizeConduitCommand` to project scope | `Mep_AutoSizeConduitAll` | `Mep.AutoSize.AllConduits` | P1 |
| MEP-A-05 | Grounding size + ground ring layout (BS 7671 542, IEEE 142) | `StandardsAPI.CalculateGroundingSize` | `Mep_GroundingDesign` | `Mep.Electrical.Grounding` | P1 |
| MEP-A-06 | Duct auto-size per CIBSE Guide B3 + aspect ratio (Phase 109 covers; extend with static regain method) | `StandardsAPI` duct sizing + static regain | `Mep_DuctSizeStaticRegain` | `Mep.AutoSize.DuctStaticRegain` | P1 |
| MEP-A-07 | Pump sizing per system TDH (CIBSE Guide C) | `AECCalculations.CalculatePumpSize` | `Mep_PumpSize` | `Mep.HVAC.PumpSize` | P1 |
| MEP-A-08 | Transformer sizing from load factor (IEC 60076) | `AECCalculations.CalculateTransformerSize` | `Mep_TransformerSize` | `Mep.Electrical.TransformerSize` | P1 |
| MEP-A-09 | Generator sizing per block / non-block load | `AECCalculations.CalculateGeneratorSize` | `Mep_GeneratorSize` | `Mep.Electrical.GeneratorSize` | P1 |
| MEP-A-10 | Water heater sizing per fixture load (BS 6700 + CIBSE G) | `StandardsAPI.CalculateWaterHeaterSize` | `Mep_WaterHeaterSize` | `Mep.Plumbing.WaterHeaterSize` | P2 |
| MEP-A-11 | Drainage pipe sizing per flow (BS EN 12056) | `StandardsAPI.CalculateDrainageSize` | `Mep_DrainageSize` | `Mep.Plumbing.Drainage` | P1 |
| MEP-A-12 | MEP balancing auto-tune from Hardy-Cross convergence to Revit dampers | extends `MEPBalancingEngine` to write `RBS_DUCT_DESIGN_FLOW` | `Mep_BalanceApply` | `Mep.Intelligence.BalanceApply` | P0 |

---

## 4. Placement & fixture automation (gaps beyond Phase 2)

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| PLC-01 | Sprinkler grid auto-layout per hazard class (NFPA 13 / EN 12845) | `StandardsAPI.DesignSprinklerSystem` + `FixturePlacementEngine` | `Place_SprinklerGrid` | `Placement.SprinklerGrid` | P0 |
| PLC-02 | Accessible WC layout (BS 8300 + Part M clearances) | `AECCalculations.CalculateAccessibleToilet` + `FixturePlacementEngine` | `Place_AccessibleWC` | `Placement.AccessibleWC` | P1 |
| PLC-03 | Fire extinguisher placement (NFPA 10 travel distance) | `NFPAStandards.FireExtinguisher` + `PlacementRule` | `Place_FireExtinguisher` | `Placement.FireExtinguisher` | P1 |
| PLC-04 | Exit sign placement (BS 5266-1 + NFPA 101 escape route) | `NFPAStandards.LifeSafety` + `FixturePlacementEngine` | `Place_ExitSigns` | `Placement.ExitSigns` | P1 |
| PLC-05 | Emergency luminaire spacing per BS 5266-1 (Phase 2 covers; extend to full model in one click) | extends `LightingGridCalculator` | `Place_EmergencyLumAll` | `Placement.EmergencyLumAll` | P2 |
| PLC-06 | Access control reader at restricted doors (from DOOR_SEC_LEVEL_INT parameter) | `PlacementRule` + door parameter scan | `Place_AccessControl` | `Placement.AccessControl` | P2 |
| PLC-07 | CCTV camera placement (line-of-sight over room polygons, 2.8m ceiling) | `PlacementRule` + room geometry walk | `Place_CCTVCoverage` | `Placement.CCTVCoverage` | P1 |

---

## 5. Routing automation (gaps beyond Phase 3)

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| RT-01 | Manhattan branch layout — generate main trunk + branches from fixture set | currently stub (`GenerateLayoutCommand`) | `Routing_GenerateLayout` already wired; build body | `Routing.GenerateLayout` | P0 |
| RT-02 | MEP collision avoidance along routes (check vs structure, shift Z in 25mm steps) | `AabbSweep` + `Routing` | `Routing_ClashAvoid` | `Routing.ClashAvoid` | P1 |
| RT-03 | Cable bundle grouping (fill + voltage segregation per BS 7671 528) | `FillValidator` + new grouping engine | `Routing_CableBundle` | `Routing.CableBundle` | P1 |
| RT-04 | Pipe insulation auto-apply per temperature + Part L TIMSA guide | `StandardsAPI` insulation lookup + `AutoPipeDrop` | `Routing_PipeInsulation` | `Routing.PipeInsulation` | P1 |
| RT-05 | Fire damper auto-insert at fire compartment wall crossings (Part B) | `AutoSleevePlacementCommand` pattern + compartment wall scan | `Routing_AutoFireDamper` | `Routing.AutoFireDamper` | P0 |
| RT-06 | Expansion loop / bellows placement on long straight runs (ASME B31.1 / BS EN 13480) | `StandardsAPI` thermal expansion + `AutoPipeDrop` | `Routing_ExpansionLoop` | `Routing.ExpansionLoop` | P2 |
| RT-07 | Cable tray vertical riser auto-insert at floor crossings | `AutoSleevePlacementCommand` inverted for cable tray | `Routing_TrayRiser` | `Routing.TrayRiser` | P2 |

---

## 6. Fabrication automation (gaps beyond Phase 5)

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| FAB-01 | BIM 360 / ACC auto-publish of generated shop drawing sheets | `PlatformLinkEngine.ACCPublish` + `FabricationResult.SheetIds` | `Fab_ACCPublishShop` | `Fab.ACCPublish` | P1 |
| FAB-02 | Robotic weld-path export (ISO 9606 procedure + coords) | `PipeFabricator` weld map extension | `Fab_WeldPathExport` | `Fab.WeldPathExport` | P2 |
| FAB-03 | CNC-ready NC code export (ISO 6983 G-code from cut-list geometry) | New `CncExportEngine` | `Fab_ExportNC` | `Fab.ExportNC` | P2 |
| FAB-04 | Duct seam + reinforcement audit per SMACNA gauge tables | `StandardsAPI` SMACNA tables + `DuctFabricator` | `Fab_DuctSeamAudit` | `Fab.DuctSeamAudit` | P1 |
| FAB-05 | Pipe support auto-spacing per CIBSE Guide C + ASME B31.1 | `StandardsAPI` pipe support tables + `PipeFabricator` | `Fab_PipeSupportGrid` | `Fab.PipeSupports` | P0 |
| FAB-06 | Hanger load takedown per spool + trapeze auto-allocation | `AssyParams.SUPPORT_COUNT_NR` writer | `Fab_HangerTakedown` | `Fab.HangerTakedown` | P1 |
| FAB-07 | Weld neck flange rating + pressure class audit (ASME B16.5) | `StandardsAPI` flange lookup | `Fab_FlangeRating` | `Fab.FlangeRating` | P2 |
| FAB-08 | Spool weight + CoG computation for crane-lift planning | `AssyParams.WEIGHT_KG` writer + geometric CoG | `Fab_SpoolWeightCoG` | `Fab.SpoolWeight` | P1 |
| FAB-09 | Title block populate from ASS_SPOOL_NR + weld count + pressure test | `ShopDrawingComposer.ApplySheetMetadata` already 90% there; extend | `Fab_TitleBlockFill` | `Fab.TitleBlockFill` | P2 |
| FAB-10 | ISO 6412 symbol replacement (currently stubs; ship + lazy-load real families) | `IsoSymbolPlacer` + `Families/ISO6412/*.rfa` authoring | `Fab_ISOSymbolsFull` | `Fab.ISOSymbols` | P0 |

---

## 7. Standards-driven compliance workflows

| ID | Workflow | Backed by | Dispatch | Dynamo | P |
|---|---|---|---|---|---|
| STD-01 | Stage-gated compliance audit (RIBA 0→7, Part L → Part Q) | `StandardsComplianceEngine` + `StageComplianceGateCommand` | `Std_StageCompliance` | `Standards.StageCompliance` | P0 |
| STD-02 | Regional code overlay — swap KEBS ↔ UNBS ↔ TBS ↔ SANS depending on project locale | `ProjectStandardsManager.SetRegion` | `Std_SetRegion` | `Standards.SetRegion` | P1 |
| STD-03 | Accessibility audit across every room (BS 8300 + Part M + ADA) | `AECCalculations.CalculateAccessibleRoute` | `Std_AccessibilityAudit` | `Standards.AccessibilityAudit` | P0 |
| STD-04 | Parking requirement compliance (floor area × occupancy factor) | `AECCalculations.CalculateParkingRequirements` | `Std_ParkingAudit` | `Standards.ParkingAudit` | P1 |
| STD-05 | Floor live load audit per category (EC 1991-1-1 / BS 6399-1 / ASCE 7) | `AECCalculations.CalculateFloorLiveLoad` | `Std_LiveLoadAudit` | `Standards.LiveLoadAudit` | P1 |
| STD-06 | Load combination generator (EC 0 / ASCE 7 / BS) | `AECCalculations.CalculateLoadCombinations` | `Std_LoadCombinations` | `Standards.LoadCombinations` | P1 |
| STD-07 | Energy use intensity EUI benchmark (Part L + ASHRAE 90.1) | `AECCalculations.CalculateEUI` | `Std_EUIBenchmark` | `Standards.EUIBenchmark` | P1 |
| STD-08 | Water use reduction + LEED credit calc | `AECCalculations.CalculateWaterUseReduction` | `Std_WaterUseReduction` | `Standards.WaterUseReduction` | P2 |
| STD-09 | Space efficiency audit (BCO / IFMA) | `AECCalculations.CalculateSpaceEfficiency` | `Std_SpaceEfficiency` | `Standards.SpaceEfficiency` | P2 |
| STD-10 | Equipment lifecycle cost (CIBSE TM56 + ISO 15686) | `AECCalculations.CalculateEquipmentLifecycle` | `Std_LifecycleCost` | `Standards.LifecycleCost` | P2 |

---

## 8. Regional African codes (the new lever)

The StingBIM.Standards import ships 8 regional code modules that
have NO counterpart in the pre-Phase 110 plugin. They unlock
market-specific automation for East + Southern + West Africa:

| Region | Module | Distinctive workflow unlock |
|---|---|---|
| Kenya | `KEBSStandards` | KEBS KS 372 (concrete), KS EAS 121 (structural steel), KS 02-46 (electrical) — cross-border spec pack for Kenya-based contracts |
| Uganda | `UNBSStandards` | US EAS 121, US 101 (building code), US 52 (fire safety) — local-standard compliance dashboard |
| Tanzania | `TBSStandards` | TZS 532 (concrete), TZS 1250 (structural steel), TZS 1320 (fire safety) — Tanzania certification package |
| Rwanda | `RSBStandards` | RS 160 (structural design), RS ISO 6708 (plumbing), RS 297 (fire) — Kigali procurement ready |
| South Sudan | `SSBSStandards` | SSBS structural + fire codes — underserved market, pre-filled templates give consultant advantage |
| Burundi | `BBNStandards` | BBN 04-10 (structural), BBN 03-20 (fire) — Francophone + English label support |
| East African | `EASStandards` | EAS 121 (steel), EAS 353 (concrete), EAS 124 (fire), EAS 117 (masonry) — 6-nation common market overlay |
| West African | `ECOWASStandards` | CEDEAO technical regulations — Lagos / Abidjan / Dakar market reach |
| Southern African | `SANSStandards` | SANS 10400 (national building regulations) + SANS 204 (energy) — RSA market |

**Automation opportunity**: one-click "target market" preset that
swaps the active code set per project, filters every validator to
the right reference standard, and re-runs the compliance engine.
Runner ID: **REG-01** → `Std_SetTargetMarket`.

---

## 9. Standards API wrapper opportunities not yet surfaced

The `StandardsAPI` + `AECCalculations` libraries expose 30+ static
methods; Phase 110 wraps 6. The remaining 24+ are all candidate
dispatch wrappers with no extra engine code:

- `CalculateVentilation` (ASHRAE 62.1 rates per occupancy class)
- `CalculatePlumbingPipeSize` (Hunter / BS 6700 fixture units)
- `CalculateDuctSize` (static-regain / equal-friction)
- `CalculatePsychrometric` (moist-air properties)
- `CalculateFaultCurrent` + `CalculateArcFlash` (IEEE 1584)
- `CalculateConduitSize` (NEC Chapter 9 Table 1)
- `CalculateSteelBeam` (AISC 360 / EC3 LRFD)
- `CalculateConcreteBeam` (ACI 318 / EC2)
- `CalculateFoundation` (ACI 318-14 / EC7 strip + pad + mat)
- `CalculateWindLoad` (already wrapped)
- `CalculateSeismicLoad` (ASCE 7 Chapter 12)
- `CalculateOccupantLoad` (already wrapped)
- `CalculateTravelDistance` (already wrapped)
- `CalculateEgressWidth` (already wrapped)
- `CalculateSpaceUtilization` (IFMA space standards)
- `DesignHydrant` (NFPA 24)
- `DesignSprinklerSystem` (already wrapped)
- `CalculateMaintenanceCosts` (BSRIA maintenance curves)
- `CalculateAccessibleToilet` (BS 8300 + Part M)
- `CalculateAccessibleFixtures` (ADA + BS 8300)

Each is a `~100-line` IExternalCommand: input dialog → API call →
result panel. **Bulk runner opportunity** — batch these into groups
of 6 per runner to maintain commit discipline.

---

## 10. Suggested phased rollout

| Phase | Scope | Effort | Delivers |
|---|---|---|---|
| 111 | ARCH-01 through ARCH-06 | ~10 commits | Full architectural shell automation |
| 112 | STR-01 through STR-10 | ~12 commits | Full structural design suite |
| 113 | MEP-A-01 through MEP-A-12 | ~15 commits | Full MEP design automation |
| 114 | PLC-01 through PLC-07 + RT-01 through RT-07 | ~15 commits | Complete placement + routing automation |
| 115 | FAB-01 through FAB-10 | ~12 commits | Production-grade fabrication pipeline |
| 116 | STD-01 through STD-10 + REG-01 + 20 more `StandardsAPI` wrappers | ~35 commits | Complete standards + compliance surface |
| 117 | Full Dynamo node coverage for all above | ~30 commits | ~150 new Dynamo nodes across 8 categories |

---

## 11. Design philosophy (why this ordering)

1. **Engine-first, UI-second**. Every item above already has an
   engine in `StingTools/Model/`, `StingTools/Core/`, or the new
   `StingTools.Standards/`. The unit of work is almost always a
   thin IExternalCommand wrapper + a dispatch case + a XAML button
   + a Dynamo facade — not net-new algorithms.

2. **Coordinator time per commit**. Each P0 item removes at least
   30 minutes of manual Revit work per execution. P1 items save
   ~10 minutes. P2 items save ~2 minutes but compound on
   repetition.

3. **Standards-cited output**. Every workflow surfaces the
   reference standard name in the result panel so the audit trail
   has the legal + technical citation embedded.

4. **Composable through Dynamo**. Every new command gets a
   corresponding ZeroTouch node so graph-driven batches chain the
   workflows at will — e.g. `CableSize → BreakerSize → PanelSchedule
   → GroundingDesign` from a single Dynamo graph.

---

## 12. Non-goals

- No ML / AI-based routing (out of scope; separate research track)
- No bespoke title block .rfa families (manual Family Editor work)
- No Planscape server-side calc (Standards library is pure C#, runs
  locally on Revit machine)
- No replacement of Autodesk Robot / Etabs / Tekla interoperability
  (StandardsAPI calcs inform design; formal analysis runs in the
  dedicated package)

---

*Generated Phase 110 alongside the `StingTools.Standards` folder
integration and Dynamo library expansion. Runner IDs are stable so
subsequent phases can claim scope unambiguously.*

---

## 13. Phase 111-117 implementation status (delivered)

| Phase | Items | Status | Delivered |
|---|---|---|---|
| 111 | ARCH-01..06 | ✅ | 6 commands + dispatch + 6 Dynamo nodes |
| 112 | STR-01..10 | ✅ | 10 commands + dispatch + 10 Dynamo nodes |
| 113 | MEP-A-01..12 | ✅ | 12 commands + dispatch + 12 Dynamo nodes |
| 114 | PLC-01..07 + RT-01..07 | ✅ | 14 commands + dispatch + 14 Dynamo nodes |
| 115 | FAB-01..10 | ✅ | 10 commands + dispatch + 10 Dynamo nodes |
| 116 | STD-01..10 + REG-01 + 20 API wrappers | ✅ | 30 commands + dispatch + 30 Dynamo nodes |
| 117 | Full Dynamo coverage + Automation dock tab | ✅ | Automation tab rolled up, 82 new nodes |

**Delivered: 82 new IExternalCommand classes, 82 dispatch cases, 82 new
Dynamo nodes, new Automation dock-panel tab.**

### Implementation notes

- Every command uses the shared `NumericPrompt` helper from Phase 110
  and renders through `StingResultPanel` for consistency with the
  rest of the dock-panel surface.
- Where the underlying engine is already implemented in
  `StingTools.Model.*` or `StingTools.Standards.*`, the wrapper
  performs a real computation or real element walk (e.g. ARCH-06,
  MEP-A-01, STR-02, PLC-06, FAB-04, STD-03).
- Where an engine is planned but not yet surfaced on a public API
  (e.g. `ArchitecturalCreationEngine.StairEngine`,
  `StructuralPrecisionEngine.ColumnLoadTakedown`), the wrapper
  computes the code-cited geometry/numbers from first principles on
  the current input and surfaces a one-line "pending full wiring"
  note so the command is immediately useful as a reference
  calculator even before the deep engine integration lands.
- Every result panel cites its source standard inline so the audit
  trail carries the reference (BS 5395, BS 6180, BS EN 13830,
  BS 7671, BS EN 12056, IEC 60364, NEC 310, ASCE 7, EC 0-8,
  NFPA 10/13, SMACNA DW/144, ASME B31.1, BS EN 1090-2, etc.).
- The Dynamo surface now has **~186 nodes** across 13 top-level
  categories — every dock-panel button from Phase 109-117 is
  reachable from a Dynamo graph.

### Still outstanding (future runners)

- Revit element placement wiring for ARCH-01..04 (engines exist but
  internal; need a public surface on ArchitecturalCreationEngine).
- Full batch apply for MEP-A-01 (cable size) + MEP-A-04 (conduit
  all) + MEP-A-12 (balance apply) — commands iterate scope but do
  not yet write parameters back to elements.
- Deep engine wiring for STR-02 / STR-09 / STR-10 per the existing
  `StructuralPrecisionEngine` / `FabricationToleranceChecker` /
  `CreepDeflectionAnalysis` classes — wrappers are scaffolding.
- Authoring of the 180 ISO 6412 detail families (FAB-10) + the 8
  assembly title blocks (Phase 5 S5.15) — manual Family-Editor
  work, not code.
- Sample `.dyn` graphs for every Phase 111-117 node category — five
  representative graphs shipped in Phase 110; remaining categories
  follow the same scaffold.

All 82 new commands are live today as **reference-grade calculators**
with cited standards. Upgrading each to a **parameter-writing batch
operation** is the next incremental runner — one small commit per item,
~100 lines each.

---

## 14. Phase 118 incremental upgrades (delivered)

Follow-up runner picking up the "still outstanding" items from Section 13.
Every command below now calls a real engine or writes a real parameter;
previous revision was reference-calculator only.

| Runner | Upgrade | From | To |
|---|---|---|---|
| S118.01 | MEP-A-01 CableSizeApply | Circuit count only | Iterates OST_ElectricalCircuit, reads RBS_ELEC_VOLTAGE + RBS_ELEC_APPARENT_LOAD_A, calls StandardsAPI.CalculateCableSize, writes SizeAWG back to CABLE_SIZE / ELC_CBL_SIZE_TXT |
| S118.02 | MEP-A-12 BalanceApply | Info panel | Collects duct + pipe flow data, runs MEPBalancingEngine.BalanceSystem, writes balanced ActualFlowLs back to RBS_DUCT_FLOW_PARAM / RBS_PIPE_FLOW_PARAM |
| S118.03 | ARCH-01 AutoStair | Geometry prompt only | Calls StairEngine.DesignStair → rise/going/pitch/compliance + prompts to place via StairEngine.CreateStair (uses StairsEditScope, picks first 2 levels) |
| S118.03 | ARCH-03 AutoCurtainWall | Grid preview | Calls CurtainWallEngine.Design → CurtainWallSpec + prompts to place via CurtainWallEngine.Create at active level origin |
| S118.04 | STR-09 ToleranceCheck | Reference tolerances | Walks OST_StructuralColumns + Framing, calls FabricationToleranceChecker.CheckElement per element, reports passed/flagged + top-30 issues |
| S118.04 | STR-10 CreepDeflection | φ back-of-envelope | Calls CreepDeflectionAnalysis.Calculate(span, immediate, dead/live ratios, RH, age, years) → full CreepResult with Pass verdict + recommendation |
| S118.05 | Sample graphs | 8 total | 15 total (+7: Architecture, Structural design, MEP full chain, Life safety, Fab CNC, Standards audit, Engineering reference) |

### Still outstanding (next session)

| ID | Work | Complexity |
|---|---|---|
| ARCH-02 RailingEngine | Add public `RailingEngine.Create` to ArchitecturalCreationEngine.cs | Small engine addition |
| ARCH-04 Opening placement | Add wall-pick step + wire to OpeningEngine.CreateWallOpening | Small |
| ARCH-05 PlasteringEngine wiring | Wire to existing PlasteringEngine material-set call | Small |
| STR-02 FullColumnTakedown | Wire to StructuralPrecisionEngine.ColumnLoadTakedown (signature TBD) | Small-Medium |
| MEP-A-04 AutoSizeConduitAll | Already delegates to Phase 109 Mep_AutoSizeConduitCommand — confirm scope in Windows build | Verification only |
| FAB-10 ISO 6412 families | Manual Family Editor authoring (180 .rfa files) | Out of scope for code session |
| Phase 5 S5.15 title blocks | Manual Family Editor authoring (8 .rfa files) | Out of scope for code session |
| PushNotification wiring | The Phase 96 P0 notification service + FCM integration — separate runner | Medium |

Delivered in Phase 118: **8 commits** covering 6 real engine wirings +
7 Dynamo samples + roadmap status update.

