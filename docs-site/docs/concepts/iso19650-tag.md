# ISO 19650 tag format

Every taggable element in a Planscape-managed project carries an 8-segment ISO 19650 tag. The tag is the primary key that links the Revit model, the cloud database, the mobile app, the document register, and the audit trail. Get the tag right and the rest follows.

## The 8 segments

```
DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ
```

| Segment | Parameter | Examples | Description |
|---|---|---|---|
| DISC | `ASS_DISCIPLINE_COD_TXT` | `M`, `E`, `P`, `A`, `S` | Discipline (Mechanical, Electrical, Plumbing, Architectural, Structural) |
| LOC | `ASS_LOC_TXT` | `BLD1`, `BLD2`, `EXT` | Location / building code |
| ZONE | `ASS_ZONE_TXT` | `Z01`, `Z02`, `Z03` | Zone within the location |
| LVL | `ASS_LVL_COD_TXT` | `L01`, `GF`, `B1`, `RF` | Level (storey) code |
| SYS | `ASS_SYSTEM_TYPE_TXT` | `HVAC`, `DCW`, `LV`, `SAN` | System type (CIBSE / Uniclass 2015) |
| FUNC | `ASS_FUNC_TXT` | `SUP`, `HTG`, `PWR` | Function code (CIBSE / Uniclass 2015) |
| PROD | `ASS_PRODCT_COD_TXT` | `AHU`, `DB`, `DR` | Product code (Air Handling Unit, Distribution Board, Door, …) |
| SEQ | `ASS_SEQ_NUM_TXT` | `0001`, `0042` | Zero-padded 4-digit sequence number, unique within (DISC, SYS, LVL) group |

A complete tag therefore looks like: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`.

Read it back: *Mechanical, Building 1, Zone 01, Level 2, HVAC system, Supply function, Air Handling Unit number 3.* Anyone familiar with the CIBSE codes can read this in plain English.

## How Planscape derives the tokens

You don't type tokens manually. Planscape derives them from the Revit context:

| Token | Source |
|---|---|
| DISC | Element category + project default discipline |
| LOC | The Room the element sits in → its `ROM_LOC_TXT` parameter, falling back to project-level default |
| ZONE | The Room's department (or zone scheme parameter) |
| LVL | The Revit Level the element is associated with, mapped to a short code |
| SYS | The MEP system the element is connected to (HVAC, plumbing, electrical), or — for non-MEP — the element category |
| FUNC | The MEP system's function classification, or the category-level default |
| PROD | The element family name (matched against a known family→PROD-code lookup) |
| SEQ | Auto-assigned by walking existing tags in the same `(DISC, SYS, LVL)` group and incrementing the highest seen |

Run **STING Tools → TAGS → Family-Stage Populate** to fill all eight tokens on every element in one pass. Run **TAGS → Batch Tag** to assemble them into the 8-segment string and write to all 36 tag containers (one per discipline-specific format).

## What "complete" means

Planscape considers a tag complete when:

1. All 8 segments are non-empty.
2. The segment codes are valid (DISC ∈ `{M, E, P, A, S, FP, LV, G}`; SYS in the project's CIBSE/Uniclass list; etc.).
3. The element category matches the DISC and SYS values (an `OS_Doors` element with `SYS=HVAC` is flagged as a cross-validation error).
4. The SEQ is unique within its `(DISC, SYS, LVL)` group.

A tag is **stale** when the element's geometry, level, or room has changed since the tag was last assembled. Planscape's `StingStaleMarker` IUpdater watches for these changes and sets `STING_STALE_BOOL = 1`. Run **TAGS → Re-Tag Stale** to refresh stale elements without touching anything else.

## Tag containers — 36 in total

The 8-segment string is written to a primary container `ASS_TAG_1` plus 35 discipline-specific containers — `HVC_EQP_TAG`, `ELC_EQP_TAG`, `PLM_EQP_TAG`, and so on. This sounds redundant but it isn't: each tag family in your Revit template binds to a single container, so the same element can carry differently-formatted tags depending on which family is hosted.

`ASS_TAG_2` through `ASS_TAG_6` are partial tags (e.g. just `DISC-SYS-PROD-SEQ`) for compact annotation; `MAT_TAG_1` through `MAT_TAG_6` are equivalents for material tagging.

## TAG7 — the rich narrative

Where `ASS_TAG_1` is the machine-readable identifier, **TAG7** is the human-readable narrative. Stored across six sub-section parameters — `ASS_TAG_7A_TXT` through `ASS_TAG_7F_TXT` — TAG7 captures:

- **A** Identity header (asset name, manufacturer, model)
- **B** System & function (system type description, function code)
- **C** Spatial context (room, department, grid reference)
- **D** Lifecycle & status (status, revision, origin, maintenance schedule)
- **E** Technical specs (capacity, flow, voltage — discipline-specific)
- **F** Classification (Uniformat, OmniClass, keynote, cost reference)

A complete TAG7 looks like:

> **Trane CSAA021 AHU** | HVAC, Supply function | Plant Room L02, Grid C-3 | NEW, Rev A | 21 kW cooling, 2.4 m³/s | Uniformat D3050, Cost £18,450

TAG7 is what gets rendered on the equipment schedule and the O&M handover document. It's what site coordinators see when they QR-scan a piece of plant. It's what makes a tag *useful* rather than just *unique*.

## Reading a tag

Given a tag like `E-BLD1-Z03-L05-LV-PWR-DB-0012`:

- **E** — Electrical
- **BLD1** — Building 1
- **Z03** — Zone 3 (in this project, the East wing)
- **L05** — Level 5
- **LV** — Low Voltage system
- **PWR** — Power function (i.e. distribution, not lighting or comms)
- **DB** — Distribution Board
- **0012** — the 12th distribution board on Level 5 in the LV system

You'll find it on a real Revit element somewhere in the model, on the schedule, on the issued layout drawing, on the O&M manual page, and — if your firm uses physical labels — on a sticker on the actual board.

## Next steps

- [CDE state machine](cde.md) — how documents move through WIP / Shared / Published / Archived
- [Bulk-tag elements](../howto/bulk-tag.md) — for migrating an existing untagged model
