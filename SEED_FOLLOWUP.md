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
