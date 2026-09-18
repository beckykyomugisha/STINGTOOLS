# Propagating the label and correcting the category — the order that works

Two jobs on the same 206 files:

- **Propagate** the hand-built universal label into every tag family.
- **Recategorise** each family from `Generic Model Tags` to the category declared for it.

They write the same folder, one of them overwrites the other, and a deploy overwrites both.
This is the order that survives that, and the evidence for each rule.

Measured 2026-09-18 against the build deployed 08:26. Companion docs:
[the test](UNIVERSAL_TAG_PROPAGATION_TEST.md) · [parameter hygiene](UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md)

---

## 0 · Where the buttons are

Verified against `StingTools/UI/StingDockPanel.xaml` on 2026-09-18 (line numbers in
brackets). **The two halves of this job are on different tabs** — the label work is on
CREATE TAGS, the family work is on MODEL.

| Button | Tab → expander | Tag | Line |
|---|---|---|---|
| **Propagate Universal** | **CREATE TAGS** → *Advanced setup, schema & migration* | `Propagate_UniversalTag` | 2409 |
| Migrate Fams | CREATE TAGS → *Advanced setup, schema & migration* | `MigrateTagFamilies` | 2407 |
| Stamp Gates | CREATE TAGS → *Advanced setup, schema & migration* | `Gate_StampStatus` | 2410 |
| Status Register | CREATE TAGS → *Advanced setup, schema & migration* | `Status_Register` | 2411 |
| Purge (shared params) | CREATE TAGS → *Advanced setup, schema & migration* | `PurgeSharedParams` | 2391 |
| **Fix Categories** | **MODEL** → *Advanced family ops* | `TagFamilyFixCategories` | 2958 |
| Conformance | MODEL → *Advanced family ops* | `FamilyConformanceCheck` | 2956 |
| Swap Category (model families only — refuses tags) | MODEL → *Advanced family ops* | `FamilySwapCategory` | 2952 |
| Set depth (1-10) | TAGGING → *Advanced tag style engine* | `SetParagraphDepthExt` | 1344 |
| Depth | CREATE TAGS → *Manual token overrides* | `SetParagraphDepth` | 2497 |
| Set depth | TAG STUDIO → *Tokens & Depth* sub-tab | `SetParagraphDepth` | 4502 |
| Set depth | TAG STUDIO → *Tools* sub-tab → *More style ops* | `SetParagraphDepth` | 4806 |

Three buttons dispatch `SetParagraphDepth` and a fourth dispatches the 1-10 variant; any of
them does the job. **Purge Unused** is Revit's own (Manage → Purge Unused), not a STING
button — the `Purge` buttons above are a shared-parameter purge and a model compactor, which
are different things.

Tags are exact and case-sensitive, and they are what `StingCommandHandler` and
`WorkflowEngine.ResolveCommand` dispatch on, so a preset step uses the Tag column.

---

## 1 · What each operation actually touches

| | Propagate Universal | Fix Categories |
|---|---|---|
| Command tag | `Propagate_UniversalTag` | `TagFamilyFixCategories` |
| Needs a project | **Yes** — master + ≥1 target loaded | **No** — opens each `.rfa` standalone |
| Reads the declared category | Yes (`TagCategoryResolver`) | Yes (same resolver) |
| Writes the project | Yes — reloads the family | **Never** |
| Writes the `.rfa` on disk | **Yes** — `SaveAs` temp → `LoadFamily` → `File.Move` over the canonical file | **Yes** — `famDoc.Save()` in place |
| Folder written | `TagFamilyConfig.GetOutputDirectory()` | the same, by default |
| On this machine that is | `…\CompiledPlugin\data\TagFamilies\` | same |

**They write the same files.** That is the first collision.

---

## 2 · Three collisions, each proven

### 2.1 — Revit will not move a *loaded* family to another category

`LoadFamily` matches by name and refuses, returning a bare `false` with no failure message.

```
19:58, 21:02, 21:23   recategorising → load refused, no message
22:16                 category already correct → succeeded=1
```

**Consequence:** a project that already holds `STING - Duct Tag` as a Generic Model Tag keeps
it. Correcting the category on disk does not change that project; the family has to be
deleted from it and re-loaded, which takes **every placed tag of that family** with it. The
Fix Categories report prices this per family in its `PlacedInProject` column.

### 2.2 — Propagation writes the category it used, over the file you just fixed

Propagation ends with `File.Move(temp → …\data\TagFamilies\<name>.rfa)`. The clone carries
whichever category that run used:

- **ENFORCE** → the declared category, and the load is refused, so nothing is written.
- **KEEP** → the category the *project* currently has, which for a stale project is
  `Generic Model Tags` — and that is then written **over the corrected file**.

**Consequence:** propagating with KEEP against a stale project silently undoes a disk-side
category fix. Fix categories, adopt them into the project, *then* propagate.

### 2.3 — Every deploy overwrites `data/` from git

`extract_plugin.sh` does `cp -rf "$BUILD_DIR/data/"* "$DEPLOY_DIR/data/"`, and the build's
`data/` is the git-tracked `StingTools/Data/`. So a deploy replaces every `.rfa` in the
deployed tag library with the committed copy.

**This already happened.** The successful 22:16 propagation wrote
`…\data\TagFamilies\STING - Duct Tag.rfa`; three deploys later all three copies are
byte-identical:

```
ee26fecdff369070  StingTools/Data/TagFamilies/STING - Duct Tag.rfa      (git)
ee26fecdff369070  CompiledPlugin/data/TagFamilies/STING - Duct Tag.rfa  (deployed, 08:26)
ee26fecdff369070  …/STING - Duct Tag.rfa.backup-20260917                (pre-run copy)
```

**The propagated file is gone from disk.** It survives only inside the project that was open
at the time — and only if that project was saved.

**Consequence:** commit a result before the next deploy, or lose it. This is not a
theoretical risk; it is the state right now.

---

## 3 · The order

### Stage A — correct the categories on disk

1. **Fix Categories → AUDIT.** Folder defaults to the deployed `data\TagFamilies`.
   Read the report: `Family · Actual · Declared · Verdict · Detail · PlacedInProject`.
   - `ALREADY-CORRECT` — nothing to do.
   - `NO-DECLARATION` — no `Category:` line in `STING_TAG_CONFIG_v5_0_*.csv`. Add the
     declaration first; the command will not guess.
   - `UNRESOLVED` — declared a host category with no matching tag category in the document.
   - `FIX` — the work list.
2. **Fix Categories → APPLY → one family (Duct).** It copies the files into
   `_precategory_<timestamp>\` first and refuses to run if that copy fails.
3. **Open the corrected `.rfa` and count the label rows.** This is the step that matters:
   **a category change preserving 65 label rows is still unproven.** If the rows survive,
   continue; if they do not, stop and restore from `_precategory_…`.
4. **APPLY to the rest** once one family has proven the change is safe.

### Stage B — commit the corrected families immediately

```bash
cp "CompiledPlugin/data/TagFamilies/"*.rfa "StingTools/Data/TagFamilies/"
```

Then commit. **Do this before any redeploy** — §2.3. Skipping this stage is how the 22:16
result was lost.

### Stage C — get the corrected families into a project

A project that already holds the stale family cannot be reloaded into the new category
(§2.1). Pick one:

- **A fresh project** — load the corrected families. Nothing to conflict with.
- **An existing project** — delete each stale family from the project browser (losing its
  placed tags, count them in the audit report first), then load the corrected `.rfa`.

Verify: the family's category in the project is the declared one, not `Generic Model Tags`.

### Stage D — propagate the label

1. Load the universal master **and** the corrected targets.
2. **Propagate Universal** → master picker (press OK, do not click) → **CHOOSE** → OK.
3. **No category dialog should appear.** Its absence is the proof that Stage C worked — the
   dialog only shows when a target's category disagrees with its declaration.
   If it does appear, a target is still stale: go back to Stage C for that family.
4. Expect `succeeded=N, failed=0`, `Standard params added to each clone: ~138`,
   `Type variants (re)created: 12`.

Full verification list: [the test doc](UNIVERSAL_TAG_PROPAGATION_TEST.md) §4.

### Stage E — commit again, before redeploying

Propagation has just rewritten every `.rfa` it touched. Repeat Stage B. A redeploy before
this point discards the whole run.

### Stage F — Set depth, then check a drawing

Without `Set depth` no tier rows render, which looks exactly like propagation having failed.

---

## 4 · Why not the other order

**"Propagate first, then fix categories"** looks attractive — one project pass, then a disk
pass — and it fails on §2.2 only if you use KEEP. With ENFORCE the propagation is refused
outright. So the sequence is either:

- propagate KEEP (label lands, category still wrong, disk file written with the wrong
  category) → fix categories on disk (category right again) → **but the project still holds
  the stale-category family**, so you are back at Stage C anyway, and
- the label you just propagated is in the project's family, which you are about to delete.

Correcting the category first means the family is loaded once, correctly, and propagation
never has a category to change.

---

## 5 · Rollback

| Stage | Undo |
|---|---|
| A (categories on disk) | Copy back from `_precategory_<timestamp>\` |
| B / E (commits) | `git revert`, or restore the `.rfa` from git |
| C (deleted a family from a project) | Undo in Revit if the session is still open; otherwise the placed tags are gone — which is why the count is in the report |
| D (propagation) | Per-family `TransactionGroup`, rolled back on failure. For a bad on-disk result, `…\data\TagFamilies\STING - Duct Tag.rfa.backup-20260917` or git |

---

## 6 · The one experiment that could shorten all of this

`SwapCategoryCommand` uses `famDoc.LoadFamily(doc, options)` — the **in-memory** family
document overload — where propagation uses `doc.LoadFamily(path, options, out family)`.
Nobody has tried the in-memory overload on a tag whose category is changing.

If it permits the change, Stages A–C collapse: propagation could enforce the declared
category in-project in one pass, and the KEEP/ENFORCE choice disappears. If it refuses the
same way, this document is the route.

Ten minutes in Revit, and worth spending before automating anything further.
