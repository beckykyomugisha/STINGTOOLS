# Documentation Index

133 documents live in `docs/`, plus 23 at the repository root. This is the table of contents.

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
`TOKEN_DEPTH_LIVE_ENHANCEMENTS_RUNNER.md` · `tagstudio-family-stage-tests.md`

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

`SMOKE_TEST_ConduitSleeve.md` · `SMOKE_TEST_PM_COMPLETE.md` · `ELECTRICAL_SMOKETEST_CHECKLIST.md` ·
`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` · `PR306_MANUAL_QA.md` · `GOLD_CONSOLIDATED_SWEEP_CHECKLIST.md`

## Domain packs

`HEALTHCARE_PACK_DESIGN.md` · `PROMPT_KUT_PHASE_192_IMPLEMENTATION.md`
