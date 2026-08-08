# Kibale review — Part I fixes 1–4

Implements items 1–4 of the fix order in `GUIDES/STINGTOOLS_GAPS_KIBALE_REVIEW.md`
(Part I). That document is **not on `main`** — it lives on
`claude/kibale-np-bim-modeling-f5e653` at commit `344b90b73`.

Four defects, all of the same family: **the tool produced a confident number it
had not earned.** A formula that could not be evaluated wrote `0`. A material
rate priced ~3,700× low and outranked the correct rate. An IFC export reported
thousands of successful stamps having written nothing. Readiness gates reported
100 % on an empty bill.

Items 5–8 of the fix order (`ParseCsvLine` `""` un-escaping, the placed-*n* link
warning, implementing `lookup()`, the identity-class normaliser) are **not** in
this PR. No files under `StingTools/Data/` are touched.

---

## ⚠️ Expect a rise in skipped formulas. That is the fix working.

**This is the one thing to understand before reviewing or merging.**

`FormulaEngine` used to substitute `0` for every failure — divide-by-zero,
unknown identifier, unresolved `lookup()`, TEXT in arithmetic — and
`WriteNumericResult` writes whenever the current value is empty or near-zero. So
a formula that could not be evaluated **stamped a real-looking `0` quantity into
the model**, which then priced through to the bill as a genuine measured zero.

Those paths now return `null`, and a `null` means *skip this element*.

### Why the blast radius is wide

`ParsePrimary` now fails when a context value is **present but unparseable**,
which includes the empty string. Two measured facts make that common:

1. **190 of the 191 resolvable inputs to numeric formulas are declared `TEXT`**
   in `MR_PARAMETERS.txt` (the 191st is `YESNO`; 182 of 190 numeric-formula
   *targets* are `TEXT` too). Counted over
   `FORMULAS_WITH_DEPENDENCIES.csv` — 302 formula rows, 190 non-TEXT.
2. **Empty strings are deliberately added to the evaluation context**
   (`FormulaEvaluatorCommand.cs:654-658`, *"Always add string params to context
   (even empty strings) so conditional formulas like `if(PARAM<>\"\", ...)`
   evaluate correctly"*).

So on a partially-populated model, an unpopulated TEXT quantity parameter arrives
in the context as `""`, fails to parse, and the formula is now **skipped**.
Previously it evaluated with that input treated as `0` and wrote a number.

**A formula that is now skipped was previously producing a wrong answer, not a
right one.** The count of written values will fall. The count of *trustworthy*
written values will not.

### How to tell a real skip from a missing input

Every skip logs one line to `StingTools.log` naming the reason and the
expression. The reason distinguishes the two cases, because they enter the
evaluator by different routes:

| Log reason | What it means | Where to fix it |
|---|---|---|
| `unknown identifier 'X'` | The parameter is **not on the element at all** — `CachedLookup` returned null and it never entered the context (`:640`). | A **binding** problem. Check `CATEGORY_BINDINGS.csv` and which setup command was run — see gap G-8, the two binders disagree on Type vs Instance. |
| `non-numeric value for 'X'` | The parameter **exists but is empty**, or holds text. | A **data** problem. Populate the input. This is the expected majority case on a partially-modelled project. |
| `unresolved function 'lookup()'` | Gap G-1 — `lookup()` is not implemented. 27 formulas call it. | Nothing to do here; those formulas were writing `0` before and are now correctly skipped. Fix order item 7. |
| `division by zero` | An input resolved to zero and was used as a divisor. | Usually a knock-on of one of the two above. |
| `undefined power (…)` | `0^-1`, `(-1)^0.5`. | Genuine arithmetic error in the formula — see G-10. |

Warnings are capped at 200 **per batch** (reset at each batch boundary, so a
messy model cannot silence later runs).

**Triage order:** if you see a lot of `unresolved function 'lookup()'`, that is
expected and pre-existing. If you see a lot of `unknown identifier`, suspect
bindings. If you see a lot of `non-numeric value`, the model is under-populated —
which is information the tool was previously hiding from you.

---

## Commits

| Commit | Gap | Change |
|---|---|---|
| `5ee46d27c` | G-5 | Failed formula evaluation returns `null`, never a silent `0` |
| `2113dfedb` | E-1/E-1b | Material-library rates labelled `USD` so the FX layer converts |
| `60e24be6a` | H-1 | IFC Qto writer counts writes, not visits; fails loudly on zero |
| `b11289a7e` | H-3 | Zero denominator is never 100 % |
| `ba703bb23` | H-1 | Export gated on **quantities** written, not parameters (review fix) |
| `5d7443105` | G-5 | Warn budget reset per batch, not per session (review fix) |

### 1 — Formula engine (G-5)

Four failure paths record a reason instead of substituting `0`; `EvaluateNumeric`
turns that into `null`. It already returned `double?` and all nine call sites
already guard on `HasValue`, so no caller changed.

`if()` is treated as **lazy**: both branches must be parsed to advance the
cursor, but only the branch actually returned may fail the formula. Without
this, a divide-by-zero in the discarded branch would void results the model is
entitled to — the fix would have caused its own regression.

### 2 — Material rates (E-1/E-1b)

`MaterialCommands` writes `ALL_MODEL_COST` from the library's
`MAT_COST_UNIT_USD` column; the provider read it back labelled `UGX`, suppressing
the conversion it needed. At priority 95 it also outranks the correct category
rate from `CsvRateProvider` (90), so the wrong figure won.

FX path verified end-to-end, not assumed: `BOQCostManager.cs:872` reads
`UGX_PER_USD` → `RateProviderRegistry.Get` → `ConvertCurrency` →
`RateCurrency.ToUgx` case `"USD"` → `rate * ugxPerUsd`.

The `MAT_COST_UNIT_UGX` column is **not** the fix: it is `USD × 3700` on all 815
BLE rows and 441 of 464 MEP rows (remainder 3750/3722 rounding), so reading it
would freeze a 2026 rate into every material permanently.

**Left open, with a note in the code:** a hand-typed UGX cost is now read as USD
and inflated. The provider cannot separate the two while both share
`ALL_MODEL_COST`; that needs the dedicated `STING_MAT_RATE_*` pair stamped at
material creation (gap E-12).

### 3 — IFC quantity writer (H-1)

All stamp helpers return `bool`, true only when `par.Set` actually took.
`StampAllElements` returns an `IfcStampTally` of *elements visited* / *elements
written* / *parameters written* / *quantities written*.

The export gate is **`QuantitiesWritten == 0`**, not the combined parameter
count. Gating on the combined count would let the commonest broken configuration
through: `Pset_StingCost.*` are STING's own shared parameters and arrive with the
standard `LoadSharedParams` run, while the IFC-standard `Qto_*` names must be
added deliberately. `Pset_StingCost.Currency` is a hardcoded non-empty string, so
it alone would satisfy a combined gate on every element — and the IFC would ship
with cost data against zero quantities, which is worse than an empty file
because it looks priced.

The two failure shapes get different messages, because they need different
fixes: *nothing bound at all* → run the shared-parameter load; *cost bound, Qto
not* → the `Qto_*` names are not part of that load and must be added to the
shared-parameter file and bound.

Also changed, because the tally is wrong without it: `IfcMaterialPsetWriter` had
the identical silent-return shape on the same path, so its `Set`/`SetString`
return `bool` and `Stamp` returns a count. It has exactly one caller.

### 4 — Zero denominators (H-3)

`pricedPct`, `epdPct` and `SchemeCoveragePct` returned **100** when they had
nothing to divide by, so an empty BOQ reported *"100 % priced"* and *"100 %
EPD-verified"* to the QS. The rest of the codebase already disagrees —
`CompliancePercent`, `StrictPercent`, `RevisionPercent`, `SheetCompliancePct`,
`DataCompletenessPercent` and `BOQModels.cs:423` all return 0 here.

All three now distinguish *nothing to measure* from *measured, and it is zero*:
the BOQ reports display `"n/a"` and say plainly there were no rows;
`SchemeCoveragePct` becomes `double?` with a `SchemeCoverageText` companion.

---

## Verification status

**Compile-verified only.** `dotnet build StingTools/StingTools.csproj -c Debug`
after every commit, and a clean `-t:Rebuild` at the end: **0 errors, 0
warnings**.

**Not yet run in Revit.** Two behaviour changes need a live model before merge:

1. **A formula that now skips.** Confirm the skip rate against a real project and
   sample the log reasons against the triage table above.
2. **An export that now refuses.** Confirm `BOQExportIfcQtoCommand` and
   `Cost_StampIfcQuantities` abort on a project without the `Qto_*` parameters
   bound, and succeed on one with them.

## Known interaction, out of scope

`StampQuantity` still returns early on `value <= 0`, so a legitimate
zero-quantity line counts as "not written" and nudges toward the abort. A project
whose BOQ is genuinely all-zero would abort with the "no quantities" message.
Not addressed here — flagged for a follow-up alongside fix-order items 5–8.
