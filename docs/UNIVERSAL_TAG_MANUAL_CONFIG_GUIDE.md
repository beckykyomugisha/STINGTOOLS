# Universal STING Tag — Manual Configuration Guide

**One label. 65 rows. Discipline-agnostic. Built ONCE by hand, then propagated to all 206.**

This is the master walkthrough for hand-building the universal tag label in the Revit Family
Editor. It supersedes the per-family bespoke-tier authoring. Sections:

- **Part 0 — SETUP** — load the params and get all 74 into the Edit Label field list.
- **Part 1 — DELETE** the discipline-specific + T3 + warning-text rows from your current master.
- **Part 2 — BUILD** the 65 universal rows (with the exact per-row calc-value procedure).
- **Part 3 — WARNING / STATUS SYMBOLS** — the two visual status badges that replace the old
  text warning rows.
- **Part 4 — COMPLETION CHECKLIST** — what "done" looks like before the smoke test.

### Build kit (files in `docs/`)
| File | Use |
|---|---|
| `UNIVERSAL_TAG_MASTER_BUILD.xlsx` | **The working spreadsheet.** Sheet *Label Rows* = all 65 rows (Name/Formula/Prefix/Suffix/Spaces/Break/param) with a **Built?** column to tick as you go; *Parameters* = the exact 74 params (type/group/GUID/role); *Badges* = the 6 visibility formulas; *Delete first* = the removal list. Work from this. |
| `UNIVERSAL_TAG_MASTER_PARAMS.txt` | **Revit shared-parameter file with ONLY the 74 master params.** Load it as the active shared-param file so the Add-parameter browser shows only what you need (fast add). GUIDs are identical to `MR_PARAMETERS.txt`. |
| `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` | The canonical 65-row table (source of the xlsx). |
| `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` | The verify gate to run when the master is done. |

Why this changed: the API can't author label rows, cross-category paste is blocked, and every
family's bespoke tier structure is unique — so a single bespoke master can't be propagated. A
*discipline-agnostic* label CAN (recategorise preserves rows). Discipline engineering data moves
to per-category **schedules** (`Tag Schedules` button); per-item warnings move to the two
**status badges** (Part 3).

---

## Part 0 — Setup: load params + populate the Edit Label field list

A parameter can only be used in a calc value if it is already in the tag's **Edit Label field
list** (Family-Types binding does NOT surface it there). Do this first so Part 2 never stalls on
a "not a valid parameter" error.

1. **Pick the pilot family.** Air Terminal is the sanctioned first master (do NOT start with
   Electrical Equipment — it's the longest). Open it in the Family Editor in a test project
   (TENDO 3.rvt).
2. **Load the master param file.** Manage → Shared Parameters → Browse → select
   `docs/UNIVERSAL_TAG_MASTER_PARAMS.txt`. This file holds exactly the 74 params (13 groups) —
   nothing else — so the browser is short.
3. **Open Edit Label** on the tag's label element (or create one label if none: Create → Label →
   place it).
4. **Add every param from the *Parameters* sheet to the field list:** Edit Label → **Add
   parameter** (the ▸ icon) → Select → pick group → pick param → OK → OK. Repeat for all 74.
   - Multi-select does **not** work — one at a time.
   - The Shared-Parameters browser **resets to the first group on every reopen**, so re-pick the
     group each time.
   - Tick them off in the xlsx *Parameters* sheet as you add them.
5. Leave Edit Label **open** — go straight to Part 1/2. **Params added to the field list are lost
   on close unless pushed to the label table as rows**, so never close Edit Label until the rows
   are built.

---

## Part 1 — Lines / parameters to DELETE

The universal label keeps only the generic tiers. From your **current Air Terminal master**,
remove these **14 label rows** (select the row in Edit Label → left-arrow to remove from label):

### 1a. T2 discipline rows to delete (3)
| Row name | Parameter |
|---|---|
| Show Tier 2 - 4 | `HVC_DCT_FLW_CFM` |
| Show Tier 2 - 5 | `HVC_VEL_MPS` |
| Show Tier 2 - 6 | `MNT_HGT_MM` |

### 1b. ALL T3 rows to delete (6) — T3 is entirely dropped
| Row name | Parameter |
|---|---|
| Show Tier 3 - 8 | `HVC_TAG_7_PARA_AT_TXT` |
| Show Tier 3 - 9 | `HVC_DCT_TERMINAL_TYPE_SD_RG_EG_VAV_TXT` |
| Show Tier 3 - 10 | `HVC_DCT_TERMINAL_SZ_TXT` |
| Show Tier 3 - 11 | `HVC_TERMINAL_MAT_TXT` |
| Show Tier 3 - 12 | `HVC_TERMINAL_FINISH_TXT` |
| Show Tier 3 - 13 | `ASS_CST_TOTAL_UGX_NR` |

### 1c. ALL warning TEXT rows to delete (5) — replaced by the Part 3 badges
| Row name | Parameter |
|---|---|
| ⚠ Warning: Air Terminal Noise | `WARN_HVC_NOISE_NC_AIR_TERMINALS` |
| ⚠ Warning: Air Terminal Airflow Capacity | `WARN_HVC_AIRFLOW_CAPACITY_AIR_TERMINALS` |
| ⚠ Warning: Air Terminal Throw Distance | `WARN_HVC_THROW_DISTANCE_AIR_TERMINALS` |
| ⚠ Warning: Air Terminal Pressure Drop | `WARN_HVC_PRESSURE_DROP_AIR_TERMINALS` |
| ⚠ Warning: Air Terminal Mounting Height | `WARN_HVC_MOUNTING_HEIGHT_AIR_TERMINALS` |

> If you built any T4–T10 rows using a **discipline-specific** parameter, delete those too —
> the universal label uses only the generic asset parameters in the Part 2 table.

### The general DELETE rule (for any starting family)
Delete a row if its parameter is **any** of:
- **T3** (every T3 row, no exceptions — T3 was the per-family engineering block);
- a **discipline prefix**: `HVC_*`, `PLM_*`, `ELC_*`, `MNT_*`, or any `WARN_*_<FAMILY>`;
- a **warning** (`WARN_*`, gated by `TAG_WARN_VISIBLE_BOOL`).

Keep everything that matches the Part 2 table below.

### Family parameters (optional cleanup)
Removing a **row** is what matters — it stops the value rendering. The underlying family
parameters left behind are harmless dead weight. You may optionally purge them later
(Manage → Family Types → delete unused params, or Purge Unused) once the label is final. Do
**not** delete `TAG_PARA_STATE_1..10_BOOL`, `TAG_WARN_VISIBLE_BOOL`, `STING_GATE_*_STATUS_INT`,
the style/visibility params, or any Part 2 / Part 3 parameter.

---

## Part 2 — Build the 65 universal rows

Work from the **`UNIVERSAL_TAG_MASTER_BUILD.xlsx` → *Label Rows* sheet** and tick the **Built?**
column per row. Build rows **in order (2 → 65)**.

**Row 1** = `ASS_TAG_1_TXT` added directly (drag the parameter into the label, not a calc value),
Break = YES.

**Exact procedure for each non-T1 row (rows 2–65):**
1. In Edit Label, click the **fx / Calculated Value** button (middle column, below the →/←
   arrows).
2. **Name** = the row's *Calc Value Name* from the xlsx (e.g. `Show Tier 2 - 2`).
3. **Type = Text.** It defaults to *Number* — change it and confirm it sticks (a Number-typed
   `if(...,"" )` errors).
4. **Formula** = paste the row's formula exactly, e.g. `if(TAG_PARA_STATE_2_BOOL, ASS_TAG_2_TXT, "")`.
   Both referenced params (the `TAG_PARA_STATE_n_BOOL` gate **and** the data param) must already
   be in the field list from Part 0, or OK throws "not a valid parameter".
5. **OK** — this creates the calc value **and** adds it as a new row in the label table (no
   separate → push needed).
6. In the label table, set that row's **Prefix**, **Suffix**, **Spaces = 0**, **Break**:
   - Set **Spaces = 0 BEFORE ticking Break** — the Spaces cell is only editable while the row
     *above* has no Break.
   - To set a Spaces value reliably: single-click the cell (make it current) → **double-click to
     select the existing digit** → type `0`. A plain click+type *appends* (`1` → `10`).
   - Break checkbox is finicky — click the square precisely.

**Copy/paste tip:** keep the xlsx open beside Revit; copy the Formula cell straight into the
Formula field to avoid typos in the `if(...)` and param names.

**Tier gating** = each row shows only when its `TAG_PARA_STATE_n_BOOL` is Yes, so the whole
label reflows cleanly as tiers toggle. This is why it's ONE label, not per-colour labels.

**Tier gating** = each row shows only when its `TAG_PARA_STATE_n_BOOL` is Yes, so the whole
label reflows cleanly as tiers toggle. This is why it's ONE label, not per-colour labels.

**The full 65-row table (Name · Formula · Prefix · Suffix · Break) is in
`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — build/verify every row against it in order.** Summary
of the tier blocks:

| Tier | Rows | Content |
|---|---|---|
| T1 | 1 | `ASS_TAG_1_TXT` (the ISO 19650 tag) |
| T2 | 2–7 | Tag 2, Description, **Status**, Std, Asset Systems, Mech System |
| T4 | 8–13 | Commissioning (state/date/operative) + Design intent (option/ref/keynote) |
| T5 | 14–34 | Cost · Payment · Variation · Performance/capacity · Item code |
| T6 | 35–40 | Carbon (A1-A3/A4/B6) + Material & finish |
| T7 | 41–46 | **Fabrication & QC** (spool/status/inspector) + Installation (date/hours/cost) |
| T8 | 47–52 | Clash triage + Coordination (criticality/zone/level) |
| T9 | 53–58 | As-built deviation + Health score + Warranty |
| T10 | 59–65 | Compliance (IFC/ACC) + Classification + Trace seq |

(T3 is intentionally absent — dropped in Part 1.)

> **T7 note.** Tier 7 is canonically **Fabrication & QC**, so it carries the three generic
> Fab/QC params (`ASS_SPOOL_NR_TXT`, `ASS_FAB_STATUS_TXT`, `ASS_QC_INSPECTOR_TXT`) plus the
> Installation rows — 6 rows, matching its sibling tiers. `ASS_FAB_STATUS_TXT` /
> `ASS_QC_INSPECTOR_TXT` also feed the **QA status badge** (Part 3): the badge is the
> at-a-glance colour, the T7 rows are the readable detail. That overlap is deliberate. The
> discipline-specific Fab/QC params (weld maps, pressure tests, refrigerant charge) stay
> dropped — they live in the fabrication schedules.

---

## Part 3 — Warning signs & status symbols (the two badges)

The old text warnings are replaced by **two visual badges** on the tag: **LEFT = data-completeness
gate**, **RIGHT = QA / sign-off gate**. Each shows 🟢/🟡/🔴 only when warnings are switched on;
hidden otherwise, and switchable off on print.

### 3a. Parameters (all already in MR_PARAMETERS.txt — shared)
| Param | Type | Scope | Set by | Meaning |
|---|---|---|---|---|
| `STING_GATE_DATA_STATUS_INT` | Integer | Instance | **Plugin** (`Stamp Gates`) | 0 = 🔴 / 1 = 🟡 / 2 = 🟢 (data gate colour) |
| `STING_GATE_QA_STATUS_INT` | Integer | Instance | **Plugin** (`Stamp Gates`) | 0 = 🔴 / 1 = 🟡 / 2 = 🟢 (QA gate colour) |
| `STING_GATE_DATA_MSG_TXT` | Text | Instance | **Plugin** (`Stamp Gates`) | terse data-gate reason (blank when green) — left message label |
| `STING_GATE_QA_MSG_TXT` | Text | Instance | **Plugin** (`Stamp Gates`) | terse QA-gate reason (blank when green) — right message label |
| `TAG_WARN_VISIBLE_BOOL` | Yes/No | Instance | User toggle | master on/off for both badges |

The plugin side is **done**: CREATE tab → **Stamp Gates** runs `ComplianceScan.ComputeElementGates`
and writes the two INTs on every taggable element. The badges read them automatically — no
per-family logic.

### 3b. Build the badges in the family (6 glyphs)
1. Place **6 nested generic-annotation glyphs**: at the LEFT badge position overlay a green ✓,
   an amber △, and a red △; at the RIGHT position overlay the same three. (Nested symbols are
   recommended over raw symbolic geometry.)
2. Put all 6 glyphs on a **new subcategory `STING_TagStatus`** (Object Styles). Colour each glyph
   via its subcategory. This subcategory is what makes the badges print-optional.
3. Add **6 family Yes/No parameters** — **UPPERCASE, family-local** (these are NOT shared params;
   do not add them to MR_PARAMETERS — they are pure in-family glue that reads the shared ints).
   Give each a formula, then bind each glyph's **Visible** property to its param:

   **LEFT — data gate:**
   - `VIS_DATA_GREEN = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 2)`
   - `VIS_DATA_AMBER = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 1)`
   - `VIS_DATA_RED   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 0)`

   **RIGHT — QA gate:**
   - `VIS_QA_GREEN = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 2)`
   - `VIS_QA_AMBER = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 1)`
   - `VIS_QA_RED   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 0)`

   > If `TAG_WARN_VISIBLE_BOOL` is stored as **TEXT**, write the first argument as
   > `(TAG_WARN_VISIBLE_BOOL = "Yes")`; if it's a real **YESNO**, use it bare.

Exactly one glyph per side is ever visible (the three `= 2 / = 1 / = 0` conditions are mutually
exclusive), so the overlay reads as a single traffic-light.

### 3c. What each gate means (so the badge colour is legible)
**Data gate** (left): 🔴 untagged · 🟡 tagged but not fully resolved (incomplete/placeholder tag,
missing STATUS, empty relevant container, or ISO 19650 validation error) · 🟢 fully resolved +
STATUS + all relevant containers + zero validation errors.
**QA gate** (right): 🟢 commissioning not required, OR commissioned + QC inspector recorded ·
🟡 some QA data present but not signed off · 🔴 no QA data.

### 3c-bis. Warning message labels beside each badge (optional but recommended)
Add a small **gated label** next to each glyph showing the plugin-stamped reason (blank when green,
so it only appears when there's an issue):
- LEFT / data:  `if(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_MSG_TXT, "")`
- RIGHT / QA:   `if(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_MSG_TXT, "")`

`Stamp Gates` fills these alongside the ints. Message vocabulary — data: `UNTAGGED` / `TAG INCOMPLETE`
/ `NO STATUS` / `EMPTY CONTAINER` / `ISO ERRORS`; QA: `NO QA` / `NO SIGN-OFF` / `QA PENDING`. Keep the
label text small (2–2.5 mm) and 1–2 words wide.

### 3c-ter. View-driven control (issue vs QA views)
Whole-tag *status colour* is not formula-drivable on a label, and per-view show/hide of the badges is
done at the **view level, not the family**:
- **Issue / print View Style Packs** turn the `STING_TagStatus` subcategory **OFF** → badges + message
  labels never print. (Wired into the issue/presentation packs in `STING_VIEW_STYLE_PACKS.json`.)
- The **`STING - Coordination / QA` pack** (`coord-qa`) turns `STING_TagStatus` **ON** and attaches the
  four gate filters (`qa-gate-data-red/amber`, `qa-gate-qa-red/amber` in `STING_AEC_FILTERS.json`) so a
  QA view recolours non-compliant tags automatically.
- `TAG_WARN_VISIBLE_BOOL` is the project-wide master switch; the subcategory is the per-view control.

### 3d. Print-optional
Turn subcategory `STING_TagStatus` **OFF** in print/issue view templates (VG); leave it **ON**
in on-screen QA views. Revit has no per-element print flag — this subcategory switch is the
clean equivalent.

### 3e. Same label or separate? — glyph placement for clean visuals (IMPORTANT)

**You cannot put a coloured glyph *inside* the label.** A Revit label is a pure **text**
element — it holds only parameter text + calculated values. Graphic traffic-light symbols
are separate annotation elements. So the badges are **NOT** part of the 65-row label.

Placement options, worst → best:
- ❌ **Text symbol inside the label** (a calc value emitting ● / ▲ / ✕). Works, but it's
  **monochrome** (a label is one colour — no green/amber/red) and it **flows with the text**,
  so it drifts as tiers toggle. Rejected.
- ❌ **A separate label per colour.** This was the original idea and is rejected for the same
  reason per-colour *text* tiers were: separate label elements sit at **fixed XY and don't
  reflow**, so they gap/overlap. Don't.
- ✅ **Nested generic-annotation symbols at a FIXED anchor** (recommended). One nested badge
  family per side, on subcategory `STING_TagStatus`, giving true per-glyph colour, clean
  on/off, and reuse.

**Best-practice layout (not broken):**
1. The 65-row text stays **ONE label** that reflows as tiers toggle.
2. Place the two badges as **nested annotation families** anchored to a **FIXED point the
   reflowing label never crosses** — put them **ABOVE the first text line or hard-LEFT of the
   tag origin** (data gate top-left, QA gate top-right). **Never below or inline** with the
   flowing block: the label grows/shrinks with tier depth, and anything beneath it gets
   overrun.
3. Prefer **one nested family per side** (its 3 internal glyphs switched by the `vis_*`
   Yes/No params) over 6 loose symbolic elements — cleaner to move, colour, and propagate.

Rule of thumb: **text tiers want reflow (one flowing label); badges want a fixed header
anchor (separate nested symbols).** Mixing the two — badges inside/below the flow — is exactly
what breaks the layout.

### 3f. Conveyor caveat (verify in the smoke test)
The badges are family elements + params, so they ride the master and propagate to all 206 via
recategorise. **Nested-symbol survival through recategorise is UNTESTED** — it's verification
**V5** in `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`. Build them into the master, propagate to Duct,
and confirm they survive before scaling.

---

## Part 4 — Completion checklist (what "done" means)

Tick every item before running the smoke test. The master is not finished until all pass.

**Params & setup**
- [ ] `UNIVERSAL_TAG_MASTER_PARAMS.txt` loaded as the active shared-param file.
- [ ] All **74** params from the xlsx *Parameters* sheet are in the Edit Label field list.

**Delete (Part 1)**
- [ ] All 3 T2 discipline rows removed.
- [ ] All 6 T3 rows removed.
- [ ] All warning text rows removed.
- [ ] No `HVC_/PLM_/ELC_/MNT_/WARN_` data param remains in any label row.

**Build (Part 2)**
- [ ] Row 1 = `ASS_TAG_1_TXT` direct, Break = YES.
- [ ] All **65** rows present, in order, **Built?** ticked in the xlsx.
- [ ] Every calc value is **Type = Text** (none left as Number).
- [ ] Every formula matches the xlsx exactly (gate + data param spelled right).
- [ ] Prefix / Suffix / **Spaces = 0** / Break set per row.
- [ ] Tier toggle test in the family: flip `TAG_PARA_STATE_4..10_BOOL` — rows appear/collapse and
      the label **reflows with no gaps/overlaps**.

**Badges (Part 3) — optional but recommended**
- [ ] 6 glyphs placed (LEFT green/amber/red + RIGHT green/amber/red).
- [ ] All 6 on subcategory `STING_TagStatus` (Object Styles).
- [ ] 6 family Yes/No params with the `vis_*` formulas from the xlsx *Badges* sheet.
- [ ] Each glyph's **Visible** bound to its `vis_*` param.
- [ ] Badges anchored at a **fixed** top-left / top-right point — never below/inside the flowing label.

**Finish**
- [ ] Master saved with an unambiguous name (e.g. `STING - UNIVERSAL Tag`).
- [ ] Loaded into the test project alongside a Duct tag family (both needed for the smoke test).

## After building: verify, then propagate

1. Run the **Duct smoke test** (`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`) — the one-family gate.
2. On PASS: `Propagate Universal` → ALL families; then **persist** the propagated `.rfa` to
   git-tracked `StingTools/Data/TagFamilies/` **and repopulate `…/TagFamilies/Seeds/`** (see the
   smoke-test doc's post-pass step 2), and redeploy.
3. Run `Stamp Gates` (fills the badge status ints) and `Tag Schedules` (builds the per-category
   engineering schedules that replace the dropped discipline tiers).

## Note — Tokens & Depth format changes need Overwrite = Yes to re-pad existing tags

The **Tokens & Depth** tab's separator / SEQ zero-pad / segment-order controls feed the tag
builder (`ParamRegistry.ApplyTagFormatOverrides` + `TagConfig.SeqPadWidth`) on the next
Combine / Stage-populate / Full-auto / Build Tags run. They **only reformat or re-pad tags that
already exist when the CREATE tab's `Overwrite` radio is set to `Yes`.** With `Overwrite = No`
(the default) an already-tagged element keeps its current `ASS_TAG_1_TXT`, so a pad change from
`0001`→`00001` (or a separator change) appears to "not take" — it applies to newly built tags
only. To reformat an existing model: set `Overwrite = Yes`, then re-run Build Tags. The on-drawing
`ASS_DISPLAY_TXT` is re-derived from the (masked) tokens on the same run.
