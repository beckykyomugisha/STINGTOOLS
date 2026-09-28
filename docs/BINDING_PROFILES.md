# Binding profiles — opt-in parameter bindings

A **binding profile** is a named set of parameter → category bindings that a
project switches on. It adds to the corporate baseline in
`CATEGORY_BINDINGS.csv`; it never removes from it.

Added 2026-09-21. Corporate library: `StingTools/Data/STING_BINDING_PROFILES.json`.

---

## Why this exists rather than more rows in the CSV

`LoadSharedParamsCommand.cs:395` states the rule the binding layer is built on:

> Each discipline-scoped parameter binds to **its own** category set … *This stops
> cross-discipline leakage (params showing on Ducts, etc.)*

The corporate baseline is deliberately narrow, and that narrowness is load-bearing.

Some parameters genuinely need a **wide** category set — but only in the minority
of projects using that feature. The case that forced this:

The three **Multi-Category LPS reuse tag families** exist because BS EN 62305-3
lets a lightning protection system reuse existing structure — foundation rebar as
a Type B earth electrode, a metal roof or metallic facade as a natural air
termination. Between them they display **13 `ELC_LPS_*` parameters across 15 host
categories**. Measured 2026-09-21: **91 of those 93 bindings did not exist**, so
the tags placed and rendered **blank**.

Two ways to fix that, and one of them is wrong:

| | Effect |
|---|---|
| 93 rows in `CATEGORY_BINDINGS.csv` | tags work — and 13 electrical parameters land on every Roof, Wall, Wall Sweep, Mullion, Fascia, Gutter, Soffit, Foundation and rebar element **in every project**, most of which have no LPS at all |
| A profile | tags work in projects that ask for it; every other project is byte-identical to before |

The first is exactly the leakage the design prevents. Hence profiles.

## Enabling one

Create `<project>/_BIM_COORD/binding_profiles.json`:

```json
{ "enabled": ["lps-natural-components"] }
```

Then run **Load Shared Params**. The log records what happened:

```
SharedParamGuids.WithProfiles: [lps-natural-components] added 91 binding(s) across 13 parameter(s)
```

No file, an empty list, or an unreadable file all mean *nothing enabled* — the
project behaves exactly as it did before profiles existed.

## Shipped profiles

| id | Adds | Required by |
|---|---|---|
| `lps-natural-components` | 13 `ELC_LPS_*` params × 15 structural / architectural categories | the three LPS reuse tag families |

## Guarantees, and why each one matters

- **Additive only.** A profile can widen a parameter's category set, never narrow
  one. Enabling a profile therefore cannot blank a tag somewhere else. Asserted by
  `BindingProfilesTests.MergeNeverRemovesABaselineCategory`.
- **The baseline is never mutated.** It is a cached static in `SharedParamGuids`;
  mutating it would leak one project's profile into every later document in the
  session. Asserted by `MergeDoesNotMutateTheBaseline`.
- **An unknown id is reported, not ignored.** A typo in an enabled list produces a
  logged warning naming the id. Silence would be indistinguishable from success.
- **Reconcile Bindings knows about profiles.** Both binding call sites in
  `LoadSharedParamsCommand` go through `WithProfiles`, so reconcile cannot see a
  profile-added binding as a stray and undo what the project asked for.
- **The shipped profile is validated against real data.** Every parameter must
  exist in `MR_PARAMETERS.txt` and every category in `PARAMETER_REGISTRY.json`'s
  `category_enum_map`, so a typo fails a test rather than silently costing one
  binding in a project nobody is watching.

## Adding a profile

Append to `STING_BINDING_PROFILES.json`:

```json
{ "id": "kebab-case-id",
  "name": "Human name",
  "description": "What it is for, and what renders blank without it",
  "requiredBy": ["STING - Some Tag"],
  "bindings": [ { "param": "ABC_X_TXT", "categories": ["Roofs", "Walls"] } ] }
```

No code change. The shipped-data tests cover it automatically.

## Not yet verified in Revit

The merge is Revit-free and tested. **Whether a Multi-Category tag renders a
shared parameter bound this way has not been confirmed in Revit** — it should be
proven on one family first (place the Foundation Earth tag on a rebar element and
read the label) before relying on it across the library. Same discipline as the
duct tag: one family, then the rest.
