# Seed Families, Auto-Placement, Auto-Routing & Sleeves — A Plain-English Guide

> A walk-through for site engineers, architects, designers, and BIM
> coordinators who want to drive STING's auto-modelling pipeline end
> to end without reading any code.
>
> **Who this is for:** anyone who has used Revit but has not built it.
> **What it covers:** what a seed family is, why STING uses them, how
> the auto-placement engine drops fixtures into rooms, how auto-routing
> wires those fixtures up to the building's services, and how sleeves
> and fire-stops are detected and placed where MEP runs cross
> structure.
> **What it does NOT cover:** the engine internals — see
> [`PLACEMENT_FAMILY_AUTHORING.md`](../PLACEMENT_FAMILY_AUTHORING.md),
> [`PLACEMENT_CENTRE_GUIDE.md`](../PLACEMENT_CENTRE_GUIDE.md), and
> [`STING_ELECTRICAL_LAYMANS_GUIDE.md`](../STING_ELECTRICAL_LAYMANS_GUIDE.md)
> for that depth.

---

## 0. The 60-second pitch

STING's auto-modelling pipeline turns a blank room into a fully
serviced room in three button-presses:

1. **Build Seed Families** — STING ships generic placeholder families
   (a "seed lighting fixture", a "seed WC", a "seed AHU"). They look
   simple but carry the full STING parameter scheme.
2. **Place Fixtures** — the placement engine reads room-by-room rules
   ("a switch lives 1100 mm AFF, 300 mm from the door hinge, on the
   hinge side") and drops the seeds where they belong.
3. **Auto-Drop / Auto-Route** — the routing engine picks every placed
   fixture up, finds the nearest service (cable tray, branch pipe,
   duct main), and draws the conduit / pipe / duct that connects them.
4. **Place Sleeves & Detect Penetrations** — wherever an MEP run
   crosses a wall, floor, or beam, STING places a fire-stop / sleeve
   family and stamps the host's fire rating onto it.

You replace the seeds with real Schneider / Hilti / Beckhoff / Hilti
families later by clicking **Swap to Manufacturer**. Position,
rotation, host, and parameters survive the swap because the seed and
the manufacturer family share the same shared-parameter GUIDs.

The same seed can therefore be authored once, used on a hundred
projects, and replaced with a hundred different real products — the
auto-tagger, schedules, and COBie export keep working unchanged.

---

## 1. The mental model — why "seeds"?

A normal Revit workflow starts with a manufacturer's family
(`Schneider_Twin_Switched_Socket.rfa`). The problem with that is:

- You can't auto-place 200 sockets unless the family is loaded.
- Different projects use different manufacturers, so the auto-place
  rules can't reference family names.
- The auto-tagger relies on shared-parameter GUIDs that real
  manufacturer families almost never carry by default.
- When procurement changes brand mid-project, every tag and schedule
  link rots.

A **seed family** is a deliberately generic stand-in:

- Same Revit *category* as the real product (Lighting Fixtures,
  Plumbing Fixtures, etc.) — so categorical rules and schedules work.
- Same shared-parameter GUIDs the STING tag engine uses
  (`ASS_TAG_*`, `ELC_*`, `LTG_*`, etc.) — so tags survive the swap.
- Stable seed identifier in `STING_SEED_FAMILY_TXT` — so the swap
  registry knows which manufacturer families are valid replacements.
- A minimal 2D symbol and 3D mass — readable enough to coordinate
  against, but small enough that nobody mistakes it for the final
  geometry.

When procurement decides on a real manufacturer, you load the
manufacturer's family, click **Swap to Manufacturer**, and STING
swaps every seed instance for the real one in a single transaction.

---

## 2. The 16 seed family catalogue

The seed library lives in `StingTools/Data/Seeds/` as 16 JSON specs.
Each JSON tells `BuildSeedFamiliesCommand` (a) which `.rft` template
to start from, (b) which shared parameters to bind, (c) which type
variants to mint, and (d) which MEP connectors to attach.

The polished `.rfa` files belong in the corporate baseline at
`Families/Seeds/`. Project-scoped `.rfa` files land in
`<project>/_BIM_COORD/Families/Seeds/` and are overwritten every time
**Build Seed Families** runs — so once you've polished a seed
visually, copy it back to `Families/Seeds/` to keep your work safe.

### The catalogue at a glance

| # | JSON spec | Category | Hosting | Default 1:100 size |
|---|---|---|---|---|
| 1 | `STING_SEED_LightingFixture.json` | Lighting Fixtures | Ceiling-based | 6 mm |
| 2 | `STING_SEED_ElectricalFixture.json` | Electrical Fixtures | Face-based | 4 mm |
| 3 | `STING_SEED_ElectricalEquipment.json` | Electrical Equipment | Standalone | 8 mm |
| 4 | `STING_SEED_FireAlarmDevice.json` | Fire Alarm Devices | Face-based | 5 mm |
| 5 | `STING_SEED_SpecialityEquipment.json` (FRP) | Speciality Equipment | Face-based | 5 mm |
| 6 | `STING_SEED_PlumbingFixture.json` | Plumbing Fixtures | Face-based | 6 mm |
| 7 | `STING_SEED_AirTerminal.json` | Air Terminals | Ceiling-based | 6 mm |
| 8 | `STING_SEED_MechanicalEquipment.json` | Mechanical Equipment | Standalone | 12 mm |
| 9 | `STING_SEED_Sprinkler.json` | Sprinklers | Ceiling-based | 4 mm |
| 10 | `STING_SEED_CommunicationDevice.json` | Communication / Data Devices | Face-based | 4 mm |
| 11 | `STING_SEED_JunctionBox.json` | Junction Boxes / Electrical Fixtures | Face-based | 5 mm |
| 12 | `STING_SEED_PlumbingEquipment.json` | Plumbing Equipment | Standalone | 12 mm |
| 13 | `STING_SEED_MedGasOutlet.json` | Plumbing Fixtures (med-gas) | Face-based | 5 mm |
| 14 | `STING_SEED_LabFixture.json` | Speciality / Plumbing Fixtures (lab) | Face-based | 6 mm |
| 15 | `STING_SEED_FireDamper.json` | Duct Accessories | Face-based | 6 mm |
| 16 | `STING_SEED_AcousticSeal.json` | Speciality Equipment (acoustic) | Face-based | 5 mm |

> Seeds 1–10 are tier 1 / 2 (everyday MEP & electrical).
> Seed 11 (Junction Box) is the **automation seed** — it materialises
> automatically wherever auto-routing sees a BS 7671 §522.8.5
> bend-count or run-length violation.
> Seeds 12–14 (tier 3) cover central plant, medical gas, and lab.
> Seeds 15–16 (Phase 178f) are penetration products for fire and
> acoustic compartmentation.

### How to author each seed

Run **Build Seed Families** once — it generates every `.rfa` from the
JSON specs and loads them into the active project. Then open each
`.rfa` in Family Editor and finish the visual symbology per the table
below. None of this requires programming. Plan on roughly **one hour
per seed** for someone comfortable with Family Editor.

---

### 2.1  STING_SEED_LightingFixture — Lighting Fixtures

- **Template:** `Metric Lighting Fixture ceiling based.rft`
- **Hosting:** Ceiling-based
- **Symbol size at 1:100:** 6 mm
- **Default sub-category:** `STING_SEED`

**2D plan symbol**
- 600 × 600 mm rectangle (auto-generated outline is correct size).
- Diagonal `X` from corner to corner.
- 3 mm filled centre circle — emergency variants fill it red.

**3D representation**
- 595 × 595 × 25 mm flush plate + 100 × 100 × 90 mm centre boss.
- Generic `STING_LumPlate` material (white) — manufacturer swap
  replaces this.

**Type variants** (matches swap-registry priority order)
1. `RECESSED_LED_600x600` (default) — `LTG_TYPE_TXT = "RECESSED"`
2. `DOWNLIGHT` — keep 600 × 600 outline, `LTG_TYPE_TXT = "DOWNLIGHT"`
3. `PENDANT` — raise 3D mass 200 mm above origin
4. `LINEAR_LED` — change rectangle to 1200 × 100 mm
5. `EMERGENCY` — fill the centre circle red

**Per-variant parameters**

| Variant | `ELC_PHOTO_LUMENS` | `ELC_PHOTO_WATTS` |
|---|---|---|
| `RECESSED_LED_600x600` | 4000 | 32 |
| `DOWNLIGHT` | 1200 | 12 |
| `LINEAR_LED` | 6000 | 50 |
| `EMERGENCY` | 200 | 4 |

---

### 2.2  STING_SEED_ElectricalFixture — Electrical Fixtures

- **Template:** `Metric Electrical Fixture face based.rft`
- **Hosting:** Face-based (wall-hosted in practice)
- **Symbol size at 1:100:** 4 mm

**2D plan symbol**
- Auto 2-gang socket symbol (rectangle + 4 short verticals).
- Add a 1100 mm mounting-height label bound to
  `ELE_FIX_MOUNT_HEIGHT_MM` so the symbol reads its own height
  without a tag.

**3D representation**
- 150 × 35 × 85 mm flush plate, projecting 5 mm proud of the wall.

**Type variants**
1. `SOCKET_2G` (default)
2. `SOCKET_1G` — 75 × 85 mm plate, 2 verticals
3. `SWITCH_2G` — 150 × 85 plate, 2 horizontal slits
4. `DATA_OUTLET_2G` — plate with two small `D` letters
5. `FLOOR_BOX` — 200 × 200 plate, mounting height = 0
6. `FCU` — plate with a 20 mm circle (fuse indicator)

> **Important origin rule:** the family origin must sit at the
> **fixing-centre midpoint of the front face** (the faceplate
> plane), NOT the back of the box. The placement engine offsets from
> the origin via `WALL_FACE_OFFSET = Auto`; if the origin is at the
> back of the box, every socket sits 25 mm inside the wall.

---

### 2.3  STING_SEED_ElectricalEquipment — Electrical Equipment

- **Template:** `Metric Electrical Fixture.rft` (standalone)
- **Hosting:** Standalone
- **Symbol size at 1:100:** 8 mm
- **Connectors:** top + bottom (auto-generated from JSON)

**2D plan symbol**
- Rectangle with horizontal divider (auto). Add `H = 600 mm` label so
  the floor-to-top dimension reads on plan.

**3D representation**
- 400 × 200 × 600 mm (W × D × H) wall-mounted panel enclosure.
- 40 × 10 × 15 mm handle on the right edge so door direction reads.

**Type variants**
1. `DISTRIBUTION_BOARD_DB` (default)
2. `MAIN_SWITCHBOARD_MSB` — 800 × 400 × 1800 mm, top-only connector
3. `CONSUMER_UNIT` — 300 × 90 × 250 mm
4. `ISOLATOR` — 150 × 100 × 200 mm
5. `JUNCTION_BOX` — 100 × 100 × 50 mm
6. `TRANSFORMER` — 600 × 400 × 800 mm

**Connector polish (critical)**
After build, **verify the connectors land on real faces** in Family
Editor — if the 3D box doesn't extrude up/down, connectors hang in
mid-air and `AutoConduitDrop` will refuse to wire them. Drag each
connector onto the relevant face reference. Set:

- Domain: *Electrical*
- Classification (supply): *Power*
- Classification (load): *Power - Balanced* (3-phase) or
  *Power - Unbalanced* (1-phase)

---

### 2.4  STING_SEED_FireAlarmDevice — Fire Alarm Devices

- **Template:** `Metric Fire Alarm Device.rft`
- **Hosting:** Face-based
- **Symbol size at 1:100:** 5 mm

**2D plan symbol**
- Auto circle with internal `X` — reads as smoke detector.

**3D representation**
- 110 × 110 × 50 mm cylindrical detector housing.
- Use a *revolve*, not extrusion — gives a circular profile.

**Type variants** (matches BS EN 54 device-type classification)
1. `SMOKE_OPTICAL` (default — auto symbol)
2. `HEAT_DETECTOR` — replace `X` with `H`
3. `MULTI_SENSOR` — circle + small triangle
4. `CALL_POINT_MCP` — 100 × 100 hatched square
5. `SOUNDER_BEACON_VAD` — circle with 3 radiating lines
6. `BEAM_DETECTOR` — rectangle with 2 small circles at ends

**Per-variant parameters**

| Variant | `FLS_DEV_TYPE_TXT` | `FLS_DEV_CERT_TXT` |
|---|---|---|
| `SMOKE_OPTICAL` | "SMOKE" | BS EN 54-7 |
| `HEAT_DETECTOR` | "HEAT" | BS EN 54-5 |
| `SOUNDER_BEACON_VAD` | "SOUNDER" | BS EN 54-3 |

`FLS_DEV_LOOP_TXT` carries the addressable loop (`L1`–`L8`).

---

### 2.5  STING_SEED_SpecialityEquipment — FRP Penetrations

> **The most important seed.** Drives the auto-generated Penetration
> Register and is placed automatically by `FrpPenetrationPlacer` at
> every wall / slab / beam crossing.

- **Template:** `Metric Specialty Equipment face based.rft`
- **Hosting:** Face-based (slab soffit / wall face / beam web)
- **Symbol size at 1:100:** 5 mm

**2D plan symbol**
- Concentric circles (Ø500 outer, Ø350 inner) with four short tick
  lines at 0°/90°/180°/270° — reads as a sleeve-through.
- For section view, add a 200 mm vertical bar with horizontal arrows
  showing the seal extending through the host.

**3D representation**
- Conical sleeve: 80 × 80 mm at top narrowing to 60 × 60 mm at bottom
  (concrete-cover allowance), 200 mm tall.
- Add a 2D line at the soffit reference plane for section visibility.

**Type variants** (matches `FrpPenetrationPlacer` rating index)
1. `FR30` — `PEN_FIRE_RATING_TXT = "FR30"`
2. `FR60` (most common) — `PEN_FIRE_RATING_TXT = "FR60"`
3. `FR90` — `PEN_FIRE_RATING_TXT = "FR90"`
4. `FR120` — `PEN_FIRE_RATING_TXT = "FR120"`
5. `SLEEVE_GENERIC` — `PEN_FIRE_RATING_TXT = ""` (non-fire-rated)

**Critical parameter wiring**
Set `Mark = PEN_CONTROL_NUMBER_TXT` as a type formula. Every
fire-stop then appears in tag schedules without extra wiring.

---

### 2.6  STING_SEED_PlumbingFixture — Plumbing Fixtures

- **Template:** `Metric Plumbing Fixture face based.rft`
- **Hosting:** Face-based (wall for WC / basin / urinal,
  floor for shower)
- **Symbol size at 1:100:** 6 mm
- **Connectors:** DCW + DHW + Sanitary outlet (3 declared — one pass
  wires all three)

**2D plan symbol**
- Auto hexagonal-ish outline. Add a small dot at the connection
  point (typically centre-back).

**3D representation**
- 400 × 600 × 400 mm (W × D × H) — respectable WC volume. Override
  per type variant.

**Type variants**
1. `WC` (default)
2. `BASIN` — 400 × 350 × 150 mm
3. `URINAL` — 400 × 350 × 600 mm
4. `SHOWER` — 900 × 900 × 100 mm (tray)
5. `SINK` — 600 × 450 × 200 mm

> The seed declares all three connectors at symbol level so
> `AutoPipeDrop` wires every service in one pass. WC and urinal
> variants don't *need* DHW but inherit it as a no-op cap; the
> manufacturer swap replaces the seed with a fixture-correct
> connector set.

---

### 2.7  STING_SEED_AirTerminal — Air Terminals

- **Template:** `Metric Air Terminal.rft`
  (or `Metric Generic Model ceiling based.rft` if not pre-installed)
- **Hosting:** Ceiling-based
- **Symbol size at 1:100:** 6 mm

**2D plan symbol**
- Auto 595 × 595 mm tile with `+` and `X` overlay (square diffuser).

**3D representation**
- 595 × 595 × 60 mm shallow ceiling-tile profile.

**Type variants**
1. `SUPPLY_DIFFUSER_SQ` (default)
2. `SLOT_DIFFUSER` — 1200 × 100 mm slot
3. `SUPPLY_GRILLE` — horizontal slats
4. `EXTRACT_GRILLE` — same outline, single inward arrow
5. `LOUVRE` — outdoor 600 × 300 mm

---

### 2.8  STING_SEED_MechanicalEquipment — Mechanical Equipment

- **Template:** `Metric Mechanical Equipment.rft`
- **Hosting:** Standalone
- **Symbol size at 1:100:** 12 mm
- **Connectors:** left + right HVAC connectors (auto-generated)

**2D plan symbol**
- Rectangle with vertical centre line (reads as AHU).
- Add `S` (supply) on left, `R` (return) on right.

**3D representation**
- 1200 × 600 × 800 mm — small AHU. Override per variant.

**Type variants**
1. `AHU` (default)
2. `FCU` — 800 × 400 × 250 mm
3. `CHILLER` — 2400 × 1200 × 1800 mm, top connectors
4. `BOILER` — 800 × 600 × 1200 mm, classification *Hydronic*
5. `PUMP` — 400 × 200 × 350 mm, side-mounted connectors

**Connector classifications**
- AHU / FCU: domain *HVAC*, *Supply Air* / *Return Air*
- Chiller / boiler / pump: domain *Piping*, *Hydronic Supply* /
  *Hydronic Return*

---

### 2.9  STING_SEED_Sprinkler — Sprinklers

- **Template:** `Metric Sprinkler.rft`
- **Hosting:** Ceiling-based
- **Symbol size at 1:100:** 4 mm

**2D plan symbol**
- Circle with cross tickmarks at 0°/90°/180°/270°.

**3D representation**
- 50 × 50 × 80 mm cylindrical body + 25 mm deflector.

**Type variants**
1. `PENDANT` (default)
2. `UPRIGHT` — flip the deflector to top
3. `SIDEWALL` — rotate body 90°, project from wall face
4. `CONCEALED` — shorten body, recessed in ceiling tile

**Per-variant parameters**

| Variant | `FLS_SPR_K_FACTOR` | `FLS_SPR_TEMP_C` |
|---|---|---|
| `PENDANT` | 80 (residential) | 68 |
| `UPRIGHT` | 115 (OH) | 79 (kitchen) |
| `CONCEALED` | 240 (HHP) | 93 (boiler room) |

---

### 2.10  STING_SEED_CommunicationDevice — Comms / Data Devices

- **Template:** `Metric Data Device.rft` or Generic Model face based
- **Hosting:** Face-based
- **Symbol size at 1:100:** 4 mm

**2D plan symbol**
- Auto outline with concentric arcs (Wi-Fi access point).

**3D representation**
- 220 × 220 × 40 mm flat disc.

**Type variants**
1. `WIFI_AP` (default)
2. `DATA_OUTLET_RJ45` — 100 × 100 × 30 mm wall plate
3. `PATCH_PANEL` — 483 × 200 × 44 mm rack panel
4. `CCTV_CAMERA` — 120 × 120 × 80 mm dome

---

### 2.11  STING_SEED_JunctionBox — the automation seed

This one is special — it's placed automatically by
`JunctionBoxAutoPlacer` wherever auto-routing sees a BS 7671 §522.8.5
violation (more than 3 bends in a run, or run length over the
configured cap).

- **Template:** `Metric Electrical Fixture face based.rft`
- **Hosting:** Face-based (slab soffit or wall)
- **Variants:** 5 A / 20 A / 30 A / IP66 (`STING_FIXTURE_VARIANT_TXT`)

When the seed is unloaded, the placer instead **stamps**
`ELC_CDT_BREAKPOINT_TXT` on the offending conduit so the schedule
still surfaces the requirement; running **Build Seed Families** then
**Auto-Drop** again materialises the boxes.

---

### 2.12 – 2.16  Tier 3 + penetration seeds

| Seed | Used for | Notes |
|---|---|---|
| `PlumbingEquipment` | Central plant — calorifiers, water heaters, pump sets | Standalone, multi-service connectors |
| `MedGasOutlet` | Wall outlets for O₂ / N₂O / med-vac (HTM 02-01) | Face-based, single connector per gas |
| `LabFixture` | Lab benches, fume cupboards, eyewash | Face-based; large connector union |
| `FireDamper` | Ducts crossing fire-rated walls (BS EN 1366-2) | Face-based; placed by `FrpPenetrationPlacer` |
| `AcousticSeal` | Non-rated but acoustically-sensitive walls (BS 8233 / Approved Doc E) | Face-based; placed by `FrpPenetrationPlacer` |

The connector polish + variant authoring rules from §2.3 / §2.6
apply identically.

---

## 3. The auto-placement workflow

Once the seeds are loaded, **Place Fixtures** (the *Place Fixtures*
button, or the Placement Centre's *Run* button) walks every room in
scope and drops seed instances per the rules in
`StingTools/Data/Placement/STING_PLACEMENT_RULES*.json`.

### 3.1  What a placement rule looks like

Each rule is a small JSON object that answers four questions:

| Question | Field |
|---|---|
| What am I placing? | `category`, `familyTypeRegex`, `variantHint` |
| Where? | `anchor` (e.g. `WALL_MIDPOINT`), `mountingHeightMm`, `mountingReference` |
| In which rooms? | `roomNameRegex`, `requiredSpatialKind` |
| How many? | `kind` (Point / Density / Linear), `perAreaM2`, `perLinearMetre` |

The shipped catalogue of ~250 rules covers every UK regulation that
matters in a fit-out:

- **Approved Doc M** — switch and socket reach heights
- **BS 7671** — perimeter sockets, isolation, RCD requirements
- **BS 5266-1** — emergency luminaires every 8 m on escape routes
- **BS 5839-1** — smoke detectors at 7.5 m radius
- **BS 5306 / BS EN 12845** — sprinkler spacing
- **BS EN 12464-1 / CIBSE LG7** — office 500 lux grids
- **BS 6465** — sanitary fixtures per occupant
- **HTM 02-01 / 03-01** — healthcare medical-gas + isolation room rules

### 3.2  The 4-button flow

1. **Open the room you want to populate** in plan view, select rooms
   in scope (or leave nothing selected — the engine then uses every
   room in the project).
2. **Open the Placement Centre** (*Place Fixtures Centre* button).
   Tick the discipline checkboxes (electrical / lighting / plumbing /
   fire / sprinklers / comms / HVAC) and the standards toggles
   (Doc M / BS 7671 / BS 5266 / BS 5839 / BS 6465 / EN 12464).
3. Click **Preview** — engine runs in dry-run mode and reports the
   per-rule + per-room counts and rejections.
4. If happy, click **Place Fixtures** — engine commits the
   placements in one Transaction and writes a provenance stamp
   (rule id, anchor, standard) into every placed instance.

### 3.3  What "where" looks like — anchors

Every rule names an **anchor** — the reference point it measures
from. The 17 shipped anchors cover almost every real placement:

| Anchor | Meaning | Typical use |
|---|---|---|
| `ROOM_CENTRE` | Middle of the room | Air diffusers |
| `CEILING_CENTRE` | Same X/Y as room centre, on the ceiling | Smoke detectors |
| `WALL_MIDPOINT` | Mid-point of each wall | Wall sockets |
| `WALL_CORNER` | Corner of two walls | WC pans |
| `DOOR_HINGE` | Hinge side of a door | Light switches (Doc M) |
| `DOOR_JAMB` | Latch/handle side | Access readers, MCPs |
| `DOOR_HEAD` | Above the door | Exit signs |
| `WINDOW_SILL` | Bottom of a window | Trickle vents |
| `LIGHTING_GRID` | BS EN 12464-1 lux grid | Office luminaires |
| `OPPOSITE_WALL` | Wall furthest from first door | Thermostats |
| `GRID_INTERSECTION` | Nearest structural grid intersection | Plant-room equipment |
| `COLUMN_FACE` | Beside the nearest column | Local isolators |
| `PERIMETER_OFFSET` | Spaced along every wall | Continuous trunking |
| `RAISED_FLOOR_TILE` | Nearest 600 mm raised-access tile centre | Floor boxes |
| `STAIR_NOSING` | Edge of every tread | Photoluminescent strips |
| `ESCAPE_ROUTE_CENTRELINE` | Down the middle of escape corridors | Emergency luminaires |
| `RELATIVE_TO` | XYZ of a previous rule's last placement | Co-located fixtures |

### 3.4  How "how high" works

`mountingHeightMm` is interpreted relative to one of four references:

| `mountingReference` | Means |
|---|---|
| `FFL` *(default)* | Above the finished floor |
| `SLAB` | Above the structural slab (≈ FFL minus screed) |
| `CEILING` | Below the suspended ceiling line |
| `SOFFIT` | Below the structural soffit (above any ceiling) |

So `mountingHeightMm = 1100, mountingReference = FFL` means
"1.1 m above the floor", which is the Doc M reach height for switches.

### 3.5  What gets stamped on every placement

Every placed instance is written with:

- The rule id that created it (`PLACE_RULE_ID_TXT`)
- The anchor and offsets used
- The standard quoted on the rule (`PLACE_STANDARD_TXT`)
- `STING_AUTO_PLACED_BOOL = 1` so the placement run can be undone
  category-cleanly later

Re-running **Place Fixtures** is **idempotent** — it only adds
instances where none already exist for that rule + anchor. So you
can safely re-run after editing rules.

### 3.6  What stops a rule firing — the diagnostic panel

After a run, the result panel reports a per-rule diagnostic. The
common reasons a rule fires zero:

| Reason | Fix |
|---|---|
| No room name matched the regex | Rename the room or relax the regex |
| No `FamilySymbol` matched the type regex | Load the right seed; check the `familyTypeRegex` in the rule |
| All candidates rejected on score (collision / clearance) | Lower the score threshold, or open the rule's Geometry panel |
| Required dependency rule placed nothing | The dependent rule failed too — trace the chain from the bottom up |
| Category disabled in the discipline checkbox | Re-enable in the Placement Centre header |

### 3.7  Critical setup for placement

Before the first run on any project, run **Placement_AuditSetup**.
It cross-checks the live model against
[`PLACEMENT_FAMILY_AUTHORING.md`](../PLACEMENT_FAMILY_AUTHORING.md)
and writes a CSV of issues. The most common fixes:

- Bind `STING_BOX_LOCATION_ID` to MEP / Lighting / Electrical /
  Junction-Box categories — without this, two-phase first-fix /
  second-fix matching runs in degraded mode.
- Bind `STING_NOGGIN_REQUIRED` (Yes/No) to Lighting Fixtures.
- Load the BESA round box family (`Conduit_BESA_Round`, types
  `57D_36mm`, `57D_47mm`).
- Load `STING_SLEEVE_ROUND` family.
- Add the `Construction` and `Handover` phases.
- Set the project `BuildingType` (Residential / Office / Healthcare /
  Education) — filters discipline packs at load time.

---

## 4. The auto-routing workflow

Once fixtures are placed, the **routing engines** wire them up to
the building's services. Three engines, one per discipline:

| Discipline | Engine | Looking for | Default search radius |
|---|---|---|---|
| Electrical | `AutoConduitDrop` | Cable tray / conduit main | 3000 mm |
| Plumbing | `AutoPipeDrop` | Branch pipe of the right system | 4000 mm |
| HVAC | `AutoDuctDrop` | Duct main of the right system | 5000 mm |

### 4.1  The 3-step flow

1. **Select the placed fixtures** (or leave nothing selected — the
   engine then uses every fixture in the active view).
2. **Click Auto-Drop** in the dock-panel ROUTING tab.
3. The engine groups the selection by discipline and dispatches each
   group to the correct drop engine. Result panel shows per-engine
   counts (created / connected / take-offs / warnings).

Behind the scenes, for each fixture:

1. Find its free MEP connector(s).
2. Search a cylinder of radius `MaxSearchRadiusMm` upwards (or
   sideways for slab-soffit pendants) for a matching service main.
3. Compute an intercept point on the main.
4. Create a vertical drop conduit / pipe / duct from fixture to
   intercept (`Conduit.Create` / `Pipe.Create` / `Duct.Create`).
5. **Wire it up** — `Connector.ConnectTo` joins the fixture connector
   to the drop's near end. A take-off fitting
   (`Document.Create.NewTakeoffFitting`) is inserted on the main at
   the drop's far end.
6. **Stamp** install method (`CLIPPED` / `SUSPENDED` / `EMBEDDED` /
   `CHASED`), fab method (`SITE` / `WORKSHOP`), and SMACNA seam type
   (ducts) on the new run.

The result is a fully connected MEP system member — system ownership
propagates, pressure-drop calcs run, and the run lands in schedules.

### 4.2  Multi-service mode (Phase 178e)

A basin needs **cold + hot + waste**; an AHU needs
**supply + return + outdoor + relief**. With `MultiServiceMode = true`
(default for Pipe and Duct engines) the base loops every free
connector on the fixture and routes each to its own corridor band:

- DCW (cold): low corridor
- DHW (hot): high corridor
- Soil / waste: floor-level corridor
- HVAC supply / return: separate bands

Corridor bands are configured in
`Data/Routing/STING_SERVICE_CORRIDORS.json`. They keep parallel
services from clashing under the same ceiling.

### 4.3  Separation rules (BS EN 50174-2)

`Data/Routing/STING_SEPARATION_RULES.json` declares minimum
separations between services (e.g. 200 mm between LV power and Cat 6
data without metal containment). The drop engine respects these
rules when it picks the intercept point on the main.

### 4.4  In-wall chase routing — when conduit hides in the wall

Where wall-hosted fixtures need conduit chased *into* the wall (rather
than dropping to a tray above the ceiling), set the rule's
`InstallMethod = "CHASED"` and switch on
`UseChaseRoutingWhenAvailable` on `AutoConduitDrop`. The
`InWallChaseRouter` then reads the wall's compound structure,
computes available chase depth (face thickness − conduit OD − cover),
and routes the conduit inside the wall. If chase depth is
insufficient, the engine falls back to the surface drop and warns.

### 4.5  Junction boxes — automatic compliance fix-up

`JunctionBoxAutoPlacer` runs as a **post-step** of `AutoConduitDrop`.
For every routed conduit, it counts bends + length. If the run
exceeds:

- **3 bends** (BS 7671 §522.8.5 default), or
- The configured `MaxRunLengthMm` (typically 10 m unswitched runs)

…the placer drops a `STING_SEED_JunctionBox` instance at the
break-point and splits the conduit into upstream + downstream
segments terminated into the new box's connectors. Stamps:

- `ELC_JB_AUTO_PLACED_BOOL = 1`
- `ELC_JB_REASON_TXT = "BEND_LIMIT_EXCEEDED"` or `"RUN_LENGTH_EXCEEDED"`

If the seed isn't loaded, the placer instead stamps
`ELC_CDT_BREAKPOINT_TXT` on the offending conduit so the schedule
still surfaces the requirement.

### 4.6  Hangers and supports

With `EmitSupports = true` (default), the engine auto-emits physical
hangers / clips on the dropped runs per:

- BS 5572 (sanitary pipework — typically 1.2 m centres for cast iron)
- MSS SP-58 (steel pipework hanger spacing)
- SMACNA (duct hanger spacing)

Hanger types are configurable: `CLEVIS_ROD` (default), `CLEVIS_ROD_TURNBUCKLE`,
`SPRING_ISOLATOR`, `BAND_HANGER`, `CHANNEL_TRAPEZE`.

### 4.7  Optional: full A* pathfinding

For complex routes that need to avoid obstacles in 3D, the engine
exposes `RoutingPathfinder` which builds an adaptive voxel grid
between two endpoints, runs A* search, and returns the polyline. This
is opt-in (the default plumb-vertical drop covers 95% of real
fixtures); turn it on per rule when you need full 3D navigation.

---

## 5. The sleeves & penetrations workflow

Wherever an MEP run crosses a wall, floor, or beam, three things have
to happen:

1. The host gets a hole.
2. The hole gets a fire-rated sleeve / fire-stop.
3. The penetration is recorded — host, member, fire rating, sealant
   type, certifier — for the FM hand-over.

STING handles all three automatically.

### 5.1  Two related but separate engines

| Engine | What it does | Output |
|---|---|---|
| `SleeveEngine` (`PlaceSleevesCommand`) | Places a hosted void family at the crossing midpoint, cuts the host with `InstanceVoidCutUtils`, inherits fire rating | `STING_SLEEVE_ROUND` / `STING_SLEEVE_RECT` instance |
| `FrpPenetrationPlacer` (`PenetrationsDetectAndPlaceCommand`) | Detects + records every penetration, places an FRP family (Hilti / Promat) at the crossing, stamps the full PEN_* parameter set | `STING_SEED_SpecialityEquipment` instance |

The two share the same UUID (`PEN_PFV_UUID_TXT`,
`STING_SLEEVE_PFV_UUID`) keyed on a UUIDv5 of host + member, so a
sleeve and an FRP for the **same physical hole pair up** and never
duplicate.

### 5.2  The sleeve workflow — `Place Sleeves`

1. **Select MEP runs** in scope (or leave nothing selected — engine
   uses every MEP curve in the active view).
2. **Click Place Sleeves**. The dialog asks Preview or Apply.
3. **Preview** scans every MEP-vs-host crossing, sizes a sleeve per
   `SleeveSizingRules.json` (insulation-aware, clearance-driven),
   reports counts + warnings. No model changes.
4. **Apply** does the same scan and:
   - Places the sleeve void family (`STING_SLEEVE_ROUND`,
     `STING_SLEEVE_RECT`, or `STING_PROVISION_VOID` — first match
     wins).
   - Sizes the sleeve: depth = host thickness + 2 × 10 mm protrusion;
     bore = element OD/width + 2 × insulation + 2 × clearance, capped
     at `MinBoreMm`.
   - Calls `InstanceVoidCutUtils.AddInstanceVoidCut` so the host gets
     the hole.
   - Inherits the host's `FIRE_RATING` (walls/floors carry it as a
     type parameter) and writes it to
     `STING_SLEEVE_HOST_FIRE_RATING`.
   - Stamps `STING_SLEEVE_PFV_UUID` (UUIDv5 of host + member) for
     IFC4 PFV round-trip with Tekla's Hole Reservation Manager.

Failures on individual penetrations are caught — the batch never
aborts.

### 5.3  The FRP / penetration register workflow — `Penetrations Detect & Place`

Use this when you want a **register** rather than just sleeves:

1. **Select MEP runs** (or leave selection empty — engine uses
   active-view MEP).
2. **Click Penetrations Detect & Place**. The pipeline runs:
   1. `SlabPenetrationDetector` — every floor crossing
   2. `WallPenetrationDetector` — every fire-rated wall crossing
      (BS 9999 / Approved Doc B)
   3. `BeamPenetrationDetector` — every beam crossing, classifies as
      `STRUCT_OK` / `STRUCT_REVIEW` / `STRUCT_FAIL` per AISC Design
      Guide 2 + BS EN 1992
   4. `FrpPenetrationPlacer` — drops a `STING_SEED_SpecialityEquipment`
      instance at every recorded crossing, picking the type variant
      that matches the host's fire rating

Every placed instance carries:

- `PEN_FIRE_RATING_TXT` — inherited from host (`FR30` / `FR60` /
  `FR90` / `FR120`, or empty for `SLEEVE_GENERIC`)
- `PEN_HOST_KIND_TXT` — `FLOOR` / `WALL` / `BEAM`
- `PEN_BEAM_OFFSET_PCT` and `PEN_BEAM_DEPTH_RATIO` (beams only) —
  used by the structural review
- `PEN_PFV_UUID_TXT` — UUIDv5 idempotency key
- `PEN_CONTROL_NUMBER_TXT` — sequential register number
- `Mark = PEN_CONTROL_NUMBER_TXT` (via the formula on the type
  variant) — so tags read the control number out of the box

### 5.4  Auto-sleeve placement (Phase 109) — model-wide sweep

`AutoSleevePlacementCommand` is the **whole-model** equivalent of
`Place Sleeves`. It scans every pipe / duct / conduit / cable-tray
in the model, tests intersection against every wall / floor / roof,
and inserts a sleeve family at every intersection midpoint. Family
resolution is first-match-wins:

1. `STING_SLV_<discipline>_<diameter>.rfa` (e.g. `STING_SLV_PIPE_150.rfa`)
2. `STING_SLV_GENERIC.rfa`
3. First `FamilySymbol` whose name contains "SLEEVE"

50 mm minimum annulus is enforced per side (BS EN 1366-3).

### 5.5  Specialty penetration products (Phase 178f)

For ducts crossing fire-rated walls or floors, the placer chooses a
`STING_SEED_FireDamper` instance instead of a generic sleeve (BS EN
1366-2 — fire dampers are a different product class). For
non-rated but acoustically-sensitive walls (BS 8233 / Approved
Doc E), it chooses `STING_SEED_AcousticSeal`. The choice is driven
by the host wall's `FIRE_RATING` + acoustic spec — no user input
required.

### 5.6  IFC export — Provision For Voids round-trip

With `IFCExportOptions.AddOption("ExportProvisionForVoids", "true")`,
every sleeve and FRP exports as an IFC4 *Pset_ProvisionForVoid*
element keyed by the same UUIDv5. Tekla's Hole Reservation Manager
imports them as native holes, makes its own decisions, and the next
IFC import round-trips back into Revit using the same UUID. No
duplicates, no orphans.

### 5.7  BCF export

`ExportSleeveBcfCommand` exports every placed sleeve as a BCF 2.1
issue with a viewpoint snapshotted at the sleeve location. The
structural team gets a BCF file that Tekla / Solibri / Navisworks
all open natively.

---

## 6. End-to-end — putting it all together

Here is the canonical 9-step workflow for a brand-new project:

1. **Load shared parameters**
   *Load Params* button — binds the entire STING parameter scheme
   from `Data/MR_PARAMETERS.txt`.
2. **Build seed families**
   *Build Seed Families* button — emits 16 `.rfa` files into
   `<project>/_BIM_COORD/Families/Seeds/` and loads them.
3. **Polish each seed in Family Editor**
   Open each `.rfa`, follow the per-seed guidance in §2.x of this
   document plus
   [`Families/Seeds/README.md`](../../Families/Seeds/README.md), save,
   then copy to the corporate baseline at `Families/Seeds/`.
4. **Audit the project setup**
   *Placement_AuditSetup* button — produces a CSV of project-side
   issues (missing parameter bindings, missing seeds, missing phases,
   missing project type). Fix every red row.
5. **Place fixtures**
   *Place Fixtures Centre* → set discipline checkboxes + standards
   toggles → *Preview* → *Apply*. Result panel reports per-rule
   diagnostics.
6. **Auto-drop conduit / pipe / duct**
   Select all placed fixtures (or none — uses active view) →
   *Auto-Drop*. Watch for:
   - Junction-box auto-placement at BS 7671 §522.8.5 violations
   - Hanger auto-emission at BS 5572 / MSS SP-58 / SMACNA spacings
   - Multi-service routing of plumbing fixtures (3 connectors → 3
     drops in one pass)
7. **Place sleeves and detect penetrations**
   Two passes:
   - *Place Sleeves* (or *Auto-Sleeve Placement* for whole-model) —
     puts sleeves and cuts hosts.
   - *Penetrations Detect & Place* — produces the FRP register and
     places fire-stop families with full PEN_* metadata.
8. **Validate**
   Run the v4 validator chain (*Routing_ValidateFills*,
   *Validation_RunAll*) — connectivity, fill, spec, termination,
   slope, plus penetration coverage (every fire-rated host must have
   a sealed crossing).
9. **Swap to manufacturer**
   When procurement decides on real products: load each manufacturer
   `.rfa`, click *Swap to Manufacturer*. Position, rotation, host,
   shared parameters, tags, schedules, COBie all survive. The seed
   identity is preserved in `STING_DESIGN_REF_TXT`; every swap is
   recorded in `STING_SWAP_HISTORY_TXT`.

After step 9, the model contains real manufacturer geometry, fully
tagged, fully scheduled, fully routed, fully sealed, with every
placement and penetration carrying provenance back to the standard
that drove it.

---

## 7. Common pitfalls & quick fixes

| Symptom | Probable cause | Fix |
|---|---|---|
| All sockets sit 25 mm inside the wall | Family origin at back of box, not faceplate | Move origin to front-face midpoint, set `Reference = Strong` on `Center (Front/Back)` plane |
| Switches lie flat at door head, facing up | Family not `Always Vertical` | Tick *Always Vertical* in Family Properties |
| Pendant lights stack at room centre | Family is `One Level Based` un-hosted | Re-author as Ceiling-Hosted or Face-Based |
| `Place Fixtures` reports "no first-fix box family matched" | Family name doesn't match `BoxFamilyTypeRegex` | Rename family + types per the BESA / Square-Outlet conventions in §2.8 of [`PLACEMENT_FAMILY_AUTHORING.md`](../PLACEMENT_FAMILY_AUTHORING.md) |
| Sprinkler 600 mm separation never enforced | Sprinkler family not loaded | Load `STING_SEED_Sprinkler.rfa`; engine sees zero sprinklers and the check is a silent no-op otherwise |
| WC lands in the walk-in closet | Old room-name regex without word boundaries | Phase 139.6 PF-1 fix — re-import `STING_PLACEMENT_RULES*.json` to pick up the tightened regex |
| Auto-Drop creates the run but won't connect connectors | Connectors hang in mid-air on seed family | Open seed `.rfa`, drag connectors onto real face references in Family Editor |
| Auto-Drop reports "no service main within radius" | Service main missing or out of search radius | Either route a main first, or raise `MaxSearchRadiusMm` in the Routing tab |
| Sleeves placed but holes don't appear in host | Seed's *Cut with Voids When Loaded* is off | Tick the property in Family Properties, save, reload |
| Junction boxes don't materialise even though run has 5 bends | `STING_SEED_JunctionBox.rfa` not loaded | Run *Build Seed Families*, finish the seed per §2.11, re-run *Auto-Drop* |
| Polished `.rfa` got overwritten on next *Build Seed Families* | Polished file lived in `_BIM_COORD/Families/Seeds/` (project-scoped fallback) | Copy polished `.rfa` to corporate baseline `Families/Seeds/` — the build command never touches that path |
| Swap to Manufacturer drops connectors | Destination family has fewer / differently-positioned connectors | Run *AutoJoinMepConnectors* on the swapped set; for unrecoverable cases, *BatchAssignCircuits* re-stitches electrical topology |

---

## 8. Where each thing lives — the file map

| Concern | Path |
|---|---|
| Seed JSON specs | `StingTools/Data/Seeds/STING_SEED_*.json` |
| Polished seed `.rfa` (corporate) | `Families/Seeds/` |
| Polished seed `.rfa` (project fallback) | `<project>/_BIM_COORD/Families/Seeds/` |
| Placement rules | `StingTools/Data/Placement/STING_PLACEMENT_RULES*.json` |
| Service corridors | `StingTools/Data/Routing/STING_SERVICE_CORRIDORS.json` |
| Service separations | `StingTools/Data/Routing/STING_SEPARATION_RULES.json` |
| Swap registry | `StingTools/Data/STING_FAMILY_SWAP_REGISTRY.json` |
| Project swap overrides | `<project>/_BIM_COORD/family_swap_registry.json` |
| Audit CSVs | `<project>/_BIM_COORD/Audits/` |
| FRP register | `<project>/_BIM_COORD/Penetrations/` |

---

## 9. Further reading

- [`PLACEMENT_FAMILY_AUTHORING.md`](../PLACEMENT_FAMILY_AUTHORING.md)
  — every authoring requirement per category, plus the audit
  command's pass/fail criteria.
- [`PLACEMENT_CENTRE_GUIDE.md`](../PLACEMENT_CENTRE_GUIDE.md) —
  button-by-button tour of the Placement Centre dialog.
- [`STING_ELECTRICAL_LAYMANS_GUIDE.md`](../STING_ELECTRICAL_LAYMANS_GUIDE.md)
  — full electrical pipeline including cable-sizing and
  panel-schedule production.
- [`Families/Seeds/README.md`](../../Families/Seeds/README.md) — the
  per-seed authoring manual the layman's guide here links into.
- [`AEC_FILTER_LIBRARY.md`](../AEC_FILTER_LIBRARY.md) — the visual
  styling layer that drives how all this geometry reads on plan.
- [`MEP_SYMBOL_GUIDE.md`](../MEP_SYMBOL_GUIDE.md) — symbol library
  used by the schedules and tag families.
