# STING title-block graphics — annotation families

These are the minimal Generic-Annotation families the **Work Item C** slot
placers load through `TitleBlockGraphicsRegistry`:

| Family (`.rfa`) | Category | Placed by | Notes |
|---|---|---|---|
| `STING_TB_NorthArrow.rfa`  | Generic Annotation | `TitleBlock_PlaceNorthArrow` | shaft + head; oriented by the placer (in the plan view, or rotated from `PRJ_ORG_PROJECT_NORTH_TXT`) |
| `STING_TB_KeyPlanBase.rfa` | Generic Annotation | `TitleBlock_PlaceKeyPlan`    | outline + highlight filled region |

> The **scale bar is not a family** — `TitleBlock_PlaceScaleBar` draws it as
> auto-scaling model-space detail lines directly in the primary plan view (a fixed
> real-world length rendered at `length / viewScale`), so no `.rfa` is generated for it.

## How they get here

The `.rfa` files are **generated programmatically inside Revit** — they are not
checked into the repo (Revit `.rfa` is a binary format that cannot be authored
outside Revit). Run the command **`TitleBlock_BuildGraphicsFamilies`** once in a
Revit session:

1. It resolves a `Generic Annotation.rft` template from Revit's family-template
   path (`Application.FamilyTemplatePath`).
2. It authors each family (`Application.NewFamilyDocument` +
   `famDoc.FamilyCreate.NewDetailCurve` + `FamilyManager` params).
3. It `SaveAs` into **this folder** (`<project>/Families/Annotations/`, else the
   add-in's `Families/Annotations/`) and loads each into the open project.

`TitleBlockGraphicsRegistry` searches `<project>/Families/Annotations/` then
`<addin>/Families/Annotations/` (and `<addin>/data/Families/Annotations/`).
The registry keys on the **family name** (`STING_TB_*`), so keep the names stable.

## Until they exist

The placement commands **skip cleanly** when a family is not found (they log and
report `no family` — no synthetic geometry, matching the fixture-placement
contract). Nothing breaks; the graphic simply isn't placed.

## Refining by hand

The generated geometry is deliberately minimal. Open any family in the Family
Editor to improve it (arrowhead style, tick spacing, dimension-locked flexing of
the scale bar off the `Scale` param, a real key-plan outline for your site). As
long as the **family name** and the **param names** above are preserved, the
registry and the placers keep working — no code change needed.

You can also drop your own hand-built `.rfa` here using these exact filenames to
override the generated ones.
