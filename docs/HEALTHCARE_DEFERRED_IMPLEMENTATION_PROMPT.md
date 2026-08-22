# Healthcare Pack — Deferred Items Implementation (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Implement the
deferred healthcare ROADMAP items (HC-DEF-01..HC-DEF-10) below, **without stopping to
ask**. These are real **features**, larger than the prior fix-passes — so this is a
**phased** build: land each phase fully and commit it before the next. At every choice
pick the most flexible, sustainable, **data-driven** option; reuse the existing engines
named below rather than reinventing them. Read every cited file before editing.

## The overriding guardrail — do NOT fake domain data or physics

This effort has been disciplined about never inventing standards values. Keep that:
- **Where an item needs data that must be authoritative** (medical-gas diversity
  factors, FGI clause→jurisdiction adoption mapping, full NCRP-151/PET physics, a real
  BMS transport), build the **mechanism / interface / data-file hook** and leave the
  actual values **project-supplied** (corporate baseline + `<project>/_BIM_COORD/`
  override, the pattern `MepSizingRegistry` / `RoomClassCodes` already use). Do NOT
  hardcode guessed numbers. If a piece genuinely cannot be done in-repo, implement the
  seam and update its ROADMAP entry — do not stub-and-pretend.
- Prefer depth over breadth: **fully landing Phase 1 is worth more than shallow-touching
  all nine.** If scope forces a stop, stop at a phase boundary with Phase 1 complete.

## Ground rules

- **Do not ask for confirmation.** Work autonomously; phase-by-phase commits.
- **Branch / worktree.** Work in a worktree off the latest healthcare branch. Prefer a
  fresh worktree + branch off `claude/healthcare-gap-fixes` (which carries all prior
  work): `git worktree add ../STINGTOOLS-hc-def -b claude/healthcare-deferred`. Confirm
  the base contains the Phase 196-198 commits. Never touch the shared
  `C:\Dev\STINGTOOLS` checkout; never commit to `main`. One logical commit per HC-DEF
  item; imperative messages ending with the repo's `Co-Authored-By` trailer.
- **Build.** This machine builds `StingTools` and `Planscape.Server` — build what you
  touch and report `0 errors` or the actual errors. Do NOT apply a "no build" caveat.
- **Any new shared parameter** must be registered in ALL of `MR_PARAMETERS.txt`,
  `MR_PARAMETERS.csv`, `PARAMETER_REGISTRY.json`, and `CATEGORY_BINDINGS.csv`, with a
  deterministic sibling GUID. (This gap has recurred — do not read an unregistered param.)
- **Docs.** New `#### Completed (Phase N — …)` CHANGELOG block (next free number — check
  the file; the branch is at Phase 198). As each HC-DEF item lands, **strike it through
  in `docs/ROADMAP.md`** with `CLOSED (Phase N)` like `HC-DEF-06` — and update the
  "data-model-ahead-of-logic" orphan list (e.g. the `CEQ_*` cluster stops being orphaned
  once HC-DEF-10 consumes it). Bump the `CLAUDE.md` phase pointer to the new max.
- Leave the branch for review — no merge, no PR. Final summary: per item DONE / PARTIAL
  (+ what's deferred and why) / build status, and any new params + GUIDs.

Existing infra confirmed present — REUSE these, do not duplicate:
`StingTools/Core/Adjacency/RoomGraphBuilder.cs`, `StingTools/UI/CobieMaterialBridge.cs`,
`StingTools/Temp/COBieDataCommands.cs` + `Docs/HandoverExportCommands.cs`, the
`StingTools/Data/COBIE_*.csv` pack, `StingTools.Standards/HBN/HBNStandards.cs`
(`AdjacencyTargets`, department-keyed), `RoomClassCodes` (has a `department` field per
canonical code), `RadiationSignoffGate`, `FgiAdoptionTracker`, `TwinReadbackBase` in
`Core/Twin/TwinReadback.cs`, `NFPA99Standards`.

---

## PHASE 1 — high-value, self-contained (build fully)

### HC-DEF-10 — Clinical-equipment COBie / SFG20 handover export
The `CEQ_CLINICAL` cluster (`CEQ_DECON_METHOD_TXT`, `CEQ_ENDO_AER_REF_TXT`,
`CEQ_ENDO_CYCLE_COUNT_INT`, `CEQ_ENDO_LAST_REPRO_DT`, `CEQ_ENDO_SCOPE_ID_TXT`,
`CEQ_GMDN_CODE_TXT`, `CEQ_UMDNS_CODE_TXT`, `CEQ_SFG20_REF_TXT`, `CEQ_INFECT_TIER_TXT`,
`CEQ_IMAGING_STRUCT_LOAD`, `CEQ_CLINICAL_BOOL`, `CEQ_EQP_TAG`, `CEQ_TAG_7_PARA_TXT`) is
registered but consumed by nothing (only `CEQ_CATEGORY_TXT` is read, by `HboAuditCommand`).
**Task:** build a clinical-equipment COBie handover export that reads the `CEQ_*` cluster
off clinical-equipment elements and emits the COBie Type/Component/Job/Spare rows, driven
by the existing `COBIE_TYPE_MAP.csv` / `COBIE_JOB_TEMPLATES.csv` / `COBIE_SPARE_PARTS.csv`
/ `COBIE_ATTRIBUTE_TEMPLATES.csv` (extend those CSVs with the clinical-equipment rows the
design promised — 50 clinical equipment types / SFG20 job refs — as **data**, not code).
Slot it into the existing COBie export path (`COBieDataCommands` / `HandoverExportCommands`
/ `CobieMaterialBridge`) rather than a parallel exporter; add a command + dispatch wiring
like its siblings. **Acceptance:** a clinical-equipment element with `CEQ_*` populated
appears in the exported COBie with its SFG20 job template + spare parts + GMDN/UMDNS
attributes; the `CEQ_*` cluster is removed from the ROADMAP orphan list.

### HC-DEF-08 — Reconcile HBN adjacency to canonical room classes
`HBNStandards.AdjacencyTargets` and `HEALTHCARE_ADJACENCY_HBN.csv` key on coarse
department codes (ED/IMAGING/OR/WARD/PHARMACY/MORT), but `AdjacencyValidator` now reads
canonical room classes (`RoomClassCodes`). **Task:** use `RoomClassCodes`'s `department`
cross-ref to group rooms by department before matching `AdjacencyTargets`, so canonical
room reads align with the department-keyed adjacency rules. Make `HEALTHCARE_ADJACENCY_HBN.csv`
the live data source for the targets (it is currently unused). Preserve existing target
values. **Acceptance:** adjacency rules fire correctly for rooms tagged with canonical
codes (e.g. `IMG-CT` maps to `IMAGING`); the CSV is consumed; no double-count.

### HC-DEF-01 — Radiation shielding write-back (QE-gated, audit-trailed)
`RadCalc*` commands compute but never persist. **Task:** add a write-back path that stamps
`RAD_LEAD_MM_NR` (+ companions: barrier type, workload/use/occ, distance, computed-at) onto
a **user-selected barrier element** (Wall/Door/Window/Generic Model — the categories
`RadShieldValidator` reads and that `RAD_DISTANCE_M_NR` is now bound to). Gate the write
through `RadiationSignoffGate`: never write an "approved" value without `RAD_QE_NAME_TXT`;
write with a **draft/approved flag** + an audit stamp (who/when). Reuse the existing gate
+ params; register any genuinely new companion param in all four files. **Acceptance:**
selecting a barrier and running write-back stamps the computed values with a draft flag
when unsigned, an approved flag when a QE is on record, and never silently certifies.

## PHASE 2 — moderate, uses existing engines

### HC-DEF-04 — AdjacencyValidator door-graph BFS
Replace the centroid-distance heuristic (which false-flags corridor-connected rooms) with
a real door/room-graph BFS via the existing `RoomGraphBuilder`. Distances become "N doors
apart", matching how `AdjacencyTargets` values are meant (e.g. `ED↔IMAGING ≤ 2`). Keep the
centroid path as a labelled fallback if the graph can't be built. Coordinate with HC-DEF-08
(both touch `AdjacencyValidator`) — do them in a sensible order. **Acceptance:** two rooms
far by centroid but connected by a short corridor are no longer mis-flagged; door-count
drives the check.

### HC-DEF-01b — True barrier-distance geometry (ties to HC-DEF-01)
Where feasible, derive the real source-to-barrier distance from model geometry (source
placement + barrier face) instead of the conservative 1.0 m default. If robust geometry
derivation is not achievable cleanly, **keep the conservative default** and implement only
the per-element `RAD_DISTANCE_M_NR` capture from the write-back workflow — then update the
ROADMAP entry to reflect what shipped. Do not ship a fragile geometry guess.

## PHASE 3 — mechanism + data-hook only; DEFER the authoritative data

For each of these, ship the **wiring/interface/data-file seam** and leave the
authoritative values project-supplied; update the ROADMAP entry to "mechanism shipped;
data project-supplied" rather than closing it if the data isn't authoritative.

### HC-DEF-07 — Med-gas diversity factors (N₂ / CO₂ / He / dental)
Add a **data-driven override** for per-gas diversity in `NFPA99Standards` (corporate JSON
+ project override), so a project can supply HTM 02-01 Pt A Table 8 / NFPA 99 Table
5.1.13.3.4 values. Keep the 1.0 fallback + the Phase-197 flag when a gas is unset. Do NOT
hardcode guessed factors. **Acceptance:** a project-supplied diversity value is honoured;
absent, the flagged 1.0 fallback stands.

### HC-DEF-09 — FGI adoption escalation wiring
Wire `FgiAdoptionTracker.ResolveSeverity` into the FGI-related validation path. This needs:
a project **US-jurisdiction** parameter + a **design-freeze date** on ProjectInformation
(register both in all four files), and a **clause-code → finding mapping** as a data file
(project-supplied overlay). Ship the mechanism (read jurisdiction + freeze date, map
findings, escalate Warning→Error once a state has adopted a clause by the freeze date).
Leave the mapping data as a documented overlay, not guessed. **Acceptance:** with a
jurisdiction + freeze date + mapping supplied, an adopted FGI clause escalates; without
them, behaviour is unchanged.

### HC-DEF-02 / HC-DEF-03 — Radiation physics depth (LINAC / PET / SPECT / Brachy)
Do NOT fake certification-grade physics. Where a **data-driven improvement** is clean, add
it as a project/QE-supplied table (e.g. per-modality Archer coefficients or a build-up-factor
hook) behind the existing calculators, keeping output DRAFT/QE-gated. Otherwise leave the
indicative calc as-is and keep the ROADMAP entry. The goal is a data seam for the QE, not a
guessed model.

### HC-DEF-05 — Twin live BMS transport
Do NOT pull a third-party BACnet/OPC-UA stack into the plugin assembly. Solidify the
pluggable `TwinReadbackBase` contract so a project/host can register a real transport
adapter (define the interface + a registration/discovery seam + a documented adapter
contract), keeping the empty built-ins as the default. Update the ROADMAP entry to
"pluggable transport seam shipped; live adapter external".

---

## Definition of done

- Phase 1 fully implemented (HC-DEF-10, 08, 01), each its own commit, build-verified.
- Phase 2 implemented where clean; Phase 3 = mechanism/seam shipped with data deferred.
- Every closed item struck through in ROADMAP with `CLOSED (Phase N)`; partial items'
  entries updated to state exactly what shipped vs what's project-supplied/external.
- Orphan list updated (CEQ_* cluster no longer orphaned after HC-DEF-10).
- New params registered in all four data files with GUIDs; `StingTools` +
  `Planscape.Server` build where touched; CHANGELOG phase block + CLAUDE.md pointer updated.
- Final summary: per-item DONE/PARTIAL/deferred with reasons, new params + GUIDs, builds.
