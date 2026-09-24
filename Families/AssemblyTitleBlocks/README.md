# STING v4 Assembly Title Blocks

Author the eight `.rfa` families described in `*.params.txt` files in
this folder. Each file lists the required Title Block parameters and
the layout slots that `ShopDrawingComposer` (S5.6) drops viewports into.

## Parameter-name convention (enforced)

Every name under a `... PARAMETERS` heading in a `.params.txt` is one of:

- **a registry shared parameter** — it exists as a `PARAM` row in
  `StingTools/Data/MR_PARAMETERS.txt` or `StingTools/Data/Parameters/*.txt`.
  Its GUID is the one in that row. Never type or invent a GUID: pick the
  parameter from the shared-parameter file in Family Types, so the GUID
  comes from the file by name.
- **`local:NAME`** — a family-local parameter (Family Types > New
  parameter > *Family parameter*). Used only where no registry parameter
  exists yet, or where code writes that exact literal name
  (`DISCIPLINE`, written by `ShopDrawingComposer`). A `local:` cell cannot
  be scheduled or tagged across families — provision a registry parameter
  and drop the prefix when one is needed.

`StingTools.Tags.Tests/AssemblyTitleBlockStubTests` parses every stub and
fails on any name that is neither in the parameter files nor marked
`local:` — the first drafts listed eight distinct names that existed
nowhere (one of them a detail item, not a parameter at all).

## Discipline-tagged shop drawing title blocks

| File | Used by | Discipline |
|---|---|---|
| `STING_TB_ASSEMBLY_PIPE.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` | Pipe / Plumbing |
| `STING_TB_ASSEMBLY_DUCT.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` | Duct / HVAC |
| `STING_TB_ASSEMBLY_COND.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` (and elec-spool-A1-1to50 fallback) | Electrical |
| `STING_TB_ASSEMBLY_ELEC.rfa`   | Optional bespoke variant for elec-spool-A1-1to50 — adds cable / glanding / IP-rating cells. **Stub only**; falls back to `STING_TB_ASSEMBLY_COND` until authored. | Electrical |
| `STING_TB_ASSEMBLY_HANGER.rfa` | Future hanger-only assemblies | Hanger |

## Authority submission title blocks

| File | Authority |
|---|---|
| `STING_TB_SUBMISSION_KCCA.rfa` | Kampala Capital City Authority |
| `STING_TB_SUBMISSION_ERA.rfa`  | Electricity Regulatory Authority |
| `STING_TB_SUBMISSION_NEMA.rfa` | National Environment Management Authority |

## Author workflow

1. Open `New Family > Title Block` in Revit
2. Choose A1 or A2 size per the spec in the matching `.params.txt`
3. Add each parameter listed under `REQUIRED PARAMETERS` per the
   convention above — registry names from the shared-parameter file
   (`MR_PARAMETERS.txt`, which includes the `ASS_*` fabrication set),
   `local:` names as family parameters (without the prefix)
4. Lay out the title strip per `LAYOUT NOTES`
5. Save into the project's title block library
6. Run `Generate Fabrication Package` from the Fabrication tab. On the
   first ViewSheet, `ShopDrawingComposer` writes `ASS_SPOOL_NR_TXT`,
   `ASS_WEIGHT_KG`, `ASS_FAB_LOC_TXT`, `ASS_FAB_STATUS_TXT`,
   `ASS_BOM_REV_TXT` and `DISCIPLINE` from the AssemblyInstance; the
   drawing type's `titleBlockParams` fill the rest it declares. Every
   other cell is left for the author / QC to fill.

## When the .rfa is missing

`ShopDrawingComposer.ResolveTitleBlock` falls back to the first
available title block in the project so the pipeline still produces
a sheet (with a generic title block) for QA validation. A
`StingLog.Warn` is emitted so the missing family is visible in the
log.
