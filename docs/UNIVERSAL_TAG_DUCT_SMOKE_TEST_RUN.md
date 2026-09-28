# Duct smoke test — executable version, 2026-09-17

The gate before propagating to 206 families. Derived from
`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` with its stale paths corrected and the current state of
`STING_Tag_Universal.rfa` folded in.

> ⚠️ **This test has never been executed.** You are the first. Treat its own steps as
> unproven — if a step does not match what Revit shows you, that is worth reporting, not
> working around.

**Why it exists.** `Propagate_UniversalTag` clones your hand-built label into another family
by SaveAs → recategorise → rename → re-create type variants → LoadFamily. The premise is that
**recategorising preserves label rows**. If that is false, it is false for all 206, and you
find out once here instead of 206 times.

---

## P · Pre-conditions

- [ ] **P1 · Convert `TAG_PARA_STATE_2_BOOL` to Type first.**
      It is currently Instance-scoped (shows `(default)` in Family Types). `SetParagraphDepth`
      is a type sweep, so T2's six rows cannot be driven until this is Type. Propagating first
      gives all 206 families the same split.
      `Family Types → select → Modify (pencil) → Type → OK`, then **Save**.

- [ ] **P2 · No badge glyphs in the master.**
      In-tag badges are abandoned — a visibility formula cannot read the tagged element's
      parameters. If you built the six glyphs or the `vis_*` parameters, delete them now.
      Status is delivered by **Status Register**, not in the tag.

- [ ] **P3 · Confirm the deploy target.** The runner names `C:\Dev\STING_PLACEMENT_GOLD`,
      which is **retired**. Verified 2026-09-17, all three Revit versions point at:
      ```
      C:\Dev\wt-sting-live\CompiledPlugin\StingTools.dll
      ```
      Re-check rather than trust this line — it has moved five times:
      ```bash
      grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
      ```

- [ ] **P4 · BACK UP the target family. Do not skip.**
      Propagation **overwrites the `.rfa` on disk** in the running DLL's data dir:
      ```
      C:\Dev\wt-sting-live\CompiledPlugin\data\TagFamilies\STING - Duct Tag.rfa
      ```
      Copy it somewhere safe. This is your rollback.

- [ ] **P5 · Clear the obsolete seeds (optional, do it once).**
      `…\CompiledPlugin\data\TagFamilies\Seeds\` holds **137 obsolete families**. The repo
      deleted them and removed the probe that read them
      (`TagFamilyCreatorCommand.cs:1083` — *"A 'Seeds/' sub-folder is deliberately NOT
      probed"*), so they are **inert, not dangerous**. They survived because the 2026-09-17
      deploy copied without purging — which was right, it also preserved runtime logs. Delete
      the `Seeds\` folder to match the repo.

- [ ] **P6 · Open the test project and load TWO families.**
      `TENDO 3.rvt` is the sanctioned one. Load **the universal master** and **`STING - Duct
      Tag`**. **At least 2 STING annotation families must be loaded or the command aborts.**

---

## R · Run

- [ ] **R1.** TAG STUDIO → **Propagate Universal**
- [ ] **R2.** Master picker → select **the universal master**, *not* Duct.
      Picking Duct would clone Duct's label over itself and prove nothing.
- [ ] **R3.** Scope dialog → **CHOOSE families…** → confirm **only Duct** is ticked.
- [ ] **R4.** Confirm → watch the progress dialog to completion.
- [ ] **R5.** Read the done dialog. Expect **1 propagated, 0 failed**.
      Note `ParamsAdded`, `TypesCreated`, and the Excel report path.

      ℹ️ Your master currently has **no Family Types defined** (the Type name box is empty),
      so expect `TypesCreated > 0` — `TagTypeVariantWriter` mints the standard variants
      during propagation. `TypesCreated = 0` is worth questioning.

---

## V · Verify — this is the actual pass/fail

Open `STING - Duct Tag` via **Edit Family**.

- [ ] **V1 · Category.** Family category is **Duct Tags**, not Air Terminal Tags.
      Proves the recategorise step ran.

- [ ] **V2 · Label rows survived. ⭐ THE ONE THAT MATTERS.**
      Edit Label shows all **65 rows**, formulas/prefixes/suffixes/breaks intact. No dropped
      rows, no formula corruption.
      **This is the whole premise.** If recategorise does not preserve labels, the conveyor
      does not work and nothing downstream is worth doing.
      Spot-check specifically: row 1 `ASS_DISPLAY_TXT`, row 22 `ASS_CST_STALE_TXT`,
      row 50 `ASS_CRITICALITY_RATING_TXT`, row 65 Break ticked.

- [ ] **V3 · Tier toggle works.**
      On the Duct tag **type**, flip `TAG_PARA_STATE_4_BOOL` … `_10_BOOL` on and off. The
      matching tier rows must appear/collapse and **the label must reflow with no gaps or
      overlaps** — that is the point of the single-label design.
      Then flip `_2` (now Type, per P1) and confirm the six T2 rows respond.

- [ ] **V4 · Type variants present.**
      Family Types shows the depth/style/colour variants (canonical names like
      `2.5_BOLD_RED_Filled30_T3`). Spot-check one: its `TAG_PARA_STATE_*` bools and its single
      active `TAG_{size}{style}_{colour}_BOOL` must match its name.

- [ ] **V5 · Placement works.**
      Place the Duct tag on a real duct. It reads `ASS_TAG_1_TXT` and renders. No
      "could not load family", no broken-tag glyph.

- [ ] **V6 · Status Register.**
      Stamp Gates → **Status Register**. The Excel opens with the duct colour-coded
      green/amber/red on the Data and QA gate columns. **There is nothing to check inside the
      tag** — this is where status lives now.

- [ ] **V7 · On-disk persistence.**
      `…\CompiledPlugin\data\TagFamilies\STING - Duct Tag.rfa` was updated.
      ⚠️ It is **not** written back to the git-tracked `StingTools/Data/TagFamilies/`. Copy it
      back by hand if you want the result committed.

---

## Pass

**V1–V6 green.** Then, and only then:

1. Propagate Universal → **ALL** families (re-load the master + targets first).
2. **Persist the result — this is the step people skip.** Propagation writes to the running
   DLL's `data/TagFamilies/`, not to git.
   - Copy the propagated `.rfa` set into `StingTools/Data/TagFamilies/` and commit.
   - **Do NOT recreate `Seeds/`** — see P5.
   - Redeploy `data/TagFamilies/` to `C:\Dev\wt-sting-live\CompiledPlugin\data\TagFamilies`,
     Revit closed.
3. Run **Set depth** — without it no tier rows render, which looks exactly like propagation
   having failed.

---

## Fail triage

| Symptom | Meaning |
|---|---|
| **V2 — rows dropped or corrupted** | **STOP.** Recategorise does not preserve this family's label, so the conveyor premise is wrong. Capture *which* rows broke and report. Do not propagate to ALL. |
| **V4 — no or partial variants** | `TagTypeVariantWriter.CreateStandardVariants` or the arrowhead lookup. Check `ParamsAdded > 0` in the report — if 0, `AddMissingParams` did not run. |
| **Command reports FAILED** | Excel report's Error column, then `StingTools_yyyyMMdd.log` (`PropagateUniversalTag:` entries). **The log filename is date-stamped** — searching for `StingTools.log` finds nothing and reads as "it never logged". |
| **T2 rows never respond** | P1 was not done, or not saved before propagating. |

## Rollback

Project state is transactional — a `TransactionGroup` per family, rolled back on failure. If a
run leaves a bad on-disk `.rfa`, restore your **P4** backup into
`…\CompiledPlugin\data\TagFamilies\` and reload the family.
