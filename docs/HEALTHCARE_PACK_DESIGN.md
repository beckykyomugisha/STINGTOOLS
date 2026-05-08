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

> Last refreshed 2026-05-08. Branch: `claude/research-hospital-design-0Uxbi`.

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
`MILK-KIT`, `BIRTH-POOL`, `HBO`.

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

(19 params in CLN — down from 32 in the first draft.)

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

(12 params — uses `PRJ_ORG_*` Phase 112 convention.)

### 5.11 Net-new parameter count

| Sub-pack | New params | Reuse existing |
|---|---|---|
| 5.1 CLN_CLINICAL (room-bound clinical) | **19** | `ASS_ROOM_*`, `BLE_ROOM_FINISH_*`, `ASS_DESIGN_OCCUPANCY_INT`, `BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR`, `RGL_ACCESS_REFUGE_AREA_TXT`, `MEP_DELTA_PRESS_PA`, `PER_ACOUSTICS_*`, `PER_ENVIRONMENTAL_TEMP_DESIGN_C`, `PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT`, `LTG_DESIGN_ILLUMINANCE_LUX`, `LTG_CLR_TEMP_K`, `BLE_DOOR_ACOUSTIC_*` |
| 5.2 MGS_SYSTEMS | **13** | `ASS_TIEIN_*`, `ASS_TEST_PRESSURE_BAR`, `HVC_PIPE_PRESSURE_KPA` |
| 5.3 ELC_PWR (EES extensions) | **7** | `ELC_EMERG_COVERED_BOOL`, `ELC_PANEL_SCHEDULE_REF_TXT`, `ELC_CIRCUIT_REF_TXT`, `ELC_FAT_CERT_REF_TXT` |
| 5.4 PLM_DRN (water-safety extensions) | **7** | `PLM_HOTWTR_TEMP_C` |
| 5.5 HVC_SYSTEMS (HEPA / ACH-outside) | **4** | `HVC_AIR_CHANGES_PER_HR` |
| 5.6 RAD_PROTECTION | **8** | — |
| 5.7 CEQ_CLINICAL | **7** + 2 calibration in MNT | `ASS_MANUFACTURER_TXT`, `ASS_MODEL_NR_TXT`, `ASS_SERIAL_NR_TXT`, `ASS_BARCODE_TXT`, `ASS_MAINTENANCE_FREQUENCY_MONTHS`, `ASS_MAINT_INTERVAL_TXT`, `ASS_MAINTENANCE_SCHEDULE_TXT`, `ASS_WARRANTY_*`, `MNT_*`, `CST_DELIVERY_LEAD_TIME_DAYS`, `CST_SUP_PROCUREMENT_LEAD_TIME_DAYS`, `ASS_OMNICLASS_TXT`, `ASS_UNICLASS_2015_TXT`, `ASS_UNIFORMAT_TXT`, `ASS_KEYNOTE_TXT` |
| 5.8 LIG_BEHAVIOURAL | **5** | — |
| 5.9 FLS_LIFE_SFTY (fire compartment) | **1** | `PER_FIRE_RATING_*`, `PROP_FIRE_RATING`, `RGL_FIRE_RATING_TXT`, `FLS_EVACUATION_TIME_MIN`, `FLS_EXIT_TRAVEL_DIST_M`, `FLS_SFTY_OCCUPANT_LOAD_NR` |
| 5.10 PRJ_ORG_HEALTH | **12** | All other `PRJ_ORG_*` |
| **Total net-new** | **~85** | (~30 existing parameters reused) |

The first draft cited "~140" net-new params — the corrected count is
roughly **40 % smaller**, with the difference reabsorbed into existing
parameters.

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

The existing **Connectivity / Fill / Spec / Termination / Slope**
validators are reused — the healthcare validators chain on top, never
replace them.

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

H-1..H-3 are pure-data prerequisites; everything else depends only on
those three. H-7..H-19 are independent special-case packs that can be
shipped in any order after the common foundation.

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

Tab visibility is gated on `PRJ_HEALTH_FACILITY_TYPE_TXT` ≠ empty so
non-healthcare projects don't see it.

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

---

## 17. Backlog candidates (post-H-22)

Things worth tracking but not yet phased:

- **Surgical robot bay** (Da Vinci, Mako) — clearance envelope, cable
  management, sterile draping route.
- **Endoscopy decontamination** (HTM 01-06) — separate validator family.
- **Cleanroom recovery test (ISO 14644-3)** — auto-generate the test
  procedure document and accept the pass/fail upload.
- **Forensic / secure unit** — observation, isolation, ligature, plus
  prison-grade fittings.
- **Veterinary hospitals** — same MGPS / shielding rules but different
  room types and biosafety.
- **Mobile / modular hospital** (NHS field-hospital pattern) — a
  templated drop of all H-1..H-3 artefacts pre-tuned to a 50-bed
  modular ward.
- **Cleanroom particle-count import** — IFC-tracked particle counter
  feeds into `ROM_HEPA_LAST_TST_DT` and recovery-test result.
- **Medical-IT network model** — HL7 / DICOM / FHIR endpoint tagging on
  imaging modalities and EHR access points.
- **Indoor positioning (RTLS / BLE / UWB)** — reader placement
  optimisation in the `Core/Placement/` engine.
- **Patient-flow simulation hook** — export room graph + door capacity
  to a discrete-event sim (e.g. Simio, AnyLogic) for ED throughput
  analysis.
- **Energy & carbon healthcare overlay** — extend
  `Model/SustainabilityEngine.cs` (BREEAM / BS EN 15978) with NHS
  Net Zero Building Standard adjustments.

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
