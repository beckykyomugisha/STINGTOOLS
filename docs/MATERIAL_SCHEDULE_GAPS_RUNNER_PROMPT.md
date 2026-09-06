# Material Schedule — remaining gaps: implementation runner

**Audience:** an autonomous coding agent working in this repository.
**Baseline:** `main` at or after `ee326ba2c` (Phase 246). Build is 0 errors / 0 warnings;
`StingTools.Boq.Tests` is 465 green. **Do not start from a stale branch** — rebase first.

---

## 0. Read this before writing any code

The material schedule reached its current state by fixing sixteen defects, most of which
were *plausible code that produced confident wrong numbers*. The invariants below are the
scar tissue. Violating one silently undoes work that took a real export to find.

### 0.1 The three sources, never mixed

| Source | Where the number comes from | Examples |
|---|---|---|
| **Layer-derived** | The host type's `CompoundStructure` — the model *states* the build-up | plaster, tiling, screed, ceiling boards, DPM |
| **Family-derived** | A loaded family's **type parameters** | doors, windows, ironmongery, sanitaryware |
| **Ratio-derived** | Nothing in the model. A published or calibrated ratio | nails, binding wire, hoop iron, hardcore |

**A ratio must never be presented as a measurement.** Ratio-derived rows carry a warning
banner naming them as practice heuristics — copy the wording pattern from
`SiteToolsCalculator` / `RoomFinishGatherer`, which already do this.

**A family-derived split must never be inferred from a type NAME.** Three rules that did
exactly that (`paint-wall`, `floor-tile`, `paint-ceiling`) were withdrawn in PR #710 for
pricing entire walls as paint. If the family does not declare the data, the honest output
is one priced unit, not a guess.

### 0.2 Invariants that must survive

1. **Wastage lives in exactly ONE place** — the supplier-unit rule
   (`StingTools/Data/STING_SUPPLIER_UNITS.json`, `defaultWastagePct`). Engine quantities are NET. Applying
   it in both places double-counted blocks at ~10% instead of 5% (#728).
2. **Unlike units never convert.** `CommodityAggregator.UnitsAlign` refuses when a rule's
   `sourceUnit` disagrees with the measured unit. A m² quantity once printed as
   `Bricks · No. · 364.31` (#728).
3. **Order quantity is rounded UP** — `Math.Ceiling(v*100 - 1e-9)/100` for divisible units,
   `Math.Ceiling` for countable. Never round-to-nearest: it under-orders (#777).
4. **Intermediate measures are memoranda.** If a row's constituents are separately listed,
   it gets `IsMemorandum = true`: quantity kept, `AmountUGX` hard-zero, no rate cell, no
   formula, skipped by R3. See `IntermediateMeasureMarker` and `intermediateMeasures` in
   `StingTools/Data/STING_MATERIAL_STAGES.json`. **Any new intermediate you introduce must be declared
   there**, with its children — and the marking is conditional on the children actually
   being present, because turning a double-count into an omission is worse.
5. **A surface is finished ONCE.** A tiled wall face is plastered as backing but not
   painted — `MasonryWallInput.TiledFaces` deducts it. Any new finish must state which
   other finish it displaces, or prove it displaces none.
6. **Two sources must never measure the same surface.** The room-finish source is
   suppressed wholesale when the layer source produced tiling, and the export SAYS so.
   Follow that pattern exactly if you add a third path to any surface.
7. **Silence is a bug.** Every new source reports its own denominator, like
   `TileScanTally` and `RoomFinishTally`: how many candidates were inspected, how many
   matched, and the names of what it rejected. An absent side effect never tells you why.
8. **Notes reach the workbook**, not just the dialog — `MaterialScheduleDocument.Warnings`
   → the Validation sheet. A diagnostic nobody can recover is barely a diagnostic (#788).

### 0.3 Verification discipline — non-negotiable

- **Every gate must be verified FAILING before you keep it.** Point each new test at a
  deliberately wrong input, watch it fail, then restore. Three defects in this codebase
  survived because a test had only ever been seen passing — including one that compared a
  value against its own rounding.
- **A `case` label is not a wiring.** A command needs a handler case AND a button. Check
  both. See `tools/check_command_doc_acquisition.ps1` and PRs #792/#794/#796.
- **Data-file keys fail silently.** The builder composes `"TILE " + key` and a spelling
  that misses returns **zero, not an error**, dropping rows without a word. Every new
  `MATERIAL_LOOKUP` key needs a test that resolves it and asserts the value is > 0.
- Run after every task: `dotnet build StingTools/StingTools.csproj -c Release` (expect 0/0)
  and `dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj` (expect all green).

---

## 1. Task order

T1 → T2 → T3 are the same mechanism (read a `CompoundStructure` layer, emit constituents)
and share one refactor. Do them in order; do **not** parallelise them.

T4 and T5 are ratio-derived and independent of T1–T3.

T6 is a data contract and should be scoped before it is coded.

> **Before starting: confirm the layer path works.** Draw a floor with
> `STING RC Slab 150 - Ceramic Tiled` and export. If `Floor tiling` / `Tile adhesive` /
> `Tile grout` do not appear under FINISHES, **stop and fix that first** — T1, T2 and T3
> all ride on the same code path, and building three features on a broken reader wastes
> all three.

---

## T1 — Screed from the Substrate layer  *(highest value, lowest risk)*

**The gap.** `STING RC Slab 150 - Ceramic Tiled` carries a 40 mm `Cement Screed`
**Substrate** layer that contributes **nothing**. `CompoundTakeoffBuilder.TilingConstituents`
inspects `Finish1`/`Finish2` only, and only for tile materials. Every screeded floor in
every project is missing its cement and sand.

**Where.**
- Engine: `StingTools/BOQ/Takeoff/CompoundTakeoff.cs` — add `ScreedInput` +
  `Screed(ScreedInput)`, modelled directly on the plaster block (search
  `"4. Plaster m"`), which derives cement and sand from a volume.
- Adapter: `StingTools/BOQ/Takeoff/CompoundTakeoffBuilder.cs` — generalise the layer walk.
  `ReadTiledFinish` is currently tile-specific; extract a
  `ReadLayers(doc, el, predicate)` that returns matched layers with their **thickness**
  (`CompoundStructureLayer.Width`, feet → m) and material name, then express the tile read
  and the screed read in terms of it.
- Classifier: `StingTools/Core/MaterialSchedule/FinishTextClassifier.cs` — add
  `IsScreed(name)`. Narrow: `screed`, `cement screed`, `sand-cement`. **Must NOT match
  `plaster`, `render` or `mortar`**, which are already measured elsewhere and would
  double-count.

**Emit.** `screed` (m², memorandum), `screed_cement` (bag), `screed_sand` (m³). Quantities
NET of wastage. Mix ratios from `StingTools/Data/MATERIAL_LOOKUP.csv` under a new `SCREED` category
(`CEMENT_BAGS_PER_M3`, `SAND_RATIO`), with a `SCREED,DEFAULT` row — follow the `PLASTER`
rows exactly.

**Data.**
- `StingTools/Data/STING_SUPPLIER_UNITS.json` — `screed_cement` → existing `cement` commodity kinds,
  `screed_sand` → existing `sand`. Reuse the commodities; do **not** mint new ones.
- `StingTools/Data/STING_MATERIAL_STAGES.json` — add all three kinds to the `finishes` stage, and add
  `screed` to `intermediateMeasures` with children `[screed_cement, screed_sand]`.

**Done when.** A floor whose type has a screed layer produces cement and sand under
FINISHES; the `screed` area row is a memorandum; a floor with no screed layer produces
nothing; and the scan line reports how many types carried a screed layer.

---

## T2 — Ceiling decomposition

**The gap.** `CeilingType` appears **nowhere** in `CompoundTakeoffBuilder.TryBuild`, so
`STING Suspended Gypsum Ceiling` yields zero materials — no boards, no furring, no skim.

**Where.** Add a `Ceilings` branch to `TryBuild` using the `ReadLayers` helper from T1.

**Emit.** `ceiling_board` (m²) from a board-material `Finish1`/`Finish2` layer;
`ceiling_furring` (m) derived from area × a spacing factor. **Furring is ratio-derived, not
measured** — the model does not state grid spacing — so it must carry the heuristic banner
and live in `MATERIAL_LOOKUP` under `CEILING` (`FURRING_M_PER_M2`, default ~2.7 for a
600×600 grid). A plastered/skim ceiling emits `plaster`-family kinds, not board.

**Data.** New commodities `ceiling-board` (Sheets, 2.88 m²/sheet for 1200×2400) and
`ceiling-furring` (Lengths, 3.6 m). Route both to `finishes`. Add rates.

**Done when.** A ceiling with a board layer produces a sheet count; a ceiling with no
compound structure produces nothing and says so; furring is visibly flagged as derived.

---

## T3 — Membrane layers (DPM, underlay, insulation)

**The gap.** `MaterialFunctionAssignment.Membrane` layers are ignored entirely. A
ground-bearing slab's DPM and a roof's underlay are both real purchased materials whose
area the model already states.

**Emit.** `dpm` (m²) and `roof_underlay` (m²), converted to Rolls. Use `ReadLayers` with a
membrane predicate; classify by material name (`FinishTextClassifier.IsMembrane`).

**Caution.** Do **not** emit insulation as a membrane — it is a separate commodity bought by
thickness, and conflating them silently mis-prices both. If an insulation layer is present,
either handle it explicitly or report it in the scan as unhandled; **never absorb it**.

---

## T4 — Ratio-derived consumables

**Emit** from quantities the schedule already measures. All ratio-derived, all banner-flagged.

| Commodity | Derived from | Typical basis |
|---|---|---|
| Hoop iron | walled area (m²) | rolls per m² of masonry, courses-dependent |
| Binding wire | rebar (kg) | ~1.0–1.5% of rebar tonnage |
| Nails (formwork) | formwork (m²) | kg per m² of contact area |
| Roofing nails/screws | roof covering (m²) | kg or nr per m² |

**Where.** A new `StingTools/Core/MaterialSchedule/ConsumablesCalculator.cs` (NEW), Revit-free and unit
tested, taking measured totals and returning `ConstituentInput` rows. Model it on
`SiteToolsCalculator` — including its honesty: the JSON, the class header and the export
banner must all say these are practice heuristics, not a standard.

**Data.** `StingTools/Data/STING_CONSUMABLES.json` (corporate, NEW) + `<project>/_BIM_COORD/consumables.json` (project
override). Ship the ratios with a source note per row.

**Hard requirement.** With no measured driver, emit **nothing**. Never a default quantity.

---

## T5 — Roof accessories

Ridge caps (m), barge/fascia boards (m), underlay (T3), roofing screws (T4). Ridge and
fascia need roof **perimeter/edge length**, which the roof element can give via its
boundary; if it cannot be obtained reliably, **report it in the scan and emit nothing**
rather than deriving a length from area.

Timber rafters and purlins are out of scope unless modelled as Structural Framing — if they
are, they already decompose. State that in the export notes so their absence is not read as
an omission.

---

## T6 — Family-derived materials (doors, windows, ironmongery)  *(scope before coding)*

**Do not implement this from type names.** A door is bought as an assembly. Splitting one
into leaf + frame + hinges + lock requires the **family to declare it**.

The machinery already exists: `StingTools/Tags/FamilyConformanceCheckCommand.cs` audits
vendor `.rfa` files against the STING contract on a 100-point scale. The correct route is:

1. Extend the family contract with material fields (e.g. `MAT_LEAF_TXT`,
   `MAT_FRAME_TXT`, `MAT_IRONMONGERY_SET_TXT`, `MAT_GLAZING_M2`).
2. Have the conformance checker report families that lack them.
3. Emit constituents **only** from families that declare them; everything else stays one
   priced unit — which is the accurate answer, not a limitation.

**Deliverable for this task is a written proposal first**, listing the parameters and their
units, for a human decision. Do not modify shipped families.

---

## 2. Cross-cutting requirements for every task

1. **Stage routing** — add each new kind to `StingTools/Data/STING_MATERIAL_STAGES.json`. Kind beats
   category, so a finish emitted from a wall still routes to `finishes`.
2. **Supplier rule + baseline rate** — every new kind needs a rule whose `sourceUnit`
   matches the emitted unit, and a row in `StingTools/Data/STING_COMMODITY_RATES.csv`. Existing tests
   `Every_Rule_Is_Reachable` and `Every_Commodity_Rule_Has_A_Baseline_Rate` will fail
   otherwise. **That is the gate working — fix the data, not the test.**
3. **Scan diagnostic** — one tally per new source, Revit-free and unit-tested, appended to
   `MaterialScheduleBuilder`'s warnings. Report the denominator and name what was rejected.
4. **Tests** — in `StingTools.Boq.Tests`, adding new source files to the `<Compile Include>`
   list and any new data file to the `<None Include ... CopyToOutputDirectory>` list.
   Cover: the happy path; the empty case (produces nothing, not zero-quantity rows); the
   double-count guard; and a shipped-data test resolving every new key.
5. **Rounding, wastage, memoranda, unit alignment** — §0.2. No exceptions.

---

## 3. Definition of done

- [ ] `dotnet build StingTools/StingTools.csproj -c Release` → **0 errors, 0 warnings**
- [ ] `dotnet test StingTools.Boq.Tests` → all green, count increased
- [ ] `./tools/check_command_doc_acquisition.ps1` → PASS
- [ ] `./tools/check_path_discipline.ps1` and `./tools/check_workflow_wiring.ps1` → PASS
- [ ] Every new gate demonstrated **failing** on a wrong input, then restored
- [ ] Every new command (if any) has a handler case **and** a button
- [ ] `docs/CHANGELOG.md` — a new phase entry stating what was *verified* vs merely built
- [ ] `docs/ROADMAP.md` — MATSCHED rows updated; anything unverified stays open
- [ ] One PR per task, each independently reviewable

## 4. Do not

- Do not widen `FinishTextClassifier.IsTile`. It is narrow on purpose; a false positive
  prices a whole floor as tiling.
- Do not infer a material split from a type or family name.
- Do not emit a default quantity when the driver is missing. Emit nothing and say why.
- Do not fold ratio-derived rows in with measured ones without the banner.
- Do not "fix" a failing shipped-data test by loosening the assertion.
- Do not claim anything is verified that has not been run. Write what was tested and what
  was not; the ROADMAP distinguishes them deliberately.
