# Documentation Index

133 documents live in `docs/`, plus 23 at the repository root. This is the table of contents.
Issued project documents that live outside `docs/` are indexed here too — see
*KUT mobilisation pack* below.

**How to read a doc's status.** Most files here are point-in-time artefacts, not living
specification. Before acting on one, check its date and whether it says *plan/prompt/proposal*
(intent — may never have shipped) or *guide/runbook* (describes shipped behaviour). Where several
documents cover one topic, the **current** one is marked ✅ and the superseded ones ⛔ below.

## Start here

| Doc | What it is |
|---|---|
| [`../CLAUDE.md`](../CLAUDE.md) | Architecture, command catalogue, conventions, and the dated codebase review. The onboarding surface. |
| [`CHANGELOG.md`](CHANGELOG.md) | Phase-by-phase history. Append completed work here. |
| [`ROADMAP.md`](ROADMAP.md) | Open gaps and future work. The living backlog. |
| [`SYSTEM_STATUS.md`](SYSTEM_STATUS.md) | What has been runtime-verified vs. written-but-unrun. |
| [`TESTING_GUIDE.md`](TESTING_GUIDE.md) | How to test the plugin and server. |

## Folder structure & ISO 19650 consolidation

| Doc | Status |
|---|---|
| [`FOLDER_STRUCTURE_REVIEW_2026-08.md`](FOLDER_STRUCTURE_REVIEW_2026-08.md) | ✅ **Current.** Aug 2026 re-measure; drove the folder fixes. The contract itself is in `CLAUDE.md` → *Project Output Folder Layout*. |
| [`ISO19650_DOC_FOLDER_REVIEW.md`](ISO19650_DOC_FOLDER_REVIEW.md) | ⛔ Superseded (Jul 2026). Historical: about half its findings are fixed. |
| [`AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md`](AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md) | ⛔ Superseded. Work prompt for the consolidation, now landed. |
| [`ISO19650_INREVIT_VERIFICATION.md`](ISO19650_INREVIT_VERIFICATION.md) | In-Revit verification checklist for the consolidation. |
| [`CONSOLIDATION_PROGRESS.md`](CONSOLIDATION_PROGRESS.md) · [`BRANCH_CONSOLIDATION_LOG.md`](BRANCH_CONSOLIDATION_LOG.md) | Historical progress logs. |

## Deployment & operations

`DEPLOYMENT.md` · `DEPLOY_RUNBOOK.md` (✅ authoritative for Render sizing/connection budget) ·
`SERVER_GO_LIVE.md` · `CI_GREEN_BASELINE.md` · `EF_SCHEMA_WORKFLOW.md` · `PUSH_FIREBASE.md` ·
`EMAIL_RESEND.md` · `PHOTO_WORKFLOW_DEPLOY.md` · `PLANSCAPE_DEPLOYMENT_REACHABILITY.md` ·
`PLANSCAPE_CONNECT_TROUBLESHOOTING.md` · `PLANSCAPE_IDENTITY_HANDOFF.md` · `PLANSCAPE_PROTOCOL.md` ·
`PLANSCAPE_ALIGNMENT_AUDIT.md` · `PRE_MERGE_CHECKLIST.md`

## User guides (describe shipped behaviour)

`BIM_MANAGEMENT_GUIDE.md` · `DOCUMENT_MANAGER_GUIDE.md` · `TAGGING_WORKFLOW_GUIDE.md` ·
`TAGGING_PROCEDURES_GUIDE.md` · `TAG_CREATION_GUIDE.md` · `TEMPLATE_MANAGER_USER_GUIDE.md` ·
`PLACEMENT_CENTRE_GUIDE.md` · `MATERIAL_HUB_USER_GUIDE.md` · `HEALTHCARE_USER_GUIDE.md` ·
`STING_ELECTRICAL_LAYMANS_GUIDE.md` · `LPS_USER_GUIDE.md` · `BOQ_QS_LAYMANS_GUIDE.md` ·
`DRAWINGS_PRODUCTION_LAYMANS_GUIDE.md` · `PENETRATION_WORKFLOW_GUIDE.md` · `MEP_SYMBOL_GUIDE.md` ·
`WIRE_ANNOTATION_GUIDE.md` · `IDS_AUTHORING_GUIDE.md` · `bcc-guide.md` ·
`UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` · `UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md`

## Drawings, templates & title blocks

`AEC_FILTER_LIBRARY.md` · `AEC_PRODUCTION_SET_STRATEGY.md` · `STING_MANAGED_TEMPLATES_DESIGN.md` ·
`DRAWINGS_PRODUCTION_REVIEW.md` · `DRAWING_VG_RESEARCH.md` · `MEP_DRAWING_TYPES_PRINT_READY_RUNNER.md` ·
`TITLE_BLOCK_FAMILY_DESIGN.md` · `TITLE_BLOCK_FAMILY_INVENTORY.md` · `TITLE_BLOCK_GENERATOR_RESEARCH.md` ·
`ISO_ANNOTATION_SYMBOLS_PLAN.md` · `ISO_ANNOTATION_SYMBOLS_REVIEW.md` · `US_STANDARDS_PRESET.md`

## BOQ, cost & sustainability

`BOQ_ACCURACY_AUDIT.md` · `BOQ_QS_GAPS_PROMPT.md` · `COST_MANAGEMENT_IMPLEMENTATION_PLAN.md` ·
`VIEWER_COST_DATAFLOW.md` · `SUSTAINABILITY_WORKFLOW_HARDENING_PROMPT.md` ·
`BOQ_5D_*.md` / `BOQ_COST_MANAGER_5D_WORKSPACE_PROMPT.md` / `BOQ_INLINE_ACTIONS_SLICE3_PROMPT.md` /
`BOQ_REVIEW_AND_HARDENING_PROMPT.md` (work prompts — intent, check against `CHANGELOG.md`)

## Placement, symbols & families

`PLACEMENT_CENTRE_REVIEW.md` · `PLACEMENT_FAMILY_AUTHORING.md` · `PLACEMENT_SEED_VARIANT_COVERAGE.md` ·
`PLACEMENT_SEEDS_SWAP_AND_ALGORITHM_DESIGN.md` · `PLACEMENT_REAL_GEOMETRY_AND_HOST_FIRST_ADVISORY.md` ·
`SYMBOL_GAP_AUDIT.md` · `SYMBOL_LIBRARY_REUSE_SCOPE.md` · `SYMBOL_CACHE_TEST_PLAN.md` ·
`SLD_SYMBOL_WORKFLOW_AND_GAPS.md` · plus `PLACEMENT_*_PROMPT.md` work prompts

## Interop & multi-host

`MULTI_HOST_INTEGRATION_PLAN.md` · `CROSS_HOST_ROUND_TRIP_RUNBOOK.md` ·
`CROSS_HOST_VALIDATION_CHECKLIST.md` · `PHASE_186_BONSAI_INTEGRATION.md` ·
`PHASE_186_VERIFICATION_CHECKLIST.md` · `MVP_SCOPE_BONSAI.md` · `EXPORTER_TEXTURES.md` ·
`MCP_V2_CAPABILITY_EXPOSURE.md`

## Parameters & tagging internals

`PARAMETER_DUPLICATES.md` · `PARAM_BINDING_DOMAIN_MAP.md` · `CATEGORY_BINDING_COVERAGE_REPORT.md` ·
`TAGGING_WORKFLOW_ANALYSIS.md` · `UNIVERSAL_TAG_*.md` (build sheets and runners) ·
`tagstudio-family-stage-tests.md`

*(`TOKEN_DEPTH_LIVE_ENHANCEMENTS_RUNNER.md` retired — E1–E5 all shipped; see the
Token-Depth Live entry in [`CHANGELOG.md`](CHANGELOG.md).)*

## Audits, findings & triage

`UNREACHABLE_COMMANDS_TRIAGE.md` · `GAP_ANALYSIS_FINDINGS.md` · `COORDINATION_AUDIT_FINDINGS.md` ·
`MEETINGS_AUDIT.md` · `VISUALIZE_AUDIT.md` · `PHASE_Z_AUDITS.md` · `PHASE_Z_NUMERIC_FOLLOWUPS.md` ·
`LIVEKIT_AND_CORPORATE_UI_FINDINGS.md` · `PHASE6_CLIENTLESS_CONTROLLER_TRIAGE.md` · `VERIFIED.md`

## Redesign plans (intent — verify against the code before trusting)

`BIM_REDESIGN_PLAN.md` · `DOCS_REDESIGN_PLAN.md` · `CREATE_TAGS_REDESIGN_PLAN.md` ·
`INTEROP_REDESIGN_PLAN.md` · `MODEL_REDESIGN_PLAN.md` · `SETUP_REDESIGN_PLAN.md` ·
`TAGGING_REDESIGN_PLAN.md` · `TAGSTUDIO_REDESIGN_PLAN.md` · `UI_CLEANUP_CAMPAIGN.md` ·
`UI_PHASE_B_PATTERNS.md` · `ACC_UI_SHELL_GRID_CONTRACT.md` · `PLATFORM_ENHANCEMENT_PROPOSAL.md` ·
`DESIGN_MODELING_AUTOMATION_ROADMAP.md` · `RESEARCH_GANTT_AND_CLASH.md` · `MOBILE_DEFERRED_FEATURES.md`

## Smoke tests & QA

| Doc | Status |
|---|---|
| [`examples/_smoke_test_schema.md`](examples/_smoke_test_schema.md) | ✅ **Current.** The contract for a generated, CI-gated smoke-test checklist: what a step may declare, what `tools/check_smoke_test.py` proves, and — stated plainly — what it cannot (anything about real Revit geometry). Owner-agnostic; read this before adding an owner pack. |
| [`examples/KUT/README.md`](examples/KUT/README.md) | ✅ **Current.** What lives in the KUT example folder. The *deployable* overlay pack is `project-templates/KUT/_BIM_COORD/`, not here. |
| [`examples/KUT/REVIT_SMOKE_TEST.md`](examples/KUT/REVIT_SMOKE_TEST.md) | ✅ **Current — GENERATED, do not edit.** Rendered from `examples/KUT/smoke_test.json` by `tools/build_smoke_test.py`. Hand edits are reverted by the gate. |

The hand-maintained checklists below predate that pipeline. They are still the
only coverage for their areas, but none is gated — treat each as a point-in-time
artefact and check its date before trusting a step:

`SMOKE_TEST_ConduitSleeve.md` · `SMOKE_TEST_PM_COMPLETE.md` · `ELECTRICAL_SMOKETEST_CHECKLIST.md` ·
`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` · `PR306_MANUAL_QA.md` · `GOLD_CONSOLIDATED_SWEEP_CHECKLIST.md`

## KUT mobilisation pack (issued documents + their generators)

The pack the Kampala Uganda Temple project is being run from. Mobilisation began the week of
25 August 2026 and these documents are relied on now.

**Two rules govern every file in this section.** The three issued documents never name the
tooling — no product name, command, parameter or file path — because they are read by the client
and by every consultant, and the Appointing Party is entitled to require a check without being
told the instrument. And no generated file is ever hand-edited: edit the generator and
regenerate. Both rules are enforced by `tools/check_kut_documents.py`, so breaking either fails
CI rather than reaching an issue.

| Document | Status |
|---|---|
| [`../KUT_BIM_Execution_Plan.docx`](../KUT_BIM_Execution_Plan.docx) | ✅ **Issued Rev P01 — GENERATED, do not edit.** `KUT-PLN-ZZ-ZZ-RP-Z-0001`. What the project requires. Built by `tools/build_bep.py`. |
| [`../KUT_Project_Delivery_Playbook.docx`](../KUT_Project_Delivery_Playbook.docx) | ✅ **Issued Rev P01 — GENERATED, do not edit.** `KUT-PLN-ZZ-ZZ-RP-Z-0002`. How a task team satisfies the BEP; the document a consultant works from. Built by `tools/build_team_playbook.py`. |
| [`../KUT_Master_Information_Delivery_Plan.xlsx`](../KUT_Master_Information_Delivery_Plan.xlsx) | ✅ **Issued Rev P01 — GENERATED, do not edit.** `KUT-PLN-ZZ-ZZ-SC-Z-0001`. 68 deliverables, when each lands, and the TIDP return template. Built by `tools/build_midp.py`. |
| [`../KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx`](../KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx) | ✅ **Current — INTERNAL, hand-maintained.** The only document in the pack that may name the tooling, and the only one not generated. Never issued to the client or to a consultant. |
| [`../project-templates/KUT/_BIM_COORD/lod_matrix.json`](../project-templates/KUT/_BIM_COORD/lod_matrix.json) | ✅ **Current — GENERATED, do not edit.** The tiered LOD overlay the close-out gate runs against. Built by `tools/build_kut_lod_overlay.py`; `--check` proves it still matches the corporate baseline. |

| Generator / tool | What it does |
|---|---|
| `tools/build_bep.py` · `tools/build_team_playbook.py` · `tools/build_midp.py` | The three issued documents. Change content HERE, never in the `.docx` / `.xlsx`. |
| `tools/build_kut_lod_overlay.py` | The LOD overlay, and the tier definitions (A serialised plant · B maintainable devices · C warranted fabric · FF&E · D everything else). `--check` is CI-gated. |
| `tools/build_smoke_test.py` | The manual Revit smoke-test checklist, rendered from `examples/KUT/smoke_test.json`. |
| `tools/corporate_docx.py` | ✅ **The shared house style.** One definition of page setup, palette, headings, tables and callouts, so the issued set looks like one set. A change here changes every document at its next build. Also owns the deterministic, stamped save. |
| `tools/kut_docs_lib.py` | Determinism and the two staleness digests (`inputs-sha256` over the generators, `parts-sha256` over the document's own parts), plus stdlib readers for `.docx` / `.xlsx`. Stdlib-only so the gate runs on a bare runner. |
| `tools/midp_schema.py` | The MIDP/TIDP columns, permitted-value lists and drop-down column mapping. Shared by the builder and the merge tool so a return is validated against exactly what the workbook offered. |
| `tools/check_kut_documents.py` | ✅ **The gate.** Proves the pack is internally consistent, matches the LOD overlay, names no tooling, and is a current un-edited regeneration. Run by `.github/workflows/kut-document-gate.yml`. It proves nothing about whether the requirements are *right*, and nothing about a real Revit model. |
| `tools/merge_tidp.py` | Merges returned TIDP workbooks into the register. **Preview by default**; writes only on `--apply`; refuses a conflicting `Ref` unless told otherwise. |

## Domain packs

`HEALTHCARE_PACK_DESIGN.md` · `PROMPT_KUT_PHASE_192_IMPLEMENTATION.md` (⛔ historical — the `WORKFLOW_GateAudit.json` it specifies has been deleted; `WORKFLOW_KUT_GateAudit.json` is the gate-audit chain) · `PROMPT_KUT_SMOKE_TEST_RECONCILIATION.md`
