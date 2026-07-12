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
