# Universal Tag — EDIT DELTA (change the existing master, do not rebuild)

**Purpose: the smallest set of edits that turns your current Air Terminal master into the
65-row universal label.** Nothing here is a from-scratch build. The full 65-row table lives in
`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`; this page is only what changes.

Measured 2026-09-17 against `UNIVERSAL_TAG_MASTER_BUILD.xlsx` and the build sheet.

---

## A · Alignment status of the source documents

| Source | Rows | Agrees? |
|---|---|---|
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Label Rows* | 65 | ✅ |
| `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` → Step 2 | 65 | ✅ param-for-param |
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Parameters* | ~~74~~ **75** | ⚠️ `ASS_DISPLAY_TXT` was missing, added 2026-09-17 |
| `UNIVERSAL_TAG_MASTER_PARAMS.txt` | ~~74~~ **75** | ⚠️ same — the kit could not build row 1 |
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Delete first* | 14 | ✅ |
| `UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` → Part 1 | 14 | ✅ |
| ~~`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` → Step 1~~ | ~~9~~ → **14** | ⚠️ **was wrong, corrected 2026-09-17** |

All 65 rows match between the workbook and the markdown, parameter for parameter. The single
reported difference is row 1, where the xlsx names `ASS_TAG_1_TXT` and the markdown shows `-`
— presentation only, because T1 is added directly rather than as a calculated value.

**So: aligned, with one fix applied.** Step 1 of the build sheet listed 9 removals and omitted
the 5 warning-text rows. The 65-row target contains no `WARN_*` rows, so 14 is correct; 9
would have left you on 70 rows.

---

## A2 · Row 1 is `ASS_DISPLAY_TXT` — the build kit was wrong, the master is right

Found 2026-09-17 by reviewing a screenshot of the live master against the kit.

`ASS_DISPLAY_TXT` is the ON-DRAWING tag: the display-mode + segment-mask resolved rendering
of the canonical `ASS_TAG_1_TXT` (`TagConfig.cs:2727`, written by `BuildDisplayTag`). Putting
`ASS_TAG_1_TXT` on the label bypasses `STING_DISPLAY_MODE` and every mask
(`TAG_SEG_MASK_TXT` / `STING_VIEW_TOKEN_MASK_TXT` / the UI "TokenMask"), so every tag prints
all eight segments in every view regardless of the mask.

The error ran through the whole kit, and all three are now fixed:

| Artifact | Was | Now |
|---|---|---|
| `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` row 1 | `ASS_TAG_1_TXT` | `ASS_DISPLAY_TXT` |
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Label Rows* row 1 | `ASS_TAG_1_TXT` | `ASS_DISPLAY_TXT` |
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Parameters* | absent | added (75) |
| `UNIVERSAL_TAG_MASTER_PARAMS.txt` | absent | added, group 17, GUID `6954e197-0524-5620-a2bb-aea7f274475a` |

**The kit could not have produced a correct row 1**: `ASS_DISPLAY_TXT` was in neither the
Parameters sheet nor the trimmed shared-parameter file, so it would never have appeared in
the Edit Label field list. Anyone rebuilding from the kit would have shipped the downgrade to
all 206 families via propagation.

`ASS_TAG_1_TXT` is left in both files. It is no longer used by any row, but it is the
canonical key and is harmless in the field list.

⚠️ **Your working copy at `C:\Dev\TAGS 210626\` is now stale** — it was byte-identical to
the repo copy before these edits. Re-copy both files from `docs/` before using them.

---

## B · What changed since the previous universal tag

Two changes, both already in the table above.

### B1 · 62 → 65 rows (`2accee34a`, 2026-07-06) — three T7 rows restored

T7 went from 3 rows to 6. **Nothing was removed.** Everything else in that commit was
whitespace normalisation in the Prefix column.

| Add to T7 | Calc Value Name | Formula | Prefix | Break |
|---|---|---|---|---|
| `ASS_SPOOL_NR_TXT` | Show T7 - Fabrication - Spool No | `if(TAG_PARA_STATE_7_BOOL, ASS_SPOOL_NR_TXT, "")` | `Spool:` | no |
| `ASS_FAB_STATUS_TXT` | Show T7 - Fabrication - Status | `if(TAG_PARA_STATE_7_BOOL, ASS_FAB_STATUS_TXT, "")` | `Fab:` | no |
| `ASS_QC_INSPECTOR_TXT` | Show T7 - QC - Inspector | `if(TAG_PARA_STATE_7_BOOL, ASS_QC_INSPECTOR_TXT, "")` | `QC:` | YES |

These sit at rows 41–43, **before** the three Installation rows that were already there.

### B2 · Badge formula corrected (`eb8484fbd`, 2026-09-07) — edit, do not add

If you already built badges, **edit the six visibility formulas**. If you have not, build them
the new way.

| | |
|---|---|
| ❌ was | `(TAG_WARN_VISIBLE_BOOL = "Yes")` |
| ✅ now | `TAG_WARN_VISIBLE_BOOL` — **bare** |

`TAG_WARN_VISIBLE_BOOL` is YESNO. Comparing it to a string fails with Revit's *Inconsistent
Units*. The old wording came from a stale claim in `LABEL_DEFINITIONS.json`, corrected in
`614aba59b`.

### B3 · `ASS_STATUS_TXT` — check, may already be there

Flagged in the build sheet as *"add it if your current master doesn't have it"*. It is present
in **both** the 62-row and 65-row tables, so the flag is about **your master**, not about the
document revision. Row 4, `Show T2 - Status`, prefix `Status:`, directly after
`ASS_DESCRIPTION_TXT`.

---

## C · The edit list

### C1 · DELETE — 14 rows

Select the row in Edit Label → left-arrow. Exact as shipped in the *Delete first* sheet.

| # | Row name | Parameter | Why |
|---|---|---|---|
| 1 | Show Tier 2 - 4 | `HVC_DCT_FLW_CFM` | discipline data → schedule |
| 2 | Show Tier 2 - 5 | `HVC_VEL_MPS` | discipline data → schedule |
| 3 | Show Tier 2 - 6 | `MNT_HGT_MM` | discipline data → schedule |
| 4 | Show Tier 3 - 8 | `HVC_TAG_7_PARA_AT_TXT` | T3 dropped entirely |
| 5 | Show Tier 3 - 9 | `HVC_DCT_TERMINAL_TYPE_SD_RG_EG_VAV_TXT` | T3 dropped entirely |
| 6 | Show Tier 3 - 10 | `HVC_DCT_TERMINAL_SZ_TXT` | T3 dropped entirely |
| 7 | Show Tier 3 - 11 | `HVC_TERMINAL_MAT_TXT` | T3 dropped entirely |
| 8 | Show Tier 3 - 12 | `HVC_TERMINAL_FINISH_TXT` | T3 dropped entirely |
| 9 | Show Tier 3 - 13 | `ASS_CST_TOTAL_UGX_NR` | T3 dropped entirely |
| 10 | ⚠ Warning: Air Terminal Noise | `WARN_HVC_NOISE_NC_AIR_TERMINALS` | → badge |
| 11 | ⚠ Warning: Air Terminal Airflow Capacity | `WARN_HVC_AIRFLOW_CAPACITY_AIR_TERMINALS` | → badge |
| 12 | ⚠ Warning: Air Terminal Throw Distance | `WARN_HVC_THROW_DISTANCE_AIR_TERMINALS` | → badge |
| 13 | ⚠ Warning: Air Terminal Pressure Drop | `WARN_HVC_PRESSURE_DROP_AIR_TERMINALS` | → badge |
| 14 | ⚠ Warning: Air Terminal Mounting Height | `WARN_HVC_MOUNTING_HEIGHT_AIR_TERMINALS` | → badge |

**The general rule, for any row not listed:** delete it if its parameter is T3, carries a
discipline prefix (`HVC_` / `PLM_` / `ELC_` / `MNT_`), or is a `WARN_*`.

### C2 · ADD — known gaps

| Row | Parameter | Confidence |
|---|---|---|
| 41 Show T7 - Fabrication - Spool No | `ASS_SPOOL_NR_TXT` | **Add** if your master predates 2026-07-06 |
| 42 Show T7 - Fabrication - Status | `ASS_FAB_STATUS_TXT` | same |
| 43 Show T7 - QC - Inspector | `ASS_QC_INSPECTOR_TXT` | same |
| 4 Show T2 - Status | `ASS_STATUS_TXT` | **Check** — flagged as possibly absent |

### C3 · EDIT — in place, no add/delete

| What | Change |
|---|---|
| 6 badge visibility formulas | `(TAG_WARN_VISIBLE_BOOL = "Yes")` → `TAG_WARN_VISIBLE_BOOL` bare |

### C4 · VERIFY — everything else

**I do not know your master's current contents**, so C2 is the *known* gap, not a guaranteed
complete one. After the deletes, walk the 65-row table once against the label, top to bottom,
ticking the *Built?* column in the xlsx. Arithmetic check:

```
rows now  -  14 deleted  +  N added  =  65
```

If it does not reconcile to 65, stop and find the discrepancy rather than adding rows until
the count matches — an extra row with a plausible name is exactly what nobody spots later.

---

## D · Per-tier target (use as the running checksum)

| T1 | T2 | T4 | T5 | T6 | T7 | T8 | T9 | T10 | total |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 6 | 6 | 21 | 6 | 6 | 6 | 6 | 7 | **65** |

There is no T3, and the numbering is **not** closed up. `TAG_PARA_STATE_3_BOOL` still ships and
is bound across 47 categories; renumbering T4→T3 would invalidate that parameter and every
value stored against it in every existing family. The gap is cheaper than the rename.

T7 is the tier to double-check — it is the one that changed, 3 → 6.
