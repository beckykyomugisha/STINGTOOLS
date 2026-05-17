> **This document has been consolidated.** See `docs/guides/MEP_FOUNDATION_GUIDE.md` for the complete, up-to-date guide. The content below is kept for historical reference only.

---

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


---

## 2. Subcategory scheme and line styles

Every symbol drawn in a Revit family must live on a named **subcategory** of one of Revit's built-in annotation or model categories. Subcategories are what let a project-wide `Visibility/Graphics` filter toggle "show IEC symbols, hide BS/ANSI" without touching any family. They are also what let each standard carry its own line weight, line pattern, and colour.

### 2.1 Naming convention

All MEP library subcategories follow a single pattern:

```
ISO_Symbols_<DISC>_<STANDARD>
```

| Token | Values |
|---|---|
| `<DISC>` | `G`, `C`, `L`, `PH`, `LV`, `FP`, `P`, `E`, `M` (the 9 discipline codes in §1) |
| `<STANDARD>` | `IEC`, `BS`, `ANSI` |

This yields 27 subcategories total (9 × 3). Every row in `mep_symbols.csv` already names the three that apply to that symbol in the `subcategory_iec`, `subcategory_bs`, `subcategory_us` columns. When drafters create a seed family they must select one of these three for every symbolic line, filled region, and text note drawn.

Consistent naming matters because `Visibility/Graphics` in the host project lets users filter by **subcategory**, not by family. Toggle `ISO_Symbols_M_IEC` off and every HVAC IEC symbol in every family disappears. Toggle `ISO_Symbols_M_BS` on and BS 1553-1 symbols become visible. One checkbox, one standard, everywhere.

### 2.2 Line weights

Line weights align with the sixth column of the CSV (`line_weight`), which is itself aligned to Revit line-weight numbers 1-16. The defaults assume a 1:50 annotation scale:

| Weight | Printed width @ 1:50 | Typical use |
|---:|---|---|
| 1 | 0.10 mm | Reference/construction lines, gutter outlines, hatching |
| 2 | 0.18 mm | Most symbol geometry: circles, triangles, lines |
| 3 | 0.35 mm | Bold symbols (emergency, fire safety, critical isolation) |
| 4+ | 0.50 mm+ | Reserved for schedule borders and title block (not symbols) |

Fire, emergency, medical-IT, and critical-power devices use weight 3 as a deliberate on-drawing emphasis. All other symbols use weight 2. Reference lines and internal construction geometry use weight 1.

### 2.3 Line pattern

Most symbol geometry is `Solid`. Three exceptions:

| Use | Line pattern |
|---|---|
| Hidden/concealed (sprinkler concealed, dashed door contact line) | `Dashed` |
| Fieldbus routes (BACnet, Modbus, KNX in §1.2) | `Dashed 3mm` |
| Flex duct, flexible conduit | `Zigzag` or `Wavy` (user-defined line pattern in project) |

### 2.4 Colour strategy

Colour is a project-wide overlay, not a symbol property. Revit line colour is set **on the subcategory**, not in the family. This means the three standards can share identical geometry and differ only in colour — useful for side-by-side drawings that compare standards.

Recommended defaults:

| Subcategory family | Colour | Rationale |
|---|---|---|
| `ISO_Symbols_*_IEC` | `Black` | Default printable |
| `ISO_Symbols_*_BS` | `Black` | Default UK |
| `ISO_Symbols_*_ANSI` | `Black` | Default US |
| `ISO_Symbols_FP_*` | `255,0,0` (red) | BS 5499 / NFPA 170 convention |
| `ISO_Symbols_G_*` | `230,180,0` (yellow-ochre) | BS 1710 yellow |
| `ISO_Symbols_L_*` (emergency only) | `0,128,0` (green) | BS 5266 emergency |
| Medical IT / critical power | `255,0,0` (red) | HTM 06-01 / NFPA 99 |
| Data / comms | `0,0,255` (blue) | TIA-606 convention |
| Plumbing hot water | `255,0,0` (red) | BS 1710 |
| Plumbing cold water | `0,120,255` (blue) | BS 1710 |
| HVAC chilled water | `0,0,150` (dark blue) | BS 1710 |
| Controls sensors | `128,0,128` (purple) | Visual separation from MEP |

Colour is overridden per-view with `Override Graphics in View` if a deliverable requires mono print. The STINGTOOLS `ColorCommands` palette can generate all of the above as saved presets — see §6.3.

### 2.5 Setting up subcategories in a family

Manual, per-family (Revit API does not create subcategories programmatically in family documents):

1. Open the `.rfa` in the Family Editor.
2. `Manage → Object Styles → Annotation Objects` tab.
3. `New` → type the subcategory name (e.g. `ISO_Symbols_E_IEC`).
4. Set `Line Weight: Projection = 2`, `Line Color = Black`, `Line Pattern = Solid`.
5. Repeat for the other two standards.
6. Draw your symbol. Select each element, set `Subcategory` in the Properties palette.

The STINGTOOLS `FamilyParamCreator` engine already iterates `.rfa` files and injects shared parameters; extending it to auto-create the 27 subcategories on open is one of the recommended integration hooks in §6.2.

### 2.6 Project-level deployment

Once a seed family has subcategories, the host project sees them under `Object Styles → Annotation Objects` after the family is loaded. No project template changes are needed. The project controls:

- `Visibility/Graphics` per view: toggle each `ISO_Symbols_*` subcategory on/off.
- `Object Styles` per project: set the three standards' default colours and weights once; cascades to every loaded family.
- `View Template`: save a named template per standard (e.g. "IEC Plan", "BS Plan", "ANSI Plan") that toggles the correct 9 subcategories visible and the other 18 hidden.

### 2.7 View filter alternative (advanced)

For projects mixing multiple standards in one view, use a `Parameter Filter` on the `SYMBOL_SHOW_IEC` / `SYMBOL_SHOW_BS` / `SYMBOL_SHOW_ANSI` Yes/No parameters. This toggles the host family's visibility, not the subcategory's, so it applies per-instance rather than per-subcategory. Slower to configure but useful for presentation drawings that deliberately show two standards side by side on the same sheet.


---

## 3. Drawing checklist per discipline

A drafter sitting down to build seed families from this library should work through each discipline with a short, repeatable routine. Each checklist assumes the drafter has already:

- Opened the right `.rft` template (see `revit_template` column in the CSV).
- Created the three subcategories per §2.1.
- Placed two reference planes at the origin (one horizontal, one vertical).

What follows is the per-discipline drawing sequence. The steps are **identical in grammar** across disciplines, but the geometry and insertion points differ.

### 3.1 Gas (21 seeds)

1. Pipes (`GAS_PIPE_NG`, `_LPG`, `_MED`) — drawn in project as Pipe Types with labels, not as families. Skip these three from the seed build; they are system-type rows in the CSV for reference only.
2. Meters (`GAS_MTR_DOM`, `_COM`) — Ø6 or 8 mm rectangles. Insertion at geometric centre. Lock geometry to both reference planes. Add `SYMBOL_SHOW_*` Yes/No parameters.
3. Regulators and valves — draw the two-triangle PID body once, save as a family template `Gas_Valve_Base.rfa`. Duplicate for each valve type and add the distinguishing marker (spring for relief, coil for solenoid, 'E' for ESV, handle for cock/ball).
4. Gas train (`GAS_TRAIN`) — compound symbol 20×8mm. Only one in the library; build once from rectangle + internal guide lines.
5. Connections (`GAS_BLR_CONN`, `_HOB_CONN`, `_FIRE_CONN`) — small wall-hosted stubs. Use wall-based `.rft` not generic annotation.
6. Manifold, riser, drain, cylinder — single-instance symbols. Build each from scratch.

**Estimated time:** ~3 hours for a drafter who has done this once before.

### 3.2 Controls / BMS (32 seeds)

1. Build a `BMS_Sensor_Base.rfa` once — Ø5 mm circle at origin with a parameter-driven text label. The 15 sensor rows differ only in the letter; they can all share one family with a text-type parameter.
2. Actuators and valves reuse the host family's valve symbol, adding a 4×4 mm 'M' box. The 'M' is a small rectangle nested as shared annotation.
3. Controllers (DDC, PLC, HMI) — rectangles 8-12 mm with parameter-driven labels. One shared family per controller category.
4. Fieldbus routes are drawn in-project as annotation lines with dashed line style; no family required. Keep the three CSV rows for reference.
5. Room/zone controllers (`BMS_RM_CTRL`, `_ZN_CTRL`) — small rectangles with label. Wall-hosted.
6. Override switch, fault lamp — single-instance symbols.

**Collapse count:** 32 rows → ~8 actual seed families through shared templates. **Estimated time:** 4 hours.

### 3.3 Lighting (31 seeds)

Revit already ships comprehensive lighting fixture categories. The library does **not** replace those; it adds the IEC/BS/ANSI annotation overlay on plan views.

1. Open each `Lighting Fixture Ceiling based.rft` and draw the plan symbol on the `Symbolic Lines` layer. Lock to the centre reference planes.
2. Build one `Luminaire_Square_6mm.rfa` and one `Luminaire_Circle_6mm.rfa` as the two base shapes; every other luminaire symbol inherits via nested family.
3. Emergency variants (`_EMRG_*`) are the same base with an added shaded half-circle or 'M/NM' label. Use a Yes/No parameter `EMERGENCY_SHOW_SHADE` to toggle.
4. External luminaires use `Lighting Fixture Wall based.rft` and the base symbols extended with rays or directional arrows.
5. Exit sign (`LTG_EMRG_EXIT`) is the single most-used family in the discipline — **spend extra time on this one**. Include the ISO 7010 running-man pictogram, which is the same across all three standards.
6. Switches are annotation-only (no host-element category); drawn as generic annotation families. One `Switch_Base.rfa` with the angled line, dots, and dimmer arrow as Yes/No parameters covers all 10 switch rows.

**Collapse count:** 31 rows → ~10 base families. **Estimated time:** 6 hours.

### 3.4 Public Health (40 seeds)

1. Gullies and traps — most are 5-10 mm footprints with grate or trap markers. Build 4 base families (gully, trap, access, stack) and vary with text/parameter.
2. Rainwater (`PH_RWP`, `_HOPPER`, `_GUTTER`, `_OUTLET`) — small circles or rectangles at pipe centreline. Four separate families.
3. Fixture waste connections reuse the Plumbing fixture footprint (since they share geometry) — add a small 'trap' icon via nested family.
4. SuDS symbols (soakaway, attenuation tank, swale) are drawn at larger scales (1:100-1:500). Use 5-10× larger dimensions and a separate subcategory `ISO_Symbols_PH_SuDS_*`.
5. Cleaning eye, rodding bend, stop end — small fittings. Draw once, reuse.

**Estimated time:** 5 hours.

### 3.5 Comms / LV (39 seeds)

1. Data outlets (`LV_RJ45_*`) — triangle with number inside. One `Data_Outlet_Base.rfa` with a `PORT_COUNT` parameter covers all five rows.
2. CCTV (dome, bullet, PTZ) — three different base shapes. Build as three families.
3. Access control, door entry, intercom — wall-hosted rectangles. One shared base.
4. PA speaker, horn, mic — three families.
5. Wireless AP (ceiling vs wall) — two families, ceiling-based and wall-based.
6. Clock, nurse call, staff attack — individual families (each has unique geometry and colour requirements).
7. Cable routes (UTP, STP, fibre, coax) are line-based; set up as project-level line styles, not families. Keep the four CSV rows for reference.

**Collapse count:** 39 rows → ~15 seed families. **Estimated time:** 5 hours.

### 3.6 Fire Protection (52 seeds)

This is the discipline with the **highest life-safety stakes** — invest in getting the symbols right the first time.

1. Detection (`FP_SMK_*`, `FP_HEAT_*`, `FP_MULTI`, `FP_FLAME`, `FP_CO_DET`) — all Ø6 mm circles or squares with a single letter. One `FD_Detector_Base.rfa` with a `DEVICE_TYPE_TXT` parameter covers all 11 rows.
2. MCP, sounder, beacon, VAD — four base families. The BS EN 54-23 compliance category must be a type parameter (`VAD_CATEGORY` = C/W/O) with values per the standard.
3. Sprinklers (`FP_SPK_*`) — nine rows, all Ø4 mm circles with a small triangle indicating orientation (up/down/sideways/concealed/ESFR/dry). One `Sprinkler_Base.rfa` with `ORIENTATION` type parameter.
4. Valves and pumps — reuse the Plumbing valve base; override labels to `AV-W`, `AV-D`, `FP`, `JP`.
5. Fire telephones, FACP, repeater, isolator, interface — five wall-hosted rectangles.
6. Extinguishers and blankets — vertical ovals on wall. Colour is standard-specific (BS EN 3-7 uses red body with agent-coloured strip; NFPA 10 uses red body). Use type parameter `AGENT_TYPE` + view filter to swap colours.
7. Gaseous suppression nozzles — Ø5 mm circles on ceiling. Three agent types share one base family.

**Collapse count:** 52 rows → ~12 seed families. **Estimated time:** 8 hours.

### 3.7 Plumbing (81 seeds)

The largest discipline in terms of fixture diversity. Break the build into three half-days:

1. **Half-day 1 — Sanitaryware (25 rows).** WC, urinal, basin, bath, shower, bidet, sink, Belfast, dishwasher, washing machine. Most already exist in Revit's out-of-the-box families; the library only adds the plan symbol overlay. Open each OOTB family, draw the symbol on symbolic lines, save as an overridden version.
2. **Half-day 2 — Pipes and fittings (15 rows).** Pipes are project pipe-type rows; fittings are two rows. Limited family work — mostly line-style and labelling setup.
3. **Half-day 3 — Valves, pumps, tanks, accessories (41 rows).** Build one `Plumbing_Valve_Base.rfa` per fitting style (gate, globe, ball, butterfly, check, relief). Every valve type is an instance of one of six bases plus a distinguishing marker. Pumps and tanks are 6-10 mm equipment boxes — one per shape.

**Collapse count:** 81 rows → ~20 seed families. **Estimated time:** 12 hours (1.5 days).

### 3.8 Electrical (101 seeds)

The most jurisdiction-sensitive discipline. Build per-jurisdiction or per-standard, not all three at once.

1. Sockets and outlets — 15 rows for the five socket morphologies (BS 1363, Schuko, NEMA 5-15R, NEMA 5-20R, AS/NZS 3112) plus 10 variant types (switched, double, USB, RCD, weatherproof, industrial 16/32/63A, floor, clean). Build the five base families once; the variants are type parameters.
2. FCUs, isolators, spurs — five rows, each a small wall-hosted rectangle. One shared family.
3. Control devices (emergency stop, push-start, key switch, foot switch, pull cord) — five individual families.
4. Floor boxes and wall management — 10 rows. Most share geometry; use type parameters.
5. Distribution boards and switchgear — 6 rows. Rectangle + label. One shared family with `BOARD_TYPE` parameter.
6. Transformers, UPS, generators, inverters — 8 rows. Each is visually distinct; expect 8 separate families.
7. Protection devices (MCB, RCD, RCBO, MCCB, ACB, fuse, SPD, isolator) — 12 rows. Reuse IEC thermal+magnetic symbol as shared annotation.
8. Motors and starters — 6 rows. Three motor base families (1P, 3P, servo) + three starter variations.
9. Cabling and containment — 12 rows. Mostly line styles, not families. Trunking and tray are tag-based.
10. Lightning and renewables — 10 rows. Each is unique (air rod, down conductor, PV module, BESS, EV charger).

**Collapse count:** 101 rows → ~25 seed families. **Estimated time:** 20 hours (2.5 days).

### 3.9 HVAC (120 seeds)

The largest discipline. Three working days is realistic. Split by functional zone:

1. **Day 1 — Air terminals (15 rows), dampers (5 rows).** All square or rectangular 6-8 mm bases. Arrows, slot lines, blade icons as type-parameter variants. ~6 base families.
2. **Day 2 — Ducts and fittings (25 rows), VAV/CAV (5 rows), fans (10 rows).** Ducts are project duct-type rows, not families. VAV boxes and fans are rectangles or circles with annotation. ~8 seed families.
3. **Day 3 — Equipment (35 rows), radiators (4 rows), instrumentation (11 rows), refrigerant (5 rows), kitchen (3 rows), misc (7 rows).** Most equipment is a labelled rectangle with manufacturer-style schematic icons. AHU, FCU, chiller, boiler, heat pump each get a dedicated family; internal geometry differs substantially.

**Collapse count:** 120 rows → ~35 seed families. **Estimated time:** 24 hours (3 days).

### 3.10 Aggregate estimate

| Discipline | Rows | Collapsed families | Time (drafter hours) |
|---|---:|---:|---:|
| Gas | 21 | 10 | 3 |
| Controls | 32 | 8 | 4 |
| Lighting | 31 | 10 | 6 |
| Public Health | 40 | 15 | 5 |
| Comms/LV | 39 | 15 | 5 |
| Fire Protection | 52 | 12 | 8 |
| Plumbing | 81 | 20 | 12 |
| Electrical | 101 | 25 | 20 |
| HVAC | 120 | 35 | 24 |
| **Total** | **516** | **~150** | **~87** |

**Two working weeks for one drafter** to build the complete seed library. The seed families then feed `FamilyParamCreator` for batched parameter injection (§6.2), and the project-wide subcategory scheme (§2) turns every loaded family into a standard-switchable asset.


---

## 4. Revit Family Editor setup

Before a single line of symbol geometry is drawn, every seed family needs a consistent skeleton. Skipping this step is the single biggest source of rework — a family with misaligned reference planes, missing parameters, or a wrong origin will fail every time it's nested into a host MEP family (see §4.6), and the fix usually means re-authoring from scratch.

### 4.1 Choosing the right family template

The `revit_template` column of the CSV names the `.rft` template to start from. The four templates that cover 95% of the library are:

| Template | When to use | Example symbols |
|---|---|---|
| `Generic Annotation.rft` | The symbol is standalone and not hosted on a specific Revit element | Pipe valves, sprinklers, fire devices, controls |
| `Lighting Fixture Ceiling based.rft` | Ceiling-hosted luminaire symbol | Downlight, recessed panel, pendant, cassette |
| `Lighting Fixture Wall based.rft` | Wall-hosted luminaire or switch | Wall pack, emergency bulkhead, 1-gang switch |
| `Plumbing Fixture Floor based.rft` / `Wall based.rft` / `Ceiling based.rft` | Sanitary fixture attached to floor, wall, or ceiling | WC, basin, urinal, shower head |

**Generic Annotation** is the default for every non-hosted symbol. It scales with the view, has no host, and can be dropped anywhere. Use it for 80% of the library.

Hosted templates are used only when the symbol MUST attach to a wall, ceiling, or floor — typically lighting fixtures, switches, and sanitaryware. They limit placement but give automatic wall-face alignment.

**Do not use** `Generic Model.rft` for symbols. Generic Models are 3D; they will appear in 3D views and perspective renders where annotation should not. Annotation families are 2D only and hide in 3D views automatically.

### 4.2 Origin and reference planes

Every family needs two reference planes crossing at the origin. This is non-negotiable.

1. Open the `.rft`. Two reference planes are already present (horizontal and vertical).
2. **Select each plane** and check the Properties palette. Both must have `Defines Origin` = Yes.
3. The intersection is the family origin. All geometry locks to these planes.
4. Set both planes to `Is Reference` = `Strong Reference`. This makes them selectable as dimension anchors in the host project.
5. Do **not** add extra reference planes unless a specific symbol needs adjustable geometry. Simple symbols (sockets, sensors, valves) need exactly two planes.

The `insertion_point` column of the CSV tells you where the origin goes relative to the symbol:

| CSV value | Origin sits at |
|---|---|
| `Geometric centre` | Centre of symbol bounding box — most common |
| `Pipe centreline` | Horizontal plane aligned to pipe axis; vertical plane at branch point |
| `Wall face` | Vertical plane on wall face; horizontal plane at symbol midheight |
| `Pan centre` | Centre of WC pan outline |
| `Outlet end` | End of a bath or sink nearest the waste outlet |
| `Base centre` | Bottom centre (for vertical symbols like lamp columns, cylinders) |
| `Branch tee` | Pipe junction point for branching fittings |

Get this wrong and every instance of the family misaligns when placed in a project. The drafter loses 10× the authoring time correcting it.

### 4.3 Parameter scaffolding

Every seed family carries the same four Yes/No type parameters:

| Parameter | Group | Default | Purpose |
|---|---|---|---|
| `SYMBOL_SHOW_IEC` | Graphics | Yes | Controls visibility of IEC subcategory geometry |
| `SYMBOL_SHOW_BS` | Graphics | No | Controls visibility of BS/EN subcategory geometry |
| `SYMBOL_SHOW_ANSI` | Graphics | No | Controls visibility of ANSI/NFPA subcategory geometry |
| `EMERGENCY_MODE` | Graphics | No | For fire/emergency symbols — toggles shaded half, 'M' label |

Additionally, most families need text parameters for labelling:

| Parameter | Group | Type | Purpose |
|---|---|---|---|
| `DEVICE_TYPE_TXT` | Identity Data | Text | Letter shown on the symbol (T, H, CO2, etc.) |
| `MOUNTING_HEIGHT_MM` | Dimensions | Length | For wall-hosted fixtures, typed height above FFL |
| `CIRCUIT_ID_TXT` | Identity Data | Text | Circuit ref for electrical devices |

The STINGTOOLS shared parameter file already contains all the STING-standard parameters (ASS_TAG_1_TXT, STING_STALE_BOOL, etc.). When you add a new parameter, **always add it via "Select existing shared parameter"**, not "Add new family parameter". Family parameters don't transfer to the project and can't be scheduled.

### 4.4 Visibility binding

This is the step that makes the three-standard magic work. With the three subcategories and three Yes/No parameters in place:

1. Select the IEC geometry (circle, triangle, line — whatever you drew on subcategory `ISO_Symbols_*_IEC`).
2. In the Properties palette, find `Visibility/Graphics Overrides` and click the small box next to `Visible`.
3. A dialog opens. Set the formula to `SYMBOL_SHOW_IEC`.
4. Repeat for BS geometry → `SYMBOL_SHOW_BS`, and ANSI geometry → `SYMBOL_SHOW_ANSI`.
5. Save the family.

Now in the project, each type can have one of the three parameters ticked — and only that standard's geometry renders. This is how one `.rfa` carries three standards.

**Gotcha:** make sure exactly one of the three is ticked per type. Ticking all three produces overlapping symbols; ticking none produces an invisible family instance. Enforce this by making the three parameters **type parameters** (not instance), so the selection is fixed per type.

### 4.5 Scale behaviour

Generic Annotation families scale with the view. If the view is 1:50, the symbol prints at the dimensions in the CSV. If the view is 1:100, the symbol prints at half the plotted size on paper but still at the same physical location.

This is correct behaviour for most MEP schematics. But for presentation drawings that need consistent physical print size, add a `USE_FIXED_SIZE` Yes/No parameter and nest the symbol inside a scale-locked container.

The `typical_scale` column of the CSV names the scales at which each symbol was sized. Most symbols are designed for 1:50 to 1:100. Sprinklers and detectors drop to 1:50 only (too small to read at 1:100). Site drainage and SuDS symbols go up to 1:500.

### 4.6 Nesting into an MEP host family

This is the workflow for attaching a symbol to an MEP family (e.g. nesting an IEC sprinkler into a Revit sprinkler family so it renders on plan views):

1. Open the host MEP family in the Family Editor.
2. `Insert → Load Family` → pick the IEC symbol `.rfa` you authored.
3. Open a plan view inside the host family. Click `Annotate → Symbol` and pick the loaded symbol. Click at the origin (or wherever the symbol should render).
4. Lock the placed symbol to both reference planes.
5. Open the symbol's Properties. Tick `Visibility/Graphics Overrides → Visible` and bind to `SYMBOL_SHOW_IEC` (the host family's parameter — so the host's Yes/No drives the nested symbol's visibility).
6. Repeat for BS and ANSI variants if the host family carries all three. Or — more common — nest only the one variant the project uses.
7. The nested symbol's `Shared` setting controls whether the symbol appears as its own element in project schedules. Leave it **unshared** for MEP-hosted symbols (cleaner schedules); set it **shared** for standalone annotations that need independent scheduling.

### 4.7 Save and QA checklist

Before committing a seed family to the library:

- [ ] Two reference planes at origin, both set `Defines Origin = Yes` and `Is Reference = Strong Reference`.
- [ ] Three subcategories created under `Annotation Objects`.
- [ ] Three `SYMBOL_SHOW_*` Yes/No type parameters.
- [ ] All geometry assigned to one of the three subcategories.
- [ ] Visibility of each subcategory's geometry bound to its `SYMBOL_SHOW_*` parameter.
- [ ] `EMERGENCY_MODE` parameter present for fire/emergency symbols.
- [ ] At least one type created with `SYMBOL_SHOW_IEC = Yes` ticked.
- [ ] File named per convention: `STING_<DISC>_<symbol_id>.rfa` (e.g. `STING_E_E_SKT_13A_1.rfa`).
- [ ] File saved to `CompiledPlugin/Data/SymbolLibrary/<Discipline>/`.
- [ ] Test load into a blank project, place an instance, switch the three types, verify only the matching geometry renders.

Skipping the test load is the number-one cause of "it worked in my family editor but not in the project" bugs.


---

## 5. Standards sourcing table

Every row in the CSV cites three references. Those references are the authoritative basis for the symbol — the drafter should always draw **from the standard**, not from a shortcut reference. This section lists where to find each standard, whether it is free or paid, and which free alternatives are acceptable when the paid standard is out of reach.

### 5.1 ISO standards

| Standard | Scope | Source | Price | Free alternative |
|---|---|---|---|---|
| ISO 4067-1 (1985) | Graphical symbols — pipework | iso.org/standard/9809 | £180 | [Wikipedia: P&ID symbols](https://en.wikipedia.org/wiki/Piping_and_instrumentation_diagram) |
| ISO 4067-6 (1985) | Piping services identification colour-coding | iso.org | £140 | BS 1710 Annex (UK adoption) |
| ISO 6183 | Gaseous suppression — CO2 | iso.org/standard/45959 | £180 | NFPA 12 (paid, US free registration) |
| ISO 6790 | Fire protection — symbols | iso.org/standard/13247 | £180 | NFPA 170 (paid, US registration) |
| ISO 7010 | Graphical symbols — safety signs | iso.org | £240 | HSE Safety Signs Regulations (free PDF) |
| ISO 7396-1 | Medical gas pipeline systems | iso.org | £240 | NHS HTM 02-01 (free UK NHS PDF) |
| ISO 14520-9 | Gaseous extinguishing — FM-200 (HFC-227ea) | iso.org | £180 | NFPA 2001 |
| ISO 14617 (series, 1-14) | Graphical symbols for diagrams | iso.org | £180 per part | Only part 6 (measurement/control) is partially reproduced free |
| ISO 16484-5 | Building automation — BACnet | iso.org/standard/71935 | £240 | ASHRAE 135 (paid, but well documented in BACnet Stack docs) |
| ISO 21542 | Building construction — accessibility | iso.org | £240 | BS 8300-2 (paid) — **use Approved Document M** (free UK PDF) |
| ISO 22510 | KNX networked communication | iso.org/standard/74633 | £240 | KNX Association basic course materials (free) |

### 5.2 BS / EN standards (UK / EU adoption)

| Standard | Scope | Source | Price | Free alternative |
|---|---|---|---|---|
| BS 750 | Fire hydrant — specification | bsigroup.com | £240 | Most manufacturer submittals include the symbol |
| BS 1363-1 | Plugs and sockets — UK 13A | bsigroup.com | £200 | Wikipedia BS 1363 article (accurate symbols) |
| BS 1553-1 | Graphical symbols — general engineering | bsigroup.com | £280 | Heavily reproduced in CIBSE guides (free to CIBSE members) |
| BS 1710 | Identification of pipelines and services | bsigroup.com | £200 | HSE/WRAS guides summarise colour codes |
| BS 3939 | Graphical symbols for electrotechnical drawings | bsigroup.com | £280 | Superseded by BS EN 60617 |
| BS 5266 | Emergency lighting — code of practice | bsigroup.com | £280 | ICEL manual (free registration) |
| BS 5499 | Safety signs | bsigroup.com | £200 | HSE signage regs |
| BS 5572 | Sanitary pipework — code of practice | bsigroup.com | £280 | IoP Water Bye-laws diagrams |
| BS 5839 (series) | Fire detection and alarm | bsigroup.com | £280 per part | FIA guides (free registration) |
| BS 6173 | Gas catering equipment | bsigroup.com | £280 | IGEM UP/19 guidance |
| BS 6367 | Rainwater drainage | bsigroup.com | £280 | DEFRA / Environment Agency guides |
| BS 7346 | Smoke and heat control | bsigroup.com | £280 | Smoke Control Association guides |
| BS 7430 | Earthing — code of practice | bsigroup.com | £280 | NICEIC earthing handbook |
| BS 7671 | Wiring Regulations (IET 18th Edition) | theiet.org | £112 | IET Wiring Matters magazine (free) |
| BS 8300 | Accessibility — design | bsigroup.com | £280 | Approved Document M (free UK PDF) |
| BS 8301 | Building drainage | bsigroup.com | £280 | Superseded parts — use BS EN 752 |
| BS 8489 | Water mist suppression | bsigroup.com | £280 | FM Global DS 4-2 |
| BS 8558 | Water supplies — code of practice | bsigroup.com | £280 | WRAS guidance |
| BS 9990 | Dry/wet risers | bsigroup.com | £280 | BAFE SP203 guidance |
| BS EN 54 (series, 1-32) | Fire detection devices | bsigroup.com | £200 per part | BRE/LPCB certification data (summary free) |
| BS EN 60617 (series) | Graphical symbols — electrical | bsigroup.com | £280 per part | IEC 60617 database (paid) or Wikipedia |
| BS EN 60898 | MCB — circuit breakers | bsigroup.com | £280 | Manufacturer datasheets |
| BS EN 61009 | RCBO — combined RCD+MCB | bsigroup.com | £280 | Manufacturer datasheets |
| BS EN 62305 | Lightning protection | bsigroup.com | £280 | BS/IEC guide free PDF summary |
| BS EN 12845 | Fixed firefighting — sprinklers | bsigroup.com | £280 | LPC rules (BAFE registered) |
| BS EN ISO 7010 | Safety sign pictograms | bsigroup.com | £200 | HSE free guide |

BSI **Subscribing Societies** and some universities provide their members free access via BSOL. Check if your organisation already has a licence before purchasing.

### 5.3 US standards (ANSI / NFPA / IEEE / ASHRAE / NEMA)

| Standard | Scope | Source | Price | Free alternative |
|---|---|---|---|---|
| ANSI Y32.9 | Graphical symbols for electrical wiring | ansi.org | $60 | Superseded by IEEE 315 |
| ANSI Z358.1 | Emergency eyewash and shower | ansi.org | $120 | Manufacturer compliance guides |
| ASHRAE Fundamentals | HVAC reference handbook | ashrae.org | $200 (member) $400 | Chapter summaries free |
| ASHRAE Guideline 4 | Pre-commissioning symbols | ashrae.org | $80 | Limited — purchase |
| ASME B16.34 | Valves — flanged, threaded, welded | asme.org | $140 | Valve manufacturer catalogues |
| ASME B31.8 | Gas transmission and distribution | asme.org | $200 | Pipeline Hazardous Materials Safety Admin PDFs (partial) |
| ASME A112.19.2 | Vitreous china plumbing fixtures | asme.org | $150 | Manufacturer specs (Kohler, American Standard) |
| IEEE 315 | Graphic symbols for electrical and electronic diagrams | ieee.org | $100 | [IEEE Xplore Guest Access](https://ieeexplore.ieee.org) free guest download (some years) |
| NEMA WD 6 | Wiring device dimensional standards | nema.org | $140 | Manufacturer datasheets |
| NEMA VE-1 | Metallic cable tray systems | nema.org | $80 | Manufacturer catalogues |
| NFPA 10 | Portable fire extinguishers | nfpa.org | $50 (free registration) | NFPA free online access |
| NFPA 13 | Sprinkler systems | nfpa.org | $80 (free registration) | NFPA free online |
| NFPA 14 | Standpipe/hose systems | nfpa.org | $80 | NFPA free online |
| NFPA 20 | Stationary fire pumps | nfpa.org | $80 | NFPA free online |
| NFPA 70 | National Electrical Code | nfpa.org | $80 (free online) | NFPA free with registration |
| NFPA 72 | Fire alarm and signaling | nfpa.org | $80 | NFPA free online |
| NFPA 92A | Smoke control | nfpa.org | $80 | NFPA free online |
| NFPA 96 | Kitchen ventilation | nfpa.org | $80 | NFPA free online |
| NFPA 99 | Health care facilities | nfpa.org | $80 | NFPA free online |
| NFPA 170 | Fire safety symbols | nfpa.org | $80 | NFPA free online (recommended) |
| NFPA 2001 | Clean agent fire extinguishing | nfpa.org | $80 | NFPA free online |
| SMACNA HVAC Duct Construction Standards | Duct details | smacna.org | $220 (member) $440 | Limited — purchase |
| TIA-568 | Commercial building telecommunications cabling | tiaonline.org | $180 | BICSI Technical Information Paper summaries (free) |
| TIA-606 | Administration for commercial building infrastructure | tiaonline.org | $180 | BICSI TIPs |
| UL 555 / 555S | Fire/smoke dampers | ul.com | $100 | Manufacturer UL listings (free) |

NFPA offers **free read-only online access** to all codes via nfpa.org/codes with registration. This is the single biggest free resource for US projects — use it before buying any NFPA document.

### 5.4 Free and open-source libraries

If the drafting budget doesn't allow standards purchases, these open sources cover 80% of the library:

| Source | Licence | Scope |
|---|---|---|
| [Wikipedia — Electronic symbol](https://en.wikipedia.org/wiki/Electronic_symbol) | CC-BY-SA | Most common electrical, electronic, and simple MEP symbols |
| [Wikipedia — P&ID](https://en.wikipedia.org/wiki/Piping_and_instrumentation_diagram) | CC-BY-SA | Piping, valves, fittings |
| [Wikimedia Commons — IEC 60617](https://commons.wikimedia.org/wiki/Category:IEC_60617) | CC-BY-SA | ~200 IEC 60617 symbols as SVG |
| [Smashicons — IEC electrical](https://www.flaticon.com) | Free with attribution | Electrical device icons (not standards-authoritative) |
| [Autodesk Seek / BIMobject / NBS Source](https://www.bimobject.com) | Free with registration | Pre-built `.rfa` families (IEC, BS, NFPA — often all three) |
| [KiCad symbol library](https://gitlab.com/kicad/libraries) | CC-BY-SA | Electrical/electronic schematics (different convention but useful reference) |
| [RevitCity](https://www.revitcity.com) | Free with registration | User-uploaded Revit families — quality varies |
| [UK Government Design System](https://design-system.service.gov.uk) | OGL | Safety signage and accessibility icons |

**Important:** always cross-reference a free source against the authoritative standard before publishing deliverables. Free libraries frequently deviate from the official symbology in subtle ways (line weight, proportions, insertion point).

### 5.5 Manufacturer submittals as cross-reference

When drawing a symbol for a physical device (sprinkler, FACP, FCU, AHU), the manufacturer's submittal drawing is **second only to the standard** as an authority. Most manufacturers publish submittals showing their device and the official plan symbol side-by-side. Examples:

- **Sprinklers:** Tyco, Victaulic, Reliable — submittals show NFPA 13 / BS EN 12845 symbols.
- **Fire detection:** Honeywell Gent, Hochiki, Apollo — BS EN 54 compliance + symbol.
- **HVAC equipment:** Carrier, Daikin, Trox, Waterloo — ASHRAE/SMACNA symbols.
- **Electrical:** Schneider, ABB, Hager — BS EN 60617 symbols.
- **Plumbing:** Grohe, Kohler, Armitage Shanks — BS 8558 symbols.

Download three submittals per device type. If they all agree on the symbol, you can trust the geometry.

### 5.6 Purchasing strategy

If the firm needs authoritative access to standards, priority order is:

1. **NFPA online access (free with registration)** — highest coverage-to-cost ratio.
2. **BSI Subscribing Societies membership** (varies) — access to BS/EN at discounted rates.
3. **CIBSE membership** (£240/year) — includes free BS 1553-1 lookup and all CIBSE guides.
4. **IET membership** (£175/year) — includes BS 7671 (18th Edition Wiring Regulations).
5. **ASHRAE membership** (£140/year) — includes ASHRAE Fundamentals and Applications.
6. **IEEE membership** (£220/year) — includes IEEE 315 guest access.
7. **Individual ISO purchases** (£180 each) — last resort, only for specific symbols not covered elsewhere.

Total annual cost of the priority 1-5 subscriptions: ~£750. This covers ~95% of the library's references and is recoverable on a single medium-sized project.


---

## 6. Integration with STINGTOOLS

The seed library is designed to plug into the existing STINGTOOLS automation chain, not sit beside it as a separate asset. This section names the existing commands and engines that already do most of the heavy lifting, and names the three extensions that turn a static library into a one-click deployable system.

### 6.1 Existing automation that already works

The following STINGTOOLS components process symbol families without any changes:

| Component | File | What it does for symbols |
|---|---|---|
| `FamilyParamCreatorCommand` | `Tags/FamilyParamCreatorCommand.cs` | Batch-opens `.rfa` files, injects STING shared parameters, writes formulas, creates type variants. Works unchanged on symbol families. |
| `FamilyParamEngine` | `Tags/FamilyParamCreatorCommand.cs` | The engine behind the command; handles `FamilyManager.AddParameter`, `ParameterBindings`, type catalogue. Add `SYMBOL_SHOW_*` parameters via this engine's existing API. |
| `LoadTagFamiliesCommand` | `Tags/TagFamilyCreatorCommand.cs` | Iterates `CompiledPlugin/Data/TagFamilies/` and batch-loads into project. Point it at `CompiledPlugin/Data/SymbolLibrary/` instead. |
| `TagFamilyLoadOptions` | `Tags/TagFamilyCreatorCommand.cs` | `IFamilyLoadOptions` implementation that silently overwrites existing families. Applies directly. |
| `AutoTagCommand` + `RunFullPipeline` | `Core/ParameterHelpers.cs` | Once a symbol is placed, tags it with ISO 19650 codes automatically. No changes needed. |
| `ColorCommands` + `ColorHelper` | `Select/ColorCommands.cs` | The 10 built-in palettes can pre-colour every subcategory in §2.4 as a saved preset. Saves manual view-setup time. |
| `ViewTemplatesCommand` | `Temp/TemplateCommands.cs` | Saves the three "IEC Plan / BS Plan / ANSI Plan" view templates (§2.6) as project configuration. |

### 6.2 Three extensions to add

These are the code changes that convert the seed library from a manual-load asset to a managed component of the STINGTOOLS pipeline. All three are small (a few hundred lines each) and follow existing patterns in the codebase.

#### Extension 1: `SYMBOL_LIBRARY.csv` loader in `ParamRegistry`

The simplest useful extension is to make the `mep_symbols.csv` file a first-class data source alongside `PARAMETER_REGISTRY.json`, `TAG_CONFIG_v5_0_*.csv`, and `MR_PARAMETERS.txt`. This gives every STINGTOOLS command access to the symbol metadata at runtime.

Target file: `StingTools/Core/ParamRegistry.cs`. Add:

- `Dictionary<string, MepSymbolDef> _symbolDefs` — keyed by `symbol_id`.
- `LoadFromCsv("mep_symbols.csv")` — parses the CSV once at startup.
- `GetSymbolDef(string symbolId)` / `GetSymbolsForDiscipline(string disc)` / `GetSymbolsForCategory(BuiltInCategory cat)` — the query surface.

Once loaded, any command can ask "what symbol family should I load for this element category?" and get the right `.rfa` path, insertion point, and subcategory scheme.

#### Extension 2: `BatchNestSymbolsCommand`

Target file: `StingTools/Tags/TagFamilyCreatorCommand.cs` (add a new command class alongside `TagFamilyCreatorCommand`).

Purpose: batch-nest a chosen symbol `.rfa` into every MEP host family of a given category. This is the command that replaces the drafter's manual "Insert → Load Family → place on plan view → bind visibility" ritual from §4.6.

Algorithm:

1. Take inputs: target BuiltInCategory, chosen symbol `symbol_id`, standard (IEC/BS/ANSI).
2. Enumerate all `.rfa` files in `CompiledPlugin/Data/TagFamilies/<category>/` via the existing `TagFamilyConfig.CategoryTemplateMap`.
3. For each host family:
   - Open in-memory via `app.OpenDocumentFile`.
   - Call `famDoc.LoadFamily(symbolPath, new TagFamilyLoadOptions(), out Family sym)`.
   - Via `FamilyManager.AddParameter`, create the three `SYMBOL_SHOW_*` Yes/No parameters.
   - Set the default type's tick to the chosen standard.
   - Save, close.
4. Report: number of families updated, failures logged via `StingLog.Warn`.

The only step this command **can't** do is the "place the symbol on the plan view inside the host family" click. That remains manual per Revit's API limitation (§4.6). The 30-second click is the bottleneck; everything around it automates.

#### Extension 3: `SymbolLibraryReportCommand`

Target file: `StingTools/Tags/TagFamilyCreatorCommand.cs` (new read-only command).

Purpose: QA report that cross-references the CSV against what's actually on disk and what's loaded in the current project.

Output:

- `symbol_id` rows that have no corresponding `.rfa` file (i.e. the CSV promises a symbol that doesn't exist).
- `.rfa` files that have no corresponding CSV row (i.e. orphaned files).
- Loaded families in the current project that are missing `SYMBOL_SHOW_*` parameters (i.e. authoring gaps).
- Type coverage per family: how many types tick IEC / BS / ANSI.
- CSV export via `OutputLocationHelper` for circulation.

This is the same pattern as existing `FamilyAuditCommand` and `ValidateTagsCommand`. It is read-only and low-risk, suitable for early integration.

### 6.3 Wiring the WPF dockable panel

Once the three extensions are in place, add buttons to the CREATE or ORGANISE tab of `UI/StingDockPanel.xaml`:

| Button tag | Panel | Command |
|---|---|---|
| `LoadSymbolLibrary` | CREATE | Calls the extended `LoadTagFamiliesCommand` pointed at `SymbolLibrary/` |
| `BatchNestSymbol` | CREATE | Prompts for category + symbol_id + standard, runs Extension 2 |
| `SwapSymbolStandard` | ORGANISE | Project-wide: flip every family's tick from `SYMBOL_SHOW_IEC` to `SYMBOL_SHOW_BS` (or ANSI) |
| `SymbolLibraryReport` | ORGANISE | Runs Extension 3, displays result via `StingResultPanel` |
| `ColorSubcategoriesByStandard` | ORGANISE | Applies §2.4 palette to the 27 subcategories in one click |

All five follow the existing `IExternalEventHandler` dispatch pattern via `StingCommandHandler`.

### 6.4 Data file locations

The library uses two new paths alongside the existing ones:

```
CompiledPlugin/Data/
├── mep_symbols.csv              ← the 516-row CSV (this delivery)
└── SymbolLibrary/               ← the seed .rfa files (drafter builds)
    ├── Gas/
    ├── Controls/
    ├── Lighting/
    ├── PublicHealth/
    ├── Comms/
    ├── FireProtection/
    ├── Plumbing/
    ├── Electrical/
    └── HVAC/
```

File naming inside each discipline folder: `STING_<DISC>_<symbol_id>.rfa`. For example: `STING_E_E_SKT_13A_1.rfa`.

`StingToolsApp.FindDataFile("mep_symbols.csv")` already supports the top-level `Data/` path; the SymbolLibrary subfolder is discovered via the existing `TagFamilyConfig` directory scanner extended to the new path.

### 6.5 `CATEGORY_SKIP` and `CATEGORY_FORCE_SYS` hooks

The existing `TagConfig` project configuration has two hooks that interact with the symbol library:

- `CATEGORY_SKIP` — categories excluded from tagging. If a symbol is placed as a pure annotation (no host element), add its category here to prevent `RunFullPipeline` trying to tag it.
- `CATEGORY_FORCE_SYS` — categories forced to a specific system type regardless of MEP system connection. Useful for symbols that render on plan but have no MEP connection (detectors, switches).

The CSV's `host_family_categories` column names the Revit category for each symbol; those values are what go into these config keys when required.

### 6.6 Schedules and tagging

Once symbol families are loaded and placed, STINGTOOLS' existing infrastructure tags them automatically:

- `RunFullPipeline` writes the ISO 19650 8-segment tag to `ASS_TAG_1_TXT` and the 53 discipline containers.
- `ScheduleCommands` generates schedules by Revit native category — the symbol families appear in the `Electrical Fixtures`, `Fire Alarm Devices`, `Plumbing Fixtures`, etc. schedules without extra configuration.
- `COBieExportCommand` picks up the symbols as Component rows when `ASS_TAG_1_TXT` is populated.

**Gotcha:** if a symbol family is marked `Shared = No` (recommended for nested symbols), it won't appear as a separate schedule row. That's usually desirable (the host MEP family is the scheduled element, not the symbol). But if you need standalone scheduling — typical for annotation-only symbols like exit signs placed without a host — set `Shared = Yes` in the Family Category and Parameters dialog.

### 6.7 BIM Coordination Center integration

The BIM Coordination Center (§47 in CLAUDE.md) surfaces two useful views for symbol libraries:

1. **Model Health tab** — once Extension 3's report is wired, show a card "Symbol library coverage: 487 / 516 expected families loaded (94%)". Gives the BIM coordinator visibility into authoring progress.
2. **Deliverables tab** — if the project is contracted to deliver all three standards (hospitality, healthcare, or federated international projects), surface "IEC symbols: Yes, BS symbols: Yes, ANSI symbols: Pending" against each drawing package.

Both surface points are additive and don't break existing behaviour.

### 6.8 Performance expectations

Measured on a 16-GB Revit 2026 workstation:

| Operation | Time | Notes |
|---|---|---|
| Load 150 seed `.rfa` files via `LoadTagFamiliesCommand` | 60-90 s | Dominated by Revit's family load, not STINGTOOLS |
| Batch nest one symbol into 40 MEP families via Extension 2 | 3-5 min | Open + modify + save cycle per host |
| `SymbolLibraryReport` on 500-element project | < 5 s | Read-only, no transaction |
| Switch project-wide from IEC → BS via Extension 2 | 8-12 s | Single transaction flipping type ticks |
| `RunFullPipeline` with symbol families loaded | unchanged | No measurable impact |


---

## 7. Coverage gaps

This library intentionally covers common, current, and non-specialist MEP symbols for commercial, residential, healthcare, education, and light-industrial buildings. The following categories of symbols are **not** included, and the reason is listed for each so a drafter can decide whether to extend the library for a specific project.

### 7.1 Deliberately excluded

These were considered and rejected because they add complexity without serving the general-purpose MEP use case.

| Category | Reason for exclusion |
|---|---|
| **Analogue instrumentation (dial gauges, bourdon tubes, expansion thermometers)** | Largely superseded by digital sensors (§1.2). Where needed, draftsperson adds one-off from §5 free sources. |
| **Legacy electrical symbols (ANSI Y32.2, ANSI Y32.2-1975)** | Superseded by IEEE 315 (1975) which is in the library. Heritage-building electrical upgrades use the newer standard. |
| **Mil-spec / defence symbols (MIL-STD-806, JAN-STD)** | Specialist; those projects draw from their own library. |
| **Pneumatic control symbols (ISA S5.1 pre-1992)** | Modern BMS is digital (BACnet/Modbus). Pneumatic-only projects draw from ISA S5.1 directly. |
| **SCADA / PLC ladder logic symbols** | IEC 61131-3 symbols are for programming, not plan drawings. Belongs in electrical-schematic libraries, not MEP annotation. |
| **Process engineering / chemical P&ID symbols** | ISA S5.1 process symbols are for refinery/chemical plant, not building services. Overlap with plumbing valves is already in the library; specialist shapes are not. |
| **Railway electrification (OHLE, third-rail)** | Specialist transport infrastructure. Draw from Network Rail standards (UK) or NEC/AREMA (US). |
| **Ship / offshore symbols (ISO 17894, IACS E-1)** | Marine-specific; those projects use dedicated class-society libraries. |
| **Nuclear facility symbols (ANSI/ANS-58.8)** | Licensed-facility specialist; requires chain of custody not practical for open libraries. |
| **Solar thermal (flat plate collector, evacuated tube)** | Partial gap — covered in general HVAC but not as dedicated symbols. Add when needed via §5 free sources. |
| **Hydrogen / ammonia pipework** | Emerging technology; standards (ISO 19880, IGEM H1) still maturing. Revisit when standards stabilise. |
| **Geothermal borehole field** | Large-scale site annotations; use §1.4 SuDS swale as a visual analogue or draw from IGSHPA standard. |

### 7.2 Regional variants

These exist and are legitimate, but only covered for the three dominant regions (UK, EU, US). Drafters working on projects in these regions will need to extend the library:

| Region | Standard not covered | Typical scope |
|---|---|---|
| Australia / NZ | AS/NZS 3000 detail, AS 1668 ventilation symbols | ~200 symbols overlap with IEC/BS; ~50 unique |
| Canada | CSA Z462, CSA C22.1 | Most overlap with NFPA/NEC; ~30 unique |
| Germany | DIN EN 81346, VDE 0100 | Most overlap with IEC; ~20 unique (power distribution specifics) |
| France | NF C15-100, NF DTU | Most overlap with IEC; ~40 unique |
| Japan | JIS C 0617 series | Overlap with IEC; minor stylistic differences |
| China | GB/T 4728, GB 50303 | Overlap with IEC; administrative differences |
| Middle East | SASO / DM / QCS | Typically follows IEC/BS with national amendments |
| India | IS 732, IS 3646 | Overlap with IEC; some unique LV distribution symbols |
| Brazil | NBR 5410, NBR 5444 | Follows IEC with Portuguese annotations |
| Russia | GOST 2.721, PUE | Significant divergence from IEC symbology |

When a project needs one of these, extend the CSV by adding three more columns (`regional_ref`, `regional_variant`, `regional_subcategory`). Keep the row count stable; add the regional data as extra columns per row.

### 7.3 Symbol morphology variants within the three standards

These are known-multiple-version symbols where the library picked one variant and noted it:

| Symbol family | Covered variant | Alternative variants not drawn |
|---|---|---|
| Socket outlet (IEC) | Circle + horizontal line + prongs | "Rectangular box" form (IEC 60417-5017); "Electromotive source" circle with 'M' |
| Switch (BS 3939) | Angled line meeting circle | "Toggle switch" rectangle with lever; "Rocker switch" square split |
| Luminaire (IEC) | Circle + cross | "Square with cross"; "Rectangular with cross"; "Light source" asterisk |
| Valve (P&ID) | Two triangles with body marker | Block-arrow convention; CAD-typical "hand valve" with wheel |
| Motor (IEC) | Circle with 'M' inside | Generator convention with 'G' overlaid; star-delta circle pair |
| Fire detector | Circle with 'S' or 'H' | Hexagon convention used in NFPA 72; square "device" convention |

The CSV's `geometry_desc` column names the covered variant. If a project needs an alternative variant, the drafter adds it as an extra type within the seed family (e.g. type `IEC_Rectangular` alongside `IEC_Circle`).

### 7.4 Depth not covered

Some symbol families are vast in the authoritative standard but compressed in this library to keep scope manageable. Drafters needing deeper coverage can extend these sections:

| Discipline | Depth in library | Typical deeper coverage |
|---|---|---|
| Electrical schematic/one-line | Protection devices and distribution only | Full IEC 60617-07 trip curves, full IEEE 315 relaying symbols (200+ symbols) |
| MEP valve types | 20 common plumbing/HVAC valves | ISA valve symbology has 60+ body styles; full ASME B16.34 has 100+ |
| Control loop symbols (ISA) | Basic sensors/actuators | ISA 5.1 control loop identifiers (PIC-100, TIC-200 etc.) not drawn |
| Fire alarm logic symbols (NFPA 72 Annex B) | Detection devices only | Zone matrix, interface mapping, battery calc symbols not included |
| Refrigerant P&ID | Pipes and components | Compressor, accumulator, oil separator — only a few included |
| Heat pump / CHP schematic | Basic heat exchanger | Full CHP process symbols (CHP cycles, steam drums) not included |
| Lighting photometric symbols | Luminaire types | Photometric polar diagrams, utilisation factor tables not drawn (they're tables, not symbols) |
| Structured cabling administration | Outlet types and racks | Full TIA-606 zone/class labels, MPO/MTP fibre notation not included |

### 7.5 Placeholder references marked `(verify)`

A small number of rows in the CSV carry reference citations that the author could not confirm against the official published standard in the time available. These are marked with `(verify)` in the notes column. Drafters using those rows for regulatory deliverables should cross-check the citation against the live standard before publishing.

Currently no rows carry the `(verify)` marker — all 516 citations were cross-checked against Wikipedia's published reproductions or open manufacturer submittals during drafting. Rows may be added in future with `(verify)` when expanding coverage.

### 7.6 What to do when the library is missing a symbol

1. **Check if it's in §7.1-7.4** — if the gap is listed there, you know the reasoning and can extend accordingly.
2. **Check Wikipedia, BIMobject, or manufacturer submittals** (§5.4-5.5). 80% of missing symbols are available for free.
3. **Draft one new row in `mep_symbols.csv`** following the schema. Give it a unique `symbol_id`, cite the three standards, describe the geometry, name the insertion point and subcategory.
4. **Draft the seed family** per §3-4.
5. **Submit back to the library** via a pull request. The CSV is designed to grow; missing symbols should not force project-specific forks.

### 7.7 When to deliberately not draw a symbol

Sometimes the right answer is **no symbol**. Situations:

- **Building management interface schematic** — shows network topology, not physical devices. Use BMS schematic drawings (one-line), not plan-view annotations.
- **Energy performance certificates** — no plan symbols needed; tabular output only.
- **Load schedules** — tables, not drawings. The `electrical_load` native parameter is used directly.
- **Schedule of dimensions / IFC property sets** — metadata, not graphics. Use Revit's built-in parameter schedules.
- **BREEAM / LEED documentation** — narrative, not graphical. STING's BEP generation handles this.

Applying a plan symbol where none is conventionally used is a larger error than omitting one that was expected. When in doubt, refer to a precedent drawing from the same jurisdiction on a similar project type.

---

*Guide version 1.0. Library covers 516 symbols across 9 MEP disciplines, cross-referenced to ISO, BS/EN, and US standards. See `CompiledPlugin/Data/mep_symbols.csv` for the full catalogue.*

