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

All listed parameters are **optional** — the placer's `LookupParameter`
calls are guarded by null-checks — but adding them unlocks features:

| Parameter name                          | Storage  | Purpose                                                  |
|-----------------------------------------|----------|----------------------------------------------------------|
| `Symbol Scale`                          | Integer  | View scale (50 for a 1:50 view). Placer only writes when current value is at the convention default of 50, so families authored at non-default scales aren't overwritten. |
| `Symbol Scale`                          | Double   | Same — placer accepts either storage type.              |
| `STING_ISO_SYMBOL_SCALE_IN`             | Double   | STING-namespaced parallel param.                        |
| `STING_PLACED_BY_SYMBOL_PLACER_BOOL`    | Integer  | **Required for idempotency.** Set to 1 by the placer; used to detect "already placed" instances in NewOnly mode and to find purge targets in Replace mode. |
| `STING_PLACER_ASSY_ID_TXT`              | String   | Owning assembly's ElementId.                            |
| `STING_PLACER_MEMBER_ID_TXT`            | String   | Source member's ElementId — keyed for idempotency.      |
| `STING_PLACER_SYMBOL_CODE_TXT`          | String   | Resolved CSV `symbol_code`.                             |

**Without** the stamp parameters above, re-running the placer will
create duplicates because the placer can't tell that a member was
already symbolised. Add them as instance shared parameters in every
family in this folder.

If you parameterise linework size by `Symbol Scale`, a 1:25 detail and
a 1:50 spool render the same plotted millimetres.

### Placement modes

`FabricationOptions.SymbolPlacementMode` is a tri-state read by both
auto and manual paths:

| Mode      | Behaviour                                                           |
|-----------|--------------------------------------------------------------------|
| `Off`     | Skip the placer entirely.                                          |
| `NewOnly` | (default) Skip members whose `STING_PLACER_MEMBER_ID_TXT` already matches a placed instance on the view — idempotent re-runs. |
| `Replace` | Purge every placer-stamped instance on the view first, then re-place all members from scratch. |

Per-discipline gates (`FabricationOptions.PlaceISOPipe / PlaceISODuct
/ PlaceISOElectrical`) further restrict which fabricators emit symbols.

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

## View identification

Every ISO 6412 view created by `AssemblyViewBuilder.CreateIso6412Section`
is renamed to:

```
STING ISO 6412 - {assemblyTypeName} ::{assemblyElementId}
```

The trailing `::id` is parsed back by `PlaceIsoSymbolsCommand` so the
standalone command can find the right view from a selected
`AssemblyInstance` (and vice-versa). Don't rename these views —
renaming breaks the link and the standalone command falls back to a
slow `GetAssociatedAssemblyViews()` scan that won't return the
hand-rolled section.

## Audit trail

Every placement run appends to `STING_v4_iso_symbols.csv` in the
project's output directory:

```
assembly_id, assembly_name, view_id, view_name, member_id,
member_category, member_name, family_name, symbol_code,
family_file, resolved
```

`resolved = 1` rows show what got matched; `resolved = 0` rows show
which members fell through the resolution chain — useful for extending
the catalogue.

Placed `FamilyInstance` ids are added to `FabricationResult.SymbolIds`
and persisted by `FabricationUndoManager`, so the standard
`Undo Fabrication Package` command rolls back symbols too.
