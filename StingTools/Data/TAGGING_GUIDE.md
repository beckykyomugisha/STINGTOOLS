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

## CREATE Tab — Panel Layout

The **CREATE** tab is the primary tagging interface. All buttons referenced in this guide are located here unless otherwise stated.

### Header Controls

| Button | What it does |
|--------|-------------|
| **Scope: View** | Toggle tagging scope: View → Selection → Project (click to cycle) |
| **Overwrite: No** | Toggle overwrite mode: No (safe) → Yes (force re-derive all tokens) |

### SETUP Section

| Button | What it does |
|--------|-------------|
| **Load Shared Params** | Bind 200+ shared parameters to Revit categories (Step 1) |
| **Project Config** | View/edit/save `project_config.json` (tag maps, codes, format) |
| **Tag Config** | Display current tag lookup tables (DISC, SYS, PROD, FUNC maps) |
| **Sync Schema** | Propagate PARAMETER_REGISTRY.json changes to project bindings |
| **Audit Schema** | Verify parameter bindings match registry definitions |
| **Add Remap** | Add a field deprecation remap (old param name → new param name) |
| **Create Tag Families** | Generate Revit tag families (.rfa) for all 50 tagged categories |
| **Configure Labels** | Set which parameters appear in tag family labels |
| **Load** | Load tag families into the current project |
| **Audit** | Audit tag family configuration against current parameter schema |

### POPULATE TOKENS Section

| Button | What it does |
|--------|-------------|
| **Auto Populate** | One-click full automation: tokens + dimensions + MEP + formulas + tags + combine |
| **Assign Numbers** | Sequential SEQ numbering within DISC/SYS/LVL groups |
| **[Brain] Smart Tokens** | Pre-populate all 7 derivable tokens using spatial + family intelligence (Step 2) |
| **Build Tags** | Rebuild TAG1 from current token values on selected/view/project elements |
| **T3 Tags** | Build TAG3 (location subset: LOC-ZONE-LVL) |
| **Mat Tags** | Build material tag containers (MAT_TAG_1 through MAT_TAG_6) |
| **Build ALL** | Build all tag containers (TAG1-TAG6 + discipline-specific + material) |

**Manual token buttons** (13 individual tokens — click to set on selected elements):

| PROJ | ORIG | VOL | LVL | DISC | LOC | ZONE | SYS | FUNC | PROD | SEQ | STATUS | REV |

### QUALITY ASSURANCE Section

| Button | What it does |
|--------|-------------|
| **Validate** | ISO 19650 compliance check on all tokens and tag format (Step 5) |
| **Highlight** | Colour-code elements: red = missing tag, orange = incomplete tag |
| **Clear Art** | Reset graphic overrides in active view (remove highlight colours) |
| **Pre-Flight** | Dry-run combine: check what containers would be written without writing |
| **Completeness %** | Per-discipline compliance dashboard with RAG percentage |
| **Find Duplicates** | Locate elements sharing the same tag value |

### PARAGRAPH & PRESENTATION Section

| Button | What it does |
|--------|-------------|
| **Presentation Mode** | Switch all types between Compact/Technical/Full/Presentation/BOQ modes |
| **Paragraph Depth** | Set TAG7 paragraph depth: State 1 (compact) / 2 (standard) / 3 (comprehensive) |
| **Toggle Warnings** | Show/hide threshold warning text in tags (e.g., `[!U > 0.70]`) |
| **View Label Spec** | Display current label specification for tag families |
| **Export Label Guide** | Export label configuration as reference document |
| **TAG7 Heading Style** | Configure TAG7 section heading appearance |

### ISO COMPLETENESS DASHBOARD

| Button | What it does |
|--------|-------------|
| **Load** | Scan project and populate the dashboard grid (Id, Cat, %, RAG, Missing) |
| **Export** | Export dashboard data as CSV audit file |

Filter controls: **Min %** slider (filter by minimum completeness) and **All** dropdown (filter by discipline).

### EXPORT Section

| Button | What it does |
|--------|-------------|
| **Export Tag Register** | Export comprehensive 40+ column asset register CSV |
| **Combine Parameters** | Write assembled tag to all 36 discipline-specific containers (Step 6) |

### TOKEN INSPECTOR Section

| Button | What it does |
|--------|-------------|
| **[Find] Inspect Selection** | Dry-run audit: predict tags, collisions, ISO violations for selected elements (Step 3) |
| **Resolve All Issues** | One-click ISO 19650 compliance resolution: fix missing tokens, duplicates, invalid codes |

---

## ORGANISE Tab — Tagging Buttons

The ORGANISE tab contains additional tagging commands for tag operations and annotation placement:

| Button | What it does |
|--------|-------------|
| **Auto Tag** | Tag elements in active view with collision mode selection (Step 4) |
| **Batch Tag** | Tag ALL elements in entire project with progress dialog |
| **Tag+Combine** | One-click: populate tokens + tag + combine all 36 containers |
| **Tag New** | Tag only new/untagged elements (incremental — skips already-tagged) |
| **Pre-Audit** | Dry-run: predict tags, collisions, ISO violations before committing |
| **Re-Tag** | Force re-derive and overwrite tags on selected elements |
| **Copy Tags** | Copy tag values from first selected element to all others |
| **Swap Tags** | Swap all tag values between exactly 2 selected elements |
| **Tag Sel** | Tag selected elements only |
| **Del Tags** | Clear all 15 tag parameters from selection (with confirmation) |
| **Renumb** | Re-sequence tags within DISC/SYS/LVL groups |
| **Fix Dups** | Auto-resolve duplicate tags by incrementing SEQ numbers |

---

## VIEW Tab — Auto-Tagger

| Button | What it does |
|--------|-------------|
| **Auto-Tagger** | Toggle real-time auto-tagging on/off (tags elements as they are placed) |

---

## TEMP Tab — Full Automation

| Button | What it does |
|--------|-------------|
| **Full AutoPop** | Same as CREATE > **Auto Populate** — full automation pipeline |

---

## Tag Creation Pipeline — Step by Step

The tagging process follows a 6-step pipeline. Each step maps to specific buttons on the CREATE tab.

### Overview

```
Step 1: Load Shared Params     ─── Bind 200+ shared parameters to Revit categories
Step 2: [Brain] Smart Tokens   ─── Auto-derive DISC/LOC/ZONE/LVL/SYS/FUNC/PROD
Step 3: [Find] Inspect Selection ─ Dry-run: predict tags, find issues before committing
Step 4: Auto Tag / Batch Tag   ─── Assign SEQ numbers, assemble 8-segment tags
Step 5: Validate               ─── ISO 19650 compliance check
Step 6: Combine Parameters     ─── Write tag to all 36 discipline-specific containers
```

Steps 2-6 are automatically combined by ORGANISE > **Tag+Combine** or CREATE > **Auto Populate** for one-click operation.

---

### Step 1: Load Shared Params

**Button**: CREATE > SETUP > **Load Shared Params**

Binds all STING shared parameters to Revit categories so they can hold tag data.

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

### Step 2: [Brain] Smart Tokens

**Button**: CREATE > POPULATE TOKENS > **[Brain] Smart Tokens**

Pre-populates all 7 derivable tokens on every element before tagging. Each token is derived automatically from element context.

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

Or set manually: CREATE > POPULATE TOKENS > **DISC** button.

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

Or set manually: CREATE > POPULATE TOKENS > **LOC** button.

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

Or set manually: CREATE > POPULATE TOKENS > **ZONE** button.

#### LVL — Level Code

Derived from the element's **Revit Level** name:

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

Or set manually: CREATE > POPULATE TOKENS > **LVL** button.

#### SYS — System Type Code

Derived using a **6-layer intelligence stack** (first match wins):

| Layer | Source | What it checks | Example |
|-------|--------|----------------|---------|
| 1 | MEP Connector | Connected system name via connector API | Pipe connected to "Domestic Cold Water" → DCW |
| 2 | System Type Param | Built-in duct/pipe system type parameter | Duct with Supply Air system → HVAC |
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

Or set manually: CREATE > POPULATE TOKENS > **SYS** button.

#### FUNC — Function Code

Derived with sub-system awareness:

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

Or set manually: CREATE > POPULATE TOKENS > **FUNC** button.

#### PROD — Product Code

Inspects the **family name** for 35+ specific product codes before falling back to the category default.

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

Or set manually: CREATE > POPULATE TOKENS > **PROD** button.

---

### Step 3: [Find] Inspect Selection (Optional)

**Button**: CREATE > TOKEN INSPECTOR > **[Find] Inspect Selection**
Also available on ORGANISE tab as: **Pre-Audit**

Performs a complete **dry-run** of the tagging process without writing anything. Predicts:

- What tag each element would receive
- Which elements would collide (duplicate tags)
- Which tokens fail ISO 19650 validation
- How LOC/ZONE were auto-detected
- Family-aware PROD code assignments

Outputs a detailed report to TaskDialog and optionally exports to CSV.

**When to use**: Before running tagging on large projects (1000+ elements) to catch issues early.

---

### Step 4: Tag

**Buttons** (choose one — all on the ORGANISE tab):

| Button | Scope | Best For |
|--------|-------|----------|
| **Auto Tag** | Active view only | Quick tagging of visible elements |
| **Batch Tag** | Entire project | Full project tagging |
| **Tag New** | Untagged elements only | Adding new elements to tagged project |
| **Tag+Combine** | View/selection/project | One-click: populate + tag + combine |

The **Scope: View** / **Overwrite: No** toggles on the CREATE tab header control the behaviour of all tagging commands.

**What happens during tagging**:

1. **Check existing tag**: Read ASS_TAG_1 from element
2. **Collision mode decision** (controlled by **Overwrite** toggle):
   - **Overwrite: No** + element has tag → Skip (leave untouched)
   - **Overwrite: Yes** → Regenerate all tokens and replace tag
   - **AutoIncrement** → If collision found, increment SEQ automatically
3. **Derive all 8 tokens** using the intelligence layers described in Step 2
4. **Generate SEQ number**:
   - Group key = `DISC_SYS_LVL` (e.g., `M_HVAC_L02`)
   - Counter increments within each group
   - Starting value = highest existing SEQ in that group + 1
   - Zero-padded to 4 digits (0001, 0002, ...)
5. **Collision detection**: Check assembled tag against all existing tags in project
   - If collision found: increment SEQ and retry (up to 100 attempts)
   - Record collision depth in stats
6. **Write tokens**: Set all 8 parameter values on the element
7. **Assemble TAG1**: Join tokens with separator → `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`
8. **Write TAG1**: Set `ASS_TAG_1_TXT` with the full tag
9. **Auto-populate STATUS**: Derive from Revit phase (NEW/EXISTING/DEMOLISHED/TEMPORARY)
10. **Auto-populate REV**: Derive from project revision sequence (P01, P02...)
11. **Write all 36 containers**: Automatically propagate to discipline-specific containers
12. **Write TAG7**: Generate rich descriptive narrative (see [TAG7 Creation](#tag7--rich-descriptive-narrative-creation))

#### Sequence Numbering Logic

SEQ numbers are grouped by **DISC + SYS + LVL**:

```
Group: M_HVAC_L02     → AHU: 0001, 0002, 0003; FCU: 0004, 0005
Group: E_LV_L02       → DB: 0001; LUM: 0002, 0003, 0004
Group: P_DCW_GF       → FIX: 0001, 0002
```

Before tagging begins, the engine scans all elements to find the highest SEQ in each group, ensuring new tags continue from where existing numbering left off.

The **Assign Numbers** button on the CREATE tab can also be used to renumber SEQ values independently.

---

### Step 5: Validate

**Button**: CREATE > QUALITY ASSURANCE > **Validate**

Checks every tagged element for ISO 19650 compliance.

**Validation checks**:

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

**Related QA buttons**:
- **Highlight** — visually colour-code elements by tag status (red = missing, orange = incomplete)
- **Clear Art** — remove highlight colour overrides from the view
- **Completeness %** — open the compliance dashboard with RAG percentages
- **Find Duplicates** — locate and select elements with duplicate tag values

---

### Step 6: Combine Parameters

**Button**: CREATE > EXPORT > **Combine Parameters**

Writes the assembled tag into all 36 discipline-specific container parameters.

> **Note**: Step 4 (Auto Tag / Batch Tag / Tag+Combine) automatically writes containers, so a separate **Combine Parameters** step is only needed if tokens were manually edited after tagging using the individual token buttons.

**Pre-flight check**: Use **Pre-Flight** (in QUALITY ASSURANCE) to preview what containers would be written without actually writing.

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

**TAG7 — Rich descriptive narrative** (see [TAG7 Creation](#tag7--rich-descriptive-narrative-creation) section below for full details):

| Parameter | Section | Content |
|-----------|---------|---------|
| ASS_TAG_7_TXT | Full | Complete narrative with markup tokens |
| ASS_TAG_7A_TXT | A: Identity | Asset name, product code, manufacturer, model |
| ASS_TAG_7B_TXT | B: System | System type, function code, serving context |
| ASS_TAG_7C_TXT | C: Spatial | Room, department, grid reference |
| ASS_TAG_7D_TXT | D: Lifecycle | Status, revision, origin, maintenance |
| ASS_TAG_7E_TXT | E: Technical | Capacity, flow, voltage (discipline-specific) + dimensions |
| ASS_TAG_7F_TXT | F: Classification | Uniformat, OmniClass, keynote, cost, ISO tag |

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

**Additional container buttons**:
- **Build Tags** — rebuild TAG1 only from current token values
- **T3 Tags** — build TAG3 (location subset) only
- **Mat Tags** — build material containers (MAT_TAG_1-6) only
- **Build ALL** — build all containers at once

---

## One-Click Automation Commands

### Tag+Combine (Recommended for most users)

**Button**: ORGANISE tab > **Tag+Combine**

What it does in one click:

1. Auto-detect LOC/ZONE from spatial data
2. Populate all 7 derivable tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD)
3. Tag all elements with SEQ assignment and collision detection
4. Combine into all 36 containers
5. Write TAG7 rich narrative
6. Scope controlled by CREATE tab > **Scope: View** toggle

### Auto Populate (Full Automation)

**Button**: CREATE > POPULATE TOKENS > **Auto Populate**
Also on TEMP tab as: **Full AutoPop**

What it does in one click:

1. Token population (all 7 tokens)
2. Dimension mapping (width, height, area, volume from Revit)
3. MEP data mapping (power, flow, voltage from built-in params)
4. Formula evaluation (199 formulas: costs, areas, rates)
5. Tag assembly and container writing
6. Grid/spatial reference population

### Resolve All Issues

**Button**: CREATE > TOKEN INSPECTOR > **Resolve All Issues**

One-click ISO 19650 compliance resolution:
1. Fills missing tokens with guaranteed defaults
2. Fixes duplicate tags by incrementing SEQ
3. Corrects invalid codes to nearest valid match
4. Rebuilds incomplete containers

---

## Collision Handling

When two elements would receive the same tag, STING offers three collision modes:

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| **AutoIncrement** (default) | Increment SEQ until unique tag found | Normal tagging — ensures every tag is unique |
| **Skip** | Leave already-tagged elements untouched | Re-running on a partially tagged project |
| **Overwrite** | Regenerate all tokens and replace existing tag | Fixing incorrect tags; re-tagging after changes |

The **Overwrite: No** / **Overwrite: Yes** toggle on the CREATE tab header controls whether tagging commands skip or overwrite existing tags.

**Collision detection**: Before writing, the tag is checked against all existing tags in the project. On collision:
- SEQ increments (0003 → 0004 → 0005...) up to 100 attempts
- If still colliding after 100 attempts, a warning is logged
- The collision depth is tracked for reporting

---

## Real-Time Auto-Tagging

**Button**: VIEW tab > **Auto-Tagger**

When enabled, newly placed elements are **automatically tagged in real-time** as they are added to the model. The auto-tagger:

1. Monitors 22 taggable categories for element additions
2. Triggers on element placement
3. Runs the full token population + tag assembly pipeline
4. Suppresses redundant triggers via processed element ID tracking

**Performance**: Deduplication clears at 10,000 processed IDs. Designed for interactive use, not batch operations.

---

## Manual Token Overrides

Individual tokens can be set manually using the 13 token buttons in CREATE > POPULATE TOKENS:

| Button | What it sets |
|--------|-------------|
| **DISC** | Discipline code (user picks from M/E/P/A/S/FP/LV/G) |
| **LOC** | Location code (user picks from configured codes) |
| **ZONE** | Zone code (user picks from configured codes) |
| **LVL** | Level code |
| **SYS** | System type code |
| **FUNC** | Function code |
| **PROD** | Product code |
| **SEQ** | Sequence number (same as **Assign Numbers**) |
| **STATUS** | Construction status (EXISTING/NEW/DEMOLISHED/TEMPORARY) |
| **REV** | Revision code |
| **PROJ** | Project code |
| **ORIG** | Origin/source |
| **VOL** | Volume reference |

After setting tokens manually, use **Build Tags** or **Build ALL** to reassemble the tag containers from the updated values.

Manually set tokens are **preserved** by the default tagging mode (Overwrite: No). Only **Overwrite: Yes** replaces manually-set values.

---

## Configuration

### Project Config

**Button**: CREATE > SETUP > **Project Config**

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

### Tag Config

**Button**: CREATE > SETUP > **Tag Config**

Displays the current tag lookup tables (read-only view). To edit, use **Project Config**.

### What you can customise:

- **Add/modify discipline mappings** (e.g., map "Specialty Equipment" to "M" instead of "G")
- **Add/modify system codes** (e.g., add "CHP" for combined heat and power)
- **Change location/zone codes** (e.g., "EAST", "WEST", "NORTH", "SOUTH" instead of BLD1-3)
- **Change tag separator** (e.g., `_` instead of `-`)
- **Change SEQ padding** (e.g., 5 digits for very large projects)
- **Reorder segments** (e.g., put SYS before LOC)

### Schema Management

| Button | What it does |
|--------|-------------|
| **Sync Schema** | Push PARAMETER_REGISTRY.json changes to project parameter bindings |
| **Audit Schema** | Verify all bindings match the registry (reports mismatches) |
| **Add Remap** | Add field deprecation remap (old name → new name) |

---

## QA & Analysis Commands

All in CREATE > QUALITY ASSURANCE:

| Button | What it does |
|--------|-------------|
| **Validate** | ISO 19650 compliance check on all tokens and tag format |
| **Highlight** | Colour-code: red = missing tag, orange = incomplete tag |
| **Clear Art** | Reset graphic overrides in active view |
| **Pre-Flight** | Dry-run combine check (preview without writing) |
| **Completeness %** | Per-discipline compliance dashboard with RAG status |
| **Find Duplicates** | Locate elements sharing the same tag value |

Additional in CREATE > ISO COMPLETENESS DASHBOARD:

| Button | What it does |
|--------|-------------|
| **Load** | Scan project and populate dashboard grid |
| **Export** | Export dashboard data to CSV |

Additional in CREATE > EXPORT:

| Button | What it does |
|--------|-------------|
| **Export Tag Register** | Comprehensive 40+ column asset register CSV |

Additional on ORGANISE tab:

| Button | What it does |
|--------|-------------|
| **Stats** | Quick tag counts by discipline/system/level |
| **AuditCSV** | Export full tag audit to CSV file |
| **Fix Dups** | Auto-resolve duplicate tags by incrementing SEQ |

---

## Quick Reference: Which Button to Use

| Situation | Button | Tab |
|-----------|--------|-----|
| First time setting up a project | **Load Shared Params** | CREATE > SETUP |
| First time tagging a project | **Tag+Combine** | ORGANISE |
| Complete automation (tags + data + formulas) | **Auto Populate** | CREATE > POPULATE TOKENS |
| Adding new elements to tagged project | **Tag New** | ORGANISE |
| Re-tagging after design changes | **Auto Tag** + Overwrite: Yes | ORGANISE + CREATE header |
| Full project re-tag from scratch | **Batch Tag** + Overwrite: Yes | ORGANISE + CREATE header |
| Check before committing tags | **[Find] Inspect Selection** | CREATE > TOKEN INSPECTOR |
| Verify tag quality after tagging | **Validate** | CREATE > QA |
| View compliance percentages | **Completeness %** or **Load** | CREATE > QA / DASHBOARD |
| Fix all issues at once | **Resolve All Issues** | CREATE > TOKEN INSPECTOR |
| Zero-touch real-time tagging | **Auto-Tagger** | VIEW |
| Export asset register | **Export Tag Register** | CREATE > EXPORT |
| Manually edit then rebuild tags | Token buttons + **Build ALL** | CREATE > POPULATE TOKENS |

---

## TAG7 — Rich Descriptive Narrative Creation

TAG7 is a comprehensive human-readable asset narrative that describes each element in natural language. Unlike TAG1-TAG6 which are structured code strings, TAG7 produces flowing prose suitable for asset registers, FM handover documents, and rich tag annotations.

### Parameters

| Parameter | Storage | Content |
|-----------|---------|---------|
| `ASS_TAG_7_TXT` | Marked-up full narrative | Contains `«H»`/`«L»`/`«V»` markup tokens for rich rendering |
| `ASS_TAG_7A_TXT` | Plain text — Section A | Identity Header |
| `ASS_TAG_7B_TXT` | Plain text — Section B | System & Function |
| `ASS_TAG_7C_TXT` | Plain text — Section C | Spatial Context |
| `ASS_TAG_7D_TXT` | Plain text — Section D | Lifecycle & Status |
| `ASS_TAG_7E_TXT` | Plain text — Section E | Technical Specifications |
| `ASS_TAG_7F_TXT` | Plain text — Section F | Classification & Reference |

TAG7 (the main parameter) holds the **marked-up** narrative with embedded formatting tokens. TAG7A-TAG7F hold **plain text** sections for use in tag family labels (which cannot render markup).

### When TAG7 is Written

TAG7 is written automatically during Step 4 (tagging) as part of **Auto Tag**, **Batch Tag**, **Tag+Combine**, and **Auto Populate**. After assembling the 8-segment tag and writing TAG1-TAG6, the tagging engine generates all 6 sections and writes them.

### Section A: Identity Header

**Colour**: Blue (`#1565C0`) | **Style**: Bold + Underline

Builds an asset identity statement from:

| Source Parameter | Used For | Example |
|------------------|----------|---------|
| DISC token | Discipline description lookup | "M" → "Mechanical" |
| PROD token | Product description lookup | "AHU" → "Air Handling Unit" |
| Category name | Fallback if no PROD description | "Mechanical Equipment" |
| `ASS_MFR_TXT` | Manufacturer name | "Daikin" |
| `ASS_MODEL_TXT` | Model number | "FXS125" |
| `ASS_FAMILY_NAME_TXT` | Family name (if no mfr/model) | "M_FCU_Horizontal" |
| `ASS_TYPE_NAME_TXT` | Type configuration | "1200mm - 4 Pipe" |
| `ASS_DESC_TXT` | Description | "Ceiling-mounted FCU" |
| `ASS_SIZE_TXT` | Size specification | "600x400mm" |

**Example output**:
> Mechanical Air Handling Unit (AHU) manufactured by Daikin Model FXS125, sized at 600x400mm

**Without manufacturer data**:
> Mechanical Air Handling Unit (AHU) from the M_AHU_Horizontal family configured as 2500 L/s

### Section B: System & Function Context

**Colour**: Green (`#2E7D32`) | **Style**: Italic

Describes the engineering system context using description lookup tables:

| Code | System Description | Function Description |
|------|--------------------|----------------------|
| HVAC | Heating Ventilation and Air Conditioning | SUP → Supply, RTN → Return, EXH → Exhaust, FRA → Fresh Air Intake |
| DCW | Domestic Cold Water | DCW → Domestic Cold Water Distribution |
| HWS | Hot Water Supply | HTG → Heating, DHW → Domestic Hot Water Distribution |
| SAN | Sanitary Drainage | SAN → Sanitary Waste Disposal |
| LV | Low Voltage Distribution | PWR → Power Distribution, LTG → Lighting |
| FP | Fire Protection | FP → Fire Protection Suppression |
| FLS | Fire Life Safety | FLS → Fire Detection and Alarm |
| COM | Communications | COM → Voice and Data Communications |
| ICT | Information and Communications Technology | ICT → Data Network and Infrastructure |
| SEC | Security Systems | SEC → Security and Access Control |
| NCL | Nurse Call Systems | NCL → Patient Nurse Call |
| ARC | Architectural Fabric | FIT → Finishes and Fitout |
| STR | Structural Elements | STR → Primary Structure |

Includes the serving context: zone, level, and building from the element's token values.

**Example output**:
> Heating Ventilation and Air Conditioning providing Supply serving Zone Z01 on Level L02 within Building BLD1

### Section C: Spatial Context

**Colour**: Orange (`#E65100`) | **Style**: Normal

Built from room and spatial data parameters:

| Source Parameter | Used For | Example |
|------------------|----------|---------|
| `ASS_ROOM_NAME_TXT` | Room name | "Office 201" |
| `ASS_ROOM_NUM_TXT` | Room number | "2.01" |
| `ASS_DEPT_TXT` | Department | "Engineering" |
| `ASS_GRID_REF_TXT` | Grid reference | "C4-D5" |
| `BLE_ROOM_NAME_TXT` | BLE room name (fallback) | Used if ASS_ROOM_NAME is empty |
| `BLE_ROOM_NUM_TXT` | BLE room number (fallback) | Used if ASS_ROOM_NUM is empty |

**Example output**:
> Located in Office 201 (Room 2.01) within the Engineering department near grid reference C4-D5

### Section D: Lifecycle & Status

**Colour**: Red (`#C62828`) | **Style**: Normal

Built from lifecycle and project context parameters:

| Source Parameter | Used For | Example |
|------------------|----------|---------|
| `ASS_STATUS_TXT` | Construction status | "NEW", "EXISTING" |
| `ASS_REV_TXT` | Revision code | "P02" |
| `ASS_ORIGIN_TXT` | Origin/source | "Design Intent" |
| `ASS_PROJECT_TXT` | Project name | "Hospital Phase 2" |
| `ASS_VOLUME_TXT` | Volume reference | "Vol 3" |
| `ASS_MNT_TYPE_TXT` | Maintenance type | "Planned Preventative" |
| `ASS_DETAIL_NUM_TXT` | Detail reference | "D-M-101" |

**Example output**:
> This element is new, currently at revision P02, originating from Design Intent within project Hospital Phase 2. Requires planned preventative maintenance, see detail D-M-101

### Section E: Technical Specifications

**Colour**: Purple (`#6A1B9A`) | **Style**: Bold

Technical data is **discipline-specific** — the builder reads different parameters based on the element's discipline and category:

#### Electrical Equipment / Fixtures (DISC = E)

| Parameter | Narrative Template | Unit |
|-----------|--------------------|------|
| `ELC_POWER_TXT` | "rated at {0} kW" | kW |
| `ELC_VOLTAGE_TXT` | "operating at {0} V" | V |
| `ELC_CIRCUIT_NR_TXT` | "connected to circuit {0}" | — |
| `ELC_PNL_NAME_TXT` | "supplied by panel {0}" | — |
| `ELC_PHASES_TXT` | "configured for {0} phase supply" | — |
| `ELC_PNL_FED_FROM_TXT` | "fed from {0}" | — |
| `ELC_MAIN_BRK_TXT` | "protected by a {0} A main breaker" | A |
| `ELC_WAYS_TXT` | "with {0} ways" | — |
| `ELC_IP_RATING_TXT` | "sealed to IP {0}" | — |
| `ELC_PNL_LOAD_TXT` | "carrying a connected load of {0} kW" | kW |

#### Lighting Fixtures / Devices

| Parameter | Narrative Template | Unit |
|-----------|--------------------|------|
| `LTG_WATTAGE_TXT` | "consuming {0} W" | W |
| `LTG_LUMENS_TXT` | "delivering {0} lm of luminous output" | lm |
| `LTG_EFFICACY_TXT` | "achieving an efficacy of {0} lm/W" | lm/W |
| `LTG_LAMP_TYPE_TXT` | "using a {0} lamp" | — |
| `ELC_CIRCUIT_NR_TXT` | "wired to circuit {0}" | — |

#### Mechanical / HVAC (DISC = M, Ducts, Air Terminals)

| Parameter | Narrative Template | Unit |
|-----------|--------------------|------|
| `HVC_AIRFLOW_TXT` | "delivering an airflow of {0} L/s" | L/s |
| `HVC_DUCT_FLOW_TXT` | "with a duct flow of {0} CFM" | CFM |
| `HVC_VELOCITY_TXT` | "at a velocity of {0} m/s" | m/s |
| `HVC_PRESSURE_TXT` | "against a pressure drop of {0} Pa" | Pa |

#### Plumbing (DISC = P, Pipes, Plumbing Fixtures)

| Parameter | Narrative Template | Unit |
|-----------|--------------------|------|
| `PLM_PIPE_FLOW_TXT` | "conveying a flow of {0} L/s" | L/s |
| `PLM_PIPE_SIZE_TXT` | "through {0} mm diameter pipework" | mm |
| `PLM_VELOCITY_TXT` | "at a velocity of {0} m/s" | m/s |
| `PLM_FLOW_RATE_TXT` | "with a design flow rate of {0} L/s" | L/s |
| `PLM_PIPE_LENGTH_TXT` | "running {0} m in length" | m |

#### Fire Protection (DISC = FP, Sprinklers, Fire Alarm Devices)

| Parameter | Narrative Template | Unit |
|-----------|--------------------|------|
| `FIRE_RATING_TXT` | "providing {0} minutes of fire resistance" | min |

#### Dimensional Data (BLE categories)

After the discipline-specific data, Section E appends dimensional properties based on category:

| Category | Parameters Read | Example Output |
|----------|----------------|----------------|
| Walls | Height, Length, Thickness, Area, Fire Rating, Structural Type | "standing 3000 mm high, spanning 5400 mm in length, with a thickness of 200 mm" |
| Doors | Width, Height, Fire Rating | "measuring 900 mm wide, by 2100 mm high, with 60 minutes of fire resistance" |
| Windows | Width, Height, Sill Height | "measuring 1200 mm wide, by 1500 mm high, set at a sill height of 900 mm" |
| Floors | Thickness, Area, Structural Type, Fire Rating | "with a build-up of 350 mm thick, covering an area of 45 m²" |
| Ceilings | Height, Area | "suspended at 2700 mm above floor level, covering an area of 30 m²" |
| Roofs | Slope, Area | "pitched at 15 degrees, covering an area of 200 m²" |
| Stairs | Tread, Rise, Width | "with treads 280 mm deep, risers of 170 mm, and a clear width of 1200 mm" |
| Ramps | Slope, Width | "inclined at 8%, with a clear width of 1500 mm" |

### Section F: Classification & Reference

**Colour**: Grey (`#37474F`) | **Style**: Italic

Built from classification and cost parameters:

| Source Parameter | Narrative Template |
|------------------|--------------------|
| `ASS_UNIFORMAT_TXT` | "Uniformat code {0}" |
| `ASS_UNIFORMAT_DESC_TXT` | Appended as "({description})" |
| `ASS_OMNICLASS_TXT` | "with OmniClass reference {0}" |
| `ASS_KEYNOTE_TXT` | "keynote {0}" |
| `ASS_TYPE_MARK_TXT` | "identified as type mark {0}" |
| `ASS_COST_TXT` | "with an estimated unit cost of {0}" |

The ISO 19650 tag is always appended at the end:
> Assigned ISO 19650 tag M-BLD1-Z01-L02-HVAC-SUP-AHU-0003

### Narrative Assembly

The 6 sections are joined into a single flowing paragraph with natural-language connectors:

| From → To | Connector |
|-----------|-----------|
| A → B | ". This asset operates within the " |
| B → C | ". It is " (Section C starts with "located in...") |
| C → D | ". Regarding its lifecycle, " |
| D → E | ". Technical specifications include " |
| E → F | ". Classified under " |

**Full example narrative** (for a distribution board):

> **Electrical Distribution Board (DB)** manufactured by Schneider Electric Model Prisma P — *This asset operates within the* **Low Voltage Distribution** *providing* Power Distribution *serving* Zone Z01 *on* Level L02 *within* Building BLD1. *It is* located in Plant Room (Room 0.12) within the Electrical department near grid reference B3-C3. *Regarding its lifecycle,* this element is new, currently at revision P01. *Technical specifications include* rated at 125 kW, operating at 415 V, configured for 3 phase supply, protected by a 400 A main breaker, with 48 ways. *Classified under* Uniformat code D5010 (Electrical Service and Distribution), keynote 260000, with an estimated unit cost of £12,500. Assigned ISO 19650 tag E-BLD1-Z01-L02-LV-PWR-DB-0001

### Markup Tokens

TAG7 (the main parameter) embeds formatting tokens that are parsed by rich rendering surfaces:

| Token | Meaning | TextNote Rendering | WPF Rendering | HTML Rendering |
|-------|---------|-------------------|---------------|----------------|
| `«H»text«/H»` | Header / emphasis | Bold + Underline | Bold | `<strong>` |
| `«L»text«/L»` | Label text | Italic | Muted colour | `<em>` |
| `«V»text«/V»` | Value text | Normal weight | Accent colour | Highlighted `<span>` |
| `«S»text«/S»` | Section separator | Normal | Normal | Normal |

TAG7A-TAG7F sub-parameters hold **plain text only** (no markup) for use in Revit tag family labels via the Edit Label dialog.

### Display Presets

TAG7 rendering can be styled per-element using display presets. Each preset maps a discriminator value to a colour scheme:

| Preset | Discriminator | Example Styles |
|--------|--------------|----------------|
| **Discipline** (default) | DISC code | M=Blue, E=Yellow, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple |
| **Status** | STATUS value | NEW=Green, EXISTING=Blue, DEMOLISHED=Red, TEMPORARY=Orange |
| **System** | SYS code | HVAC=Blue, DCW=Cyan, HWS=Red, SAN=Brown, LV=Yellow, FP=Orange |
| **Compliance** | Tag completeness | COMPLETE=Green, PARTIAL=Yellow, INCOMPLETE=Red |
| **Monochrome** | — | All elements: Black header on light grey |
| **Accessible** | DISC code | Colorblind-safe palette (viridis-like) |
| **Engineering Review** | — | All elements: Purple header on light purple |

Each style defines: `HeaderColor`, `BackgroundTint`, `Label`, and `SectionColors[A-F]`.

### Paragraph Depth Control

**Button**: CREATE > PARAGRAPH & PRESENTATION > **Paragraph Depth**

TAG7 tag family labels support **paragraph depth** — controlling how much detail is visible in annotation tags:

| Depth | State Parameters | Content Shown |
|-------|-----------------|---------------|
| **Compact** (State 1) | PARA_STATE_1=Yes | Tier 1 only — basic identity and dimensions |
| **Standard** (State 2) | PARA_STATE_1+2=Yes | Tiers 1+2 — adds materials, thermal, acoustic data |
| **Comprehensive** (State 3) | PARA_STATE_1+2+3=Yes | Tiers 1+2+3 — full specification with regulatory, sustainability, QA |

These are **Type parameters** (`TAG_PARA_STATE_1_BOOL` through `TAG_PARA_STATE_10_BOOL`) — all instances of the same type change together.

**Related buttons**:
- **Toggle Warnings** — show/hide threshold warning text in tags (e.g., `[!U > 0.70]`, `[!VD > 4%]`)
- **TAG7 Heading Style** — configure section heading appearance

### Presentation Modes

**Button**: CREATE > PARAGRAPH & PRESENTATION > **Presentation Mode**

Presentation modes combine paragraph depth + warning visibility into named presets for one-click switching:

| Mode | States | Warnings | Use Case |
|------|--------|----------|----------|
| **Compact** | 1 only | OFF | Quick labels, drawing title blocks |
| **Technical** | 1+2 | ON | Engineering documentation, design review |
| **Full Specification** | 1+2+3 | ON | Detail sheets, specifications |
| **Presentation** | 1+2 | OFF | Client presentations (clean, no warnings) |
| **BOQ** | 1+2 | OFF | Cost schedules and quantity extraction |

**Related buttons**:
- **View Label Spec** — display current label specification for tag families
- **Export Label Guide** — export label configuration as reference document

---

## Data Files Referenced

| File | Purpose |
|------|---------|
| `PARAMETER_REGISTRY.json` | Master source of truth: token names, GUIDs, containers, tag format |
| `MR_PARAMETERS.txt` | Revit shared parameter definitions (200+ params, 18 groups) |
| `CATEGORY_BINDINGS.csv` | 10,661 parameter-to-category bindings for discipline-specific binding |
| `TAG_GUIDE_V3.csv` | Tag format specification, validation rules, auto-population logic |
| `LABEL_DEFINITIONS.json` | 3,623-line label/legend definition specs for tag display styles |
| `project_config.json` | Per-project tag configuration overrides (user-editable) |
