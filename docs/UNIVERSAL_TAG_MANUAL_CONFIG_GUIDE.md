# Universal STING Tag — Manual Configuration Guide

**One label. 62 rows. Discipline-agnostic. Built ONCE by hand, then propagated to all 206.**

This is the master walkthrough for hand-building the universal tag label in the Revit Family
Editor. It supersedes the per-family bespoke-tier authoring. Three parts:

- **Part 1 — DELETE** the discipline-specific + T3 + warning-text rows from your current master.
- **Part 2 — BUILD** the 62 universal rows.
- **Part 3 — WARNING / STATUS SYMBOLS** — the two visual status badges that replace the old
  text warning rows.

Companion docs (same folder): `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` (the canonical 62-row table)
· `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` (the verify gate).

Why this changed: the API can't author label rows, cross-category paste is blocked, and every
family's bespoke tier structure is unique — so a single bespoke master can't be propagated. A
*discipline-agnostic* label CAN (recategorise preserves rows). Discipline engineering data moves
to per-category **schedules** (`Tag Schedules` button); per-item warnings move to the two
**status badges** (Part 3).

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

## Part 2 — Build the 62 universal rows

**Mechanics (every non-T1 row):**
1. Edit Label → **fx** (Calculated Value) button in the middle column.
2. **Name** = the row name from the table; **Type = Text** (it defaults to Number — verify it
   sticks); **Formula** = paste the `if(...)` exactly. OK. (The referenced parameter must
   already be in the label field list, or OK errors — add it first via Add-parameter.)
3. In the label table set that row's **Prefix**, **Suffix**, **Spaces = 0**, and **Break**.
   Set **Spaces = 0 BEFORE ticking Break** — Spaces is only editable while the row *above* has
   no Break. Double-click the Spaces cell to select the digit before typing (a plain click
   appends).

**Row 1** = `ASS_TAG_1_TXT` added directly (not a calc value), Break = YES.

**Tier gating** = each row shows only when its `TAG_PARA_STATE_n_BOOL` is Yes, so the whole
label reflows cleanly as tiers toggle. This is why it's ONE label, not per-colour labels.

**The full 62-row table (Name · Formula · Prefix · Suffix · Break) is in
`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — build/verify every row against it in order.** Summary
of the tier blocks:

| Tier | Rows | Content |
|---|---|---|
| T1 | 1 | `ASS_TAG_1_TXT` (the ISO 19650 tag) |
| T2 | 2–7 | Tag 2, Description, **Status**, Std, Asset Systems, Mech System |
| T4 | 8–13 | Commissioning (state/date/operative) + Design intent (option/ref/keynote) |
| T5 | 14–34 | Cost · Payment · Variation · Performance/capacity · Item code |
| T6 | 35–40 | Carbon (A1-A3/A4/B6) + Material & finish |
| T7 | 41–43 | Installation (date/hours/cost) |
| T8 | 44–49 | Clash triage + Coordination (criticality/zone/level) |
| T9 | 50–55 | As-built deviation + Health score + Warranty |
| T10 | 56–62 | Compliance (IFC/ACC) + Classification + Trace seq |

(T3 is intentionally absent — dropped in Part 1.)

---

## Part 3 — Warning signs & status symbols (the two badges)

The old text warnings are replaced by **two visual badges** on the tag: **LEFT = data-completeness
gate**, **RIGHT = QA / sign-off gate**. Each shows 🟢/🟡/🔴 only when warnings are switched on;
hidden otherwise, and switchable off on print.

### 3a. Parameters (all already in MR_PARAMETERS.txt — Task 2 added the two INTs)
| Param | Type | Scope | Set by | Meaning |
|---|---|---|---|---|
| `STING_GATE_DATA_STATUS_INT` | Integer | Instance | **Plugin** (`Stamp Gates`) | 0 = 🔴 / 1 = 🟡 / 2 = 🟢 (data gate) |
| `STING_GATE_QA_STATUS_INT` | Integer | Instance | **Plugin** (`Stamp Gates`) | 0 = 🔴 / 1 = 🟡 / 2 = 🟢 (QA gate) |
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
3. Add **6 family Yes/No parameters** (formula-driven) and bind each glyph's **Visible** property
   to its param:

   **LEFT — data gate:**
   - `vis_data_green = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 2)`
   - `vis_data_amber = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 1)`
   - `vis_data_red   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 0)`

   **RIGHT — QA gate:**
   - `vis_qa_green = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 2)`
   - `vis_qa_amber = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 1)`
   - `vis_qa_red   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 0)`

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

### 3d. Print-optional
Turn subcategory `STING_TagStatus` **OFF** in print/issue view templates (VG); leave it **ON**
in on-screen QA views. Revit has no per-element print flag — this subcategory switch is the
clean equivalent.

### 3e. Conveyor caveat (verify in the smoke test)
The badges are family elements + params, so they ride the master and propagate to all 206 via
recategorise. **Nested-symbol survival through recategorise is UNTESTED** — it's verification
**V5** in `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`. Build them into the master, propagate to Duct,
and confirm they survive before scaling.

---

## After building: verify, then propagate

1. Save the master (unambiguous name, e.g. `STING - UNIVERSAL Tag`).
2. Run the **Duct smoke test** (`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`) — the one-family gate.
3. On PASS: `Propagate Universal` → ALL families, then run `Stamp Gates` and `Tag Schedules`.
