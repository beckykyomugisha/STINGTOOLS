> **This document has been consolidated.** See `docs/guides/MEP_FOUNDATION_GUIDE.md` for the complete, up-to-date guide. The content below is kept for historical reference only.

---

# Placement Family Authoring & Setup Requirements (Phase 139.6)

How every Revit family + project setting must be authored for the
STING Placement Centre auto-placement, lux grid, two-phase
conduiting, and in-wall chase routing to land cleanly.

Run **Placement_AuditSetup** in the Placement Centre before the first
Place Fixtures run on a project — it cross-checks every requirement
on this page against the live model and writes a CSV.

## 1 — Universal authoring rules (every category)

| Requirement | Why |
|---|---|
| Family origin = the rule's expected insertion point (NOT geometry centre). For a wall socket: fixing-centre midpoint, not box back. | `CompoundClusterPlacer` and `WALL_FACE_OFFSET` offset from the family origin. |
| Reference Plane `Center (Front/Back)` = wall-face plane, `Defines Origin = Yes`, `Reference = Strong`. | Lets `WALL_FACE_OFFSET` apply `PlasterOffsetMode = Auto` against the right datum. |
| `Always Vertical = Yes` for vertical fittings; `Cuts with Voids When Loaded = Yes` for sleeves, anchors, pendants. | `SleeveEngine` won't cut the wall otherwise. |
| Shared parameters loaded: `STING_BOX_LOCATION_ID`, `STING_NOGGIN_REQUIRED`, `STING_AUTO_PLACED_BOOL`, the `MK_*` catalogue params. | Required for two-phase matching, noggin export, catalogue auto-populate. |
| `STING_FIXTURE_VARIANT_TXT` = rule's `VariantHint` ("FLUSH", "DIMMER", "IP65", etc.). | `FixturePlacementEngine.ResolveSymbol` chains on this value. |
| Type name starts with the gang/size/IP code — e.g. `1G_25mm_IP2X_FlushWhite`. | `FamilyTypeRegex` patterns rely on the type name. |

## 2 — Per-category authoring matrix

### 2.1  Electrical Fixtures (sockets, FCUs, cooker connections)

- **Hosting**: Wall-Hosted (`OneLevelBasedHosted` + Wall).
- **Origin**: fixing-centre midpoint of the **front** face (faceplate plane).
- **Facing**: outward into the room.
- **Reference planes**: `Center (Front/Back)` = wall face; `Center (Left/Right)` for symmetry.
- **Required params**: `MK_BOX_DEPTH_MM`, `MK_FIXING_CENTRES_MM`, `MK_GANG_COUNT`, `MK_CATALOGUE_REF`, `STING_FIXTURE_VARIANT_TXT`.
- **Engine behaviour**: Phase 139.6 `OrientPlacedInstance` flips facing to the room's inward normal automatically.
- **Common mistake**: origin at the back of the box → socket sits 25 mm into the wall.

### 2.2  Lighting Devices (switches, dimmers, 2-way, DALI)

- **Hosting**: Wall-Hosted.
- **Origin**: faceplate midpoint. Toggle/rocker faces room.
- **Facing**: outward into the room.
- **Reference planes**: `Center (Front/Back)` = wall face.
- **Required params**: `MK_BOX_DEPTH_MM`, `STING_FIXTURE_VARIANT_TXT` ("DIMMER" / "DALI" / "2-WAY"). `Family Placement Type = One Level Based Hosted`.
- **Engine behaviour**: Phase 139.6 auto-rotates so the rocker faces into the room (this is what stopped switches landing "facing up" at door centre).
- **Common mistake**: family not `Always Vertical` → switch lies flat on door head when placed at `DOOR_LATCH_SIDE`.

### 2.3  Lighting Fixtures (pendants, downlights, LED panels)

- **Hosting**: Ceiling-Hosted for surface; `Face-Based` for soffit-mount; Floor-/Roof-hosted for slab pendants.
- **Origin**: visible drop centre.
- **Facing**: photometric axis points down (-Z).
- **Reference planes**: `Ceiling`, `Defines Origin = Yes`.
- **Required params**: `MK_CATALOGUE_REF`, `STING_PHOTOMETRIC_LM` (lumens), photometric IES file, `IS_INDIVIDUAL_LUMINAIRE = Yes`.
- **Engine behaviour**: `LightingGridCalculator` snaps to ceiling-tile centres, enforces `MinSpacingMm` (Phase 139.6 LX-1), nudges away from sprinklers (BS 5306), reports uniformity.
- **Common mistake**: pendant authored as `OneLevelBased` un-hosted → no ceiling host found → engine drops it to `ROOM_CENTRE` and stacks.

### 2.4  Plumbing Fixtures (WC, basin, shower, urinal)

- **Hosting**: Wall-Hosted for WC/basin; `Floor-Based` for shower trays.
- **Origin**: centre of the fixture footprint, not the bowl.
- **Facing**: away from the wall (engine does NOT rotate further).
- **Reference planes**: `Center (Left/Right)`, `Center (Front/Back)`, `Reference = Strong`.
- **Required params**: `IS_FIXTURE_BACK_TO_WALL = Yes`, `STING_FIXTURE_VARIANT_TXT` ("WC", "BASIN", "SHOWER"), Plumbing Fixture Connection Type set.
- **Engine behaviour**: Phase 139.6 PF-1 tightened the room-name regex with word boundaries so `wc` no longer matches `walk-in closet` / `wardrobe`. Updated patterns: `\b(bathroom|wc|toilet|en-suite|cloakroom|lavatory)\b`.
- **Common mistake**: WC family with origin on the bowl rather than the footprint → the auto-routing chase pipe lands inside the cistern.

### 2.5  Communication Devices, Data Devices

- **Hosting**: Wall-Hosted.
- **Origin**: faceplate midpoint.
- **Facing**: outward into the room.
- **Required params**: `STING_FIXTURE_VARIANT_TXT` ("HDMI" / "USB-C" / "RJ45" / "RJ12"), `MK_MODULE_PITCH_MM = 25.4` for Grid Plus modules.
- **Common mistake**: authored as Generic Model rather than the matching category → category filter rejects, engine warns "no FamilySymbol found".

### 2.6  Fire Alarm Devices

- **Hosting**: Wall-Hosted (MCP, sounder, beacon); Ceiling-Hosted (smoke, heat).
- **Origin**: faceplate (MCP/sounder) or sensor head (smoke/heat).
- **Required params**: `BS5839_DEVICE_KIND` ("MCP" / "SOUNDER" / "SMOKE" / "HEAT"), `STING_FIXTURE_VARIANT_TXT`.
- **Coverage**: smoke head `Coverage Radius = 7500 mm`, heat = `5300 mm` per BS 5839-1:2025.
- **Engine behaviour**: rules with `CoverageRadiusMm > 0` use the lux-grid algorithm with `GuaranteeCoverage` to fill to 100 %.
- **Common mistake**: smoke authored as Wall-Hosted → `LightingGridCalculator.CheckStructuralFixing` can't find a ceiling host → all coverage rules silently skip.

### 2.7  Sprinklers

- **Hosting**: Ceiling-Hosted (or Face-Based).
- **Origin**: deflector centre. K-factor in the type.
- **Required params**: `BS_5306_K_FACTOR`, `Coverage Radius`, `STING_FIXTURE_VARIANT_TXT` ("UPRIGHT" / "PENDANT" / "CONCEALED").
- **Facing**: down for pendants, up for upright.
- **Engine behaviour**: `LightingGridCalculator.CheckSprinklerSeparation` enforces 600 mm BS 5306 separation between every grid pendant and any sprinkler in the room; nudges first, drops last.
- **Common mistake**: family not loaded → engine sees zero sprinklers → 600 mm check is a silent no-op.

### 2.8  Conduits — first-fix square / BESA round boxes

- **Hosting**: Wall-Hosted (square box) or Face-Based (BESA, on slab soffit).
- **Origin**: box centre.
- **Reference planes**: `Center (Front/Back)` = wall face / soffit face.
- **Required params**: `STING_BOX_LOCATION_ID` shared param (Text), `MK_CATALOGUE_REF`.
- **Family naming**: must match `BoxFamilyTypeRegex` from the rule. Conventions:
  - BESA round 36 mm → `Conduit_BESA_Round` family + `57D_36mm` type.
  - BESA round 47 mm deep → `Conduit_BESA_Round` family + `57D_47mm_Deep` type.
  - Square outlet 75 mm × 44 mm → `Conduit_Square_Outlet` family + `75sq_44mm` type.
  - Square outlet 75 mm × 57 mm deep → `Conduit_Square_Outlet` family + `75sq_57mm` type.
- **Common mistake**: family name doesn't match the regex → 14+ rules fall to "no first-fix box family matched" warnings (your screenshot).

### 2.9  Junction Boxes (5 A / 20 A / 30 A / IP66)

- **Hosting**: Face-Based (slab soffit / wall).
- **Origin**: box centre.
- **Required params**: `STING_FIXTURE_VARIANT_TXT` ("5A_3T" / "20A_5T" / "30A_3T" / "20A_WP_IP66"), `MK_CATALOGUE_REF`.
- **Common mistake**: authored as Generic Model rather than Junction Boxes / Electrical Fixtures category.

### 2.10  Air Terminals

- **Hosting**: Face-Based (ceiling / wall).
- **Origin**: diffuser centre. Air-flow axis along throw direction.
- **Reference planes**: `Diffuser face` = `Defines Origin`.
- **Required params**: `STING_FIXTURE_VARIANT_TXT` ("LINEAR_SLOT" / "4-WAY" / "JET" / "EXTRACT"), `Air Flow`.
- **Facing**: 4-way diffusers no rotation; linear slots align to the longest wall.
- **Common mistake**: family not loaded → all Mechanical-pack rules skip silently.

### 2.11  Pipes (chase / waste / cold-water)

- **Hosting**: self-hosted segments.
- **PipeType**: must have **Routing Preferences > Bend / Tee / Cross fittings** assigned, else `Pipe.Create` segments don't auto-elbow at corners.
- **Required**: insulation type set on PipeType when the rule declares `ObstructionClearanceMm` (insulation thickness).

## 3 — Project setup checklist

| Item | What it does | Symptom if missing |
|---|---|---|
| Run `Placement_AutoPopulateCatalogue` once after loading MK families | Walks every loaded `FamilySymbol`, harvests `MK_*` shared params, writes `STING_MANUFACTURER_CATALOGUE.json` | `ScoreManufacturerResolution` returns 0 / 0.5 for every rule. |
| Bind `STING_BOX_LOCATION_ID` to MEP / Lighting / Electrical / Junction-Box categories | Two-phase first-fix → second-fix matching | Engine warns "Two-phase matching will run in degraded mode". |
| Bind `STING_NOGGIN_REQUIRED` (Yes/No) to Lighting Fixtures | Noggin export tracks pendants needing structural fixings | `NogginRequirementExportCommand` returns zero rows. |
| Load BESA round box family (`Conduit_BESA_Round` types `57D_36mm`, `57D_47mm`) | First-fix box for every pendant / downlight | 14 rules silently skipped. |
| Load `STING_SLEEVE_ROUND` family | `InWallChaseRouter.AutoSleeve` post-step | `SleeveEngine` falls back to dry-run; chase pipes leave uncut walls. |
| Add Phases `Construction` and `Handover` | Two-phase routing decoupling | First-fix and second-fix both land in the active phase — coordination breaks. |
| Set Project `BuildingType` (Residential / Office / Healthcare / Education) via the Placement Centre profile | Filters discipline packs at load time | Healthcare HTM rules fire on residential projects. |

## 4 — Phase 139.6 code fixes triggered by this audit

- **SW-1** (`FixturePlacementEngine.OrientPlacedInstance`): `RotationDeg` is now applied; wall-hosted families auto-flip facing toward the room interior. Switches no longer land "facing up" at door centre.
- **LX-1** (`LightingGridCalculator.EnforceMinSpacing`): the lux grid now drops grid points closer than `rule.MinSpacingMm` after the BS 5306 sprinkler nudge but before uniformity check. Lights stop stacking in narrow rooms.
- **PF-1** (`STING_PLACEMENT_RULES*.json`): every `(?i)wc|toilet|...` regex has been tightened to `(?i)\b(wc|toilet|bathroom|shower|en-suite|cloakroom|lavatory|powder room)\b`. Toilets stop landing in walk-in closets / wardrobes / utility rooms.

## 5 — When to re-run the audit

- After loading any new family library.
- After binding new shared parameters.
- After changing the active phase.
- After running `Placement_ImportRulesExcel` (the audit checks rule count + integrity).
- Before committing a federation model — the audit's CSV is the deliverable.

