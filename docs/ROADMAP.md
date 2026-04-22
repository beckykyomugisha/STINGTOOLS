# ROADMAP — STINGTOOLS

Open automation gaps, future-enhancement tables, and deep-review findings for the StingTools plugin. See [`../CLAUDE.md`](../CLAUDE.md) for current architecture and [`CHANGELOG.md`](CHANGELOG.md) for the history of closed items.

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
| FM-HO-01 | COBie Contact/Attribute/Job/Resource sheets | High | HandoverExportCommands.cs COBie export missing 4 of 11 COBie V2.4 required worksheets |
| FM-HO-02 | Phase-aware COBie export | High | Multi-phase models cannot differentiate asset lifecycles per phase in COBie Component sheet |
| WF-SCHED-01 | Schedule template library | Medium | No schedule template save/load/apply system for standardized schedule creation |
| WF-SCHED-02 | Cross-schedule field consistency | Medium | No validation that different schedules using same field have consistent naming |
| UI-DISP-01 | Dispatch registry pattern | Low | Refactor 1142-case switch to dispatch registry with per-module command registrations |
| DOC-REG-01 | Drawing register ISO 19650-2 fields | Medium | Missing CDE status, suitability code, approval history in drawing register export |
| DWG-MULTI-01 | Multi-layer wall detection | Medium | DWG wizard doesn't detect dual-layer wall encoding (exterior + interior leaf pairs) |
| DWG-CURVE-01 | Curved wall support | Low | Arc segments in DWG wall layers not detected; only straight lines converted |
| MEP-SCHED-01 | MEP commissioning schedules | Medium | Missing connector flow rate, balancing status, pressure drop summary schedules |
| STRUCT-REBAR-01 | Rebar spacing validation | Medium | No pre-check that rebar spacing exceeds bar diameter before design output |
| PERF-WARN-01 | Warning regex compilation | Medium | 150+ regex patterns evaluated linearly per warning; pre-compile into Regex[] array |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus | Medium | Double-leaf acoustic calculation uses static 10dB cavity bonus instead of frequency-dependent lookup |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | Critical | System worksheet uses TagConfig.SysMap defaults, not actual element SYS token distribution |
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement | Critical | CDE transitions allowed without required ISO 19650-2 §5.6 role-based approval hierarchy |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal cross-linking | Critical | Issues, revisions, transmittals stored as independent JSON silos with no foreign key references |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | Critical | Dialog exits after single action instead of staying open for iterative BIM coordinator workflow |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | Critical | Excel import reads entire .xlsx into memory causing OOM on large models |
| BIM-COBIE-SHEETS-01 | Missing COBie Contact/Facility/Floor/Space worksheets | High | Only 7 of 11 required COBie V2.4 worksheets generated |
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | High | No deliverables tracker for DD1-DD4 milestones in coordination center |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | High | Revision creation does not auto-update REV parameter on all tagged elements |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | High | Import allows invalid FUNC/SYS combinations (e.g., FUNC=PWR on SYS=HVAC) |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | High | Dashboard cannot project when compliance target will be reached |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | High | Users must manually create WIP/SHARED/PUBLISHED/ARCHIVE folders per project |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | High | BCF export works but no import mechanism for changes from ACC/Procore |
| BIM-4D-HANDOVER-01 | 4D schedule linked to document handover dates | Critical | Schedule shows "complete" on construction finish; ISO 19650 DD4 handover not tracked |
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | Medium | No version field in sidecar JSON files; future field additions break older files |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | Medium | Transmittals never validated for minimum CDE state before sending |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | Medium | No way to see per-member issue/task distribution for resource balancing |
| TAG-CACHE-01 | Parameter cache key instability | Critical | Cache key using doc.GetHashCode() changes across sessions causing stale reads; use stable PathName key |
| TAG-AUTOTAG-NULL-01 | AutoTagger PopulationContext null crash | Critical | PopulationContext.Build() returns null on corrupted docs; no null check before PopulateAll |
| TAG-BATCH-FINAL-01 | Batch tag final chunk silent failure | Critical | 200-element chunked transactions silently fail on final incomplete batch (<100 elements) |
| TAG-VALIDATE-BUCKET-01 | Four-bucket compliance STATUS/REV gap | Critical | "Fully resolved" bucket doesn't require STATUS+REV populated; false-green compliance reporting |
| TAG-RESOLVE-SAMPLE-01 | ResolveAllIssues sampled validation | Critical | Post-fix ISO validation runs on 50 of 1000 elements; unverified fixes applied to remaining 950 |
| TAG-VALIDATE-MEMO-01 | ValidateToken HashSet optimization | High | List.Contains O(k) → HashSet O(1) for token validation; 400x faster for 50K-element models |
| TAG-SORT-LEVEL-01 | SmartSort level elevation recalculated per batch | High | Level elevation dictionary rebuilt per 500-element batch; should be built once per document |
| TAG-PREFLIGHT-DUP-01 | Pre-flight and main loop duplicate spatial indexing | High | PopulationContext.Build() called twice (pre-flight + tagging); reuse context from pre-flight |
| TAG-DEFERRED-OVERFLOW-01 | AutoTagger deferred queue overflow silent drop | High | 5000-element queue overflow silently drops elements; need warning + retry sidecar |
| TAG-SEQ-SIDECAR-DRIFT-01 | SEQ sidecar/model counter divergence on cancel | High | Cancel during batch N leaves sidecar at N but model at N-1; counters diverge by 500 |
| TAG-ISO-USERNAME-01 | ISO 19650 contributor tracking in audit trail | High | TAG_HISTORY logs timestamp but not username; ISO requires "person responsible" traceability |
| TAG-STALE-WARN-01 | Stale elements not auto-creating warnings | Medium | Stale flag set by IUpdater but not fed into WarningsEngine pipeline automatically |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization | Medium | Independent workflow steps execute sequentially; DAG-based dependency ordering would halve execution time |
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
- **FM-HO-01 (COBie sheets)**: COBie handover export already generates all 12 sheets (Facility, Floor, Space, Type, Component, System, Zone, Contact, Attribute, Job, Resource + Instruction)
- **BIM-COBIE-SHEETS-01**: Same as FM-HO-01 — already complete

### Remaining Future Enhancement Gaps (Phase 78 Triage)

After verification, 15 of 44 gaps were confirmed as already implemented or false positives. The remaining 29 gaps are prioritized below:

**CRITICAL (should implement before handover):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement per ISO 19650-2 §5.6 | Documented |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal JSON cross-linking | Documented |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | Documented |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | Documented |
| BIM-4D-HANDOVER-01 | 4D schedule linked to DD4 handover dates | Documented |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | Documented |

**HIGH (should implement for production):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | Documented |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | Documented |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | Documented |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | Documented |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | Documented |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | Documented |
| TAG-SORT-LEVEL-01 | SmartSort level elevation cached per document | Documented |
| TAG-PREFLIGHT-DUP-01 | Reuse PopulationContext from pre-flight in main loop | Documented |

**MEDIUM (enhancement quality):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | Documented |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | Documented |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | Documented |
| TAG-STALE-WARN-01 | Stale elements auto-creating warnings | Documented |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization via DAG | Documented |
| DWG-MULTI-01 | DWG multi-layer wall detection | Documented |
| DWG-CURVE-01 | Curved wall support from DWG arcs | Documented |
| WF-SCHED-01 | Schedule template library (save/load/apply) | Documented |
| WF-SCHED-02 | Cross-schedule field consistency validation | Documented |
| MEP-SCHED-01 | MEP commissioning schedules | Documented |
| STRUCT-REBAR-01 | Rebar spacing validation (spacing > bar diameter) | Documented |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus in double-leaf Rw | Documented |


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
Actual content authored in `STING_TAG_CONFIG_v5_0_{ARCH,GEN,MEP,STR}.csv` is a subset.

| Gap | Title | Status |
|-----|-------|--------|
| TAG-LABEL-T4-10 | Author T4-T10 label rows across all tag families. Current data: 170 T1 + 751 T2 + 744 T3 + Warning rows. T4-T10 rows: **0**. Picking .04-.10 in the ParaDepth UI renders identically to .03 until rows exist. | Open |
| TAG-LABEL-STYLE-COLS | Populate per-row `Style` / `Color` / `Size` columns in the v5.2 schema. Current data: **0/1,665** rows populated — every row inherits the tier default (T1=NOM/2.5, T2=NOM/2.0, T3=NOM/2.0, all BLACK). Overrides like "BOLD BLUE for 7A headers" are documented (TAGGING_WORKFLOW_GUIDE.md §6) but not yet written into the CSVs. | Open |
| TAG-LABEL-BOX-ARROW-COLS | Extend the v5.2 schema to include per-row `Box` and `Arrow` columns, mirroring the tag family variant naming `{size}_{style}_{colour}_{arrow}_T{n}`. Today these only exist as type parameters (`TAG_BOX_*`) and catalogue arrowhead names — no per-row authoring path. | Open (schema + content) |
