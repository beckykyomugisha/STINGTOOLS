# SLD & Symbol Workflow — Layman's Guide + Gap Analysis

> **Who this is for:** BIM engineers who need to understand how seed-family
> symbols are configured, and developers who want to know exactly what is
> missing for a fully automated single-line diagram (SLD) from the Revit model.
>
> *Phase 179 — covers the full pipeline from JSON spec to printed A1 sheet.*

---

## Part 1 — What the Symbol Workflow Does (Plain English)

Think of the symbol workflow as a **four-stage production line**.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  STAGE 1         STAGE 2            STAGE 3           STAGE 4               │
│  JSON Spec  ───► Auto Generator ──► Family Editor ──► Project + SLD         │
│  (the recipe)    (the factory)      (the polish)      (the result)           │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Stage 1 — The recipe (JSON spec files)

Every seed family starts as a JSON file in `StingTools/Data/Seeds/`.
That file is the authoritative source of truth for:

- Which Revit template (`.rft`) to start from
- Every shared parameter the family carries (name + GUID)
- What the basic 2D outline and 3D box should look like
- Where connectors sit and what domain/system they carry
- Every **type variant** (e.g. `FD_FR60_RECT_FUSIBLE`, `FD_FR90_RECT_FUSIBLE`)
  and what parameter values differ between them

For SLD diagrams a second set of JSON files drives the electrical symbols:
`StingTools/Data/Symbols/STING_SLD_SYMBOLS.json` (IEC 60617 / BS 7671).

---

### Stage 2 — The factory (BuildSeedFamiliesCommand)

Click **TEMP → Build Seed Families** and the generator:

1. Opens the correct `.rft` Revit template in the background
2. Injects every shared parameter (by GUID so they survive family renames)
3. Draws a basic 2D outline and 3D bounding box from the JSON geometry block
4. Places MEP connectors at the declared positions
5. Creates every type variant with its parameter values pre-set
6. Saves the result as an `.rfa` to `<project>/_BIM_COORD/Families/Seeds/`

**What the generator still cannot do:**
- Draw accurate 2D plan symbols (fire damper cross-diagonal, concentric rings, etc.)
- Replace the bounding box with real 3D geometry
- Set the `Mark = PEN_CONTROL_NUMBER_TXT` formula
- Classify connectors beyond the declared domain

That is all Stage 3 work.

---

### Stage 3 — The polish (Family Editor, manual)

Open the generated `.rfa` in Revit's Family Editor and work through
the **per-seed symbol guidance** in `Families/Seeds/README.md`.

#### How to configure each type variant

1. Go to **Manage → Family Types** (keyboard `FT`).
2. The left dropdown lists every type. The auto-built types already exist —
   `FD_FR60_RECT_FUSIBLE`, `FD_FR90_RECT_FUSIBLE`, etc.
3. Select a type, then set its **type parameters** in the right panel:

   | Parameter | What to enter | Where it comes from |
   |---|---|---|
   | `PEN_FIRE_RATING_TXT` | `FR60` | README type-variants table |
   | `FD_BSEN15650_CLASS_TXT` | `EI60S` | BS EN 15650 class notation |
   | `FD_ACTUATION_TXT` | `FUSIBLE_LINK_72C` | README type-variants table |
   | `ASS_PRODCT_COD_TXT` | `FD` | STING PROD code |
   | `PEN_CERTIFICATION_TXT` | `BS 476-20 / EN 1366-2 (60 min)` | README type-variants table |

4. Repeat for every type. Use the README table as your reference — it lists
   every parameter value for every variant.
5. **Add the Mark formula once** (applies to all types): click the formula
   cell next to **Mark** and type `= PEN_CONTROL_NUMBER_TXT`.
6. **Save → Load into Project and Close**.

#### How symbol standards control the look in the SLD

The family itself is the geometry. The **symbol standard** (IEC 60617,
BS 7671, NFPA 70) controls:

- Which family to place for a given device concept
  (e.g. concept `SLD_MCB` → family `STING_SLD_MCB_IEC` vs `STING_SLD_MCB_BS7671`)
- Text height and label format
- Circuit-reference prefix/suffix format
- Rating display format (`{poles}P {rating}A` vs `{rating}/{curve}`)

The active standard is resolved by `SymbolStandardResolver` which reads
the view's `STING_VIEW_SYMBOL_STANDARD` parameter. Set it in the
Electrical Panel dock → **SLD Options → Symbol Standard**.

---

### Stage 4 — The result (Project placement + SLD)

Once the polished families are loaded the automation takes over:

```
ElectricalSystem model
        │
        ▼
SLDCircuitTraverser.BuildHierarchy()
  → reads every ElectricalSystem, builds a tree of SLDNodes
        │
        ▼
SLDLayoutEngine.CalculateLayout()
  → assigns XYZ positions, busbar segments, branch lines
        │
        ▼
SLDGenerator.PlaceSymbols()
  → places IEC 60617 annotation families in a new ViewDrafting
        │
        ▼
SLDAnnotationPlacer.PlaceAllAnnotations()
  → writes rating / circuit-ref TextNotes beside each symbol
        │
        ▼
SLDSyncUpdater (IUpdater)
  → fast-path label refresh when a circuit changes
  → full rebuild when panels are added/deleted
```

---

## Part 2 — SLD Gap Analysis (100% Automation Targets)

Reviewed against `SLDCircuitTraverser.cs`, `SLDLayoutEngine.cs`,
`SLDAnnotationPlacer.cs`, `SLDGenerator.cs`, `SLDSyncUpdater.cs`,
`SLDRiserDiagramCommand.cs`.

**Severity key:** 🔴 Blocks correct output · 🟡 Degrades quality · 🟢 Minor

---

### GAP-SLD-01 🔴 Single root — multi-MSB buildings not supported

**File:** `SLDCircuitTraverser.cs:65`
```csharp
var root = equipment.FirstOrDefault(e => !loadIds.Contains(e.Id.Value));
```
`FirstOrDefault` returns one panel. A building with two MSBs (normal UK
practice — essential / non-essential boards) silently drops the second
tree entirely.

**Fix:** Return `List<SLDNode>` from `BuildHierarchy`. `SLDGenerator`
creates one SLD view per root and labels them `STING - SLD - MSB-1`,
`STING - SLD - MSB-2`, or merges both trees onto a single view with a
tie-switch gap in the busbar layout.

---

### GAP-SLD-02 🔴 Protection devices are not nodes

**File:** `SLDCircuitTraverser.cs:106–129`

The traverser creates nodes for panels and for loads, but MCBs, MCCBs,
and ACBs are never traversed as their own `SLDNode`. The `IsProtection`
flag on `SLDNode` is declared but never set `true`. As a result:

- No breaker symbol appears between the busbar and the load
- The `SLD_MCB`, `SLD_MCCB`, `SLD_ACB` symbol families are never placed
- The SLD is just boxes connected by lines — not a real single-line diagram

**Fix:** After resolving `ElectricalSystem.Elements`, inspect each
element's `STING_SYMBOL_ID` or category. If it is a `GenericModel`
family with a breaker concept, insert an intermediate `SLDNode` with
`IsProtection = true` between the panel node and the load node.
Where no separate protection element exists (most Revit models),
synthesise a protection node from the circuit's `RatingA` parameter
and map it to the correct symbol concept based on current threshold
(≤63A MCB · ≤250A MCCB · >250A ACB).

---

### GAP-SLD-03 🔴 Rating field is never populated

**File:** `SLDCircuitTraverser.cs:144–163` (`ReadCircuitData`)

```csharp
try { node.Poles = circuit.PolesNumber; } catch ...
try { ... node.LoadKW = ...; } catch ...
// node.Rating is never set
```

`node.Rating` stays `null` for every node. `BuildCircuitLabel` then
formats `"{rating}{unit}"` as an empty string — no ampere values appear
on the diagram.

**Fix:** Read rating from priority order:
1. `ElectricalSystem` parameter `RBS_ELEC_CIRCUIT_RATING` (Revit built-in)
2. `BaseEquipment` family parameter `ELC_CDT_AMP_RATING` or `ELC_KVA_RATING`
3. `STING_BREAKER_RATING_A` shared parameter on the panel type

```csharp
try
{
    var rp = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING);
    if (rp != null && rp.HasValue)
        node.Rating = $"{(int)(rp.AsDouble() * 0.001):0}"; // VA → A
}
catch (Exception ex) { StingLog.Warn($"Rating: {ex.Message}"); }
```

---

### GAP-SLD-04 🟡 Symbol standard ignored in layout

**File:** `SLDLayoutEngine.cs:34`
```csharp
public static SLDLayout CalculateLayout(SLDNode root, string standardId)
```
`standardId` is received but never read inside the method. IEC 60617
and NFPA 70 have different standard symbol sizes (3–5 mm IEC vs 6–10 mm
NFPA), different busbar offsets, and different branch-line lengths.
Using fixed `SymbolHeightMm = 8.0` produces diagrams that are either
too cramped (NFPA) or too spread out (IEC).

**Fix:** Load spacing constants from `SymbolStandardRegistry.GetAnnotationRules(standardId).SymbolSizeMm` and use those for `SymbolHeightMm`, `SymbolSpacingMm`, and `BusbarOffsetMm`.

---

### GAP-SLD-05 🟡 Single-column layout collapses large panels

**File:** `SLDLayoutEngine.cs:44–68`

All children of a panel share a single vertical column via `yByLevel`.
A distribution board with 40 circuits creates a view 40 × 13 mm = 520 mm
tall — too tall for an A1 sheet. There is no wrapping, no column break,
and no `TotalHeight` cap.

**Fix:** Add a `MaxCircuitsPerColumn` constant (default 24 for A1).
When a panel has more circuits than the limit, wrap to the next column
with an X offset of `levelDx`. Emit a horizontal busbar continuation
symbol (dashed line) at the column break. `TotalWidth` and `TotalHeight`
must be recalculated to reflect the true multi-column extents.

---

### GAP-SLD-06 🟡 Busbar segments ignore panel and load spread

**File:** `SLDLayoutEngine.cs:56–59`
```csharp
var busFrom = new XYZ(pos.X - Mm(10), busY, 0);
var busTo   = new XYZ(pos.X + Mm(10) + node.Children.Count * Mm(SymbolSpacingMm), busY, 0);
```
The busbar is drawn ±10 mm from the panel symbol regardless of how
many circuits it feeds or how wide they are spaced. When circuit spacing
is standard (5 mm) and there are 20 circuits, the busbar is 10+10+100 = 120 mm
but the circuits are scattered over more than that.

**Fix:** Calculate `busTo` from the actual X position of the rightmost
child symbol after `Place(child)` has run, not from the formula.
Store child positions in a temporary list inside the `Place` closure,
then set `busTo.X = maxChildX + Mm(SymbolSpacingMm / 2)`.

---

### GAP-SLD-07 🟡 Branch-line weight and style not differentiated

**File:** `SLDAnnotationPlacer.cs:86–106`
```csharp
doc.Create.NewDetailCurve(view, Line.CreateBound(seg.from, seg.to));
```
Both busbar segments (main feed) and branch lines (circuit tails) use
`NewDetailCurve` with no `LineStyle` parameter. Revit assigns the view's
default detail line weight (typically 0.18 mm). A real SLD uses:

- **Busbar:** wide line (0.50 mm), solid
- **Branch tap:** medium line (0.25 mm), solid
- **Control wiring:** thin line (0.13 mm), dashed

**Fix:** Resolve `LineStyle` elements by name before the draw loop:
```csharp
var wideStyle  = GetLineStyle(doc, "STING_SLD_BUSBAR");    // 0.50 mm
var thinStyle  = GetLineStyle(doc, "STING_SLD_BRANCH");    // 0.25 mm
```
Use `doc.Create.NewDetailCurve(view, line, wideStyle.Id)` (Revit 2025+
overload) for busbars and `thinStyle` for branches.
`TemplateCommands.CreateLineStylesCommand` already creates STING line
styles — add `STING_SLD_BUSBAR` and `STING_SLD_BRANCH` to that run.

---

### GAP-SLD-08 🟡 Annotation tokens {curve} and {unit} always empty

**File:** `SLDAnnotationPlacer.cs:113–117`
```csharp
.Replace("{curve}", "")
.Replace("{unit}", "")
```
The curve type (B · C · D for MCBs) and current unit (A · kA) are
always blank. The `RatingFormat` string from the standard rules is e.g.
`"{poles}P {rating}{unit} {curve}"` but produces `"3P  "` instead of
`"3P 32A C"`.

**Fix:**
- `{unit}` — derive from `node.Rating` numeric value: if ≥1000, display
  as kA and divide by 1000; otherwise `A`.
- `{curve}` — read `STING_BREAKER_CURVE_TXT` from the protection element,
  or from the circuit's protective device type parameter `ELC_CDT_CURVE_TXT`.

---

### GAP-SLD-09 🟡 Pole-count tick marks not drawn on branch lines

**Files:** `SLDAnnotationPlacer.cs`, `SLDLayoutEngine.cs`

A 3-phase circuit should show **three parallel diagonal tick marks**
crossing the branch line at 45°, 3 mm apart. This is universal IEC 60617
convention. Currently the branch line is a plain `DetailCurve` with no ticks.

**Fix:** After drawing each branch line, check `node.Poles`. If ≥2,
draw `node.Poles` short perpendicular DetailCurves crossing the branch
line at its midpoint, spaced 2 mm apart, length 3 mm, rotated 45°.

---

### GAP-SLD-10 🟡 TextNoteType not standard-aware

**File:** `SLDAnnotationPlacer.cs:73–75`
```csharp
var tnt = new FilteredElementCollector(doc)
    .OfClass(typeof(TextNoteType))
    .FirstElementId();
```
`FirstElementId()` picks whatever TextNoteType appears first in the
collector — typically the project's default. The IEC standard requires
1.8 mm text; BS 7671 traditionally uses 2.5 mm. Both require Isonorm
or similar engineering font.

**Fix:** Resolve by name: `"STING_SLD_{standardId}_LABEL"`. If not
found, fall back to first available. `CreateLineStylesCommand` should
also create these TextNoteTypes alongside the line styles.

---

### GAP-SLD-11 🔴 Feeder CSA and fault kA not annotated

**File:** `SLDRiserDiagramCommand.cs:12–17`
```csharp
public bool ShowFaultKa;
public bool ShowFeederCsa;
public bool ShowLoadingPct;
```
`RiserOptions` declares these flags and the Electrical Panel UI exposes
them, but `DrawRiser` and `SLDAnnotationPlacer.BuildCircuitLabel` never
read them. The options exist in the UI but have no effect.

**Fix:** Thread `opts.ShowFaultKa`, `opts.ShowFeederCsa`,
`opts.ShowLoadingPct` into `BuildCircuitLabel` via `AnnotationRules` or
directly via a separate label line:
- `ShowFeederCsa` → read `ELC_CDT_CSA_MM2` or the conductor parameter on
  the feeder cable, append e.g. `4×50mm²` below the main label.
- `ShowFaultKa` → read `ELC_FAULT_KA_TXT` from the panel/circuit,
  append `Isc=12kA` in parentheses.
- `ShowLoadingPct` → `node.LoadKW / (node.Rating × 0.23 × node.Poles) × 100`.

---

### GAP-SLD-12 🔴 SLD view never placed on a sheet

**File:** `SLDGenerator.cs:51–65`

The generator creates a `ViewDrafting` and returns it. No code calls
`DrawingDispatcher.Resolve(doc, "Electrical", "*", "RISER")` or
`SheetTemplateEngine` to place the view on a sheet. The user has to
drag it manually.

**Fix:** After `tx.Commit()`, call:
```csharp
var dt = DrawingDispatcher.Resolve(doc, "Electrical", "*", "RISER");
if (dt != null) DrawingTypePresentation.Apply(doc, result.SLDView, dt);
// Then place on sheet via SheetManagerEngine.AutoPlaceViewport
```
The `elec-riser-A2-1to100` drawing type already exists in
`STING_DRAWING_TYPES.json`. Wire it up.

---

### GAP-SLD-13 🔴 Compound symbols never placed

**File:** `SLDGenerator.cs:273–313` (`PlaceSymbols`)

`CompoundSymbolPlacer` exists in `Core/Symbols/` and supports
`VerticalStack`, `HorizontalSeries`, and `Ladder` layout for composite
devices (RCBO = MCB + RCD; contactor with overcurrent relay). But
`PlaceSymbols` only calls `doc.Create.NewFamilyInstance` for a single
concept — compound placement is never invoked.

**Fix:** In `PlaceSymbols`, after resolving `node.ConceptId`, check
`SymbolConceptRegistry.IsCompound(node.ConceptId)`. If true, call
`CompoundSymbolPlacer.Place(doc, view, pos, node.ConceptId, standard)`
instead of the single-family path.

---

### GAP-SLD-14 🟡 Symbol families must be pre-loaded manually

**File:** `SLDGenerator.cs:286–291`
```csharp
var sym = new FilteredElementCollector(doc)
    .OfClass(typeof(FamilySymbol))
    ...FirstOrDefault(s => string.Equals(s.Name, fam ...));
if (sym != null) { ... }
```
If `sym == null` the symbol is silently skipped — no warning, no
fallback. On a fresh project where SLD annotation families were never
loaded, every node silently produces no symbol. The result panel shows
`SymbolsPlaced: 0` with no explanation.

**Fix:** Before `PlaceSymbols`, call a new
`SymbolLibraryCreator.EnsureLoaded(doc, symbolIds, standardId)` method
that checks which families are missing and loads them from
`Families/SLD/` (or generates them from `STING_SLD_SYMBOLS.json`).
Add a `result.Warnings.Add("Symbol not loaded: {fam} — run Load SLD Families first")` when `sym == null`.

---

### GAP-SLD-15 🟡 `STING_SYMBOL_ID` parameter required but not guaranteed

**File:** `SLDCircuitTraverser.cs:166–181`

`SymbolConceptForElement` checks `STING_SYMBOL_ID` parameter on the
element. If the project's shared parameters weren't loaded (i.e.
`LoadSharedParamsCommand` not run), this parameter doesn't exist and
every element returns `null`. All `node.ConceptId` values are `null`,
so `PlaceSymbols` silently skips every element.

**Fix:** Add a pre-flight check in `SLDGenerator.GenerateSLD`:
```csharp
if (!SymbolParametersAreLoaded(doc))
{
    result.Warning = "Run 'Load Params' first — STING_SYMBOL_ID is not bound in this project.";
    return result;
}
```

---

### GAP-SLD-16 🟡 SLD sync has no UI toggle

**File:** `SLDSyncUpdater.cs:98–113`

Live sync is gated on `project_config.json` `"sld_sync_enabled": true`
which must be set by hand in a text editor. There is no button in the
Electrical Panel dock panel to enable/disable it.

**Fix:** Add `SLDSyncToggleCommand` button to the Electrical tab
(alongside `GenerateSLD` and `GenerateRiser`). The command reads the
current value, flips it, and writes it back — same pattern as
`AutoTaggerToggleCommand`.

---

### GAP-SLD-17 🟡 Riser CreateOrReplaceView doesn't replace

**File:** `SLDRiserDiagramCommand.cs:65–82`

```csharp
if (existing != null) return existing;
```
When a view with the same name already exists it is returned unchanged.
The caller's `DrawRiser` then draws on top of the old content, doubling
all boxes and feeder lines. "Replace" in the method name is misleading.

**Fix:** Delete the existing view's content before returning it:
```csharp
if (existing != null)
{
    var content = new FilteredElementCollector(doc, existing.Id).ToElementIds();
    foreach (var id in content) try { doc.Delete(id); } catch { }
    return existing;
}
```

---

### GAP-SLD-18 🟡 Riser layout ignores actual Revit levels

**File:** `SLDRiserDiagramCommand.cs:92–103`

The riser stacks panels at BFS depth (distance from MSB in the circuit
tree), not at actual floor elevation. A panel on Level 3 fed directly
from the MSB appears in column 1 alongside other Level 3 panels —
but so does a sub-board two tiers below MSB that happens to be on Level 3.

**Fix:** After `BuildHierarchy`, resolve each panel's actual Revit level
from `fi.LevelId` or the nearest level below `fi.Location`. Group panels
by level name; lay levels out vertically with consistent row heights (one
row per floor, labelled on the left with the level name). Feeders connect
vertically through floors.

---

### GAP-SLD-19 🟢 SLDResult.Warning swallows all but the last error

**File:** `SLDGenerator.cs:14–21`
```csharp
public string Warning { get; set; }
```
Every catch block overwrites this single string. In a 200-circuit
project with 15 warnings only the final one is visible.

**Fix:** Change to `List<string> Warnings { get; set; } = new();`
and update all call sites to `.Warnings.Add(...)`. Mirror the pattern
used by `DropResult`, `FabricationResult`, etc.

---

## Part 3 — Gaps Summary Table

| ID | Severity | Layer | What breaks | Quick fix |
|---|---|---|---|---|
| SLD-01 | 🔴 | Traverser | Multi-MSB buildings only show first MSB | Return `List<SLDNode>` |
| SLD-02 | 🔴 | Traverser | No breaker symbols placed | Synthesise protection nodes from circuit rating |
| SLD-03 | 🔴 | Traverser | No ampere values on diagram | Read `RBS_ELEC_CIRCUIT_RATING` built-in param |
| SLD-04 | 🟡 | Layout | Standard spacing ignored | Use `AnnotationRules.SymbolSizeMm` |
| SLD-05 | 🟡 | Layout | Large panels overflow sheet | Wrap to columns at `MaxCircuitsPerColumn` |
| SLD-06 | 🟡 | Layout | Busbar too short or too long | Measure actual child extent after `Place()` |
| SLD-07 | 🟡 | Annotation | All lines same weight | Apply `STING_SLD_BUSBAR` / `STING_SLD_BRANCH` line styles |
| SLD-08 | 🟡 | Annotation | Curve type and units blank | Read curve from protection element; derive unit from value |
| SLD-09 | 🟡 | Annotation | No pole-count tick marks | Draw diagonal ticks on branch line at midpoint |
| SLD-10 | 🟡 | Annotation | Wrong text size | Resolve `STING_SLD_{standard}_LABEL` TextNoteType |
| SLD-11 | 🔴 | Annotation | CSA / fault kA / loading% never shown | Thread `RiserOptions` flags into label builder |
| SLD-12 | 🔴 | Generator | View never placed on sheet | Call `DrawingDispatcher` + `SheetManagerEngine.AutoPlaceViewport` |
| SLD-13 | 🔴 | Generator | Compound symbols (RCBO, contactor+OL) never placed | Call `CompoundSymbolPlacer.Place` for compound concepts |
| SLD-14 | 🟡 | Generator | Missing families silently skipped | Add `EnsureLoaded` pre-flight + warning per missing family |
| SLD-15 | 🟡 | Traverser | All ConceptIds null if LoadParams not run | Add pre-flight binding check |
| SLD-16 | 🟡 | Sync | Live sync only toggleable via JSON text edit | Add `SLDSyncToggleCommand` button |
| SLD-17 | 🟡 | Riser | Re-run doubles content | Delete existing view content before redraw |
| SLD-18 | 🟡 | Riser | Floors not represented by actual level | Group panels by `fi.LevelId`, lay out vertically by elevation |
| SLD-19 | 🟢 | Generator | Only last error visible | Change `Warning: string` → `Warnings: List<string>` |

---

## Part 4 — Recommended Fix Order (for 100% automation)

Work these in order — each group unblocks the next.

### Sprint 1 — Make the diagram correct (blockers first)

1. **SLD-03** Rating population — without ampere values the diagram is useless
2. **SLD-02** Protection node synthesis — without breaker symbols it's not an SLD
3. **SLD-01** Multi-root support — needed for every real UK project
4. **SLD-11** CSA / fault kA / loading% annotation — wires up existing UI flags
5. **SLD-19** `Warnings` list — needed to see the other fixes working

### Sprint 2 — Make the diagram look right

6. **SLD-07** Line weight differentiation
7. **SLD-08** Curve type and unit tokens
8. **SLD-09** Pole-count tick marks
9. **SLD-10** TextNoteType standard-awareness
10. **SLD-04** Standard-aware layout spacing

### Sprint 3 — Make the diagram publish itself

11. **SLD-12** Automatic sheet placement
12. **SLD-13** Compound symbol placement
13. **SLD-05** Column wrapping for large panels
14. **SLD-06** Busbar width from actual child positions

### Sprint 4 — Polish and ops

15. **SLD-14** `EnsureLoaded` pre-flight
16. **SLD-15** Binding pre-flight check
17. **SLD-17** Riser replace-not-double
18. **SLD-18** Riser level-based layout
19. **SLD-16** Live-sync UI toggle

---

## Part 5 — Symbol Variant Configuration Quick Reference

### FireDamper — what each type parameter controls

| Parameter | Where set | Controls |
|---|---|---|
| `PEN_FIRE_RATING_TXT` | Type (Family Types dialog) | Which fire-resistance class the damper is certified to — `FR60`, `FR90`, `FR120`. The `PenetrationProductSelector` matches this against the host wall's `STING_FIRE_RATING_TXT` to pick the right variant automatically. |
| `FD_BSEN15650_CLASS_TXT` | Type | BS EN 15650 classification string that appears in the Penetration Register schedule. `EI60S` = 60 min integrity + insulation, self-closing. `EIS60` = fire + smoke combined. `ES` = smoke only. |
| `FD_ACTUATION_TXT` | Type | How the blade closes — `FUSIBLE_LINK_72C` (fusible link at 72°C, no power), `MOTORISED_24V` (24V actuator, spring-return on power loss), `MOTORISED_24V_FUSIBLE_LINK` (both). Important for the M&E coordinator — motorised dampers need a 24V power supply run to them. |
| `FD_TRIGGER_TEMP_C` | Type | For fusible types only — the rated release temperature. `72` for standard; `93` for high-temperature zones (kitchens, plant rooms). `N/A` for motorised types. |
| `FD_RESET_AFTER_TEST_TXT` | Instance | Set during commissioning after each fire test: `GRAVITY_RESET`, `MANUAL_RESET`, `POWER_RESET`, or left blank until tested. |
| `ASS_PRODCT_COD_TXT` | Type | STING product code used in the asset tag. `FD` for fire dampers, `FSD` for combined fire+smoke. |

### SpecialityEquipment (FRP) — what each type parameter controls

| Parameter | Where set | Controls |
|---|---|---|
| `PEN_FIRE_RATING_TXT` | Type | Fire-resistance period — `FR30`, `FR60`, `FR90`, `FR120`, `FR240`. The selector matches this against the host element's fire rating. |
| `PEN_SEALANT_TYPE_TXT` | Type | Material used — `INTUMESCENT`, `INTUMESCENT_WRAP`, `INTUMESCENT_BOARD`, `ACOUSTIC_FIRE_COMPOUND`, `FIRE_RATED_FOAM`, `NONE`. Drives the material take-off schedule. |
| `PEN_CERTIFICATION_TXT` | Type | Standard reference printed on the Penetration Register — e.g. `BS 476-20 / EN 1366-3 (60 min)`. |
| `PEN_SEALANT_TYPE_TXT` | Instance | Updated by installer on site if actual product differs from designed type. |

### AcousticSeal — what each type parameter controls

| Parameter | Where set | Controls |
|---|---|---|
| `ACS_RW_TARGET_DB` | Type | Target weighted sound reduction in dB. The coverage audit flags any acoustic seal where the host partition's `STING_ACOUSTIC_RW_DB` parameter exceeds this value. |
| `ACS_SEAL_TYPE_TXT` | Type | Sealant construction — `MINERAL_WOOL`, `MINERAL_WOOL_PLUS_SEALANT`, `MINERAL_WOOL_PLUS_SEALANT_PLUS_PUTTY`, `FLEXIBLE_BOOT`, `PIPE_SLEEVE_ACOUSTIC_LINING`, `LABYRINTH_BAFFLE`. |
| `ACS_DEPTH_MM` | Type | Minimum depth of sealant in the wall — checked against `PEN_SLAB_THICK_MM` during coverage audit. |
| `ACS_CERT_TXT` | Type | Standard reference for the acoustic design — `BS 8233`, `Approved Document E`, `DW/144`. |

---

*Last updated: Phase 179. For SLD implementation history see `docs/CHANGELOG.md`. For open roadmap items see `docs/ROADMAP.md`.*
