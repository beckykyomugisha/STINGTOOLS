# STING v4 Assembly Title Blocks

Author the eight `.rfa` families described in `*.params.txt` files in
this folder. Each file lists the required Title Block instance
parameters (bound to the shared parameters in
`StingTools.Core.Fabrication.AssyParams`) and the layout slots that
`ShopDrawingComposer` (S5.6) drops viewports into.

## Discipline-tagged shop drawing title blocks

| File | Used by | Discipline |
|---|---|---|
| `STING_TB_ASSEMBLY_PIPE.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` | Pipe / Plumbing |
| `STING_TB_ASSEMBLY_DUCT.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` | Duct / HVAC |
| `STING_TB_ASSEMBLY_COND.rfa`   | `ShopDrawingComposer.ResolveTitleBlock` | Electrical |
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
3. Add each shared parameter listed under `REQUIRED PARAMETERS`,
   binding by GUID to `STING_PARAMS_V4.txt` (or any project param
   file that already includes them)
4. Lay out the title strip per `LAYOUT NOTES`
5. Save into the project's title block library
6. Run `Generate Fabrication Package` from the Fabrication tab; the
   first ViewSheet's title block instance parameters are populated
   automatically from the AssemblyInstance metadata

## When the .rfa is missing

`ShopDrawingComposer.ResolveTitleBlock` falls back to the first
available title block in the project so the pipeline still produces
a sheet (with a generic title block) for QA validation. A
`StingLog.Warn` is emitted so the missing family is visible in the
log.
