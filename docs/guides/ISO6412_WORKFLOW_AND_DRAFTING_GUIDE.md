# ISO 6412 Spool Symbol Workflow & Drafting Quality Guide

**Applies to**: `STING_ISO6412_SYMBOLS.json` → `SymbolLibraryCreator` → `.rfa` families  
**Output folder**: `<project>/_BIM_COORD/Families/Symbols/ISO6412/`  
**Standard**: ISO 6412 / BS 308 Part 3 — Piping, Duct & Conduit Isometric Symbols  
**Date**: 2026-05

---

## 1. Overview of the Pipeline

```
STING_ISO6412_SYMBOLS.json
        │
        ▼
  SymbolLibraryCreator.cs        ← reads JSON, opens family template
        │  Opens: Generic Annotation.rft
        │  familyType: "GenericAnnotation"
        │  DrawGeometry → NewSymbolicCurve / FilledRegion.Create
        │  Saves → _BIM_COORD/Families/Symbols/ISO6412/<name>.rfa
        ▼
  MepSymbolEngine.cs             ← places families on spool drafting views
        │  ResolveFamilySymbol() — recursive scan of _BIM_COORD/Families/Symbols/
        │  Places as FamilyInstance (Detail Item)
        ▼
  Spool isometric drafting view  ← view.Scale = 1 (paper scale)
```

"Create All Symbols" (TAGS tab → Symbols section) runs **all** batches in
`SymbolBatchHelper.AllBatches`, including the ISO 6412 batch last.

---

## 2. Scale Settings — Complete Reference

### 2.1 The Scale Chain

Every ISO 6412 family goes through three scale multipliers. Understand all three
before adjusting geometry.

| Stage | Variable | Default | Where set |
|---|---|---|---|
| **JSON normalised coord** | `-0.5 … +0.5` | fixed | `STING_ISO6412_SYMBOLS.json` geometry arrays |
| **`symbolSize`** (mm) | paper mm per half-width | see §2.2 | per-symbol in JSON |
| **Revit view scale** | `view.Scale` | **1** for spool views | `MepSymbolEngine` detects SLD/spool views and forces `Symbol Scale = 1` |

The internal conversion is:  
`Revit feet = normCoord × symbolSize_mm / 304.8`

So a line from `x1: -0.5` to `x1: +0.5` with `symbolSize: 6.0` draws
`6.0 mm` wide on paper — independent of view scale because `view.Scale = 1`.

### 2.2 `symbolSize` Values Used in This Library

| Category | `symbolSize` (mm) | Plotted half-width | Reason |
|---|---|---|---|
| Pipe fittings | **6.0** | 3.0 mm each side of centreline | Standard pipe symbol width on 1:1 spool iso |
| Flanges | **6.0** | 3.0 mm | Matches pipe symbol width |
| Valves | **6.0** | 3.0 mm | Body of bowtie/circle is 6 mm nominal |
| Strainers | **6.0** | 3.0 mm | Matches pipe path |
| Steam traps | **6.0** | 3.0 mm | Matches pipe path |
| Equipment | **6.0** | 3.0 mm | Separator circles match pipe |
| Pumps | **6.0** | 3.0 mm | Circle diameter = 6 mm |
| Duct fittings | **8.0** | 4.0 mm each side of duct CL | Wider duct wall lines need more space |
| Conduit fittings | **5.0** | 2.5 mm | Conduit is narrower than pipe |
| Cable tray | **5.0** | 2.5 mm | Tray walls narrower than duct |
| Penetrations | **5.0** | 2.5 mm | Generic sleeve width |
| Welds | **4.0** | 2.0 mm | Weld marks are compact annotations |
| Hangers | **5.0** | 2.5 mm | Support symbols, medium size |
| Insulation | **5.0** | 2.5 mm | Wraps around pipe symbol |
| Notation | **5.0** | 2.5 mm | Callout/arrow annotations |

### 2.3 Adjusting Size After Generation

To resize a symbol category, edit the `symbolSize` field in the JSON and
re-run **Create All Symbols** (existing `.rfa` files are overwritten).

Do **not** change view scale — spool views must stay at `view.Scale = 1`.

### 2.4 The `Symbol Scale` Shared Parameter

Every generated family carries the `Symbol Scale` (INTEGER) shared parameter
from `Families/ISO6412/STING_ISO_SYMBOL_TEMPLATE.params.txt`.

`MepSymbolEngine` reads this at placement and writes it back. Currently unused
for geometry scaling (geometry is baked at creation time), but reserved for
a future runtime-scale modifier so families can be resized without regeneration.
Do **not** delete or rename this parameter.

---

## 3. Running the Generator

### 3.1 First Run

1. Open a Revit project (`.rvt`) — the generator needs an active document to
   resolve the `_BIM_COORD` output path.
2. Go to **TAGS tab → Symbols section → Create All Symbols**.
3. The command runs all 12 batches sequentially. The ISO 6412 batch is last.
4. Progress is shown in a modeless dialog. Expect ~2–4 seconds per family on
   a modern workstation → roughly 5–10 minutes for all 164 ISO 6412 families.
5. Families are saved to:
   ```
   <project folder>/_BIM_COORD/Families/Symbols/ISO6412/STING_FAM_ISO6412_<ID>.rfa
   ```
6. The result dialog shows Created / Existed / Failed / Warnings counts.

### 3.2 Re-running (Overwrite Mode)

The generator checks for an existing `.rfa` before creating. If the file
exists it is **skipped** (Existed++). To force regeneration of a symbol:

- Delete the target `.rfa` in Explorer, then re-run.
- Or run **TAGS → Symbols → Reload Symbol Library** — this does not regenerate
  but re-loads existing families into the document.

### 3.3 Checking the Log

`StingTools.log` (next to the plugin DLL) records every symbol creation
attempt. Search for `ISO6412` to filter. Warnings appear as:

```
[WARN] ISO6412_ELBOW_90_BW: geometry coordinate 1.5 out of range — check JSON
[WARN] ISO6412_DCT_FLEX: no plan view in family template; geometry skipped
```

Errors block file save — check the Errors list in the result dialog first.

---

## 4. Geometry Conventions

### 4.1 Coordinate System

```
+Y (branch up / duct up)
 |
 |_____ +X (flow direction, left to right)
(0,0)
```

- **Pipe centreline** runs along **Y = 0**, entering from the left (`x = -0.5`),
  exiting right (`x = +0.5`).
- **Branches** go up (+Y) for tees, cross branches.
- **Elbows** turn from horizontal (incoming) to vertical (exiting upward).
- **Valve bodies** are centred at origin.
- All coordinates are **normalised** (-0.5 to +0.5). The `symbolSize` mm value
  maps `±0.5` to the actual plotted half-width.

### 4.2 Line Proportions

Good symbols use these proportions within the normalised space:

| Element | Typical normalised value | Plotted at symbolSize=6mm |
|---|---|---|
| Pipe stub on each side of fitting | `0.25` → `0.5` (half-width) | 1.5 mm to edge |
| Valve body half-width (bowtie) | `0.25` | 1.5 mm |
| Gate valve triangle height | `0.22` | 1.32 mm |
| Globe valve circle radius | `0.18` | 1.08 mm |
| Flange face tick mark | `0.25` tall | 1.5 mm |
| Elbow corner arc radius | `0.12`–`0.15` | 0.72–0.90 mm |
| Weld arrow leg | `0.2` | 0.8 mm (at symbolSize=4mm) |

### 4.3 Filled Regions

`filledRegions` use polygon vertex lists (wound counter-clockwise when viewed
from +Z). The renderer calls `FilledRegion.Create()` with a solid fill.

Rule: filled regions must be **convex or simple non-self-intersecting polygons**.
The generator does not validate topology — a self-intersecting region will cause
a Revit API exception at creation time (logged as Error, family is still saved
without that region).

The standard gate-valve bowtie uses **two separate triangles**, not one
bowtie shape, to avoid the self-intersection at the centre:
```json
"filledRegions": [
  {"vertices": [{"x": -0.25,"y": 0.22},{"x": 0.0,"y": 0.0},{"x": -0.25,"y": -0.22}]},
  {"vertices": [{"x":  0.25,"y": 0.22},{"x": 0.0,"y": 0.0},{"x":  0.25,"y": -0.22}]}
]
```

### 4.4 Arcs

Arc parameters map to `Arc.Create(centre, radius, startAngle, endAngle)`.
Angles are in **degrees** in the JSON; the creator converts to radians.

- `startDeg: 0, endDeg: 360` = full circle (use for globe valve, ball valve, pump).
- `startDeg: 90, endDeg: 180` = quarter circle, top-left quadrant (90° elbow).
- `startDeg: 270, endDeg: 90` = right-side semicircle (return bend cap).

Arc centre `(cx, cy)` is in normalised units, scaled by `symbolSize` exactly
like line endpoints.

---

## 5. Drafting Quality Checklist

Work through this checklist **after** the first generation run and **before**
issuing spool drawings. Open each `.rfa` directly in Revit to inspect.

### 5.1 Visual Inspection — Every Family

Open the family in Revit (double-click `.rfa` or File → Open → Family).

- [ ] **Geometry is on the correct view**: Symbolic lines appear in the
  `{3D}` or `Ref. Level` plan view. If the family is blank, check that
  `IsAnnotationFamily()` returned true — this means `familyType` must be
  `"GenericAnnotation"` in the JSON.

- [ ] **No stray geometry**: no lines outside the symbol boundary; no dots
  left from degenerate zero-length lines. If you see one, find the JSON
  vertex that has `x1 == x2` and `y1 == y2` and remove or fix it.

- [ ] **Filled regions are solid and black**: open a schedule or place on a
  white sheet. Gate valve triangles, plug valve body, blind flange pad should
  be solid black. If hollow or hatched, the fill pattern applied is not the
  solid fill — regenerate after confirming `DrawFilledRegion` found a solid
  fill pattern in the document.

- [ ] **No reference planes visible on plotting**: Reference planes are
  non-printing by default in Revit. If you see blue dashed lines when
  printing, change the print setting to exclude reference planes.

- [ ] **Symbol sits at origin**: Select all geometry in the family editor and
  check that the bounding box is centred on `(0,0)`. Centred families place
  correctly when `MepSymbolEngine` uses the element's centreline point.

- [ ] **The `Symbol Scale` parameter exists**: In the family editor, go to
  Family Types. You should see `Symbol Scale` (Type INTEGER). If missing,
  the shared parameter binding failed — load `STING_ISO_SYMBOL_TEMPLATE.params.txt`
  manually and re-create the parameter.

### 5.2 Pipe Fitting Families — Specific Checks

Open each pipe fitting family and verify:

- [ ] **Centreline is continuous**: place the family on a spool view between
  two pipe stubs and confirm the pipe line enters and exits cleanly through
  the symbol endpoints (`x = ±0.5` on the centreline `y = 0`).

- [ ] **90° elbow corner is smooth**: the arc should be tangent to both stubs.
  Check that the arc centre is at `(0, 0)` and the radius matches the stub
  start distance from origin (`0.12`–`0.15`). If the corner looks kinked,
  adjust the arc `cx/cy` to the intersection of the two stub directions.

- [ ] **Reducer is symmetric (concentric) or flat-bottom (eccentric)**: for
  `RED_CONC`, the top and bottom taper lines should slope equally. For
  `RED_ECC`, the bottom line must be horizontal (same Y on both ends).

- [ ] **Tee branch perpendicular**: for `TEE_EQ`, the branch line at `x=0`
  must be exactly vertical (`x1 == x2 == 0.0`). Floating-point drift can
  cause a slight lean — check the JSON values are exact integers or simple
  fractions.

### 5.3 Valve Families — Specific Checks

- [ ] **Gate valve bowtie**: both triangles must share their apex at exactly
  `(0, 0)`. The two stubs on each side (`x = ±0.25` to `x = ±0.5`) must
  align with the triangle base corners at `(±0.25, ±0.22)`. Test by placing
  on a horizontal pipe — the bowtie should span the full pipe width.

- [ ] **Globe valve circle**: the circle centre is at origin, radius `0.18`.
  The pipe stubs must start at `x = ±0.18` exactly to avoid a visible gap
  or overlap where the stub meets the circle.

- [ ] **Ball valve crosshair**: the two diameter lines (`-0.18` to `+0.18`
  on both axes) should be the same weight as the body circle. Verify they
  are drawn as symbolic lines, not as heavy construction lines.

- [ ] **Control valve actuator**: the horizontal bar at the top (`y = 0.25`)
  represents the actuator connection point. It should be centred on the
  valve stem (`x = 0`). For motorised valves the actuator circle should sit
  above the bar, not overlap it.

- [ ] **Check valve flap direction**: the inclined flap line (`-0.08, 0.22`
  to `0.08, 0.0`) shows flow is left-to-right. If placed on a reverse-flow
  segment, mirror the family instance — do not rotate, mirroring preserves
  the annotation plane.

- [ ] **Fail-open / fail-closed triangles**: the upward/downward arrows
  (small filled triangles above the actuator bar) must be clearly
  distinguishable. Verify the triangle apex points up for FO and down for FC.

### 5.4 Flange Families — Specific Checks

- [ ] **Flange face is perpendicular**: the face tick mark must be at `x = 0`,
  spanning `y = -0.25` to `y = +0.25`. A tilted face mark indicates the
  geometry was authored at an angle — correct in JSON.

- [ ] **Weld-neck hub**: the short stub between face and pipe (`x = -0.08` to
  `x = 0`) represents the weld-neck hub. The gap between the hub line and
  the face should be visible but small — typically 0.08 normalised = 0.48 mm.

- [ ] **Blind flange filled pad**: the pad (`x = 0` to `x = 0.08`) must be
  solid black. Open the family and check the filled region is within the
  face and pad boundary, not protruding.

- [ ] **Paired flanges on a joint**: place two flange families back-to-back
  on a spool view. Their face marks should sit flush. If they overlap or
  have a gap, the stub length (`x = 0.5` to face) needs adjusting.

### 5.5 Weld Mark Families — Specific Checks

Weld marks are the smallest symbols (symbolSize = 4.0 mm). They sit on the
pipe centreline, not across it.

- [ ] **Reference line ends at the weld**: the horizontal reference line
  (`y = -0.2`, from `x = -0.5` to `x = 0`) must terminate exactly at the
  vertical weld arrow line (`x = 0`). No overshoot.

- [ ] **Field weld flag triangle**: the solid triangle (`filledRegion`) at
  the top of the arrow line indicates field weld (vs. shop). Verify it is
  solid, proportionate (~3 mm tall at plot scale), and does not clip.

- [ ] **Weld symbol icons** (X-ray, PWHT, hardness): these are small graphic
  marks above the reference line. Verify they are legible at 1:1 — print a
  test sheet and check at 100% zoom. If illegible, increase symbol content
  size by 0.05–0.1 normalised units and regenerate.

- [ ] **Orbital weld circle**: the circle at `(−0.17, 0.12)` radius `0.1`
  must not overlap the reference line. At symbolSize=4mm, the circle is
  0.4 mm radius — confirm it clears the `y = 0` baseline.

### 5.6 Hanger & Support Families — Specific Checks

- [ ] **Rod hangs below pipe**: hanger geometry goes **downward** (negative Y)
  from the pipe centreline. The pipe line is at `y = 0`. The rod, clevis,
  or spring geometry should extend to `y = -0.4` or `y = -0.5`.

- [ ] **Spring coil legibility**: the spring symbol uses zigzag lines. At
  symbolSize=5mm the coil pitch is 0.06 normalised = 0.3 mm per half-wave.
  Print a test and confirm the zigzag is visible. If too fine, increase
  the pitch to `0.08` and reduce the number of waves.

- [ ] **Trapeze hanger span**: the horizontal bottom bar should extend beyond
  the pipe symbol width. At symbolSize=5mm, `x = ±0.3` = ±1.5 mm. For a
  multi-pipe trapeze, increase to `±0.4`.

- [ ] **Anchor is visually distinct from guide**: the anchor (`ANCHOR`) uses
  a solid filled rectangle; the guide (`GUIDE`) uses an open rectangle.
  Confirm the anchor fill is solid black and the guide is outline only.

- [ ] **Shoe height variants**: `SHOE_LOW` (single bottom bar) vs `SHOE_HIGH`
  (two bars with insulation gap between). Verify the midline horizontal bar
  is present on `SHOE_HIGH` and absent on `SHOE_LOW`.

### 5.7 Duct Fitting Families — Specific Checks

Duct symbols use `symbolSize = 8.0 mm`, so the wall lines at `y = ±0.15`
plot at ±1.2 mm from centreline — representing a 2.4 mm wide duct stub.

- [ ] **Double-line convention**: duct symbols use two parallel lines for each
  duct wall (unlike pipe single-line). Verify both top (`y = +0.15`) and
  bottom (`y = -0.15`) wall lines are present on every straight run.

- [ ] **Box closure on ends**: each duct symbol must close the box at open
  ends with a vertical end cap line (`x = ±0.5`, `y = -0.15` to `y = +0.15`).
  If end caps are missing, the symbol looks like an open channel.

- [ ] **90° elbow inner/outer corners**: the inner corner lines (`x = -0.15`,
  `y = -0.15` to `y = -0.5`) must be shorter than the outer corner lines
  to reflect the shorter inner bend length. Check that both inner lines exist
  and are shorter.

- [ ] **Damper blade diagonal**: volume control dampers show one diagonal
  line (`-0.4, 0.15` to `0.4, -0.15`). Fire dampers show a filled vertical
  bar. Motorised dampers show the diagonal plus an actuator circle. Ensure
  these are not confused in the generated families.

- [ ] **Flexible duct connection**: the zigzag between `x = -0.3` and `x = 0.3`
  represents flex duct. Verify all five zigzag lines are present and the
  end box closures at `x = ±0.3` are correct.

### 5.8 Notation Families — Specific Checks

- [ ] **Flow arrow points right**: the filled triangle apex must be at
  `x = 0.5` (right side). If placed on a return line, the instance is
  mirrored in the view, not flipped in the family.

- [ ] **Line tag box proportions**: the rectangular border of `TAG_PIPE`
  (`x = ±0.25`, `y = ±0.20`) should have aspect ratio roughly 2.5:1 wide.
  Adjust to match the project's pipe tag standard if needed.

- [ ] **Continuation arrows direction**: `CONT_OFF` (leaving this sheet) has
  the arrow pointing right; `CONT_ON` (arriving from another sheet) points
  left. Verify both are in the library and that the arrows are solid filled.

- [ ] **Battery limit tripling**: the three vertical lines of `BATTERY_LIMIT`
  at `x = 0, ±0.15` represent the boundary. All three must be present and
  the horizontal pipe line must pass through all three.

---

## 6. Placement Rules (MepSymbolEngine)

### 6.1 How Symbols Are Found

`MepSymbolEngine.ResolveFamilySymbol()` searches in this order:

1. `Families/ISO6412/` (static bundle alongside plugin)
2. `Families/MEP/` (manually authored families)
3. `Families/SLD/` (SLD families, normally skipped for spool views)
4. `<project>/_BIM_COORD/Families/Symbols/` — **recursive**, all sub-folders

Generated families land in tier 4. If a manually-authored `.rfa` exists in
tier 1, it takes priority. Use tier 1 for custom overrides; let the generator
fill tier 4 for standard content.

### 6.2 View Scale Behaviour

`MepSymbolEngine` checks `view.Scale`:

```csharp
bool isSpoolView = view.Scale == 1;
int scaleFactor = isSpoolView ? 1 : view.Scale;
```

For spool views (`Scale = 1`), families are placed at 1:1 (geometry in mm =
geometry plotted). For plan views (e.g. `Scale = 50`), the engine writes
`Symbol Scale = 50` on placed instances so families with scale-tier
parameters can switch label visibility.

ISO 6412 spool symbols should **only be placed on spool drafting views**
(`view.Scale = 1`). If placed on a plan view by mistake the symbol will be
50–100× too small and invisible.

### 6.3 Placing Symbols Manually

If `MepSymbolEngine` cannot auto-place (e.g. bespoke spool geometry), place
manually:

1. Go to **TAGS tab → Symbols → Place MEP Detail Symbols**.
2. Choose "ISO6412" category from the dropdown.
3. Click the target point on the spool drafting view (must have `Scale = 1`).
4. The command places the selected `.rfa` as a Detail Item.

---

## 7. Updating Geometry (JSON Editing Workflow)

### 7.1 When to Edit the JSON

- Symbol is geometrically wrong after inspection (wrong proportions, missing line).
- Standard requires a symbol not yet in the library (add new entry).
- Company drafting standard differs from ISO 6412 (e.g. BS vs IEC valve shapes).

### 7.2 Edit Cycle

```
1. Open STING_ISO6412_SYMBOLS.json in VS Code (or any JSON editor)
2. Locate the symbol by its "id" field
3. Edit the geometry arrays (lines / arcs / filledRegions)
4. Validate JSON: python3 -c "import json; json.load(open('STING_ISO6412_SYMBOLS.json'))"
5. In Revit: delete the old .rfa from _BIM_COORD/Families/Symbols/ISO6412/
6. Run Create All Symbols (or just reload the single batch via command)
7. Inspect the regenerated family in the family editor
8. Place on a test spool view and print at 1:1 to verify appearance
9. Commit the JSON change to Git with a clear description of what changed
```

### 7.3 Adding a New Symbol

Add a new entry to the `symbols` array in the JSON:

```json
{
  "id": "ISO6412_<CATEGORY>_<CODE>",
  "name": "Human-readable name",
  "category": "<match existing category string exactly>",
  "familyType": "GenericAnnotation",
  "discipline": "Generic",
  "subcategory": "ISO6412",
  "symbolSize": 6.0,
  "parameters": [{"name": "Symbol Scale", "type": "Integer", "shared": true}],
  "geometry": {
    "lines": [],
    "arcs": [],
    "filledRegions": []
  }
}
```

Rules:
- `id` must be unique across the entire file.
- `category` must match one of the 15 existing category strings (case-sensitive)
  or the CSV index will not group it correctly.
- `symbolSize` should follow the category defaults in §2.2.
- All coordinates in `-0.5` to `+0.5` normalised space.

### 7.4 Retiring / Replacing a Symbol

Do not delete entries from the JSON — downstream CSV references (`STING_ISO_SYMBOLS_INDEX.csv`)
key on `symbol_code` which maps to the JSON `id`. Instead:

1. Add a `"deprecated": true` field to the entry.
2. Create the replacement entry with a new `id`.
3. Update `STING_ISO_SYMBOLS_INDEX.csv` to point `family_filename` to the new family.
4. The old `.rfa` remains loadable for existing spool sheets that reference it.

---

## 8. Printing & Plot Quality

### 8.1 Line Weights

All symbolic lines in GenericAnnotation families use the family's default
line weight (Revit weight 1 = 0.18 mm at print). For spool drawings the
recommended line weight settings are:

| Element | Revit line weight | Plotted thickness |
|---|---|---|
| Pipe run centreline | 3 | 0.35 mm |
| Symbol outline (valves, fittings) | 2 | 0.25 mm |
| Symbol fill lines (hatching, coils) | 1 | 0.18 mm |
| Weld reference line | 1 | 0.18 mm |
| Dimension / annotation text | 1 | 0.18 mm |

To apply custom line weights to generated families, open the `.rfa`,
select the geometry, and set the Subcategory line weight in the Element
Properties dialog. This change survives regeneration **only if you manually
apply it** — the generator does not set line weights beyond the family default.

**Planned enhancement**: add `"lineWeight"` field to the JSON geometry arrays
so `SymbolLibraryCreator` sets line weight per symbol on creation.

### 8.2 Print Scale

All spool views should print at 1:1. In Revit's print dialog:
- **View/Sheet set**: select spool sheets.
- **Print range**: current view/sheet or all applicable.
- **Zoom**: 100% (do not fit-to-page — this breaks the 1:1 assumption).
- **Raster/vector**: vector for crisp line weights.

### 8.3 Minimum Readable Size

At 1:1 on A1 paper, the minimum readable line weight is ~0.13 mm. The
smallest symbol in this library is the weld mark at `symbolSize = 4.0 mm`.
At this size:
- The reference line is 2.0 mm long on each side.
- The weld symbol icons (X-ray, PWHT) are ~0.6–0.8 mm tall.

If these are too small for your project's spool scale, increase `symbolSize`
for the Welds category from `4.0` to `5.0` mm in the JSON.

---

## 9. Governance & Version Control

### 9.1 Committing JSON Changes

Every edit to `STING_ISO6412_SYMBOLS.json` must be committed to Git with a
message that includes:
- Which symbol(s) changed.
- What was wrong or missing.
- The corrected normalised coordinate values.

Example:
```
Fix ISO6412_VLV_GATE bowtie apex alignment

Left triangle apex was at (0, 0.01) due to copy-paste rounding.
Corrected to exact (0, 0) so both triangles share the apex cleanly.
Also increased gate valve stub length from 0.2 to 0.25 to match
the ISO 6412:1995 Annex A figure proportions.
```

### 9.2 Generated `.rfa` Files

Generated `.rfa` files live under `_BIM_COORD/` which is project-specific.
They are **not committed to Git** (gitignored). The JSON is the single source
of truth; `.rfa` files are build artefacts.

To share a finished, reviewed symbol set across projects:
1. Copy the reviewed `.rfa` files from `_BIM_COORD/Families/Symbols/ISO6412/`
   to `Families/ISO6412/` in the repository.
2. Commit these `.rfa` files to Git.
3. `MepSymbolEngine` tier 1 will find them before the generator output.
4. Document in `Families/ISO6412/README.md` which families are static (Tier 1)
   and which are still generator-only (Tier 4).

### 9.3 Finalization Gate

Before spool issue, run **Fabrication → Pre-flight Check** (or the equivalent
`IsoSymbolPlacer.GetMissingFamilyReport()` call). This reports:
- Symbols referenced in the spool CSV but not found in any search tier.
- Symbols placed in views that have been deleted from disk.
- `STING_FINALIZATION_CHECKLIST` parameter value on each placed instance.

All missing families must be resolved (regenerate or manually author) before
the spool package is issued.

---

## 10. Quick Reference — Symbol IDs

| Code | Description | Category | symbolSize |
|---|---|---|---|
| `ISO6412_ELBOW_90_BW` | 90° LR Butt-Weld Elbow | Pipe Fittings | 6 |
| `ISO6412_ELBOW_45_BW` | 45° Butt-Weld Elbow | Pipe Fittings | 6 |
| `ISO6412_ELBOW_90_SR` | 90° Short-Radius Elbow | Pipe Fittings | 6 |
| `ISO6412_ELBOW_180_BW` | 180° Return Bend | Pipe Fittings | 6 |
| `ISO6412_TEE_EQ` | Equal Tee | Pipe Fittings | 6 |
| `ISO6412_TEE_RED` | Reducing Tee | Pipe Fittings | 6 |
| `ISO6412_CROSS` | Equal Cross | Pipe Fittings | 6 |
| `ISO6412_RED_CONC` | Concentric Reducer | Pipe Fittings | 6 |
| `ISO6412_RED_ECC` | Eccentric Reducer | Pipe Fittings | 6 |
| `ISO6412_COUPLING` | Coupling | Pipe Fittings | 6 |
| `ISO6412_UNION` | Union | Pipe Fittings | 6 |
| `ISO6412_CAP` | End Cap | Pipe Fittings | 6 |
| `ISO6412_PLUG` | Plug | Pipe Fittings | 6 |
| `ISO6412_SWAGE` | Swaged Nipple | Pipe Fittings | 6 |
| `ISO6412_NIPPLE` | Nipple | Pipe Fittings | 6 |
| `ISO6412_FLG_WN` | Weld-Neck Flange | Flanges | 6 |
| `ISO6412_FLG_SO` | Slip-On Flange | Flanges | 6 |
| `ISO6412_FLG_BL` | Blind Flange | Flanges | 6 |
| `ISO6412_FLG_LJ` | Lap-Joint Flange | Flanges | 6 |
| `ISO6412_FLG_THD` | Threaded Flange | Flanges | 6 |
| `ISO6412_FLG_SW` | Socket-Weld Flange | Flanges | 6 |
| `ISO6412_FLG_RTJ` | Ring-Type Joint Flange | Flanges | 6 |
| `ISO6412_GASKET_RF` | Raised-Face Gasket | Flanges | 6 |
| `ISO6412_GASKET_FF` | Full-Face Gasket | Flanges | 6 |
| `ISO6412_SPECTACLE_BLIND` | Spectacle Blind (closed) | Flanges | 6 |
| `ISO6412_SPACER_RING` | Spacer Ring | Flanges | 6 |
| `ISO6412_VLV_GATE` | Gate Valve | Valves | 6 |
| `ISO6412_VLV_GLOBE` | Globe Valve | Valves | 6 |
| `ISO6412_VLV_BALL` | Ball Valve | Valves | 6 |
| `ISO6412_VLV_BUTTERFLY` | Butterfly Valve | Valves | 6 |
| `ISO6412_VLV_CHECK` | Check Valve (swing) | Valves | 6 |
| `ISO6412_VLV_PLUG` | Plug Valve | Valves | 6 |
| `ISO6412_VLV_NEEDLE` | Needle Valve | Valves | 6 |
| `ISO6412_VLV_DIAPHRAGM` | Diaphragm Valve | Valves | 6 |
| `ISO6412_VLV_ANGLE` | Angle Valve | Valves | 6 |
| `ISO6412_VLV_3WAY` | 3-Way Valve | Valves | 6 |
| `ISO6412_VLV_4WAY` | 4-Way Valve | Valves | 6 |
| `ISO6412_VLV_RELIEF` | Pressure Relief Valve | Valves | 6 |
| `ISO6412_VLV_SAFETY` | Safety Valve | Valves | 6 |
| `ISO6412_VLV_CTRL` | Control Valve (generic) | Valves | 6 |
| `ISO6412_VLV_CTRL_FC` | Control Valve Fail-Closed | Valves | 6 |
| `ISO6412_VLV_CTRL_FO` | Control Valve Fail-Open | Valves | 6 |
| `ISO6412_VLV_SOLENOID` | Solenoid Valve | Valves | 6 |
| `ISO6412_VLV_MOTOR` | Motor-Operated Valve | Valves | 6 |
| `ISO6412_VLV_HANDWHL` | Handwheel Valve | Valves | 6 |
| `ISO6412_VLV_GEAR` | Gear-Operated Valve | Valves | 6 |
| `ISO6412_VLV_PNEU` | Pneumatic Valve | Valves | 6 |
| `ISO6412_VLV_HOSE` | Hose Connection Valve | Valves | 6 |
| `ISO6412_VLV_LOCKSHIELD` | Lockshield Valve | Valves | 6 |
| `ISO6412_VLV_BALANCING` | Balancing Valve | Valves | 6 |
| `ISO6412_VLV_PREG` | Pressure Regulating Valve | Valves | 6 |
| `ISO6412_VLV_FOOT` | Foot Valve | Valves | 6 |
| `ISO6412_VLV_FLOAT` | Float Valve | Valves | 6 |
| `ISO6412_VLV_PISTON` | Piston Valve | Valves | 6 |
| `ISO6412_STRAINER_Y` | Y-Type Strainer | Strainers | 6 |
| `ISO6412_STRAINER_T` | T-Type Strainer | Strainers | 6 |
| `ISO6412_STRAINER_BASKET` | Basket Strainer | Strainers | 6 |
| `ISO6412_STRAINER_DUPLEX` | Duplex Strainer | Strainers | 6 |
| `ISO6412_TRAP_FLOAT` | Float Steam Trap | Steam Traps | 6 |
| `ISO6412_TRAP_BUCKET` | Inverted Bucket Trap | Steam Traps | 6 |
| `ISO6412_TRAP_THERM` | Thermostatic Trap | Steam Traps | 6 |
| `ISO6412_TRAP_BALANCED` | Balanced-Pressure Trap | Steam Traps | 6 |
| `ISO6412_SEP_AIR` | Air Separator | Equipment | 6 |
| `ISO6412_SEP_DIRT` | Dirt Separator | Equipment | 6 |
| `ISO6412_EXP_TANK` | Expansion Tank | Equipment | 6 |
| `ISO6412_ACCUMULATOR` | Accumulator / Receiver | Equipment | 6 |
| `ISO6412_PUMP_CENTR` | Centrifugal Pump | Pumps | 6 |
| `ISO6412_PUMP_INLINE` | In-Line Pump | Pumps | 6 |
| `ISO6412_PUMP_SUMP` | Sump / Submersible Pump | Pumps | 6 |
| `ISO6412_DCT_ELBOW_90_R` | Rect Duct 90° Elbow | Duct Fittings | 8 |
| `ISO6412_DCT_ELBOW_45_R` | Rect Duct 45° Elbow | Duct Fittings | 8 |
| `ISO6412_DCT_ELBOW_90_RND` | Round Duct 90° Elbow | Duct Fittings | 8 |
| `ISO6412_DCT_TEE_EQ_R` | Rect Duct Equal Tee | Duct Fittings | 8 |
| `ISO6412_DCT_TEE_RED_R` | Rect Duct Reducing Tee | Duct Fittings | 8 |
| `ISO6412_DCT_CROSS_R` | Rect Duct Cross | Duct Fittings | 8 |
| `ISO6412_DCT_RED_CONC_R` | Rect Duct Concentric Reducer | Duct Fittings | 8 |
| `ISO6412_DCT_RED_ECC_R` | Rect Duct Eccentric Reducer | Duct Fittings | 8 |
| `ISO6412_DCT_TRANSR` | Rect-to-Round Transition | Duct Fittings | 8 |
| `ISO6412_DCT_CAP_R` | Rect Duct End Cap | Duct Fittings | 8 |
| `ISO6412_DCT_FLEX` | Flexible Duct Connection | Duct Fittings | 8 |
| `ISO6412_DCT_DAMPER` | Volume Control Damper | Duct Fittings | 8 |
| `ISO6412_DCT_FD` | Fire Damper | Duct Fittings | 8 |
| `ISO6412_DCT_SDDAMPER` | Smoke/Fire Damper | Duct Fittings | 8 |
| `ISO6412_DCT_ATTENUATOR` | Duct Attenuator / Silencer | Duct Fittings | 8 |
| `ISO6412_DCT_ACCESS` | Duct Access Door | Duct Fittings | 8 |
| `ISO6412_DCT_GRILLE` | Supply / Extract Grille | Duct Fittings | 8 |
| `ISO6412_DCT_DIFFUSER` | Ceiling Diffuser | Duct Fittings | 8 |
| `ISO6412_DCT_PLENUM` | Plenum Box | Duct Fittings | 8 |
| `ISO6412_DCT_LOUVRE` | Louvre / Weather Louvre | Duct Fittings | 8 |
| `ISO6412_DCT_ISOL_JT` | Duct Isolation Joint | Duct Fittings | 8 |
| `ISO6412_DCT_MOTORIZED_DAMPER` | Motorised Damper | Duct Fittings | 8 |
| `ISO6412_DCT_BACKD_DAMPER` | Backdraught Damper | Duct Fittings | 8 |
| `ISO6412_CDT_ELBOW_90` | Conduit 90° Elbow | Conduit Fittings | 5 |
| `ISO6412_CDT_ELBOW_45` | Conduit 45° Elbow | Conduit Fittings | 5 |
| `ISO6412_CDT_TEE` | Conduit Tee | Conduit Fittings | 5 |
| `ISO6412_CDT_COUPL` | Conduit Coupling | Conduit Fittings | 5 |
| `ISO6412_CDT_CONN` | Conduit Connector | Conduit Fittings | 5 |
| `ISO6412_CDT_CAP` | Conduit End Cap | Conduit Fittings | 5 |
| `ISO6412_CDT_PULL_BOX` | Conduit Pull Box | Conduit Fittings | 5 |
| `ISO6412_CDT_EXPAN` | Conduit Expansion Fitting | Conduit Fittings | 5 |
| `ISO6412_CDT_SEALPIPE` | Conduit Sealing Fitting | Conduit Fittings | 5 |
| `ISO6412_CDT_LOCKNUT` | Conduit Locknut | Conduit Fittings | 5 |
| `ISO6412_CDT_ELBOW_LB` | Conduit LB Elbow | Conduit Fittings | 5 |
| `ISO6412_CDT_REDUCER` | Conduit Reducer / Bushing | Conduit Fittings | 5 |
| `ISO6412_CDT_ELBOW_90_STR` | Conduit Street Elbow 90° | Conduit Fittings | 5 |
| `ISO6412_CTR_BEND_90` | Cable Tray 90° Bend | Cable Tray | 5 |
| `ISO6412_CTR_TEE` | Cable Tray Tee | Cable Tray | 5 |
| `ISO6412_CTR_CROSS` | Cable Tray Cross | Cable Tray | 5 |
| `ISO6412_CTR_REDUCER` | Cable Tray Reducer | Cable Tray | 5 |
| `ISO6412_CTR_ELBOW_V` | Cable Tray Vertical Bend | Cable Tray | 5 |
| `ISO6412_CTR_END_STOP` | Cable Tray End Stop | Cable Tray | 5 |
| `ISO6412_CTR_SPLICE` | Cable Tray Splice Plate | Cable Tray | 5 |
| `ISO6412_CTR_DIVIDER` | Cable Tray Divider Strip | Cable Tray | 5 |
| `ISO6412_CTR_COVER` | Cable Tray Cover | Cable Tray | 5 |
| `ISO6412_SLEEVE_FIRESTOP` | Fire-Stop Pipe Sleeve | Penetrations | 5 |
| `ISO6412_SLEEVE_PLAIN` | Pipe Sleeve (plain) | Penetrations | 5 |
| `ISO6412_WALL_PEN` | Wall Penetration Seal | Penetrations | 5 |
| `ISO6412_FLOOR_PEN` | Floor Penetration Seal | Penetrations | 5 |
| `ISO6412_SLAB_PEN` | Slab Core Hole | Penetrations | 5 |
| `ISO6412_WELD_SHOP_FW` | Shop Fillet Weld | Welds | 4 |
| `ISO6412_WELD_FIELD_FW` | Field Fillet Weld | Welds | 4 |
| `ISO6412_WELD_SHOP_BW` | Shop Butt Weld | Welds | 4 |
| `ISO6412_WELD_FIELD_BW` | Field Butt Weld | Welds | 4 |
| `ISO6412_WELD_SHOP_SW` | Shop Socket Weld | Welds | 4 |
| `ISO6412_WELD_FIELD_SW` | Field Socket Weld | Welds | 4 |
| `ISO6412_WELD_BRANCH_FW` | Branch Fillet Weld | Welds | 4 |
| `ISO6412_WELD_ORBITAL` | Orbital / Automatic Weld | Welds | 4 |
| `ISO6412_WELD_FLANGED` | Flanged Weld Joint | Welds | 4 |
| `ISO6412_WELD_THRD` | Threaded Joint | Welds | 4 |
| `ISO6412_WELD_SHOP_TW` | Shop Tack Weld | Welds | 4 |
| `ISO6412_WELD_PRESSURE` | Pressure Test Point Weld | Welds | 4 |
| `ISO6412_WELD_XRAY` | Radiograph (X-ray) Weld | Welds | 4 |
| `ISO6412_WELD_UT` | Ultrasonic Test Weld | Welds | 4 |
| `ISO6412_WELD_PWHT` | Post-Weld Heat Treatment | Welds | 4 |
| `ISO6412_WELD_HARDNESS` | Hardness Test Point | Welds | 4 |
| `ISO6412_HANGER_CLEVIS` | Clevis Hanger | Hangers | 5 |
| `ISO6412_HANGER_RROD` | Riser Rod Hanger | Hangers | 5 |
| `ISO6412_HANGER_SPLIT_RING` | Split Ring Hanger | Hangers | 5 |
| `ISO6412_HANGER_TRAPEZE` | Trapeze Hanger | Hangers | 5 |
| `ISO6412_HANGER_UCLAMP` | U-Bolt Pipe Clamp | Hangers | 5 |
| `ISO6412_HANGER_SPRING` | Spring Hanger | Hangers | 5 |
| `ISO6412_HANGER_CONST_SPR` | Constant-Spring Hanger | Hangers | 5 |
| `ISO6412_GUIDE` | Pipe Guide | Hangers | 5 |
| `ISO6412_ANCHOR` | Pipe Anchor / Fixed Point | Hangers | 5 |
| `ISO6412_SLIDE` | Pipe Slide / Sliding Support | Hangers | 5 |
| `ISO6412_SHOE_LOW` | Low Shoe Support | Hangers | 5 |
| `ISO6412_SHOE_HIGH` | High Shoe Support | Hangers | 5 |
| `ISO6412_INSUL_PIPE` | Pipe Insulation | Insulation | 5 |
| `ISO6412_INSUL_DUCT` | Duct Insulation | Insulation | 8 |
| `ISO6412_INSUL_VAPOR` | Vapour Barrier Insulation | Insulation | 5 |
| `ISO6412_TAG_PIPE` | Pipe Tag / Line Number | Notation | 5 |
| `ISO6412_TAG_EQUIP` | Equipment Tag | Notation | 5 |
| `ISO6412_TAG_VALVE` | Valve Tag | Notation | 5 |
| `ISO6412_TAG_INSTR` | Instrument Tag | Notation | 5 |
| `ISO6412_FLOW_ARROW` | Flow Direction Arrow | Notation | 5 |
| `ISO6412_SPEC_BREAK` | Specification Break | Notation | 5 |
| `ISO6412_BATTERY_LIMIT` | Battery Limit | Notation | 5 |
| `ISO6412_CONT_OFF` | Continuation Off-Sheet | Notation | 5 |
| `ISO6412_CONT_ON` | Continuation On-Sheet | Notation | 5 |
| `ISO6412_RELIEF_VENT` | Relief Vent Outlet | Notation | 5 |
| `ISO6412_DRAIN_OPEN` | Open Drain Point | Notation | 5 |
| `ISO6412_VENT_OPEN` | Open Vent Point | Notation | 5 |
| `ISO6412_TEST_POINT` | Test / Sample Point | Notation | 5 |
| `ISO6412_CALLOUT_HEAD` | Callout / Reference Symbol | Notation | 5 |

---

*This guide covers 164 symbols across 15 categories. For the SLD symbol workflow
see `SLD_SYMBOLS_LAYMANS_GUIDE.md`. For MEP plan view symbols see
`MEP_SYMBOL_COLOUR_SCALE_GUIDE.md`.*

---

## §10 — Authoring Seed Families from Drafts

### 10.1 Why bother?

The JSON generator gives you working symbols in about 10 seconds but they are geometric
**approximations** — the proportions were estimated, not measured from the standard plates.
For a spool drawing that will be used in a contract or fabrication package, a draftsperson
should replace each "draft" family with a standard-accurate seed `.rfa`.

Once a seed is in `Families/ISO6412/`, STING uses it automatically at the highest search
priority. You never touch the JSON again for that symbol.

---

### 10.2 Colour and Line Weight Control (plain English)

You do **not** need to edit individual families to change colour or line weight in a project.
Revit's Object Styles and Visibility/Graphics do this at the category level:

| What you want | Where to go | Time |
|---|---|---|
| Change ALL symbols thicker/thinner project-wide | Manage tab → Object Styles → Annotation Objects → `ISO6412` row → edit line weight | 10 s |
| Change colour of all symbols in one view | View tab → Visibility/Graphics → Annotation Categories → `ISO6412` row → override colour | 10 s |
| Change one specific symbol on screen | Right-click it → Override Graphics in View → By Element | 5 s |

Because all 164 families share the `ISO6412` subcategory, one Object Styles change hits
every symbol on every sheet simultaneously.

**Scale** is different. The symbols are drawn at a fixed paper size (6 mm, 8 mm etc.).
The only way to make them globally bigger or smaller today is to re-run "Create All Symbols"
after changing the `symbolSize` value in the JSON. Proper seed families should wire
`Symbol Scale` to the geometry so you can adjust size by changing one instance parameter.

---

### 10.3 The Per-Symbol Correction Workflow

**Step 1 — Pick a symbol to graduate**

Start with the most-used symbols: gate valve, globe valve, 90° elbow, butt-weld tee.
Find its id in the Quick Reference table (§11) and open the corresponding generated `.rfa`
from `_BIM_COORD/Families/Symbols/ISO6412/` in the Family Editor.

**Step 2 — Open the standard plate**

Open ISO 6412:1999 or BS 308 Part 3 alongside the family editor. Find the correct plate for
the symbol. Note the key proportions relative to the "nominal size" dimension (usually labelled
`d` or `DN`).

**Step 3 — Correct the geometry**

In the Family Editor:
- Delete the generated lines/arcs/filled-regions
- Redraw using the standard proportions, keeping the coordinate convention:
  - Pipe centreline on `Y = 0`
  - Inlet at left (`x = −half symbol size`)
  - Outlet at right (`x = +half symbol size`)
  - Branches go up (`+Y`)

Use reference planes to lock key geometry ratios so the family scales cleanly.

**Step 4 — Set subcategory and line weights**

- All lines → subcategory `ISO6412` (create it if absent)
- In Manage → Object Styles inside the family, set line weights:
  - Main outline: weight 3 (≈ 0.35 mm)
  - Pipe run: weight 2 (≈ 0.25 mm)
  - Weld symbols: weight 4 (≈ 0.50 mm)

**Step 5 — Wire Symbol Scale (optional but recommended)**

Add a reference parameter `SymbolScaleRatio` with the formula `Symbol Scale / 100`.
Multiply every reference-plane offset by `SymbolScaleRatio`. Default `Symbol Scale = 100`.
This lets anyone resize the symbol by changing one instance parameter.

**Step 6 — Bind shared parameters**

Load `Families/ISO6412/STING_ISO_SYMBOL_TEMPLATE.params.txt` via Insert → Load from File →
Shared Parameters. Bind all 7 parameters listed in the README to the family.

**Step 7 — Run the drafting quality checklist**

Work through §5 of this guide for the symbol's category. Every item must pass before the
family is submitted.

**Step 8 — Save to the seed folder**

Save As → `Families/ISO6412/<id>.rfa` (name must match JSON id exactly, e.g.
`ISO6412_GATE_VALVE_FS.rfa`).

**Step 9 — Update the JSON status**

In `StingTools/Data/Symbols/STING_ISO6412_SYMBOLS.json` find the symbol by id and change:
```json
"status": "draft"   →   "status": "reviewed"
```

Change to `"final"` only after the PR is reviewed and the `.rfa` is committed to `main`.

**Step 10 — Commit**

```bash
git add Families/ISO6412/ISO6412_GATE_VALVE_FS.rfa
git add StingTools/Data/Symbols/STING_ISO6412_SYMBOLS.json
git commit -m "Graduate ISO6412_GATE_VALVE_FS to reviewed seed family

Geometry verified against ISO 6412:1999 plate 3-04.
Line weights set by subcategory. Symbol Scale wired to geometry."
```

---

### 10.4 Batch Graduation Strategy

164 symbols is a lot. A pragmatic order:

**Batch A — High frequency (do first, biggest return)**
Gate valve, globe valve, ball valve, butterfly valve, check valve (swing),
90° elbow (BW), 45° elbow (BW), equal tee (BW), concentric reducer,
socket weld coupling, slip-on flange, blind flange, butt weld mark.
≈ 13 symbols, covers ≈ 80% of a typical isometric.

**Batch B — Valve variants**
Relief/safety valve, control valve (FC/FO), solenoid valve, motor valve.
≈ 4 symbols.

**Batch C — Fittings**
All remaining pipe fittings, flanges, strainers.
≈ 20 symbols.

**Batch D — Duct, conduit, cable tray**
Once pipe symbols are done, repeat the process for duct fittings and conduit.
≈ 45 symbols.

**Batch E — Notation, hangers, insulation, welds**
These change rarely; graduate last.
≈ 45 symbols remaining.

---

### 10.5 Verifying the Generator Still Covers the Gap

Run the following in any project to see which symbols still use draft geometry
(i.e. no seed `.rfa` in `Families/ISO6412/`):

1. Open STING Tools panel → TAGS tab → Tag Studio
2. Tag → "Create ISO 6412 Symbols" with Overwrite unchecked
3. Check `StingTools.log` for lines starting with `[DRAFT]`

Each `[DRAFT]` line is a symbol that still needs a seed family. The count will
drop to zero as Batch A–E are completed.

---
