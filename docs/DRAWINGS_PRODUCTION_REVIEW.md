# Drawings Production — Deep Review

**Date**: 2026-07-20 · **Baseline**: `main` @ `28bf1e6` (Phase 194+) · **Branch**: `claude/drawings-production-review-g9dhrt`

**Scope**: everything concerned with drawings production — the Drawing Type engine (`Core/Drawing/`, 40 files), the corporate catalogue (`STING_DRAWING_TYPES.json`, 93 types / 118 routing rules), View Style Packs + AEC filters (`STING_VIEW_STYLE_PACKS.json`, `STING_AEC_FILTERS.json`), title blocks (factory / resolver / param applier / revision syncer / `Families/AssemblyTitleBlocks/`), annotation automation (AnnotationRunner, dimensioners, match lines), legends & labels (`LegendBuilderCommands.cs`, `LABEL_DEFINITIONS.json`), the sheet production pipeline (DrawingProducer, ShopDrawingComposer, SheetTemplateEngine, SheetSequenceStore, renumbering), and command wiring (dock panel / NLP / WorkflowEngine dispatch).

**Method**: seven parallel focused code reviews (engine core · catalogue data audit · style packs & filters · title blocks · annotation/legends · sheet pipeline · wiring audit), all files read in full, JSON audited programmatically, findings cross-checked between reviewers. Top-severity findings were independently re-verified against source before inclusion (marked ✅).

**Review-only** — no production code was changed on this branch.

---

## Executive summary

The architecture is genuinely good: a single data-driven DrawingType concept, per-document registries with corporate-baseline + `_BIM_COORD` project-override layering, an idempotency design based on stamped parameters, and a well-ordered 8-step presentation pipeline. Referential integrity of the catalogue is excellent (no duplicate ids, all routing targets resolve, all style-pack references valid, all slots in range).

But the review found **~85 distinct defects**, including **5 critical** ones, and they cluster into five systemic failure modes:

1. **JSON keys the POCOs never bind** — large parts of the shipped corporate data are silently dead at runtime. The worst case: 11 of 35 style packs (including **all 8 healthcare packs**) keep their filter rules under `filterRules` while the loader binds only `filters`, and ~600 short-form `projColor`/`projWeight`/`cutColor`/`cutWeight`/`surfFgColor` override keys are unbound — so healthcare pressure/MGS/fire drawings get **no colour coding at all** and most packs lose their line graphics, with zero warnings.
2. **`extends` resolution strips fields** — both `DrawingTypeRegistry.ResolveExtends` (~12 fields, incl. `titleBlockParams` and `isoNaming`) and `ViewStylePackRegistry.ResolveExtends` (~14 fields, incl. `templateMode`/`managedFields`). All 35 packs use `extends`, so the **managed-template mode is unreachable** for every pack that declares it.
3. **No idempotency where the docs promise it** — AnnotationRunner duplicates every tag/dimension/north-arrow on re-run (`skipIfTagged` is never read); match-line dog-legs and captions duplicate every sync; legends duplicate on re-place; the producer re-run emits spurious warnings and cross-command runs duplicate views.
4. **Three disconnected sheet-numbering systems** (SheetSequenceStore, ShopDrawingComposer's in-memory counters, SheetManagerEngine prefix-scan) — and the flagship producer path doesn't actually use the sequence value it consumes; 13 catalogue types carry ISO tokens the producer can't substitute, so their sheet numbers **fail to apply at all**.
5. **Silent fallbacks on issued-drawing identity** — wrong or arbitrary title blocks, blanked title-block cells, literal `{token}` text on sheets, and level tokens erased by Renumber — all warn-only or fully silent.

The corporate-lock story ("SHA-256 checksums stop silent mutation of the shipped baseline") is **inert as shipped**: zero checksum fields exist in the drawing-types JSON, computed hashes are never persisted, and the pack registry has no checksum code at all despite CLAUDE.md describing it.

Severity totals (deduplicated): **5 Critical · 27 High · 32 Medium · ~21 Low**.

---

## 1. Critical findings

### C-1 ✅ Most of the corporate style-pack payload is runtime-dead (unbound JSON keys)
`Core/Drawing/ViewStylePack.cs:67` binds filter rules only to `[JsonProperty("filters")]`; **11 of 35 packs** — `corp-base`, `corp-clarification`, `corp-demolition-phase`, and all 8 `corp-healthcare-*` packs — key their 78 rules under `"filterRules"` (verified programmatically; only `corp-coordination` and `coord-qa` use the bound key). `StyleVgOverride` (ViewStylePack.cs:268-281) binds only long-form names, while the corporate JSON uses short forms: **280 `projColor`, 298 `projWeight`, 9 `cutColor`, 14 `cutWeight`, 15 `surfFgColor`** occurrences — all silently dropped by Newtonsoft. `visible`/`halftone`/`transparency` do bind, so packs *half*-work and the loss is invisible in casual testing. The Phase 139 alias shim (which fixed `stylePacks` at the library root) was never applied to these two levels. **Effect**: healthcare pressure-regime, MGPS and fire drawings render with no filter colour coding; 25 of 35 packs lose their authored line colours/weights.

### C-2 Style-pack `ResolveExtends` strips Phase 137/177 fields — managed templates can never engage
`Core/Drawing/ViewStylePackRegistry.cs:190-234` folds the extends chain into a new pack copying only 10 fields. `TemplateMode`, `ManagedFields`, `Discipline`, `VisualStyle`, `ViewRange`, `WorksetVisibility`, `LinkOverrides`, `ColorFillSchemes`, `CategoryDepths`, `CategoryTag7Sections` and more are discarded — and **all 35 packs use `extends`**, so every `Get()` result is stripped (the fold runs even for a chain of length 1). `DrawingTypePresentation`'s `resolvedPack.IsManaged` branch (DrawingTypePresentation.cs:565) is therefore unreachable: the **14 packs declaring `templateMode: "managed"`** silently fall through to direct-apply, and workset/link/colour-fill application always no-ops.

### C-3 ✅ Producer sheet identity ignores context — per-level batches stack all levels onto one sheet
`Core/Drawing/DrawingProducer.cs:497-517`: `CreateOrFindSheet` finds an existing sheet by `(drawingTypeId, packageId)` **only** — no level/room/scope-box component (verified in source). The view-idempotency key *does* include context, so views are correctly minted per level; they are then all placed on the *same* sheet at the same slot. `ProduceViewsPerLevelCommand` over 10 levels yields 1 sheet with 10 stacked viewports numbered for level 1 — not 10 sheets. Same defect on the scope-box and produce-and-export paths.

### C-4 ✅ AnnotationRunner has zero idempotency — every re-run duplicates all annotations
`Core/Drawing/AnnotationRunner.cs:383-413` (tags), `:267-298` (grid dims), `:625-679` (spot rules), `:559-601` (north arrow / scale bar / inline matchline curves): all create without checking for existing annotations. `AutoAnnotationRule.SkipIfTagged` (default `true`, AnnotationRulePack.cs:137) is **never read anywhere** (verified — only the config dialog references it). Re-running `DrawingTypes_SyncStyles`, a drift heal, or `DrawingTypePresentation.Apply` doubles every tag, dimension chain, and north arrow on the view.

### C-5 ✅ The corporate-lock checksum mechanism is inert; packs have none at all
`Core/Drawing/DrawingTypeRegistry.cs:447-473` fires the drift/origin-flip only when a *prior* checksum exists — but `STING_DRAWING_TYPES.json` ships **zero** `"checksum"` fields (verified), and computed hashes are written only in-memory, never persisted. Hand-editing the shipped baseline is undetectable, contradicting the file's own header and CLAUDE.md. Worse: CLAUDE.md claims "edits to corporate packs silently flip `origin` to `project` via `ViewStylePackRegistry.ComputeChecksums`" — **that method does not exist**; `ViewStylePack.Checksum` is never computed or verified anywhere.

---

## 2. Drawing Type engine core (`Core/Drawing/`)

| # | Sev | Location | Finding |
|---|---|---|---|
| E-1 | High | `DrawingTypeRegistry.cs:159-183` | `ResolveExtends` copies only ~20 fields; `TitleBlockParams`, `TitleBlockParamsBySymbol`, `TitleBlockSymbolType`, `IsoNaming`, `System`, `MaterialPack`, `OptionScope`, `ProductionRules`, `PackageId`, `TitleBlockVariantRules`, `TagTextSizeMm` are dropped — **including the leaf's own values**. Dormant only because 0/93 shipped types use `extends`; any project profile using the documented inheritance produces sheets with empty title-block cells and no ISO naming, silently. Corroborated independently by two reviewers. |
| E-2 | High | `DrawingCropApplier.cs:153-161, 243-249` | TightBbox/RoomBoundary crops assign a **model-space** box to `View.CropBox`, which Revit interprets in the crop's own transformed frame. Accidentally works on unrotated plans; sections/elevations (the built-in spool profiles use TightBbox) and rotated plans get garbage crops. |
| E-3 | High | `ManagedTemplateSyncer.cs:143-148, 336` | Managed-template minting falls back to `CopyElement` on a **non-template** live view when no "STING - " seed template exists; the copy either throws (managed mode silently degrades) or yields a junk view that `SetViewTemplateId` then rejects — and is re-minted on every run (`_(2)`, `_(3)`…). Correct API is `View.CreateViewTemplate()`. |
| E-4 | High | `ManagedTemplateSyncer.cs:588-601` | Hardcoded `VIEW_DISCIPLINE` integers `4097`/`4098`/`4095` for electrical/plumbing/coordination are **not members of Revit's `ViewDiscipline` enum** (Mechanical=4096 is the only correct one). Managed packs declaring those disciplines set a wrong/invalid discipline. |
| E-5 | High | `SheetSequenceStore.cs:51-57, 111-133` | `ReadAll` swallows any read failure into an **empty dict**; `Next` then persists just the one bucket — wiping every other counter. `WriteAll` swallows "no active transaction", handing out numbers that were never persisted (reused next session → duplicate ISO numbers). Workshared: second session's `SetEntity` fails on ownership, is swallowed, and both users receive the same sequence. |
| E-6 | Med | `ManagedTemplateSyncer.cs:536-572` | `SetManagedTemplateParameterIds` builds the param-id list then **discards it** — no `SetNonControlledTemplateParameterIds` call, contradicting the file's stated contract. Seed templates that control Scale keep overriding the per-profile `DrawingType.Scale` the pipeline just wrote. |
| E-7 | Med | `Iso19650Vocabulary.cs:294-306` | Shipped vocabulary patterns use `{prj}`, `{orig}`, `{datadrop}` — tokens neither `DrawingTokenContext.Build` emits nor the validator's `_knownTokens` allows. Users picking the shipped "Full BS 1192 / ISO 19650-2" dropdown get literal `{prj}-{orig}` text plus DT-098 warnings against the tool's own suggestion. |
| E-8 | Med | `Iso19650Vocabulary.cs:233` vs `DrawingType.cs:94` | `"ThreeD"` in the purposes list vs canonical constant `"3D"` — editor-authored 3D profiles miss the DT-095 scale exemption and every 3D-specific code path. |
| E-9 | Med | `DrawingTypePresentation.cs:60-69` | Negative view-template cache entries are evicted on every read — a missing template name pays a full view collector scan per view in a batch (defeating the cache it lives in). Plus a dead duplicate `else if` branch at `:606-609`. |
| E-10 | Med | `DrawingTypeValidator.cs:465-517` | The `[ThreadStatic]` validation snapshot is set without try/finally and carries no document identity — an exception mid-`ValidateAll` leaks doc A's asset inventory into doc B's subsequent single-profile validations. |
| E-11 | Med | `DrawingDriftDetector.cs:125-136` | The stamp reverse-index only invalidates via `DrawingTypeStamper.Stamp` — views stamped via the Properties palette, copy/paste, or Dynamo are permanently invisible to `Scan` for the session. |
| E-12 | Med | `DrawingDispatcher.cs:59-71` | First matching routing rule with an unknown `drawingTypeId` returns **null** instead of continuing to later rules. Because project rules are *prepended*, one stale project rule silently disables routing for that whole key. |
| E-13 | Low | `SheetSequenceStore.cs:62-68` | `Peek` returns the *last-used* sequence, not the next one (off-by-one vs `Next`). No live caller today; any future preview inherits the bug. |
| E-14 | Low | `RevitCategoryTree.cs:280-282, 526-527` | "MEP Fabrication Hangers" row maps to `OST_FabricationContainment` (hangers are `OST_FabricationHangers`, absent); duplicate unreachable `OST_PlumbingFixtures` entry; `OST_Toposolid` labelled "Topsoil". |

**Solid**: registry load hardening (dedupe, tolerant scale converter, ES-first overrides, graceful degradation to built-ins), consistent per-doc cache invalidation, warning-accumulation error model, and the Apply pipeline matches its documented 8-step order exactly.

---

## 3. Corporate catalogue (`STING_DRAWING_TYPES.json`)

Counts verified: **93 drawing types / 118 routing rules** — matches docs exactly. Referential integrity is **perfect**: no duplicate ids, no duplicate routing tuples, all routing targets resolve, all 93 types reachable, all 23 referenced style-pack ids exist, all slots within 0..1, scale/paper names all consistent with ids.

| # | Sev | Affected | Finding |
|---|---|---|---|
| D-1 | High | 13 types (`arch-plan-A1-1to100`, `mep-plan-*` ×4, `elec-*` ×3, `pipe-spool-A1-1to50`, `struct-plan-A1-1to100`, `arch-section-A1-1to50`, `pres-3d-axon-A1`, +1) | `sheetNumberPattern` uses the ISO form `{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}-{suit}-{rev}`. `ShopDrawingComposer.SubstituteTokens` resolves these via its `extras` dict, but `DrawingProducer.SubstituteTokens` (:765) knows only `{disc}{discipline}{lvl}{sys}{mark}{spool}{purpose}{seq}` — the ISO tokens pass through as literal braces, which are **illegal in Revit sheet numbers** → assignment throws, is caught, sheet keeps its default number. The flagship production path cannot number 13 of its own corporate types. |
| D-2 | High | `mep-plan-*` group (4 types), `elec-*` group (3), `arch-plan`/`arch-section`, `pres-3d-*` (3) | **Duplicate sheet numbers by construction**: `SheetSequenceStore` buckets counters per drawing type, but these groups share identical patterns *and* identical `isoNaming` payloads with no differentiating token — two profiles produced on the same level collide; Revit rejects the second (caught → default number, silent misnumbering). `elec-spool` carries `(vol 01, type DR)` — copy-paste from elec-plan (`pipe-spool` correctly uses `(02, SP)`). The three `pres-3d-*` types share `PRES-{disc}-{seq:D3}` with `discipline:"*"` → `PRES--001`. |
| D-3 | Med | 57/93 types | Dead `tokenProfile` appearance keys (`tagSize`, `tagStyle`, `tagColor`, `colorScheme`) — deliberately removed from the POCO (`DrawingType.cs:598-603`) but never cleaned from the corporate JSON; authored appearance intent silently ignored. |
| D-4 | Med | routing #82, #89, #91, #95, #101 | The healthcare routing block exists twice (regex-predicate + literal encodings); 5 literal rules are fully shadowed by earlier regex rules. Harmless today (same targets) — but any future edit to only one encoding diverges silently. |
| D-5 | Med | `pipe-spool-A1-1to50`, `duct-spool-A1-1to50` | Slot `ISO` (0.55–0.95 × 0.55–0.95) overlaps slot `BOM` (0.78–0.98 × 0.55–0.95) by 0.17×0.40 normalized — also present in the C# built-in fallback (`MakeFabSpool`), so shipped data trips the validator's own overlap check. |
| D-6 | Med | 7 health types | Unknown tokens in `sheetNamePattern`: `{area}` ×5, `{room}`, `{gas}` — render literally and warn on every ValidateAll. |
| D-7 | Med | 4 health types | `scale: "NA"` with purposes (`Schedule`, `Schematic`) the validator doesn't exempt → perpetual DT-095 warnings on shipped data. `Schematic` (8 types) and `Clarification` (2) aren't in the canonical purpose set at all. |
| D-8 | Med | 23 health types | `annotation.autoDimension` is not a POCO key (real key: `autoDim`) — silently dropped (all values currently `false`, so behavioural delta is nil — for now). |
| D-9 | Low | 92 types | Naming-convention splits: `viewportTypeName` `"STING - Standard Viewport"` ×70 vs `"STING Viewport"` ×22 (all healthcare) vs stock `"Title w Line"` ×1 — one spelling always warns per project. `titleBlockFamily` logical `STING_TB_*` vocabulary ×60 vs display names ×23 (healthcare + legacy) that bypass the resolver/factory. Section marker near-duplicates: `STING_SECTION_HEAD` vs `STING_SECTION_MARK`, `STING_ELEV_MARKER` vs `STING_ELEV_MARK` — none of the four appear anywhere in C#. |

---

## 4. View Style Packs + AEC filters

Counts: **35 packs** (docs/comment say 31) · **298 filters** (docs claim 199) · **81 healthcare filters** (docs claim 58) · 373 leaf rules. Data hygiene where bound is excellent: zero duplicate ids/names, all colours valid hex, weights 1-16, all 97 pack→filter references resolve, all extends chains resolve.

| # | Sev | Location | Finding |
|---|---|---|---|
| V-1 | Crit | — | See **C-1** (unbound `filterRules` + short-form vgOverride keys). |
| V-2 | Crit | — | See **C-2** (`ResolveExtends` strips managed-mode fields). |
| V-3 | High ✅ | `ViewStylePackApplier.cs:517` | `new ElementParameterFilter(rules, false /* OR semantics */)` — the second arg is `inverted`; multiple rules in one `ElementParameterFilter` are **AND**ed. Multi-material class filters match nothing (`mat==A AND mat==B`). Correct form: `LogicalOrFilter` over per-rule filters. Compounding: `ApplyMaterialClassOverrides` is never called from `Apply` (dead code), and never writes the override graphics its doc-comment claims. |
| V-4 | High | `MergeRecoveryStubs.cs:454-458`, `ViewStylePackApplier.cs:152-309`, `AecFilterFactory.cs:52-55` | Applier performance: `ResolveFilterIdCached` runs a full collector on **every call** despite the name; per rule the applier does up to two linear scans over 298 definitions plus duplicate-name collectors repeated by the factory; line/fill-pattern resolvers run their own collector per pattern per rule; `InvalidateCache` is a no-op. Batch of N sheets × F rules ⇒ O(N·F) whole-model scans. |
| V-5 | High | `TokenProfileApplier.cs:665-675` | `ApplyPresentationPreset` walks **every ElementType in the document** (unfiltered, 10k+ typical) with 4 writes each, per view applied. The category pre-filter added to `ApplyTagStylePreset` was never applied here. Four sibling methods each still run their own per-view collector after the "single-pass merge". |
| V-6 | Med | `STING_AEC_FILTERS.json` (8 filters) | `iso-status-*` filters bind only `OST_Sheets`, which is **not filterable** in Revit view filters — unmintable by design; their param `STING_DOC_STATUS` also isn't in the registry. |
| V-7 | Med | 8 clinical/EES/MGS/water filters | `param: "Family Name"` with `kind=builtin` — `Enum.TryParse` fails (should be `ALL_MODEL_FAMILY_NAME`, used correctly 62× elsewhere) → filters never mint. |
| V-8 | Med | `plumb-dead-legs`, `elec-lps-bonding` | Invalid ops: `greater_than` (grammar: `greater`) kills the whole filter; `isNotEmpty` (should be `hasValue`) kills one OR-leaf, silently halving scope. |
| V-9 | Med | `ViewStylePackApplier.cs:288, 302-304` | Non-nullable `bool` defaults make an explicit pack `visible:true` indistinguishable from unset — registry defaults override packs, inverting the documented "pack wins" precedence. |
| V-10 | Med | multiple | Silent-catch convention violations: `ViewStylePackApplier.cs:121, 140-141, 559, 580`; `TokenProfileApplier.cs:139, 150, 501`; `MergeRecoveryStubs.cs:439, 451`. |
| V-11 | Low | `ViewStylePackApplier.cs:347-349` | Workshared warning fires per view per apply on every non-workshared project even when the pack sets no workset visibility. |
| V-12 | Low | data | 12 filter params absent from `PARAMETER_REGISTRY.json` (resolve only via last-resort by-name scan); `struct-pt-tendon` lists invalid `OST_StructuralRebar` (should be `OST_Rebar`); VSP `routing[]` (13 entries) has no POCO binding — dead data (known: ROADMAP notes routing is unconsumed); `schemaVersion: 1.3` vs POCO `version` int — packs get no version gate (AEC filters do). |

---

## 5. Title blocks

| # | Sev | Location | Finding |
|---|---|---|---|
| T-1 | High | `ShopDrawingComposer.cs:769-800, 299-345` | The fabrication sheet path **never calls `TitleBlockResolver.ToConcreteFamily`** — drawing types declare logical names (`STING_TB_ASSEMBLY_PIPE`) but the factory saves versioned families (`STING_TB_ASSEMBLY_PIPE_v1.0`), so tier-1 literal match and the tier-3 unversioned discipline dict both fail → every spool sheet lands on "first available title block". The resolver's own header describes this exact bug as its raison d'être — the composer was never wired to it. |
| T-2 | High ✅ | `DrawingProducer.cs:552-558` | A comment says "the producer never falls back to an arbitrary title block", then the code does exactly that — silent `FirstOrDefault()` over all title blocks, **no warning appended** (verified in source). Blank/unresolvable family names also match *any* loaded title block via the `IsNullOrEmpty ||` clause. Batch production can issue sheets with wrong corporate identity, silently. (`DrawingTypeSheetAdapter` behaves correctly — returns invalid id so the picker asks — the two paths diverge.) |
| T-3 | High | `TitleBlockParamApplier.cs` (whole), `TitleBlockRevisionSyncer.cs:183-189`, `DrawingHealTitleBlocksCommand.cs:47` | `PRJ_TB_LOCK_BOOL` is documented in `MR_PARAMETERS.txt:2474` as "prevents TitleBlockParamApplier from overwriting manually set values" — **no code in the declarative pipeline reads it** (only legacy `Docs/TitleBlockCommands.cs:388` does). Heal/RevisionSync/Migrate clobber locked cells. |
| T-4 | High | `MaterialTitleBlockTokens.cs` vs `TitleBlockParamApplier.cs:221-225` | `${MAT_*}` tokens are a dead feature: `MaterialTitleBlockTokens.Resolve` has **zero callers**; the `${...}` handler only reads ProjectInformation → resolves null → blanks the cell (ACC-07 always-write). 170 lines of unreachable code incl. an O(all-elements) usage scan. |
| T-5 | High | `TitleBlockParamApplier.cs:228-247` + `TitleBlockParamMigrateCommand.cs:198` | All `{token}` substitution is gated on a non-empty token dict; the migrate command calls `Apply` with **no tokens** → literal `{disc}` / `{seq:D4}` text is stamped onto every migrated sheet's title block. Asymmetric semantics (missing `${…}` → blank; unknown `{…}` → literal) surface nowhere; `TitleBlockApplyResult.ParametersDeclared/Missing` are declared but never populated. |
| T-6 | Med | `TitleBlockResolver.cs:69-71, 198-202` | Any `STING_TB_SHEET*` containing "PRESENT" hard-returns the **A1** presentation family regardless of paper size; blank paper silently normalizes to A1; A2/A4 return the logical name unchanged → feeds T-2's match-anything path. Two different `IFamilyLoadOptions` semantics (`overwriteParameterValues` false vs true) depending on entry path. |
| T-7 | Med | `TitleBlockFactory.cs:644-807` | Master-seed propagation is a whole-sheet affine remap: A1→A3 halves geometry but steps text only one ISO tier (5→3.5mm expected, cells get 2.5mm-in-5mm-cells overflow); landscape→portrait is a 2× directional stretch; arcs/splines and curved regions are left unscaled. |
| T-8 | Med | `TitleBlockFactoryCommands.cs:74-83` | `TitleBlock_Create` offers `Math.Min(4, …)` command links — only 4 of the 31 concrete families can be minted individually. |
| T-9 | Med | `DrawingHealTitleBlocksCommand.cs:66-69` | Heal dialog promises "titleBlockParams + per-symbol overlays" — no overlay code exists anywhere. Heal is a param re-stamp only: it does **not** detect/replace a wrong title-block family, so a sheet on the wrong family reports "healed" while staying wrong. |
| T-10 | Med | `Families/AssemblyTitleBlocks/*.params.txt` | Spec drift: ELEC stub requires `ELC_PNL_NAME`, `ELC_PNL_VOLTAGE`, `ELC_GLAND_COUNT_NR` — none exist in `MR_PARAMETERS.txt` (actual `ELC_PNL_NAME_TXT`; no voltage/gland params). PIPE/DUCT/COND/HANGER stubs transpose A1 dims ("594×841"). HANGER inherits pressure-test/insulation params (copy-paste). `ASS_FAB_LOC_TXT` vocab: stubs "WORKSHOP/SITE" vs `STING_PARAMS_V4.txt` "SHOP/FIELD/VENDOR" vs code writes "WORKSHOP". No GUIDs in any stub despite README's bind-by-GUID step. README ships 8 specs; CLAUDE.md says 7 (stale). |
| T-11 | Low | `TitleBlockFactory.cs:344-349, 427-462` | `SharedParametersFilename` restore skips when the original was empty → users with no SP file permanently inherit `MR_PARAMETERS.txt` globally. `.rfa` save path = directory of the *first open* project — with multiple projects open, output lands in an arbitrary folder the resolver may not search. |
| T-12 | Low | `TitleBlockRevisionSyncer.cs:179-236` | Unbound/read-only params silently return false → "0 params written" with zero diagnostics; `PRJ_TB_ISSUE_SUMMARY_TXT` (registry: free-text issue purpose) is overwritten with the revision description each sync — with T-3, hand-authored cells are clobbered. Revision API usage otherwise correct. |
| T-13 | Low | `MergeRecoveryStubs.cs:485-492`, `TitleBlockSlotCommands.cs:96-115, 315-341, 539-651` | Stub 4-arg `Peek` returns empty + clears `unresolved` ("real impl lost to the merge") — a landmine for any future caller. Slot commands: JSON-first but still call `doc.EditFamily` per run (expensive, usually pointless); "every unplaced view if none selected" comment vs selection-required code; `ToggleBIMMode` claims "positions transferred 1:1" without transferring anything; multi-title-block sheets handled inconsistently (`FirstOrDefault` here vs all-instances in the applier). |

---

## 6. Annotation, dimensioning, match lines, legends, labels

| # | Sev | Location | Finding |
|---|---|---|---|
| A-1 | Crit | — | See **C-4** (AnnotationRunner idempotency). |
| A-2 | High | `AnnotationConditionEvaluator`, `Dimensioning/*` | The Phase 175 rule engine is largely **dead wiring**: `Evaluate` has zero call sites; `GridDimensioner`/`MEPDimensioner`/`DrainageInvertDimensioner` are referenced only by each other; `AnnotationRunner.DimByRules` routes everything to its own primitive `DimGrids`/`DimLevels`. Rule-pack fields `condition`, per-rule `tagFamily`, `densityMode`, `minSizeMm`, `orientation`, `tag7Depth`, `skipIfTagged`, `dimensionStrategy` are silent no-ops — 48 shipped types declare them. |
| A-3 | High | `Tags/LegendBuilderCommands.cs:3272-3401` | `UpdateLegendCommand` deletes all content of the existing (sheet-placed) legend view, then creates a **new** view with a "(1)" suffix — the placed view stays permanently blank while the completion dialog claims "sheet placements are preserved". The needed in-place populate (`PopulateLegendContent`, `internal static` at :221) was skippable only because a comment wrongly believed it private. |
| A-4 | High | `AnnotationRunner.cs:276-295` | `DimGrids` appends **both grid orientations** into one `ReferenceArray` with a near-arbitrary dim line — on any project with orthogonal grids `NewDimension` throws (parallel references), caught per-view → grid auto-dimensioning effectively never succeeds on real projects. |
| A-5 | High | `Dimensioning/GridDimensioner.cs:120-129, 46-48` | The intended replacement has its orientation predicates **inverted** (`IsVertical` ⇔ `|dX|≥|dY|`), so both chains build dim lines parallel to their grids — guaranteed rejection whenever this path gets wired (latent, per A-2). `DrainageInvertDimensioner` subtracts wall thickness on top of `Diameter/2` — lands ~2 wall thicknesses below the true BS EN 12056 invert. |
| A-6 | High | `MatchLineEngine.cs:504-508 vs :451, :738-741` | Dog-leg pairs are stamped `guid:segN` but looked up by bare `guid` → never found on re-run → **curves duplicate every sweep**, and the orphan pruner strips only the prefix so stale segments are never pruned. Contradicts the file header ("re-runs find the existing pair and update in place"). |
| A-7 | High | `MatchLineEngine.cs:390-409, 460-462, 585-594` | The existing-pair index tracks only `DetailCurve`s; the update path deletes curves but creates fresh `TextNote` captions each time — `MatchLine_Sync` (recommended after every renumber) stacks a new "see XXX →" note per end, per run. |
| A-8 | Med | `AnnotationRunner.cs:462-524` | `ResolveTagTypeId` matches family name across **all** FamilySymbols (wrong category/model families accepted → per-element create exceptions); substring fallback; a missing named family silently degrades to "first loaded tag of the category" with no warning. |
| A-9 | Med | `AnnotationRunner.cs:443-458 + 387-393` | `GetElementCentre` returns `null` (comment promises an `XYZ.Zero` sentinel); caller does no null check → warning floods and zero tags on floor/ceiling-heavy categories. |
| A-10 | Med | `AnnotationRunner.cs:322-327` | `DimLevels` pins the level chain at project origin (X=0,Y=0) — off-crop/invisible for any section not passing through origin; equal-elevation levels → zero-length line exception. |
| A-11 | Med | `Data/LABEL_DEFINITIONS.json` | Structurally valid (no duplicate keys; 9,320 label rows consistent) but **524 mojibake strings** (double-encoded UTF-8): 162 `prefix_override` (" â€” "), units like "mÂ²"/"Â°", and — worst — **6 `family_name` values** (e.g. `"STING - Tie-In Point Tag (Pipe â€” Plumbing & Hydraulic)"`) that break exact family-name matching. |
| A-12 | Med | `LegendBuilderCommands.cs:176-182, 989-993, 2688-2696, 2793-2814` | Legend creation/placement duplicates on re-run: name-uniquify instead of detect-and-reuse; `CanAddViewToSheet` returns true for legends already on the sheet → `PlaceLegendOnAllSheets` re-run stacks a second viewport at the identical point on every sheet. |
| A-13 | Med | `LegendBuilderCommands.cs:951-1021, 737` | Sheet-placement math discards the title block bbox `Min` (families with origin at bottom-right → legend placed off-sheet); viewport footprint hardcoded 0.4×0.3 ft regardless of entry count; failed filled-region duplication silently returns the base type → swatch renders in an arbitrary colour. |
| A-14 | Med | `MatchLineCommands.cs:135-141`, `MatchLineConfig.cs:91-105` | `MatchLine_Validate` compares stamped-pair count against scope-box-pair count — any scope box driving multiple level views (the normal case) yields bogus drift warnings. Config registry caches one static config **for all documents**. |
| A-15 | Low | `LegendBuilderCommands.cs:292/1407→706-709`, `AnnotationRunner.cs:401-409` | Per-entry `FilledRegionType` collector inside the entry loop; 11 param writes per element per run even when unchanged (compounds with A-1 on re-runs). |

**Solid**: `denseUntilScale` comparison is on the correct side; legend transaction discipline (one transaction per command, clean cancellation) is good; match-line mm→ft conversions use a named constant; the unwired condition evaluator is itself a clean fail-open parser.

---

## 7. Sheet production pipeline & numbering

| # | Sev | Location | Finding |
|---|---|---|---|
| P-1 | Crit | — | See **C-3** (sheet identity ignores context). |
| P-2 | High | `DrawingProducer.cs:765-791, 588-631` | Producer numbering is disconnected from the sequence machinery: `{seq}` substitutes `int.TryParse(ctx.Tag) ?? 1` — but `ctx.Tag` is a **level name** in every batch command, so `{seq}` is always `0001`; meanwhile `SheetSequenceStore.Next` **is** consumed (:599) but its value is only stamped into `STING_SHEET_SEQUENCE_INT`, never used in the number. No uniquify-on-collision fallback (contrast ShopDrawingComposer). Combined with D-1/D-2: the flagship path mis-numbers or fails to number much of the catalogue. |
| P-3 | High | `DrawingRenumberCommand.cs:78-86` | Renumber rebuilds tokens with no `levelCode`/`mark`/`spool` — `{lvl}` substitutes **empty** (not literal), so `A-RCP-L02-001` → `A-RCP--001`, and since the group key is only (dtId, pkg), sheets across all levels are compacted into one flat sequence with level identity erased. A user closing a two-sheet gap silently corrupts the register. |
| P-4 | High | `DrawingRenumberCommand.cs:117-148` | Pass-2 collisions with locked/unstamped/other-bucket sheets throw per sheet and leave the sheet permanently numbered `ZZ_STING_RENUM_<ticks>_NNNN` — committed, warn-log only. The store's high-water mark is then set to the group count even when locked sheets hold higher numbers → next production collides again. |
| P-5 | High | `DrawingProducer.cs:457-479`, `BatchProduceCommands.cs:345-364` | Section batch production builds geometrically wrong section boxes: producer puts the elevation range on **Y** with `Transform.Identity` (a downward "plan-section" frame); the command path puts height on **Z**; neither constructs the required frame (BasisY=up, BasisZ=view direction); north-south grids produce `Min > Max` → `CreateSection` throws. The two paths also disagree on which axis is height. |
| P-6 | High | `DrawingProducer.cs:26` (sole occurrence) | `ctx.ScopeBox` is **dead** — `ProduceViewsFromScopeBoxesCommand` passes the box in, nothing reads it; crop comes only from the profile's static `Crop.ScopeBoxName`, so "produce from scope boxes" doesn't crop to the box. The parallel `GenerateFromScopeBoxesCommand` *does* set `VIEWER_VOLUME_OF_INTEREST_CROP` — two entry points, materially different output. Also: two untagged scope boxes on the same level share one idempotency key → the second silently produces nothing. |
| P-7 | High | `Docs/SheetTemplateEngine.cs:295-296` vs `Core/Drawing/SheetPlacementBridge.cs:140-141` | **Slot semantics diverge between the two sheet-creation paths**: template engine treats `NormX` as the slot *center*; the placement bridge treats it as *bottom-left* (adding `NormW/2`). The adapter copies coords "verbatim" claiming both use the same convention (false) — the same profile places viewports offset by half a slot depending on entry point. Further divergences: drawable-zone frames, numbering (`CreateSheetFromTemplate` ignores `SheetNumberPattern` entirely, uses prefix-scan `A-001`), and the template path never touches `SheetSequenceStore`. |
| P-8 | Med | `DrawingProducer.cs:648-674` vs `SheetPlacementBridge.cs:342-380` | Producer calls `Viewport.Create` unconditionally — the AUTO-3 schedule fix (`ScheduleSheetInstance`), per-slot viewport types (SLOT-1) and type-compat checks (SLOT-3) live only in `PlaceAccordingToSlots`, which the producer never calls. Schedule rules create views that can never be placed. |
| P-9 | Med | `DrawingProducer.cs:187-195, 692-698`; `DrawingProduceAndExportCommand.cs:173` vs `BatchProduceCommands.cs:132` | Idempotent re-runs re-place already-placed views (spurious warning per view; reused sheets counted as "produced"); `ProduceAndExport` sets `ctx.Tag = level.Name` while `ProducePerLevel` leaves it null → different context tags → running both **duplicates every per-level view** despite both claiming idempotency. |
| P-10 | Med | `DrawingProducer.cs:799-814` | Title-block tokens built with `doc: null` (the caller has `doc` in scope) and no `seq` → `{project}`/`{originator}` blank and `{seq:Dn}` literal in producer-path title-block cells, while fabrication and SheetManager paths fill them — same profile, different cells per entry point. |
| P-11 | Med | `ShopDrawingComposer.cs:66-93, 675-696` | Spool numbering is in-memory per session — restart resets to 0 and `EnsureUniqueSheetNumber` degrades to `-A…-Z` then a random 6-char suffix. Third parallel numbering mechanism; none consult each other. `EnsureUniqueSheetNumber` re-collects all sheets per composed sheet (O(N²)). |
| P-12 | Med | `DrawingProducer.cs:745-763, 379-392` | First-run perf: view-name uniquify runs a full `OfClass(View)` collector per probe (up to 100/view, O(views²)); schedule category resolution iterates ~1,400 `BuiltInCategory` values per rule with a bare `catch { }`. |
| P-13 | Low | `ShopDrawingComposer.cs:97-106`; `GenerateFromScopeBoxesCommand.cs:224-235`; `BatchProduceCommands.cs:172` | Spool PLAN/ISO slot centers sit at Y = 594 mm = the full A1 height (upper half off-sheet), hardcoded regardless of title block. Scope-box level resolution silently falls back to the lowest level on a typo. `Ordinal` vs `OrdinalIgnoreCase` scope-box prefix filtering between commands — `sting::` boxes handled by one, dropped by the other. Metric conversion `3.281` (~0.02% error). |

**Solid**: SheetSequenceStore's ES-inside-the-transaction design is right (rollback keeps counters and sheets consistent); the stamped-parameter idempotency architecture and per-batch cache hygiene are genuinely good; `ShopDrawingComposer.SubstituteTokens` and `TitleBlockParamApplier`'s template resolver are correct and well-specified; exports run correctly outside transactions.

---

## 8. Command wiring, reachability, documentation drift

Dispatch is exact-match, case-sensitive (`StingCommandHandler.cs:181`); unknown tags produce a warn + "Unknown Command" dialog.

| # | Sev | Finding |
|---|---|---|
| W-1 | High ✅ | **Two workflow presets are entirely inert**: `WORKFLOW_PenetrationRegister.json` and `WORKFLOW_PenetrationSweep.json` use per-step keys `"command"`/`"name"` instead of `"commandTag"`/`"label"` (verified) — every step deserializes with `CommandTag = null` and fails validation. Even with fixed keys, 4 tags have no `ResolveCommand` case (`BuildSeedFamilies`→`Seeds_Build`, `Penetrations_DetectAndPlace`, `Validation_PenetrationCoverage`, `DrawingTypes_FromScopeBoxes` are handler-only). The documented 5-step Penetration Sweep cannot run at all. |
| W-2 | High | **The MatchLine suite (5 commands + ~900-line engine) is user-unreachable**: `MatchLine_Generate|Sync|Validate|ValidateBundle|Inspect` have handler cases but **no XAML button, no NLP pattern, no workflow step, no ResolveCommand case** anywhere. The documented tag `MatchLines_Create` never existed. |
| W-3 | Med | **11 of the 21 documented command tags don't exist** under those names (CLAUDE.md drift): `DrawingTypes_BrowserOrganize`→`Drawing_BrowserOrganize`, `DrawingTypes_Produce`→six `DrawingTypes_Produce*` variants, `ManagedTemplates_*`→`DrawingTypes_{ConvertToManaged,DetachManaged,RegenerateTemplates}`, `TitleBlocks_Factory`→`TitleBlock_Create/CreateAll`, `TitleBlocks_MigrateCsv`→`DrawingTypes_MigrateCsv`, `TitleBlocks_Migrate`→`DrawingTypes_MigrateParams`/`TitleBlock_MigrateLegacy`, `MatchLines_Create`→`MatchLine_Generate`, `PresentationStyle_Setup`→`DrawingTypes_PresentationSetup`, `DrawingTypes_BatchProduce`→none. |
| W-4 | Med | **11 broken NLP intents**: NLP emits `DrawingTypes_BrowserOrganize` (handler knows `Drawing_BrowserOrganize`) plus 10 legend intents (`CreateMasterLegend`, `CreateDisciplineLegend`, …, `SyncLegend`) that no dispatcher resolves — real equivalents exist under different names. All dead-end in the "Unknown Command" dialog. |
| W-5 | Med | `DrawingSyncStylesCommand` and `GenerateFromScopeBoxesCommand` classes are never dispatched — their tags route to inline handler reimplementations, so fixes to the command classes don't reach users. `docs/UNREACHABLE_COMMANDS_TRIAGE.md` is stale in both directions (claims alias tags that don't exist; lists now-wired commands as dead). |
| W-6 | Low | Doc-count drift: filters 298 actual vs 199 claimed; healthcare filters 81 vs 58; packs 35 vs "31-pack catalogue" comment; title-block stubs 8 shipped vs 7 documented; `ViewStylePackRegistry.ComputeChecksums` described in CLAUDE.md does not exist. |

**Cleanly wired**: all 30 `DrawingTypes_*`/`TitleBlock*`/`AecFilters_*` dock-panel buttons have exact handler cases (zero dead buttons); the legend subsystem is fully coherent (31 classes = 31 handler cases = 31 buttons).

---

## 9. Overlap with already-tracked ROADMAP items

These review findings intersect items already recorded in `docs/ROADMAP.md` — listed here so they aren't double-counted as new discoveries:

- **`tagFamilies` debt on non-MEP types** (~87 key mismatches, 19 nonexistent `STING_TAG_*` families, `STING - Generic Tag` on ~22 healthcare types) — known, deliberately deferred (Phase 198 notes). This review adds the *mechanism* detail: AnnotationRunner's silent fall-through (A-8) is what makes the debt invisible at runtime.
- **Title-block revision rebinding to built-ins** (Phase 199) — T-12's clobbering behaviour strengthens the case for it.
- **`stylePacks` root alias fix pending Revit validation** — this review shows the validation will still under-deliver until C-1's *nested* key gaps (`filterRules`, short-form vgOverrides) are also fixed.
- **Style-pack `routing[]` unconsumed** — V-12 confirms; still declarative-only.
- **SEQ pad project-global** — by design; not counted as a defect.
- **Tag text-size consumer unwired** (`DrawingProducer`/`AnnotationRunner` should pick the size-variant tag) — consistent with A-2's broader dead-wiring picture.
- **Fire suppression drawing types missing** — catalogue gap, already scoped as a fast-follow.

---

## 10. Prioritised remediation plan

**P0 — data corrections + one-line fixes with outsized blast radius (no behaviour redesign):**
1. Rename `filterRules`→`filters` in the 11 packs *or* (better) add `[JsonProperty]` aliases for `filterRules`, `projColor`, `projWeight`, `cutColor`, `cutWeight`, `surfFgColor` (C-1). Data-only + POCO attributes; unlocks all healthcare styling.
2. Fix both `ResolveExtends` folds to copy all fields (E-1, C-2) — mechanical; enables managed templates and safe project inheritance.
3. Fix `WORKFLOW_Penetration*.json` step keys and the 4 unresolvable tags (W-1).
4. Fix the mojibake in `LABEL_DEFINITIONS.json` (A-11) — scripted re-encode; the 6 `family_name` values first.
5. `ElementParameterFilter(rules, false)` → `LogicalOrFilter` (V-3); `ALL_MODEL_FAMILY_NAME` for the 8 "Family Name" filters (V-7); two invalid ops (V-8).
6. Include context in the producer's sheet key (C-3) — small change, fixes stacked-sheet production.

**P1 — correctness of issued output:**
7. Unify token substitution: route `DrawingProducer` through the composer's full token set + `DrawingTokenContext.Build(doc, …, seq)` (D-1, P-2, P-10); fix Renumber's token context (P-3) and pass-2 sentinel recovery (P-4).
8. Wire `ShopDrawingComposer` through `TitleBlockResolver` (T-1); warn (or prompt) instead of silent-fallback title blocks (T-2); honour `PRJ_TB_LOCK_BOOL` in the declarative pipeline (T-3).
9. Add idempotency to AnnotationRunner (read `skipIfTagged`, pre-collect existing tags/dims per view) (C-4) and fix the match-line dog-leg/caption keys (A-6, A-7); in-place legend refresh via `PopulateLegendContent` (A-3).
10. Fix section-box frame construction on both paths (P-5); crop-frame coordinates in `DrawingCropApplier` (E-2); read `ctx.ScopeBox` or delete it (P-6).
11. Persist checksums (or drop the corporate-lock claim from docs) (C-5).

**P2 — consolidation and hygiene:**
12. Converge on **one** sheet-numbering service and **one** slot-coordinate convention (P-7, P-11); one scope-box production path (P-6).
13. Wire or delete the dead rule engine (condition evaluator + strategy dimensioners — fixing GridDimensioner's inverted predicates first) (A-2, A-5); wire or delete MatchLine commands (W-2); delete `${MAT_*}` or wire it (T-4).
14. Perf pass: real caches in the style applier (V-4), category-filtered `ApplyPresentationPreset` (V-5), view-name uniquify without full scans (P-12).
15. Documentation pass: CLAUDE.md command tags (W-3), counts (W-6), NLP intent tags (W-4), triage doc refresh (W-5), params-spec drift (T-10).

---

*Full per-finding evidence (code excerpts, exact line references, failure scenarios) is preserved in the per-area sections above. All Critical and spot-checked High findings were verified directly against source on this branch; remaining findings carry their reviewers' line-level evidence.*
