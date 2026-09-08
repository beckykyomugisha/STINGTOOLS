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

## Status

The review is **complete and independently verified**. `--apply` reports *37 agreed, 0 to
change, 0 refused, 0 still unreviewed*, and a second, adversarial pass is recorded in
[`VERIFICATION.md`](VERIFICATION.md) — 31 confirmed, 5 acceptable, 1 overturned.

**What remains is a countersignature.** `VERIFICATION.md` §10 carries a sign-off block with
name, professional registration, organisation, date and signature, left deliberately blank.
Nothing in this repository can fill it: a signature is an accountability record, and it is
the one part of a QS review that cannot be produced by reviewing.

### The one verdict the verification overturned

**Buried gas moved from `33` Mechanical services to `32` Piped supply systems**, and the
in-building gas run (`23 11 23`) moved with it. Section `32` is literally *Piped supply
systems*, which is what a gas main is; every other occupant of `33` is HVAC, so gas was the
outlier there rather than the precedent.

The pairing matters more than the value. Which of the two rules an element matches turns on
whether its pipe **type name** happens to contain "buried" / "external" / "underground" — so
if the two rows ever carry different sections, one installation bills under two headings on
a naming accident. `Both_Gas_Rows_Bill_Under_The_Same_Section` pins that.

### One caveat the signer inherits

**A pile whose family name contains none of `pile` / `bored pile` / `CFA` falls through to
the bare `Structural Foundations` rule and bills at `5` In-situ concrete, not `4`.** That is
a naming dependency, not a classification error — `5` is right for the pad and strip footings
that rule exists to catch — but it means two identical piles can bill under two headings if
one is misnamed. Fixing it in the map would misclassify footings; it belongs in the
modelling standard as a family-naming rule.

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

### All 37 rows are now answered and applied

`--apply` reports **37 agreed, 0 to change, 0 refused, 0 still unreviewed**. What that
means precisely: every row carries a verdict and written reasoning, and the map now holds
the reviewed answer. **It does not mean a QS has signed it** — see the top of this file.

Three sections were added to `GuessSectionName`, because the scheme had **no external-works
section at all**:

| | |
|---|---|
| `40` | External works — roads, paving and kerbs |
| `41` | Fencing, gates and barriers |
| `42` | Soft landscaping |

NRM2's own external-works numbers were not available — `35` Site works and `36` Fencing are
taken here by services — and reusing `37`/`38` at their NRM2 values would have deepened the
trap described above: a vocabulary that agrees with NRM2 at `14`/`15`/`16` and diverges
elsewhere invites a reader to conclude it *is* NRM2. A fresh block above the existing range
cannot be misread as alignment.

Two rows needed something the `Nrm2` column cannot express:

- **The fence/balustrade rule was split.** One rule matched `fence|gate|balustrade` and gave
  all three one code; a balustrade is a railing, and no single answer was right. Matching is
  score-based rather than positional, so the new rule was **appended** — the sheet addresses
  rows by number, and inserting mid-file would renumber every row after it.
- **`Entourage` now never reaches takeoff.** It is Revit's presentation context — the cars,
  people and trees that make a render read as a place — and nobody buys it. No `Nrm2` value
  can say "not measured": every element that reaches takeoff gets a section, from its rule
  or from `DeriveNrm2Section`'s keyword fallback. So it is enforced where it belongs, in the
  takeoff exclusion set beside the 2D content. Unlike that content it is real 3D geometry,
  so it never looked like noise — it priced as plausible "each" rows.

### What was closed on 2026-09-08

**`3` Groundworks and `31` Drainage below ground are now used by the map** (4 and 7 rows).
They were defined in `GuessSectionName` and used nowhere, and `--apply` would not write
them: its validity test was *"does another row already use this code"*, and its refusal
told you to *"add it to a row that already bills under it first"* — which was the very
apply it was blocking. Correct, defined, and unreachable.

Two changes fixed that, and both are worth knowing before you use the tool:

- **`--apply` now validates against `GuessSectionName`, not against map usage.** That
  function is what prints the heading, so it is the authority on what a section is. Usage
  was the wrong test in both directions: it made a defined-but-unused code unwritable, and
  a code would have become unwritable again the moment its last row was re-classified. The
  parse fails **loudly** if the function is renamed — a quiet fallback would restore the
  trap while looking like the tool working.
- **`--apply` now writes the rows it can and names the rows it cannot**, exiting non-zero.
  It used to write nothing at all if any row refused, which made it unusable on a real
  review: a review normally has rows that cannot be answered yet, and holding the
  answerable ones hostage to them meant nothing ever landed.

`StingTools.Boq.Tests/Nrm2VocabularyTests.cs` pins the reverse defect, which nothing was
watching: a code the map bills under that `GuessSectionName` does not name prints the raw
Revit category instead of a heading.

## Context

The `Nrm2` value is per **row**, not per section — section `03 30 00` is NRM2 5 for a
concrete floor and 14 for a concrete wall — which is why the sheet carries the full rule
(category, family regex, type regex, system) and not just the section number.

Tracked as **LIFE-2** in [`../ROADMAP.md`](../ROADMAP.md). That row stays open until a QS
has actually signed this sheet; shipping the tooling is not the same as having the answer.
