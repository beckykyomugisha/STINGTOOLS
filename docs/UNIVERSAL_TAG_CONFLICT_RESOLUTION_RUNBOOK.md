# Resolving the 12 shared-parameter conflicts — runbook

For `STING_Tag_Universal.rfa`, which currently fails to load with
*"12 Errors (must be addressed in order to continue)"*.

**Order is not optional.** Two of the twelve are used by label rows; delete them before
swapping the rows and Revit will strip the rows out from under you. Do the label work
first, the deletions second.

Budget one sitting. ⚠️ **Parameters added to the Edit Label field list are lost if the
dialog closes before they are pushed into the label as rows.**

---

## 0 · Before you touch anything

**0.1 — Version the family.** The deletions in §3 cannot be undone once saved.

```
File → Save As → STING_Tag_Universal.0005.rfa
```

You already keep `.0002` / `.0003` / `.0004`; this is the same habit. Work in `.0005`,
and only rename it back to `STING_Tag_Universal.rfa` once §5 passes.

**0.2 — Refresh the build kit.** Copy over `C:\Dev\TAGS 210626\`:

| File | Why it changed |
|---|---|
| `UNIVERSAL_TAG_MASTER_PARAMS.txt` | the two conflicting params are replaced by their TEXT mirrors |
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` | rows 22 and 50 repointed; row 1 is `ASS_DISPLAY_TXT` |

The copies in that folder were byte-identical to the repo until 2026-09-17. They are not
any more — **use the new ones or you will re-add the parameters you are about to delete.**

**0.3 — Point Revit at the refreshed file.**

```
Manage → Shared Parameters → Browse → C:\Dev\TAGS 210626\UNIVERSAL_TAG_MASTER_PARAMS.txt
```

Without this the two mirror parameters are not in the browser and §2 stalls.

---

## 1 · Why the errors happen (read once, it makes the rest obvious)

Revit identifies a shared parameter by its **GUID**, not its name. Every one of the 12
errors is the same shape: the family offers a parameter as **Text**, the project already
holds that **same GUID** as Number / Currency / Length / Yes-No, and Revit refuses rather
than silently pick one.

The project is right. `MR_PARAMETERS.txt` — which the project was bound from — declares all
12 with their proper numeric types, and the GUIDs in the error list match it exactly.

So nothing here is fixed by editing the project. The **family** has to stop offering the
Text versions.

---

## 2 · Swap rows 22 and 50 onto the display mirrors — LABEL WORK FIRST

A numeric parameter cannot be retyped to Text for a label. The established route is a
`_TXT` **display mirror**: a real TEXT parameter carrying the formatted value. 24 rows of
this label already work that way, and both mirrors you need already exist and are bound.

| Row | was | now |
|---|---|---|
| 22 `Show T5 - Cost - Stale Flag` | `ASS_CST_STALE_BOOL` | **`ASS_CST_STALE_TXT`** |
| 50 `Show T8 - Coordination - …` | `ASS_CRITICALITY_RATING_NR` | **`ASS_CRITICALITY_RATING_TXT`** |

**Steps** — select the label, then **Edit Label**:

1. **Add both mirrors to the field list.** Add parameter (the ▸ icon) → Select → group
   **STINGTags_ISO19650** for `ASS_CST_STALE_TXT`, **ASS_MNG** for
   `ASS_CRITICALITY_RATING_TXT` → OK. One at a time; the browser resets to the first group
   on every reopen.
2. **Edit row 22 formula** — change `ASS_CST_STALE_BOOL` to `ASS_CST_STALE_TXT`:
   ```
   if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_TXT, "")
   ```
3. **Edit row 50 formula** — change `ASS_CRITICALITY_RATING_NR` to
   `ASS_CRITICALITY_RATING_TXT`:
   ```
   if(TAG_PARA_STATE_8_BOOL, ASS_CRITICALITY_RATING_TXT, "")
   ```
   Also rename the row so its name stops naming the old parameter:
   `Show T8 - Coordination - ASS_CRITICALITY_RATING_TXT`.
4. **While you are in here, close out the last two items from the label review:**
   - row **65** `Show T10-Ph179 - Trace Seq` → tick **Break**
   - row **1** `ASS_DISPLAY_TXT` → **Spaces = 0** (currently 1)
5. **OK** — push the rows. Do not Cancel; do not open another dialog first.

✅ **Gate:** the label still shows **65 rows**, and rows 22 and 50 name the `_TXT` params.

---

## 3 · Delete the 12 conflicting parameters — DESTRUCTIVE

```
Family Types → the parameter list → select → Delete (the red X / minus)
```

**Group A — 10 orphans.** In neither the label nor the kit. Inherited from the Air Terminal
master this family grew out of. Deleting a label ROW never deleted the family PARAMETER,
which is why three of these are rows you removed weeks ago.

```
PER_EMBODIED_ENERGY_MJ          HVC_DCT_SOUNDLVL_DB
HVC_DCT_FLW_CFM                 ASS_CST_UNIT_PRICE_UGX_NR
MNT_HGT_MM                      PER_RECYCLABILITY_PCT
PER_EXPECTED_LIFE_YEARS         PER_REPLACEMENT_COST_UGX
HVC_VEL_MPS                     ASS_ELEVATION_M
```

**Group B — the 2 you just freed in §2.** Only deletable now that no row references them.

```
ASS_CST_STALE_BOOL              ASS_CRITICALITY_RATING_NR
```

**This does not affect your projects.** You are removing them from one *family*. The
project keeps its own correctly-typed versions, and the plugin keeps writing
`ASS_CST_STALE_BOOL` on model elements exactly as before — `BOQCostManager` is untouched by
anything here.

⚠️ If Revit refuses a delete, something in the family still references it — a family
formula, a visibility parameter, or a label row you have not spotted. Find the reference
before forcing it.

✅ **Gate:** the Family Types parameter list contains none of the 12.

---

## 4 · Save and reload

```
Save → Load into Project (or Insert → Load Family)
```

---

## 5 · Verify — and do not accept a quiet success

| Check | Pass |
|---|---|
| The load dialog | **no** "Could Not Load Family", **no** error list |
| The placed tag | still renders; the T1 line shows a tag string |
| Row 22 `Stale:` | **blank — expected, see below** |
| Row 50 `Crit:` | shows a number once `ASS_CRITICALITY_RATING_NR` has a value |

**Row 22 will be blank, and that is known, not a new fault.** `ASS_CST_STALE_TXT` has no
formula yet. Its source is Yes/No, so it needs `if(ASS_CST_STALE_BOOL, "STALE", "")`, and
the text formula evaluator (`EvaluateTextLegacy`) supports only concatenation, quoted
literals, `format()` and bare parameter references — its last branch *silently skips* what
it cannot parse. Writing that formula today would produce a blank row indistinguishable
from a working one, so it was deliberately not written. Tracked as **MIRROR-2** in
`docs/ROADMAP.md`.

Row 50 works because its source is numeric and `format()` is supported; its formula shipped
in this change.

---

## 6 · Then, and only then

1. Rename `.0005` back to `STING_Tag_Universal.rfa`.
2. Run the **Duct smoke test** (`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`) on one family. The
   nested badge symbols surviving SaveAs + recategorise is the item most likely to break.
3. Only after that passes: **TAG STUDIO → Propagate Universal**.

Propagating before the smoke test spreads whatever is wrong to 206 families, and no gate
can see inside an `.rfa` to tell you afterwards.

---

## If it still fails

Read the new error list rather than assuming it is the same one.

- **Different parameter names** → more orphans of the same kind. Same treatment: confirm
  the name is in neither the label nor `UNIVERSAL_TAG_MASTER_PARAMS.txt`, then delete it.
- **The same names** → the family re-acquired them, almost always because Revit was still
  pointed at an old shared-parameter file. Redo §0.3 and check §3 actually took.
- **A name in the kit** → send it to me. `tools/check_shared_param_types.py` passes on all
  75 kit parameters today, so that would mean a source I have not audited.
