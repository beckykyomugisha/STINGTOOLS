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

| File | Role | `TAG_PARA_STATE_*` / `TAG_WARN_VISIBLE_BOOL` |
|---|---|---|
| `MR_PARAMETERS.txt` | shared-parameter declarations | **present** — 10 gates + warn |
| `BINDING_COVERAGE_MATRIX.csv` | coverage *claim* | **marked in 47 categories** |
| `CATEGORY_BINDINGS.csv` | what `LoadSharedParamsCommand` actually binds | **ZERO rows** |
| `RESOLVED_BINDINGS.csv` | the spec-driven binder's input | **ABSENT** |

The 47-category figure in the brief is real, but it comes from the **coverage matrix**, which is a
report, not a binder input. Neither file the binders read carries these parameters at all.

**So `AnnotationRunner`'s write fails on every element** — `SetInt` returns `false` on an unbound
parameter and throws nothing. Since H-4 that is reported rather than swallowed, and this is precisely
the case `SafeWrite.Set` was built to surface: it will log
`'TAG_PARA_STATE_3_BOOL' is not bound to this element's category`.

This is the root of **G-8**: a coverage matrix asserting 47 categories while the binder input is
empty is the same "gate that never ran, so its clean baseline was trusted" shape as the standing
register finding.

### The 2-minute in-Revit confirmation

1. Open any project with the universal tag loaded. Place it on a **Duct**.
2. Select the duct. In Properties, look for `TAG_PARA_STATE_3_BOOL`.
   - **Not present** → confirms (b): the parameter is not bound to Ducts, and `AnnotationRunner` has
     nothing to write to. Expect the SafeWrite warning in `StingTools.log`.
   - **Present** → (b) is wrong on this model (someone bound it by hand); continue to 3.
3. Tick it. Observe the tag. If row 3 does not appear, (a) is confirmed: the element-side value has
   no path to family-side visibility.
4. Now open the tag's **Type Properties** and tick `TAG_PARA_STATE_3_BOOL` there. Row 3 should
   appear. That is the parameter that actually controls it.

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
