# Native-type parameter migration (#338) — scope analysis and recommendation

**Date:** 2026-07-21
**Verdict: DO NOT LAND the migration as-is.** It does not do what it appears to
do in any existing project, and there is no shipped migration path that would
make it do so.

This document is the output of the compiler-in-the-loop regeneration that
[`ROADMAP`] asked for. The mechanical regeneration was **deliberately not
performed** — the analysis below is why. The generator is included so every
number here is reproducible:

```
python tools/transform_mr_params.py --dry-run
```

---

## 1. What the migration proposes

Flip ~157 shared parameters from `TEXT` to native Revit types
(`LENGTH` / `AREA` / `CURRENCY` / `INTEGER` / `NUMBER`), flip 27 `_BOOL`
parameters from `TEXT` to `YESNO`, and add a `_TXT` "display mirror" parameter
beside each native one so tags and schedules can still read a formatted string.

Measured against **main's current** `MR_PARAMETERS.txt` (3,392 params):

| Change | Count |
|---|---|
| Datatype flips (`TEXT` → native) | **157** |
| `_BOOL` `TEXT` → `YESNO` | **27** |
| New `_TXT` mirror params added | **175** |
| Target params no longer present on main | 3 |
| Resulting param-file size | 3,488 → 3,663 lines (**+5%**) |

## 2. How many of those parameters are already bound

| Binding file | Rows touching target params | Distinct target params bound |
|---|---|---|
| `CATEGORY_BINDINGS.csv` | 1,256 | **123** |
| `FAMILY_PARAMETER_BINDINGS.csv` | 495 | 108 |
| `PARAMETER_CATEGORIES.csv` | 110 | 109 |

So **123 of the 157 flipping parameters are already bound to categories** in
every project created from this template.

## 3. Why it does not take effect — the blocking finding

A shared parameter's data type is part of its *definition*, keyed by GUID. Revit
will not redefine an existing GUID with a different data type.

The codebase already knows this, from experience. `LoadSharedParamsCommand.cs`
detects exactly this case and its documented behaviour is to **skip**:

> ```
> // Skip if a SharedParameterElement already holds this GUID
> // (or this name) in the project. Revit refuses to add a definition
> // whose GUID matches an existing one if name OR data type differs,
> // and the failure is severity=Error so BindingWarningSwallower
> // (warning-only) cannot dismiss the resulting modal.
> ```
>
> ```
> // Inserting blind risks Revit's unrecoverable "cannot be added"
> // Error-severity modal that the failure preprocessor can't eat.
> ```

`LoadSharedParamsCommand` computes `typeMismatch` and adds the parameter to a
`typeConflicts` list that is reported to the user and otherwise skipped.

**Therefore, in every existing project, after this migration:**

1. The 123 already-bound parameters keep their existing **TEXT** definition.
2. `LoadSharedParams` reports all 123 as type conflicts and **binds none of them**.
3. The 175 `_TXT` mirrors have fresh GUIDs, so they *do* bind — as empty params.
4. Net effect: the project gains 175 empty parameters and **migrates nothing**.

Meanwhile the C# side would have been rewritten to write native doubles
(`SetDouble` + unit conversion) into parameters that, in those projects, are
still `TEXT`. New projects built from the template get native types; existing
projects do not. That is a **silent behavioural fork between new and existing
projects**, which is materially worse than the status quo — the same code path
produces different results depending on the age of the file it is run against,
with no error to signal it.

## 4. There is no migration path in the repo

Making the flip actually take effect in an existing project requires, per
parameter, per project: unbind from every category → delete the
`SharedParameterElement` → rebind from the new file → re-parse and re-write
every stored value with unit conversion (a stored `"2400"` in a TEXT param does
not become 2400 mm in a LENGTH param; internal units are decimal feet).

No such command exists. The `RemoveBinding` / `ReInsert` code that does exist is
for changing a parameter's *category set*, and `LoadSharedParamsCommand`
documents that even for that narrower job:

> ```
> // - ReInsert() silently fails to actually change categories in many cases
> // - ReInsert using ExternalDefinition FAILS silently
> ```

A migration whose success path depends on an API the codebase has already
documented as silently unreliable should not ship without a verified,
in-Revit-tested migration command and a rollback story.

## 5. What the change actually buys

Native types give correct Revit schedule sorting, unit formatting and rounding —
genuinely valuable, and the right long-term architecture.

But the `_TXT` mirror re-introduces a string copy for display, which means:

- **two parameters per quantity** to define, bind, and keep in sync;
- **a sync obligation on every write** (`SetDouble` must also write the mirror,
  which is what the corrected `WriteTxtMirror` helper does);
- tags and schedules that read the mirror get **no native sorting anyway** —
  they are reading a string again.

So the schedule-sorting benefit only materialises for consumers that read the
*native* parameter, while the migration's own display strategy routes the main
consumer (tags) back to a string.

Call-site coupling is also thinner than the migration's size suggests — only
~59 C# references to the 157 target params in 1,443 scanned files:

| Call kind on target params | Sites | Consequence after a flip |
|---|---|---|
| `SetString` / `SetIfEmpty` | 26 (17 params) | **Runtime failure** — cannot set a string on a native param |
| `GetString` | 31 (26 params) | Silent behaviour change (formatted value or empty) |
| `SetDouble` | 2 | Already correct |

Note this contradicts the review's estimate of "~40 native double write sites
bypassing `SetDouble`". The measured figure is **2** `SetDouble` sites and
**26** `SetString` sites — the wiring problem is the *opposite* of the one
described: the risk is existing string writers breaking, not missing double
writers. (981 `LookupParameter` sites exist repo-wide; they resolve parameter
names dynamically and cannot be attributed by static grep, so a residual
unknown remains — which is itself an argument against a blind flip.)

## 6. Recommendation

**Do not land #338 as a single migration.** Specifically:

1. **Drop** the 157-parameter datatype flip and the 175 `_TXT` mirrors for now.
2. **Keep** the `_BOOL` → `YESNO` direction as a separate, already-in-flight
   track (#337 / #479), which is lower-risk: `YESNO` is stored as an integer
   0/1, most `_BOOL` params are already `YESNO` in `MR_PARAMETERS.txt`
   (260 of 287), and that work is confined to the binding CSVs.
3. **If native types are wanted**, sequence them properly:
   - Ship a verified, in-Revit-tested **binding-migration command** first
     (unbind → delete → rebind → re-parse values with unit conversion), with a
     dry-run report and a rollback, and prove it on a real project.
   - Then migrate in **small batches** (one discipline / one unit family at a
     time), each batch with its own before/after value audit.
   - Decide the display strategy first. If tags read `_TXT` mirrors, the
     schedule-sorting benefit is largely forfeited and the migration is mostly
     cost; if tags are moved to read native params with Revit formatting, the
     mirrors are unnecessary and 175 parameters disappear from the plan.
4. **Adopt native types for NEW parameters going forward** — no migration cost,
   all of the benefit. This captures most of the value at near-zero risk.

The one localized fix worth keeping from the #338 work is the correction already
sitting on `claude/review-5zi8sy-338` (PR #467): `SetDouble` converting `_MM`/`_M`
values through `UnitUtils.ConvertToInternalUnits` before `p.Set`, and
`WriteTxtMirror` converting internal→display before stringifying. Those are
correct regardless of whether the wider migration ever happens, and that branch
**builds clean** (0 warnings, 0 errors, Revit 2025).

## 7. Pre-existing inconsistency found while measuring

`ASS_CST_STALE_BOOL` is `YESNO` in `FAMILY_PARAMETER_BINDINGS.csv` but `TEXT` in
`MR_PARAMETERS.txt`. This exists on `main` today and is **not** introduced by
#337/#479 (verified against that branch's blob: it is the only such divergence
before and after). It is a one-line fix and a good example of the class of drift
the native-type work is trying to address.
