# STINGTOOLS HVAC — Capability & Gap Analysis

**Date:** 2026-07-06 · **Branch:** `claude/hvac-gap-analysis`
**Scope:** Phase 180 (HVAC Center panel) + Phase 187 (design engines) + MEP sizing/balancing + acoustics + refrigerant + load/climate.
**Method:** Direct read of the calc engines, commands, UI wiring and data files; benchmarked against native Revit 2025/26, MagiCAD, TRACE 700, Carrier HAP, IES VE, Daikin VRV Xpress.

---

## 1. Verdict

The HVAC work is **real engineering, not UI scaffolding.** The panel reads live Revit MEP systems, the calc kernels implement recognised standard methods (Darcy–Weisbach/Swamee–Jain, Hardy Cross, ASHRAE RTS, VDI 2081/A48, Clausius–Clapeyron flash checks), and results are written back to native geometry parameters. Auto-routing models actual 3D paths. A one-click `FullDesignPass` chains block-load → propagate → auto-size → balance → NC → pressure-class → stale-flag.

This puts STING **ahead of native Revit** (which only does velocity/friction duct sizing with no balancing, no NC, no diversity, no refrigerant) and **into the same category as MagiCAD** on breadth — while lagging the dedicated load tools (TRACE/HAP/IES) on physics fidelity, which is the correct trade for a BIM-integrated early/mid-design tool.

The gaps below are about **closing the fidelity/coverage distance** and **fixing a few concrete correctness leaks and wiring bugs**, not about rebuilding.

---

## 2. Capability map — what is genuinely solid

| Area | Method implemented | Assessment |
|---|---|---|
| **Duct friction** | Darcy–Weisbach + Swamee–Jain explicit Colebrook; Re-regime split at 2300; galv/alu/flex roughness | Industry-standard, ±1% vs iterative Colebrook. Matches Trane Ductulator / ASHRAE. |
| **Velocity duct sizing** | `A=q/v` → round to region size table; per-role velocity/aspect targets; writes Width/Height/Diameter to native params | Production. Region-aware (UK_SI/US_IP/EU_SI). |
| **Per-element role detection** | `HvacSegmentRoleDetector` graph-walk (main/branch/runout by hop depth + terminal sniff); `PipeServiceDetector` by system abbreviation | **Production (Phase 182/183)** — this is *ahead* of MagiCAD which needs manual classification. **NOTE: CLAUDE.md still says this is "pending" — docs are stale.** |
| **Fitting losses** | SMACNA Appendix A baseline + manufacturer C (Lindab/Trox/Halton) + project override | Data-driven, `ΔP=C·½ρv²`. Good. |
| **Balancing** | Real Hardy Cross (`ΔQ = −Σh/Σ(n·h/Q)`, n=2), convergence + iteration log, writes flow back to `RBS_PIPE_FLOW_PARAM` | Production-grade, converges. |
| **Load calc** | Simplified steady-state U·A·ΔT + ASHRAE RTS convolution; 24-h design-day; **system-level diversity** (fixes Revit's sum-of-peaks over-sizing) | The diversity handling is a genuine differentiator vs native Revit. |
| **Load fidelity tiering** | Tier-1 class RTF (L/M/H) → Tier-3 CTF Y-series from `STING_CTF_COEFFICIENTS.json` | Progressive; higher tier is better than class-based. |
| **Solar geometry** | Rigorous declination/latitude/hour-angle beam projection (replaced old linearised azimuth) | Geometry correct. |
| **Acoustics (NC)** | VDI 2081 / ASHRAE A48 attenuation tables (straight/lined/elbow/tee/end-reflection/silencer), Bullock v⁶ regen, Eyring room field; reads real duct area/length/flow | Suitable for preliminary path NC. |
| **Refrigerant sizing** | Darcy + Blasius, oil-return velocity floors (vert/horiz), liquid static-head sign convention, Clausius–Clapeyron flash-gas check, 7 vendor length-table envelopes | Accurate at fixed design saturation. |
| **Integration** | Reads OST_MEPSpaces/Rooms, exterior walls + hosted windows (area/orientation), ducts, MechanicalSystem graph; gbXML export + import round-trip; native loads via reflection | Strong model integration. |
| **Flexibility** | `STING_MEP_SIZING_RULES.json` + `STING_LOAD_PROFILES.json` + `STING_CONSTRUCTION_PROFILES.json` + `STING_CLIMATE_DATA.json`, all with `_BIM_COORD/` project overrides | Excellent no-code customisation surface. |
| **Automation** | `FullDesignPass` composite chain; `AutoDropCommand` real 3D routing + BS5572/MSS SP-58 hangers; WorkflowEngine tag-keyed presets | Genuine end-to-end automation. |

---

## 3. Gaps by axis

### 3A. Accuracy / correctness (physics)

**Highest-impact accuracy leak — construction properties come from a profile, not the model:**
- U-values are read from a global `ConstructionProfileRegistry` profile (e.g. PartL2021 wall 0.18) applied to *all* exterior walls; **not** derived from each wall type's Revit compound structure. Mixed/refurbished envelopes are invisible → ±10–20% conduction error. (`BlockLoadEngine.cs:244`, `ConstructionProfileRegistry`.)
- Window **SHGC is profile-global**, not read from the window type → solar gain ±30% for high-SHGC glass or where shading isn't modelled.
- *This is the single most valuable fidelity fix: read layer U from `WallType.GetCompoundStructure()` layer resistances and glazing SHGC from the family/type, falling back to the profile only when absent.*

**Solar model simplifications:**
- Diffuse fixed at **15% of direct-normal** — understates hazy/high-latitude/tropical diffuse by ~20%. Consider Erbs/Perez decomposition.
- **No ground reflectance** (albedo) — misses 5–10% on low storeys.
- **No sky-temperature / long-wave correction** on roofs → roof peaks overstated ~5–10%.
- **Design-day DOY hardcoded** (202 cooling / 21 heating) with no override — can miss the true worst month in monsoon/shoulder-season climates.

**Infiltration:** windward `Cp=0.6` is a single global value; no per-façade windward/leeward exposure. Minor vs solar.

**Acoustics:**
- Synthetic fan Lw fallback (`67 + 10log Q + 10log ΔP`) assumes a centrifugal-AHU spectrum shape — wrong for axial/mixed-flow; only reliable when a real spectrum is in `STING_FAN_SPECTRA.json`.
- Diffuser regeneration **double-counts** terminal mixing (author-acknowledged +3–5 dB bias); code already tells users to override with manufacturer terminal NC.
- **No breakout/casing transmission**; damper has regen but no insertion-loss; lined-duct is hardcoded 25 mm only; room geometry is a hardcoded 100 m³/α=0.2 cube, **not read from the Revit Room/Space**.

**Refrigerant:**
- Properties are **spot design points** (5 °C/45 °C) not an EoS — cannot track sliding-pressure/part-load VRF. CoolProp/REFPROP integration would fix this.
- Two-phase suction ΔP is a flat **10% uplift** vs Lockhart–Martinelli (real penalty 1.5–3×).
- No capacity modulation, heat-recovery (simultaneous heat+cool), or oil-holdup validation.

### 3B. Integration

- **Refrigerant sizing is dialog-only.** Capacity, equivalent length, and lift are typed by hand; the Revit doc is used only to resolve the vendor registry. It does **not** auto-read equipment capacity, trace the piping run for equivalent length, or read ODU/IDU elevations from 3D coords. This is the biggest integration gap vs Daikin VRV Xpress. (`RefrigerantSizingDialog.cs`, `HvacRefrigerantSizeCommand.cs`.)
- **NC room model ignores the model** — hardcoded room defaults rather than Revit Room volume/surfaces/finish absorption.
- **gbXML import is overwrite-only** — no diff/delta vs STING's own BlockLoad; the two engines produce different numbers (Revit sums zone peaks; STING does system diversity) and the user must silently pick which to trust.

### 3C. Flexibility

Strong overall (JSON + project overrides everywhere). What is **not** overridable and probably should be: design-day DOY, outdoor daily range (fixed 8 K), diffuse fraction, infiltration Cp, and the solar clear-sky constants. These are baked into `BlockLoadEngine`.

### 3D. Automation logic

- `FullDesignPass` chain, Hardy Cross, and `AutoDropCommand` are genuinely automated. Good.
- **`Routing_GenerateLayout` is a dead button** — the tag dispatches but `GenerateLayoutCommand.cs` does not exist (glob found nothing). Either implement it or remove the wiring so it doesn't throw. **(Verify + fix.)**
- **Panel grids start empty** on open and require a manual "Refresh grids" click (by design, to avoid scan cost). A one-time lazy auto-populate on first tab activation with a progress bar would remove a papercut.
- **PICV curves and valve Kvs are loaded but not used inside the Hardy Cross loop**; pump-curve intersection (`OperatingPoint`) exists but isn't wired to the balance command → balancing is flow-correction only, not a true pump/valve-authority solve.
- **Friction Pa/m budget is read but not enforced during sizing** — only max velocity constrains the size; `maxFrictionPaPerM` is validation-only. Long mains sized purely on velocity can exceed the friction target.

### 3E. Missing coverage vs the market

Not present at all (candidate roadmap, not defects): psychrometric/coil sizing & apparatus dew-point, AHU/plant selection, annual/8760 energy & part-load, chilled-beam/radiant/UFAD load paths, condensation/dew-point risk on chilled pipe, stratification for tall spaces, and a proper duct **static-pressure/fan-selection report** (the highest-index path total → fan external static).

---

## 4. What's possible (positioning)

- Native Revit gives you velocity/friction duct sizing and schedules — nothing else. Everything STING adds (balancing, NC, diversity-aware loads, refrigerant, carbon, auto-routing) is **net-new capability the market pays MagiCAD/Hevacomp money for.** The strategic question is depth vs breadth, not whether to build.
- The Revit API **can** support the fidelity fixes above: `WallType.GetCompoundStructure()` exposes per-layer thermal resistance; glazing SHGC is on the type; `Space` carries volume/boundary for the acoustics room model; connector graphs already power role detection, so tracing refrigerant equivalent-length and reading ODU/IDU Z is feasible with the same walker.
- For load physics you will **not** out-fidelity TRACE/HAP/IES inside Revit, and shouldn't try. The right play is: (a) tighten inputs from the real model, (b) keep the gbXML bridge as the certified-tool handoff, and (c) make the diversity + speed advantage explicit.

---

## 5. Recommended actions (prioritised)

**Fix now (correctness / hygiene — low effort):**
1. Resolve `Routing_GenerateLayout` — implement `GenerateLayoutCommand` or drop the dispatch case.
2. Update CLAUDE.md: per-element duct-role & pipe-service detection **shipped** (Phase 182/183); remove the "pending" caveat.
3. Enforce `maxFrictionPaPerM` alongside velocity in `DuctSizingApplyEngine` (size up when either limit is exceeded).

**High value / medium effort:**
4. **Read U-values from `WallType.GetCompoundStructure()` and SHGC from the window type**, profile as fallback. Biggest single accuracy gain.
5. **Auto-populate refrigerant sizing from the model** — capacity from equipment param, equivalent length by tracing the connector graph, lift from ODU/IDU Z. Removes the manual dialog.
6. Add a **duct static-pressure / fan-selection report** (index run → total external static) — commonly the first thing a mechanical engineer asks for and currently absent.
7. Make design-day DOY, daily range, diffuse fraction and Cp **project-overridable** (they're already the only baked-in load constants).

**Strategic / larger:**
8. Refrigerant EoS via CoolProp for sliding-pressure/part-load; replace the 10% two-phase multiplier with Lockhart–Martinelli.
9. Wire PICV authority + pump-curve intersection into the Hardy Cross solve for a true balanced-flow result.
10. NC: read room volume/absorption from the Revit Space; add breakout + manufacturer terminal-NC lookup.
11. gbXML import: add a per-zone **delta report** (STING vs simulator) instead of silent overwrite.

---

## 6. Caveat on this review

Static analysis only — no `dotnet build` or in-Revit run was performed for this document. The "dead button" (#1) and the friction-enforcement gap (#3) should be confirmed live before action. All file:line references are from the `claude/hvac-gap-analysis` branch state on 2026-07-06.
