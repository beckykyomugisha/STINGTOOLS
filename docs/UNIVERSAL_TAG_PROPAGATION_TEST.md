# Universal tag propagation — the current test

**Supersedes** [`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`](UNIVERSAL_TAG_DUCT_SMOKE_TEST.md) (marked
NEVER RUN, retired deploy paths) and the `…_DUCT_SMOKE_TEST_RUN.md` drafted on
`claude/func-prod-binding-strategy-8ac840`. Written against the build deployed **2026-09-17
22:37**, after the first successful propagation on this machine.

**Doing the whole library, not just this test?** Read [UNIVERSAL_TAG_PROPAGATE_AND_RECATEGORISE.md](UNIVERSAL_TAG_PROPAGATE_AND_RECATEGORISE.md) first: propagation and the category fix write the same files, and a deploy overwrites both.

Read §0 before running anything: four of the things the old runner asks you to check by hand
are now either proven or checked by the plugin, and one step it never mentioned is the whole
remaining point.

---

## 0 · What is already settled, and what is not

| Claim | State | Evidence |
|---|---|---|
| The master's shared parameters can load into a project | **PROVEN** | `21:01:02 SharedParamPreflight: 'STING_Tag_Universal' offers 210 shared parameters; project holds 312` — zero conflicts, against a non-empty project side |
| Revit refuses a reload that changes a loaded family's category | **PROVEN** | Three runs recategorising → refused with no message (19:58, 21:02, 21:23). Category corrected by hand → `22:16:38 succeeded=1` |
| `Propagate_UniversalTag` can complete at all | **PROVEN, once** | `22:16:38 master=STING_Tag_Universal, succeeded=1, failed=0, params=139, types=14` |
| **Recategorising preserves the label rows** | ✅ **PROVEN, 2026-09-24** | Two duct tags drawn by `STING - Tie-In Point Tag (Duct — HVAC) Tag` — a PROPAGATED family, not the master — rendered their T6 carbon rows and their TAG7F row once the gate was bound to Ducts and ticked. The rows came through propagation intact. **The conveyor's founding premise now has evidence.** |
| The 206 tag families have no label rows of their own | **CONFIRMED in Revit, 2026-09-21** | They are empty shells. Every row comes from the master, through propagation — so V2 can only be answered on a family that has been PROPAGATED to, never on one that has only had its category corrected |
| Flipping a tier gate changes what the tag draws | **PROVEN, 2026-09-23** | Ticking `TAG_PARA_STATE_6_BOOL` on an air terminal's TYPE made the T6 rows draw; unticking it removed them. Same element, same tag |
| **Which copy of the gate the label reads** | **ANSWERED, 2026-09-23** | The **tagged element's type**. The tag type's copy was ON throughout and drew nothing. See [`…_FAMILY_PARAM_HYGIENE.md`](UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md) §4 |
| A gate must be BOUND to the tagged category before any of this works | **PROVEN, 2026-09-23** | `TAG_PARA_STATE_*` is bound to no model category by default. Unbound, the condition can never be true and every gated row returns `""` — see §1.7 |
| The result survives to disk and to git | **UNPROVEN** | §6 |

Everything in §1 that the plugin now does for you was a manual pre-condition in the old
runner. Do not redo it by hand.

---

## 1 · Pre-conditions

### 1.1 — Restart Revit

The 22:37 deploy happened while Revit was open. The copy completed (47 DLLs, hash-verified
against the build), but a running Revit is still executing the previous DLL.

**If you did not restart Revit after 22:37, nothing below tests what you think it does.**

### 1.2 — Confirm what Revit is loading

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

Expected today:

```
C:\Dev\STINGTOOLS\.claude\worktrees\relaxed-goodall-bd632a\CompiledPlugin\StingTools.dll
```

**This path has moved seven times in two days.** Trust the grep, not this line. Copying a
build into a folder that is not in the manifest succeeds silently and you debug code that
never ran.

### 1.3 — Know where the evidence lands

| Thing | Path |
|---|---|
| Log (ground truth) | `<CompiledPlugin>\StingTools_yyyyMMdd.log` — **date-stamped**; `StingTools.log` does not exist |
| Propagation writes the `.rfa` here | `<CompiledPlugin>\data\TagFamilies\` |
| Excel report | project container (`…\20_MISC_<CODE>\`) when the project is saved; `%LOCALAPPDATA%\STING\exports\` when it is not |
| Existing backup | `<CompiledPlugin>\data\TagFamilies\STING - Duct Tag.rfa.backup-20260917` |

### 1.4 — Save the test project

An unsaved project has no output folder, and several subsystems degrade quietly on it
(`ViewStylePackRegistry`, `LiveProfileSync`, `EmbeddedTemplates`). Save it; the report then
lands beside the model instead of in a per-user fallback.

### 1.5 — Open the project and load TWO families

The command aborts with fewer than two STING annotation families loaded. Load **the universal
master** and **`STING - Duct Tag`**.

### 1.6 — Close any family tab for a target

Revit will not load a family while a document for it is open, and it refuses by returning a
bare `false`. The command now checks this and aborts in a second, naming the file — but it is
faster to close the tab.

### 1.7 — Bind the tier gate to the tagged category  ⚠ NEW, and nothing works without it

`TAG_PARA_STATE_*_BOOL` ships bound to **no model category**. A label row gates on the
TAGGED ELEMENT's copy, so until the parameter exists there the condition can never be
true and every gated row returns `""` — one line on the drawing, whatever is ticked on
the tag type.

Cost 2026-09-23 four rounds of "nothing happened" before it was found. For the test, bind
one tier and no more:

1. **Manage → Shared Parameters → Browse** → `<CompiledPlugin>\data\MR_PARAMETERS.txt`
2. **Manage → Project Parameters → Add → Shared parameter → Select** → group
   `STINGTags_ISO19650` → `TAG_PARA_STATE_6_BOOL`
3. **Type**, not Instance. Categories: the one you are tagging.
4. On the element's **Edit Type**, tick it, **Apply**.
5. **Run Tag Doctor and confirm `gate@hostType` reads `ON`** before reading the drawing.
   A tick that did not hold and a tick nobody made look identical afterwards.

### What you no longer need to do by hand

- ~~Convert `TAG_PARA_STATE_2_BOOL` to Type~~ — `MakeVisibilityParamsType` converts every
  Instance-scoped gate in the clone and reports the count.
- ~~Check the twelve shared-parameter conflicts~~ — the pre-flight reads both sides and
  aborts with the names if any return.
- ~~Delete `TAG_PARA_STATE_3_BOOL` afterwards~~ — it is no longer added: the master decides
  which gates exist, and a reload replaces the family definition, so an existing `_3`
  disappears on the next run.
- ~~Back up the target~~ — done, see 1.3. Re-copy it if you have since overwritten it.

---

## 2 · Run

1. **CREATE TAGS tab → “Advanced setup, schema & migration” expander → Propagate Universal.**
   (Not TAG STUDIO — this doc said so until 2026-09-18 and it was wrong. Full button
   map: [UNIVERSAL_TAG_PROPAGATE_AND_RECATEGORISE.md §0](UNIVERSAL_TAG_PROPAGATE_AND_RECATEGORISE.md).)
2. **Master picker.** The universal master is pre-highlighted. **Press OK without clicking
   any row.** Clicking a highlighted row in a multi-select list *un*-highlights it.
   - Pick anything that does not look like a universal master and you get a challenge naming
     it, defaulting to No. That is deliberate: the wrong master overwrites 205 labels.
3. **Scope dialog → CHOOSE families…**
4. **Target picker.** Duct is pre-highlighted. **Press OK without clicking.** If you end up
   with nothing selected you now get a dialog explaining the toggle, not silence.
5. **Category dialog — only appears if a target's category disagrees with its declaration.**
   It should NOT appear for Duct any more (you corrected it, and the mismatch warning stopped
   at 21:21). If it does appear, choose **KEEP each family's current category** — `ENFORCE`
   is correct as an end state and is the thing Revit refuses.
6. **Authoring-note challenge — only appears if the master carries text like "delete this
   note before saving".** Delete the note in the master and re-run, or proceed knowingly:
   whatever is in the master is cloned into every target and prints.
7. **Confirmation** → OK. Watch the progress dialog. Escape cancels between families.
8. **Read the done dialog.** Expect:
   - `1 propagated, 0 failed`
   - `Standard params added to each clone: ~138` — one fewer than the 22:16 run's 139,
     because `_3` is no longer added
   - `Type variants (re)created: 12` — was 14; depth tier 3 was dropped from the catalogue
   - `Tier gates converted Instance → Type: N` — only if the master still has Instance gates
   - a report path

---

## 3 · The log lines that prove what happened

Read these before opening Revit's UI. `grep` the date-stamped log:

```bash
grep -E "PropagateUniversalTag|SharedParamPreflight|ChooseTargets|TagTypeVariantWriter" "/c/Dev/STINGTOOLS/.claude/worktrees/relaxed-goodall-bd632a/CompiledPlugin/StingTools_20260918.log"
```

**Expected, in order:**

```
RunCommand<PropagateUniversalTagCommand>: start
ChooseTargets: 1 target(s) chosen - STING - Duct Tag
SharedParamPreflight: 'STING_Tag_Universal' offers 210 shared parameters; project holds N
SharedParamPreflight: each clone will GAIN 138 standard parameter(s) …
PropagateUniversalTag: not adding TAG_PARA_STATE_3_BOOL - the master does not carry it …
TagCategoryResolver: 146 families with a declared category, from 10 config file(s)
TagTypeVariantWriter: N existing type(s) are not in the catalogue and were left alone: …_T3 …
PropagateUniversalTag: master=STING_Tag_Universal, succeeded=1, failed=0, params=138, types=12
RunCommand<PropagateUniversalTagCommand>: done
```

**`start` followed straight by `done` is now impossible** — every early exit logs its reason.
If you see one, the reason is on the line between them.

**Expected warnings that are NOT failures:**

```
TagTypeVariantWriter: arrowhead 'Arrow Filled 30' not present in project — skipped for …
… asks for depth 3 but the family's highest tier gate is 2 …      (only on a stale _T3 type)
```

---

## 4 · Verify — this is the actual pass/fail

Open the propagated `STING - Duct Tag` via **Edit Family**.

### V1 · Category
Family category is **Duct Tags**. Proves the recategorise step ran and stuck.

### V2 · ⭐ The label rows survived — ✅ PROVEN 2026-09-24

**Only meaningful on a family that has been propagated to.** The tag families carry no
label rows of their own (confirmed in Revit, 2026-09-21): they are empty shells, and every
row arrives from the master through this command. Opening one that has only had its
category corrected shows an empty label and proves nothing either way.

**The evidence.** With `TAG_PARA_STATE_6_BOOL` bound to **Ducts** (Type) and ticked on the
duct type, two tags drawn by `STING - Tie-In Point Tag (Duct — HVAC) Tag` — a **propagated**
family, not the master — rendered three lines each:

```
M-BLD1-Z01-L01-HVAC-SUP-DU-006
A1-A3:0kgCO2e A4:0kgCO2e B6:0kgCO2e/yr          <- the three T6 carbon rows
ISO 19650 tag M-BLD1-Z01-L01-HVAC-SUP-DU-0006   <- the T6 TAG7F row
```

Rows that only exist in the master were drawn by a family the master was cloned into. The
premise holds.

**Why the earlier runs did not settle this.** Every 2026-09-23 experiment ran on
`STING_Tag_Universal` — the master itself, which the air terminal happens to use. Adjacent
things working is not evidence for this one, and the row stayed UNPROVEN until a propagated
family drew a gated row.

**If you want the row count as well**, Edit Label on the propagated family and count: 71
rows, formulas, prefixes, suffixes and breaks intact. Spot-check row **1**
`ASS_DISPLAY_TXT`, row **22** `ASS_CST_STALE_TXT`, row **50** `ASS_CRITICALITY_RATING_TXT`.
Revit exposes no API for this, so it is an eyeball check or nothing.

### V3 · Which copy of a gate drives the label — ✅ ANSWERED 2026-09-23

**The tagged element's type.** Nothing to run.

| `TAG_PARA_STATE_6_BOOL` on the air terminal's TYPE | The tag |
|---|---|
| unticked | one line |
| **ticked** | one line **+ the three T6 carbon rows** |

The tag type's copy read `ON` throughout and drew nothing, so it is inert for rendering.
Recorded in [`…_FAMILY_PARAM_HYGIENE.md`](UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md) §4.

**The steps this section used to give would have failed**, and not for the reason they were
testing: flipping the gate "on the duct type" assumes the parameter is there, and it is bound
to no model category until you do §1.7.

**Consequence worth knowing:** depth is a property of the ELEMENT, not of the tag. A duct
cannot carry a T2 tag on the coordination sheet and a T10 tag on handover. The `_T1`/`_T2`
type variants are real types, but their gate values never reach the label.

### V4 · Gates are Type-scoped and only `_1`/`_2` are on — ⚠ CURRENTLY FAILS
In Family Types, the gate rows should read:

- `TAG_PARA_STATE_1_BOOL` ✓ · `_2_BOOL` ✓ · `_4`…`_10` clear · `TAG_WARN_VISIBLE_BOOL` ✓
- **no `TAG_PARA_STATE_3_BOOL` row at all**
- **no `(default)` suffix on any of them** — that suffix means Instance, and `Set depth`
  writes to types

**Measured 2026-09-23 on `STING_Tag_Universal`: all ten gates read `1 (on)` while
`TAG_DEPTH_TIER_INT` read `2`.** A state that should not be expressible.

The cause is two writers over one set of flags. `TagTypeVariantWriter` sets
`1..DepthTier` per variant — the design. `Set depth` then sets every carrier type to the
GLOBAL depth, overwriting it. Running Set depth at 10 turns every variant into a T10.

The T3 half of this passes: Tag Doctor confirms the gate is absent from the family.

This is what the single-integer migration removes — one value cannot disagree with itself.
See [`UNIVERSAL_TAG_LABEL_INTEGER_MIGRATION.md`](UNIVERSAL_TAG_LABEL_INTEGER_MIGRATION.md).

### V4b · The depth INTEGER drives the label — ✅ PROVEN 2026-09-24, both directions

After migrating the 70 formulas to `if(TAG_DEPTH_TIER_INT > N-1, …, "")` and binding the
parameter Type-scoped, one air terminal type was walked through five values:

| `TAG_DEPTH_TIER_INT` | What the tag drew |
|---|---|
| **1** | the ISO tag, one line |
| **2** | + mark, status, and the T2 narrative |
| **3** | identical to 2 — see below |
| **5** | + cost, payment, performance, carbon rows |
| **6** | + the T6 OmniClass / TAG7F line |

**Both directions were checked.** Raising the value added rows and lowering it removed them.
That matters: a row that always draws is as wrong as one that never does, and only the
downward half catches it. Most of this feature's history is failures that looked like
successes because nobody ran the test the other way.

**Depth 2 and 3 are identical, and that is correct.** T3 owns **zero** label rows — counted
from the generated migration sheet, not assumed:

```
T1  >0    0 rows      (row 1 is an ungated plain parameter)
T2  >1    9 rows
T3  >2    0 rows      <- nothing to draw
T4  >3    7 rows
T5  >4   22 rows
T6  >5    7 rows      T7..T10  6,6,6,7 rows
```

T3 was dropped from the master, so `depth_tiers` still accepting 1-10 is `Set depth`'s
vocabulary, not a promise that every value looks different. Already listed in §7. To make
depth 3 distinct: label rows first, then the variants.

### V5 · Type variants
Family Types should list the **12** catalogue variants, named `_T1` or `_T2` only:

```
2_NOM_BLACK_None_T1              2.5_BOLD_GREEN_ArrowFilled30_T2
2_NOM_BLACK_None_T2              2.5_BOLD_RED_ArrowFilled30_T2
2.5_NOM_BLACK_ArrowFilled30_T2   2.5_NOM_BLACK_ArrowOpen30_T2
2.5_BOLD_BLUE_ArrowFilled30_T2   2_ITALIC_PURPLE_DotFilled_T2
2.5_BOLD_ORANGE_ArrowFilled30_T2 3_NOM_BLACK_None_T1
3_BOLD_BLACK_ArrowFilled30_T2    3.5_BOLD_BLACK_ArrowFilled30_T2
```

Any `…_T3` type you see is **residue** from before the catalogue change — the writer leaves
existing types alone and names them in the log. Purge Unused removes the ones with no tags on
them. Spot-check one variant: its single active `TAG_{size}{style}_{colour}_BOOL` and its
`TAG_DEPTH_TIER_INT` must match its name.

### V6 · Placement
Place the tag on a duct. It reads `ASS_TAG_1_TXT` and renders. No "could not load family", no
broken-tag glyph.

### V7 · Status register
**CREATE TAGS → “Advanced setup, schema & migration” → Stamp Gates**, then **Status Register** (same expander). The duct appears colour-coded on the Data and QA gate
columns. **There is nothing to check inside the tag** — in-tag status badges are abandoned
(a visibility formula cannot read the tagged element's parameters).

### V8 · On-disk persistence
`<CompiledPlugin>\data\TagFamilies\STING - Duct Tag.rfa` has a new timestamp. It is **not**
written back to the git-tracked `StingTools/Data/TagFamilies/` — copy it by hand if you want
the result committed.

---

## 5 · Fail triage

Every row here is a message the build actually produces. If you see something not on this
list, that is worth reporting rather than working around.

| What you see | What it means |
|---|---|
| `LoadFamily … failed: '<name>' is open in the Family Editor (<path>)` | Close that tab and re-run |
| `LoadFamily … failed: this run recategorised the family from 'X' to 'Y', and Revit will not change a loaded family's category by reloading over it` | Re-run and choose **KEEP**, or delete the family from the project first — the message counts the placed instances that would be lost |
| `LoadFamily … failed: the clone document could not be closed` | `Document.Close` returned false; something in the session still holds it |
| `LoadFamily … failed, and Revit reported no failure message. No document for this family is open either` | Genuinely unexplained. Three of the four known causes are now ruled out by the message itself — capture the log and report it |
| `N shared parameters block the load: …` before anything runs | The twelve are back, or new ones. Fix in the FAMILY, not the project — see [the runbook](UNIVERSAL_TAG_CONFLICT_RESOLUTION_RUNBOOK.md) |
| **V2 — rows dropped or corrupted** | **STOP.** The conveyor premise is wrong. Capture *which* rows broke. Do not propagate to ALL |
| `Set FamilyCategory failed: …` | The declared category is not valid for an annotation family in this document |
| Escape pressed | `cancelled=N` in the summary; families already done are done |

---

## 6 · After a pass

In this order:

1. **Record V2 and V3 results** in this document and in `…_FAMILY_PARAM_HYGIENE.md` §4.
2. **Propagate to ALL** — re-load the master and the targets, then Propagate Universal →
   ALL. Expect a category dialog this time: most of the 206 are mis-categorised, and
   `ENFORCE` will be refused for any family already loaded under another category. Take
   **KEEP** for the first full pass and fix categories as a separate exercise.
3. **Persist the result.** Propagation writes to the running DLL's `data/TagFamilies/`, not to
   git. Copy the `.rfa` set into `StingTools/Data/TagFamilies/` and commit. **Do not recreate
   `Seeds/`** — the 137 families there are inert and untracked.
4. **Purge the `_T3` residue** (Purge Unused) once you are satisfied nothing is placed on
   those types.
5. **Run Set depth.** Without it no tier rows render, which looks exactly like propagation
   having failed.

---

## 7 · Known gaps that are not failures

| Gap | Why it is not a failure |
|---|---|
| 10-of-14 (now fewer) arrowhead warnings | `Arrow Filled 30`, `Arrow Open 30`, `Dot Filled` do not exist as arrowhead types in this project. Those variants keep their default arrowhead. Create them once in the template, or change the catalogue to name arrowheads that exist |
| `…_T3` types in already-propagated families | Residue from the catalogue change. Named in the log, left alone on purpose: deleting a type takes any tag placed on it with it |
| Depth 3 renders like depth 2 | The label has no T3 rows. `depth_tiers` still accepts 1-10 because that is `Set depth`'s vocabulary. To re-add T3: label rows first, then the gate, then the variants |
| `Migrate Tag Families` (CREATE TAGS → *Advanced setup*) re-adds `TAG_PARA_STATE_3_BOOL` | It injects the full standard set and has no master to align to. **Propagation is the last step, not Migrate** |
| The master carries ~72 element-data parameters | Hygiene, not a blocker — see `…_FAMILY_PARAM_HYGIENE.md` §2. They are why the family offers 210 shared parameters against a ~138 design |

---

## Rollback

Project state is transactional — one `TransactionGroup` per family, rolled back on failure. If
a run leaves a bad `.rfa` on disk, restore
`<CompiledPlugin>\data\TagFamilies\STING - Duct Tag.rfa.backup-20260917` and reload the family.

---

## Result log

Fill this in. An empty row is honest; a missing row is not.

| Date | Build | V1 | V2 (row count) | V3 (which copy) | V4 | V5 | V6 | V7 | V8 | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-09-17 | 22:16 deploy | — | not checked | — | — | 14 types (`_T3` era) | — | — | — | `succeeded=1, params=139, types=14`. First successful run; verification not performed |
| 2026-09-23 | 21:08 deploy | — | **still not checked** — every run used the MASTER family, not a propagated one | ✅ **tagged element's type** | ❌ all ten gates on at depth 2 | placed type is `STING_Tag_Universal`, not a catalogue variant | ✅ renders | — | — | Gate must be BOUND to the tagged category first (§1.7) — four rounds lost to that. Master holds 71 label rows, all present |
| 2026-09-24 | tier-gate-scope | — | ✅ **PROVEN** — a propagated duct family drew its T6 rows | ✅ tagged element's type | ❌ (fix in flight) | not checked | ✅ renders | not checked | not checked | Gate bound to Ducts + ticked. Carbon rows read 0 — real parameters, no data |
| | | | | | | | | | | |
