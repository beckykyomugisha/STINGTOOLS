# KUT — Revit smoke-test checklist (Phase 192)

Ordered manual checklist for the BIM Manager's first Revit session after
deploying the Phase 192 build. Every item below built clean but was
**not** exercised in Revit (Linux/sandbox authoring) — walk this list on
a sample KUT model before merging to `main`. One line per check with the
expected outcome.

## 0. Setup

1. Deploy the build (DLL + `data/` + `StingTools.addin`); start Revit 2025; open a KUT model with rooms placed. → STING dock panel loads, no startup errors in `StingTools.log`.
2. Copy `docs/examples/KUT/{project_config.json, tag_schemes.json, climate_data.json}` into `<project>/_BIM_COORD/`. On Project Information set `PRJ_ORG_PROJECT_CODE_TXT = KUT` + originator + address containing "Kampala". → files in place.

## 1. Parameters + tag scheme (Part A)

3. **Load Params** (TAGS · Setup). → binds without error; verify `ASS_TAG_SCHEME_TXT`, `ASS_LOD_VERIFIED_TXT`, `CSI_SECTION_TXT`, `CSI_TITLE_TXT`, `FOHLIO_REF_TXT`, `LTG_HOIST_WEIGHT_KG/_MOTOR_TXT/_DROP_MM` all appear in Manage → Shared Parameters.
4. **Scheme Inspect** (`TagScheme_Inspect`, TAGS · SCHEME TAGS). → `kut-temple-example` shows ● enabled + valid.
5. **Batch Tag** a sample area. → elements get `ASS_TAG_1_TXT`; new tags get a scheme string automatically.
6. **Render Scheme** (`TagScheme_Render`). → back-fills scheme strings on existing tags; stamp updates.
7. **Scheme Audit** (`TagScheme_Audit`). → reports 0 mismatches after render.
8. **Token Confidence** (`TokenConfidenceAudit`, TAGS · QA). → TaskDialog shows High/Medium/Low bands + silent-BLD1 count; CSV `STING_TokenConfidence_Audit.csv` written. Confirm a known site element with a `STING-LOC::BLD2` scope box reads `LOC_SOURCE=ScopeBox` (High).

## 2. LOD + Owner-standards gates (Part B)

9. **LOD Verify** (`LOD_Verify`, BIM · LOD VERIFICATION) → pick "Deliverable B". → TaskDialog %, CSV `STING_LOD_deliverable-b_Audit.csv`, JSON in `_BIM_COORD/lod_reports/`.
10. **LOD Stamp** (`LOD_Stamp`) → same milestone. → passing elements get `ASS_LOD_VERIFIED_TXT = deliverable-b`.
11. **Program Audit** (`Program_Audit`) → pick `Tests/fixtures/kut/program_template_sample.xlsx`. → summary (compliant/over/under/missing/extra); XLSX `STING_ProgramAudit_<date>.xlsx` with a Status column.
12. **Owner Standards** (`OwnerStandards_Audit`). → RAG summary; CSV + JSON in `_BIM_COORD/owner_standards_reports/`. Confirm the workset-prefix + required-PRJ_ORG rules fire as expected.
13. **Devices** (`DeviceCoord_Audit`, BIM · SPATIAL VALIDATION). → per-room findings; CSV `STING_DeviceCoord_Audit_<date>.csv`. Sanity-check a deliberately mis-placed switch behind a door swing is flagged.

## 3. Platform round-trips (Part C)

14. **CSI Assign** (`CSI_Assign`, BIM · CSI / SPECLINK) → "Fill empty only". → `CSI_SECTION_TXT` / `CSI_TITLE_TXT` written; unmapped-category list reported.
15. **SpecLink Reconcile** (`SpecLink_Reconcile`) → pick `Tests/fixtures/kut/speclink_toc_sample.csv`. → spec-gap / over-spec / title-mismatch counts; XLSX report.
16. **Fohlio Export** (`Fohlio_Export`, BIM · FOHLIO FF&E). → FF&E CSV `STING_Fohlio_Export_<date>.csv` with the mapped columns.
17. **Fohlio Import** (`Fohlio_Import`) → re-pick that CSV (or an edited copy with a `Fohlio Ref` column). → preview/diff dialog before any write; on Apply, `FOHLIO_REF_TXT` + fields written; ES snapshot stored.
18. **Fohlio Audit** (`Fohlio_Audit`). → linked % + missing-ref + stale counts per category. Edit a model value, re-run → that row reads stale.
19. **Review Comments Import** (`ReviewComments_Import`, BIM · REVIEW COMMENTS) → pick `Tests/fixtures/kut/bluebeam_comments_sample.csv`, gate "Deliverable B". → upserts into `_BIM_COORD/review_comments.json`; close-out rate shown.
20. **Review Dashboard** (`ReviewComments_Dashboard`) then **KPI Export** (`ReviewComments_Export`). → grid of comments; KPI CSV with per-gate close-out %.

## 4. US standards + engineering (Parts D, E)

21. **ComCheck export** (`Lite_ComCheck`, ELECTRICAL panel · LPD card). → per-space CSV `STING_ComCheck_Lighting_<date>.csv` with allowed-vs-proposed summary; dialog states the paste-into-COMcheck workflow.
22. **Life-cycle cost** (`Hvac_LifeCycleCompare`, HVAC panel · RPRT). → year-by-year XLSX `STING_HVAC_LCC_<date>.xlsx` (Summary + per-option sheets, nominal + NPV); crossover year reported.
23. **Prototype Drift** (`PrototypeDrift_Report`, BIM · CARBON and CHANGE TRACKING) → load the prototype as a link or open it, then pick it. → type-level diff XLSX grouped by discipline.
24. **LPS Full Report** (LPS panel) with a US-context address. → report includes the EN 62305-vs-NFPA 780 INFO note.
25. **Gate Audit workflow** (run `WORKFLOW_GateAudit.json`). → chains ValidateTags → TokenConfidenceAudit → LOD_Verify → CompletenessDashboard → TagScheme_Audit without an unknown-tag error.

## 5. Schedules + seeds

26. Create the **MEP Lighting Schedule** (Temp · Schedules). → the Hoist Load / Hoist Motor / Hoist Drop columns appear.
27. **Build Seeds** (Symbols) on a scratch project. → `STING_SEED_BaptismalFont` builds with DCW/DHW/SAN + recirc connectors.

> Log any failure with the command, the `StingTools.log` excerpt, and the
> model context. Until this list is green, keep the standard "verify in
> Revit before merge" caveat on the Phase 192 CHANGELOG blocks.
