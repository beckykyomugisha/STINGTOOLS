# T6 — Family-derived materials: proposal

**Status: PROPOSAL. No code, no family modified.** Four decisions at the end need a human.

---

## 1. The problem, stated precisely

A door reaches the material schedule as **one priced unit**. That is not a bug — a door *is*
bought as an assembly — but it means a schedule cannot answer "how many hinges", "how much
ironmongery", or "what area of glazing", which a Ugandan tender schedule normally itemises.

The reason it cannot is that **the model does not say**. Revit's door and window families
carry geometry, not a bill of parts. The existing STING parameters on them are dimensional
only:

```
BLE_DOOR_WIDTH_MM      BLE_DOOR_HEIGHT_MM      BLE_DOOR_HEAD_HEIGHT_MM   BLE_DOOR_FUNCTION_TXT
BLE_WINDOW_WIDTH_MM    BLE_WINDOW_HEIGHT_MM    BLE_WINDOW_SILL_HEIGHT_FROM_FLR_MM
```

**Nothing about what the door is made of.**

### What must not happen

Three rules that inferred materials from *type names* were withdrawn in PR #710 for pricing
whole walls as paint. A door type called `Flush Door - Timber 900x2100` must **not** be split
into timber leaf + ironmongery on the strength of the word "Timber". A name is a label, not a
declaration, and the failure is silent: it produces confident quantities nobody checks.

So the rule for T6 is the same as for every other source in this schedule: **emit only what
the model states.** If a family does not declare its materials, one priced unit is the
accurate answer — not a limitation to code around.

---

## 2. The proposal in one line

**Add five TEXT parameters. Add no dimensional ones.** Everything measurable is already
derivable from width and height, which every conforming family already carries.

That is the whole insight: the gap is **material identity**, not measurement.

### 2.1 The five parameters

Following the existing `BLE_<SUBJECT>_<PROPERTY>_<TYPE>` convention, all **type** parameters
(a door type's leaf material does not vary per instance):

| Parameter | Example value | Purpose |
|---|---|---|
| `BLE_DOOR_LEAF_MATERIAL_TXT` | `Flush timber, hollow core` | Names the leaf. Priced per m² of leaf. |
| `BLE_DOOR_FRAME_MATERIAL_TXT` | `Hardwood 100x50` | Names the frame. Priced per m of frame. |
| `BLE_DOOR_IRONMONGERY_SET_TXT` | `IM-02` | A **set code**, not a list. See §2.3. |
| `BLE_WINDOW_FRAME_MATERIAL_TXT` | `Aluminium, powder-coated` | Priced per m of frame. |
| `BLE_WINDOW_GLAZING_TYPE_TXT` | `Clear float 6mm` | Priced per m² of glazed area. |

### 2.2 What is DERIVED, not declared

| Quantity | Derived as | Why not a parameter |
|---|---|---|
| Leaf area (m²) | `WIDTH_MM × HEIGHT_MM` | Already in the model. A second source would drift. |
| Frame length (m) | `2 × HEIGHT_MM + WIDTH_MM` | Standard three-sided frame. |
| Glazing area (m²) | `WIDTH × HEIGHT × glazedFraction` | See decision D4 — this one is **not** exact. |

Deriving rather than declaring is deliberate: **two sources for one number always drift**,
and the drift is invisible. It is the same reason the schedule's Summary is projected from
its body rather than authored beside it.

### 2.3 Ironmongery: a set code, not a list

A family declares **which** set (`IM-01`); a corporate table declares **what is in it**. So
the family carries one short string, and a change to the standard ironmongery schedule is one
file edit, not a re-issue of every door family.

`StingTools/Data/STING_IRONMONGERY_SETS.json`, with the usual project override at
`<project>/_BIM_COORD/ironmongery_sets.json`:

```json
{
  "sets": [
    { "code": "IM-01", "description": "Internal single leaf, non-lockable",
      "items": [ { "item": "Butt hinge 100mm", "perLeaf": 3, "unit": "nr" },
                 { "item": "Lever handle on rose", "perLeaf": 1, "unit": "set" } ] },
    { "code": "IM-02", "description": "Internal single leaf, lockable",
      "items": [ { "item": "Butt hinge 100mm", "perLeaf": 3, "unit": "nr" },
                 { "item": "Mortice lockset", "perLeaf": 1, "unit": "nr" },
                 { "item": "Lever handle on rose", "perLeaf": 1, "unit": "set" } ] }
  ]
}
```

**This is declared data, not a ratio.** Three hinges per leaf is a specification the QS
writes, not a heuristic — so ironmongery is **family-derived**, and must not carry the
practice-heuristics banner that T4's consumables do. Conflating the two would devalue the
banner where it matters.

---

## 3. How it reaches the schedule

Unchanged machinery, four new constituent kinds:

| Kind | Unit | Commodity | Stage |
|---|---|---|---|
| `door_leaf` | m² | `door-leaf` | `doors-windows` |
| `door_frame` | m | `door-frame` | `doors-windows` |
| `ironmongery` | nr / set | per item in the set table | `doors-windows` |
| `window_glazing` | m² | `glazing` | `doors-windows` |

**The unit guard applies unchanged**: a rule's `sourceUnit` must match the emitted unit, or
no conversion happens. **Wastage stays in the supplier rule.** **The door itself becomes a
memorandum** once its parts are listed — declared in `intermediateMeasures` with children
`[door_leaf, door_frame, ironmongery]`, conditional on those children actually being present,
exactly as blockwork is. Otherwise the schedule would price the door **and** its parts.

That last point is the one most likely to be got wrong, and it is the same defect that PR
#777 had to make unrepresentable for masonry.

---

## 4. Conformance scoring — a real hazard

`FamilyConformanceInspector` scores 8 dimensions to exactly 100, with
**PASS ≥ 85 / WARN 70–84 / BLOCK < 70**.

**Do not add a 9th dimension inside the 100.** Every existing family would be rescaled, and
families that audited PASS yesterday would silently become WARN today — not because they got
worse, but because the ruler changed. A reclassification that looks like a regression is
exactly the kind of confident wrong signal this schedule has spent sixteen fixes removing.

**Recommendation:** report material declaration as a **separate, unscored section** of the
conformance report — "declares materials: yes/no, missing: […]" — leaving the 100-point
verdict untouched. It answers "which families can feed a material schedule?" without
disturbing "which families are structurally sound?".

---

## 5. What happens to families that declare nothing

**Nothing changes.** One priced unit per door, as today. No warning fatigue, no zero rows.

The export's notes gain one line, in the established denominator style:

```
Family materials: 47 door/window type(s) inspected, 0 declare a leaf or frame material.
Doors and windows are priced as units. Add BLE_DOOR_LEAF_MATERIAL_TXT (and the rest)
to the family types to itemise them.
```

Silent absence is what made "no tiling appeared" ambiguous for three exports. This says which
of the two situations you are in.

---

## 6. Effort and order

| Step | Effort | Depends on |
|---|---|---|
| Shared-parameter definitions + registry entries | 1h | D1 |
| `STING_IRONMONGERY_SETS.json` + loader + tests | 3h | D2, D3 |
| Emitter + memorandum wiring + scan tally + tests | 4h | — |
| Conformance report section | 2h | D5 |
| Adding parameters to loaded families | **automatable** | see the correction below |

> **CORRECTED.** This row originally read *"yours — not a code task"*. That was wrong.
> `FamilyAugmentationEngine` already does `EditFamily` → `FamilyManager.AddParameter` →
> `LoadFamily` on families loaded in the project, with a rollback beside it. Adding the
> parameters is automatable; see
> [`MATERIAL_SCHEDULE_BASELINE_LAYER2_LAYER3_SPEC.md`](MATERIAL_SCHEDULE_BASELINE_LAYER2_LAYER3_SPEC.md).
>
> What remains human is the VALUE, not the parameter — and the line between a reviewed
> mapping table (a declaration) and a silent code mapping (an inference, withdrawn in #710)
> is set out in §5 of that spec.

---

## 7. Decisions needed

- **D1 — Are the five parameters right, and are they type parameters?** Instance-level would
  let one door in a run differ, at the cost of every door needing filling individually.
- **D2 — Ironmongery by set code?** The alternative is per-item parameters on the family,
  which is more precise and much heavier to author and maintain.
- **D3 — What are your standard sets?** `IM-01…IM-nn` need real content. The two above are
  placeholders written to show the shape, not a recommendation.
- **D4 — Glazing: measure or not?** `WIDTH × HEIGHT × glazedFraction` is an approximation —
  the frame and any transom are not deducted. Options: (a) accept it, banner-flagged as
  approximate; (b) add `BLE_WINDOW_GLAZED_AREA_M2` as a declared value, exact but another
  field to author; (c) leave glazing inside the window unit rate. **Recommendation: (c) now,
  (b) later** — an approximate area presented next to measured quantities is precisely the
  mixing §1 warns against.
- **D5 — Conformance: separate section, or fold into the 100?** Recommendation as in §4:
  separate.

---

## 8. Not recommended

- Inferring material from a type or family name (PR #710).
- A default ironmongery set for families that declare none — it would put hinges on every
  opening in the model, including fixed lights.
- Deriving leaf area from the door's host wall opening: an opening is not a leaf, and
  double-leaf doors would silently halve.
