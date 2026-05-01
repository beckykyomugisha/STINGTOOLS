# STING Title-Block Slot Taxonomy

> Canonical slot vocabulary for the STING title-block factory. Every slot in `STING_TITLE_BLOCKS.json` carries a **purposeTag** from this list — the auto-placer (`TitleBlock_AutoPlaceViewports`) routes views to slots by matching this tag, the SVG preview renderer (`tools/generate_title_block_previews.py`) colour-codes slot outlines by it, and the slot system as a whole connects the JSON spec to live Revit API automation.

## 1. Categories

Every slot has a `category` that controls visual treatment in previews + auto-placer behaviour:

| Category | Use | Stroke pattern | Fill |
|---|---|---|---|
| `primary`    | Main drawable area — full / half / quad / spool isometric / RFI sketch | solid                | 5 % colour |
| `auxiliary`  | Content slot for key-plans / notes / legends / schedules / revision-history | dashed `3,2`         | 10 % colour |
| `symbol`     | Small-graphic slot — north arrow / scale bar / discipline chip / QR | dotted `1,1.5`       | none |
| `overlay`    | Sits on top of another slot (caption over render, RFI markup over plan) | dot-dash `5,1.5,1.5,1.5` | none, transparent |

## 2. Purpose tags + colour palette

The colour palette in `tools/generate_title_block_previews.py` (`PURPOSE_PALETTE`) keys to these tags:

### Primary

| Tag | Colour | Use |
|---|---|---|
| `main-plan` | `#1F4E79` deep blue | Full drawable, main plan / 3D / section |
| `main-plan-half-left` / `-half-right` | `#5fa8d3` light blue | 50/50 split |
| `quad-bottom-left` / `quad-bottom-right` / `quad-top-left` / `quad-top-right` | `#5fa8d3` light blue | 4-up grid quadrant |
| `fabrication-isometric` | `#E91E63` magenta | Fabrication shop drawing primary |
| `rfi-sketch` | `#00BCD4` cyan | Clarification/RFI sketch primary |
| `presentation-render` | `#FFEB3B` yellow | Full-bleed render |
| `presentation-perspective` | `#FBC02D` amber | Architectural perspective |

### Auxiliary content

| Tag | Colour | Use | TB_SHOW_*_BOOL toggle |
|---|---|---|---|
| `key-plan` | `#4CAF50` green | Small location overview | `TB_SHOW_KEY_PLAN_BOOL` |
| `aerial-key` | `#8BC34A` light green | Site-wide aerial context |  |
| `notes` | `#607D8B` slate | General notes legend |  |
| `discipline-legend` | `#FF9800` orange | Discipline-specific symbol key |  |
| `material-legend` | `#FFB74D` light orange | Material schedule / colour key |  |
| `fire-legend` | `#E53935` red | Fire compartmentation legend |  |
| `accessibility-legend` | `#7B1FA2` purple | Part M / wayfinding legend |  |
| `falls-legend` | `#0288D1` blue | Drainage falls legend |  |
| `lighting-legend` | `#FFC107` amber | Luminaire schedule for lighting plans |  |
| `schedule` | `#9C27B0` deep purple | Generic Revit schedule view |  |
| `bom` | `#9C27B0` deep purple | Bill of Materials | (alias of `schedule`) |
| `cut-list` | `#AD1457` dark pink | Lengths / cut summary for fab |  |
| `revision-history` | `#F44336` red | Revit revision schedule | `TB_SHOW_REV_TABLE_BOOL` |
| `caption` | `#795548` brown | Drawing-title caption (presentation) |  |
| `recipient-to` / `recipient-from` | `#607D8B` slate | Transmittal recipient blocks |  |
| `regulator-stamp` | `#795548` brown | Authority seal placeholder |  |
| `discipline-band` | `#FF9800` orange | Discipline-coloured banner | `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` |

### Symbol

| Tag | Colour | Use | TB_SHOW_*_BOOL toggle |
|---|---|---|---|
| `north-arrow` | `#009688` teal | North-arrow nested family | `TB_SHOW_NORTH_ARROW_BOOL` |
| `scale-bar` | `#009688` teal | Scale-bar nested family | `TB_SHOW_SCALEBAR_BOOL` |
| `qr-code` | `#37474F` dark grey | QR-code post-export stamp | `TB_SHOW_QR_CODE_BOOL` |

### Overlay / specialty

| Tag | Colour | Use |
|---|---|---|
| `spool-refs` | `#880E4F` dark pink | Cross-spool reference list (overlay on fab sheet) |
| `markup-plan` | `#00BCD4` cyan | Review-cloud overlay on a base plan |
| `rfi-query` | `#FF5722` deep orange | RFI question text legend |

## 3. Revit API automation flow

The slot system bridges three layers of the codebase:

```
┌──────────────────────────────────────────────────────────────────────┐
│   STING_TITLE_BLOCKS.json   ──────►  TitleBlockFactory  ──►  .rfa    │
│   (slot declarations)                                                │
│                                                                      │
│             │                                                        │
│             │ purposeTag                                             │
│             ▼                                                        │
│                                                                      │
│   STING_VIEWPORT_PLACEMENT_RULES.json                                │
│   (ViewType + name pattern → purposeTag)                             │
│             │                                                        │
│             ▼                                                        │
│                                                                      │
│   TitleBlock_AutoPlaceViewports                                      │
│   ─────────────────────────                                          │
│   1. For each selected View v in the project browser                 │
│   2. Resolve purposeTag from rules                                   │
│   3. Look up slot on active sheet's title-block .rfa                 │
│   4. Viewport.Create at slot centre                                  │
│   5. Apply slot.viewportType / scaleHint                             │
│   6. Honour respectShowToggle ↔ TB_SHOW_*_BOOL                       │
│             │                                                        │
│             │ slot empty + automationHook set?                       │
│             ▼                                                        │
│                                                                      │
│   automationHook command (e.g. Legend_BuildNotes,                    │
│                                  Revisions_AutoPopulateSchedule,     │
│                                  Symbol_PlaceNorthArrow,             │
│                                  Fab_BuildBOMSchedule)               │
└──────────────────────────────────────────────────────────────────────┘
```

### 3.1 Slot definition (JSON spec)

```jsonc
{
  "id": "NOTES",
  "anchor": [690, 130], "size": [120, 100],
  "purposeTag": "notes",
  "category": "auxiliary",
  "viewportType": "No Title",
  "automationHook": "Legend_BuildNotes",
  "respectShowToggle": false,
  "createReferencePlanes": false,
  "showCornerMarker": true,
  "description": "Notes panel — drafting view or legend with general notes"
}
```

### 3.2 Slot bounds resolution (runtime)

`TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, titleBlock)` returns a `Dictionary<string, SlotBounds>` keyed by slot id. Two sources, JSON-first:

1. **Primary**: `TitleBlockSpecRegistry.Resolve(library, family)` — extends-resolved spec, slot anchor + size + purposeTag + viewportType + scaleHint read straight from JSON.
2. **Override**: Named reference planes inside the `.rfa` (`<id>_TOP/BOT/LFT/RGT`) override the JSON bbox. Title-block families on Revit 2025 reject ref-plane creation, so this path usually no-ops; legacy or hand-edited families still resolve.

### 3.3 Auto-placer routing

```csharp
// TitleBlockAutoPlaceViewportsCommand.Execute
var rules    = ViewportPlacementRules.Load();          // STING_VIEWPORT_PLACEMENT_RULES.json
var slotMap  = TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, tb);

foreach (var v in selectedViews)
{
    var tag    = ResolvePurposeTag(rules, v);          // (ViewType, name) → tag
    var slotId = ResolveSlotForTag(slotMap, tag, rules);
    if (slotId == null) continue;                      // (or fall back to default)

    var bounds = slotMap[slotId];
    if (bounds.ScaleHint.HasValue) v.Scale = bounds.ScaleHint.Value;

    var vp = Viewport.Create(doc, sheet.Id, v.Id, bounds.Centre);
    if (bounds.ViewportType != null)
        vp.ChangeTypeId(ResolveViewportTypeId(doc, bounds.ViewportType));
}
```

### 3.4 `automationHook` — populating empty slots

When a slot's `purposeTag` doesn't match any view in the project browser AND the slot has an `automationHook` set, `TitleBlock_AutoPlaceViewports` (Phase 173) can dispatch to the hooked command, which mints the missing view on demand. Hooks shipped today (still stubbed at command level — Phase 173 wires them):

| Hook | Behaviour | Existing command |
|---|---|---|
| `Legend_BuildNotes` | Mint a project-default notes legend if none exists | (Phase 173) |
| `Legend_DisciplineLegendBind` | Place LGD-{DISC}-NOTES legend view from the legend builder | `DisciplineLegendBind` (existing) |
| `Legend_BuildCaption` | Mint a presentation-caption drafting view | (Phase 173) |
| `Legend_BuildRFIQuery` | Mint a one-off legend for a single RFI query | (Phase 173) |
| `Legend_BuildStatutoryDeclaration` | Mint a statutory-declaration legend per regulator | (Phase 173) |
| `Revisions_AutoPopulateSchedule` | Use Revit's Revision feature to populate the rev schedule | `RevisionSync` (existing — adapter) |
| `Symbol_PlaceNorthArrow` | Drop a `STING_NORTH_ARROW.rfa` family at the slot anchor | (Phase 173) |
| `Symbol_PlaceScaleBar` | Drop a `STING_SCALE_BAR.rfa` scaled to the active view scale | (Phase 173) |
| `ExportSheetRegister` | Run sheet-register export — used by REGISTER_A1 family | `ExportSheetRegister` (existing) |
| `Transmittal_PopulateRecipient` / `_PopulateSender` | Read the active transmittal record + populate the TO/FROM blocks | `CreateTransmittalOrchestrated` (existing — adapter) |
| `Transmittal_BuildAccompanying` | Build the accompanying-documents schedule from the export bundle | (Phase 173) |
| `Submission_PlaceStamp_KCCA` / `_ERA` / `_NEMA` | Place the regulator's official seal image | (Phase 173) |
| `Fab_BuildBOMSchedule` / `_BuildCutList` / `_LinkSpoolRefs` | Wire to fabrication subsystem (`AssemblyBuilder` / `AssemblyViewBuilder`) | Phase 168/169 (existing — adapters) |
| `Markup_AttachReviewCloud` | Wrap the right pane of a CLARIFICATION sheet in a markup overlay | (Phase 173) |
| `TB_ApplyDisciplineBand` | Apply discipline-coded fill to the divider band | (Phase 173) |

### 3.5 `respectShowToggle` — visibility booleans

When `respectShowToggle: true`, the auto-placer reads the corresponding `TB_SHOW_*_BOOL` parameter on the title-block instance and skips placement (or hides existing viewport) if false. Mapping:

| purposeTag | TB_SHOW_*_BOOL parameter |
|---|---|
| `key-plan` | `TB_SHOW_KEY_PLAN_BOOL` |
| `north-arrow` | `TB_SHOW_NORTH_ARROW_BOOL` |
| `scale-bar` | `TB_SHOW_SCALEBAR_BOOL` |
| `revision-history` | `TB_SHOW_REV_TABLE_BOOL` |
| `qr-code` | `TB_SHOW_QR_CODE_BOOL` |
| `discipline-band` | `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` |

## 4. Portrait vs landscape — convention

Per `TITLE_BLOCK_FAMILY_DESIGN.md` § 4: **bottom strip, both orientations**. A3/A4 portrait specifically "collapse the right strip to a bottom strip". The differences between landscape and portrait are:

| Aspect | Landscape | Portrait |
|---|---|---|
| Strip placement | Bottom edge, full width | Bottom edge, full width (same convention) |
| Strip height | 110/130 mm typical for A0–A1, scaled for smaller | Same |
| Drawable shape | Wide → main slot horizontal; aux column on right | Tall → main slot vertical; aux column has more vertical height |
| Auxiliary slot density | 4 aux slots fit comfortably in the right column | 5+ aux slots possible since drawable is taller |
| Fab/spool drawings | Right BOM strip natural fit | Bottom BOM strip would compress the title strip — fab variants stay landscape-only |

The polish script's working-sheet auxiliary-column logic (`KP / NOTES / LEGEND / REV` stack) works for both orientations because it indexes off the drawable-zone bounds, which scale with the chosen orientation.

## 5. Slot palette per family — visual confirmation

Open `docs/title_blocks/CATALOGUE.html` to see all 45 families with colour-coded slot outlines. Each preview includes a **slot legend** strip below the title bar showing every unique `purposeTag` in that family with its colour swatch.

## 6. Adding a new purposeTag

1. Pick a colour and add to `PURPOSE_PALETTE` in `tools/generate_title_block_previews.py`.
2. Decide a category (`primary` / `auxiliary` / `symbol` / `overlay`).
3. If it should respond to a `TB_SHOW_*_BOOL`, add the mapping in § 3.5 + the runtime check in `TitleBlockAutoPlaceViewportsCommand`.
4. If it has an `automationHook`, register the command in `StingCommandHandler` and add an adapter to the existing legend / revision / fabrication subsystem.
5. Add routing rules in `STING_VIEWPORT_PLACEMENT_RULES.json` (with optional aliases for graceful fallback).
6. Use the new tag in slot specs in `STING_TITLE_BLOCKS.json` (or via the polish script).
7. Re-run `tools/generate_title_block_previews.py` to regenerate the catalogue.
