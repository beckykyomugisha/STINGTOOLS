# MEP Symbol Colour & Scale Guide

**What this covers**: How STING's MEP detail symbols adapt to different drawing scales, how colour schemes work, which colour standard to apply for which drawing type, and how to configure both in the plugin.

**Symbols covered**: General MEP plan/section annotation symbols loaded from `Families/MEP/` via `MepSymbolEngine`. This guide does NOT cover ISO 6412 fabrication spool symbols (see `ISO6412_PIPING_SYMBOLS_GUIDE.md`) or SLD symbols (see `SLD_SYMBOLS_LAYMANS_GUIDE.md`).

---

## Part 1 — Why Scale Matters for Symbols

### The problem with fixed-size symbols

A pipe elbow symbol drawn at 400 mm in model space looks correct on a 1:50 plan. But on a 1:200 site plan (four times smaller) that same symbol would shrink to a tiny dot. On a 1:25 detail drawing it would become enormous and obscure the detail.

**STING's solution**: Every MEP symbol family contains a `Symbol Scale` parameter (integer). The symbol's internal geometry formulas are driven by this parameter so that the **plotted size stays constant** regardless of the view scale.

### How the formula works

For a symbol targeting 8 mm plotted at 1:50:

```
Model size = Paper target × View scale
Model size = 8 mm × 50 = 400 mm   (at 1:50)
Model size = 8 mm × 25 = 200 mm   (at 1:25)
Model size = 8 mm × 100 = 800 mm  (at 1:100)
```

The family parameter formula is:
```
[Geometry dimension] = STING_ISO_SYMBOL_SCALE_IN / 50 × 8 mm
```

When `MepSymbolEngine` places a symbol, it reads `view.Scale` and writes that integer into the `Symbol Scale` instance parameter. The family's formulas then compute the correct geometry size automatically.

---

## Part 2 — Scale Tiers

STING groups drawing scales into four tiers. Each tier has a recommended **paper target size** — the size the symbol appears on paper after printing:

| Scale tier | Example scales | Paper target (plotted mm) | Symbol complexity |
|---|---|---|---|
| **Large** (detail) | 1:5, 1:10, 1:20, 1:25 | 6 mm | Full detail — all linework visible |
| **Standard** (working) | 1:50, 1:100 | 8 mm | Standard — main features shown |
| **Small** (coordination) | 1:200, 1:500 | 6 mm | Simplified — key outline only |
| **Diagram** (schematic / SLD) | 1:1 (drafting view) | 5 mm at 1:1 | Schematic representation |

**Why does the large-scale tier use 6 mm instead of 8 mm?**
At 1:25, an 8 mm symbol would be 200 mm in model space — which starts to crowd the drawing. Reducing to 6 mm keeps the symbol readable while leaving room for dimensions and annotations.

**Diagram tier (SLD)**
SLD drafting views have `view.Scale = 1` in Revit (they're not cut from a 3D model). The symbol family is authored at its literal plotted size. The `MepSymbolEngine` detects a drafting view and sets `Symbol Scale = 1` instead of reading the view scale.

---

## Part 3 — Symbol Complexity by Scale

The same component can have different visual representations at different scales. The `STING_MEP_SYMBOLS_INDEX.csv` catalogue flags which view types each symbol targets. At scale-tier boundaries, `MepSymbolEngine` picks the appropriate family variant:

### Example: Air handling unit (AHU)

| Scale | Symbol appearance | Family used |
|---|---|---|
| 1:200 (coordination) | Simple rectangle with "AHU" label | `STING_SYM_HVAC_AHU_PLAN_SIMPLE.rfa` |
| 1:100 (working plan) | Rectangle with supply/return connections + fan symbol | `STING_SYM_HVAC_AHU_PLAN.rfa` |
| 1:50 (detail plan) | Full outline with all duct connections + coil, fan, filter symbols | `STING_SYM_HVAC_AHU_PLAN_DETAIL.rfa` |

STING uses the `_SIMPLE` suffix convention to distinguish reduced-complexity variants. When no simple variant exists, the standard symbol is used at all scales with the auto-scaling formula compensating for size.

---

## Part 4 — Colour Schemes

### 4.1 Overview of Available Schemes

STING supports six colour schemes:

| Scheme | When to use | Industry source |
|---|---|---|
| **Corporate** | All internal STING drawings; neutral monochrome grey | Planscape house standard |
| **BS 1710** | UK pipe identification on plans and sections | BS 1710:2014 |
| **CIBSE** | UK building services drawings (most common on UK projects) | CIBSE Guide — recommended practice |
| **ASHRAE** | HVAC air-side drawings on US/international projects | ASHRAE colour conventions |
| **IEC 60617** | Electrical SLD drawings (monochrome, matching the standard) | IEC 60617 |
| **NBS** | UK specification-linked drawings using NBS colour palette | NBS Chorus / NBS colour library |
| **Monochrome** | Print-ready drawings, black on white | All-black linework |

---

### 4.2 BS 1710:2014 — Pipe Identification Colours

**What it is**: BS 1710 is a British Standard that specifies mandatory colour bands on physical pipes to identify their contents. STING applies the same colours to pipe symbols on drawings for consistency with the physical installation.

| Service | Base colour | STING RGB | Notes |
|---|---|---|---|
| Cold water | Green | R:0, G:128, B:0 | Fresh cold water supply |
| Hot water | Green | R:0, G:128, B:0 | Same base; label distinguishes hot |
| Fire main | Red | R:255, G:0, B:0 | Fire fighting water — always Red |
| Steam | Silver / grey | R:180, G:180, B:180 | Process steam |
| Condensate | Silver / grey | R:180, G:180, B:180 | Returns from steam system |
| Natural gas | Yellow ochre | R:255, G:200, B:0 | Fuel gas — also LPG |
| Compressed air | Light blue | R:135, G:206, B:235 | Instrument / utility air |
| Mineral / fuel oil | Brown | R:139, G:69, B:19 | Heating oil, lubricants |
| Electrical conduit | Orange | R:255, G:140, B:0 | Electrical containment (conduit, tray) |
| Drainage | Black | R:0, G:0, B:0 | Foul and storm drainage |
| Refrigerant | Violet / purple | R:128, G:0, B:128 | Air conditioning refrigerant lines |
| Medical gas (O₂) | White | R:255, G:255, B:255 | Oxygen supply — per HTM 02-01 |
| Medical gas (N₂O) | Blue | R:0, G:0, B:255 | Nitrous oxide — per HTM 02-01 |

**How STING applies it**: When `SymbolColorScheme.BS1710` is selected, `MepSymbolEngine` calls `GetSchemeColor("BS1710", categoryHint)` where `categoryHint` is the Revit category of the MEP element (e.g. "Pipe Curves", "Pipe Accessories"). The returned `RgbColor` is written to `STING_SYM_R`, `STING_SYM_G`, `STING_SYM_B` integer instance parameters on the placed FamilyInstance. The family must be authored to read these and colour its linework accordingly (via a formula-driven Line Color parameter or a subcategory colour override).

---

### 4.3 CIBSE Colours — UK Building Services Standard

**What it is**: The Chartered Institution of Building Services Engineers (CIBSE) publishes recommended colour conventions for building services drawings in the UK. These are not mandatory law but are the industry norm on UK MEP projects.

| Discipline | CIBSE colour | STING RGB |
|---|---|---|
| Plumbing / pipework | Blue | R:0, G:112, B:192 |
| Ductwork / HVAC | Green | R:70, G:175, B:0 |
| Electrical (general) | Orange/amber | R:255, G:153, B:0 |
| Drainage | Dark brown | R:100, G:60, B:0 |
| Fire protection | Red | R:255, G:0, B:0 |
| Gas supply | Yellow | R:255, G:215, B:0 |
| Medical gas | Purple | R:128, G:0, B:128 |
| Cable management (trays, conduit) | Light orange | R:255, G:180, B:100 |
| Structural steelwork | Grey | R:130, G:130, B:130 |

**When to use**: All UK MEP working drawings. CIBSE colours are the default for UK practices. If your project is a UK hospital, school, or commercial building, CIBSE is almost certainly what the architect and structural engineer expect.

---

### 4.4 ASHRAE Colours — Air-Side HVAC (US/International)

**What it is**: ASHRAE (American Society of Heating, Refrigerating and Air-Conditioning Engineers) uses specific colours for air-side ductwork to distinguish supply, return, exhaust, and outside air. This is the dominant standard for HVAC coordination in North America and on US-standard international projects.

| Air service | ASHRAE colour | STING RGB | Meaning |
|---|---|---|---|
| Supply air | Blue | R:0, G:112, B:192 | Conditioned air delivered to spaces |
| Return air | Orange | R:255, G:140, B:0 | Air drawn back from spaces |
| Exhaust air | Grey | R:128, G:128, B:128 | Air expelled from building |
| Outside air | Green | R:0, G:176, B:80 | Fresh air drawn in from outside |
| Relief air | Light blue | R:135, G:206, B:235 | Exhaust from pressurised spaces |
| Transfer air | Yellow | R:255, G:215, B:0 | Air moved between zones without conditioning |
| Smoke exhaust | Dark grey | R:64, G:64, B:64 | Dedicated smoke control |
| Chilled water supply | Blue (darker) | R:0, G:70, B:160 | Cold water to cooling coils |
| Chilled water return | Blue (lighter) | R:100, G:170, B:255 | Warmed water returning from coils |
| Hot water supply | Red-orange | R:220, G:80, B:0 | Heating hot water |
| Hot water return | Orange | R:255, G:140, B:0 | Cooled return |

---

### 4.5 NBS Colours

**What it is**: NBS (National Building Specification) Chorus has a defined palette linked to Uniclass 2015 classification codes. STING applies NBS colours when a drawing type is linked to an NBS specification section.

| Uniclass system | NBS colour | STING RGB |
|---|---|---|
| Ss_45 HVAC | Dark teal | R:0, G:100, B:100 |
| Ss_50 Electrical | Dark orange | R:200, G:100, B:0 |
| Ss_55 Communications | Purple | R:100, G:0, B:150 |
| Ss_65 Fire protection | Dark red | R:150, G:0, B:0 |
| Ss_70 Plumbing | Dark blue | R:0, G:0, B:160 |

---

### 4.6 Corporate (Planscape House Standard)

**What it is**: STING's own neutral scheme. Uses a consistent mid-grey for all MEP symbols so drawings have a uniform appearance. Discipline is communicated by linetype and label rather than colour.

| Component type | Corporate colour | STING RGB |
|---|---|---|
| All symbols | Medium grey | R:128, G:128, B:128 |
| Critical / life safety | Dark grey | R:64, G:64, B:64 |
| Background / ghosted | Light grey | R:192, G:192, B:192 |

This is the default scheme and is recommended when:
- Printing in black-and-white
- Submitting for planning where colour plots are not required
- The drawing will be overlaid with another discipline's drawing (coloured clashes would be confusing)

---

## Part 5 — Which Scheme for Which Drawing Type?

| Drawing type | Recommended scheme | Reason |
|---|---|---|
| MEP coordination plan (all disciplines overlaid) | Corporate (monochrome) | Colour per discipline would clash; use linetype/label for identification |
| Mechanical HVAC plan (ductwork only) | ASHRAE or CIBSE | Standard conventions for air-side systems |
| Piping plan (plumbing + heating + water) | BS 1710 or CIBSE | BS 1710 for pipe contents identification; CIBSE for discipline identification |
| Electrical power plan | CIBSE | Standard for UK electrical drawings |
| Electrical lighting plan | Corporate or CIBSE | Lighting circuits differentiated by circuit number not colour |
| Fire protection plan | Corporate or BS 1710 (Red) | Fire mains are always red per BS 1710 |
| SLD / schematic | IEC 60617 (monochrome) | IEC 60617 is black-on-white; colour added only for life-safety emphasis |
| Fabrication spool | Corporate | ISO 6412 symbols are monochrome standard; colour on pipe itself, not symbol |
| Client presentation | Corporate or CIBSE | Clean, professional appearance |
| RCP (reflected ceiling plan) | Corporate | Ceiling services typically shown monochrome |
| Section through building | CIBSE | Discipline colour makes services readable |

---

## Part 6 — Configuring Colour and Scale in STING

### Via the dock panel button

**TAGS tab → (scroll to Symbols section) → ★ Place MEP Symbols**

1. Click **★ Place MEP Symbols** (operates on selection or active view).
2. A dialog asks: **Choose symbol standard** (CIBSE / IEC 60617 / ISO 14617 / Corporate).
3. Then: **Choose colour scheme** (Corporate / BS 1710 / CIBSE / Monochrome).
4. STING places symbols, auto-setting scale and colour for each element.

### Via FabricationOptions (for the fabrication package)

```
FabricationOptions.PlaceISO6412Symbols = true
FabricationOptions.SymbolPlacementMode = PlacementMode.Replace
```

ISO 6412 symbols on spool sheets always use Corporate scheme (monochrome) — colour annotation is added via the pipe's graphic override, not the symbol itself.

### Changing the colour scheme on already-placed symbols

Use **Clear MEP Symbols** (removes all symbols from active view), then **Place MEP Symbols** again with the new colour scheme. Alternatively, change the `STING_SYM_R/G/B` instance parameters directly on the placed FamilyInstances — useful for one-off overrides.

---

## Part 7 — Authoring a Scale-Aware, Colour-Aware Symbol Family

### Required parameters

Every MEP symbol family in `Families/MEP/` must carry these instance parameters (bind from the shared parameter file `Families/ISO6412/STING_ISO_SYMBOL_TEMPLATE.params.txt` — same parameter file as ISO 6412 symbols):

| Parameter | Type | Purpose |
|---|---|---|
| `Symbol Scale` | Integer | View scale (e.g. 50 for 1:50). Drives linework size. |
| `STING_SYM_R` | Integer | Red channel (0–255) of the scheme colour. |
| `STING_SYM_G` | Integer | Green channel. |
| `STING_SYM_B` | Integer | Blue channel. |
| `STING_PLACED_BY_SYMBOL_PLACER_BOOL` | Integer | Set to 1 at placement for idempotency. |
| `STING_SYM_VIEW_ID` | Text | View ElementId where placed (for Remove / Replace). |
| `STING_SYM_CODE_TXT` | Text | Symbol code from CSV (e.g. `HVAC_AHU_PLAN`). |
| `STING_SYM_HOST_ELEMENT_ID` | Text | ElementId of the MEP element this symbol annotates. |

### Linework size formula

```
Base Size = STING_ISO_SYMBOL_SCALE_IN / 50 × 8 mm
```
Drive all linework dimensions off `Base Size`.

### Colour formula (optional — requires formula-driven subcategory)

Revit does not allow linework colour to be driven by a formula directly. Instead, create a **Line Color** type parameter (Family Types) and use a lookup table approach, or use graphic overrides applied by the placer post-placement:

**Option A — Graphic override (simpler, recommended)**
`MepSymbolEngine.ApplyColor()` uses `view.SetElementOverrides(instanceId, ogs)` to apply projection line colour = `RgbColor` from the scheme. The symbol linework is black by default; the override colourises it in the view. This requires no family changes beyond the RGB parameters being present.

**Option B — Family parameter-driven colour (complex)**
Create a subcategory (e.g. "STING - Symbol Lines") and set the subcategory's line colour programmatically after placement using the RGB parameter values. This allows colour to follow the symbol into any view where overrides are not active.

For most projects, Option A is sufficient.

---

## Part 8 — Drawing-Type Matrix

This table shows which symbol standard and colour scheme apply to each STING drawing type:

| STING Drawing Type ID | Standard | Colour scheme | Scale tier |
|---|---|---|---|
| `mep-plan-A1-1to100` | CIBSE | CIBSE | Standard (1:100) |
| `mep-hvac-duct-A1-1to100` | CIBSE / ISO14617 | ASHRAE | Standard (1:100) |
| `mep-plantroom-A1-1to50` | CIBSE | CIBSE | Standard (1:50) |
| `mep-coord-A1-1to50` | Corporate | Corporate (monochrome) | Standard (1:50) |
| `plumb-drainage-A1-1to100` | CIBSE / BS6465 | BS 1710 (drainage=black) | Standard (1:100) |
| `elec-power-A1-1to100` | IEC 60617-11 | CIBSE (elec=orange) | Standard (1:100) |
| `elec-lighting-A1-1to100` | IEC 60617-11 | Corporate | Standard (1:100) |
| `elec-riser-A2-1to100` | IEC 60617 | IEC (monochrome) | Diagram |
| `pipe-spool-A1-1to50` | ISO 6412 | Corporate | Standard (1:50) |
| `duct-spool-A1-1to50` | ISO 6412 | Corporate | Standard (1:50) |
| `arch-rcp-A1-1to100` | IEC 60617-11 | Corporate | Standard (1:100) |
| `pres-3d-axon-A1` | Corporate | Corporate | Large (1:50 or larger) |

---

## Part 9 — Frequently Asked Questions

**Q: Do I need to re-place symbols every time I change the view scale?**
A: No. The `Symbol Scale` instance parameter is the link between the view scale and the symbol size. When you change the view's scale in Revit, you can update the symbols by running **Place MEP Symbols** with **Replace** mode. STING reads the new `view.Scale` and writes it to each placed symbol.

**Q: Can I mix colour schemes in the same view?**
A: Yes — you can select individual symbols and manually change their `STING_SYM_R/G/B` values, or apply a graphic override. The colour scheme applies when placing/replacing; after that you can override individual instances like any Revit element.

**Q: The symbols look too large on my 1:200 site plan. What do I do?**
A: The `STING_MEP_SYMBOLS_INDEX.csv` has a `paper_size_mm` column that the engine uses as the target. If 8 mm is too large at 1:200, add a `_SIMPLE` variant family and set its `paper_size_mm` to 4 mm in the CSV. Or simply use a filter to exclude MEP symbols from site-plan views.

**Q: What is the difference between MEP detail symbols and symbol overlays?**
A: MEP detail symbols (this guide) are **FamilyInstance** objects — real Revit elements you can select, delete, and whose parameters you can edit. Symbol overlays (`PlaceSymbolsInViewCommand`) are **IndependentTag** annotations tied to a concept/standard lookup system. Both exist in STING; they serve different purposes and should not be confused.

**Q: Why does the STING_MEP_SYMBOLS_INDEX.csv not include ISO 6412 spool symbols?**
A: ISO 6412 spool symbols are in a separate catalogue (`STING_ISO_SYMBOLS_INDEX.csv`) and are placed by a separate engine (`IsoSymbolPlacer`). `MepSymbolEngine` loads both CSVs at runtime to avoid naming conflicts between the two symbol sets, but the catalogue data is kept separate so the authoring teams and review processes can stay independent.

---

*Guide version 1.0 — 2026-05-17*
*See also: ISO6412_PIPING_SYMBOLS_GUIDE.md, SLD_SYMBOLS_LAYMANS_GUIDE.md, MEP_FOUNDATION_GUIDE.md*
