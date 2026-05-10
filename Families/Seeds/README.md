# STING Seed Families — Author's Manual-Finishing Guide

This folder hosts the `.rfa` files that **`BuildSeedFamiliesCommand`** auto-generates from the JSON specs in `StingTools/Data/Seeds/`. The auto-generator gives you:

- The right `.rft` template (face-based / wall-based / ceiling-based / standalone) for each category
- Every shared parameter the STING parameter scheme expects, with stable GUIDs
- `STING_SEED_FAMILY_TXT` stamped on every type variant
- A minimal 2D plan symbol drawn from the JSON's geometry block
- A 3D bounding-box solid sized from the JSON's `solid3D` block
- MEP connectors at the positions declared in the JSON's `connectors` block (panels + mechanical equipment only)

What it does **not** give you — and what this guide walks through — is **visual polish**: the 2D symbols that read at 1:50 and 1:100 plan scales, the section/elevation symbology, the 3D representation that shows convincingly in coordination views, and the type variants that distinguish FR60 from FR120 or FLUSH from RECESSED. None of that is hard. None of it requires programming. It's about an hour of Family Editor work per seed.

## Workflow per seed

1. **Run** `BuildSeedFamiliesCommand` (the *Build Seed Families* button in the dock panel) — produces `STING_SEED_<Name>.rfa` in `<project>/_BIM_COORD/Families/Seeds/` and loads it into the active project.
2. **Open** the resulting `.rfa` in Revit Family Editor. Right-click the family in the Project Browser → *Edit Family*.
3. **Verify the parameters arrived** — Family Manager (Family Properties → *Family Types*) should show every `STING_*`, `ASS_*`, and discipline parameter from the JSON spec. If any are missing, the parameter binding is the issue, not the family — check that the project has a shared parameter file loaded.
4. **Polish the 2D symbols** per the per-seed sections below.
5. **Polish the 3D representation** — replace the auto-generated bounding box with something that reads as the right object class in 3D views.
6. **Add type variants** — duplicate the seed type (Family Types dialog → *New Type*) for every variant the swap registry references. The variant names matter (see per-seed sections).
7. **Save** (`File → Save`) — overwrites the auto-generated `.rfa`. **Reload** into the project (the *Edit Family* dialog asks; choose *Load into Project and Close*).
8. **Test** — place an instance, tag it, schedule it, then run *Swap to Manufacturer* with a real family loaded to verify parameters carry over.

> **Re-running `BuildSeedFamiliesCommand` overwrites your `.rfa`.** Once you've polished a seed, do not re-run unless you've also updated the JSON spec — your visual work would be lost. Ship polished `.rfa` files via the corporate baseline at `Families/Seeds/` (this folder); keep auto-generated copies under `_BIM_COORD/Families/Seeds/` as a project-scoped fallback.

## Common subcategory pattern

Every seed should declare a single `STING_SEED` subcategory in Family Editor (Manage → Object Styles → Sub-objects → New). Set the line weights:

- **Plan / Section**: weight 3 default; weight 4 for primary symbols
- **Cut**: weight 5 for cut symbology

This makes view-template control trivial — one VG override line drives every seed family in the project.

---

## STING_SEED_LightingFixture (Lighting Fixtures)

**Hosting:** Ceiling-based · **Template:** `Metric Lighting Fixture ceiling based.rft` · **Symbol size at 1:100:** 6 mm

### 2D plan symbol
- 600×600 mm rectangle (auto-generated outline is correct size; relocate to family origin if needed).
- Diagonal `X` from corner to corner — auto-generated.
- Add a small filled circle (3 mm) at centre — emergency luminaires can fill this red via type variants.
- Subcategory: `STING_SEED`.

### 3D representation
- Replace the auto 100 mm box with a flush-fit recessed plate: 595×595×25 mm panel face + 100×100×90 mm centre boss for the housing.
- Material: a generic `STING_LumPlate` material (white) — manufacturer swap will replace.

### Type variants (matches swap registry priority order)
1. `RECESSED_LED_600x600` ← seed default
2. `DOWNLIGHT` — keep 600×600 outline, set `LTG_TYPE_TXT = "DOWNLIGHT"`
3. `PENDANT` — `LTG_TYPE_TXT = "PENDANT"`, raise the 3D mass 200 mm above origin
4. `LINEAR_LED` — change 2D rectangle to 1200×100 mm, `LTG_TYPE_TXT = "LINEAR_LED"`
5. `EMERGENCY` — `LTG_TYPE_TXT = "EMERGENCY"`, fill the centre circle red

### Parameters to populate per type
- `ELC_PHOTO_LUMENS` — 4000 (recessed), 1200 (downlight), 6000 (linear), 200 (emergency)
- `ELC_PHOTO_WATTS` — 32, 12, 50, 4 respectively

---

## STING_SEED_ElectricalFixture (Electrical Fixtures)

**Hosting:** Face-based · **Template:** `Metric Electrical Fixture face based.rft` · **Symbol size at 1:100:** 4 mm

### 2D plan symbol
- Auto-generated 2-gang socket symbol (rectangle + 4 short verticals) — keep for `SOCKET_2G`.
- Add a 1100 mm mounting-height label referencing `ELE_FIX_MOUNT_HEIGHT_MM` so the symbol reports mounting height without a tag.
- Subcategory: `STING_SEED`.

### 3D representation
- 150×35×85 mm flush plate, projecting 5 mm proud of the wall face.

### Type variants
1. `SOCKET_2G` ← default
2. `SOCKET_1G` — 75×85 mm plate, 2 verticals
3. `SWITCH_2G` — 150×85 plate, 2 horizontal slits
4. `DATA_OUTLET_2G` — 150×85 plate with 2 small `D` letters
5. `FLOOR_BOX` — 200×200 plate, mounting height 0
6. `FCU` — 150×85 plate with a 20 mm circle (fuse indicator)

---

## STING_SEED_ElectricalEquipment (Electrical Equipment)

**Hosting:** Standalone · **Template:** `Metric Electrical Fixture.rft` · **Symbol size at 1:100:** 8 mm  
**Connectors:** Top + bottom MEP connectors auto-generated

### 2D plan symbol
- Auto-generated rectangle with horizontal divider — reads as a wall-mounted DB. Keep.
- Add a label `H = 600 mm` so plan reading shows floor-to-top dimension.
- Subcategory: `STING_SEED`.

### 3D representation
- Replace auto box with a 400×200×600 mm (W×D×H) panel enclosure.
- Add a small handle (40×10×15 mm cuboid) on the right edge — door direction obvious.

### Type variants
1. `DISTRIBUTION_BOARD_DB` ← default
2. `MAIN_SWITCHBOARD_MSB` — 800×400×1800 mm (floor-mounted), connectors at top only
3. `CONSUMER_UNIT` — 300×90×250 mm (small wall box)
4. `ISOLATOR` — 150×100×200 mm
5. `JUNCTION_BOX` — 100×100×50 mm, no door
6. `TRANSFORMER` — 600×400×800 mm

### Connector polish (critical)
The JSON declares two connectors at z=±0.5 (top + bottom of the 3D box). In Family Editor, **verify** that the connectors land on the right faces — if the 3D box doesn't extrude up/down, connectors hang in mid-air. Fix: select each connector, drag onto the relevant face reference.

Set connector domain *Electrical*, classification *Power* on the supply side, *Power - Balanced* (or *Power - Unbalanced* for single-phase) on the load side.

---

## STING_SEED_FireAlarmDevice (Fire Alarm Devices)

**Hosting:** Face-based · **Template:** `Metric Fire Alarm Device.rft` · **Symbol size at 1:100:** 5 mm

### 2D plan symbol
- Auto-generated circle with internal `X` — reads as a smoke detector. Keep for `SMOKE_OPTICAL`.
- Subcategory: `STING_SEED`.

### 3D representation
- Replace auto box with a 110×110×50 mm cylindrical detector housing (use a *revolve*, not extrusion, for a circular profile).

### Type variants (matches BS EN 54 device-type classification)
1. `SMOKE_OPTICAL` ← default (auto symbol)
2. `HEAT_DETECTOR` — replace the X with `H` text
3. `MULTI_SENSOR` — circle + small triangle inside
4. `CALL_POINT_MCP` — square 100×100, hatched fill
5. `SOUNDER_BEACON_VAD` — circle with three radiating lines (horn icon)
6. `BEAM_DETECTOR` — rectangle with 2 small circles at the ends

### Parameters per type
- `FLS_DEV_TYPE_TXT` matches variant name
- `FLS_DEV_LOOP_TXT` — `L1`–`L8` for typical addressable systems
- `FLS_DEV_CERT_TXT` — `BS EN 54-7` (smoke), `BS EN 54-5` (heat), `BS EN 54-3` (sounder), etc.

---

## STING_SEED_SpecialityEquipment (Specialty Equipment) — FRP Penetrations

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 5 mm  
**The most important seed — drives the auto-generated Penetration Register.**

> **Phase 178d** — the placer now hosts on **floors, walls, and structural beams**.
> `WallPenetrationDetector` covers fire-rated compartment walls (BS 9999 / Approved Document B).
> `BeamPenetrationDetector` reads beam material (steel / concrete) and writes
> `PEN_BEAM_OFFSET_PCT` (% of span from nearest support) and
> `PEN_BEAM_DEPTH_RATIO` (member-OD ÷ beam-depth), classifying every crossing as
> `STRUCT_OK` / `STRUCT_REVIEW` / `STRUCT_FAIL` per AISC Design Guide 2 + BS EN 1992.
> Idempotence is keyed off `PEN_PFV_UUID_TXT` (UUIDv5 of host + member) — re-runs
> update existing instances instead of duplicating. The same UUID is shared with
> `SleeveEngine`, so a sleeve and an FRP for the same physical hole pair up.

### 2D plan symbol
- Concentric circles (Ø500 outer, Ø350 inner — auto-generated) with four short tick lines crossing the gap at 0°/90°/180°/270°. Reads as a sleeve-through.
- For section view, add a 200 mm vertical bar with horizontal arrows pointing in/out — represents the seal extending through the host.
- Subcategory: `STING_SEED`.

### 3D representation
- Replace auto box with a conical sleeve: 80×80 mm at top, narrowing to 60×60 mm at bottom (concrete-cover allowance), 200 mm tall.
- Also add a 2D line at the soffit reference plane for section visibility.

### Type variants (matches FrpPenetrationPlacer rating index)
1. `FR30` — `PEN_FIRE_RATING_TXT = "FR30"`
2. `FR60` — `PEN_FIRE_RATING_TXT = "FR60"` ← most common
3. `FR90` — `PEN_FIRE_RATING_TXT = "FR90"`
4. `FR120` — `PEN_FIRE_RATING_TXT = "FR120"`
5. `SLEEVE_GENERIC` — `PEN_FIRE_RATING_TXT = ""` (non-fire-rated, e.g. service sleeves)

### Critical: parameter wiring
`PEN_CONTROL_NUMBER_TXT` should display in the type's `Mark` parameter so it shows on tags by default. Add a formula: `Mark = PEN_CONTROL_NUMBER_TXT`. Every fire-stop then appears in tag schedules without extra wiring.

---

## STING_SEED_PlumbingFixture (Plumbing Fixtures)

**Hosting:** Face-based · **Template:** `Metric Plumbing Fixture face based.rft` · **Symbol size at 1:100:** 6 mm

> **Phase 178d** — the seed now declares three connectors at the symbol level
> (DCW + DHW supply + Sanitary outlet) so `AutoPipeDrop` can wire each fixture
> into all three services in one pass. WC and urinal variants don't *need* the
> DHW connector but inherit it as a no-op cap; SwapToManufacturer replaces the
> seed with the real product whose connector set is fixture-correct.

### 2D plan symbol
- Auto-generated hexagonal-ish outline reads as generic sanitary fixture. Add a small dot at the connection point (typically centre-back).

### 3D representation
- 400×600×400 mm (W×D×H) bounding box — respectable WC volume. Override per type variant.

### Type variants
1. `WC` ← default
2. `BASIN` — 400×350×150 mm
3. `URINAL` — 400×350×600 mm
4. `SHOWER` — 900×900×100 mm (tray)
5. `SINK` — 600×450×200 mm

---

## STING_SEED_AirTerminal (Air Terminals)

**Hosting:** Ceiling-based · **Template:** `Metric Air Terminal.rft` (often pre-installed) or `Metric Generic Model ceiling based.rft` · **Symbol size at 1:100:** 6 mm

### 2D plan symbol
- Auto-generated 595×595 mm tile with `+` and `X` overlay reads as a square diffuser. Keep for `SUPPLY_DIFFUSER_SQ`.

### 3D representation
- 595×595×60 mm shallow ceiling-tile profile. The auto box is correct.

### Type variants
1. `SUPPLY_DIFFUSER_SQ` ← default
2. `SLOT_DIFFUSER` — 2D to 1200×100 mm slot, 3D to 1200×100×60 mm
3. `SUPPLY_GRILLE` — rectangle with horizontal slats
4. `EXTRACT_GRILLE` — same outline, single arrow inward
5. `LOUVRE` — outdoor, larger 600×300 mm

---

## STING_SEED_MechanicalEquipment (Mechanical Equipment)

**Hosting:** Standalone · **Template:** `Metric Mechanical Equipment.rft` · **Symbol size at 1:100:** 12 mm  
**Connectors:** Left + right HVAC connectors auto-generated

### 2D plan symbol
- Auto-generated rectangle with vertical centre line — reads as typical AHU. Keep for `AHU`.
- Add labels at connector ends: `S` (supply) on left, `R` (return) on right.

### 3D representation
- 1200×600×800 mm (W×D×H) is a small AHU — scale per type.

### Type variants
1. `AHU` ← default
2. `FCU` — 800×400×250 mm (above-ceiling unit)
3. `CHILLER` — 2400×1200×1800 mm, connectors moved to top
4. `BOILER` — 800×600×1200 mm, connector classification *Hydronic*
5. `PUMP` — 400×200×350 mm, two side-mounted connectors

### Connector classifications
- AHU/FCU: domain *HVAC*, classification *Supply Air* / *Return Air*
- Chiller/boiler/pump: domain *Piping*, classification *Hydronic Supply* / *Hydronic Return*

---

## STING_SEED_Sprinkler (Sprinklers)

**Hosting:** Ceiling-based · **Template:** `Metric Sprinkler.rft` · **Symbol size at 1:100:** 4 mm

### 2D plan symbol
- Auto-generated circle with cross tickmarks at 0°/90°/180°/270° — reads as a sprinkler head. Keep.

### 3D representation
- 50×50×80 mm cylindrical body with a small 25 mm deflector at the bottom.

### Type variants (matches BS EN 12845 deployment)
1. `PENDANT` ← default
2. `UPRIGHT` — flip the deflector to top
3. `SIDEWALL` — rotate body 90°, project from wall face
4. `CONCEALED` — shorten body, recessed in ceiling tile

### Parameters per type
- `FLS_SPR_K_FACTOR` — 80 (residential), 115 (OH), 240 (HHP)
- `FLS_SPR_TEMP_C` — 68 (default), 79 (kitchen), 93 (boiler room)

---

## STING_SEED_CommunicationDevice (Communication Devices)

**Hosting:** Face-based · **Template:** `Metric Data Device.rft` or Generic Model face based · **Symbol size at 1:100:** 4 mm

### 2D plan symbol
- Auto-generated outline with concentric arcs reads as a Wi-Fi access point. Keep for `WIFI_AP`.

### 3D representation
- 220×220×40 mm flat disc shape (auto box approximates).

### Type variants
1. `WIFI_AP` ← default
2. `DATA_OUTLET_RJ45` — 100×100×30 mm wall plate, replace 2D arcs with two small `J45` rectangles
3. `PATCH_PANEL` — 483×200×44 mm rack panel
4. `CCTV_CAMERA` — dome 120×120×80 mm ceiling-mounted

---

## After authoring: end-to-end test

1. Place one instance of each polished seed in a test view.
2. Tag each — existing STING tag families pick them up automatically (tag families bind by category + shared param GUID, so seed identity doesn't matter).
3. Open a schedule for the relevant category — every parameter from the JSON spec should appear as a column-able field.
4. Run `Swap to Manufacturer` with a real manufacturer family loaded — verify:
   - Position / rotation / host preserved
   - Tag still reads (parameters survived the swap via GUID)
   - `STING_DESIGN_REF_TXT` now contains `STING_SEED_<original>`
   - `STING_SWAP_HISTORY_TXT` records timestamp + operator + source/dest pair
5. Re-run `Swap to Manufacturer` selecting the swapped instances + a different manufacturer family — should swap forward AND record both entries in the history.

---

## Troubleshooting

**Q: My polished seed got overwritten when I re-ran `Build Seed Families`.**  
A: Yes — the command writes to `_BIM_COORD/Families/Seeds/` which is a project-scoped fallback. Save your polished `.rfa` to the corporate baseline at `Families/Seeds/` (this folder) instead. The Build command never touches the corporate baseline.

**Q: A type variant is missing from the swap candidates.**  
A: The swap registry uses regex against the loaded family's name. If the manufacturer family is loaded but no candidate fires, edit `STING_FAMILY_SWAP_REGISTRY.json` and add a pattern that matches its filename. Project overrides go in `<project>/_BIM_COORD/family_swap_registry.json`.

**Q: Connectors disappeared after the swap.**  
A: Revit re-creates connectors based on the destination family's definitions. If the destination has fewer or differently-positioned connectors, the unmatched ones are dropped. Run `AutoJoinMepConnectors` on the swapped set; for unrecoverable cases, run `BatchAssignCircuits` to re-stitch electrical topology.

**Q: My company has more than the 10 default seed categories.**  
A: Drop a `STING_SEED_<NewCategory>.json` into `StingTools/Data/Seeds/`, add a corresponding entry to `STING_FAMILY_SWAP_REGISTRY.json`, run `Build Seed Families`, finish per the same workflow above. No code change required.

**Q: I don't have a shared parameter file loaded.**  
A: Run `LoadSharedParamsCommand` (the dock-panel button labelled "Load Params") before `BuildSeedFamiliesCommand`. The shared params live in `Data/MR_PARAMETERS.csv` and `Data/MR_PARAMETERS.txt`; loading them binds every shared parameter the seeds need.
