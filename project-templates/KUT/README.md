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
| `StingTools/Data/STING_LOD_MATRIX.json` | Unified A1/A2 LOD matrix — already carries the exact `deliverable-a/b/c` + `conformed-set` + `deliverable-d` milestones (200/300/350/350/400) from proposal §3.5 |
| `StingTools/Data/STING_OWNER_STANDARDS_PACK.json` | Corporate Owner-standards rule pack (KUT-aware) |
| `StingTools/Data/STING_TAG_SCHEMES.json` | Tag-scheme library incl. a disabled `kut-temple-example` |
| `StingTools/Data/STING_CLIMATE_DATA.json` | Kampala already present (`id: kampala`, elev 1155 m) |
| `STING_UGANDA_REGIONAL_LOADS.json`, `Core/UgandaRegionalDefaults.cs`, `StingTools.Standards/{UNBS,SSBS,CIBSE}` | Uganda localisation |
| `WORKFLOW_GateAudit.json` | Per-gate audit chain (KUT) |
| Commands: `LOD_Verify`, `LOD_Stamp`, `TokenConfidenceAudit`, `TagScheme_Audit`, `Program_Audit`, `CSI_Assign`, `SpecLink_Reconcile`, `ReviewComments_Import` | Phase 192 KUT commands |

**This pack adds only the missing pieces:** an *activated* project overlay
(the corporate baselines ship disabled) and the four workflow rhythms the
proposal describes that had no preset yet.

---

## 2. What this pack adds

### A. Project overlay — the activated Owner-standards profile
Copy the whole `_BIM_COORD/` folder into the temple project folder (next to the
central `.rvt`). Each file **merges by id over the corporate baseline, project
wins** — so you are activating/localising, never forking.

| File | Effect |
|---|---|
| `_BIM_COORD/owner_standards.json` | Enables the `KUT-ZZZ-XX-XX-M3-A-0001` sheet-number rule; narrows discipline codes to the temple team (A/S/M/E/P/FP/LV/G); adds a (disabled) Fohlio FF&E link check |
| `_BIM_COORD/lod_matrix.json` | Restates the confirmed 5-milestone matrix as the client-facing record; adds Lighting Fixtures + Plumbing Fixtures category rules |
| `_BIM_COORD/tag_schemes.json` | Enables the KUT element identifier (`KUT-…`) with the six-building volume map (BLD1 Temple→01 … BLD6 Guard→06, EXT→00) |
| `_BIM_COORD/fohlio_map.json` | FF&E ↔ Fohlio mapping (`ASS_TAG_1_TXT` ↔ Item Tag; `FOHLIO_REF_TXT` link key). Used by ExLink `Fohlio_Export` / `Fohlio_Import`. Pairs with the now-enabled `ffe-fohlio-ref` check in `owner_standards.json`. |

### B. Workflow presets (in `StingTools/Data/`, auto-loaded)
| Preset | Proposal ref | Rhythm |
|---|---|---|
| `WORKFLOW_KUT_Mobilisation.json` | §4.1 | Once at kick-off — params, worksets, filters, BEP, CDE register |
| `WORKFLOW_KUT_CoordinationCycle.json` | §4.2 | Fortnightly — federate, clash, BCF→ACC Issues, model health, completeness |
| `WORKFLOW_KUT_DeliverableD.json` | §4.4 | Close-out — LOD 400 verify/stamp, SpecLink reconcile, audit, sign-off |
| `WORKFLOW_KUT_MonthlyReport.json` | §4.6 | Monthly — read-only KPI chain for the status report |

Run from **STING panel → Workflows**, or `WorkflowPreset`.

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
compliance 40% · clash 25% · warnings 20% · stale 15%.

---

## 4. Integration alignment (confirmed decisions)

- **Clash in ACC *and* STING.** ACC Model Coordination is the system of record;
  the Coordination Cycle runs STING's rule-based clash (discipline tolerance /
  access / maintenance-space checks ACC can't express) and pushes results as
  **BCF 2.1 → ACC Issues**. *Open item:* an ACC Model-Coordination **read**
  client (ingest ACC clash results for triage) — small follow-on, reuses the
  existing `AccIssueSync` OAuth.
- **Fohlio = link, never duplicate.** Stay on the shipped CSV/XLSX link
  (`ExLink Fohlio_Import`, key `FOHLIO_REF_TXT`). The REST tier stays stubbed —
  no API key needed for this contract (see §6 of the chat advisory).
- **SpecLink / Niagara / Bluebeam / Teams.** Coordination-only; STING archives
  issued spec sets, reconciles the SpecLink TOC, imports Bluebeam comments, and
  feeds Teams/Bluebeam — no integration build required.
- **Speckle.** Not in the Owner-mandated environment — keep internal-only;
  do **not** introduce as a deliverable or competing model home.

---

## 5. Deployment checklist

1. Copy `_BIM_COORD/` into the temple project folder.
2. Set `PRJ_ORG_PROJECT_CODE_TXT = KUT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` on
   Project Information (drives the sheet pattern + tag scheme).
3. Confirm the LOC→volume map and the originator/volume/level/type number table
   against the Owner's week-1 BEP register; edit `_BIM_COORD/*.json` to match.
4. Run **KUT Mobilisation** once on the federation host.
5. Run **KUT Coordination Cycle** fortnightly; **KUT Monthly Report** monthly;
   **KUT Gate Audit** + **KUT Deliverable D** at the contractual gates.

> Built without `dotnet build` verification (Linux sandbox). The JSON conforms
> to the existing registry schemas and every workflow `commandTag` resolves in
> `WorkflowEngine.ResolveCommand`; verify the workflow run end-to-end in Revit
> before the engagement relies on it.
