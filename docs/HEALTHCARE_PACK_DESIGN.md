# Healthcare Pack — Design Document

A multi-phase plan to extend StingTools with a complete **Healthcare /
Hospital Design** content layer that plugs into the plugin's existing
data-driven subsystems (parameter registry, tag taxonomy, drawing
template manager, filter library, view-style packs, validators,
template-engine v1.1, BIM Coordination Centre, mobile app, Planscape
server) without replacing any of them.

This is a research + design document. **No code yet.** Every file path
listed under "Where it lands" is an *intended* location; the actual
implementation should follow the phasing in §10.

> Last refreshed 2026-05-08 (gap analysis + IoT cross-check complete — §19 added, H-23..H-30 phased). Branch: `claude/research-hospital-design-0Uxbi`.

---

## 1. Why a Healthcare Pack

Hospitals are the single most regulated and most BIM-information-intensive
building type in the AEC sector. The current StingTools content layer is
discipline-aware (M/E/P/A/S/FP/LV/G) but **building-type-agnostic**:

- The 2,555-row `MR_PARAMETERS.txt` registry has *seeds* of healthcare
  intent — `WARN_BLE_MEDICAL_CLEARANCE_MEDICAL_EQUIP`,
  `WARN_BLE_MEDICAL_POWER_MEDICAL_EQUIP`, `WARN_RGL_STD_MEDICAL_EQUIP`,
  `WARN_ASS_LEAD_TIME_SPECIALTY_EQUIP` — but no first-class clinical
  attributes. There is no clinical room class, no pressure regime,
  no medical-gas type, no NFPA 99 essential-power branch, no NCRP 147
  lead-equivalence, no MRI zone, no infection-control class, no TMV3
  dead-leg, no HBN / HTM / FGI / ADB cross-reference. (See §5.0 for
  the full crosscheck — the surviving net-new parameter count after
  reusing existing params is **~85**, not 140 as the first draft
  estimated.)
- The 199-filter `STING_AEC_FILTERS.json` has zero healthcare filters.
- The 11 ViewStylePacks include no clinical or shielding presentation
  pack.
- The 40 corporate Drawing Types include no Room Data Sheet, no
  medical-gas schematic, no essential-power riser, no MRI-zone overlay.
- The 22 COBie presets and 70-row `COBIE_TYPE_MAP.csv` carry no
  clinical-equipment classifications and no SFG20-Healthcare schedule
  references.
- Five v4 validators cover connectivity / fill / spec / termination /
  slope. None covers AIIR pressure cascade, EES branch wiring,
  HTM 04-01 dead-leg distance, NCRP 147 shielding sufficiency, or
  ADB room-data-sheet completeness.

The Healthcare Pack closes that gap **without replacing the rails** —
every artefact slots into an existing extension point.

---

## 2. Standards landscape

| Authority | Document | Scope | Why StingTools needs to encode it |
|---|---|---|---|
| NHS England | HTM 00 | Healthcare buildings — best practice umbrella | Compliance gating |
| NHS England | HTM 02-01 Pts A/B | Medical gas pipeline systems | Parameter schema, tag taxonomy, schematics |
| NHS England | HTM 03-01 Pts A/B | Specialist ventilation in healthcare premises | Pressure regime + ACH validators |
| NHS England | HTM 04-01 Pts A/B/C | Safe water in healthcare premises | TMV / dead-leg / *Pseudomonas* validators |
| NHS England | HTM 05-01 / 05-02 | Firecode — managing fire safety / functional provisions | Fire-compartment + PHE strategy |
| NHS England | HTM 06-01 | Electrical services supply and distribution | EES branches, IPS, isolated power |
| NHS England | HTM 07-01 / 07-04 / 07-07 | Waste, water, sustainable energy (FM-side) | COBie + sustainability hooks |
| NHS England | HTM 08-01 / 08-02 | Acoustics / lifts in healthcare | Acoustic engine + bedlift schedules |
| NHS England | HBN 00-01 | General design guidance | Departmental adjacency baseline |
| NHS England | HBN 00-04 | Circulation and communication spaces | Patient/clean/dirty flow |
| NHS England | HBN 03-01 | Adult acute mental health | Anti-ligature parameter pack |
| NHS England | HBN 04-01 | Adult in-patient facilities | Bedhead trunking + isolation |
| NHS England | HBN 09-02 | Critical care | ICU column + monitoring |
| NHS England | HBN 11-01 | Primary and community care | Departmental adjacency |
| NHS England | HBN 13 | Sterile services / HSDU | Decon flow |
| NHS England | HBN 16 | Mortuary and post-mortem | Specialist room types |
| NHS England | HBN 21 / 22 | Maternity / day surgery | Specialist room types |
| NHS Estates | ADB / Activity DataBase | Per-room equipment, services, finishes | RDS renderer, room-type catalogue |
| FGI (US) | Guidelines (→ Facility Code 2026) | Hospitals, outpatient, residential, behavioural | Cross-reference param + validator pack |
| NFPA | NFPA 99 (Healthcare Facilities Code) | Medical gas, EES, IPS, anaesthetising locations | Validators + tag taxonomy |
| NFPA | NFPA 110 | Standby power | EES generator schedules |
| NEC | NEC 517 | Health-care occupancies (electrical) | Branch + IPS rules |
| NCRP | Report 147 | Shielding for medical X-ray | Lead-equivalence calc |
| NCRP | Report 151 | Megavoltage radiotherapy shielding | LINAC vault calc |
| ICRP | IRR17 (UK) | Ionising Radiations Regulations 2017 | Designation params (controlled / supervised) |
| ASHRAE / ASHE | ANSI/ASHRAE/ASHE 170 | Ventilation of healthcare facilities | ACH + pressure validator |
| ISO | 14644-1/3/4 | Cleanrooms — classification & recovery | OR ultra-clean validator |
| ISO | 7396-1 | Medical gas pipeline systems | Parameter GUIDs + alarm rules |
| BS EN | 1822 / 13779 / 13053 | HEPA filtration / non-residential vent / AHU | Filter grade params |
| BS | 5682 | Terminal units for medical gas | Family parameter spec |
| BS | 6465 | Sanitary installations (incl. healthcare) | Existing — extend |
| BS | 7671 | Wiring regulations (already in repo) | Cross-reference EES branches |
| BS | 8300 | Accessibility (already implicit) | Healthcare-specific clauses |
| BS | 9999 | Fire safety code of practice | Compartment + PHE |
| BS EN | 15224 | Quality management for healthcare | QA hooks (→ existing QA module) |
| USP | <797> | Sterile compounding | Pharmacy cleanroom params |
| USP | <800> | Hazardous drug handling | C-PEC / C-SEC params |
| USP | <795> | Non-sterile compounding | Pharmacy params |
| Joint Commission | EC.02.05.* | Environment of Care (US) | Audit hooks |
| CDC | Guidelines for Environmental Infection Control | AIIR / PE / decon | Room-class enum |
| iHFG | Parts A/B/C/D | International Health Facility Guidelines (TAHPI) | Specialist room types |
| SFG20 | Healthcare schedule pack (100+) | Maintenance schedules | COBie job-template overlay |
| NHS England | HTM 01-01 | Decontamination in primary care (non-dental) | Washer-disinfector params + flow validator |
| NHS England | HTM 01-05 | Decontamination in primary care dental | Dental decon room type |
| NHS England | HTM 01-06 Pts A/B/C | Decontamination of flexible endoscopes | Endoscope RFID traceability (H-26) |
| NHS England | HTM 06-02 | Medical electrical equipment | Safety clearance params + `ELC_MED_ELEC_CLEARANCE_M_NR` |
| NHS Scotland | SHTM 01-06 (adds TOE probe) | Scottish decon variants | Regional flag `PRJ_ORG_HEALTH_HTM_REGION_TXT` |
| NHS Wales | WHTM series | Welsh HTM variants | Same regional flag |
| NHS Northern Ireland | Regional guidance | NI variants | Same regional flag |
| ASSE | 6030 / 6040 | Medical gas verifier / maintenance credentials | `MGS_VERIFY_ASSE_CERT_TXT` on verification log |
| JCI | EC.02.05 / EC.02.06 | Environment of Care; pre-construction risk | Infection-control barrier params; construction-phase risk enum |
| IEC | 60601-1 | Medical electrical equipment safety | Safety clearance; `ELC_IPS_BOOL` already covers Part 2-25 |
| ISO | 15189 | Medical laboratory accreditation | Lab room class `LAB-WET` / `LAB-DRY` params |
| NFPA | 13 | Sprinkler systems | Healthcare sprinkler exemptions by room class |
| ASHRAE | 188 | Legionella prevention in building water | `PLM_SENTINEL_BOOL` + dead-leg validator |
| NFPA | 110 | Emergency and standby power | Generator test log; `ELC_GEN_TEST_LOG_REF_TXT` |
| ASSE | 6030 / 6040 | MGPS verifier credentials | `MGS_VERIFY_ASSE_CERT_TXT` |

The pack ships **schemas and hooks**, never the licensed prose. Where a
standard sits behind a paywall (SFG20, ADB content), the pack carries
identifiers and structure only; users supply their licensed copies into
the project sandbox at `<project>/_BIM_COORD/healthcare/`.

---

## 3. Where it slots into the existing architecture

| StingTools subsystem | Healthcare extension point |
|---|---|
| `Core/ParamRegistry.cs` + `Data/PARAMETER_REGISTRY.json` | Adds **~85 net-new shared parameters** (after crosscheck — see §5.0) into 5 new groups (28 `CLN_CLINICAL`, 29 `MGS_SYSTEMS`, 30 `RAD_PROTECTION`, 31 `CEQ_CLINICAL`, 32 `LIG_BEHAVIOURAL`) plus extensions to existing groups 4 (`ELC_PWR`), 5 (`HVC_SYSTEMS`), 6 (`PLM_DRN`), 8 (`FLS_LIFE_SFTY`), 13 (`PRJ_INFORMATION`), 25 (`COMMISSIONING`); 6 new SeqScheme variants |
| `Core/TagConfig.cs` + `Data/TAG_CONFIG_v5_0_*.csv` | Adds `H` and `MG` disciplines, `MGAS-*` system family, ~40 product codes, ~20 function codes, 12 clinical room-class enums |
| `Core/Drawing/` (Drawing Template Manager) | Adds 12 healthcare drawing types, 4 healthcare ViewStylePacks, 1 routing-rule supplement |
| `Core/Drawing/AecFilterDefinition.cs` + `Data/STING_AEC_FILTERS.json` | Adds 60 healthcare filters (pressure regime, EES branches, MRI zones, infection-control class, decon flow, fire compartment, anti-ligature, MGAS by gas) |
| `Core/Validation/` (5 v4 validators) | Adds 8 healthcare validators (see §6) |
| `Core/Fabrication/` | Adds discipline pack `Fabrication.MedicalGas` for MGPS prefab spool packages |
| `Core/Placement/` (rule + scorer) | Adds ~25 healthcare placement rules (pendant, scrub trough, bed-head, AED cabinet, hand-rub dispenser, fire pull) |
| `Docs/Templates/` (template engine v1.1) | Adds 14 healthcare templates including the **Room Data Sheet (E17)** family |
| `Docs/Workflow/` | Adds 6 healthcare workflow JSONs (commissioning, MGPS verification, pressure-regime audit, RDS issue, water-safety audit, anti-ligature audit) |
| `BIMManager/` (BIM Coordination Centre) | Adds 14th tab "Healthcare" surfacing pressure / EES / MGPS / RDS dashboards |
| `BIMManager/RevisionManagementCommands.cs` | Healthcare-specific suitability codes (S0-S7 retained, plus clinical sign-off `CS1`/`CS2`) |
| `BIMManager/SchedulingCommands.cs` (4D / 5D) | Healthcare commissioning gantt template (HBN 00 driven) |
| `Standards/` | New namespaces: `HTM/`, `HBN/`, `FGI/`, `NFPA99/`, `NCRP147/`, `ASHRAE170/`, `USP797800/` |
| `Planscape.Server/src/Planscape.API/Controllers/` | New `HealthcareController` (pressure logs, MGPS verification records, RDS retrieval) |
| `Planscape/app/` (mobile) | New tab `healthcare/` with on-site MGPS commissioning checklist + pressure-cascade live read |
| `Planscape.MIM/` | Healthcare asset overlay — adds clinical-equipment categories to `Asset` entity |

No subsystem is replaced. Every artefact is additive.

---

## 4. Tag taxonomy extensions (Phase H-1)

### 4.1 New disciplines

| Code | Long form | Notes |
|---|---|---|
| `H`  | Healthcare / Clinical equipment | Pendants, scrub troughs, bed-head trunking, dialysis stations, nurse-call posts |
| `MG` | Medical gas | MGPS pipework, terminal units, zone valves, alarm panels |
| `RP` | Radiation protection | Shielding, dosimetry posts, MRI fences, controlled-area signage |

`A`, `M`, `E`, `P`, `S`, `FP`, `LV` retain existing semantics; the new
disciplines do not poach categories. Pendants and bed-heads are
*authored* under `H` even though they carry MEP services, because their
ownership belongs to the clinical fit-out package.

### 4.2 New systems (`SysMap`)

| System code | Family | Member categories |
|---|---|---|
| `MGAS-O2`  | Medical gas — Oxygen 400 kPa | Pipe, FlexPipe, MechanicalEquipment, GenericModel |
| `MGAS-MA4` | Medical air 400 kPa | as above |
| `MGAS-MA7` | Surgical air 700 kPa | as above |
| `MGAS-N2O` | Nitrous oxide | as above |
| `MGAS-N2`  | Surgical nitrogen | as above |
| `MGAS-CO2` | Carbon dioxide | as above |
| `MGAS-HE`  | Helium / Heliox | as above |
| `MGAS-VAC` | Medical vacuum | as above |
| `MGAS-AGS` | Anaesthetic gas scavenging | as above |
| `MGAS-DENT`| Dental compressed air / vacuum (when present) | as above |
| `EES-LS`   | Essential power — Life Safety branch | ElectricalEquipment, Wires, Conduits, ElectricalFixtures |
| `EES-CR`   | Essential power — Critical branch | as above |
| `EES-EQ`   | Essential power — Equipment branch | as above |
| `IPS-WET`  | Isolated power — wet location | as above |
| `IT-CARD`  | IT-network — cardiac protected | as above |
| `WAT-DCW-TMV` | TMV-tempered cold water | Pipe |
| `WAT-DHW-AC`  | Augmented-care DHW | Pipe |
| `RAD-X`   | X-ray controlled area | Walls, Doors, Windows, GenericModel |
| `RAD-MV`  | Megavoltage controlled area (LINAC) | as above |
| `RAD-MRI` | MR Faraday cage | as above |

### 4.3 New functions (`FuncMap`)

`AIIR`, `PE`, `ANTERM` (anteroom), `ULTRACL` (ultra-clean OR), `LIFE-SAF`,
`CRIT`, `EQP-BR`, `IPS`, `IT-CP`, `DECON-DTY`, `DECON-CLN`, `DECON-STR`
(sterile), `SCRUB`, `MORT`, `RES`, `DIA-RO`, `PH-CSP-797`, `PH-CSP-800`,
`HBO`, `LIG-RES` (ligature-resistant), `PSY-OBS` (observation),
`BARI` (bariatric).

### 4.4 New product codes (excerpt)

`ZVB` (zone valve box), `AAP` (area alarm), `MAP` (master alarm),
`TU-O2`, `TU-MA4`, `TU-MA7`, `TU-VAC`, `TU-AGS`, `BHP` (bed-head pendant),
`CP-OR` (ceiling pendant — OR), `CP-ICU`, `CP-ANES` (anaesthesia boom),
`SCR` (scrub trough), `SCRD` (scrub-in dispenser), `BPW` (bedpan washer),
`WD` (washer-disinfector), `STZ` (sterilizer / autoclave),
`RO-DIA` (dialysis RO unit), `MRI` (MRI bore unit), `LIN` (linear accelerator),
`CT`, `PET`, `XRY`, `FLR` (fluoroscopy), `CTH` (cath-lab equipment),
`CRT` (crash trolley), `CRB` (crash buzzer), `NCS` (nurse-call station),
`NCP` (nurse-call posts), `AED`, `HRD` (hand-rub dispenser),
`PTS` (pneumatic-tube station), `PTBL` (pneumatic-tube blower),
`AGV-D` (AGV dock), `DUM` (dumbwaiter), `MORT-FRG` (mortuary fridge),
`POST-MTBL` (post-mortem table), `INC` (incubator), `WCT` (warming cot),
`CPB` (cardiopulmonary bypass), `PER` (perfusion stack), `RAD-PB` (lead curtain),
`MR-FNC` (MRI 5-Gauss fence), `LIG-WC`, `LIG-DR` (anti-ligature).

### 4.5 Clinical room-type enum (`ROM_HEALTH_CLASS_TXT`)

`OR-ULTRA`, `OR-CONV`, `OR-HYBRID`, `CATHLAB`, `IR`, `ICU`, `HDU`,
`NICU`, `MAT-DEL`, `MAT-LDR`, `RECOV-1`, `RECOV-2`, `WARD-INPT`,
`WARD-AMB`, `AIIR`, `PE-PROT`, `ANTERM`, `EXAM`, `TREAT`, `CONS`,
`IMG-CT`, `IMG-MRI`, `IMG-XRY`, `IMG-PET`, `IMG-LIN`, `IMG-FLR`,
`PH-CSP-797`, `PH-CSP-800`, `PH-NSP`, `LAB-WET`, `LAB-DRY`, `BSL2`,
`BSL3`, `MORT`, `POST`, `DECON-D`, `DECON-C`, `HSDU-W`, `HSDU-P`,
`DIAL`, `PSY-BED`, `PSY-OBS`, `SECL`, `BARI`, `NEONAT-ROOM`,
`MILK-KIT`, `BIRTH-POOL`, `HBO`,
`EP-LAB` (electrophysiology / cardiac ablation — high RF shielding + IPS),
`ENDOSCOPY` (GI endoscopy suite — HTM 01-06 decon flow),
`SURG-ROBOT` (robotic surgery suite — ceiling-zone + boom clearance),
`PET-HOT-LAB` (nuclear medicine dose calibration — 511 keV shielding + ventilation),
`NM-SCAN` (nuclear medicine scanner room — SPECT / gamma camera),
`BRACHYTHERAPY` (sealed-source radiotherapy — afterloading vault),
`BSL4` (biosafety level 4 — suit lab, separate ventilation),
`BONE-MARROW` (stem cell / BMT — HEPA PE, distinct from AIIR),
`STROKE-HYPER` (hyperacute stroke unit — time-critical ED↔CT adjacency),
`BIRTH-CTR` (midwife-led birth centre, distinct from `MAT-DEL`),
`CYSTO` (cystoscopy — endoscopy + fluoroscopy hybrid),
`TELEHEAL` (telehealth consultation room — camera, audio, lighting spec).

---

## 5. Shared parameter pack (Phase H-1)

> **Crosschecked against `Data/MR_PARAMETERS.txt` (2,743 lines, 27 groups,
> ~2,555 params) on 2026-05-08.** The first draft of this section
> proposed ~140 net-new parameters under fresh namespaces (`ROM_*`,
> `WAT_*`, `ELE_*`, `CEQ_*`, `MGAS_*`, etc.). After full crosscheck the
> net-new count drops to **~95** because (a) ~25 already exist under
> different names, (b) ~20 must re-prefix to match existing namespaces
> (`ELC_*`, `PLM_*`, `HVC_*`, `FLS_*`, `MNT_*`, `ASS_ROOM_*`, `PRJ_ORG_*`),
> and (c) one proposed namespace (`HEALTH_*`) clashes with the existing
> `HEALTH_METRICS` group (23 — model-health-score, not building-health).

All parameters are **`SHARED`** type, scoped per the existing
`ParamRegistry` JSON conventions. GUIDs are minted from a stable
namespace `c8d4f6e2-a9b3-4d27-8c61-0e7a3f9b2d15` (UUIDv5 over the
parameter name). Storage type is given in column 3.

### 5.0 Crosscheck — duplicates to drop, params to re-prefix

Three categories. Reuse first, rename second, mint last.

#### 5.0.1 Drop — already present under a different name

| First-draft proposal | Existing parameter | Group | Action |
|---|---|---|---|
| `ROM_PRESS_DELTA_PA_NUM` (required Δp) | `MEP_DELTA_PRESS_PA` (TEXT) | 5 (HVC) | **Drop** — reuse; storage upgrade to NUMBER tracked separately |
| `ROM_ACH_REQ_NUM` | `HVC_AIR_CHANGES_PER_HR` (TEXT) | 5 (HVC) | **Drop** — reuse |
| `ROM_TEMP_DESIGN_C_NUM` | `PER_ENVIRONMENTAL_TEMP_DESIGN_C` *and* `MEP_DESIGN_TEMP_C` *and* `SYS_TEMPERATURE_NR` | 9 / 5 / 11 | **Drop** — reuse `PER_ENVIRONMENTAL_TEMP_DESIGN_C` for design temp |
| `ROM_RH_DESIGN_PCT_NUM` | `PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT` | 9 | **Drop** — reuse |
| `ROM_NOISE_NR_NUM` | `PER_ACOUSTICS_BACKGROUND_NOISE_DB` | 9 | **Drop** for background noise; if NR-rating is required separately, mint `CLN_ROOM_NOISE_NR_NR` (NR ≠ dB(A)) |
| `ROM_PHE_REFUGE_BOOL` | `RGL_ACCESS_REFUGE_AREA_TXT` | 15 (RGL) | **Drop** — reuse (TEXT carries `Y / N / Code`) |
| `ROM_OCC_CLINICAL_INT` | `ASS_DESIGN_OCCUPANCY_INT` *and* `BLE_ROOM_OCCUPANCY_NR` *and* `FLS_SFTY_OCCUPANT_LOAD_NR` | 1 / 10 / 8 | **Drop** — reuse `ASS_DESIGN_OCCUPANCY_INT` for clinical occupancy; visitors mint as new |
| `CEQ_MAKE_TXT` | `ASS_MANUFACTURER_TXT` | 1 | **Drop** — reuse |
| `CEQ_MODEL_TXT` | `ASS_MODEL_NR_TXT` | 1 | **Drop** — reuse |
| `CEQ_PPM_FREQ_TXT` | `ASS_MAINTENANCE_FREQUENCY_MONTHS` *and* `ASS_MAINT_INTERVAL_TXT` *and* `MNT_SERVICE_INTERVAL_TXT` | 1 / 1 / 25 | **Drop** — reuse `ASS_MAINTENANCE_FREQUENCY_MONTHS` (numeric) |
| `CEQ_CALIBRATION_DT` | `MNT_LAST_SERVICE_DATE_TXT` exists; calibration date does NOT | 25 | **Partial** — calibration is distinct from service; mint `MNT_CAL_LAST_DT` + `MNT_CAL_NEXT_DT` (matches `MNT_*` convention) |
| (no proposal — serial) | `ASS_SERIAL_NR_TXT` already exists | 1 | (use as-is for clinical equipment serial) |
| (no proposal — barcode) | `ASS_BARCODE_TXT` already exists | 1 | (use as-is for asset tracking) |
| Room area / volume / height / name / number (implied by RDS template) | `ASS_ROOM_AREA_SQ_M`, `ASS_ROOM_VOLUME_CU_M`, `ASS_ROOM_HEIGHT_MM`, `ASS_ROOM_NAME_TXT`, `ASS_ROOM_NUM_TXT`, `ASS_ROOM_FUNCTION_USE_TXT` | 1 | **Reuse** — RDS template tokens bind to these |
| Room finishes (floor / ceiling / wall / base) | `BLE_ROOM_FINISH_FLOOR_TXT`, `BLE_ROOM_FINISH_CEILING_TXT`, `BLE_ROOM_FINISH_WALL_TXT`, `BLE_ROOM_FINISH_BASE_TXT` | 10 (BLE) | **Reuse** — RDS template tokens bind to these |
| Room lighting design lux (RDS token) | `LTG_DESIGN_ILLUMINANCE_LUX` *and* `ASS_DESIGN_LTG_LVL_LUX_NR` | 7 / 1 | **Reuse** — `LTG_DESIGN_ILLUMINANCE_LUX` |
| Room lighting colour temp (RDS token) | `LTG_CLR_TEMP_K` | 7 | **Reuse** |
| Door acoustic rating (RDS token) | `BLE_DOOR_ACOUSTIC_RATING_DB`, `BLE_DOOR_ACOUSTIC_STC_NR` | 10 | **Reuse** |
| Floor impact sound (RDS token) | `BLE_FLR_IMPACT_SOUND_INS_DB` | 10 | **Reuse** |
| `MGAS_PIPE_MATERIAL_TXT` | `HVC_PIPE_*` material covered via `SYS_INSULATION_TYPE_TXT` and material params on the pipe type | 5 / 11 | **Drop** — material rides on the pipe type; `MGAS_GAS_TYPE_TXT` discriminates the system |
| MGAS test pressure | `ASS_TEST_PRESSURE_BAR` already exists | 1 | **Reuse** — drop dedicated MGAS test-pressure param |
| MGAS terminal-unit ↔ ZVB linkage (`MGAS_ZVB_REF_TXT`) | `ASS_TIEIN_REF_TXT` + `ASS_TIEIN_CONNECTED_BOOL` + `ASS_TIEIN_FLOW_DIR_TXT` already exist as a generic tie-in framework (10 params) | 1 | **Reuse** the tie-in framework for TU↔ZVB linkage; keep `MGAS_ZVB_REF_TXT` only as a denormalised quick-lookup, optional |
| Cost / lead-time for medical equipment | `CST_DELIVERY_LEAD_TIME_DAYS`, `CST_SUP_PROCUREMENT_LEAD_TIME_DAYS`, `WARN_ASS_LEAD_TIME_SPECIALTY_EQUIP`, `WARN_GEN_SPEC_EQUIP_LEAD_TIME` | 2 / 18 | **Reuse** — clinical-equipment lead-time rides existing CST + WARN slots |
| `ELC_EMERG_COVERED_BOOL` (existing binary "is this on emergency power?") | exists, but cannot represent LS / CR / EQ / N | 4 | **Keep both** — `ELC_EMERG_COVERED_BOOL` stays as the legacy binary; new `ELC_EES_BRANCH_TXT` carries the four-value branch |
| `LTG_EMRG_TYPE_TXT` *and* `LTG_EMERGENCY_TXT` | exist for emergency lighting type | 7 | **Reuse** — emergency lighting type isn't an EES branch |
| Fire-rating of construction (carrier of the room boundary) | `PER_FIRE_RATING_HR`, `PER_FIRE_RATING_MINS`, `PROP_FIRE_RATING`, `RGL_FIRE_RATING_TXT` | 14 / 9 / 15 | **Reuse** — none of these is a *compartment ID*; the new `FLS_COMPARTMENT_ID_TXT` is additive |
| Fire compartment occupant load | `BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR`, `FLS_SFTY_OCCUPANT_LOAD_NR`, `FLS_EVACUATION_TIME_MIN`, `FLS_EXIT_TRAVEL_DIST_M` | 10 / 8 | **Reuse** — PHE refuge + evacuation time are already there |

#### 5.0.2 Re-prefix — keep concept, match existing namespace

| First-draft name | Adopted name | Reason |
|---|---|---|
| `ROM_HEPA_REQ_BOOL`, `ROM_HEPA_GRADE_TXT`, `ROM_HEPA_LAST_TST_DT` | `HVC_HEPA_REQ_BOOL`, `HVC_HEPA_GRADE_TXT`, `HVC_HEPA_LAST_TST_DT` | HEPA is HVAC — matches `HVC_*` group 5 |
| `WAT_TMV_TYPE_TXT`, `WAT_DEAD_LEG_M_NUM`, `WAT_AUG_CARE_BOOL`, `WAT_POU_FILTER_BOOL`, `WAT_SENTINEL_BOOL`, `WAT_FLUSH_LOG_REF_TXT`, `WAT_RO_LOOP_BOOL` | `PLM_TMV_TYPE_TXT`, `PLM_DEAD_LEG_M_NUM`, `PLM_AUG_CARE_BOOL`, `PLM_POU_FILTER_BOOL`, `PLM_SENTINEL_BOOL`, `PLM_FLUSH_LOG_REF_TXT`, `PLM_RO_LOOP_BOOL` | Water safety lives under `PLM_*` group 6 |
| `ELE_BRANCH_TXT`, `ELE_ATS_TIME_S_NUM`, `ELE_IPS_BOOL`, `ELE_IT_CARDIAC_BOOL`, `ELE_RECEPT_TYPE_TXT`, `ELE_GEN_RUN_HRS_TGT_NUM`, `ELE_LSC_TIER_TXT` | `ELC_EES_BRANCH_TXT`, `ELC_ATS_TIME_S_NUM`, `ELC_IPS_BOOL`, `ELC_IT_CARDIAC_BOOL`, `ELC_RECEPT_TYPE_TXT`, `ELC_GEN_RUN_HRS_TGT_NUM`, `ELC_LSC_TIER_TXT` | All electrical params use `ELC_*` group 4 |
| `ROM_FIRE_COMP_TXT` | `FLS_COMPARTMENT_ID_TXT` | Fire-compartment membership is FLS — group 8 |
| `ROM_HEALTH_CLASS_TXT`, `ROM_HBN_REF_TXT`, `ROM_FGI_REF_TXT`, `ROM_HTM_REF_TXT`, `ROM_ADB_CODE_TXT`, `ROM_PRESS_REGIME_TXT`, `ROM_PRESS_DELTA_ACT_PA_NUM`, `ROM_INFECT_CLASS_TXT`, `ROM_ANTERM_LINKED_ID_TXT`, `ROM_RAD_CONTROLLED_BOOL`, `ROM_RAD_DESIGN_GOAL_MGY_NUM`, `ROM_RAD_LEAD_REQ_MM_NUM`, `ROM_RAD_LEAD_ACT_MM_NUM`, `ROM_MRI_ZONE_INT`, `ROM_RF_SHIELD_BOOL`, `ROM_LIGATURE_RES_BOOL`, `ROM_LIG_RISK_LVL_TXT`, `ROM_BARI_DESIGN_KG_NUM`, `ROM_HOIST_TRACK_BOOL`, `ROM_NURSECALL_TYPE_TXT`, `ROM_PRIVACY_LVL_TXT` | All re-prefixed `CLN_*` (Clinical — new group 28) | New three-letter namespace `CLN_*` matches existing `HVC_/PLM_/ELC_/LTG_/FLS_/MAT_/MNT_` convention. Group 23 `HEALTH_METRICS` is reserved for model-health and must not be reused |
| `MGAS_*` (13 params) | `MGS_*` (13 params) — `MGS_GAS_TYPE_TXT`, `MGS_NOM_PRESS_KPA_NUM`, `MGS_DESIGN_FLOW_LPM_NUM`, `MGS_DIVERSITY_PCT_NUM`, `MGS_TU_BS5682_BOOL`, `MGS_TU_INDEXED_BOOL`, `MGS_ZVB_REF_TXT`, `MGS_AAP_REF_TXT`, `MGS_PIPE_BRAZED_BOOL`, `MGS_VERIFY_DT`, `MGS_VERIFY_BY_TXT`, `MGS_VERIFY_PASS_BOOL` (12 — material dropped, test-pressure dropped) | Three-letter prefix matches existing `HVC_/PLM_/ELC_` convention |
| `RAD_*` (8 params) | `RAD_*` — kept as-is | 3-letter, no clash |
| `LIG_*` (5 params) | `LIG_*` — kept as-is | 3-letter, no clash |
| `CEQ_*` (5 surviving params: GMDN / UMDNS / SFG20 ref / clinical flag / Spaulding tier / decon method) | `CEQ_*` — kept as-is | 3-letter, no clash |
| `PRJ_HEALTH_*` (12 project-info params) | `PRJ_ORG_HEALTH_*` (12 params) | Matches Phase 112 `PRJ_ORG_*` convention. Lands inside group 13 `PRJ_INFORMATION` |

#### 5.0.3 Existing infrastructure to plug into rather than duplicate

| Existing system | What to plug in |
|---|---|
| `ASS_TIEIN_*` (10 params, generic tie-in framework) | MGS terminal-unit ↔ zone-valve-box and ↔ alarm-panel linkage rides on the existing tie-in framework (`ASS_TIEIN_REF_TXT`, `ASS_TIEIN_CONNECTED_BOOL`, `ASS_TIEIN_FLOW_DIR_TXT`, `ASS_TIEIN_PHASE_TXT`, `ASS_TIEIN_BY_TXT`, `ASS_TIEIN_STATUS_TXT`, `ASS_TIEIN_IFC_REF_TXT`, `ASS_TIEIN_TAG_1_TXT`). Healthcare adds only `MGS_GAS_TYPE_TXT` to discriminate gas |
| `MNT_*` (5 params for last/next service / interval / provider / warranty) | All clinical-equipment PPM rides on this; calibration adds `MNT_CAL_LAST_DT` + `MNT_CAL_NEXT_DT` only |
| `ASS_WARRANTY_*` (5 params, parts/labor/duration) | All clinical-equipment warranties ride on this |
| `WARN_BLE_MEDICAL_CLEARANCE_MEDICAL_EQUIP`, `WARN_BLE_MEDICAL_POWER_MEDICAL_EQUIP`, `WARN_RGL_STD_MEDICAL_EQUIP`, `WARN_ASS_LEAD_TIME_SPECIALTY_EQUIP`, `WARN_GEN_SPEC_EQUIP_LEAD_TIME` (5 existing healthcare-flavoured `WARN_*` thresholds) | Healthcare validators publish back into these existing warning channels via `WarningsManager` so the existing warnings dashboard surfaces healthcare drift without UI changes |
| `ELC_LPS_CERT_REF_TXT`, `ELC_FAT_CERT_REF_TXT` (existing certificate-ref pattern) | MGPS verification cert rides this pattern; new `MGS_VERIFY_CERT_REF_TXT` mirrors `ELC_FAT_CERT_REF_TXT` shape |
| `ELC_PANEL_SCHEDULE_REF_TXT`, `ELC_CIRCUIT_REF_TXT` | EES branch is a *classification* on the existing circuit; `ELC_EES_BRANCH_TXT` rides alongside the existing circuit refs, no parallel framework |
| `RGL_ACCESS_REFUGE_AREA_TXT` | Reuse for PHE refuge designation |
| Group 23 `HEALTH_METRICS` (model-health-score) | **Do not extend** — strict naming separation. Group 28 `CLN_CLINICAL` (new) is the home of clinical-attribute params |
| Group 25 `COMMISSIONING` | Healthcare commissioning workflows persist run records here, reusing the existing schema |
| `CST_DELIVERY_LEAD_TIME_DAYS`, `CST_SUP_PROCUREMENT_LEAD_TIME_DAYS` | Long-lead clinical-equipment dates ride these |

#### 5.0.4 Group additions to `PARAMETER_REGISTRY.json`

| New group id | Code | Purpose |
|---|---|---|
| 28 | `CLN_CLINICAL` | Room-bound clinical attributes (room class, infection class, MRI zone, RF shielding, ligature, hoist, bariatric, nurse-call type, privacy, anteroom link, etc.) |
| 29 | `MGS_SYSTEMS` | Medical gas pipeline systems (parallel to `HVC_SYSTEMS` / `PLM_DRN`) |
| 30 | `RAD_PROTECTION` | Radiation shielding (NCRP 147 / 151 driven, X-ray / CT / MRI / LINAC) |
| 31 | `CEQ_CLINICAL` | Clinical equipment classification (GMDN / UMDNS / SFG20-Healthcare / Spaulding) |
| 32 | `LIG_BEHAVIOURAL` | Anti-ligature / behavioural-health attributes |

`HVC_HEPA_*`, `PLM_TMV_*`, `PLM_DEAD_LEG_*`, `PLM_AUG_CARE_*`, `PLM_RO_*`,
`ELC_EES_*`, `ELC_IPS_*`, `ELC_IT_CARDIAC_*`, `FLS_COMPARTMENT_*`, and
`MNT_CAL_*` extensions land in their existing groups (5, 6, 4, 8, 25)
and need no new group.

---

### 5.1 Clinical room-bound (group 28 `CLN_CLINICAL`, binds to Rooms)

| Parameter | Type | Purpose |
|---|---|---|
| `CLN_ROOM_CLASS_TXT` | TEXT | Clinical room class enum (§4.5) |
| `CLN_HBN_REF_TXT` | TEXT | HBN reference (e.g. `HBN 04-01 R-1234`) |
| `CLN_FGI_REF_TXT` | TEXT | FGI section reference |
| `CLN_HTM_REF_TXT` | TEXT | HTM reference |
| `CLN_ADB_CODE_TXT` | TEXT | NHS ADB room code |
| `CLN_PRESS_REGIME_TXT` | TEXT | `NEG / POS / NEUTRAL` (Δp itself rides on existing `MEP_DELTA_PRESS_PA`; design Δp adds `CLN_PRESS_DELTA_DESIGN_PA_NR` because the existing param is field-measured) |
| `CLN_PRESS_DELTA_DESIGN_PA_NR` | NUMBER | Required design Δp (Pa) — distinct from field-tested `MEP_DELTA_PRESS_PA` |
| `CLN_INFECT_CLASS_TXT` | TEXT | `STD / AIIR / PE / CLASS-N / CLASS-P` |
| `CLN_ANTERM_LINKED_ID_TXT` | TEXT | ElementId of paired anteroom |
| `CLN_OCC_VISITOR_INT` | INTEGER | Visitor headcount (clinical occupancy reuses `ASS_DESIGN_OCCUPANCY_INT`) |
| `CLN_RAD_CONTROLLED_BOOL` | YESNO | IRR17 controlled area? |
| `CLN_MRI_ZONE_INT` | INTEGER | 1..4 |
| `CLN_RF_SHIELD_BOOL` | YESNO | RF Faraday cage present |
| `CLN_LIGATURE_RES_BOOL` | YESNO | Anti-ligature design applies |
| `CLN_LIG_RISK_LVL_TXT` | TEXT | `LOW / MED / HIGH / VERY-HIGH` |
| `CLN_BARI_DESIGN_KG_NR` | NUMBER | Bariatric SWL (kg) |
| `CLN_HOIST_TRACK_BOOL` | YESNO | Ceiling-hoist track installed |
| `CLN_NURSECALL_TYPE_TXT` | TEXT | `STD / EMG / ASSAULT / CARDIAC` |
| `CLN_PRIVACY_LVL_TXT` | TEXT | `OPEN / SEMI / SINGLE / SECURE` |
| `CLN_NOISE_NR_NR` | INTEGER | NR rating (mint only if NR ≠ existing `PER_ACOUSTICS_BACKGROUND_NOISE_DB` semantics; otherwise drop) |
| `CLN_ROOM_ACOUSTIC_CLASS_TXT` | TEXT | HTM 08-01 sensitivity tier: `SENSITIVE / MEDIUM / NOT-SENSITIVE` |
| `CLN_STC_RATING_NR` | NUMBER | Required Sound Transmission Class of the room boundary assembly |
| `CLN_SOUND_MASKING_BOOL` | YESNO | White-noise / speech-masking system installed |
| `CLN_SELF_CLOSE_DOOR_BOOL` | YESNO | All doors self-closing (pressure-regime prerequisite) |
| `CLN_DOOR_SWEEP_BOOL` | YESNO | Door-bottom sweep seal fitted |
| `CLN_FLOOR_INTEGRAL_COVE_BOOL` | YESNO | Integral floor/wall coving (infection control) |
| `CLN_HANDWASH_COUNT_INT` | INTEGER | Number of clinical handwash basins in room (HBN minimum) |
| `CLN_ABR_DISPENSER_COUNT_INT` | INTEGER | Number of alcohol-based rub dispensers |
| `CLN_PASS_THROUGH_BOOL` | YESNO | Pass-through hatch (clean/soiled) fitted |
| `CLN_WASTE_CLASS_TXT` | TEXT | Dominant waste stream: `CLINICAL/ANATOMICAL/PHARMACEUTICAL/CYTOTOXIC/SHARPS/RADIOACTIVE/DOMESTIC` |
| `CLN_WASTE_ROUTE_TXT` | TEXT | Waste collection point / chute reference |
| `CLN_FHIR_LOCATION_ID_TXT` | TEXT | HL7 FHIR Location resource ID (links BIM room to EHR patient location) |
| `CLN_RTLS_ZONE_ID_TXT` | TEXT | RTLS zone reference for this room (position engine training) |
| `CLN_IHFG_ROOM_CODE_TXT` | TEXT | iHFG (TAHPI) room code for international projects |
| `CLN_IHFG_REF_TXT` | TEXT | iHFG section reference (parallel to `CLN_HBN_REF_TXT`) |
| `CLN_TELEHEAL_BOOL` | YESNO | Room is telehealth-capable (camera, audio, lux spec apply) |
| `CLN_BMS_PRESS_OBJ_TXT` | TEXT | BACnet/IP object ref — differential pressure sensor |
| `CLN_BMS_ACH_OBJ_TXT` | TEXT | BACnet/IP object ref — supply airflow (converted to ACH in code) |
| `CLN_BMS_TEMP_OBJ_TXT` | TEXT | BACnet/IP object ref — room temperature sensor |
| `CLN_BMS_RH_OBJ_TXT` | TEXT | BACnet/IP object ref — room relative humidity sensor |
| `CLN_BMS_CO2_OBJ_TXT` | TEXT | BACnet/IP object ref — CO₂ sensor (occupancy proxy) |
| `CLN_BMS_FAN_STATUS_OBJ_TXT` | TEXT | BACnet/IP binary object ref — supply fan run status (prevents false pressure-validator positives when unit is offline for maintenance) |
| `CLN_BMS_LAST_SEEN_DT` | TEXT (ISO datetime) | Timestamp of last successful BMS poll (staleness guard — >30 min triggers sensor-fault warning) |

(44 params in CLN — grown from original 19 to accommodate acoustic, infection-control detail, IoT sensor refs, FHIR, RTLS, iHFG, telehealth, and waste fields.)

### 5.2 Medical gas (group 29 `MGS_SYSTEMS`, binds to Pipes / Mech Equipment / Plumbing Fixtures)

| Parameter | Type | Purpose |
|---|---|---|
| `MGS_GAS_TYPE_TXT` | TEXT | `O2 / MA4 / MA7 / N2O / N2 / CO2 / HE / VAC / AGS / DENT` |
| `MGS_NOM_PRESS_KPA_NR` | NUMBER | Nominal design pressure (kPa) — distinct from field-tested `HVC_PIPE_PRESSURE_KPA` and `ASS_TEST_PRESSURE_BAR` |
| `MGS_DESIGN_FLOW_LPM_NR` | NUMBER | Design flow (l/min, free air) — l/min is the medical-gas convention; existing `HVC_PIPE_FLOWRATE_LPS` is wrong unit |
| `MGS_DIVERSITY_PCT_NR` | NUMBER | Diversity factor (%) |
| `MGS_TU_BS5682_BOOL` | YESNO | Terminal unit complies with BS 5682 |
| `MGS_TU_INDEXED_BOOL` | YESNO | Has gas-specific NIST indexing |
| `MGS_ZVB_REF_TXT` | TEXT | Owning zone valve box ID (denormalised; canonical link rides `ASS_TIEIN_REF_TXT`) |
| `MGS_AAP_REF_TXT` | TEXT | Owning area alarm panel ID |
| `MGS_PIPE_BRAZED_BOOL` | YESNO | Brazed under inert-gas purge |
| `MGS_VERIFY_DT` | TEXT (ISO date) | NFPA 99 §5.1.12 verification date |
| `MGS_VERIFY_BY_TXT` | TEXT | ASSE 6030 verifier name |
| `MGS_VERIFY_PASS_BOOL` | YESNO | Latest verification passed |
| `MGS_VERIFY_CERT_REF_TXT` | TEXT | Verification certificate reference (mirrors `ELC_FAT_CERT_REF_TXT`) |

(13 params, all new. Pipe material drops — rides existing pipe-type material; test pressure drops — reuses `ASS_TEST_PRESSURE_BAR`.)

### 5.3 Essential electrical (extends group 4 `ELC_PWR`)

| Parameter | Type | Purpose |
|---|---|---|
| `ELC_EES_BRANCH_TXT` | TEXT | `LIFE-SAF / CRIT / EQP-BR / NORMAL` (existing `ELC_EMERG_COVERED_BOOL` retained as legacy binary) |
| `ELC_ATS_TIME_S_NR` | NUMBER | Required ATS transfer time (s) — NFPA 99 ≤ 10 s for LS / CR |
| `ELC_IPS_BOOL` | YESNO | Powered from isolated power system (NEC 517) |
| `ELC_IT_CARDIAC_BOOL` | YESNO | Cardiac-protected outlet |
| `ELC_RECEPT_TYPE_TXT` | TEXT | `HOSP-GR / TR / ISO / EMG-RED` |
| `ELC_GEN_RUN_HRS_TGT_NR` | NUMBER | Required runtime (h) at full load |
| `ELC_LSC_TIER_TXT` | TEXT | NEC 517 / NFPA 99 Tier — `1 / 2 / 3` |

(7 params — all new — sit alongside existing `ELC_EMERG_COVERED_BOOL`, `ELC_PANEL_SCHEDULE_REF_TXT`, `ELC_CIRCUIT_REF_TXT`.)

### 5.4 Water safety (extends group 6 `PLM_DRN`)

| Parameter | Type | Purpose |
|---|---|---|
| `PLM_TMV_TYPE_TXT` | TEXT | `TMV2 / TMV3 / NONE` |
| `PLM_DEAD_LEG_M_NR` | NUMBER | Distance from main to outlet (m) |
| `PLM_AUG_CARE_BOOL` | YESNO | Augmented-care wing rules apply (HTM 04-01 Pt C) |
| `PLM_POU_FILTER_BOOL` | YESNO | Point-of-use filter fitted |
| `PLM_SENTINEL_BOOL` | YESNO | Sentinel temperature point |
| `PLM_FLUSH_LOG_REF_TXT` | TEXT | Flushing-log document ref |
| `PLM_RO_LOOP_BOOL` | YESNO | Belongs to dialysis RO loop |

(7 params — hot/cold-water temps reuse existing `PLM_HOTWTR_TEMP_C`.)

### 5.5 HEPA / cleanroom ventilation (extends group 5 `HVC_SYSTEMS`)

| Parameter | Type | Purpose |
|---|---|---|
| `HVC_HEPA_REQ_BOOL` | YESNO | HEPA terminal required |
| `HVC_HEPA_GRADE_TXT` | TEXT | `H13 / H14 / U15 / U16` (BS EN 1822) |
| `HVC_HEPA_LAST_TST_DT` | TEXT (ISO date) | Last DOP / PAO test |
| `HVC_ACH_OUTSIDE_NR` | INTEGER | Outside-air component (total ACH reuses existing `HVC_AIR_CHANGES_PER_HR`) |

(4 params — ACH reuses existing.)

### 5.6 Radiation (group 30 `RAD_PROTECTION`, binds to Walls / Doors / Windows / GenericModel)

| Parameter | Type | Purpose |
|---|---|---|
| `RAD_LEAD_MM_NR` | NUMBER | Pb-equivalent thickness in this element |
| `RAD_BARRIER_TYPE_TXT` | TEXT | `PRIMARY / SECONDARY / SCATTER / LEAKAGE` |
| `RAD_WORKLOAD_MAWK_NR` | NUMBER | Workload (mA·min/wk) |
| `RAD_USE_FACTOR_NR` | NUMBER | NCRP 147 U |
| `RAD_OCC_FACTOR_NR` | NUMBER | NCRP 147 T |
| `RAD_DOSE_DESIGN_GOAL_TXT` | TEXT | `CONTROLLED / UNCONTROLLED` |
| `RAD_QE_NAME_TXT` | TEXT | Qualified Expert (medical / health physicist) |
| `RAD_QE_DT` | TEXT (ISO date) | Sign-off date |

(8 params, all new.)

### 5.7 Clinical equipment (group 31 `CEQ_CLINICAL`, binds to Mechanical Equipment / Specialty Equipment / GenericModel)

| Parameter | Type | Purpose |
|---|---|---|
| `CEQ_CATEGORY_TXT` | TEXT | `PENDANT / BEDHEAD / IMAGING / PHARMACY / DIAL / MORT / MOBIL` |
| `CEQ_GMDN_CODE_TXT` | TEXT | Global Medical Device Nomenclature code |
| `CEQ_UMDNS_CODE_TXT` | TEXT | ECRI UMDNS code |
| `CEQ_SFG20_REF_TXT` | TEXT | SFG20 healthcare schedule id |
| `CEQ_CLINICAL_BOOL` | YESNO | Counts towards clinical asset register |
| `CEQ_INFECT_TIER_TXT` | TEXT | Spaulding `CRITICAL / SEMI / NON-CRITICAL` |
| `CEQ_DECON_METHOD_TXT` | TEXT | Decon route |

(7 params — make/model/serial/PPM/warranty/cost all reuse `ASS_*` and `MNT_*`.)

### 5.12 Structural loading for heavy medical equipment (extends group 28 `CLN_CLINICAL`, binds to Rooms)

Heavy imaging equipment (MRI up to 15 t, CT gantry, LINAC) drives structural requirements distinct from standard live-load tables. These parameters are separate from shielding (§5.6) — a room can satisfy NCRP 147 barriers while failing on floor deflection.

| Parameter | Type | Purpose |
|---|---|---|
| `CLN_FLOOR_LOAD_KN_M2_NR` | NUMBER | Design floor live load (kN/m²) for this room |
| `CLN_FLOOR_LOAD_POINT_KN_NR` | NUMBER | Concentrated point load from heaviest equipment item (kN) |
| `CLN_VIB_VM_NR` | NUMBER | Vibration velocity criterion (µm/s) — imaging-suite tolerance (MRI typically VC-D/E) |
| `CLN_VIB_ISOLATION_BOOL` | YESNO | Vibration isolation pads / inertia block specified |
| `CLN_SLAB_FLATNESS_MM_NR` | NUMBER | Maximum allowed floor flatness deviation (mm) — MRI typically ±2–3 mm |
| `CLN_STRUCT_SIGN_OFF_TXT` | TEXT | Structural engineer name / practice |
| `CLN_STRUCT_SIGN_OFF_DT` | TEXT (ISO date) | Date of structural engineer sign-off |

(7 params, all new. Suggest **H-23** phase.)

### 5.13 Acoustic compliance (extends group 28 `CLN_CLINICAL` — acoustic sub-fields, binds to Rooms / Walls)

HTM 08-01 specifies NR targets by room type; FGI/HIPAA mandates speech privacy. `CLN_ROOM_ACOUSTIC_CLASS_TXT`, `CLN_NOISE_NR_NR`, `CLN_STC_RATING_NR`, and `CLN_SOUND_MASKING_BOOL` are already in §5.1. The following additional params bind to **Wall / Floor / Ceiling** elements:

| Parameter | Type | Purpose |
|---|---|---|
| `CLN_RT60_TARGET_S_NR` | NUMBER | Target reverberation time RT60 (s) at 500 Hz mid-band |
| `HVC_NC_CURVE_INT` | INTEGER | Noise Criteria curve target for HVAC background noise (NC-25 to NC-45) — extends group 5 |

(2 additional params. `HVC_NC_CURVE_INT` lands in group 5 `HVC_SYSTEMS`; RT60 in CLN. Suggest **H-24** phase.)

### 5.14 IoT device and network refs (new group 33 `ICT_HEALTHIOT`, binds to GenericModel / Mechanical Equipment / Specialty Equipment)

Parameters for IoT anchor devices that are modelled as BIM families (RTLS beacons, nurse call nodes, energy meters, radiation monitors, TMV sensors, endoscope RFID readers, infant security readers).

| Parameter | Type | Purpose |
|---|---|---|
| `ICT_RTLS_ANCHOR_ID_TXT` | TEXT | RTLS beacon / anchor device ID |
| `ICT_RTLS_TECH_TXT` | TEXT | `BLE / UWB / Wi-Fi / IR` |
| `ICT_RTLS_TX_POWER_DBM_NR` | NUMBER | Transmit power (dBm) for coverage radius calculation |
| `ICT_RTLS_COVERAGE_M_NR` | NUMBER | Nominal coverage radius (m) at design sensitivity |
| `ICT_NC_NODE_REF_TXT` | TEXT | Nurse call controller node address (IP-SIP, 900 MHz, or analogue pair) |
| `ICT_NC_PROTOCOL_TXT` | TEXT | `IP-SIP / ANALOGUE / HYBRID` |
| `ICT_NC_RTLS_DISPATCH_BOOL` | YESNO | Nurse call integrates with RTLS for auto-staff-dispatch |
| `ICT_DOSIMETRY_SYSTEM_TXT` | TEXT | Radiation personnel dosimetry contractor / platform (TLD, electronic) |
| `ICT_INFANT_RFID_READER_ID_TXT` | TEXT | Infant anti-abduction RFID reader ID on door / window family |
| `ICT_CAM_MOUNT_TXT` | TEXT | Telehealth camera mount type: `PTZ-CEILING / PTZ-WALL / FIXED` |
| `ICT_TELEHEAL_BW_MBPS_NR` | NUMBER | Required uplink bandwidth (Mbps) for telehealth |
| `ICT_TELEHEAL_LUX_NR` | NUMBER | Required face illuminance (lux) — minimum 500 lux at seat height |
| `ICT_TELEHEAL_PLATFORM_TXT` | TEXT | `ZOOM / WEBEX / EPIC / CERNER / PROPRIETARY` |
| `ELC_SUBMETER_ID_TXT` | TEXT | Sub-meter device ID on electrical panel (Revit panel mark → meter ID) — extends group 4 |
| `ELC_METER_PROTOCOL_TXT` | TEXT | `MODBUS-TCP / BACNET / DLMS` |
| `ELC_GEN_TEST_LOG_REF_TXT` | TEXT | Reference to NFPA 110 weekly generator test log — extends group 4 |
| `ELC_UPS_REPLACE_DT` | TEXT (ISO date) | UPS battery replacement due date — extends group 4 |
| `ELC_ATS_TEST_LOG_REF_TXT` | TEXT | NFPA 99 §6.4.2 monthly ATS transfer test log ref — extends group 4 |
| `MGS_ALARM_PROTOCOL_TXT` | TEXT | MGPS alarm panel protocol: `BACNET / MODBUS-RTU / RS485-PROPR / RS232` — extends group 29 |
| `MGS_ALARM_GATEWAY_IP_TXT` | TEXT | Serial-to-Ethernet gateway IP address (if RS-485 / RS-232 panel) — extends group 29 |
| `MGS_VERIFY_ASSE_CERT_TXT` | TEXT | ASSE 6030 / 6040 verifier certification number — extends group 29 |
| `RAD_MONITOR_DEVICE_ID_TXT` | TEXT | Area radiation monitor device ID — extends group 30 |
| `RAD_MONITOR_PROTOCOL_TXT` | TEXT | `BACNET / HTTP-API / PROPRIETARY` — extends group 30 |
| `RAD_DOSE_RATE_USV_HR_NR` | NUMBER | Live area dose rate read-back (µSv/hr, read-only from IoT) — extends group 30 |
| `RAD_DOSE_ALERT_USV_HR_NR` | NUMBER | Dose rate alert threshold (default 500 µSv/hr) — extends group 30 |
| `CLN_MRI_QUENCH_OPC_TXT` | TEXT | OPC-UA node ID for quench-in-progress status (life-safety) — extends group 28 |
| `CLN_MRI_CRYO_LEVEL_OPC_TXT` | TEXT | OPC-UA node ID for cryogen fill level (%) — extends group 28 |
| `PLM_TMV_SENSOR_ID_TXT` | TEXT | Smart TMV sensor device ID — extends group 6 |
| `PLM_TMV_PROTOCOL_TXT` | TEXT | `BACNET / MODBUS / PROPRIETARY` — extends group 6 |
| `PLM_TMV_TEMP_ACTUAL_C_NR` | NUMBER | Live TMV outlet temperature read-back (°C, read-only) — extends group 6 |
| `PLM_TMV_ALERT_LOW_C_NR` | NUMBER | Low-temp alert threshold (default 40 °C — Legionella risk) — extends group 6 |
| `PLM_TMV_ALERT_HIGH_C_NR` | NUMBER | High-temp alert threshold (default 45 °C — scald risk) — extends group 6 |
| `CEQ_ENDO_SCOPE_ID_TXT` | TEXT | Endoscope asset ID (HTM 01-06 traceability) — extends group 31 |
| `CEQ_ENDO_AER_REF_TXT` | TEXT | Linked washer-disinfector (AER) device ID — extends group 31 |
| `CEQ_ENDO_READER_ID_TXT` | TEXT | RFID reader ID at transfer point (soak/wash/dry/store) — extends group 31 |
| `CEQ_ENDO_CYCLE_COUNT_INT` | INTEGER | Reprocessing cycle count (lifetime) — extends group 31 |
| `CEQ_ENDO_LAST_REPROCESS_DT` | TEXT (ISO date) | Last reprocessing date — extends group 31 |
| `HVC_HEPA_DP_SENSOR_REF_TXT` | TEXT | BMS object ref for HEPA filter differential pressure — maintenance trigger — extends group 5 |
| `HVC_ISO_CLASS_INT` | INTEGER | ISO 14644-1 cleanroom class (1–8) — USP 797/800 monitoring — extends group 5 |
| `CLN_ENV_MONITOR_INTERVAL_MIN_NR` | NUMBER | Environmental monitoring sample interval (min) — USP 797/800 — extends group 28 |
| `CLN_ENV_CERT_DUE_DT` | TEXT (ISO date) | Cleanroom next recertification due date (USP 6-month cycle) — extends group 28 |

(41 params — most extend existing groups 4/5/6/28/29/30/31; new group 33 `ICT_HEALTHIOT` carries the RTLS anchor, nurse call, and telehealth device refs.)

Calibration extensions in group 25 `COMMISSIONING`:

| Parameter | Type | Purpose |
|---|---|---|
| `MNT_CAL_LAST_DT` | TEXT (ISO date) | Last calibration date |
| `MNT_CAL_NEXT_DT` | TEXT (ISO date) | Next calibration due |

(2 params, additive to existing `MNT_*` set.)

### 5.8 Anti-ligature / behavioural (group 32 `LIG_BEHAVIOURAL`)

| Parameter | Type | Purpose |
|---|---|---|
| `LIG_PRODUCT_RATING_TXT` | TEXT | `BS 9263 LR1..LR4` or US grade |
| `LIG_SELF_CLOSE_BOOL` | YESNO | Self-closing fitting |
| `LIG_PRESSURE_REL_BOOL` | YESNO | Releases under load |
| `LIG_FORCE_LIMIT_KG_NR` | NUMBER | Release threshold |
| `LIG_AREA_OBS_LOS_TXT` | TEXT | Observation line-of-sight code |

(5 params, all new.)

### 5.9 Fire compartment (extends group 8 `FLS_LIFE_SFTY`)

| Parameter | Type | Purpose |
|---|---|---|
| `FLS_COMPARTMENT_ID_TXT` | TEXT | Fire-compartment membership (`PER_FIRE_RATING_*` continues to rate the construction itself) |

(1 param — PHE refuge designation reuses `RGL_ACCESS_REFUGE_AREA_TXT`; evacuation time + travel distance + escape capacity all reuse existing `FLS_*` / `BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR`.)

### 5.10 Healthcare project info (extends group 13 `PRJ_INFORMATION`, prefix `PRJ_ORG_HEALTH_*`)

| Parameter | Type | Purpose |
|---|---|---|
| `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` | TEXT | `Acute / Mental / Community / Rehab / Day / FM` |
| `PRJ_ORG_HEALTH_BEDS_INT` | INTEGER | Bed count |
| `PRJ_ORG_HEALTH_OR_INT` | INTEGER | OR count |
| `PRJ_ORG_HEALTH_ICU_INT` | INTEGER | ICU bed count |
| `PRJ_ORG_HEALTH_CODE_BASE_TXT` | TEXT | `HBN/HTM`, `FGI`, `iHFG`, `Other` |
| `PRJ_ORG_HEALTH_AREA_M2_NR` | NUMBER | GIA |
| `PRJ_ORG_HEALTH_AHJ_TXT` | TEXT | Authority Having Jurisdiction |
| `PRJ_ORG_HEALTH_QE_TXT` | TEXT | Qualified Expert (radiation) |
| `PRJ_ORG_HEALTH_AE_VENT_TXT` | TEXT | Authorising Engineer — Ventilation |
| `PRJ_ORG_HEALTH_AE_MGAS_TXT` | TEXT | AE — Medical Gas |
| `PRJ_ORG_HEALTH_AE_WATER_TXT` | TEXT | AE — Water Safety |
| `PRJ_ORG_HEALTH_AE_ELEC_TXT` | TEXT | AE — Electrical |
| `PRJ_ORG_HEALTH_HTM_REGION_TXT` | TEXT | Regional HTM variant: `ENGLAND / SCOTLAND / WALES / NORTHERN-IRELAND / OTHER` — controls whether SHTM or WHTM rule variants apply (e.g. SHTM 01-06 adds TOE probe decon step) |
| `PRJ_ORG_HEALTH_IHFG_BOOL` | YESNO | Project uses iHFG (TAHPI) as its primary design guideline (Africa / ME / Australasia) — enables iHFG room-code fields and FGI cross-references |
| `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` | TEXT | Healthcare pack profile: `FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY` — gates which validators and drawing types are activated; prevents community-clinic projects from requiring LINAC vault calculators |

(15 params — uses `PRJ_ORG_*` Phase 112 convention. Three new params added for regional standard variants, iHFG, and pack scoping.)

### 5.11 Net-new parameter count

| Sub-pack | New params | Reuse existing |
|---|---|---|
| 5.1 CLN_CLINICAL (room-bound clinical — expanded) | **44** | `ASS_ROOM_*`, `BLE_ROOM_FINISH_*`, `ASS_DESIGN_OCCUPANCY_INT`, `BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR`, `RGL_ACCESS_REFUGE_AREA_TXT`, `MEP_DELTA_PRESS_PA`, `PER_ACOUSTICS_*`, `PER_ENVIRONMENTAL_TEMP_DESIGN_C`, `PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT`, `LTG_DESIGN_ILLUMINANCE_LUX`, `LTG_CLR_TEMP_K`, `BLE_DOOR_ACOUSTIC_*` |
| 5.2 MGS_SYSTEMS | **13** | `ASS_TIEIN_*`, `ASS_TEST_PRESSURE_BAR`, `HVC_PIPE_PRESSURE_KPA` |
| 5.3 ELC_PWR (EES extensions) | **7** | `ELC_EMERG_COVERED_BOOL`, `ELC_PANEL_SCHEDULE_REF_TXT`, `ELC_CIRCUIT_REF_TXT`, `ELC_FAT_CERT_REF_TXT` |
| 5.4 PLM_DRN (water-safety extensions) | **7** | `PLM_HOTWTR_TEMP_C` |
| 5.5 HVC_SYSTEMS (HEPA / ACH-outside) | **4** | `HVC_AIR_CHANGES_PER_HR` |
| 5.6 RAD_PROTECTION | **8** | — |
| 5.7 CEQ_CLINICAL | **7** + 2 calibration in MNT | `ASS_MANUFACTURER_TXT`, `ASS_MODEL_NR_TXT`, `ASS_SERIAL_NR_TXT`, `ASS_BARCODE_TXT`, `ASS_MAINTENANCE_FREQUENCY_MONTHS`, `ASS_MAINT_INTERVAL_TXT`, `ASS_MAINTENANCE_SCHEDULE_TXT`, `ASS_WARRANTY_*`, `MNT_*`, `CST_DELIVERY_LEAD_TIME_DAYS`, `CST_SUP_PROCUREMENT_LEAD_TIME_DAYS`, `ASS_OMNICLASS_TXT`, `ASS_UNICLASS_2015_TXT`, `ASS_UNIFORMAT_TXT`, `ASS_KEYNOTE_TXT` |
| 5.8 LIG_BEHAVIOURAL | **5** | — |
| 5.9 FLS_LIFE_SFTY (fire compartment) | **1** | `PER_FIRE_RATING_*`, `PROP_FIRE_RATING`, `RGL_FIRE_RATING_TXT`, `FLS_EVACUATION_TIME_MIN`, `FLS_EXIT_TRAVEL_DIST_M`, `FLS_SFTY_OCCUPANT_LOAD_NR` |
| 5.10 PRJ_ORG_HEALTH (expanded) | **15** | All other `PRJ_ORG_*` |
| 5.12 Structural loading (CLN sub-fields) | **7** | — |
| 5.13 Acoustic compliance (CLN + HVC sub-fields) | **2** | `PER_ACOUSTICS_BACKGROUND_NOISE_DB`, `BLE_DOOR_ACOUSTIC_RATING_DB`, `BLE_FLR_IMPACT_SOUND_INS_DB` |
| 5.14 IoT device refs (new group 33 `ICT_HEALTHIOT` + extends to groups 4/5/6/28/29/30/31) | **41** | `ELC_*` (group 4 existing), `HVC_*` (group 5 existing), `PLM_*` (group 6 existing), `MGS_*` (group 29), `RAD_*` (group 30), `CEQ_*` (group 31) |
| **Total net-new** | **~175** | (~30 existing parameters reused across all sub-packs) |

The first draft cited "~140" net-new params; the §5.0 crosscheck
reduced that to ~85 by eliminating duplicates. The gap analysis and
IoT cross-check (§19) then added 44 expanded CLN_CLINICAL fields,
7 structural, 2 acoustic, 41 IoT device-ref, and 3 new PRJ params —
bringing the revised net-new total to roughly **175**.

---

## 6. Healthcare validators (Phase H-5)

All validators slot into `Core/Validation/`, return the existing
`ValidationResult` record, and are surfaced through
`Commands/Validation/RunAllValidatorsCommand.cs` plus dedicated
single-validator commands for triage.

| Validator class | Targets | Rule summary |
|---|---|---|
| `MgasFlowValidator` | Pipes + TUs in `MGAS-*` systems | Diversified flow per HTM 02-01 / NFPA 99; pressure drop ≤ 10 % source pressure; gas-type indexing; ZVB serves only one alarm panel; brazing flag set |
| `EesBranchValidator` | Receptacles, electrical fixtures, equipment | Every patient-care socket is on `EES-LS` or `EES-CR`; ATS time ≤ 10 s; OR/ICU mix; cardiac-protected outlets in IT-CARD only |
| `PressureRegimeValidator` | Rooms with `ROM_PRESS_REGIME_TXT` | Anteroom cascade is monotonic; AIIR exhaust to outside; PE supply HEPA H14; OR ≥ 20 ACH; pressure-cascade arrows consistent across linked anterooms |
| `RadShieldValidator` | Walls / doors / windows around `ROM_RAD_*` rooms | NCRP 147 W·U·T → required mm Pb ≤ provided; doors interlocked with beam-on; controlled-area signage placed; MRI 5-Gauss fence respected; LINAC maze geometry plausible |
| `WaterSafetyValidator` | Plumbing fixtures + pipework | HTM 04-01 dead-leg ≤ 1 m for sentinel; TMV3 on aug-care; flush-loop temperatures within 50–60 °C; no blind branches; RO-loop topology closed; dialysis stations only on RO-loop |
| `AdjacencyValidator` | Rooms grouped by `ROM_HEALTH_CLASS_TXT` | HBN-derived: ED ↔ Imaging ≤ 3 doors; OR ↔ HSDU same compartment; mortuary remote; pharmacy central; clean / dirty flow non-crossing |
| `AntiLigatureValidator` | Family instances in `PSY-*` rooms | Every fitting carries `LIG_PRODUCT_RATING_TXT`; observation line-of-sight intact; en-suite door anti-barricade; no exposed pipes |
| `RdsCompletenessValidator` | Rooms | All ADB / HBN-required parameters populated; equipment groups accounted for; finishes match clinical class; signed off by clinician |
| `AcousticValidator` | Rooms + Walls / Floors / Ceilings | HTM 08-01 NR target by room class; FGI / HIPAA speech-privacy STC threshold; RT60 target vs CLN_RT60_TARGET_S_NR; NC curve vs HVC_NC_CURVE_INT; masking system present when STC < 45 |
| `StructuralLoadValidator` | Rooms with `CLN_FLOOR_LOAD_KN_M2_NR` or `CLN_FLOOR_LOAD_POINT_KN_NR` | Declared floor load ≥ equipment weight lookup from CEQ_CLINICAL classification; vibration criterion (CLN_VIB_VM_NR) flagged for imaging rooms if VC-D or stricter is not specified; structural sign-off date required before handover (`CLN_STRUCT_SIGN_OFF_DT` must be populated) |
| `AdvancedRadShieldValidator` | Walls / Doors / Windows in PET-HOT-LAB / NM-SCAN / BRACHYTHERAPY rooms | 511 keV PET two-photon geometry; SPECT/gamma camera scatter energy (140 keV Tc-99m); afterloading brachytherapy vault dose-rate calculation; all three require RAD_LEAD_MM_NR to account for energy-specific transmission, not just NCRP 147 kV shielding curves |
| `IoTStalenessValidator` | Rooms with `CLN_BMS_LAST_SEEN_DT` or `PLM_TMV_SENSOR_ID_TXT` | BMS last-seen timestamp > 30 min triggers sensor-fault warning; PLM_TMV_TEMP_ACTUAL_C_NR outside alert band triggers scald / Legionella warning; CLN_ENV_CERT_DUE_DT past-due raises cleanroom re-certification flag; RAD_DOSE_RATE_USV_HR_NR above RAD_DOSE_ALERT_USV_HR_NR raises life-safety alert |

The existing **Connectivity / Fill / Spec / Termination / Slope**
validators are reused — the healthcare validators chain on top, never
replace them.

### 6.1 RdsCompletenessValidator — mandatory parameter set

`RdsCompletenessValidator` checks the 14 parameters below on every room
whose `CLN_ROOM_CLASS_TXT` is non-empty (i.e. every clinical room).
A room that fails any check is flagged `RDS.INCOMPLETE` at Warning
severity; the room data sheet may not be issued until all items are
resolved.

| # | Parameter | Group | Applies to | Source standard |
|---|---|---|---|---|
| 1 | `CLN_ROOM_CLASS_TXT` | 28 CLN | All clinical rooms | ADB / HBN room-type enum |
| 2 | `ASS_ROOM_NAME_TXT` | 1 ASS | All rooms | ISO 19650 / ADB |
| 3 | `ASS_ROOM_NUM_TXT` | 1 ASS | All rooms | ISO 19650 / ADB |
| 4 | `ASS_ROOM_AREA_SQ_M` | 1 ASS | All rooms | HBN / FGI minimum area |
| 5 | `ASS_DESIGN_OCCUPANCY_INT` | 1 ASS | All rooms | FGI / BS 9999 |
| 6 | `HVC_AIR_CHANGES_PER_HR` | 5 HVC | All rooms with ventilation | HTM 03-01 / ASHRAE 170 |
| 7 | `PER_ENVIRONMENTAL_TEMP_DESIGN_C` | 8 PER | All rooms | HTM 03-01 / ASHRAE 170 |
| 8 | `PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT` | 8 PER | All rooms | HTM 03-01 / ASHRAE 170 |
| 9 | `BLE_ROOM_FINISH_FLOOR_TXT` | 10 BLE | All rooms | ADB finishes schedule |
| 10 | `BLE_ROOM_FINISH_CEILING_TXT` | 10 BLE | All rooms | ADB finishes schedule |
| 11 | `BLE_ROOM_FINISH_WALL_TXT` | 10 BLE | All rooms | ADB finishes schedule |
| 12 | `LTG_DESIGN_ILLUMINANCE_LUX` | 7 LTG | All rooms | HTM 06-01 / CIBSE LG2 |
| 13 | `PER_ACOUSTICS_BACKGROUND_NOISE_DB` | 8 PER | All rooms | HTM 08-01 |
| 14 | `FLS_COMPARTMENT_ID_TXT` | 8 FLS | All rooms | HTM 05-01 / BS 9999 |

**Room-class exemptions** (not yet enforced in code — planned for H-28
profile enhancement):

| Room class | Exempt from | Reason |
|---|---|---|
| `MORT`, `POST` | `HVC_AIR_CHANGES_PER_HR` mandatory-value gate | Down-flow / negative-pressure regimes defined separately via `ROM_PRESS_REGIME_TXT` |
| `DIAL` | `BLE_ROOM_FINISH_BASE_TXT` (not in the 14) | Plinths replace skirting |
| Non-clinical (stores, plant) | All 14 | Gate is `CLN_ROOM_CLASS_TXT` non-empty; plant/store rooms carry no clinical class |

**Parameters that are desirable but not yet mandatory** (candidates for
promotion in a future minor release after field calibration):

- `ASS_ROOM_VOLUME_CU_M` — needed for RT60 / reverberation calculation
- `PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT` range (min / max) rather than single design point
- `CLN_INFECTION_CTRL_CLASS_TXT` — required by CDC / ADB for isolation rooms
- `BLE_ROOM_FINISH_BASE_TXT` — skirting / coving finish (ADB)
- `CLN_STRUCT_SIGN_OFF_DT` — mandatory before handover for imaging rooms (currently enforced by `StructuralLoadValidator`, not `RdsCompletenessValidator`)

---

## 7. Drawing Type catalogue (Phase H-3)

Added to `Data/STING_DRAWING_TYPES.json`. All routed via the existing
`DrawingDispatcher` registry; each carries a corresponding routing rule.

| Id | Paper / scale | Purpose |
|---|---|---|
| `health-rds-A2`              | A2 / NA  | Room Data Sheet — one per clinical room (template, plan, elev, equipment list, services, finishes) |
| `health-eqp-pln-A1-1to50`    | A1 / 1:50 | Clinical equipment plan with FF&E callouts |
| `health-mep-coord-A1-1to50`  | A1 / 1:50 | MEP coordination with shielding overlay |
| `health-medgas-pln-A1-1to100`| A1 / 1:100 | MGPS layout |
| `health-medgas-schem-A1`     | A1 / NA  | MGPS schematic (zone valves, manifolds, alarms, plant) |
| `health-pressure-pln-A1-1to100`| A1 / 1:100 | Pressure-regime plan with arrows |
| `health-ess-power-riser-A1`  | A1 / NA  | Essential power single-line (LS/CR/EQ branches + ATS) |
| `health-ips-pln-A1-1to50`    | A1 / 1:50 | Isolated power systems for OR/ICU |
| `health-decon-flow-A1-1to50` | A1 / 1:50 | Decontamination dirty-clean-sterile flow |
| `health-mortuary-pln-A2-1to50`| A2 / 1:50| Mortuary / post-mortem suite |
| `health-fire-comp-A1-1to100` | A1 / 1:100 | Fire compartment + PHE refuge plan |
| `health-rad-shield-A1-1to50` | A1 / 1:50 | Radiation shielding plan with mm Pb callouts |
| `health-mri-zoning-A1-1to50` | A1 / 1:50 | MRI suite Z1–Z4 zoning + 5-Gauss line |
| `health-ligature-pln-A1-1to50`| A1 / 1:50 | Anti-ligature plan with risk levels |
| `health-bedhead-elev-A2-1to20`| A2 / 1:20| Bedhead trunking elevations |
| `health-or-ceiling-A2-1to20` | A2 / 1:20 | OR ceiling reflected plan with pendant booms |
| `health-acoustic-pln-A1-1to100` | A1 / 1:100 | Acoustic zoning plan — NR targets per room, STC of separating walls, masking zones |
| `health-struct-loads-A1-1to50` | A1 / 1:50 | Structural loading plan — floor live-load zones, point-load callouts, vibration-isolation pads |
| `health-infection-ctrl-A1-1to100` | A1 / 1:100 | Infection-control classification plan — room colour by class (AIIR/PE/Standard), coving, pass-through, handwash count |
| `health-waste-flow-A1-1to100` | A1 / 1:100 | Waste flow diagram — segregation by waste class, chute / collection routes, disposal points |
| `health-rtls-coverage-A1-1to100` | A1 / 1:100 | RTLS anchor coverage plan — beacon locations, technology type (BLE/UWB/IR), coverage radii, RF dead zones (shielded rooms) |
| `health-nm-schem-A1` | A1 / NA | Nuclear medicine / PET schematic — dose-calibrator, hot lab, decay storage, exhaust route, dose-rate contours |

Each drawing type binds to an appropriate ViewStylePack (§8) and
publishes a routing rule keyed on `(discipline=H/MG/RP, docType=*)`.

---

## 8. ViewStylePacks (Phase H-2)

| Pack id | Extends | Highlights |
|---|---|---|
| `corp-healthcare-clinical` | `corp-standard-plan` | Lighter wash for non-clinical content; hatched flooring for wet rooms; clinical equipment in saturated colour; nurse-call posts visible at coarse |
| `corp-healthcare-shielding` | `corp-standard-plan` | Pb-equivalent walls dashed-thick; X-ray hazard hatch; MRI 5-Gauss line as red dashed circle; controlled-area signage as halo |
| `corp-healthcare-pressure` | `corp-standard-plan` | Negative rooms blue tint, positive red, neutral grey, anterooms striped; pressure arrows as detail components |
| `corp-healthcare-fire` | `corp-coordination` | Fire compartments in primary colours; PHE refuge zones halftoned; smoke-rated lines distinct from fire-rated |
| `corp-healthcare-acoustic` | `corp-standard-plan` | Room boundaries tinted by acoustic class (sensitive / medium / not-sensitive); STC callout annotation visible at coarse; sound-masking zones as overlay pattern |
| `corp-healthcare-structural` | `corp-coordination` | Floor-load zone polygons in graduated fill intensity; point-load symbols prominent at 1:50; vibration-isolation elements as hatched pattern |

All in `managed` template mode so STING auto-creates and maintains the
underlying Revit view templates.

---

## 9. Filter library extensions (Phase H-2)

60 new filters added to `Data/STING_AEC_FILTERS.json`, organised in 8
groups. All use the existing `AecFilterDefinition` rule grammar; default
overrides supplied so they work without per-pack tuning.

| Group | Filter examples (subset) |
|---|---|
| **Pressure** | `STING - Press Negative`, `... Positive`, `... Neutral`, `... Anteroom Cascade Out-of-Spec` |
| **EES branches** | `STING - EES Life Safety`, `... Critical`, `... Equipment`, `... Normal`, `... IPS`, `... Cardiac IT-Net` |
| **Med gas by gas** | One filter per gas (`O2`, `MA4`, `MA7`, `N2O`, `N2`, `CO2`, `HE`, `VAC`, `AGS`) + ZVB / alarm panel |
| **Infection-control** | `STING - AIIR`, `... PE`, `... Class-N`, `... Class-P`, `... Anteroom`, `... Decon Dirty`, `... Decon Clean`, `... HSDU Sterile` |
| **Radiation** | `STING - Rad Controlled`, `... Uncontrolled`, `... Primary Barrier`, `... Secondary Barrier`, `... MRI Z1..Z4`, `... 5-Gauss Boundary` |
| **Fire / PHE** | `STING - Fire Compartment <id>`, `... PHE Refuge`, `... Smoke-Rated`, `... 60-min`, `... 120-min` |
| **Anti-ligature** | `STING - Lig High Risk`, `... Med Risk`, `... Low Risk`, `... Observation LOS Required` |
| **Clinical equipment** | `STING - Clinical Asset`, `... Critical (Spaulding)`, `... Semi-Critical`, `... Bariatric`, `... Hoist-tracked` |
| **Acoustic** | `STING - Acoustic Sensitive`, `... Acoustic Medium`, `... Acoustic Not-Sensitive`, `... STC Deficient`, `... Masking Required`, `... RT60 Out-of-Spec` |
| **Structural loads** | `STING - Heavy Equipment Zone (>5 kN/m²)`, `... Point Load ≥ 50 kN`, `... Vibration Sensitive (VC-D/E)`, `... Structural Sign-off Missing` |
| **RTLS / IoT** | `STING - RTLS BLE Anchor`, `... RTLS UWB Anchor`, `... RTLS IR Anchor`, `... Infant Security Reader`, `... Nurse Call Node`, `... IoT BMS Sensor`, `... IoT BMS Sensor Stale (>30 min)` |
| **Waste** | `STING - Waste Clinical`, `... Waste Anatomical`, `... Waste Pharmaceutical`, `... Waste Cytotoxic`, `... Waste Radioactive`, `... Waste Sharps` |

Total expanded to approximately **80 healthcare filters** across **12 groups** (up from the original 60 across 8 groups).

---

## 10. Phasing roadmap

| Phase | Title | Approx scope | Touches |
|---|---|---|---|
| **H-1** | Vocabulary + parameter pack | Add disciplines / systems / functions / products to `TagConfig`; add ~140 shared params to `MR_PARAMETERS.txt`; bind via `PARAMETER_REGISTRY.json`; clinical room-class enum | Pure data — no Revit API surface change |
| **H-2** | Filters + ViewStylePacks | 60 filters + 4 packs; routing into managed templates | `Data/STING_AEC_FILTERS.json`, `Data/STING_VIEW_STYLE_PACKS.json` |
| **H-3** | Drawing Type catalogue | 16 healthcare drawing types + routing rules | `Data/STING_DRAWING_TYPES.json` |
| **H-4** | Standards-API skeleton | `Standards/HTM/`, `Standards/HBN/`, `Standards/FGI/`, `Standards/NFPA99/`, `Standards/NCRP147/`, `Standards/ASHRAE170/`, `Standards/USP797800/` — checklist + lookup tables; expose through `StandardsAPI` |
| **H-5** | Healthcare validators | 8 validators (§6) + commands; chained from `RunAllValidatorsCommand`; surfaced in BCC |
| **H-6** | COBie healthcare overlay | ~100 SFG20-Healthcare schedules into `COBIE_JOB_TEMPLATES.csv`; ~50 clinical equipment types into `COBIE_TYPE_MAP.csv`; healthcare picklists; new BIMManager preset `Hospital-NHS` |
| **H-7** | Medical-gas package | Family parameter specs (no .rfa shipped — same policy as v4 title-blocks); MGPS schematic generator; `MgasFlowValidator`; verification workflow |
| **H-8** | Room Data Sheet engine | E17 RDS template (`Docs/_template_sources/health_rds.docx`); per-room data extractor; batch issue command; round-trip with Excel for MEP authors |
| **H-9** | Radiation & MRI | NCRP 147 lead-equiv calc + checklist; MRI zone overlay validator; LINAC vault sketch helper |
| **H-10** | Adjacency + flow analyser | HBN-derived adjacency matrix tool; clean / dirty flow analyser; AGV / pneumatic-tube path tool |
| **H-11** | Anti-ligature pack | Anti-ligature parameters + validator + Drawing Type + Filter group; HBN 03-01 / FGI behavioural-health subset |
| **H-12** | Hybrid OR / Cath / IR | Equipment-clearance collision tool; ceiling-zone planner; biplane / floor-mount imaging clash; documented clearance bands |
| **H-13** | Pharmacy USP 797 / 800 | Pressure-cascade validator; C-PEC / C-SEC param set; airlock-sequence checker |
| **H-14** | Behavioural / Mental health (FGI Pt 2) | Observation line-of-sight tool; safety-risk-assessment doc generator; self-harm-risk room class |
| **H-15** | Mortuary / Post-mortem | HBN 16 room-types catalogue; refrigeration capacity calc; viewing-room privacy partition |
| **H-16** | Maternity / NICU | HBN 21 / 09-03 room-types; couplet care room; warming-cot, milk-kitchen workflows |
| **H-17** | Decontamination / HSDU | HBN 13 dirty-clean-sterile flow; washer-disinfector + autoclave clearance pack |
| **H-18** | Dialysis | RO-loop topology validator; satellite vs main unit pack; HBN 07-02 |
| **H-19** | Hyperbaric / specialist | HBO suite oxygen-fire envelope; cytotoxic & milk kitchen; IVF / ART |
| **H-20** | Digital twin / IoT bridge | BACnet / OPC-UA pressure read-back; MGPS verification log; SignalR live dashboards on the BIM Coordination Centre Healthcare tab |
| **H-21** | Mobile commissioning app | New `Planscape/app/healthcare/` tab — on-site MGPS verification checklist, pressure-cascade live read, water-flushing logs |
| **H-22** | Server APIs | `Planscape.API/Controllers/HealthcareController.cs` — pressure logs, MGPS verifications, RDS retrieval, anti-ligature audit submissions |
| **H-23** | Structural loads for heavy equipment | §5.12 params; `StructuralLoadValidator`; `health-struct-loads-A1-1to50` drawing type; `corp-healthcare-structural` pack; structural filter group; phase notes §11 |
| **H-24** | Acoustic compliance | §5.13 params; `AcousticValidator`; `health-acoustic-pln-A1-1to100` drawing type; `corp-healthcare-acoustic` pack; acoustic filter group; HTM 08-01 NR table per room class |
| **H-25** | Advanced imaging shielding (PET / NM / brachytherapy) | `AdvancedRadShieldValidator` for 511 keV / SPECT / afterloading; `health-nm-schem-A1` drawing type; PET-HOT-LAB / NM-SCAN / BRACHYTHERAPY room class params; NCRP 151 supplementary tables |
| **H-26** | HTM 01-06 endoscope traceability | §5.14 endoscope IoT params (`CEQ_ENDO_*`); RFID reader BIM families; `ENDOSCOPY` room class; endoscope-scope-to-patient-to-procedure chain validator; decon flow drawing type; workflow JSON |
| **H-27** | Resilience and business continuity | EES generator test log params (`ELC_GEN_TEST_LOG_REF_TXT`, `ELC_ATS_TEST_LOG_REF_TXT`, `ELC_UPS_REPLACE_DT`); NFPA 110 compliance dashboard; server-side weekly-test log ingestion endpoint |
| **H-28** | Healthcare-lite profiles | `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` runtime gating; `COMMUNITY` / `DENTAL` / `IMAGING-ONLY` sub-profiles that suppress irrelevant validators, drawing types, and COBie presets; reduce load for non-acute projects |
| **H-29** | RTLS infrastructure | §5.14 RTLS params; `health-rtls-coverage-A1-1to100` drawing type; RTLS filter group; `Core/Placement/` rules for BLE/UWB anchor placement (coverage radius optimisation, RF dead-zone flagging in shielded rooms); anchor-to-zone-ID link validator |
| **H-30** | Waste-management IoT | §5.14 waste IoT extension; `health-waste-flow-A1-1to100` drawing type; waste filter group; smart-bin device ID params; IoT alert routing for cytotoxic / radioactive waste container breach |

H-1..H-3 are pure-data prerequisites; everything else depends only on
those three. H-7..H-19 are independent special-case packs that can be
shipped in any order after the common foundation. H-23..H-30 are gap-analysis
additions; each is independent and can ship in any order after H-1..H-3.

---

## 11. Detailed phase notes

### Phase H-1 — Vocabulary & parameter pack

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+~85 rows — see §5.0 crosscheck for re-prefixing
                                                          and duplicate elimination against the existing 2,555 params)
StingTools/Data/MR_PARAMETERS.csv                     (mirror)
StingTools/Data/PARAMETER_REGISTRY.json               (+5 GROUP rows: 28 CLN_CLINICAL, 29 MGS_SYSTEMS,
                                                          30 RAD_PROTECTION, 31 CEQ_CLINICAL,
                                                          32 LIG_BEHAVIOURAL; container groups + bindings)
StingTools/Data/TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv     (new disciplines H/MG/RP, MGS-* systems, clinical functions)
StingTools/Data/TAG_CONFIG_v5_0_CONTAINERS.csv        (5 new container rows: CLN_TAG, MGS_TAG, RAD_TAG, CEQ_TAG, LIG_TAG)
StingTools/Data/TAG_CONFIG_v5_0_VALIDATION.csv        (clinical validation rules)
StingTools/Data/STING_TAG_CONFIG_v5_0_HEALTH.csv      (NEW — 70+ tag families across H/MG/RP)
StingTools/Core/ParamRegistry.cs                      (constants for hot-path params, e.g. CLN_ROOM_CLASS,
                                                          MGS_GAS_TYPE, ELC_EES_BRANCH, RAD_LEAD_MM)
StingTools/Core/TagConfig.cs                          (+1 const file load + ISO 19650 cross-validation entries)
```

**Acceptance**

- `LoadSharedParamsCommand` binds 100 % of new parameters in two passes
  without errors on a stock Revit project (no missing GUIDs).
- No naming clash with existing `HEALTH_METRICS` (group 23) — all
  clinical attributes use the `CLN_*` prefix; the model-health-score
  pair (`HEALTH_SCORE_LAST_NR`, `HEALTH_SCORE_DATE_TXT`) is untouched.
- Existing `WARN_BLE_MEDICAL_CLEARANCE_MEDICAL_EQUIP` and the four
  other healthcare-flavoured `WARN_*` thresholds remain wired to
  `WarningsManager` — Phase H-5 validators publish back into them
  rather than re-introducing the same warnings.
- Existing `ASS_TIEIN_*` framework (10 params) is reused for medical-gas
  terminal-unit ↔ zone-valve-box ↔ alarm-panel linkage; no parallel
  link framework is introduced.
- `Tags.AutoTagCommand` on a sample hospital project produces non-empty
  `H-*-*-*-*-*-*-NNNN` tags for clinical equipment.
- `PreTagAuditCommand` reports < 5 % unresolved tokens.

### Phase H-2 — Filters + ViewStylePacks

**Where it lands**

```
StingTools/Data/STING_AEC_FILTERS.json                (+60 filters)
StingTools/Data/STING_VIEW_STYLE_PACKS.json           (+4 packs)
docs/HEALTHCARE_PACK_DESIGN.md                        (this file — section §9)
```

**Acceptance**

- `AecFiltersInspect` reports all 60 new filters as create-able.
- Applying `corp-healthcare-pressure` colours rooms by regime.
- Drift detection (`DrawingDriftDetector`) treats the new packs as
  managed.

### Phase H-3 — Drawing Type catalogue

**Where it lands**

```
StingTools/Data/STING_DRAWING_TYPES.json              (+16 drawing types, +12 routing rules)
docs/HEALTHCARE_PACK_DESIGN.md                        (§7)
```

**Acceptance**

- `DrawingTypes_Inspect` lists all healthcare types as origin=corporate.
- `DrawingDispatcher.Resolve(doc, "MG", "*", Schematic)` returns
  `health-medgas-schem-A1`.

### Phase H-4 — Standards-API skeleton

**Where it lands**

```
StingTools.Standards/HTM/                             (HTM 00..08 lookup tables, checklist generators)
StingTools.Standards/HBN/                             (HBN catalogue keyed by HBN id; cross-refs to room class)
StingTools.Standards/FGI/                             (FGI 2026 sections + cross-ref)
StingTools.Standards/NFPA99/                          (Ch. 5 medical gas, Ch. 6 EES, Ch. 14 emergency mgmt)
StingTools.Standards/NCRP147/                         (W·U·T calc + barrier solver)
StingTools.Standards/ASHRAE170/                       (table 7.1 ACH + pressure look-up)
StingTools.Standards/USP797800/                       (pressure cascade + cleanroom class table)
StingTools.Standards/StandardsAPI.cs                  (extend façade)
StingTools.Standards/StandardsAPI_ResultClasses.cs    (new result types)
```

**Approach**

Mirror existing `Standards/CIBSE/` and `Standards/NFPA/` patterns —
classes are stateless lookup engines, no I/O at runtime, all reference
tables embedded as resources.

**Acceptance**

- Unit test (or `RunAll` smoke test) returns expected ACH for a sample
  AIIR, expected mm Pb for a sample chest-room shielding query, expected
  EES branch for a sample patient-care receptacle.

### Phase H-5 — Healthcare validators

**Where it lands**

```
StingTools/Core/Validation/Healthcare/               (NEW subdirectory)
  MgasFlowValidator.cs
  EesBranchValidator.cs
  PressureRegimeValidator.cs
  RadShieldValidator.cs
  WaterSafetyValidator.cs
  AdjacencyValidator.cs
  AntiLigatureValidator.cs
  RdsCompletenessValidator.cs
StingTools/Commands/Validation/Healthcare*.cs        (8 single-validator commands + RunAllHealthcareCommand)
```

**Wiring**

Each validator's `Validate(doc)` returns `ValidationResult`. They are
chained from `RunAllValidatorsCommand` *and* surfaced as a separate
`RunAllHealthcareValidatorsCommand` so non-healthcare projects don't pay
the cost. The BCC's new Healthcare tab (Phase H-20) consumes these
results.

### Phase H-6 — COBie healthcare overlay

**Where it lands**

```
StingTools/Data/COBIE_TYPE_MAP.csv                    (+50 clinical equipment types)
StingTools/Data/COBIE_SYSTEM_MAP.csv                  (+11 healthcare systems)
StingTools/Data/COBIE_PICKLISTS.csv                   (+~80 healthcare picklist values)
StingTools/Data/COBIE_JOB_TEMPLATES.csv               (+100 SFG20-Healthcare references)
StingTools/Data/COBIE_SPARE_PARTS.csv                 (+~60 healthcare-specific spares)
StingTools/Data/COBIE_DOCUMENT_TYPES.csv              (+12 healthcare doc types)
StingTools/BIMManager/BIMManagerCommands.cs           (new COBiePreset entries: Hospital-NHS, Hospital-FGI, Mental-Health-Unit, Day-Surgery, Diagnostic-Imaging-Centre, Pharmacy)
```

**Approach**

The pack carries SFG20 *identifiers and job structure* only. Licensed
schedule prose is loaded from the project sandbox at
`<project>/_BIM_COORD/healthcare/sfg20/` if the customer has an SFG20
licence. Without a licence, the pack still produces a valid COBie export
but the long-form maintenance text is blank.

### Phase H-7 — Medical-gas package

**Where it lands**

```
StingTools/Core/MedGas/                               (NEW)
  MgasNetwork.cs                                      (graph builder over all MGAS-* systems)
  MgasZoneSolver.cs                                   (assigns ZVBs to terminal-unit groups)
  MgasFlowSolver.cs                                   (diversified flow + pressure-drop)
  MgasSchematicComposer.cs                            (auto-generates the A1 schematic)
  MgasVerificationLog.cs                              (NFPA 99 §5.1.12 verifier checklist persistence)
StingTools/Commands/MedGas/                           (NEW)
  MgasNetworkAuditCommand.cs
  MgasGenerateSchematicCommand.cs
  MgasVerifyCommand.cs
  MgasZoneRenumberCommand.cs
StingTools/Data/MEDGAS_TERMINAL_UNITS_BS5682.csv      (terminal-unit catalogue + indexing)
StingTools/Data/MEDGAS_PIPE_SIZING.csv                (HTM 02-01 sizing table)
Families/MedGas/                                      (parameter specs — no .rfa shipped, parity with v4 title-blocks)
```

**Approach**

`MgasNetwork` walks `MEPSystem` graphs filtered to MGAS-* systems,
assigns each terminal unit to its ZVB by spatial query + pipe-graph
ancestry, computes diversified flow per HTM 02-01 / NFPA 99, and emits
a schematic into a drafting view using `MgasSchematicComposer`.
`MgasVerifyCommand` writes a verification log to
`<project>/_BIM_COORD/healthcare/mgas_verifications/YYYYMMDD_zoneN.json`
and pushes to `Planscape.Server` via the existing `PlanscapeServerClient`.

### Phase H-8 — Room Data Sheet engine

**Where it lands**

```
StingTools/Docs/_template_sources/health_rds.docx     (E17 — Room Data Sheet)
StingTools/Docs/_template_sources/health_rds_extra.docx (E17b — multi-room schedule)
StingTools/Docs/Templates/RdsContextBuilder.cs        (extends TokenContext with clinical fields)
StingTools/Docs/Templates/RdsRenderer.cs              (façade over MiniWordAdapter)
StingTools/Commands/Healthcare/                       (NEW)
  IssueRoomDataSheetCommand.cs                        (one room)
  BatchIssueRoomDataSheetsCommand.cs                  (whole project / by class)
  ImportRoomDataSheetCommand.cs                       (round-trip from Excel for clinical authors)
StingTools/Data/HEALTHCARE_RDS_FIELDMAP.json          (token → param mapping)
```

**Token surface (subset)**

```
{{room.number}} {{room.name}} {{room.area}} {{room.height}}
{{room.health_class}} {{room.hbn_ref}} {{room.adb_code}}
{{room.press.regime}} {{room.press.delta_pa}}
{{room.ach.req}} / {{room.ach.outside}}
{{room.temp.design_c}} {{room.rh.design_pct}} {{room.noise.nr}}
{{#each services}} {{type}} {{count}} {{notes}} {{/each}}
{{#each equipment}} {{prod_code}} {{description}} {{make_model}} {{notes}} {{/each}}
{{#each finishes}} {{element}} {{spec}} {{notes}} {{/each}}
{{room.lighting.lux}} {{room.lighting.ra}}
{{room.fire_comp}} {{room.phe_refuge}}
{{room.lig.risk}} {{room.bari.swl_kg}}
{{signoff.architect}} {{signoff.clinician}} {{signoff.date}}
```

The renderer reuses the existing `MiniWordAdapter` post-processor for
`{{link:…}}`, `{{#if …}}`, and looped tables — no new template-engine
machinery required.

### Phase H-9 — Radiation & MRI

**Where it lands**

```
StingTools/Core/Radiation/                            (NEW)
  Ncrp147Calculator.cs                                (W·U·T → required mm Pb)
  Ncrp151Calculator.cs                                (LINAC primary + secondary + neutron)
  ShieldingValidator.cs                               (provided vs required)
  MriZoneEngine.cs                                    (Z1-Z4 + 5-Gauss boundary geometry)
StingTools/Commands/Radiation/                        (NEW)
  RadCalcChestRoomCommand.cs                          (worked-example wizard)
  RadCalcCtRoomCommand.cs
  RadCalcLinacVaultCommand.cs
  MriZoneAuditCommand.cs
StingTools/Data/RADIATION_NCRP147_TABLES.csv          (transmission curves digitised)
StingTools/Data/RADIATION_NCRP151_TABLES.csv
```

**Approach**

The calculator is a textbook implementation of NCRP 147 §4–§6
(transmission curves α, β, γ for concrete, lead, gypsum, steel) and
NCRP 151 chapter 5 (workload, use factor, occupancy factor, primary
barrier, secondary barrier from leakage and patient-scatter, neutron
shielding for ≥10 MV LINAC). Designs are not certified by STING — the
output is a draft for the Qualified Expert (`PRJ_HEALTH_QE_TXT`) to
sign off, and the validator only reports drift, never overrides the QE.

### Phase H-10 — Adjacency + flow analyser

**Where it lands**

```
StingTools/Core/Adjacency/                            (NEW)
  AdjacencyMatrix.cs                                  (HBN-derived target adjacencies)
  RoomGraphBuilder.cs                                 (door / corridor graph from doc)
  CleanDirtyFlowSolver.cs                             (BFS for crossing detection)
  PathPlanner.cs                                      (pneumatic tube + AGV paths)
StingTools/Commands/Adjacency/
  AdjacencyAuditCommand.cs
  CleanDirtyFlowAuditCommand.cs
  PneumaticTubePathCommand.cs
  AgvPathPlanCommand.cs
StingTools/Data/HEALTHCARE_ADJACENCY_HBN.csv          (target-adjacency matrix per HBN)
```

The `RoomGraphBuilder` reuses the existing room/door collectors and
emits a simple adjacency graph; the AGV planner reuses
`Core/Routing/DropEngineBase` for path scoring.

### Phase H-11..H-19 — Special-case packs

Each follows the same template:

1. New parameter sub-pack (typically 5–15 params).
2. New filter group (5–10 filters).
3. New drawing type (1–3).
4. New validator (1–2).
5. New command pack in `Commands/Healthcare/<topic>/`.
6. New COBie picklist entries.
7. Documented sample in `docs/HEALTHCARE_<topic>.md`.

The phasing is independent — H-15 (Mortuary) does not block H-16
(Maternity); they can ship in any order after H-1..H-3.

### Phase H-20 — Digital twin / IoT bridge

**Where it lands**

```
StingTools/Core/Twin/                                 (NEW)
  BacnetReadback.cs                                   (BACnet/IP read of pressure / ACH from BMS)
  OpcUaReadback.cs                                    (OPC-UA alternative)
  MgasAlarmSubscriber.cs                              (subscribes to area/master alarm panels)
  TwinDashboardModel.cs                               (rolls up live metrics for BCC)
Planscape.Server/src/Planscape.API/Controllers/HealthcareController.cs
                                                      (POST /api/projects/{id}/healthcare/pressure-log,
                                                       POST /api/projects/{id}/healthcare/mgas-verification,
                                                       GET  /api/projects/{id}/healthcare/dashboard,
                                                       POST /api/projects/{id}/healthcare/anti-ligature-audit,
                                                       GET  /api/projects/{id}/healthcare/rds/{roomId})
Planscape.Server/src/Planscape.Core/Entities/         (HealthcarePressureLog, HealthcareMgasVerification,
                                                       HealthcareAntiLigatureAudit, HealthcareRdsSnapshot)
Planscape.Server/src/Planscape.Infrastructure/SignalR/HealthcareHub.cs
                                                      (live pressure / alarm broadcast)
```

**Approach**

`BacnetReadback` runs in a background thread on the plugin side, polls
configured BMS objects per room (mapped via `ROM_BMS_OBJECT_REF_TXT`),
and pushes time-series snapshots to the server. The BCC Healthcare tab
subscribes to `HealthcareHub` for live data; the mobile app subscribes
to the same hub for on-site dashboards. The plugin never *commands* the
BMS — read-only by design.

### Phase H-21 — Mobile commissioning app

**Where it lands**

```
Planscape/app/healthcare/                             (NEW)
  _layout.tsx
  index.tsx                                           (overview — RAG by room class)
  mgas-checklist.tsx                                  (NFPA 99 §5.1.12 walk-through)
  pressure-live.tsx                                   (per-room live Δp + arrow)
  water-flush.tsx                                     (HTM 04-01 sentinel logs)
  anti-ligature-audit.tsx                             (per-fitting checklist with photo + GPS)
  rds-viewer.tsx                                      (read-only RDS render)
Planscape/src/api/endpoints.ts                        (extend with healthcare endpoints)
```

Reuses existing offline queue, push-token infrastructure, biometric lock.

### Phase H-22 — Server APIs

Already enumerated under Phase H-20. New `Planscape.Core` entities are
audited via the existing `AuditLog` chain (SHA-256 tamper-evidence).
Push notifications fire on pressure breaches and MGPS alarms via the
existing `INotificationService`.

### Phase H-23 — Structural loads for heavy medical equipment

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+7 CLN_STRUCT_* params — §5.12)
StingTools/Data/PARAMETER_REGISTRY.json               (+7 entries in group 28)
StingTools/Core/Validation/Healthcare/StructuralLoadValidator.cs
StingTools/Commands/Validation/Healthcare/StructuralLoadValidatorCommand.cs
StingTools/Data/STING_DRAWING_TYPES.json              (+health-struct-loads-A1-1to50)
StingTools/Data/STING_VIEW_STYLE_PACKS.json           (+corp-healthcare-structural)
StingTools/Data/STING_AEC_FILTERS.json                (+4 structural filters)
StingTools/Data/HEALTHCARE_EQUIPMENT_WEIGHTS.csv      (NEW — reference weights for MRI/CT/LINAC/PET/fluoroscopy)
```

**Acceptance**

- `StructuralLoadValidator` warns when a room classified as `IMG-MRI`
  has `CLN_FLOOR_LOAD_KN_M2_NR` < 15 kN/m² or `CLN_VIB_VM_NR` >
  VC-C threshold (8 µm/s).
- `CLN_STRUCT_SIGN_OFF_DT` empty on handover raises a blocking
  `RdsCompletenessValidator` error.
- Note: the validator reports drift only — it does not certify the
  structure. The Structural Engineer's sign-off (`CLN_STRUCT_SIGN_OFF_TXT`)
  is mandatory before handover.

### Phase H-24 — Acoustic compliance

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+2 params: CLN_RT60_TARGET_S_NR, HVC_NC_CURVE_INT)
StingTools/Core/Validation/Healthcare/AcousticValidator.cs
StingTools/Commands/Validation/Healthcare/AcousticValidatorCommand.cs
StingTools/Data/STING_DRAWING_TYPES.json              (+health-acoustic-pln-A1-1to100)
StingTools/Data/STING_VIEW_STYLE_PACKS.json           (+corp-healthcare-acoustic)
StingTools/Data/STING_AEC_FILTERS.json                (+6 acoustic filters)
StingTools/Data/HEALTHCARE_ACOUSTIC_NR_TARGETS.csv    (NEW — HTM 08-01 NR target table by room class)
```

**Key reference data**

HTM 08-01 Table 1 NR targets (excerpt): wards NR 35, consulting rooms
NR 35, operating theatres NR 40, ICU NR 35, mental health NR 30.
FGI / HIPAA speech-privacy STC thresholds: STC 45 for exam rooms,
STC 50 for private consultation. The acoustic filter pack colour-codes
rooms below threshold red.

### Phase H-25 — Advanced imaging shielding (PET / NM / brachytherapy)

**Where it lands**

```
StingTools/Core/Radiation/AdvancedRadShieldValidator.cs
StingTools/Core/Radiation/Pet511Calculator.cs         (511 keV two-photon geometry)
StingTools/Core/Radiation/SpectCalculator.cs          (140 keV Tc-99m scatter)
StingTools/Core/Radiation/BrachyVaultCalculator.cs    (afterloading dose-rate calc)
StingTools/Commands/Radiation/AdvancedRadCalcCommand.cs
StingTools/Data/STING_DRAWING_TYPES.json              (+health-nm-schem-A1)
StingTools/Data/RADIATION_PET_TABLES.csv              (NEW — 511 keV concrete/Pb/steel attenuation)
StingTools/Data/RADIATION_BRACHY_SOURCES.csv          (NEW — common afterloading source data)
```

**Caveats**

PET 511 keV, SPECT, and brachytherapy shielding require significantly
more concrete than diagnostic X-ray for equivalent Pb values. The
`AdvancedRadShieldValidator` warns any RAD_LEAD_MM_NR entered on a
PET room that was derived from NCRP 147 (kV) curves without energy
correction. All outputs require QE sign-off (`RAD_QE_NAME_TXT`).

### Phase H-26 — HTM 01-06 endoscope decontamination traceability

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+5 CEQ_ENDO_* params — §5.14)
StingTools/Core/Validation/Healthcare/EndoscopeTraceValidator.cs
StingTools/Commands/Healthcare/Endoscopy/
  EndoscopeTraceAuditCommand.cs
  EndoscopeDeconFlowCommand.cs
StingTools/Data/STING_DRAWING_TYPES.json              (+health-decon-flow-A1-1to50 already in §7; supplement with endo detail)
StingTools/Data/HEALTHCARE_ENDOSCOPE_TYPES.csv        (NEW — HTM 01-06 Annex C scope types + decon method)
WORKFLOW_HTM-01-06-EndoReprocess.json                 (NEW — soak → wash → AER → drying → storage RFID chain)
```

**Scope / RFID chain**

The validator checks that each `ENDOSCOPY` room has at minimum 4 RFID
reader positions modelled (`ICT_ENDO_READER_ID_TXT` on door/storage
families): soak sink, washer-disinfector (AER) inlet, drying cabinet,
and sterile storage. Each scope asset carries `CEQ_ENDO_SCOPE_ID_TXT`,
`CEQ_ENDO_AER_REF_TXT`, and `CEQ_ENDO_CYCLE_COUNT_INT`. The RFID
chain is the BIM contract topology — the live event log is pushed to
Planscape Server.

### Phase H-27 — Resilience and business continuity

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+ELC_GEN_TEST_LOG_REF_TXT, ELC_ATS_TEST_LOG_REF_TXT, ELC_UPS_REPLACE_DT)
StingTools/Core/Validation/Healthcare/EesResilienceValidator.cs
StingTools/Commands/Healthcare/Resilience/EesResilienceDashboardCommand.cs
Planscape.Server/Controllers/HealthcareController.cs  (+POST .../generator-test, +GET .../resilience-dashboard)
WORKFLOW_NFPA110-GeneratorTest.json                   (NEW — weekly run test → record runtime → upload log)
```

NFPA 110 and NFPA 99 mandate monthly ATS transfer tests and weekly
generator exercise runs for Level 1 systems. The resilience validator
flags any EES circuit whose `ELC_GEN_TEST_LOG_REF_TXT` has not been
updated within 35 days (7-day test + 4-week buffer).

### Phase H-28 — Healthcare-lite profiles (pack scoping)

**Where it lands**

```
StingTools/Core/ParamRegistry.cs                      (+PRJ_ORG_HEALTH_PACK_PROFILE_TXT constant)
StingTools/Core/Validation/Healthcare/HealthcareValidatorGate.cs
                                                      (reads pack profile, returns bool IsInScope(validatorType))
StingTools/Core/Drawing/DrawingTypeRegistry.cs        (profile-filtered Resolve overload)
StingTools/Data/HEALTHCARE_PACK_PROFILES.json         (NEW — FULL/ACUTE/COMMUNITY/DENTAL/IMAGING-ONLY gate tables)
```

A `COMMUNITY` profile suppresses LINAC vault, HBO, BSL4, and advanced
imaging validators. An `IMAGING-ONLY` profile activates only radiation,
MRI, RTLS, and structural-load validators. A `DENTAL` profile activates
dental-compressed-air terminal units, MGPS-DENT system, and HTM 01-05
decon flow. Gate logic runs in `HealthcareValidatorGate` so individual
validator `Validate()` implementations never inspect project info — the
gate injects a filtered list at the call site.

### Phase H-29 — RTLS infrastructure

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+ICT_RTLS_ANCHOR_ID_TXT, ICT_RTLS_TECH_TXT,
                                                          ICT_RTLS_TX_POWER_DBM_NR, ICT_RTLS_COVERAGE_M_NR — §5.14)
StingTools/Data/STING_DRAWING_TYPES.json              (+health-rtls-coverage-A1-1to100)
StingTools/Data/STING_AEC_FILTERS.json                (+7 RTLS filters)
StingTools/Core/Placement/RtlsAnchorPlacementRules.json (+rules for BLE/UWB minimum anchor density)
StingTools/Core/Validation/Healthcare/RtlsCoverageValidator.cs
```

**RF dead zones**

The `RtlsCoverageValidator` flags RTLS anchors placed in rooms with
`CLN_RF_SHIELD_BOOL = true` (MRI Faraday cage, lead-lined radiology,
EP lab) with a WARNING: BLE/Wi-Fi signals do not penetrate these rooms;
UWB and IR are the only viable options. Each anchor family's
`ICT_RTLS_TECH_TXT` is validated against the room's shielding type.
MRI zones Z3/Z4 prohibit all active wireless transmitters —
`RtlsCoverageValidator` raises a BLOCKING error for any non-IR anchor
within `CLN_MRI_ZONE_INT ≥ 3`.

### Phase H-30 — Waste-management IoT

**Where it lands**

```
StingTools/Data/MR_PARAMETERS.txt                     (+CLN_WASTE_CLASS_TXT, CLN_WASTE_ROUTE_TXT already in §5.1)
StingTools/Data/STING_DRAWING_TYPES.json              (+health-waste-flow-A1-1to100)
StingTools/Data/STING_AEC_FILTERS.json                (+6 waste filters)
StingTools/Data/HEALTHCARE_ALERT_ROUTING.json         (NEW — alert routing table; waste-breach alerts route to
                                                          INotificationService for cytotoxic/radioactive containers;
                                                          see §19 for full schema)
StingTools/Core/Validation/Healthcare/WasteFlowValidator.cs
                                                      (checks CLN_WASTE_CLASS_TXT populated per HBN; routing
                                                          from clinical waste points to segregation store non-crossing
                                                          with patient / clean supply flow; radioactive waste rooms
                                                          require CLN_RAD_CONTROLLED_BOOL = true)
```

---

## 12. BIM Coordination Centre — Healthcare tab (new)

`UI/BIMCoordinationCenter.cs` already hosts 13 tabs. Phase H-5 adds a
14th — `Healthcare` — with the following cards:

| Card | Source | Action buttons |
|---|---|---|
| **Pressure regime status** | `PressureRegimeValidator` + `BacnetReadback` | Run audit, open press-regime drawing, push to mobile |
| **Medical gas health** | `MgasFlowValidator` + `MgasAlarmSubscriber` | Run audit, generate schematic, start verification |
| **Essential power** | `EesBranchValidator` | Run audit, open EES riser, export single-line |
| **Water safety** | `WaterSafetyValidator` | Run audit, open flushing schedule, log sentinel |
| **Radiation** | `RadShieldValidator` + `MriZoneEngine` | Run NCRP calc, open shielding plan, MRI zoning |
| **Anti-ligature** | `AntiLigatureValidator` | Run audit, on-site checklist, sign off |
| **RDS completeness** | `RdsCompletenessValidator` | Issue all RDS, export register |
| **Adjacency** | `AdjacencyValidator` + `CleanDirtyFlowSolver` | Run audit, open adjacency matrix |
| **Commissioning gantt** | Healthcare commissioning workflow | Open gantt, mark milestone, push to schedule |
| **Structural loads** | `StructuralLoadValidator` | Run audit, open structural-loads drawing, flag missing sign-off |
| **Acoustic compliance** | `AcousticValidator` | Run audit, open acoustic-zones drawing, export NR deficiency report |
| **RTLS coverage** | `RtlsCoverageValidator` + anchor families | Open RTLS-coverage plan, flag RF dead zones, show anchor count per zone |
| **EES branch load** | `EesBranchValidator` + `EesResilienceValidator` | Show LS / CR / EQ branch load by panel, generator test log age, UPS replace date |
| **IoT device health** | `IoTStalenessValidator` + BACnet/OPC-UA read-back | Show stale BMS sensors (>30 min), TMV alerts, HEPA ΔP alarms, radiation area monitor status |
| **Endoscope status** | `EndoscopeTraceValidator` | Show scope count by room, last-reprocessed date, cycle-count warnings, AER faults |

Tab visibility is gated on `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` ≠ empty so
non-healthcare projects don't see it. Individual cards are further gated
on `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` — e.g. the RTLS and Endoscope cards
are hidden on `IMAGING-ONLY` and `DENTAL` profiles.

---

## 13. Workflow presets (Phase H-7..H-21)

New JSON workflows under `Data/`:

| File | Steps |
|---|---|
| `WORKFLOW_HealthcareCommissioning.json` | RDS issue → adjacency audit → pressure audit → MGPS audit → EES audit → water-safety audit → anti-lig audit → COBie export → handover manual |
| `WORKFLOW_MgasVerification.json` | Pre-purge → cross-connection test → particulate test → purity test → indexing test → labelling test → alarm test → write verification log |
| `WORKFLOW_PressureRegimeAudit.json` | Build room graph → resolve cascades → compare to target → produce drift report → push to mobile |
| `WORKFLOW_RdsIssue.json` | Refresh tag tokens → recompute room areas → fetch ADB cross-refs → render RDS docs → audit + sign-off |
| `WORKFLOW_HTM-04-01-Annual.json` | Flush sentinels → log temperatures → review aug-care list → check TMV3 → produce annual report |
| `WORKFLOW_AntiLigatureAudit.json` | Build psy-room set → load fittings → check `LIG_PRODUCT_RATING_TXT` → check observation LOS → produce risk register |

All consumed by the existing `WorkflowEngine` — no engine changes required.

---

## 14. Special-case room catalogue (Phase H-15..H-19)

| Room class | Code | Source(s) | Notable params |
|---|---|---|---|
| Ultra-clean OR | `OR-ULTRA` | HTM 03-01, ASHRAE 170 | ACH ≥ 300 (laminar), HEPA H14, +25 Pa, 19–24 °C, NR 35 |
| Conventional OR | `OR-CONV`  | HTM 03-01 | ACH ≥ 20, +25 Pa, NR 40 |
| Hybrid OR | `OR-HYBRID` | FGI Hybrid OR Design Basics | ≥70 m² (US 750 ft²), ceiling-zone clearances for fluoroscopy + booms + lights |
| ICU bay | `ICU` | HBN 09-02 | ACH 6, neutral, IT-CARD, BHP × 1, scrub at 20 m, nurse-call type EMG |
| AIIR | `AIIR` | CDC, HBN 04-01 | ACH 12 (new), HEPA H14, –15 Pa to anteroom, ≥–30 Pa to corridor, exhaust to outside |
| PE room | `PE-PROT` | CDC | ACH 12, HEPA H14, +12 Pa to anteroom, monitored Δp |
| Pharmacy CSP-797 | `PH-CSP-797` | USP <797> | ISO 5 PEC in ISO 7 buffer; +5 Pa to anteroom; +5 Pa anteroom to corridor |
| Pharmacy CSP-800 | `PH-CSP-800` | USP <800> | C-PEC negative; –2.5 Pa buffer to anteroom; isolated exhaust |
| MRI suite | `IMG-MRI` | HBN 06, IEC 60601-2-33 | RF Faraday cage, 5-Gauss line geometry, Z1–Z4 zoning, no ferrous within Z3 |
| LINAC vault | `IMG-LIN` | NCRP 151 | Maze geometry, primary + secondary barriers, neutron shielding ≥ 10 MV |
| Cath lab | `CATHLAB` | HBN 12 | Lead curtain at table, 2-mm Pb walls (typical), control room observation |
| Hybrid imaging IR | `IR` | FGI | Floor-mount or biplane fluoroscopy, lead control room |
| HSDU pack room | `HSDU-P` | HBN 13 | +10 Pa to corridor, separate from wash side |
| HSDU wash room | `HSDU-W` | HBN 13 | –10 Pa to corridor, separate exhaust |
| Mortuary | `MORT` | HBN 16 | Refrigeration capacity per beds × 0.5 %; 6 ACH; remote |
| Post-mortem | `POST` | HBN 16 | Down-flow ventilation; –15 Pa; HEPA on supply only when biohazard |
| Maternity LDR | `MAT-LDR` | HBN 21 | Birthing pool option, neonatal resuscitator point, soft lighting |
| NICU room | `NICU` | HBN 09-03, FGI 2026 | ≥ 14 m² single, couplet care option, 6 ACH, NR 30 |
| Dialysis station | `DIAL` | HBN 07-02 | RO-loop tap, individual chair clearance ≥ 1.5 m, RO water 24 h on |
| HBO chamber | `HBO` | NFPA 99 Ch. 14 | Oxygen-fire envelope; restricted fittings; sprinkler design |
| Behavioural seclusion | `SECL` | HBN 03-01, FGI Pt 2 | Anti-ligature throughout; observation LOS 100 %; soft furnishings |
| Behavioural bedroom | `PSY-BED` | HBN 03-01 | LR rated fittings; nurse-call EMG; min camera coverage if applicable |
| Bariatric room | `BARI` | iHFG, HBN 04-01 | SWL ≥ 270 kg; 1.5 m clearance both sides; reinforced ceiling track |
| Electrophysiology lab | `EP-LAB` | HBN 12, NEC 517 | RF-shielded room (12-lead mapping + ablation RF interference); IPS mandatory; ceiling cable grid for EP catheters; EES-LS branch throughout; lead control room |
| GI endoscopy suite | `ENDOSCOPY` | HTM 01-06, HBN 12 | Decon dirty-clean-sterile flow (separate wash, drying, storage bays); RFID reader points at each transfer; ventilation –10 Pa to adjacent corridor; HTM 01-06 AER clearance |
| Robotic surgery suite | `SURG-ROBOT` | FGI Hybrid OR Design Basics | ≥ 56 m² (600 ft²) free floor for Da Vinci / Mako envelope; ceiling-zone clearance for boom, lights, robot arm, camera column co-ordination; IPS + EES-LS; vibration isolation under slab |
| PET hot lab / dose calibration | `PET-HOT-LAB` | NCRP 151, IRR17 | 511 keV shielding (concrete-equivalent; not Pb-per-NCRP-147); negative ventilation to active waste drain; lead-glass dispensing hood; RAD_CONTROLLED_BOOL = true; CLN_PRESS_REGIME = NEG |
| Nuclear medicine scan room | `NM-SCAN` | NCRP 151, HTM 06-01, IRR17 | SPECT / gamma-camera clearances; 140 keV Tc-99m scatter shielding; patient toilet adjacent (radioactive waste drainage); no ferrous fixtures for SPECT table lateral movement |
| Brachytherapy vault | `BRACHYTHERAPY` | NCRP 151, IRR17 | Afterloading source vault; interlocked door beam-off; dose-rate area monitor mandatory (`RAD_MONITOR_DEVICE_ID_TXT`); CLN_MRI_QUENCH_OPC_TXT not applicable but CLN_RAD_CONTROLLED_BOOL = true; OPC-UA readback for source position |
| Stem-cell / BMT room | `BONE-MARROW` | HBN 09-03, CDC | HEPA H14 positive-pressure PE (distinct from AIIR — immune-compromised not infectious); ACH ≥ 12; dedicated exhaust not required; CLN_INFECT_CLASS = PE; CLN_ENV_CERT_DUE_DT required |
| Hyperacute stroke unit | `STROKE-HYPER` | NICE guidelines, NHS HASU standards | Time-critical CT adjacency (door-to-CT ≤ 4 min walk); EES-CR for all CT power; direct link to interventional suite; LVO screening room adjacent |
| Midwife-led birth centre | `BIRTH-CTR` | HBN 21, Birthplace in England | Freestanding or alongside (not OU); birthing pool option; no piped nitrous oxide (Entonox only — portable); O₂ + Entonox terminal units; maternal resuscitation call point (not cardiac-arrest type) |
| Cystoscopy room | `CYSTO` | HBN 12 | Flexible + rigid cystoscopy; fluoroscopy capable (lead lining for hybrid use); HTM 01-06 decon flow for flexible scopes; EES-LS for imaging; urology table clearances |
| Telehealth consultation room | `TELEHEAL` | NHS Digital / NHS Estates guidance | Camera mount (PTZ or fixed), CLN_TELEHEAL_BOOL = true; ICT_TELEHEAL_BW_MBPS_NR ≥ 4; ICT_TELEHEAL_LUX_NR ≥ 500 lux at face level; ICT_TELEHEAL_PLATFORM_TXT set; acoustic class SENSITIVE (no echo); no window behind clinician |

Every room class above ships with a default ADB / HBN cross-reference,
default values for `ROM_PRESS_*`, `ROM_ACH_*`, `ROM_HEPA_*`,
`ROM_INFECT_CLASS_TXT`, and a tagged equipment list.

---

## 15. Coordination with existing subsystems — file-by-file impact

| File | Phase | Change kind |
|---|---|---|
| `Core/ParamRegistry.cs` | H-1 | + ~85 string constants; +5 groups (CLN_CLINICAL, MGS_SYSTEMS, RAD_PROTECTION, CEQ_CLINICAL, LIG_BEHAVIOURAL); extensions to ELC_PWR / HVC_SYSTEMS / PLM_DRN / FLS_LIFE_SFTY / PRJ_INFORMATION / COMMISSIONING — see §5.0 crosscheck |
| `Core/TagConfig.cs` | H-1 | + load `STING_TAG_CONFIG_v5_0_HEALTH.csv`; +clinical room-class enum; +`TagConfig.HealthDisciplines`; cross-validator extension |
| `Core/Drawing/DrawingTypeRegistry.cs` | H-3 | unchanged code; corporate JSON gets +16 entries |
| `Core/Drawing/AecFilterRegistry.cs` | H-2 | unchanged code; +60 filter definitions in JSON |
| `Core/Drawing/ViewStylePackRegistry.cs` | H-2 | unchanged code; +4 packs in JSON |
| `Core/Validation/` | H-5 | + 8 validator classes |
| `Commands/Validation/` | H-5 | + 8 commands + chain entry |
| `Core/Fabrication/` | H-7 | + `Fabrication.MedicalGas` namespace (mirrors Pipe / Duct / Electrical) |
| `Core/Placement/` | H-1 | + ~25 placement rules in `STING_PLACEMENT_RULES.json` |
| `Docs/Templates/EmbeddedTemplates.cs` | H-8 | + 14 healthcare templates in extraction list |
| `Docs/Templates/TemplateRegistry.cs` | H-8 | unchanged code; manifest gets +14 entries |
| `Docs/Workflow/WorkflowRegistry.cs` | H-7..H-21 | unchanged code; + 6 workflow JSONs |
| `BIMManager/BIMManagerCommands.cs` | H-6 | + 6 healthcare COBie presets; + `Healthcare` tab data assembly |
| `BIMManager/SchedulingCommands.cs` | H-7..H-21 | + healthcare commissioning gantt template |
| `BIMManager/CoordinationCenterCommands.cs` | H-5..H-22 | + Healthcare tab tab data assembly |
| `UI/BIMCoordinationCenter.cs` | H-5 | + 14th tab |
| `Planscape.Server/src/Planscape.API/Program.cs` | H-22 | + `HealthcareHub` mapping |
| `Planscape.Server/src/Planscape.API/Controllers/` | H-22 | + `HealthcareController.cs` |
| `Planscape.Server/src/Planscape.Core/Entities/` | H-22 | + 4 entities |
| `Planscape.Server/src/Planscape.Infrastructure/Data/PlanscapeDbContext.cs` | H-22 | + 4 DbSets + indexes |
| `Planscape/app/(tabs)/_layout.tsx` | H-21 | conditional `healthcare` tab |
| `Planscape/app/healthcare/*` | H-21 | new screen suite |
| `Planscape/src/api/endpoints.ts` | H-21 | + healthcare endpoints |
| `docs/HEALTHCARE_PACK_DESIGN.md` | this | this design document |
| `docs/ROADMAP.md` | H-1 | + Healthcare Pack section linking back here |
| `docs/CHANGELOG.md` | per-phase | + entry per shipped phase |
| `StingTools/Data/MR_PARAMETERS.txt` | H-23/H-24/H-25/H-26 | + group 33 `ICT_HEALTHIOT` (41 params) + 7 structural + 2 acoustic + 5 endoscope = 55 additional params beyond H-1..H-22 |
| `StingTools/Core/Twin/IoTDeviceRegistry.cs` | H-20 (extend) | Per-room lookup of all ICT_HEALTHIOT device IDs → protocol → last-seen timestamp; used by `IoTStalenessValidator` and BCC IoT-device-health card |
| `StingTools/Data/HEALTHCARE_ALERT_ROUTING.json` | H-30 | JSON routing table: alert type → notification channel → roles; covers MGPS alarm, pressure breach, TMV temperature, radiation area monitor, quench, USP cert expiry, waste breach, IoT staleness — see §19 |
| `StingTools/Data/HEALTHCARE_PACK_PROFILES.json` | H-28 | Pack profile gate tables: `FULL`, `ACUTE`, `COMMUNITY`, `DENTAL`, `IMAGING-ONLY`; maps profile → active validators → active drawing types |
| `StingTools/Core/Validation/Healthcare/HealthcareValidatorGate.cs` | H-28 | Reads `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` and returns the filtered validator list |

Nothing in the table requires re-architecting existing subsystems —
every change is an *extension* of an existing extension point.

---

## 16. Caveats & open questions

1. **Code-base policy** — UK NHS HBN/HTM, US FGI/NFPA 99, both, or a
   project-level switch? The pack supports both via
   `PRJ_HEALTH_CODE_BASE_TXT`; the validators read this and skip rules
   that don't apply.
2. **SFG20 / ADB licensing** — paywalled. The pack carries identifiers
   and structure only; licensed prose is loaded from the project sandbox
   if available.
3. **Family library scope** — the pack ships parameter specs only,
   parity with the v4 MVP title-blocks (§"v4 MVP — Caveats" in
   `CLAUDE.md`). Real .rfa families are sourced from manufacturers.
4. **Behavioural-health depth** — FGI Pt 2 Ch. 2.5 + HBN 03-01 are
   substantial standards in their own right. Phase H-11 ships the
   parameter pack + validator; Phase H-14 expands to the full FGI Pt 2
   coverage.
5. **Imaging workload calc accuracy** — NCRP 147 / 151 calculators are
   reference implementations, not certified products. The output names
   the Qualified Expert and is explicitly marked "draft for QE
   sign-off".
6. **Live BMS read-back security** — Phase H-20 BACnet / OPC-UA bridge
   is read-only by design and runs in the plugin process, never in the
   server. Push to server is one-way.
7. **Mobile offline** — Phase H-21 commissioning checklists must work
   offline; reuses the existing `OfflineQueue` action types
   (`CREATE_ISSUE`, `UPDATE_ISSUE`, `TRANSITION_CDE`) plus 4 new types
   (`MGAS_VERIFY`, `PRESSURE_LOG`, `WATER_FLUSH`, `ANTI_LIG_AUDIT`).
8. **Internationalisation** — code labels use UK conventions by default;
   FGI/iHFG cross-references are present so a US/AU project can use the
   same parameter pack with a different code base.
9. **Verification automation vs human sign-off** — the pack never marks
   anything "verified" automatically. All sign-off paths (MGPS
   verification, RDS issue, NCRP barrier, anti-ligature) require a
   named human signature captured against `MGAS_VERIFY_BY_TXT`,
   `RAD_QE_NAME_TXT`, `signoff.clinician`, etc.
10. **Performance** — H-1..H-3 have negligible runtime cost (data-only).
    H-5 validators run on demand, not in `IUpdater`. H-20 BACnet polling
    runs in a background thread, throttled, with circuit-breaker on
    failure.
11. **SHTM / WHTM regional divergence** — Scottish HTM (SHTM) and Welsh
    HTM (WHTM) series diverge from NHS England HTM on specific topics
    (e.g. SHTM 01-06 adds a TOE probe decon step absent from the England
    edition). The `PRJ_ORG_HEALTH_HTM_REGION_TXT` flag gates
    region-specific validator rule variants in H-26 (endoscope decon) and
    H-7 (MGPS verification). Northern Ireland uses its own regional
    guidance for some areas. The iHFG flag (`PRJ_ORG_HEALTH_IHFG_BOOL`)
    enables the iHFG room-code fields and suppresses HBN-only rules for
    international projects (Africa, ME, Australasia).
12. **Structural loading is advisory only** — `StructuralLoadValidator`
    reports parameter drift (declared load < equipment weight reference)
    and missing sign-off, but never certifies structural adequacy. The
    declared values are BIM design intent; the civil/structural engineer's
    stamp (`CLN_STRUCT_SIGN_OFF_TXT` + `_DT`) is the sole authority.
    Point loads and vibration criteria for imaging equipment must be
    confirmed with manufacturer specifications — the reference weights
    in `HEALTHCARE_EQUIPMENT_WEIGHTS.csv` are typical, not contractual.
13. **RTLS RF dead zones in shielded rooms** — MRI Faraday cages and
    lead-lined radiology rooms block BLE, Wi-Fi, and UWB signals. The
    `RtlsCoverageValidator` flags any non-IR RTLS anchor in a room with
    `CLN_RF_SHIELD_BOOL = true`. Any RTLS anchor inside MRI zone ≥ Z3
    raises a BLOCKING error — active wireless transmitters within the
    5-Gauss line can quench the magnet or introduce image artefacts.
    IR-based RTLS (infrared badge readers on the door frame) is the only
    viable technology inside shielded rooms.
14. **MRI quench is the highest-severity IoT alert** — `CLN_MRI_QUENCH_OPC_TXT`
    OPC-UA readback feeds the `IoTStalenessValidator` and the BCC
    IoT-device-health card. A quench event releases several hundred litres
    of liquid helium at 4 K; `HEALTHCARE_ALERT_ROUTING.json` routes a
    quench alert to every role in the project (`broadcast: true`) with
    push priority `HIGH` and no rate-limit. This is a life-safety event
    and must not be suppressed or filtered by the notification gateway.
15. **BACnet / OPC-UA protocol coverage** — Phase H-20 BACnet/IP read-back
    targets ASHRAE 135 object types (AI, AO, BI, BO, AV, BV, MSV).
    Area alarm panels that use Modbus RTU or RS-485 proprietary protocols
    require a serial-to-Ethernet gateway (IP address stored in
    `MGS_ALARM_GATEWAY_IP_TXT`); the plugin never directly drives the
    serial bus. OPC-UA is used only for MRI quench / cryogen status where
    the magnet manufacturer exposes an OPC-UA server interface (Siemens
    MAGNETOM, Philips Ingenia, GE Signa) — not all manufacturers do.
    Fallback is a Modbus register read-back via the BMS.
16. **USP 797 / 800 recertification cycle** — ISO 14644-1 cleanroom
    classification and USP 797 / 800 environmental monitoring require
    6-monthly recertification. `CLN_ENV_CERT_DUE_DT` must be populated
    for all rooms with `CLN_ROOM_CLASS_TXT = PH-CSP-797` or `PH-CSP-800`;
    `IoTStalenessValidator` raises an escalating warning when the cert due
    date is within 30 days, and a blocking error when it is overdue.

---

## 17. Backlog candidates (post-H-30)

Items now phased (moved from backlog to roadmap):
- **Indoor positioning (RTLS / BLE / UWB)** → **Phase H-29** (anchor placement, RF dead zones, coverage validator)
- **Endoscopy decontamination (HTM 01-06)** → **Phase H-26** (RFID traceability chain, validator)
- **Medical-IT network model (HL7 / FHIR)** → Partial — `CLN_FHIR_LOCATION_ID_TXT` in §5.1 links BIM rooms to EHR Location resources; full DICOM/HL7 endpoint tagging remains below.

Remaining backlog (not yet phased):

- **Surgical robot bay** (Da Vinci, Mako) — clearance envelope, cable
  management, sterile draping route. Room class `SURG-ROBOT` is now in
  §4.5 and §14; Phase H-12 (Hybrid OR / Cath / IR) covers equipment
  collision only — a dedicated robotic-surgery clearance tool is deferred.
- **Cleanroom recovery test (ISO 14644-3)** — auto-generate the test
  procedure document and accept the pass/fail upload from particle
  counters. `HVC_HEPA_LAST_TST_DT` is the landing param; the recovery
  test procedure generator is post-H-30.
- **Forensic / secure unit** — observation, isolation, ligature, plus
  prison-grade fittings not covered by HBN 03-01 alone.
- **Veterinary hospitals** — same MGPS / shielding rules but different
  room types and biosafety.
- **Mobile / modular hospital** (NHS field-hospital pattern) — a
  templated drop of all H-1..H-3 artefacts pre-tuned to a 50-bed
  modular ward.
- **Full DICOM / HL7 endpoint tagging** — imaging modalities (PACS
  AE titles, worklist server), EHR access points, and DICOM SR
  destination routing as BIM family parameters beyond the `CLN_FHIR_LOCATION_ID_TXT`
  link already in §5.1.
- **Patient-flow simulation hook** — export room graph + door capacity
  to a discrete-event sim (e.g. Simio, AnyLogic) for ED throughput
  analysis.
- **Energy & carbon healthcare overlay** — extend
  `Model/SustainabilityEngine.cs` (BREEAM / BS EN 15978) with NHS
  Net Zero Building Standard adjustments.
- **Pneumatic-tube network validator** — extend `PathPlanner.cs`
  (Phase H-10) to validate PTS blower capacity against station count,
  tube diameter, and travel distance per the manufacturer's design guide.
- **Decontamination room / dirty-utility network** — linked soiled-utility
  room to sluice-room topology validator for HTM 07-01 compliance.
- **Smart-bed integration** — Linet / Stryker smart-bed device IDs on
  room families; nurse-call integration via `ICT_NC_RTLS_DISPATCH_BOOL`
  extension for auto-escalation when bed-exit sensor fires.

---

## 19. IoT Architecture

This section consolidates the IoT design decisions across §5.1, §5.14,
Phase H-20, H-26, H-27, H-29, H-30, and the caveats in §16. It is a
companion to the code layer — no code yet, design only.

### 19.1 IoT domains and protocols

| Domain | Technology | Protocol | Direction | Phase |
|---|---|---|---|---|
| BMS (pressure / ACH / temperature / RH / CO₂ / fan status) | BACnet/IP (ASHRAE 135) | UDP/IP — BACnet APDU; object per quantity | Read-only poll | H-20 |
| MRI quench / cryogen level | OPC-UA (IEC 62541) | TCP/IP — subscription model | Read-only subscribe | H-20 |
| MGPS area alarm panels | BACnet, Modbus RTU, RS-485 proprietary, or RS-232 | See `MGS_ALARM_PROTOCOL_TXT`; proprietary panels need serial-to-IP gateway | Read-only subscribe / poll | H-20 |
| Smart TMV temperature sensors | BACnet, Modbus TCP, or proprietary | See `PLM_TMV_PROTOCOL_TXT` | Read-only poll | H-20 |
| RTLS beacons / anchors | BLE (Bluetooth 5.1 AoA), UWB (IEEE 802.15.4z), Wi-Fi RSSI, IR | Vendor cloud or on-prem RTLS middleware API | Read-only (position events) | H-29 |
| Nurse call nodes | IP-SIP, 900 MHz DECT, analogue pair | See `ICT_NC_PROTOCOL_TXT` | Read-only event | H-20 / H-29 |
| Infant anti-abduction RFID | 125 kHz or 13.56 MHz RFID | Proprietary IP reader gateway | Read-only event | H-29 |
| Energy sub-meters | Modbus TCP, BACnet, DLMS/COSEM | See `ELC_METER_PROTOCOL_TXT` | Read-only periodic | H-27 |
| Generator / ATS test logs | NFPA 110 log book (manual upload) | JSON POST to Planscape Server | Write (plugin pushes) | H-27 |
| Endoscope RFID readers | 13.56 MHz ISO 14443 / ISO 15693 | Proprietary IP reader → REST gateway | Read-only event | H-26 |
| Radiation area monitors | BACnet, HTTP/REST API, proprietary | See `RAD_MONITOR_PROTOCOL_TXT` | Read-only continuous | H-25 |
| HEPA filter ΔP sensors | BACnet AI object | `HVC_HEPA_DP_SENSOR_REF_TXT` object ref | Read-only poll | H-20 |
| USP cleanroom environmental monitors | BACnet or proprietary | Particle count, temp, RH | Read-only periodic | H-25 |
| Waste IoT / smart bins | HTTP/REST or proprietary | Bin-full status, cytotoxic / radioactive breach | Read-only event | H-30 |
| Telehealth AV | REST / WebSocket (platform-specific) | Zoom / Epic / Cerner / Webex APIs | n/a (platform manages) | H-28 |

### 19.2 IoTDeviceRegistry design

`StingTools/Core/Twin/IoTDeviceRegistry.cs` (Phase H-20 extension) is a
per-project registry that maps BIM element IDs to IoT device endpoints.
It is populated at runtime from the param values of ICT_HEALTHIOT group
elements, not from a separate config file.

```
IoTDeviceRegistry
  ├── GetDevicesForRoom(roomId) → IEnumerable<IoTDeviceRef>
  ├── GetDeviceByBimId(elementId) → IoTDeviceRef
  ├── GetStaleDevices(threshold=TimeSpan.FromMinutes(30)) → IEnumerable<IoTDeviceRef>
  └── IoTDeviceRef { BimElementId, DeviceId, Protocol, LastSeen, AlertBand }
```

The registry is built lazily on first access and invalidated when the
document is saved (in case param values were edited). It is consumed by:
- `IoTStalenessValidator` (staleness check)
- `BacnetReadback` (building the poll list from BACnet object refs)
- BCC Healthcare tab IoT-device-health card
- Mobile commissioning app (live read-back endpoints)

### 19.3 HEALTHCARE_ALERT_ROUTING.json schema

Each alert type maps to: notification channel (push / SignalR / email),
target roles, priority, and rate-limit. The file lives at
`StingTools/Data/HEALTHCARE_ALERT_ROUTING.json` and is loaded by the
existing `INotificationService` infrastructure.

```json
{
  "alerts": [
    {
      "id": "MGAS_ALARM",
      "description": "MGPS area alarm panel fault or gas failure",
      "channel": ["push", "signalr"],
      "roles": ["AE-MGAS", "Estates", "FM"],
      "priority": "HIGH",
      "rateLimit": null
    },
    {
      "id": "PRESSURE_BREACH",
      "description": "Room pressure regime outside design band",
      "channel": ["push", "signalr"],
      "roles": ["AE-VENT", "Infection-Control", "Estates"],
      "priority": "MEDIUM",
      "rateLimit": "PT5M"
    },
    {
      "id": "MRI_QUENCH",
      "description": "MRI quench in progress — cryogen release",
      "channel": ["push", "signalr"],
      "roles": ["ALL"],
      "broadcast": true,
      "priority": "HIGH",
      "rateLimit": null
    },
    {
      "id": "TMV_TEMP_OUT_OF_BAND",
      "description": "Smart TMV outlet temperature outside 40–45 °C band",
      "channel": ["push"],
      "roles": ["AE-WATER", "Estates"],
      "priority": "MEDIUM",
      "rateLimit": "PT15M"
    },
    {
      "id": "RAD_DOSE_ALERT",
      "description": "Radiation area monitor exceeds dose-rate alert threshold",
      "channel": ["push", "signalr"],
      "roles": ["QE-RAD", "Radiation-Protection", "Estates"],
      "priority": "HIGH",
      "rateLimit": null
    },
    {
      "id": "USP_CERT_EXPIRY",
      "description": "Cleanroom environmental certification overdue",
      "channel": ["push"],
      "roles": ["Pharmacy-Manager", "Estates"],
      "priority": "LOW",
      "rateLimit": "P1D"
    },
    {
      "id": "BMS_SENSOR_STALE",
      "description": "BMS sensor last-seen > 30 minutes — possible sensor fault",
      "channel": ["signalr"],
      "roles": ["Estates", "FM"],
      "priority": "LOW",
      "rateLimit": "PT30M"
    },
    {
      "id": "WASTE_CYTOTOXIC_BREACH",
      "description": "Cytotoxic waste container fault or breach detected",
      "channel": ["push", "signalr"],
      "roles": ["Pharmacy-Manager", "Infection-Control", "Estates"],
      "priority": "HIGH",
      "rateLimit": null
    },
    {
      "id": "WASTE_RADIOACTIVE_BREACH",
      "description": "Radioactive waste container fault or breach detected",
      "channel": ["push", "signalr"],
      "roles": ["QE-RAD", "Radiation-Protection", "Estates"],
      "priority": "HIGH",
      "rateLimit": null
    },
    {
      "id": "EES_GENERATOR_TEST_OVERDUE",
      "description": "NFPA 110 weekly generator exercise test not logged within 35 days",
      "channel": ["push"],
      "roles": ["AE-ELEC", "Estates"],
      "priority": "MEDIUM",
      "rateLimit": "P1D"
    }
  ]
}
```

### 19.4 BACnet object reference convention

Each `CLN_BMS_*_OBJ_TXT` parameter stores a BACnet OBJECT IDENTIFIER
string in the form `<ObjectType>:<InstanceNumber>` as registered in the
BMS controller. The plugin parses this at runtime to construct the
BACnet ReadProperty APDU.

| Parameter | BACnet Object Type | Unit |
|---|---|---|
| `CLN_BMS_PRESS_OBJ_TXT` | Analog Input (AI) | Pa |
| `CLN_BMS_ACH_OBJ_TXT` | Analog Input (AI) | m³/h (plugin converts to ACH using room volume) |
| `CLN_BMS_TEMP_OBJ_TXT` | Analog Input (AI) | °C |
| `CLN_BMS_RH_OBJ_TXT` | Analog Input (AI) | % RH |
| `CLN_BMS_CO2_OBJ_TXT` | Analog Input (AI) | ppm |
| `CLN_BMS_FAN_STATUS_OBJ_TXT` | Binary Input (BI) | 0 = off, 1 = running |

The BACnet read-back runs in a background thread (30-second poll
interval by default, configurable). On BACnet COV notification support
the poll falls back to subscription (BACnet COV-Subscribe). The plugin
never writes BACnet objects — read-only enforcement is in `BacnetReadback.cs`.

### 19.5 OPC-UA node ref convention

`CLN_MRI_QUENCH_OPC_TXT` and `CLN_MRI_CRYO_LEVEL_OPC_TXT` store OPC-UA
Node IDs in the standard string form `ns=<namespace>;s=<identifier>` or
`ns=<namespace>;i=<numeric-id>`. The `OpcUaReadback` client connects to
the MRI manufacturer's OPC-UA server endpoint (configured at
`<project>/_BIM_COORD/healthcare/iot_config.json`) and subscribes to
these nodes. On quench detection (quench node transitions to `true`)
the client raises an immediate `MRI_QUENCH` alert via
`INotificationService` (no rate limit, no suppression).

### 19.6 Push notification routing

The `HEALTHCARE_ALERT_ROUTING.json` file is consumed by
`Planscape.Server/src/Planscape.Infrastructure/Services/NotificationService.cs`
via a healthcare-specific routing layer. At Phase H-20, the existing
`INotificationService` is extended to accept an `alertId` parameter that
looks up the routing table before dispatching FCM / APNs pushes. The
Planscape mobile app's `notificationTapRouter.ts` is extended with
healthcare alert deep-links (e.g. `MRI_QUENCH` → `healthcare/pressure-live`,
`MGAS_ALARM` → `healthcare/mgas-checklist`).

---

## 18. References

### Standards & guidance
- NHS England — [Health Technical Memoranda index](https://www.england.nhs.uk/estates/health-technical-memoranda/)
- NHS England — [HTM 04-01 Safe water in healthcare premises](https://www.england.nhs.uk/publication/safe-water-in-healthcare-premises-htm-04-01/)
- NHS England — [HTM 05-02 Firecode 2015](https://www.england.nhs.uk/wp-content/uploads/2021/05/HTM_05-02_2015.pdf)
- NHS England — [HBN 11-01 Primary and community care](https://www.england.nhs.uk/wp-content/uploads/2021/05/HBN_11-01_Final.pdf)
- NHS England — [Activity Database (ADB) FoI page](https://www.england.nhs.uk/publication/foi-activity-database/)
- NHS Scotland — [BIM Guidance v0.2](https://www.nss.nhs.scot/media/2240/guidance-building-information-modelling-v02.pdf)
- Munday & Cramer — [HTM 00 explained](https://mcessex.co.uk/2021/11/01/healthcare-buildings-best-practice-htm-00/)
- Buckinghamshire Healthcare NHS — [HBN library index](https://buckshealthcare.nhs.libguides.com/estates/hbn-by-number)
- FGI — [Editions index](https://fgiguidelines.org/codes/editions/)
- FGI — [Hybrid OR Design Basics PDF](https://www.fgiguidelines.org/wp-content/uploads/2019/01/FGI-Hybrid-OR-Design-Basics.pdf)
- FGI — [Behavioural Health Design Guide 7.2](https://www.fgiguidelines.org/wp-content/uploads/2017/03/DesignGuideBH_7.2_1703.pdf)
- Compass Cryogenics — [FGI 2026: from guideline to code](https://www.compasscryo.com/articles/fgi-facility-code-2026-healthcare-design/)
- ASHE — [A Preview of the Draft 2026 FGI](https://ashe.digitellinc.com/p/s/a-preview-of-the-draft-2026-fgi-guidelines-for-design-and-construction-documents-7465)
- ANSI Blog — [ANSI/ASHRAE/ASHE 170-2025](https://blog.ansi.org/ansi/ansi-ashrae-ashe-170-2025-health-care-ventilation/)
- NFPA — [Essential Electrical System overview](https://www.nfpa.org/news-blogs-and-articles/blogs/2019/09/17/dissecting-the-essential-electrical-system-ees-in-healthcare-facilities)
- Compass Cryogenics — [Medical Gas Verification (NFPA 99)](https://www.compasscryo.com/articles/medical-gas-system-verification-checklist/)
- NCRP — [Report 147 — Structural Shielding for Medical X-Ray](https://ncrponline.org/shop/reports/report-no-147-structural-shielding-design-for-medical-x-ray-imaging-facilities-2004/)
- iHFG — [Part D Isolation rooms (TAHPI)](https://healthfacilityguidelines.com/ViewPDF/ViewIndexPDF/iHFG_part_d_isolation_rooms)
- iHFG — [Part B Renal dialysis unit](https://www.healthfacilityguidelines.com/ViewPDF/ViewIndexPDF/iHFG_part_b_renal_dialysis_unit)
- iHFG — [Part B Inpatient maternity unit](https://www.healthfacilityguidelines.com/ViewPDF/ViewIndexPDF/iHFG_part_b_inpatient_maternity_unit)

### Subsystem & research papers
- WBDG — [Hospital building type](https://www.wbdg.org/building-types/health-care-facilities/hospital)
- Journal of Hospital Infection — [OR ventilation: recovery, cleanliness, ACH](https://www.journalofhospitalinfection.com/article/S0195-6701(21)00459-X/fulltext)
- PMC — [Isolation anterooms — important components](https://pmc.ncbi.nlm.nih.gov/articles/PMC7135637/)
- MDPI Buildings — [AGV transport in hospitals via BIM/IFC](https://www.mdpi.com/2075-5309/16/5/900)
- HFM Magazine — [Behavioral design strategies](https://www.aha.org/system/files/2019-01/2018-oct-hfm-behavioral-design.pdf)
- TWord (Tamlite) — [Anti-ligature lighting in UK MH](https://tword.tamlite.co.uk/anti-ligature-lighting-requirements-in-uk-mental-healthcare-facilities/)
- WPI — [Horizontal Evacuation in Healthcare Facilities](https://www.wpi.edu/academics/departments/fire-protection-engineering/research/research-areas/horizontal-evacuation-healthcare-facilities)
- DBTH — [Fire Strategy Concepts (NHS Trust example)](https://www.dbth.nhs.uk/wp-content/uploads/2023/12/CORP-HSFS-14-v.1-Protocol-6-Fire-Strategy-Concepts.pdf)
- Cleanetics — [USP 797 / 800 cleanroom design](https://www.cleanetics.com/industries/usp797800.html)
- Dekker — [Pharmacy flow with USP 797 / 800](https://www.dekkerdesign.org/insights/clean-room-design-achieving-pharmacy-flow-with-usp-797-and-usp-800-standards/)
- BIMobject — [Bed-head unit families](https://www.bimobject.com/en/electraplan/product/bed_head_units)
- Static Systems Group — [Bedhead trunking catalogue](https://www.staticsystems.co.uk/wp-content/uploads/2023/10/SSG-Trunking-Catalogue-1023-v15.pdf)
- SFG20 — [Healthcare maintenance schedules](https://www.sfg20.co.uk/maintenance-schedules/healthcare)
- buildingSMART forums — [COBie / IFC mapping discussion](https://forums.buildingsmart.org/t/cobie-and-ifc-mapping/1796)
- MDPI Sustainability — [Digital twins in mega-facilities](https://www.mdpi.com/2071-1050/17/5/1826)
- PMC — [Hospital intelligent twins for healthcare](https://pmc.ncbi.nlm.nih.gov/articles/PMC9203951/)
