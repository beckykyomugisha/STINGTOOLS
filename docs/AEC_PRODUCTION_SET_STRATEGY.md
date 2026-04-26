# AEC Production-Set Strategy

A reference catalogue mapping every drawing a typical AEC firm produces across
a project lifecycle to a (DrawingType, ViewStylePack) pair, so generators can
route automatically and presentation stays consistent across stages, disciplines
and audiences.

This is the planning companion to `STING_DRAWING_TYPES.json` and
`STING_VIEW_STYLE_PACKS.json`. Adopt as much or as little as fits your firm.

---

## 1. The pack catalogue (corp-base + 10 specialised)

Packs are organised so most production work uses one of three baseline
plan packs (`corp-construction`, `corp-presentation`, `corp-coordination`),
with specialised packs branching off via `extends`.

| Pack id | Purpose | Extends | Visual signature |
|---|---|---|---|
| `corp-base` | House defaults — line weights, text/dim styles, hatch palette, "Existing/Demolished/New" filters | — | ISO 13567 mono palette, STING-2.5mm text, STING-Linear dims |
| `corp-construction` | IFC and tender drawings (full info, bold proposed, halftoned existing) | `corp-base` | Bold cut lines, full hatch, no presentation effects |
| `corp-presentation` | Client-facing renders, design narratives, marketing | `corp-base` | Light line weights, rich colour, no grids/levels, halftoned context |
| `corp-coordination` | MEP coordination, clash, federated views | `corp-base` | Discipline colour-coded, all systems visible halftoned for context |
| `corp-asbuilt` | Hand-back as-built sets | `corp-construction` | Mono only, no proposed/demolition graphics, "AS BUILT" overlay |
| `corp-fabrication-shop` | Pipe / duct / weld spool sheets | `corp-construction` | Heavy lines (1.4× scale), part-mark filters, ISO 6412 symbol family |
| `corp-survey` | Existing-condition surveys, demolition records | `corp-base` | Light grey 0.5× weights, dimension-heavy, photo callouts |
| `corp-authority` | Planning, building control, conservation submissions | `corp-construction` | Authority-required line weights, NTS suppressed, statutory tags |
| `corp-clarification` | RFI sketches, mark-ups, query packs | `corp-base` | Red-line markup, revision-cloud bias, tab-style query log |
| `corp-handover` | FM / O&M / asset-handover drawings | `corp-construction` | Asset categories accented, base halftoned, COBie tags only |
| `corp-presentation-mono` | Mono / black-and-white render alternative | `corp-presentation` | Greyscale palette, halftones, no colour fills |

---

## 2. Drawing-Type matrix (RIBA Stage × Discipline × Output)

Each row is a `(DrawingType.Id, ViewStylePack.Id)` pair, with the kind of view
it produces and a recommended scale. Stage codes follow RIBA Plan of Work 2020;
discipline codes follow ISO 19650-2 § A.5.

### 2.1 Pre-design / Stage 0–1 (Site appraisal + briefing)

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Site location plan | `site-location-A1-1to1250` | `corp-presentation` | 1:1250 | Plan over OS context |
| Site survey | `site-survey-A1-1to200` | `corp-survey` | 1:200 | Plan w/ levels, trees, services |
| Existing site photos | `site-photo-board-A1` | `corp-presentation` | NTS | Photo composite |
| Demolition plan | `arch-demolition-A1-1to100` | `corp-survey` | 1:100 | Plan w/ red-line demo |
| Phasing plan | `coord-phasing-A1-1to200` | `corp-construction` | 1:200 | Coloured phase blocks |

### 2.2 Concept design / Stage 2

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Site strategy | `arch-site-strategy-A1-1to500` | `corp-presentation` | 1:500 | Plan w/ massing |
| Massing study | `arch-massing-A1` | `corp-presentation` | NTS | 3D axon |
| Floor plans (concept) | `arch-plan-A1-1to200-concept` | `corp-presentation` | 1:200 | Plan |
| Sections (concept) | `arch-section-A1-1to200-concept` | `corp-presentation` | 1:200 | Section |
| Concept narrative | `pres-design-intent-A1` | `corp-presentation` | NTS | Plan + 3D + caption |
| Mood board | `pres-mood-board-A1` | `corp-presentation` | NTS | Image grid |

### 2.3 Schematic / Stage 3 (Developed design)

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Floor plans | `arch-plan-A1-1to100` | `corp-construction` | 1:100 | Plan |
| RCP | `arch-rcp-A1-1to100` | `corp-construction` | 1:100 | Reflected ceiling plan |
| Roof plan | `arch-roof-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Site plan | `arch-site-A1-1to500` | `corp-construction` | 1:500 | Plan |
| Floor finishes | `arch-floor-finishes-A1-1to100` | `corp-construction` | 1:100 | Plan w/ material legend |
| Elevations | `arch-elev-A1-1to100` | `corp-construction` | 1:100 | Elevation |
| Sections | `arch-section-A1-1to50` | `corp-construction` | 1:50 | Section |
| Door schedule | `door-schedule-A2` | `corp-construction` | NTS | Schedule |
| Window schedule | `arch-window-schedule-A2` | `corp-construction` | NTS | Schedule |
| Coordinated MEP plan | `mep-coord-A1-1to50` | `corp-coordination` | 1:50 | Plan w/ all systems |

### 2.4 Technical / Stage 4 (Construction documentation)

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| **Architectural** |
| Setting-out plan | `arch-setting-out-A1-1to50` | `corp-construction` | 1:50 | Plan w/ grids + dims |
| Detail sections | `arch-detail-A3-1to20` | `corp-construction` | 1:20 | Section detail |
| Wall types schedule | `arch-wall-types-A2` | `corp-construction` | NTS | Schedule + types |
| Interior elevations | `arch-interior-elev-A1-1to50` | `corp-construction` | 1:50 | Elevation |
| Stair / lift cores | `arch-core-A1-1to50` | `corp-construction` | 1:50 | Plan + section |
| Fire strategy | `arch-fire-strategy-A1-1to100` | `corp-authority` | 1:100 | Plan w/ fire lines |
| Accessibility | `arch-accessibility-A1-1to100` | `corp-authority` | 1:100 | Plan w/ M-clauses |
| **Structural** |
| Foundation plan | `struct-foundation-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Slab plans | `struct-slab-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Framing plans | `struct-framing-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Column schedule | `struct-column-schedule-A2` | `corp-construction` | NTS | Schedule |
| Rebar details | `struct-rebar-detail-A3-1to20` | `corp-construction` | 1:20 | Section detail |
| Connections | `struct-connection-A3-1to10` | `corp-construction` | 1:10 | Detail |
| **Mechanical** |
| HVAC duct plan | `mep-hvac-duct-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Plant room | `mep-plantroom-A1-1to50` | `corp-construction` | 1:50 | Plan + section |
| Riser diagrams | `mep-hvac-riser-A1-1to50` | `corp-construction` | 1:50 | Section |
| Schematic (single-line) | `mep-hvac-schematic-A1` | `corp-construction` | NTS | Schematic |
| **Electrical** |
| Power layout | `elec-power-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Lighting layout | `elec-lighting-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Fire alarm | `elec-fire-alarm-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Containment (cable tray) | `elec-containment-A1-1to100` | `corp-construction` | 1:100 | Plan |
| LV riser diagram | `elec-riser-A2-1to100` | `corp-construction` | 1:100 | Riser schematic |
| Single-line diagram | `elec-sld-A1` | `corp-construction` | NTS | Schematic |
| **Plumbing / Public health** |
| DCW / DHW plans | `plumb-water-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Drainage plan | `plumb-drainage-A1-1to100` | `corp-construction` | 1:100 | Plan |
| Plumbing risers | `plumb-riser-A1` | `corp-construction` | NTS | Schematic |
| **Fire protection** |
| Sprinkler layout | `fp-sprinkler-A1-1to100` | `corp-construction` | 1:100 | Plan |

### 2.5 Construction / Stage 5

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| **Fabrication shop drawings** |
| Pipe spool | `pipe-spool-A1-1to50` | `corp-fabrication-shop` | 1:50 | Spool layout w/ ISO 6412 |
| Duct spool | `duct-spool-A1-1to50` | `corp-fabrication-shop` | 1:50 | Spool layout |
| Weld map | `weld-map-A1-1to50` | `corp-fabrication-shop` | 1:50 | Plan w/ weld symbols |
| Cut list | `cutlist-A2` | `corp-fabrication-shop` | NTS | Schedule |
| Isometrics | `iso-A3` | `corp-fabrication-shop` | NTS | 3D iso |
| **Site works** |
| RFI sketch | `clar-rfi-A3` | `corp-clarification` | varies | Plan/section + question |
| Mark-up | `clar-markup-A1` | `corp-clarification` | varies | Plan + revision strip |
| Issued for construction | uses base IFC types above | `corp-construction` | varies | — |

### 2.6 Handover / Stage 6

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| As-built plans | `arch-asbuilt-A1-1to100` | `corp-asbuilt` | 1:100 | Plan w/ AS BUILT stamp |
| FM asset location | `fm-asset-location-A1-1to100` | `corp-handover` | 1:100 | Plan w/ assets accented |
| O&M index | `handover-A1` | `corp-handover` | NTS | Sheet index |
| Health & safety file | `hsf-A2` | `corp-handover` | NTS | Schedule |
| Maintenance access | `fm-access-A1-1to100` | `corp-handover` | 1:100 | Plan w/ access zones |

### 2.7 Authority submissions

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Planning submission | `auth-planning-A1-1to200` | `corp-authority` | 1:200 | Plan |
| Building control | `auth-bc-A1-1to100` | `corp-authority` | 1:100 | Plan |
| Conservation area | `auth-heritage-A1-1to100` | `corp-authority` | 1:100 | Plan + photos |
| Section 106 | `auth-s106-A1` | `corp-authority` | varies | — |

### 2.8 Client presentation / marketing

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Presentation 3D axon | `pres-3d-axon-A1` | `corp-presentation` | NTS | 3D + key plan |
| Perspective | `pres-perspective-A1` | `corp-presentation` | NTS | Full-bleed perspective |
| Render board | `pres-render-board-A1` | `corp-presentation` | NTS | 4-up renders |
| Site context | `pres-context-site-A1` | `corp-presentation` | 1:500 | Aerial + caption |
| Exterior elevation (rendered) | `pres-exterior-elev-A1` | `corp-presentation` | 1:200 | Elevation w/ materials |
| Material strip | `pres-material-strip-A2` | `corp-presentation` | NTS | Material samples |
| Interior render | `pres-interior-A1` | `corp-presentation` | NTS | Render |
| Marketing 4-pager | `marketing-4pp-A1` | `corp-presentation` | NTS | Composite |

### 2.9 Coordination / Federation

| Drawing | DrawingType | Pack | Scale | View kind |
|---|---|---|---|---|
| Clash sheet | `coord-clash-A1-1to50` | `corp-coordination` | 1:50 | Plan w/ clash callouts |
| Federation 3D | `coord-fed-3d-A1` | `corp-coordination` | NTS | 3D federated |
| Coordination section | `coord-section-A1-1to50` | `corp-coordination` | 1:50 | Section all systems |
| Section cuts | `coord-cuts-A1-1to50` | `corp-coordination` | 1:50 | Multi-section |

---

## 3. Routing rules — automatic profile selection

The matrix above is consumed by `STING_DRAWING_TYPES.json:routing[]`. Pattern:

```
{ "discipline": "A", "phase": "STAGE_4", "docType": "PLAN",       "drawingTypeId": "arch-plan-A1-1to100"      }
{ "discipline": "M", "phase": "STAGE_4", "docType": "HVAC_DUCT",  "drawingTypeId": "mep-hvac-duct-A1-1to100"  }
{ "discipline": "*", "phase": "PRESENTATION", "docType": "AXON",  "drawingTypeId": "pres-3d-axon-A1"          }
{ "discipline": "*", "phase": "*",       "docType": "RFI",        "drawingTypeId": "clar-rfi-A3"              }
```

When `BatchSectionsCommand` / `ShopDrawingComposer` / `BatchSheetsCommand` are
called, they pass `(discipline, phase, docType)` to `DrawingDispatcher.Resolve`
which returns the right DrawingType — and that DrawingType already names the
right ViewStylePack. End-to-end automation, no per-drawing dialog.

---

## 4. Workflow guidance — choosing the right (DrawingType, Pack) pair

### Production drawings (stages 4–5 IFC)
Bind every DrawingType to **`corp-construction`** unless there's a strong
reason to deviate. House the per-category VG overrides here. Use Revit view
templates to lock graphics; the pack adds filter rules + tag families on top.

### Coordination drawings (clash, federated)
Bind to **`corp-coordination`**. The pack's VG overrides should colour-code
discipline categories (M=blue, E=yellow, P=green, FP=red) and halftone the
arch base for context.

### Fabrication
Bind spool DrawingTypes to **`corp-fabrication-shop`**. The pack should set
`lineWeightScale = 1.4`, point at fabrication-specific text + dim styles,
and include filters that highlight the spool's part-mark.

### Client-facing
Bind to **`corp-presentation`** for colour boards + renders, or
**`corp-presentation-mono`** for greyscale. Strip grids, levels, dimensions.
DrawingType.ViewTemplateName usually empty — let the pack drive.

### Authority submissions
Bind to **`corp-authority`**. Each submitting authority can extend further
(e.g. `corp-authority-london`, `corp-authority-conservation`) — set
`Extends: corp-authority` and override the differences only.

### As-built handover
Bind to **`corp-asbuilt`**. Pack should suppress proposed/demolition graphics,
add an "AS BUILT" title-block stamp via title-block param binding, and lock
revision codes to "P0".

### RFI / mark-ups
Bind to **`corp-clarification`**. Pack should bias revision clouds + a query
log table block. Sheet number pattern: `RFI-{nnn}` not the project pattern.

---

## 5. Build-out plan — recommended sequencing

Don't try to author all 80+ drawing types and 11 packs at once. Sensible order:

1. **Pack baseline** (1 day): `corp-base` + `corp-construction` +
   `corp-presentation` + `corp-coordination`. These cover ~70% of work.
2. **Stage 4 IFC drawing types** (1–2 days): every row in §2.4 above.
   Bind all to `corp-construction` initially.
3. **Routing** (½ day): the rules table in §3 — once written, every batch
   generator picks the right type.
4. **Specialised packs** (per-project, as needed): `corp-fabrication-shop`,
   `corp-asbuilt`, `corp-handover`, `corp-authority`, `corp-clarification`.
5. **Presentation drawings** (per-project): `corp-presentation` is the
   workhorse; the DrawingTypes are short-lived per pitch.

Use the editor's **Clone** button liberally — it's faster to clone an
existing pack/type and tweak than to author from scratch.

---

## 6. Copying view templates between packs and types

Two new buttons added in Phase 136 (see Drawing Type Editor):

- **View Style Packs tab → "Push template → bound types"**: takes the
  current pack's `ViewTemplate` and writes it onto every Drawing Type bound
  to this pack via `ViewStylePackId`. Use when you've changed the pack
  default and want every drawing to adopt it explicitly (rather than
  relying on the runtime fallback).
- **Drawing Types tab → "↑ Push to pack"**: takes the current Drawing
  Type's `ViewTemplateName` and writes it to the bound pack's
  `ViewTemplate`. Use when you've authored a drawing-specific template
  you want adopted as the new pack default.
- **Drawing Types tab → "Use pack template"**: clears the Drawing Type's
  `ViewTemplateName` so it inherits from the pack. Useful after a "push to
  pack" if you want clean inheritance going forward.

Both push operations flip the affected entries' `Origin` to `project` so
they save to `<project>/_BIM_COORD/` overrides, never the corporate baseline.
