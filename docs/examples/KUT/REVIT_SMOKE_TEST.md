# KUT — Revit smoke-test checklist

> **Generated file — do not edit.** Source:
> [`smoke_test.json`](smoke_test.json) · regenerate with
> `python tools/build_smoke_test.py` · gated by `tools/check_smoke_test.py`.

Ordered manual checklist for the BIM Manager's first Revit session on the Kampala Uganda Temple pack. Every command below builds clean and its wiring is machine-checked (`python tools/check_smoke_test.py`), but **none of it has been exercised against a real model** — walk this list on a sample KUT model before the engagement relies on it.

Steps marked **pre-cleared offline** have had their contract asserted by the CI gate: the command tag resolves, the button is really on the declared panel/tab/section with that label, the fixture exists, and the parameters named in the expected outcome are registered and bound. A failure on one of those in Revit is a genuine surprise, not a stale checklist.

What the gate cannot prove is everything about real geometry — whether the tags are *right*, whether an LOD verdict is *fair*, whether the mis-placed switch is actually caught. That judgement is what this session is for.

## Setup

1. **Deploy the build and open a KUT model with rooms placed** — _Revit-native action — no STING command_
   - Expected: STING dock panel loads; no startup errors in StingTools.log
   - Note: DLL + data/ + StingTools.addin, Revit 2025. Confirm where Revit actually loads from — grep the live .addin's <Assembly> path; copying into a folder the manifest does not name fails silently.

2. **Copy the whole KUT overlay pack into <project>/_BIM_COORD/** — _Revit-native action — no STING command_
   - Expected: All seven overlay files in place: manifest.json, project_config.json, tag_schemes.json, lod_matrix.json, owner_standards.json, fohlio_map.json, sting_classification.json (+ climate_data.json only if the corporate Kampala entry is being replaced)
   - Fixture: `project-templates/KUT/_BIM_COORD`
   - Note: Copy the FOLDER, not a selection. This step used to name three files from docs/examples/KUT/, so owner_standards.json, lod_matrix.json and fohlio_map.json never arrived and steps 12, 13 and 17-19 silently exercised the corporate baseline. Then on Project Information set PRJ_ORG_PROJECT_CODE_TXT = KUT, the originator code, an address containing "Kampala", and PLM_PRJ_PLUMBING_CODE_TXT = IPC-US (blank falls back to BS EN 12056 with no error).

## Parameters + tag scheme (Part A)

3. **Load Params** — **Load Params** (STING panel · CREATE TAGS · ⚙ SETUP) _(pre-cleared offline)_
   - Expected: Binds without error. In Manage → Shared Parameters confirm ASS_TAG_SCHEME_TXT, ASS_LOD_VERIFIED_TXT, CSI_SECTION_TXT, CSI_TITLE_TXT and FOHLIO_REF_TXT are bound to all categories, and that LTG_HOIST_WEIGHT_KG, LTG_HOIST_MOTOR_TXT and LTG_HOIST_DROP_MM are bound to Lighting Fixtures ONLY — not Generic Models
   - Command tag: `LoadSharedParams`
   - Note: The Lighting-Fixtures-only scope is the correct one: PARAMETER_REGISTRY.json declares binding "LightingFixtures" and RESOLVED_BINDINGS.csv (the domain-derived source of truth SharedParamGuids reads) lists Lighting Fixtures alone. The sibling LTG_FIX_* params are "universal" and DO also reach Generic Models — do not use them as the pattern. Step 27 is the live test of this binding.

4. **Scheme Inspect** — **Scheme Inspect** (STING panel · CREATE TAGS · ⚙ SCHEME TAGS) _(pre-cleared offline)_
   - Expected: kut-temple-example shows ● enabled and valid
   - Command tag: `TagScheme_Inspect`
   - Depends on: step(s) 2, 3

5. **Batch Tag a sample area** — **Batch Tag** (STING panel · TAGGING · DATA TAGGING (ISO 19650)) _(pre-cleared offline)_
   - Expected: Elements get ASS_TAG_1_TXT; new tags get a scheme string in ASS_TAG_SCHEME_TXT automatically
   - Command tag: `BatchTag`
   - Depends on: step(s) 4

6. **Render Scheme** — **Render Scheme** (STING panel · CREATE TAGS · ⚙ SCHEME TAGS) _(pre-cleared offline)_
   - Expected: Back-fills ASS_TAG_SCHEME_TXT on already-tagged elements; the render stamp updates
   - Command tag: `TagScheme_Render`
   - Depends on: step(s) 5

7. **Scheme Audit** — **Scheme Audit** (STING panel · CREATE TAGS · ⚙ SCHEME TAGS) _(pre-cleared offline)_
   - Expected: 0 mismatches after the render
   - Command tag: `TagScheme_Audit`
   - Depends on: step(s) 6

8. **Token Confidence audit** — **Token Conf** (STING panel · CREATE TAGS · ⚙ QUALITY ASSURANCE) _(pre-cleared offline)_
   - Expected: High/Medium/Low bands plus a silent-BLD1 count; ASS_LOC_SOURCE_TXT and ASS_ZONE_SOURCE_TXT populated
   - Command tag: `TokenConfidenceAudit`
   - Artefact: `STING_TokenConfidence_Audit.csv`
   - Depends on: step(s) 5
   - Note: Confirm a site element inside a STING-LOC::BLD2 scope box reads LOC_SOURCE=ScopeBox (High). Draw STING-LOC scope boxes UNROTATED — STING stores axis-aligned plan extents, so a rotated box is treated as its larger envelope. Where boxes nest, the smallest containing box wins.

## LOD + Owner-standards gates (Part B)

9. **LOD Verify at Deliverable B** — **LOD Verify** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: TaskDialog states LOD 300; CSV STING_LOD_deliverable-b_Audit.csv and a JSON gate report in _BIM_COORD/lod_reports/
   - Command tag: `LOD_Verify`
   - Artefact: `STING_LOD_deliverable-b_Audit.csv`
   - Depends on: step(s) 2, 5
   - Note: Check the SKIPPED line. If it reports elements skipped for having no category rule and no "*" fallback, those are outside the denominator — a coverage gap, not a pass. A run with nothing in scope now says "NO ELEMENTS IN SCOPE", never 100%.

10. **LOD Verify at Construction (LOD 400)** — **LOD Verify** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: Pick "Construction stage (fabrication/installation)"; the report states LOD 400. Plumbing Fixtures require MNT_TYPE_TXT at this rung
   - Command tag: `LOD_Verify`
   - Artefact: `STING_LOD_construction_Audit.csv`
   - Depends on: step(s) 9
   - Note: The construction milestone at LOD 400 was inserted by #623 and no step has ever exercised it. The MNT_TYPE_TXT requirement is new to KUT: the project lod_matrix.json overlay used to restate the Plumbing Fixtures rule minus that line, which silently relaxed the gate; the overlay no longer pins any category.

11. **LOD Verify at Deliverable D (LOD 500)** — **LOD Verify** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: Pick "Deliverable D (record / as-built model)"; the report states LOD 500. Serialised plant requires ASS_SERIAL_NR_TXT and ASS_INSTALLATION_DATE_TXT; fabric and distribution categories inherit 400 and add nothing
   - Command tag: `LOD_Verify`
   - Artefact: `STING_LOD_deliverable-d_Audit.csv`
   - Depends on: step(s) 9
   - Note: Deliverable D moved 400 → 500 in #623 and rung 500 was made category-dependent in #635. This is the newest, least-exercised data in the pack — expect fabric to pass and serialised plant to fail on an early model, and confirm that asymmetry rather than a blanket failure.

12. **LOD Stamp at Deliverable B** — **LOD Stamp** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: Passing elements get ASS_LOD_VERIFIED_TXT = deliverable-b. This is the only LOD command that writes, and it writes exactly this one parameter
   - Command tag: `LOD_Stamp`
   - Depends on: step(s) 9

13. **Program Audit against the A1 brief** — **Program Audit** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: Compliant / over / under / missing / extra summary; XLSX with a Status column
   - Command tag: `Program_Audit`
   - Fixture: `Tests/fixtures/kut/program_template_sample.xlsx`
   - Artefact: `STING_ProgramAudit_<date>.xlsx`

14. **Owner Standards audit** — **Owner Standards** (STING panel · BIM · LOD VERIFICATION) _(pre-cleared offline)_
   - Expected: RAG summary; CSV + JSON in _BIM_COORD/owner_standards_reports/. The KUT sheet-number rule, the narrowed discipline taxonomy and the ffe-fohlio-ref check (WARN, non-blocking) must all appear — if they do not, the overlay from step 2 did not arrive
   - Command tag: `OwnerStandards_Audit`
   - Depends on: step(s) 2

15. **Device coordination audit** — **Devices** (STING panel · BIM · SPATIAL VALIDATION) _(pre-cleared offline)_
   - Expected: Per-room findings; CSV. Sanity-check that a deliberately mis-placed switch behind a door swing is flagged
   - Command tag: `DeviceCoord_Audit`
   - Artefact: `STING_DeviceCoord_Audit_<date>.csv`

## Platform round-trips (Part C)

16. **CSI Assign — fill empty only** — **CSI Assign** (STING panel · BIM · CSI / SPECLINK) _(pre-cleared offline)_
   - Expected: CSI_SECTION_TXT and CSI_TITLE_TXT written; unmapped-category list reported
   - Command tag: `CSI_Assign`
   - Depends on: step(s) 2
   - Note: CSI_Assign cannot classify demolition — CsiMasterFormat.Resolve never sees Phase Demolished, so Division 02 stays empty. Assign it by hand BEFORE step 17, or the Owner's Division 02 spec sections report as over-specification and the reconciliation reads clean when it is not.

17. **SpecLink Reconcile** — **SpecLink Reconcile** (STING panel · BIM · CSI / SPECLINK) _(pre-cleared offline)_
   - Expected: Spec-gap / over-spec / title-mismatch counts; XLSX report
   - Command tag: `SpecLink_Reconcile`
   - Fixture: `Tests/fixtures/kut/speclink_toc_sample.csv`
   - Depends on: step(s) 16

18. **Fohlio Export** — **Fohlio Export** (STING panel · BIM · FOHLIO FF&E) _(pre-cleared offline)_
   - Expected: FF&E CSV with the columns declared in _BIM_COORD/fohlio_map.json
   - Command tag: `Fohlio_Export`
   - Artefact: `STING_Fohlio_Export_<date>.csv`
   - Depends on: step(s) 2

19. **Fohlio Import** — **Fohlio Import** (STING panel · BIM · FOHLIO FF&E) _(pre-cleared offline)_
   - Expected: Preview/diff dialog before any write; on Apply, FOHLIO_REF_TXT and the mapped fields are written and an ES snapshot is stored
   - Command tag: `Fohlio_Import`
   - Depends on: step(s) 18
   - Note: Re-pick the CSV from step 18, or an edited copy carrying a Fohlio Ref column.

20. **Fohlio Audit** — **Fohlio Audit** (STING panel · BIM · FOHLIO FF&E) _(pre-cleared offline)_
   - Expected: Linked % plus missing-ref and stale counts per category. Edit a model value and re-run — that row must read stale
   - Command tag: `Fohlio_Audit`
   - Depends on: step(s) 19

21. **Review Comments import (Bluebeam)** — **Import** (STING panel · BIM · REVIEW COMMENTS (BLUEBEAM)) _(pre-cleared offline)_
   - Expected: Upserts into _BIM_COORD/review_comments.json against gate "Deliverable B"; close-out rate shown
   - Command tag: `ReviewComments_Import`
   - Fixture: `Tests/fixtures/kut/bluebeam_comments_sample.csv`

22. **Review dashboard, then KPI export** — **Dashboard** (STING panel · BIM · REVIEW COMMENTS (BLUEBEAM)) _(pre-cleared offline)_
   - Expected: Grid of comments; then "KPI Export" writes a CSV with per-gate close-out %
   - Command tag: `ReviewComments_Dashboard`
   - Depends on: step(s) 21

## US standards + engineering (Parts D, E)

23. **ComCheck export** — **▶ ComCheck export** (ELECTRICAL panel · LITE · Custom limit (W/m²)) _(pre-cleared offline)_
   - Expected: Per-space CSV with an allowed-vs-proposed summary; the dialog states the paste-into-COMcheck workflow
   - Command tag: `Lite_ComCheck`
   - Artefact: `STING_ComCheck_Lighting_<date>.csv`
   - Note: On the ELECTRICAL panel, not the main one. Its buttons were ungated by the wiring check until Tier 4 was widened to all six panels.

24. **HVAC life-cycle cost comparison** — **Life-cycle cost** (HVAC panel · RPRT · WORKFLOW RUNS) _(pre-cleared offline)_
   - Expected: Year-by-year XLSX (Summary + per-option sheets, nominal + NPV); crossover year reported
   - Command tag: `Hvac_LifeCycleCompare`
   - Artefact: `STING_HVAC_LCC_<date>.xlsx`

25. **Prototype drift report** — **Prototype Drift** (STING panel · BIM · CARBON and CHANGE TRACKING) _(pre-cleared offline, optional)_
   - Expected: Type-level diff XLSX grouped by discipline
   - Command tag: `PrototypeDrift_Report`
   - Note: Needs the Owner's prototype model, loaded as a link or opened, then picked.

26. **ACC pull clashes** — **ACC Pull** (STING panel · BIM · COORDINATION CHECKS) _(pre-cleared offline, optional)_
   - Expected: Model Coordination clashes pulled and ranked; CSV written; top-ranked escalated to ACC Issues idempotently via the _BIM_COORD/acc/pushed_clashes.json sidecar
   - Command tag: `ACC_PullClashes`
   - Note: Needs %APPDATA%/Planscape/acc_credentials.json. Endpoint paths are verified against the public APS sample but not against a live tenant — this is the live pull to do during mobilisation. The command existed and was step 3 of the Coordination Cycle preset, but had no button of its own until now.

## Schedules + seeds

27. **Create the MEP Lighting Schedule** — _Revit-native action — no STING command_ _(pre-cleared offline)_
   - Expected: The Hoist Load (kg) / Hoist Motor / Hoist Drop (mm) columns appear, driven by LTG_HOIST_WEIGHT_KG, LTG_HOIST_MOTOR_TXT and LTG_HOIST_DROP_MM
   - Depends on: step(s) 3
   - Note: Revit-native schedule creation from the STING schedule definitions (MR_SCHEDULES.csv row "Lighting Schedule"). This is the live test of step 3's binding claim: the columns can only appear because those three parameters bind to Lighting Fixtures.

28. **Build Seeds on a scratch project** — **★ Build Seeds** (STING panel · SETUP · SYMBOLS & DEVICES) _(pre-cleared offline)_
   - Expected: All 16 seed families build. In STING_SEED_PlumbingFixture, the STING_SEED_BaptismalFont symbol builds with DCW + DHW supply, a SAN drain, and a recirculation supply/return pair carrying the DomesticHot systemType
   - Command tag: `Seeds_Build`
   - Note: There is no STING_SEED_BaptismalFont.json — the font is a symbol INSIDE Data/Seeds/STING_SEED_PlumbingFixture.json (Phase 192 E4). The old checklist also told you to press a "Build Seeds" button that did not exist: Seeds_Build was reachable only from inside WORKFLOW_{PenetrationSweep, PenetrationRegister, PlumbingRoughIn, ElectricalRoughIn, SLDProduction}. The button now exists, which is why this step reads "button" and not "workflow".

## Workflow gates

29. **KUT Gate Audit — the read-only pre-gate check** — run **WORKFLOW_KUT_GateAudit.json** (workflow only — no standalone button) _(pre-cleared offline)_
   - Expected: Eight steps chain without an unknown-tag error: PreTagAudit → TokenConfidenceAudit → OwnerStandards_Audit → Program_Audit → DeviceCoord_Audit → LOD_Verify → ExportModelHealth → FullComplianceDashboard. NOTHING is written to the model
   - Command tag: `WorkflowPreset`
   - Depends on: step(s) 9, 13, 14, 15
   - Note: Replaces the old step that ran WORKFLOW_GateAudit.json, which has been deleted: it contained ValidateTags and CompletenessDashboard, both [Transaction(TransactionMode.Manual)] writers that build legends, so it was not the read-only check its description claimed. The checker now proves the read-only claim for every preset that makes it.

30. **KUT Deliverable A gate (LOD 200)** — run **WORKFLOW_KUT_DeliverableA.json** (workflow only — no standalone button) _(pre-cleared offline)_
   - Expected: The A gate runs end to end: tokens, tags, program audit; LOD verification at deliverable-a
   - Command tag: `WorkflowPreset`
   - Depends on: step(s) 29
   - Note: The three Deliverable gate presets (#638) had no smoke-test coverage at all before this.

31. **KUT Deliverable B gate (LOD 300)** — run **WORKFLOW_KUT_DeliverableB.json** (workflow only — no standalone button) _(pre-cleared offline)_
   - Expected: The fullest gate runs: program + owner-standards + CSI + device coordination + Fohlio finishes + clash, then LOD verification at deliverable-b
   - Command tag: `WorkflowPreset`
   - Depends on: step(s) 29
   - Note: Confirm CSI_Assign runs BEFORE SpecLink_Reconcile — reversed, every spec section reports as a gap and the report is worthless.

32. **KUT Deliverable C gate (LOD 350)** — run **WORKFLOW_KUT_DeliverableC.json** (workflow only — no standalone button) _(pre-cleared offline)_
   - Expected: Adds CSI → SpecLink reconcile and the sheet register for the bidding set; LOD verification at deliverable-c
   - Command tag: `WorkflowPreset`
   - Depends on: step(s) 29

33. **Owner KPI dashboard** — **KPI Dashboard** (STING panel · BIM · OWNER KPI DASHBOARD) _(pre-cleared offline)_
   - Expected: RAG bars, per-discipline table and clash burn-down; HTML + CSV written; a snapshot appended to _BIM_COORD/kpi/KUT_kpi_log.jsonl
   - Command tag: `Owner_KpiDashboard`
   - Depends on: step(s) 2
   - Note: Was KUT_KpiDashboard, which still resolves as an alias. The code comes from PRJ_ORG_PROJECT_CODE_TXT, so with step 2 done the log is KUT_kpi_log.jsonl; an existing kut_kpi_log.jsonl is read and appended to rather than orphaned.

Log any failure with the command, the `StingTools.log` excerpt, and the model context.

**This file is generated.** Edit `docs/examples/KUT/smoke_test.json` and run `python tools/build_smoke_test.py`; `tools/check_smoke_test.py` fails CI if the two disagree. See [`docs/examples/_smoke_test_schema.md`](../_smoke_test_schema.md).
