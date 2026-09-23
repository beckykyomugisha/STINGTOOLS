# Which parameters belong in the universal tag — and how the gates must be scoped

Answering, from the Family Types screenshot of 2026-09-17: *which rows should be deleted
before propagating, and should every `TAG_PARA_STATE_*_BOOL` be ticked by default?*

Measured against `main` @ `114e38244`: `MR_PARAMETERS.csv`, `TagFamilyConfig`,
`ParagraphDepthCommand`, and the pre-flight line from the 21:21 run.

---

## The short answer

| Question | Answer |
|---|---|
| Which rows to delete | Every **non-`TAG_*`** shared parameter. A tag reads element data from the *tagged element*, not from its own family parameters. ~72 of the master's 210 are of this kind. |
| Should all the gates be ticked | **No — tick `_1` and `_2` only.** Depth is cumulative and `Set depth` overwrites all ten on every run, so the ticks only decide what a freshly placed tag shows before anyone runs it. All ten ticked means a 65-row tag on first placement. |
| The finding neither question asked about | **`_1`, `_2` and `TAG_WARN_VISIBLE_BOOL` are Instance-scoped; `_4`–`_10` are Type.** `Set depth` writes to element **types**, so those three tiers cannot be driven at all. |

---

## 1 · The scope split is the real defect

From the screenshot, reading Revit's `(default)` suffix as "instance parameter":

| Parameter | Scope in the master | `MR_PARAMETERS.csv` declares |
|---|---|---|
| `TAG_PARA_STATE_1_BOOL` | **Instance** | Type |
| `TAG_PARA_STATE_2_BOOL` | **Instance** | Type |
| `TAG_PARA_STATE_3_BOOL` | **absent** | Type |
| `TAG_PARA_STATE_4_BOOL` … `_10_BOOL` | Type | Type |
| `TAG_WARN_VISIBLE_BOOL` | **Instance** | Type |

`SetParagraphDepthCommand` sweeps `ElementType`s — *"These are Type parameters — changes
apply to every instance sharing the type"* — and writes all ten gates on each carrier. An
Instance-scoped gate is therefore **present, scored by the conformance audit, and
undrivable**: `Set depth` writes nothing to it, and the tier looks broken for a reason no
audit was reporting.

**It would have propagated.** `AddMissingParams` only adds what a family does not already
have, so an existing Instance-scoped gate is skipped and stays Instance — in all 206
families. Two changes close that:

- `PropagateUniversalTagCommand.MakeVisibilityParamsType` converts any Instance-scoped gate
  in the clone to Type via `FamilyManager.MakeType`, counts it, and reports the count in the
  done dialog.
- The conformance audit now checks the **scope** of each gate, not just its presence, and
  warns when one is Instance.

`_3` being absent is not a defect: the 65-row label has **no T3 rows** (T1/T2, then T4
onward), so nothing gates on it. It is worth knowing that **depth 3 does nothing** on this
master even though `ParagraphDepthCommand` documents it as "Comprehensive". Propagation adds
`_3` as a Type parameter to every clone, so the clones will carry a gate with no rows.

---

## 2 · Which rows to delete

**The rule:** a tag family needs `TAG_*` parameters and nothing else.

- **Keep** — `TAG_PARA_STATE_1..10_BOOL`, `TAG_WARN_VISIBLE_BOOL`, the 128
  `TAG_{size}{style}_{colour}_BOOL` style matrix, and the box/leader parameters
  (`TAG_BOX_*`, `TAG_LEADER_*`). That is the 138 `TagFamilyConfig.StyleParams +
  VisibilityParams` set that propagation adds and the style engine drives.
- **Delete** — every parameter that describes the *tagged element*: `MEP_*`, `MNT_*`,
  `PRJ_*`, `RGL_*`, `STR_*`, `PER_*`, `HVC_*`, `ELC_*`, `PLM_*`, `ASS_*`, `CST_*`,
  `WARN_<discipline>_*`.

**Why they are safe to delete.** A tag's label fields come from the **tagged category's**
parameters — that is what the left-hand pane of Edit Label lists. The family's own copy of
`MNT_FREQUENCY_TXT` is not what row N prints; the duct's copy is. The family copy does two
things, both bad:

1. it is offered to the project on load, which is where the twelve type conflicts came from;
2. it makes the family offer **210 shared parameters** (the pre-flight line from the 21:21
   run) where ~138 is the designed footprint.

**Why the deletion is safe to attempt.** Revit refuses to delete a parameter that a label
row or a formula still references. So the refusal *is* the gate: delete in batches, and
anything Revit protects is something to look at rather than force. Confirm with the label
row count afterwards — it must still be 65.

**Two to check before deleting.** `MNT_TYPE_TXT` and `WARN_HVC_DCT_SOUNDLVL_DB` are greyed
out in the screenshot, and greying means something else owns the row. Select each and open
Modify (the pencil) to see what it is before touching it.

---

## 3 · Should every gate be ticked?

**No. `_1` and `_2` ticked; `_3`–`_10` clear.** Reasons, in order of weight:

1. **All ten ticked means depth 10 on first placement** — all 65 rows print. A tag like that
   is unusable on a drawing and reads as a broken family to anyone who places one.
2. **The ticks are not the control.** `Set depth` writes all ten gates on every carrier each
   time it runs, so whatever the family ships with is overwritten the first time anyone sets
   a depth. The ticks only decide the out-of-box state.
3. **Depth 2 is the documented "Standard" mode** — T1 identity plus T2 dimensions, materials,
   thermal, acoustic. That is a sensible tag; T5 cost and T8 clash data are not what you want
   printing by default.
4. `TAG_WARN_VISIBLE_BOOL` **ticked** is right: a warning that has to be switched on is a
   warning nobody sees.

This recommendation holds whichever way the open question in §4 falls, which is why it is
worth doing now.

---

## 4 · ANSWERED (2026-09-23) — the TAGGED ELEMENT's type copy wins

**Which copy of a gate does a label row read: the tag type's, or the tagged
element's?**

**The tagged element's type.** Measured in Revit on a live air terminal:

| `TAG_PARA_STATE_6_BOOL` on the air terminal's TYPE | The tag |
|---|---|
| unticked | one line |
| **ticked** | one line **+ the three T6 carbon rows** |

Same element, same tag, nothing else changed. The gate was simultaneously **ON at
the tag type** throughout — so the tag type's copy is inert for rendering.

An integer comparison behaves identically:
`if(TAG_DEPTH_TIER_INT > 5, ASS_TAG_7F_TXT, "")` with the value 6 drew its row.

### This reverses the answer recorded here earlier the same day

The first version of this section concluded *"the tag type's copy wins, because it
is the only copy."* That was **inferred**, not measured — from the gates reading
`ON` at the tag type and `-` at the host while nothing drew. The inference was
backwards: nothing drew *because* the host had no copy, and the tag-type copy
that was `ON` never mattered.

The premise was wrong too. It claimed tiers 4–10 have no model-category binding
spec anywhere, so a host copy was impossible. `PARAMETER_CATEGORIES.csv` does
cover only tiers 1–3 — but a spec is not a binding and its absence is not a
prohibition. Binding tier 6 by hand took two minutes and worked.

**The lesson is the one this file keeps relearning:** four causes of a blank row
look identical on a drawing, and reasoning from which of them *seems* most likely
produced a confident wrong answer twice in one day. The experiment that settled
it was a single tick.

### Consequences

- **The gate must be bound to the tagged element, Type-scoped.** Nothing else
  makes a tier draw.
- **Per-drawing depth is not achievable** through tag type variants. A duct cannot
  carry a T2 tag on the coordination sheet and a T10 tag on handover — depth is a
  property of the element, not of the tag. `TagTypeVariantWriter`'s depth tiers
  are real type variants, but their gate values do not reach the label.
- **`Set depth`'s writes to tag types change nothing on a drawing.** It now also
  writes `TAG_DEPTH_TIER_INT` on element types, which does.
- **One integer beats ten booleans**, now that binding is required either way: one
  binding instead of ten, "all ten on at depth 2" becomes unrepresentable, and
  tier 11 costs nothing. The 70 formulas are in
  [`UNIVERSAL_TAG_LABEL_INTEGER_MIGRATION.md`](UNIVERSAL_TAG_LABEL_INTEGER_MIGRATION.md).

### Two Revit rules found on the way

1. **No `>=` operator.** Revit answers `Operator not expected: =`. Tier N is
   written `> N-1`.
2. **An unset integer reads as 0**, so a gated row stays hidden until
   `TAG_DEPTH_TIER_INT` has a value — the correct default.

---

## 4b · Why a duct tag showed one line — measured 2026-09-23

Not the label rows. **All 71 are present and correct in the master.** Row 1 is a
plain parameter; rows 2–71 are calculated values, each gated on
`if(TAG_PARA_STATE_n_BOOL, …, "")`.

Exactly one line drew — row 1, the only ungated row. Every gated row returned `""`
because the gate was not bound to the tagged element, so the condition could never
be true.

The sequence that proved it, each step a positive readout rather than an absence:

| Row 71's formula | Result |
|---|---|
| `if(TAG_PARA_STATE_6_BOOL, ASS_TAG_7F_TXT, "")` | blank |
| `ASS_TAG_7F_TXT` | **drew** — so the row and the value are fine |
| `"HELLO"` | **drew** — so the reload reaches the drawing |
| gate restored, **then bound + ticked on the host type** | **drew** |

The third line matters as much as the fourth. Two rounds of "nothing happened"
could have been a formula that fails, a gate that is false, or a change that never
reached the drawing. `"HELLO"` ruled out the third, and only then did the silences
become evidence.

### Two data defects this uncovered

- `ASS_TAG_7F_TXT` holds an **OmniClass classification sentence**, not the carbon
  narrative its row name promises — and it duplicates the ISO tag already on line 1.
- The three carbon rows draw **`A1-A3:0kgCO2e A4:0kgCO2e B6:0kgCO2e/yr`** — real
  parameters with no data behind them.

Both were invisible while nothing drew, and neither is a label problem.

---

## 5 · Why the propagated family differs from the master

Measured 2026-09-17 from the first successful propagation (`22:16:38 — succeeded=1,
failed=0, params=139, types=14`) by comparing the two Family Types lists.

Every difference is the design, not a fault. Two mechanisms produce all of them:

**`AddMissingParams` adds the standard set, it does not copy the master's.** Propagation
adds every name in `TagFamilyConfig.StyleParams + VisibilityParams` that the clone does not
already carry — 139 of them on that run — taking each definition from
`MR_PARAMETERS.txt`. `VisibilityParams` includes `TAG_PARA_STATE_3_BOOL`, and `StyleParams`
includes `TAG_DEPTH_TIER_INT` and `TAG_SCALE_TIER_AUTO_BOOL`. So:

> **`TAG_PARA_STATE_3_BOOL` appears in the propagated family because the standard set
> declares it, not because it was inherited.** The master not having it is the anomaly; the
> clone is the canonical shape.

That is deliberate — propagation enforces the standard rather than preserving whatever the
master happens to hold — and it is now *stated*: the pre-flight logs how many standard
parameters each clone will gain and names the gates among them, and the done dialog reads
"Standard params added to each clone" rather than the ambiguous "Params added".

**`TagTypeVariantWriter` sets the gates per type variant.** The ticks are not inherited
either. For a variant whose spec has `DepthTier = N` it writes `PARA_STATE_1..N = Yes`, the
rest `No`, and `TAG_DEPTH_TIER_INT = N`. The type on screen was
`3.5_BOLD_BLACK_Filled30_T3`, hence gates 1-3 ticked, 4-10 clear, depth 3. Selecting a
`_T2` variant would show a different pattern in the same family.

The master has **no** types at all (empty Type name box), so it has no variant to set its
gates, which is why its own ticks are whatever was last set by hand.

### Two findings that came out of the comparison

1. **A `_T3` variant shows the same rows as `_T2`.** The 65-row label has no T3 rows, so
   `TAG_PARA_STATE_3_BOOL` gates nothing and `TAG_DEPTH_TIER_INT = 3` overstates what is
   visible. Either add T3 rows to the master (a content decision — T3 is documented as
   "regulatory, sustainability, QA") or stop the catalogue minting `_T3` variants. Until one
   of those happens, depth 3 and depth 2 are the same drawing.
2. **10 of the 14 type variants got no arrowhead.** The 22:16 run logged
   *"arrowhead 'Arrow Filled 30' not present in project — skipped"* ten times, plus
   `'Arrow Open 30'` and `'Dot Filled'`. The arrowhead element types the catalogue names do
   not exist in this project, so those variants carry whatever arrowhead they default to.
   They need creating once in the project template (Manage → Additional Settings →
   Arrowheads), or the catalogue needs to name arrowheads that exist.

---

## 6 · Aligning the propagated family with the master

Asked 2026-09-17: make propagation produce what is actually wanted — the master's parameter
set, gates Type-scoped, only `_1` and `_2` ticked, and no `TAG_PARA_STATE_3_BOOL`.

All four now follow from one rule and one conversion.

### The rule: the master decides which tier gates exist

`AddMissingParams` added all eleven of `TagFamilyConfig.VisibilityParams` to every clone
regardless of the master. It now adds only the gates **the master carries**, and logs the
ones it skipped:

```
PropagateUniversalTag: not adding TAG_PARA_STATE_3_BOOL - the master does not
carry it, so the clones will not either
```

The style matrix is deliberately **not** filtered this way. The master expresses style
through type variants rather than the 128 `TAG_{size}{style}_{colour}_BOOL` switches, so the
clones have to be given the matrix or their variants switch nothing. "Align with the master"
therefore means the gates, not the whole set — which is also why a propagated family
legitimately has ~139 parameters the master does not.

### What each of the four asks resolves to

| Ask | How it is met |
|---|---|
| Parameters align with the master | Gates filtered to the master's set (above). Style matrix still supplied, by design. |
| Instance → Type | `MakeVisibilityParamsType` converts any Instance-scoped gate in the clone via `FamilyManager.MakeType`, logs each one and reports the count. |
| Only `_1` and `_2` ticked | Follows automatically. `TagTypeVariantWriter` sets `PARA_STATE_1..N = Yes` for a variant of depth N, the catalogue only mints `_T2` and `_T3` variants, and with `_3` absent every variant lands on exactly `_1` + `_2`. |
| `_3` deleted | A reload **replaces** the family definition, so the already-propagated duct tag loses `_3` on the next run. Nothing has to delete it by hand. |

### Two things to know

**A `_T3` variant now renders as depth 2**, because the gate it needs is gone. The writer
says so rather than clamping silently:

```
TagTypeVariantWriter: 3.5_BOLD_BLACK_Filled30_T3 asks for depth 3 but the family's
highest tier gate is 2 (TAG_PARA_STATE_3_BOOL is absent), so it renders as depth 2
```

Two variants that look like a choice and are not. Resolve it by adding T3 rows to the label
or by dropping `_T3` from the catalogue — a decision about the label, not about the writer,
which is why it is a warning and not a fix.

**`Migrate Tag Families` will re-add `_3`.** It injects the full
`StyleParams + VisibilityParams` set (`MigrateTagFamiliesCommand.cs:213`) and has no master
to align to, so a Migrate run after a propagation puts the gate back. That is defensible for
what Migrate is for — bringing an arbitrary family up to the standard set — but it means
**propagation is the last step, not Migrate**.

---

## 7 · Depth tier 3 dropped from the variant catalogue

Decided 2026-09-17, resolving the choice left open in §6: **drop `_T3`** rather than add T3
rows to the label.

`tag_style_catalogue.json` offered 14 standard variants, 8 of them at depth 3. With no T3
rows in the 65-row label and no `TAG_PARA_STATE_3_BOOL` in the master, each `_T3` variant
drew exactly what its `_T2` sibling drew while reporting `TAG_DEPTH_TIER_INT = 3`.

Every depth-3 variant was **retargeted to depth 2, keeping its size, style, colour and
arrowhead** — so nothing was removed from the offering, including the disciplinary colours.
Two rows became duplicates of an existing depth-2 row and were dropped: 14 variants → **12**,
across the same 11 distinct style combinations.

```
2_NOM_BLACK_None_T1              2.5_BOLD_GREEN_ArrowFilled30_T2
2_NOM_BLACK_None_T2              2.5_BOLD_RED_ArrowFilled30_T2
2.5_NOM_BLACK_ArrowFilled30_T2   2.5_NOM_BLACK_ArrowOpen30_T2
2.5_BOLD_BLUE_ArrowFilled30_T2   2_ITALIC_PURPLE_DotFilled_T2
2.5_BOLD_ORANGE_ArrowFilled30_T2 3_NOM_BLACK_None_T1
3_BOLD_BLACK_ArrowFilled30_T2    3.5_BOLD_BLACK_ArrowFilled30_T2
```

Also moved off depth 3: the five `defaults_per_discipline` entries that used it (M, E, P, S,
FP), the `DepthTier` default on both `DisciplineDefault` and `TypeVariantSpec`, and the
`?? 3` fallback used when a catalogue row omits `depth_tier`. A code default of 3 would have
quietly re-created the problem for any caller that did not name a tier.

**`depth_tiers` in the catalogue still lists 1-10, deliberately.** That is the vocabulary
`Set depth` accepts, not the variant set; depth 3 remains a legal depth that simply renders
like 2 until the label has T3 rows.

### The residue

`TagTypeVariantWriter` only ever adds types, so families already propagated keep their
`_T3` types alongside the new `_T2` ones — which is the very "two types that look like a
choice" this change removes. It now names them rather than deleting them:

```
TagTypeVariantWriter: 3 existing type(s) are not in the catalogue and were left
alone: 2_NOM_BLACK_None_T3, 2.5_NOM_BLACK_ArrowFilled30_T3, … Purge Unused
removes any with no tags placed on them.
```

Deleting a type takes any tag placed on it with it, so that stays the operator's call.

**To re-add T3 later**, add the T3 rows to the master's label first, then
`TAG_PARA_STATE_3_BOOL` to the master, then restore the depth-3 rows here — in that order.
The gate before the rows produces exactly the state this section removed.

---

## 8 · Fixing the families' categories in code — what is possible

Asked 2026-09-18: most of the 206 are `Generic Model Tags` rather than the category they
were written for. Can that be corrected by code?

**Yes for the library, no for a project that already holds the stale family.** The two
halves need different answers because they fail for different reasons.

### What already worked, and what never did

| Step | State |
|---|---|
| Set a family's category **inside a family document** — `famDoc.OwnerFamily.FamilyCategory = cat` | **Works.** Three places in the tree already do it (`PropagateUniversalTagCommand`, `FamilyHostConverter`, `SymbolLibraryCreator`), and the Revit UI does it too — you did it to the duct tag by hand |
| Get that family **back into a project that has it loaded** | **Refused.** Revit matches a reloaded family by NAME and will not move a loaded family to another category. `LoadFamily` returns a bare `false` (proven three times, 2026-09-17) |
| `FamilySwapCategory`, the existing command for this | **Never covered tags.** `SwapCategoryCommand` refuses annotation families by design — "System families, hosted (doors / windows) and annotation families are refused" |

### The route that works: fix the `.rfa` on disk

`Fix Categories` (**MODEL** tab → *Advanced family ops* expander; `TagFamilyFixCategories` →
[`FixTagFamilyCategoriesCommand`](../StingTools/Commands/TagStudio/FixTagFamilyCategoriesCommand.cs))
opens each family **standalone** with `app.OpenDocumentFile` — no project, no `LoadFamily`,
so there is nothing to refuse — sets the category declared for it in
`STING_TAG_CONFIG_v5_0_*.csv`, reads the value back, and saves the file. A project that
loads the corrected file afterwards gets the right category from the start.

It never guesses. A family with no declared category is reported `NO-DECLARATION` and
skipped; a declaration that resolves to no category in the document is `UNRESOLVED`. Both
are in the report rather than corrected on a hunch.

**Audit is the default and writes nothing.** Apply asks which files, copies every one into a
timestamped `_precategory_…` folder first, refuses to run at all if that copy fails, and
pre-selects Duct — because **whether a category change preserves a family's label rows is
still unproven** (§V2 of the test doc) and this command is otherwise a way to find that out
206 times at once.

### The part that cannot be automated, and why the report names it

An existing model keeps its `Generic Model Tags` version until the family is deleted from it
and re-loaded, which takes every placed tag with it. So the report carries a
**`PlacedInProject`** column: how many elements in the open project use that family's types.
0 means adoption is free; a number means it costs that many tags. That decision is not one a
command should make silently, so it is priced, not paid.

### Untried, and worth one experiment

`SwapCategoryCommand` uses a **different** load overload from propagation:
`famDoc.LoadFamily(doc, options)` — the in-memory family document — where propagation uses
`doc.LoadFamily(path, options, out family)`. Whether the in-memory overload also refuses a
category change is unknown; nobody has tried it on a tag. If it permits one, propagation
could enforce the declared category in-project after all, and §6's KEEP/ENFORCE choice
becomes unnecessary.

That is a ten-minute test in Revit and it would be worth doing before building anything else
on the disk-side route.
