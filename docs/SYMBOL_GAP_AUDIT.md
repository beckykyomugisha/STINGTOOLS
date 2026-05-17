# STING Symbol Library — Gap Audit

**Date**: 2026-05-17
**Audit scope**: All symbol JSON files under `StingTools/Data/` and `StingTools/Core/Fabrication/`
**Auditor**: Automated analysis + manual review

---

## Executive Summary

- **389 symbols are defined** across 9 JSON files, against an estimated requirement of **900–1,000 symbols** for full AEC/FM project coverage — a gap of roughly **550–600 symbols (55–60%)**.
- **Three JSON files are completely empty** (`STING_SLD_SYMBOLS_BS.json`, `STING_SLD_SYMBOLS_CIBSE.json`, `STING_SLD_SYMBOLS_NFPA.json`), representing planned standards coverage that has never been authored.
- **Thirteen entire symbol categories have no JSON file at all**, including wire/cable annotations, earthing and bonding, BMS/controls, DALI/lighting controls, telecommunications, plumbing equipment (hot/cold), and EV charging — all of which appear on standard commercial-project drawings.
- **A critical wiring bug in `IsoSymbolPlacer`** means none of the 164 ISO 6412 spool symbols can be placed at runtime: the index CSV uses short-code filenames (`STING_FAM_PIPE_ELBOW_90_BW.rfa`) while the JSON generator produces prefixed-ID filenames (`ISO6412_ELBOW_90_BW.rfa`), so every lookup fails silently.
- **225 of the 389 existing symbols** (all non-ISO files) are missing the `"status"` field, making it impossible to filter draft vs production-ready content programmatically.

---

## Critical Bugs (Fix Before Content Work)

> These issues make existing symbols non-functional regardless of content completeness.

### BUG-1 — IsoSymbolPlacer filename resolution failure

| Property | Detail |
|---|---|
| Affected component | `StingTools/Core/Fabrication/IsoSymbolPlacer.cs` |
| Affected data | `StingTools/Data/STING_ISO_SYMBOLS_INDEX.csv` (182 entries) |
| Severity | **Critical** — all 164 ISO 6412 symbols are unreachable at runtime |

**Root cause**: `IsoSymbolPlacer` reads `family_filename` from the index CSV and searches `Families/ISO6412/` for that filename. The index CSV uses short-code names derived from the STING pipe-spool convention, but the JSON geometry files generate families using the `ISO6412_` prefix convention. The two naming schemes never intersect.

| Convention | Example |
|---|---|
| Index CSV `family_filename` | `STING_FAM_PIPE_ELBOW_90_BW.rfa` |
| JSON-generated family id | `ISO6412_ELBOW_90_BW.rfa` |
| Match? | **No** |

**Proposed fixes (choose one)**:

- **Option A** — Add a `json_id` column to `STING_ISO_SYMBOLS_INDEX.csv` mapping each row to its corresponding JSON symbol `id`. Update `IsoSymbolPlacer` to use this column for file resolution.
- **Option B** — Update `IsoSymbolPlacer` to resolve by `symbol_code` via a JSON id lookup (build an in-memory dictionary from the JSON files at startup, keyed on a normalised code string).

**Affected index entries**: all 182 rows; 164 have a corresponding JSON entry (under the wrong name), 18 have no JSON entry at all.

---

### BUG-2 — ISO 6412 index vs JSON naming mismatch (93 unresolvable entries)

Even after fixing BUG-1, 93 of the 182 index entries have **no matching JSON symbol** at any name. These are listed in detail under Gap Level 3 below.

---

## Gap Level 1 — Empty Files (Exist, Zero Symbols)

These files are registered in the codebase and presumably referenced by the loader, but contain no symbol definitions.

| File | Standard | Expected content | Symbol count |
|---|---|---|---|
| `STING_SLD_SYMBOLS_BS.json` | BS EN 60617 | BS-annotated mirror of IEC content; UK project standard | **0** |
| `STING_SLD_SYMBOLS_CIBSE.json` | CIBSE building services | CIBSE Guide symbols for HVAC, controls, and plumbing schematics | **0** |
| `STING_SLD_SYMBOLS_NFPA.json` | NFPA 70 / NEC | North American electrical single-line symbols | **0** |

**Action required**: Author content for all three files or mark as `planned` with a clear owner and target date.

---

## Gap Level 2 — Missing Symbol Categories (No JSON File)

The following 13 categories have no JSON file and no symbols. Estimated symbol counts are based on industry standard drawing sets.

| # | Category | Est. symbols needed | Typical use |
|---|---|---|---|
| 1 | Wire / cable annotations | 18 | Every electrical drawing |
| 2 | Earthing and bonding | 8 | Every electrical installation drawing |
| 3 | BMS / building controls | 12 | Commercial and healthcare buildings |
| 4 | DALI / lighting controls | 9 | Offices, retail, healthcare |
| 5 | Telecommunications | 13 | All commercial buildings |
| 6 | Plumbing equipment (hot/cold services) | 16 | Every services drawing |
| 7 | Above-ground drainage | 9 | All occupied buildings |
| 8 | Gas (natural gas and LPG) | 8 | Gas-fired buildings and plant rooms |
| 9 | Renewable energy | 8 | New builds and retrofit |
| 10 | Structural plan annotation | 12 | All structural drawings |
| 11 | Civil / external drainage | 9 | Sites with external drainage |
| 12 | EV and transport | 8 | Car parks, commercial sites |
| 13 | Medical gas extension (clinical spaces) | 9 | Healthcare projects |
| **Total** | | **~139** | |

### 2.1 Wire / Cable Annotations (18 symbols needed)

| Symbol | Description |
|---|---|
| Home run arrow | Single-line circuit home-run to panel |
| Circuit bubble | Numbered circuit reference |
| Phase label L1 / L2 / L3 | Phase identification on SLD conductors |
| Neutral label N | Neutral conductor label |
| PE / CPC label | Protective earth conductor |
| Cable size tag | mm² annotation on cable runs |
| Wire count slashes | Number of conductors in a run |
| Conduit label | Conduit size and type annotation |
| SLD cable label | Cable reference on single-line |
| Voltage annotation | kV / V annotation |
| Current annotation | A annotation |
| Fault level annotation | kA annotation |
| Feeder arrow | Feed direction indicator |
| Cable reference tag | CPC/SR cross-reference bubble |
| Loop impedance annotation | Zs / Ze annotation |
| Disconnection time annotation | Trip time annotation |
| Diversity factor annotation | Phase diversity note |
| Load annotation | kW / kVA load label |

### 2.2 Earthing and Bonding (8 symbols)

| Symbol | Description |
|---|---|
| Earth rod | Driven earth electrode |
| Earth pit | Inspection pit over earth electrode |
| Bonding conductor | Supplementary bonding line |
| Test link | Removable link in earthing conductor |
| Equipotential bonding bar | Main protective bonding bar |
| Main earth terminal (MET) | Main earthing terminal block |
| Combined protection/neutral bar | PEN conductor bar (TN-C) |
| Lightning earth termination | Down conductor earth termination |

### 2.3 BMS / Building Controls (12 symbols)

| Symbol | Description |
|---|---|
| Temperature sensor (BMS) | Room / duct temperature |
| Humidity sensor (BMS) | Room / duct humidity |
| CO2 sensor (BMS) | Demand-controlled ventilation |
| Occupancy sensor (BMS) | PIR / ultrasonic presence |
| Pressure sensor | Differential / static pressure |
| Flow sensor | Volume / velocity flow |
| Valve actuator (BMS) | Motorised valve signal |
| Damper actuator | Motorised damper signal |
| Controller panel | Local BMS controller |
| BMS outstation / DDC | Distributed direct digital controller |
| Room controller | Fan coil / VAV room controller |
| Building management panel | Central BMS panel (plan) |

### 2.4 DALI / Lighting Controls (9 symbols)

| Symbol | Description |
|---|---|
| DALI bus driver | DALI power supply unit |
| DALI gateway | DALI-to-IP bridge |
| Scene controller | Multi-scene wall plate |
| Daylight sensor | Photocell for daylight-linked dimming |
| Presence detector (DALI) | DALI-addressable PIR |
| Push-dim switch | Momentary push dimmer |
| Wireless switch / receiver | RF/BLE switch and receiver pair |
| Emergency test unit | Central test unit for emergency gear |
| Central battery unit | Central battery emergency system |

### 2.5 Telecommunications (13 symbols)

| Symbol | Description |
|---|---|
| Comms room / rack (plan) | Rack footprint on floor plan |
| Patch panel | Copper structured cabling patch panel |
| ODF (optical distribution frame) | Fibre termination frame |
| Fibre splice enclosure | Blown-fibre / fusion splice box |
| Server rack | Server enclosure (plan) |
| Structured cabling outlet | RJ45 outlet (floor / wall / ceiling) |
| Wi-Fi access point | Ceiling-mounted WAP |
| Leaky feeder antenna | Radiating coaxial for coverage |
| IPTV outlet | TV distribution outlet |
| Nurse call point | Patient call unit |
| Warden call unit | Warden intercom |
| Intercom panel | Door intercom handset |
| Door entry panel | Video/audio door entry |

### 2.6 Plumbing Equipment — Hot/Cold Services (16 symbols)

| Symbol | Description |
|---|---|
| Combi boiler | Wall-hung combination boiler (plan) |
| System boiler | System boiler (plan) |
| Heat pump (ASHP) | Air source heat pump (plan) |
| HIU | Heat interface unit |
| Calorifier | Indirect hot water cylinder |
| Thermal store | Thermal buffer vessel |
| Cold water storage tank (CWST) | Break-tank / header tank |
| Pressurisation unit | Sealed system fill/pressurise set |
| Expansion vessel | Heating / DHW expansion vessel |
| Booster pump set | Cold water booster set |
| Water meter | Cold water / DHW meter |
| TMV | Thermostatic mixing valve |
| Hose bib / bibcock | External/lab tap |
| Water softener | Ion-exchange softener |
| RO unit | Reverse osmosis unit |
| Backflow preventer | Double-check / RPZ valve |

### 2.7 Above-Ground Drainage (9 symbols)

| Symbol | Description |
|---|---|
| Soil and vent stack | SVP (plan) |
| Access junction | Rodding access in stack |
| Rodding eye | External rodding point |
| Cleanout | Inline cleanout plug |
| Anti-syphon valve | Trap-seal protector |
| Air admittance valve | Durgo / Hepvo AAV |
| Grease trap | Grease interceptor under sink |
| Petrol / oil interceptor | Forecourt interceptor |
| Inspection chamber (plan) | IC cover on drainage layout |

### 2.8 Gas — Natural Gas and LPG (8 symbols)

| Symbol | Description |
|---|---|
| Gas meter | MPRN meter |
| Gas governor | Pressure regulator |
| Gas isolator valve | ECV / service valve |
| Gas solenoid | Emergency shut-off solenoid |
| CSST connector | Corrugated stainless steel flexible |
| Gas booster | Low-pressure gas booster |
| Gas detection head | Fixed gas detector |
| Gas emergency control valve | Remotely operated ECV |

### 2.9 Renewable Energy (8 symbols)

| Symbol | Description |
|---|---|
| Solar PV array (plan) | Panel array roof footprint |
| Solar thermal panel (plan) | Flat-plate / evacuated tube (plan) |
| ASHP (schematic) | ASHP in DHW/heating schematic |
| GSHP | Ground source heat pump |
| Wind turbine (plan) | Small-scale turbine |
| Battery storage unit | Li-ion battery pack |
| Solar inverter | DC/AC string/micro inverter |
| Bidirectional meter | Export / import meter |

### 2.10 Structural Plan Annotation (12 symbols)

| Symbol | Description |
|---|---|
| Steel beam (plan) | UB/UC plan hatch annotation |
| Steel column (plan) | Column symbol with mark |
| Steel connection plate | Gusset / end plate |
| Moment connection | Rigid frame connection symbol |
| Pin connection | Pinned base / apex |
| Base plate | Column base plate |
| Hold-down bolt | Anchor bolt group |
| Shear stud | Composite shear connector |
| Rebar bar mark | BS 8666 bar mark |
| Rebar distribution | Distribution bar callout |
| Section reference | Cut plane reference bubble |
| Detail reference | Detail enlargement bubble |

### 2.11 Civil / External Drainage (9 symbols)

| Symbol | Description |
|---|---|
| Manhole / chamber (plan) | Access chamber cover |
| Soakaway | Infiltration soakaway |
| French drain | Linear soakaway |
| Surface water gully | Road / yard gully |
| Petrol interceptor (external) | Class 1/2 separator |
| Silt trap | Upstream silt chamber |
| Outfall | Discharge point |
| Rodding point (external) | External rodding eye |
| Rainwater harvesting tank | Underground storage |

### 2.12 EV and Transport (8 symbols)

| Symbol | Description |
|---|---|
| EV charge point (tethered) | Type 2 tethered 7 kW |
| EV charge point (socketed) | Type 2 socketed 7 kW |
| EV rapid charger | 50 kW+ DC rapid |
| EV charge post | Bollard-style charge post |
| Cable management pit | In-ground ducting pit |
| Parking sensor | Bay-occupancy sensor |
| ANPR camera | Automatic number plate recognition |
| Barrier arm | Entry/exit barrier |

### 2.13 Medical Gas Extension — Clinical Spaces (9 symbols)

| Symbol | Description |
|---|---|
| Medical gas manifold (plan) | Cylinder bank manifold |
| Oxygen cylinder bank | O2 H-cylinder bank |
| Nitrous oxide bank | N2O cylinder bank |
| Medical air compressor | Oil-free compressor set |
| Vacuum pump | MGVS pump |
| AGSS pump | Anaesthetic gas scavenging pump |
| Zone valve box | NIST zone valve panel |
| Alarm panel (medical gas) | Area alarm / master alarm |
| Bedhead unit (plan) | Pendant / trunking outlet assembly |

---

## Gap Level 3 — ISO 6412 Index vs JSON Naming Mismatch

### Overview

| Metric | Count |
|---|---|
| Index CSV entries | 182 |
| JSON symbols | 164 |
| Entries with BUG-1 naming mismatch | 182 (100%) |
| Entries with no JSON equivalent (any name) | 93 |

The index uses short codes derived from the STING pipe-spool system; the JSON uses `ISO6412_`-prefixed IDs. Until BUG-1 is fixed, all 164 JSON symbols are unreachable via `IsoSymbolPlacer`. Additionally, 93 index entries have no matching JSON symbol under any naming scheme — these are true content gaps within the ISO 6412 scope.

### 3.1 Missing Valves in JSON (19 entries)

| Index code | Description |
|---|---|
| VLV_BFLY | Butterfly valve |
| VLV_DIAPHR | Diaphragm valve |
| VLV_PINCH | Pinch valve |
| VLV_KNIFE | Knife gate valve |
| VLV_CHECK_SWING | Swing check valve |
| VLV_CHECK_LIFT | Lift check valve |
| VLV_CHECK_DUAL | Dual-plate check valve |
| VLV_VENT | Vent valve |
| VLV_DRAIN | Drain valve |
| VLV_FCV | Flow control valve |
| VLV_PCV | Pressure control valve |
| VLV_TCV | Temperature control valve |
| VLV_LCV | Level control valve |
| VLV_MOV | Motor-operated valve |
| VLV_AOV | Air-operated valve |
| VLV_HOV | Hand-operated valve |
| VLV_SOV | Solenoid-operated valve |
| VLV_HANDLE | Handwheel operator |
| VLV_LEVER | Lever operator |

### 3.2 Missing Ductwork Symbols in JSON (23 entries)

Round elbows, round tees, round reducers, round-to-rectangular transitions, offset, elbow (mitered), and all of: fire damper, smoke damper, volume control damper, access door, linear diffuser, circular diffuser, square diffuser, supply register, return register, air filter, heating coil, cooling coil, fan (inline), fan (centrifugal), silencer, louvre, flexible connection.

### 3.3 Missing Conduit Symbols in JSON (8 entries)

| Index code | Description |
|---|---|
| CDT_COUPLING | Conduit coupling |
| CDT_BUSH | Conduit bush (reducing) |
| CDT_LB | LB conduit body |
| CDT_LL | LL conduit body |
| CDT_LR | LR conduit body |
| CDT_T | T conduit body |
| CDT_X | X conduit body |
| CDT_GLAND | Cable gland |

### 3.4 Missing Cable Tray Symbols in JSON (3 entries)

| Index code | Description |
|---|---|
| CTR_BEND_45 | 45-degree horizontal bend |
| CTR_DROPOUT | Dropout (riser) |
| CTR_END_PLATE | End stop / closure plate |

### 3.5 Missing Penetration Symbols in JSON (4 entries)

| Index code | Description |
|---|---|
| SLEEVE_PUDDLE | Puddle flange sleeve |
| SLEEVE_LINK_SEAL | Link-seal modular sleeve |
| WALL_PENETRATION | Generic wall penetration annotation |
| SLAB_PENETRATION | Generic slab penetration annotation |

### 3.6 Missing Weld Symbols in JSON (16 entries)

Field-fit butt weld, socket weld (field), spot weld, seam weld, tack weld, V-groove weld, bevel groove weld, backing weld, plug weld, slot weld, and NDT test marks: RT (radiographic), UT (ultrasonic), PT (dye penetrant), MT (magnetic particle), VT (visual test), PWHT (post-weld heat treatment).

### 3.7 Missing Hanger Symbols in JSON (7 entries)

| Index code | Description |
|---|---|
| HANGER_CLAMP | Pipe clamp hanger |
| HANGER_ROLLER | Roller support |
| HANGER_CONSTANT | Constant-load spring hanger |
| HANGER_RIGID | Rigid strut |
| HANGER_GUIDE | Pipe guide |
| HANGER_ANCHOR | Fixed anchor point |
| EXP_JOINT | Expansion joint |

### 3.8 Missing Notation Symbols in JSON (13 entries)

Duct tag callout, pipe fitting tag, hanger tag, weld tag, general note flag, TYP (typical) callout, NIC (not in contract) marker, NTS (not to scale) flag, revision cloud, linear dimension chain, radial dimension, angular dimension, slope indicator, north arrow, scale bar.

---

## Gap Level 4 — Coverage Gaps Within Existing Files

### 4.1 Electrical (STING_ELEC_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 32 symbols | ~23 symbols | ~55% |

**Missing symbols**: RCD socket outlet, RCD isolator, fused spur (switched), shaver socket, TV/satellite outlet, telephone outlet, structured cabling outlet, BMS panel (plan), nurse call point (plan), warden call unit, intercom handset, door entry panel, solar inverter (plan), heat pump controller, ATS (plan device), main switchboard (plan), sub-main distribution board, motor starter (DOL), star-delta starter, busbar trunking (plan), cable trunking (plan), cable tray (plan), conduit (plan).

### 4.2 Lighting (STING_LIGHTING_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 25 symbols | ~9 symbols | ~70% |

**Missing symbols**: Directional exit sign (left), directional exit sign (right), directional exit sign (straight-on), photocell (external), DALI dimmer driver, integrated emergency gear (3hr self-test), wireless battery switch, scene controller plate, daylight pipe/tube, linear emergency luminaire, IP65 bulkhead (outdoor).

### 4.3 HVAC (STING_MEP_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 27 symbols | ~25 symbols | ~50% |

**Missing symbols**: Heat pump outdoor unit, heat pump indoor unit, chiller (plan), cooling tower, plate heat exchanger, MVHR unit (plan), split AC indoor unit, split AC outdoor unit, VRF indoor unit, VRF outdoor unit, fan coil (ceiling cassette), CRAC/CRAH unit, humidifier, dehumidifier, heat recovery unit, active chilled beam, passive chilled beam, radiant panel, underfloor heating manifold, comfort cooling unit, pressurisation unit (HVAC), expansion vessel (HVAC), pump (HVAC plan), air/dirt separator, pressurisation set.

### 4.4 Fire Protection (STING_FP_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 28 symbols | ~17 symbols | ~60% |

**Missing symbols**: Gas suppression nozzle (total flood), foam head, CO2 nozzle (local application), dry powder nozzle, water mist nozzle, portable extinguisher (plan), hose reel (plan), dry riser inlet (plan), dry riser outlet (plan), wet riser outlet, electromagnetic fire door holder, automatic smoke vent, VESDA sampling point, aspirating detector head, linear heat detection cable run, fire hydrant (plan), fire curtain.

### 4.5 SLD IEC (STING_SLD_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 54 symbols | ~19 symbols | ~70% |

**Missing symbols**: Wire junction (T-off on SLD), wire crossing (bridge/no-connection), protection relay (overcurrent), protection relay (differential), protection relay (earth fault), harmonic filter, reactive power compensator, PFC capacitor bank, selector switch, push button (NO), push button (NC), pilot lamp/indicator light, interlock symbol, earth fault relay, overload relay, thermal relay, time delay relay, changeover switch, cam switch.

### 4.6 SLD IEEE (STING_SLD_SYMBOLS_IEEE.json)

| Current | Missing | Coverage |
|---|---|---|
| 15 symbols | ~39 symbols | ~30% |

**Missing to reach IEC parity**: transformers (2W, 3W, autotransformer), meters (ammeter, voltmeter, wattmeter, kWh meter, PF meter), current transformers, voltage transformers, busbars, bus section switch, earth bar, neutral bar, generator, UPS, battery, solar PV, ATS, single-phase motor, capacitor, heating element, lighting load, socket load, switch fuse, mains switch, LPS symbols, ATEX zone markers, phase sequence symbols.

### 4.7 Plumbing (STING_PLUMBING_SYMBOLS.json)

| Current | Missing | Coverage |
|---|---|---|
| 24 symbols | ~21 symbols | ~55% |

**Missing symbols**: Combi boiler, system boiler, ASHP (plan), HIU, calorifier, thermal store, CWST, pressurisation unit, expansion vessel, booster pump set, water meter, TMV, hose bib, water softener, RO unit, backflow preventer (double-check), soil stack (plan), waste stack, rodding eye, air admittance valve, condensate drain.

### 4.8 Coverage Summary Table

| File | Symbols now | Status field | Est. coverage |
|---|---|---|---|
| `STING_ISO6412_SYMBOLS.json` | 164 | `draft` present | ~64% of index (naming mismatch) |
| `STING_SLD_SYMBOLS.json` | 54 | missing | ~70% of IEC needed |
| `STING_ELEC_SYMBOLS.json` | 32 | missing | ~55% of needed |
| `STING_FP_SYMBOLS.json` | 28 | missing | ~60% of needed |
| `STING_MEP_SYMBOLS.json` | 27 | missing | ~50% of needed |
| `STING_LIGHTING_SYMBOLS.json` | 25 | missing | ~70% of needed |
| `STING_PLUMBING_SYMBOLS.json` | 24 | missing | ~55% of needed |
| `STING_PIPE_ACCESSORIES.json` | 20 | missing | ~65% of needed |
| `STING_SLD_SYMBOLS_IEEE.json` | 15 | missing | ~30% of needed |
| `STING_SLD_SYMBOLS_BS.json` | 0 | missing | **EMPTY** |
| `STING_SLD_SYMBOLS_CIBSE.json` | 0 | missing | **EMPTY** |
| `STING_SLD_SYMBOLS_NFPA.json` | 0 | missing | **EMPTY** |
| **Total** | **389** | | **~40% of needed** |

---

## Gap Level 5 — Missing Status Field (225 Symbols)

All non-ISO symbol files are missing the `"status"` field that the ISO 6412 file uses (`"status": "draft"`). Without this field:

- Symbol loaders cannot distinguish production-ready symbols from draft or placeholder content.
- Filter queries (`WHERE status = 'production'`) return empty results for all non-ISO disciplines.
- QA tooling cannot report coverage broken down by readiness state.

| File | Symbols affected |
|---|---|
| `STING_SLD_SYMBOLS.json` | 54 |
| `STING_ELEC_SYMBOLS.json` | 32 |
| `STING_FP_SYMBOLS.json` | 28 |
| `STING_MEP_SYMBOLS.json` | 27 |
| `STING_LIGHTING_SYMBOLS.json` | 25 |
| `STING_PLUMBING_SYMBOLS.json` | 24 |
| `STING_PIPE_ACCESSORIES.json` | 20 |
| `STING_SLD_SYMBOLS_IEEE.json` | 15 |
| **Total** | **225** |

**Fix**: Add `"status": "draft"` to all existing symbols in these files as a schema-alignment pass. Promote to `"production"` once geometry is verified in Revit.

---

## Gap Level 6 — IsoSymbolPlacer Wiring Bug (Detail)

This is a re-statement of BUG-1 with the full resolution path.

### Current flow (broken)

```
IsoSymbolPlacer.Place(symbolCode)
  → reads STING_ISO_SYMBOLS_INDEX.csv
  → gets family_filename = "STING_FAM_PIPE_ELBOW_90_BW.rfa"
  → searches Families/ISO6412/ for "STING_FAM_PIPE_ELBOW_90_BW.rfa"
  → file not found (JSON produces "ISO6412_ELBOW_90_BW.rfa")
  → logs warning, returns null
  → no symbol placed
```

### Required flow (fixed — Option A)

```
STING_ISO_SYMBOLS_INDEX.csv gains column: json_id
  row: ..., ELBOW_90_BW, ..., STING_FAM_PIPE_ELBOW_90_BW.rfa, ISO6412_ELBOW_90_BW

IsoSymbolPlacer.Place(symbolCode)
  → reads index, gets json_id = "ISO6412_ELBOW_90_BW"
  → searches Families/ISO6412/ for "ISO6412_ELBOW_90_BW.rfa"
  → file found, loads family, places symbol
```

### Files to change (Option A)

| File | Change |
|---|---|
| `StingTools/Data/STING_ISO_SYMBOLS_INDEX.csv` | Add `json_id` column (182 rows to populate) |
| `StingTools/Core/Fabrication/IsoSymbolPlacer.cs` | Read `json_id` column; use it for file resolution |

---

## Quantified Gap Summary

| Gap level | Description | Symbol count |
|---|---|---|
| Level 1 | Empty JSON files (3 files × est. 30 symbols each) | ~90 |
| Level 2 | Entire missing categories (13 categories) | ~139 |
| Level 3 | ISO 6412 index entries with no JSON equivalent | ~93 |
| Level 4 | Thin coverage in existing files | ~153 |
| Level 5 | Status field missing (schema, not content) | 225 (existing) |
| Level 6 | Wiring bug (runtime, not content) | all 164 ISO |
| **Total content gap** | | **~475–550 symbols** |
| **Total with schema/wiring** | | **~650–700 items** |

---

## Prioritised Action Plan

### P1 — Critical (resolve before any production use)

These gaps block standard deliverables on almost every project.

| Action | Files | Est. effort | Symbols |
|---|---|---|---|
| Fix IsoSymbolPlacer BUG-1 (add `json_id` column + update placer) | `STING_ISO_SYMBOLS_INDEX.csv`, `IsoSymbolPlacer.cs` | 0.5 day | unblocks 164 |
| Add status field to all existing non-ISO symbol files | 8 JSON files | 0.25 day | 225 updated |
| Author wire/cable annotation symbols | new `STING_WIRE_ANNOTATIONS.json` | 2 days | 18 new |
| Author plumbing equipment symbols (hot/cold) | extend `STING_PLUMBING_SYMBOLS.json` | 2 days | 16 new |
| Author earthing and bonding symbols | new `STING_EARTHING_SYMBOLS.json` | 1 day | 8 new |
| Fill ISO 6412 valve gaps (VLV_BFLY … VLV_LEVER) | `STING_ISO6412_SYMBOLS.json` | 1.5 days | 19 new |

### P2 — High (commercial projects, most disciplines)

| Action | Files | Est. effort | Symbols |
|---|---|---|---|
| Electrical coverage gaps (RCD, BMS panel, etc.) | `STING_ELEC_SYMBOLS.json` | 2 days | 23 new |
| HVAC equipment gaps (heat pumps, chillers, VRF) | `STING_MEP_SYMBOLS.json` | 2.5 days | 25 new |
| SLD IEC gaps (relays, filters, switches) | `STING_SLD_SYMBOLS.json` | 2 days | 19 new |
| Above-ground drainage | new `STING_DRAINAGE_ABOVE.json` | 1 day | 9 new |
| Fill ISO 6412 ductwork gaps (23 entries) | `STING_ISO6412_SYMBOLS.json` | 2 days | 23 new |
| Fill ISO 6412 hanger gaps (7 entries) | `STING_ISO6412_SYMBOLS.json` | 0.5 day | 7 new |

### P3 — Medium (specialist or project-type dependent)

| Action | Files | Est. effort | Symbols |
|---|---|---|---|
| BMS / building controls | new `STING_BMS_SYMBOLS.json` | 1.5 days | 12 new |
| DALI / lighting controls | new `STING_DALI_SYMBOLS.json` | 1 day | 9 new |
| Telecommunications | new `STING_TELECOM_SYMBOLS.json` | 1.5 days | 13 new |
| IEEE SLD parity (to match IEC file) | `STING_SLD_SYMBOLS_IEEE.json` | 3 days | 39 new |
| Fire protection gaps | `STING_FP_SYMBOLS.json` | 2 days | 17 new |
| Fill ISO 6412 notation gaps | `STING_ISO6412_SYMBOLS.json` | 1 day | 13 new |
| Fill ISO 6412 conduit/penetration gaps | `STING_ISO6412_SYMBOLS.json` | 1 day | 12 new |

### P4 — Lower (niche or specialist projects)

| Action | Files | Est. effort | Symbols |
|---|---|---|---|
| Gas symbols | new `STING_GAS_SYMBOLS.json` | 1 day | 8 new |
| Renewable energy | new `STING_RENEWABLE_SYMBOLS.json` | 1 day | 8 new |
| Structural plan annotation | new `STING_STRUCTURAL_ANNOTATIONS.json` | 1.5 days | 12 new |
| Civil / external drainage | new `STING_CIVIL_DRAINAGE.json` | 1 day | 9 new |
| EV and transport | new `STING_EV_SYMBOLS.json` | 1 day | 8 new |
| Medical gas extension | extend `STING_ELEC_SYMBOLS.json` or new file | 1 day | 9 new |
| Fill `STING_SLD_SYMBOLS_BS.json` | `STING_SLD_SYMBOLS_BS.json` | 1 day | ~30 |
| Fill `STING_SLD_SYMBOLS_CIBSE.json` | `STING_SLD_SYMBOLS_CIBSE.json` | 2 days | ~40 |
| Fill `STING_SLD_SYMBOLS_NFPA.json` | `STING_SLD_SYMBOLS_NFPA.json` | 1.5 days | ~30 |
| Fill ISO 6412 weld symbols (16 entries) | `STING_ISO6412_SYMBOLS.json` | 1.5 days | 16 new |

### Milestone Targets

| Milestone | Actions | Symbols added | Running total |
|---|---|---|---|
| M1 — Unblock production | P1 actions + BUG-1 fix | +66 (+ 164 unblocked) | 455 usable |
| M2 — Commercial projects | M1 + P2 actions | +106 | 561 |
| M3 — Specialist cover | M2 + P3 actions | +103 | 664 |
| M4 — Full catalogue | M3 + P4 actions | +170 | 834 |

---

## Appendix — Related Files

| Path | Role |
|---|---|
| `StingTools/Data/STING_ISO_SYMBOLS_INDEX.csv` | 182-entry index; needs `json_id` column added |
| `StingTools/Core/Fabrication/IsoSymbolPlacer.cs` | Runtime placer; needs BUG-1 fix |
| `Families/ISO6412/` | Generated `.rfa` output directory; families named `ISO6412_*.rfa` |
| `StingTools/Data/STING_FAB_RULES.json` | Fabrication rules referencing symbol ids |
| `docs/SLD_SYMBOL_WORKFLOW_AND_GAPS.md` | Earlier SLD-specific gap notes (partial overlap) |
| `docs/MEP_SYMBOL_GUIDE.md` | MEP symbol authoring guide |
| `docs/WIRE_ANNOTATION_GUIDE.md` | Wire annotation context (P1 gap) |

---

*This audit was produced on 2026-05-17. Re-run after each content sprint to track gap closure.*
