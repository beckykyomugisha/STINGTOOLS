# STING Tools — Complete Tagging Guide

## ISO 19650 Asset Tag Format

Every element in a STING-managed Revit project receives an **8-segment asset tag** compliant with **ISO 19650-3:2020**. The tag uniquely identifies each element by discipline, location, system, and sequence.

```
 DISC - LOC  - ZONE - LVL - SYS  - FUNC - PROD - SEQ
  M   - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

| Pos | Segment | Parameter                  | Max | Description                        | Example        |
|-----|---------|----------------------------|-----|------------------------------------|----------------|
| 1   | DISC    | ASS_DISCIPLINE_COD_TXT     | 3   | Discipline code                    | M, E, P, A     |
| 2   | LOC     | ASS_LOC_TXT                | 4   | Location / building code           | BLD1, EXT      |
| 3   | ZONE    | ASS_ZONE_TXT               | 3   | Zone code                          | Z01, Z02       |
| 4   | LVL     | ASS_LVL_COD_TXT            | 4   | Level code                         | L02, GF, B1    |
| 5   | SYS     | ASS_SYSTEM_TYPE_TXT        | 4   | System type (CIBSE / Uniclass)     | HVAC, DCW, LV  |
| 6   | FUNC    | ASS_FUNC_TXT               | 4   | Function code (CIBSE / Uniclass)   | SUP, HTG, PWR  |
| 7   | PROD    | ASS_PRODCT_COD_TXT         | 4   | Product code                       | AHU, DB, DR    |
| 8   | SEQ     | ASS_SEQ_NUM_TXT            | 4   | Zero-padded sequence number        | 0001, 0042     |

**Separator**: `-` (configurable via `project_config.json`)
**Padding**: 4 digits for SEQ (configurable)

---

## Tag Creation Pipeline — Step by Step

The tagging process follows a 6-step pipeline. Each step can be run individually or combined via one-click automation commands.

### Overview

```
Step 1: Load Parameters    ─── Bind 200+ shared parameters to Revit categories
Step 2: Populate Tokens    ─── Auto-derive DISC/LOC/ZONE/LVL/SYS/FUNC/PROD
Step 3: Pre-Tag Audit      ─── Dry-run: predict tags, find issues before committing
Step 4: Tag                ─── Assign SEQ numbers, assemble 8-segment tags
Step 5: Validate           ─── ISO 19650 compliance check
Step 6: Combine            ─── Write tag to all 36 discipline-specific containers
```

Steps 2-6 are automatically combined by **Tag & Combine** or **Full Auto-Populate** for one-click operation.

---

### Step 1: Load Shared Parameters

**Command**: Load Params (CREATE tab > Setup section)
**Class**: `Tags.LoadSharedParamsCommand`
**What it does**: Binds all STING shared parameters to Revit categories so they can hold tag data.

**Two-pass binding**:

| Pass | Source | Scope | Parameters |
|------|--------|-------|------------|
| Pass 1 | `MR_PARAMETERS.txt` | Universal — all 53 taggable categories | 8 source tokens + TAG containers + identity + spatial |
| Pass 2 | `CATEGORY_BINDINGS.csv` | Discipline-specific | 10,661 category-parameter bindings (e.g., HVC_EQP_TAG only to Mechanical Equipment) |

**When to run**: Once per project (or after adding new categories). Already-bound parameters are skipped.

**What happens**:
1. Locates `MR_PARAMETERS.txt` in the Data folder
2. Opens the shared parameter file as the definition source
3. Creates `InstanceBinding` for each parameter to its target categories
4. Reports: "Bound X parameters to Y categories"

---

### Step 2: Populate Tokens

**Command**: Family-Stage Populate (CREATE tab > More section)
**Class**: `Tags.FamilyStagePopulateCommand`
**What it does**: Pre-populates all 7 derivable tokens on every element before tagging.

Each token is derived automatically from element context:

#### DISC — Discipline Code

Derived from the element's **Revit category** using the Discipline Map (41 mappings):

| Category | DISC | Category | DISC |
|----------|------|----------|------|
| Mechanical Equipment | M | Doors | A |
| Ducts / Air Terminals | M | Windows | A |
| Pipes / Pipe Fittings | M* | Walls / Floors / Ceilings | A |
| Electrical Equipment | E | Roofs / Furniture | A |
| Lighting Fixtures | E | Structural Columns / Framing | S |
| Conduits / Cable Trays | E | Sprinklers | FP |
| Plumbing Fixtures | P | Fire Alarm Devices | FP |
| Communication Devices | LV | Generic Models | G |

*\* Pipes default to M (Mechanical) but are auto-corrected at tag time based on connected system:*
- Connected to DCW/SAN/RWD/DHW/GAS system → **P** (Plumbing)
- Connected to FP system → **FP** (Fire Protection)
- Connected to HVAC system → stays **M** (Mechanical)

#### LOC — Location Code

Derived by `SpatialAutoDetect` using 3 sources (in priority order):

1. **Room name** — scans the room containing the element for building keywords (e.g., "Building 1" → BLD1)
2. **Room number prefix** — parses room number for location codes
3. **Project Information** — falls back to project-level LOC setting

| Code | Meaning |
|------|---------|
| BLD1 | Building 1 (primary) |
| BLD2 | Building 2 |
| BLD3 | Building 3 |
| EXT  | External / site |
| XX   | Unknown (placeholder — triggers validation warning) |

#### ZONE — Zone Code

Derived by `SpatialAutoDetect` from the **Room Department** field or room name patterns:

| Code | Meaning |
|------|---------|
| Z01  | Zone 1 |
| Z02  | Zone 2 |
| Z03  | Zone 3 |
| Z04  | Zone 4 |
| ZZ   | Unzoned area |
| XX   | Unknown |

#### LVL — Level Code

Derived by `ParameterHelpers.GetLevelCode()` from the element's **Revit Level** name:

| Pattern in Level Name | LVL Code | Example |
|-----------------------|----------|---------|
| Ground, GF, G/F, Level 0 | GF | Ground Floor → GF |
| Basement, B1, B2... | B1, B2... | Basement Level 1 → B1 |
| Level 01, Level 1, L01... | L01, L02... | Level 02 → L02 |
| Roof | RF | Roof Level → RF |
| Mezzanine | MZ | Mezzanine → MZ |
| Plant | PL | Plant Room Level → PL |
| Penthouse | PH | Penthouse → PH |
| (no level / unresolved) | L00 | Guaranteed default |

#### SYS — System Type Code

Derived by `TagConfig.GetMepSystemAwareSysCode()` using a **6-layer intelligence stack** (first match wins):

| Layer | Source | What it checks | Example |
|-------|--------|----------------|---------|
| 1 | MEP Connector | Connected system name via `FamilyInstance.MEPModel.ConnectorManager` | Pipe connected to "Domestic Cold Water" → DCW |
| 2 | System Type Param | `RBS_DUCT_SYSTEM_TYPE` / `RBS_PIPING_SYSTEM_TYPE` built-in parameter | Duct with Supply Air system → HVAC |
| 3 | Electrical Circuit | Panel name analysis from circuit | Circuit on "DB-LTG-L01" → LV |
| 4 | Family Name | Pattern matching on family name | "Exhaust Fan" → HVAC |
| 5 | Room Type | Room name inference | Element in "Server Room" → ICT |
| 6 | Category Fallback | Category → SYS via SysMap | Lighting Fixtures → LV |

**System Codes (17)**:

| Code | System | Discipline | Function Default |
|------|--------|------------|------------------|
| HVAC | Heating, Ventilation, Air Conditioning | M | SUP |
| DCW  | Domestic Cold Water | P | DCW |
| DHW  | Domestic Hot Water | P | DHW |
| HWS  | Hot Water Service (heating) | M/P | HTG |
| SAN  | Sanitary / Waste | P | SAN |
| RWD  | Rainwater Drainage | P | RWD |
| GAS  | Natural Gas | P | GAS |
| FP   | Fire Protection (sprinklers, wet risers) | FP | FP |
| LV   | Low Voltage / Power / Lighting | E | PWR |
| FLS  | Fire Life Safety (alarms, detection) | FP | FLS |
| COM  | Communications / Telephony | LV | COM |
| ICT  | Information & Communications Technology | LV | ICT |
| NCL  | Nurse Call | LV | NCL |
| SEC  | Security (CCTV, access control) | LV | SEC |
| ARC  | Architectural | A | FIT |
| STR  | Structural | S | STR |
| GEN  | General / Generic | G | GEN |

**Discipline-based defaults** (when all 6 layers fail):
M→HVAC, E→LV, P→DCW, A→ARC, S→STR, FP→FP, LV→LV, G→GEN

#### FUNC — Function Code

Derived by `TagConfig.GetSmartFuncCode()` with sub-system awareness:

**HVAC sub-functions** (from duct/pipe system name analysis):
| Connected System Contains | FUNC |
|---------------------------|------|
| "Supply", "SUP" | SUP (Supply) |
| "Return", "RTN" | RTN (Return) |
| "Exhaust", "EXH" | EXH (Exhaust) |
| "Fresh Air", "Outside", "FRA" | FRA (Fresh Air) |

**HWS sub-functions**:
| Connected System Contains | FUNC |
|---------------------------|------|
| "Heating", "LTHW", "Radiator" | HTG (Heating) |
| "Domestic Hot", "DHW" | DHW (Dom. Hot Water) |

**All other systems**: Uses the SYS→FUNC map (see System Codes table above).

#### PROD — Product Code

Derived by `TagConfig.GetFamilyAwareProdCode()` which inspects the **family name** for 35+ specific product codes before falling back to the category default.

**Family name pattern matching examples**:

| Family Name Contains | PROD | Category Default |
|----------------------|------|------------------|
| "Air Handling", "AHU" | AHU | (Mech. Equip.) |
| "Fan Coil", "FCU" | FCU | AHU |
| "VAV", "Variable Air" | VAV | AHU |
| "Chiller" | CHL | AHU |
| "Boiler" | BLR | AHU |
| "Pump" | PMP | AHU |
| "Distribution Board", "Switchboard" | DB | DB |
| "Transformer" | TFR | DB |
| "Panel", "Consumer Unit" | PNL | DB |
| "Smoke Detector" | SMK | FAD |
| "Heat Detector" | HTD | FAD |
| "Basin", "Sink" | BSN | FIX |
| "WC", "Toilet" | WC | FIX |
| "Shower" | SHW | FIX |

**Category-level defaults** (when family name doesn't match):

| Category | Default PROD |
|----------|-------------|
| Mechanical Equipment | AHU |
| Ducts | DU |
| Pipes | PP |
| Electrical Equipment | DB |
| Lighting Fixtures | LUM |
| Plumbing Fixtures | FIX |
| Sprinklers | SPR |
| Doors | DR |
| Windows | WIN |
| Walls | WL |
| Floors | FL |
| Ceilings | CLG |
| Roofs | RF |
| Furniture | FUR |
| Generic Models | GEN |

---

### Step 3: Pre-Tag Audit (Optional)

**Command**: Pre-Tag Audit (CREATE tab > More section)
**Class**: `Tags.PreTagAuditCommand`
**What it does**: Performs a complete **dry-run** of the tagging process without writing anything. Predicts:

- What tag each element would receive
- Which elements would collide (duplicate tags)
- Which tokens fail ISO 19650 validation
- How LOC/ZONE were auto-detected
- Family-aware PROD code assignments

Outputs a detailed report to TaskDialog and optionally exports to CSV.

**When to use**: Before running Tag on large projects (1000+ elements) to catch issues early.

---

### Step 4: Tag

**Commands** (choose one):

| Command | Scope | Best For |
|---------|-------|----------|
| Auto Tag | Active view only | Quick tagging of visible elements |
| Batch Tag | Entire project | Full project tagging |
| Tag New Only | Untagged elements only | Adding new elements to tagged project |
| Tag & Combine | View/selection/project | One-click: populate + tag + combine |

**What happens during tagging** (inside `TagConfig.BuildAndWriteTag()`):

1. **Check existing tag**: Read ASS_TAG_1 from element
2. **Collision mode decision**:
   - **Skip**: Leave already-tagged elements untouched
   - **Overwrite**: Regenerate all tokens and replace tag
   - **AutoIncrement**: Skip complete tags by default; increment SEQ on collision
3. **Derive all 8 tokens** using the intelligence layers described in Step 2
4. **Generate SEQ number**:
   - Group key = `DISC_SYS_LVL` (e.g., `M_HVAC_L02`)
   - Counter increments within each group
   - Starting value = highest existing SEQ in that group + 1
   - Zero-padded to 4 digits (0001, 0002, ...)
5. **Collision detection**: Check assembled tag against `HashSet<string>` of all existing tags
   - If collision found: increment SEQ and retry (up to 100 attempts)
   - Record collision depth in stats
6. **Write tokens**: Set all 8 parameter values on the element
   - Overwrite mode: always writes
   - Default mode: `SetIfEmpty` — preserves manually-set values
7. **Assemble TAG1**: Join tokens with separator → `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`
8. **Write TAG1**: Set `ASS_TAG_1_TXT` with the full tag
9. **Auto-populate STATUS**: Derive from Revit phase (NEW/EXISTING/DEMOLISHED/TEMPORARY)
10. **Auto-populate REV**: Derive from project revision sequence (P01, P02...)
11. **Write all 36 containers**: Automatically propagate to discipline-specific containers

#### Sequence Numbering Logic

SEQ numbers are grouped by **DISC + SYS + LVL**:

```
Group: M_HVAC_L02     → AHU: 0001, 0002, 0003; FCU: 0004, 0005
Group: E_LV_L02       → DB: 0001; LUM: 0002, 0003, 0004
Group: P_DCW_GF       → FIX: 0001, 0002
```

Before tagging begins, `GetExistingSequenceCounters()` scans all elements to find the highest SEQ in each group, ensuring new tags continue from where existing numbering left off.

---

### Step 5: Validate

**Command**: Validate (CREATE tab > QA section)
**Class**: `Tags.ValidateTagsCommand`
**What it does**: Checks every tagged element for ISO 19650 compliance.

**Validation checks performed by `ISO19650Validator`**:

| Check | Rule | Example Failure |
|-------|------|-----------------|
| DISC valid | Must be in {M, E, P, A, S, FP, LV, G} | DISC = "X" → FAIL |
| LOC valid | Must be in configured LocCodes | LOC = "BLDG1" → FAIL (should be BLD1) |
| ZONE valid | Must be in configured ZoneCodes | ZONE = "Zone1" → FAIL (should be Z01) |
| LVL pattern | Must match level code pattern | LVL = "Floor 2" → FAIL (should be L02) |
| SYS valid | Must be in ValidSysCodes list | SYS = "AIRCON" → FAIL (should be HVAC) |
| FUNC valid | Must be in ValidFuncCodes list | FUNC = "SUPPLY" → FAIL (should be SUP) |
| PROD format | 2-4 alphanumeric characters | PROD = "Air Handling Unit" → FAIL |
| SEQ format | Positive integer, zero-padded | SEQ = "3" → FAIL (should be 0003) |
| Tag complete | All 8 segments present | "M-BLD1-Z01--HVAC-SUP-AHU-0001" → FAIL (empty LVL) |
| DISC/SYS cross-check | SYS must be valid for DISC | DISC=E with SYS=HVAC → WARNING |
| DISC/Category cross-check | DISC must match element category | Lighting Fixture with DISC=M → WARNING |

**Output**: Per-discipline compliance percentage and list of failing elements.

---

### Step 6: Combine to Containers

**Command**: Combine Parameters (CREATE tab > Tokens section)
**Class**: `Tags.CombineParametersCommand`
**What it does**: Writes the assembled tag into all 36 discipline-specific container parameters.

> **Note**: As of the current version, Step 4 (Tag) automatically writes containers, so a separate Combine step is only needed if tokens were manually edited after tagging.

#### Tag Container Parameters (36 total)

**Universal containers** (written for ALL elements):

| Parameter | Preset | Content | Example |
|-----------|--------|---------|---------|
| ASS_TAG_1_TXT | all (8 segments) | Full tag | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |
| ASS_TAG_2_TXT | short_id (DISC-PROD-SEQ) | Short reference | M-AHU-0003 |
| ASS_TAG_3_TXT | location (LOC-ZONE-LVL) | Spatial reference | BLD1-Z01-L02 |
| ASS_TAG_4_TXT | sys_ref (SYS-FUNC-PROD) | System reference | HVAC-SUP-AHU |
| ASS_TAG_5_TXT | line1 (top half) | Multi-line top | M-BLD1-Z01-L02 |
| ASS_TAG_6_TXT | line2 (bottom half) | Multi-line bottom | HVAC-SUP-AHU-0003 |

**TAG7 — Rich narrative** (6 sub-sections):

| Parameter | Section | Content |
|-----------|---------|---------|
| ASS_TAG_7_TXT | Full | Complete narrative with markup |
| ASS_TAG_7A_TXT | Identity | Asset name, product code, manufacturer, model |
| ASS_TAG_7B_TXT | System | System type, function code |
| ASS_TAG_7C_TXT | Spatial | Room, department, grid reference |
| ASS_TAG_7D_TXT | Lifecycle | Status, revision, origin |
| ASS_TAG_7E_TXT | Technical | Capacity, flow, voltage (discipline-specific) |
| ASS_TAG_7F_TXT | Classification | Uniformat, OmniClass, keynote, ISO tag |

**Discipline-specific containers** (written only to matching categories):

| Container | Categories | Parameters |
|-----------|-----------|------------|
| HVC_EQP | Mechanical Equipment | HVC_EQP_TAG_01, _02, _03 |
| HVC_DCT | Ducts, Air Terminals, Duct Fittings | HVC_DCT_TAG_01, _02, _03 |
| HVC_FLX | Flex Ducts | HVC_FLX_TAG_01, _02, _03 |
| ELC_EQP | Electrical Equipment | ELC_EQP_TAG_01, _02 |
| ELE_FIX | Electrical Fixtures | ELE_FIX_TAG_01, _02 |
| LTG_FIX | Lighting Fixtures/Devices | LTG_FIX_TAG_01, _02 |
| PLM_EQP | Pipes, Plumbing Fixtures | PLM_EQP_TAG_01, _02 |
| FLS_DEV | Sprinklers, Fire Alarm Devices | FLS_DEV_TAG_01, _02 |
| COM_DEV | Communication Devices | COM_DEV_TAG_01 |
| SEC_DEV | Security Devices | SEC_DEV_TAG_01 |
| NCL_DEV | Nurse Call Devices | NCL_DEV_TAG_01 |
| ICT_DEV | Data Devices | ICT_DEV_TAG_01 |
| MAT_TAG | Walls, Floors, Ceilings, Roofs, Doors, Windows | MAT_TAG_1 through MAT_TAG_6 |

---

## One-Click Automation Commands

### Tag & Combine (Recommended for most users)

**Button**: CREATE tab > "Tag & Combine"
**What it does in one click**:

1. Auto-detect LOC/ZONE from spatial data
2. Populate all 7 derivable tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD)
3. Tag all elements with SEQ assignment and collision detection
4. Combine into all 36 containers
5. Scope options: Active view, Selection, or Entire project

### Full Auto-Populate

**Button**: TEMP tab > Schedules section > "Full Auto"
**What it does in one click**:

1. Token population (all 7 tokens)
2. Dimension mapping (width, height, area, volume from Revit)
3. MEP data mapping (power, flow, voltage from built-in params)
4. Formula evaluation (199 formulas: costs, areas, rates)
5. Tag assembly and container writing
6. Grid/spatial reference population

---

## Collision Handling

When two elements would receive the same tag, STING offers three collision modes (user selects before tagging):

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| **AutoIncrement** (default) | Increment SEQ until unique tag found | Normal tagging — ensures every tag is unique |
| **Skip** | Leave already-tagged elements untouched | Re-running on a partially tagged project |
| **Overwrite** | Regenerate all tokens and replace existing tag | Fixing incorrect tags; re-tagging after changes |

**Collision detection**: Before writing, the tag is checked against a `HashSet<string>` of all existing tags in the project. On collision:
- SEQ increments (0003 → 0004 → 0005...) up to 100 attempts
- If still colliding after 100 attempts, a warning is logged
- The collision depth is tracked for reporting

---

## Real-Time Auto-Tagging

**Toggle**: CREATE tab > "Auto-Tagger" toggle button
**Class**: `Core.StingAutoTagger` (IUpdater)

When enabled, newly placed elements are **automatically tagged in real-time** as they are added to the model. The auto-tagger:

1. Monitors 22 taggable categories for element additions
2. Triggers on `Element.GetChangeTypeElementAddition()`
3. Runs the full token population + tag assembly pipeline
4. Suppresses redundant triggers via processed element ID tracking

**Performance**: Deduplication clears at 10,000 processed IDs. Designed for interactive use, not batch operations.

---

## Manual Token Overrides

Individual tokens can be set manually before or after auto-tagging:

| Button | Command | What it sets |
|--------|---------|-------------|
| Set Discipline | `Tags.SetDiscCommand` | DISC token (user picks from M/E/P/A/S/FP/LV/G) |
| Set Location | `Tags.SetLocCommand` | LOC token (user picks from configured codes) |
| Set Zone | `Tags.SetZoneCommand` | ZONE token (user picks from configured codes) |
| Set Status | `Tags.SetStatusCommand` | STATUS (EXISTING/NEW/DEMOLISHED/TEMPORARY) |
| Assign Numbers | `Tags.AssignNumbersCommand` | SEQ — renumber within DISC/SYS/LVL groups |
| Build Tags | `Tags.BuildTagsCommand` | Rebuild TAG1 from current token values |

Manually set tokens are **preserved** by the default tagging mode (`SetIfEmpty`). Only the Overwrite collision mode replaces manually-set values.

---

## Configuration

### project_config.json

Tag configuration can be customised per-project via `project_config.json` (saved alongside the Revit model):

```json
{
  "DISC_MAP": { "Mechanical Equipment": "M", "Ducts": "M", ... },
  "SYS_MAP": { "HVAC": ["Air Terminals", "Ducts", ...], ... },
  "PROD_MAP": { "Mechanical Equipment": "AHU", ... },
  "FUNC_MAP": { "HVAC": "SUP", "DCW": "DCW", ... },
  "LOC_CODES": ["BLD1", "BLD2", "BLD3", "EXT"],
  "ZONE_CODES": ["Z01", "Z02", "Z03", "Z04"],
  "TAG_FORMAT": {
    "separator": "-",
    "num_pad": 4,
    "segment_order": ["DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ"]
  }
}
```

**Edit via**: CREATE tab > Setup section > "Configure" button

### What you can customise:

- **Add/modify discipline mappings** (e.g., map "Specialty Equipment" to "M" instead of "G")
- **Add/modify system codes** (e.g., add "CHP" for combined heat and power)
- **Change location/zone codes** (e.g., "EAST", "WEST", "NORTH", "SOUTH" instead of BLD1-3)
- **Change tag separator** (e.g., `_` instead of `-`)
- **Change SEQ padding** (e.g., 5 digits for very large projects)
- **Reorder segments** (e.g., put SYS before LOC)

---

## QA & Analysis Commands

| Command | What it does |
|---------|-------------|
| Completeness Dashboard | Per-discipline compliance % with RAG status |
| Find Duplicates | Locate elements sharing the same tag |
| Fix Duplicates | Auto-resolve by incrementing SEQ numbers |
| Highlight Invalid | Color-code: red = missing, orange = incomplete |
| Audit to CSV | Export full tag audit (40+ columns) |
| Tag Statistics | Quick counts by discipline/system/level |
| Tag Register Export | Comprehensive asset register CSV |

---

## Quick Reference: Which Command to Use

| Situation | Command | Tab |
|-----------|---------|-----|
| First time tagging a project | Tag & Combine | CREATE |
| Adding new elements to tagged project | Tag New Only | CREATE > More |
| Re-tagging after design changes | Auto Tag (Overwrite mode) | CREATE |
| Full project re-tag from scratch | Batch Tag (Overwrite mode) | CREATE |
| Check before committing tags | Pre-Tag Audit | CREATE > More |
| Verify tag quality after tagging | Validate | CREATE > QA |
| Zero-touch real-time tagging | Auto-Tagger Toggle | CREATE |
| Complete automation (tags + data + formulas) | Full Auto-Populate | TEMP > Schedules |

---

## Data Files Referenced

| File | Purpose |
|------|---------|
| `PARAMETER_REGISTRY.json` | Master source of truth: token names, GUIDs, containers, tag format |
| `MR_PARAMETERS.txt` | Revit shared parameter definitions (200+ params, 18 groups) |
| `CATEGORY_BINDINGS.csv` | 10,661 parameter-to-category bindings for discipline-specific binding |
| `TAG_GUIDE_V3.csv` | Tag format specification, validation rules, auto-population logic |
| `project_config.json` | Per-project tag configuration overrides (user-editable) |
