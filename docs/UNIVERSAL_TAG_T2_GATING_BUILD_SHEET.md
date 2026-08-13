<!--
  Option A build sheet — gate the T2 block on STING_Tag_Universal.

  DERIVED, NOT INVENTED. Every name, formula, prefix and break below is rows 2-7
  of docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md, which remains the authoritative
  source. Regenerate the machine copy with:
      python tools/extract_universal_tag_rows.py
  and verify a built family against it with the UniversalTag_Diff command.
-->

# Universal Tag — T2 gating build sheet (Option A)

**Six calculated values. One tier gate. Roughly a morning.**

## Why only six

`UniversalTag_Diff` measured `STING_Tag_Universal` on 2026-08-13:

| | |
|---|---|
| family parameters, total | 82 |
| spec SOURCE parameters bound | **7 of 62** |
| parameters carrying ANY formula | **0** |

Those 7 are the T1 primary row plus the six T2 sources. **T4–T10 were never built
into this family** — 55 of the parameters they would read are not bound. The
65-row master in `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` describes a target that
was never realised here.

So there is nothing to strip out, and building T4–T10 would mean binding 55
shared parameters, creating 64 calculated values and hand-placing 64 label rows
— for content that belongs in schedules. Gating what already exists gives a real
two-state tag for a fraction of that.

**Result when done:** `TAG_PARA_STATE_2_BOOL` off → the tag is the ISO code
alone, `M-BLD1-Z01-L01-HVAC-SUP-SAT-0025`. On → code plus description, status,
standard and systems. Revit omits a parameter that evaluates to empty, so the
lines collapse rather than leaving gaps.

---

## Step 0 — check the gate's storage type (30 seconds)

In **Family Types**, find `TAG_PARA_STATE_2_BOOL` and look at its type.

| Its type | Formula form to use below |
|---|---|
| **Yes/No** (expected, v5.4+) | `if(TAG_PARA_STATE_2_BOOL, …)` — **bare**, as written in the table |
| **Text** (legacy) | `if(TAG_PARA_STATE_2_BOOL = "Yes", …)` — add `= "Yes"` to every row |

Comparing a Yes/No gate to `"Yes"` fails with Revit's *Inconsistent Units*. If
the bare form is rejected, you have a Text gate — switch forms rather than
editing the parameter.

## Step 1 — build ONE row first, and test it

Do **row 2 only**, then check the tag in a view with the gate on and off.

This de-risks the one assumption worth testing: that a calculated value in a
**tag** family can read a shared parameter whose value comes from the tagged
element. The technique is standard and `UNIVERSAL_TAG_FINALIZE_RUNNER.md`
records it working on the Duct tag — but it has not been re-verified on this
family, and finding out after building six rows is worse than after one.

**If row 2 toggles correctly, do the other five. If it does not, stop** — the
remaining five would fail the same way, and the answer is Option B (leave T2
ungated) rather than more rows.

## Step 2 — the six rows

Create each as a **Text** family parameter, then set its formula.

Preferred route, per row: **Edit Label → fx (Add parameter)** → enter Name,
Discipline **Common**, Type **Text**, paste the Formula.

If your Revit's dialog offers no formula field there, do it in two passes
instead: create all six in **Family Types → New Parameter** (Text, Common),
set each **Formula** in Family Types, then add them to the label in Step 3.

| # | Calculated value name | Formula | Prefix | Break |
|---|---|---|---|---|
| 2 | `Show Tier 2 - 2` | `if(TAG_PARA_STATE_2_BOOL, ASS_TAG_2_TXT, "")` | | YES |
| 3 | `Show Tier 2 - 3` | `if(TAG_PARA_STATE_2_BOOL, ASS_DESCRIPTION_TXT, "")` | | YES |
| 4 | `Show T2 - Status` | `if(TAG_PARA_STATE_2_BOOL, ASS_STATUS_TXT, "")` | `Status:` | YES |
| 5 | `Show Tier 2 - 7` | `if(TAG_PARA_STATE_2_BOOL, RGL_STD_TXT, "")` | `Std:` | YES |
| 6 | `Show T2-Ph179 - Asset Systems` | `if(TAG_PARA_STATE_2_BOOL, ASS_SYSTEMS_TXT, "")` | `Sys:` | **no** |
| 7 | `Show T2-Ph179 - Mec System` | `if(TAG_PARA_STATE_2_BOOL, MEC_SYS_TXT, "")` | `MSys:` | YES |

No suffixes on any of the six. Row 6 is the only one without a break — Sys and
MSys share a line.

**Use these names exactly.** `UniversalTag_Diff` looks them up by name; a
different name reports as `calculated value missing` even when the row works.

## Step 3 — put them in the label

In **Edit Label**, for each of the six:

1. Add the new calculated value to the label (right arrow)
2. Set **Spaces = 0**
3. Set **Prefix** from the table
4. Tick **Break** per the table

**Order matters: Spaces before Break.** Spaces is only editable while the row
*above* has no break, so ticking breaks first locks you out of the spacing.

Then **remove the six raw source rows** from the label (left arrow):
`ASS_TAG_2_TXT`, `ASS_DESCRIPTION_TXT`, `ASS_STATUS_TXT`, `RGL_STD_TXT`,
`ASS_SYSTEMS_TXT`, `MEC_SYS_TXT`.

Removing a parameter from the label does **not** unbind it from the family, so
the formulas keep reading it. Leave them in and every line renders twice.

Final label order, top to bottom: the T1 primary row, then rows 2–7 above.

## Step 4 — verify

Save the family, load it into a project, and run **Diff Universal**
(CREATE TAGS tab).

Expect on the six T2 rows:

```
correct — gated formula present     6
calculated value missing           58
```

The 58 are T4–T10, which this family does not implement — expected, not a
failure. What matters is that the six read `correct`.

Then toggle `TAG_PARA_STATE_2_BOOL` on the tag **type** and confirm the tag
collapses to the ISO code alone and expands back.

## Step 5 — propagate

Once the master verifies, `Propagate_UniversalTag` clones the label to the
loaded STING tag families. Smoke-test on one family first — see
`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`.

---

## Not in scope

**T3** is the per-family engineering block — wall build-up on a wall tag,
duct-terminal data on a duct tag — and cannot ride a master cloned to 206
families. It belongs to the per-family authoring path, where the v5 tag-config
CSVs already declare it: 783 rows across 152 of 156 families, each with its
formula already written in the CSV's `Formula` column. That is a separate piece
of work with its own data source; do not add T3 rows to this master.

**T4–T10** stay in schedules. Cost, carbon, commissioning, fabrication, clash,
as-built and compliance data are all live in the model and all schedulable; a
drawing tag is not where they earn their keep.
