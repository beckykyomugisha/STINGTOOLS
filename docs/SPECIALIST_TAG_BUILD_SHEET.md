# Specialist tag families — hand-authoring sheet

Five tag families that the drawing types need and that the Revit API cannot build:
it cannot author label rows. A person builds each one in the Family Editor from this
sheet; everything around the first four is already in place (2026-09-24, merged in
#974). The fifth — the material callout — follows different rules and has its own
section (§5): read it before building it.

| Family | Tags | For drawing type |
|---|---|---|
| `STING - Fire Door Tag` | Doors | `arch-fire-strategy-A1-1to100` |
| `STING - Accessible Door Tag` | Doors | `arch-accessibility-A1-1to100` |
| `STING - Room Finish Tag` | Rooms | `arch-floor-finishes-A1-1to100` |
| `STING - Fire Compartment Tag` | Rooms | `arch-fire-strategy-A1-1to100` |
| `STING - Material Callout Tag` | **Material Tags** (a face of any host) | `pres-exterior-elev-A1`, `arch-elev-A1-1to100`; sections later (§5.7) |

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

The drawing engine picks the size type for each drawing's scale (`TagSizeVariant`) and
reads **this** naming — the size is everything before the first `_`, and it only
switches between types whose remainder matches, so a bold red 2.5 mm tag becomes a
bold red 2 mm tag, never a black one. (Until 2026-09-24 it only recognised types named
`2.5mm`, so families built to this sheet would have kept their default size.) Keep the
remainder identical across the three types.

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

## 5. `STING - Material Callout Tag` (Material Tags)

Prints e.g. `CLG-001` / `Gypsum board standard 12.5 mm` with a leader to the face it
describes — the callout on an elevation, and (later, §5.7) on a wall build-up in section.

**This one is different from §1–4 in three ways, and each one decides how it is built:**

1. **A material tag reads the MATERIAL, never the element.** Its label can show only the
   material's own parameters — the built-in identity data and shared parameters bound to
   the **Materials** category. Nothing on the wall (`ASS_TAG_1_TXT`, type marks, the
   `ASS_*` family) is visible to it. A multi-category or wall tag cannot show material
   data at all, which is why this is a Material Tag and not a Wall Tag.
2. **It tags a FACE.** Revit places a material tag on a face reference, and the tag reports
   that face's material — on a compound wall's exterior face, the outer layer only. The
   drawing engine does this: `AnnotationRunner.FaceReferenceFor` takes a wall's exterior /
   interior side faces or a floor / roof's top / bottom faces, and picks the one facing the
   viewer (`FaceChoice`).
3. **No tiers.** The `TAG_PARA_STATE_n_BOOL` gates are read from the tagged element's
   *type* (`Core/TierGateScope.cs`); a Material has no type and the gates are never bound to
   Materials. **Do not copy the tier rows or the tier gates into this family.** Variants are
   done with family **types** instead (§5.3).

### 5.1 What exists today — and why not to use it

| Thing | State | Evidence |
|---|---|---|
| Shared parameters **on Materials** | ✅ Works. `CleanMaterialBindings` adds the Materials category to every `MAT_*` / `PROP_*` / `BLE_MAT_*` / `COMP_MAT_*` / `STING_MAT_*` parameter; a 2026-09-21 run logged 116 added, 0 failed. The comments in `LoadSharedParamsCommand.cs`, `SharedParamGuids.cs` and `ParamRegistry.cs` that say Materials cannot take bound parameters are **stale**. | `Tags/LoadSharedParamsCommand.cs` `CleanMaterialBindings` |
| Parameters bound `<ALL>` | ❌ Never reach a Material (`ASS_TAG_1_TXT`, the cost / carbon / tier parameters). | `RESOLVED_BINDINGS.csv` |
| Material identity from the CSV | ✅ **only for materials STING created**: `CreateBLEMaterials` / `CreateMEPMaterials` write Mark = `MAT_CODE`, Keynote = `MAT_ISO_19650_ID`, Description = the long enriched paragraph, Manufacturer, Model (holds the standard), Cost, URL. Existing materials are skipped. | `Temp/MaterialCommands.cs` `ApplyIdentityProperties` |
| Other writers | ⚠ `Materials_StampCodes` and the compound-type creator write the shared `MAT_CODE` only, **not Mark**, so Mark and `MAT_CODE` can disagree. | `StampMaterialCodesCommand.cs`, `Temp/FamilyCommands.cs` |
| `STING - Materials Tag.rfa` (in the library) | ❌ **Not usable as a callout.** It is the universal 55-row label master on a material tag: its rows gate on `TAG_PARA_STATE_*` (cannot resolve on a Material) and ~35 of its parameters are never on Materials. Leave it alone; build the callout as a new family. | `STING_TAG_CONFIG_v5_0_GEN.csv`, `LABEL_DEFINITIONS.json` |
| Engine: tag a face | ✅ Walls, floors, roofs, ceilings, plain solids. ❌ Family instances (doors, windows, curtain panels, furniture) — counted and reported, not tagged. ❌ Painted faces are not preferred. ❌ Every element gets a callout — no one-per-material thinning yet. | `AnnotationRunner.FaceReferenceFor` |
| Engine: the `MaterialTag` rule kind | ❌ Dead. It resolves the host's own tag category, never Material Tags, and the one catalogue rule (`arch-screed-buildup-A3-1to10`, category `"*"`) cannot resolve a category. **Use an `AutoTag` rule with `tagFamily` instead (§5.6)** — that route works today. | `AnnotationRunner.ResolveTagTypeId`, `STING_DRAWING_TYPES.json` |

### 5.2 Data contract — do this on the project first

The label reads built-in identity data first, so the tag works even on a project where
the STING shared parameters were never loaded. For that, every material that will be
called out needs:

| Built-in (as it appears in Edit Label) | Must hold | Today |
|---|---|---|
| **Mark** | `MAT_CODE`, e.g. `CLG-001` — the callout key | Set only on materials STING created. Otherwise type it, or copy `MAT_CODE` into it. |
| **Description** | a **short** name, e.g. `Gypsum board standard 12.5 mm` | STING writes the long enriched paragraph here — too long for a callout. Replace it with `MAT_NAME`. |
| **Manufacturer**, **Model** | maker, product / standard | As STING writes them. |

A `Materials_SyncIdentity` command that does this for every coded material (Mark ←
`MAT_CODE`, Description ← `MAT_NAME`, Keynote ← `MAT_CODE`, long paragraph moved to a
specification parameter) is **not built yet** — see §5.8. Until it is, the material
callout is only as good as those three fields.

### 5.3 Template, category, types

- **Template:** `Material Tag.rft` (the one `TagFamilyCreatorCommand` maps Materials to;
  some libraries ship it as `Metric Material Tag.rft`). *Family Category and Parameters*
  must read **Material Tags**.
- **Types — content × size, named to the tag-style convention** so the engine switches
  size and keeps content (`{size}_{CONTENT}`):

  | Type | Rows | Use |
  |---|---|---|
  | `2_CODE` · `2.5_CODE` · `3.5_CODE` | Mark | Dense elevations and sections with a Materials Key beside them |
  | `2_CODENAME` · **`2.5_CODENAME`** · `3.5_CODENAME` | Mark / Description | **Default.** Elevations and presentation |
  | `2_FULL` · `2.5_FULL` · `3.5_FULL` | Mark / Description / Manufacturer + Model | Specification elevations, samples boards |

  Nine types. The engine reads the size from the text before the first `_` and matches
  the rest, so a drawing that wants 2 mm turns `2.5_CODENAME` into `2_CODENAME`.
- **Labels:** nine label copies at the same point — one per type — each at its text size,
  each with *Visible* tied to its own family Yes/No parameter (`V_2_CODE` … `V_3_5_FULL`),
  exactly one ticked per type. Build and check the 2.5 mm CODENAME label first, then copy.

### 5.4 Label rows

| Content | Row | Parameter (Material) | Prefix / suffix | Spaces / Break |
|---|---|---|---|---|
| CODE | 1 | `Mark` | — | — |
| CODENAME | 1 | `Mark` | — | Break |
| CODENAME | 2 | `Description` | — | — |
| FULL | 1 | `Mark` | — | Break |
| FULL | 2 | `Description` | — | Break |
| FULL | 3 | `Manufacturer` | — | Spaces 1 |
| FULL | 4 | `Model` | `(` / `)` | — |

A row whose parameter is empty prints nothing, so an uncoded material shows a leader with
no code — visible on the drawing, which is the intent (§5.8 makes the engine count it).

**Optional shared-parameter variant (`SPEC`)**: `MAT_CODE` (758ba3d0-ea41-51fc-8dbf-3bb444174385)
/ `MAT_NAME` (819a8cc6-a552-5649-921f-3b6e494c49d7) / `PROP_FIRE_RATING`
(e9f9cfd6-e086-5eff-93a3-97eb552693a1), all TEXT and all bound to Materials in
`RESOLVED_BINDINGS.csv`. Only after checking in Revit that they appear in this family's
Edit Label list (§5.9, check 1).

### 5.5 Graphics

- **Leader: always.** Callouts point at the face they describe; the drawing engine places
  the head at the face centre and a leader is what makes that readable. Leader Arrowhead
  **Dot Filled 1mm** (or Arrow Open 30, to match the other STING tags).
- **No box.** Text only; the Materials Key legend (§5.7) carries the full list.
- Family origin at the label's left-middle, so leaders land on the text.

### 5.6 Wiring it in (after the `.rfa` exists)

1. Save as `StingTools/Data/TagFamilies/STING - Material Callout Tag.rfa`.
2. Declare it — one `TAG_FAMILY` row with its **own** `LabelMaster` group and category
   `Materials` in `STING_TAG_CONFIG_v5_0_ARCH.csv` (and the `_DesignConstruction` twin),
   exactly like the four above, so Propagate Universal never overwrites it.
3. Add a rule — **AutoTag with `tagFamily`, not `MaterialTag`** (§5.1):

   ```json
   { "category": "Walls", "ruleType": "AutoTag", "enabled": true,
     "tagFamily": "STING - Material Callout Tag" }
   ```

   to `pres-exterior-elev-A1` and `arch-elev-A1-1to100`; add `Roofs` the same way for
   elevations that show them. `DrawingTypeTagFamilyTests` accepts a Materials-declared
   family on a Walls / Roofs rule (a material tag tags a face of any host).
4. Re-stamp checksums, run the tests (as for §1–4).
5. **Expect clutter until §5.8 lands:** every wall gets its own callout. Try it on one
   elevation before rolling it out.

### 5.7 Sections and build-ups — not yet

A compound wall's inner layers are only taggable where they are **cut**, in a section or
detail. That needs the engine to take the view's cut faces, group them by material, and
stack one callout per material in a column — the classic build-up note. Not built; until
it is, tag build-ups by hand with this family (it works on cut faces when you place it
yourself). The detail and screed build-up types already have "Materials Key" / "Materials
Strip" slots for the legend that goes with it.

### 5.8 What would make it robust — recommended next work

In order of value:

1. **`Materials_SyncIdentity`** — one command, Revit-free planner plus thin writer (the
   shape of `Materials_StampCodes`): Mark ← `MAT_CODE`, Description ← `MAT_NAME`, Keynote ←
   `MAT_CODE`; the long paragraph moves to a specification parameter; empty-only unless
   forced. Then **extend `KeynoteSync`** to write one `MAT_CODE → MAT_NAME` row per
   material, so a Keynote-by-Material tag and a keynote legend work from the same code.
   One source of truth, three ways to read it.
2. **Make the `MaterialTag` rule kind real** — always resolve a Material Tags symbol
   (rule `tagFamily`, then pack `tagFamilies["Materials"]`, then the first loaded), treat
   `category` as the host filter, reject `"*"` with a message.
3. **One callout per material** per wall run (or per N metres) instead of per element —
   the difference between a readable elevation and a noisy one.
4. **Paint first**: prefer a painted face (`Document.IsPainted`) and report its paint
   material; **family instances** through symbol geometry (curtain panels, doors, windows);
   **stacked walls** through their members.
5. **QA in the engine**: before placing, read the face's material; if it has no code, place
   anyway (so the gap shows), count it, and warn "N callouts have no MAT_CODE — run
   Materials_SyncIdentity".
6. **Section build-ups** (§5.7) and a data test that every label parameter is a built-in
   or actually bound to Materials — the test that would have caught `STING - Materials Tag`.

Alternatives considered: a **Material Keynote** is the right second route for offices that
keynote (needs item 1 and a loaded keynote table); a **wall or multi-category tag reading a
mirrored parameter** is rejected — one value per element not per face, goes stale, blind
to paint.

### 5.9 Check in Revit before relying on it

1. The exact names in Edit Label → Category Parameters for a Material Tag (`Mark`,
   `Description`, `Manufacturer`, `Model` — and is it `Name` or `Material: Name`?), and
   whether the STING shared parameters bound to Materials appear there.
2. That `IndependentTag.Create` accepts the wall side-face references the engine passes,
   including curtain and stacked walls.
3. That a tag on a painted face reports the paint material.
4. What an existing `STING - Materials Tag` row gated on `TAG_PARA_STATE_*` draws — blank
   or everything. It settles whether that family has any use left.

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
