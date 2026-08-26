# PLNS-CPD-01 — Course Pack Toolchain

Generates every document in the ISO 19650 CPD course pack from a single source of truth,
and fails the build if anything in the repository contradicts it.

**Zero dependencies.** Node 18+. No install step, no lockfile, nothing to rot.

```bash
node cpd/validate.mjs              # check for drift  (CI gate — exit 1 on failure)
node cpd/build.mjs                 # build the primary pack   -> cpd/dist/
node cpd/build.mjs --variant=1     # build an equivalent resit paper
node cpd/certificates.mjs roster.csv
```

---

## Why this exists

Three hand-written documents in this repository once taught the published-information codes
three different ways. The marketing-site guide mapped S4–S7 to Published; the KUT BEP used
A1/B1; the MIDP template followed the BEP. All three were maintained by hand, so nothing
could tell you they disagreed.

**The fix is structural, not editorial.** The code tables now live in exactly one file, every
document renders from it, and a validator fails the build when prose anywhere in the repo
contradicts it.

```
cpd/data/codes.json ──┬──► field-guide.html
                      ├──► BEP_TEMPLATE.md
                      ├──► MIDP_TEMPLATE.csv
                      ├──► workbook-answers.html
                      └──► assessment-marking.html

node cpd/validate.mjs  checks the rest of the repo against it
```

---

## Layout

| Path | What it is |
|---|---|
| `data/codes.json` | **Single source of truth.** Parties, requirement chain, name fields, role and type codes, the two code families, CDE states, self-audit, glossary. |
| `data/course.json` | Course identity, learning outcomes, run sheet, neutrality rules. |
| `data/assessment.json` | 15-item bank with model answers, marking guidance and **scenario variants** for resits. |
| `data/exercises.json` | Exercises 1–4 with model answers and teaching notes. |
| `data/drift-baseline.json` | Legacy drift accepted at baseline. **May shrink, never grow.** |
| `data/delegates.sample.csv` | Roster format for the certificate generator. |
| `lib/theme.mjs` | Shared visual system + HTML helpers. Theme-aware, print-ready. |
| `build.mjs` | Renders the pack into `dist/`. |
| `validate.mjs` | Drift and consistency gate. |
| `certificates.mjs` | Roster CSV → print-ready A4 landscape certificates. |

---

## Editing

**To change a code, a role, a CDE rule:** edit `data/codes.json`, run `node cpd/build.mjs`.
Every document updates together. Never edit the generated files in `dist/` — they are
overwritten.

**To add an assessment question:** append to `data/assessment.json` `items[]`. The validator
enforces that marks sum to `totalMarks`, that the pass mark stays at or above 70%, that every
MCQ answer index is in range, that every written item has a model answer, and that every
examinable outcome is covered.

**To produce a resit paper:** add a second entry to an item's `variants[]` and run with
`--variant=1`. Marks and the outcome map are identical by construction, so the papers are
equivalent — which is what makes the resit defensible if a board asks.

---

## What the validator checks

| Rule | Fails when |
|---|---|
| `MARKS-SUM` | Item marks don't sum to `totalMarks` |
| `PASS-MARK` | Pass mark drops below the advertised 70% |
| `MCQ-ANS` / `MCQ-OPTS` | An answer index is out of range, in any variant |
| `NO-ANSWER` | A written item has no model answer |
| `LO-UNCOVERED` | An outcome is neither examined nor marked `assessedBy` |
| `RUNSHEET-MINS` | The run sheet doesn't add up to the advertised contact hours |
| `NAME-FIELDS` | The container name stops having exactly seven fields |
| `CODE-FAMILY` | A CDE state is assigned the wrong code family |
| `S-CODE-PUBLISHED` | Prose maps an upper S code to the Published container |
| `S7-TABLE` | Prose presents S0–S7 as one table without the authorization family |

### Two tiers

**Tier 1 — hard fail.** `cpd/`, `marketing-site/`, `GUIDES/`, `docs/CPD_*`. These documents
*teach* the codes, so being wrong here means being wrong in front of delegates.

**Tier 2 — baselined.** Everything else. Many of those files describe *shipped software
behaviour*; rewriting the prose without changing the code would create a worse
inconsistency. The known set is recorded in `data/drift-baseline.json` and **may shrink,
never grow** — a new hit outside the baseline fails the build.

Removing a legacy drift raises a `BASELINE-STALE` warning telling you to delete the entry, so
the list stays honest.

Regenerate the baseline **deliberately** and only after reviewing the diff:

```bash
node cpd/validate.mjs --update-baseline
```

---

## Certificates

```bash
node cpd/certificates.mjs roster.csv --out=dist/nov-kampala.html
```

CSV columns (header required, order free): `name, registration, board, date, result, marks`.
Leave `result` blank to derive pass/fail from `marks` against the pass mark.

The generator **withholds** a certificate and tells you why when a delegate has no
professional registration number (the certificate is issued against it), no name, or a
failing score. Output is one A4 landscape page per delegate — print to PDF with margins set
to none.

Serials are deterministic — `PLNS-CPD-01-{year}-{initials}{registration tail}` — so a
certificate can be checked against the roster without a database.

---

## CI

`.github/workflows/cpd-validate.yml` runs the validator and rebuilds the pack on every push
that touches `cpd/`, `docs/`, `GUIDES/` or `marketing-site/`, and fails the build on drift.
