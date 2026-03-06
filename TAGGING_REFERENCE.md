# STINGTOOLS Comprehensive Tagging Reference

> Version 3.0 | 2026-03-06 | ISO 19650 Asset Tagging System
> Covers all tagging infrastructure, TAG7 narrative assembly, formula dependency chains, container definitions, and manual creation procedures.

---

## Table of Contents

1. [Tag Format Overview](#1-tag-format-overview)
2. [The 8 Source Tokens](#2-the-8-source-tokens)
3. [Token Auto-Detection Intelligence Layers](#3-token-auto-detection-intelligence-layers)
4. [Tag Containers (36 Parameters)](#4-tag-containers-36-parameters)
5. [TAG7 Rich Narrative System](#5-tag7-rich-narrative-system)
6. [Formula Dependency Chain (199 Formulas)](#6-formula-dependency-chain-199-formulas)
7. [Tagging Workflow Commands](#7-tagging-workflow-commands)
8. [Recent Changes & New Features](#8-recent-changes--new-features)
9. [Efficiency Review & Optimisation Notes](#9-efficiency-review--optimisation-notes)
10. [Manual Tag Creation Guide](#10-manual-tag-creation-guide)

---

## 1. Tag Format Overview

Every taggable element receives an **8-segment ISO 19650-compliant asset tag**:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
  M  - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

| Property | Value | Source |
|----------|-------|--------|
| Separator | `-` (hyphen) | `ParamRegistry.Separator` / `project_config.json` |
| SEQ Padding | 4 digits (0001-9999) | `ParamRegistry.NumPad` |
| Segment Order | DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ | `ParamRegistry.SegmentOrder` |
| Collision Limit | 10,000 auto-increments | `TagConfig.MaxCollisionDepth` |
| Stored In | `ASS_TAG_1_TXT` (TAG1) | Primary tag parameter |

### Completeness Rules

| Check | Method | Description |
|-------|--------|-------------|
| Complete | `TagConfig.TagIsComplete(tag)` | All 8 segments present and non-empty |
| Fully Resolved | `TagConfig.TagIsFullyResolved(tag)` | Complete + no placeholders (XX, ZZ, 0000) |
| ISO Valid | `ISO19650Validator.ValidateTagFormat(tag)` | All segments pass code validation |

---

## 2. The 8 Source Tokens

### Token 0: DISC (Discipline)
**Parameter**: `ASS_DISCIPLINE_COD_TXT`
**Valid Codes**: M, E, P, A, S, FP, LV, G

| Code | Discipline | Categories |
|------|-----------|------------|
| M | Mechanical | Air Terminals, Ducts, Duct Fittings, Duct Accessories, Flex Ducts, Mechanical Equipment, Pipes*, Pipe Fittings*, Pipe Accessories*, Flex Pipes* |
| E | Electrical | Electrical Equipment, Electrical Fixtures, Lighting Fixtures, Lighting Devices, Conduits, Conduit Fittings, Cable Trays, Cable Tray Fittings |
| P | Plumbing | Plumbing Fixtures (+ Pipes* when SYS = DCW/DHW/SAN/RWD/GAS/HWS) |
| A | Architectural | Doors, Windows, Walls, Floors, Ceilings, Roofs, Rooms, Furniture, Furniture Systems, Casework, Railings, Stairs, Ramps, Curtain Panels, Curtain Wall Mullions, Curtain Systems |
| S | Structural | Structural Columns, Structural Framing, Structural Foundations, Columns |
| FP | Fire Protection | Sprinklers, Fire Alarm Devices (+ Pipes* when SYS = FP) |
| LV | Low Voltage | Communication Devices, Data Devices, Nurse Call Devices, Security Devices, Telephone Devices |
| G | Generic | Generic Models, Specialty Equipment, Medical Equipment |

*\*Pipe categories are dynamically re-assigned from M to P/FP based on connected MEP system (see System-Aware DISC Correction below).*

**System-Aware DISC Correction** (`TagConfig.GetSystemAwareDisc`):
Pipe-category elements default to M but are corrected when the detected SYS indicates a different discipline:
- DCW/DHW/SAN/RWD/GAS/HWS → P (Plumbing)
- FP → FP (Fire Protection)
- HVAC → M (stays Mechanical)

### Token 1: LOC (Location)
**Parameter**: `ASS_LOC_TXT`
**Valid Codes**: BLD1, BLD2, BLD3, EXT, XX

**Auto-Detection** (`SpatialAutoDetect.DetectLoc`):
1. Room name/number patterns: "BLD1", "Building 1", "Block A" → BLD1
2. Room number prefix: "B1-101" → BLD1
3. Exterior heuristic: element not in any room + family name contains "External"/"Outdoor"/"Bollard"/"Floodlight" → EXT
4. Workset name fallback: workset contains "BLD1" etc.
5. Project-level default: `SpatialAutoDetect.DetectProjectLoc(doc)` reads Project Information (BuildingName, Name, Address)
6. Final default: BLD1

### Token 2: ZONE (Zone)
**Parameter**: `ASS_ZONE_TXT`
**Valid Codes**: Z01, Z02, Z03, Z04, ZZ, XX

**Auto-Detection** (`SpatialAutoDetect.DetectZone`):
1. Room Department parameter: "Zone 1"/"Wing A"/"North" → Z01
2. Room name patterns: "Z01", "Zone 2", "Wing B" → Z02
3. Room number prefix: "Z01-101" → Z01
4. Directional mapping: North→Z01, South→Z02, East→Z03, West→Z04
5. Workset name fallback
6. Final default: Z01

### Token 3: LVL (Level)
**Parameter**: `ASS_LVL_COD_TXT`
**Valid Codes**: L00-L99, GF, LG, UG, B1-B9, SB, RF, PH, AT, TR, POD, MZ, PL, XX

**Auto-Detection** (`ParameterHelpers.GetLevelCode`):

| Revit Level Name | LVL Code |
|------------------|----------|
| Level 1, Level 01 | L01 |
| Ground, Ground Floor | GF |
| Lower Ground, LG | LG |
| Upper Ground, UG | UG |
| Basement, B1, B2 | B1, B2 |
| Sub-Basement, SB | SB |
| Roof, RF | RF |
| Penthouse, PH | PH |
| Attic, AT | AT |
| Terrace, TR | TR |
| Podium, POD | POD |
| Mezzanine, MZ | MZ |
| Plant Room | PL |
| 1st Floor, First Floor | L01 |
| 2nd Floor, Second Floor | L02 |
| Unrecognised | XX |

When used in `BuildAndWriteTag`, XX is replaced with L00 as a guaranteed default.

### Token 4: SYS (System)
**Parameter**: `ASS_SYSTEM_TYPE_TXT`
**Valid Codes**: HVAC, HWS, DHW, DCW, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN

**6-Layer MEP System Detection** (`TagConfig.GetMepSystemAwareSysCode`):

| Layer | Source | Example |
|-------|--------|---------|
| 1. Connector | `FamilyInstance.MEPModel.ConnectorManager` → system name | "Supply Air" → HVAC |
| 2. System Type Param | `RBS_DUCT_SYSTEM_TYPE_PARAM` / `RBS_PIPING_SYSTEM_TYPE_PARAM` | "Return Air" → HVAC |
| 3. Electrical Circuit | `RBS_ELEC_CIRCUIT_PANEL_PARAM` panel name | "FA-PANEL-01" → FLS |
| 4. Family Name | Pattern matching on family name | "Exhaust Fan" → HVAC |
| 5. Room Type | Room name/department inference | "Server Room" → ICT |
| 6. Category Fallback | `SysMap[categoryName]` | Plumbing Fixtures → DCW |

**Discipline Default SYS** (`TagConfig.GetDiscDefaultSysCode`):
M→HVAC, E→LV, P→DCW, A→ARC, S→STR, FP→FP, LV→LV, G→GEN

**System Name Mapping** (MEP system name → SYS code):

| System Name Pattern | SYS Code |
|--------------------|----------|
| Supply Air, SA | HVAC |
| Return Air, RA | HVAC |
| Exhaust, Extract, EA | HVAC |
| Fresh Air, Outside Air, OA | HVAC |
| Chilled, Cooling, CHW | HVAC |
| FCU | HVAC |
| CW (pipe categories) | DCW |
| CW (non-pipe) | HVAC |
| Hot Water, DHW, HWS | HWS |
| Heating, LTHW, MTHW, Radiator | HWS |
| Cold Water, CWS, DCW, Mains, Potable | DCW |
| Sanitary, Waste, Soil, Drain, Sewage, SVP | SAN |
| Rainwater, Storm, Surface Water, RWP | RWD |
| Gas, Natural Gas, LPG | GAS |
| Fire, Sprinkler, Wet Riser, Dry Riser | FP |

### Token 5: FUNC (Function)
**Parameter**: `ASS_FUNC_TXT`
**Valid Codes**: SUP, HTG, DCW, SAN, RWD, GAS, FP, PWR, FLS, COM, ICT, NCL, SEC, FIT, STR, GEN, EXH, RTN, FRA, DHW

**Smart FUNC** (`TagConfig.GetSmartFuncCode`):
For HVAC systems, differentiates subsystem function:
- Supply ductwork/diffusers → SUP
- Return grilles/ductwork → RTN
- Exhaust/extract fans → EXH
- Fresh/outside air → FRA

For HWS systems:
- Heating circuits (LTHW, radiators) → HTG
- Domestic hot water (calorifiers, water heaters) → DHW

Basic `FuncMap` lookup:

| SYS | FUNC |
|-----|------|
| HVAC | SUP |
| HWS | HTG |
| DCW | DCW |
| DHW | DCW |
| SAN | SAN |
| RWD | RWD |
| GAS | GAS |
| FP | FP |
| LV | PWR |
| FLS | FLS |
| COM | COM |
| ICT | ICT |
| NCL | NCL |
| SEC | SEC |
| ARC | FIT |
| STR | STR |
| GEN | GEN |

### Token 6: PROD (Product)
**Parameter**: `ASS_PRODCT_COD_TXT`
**Valid**: 2-4 uppercase alphanumeric characters

**Family-Aware PROD** (`TagConfig.GetFamilyAwareProdCode`):
Inspects the family name to return specific product codes instead of generic category codes:

| Category | Family Pattern | PROD |
|----------|---------------|------|
| Mechanical Equipment | FCU, Fan Coil | FCU |
| Mechanical Equipment | VAV, Variable Air | VAV |
| Mechanical Equipment | Chiller, CHR | CHR |
| Mechanical Equipment | Boiler, BLR | BLR |
| Mechanical Equipment | Pump, PMP | PMP |
| Mechanical Equipment | Fan, EXF | FAN |
| Mechanical Equipment | HRU, Heat Recovery | HRU |
| Mechanical Equipment | Split, Cassette | SPL |
| Mechanical Equipment | AHU, Air Handling | AHU |
| Mechanical Equipment | Damper | DAM |
| Mechanical Equipment | Cooling Tower | CLT |
| Mechanical Equipment | VFD, Inverter | VFD |
| Electrical Equipment | MCC, Motor Control | MCC |
| Electrical Equipment | MSB, Main Switch | MSB |
| Electrical Equipment | SWB, Switchboard | SWB |
| Electrical Equipment | UPS | UPS |
| Electrical Equipment | Transformer, TRF | TRF |
| Electrical Equipment | Generator | GEN |
| Electrical Equipment | ATS, Auto Transfer | ATS |
| Electrical Equipment | SPD, Surge | SPD |
| Electrical Equipment | RCD, Residual | RCD |
| Electrical Equipment | DB, Distribution | DB |
| Lighting Fixtures | Emergency, Exit | EML |
| Lighting Fixtures | Track | TRK |
| Lighting Fixtures | Decorative, Pendant | DEC |
| Lighting Fixtures | Downlight, Recessed | DWN |
| Lighting Fixtures | Linear, Batten | LIN |
| Lighting Fixtures | Spotlight | SPT |
| Lighting Fixtures | Wall Wash | WSH |
| Lighting Fixtures | Bollard | BOL |
| Lighting Fixtures | Uplight | UPL |
| Lighting Fixtures | Flood | FLD |
| Plumbing Fixtures | WC, Toilet | WC |
| Plumbing Fixtures | Basin, Wash Hand | WHB |
| Plumbing Fixtures | Urinal | URN |
| Plumbing Fixtures | Sink | SNK |
| Plumbing Fixtures | Shower | SHW |
| Plumbing Fixtures | Bath | BTH |
| Plumbing Fixtures | Drinking Fountain | DRK |
| Plumbing Fixtures | Grease Trap | TRP |
| Fire Alarm Devices | Smoke Detector | SML |
| Fire Alarm Devices | Manual Call Point | MCP |
| Fire Alarm Devices | Bell, Sounder | BLL |
| Fire Alarm Devices | Strobe, Beacon | STB |
| Fire Alarm Devices | Heat Detector | HTD |
| Pipe Accessories | Balancing Valve | BLV |
| Pipe Accessories | TRV, Radiator Valve | TRV |
| Pipe Accessories | Isolation, Gate, Ball | IVL |
| Pipe Accessories | Check, Non-Return | NRV |
| Pipe Accessories | Pressure Reducing | PRV |
| Pipe Accessories | Strainer, Filter | STN |

**Category Fallback** (`ProdMap`): If no family pattern matches, the category default applies (e.g., Doors→DR, Windows→WIN, Walls→WL).

### Token 7: SEQ (Sequence)
**Parameter**: `ASS_SEQ_NUM_TXT`
**Format**: 4-digit zero-padded (0001-9999)

**Sequence Grouping**: SEQ numbers are unique within `{DISC}_{SYS}_{LVL}` groups.
- Example: M_HVAC_L02 starts at 0001 and increments per element
- New tagging continues from existing highest SEQ via `GetExistingSequenceCounters(doc)`

**Collision Handling** (`TagCollisionMode`):
| Mode | Behaviour |
|------|-----------|
| AutoIncrement (default) | If generated tag exists in index, increment SEQ until unique |
| Skip | Leave already-tagged elements untouched |
| Overwrite | Regenerate all tokens fresh, remove old tag from index |

---

## 3. Token Auto-Detection Intelligence Layers

The tagging system uses a multi-layer intelligence pipeline. Each token has fallback chains ensuring **every element gets a valid tag with zero manual input**.

### Guaranteed Defaults

| Token | Detection Chain | Final Default |
|-------|----------------|---------------|
| DISC | Category→DiscMap | "A" |
| LOC | Room→Project Info→Workset→Config | "BLD1" |
| ZONE | Room Dept→Room Name→Room Num→Workset | "Z01" |
| LVL | Element Level→Name Parse→XX→L00 | "L00" |
| SYS | Connector→SysType→Circuit→Family→Room→Category | Disc default (HVAC/LV/DCW/ARC/STR/GEN) |
| FUNC | SmartFunc(HVAC subsystem)→FuncMap→ | "GEN" |
| PROD | FamilyName→ProdMap→ | "GEN" |
| SEQ | Counter[DISC_SYS_LVL]++ | "0001" |
| STATUS | Phase→Workset→ | "NEW" |
| REV | Project Revisions→ | "P01" |

### Layer 9: Adjacent Element SYS Inference (ENH-004)
When all 6 standard layers return empty, a BoundingBoxIntersectsFilter at 500mm radius checks nearby elements. If 80%+ of neighbours share the same SYS code, that code is adopted with confidence 0.3.

---

## 4. Tag Containers (36 Parameters)

After tokens are populated, they are assembled into **36 container parameters** via index arrays. Each container selects specific token slots and joins them with a separator.

### Universal Containers (All Categories)

| Container | Parameter | Token Indices | Formula | Example |
|-----------|-----------|---------------|---------|---------|
| TAG1 | `ASS_TAG_1_TXT` | [0,1,2,3,4,5,6,7] | DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |
| TAG2 | `ASS_TAG_2_TXT` | [0,6,7] | DISC-PROD-SEQ | M-AHU-0003 |
| TAG3 | `ASS_TAG_3_TXT` | [1,2,3] | LOC-ZONE-LVL | BLD1-Z01-L02 |
| TAG4 | `ASS_TAG_4_TXT` | [4,5] | SYS-FUNC | HVAC-SUP |
| TAG5 | `ASS_TAG_5_TXT` | [0,1,2,3] | DISC-LOC-ZONE-LVL | M-BLD1-Z01-L02 |
| TAG6 | `ASS_TAG_6_TXT` | [4,5,6,7] | SYS-FUNC-PROD-SEQ | HVAC-SUP-AHU-0003 |
| TAG7 | `ASS_TAG_7_TXT` | *narrative* | Rich descriptive text | (see Section 5) |

### Discipline-Specific Containers

| Container | Parameter | Categories | Token Indices |
|-----------|-----------|------------|---------------|
| HVC_EQP_TAG | `HVC_EQP_TAG_TXT` | Mechanical Equipment | [0,4,5,6,7] |
| HVC_DCT_TAG | `HVC_DCT_TAG_TXT` | Ducts, Duct Fittings, Duct Accessories, Flex Ducts | [0,4,5,6,7] |
| HVC_FLX_TAG | `HVC_FLX_TAG_TXT` | Flex Ducts | [0,4,5,6,7] |
| ELC_EQP_TAG | `ELC_EQP_TAG_TXT` | Electrical Equipment | [0,4,5,6,7] |
| ELE_FIX_TAG | `ELE_FIX_TAG_TXT` | Electrical Fixtures | [0,4,5,6,7] |
| LTG_FIX_TAG | `LTG_FIX_TAG_TXT` | Lighting Fixtures, Lighting Devices | [0,4,5,6,7] |
| ELC_CDT_TAG | `ELC_CDT_TAG_TXT` | Conduits, Conduit Fittings | [0,4,5,6,7] |
| ELC_CTR_TAG | `ELC_CTR_TAG_TXT` | Cable Trays, Cable Tray Fittings | [0,4,5,6,7] |
| PLM_EQP_TAG | `PLM_EQP_TAG_TXT` | Plumbing Fixtures | [0,4,5,6,7] |
| FLS_DEV_TAG | `FLS_DEV_TAG_TXT` | Fire Alarm Devices, Sprinklers | [0,4,5,6,7] |
| COM_DEV_TAG | `COM_DEV_TAG_TXT` | Communication Devices | [0,4,5,6,7] |
| SEC_DEV_TAG | `SEC_DEV_TAG_TXT` | Security Devices | [0,4,5,6,7] |
| NCL_DEV_TAG | `NCL_DEV_TAG_TXT` | Nurse Call Devices | [0,4,5,6,7] |
| ICT_DEV_TAG | `ICT_DEV_TAG_TXT` | Data Devices | [0,4,5,6,7] |
| MAT_TAG_1-6 | `MAT_TAG_{n}_TXT` | Material-tagged categories | various |

### Container Assembly Logic

```
ReadTokenValues(element) → [DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ]
                                0     1     2     3    4     5     6     7

For each container:
  1. Select tokens by index array: e.g., TAG2 = [0,6,7] → pick slots 0, 6, 7
  2. Join with separator: "M" + "-" + "AHU" + "-" + "0003" = "M-AHU-0003"
  3. Apply optional prefix/suffix
  4. Write to parameter via SetString()
```

**Performance**: Tokens read once per element → 6-10 containers assembled via O(1) index lookup → all written in single transaction.

### Paragraph Containers (39 Parameters)

Category-specific TAG7 narrative containers for detailed specifications:

| Parameter | Category | Content |
|-----------|----------|---------|
| `ARCH_TAG_7_PARA_WALL_TXT` | Walls | Full wall specification narrative |
| `ARCH_TAG_7_PARA_FLOOR_TXT` | Floors | Floor assembly narrative |
| `ARCH_TAG_7_PARA_DOOR_TXT` | Doors | Door specification narrative |
| `ARCH_TAG_7_PARA_WIN_TXT` | Windows | Window specification narrative |
| `ARCH_TAG_7_PARA_ROOM_TXT` | Rooms | Space designation narrative |
| `ARCH_TAG_7_PARA_CEIL_TXT` | Ceilings | Ceiling assembly narrative |
| `ARCH_TAG_7_PARA_ROOF_TXT` | Roofs | Roof assembly narrative |
| `STR_TAG_7_PARA_FDN_TXT` | Foundations | Foundation specification |
| `STR_TAG_7_PARA_COL_TXT` | Columns | Column specification |
| `HVC_TAG_7_PARA_SPEC_TXT` | HVAC Equipment | HVAC technical specification |
| `ELC_TAG_7_PARA_PANEL_TXT` | Electrical Panels | Panel specification |
| `LTG_TAG_7_PARA_SPEC_TXT` | Lighting | Lighting specification |
| `PLM_TAG_7_PARA_FIXTURE_TXT` | Plumbing Fixtures | Fixture specification |
| `FLS_TAG_7_PARA_FA_TXT` | Fire Alarm | Fire alarm specification |
| `FLS_TAG_7_PARA_SPR_TXT` | Sprinklers | Sprinkler specification |
| ... | (26 more) | Various disciplines |

Controlled by visibility parameters:
- `TAG_PARA_STATE_1_BOOL` → Compact depth (Section A only)
- `TAG_PARA_STATE_2_BOOL` → Standard depth (Sections A+B)
- `TAG_PARA_STATE_3_BOOL` → Comprehensive (all sections)
- `TAG_WARN_VISIBLE_BOOL` → Show/hide warning text
- `TAG_WARN_SEVERITY_FILTER_TXT` → CRITICAL, HIGH, MEDIUM, ALL

---

## 5. TAG7 Rich Narrative System

TAG7 is a comprehensive human-readable asset narrative built from element data. It uses 6 independently stylable sub-sections (A-F).

### TAG7 Section Structure

| Section | Parameter | Content | Markup Style |
|---------|-----------|---------|-------------|
| A: Identity | `ASS_TAG_7A_TXT` | Asset name, PROD code, manufacturer, model, size | **Bold**, Blue |
| B: System | `ASS_TAG_7B_TXT` | System type description, function code, spatial context | *Italic*, Green |
| C: Spatial | `ASS_TAG_7C_TXT` | Room name, room number, department, grid reference | Normal, Orange |
| D: Lifecycle | `ASS_TAG_7D_TXT` | Status, revision, origin, project, volume | Normal, Red |
| E: Technical | `ASS_TAG_7E_TXT` | Discipline-specific performance data | **Bold**, Purple |
| F: Classification | `ASS_TAG_7F_TXT` | Uniformat, OmniClass, keynote, cost, ISO tag | *Italic*, Grey |

### TAG7 Connecting Strings

The full TAG7 narrative joins sections with **pipe separators** (`|`):

```
Section A | Section B | Section C | Section D | Section E | Section F
```

**Markup Tokens** used in TAG7 (for rich rendering in WPF/TextNote):
- `«H»text«/H»` — Header / Bold / Underline
- `«L»text«/L»` — Label / Italic / Muted
- `«V»text«/V»` — Value / Accent colour
- `«S»|«/S»` — Section separator (pipe between sections)

**Plain sections** (TAG7A-TAG7F) contain text without markup tokens — used in tag family labels where each label can have its own font/size/colour.

**Marked-up narrative** (TAG7) uses markup tokens for rich rendering:
```
«H»AHU (Air Handling Unit)«/H» «L»Mfr:«/L» «V»Trane«/V» «L»Model:«/L» «V»XL-15i«/V»
«S»|«/S» «H»HVAC«/H» «L»Function:«/L» «V»Supply«/V» «V»Zone Z01«/V», «V»Level L02«/V»
«S»|«/S» «L»Located in«/L» «V»Mechanical Room«/V» «L»(Room 215)«/L»
«S»|«/S» «L»Status:«/L» «V»NEW«/V» «L»Rev:«/L» «V»P01«/V»
«S»|«/S» «L»Airflow:«/L» «V»5,000 L/s«/V» «L»Pressure:«/L» «V»800 Pa«/V»
«S»|«/S» «L»Uniformat:«/L» «V»23 40 00«/V» «L»ISO Tag:«/L» «H»M-BLD1-Z01-L02-HVAC-SUP-AHU-0003«/H»
```

### TAG7 Section Building Rules

**Section A — Identity Header**:
```
{CategoryName} {PROD} ({ProductDescription})
[if MFR:] manufactured by {MFR}
[if MODEL:] Model {MODEL}
[if SIZE:] Size {SIZE}
[if FAMILY_NAME:] Family: {FAMILY_NAME}
[if TYPE_NAME:] Type: {TYPE_NAME}
[if DESC:] {DESC}
```

**Section B — System & Function**:
```
{SystemFullName} {FunctionDescription}
serving Zone {ZONE}, Level {LVL} of Building {LOC}
```

**Section C — Spatial Context**:
```
Located in {ROOM_NAME} (Room {ROOM_NUM})
[if DEPT:] Department: {DEPT}
[if GRID_REF:] Grid Reference {GRID_REF}
```

**Section D — Lifecycle & Status**:
```
Status: {STATUS}, Revision: {REV}
[if ORIGIN:] Origin: {ORIGIN}
[if PROJECT:] Project: {PROJECT}
[if VOLUME:] Volume: {VOLUME}
```

**Section E — Technical Specs** (discipline-specific):

| Discipline | Parameters Included |
|-----------|-------------------|
| Electrical | ELC_POWER, ELC_VOLTAGE, ELC_CIRCUIT_NR, ELC_PHASES, ELC_PNL_NAME, ELC_IP_RATING |
| Lighting | LTG_WATTAGE, LTG_LUMENS, LTG_EFFICACY, LTG_LAMP_TYPE |
| HVAC | HVC_DUCT_FLOW, HVC_VELOCITY, HVC_PRESSURE, HVC_AIRFLOW, HVC_DUCT_WIDTH, HVC_DUCT_HEIGHT |
| Plumbing | PLM_PIPE_FLOW, PLM_PIPE_SIZE, PLM_VELOCITY, PLM_FLOW_RATE |
| Architectural | WALL_HEIGHT, WALL_LENGTH, WALL_THICKNESS, DOOR_WIDTH, DOOR_HEIGHT, FIRE_RATING |
| Structural | STRUCT_TYPE, FIRE_RATING |

**Section F — Classification & Reference**:
```
[if UNIFORMAT:] Uniformat: {UNIFORMAT} ({UNIFORMAT_DESC})
[if OMNICLASS:] OmniClass: {OMNICLASS}
[if KEYNOTE:] Keynote: {KEYNOTE}
[if TYPE_MARK:] Type Mark: {TYPE_MARK}
[if COST:] Unit Cost: {COST}
ISO 19650 Tag: {TAG1}
```

### Paragraph Narratives (Category-Specific)

For detailed specifications, paragraph containers use the formula system (see Section 6, rows 200-208 in FORMULAS_WITH_DEPENDENCIES.csv). These generate multi-sentence descriptions controlled by `TAG_PARA_STATE_3_BOOL`.

**Example — Wall paragraph** (`ARCH_TAG_7_PARA_WALL_TXT`):
> "This wall assembly forms part of the building fabric and has been carefully specified to fulfil the project requirements for spatial separation and environmental control. The wall serves as a load-bearing element within the building layout. The overall construction measures 300 millimetres in total depth... The principal material incorporated within the wall build-up is concrete block... In terms of thermal performance, the wall has been designed to achieve a heat transfer rate of 0.35 watts... From a fire safety perspective, this wall construction has been tested and certified to maintain its structural integrity for 120 minutes..."

### Presentation Modes

| Mode | Content | TAG7 Depth |
|------|---------|-----------|
| Compact | TAG1 + TAG2 only | State 1 |
| Technical | TAG1-4 + Section E specs | State 2 |
| Full Specification | All TAG1-7 + paragraphs | State 3 |
| Presentation | Formatted for display/print | State 2 |
| BOQ | Cost-focused + quantities | State 2 |

---

## 6. Formula Dependency Chain (199 Formulas)

The formula engine (`FormulaEvaluatorCommand`) evaluates 199 formulas in dependency order (levels 0→6). Each level depends only on parameters computed at lower levels.

### Batch 1: Foundation Formulas (Rows 1-70, Level 0)

These formulas depend only on **raw Revit geometry or direct input parameters** — no prior formula outputs needed.

| Discipline | Count | Key Formulas |
|-----------|-------|-------------|
| ARCHITECTURAL | 12 | Room volume, ceiling area, door/window areas, wall U-value, paint area, stair ratios, ramp slope |
| CONSTRUCTION | 9 | Concrete volume, formwork quantities (plywood/props/timber/release agent), brick count, net masonry area, rebar weight |
| COSTING | 3 | Lifecycle cost, total cost (qty×price), labour+material total |
| ELECTRICAL | 12 | Circuit current (I=P/V√3×PF), diversity factor, annual energy, earth resistance, lighting illuminance, power density, tag concatenations |
| FIRE | 10 | Detector count (1/36m²), alarm circuits, travel distance, fire resistance, sprinkler coverage/flow, occupant load, travel check |
| HVAC | 10 | Cooling capacity (100W/m²), duct area, pipe flowrate/pressure/length, tag concatenations |
| MULTI | 6 | Tag concatenation formulas (ASS_ID + ASS_TAG_1, etc.) |
| PLUMBING | 16 | Pipe flow (Q=πr²V), drainage velocity (Manning), pipe sizing, insulation thickness, support spacing, vent sizing, head calculations |
| REGULATORY | 4 | Accessible clear width, ramp gradient check, threshold height, turning radius |

**Geometry-dependent formulas** (use Revit built-in Width/Height/Length/Diameter): BLE_ELE_AREA, CST_S_CON_VOLUME, HVC_DUCT_AREA, HVC_PIPE_FLOWRATE, HVC_PIPE_LENGTH, PLM_PPE_LENGTH

### Batch 2: Derived Formulas (Rows 71-149, Levels 0-1)

Level 0 continuation (rows 71-97):

| Discipline | Key Formulas |
|-----------|-------------|
| PLUMBING (cont.) | Pipe nominal size (mm→inch), facade water penetration, head, heater efficiency, hot water capacity (m³→gal) |
| REGULATORY | Natural light ratio, parking spaces, plot coverage, plot ratio, room height check, setback check |
| SUSTAINABILITY | Daylight factor (Sabine), energy use intensity, natural ventilation area (5% floor), rainwater harvest, water rating, R-value, solar absorptance |

Level 1 formulas (rows 98-149) — depend on Level 0 outputs:

| Discipline | Count | Key Formulas |
|-----------|-------|-------------|
| ARCHITECTURAL | 6 | Ceiling tile qty, paint volume, plaster area, tile area, floor tile qty, window-to-wall ratio |
| CONSTRUCTION | 18 | Block count, fasteners, primer, purlins, putty, roofing sheets, steel (from concrete vol), aggregate, cement bags, sand, concrete wastage, blinding, excavation, hardcore, mortar, rebar wastage |
| COSTING | 4 | Formwork cost total, cost reference, total cost, earthworks |
| ELECTRICAL | 5 | Cable ampacity, conductor size selection, breaker rating, energy cost (UGX), connected load after diversity |
| FIRE | 4 | Detection cost, evacuation time, system demand, escape width |
| HVAC | 2 | Duct flowrate (m³/h), COP efficiency ratio |
| PLUMBING | 2 | Drainage flow rate, valve Cv |
| REGULATORY | 3 | Clear width check, threshold check, turning check |
| SUSTAINABILITY | 9 | Reverberation time, RT60, embodied carbon, recycled content, R-value from U-value, solar heat gain, thermal mass, U-value from R-value |

### Batch 3: Higher-Order Formulas (Rows 150-199, Levels 2-6)

Level 2 (rows 151-182):

| Discipline | Key Formulas |
|-----------|-------------|
| ARCHITECTURAL | Adhesive weight, grout weight, tile quantity |
| CONSTRUCTION | Adhesive, aggregate buffer, concrete total (structural+blinding), grout, mortar from blocks, paint, plaster, water for concrete, backfill, disposal (25% swell), masonry cement, masonry sand |
| COSTING | Concrete material total, reinforcement cost, blocks cost, roofing cost, steel cost, tile cost |
| ELECTRICAL | Voltage drop % |
| FIRE | Tank volume, suppression pipe length, total demand |
| HVAC | Air changes/hr, CFM conversion, duct sound level |
| PLUMBING | Drain fill ratio |
| SUSTAINABILITY | Annual energy cost, carbon footprint (concrete+steel+blocks), heat loss |

Level 3 (rows 183-193):
- Cement bags total (concrete + masonry)
- Plaster cement bags
- Sand total, sand for plaster, water for concrete (m³)
- Earthworks total cost
- Adhesive/aggregate/grout/paint cost totals
- Fire suppression total cost

Level 4 (rows 194-197):
- Masonry material cost total
- Cement cost, plaster cost, sand cost totals

Level 5 (row 198):
- **CST_TOTAL_MATERIAL_COST**: Grand total of all 11 material cost categories (cement + sand + aggregate + steel + blocks + tile + paint + plaster + adhesive + grout + roofing)

Level 6 (row 199):
- **CST_PERCENTAGE_OF_TOTAL_PCT**: Element cost as percentage of project total

### Formula Evaluation Order

```
Level 0 (97 formulas)  → Raw geometry + input params
Level 1 (52 formulas)  → Depends on Level 0 outputs
Level 2 (32 formulas)  → Depends on Level 0-1 outputs
Level 3 (11 formulas)  → Depends on Level 0-2 outputs
Level 4 (4 formulas)   → Depends on Level 0-3 outputs
Level 5 (1 formula)    → Grand total material cost
Level 6 (1 formula)    → Percentage of total
                         + 9 paragraph formulas (TAG7 narratives)
```

**Total**: 199 formulas + 9 paragraph narratives = 208 computed parameters

---

## 7. Tagging Workflow Commands

### Primary Tagging Commands

| Command | Class | Action |
|---------|-------|--------|
| **Auto Tag** | `AutoTagCommand` | Tag elements in active view (view-discipline-filtered) |
| **Batch Tag** | `BatchTagCommand` | Tag ALL elements in entire project |
| **Tag & Combine** | `TagAndCombineCommand` | One-click: auto-detect → populate → tag → combine all 36 containers |
| **Tag New Only** | `TagNewOnlyCommand` | Tag only untagged elements (incremental) |
| **Family-Stage Populate** | `FamilyStagePopulateCommand` | Pre-populate all 7 tokens from category/spatial/family data |
| **Pre-Tag Audit** | `PreTagAuditCommand` | Dry-run: predict tags, collisions, ISO violations |

### Token Writer Commands

| Command | Action |
|---------|--------|
| Set Discipline | Set DISC token (user selection) |
| Set Location | Set LOC token (user selection) |
| Set Zone | Set ZONE token (user selection) |
| Set Status | Set STATUS token (user selection) |
| Assign Numbers | Sequential SEQ numbering within DISC/SYS/LVL groups |
| Build Tags | Rebuild TAG1 from existing token values |
| Combine Parameters | Write tokens to all 36 containers |
| Completeness Dashboard | Per-discipline compliance % with RAG status |

### QA & Validation Commands

| Command | Action |
|---------|--------|
| Validate Tags | ISO 19650 compliance check (all 8 tokens + cross-validation) |
| Find Duplicates | Locate duplicate tag values |
| Fix Duplicates | Auto-resolve duplicates by incrementing SEQ |
| Highlight Invalid | Colour-code missing (red) and incomplete (orange) tags |
| Tag Statistics | Quick counts by DISC/SYS/LVL |
| Audit to CSV | Full tag export to CSV |
| Tag Register Export | 40+ column asset register CSV |

### Tag Operation Commands

| Command | Action |
|---------|--------|
| Tag Selected | Tag selected elements only |
| Delete Tags | Clear all 15 tag params from selection |
| Renumber | Re-sequence within DISC/SYS/LVL groups |
| Copy Tags | Copy from first selected to all others |
| Swap Tags | Swap all values between 2 elements |
| Re-Tag | Force re-derive all tokens |

---

## 8. Recent Changes & New Features

### Workflow Engine (`Core/WorkflowEngine.cs`) — NEW
JSON-based command chain orchestration with 3 built-in presets:

| Preset | Steps | Purpose |
|--------|-------|---------|
| Project Kickoff | 25 | Full project setup: params → materials → families → schedules → templates → tagging |
| Daily QA Sync | 6 | TagNewOnly → TagChanged → Validate → ComplianceScan → AnomalyAutoFix → Report |
| Document Package | 6 | Validate → BatchTag → BOQExport → DrawingRegister → IFCExport → DocPackage |

Features: per-step timing, Escape cancellation, TransactionGroup rollback on failure.

### StingAutoTagger (`Core/StingAutoTagger.cs`) — NEW
IUpdater-based real-time auto-tagging. When enabled:
- Registers 22 categories for element-addition trigger
- On element creation: auto-populate tokens + build tag + write containers
- Uses deduplication HashSet (10K cap) to prevent re-trigger loops
- Disabled by default; toggle via command

### TagChangedCommand (Delta Tagging) — NEW
Detects stale spatial tokens without full re-tag:
1. Scans all tagged elements
2. Compares stored LVL/LOC/ZONE vs freshly-derived values
3. Updates only changed tokens, rebuilds TAG1 for affected elements
4. Uses `HashSet<ElementId>` deduplication

### AnomalyAutoFixCommand — NEW
Auto-fixes 7 anomaly types: empty DISC, wrong DISC, empty LOC/ZONE/LVL/SYS, placeholder SEQ.

### TagFormatMigrationCommand — NEW
Previews current vs rebuilt tags for format changes (separator, padding, segment order). Shows affected element count before bulk migration.

### ValidateBepComplianceCommand — NEW
Loads `project_bep.json` with allowed LOC/ZONE/DISC codes and reports violations per token type. Enforces BIM Execution Plan constraints.

### StingProgressDialog — NEW
Modeless WPF progress window with cancel support, ETA display, Escape key detection. Integrated into BatchTagCommand.

### Live Compliance Dashboard — NEW
`ComplianceScan.cs` runs cached compliance scans with RAG status, updates status bar after every command.

### IFC Property Set Export — NEW
Maps STING parameters to IFC property sets (AssetTag, AssetLifecycle, AssetIdentity, AssetCost).

---

## 9. Efficiency Review & Optimisation Notes

### Current Architecture Strengths

1. **Single-pass token read**: `ReadTokenValues()` reads 8 tokens once → reused for all containers
2. **O(1) collision detection**: `BuildExistingTagIndex()` creates HashSet, checked per tag
3. **Combined index+counter scan**: `BuildTagIndexAndCounters()` builds both indexes in one pass using `ElementMulticategoryFilter`
4. **Category-filtered containers**: `ContainersForCategory()` returns only 6-10 applicable containers per element (not all 36)
5. **Parameter lookup cache**: `ConcurrentDictionary<(ElementId, string), Definition>` eliminates O(n) `LookupParameter` per call
6. **Deduplication in delta tagging**: `HashSet<ElementId>` prevents redundant tag rebuilds
7. **Single-transaction batching**: All token writes + container writes in one transaction
8. **View-discipline filtering**: Tags only relevant categories per view, avoiding wasted work

### Identified Optimisation Opportunities

| Area | Issue | Impact | Recommendation |
|------|-------|--------|----------------|
| TAG7 Separate Write | `WriteContainers()` skips TAG7; `WriteTag7All()` re-reads 20+ params | ~20 extra GetString calls per element | Batch TAG7 param reads with initial token read |
| Markup Regeneration | BuildTag7Sections() always regenerates all 6 sections even for single-token changes | String allocation overhead for complex narratives | Cache unchanged sections, only rebuild what changed |
| Formula Evaluation | 199 formulas evaluated sequentially per element | Can be slow on large models (10K+ elements) | Consider batch evaluation with early-exit for unchanged inputs |
| SEQ Overflow | NumPad=4 limits to 9999 per group | Warning only, no auto-recovery | Log and suggest NumPad increase in project_config.json |
| WhereElementIsNotElementType | `BuildExistingTagIndex` scans ALL non-type elements | Includes views, sheets, annotation | Use `ElementMulticategoryFilter` (already done in `BuildTagIndexAndCounters`) |
| Reverse SysMap | `GetReverseSysMap()` uses `List<string>` with `Contains()` check | O(n) per entry for small lists (~7 items) | Acceptable for current scale; `HashSet` if SysMap grows |

### Recommendations for 100% Efficiency

1. **Always use `BuildTagIndexAndCounters()`** instead of separate `BuildExistingTagIndex()` + `GetExistingSequenceCounters()` — saves one full project scan.

2. **Use `TagAndCombineCommand`** for standard workflows — it combines token population, tagging, and container writing in a single optimised pass.

3. **Use `TagNewOnlyCommand`** for incremental work — pre-filters to untagged elements, much faster than full BatchTag.

4. **Use `TagChangedCommand`** after model edits — only scans for stale spatial tokens, avoids full re-tag.

5. **Clear parameter cache** (`ParameterHelpers.ClearParamCache()`) after `LoadSharedParams` to prevent stale lookups.

6. **Leverage Escape cancellation** — all batch commands support Escape key via `EscapeChecker` with transaction rollback.

---

## 10. Manual Tag Creation Guide

When automated tagging is unavailable (e.g., custom categories, complex overrides, or legacy models), tags can be manually constructed following these rules.

### Step 1: Determine the 8 Token Values

Work through each token in order:

#### DISC — Look up your element's Revit category:
```
Air Terminals / Ducts / Mech Equipment → M
Electrical Equipment / Fixtures / Lighting → E
Plumbing Fixtures → P
Doors / Windows / Walls / Floors / Ceilings / Roofs / Rooms → A
Structural Columns / Framing / Foundations → S
Sprinklers / Fire Alarm Devices → FP
Communication / Data / Security Devices → LV
Generic Models / Specialty Equipment → G

Special case: Pipes default to M, but override to P if plumbing system
(DCW/DHW/SAN/RWD/GAS/HWS) or FP if fire protection system.
```

#### LOC — Identify the building/location:
```
Main building → BLD1
Second building → BLD2
Third building → BLD3
External/outdoor → EXT
Unknown → XX (placeholder)
```

#### ZONE — Identify the zone:
```
Zone 1 / Wing A / North → Z01
Zone 2 / Wing B / South → Z02
Zone 3 / Wing C / East  → Z03
Zone 4 / Wing D / West  → Z04
Unknown → ZZ or XX (placeholder)
```

#### LVL — Identify the level:
```
Ground Floor → GF
Level 01 → L01
Level 02 → L02
Basement 1 → B1
Roof → RF
Mezzanine → MZ
Unknown → XX (will be replaced with L00 by system)
```

#### SYS — Identify the system:
```
HVAC / Air conditioning → HVAC
Hot water / heating → HWS
Cold water → DCW
Domestic hot water → DHW
Sanitary / drainage → SAN
Rainwater → RWD
Gas → GAS
Fire protection → FP
Low voltage / power → LV
Fire alarm / life safety → FLS
Communications → COM
ICT / data → ICT
Nurse call → NCL
Security → SEC
Architectural → ARC
Structural → STR
Other → GEN
```

#### FUNC — Derive from SYS:
```
HVAC → SUP (or RTN/EXH/FRA for return/exhaust/fresh air)
HWS  → HTG (or DHW for domestic hot water)
DCW  → DCW
SAN  → SAN
RWD  → RWD
GAS  → GAS
FP   → FP
LV   → PWR
FLS  → FLS
COM  → COM
ICT  → ICT
ARC  → FIT
STR  → STR
GEN  → GEN
```

#### PROD — Identify the product type:
```
Check the family-aware PROD table in Section 2, Token 6 above.
If no specific match: use the category default from ProdMap.
If still unknown: use GEN.
```

#### SEQ — Assign sequence number:
```
Within each {DISC}_{SYS}_{LVL} group, assign sequential 4-digit numbers:
0001, 0002, 0003, ...
Continue from the highest existing number in that group.
```

### Step 2: Assemble the Tag

Join all 8 tokens with hyphens:
```
{DISC}-{LOC}-{ZONE}-{LVL}-{SYS}-{FUNC}-{PROD}-{SEQ}
```

**Examples**:
```
M-BLD1-Z01-L02-HVAC-SUP-AHU-0003    ← Mechanical AHU, Building 1, Zone 1, Level 2
E-BLD1-Z01-GF-LV-PWR-DB-0001        ← Electrical DB, Building 1, Zone 1, Ground Floor
P-BLD1-Z02-L01-DCW-DCW-WHB-0005     ← Plumbing wash basin, Building 1, Zone 2, Level 1
A-BLD1-Z01-L03-ARC-FIT-DR-0012      ← Architectural door, Building 1, Zone 1, Level 3
FP-BLD1-Z01-L02-FP-FP-SPR-0001     ← Fire sprinkler, Building 1, Zone 1, Level 2
LV-BLD1-Z01-GF-SEC-SEC-SEC-0003    ← Security camera, Building 1, Zone 1, Ground Floor
```

### Step 3: Write to Parameters

Write the assembled tag to `ASS_TAG_1_TXT`, then write individual tokens:

| Parameter | Value |
|-----------|-------|
| `ASS_DISCIPLINE_COD_TXT` | DISC |
| `ASS_LOC_TXT` | LOC |
| `ASS_ZONE_TXT` | ZONE |
| `ASS_LVL_COD_TXT` | LVL |
| `ASS_SYSTEM_TYPE_TXT` | SYS |
| `ASS_FUNC_TXT` | FUNC |
| `ASS_PRODCT_COD_TXT` | PROD |
| `ASS_SEQ_NUM_TXT` | SEQ |
| `ASS_TAG_1_TXT` | Full 8-segment tag |
| `ASS_STATUS_TXT` | NEW / EXISTING / DEMOLISHED / TEMPORARY |

### Step 4: Populate Container Parameters

Build the sub-tags using the token index arrays:

| Container | Formula | Example |
|-----------|---------|---------|
| TAG1 | DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |
| TAG2 | DISC-PROD-SEQ | M-AHU-0003 |
| TAG3 | LOC-ZONE-LVL | BLD1-Z01-L02 |
| TAG4 | SYS-FUNC | HVAC-SUP |
| TAG5 | DISC-LOC-ZONE-LVL | M-BLD1-Z01-L02 |
| TAG6 | SYS-FUNC-PROD-SEQ | HVAC-SUP-AHU-0003 |

Then write the discipline-specific container (e.g., `HVC_EQP_TAG_TXT` = DISC-SYS-FUNC-PROD-SEQ for Mechanical Equipment).

### Step 5: Validate

Run the **Validate Tags** command or manually check:

- [ ] DISC is in valid set (M, E, P, A, S, FP, LV, G)
- [ ] LOC is in valid set (BLD1, BLD2, BLD3, EXT, XX)
- [ ] ZONE is in valid set (Z01-Z04, ZZ, XX)
- [ ] LVL matches element's host level
- [ ] SYS is valid for this category (check SysMap)
- [ ] FUNC is consistent with SYS (check FuncMap)
- [ ] PROD is 2-4 alphanumeric characters, appropriate for DISC
- [ ] SEQ is unique within DISC_SYS_LVL group
- [ ] TAG1 has exactly 8 hyphen-separated segments
- [ ] No placeholder tokens remain (XX, ZZ, 0000) for production use
- [ ] DISC matches category (e.g., Plumbing Fixtures must be P, not M)
- [ ] Pipe DISC matches connected system (DCW pipes = P, not M)

### Common Mistakes to Avoid

| Mistake | Problem | Fix |
|---------|---------|-----|
| Pipes tagged as M when carrying cold water | Wrong DISC | Check connected MEP system → if DCW/DHW/SAN/RWD/GAS, use P |
| Duplicate SEQ in same group | Tag collision | Use Find Duplicates → Fix Duplicates commands |
| LOC left as XX on all elements | Incomplete spatial data | Run Family-Stage Populate or set LOC manually |
| SYS=GEN on all MEP elements | System detection failed | Connect elements to MEP systems in Revit, then re-tag |
| TAG7 empty | Narrative not built | Run Combine Parameters or Tag & Combine |
| Containers out of sync with tokens | Manual token edit without rebuild | Run Build Tags or Combine Parameters |

---

*Document generated from codebase analysis of StingTools v1.0.0.0 (58 source files, 234 commands, 199 formulas). For the latest code references, see `Core/TagConfig.cs`, `Core/ParamRegistry.cs`, `Core/ParameterHelpers.cs`, and `Data/FORMULAS_WITH_DEPENDENCIES.csv`.*
