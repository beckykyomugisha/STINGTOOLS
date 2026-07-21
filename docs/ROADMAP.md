# ROADMAP — STINGTOOLS

Open automation gaps, future-enhancement tables, and deep-review findings for the StingTools plugin. See [`../CLAUDE.md`](../CLAUDE.md) for current architecture and [`CHANGELOG.md`](CHANGELOG.md) for the history of closed items.

## Drawings-production deep review (2026-07-20)

Full-surface review of the Drawing Type engine, corporate catalogue, View Style Packs + AEC
filters, title blocks, annotation/legends/match lines, sheet production pipeline, and command
wiring: **~85 findings (5 Critical / 27 High)** with a prioritised P0–P2 remediation plan.
See [`DRAWINGS_PRODUCTION_REVIEW.md`](DRAWINGS_PRODUCTION_REVIEW.md).

### P0 — CLOSED (see CHANGELOG Phase 223)

| Finding | Status |
|---|---|
| C-1 unbound style-pack JSON keys (`filterRules`, short-form vgOverrides) | ✅ fixed — POCO aliases; filter rules bound 19 → 97 |
| C-2 / E-1 both `ResolveExtends` folds strip fields | ✅ fixed — generic default-aware overlay |
| C-3 producer sheet key ignores context | ✅ fixed — `STING_SHEET_CONTEXT_TXT` in the sheet key |
| W-1 two penetration workflow presets inert | ✅ fixed — step keys + 3 `ResolveCommand` cases |
| A-11 `LABEL_DEFINITIONS.json` mojibake | ✅ fixed — 530 strings / 1,171 chars repaired |
| V-3 material-class filter AND-ed instead of OR-ed | ✅ fixed — `LogicalOrFilter` |
| V-7 / V-8 dead filter definitions | ✅ fixed — 12 filters (8 `Family Name`, 2 ops, 2 compound schema) |

Found while fixing P0, **not** in the review:

- **`surfFgColor` (15×) and `projLinePattern` (5×) were also unbound** on `StyleFilterRule` —
  the Phase 139 alias pass covered the four line keys and stopped short of these. Fixed with C-1.
- **`plumb-high-pressure-zone` and `plumb-backflow-risk` use a third rule schema**
  (`{"kind":"compound","op":"or","operands":[…]}`) that binds to neither `IsLeaf` nor
  `IsCompound`, so `BuildFilter` returned null silently. Fixed with V-8.
- **`DrawingType.extends` could never inherit any field with a non-null POCO default**
  (`PaperSize` "A1", `Discipline`/`Phase` "*", `Purpose` "Plan", `Orientation` "Landscape",
  `Scale` 100, `DetailLevel` "Medium", both sheet patterns): the old `!IsNullOrEmpty` guard
  treats a child's default as a value, so an A0 parent always resolved back to A1. Fixed with
  E-1. Known residual limitation: a child that explicitly restates a default is
  indistinguishable from one that omits it and will inherit the parent's value.
- **`ViewStylePack`'s six already-working scalars keep their original guards** — that path runs
  on every `Get()` and changing `lineWeightScale` merge semantics would alter rendering. The
  same default-clobbers-parent issue therefore remains open for style packs specifically.
- **Duplicated `OST_Sheets` insertion** in `LoadSharedParamsCommand` (two identical try blocks,
  ~lines 333–349). Harmless, not fixed.

### P1 — managed-template hardening (done, prerequisite of C-2)

| Finding | Status |
|---|---|
| E-3 `CopyElement` on a non-template seed → junk views re-minted each run | ✅ fixed — `View.CreateViewTemplate()` + cleanup on failure |
| E-4 hardcoded `VIEW_DISCIPLINE` ints | ✅ fixed — reads `ViewDiscipline` members |
| E-6 discarded managed parameter-id list | ✅ fixed — `SetNonControlledTemplateParameterIds` complement |

The review mis-stated E-4: `Coordination = 4095` was already correct and `Mechanical = 4096` was
not. `VIEW_DISCIPLINE` is a bit-flag parameter (Architectural 1, Structural 2, Mechanical 4,
Electrical 8, Plumbing 16; Coordination 4095 = all bits). The genuine defects were Mechanical,
Electrical and Plumbing.

### P1 — issued-output correctness (done)

| Finding | Status |
|---|---|
| D-1 / P-2 / P-10 producer token substitution + numbering | ✅ fixed |
| T-1 composer never called `ToConcreteFamily` | ✅ fixed |
| T-2 blank-name match-anything + silent arbitrary fallback | ✅ fixed (two defects) |
| T-3 `PRJ_TB_LOCK_BOOL` unread by the declarative pipeline | ✅ fixed (skip-and-report) |
| T-12 issue summary clobbered by revision description | ✅ fixed (write removed) |
| C-4 AnnotationRunner had no idempotency | ✅ fixed (tags / dims / decorative) |
| A-6 dog-leg pair key never matched on re-run | ✅ fixed |
| A-7 captions stacked every sync | ✅ fixed |
| A-8 silent tag-family fallback | ✅ fixed (warns) |
| A-9 `GetElementCentre` null passed to `IndependentTag.Create` | ✅ fixed (bbox centre) |
| A-3 legend refresh minted a blank twin | ✅ fixed (in-place, both branches) |
| P-5 section frames wrong and divergent | ✅ fixed (one shared builder) |
| E-2 crop assigned in model space | ✅ fixed (converted to crop frame) |

Corrections to the review found while fixing these:

- **E-4 was wrong in both directions.** `VIEW_DISCIPLINE` is a bit-flag parameter —
  Architectural 1, Structural 2, Mechanical 4, Electrical 8, Plumbing 16, Coordination 4095
  (all bits). So the shipped 4095 the review flagged as invalid was correct, and the 4096 it
  called "the only correct one" was not. Real defect: Mechanical / Electrical / Plumbing.
- **A-6's second clause is incorrect.** `PruneOrphans` does not need the segment-key
  discipline: it takes the scope-pair GUID from the first colon-delimited field, identical in
  the stamped and collapsed forms, so orphan segments pruned correctly before the fix.
- **T-2 is two defects.** The `IsNullOrEmpty(tbFamily) ||` clause matched any loaded title
  block and returned before the documented silent-fallback path could run.
- **T-7's `Family Name` filters carry no `kind` field at all**, rather than `kind: builtin`.

Deliberately deferred, with reasons:

- **Dimension idempotency uses reference-category detection, not a stamped marker.** A marker
  needs a new parameter bound to Dimensions (4-file provisioning). Residual: a dimension
  reporting `AreReferencesAvailable == false` is treated as unknown, so a duplicate remains
  possible if every dimension in a view is unreadable AND a prior chain exists.
- **Match-line captions are identified by (view + type + caption-template shape + proximity)**,
  since `STING_MATCH_LINE_GUID_TXT` does not bind to Text Notes. Template-shape matching (not
  exact text) is what makes the post-renumber case work — the caption embeds the sheet number,
  so exact text would miss every stale caption after a renumber, which is precisely when
  `MatchLine_Sync` is run. Residual: a caption whose boundary geometry moved is orphaned rather
  than deleted.
- **P-5 / E-2 could not be verified outside Revit.** Both take Revit geometry types and
  RevitAPI.dll is mixed-mode, so it will not load in a bare host. Testing a re-implementation
  would assert against a copy, not the shipped code, so they rest on structural argument plus
  the Revit smoke test.
- **`IFamilyLoadOptions` `overwriteParameterValues` disagreement** (resolver false,
  TitleBlockSlotCommands true) — untouched, since no loading behaviour changed.

### P2 — tracks A + D CLOSED (see CHANGELOG Phase 224)

| Finding | Status |
|---|---|
| E-5 / E-13 SheetSequenceStore wipe, silent write failure, Peek off-by-one | ✅ fixed |
| A-4 / A-10 grid + level dimension chains never placed | ✅ fixed |
| P-6 / P-13b / P-13c scope-box production | ✅ fixed |
| P-8 producer schedule placement + per-slot viewport types | ✅ fixed |
| P-9 re-run warnings + double counting + ctx.Tag divergence | ✅ fixed |
| E-7 / E-8 / E-9 / E-10 / E-12 / E-14 / V-9 engine small-bore | ✅ fixed |
| A-12 / A-13 / A-14 legend placement + match-line validate | ✅ fixed |
| T-8 / T-9 / T-11 / T-13 title-block loose ends | ✅ fixed |
| V-4 / V-5 / P-12 / P-13a performance | ✅ fixed |
| A-5 GridDimensioner axes + drainage invert | ✅ fixed |
| W-2 MatchLine suite unreachable | ✅ fixed (5 buttons + 5 ResolveCommand cases) |

Corrections to the review found in this pass (bringing the running total to six):

- **W-5 was wrong and acting on it would have been destructive.** The instruction was to delete
  the inline handler copies for `DrawingTypes_SyncStyles` / `DrawingTypes_FromScopeBoxes` as
  "reimplementations". They are not: the inline versions are read-only (an audit and an
  advisory) while the command classes WRITE. Deleting them would have turned two read-only
  buttons into model-writing operations. Resolved by naming instead — the advisories moved to
  `DrawingTypes_AuditStyleRefs` / `DrawingTypes_SuggestFromScopeBoxes`, the canonical tags now
  mean the command class everywhere, and new buttons expose the writing commands.
- **P-7's conclusion does not follow from its evidence.** The two slot data sets are authored to
  DIFFERENT conventions, each self-consistent with its consumer: `STING_DRAWING_TYPES.json` is
  bottom-left (pipe-spool tiles 0.05→0.45, 0.55→0.95; every x+w ≤ 0.98) while
  `SheetTemplateEngine`'s built-ins are centre (normX 0.47/w 0.80, 0.72/w 0.38, 0.72/w 0.40 give
  right edges of 1.27, 1.10, 1.12 under a bottom-left reading — three of four off-sheet). The
  defect is the adapter's claim that they share a convention. Still open; see below.
- **A-5's drainage magnitude is wrong.** The error is one wall thickness, not two: that would
  require `Diameter` to be the outside diameter, and Revit exposes no inside/outside distinction
  on `Pipe` or `MEPCurve`.
- **E-10 is worse than described** — the `Validate` loop sat outside the `try`, so the snapshot
  clear was skipped on any throw, not merely un-stamped.

### P2 — still open

**Track B — CLOSED except the two data-territory items.**

| Item | Status |
|---|---|
| B1 grid-dimensioning convergence | ✅ `DimGrids` survives; `GridDimensioner` reduced to `IsDimensionable` |
| B1 `dimensionStrategy` | ✅ wired into `DimGrids` via `DimensionStrategy.ResolveType` |
| B1 `condition` | ✅ wired (evaluator had zero call sites) |
| B1 per-rule `tagFamily` | ✅ wired, warns when the named family is absent |
| B1 `densityMode` / `minSizeMm` / `orientation` / `tag7Depth` | ⬜ still no-ops — see below |
| B2 MatchLine reachability | ✅ 5 buttons + 5 ResolveCommand cases |
| B3 `${MAT_*}` tokens | ✅ wired; usage scan memoised per doc |
| B5 composer numbering | ✅ routed through `SheetSequenceStore` |
| B4 checksums | ⬜ data-territory — data PR |
| B6 unmintable `iso-status-*` filters | ⬜ data-territory — data PR |

`densityMode` / `minSizeMm` / `orientation` / `tag7Depth` remain unwired. They are per-rule
*refinements* to tagging behaviour rather than the on/off wiring the other fields needed
(`densityMode` interacts with the existing `denseUntilScale` gate; `minSizeMm` needs a per-element
size measure; `orientation` and `tag7Depth` need per-tag write paths). Each is a small feature in
its own right and none of them silently corrupts output today — they simply do nothing — so they
are better scoped against smoke-test evidence than guessed at.

**Previously listed as remaining (now done):**

- **B1 (rest of the rule engine).** `GridDimensioner`'s axis bug and the drainage invert are
  fixed, but `AnnotationConditionEvaluator` still has no call site and the rule-pack fields
  `condition` / per-rule `tagFamily` / `densityMode` / `minSizeMm` / `orientation` / `tag7Depth`
  / `dimensionStrategy` remain silent no-ops on 48 shipped types. Recommendation: wire, not
  delete — deleting means stripping the fields from 48 catalogue entries, which is a Track C
  data edit. Note that A2 rewrote `DimGrids` correctly, so wiring `dimensionStrategy` would give
  two implementations of grid dimensioning; converge them in the same pass.
- **B3 `${MAT_*}` tokens (T-4).** `MaterialTitleBlockTokens.Resolve` still has zero callers.
  One call site in `TitleBlockParamApplier`'s `${…}` handler would wire it; its O(all-elements)
  usage scan should be made lazy-once-per-doc at the same time.
- **B4 checksum persistence (C-5).** Unchanged — no checksum fields ship, so the corporate-lock
  drift branch is inert. Needs a build-time stamping script plus a decision on whether packs get
  parity or are documented as deliberately unlocked.
- **B5 composer numbering (P-11).** `ShopDrawingComposer._sequenceByBucket` is still in-memory
  per session. Routing it through the (now hardened) `SheetSequenceStore` would retire the third
  parallel numbering system.
- **B6 unmintable filters (V-6).** The 8 `iso-status-*` filters bind only `OST_Sheets`, which
  Revit view filters cannot target. Removal is a Track C data edit.

**Track C (catalogue data) — not started**, gated on smoke-test evidence. C1–C6 as originally
scoped. Note C3's overlap is confirmed by measurement: pipe-spool ISO spans 0.55→0.95 and BOM
0.78→0.98, overlapping 0.78→0.95.

**P-7 slot convention** — the divergence is measured and recorded above; converging requires
rewriting `SheetTemplateEngine`'s built-in numbers and migrating user-saved templates, so it
wants smoke-test evidence first.


Everything else in the review remains open, notably: D-1/P-2/P-10 token substitution and
numbering; T-1/T-2/T-3 title-block resolution, silent fallback and `PRJ_TB_LOCK_BOOL`;
C-4/A-6/A-7 annotation and match-line idempotency; A-3 legend in-place refresh; P-5/E-2 section
frame and crop coordinates; C-5 inert corporate-lock checksums; W-2 unreachable MatchLine suite;
W-3/W-4/W-5/W-6 wiring and documentation drift.

## Revision system — deferred items (Phase 199)

Recorded while aligning the revision subsystem (see CHANGELOG Phase 199).

- **Rebind title-block revision labels to built-in parameters, then delete the TB half of the
  syncer.** The revision box currently reads STING shared params (`PRJ_TB_REVISION_NR_TXT` /
  `_DATE_TXT` / `_DESCRIPTION_TXT`) that a command must keep in sync. Revit exposes built-in
  **Current Revision / Current Revision Date / Current Revision Description** parameters on
  title blocks that it maintains itself. Rebinding the catalogue's revision-box labels to those
  built-ins makes the drawing correct with **zero sync** — no command run, no drift window, no
  stale box if someone forgets to click. Once rebound, `TitleBlockRevisionSyncer` can drop its
  title-block writes entirely and keep only the `SHT_REV_TXT` / `SHT_REV_DATE_TXT` sheet stamps
  (which feed schedules and exports, and have no built-in equivalent). Scope: a catalogue
  migration across the affected families plus a factory change — deliberately out of scope for
  Phase 199, which did not mass-edit the 206-family catalogue.
- **Consolidate the three revision-cloud implementations.** `AutoRevisionCloudCommand`
  (`BIMManager`), `DocAutomationExtCommands.RevisionCloudAuto` (`Docs`), and
  `MaterialRevisionCloudJob` (`Core`) each independently decide what "changed" means, how clouds
  are grouped, and which view they land in. Fold them onto one shared engine (change-set in →
  clouds out) so the three entry points stay behaviourally identical and a fix to cloud grouping
  lands once.
- **Data-drive the LG-03 per-discipline auto-revision thresholds.** `AutoRevisionOnTagChangeCommand`
  carries a hardcoded per-discipline threshold dictionary. Move it into a JSON data file with a
  project override (same corporate-baseline + `_BIM_COORD` override pattern the drawing types and
  sizing rules already use), so a project can tune how many tag changes trigger an auto-revision
  without a code change.

## MEP print-readiness — deferred items (Phase 198)

Recorded while making the MEP drawing types print-ready (see CHANGELOG Phase 198).

- **Fire suppression drawing types (fast-follow).** Only fire *detection* exists today. There are no
  sprinkler / suppression **layout**, **section**, or **detail** drawing types (corporate DrawingType +
  routing + a fire style pack). Author them as a fast-follow so a fire-suppression package is
  drop-a-view print-ready like M/E/P. Scope: a `corp-standard-fire` style pack (sprinklers + fire-alarm
  bold `#C00000`, other MEP + arch/struct halftone), `fire-sprinkler-layout` / `fire-section` /
  `fire-detail` types with `${PRJ_ORG_*}` title-block params + `tagFamilies` (`STING - Sprinkler Tag`,
  `STING - Fire Alarm Device Tag`), and `F/*/SPRINKLER|SECTION|DETAIL` routing rules.
- **SEQ zero-pad is project-global by design (not per-DrawingType).** SEQ pad lives in
  `TagConfig.SeqPadWidth` / `EffectiveSeqPad`, set from the Tag Studio → Tokens & Depth dock tab, and
  applies project-wide. It was **deliberately not** made an `AnnotationTokenProfile` field, so a
  DrawingType cannot override it per drawing. If per-type SEQ pad is ever wanted, add a `seqPad` field to
  `AnnotationTokenProfile` and push it on the produce path (mirroring how paragraph depth already flows),
  with the project-global value as the fallback.
- **Arch / structural / health `tagFamilies` debt (NOT fixed — separate scope).** The Phase-198 punch-list
  cross-check found the same key-alignment + family-existence defects the MEP fix cleared, still present on
  **non-MEP** drawing types: ~87 `tagFamilies` key↔AutoTag-category mismatches (camelCase keys like
  `StructuralColumns` that never match the display-name rule category), **19 `STING_TAG_*`
  non-existent family values across ~14 arch/structural types**, and `STING - Generic Tag` (should be
  `STING - Generic Model Tag`) on ~22 healthcare types. So "MEP print-ready" ≠ "whole file fixed". Real
  target families exist (`STING - Door/Window/Room Tag`, `STING - Structural Column/Rebar Tag`,
  `STING - Generic Model Tag`), so the same mechanical fix applies — do it only when explicitly scoped as
  an arch/struct/health pass (out of scope for the MEP runner).

## Ambiguous parameter bindings — needs SME confirmation (Phase 196)

The Phase 196 binding-accuracy fix narrowed every confidently-classifiable discipline family and
preserved the universal set. The parameters below remain bound to the **broad core set** (they had no
per-param `CATEGORY_BINDINGS.csv` row and no group override) and are logged as coverage **GAPs** by
`LoadSharedParamsCommand`. They are semantically mis-located (space/zone/landscape/structural-cost
params filed under the `COM_DAT` communications group) — narrowing them requires an SME decision on the
correct element domain, so they were **not** guessed. Recommended categories noted for review; once
confirmed, add rows to `CATEGORY_BINDINGS.csv` (+ `PARAMETER_CATEGORIES.csv`) and re-run
`Bindings_PruneToSpec`.

| Param(s) | Group | Recommended domain (needs confirmation) |
|---|---|---|
| `SPC_GROSS_AREA_M2`, `SPC_CEILING_HEIGHT_M`, `SPC_MIN_HEADROOM_M`, `SPC_FINISH_FLOOR/CEILING/WALLS_TXT`, `SPC_PUBLIC_ACCESSIBLE_BOOL` | COM_DAT | Rooms, Spaces |
| `ZON_CATEGORY_NAME/CODE_TXT`, `ZON_NET/GROSS_AREA_M2`, `ZON_VOLUME_M3`, `ZON_OCCUPANTS_NR` | COM_DAT | Areas, Spaces, HVAC Zones |
| `GEN_SPECIES_TXT`, `GEN_HEIGHT_AT_MATURITY_M`, `GEN_ROAD_TYPE_TXT`, `GEN_TAG_7_PARA_MISC_TXT` | COM_DAT | Planting, Site, Roads |
| `CST_S_FRM_FORMWORK_AREA_SQ_M`, `CST_S_MAS_BLOCKS_NR`, `CST_S_MAS_NET_WALL_AREA_SQ_M`, `CST_S_REI_LAP_LENGTH_MM`, `CST_S_REI_TOTAL_WEIGHT_KG`, `CST_S_SUPPLIER_TXT`, `CST_FORMWORK_TYPE_TXT`, `CST_PLYWOOD_SIZE_TXT`, `CST_SAND_MOISTURE_TXT` | COM_DAT | Structural Framing/Columns/Foundations, Walls, Floors |
| `SLV_TAG`, `SLV_TAG_7_PARA_TXT` | SLV_SLEEVE_PARAMS | Generic Models (matches rest of `SLV_*` family) |
| `COMP_TAG_1_TXT` | COM_DAT | universal composite tag — likely keep broad |

Additionally, the healthcare families `MGS_` / `RAD_` / `CLN_` / `CEQ_` were bound to reasoned
discipline domains (medical-gas piped set, radiation-shielding set, clinical room/equipment set) that
exclude all cross-discipline conveyance categories, but the **precise per-param category refinement**
(e.g. which exact device vs. room vs. equipment category each WARN_/DESIGN_/TAG param belongs to)
should be SME-confirmed against HTM/HBN modelling conventions. See `docs/binding_audit_report.csv`.

## Universal Tag pivot — Task 4 legacy cleanup (DEFERRED, branch `feature/universal-tag-system`)

Tasks 1-3 of the universal-tag pivot landed (propagation command, status gates, tag-expander
schedules). **Staged cutover steps 1-3 are now done** — step 1-2 in Phase 196
(`claude/universal-tag-finalize`) and the **teardown in Phase 197** (`claude/universal-tag-teardown`):
`TagFamilyCreatorCommand` ("Create Tag Fams") was gutted of its CSV tier-authoring path (labels now
come from `Propagate_UniversalTag`), which removed the last live callers of `FamilyLabelAuthor` and
`TagConfigPlanResolver`; both files were then **deleted** (0 callers verified). **Steps 4-5 remain**
(repurpose `HandoverModeHelper` DC/HO; deprecate the colour-scheme commands) — those still have live
callers and are separately gated.

| Candidate | Status | Notes |
|---|---|---|
| ~~`Tags/FamilyLabelAuthor.cs`~~ | **DELETED (Phase 197)** | 0 code callers after the Create Tag Fams gut. Nested `Options`/`ModePlan` went with it. |
| ~~`Tags/TagConfigPlanResolver.cs`~~ | **DELETED (Phase 197)** | 0 code callers after the gut. `TierPlan`/`TierState` live in `Core/PerFamilyTierMap.cs` (retained). |
| `Core/TagConfigCsvReader.cs` + `Data/STING_TAG_CONFIG_v5_0_*.csv` | **RETAINED** | The reader's `TierPlan` API (`LoadFile`/`LoadFiles`/`Parse`) is now caller-less (its consumer `TagConfigPlanResolver` was deleted), but the **v5.0 CSV data** it parses is the canonical *synced* source (per `reference-tag-config-sources`) and is still read by `ParamRegistry` / `TagConfig` / `HandoverModeHelper` / `PresentationModeCommand` / `FamilyParamCreatorCommand` / `LpsValidator` via their own paths. Marked `LEGACY(universal-tag)` in-file; a future pass can rewire a reader onto the typed parser or retire it with the CSVs together. **Do NOT delete the data files — still parsed.** |
| `Core/HandoverModeHelper.cs` (DC/HO) | **RETAINED** | Live callers: `StingToolsApp.GetAllTagConfigCsvs`, `TagConfig.GetTagConfigCsv` (×3), `ApplyParagraphPresetCommand.GetSelectorBool`/`ModeSelectorBool`; `GetActiveMode` used internally. It's a mode/CSV resolver, not pure-legacy. Step 4 (repurpose DC/HO → `PARA_STATE` view preset) still open. |
| `Core/TagStyleCatalogue` colour dims + `Tags/TagStyleEngine.cs` + `Tags/TagStyleCommands.cs` | **RETAINED** | `TagStyleEngine.ResolveTagTypeForPlacement` used by `StingAutoTagger` + `SmartTagPlacement` (6 sites); colour commands wired to live buttons. Keep DEPTH-variant logic (also in `TagTypeVariantWriter`). Step 5 (colour-scheme deprecation) is a separate surgical refactor. |
| ~~`MigrateTagFamiliesCommand` tier-authoring path~~ | **DONE (Phase 196)** | Trimmed to param + type-variant migrator. |
| ~~`TagFamilyCreatorCommand` tier-authoring path~~ | **DONE (Phase 197)** | Gutted to mint (shell + params + variants); label via `Propagate_UniversalTag`. Dead alias helpers (`CsvFamilyNameCandidates`/`TryGetTierPlan`/`ContainsPlanForFamily`) removed. |

**Prerequisites now in-repo (tracked):**
- `docs/UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` — the consolidated manual walkthrough: what to
  DELETE (T3 + discipline + warning rows), how to BUILD the 65 rows, and the status-badge system.
- `docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — the authoritative 62-row master-label build guide
  (human-authored in the Family Editor; the API can't do it).
- `docs/UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` — the precise Duct smoke-test checklist (the step-1 gate).
- `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md` — the ready-to-apply step-2 trim of
  `MigrateTagFamiliesCommand` (staged; apply only after the smoke test passes).

**Staged cutover (do in order, each gated):**
1. ~~Prove the universal path in Revit (Duct smoke test for `Propagate_UniversalTag`)~~ —
   **DONE.** Recategorise preserves labels/formulas/breaks (proven live on Duct).
2. ~~Retire the OLD authoring ENTRY POINTS: trim `MigrateTagFamiliesCommand`'s tier-authoring
   call~~ — **DONE (Phase 196).** Applied `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md`; build +
   Tags.Tests green. The remaining entry point is `TagFamilyCreatorCommand` (step 3).
3. ~~Once nothing calls them, delete the `FamilyLabelAuthor` / `TagConfigPlanResolver` cluster~~ —
   **DONE (Phase 197).** `TagFamilyCreatorCommand` was gutted off the CSV path first (removing the
   last callers), then both files deleted (0 callers verified). The v5.0-CSV **reader** and **data**
   are RETAINED (still the canonical synced source, still parsed elsewhere) — see the table above.
4. Repurpose `HandoverModeHelper` DC/HO → `PARA_STATE` view preset (Task-3-adjacent); remove
   the dual-CSV authoring path only. **Still open** (helper has live callers).
5. Deprecate the colour-scheme commands separately (keep depth-variant creation everywhere).
   **Still open** (live buttons + placement).

**Consistency findings (Phase 196 sweep) — both APPLIED in Phase 197:**
- ~~**`FamilyParamCreatorCommand` injects STATE/style params as INSTANCE.**~~ **FIXED (Phase 197).**
  `InjectSharedParams` now uses an `IsTypeParam` predicate: `TagFamilyConfig.VisibilityParams` ∪
  `StyleParams` ∪ `{ TAG_DEPTH_TIER, TAG_BOX_*, TAG_LEADER_*, TAG_POS }` bind as TYPE; container
  values (`ASS_TAG_*`, tokens, description, label params) stay INSTANCE. `TAG_POS` preserved as type
  (drives its offset formula). Depth-setting on "Inject Params"-built families now works.
- ~~**SEQ zero-pad has two sources of truth.**~~ **FIXED (Phase 197).** New `TagConfig.EffectiveSeqPad`
  (`SeqPadWidth > 0 ? SeqPadWidth : ParamRegistry.NumPad`); `BuildSeqString` and `BuildAndWriteTag`
  route through it. Panel still writes `SeqPadWidth` (live driver); `NumPad` write kept in
  `ApplyTagFormatOverrides` (fallback + `num_pad` export). Single accessor — can't desync.


## Universal Tag badge/gate ↔ drawing-pipeline integration (branch `feature/universal-tag-system`)

Follow-on to the badge/gate system: wired the stamped status gates into the AEC filters,
View Style Packs, and QA workflows (see CHANGELOG "Universal-tag badge/gate integration").
Open items surfaced while doing it — recorded rather than force-fixed:

**QA-filter rationalization (runner Task 4 — DOCS ONLY, nothing deleted).**
The stamped **data gate** (`STING_GATE_DATA_STATUS_INT`) now computes the same completeness
signal the old ad-hoc completeness filters derive per-view. Once `coord-qa` + the four
`qa-gate-*` filters are proven in Revit, deprecate the overlapping completeness filters in
favour of the gate-based ones (single source of truth = the stamped gate, not a re-derived
per-view rule). Overlapping legacy ids in `STING_AEC_FILTERS.json`:
`qa-untagged` (⊆ data-gate red), `qa-incomplete-tag` (⊆ data-gate amber "TAG INCOMPLETE"),
`qa-missing-disc` / `qa-missing-loc` (⊆ data-gate amber container/ISO reasons),
`qa-stale-element` (orthogonal — keep; staleness is not a gate input). These have live
callers/packs, so **not deleted** — deprecate only after the Revit smoke test.

**Drawing-Type tag-binding audit (runner Task 5 — reported, no change made).**
`STING_DRAWING_TYPES.json` annotation rule packs bind `tagFamilies` per category. 22 bindings
reference `"STING - Generic Tag"`; the rest reference specific `STING_TAG_*` families. Finding:
`"STING - Generic Tag"` is a **placeholder generic-tag family name** (spaces / "STING - " prefix),
NOT the universal master — the universal label is propagated *into* the 206 named `STING_TAG_*`
families, so the specific bindings already carry it. Repointing the 22 generic bindings to a
"universal master" would therefore be wrong. **No bindings changed.** Revisit only if a single
universal `.rfa` master is ever loaded per-category and the names are proven to resolve.

**Integration follow-ups (need Revit validation before merge — repo norm):**
1. **View Style Pack catalogue now loads at runtime.** `ViewStylePackLibrary` gained a `stylePacks`
   alias (it previously bound only `viewStylePacks`, so the 31-pack corporate file + editor-written
   project overrides were runtime-dead and the registry used 3 hard-coded `BuildDefaults` packs).
   This activates the full corporate catalogue at runtime for the first time — **validate drawing
   production in Revit** (managed-template minting + authored vgOverrides now apply). Treat like the
   Duct smoke test: prove before merge.
2. **Managed issue packs still show badges.** `corp-standard-plan`, `corp-fabrication-shop`,
   `corp-coordination` are `templateMode: managed` with `managedFields` that exclude `vgOverrides`,
   so their `STING_TagStatus: {visible:false}` hide does not apply through the managed path. Either
   add `"vgOverrides"` to those packs' `managedFields` (note: this also applies their existing
   authored vgOverrides, a visible appearance change) or leave as-is (badges are opt-in —
   `TAG_WARN_VISIBLE_BOOL` defaults off — so this is belt-and-braces). Deferred pending the Revit
   validation in (1).
3. **Style-pack `routing[]` is not consumed by `ViewStylePackRegistry`** (resolution is by explicit
   pack id). The added `{purpose:QA → coord-qa}` entry (and all existing routing entries) are
   declarative until routing consumption is wired. Low priority.
4. **Guide edits live on a different branch.** The runner's Task 6.1 (rename badge visibility params
   to UPPERCASE `VIS_DATA_*` / `VIS_QA_*`; document the message labels + view-driven control) targets
   `docs/UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` + `docs/UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md`, which do
   **not exist on `feature/universal-tag-system`** — they live on branch `claude/tag-tier-review-94c78a`
   (worktree `awesome-kirch-94c78a`), together with the runner itself. This branch implemented the
   *enabling* code (the `STING_GATE_*_MSG_TXT` params + message computation + stamping, and the
   filters/packs/workflows); the guide edits must be applied on that branch (or when the two branches
   merge). Not forked here to avoid divergent guide copies.

**Smoke-test survival (carry forward):** when the Duct smoke test runs, additionally verify the
badge subsystem survives recategorise-propagation — the 6 family-local `VIS_DATA_GREEN/AMBER/RED` +
`VIS_QA_GREEN/AMBER/RED` Yes/No params, the `STING_TagStatus` annotation subcategory, and the coloured
glyphs must all survive `Propagate_UniversalTag`, and `Gate_StampStatus` must repopulate the four
`STING_GATE_*` params (2 INT + 2 MSG) so badges + message labels render.


## PM / Cost-Control — remaining (branch `claude/pm-cost-control`)

PM-1 landed (the §2 correctness bugs + the do-once shared helpers
`IssueStatusNormalizer` / `MoneyRound` / `ContractSumResolver`, with
`StingTools.Cost.Tests`, 41 green). The full catalogue with file:line anchors is in
`docs/PROJECT_MANAGEMENT_COST_CONTROL_PROMPT.md`. Still open:

- **PM-2 — remainder.** Done (`claude/pm2-onward`): CostPlan→`PROJECT_BUDGET_UGX`
  seeding, cert cumulative keyed on stable `SectionKey`, FinalAccount certified-to-date,
  AFC/FinalAccount/EVM unified on `ContractSumResolver`, retention-MoS basis
  configurable. Still open: approved VO → next-cert "Adjustments/Variations" SOV
  section; clash → tracked issue (via the normalizer) into `issues.json` with SLA +
  reconcile `clashes.json`.
- **PM-3 — remainder.** Done: index-linked fluctuations engine (feeds AFC +
  FinalAccount). Still open: schedule-driven cash-flow S-curve (real time-phased PV,
  consuming the PM-4 CPM dates); dayworks build-up (via `StarRate`); loss & expense /
  compensation events (off the captured EOT days); CVR report; NRM1 elemental cost
  plan + OCE; line-level cost-to-complete; commitments register; QuickBooks/Sage/Excel
  export.
- **PM-4 — remainder.** Done: the pure `CpmEngine` (forward/backward pass → critical
  path + total/free float, cycle detection). Still open: converge the two MSP/XER
  parsers + read P6 `TASKPRED`/MSP links to feed the engine; model-driven % complete;
  Uganda working-calendar into generation.
- **PM-5 — carbon data.** Per-material/category waste table; UG clamp-kiln brick
  factor; Kampala/UG green-baseline rows; stainless factor. (B6→GridCarbonRegistry +
  benchmark consolidation done in PM-1.)
- **PM-6 perf / PM-7 hygiene / PM-8 delivery layer** — multicategory filters on the
  heavy sweeps + one cached `BuildBOQDocument` per command run; the WorkflowEngine
  name-collision rename, dup `ContractForm`/`SuggestLiability`/`MapProviderIdToLegacySource`,
  sidecar-root unification; MIDP/TIDP engine + real KPI time-series + risk register.

## BOQ & Cost Manager — remaining after Round 3

Branches `claude/boq-master-impl`, `claude/boq-measurement-qs` and
`claude/boq-round3` (WP-C carbon convergence, WP-M measurement parity 2, WP-A
automation, WP-Q QS fills) landed. Round 3 closed: the fossil/biogenic carbon
convention on every surface (off real volume, with waste), shaft-void deduction,
aggregated MeasureQuantity write-back, the could-not-measure export gate, unified
solid-reader detail level, debounced incremental auto-refresh, frozen contract sum,
retention release and the sign-off guard. Still open:

- **WP-M — remainder.** The full per-material COST-row split (multiple bill rows per
  material — carbon already splits, cost still bills the whole element); the optional
  `matchSystem` (SYS) token in takeoff/measurement matching (discipline preference is
  done); a regional/per-category waste override (currently the flat
  `COST_DEFAULT_WASTE_PCT`); remaining magic unit literals → `UnitUtils`.
- **WP-C — remainder.** Per-row EN 15978 A4/A5/C module surfacing on the BOQ (the
  V6 `CarbonStageTracker` computes them and now shares the A1-A3 basis; the BOQ row
  is explicitly scoped "A1-A3 upfront"); repoint/relabel the dead legacy
  `CARBON_FACTORS.csv` loader.
- **WP-Q — remainder.** Index-linked Fluctuations engine (NEDO/BCIS formula or local
  CPI, basket UI) feeding both AFC and Final Account; contractor CVR (`Cost_CVR`)
  fusing BOQ total + latest cert + VOs + PS movement + retention + EVM + cash-flow;
  itemised contra-charges register behind the flat `OtherDeductions`; Materials-on-Site
  capture into `SovLine.MaterialsOnSite` with a vesting trail + statutory
  payment/pay-less dates; Dayworks build-up sheet (mirror the star-rate builder);
  distinct PC-sum mechanism (NRM2 defined/undefined) separate from provisional sums;
  time-phased EVM BCWS curve; adjudication hardening (IQR/std-dev outliers).
- **WP-A — remainder.** Off-thread rate-feed pre-warm on document open; a low-frequency
  scheduled server-baseline drift check (surface `SyncState=Conflict` proactively).
- **WP6 — remainder.** Snapshot one FX rate per build + make currency mandatory on
  every rate source (validate FX presence, no silent default). (USD-from-UGX at
  summary and BCIS-off-sync-path are done.)

## Placement Centre — residual gaps (post review/hardening)

- **BOQ quantity handoff.** Placed-element counts (`PlacementResult.PlacedIds`,
  keyed by provenance) still don't feed `StingTools/BOQ/`. A "Send placed
  quantities to BOQ" action keyed off the provenance stamp would close the
  loop. Deferred from the review (large BOQ API surface; unverifiable here).
- **Drawing-Type preset *apply* during placement.** "Save view preset" writes
  `StingViewPresetSchema` (works), but nothing applies a preset during a run
  and it bypasses `DrawingTypePresentation.Apply`. Either route through the
  Drawing Template Manager or drop the half-feature. Deferred.
- **Wall/ceiling/floor-follow router.** The legacy `WALL_FOLLOW` /
  `CEILING_FOLLOW` / `FLOOR_FOLLOW` rule tokens are normalized to the nearest
  drop engine with an honest warning (Part C.5). A genuine follower router
  (route along wall/ceiling faces, not a drop) is net-new work.
- **Data-driven Auto-place category checklist.** The 19 category checkboxes
  are hardcoded; non-placeable ones (Conduits/Pipes/Cable Trays routing
  outputs; Specialty/Nurse Call needing rule packs) are annotated via tooltips
  rather than driven from the engine's supported-category set. A registry-driven
  checklist would prevent UI↔engine drift.
- **PlacementRule push-to-family-types fields.** `Material` / `GlazingSpec` /
  `InsulationThicknessMm` / `NominalDiameterMm` / `MaintenanceClearance` and
  the advisory fields (`ToughenedGlazingRequired` / `MinSlopePercent` /
  `MinUniformityRatio` / `ExposureClass` / `EmitSupports`) are loaded + edited
  but not consumed by the placement engine (DEFERRED in the CHANGELOG table).
  Either wire them into the push-to-family-types pass / a post-validator, or
  retire the editor cards.
- **StandardRef ↔ ApplicableStandards.** The profile standards gate keys off
  the structured `ApplicableStandards`; rules citing standards only via
  free-text `StandardRef` are kept (not dropped) and the engine warns when the
  filter is inert. Backfilling `ApplicableStandards` across the rule packs (or
  a normalized StandardRef token match) would make the standards filter active.

## BOQ / Cost — residual gaps (post Stage-B integration)

- **Currency conversion (not just labels).** Stage-B B.1 made currency labels
  consistent (UGX), but there is no FX conversion. Mixing a GBP rate-card entry
  with a UGX BOQ would still sum face values. A single project-currency knob +
  per-provider FX (the `RateProviderRegistry` already converts GBP/USD→UGX for
  rates) extended to certs / variations / EVM is the real fix.
- **NRM1 cost-plan UGX benchmarks.** `STING_NRM1_BENCHMARKS` are genuinely
  £/m² GIFA (UK BCIS-style). For a UGX project the cost plan is shown in GBP
  (honest, via `CostPlanDocument.Currency`) — a UGX benchmark set, or an FX
  projection at plan time, is needed for a UGX-native concept estimate.
- **B.6 — `AssignBoqLineRefs` group index.** Middle index is the literal `1`
  (`{section}.1.{n}`). Cosmetic (refs unique within a section); a per-Category
  group index would change the ref format and risks the write-once
  `ASS_BOQ_LINE_REF` stamp — deferred.
- **B.6 — QS import non-numeric rate warning.** A blank rate is treated as
  "unpriced / no change"; genuinely non-numeric rate text (e.g. "TBC") is
  currently read as 0 silently. Surface it as a diff-preview warning row.
- **EVM BCWS from a real schedule.** BCWS still comes from a QS-entered planned
  %; no 4D / cost-loaded-schedule wiring yet.
- **QS import per-row accept/reject.** The import diff is whole-batch
  Apply/Cancel; per-row checkboxes would let a QS accept a subset.

## StingBridge — remaining gaps (post 0.1.0-beta.2)

Shipped self-serve on planscape.build/downloads. **0.1.0-beta.2 is live** (SB-2, SB-3 and SB-4 closed; PATs added). Remaining gaps, roughly in value order:

| # | Gap | Detail |
|---|---|---|
| SB-1 | **Live-ArchiCAD verification** | Two documented-but-unverified assumptions need one session against real ArchiCAD (AC 28/29): (a) `_ifc_global_id_from_acguid` presumes ArchiCAD derives the IFC-export GlobalId from the JSON-API element GUID — if wrong, the live-sync and IFC-watcher paths mint two mapping rows per element; (b) zone labels read from `Zone_ZoneNumber`/`Zone_ZoneName` built-ins. Both degrade gracefully today. |
| SB-2 | ~~SEQ minting~~ **DONE Phase 202** | New atomic `POST /api/projects/{id}/seq/reserve` (INSERT … ON CONFLICT … RETURNING) plus `StingBridge/sync/seq_minter.py`, which ports `SeqAssigner.BuildSeqKey` exactly so both hosts draw from the same per-key counters. Wired into live sync + the IFC watcher, batched per run, idempotent, degrades to 7-segment rather than failing. Verified collision-free under 8-way concurrency. **Phase 211** closed the two holes the review found: the IFC path never adopted an already-written `ASS_SEQ_NUM_TXT`, so every re-drop re-minted (fixed + covered by a full-pipeline round-trip test), and the Revit plugin gained a server-side block-reservation **mechanism** (`SeqBlockReservation`). **The mechanism is NOT wired** — see SB-2b. **Remaining:** the Revit cross-host duplicate window is **still open in all cases**, not just offline ones (SB-2b). By design, even once wired, an unconfigured or unreachable server falls back to local allocation and the window stays open for that session — refusing to number offline would be worse than numbering optimistically and reconciling on the next `/seq/sync`. Also **not verified in Revit** — the reservation logic is unit-tested and the plugin builds clean, but no in-Revit runtime run was possible. |
| SB-2b | **Revit SEQ reservation is scaffolding only — not wired** | Phase 211 landed the mechanism and its unit tests; nothing calls it, so Revit still allocates purely locally and the online cross-host duplicate window (Revit vs StingBridge minting the same number on the same key) is **OPEN**. Two wiring points: (1) `PlanscapeServerClient.ReserveSeqBlocksAsync` (`StingTools/BIMManager/PlanscapeServerClient.cs:691`) has **zero callers** — a tagging run must pre-compute its per-key counts and reserve one block per key; (2) `TagConfig.BuildAndWriteTag` (`StingTools/Core/TagConfig.cs:2317`) calls `SeqAssigner.AssignNext` without the optional `reservation` argument, so it defaults to `null` → local allocation. Both are single-line-ish changes; the work is not the edit. **Why it waits:** this is the hot path for every tag the plugin writes, and a mistake renumbers a live model. It needs in-Revit runtime verification against a real document — unit tests and a clean compile do not cover the failure modes that matter (transaction scope, partial-run rollback, counter drift after a cancelled command). No Revit runtime is available to the agent that wrote it. Suggest gating behind a config flag on first release so a site can fall back without a redeploy. |
| SB-3 | ~~Token inference single-sourcing~~ **DONE Phase 205** | ArchiCAD vocabulary moved to `stingtools_core/hosts/archicad.py`; the bridge modules are re-export shims. The two level-derivation functions disagreed on 13 of 31 storey names and the ArchiCAD one collapsed every numbered basement to `B1` — now one implementation, union of both, bug fixed. `ArchiCadHostAdapter` implements the HostAdapter contract. **Follow-on:** the IFC watcher still uses its own extraction rather than `IfcFileHostAdapter`; swapping it needs equivalence coverage first. |
| SB-4 | ~~Hot-folder contract mismatch~~ **DONE Phase 204** | The Python watcher now follows the C# `processing/ → done/YYYYMMDD_<name>` \| `failed/` + `.log` contract (`StingBridge/watch/hot_folder.py`), keeping the sidecars and moving the `_sting.ifc` output with the source. Also added a start-up sweep of the inbox and `processing/` orphan recovery, both only safe once processed files leave the root. Failure routing reads `result["errors"]`, not just exceptions — routing on exceptions alone archived unopenable files as successes. |
| SB-5 | **Multi-host Phase B/C** — **tag-slice** engine DONE Phase 207 (corrected Phase 214), wiring open | The change feed (`GET /api/projects/{id}/changes`), `PullClient`/`CursorStore` and the `ReconcileEngine` are landed and verified two-way against real Postgres. Remaining: **SB-5a** wire the IFC-watcher path to pull→reconcile→push (testable today without ArchiCAD — the sensible first cut); **SB-5b** wire the live-ArchiCAD path and delete the 60-second grace heuristic in `sync/engine.py` (needs a licence to exercise the local-index read, so blocked behind SB-1). Also open: §1.4.4 client-side push chunking, §1.4.5 the GlobalId-stability CI fixture, and the Part 2 LoGeoRef coordinate engine. **Scope correction (Phase 214):** what landed is the **TAG slice** of §1.4.1–§1.4.2. The feed carries `kind="tag"` only — **issues / BCF / clash payloads are not implemented** — and **§1.4.3's "surface the loser as a Planscape issue" is not implemented**: conflicts are reported to an `on_conflict` callback that nothing consumes. The seams (`kind` field, callback) exist; the wiring does not. **Two further documented limitations:** rows with a null `LastModifiedUtc` never appear in the feed, so pre-existing elements stay invisible until next edited (a backfill may be needed); and there are **no delete tombstones**, so a deletion in one host never propagates. |
| SB-6 | **macOS notarized binary** | `any` zip covers macOS today; a signed native build is deliberate future work. |
| SB-7 | **Beta feedback loop** | Optional `download_log` table (D1) on the gated endpoint so beta testers can be followed up without the old request-by-email list. |

## Planscape Server — deployment gaps (Phase 200)

The blueprint is validated and the owner package is written; what remains is
owner-side or follow-on work. Status verified 2026-07-20.

| # | Gap | Detail |
|---|---|---|
| DEP-1 | **Server is not deployed** | `api.planscape.build` does not resolve. Owner-only: apply the Render Blueprint, paste secrets, add the custom domain + registrar CNAME. Prep is complete — see [`SERVER_GO_LIVE.md`](SERVER_GO_LIVE.md) and the local package at `C:\Dev\planscape-render-golive\`. |
| DEP-2 | **`PLANSCAPE_HANDOFF_SECRET` unset on Cloudflare** | `wrangler pages secret list --project-name planscape-marketing` returns 12 secrets and this is not one of them, so cloud→server handoff cannot work in production yet. It is a *shared* secret: set the identical value on Render (`planscape-api` **and** `planscape-worker`) and on Cloudflare Pages. Rotate both sides together. |
| DEP-3 | ~~Handoff provisions no Project~~ **DONE Phase 201, hardened Phase 212** | `EnsureStarterProjectAsync` now creates a project + `ProjectMember` when the tenant has none. Idempotent (gate is "zero projects"), best-effort so a failure never costs the session. Phase 212 made "never costs the session" actually true: the catch now detaches the failed `Added` entities, which otherwise poisoned the next `SaveChangesAsync` (the refresh-token one) and turned a provisioning failure into a 500 at login. |
| DEP-7 | **Test infra: `Hangfire.JobStorage.Current` is process-global** | Program.cs's ~40 static `RecurringJob.AddOrUpdate` calls read this process-wide static *during host build*, and each `WebApplicationFactory` points it at a container-owned storage that is disposed with the factory. Any host built after a sibling's teardown reads a dangling reference and dies with `ObjectDisposedException: Hangfire.InMemory.State.Dispatcher`, reported against whichever test ran next. Consequence: **no test can reliably stand up an extra factory**, which is why Phase 212's HTTP-level provisioning-failure test had to be dropped in favour of a SQLite mechanism test. Phase 212 measured five remedies; suite-wide serialisation made things worse (136 failures — classes sharing an in-memory DB bleed state), and the rest either did not help or regressed green tests. Real fix: stop reaching for process-global state during host build (inject `JobStorage` rather than reading the static), then restore the HTTP test. |
| DEP-4 | ~~Handoff accounts have no headless credential~~ **DONE Phase 201** | Personal access tokens: `POST/GET/DELETE /api/auth/tokens` + `POST /api/auth/token/exchange`, wired into StingBridge as `STING_PLANSCAPE_TOKEN`. A PAT is exchanged for a normal JWT, never accepted as a bearer token, so the API stays single-scheme. The unusable password hash on handoff accounts remains, by design. |
| DEP-5 | **`/api/auth/license/activate` is unrate-limited** | It is the only `AuthController` endpoint without `[EnableRateLimiting("auth")]`, leaving the licence-key space brute-forceable. It returns entitlement facts (`Valid`, `Tier`, `MimEnabled`, `ServerUrl`, `ExpiresAt`) rather than a JWT, so the blast radius is disclosure + activation-count burn, not session theft. |
| DEP-6 | **Handoff single-use check fails open** | The `jti` replay guard is a Redis `SET … When.NotExists`; when Redis is unavailable the exchange logs a warning and proceeds, so a captured ticket could be replayed within its 120 s TTL during a Redis outage. Acceptable given the TTL, but it is an availability-over-integrity choice worth making deliberately. |
| DEP-6b | **Rate-limiter Production gate is verified by review, not by a test** | PR #439 made `RateLimiting:Enabled=false` inert when `IsProduction()`. F5 got a Production-environment test host building (`UseSetting("environment", "Production")` — `UseEnvironment` after the base factory does not stick) and **confirmed the gate fires**: startup prints `[rate-limit] RateLimiting:Enabled=false IGNORED - the environment is Production and the auth limiter is not optional there.` The remaining leg — asserting an actual **429** — could not be landed: the `auth` policy is a **Redis-backed** sliding window (`RedisRateLimitPartition.GetSlidingWindowRateLimiter`, Program.cs:816), and in the in-process test host it does not trip even with the docker Redis reachable at the default `localhost:6379`. Asserting on a console line is too brittle to keep, so no test shipped. **To close:** either expose an in-memory limiter for the test host behind config, or stand the check up as an out-of-process smoke test against a running container. |
| DEP-6a | **No automated test for handoff jti replay** | The single-use guard is a Redis `SET … When.NotExists` that **fails open** when Redis is down, and the integration-test host registers no Redis — so a replay test would pass or fail depending on whether a docker Redis happened to be running. Needs either a Redis test double registered in `PlanscapeWebApplicationFactory` or an `IReplayGuard` seam that can be faked. Until then the guard is covered by review only (Phase 215). |
| DEP-7 | **`PLANSCAPE_IDENTITY_HANDOFF.md` status line is stale** | It reads "design agreed 2026-07-18, not yet implemented"; the feature is in fact implemented on all three sides (Cloudflare Pages Function, `AuthController`, Next.js `/handoff` page). The doc's own line references still resolve, so only the status line drifted. Its role table also says `project_lead` → `ProjectLead`, but `UserRole` has no such member and the code maps it to `Manager`. |
| DEP-8 | ~~StingBridge token-expiry constant is wrong~~ **DONE Phase 201** | Was 55 min against a 30-min server token, so the proactive refresh only fired ~25 min after expiry. Now 30 min with a 5-min margin, pinned by a regression test. |
| DEP-9 | **No UI for minting access tokens** | The API exists (`POST /api/auth/tokens`) and the guides document the `curl`, but the cloud app has no screen for it. A subscriber currently needs a terminal to get a StingBridge credential. |
| DEP-10 | **Integration-test suite has 73 pre-existing failures** | Down from 129 after the Phase 201 harness repair, and no longer blocked at startup. The remainder are assertions that drifted from current behaviour (e.g. `HealthCheck_ReturnsHealthy` expects 200 but gets 403; `Register_NewOrg_Returns201WithToken` reads a response property that no longer exists), not infrastructure faults. Each needs reading against the endpoint it covers. |
| DEP-11 | **`Jwt__Key` is an undocumented prerequisite for running the tests** | Without it every `WebApplicationFactory` test fails at host construction with a message about docker-compose. Worth either defaulting a throwaway key in the test factory or documenting it in the test project README. |

## Sub-system reviews

- [`PLACEMENT_CENTRE_GUIDE.md`](PLACEMENT_CENTRE_GUIDE.md) — plain-English user guide to the Placement Centre: every button, every editor field, background concepts (anchors, regex, mounting reference, provenance, standards), worked walk-throughs, troubleshooting and a cheat-sheet (2026-04-25).
- [`PLACEMENT_CENTRE_REVIEW.md`](PLACEMENT_CENTRE_REVIEW.md) — flexibility / functionality / automation gap audit of the Placement Centre with PC-01..PC-25 backlog and a recommended ≈ 25-category baseline catalogue (2026-04-25).
- [`HEALTHCARE_PACK_DESIGN.md`](HEALTHCARE_PACK_DESIGN.md) — multi-phase design document for the Healthcare / Hospital Design pack covering HTM / HBN / FGI / NFPA 99 / NCRP 147 / ASHRAE 170 / ISO 14644 / USP 797-800 / SFG20-Healthcare integration. Defines ~140 new shared parameters, 60 filters, 16 drawing types, 4 ViewStylePacks, 8 validators, COBie-Healthcare overlay, RDS template engine, MGPS package, radiation calc, adjacency analyser, anti-ligature pack, behavioural-health pack, digital-twin / IoT bridge, mobile commissioning app and server APIs. Phased H-1..H-22 with file-by-file integration map (2026-05-08).

## How to use this file

- Items are grouped by the review that surfaced them (Phase 74 5-agent review, Phase 76 DWG review, Phase 77 review, Phase 78 triage, etc.). The grouping is preserved so you can trace each gap back to its origin.
- Items marked `~~strikethrough~~` with `**DONE**` are completed — they stay here as a record of what the review covered. When closing a new item, either strike it through in place or move it to `CHANGELOG.md` under the appropriate phase.
- When adding a new gap, either extend an existing section's table or add a new `### Future Enhancement Gaps — <topic> (Phase N Review)` section at the end.

---

### MEP-from-DWG — V2 / V3 backlog

V1 (MEP fixtures from DWG blocks) shipped — see `CHANGELOG.md`. Remaining:

| Id | Item | Notes |
|---|---|---|
| ~~MEPDWG-V2-1~~ | ~~**Straight runs** — line → Duct/Pipe/Conduit/CableTray; size from layer suffix or default.~~ | **DONE (V2)** — `MepRunBuilder` + `MepRunClassifier`. |
| ~~MEPDWG-V2-2~~ | ~~**System assignment** + run elevation from level + per-layer offset.~~ | **DONE (V2)** — system TYPE at `Create`; per-kind / per-layer offset. |
| ~~MEPDWG-V2-3~~ | ~~**Fixture host-snapping** — nearest wall/ceiling; fall back to unhosted.~~ | **DONE (V2)** — best-effort, hosted-family only. |
| ~~MEPDWG-V2-4~~ | ~~**`MepCadWizard`** — per-layer mapping UI.~~ | **DONE (V2)** — per-layer include / run-kind / level / offset. |
| MEPDWG-V2-5 | **Native mounting-height param** — wire the numeric `MNT_HGT_MM` stamp once its exact name + unit (Length vs Number) is confirmed against Placement-Center output (height is encoded in the instance Z; only `MOUNTING_REFERENCE_TXT` stamped). | `TODO-VERIFY-API` in `MepFixtureBuilder.StampMetadata`. |
| MEPDWG-V2-type | **Per-layer family/run TYPE selection** in the wizard — V2 uses the first available type per category/kind. Add a per-layer type combo (enumerate types per category/kind) threaded into the builders. | |
| MEPDWG-V2-host2 | **Ceiling face-hosting fidelity** — V2 uses the host-element overload; face-based (WorkPlaneBased) families may fall back to unhosted. Use a real face `Reference` for true face hosting. | `TODO-VERIFY-API` in `MepFixtureBuilder.PlaceWithHost`. |
| ~~MEPDWG-V3-1~~ | ~~**Fittings** — junction detection → elbows/tees/crosses.~~ | **DONE (V3)** — `MepFittingBuilder`, best-effort + guarded. |
| ~~MEPDWG-V3-2~~ | ~~**Risers** — UP/DN/RISER blocks → vertical run segments.~~ | **DONE (V3)** — span to adjacent level / ±3 m. |
| ~~MEPDWG-V3-3~~ | ~~**Drainage slope** — sanitary/drainage pipe fall.~~ | **DONE (V3)** — End dropped by length × slope (1:80 default). |
| MEPDWG-V3-fit2 | **Fitting robustness** — `New*Fitting` needs the type's routing preferences to carry a fitting family and matching size/system; mismatches fall to the guarded skip. Pre-flight routing prefs + a transition fitting on size change would raise the hit rate. | `TODO-VERIFY-API` in `MepFittingBuilder`. |
| MEPDWG-V3-flow | **Drainage flow direction** — V3 drops the line's End end deterministically (no flow data from a 2D plan). Infer direction from connected stacks / gullies, or expose a per-run flip. | |
| MEPDWG-V3-riser2 | **Riser connection** — risers are created as standalone vertical segments; auto-connect them to the horizontal run at the same XY (the fitting pass joins coincident ends but the riser base may sit at a different Z than the run). | |
| MEPDWG-V1-note | V1 only captures fixture blocks whose layer/block name is recognised as MEP (the extraction whitelist). Fixtures on mislabelled layers with non-MEP block names are not captured — extend the layer mapper or rename layers. | Documented V1 limitation. |

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
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | Medium | **DONE** Phase 149: `TeamWorkloadEngine.Build()` + `TeamWorkloadReportCommand` + BCC Project Members "Issue Workload" sub-tab with sortable DataGrid (Critical×3+High×2+Open×1 score), KPI strip, and CSV export. |
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

---

### General Tagging Functionality Review — 2026-05-17 Audit

A holistic review of the tagging subsystem was performed covering the full pipeline (`TagPipelineHelper.RunFullPipeline`), the auto-tagger IUpdater, NLP processor, placement presets, style rules, and supporting data files. The review identified seven actionable gaps, all of which were fixed in this session on branch `claude/review-tagging-functionality-0yYY5`.

#### Fixed Gaps (Phase 177 — 2026-05-17)

| ID | Gap | File(s) Changed | Resolution |
|----|-----|-----------------|------------|
| GAP-STATUS-01 | STATUS token can drift from Revit phase model after phases are reorganised post-tagging. When phases are renamed or elements moved to a different phase, existing STATUS values become stale — but `PopulateAll` only writes STATUS when the token is empty (unless `overwrite=true`). | `TagConfig.cs`, `ParameterHelpers.cs` | Added `AutoCorrectStatusFromPhase` boolean property to `TagConfig` loaded from `AUTO_CORRECT_STATUS_FROM_PHASE` in `project_config.json` (default `false` for backwards compatibility). When `true`, `TokenAutoPopulator.PopulateAll` re-derives STATUS from Revit phase data and overwrites the existing value even without the `overwrite` flag. ISO 19650 projects reorganising phases mid-project should enable this key. |
| GAP-PLACE-01 / ENH-03 | Leader clearance margin for elbow avoidance (`LeaderClearanceMargin` in `SmartTagPlacementCommand`) was a `const double = 0.5` — no way to tune it without recompiling. Dense plant rooms or tight service corridors need 2–3 ft; sparse office floors may only need 0.1 ft. | `SmartTagPlacementCommand.cs` | Changed `const double LeaderClearanceMargin` to a computed property reading `TagConfig.GetConfigDouble("LEADER_CLEARANCE_MARGIN_FT", 0.5)`. Projects set this via `project_config.json`. Added `"LEADER_CLEARANCE_MARGIN_FT"` to `TagConfig.knownKeys` to suppress "unknown key" warnings. |
| GAP-AT-02 | Elements tagged asynchronously by `StingAutoTagger` (element placement or deferred replay) were indistinguishable from manually tagged elements in the `ASS_TAG_MODIFIED_BY_TXT` audit trail. ISO 19650-2 §A.5 requires traceability of the person/process responsible. | `StingAutoTagger.cs` | After every successful `RunFullPipeline` call in `ProcessBatch`, the auto-tagger now prepends `[AUTO_TAGGER]` to `ASS_TAG_MODIFIED_BY_TXT` if not already present, preserving any existing user name from a prior manual edit. |
| GAP-NLP-01 | `NLPCommandProcessor.IntentPatterns` lacked coverage for ~15 common user intents: ISO validation commands (`validate tags`, `check ISO`, `full compliance check`, `dry run tag`), token-level setters (`set level`, `set system`, `set function`, `set product`), placement resolution (`fix overlap`, `resolve collision`, `reset position`, `lock position`, `align horizontal/vertical`, `stack tags`, `learn placement`, `apply template`, `batch place`), 3D tagging (`tag 3d`), and repair commands (`repair duplicate seq`, `decluster tags`). | `NLPCommandProcessor.cs` | Added ~15 new regex → intent mappings covering all identified missing patterns. |
| GAP-STYLE-01 | `TAG_STYLE_RULES.json` had 7 named presets (Default through Zone Highlight) covering only DISC-based and system-based colour switching. Missing were: stale-element highlighting, revision-code colouring, per-level identification, per-location colour coding, completeness QA (complete / partial / missing tiers), combined discipline+system+function rules for HVAC/electrical/FP, and auto-tagger audit visibility. | `TAG_STYLE_RULES.json` | Added 8 new named presets: `Stale`, `Revision`, `Level`, `Location`, `Completeness QA`, `Discipline + System`, `Auto-Tagger Audit`, each with appropriate condition arrays and type mappings. |
| GAP-DATA-01 | `TAG_PLACEMENT_PRESETS_DEFAULT.json` had rules for only 17 categories (12 standard MEP/arch + 5 STING-LPS). The smart placement engine uses category name as a lookup key, so any category not listed falls through to a generic default with no tuning. Missing categories included: Plumbing Fixtures, Conduits, Cable Trays, all Communication/Data/Security/Nurse-Call device types, Structural Columns, Structural Framing, Structural Foundations, Walls, Floors, Ceilings, Stairs, Furniture, Casework, Parking, MEP Spaces, Duct / Pipe Accessories & Fittings, Flex Ducts/Pipes, Mass, Curtain Panels, Mullions, Structural Rebar, Planting, Site, Topography, plus healthcare-specific STING tags. | `TAG_PLACEMENT_PRESETS_DEFAULT.json` | Expanded from 17 rules to 67 rules (50 new categories added). Each new rule has a calibrated `preferredSide` (0=above, 1=right, 2=left, 3=below), `offsetX/Y`, `addLeader`, `orientation`, and `leaderThreshold` based on industry annotation conventions (BS 1192, CIBSE, HTM). Added healthcare STING tags: Medical Gas Outlet, Medical Gas Manifold, Emergency Equipment, HVAC Sensor, Fire Door, Tie-In Point, Waste Container, Radiation Shielding. |
| GAP-DATA-02 | No machine-readable registry of valid PROD codes existed. `TagConfig.GetFamilyAwareProdCode()` contains ~35 hardcoded `if/else` branches that are invisible to auditing tools and cannot be extended without code changes. No way to validate PROD codes against a known catalogue or report coverage gaps. | `StingTools/Data/STING_PROD_CODES.csv` (new file) | Created a 165-row PROD code registry CSV (`PROD_CODE, CATEGORY, FAMILY_PATTERN, DESCRIPTION, DISCIPLINE, SYSTEM, STANDARD_REF`) covering all MEP, structural, healthcare, fire protection, and site categories. Intended as the single source of truth for `ValidateProdForDisc()` coverage audits and future refactoring of `GetFamilyAwareProdCode()` to be data-driven. |
| GAP-DATA-03 | The SYS→FUNC validation matrix (`_validFuncsForSys` in `TagConfig.cs`) was entirely hardcoded — ~25 systems each with a hardcoded array of valid FUNC codes. Any extension required a code change and recompilation. The matrix was not visible to QA processes or project configuration tooling. | `StingTools/Data/STING_FUNC_SYS_MATRIX.csv` (new file) | Created a 130-row SYS/FUNC matrix CSV (`SYS_CODE, SYS_DESCRIPTION, FUNC_CODE, FUNC_DESCRIPTION, DISCIPLINE, CIBSE_REF, ISO_19650_VALID`) covering: HVAC, HWS, DCW, DHW, SAN, RWD, GAS, FP, LV, HV, FA, ICT, COM, NCL, SEC, BMS, MGS, LPS, RAD, ARC, STR, GEN. Includes CIBSE / BS standard references per row. Intended as the source for a future data-driven `ValidateFuncForSys()` loader and `STING_FUNC_SYS_MATRIX` NLP command. |

#### False Positives Identified and Ruled Out

| Reported Gap | Verdict |
|---|---|
| `CommissioningChecklistCommand` missing | **False positive** — exists at `IoTMaintenanceCommands.cs:374`, wired in `StingCommandHandler.cs:2916`. |
| `ValidateProdForDisc` always returns null | **False positive** — method has real implementation with 35+ category branches; not null. |

#### Data Files Created This Session

| File | Rows | Purpose |
|------|------|---------|
| `StingTools/Data/STING_PROD_CODES.csv` | 165 | Machine-readable PROD code registry for coverage auditing and future data-driven refactor |
| `StingTools/Data/STING_FUNC_SYS_MATRIX.csv` | 130 | Editable SYS→FUNC validation matrix replacing hardcoded `_validFuncsForSys` dictionary |

#### Remaining Open Items (not implemented — require further investigation)

| ID | Gap | Why Deferred |
|----|-----|--------------|
| GAP-REFACTOR-01 | Refactor `GetFamilyAwareProdCode()` to load from `STING_PROD_CODES.csv` at startup | Requires testing the CSV loader path against all 165 rows and updating the `knownKeys` / config infrastructure. Medium-complexity refactor. |
| GAP-REFACTOR-02 | Refactor `_validFuncsForSys` to load from `STING_FUNC_SYS_MATRIX.csv` | Same loader pattern as above. Both refactors should land together to avoid two separate data-loading PRs. |
| GAP-NLP-02 | NLP patterns for healthcare commands added in Phase 176 ("run pressure audit", "mgps verify", etc.) | 19 patterns were added to NLPCommandProcessor in the Healthcare Pack. Verify they are still present after this session's append. |
| GAP-UI-01 | No UI surface for `AUTO_CORRECT_STATUS_FROM_PHASE` toggle | `ConfigEditorCommand` should expose this boolean alongside the existing toggle controls. Low risk but requires XAML + command handler changes. |
| GAP-UI-02 | No UI surface for `LEADER_CLEARANCE_MARGIN_FT` | Same as above — could be added to the Smart Placement wizard or Config Editor as a numeric text box. |
| GAP-STRUCT-01 | StructuralAnalysisEngine subchecks need per-subcheck phases | `StructuralAnalysisEngine` general — deflection / punching / wind / vibration / SSI / progressive collapse are diffuse single-shot calcs. Each subcheck takes a different parameter set (member type × load case × code combination) so there's no clean one-pass model walker. Each needs its own phase. That's the genuinely-deferred remainder of the integration audit. (Note rescued during merge of `claude/stingtools-bim-research-8Kkwv` into `claude/continue-model-viewer-updates-4GJR4`; previously orphaned in a truncated CHANGELOG.md.) |

#### Symbol library — Phase 188 closure status

Closed in Phase 188 (this session):

| ID | Gap | Status |
|---|---|---|
| ✅ GAP-SYM-01 | BS EN 60617 SLD parity (15 → 52) | **CLOSED** — `STING_SLD_SYMBOLS_BS.json` brought to IEC parity with 37 new symbols (transformers, generation, protection, switches, busbars, motors, meters, ATS, EV charger). |
| ✅ GAP-SYM-02 | NFPA 70 / NEC US-style parity (13 → 47) | **CLOSED** — `STING_SLD_SYMBOLS_NFPA.json` extended with 34 NEC + NFPA 72 symbols (NEMA receptacles, NFPA 72 alarm devices, NEC panels/busways/breakers). |
| ✅ GAP-SYM-03 | CIBSE building-services SLD content (14 → 36) | **CLOSED** — `STING_SLD_SYMBOLS_CIBSE.json` extended with 22 mechanical-services symbols (pumps, fans, heat exchangers, tanks, boilers/chillers/heat pumps, AHU/FCU, control valves, sensors). |
| ✅ GAP-SYM-07 | Symbol coverage audit command | **CLOSED — pre-existing** — `SymbolCoverageAuditCommand` (`Commands/Symbols/SymbolMaintenanceCommands.cs:43`) already wraps `SymbolCoverageAuditor.GenerateCoverageReport`. Phase 188 audit had flagged this as missing; on inspection it is wired and functional. |

Still open (cannot complete in Linux sandbox or out-of-scope for this session):

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-SYM-04 | Verify and promote `status: draft` → `status: reviewed` for the 884 symbols by running each in Revit against its standard plate | 6–8 weeks (1 discipline/week × 8) | This is the path from "comprehensive draft" to "comprehensive verified". No symbol is `final` without (a) seed `.rfa` committed, (b) Revit-rendered comparison vs standard plate, (c) `STING_FINALIZATION_CHECKLIST` bitmask = 127. Cannot run in Linux sandbox. |
| GAP-SYM-05 | Author hand-drafted seed `.rfa` families for the ISO 6412 priority symbols (5 elbows + 5 valves + 5 flanges + butt-weld + tee + cap = 18 families) | 3 days | Currently every ISO 6412 symbol resolves via the runtime generator. Hand-drafted seeds give pixel-perfect standard accuracy and let users hot-fix specific symbols without regenerating the whole pack. Requires Revit family editor. |
| GAP-SYM-06 | Project-scoped overlay layer for symbol catalogues, mirroring the Drawing-Type project override mechanism (`<project>/_BIM_COORD/symbol_overrides.json`) | 1 week | Every symbol catalogue today loads directly from `StingTools/Data/Symbols/`. Organisations want to override specific glyphs (e.g. corporate sub-form of MCB) without forking the corporate baseline. Touches 5+ catalogue loaders so deferred for a focused refactor session. |


#### Symbol library — second-pass review backlog (2026-07-04, branch `claude/symbol-fixes-2`)

Surfaced by the extended `Symbols_Validate` command (run it to reproduce these counts).
Recorded here rather than auto-fixed because each item needs human specification or Revit
family authoring — no geometry / connector topology was invented.

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-SYM-08 | **Seed connector coverage** — 12 MEP-category seeds ship with zero connectors, so their instances cannot be inserted inline into a duct/pipe/tray run or auto-routed. (Duct + Pipe accessory seeds were fixed in this pass; the rest need per-device connector specs — many are face-hosted annotation devices that may legitimately need only an electrical connector, or none.) Seeds: `STING_SEED_AudioVisualDevice`, `STING_SEED_CommunicationDevice`, `STING_SEED_DataDevice`, `STING_SEED_ElectricalFixture`, `STING_SEED_FireAlarmDevice`, `STING_SEED_FireDamper`, `STING_SEED_LightingDevice`, `STING_SEED_LightingFixture`, `STING_SEED_MechanicalControlDevice`, `STING_SEED_NurseCallDevice`, `STING_SEED_SecurityDevice`, `STING_SEED_TelephoneDevice`. | 1–2 days | Each device class has a different connector topology (electrical power vs data vs none vs airflow for the fire damper). Connector count/domain/systemType must be specified per device by an engineer; guessing risks wrong-domain connectors that break routing. Use the `offsetX/offsetY/offsetZ` + `facing` bindable fields (NOT `x/y/z/direction`, which do not bind). |
| GAP-SYM-09 | **Symbol authoring backlog** — 53 unique family names referenced by concept `standardMappings` are not defined in any catalogue (of 799 concept refs, 276 dangle: 0 prefix-fixable after this pass, 218 view-context overrides that now degrade to the base family via P8a, and 58 genuinely-absent refs → 53 unique). These are specialty glyphs that must be hand-authored per standard plate: **Hazardous-area (19)** ATEX 2014/34/EU + IEC/BS EN 60079 + DSEAR zone/Ex markers (concepts `ELEC_ATEX_*`, `SLD_ATEX_*`); **Medical gas (16)** HTM 02-01 / ISO 7396 / NFPA 99 O₂·N₂O·Air·Vac·CO₂·AGSS outlets (`ELEC_MG_*`); **Lightning protection (7)** BS EN 62305 / NFPA 780 air-terminal / down-conductor / earth-electrode / bonding-bar (`SLD_LPS_*` under BS/NFPA); **Phase sequence (4)** IEC 60034-8 / BS 7671 ABC/ACB (`SLD_PHASE_SEQUENCE_*`); **Other (7)** `ELEC_DB`, `ELEC_FCU_DEVICE`, `SLD_DB_DOWNSTREAM` (+IEEE), `PLM_PUMP_INLINE`, `SLD_RCBO_COMPOUND`, `SLD_STAR_DELTA_STARTER`. | 1–2 weeks | Requires authoring ~53 standard-accurate symbol definitions across ATEX / medical-gas / LPS / motor-control domains, each verified against its standard plate. `Symbols_Validate` (check 1b) is the tracking mechanism — the "absent" count should trend to 0 as these are authored. |

### Tag text-size variants (Option 2 — per-drawing sizing)
`DrawingType.TagTextSizeMm` (0 = derive) + `EffectiveTagTextSizeMm()` resolve a per-view tag size
from the drawing scale, returning one of 8 canonical sizes (1.0/1.5/2.0/2.5/3.0/3.5/4.0/5.0 mm;
ISO default 2.5 mm at 1:50). `DrawingType.TagSizeToken(mm)` → the "2.5mm" text-type/size-family token.
**Pending (needs Revit + propagation):**
- Human authors the 8 label **text types** (`1.0mm`…`5.0mm`) on the universal master; because a
  single label's text size is a Type property (not param-drivable), selectable size = **one
  size-variant family per size** (build once, SaveAs per size changing only the label Text Size,
  propagate each). 8 sizes is generous — 2.5 mm + 3.5 mm cover most output; author the rest as needed.
- Consumer not yet wired: `DrawingProducer`/`AnnotationRunner` should pick the size-variant tag
  family via `EffectiveTagTextSizeMm()`/`TagSizeToken` when placing tags. Add once the size families exist.

### Status delivery — in-tag badges ABANDONED, replaced by the Status Register
The coloured status-badge glyphs cannot work in Revit: a tag family's **formulas can only
reference the family's own parameters, not the tagged element's** (confirmed live — `vis_data_green`
= `and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 2)` errors "not a valid parameter",
because `STING_GATE_DATA_STATUS_INT` is an element param). Only LABELS can surface element data,
and label text is monochrome. So per-element coloured badges are impossible.

**Replacement (shipped):** `Status_Register` command (`Commands/TagStudio/StatusRegisterCommand.cs`,
"Status Register" button) exports a colour-coded Excel register (Register + Summary sheets, reds
sorted to top, auto-filtered) from `ComplianceScan.ComputeElementGates` — read-only, no stamp run
needed. Element-level at-a-glance colour still available via the `coord-qa` view filters.

**Now-vestigial (keep for now, no harm):** the four `STING_GATE_*_MSG_TXT` params + `Stamp Gates`
still feed the register's message columns (useful). The `STING_TagStatus` subcategory rules in the
view style packs are moot without in-tag glyphs but harmless. The badge-glyph sections of
UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md / UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md are superseded — status is
a register/view concern, not an in-family one. User deletes the drawn glyph fills + vis_* params in Revit.


#### Title-block family — base-split debt (2026-07-06, branch `claude/tb-rest-autonomous`)

Structural end-state that P10 / P11 / P12 deferred. Logged here rather than done
because it is a data-model refactor of `STING_TITLE_BLOCKS.json` inheritance, not a
behavioural change, and every concrete family + the leaf-wins merge logic depend on
the current shape.

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-TB-01 | **Split `A1_common` into a params-only identity base + a separate A1-geometry base.** Today `A1_common_v2.0` is the single root every size/portrait/fab/presentation family extends, and it carries BOTH the ~40-param identity-data universe (Group A/C, shared by all sizes) AND A1-landscape-specific geometry (lines, static text, labels, filled regions, the drawable rect, and the S01–S07/KP slots). Because A0/A3/portrait bases must override that A1 geometry, the merge had to be made leaf-wins (P10 static-text/labels, P11 params/slots, P12 drawable) so a size base can shadow the root's A1 values. The clean end-state is two roots: `STING_TB_identity_common` (params only, no geometry) and `STING_TB_A1_geom_common` (A1 landscape geometry) that the A1 concrete families extend, with A0/A3/portrait bases extending only the identity base. That removes the need for size bases to re-declare-to-override A1 geometry, shrinks the JSON, and makes "what geometry does this family inherit" answerable without running the leaf-wins fold. | 2–3 days | Touches the inheritance root every one of the ~30 title-block specs extends; requires re-parenting all size/portrait/fab/presentation/specialty families and re-verifying each builds identically (per-family slot/param/label counts unchanged) via `TitleBlock_CreateAll`. Best done as a focused refactor session with a before/after build-report diff, not folded into a feature change. The leaf-wins merge added by P10/P11/P12 keeps the current single-root shape correct in the meantime, so this is a cleanliness/maintainability debt, not a correctness bug. |


#### Title-block param namespace standardisation — P2 (2026-07-06, branch `claude/tb-rest-autonomous`)

**SKIPPED in the autonomous P12/P5 run** — the clean subset is real but execution
needs an owner decision (which naming system is canonical for title-block CELL
keys) plus a Revit-verified family rebuild, and the blast radius (shared param
file + 90 drawing types + 8 title-block families) is too high to land unverified.
Full analysis preserved here so a focused session can execute it safely.

**Three unreconciled naming systems** (the "PRJ_ORG_* / PRJ_TB_* / STING_SHEET_*"
divergence, made concrete):
1. `STING_DRAWING_TYPES.json` `titleBlockParams` **keys** are friendly cell names
   (`"Client Name"`, `"Company Name"`, `"Project Code"` — 998 entries across the
   90 profiles), with **values** read from `${PRJ_ORG_*}` on ProjectInformation.
2. The built `STING_TB_*` families expose **parameters** named `PRJ_TB_*` (36) and
   `PRJ_ORG_*` (16) — NOT the friendly cell names.
3. `TitleBlockParamApplier.Apply` does `tb.LookupParameter(key)` with key = the
   friendly name, so it only writes to a family whose params are literally named
   `"Client Name"`. Against the `STING_TB_*` families (params `PRJ_TB_CLIENT_NAME_TXT`
   etc.) the write warn-and-skips. **This friendly-name vs param-name mismatch must
   be decided first** — it is independent of, and blocks, the PRJ_TB_→PRJ_ORG_ move.

**Clean org-identity twin map** (project-level, same value across every sheet →
belong on ProjectInformation as `PRJ_ORG_*`):

| PRJ_TB_* (legacy) | PRJ_ORG_* twin | twin status |
|---|---|---|
| PRJ_TB_CLIENT_NAME_TXT | PRJ_ORG_CLIENT_NAME_TXT | exists |
| PRJ_TB_CLIENT_ADDRESS_TXT | PRJ_ORG_CLIENT_ADDRESS_TXT | add |
| PRJ_TB_COMPANY_NAME_TXT | PRJ_ORG_COMPANY_NAME_TXT | exists |
| PRJ_TB_COMPANY_ADDRESS_TXT | PRJ_ORG_COMPANY_ADDRESS_TXT | exists |
| PRJ_TB_CONTRACTOR_NAME_TXT | PRJ_ORG_CONTRACTOR_NAME_TXT | add |
| PRJ_TB_CONTRACTOR_ADDRESS_TXT | PRJ_ORG_CONTRACTOR_ADDRESS_TXT | add |
| PRJ_TB_MEP_CONSULTANTS_NAME_TXT | PRJ_ORG_MEP_CONSULTANTS_NAME_TXT | add |
| PRJ_TB_MEP_CONSULTANTS_ADDRESS_TXT | PRJ_ORG_MEP_CONSULTANTS_ADDRESS_TXT | add |
| PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT | PRJ_ORG_STRUCTURAL_CONSULTANTS_NAME_TXT | add |
| PRJ_TB_STRUCTURAL_CONSULTANTS_ADDRESS_TXT | PRJ_ORG_STRUCTURAL_CONSULTANTS_ADDRESS_TXT | add |
| PRJ_TB_LOGO_PATH_TXT | PRJ_ORG_LOGO_PATH_TXT | add |

**NOT twins — leave on `PRJ_TB_*` (legitimately per-sheet / workflow, not org identity):**
`PRJ_TB_SHEET_NR_TXT`, `PRJ_TB_PAPER_SZ_TXT`, `PRJ_TB_SCALE_OVERRIDE_TXT`,
`PRJ_TB_TOTAL_NO_SHEETS_TXT`, `PRJ_TB_VARIANT_TXT`, `PRJ_TB_DISCIPLINE_TXT`,
`PRJ_TB_REVISION_NR_TXT`, `PRJ_TB_REVISION_DATE_TXT`, `PRJ_TB_REVISION_DESCRIPTION_TXT`,
`PRJ_TB_DRAWN_BY_TXT`, `PRJ_TB_CHECKED_BY_TXT`, `PRJ_TB_APVD_BY_TXT`,
`PRJ_TB_DATE_DRAWN_TXT`, `PRJ_TB_DATE_CHECKED_TXT`, `PRJ_TB_DATE_APVD_TXT`,
`PRJ_TB_DELIVERABLE_*` (4), `PRJ_TB_LAST_TRANSMITTAL_*` (2), `PRJ_TB_LAST_SYNC_*` (2),
`PRJ_TB_ISSUE_SUMMARY_TXT`, `PRJ_TB_DESIGN_STAGE_TXT` (ambiguous vs PRJ_ORG_PHASE),
`PRJ_TB_SHOW_*_BOOL` (4), `PRJ_TB_LOCK_BOOL`, `PRJ_TB_NOTES_LEGEND_REF_TXT`,
`PRJ_TB_SCHEMA_VERSION_TXT`.

**De-risked:** the `PRJ_ORG_*` GUID scheme is deterministic —
`uuidv5(namespace = a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a, "PRJ_ORG_<NAME>_TXT")`
(verified against PRJ_ORG_CLIENT_NAME_TXT / _COMPANY_NAME_TXT / _PROJECT_CODE_TXT).
So the 8 new twins' GUIDs can be generated correctly-by-construction.

**Focused-session plan:** (1) decide the canonical `titleBlockParams` cell-key
convention (recommend: keys = the family param name, e.g. `PRJ_ORG_CLIENT_NAME_TXT`,
so LookupParameter hits directly), and rekey the 998 entries; (2) add the 8 new
`PRJ_ORG_*` twins to MR_PARAMETERS.txt (uuidv5, GROUP 13 PRJ_INFORMATION, TEXT);
(3) rebind the 11 org-identity labels in STING_TITLE_BLOCKS.json from `PRJ_TB_*` to
`PRJ_ORG_*`; keep `PRJ_TB_*` as deprecated aliases; (4) add a `TitleBlock_MigrateParams`
command copying `PRJ_TB_* -> PRJ_ORG_*` on ProjectInformation (SetIfEmpty);
(5) regenerate STING_TITLE_BLOCK_PARAMETERS.txt; (6) run TitleBlock_CreateAll +
verify the stamp fills from PRJ_ORG_* in Revit.
