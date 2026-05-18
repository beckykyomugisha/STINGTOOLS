# Families/ISO6412 — Seed Symbol Families

This folder holds **hand-drafted, standard-accurate Revit annotation families** (`.rfa`) for
ISO 6412 / BS 308 Part 3 piping, duct, and conduit spool symbols.

---

## Why this folder exists

STING can **auto-generate** symbol families from the JSON definitions in
`StingTools/Data/Symbols/STING_ISO6412_SYMBOLS.json`. Those generated families are marked
`"status": "draft"` — their geometry is a reasonable approximation of the standard but has
**not** been dimensionally verified. They are useful as placeholders while proper seed families
are being authored.

When a seed `.rfa` is placed here, `MepSymbolEngine` picks it up at **search tier 1** (highest
priority) and uses it instead of the generated family. The JSON generator is never called for
that symbol.

---

## File Naming Convention

Every file **must** be named exactly as the `id` field in the JSON definition, with `.rfa` extension.

| JSON id | Required filename |
|---|---|
| `ISO6412_ELBOW_90_BW` | `ISO6412_ELBOW_90_BW.rfa` |
| `ISO6412_GATE_VALVE_FS` | `ISO6412_GATE_VALVE_FS.rfa` |
| `ISO6412_WELD_BW` | `ISO6412_WELD_BW.rfa` |

The full list of 164 expected filenames is in the [Quick Reference table](../../docs/guides/ISO6412_WORKFLOW_AND_DRAFTING_GUIDE.md#quick-reference).

---

## Family Template

All seed families must use the **Generic Annotation** template:

- Revit template: `Generic Annotation.rft`
- Family Category: `Generic Annotations`
- Subcategory: `ISO6412` (create if absent — Manage → Object Styles → Annotation Objects → New)

Do **not** use Detail Component, Detail Item, or any 3D template.

---

## Required Shared Parameters

Every seed family must bind these shared parameters from
`Families/ISO6412/STING_ISO_SYMBOL_TEMPLATE.params.txt`:

| Parameter | Type | Purpose |
|---|---|---|
| `Symbol Scale` | Integer | Runtime scale modifier (wire to geometry in family) |
| `STING_PLACED_BY_SYMBOL_PLACER_BOOL` | Yes/No | Set by IsoSymbolPlacer on placement |
| `STING_PLACER_ASSY_ID_TXT` | Text | Assembly ID that placed this instance |
| `STING_PLACER_MEMBER_ID_TXT` | Text | Member element ID |
| `STING_PLACER_SYMBOL_CODE_TXT` | Text | Symbol code for back-reference |
| `STING_ISO_SYMBOL_SCALE_IN` | Number | Internal scale factor |
| `STING_FINALIZATION_CHECKLIST` | Integer | Quality gate bitmask |

---

## Line Weight Specification

Line weights follow **ISO 128-20** and **BS 8888** for technical drawing:

| Use | Pen weight | Revit line weight number* |
|---|---|---|
| Main symbol outline | 0.35 mm | 3 |
| Pipe centreline (run) | 0.25 mm | 2 |
| Hidden / secondary lines | 0.18 mm | 1 |
| Weld symbols | 0.50 mm | 4 |
| Filled regions (solid valve bodies) | 0.25 mm outline | 2 |

\* Revit line weight numbers depend on the project's line weight table. Calibrate to your
corporate standard. The numbers above assume the STING corporate line weight table where
number 1 = 0.18 mm, 2 = 0.25 mm, 3 = 0.35 mm, 4 = 0.5 mm.

Assign line weights **by subcategory** in the family (Manage → Object Styles inside the family
document), not per-element. This allows project-wide override via Object Styles.

---

## Scale Behaviour

Spool isometric drawings use `view.Scale = 1` (paper scale = model scale).
At scale 1:1 a symbol drawn at 6 mm in the family appears 6 mm on paper.

**To make symbols resizable** — wire the `Symbol Scale` integer parameter to a reference
parameter that multiplies all geometry lengths. Pattern:

```
Geometry length formula = <base_length_ft> * (Symbol Scale / 100)
```

Default `Symbol Scale = 100` = 100% = nominal size. `Symbol Scale = 150` = 50% larger.

---

## Symbol Geometry Conventions

```
      +Y (branch up)
       |
−X ────┼──── +X  (pipe run, flow left → right)
       |
      −Y
```

- Pipe centreline runs along **Y = 0**
- Inlet on the left (`x = −0.5`), outlet on the right (`x = +0.5`)
- All coordinates normalised −0.5 to +0.5 over the symbol bounding box
- Actual size set by `symbolSize` mm (6 mm for pipe/valves/flanges, 8 mm for duct, 4 mm for welds)

---

## Controlling Colour and Line Weight in Projects

Because all symbols share the `ISO6412` subcategory, overrides are easy:

| Scope | Where |
|---|---|
| **All symbols, all views** | Manage → Object Styles → Annotation Objects → ISO6412 |
| **All symbols, one view** | View → Visibility/Graphics → Annotation Categories → ISO6412 row |
| **One symbol instance** | Right-click → Override Graphics in View → By Element |

---

## How to Submit a Corrected Family

1. **Draft the family** against the actual ISO 6412 / BS 308 Part 3 plate. Use the coordinate
   conventions and line weight spec above.
2. **Checklist** — run through the relevant checklist in
   `docs/guides/ISO6412_WORKFLOW_AND_DRAFTING_GUIDE.md §5` before submitting.
3. **Name the file** exactly as the JSON `id` (see naming convention above).
4. **Add to this folder** and commit on a feature branch:
   ```
   git add Families/ISO6412/ISO6412_GATE_VALVE_FS.rfa
   git commit -m "Add seed family: ISO6412_GATE_VALVE_FS (BS 308 Pt3 verified)"
   ```
5. **Update JSON status** — change `"status": "draft"` to `"status": "reviewed"` for that
   symbol in `STING_ISO6412_SYMBOLS.json`.
6. Once accepted and the family is committed, update to `"status": "final"`.

---

## Finalization Gate

A symbol is considered **final** when ALL of the following are true:

- [ ] `.rfa` file committed to this folder with correct name
- [ ] Geometry verified against ISO 6412 / BS 308 Part 3 plate
- [ ] Line weights set by subcategory (not by element)
- [ ] `Symbol Scale` parameter wired to geometry
- [ ] All 7 shared parameters bound
- [ ] JSON `"status"` updated to `"final"`
- [ ] `STING_FINALIZATION_CHECKLIST` bitmask set to `127` (all 7 bits)

---

## Current Status

164 symbols defined in JSON. 0 seed families committed.
All symbols are currently `"status": "draft"` — generated geometry is in use as placeholders.

See the [Quick Reference](../../docs/guides/ISO6412_WORKFLOW_AND_DRAFTING_GUIDE.md#quick-reference)
for the full list of symbol IDs, categories, and symbol sizes.
