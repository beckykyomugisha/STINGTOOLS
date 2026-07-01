# Healthcare Room Data Sheet template (H-8)

`healthcare_rds.docx` in this folder is the MiniWord template consumed by
`RdsRenderer.Render()`. It is **generated**, not hand-authored — rebuild it with:

```
python tools/build_healthcare_rds_docx.py
```

The generator reads `StingTools/Data/HEALTHCARE_RDS_FIELDMAP.json` and self-tests
that every field-map token appears in the template (and vice-versa), so the two
never drift. Edit the field map (or the section layout / labels in the generator)
and re-run — do not edit the `.docx` binary directly.

## Runtime token contract

`RdsContextBuilder` reads the field map and `TokenContext.AsDictionary()` flattens
it the same way as every other template in this folder. The **actual** token
names in the `.docx` are therefore the *routed* forms, not the dotted source
names:

| Field-map source        | Bucket    | Token in the .docx        |
|-------------------------|-----------|---------------------------|
| `room.<x>`              | Doc       | `{{doc.<x>}}`             |
| `prj.<x>`               | Project   | `{{project.<x>}}`         |
| (builder-added)         | Doc       | `{{doc.generated_at}}`, `{{doc.generator}}` |

Examples: `{{doc.number}}`, `{{doc.name}}`, `{{doc.press.regime}}`,
`{{project.code}}`, `{{project.ae_vent}}`.

## Loops (MiniWord `{{foreach X}} … {{endforeach}}`)

Loop rows use MiniWord's native table `foreach`, with the item fields referenced
by **bare** name inside the row (identical to `transmittal.docx` /
`deliverable_standard.docx`). The four loops and their fields come from the
`loops` block of the field map:

- `services`   — `{{type}} {{count}} {{spec}} {{notes}}`
- `equipment`  — `{{prod_code}} {{description}} {{make_model}} {{qty}} {{notes}}`
- `finishes`   — `{{element}} {{spec}} {{notes}}`
- `signatures` — `{{role}} {{name}} {{date}}`

The loop lists start empty in `RdsContextBuilder`; downstream commands populate
`TokenContext.Loops[...]` with dictionaries keyed by those field names.
