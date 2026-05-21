# Families — Drop Zone for STING-Compatible Revit Family Libraries

This directory is the on-disk family library that the placement, symbol,
fabrication and tag engines search at runtime. Drop your `.rfa` files
into the right subdirectory and they get picked up automatically — no
code change needed.

---

## CRITICAL: Two tracks, do not confuse them

STING uses families for two structurally different jobs. The rules for
authoring and replacement are not the same. If you put a 3D model
family where a symbol belongs, or vice versa, the placement pipeline
breaks silently.

### Track A — 3D model families (real product geometry)

**What:** luminaires, AHUs, FCUs, valves, panels, MGS terminal units,
beds, scrub sinks, doors, windows, sanitary ware, fire devices, any
piece of equipment that occupies real space in the model.

**Where:** `Families/MEP/`, `Families/MedGas/`, `Families/Healthcare/`,
plus discipline subfolders authors create as needed.

**Template:** Revit category-appropriate **3D family template**
(`Metric Lighting Fixture.rft`, `Metric Generic Model.rft`, etc.). Must
be a 3D Generic Model or category-specific 3D family — **NOT** a
Generic Annotation.

**Sizing:** carry **real product dimensions**. A 1200 mm AHU should be
1200 mm in the family. The placement engine will read the bounding box
at placement time when `FamilyBboxAware: true` is set on the rule.

**Status today:** these are **what you should replace placeholders
with**. There are currently **zero** shipped `.rfa` files for 3D
equipment in this repo — placement rules exist in
`Data/Placement/STING_PLACEMENT_RULES.*.json` but silently skip
elements when the real family is missing. See **§ Priority acquisition
list** below.

### Track B — 2D symbol families (schematic representation)

**What:** ISO 6412 spool symbols, MEP single-line symbols, electrical
SLD symbols, fire-protection symbols, plumbing symbols.

**Where:** `Families/ISO6412/`, `Families/SLD/`, `Families/Seeds/`,
`Families/Annotation/`.

**Template:** **Generic Annotation** (`.rft`) — 2D only. A 3D Generic
Model here will be rejected by `IsoSymbolPlacer.cs:823-842` with a
warning, and placement will fall back to the auto-generated draft
symbol.

**Sizing:** symbols are **schematic** by design. ISO 6412 symbols
target 6 mm paper height regardless of the real product size. **Do not
replace symbol families with photoreal product geometry** — it will
break spool sheets, single-line diagrams and detail views, all of
which rely on consistent paper-space symbol sizing.

**Status today:** 164 ISO 6412 symbols are auto-generated draft
families (line-work approximations). Replacing them with hand-drafted
standard-accurate seed families is a quality upgrade, not a structural
change. See `Families/ISO6412/README.md` for the seed authoring spec.

---

## How families are resolved at runtime

Tier order (`MepSymbolEngine.cs:663-698`,
`FixturePlacementEngine.cs:812-920`):

1. **Tier 0 — already loaded.** Family already in the project document.
2. **Tier 1 — seed families on disk.** Files in `Families/<subdir>/`
   matching the expected name. These beat everything else.
3. **Tier 2 — auto-generated drafts.** Symbols produced by
   `SymbolLibraryCreator` in `<project>/_BIM_COORD/Families/Symbols/`.
   Only used when Tier 1 missing.
4. **Tier 3 — category match.** Any loaded family whose
   `Category.Name` matches the rule's `CategoryFilter`. Last-ditch
   fallback.
5. **No 3D placeholder fallback.** If all four tiers miss, placement
   is **silently skipped** with a warning in `PlacementResult.Warnings`.
   The engine does **not** synthesise a generic box. The only
   exception is `PlaceHangersCommand`, which falls back to 2D
   DetailCurve crosshair previews when no Generic Model hanger family
   exists.

---

## Vendor / manufacturer family intake — Standard Operating Procedure

When importing a manufacturer's `.rfa` library (Beaconmedaes, GCE,
Pattons, Victaulic, Megasan, Wandsworth, Static Systems,
manufacturer-supplied REL/REL Revit Add-ins, etc.):

### Step 1 — Sanity check before stamping

Run the **Tag `FamilyConformanceCheck`** dock-panel button (Tags →
QA → Conformance Check) against each `.rfa`. The checker is
read-only — it reports:

- Is the family template appropriate for its category? (3D vs
  annotation)
- Are the STING shared parameters bound by **GUID**, not just name?
- Does the family expose `MNT_HGT_MM`, `ASS_PLACE_ANCHOR_TXT`,
  `ASS_PLACE_OFFSET_X_MM`, `ASS_PLACE_SIDE_TXT`?
- For tag families: are the 128 `TAG_{size}{style}_{colour}_BOOL`
  visibility parameters present?
- Score: 0–100. **Treat <70 as a blocker.**

Catches the most common failure mode: manufacturer family uses
`Mounting Height` (typed parameter) instead of `MNT_HGT_MM` (STING
shared parameter), so the placement engine silently fails to write
the mounting height.

### Step 2 — Stamp the family (additive)

Run `FamilyParamCreator` (Tags → Setup → Family Param Creator) with:

| Option | Value | Rationale |
|---|---|---|
| `PurgeMode` | `None` | Keep the manufacturer's own parameters; only add what's missing. |
| `InjectFormulas` | `true` | Add the 16-branch TAG_POS nested-if formula chain. |
| `InjectTagPos` | `true` | Add the `STING_TAG_POS` integer parameter. |
| `CreatePositionTypes` | `true` | Mint Ring 1 / Ring 2 position types so tag placement works. |
| `LoadAfterSave` | `true` | Reload the stamped family into the active project. |
| `LoadOverwriteParameterValues` | `false` | Don't clobber existing values on the in-project copy. |

The flow is **idempotent** — running it twice does not duplicate
parameters. It only adds what's missing.

### Step 3 — Drop into the right subdirectory

| Family kind | Target folder |
|---|---|
| 3D MEP equipment | `Families/MEP/` |
| 3D Medical gas equipment | `Families/MedGas/` |
| 3D Healthcare clinical equipment | `Families/Healthcare/` (create if absent) |
| 3D Fire protection devices | `Families/FP/` (create if absent) |
| 2D ISO 6412 symbols | `Families/ISO6412/` (see folder README) |
| 2D SLD symbols | `Families/SLD/` |
| 2D Annotation seeds | `Families/Annotation/` or `Families/Seeds/` |
| Title-block (sheet) | `Families/AssemblyTitleBlocks/` |

### Step 4 — Tune the placement rule (if 3D)

If the family has a footprint significantly different from the
~150 mm default the legacy rules were tuned for (e.g. AHU at 1200 mm),
either:

- **Manually adjust** the rule's `MinSpacingMm` / `OffsetXMm` /
  `CoverageRadiusMm` in `Data/Placement/STING_PLACEMENT_RULES.*.json`,
  or
- **Enable `FamilyBboxAware: true`** on the rule. The engine then reads
  the resolved `FamilySymbol`'s bounding box and scales the
  ~150 mm-tuned spacings proportionally to
  `ReferenceFootprintMm` (default 150). This keeps one rule usable
  across multiple manufacturers without per-vendor JSON edits.

### Step 5 — Type catalog for libraries with > 20 sizes

If the manufacturer family ships 50+ types (valves, fittings, wire
sizes), pair it with a **`.txt` type catalog sidecar** next to the
`.rfa`. The placement engine will load-on-demand from the catalog
when the rule specifies a `TypeCatalogKey`, avoiding the bloat of
loading 200 types into the project. See § Type catalogs below.

---

## Type catalogs (`.txt` next to `.rfa`)

Revit natively supports `.txt` type catalogs that let one `.rfa`
expose hundreds of types without all loading. STING's placement
engine reads these via the optional `TypeCatalogKey` field on
`PlacementRule`.

### Filename convention

`YourFamily.rfa` is paired with `YourFamily.txt` in the same folder.
Revit looks for the `.txt` automatically on `Document.LoadFamily`.

### Catalog format (Revit standard)

First row: parameter headers. Subsequent rows: one per type.

```
,Nominal_Diameter_mm##length##millimetres,Pressure_Rating_bar##other##
DN15-PN16,15,16
DN20-PN16,20,16
DN25-PN16,25,16
...
```

### How STING picks one

A placement rule sets `TypeCatalogKey` to one of the type-name strings
(or a regex like `^DN20-PN1[06]$`). At resolve time the engine loads
only the matching type rather than the whole catalog. If multiple
match, the first one wins (rule's `FamilyTypeRegex` further refines).

If a rule has no `TypeCatalogKey`, the engine ignores the catalog and
behaves as before (loads all types — the legacy path).

---

## Priority acquisition list

These categories have placement rules that silently fail today
because no shipped family exists. Acquire in this order:

1. **MGS terminal units / manifolds** — `Families/MedGas/` (per
   `Families/MedGas/README.md`). Vendor: Beaconmedaes / GCE / Pattons.
2. **Luminaires** — `Families/MEP/Lighting/`. Vendor: Erco / Zumtobel
   / Fagerhult / regional equivalents.
3. **AHU / FCU** — `Families/MEP/HVAC/`. Vendor: Carrier / Daikin /
   Trane / regional equivalents.
4. **Distribution panel boards** — `Families/MEP/Electrical/`.
   Vendor: Schneider / ABB / Eaton / Siemens.
5. **Bedhead trunking + medical service panels** —
   `Families/Healthcare/`. Vendor: Wandsworth / Static Systems.
6. **Fire detection devices** — `Families/FP/`. Vendor: Apollo /
   Hochiki / Notifier.

For each, follow the vendor-intake SOP above.

---

## Where the engine code lives

| Concern | File |
|---|---|
| Placement rule schema | `StingTools/Core/Placement/PlacementRule.cs` |
| Family resolution + auto-load | `StingTools/Core/Placement/FixturePlacementEngine.cs:812-984` |
| Family bbox-aware scaling | `StingTools/Core/Placement/PlacementRule.cs` (`FamilyBboxAware`, `ReferenceFootprintMm`) + engine helper `ScaleByFootprint(rule, symbol)` |
| Type catalog support | `StingTools/Core/Placement/FixturePlacementEngine.cs` (`TryLoadFromCatalog`) |
| Symbol resolver (Tier 1→3) | `StingTools/Core/Symbols/MepSymbolEngine.cs:663-698` |
| Symbol auto-generator (drafts) | `StingTools/Core/Symbols/SymbolLibraryCreator.cs` |
| Family-mutation engine | `StingTools/Tags/FamilyParamCreatorCommand.cs` |
| Conformance checker | `StingTools/Tags/FamilyConformanceCheckCommand.cs` |
| Tag family creator | `StingTools/Tags/TagFamilyCreatorCommand.cs` |
| Hanger resolver (Tier 1→3 + 2D crosshair fallback) | `StingTools/Core/Calc/HangerFamilyResolver.cs` |

---

## Caveats

1. The 7 title-block families in `Families/AssemblyTitleBlocks/` are
   shipped as parameter-spec stubs only. `ShopDrawingComposer.ResolveTitleBlock`
   falls back to the first available title block in the project with
   a warning. Fab sheets produced today have generic (not
   discipline-specific) layout until real `.rfa`s are dropped in.
2. The 164 ISO 6412 symbol families are auto-generated drafts. Their
   line work is approximate; replacing them with hand-drafted ISO-
   compliant seeds is a quality upgrade. **Do not** replace them with
   3D product geometry — symbols are intentionally schematic.
3. None of these changes have been verified in Revit on this branch
   (Linux sandbox). Verify in Revit before merging to `main`.
