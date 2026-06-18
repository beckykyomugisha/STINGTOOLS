# US standards preset — capability map (Phase 192 D2)

The Kampala Uganda Temple project is delivered to **US codes** (NEC,
NFPA 13/72, ComCheck/ASHRAE 90.1, ASHRAE 62.1, NFPA 780). STING is
Uniclass / BS / CIBSE-centric in places. This page maps which existing
engines are already US-capable (switch them on with data, no new code)
and which remain BS-only (flagged, not silently wrong).

**Data, not code.** Where an engine already reads its limits from a JSON
registry, the US values are supplied via an overlay — see
[`../StingTools/Data/STING_US_PRESET_OVERLAY.json`](../StingTools/Data/STING_US_PRESET_OVERLAY.json)
(copy the slots you need into `<project>/_BIM_COORD/mep_sizing_rules.json`).
No new US calculation engines were added in this phase.

## Already US-capable (configure + use)

| Capability | Engine / command | US basis | How to switch |
|---|---|---|---|
| Lighting power density | `LightingPowerDensityCommand` + `STING_LPD_LIMITS.json` | ASHRAE 90.1-2019 | Pick `ASHRAE_90_1_2019` standard (default) |
| ComCheck lighting input | `ComCheck_Export` (Phase 192 D1) | ASHRAE 90.1 (reuses the LPD table) | Run as-is; CSV companion for COMcheck |
| Cable-tray fill | `ShowTrayFillCommand` / `TrayFillCalculator` | NEC 392.22 | `cableTray.maxFillPct: 50` in the overlay |
| Conduit fill | `ConduitFillValidateCommand` / `ConduitFillSolver` | NEC Ch 9 Table 1 | `conduit.maxFillPct: 40` (>2 conductors) in the overlay |
| Demand factors | `DemandFactorReportCommand` | NEC demand factors | Report already exposes NEC factors |
| Arc flash | `ArcFlashCommand` (IEEE 1584) | IEEE 1584 / NFPA 70E | US standard already |
| Fault current | `FaultCurrentCommand` (IEC 60909) | IEC 60909 (AIC ratings) | Verify AIC against the US equipment schedule |
| Plumbing supply sizing | `WaterSupplySizer` (Hunter's method) | Hunter's method (US-origin) | US-applicable as-is |
| Duct / pipe standard sizes | `MepSizingRegistry` (`US_IP` region) | US_IP size + bore tables | `_defaultRegion: US_IP` in the overlay |
| HVAC loads | `BlockLoadEngine` | ASHRAE Handbook Fundamentals / RTS | Set the climate site (Kampala) + construction profile |
| HVAC NC acoustics | `NcPredictionEngine` | ASHRAE A48 / VDI 2081 | NC targets in the acoustics block |

## Remains BS / EN-centric (flag, do not fix here)

| Capability | Current basis | US equivalent (not implemented) | Note |
|---|---|---|---|
| Lightning protection | `LpsEngine` — BS EN 62305 | NFPA 780 | The LPS report now emits an **INFO note** when the project country is US / NFPA 780 is owner-mandated: figures are EN 62305; NFPA 780 is a performance spec by the engineer of record. A2 makes lightning a performance spec anyway. |
| Sprinkler hydraulics | BS EN 12845 references | NFPA 13 | STING does not run NFPA 13 hydraulic calcs; coordinate the FP engineer's sizing. |
| Drainage sizing | BS EN 12056 / Maguire | IPC / UPC fixture units | Plumbing drainage uses BS EN tables; US fixture-unit sizing is a separate exercise. |
| Cable sizing | BS 7671 / IEC 60364 | NEC 310.16 ampacity | `CableSizerEngine` is BS 7671; NEC ampacity tables are not implemented. |
| Insulation thickness | BS 5422 | ASHRAE 90.1 §6.8.3 | `STING_BS5422_INSULATION.csv` is BS; US insulation schedules differ. |

## What this phase shipped

- `STING_US_PRESET_OVERLAY.json` — the documented overlay above.
- This capability map.
- An EN 62305 provenance INFO note in the LPS full report (keyed on the
  project country / owner-mandated NFPA 780).

It deliberately did **not** add NEC ampacity, NFPA 13 hydraulics, or IPC
drainage engines — those are genuine new-engine work, flagged here so the
gap is explicit rather than silently mis-applied.
