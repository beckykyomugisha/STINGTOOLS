# G-8 — Type vs Instance binding: measurement and proposal

**Status: PROPOSAL. No binder, no data file and no parameter was changed.**
This decides whether thousands of parameters bind on type or instance across every
existing project. It is the owner's call.

Measured 2026-08-09 on `claude/kibale-integration` @ `3a66fc285`. Every number below is
reproducible from the shipped data; the commands are in the appendix.

---

## 1. The question, restated precisely

`ParameterHelpers.CachedLookup` is `el.LookupParameter(name)` — **instance-only, no type
traversal**. `FormulaEvaluatorCommand` collects with `WhereElementIsNotElementType()`
(`FormulaEvaluatorCommand.cs:68`) and writes through that lookup
(`:145` → `targetParam.Set(...)`).

So: **if a formula target is Type-bound in a model, the formula engine cannot write it.**
No exception, no warning — `CachedLookup` returns null and the formula is skipped by the
`if (targetParam == null …) continue` on the next line.

The register asked whether the declared bindings are what models actually carry. They are
not, and the reason is mechanical rather than accidental.

---

## 2. What the data declares

| Source | Formula targets found | Declared **Type** | Declared **Instance** |
|---|---|---|---|
| `MR_PARAMETERS.csv` (`Binding_Type` column) | 302 of 303 | **296** | 6 |
| `CATEGORY_BINDINGS.csv` (binding column) | 240 of 303 | **231** | 7 (+1 mixed, +1 header artefact) |

**63 formula targets have no row in `CATEGORY_BINDINGS.csv` at all.**

> **Correction to the register.** The entry says *"of 302 formula targets the data declares
> 289 as Type-only"*. The 302 is right for `MR_PARAMETERS.csv`; the Type count there is
> **296**, not 289. And `MR_PARAMETERS.csv` is **not the file the binder reads** — see §3 —
> so the operative figure is **231 of 240** from `CATEGORY_BINDINGS.csv`. Neither 289 nor
> 296 is the number that decides anything.

---

## 3. What the binders actually do

Three code paths create bindings. They do not agree, and **only one of them reads the
`Binding_Type` declaration at all.**

| Binder | Reads | Creates |
|---|---|---|
| `Tags/LoadSharedParamsCommand` | `CATEGORY_BINDINGS.csv` (categories only) | **`NewInstanceBinding` unconditionally.** Zero occurrences of `NewTypeBinding` in the file. |
| `Temp/DataPipelineCommands` → `DynamicBindingsCommand` (`:606`, binder at `:792`) | `CATEGORY_BINDINGS.csv` **including** the bind-type column | `NewTypeBinding` or `NewInstanceBinding`, by **majority vote per parameter**: `isType = count(Type) > count/2` (`:757`) |
| `Temp/TemplateManagerCommands` (`:2813`) | — | `NewTypeBinding` |

`MR_PARAMETERS.csv`'s `Binding_Type` column — the one carrying 296 Type declarations — is
read by **no binder**. It is documentation.

### The decisive mechanic: first binder wins, permanently

Neither binder converts an existing binding.

- `LoadSharedParamsCommand` skips anything already bound by name
  (`existingBindings.Contains(d.Name)` → `alreadyBound++`, `:202`).
- `DynamicBindingsCommand`, on an existing binding, edits the category set and calls
  `ParameterBindings.ReInsert(def, existingBinding)` — **re-inserting the existing binding
  object**, so its Type/Instance kind is preserved (`:778-782`).

There is no Instance↔Type conversion anywhere in the codebase. **Whichever command touched
a parameter first fixed its kind for the life of that model.**

---

## 4. What a model actually ends up with

| Setup path taken | Result for the 231 | Formula writes |
|---|---|---|
| `LoadSharedParams` only *(the documented path)* | **Instance** | ✅ work |
| `DynamicBindings` / Template Manager first | **Type** | ❌ **231 targets write nothing, silently** |
| Mixed, in any order | per-parameter, by whoever ran first | partially dead, unpredictably |

**The evidence says real models are Instance-bound and formulas do write.** The
runtime-verified binding baseline for a clean project — 3,018 bound / 374 skipped / 0
conflicts — came from the `LoadSharedParams` path, which creates Instance bindings
exclusively. That is consistent with formulas having demonstrably produced values across
this batch (`lookup()` alone turned 29 call sites from writing `0` to writing real
quantities, verified against shipped data).

**So the alarming reading — "a large fraction of the formula work has never written
anything" — is most likely false, and it is false for a fragile reason:** not because the
declarations are right, but because the binder that ignores them is the one people run. A
project set up via Dynamic Bindings has 231 dead formula targets today and nothing reports
it.

This is not verified in Revit. It is inference from the code paths plus one runtime
baseline, and §6 says how to settle it in ten minutes.

---

## 5. Options

**A. Make the declaration true — bind Type where declared.**
Honest to the data, and Uniclass/NBS-style classification genuinely belongs on the type.
But it requires the formula engine to write to `el.GetTypeId()` for those parameters, which
changes formula semantics from per-instance to per-type — a 900 mm door and a 2100 mm door
of the same type would share one computed value. **Wrong for most of these formulas**, which
compute per-element quantities. Also a migration on every existing model.

**B. Make the declaration match reality — change the data to Instance.**
Zero code change, zero model migration, and it makes `CATEGORY_BINDINGS.csv` describe what
every real model already has. Loses the (currently inert) intent that some parameters are
type-level. Cheapest and lowest-risk.

**C. Leave both, add a reconciliation report.**
A read-only command that walks `doc.ParameterBindings` and reports, per parameter,
*declared* vs *actual* kind and whether the formula engine can reach it. Changes nothing;
turns an invisible failure into a visible one. **This is the H-1 shape** — report what
actually happened.

**D. Make the two binders agree.**
Have `DynamicBindingsCommand` stop creating Type bindings, so setup order stops mattering.
One-line change, removes the whole class of divergence, but silently overrides a declared
intent without deciding what that intent should be.

### Recommendation

**C now, then B — and D alongside B.**

C first because it costs nothing, cannot break a model, and answers §4 with fact instead of
inference. Run it against a real Kibale model and the question is settled: either the
bindings are Instance and this is a documentation defect, or some are Type and there is a
live silent-write bug with a known blast radius of 231.

Then B rather than A, because the formulas are per-element by construction — the declared
Type intent is wrong for this workload, not merely unimplemented. B is the only option that
does not require a migration on delivered models.

D with B, because leaving a second binder that can still mint Type bindings would let the
defect reappear on the next project set up in a different order.

**What I would not do is A**, and not because it is expensive: making 231 per-element
quantity formulas write per-type would be a correctness regression that looks like a
cleanup.

---

## 6. How to settle §4 in ten minutes, before deciding

Open a real project that has been through setup and run, in the Revit Python/macro console
or as a throwaway command:

```csharp
foreach (var name in new[] { "CST_CALC_BLOCKS_NR", "BLE_ELE_AREA_SQ_M", "PLM_PPE_FLW_LPS" })
{
    var it = doc.ParameterBindings.ForwardIterator();
    while (it.MoveNext())
        if (((Definition)it.Key).Name == name)
            StingLog.Info($"{name}: {(it.Current is InstanceBinding ? "Instance" : "Type")}");
}
```

If all three report **Instance**, §4's optimistic reading is confirmed and this is a
documentation defect — take B + D.
If any reports **Type**, there is a live silent-write bug and C becomes urgent, not optional.

`Core/Electrical/CableSizerApplyEngine.cs:286-310` already contains exactly this
Instance-vs-Type detection and can be lifted rather than rewritten.

---

## Appendix — reproducing the numbers

```bash
# formula targets, and their declared binding in each file
python - <<'PY'
import csv, io, collections
rows = list(csv.reader(io.open('StingTools/Data/FORMULAS_WITH_DEPENDENCIES.csv', encoding='utf-8-sig')))
targets = {r[1] for r in rows[1:] if len(r) > 2 and r[1]}
print('formula targets:', len(targets))

bind = collections.defaultdict(set)
for l in io.open('StingTools/Data/CATEGORY_BINDINGS.csv', encoding='utf-8'):
    f = l.rstrip('\n').split(',')
    if len(f) >= 3 and not f[0].startswith('#'):
        bind[f[0]].add(f[2])
c = collections.Counter(tuple(sorted(bind[p])) for p in targets if p in bind)
print('CATEGORY_BINDINGS.csv:', c, 'no row:', len(targets - set(bind)))
PY

# no code path converts an existing binding
grep -rn "NewTypeBinding" --include=*.cs StingTools/          # 2 sites, both binders
grep -rn "NewInstanceBinding" --include=*.cs StingTools/Tags/LoadSharedParamsCommand.cs
```
