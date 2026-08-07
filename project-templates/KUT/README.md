# Kampala Uganda Temple (KUT) — Owner-Standards Profile & Workflow Presets

Project-deployment pack for the LDS **Kampala Uganda Temple** BIM coordination
engagement (Mayanja Davis, sub-consultant BIM Manager → Symbion Consulting
Group Studios). Aligns STINGTOOLS to the proposal `DV-BIM-PRO-001 v2` and the
Owner's A1 (architectural) and A2 (engineering) scope documents.

> **Scope discipline.** The contracted role is **information management,
> coordination and verification — not authoring** (proposal §2.2). Everything
> here supports that role. Authoring accelerators (HVAC/electrical/plumbing
> sizing, fabrication, energy model, photometrics) stay **out** of this pack —
> they are billable additional services (§6.1), not BIM-Manager deliverables.

---

## 1. What already existed (conflict review)

This pack was built **on top of** the existing Phase 191/192 KUT foundation —
nothing here duplicates or overrides those engines. Confirmed present before
drafting:

| Already in the codebase | Role |
|---|---|
| `StingTools/Data/STING_LOD_MATRIX.json` | Unified A1/A2 LOD matrix — carries the `deliverable-a/b/c` + `conformed-set` + `construction` + `deliverable-d` milestones (200/300/350/350/400/**500**) from proposal §3.5. **Deliverable D is LOD 500, not the 400 printed in the client's A1 document** — see §4c |
| `StingTools/Data/STING_OWNER_STANDARDS_PACK.json` | Corporate Owner-standards rule pack (KUT-aware) |
| `StingTools/Data/STING_TAG_SCHEMES.json` | Tag-scheme library incl. a disabled `kut-temple-example` |
| `StingTools/Data/STING_CLIMATE_DATA.json` | Kampala already present (`id: kampala`, elev 1155 m) |
| `STING_UGANDA_REGIONAL_LOADS.json`, `Core/UgandaRegionalDefaults.cs`, `StingTools.Standards/{UNBS,SSBS,CIBSE}` | Uganda localisation |
| `WORKFLOW_KUT_GateAudit.json` | Per-gate audit chain (KUT). Superseded `WORKFLOW_GateAudit.json`, which shipped alongside it until it was deleted — the old one contained `ValidateTags` and `CompletenessDashboard`, both `[Transaction(TransactionMode.Manual)]` writers that build legends, so it was not the read-only pre-gate check its successor's description argues for |
| Commands: `LOD_Verify`, `LOD_Stamp`, `TokenConfidenceAudit`, `TagScheme_Audit`, `Program_Audit`, `CSI_Assign`, `SpecLink_Reconcile`, `ReviewComments_Import` | Phase 192 KUT commands |

**This pack adds only the missing pieces:** an *activated* project overlay
(the corporate baselines ship disabled) and the four workflow rhythms the
proposal describes that had no preset yet.

---

## 2. What this pack adds

### A. Project overlay — the activated Owner-standards profile

**This is the single deployable KUT overlay pack.** Copy the whole
`_BIM_COORD/` folder into the temple project folder (next to the central
`.rvt`). Each file **merges by id over the corporate baseline, project wins** —
so you are activating/localising, never forking.
[`_BIM_COORD/manifest.json`](_BIM_COORD/manifest.json) records, per file, what
it overlays, the corporate baseline it merges over, the merge key and the code
that reads it — start there when building a pack for a different owner.

| File | Effect |
|---|---|
| `_BIM_COORD/manifest.json` | Not deployed data — the index of this pack (what each file overlays, its merge key, its reader) |
| `_BIM_COORD/owner_standards.json` | Enables the `KUT-ZZZ-XX-XX-M3-A-0001` sheet-number rule; narrows discipline codes to the temple team (A/S/M/E/P/FP/LV/G); enables the `ffe-fohlio-ref` FF&E link check at severity **WARN** (non-blocking — it reports FF&E not yet linked to Fohlio, it does not fail a gate) |
| `_BIM_COORD/lod_matrix.json` | Restates the confirmed 6-milestone matrix as the client-facing record |
| `_BIM_COORD/tag_schemes.json` | Enables the KUT element identifier (`KUT-…`) with the six-building volume map (BLD1 Temple→01 … BLD6 Guard→06, EXT→00) |
| `_BIM_COORD/project_config.json` | Six-building `LOC_CODES` (`BLD1..BLD6` + `EXT`) + per-building sequence grouping. **The tag scheme's volume map depends on these codes existing** — this is why the file is in the same pack |
| `_BIM_COORD/fohlio_map.json` | FF&E ↔ Fohlio mapping (`ASS_TAG_1_TXT` ↔ Item Tag; `FOHLIO_REF_TXT` link key). Used by ExLink `Fohlio_Export` / `Fohlio_Import`. Pairs with the enabled `ffe-fohlio-ref` check in `owner_standards.json`. |
| `_BIM_COORD/sting_classification.json` | Sets CSI MasterFormat as the leading classification standard (the Owner mandates RIB SpecLink) |
| `_BIM_COORD/climate_data.json` | **Optional.** The corporate baseline already carries Kampala; deploy this only to replace it with engineer-confirmed ASHRAE 2021 Entebbe (HUEN / 636800) values |

### B. Workflow presets (in `StingTools/Data/`, auto-loaded)
| Preset | Proposal ref | Rhythm |
|---|---|---|
The **Preset** column is the file; the **name** shown in the Workflows picker is the
preset's `name` field, given in bold. The two must stay in step — the deployment
checklist in §5 refers to presets by their picker name.

| Preset | name | Proposal ref | Rhythm |
|---|---|---|---|
| `WORKFLOW_KUT_Mobilisation.json` | **KUT Mobilisation** | §4.1 | Once at kick-off — params, worksets, filters, BEP, CDE register |
| `WORKFLOW_KUT_CoordinationCycle.json` | **KUT Coordination Cycle** | §4.2 | Fortnightly — federate, clash, BCF→ACC Issues, model health, completeness |
| `WORKFLOW_KUT_GateAudit.json` | **KUT Gate Audit** | A1 gates | **Read-only pre-gate check, any milestone** — run before declaring a deliverable ready. Writes nothing |
| `WORKFLOW_KUT_DeliverableA.json` | **KUT Deliverable A** | A1 Phase 1 | Gate — LOD 200 schematic; tokens, tags, program audit |
| `WORKFLOW_KUT_DeliverableB.json` | **KUT Deliverable B** | A1 Phase 2 | Gate — LOD 300 50% docs. The fullest gate: program + owner-standards + CSI + device coordination + Fohlio finishes + clash |
| `WORKFLOW_KUT_DeliverableC.json` | **KUT Deliverable C** | A1 Phase 2 | Gate — LOD 350 100% docs; adds CSI → SpecLink reconcile and the sheet register for the bidding set |
| `WORKFLOW_KUT_DeliverableD.json` | **KUT Deliverable D** | §4.4 | Close-out — Fohlio refresh, LOD 500 verify/stamp, CSI → SpecLink reconcile, audit, sign-off |
| `WORKFLOW_KUT_MonthlyReport.json` | **KUT Monthly Report** | §4.6 | Monthly — read-only KPI chain for the status report |
| `WORKFLOW_KUT_FFESync.json` | **KUT FF&E Sync** | A1 Fohlio | FF&E round-trip — Fohlio export → import → currency audit |

Run from **STING panel → Workflows**, or `WorkflowPreset`.

**Two orderings inside these presets are load-bearing — do not reorder them:**

- **`CSI_Assign` before `SpecLink_Reconcile`.** Reconciliation compares the model's CSI
  sections against the issued SpecLink TOC. With nothing assigning them first, every spec
  section reports as a gap and every model section as over-spec, and the report is worthless.
- **The Fohlio steps before `LOD_Verify` in Deliverable D.** LOD 500 requires
  `FOHLIO_REF_TXT` on Furniture and Furniture Systems, and `Fohlio_Import` is the only
  thing that writes it. Run the gate first and every piece of furniture fails for a
  data-linkage reason unrelated to as-built accuracy.

---

## 3. KPI mapping (proposal §4.6 → STING artefact)

| KPI in your proposal | Produced by | Run via |
|---|---|---|
| Open clash count + fortnight burn-down by discipline | `ClashRun` + `ExportModelHealth` | Coordination Cycle |
| Model-health score (warnings, duplicates, unplaced rooms, file-size trend) | `ExportModelHealth` / `ModelHealthDashboard` | Coordination Cycle, Monthly |
| Naming + metadata compliance % | `CompletenessDashboard` / `ValidateTags` | every workflow |
| Per-discipline compliance | `DiscComplianceReport` | Monthly |
| Exchange punctuality / sheet register | `ExportSheetRegister` + `SheetComplianceCheck` | Monthly |
| Review-comment close-out rate (Bluebeam) | `ReviewComments_Import` | as Owner sessions close |
| As-built capture currency (construction) | `LOD_Verify` (deliverable-d) trend | Deliverable D / quarterly |

KPIs are **derived from live commands**, not a static config file — so the
monthly report is always computed from the current model, never hand-kept.

**KPI dashboard (`KUT_KpiDashboard`)** renders the §4.6 set in one visual panel
(RAG bars + per-discipline table + clash burn-down), persists a snapshot to
`_BIM_COORD/kpi/kut_kpi_log.jsonl` for fortnight-on-fortnight burn-down, and
writes an HTML + CSV report for attachment to the monthly status report. It is
the final step of the **KUT Monthly Report** workflow. Model-health score =
compliance 40% · clash 25% · warnings 20% · stale 15%. The dashboard also
surfaces **Owner-system coverage**: Fohlio FF&E linked %, FF&E stale, SpecLink
CSI coverage %, and Niagara BMS point count (+ points missing an endpoint).

**Niagara (BMS) bridge** — `Niagara_ExportPoints` writes a Niagara-ingestable
point list (controls submittal) from BMS/IoT-tagged devices; `Niagara_Reconcile`
compares a Niagara/BACnet station export against the modelled BMS devices
(station-only vs model-only points). Device source is the `ICT_HEALTHIOT_*`
parameter set via `IoTDeviceRegistry`; live read-back stays an FM add-on.

---

## 4. Integration alignment (confirmed decisions)

- **Clash in ACC *and* STING.** ACC Model Coordination is the system of record;
  the Coordination Cycle runs STING's rule-based clash (discipline tolerance /
  access / maintenance-space checks ACC can't express) and pushes results as
  **BCF 2.1 → ACC Issues**. The ACC Model-Coordination **read** path is built:
  `ACC_PullClashes` (`Clash/AccPullClashesCommand.cs` + `V6/AccModelCoordSync.cs`)
  lists the coordination model sets, pulls the latest test's clashes, ranks them
  with `ClashTriageEngine`, writes a CSV, and escalates the top-ranked back to
  ACC Issues idempotently (order-invariant signature, sidecar at
  `_BIM_COORD/acc/pushed_clashes.json`). `ACC_SyncIssueStatus` reconciles the
  other way so a closed ACC issue lets a recurring clash re-raise. Both sit on
  the BIM Coordination Center ACC card, on the BIM tab's clash section, and in
  the fortnightly Coordination Cycle preset; credentials stay in
  `%APPDATA%\Planscape\acc_credentials.json`. Endpoint paths are verified
  against the public APS sample but **not** against a live tenant — do one live
  pull during mobilisation.
- **Fohlio = link, never duplicate.** Stay on the shipped CSV/XLSX link
  (`ExLink Fohlio_Import`, key `FOHLIO_REF_TXT`). The REST tier stays stubbed —
  no API key needed for this contract (see §6 of the chat advisory).
- **SpecLink / Niagara / Bluebeam / Teams.** Coordination-only; STING archives
  issued spec sets, reconciles the SpecLink TOC, imports Bluebeam comments, and
  feeds Teams/Bluebeam — no integration build required.
- **Speckle.** Not in the Owner-mandated environment — keep internal-only;
  do **not** introduce as a deliverable or competing model home.

---

## 4b. Phased tagging (TAG1-only + Scaffold Tiers)

Run the ISO tag now, complete the rest later, in parallel with the team:

- **`Scaffold Tiers`** (one click) binds every tier/container (8 tokens + `ASS_TAG_2..7`
  + discipline containers + TAG7 A–F), reveals all 10 paragraph tiers (`SetParagraphDepth`
  T10), and leaves the segment mask at default — a ready-to-fill model for colleagues.
- **`TAG1_ONLY: true`** in `project_config.json` (set via the Configure command) makes every
  tagging path write only the 8 tokens + `ASS_TAG_1_TXT` (the ISO 19650 first line) and
  **skip** the containers + TAG7 narrative — enforced centrally in
  `ParamRegistry.WriteContainers` + `TagConfig.WriteTag7All`. Default `false` (full pipeline).
- Colleagues complete the deferred tiers via element **Properties** or the **Excel
  round-trip** (Export → fill → Import). Gates stay green because `required_containers`
  is `["ASS_TAG_1_TXT"]` — the deferred tiers are tracked non-blockingly.

---

## 4c. LOD ladder — Deliverable D is LOD 500

The client's **A1 Design Scope of Services states LOD 400 for Deliverable D**. We
verify Deliverable D at **LOD 500**, and record here why.

| Deliverable | LOD | Milestone id |
|---|---|---|
| A — schematic | 200 | `deliverable-a` |
| B — 50% documents | 300 | `deliverable-b` |
| C — 100% documents | 350 | `deliverable-c` |
| Conformed set | 350 | `conformed-set` |
| Construction supervision (Work Program 3.1) | 400 | `construction` |
| **D — record / as-built** | **500** | `deliverable-d` |

LOD 400 is *fabrication and installation* maturity — the state a model is in while
the work is being built. LOD 500 is *field-verified as-built*, which is by
definition what a record/handover model is. A record model held to LOD 400 would
not have to carry the installed serial numbers, installation dates or maintenance
data that make it useful to the Owner's FM team, which is the entire point of the
deliverable. The A1 figure is read as a drafting error in the client document.

**LOD 400 has not been discarded** — it is reachable as the `construction`
milestone, covering Work Program item *3.1 Supervise the Building Construction
Contract*. Nothing that was verifiable before became unverifiable.

**Raise this with the Owner** at the next BEP review so the contract record and
the verification gate agree. If the Owner confirms 400 is intended, change
`deliverable-d` back to `"lod": 400` in `_BIM_COORD/lod_matrix.json` — the project
overlay wins over the corporate baseline, so it is a one-line project decision and
needs no code change.

---

## 5. Deployment checklist

**This is the only copy of the deployment sequence.**
`docs/examples/KUT/README.md` points here rather than restating it.

1. Copy this whole `_BIM_COORD/` folder into the temple project folder — all of
   it, not a selection. (`manifest.json` is an index, harmless to copy.)
2. Set `PRJ_ORG_PROJECT_CODE_TXT = KUT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` on
   Project Information (drives the sheet pattern + tag scheme).
3. **Set `PLM_PRJ_PLUMBING_CODE_TXT = IPC-US` on Project Information.**
   This is a **Revit Project Information value, so no repo file can set it** — it
   must be written into the model, and it is easy to miss because there is no
   error when it is absent. `DrainageSizer.ResolveCode` and `VentDesigner` route
   any value starting `IPC` to `IPCSiAdapter`; **anything else — including blank —
   silently falls back to BS EN 12056 (`BS-UK`)**. The Owner is US-standard, so
   leaving this unset produces UK drainage and vent sizes that look perfectly
   valid and are wrong for the code the design is reviewed against.
4. Confirm the LOC→volume map and the originator/volume/level/type number table
   against the Owner's week-1 BEP register; edit `_BIM_COORD/*.json` to match.
5. **Classify demolition by hand — `CSI_Assign` cannot do it.** See
   "Demolition (CSI Division 02) is a manual step" below. Assign the owner of this
   task at the Phase 2 kick-off, not at the Deliverable B review.
6. Run **KUT Mobilisation** once on the federation host.
7. Run **KUT Coordination Cycle** fortnightly and **KUT Monthly Report** monthly.
8. At each contractual gate, run **KUT Gate Audit** first — it is read-only and tells you
   what the gate would say without changing anything — then the gate itself:
   **KUT Deliverable A** (LOD 200) · **KUT Deliverable B** (LOD 300) ·
   **KUT Deliverable C** (LOD 350) · **KUT Deliverable D** (LOD 500).
   **KUT FF&E Sync** runs whenever the Fohlio register moves; Deliverable D refreshes the
   link itself, so it does not depend on you having remembered.

### Demolition (CSI Division 02) is a manual step

A1 Deliverable B requires an **Existing Conditions & Removals Plan**, and Deliverable C
carries the removals scope through to the tender documents. **`CSI_Assign` cannot classify
demolition**, so nothing in the automated pipeline will produce Division 02 sections.

`CsiMasterFormat.Resolve` matches on **category / family / type / system** only. Revit
expresses demolition through the **`Phase Demolished`** property, which the resolver never
receives. Naming-based rules (`(?i)existing|demolition|clearance`) were drafted and then
**withdrawn deliberately** — they would have read as coverage in a review while matching
nothing, because nobody names a toposolid "demolition". Honest absence beats a rule that
looks like it works. Tracked as ROADMAP **KUT-5**; the fix is phase-awareness in the
resolver, which is a schema change to the map CSV and is not scheduled.

Until then, one of:

- **Project overlay.** Add explicit Division 02 rows to `_BIM_COORD/csi_map.csv` keyed on a
  naming convention the team actually applies (e.g. a `DEMO_` type-name prefix agreed at
  kick-off, matched in the **TypeRegex** column — *not* FamilyRegex, which returns empty for
  system elements such as Topography and Toposolid). The overlay loads before the corporate
  map, so overlay rows win.
- **Write `CSI_SECTION_TXT` / `CSI_TITLE_TXT` directly** on the demolition scope from a
  schedule or a filtered selection, before running `SpecLink_Reconcile`.

Either way, do it **before** `SpecLink_Reconcile` — otherwise the Division 02 sections in the
Owner's SpecLink book report as over-specification (spec with no model backing) and the
reconciliation reads clean when it is not.

### Owner-standard settings summary

| Setting | Value | Where it lives |
|---|---|---|
| Classification standard | **CSI MasterFormat** (not Uniclass) | `_BIM_COORD/sting_classification.json` — in this pack |
| Plumbing code | **`IPC-US`** (not BS EN 12056) | `PLM_PRJ_PLUMBING_CODE_TXT` on **Project Information** — step 3 above |
| Specifications | RIB SpecLink | reconciled by `SpecLink_Reconcile` |
| FF&E / finishes / O&M | Fohlio | `_BIM_COORD/fohlio_map.json` + `Fohlio_*` commands |
| Demolition (Division 02) | **manual — not automated** | `_BIM_COORD/csi_map.csv` overlay or direct parameter write — step 5 above |

> **Not required by this contract:** ISO 19650 naming on Owner deliverables, COBie,
> or IFC. STING's ISO 19650 machinery remains our *internal* method for tagging and
> coordination — it is not imposed on what the Owner receives.

> Built without `dotnet build` verification (Linux sandbox). The JSON conforms
> to the existing registry schemas and every workflow `commandTag` resolves in
> `WorkflowEngine.ResolveCommand`; verify the workflow run end-to-end in Revit
> before the engagement relies on it.
