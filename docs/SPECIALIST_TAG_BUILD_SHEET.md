# Specialist tag families — hand-authoring sheet

Five tag families that the drawing types need and that the Revit API cannot build:
it cannot author label rows. A person builds each one in the Family Editor from this
sheet; everything around the first four is already in place (2026-09-24, merged in
#974). The fifth — `STING - Materials Tag`, rebuilt in place as the material callout —
follows different rules and has its own section (§5): read it before building it.

| Family | Tags | For drawing type |
|---|---|---|
| `STING - Fire Door Tag` | Doors | `arch-fire-strategy-A1-1to100` |
| `STING - Accessible Door Tag` | Doors | `arch-accessibility-A1-1to100` |
| `STING - Room Finish Tag` | Rooms | `arch-floor-finishes-A1-1to100` |
| `STING - Fire Compartment Tag` | Rooms | `arch-fire-strategy-A1-1to100` |
| `STING - Materials Tag` (rebuilt in place) | **Material Tags** (a face of any host) | `pres-exterior-elev-A1`, `arch-elev-A1-1to100`; sections later (§5.7) |

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

## 5. `STING - Materials Tag` (Material Tags) — rebuilt in place

Prints e.g. `CLG-001` / `Gypsum board standard 12.5 mm` with a leader to the face it
describes — the callout on an elevation, and the build-up note in a section (§5.7).

**Rebuilt in place, not replaced (2026-09-24).** The library already ships
`STING - Materials Tag.rfa`; it carried the universal label, and **none of its 68 fields can
appear on a material** (§5.1). It keeps its name: the engine's fallback, the tag-family
loader and the content manifest all find it by that name, and reloading the rebuilt family
into an existing project replaces the broken label there too. A second family would have
left the broken one behind in every project that has it. Its spec in the repo is already
rebuilt — `LABEL_DEFINITIONS.json` (`category_labels.Materials`) and the four v5.0 config
blocks now carry the four rows below — and it is declared `LabelMaster: MaterialsTag`, so
Propagate Universal never puts the universal label back. What is left is the `.rfa`.

**This one is different from §1–4 in three ways, and each one decides how it is built:**

1. **A material tag reads the MATERIAL, never the element.** Its label can show only the
   material's own parameters — built-in identity data and shared parameters bound to the
   **Materials** category. Nothing on the wall (`ASS_TAG_1_TXT`, type marks, the `ASS_*`
   family) is visible to it, and `<ALL>` bindings never reach a material.
   `MaterialTagLabelTests` fails the build on a row that breaks this.
2. **It tags a FACE.** Revit places a material tag on a face reference, and the tag reports
   that face's material — on a compound wall's exterior face, the outer layer only. The
   drawing engine does this (§5.1).
3. **No tiers.** The `TAG_PARA_STATE_n_BOOL` gates are read from the tagged element's
   *type* (`Core/TierGateScope.cs`); a Material has no type. **Strip every tier row and every
   tier gate out of the existing family.** Variants are family **types** (§5.3).

### 5.1 What exists today

| Thing | State | Evidence |
|---|---|---|
| Shared parameters **on Materials** | ✅ Works. `CleanMaterialBindings` adds the Materials category to every `MAT_*` / `PROP_*` / `BLE_MAT_*` / `COMP_MAT_*` / `STING_MAT_*` parameter; a 2026-09-21 run logged 116 added, 0 failed. The comments in `LoadSharedParamsCommand.cs`, `SharedParamGuids.cs` and `ParamRegistry.cs` that said Materials cannot take bound parameters have been corrected (the last on 2026-09-25). | `Tags/LoadSharedParamsCommand.cs` `CleanMaterialBindings` |
| Parameters bound `<ALL>` | ❌ Never reach a Material (`ASS_TAG_1_TXT`, the cost / carbon / tier parameters). | `RESOLVED_BINDINGS.csv` |
| Material identity | ✅ `Materials_SyncIdentity` (§5.2) makes Mark, Keynote, Description, `MAT_CODE` and `MAT_NAME` agree. Before it, only materials STING created carried any of them. | `Commands/Materials/SyncMaterialIdentityCommand.cs` |
| The spec of `STING - Materials Tag` | ✅ Rebuilt: `MAT_CODE` / `MAT_NAME` / `MAT_MANUFACTURER` / `MAT_STANDARD`, all bound to Materials, no tiers, no warning rows, own `LabelMaster` group. The universal label it had (68 fields, 0 readable on a material) is gone from `LABEL_DEFINITIONS.json` and from the four v5.0 config blocks. | `MaterialTagLabelTests`, `UniversalOptOutTests` |
| The `.rfa` of `STING - Materials Tag` | ❌ **Still the universal label** until someone rebuilds it from this section. | `StingTools/Data/TagFamilies/STING - Materials Tag.rfa` |
| Engine: tag a face | ✅ (2026-09-24) Hosts through their finish faces; curtain walls through their **panels**, stacked walls through their **members**, family instances through their own geometry (instance, else symbol). A **painted** face towards the viewer wins and reports the paint material. | `AnnotationRunner.MaterialCallouts.cs` `FaceCandidates` |
| Engine: the `MaterialTag` rule kind | ✅ (2026-09-24) Resolves a **Material Tags** family — the rule's `tagFamily`, else `tagFamilies["Materials"]`, else the first loaded — with `category` as the hosts to tag (`"*"` = walls, floors, roofs, ceilings). **One callout per material** within 80 mm on paper; callouts already on the view count, so a re-run adds nothing. Callouts on materials with no code are placed, counted and warned about. | `AnnotationRunner.MaterialCallouts.cs`, `MaterialCalloutPlan` |
| Engine: `MaterialTagLayers` | ✅ (2026-09-24) Sections and details only: each cut host's cut faces grouped by material, one callout per material, heads stacked in a column beside the element. A host already carrying a material tag in the view is skipped. | same |
| Drawing types | ✅ `MaterialTag` on walls: `pres-exterior-elev-A1` (and roofs), `arch-elev-A1-1to100`, `arch-interior-elev-A1-1to50`. `MaterialTagLayers` on walls / floors / roofs: `arch-section-A1-1to50`, `arch-detail-A3-1to20`; floors: `arch-screed-buildup-A3-1to10`. All six name `tagFamilies["Materials"] = "STING - Materials Tag"`. | `STING_DRAWING_TYPES.json` |

### 5.2 Data contract — do this on the project first

The label prints shared parameters bound to Materials, so run **Load Shared Parameters**
(it binds `MAT_*` to Materials), then **`Materials_SyncIdentity`** (SETUP → Model Baseline,
"Sync material identity"). For every coded material it fills:

| Field | Gets | Why |
|---|---|---|
| `MAT_CODE` (shared) | the register code, where empty | row 1 of the tag |
| `MAT_NAME` (shared) | the short register name, where empty | row 2 of the tag |
| Mark, Keynote (built-in) | the code | Keynote-by-Material tags and schedules read the same code |
| Description (built-in) | the short name; STING's long paragraph moves to `MAT_SPECIFICATIONS` | readable in the Material Browser and schedules |

It writes the plan to CSV first and never replaces a value someone typed unless you pick
*Overwrite*. Then run **Keynote Sync**: the keynote table carries one `code → name` row
per material under a `MAT` heading. `MAT_MANUFACTURER` / `MAT_STANDARD` come from the
register when STING creates the material; type them for materials it did not.

### 5.3 Template, category, types

- **Start from the existing `STING - Materials Tag.rfa`** (category Material Tags is
  already right). Delete every label row, every calculated value, and the
  `TAG_PARA_STATE_*` / `TAG_WARN_VISIBLE_BOOL` / `TAG_DEPTH_TIER_INT` family parameters —
  none of them can resolve on a material. Or start clean from `Material Tag.rft` and save
  over it under the **same name**.
- **Types — content × size, named to the tag-style convention** so the engine switches
  size and keeps content (`{size}_{CONTENT}`):

  | Type | Rows | Use |
  |---|---|---|
  | `2_CODE` · `2.5_CODE` · `3.5_CODE` | `MAT_CODE` | Dense elevations and sections with a Materials Key beside them |
  | `2_CODENAME` · **`2.5_CODENAME`** · `3.5_CODENAME` | `MAT_CODE` / `MAT_NAME` | **Default.** Elevations, build-ups, presentation |
  | `2_FULL` · `2.5_FULL` · `3.5_FULL` | `MAT_CODE` / `MAT_NAME` / `MAT_MANUFACTURER` (`MAT_STANDARD`) | Specification elevations, sample boards |

  Nine types. The engine reads the size from the text before the first `_` and matches
  the rest, so a drawing that wants 2 mm turns `2.5_CODENAME` into `2_CODENAME`.
- **Labels:** nine label copies at the same point — one per type — each at its text size,
  each with *Visible* tied to its own family Yes/No parameter (`V_2_CODE` … `V_3_5_FULL`),
  exactly one ticked per type. Build and check the 2.5 mm CODENAME label first, then copy.

### 5.4 Label rows

| Content | Row | Parameter (bound to Materials) | GUID | Prefix / suffix | Spaces / Break |
|---|---|---|---|---|---|
| CODE | 1 | `MAT_CODE` | 758ba3d0-ea41-51fc-8dbf-3bb444174385 | — | — |
| CODENAME | 1 | `MAT_CODE` | 〃 | — | Break |
| CODENAME | 2 | `MAT_NAME` | 819a8cc6-a552-5649-921f-3b6e494c49d7 | — | — |
| FULL | 1 | `MAT_CODE` | 〃 | — | Break |
| FULL | 2 | `MAT_NAME` | 〃 | — | Break |
| FULL | 3 | `MAT_MANUFACTURER` | 66307eed-372a-5804-9c95-6866cceb503a | — | Spaces 1 |
| FULL | 4 | `MAT_STANDARD` | f7b7a311-f04b-5859-998e-e88295f4badc | `(` / `)` | — |

Add them through *Edit Label → Add Parameter → Select…* with `MR_PARAMETERS.txt` as the
shared parameter file. A row whose parameter is empty prints nothing, so an uncoded
material shows a leader with no code — visible on the drawing, which is the intent (the
engine also counts it and names the material in its warning).

**If the shared parameters are not in a project** (it never ran Load Shared Parameters),
the equivalent built-ins are `Mark` / `Description` / `Manufacturer` / `Model` —
`Materials_SyncIdentity` fills Mark and Description with the same values. Do not mix the
two in one family: pick the shared rows, which is what the spec and the test hold.

### 5.5 Graphics

- **Leader: always.** Callouts point at the face they describe; the drawing engine places
  the head beside the face and a leader is what makes that readable. Leader Arrowhead
  **Dot Filled 1mm** (or Arrow Open 30, to match the other STING tags).
- **No box.** Text only; the Materials Key legend (§5.7) carries the full list.
- Family origin at the label's left-middle, so leaders land on the text.

### 5.6 Wiring it in (after the `.rfa` is rebuilt)

1. Save over `StingTools/Data/TagFamilies/STING - Materials Tag.rfa` — **same name**.
2. Nothing else in the repo: the spec, the `LabelMaster` declaration and the six drawing
   types' `tagFamilies["Materials"]` are already in place.
3. In each project: reload the family (Insert → Load Family, **Overwrite the existing
   version and its parameter values**), then Load Shared Parameters and
   `Materials_SyncIdentity` if not done (§5.2).
4. Produce one elevation and one section on a model with coded materials and check §5.9.

### 5.7 Sections and build-ups

`MaterialTagLayers` does the classic build-up note: in a section or detail it takes each
host's **cut** faces, keeps one per material (the largest), and places one callout per
material with its head in a column to the right of the element, ordered across the
element so the column reads outside-in. It needs the same family; the `CODE` types are the
ones to use at 1:20 and denser with the Materials Key legend (the detail and screed
build-up types already have "Materials Key" / "Materials Strip" slots). A host that
already carries a material tag in the view is skipped, so re-running adds nothing — to
redo one, delete its callouts first.

### 5.8 What is built, and what is still open

**Built (2026-09-24):**

1. **`Materials_SyncIdentity`** — Mark / Keynote ← code, Description ← short name,
   paragraph → `MAT_SPECIFICATIONS`, `MAT_CODE` from the register; `MaterialIdentityPlanner`
   is Revit-free and measured against the whole shipped register. **Keynote Sync** writes one
   row per material — and its existing rows were fixed: every one was `key<TAB><TAB>name`,
   which puts the name in the PARENT column and leaves the text blank.
2. **`MaterialTag` rule kind** — resolves a Material Tags family; `category` is the hosts.
3. **One callout per material** within 80 mm on paper (`MaterialCalloutPlan.Thin`).
4. **Paint first; curtain panels, stacked members, family instances.**
5. **No-code QA** — placed, counted, named in the warning, pointing at SyncIdentity.
6. **`MaterialTagLayers`** — build-up callouts on cut faces.

**Still open:** a data test that every label parameter of a material tag family is a
built-in or bound to Materials (it would have caught `STING - Materials Tag`); retiring or
rebuilding that universal-label family; and the Revit run in §5.9 — every Revit-side piece
above is unverified (face references on panels and instances, cut-face references, paint
read-back, head placement).

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
5. `MaterialTagLayers` in a wall section: one callout per layer, leaders landing on the
   right layer, heads in a tidy column; re-run adds nothing.
6. An elevation: one callout per material (not per wall), a re-run adds nothing, and a
   material with no code shows a blank callout plus the warning naming it.
7. `Materials_SyncIdentity` on an existing project: the CSV before, then Mark / Keynote /
   Description after; a hand-typed Mark left alone.

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
