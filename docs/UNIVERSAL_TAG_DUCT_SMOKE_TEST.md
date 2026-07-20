# Universal Tag — Duct smoke test (the gate before scaling to all 206)

**Purpose.** Prove `Propagate_UniversalTag` in real Revit on ONE family (Duct) before
propagating to all 206 and before any Task-4 legacy teardown. Everything downstream is
gated on this passing. Nothing here has been exercised in Revit yet — Tasks 1–3 are
"built, wired, unproven".

**Owner.** This is a human-in-Revit test (the master label can only be built by hand;
the API cannot author label rows). Claude cannot drive Revit reliably on this machine
(two-monitor click bleed).

**Related docs.** `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` (how to build the master) ·
`docs/ROADMAP.md` (staged cutover) · `docs/CHANGELOG.md` Phase 195.

---

## Pre-conditions (do these first)

- [ ] **P0. Build + deploy the current plugin.** `dotnet build StingTools/StingTools.csproj -c Release`
      then copy the DLLs + `data/` to the deploy target (`C:\Dev\STING_PLACEMENT_GOLD`, close
      Revit first). Confirm the "Propagate Universal", "Stamp Gates", "Tag Schedules"
      buttons appear on the CREATE tab.
- [ ] **P1. Build the universal master label BY HAND** on one source family (Air Terminal is
      the historical master), following `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — all 65 rows,
      Type=Text, Spaces=0, breaks per the table, tier gating on `TAG_PARA_STATE_n_BOOL`.
      **Do NOT build status-badge glyphs** — in-tag badges are abandoned (Revit tag formulas
      can't read the tagged element's params). Status is delivered by the **Status Register**
      (`Status_Register`, colour-coded Excel), not in the tag. Also **remove any size BOOLs /
      size-named Family Types** from the master before saving — size lives in the SaveAs variant
      families, not in a BOOL.

  > **Base size.** Set the master label to the **2.5mm** Label text-type so the default SaveAs
  > copy needs no size change. The 8 size variants are cut *from this master AFTER it passes* —
  > do not SaveAs the 8 until V1–V6 below are green, or you'll re-cut them after any fix.
- [ ] **P2. Save the master** and give it an unambiguous name (e.g. `STING - UNIVERSAL Tag`)
      so it is easy to pick in the master picker.
- [ ] **P3. Open a test project** (TENDO 3.rvt is the sanctioned test project) and **load two
      families**: the universal master, and the ONE target — a Duct tag family
      (`STING - Duct Tag`). At least 2 STING annotation families must be loaded or the
      command aborts.
- [ ] **P4. Back up** the git-tracked `StingTools/Data/TagFamilies/STING - Duct Tag.rfa`
      (propagation overwrites the on-disk `.rfa` in the running DLL's `data/TagFamilies`).

---

## Run

- [ ] **R1.** CREATE tab → **Propagate Universal**.
- [ ] **R2.** Master picker → select the universal master (**not** Duct).
- [ ] **R3.** Scope dialog → **CHOOSE families…** (Duct is pre-ticked). Confirm only Duct is ticked.
- [ ] **R4.** Confirmation dialog → **OK**. Watch the progress dialog to completion.
- [ ] **R5.** Read the done dialog: expect **1 propagated, 0 failed**. Note ParamsAdded /
      TypesCreated counts and the Excel report path.

---

## Verify in Revit (the actual pass/fail criteria)

Open `STING - Duct Tag` in the Family Editor (Edit Family) and check each:

- [ ] **V1 — Category.** Family category is **Duct Tags** (not Air Terminal Tags). The clone
      was recategorised.
- [ ] **V2 — Label rows survived.** Edit Label shows all **65 universal rows** with correct
      formulas, prefixes, suffixes, breaks. No rows dropped, no formula corruption. This is
      the single most important check — it proves recategorise preserves labels.
- [ ] **V3 — Tier toggle works.** On a placed Duct tag instance (or a family type), flip
      `TAG_PARA_STATE_4_BOOL` … `_10_BOOL` on/off and confirm the corresponding tier rows
      appear/collapse and the label **reflows** with no gaps/overlaps (the whole point of the
      single-label design).
- [ ] **V4 — Type variants present.** Family Types shows the standard depth/style/colour
      variants (canonical names like `2.5_BOLD_RED_Filled30_T3`), re-created by
      `TagTypeVariantWriter`. Spot-check one: its `TAG_PARA_STATE_1..depth` bools and the
      single active `TAG_{size}{style}_{colour}_BOOL` match the name.
- [ ] **V5 — Placement works.** Place the Duct tag on a real duct in a view; it reads
      `ASS_TAG_1_TXT` and renders. No "can't load family" / broken-tag errors.
- [ ] **V6 — Status Register (replaces in-tag badges).** Stamp Gates → **Status Register**
      (`Status_Register`) on the test project; confirm the Excel opens with the Duct element(s)
      colour-coded green/amber/red on the Data + QA gate columns. This is where status lives now
      — there is nothing to verify *inside* the tag.
- [ ] **V7 — On-disk persistence.** Confirm `data/TagFamilies/STING - Duct Tag.rfa` (in the
      running DLL's data dir) was updated. **Note:** it is NOT auto-written back to the
      git-tracked `StingTools/Data/TagFamilies/` — copy it back manually if you want to commit
      the propagated result.

---

## Pass / fail

**PASS** = V1–V6 all green. Then, and only then:
1. Re-run Propagate Universal → **ALL** families (after re-loading the master + all targets).
2. **Persist the propagated `.rfa` files — do not skip this.** Propagation writes to the running
   DLL's `data/TagFamilies/`, NOT to git. So after the ALL run:
   a. Copy the propagated `.rfa` set into the git-tracked `StingTools/Data/TagFamilies/` and
      commit them (this is what the deploy + git actually ship).
   b. **Repopulate the seed folder** `StingTools/Data/TagFamilies/Seeds/` with the propagated,
      universal-label families (overwrite the old bespoke seeds). `TagFamilyCreatorCommand.FindSeedFamily`
      loads these as the "gold standard" for fresh `Create Tag Families` runs, so stale seeds would
      re-introduce the old scheme. Match the per-category file names `FindSeedFamily` expects
      (category base name, optionally `_seed.rfa`).
   c. Redeploy: copy the updated `data/TagFamilies/` (incl. `Seeds/`) to the deploy target
      (`C:\Dev\STING_PLACEMENT_GOLD\data\TagFamilies`, Revit closed).
3. Begin the Task-4 staged cutover (`docs/ROADMAP.md` step 2) — apply
   `UNIVERSAL_TAG_TASK4_STEP2_PATCH.md`.

**FAIL** triage:
- **V2 fails (rows dropped/corrupted)** → recategorise does NOT preserve this family's label;
  the whole conveyor premise is wrong for Duct. Stop. Capture which rows broke; re-open the
  memory `project-tag-family-label-tiers` and reconsider.
- **V4 fails (no/partial variants)** → issue in `TagTypeVariantWriter.CreateStandardVariants`
  or the arrowhead lookup; the master's params may be missing the STATE/style bools. Check
  `AddMissingParams` ran (ParamsAdded > 0 in the report).
- **Command reports FAILED** → read the Excel report's Error column + `StingTools.log`
  (`PropagateUniversalTag:` entries). The atomic SaveAs→LoadFamily→move leaves the target's
  existing family untouched on any failure, so the project is safe to re-run.

## Rollback

The project state is transactional (TransactionGroup per family, rolled back on failure).
If a run leaves a bad on-disk `.rfa`, restore the backup from **P4** into
`data/TagFamilies/` and reload the family.
