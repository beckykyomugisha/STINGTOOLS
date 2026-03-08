# STING Tools — Tag Creation & Tagging Process Guide

> Comprehensive reference for the ISO 19650-compliant asset tagging pipeline in StingTools.

---

## Table of Contents

1. [Tag Format Overview](#1-tag-format-overview)
2. [The 8 Token Segments](#2-the-8-token-segments)
3. [Token Derivation Logic](#3-token-derivation-logic)
4. [Tag Containers (36 Parameters)](#4-tag-containers-36-parameters)
5. [TAG7 — Rich Descriptive Narrative](#5-tag7--rich-descriptive-narrative)
6. [Tagging Pipeline — Step by Step](#6-tagging-pipeline--step-by-step)
7. [One-Click Workflows](#7-one-click-workflows)
8. [Collision Handling & Sequence Numbers](#8-collision-handling--sequence-numbers)
9. [Real-Time Auto-Tagging (IUpdater)](#9-real-time-auto-tagging-iupdater)
10. [Validation & QA](#10-validation--qa)
11. [Configuration & Customization](#11-configuration--customization)
12. [Lookup Tables Reference](#12-lookup-tables-reference)
13. [Data Files Involved](#13-data-files-involved)
14. [Source Code Reference](#14-source-code-reference)

---

## 1. Tag Format Overview

Every asset in a Revit model receives an **8-segment ISO 19650-compliant tag** that uniquely identifies it:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
 M   - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

This tag is stored in the shared parameter `ASS_TAG_1_TXT` and is also written to up to 36 discipline-specific container parameters for use in schedules, labels, and IFC export.

**Key properties:**
- **Separator**: `-` (configurable via `project_config.json`)
- **SEQ padding**: 4 digits (configurable, e.g. `0001`–`9999`)
- **Segment order**: Configurable — default is DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ
- **Every token is guaranteed a valid value** — no segment is ever left blank

---

## 2. The 8 Token Segments

| # | Segment | Parameter Name | Example Values | Description |
|---|---------|----------------|----------------|-------------|
| 0 | **DISC** | `ASS_DISCIPLINE_COD_TXT` | M, E, P, A, S, FP, LV, G | Discipline code per ISO 19650 |
| 1 | **LOC** | `ASS_LOC_TXT` | BLD1, BLD2, BLD3, EXT | Building/location identifier |
| 2 | **ZONE** | `ASS_ZONE_TXT` | Z01, Z02, Z03, Z04 | Zone within building |
| 3 | **LVL** | `ASS_LVL_COD_TXT` | GF, L01, L02, B1, RF | Level code derived from Revit level name |
| 4 | **SYS** | `ASS_SYSTEM_TYPE_TXT` | HVAC, DCW, DHW, HWS, SAN, LV, FLS | System type (CIBSE / Uniclass 2015) |
| 5 | **FUNC** | `ASS_FUNC_TXT` | SUP, RTN, EXH, HTG, PWR, FIT | Function code (CIBSE / Uniclass 2015) |
| 6 | **PROD** | `ASS_PRODCT_COD_TXT` | AHU, DB, DR, LUM, PP, WL | Product/component code |
| 7 | **SEQ** | `ASS_SEQ_NUM_TXT` | 0001, 0042, 0815 | 4-digit sequence number (unique per group) |

**Additional tokens** (written to elements but not part of TAG1):

| Parameter | Example | Description |
|-----------|---------|-------------|
| `ASS_STATUS_TXT` | NEW, EXISTING, DEMOLISHED, TEMPORARY | Element lifecycle status (from Revit phase) |
| `ASS_REV_TXT` | P01 | Project revision code |

---

## 3. Token Derivation Logic

Each token is derived through a multi-layer intelligence system. The derivation runs automatically — users do not need to set any values manually.

### 3.1 DISC — Discipline Code

**Source:** Element's Revit category → `TagConfig.DiscMap` lookup

| Discipline | Code | Categories |
|------------|------|------------|
| Mechanical | **M** | Air Terminals, Ducts, Duct Fittings, Duct Accessories, Flex Ducts, Mechanical Equipment, Pipes*, Pipe Fittings*, Pipe Accessories*, Flex Pipes* |
| Electrical | **E** | Electrical Equipment, Electrical Fixtures, Lighting Fixtures, Lighting Devices, Conduits, Conduit Fittings, Cable Trays, Cable Tray Fittings |
| Plumbing | **P** | Plumbing Fixtures |
| Architectural | **A** | Doors, Windows, Walls, Floors, Ceilings, Roofs, Rooms, Furniture, Stairs, Ramps, Railings, Casework, Curtain systems |
| Structural | **S** | Structural Columns, Structural Framing, Structural Foundations, Columns |
| Fire Protection | **FP** | Sprinklers, Fire Alarm Devices |
| Low Voltage | **LV** | Communication Devices, Data Devices, Nurse Call Devices, Security Devices, Telephone Devices |
| General | **G** | Generic Models, Specialty Equipment, Medical Equipment |

> *\*Pipes are M (Mechanical) by default, but **auto-corrected to P (Plumbing)** at runtime if connected to a plumbing system (DCW, DHW, SAN, RWD, GAS).*

**Default:** `"A"` (Architectural) if category is not in DiscMap.

**Code:** `TagConfig.cs:1047–1079` — `BuildAndWriteTag` derives DISC and applies pipe correction.

### 3.2 LOC — Location Code

**Source:** Multi-layer spatial detection via `SpatialAutoDetect.DetectLoc()`

**Detection priority (first match wins):**

1. **Room name patterns** — Element's host room name checked for: `BLD1/BLD2/BLD3`, `Building 1/2/3`, `Block A/B/C`, `EXT/EXTERNAL/EXTERIOR`
2. **Room number prefix** — e.g., room number `"B1-101"` → `BLD1`
3. **Exterior heuristic** — If element has no room but project has rooms, check family/category names for `EXTERNAL`, `EXTERIOR`, `OUTDOOR`, `BOLLARD`, `FLOODLIGHT` → `EXT`
4. **Workset name** — ISO 19650-2 AEC workset naming like `"M-BLD1-Mechanical"` → extract `BLD1`
5. **Project Information** — `DetectProjectLoc()` scans project info for building identifiers
6. **Default:** `"BLD1"`

**Valid codes:** BLD1, BLD2, BLD3, EXT (configurable via `project_config.json`)

**Code:** `ParameterHelpers.cs:413–530` — `SpatialAutoDetect.DetectLoc()`, `ParseLocCode()`

### 3.3 ZONE — Zone Code

**Source:** Multi-layer spatial detection via `SpatialAutoDetect.DetectZone()`

**Detection priority (first match wins):**

1. **Room Department parameter** — Checked for zone patterns (Z01–Z04, Zone 1–4, Wing A–D)
2. **Room name patterns** — Z01–Z04, directional terms (North→Z01, South→Z02, East→Z03, West→Z04)
3. **Room number prefix** — Zone code embedded in room number
4. **Workset name** — Zone extracted from ISO workset naming
5. **Default:** `"Z01"`

**Valid codes:** Z01, Z02, Z03, Z04 (configurable)

**Code:** `ParameterHelpers.cs:468–558` — `SpatialAutoDetect.DetectZone()`, `ParseZoneCode()`

### 3.4 LVL — Level Code

**Source:** Element's associated Revit level → `ParameterHelpers.GetLevelCode()`

**Parsing rules:**

| Level Name Pattern | Code | Examples |
|-------------------|------|---------|
| Ground/G/GF | **GF** | "Ground Floor", "Level G", "GF" |
| Basement/B + number | **B1**, **B2** | "Basement 1", "Level B1" |
| Roof/RF | **RF** | "Roof Level", "RF" |
| Number extraction | **L01**, **L02** | "Level 1", "First Floor", "02 - Floor" |
| No level found | **L00** | Levelless elements (e.g., linked, unhosted) |

**Default:** `"L00"` for elements with no associated level.

**Code:** `ParameterHelpers.cs` — `GetLevelCode()`

### 3.5 SYS — System Type Code

**Source:** 6-layer MEP-aware detection via `TagConfig.GetMepSystemAwareSysCode()`

**Detection priority (first match wins):**

1. **MEP Connector graph analysis** — Follows piping/ductwork connectors to find connected system name (most reliable)
2. **System type built-in parameter** — `RBS_DUCT_SYSTEM_TYPE`, `RBS_PIPING_SYSTEM_TYPE`
3. **Electrical circuit panel** — Circuit analysis for electrical elements
4. **Family name pattern matching** — e.g., "Exhaust Fan" → HVAC, "Fire Sprinkler" → FP
5. **Room type inference** — e.g., Server Room → ICT, Kitchen → SAN
6. **Category-based fallback** — `TagConfig.SysMap` reverse lookup

**Valid codes:** HVAC, HWS, DHW, DCW, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN

**Default:** Discipline-appropriate default (M→HVAC, E→LV, P→DCW, A→ARC, S→STR, others→GEN)

**Code:** `TagConfig.cs:1068–1074`, `GetMepSystemAwareSysCode()`

### 3.6 FUNC — Function Code

**Source:** System-aware lookup via `TagConfig.GetSmartFuncCode()`

**Intelligence layer:**
- **HVAC subsystem differentiation** — Detects supply vs return vs exhaust vs fresh air from fan direction, duct system type, or element name:
  - Supply → `SUP`
  - Return → `RTN`
  - Exhaust → `EXH`
  - Fresh Air → `FRA`
- **Hot Water System** — Differentiates heating (HTG) from domestic hot water (DHW)
- **Other systems** — Direct lookup via `FuncMap`

| System | Default Function | Description |
|--------|-----------------|-------------|
| HVAC | SUP | Supply (overridden by smart detection) |
| HWS | HTG | Heating |
| DHW | DHW | Domestic Hot Water |
| DCW | DCW | Domestic Cold Water |
| SAN | SAN | Sanitary Drainage |
| RWD | RWD | Rainwater Drainage |
| GAS | GAS | Gas Supply |
| FP | FP | Fire Protection |
| LV | PWR | Power |
| FLS | FLS | Fire Life Safety |
| COM | COM | Communications |
| ICT | ICT | Information & Communications Tech |
| NCL | NCL | Nurse Call |
| SEC | SEC | Security |
| ARC | FIT | Fit-out (Architectural) |
| STR | STR | Structural |
| GEN | GEN | General |

**Default:** `"GEN"`

**Code:** `TagConfig.cs:1081–1085`, `FuncMap`, `GetSmartFuncCode()`

### 3.7 PROD — Product Code

**Source:** Family-name-aware detection via `TagConfig.GetFamilyAwareProdCode()`

**2-layer resolution:**

1. **Family name pattern matching (35+ codes)** — Inspects the Revit family name for keywords:

   | Pattern | Code | Examples |
   |---------|------|---------|
   | Fan Coil | FCU | "Fan Coil Unit 4-Pipe" |
   | VAV | VAV | "VAV Box Single Duct" |
   | Roof Top | RTU | "Roof Top Unit" |
   | Distribution Board | DB | "DB 3-Phase" |
   | Mini Circuit Breaker | MCB | "MCB Type B" |
   | Downlight / Recessed | REC | "LED Recessed Downlight" |
   | Pendant | PENDANT | "Pendant Light 600mm" |
   | Toilet / WC | TOILET | "WC Suite" |
   | Detector / Smoke | DETECTOR | "Optical Smoke Detector" |
   | ... | ... | (35+ patterns across M, E, P, FP, LV) |

2. **Category-based fallback** — `TagConfig.ProdMap` lookup:

   | Category | Code |
   |----------|------|
   | Air Terminals | GRL |
   | Ducts | DU |
   | Mechanical Equipment | AHU |
   | Pipes | PP |
   | Electrical Equipment | DB |
   | Lighting Fixtures | LUM |
   | Doors | DR |
   | Windows | WIN |
   | Walls | WL |
   | ... | (41 categories mapped) |

**Default:** `"GEN"`

**Code:** `TagConfig.cs`, `GetFamilyAwareProdCode()`

### 3.8 SEQ — Sequence Number

**Source:** Auto-incremented counter per `(DISC, SYS, LVL)` group

**Rules:**
- Sequence counters are grouped by the combination of discipline, system type, and level
- Before tagging begins, `GetExistingSequenceCounters()` scans the project for the highest existing SEQ in each group
- New elements continue from `max_existing + 1`
- Padded to 4 digits: `0001`, `0002`, ..., `9999`
- **Overflow guard:** Warning issued when SEQ exceeds 9999

**Example:** If the project already has mechanical HVAC elements on Level 01 numbered up to 0042, the next element gets `0043`.

**Code:** `TagConfig.cs:1101–1115`

### 3.9 STATUS — Lifecycle Status (bonus token)

**Source:** `PhaseAutoDetect.DetectStatus()` — derived from Revit element phase data

| Revit Phase | STATUS |
|-------------|--------|
| New Construction | NEW |
| Existing | EXISTING |
| Demolished | DEMOLISHED |
| Temporary | TEMPORARY |

**Default:** `"NEW"`

### 3.10 REV — Revision (bonus token)

**Source:** `PhaseAutoDetect.DetectProjectRevision()` — from Project Information

**Default:** `"P01"`

---

## 4. Tag Containers (36 Parameters)

Once the 8 tokens are populated and assembled into TAG1, the same token values are written to up to **36 container parameters** for use in discipline-specific schedules, tag families, and IFC export.

### 4.1 Universal Containers (applied to all categories)

| Parameter | Content |
|-----------|---------|
| `ASS_TAG_1_TXT` | Full 8-segment tag: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003` |
| `ASS_TAG_2_TXT` | Full 8-segment tag (duplicate for alternate schedules) |
| `ASS_TAG_3_TXT` | Multi-line format (top line) |
| `ASS_TAG_4_TXT` | Multi-line format (middle line) |
| `ASS_TAG_5_TXT` | Multi-line format (bottom line) |
| `ASS_TAG_6_TXT` | Alternate multi-line format |

### 4.2 HVAC Containers

| Parameter | Applied To |
|-----------|-----------|
| `HVC_EQP_TAG` | Mechanical Equipment, Air Terminals |
| `HVC_DCT_TAG` | Ducts, Duct Fittings, Duct Accessories, Flex Ducts |
| `HVC_FLX_TAG` | Flex Ducts |

### 4.3 Electrical Containers

| Parameter | Applied To |
|-----------|-----------|
| `ELC_EQP_TAG` | Electrical Equipment |
| `ELE_FIX_TAG` | Electrical Fixtures |
| `LTG_FIX_TAG` | Lighting Fixtures, Lighting Devices |
| `ELC_CDT_TAG` | Conduits, Conduit Fittings |
| `ELC_CTR_TAG` | Cable Trays, Cable Tray Fittings |

### 4.4 Plumbing Container

| Parameter | Applied To |
|-----------|-----------|
| `PLM_EQP_TAG` | Plumbing Fixtures, Pipes, Pipe Fittings, Pipe Accessories |

### 4.5 Fire/Safety Container

| Parameter | Applied To |
|-----------|-----------|
| `FLS_DEV_TAG` | Fire Alarm Devices, Sprinklers |

### 4.6 Comms/Low Voltage Containers

| Parameter | Applied To |
|-----------|-----------|
| `COM_DEV_TAG` | Communication Devices, Telephone Devices |
| `SEC_DEV_TAG` | Security Devices |
| `NCL_DEV_TAG` | Nurse Call Devices |
| `ICT_DEV_TAG` | Data Devices |

### 4.7 Material Containers

| Parameter | Applied To |
|-----------|-----------|
| `MAT_TAG_1` – `MAT_TAG_6` | Material elements |

### How Containers Work

Each container has a **token index array** defining which segments to include and in what order. The `ParamRegistry.AssembleContainer()` method reads the token values and joins them with the separator.

```
Element tokens: [M, BLD1, Z01, L02, HVAC, SUP, AHU, 0003]
Container indices: [0,1,2,3,4,5,6,7]  →  "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"
Container indices: [0,4,5,6,7]         →  "M-HVAC-SUP-AHU-0003" (short form)
```

Container definitions are loaded from `PARAMETER_REGISTRY.json` and applied per element category via `ParamRegistry.ContainersForCategory()`.

**Code:** `ParamRegistry.cs:1338–1394` — `AssembleContainer()`, `WriteContainers()`

---

## 5. TAG7 — Rich Descriptive Narrative

TAG7 is a comprehensive human-readable tag stored across 7 parameters (`ASS_TAG_7_TXT` + `ASS_TAG_7A_TXT` through `ASS_TAG_7F_TXT`). It provides full context for asset management systems.

| Parameter | Section | Content | Color |
|-----------|---------|---------|-------|
| `ASS_TAG_7A_TXT` | **A: Identity** | Asset name, product code, manufacturer, model | Blue |
| `ASS_TAG_7B_TXT` | **B: System** | System type description, function code | Green |
| `ASS_TAG_7C_TXT` | **C: Spatial** | Room, department, grid reference | Orange |
| `ASS_TAG_7D_TXT` | **D: Lifecycle** | Status, revision, origin, maintenance | Red |
| `ASS_TAG_7E_TXT` | **E: Technical** | Discipline-specific specs (capacity, flow, voltage) | Purple |
| `ASS_TAG_7F_TXT` | **F: Classification** | Uniformat, OmniClass, keynote, cost, ISO tag | Grey |
| `ASS_TAG_7_TXT` | **Full** | All sections combined with pipe separators | Multi |

TAG7 uses `|` pipe separators between sections and supports **paragraph depth control** via `TAG_PARA_STATE_1/2/3_BOOL` parameters (for expandable/collapsible display).

**Presentation modes:** Compact, Technical, Full Specification, Presentation, BOQ — configurable via `LABEL_DEFINITIONS.json`.

**Code:** `TagConfig.cs:2044+` — TAG7 narrative builder, `WriteTag7All()`

---

## 6. Tagging Pipeline — Step by Step

The recommended workflow proceeds through 6 phases. Each can be run independently, but the order ensures data completeness.

### Phase 1: Setup — Load Shared Parameters

**Command:** `LoadSharedParamsCommand`
**File:** `Tags/LoadSharedParamsCommand.cs`

Binds 200+ shared parameters from `MR_PARAMETERS.txt` to 54 Revit categories in 2 passes:

- **Pass 1 (Universal):** Binds ASS_MNG parameters (tags, tokens, status, etc.) to ALL 54 categories including Materials
- **Pass 2 (Discipline):** Binds discipline-specific tag containers (HVC_EQP_TAG, ELC_EQP_TAG, etc.) to their correct category subsets, using both JSON registry and `CATEGORY_BINDINGS.csv` (10,661 entries)

**Must run first** — all subsequent commands depend on these parameters being bound.

### Phase 2: Pre-Population — Fill Token Values

**Command:** `FamilyStagePopulateCommand`
**File:** `Tags/FamilyStagePopulateCommand.cs`

Pre-populates all 7 primary tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD) from:
- Category data (DISC, SYS, PROD)
- Spatial data (LOC, ZONE via room detection)
- Level data (LVL)
- Family name intelligence (PROD override)
- MEP system connections (SYS override)

**Non-destructive:** Only fills empty values — never overwrites existing user data.

Uses `TokenAutoPopulator.PopulateAll()` internally.

### Phase 3: Dry-Run Audit (Optional)

**Command:** `PreTagAuditCommand`
**File:** `Tags/PreTagAuditCommand.cs`

Performs a complete dry-run **without modifying any data**:
- Predicts what tags would be assigned to each element
- Reports expected collisions and how they would resolve
- Flags ISO 19650 code violations (invalid DISC, SYS, FUNC codes)
- Shows spatial detection accuracy (LOC/ZONE)
- Exports full audit trail to CSV

**Use this before committing** to catch issues early.

### Phase 4: Tagging — Assign SEQ and Build Tags

Three commands, choose based on scope:

#### AutoTagCommand (Active View)

**File:** `Tags/AutoTagCommand.cs:30–217`

1. Collects taggable elements visible in the active view
2. Filters to view-relevant disciplines (inspects view name, template, VG)
3. Prompts for collision mode: **Skip** / **Overwrite** / **AutoIncrement**
4. Builds existing tag index (O(1) collision detection) + sequence counters
5. Per element:
   - `PopulateAll()` — fills any remaining empty tokens
   - `BuildAndWriteTag()` — assigns SEQ, assembles tag, writes to TAG1
   - `WriteTag7All()` — builds rich narrative
6. Reports breakdown by discipline/system/level with collision stats

#### BatchTagCommand (Entire Project)

**File:** `Tags/BatchTagCommand.cs:32–594`

Same pipeline as AutoTag but across the **entire project**:
- Smart element ordering: groups by Level → Discipline → Category for contiguous SEQ numbers
- Progress reporting via `StingProgressDialog` (every 500 elements)
- Escape key cancellation support

#### TagNewOnlyCommand (Incremental)

**File:** `Tags/AutoTagCommand.cs:220–354`

Tags only elements where `ASS_TAG_1_TXT` is empty. Much faster for adding a few new elements to an already-tagged project.

### Phase 5: Validation

**Command:** `ValidateTagsCommand`
**File:** `Tags/ValidateTagsCommand.cs`

Checks every tagged element against ISO 19650 rules:
- Tag completeness (all 8 segments present)
- Individual token population (no empty segments)
- **Code validation:** DISC, SYS, FUNC, PROD against allowed code lists
- **Cross-validation:** DISC vs SYS must be consistent with category (e.g., a Pipe tagged as DISC=M with SYS=SAN should be DISC=P)
- STATUS and REV population
- Duplicate tag detection
- Phase mismatch detection
- Exports full audit trail to CSV

### Phase 6: Combine — Write to All Containers

**Command:** `CombineParametersCommand`
**File:** `Tags/CombineParametersCommand.cs`

Reads the 8 token values from each element and writes them to ALL applicable tag containers:

**Modes:**
- **All Containers** — Write to all 36 containers (universal + discipline-specific + material)
- **Universal Only** — Write only to TAG1–TAG6
- **Discipline Only** — Write only to discipline-specific containers (HVC, ELC, PLM, etc.)
- **Pick Groups** — User selects which container groups to write

**Includes pre-flight check** (`CombinePreFlight`) that reports how many elements have data ready vs missing tokens.

---

## 7. One-Click Workflows

### 7.1 Tag & Combine (Recommended)

**Command:** `TagAndCombineCommand`
**File:** `Tags/TagAndCombineCommand.cs:34–306`

Executes the **complete pipeline** in one click:

```
Scope Selection → Build Room Index → Detect Project LOC/REV
→ Per Element:
    1. SpatialAutoDetect LOC + ZONE
    2. Populate DISC/SYS/FUNC/PROD/LVL
    3. PhaseAutoDetect STATUS
    4. BuildAndWriteTag (TAG1 with collision handling)
    5. WriteContainers (all 36 containers)
    6. WriteTag7All (rich narrative)
→ Report
```

**Scope options:** Active View / Selected Elements / Entire Project

### 7.2 Full Auto-Populate

**Command:** `FullAutoPopulateCommand`
**File:** `Temp/ScheduleCommands.cs`

Runs the entire data enrichment pipeline with zero manual input:

```
Tokens → Dimensions → MEP Data → Formulas → Tags → Combine → Grid Mapping
```

### 7.3 Master Setup

**Command:** `MasterSetupCommand`
**File:** `Temp/MasterSetupCommand.cs:36–305`

17-step full project setup including materials, families, schedules, templates, AND tagging:

| Step | Action |
|------|--------|
| 1 | Load shared parameters |
| 2–3 | Create BLE + MEP materials (815 + 464) |
| 4–5 | Create wall/floor/ceiling/roof + duct/pipe types |
| 6–7 | Create 168 schedules + evaluate 199 formulas |
| **8** | **Tag & Combine (full pipeline)** |
| 9–14 | Filters, worksets, view templates, styles |
| 15–17 | Family params, auto-assign templates, legends |

Each step is independent — if one fails, subsequent steps still execute (with warnings).

---

## 8. Collision Handling & Sequence Numbers

### Collision Modes

When a tag-to-be-assigned already exists in the project, the user chooses how to handle it:

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| **Skip** | Never modify already-complete tags | Safe re-run, preserve manual edits |
| **Overwrite** | Force re-derive ALL tokens and reassign SEQ | Fresh start, fix bad data |
| **AutoIncrement** | Increment SEQ until tag is unique (up to 100 attempts) | Default — safest for batch operations |

### How Sequence Counters Work

1. **Pre-scan:** `GetExistingSequenceCounters()` scans all project elements, building a dictionary of `{(DISC, SYS, LVL) → max SEQ}` groups
2. **Assignment:** For each new element, look up the counter for its group, increment by 1, pad to 4 digits
3. **Collision check:** If the assembled tag exists in `existingTags` HashSet (O(1) lookup):
   - Increment SEQ and retry
   - Maximum 100 retries (safety limit `MaxCollisionDepth`)
   - Log warning if overflow (SEQ > 9999)
4. **Registration:** After successful placement, add the new tag to `existingTags` and remove the old tag if it changed

### TaggingStats

Every tagging operation collects detailed statistics:
- Tagged/Skipped/Overwritten counts
- Breakdown by Category, Discipline, System, Level
- Collision details (top 20 by depth)
- Warnings (capped at 100)

Displayed in a TaskDialog after completion.

**Code:** `TagConfig.cs:30–163` — `TaggingStats`, `TagConfig.cs:1119–1146` — collision loop

---

## 9. Real-Time Auto-Tagging (IUpdater)

**Class:** `StingAutoTagger` (`Core/StingAutoTagger.cs`)

An `IUpdater` implementation that automatically tags elements **the moment they are placed** in the model.

### How It Works

1. **Registration:** Registered in `StingToolsApp.OnStartup()` but starts **disabled** (no triggers)
2. **Toggle:** User enables via `AutoTaggerToggleCommand` — adds triggers for `Element.GetChangeTypeElementAddition()` on 22 taggable categories
3. **Trigger:** When Revit detects a new element in any of the 22 categories:
   - Builds context once per batch (room index, sequence counters, existing tags)
   - Runs `PopulateAll()` + `BuildAndWriteTag()` + `WriteTag7All()` on each new element
   - Skips already-processed elements via `HashSet<long>` (trims at 10,000 entries)
4. **Safety:** Guards against large paste/import operations (max 50 elements per trigger); auto-disables after 3 consecutive failures

### Monitored Categories (22)

Air Terminals, Cable Trays, Cable Tray Fittings, Casework, Ceilings, Communication Devices, Conduits, Conduit Fittings, Data Devices, Doors, Ducts, Duct Accessories, Duct Fittings, Electrical Equipment, Electrical Fixtures, Fire Alarm Devices, Flex Ducts, Floors, Furniture, Lighting Devices, Lighting Fixtures, Mechanical Equipment, Nurse Call Devices, Pipe Accessories, Pipe Fittings, Pipes, Plumbing Fixtures, Roofs, Security Devices, Sprinklers, Structural Columns, Structural Framing, Telephone Devices, Walls, Windows

---

## 10. Validation & QA

### ISO 19650 Validator

**Class:** `ISO19650Validator` (`Core/TagConfig.cs:170–270`)

Validates tokens against allowed code lists:

- **DISC:** M, E, P, A, S, FP, LV, G
- **SYS:** HVAC, HWS, DHW, DCW, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN
- **FUNC:** SUP, HTG, DCW, SAN, RWD, GAS, FP, PWR, FLS, COM, ICT, NCL, SEC, FIT, STR, GEN, EXH, RTN, FRA, DHW
- **LOC:** Must be in `LocCodes` list
- **ZONE:** Must be in `ZoneCodes` list
- **LVL:** Must match pattern (GF, L##, B#, RF, L00, XX)
- **SEQ:** Must be numeric, 4 digits

Cross-validates DISC vs SYS vs element category for consistency.

### QA Commands

| Command | Type | Description |
|---------|------|-------------|
| `ValidateTagsCommand` | ReadOnly | Full ISO 19650 validation with CSV export |
| `PreTagAuditCommand` | ReadOnly | Dry-run prediction before tagging |
| `CompletenessDashboardCommand` | ReadOnly | Per-discipline compliance percentages |
| `FindDuplicateTagsCommand` | ReadOnly | Detect duplicate tags, select affected elements |
| `HighlightInvalidCommand` | Manual | Color-code: red=missing, orange=incomplete |
| `AuditTagsCSVCommand` | ReadOnly | Export full tag audit to CSV |
| `TagStatsCommand` | ReadOnly | Quick counts by discipline/system/level |
| `TagRegisterExportCommand` | ReadOnly | 40+ column asset register CSV export |

### Compliance Scan (Live Dashboard)

**Class:** `ComplianceScan` (`Core/ComplianceScan.cs`)

Cached compliance scan for real-time WPF status bar display:
- **RAG Status:** Red (<50%), Amber (50–80%), Green (>80%) complete
- Refreshes every 30 seconds (or on-demand after tagging operations)
- Shows top 5 issues

---

## 11. Configuration & Customization

### project_config.json

All lookup tables are configurable via `project_config.json` (located alongside the DLL):

```json
{
  "DISC_MAP": {
    "Doors": "A",
    "Mechanical Equipment": "M",
    "Custom Category": "X"
  },
  "SYS_MAP": {
    "HVAC": ["Air Terminals", "Ducts", "Mechanical Equipment"],
    "CUSTOM_SYS": ["Custom Category"]
  },
  "PROD_MAP": {
    "Custom Category": "CUS"
  },
  "FUNC_MAP": {
    "CUSTOM_SYS": "CST"
  },
  "LOC_CODES": ["BLD1", "BLD2", "BLD3", "BLD4", "EXT"],
  "ZONE_CODES": ["Z01", "Z02", "Z03", "Z04", "Z05"],
  "TAG_FORMAT": {
    "separator": "-",
    "num_pad": 4,
    "segment_order": ["DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ"]
  }
}
```

**Commands for editing:**
- `ConfigEditorCommand` — View/edit/save/reload configuration in Revit
- `TagConfigCommand` — Display current lookup table configuration

### Tag Format Overrides

The tag format (separator, padding, segment order) can be customized:

| Setting | Default | Effect |
|---------|---------|--------|
| `separator` | `-` | Character between segments: `M-BLD1-Z01` vs `M.BLD1.Z01` |
| `num_pad` | `4` | SEQ padding width: `0001` (4) vs `001` (3) |
| `segment_order` | DISC,LOC,ZONE,LVL,SYS,FUNC,PROD,SEQ | Reorder segments: `LOC-DISC-SYS-SEQ` etc. |

---

## 12. Lookup Tables Reference

### DISC Map (41 categories → 8 codes)

```
M (Mechanical):  Air Terminals, Duct Accessories, Duct Fittings, Ducts,
                 Flex Ducts, Mechanical Equipment, Pipes*, Pipe Fittings*,
                 Pipe Accessories*, Flex Pipes*
E (Electrical):  Electrical Equipment, Electrical Fixtures, Lighting Fixtures,
                 Lighting Devices, Conduits, Conduit Fittings, Cable Trays,
                 Cable Tray Fittings
P (Plumbing):    Plumbing Fixtures
A (Architecture):Doors, Windows, Walls, Floors, Ceilings, Roofs, Rooms,
                 Furniture, Furniture Systems, Casework, Railings, Stairs,
                 Ramps, Curtain Panels, Curtain Wall Mullions, Curtain Systems
S (Structural):  Structural Columns, Structural Framing, Structural Foundations,
                 Columns
FP (Fire Prot.): Sprinklers, Fire Alarm Devices
LV (Low Voltage):Communication Devices, Data Devices, Nurse Call Devices,
                 Security Devices, Telephone Devices
G (General):     Generic Models, Specialty Equipment, Medical Equipment
```

### SYS Map (17 systems → categories)

```
HVAC: Air Terminals, Ducts, Duct Fittings, Duct Accessories, Flex Ducts,
      Mechanical Equipment, Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
DCW:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes, Plumbing Fixtures
DHW:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
HWS:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
SAN:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes, Plumbing Fixtures
RWD:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
GAS:  Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
FP:   Sprinklers, Pipes, Pipe Fittings, Pipe Accessories, Flex Pipes
LV:   Electrical Equipment, Electrical Fixtures, Lighting Fixtures,
      Lighting Devices, Conduits, Conduit Fittings, Cable Trays, Cable Tray Fittings
FLS:  Fire Alarm Devices
COM:  Communication Devices, Telephone Devices
ICT:  Data Devices
NCL:  Nurse Call Devices
SEC:  Security Devices
ARC:  Doors, Windows, Walls, Floors, Ceilings, Roofs, Rooms, Furniture, etc.
STR:  Structural Columns, Structural Framing, Structural Foundations, Columns
GEN:  Generic Models, Specialty Equipment, Medical Equipment
```

### PROD Map (41 categories → codes)

```
GRL (Grille)=Air Terminals   DU=Ducts        DFT=Duct Fittings
DAC=Duct Accessories         FDU=Flex Ducts  AHU=Mechanical Equipment
PP=Pipes                     PFT=Pipe Fittings    PAC=Pipe Accessories
FPP=Flex Pipes              FIX=Plumbing Fixtures SPR=Sprinklers
DB=Electrical Equipment     SKT=Electrical Fixtures
LUM=Lighting Fixtures       LDV=Lighting Devices
CDT=Conduits                CFT=Conduit Fittings
CBLT=Cable Trays            CTF=Cable Tray Fittings
FAD=Fire Alarm Devices      COM=Communication Devices
DAT=Data Devices            NCL=Nurse Call Devices
SEC=Security Devices        TEL=Telephone Devices
DR=Doors      WIN=Windows    WL=Walls      FL=Floors
CLG=Ceilings  RF=Roofs      RM=Rooms      FUR=Furniture
CWK=Casework  RLG=Railings  STR=Stairs    RMP=Ramps
COL=Columns   BM=Structural Framing  FDN=Structural Foundations
CPN=Curtain Panels  MUL=Mullions  CWS=Curtain Systems
GEN=Generic Models  SPE=Specialty Equip  MED=Medical Equipment
```

### FUNC Map (17 systems → codes)

```
HVAC→SUP   HWS→HTG   DHW→DHW   DCW→DCW   SAN→SAN   RWD→RWD   GAS→GAS
FP→FP      LV→PWR    FLS→FLS   COM→COM   ICT→ICT   NCL→NCL   SEC→SEC
ARC→FIT    STR→STR   GEN→GEN
```

---

## 13. Data Files Involved

| File | Format | Purpose |
|------|--------|---------|
| `PARAMETER_REGISTRY.json` | JSON | Master source of truth: token names, container definitions, category bindings, tag format |
| `MR_PARAMETERS.txt` | Revit SP | Shared parameter definitions (1080 params, UTF-16LE) |
| `project_config.json` | JSON | Project-level overrides: DISC/SYS/PROD/FUNC maps, LOC/ZONE codes, tag format |
| `CATEGORY_BINDINGS.csv` | CSV | 10,661 category-specific parameter bindings |
| `FAMILY_PARAMETER_BINDINGS.csv` | CSV | 4,686 family-specific parameter bindings |
| `LABEL_DEFINITIONS.json` | JSON | 3,623-line TAG7 label/legend display definitions |
| `FORMULAS_WITH_DEPENDENCIES.csv` | CSV | 199 parameter formulas in dependency order |
| `SCHEDULE_FIELD_REMAP.csv` | CSV | 50 deprecated field name remappings |
| `BLE_MATERIALS.csv` | CSV | 815 building element materials (70 columns each) |
| `MEP_MATERIALS.csv` | CSV | 464 MEP materials (70 columns each) |
| `TAG_GUIDE_V3.csv` | CSV | Tag reference guide |

---

## 14. Source Code Reference

### Core Engine

| File | Key Classes/Methods | Lines |
|------|-------------------|-------|
| `Core/TagConfig.cs` | `TagConfig.BuildAndWriteTag()` | 1007–1223 |
| | `TagConfig.GetMepSystemAwareSysCode()` | 1238+ |
| | `TagConfig.GetFamilyAwareProdCode()` | 1380+ |
| | `TagConfig.GetSmartFuncCode()` | inline at 1081 |
| | `TagConfig.GetExistingSequenceCounters()` | 1500+ |
| | `TagConfig.BuildExistingTagIndex()` | 1500+ |
| | `ISO19650Validator` | 170–270 |
| | `TaggingStats` | 30–163 |
| | `TagCollisionMode` enum | ~20 |
| | Default lookup tables | 1922–2042 |
| `Core/ParamRegistry.cs` | `ReadTokenValues()` | 1361–1368 |
| | `AssembleContainer()` | 1338–1356 |
| | `WriteContainers()` | 1376–1394 |
| | Token constants (DISC, LOC, etc.) | 76–91 |
| `Core/ParameterHelpers.cs` | `TokenAutoPopulator.PopulateAll()` | 942–1091 |
| | `SpatialAutoDetect.DetectLoc()` | 413–462 |
| | `SpatialAutoDetect.DetectZone()` | 468–506 |
| | `PhaseAutoDetect.DetectStatus()` | 630+ |
| | `GetLevelCode()` | varies |
| `Core/StingAutoTagger.cs` | `StingAutoTagger` (IUpdater) | 27–283 |
| `Core/ComplianceScan.cs` | `ComplianceScan.Scan()` | 1–160 |

### Tagging Commands

| File | Commands | Lines |
|------|----------|-------|
| `Tags/LoadSharedParamsCommand.cs` | LoadSharedParams (2-pass binding) | 1–344 |
| `Tags/FamilyStagePopulateCommand.cs` | FamilyStagePopulate (7-token pre-fill) | 1–379 |
| `Tags/PreTagAuditCommand.cs` | PreTagAudit (dry-run prediction) | 1–442 |
| `Tags/AutoTagCommand.cs` | AutoTag (view scope) + TagNewOnly (incremental) | 1–355 |
| `Tags/BatchTagCommand.cs` | BatchTag (project scope) | 1–594 |
| `Tags/TagAndCombineCommand.cs` | TagAndCombine (one-click full pipeline) | 1–307 |
| `Tags/ValidateTagsCommand.cs` | ValidateTags (ISO 19650 compliance) | 1–499 |
| `Tags/CombineParametersCommand.cs` | CombineParameters + PreFlight | 1–426 |
| `Tags/TokenWriterCommands.cs` | SetDisc, SetLoc, SetZone, SetStatus, AssignNumbers, BuildTags, Dashboard | 1–592 |

### QA & Management

| File | Commands |
|------|----------|
| `Tags/SmartTagPlacementCommand.cs` | 9 visual annotation commands |
| `Tags/LegendBuilderCommands.cs` | 31 legend generation commands |
| `Tags/RichTagDisplayCommands.cs` | 6 TAG7 display/export commands |
| `Tags/ResolveAllIssuesCommand.cs` | One-click ISO 19650 compliance fix |
| `Organise/TagOperationCommands.cs` | 40 tag management commands |

---

## Quick Reference: Tag Examples

```
Mechanical AHU on Level 2:    M-BLD1-Z01-L02-HVAC-SUP-AHU-0001
Electrical DB on Ground:      E-BLD1-Z01-GF-LV-PWR-DB-0001
Plumbing Fixture Basement:    P-BLD1-Z02-B1-SAN-SAN-FIX-0003
Fire Sprinkler on Level 1:    FP-BLD1-Z01-L01-FP-FP-SPR-0001
Door on Level 3:              A-BLD1-Z01-L03-ARC-FIT-DR-0012
External Light:               E-EXT-Z01-L00-LV-PWR-LUM-0001
Structural Column:            S-BLD1-Z01-GF-STR-STR-COL-0001
Security Camera:              LV-BLD1-Z03-L01-SEC-SEC-SEC-0001
```

---

*This guide reflects the StingTools codebase as of March 2026. All lookup tables and behaviours are configurable via `project_config.json` and `PARAMETER_REGISTRY.json`.*
