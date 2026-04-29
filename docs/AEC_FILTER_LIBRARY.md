# AEC/FM Filter Library

Phase 139 ships a **199-filter corporate-baseline `ParameterFilterElement`
library** for the StingTools Drawing Template Manager. Every filter has
a JSON definition (categories + rule tree) and a default
`OverrideGraphicSettings` recipe so it renders out-of-the-box without
per-pack tuning. Pack-level rules can still override any field.

## Files

| Path | Role |
|---|---|
| `StingTools/Data/STING_AEC_FILTERS.json` | 199 filter definitions (categories + rule trees + default overrides) |
| `StingTools/Core/Drawing/AecFilterDefinition.cs` | POCO + rule grammar |
| `StingTools/Core/Drawing/AecFilterRegistry.cs` | Per-document loader; layers `<project>/_BIM_COORD/aec_filters.json` over corporate |
| `StingTools/Core/Drawing/AecFilterFactory.cs` | JSON rule tree → `ElementFilter` + `ParameterFilterElement.Create` |
| `StingTools/Commands/Drawing/AecFilterCommands.cs` | `AecFiltersCreate` / `AecFiltersInspect` / `AecFiltersReload` |

## Rule grammar

```jsonc
// Leaf
{ "param": "FIRE_RATING", "kind": "builtin", "op": "equals", "value": "60" }

// Compound (any depth)
{
  "logic": "or",
  "rules": [
    { "param": "ALL_MODEL_INSTANCE_COMMENTS", "op": "contains", "value": "Acoustic" },
    { "param": "ALL_MODEL_TYPE_COMMENTS",     "op": "contains", "value": "Rw" }
  ]
}
```

| Field | Values |
|---|---|
| `param` | `BuiltInParameter` enum name (e.g. `FIRE_RATING`), shared parameter name (e.g. `ASS_DISCIPLINE_COD_TXT`) or GUID |
| `kind`  | `builtin` (default), `shared`, `phase`, `workset`, `level` |
| `op`    | `equals`, `notEquals`, `greater`, `greaterOrEqual`, `less`, `lessOrEqual`, `contains`, `notContains`, `beginsWith`, `notBeginsWith`, `endsWith`, `notEndsWith`, `hasValue`, `hasNoValue` |
| `value` | string; coerced to int / double / ElementId by `type` hint or sniffed from the parameter's data type |
| `type`  | `string` (default), `int`, `double`, `elementId`, `yesno` |
| `logic` | `and` (default for compound), `or` |

The factory enforces the Revit 2025/2026/2027 constraints:

- Categories must intersect `ParameterFilterUtilities.GetAllFilterableCategories`
- Numeric epsilon defaults to `1e-9`
- Case-sensitivity flag dropped (no-op since Revit 2022)
- Compound trees emit `LogicalAndFilter` / `LogicalOrFilter` with
  one `ElementParameterFilter` per leaf

## Override recipe

Every filter ships a `defaultOverride` block mirroring `StyleFilterRule`:

```jsonc
"override": {
  "halftone": false,
  "projColor": "#B40000", "projWeight": 6, "projLinePattern": "Dash",
  "cutColor":  "#B40000", "cutWeight":  6,
  "surfFgColor": "#FF6464", "surfFgPattern": "Solid fill",
  "transparency": 0,
  "detailLevel": "Fine"
}
```

`ViewStylePackApplier.ApplyFilterRules` merges:

1. The pack's own `StyleFilterRule` fields (always win when set)
2. The corporate `defaultOverride` (filled in for any null pack field, when `inheritDefaults != false`)
3. Revit defaults

Pack rules can opt out of inheritance with `"inheritDefaults": false`.

## Lazy filter creation

When a pack references a filter by name that doesn't yet exist in the
document, `ApplyFilterRules` calls `AecFilterFactory.FindOrCreate` to
mint it from the registry under the active transaction. This means a
fresh model can apply the corp-coordination pack and see all 22 MEP
service filters auto-created on first use.

## Standards covered

| Discipline | Filter count | Standards |
|---|---:|---|
| Architectural | 47 | BS 9999 fire ratings · BS 8300 accessibility · ISO 13567 |
| Mechanical / HVAC | 33 | GSA MEP colour mapping · CIBSE-SDE · BS 1710 (steam) · ASHRAE / SMACNA insulation |
| Structural | 31 | BS 4449 rebar · BS 5950 / BS EN 1993 sections · BS EN 1992 concrete · BS 5268 timber |
| Fire | 30 | BS 9999 · BS 9990 risers · BS 5266 emergency lighting · BS 5839 alarm · BS EN 12845 sprinkler |
| Electrical | 27 | BS 7671 · BS EN 62305 LPS · ISO 6707 · GSA |
| Plumbing | 18 | BS 1710 (UK) · ASME A13.1 (US) · BS EN 12056 (above ground) · BS EN 752 (below ground) |
| FM / COBie | 11 | COBie 2.4 · SFG20 · ISO 15686 lifecycle |
| ISO 19650 | 8 | Status codes S0–S7, A1+, B1+ |
| Coord / LOD | 8 | BIMForum LOD spec · workset / clash conventions |
| Vertical transport | 5 | BS EN 81-72 firefighter · BS EN 81-76 evacuation |
| QA | 5 | STING tag-completeness flags |

## Colour schemes

| Scheme | Source |
|---|---|
| BS 1710 | UK pipe identification (water green, steam silver, gas yellow ochre) |
| ASME A13.1 | US pipe identification (yellow flammable, green water, blue compressed air) |
| GSA | US public-sector BIM Technical Standards MEP colour mapping (RGB seeded into Revit MEP defaults) |
| CIBSE-SDE | CIBSE Society of Digital Engineering symbols / colours |
| BS 9999 | UK fire compartmentation pink → red → dark red ladder by rating |
| ISO 19650 | Status code RAG (red WIP → amber shared → black published) |

## Commands

| Tag | Class | Purpose |
|---|---|---|
| `AecFiltersCreate` | `AecFiltersCreateCommand` | Mint every definition as a `ParameterFilterElement` in the active doc (idempotent — already-present filters skipped) |
| `AecFiltersInspect` | `AecFiltersInspectCommand` | Read-only summary: total, present-in-doc, missing, top tag groups |
| `AecFiltersReload` | `AecFiltersReloadCommand` | Clear the per-doc registry cache (re-reads JSON on next access) |

## Project overrides

Override or extend the corporate library by dropping
`<project>/_BIM_COORD/aec_filters.json` next to the model — same shape as
the corporate file. Project entries win by `id` (replace) and any new
ids append. `Origin` flips to `project` automatically.

## Per-drawing-type filter usage

Recommended starting filter set per drawing type — opt out per-project
by editing the `filterRules` array in your project pack override:

| Drawing type | Filter set |
|---|---|
| `arch-plan-A1-1to100` | phase × 3 + fire FD30/60/90/120 + acoustic + load-bearing + curtain wall + accessibility |
| `arch-rcp-A1-1to100` | ceiling types + lighting (general / emergency / feature) + sprinklers + smoke detectors |
| `arch-fire-strategy-A1-1to100` | fire compartments + smoke compartments + escape routes + fire alarm zones + fire-rated doors / walls / glazing |
| `arch-accessibility-A1-1to100` | accessible WC + accessible doors + ramps + escape + refuge |
| `mep-coord-A1-1to50` | all HVAC services + plumbing services + electrical containment + clash flags + insulation thickness |
| `mep-hvac-duct-A1-1to100` | supply / return / exhaust / smoke extract + insulation + dampers + VAV / FCU / AHU |
| `elec-power-A1-1to100` | small power + normal / essential / UPS + voltage tiers + distribution boards + transformers |
| `elec-lighting-A1-1to100` | general / emergency / feature / external + switchgear (halftone) + ceiling categories (off) |
| `elec-fire-alarm-A1-1to100` | fire alarm devices + sprinklers + dry / wet risers + fire compartments |
| `plumb-drainage-A1-1to100` | foul AG / BG + RWP + surface water + vents (above-ground vs below-ground per project) |
| `pipe-spool-A1-1to50` | LOD ≥ 400 only + insulation thickness + system-specific colour |
| `struct-plan-A1-1to100` | structural material + sections (UB / UC / SHS / RHS / CHS / PFC) + foundations + bracing + rebar bands |
| `coord-clash-A1-1to50` | clash high / medium / low + workset arch / struct / mep + LOD < 300 (halftone) |
| `fm-asset-location-A1-1to100` | COBie equipment / component + asset criticality + warranty active + replacement cycle + owner |
| `pres-3d-axon-A1` | (none — clear all technical filters) |
| `clar-rfi-A3` | only the discipline filter relevant to the issue |

## Caveats

1. `STRUCTURAL_MATERIAL_TYPE`, `WALL_STRUCTURAL_USAGE_PARAM`, `FUNCTION_PARAM` numeric values come from the underlying `WallFunction` / `WallStructuralUsage` / `StructuralMaterialType` enums — values are stable across Revit versions but the enum positions can shift. Verify a filter that uses an int rule against the target Revit's enum values if compliance is critical.
2. Categories that don't exist in the target Revit version (e.g. `OST_FabricReinforcement` on older builds) are silently dropped from the category list with a warning. If all categories drop, the filter creation is skipped with an error.
3. Shared parameters referenced by `kind: "shared"` must be bound on the project before the filter can be created. The factory emits a warning and skips the filter when a referenced shared param is unbound — graceful degradation rather than failure of the whole batch.
4. `paramId.Value` (Revit 2024+ Int64) replaces the deprecated `paramId.IntegerValue`.
5. `Definition.GetDataType()` (Revit 2024+ ForgeTypeId) replaces `Definition.ParameterType`.
