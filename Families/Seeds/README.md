# STING Seed Families — Author's Manual-Finishing Guide

This folder hosts the polished `.rfa` files that live in the **corporate baseline**. The auto-generated drafts go to `<project>/_BIM_COORD/Families/Seeds/`; your hand-finished versions belong here and are never touched by the generator.

`BuildSeedFamiliesCommand` reads the 16 JSON specs in `StingTools/Data/Seeds/` and gives you:

| What the generator provides | What you still add manually |
|---|---|
| Correct `.rft` template for the Revit category | Readable 2D plan symbol at 1:50 / 1:100 |
| All shared parameters with stable GUIDs | Section / elevation symbology |
| Every `STING_*` / `ASS_*` / discipline parameter already bound | 3D geometry that reads as the right object class |
| Minimal 2D outline from the JSON `geometry` block | Connector face-anchoring and domain classification |
| 3D bounding box from `solid3D` | Named type variants matching the swap registry |
| MEP connectors at JSON-declared positions | Type parameter values per variant |
| `STING_SEED_FAMILY_TXT` stamped on every type | `Mark = PEN_CONTROL_NUMBER_TXT` formula where relevant |

---

## What's new — Phase add-type-variants

### 1. Rebuild mode picker

Every time you click **Build Seed Families** a three-option dialog now appears before any files are written:

| Option | Effect |
|---|---|
| **Safe (missing only)** *(default)* | Only generates seeds that don't already have an `.rfa` on disk. Your polished files are never touched. |
| **Rebuild unfinalized** | Regenerates any `.rfa` that does NOT have a `.sting-finalized` sidecar alongside it. Files you've marked as finished are skipped. |
| **Rebuild all** | Overwrites every `.rfa`, including finalized ones. Requires a second confirmation dialog. Families with `"protectExisting": true` in their JSON are skipped even in this mode. |

The default is always **Safe (missing only)** — pressing Enter or clicking the first button never destroys polished work.

### 2. Finalization sidecar — marking a seed as finished

Once you are happy with a seed, create a small JSON sidecar file alongside the `.rfa`:

```
STING_SEED_ElectricalFixture.rfa          ← your polished family
STING_SEED_ElectricalFixture.sting-finalized  ← protection marker
```

**To create the sidecar in one step**, run this in the Revit API console or a PowerShell terminal from the Seeds folder:

```powershell
# PowerShell — run from the folder containing the .rfa
$rfa = "STING_SEED_ElectricalFixture.rfa"
@{ finalized = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"); note = "Polished by <initials>" } |
  ConvertTo-Json | Set-Content ("$rfa" -replace "\.rfa$", ".sting-finalized")
```

Or, inside Revit, call `SymbolLibraryCreator.MarkFinalized(rfaPath, "Polished by BK 2026-05")` — the API method writes an identical JSON file.

**To remove the sidecar** (allow future regeneration):

```powershell
Remove-Item "STING_SEED_ElectricalFixture.sting-finalized"
```

The sidecar has no effect on Revit loading — it is only read by `BuildSeedFamiliesCommand`.

### 3. `protectExisting` flag in the JSON spec

Every seed JSON now has `"protectExisting": true`. This is a hard-coded guard in the spec itself: even if a developer runs **Rebuild all**, the generator refuses to overwrite an existing `.rfa` for that seed. It is the backstop for seeds that encode important company-standard geometry. To override it you must remove or set the flag to `false` in the JSON source.

### 4. Importing a pre-built manufacturer family as a seed

You can supply a finished `.rfa` (e.g., a downloaded Revit content file from a manufacturer) and have the generator **augment** it with STING parameters without touching its geometry:

```json
"sourceFamilyPath": "Families/Seeds/Source/Grundfos_MAGNA3.rfa"
```

When `sourceFamilyPath` is set, `BuildSeedFamiliesCommand`:

1. Opens the source `.rfa` in Family Editor (hidden)
2. Injects every shared parameter from the JSON spec idempotently (skips params that already exist by GUID)
3. Adds or updates type variants whose names match the JSON's `typeVariants` array
4. Saves the result to the output folder under the seed's standard filename

The source file is never modified — the output is a copy. Paths are resolved relative to the JSON spec's location, then relative to the project root, then as absolute paths.

**Use this when:** a manufacturer supplies Revit families that are already geometrically accurate and you want to avoid duplicating work. The swap registry for that seed will still point to the same manufacturer family — the seed IS the manufacturer family, pre-enriched with STING parameters.

### 5. Auto-registered swap candidates

Each JSON spec now contains a `swapCandidates` array. After every build run, `BuildSeedFamiliesCommand` merges those entries into `STING_FAMILY_SWAP_REGISTRY.json` (stored in `<project>/_BIM_COORD/`). The merge is **additive** — existing entries you have manually curated are never removed; only new seed-declared candidates are appended or updated.

```json
"swapCandidates": [
  {
    "label": "Waldner / Kewlab Fume Hood",
    "familyPath": "",          // empty = user browses at swap time
    "variantPattern": "FUME_HOOD*",   // regex matched against the loaded family's types
    "priority": 1,
    "autoLoad": false          // true = attempt to load from familyPath automatically
  }
]
```

To pre-populate a `familyPath`, set it to a path relative to the project's Families folder or an absolute network path. Setting `autoLoad: true` and a valid path lets **Swap to Manufacturer** run silently in batch mode without a file-picker dialog.

---

## Standard per-seed workflow

1. **Run Build Seed Families** — choose *Safe (missing only)* unless you need to regenerate.
2. **Open** the resulting `.rfa` in Family Editor — right-click the family in Project Browser → *Edit Family*.
3. **Verify parameters** — open *Family Types* (Manage tab). Every parameter below should be present. If any are missing, run `LoadSharedParamsCommand` first and retry.
4. **Polish 2D** — follow the per-seed symbol guidance below.
5. **Polish 3D** — replace the bounding box with object-class-appropriate geometry.
6. **Wire connectors** — drag each connector onto the correct face reference plane; set domain and system classification.
7. **Create type variants** — Family Types → *Duplicate* for each variant; set parameters per the tables below.
8. **Save and reload** — *File → Save*; when prompted *Load into Project and Close*, click yes.
9. **Mark as finalized** — create the `.sting-finalized` sidecar (see §2 above).
10. **Copy polished `.rfa` here** — `Families/Seeds/` is the corporate baseline. The project-scoped copy under `_BIM_COORD/` is the runtime fallback.
11. **Test end-to-end** — place, tag, schedule, swap. See the [End-to-end test](#after-authoring-end-to-end-test) section.

---

## Common subcategory pattern

In Family Editor: Manage → Object Styles → Sub-objects → New subcategory named **STING_SEED**.

Recommended line weights:

| View | Weight |
|---|---|
| Plan / Section (projection) | 3 |
| Primary symbol elements | 4 |
| Cut | 5 |

One VG override line in every view template then controls all seed families simultaneously. In your project's view templates, add a subcategory override for `Specialty Equipment : STING_SEED` → Projection colour = RGB(0, 128, 192), Cut colour = RGB(0, 0, 0).

---

## Parameter injection — confirmation table

All parameters listed below are injected automatically by `BuildSeedFamiliesCommand` from the JSON spec. The table confirms what is expected in every polished `.rfa`. Mark column shows the family-level formula you must add manually.

### Universal parameters (all 16 seeds)

| Parameter | Storage | Instance? | Default | Notes |
|---|---|---|---|---|
| `STING_SEED_FAMILY_TXT` | Text | Yes | Seed ID string | Read-only stamp — do not edit |
| `STING_DESIGN_REF_TXT` | Text | Yes | *(empty)* | Populated by SwapToManufacturer |
| `STING_SWAP_HISTORY_TXT` | Text | Yes | *(empty)* | Populated by SwapToManufacturer |
| `ASS_TAG_1` | Text | Yes | *(empty)* | Full 8-segment ISO 19650 tag |
| `ASS_DISCIPLINE_COD_TXT` | Text | Yes | Seed-specific | DISC token |
| `ASS_PRODCT_COD_TXT` | Text | Yes | Seed-specific | PROD token — varies per type variant |

### Penetration seeds only (`SpecialityEquipment`, `FireDamper`, `AcousticSeal`)

| Parameter | Storage | Instance? | Default |
|---|---|---|---|
| `PEN_OD_MM` | Text | Yes | `0` or `32` |
| `PEN_HOST_REF_TXT` | Text | Yes | *(empty)* |
| `PEN_HOST_TYPE_TXT` | Text | Yes | *(empty)* |
| `PEN_MEMBER_ID_TXT` | Text | Yes | *(empty)* |
| `PEN_INSTALL_STATUS_TXT` | Text | Yes | `DRAFT` |
| `PEN_INSTALLER_TXT` | Text | Yes | *(empty)* |
| `PEN_INSTALL_DATE` | Text | Yes | *(empty)* |
| `PEN_CONTROL_NUMBER_TXT` | Text | Yes | *(empty)* |
| `PEN_PFV_UUID_TXT` | Text | Yes | *(empty)* |
| `PEN_CERTIFICATION_TXT` | Text | **No (type)** | Standard ref |
| **`Mark` formula** | — | — | `= PEN_CONTROL_NUMBER_TXT` | **Add manually in Family Editor** |

### Discipline-specific parameters

| Seed | Parameter | Storage | Instance? |
|---|---|---|---|
| LightingFixture | `LTG_DIMMABLE_BOOL` | Text | No (type) |
| LightingFixture | `LTG_IP_RATING_TXT` | Text | No (type) |
| ElectricalFixture | `ELE_FIX_GANG_COUNT_INT` | Integer | No (type) |
| ElectricalFixture | `ELE_FIX_WAY_CONFIG_TXT` | Text | No (type) |
| ElectricalEquipment | `ELC_EQP_TYPE_TXT` | Text | Yes |
| ElectricalEquipment | `ELC_KVA_RATING` | Text | No (type) |
| FireAlarmDevice | `FLS_DEV_TYPE_TXT` | Text | Yes |
| FireAlarmDevice | `FLS_DEV_LOOP_ADDR_TXT` | Text | Yes |
| FireAlarmDevice | `FLS_DEV_CERT_TXT` | Text | No (type) |
| PlumbingFixture | `PLM_FIX_ACCESSIBLE_BOOL` | Text | No (type) |
| PlumbingFixture | `PLM_FIX_TYPE_TXT` | Text | Yes |
| PlumbingEquipment | `PLM_EQP_TYPE_TXT` | Text | Yes |
| PlumbingEquipment | `PLM_EQP_CAPACITY_LITRES` | Text | No (type) |
| PlumbingEquipment | `PLM_EQP_HEAT_OUTPUT_KW` | Text | No (type) |
| PlumbingEquipment | `PLM_EQP_PRESSURE_BAR` | Text | No (type) |
| PlumbingEquipment | `PLM_EQP_INLET_DN_MM` | Text | No (type) |
| PlumbingEquipment | `PLM_EQP_OUTLET_DN_MM` | Text | No (type) |
| MechanicalEquipment | `HVC_EQP_TYPE_TXT` | Text | Yes |
| MechanicalEquipment | `HVC_EQP_DUTY_KW` | Text | No (type) |
| MechanicalEquipment | `HVC_EQP_AIRFLOW_LS` | Text | No (type) |
| MechanicalEquipment | `HVC_EQP_COP_FACTOR` | Text | No (type) |
| AirTerminal | `HVC_AIR_TERM_TYPE_TXT` | Text | Yes |
| AirTerminal | `HVC_AIR_FLOW_LS` | Text | No (type) |
| AirTerminal | `HVC_FACE_VEL_MS` | Text | No (type) |
| Sprinkler | `FLS_SPR_TYPE_TXT` | Text | Yes |
| Sprinkler | `FLS_SPR_RESPONSE_TXT` | Text | No (type) |
| Sprinkler | `FLS_SPR_K_FACTOR` | Text | No (type) |
| Sprinkler | `FLS_SPR_TEMP_C` | Text | No (type) |
| Sprinkler | `FLS_SPR_HAZARD_TXT` | Text | Yes |
| Sprinkler | `FLS_SPR_COVER_M2` | Text | No (type) |
| CommunicationDevice | `COM_DEV_TYPE_TXT` | Text | Yes |
| CommunicationDevice | `COM_DEV_SPEED_GBPS` | Text | No (type) |
| JunctionBox | `ELC_JB_TYPE_TXT` | Text | Yes |
| JunctionBox | `ELC_JB_SIZE_MM` | Text | Yes |
| JunctionBox | `ELC_JB_IP_RATING_TXT` | Text | No (type) |
| JunctionBox | `ELC_JB_FIRE_RATING_TXT` | Text | Yes |
| JunctionBox | `ELC_JB_ATEX_ZONE_TXT` | Text | No (type) |
| JunctionBox | `ELC_JB_AUTO_PLACED_BOOL` | Text | Yes |
| MedGasOutlet | `MGS_TU_TYPE_TXT` | Text | Yes |
| MedGasOutlet | `MGS_GASES_TXT` | Text | Yes |
| MedGasOutlet | `MGS_SOCKET_STD_TXT` | Text | No (type) |
| MedGasOutlet | `MGS_OPERATING_KPA` | Text | No (type) |
| MedGasOutlet | `MGS_CERT_TXT` | Text | No (type) |
| MedGasOutlet | `MGS_HOSPITAL_AREA_TXT` | Text | Yes |
| MedGasOutlet | `MGS_AVSU_ZONE_TXT` | Text | Yes |
| LabFixture | `LAB_FIX_TYPE_TXT` | Text | Yes |
| LabFixture | `LAB_FIX_FACE_VEL_MS` | Text | No (type) |
| LabFixture | `LAB_FIX_AIR_FLOW_LS` | Text | No (type) |
| LabFixture | `LAB_FIX_DELUGE_LMIN` | Text | No (type) |
| LabFixture | `LAB_FIX_HAZARD_CLASS_TXT` | Text | Yes |
| LabFixture | `LAB_FIX_BACKFLOW_CAT_TXT` | Text | Yes |
| SpecialityEquipment | `PEN_FIRE_RATING_TXT` | Text | No (type) |
| SpecialityEquipment | `PEN_SEALANT_TYPE_TXT` | Text | Yes |
| FireDamper | `PEN_FIRE_RATING_TXT` | Text | No (type) |
| FireDamper | `FD_ACTUATION_TXT` | Text | No (type) |
| FireDamper | `FD_TRIGGER_TEMP_C` | Text | No (type) |
| FireDamper | `FD_BSEN15650_CLASS_TXT` | Text | No (type) |
| FireDamper | `FD_RESET_AFTER_TEST_TXT` | Text | Yes |
| AcousticSeal | `ACS_RW_TARGET_DB` | Text | No (type) |
| AcousticSeal | `ACS_SEAL_TYPE_TXT` | Text | No (type) |
| AcousticSeal | `ACS_DEPTH_MM` | Text | No (type) |
| AcousticSeal | `ACS_CERT_TXT` | Text | No (type) |

---

## STING_SEED_LightingFixture

**Hosting:** Ceiling-based · **Template:** `Metric Lighting Fixture ceiling based.rft` · **Symbol size at 1:100:** 6 mm  
**15 type variants**

### 2D plan symbol
- 600×600 mm rectangle (auto-generated outline). Relocate to family origin if needed.
- Diagonal `X` corner-to-corner — auto-generated.
- Add a small filled circle (3 mm) at centre — emergency luminaires fill this solid red via type parameter or graphic override.
- Subcategory: `STING_SEED`.

### 3D representation
- Replace the auto 100 mm box with a flush-fit recessed panel: 595×595×25 mm face plate + 100×100×90 mm centre housing boss.
- Material: a generic `STING_LumPlate` material (white). Manufacturer swap replaces this.

### Type variants — set these parameters in *Family Types*

| Type name | `ASS_PRODCT_COD_TXT` | `LTG_DIMMABLE_BOOL` | `LTG_IP_RATING_TXT` | Notes |
|---|---|---|---|---|
| `RECESSED_LED_600x600` | `LTG-R` | `1` | `IP20` | Default |
| `RECESSED_LED_600x600_DIMMABLE` | `LTG-RD` | `1` | `IP20` | Add DALI symbol |
| `DOWNLIGHT_75MM` | `LTG-DL` | `0` | `IP20` | Change 2D to Ø75 circle |
| `DOWNLIGHT_100MM` | `LTG-DL` | `0` | `IP44` | |
| `DOWNLIGHT_DIMMABLE_75MM` | `LTG-DLD` | `1` | `IP44` | |
| `LINEAR_LED_1200` | `LTG-L` | `0` | `IP20` | Change 2D rect to 1200×100 mm |
| `LINEAR_LED_1500` | `LTG-L` | `0` | `IP20` | 1500×100 mm |
| `LINEAR_LED_DIMMABLE_1200` | `LTG-LD` | `1` | `IP20` | |
| `PENDANT_ROUND` | `LTG-P` | `0` | `IP20` | Change 2D to Ø300 circle; raise 3D 200 mm |
| `WALL_BULKHEAD` | `LTG-W` | `0` | `IP44` | Face-based — add to wall |
| `WALL_EXTERIOR` | `LTG-WX` | `0` | `IP65` | |
| `FLOOD_LIGHT` | `LTG-FL` | `0` | `IP65` | Ø200 circle + 4 radial lines |
| `TRACK_SPOTLIGHT` | `LTG-T` | `1` | `IP20` | 1200×50 mm track rect |
| `EMERGENCY_MAINTAINED` | `LTG-EM` | `0` | `IP20` | Fill centre circle solid red |
| `EMERGENCY_NON_MAINTAINED` | `LTG-ENM` | `0` | `IP20` | Centre circle hatched |

### Connector note
Lighting fixtures do not require MEP connectors in the STING scheme — electrical circuit topology is managed via `ELC_CIRCUIT_GROUP_TXT` parameter, not connector topology. Leave connectors as-is (none declared in the JSON).

---

## STING_SEED_ElectricalFixture

**Hosting:** Face-based · **Template:** `Metric Electrical Fixture face based.rft` · **Symbol size at 1:100:** 4 mm  
**26 type variants — full gang/way combinatorial matrix**

### Gang and way — what they mean

**Gang** = the number of switch or socket modules on a single back-plate (1, 2, 3, or 4).  
**Way** = the switching circuit topology:

| Way code | Meaning | Where used |
|---|---|---|
| `1W` | 1-way (single location) | Most lighting circuits |
| `2W` | 2-way (two-location switching) | Corridors, staircases |
| `INT` | Intermediate (three or more locations) | Long corridors — fitted between two 2-way switches |
| `DIM` | Dimmer | Living / hospitality spaces, DALI rooms |
| `DP` | Double-pole switch (isolator) | Shower, immersion heater, kitchen |

### 2D plan symbol guidance

- **Sockets:** Rectangle (plate outline) + short vertical strokes inside for each socket. 1G = 1 stroke, 2G = 2 strokes, etc.
- **Switches:** Rectangle + horizontal slits (one per gang). Add a diagonal line for 2-way, double diagonal for intermediate.
- **Data outlets:** Rectangle + small `J45` or `D` label inside.
- **Floor box:** Square 200×200 mm, centred on origin.
- All mounting-height labels: add an annotation label referencing `ELE_FIX_MOUNT_HEIGHT_MM` to the front elevation view so the symbol self-reports height on elevations and sections.

### 3D representation
- Standard wall plate: 150×35×85 mm for 2G, 75×35×85 mm for 1G, 200×35×85 mm for 3G/4G.
- Projecting 5 mm proud of the face plane.
- Floor box: 200×200×80 mm, flush with floor.

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `ELE_FIX_GANG_COUNT_INT` | `ELE_FIX_WAY_CONFIG_TXT` | Notes |
|---|---|---|---|---|
| **Sockets** | | | | |
| `SOCKET_1G_13A` | `SKT-1G` | 1 | — | Default |
| `SOCKET_2G_13A` | `SKT-2G` | 2 | — | |
| `SOCKET_3G_13A` | `SKT-3G` | 3 | — | |
| `SOCKET_4G_13A` | `SKT-4G` | 4 | — | |
| `SOCKET_1G_USB` | `SKT-USB` | 1 | — | Add USB icon |
| `SOCKET_2G_USB` | `SKT-USB2` | 2 | — | |
| `SOCKET_FCU` | `SKT-FCU` | 1 | — | Fused + small circle |
| `SOCKET_SHAVER_1G` | `SKT-SHV` | 1 | — | |
| `SOCKET_WEATHERPROOF_2G` | `SKT-WP` | 2 | — | IP66 box outline |
| `SOCKET_COMMANDO_16A` | `SKT-CMD` | 1 | — | Round 3-pin profile |
| `SOCKET_COMMANDO_32A` | `SKT-CMD32` | 1 | — | |
| **1-gang switches** | | | | |
| `SWITCH_1G_1W` | `SW-1G1W` | 1 | `1W` | |
| `SWITCH_1G_2W` | `SW-1G2W` | 1 | `2W` | |
| `SWITCH_1G_INT` | `SW-1GIN` | 1 | `INT` | |
| `SWITCH_1G_DIM` | `SW-1GD` | 1 | `DIM` | |
| `SWITCH_1G_DP` | `SW-1GDP` | 1 | `DP` | Double-pole |
| **2-gang switches** | | | | |
| `SWITCH_2G_1W` | `SW-2G1W` | 2 | `1W` | |
| `SWITCH_2G_2W` | `SW-2G2W` | 2 | `2W` | |
| `SWITCH_2G_INT` | `SW-2GIN` | 2 | `INT` | |
| `SWITCH_2G_DIM` | `SW-2GD` | 2 | `DIM` | |
| `SWITCH_2G_DP` | `SW-2GDP` | 2 | `DP` | |
| **3-gang switches** | | | | |
| `SWITCH_3G_1W` | `SW-3G1W` | 3 | `1W` | 3-slit plan symbol |
| `SWITCH_3G_2W` | `SW-3G2W` | 3 | `2W` | |
| `SWITCH_3G_DIM` | `SW-3GD` | 3 | `DIM` | |
| **4-gang switches** | | | | |
| `SWITCH_4G_1W` | `SW-4G1W` | 4 | `1W` | 4-slit plan symbol |
| `SWITCH_4G_2W` | `SW-4G2W` | 4 | `2W` | |
| `SWITCH_4G_DIM` | `SW-4GD` | 4 | `DIM` | |
| **Data / specialist** | | | | |
| `DATA_OUTLET_1G_RJ45` | `DAT-1G` | 1 | — | |
| `DATA_OUTLET_2G_RJ45` | `DAT-2G` | 2 | — | |
| `DATA_OUTLET_1G_SFP` | `DAT-SFP` | 1 | — | Fibre |
| `HDMI_AV_PLATE_1G` | `AV-HDMI` | 1 | — | |
| `FLOOR_BOX_4G` | `FLR-4G` | 4 | — | 200×200 mm flush |
| `FLOOR_BOX_POWER_DATA` | `FLR-PD` | 4 | — | Power + data combined |
| `ISOLATOR_DP_20A` | `ISO-20A` | 1 | `DP` | Shower/appliance |
| `ISOLATOR_DP_45A` | `ISO-45A` | 1 | `DP` | Cooker/range |
| `TV_AERIAL_OUTLET` | `TV-AER` | 1 | — | |
| `TELEPHONE_OUTLET` | `TEL` | 1 | — | |

### How `ELE_FIX_GANG_COUNT_INT` is used downstream
This integer parameter feeds quantity take-off schedules — a 2G socket counts as 2 gangs for materials budgets. Do not leave it at zero; set it to the integer matching the gang count for every type.

---

## STING_SEED_ElectricalEquipment

**Hosting:** Standalone · **Template:** `Metric Electrical Fixture.rft` · **Symbol size at 1:100:** 8 mm  
**16 type variants**

### 2D plan symbol
- Auto-generated rectangle with horizontal divider reads as a wall-mounted DB. Keep for `DISTRIBUTION_BOARD_1PH`.
- Add an `H=xxx mm` annotation label in the front elevation referencing the height parameter.
- Subcategory: `STING_SEED`.

### 3D representation
- Distribution board: 400×200×600 mm (W×D×H) panel. Add a 40×10×15 mm door handle on the right edge.
- Main switchboard: 800×400×1800 mm free-standing. Add 3 mm ventilation slots along the top.
- UPS: low-profile 600×600×200 mm (tower) or 483×600×3U (rack).

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `ELC_EQP_TYPE_TXT` | `ELC_KVA_RATING` | 3D size (W×D×H mm) |
|---|---|---|---|---|
| `CONSUMER_UNIT_1PH` | `CU` | `CONSUMER_UNIT` | `6` | 300×90×250 |
| `CONSUMER_UNIT_3PH` | `CU-3P` | `CONSUMER_UNIT` | `18` | 300×90×250 |
| `DISTRIBUTION_BOARD_1PH` | `DB` | `DISTRIBUTION_BOARD` | `30` | 400×200×600 ← default |
| `DISTRIBUTION_BOARD_3PH` | `DB-3P` | `DISTRIBUTION_BOARD` | `63` | 400×200×800 |
| `DISTRIBUTION_BOARD_MCCB` | `DB-M` | `DISTRIBUTION_BOARD` | `100` | 600×250×1200 |
| `MAIN_SWITCHBOARD` | `MSB` | `MAIN_SWITCHBOARD` | `250` | 800×400×1800 |
| `MOTOR_CONTROL_CENTRE` | `MCC` | `MOTOR_CONTROL_CENTRE` | `0` | 1200×400×2100 |
| `SOFT_STARTER` | `SST` | `SOFT_STARTER` | `0` | 400×300×600 |
| `VSD_DRIVE` | `VSD` | `VARIABLE_SPEED_DRIVE` | `0` | 300×200×500 |
| `UPS_STATIC_10KVA` | `UPS` | `UPS_STATIC` | `10` | 600×600×1200 |
| `UPS_STATIC_RACK` | `UPS-R` | `UPS_STATIC` | `6` | 483×600×89 (2U) |
| `GENERATOR_AUTO_TRANSFER` | `ATS` | `AUTO_TRANSFER_SWITCH` | `0` | 600×400×800 |
| `RCD_PROTECTION_UNIT` | `RCD` | `RCD_UNIT` | `0` | 150×90×200 |
| `ENERGY_METER` | `EM` | `ENERGY_METER` | `0` | 100×60×150 |
| `POWER_FACTOR_CORRECTION` | `PFC` | `PFC_UNIT` | `0` | 400×400×600 |
| `DRY_TYPE_TRANSFORMER` | `TXF` | `TRANSFORMER` | `100` | 600×400×800 |

### Connector wiring (critical)
The JSON declares top + bottom connectors at z = ±0.5 of the bounding box. In Family Editor after resizing the 3D solid:

1. Select each connector (in 3D view, use Tab to cycle to the small connector symbol).
2. Drag onto the correct face reference plane — top face for supply-in, bottom face for distribution-out.
3. Set **Domain** = Electrical. Set **System** = Power (supply-in) / Power - Balanced (distribution-out, 3-phase balanced load) / Power - Unbalanced (1-phase).
4. For MSB / MCC: set supply connector at the bottom (cable entry from below), distribution connectors at top of each section.

---

## STING_SEED_FireAlarmDevice

**Hosting:** Face-based · **Template:** `Metric Fire Alarm Device.rft` · **Symbol size at 1:100:** 5 mm  
**14 type variants**

### 2D plan symbol guidance

| Symbol style | Type | How to draw |
|---|---|---|
| Circle + internal X | Smoke detector | Auto-generated — keep |
| Circle + internal H | Heat detector | Replace X with a `H` text note |
| Circle + triangle | Multi-sensor | Add small triangle inside circle |
| Circle + wavy lines | Gas / CO | Add 2–3 wavy arcs inside |
| Circle + `CO` text | CO only | Text annotation inside |
| Circle + filled sector | Flame detector | Fill one quarter of circle |
| Square (hatched) | Call point (MCP) | Replace circle with 100×100 hatched square |
| Circle + 3 radials | Sounder / beacon | Three lines radiating outward |
| Square + small square | Interface module | Nested square, smaller |
| Circle + `B` | Beam detector | Add `B` text; pair with second circle at far end |
| Circle + `H`+`S` | Heat + smoke combined | Both letters inside |

### 3D representation
- Detector body: 110×110×50 mm shallow cylinder (use *Revolve* for circular profile, 55 mm radius, 50 mm tall).
- Call point: 100×100×30 mm flat box.
- Sounder/VAD: 220×100×100 mm horn shape (Sweep along path).

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `FLS_DEV_TYPE_TXT` | `FLS_DEV_CERT_TXT` |
|---|---|---|---|
| `SMOKE_OPTICAL` | `SD-O` | `SMOKE_OPTICAL` | `BS EN 54-7` |
| `SMOKE_IONISATION` | `SD-I` | `SMOKE_IONISATION` | `BS EN 54-7` |
| `HEAT_FIXED_TEMP` | `HD` | `HEAT_FIXED_TEMP` | `BS EN 54-5` |
| `MULTI_SENSOR_SMOKE_HEAT` | `MSD` | `MULTI_SENSOR` | `BS EN 54-29` |
| `MULTI_SENSOR_SMOKE_CO` | `MSD-CO` | `MULTI_SENSOR` | `BS EN 54-29` |
| `GAS_DETECTOR_LPG` | `GD-L` | `GAS_LPG` | `BS EN 60079` |
| `GAS_DETECTOR_NATURAL` | `GD-N` | `GAS_NATURAL` | `BS EN 60079` |
| `CO_DETECTOR` | `COD` | `CO` | `BS EN 50291` |
| `FLAME_DETECTOR` | `FLD` | `FLAME` | `BS EN 54-10` |
| `BEAM_DETECTOR` | `BD` | `BEAM` | `BS EN 54-12` |
| `CALL_POINT_MCP` | `MCP` | `CALL_POINT` | `BS EN 54-11` |
| `SOUNDER_BEACON_VAD` | `VAD` | `SOUNDER_BEACON` | `BS EN 54-3` |
| `INPUT_MODULE` | `MOD-IN` | `INPUT_MODULE` | `BS EN 54-17` |
| `OUTPUT_MODULE` | `MOD-OUT` | `OUTPUT_MODULE` | `BS EN 54-18` |

### How `FLS_DEV_LOOP_ADDR_TXT` is used
This instance parameter holds the addressable device loop address (e.g., `L1/D047`). It is entered at placement time by the engineer or auto-populated by the fire alarm loop-schedule import command. Do not pre-populate it in the type; leave the family default blank.

---

## STING_SEED_PlumbingFixture

**Hosting:** Face-based · **Template:** `Metric Plumbing Fixture face based.rft` · **Symbol size at 1:100:** 6 mm  
**16 type variants**

### Connectors (auto-generated — verify positions)
- DCW (cold water supply) at top-left — drag to wall-face reference plane, Domain = Piping, System = Domestic Cold Water
- DHW (hot water supply) at top-right — Domain = Piping, System = Domestic Hot Water
- Sanitary outlet at bottom-centre — Domain = Piping, System = Sanitary, Direction = Out, Size = 50 mm

WC and urinal variants don't use DHW — leave the connector as-placed; SwapToManufacturer will drop unused connectors when loading a real product family.

### 2D plan symbol — WC / sanitary convention
Draw the fixture outline as it appears in BS 8298 / NBS product drawings. Minimum readable outlines at 1:100:

| Fixture | Min 2D outline at 1:100 |
|---|---|
| WC (close-coupled) | 350×600 mm D-shape |
| Basin | 400×250 mm oval |
| Urinal | 350×300 mm rect |
| Bath | 700×1600 mm rect |
| Shower tray | 900×900 mm square |
| Sink (single bowl) | 400×500 mm rect |
| Sink (double bowl) | 800×500 mm rect |
| Drinking fountain | 250×350 mm half-circle |

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `PLM_FIX_TYPE_TXT` | `PLM_FIX_ACCESSIBLE_BOOL` | 3D size (W×D×H) |
|---|---|---|---|---|
| `WC_CLOSE_COUPLED` | `WC` | `WC_CLOSE_COUPLED` | `0` | 350×650×780 |
| `WC_BACK_TO_WALL` | `WC-BTW` | `WC_BACK_TO_WALL` | `0` | 350×550×400 |
| `WC_WALL_HUNG` | `WC-WH` | `WC_WALL_HUNG` | `0` | 350×540×400 |
| `WC_ACCESSIBLE_HTM64` | `WC-A` | `WC_ACCESSIBLE` | `1` | 480×700×450 |
| `WC_ACCESSIBLE_RIMLESS` | `WC-AR` | `WC_ACCESSIBLE_RIMLESS` | `1` | 480×700×450 |
| `BASIN_PEDESTAL` | `BAS` | `BASIN_PEDESTAL` | `0` | 560×460×850 |
| `BASIN_WALL_HUNG` | `BAS-WH` | `BASIN_WALL_HUNG` | `0` | 560×460×800 |
| `BASIN_ACCESSIBLE` | `BAS-A` | `BASIN_ACCESSIBLE` | `1` | 560×460×800 |
| `BATH_STANDARD` | `BTH` | `BATH_STANDARD` | `0` | 700×1700×550 |
| `BATH_SHOWER_OVER` | `BTH-S` | `BATH_SHOWER_OVER` | `0` | 700×1700×550 |
| `SHOWER_TRAY_900` | `SHW` | `SHOWER_TRAY` | `0` | 900×900×150 |
| `URINAL_WALL_HUNG` | `URI` | `URINAL` | `0` | 350×350×600 |
| `SINK_SINGLE_INSET` | `SNK` | `SINK_SINGLE` | `0` | 400×500×200 |
| `SINK_DOUBLE_INSET` | `SNK-D` | `SINK_DOUBLE` | `0` | 800×500×200 |
| `SINK_CLEANERS` | `SNK-CL` | `SINK_CLEANERS` | `0` | 400×400×300 |
| `DRINKING_FOUNTAIN` | `DFN` | `DRINKING_FOUNTAIN` | `0` | 250×350×900 |

---

## STING_SEED_PlumbingEquipment

**Hosting:** Standalone · **Template:** `Metric Mechanical Equipment.rft` · **Symbol size at 1:100:** 8 mm  
**14 type variants**

### 2D plan symbol
- Auto-generated rectangle with horizontal centre divider. Represents a vertical vessel (calorifier, cylinder). Keep for all vessel types.
- For pumps: change to 400×200 mm circle/oval. Add flow direction arrow.
- Subcategory: `STING_SEED`.

### Connectors (auto-generated — verify positions)
- DCW supply (cold in) — left face, Piping / Domestic Cold Water, In, 22 mm
- DHW flow (hot out) — right face, Piping / Domestic Hot Water, Out, 22 mm
- Hydronic supply (heating in) — front face at mid-height, Piping / Hydronic Supply, In, 28 mm
- Hydronic return (heating out) — front face lower, Piping / Hydronic Return, Out, 28 mm

Pumps and manifolds only need 2 connectors — remove the unused ones by selecting them in Family Editor and deleting.

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `PLM_EQP_TYPE_TXT` | `PLM_EQP_CAPACITY_LITRES` | `PLM_EQP_HEAT_OUTPUT_KW` | `PLM_EQP_PRESSURE_BAR` |
|---|---|---|---|---|---|
| `CALORIFIER` | `CAL` | `CALORIFIER` | `300` | `9` | `6` |
| `CALORIFIER_DIRECT` | `CAL-D` | `CALORIFIER_DIRECT` | `200` | `9` | `6` |
| `DHW_CYLINDER_UNVENTED` | `CYL` | `DHW_CYLINDER_UNVENTED` | `200` | `3` | `3` |
| `DHW_CYLINDER_VENTED` | `CYL-V` | `DHW_CYLINDER_VENTED` | `140` | `3` | `1` |
| `WATER_HEATER_ELECTRIC` | `WHE` | `WATER_HEATER_ELECTRIC` | `100` | `3` | `3` |
| `PUMP_INLINE` | `PMP` | `PUMP_INLINE` | — | — | — |
| `PUMP_TWIN_SET` | `PMP-T` | `PUMP_TWIN_SET` | — | — | — |
| `BOOSTER_SET` | `BST` | `BOOSTER_SET` | — | — | `8` |
| `PRESSURISATION_SET` | `PST` | `PRESSURISATION_SET` | — | — | `3` |
| `EXPANSION_VESSEL` | `EXV` | `EXPANSION_VESSEL` | `24` | — | `3` |
| `MANIFOLD` | `MAN` | `MANIFOLD` | — | — | — |
| `WATER_SOFTENER` | `WSO` | `WATER_SOFTENER` | — | — | — |
| `RO_FILTER_UNIT` | `ROF` | `RO_FILTER` | — | — | — |
| `GREASE_TRAP` | `GRT` | `GREASE_TRAP` | `100` | — | — |

---

## STING_SEED_MechanicalEquipment

**Hosting:** Standalone · **Template:** `Metric Mechanical Equipment.rft` · **Symbol size at 1:100:** 12 mm  
**15 type variants**

### 2D plan symbol
- Auto-generated rectangle + vertical centre divider reads as AHU. Keep for all AHU types.
- Add connector-side labels: `S` (supply) on left, `R` (return/extract) on right.
- Chiller/boiler: replace centre line with two dashed circles (evaporator + condenser).
- Pump: small circle 300 mm, add two 30°-offset triangles for impeller.

### Connectors (auto-generated — domain classification needed)

| Connector position | Equipment types | Domain | System |
|---|---|---|---|
| Left face | AHU, FCU | HVAC | Supply Air |
| Right face | AHU, FCU | HVAC | Return Air |
| Top face | Chiller, HP, VRF | Piping | Condenser Water / Refrigerant |
| Bottom face | Chiller, HP | Piping | Hydronic Supply / Return |
| Both sides | Boiler, pump | Piping | Hydronic Supply / Return |

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `HVC_EQP_TYPE_TXT` | `HVC_EQP_DUTY_KW` | `HVC_EQP_AIRFLOW_LS` | `HVC_EQP_COP_FACTOR` |
|---|---|---|---|---|---|
| `AHU_SUPPLY_EXTRACT` | `AHU` | `AHU` | `0` | `2000` | — |
| `AHU_HEAT_RECOVERY` | `AHU-HR` | `AHU_HEAT_RECOVERY` | `0` | `2000` | `0.75` |
| `FCU_CEILING_4PIPE` | `FCU` | `FCU_4PIPE` | `4` | `250` | — |
| `CHILLER_AIR_COOLED` | `CH-A` | `CHILLER_AIR_COOLED` | `200` | — | `3.2` |
| `CHILLER_WATER_COOLED` | `CH-W` | `CHILLER_WATER_COOLED` | `500` | — | `5.5` |
| `HEAT_PUMP_AIR_SOURCE` | `HP-A` | `HEAT_PUMP_AIR` | `16` | — | `3.5` |
| `HEAT_PUMP_GROUND_SOURCE` | `HP-G` | `HEAT_PUMP_GROUND` | `16` | — | `4.2` |
| `VRF_OUTDOOR_UNIT` | `VRF-O` | `VRF_OUTDOOR` | `22` | — | `3.8` |
| `VRF_INDOOR_CASSETTE` | `VRF-I` | `VRF_INDOOR` | `3` | `150` | — |
| `BOILER_CONDENSING_GAS` | `BLR` | `BOILER_GAS` | `50` | — | — |
| `BOILER_ELECTRIC` | `BLR-E` | `BOILER_ELECTRIC` | `12` | — | — |
| `PUMP_INLINE` | `PMP` | `PUMP_INLINE` | `0.75` | — | — |
| `PUMP_END_SUCTION` | `PMP-E` | `PUMP_END_SUCTION` | `2.2` | — | — |
| `EXPANSION_VESSEL_HVAC` | `EXV-H` | `EXPANSION_VESSEL` | — | — | — |
| `PRESSURISATION_UNIT` | `PSU` | `PRESSURISATION_UNIT` | `0.3` | — | — |

---

## STING_SEED_AirTerminal

**Hosting:** Ceiling-based · **Template:** `Metric Air Terminal.rft` · **Symbol size at 1:100:** 6 mm  
**14 type variants**

### 2D plan symbol guidance

| Symbol style | Type | Outline |
|---|---|---|
| Square + X + cross | Square diffuser | 595×595 mm — auto-generated, keep |
| Circle + X | Round diffuser | Ø200–Ø400 mm circle |
| Long narrow rect | Slot diffuser | 1200×50 mm to 2400×50 mm |
| Rect + horizontal lines | Supply/extract grille | Lines 3 mm apart fill the rectangle |
| Rect + angled slats | Louvre | Lines at 45° |
| Square + P | Plenum box | Solid fill 595×595 |
| Circle + triangle | VAV terminal | Arrow pointing into circle |

### 3D representation
- Square diffuser: 595×595×60 mm (matches ceiling tile module). Auto box is correct.
- Slot diffuser: 1200×100×60 mm.
- Round diffuser: 300 mm diameter, 60 mm deep.
- Grille: 600×300×30 mm flat plate with horizontal slots.
- VAV terminal: 300×300×200 mm box with circular inlet at top.

### Connector (auto-generated)
Domain = HVAC; set system to Supply Air (diffusers, grilles in supply mode) or Exhaust Air (extract grilles). Size in mm matching the JSON spec.

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `HVC_AIR_TERM_TYPE_TXT` | `HVC_AIR_FLOW_LS` | `HVC_FACE_VEL_MS` |
|---|---|---|---|---|
| `SUPPLY_DIFFUSER_SQ_595` | `DIFF-SQ` | `SUPPLY_DIFFUSER_SQUARE` | `200` | `0.25` |
| `SUPPLY_DIFFUSER_RD_200` | `DIFF-RD` | `SUPPLY_DIFFUSER_ROUND` | `80` | `0.30` |
| `SUPPLY_DIFFUSER_RD_300` | `DIFF-RD` | `SUPPLY_DIFFUSER_ROUND` | `150` | `0.28` |
| `SUPPLY_DIFFUSER_RD_400` | `DIFF-RD` | `SUPPLY_DIFFUSER_ROUND` | `250` | `0.26` |
| `SLOT_DIFFUSER_1200` | `DIFF-SL` | `SLOT_DIFFUSER` | `100` | `0.50` |
| `SUPPLY_GRILLE` | `GRLL-S` | `SUPPLY_GRILLE` | `300` | `2.50` |
| `EXTRACT_GRILLE_600x300` | `GRLL-E` | `EXTRACT_GRILLE` | `200` | `2.00` |
| `EXTRACT_GRILLE_600x600` | `GRLL-E` | `EXTRACT_GRILLE` | `400` | `2.00` |
| `TRANSFER_GRILLE` | `GRLL-T` | `TRANSFER_GRILLE` | `0` | — |
| `LOUVRE_INTAKE` | `LVR-I` | `LOUVRE_INTAKE` | `0` | `1.50` |
| `VAV_TERMINAL_RECT` | `VAV-R` | `VAV_TERMINAL` | `500` | — |
| `VAV_TERMINAL_ROUND` | `VAV-C` | `VAV_TERMINAL` | `300` | — |
| `ACTIVE_CHILLED_BEAM` | `ACB` | `CHILLED_BEAM_ACTIVE` | `100` | — |
| `PASSIVE_CHILLED_BEAM` | `PCB` | `CHILLED_BEAM_PASSIVE` | `0` | — |

---

## STING_SEED_Sprinkler

**Hosting:** Ceiling-based · **Template:** `Metric Sprinkler.rft` · **Symbol size at 1:100:** 4 mm  
**10 type variants**

### 2D plan symbol
- Auto-generated circle + cross tickmarks. Keep as-is — this is the universal sprinkler symbol.
- For concealed heads, draw circle with a filled solid ring (hides deflector behind cover plate).

### 3D representation
- Pendant: 50×50×80 mm cylindrical body, 25 mm deflector disc at bottom.
- Upright: flip deflector to top.
- Sidewall: rotate body 90°, deflector faces outward from wall.
- Concealed: shorten body to 30 mm, add 90 mm cover plate flush with ceiling.

### Connector
Domain = Piping, System = Fire Protection Wet, Round, 25 mm (default), Direction = In. Locate at the top of the body, facing upward (+Z).

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `FLS_SPR_TYPE_TXT` | `FLS_SPR_RESPONSE_TXT` | `FLS_SPR_K_FACTOR` | `FLS_SPR_TEMP_C` | `FLS_SPR_COVER_M2` |
|---|---|---|---|---|---|---|
| `PENDANT` | `SPR` | `PENDANT` | `STANDARD` | `80` | `68` | `12` |
| `PENDANT_FAST_RESPONSE` | `SPR-FR` | `PENDANT` | `FAST_RESPONSE` | `80` | `68` | `12` |
| `PENDANT_RESIDENTIAL` | `SPR-R` | `PENDANT_RESIDENTIAL` | `FAST_RESPONSE` | `42` | `57` | `9` |
| `PENDANT_EXTENDED_COVERAGE` | `SPR-EC` | `PENDANT_EC` | `STANDARD` | `115` | `68` | `20` |
| `UPRIGHT` | `SPR` | `UPRIGHT` | `STANDARD` | `80` | `68` | `12` |
| `ATTIC_HEAD_UPRIGHT` | `SPR-AT` | `ATTIC_HEAD` | `STANDARD` | `80` | `93` | `14` |
| `SIDEWALL` | `SPR` | `SIDEWALL` | `STANDARD` | `80` | `68` | `12` |
| `CONCEALED` | `SPR` | `CONCEALED` | `STANDARD` | `80` | `68` | `12` |
| `STANDARD_RESPONSE_93C` | `SPR-HT` | `PENDANT` | `STANDARD` | `80` | `93` | `12` |
| `OPEN_HEAD_DELUGE` | `DLG` | `OPEN_HEAD_DELUGE` | `STANDARD` | `115` | `0` | — |

---

## STING_SEED_CommunicationDevice

**Hosting:** Face-based · **Template:** `Metric Data Device.rft` or `Metric Generic Model face based.rft` · **Symbol size at 1:100:** 4 mm  
**12 type variants**

### 2D plan symbol

| Symbol | Type | How to draw |
|---|---|---|
| Disc + 3 arcs | Wi-Fi AP | Auto-generated — keep |
| Rectangle + 2 small holes | Data outlet (RJ45) | 100×35 mm plate + 2 circular recesses |
| Long rectangle + dots | Patch panel | 483×44 mm (1U rack) + grid of port dots |
| Dome circle | CCTV dome | Ø120 mm circle with a small eye symbol inside |
| Bullet shape | CCTV fixed | Cylinder pointing outward |
| Small rectangle | Intercom panel | 100×150 mm + speaker grille lines |
| Small keypad rect | Access reader | 80×130 mm + small LED dot |
| Wide panel | PABX / IP phone | 200×100 mm + handset icon |
| Circle + H lines | NFC / RFID reader | Circle + 3 horizontal lines |

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `COM_DEV_TYPE_TXT` | `COM_DEV_SPEED_GBPS` | Notes |
|---|---|---|---|---|
| `WIFI_AP_CEILING` | `WAP-C` | `WIFI_AP` | `1` | Default — ceiling disc |
| `WIFI_AP_WALL` | `WAP-W` | `WIFI_AP` | `1` | Wall-mounted |
| `DATA_OUTLET_1G_RJ45` | `DTO-1G` | `DATA_OUTLET` | `1` | Single port |
| `DATA_OUTLET_2G_RJ45` | `DTO-2G` | `DATA_OUTLET` | `1` | Dual port |
| `DATA_OUTLET_SFP_FIBRE` | `DTO-F` | `DATA_OUTLET_FIBRE` | `10` | Fibre port |
| `PATCH_PANEL_1U_24PORT` | `PP-24` | `PATCH_PANEL` | `1` | 483×44 mm |
| `NETWORK_SWITCH_1U` | `NSW-1U` | `NETWORK_SWITCH` | `1` | Rack-mounted |
| `CCTV_DOME_CEILING` | `CCTV-D` | `CCTV_DOME` | — | Ø120 mm dome |
| `CCTV_FIXED_BULLET` | `CCTV-B` | `CCTV_FIXED` | — | Bullet camera |
| `INTERCOM_DOOR_PANEL` | `IC-DP` | `INTERCOM` | — | Door entry |
| `ACCESS_READER_CARD` | `ACR` | `ACCESS_READER` | — | Proximity card |
| `PABX_RACK_UNIT` | `PABX` | `PABX` | — | |

---

## STING_SEED_JunctionBox

**Hosting:** Standalone · **Template:** `Metric Generic Model.rft` · **Symbol size at 1:100:** 4 mm  
**10 type variants** — auto-placed by conduit-run engine at every break exceeding BS 7671 §522.8.5 limit (3 bends per segment)

### 2D plan symbol
- Auto-generated square + cross + horizontal centre line reads as a tee junction box. Keep.

### 3D representation
- Standard box: 100×100×60 mm. This is already correct in the auto-generated solid.
- Vary width and depth per type; keep height constant at 60–100 mm.

### Connectors (auto-generated electrical connectors — verify facing)
Four electrical connectors at ±X and ±Y faces. In Family Editor, confirm each connector:
- Domain = Electrical
- System = Conduit (or Cable Tray for busbar types)
- Connectors are non-connectable stubs — they signal topological presence to `AutoConduitDrop`, not physical pipe connectivity.

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `ELC_JB_TYPE_TXT` | `ELC_JB_SIZE_MM` | `ELC_JB_IP_RATING_TXT` | `ELC_JB_FIRE_RATING_TXT` |
|---|---|---|---|---|---|
| `PULL_BOX` | `JB` | `PULL_BOX` | `100x100x60` | `IP54` | — |
| `DRAW_IN_BOX` | `JB` | `DRAW_IN_BOX` | `200x200x80` | `IP54` | — |
| `ADAPTABLE_BOX` | `JB` | `ADAPTABLE_BOX` | `300x300x100` | `IP54` | — |
| `TEE_BOX` | `JB` | `TEE_BOX` | `150x150x80` | `IP54` | — |
| `WEATHERPROOF_JB_IP65` | `JB-WP` | `WEATHERPROOF_JB` | `160x135x70` | `IP65` | — |
| `FIRE_RATED_JB_30MIN` | `JB-FR` | `FIRE_RATED_JB` | `150x150x60` | `IP54` | `FR30` |
| `FIRE_RATED_JB_60MIN` | `JB-FR` | `FIRE_RATED_JB` | `200x200x80` | `IP54` | `FR60` |
| `HAZARDOUS_AREA_JB_EXDE` | `JB-EX` | `HAZARDOUS_JB_ExDE` | `200x200x100` | `IP66` | — |
| `EARTH_BONDING_BOX` | `JB-EB` | `EARTH_BONDING_BOX` | `100x100x50` | `IP31` | — |
| `CABLE_GLAND_PLATE` | `JB-GP` | `CABLE_GLAND_PLATE` | `200x100x10` | `IP54` | — |

### How `ELC_JB_AUTO_PLACED_BOOL` works
When the conduit-routing engine auto-inserts a junction box, it sets this parameter to `1` and writes the upstream/downstream element IDs into `ELC_JB_UPSTREAM_REF_TXT` and `ELC_JB_DOWNSTREAM_REF_TXT`. Boxes placed manually have it set to `0`. This distinction drives the *Auto-Placed JBs* schedule view — useful for reviewing generated topologies before issuing schematics.

---

## STING_SEED_MedGasOutlet

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 5 mm  
**13 type variants** — HTM 02-01 terminal units and zone control assemblies

### 2D plan symbol
- Auto-generated rectangle + 4 small circles (one per gas service) reads as a multi-service bedhead unit. Keep for `BEDHEAD_UNIT_WARD`.
- For single-service terminal units: use a rectangle + 1 circle offset left.
- For AVSU box: rectangle with a bold border and diagonal stripe.

### Gas-specific colour coding (apply via Subcategory overrides in view templates)
HTM 02-01 colour code: O₂ = white, N₂O = blue, Medical Air = black+white, VAC = yellow, CO₂ = grey, N₂ = black.
Add a subcategory per gas and assign the colour — this lets engineers rapidly audit gas coverage in views.

### 3D representation
- Terminal unit: 400×80×200 mm flat wall panel.
- AVSU box: 500×200×400 mm surface-mounted box.
- Bedhead unit: 1200×80×350 mm long panel.

### Connectors (auto-generated)
Three inlet connectors (8 mm) + one outlet/vacuum connector (12 mm). In Family Editor:
- Set each inlet: Domain = Piping, System = Other Pipe, Direction = In, Size = 8 mm
- Set the outlet (VAC or drain): Domain = Piping, System = Other Pipe, Direction = Out, Size = 12 mm

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `MGS_TU_TYPE_TXT` | `MGS_GASES_TXT` | `MGS_OPERATING_KPA` |
|---|---|---|---|---|
| `TERMINAL_UNIT_O2` | `TU-O2` | `TERMINAL_UNIT` | `O2` | `414` |
| `TERMINAL_UNIT_N2O` | `TU-N2O` | `TERMINAL_UNIT` | `N2O` | `414` |
| `TERMINAL_UNIT_MEDAIR` | `TU-MA` | `TERMINAL_UNIT` | `MEDAIR` | `414` |
| `TERMINAL_UNIT_SURGAIR` | `TU-SA` | `TERMINAL_UNIT` | `SURGAIR` | `800` |
| `TERMINAL_UNIT_VAC` | `TU-VAC` | `TERMINAL_UNIT` | `VAC` | `-40` |
| `TERMINAL_UNIT_CO2` | `TU-CO2` | `TERMINAL_UNIT` | `CO2` | `414` |
| `TERMINAL_UNIT_N2` | `TU-N2` | `TERMINAL_UNIT` | `N2` | `800` |
| `TERMINAL_UNIT_HELIOX` | `TU-HX` | `TERMINAL_UNIT` | `HELIOX` | `414` |
| `AVSU_BOX_5GAS` | `AVSU` | `AVSU` | `O2,N2O,MEDAIR,SURGAIR,VAC` | — |
| `ALARM_PANEL_AREA` | `AP-AREA` | `ALARM_PANEL` | `O2,N2O,MEDAIR,VAC` | — |
| `MAP_THEATRE_PANEL` | `MAP` | `THEATRE_PANEL` | `O2,N2O,MEDAIR,SURGAIR,VAC,CO2` | — |
| `BEDHEAD_UNIT_WARD` | `BHU` | `BEDHEAD_UNIT` | `O2,MEDAIR,VAC` | — |
| `VIE_MANIFOLD` | `VIE` | `VIE_MANIFOLD` | `O2` | `1400` |

### HTM 02-01 compliance note
`MGS_AVSU_ZONE_TXT` must be completed for all AVSU and alarm panel instances — the MGPS validation command uses it to confirm every zone has exactly one AVSU per gas and that all TUs on a zone are downstream of that AVSU. Leave the parameter blank in the family type; it is set per instance at placement time.

---

## STING_SEED_LabFixture

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 7 mm  
**14 type variants**

### 2D plan symbol
- Auto-generated square with diagonal X — reads as generic lab equipment.
- For fume hoods: rectangle + 3 vertical bars (sash slots), labelled with face velocity.
- For eyewash/shower: circle + cross (emergency station symbol).

### 3D representation
- Fume hood: 1500×800×2400 mm tall enclosure. Keep the auto-generated box.
- Biosafety cabinet: 1200×700×2200 mm. Same template.
- Autoclave: 800×800×1200 mm.
- Emergency shower: 300×300×2200 mm column with 300 mm head at top.
- Eyewash: 400×200×1000 mm pedestal.
- Lab tap: 100×50×200 mm (face-based, very small).

### Connectors (auto-generated — verify)
- DCW supply (−X face, +Z facing)
- DHW supply (−X face +Z)
- Sanitary outlet (bottom, −Z)
- OtherPipe × 2 (gas, DI water — +X face)
- ExhaustAir (top, +Z) — for fume hoods only; drag off the family or suppress for tap/shower types

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `LAB_FIX_TYPE_TXT` | `LAB_FIX_FACE_VEL_MS` | `LAB_FIX_DELUGE_LMIN` | `LAB_FIX_HAZARD_CLASS_TXT` |
|---|---|---|---|---|---|
| `FUME_HOOD` | `FH` | `FUME_HOOD` | `0.5` | — | `BSL2` |
| `FUME_HOOD_LOW_FLOW` | `FH-LF` | `FUME_HOOD_LOW_FLOW` | `0.3` | — | `BSL2` |
| `FUME_CUPBOARD_RADIOISOTOPE` | `FH-RI` | `FUME_CUPBOARD_RADIOISOTOPE` | `0.7` | — | `BSL3` |
| `BIOSAFETY_CABINET_II` | `BSC-II` | `BIOSAFETY_CABINET_II` | — | — | `BSL3` |
| `BIOSAFETY_CABINET_III` | `BSC-III` | `BIOSAFETY_CABINET_III` | — | — | `BSL4` |
| `LAMINAR_FLOW_HOOD` | `LFH` | `LAMINAR_FLOW_HOOD` | `0.45` | — | — |
| `AUTOCLAVE` | `ATC` | `AUTOCLAVE` | — | — | `BSL2` |
| `EYEWASH_STATION` | `EW` | `EYEWASH_STATION` | — | `6` | — |
| `EMERGENCY_SHOWER` | `ES` | `EMERGENCY_SHOWER` | — | `76` | — |
| `COMBO_SHOWER_EYEWASH` | `ES-EW` | `COMBO_SHOWER_EYEWASH` | — | `82` | — |
| `FACE_SHOWER` | `FS` | `FACE_SHOWER` | — | `11` | — |
| `ELBOW_TAP` | `ELT` | `ELBOW_TAP` | — | — | — |
| `LAB_GAS_TAP` | `LGT` | `LAB_GAS_TAP` | — | — | — |
| `LAB_DI_WATER_TAP` | `LDI` | `LAB_DI_WATER_TAP` | — | — | — |

### ANSI Z358.1 compliance note
`LAB_FIX_DELUGE_LMIN` drives the emergency fixture sizing report. The values in the table (6 / 76 / 82 / 11 L/min) are ANSI Z358.1 minimums — do not reduce them. The plumbing engineer must confirm the supply pressure delivers these flow rates at the fixture.

---

## STING_SEED_SpecialityEquipment — FRP Penetrations

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 5 mm  
**11 type variants** — fire-rated and unrated MEP penetration seals

> **This is the highest-priority seed to polish.** Every duct, pipe, cable, and cable-tray crossing a fire-rated element gets one of these. The Penetration Register is auto-generated from instances of this seed — the `PEN_CONTROL_NUMBER_TXT` field is the register's primary key.

### 2D plan symbol
- Concentric circles (Ø500 outer, Ø350 inner — auto-generated) with four short tick lines crossing the gap at 0°/90°/180°/270°. Reads as a sleeve-through.
- For section view (Front elevation): add a 200 mm vertical bar with horizontal arrows pointing in/out (auto-generated from the JSON `section` block).
- Subcategory: `STING_SEED`.

### Critical formula — wire Mark to control number
In Family Editor, open *Family Types*, select the formula cell next to **Mark**, type:
```
= PEN_CONTROL_NUMBER_TXT
```
This makes the control number visible in tag schedules without extra wiring. The `formulaBindings` block in the JSON documents this requirement — **it is not auto-applied**; you must add it manually.

### 3D representation
Replace the auto box with a conical sleeve: 80×80 mm at top narrowing to 60×60 mm at bottom, 200 mm tall. Add a 2D line at the soffit reference plane for section view visibility.

### Type variants

| Type name | `PEN_FIRE_RATING_TXT` | `PEN_SEALANT_TYPE_TXT` | `PEN_CERTIFICATION_TXT` | Notes |
|---|---|---|---|---|
| `FR30` | `FR30` | `INTUMESCENT` | `BS 476-20 / EN 1366-3 (30 min)` | |
| `FR60` | `FR60` | `INTUMESCENT` | `BS 476-20 / EN 1366-3 (60 min)` | Most common |
| `FR90` | `FR90` | `INTUMESCENT` | `BS 476-20 / EN 1366-3 (90 min)` | |
| `FR120` | `FR120` | `INTUMESCENT` | `BS 476-20 / EN 1366-3 (120 min)` | |
| `FR240` | `FR240` | `INTUMESCENT_BOARD` | `BS 476-20 / EN 1366-3 (240 min)` | Stair shafts, compartment floors |
| `FR30_MULTI` | `FR30` | `INTUMESCENT_WRAP` | `EN 1366-3 multi-service (30 min)` | Multiple services in one sleeve |
| `FR60_CAVITY_BARRIER` | `FR60` | `INTUMESCENT_CAVITY_BARRIER` | `BS 9999 cavity barrier (60 min)` | Cavity walls |
| `ACOUSTIC_FIRE_SEAL` | `FR60` | `ACOUSTIC_FIRE_COMPOUND` | `EN 1366-3 (60 min) + BS 8233` | Acoustic + fire combined |
| `INTUMESCENT_COLLAR` | `FR60` | `INTUMESCENT_COLLAR` | `EN 1366-3 plastic pipe collar (60 min)` | Plastic pipes only |
| `EXPANDING_FOAM_FILL` | `FR30` | `FIRE_RATED_FOAM` | `EN 1366-3 foam sealant (30 min)` | Services voids |
| `SLEEVE_GENERIC` | *(empty)* | `NONE` | *(empty)* | Non-fire-rated sleeve |

---

## STING_SEED_FireDamper

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 6 mm  
**12 type variants** — BS EN 15650 / EN 1366-2 fire and combined fire/smoke dampers

### 2D plan symbol
- Auto-generated square with cross diagonals and centre cross reads as a fire damper. Keep.
- Add a small filled half-moon arc at one side for actuator position on motorised types.

### 3D representation
- Rectangular dampers: 400×200×400 mm box (W×D×H where D = duct depth). This scales with the installed duct size — parameterise width and height against instance parameters.
- Round dampers: Ø250 mm cylinder, 200 mm long.

### Critical formula
```
Mark = PEN_CONTROL_NUMBER_TXT
```
Same as SpecialityEquipment — the fire damper register uses this as its key.

### Type variants

| Type name | `ASS_PRODCT_COD_TXT` | `PEN_FIRE_RATING_TXT` | `FD_BSEN15650_CLASS_TXT` | `FD_ACTUATION_TXT` | Notes |
|---|---|---|---|---|---|
| `FD_FR60_RECT_FUSIBLE` | `FD` | `FR60` | `EI60S` | `FUSIBLE_LINK_72C` | Default |
| `FD_FR90_RECT_FUSIBLE` | `FD` | `FR90` | `EI90S` | `FUSIBLE_LINK_72C` | |
| `FD_FR120_RECT_FUSIBLE` | `FD` | `FR120` | `EI120S` | `FUSIBLE_LINK_72C` | |
| `FD_FR60_RECT_MOTORISED` | `FD` | `FR60` | `EI60S` | `MOTORISED_24V` | |
| `FD_FR120_RECT_MOTORISED` | `FD` | `FR120` | `EI120S` | `MOTORISED_24V` | |
| `FD_FR30_ROUND_MOTORISED` | `FD` | `FR30` | `EI30S` | `MOTORISED_24V` | Round duct |
| `FD_FR60_ROUND_MOTORISED` | `FD` | `FR60` | `EI60S` | `MOTORISED_24V` | Round duct |
| `FD_FR90_ROUND_MOTORISED` | `FD` | `FR90` | `EI90S` | `MOTORISED_24V` | Round duct |
| `FD_FR120_ROUND_MOTORISED` | `FD` | `FR120` | `EI120S` | `MOTORISED_24V` | Round duct |
| `FSD_SMOKE_ONLY` | `SD` | `SMOKE_ONLY` | `ES` | `MOTORISED_24V` | Smoke damper only |
| `FD_FR60_COMBINED_SMOKE` | `FSD` | `FR60` | `EIS60` | `MOTORISED_24V_FUSIBLE_LINK` | FSD |
| `FD_FR120_COMBINED_SMOKE` | `FSD` | `FR120` | `EIS120` | `MOTORISED_24V_FUSIBLE_LINK` | FSD |
| `FD_CURTAIN_FR60` | `FD-C` | `FR60` | `EI60` | `GRAVITY_FUSIBLE_LINK` | Restricted depth |

### BS EN 15650 class notation
- `EI60S` = 60-minute integrity + insulation, self-closing (S = spring/gravity close — no power needed)
- `EIS60` = Fire + Smoke combined, 60-minute
- `ES` = Smoke only, no fire integrity
- Suffix `300` (e.g., `EI60S300`) indicates leakage class — add when specified by the acoustic engineer.

---

## STING_SEED_AcousticSeal

**Hosting:** Face-based · **Template:** `Metric Specialty Equipment face based.rft` · **Symbol size at 1:100:** 4 mm  
**10 type variants** — Approved Document E, BS 8233, DW/144 acoustic penetration seals

### 2D plan symbol
- Auto-generated triple concentric circles read as an acoustic seal. Keep.
- The three rings represent: outer boundary of the seal compound, inner mineral-wool infill, inner void / pipe.

### 3D representation
- 80×80×100 mm box. Replace with a cylinder 80 mm diameter, 100 mm long for round pipe penetrations.

### Critical formula
```
Mark = PEN_CONTROL_NUMBER_TXT
```

### Type variants

| Type name | `ACS_RW_TARGET_DB` | `ACS_SEAL_TYPE_TXT` | `ACS_DEPTH_MM` | `ACS_CERT_TXT` |
|---|---|---|---|---|
| `ACS_RW30` | `30` | `MINERAL_WOOL` | `50` | `Approved Doc E (Rw 30 dB)` |
| `ACS_RW40` | `40` | `MINERAL_WOOL_PLUS_SEALANT` | `75` | `BS 8233 / Approved Doc E (Rw 40 dB)` |
| `ACS_RW45` | `45` | `MINERAL_WOOL_PLUS_SEALANT` | `100` | `BS 8233 / Approved Doc E (Rw 45 dB)` |
| `ACS_RW55` | `55` | `MINERAL_WOOL_PLUS_SEALANT_PLUS_PUTTY` | `150` | `BS 8233 (Rw 55 dB)` |
| `ACS_RW63` | `63` | `INTUMESCENT_PUTTY_PADS` | `200` | `BS 8233 (Rw 63 dB)` |
| `ACS_DW144_DUCT` | `40` | `DW144_DUCT_WRAP` | `100` | `DW/144 duct acoustic seal` |
| `ACS_FLEXIBLE_BOOT` | `45` | `FLEXIBLE_BOOT` | `150` | `BS 8233 flexible connection (Rw 45 dB)` |
| `ACS_PIPE_SLEEVE_40DB` | `40` | `PIPE_SLEEVE_ACOUSTIC_LINING` | `100` | `BS 8233 pipe sleeve (40 dB attenuation)` |
| `ACS_FIRE_ACOUSTIC_COMBO` | `45` | `ACOUSTIC_FIRE_COMPOUND` | `100` | `EN 1366-3 FR60 + BS 8233 (Rw 45 dB)` |
| `ACS_LABYRINTH_SEAL` | `50` | `LABYRINTH_BAFFLE` | `300` | `BS 8233 labyrinth seal (Rw ≥50 dB)` |

---

## After authoring: end-to-end test

1. Place one instance of each polished seed in a test plan view.
2. **Tag each** — existing STING tag families pick them up automatically (they bind by category + shared param GUID, not family name).
3. **Schedule** — open a schedule for the relevant category. Every parameter in the JSON spec's `parameters` array must appear as a schedulable field. If a parameter is missing from the schedule fields, it was not bound — run `LoadSharedParamsCommand` and reload the family.
4. **Swap to Manufacturer** — load a real manufacturer family, then run the swap command on a test instance:
   - Position / rotation / host must be preserved exactly.
   - Tag must still read (parameters survive via GUID, not name).
   - `STING_DESIGN_REF_TXT` now contains the original seed ID.
   - `STING_SWAP_HISTORY_TXT` records timestamp + operator + source/dest pair.
5. **Double-swap** — select the already-swapped instance and swap again to a different manufacturer family. Both swap history entries must appear in `STING_SWAP_HISTORY_TXT`.
6. **Rebuild-safe test** — re-run `BuildSeedFamiliesCommand` in *Safe (missing only)* mode. Confirm zero polished families were overwritten (check `Result: X protected` in the command result dialog).
7. **Finalization test** — create a `.sting-finalized` sidecar for one seed. Re-run in *Rebuild unfinalized* mode. Confirm that finalized seed was skipped and the others were regenerated.

---

## Troubleshooting

**Q: I re-ran Build Seed Families and my polished family was overwritten.**  
A: You were in *Rebuild all* mode and the JSON doesn't have `"protectExisting": true`. Fix: add `"protectExisting": true` to the seed's JSON spec, OR create the `.sting-finalized` sidecar before the next run, OR always use *Safe (missing only)* mode. Store polished `.rfa` files in `Families/Seeds/` (this corporate folder) — the command only writes to `<project>/_BIM_COORD/Families/Seeds/`.

**Q: How do I force a full regeneration of one specific seed after updating its JSON?**  
A: Delete (or temporarily rename) the `.sting-finalized` sidecar for that seed, then run *Rebuild unfinalized*. Only that seed regenerates; all finalized seeds are skipped.

**Q: A type variant is missing from the swap candidates list.**  
A: The swap picker matches `variantPattern` (a regex) against the loaded family's type names. If no match fires, edit `STING_FAMILY_SWAP_REGISTRY.json` at `<project>/_BIM_COORD/` and add a pattern that matches the manufacturer family's type name. Project overrides are additive — they merge on top of the auto-registered candidates from the seed JSON.

**Q: Connectors disappeared after the swap.**  
A: Revit re-creates connectors from the destination family's definitions. Unmatched connectors are dropped. Run `AutoJoinMepConnectors` on the swapped set; for unrecoverable electrical topology, run `BatchAssignCircuits`.

**Q: Shared parameters are missing from the Family Types dialog.**  
A: The shared parameter file was not loaded before building. Run `LoadSharedParamsCommand` (dock panel → *Load Params*), then re-run `BuildSeedFamiliesCommand` in *Rebuild unfinalized* or *Rebuild all* mode for the affected seeds.

**Q: I have a manufacturer `.rfa` I want to use as the seed geometry.**  
A: Set `"sourceFamilyPath"` in the seed's JSON to the relative path of the manufacturer `.rfa` (e.g., `"Families/Seeds/Source/Grundfos_MAGNA3.rfa"`). On the next build run, the command opens that file, injects STING parameters idempotently, and saves the result under the seed's standard filename. Your source `.rfa` is never modified.

**Q: My company needs a seed category that isn't in the list.**  
A: Drop a new `STING_SEED_<Category>.json` into `StingTools/Data/Seeds/` following the same schema as existing specs. Run `Build Seed Families` — no code change is required. The new seed appears in the swap registry automatically after the first build.

**Q: The penetration register is empty even though I've placed SpecialityEquipment instances.**  
A: The `PEN_CONTROL_NUMBER_TXT` parameter must be non-empty for a penetration to appear in the register. Either enter numbers manually, or run `PenetrationAutoNumberCommand` which assigns sequential control numbers per host-element. Also confirm the `Mark = PEN_CONTROL_NUMBER_TXT` formula was added in Family Editor (it is not auto-applied).
