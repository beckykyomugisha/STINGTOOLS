# STING_Tag_Universal.rfa — conformance against the code contracts

**No `.rfa` was edited.** The Revit API cannot author label rows, so every gap below closes with
operator steps in the Family Editor, not with code.

Measured 2026-08-09 against `StingTools/Data/TagFamilies/STING_Tag_Universal.rfa` (872,448 B, now
entry `tag-universal` in `STING_CONTENT_MANIFEST.json`).

---

## 0. What my probe can and cannot see — read this first

I can enumerate the file's OLE2 streams without Revit. For the 206 per-category families that was
decisive: their parameter names sit in `PartAtom` as readable text.

**For the universal tag it is not.** Its `PartAtom` is 2,030 bytes; the payload is in `Partitions/0`
(589,850 B), which is compressed. Across *all* streams and both encodings my probe finds **7**
identifiers — `TAG_PARA_STATE_4..10_BOOL` — while the operator, with the family open in Revit
2025.4, reports roughly **90** (about 80 `ASS_`/`COM_`/`HVC_`/`MNT_`/`PER_`/`PRJ_`/`RGL_`/`STR_`
parameters, gates 1–10, `TAG_WARN_VISIBLE_BOOL`).

**That is a false-negative rate above 90 %.** So for this family the probe is evidence of *presence*
only, never of absence. Every "ABSENT" below rests on the operator's Revit inspection, not on my
scan, and is marked accordingly. Treating my zero counts as confirmation would be exactly the
mistake this workstream keeps closing.

| Claim | Source | Confidence |
|---|---|---|
| gates 4–10 exist | probe **and** Revit | certain |
| ~80 STING params, gates 1–3, warn bool, labels T2–T10 exist | Revit only | operator-reported |
| `HANDOVER_MODE_*`, style matrix, `TAG_DEPTH_TIER_INT`, types absent | Revit only | operator-reported, **probe cannot corroborate** |

---

## 2.1 `HANDOVER_MODE_*` — the dual-wire design is LIVE, and the family cannot participate

**What the code requires.** `ParamRegistry.cs:1026-1041` documents it: families carry *both* the
Handover and Design-&-Construction row sets, and each T4–T10 row's formula AND-gates
`TAG_PARA_STATE_N_BOOL` with one of the trio, so switching pattern is a project-level toggle rather
than a family re-author.

**Is it still live? Yes — it has a real writer and a real reader:**

| Role | Site |
|---|---|
| Writer | `Core/Drawing/TokenProfileApplier.cs:634-636` — `SetYesNo(typeEl, MODE_HANDOVER/MODE_DC/MODE_CUSTOM, …)`, mutually exclusive |
| Reader | `Core/TagConfig.Tag7.cs:1903-1904` — `ReadParaStateBool(typeEl, MODE_HANDOVER)` → `"HANDOVER"` / `"CUSTOM"` |
| Selector map | `Core/HandoverModeHelper.cs:41-43` maps mode → selector bool, and picks the per-mode tag-config CSV |
| Consumer | `Tags/ApplyParagraphPresetCommand.cs:422` |

It is not superseded. It is wired end to end **except** at the family.

**Consequence.** `TokenProfileApplier` writes to `typeEl` — the tag *type*. If the trio are not
family parameters in the tag, `SetYesNo` returns `false` and throws nothing, so the mode switch
silently does nothing. (Since H-4, `SafeWrite` reports that class of failure; this call site is not
yet routed through it.)

**Operator steps.** In the Family Editor, Family Types → New Parameter, three times:

| Name | Type | Group | Instance/Type |
|---|---|---|---|
| `HANDOVER_MODE_HANDOVER_BOOL` | Yes/No | Other | Type |
| `HANDOVER_MODE_DC_BOOL` | Yes/No | Other | Type |
| `HANDOVER_MODE_CUSTOM_BOOL` | Yes/No | Other | Type |

Add them as **shared** parameters from `MR_PARAMETERS.txt` (all three are declared there) so the
GUIDs match what `TokenProfileApplier` writes.

Then, for each T4–T10 row, the visibility formula is the AND of the tier gate and the mode:

```
and(TAG_PARA_STATE_4_BOOL, HANDOVER_MODE_HANDOVER_BOOL)
```

…for a Handover row, and `HANDOVER_MODE_DC_BOOL` for the Design-&-Construction twin. **Bare gate
name, no `= "Yes"`** — these are YESNO and Revit rejects a string comparison with *Inconsistent
Units* (see §4 of `LABEL_DEFINITIONS.json`'s `calculated_value_templates._comment`).

Set exactly one of the three ticked; `TokenProfileApplier` will maintain that afterwards.

---

## 2.2 The style matrix — and whether it should be there at all

**What the code requires.** `Tags/FamilyConformanceCheckCommand.cs:88-92` samples the 128-parameter
matrix with two fingerprints: `TAG_2_5_NOM_BLACK_BOOL` and `TAG_3_BOLD_BLUE_BOOL`.

**Scoring, read from the checker (`:135-273`), 100 points:**

| Band | Points | Universal tag |
|---|---|---|
| Category + template classification | 15 | likely 15 — it has a category |
| Placement parameters bound by GUID (6 pts × 4, rounded to 25) | 25 | unknown — depends which 4; operator can read them |
| Tag fingerprint params (3 pts each: `TAG_PARA_STATE_1/2_BOOL`, `WARN_VISIBLE_BOOL`) | 10 | **10** — all three reported present |
| **Style matrix sample (5 pts each)** | 10 | **0 — both fingerprints absent** |
| Tag visibility tiers (4 pts each) | 10 | **10** |
| remainder | 30 | not itemised here |

Verdict bands are `Score >= 85 ? PASS : …` at `:273`, with hard `BLOCK` at `:112` and `:122` for
unopenable / non-tag files.

**Losing 10 of 100 does not by itself fail the 85 threshold** — but it is a guaranteed 10-point
deficit on every run, so the family needs ~95 of the remaining 90 to pass. In practice this check
will report the universal tag as WARN or BLOCK until the matrix question is settled.

**Should it carry the matrix?** My recommendation: **no — and the checker should be changed, not the
family.** The 128-parameter matrix existed because there were 206 families and no other way to vary
text size/style/colour per tag. With one universal family, per-*type* variants are the natural
carrier (§2.3), and 128 Yes/No parameters × one family is a worse trade than 128 parameters × 206
families only in that it is now visibly absurd. But this is a design decision, not a defect —
**flagged for the owner, not decided here.**

Until it is decided, `FamilyConformanceCheck` will keep scoring the universal tag down for a matrix
it may never be meant to have. That is the checker asserting a contract that the architecture moved
past.

---

## 2.3 No types — every code path that depends on a tag TYPE NAME

The family reportedly has no types (blank Type name). These paths key off the canonical type name
`"2.5_BOLD_RED_Filled30_T3"` or `TAG_DEPTH_TIER_INT` and **no-op against a typeless family**:

| Site | What it does | Behaviour with no types |
|---|---|---|
| `Tags/TagStyleEngine.cs:1411` `ResolveVariantSymbol` | finds the `FamilySymbol` whose name matches `(size, style, colour, arrowhead, depthTier)` | returns `InvalidElementId`; caller falls back to the current type and logs "MigrateTagFamilies has not been run" |
| `Tags/TagTypeVariantWriter.cs:125` | writes `TAG_DEPTH_TIER_INT` per minted type | nothing to write to |
| `Tags/TagStyleCommands.cs:452-462` | the deprecation notice telling users depth is **type-based** now | the advice it gives is unreachable |
| Tag Studio ParaDepth slider → SmartPlace / BatchPlace / Tag&Combine | picks the correct variant at placement | always falls back to the single nameless type |

**This is the decision the brief says to enumerate, not resolve — so, enumerated:** depth is
currently *designed* as per-type (`TagStyleCommands` calls the per-instance path "deprecated" and
`TagStyleEngine` resolves by type name), but the universal family as shipped can only support
per-instance. One of the two has to move. Both are coherent; they are not compatible.

---

## 2.4 All ten tier gates plus warn are ticked — the "?" explosion

Every gate ticked means every tier renders, and any tier whose source parameter is unpopulated shows
`?`. With 10 tiers on and `TAG_WARN_VISIBLE_BOOL` on as well, a freshly placed tag on a
lightly-populated element is mostly question marks.

**Recommended shipped default: T1 and T2 on, T3–T10 off, warn off.** T1–T2 is the identity payload
that is populated on any tagged element; T3+ depend on discipline data that arrives later in the
project, and the warning row is a QA overlay, not a default view.

**Operator steps.** Family Editor → Family Types (the dialog, with no type selected — these are the
family's default values):

1. `TAG_PARA_STATE_1_BOOL` ✔
2. `TAG_PARA_STATE_2_BOOL` ✔
3. `TAG_PARA_STATE_3_BOOL` … `TAG_PARA_STATE_10_BOOL` ✘ (all eight)
4. `TAG_WARN_VISIBLE_BOOL` ✘
5. Save, then reload into any open project **with "Overwrite the existing version and its parameter
   values"** — the plain overwrite keeps existing instance values and the change will appear not to
   take.

---

## 2.5 The binding question — and it is worse than "unconnected"

**Two separate findings. The first answers the question; the second is why it could not have worked
anyway.**

### (a) Statically, these are two unconnected populations

`Core/Drawing/AnnotationRunner.cs:790` writes `TAG_PARA_STATE_{t}_BOOL` onto **`el` — the tagged
element** (the duct, the door), not onto the tag.

The tag family's label-row visibility is driven by **family parameters evaluated inside the family's
own formula context**. In Revit, a family parameter's formula can reference only parameters *in that
family*. A tag *label* can display a host element's parameter; a *visibility* formula cannot read
one. There is no mechanism by which an element-side `TAG_PARA_STATE_3_BOOL = 1` reaches the tag
family's row-3 visibility.

So: **element-side gates and family-side gates are two unconnected populations**, and writing the
former does not change what the tag renders.

### (b) The element-side population does not exist either

> **CORRECTED 2026-08-10.** The table below was right about the two binders it named and
> **wrong to conclude the element-side population does not exist**. There is a THIRD binder
> route, and on any project that has run Project Setup the gates *are* Type-bound on 42 host
> categories. The conclusion in (a) is unaffected — see "What this changes" below.

| File | Role | `TAG_PARA_STATE_*` / `TAG_WARN_VISIBLE_BOOL` |
|---|---|---|
| `MR_PARAMETERS.txt` | shared-parameter declarations | **present** — 10 gates + warn |
| `BINDING_COVERAGE_MATRIX.csv` | coverage *claim* | **marked in 47 categories** |
| `CATEGORY_BINDINGS.csv` | what `LoadSharedParamsCommand` binds | **ZERO rows** |
| `RESOLVED_BINDINGS.csv` | the spec-driven binder's input | **present (3,019 rows), ZERO for these** |
| `FAMILY_PARAMETER_BINDINGS.csv` | what `BatchAddFamilyParamsCommand` binds | **420 rows — 42 categories × 10 gates, plus 42 for warn, all `Type`** |

The 47-category figure comes from the coverage matrix, which is a report. But
`FAMILY_PARAMETER_BINDINGS.csv` is **not** a report — `BatchAddFamilyParamsCommand`
(`Temp/TemplateManagerCommands.cs:2636`) reads it and creates real project bindings via
`doc.ParameterBindings` + `NewTypeBinding(catSet)`. That command is **not optional in practice**: it
runs inside `ProjectSetupCommand` (`:361`, `:606`) and `MasterSetupCommand` (`:253`).

**So on a set-up project the gates ARE Type-bound on host element types, and type-scoped writes to
them DO land.** What they do not do is *matter* — see (a). Instance-scoped writes still fail, because
`ParameterHelpers.SetInt` resolves via `Element.LookupParameter`, which cannot see a type parameter
from an instance.

### What this changes

| Writer | Scope | Lands? | Affects the tag? |
|---|---|---|---|
| `AnnotationRunner` (removed) | host **instance** | **No** — Type-bound param, instance lookup | No |
| `TokenProfileApplier.WriteCategoryDepths` | host **type** | **Yes** (post-setup) | **No** — wrong population, per (a) |
| `TagStyleEngine.SetParagraphDepth` | *all* element types, incl. tag `FamilySymbol`s | **Yes** | **Yes** — this is the functional path |

`AnnotationRunner`'s writes were removed for the stronger reason: unreachable by construction, not
merely unbound. `WriteCategoryDepths` was kept — but on this evidence it is **inert for tag
rendering** and is a candidate for removal in its own right. Logged, not actioned.

**G-8 stands but narrows**: the defect is not "nothing is bound", it is that a coverage matrix,
three binder inputs and two writer scopes disagree about which population is authoritative.

### Can `WriteCategoryDepths` reach a label row? As shipped, NO — but not for the reason you'd guess

The method is **not intrinsically incapable**. It collects
`FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()` and groups by category name,
which **includes `IndependentTag`s**. `el.GetTypeId()` on a tag returns its `FamilySymbol` — the very
population the label formula reads. So if a `categoryDepths` key named a **tag** category, the existing
code would write the gates straight onto the tag type and it would work.

Measured: it never does. The only shipped `categoryDepths` live in `STING_DRAWING_TYPES.json` — **2
blocks, 4 distinct keys, all host categories**: `Mechanical Equipment`, `Pipe Fittings`,
`Duct Fittings`, `Air Terminals`. `STING_VIEW_STYLE_PACKS.json` has **35 packs and zero**
`categoryDepths`.

**So as configured, the writes land on host element types and cannot reach the tag** — and the cheap
fix, if per-category depth is wanted, is a **configuration change** (key on `Air Terminal Tags` rather
than `Air Terminals`), not code.

### The 2-minute in-Revit confirmation

**This test settles both open questions at once** — whether host-side values reach the label (2.5a),
and whether the family-side gates are TYPE or INSTANCE parameters, which is what §C is held on.

Use an **Air Terminal**: it is in the 42 categories bound by `FAMILY_PARAMETER_BINDINGS.csv` *and* one
of the 4 shipped `categoryDepths` keys, so both mechanisms are in play on the same element.

1. Open a project that has had **Project Setup** run (that is what applies the Type binding). Place the
   universal tag on an **Air Terminal**.
2. Select the air terminal → **Edit Type**. Look for `TAG_PARA_STATE_3_BOOL`.
   - **Present** → confirms the Type binding from `FAMILY_PARAMETER_BINDINGS.csv`.
   - **Absent** → `BatchAddFamilyParams` has not run here; run Project Setup or test elsewhere.
3. Tick it **on the type**. Watch the tag.
   - **Row 3 does not appear** → confirms host-side gates are inert (answer to 2.3 = **no**). Expected.
   - **Row 3 appears** → the whole §2.5(a) analysis is wrong; stop and report, that would be a
     significant finding.
4. Now select the **tag** itself and find `TAG_PARA_STATE_3_BOOL`. **Where it appears is the answer to
   §C:**

   | Where it appears | What it means | Consequence |
   |---|---|---|
   | Tag's **Edit Type** | gates are **TYPE** family parameters | `SetParagraphDepth`'s type sweep reaches them. Per-type depth works today. **Keep per-type; unhold §C as per-type.** |
   | Tag's **instance Properties** | gates are **INSTANCE** family parameters | The type sweep never reaches them — depth is broken today, and per-instance is forced. **Convert, and write the missing per-instance writer.** |

5. Tick it wherever it appeared. Row 3 should appear on the tag. That is the controlling parameter.

**Report which of the two rows in step 4 you saw.** That single observation unholds §C and the
playbook's Part 3E depth paragraph.

---

## 2.6 Family Category — NOT VERIFIED, and it matters

**I could not read the family's category.** `PartAtom` carries no `OST_*` token and `Partitions/0`
is compressed; per §0 my probe cannot establish absence. The file name gives no hint.

**Why it matters.** Multi-Category Tags cannot tag Rooms — Rooms need a Room Tag family
(`OST_RoomTags`). If the universal tag is Multi-Category, a separate Room Tag is still required.

`STING - Room Tag.rfa` **is present** in the library and is manifest entry `tag-room` (category
`Rooms`), so the role is already filled by one of the 206 — no new family is needed. It carries the
old-set parameter payload, so it will need the same treatment as the universal tag if Rooms are to
get tiered labels.

**Operator check:** open `STING_Tag_Universal.rfa` → Family Category and Parameters. Report the
category. If it is **Multi-Category**, also confirm whether Spaces and Areas need their own tags on
this project — they have the same restriction as Rooms.

---

## Summary

| # | Contract | State | Closes with |
|---|---|---|---|
| 2.1 | `HANDOVER_MODE_*` trio | live in code, absent from family | 3 shared params + per-row `and(...)` formulas |
| 2.2 | 128-param style matrix | absent; **may be correctly absent** | owner decision, then family or checker |
| 2.3 | Type names + `TAG_DEPTH_TIER_INT` | absent; 4 code paths no-op | owner decision: per-type or per-instance depth |
| 2.4 | Gate defaults | all 10 + warn ticked | T1–T2 on, rest off |
| 2.5 | Element-side ↔ family-side gates | **unconnected, and element side unbound** | binding fix (G-8) or accept family-side only |
| 2.6 | Family Category | **unverified** | operator reads it in Revit |

---

# Operator sheet

Everything in §A and §B is independent of the per-type/per-instance depth question and can be done
now. §C is **held** — do not start it.

## §A — Add the `HANDOVER_MODE_*` trio (closes 2.1)

The dual-wire design is live in code (`TagConfig.ResolveActivePatternMode`) but the family cannot
participate, because the three gate parameters are absent.

1. Open `STING_Tag_Universal.rfa`.
2. **Manage → Shared Parameters** → set the file to `data/MR_PARAMETERS.txt` from the deployed plugin
   folder. (Confirm the folder first: `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u`.)
3. **Family Types → New Parameter → Shared Parameter**, add all three:
   `HANDOVER_MODE_HANDOVER_BOOL`, `HANDOVER_MODE_DC_BOOL`, `HANDOVER_MODE_CUSTOM_BOOL`.
4. Group: **General**. Match the instance/type setting of the existing `TAG_PARA_STATE_*` gates —
   whatever §C resolves to, these must agree with them, because they appear in the same formula.
5. Set exactly one to Yes (`HANDOVER_MODE_DC_BOOL` is the normal default).

## §B — Per-row `and(...)` formulas (closes 2.1's second half)

Rows for tiers T4–T10 must gate on **both** the tier and the pattern mode, otherwise a Handover-only
row renders during Design & Construction.

- Form: `if(and(TAG_PARA_STATE_7_BOOL, HANDOVER_MODE_HANDOVER_BOOL), ASS_TAG_7E_TXT, "")`
- If a row is claimed by more than one mode, OR-merge rather than duplicating the row:
  `if(or(and(state7, modeA), and(state7, modeB)), PARAM, "")`
- `FamilyLabelAuthor.ApplyVisibilityFormulas` emits exactly these shapes, so authoring by hand and
  re-running the author later converge on the same text.

**Gate storage type matters.** STING stores these BOOLs as TEXT (`"Yes"`/`"No"`) in the v5.3+ default.
A TEXT gate is not a valid bare condition — it must be written `GATE = "Yes"`. `TagConfig.GateToken`
picks the right form automatically; if you hand-author, check the parameter's type first or Revit
raises "Inconsistent Units".

## The reload caveat — read before any of the above

Loading an edited family back offers **"Overwrite the existing version and its parameter values"**.
That discards project-set values for **every** parameter of the family, not just the ones you edited.

**Capture first:**
1. **Tag Studio → Style Audit**, export — records the current variant set per family.
2. Schedule the tag category with the 11 gates, `TAG_DEPTH_TIER_INT` and the style BOOLs as columns;
   export to Excel. This is the restore sheet.
3. Note the active presentation mode (**Presentation Mode → Report**) — the fastest global restore.

**Restore after reload:**
4. **Tag Studio → Presentation Mode** → the captured mode. Rewrites states 1–3 + warn project-wide.
5. Re-apply the **ViewStylePack** for tiers 4–10 and per-category depth, rather than hand-editing.
6. Reconcile against the Excel; only genuinely bespoke per-type values need manual re-entry.

**Do one family first and verify before committing to 206.**

## §C — Depth: per-type vs per-instance — **HELD, DO NOT START**

> **Status 2026-08-10: held pending owner re-decision.** The standing decision was to convert the
> eleven gates to INSTANCE. The evidence gathered since favours **leaving them per-TYPE** — see §2.5
> "What this changes" and the reasoning below. This section will be written once the owner
> re-decides; converting 206 families is destructive and must not start on the old assumption.

Why the evidence moved:

- The functional path already works. `TagStyleEngine.SetParagraphDepth` sweeps
  `WhereElementIsElementType()`, which **includes tag `FamilySymbol`s**, so it writes the gate on the
  tag type — the population the label formula actually reads.
- Per-tag depth variation already exists at type granularity. `TagStyleCatalogue.TypeVariantSpec`
  carries `DepthTier` and mints `2.5_BOLD_RED_Filled30_T3`; `TagStyleEngine.FindTypeVariant` selects
  it at placement. That is a working design, not a stub.
- Converting to INSTANCE costs: a destructive reload of 206 families, retirement of the T-variant
  catalogue, and **a new per-instance writer that does not exist today** — no code currently writes
  the gates to a tag instance.
- The gain is per-placed-tag depth, which the variant mechanism already provides per type.

## Family Category — the 30-second check (closes 2.6)

1. Open `STING_Tag_Universal.rfa`.
2. **Create → Family Category and Parameters** (or **Modify → Family Category and Parameters**).
3. Read the highlighted row. Close **without** changing it.

| Answer | What it implies |
|---|---|
| **Multi-Category Tags** | Cannot tag Rooms, Spaces or Areas — Revit forbids it regardless of parameters. Those three need their own tag families. Confirm whether Spaces and Areas are in scope for this project. |
| **A specific category** (e.g. Duct Tags) | The universal master is mis-categorised for its role; `PropagateUniversalTagCommand` recategorises clones, so this is survivable, but the master should be Multi-Category. |
| **Generic Annotations** | It is not a tag at all and cannot be assigned to a host — it would place as a symbol. This would be a blocking defect. |

**Either way, Rooms are already covered.** `STING - Room Tag.rfa` is present in the library as manifest
entry `tag-room` (category `Rooms`), so no new family is needed. It carries the old-set parameter
payload, so it needs the same §A/§B treatment as the universal tag if Rooms are to get tiered labels.
