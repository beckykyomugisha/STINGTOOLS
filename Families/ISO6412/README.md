# ISO 6412 Detail Symbol Library — Family Contract

This directory holds the `.rfa` detail-component families that
`StingTools.Core.Fabrication.IsoSymbolPlacer` drops onto fabrication
shop drawings (the ISO 6412 axonometric created by
`AssemblyViewBuilder` for every spool / assembly).

The placer is invoked two ways:

1. **Auto** — `Generate Fabrication Package` runs the placer for every
   generated assembly when `FabricationOptions.PlaceISO6412Symbols` is
   on (Fabrication tab → "Place ISO 6412 symbols" checkbox).
2. **Manual** — the standalone `Place ISO 6412 Symbols` command
   (`Fabrication_PlaceISOSymbols`) re-runs the placer against the
   active assembly view or a selection of `AssemblyInstance`s, useful
   when this folder is updated after the package was generated.

## Catalogue

The catalogue lives in `StingTools/Data/Fabrication/STING_ISO_SYMBOLS_INDEX.csv`
(188 rows). Each row maps:

| Column           | Purpose                                                   |
|------------------|-----------------------------------------------------------|
| `symbol_code`    | Uppercase keyword matched against assembly member names   |
| `family_filename`| `.rfa` to load — must live in **this** directory          |
| `category`       | `Pipe` / `Duct` / `Conduit` / etc. (used as fallback)     |
| `description`    | Human-readable description (not used at runtime)          |

Lookup order in `IsoSymbolPlacer.ResolveSymbol`:

1. Substring match: member name (uppercase) **contains** `symbol_code`.
2. Category fallback: `member.Category.Name == row.Category` (first row wins).

## Family contract

Every family in this folder MUST satisfy the following so the placer
can use it without per-family special cases:

### File naming

* Filename matches `family_filename` in the CSV exactly, **including**
  case on case-sensitive filesystems.
* Extension is `.rfa`.
* Convention: `STING_FAM_<DISCIPLINE>_<KIND>.rfa`, e.g.
  `STING_FAM_PIPE_ELBOW_90_BW.rfa`.

### Family template

* **Detail Item** family (`Metric Detail Item.rft` or your local
  equivalent) — NOT a 3D model component. The placer calls
  `doc.Create.NewFamilyInstance(point, fs, view)` with a 2D detail
  view, so the family must accept that overload (Detail Item families
  do; Generic Model 3D families do not).
* Drawn **at the family origin**. The placer puts the symbol at the
  member's `LocationPoint` (or first `LocationCurve` endpoint) — there
  is no per-symbol offset, so author the linework so the visual
  centre is at (0,0).
* Symbols are 2D linework only (Symbolic Lines, Filled Regions,
  Detail Lines). No 3D extrusions, no host requirements.

### Required parameters

The placer sets up to three optional instance parameters when present.
None are mandatory — the placer's `LookupParameter` calls are guarded
by null-checks — but adding them lets the symbol size match the host
view's plotted scale.

| Parameter name              | Storage  | Purpose                                                  |
|-----------------------------|----------|----------------------------------------------------------|
| `Symbol Scale`              | Integer  | View scale (e.g. 50 for a 1:50 detail view).            |
| `Symbol Scale`              | Double   | Same — placer accepts either storage type.              |
| `STING_ISO_SYMBOL_SCALE_IN` | Double   | STING-namespaced parallel param for shared-param users. |

If you parameterise linework size by `Symbol Scale`, a 1:25 detail and
a 1:50 spool render the same plotted millimetres.

### Authoring checklist

- [ ] Detail Item family template
- [ ] Linework centred at origin
- [ ] 8 × 8 mm bounding box at 1:50 (scale via `Symbol Scale`)
- [ ] No 3D, no host
- [ ] No reference planes named `Center (Front/Back)` etc. that
      collide with annotation symbol-host expectations
- [ ] `Symbol Scale` instance parameter (Integer, default 50)
- [ ] Family filename matches CSV exactly
- [ ] Test: load into a project, place on a Section view → renders
      cleanly at 1:50 and 1:25

## Missing families

If a family listed in the CSV is not present in this directory:

* `IsoSymbolPlacer.ResolveFamilySymbol` logs a single warning per
  family per session (`StingLog.Warn` → `StingTools.log`).
* The missing filename is added to `FabricationResult.MissingFamilies`
  and surfaced in the `FabricationResultDialog`'s "ISO 6412 symbols"
  card so the user sees exactly what to author next.
* The element is silently skipped — placement continues for other
  members.

## Recommended authoring order

The 188-row catalogue is long; ship a placeholder pack of the highest-
frequency symbols first to validate the wiring, then fill in the long
tail. The top-20 quick-win set:

| Discipline | Symbol codes |
|---|---|
| Pipe | `ELBOW_90_BW`, `ELBOW_45_BW`, `TEE_EQ`, `TEE_RED`, `RED_CONC`, `RED_ECC`, `COUPLING`, `UNION`, `CAP`, `FLANGE_WN`, `FLANGE_SO`, `VALVE_GATE`, `VALVE_BALL`, `VALVE_CHECK` |
| Duct  | `DUCT_ELBOW_90`, `DUCT_TEE`, `DUCT_RED`, `DAMPER_VOLUME` |
| Conduit | `CDT_ELBOW_90`, `CDT_BOX_4SQ` |

Author those 20 first, regenerate a fabrication package against a test
project, and verify symbols render on the ISO views before committing
to the long tail.

## Project overrides

The CSV itself can be overridden per project. `IsoSymbolPlacer` reads
the bundled file via `StingToolsApp.FindDataFile(...)`, which prefers
project-local copies — drop a customised `STING_ISO_SYMBOLS_INDEX.csv`
into the project's data folder to add or remap symbols without
shipping a new plugin build.
