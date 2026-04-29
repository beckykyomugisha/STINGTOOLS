# Drawing Type, View Style Pack & VG Research

Deep-dive on the Drawing Template Manager's editor, data model, runtime,
and the gaps between what the JSON ships today and what corporate AEC
drawing standards (BS EN ISO 19650, BS 1192, ISO 13567, NBS, AIA NCS)
actually require. Companion to the JSON edits in this branch.

Scope: every field the editor exposes, every Revit API call the applier
makes, the corporate baseline as it ships, the bugs that silently drop
overrides on the floor, and the AEC-standard line-weight / colour /
halftone matrix the JSON was reauthored against.


## 1. Editor architecture

`UI/DrawingTypeEditorDialog.cs` (2,537 lines) is a two-tab WPF dialog,
`UI/RevitVgEditor.cs` (1,194 lines) is the embedded Revit-VG-grid replica
that lives inside the View Style Packs tab. Three sub-dialogs (`VgFillPatternDialog`,
`VgLineGraphicsDialog`, `VgColorPicker`) replicate Revit's "Override…"
popups so every cell behaves the way a Revit user expects.

### 1.1 Drawing Types tab

| Card | Field (UI control) | Target property on `DrawingType` |
|---|---|---|
| Identity | Id / Name / Description / Purpose / Discipline / Phase / Origin | `Id` `Name` `Description` `Purpose` `Discipline` `Phase` `Origin` |
| Sheet | Paper size / Title block / Orientation | `PaperSize` `TitleBlockFamily` `Orientation` |
| Views | Scale / Detail level / View template / Viewport type | `Scale` `DetailLevel` `ViewTemplateName` `ViewportTypeName` |
| Numbering | Sheet number pattern / Sheet name pattern | `SheetNumberPattern` `SheetNamePattern` |
| Crop | Kind / Scope-box name / Margin (mm) | `Crop.Kind` `Crop.ScopeBoxName` `Crop.MarginMm` |
| Section marker | Family / Mark prefix / Bubble style / Far-clip mm | `SectionMarker.{Family,MarkPrefix,BubbleStyle,FarClipMm}` |
| Annotation rule pack | Per-rule grid + per-category tag-family map + dimension strategy + denseUntilScale | `Annotation.{Rules,TagFamilies,TagDepths,DimensionStrategy,DimensionStyle,DenseUntilScale}` |
| Token profile (Phase 135) | Presentation mode / paragraph depth / tag size+style+colour / colour scheme / segment mask / display mode / per-cat depth | `TokenProfile.*` |
| Slots | Per-slot grid: label, ViewType, NormX/Y/W/H, scale | `Slots[]` (DrawingSlot) |
| Title-block params (Phase 138 bonus 4) | Row-per-param key/value | `TitleBlockParams` Dict<string,string> |

Validation strip at the bottom calls `DrawingTypeValidator.Validate()` —
asset-presence checks (title block / view template / viewport type /
section-marker family / tag family / dim style / text style), naming
sanity, and crop-strategy sanity (margin > 0, scope box exists when
Kind=ScopeBox).

### 1.2 View Style Packs tab

| Card | Field (UI control) | Target property on `ViewStylePack` |
|---|---|---|
| Template mode (Phase 137) | external/managed radio + `managedFields` checkbox grid + Discipline / VisualStyle / PhaseFilter | `TemplateMode` `ManagedFields` `Discipline` `VisualStyle` `PhaseFilter` |
| Identity | Id / Name / Description / Extends / Origin | `Id` `Name` `Description` `Extends` `Origin` |
| Appearance | Line-weight scale / Text style / Dim style / Hatch palette / View template / Detail level / Scale hint / Colour scheme | `LineWeightScale` `TextStyle` `DimensionStyle` `HatchPalette` `ViewTemplate` `DetailLevel` `ScaleHint` `ColorScheme` |
| Filter rules | Row grid: Name, Visible, Halftone, Proj-Col, Proj-Wt, Cut-Col, Cut-Wt, Trans% | `Filters[]` (StyleFilterRule) |
| VG overrides | **Embedded `RevitVgEditor`** — 4 tabs Model/Annotation/Imported/Filters, every cell mirrors Revit's VG dialog | `VgOverrides` Dict<string, StyleVgOverride> via bridge |
| Tag appearance (Phase 135) | Default colour scheme / Default tag style preset / Per-category tag style grid | `TagColorScheme` `DefaultTagStyle` `CategoryTagStyles` |

The editor only persists project-origin packs to
`<project>/_BIM_COORD/view_style_packs.json`. Edits to corporate packs
flip `Origin` to `project` automatically (drift detected via
`ViewStylePackRegistry.ComputeChecksums`).

### 1.3 Revit VG editor cells

`RevitVgEditor` builds rows from `doc.Settings.Categories` (Model +
Annotation tabs) plus `RevitCategoryTree.All` for project-specific
categories. Every cell maps to one or more `OverrideGraphicSettings`
calls inside `ViewStylePackApplier.ApplyPresetOverrides`:

| Cell | API call(s) |
|---|---|
| Visibility (3-state checkbox + chevron) | `View.SetCategoryHidden(catId, !visible)` |
| Line Style picker (Phase 137) | autofills `ProjLineColor` / `ProjLineWeight` / `ProjLinePattern` from the picked GraphicsStyle |
| Proj Lines "Override…" → `VgLineGraphicsDialog` | `ogs.SetProjectionLineColor` `SetProjectionLineWeight` `SetProjectionLinePatternId` |
| Proj Patterns "Override…" → `VgFillPatternDialog` | `ogs.SetSurfaceForegroundPattern{Id,Color,Visible}` `ogs.SetSurfaceBackgroundPattern{Id,Color,Visible}` |
| Trans % textbox | `ogs.SetSurfaceTransparency(0..100)` (≥100 also hides patterns) |
| Cut Lines "Override…" | `ogs.SetCutLineColor` `SetCutLineWeight` `SetCutLinePatternId` |
| Cut Patterns "Override…" | `ogs.SetCutForegroundPattern{Id,Color,Visible}` `ogs.SetCutBackgroundPattern{Id,Color,Visible}` |
| Halftone (3-state) | `ogs.SetHalftone(bool)` |
| Detail Level (read-only combo) | `ogs.SetDetailLevel(ViewDetailLevel)` |

Sub-dialogs return null on Cancel (no change), `Cleared = true` on
"Clear Overrides" (explicit reset), or a populated value object on OK.

## 2. Data model — what the persisted JSON actually carries

`ViewStylePack` (Core/Drawing/ViewStylePack.cs, 190 lines) declares the
authoritative JSON keys. The pack catalogue ships in
`Data/STING_VIEW_STYLE_PACKS.json` and merges with project overrides
from `<project>/_BIM_COORD/view_style_packs.json`. Field set:

```
id              name             description     origin
extends         (parent pack id for inheritance chain)
lineWeightScale (double)         textStyle       dimensionStyle
hatchPalette
filters[]       (StyleFilterRule list)
vgOverrides     (Dict<string, StyleVgOverride>)
tagFamilies     (Dict<categoryName, familyName>)
tagColorScheme  defaultTagStyle  categoryTagStyles  (Phase 135 tag defaults)
templateMode    managedFields[]  managedChecksum    (Phase 137 managed mode)
discipline      visualStyle      phaseFilter        phase
annotationCrop  farClipMm        viewRange          underlay     background
worksetVisibility  linkOverrides  colorFillSchemes  filterEnabled
checksum        (corporate-lock SHA-256)
```

`StyleVgOverride` (the value type for `vgOverrides`):

```
visible                  bool?
halftone                 bool?
projectionLineWeight     int?     (1..16)
projectionLineColor      string   ("#RRGGBB")
cutLineWeight            int?
cutLineColor             string
transparency             int?     (0..100)
```

`StyleFilterRule` (the value type for `filters[]`):

```
filterName               string   (must match a ParameterFilterElement)
visible                  bool     (default true)
halftone                 bool     (default false)
projectionLineColor      string?
projectionLineWeight     int?
cutLineColor             string?
cutLineWeight            int?
transparency             int?
```

The data model is intentionally narrower than the editor's in-memory
`PresetCategoryOverride` — surface fill patterns, cut fill patterns,
line patterns, and per-cell detail-level are **not** persisted on the
pack. The bridge in `RevitVgEditor` flattens patterns to colour/weight
on save. If a pack needs hatching control it has to live in a Revit
view template that the pack references via `ViewTemplate`.

### 2.1 Inheritance — extends chain

`ViewStylePackRegistry.ResolveExtends` walks `Extends` until null or
loop. Resolved pack carries `Id` + `Origin` from the leaf, scalar fields
folded later-wins, dictionaries (`vgOverrides`, `tagFamilies`,
`categoryTagStyles`) merged by key, lists (`filters`) appended. Every
consumer sees a fully merged snapshot — no walk required at apply time.

### 2.2 Apply pipeline

`DrawingTypePresentation.Apply(doc, view, dt)` runs ten steps in order
(see CLAUDE.md "Pipeline order"). Step 7 is the pack apply:

- **External mode** — `ViewStylePackApplier.Apply(doc, view, pack)`
  writes filters + VG overrides + workset visibility + link overrides +
  color-fill schemes directly to the view.
- **Managed mode** — `ManagedTemplateSyncer.EnsureTemplate(doc, pack,
  viewType)` is idempotent: absent → mint a `STING:{packId}:{ViewType}`
  template from a same-ViewType seed; present + checksum match → no-op;
  present + drift → re-apply pack settings + restamp checksum. The
  managed-fields whitelist defaults to `scale, detailLevel, discipline,
  visualStyle, phaseFilter, tagColorScheme, defaultTagStyle`; a pack
  may extend it with `phase, annotationCrop, farClip, viewRange,
  underlay, vgOverrides, filters, worksetVisibility, linkOverrides,
  colorFillSchemes, filterEnabled`. Drift detected via `SHA256` of the
  managed-field JSON stamped into `STING_PACK_CHECKSUM_TXT`.

### 2.3 DrawingType ↔ ViewStylePack

`DrawingType.ViewStylePackId` is a soft reference; null = no pack.
`DrawingType.TokenProfile` (Phase 135) takes precedence over the
pack's `TagColorScheme` / `DefaultTagStyle` / `CategoryTagStyles`
when set. Other DrawingType fields (`Scale`, `DetailLevel`,
`ViewTemplateName`) win over the pack's hint values.

## 3. Bugs — JSON keys silently drop on the floor

There are **three independent JSON-vs-model key mismatches** in
`Data/STING_VIEW_STYLE_PACKS.json`. All three drop user-authored intent
silently on load via `Newtonsoft.Json` ignoring unknown properties.

### 3.1 Inline shorthand line / cut keys (350 occurrences)

`StyleVgOverride` uses shorthand keys:

```json
"Walls": { "projColor": "#000000", "projWeight": 5, "cutColor": "#000000", "cutWeight": 8 }
```

But `StyleVgOverride` (ViewStylePack.cs:170) declares:

```csharp
[JsonProperty("projectionLineColor")]  public string ProjectionLineColor
[JsonProperty("projectionLineWeight")] public int?   ProjectionLineWeight
[JsonProperty("cutLineColor")]         public string CutLineColor
[JsonProperty("cutLineWeight")]        public int?   CutLineWeight
```

`Newtonsoft.Json` deserialisation drops unknown keys silently. Across
the 27-pack file, `grep -c '"projColor"\|"projWeight"\|"cutColor"\|"cutWeight"'`
returns **350 occurrences**, all silently dropped at load. The
applier-side counts confirm it — `ViewStylePackApplier.cs:180-181` reads
only `src.ProjectionLineColor` / `src.ProjectionLineWeight`. Net effect
on a real Revit project: **every non-visibility override on every pack
is lost**. The 21 "real" overrides on `corp-standard-plan` (walls 5/8,
floors 6/7, MEP halftone-grey, etc.) never reach Revit's
`OverrideGraphicSettings`.

Fix: rename the four shorthand keys to the canonical four. The same
edit unblocks:

- 19 packs that have non-trivial overrides (corp-base + 9 corp-* +
  pres-base + 9 pres-* variants).
- The Phase 138 typo-prevention pass — colour swatches in the editor
  read live from `_dataKeyMap` and write back through the bridge using
  the canonical keys, so the editor's preview was correct but its
  persisted output got dropped on every reload.

### 3.2 `appearance` sub-object instead of top-level fields

Eight packs nested their text / dimension / line-weight settings under
an `appearance` sub-object:

```json
"appearance": {
  "lineWeightScale": 1.0,
  "textStyleName":   "STING - 2.5mm",
  "dimensionStyleName": "STING - Linear",
  "hatchPalette": "ISO 13567 monochrome"
}
```

But `ViewStylePack.cs:33-36` puts them at the root:

```csharp
[JsonProperty("lineWeightScale")] public double LineWeightScale
[JsonProperty("textStyle")]       public string TextStyle
[JsonProperty("dimensionStyle")]  public string DimensionStyle
[JsonProperty("hatchPalette")]    public string HatchPalette
```

Two of the dropped values were real customisation:
`proj-arch-presentation.lineWeightScale = 0.75` and
`proj-mep-coordination.lineWeightScale = 0.5`. Both packs are
`templateMode: managed`, so a 25–50 % global line-weight reduction
that was authored against the corporate baseline never reached
Revit. Three other packs (`corp-fabrication-shop`, `corp-presentation-rich`,
`corp-standard-detail`) had bespoke `STING - 2.0mm Shop` /
`STING - 3.0mm Presentation` text styles buried under the dead key.

Fix promotes every `appearance.lineWeightScale` /
`appearance.textStyleName` / `appearance.dimensionStyleName` /
`appearance.hatchPalette` to top-level (when not already set), then
deletes the dead `appearance` block.

### 3.3 `filterRules` instead of `filters`

`corp-base`, `corp-coordination`, `corp-clarification`, and
`corp-demolition-phase` declared an entire filter library under
`filterRules`:

```json
"filterRules": [
  { "name": "Existing - Halftone", "halftone": true, "projColor": "#808080", … },
  { "name": "New Construction",    "projColor": "#000000", "projWeight": 7, … },
  { "name": "Demolished",          "projColor": "#FF0000", … },
  { "name": "Temporary",           "projColor": "#FF8C00", … },
  { "name": "Proposed - Planning", "projColor": "#4472C4", … },
  { "name": "STING - First-Fix Phase", "halftone": true, … },
  { "name": "STING - Noggin Required", "projColor": "#FF8C00", "projWeight": 6 }
]
```

But `ViewStylePack.cs:38` reads the list from `filters`, and
`StyleFilterRule.cs:160` reads the rule name from `filterName`, not
`name`. Both keys silently dropped. Net effect: the seven corporate
phase / status / first-fix / noggin rules — already authored and
visually consistent with the existing CDE workflow — never made it to
any view.

Fix renames `filterRules` → `filters` and rewrites each rule's `name`
→ `filterName` (the inline `projColor` etc. are caught by §3.1's bulk
rename). Result: `corp-base.filters` now holds the seven corporate
rules, every derived pack inherits them via the extends chain
(`ViewStylePackRegistry.ResolveExtends` appends parent filter rules
to child).

## 4. Other gaps in the corporate baseline

| Gap | Where | Impact |
|---|---|---|
| 0 filters across 27 packs | `filters[]` empty everywhere | Phase / suitability / fire-rating / discipline-isolation rules never run |
| 0 `tagFamilies` entries | `tagFamilies` empty everywhere | AnnotationRunner falls through to the per-DrawingType map only — no pack-level shared family table |
| `lineWeightScale = null` everywhere | every pack | `PackAppearance.LineWeightScale` never multiplied; line weights stuck at Revit's project default mapping |
| `hatchPalette = null` everywhere | every pack | Hatch-palette field is informational only today, but corporate styling intent is not recorded |
| `corp-base` is a 81-row visibility-only stub | all 81 entries set `visible: true` and nothing else | The shared base provides no actual graphic standards — every derived pack starts from zero |
| 36 of 54 drawing types have `viewStylePackId: null` | `STING_DRAWING_TYPES.json` | Most architectural / MEP / public-health / FM / presentation drawings have no pack assigned; rely on per-project template only |
| `proj-arch-presentation` / `proj-mep-coordination` / `proj-structural` are empty | they only set `extends` + `templateMode` | Project-scoped packs don't override anything beyond their parent — fine if parent is correct, fatal if it isn't |

## 5. AEC drawing standards — what corporate VG should look like

Three families of standard converge on the same answers in slightly
different vocabulary. Plugin's primary jurisdiction is UK
(`StandardsEngine.cs` already references BS 7671 / Approved Doc / BS
8300 / Part L), so the BS / ISO 19650 mapping leads.

### 5.1 Line-weight ladder (BS 1192-1, ISO 128-20)

Revit line-weights are a 1..16 index that the project's units-and-line-weight
table maps to mm. The BS 1192 ladder is mm-based; the table below
assumes the standard Revit line-weight defaults at 1:100 plan scale
(other scales rescale via the same table — this is why it has to be
correct at the project level, not on the pack).

| Revit weight | mm | Use |
|---|---|---|
| 1 | 0.05 | hairline, hatch hatching detail |
| 2 | 0.10 | text linework, fine annotation |
| 3 | 0.18 | dimensions, leaders, light projection |
| 4 | 0.25 | secondary projection — doors, windows, casework, furniture |
| 5 | 0.35 | primary projection — walls, floors in projection |
| 6 | 0.50 | secondary cut — floor cut, ceiling cut |
| 7 | 0.70 | primary cut — wall cut, structural cut |
| 8 | 1.00 | sheet border lower band, structural primary |
| 9 | 1.40 | sheet border upper band |
| 10+ | ≥ 2.00 | reserve — title-block heavy, graphic emphasis |

Heavy element rule: cut wall = 7, cut floor = 6, projection wall = 5,
projection door/window = 4, dimensions = 3, hatch = 2.

### 5.2 Colour conventions

ISO 13567-2 + AIA NCS layer-colour conventions say discipline tints
exist for screen visualisation but **printed working drawings are
monochrome** — pure black on white, halftone for context. The corporate
baseline therefore distinguishes:

| Pack purpose | Colour rule |
|---|---|
| Working drawings (plan/section/elev/detail/RCP) | Black `#000000` for primary linework, `#808080` mid-grey 50% halftone for context, no tints |
| Discipline-coordination packs | Discipline tints for own-discipline (e.g. blue for M, yellow for E), grey + halftone for other disciplines |
| Fabrication / shop drawings | Red `#C00000` for sectioned primary, black for projection, halftone everything else |
| Structural plans | Red `#C00000` projection + cut for own-discipline (industry convention), black for everything else |
| Presentation rich | Tinted surfaces, soft greys, MEP hidden |
| Presentation mono | Pure greyscale, no tints |
| Clarification (RFI sketches) | Black + revision red `#E60000` for callouts |

Discipline tints (when used) follow Uniclass / AIA NCS:

| Discipline | Colour | Hex |
|---|---|---|
| Architectural (A) | Black / dark grey | `#000000` / `#404040` |
| Structural (S) | Brick red | `#C00000` |
| Mechanical / HVAC (M) | Mid blue | `#1976D2` |
| Electrical (E) | Yellow / amber | `#E6A800` |
| Plumbing / public health (P) | Green | `#2E7D32` |
| Fire protection (FP) | Bright red | `#E60000` |
| Comms / LV (LV) | Purple | `#7B1FA2` |
| Site / landscape (G) | Earth brown | `#6D4C41` |

### 5.3 Halftone strategy

| Pack | Halftone targets |
|---|---|
| `corp-standard-plan` (architectural) | All MEP categories (Mech / Elec / Plumb / FP) at `#808080`. Ceilings `#808080`. |
| `corp-standard-rcp` | Walls / floors / furniture / casework halftone. Ceilings, lighting fixtures, air terminals, sprinklers, fire alarm full colour. |
| `corp-standard-section` | Same as plan but cut weights heavier (walls cut = 7, structure cut = 8). |
| `corp-standard-elevation` | All MEP halftone or hidden; structural framing halftone; surface materials shown. |
| `corp-standard-detail` | No halftone — every layer at full weight, fine detail. |
| `corp-coordination` | Other-discipline categories halftone at 30 transparency; own-discipline full colour. |
| `corp-fabrication-shop` | Fabricated assembly heavy + red; surrounding context all halftone. |
| `corp-presentation-rich` | MEP hidden (`visible: false`), structural framing halftone, walls / floors / furniture full colour. |
| `corp-presentation-mono` | Same visibility rules but everything clamped to grey-scale. |

### 5.4 Filter library — corporate standard

Eight filters that every corporate template should ship with. Each one
needs a `ParameterFilterElement` of the same name in the project (the
Filters panel's "Create" button or `TemplateExtCommands.ApplyFiltersCommand`
mints them):

| Filter name | Rule | Default action |
|---|---|---|
| `STING - New Construction` | Phase Created = "New Construction" | leave default (no override) |
| `STING - Existing` | Phase Created = "Existing" | halftone, projection grey `#808080` |
| `STING - Demolish` | Phase Demolished ≠ "None" | red projection `#E60000`, dashed pattern |
| `STING - Temporary` | Comments contains "TEMP" | halftone yellow `#E6A800` |
| `STING - Suitability S0-S2` (WIP/shared) | Shared param `STING_SUITABILITY = S0/S1/S2` | halftone grey `#A0A0A0` |
| `STING - Suitability S3-S4` (published) | Shared param `STING_SUITABILITY = S3/S4` | full colour, no halftone |
| `STING - Fire Rating` | Type param `Fire Rating` not empty | red projection `#E60000`, weight 6 |
| `STING - Acoustic Rating` | Type param `Acoustic Rating` not empty | blue projection `#1976D2`, weight 5 |

Phase / suitability filters apply to almost every working drawing pack.
Fire / acoustic apply selectively to arch-fire-strategy /
arch-accessibility / arch-acoustic profiles.

## 6. Pack-by-pack VG specification

The matrix below is what this branch's `STING_VIEW_STYLE_PACKS.json`
edits target — every cell is a default that the corporate baseline
should ship and that derived packs override only when justified.

### 6.1 `corp-base` (root of every corporate pack)

`lineWeightScale: 1.0` `hatchPalette: "BS 1192 mono"`
`textStyle: "STING - 2.5mm"` `dimensionStyle: "STING - Linear 2.5mm"`

Sets visibility + halftone defaults that every working pack inherits.
No projection / cut weights — those come from the purpose-specific
child pack so that section / elevation / detail can override the same
category at different weights.

| Category group | visibility | halftone |
|---|---|---|
| Architectural primary (Walls / Floors / Roofs / Ceilings / Doors / Windows / Curtain Walls + Mullions + Panels / Columns / Stairs / Ramps / Railings) | true | false |
| Structural (Structural Columns / Framing / Foundations / Rebar) | true | false |
| MEP (Mech Equip / Elec Equip / Plumbing Fixtures / Pipes + fittings / Ducts + fittings / Air Terminals / Lighting Fixtures + Devices / Conduits / Cable Trays / Sprinklers / Fire Alarm / Comms / Data / Security / Nurse Call) | true | true |
| Annotation (Grids / Levels / Reference Planes / Section Marks / Elevation Marks / Callout Heads / Spot Coords / Spot Elevations / Dimensions / Tags / View Titles / Viewports / Revision Clouds / Filled Regions / Detail Components / Detail Lines / Text Notes) | true | false |
| Site (Topography / Site / Planting / Parking) | true | false |
| Spatial (Rooms / Spaces / Areas / Mass / Scope Boxes / Section Boxes) | true | false |

### 6.2 `corp-standard-plan` — A1 1:100 architectural plan

`extends: corp-base` `templateMode: managed`
`managedFields: [scale, detailLevel, discipline, visualStyle,
phaseFilter, tagColorScheme, defaultTagStyle, vgOverrides, filters]`
`tagColorScheme: "Discipline"` `defaultTagStyle: "2.5NOM_BLACK"`

| Category | proj wt | proj col | cut wt | cut col | halftone | trans |
|---|---|---|---|---|---|---|
| Walls | 5 | #000000 | 7 | #000000 | — | — |
| Curtain Walls | 5 | #000000 | 6 | #000000 | — | — |
| Curtain Wall Mullions | 4 | #000000 | — | — | — | — |
| Curtain Wall Panels | 4 | #000000 | — | — | — | — |
| Floors | 5 | #000000 | 6 | #000000 | — | — |
| Ceilings | 4 | #808080 | — | — | true | — |
| Roofs | 5 | #000000 | 6 | #000000 | — | — |
| Doors | 4 | #000000 | — | — | — | — |
| Windows | 4 | #000000 | — | — | — | — |
| Stairs | 4 | #000000 | 5 | #000000 | — | — |
| Railings | 3 | #000000 | — | — | — | — |
| Ramps | 4 | #000000 | 5 | #000000 | — | — |
| Furniture | 2 | #000000 | 3 | #000000 | — | — |
| Casework | 2 | #000000 | 3 | #000000 | — | — |
| Structural Columns | 5 | #404040 | 7 | #404040 | true | — |
| Structural Framing | 4 | #404040 | 6 | #404040 | true | — |
| Mechanical / Electrical / Plumbing / Pipes / Ducts / etc. | 1 | #808080 | — | — | true | 30 |
| Rooms (color-fill scheme room-tagging plan) | 1 | #1976D2 | — | — | — | 80 |
| Grids | 2 | #000000 | — | — | — | — |
| Filters | `STING - Existing` halftone, `STING - Demolish` dashed red, `STING - New Construction` no override | | | | | |

### 6.3 `corp-standard-rcp` — A1 1:100 reflected ceiling plan

`extends: corp-standard-plan`. Inverts halftone strategy: walls /
floors / casework / furniture become halftone context; ceiling-mounted
families come to the foreground.

| Category | proj wt | proj col | halftone | notes |
|---|---|---|---|---|
| Walls | 4 | #808080 | true | context |
| Floors | 3 | #C0C0C0 | true | context |
| Furniture / Casework | 1 | #C0C0C0 | true | context |
| Ceilings | 5 | #000000 | false | grid lines visible |
| Lighting Fixtures | 4 | #000000 | false | full colour, on top |
| Air Terminals | 4 | #000000 | false | grilles + diffusers |
| Sprinklers | 3 | #E60000 | false | red, fire-protection convention |
| Fire Alarm Devices | 4 | #E60000 | false | red |
| Communication / Security Devices | 3 | #1976D2 | false | mid blue |

### 6.4 `corp-standard-section` — A1 1:50 architectural section

`extends: corp-base`. Cut weights heavier than plan (architects expect
sections to read first as cut planes, projection second).

| Category | proj wt | cut wt | notes |
|---|---|---|---|
| Walls | 4 | 7 | poché-ready |
| Floors | 4 | 7 | slabs read as primary cut |
| Roofs | 4 | 7 | roof cut primary |
| Ceilings | 4 | 6 | secondary cut |
| Doors / Windows | 4 | 5 | profile cut |
| Stairs / Ramps / Railings | 4 | 6 | sectioned through |
| Structural Columns / Framing | 5 | 8 | structure heaviest |
| Curtain Walls + Mullions + Panels | 4 | 6 | sectioned |
| Topography | 4 | 7 | ground line bold |
| MEP | 1 #808080 | — | halftone background |
| Grids | 2 | — | extended through section |

### 6.5 `corp-standard-elevation` — A1 1:100 architectural elevation

`extends: corp-base`. No cuts; surface texture matters; structural
framing halftoned as visible-but-secondary.

| Category | proj wt | proj col | halftone | visible |
|---|---|---|---|---|
| Walls | 5 | #000000 | — | true |
| Curtain Walls + Mullions + Panels | 4 | #000000 | — | true |
| Doors / Windows | 4 | #000000 | — | true |
| Roofs | 6 | #000000 | — | true |
| Floors | 4 | #000000 | — | true |
| Stairs / Railings | 4 | #000000 | — | true |
| Topography | 7 | #000000 | — | true (ground line heaviest) |
| Site / Planting | 3 | #000000 | true | true |
| Structural Columns / Framing | 3 | #808080 | true | true |
| MEP (all disciplines) | — | — | — | false (hidden) |
| Grids / Levels | 2 | #000000 | — | true |

### 6.6 `corp-standard-detail` — A3 1:20 architectural detail

`extends: corp-base`. No halftone anywhere. Heavy cuts, fine detail
components, dense hatching.

| Category | proj wt | cut wt |
|---|---|---|
| Walls | 5 | 7 |
| Floors | 5 | 6 |
| Roofs | 5 | 7 |
| Ceilings | 4 | 5 |
| Doors / Windows | 4 | 5 |
| Detail Components / Detail Lines | 3 | — |
| Filled Regions | — | — (use hatch palette) |
| Structural Framing / Columns | 5 | 7 |
| Dimensions | 3 | — |

### 6.7 `corp-structural-plan` — A1 1:100 structural plan

`extends: corp-base`. Industry convention: structural drawings show
their own discipline in red (BS 8666 / Eurocode drawing convention),
architectural context in halftone grey.

| Category | proj wt | proj col | cut wt | cut col | halftone |
|---|---|---|---|---|---|
| Structural Columns | 6 | #C00000 | 8 | #C00000 | — |
| Structural Framing | 5 | #C00000 | 7 | #C00000 | — |
| Structural Foundations | 6 | #C00000 | 8 | #C00000 | — |
| Structural Rebar | 4 | #C00000 | — | — | — |
| Walls | 3 | #808080 | 4 | #808080 | true |
| Floors | 3 | #808080 | 4 | #808080 | true |
| Doors / Windows | 2 | #C0C0C0 | — | — | true |
| Furniture / Casework | — | — | — | — | hidden |
| MEP (all) | — | — | — | — | hidden |
| Grids | 3 | #000000 | — | — | — |

### 6.8 `corp-coordination` — A1 1:50 multi-discipline clash review

`extends: corp-base` `templateMode: managed`
`discipline: Coordination` `visualStyle: Wireframe`
`tagColorScheme: "System"` `defaultTagStyle: "2BOLD_BLACK"`

| Category | proj wt | proj col | trans | halftone | visible |
|---|---|---|---|---|---|
| Walls / Floors / Roofs / Ceilings (architectural) | 3 | #404040 | 30 | true | true |
| Structural Framing / Columns | 4 | #C00000 | — | — | true |
| Mechanical Equipment / Ducts / Duct Fittings / Air Terminals | 4 | #1976D2 | — | — | true |
| Pipes / Pipe Fittings / Plumbing Fixtures | 4 | #2E7D32 | — | — | true |
| Electrical Equipment / Lighting / Conduits / Cable Trays | 4 | #E6A800 | — | — | true |
| Sprinklers / Fire Alarm | 4 | #E60000 | — | — | true |
| Furniture / Casework | — | — | — | — | false |
| Filters | `STING - New Construction` no override; existing halftone | | | | |

### 6.9 `corp-fabrication-shop` — A1 1:50 spool / shop drawing

`extends: corp-base` `templateMode: managed`
`tagColorScheme: "System"` `defaultTagStyle: "2BOLD_RED"`

| Category | proj wt | proj col | cut wt | cut col | halftone | visible |
|---|---|---|---|---|---|---|
| Pipes (own-discipline assembly) | 6 | #C00000 | 8 | #C00000 | — | true |
| Pipe Fittings | 5 | #C00000 | 7 | #C00000 | — | true |
| Pipe Insulations | 3 | #C00000 | — | — | — | true |
| Ducts / Duct Fittings (when fab pack hosts duct spools) | 6 | #C00000 | 8 | #C00000 | — | true |
| Mechanical Equipment | 5 | #000000 | 7 | #000000 | — | true |
| Walls / Floors / Ceilings (context) | 2 | #C0C0C0 | — | — | true | true |
| Structural Framing (context) | 3 | #808080 | — | — | true | true |
| Other MEP disciplines | 1 | #C0C0C0 | — | — | true | true |
| Furniture / Casework / Site | — | — | — | — | — | false |

### 6.10 `corp-presentation-rich` — A1 client-facing rendered plan

`extends: corp-base`. Soft greys, no MEP, accent fills via `colorFillSchemes`.

`tagColorScheme: "Discipline"` `defaultTagStyle: "2.5NOM_BLUE"`

| Category | proj wt | proj col | trans | visible |
|---|---|---|---|---|
| Walls / Curtain Walls | 4 | #404040 | — | true |
| Floors | 3 | #606060 | — | true |
| Roofs | 4 | #404040 | — | true |
| Doors / Windows | 3 | #404040 | — | true |
| Furniture / Casework | 2 | #606060 | — | true |
| Stairs / Railings / Ramps | 3 | #404040 | — | true |
| Site / Planting / Topography | 3 | #6D4C41 | 20 | true |
| Structural Columns / Framing | 2 | #B0B0B0 | 50 | true |
| MEP (all) | — | — | — | false (hidden) |
| Rooms (color-fill scheme department) | 1 | #1976D2 | 90 | true |
| Grids / Levels / Section Marks | — | — | — | false (hidden on presentation) |

### 6.11 `corp-presentation-mono` — A1 monochrome client-facing

`extends: corp-presentation-rich`. Clamp every projection colour to
greyscale. Override the few coloured cells:

| Category | proj col |
|---|---|
| Site / Planting / Topography | #404040 |
| Rooms color-fill | (drop scheme; keep boundary `#808080`) |

## 7. DrawingType → ViewStylePack default mapping

The 36 drawing types currently shipping with `viewStylePackId: null`
should resolve to a corporate pack by purpose + discipline + phase.
Routing table the JSON edit installs:

| Drawing-type pattern | viewStylePackId |
|---|---|
| `arch-plan-*`, `arch-rcp-*` (rcp uses dedicated child), `arch-site-*`, `arch-roof-*`, `arch-floor-finishes-*`, `arch-fire-strategy-*`, `arch-accessibility-*` | `corp-standard-plan` (rcp → `corp-standard-rcp`) |
| `arch-section-*`, `arch-interior-elev-*` | `corp-standard-section` (interior elev → `corp-standard-elevation`) |
| `arch-elev-*` | `corp-standard-elevation` |
| `arch-detail-*`, `struct-rebar-detail-*` | `corp-standard-detail` |
| `arch-window-schedule-*`, `door-schedule-*` | `corp-standard-detail` (legend/schedule needs no pack but registry expects one for stamping) |
| `struct-plan-*`, `struct-foundation-*`, `struct-section-*` | `corp-structural-plan` |
| `mep-plan-*`, `mep-hvac-duct-*`, `mep-plantroom-*`, `elec-power-*`, `elec-lighting-*`, `elec-fire-alarm-*`, `elec-riser-*`, `plumb-drainage-*`, `fm-asset-location-*` | `corp-standard-plan` (MEP packs ship in a future commit; plan baseline halftones MEP appropriately) |
| `mep-coord-*`, `coord-clash-*` | `corp-coordination` |
| `pipe-spool-*`, `duct-spool-*` | `corp-fabrication-shop` |
| `pres-3d-axon-*`, `pres-perspective-*`, `pres-exterior-elev-*`, `pres-render-board-*`, `pres-context-site-*` | `corp-presentation-rich` |
| `clar-markup-*`, `clar-rfi-*`, `clar-design-intent-*` | `corp-clarification` |
| `legend-*`, `handover-*` | `corp-standard-detail` (uniform sheet styling) |

## 8. Verification plan

The JSON changes in this branch are not Revit-tested (Linux sandbox,
no Revit API). Verification a Windows reviewer should run:

1. Open an existing project with the plugin loaded.
2. Run `DrawingTypes_Reload` (force registry refresh).
3. Run `DrawingTypes_Inspect` — every pack should show non-zero VG
   count (no longer "visibility-only stub").
4. Open a clean architectural plan, apply DrawingType
   `arch-plan-A1-1to100`, confirm:
   - Walls cut at 0.70 mm (Revit weight 7)
   - Walls projection at 0.35 mm (weight 5)
   - MEP categories halftone grey
   - Filter `STING - Existing` shows existing-phase elements halftoned
5. Run `DrawingTypes_SyncStyles` — drift count should be 0 immediately
   after apply (confirms checksum matches).
6. Edit a single category override on the saved view. Re-run SyncStyles —
   drift count should rise to 1 (proves drift detection works against the
   newly-correct pack).
7. Repeat 4–6 for `corp-standard-section`, `corp-coordination`,
   `corp-fabrication-shop`, `corp-presentation-rich`.

## 9. References

- BS EN ISO 19650-1:2018 §A.5 — drawing presentation conventions.
- BS 1192:2007+A2:2016 — annex A line-weight ladder.
- ISO 128-20:1996 / ISO 128-23:1999 — line conventions.
- ISO 13567-1:2017, -2:2017 — layer naming + colour conventions.
- AIA CAD Layer Guidelines (NCS) — colour-by-discipline pen tables.
- NBS BIM Toolkit — Uniclass 2015 colour mapping for system / function.
- BS 8666:2020 — structural rebar drawing conventions (colour red for
  reinforcement primary).
- Revit API: `OverrideGraphicSettings` (revitapidocs 2025).
- Project files cross-referenced: `Core/Drawing/ViewStylePack.cs`,
  `Core/Drawing/ViewStylePackApplier.cs`, `Core/Drawing/ManagedTemplateSyncer.cs`,
  `Core/Drawing/DrawingTypePresentation.cs`, `UI/RevitVgEditor.cs`,
  `Data/STING_VIEW_STYLE_PACKS.json`, `Data/STING_DRAWING_TYPES.json`.
