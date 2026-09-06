# STINGTOOLS HVAC — Gap Remediation Prompt

> **Purpose:** A single, self-contained brief to close the gaps found in the HVAC capability
> review ([docs/HVAC_GAP_ANALYSIS.md](HVAC_GAP_ANALYSIS.md)). Hand any item to a coding agent
> cold — each has current state, desired state, files, and acceptance criteria.
> **Do the items in tier order.** Verify in Revit before merge (this machine can build — Revit
> 2025/26 + .NET SDK; do not reflexively use the no-build caveat).

---

## Context you need before starting

- **Repo:** StingTools C# Revit plugin, `net8.0-windows`, Revit 2025/2026/2027.
- **HVAC surfaces:** Panel `UI/StingHvacPanel.xaml(.cs)` + `UI/StingHvacCommandHandler.cs` (tag → command dispatch, `Snapshot()` header context). Commands under `Commands/Hvac/`, `Commands/Mep/`, `Commands/Routing/`. Engines under `Core/Hvac/`, `Core/Mep/`, `Core/Calc/`, `Core/Climate/`, `Core/Acoustic/`, `Core/Refrigerant/`.
- **Data (corporate baseline + `<project>/_BIM_COORD/` override):** `STING_MEP_SIZING_RULES.json`, `STING_LOAD_PROFILES.json`, `STING_CONSTRUCTION_PROFILES.json`, `STING_CLIMATE_DATA.json`, `STING_CTF_COEFFICIENTS.json`, `STING_FAN_SPECTRA.json`, `STING_REFRIG_VENDOR_LIMITS.json`.
- **Conventions:** `[Transaction(Manual)]` for writes / `[ReadOnly]` for queries; wrap DB edits in `Transaction` named `STING …`; `TaskDialog` not `MessageBox`; `StingLog.Info/Warn/Error` — no silent catches; use `ParameterHelpers.SetString/SetIfEmpty`, `MepSizingRegistry.Get(doc)`. New shared params go in `Data/MR_PARAMETERS.txt` (group `HVC_SYSTEMS`) and must be bound via `LoadSharedParamsCommand`.
- **Log every completed item** in `docs/CHANGELOG.md`; keep `CLAUDE.md` structural facts current.

---

## TIER 1 — Correctness & hygiene (do first, low effort)

### 1.1 Resolve the dead `Routing_GenerateLayout` button
**Current:** `StingHvacCommandHandler.cs` dispatches tag `Routing_GenerateLayout` to `GenerateLayoutCommand`, but no such class/file exists (glob of `Commands/Routing/` found nothing). CLAUDE.md lists it as a v4 command. Clicking it throws.
**Do:** First confirm live (grep the tag + class; check the DOCKPANEL/HVAC panel button that raises it). Then **either** (a) implement `Commands/Routing/GenerateLayoutCommand.cs` as the "detailed routing after AutoDrop" step (connect placed drops into runs using the existing `Core/Routing` A*/voxel engines), **or** (b) if out of scope now, remove the dispatch case and the panel button and note it in CHANGELOG.
**Accept:** No routable tag maps to a missing class anywhere in the HVAC/main handlers; button either works or is gone; build clean.

### 1.2 Correct stale CLAUDE.md caveats
**Current:** CLAUDE.md (Phase 180/181 section, caveat 2) says per-element segment-role / pipe-service detection is "still pending — the data path is in place." It actually **shipped**: `Core/…/HvacSegmentRoleDetector.cs` (graph-walk main/branch/runout, Phase 182) and `PipeServiceDetector.cs` (system-abbreviation match, Phase 183) are production and used in the auto-size pass.
**Do:** Update the caveat to reflect shipped state; cross-check the HVAC panel caveats (PaneGuid, grid-empty-on-open, `Hvac_RunLoads`/`Hvac_ExportGbxml` are real) are still accurate.
**Accept:** No caveat in CLAUDE.md contradicts the code.

### 1.3 Enforce the friction (Pa/m) budget during duct sizing
**Current:** `Core/…/DuctSizingApplyEngine.cs` sizes on `maxVelocityMs` only. Roles carry `maxFrictionPaPerM` (e.g. main 1.2, branch 1.0) but it is validation-only — long mains sized on velocity can exceed the friction target.
**Do:** After the velocity-based candidate size is chosen, compute straight-run friction via `Core/Calc/DuctFrictionSolver` at the candidate size + design flow; if `Pa/m > role.maxFrictionPaPerM`, step up to the next standard size and re-check (bounded loop). Use the role's air density from the header `Snapshot()`/climate registry.
**Accept:** A long main at high flow that passes velocity but fails friction is upsized; unit-check one worked case against a Ductulator value; audit stamp records which limit governed (extend `HVC_SIZE_RULE_ID_TXT`).

---

## TIER 2 — High-value fidelity & integration (medium effort)

### 2.1 Read construction properties from the model, not just the profile *(biggest accuracy gain)*
**Current:** `BlockLoadEngine.cs:244` uses U-values from a global `ConstructionProfileRegistry` profile for all exterior walls; window **SHGC is profile-global**. Mixed/refurbished envelopes and real glazing are invisible → ±10–20% conduction, ±30% solar.
**Do:**
- In `EnvelopeDetector` (where walls/windows are collected per Space), read each wall's U from `WallType.GetCompoundStructure()` — sum layer thermal resistances (`CompoundStructureLayer.Width / material ThermalConductivity`) + standard inside/outside air films → U = 1/ΣR. Fall back to the profile when the type has no thermal data (curtain/generic).
- Read glazing **SHGC / U** from the window `FamilySymbol` (Analytical Construction param or a `HVC_GLAZING_SHGC_NR` / built-in if present); fall back to profile.
- Keep the profile as the documented fallback; log per-segment when fallback is used.
**Accept:** A model with two wall types of different U produces different conduction per wall; a high-SHGC glazing type raises solar vs a low-SHGC one; fallback path still works on a stripped model; result panel reports how many segments used model data vs fallback.

### 2.2 Auto-populate refrigerant sizing from the model
**Current:** `HvacRefrigerantSizeCommand.cs` + `RefrigerantSizingDialog.cs` are dialog-only — capacity, equivalent length and lift are typed by hand; the doc is used only to resolve the vendor registry. Main integration gap vs Daikin VRV Xpress.
**Do:** Before showing the dialog (pre-fill it), when an equipment element or refrigerant run is selected:
- Capacity from the equipment param (`HVC_CAPACITY_KW`), summed over served IDUs where applicable.
- Equivalent length by walking the refrigerant connector graph (reuse the `HvacSegmentRoleDetector`/`PipeServiceDetector` walker) — straight length + fitting equivalents.
- Lift from the Z-delta between ODU and IDU connector origins (world coords).
Leave every field user-editable (pre-fill, don't lock).
**Accept:** Selecting a VRF ODU + its IDUs pre-fills capacity/length/lift within tolerance of a hand calc; empty selection still allows the manual path; vendor envelope checks run against the auto values.

### 2.3 Duct static-pressure / fan-selection report *(new, commonly requested)*
**Current:** No index-run total-static report exists — an engineer can't get "fan external static" from STING today.
**Do:** New `[ReadOnly]` command `Hvac_FanStaticReport` (`Commands/Hvac/HvacFanStaticReportCommand.cs`): from a selected system or AHU, walk the duct network to the highest-pressure-drop path (index run), sum straight friction (`DuctFrictionSolver`) + fitting losses (SMACNA/manufacturer C) + terminal/coil/filter allowances (from rules or prompt), output total external static (Pa) with a per-segment breakdown table. Push a result row to the panel; offer CSV export.
**Accept:** Report lists the index path and a defensible total static for a test system; matches a manual sum on a small network; wired to a RPRT-tab button + panel result row.

### 2.4 Expose the baked-in load constants as project overrides
**Current:** Design-day DOY (202/21), outdoor daily range (8 K), diffuse fraction (15%), and infiltration `Cp` (0.6) are hardcoded in `BlockLoadEngine`; everything else in loads is JSON-driven.
**Do:** Move these into a small `loads` block in `STING_CLIMATE_DATA.json` (or a new `STING_LOAD_ASSUMPTIONS.json`) with `_BIM_COORD/` override, read through the existing registry pattern; keep current values as defaults.
**Accept:** Changing DOY/range/diffuse/Cp in the project override changes the computed load without a rebuild; defaults unchanged when no override present.

---

## TIER 3 — Strategic depth (larger, schedule deliberately)

### 3.1 Refrigerant EoS + real two-phase ΔP
Replace spot saturation points in `RefrigerantProperties.cs` with a CoolProp-backed lookup (sliding pressure / part-load), and the flat 10% suction multiplier in `RefrigerantPipeSolver.cs` with a Lockhart–Martinelli (or Chisholm) two-phase multiplier. **Accept:** suction ΔP varies with quality; sizing tracks non-design saturation; existing vendor-envelope checks still pass.

### 3.2 True balanced-flow solve (PICV + pump curve)
PICV authority windows and valve Kvs are loaded but unused inside `Core/Calc/HardyCrossSolver.cs`; `OperatingPoint` (pump-curve intersection) exists but isn't wired to the balance command. Fold valve authority into loop head loss and intersect the system curve with the pump curve. **Accept:** balancing reports valve positions/authority and a real duty point, not just corrected flows.

### 3.3 Acoustics fidelity
Read room volume/surface/absorption from the Revit `Space` (replace the hardcoded 100 m³/α=0.2 cube); add breakout/casing transmission; add a manufacturer terminal-NC lookup to supersede the double-counted diffuser regen; broaden `STING_FAN_SPECTRA.json` beyond the centrifugal default and only fall back to the synthetic Lw when no spectrum matches. **Accept:** room-side NC responds to real room geometry/finish; diffuser NC uses catalogue value when available.

### 3.4 gbXML import delta report
`HvacImportGbxmlLoadsCommand` currently overwrites Space loads silently. Add a per-zone delta report (STING BlockLoad vs simulator) before applying, so the divergence (Revit sum-of-peaks vs STING system diversity) is visible and the user chooses. **Accept:** import shows a diff table and requires confirm before stamping.

### 3.5 Coverage candidates (scope separately, not defects)
Psychrometric coil sizing + apparatus dew-point; AHU/plant selection; annual/8760 part-load energy; chilled-beam/radiant/UFAD load paths; chilled-pipe condensation/dew-point risk; tall-space stratification.

---

## Cross-cutting acceptance & wrap-up
- Each item: builds clean (Release, expect the 6 pre-existing baseline warnings, not 0); no new silent catches; new writes wrapped in named `STING` transactions; new shared params added to `MR_PARAMETERS.txt` + bound.
- Verify Tier 1.1 and 1.3 **live in Revit** (they were flagged from static analysis only).
- Log each landed item in `docs/CHANGELOG.md`; update `CLAUDE.md` structure facts; move any closed roadmap gaps out of `docs/ROADMAP.md`.
- Commit per logical change on a feature branch; do not commit to `main`.
