# ROADMAP — STINGTOOLS

Open automation gaps, future-enhancement tables, and deep-review findings for the StingTools plugin. See [`../CLAUDE.md`](../CLAUDE.md) for current architecture and [`CHANGELOG.md`](CHANGELOG.md) for the history of closed items.

## Sub-system reviews

- [`PLACEMENT_CENTRE_GUIDE.md`](PLACEMENT_CENTRE_GUIDE.md) — plain-English user guide to the Placement Centre: every button, every editor field, background concepts (anchors, regex, mounting reference, provenance, standards), worked walk-throughs, troubleshooting and a cheat-sheet (2026-04-25).
- [`PLACEMENT_CENTRE_REVIEW.md`](PLACEMENT_CENTRE_REVIEW.md) — flexibility / functionality / automation gap audit of the Placement Centre with PC-01..PC-25 backlog and a recommended ≈ 25-category baseline catalogue (2026-04-25).
- [`HEALTHCARE_PACK_DESIGN.md`](HEALTHCARE_PACK_DESIGN.md) — multi-phase design document for the Healthcare / Hospital Design pack covering HTM / HBN / FGI / NFPA 99 / NCRP 147 / ASHRAE 170 / ISO 14644 / USP 797-800 / SFG20-Healthcare integration. Defines ~140 new shared parameters, 60 filters, 16 drawing types, 4 ViewStylePacks, 8 validators, COBie-Healthcare overlay, RDS template engine, MGPS package, radiation calc, adjacency analyser, anti-ligature pack, behavioural-health pack, digital-twin / IoT bridge, mobile commissioning app and server APIs. Phased H-1..H-22 with file-by-file integration map (2026-05-08).
- [`HEALTHCARE_USER_GUIDE.md`](HEALTHCARE_USER_GUIDE.md) — plain-English layman's guide to using STINGTOOLS / Planscape on a healthcare facility. Covers every healthcare button on the dock panel + BCC, every mobile screen, every standard (HTM / HBN / FGI / NFPA / NCRP / ASHRAE / USP / iHFG / SFG20 / BS / IRR17), every parameter prefix, every workflow preset, every clinical room class, every term and acronym (ACH, AIIR, PE, MGPS, ZVB, AAP, TMV, EES, IPS, RTLS, etc.). Includes 5 step-by-step common workflows + troubleshooting + sign-offs table (2026-05-09).

## How to use this file

- Items are grouped by the review that surfaced them (Phase 74 5-agent review, Phase 76 DWG review, Phase 77 review, Phase 78 triage, etc.). The grouping is preserved so you can trace each gap back to its origin.
- Items marked `~~strikethrough~~` with `**DONE**` are completed — they stay here as a record of what the review covered. When closing a new item, either strike it through in place or move it to `CHANGELOG.md` under the appropriate phase.
- When adding a new gap, either extend an existing section's table or add a new `### Future Enhancement Gaps — <topic> (Phase N Review)` section at the end.

---

### Current Automation Gaps

#### A. Gaps That Hinder Full Automation

| Gap | Location | Problem | Impact |
|-----|----------|---------|--------|
| ~~**No tag collision detection**~~ | `TagConfig.cs` | **DONE** — `BuildAndWriteTag` accepts `existingTags` HashSet for O(1) collision detection; auto-increments SEQ on duplicate. `BuildExistingTagIndex()` builds the index once per batch. All callers updated. | Done |
| ~~**No progress reporting**~~ | `BatchTagCommand`, `MasterSetupCommand` | **DONE** — BatchTag shows element count upfront, logs every 500 elements, reports duration. MasterSetup reports per-step timing. | Done |
| ~~**No cancellation support**~~ | All batch commands | **DONE** — `StingProgressDialog` provides modeless progress window with Cancel button and Escape key detection. `EscapeChecker` utility for Win32 key state. `WorkflowEngine` checks cancellation between steps. | Done |
| ~~**Hardcoded category bindings**~~ | `SharedParamGuids.cs`, `ParamRegistry.cs` | **DONE** — Discipline bindings derived from `PARAMETER_REGISTRY.json` container_groups (data-driven). `CATEGORY_BINDINGS.csv` loaded by `TemplateManager.LoadCategoryBindings()` and used by `LoadSharedParamsCommand` Pass 2 to augment JSON bindings. `FAMILY_PARAMETER_BINDINGS.csv` loaded by `BatchAddFamilyParamsCommand`. | Done |
| ~~**No error recovery**~~ | `MasterSetupCommand.cs` | **DONE** — Wrapped in `TransactionGroup` for atomic rollback. If critical step 1 (Load Params) fails, user can rollback immediately. Per-step timing reported. | Done |
| ~~**Fixed tag format**~~ | `ParamRegistry.cs`, `TagConfig.cs` | **DONE** — Tag format (separator, num_pad, segment_order) loaded from `PARAMETER_REGISTRY.json`, with project-level overrides via `project_config.json` TAG_FORMAT section. `ConfigEditorCommand` displays and saves tag format settings. | Done |
| ~~**Partially unused data files**~~ | `Data/` directory | **DONE** — All data files now loaded: CATEGORY_BINDINGS.csv (LoadSharedParams Pass 2), FAMILY_PARAMETER_BINDINGS.csv (BatchAddFamilyParams), MATERIAL_SCHEMA.json (SchemaValidate), BINDING_COVERAGE_MATRIX.csv (DynamicBindings), VALIDAT_BIM_TEMPLATE.py (ported to ValidateTemplate). | Done |

#### B. Enhancement Opportunities

| Enhancement | Why Needed | Status |
|-------------|-----------|--------|
| ~~Pre-tagging audit~~ | **DONE** — `PreTagAuditCommand` performs complete dry-run predicting tags, collisions, ISO violations, spatial detection, and family PROD codes. Exports CSV. | Done |
| ~~Tag collision auto-fix~~ | **DONE** — `BuildAndWriteTag` auto-increments SEQ on collision. User can choose Skip/Overwrite/AutoIncrement via `TagCollisionMode` enum. | Done |
| ~~LOC/ZONE auto-detection~~ | **DONE** — `SpatialAutoDetect` class auto-derives LOC and ZONE from room data and project info. Integrated into TagAndCombine, AutoPopulate, TagNewOnly, FamilyStagePopulate. | Done |
| ~~Family-aware PROD codes~~ | **DONE** — `TagConfig.GetFamilyAwareProdCode()` inspects family name for 35+ specific PROD codes (Mechanical, Electrical, Lighting, Plumbing, Fire Alarm). | Done |
| ~~TagAndCombine writes only 6 containers~~ | **DONE** — Now writes ALL 36 containers (6 universal + 30 discipline-specific). | Done |
| ~~No incremental tagging~~ | **DONE** — `TagNewOnlyCommand` pre-filters to untagged elements. Much faster for adding new elements. | Done |
| ~~CompoundTypeCreator material properties~~ | **DONE** — Applies color, transparency, smoothness, shininess from CSV. | Done |
| ~~**No template automation**~~ | **DONE** — `TemplateManagerCommands.cs` with 17 commands and `TemplateManager` intelligence engine: 5-layer auto-assignment, compliance scoring, VG diff, style definitions. `ViewTemplatesCommand` expanded to 23 template definitions with VG configuration. | Done |
| ~~**No dockable panel UI**~~ | **DONE** — WPF dockable panel (`UI/` directory, 6 files) with 7-tab interface (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL), `IExternalEventHandler` dispatch for thread safety, ~521 buttons, colour swatches, bulk parameter controls. | Done |
| ~~Cross-parameter validation~~ | **DONE** — `ISO19650Validator` validates all tokens, cross-validates DISC/SYS against category, validates tag format. `FixDuplicateTagsCommand` auto-resolves duplicates. | Done |
| ~~Formula evaluation engine~~ | **DONE** — `FormulaEvaluatorCommand` + `FormulaEngine` reads 199 formulas from CSV, evaluates in dependency order (levels 0-6), supports arithmetic, conditionals, string concat, and Revit geometry inputs. | Done |
| ~~Family-stage pre-population~~ | **DONE** — `FamilyStagePopulateCommand` pre-populates all 7 tokens before tagging (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD). | Done |
| ~~Leader management commands~~ | **DONE** — 14 leader management commands: Toggle/Add/Remove Leaders, Align Tags, Reset Positions, Toggle Orientation, Snap Elbows, Auto-Align Leader Text, Flip Tags, Align Text, Pin/Unpin, Nudge, Attach/Free, Select by Leader. | Done |
| ~~Tag register export~~ | **DONE** — `TagRegisterExportCommand` exports comprehensive 40+ column asset register (tags, identity, spatial, MEP, cost, validation) to CSV. | Done |
| ~~Full auto-populate pipeline~~ | **DONE** — `FullAutoPopulateCommand` runs Tokens, Dimensions, MEP, Formulas, Tags, Combine, Grid in one click with zero manual input. | Done |
| ~~Native parameter mapping~~ | **DONE** — `NativeParamMapper` maps 30+ Revit built-in parameters to STING shared parameters. | Done |
| ~~Document automation~~ | **DONE** — `DeleteUnusedViewsCommand`, `SheetNamingCheckCommand`, `AutoNumberSheetsCommand` for view cleanup and ISO 19650 sheet compliance. | Done |
| ~~Schedule field remapping~~ | **DONE** — `ScheduleHelper.LoadFieldRemaps()` loads SCHEDULE_FIELD_REMAP.csv; `BatchSchedulesCommand` auto-remaps deprecated field names. | Done |
| ~~Port VALIDAT_BIM_TEMPLATE.py (45 checks) to C# ValidateTemplateCommand~~ | **DONE** — `ValidateTemplateCommand` in `DataPipelineCommands.cs` performs 45 validation checks (data file inventory, parameter consistency, material completeness, formula dependencies, schedule definitions, cross-references). | Done |
| ~~Dynamic category bindings from BINDING_COVERAGE_MATRIX.csv~~ | **DONE** — `DynamicBindingsCommand` in `DataPipelineCommands.cs` loads bindings from CSV, replacing hardcoded `SharedParamGuids.AllCategoryEnums`. | Done |
| ~~Color By Parameter system~~ | **DONE** — `ColorCommands.cs` with 5 commands: ColorByParameter (10 palettes, `<No Value>` detection), ClearOverrides, SavePreset, LoadPreset, CreateFiltersFromColors. Full `OverrideGraphicSettings` support. | Done |
| ~~Smart Tag Placement~~ | **DONE** — `SmartTagPlacementCommand.cs` with 9 commands: SmartPlace (8-position collision avoidance), Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight. `TagPlacementEngine` with scale-aware offsets and 2D AABB collision detection. | Done |
| ~~View automation commands~~ | **DONE** — `ViewAutomationCommands.cs` with 6 commands: DuplicateView, BatchRename, CopyViewSettings, AutoPlaceViewports, CropToContent, BatchAlignViewports. | Done |
| ~~Annotation color management~~ | **DONE** — 5 commands in `TagOperationCommands.cs`: ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors. | Done |
| ~~Schema validation~~ | **DONE** — `SchemaValidateCommand` validates BLE/MEP CSV columns match MATERIAL_SCHEMA.json (77-column schema). | Done |
| ~~Schedule management system~~ | **DONE** — `ScheduleEnhancementCommands.cs` (1,579 lines) with 9 commands: Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report. Plus ScheduleAutoFit, MatchWidest (functional), ToggleHidden inline operations. `ScheduleAuditHelper` engine loads CSV definitions for cross-reference. | Done |
| ~~Configurable tag format in project_config.json (separator, padding, segments)~~ | **DONE** — TAG_FORMAT section in project_config.json with `ParamRegistry.ApplyTagFormatOverrides()`. ConfigEditorCommand displays and saves tag format. | Done |
| ~~Batch command chaining / workflow presets~~ | **DONE** — `WorkflowEngine` with JSON presets, 3 built-in workflows, cancellation, TransactionGroup rollback | Done |
| ~~Cancellation support~~ | **DONE** — `StingProgressDialog` + `EscapeChecker` for batch operations | Done |
| ~~Real-time auto-tagging~~ | **DONE** — `StingAutoTagger` IUpdater for zero-touch tagging on element placement | Done |
| ~~Live compliance dashboard~~ | **DONE** — `ComplianceScan` cached RAG status for status bar | Done |
| ~~IFC/BEP/Clash pipeline~~ | **DONE** — 6 new DataPipeline commands (IFC, BEP, clash, Excel import, keynote sync) | Done |

---

### Remaining Underutilized Data Files

| File | Rows | Current Status | Proposed Usage |
|------|------|----------------|----------------|
| ~~`MATERIAL_SCHEMA.json`~~ | 77 cols | **DONE** — loaded by `SchemaValidateCommand` | Validates BLE/MEP CSV columns match schema |
| ~~`BINDING_COVERAGE_MATRIX.csv`~~ | Large | **DONE** — loaded by `DynamicBindingsCommand` | Replaces hardcoded category bindings |
| ~~`CATEGORY_BINDINGS.csv`~~ | 10,661 | **DONE** — loaded by `TemplateManager.LoadCategoryBindings()`, used in `LoadSharedParamsCommand` Pass 2 | Augments JSON-derived discipline bindings with CSV-based category mappings |
| ~~`FAMILY_PARAMETER_BINDINGS.csv`~~ | 4,686 | **DONE** — loaded by `TemplateManager.LoadFamilyParameterBindings()`, used in `BatchAddFamilyParamsCommand` | Data-driven family parameter binding with GUID validation |
| ~~`VALIDAT_BIM_TEMPLATE.py`~~ | 45 checks | **DONE** — ported to C# `ValidateTemplateCommand` | 45 validation checks now in `DataPipelineCommands.cs` |

---

### Implementation Priority Matrix

#### Known Gaps — Tagging Pipeline Deep Review (Phase 34)

Critical review of the tagging workflow identified the following logic, automation, and flexibility gaps across tagging, BIM/BEP/COBie systems:

**Critical Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| GAP-001 | WriteContainers in RunFullPipeline | `ParameterHelpers.cs` | **DONE** — `WriteContainers` retry call added at lines 2804-2811 after `BuildAndWriteTag`, ensuring all 53 containers are written even if `BuildAndWriteTag` only writes TAG1. |
| GAP-008 | PreTagAudit token validation | `PreTagAuditCommand.cs` | **DONE** — Phase 36: Predicted token values (DISC/SYS/FUNC/PROD/LOC/ZONE) now validated against `ISO19650Validator.ValidateToken()` code lists before tag simulation. Invalid codes reported as `ISO_PREDICTED_TOKEN` audit issues with grouped counts in report. |
| ERR-002 | Read-only parameter binding | `ParameterHelpers.cs` | **DONE** — Phase 35: `SetString` logs first 5 + every 100th read-only skip with `_readOnlySkipCount` throttle. `ResetReadOnlySkipCount()` for batch operation boundaries. |

**High Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| GAP-002 | TOKEN_LOCK_TXT timing | `ParameterHelpers.cs` | **DONE** — Lock snapshot taken after `TypeTokenInherit` (line 2665), restore runs AFTER both `CategoryForceSys` and `CategoryTokenOverrides` (line 2727-2748). Locked tokens are correctly restored after all overrides. |
| GAP-006 | Formula context timing | `FormulaEvaluatorCommand.cs` | **By design** — Formulas intentionally evaluate post-population state so they can reference derived token values (DISC, SYS, etc.). NativeParamMapper runs before formulas, providing Revit native values. This is the correct order: raw data → token population → native mapping → formula evaluation. |
| ERR-003 | Collision detection atomicity | `TagConfig.cs` | **Accepted risk** — In worksharing environments, tag index is built at batch start. Collision detection is best-effort; Revit's own worksharing conflict resolution handles multi-user scenarios. Adding distributed locking would require a central server, which is outside the plugin's scope. |

**Medium Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| FLEX-001 | No custom token validators | `TagConfig.cs` | **DONE** — Phase 35: `CUSTOM_VALID_DISC/SYS/FUNC/LOC/ZONE` arrays in `project_config.json` merged with built-in ISO 19650 code lists. |
| FLEX-003 | No post-population hooks | `ParameterHelpers.cs` | **Mitigated** — `CATEGORY_TOKEN_OVERRIDES` in `project_config.json` provides per-category token overrides without source code changes. `CATEGORY_FORCE_SYS` provides SYS overrides. For PROD rules, `GetFamilyAwareProdCode()` handles 35+ family-name patterns. |
| FLEX-005 | SEQ counter isolation | `TagConfig.cs` | **DONE** — Phase 35: `BuildAndWriteTag` tracks `preIncrementValue` and rolls back on TAG1 write failure. Sidecar persistence only saves after successful commit. |
| HC-001 | Hardcoded 10 ft proximity | `ParameterHelpers.cs` | **DONE** — Phase 35: `TagConfig.ProximityRadiusFt` configurable via `PROXIMITY_RADIUS_FT` config key. |
| HC-003 | Hardcoded 500-element batch | `ResolveAllIssuesCommand.cs` | **DONE** — Phase 35: `TagConfig.ResolveBatchSize` configurable via `RESOLVE_BATCH_SIZE` config key. |

### Future Enhancement Gaps (Phase 74 Deep Review — 5-Agent Analysis)

**Model Tab (Agent 1 — 18 gaps):** Missing auto-tagging after model creation (INT-01), DWG layer-to-parameter mapping (CAD-01), geometric cleanup after DWG import (CAD-02), regional LCA factors (CONFIG-01), custom fitting loss database from JSON (CONFIG-02), one-way shear check (PUNCH-01), wind height profile (WIND-01), Voronoi edge case guard (EDGE-01).

**Tagging/BIM (Agent 2 — 47 gaps):** Config key preservation on LoadDefaults (CONFIG-01), ReadOnlySkipCount auto-reset (CONFIG-02), DocumentManager tab persistence to disk (CONFIG-03), ComplianceScan concurrent -1 sentinel check (CRASH-01), PopulationContext ActiveView validity (CRASH-02), ProjectTeamRegistry graceful degradation (CRASH-03), sidecar directory creation guard (CRASH-04).

**Workflows/Coordination (Agent 3 — 29 gaps implemented in Phase 75):** All 29 remaining gaps from Agent 3 have been implemented. See Phase 75 above.

**Docs/Schedules (Agent 4 — 11 gaps):** ViewScheduleLinkEngine missing (DOC-01), schedule template library (DOC-02), document package only 2 of 8 deliverables (DOC-03), PrintQueue O(n²) performance (DOC-04), COBie export only 7 of 11 sheets (HO-01), document versioning/supersession (DOC-06).

**UI/Dispatch (Agent 5 — 6 gaps):** 4 missing command classes (BimKnowledgeBase, CommandSuggestion, ConfigurableTagFormat, CommissioningChecklist), 5 TagStudio stubs with misleading names, 170 dispatch-only entries undocumented.

### Future Enhancement Gaps — DWG-to-Structural Auto-Modeling (Phase 76 Review)

| ID | Gap | Priority | Description |
|----|-----|----------|-------------|
| DWG-FUT-01 | Structural detail reading | High | Read reinforcement schedules, bar marks, curtain lengths from DWG text/tables and populate Revit rebar parameters |
| DWG-FUT-02 | Multi-storey propagation | High | Detect repeating floor patterns and auto-replicate structural layout to upper levels with column continuity |
| DWG-FUT-03 | Section drawing interpretation | Medium | Parse DWG sections/elevations to extract beam depth, slab edge detail, and connection types |
| DWG-FUT-04 | Block-to-family mapping | Medium | Map DWG blocks to Revit families (door/window/equipment blocks → family instances) with attribute transfer |
| DWG-FUT-05 | Hatch-to-material mapping | Medium | Interpret DWG hatch patterns to assign materials (45° hatch → concrete, cross-hatch → masonry, etc.) |
| DWG-FUT-06 | Dimension text extraction | Medium | Read dimension strings near elements to override auto-detected sizes (e.g., "300x600" near a beam) |
| DWG-FUT-07 | Transfer beam schedule | Medium | Parse tabulated beam schedules from DWG (beam mark, size, span, reinforcement) and apply to created beams |
| DWG-FUT-08 | Curved wall support | Low | Detect arc segments in wall layers and create curved Revit walls |
| DWG-FUT-09 | Opening detection | Low | Detect gaps in wall lines as door/window openings and place appropriate family instances |
| DWG-FUT-10 | Retaining wall detection | Low | Identify retaining walls from ground level context and apply appropriate structural properties |
| DWG-FUT-11 | Connection detail extraction | Low | Read structural connection details (base plates, splice connections) and create corresponding elements |
| DWG-FUT-12 | Point cloud integration | Future | Combine DWG structural layout with point cloud scan for as-built verification |
| DWG-FUT-13 | ML-based element recognition | Future | Train element classifier on DWG geometry patterns for improved auto-detection accuracy |
| DWG-FUT-14 | IFC structural import | Future | Import IFC structural models as alternative to DWG with analytical model creation |

### 5-Agent Deep Review Findings Summary (Phase 77)

**Agent 1 (Tagging Pipeline):** 71 findings — 7 CRITICAL, 10 HIGH, 10 MEDIUM + 5 workflow + 6 integration + 4 standards + 5 error recovery + 7 efficiency + 8 automation gaps. Key: parameter cache key instability across sessions, ValidSysCodes null-check pattern, AutoTagger PopulationContext null crash, 200-element batch final chunk silent failure, four-bucket compliance missing STATUS/REV for "fully resolved", ResolveAllIssues sampled validation (50 of 1000), ValidateToken HashSet optimization (400x faster).
**Agent 2 (BIM/Coordination):** 47 findings — 8 CRITICAL, 10 HIGH, 10 MEDIUM, 10 LOW + 5 architecture + 4 performance. Key: COBie System worksheet uses defaults not actual SYS distribution, CDE transitions lack approval hierarchy enforcement, issues/revisions/transmittals are disconnected JSON silos, BIM Coordination Center exits after single action, Excel import OOM on 10K+ rows.
**Agent 3 (Warnings/Model/Structural):** 42 findings — 4 CRITICAL, 7 HIGH, 18 MEDIUM. Key: dimension validation (fixed), level fallback (fixed), warning category split (fixed). Many structural algorithm findings confirmed already-fixed in earlier phases.
**Agent 4 (UI/Dispatch/Docs):** 15 findings — 2 CRITICAL (dispatch oversupply), 3 HIGH (COBie handover gaps), 7 MEDIUM. Key: 1142 dispatch entries vs 721 commands (421 are legitimate aliases/inline handlers). COBie handover missing Contact/Attribute/Job/Resource sheets (documented for future phase).
**Agent 5 (DWG/Phase75):** 42 findings — 12 CRITICAL, 10 HIGH, 12 MEDIUM. Key: WorkflowScheduler consumer not wired (fixed), config dimension validation (fixed), conversion sidecar (fixed). Many "CRITICAL" findings were false positives (StructuralLayerClassifier exists, IsSuppressed handles expiry, prerequisite logic correct).

### Future Enhancement Gaps (Phase 77 Deep Review)

| ID | Gap | Priority | Description |
|----|-----|----------|-------------|
| FM-HO-01 | COBie Contact/Attribute/Job/Resource sheets | High | **DONE** — verified Phase 78: COBie handover export already generates all 11 worksheets (Facility, Floor, Space, Type, Component, System, Zone, Contact, Attribute, Job, Resource + Instruction). |
| FM-HO-02 | Phase-aware COBie export | High | **DONE** Phase 148: `PhaseAwareCobie.Filter` (`Phase148Engine.cs`) returns only elements alive in the requested phase using PHASE_CREATED / PHASE_DEMOLISHED, stamping each row with the phase name so the Component sheet can be partitioned per phase. |
| WF-SCHED-01 | Schedule template library | Medium | **DONE** Phase 148: `ScheduleTemplateLib.Save / List` persists named templates as JSON in `_BIM_COORD/schedule_templates/`. |
| WF-SCHED-02 | Cross-schedule field consistency | Medium | **DONE** Phase 148: `ScheduleTemplateLib.CheckFieldConsistency` walks every `ViewSchedule` and reports fields whose canonical name appears under different `ColumnHeading` labels. |
| UI-DISP-01 | Dispatch registry pattern | Low | Refactor 1142-case switch to dispatch registry with per-module command registrations |
| DOC-REG-01 | Drawing register ISO 19650-2 fields | Medium | Missing CDE status, suitability code, approval history in drawing register export |
| DWG-MULTI-01 | Multi-layer wall detection | Medium | DWG wizard doesn't detect dual-layer wall encoding (exterior + interior leaf pairs) |
| DWG-CURVE-01 | Curved wall support | Low | Arc segments in DWG wall layers not detected; only straight lines converted |
| MEP-SCHED-01 | MEP commissioning schedules | Medium | **DONE** Phase 148: `MepCommissioningSchedules.CreateMissing(doc)` mints three commissioning schedules — Connector Flow Rate, Pipe Balancing Status, HVAC Pressure Drop Summary — idempotent (skips schedules already present). |
| STRUCT-REBAR-01 | Rebar spacing validation | Medium | **DONE** Phase 148: `RebarSpacingChecker.Check(doc)` walks every `Rebar` element, derives bar diameter from `RebarBarType.BarDiameter`, computes clear spacing from `REBAR_ELEM_LENGTH / NumberOfBarPositions`, and reports any clear spacing < max(diameter, 20 mm) per EC2 §8.2. |
| PERF-WARN-01 | Warning regex compilation | Medium | 150+ regex patterns evaluated linearly per warning; pre-compile into Regex[] array |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus | Medium | **DONE** Phase 148: `AcousticCavityBonus.BonusAt(hz)` interpolates BS EN 12354-1 Annex B.3 indicative values; `WeightedRwBonus()` averages across the 16 standard 1/3-octave bands used to derive Rw. |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | Critical | **DONE** Phase 148: `CobieSystemDistribution.Build(doc)` walks every tagged element and aggregates real `ASS_SYS_TXT` values + sample tag list, replacing the static `TagConfig.SysMap` defaults. |
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement | Critical | **DONE** Phase 148: `CdeApprovalGate.Validate(doc, fromState, toState)` resolves the current user's role from `_BIM_COORD/project_team.json` and denies transitions whose minimum role rank is not met (Originator/Reviewer/Approver). |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal cross-linking | Critical | **DONE** Phase 148: `CrossLinkEngine.WalkFromIssue` walks `linked_revision_ids` / `linked_transmittal_ids` / `linked_issue_ids` arrays across the three sidecars; `AppendLink` adds cross-references with dedupe. |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | Critical | **DONE** Phase 148: BCC is already modeless via `dlg.Show()` + `ExternalEvent`. The Ctrl+E shortcut now dispatches the export action through `ActionDispatcher` instead of closing the window, so coordinators stay in the centre. |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | Critical | **DONE** Phase 165: `StreamingImport` now wraps the workbook load in OOM-aware exception handling that produces operator-actionable guidance ("split the workbook"). Per-batch transactions and the 500K-row clamp remain in place from the original Phase 78 work. Full `OpenXmlReader` rewrite still deferred until ClosedXML 1.x. |
| BIM-COBIE-SHEETS-01 | Missing COBie Contact/Facility/Floor/Space worksheets | High | **DONE** — verified Phase 78 (same scope as FM-HO-01 above). |
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | High | **DONE** Phase 148: `DataDropTracker` POCO + Load/Save round-trip on `_BIM_COORD/data_drops.json` with default DD1-DD4 milestones, planned/actual dates, and RAG via `DataDropTracker.Rag(milestone, currentCompliancePct)`. |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | High | **DONE** Phase 78 — verified at `RevisionManagementCommands.cs:677-701` (`GAP-R9: Auto-propagate new REV to all tagged elements`). |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | High | **DONE** Phase 148: `FuncSysValidator.Validate(rows)` returns mismatches against the SYS→{FUNC*} matrix (HVAC → SUP/RET/EXH/HTG/CLG/…, LV → PWR/LIT/CTL/DAT, etc.). |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | High | **DONE** Phase 148: `ComplianceForecast.Build(doc, target)` reads `_BIM_COORD/compliance_trend.json`, runs `WarningsEngine.ForecastCompliance`, and returns a `ForecastSummary` with caption text the dashboard can render inline. |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | High | **DONE** Phase 148: `OnDocumentOpened` now calls `ProjectFolderEngine.CreateFolderStructure(doc)` on every doc open (idempotent). Toggle via `AUTO_CREATE_CDE_FOLDERS` config key (default true). |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | High | BCF export works but no import mechanism for changes from ACC/Procore — **deferred** (needs ACC/Procore OAuth). |
| BIM-4D-HANDOVER-01 | 4D schedule linked to document handover dates | Critical | **DONE** Phase 148: `DataDropTracker.GetDD4HandoverDate(doc)` exposes the DD4 actual / planned date so `Scheduling4DEngine` can extend the timeline beyond construction-finish into handover. |
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | Medium | **DONE** Phase 148: `SidecarVersioning.EnsureArrayMeta(arr, schema)` stamps a `_meta` sentinel record (`version=1.1`, `schema`, `written_at`, `written_by`); readers iterate via `Records()` to skip the sentinel and tolerate missing-meta legacy files. |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | Medium | **DONE** Phase 148: `TransmittalGate.Validate(doc, transmittal, requiredRank=1)` blocks transmittals whose referenced documents are below SHARED, returning a structured `(pass, blockers, summary)` result. |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | Medium | No way to see per-member issue/task distribution for resource balancing |
| TAG-CACHE-01 | Parameter cache key instability | Critical | Cache key using doc.GetHashCode() changes across sessions causing stale reads; use stable PathName key |
| TAG-AUTOTAG-NULL-01 | AutoTagger PopulationContext null crash | Critical | PopulationContext.Build() returns null on corrupted docs; no null check before PopulateAll |
| TAG-BATCH-FINAL-01 | Batch tag final chunk silent failure | Critical | 200-element chunked transactions silently fail on final incomplete batch (<100 elements) |
| TAG-VALIDATE-BUCKET-01 | Four-bucket compliance STATUS/REV gap | Critical | "Fully resolved" bucket doesn't require STATUS+REV populated; false-green compliance reporting |
| TAG-RESOLVE-SAMPLE-01 | ResolveAllIssues sampled validation | Critical | Post-fix ISO validation runs on 50 of 1000 elements; unverified fixes applied to remaining 950 |
| TAG-VALIDATE-MEMO-01 | ValidateToken HashSet optimization | High | List.Contains O(k) → HashSet O(1) for token validation; 400x faster for 50K-element models |
| TAG-SORT-LEVEL-01 | SmartSort level elevation recalculated per batch | High | **DONE** — `BatchTagCommand._levelElevationCache` (atomic tuple, doc-keyed) reuses elevations across batches; cleared on document close. |
| TAG-PREFLIGHT-DUP-01 | Pre-flight and main loop duplicate spatial indexing | High | **DONE** — Phase 147: `TokenAutoPopulator.PopulationContext.Build` cached per-document with 30 s TTL; invalidated on doc close, `TagConfig.LoadFromFile`, and after every tagging command via `PostTagCleanup`. |
| TAG-DEFERRED-OVERFLOW-01 | AutoTagger deferred queue overflow silent drop | High | **DONE** — Phase 147: `StingAutoTagger.LoadDroppedElementsSidecar` re-enqueues previously-dropped IDs on document open and rotates the sidecar to `.consumed` so a re-open does not double-replay. Save-path now also resets in-memory state. |
| TAG-SEQ-SIDECAR-DRIFT-01 | SEQ sidecar/model counter divergence on cancel | High | Cancel during batch N leaves sidecar at N but model at N-1; counters diverge by 500 |
| TAG-ISO-USERNAME-01 | ISO 19650 contributor tracking in audit trail | High | **DONE** — Phase 147: `ASS_TAG_MODIFIED_BY_TXT` (GUID `c1f4d6b8-2a3e-4d5b-9c6f-7a8b9c0d1e2f`) added to `MR_PARAMETERS.{txt,csv}`. `RunFullPipeline` was already writing `Environment.UserName` to it; the parameter is now actually bound and persisted, closing the ISO 19650-2 §A.5 "person responsible" requirement. |
| TAG-STALE-WARN-01 | Stale elements not auto-creating warnings | Medium | **DONE** — Phase 147: `StaleWarningPromotionJob` (single-shot idle consumer) calls `WarningsEngineExt.AutoRaiseStaleIssues` once `staleCount >= TagConfig.StaleWarningThreshold` (default 5, configurable via `STALE_WARNING_THRESHOLD`). Enqueued on every batch in `StingStaleMarker.Execute` that flags stale, and once on document open after the compliance refresh. |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization | Medium | **DONE** Phase 148 (with caveat): `WorkflowDagPlanner.Plan` topo-sorts steps by `(parallelGroup, originalIndex)` and `MarkBlocked` flags steps in groups behind a failed upstream group. True OS-thread parallelism is impossible because the Revit API is single-threaded; the DAG planner is the realistic interpretation. |
| TAG-COMPLIANCE-LOCK-01 | ComplianceScan pending state deadlock | Medium | **DONE** — Phase 78: 60s timeout auto-resets _scanning flag |

### Verified Already-Fixed Gaps (False Positives from Deep Review)

The following gaps were reported by deep review agents but verified as already implemented:
- **TAG-CACHE-01**: Parameter cache already uses stable `PathName/Title` key (not `GetHashCode()`)
- **TAG-AUTOTAG-NULL-01**: AutoTagger already has H-03 null guard at line 336
- **TAG-VALIDATE-MEMO-01 (HashSet)**: Validator already uses `HashSet<string>` (not `List`)
- **TAG-BATCH-FINAL-01**: Batch pattern handles final chunk via `batchEnd = Math.Min(...)` range guard
- **TAG-RESOLVE-SAMPLE-01**: ResolveAllIssues runs RunFullPipeline on ALL elements (not sampled 50)
- **TAG-VALIDATE-BUCKET-01**: Four-bucket classification already requires STATUS+REV for "fully resolved"
- **TAG-SEQ-SIDECAR-DRIFT-01**: Sidecar saved per-batch; cancel rolls back current batch only, sidecar tracks committed batches accurately
- **FM-HO-01 (COBie sheets)**: COBie handover export already generates all 11 + Instruction sheets (Facility, Floor, Space, Type, Component, System, Zone, Contact, Attribute, Job, Resource + Instruction). Re-verified Phase 148.
- **BIM-COBIE-SHEETS-01**: Same as FM-HO-01 — already complete (re-verified Phase 148).

### Remaining Future Enhancement Gaps (Phase 78 Triage)

After verification, 15 of 44 gaps were confirmed as already implemented or false positives. The remaining 29 gaps are prioritized below:

**CRITICAL (should implement before handover):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement per ISO 19650-2 §5.6 | DONE Phase 148 |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal JSON cross-linking | DONE Phase 148 |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | DONE Phase 148 |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | DONE Phase 165 (OOM hardening) — full streaming reader still pending |
| BIM-4D-HANDOVER-01 | 4D schedule linked to DD4 handover dates | DONE Phase 148 |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | DONE Phase 148 |

**HIGH (should implement for production):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | DONE Phase 148 |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | DONE Phase 78 (verified Phase 148) |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | DONE Phase 148 |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | DONE Phase 148 |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | DONE Phase 148 |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | Deferred — needs ACC/Procore OAuth |
| TAG-SORT-LEVEL-01 | SmartSort level elevation cached per document | DONE (verified Phase 147) |
| TAG-PREFLIGHT-DUP-01 | Reuse PopulationContext from pre-flight in main loop | DONE Phase 147 |

**MEDIUM (enhancement quality):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | DONE Phase 148 |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | DONE Phase 148 |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | DONE Phase 148 |
| TAG-STALE-WARN-01 | Stale elements auto-creating warnings | DONE Phase 147 |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization via DAG | DONE Phase 148 (DAG planner; true parallelism blocked by Revit single-threading) |
| DWG-MULTI-01 | DWG multi-layer wall detection | Open — multi-day spike (DWG geometry rewrite) |
| DWG-CURVE-01 | Curved wall support from DWG arcs | Open — multi-day spike (DWG geometry rewrite) |
| WF-SCHED-01 | Schedule template library (save/load/apply) | DONE Phase 148 |
| WF-SCHED-02 | Cross-schedule field consistency validation | DONE Phase 148 |
| MEP-SCHED-01 | MEP commissioning schedules | DONE Phase 148 |
| STRUCT-REBAR-01 | Rebar spacing validation (spacing > bar diameter) | DONE Phase 148 |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus in double-leaf Rw | DONE Phase 148 |


### v6 Runner Gaps — 2026-04-22 Audit

The "STING v6 — Claude Code runner prompt" (docx 2026-04-22) defined 18
new gaps (N-G1 … N-G18). Closing status as of Phase 111:

| Gap | Title | Status | Reference |
|-----|-------|--------|-----------|
| N-G1 | FilteredElementCollector audit | **DONE** Phase 109 (S1.3) | `Performance_AuditNotes.md` |
| N-G2 | TransactionHelper | **DONE** Phase 109 (S1.5) | `Core/TransactionHelper.cs` |
| N-G3 | Live standards IUpdater | **DONE** Phase 109 (S4.10) | `Core/Validation/LiveStandardsUpdater.cs` |
| N-G4 | Health dashboard | **DONE** Phase 111 (S7.1) | `V6/HealthDashboardEngine.cs` |
| N-G5 | Clash triage | **DONE** Phase 109 (S6.1) | `V6/ClashTriageEngine.cs` |
| N-G6 | Clash resolution suggester | **DONE** Phase 109 (S6.2) | `V6/ClashResolutionSuggester.cs` |
| N-G7 | Federation walker | **DONE** Phase 109 (S6.3) | `V6/FederationLinkedWalker.cs` |
| N-G8 | ACC Issues round-trip | **DONE** Phase 109 (S6.4) | `V6/AccIssueSync.cs` |
| N-G9 | As-built reconciler | **DONE** Phase 109 (S6.5) | `V6/AsBuiltReconciler.cs` |
| N-G10 | Sheet matrix | **DONE** Phase 109 (S6.6) | `V6/SheetMatrixGenerator.cs` |
| N-G11 | 4D Gantt reader | **DONE** Phase 109 (S6.7) | `V6/FourdGanttReader.cs` |
| N-G12 | Install-hours / labour takeoff | **DONE** Phase 111 (S7.2) | `V6/LabourHoursEngine.cs` |
| N-G13 | Carbon staging | **DONE** Phase 109 (S6.8) | `V6/CarbonStageTracker.cs` |
| N-G14 | IFC 4.3 PSet mapping | **DONE** Phase 109 (S6.9) | `V6/IfcPsetMapping.cs` |
| N-G15 | Excel formula-preserving sync | **DONE** Phase 109 (S6.11) | `V6/ExcelBidirectionalSync.cs` |
| N-G16 | QR commissioning workflow | **DONE** Phase 111 (S7.3) | `V6/QRCommissioningWorkflow.cs` |
| N-G17 | Mobile offline-first | **DONE** Phase 111 (S7.4) | `Planscape/src/utils/{readThroughCache,connectivity,conflictResolver,offlineQueue}.ts` |
| N-G18 | AI vision | Deferred Y2 | — |

**Outcome**: 17 of 18 gaps closed. Only N-G18 remains deferred per the
original runner's Year-2 scope. The Phase 111 commits (`S7.1` → `S7.4`)
landed without `dotnet build` verification — the only remaining
pre-merge task is running `Tests_V6SmokeTest.md` Section 8 in Revit.

### Tag Label Content Gaps — 2026-04-22 Audit

Infrastructure for paragraph-depth tiers T1-T10 and per-row Style/Color/Size/Box/Arrow
overrides is fully live (`ParamRegistry.PARA_STATE_1..10`, `TagStyleEngine.SetParagraphDepth`,
`SetParagraphDepthExtCommand` with .01-.10 picker, TagStyleCatalogue variant naming).

| Gap | Title | Status |
|-----|-------|--------|
| TAG-LABEL-T4-10 | Author T4-T10 label rows across all tag families. | **DONE** Phase 106. Added 2,982 rows across 142 tag families following the v5.3 preamble blueprint: T4=Commissioning (COMM_STATE/DATE/OPERATIVE), T5=Cost (CST_UG_PRICE/INTL_PRICE/QUOTE_REF), T6=Carbon (CBN_A1_A3/A4/B6), T7=Fabrication (ASS_SPOOL_NR/FAB_STATUS/QC_INSPECTOR), T8=Clash (CLASH_TRIAGE_SEVERITY/CATEGORY/RESOLUTION_STATUS), T9=As-built/Health (ASBUILT_DEVIATION/CAPTURE_DATE + HEALTH_SCORE_LAST), T10=Compliance (IFC_PSET_OVERRIDE + ACC_ISSUE_ID/SYNC_STATUS). Per-family dedupe skips any candidate already in T1-T3. |
| TAG-LABEL-STYLE-COLS | Populate per-row `Style` / `Color` / `Size` columns. | **DONE** Phase 106. All 4,647 tier rows now carry explicit Style/Color/Size per industry-convention defaults: T1-T3=NOM/BLACK; T4=BOLD/BLUE (commissioning, client-facing); T5=NOM/PURPLE (cost, finance); T6=ITALIC/GREEN (carbon, sustainability); T7=BOLD/ORANGE (fabrication, workshop hi-vis); T8=BOLD/RED (clash, alert); T9=ITALIC/GREY (as-built, retrospective); T10=NOM/GREY (compliance, administrative). TAG7A-F rows carry ISO 19650 per-section prescriptions. |
| TAG-LABEL-BOX-ARROW-COLS | Add `Box` and `Arrow` trailing columns. | **DONE** Phase 106. Schema bumped v5.2 → v5.3. All 4,647 tier rows carry explicit Box=None / Arrow=None defaults; per-row overrides can be set to `TagStyleCatalogue.Arrowheads` values (Arrow30, Arrow_Open_30, Arrow_Filled_15, Dot, Tick) or tag-box set (Filled30, Filled50, Outline). Warning sections untouched. |


### Future Enhancement Gaps — Template Engine v1.2 (Phase 112 Deferrals)

Two stages from the `20260423_planscape_template_engine_runner_v1.1.pdf`
runner are design-complete and deferred to v1.2. The parameters and
manifest scaffolding to turn them on are already in place (see Phase 112
in `CHANGELOG.md`).

| Gap | Runner stage | Status | Unblocker |
|-----|--------------|--------|-----------|
| `TPL-V12-SIG` Signature provider abstraction | S19 | **Deferred** — `PRJ_ORG_SIGNATURE_PROVIDER_TXT` + `SignatureConfig` POCO already shipped; no adapter yet. | Requires server-side key management (DocuSign / Adobe OAuth); design-complete. |
| `TPL-V12-AI`  AI-assisted metadata extraction from incoming PDFs | S20 | **Deferred** — `PRJ_ORG_AI_EXTRACT_ENABLED_BOOL` already shipped; no service wire. | Requires server-side Python extraction service; design-complete. |

### Future Enhancement Gaps — Template Engine v1.1 Follow-ups (Phase 112 Review)

| Gap | Location | Status |
|-----|----------|--------|
| `TPL-FOLLOW-01` `.docx` templates ship as professional stubs with proper tables, banded header, footer `PAGE`/`NUMPAGES` fields, loop tables and signature blocks — designers may still want bespoke branded layouts in Word. | `StingTools/Docs/_template_sources/*.docx` | Open — non-blocking (stubs render cleanly). |
| `TPL-FOLLOW-02` `dotnet build` verification pending — every Revit API call uses the documented signature and every `.cs` file was brace-balanced after stripping strings and comments. | All 22 new `.cs` files under `StingTools/Docs/` | Open — needs Windows dev box with Revit 2025 API. |
| `TPL-FOLLOW-03` "My queue" sub-section in BCC Deliverables tab (S12 v1.1) — `WorkflowEngine.GetMyQueue(userEmail)` is implemented but no UI binding yet. | `StingTools/UI/BIMCoordinationCenter.cs` | **DONE** Phase 165 — surfaced in the Workflows tab above the quick-workflow buttons; populated by `BuildCoordData` with SLA RAG (GREEN/AMBER/RED). |
| `TPL-FOLLOW-04` "Recipient matrix" view in BCC Deliverables tab (S18) — `DistributionGroups.SuggestFor(deliverable)` and group persistence are implemented; matrix view not yet drawn. | `StingTools/UI/BIMCoordinationCenter.cs` | Open — data layer ready. |
| `TPL-FOLLOW-05` Faceted filter pills + saved-searches combo in Document Manager filter bar (S17). `DocumentIndex.Search` + `SavedSearchStore` implemented; dialog bar still uses the legacy free-text box. | `StingTools/UI/DocumentManagementDialog.cs` | Open — data layer ready. |

### Future Enhancement Gaps — Structural DWG-to-BIM (Phase 140 Deferrals)

Phase 140 landed grid snapping, span-proportional beam depth, multi-storey
column heights, beam endpoint trimming, multi-category numbering, slab
voids, grid-label marks, load-path warnings as TextNotes, duplicate
detection, the Re-analyse dry-run button, and the misleading-label fix
that triggered the branch (`Parallel max gap` → `Parallel pair max
gap`). The items below were called out in the Phase 140 planning prompt
but deferred so each could land cleanly in its own follow-up.

| Gap | Description | Unblocker |
|-----|-------------|-----------|
| ~~`DWG-STRUCT-P2A` Strip foundation detection (under walls)~~ | **DONE** Phase 142. `StripFoundationDetector` builds rectangular loops along each wall centreline (oversized by `StripFndOversizeMm` per side) and feeds them through `CreateSlabsFromBoundaries`. Wizard exposes the toggle + oversize knob. |
| ~~`DWG-STRUCT-P2F` Endpoint gap bridging at detection time~~ | **DONE** Phase 142. `DetectStructuralWalls` and `DetectBeamCenterlinesV2` synthesise overlap when two parallel lines fall within `EndpointGapToleranceMm` of each other longitudinally. |
| ~~`DWG-STRUCT-P3B` Slab centroid → room seeding~~ | **DONE** Phase 143. `SlabRoomSeeder.Seed(doc, level, outerSlabs, voidLoops, cfg)` drops Revit Rooms at outer-loop centroids skipping points inside voids and existing rooms. Wizard exposes the toggle. |
| ~~`DWG-STRUCT-P3C` Auto-create structural views after conversion~~ | **DONE** Phase 143. `StructuralViewCreator.CreateViews` creates a StructuralPlan ViewPlan per level that received elements and applies the corporate "S-PLAN" DrawingType via Phase-113 `DrawingTypePresentation.Apply()`. Default OFF (opt-in). |
| ~~`DWG-STRUCT-DEEP-1` Steel I-section vs concrete rectangle inference~~ | **DONE** Phase 143. `BeamMaterialInferrer.AnnotateAll` heuristically classifies beams: parallel-pair (`WidthDetected==true`, width ≥ 200 mm) → concrete; single-line → steel I-section. Suffix appended to LayerName for downstream type matching. |
| ~~`DWG-STRUCT-DEEP-2` Pile cap / raft / strip differentiation~~ | **DONE** Phase 143. `FoundationClassifier.Classify` splits detected rectangles into Pad / Raft / PileCap based on plan area + clustering. Rafts route to slab path; pads + pile caps stay on pad-foundation path. |
| ~~`DWG-STRUCT-DEEP-3` Cantilever detection~~ | **DONE** Phase 142. `BeamSupportClassifier` flags free-end and cantilever beams; `MarkCantileverBeams` toggle stamps the Comments parameter so they're filterable in schedules. Junction warnings are also placed as TextNotes via Phase 141 `DetectJunctions` wiring. |
| ~~`DWG-STRUCT-DEEP-4` Beam-overlap ratio configurability~~ | **DONE** Phase 142. `BeamOverlapMinRatio` config field threads through `DetectBeamCenterlinesV2` and `DetectStructuralWalls`. Wizard exposes the knob in ACCURACY (Phase-142). |
| `DWG-STRUCT-DEEP-5` Foundation EC7 sizing  with soil class + load | Phase 140 surfaced the EC7 §6.5 disclaimer; the actual heuristic is still a flat 1.5× column-bbox oversize. A correct implementation needs soil bearing capacity, load combinations, and serviceability checks per EC2/EC7. | New `FoundationSizingEngine` that takes `(columnLoad, soilClass, loadCombination)` and returns a footing size from EC2/EC7. |
| ~~`DWG-STRUCT-DEEP-6` Junction-type Mark stamping~~ | **DONE** Phase 143. `JunctionMarkStamper.Stamp` appends `J:T` / `J:L` / `J:X` / `J:S` to the Mark of every column / beam participating in a detected junction. (True connection-detail synthesis — bolt patterns, weld lines, Revit connection families — remains future work, tracked as DWG-STRUCT-DEEP-6b below.) |
| `DWG-STRUCT-DEEP-6b` Connection-detail element synthesis | Phase 143 stamps junction-type Marks but does not create connection geometry. A future pass would create bolt patterns, weld lines, or Revit structural connection family instances at each L/T/Cross junction. | Connection-element synthesizer per junction type, consumed by Revit structural connection families. |
| ~~`DWG-STRUCT-DEEP-7` Continuous columns via Top Constraint = top level~~ | **DONE** Phase 142. `CreateColumnsWithHeight` already sets `FAMILY_TOP_LEVEL_PARAM` to the top level when `topLevel != null` — Phase 142 verified the existing behaviour, added the explicit `UseTopConstraintForContinuousColumns` config flag for documentation, and clarified the wizard tooltip. |
| ~~`DWG-STRUCT-DEEP-8` Beams-on-walls applied per-beam, not globally~~ | **DONE** Phase 142. `BeamSupportClassifier` reads each beam endpoint's actual support type; `ApplyBeamSupportPostCreation` only applies the wall-top offset to beams that rest on a wall and not on a column. (The `BeamsRestOnWalls` config field had been on the type since Phase 78 but was never read — Phase 142 closed the loop.) |

### Cloud-Sync & Federation Feature Audit (2026-04-28)

A consultancy estimate received in April 2026 priced seven phases of cloud-sync work
(STC hook, conflict detection, auto-sync scheduler, Speckle, web 3D viewer, BCF
endpoints, mobile 3D context) at £20–28k. An audit against the live tree shows
six of the seven are already shipped — the estimate was working from out-of-date
assumptions about what the codebase contains. **This table is the authoritative
ground truth so future estimates do not re-bid these line items.**

| Estimate phase | Real status | Evidence | Open scope |
|---|---|---|---|
| 3 — `OnDocumentSynchronizedWithCentral` deferred-tag drain | **DONE** | `StingTools/Core/StingToolsApp.cs:87` subscribes; drain logic at `:185–243`. CHANGELOG entry 371 (R-02). | None |
| 4 — `LastModifiedUtc` last-write-wins conflict detection | **DONE** | Field on `TagElementPayload` (`StingTools/BIMManager/PlanscapeServerClient.cs:1042`); populated from `ASS_TAG_MODIFIED_DT` at `StingTools/BIMManager/PlatformLinkCommands.cs:2140`. Server side: migration `Planscape.Server/src/Planscape.Infrastructure/Data/Migrations/20250418000000_AddTagLastModified.cs` + conflict logic in `TagSyncController.cs:69–104` + `Planscape.Server/tests/Planscape.Tests/TagSyncConflictTests.cs` (184 lines). CHANGELOG Phase 91. | None |
| 5 — `SyncScheduler.Start()` 5-min auto-sync | **DONE** | Wired at `StingTools/Core/StingToolsApp.cs:95–118` via `PluginSyncTickBridge`; `DocumentSaved` enqueue at `:542–635`. CHANGELOG Phase 92 ("Activate Planscape.PluginSync.SyncScheduler"). | None |
| 6 — Speckle Send / Receive / Diff | **DONE** | Snapshot engine + three commands + `SpeckleSnapshot` workflow preset (Phase 92). HTTP transport `SpeckleHttpTransport` (~190 lines, raw GraphQL + multipart `/objects/{streamId}` upload, no Speckle SDK NuGet) added in Phase 161 — `Send` round-trips a single root `Base` with tag DTOs inline; `Receive` reads the latest commit on the target branch and overwrites the local snapshot. Stream URL parser supports both v2 `/streams/<id>[/branches/<name>]` and FE2/v3 `/projects/<id>[/models/<name>]` shapes. | None |
| 7 — xeokit / web 3D viewer | **DONE** | `Planscape.Server/src/Planscape.API/wwwroot/{index.html,viewer/,viewer.html,css/,js/}`; `app.UseStaticFiles()` in `Program.cs`; `ViewerController.cs` (99 lines, "PHASE 93 — xeokit-based model viewer") at `/api/viewer/models[/{filename}]`. | None |
| 8 — BCF 2.1 export/import endpoints | **DONE** | Shared engine: `StingTools/BIMManager/BcfEngine.cs` (~380 lines, `Planscape.Shared.BCF` namespace, no Revit / no Newtonsoft). Server controller: `Planscape.Server/src/Planscape.API/Controllers/BcfController.cs` (186 lines) — `GET /api/projects/{id}/bcf/export`, `POST /api/projects/{id}/bcf/import`, BcfGuid round-trip. CHANGELOG Phase 95. | None on the endpoint itself. (`BIM-BCF-SYNC-01` below tracks the separate ACC/Procore-pull half.) |
| 9 — Mobile 3D context in issue detail | **DONE** | Three complementary paths shipped: (a) **Phase 94** — fullscreen WebBrowser xeokit viewer via `openViewer(projectCode, modelId?)` in `issues.tsx`/`issue-detail.tsx`. (b) **Phase 162** — inline `<ModelViewer>` embed in `issue-detail.tsx` gated on `issue.modelId`; "Linked model" chip-row picker in `issues.tsx` creation form driven by `listModels(projectId)`; server `CreateIssueRequest` accepts `ModelId` + anchor coords with project-ownership validation. (c) **Phase 163** — closes Phase 162's three caveats: viewer's `onPlaceIssue` gesture deep-links into the (tabs) creation modal via `?fromViewer=1&modelId=...&modelX/Y/Z=...&modelElementGuid=...` (replaces the broken `/issues/new` push at `models/[id].tsx:96`); the inline embed pins every other open issue on the same model with `onPinTap` navigation; fullscreen `openIn3D`/`openViewer` route through `<modelId>.xkt` when the issue is linked, falling back to the project default otherwise. | None |

**Bonus already-shipped, missed by the consultancy estimate**: Sync-conflict triage UI
(`Planscape.Server/src/Planscape.API/Controllers/SyncConflictsController.cs`, 427 lines
+ mobile `Planscape/app/conflicts/` route, CHANGELOG Phase 143).

**Net remaining work for this whole bundle**: nothing — the last partial item
(Speckle HTTP transport) closed in Phase 161.

### Phase 165 closures (Write enhanced analysis and placement centre overhaul)

| ID | Status |
|---|---|
| ~~`NEW-02` Clash engine wiring audit~~ | **DONE** Phase 165: `ClashScheduler.Start` is now invoked from `StingToolsApp.OnDocumentOpened` for project documents (gated by the new `TagConfig.AutoStartClashScheduler` flag, default false because per-tick cost on large models is non-trivial). Idempotent across re-opens via `Stop()` before `Start()`. ClashSlaIntegration / ClashRunCommand / ClashHistory remain wired as they were. |
| ~~`NEW-08` Outbound webhooks~~ | **DONE** Phase 165: Server-side `OutboundWebhook` entity + `OutboundWebhookDispatcher` (HMAC-SHA256, single retry, per-row outcome) + `WebhooksController` (CRUD + `/test` synthetic fire). Wired into `IssuesController.CreateIssue` and `DocumentsController.TransitionState` fanouts. EF migration to mint the table is the only follow-up — DbContext + entity changes are in place so `dotnet ef migrations add OutboundWebhooks` will pick them up. |
| ~~`MOB-11` Dark mode~~ | **DONE** Phase 165: `src/theme/theme.ts` now persists user preference (light/dark/system) in AsyncStorage with listener-based notification. `_layout.tsx::checkAuth` calls `loadThemePref()` before first paint. `(tabs)/settings.tsx` Appearance card with three accessibility-labelled selectable buttons. Legacy `utils/theme.ts` corporate palette unchanged for gradual migration. |
| `INT-02` Dispatch registry framework | **PARTIAL** Phase 165: `UI/CommandRegistry.cs` ships the framework (ICommandModule + Register + TryHandle + lazy singleton); `ElectricalCommandModule` is the first migrated panel (4 tags). The `CommandRegistry.TryHandle` short-circuit at the top of `StingCommandHandler.Execute` lets new modules win over the giant switch. Remaining ~25 panels migrate panel-by-panel in subsequent phases. |
| `INT-01` HTTP client consolidation | Deferred (Phase 165 audit): `Planscape.PluginSync` is actively used by `PlatformLinkCommands.cs` (3 sites) + `StingDockPanel.xaml.cs` (3 sites). Deletion would require reworking ~600 lines without compile verification. The dual-layer state remains as documented in CLAUDE.md. |
| Fabrication doc-register auto-link | **DONE** Phase 165: `GenerateFabPackageCommand` now calls `FabricationDocRegister.PushSheets` so generated SP-* sheets land in `_BIM_COORD/document_register.json` automatically with suitability `S0`, status `WIP`. |

### Phase 168 closures (Unified project folder system)

| ID | Status |
|---|---|
| Messy folder structure (4 competing roots) | **DONE** Phase 168: `_BIM_COORD\`, `STING_BIM_MANAGER\`, `STING_Exports\`, `STING_Project\` consolidated into one `{ProjectCode}\` root with `_data\` subfolder for sidecar JSON. New `ProjectSetup` POCO + `FolderTemplateLibrary` (4 built-ins) + `ProjectFolderSetupDialog` (3-section WPF dialog) + `FolderHealthPanel` (per-folder status pills). `ProjectFolderEngine.MigrateFromLegacy(doc)` moves legacy folders + `.sting_*.json` sidecars into the new structure routed by extension. 10 sidecar callers redirected to `ProjectFolderEngine.GetDataPath(doc, "*.json")` with try/catch fallback. |

### Future Enhancement Gaps — Parameter System Architecture (Phase 168)

| ID | Description | Priority |
|---|---|---|
| TAG-01 | **Replace 128 TAG style BOOL parameters with single TAG_STYLE_CODE_TXT.** Current state: 128 universal YESNO parameters (`TAG_2NOM_BLACK_BOOL` … `TAG_3.5BOLDITALIC_WHITE_BOOL`) bound to every element in the model. Mutual exclusion enforced by `TagStyleEngine` code, not by the data model. Target state: 1 TEXT param `TAG_STYLE_CODE_TXT` (value e.g. `"2BOLD_BLUE"`) + 128 calculated BOOL formulas inside each tag family reading it. Net saving: 127 universal shared params off every element. Migration: requires updating every `.rfa` tag family file + one migration command to write `TAG_STYLE_CODE_TXT` from the currently-true BOOL on existing elements. **Effort:** Large (tag family rework). **Benefit:** Significant Revit performance on large models. | Medium |

### Future Enhancement Gaps — Project Folder System (Phase 168 follow-ups)

| ID | Description | Priority |
|---|---|---|
| FOLDER-01 | Cloud sync mapping — extend `ProjectSetup` with `CloudRoot` + `CloudProvider` fields so the `{ProjectCode}\` tree can mirror to ACC / SharePoint / Dropbox / OneDrive automatically when files land in `02_SHARED` or `03_PUBLISHED`. | High |
| FOLDER-02 | Free-text discipline entry — surface custom discipline names (beyond the fixed 8 A/E/M/P/S/FP/LV/Z list) in the setup dialog so projects can add bespoke discipline subfolders without hand-editing JSON. | Medium |
| FOLDER-03 | Multi-model workspace — when two `.rvt` files (e.g. `TROKON FRIEND.rvt` + `TROKON FRIEND 2.rvt`) share one project code, both should resolve to the same `{ProjectCode}\` root deterministically; today's sibling-folder scan in `LoadOrDetectSetup` is best-effort. | Medium |
| FOLDER-04 | Folder-watcher toggle — surface `ProjectFolderEngine.StartWatching` from the dockable panel so external Explorer drops auto-trigger CDE classification. | Low |
| FOLDER-05 | Schema versioning — add `SchemaVersion` to `ProjectSetup` + migration step so future schema changes can read old `project_setup.json` and upgrade in place. | Low |

### Future Enhancement Gaps — Healthcare Pack (H-1..H-30 follow-ups)

Items left open after the H-1..H-30 implementation sweep on branch
`claude/research-hospital-design-0Uxbi`. Each is additive — none
blocks a healthcare project from using the pack today.

| ID | Description | Priority |
|---|---|---|
| HC-01 | **WPF Healthcare tab** — the dock panel does not yet have a Healthcare ribbon / tab. Commands dispatch via `WorkflowEngine.ResolveCommand` + `StingCommandHandler` tags only; clinicians need a visible button surface. | High |
| HC-02 | **BIM Coordination Centre 14th tab** — design doc §12 specifies 9 RAG cards (Pressure / MGPS / EES / Water / Radiation / Anti-Lig / RDS / Adjacency / Commissioning Gantt) gated on `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT`. Not yet built. | High |
| HC-03 | **`healthcare_rds.docx` template authoring** — the field-map is committed but the .docx itself is a README placeholder. RDS render currently logs a "resource missing" warning. | High |
| HC-04 | **MGS family library** — 6 family stubs (Manifold / VIE / ZVB / AAP / MAP / TU) ship parameter specs only. Real `.rfa` from manufacturers. | High |
| HC-05 | **`TwinReadback` BACnet + OPC-UA transports** — `BacnetReadback` and `OpcUaReadback` are abstract stubs; they wire `IoTDeviceRegistry` correctly but never poll. Plug in `yabe`/CAS-BACnet and `opcfoundation/UA-.NETStandard` behind the existing interface. | High |
| HC-06 | **EF migration `HealthcarePack`** — 4 new entities + DbSets are registered but `dotnet ef migrations add HealthcarePack` not yet run against `Planscape.Server`. | High |
| HC-07 | **DocumentOpened morning briefing** — `StingToolsApp.OnDocumentOpened` runs `ComplianceScan` on open; should detect healthcare facility-type and surface healthcare validator counts in the status bar / morning briefing. | Medium |
| HC-08 | **`ProjectSetupWizard` healthcare branch** — wizard does not ask if the project is a healthcare facility; user must populate `PRJ_ORG_HEALTH_*` parameters by hand to unlock the validator chain. | Medium |
| HC-09 | **`MasterSetupCommand` healthcare extension** — the 15-step master setup does not load healthcare params or apply COBie-Healthcare overlay; manual COBie preset selection required. | Medium |
| HC-10 | **AutoTagger / StingStaleMarker healthcare categories** — `OST_SpecialityEquipment` is already in the IUpdater categories. Confirm `OST_MedicalEquipment` and `OST_NurseCallDevices` (Revit 2018+ healthcare-specific categories) are also in the IUpdater list so imaging modalities and nurse-call posts auto-tag and stale-mark. | Medium |
| HC-11 | **Mobile offline-queue integration** — healthcare screens POST directly. Failures trigger `Alert.alert` only. Queue them via the existing `OfflineQueue` so MGPS verifications / pressure logs / anti-lig audits captured offline land on the server when connectivity returns. | Medium |
| HC-12 | **HEALTHCARE_PACK_PROFILES.json UX** — gate works but no UI exposes `PRJ_ORG_HEALTH_PACK_PROFILE_TXT`. Add a project-info panel control so a coordinator can switch ACUTE → COMMUNITY → IMAGING-ONLY without text-editing. | Medium |
| HC-13 | **Healthcare workflow auto-run on open** — `AUTO_RUN_WORKFLOW_ON_OPEN` config exists; healthcare projects should default to `HealthcareCommissioning` (or the workflow appropriate for the active pack profile). | Low |
| HC-14 | **Issue tracker healthcare categories** — `BimIssue` accepts free-text categories; add a healthcare picklist (Infection-Control / MGPS-Defect / Anti-Lig-Failure / Calibration-Due / RDS-Drift). | Low |
| HC-15 | **HBN room-type catalogue auto-populator** — given a `CLN_ROOM_CLASS_TXT`, auto-populate the design ACH / pressure / temp / RH / NR / lighting lux from the standards lookup tables. Saves manual transcription per room. | High |
| HC-16 | **CommandRegistry framework migration** — `INT-02` introduced `ICommandModule`; healthcare commands could be a self-registering `HealthcareCommandModule` so the `StingCommandHandler` switch shrinks. | Low |
| HC-17 | **PARAMETER_REGISTRY.json container_groups** — tag-container metadata for the 5 new groups (CLN/MGS/RAD/CEQ/LIG) lives only in `TAG_CONFIG_v5_0_CONTAINERS.csv`. Mirror into the registry JSON so `TagPipelineHelper.WriteContainers` writes healthcare tag containers automatically. | Medium |
| HC-18 | **Briefcase / Sticky note hooks** — RDS / MGPS verification logs can be useful in the briefcase. Audit `BriefcaseAddFile` to ensure healthcare folders are scannable. | Low |
| HC-19 | **iHFG (TAHPI) fully populated** — `Standards/iHFG/` not built; international healthcare projects fall back to FGI defaults. | Medium |
| HC-20 | **WHTM / SHTM / NHS-NI variant tables** — `PRJ_ORG_HEALTH_HTM_REGION_TXT` accepts the variant codes but the standards modules carry only NHS England HTMs; regional variants need their own lookup tables. | Low |
| HC-21 | **STING Healthcare Title Block family** — 22 healthcare drawing types reference `STING - Healthcare Title Block`. Family is not authored yet; existing default title block falls through. | Medium |
| HC-22 | **SignalR HealthcareHub for live pressure cascade** — `pressure-live.tsx` mobile screen renders empty state until a SignalR hub is added server-side and the BACnet bridge pushes live Δp. | Medium |
| HC-23 | **CCTV / observation LOS computational geometry** — `LIG_AREA_OBS_LOS_TXT` is a TEXT code today. A proper visibility-cone solver would let the validator compute LOS percentage automatically against `MinObservationLOSPercent` per behavioural-health room class. | Low |
| HC-24 | **Pneumatic tube / AGV path optimiser** — design doc §11 H-10 mentions a path planner; current implementation only does adjacency BFS. AGV path optimisation against `Core/Routing/DropEngineBase` is a future enhancement. | Low |
| HC-25 | **iHFG / FGI 2026 facility code adoption tracking** — FGI 2026 transitions from guidance to enforceable code. As clauses become mandatory in jurisdictions, validators should escalate from `Warning` to `Error` automatically. | Low |
