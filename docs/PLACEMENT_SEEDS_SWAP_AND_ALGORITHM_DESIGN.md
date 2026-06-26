# Placement: Seed-Family-Per-Rule, Non-Destructive Swap & Algorithm Roadmap

> Design + research deliverable. Grounded in the existing tag-seed, symbol-library, and
> swap code. The model-modifying parts (auto-build seed, swap parameter-bridge) are
> **designed and code-sketched but not yet wired** — they create/modify families and must
> be verified in Revit before merge. The data artifact (`STING_CATEGORY_TO_SEED_MAP.json`)
> and the rule/scorer fixes already landed.

---

## 1. The core idea (what the user asked for)

> "Like we did for tag family seeds, we should already have a seed-family folder with each
> rule aligning to a family; at placement, use those default families; then research the
> most flexible way of swapping families without destruction while maintaining parameter
> alignment — even when swapping with an old family that has no STING parameters."

Three things, in order:

1. **Every placement rule resolves to a guaranteed default seed family** (mirroring how tags
   resolve a seed per category) so a run *always* has something to place — no more silent
   `SkippedNoSymbol` because "no family is loaded."
2. **Placement uses those seeds by default**, stamped with STING shared parameters by GUID
   and `STING_SEED_FAMILY_TXT = <seedId>` so they are swap-ready.
3. **Swap seed → real/manufacturer family non-destructively**, preserving position, host,
   and *parameter values*, even when the target family was authored without STING params.

The good news from the audit: **the seed specs already exist** — 33 `Data/Seeds/STING_SEED_*.json`
cover virtually every placement category (Lighting Fixtures, Lighting Devices, Electrical
Fixtures/Equipment, Air Terminals, Sprinklers, Fire Alarm, Comms/Data/Security, Plumbing,
Mechanical, Junction Boxes, …). They were just **never wired into the placement resolution
path**. This design closes that loop.

---

## 2. How tag seeds work today (the model to mirror)

(From `Tags/TagFamilyCreatorCommand.cs` + `Tags/FamilyParamCreatorCommand.cs`.)

```
BuiltInCategory  --CategoryTemplateMap-->  Revit .rft template
   --NewFamilyDocument-->  family doc
   --FamilyParamEngine.InjectSharedParams (MR_PARAMETERS.txt, bound by GUID)-->
   --InjectTagPosFormulas + InjectPositionTypes (16 variants)-->
   --save .rfa--> TagFamilies/  --LoadFamily--> project
```

Key property that makes tags robust: **parameters are bound by GUID, not name** — so once a
family carries `ASS_TAG_1` (GUID `g`), the value survives type swaps and is readable by every
consumer keyed on `g`.

The placement seed system already has the equivalent machinery:
- `Core/Symbols/SymbolLibraryCreator.cs` builds a `.rfa` from a `Data/Seeds/*.json` spec
  (geometry + connectors + parameters + type variants), stamps every type with
  `STING_SEED_FAMILY_TXT = def.Id`, and can load it into the project.
- `Commands/Symbols/BuildSeedFamiliesCommand.cs` builds all seeds into
  `<project>/_BIM_COORD/Families/Seeds/`, with `MissingOnly | RebuildUnfinalized | RebuildAll`
  modes and a `.sting-finalized` protection sidecar.
- `Commands/Symbols/SwapToManufacturerCommand.cs` swaps via `Element.ChangeTypeId`.

What's missing is the **explicit category → seed binding** and the **placement → seed
resolution tier**.

---

## 3. Seed-family-per-rule — architecture

### 3.1 The binding (DONE — data)
`StingTools/Data/Placement/STING_CATEGORY_TO_SEED_MAP.json` — a flat
`CategoryFilter → seedId` map (29 categories; `Conduits/Pipes/Stairs` intentionally
seedless). This is the single source of truth that ties a rule's category to its default
seed family. Project override goes at `<project>/_BIM_COORD/category_to_seed_map.json`.

### 3.2 The resolution tier (TO WIRE — `FixturePlacementEngine.ResolveSymbol`)
Insert a **seed tier** between "loaded-family match" and "skip". New order:

1. Already-loaded family of the category (current behaviour) — honour `VariantHint` /
   `FamilyTypeRegex`.
2. `TryAutoLoadFromLibrary` from `Families/<subdir>/` (current).
3. **NEW — seed tier:** look up the rule's category in `STING_CATEGORY_TO_SEED_MAP`. If a
   seed is mapped and not yet loaded, **build-or-load the seed** (see 3.3) and use its
   symbol. Stamp the seed instance with STING params (already done at build) + provenance.
4. Skip with a *visible* `SkippedNoSymbol` warning naming the missing seed (so the user can
   build it) — current behaviour, now the last resort.

Resolution stays per-(category, variant) cached, so the build happens at most once per run.

### 3.3 Build-or-load seed (TO WIRE)
```
EnsureSeedSymbol(doc, category):
    seedId = CategoryToSeedMap.Resolve(category)        // null -> caller skips
    if loaded family stamped STING_SEED_FAMILY_TXT==seedId exists -> return its active symbol
    path = <project>/_BIM_COORD/Families/Seeds/<seedId>.rfa
    if !exists(path):  SymbolLibraryCreator.BuildOne(Data/Seeds/<seedId>.json) -> path  // model-modifying
    LoadFamily(path) ; activate first symbol ; return symbol
```
Risk: builds/loads families **inside the placement transaction**. Revit allows
`LoadFamily`/family creation inside a transaction, but it is slow and must be verified.
Safer variant: a **pre-pass** (`Placement_EnsureSeeds` button / first step of the run
workflow) that builds every seed for the ticked categories *before* the placement
transaction opens — keeps the hot path fast and avoids nested-transaction surprises.

### 3.4 Why this makes runs robust
- "Tick Lighting Fixtures, run, with no luminaire family loaded" → engine builds/loads
  `STING_SEED_LightingFixture`, places it (stamped, ISO-tagged), and the result is a real
  scheduled instance — not a silent skip.
- Every placed seed is swap-ready (carries `STING_SEED_FAMILY_TXT`), so the designer later
  swaps the whole project to manufacturer families in one pass (§4).

---

## 4. Non-destructive family swap with parameter alignment (the research)

### 4.1 What `ChangeTypeId` already preserves
`Element.ChangeTypeId(newTypeId)` (used by `SwapToManufacturerCommand`) is the right
primitive — **in-place, fast, non-destructive**:
- **Preserved automatically:** XYZ location, rotation, **host** (wall/ceiling/floor),
  element id, connector topology (best-effort).
- **Preserved iff the destination family carries the same parameter by GUID:** all STING
  shared params (`ASS_TAG_*`, `ELC_*`, `LTG_*`, …). Revit copies the instance value across a
  type change **by GUID**, not by name.
- **Audit:** the command stamps `STING_DESIGN_REF_TXT` (original seed id) and appends
  `STING_SWAP_HISTORY_TXT` (`ts|operator|src|dst`).

### 4.2 The failure the user named
> "swapping with an old family that has **no** STING parameters."

If the destination family was authored without STING shared params, then after
`ChangeTypeId` the instance has **no parameter with the STING GUIDs**, so:
- The values aren't carried (nothing to carry them into) → tags/schedules read empty.
- Native equivalents (e.g. a manufacturer's own "Mounting Height") are unrelated to
  `MNT_HGT_MM` and don't line up.

### 4.3 The fix — a swap-time **Parameter Bridge** (stamp → snapshot → swap → restore)
The flexible, non-destructive answer is to make the destination family STING-aware *as part
of the swap*, and carry values through a name↔GUID alias table. Per swap group
`(seedId, sourceType) → destFamily`:

```
1. ENSURE-STAMP (once per destFamily, not per instance):
   if destFamily lacks the STING shared params (by GUID):
       FamilyParamEngine.InjectSharedParams(destFamily, requiredGuids)   // geometry untouched
       (optionally InjectTagPos formulas/types if it's tag-like)
       reload destFamily into project
   -> destFamily now carries ASS_TAG_*, discipline params by GUID. Non-destructive:
      only ADDS parameters; never alters geometry, existing types, or native params.

2. SNAPSHOT (per instance, before ChangeTypeId):
   values = { guid -> instance.get_Parameter(guid).Value  for guid in STING set if present }
   // also capture native-named values for ALIAS carry-over (see 4.4)
   nativeValues = { nativeName -> value  for nativeName in AliasTable.keys if present }

3. SWAP:
   instance.ChangeTypeId(destTypeId)      // location/host/rotation preserved

4. RESTORE (per instance, after ChangeTypeId):
   for guid,value in values:    instance.get_Parameter(guid)?.Set(value)   // now exists on dest
   for nativeName,value in nativeValues:
       guid = AliasTable[nativeName]
       if instance.get_Parameter(guid) is empty: instance.get_Parameter(guid).Set(value)
```

Result: an *old, STING-naive* family becomes a first-class STING instance after the swap,
with positions and values intact — fully non-destructive (additive only).

### 4.4 Alias table — carrying legacy values into STING GUIDs
A small data file `STING_PARAM_ALIAS_MAP.json` maps common native/legacy parameter names to
the STING shared-parameter GUID they should populate:
```
"Mounting Height"      -> MNT_HGT_MM
"Elevation from Level" -> MNT_HGT_MM    (fallback)
"Wattage" / "Load"     -> ELC_LOAD_VA
"Switch Voltage"       -> ELC_VOLT_V
"IP Rating"            -> ASS_IP_RATING_TXT
```
On swap, if the old family carried a value under a legacy name, it lands in the right STING
GUID. Unknown native params are left untouched (non-destructive). This is the "maintain
parameter alignment even with an old family" guarantee.

### 4.5 Flexibility + speed
- **Batch by `(seedId, sourceType)`** (already done) so `EnsureStamp` + `EditFamily/reload`
  runs **once per destination family**, then `ChangeTypeId` runs per instance (cheap).
- **No delete+recreate** anywhere — `ChangeTypeId` keeps element ids stable (preserves
  hosting, view-specific overrides, dimensions referencing the element).
- **Connector re-stitch** (existing, 600 mm proximity) handles connector count/position
  differences after swap.
- **Reversible:** `STING_DESIGN_REF_TXT` retains the original seed id, so a "swap back to
  seed" is symmetric.

### 4.6 Swap surfaces to add
- `Placement → Swap to Manufacturer` already exists. Add a **bridge** flag (default on) that
  runs §4.3 so swapping to non-STING families is safe.
- Expose **"Stamp this family with STING params"** as a standalone command (it already
  exists for tags via `FamilyParamCreator`; generalize to any placeable category) so users
  can pre-bless a manufacturer library once.

---

## 5. Algorithm gaps & roadmap (from the deep review)

Ordered by value. Items marked ✅ are already fixed on this branch.

| # | Area | Gap | Severity | Status / fix |
|---|---|---|---|---|
| A1 | Candidate gen | `CEILING_CENTRE` emitted **1 point** → fire alarms / sprinklers / emergency lights / area-lights placed one device per room | CRITICAL | ✅ Fixed — `EmitCeilingGrid` (density/spacing aware, backward-compatible) |
| A2 | Symbol res | Light-switch rules picked a "Circuit Breaker" type (no `FamilyTypeRegex`) | HIGH | ✅ Fixed — gang-aware `FamilyTypeRegex` on mk switch rules |
| A3 | Resolution | No category→seed wiring → "no family loaded" silently skips | HIGH | ▶ §3 (map landed; engine tier to wire) |
| A4 | Swap | Values lost swapping to families without STING params | HIGH | ▶ §4 parameter-bridge (designed) |
| A5 | Candidate gen | **Linear** rules (`PerLinearMetre`) don't densify along the perimeter — `WALL_MIDPOINT` emits 1 pt/segment → ~40% of intended count | HIGH | New `LINEAR_WALL` densifier OR post-emit re-spacing keyed on `ComputeCap` |
| A6 | Flexibility | No "≥X m from door/window" clearance for sockets | HIGH | Add `DoorClearanceMm`/`WindowClearanceMm` + collision penalty |
| A7 | Flexibility | No "one per wall segment > X m" | HIGH | Add `MinSegmentLengthMm`; filter in `EmitWallMidpoints` |
| A8 | Selection | `SelectWithSpacing` is greedy nearest-first → clusters / bare corners in L-shaped/tight rooms | MEDIUM | Poisson-disk seed or Lloyd-relaxation pass for density/coverage rules; expose `SelectionMethod` |
| A9 | Scoring | Score weights are global constants, not sensitive to `RuleKind` (density rules under-weight spacing/coverage) | MEDIUM | Per-`RuleKind` weight profiles; allow per-rule override |
| A10 | Candidate gen | Single-point structural anchors (`BEAM_SOFFIT`, `COLUMN_FACE_NEAREST`) can't satisfy density rules | MEDIUM | Emit a local grid / N-nearest for density |
| A11 | Diagnostics | Anchor generators silently fall back to `ROOM_CENTRE` (no door/window/ceiling) with no warning | MEDIUM | `StingLog.Warn` + per-rule "anchor-miss" count surfaced in the run report |
| A12 | Perf | Obstruction + wall-inside checks are O(candidates × obstacles); CSG per candidate when `RejectInsideWall` on | MEDIUM | 2D grid/quadtree of exclusion rects when >10–20; pre-computed boundary derivatives |
| A13 | Flexibility | No `LONGEST_WALL_MIDPOINT`, no structural-grid-aligned anchor, no adjacent-room stagger | MEDIUM | New anchors (each ~20–40 lines) |
| A14 | Density math | `room.Area` ft²→m² uses `0.3048*0.3048` (numerically right, semantically confusing); under-fill is silent | LOW | Named constant + "cap N but only M candidates" warning |

### 5.1 Recommended next implementation slice (when a stable run is available)
1. **Seed tier + EnsureSeeds pre-pass** (§3) — biggest UX win: runs never silently skip.
2. **Swap parameter-bridge** (§4) — makes manufacturer adoption non-destructive.
3. **A5 (linear densify) + A6 (door/window clearance)** — the two highest-value placement-
   quality fixes for real MEP layouts.
4. **A11 (anchor-miss diagnostics)** — cheap, makes every other gap self-reporting in the run
   report so users (and we) can see *why* a rule under-placed.

All of #1–#2 are model-modifying and need in-Revit verification; #3–#4 are candidate-
generation/scoring and can be unit-reasoned but should still be eyeballed on a real plan.
