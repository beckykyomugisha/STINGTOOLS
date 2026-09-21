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
| **Recategorising preserves the 65 label rows** | **UNPROVEN** | The premise the whole conveyor rests on. No run has yet been followed by a row count. **This is V2, and it is why you are running this test.** |
| The 206 tag families have no label rows of their own | **CONFIRMED in Revit, 2026-09-21** | They are empty shells. Every row comes from the master, through propagation — so V2 can only be answered on a family that has been PROPAGATED to, never on one that has only had its category corrected |
| Flipping a tier gate changes what the tag draws | **UNPROVEN** | Both the tag type and the tagged element carry the gates. §4 V3 decides which one wins |
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

### V2 · ⭐ The label rows survived — THE ONE THAT MATTERS

**Only meaningful on a family that has been propagated to.** The tag families carry no
label rows of their own (confirmed in Revit, 2026-09-21): they are empty shells, and every
row arrives from the master through this command. Opening one that has only had its
category corrected shows an empty label and proves nothing either way.

**Edit Label. Count the rows.**

- **65 rows**, formulas, prefixes, suffixes and breaks intact.
- Spot-check: row **1** `ASS_DISPLAY_TXT` (Spaces 0), row **22** `ASS_CST_STALE_TXT`,
  row **50** `ASS_CRITICALITY_RATING_TXT`, row **65** Break ticked.

**This is the whole premise.** If recategorising does not preserve this label, it does not
preserve it for any of the 206 and nothing downstream is worth doing. Record the number you
actually see, not the number you expected.

### V3 · Which copy of a gate drives the label — the open question
Both the tag type and the tagged element carry `TAG_PARA_STATE_*`. Nobody has established
which one the label reads, and it changes what `Set depth` is for.

1. Place the duct tag on a real duct. Note which tier rows are drawn.
2. Flip `TAG_PARA_STATE_7_BOOL` **on the tag type**. Do the T7 rows vanish?
3. Flip it back. Flip it **on the duct type** instead. Do they vanish now?

**Record the answer in [`UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md`](UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md) §4.**
Either outcome is information; only leaving it unanswered is a problem.

### V4 · Gates are Type-scoped and only `_1`/`_2` are on
In Family Types, the gate rows should read:

- `TAG_PARA_STATE_1_BOOL` ✓ · `_2_BOOL` ✓ · `_4`…`_10` clear · `TAG_WARN_VISIBLE_BOOL` ✓
- **no `TAG_PARA_STATE_3_BOOL` row at all**
- **no `(default)` suffix on any of them** — that suffix means Instance, and `Set depth`
  writes to types

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
| | | | | | | | | | | |
