# QS review — NRM2 work sections for the site, civil and external-utility rows

`nrm2_site_civil_review.csv` in this folder is a review sheet, not a decision.

## What is being asked

`StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv` carries an `Nrm2` column that decides
which **NRM2 work section** a priced BOQ line is billed under. For most divisions that
mapping came from the project's own history. For divisions **31 / 32 / 33** — site
preparation, exterior improvements and external utilities — it carries the code implied
by the CSI division and this file's existing convention.

That is a strict improvement on what those rows had before, which was a category-keyword
fallback that put `Generic Models`, `Topography` and `Pads` into a general bucket. It is
**not** a quantity surveyor's judgement, and it has not had one.

A wrong code here does not fail. It moves money into the wrong section of an issued
tender, quietly, because the number is plausible either way.

## How to review

```
python tools/qs_nrm2_review.py --export     # refresh the sheet from the map
```

Open the CSV. For each row, fill in:

| Column | What to put |
|---|---|
| `QS_VERDICT` | `ok` if the current code is right, `change` if it is not |
| `QS_NRM2` | when changing: the correct NRM2 work section |
| `QS_NOTE` | free text; recorded here, never written into the map |

Then:

```
python tools/qs_nrm2_review.py --apply --dry-run   # see what would change
python tools/qs_nrm2_review.py --apply             # fold the answers into the map
```

The answers land as data rather than as a message somebody has to re-key.

## What `--apply` refuses

It writes only rows explicitly marked `change` with a code that differs, and it refuses
rather than guesses when:

- `QS_NRM2` is empty on a row marked `change`;
- the code appears **nowhere else in the map** — far more likely a typo than a section
  this project has never billed under, and a typo creates a section that silently
  collects nothing;
- the sheet has gone **stale** (the category or section no longer matches the row it
  claims), so it cannot write to whatever row moved into that slot;
- the verdict is neither an agreement nor a change.

Re-running the same reviewed sheet changes nothing the second time.

## Before you review: the `Nrm2` column is not NRM2 numbering

A first pass filled every row's `QS_VERDICT` / `QS_NRM2` / `QS_NOTE` (see the sheet). It
surfaced something that changes how the whole column should be read, and that a reviewer
needs before the first row makes sense.

**The integers in this column are an in-house vocabulary, not RICS NRM2 work-section
numbers.** The authority is `GuessSectionName` in
[`StingTools/BOQ/BOQCostManager.cs`](../../StingTools/BOQ/BOQCostManager.cs), which is what
actually prints the bill headings:

| | this project | RICS NRM2 |
|---|---|---|
| `1` | Demolitions | Preliminaries |
| `3` | Groundworks | Demolitions |
| `22` | Furniture, fittings and equipment | Windows, screens and lights |
| `33` | Mechanical services | Drainage above ground |
| `36` | Security and fire alarm | Fencing |

`14` Masonry, `15` Structural metalwork and `16` Carpentry *do* coincide with NRM2. **That
partial agreement is the hazard** — a hybrid that agrees in the middle and diverges at both
ends reads as NRM2 to anyone who spot-checks it, so a technically-correct NRM2 number
written here moves money silently. NRM2 `36` Fencing, for instance, lands in this project's
*Security and fire alarm*.

**Review into the in-house vocabulary above, not into published NRM2.**

### Two structural gaps that no per-row verdict can close

1. **`3` Groundworks and `31` Drainage below ground are defined in `GuessSectionName` but
   used on no row of the map.** They are the correct home for the earthworks rows (131–134)
   and the below-ground drainage rows (154–162). Because `--apply` refuses a code that
   appears nowhere else in the map — correctly, since a novel code silently collects
   nothing — those rows cannot be answered until one row somewhere uses `3` and `31`.
2. **There is no external-works section at all.** Nothing in the vocabulary covers roads,
   paving, kerbs, fencing, soft landscaping or site furniture; NRM2's `35`–`38` are all
   occupied here by services. Every division-32 row currently carries `4`, so roads, lawns
   and fences print under a bill heading reading **"Foundations"**.

Twenty-five of the thirty-seven rows are blocked on one of these two. They are marked
`change` with `QS_NRM2` deliberately left **empty** and the reasoning in `QS_NOTE`, rather
than filled with a plausible wrong code.

**`--apply` is all-or-nothing**: it aborts the whole run if any row refuses, so the seven
answerable rows cannot land while the twenty-five are outstanding. That is the tool as
built, not a fault in the sheet — but it means the vocabulary question has to be settled
first.

## Context

The `Nrm2` value is per **row**, not per section — section `03 30 00` is NRM2 5 for a
concrete floor and 14 for a concrete wall — which is why the sheet carries the full rule
(category, family regex, type regex, system) and not just the section number.

Tracked as **LIFE-2** in [`../ROADMAP.md`](../ROADMAP.md). That row stays open until a QS
has actually signed this sheet; shipping the tooling is not the same as having the answer.
