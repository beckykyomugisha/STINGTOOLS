# Implementation Prompt — Seed Tier, Swap Bridge, Placement-Quality Fixes & Diagnostics

> **For the implementing agent.** Self-contained brief to implement four placement
> improvements. The full rationale + design is in
> [`PLACEMENT_SEEDS_SWAP_AND_ALGORITHM_DESIGN.md`](PLACEMENT_SEEDS_SWAP_AND_ALGORITHM_DESIGN.md) —
> read it first. Work on the **current branch** (`claude/placement-centre-review-audit`); do
> not branch or merge. The repo builds only on Windows with the Revit API — verify call
> signatures against the documented Revit 2025/2026/2027 API. `build.bat` now only
> compiles + stages (no Revit install); use it to compile-check. Note the
> "built without in-Revit verification" caveat in commits + a CHANGELOG entry.
> **Items 1 and 2 create/modify families — they MUST be verified in Revit before merge.**

---

## Where things live

| Concern | File |
|---|---|
| Engine run loop + `ResolveSymbol` + `TryAutoLoadFromLibrary` + `ComputeCap` + `ComputeRoomPerimeterMetres` | `StingTools/Core/Placement/FixturePlacementEngine.cs` |
| Anchor emitters (`EmitWallMidpoints`, `EmitDoorAnchor`, `GetBoundary`, `Fallback`) + scoring | `StingTools/Core/Placement/PlacementScorer.cs`, `PlacementScorer.AnchorTypes.cs` |
| Rule POCO (add new fields here) | `StingTools/Core/Placement/PlacementRule.cs` |
| Seed build from JSON | `StingTools/Core/Symbols/SymbolLibraryCreator.cs` (`CreateAllFromFile`, `BuildOne`) |
| Seed build command (output folder, modes) | `StingTools/Commands/Symbols/BuildSeedFamiliesCommand.cs` |
| Swap command (batch, `ChangeTypeId`, audit) | `StingTools/Commands/Symbols/SwapToManufacturerCommand.cs` |
| Stamp shared params into a family by GUID | `StingTools/Tags/FamilyParamCreatorCommand.cs` → `FamilyParamEngine.InjectSharedParams` |
| Category → seed map (already landed) | `StingTools/Data/Placement/STING_CATEGORY_TO_SEED_MAP.json` |
| Run report surface (per-rule counts/warnings) | `StingTools/UI/PlacementCenter/StingPlacementCenter.xaml.cs` (`OnRunCompleted`, the inline Report panel) |

Re-`grep`/re-read before editing — line numbers drift.

---

## Item 1 — Seed tier + `EnsureSeeds` pre-pass (model-modifying)

**Goal:** a placement run must *never* silently skip a ticked category for "no family
loaded." When no manufacturer family is loaded, build/load the mapped **seed** family and
place it (stamped, swap-ready).

1. **`CategoryToSeedRegistry`** (new, `Core/Placement/`): loads
   `STING_CATEGORY_TO_SEED_MAP.json` (corporate) + `<project>/_BIM_COORD/category_to_seed_map.json`
   (project override, merged by `category`). `Resolve(categoryName) → seedId|null`. Cache per
   document like the other registries.
2. **`EnsureSeeds` pre-pass** — the safe path (do this, not in-transaction building):
   - New command `Placement_EnsureSeeds` (`Commands/Placement/`) + a step at the **start** of
     the Centre run (before the engine transaction opens, in `OnRunPlacement_Click` /
     `ExecuteRun`). For each ticked category with no loaded family of that category:
     resolve the seed id, and if the seed `.rfa` isn't already at
     `<project>/_BIM_COORD/Families/Seeds/<seedId>.rfa`, build it via
     `SymbolLibraryCreator` (reuse `BuildSeedFamiliesCommand`'s build path in `MissingOnly`
     mode for just those seeds), then `LoadFamily` it. Idempotent.
   - Report what was built/loaded in the run report.
3. **Seed tier in `ResolveSymbol`** (`FixturePlacementEngine.cs`, ~the `ResolveSymbol`
   method ending with the "using first available symbol" warning ~line 1516): after the
   loaded-family + `TryAutoLoadFromLibrary` tiers and before returning null, consult
   `CategoryToSeedRegistry`. If a seed is mapped and now loaded (from the pre-pass), return
   its active symbol. Honour `FamilyTypeRegex`/`VariantHint` against the seed's type
   variants. Keep the per-(category,variant) `perCategorySymbol` cache.
4. **Guarantee the seed instance is STING-stamped + swap-ready.** Seeds built by
   `SymbolLibraryCreator` already inject STING params + stamp `STING_SEED_FAMILY_TXT`. Verify
   the placed instance carries `STING_SEED_FAMILY_TXT = <seedId>` so §Item-2 swap can find it.
5. **Caveats to honour:** building/loading families inside the engine's transaction is slow
   and risky — keep it in the **pre-pass** (own transaction) so the hot placement loop only
   resolves already-loaded symbols. If a seed build fails, warn and fall through to the
   existing visible `SkippedNoSymbol` path (never abort the run).

**Acceptance:** with no luminaire/switch/etc. family loaded, ticking those categories and
running places the seed families (real scheduled instances), and the run report lists
"built/loaded seed X for category Y." No silent skips for mapped categories.

---

## Item 2 — Swap parameter-bridge (model-modifying)

**Goal:** swapping a seed (or any) instance to a real/manufacturer family must preserve
**parameter values even when the destination family has no STING shared parameters** — fully
non-destructive. Implement the bridge from design §4.3 in `SwapToManufacturerCommand` (and/or
a shared helper so a future Placement-Centre "Swap" button reuses it).

Per swap group `(seedId, sourceType) → destFamily` (the command already batches like this):

1. **Ensure-stamp (once per destFamily):** if `destFamily` lacks the required STING shared
   params (check by GUID via `FamilyManager`/`get_Parameter`), open it (`Document.EditFamily`),
   inject them by GUID via `FamilyParamEngine.InjectSharedParams` (geometry/types untouched —
   additive only), reload into the project. Skip if already stamped.
2. **Snapshot (per instance, before `ChangeTypeId`):** capture `{ guid → value }` for every
   STING shared param present on the instance, **and** `{ nativeName → value }` for any name
   in the alias map (Item 2c).
3. **Swap:** `instance.ChangeTypeId(destTypeId)` (already in the command; preserves
   location/host/rotation/element-id).
4. **Restore (per instance, after `ChangeTypeId`):** write each snapshotted `guid → value`
   back (the param now exists on the dest). For each `nativeName → value`, write it to the
   mapped STING GUID **only if that GUID is currently empty** (don't clobber a real value).
5. **Alias map (Item 2c) — new data file** `StingTools/Data/STING_PARAM_ALIAS_MAP.json`:
   `{ "Mounting Height": "MNT_HGT_MM", "Elevation from Level": "MNT_HGT_MM", "Wattage":
   "ELC_LOAD_VA", "IP Rating": "ASS_IP_RATING_TXT", ... }` — native/legacy parameter name →
   STING shared-param name (resolve to GUID via `ParamRegistry`). Project override at
   `_BIM_COORD/param_alias_map.json`. Keep it small + documented; only well-known aliases.
6. **Keep existing behaviour:** `STING_DESIGN_REF_TXT` + `STING_SWAP_HISTORY_TXT` stamping,
   connector re-stitch, UL-system match. Add a `bridge` toggle (default **on**); when off,
   legacy swap behaviour.

**Acceptance:** swap a seed instance to a manufacturer family that has **no** STING params →
after swap the instance carries the STING params (added by stamp) with the original values
(restored), positions/hosts intact, and a legacy "Mounting Height" value shows up under
`MNT_HGT_MM`. Re-running the swap is idempotent (stamp skipped).

---

## Item 3 — Placement-quality: linear densify + door/window clearance (candidate gen + scoring)

### 3a — Linear-rule perimeter densify (gap A5)
**Problem:** a `RuleKind = Linear` rule with `PerLinearMetre` on a `WALL_MIDPOINT` anchor
emits ~1 point per wall segment, so it places ~40% of the intended count. **Fix:** in
`PlacementScorer.GenerateAnchorPoints` (or a post-emit pass), when `rule.RuleKind == Linear`
and `rule.PerLinearMetre > 0`, **densify along the perimeter** to the target count: walk the
room boundary segments (already cached in `GetBoundary`), step along each segment at
`PerLinearMetre` spacing (inset by `WallClearanceMm`), and emit candidates. Respect
`MinSpacingMm`. Prefer this over relying on `WALL_MIDPOINT`'s one-per-segment output.
Consider a dedicated `LINEAR_WALL` anchor token so authors can opt in explicitly, while also
auto-densifying existing `WALL_MIDPOINT`+Linear rules.

**Acceptance:** "outlet every 2 m" in a 20 m-perimeter room places ~10, evenly along the
walls, not 4 at segment midpoints.

### 3b — Door/window clearance (gap A6)
**Problem:** no way to keep sockets/devices clear of door/window openings. **Fix:** add
`DoorClearanceMm` + `WindowClearanceMm` to `PlacementRule.cs` (default 0 = off). In the
candidate scoring (`PlacementScorer` collision/`BuildCandidate` path), compute the distance
from each candidate to the nearest door/window (the door/window sets are already collected in
`GetBoundary`); when within the clearance, **reject or heavily penalise** the candidate
(mirror the obstruction-buffer logic). Add the two fields to the rule-editor UI cards in the
Placement Centre so they're authorable.

**Acceptance:** a socket rule with `DoorClearanceMm: 300` never places a socket within 300 mm
of a door opening.

These are candidate-generation/scoring changes — unit-reasonable, but still eyeball on a real
plan. Keep both **default-off / no-op** so existing rules are unchanged unless they opt in.

---

## Item 4 — Anchor-miss diagnostics (cheap, high signal)

**Problem (gap A11):** anchor generators silently fall back to `ROOM_CENTRE` when the room
has no doors/windows/ceiling/grid, so devices land at the centroid with no warning — the user
can't tell *why* a rule mis-placed.

**Fix:**
1. In every `Fallback(...)` / default-case ROOM_CENTRE path in `PlacementScorer.cs` +
   `PlacementScorer.AnchorTypes.cs` (`EmitWallMidpoints`, `EmitDoorAnchor`, `EmitWindowSills`,
   the door/window/ceiling/grid/column emitters), emit a one-shot
   `StingLog.Warn` AND increment a per-rule diagnostic counter (the `RuleDiagnostic` object
   already exists on `PlacementResult` — add an `AnchorMisses` / `FellBackToRoomCentre`
   count if not present).
2. Also warn when `ComputeCap` derives a cap greater than the candidate count (silent
   under-fill): `"rule X cap=N but only M candidates generated — placed M"`.
3. Surface both in the **run report** (per-rule section already lists counts): show
   "anchor fell back to room centre: K room(s)" and "under-filled: cap N vs M" per rule, so
   every other algorithm gap becomes self-reporting.

**Acceptance:** running a door-anchored rule in a doorless room shows a clear per-rule warning
in the report ("DOOR_HINGE found no doors in 3 room(s) — used room centre"), not a silent
mis-placement.

---

## Constraints & house rules
- One branch (`claude/placement-centre-review-audit`); no merge to `main`.
- Read before edit; targeted `Edit`s; one logical change per commit (suggest one commit per
  Item).
- Revit transactions wrapped + named `STING …`; `[Transaction(Manual)]` for state-changers;
  `TaskDialog`/inline report for user messages; all error paths via `StingLog`; never abort a
  run on a single rule/seed failure.
- **Keep the working path byte-identical when a feature is off**: Item 3 fields default to 0
  (no-op); Item 1 seed tier only fires when no family is otherwise resolved; Item 2 bridge is
  additive-only and toggleable.
- Reuse existing engines (`SymbolLibraryCreator`, `FamilyParamEngine`, `GetBoundary`,
  `ParamRegistry`) — don't re-implement.
- Compile with `build.bat` (stages only, no Revit install). Log work in `docs/CHANGELOG.md`;
  note the "no in-Revit verification" caveat. **Items 1 & 2 need an in-Revit smoke test before
  merge** (build a seed for an empty category and place it; swap to a non-STING family and
  confirm values survive).

## Suggested order
1. **Item 4** (diagnostics) — cheap, and it makes Items 1/3 self-verifying in the report.
2. **Item 1** (seed tier + EnsureSeeds) — the headline UX fix.
3. **Item 3** (linear densify + clearance) — placement quality.
4. **Item 2** (swap bridge) — manufacturer adoption.

## Definition of done
- A run with no families loaded for ticked categories places seed families and reports it.
- Swapping to a STING-naive family preserves positions + values (via stamp+restore+alias).
- Linear rules hit their target count along walls; clearance rules respect door/window gaps.
- Every anchor fallback / under-fill is visible in the run report.
- CHANGELOG entry; the model-modifying items flagged for in-Revit verification.
