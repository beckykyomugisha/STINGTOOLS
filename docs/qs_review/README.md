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

## Context

The `Nrm2` value is per **row**, not per section — section `03 30 00` is NRM2 5 for a
concrete floor and 14 for a concrete wall — which is why the sheet carries the full rule
(category, family regex, type regex, system) and not just the section number.

Tracked as **LIFE-2** in [`../ROADMAP.md`](../ROADMAP.md). That row stays open until a QS
has actually signed this sheet; shipping the tooling is not the same as having the answer.
