# Specialist tag families — hand-authoring sheet

Four tag families that the drawing types need and that the Revit API cannot build:
it cannot author label rows. A person builds each one in the Family Editor from this
sheet; everything around them is already in place (2026-09-24, branch
`claude/specialist-tag-params`).

| Family | Tags | For drawing type |
|---|---|---|
| `STING - Fire Door Tag` | Doors | `arch-fire-strategy-A1-1to100` |
| `STING - Accessible Door Tag` | Doors | `arch-accessibility-A1-1to100` |
| `STING - Room Finish Tag` | Rooms | `arch-floor-finishes-A1-1to100` |
| `STING - Fire Compartment Tag` | Rooms | `arch-fire-strategy-A1-1to100` |

**Already done, so do not redo it:**

- Every parameter below exists in `MR_PARAMETERS.txt` and is bound to the tagged
  category in `RESOLVED_BINDINGS.csv` (checked row by row — see the table in each
  section). Run **Load Shared Params** on the project first so the tagged elements
  carry them.
- Each family is declared in `STING_TAG_CONFIG_v5_0_ARCH.csv` (and its
  `_DesignConstruction` twin) with its **own** `LabelMaster` group, so
  **Propagate Universal** skips it and never overwrites the bespoke label. The groups
  are one family each on purpose: a shared group would let one of the four, run as a
  master, overwrite the other three.
- `TAG_PLACEMENT_PRESETS_DEFAULT.json` has a placement preset for each.

**Family names must match exactly** — including the spaces around the hyphen. The
declaration, the preset and the drawing type all look the family up by name.

---

## Mechanics that apply to all four

- **Template.** Doors: `Metric Door Tag.rft`. Rooms: `Metric Room Tag.rft`. Category
  is set by the template — check *Family Category and Parameters* reads *Door Tags* /
  *Room Tags* before saving.
- **Adding a shared parameter to the label.** *Edit Label → Add Parameter → Select… →*
  pick it from `MR_PARAMETERS.txt` (set it as the shared parameter file first). A
  calculated value can only name parameters that are already in the label's category
  parameter list, so add every parameter a formula names — even one that is not
  itself a visible row.
- **Calculated value rows.** *Edit Label → Add Calculated Value*: Name, **Type = Text**,
  paste the Formula. Then Prefix / Suffix, **Spaces = 0**, then Break. Set Spaces
  before ticking Break — Spaces is only editable while the row above has no Break.
  Revit greys out row 1's Spaces and the last row's Break; ignore those two cells.
- **Yes/No parameters are written bare** in a formula — `if(PER_SMOKE_STOP_BOOL, "S", "")`.
  Comparing a Yes/No to `"Yes"` fails with "Inconsistent Units". An unset Yes/No reads
  as No, so the row stays empty until someone ticks it.
- **Prefix and suffix vanish with an empty value**, which is what makes the
  conditional rows below work: when the formula returns `""`, the row prints nothing.
- **␣ marks a space that matters** in a Prefix / Suffix cell. Type a real space.
- **Tiers.** T1 rows have no gate and always print. **Every row a drawing is issued for
  is T1.** T2 rows are gated `if(TAG_DEPTH_TIER_INT > 1, X, "")` — the same gate as the
  universal label (`UNIVERSAL_TAG_LABEL_INTEGER_MIGRATION.md`). Know before relying on
  T2: the gate is read from the **tagged element's type**, and `TAG_DEPTH_TIER_INT` is
  not yet bound to any model category (`Core/TierGateScope.cs`). Until it is bound
  Type-scoped to Doors, every T2 row below stays blank. Rooms have no type at all,
  which is why both room tags are T1 only.
- **Numbers.** Every millimetre / newton / minute parameter here is **TEXT**, not a
  number (the same convention as `RGL_ACCESS_CLEAR_WIDTH_MM`), so it is used bare in a
  label or a Text formula. A label cannot therefore do the compliance comparison
  (e.g. force ≤ 30 N) itself; it prints the value and the unit.

### Types to create (all four families)

Three family types, named by the tag style catalogue's convention
(`Core/TagStyleCatalogue.cs`: `{size}_{style}_{colour}_{arrowhead}_T{depth}`,
architectural default NOM / BLACK / Arrow Open 30 / T2):

| Type name | Label text size | Use |
|---|---|---|
| `2_NOM_BLACK_Open30_T2` | 2.0 mm | 1:50 and denser plans |
| `2.5_NOM_BLACK_Open30_T2` | 2.5 mm | **default** — the 1:100 drawing types above |
| `3.5_NOM_BLACK_Open30_T2` | 3.5 mm | presentation / 1:200 |

Label text size is a property of the **label's** type, not the family type, so make
three copies of the label (one per text size: *Edit Type → Duplicate → Text Size*) at
the same point, and drive each copy's *Visible* from a family Yes/No that the type sets
(`TXT_2_0`, `TXT_2_5`, `TXT_3_5`; exactly one ticked per type). Text font Arial,
width factor 1.0, background Transparent. Build and check the 2.5 mm label first,
then copy it — the copies carry the rows with them.

### Box and leader

- **Leader:** *Family Category and Parameters →* **Leader Arrowhead = Arrow Open 30**.
  Door tags sit on the leaf without a leader (the presets say `addLeader: false`); room
  tags sit inside the room.
- **Box:** Doors — a rectangle of Lines (subcategory *Tag Box*, pen 1 / 0.18 mm)
  drawn round the 2.5 mm label with 1 mm clearance, **made visible by the same
  `TXT_*` switch** as the label it surrounds (so three boxes). Rooms — no box; the room
  tag reads as a block of text inside the room.
- Keep the family origin at the label's centre so the placement offsets in the preset
  mean what they say.

---

## 1. `STING - Fire Door Tag` (Door Tags)

Prints e.g. `D-012` / `FD30S SC` / `Closer: overhead`.

| # | Tier | Row | Parameter / Formula | Spaces | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|---|
| 1 | T1 | Mark | built-in **Mark** (add directly, no fx) | 1 | | | YES |
| 2 | T1 | Fire Rating | built-in **Fire Rating** (Door *type* parameter; add directly) | 0 | FD | | no |
| 3 | T1 | Smoke seal | `if(PER_SMOKE_STOP_BOOL, "S", "")` | 0 | | | no |
| 4 | T1 | Self-closing | `if(PER_SELF_CLOSING_BOOL, "SC", "")` | 0 | ␣ | | YES |
| 5 | T2 | Closer | `if(TAG_DEPTH_TIER_INT > 1, BLE_DOOR_CLOSER_TXT, "")` | 0 | Closer: | | — |

- **Fire Rating convention.** Enter minutes only (`30`, `60`). The FD prefix makes it
  `FD30`, and the smoke-seal row appends `S` for `FD30S` (BS 476-22 / BS 9999
  notation). If the project already types `FD30` into Fire Rating, delete the FD
  prefix — otherwise the tag prints `FDFD30`. An unrated door prints no FD at all,
  because the prefix vanishes with the empty value.
- **Binding check:** `PER_SMOKE_STOP_BOOL`, `PER_SELF_CLOSING_BOOL` → `<ALL>`;
  `BLE_DOOR_CLOSER_TXT` → Doors. `PER_*` are the IFC `Pset_DoorCommon.SmokeStop /
  SelfClosing` round-trip parameters, so an ArchiCAD or IFC import fills them.

## 2. `STING - Accessible Door Tag` (Door Tags)

Prints e.g. `D-012` / `W 850mm  Thr 10mm` / `F 28N  LE 300mm` / `Glazed  VP zone ok`.
Approved Document M Vol 2 / BS 8300-2:2018 limits, printed so a reviewer can check
them against the value: clear width per the approach table, threshold ≤ 15 mm total,
opening force ≤ 30 N (0–30°), leading-edge space ≥ 300 mm on the pull side, vision
panel zone 500–1500 mm AFFL.

| # | Tier | Row | Parameter / Formula | Spaces | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|---|
| 1 | T1 | Mark | built-in **Mark** | 1 | | | YES |
| 2 | T1 | Clear width | `RGL_ACCESS_CLEAR_WIDTH_MM` (add directly) | 0 | W␣ | mm | no |
| 3 | T1 | Threshold | `RGL_ACCESS_THRESHOLD_HEIGHT_MM` (add directly) | 0 | ␣␣Thr␣ | mm | YES |
| 4 | T1 | Opening force | `BLE_DOOR_OPENING_FORCE_N` (add directly) — **new** | 0 | F␣ | N | no |
| 5 | T1 | Leading edge | `BLE_DOOR_LEADING_EDGE_CLEAR_MM` (add directly) — **new** | 0 | ␣␣LE␣ | mm | YES |
| 6 | T1 | Glazing | `if(BLE_DOOR_GLAZING_BOOL, "Glazed", "")` | 0 | | | no |
| 7 | T1 | Vision panel | `if(BLE_DOOR_GLAZING_BOOL, if(BLE_DOOR_VISION_PANEL_ZONE_BOOL, "VP zone ok", "VP ZONE NOT MET"), "")` — **new** param | 0 | ␣␣ | | YES |
| 8 | T2 | Operation | `if(TAG_DEPTH_TIER_INT > 1, BLE_DOOR_OPERATION_TYPE_TXT, "")` | 0 | Op: | | — |

- Row 7 only speaks for a glazed door, and says so loudly when the zone is not met —
  an unticked box on a glazed door is exactly the case to surface.
- **Binding check:** `RGL_ACCESS_CLEAR_WIDTH_MM`, `RGL_ACCESS_THRESHOLD_HEIGHT_MM` →
  `<ALL>`; `BLE_DOOR_GLAZING_BOOL`, `BLE_DOOR_OPERATION_TYPE_TXT` and the three new
  `BLE_DOOR_*` → Doors.

## 3. `STING - Room Finish Tag` (Room Tags)

Prints e.g. `G.04` / `F: FL-02  W: WL-01` / `C: CL-03  B: SK-01`.

| # | Tier | Row | Parameter / Formula | Spaces | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|---|
| 1 | T1 | Number | built-in **Number** | 1 | | | YES |
| 2 | T1 | Floor | `BLE_ROOM_FINISH_FLOOR_COD_TXT` (add directly) | 0 | F:␣ | | no |
| 3 | T1 | Wall | `BLE_ROOM_FINISH_WALL_COD_TXT` (add directly) | 0 | ␣␣W:␣ | | YES |
| 4 | T1 | Ceiling | `BLE_ROOM_FINISH_CEILING_COD_TXT` (add directly) | 0 | C:␣ | | no |
| 5 | T1 | Base | `BLE_ROOM_FINISH_BASE_COD_TXT` (add directly) | 0 | ␣␣B:␣ | | — |

- The codes are the finish-schedule keys, not descriptions — the schedule on the
  same sheet decodes them. **Binding check:** all four → Rooms.

## 4. `STING - Fire Compartment Tag` (Room Tags)

Prints e.g. `G.04` / `Comp FC-03` / `FR 60 min` / `Esc 120 pers`.

| # | Tier | Row | Parameter / Formula | Spaces | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|---|
| 1 | T1 | Number | built-in **Number** | 1 | | | YES |
| 2 | T1 | Compartment | `FLS_COMPARTMENT_ID_TXT` (add directly) | 0 | Comp␣ | | YES |
| 3 | T1 | Fire resistance | `FLS_COMPARTMENT_FR_MINS_TXT` (add directly) — **new** | 0 | FR␣ | ␣min | YES |
| 4 | T1 | Escape capacity | `BLE_ROOM_FIRE_ESCAPE_CAPACITY_TXT` (add directly) | 0 | Esc␣ | ␣pers | — |

- Enter the fire-resistance period as minutes only (`60`), per Approved Document B /
  BS 9999 Table for the compartment.
- **Binding check:** `FLS_COMPARTMENT_ID_TXT` → Rooms **as of this branch** (it was
  bound only to Sprinklers and Fire Alarm Devices, so it could not be typed into a
  room — which also left the `fls-compartment-id` filter and the RDS completeness
  check reading an empty value); `FLS_COMPARTMENT_FR_MINS_TXT` → Rooms (new);
  `BLE_ROOM_FIRE_ESCAPE_CAPACITY_TXT` → Rooms. Re-run **Load Shared Params** on an
  existing project to pick up the Rooms binding — the loader adds categories, it
  never removes them.

---

## After building — wiring them into the drawings

1. Save each `.rfa` into `StingTools/Data/TagFamilies/` under the exact family name
   (e.g. `STING - Fire Door Tag.rfa`).
2. Point the drawing types at them in `StingTools/Data/STING_DRAWING_TYPES.json` —
   `annotation.tagFamilies` for the category:

   | Drawing type | Category | Family |
   |---|---|---|
   | `arch-fire-strategy-A1-1to100` | Doors | `STING - Fire Door Tag` |
   | `arch-fire-strategy-A1-1to100` | Rooms | `STING - Fire Compartment Tag` |
   | `arch-floor-finishes-A1-1to100` | Rooms | `STING - Room Finish Tag` |
   | `arch-accessibility-A1-1to100` | Doors | `STING - Accessible Door Tag` |

   Do this **only after** the `.rfa` is in the library: `DrawingTypeTagFamilyTests`
   fails on a tag family that is not in `TagFamilies/`, and on one declared for a
   different category than the rule it serves.
3. Re-stamp the corporate checksums — the lock is live, and an edited type with a
   stale checksum is demoted to `project`:

   ```
   dotnet run --project tools/StampDrawingTypeChecksums
   dotnet run --project tools/StampDrawingTypeChecksums -- --check
   ```

4. `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj` — in particular
   `DrawingTypeTagFamilyTests`, `TagConfigDeclarationsTests` and
   `UniversalOptOutTests`.
5. In Revit: produce one sheet of each drawing type on a model with the parameters
   filled, and check the tags print the rows above — **not** the generic Door / Room
   tag. A rule naming a family that is not loaded falls back to the pack default
   silently, so a generic tag on the sheet means step 1 or 2 did not land.
