# Title Block Family Library — Review + Design

Review of the shipped `STING_TB_A1_B_v1.0.rfa` (PDF supplied at
`https://github.com/StingD85/transfer/blob/main/STING_TB_A1_B_v1.0.rfa.pdf`)
and a complete design of the remaining title block + cover page
families needed for an ISO 19650-2 / BS EN ISO 7200 production set,
in the visible box-layout format that maps 1:1 to a Revit Family
Editor session.


## 1. Review of `STING_TB_A1_B_v1.0`

### 1.1 What ships today

The supplied PDF is an A1 landscape sheet stub with the title-strip
along the bottom edge and a thin status band along the top edge.
Inferred field inventory:

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                     │
│                       ┌──── DRAWABLE ZONE ────┐                                     │
│                       │ (viewport area)       │                                     │
│                                                                                     │
│                                                                                     │
│                                                                                     │
│                                                                                     │
│                                                                                     │
│                                                                                     │  ← status band only on
│ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  │     the right ~30 %
│ │STATUS │SUITABILITY                          │  REV  │  REV DATE                 │ │
│ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  │
├─────────────┬──────────────┬───────────────┬─────────┬────────┬──────────────────┤
│ CLIENT      │ NOTES        │ DRAWING TITLE │ DATE    │ PAPER  │  SCALE           │
│             │              │   "Unnamed"   │         │ SIZE   │                  │
├─────────────┤              │               ├─────────┴────────┴──────────────────┤
│ PROJECT     │              │               │ DRAWN  │ CHECKED │ APPROVED         │
│             │              │               │ Author │ Checker │  Approver        │
├─────────────┤              │               ├────────┴─────────┴──────────────────┤
│             │              │               │ DRG STATUS    SHEET. ┌────────┐    │
│             │              │               │               ┌──    │ A0-003 │    │  ← arrow-shape
├─────────────┤              │               │               └──    └────────┘    │     "sheet tag"
│ LEAD/ARCH   │              │               ├──────┬───────┬───────┬───────┬─────┤
│ STRUCTURAL  │              │               │SEQ.NO│REV.NO │DESCR. │APPR.BY│DATE │  ← revision history
│ MEP         │              │               ├──────┴───────┴───────┴───────┴─────┤
│ CONTRACTOR  │              │ DRAWING TITLE │             A0-003                 │  ← redundant sheet#
└─────────────┴──────────────┴───────────────┴────────────────────────────────────┘
```

Visible fields:

| Block | Field | Status |
|---|---|---|
| Top status band | `STATUS`, `SUITABILITY`, `REV`, `REV DATE` | empty placeholders + chip shapes |
| Parties strip (left column) | `CLIENT`, `PROJECT`, `LEAD/ARCHITECT`, `STRUCTURAL`, `MEP`, `CONTRACTOR` | static labels only |
| Notes panel | `NOTES` | empty |
| Drawing title | `DRAWING TITLE: Unnamed` | placeholder text twice (top + bottom) |
| Authoring | `DRAWN BY: Author`, `CHECKED BY: Checker`, `APPROVED BY: Approver` | Revit defaults — not bound |
| Sheet meta | `DATE`, `PAPER SIZE`, `SCALE`, `DRG STATUS`, `DRG NO.` | empty placeholders |
| Sheet number | `A0-003` shown twice — once inside the arrow "SHEET." badge, once in the bottom-right tile | duplicated, no segmentation |
| Revision history | 5 columns: `SEQ.NO`, `REV.NO`, `DESCRIPTION`, `APPR.BY`, `DATE` | header row only |

### 1.2 What does the "sheet tag" do?

The arrow-shaped element labelled `SHEET.` is a **sheet locator
graphic**, not a Revit feature. In production it is a chevron/flag
that visually anchors the sheet number on the page so reviewers can
quickly find it when sheets are stacked or A3-printed at reduced
scale. Inside the chevron sits a single label bound (or intended to
be bound) to the Revit built-in parameter `Sheet Number`.

The duplication of `A0-003` in two places is a layout bug — one of
them is meant to show **sheet of total** (e.g. `03 / 12`) using the
Revit built-ins `Sheet Number` + `Sheet Issue Date` + a "Total Sheet
Count" calculated parameter, but neither cell currently carries the
formula that converts the chevron into a "this is sheet 3 of 12"
indicator. The intent is right; the binding is missing.

### 1.3 Missing fields against ISO 19650-2 / BS EN ISO 7200

The current title block carries 10 distinct field groups. ISO 19650-2
§5.2.2 + the UK BIM Framework's "Information Container" guidance
expects **27**. Table groups what is missing:

#### A. ISO 19650 sheet identifier — segmented

The bottom-right cell shows `A0-003` (free text). ISO 19650-2 mandates
the seven-segment information container ID:

```
{project}-{originator}-{volume/system}-{level/location}-{type}-{role}-{number}
   STG   -   ABC      -      ZZ        -      XX        -  DR  -  A   -  0001
```

— and the title block should display **all seven segments**, not just
the trailing number. Recommend two cells: a small breakdown row above
the prominent sheet number that names every segment, then the full
concatenated string in the prominent cell.

#### B. Originator practice block

| Missing field | ISO 19650 reference |
|---|---|
| Originator full company name + Companies House number | §5.2.2 a) |
| Originator logo (image parameter) | §5.2.2 a) |
| Originator address (registered office) | §5.2.2 a) |
| Originator phone / email / web | §5.2.2 a) |
| Project security classification | §5.2.2 b) UK Govt Security Policy Framework |

#### C. Drawing context block

| Missing field | Reference |
|---|---|
| Project number / project code | ISO 7200 §6.4 |
| Project address / site location | BS 1192 §A.4 |
| RIBA stage (0–7) | RIBA Plan of Work 2020 |
| Uniclass 2015 classification (Co/En/SL/EF/Ss/Pr) | NBS Toolkit |
| Coordinate system + project north bearing | BS 1192 §A.5 / OS GB |
| Survey datum + ground level reference | BS 1192 §A.5 |
| Drawing classification (DR / SK / SH / VI / CA / 3D) | ISO 19650-2 type code |
| Originator's drawing reference (pre-CDE legacy ref) | optional |

#### D. Lifecycle / suitability

| Missing field | Reference |
|---|---|
| Suitability code with full description (`S2 — Shared, Non-contractual`) | ISO 19650-2 §5.2.2 c) |
| Suitability colour chip (the shipped one is filled only with the code) | UK BIM Framework example |
| `IFC` / `IFA` / `IFR` / `IFT` issue-purpose code | BS EN ISO 7200 §6.10 |
| Federation status (Federated / Not federated) | ISO 19650-2 §5.4 |
| LOIN / LOD note (e.g. "LOD 300 — geometric, no manuf data") | ISO 19650-2 §5.2.4 + BIMForum LOD |
| Model file path + last save timestamp | BS 1192 §A.7 |
| Native software + version (e.g. Revit 2025.1) | ISO 7200 §6.6 |

#### E. Issuance / approval lifecycle

| Missing field | Reference |
|---|---|
| `Drawn by`, `Checked by`, `Approved by` — bound to PRJ_TEAM members not Revit's "Author" | — |
| `Verified by` (4-eyes ISO 19650 author/checker/reviewer/authoriser) | ISO 19650-2 §5.6 |
| `Authorised by` — separate from approved | ISO 19650-2 §5.6 |
| Sign-off date per role | ISO 19650-2 §5.6 |
| Issue purpose narrative (free text) | ISO 7200 §6.10 |
| Transmittal reference number (back-link to TR-NNNN) | UK BIM Framework |
| CDE container reference (the WIP / Shared / Published path) | ISO 19650-2 §5.5 |
| MIDP / TIDP deliverable ID | ISO 19650-2 §5.5 |

#### F. Statutory / safety

| Missing field | Reference |
|---|---|
| CDM 2015 hazard / risk note | CDM Regulations 2015 reg 9 |
| BS 8536 operational handover note | BS 8536-1 Annex C |
| Fire-safety design intent reference | Building Safety Act 2022 |
| Asbestos register reference (refurb) | CAR 2012 |
| Disclaimer / copyright / "do not scale" notice | BS 1192 §A.8 |
| "Original size: A1" check note | BS EN ISO 5457 |
| Print-scale check bar (graphic ruler used to verify scaling) | BS EN ISO 5457 §5.7 |

#### G. Navigation / metadata

| Missing field | Reference |
|---|---|
| North arrow (instance) — for plan sheets | BS 1192 §A.5 |
| Key plan / location plan thumbnail | BS 1192 §A.5 |
| QR code / deep-link to CDE | UK BIM Framework BIMplus example |
| Bar code + sheet GUID | optional |
| Adjacent-sheet match-line tags | BS 1192 §A.6 |
| Sheet-of-total counter (`03 / 12`) | ISO 7200 §6.5 |
| Continuation arrow to next/previous sheet | BS 1192 §A.6 |

#### H. Revision history strip

The shipped strip has 5 columns. ISO 19650-2 expects 8:

| Column | Purpose | Currently |
|---|---|---|
| Rev | Revision code (P01 / C01) | `REV.NO` ✓ |
| Date | Issue date | `DATE` ✓ |
| Description | Free-text change description | `DESCRIPTION` ✓ |
| Drawn by | Initials of author | missing — `SEQ.NO` is unrelated |
| Checked by | Initials of checker | missing |
| Approved by | Initials of approver | `APPR.BY` ✓ |
| Suitability | Suitability code at issue | missing |
| Status | WIP/Shared/Published at issue | missing |

### 1.4 Workflow gaps

`StingTools.Core.Drawing.TitleBlockParamApplier` (Phase 138 bonus 4)
binds title-block instance parameters declaratively from JSON, with
two substitution kinds: `${PRJ_ORG_xxx}` and `{disc}/{lvl}/{sys}/…`.
The applier is correct, the spec is correct, but the production
families have not yet been authored to honour the spec. Specific
gaps surfaced by the PDF:

1. **No declared `PRJ_ORG_*` bindings on the supplied family.** None
   of `${PRJ_ORG_CLIENT_NAME}`, `${PRJ_ORG_PROJECT_CODE}`,
   `${PRJ_ORG_COMPANY_NAME}` etc. resolve — the labels carry the
   default Revit text "CLIENT", "PROJECT" instead of the resolved
   organisational data. Effect: every project re-types the same
   information into every sheet manually.
2. **`Author` / `Checker` / `Approver` placeholder.** These read from
   Revit's `View.Author` (a string, not a person). Should bind to
   project-team roles via `PRJ_TEAM_DRAWN_BY_TXT` /
   `_CHECKED_BY_TXT` / `_APPROVED_BY_TXT` shared params (Phase 165
   adds `Planscape.Server` ProjectMembers; the ID should travel
   through the title block).
3. **Sheet number is a single string.** Should be a calculated
   parameter assembling from
   `${PRJ_ORG_PROJECT_CODE}-${PRJ_ORG_ORIGINATOR_CODE}-{vol}-{lvl}-{type}-{disc}-{seq:D4}`
   so the seven segments are individually editable through Revit's
   sheet properties dialog and recombine automatically.
4. **No revision history schedule.** The strip should be a Revit
   `Revision Schedule` element bound to `Sheet Issued To` /
   `Revision Description` rows, not static cells. Currently it only
   shows column headers.
5. **No North arrow / key-plan placeholder.** Should be detail items
   that Revit's `BatchSectionsCommand` / `BatchPlanCommand` can drop
   pre-located in the layout.
6. **Status / Suitability chips are graphic-only.** The chip shapes
   carry no parameter binding; the colour fill should be an Object
   Style + filter pair driven by `STING_SUITABILITY` so the chip
   colour reflects the active suitability automatically (S0–S2 grey,
   S3–S4 amber, S6–S7 green).
7. **No transmittal back-link.** Transmittal orchestration (Phase 112
   template engine v1.1) writes `Transmittals.json` rows but the
   sheet doesn't carry the `TR-NNNN` ID — printed PDFs lose the link
   back to their issue context.
8. **No QR code / model GUID.** The mobile app's BCC scan-to-open
   workflow needs a QR on every sheet that decodes to
   `planscape://project/{id}/sheet/{guid}`.

## 2. Family library plan

Eighteen `.rfa` families covering the full production set. Existing
seven (Phase 113 Assembly + Authority stubs already in
`Families/AssemblyTitleBlocks/`) are listed but not redrawn. Eleven
are new.

| # | File | Status | Purpose |
|---|---|---|---|
| 1 | `STING_TB_A0_v1.0.rfa` | NEW | A0 working sheet — large plans / coordination |
| 2 | `STING_TB_A1_v2.0.rfa` | REPLACES `_A1_B_v1.0` | A1 working sheet — primary production size |
| 3 | `STING_TB_A2_v1.0.rfa` | NEW | A2 working sheet — riser / schedule / smaller plans |
| 4 | `STING_TB_A3_v1.0.rfa` | NEW | A3 working sheet — details, RFI sketches |
| 5 | `STING_TB_A3_PORT_v1.0.rfa` | NEW | A3 portrait — schedules, lists, certificates |
| 6 | `STING_TB_A4_PORT_v1.0.rfa` | NEW | A4 portrait — RFIs, transmittal notes, single-page memos |
| 7 | `STING_TB_ASSEMBLY_PIPE.rfa` | EXISTS (stub) | Pipe spool fab |
| 8 | `STING_TB_ASSEMBLY_DUCT.rfa` | EXISTS (stub) | Duct spool fab |
| 9 | `STING_TB_ASSEMBLY_COND.rfa` | EXISTS (stub) | Conduit / cable assembly fab |
| 10 | `STING_TB_ASSEMBLY_HANGER.rfa` | EXISTS (stub) | Hanger / support fab |
| 11 | `STING_TB_PRESENT_A1_v1.0.rfa` | NEW | Presentation — full-bleed render area |
| 12 | `STING_TB_PRESENT_A1_MONO_v1.0.rfa` | NEW | Mono presentation variant |
| 13 | `STING_TB_COVER_A1_v1.0.rfa` | NEW | Project / package cover page |
| 14 | `STING_TB_DIVIDER_A1_v1.0.rfa` | NEW | Discipline section divider |
| 15 | `STING_TB_REGISTER_A1_v1.0.rfa` | NEW | Drawing register sheet |
| 16 | `STING_TB_TRANSMITTAL_A4_v1.0.rfa` | NEW | Transmittal notice sheet |
| 17 | `STING_TB_SUBMISSION_KCCA.rfa` | EXISTS (stub) | KCCA submission |
| 18 | `STING_TB_SUBMISSION_ERA.rfa` | EXISTS (stub) | ERA submission |
| 19 | `STING_TB_SUBMISSION_NEMA.rfa` | EXISTS (stub) | NEMA submission |
| 20 | `STING_TB_CLARIFICATION_A3_v1.0.rfa` | NEW | RFI / clarification sketch sheet |

The working-sheet family (1–6) shares one design — only paper size
and slot-coordinate set differ. The fab / authority / presentation /
cover / divider / register / transmittal / clarification families
each have a distinct layout shown below.

### 2.1 Shared parameter universe

Every family in the library binds the same four-part shared parameter
universe so the `TitleBlockParamApplier` can populate any cell on any
sheet from a single token dict:

```
GROUP A — Project / Originator (ProjectInformation scope)
  PRJ_ORG_PROJECT_CODE_TXT      "STG"
  PRJ_ORG_PROJECT_NUMBER_TXT    "2026-104"
  PRJ_ORG_PROJECT_NAME_TXT      "Acme Logistics HQ"
  PRJ_ORG_PROJECT_ADDRESS_TXT   "12 Industrial Way, Reading RG2 0XX"
  PRJ_ORG_CLIENT_NAME_TXT       "Acme Holdings Ltd"
  PRJ_ORG_CLIENT_LOGO_IMG       (image param)
  PRJ_ORG_ORIGINATOR_CODE_TXT   "PLNS"
  PRJ_ORG_ORIGINATOR_NAME_TXT   "Planscape Limited"
  PRJ_ORG_ORIGINATOR_ADDRESS_TXT
  PRJ_ORG_ORIGINATOR_LOGO_IMG
  PRJ_ORG_LEAD_APPOINTED_PARTY_TXT
  PRJ_ORG_APPOINTING_PARTY_TXT
  PRJ_ORG_RIBA_STAGE_TXT        "Stage 4"
  PRJ_ORG_UNICLASS_CO_TXT       "Co_25_30 Office buildings"
  PRJ_ORG_COORD_SYSTEM_TXT      "OS GB / British National Grid"
  PRJ_ORG_PROJECT_NORTH_TXT     "12.5° E of true north"
  PRJ_ORG_GROUND_LEVEL_TXT      "+114.250 m AOD"
  PRJ_ORG_SECURITY_CLASS_TXT    "OFFICIAL"

GROUP B — Sheet identity (sheet instance scope)
  STING_SHEET_VOLUME_TXT        "ZZ"
  STING_SHEET_LEVEL_TXT         "01"
  STING_SHEET_TYPE_TXT          "DR"
  STING_SHEET_ROLE_TXT          "A"  (A/S/M/E/P/FP/LV/G)
  STING_SHEET_SEQ_TXT           "0001"
  STING_SHEET_FULL_REF_TXT      (calculated — concat of above)
  STING_SHEET_OF_TOTAL_TXT      "03 / 12"
  STING_SUITABILITY_TXT         "S2"
  STING_SUITABILITY_DESC_TXT    "Shared, Non-contractual"
  STING_STATUS_TXT              "Shared"
  STING_ISSUE_PURPOSE_TXT       "IFC — Issued for Construction"
  STING_REV_TXT                 "P02"
  STING_REV_DATE_TXT            (date)
  STING_TRANSMITTAL_REF_TXT     "TR-0042"
  STING_CDE_PATH_TXT            "Shared / Architectural"
  STING_DELIVERABLE_ID_TXT      "MIDP-A-2026-104-0017"
  STING_FEDERATION_STATUS_TXT   "Federated v.4"
  STING_LOIN_LOD_TXT            "LOD 300"

GROUP C — Authoring (sheet instance scope, role-driven)
  STING_DRAWN_BY_TXT            (3-letter initials)
  STING_DRAWN_DATE_TXT
  STING_CHECKED_BY_TXT
  STING_CHECKED_DATE_TXT
  STING_APPROVED_BY_TXT
  STING_APPROVED_DATE_TXT
  STING_AUTHORISED_BY_TXT
  STING_AUTHORISED_DATE_TXT

GROUP D — Statutory / Safety
  STING_CDM_HAZARD_TXT          (free text)
  STING_FIRE_DESIGN_REF_TXT
  STING_ASBESTOS_REGISTER_TXT
  STING_DISCLAIMER_TXT          (boilerplate)
  STING_COPYRIGHT_TXT           "© 2026 Planscape Limited"
  STING_DO_NOT_SCALE_TXT        "Do not scale from this drawing"
  STING_ORIGINAL_SIZE_TXT       "Original size: A1"
```

Every family below references this universe — the layout differs but
the parameter set is uniform.

## 3. Working sheet — A1 v2.0 (revised after v1.0 review)

A1 landscape, 841 × 594 mm. **Title strip on the bottom edge only**
(NOT right + bottom as the earlier proposal suggested) — preserves
the maximum drawable area, matches the existing v1.0 design intent,
follows BS 1192 Annex A and matches the practice habits operators
already use.

Strip height: ~110 mm in BIM mode, ~70 mm when BIM mode toggled
off (3 rows of BIM-only cells collapse). Drawable zone:
**830 × 480 mm in BIM mode, 830 × 520 mm in non-BIM mode** — both
well above the v1.0's ~420 mm height with the right-strip proposal.

**Typo fixes carried over from v1.0**: `ARCHITECH → ARCHITECT`,
`CONTRUCTOR → CONTRACTOR`, `PAPAR SIZE → PAPER SIZE`.

```
┌───────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                       │
│                                                                                       │
│                                                                                       │
│                                                                                       │
│                                  DRAWABLE ZONE                                        │
│                                  830 × 480 mm  (BIM on)                               │
│                                  830 × 520 mm  (BIM off)                              │
│                                                                                       │
│                                                                                       │
│                                                                                       │
├───────┬──────────────┬──────────────────────────────┬────────────────────┬────────────┤
│       │              │                              │  ─── BIM ONLY ───  │            │
│       │              │                              │ STAT │SUIT│REV│DATE│            │
│       ├──────────────┼──────────────────────────────┼──────┬──────┬─────┬┴──┬─────────┤
│CLIENT │ NOTES        │ DRAWING TITLE                │ DATE │PAPER │SCAL │   │         │
│       │              │ ${SHEET_NAME}                │      │ SIZE │     │   │         │
│       │              │                              ├──────┼──────┼─────┤   │         │
├───────┤              │                              │ DRWN │ CHKD │ APPR│   │ Rev     │
│PROJ.  │              │                              │      │      │     │   │ Hist.   │
│       │              │                              ├──────┴──────┴─────┴───┤ (8 cols │
│       │              │                              │  ─── BIM ONLY ───     │ in BIM, │
│       │              │                              │ PRJ│ORIG│VOL│LVL│TYPE│ 5 cols  │
├───────┤              │                              │ ROL │SEQ              │ in non- │
│LEAD/  │              │                              ├───────┬──────────────┤ BIM     │
│ARCH   │              │                              │ STATUS│ SHEET ──▶    │ mode)   │
│STRUCT.│              │                              │       │ ${OF_TOTAL}  │         │
│MEP    │              │                              │       │              ├─────────┤
│CONTR. │              │                              │ DRG NO│ ${SHEET_FULL_REF}      │ ← BIM
│       │              │ DRAWING TITLE.               │       │ STG-PLNS-…-0001        │   7-seg
├───────┴──────────────┴──────────────────────────────┴───────┴────────────────────────┤
│ ─── BIM ONLY ─── (entire strip hides in non-BIM mode)                                 │
│ TR-REF ${TRANSMITTAL_REF}   CDE ${CDE_PATH}   MIDP ${DELIVERABLE_ID}                  │
│ LOIN/LOD ${LOIN_LOD}        FED ${FEDERATION_STATUS}     [QR: planscape://…]          │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

**Cell map** (left → right along the bottom strip, BIM mode on):

| # | Cell | Width × Height (mm) | BIM only? | Bound parameter |
|---:|---|---|:---:|---|
|  1 | CLIENT          | 90 × 18  | | `${CLIENT_NAME}` |
|  2 | PROJECT         | 90 × 18  | | `${PROJECT_NAME}` |
|  3 | LEAD / ARCHITECT| 90 × 14  | | `${LEAD_ARCH_TXT}` |
|  4 | STRUCTURAL      | 90 × 14  | | `${STRUCT_TXT}` |
|  5 | MEP             | 90 × 14  | | `${MEP_TXT}` |
|  6 | CONTRACTOR      | 90 × 14  | | `${CONTRACTOR_TXT}` |
|  7 | NOTES (free text) | 200 × 70 | | `${NOTES_TXT}` |
|  8 | DRAWING TITLE   | 200 × 70 | | `${SHEET_NAME}` |
|  9 | STATUS chip     | 30 × 6   | | `${STATUS}` |
| 10 | SUITABILITY chip | 35 × 6  | ✓ | `${SUITABILITY}` |
| 11 | REV chip        | 25 × 6   | | `${REV}` |
| 12 | REV-DATE chip   | 30 × 6   | | `${REV_DATE}` |
| 13 | DATE            | 35 × 14  | | (Sheet Issue Date) |
| 14 | PAPER SIZE      | 30 × 14  | | (computed from family) |
| 15 | SCALE           | 35 × 14  | | `${SCALE}` |
| 16 | DRAWN BY        | 35 × 14  | | `${DR}` |
| 17 | CHECKED BY      | 30 × 14  | | `${CK}` |
| 18 | APPROVED BY     | 35 × 14  | | `${AP}` |
| 19 | AUTHORISED BY (4-eyes) | 35 × 14 | ✓ | `${AU}` |
| 20 | 7-segment ID breakdown row | 100 × 8 | ✓ | 7 cells PRJ/ORIG/VOL/LVL/TYPE/ROL/SEQ |
| 21 | DRG STATUS      | 35 × 18  | | `${STATUS}` |
| 22 | SHEET locator (chevron) | 65 × 18 | | `${OF_TOTAL}` (e.g. `03 / 12`) |
| 23 | DRG NO. (label) | 35 × 22  | | (static text) |
| 24 | `${SHEET_FULL_REF}` (the BIG cell) | 65 × 22 | partial | When BIM on: 7-seg concat. When off: plain `${Sheet Number}` |
| 25 | TR-REF row      | 250 × 6  | ✓ | `TR-REF ${TRANSMITTAL_REF}   CDE ${CDE_PATH}   MIDP ${DELIVERABLE_ID}` |
| 26 | LOIN/LOD/FED row| 250 × 6  | ✓ | `LOIN/LOD ${LOIN_LOD}   FED ${FEDERATION_STATUS}` |
| 27 | QR code         | 25 × 25  | ✓ | encoded sheet GUID |
| 28 | Revision history strip | full width × 18 | partial | 8 cols in BIM, 5 cols in non-BIM (SUIT + STAT cols hide) |

Total cells: **28**, of which **12 are BIM-only** and collapse when
`STING_BIM_MODE_BOOL = 0`.

### 3.1 BIM-mode toggle

A single instance parameter drives BIM-cell visibility:

```
STING_BIM_MODE_BOOL    (Yes/No, Instance, default = 1 / on)
```

**Important behaviour note: Revit's `Visible` parameter only hides
the element — it does NOT reflow the surrounding layout.** Hiding a
cell leaves an empty rectangle where it used to be unless the
surrounding geometry is parameterised to grow into it. There are
three strategies; we use a hybrid of two.

#### 3.1.1 The three strategies

| Strategy | Mechanism | Pro | Con |
|---|---|---|---|
| **A — Hide only** | Cell `Visible = STING_BIM_MODE_BOOL` | Simplest | Empty white rectangles remain |
| **B — Parametric strip + reflow** | Outer strip rectangle's top edge constrained to a reference plane whose offset is `if(BIM, 110, 70)` mm; row groups have their own formula-driven heights | Strip auto-collapses, drawable zone grows | 3-4× authoring complexity |
| **C — Two family types** | "BIM" / "non-BIM" types each carry a polished layout | Each type visually clean | Every fix made twice, types drift apart |

#### 3.1.2 Recommended hybrid (B for whole rows, A for in-row cells)

| Cell category | Strategy applied | When BIM = 0 |
|---|---|---|
| **Whole-row BIM blocks** — 7-seg ID row (#20), TR-REF/CDE/MIDP row (#25), LOIN/FED/QR row (#26-27) | **B — parametric reflow** via reference-plane offset formula | Row collapses to 0 height; strip top edge moves down 40 mm; drawable zone grows |
| **In-row BIM cells** — SUITABILITY chip (#10), AUTHORISED BY (#19), revision-history SUIT/STAT columns (last 2 cols of #28) | **A — hide only** | Cell becomes invisible; small empty space remains within a row that contains other always-on cells. Visually acceptable |
| **`${SHEET_FULL_REF}` big cell (#24)** | **Two-label trick** — Label A bound to `STING_SHEET_FULL_REF_TXT`, Label B bound to Revit `Sheet Number`, both at the same XYZ with reciprocal `Visible` bindings | Label A hides, Label B becomes visible. Cell rectangle stays the same size; only the displayed text changes from `STG-PLNS-…-0001` to `A-001` |

#### 3.1.3 The `${SHEET_FULL_REF}` two-label trick

You **cannot** switch a label's bound parameter dynamically in
Revit. The workable pattern is to place TWO labels in exactly the
same cell:

```
Label A:  bound to STING_SHEET_FULL_REF_TXT,  Visible = STING_BIM_MODE_BOOL
Label B:  bound to <Sheet Number> (Revit built-in), Visible = NOT STING_BIM_MODE_BOOL
```

Only one renders at a time. Same approach for the revision-history
SUIT and STAT columns (entire columns of labels with `Visible =
STING_BIM_MODE_BOOL`).

#### 3.1.4 Net visual effect when BIM mode toggles OFF

- 3 entire rows collapse via Strategy B → strip height drops from
  ~110 mm to ~70 mm → drawable zone grows from 830 × 480 mm to
  830 × 520 mm.
- 3 in-row cells go invisible via Strategy A — small empty
  rectangles in rows that still carry their other always-on cells.
  Acceptable.
- 2 cells switch their displayed text via the two-label trick
  (`SHEET_FULL_REF` cell + revision-history rows) — same rectangle,
  different content.

Switch is one click on the sheet's instance properties. No
rebuilds, no family swap.

#### 3.1.5 Why this hybrid beats Strategy C (two family types)

| Concern | Hybrid (single .rfa, parametric) | Two family types |
|---|---|---|
| Source of truth | one `.rfa`, one JSON spec | two layouts diverge over time |
| Per-sheet flexibility | toggle parameter per sheet | type-switch per sheet (heavier) |
| Authoring effort | ~30 % more than Strategy A alone | 2× (full layout per type) |
| Drift over time | none (single layout source) | high (every fix made twice) |
| Visual cleanliness in non-BIM | excellent (rows collapse, in-row cells leave small gaps) | excellent (each type polished) |
| Generator code complexity | adds reference planes + formulas (~80 lines) | duplicates the spec per type |

#### 3.1.6 Generator authoring cost (Phase 170+)

Strategy A alone: ~5 lines of code per BIM cell to bind the
`Visible` parameter.

Strategy B for whole rows: per row, ~15 lines — place reference
plane via `famDoc.FamilyCreate.NewReferencePlane`, lock dimension
via `Dimension.Create` between two reference planes, set formula
via `FamilyManager.SetFormula(lengthParam, "if(STING_BIM_MODE_BOOL,
110, 70)")`. Three rows → ~45 lines.

Two-label trick for `SHEET_FULL_REF`: ~10 lines (two `NewLabel`
calls at same XYZ with reciprocal `Visible` bindings).

Total generator overhead vs. Strategy A: ~80 lines. Worth it for
the visual cleanliness — empty white blocks in non-BIM mode look
unfinished.

### 3.2 Slot list (drawing-type slots, unchanged)

| label | NormX | NormY | NormW | NormH | Use |
|---|---|---|---|---|---|
| `MAIN`   | 0.00 | 0.00 | 1.00 | 1.00 | full drawable zone |
| `MAIN_LEFT` | 0.00 | 0.00 | 0.66 | 1.00 | plan + side detail |
| `MAIN_RIGHT_TOP` | 0.66 | 0.00 | 0.34 | 0.50 | section / 3D |
| `MAIN_RIGHT_BOT` | 0.66 | 0.50 | 0.34 | 0.50 | key plan / detail |
| `FOUR_UP_TL` | 0.00 | 0.00 | 0.50 | 0.50 | quarter |
| `FOUR_UP_TR` | 0.50 | 0.00 | 0.50 | 0.50 | quarter |
| `FOUR_UP_BL` | 0.00 | 0.50 | 0.50 | 0.50 | quarter |
| `FOUR_UP_BR` | 0.50 | 0.50 | 0.50 | 0.50 | quarter |

Slots resolve against the drawable zone — the zone shrinks/grows
when BIM mode toggles, so absolute slot mm values follow
automatically.

### 3.3 Bound parameter cells

**28 bound cells**, of which **16 always-visible** and **12
BIM-only**. Every label that begins with `${…}` is a `Label` element
bound by GUID to the matching shared parameter in §2.1. Cells
without a binding shown (the static labels CLIENT / PROJECT /
NOTES / DRAWING TITLE / DRWN / etc.) are static text on the
title-block layer.

## 4. Working sheet — A0 / A2 / A3 / A3 portrait / A4 portrait

Same layout family as A1 v2.0, scaled isometrically. Slot table
unchanged. Right-strip width scales:

| Family | Sheet size | Right strip | Drawable zone |
|---|---|---|---|
| `STING_TB_A0_v1.0`        | 1189 × 841 | 220 mm | 950 × 720 |
| `STING_TB_A1_v2.0`        | 841 × 594  | 200 mm | 620 × 470 |
| `STING_TB_A2_v1.0`        | 594 × 420  | 170 mm | 405 × 320 |
| `STING_TB_A3_v1.0`        | 420 × 297  | 130 mm | 270 × 220 |
| `STING_TB_A3_PORT_v1.0`   | 297 × 420  | 130 mm (bottom)| 270 × 250 |
| `STING_TB_A4_PORT_v1.0`   | 210 × 297  | 100 mm (bottom)| 195 × 175 |

A3 portrait + A4 portrait collapse the right strip to a **bottom
strip**. The status band on the top edge stays full-width on every
size. A4 portrait drops the QR + revision-history strip onto a
second page (overflow handled by `SheetTemplateEngine`).


## 5. Fabrication / shop drawing — `STING_TB_ASSEMBLY_PIPE/DUCT/COND/HANGER`

A1 landscape, BOM strip on the right (200 mm wide), title strip on
the bottom (80 mm tall). Replaces the partial stub already specced
in `Families/AssemblyTitleBlocks/`.

```
┌────────────────────────────────────────────────────────────────────────┬──────────────────────────────┐
│ ▒▒▒▒▒▒ STATUS ▒▒▒ SUITABILITY ▒▒▒▒▒▒▒ FAB STATUS ▒▒▒ FAB LOC ▒▒▒▒▒▒▒▒▒  │ BOM SCHEDULE                 │
│ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  │ ┌──┬─────────────┬────┬────┐ │
├────────────────────────────────────────────────────────────────────────┤ │# │ DESCRIPTION │QTY │UOM │ │
│  ┌────────────────┐ ┌──────────────────┐                                │ ├──┼─────────────┼────┼────┤ │
│  │  PLAN VIEW     │ │  ISOMETRIC       │                                │ │1 │ Pipe DN50   │ 12 │ m  │ │
│  │  slot 1        │ │  slot 2 (3D)     │                                │ │2 │ Elbow 90°   │  4 │ no │ │
│  │                │ │                  │                                │ │3 │ Tee equal   │  2 │ no │ │
│  │                │ │                  │                                │ │..│             │    │    │ │
│  └────────────────┘ └──────────────────┘                                │ ├──┼─────────────┼────┼────┤ │
│  ┌────────────────┐ ┌──────────────────┐                                │ │  │ TOTALS      │ 18 │    │ │
│  │  ELEVATION 0°  │ │  ELEVATION 90°   │                                │ └──┴─────────────┴────┴────┘ │
│  │  slot 3        │ │  slot 4          │                                │                              │
│  │                │ │                  │                                │ WELD COUNT     ${WELD}       │
│  └────────────────┘ └──────────────────┘                                │ BOLT COUNT     ${BOLT}       │
│  ┌─────────────────────────────────────┐                                │ FLANGE COUNT   ${FLG}        │
│  │  AXONOMETRIC ISO6412                 │                                │ FITTING COUNT  ${FIT}        │
│  │  slot 5                              │                                │ SUPPORT COUNT  ${SUP}        │
│  │                                      │                                │ TOTAL LENGTH   ${LEN} mm     │
│  │                                      │                                │ INSULATION     ${INS} m²     │
│  │                                      │                                │ ASSY WEIGHT    ${WT} kg      │
│  └─────────────────────────────────────┘                                │ TEST PRESS.    ${TP} bar     │
├────────────────────────────────────────────────────────────────────────┴──────────────────────────────┤
│ SPOOL    ${SPOOL_NR}      DRAWING TITLE   ${SHEET_NAME}      QC INSPECTOR  ${QC}                       │
│ DISC     Pipe             FAB SEQUENCE    ${SEQ_NR}          BOM REV       ${BOM_REV}                  │
│ ┌────────────────────────────────────────────┬───────────────┬──────┬──────┬──────┬──────┬──────────┐  │
│ │  STG-PLNS-ZZ-XX-AS-M-${SPOOL_NR}            │  Sheet OF     │ DRWN │ CHKD │ APPR │ AUTH │  Rev     │  │
│ ├────────────────────────────────────────────┴───────────────┴──────┴──────┴──────┴──────┴──────────┤  │
│ │ REV │ DATE │ DESCRIPTION │ DRWN │ CHKD │ APPR │ SUIT │ STAT │  CDE ${CDE}   QR ▭ ${QR}            │  │
│ └────────────────────────────────────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Discipline variants share this layout — only the dropdowns the BOM
reads from change (pipe / duct / conduit / hanger). Slot list:

| label | NormX | NormY | NormW | NormH | Slot |
|---|---|---|---|---|---|
| `PLAN`   | 0.00 | 0.00 | 0.36 | 0.40 | 1 |
| `ISO`    | 0.36 | 0.00 | 0.36 | 0.40 | 2 |
| `ELEV0`  | 0.00 | 0.40 | 0.36 | 0.30 | 3 |
| `ELEV90` | 0.36 | 0.40 | 0.36 | 0.30 | 4 |
| `AXON`   | 0.00 | 0.70 | 0.72 | 0.30 | 5 |

## 6. Authority submission — `STING_TB_SUBMISSION_KCCA / ERA / NEMA`

A1 landscape, single drawable zone, full-width approval block at the
bottom. KCCA / ERA / NEMA each carry the authority's mandated stamp
area and approval matrix.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ┌──────────────┐                                                       ┌──────────────┐         │
│ │ AUTHORITY    │  ${AUTHORITY_NAME}                                    │ ORIGINATOR   │         │
│ │ LOGO         │  ${AUTHORITY_ADDRESS}                                 │ LOGO         │         │
│ └──────────────┘                                                       └──────────────┘         │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                                │
│                                  DRAWABLE ZONE                                                 │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ PROJECT REF      ${PROJECT_REF}        DISCIPLINE  ${DISC}      DRAWING NO  ${SHEET_FULL_REF}  │
│ APPLICANT        ${CLIENT_NAME}                                  REVISION    ${SUBMISSION_REV}  │
│ SITE LOCATION    ${PROJECT_ADDRESS}                              SUBMIT DATE ${SUBMISSION_DATE} │
├──────────────────────────────────────┬─────────────────────────────────────────────────────────┤
│ APPROVAL                             │  AUTHORITY STAMP AREA                                   │
│ ┌──────────┬─────────┬─────┬──────┐  │  (reserved 200 × 100 mm — wet stamp + signatures)       │
│ │   ROLE   │  NAME   │SIGN │ DATE │  │                                                         │
│ ├──────────┼─────────┼─────┼──────┤  │                                                         │
│ │ DESIGNER │${DRWN}  │     │      │  │                                                         │
│ │ CHECKER  │${CHKD}  │     │      │  │                                                         │
│ │ APPROVER │${APPR}  │     │      │  │                                                         │
│ │ AUTH'D   │${AUTH}  │     │      │  │                                                         │
│ └──────────┴─────────┴─────┴──────┘  │                                                         │
├──────────────────────────────────────┴─────────────────────────────────────────────────────────┤
│ Statutory notes: comply with ${AUTHORITY_REGS}. Original size A1. © Planscape Ltd.            │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

KCCA-specific addition: building permit number cell (top-right of
authority block). ERA-specific addition: licence-class cell. NEMA-
specific addition: EIA reference cell.

## 7. Presentation — `STING_TB_PRESENT_A1_v1.0`

A1 landscape full-bleed render. Title strip is a single 60 mm tall
caption bar **outside** the render area.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                              FULL-BLEED RENDER / 3D AXON                                       │
│                              No grid, no border on this zone                                   │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ${PROJECT_NAME}  /  ${SHEET_NAME}                                            ${SHEET_FULL_REF} │
│                                                                              ${SCALE} @ A1     │
│ ${ORG_LOGO}                                              ${CLIENT_LOGO}      ${REV}            │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Slot list: single `MAIN` slot covering 100 % of the sheet above the
caption bar. Caption bar is 60 mm tall; render zone is 534 mm tall ×
841 mm wide. `STING_TB_PRESENT_A1_MONO_v1.0` is identical except the
graphics are forced to halftone via the bound `corp-presentation-mono`
ViewStylePack (no separate family-side change).


## 8. Cover page — `STING_TB_COVER_A1_v1.0`

A1 landscape, project front. No drawable viewport — entirely
information design. Used as page 1 of every issue bundle.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                                │
│                                                                                                │
│                                                                                                │
│             ┌──────────────────────────┐    ┌──────────────────────────┐                        │
│             │                          │    │                          │                        │
│             │     ORIGINATOR LOGO       │    │       CLIENT LOGO        │                        │
│             │       ${ORG_LOGO}         │    │       ${CL_LOGO}          │                        │
│             │                          │    │                          │                        │
│             └──────────────────────────┘    └──────────────────────────┘                        │
│                                                                                                │
│                                                                                                │
│           ╔══════════════════════════════════════════════════════════════════════╗             │
│           ║                                                                      ║             │
│           ║                       ${PROJECT_NAME}                                ║             │
│           ║                                                                      ║             │
│           ║                       ${PROJECT_ADDRESS}                             ║             │
│           ║                                                                      ║             │
│           ╚══════════════════════════════════════════════════════════════════════╝             │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│           ┌────────────────────────────────────────────────┐                                    │
│           │  Information Container Issue                   │                                    │
│           ├────────────────────────────────────────────────┤                                    │
│           │  Suitability      ${SUITABILITY} — ${SUIT_DESC}│                                    │
│           │  Issue purpose    ${ISSUE_PURPOSE}             │                                    │
│           │  Revision         ${REV}                       │                                    │
│           │  Issue date       ${REV_DATE}                  │                                    │
│           │  Transmittal      ${TRANSMITTAL_REF}           │                                    │
│           │  Deliverable      ${DELIVERABLE_ID}            │                                    │
│           │  RIBA stage       ${RIBA_STAGE}                │                                    │
│           │  CDE container    ${CDE_PATH}                  │                                    │
│           │  LOIN / LOD       ${LOIN_LOD}                  │                                    │
│           └────────────────────────────────────────────────┘                                    │
│                                                                                                │
│                                                                                                │
│           Project no. ${PROJECT_NUMBER}                                                        │
│           Originator  ${ORG_NAME} (${ORG_CODE})                                                │
│           Appointing  ${APPOINTING_PARTY}                                                       │
│           Lead        ${LEAD_APPOINTED_PARTY}                                                   │
│                                                                                                │
│                                                                                                │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Statutory  CDM / fire / asbestos as per individual sheets.   © ${COPYRIGHT}.   Original A1.    │
│            ${SECURITY_CLASS} — restricted distribution per CDE rules.                          │
│ Sheet ${SHEET_FULL_REF} — Cover                                            ${OF_TOTAL}         │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

## 9. Section divider — `STING_TB_DIVIDER_A1_v1.0`

A1 landscape, between disciplines in a multi-discipline bundle. One
big discipline title + a clickable index of the sheets that follow.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                                │
│                                                                                                │
│                                                                                                │
│                                                                                                │
│              ╔══════════════════════════════════════════════════════════════════╗              │
│              ║                                                                  ║              │
│              ║                   DISCIPLINE: ${DISC_NAME}                       ║              │
│              ║                                                                  ║              │
│              ║                   (Architectural / Structural / Mechanical /     ║              │
│              ║                    Electrical / Plumbing / Fire Protection /     ║              │
│              ║                    Comms+LV / Site)                              ║              │
│              ║                                                                  ║              │
│              ╚══════════════════════════════════════════════════════════════════╝              │
│                                                                                                │
│                                                                                                │
│              ┌──────────────────────────────────────────────────────────────────┐              │
│              │ Sheet Index — Discipline ${DISC}                                 │              │
│              ├──────────────────────────────────────────────────────────────────┤              │
│              │ ${SHEET_FULL_REF_1}    ${SHEET_NAME_1}                  ${REV_1} │              │
│              │ ${SHEET_FULL_REF_2}    ${SHEET_NAME_2}                  ${REV_2} │              │
│              │ ${SHEET_FULL_REF_3}    ${SHEET_NAME_3}                  ${REV_3} │              │
│              │ …                                                                │              │
│              └──────────────────────────────────────────────────────────────────┘              │
│                                                                                                │
│                                                                                                │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ${PROJECT_NAME}                                                       ${SHEET_FULL_REF}        │
│ ${ORG_NAME}                                                           ${REV}    ${REV_DATE}    │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

The sheet-index table is generated by
`Docs.SheetTemplateCommands.ExportSheetRegisterCommand` then placed
as a Revit `RevisionSchedule`-style schedule in this slot.

## 10. Drawing register — `STING_TB_REGISTER_A1_v1.0`

A1 landscape. The full sheet is a Revit Sheet List schedule. Title
block carries only the project ID + register-as-of date.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ DRAWING REGISTER                                                          ${SHEET_FULL_REF}     │
│ ${PROJECT_NAME}                              As of ${REV_DATE}            ${REV}    ${OF_TOTAL}│
├────┬────────────────┬───────────────────┬────────┬──────────┬──────┬──────┬──────┬─────────────┤
│ #  │ Sheet number   │ Sheet name        │ Disc.  │ RIBA     │ Suit │ Stat │ Rev  │ Issued date │
├────┼────────────────┼───────────────────┼────────┼──────────┼──────┼──────┼──────┼─────────────┤
│ 01 │ STG-PLNS-ZZ-XX-DR-A-0001 │ Site Plan         │ A      │ Stage 4  │ S2   │ Shar │ P02  │ 2026-04-15  │
│ 02 │ STG-PLNS-ZZ-XX-DR-A-0002 │ Ground Floor Plan │ A      │ Stage 4  │ S2   │ Shar │ P02  │ 2026-04-15  │
│ 03 │ STG-PLNS-ZZ-XX-DR-A-0003 │ First Floor Plan  │ A      │ Stage 4  │ S2   │ Shar │ P01  │ 2026-04-01  │
│ …  │                │                   │        │          │      │      │      │             │
├────┴────────────────┴───────────────────┴────────┴──────────┴──────┴──────┴──────┴─────────────┤
│ ${ORG_NAME}    Originator ${ORG_CODE}                                                           │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

The schedule rows are populated automatically by
`BIMManagerEngine.GenerateDrawingRegister` running over every sheet
in the project. Filtering and grouping by discipline handled by the
schedule's own sorting/grouping fields.

## 11. Transmittal cover — `STING_TB_TRANSMITTAL_A4_v1.0`

A4 portrait. Single-page cover that accompanies an issue bundle.

```
┌──────────────────────────────────────────────┐
│ ${ORG_LOGO}                  ${CLIENT_LOGO}  │
├──────────────────────────────────────────────┤
│                                              │
│       TRANSMITTAL  ${TRANSMITTAL_REF}        │
│                                              │
│       ${PROJECT_NAME}                        │
│       Project no. ${PROJECT_NUMBER}          │
│                                              │
├──────────────────────────────────────────────┤
│ FROM                                          │
│   ${ORG_NAME}                                │
│   ${ORG_ADDRESS}                             │
│   Issued by ${APPROVED_BY}                   │
│                                              │
│ TO                                            │
│   ${RECIPIENT_NAME}                          │
│   ${RECIPIENT_ROLE}                          │
│   ${RECIPIENT_EMAIL}                         │
├──────────────────────────────────────────────┤
│ ISSUE PURPOSE                                 │
│   ${ISSUE_PURPOSE}                           │
│ SUITABILITY    ${SUITABILITY} — ${SUIT_DESC} │
│ STATUS         ${STATUS}                     │
│ REVISION       ${REV}                        │
│ ISSUE DATE     ${REV_DATE}                   │
│ DELIVERABLE    ${DELIVERABLE_ID}             │
├──────────────────────────────────────────────┤
│ ENCLOSED                                     │
│ ┌──┬───────────────────┬──────────┬───────┐  │
│ │# │ Sheet ref         │ Title    │ Rev   │  │
│ ├──┼───────────────────┼──────────┼───────┤  │
│ │ 1│ STG-PLNS-ZZ-XX-…  │ …        │ P02   │  │
│ │..│                   │          │       │  │
│ └──┴───────────────────┴──────────┴───────┘  │
├──────────────────────────────────────────────┤
│ ACKNOWLEDGEMENT                              │
│  Received by ___________________  date ____  │
│  Signature   ___________________             │
│                                              │
│  Please acknowledge receipt within 24 h.     │
├──────────────────────────────────────────────┤
│ ${COPYRIGHT}              ${SHEET_FULL_REF}  │
└──────────────────────────────────────────────┘
```

Wired to `Planscape.Docs.Templates.TransmittalOrchestrator` —
`TR-NNNN` ID minted by `DocumentIdentityGenerator`, recipient list
pulled from the deliverable's distribution group, enclosed-sheet
table fed by the bundle manifest.

## 12. Clarification / RFI sketch — `STING_TB_CLARIFICATION_A3_v1.0`

A3 landscape. Single sketch zone on the left, RFI Q&A panel on the
right.

```
┌─────────────────────────────────────────────┬─────────────────────────────────────┐
│ ▒▒▒▒ STATUS ▒▒▒▒▒▒▒▒ SUITABILITY ▒▒▒▒▒▒▒▒▒  │ RFI / CLARIFICATION                 │
├─────────────────────────────────────────────┤ ┌─────────────────────────────────┐ │
│                                              │ │ RFI no.  ${RFI_NR}              │ │
│                                              │ │ Subject  ${RFI_SUBJECT}         │ │
│                                              │ │ Raised   ${RAISED_DATE} by ${BY}│ │
│                                              │ │ Required ${RESPONSE_BY_DATE}    │ │
│                                              │ ├─────────────────────────────────┤ │
│                  SKETCH AREA                 │ │ Question                        │ │
│                  (one slot, free)            │ │                                 │ │
│                                              │ │ ${QUESTION_TEXT}                │ │
│                                              │ │                                 │ │
│                                              │ ├─────────────────────────────────┤ │
│                                              │ │ Response  by ${RESPONDER}       │ │
│                                              │ │                                 │ │
│                                              │ │ ${RESPONSE_TEXT}                │ │
│                                              │ │                                 │ │
│                                              │ ├─────────────────────────────────┤ │
│                                              │ │ Cost impact   ${COST_IMPACT}    │ │
│                                              │ │ Time impact   ${TIME_IMPACT}    │ │
│                                              │ └─────────────────────────────────┘ │
├─────────────────────────────────────────────┴─────────────────────────────────────┤
│ ${PROJECT_NAME}                                          ${SHEET_FULL_REF}        │
│ ${ORG_NAME}                                              ${REV}  ${REV_DATE}      │
└───────────────────────────────────────────────────────────────────────────────────┘
```

Drives `Tags/RichTagDisplayCommands.SegmentNoteCommand` outputs +
`BIMManager.IssueWizard` BCF round-trip.

## 13. Author workflow — building the families

Per family, Revit Family Editor session:

1. **Start template.** `New > Family > Title Block > A1 Metric.rft`
   (or the matching paper size). Save to
   `Families/AssemblyTitleBlocks/<filename>.rfa`.
2. **Bind shared parameters.** Load the §2.1 universe from
   `StingTools/Data/MR_PARAMETERS.txt` via
   `Manage > Shared Parameters`. Add each parameter to the family as
   a **Title Block instance parameter** (so each sheet can hold a
   different value).
3. **Lay out the static cells.** Draw lines / rectangles per the box
   diagrams in §3–§12. Use line-weight 4 (0.25 mm) for cell borders,
   weight 5 (0.35 mm) for primary outlines, weight 7 (0.70 mm) for
   the sheet outer border.
4. **Place the dynamic labels.** For every `${PARAM}` placeholder in
   the diagram, place a `Label` element bound to that shared param.
   Set `Sample Value` to a meaningful default so the family loads
   visually correct in a fresh project.
5. **Place the Revit built-in labels.** `Sheet Number`,
   `Sheet Name`, `Current Revision`, `Current Revision Date`,
   `Drawn By`, `Checked By`, `Approved By`, `Project Name`,
   `Project Number`, `Project Address`, `Client Name`, `Sheet Issue
   Date` — Revit will populate these from the sheet's instance
   properties. Override binding to STING shared params (Group B + C
   in §2.1) by going `Properties > Edit Type > Calculated` if
   needed.
6. **Add the revision schedule.** `View > Revisions > Revision
   Schedule` inside the family — drop into the bottom-edge strip,
   set 8 columns per §1.3 H.
7. **Add detail-item placeholders for North arrow + key plan + QR.**
   Use empty detail family slots; production-side
   `BatchPlanCommand` will replace them with real annotations.
8. **Save + load into project.** `Load into Project` from the family
   editor. Add the loaded symbol to `STING_DRAWING_TYPES.json` under
   the relevant DrawingType's `titleBlockFamily` field.
9. **Wire the param applier.** For each `DrawingType`, populate
   `TitleBlockParams` (Dictionary<string, string>) with the per-cell
   bindings:
   ```json
   {
     "PRJ_ORG_PROJECT_CODE_TXT":   "${PRJ_ORG_PROJECT_CODE}",
     "STING_SHEET_FULL_REF_TXT":   "${PRJ_ORG_PROJECT_CODE}-${PRJ_ORG_ORIGINATOR_CODE}-{vol}-{lvl}-{type}-{disc}-{seq:D4}",
     "STING_REV_DATE_TXT":         "{today}",
     "STING_TRANSMITTAL_REF_TXT":  "${transmittal_ref}",
     "STING_CDM_HAZARD_TXT":       "${PRJ_ORG_CDM_HAZARD}"
   }
   ```
10. **Verify in Revit.** Create a sheet from the new title block,
    confirm every `${param}` resolves; if any cell still shows the
    sample value, the binding key is wrong.

### 13.1 What the existing applier already supports

`StingTools/Core/Drawing/TitleBlockParamApplier.cs` (Phase 138 bonus
4) honours the spec described above. Nothing in the engine needs to
change for the new families — the gap is purely the `.rfa` files
themselves. Per family:

- 35–50 bound labels (working sheet, A0–A4)
- 18 bound labels (fab / shop drawing)
- 14 bound labels (authority submission)
- 9 bound labels (presentation)
- 22 bound labels (cover page)
- 10 bound labels + sheet-list schedule (divider)
- 4 bound labels + sheet-list schedule (register)
- 24 bound labels (transmittal A4)
- 14 bound labels (clarification A3)

### 13.2 Suitability chip — colour-by-filter

The orange chip on the supplied PDF should be **driven by a filter**,
not coloured statically. Add to `STING_AEC_FILTERS.json` (Phase 166):

```json
{
  "id": "STING - Suitability S0-S2",
  "rule": { "param": "STING_SUITABILITY", "kind": "shared", "op": "in",
            "value": ["S0","S1","S2"] },
  "defaultOverride": { "surfaceFgPattern": "Solid",
                       "surfaceFgColor": "#A0A0A0" }
},
{
  "id": "STING - Suitability S3-S4",
  "rule": { "param": "STING_SUITABILITY", "kind": "shared", "op": "in",
            "value": ["S3","S4"] },
  "defaultOverride": { "surfaceFgPattern": "Solid",
                       "surfaceFgColor": "#FF8C00" }
},
{
  "id": "STING - Suitability S6-S7",
  "rule": { "param": "STING_SUITABILITY", "kind": "shared", "op": "in",
            "value": ["S5","S6","S7"] },
  "defaultOverride": { "surfaceFgPattern": "Solid",
                       "surfaceFgColor": "#2E7D32" }
}
```

The chip becomes a `FilledRegion` on the title block carrying these
filters. The fill colour switches automatically when the sheet's
`STING_SUITABILITY` parameter is updated through any Phase 167 +
Phase 166 path.

## 14. Verification plan

A reviewer running the families in Revit should confirm:

1. **A1 v2.0 replaces v1.0 on every existing sheet.** `Insert > Load
   Family` over-writes the old symbol; Revit retains all sheet
   numbers + revisions.
2. **All 35 cells on the working sheet resolve.** Open a sheet in a
   project that has `ProjectInformation.PRJ_ORG_*` populated; no
   cell should show its sample value.
3. **Sheet ID concat.** Edit one of the seven segments
   (`STING_SHEET_VOLUME_TXT` etc.) — the prominent
   `STING_SHEET_FULL_REF_TXT` cell should recompute via the formula.
4. **Suitability chip changes colour** when
   `STING_SUITABILITY_TXT` flips from S2 to S4 to S6.
5. **Revision schedule populates** when a revision is added via
   `Manage > Revisions`.
6. **QR code shows the project + sheet GUID.** Scan with the
   Planscape mobile app — the deep-link should open the sheet
   record.
7. **Cover / divider / register sheets generate** via
   `BIMManagerEngine.GenerateProjectCover`,
   `GenerateDisciplineDivider`, `GenerateDrawingRegister`.
8. **Transmittal A4 produces** through
   `TransmittalOrchestrator.Create` — recipient table populated,
   enclosed-sheet table populated.
9. **Authority submission stamps** print at correct size for KCCA /
   ERA / NEMA — check against authority sample sheets.
10. **Fabrication BOM populates** for a generated assembly via
    `Fabrication_GeneratePackage` — counts match
    `AssyParams.WeldCount` etc.

## 15. References

- ISO 19650-1:2018, ISO 19650-2:2018 — information container
  identification + suitability codes.
- BS EN ISO 7200:2004 — title block field requirements.
- BS EN ISO 5457:1999 — drawing sheet sizes + foldout layout.
- BS 1192:2007+A2:2016 — UK CAD layering conventions.
- NBS BIM Toolkit — Uniclass 2015 classification.
- UK BIM Framework Information Container Guide (2020 edition).
- BIMForum LOD Specification 2024 — LOIN / LOD note format.
- CDM 2015 reg 9 — designer hazard notes on drawings.
- BS 8536-1 — operational handover information.
- Building Safety Act 2022 — fire-safety design intent reference.
- RIBA Plan of Work 2020 — stage codes 0–7.
- KCCA / ERA / NEMA submission guides — authority-specific stamp
  areas + approval matrices (jurisdictional).
