# Drawing catalogue — test plan

**Scope:** the Drawing Type Editor, the 36 view style packs, the 290-filter registry, all 93
drawing types, and the annotation / dimension / style-pack engines behind them.

**Written:** 2026-09-24, against `claude/drawing-catalogue-fixes` (Phase 294).

**Read this first.** Everything in §1 runs on a plain test host and is already green. Everything
in §2 needs Revit and **has never been run**. The split is the point of this document: the
automated half proves the *data and the vocabulary agree*, and it cannot prove that a dimension
lands in the right place. Do not read a green §1 as a working drawing.

---

## 0. Why this catalogue needs a test plan at all

Every defect fixed in Phase 294 had the same shape: **data declared an intention and an engine
silently declined it.** Nothing threw. Nothing warned. The drawing came out missing an annotation,
or carrying the wrong tag, and the only symptom was "the tool didn't run".

That shape defeats manual testing, because a correct run and a silent no-op look identical. So the
gates below are built to make the *absence* of an effect detectable:

| Mechanism | What it makes impossible |
|---|---|
| `AnnotationRuleKinds` as the single vocabulary | A `ruleType` in the JSON with no handler, or a handler with no declared name |
| `DT-139` as an **Error**, not a warning | Shipping a rule that places nothing |
| Bidirectional vocabulary tests | A gate that only checks the direction its author happened to think of |
| Reading the vocabulary from the source it guards | A test drifting from the switch it protects |
| `ApplyWeight` treating a stray `0` as unset | One bad value discarding a whole category override |
| Whole-rule replace on filter merge | Two rules for one filter partially merging into an unauthored result |
| `CanCarryViewTemplate` whitelist | Minting a template Revit cannot assign |
| A terminal `*/*` routing rule | A profile resolving to no style pack at all |

---

## 1. Automated — runs now, no Revit

### 1.1 How to run

```bash
dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj
```

Just the catalogue gates:

```bash
dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter "FullyQualifiedName~Drawing|FullyQualifiedName~SheetNumberPolicy"
```

The corporate-lock check (must be run after **any** edit to `STING_DRAWING_TYPES.json`):

```bash
dotnet run --project tools/StampDrawingTypeChecksums -- --check
```

Path discipline:

```bash
powershell -NoProfile -File tools/check_path_discipline.ps1
```

### 1.2 What each gate asserts, and the defect it exists for

#### `DrawingAnnotationVocabularyTests` — 17 methods

| Test | Asserts | Defect it guards |
|---|---|---|
| `Every_shipped_ruleType_is_in_the_implemented_vocabulary` | Every `annotation.rules[].ruleType` in the catalogue is a name `AnnotationRuleKinds` declares | **56 of 334 rules placed nothing.** 8 ruleTypes fell through both the tag filter and the dim filter with no warning |
| `Every_declared_ruleKind_belongs_to_a_pass` | No registry entry has `Pass == Unknown` | The reverse direction: a name declared but unhandled |
| `Resolve_round_trips_every_declared_name_case_insensitively` | Case and whitespace tolerance for every name | A JSON casing difference silently disabling a rule |
| `Unknown_ruleType_resolves_to_null_not_to_a_default` | `Resolve` returns **null**, not a fallback | The original bug: unknown behaved like a no-op instead of an error |
| `Null_or_blank_ruleType_defaults_to_AutoTag` | Matches the POCO default | Divergence between POCO default and registry default |
| `Room_and_space_aliases_force_their_own_category` | `AutoTagRoomName` on a "Walls" row still targets Rooms | An alias tagging the wrong category |
| `No_style_pack_declares_the_same_filter_twice` | No pack lists one filter twice | **4 pairs shipped, every pair disagreeing** on colour or weight; the later row silently won |
| `Every_pack_filter_rule_names_a_filter_the_registry_defines` | All 151 referenced names exist in the 290-filter registry | A rule that can never be lazy-created |
| `Every_drawing_type_names_a_style_pack_that_exists` | No dangling `viewStylePackId` | A typo silently yielding no styling |
| `Every_drawing_type_resolves_to_a_style_pack` | Explicit id **or** a matching routing rule, for all 93 | **8 of 93 profiles had neither** — no VG overrides, no filters, silently |
| `Every_pack_extends_target_exists_and_no_cycle` | The whole inheritance graph | A broken chain collapsing a pack to its own fields |
| `Style_pack_routing_uses_real_purpose_and_discipline_vocabulary` | Routing purposes are real `DrawingPurpose` values; disciplines are the codes the types carry | The shipped table used `Coord`/`QA`/`ClientReview`/`ARCH`/`MEP` — **none could ever match** |
| `Style_pack_routing_ends_in_a_catch_all` | Last rule is `*`/`*` with no phase | Without it the fallback guarantees nothing |
| `No_profile_claims_NTS_in_its_id_while_carrying_a_real_scale` | NTS ⇒ `scale: "NA"` | **4 profiles had `scale: 1`** → a real 1:1 view and 5 mm tag text instead of 2.5 mm |
| `Schedules_carry_no_scale` | `purpose: Schedule` ⇒ `scale: "NA"` | `view.Scale` on a ScheduleView throws; it was swallowed as a warning on every run |
| `Print_colour_schemes_come_from_one_closed_vocabulary` | Only the 5 known schemes | **Two spellings for one concept** (`Monochrome` 38, `BlackAndWhite` 28), neither validated |
| `Auto3DTag_is_declared_at_most_once_per_profile` | One row per profile | 5 profiles × 8 rows = 40 rows expressing 5 decisions |

#### `DrawingCategoryNameTests` — 6 methods

| Test | Asserts | Defect it guards |
|---|---|---|
| `Every_annotation_rule_category_resolves_to_a_real_category` | Every rule category resolves as a BIC **or** a display name | The engines' first resolver accepted only `OST_` names while the catalogue writes display names — **every shipped rule would have collected nothing** |
| `Every_pack_vgOverride_key_resolves_to_a_real_category` | Every `vgOverrides` key resolves | `"Insulation"` is a **subcategory**, not a category, so two packs' overrides were dropped |
| `Every_tagFamilies_key_resolves_to_a_real_category` | Every `tagFamilies` key resolves | 7 keys were PascalCase-without-spaces, so the declared family was silently replaced by "first loaded tag" |
| `No_tagFamilies_key_uses_the_PascalCase_no_space_spelling` | Names the exact mistake | `StructuralColumns` is what a contributor naturally types, and it fails silently |
| `Forced_categories_are_display_names_not_BIC_strings` | `ForcedCategory` never starts with `OST_` | **A regression introduced during this work**: `"OST_Rooms"` broke the 12 profiles keying their room tag under `"Rooms"` |
| `The_display_names_the_catalogue_actually_uses_all_resolve` (Theory ×7) | Doors, Windows, Walls, Structural Columns, Pipes, Ducts, Roofs | If any one stops resolving, a dimension engine goes quiet again |

#### `DrawingSlotVocabularyTests` — 5 methods

| Test | Asserts | Defect it guards |
|---|---|---|
| `Vocabulary_matches_the_shipped_switch` | The local mirror equals `SheetPlacementBridge.KnownSlotViewTypes`, read from source | **Without this the other four tests check the wrong list** |
| `Every_slot_viewType_is_a_term_the_predicate_discriminates_on` | No unknown terms | `IsViewTypeCompatible`'s default arm **allows any view**, so a typo and a deliberate term are indistinguishable |
| `Schematic_profiles_use_the_schematic_slot_term` | Schematic drawing slots say `Schematic` | **3 spellings across 8 profiles**; 4 said `Section`, which *rejects* the drafting view a schematic is produced as |
| `No_schedule_or_legend_profile_asks_for_a_crop_box` | `crop.kind: None` on Schedule/Legend | A ScheduleView has no crop box; the applier warned every run |
| `No_schedule_or_legend_profile_is_bound_to_a_managed_pack` | Resolved `templateMode != managed` | Revit rejects `ViewTemplateId` on a schedule, so a managed pack mints a template it can never assign |

#### `SheetNumberPolicyTests` — 9 methods

| Test | Asserts | Defect it guards |
|---|---|---|
| `Parse_defaults_to_profile_for_anything_unrecognised` (Theory ×11) | Unknown / null / blank ⇒ existing behaviour | **A numbering change must never be a side effect of a plugin update** |
| `Profile_policy_never_rewrites_a_pattern` | Default policy is inert | Silent renumbering of projects in flight |
| `Iso_policy_rewrites_a_short_pattern_when_isoNaming_exists` | Opt-in works | — |
| `Iso_policy_refuses_a_profile_with_no_isoNaming_and_says_why` | Refusal is **reported** | Applying ISO without the fields renders `--ZZ--DR--0001--` |
| `Iso_policy_leaves_an_already_iso_pattern_alone` | The 9 compliant profiles are untouched | — |
| `IsAlreadyIso_requires_all_three_marker_tokens` (Theory ×5) | `{project}` + `{originator}` + `{vol}` | A half-ISO pattern being treated as compliant |
| `Null_drawing_type_is_tolerated` | No throw | — |
| `Every_shipped_profile_can_express_its_number_under_both_policies` | No profile lacks a pattern | — |
| `Profiles_declaring_isoNaming_declare_the_fields_the_iso_pattern_needs` | volume / type / role non-blank on all 90 | Empty ISO segments on an issued drawing |

### 1.3 Proving the gates are real

**A gate only ever seen passing is not evidence.** Each was driven RED deliberately:

| Gate | How it was driven red | Result |
|---|---|---|
| The 6 data-shape gates | `git checkout -- StingTools/Data/STING_{DRAWING_TYPES,VIEW_STYLE_PACKS}.json` (revert to `main`) | **6 red, 34 green** — proves they test the fix, not the fixture |
| `Every_shipped_ruleType_is_in_the_implemented_vocabulary` | Injected `"ruleType": "AutoDimInventedByAFutureContributor"` into the first profile | **Red**, message named the rule and listed the valid vocabulary |
| `DrawingCategoryNameTests` (both sweeps) | Ran for the first time against the then-current data | **Red on first run** — this is how 4 of the findings were discovered, including my own `OST_Rooms` regression |

Re-run the first check any time you doubt a gate:

```bash
cp StingTools/Data/STING_DRAWING_TYPES.json /tmp/keep.json
git checkout -- StingTools/Data/STING_DRAWING_TYPES.json
dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter "FullyQualifiedName~Drawing"
cp /tmp/keep.json StingTools/Data/STING_DRAWING_TYPES.json
```

### 1.4 Current result

| Check | Result |
|---|---|
| `dotnet build StingTools/StingTools.csproj` | 0 errors, 0 warnings (clean `-t:Rebuild` included) |
| `StingTools.Tags.Tests` | 1,599 passing, 0 failing |
| New catalogue gates | 37 methods across 4 files |
| `StampDrawingTypeChecksums -- --check` | 93/93 correct |
| `check_path_discipline.ps1` | Tier 1: 0, Tier 2: 0 |

---

## 2. Manual — needs Revit, NOT YET RUN

Everything here creates real Revit geometry. **None of it has been executed.** The engines fail to
a named warning rather than an exception, but "fails safely" is not "produces the right drawing" —
and a count of placed elements is not evidence that they landed correctly.

### 2.A Automated — in-Revit smoke harness (built, NOT YET RUN)

Most of §2.1–2.6 is now automated by `StingTools.Revit.SmokeTests/`, which runs NUnit tests
**inside desktop Revit** through [ricaun.RevitTest](https://github.com/ricaun-io/ricaun.RevitTest)
(MIT; `dotnet test` opens Revit on the local licence, runs the tests on the API thread, closes it).
It builds its own model from the running version's `Default-Multi-Discipline_Metric.rte`
(`Fixtures/SmokeModelBuilder.cs` — no `.rvt` is checked in) and calls the engines directly
(`InternalsVisibleTo` in `StingTools/Properties/AssemblyInfo.cs`). Every test runs in a
rolled-back `TransactionGroup`, so tests are order-independent.

```powershell
# Close Revit first — the script refuses to run while any Revit process exists.
powershell -ExecutionPolicy Bypass -File tools\run_revit_smoke.ps1                  # Revit 2025
powershell -ExecutionPolicy Bypass -File tools\run_revit_smoke.ps1 -RevitVersion 2026
```

Results: `TestResults\revit-smoke\<stamp>\{smoke.trx, smoke.log, summary.txt}`; the log carries every
engine warning. **Read `HarnessIntegrityTests` first**: it fails the run if Revit resolved a
different `StingTools.dll` (the deployed add-in) from the one built beside the tests. If it fails,
move `%APPDATA%\Autodesk\Revit\Addins\<ver>\StingTools.addin` aside for the run.

The project name deliberately does **not** match the CI glob `StingTools.*.Tests/` — GitHub
runners have no Revit. It is local-only until a self-hosted licensed runner exists.

| Test | Covers | Asserts (outcomes, not counts) |
|---|---|---|
| `WallLength_…` | §2.1 #1 #3 #4 | exactly one dim per straight wall, single segment, parallel to the wall, `Value` = `CURVE_ELEM_LENGTH` ±1 mm; arc wall gets no dim and a warning naming its id; stacked wall dimensioned or named (never silent); re-run adds no element |
| `Openings_…` | §2.2 #1–3 #6 | one chain on the host wall; `NumberOfSegments` = openings + 1; segment values equal the fixture's cap→opening→cap stations ±1 mm, in order (either direction); origins monotonic; no chain on walls without openings; re-run adds nothing |
| `ColumnToGrid_…` | §2.3 #1–4 | each off-grid column has one dim, to its nearest grid, dim line **perpendicular** to that grid, value = the known 450 / 400 mm offset; on-grid column gets none; re-run adds nothing |
| `SpotSlope_EveryPlacedSpotIsASlope_…` | §2.4 #1 #3 #5, DRAW-3 | every spot left in the view has `StyleType == SpotSlope` (no elevation survives); engine tally = spots present; exactly one on the 1:80 drain, none on the level pipe; level run reported; re-run adds nothing. If Revit refuses the re-type the test is **Inconclusive** with the BLOCKED warning — the guard held, but nothing is placed |
| `SpotSlope_WithNoSlopeType_Blocks_…` | §2.4 #2, DRAW-3 | slope types deleted ⇒ zero elements added to the view, `SpotsPlaced` 0, a `BLOCKED` warning |
| `FlowArrow_…` | §2.5 #2 #3 #5 #6 | authors `STING_ANNO_FLOW_ARROW` via `BuildFlowArrowFamilyCommand.Author`; one arrow per unconnected run, within 1 ft of the run midpoint, aligned with the pipe; if the engine does *not* warn that direction is unknown, the drain's arrow must point downhill; re-run adds nothing |
| `Invert_PlanView_…` | §2.6 | 2 IL notes + one `1:80` on the drain only (cold-water pipe gets none); IL text = centreline Z + survey-point datum (fixture sets +45.250 m) − **internal** radius, at `IlReportingOptions.Default.Decimals`; no NOMINAL fallback; re-run keeps the same note ids and texts; after moving the pipe 2 m the SAME three notes (they are provenance-stamped) follow it with correct values, no orphan warning and nothing new; after deleting the pipe its notes are removed |
| `Invert_SectionAlongTheRun_…` | §2.6 | the same IL / gradient values in a section cut along the drain; re-run adds nothing |

**Predicted from reading the code, and fixed before any run:** `ColumnToGrid_…` would have
failed three ways — the witness line ran along the grid instead of across it,
`BestAlignedReference` was handed the grid direction and so chose the column plane
perpendicular to the grid, and an on-grid column (`D > 1e-6` filter) was dimensioned to the
next-nearest grid. All three are fixed (line along the grid's in-plane normal, reference chosen
for that normal, `PickSettingOutGrid` skips a column at a grid intersection and dimensions a
column on one grid line only in the other direction). The first run is what confirms it.

**Still manual** (not asserted by the harness): visual placement quality — offsets, overlap, text
legibility (§2.1 #2, §2.2 #4); the flow-arrow **glyph** and its direction on a connected duct run
with a real AHU (§2.5 #4); the `Roofs` slope rule (§2.4 #4); `minSizeMm` (§2.1 #5); an opening
without a `CenterLeftRight` reference (§2.2 #5); a fitting mid-run; anything involving a Revit
link (§2.7) and everything in §2.7–2.9.

### 2.0 Test model

One model, reused across every case below. Build it once:

- Grids on two axes, ≥3 each, unevenly spaced (even spacing hides ordering bugs).
- Straight walls **and** at least one curved wall and one stacked wall.
- ≥2 wall-hosted doors and ≥2 wall-hosted windows in one wall, unevenly spaced.
- Structural columns on grid intersections, plus one deliberately **off**-grid.
- A sloped drainage pipe run (≥1:80) with a fitting mid-run, and one level pipe run.
- A duct run with a legible flow direction (a connected AHU or terminal).
- Rooms placed and named; one room with no bounding.
- A Revit link, for §2.7.
- A Spot Slope `SpotDimensionType` loaded — see §2.4.

Deploy the build under test and **verify which DLL Revit loaded** before believing any result:

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

### 2.1 `AutoDimWallLength` — DRAW-1

**Command:** `DrawingTypes_ProducePerLevel` with a profile carrying an `AutoDimWallLength` rule
(6 do, e.g. `arch-partition-layout-A1-1to50`).

| # | Check | Pass | Why it matters |
|---|---|---|---|
| 1 | One dimension per straight wall, **spanning the wall end to end** | Length equals the wall's own length | `EndCapReferences` recovers end faces from solid geometry because Revit has no "end of wall" reference. A **wrong pair** produces a dimension with no visible error |
| 2 | The dimension sits ~800 mm off the wall centreline, on one side | Not overlapping the wall | `WallDimOffsetMm` |
| 3 | The curved and stacked walls are **skipped with a named warning** | Warning names the wall id | Fewer than two planar caps is legitimate |
| 4 | Re-run the command | **No second dimension** | `DimensionedHostIndex` idempotency |
| 5 | Set the rule's `minSizeMm` to 2000 and re-run on a fresh view | Sub-2 m walls skipped, counted under Skipped | The gate that stops a plan drowning in 100 mm dims |

**Fails how:** a zero-length dimension, or one spanning two unrelated walls ⇒ the cap pair is
wrong. Report the wall id from the warning.

### 2.2 `AutoDimOpenings` — DRAW-1

**Command:** same, on a profile with an `AutoDimOpenings` rule (6 do).

| # | Check | Pass |
|---|---|---|
| 1 | **One chain per host wall**, not one dimension per opening | A single chain with multiple segments |
| 2 | Segments read in geometric order along the wall | Left to right, no crossing witness lines |
| 3 | First and last segments run from the wall's ends | Chain spans the whole wall |
| 4 | The chain sits ~1600 mm out — **outboard of** the wall-length dim | Two parallel chains, not overlapping |
| 5 | An opening with no `CenterLeftRight` reference is **named and omitted**, chain still placed | Warning names the instance |
| 6 | Re-run | No duplicate chain |

**Fails how:** segments in collector order rather than geometric ⇒ `ProjectOnto` ordering is wrong.

### 2.3 `AutoDimColumnGrid` — DRAW-1

| # | Check | Pass |
|---|---|---|
| 1 | Each column dimensioned to its **nearest** grid | Not to an arbitrary one |
| 2 | The dimension measures **across** the grid, not along it | Perpendicular offset |
| 3 | The off-grid column still gets a dimension | Within the 60 ft search cap |
| 4 | A column exactly on a grid is **skipped** (distance < 1e-6) | Counted under Skipped, not a zero dim |

**Fails how:** "references are not parallel" (caught, warned) or a **zero-length** dimension (not
caught) ⇒ `BestAlignedReference` chose the plane parallel to the grid instead of perpendicular.
This is the single most likely failure in §2.

### 2.4 `AutoAnnotateSlope` — DRAW-3

**Precondition:** the project must contain a `SpotDimensionType` in `OST_SpotSlopes`.

| # | Check | Pass |
|---|---|---|
| 1 | Each sloped pipe carries a spot annotation reading a **slope** (1:80, 2%) | **Not an elevation** |
| 2 | With no Spot Slope type loaded, the run **warns that it will print an elevation** | Warning present and explicit |
| 3 | The level pipe run is skipped; the count of level runs is reported | "N run(s) are level (flatter than 1:2000)" |
| 4 | The `Roofs` slope rule (1 profile) annotates the **roof**, not a pipe | `RunSlopeOnElements` path |
| 5 | Re-run | No duplicate spots |

**⚠ Treat #2 as a blocker, not a warning.** A project that ignores it gets a plausible-looking
**wrong number** on a drainage drawing.

### 2.5 `AutoAnnotateFlowArrow` — DRAW-2

**Precondition:** a generic-annotation family whose name contains "flow" and "arrow", or the
rule's `tagFamily` set. **None ships.**

| # | Check | Pass |
|---|---|---|
| 1 | With no family loaded: **no arrows, and a warning naming what to load** | Placing nothing is correct here |
| 2 | With a family loaded: **one arrow per connected run**, at the longest straight's midpoint | Not one per pipe segment |
| 3 | **The arrow points downstream** | Against the connector flow direction |
| 4 | Verify #3 on a **floor plan specifically** | See below |
| 5 | Where flow is unreadable, the arrow follows the geometric axis **and says so** | Warning names the element |
| 6 | Re-run | No stacked arrows |

**Check #4 carefully.** A plan's `ViewDirection` is **−Z**, so an angle measured in world XY and
applied about it turns the arrow the *opposite* way — every arrow in every plan would point
upstream. The code now measures against the view's own `RightDirection`/`UpDirection`. This was
fixed by reading, not by running, so **a plan is the case that proves it**.

### 2.6 `AutoSpotInvert`

No profile declares it (the engine had zero call sites and is now reachable). To exercise it, add
`{"ruleType":"AutoSpotInvert","category":"Pipes"}` to a project-override drawing type.

| # | Check | Pass |
|---|---|---|
| 1 | Spot elevation at pipe **invert**, not centreline | Centreline Z − radius |
| 2 | Only drainage-classified pipes annotated | Sanitary / Storm / Vent / Foul |

**⚠ The invert value wants a drainage engineer's sign-off** before this is used on issued output —
see the A-5 note in `DrainageInvertDimensioner`.

### 2.7 Style packs, print overrides, editor

| # | Case | Check |
|---|---|---|
| 1 | Produce with a presentation profile (`lineWeightScale` 0.6–0.8) | Line work is **visibly lighter** than the same drawing from a production profile |
| 2 | Same, with `print.halftoneLinks: true` and a Revit link present | Link renders halftone; a warning states how many references were halftoned |
| 3 | Produce a structural drawing (`corp-standard-struct`, new) | Frame and rebar at full weight; arch fabric halftone; MEP hidden; 22 filters present in V/G |
| 4 | Produce an MEP plan (`corp-standard-hvac`) | **System colour-coding visible** — supply/return/exhaust distinct. Was 5 phase rules only |
| 5 | Produce a section (`corp-standard-section`) | **Duct and pipe insulation reads halftone grey.** Was silently dropped as `"Insulation"` |
| 6 | Produce an RCP | **Room tags appear.** The pack hid Rooms, and a view-scoped collector cannot see a hidden element, so no room tag was ever placed |
| 7 | Produce a plumbing schematic | Pipes **visible**. Was bound to a pack that hides Pipes |
| 8 | Produce a schedule profile | No crop warning; no managed-template warning; no `view.Scale` warning |
| 9 | Editor → open a **corporate** pack, change one colour, Save, reopen | **The change persisted.** It was discarded twice over: no pack write at all, then a project-origin-only filter |
| 10 | Editor → Save, then inspect `_BIM_COORD/view_style_packs.json` | Contains only edited packs; header preserved; **no `routing` array** (it would freeze the corporate table) |
| 11 | Editor → Save, then inspect `_BIM_COORD/drawing_types.json` | `routing` holds only project rules, not all 113 corporate ones |
| 12 | Editor → set a filter rule's visible checkbox to **indeterminate**, Save | JSON omits `visible` — "the pack does not say", deferring to the filter registry |
| 13 | Editor → VG tab | **Levels, Dimensions, Text Notes, Generic Annotations, Scope Boxes, Matchline, Section Boxes, Reference Lines are listed.** `RevitCategoryTree` lacked all eight, so the editor could not show overrides `corp-base` ships |
| 14 | Editor → save a VG override with no weight set, then produce | Override applies. A stray `projWeight: 0` used to throw and discard the **whole** category override |

### 2.8 Validator

Run `DrawingTypes_Doctor` on the test model. Expect **zero** DT-139, DT-139-FAM, DT-139-TAG,
DT-140, DT-142, DT-143-DUP, DT-143-NOPACK and DT-137-SLOTVT findings against the shipped
catalogue — all were fixed. A non-zero count means either a project override reintroduced one, or
a gate has a hole.

Then break one deliberately and confirm the code fires:

```jsonc
// <project>/_BIM_COORD/drawing_types.json
{ "drawingTypes": [ { "id": "probe", "origin": "project", "purpose": "Plan",
    "annotation": { "rules": [ { "ruleType": "NotAThing", "category": "Walls" } ] },
    "slots": [ { "label": "Main", "viewType": "Nonsense", "normX": 0, "normY": 0, "normW": 1, "normH": 1 } ] } ] }
```

Expect **DT-139** (Error) and **DT-137-SLOTVT** (Warning). If they do not appear, the validator is
not reaching project overrides and §2.8 is worthless.

### 2.9 ISO numbering — DRAW-6

`PRJ_ORG_SHEET_NUMBER_POLICY_TXT` is **not yet in `MR_PARAMETERS.txt`**, so `LookupParameter`
returns null and the default applies. That is why this is low risk — and why opting in currently
needs the parameter added by hand.

| # | Check | Pass |
|---|---|---|
| 1 | Produce with no policy parameter | Numbers **unchanged** from today |
| 2 | Add the parameter, leave it blank, produce | Numbers unchanged |
| 3 | Set `iso`, produce | Full ISO number; a warning per profile naming the pattern replaced |
| 4 | Set `iso` with a profile lacking `isoNaming` | Keeps its own pattern **and says why** — never `--ZZ--DR--0001--` |

---

## 3. Known-open, with the reason

| ID | Status | Why it is not closed |
|---|---|---|
| DRAW-1 | Needs Revit | §2.1–2.3. Reference strategies are the risk |
| DRAW-2 | Needs a family | No flow-arrow family ships. Authoring `STING_ANNO_FLOW_ARROW` is the real close |
| DRAW-3 | Needs Revit | The elevation-instead-of-slope warning has never been seen fire |
| DRAW-4 | Closed 2026-09-24 | 251 of 287 filters now in a pack; the 36 left are per-project by design. Check in Revit: the rebar / insulation range filters select by size, clash and MGS verify-fail colours win over system colours (they sit first in their packs), `fire-escape-*` accept `OST_Areas` |
| DRAW-5 | **Deliberate** | `ViewStylePack.Checksum` declared, never computed. Phase 225 reasoned packs decide appearance, not deliverable identity. Should be wired or dropped — declared-and-unused is the part that reads as a bug |
| DRAW-6 | Low risk | §2.9. Bind the parameter before offering the policy |
| DRAW-7 | Needs 2027 API | `RevitLinkGraphicsSettings` has no `Halftone` on 2025; the category override is the route. Unconfirmed on 2026/2027 |
| DRAW-8 | Open | `RainwaterOutlets` / `RoofLights` tagFamilies keys dropped — they name no Revit category and no rule consulted them. Roof-plan tagging of RWOs and roof lights needs a rule against the real host category (Plumbing Fixtures / Windows) |

---

## 4. When you change the catalogue

In order. Skipping step 2 makes every drawing type report drift and silently demote itself to
`origin: "project"`, disabling the corporate lock.

1. Edit `STING_DRAWING_TYPES.json` / `STING_VIEW_STYLE_PACKS.json` / `STING_AEC_FILTERS.json`.
2. `dotnet run --project tools/StampDrawingTypeChecksums` — **required** after any drawing-type edit.
3. `dotnet build StingTools/StingTools.csproj` — expect 0/0.
4. `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`.
5. If you added a `ruleType`: declare it in `AnnotationRuleKinds` **and** wire a handler — the
   tests fail until both exist, which is the intended friction.
6. If you added a slot `viewType`: add it to `SheetPlacementBridge.KnownSlotViewTypes` and to the
   compatibility switch, or it silently matches every view.
7. Re-read §2 for anything touching geometry.
