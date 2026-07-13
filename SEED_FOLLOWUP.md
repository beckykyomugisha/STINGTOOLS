# SEED-AUTHORING FOLLOW-UP — Title-block cell authoring (Revit UI)

This file lists the work that **cannot be done from the Revit 2025 API** and must be
completed by a human in the Revit Family Editor (or by a future Revit version that
restores the label-authoring API). It is the companion to the W1–W5 data/contract
changes on branch `claude/tb-w1w5-impl`.

> **Why this exists.** Revit 2025 removed `Document.FamilyCreate.NewLabel` — the only
> API that could add or rebind a Tag-Label cell in a title-block `.rfa`. Proven by
> reflection scan + binary grep (0 hits). Everything below is therefore
> **DATA/CONTRACT prepared in code, CELL authored by hand**. The JSON, shared params,
> and drawing-type stamp mappings are already in place so each cell "lights up" the
> moment the seed `.rfa` label is authored — no further code change needed.

The seed families live in `Families/TitleBlocks/` and are declared in
`StingTools/Data/STING_TITLE_BLOCKS.json`.

---

## 1. DRAWING TITLE label — rebind to the built-in **Sheet Name** (W5a)

- **Data done:** the 7 DRAWING-TITLE labels in `STING_TITLE_BLOCKS.json` (anchor x =
  404 / 502 / 356 / 355 / 251 / 178 / 127) were repointed from the wrong
  `PRJ_ORG_PROJECT_NAME_TXT` to `{"param": "Sheet Name", "builtin": true}`.
  `LabelSpec.Builtin` + `TitleBlockFactory.PlaceLabel` now record the intent and skip
  authoring cleanly (no warning).
- **Cell to author:** in each affected seed `.rfa`, delete the label currently bound
  to `PRJ_ORG_PROJECT_NAME_TXT` in the DRAWING TITLE cell and add a **Label** bound to
  the Revit **built-in "Sheet Name"** parameter at the same position/size.
  (The legitimate PROJECT cell — anchor x = 2/3/4 — must stay on
  `PRJ_ORG_PROJECT_NAME_TXT`; do not touch it.)

## 2. QR-code slot (W4) — nothing to author, but verify placement

- **Code done:** `TitleBlock_StampQR` places the QR as a **sheet-level raster image**
  (`ImageType`/`ImageInstance`), so no `.rfa` cell is required. A `qr-code` slot was
  added to the 6 symbol-bearing families with a **seed default position** next to the
  north arrow.
- **To refine (optional):** nudge the `qr-code` slot `anchor`/`size` in
  `STING_TITLE_BLOCKS.json` (or draw a real reference-plane box in the `.rfa`) so the
  QR lands exactly where the title block reserves space. With no slot, the command
  falls back to the sheet's bottom-right corner.

## 3. New shared params — author the visible cells (W5b/W5c)

These shared params were added to `MR_PARAMETERS.txt` + `PARAMETER_REGISTRY.json`
(group 26 `TBL_TITLEBLOCK`) and wired into every drawing type's `titleBlockParams`
in `STING_DRAWING_TYPES.json` so they auto-stamp once a matching cell exists. Run
**CREATE → Load Shared Params** after pulling, then author a Label cell for each:

| Shared param | Suggested cell / label name | Auto-stamp source (already wired) |
|---|---|---|
| `TB_COPYRIGHT_TXT` | "Copyright" (footer) | `${TB_COPYRIGHT_TXT}` |
| `TB_DO_NOT_SCALE_TXT` | "Do Not Scale" caption | `${TB_DO_NOT_SCALE_TXT}` |
| `PRJ_ORG_CONTACT_PHONE_TXT` | "Company Phone" (contact block) | `${PRJ_ORG_CONTACT_PHONE_TXT}` |
| `PRJ_ORG_CONTACT_EMAIL_TXT` | "Company Email" (contact block) | `${PRJ_ORG_CONTACT_EMAIL_TXT}` |
| `PRJ_ORG_CONTACT_WEBSITE_TXT` | "Company Website" (contact block) | `${PRJ_ORG_CONTACT_WEBSITE_TXT}` |
| `PRJ_ORG_REG_NO_TXT` | "Registration No" (contact block) | `${PRJ_ORG_REG_NO_TXT}` |

> The `titleBlockParams` **key** must equal the family/label parameter name that the
> seed author gives the cell (the `${…}` value already reads the ProjectInfo shared
> param). If you bind the visible label directly to the shared param, rename the
> `titleBlockParams` key to the shared-param name so `TitleBlockParamApplier` finds it.
> Seed default text for copyright / contacts can be lifted from
> `Data/Templates/STING_CORPORATE_BRAND.json` (`copyright_mask`, `company_phone`,
> `company_contact_email`, `company_website`).

## 4. Confirm label slots for already-defined params (W5b)

These shared params already exist (`STING_TITLE_BLOCK_PARAMETERS.txt` /
`MR_PARAMETERS.txt`); confirm each has an authored label cell in the seed `.rfa`,
add one where missing:

- `PRJ_ORG_SECURITY_CLASS_TXT` (security classification)
- `PRJ_ORG_PROJECT_NORTH_TXT`, `PRJ_ORG_COORD_SYSTEM_TXT`, `PRJ_ORG_GROUND_LEVEL_TXT`
- `PRJ_ORG_APPOINTING_PARTY_TXT`, `PRJ_ORG_LEAD_APPOINTED_PARTY_TXT`
- `PRJ_TB_DELIVERABLE_CDE_TXT` (CDE ref)
- `STING_LOIN_LOD_TXT` (LOIN / LOD)
- `STING_AUTHORISED_BY_TXT` (+ `STING_AUTHORISED_DATE_TXT`)
- `PRJ_DWG_ISSUE_PURPOSE_TXT` (issue purpose)
- `PRJ_TB_REVISION_DESCRIPTION_TXT` (revision description)

## 5. Presentation symbols / graphics (not label cells)

Author or verify in the seed `.rfa` (nested families / detail items):

- Scale bar, north arrow, projection symbol, key plan — nested families toggled by
  `TB_SHOW_SCALEBAR_BOOL` / `TB_SHOW_NORTH_ARROW_BOOL` / `TB_SHOW_KEY_PLAN_BOOL`.
- Suitability-code legend chip.
- Discipline colour strip (`TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL`).

---

## App-side follow-up (not Revit)

- **QR payload scheme.** `TitleBlock_StampQR` encodes
  `https://app.planscape.build/sheet/{fullRef}` (base URL is configurable via
  `STING_PLANSCAPE_URL` / machine settings). There is a legacy `sting://asset/...`
  deep-link scheme in `StingQRHelper.BuildAssetUrl`. Decide whether the mobile /
  web app should also register a `planscape://` custom scheme and add a
  `/sheet/{ref}` route to the web app so scanned codes resolve.

## Out of scope (left as-is by design)

- `PRJ_SHEET_SYSTEM_TXT` — display-only, drives nothing; not changed.
- Any merge to `main` — this branch is for the in-Revit acceptance sweep only.

---

## STATUS — seed authoring session 2026-07-12 (STING_TB_A1_BIM_v2.0.rfa)

Authored **live in the Revit Family Editor** by computer-use against the user's master
seed at `D:\Work 2026\tendo test\Families\TitleBlocks\_seeds\STING_TB_A1_BIM_v2.0.rfa`
(seed lives outside the repo per the seed-family pattern). Saved.

### DONE — "Core compliance set" (user-confirmed scope)

The three previously-empty gridded boxes in the bottom row (below DRG NO, left of the
SYSTEM cell) were authored as inline `CAPTION: value` Tag-Label cells, type **`2`**
(2 mm regular) to match the row, each bound to its shared param:

| Cell | Prefix caption | Shared param (verified bound) | Sample |
|---|---|---|---|
| Box 1 | `SECURITY: ` | `PRJ_ORG_SECURITY_CLASS_TXT` | OFFICIAL |
| Box 2 | `CDE REF: ` | `PRJ_TB_DELIVERABLE_CDE_TXT` | PLNS-BIM |
| Box 3 | `PURPOSE: ` | `PRJ_DWG_ISSUE_PURPOSE_TXT` | CONSTRUCTION |

- **SYSTEM cell normalised:** was Tag-Label type `B 2` (2.5 mm bold); reassigned to
  type `2` so the whole bottom row (`SECURITY | CDE REF | PURPOSE | SYSTEM`) is a
  uniform 2 mm regular row, left-aligned at a consistent inset.
- **Standing notes** added as static **Text** (type `2mm`) in the NOTES box, below the
  `PRJ_TB_NOTES_LEGEND_REF_TXT` label (single cell, no divider crossing):
  - `DO NOT SCALE - USE FIGURED DIMENSIONS ONLY`
  - `(C) PLANSCAPE LIMITED - ALL RIGHTS RESERVED`
  (Authored as fixed text rather than binding `TB_DO_NOT_SCALE_TXT` /
  `TB_COPYRIGHT_TXT`, since these are standing corporate notices; the params remain
  available if a project ever needs to override them per §3 above.)

### DRAWING TITLE (W5a) — appears already correct

The DRAWING TITLE cell in this seed displays the built-in **Sheet Name** sample
("Drawing title"), i.e. it is already bound to Sheet Name, not
`PRJ_ORG_PROJECT_NAME_TXT`. Confirmed by inspection; no rebind performed. **Verify**
on next open (select the cell → Edit Label shows *Sheet Name*).

### REMAINING — graphics (need symbol families)

Per the user's chosen option "load as symbol families" (hand-drawing declined), these
graphic conventions were **not** authored this session because suitable annotation
symbol `.rfa` families were not available/identified while working unattended. Supply
the families and place them (or point to them) to finish:

- **North arrow** — annotation symbol; natural home is the KEY PLAN cell.
- **Scale bar** — graphic scale annotation; near the SCALE cell / bottom.
- **Projection symbol** (1st/3rd-angle) — small standard TB graphic.
- (Optional) **Contact block** — phone/email/website/reg-no labels were the *Full set*,
  not selected this pass; params are deployed (§3) if wanted later.

### Deploy note

`MR_PARAMETERS.txt` (with the 6 new params) was deployed to
`C:\Dev\STING_PLACEMENT_GOLD\data\MR_PARAMETERS.txt` (backup kept) and Revit's active
shared-parameter file already points there, so the params above were available to bind.

---

## STATUS — parameter naming normalization (Work Item A, 2026-07-13)

Shared parameters were **renamed (GUID-preserved)** to a consistent `PRJ_*` scheme.
No `.rfa` was touched. Because **GUIDs are preserved**, any title-block family cell
already bound to a renamed parameter keeps working — Revit binds label cells by GUID,
not by name — but the **Family Editor will display the OLD parameter name** on that
cell until the shared-parameter file is re-loaded and the family is re-bound.

### Renames (GUID preserved, 1:1)

| Old parameter | New parameter |
|---|---|
| `STING_SHEET_*_TXT` (10: BIM_MODE / FULL_REF / LEVEL / OF_TOTAL / ORIG / PROJECT / ROLE / SEQ / TYPE / VOLUME) | `PRJ_SHEET_*_TXT` |
| `STING_SHEET_SEQUENCE_INT` | `PRJ_SHEET_SEQUENCE_INT` |
| `STING_SUITABILITY_DESC_TXT` | `PRJ_DWG_SUITABILITY_DESC_TXT` |
| `STING_LOIN_LOD_TXT` | `PRJ_DWG_LOIN_LOD_TXT` |
| `STING_FEDERATION_STATUS_TXT` | `PRJ_TB_FEDERATION_STATUS_TXT` |
| `STING_AUTHORISED_BY_TXT` | `PRJ_TB_AUTHORISED_BY_TXT` |
| `STING_AUTHORISED_DATE_TXT` | `PRJ_TB_AUTHORISED_DATE_TXT` |
| `TB_COPYRIGHT_TXT` | `PRJ_TB_COPYRIGHT_TXT` |
| `TB_DO_NOT_SCALE_TXT` | `PRJ_TB_DO_NOT_SCALE_TXT` |

### Consolidations (A2 — duplicate toggles merged to ONE canonical)

The legacy GROUP 13 `PRJ_INFORMATION` toggle variants were **deleted** (their GUIDs
dropped from `MR_PARAMETERS.txt` / `.csv` / `PARAMETER_REGISTRY.json`); the surviving
canonical toggle carries the **GROUP 26 `TBL_TITLEBLOCK`** GUID under a new `PRJ_TB_SHOW_*`
name:

| Merged from | Canonical (surviving GUID) |
|---|---|
| `TB_SHOW_KEY_PLAN_BOOL` + `PRJ_TB_SHOW_KEYPLAN_BOOL` (dropped `8dd6b517…`) | `PRJ_TB_SHOW_KEY_PLAN_BOOL` (`9a64e982…`) |
| `TB_SHOW_NORTH_ARROW_BOOL` + `PRJ_TB_SHOW_NORTHARROW_BOOL` (dropped `58c6e51f…`) | `PRJ_TB_SHOW_NORTH_ARROW_BOOL` (`0981c0a9…`) |
| `TB_SHOW_SCALEBAR_BOOL` + `PRJ_TB_SHOW_SCALEBAR_BOOL` (dropped `fa841ad5…`) | `PRJ_TB_SHOW_SCALE_BAR_BOOL` (`afcd0647…`) |
| `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` + `PRJ_TB_SHOW_DISCBAND_BOOL` (dropped `483f47d7…`) | `PRJ_TB_SHOW_DISCIPLINE_BAND_BOOL` (`fcd1f7f2…`) |
| `TB_SHOW_QR_CODE_BOOL` | `PRJ_TB_SHOW_QR_CODE_BOOL` (`77246dff…`) |
| `TB_SHOW_COMPANY_STRIP_BOOL` | `PRJ_TB_SHOW_COMPANY_STRIP_BOOL` (`46ee0d08…`) |
| `TB_SHOW_REV_TABLE_BOOL` | `PRJ_TB_SHOW_REV_TABLE_BOOL` (`da7b6ce4…`) |

> A dropped legacy toggle's GUID is gone. A family cell/visibility param that was bound
> to a dropped legacy toggle (e.g. `PRJ_TB_SHOW_DISCBAND_BOOL`) must be **re-bound** to the
> surviving canonical param in Family Editor. Cells bound to the surviving GUID are unaffected.

### Seed re-bind impact (what a human must still do)

1. **Re-copy the shared-parameter file** to GOLD: the renamed `MR_PARAMETERS.txt` needs to
   replace `C:\Dev\STING_PLACEMENT_GOLD\data\MR_PARAMETERS.txt` so Revit's active SP file
   shows the new names. *(Explicitly OUT OF SCOPE for this branch — do it at deploy.)*
2. In each seed `.rfa`, run **Manage → Shared Parameters → reload**, then for any cell that
   now shows an old name, **rebind** it to the new-named parameter (same GUID → same data).
3. Rebind the four dropped legacy toggles to their canonical survivors (previous table).
4. `.rfa` files were **NOT** touched in code (Revit 2025 has no label-authoring API).

---

## STATUS — suitability/purpose row fix (Work Item B, 2026-07-13)

The default title-block compliance row was **de-duplicated**: the suitability *code*
and the *issue purpose* overlapped semantically, and the master seed stacked both a
**LOD** and a **PURPOSE** label cell in the same pocket.

### Data/contract changes (done in code)

- **`STING_TITLE_BLOCKS.json`** — removed the 2 `PRJ_DWG_ISSUE_PURPOSE_TXT` **label
  cells** (Box 3) from the seed `labels[]`; the parameter stays **DEFINED** in every
  family's `parameters[]` (7 defs, unchanged) so a project can still surface it if
  wanted — it is just no longer placed in the default row. Normalized the 6 `"LOIN/LOD"`
  captions to `"LOD"`.
- Suitability is already split correctly: the **chip** binds to
  `PRJ_DWG_SUITABILITY_COD_TXT` (a CODE, default `"S2"`) and the **description** to the
  separate `PRJ_DWG_SUITABILITY_DESC_TXT` — no change needed.
- **`STING_DRAWING_TYPES.json`** — added `"PRJ_DWG_LOIN_LOD_TXT": "LOD 300"` to the
  `titleBlockParams` of all 93 production drawing types so the repurposed Box 3 (now LOD)
  is auto-stamped. Result: the row reads **SECURITY | CDE REF | LOD | SYSTEM**.

### Seed re-bind impact (what a human must still do)

- In the master seed `.rfa`, the bottom-row **Box 3** cell authored as `PURPOSE:`
  (bound to `PRJ_DWG_ISSUE_PURPOSE_TXT`) must be **re-captioned `LOD:`** and **re-bound to
  `PRJ_DWG_LOIN_LOD_TXT`**. `PRJ_DWG_ISSUE_PURPOSE_TXT` remains a valid shared param — leave
  it available but do not place it in the default row.
