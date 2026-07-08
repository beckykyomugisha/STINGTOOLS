# Parameter → Category Binding — Domain Map (SIGN-OFF DRAFT)

**Purpose.** The authoritative, complete map of *which shared parameters bind to which Revit
categories*, keyed by parameter **domain** (prefix + sub-prefix), covering all **3,390** params —
not the ~1,588 (47%) that happen to have a hand-written `CATEGORY_BINDINGS.csv` row.

**The rule that fixes the leak:** a parameter binds **only** to its domain's category set. **Anything
that does not resolve to a domain is left UNBOUND** (reported, never broad-bound). No `AllCategoryEnums`
fallback. "Universal" shrinks to the genuinely-universal few.

> Legend for category sets is in §1; the mapping is §2; judgment calls needing your confirmation are
> flagged **⚑** and collected in §4.

---

## 1. Named category sets (exact Revit `OST_` members)

| Set | Categories |
|---|---|
| **HVAC** | MechanicalEquipment, DuctTerminal, DuctCurves, DuctFitting, DuctAccessory, DuctInsulations, FlexDuctCurves |
| **HVAC_TERMINAL** | DuctTerminal *(Air Terminals only)* |
| **PLUMBING** | PipeCurves, PipeFitting, PipeAccessory, FlexPipeCurves, PipeInsulations, PlumbingFixtures |
| **FIRE_SUPPR** | Sprinklers, PipeCurves *(wet)*, FireAlarmDevices |
| **ELECTRICAL** | ElectricalEquipment, ElectricalFixtures, CableTray, CableTrayFitting, Conduit, ConduitFitting, ElectricalCircuit |
| **LIGHTING** | LightingFixtures, LightingDevices |
| **DATA_COMMS** | DataDevices, CommunicationDevices, TelephoneDevices, SecurityDevices, NurseCallDevices |
| **STRUCTURAL** | StructuralFraming, StructuralColumns, StructuralFoundation, Rebar, Floors *(slabs)* |
| **ARCH_DOOR** | Doors · **ARCH_WINDOW** Windows · **ARCH_WALL** Walls, CurtainWallPanels, CurtainWallMullions · **ARCH_FLOOR** Floors · **ARCH_CEILING** Ceilings · **ARCH_ROOF** Roofs · **ARCH_STAIR** Stairs, StairsRailing · **ARCH_RAMP** Ramps · **ARCH_RAILING** Railings, StairsRailing · **ARCH_CASEWORK** Casework · **ARCH_FURNITURE** Furniture, FurnitureSystems · **ARCH_PARKING** Parking · **ARCH_COLUMN** Columns, StructuralColumns · **ARCH_ROOM** Rooms |
| **FINISHES** | Walls, Floors, Ceilings, Roofs, Rooms *(surface-finish params: tile/paint/plaster/mortar/brick/block)* |
| **MATERIAL** | Materials *(via `CleanMaterialBindings` — `BLE_APP-*`, `BLE_MAT_*`, `MAT_*`)* |
| **HEALTHCARE** | SpecialityEquipment, MechanicalEquipment *(MedGas)*, PlumbingFixtures *(clinical)* |
| **ANNOTATION_TAGS** | *(tag families only — NOT bound to model elements)* |
| **SHEETS** | Sheets, TitleBlocks |
| **UNIVERSAL** | every taggable model category *(the current core set)* — reserved for identity/tag/IFC/status ONLY |

---

## 2. Domain → category set (the full map)

| Domain (prefix / sub) | # | → Category set | Note |
|---|---|---|---|
| `HVC_` (general) | ~90 | **HVAC** | |
| `HVC_TERMINAL_*` | ~5 | **HVAC_TERMINAL** | the leak we already fixed — keep scoped |
| `PLM_` | 181 | **PLUMBING** | |
| `ELC_` | 249 | **ELECTRICAL** | (circuits only via ELC power sub) |
| `LTG_` | 26 | **LIGHTING** | |
| `ICT_` `COM_` | 45 | **DATA_COMMS** | |
| `FLS_` | 51 | **FIRE_SUPPR** | ⚑ some FLS are arch fire-rating → see §4 |
| `STR_` | 148 | **STRUCTURAL** | |
| `MEP_` | 20 | **HVAC ∪ PLUMBING ∪ ELECTRICAL** | generic MEP |
| `MGS_` (WARN_MGS too) | ~15 | **HEALTHCARE** (MedGas) | |
| `CLN_` `CEQ_` `RAD_` | ~40 | **HEALTHCARE** | |
| `BLE_DOOR_*` | 21 | **ARCH_DOOR** | |
| `BLE_WINDOW_*` | 17 | **ARCH_WINDOW** | |
| `BLE_WALL_/FACADE_/CW_/PANEL_/MULLION_*` | ~30 | **ARCH_WALL** | |
| `BLE_FLR_/FLOOR_/SLAB_*` | ~27 | **ARCH_FLOOR** | |
| `BLE_CEILING_/CEIL_*` | ~16 | **ARCH_CEILING** | |
| `BLE_ROOF_*` | 20 | **ARCH_ROOF** | |
| `BLE_STAIR_*` | 14 | **ARCH_STAIR** | |
| `BLE_RAMP_*` | 9 | **ARCH_RAMP** | |
| `BLE_RAILING_/RAIL_*` | 6 | **ARCH_RAILING** | |
| `BLE_CASEWORK_*` | 7 | **ARCH_CASEWORK** | |
| `BLE_FURN_/FURNITURE_*` | 4 | **ARCH_FURNITURE** | |
| `BLE_PARK_/PARKING_*` | 8 | **ARCH_PARKING** | |
| `BLE_COLUMN_*` | 1 | **ARCH_COLUMN** | |
| `BLE_ROOM_/HEADROOM_*` | 10 | **ARCH_ROOM** | |
| `BLE_STRUCT_*` | 21 | **STRUCTURAL** | |
| `BLE_FINISH_/TILE_/PAINT_/PLASTER_/MORTAR_/BRICK_/BLOCK_/SURFACE_*` | ~24 | **FINISHES** | |
| `BLE_MAT_/MATERIAL_*` + `BLE_APP-*` | ~75 | **MATERIAL** | already handled by material path |
| `BLE_ELE_/ELES_/CBL_*` | 10 | **ELECTRICAL** | ⚑ confirm — electrical-in-BLE |
| `BLE_SIGN_*` | 2 | GenericModel | signage |
| `BLE_LOAD_/LIVE_*` | 2 | **STRUCTURAL** | loads |
| `MAT_` | 55 | **MATERIAL** | |
| `Qto_` | 59 | *(host element of the quantity)* | ⚑ see §4 |
| `CST_CALC_*` | 23 | **FINISHES ∪ STRUCTURAL** *(material takeoff)* | **NOT MEP** — the flood you saw |
| `CST_S_*` | 75 | **STRUCTURAL** | concrete/structural QTO |
| `CST_` rollup (UNIT/TOTAL/RATE/SUP/LABOUR/BOQ/DUTY/FX/UG/INTL/PROC/INSTALL/FORMWORK/EMBODIED) | ~86 | **UNIVERSAL** ⚑ | cost rollup applies to any billed element — see §4 |
| `WARN_<X>_*` | 567 | **same set as `<X>`** | mirror rule (WARN_HVC→HVAC, WARN_BLE_DOOR→ARCH_DOOR, WARN_ASS→UNIVERSAL) |
| `ASS_TAG_*`, `ASS_DISCIPLINE/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ`, `ASS_STATUS`, `ASS_DISPLAY`, identity | ~120 | **UNIVERSAL** | the true universals |
| `ASS_FAB_*` | ~10 | **HVAC ∪ PLUMBING ∪ STRUCTURAL** *(fabricated)* | ⚑ |
| `ASS_` other (cost/commissioning/asbuilt on the asset) | ~110 | **UNIVERSAL** | |
| `IFC_` | (in ASS/EXCH) | **UNIVERSAL** | interop ids |
| `PER_` (sustainability) | 92 | **UNIVERSAL** ⚑ | per-element carbon/life — see §4 |
| `RGL_` (regulatory) | 67 | **UNIVERSAL** ⚑ | compliance metadata |
| `PRJ_` | 116 | **ProjectInformation** *(+ UNIVERSAL for a few)* | ⚑ mostly project-level |
| `TAG_` (PARA_STATE / style / box / leader / depth) | 171 | **ANNOTATION_TAGS** | **do NOT bind to model elements** |
| `STING_` (gate/cluster/display/pos/stale) | 99 | **UNIVERSAL** *(model)* + some annotation | ⚑ split |
| `TB_` `TBL_` `SHT_`/`STING_DRAWING` | ~60 | **SHEETS** | |
| `VT_` | 19 | Views *(non-instance)* | ⚑ view params, not element |
| `SLV_` `INS_` `SITE_` `GEN_` | ~30 | ⚑ see §4 | |

---

## 3. The binding-safety rules (the actual fix)

1. **Resolve → bind, else SKIP.** Every param resolves through §2. If it resolves to a set → bind to
   exactly that set. **If it resolves to nothing → leave UNBOUND and list it** (a coverage gap to fix,
   never a broad bind).
2. **`UNIVERSAL` is the ONLY set that spans all categories** — and only identity/tag/IFC/status/cost-rollup
   use it. Everything else is a scoped set.
3. **`ANNOTATION_TAGS` params never bind to model elements** — they live on tag families.
4. **Prune unbinds** anything currently bound outside its resolved set (that clears the door/facade/cost
   leak from existing models), guarded by the data-loss pre-flight already added.

---

## 4. Judgment calls — need your sign-off ⚑

1. **`CST_` cost rollup (UNIT/TOTAL/RATE/…, ~86)** — UNIVERSAL (any billed element) or scoped to
   costable disciplines only? I lean **UNIVERSAL** (you cost everything), while `CST_CALC_*` and
   `CST_S_*` stay scoped (arch/struct takeoff). Confirm.
2. **`PER_` sustainability (92)** — carbon/embodied/recyclability. UNIVERSAL (every element has a carbon
   figure) or per-element? I lean **UNIVERSAL**.
3. **`RGL_` regulatory (67)** — compliance metadata. UNIVERSAL or per-discipline? I lean **UNIVERSAL**.
4. **`Qto_` (59)** — Revit's own Qto quantity params; each belongs on the element that owns that quantity.
   Map by quantity name, or leave to Revit's native binding and **exclude from STING binding**? I lean
   **exclude** (Revit manages Qto).
5. **`FLS_` (51)** — fire life-safety: some are device params (→ FIRE_SUPPR), some are arch fire-rating
   (→ walls/doors). Split by sub-prefix — confirm that's wanted.
6. **`ASS_FAB_*` / fabrication** — bind to fabricated disciplines (HVAC/PLUMB/STRUCT) only, not universal.
7. **`VT_` (19), `PRJ_` (116)** — view/project-level params: bind to Views / ProjectInformation, not model
   instances. Confirm exclude-from-element-binding.
8. **`SLV_` `INS_` `SITE_` `GEN_` (~30)** — need a quick domain call each (sleeves→penetration hosts,
   insulation→ducts/pipes, site→site/topography, generic→?).

---

## 5. What I'll build once you sign off

- A `ParamDomainResolver` (prefix + sub-prefix + WARN-mirror rules → named category set), **complete for
  all 3,390**, replacing the CSV-row-plus-broad-fallback.
- Binder change: resolve → bind-to-set **or skip+report**; drop `coreBinding` fallback; shrink
  `UniversalGroups` to identity/tag/IFC/status/(cost-rollup pending #1).
- Prune change: **unbind** anything outside the resolved set (clears the existing leak).
- A **full dry-run audit**: every param → its resolved set, written to a report you review **before any
  model is touched**. No live change until that reads clean.
