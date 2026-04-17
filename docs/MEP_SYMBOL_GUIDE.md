# MEP symbol library — companion guide

Companion to `CompiledPlugin/Data/mep_symbols.csv`.

## Table of contents

1. Discipline introductions
2. Subcategory scheme and line styles
3. Drawing checklist per discipline
4. Revit Family Editor setup
5. Standards sourcing table
6. Integration with STINGTOOLS
7. Coverage gaps

<!-- Sections appended in subsequent commits. -->

---

## 1. Discipline introductions

The library covers nine MEP disciplines. Each discipline has its own standards family, its own conventions for symbol morphology, and its own reasons for choosing between ISO, BS/EN, and US references. The rows in `mep_symbols.csv` carry all three so that a single library can be deployed on UK, EU, Commonwealth, US, and international projects without redrawing.

### 1.1 Gas (G) — 21 symbols

**Why these standards.** ISO 14617-2/8 defines the P&ID grammar for combustion and fuel supply; BS 1553-1 overlays UK Approved Document J, Gas Safe Register, and IGEM conventions; ASME B31.8 governs US distribution piping. Fire-safety valves (ESVs, solenoids, relief) cross-reference BS EN 161 and IGEM/UP/1B.

**When to use which.** UK domestic and light-commercial projects follow BS 1553-1 with BS EN 161 for gas trains. International hospitality, manufacturing, and oil/gas use ISO 14617 or ASME B31.8 depending on jurisdiction. Medical gases are deliberately excluded here (covered under Plumbing §HTM 02-01) to prevent confusion on the drawing.

**Morphology.** Gas symbols typically carry a yellow highlight (BS 1710) on line work, small annotation labels ('G', 'NG', 'LPG'), and valve stem markers (spring for relief, fusible link for ESV). The 21 rows cover pipes (3), meters (2), regulators (1), valves (6), train assembly (1), equipment connections (3), detection (1), manifold/riser/drain (3), and portable cylinders (1).

### 1.2 Controls / BMS (C) — 32 symbols

**Why these standards.** ISO 14617-6 is the international P&ID standard for control instrumentation; BS EN ISO 14617-6 is the UK adoption; ASHRAE Guideline 4 is the US convention for HVAC controls drawings. BACnet (ISO 16484-5), Modbus (IEC 61158), and KNX (ISO 22510) define the fieldbus symbols.

**When to use which.** Controls symbols are **standard-agnostic** in practice — the same circle-with-letter convention is used worldwide. What varies is the fieldbus labelling and the sensor letter codes (T/H/CO2 vs ISA tag codes).

**Morphology.** Sensors are circles Ø4-5mm with a single identifying letter (T, H, P, F, L). Actuators are the host equipment (valve, damper) with a small square 'M' box above. Controllers are labelled rectangles (DDC, PLC, HMI). Fieldbus cables are dashed lines with the protocol name. The 32 rows cover sensors (15), actuators (3), drives (2), controllers (4), fieldbus (3), room controllers (2), overrides (2), manual intervention (1).

### 1.3 Lighting (L) — 31 symbols

**Why these standards.** IEC 60617-11-02 defines luminaire symbols; BS EN 60617-11 is the UK adoption. NFPA 170 is the US life-safety adoption. ISO 7010 E001 is the running-man exit pictogram shared across all three jurisdictions. BS EN 60598-2-22 governs emergency luminaires in UK/EU; UL 924 is the US equivalent.

**When to use which.** UK projects default to BS EN 60617-11. EU projects use IEC 60617-11. US projects swap to NFPA 170 and NEC Article 700/701 for emergency circuits. Exit signs are the one symbol that is **identical** across standards.

**Morphology.** Luminaires are circles or rectangles with a cross or dot indicating the light source, and a shaded half or 'M/NM' letter for emergency variants. Switches are angled lines meeting a circle, with dots for N-way switching. The 31 rows cover luminaires by mount (8), luminaires by size (4), emergency (4), external (4), signage (1), and switches (10).

### 1.4 Public Health / Drainage (PH) — 40 symbols

**Why these standards.** ISO 4067-1 is the international piping P&ID standard; BS EN 752 (drainage outside buildings), BS 5572 (sanitary pipework), BS 8301 (building drainage) together define UK practice. ASPE 45 is the US sanitary/drainage reference. SuDS-specific symbols (attenuation, swales) draw on BS 8582.

**When to use which.** UK and Commonwealth projects follow BS EN 752 / BS 5572 / BS 8301. US projects follow ASPE 45 and UPC/IPC. ISO 4067-1 is the international fallback for SFS, DIN, NBR, and similar national standards.

**Morphology.** Drainage symbols emphasise flow direction, trap seals (P, S, bottle, running), and access points (rodding eye, inspection chamber, manhole). SuDS symbols use stippled fills. The 40 rows cover gullies (4), traps (4), access (4), rainwater (5), stacks/branches (4), fixture waste connections (8), pumping and separation (5), interceptors and SuDS (6).

### 1.5 Comms / Low voltage (LV) — 39 symbols

**Why these standards.** IEC 60617-11 and BS EN 60617-11 govern communications outlet graphics; TIA-606 is the US structured cabling labelling standard; TIA-568 is the cabling performance standard. BS EN 50173 is the UK/EU adoption of ISO/IEC 11801. Fire telephones (type B) follow BS 5839-9. Nurse call (HTM 08-03) and DDA hearing loop (BS EN 60118-4) have UK-specific conventions.

**When to use which.** UK projects use BS EN standards plus HTM documents for healthcare. US projects use TIA/EIA standards and NFPA 70 Chapter 8. International projects default to IEC/ISO.

**Morphology.** Data outlets are triangles with numbers (single, double, quad). Comms rooms are rectangles labelled RACK or WR. CCTV uses category icons (dome, bullet, PTZ). PA and nurse-call devices use speaker or cross icons. The 39 rows cover data outlets (5), cabinets (3), wireless (2), CCTV (3), access (4), audio (5), video/TV (4), DECT/cellular (3), aerials (2), cables (4), clocks/nurse call (4).

### 1.6 Fire Protection (FP) — 52 symbols

**Why these standards.** ISO 6790 defines fire detection and suppression symbols; BS EN 54 (series) covers detection devices; BS EN 12845 covers sprinklers; BS 5839 covers detection and warning; NFPA 170 / 13 / 14 / 15 / 2001 govern US installations. Gaseous agents follow ISO 14520-9 (FM-200) and ISO 6183 (CO2).

**When to use which.** UK projects follow BS EN 54 for detection and BS EN 12845 / BS 9990 for suppression. US projects follow NFPA 72 and NFPA 13 as their core references. International projects use ISO 6790 plus whichever national building regulations apply.

**Morphology.** Detection symbols are squares or circles Ø5-6mm with letters (S, H, M, CO, V). Manual call points are squares with 'MCP' or 'break glass' icon. Sounders and beacons use bells, triangles, and 'VAD' labels per BS EN 54-23. Sprinklers use Ø4mm circles with upward/downward/sideways triangles indicating orientation. Extinguishers use vertical ovals with colour-coded letters (BS EN 3-7 colours). The 52 rows cover detection (11), signalling (6), panels and interfaces (5), sprinklers (9), alarm/control valves (4), fire pumps (2), storage and outlets (3), hydrants and risers (3), extinguishers and blankets (5), gaseous suppression (4).

### 1.7 Plumbing (P) — 81 symbols

**Why these standards.** ISO 4067-1/6 covers piping P&IDs and fluid colour-coding; BS 5572 (above-ground sanitary), BS 8558 (water supply), BS 1710 (pipe identification) cover UK practice; ASME A112.19.2 and ASPE 45 cover US. Medical gases follow ISO 7396-1 / BS EN ISO 7396-1 / NFPA 99 (Chapter 5). Doc M accessible sanitaryware follows BS 8300 and ISO 21542.

**When to use which.** UK projects follow BS 5572, BS 8558, BS 6700 (unvented G3), WRAS fittings directory. US projects follow UPC/IPC and ASPE references. International hospitality defaults to ISO 4067-1.

**Morphology.** Sanitaryware uses plan-view 'footprints' (oval WC, kidney basin, rectangle bath, square tray) with insertion at the trap or centre. Pipe services are labelled lines (CWS, HWS, LTHW) with colour codes per BS 1710. Valves use the two-triangle PID morphology with identifying marker (plug handle, ball fill, gate line, butterfly disc, globe body, check arrow). The 81 rows cover sanitaryware (23), services pipes (13), services fittings (2), valves (19), pumps (5), tanks (4), meters and backflow (3), accessories and treatment (12).

### 1.8 Electrical (E) — 101 symbols

**Why these standards.** IEC 60617 (series) is the international electrical symbology standard; BS EN 60617 is the UK adoption; IEEE 315 / ANSI Y32.9 / NEMA / NFPA 170 are the US references. Industrial plugs follow IEC 60309. Socket outlets have **jurisdiction-specific** morphologies (BS 1363, CEE 7/4 Schuko, NEMA 5-15R, AS/NZS 3112) — the CSV covers all four.

**When to use which.** UK projects must use BS 7671 (Wiring Regulations) with BS EN 60617 symbols. US projects use NEC with IEEE 315 / NEMA symbols. EU projects use IEC 60617 and Schuko. AU/NZ use AS/NZS 3000 with GPO symbols.

**Morphology.** Sockets are circles with slot or prong indicators. Switches use angled line symbols (1-way, 2-way, intermediate, dimmer). Distribution boards are rectangles with sub-labels (CU, DB, MSB). Protection devices use the IEC thermal+magnetic or 'MCB/RCD/RCBO' lettering. Cable containment uses hatched rectangles (ladder, basket, trunking) with line-style differentiation. The 101 rows cover sockets (15), FCUs and isolators (5), control devices (5), floor and accessory outlets (10), specialty and critical power (10), DBs and switchboards (6), transformers (2), UPS/generator/renewable (8), protection (12), motors and starters (6), cabling and containment (12), lightning and renewables (10).

### 1.9 HVAC / Mechanical (M) — 120 symbols

**Why these standards.** ISO 14617-8 covers fluid-power and HVAC P&IDs; BS 1553-1 / BS EN 1505 cover UK ductwork and fittings; ASHRAE Fundamentals and SMACNA cover US. Fire, smoke, and FSDs follow BS EN 15650 and UL 555 / 555S. Refrigerant lines follow BS EN 378 and ASHRAE 15. Kitchen extract follows BS 6173 / BS EN 16282 / NFPA 96.

**When to use which.** UK projects follow BS EN 1505 (ducting), BS 7346 (smoke extract), BS EN 12101 (smoke control), CIBSE Guides B/C/H. US projects follow ASHRAE/SMACNA, NFPA 90A/92A/96. International projects default to ISO 14617-8.

**Morphology.** Air terminals use squares with arrows radiating (supply) or double lines (return). Ducts are rectangles or circles with discipline abbreviations. Dampers use the rectangle+blade morphology with VCD/FD/FSD/SD letter codes. Valves repeat the plumbing two-triangle PID grammar. Equipment (AHU, FCU, boiler, chiller) use labelled rectangles with manufacturer-style schematic icons (fan, coil, burner, heat-exchanger plates). The 120 rows cover air terminals (15), ducts and fittings (25), dampers (5), VAV and DOAS (5), fans (10), AHU/FCU/cassettes (15), radiators and TRVs (7), heat exchangers and pumps (5), air and dirt separation (5), condensate and expansion (4), instrumentation (7), refrigerant (5), kitchen extract (3), attenuation and jet nozzles (3), anti-vibration and passive stacks (3).

### 1.10 Cross-discipline philosophy

Three rules make the three-standard library usable in practice:

1. **One insertion point per symbol.** The CSV locks down where the family origin sits (wall face, pipe centreline, geometric centre, duct centreline). This is jurisdiction-independent and must match the Revit reference planes.
2. **Standard as a visibility switch, not a family count.** A single `.rfa` carries all three symbol variants; the Yes/No type parameters `SYMBOL_SHOW_IEC`, `SYMBOL_SHOW_BS`, `SYMBOL_SHOW_ANSI` swap which variant is visible. Typical projects set one per project-wide type catalogue.
3. **Category binding per Revit's native categories.** Fire alarm devices, lighting fixtures, duct accessories, etc. — the host category stays native so Revit schedules, system browser, and filters work unchanged.

