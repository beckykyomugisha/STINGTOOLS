# STING Tools — Complete Tagging & Asset Management Guide

**Version**: 7.0 | **Standard**: ISO 19650-1/2/3, CIBSE TM40, Uniclass 2015  
**Last Updated**: 2026-03-24 | **Audience**: BIM Coordinators, Discipline Leads, All Team Members  
**Elements Supported**: 22 Revit categories across 8 disciplines

---

## Table of Contents

1. [Introduction to ISO 19650 Asset Tagging](#1-introduction-to-iso-19650-asset-tagging)
2. [Tag Format & Structure](#2-tag-format--structure)
3. [Token Reference (All 8 Segments)](#3-token-reference-all-8-segments)
4. [Tagging Pipeline (How Tags Are Built)](#4-tagging-pipeline-how-tags-are-built)
5. [Tagging Commands Reference](#5-tagging-commands-reference)
6. [One-Click Workflows](#6-one-click-workflows)
7. [Token Management](#7-token-management)
8. [Tag Collision Handling & SEQ Numbering](#8-tag-collision-handling--seq-numbering)
9. [Tag Containers (53 Parameters)](#9-tag-containers-53-parameters)
10. [TAG7 Rich Narrative](#10-tag7-rich-narrative)
11. [Tag Validation & Compliance](#11-tag-validation--compliance)
12. [Smart Tag Placement (Visual Annotations)](#12-smart-tag-placement-visual-annotations)
13. [Tag Style Engine (128 Combinations)](#13-tag-style-engine-128-combinations)
14. [Display Modes & Presentation](#14-display-modes--presentation)
15. [Real-Time Auto-Tagging](#15-real-time-auto-tagging)
16. [Stale Element Detection & Re-Tagging](#16-stale-element-detection--re-tagging)
17. [Tag Operations (ORGANISE Tab)](#17-tag-operations-organise-tab)
18. [Leader Management (14 Commands)](#18-leader-management-14-commands)
19. [Legend Building](#19-legend-building)
20. [Workflow Automation for Tagging](#20-workflow-automation-for-tagging)
21. [Cross-System Integration](#21-cross-system-integration)
22. [Data Exchange (Excel/COBie)](#22-data-exchange-excelcobie)
23. [Graitec-Style Numbering](#23-graitec-style-numbering)
24. [Tag Export/Import Between Projects](#24-tag-exportimport-between-projects)
25. [Configuration Reference](#25-configuration-reference)
26. [Troubleshooting](#26-troubleshooting)
27. [Complete Command Reference (35+ Commands)](#27-complete-command-reference-35-commands)

---

## 1. Introduction to ISO 19650 Asset Tagging

### What Is Asset Tagging?

Asset tagging assigns a **unique, structured identifier** to every building element in your Revit model. These tags follow the **ISO 19650** standard for information management and enable:

- **Asset tracking** throughout the building lifecycle (design → construction → operation)
- **COBie data exchange** for facilities management handover
- **Compliance reporting** against ISO 19650 information requirements
- **Cross-discipline coordination** with consistent naming conventions
- **FM/CAFM system integration** via unique asset identifiers

### Why STING Tags?

| Feature | Manual Tagging | STING Auto-Tagging |
|---------|---------------|-------------------|
| Speed | ~2 min/element | ~0.01 sec/element |
| Consistency | Human error prone | 100% format compliance |
| Coverage | Typically 60-70% | 95-100% achievable |
| Maintenance | Manual update on changes | Auto-stale detection |
| Containers | 1 parameter only | All 53 containers populated |
| Validation | Visual inspection | 45+ automated checks |

### Supported Categories (22)

| Category | Discipline | PROD Code |
|----------|-----------|-----------|
| Air Terminals | M | AT |
| Cable Trays | LV | CTR |
| Ceilings | A | CLG |
| Columns (Structural) | S | COL |
| Conduits | E | CDT |
| Doors | A | DR |
| Duct Systems | M | DCT |
| Electrical Equipment | E | DB |
| Electrical Fixtures | E | EFX |
| Floors | A | FLR |
| Furniture | A | FUR |
| Lighting Fixtures | E | LUM |
| Mechanical Equipment | M | AHU/FCU/etc. |
| Pipe Systems | P | PIP |
| Plumbing Fixtures | P | SAN |
| Roofs | A | RF |
| Rooms | A | RM |
| Sprinklers | FP | SPR |
| Structural Framing | S | BM |
| Walls | A | WL |
| Windows | A | WN |
| Generic Models | G | GEN |

---

## 2. Tag Format & Structure

### The 8-Segment Tag

Every tag follows this format:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
```

Example: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`

Reading this tag:
- **M** = Mechanical discipline
- **BLD1** = Building 1
- **Z01** = Zone 01
- **L02** = Level 02 (second floor)
- **HVAC** = HVAC system type
- **SUP** = Supply function
- **AHU** = Air Handling Unit product
- **0003** = Third unit in this group

### Tag Format Configuration

Configurable in `project_config.json`:

```json
{
    "TAG_FORMAT": {
        "separator": "-",
        "num_pad": 4,
        "segment_order": ["DISC","LOC","ZONE","LVL","SYS","FUNC","PROD","SEQ"]
    },
    "TAG_PREFIX": "",
    "TAG_SUFFIX": ""
}
```

- **separator**: Character between segments (default "-")
- **num_pad**: SEQ zero-padding width (default 4 → "0001")
- **segment_order**: Order of segments in assembled tag
- **TAG_PREFIX/SUFFIX**: Optional prefix/suffix (e.g., "PRJ-" prefix)

---

## 3. Token Reference (All 8 Segments)

### DISC — Discipline Code

| Code | Discipline | Categories |
|------|-----------|------------|
| M | Mechanical | Mechanical Equipment, Air Terminals, Duct Systems |
| E | Electrical | Electrical Equipment, Electrical Fixtures, Conduits |
| P | Plumbing | Plumbing Fixtures, Pipe Systems |
| A | Architectural | Walls, Doors, Windows, Floors, Ceilings, Roofs, Rooms, Furniture |
| S | Structural | Structural Columns, Structural Framing |
| FP | Fire Protection | Sprinklers, Fire Alarm devices |
| LV | Low Voltage | Cable Trays, Communication devices |
| G | General | Generic Models, uncategorized |

**Auto-detection**: From element category via `TagConfig.DiscMap` (41 mappings).

### LOC — Location Code

| Code | Meaning |
|------|---------|
| BLD1 | Building 1 (main building) |
| BLD2 | Building 2 |
| BLD3 | Building 3 |
| EXT | External / Site |
| XX | Unknown (placeholder) |

**Auto-detection**: `SpatialAutoDetect.DetectLoc()` derives from:
1. Room name/number patterns
2. Project Information building name
3. Custom rules in `project_config.json`

### ZONE — Zone Code

| Code | Meaning |
|------|---------|
| Z01 | Zone 1 (typically ground floor public) |
| Z02 | Zone 2 (upper floors) |
| Z03 | Zone 3 (service/plant areas) |
| Z04 | Zone 4 (restricted areas) |
| ZZ | Unknown (placeholder) |

**Auto-detection**: `SpatialAutoDetect.DetectZone()` derives from:
1. Room Department parameter
2. Room name patterns
3. Custom rules in `project_config.json`

### LVL — Level Code

| Code | Meaning |
|------|---------|
| B1, B2, B3 | Basement levels |
| GF | Ground floor |
| L01-L99 | Upper levels |
| RF | Roof level |
| XX | Unknown |

**Auto-detection**: `ParameterHelpers.GetLevelCode()` from element level name.

### SYS — System Type Code (CIBSE TM40 / Uniclass 2015)

| Code | System | Discipline |
|------|--------|-----------|
| HVAC | Heating, Ventilation, Air Conditioning | M |
| DCW | Domestic Cold Water | P |
| DHW | Domestic Hot Water | P |
| HWS | Hot Water Service | P |
| SAN | Sanitary/Drainage | P |
| RWD | Rainwater Drainage | P |
| GAS | Natural Gas | P |
| FP | Fire Protection | FP |
| LV | Low Voltage | LV |
| FLS | Fire/Life Safety | FP |
| COM | Communications | LV |
| ICT | Information & Communications | LV |
| NCL | Nurse Call / Low Voltage | LV |
| SEC | Security | LV |
| ARC | Architectural (non-MEP) | A |
| STR | Structural | S |
| GEN | General | G |

**Auto-detection**: 6-layer derivation:
1. Connected MEP system name → CIBSE code
2. Element connector analysis
3. Nearest tagged element inheritance
4. Category fallback from `TagConfig.SysMap`
5. CATEGORY_FORCE_SYS config overrides
6. Default "GEN" if all else fails

### FUNC — Function Code

| Code | Function | Typical SYS |
|------|----------|-------------|
| SUP | Supply | HVAC, DCW, DHW |
| RET | Return | HVAC |
| EXH | Exhaust | HVAC |
| HTG | Heating | HVAC, HWS |
| CLG | Cooling | HVAC |
| VNT | Ventilation | HVAC |
| DCW | Domestic Cold Water | DCW |
| DHW | Domestic Hot Water | DHW |
| SAN | Sanitary | SAN |
| PWR | Power | LV, E |
| LTG | Lighting | E |
| DTA | Data | ICT, COM |
| DET | Detection | FLS, SEC |
| SPR | Sprinkler | FP |
| GEN | General | Any |

**Auto-detection**: From SYS via `TagConfig.FuncMap`.

### PROD — Product Code

35+ family-name-aware mappings:

| Code | Product | Family Name Pattern |
|------|---------|-------------------|
| AHU | Air Handling Unit | *Air Handling*, *AHU* |
| FCU | Fan Coil Unit | *Fan Coil*, *FCU* |
| VAV | Variable Air Volume | *VAV*, *Variable Air* |
| CHR | Chiller | *Chiller*, *CHL* |
| BLR | Boiler | *Boiler*, *BLR* |
| PMP | Pump | *Pump*, *PMP* |
| DB | Distribution Board | *Distribution Board*, *Panel* |
| LUM | Luminaire | *Light*, *Luminaire*, *LED* |
| DR | Door | (all door families) |
| WN | Window | (all window families) |
| WL | Wall | (all wall types) |
| FLR | Floor | (all floor types) |
| ... | ... | (35+ total mappings) |

**Auto-detection**: `TagConfig.GetFamilyAwareProdCode()` inspects family name.

### SEQ — Sequence Number

4-digit zero-padded sequential number within each group:
- Group key: `{DISC}_{SYS}_{FUNC}_{PROD}`
- Auto-increments from highest existing number
- Collision-safe: checks existing tags before assignment
- Persists to `.sting_seq.json` sidecar file

**Numbering schemes** (via `SetSeqScheme` command):
- **Numeric**: 0001, 0002, 0003...
- **Alpha**: A, B, C... AA, AB...
- **ZonePrefix**: Z01-0001, Z02-0001...
- **DiscPrefix**: M-0001, E-0001...


---

## 4. Tagging Pipeline (How Tags Are Built)

### The 11-Step Pipeline

Every element tagged by STING goes through `TagPipelineHelper.RunFullPipeline()`:

```
Step 1:  AUDIT TRAIL ────────→ Save previous tag + timestamp
Step 2:  TOKEN LOCK CHECK ───→ Snapshot locked tokens (ASS_TOKEN_LOCK_TXT)
Step 3:  TYPE INHERIT ───────→ Copy DISC/SYS/FUNC/PROD from family type
Step 4:  POPULATE ALL ───────→ Auto-derive all 9 tokens from context
Step 5:  CATEGORY FORCE SYS ─→ Override SYS per CATEGORY_FORCE_SYS config
Step 6:  CATEGORY OVERRIDES ─→ Apply CATEGORY_TOKEN_OVERRIDES
Step 7:  RESTORE LOCKS ──────→ Restore locked token values
Step 8:  NATIVE PARAM MAP ───→ Map 30+ Revit built-in params to STING
Step 9:  FORMULA ENGINE ─────→ Evaluate 199 formulas (cost/flow/area/env)
Step 10: BUILD TAG ──────────→ Assemble 8-segment tag with collision detect
Step 11: POST-TAG ───────────→ Write containers + TAG7 + grid reference
```

### Step Details

#### Step 1: Audit Trail
- Saves current `ASS_TAG_1` to `ASS_TAG_PREV_TXT`
- Records timestamp in `ASS_TAG_MODIFIED_DT`
- Enables "before/after" change tracking across revisions

#### Step 2: Token Lock
- Reads `ASS_TOKEN_LOCK_TXT` (e.g., "DISC,LOC" = lock these tokens)
- Snapshots locked values BEFORE any pipeline changes
- Restored AFTER all overrides (Step 7)

#### Step 3: Type Token Inheritance
- Checks element's family TYPE for non-empty DISC/SYS/FUNC/PROD
- Copies to instance if instance values are empty
- Enables type-level token configuration (set once on type, inherit to all instances)

#### Step 4: Token Population
- `TokenAutoPopulator.PopulateAll()` derives all 9 tokens:
  - DISC from category
  - LOC from room/project (SpatialAutoDetect)
  - ZONE from room department (SpatialAutoDetect)
  - LVL from element level
  - SYS from MEP system (6-layer derivation)
  - FUNC from SYS lookup
  - PROD from family name (35+ patterns)
  - STATUS from Revit phase
  - REV from project revision
- Includes `CopyTokensFromNearest` for SYS/FUNC when generic defaults detected

#### Step 5-6: Configuration Overrides
- `CATEGORY_FORCE_SYS`: Override SYS for specific categories (e.g., Sprinklers→FP)
- `CATEGORY_TOKEN_OVERRIDES`: Per-category token enforcement (e.g., Structural Columns→DISC=S)
- SKIP flag in overrides excludes categories entirely

#### Step 7: Token Lock Restore
- Restores locked token values captured in Step 2
- Ensures user-specified values survive all auto-detection

#### Step 8: Native Parameter Mapping
- `NativeParamMapper.MapAll()` bridges 30+ Revit built-in parameters:
  - Dimensions: Width, Height, Length, Area, Volume
  - MEP: Flow, Pressure, Voltage, Circuit, Power
  - Identity: Mark, Model, Manufacturer, Description

#### Step 9: Formula Evaluation
- 199 formulas from `FORMULAS_WITH_DEPENDENCIES.csv`
- Dependency-ordered evaluation (levels 0-6, Kahn's topological sort)
- Supports: arithmetic, conditionals, string concat, unit conversion
- Includes cost calculations, environmental data, performance metrics

#### Step 10: Tag Assembly
- Assembles `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`
- Applies TAG_PREFIX and TAG_SUFFIX
- Collision detection via O(1) HashSet lookup
- SEQ auto-increment on collision
- Writes to `ASS_TAG_1` through `ASS_TAG_6`

#### Step 11: Post-Tag Operations
- `WriteContainers()`: Writes to all 53 discipline-specific containers
  - Selective by discipline (DISC=M skips ELC_*, PLM_*, etc.)
- `WriteTag7All()`: Builds TAG7 rich narrative (A-F sub-sections)
- `GetGridRef()`: Auto-detects nearest grid intersection → `ASS_GRID_REF_TXT`

---

## 5. Tagging Commands Reference

### Primary Tagging Commands

| Command | Location | Scope | Description |
|---------|----------|-------|-------------|
| **Auto Tag** | CREATE → Auto Tag | Active view | Tag elements in current view |
| **Batch Tag** | CREATE → Batch Tag | Entire project | Tag ALL elements in model |
| **Tag & Combine** | CREATE → Tag & Combine | View/Selection/Project | Full pipeline + all containers |
| **Tag New Only** | CREATE → More → Tag New Only | View/Project | Only untagged elements |
| **Pre-Tag Audit** | CREATE → More → Pre-Tag Audit | View/Selection/Project | Dry-run prediction |
| **Family-Stage Populate** | CREATE → More → Family-Stage Populate | Selection | Pre-populate 7 tokens |

### Validation Commands

| Command | Location | Description |
|---------|----------|-------------|
| **Validate Tags** | CREATE → QA → Validate | 4-bucket compliance check |
| **Completeness Dashboard** | CREATE → QA → Dashboard | Per-discipline RAG display |
| **Quick Tag Preview** | CREATE → QA → Preview | Show predicted tag without changes |
| **Find Duplicates** | ORGANISE → Analysis → Duplicates | Find duplicate tag values |
| **Highlight Invalid** | ORGANISE → Analysis → Highlight | Color-code missing/incomplete tags |

### Fix Commands

| Command | Location | Description |
|---------|----------|-------------|
| **Resolve All Issues** | CREATE → More → Resolve All | One-click full compliance fix |
| **Fix Duplicates** | CREATE → More → Fix Duplicates | Auto-increment SEQ on duplicates |
| **Repair Duplicate SEQ** | (dispatch) | Smart spatial proximity repair |
| **Anomaly Auto-Fix** | (dispatch) | Detect and fix 8+ anomaly types |

### Setup Commands

| Command | Location | Description |
|---------|----------|-------------|
| **Tag Config** | CREATE → Setup → Tag Config | View current configuration |
| **Load Params** | CREATE → Setup → Load Params | Bind shared parameters (2-pass) |
| **Configure** | CREATE → Setup → Configure | Edit project_config.json |
| **Auto-Tagger Toggle** | CREATE → Setup → Auto-Tagger | Enable/disable real-time tagging |

---

## 6. One-Click Workflows

### Recommended Workflow by Project Stage

| Stage | Workflow | Commands | Time |
|-------|----------|----------|------|
| **Project Start** | Initial Setup | Master Setup → Tag & Combine | 5-10 min |
| **During Design** | Incremental | Tag New Only (daily) | 30 sec |
| **Pre-Coordination** | Full QA | Pre-Tag Audit → Validate → Resolve | 3-5 min |
| **Pre-Handover** | Handover Prep | HandoverReadiness workflow | 5-8 min |
| **Weekly QA** | Weekly Data Drop | WeeklyDataDrop workflow | 3-5 min |
| **Daily Check** | Morning Health | MorningHealthCheck workflow | 3-5 min |

### Workflow Automation Presets for Tagging

| Preset | Steps | When to Use |
|--------|-------|-------------|
| **DailyQA** | RetagStale → PreTagAudit → BatchTag → Validate → Dashboard | Daily quality check |
| **PostTaggingQA** | PreTagAudit → ValidateTags → Dashboard → TagRegister → ValidateTemplate | After major tagging |
| **MorningHealthCheck** | RetagStale → WarningsAutoFix → TagNew → Audit → Validate → Templates → Sheets → RevCheck | Morning routine |
| **HandoverReadiness** | RetagStale → FullTag → Validate → Templates → COBie → Register → BOQ → BEP → Revision | Pre-handover |

### Running a Workflow

1. BIM tab → Workflows section → Click preset button
2. Or: BIM Coordination Center → WORKFLOWS tab → Quick Workflow
3. Progress shown with ETA and cancel support
4. Each step shows pass/fail status
5. Compliance gate checked after completion

---

## 7. Token Management

### Setting Individual Tokens

| Command | Location | What It Sets |
|---------|----------|-------------|
| Set Discipline | CREATE → Tokens → Set Discipline | DISC code (M/E/P/A/S/FP/LV/G) |
| Set Location | CREATE → Tokens → Set Location | LOC code (BLD1/BLD2/BLD3/EXT) |
| Set Zone | CREATE → Tokens → Set Zone | ZONE code (Z01-Z04) |
| Set Status | CREATE → Tokens → Set Status | STATUS (NEW/EXISTING/DEMOLISHED/TEMP) |
| Assign Numbers | CREATE → Tokens → Assign Numbers | Sequential SEQ within groups |
| Build Tags | CREATE → Tokens → Build Tags | Rebuild TAG1 from tokens |
| Combine | CREATE → Tokens → Combine | Write to all containers |

### Bulk Operations

**Bulk Parameter Write** (SELECT tab → Bulk Param):
- **Set Token**: Pick any token → pick value → apply to selection/view/project
- **Auto-populate**: Run full token population
- **Clear Tags**: Remove all 15 tag parameters
- **Re-tag**: Force re-derive with overwrite

### Token Lock System

Lock specific tokens to prevent auto-detection from overwriting user-set values:

1. Set `ASS_TOKEN_LOCK_TXT` on element (e.g., "DISC,LOC")
2. Pipeline preserves locked values through all override steps
3. Useful for elements with non-standard classification

### Cross-Discipline Token Updates

When changing DISC (e.g., M→E), STING detects cross-discipline mismatches:
- If SYS doesn't match new discipline, offers to auto-update
- FUNC auto-derived from new SYS
- Prevents invalid ISO 19650 tags (e.g., DISC=E but SYS=HVAC)

---

## 8. Tag Collision Handling & SEQ Numbering

### Collision Modes

| Mode | Behavior | When to Use |
|------|----------|-------------|
| **Skip** | Keep existing tag | Incremental tagging (Tag New Only) |
| **Overwrite** | Replace with re-derived tag | Correcting mistakes |
| **Auto-Increment** | Assign next available SEQ | Default — recommended for all tagging |

### SEQ Counter Persistence

Sequence counters persist across sessions via `.sting_seq.json`:

```json
{
    "M_HVAC_SUP_AHU": 47,
    "E_LV_PWR_DB": 12,
    "P_DCW_DCW_SAN": 8
}
```

- Saved after every tagging transaction
- Merged with project parameters via max-per-key strategy
- Survives Revit crashes (sidecar file alongside .rvt)

### SEQ Range Allocation (Federated Models)

For projects with multiple discipline models:

```json
{
    "SEQ_RANGE_ALLOCATION": {
        "M": [1, 9999],
        "E": [10000, 19999],
        "P": [20000, 29999],
        "A": [30000, 39999],
        "S": [40000, 49999]
    }
}
```

Prevents duplicate asset tags when merging federated models for COBie handover.


---

## 9. Tag Containers (53 Parameters)

### Universal Containers

| Parameter | Content | Example |
|-----------|---------|---------|
| ASS_TAG_1 | Full 8-segment tag | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |
| ASS_TAG_2 | Top line (DISC-LOC-ZONE-LVL) | M-BLD1-Z01-L02 |
| ASS_TAG_3 | Bottom line (SYS-FUNC-PROD-SEQ) | HVAC-SUP-AHU-0003 |
| ASS_TAG_4 | Multi-line top | M-BLD1\nZ01-L02 |
| ASS_TAG_5 | Multi-line middle | HVAC-SUP |
| ASS_TAG_6 | Multi-line bottom | AHU-0003 |

### Discipline-Specific Containers (30)

| Discipline | Containers |
|-----------|-----------|
| HVAC | HVC_EQP_TAG, HVC_DCT_TAG, HVC_FLX_TAG |
| Electrical | ELC_EQP_TAG, ELE_FIX_TAG, LTG_FIX_TAG, ELC_CDT_TAG, ELC_CTR_TAG |
| Plumbing | PLM_EQP_TAG |
| Fire/Safety | FLS_DEV_TAG |
| Comms/LV | COM_DEV_TAG, SEC_DEV_TAG, NCL_DEV_TAG, ICT_DEV_TAG |
| Material | MAT_TAG_1 through MAT_TAG_6 |

### Selective Container Writing

STING only writes containers relevant to the element's discipline:
- DISC=M elements: Only HVC_* containers
- DISC=E elements: Only ELC_*, ELE_*, LTG_* containers
- DISC=P elements: Only PLM_* containers
- This reduces container writes by 60-80% per element

---

## 10. TAG7 Rich Narrative

### TAG7 Structure (6 Sub-Sections)

TAG7 is a human-readable descriptive tag with 6 sections (A-F):

| Parameter | Section | Content | Color |
|-----------|---------|---------|-------|
| ASS_TAG_7A_TXT | Identity Header | Asset name, PROD, manufacturer, model | Blue |
| ASS_TAG_7B_TXT | System & Function | System description, function code | Green |
| ASS_TAG_7C_TXT | Spatial Context | Room, department, grid reference | Orange |
| ASS_TAG_7D_TXT | Lifecycle & Status | Status, revision, origin, maintenance | Red |
| ASS_TAG_7E_TXT | Technical Specs | Capacity, flow, voltage, dimensions | Purple |
| ASS_TAG_7F_TXT | Classification | Uniformat, OmniClass, keynote, cost, tag | Grey |
| ASS_TAG_7_TXT | Full Narrative | All sections combined with delimiters | Multi |

### Presentation Modes

| Mode | Content | Use Case |
|------|---------|----------|
| Compact | Identity + System only | Plans |
| Technical | Identity + System + Technical | Engineering drawings |
| Full Specification | All 6 sections | Schedules |
| Presentation | Identity + Spatial | Client presentations |
| BOQ | Identity + Classification + Cost | Bill of Quantities |

### Paragraph Depth Control

TAG7 visibility controlled by `TAG_PARA_STATE_1/2/3_BOOL` parameters:
- Tier 1: Basic (identity + system)
- Tier 2: Extended (+ spatial + lifecycle)
- Tier 3: Full (+ technical + classification)
- Tiers 4-10: Custom depth levels

Set via: CREATE → Presentation → Set Paragraph Depth

---

## 11. Tag Validation & Compliance

### Four Compliance Buckets

| Bucket | Color | Criteria | Weight |
|--------|-------|----------|--------|
| **RESOLVED** | Green | All 8 segments with valid ISO codes | 100% |
| **COMPLETE_PLACEHOLDERS** | Amber | 8 segments but contains GEN/XX/ZZ/0000 | 70% |
| **INCOMPLETE** | Orange | Fewer than 8 segments | 30% |
| **UNTAGGED** | Red | No tag at all | 0% |

### ISO 19650 Code Validation

Each token is validated against ISO 19650/CIBSE/Uniclass code lists:

| Token | Valid Codes | Custom Extension |
|-------|-----------|-----------------|
| DISC | M, E, P, A, S, FP, LV, G + custom | CUSTOM_VALID_DISC |
| SYS | HVAC, DCW, DHW, HWS, SAN, RWD, GAS, FP, LV, FLS, COM, ICT, NCL, SEC, ARC, STR, GEN + custom | CUSTOM_VALID_SYS |
| FUNC | SUP, RET, EXH, HTG, CLG, VNT, DCW, DHW, SAN, PWR, LTG, DTA, DET, SPR, GEN + custom | CUSTOM_VALID_FUNC |
| LOC | BLD1-3, EXT, XX + custom | CUSTOM_VALID_LOC |
| ZONE | Z01-Z04, ZZ, XX + custom | CUSTOM_VALID_ZONE |

### Cross-Validation Rules

STING validates token combinations:
- **DISC ↔ SYS**: SYS=HVAC must be DISC=M (not E or P)
- **SYS ↔ FUNC**: FUNC=PWR invalid for SYS=HVAC
- **FUNC ↔ PROD**: FUNC=SUP incompatible with PROD=WC (sanitary fixture)

### Compliance Gates

| Gate | Threshold | When Checked |
|------|-----------|-------------|
| Post-tagging | COMPLIANCE_GATE_PCT (80%) | After any tagging command |
| CDE SHARED | CDE_SHARED_MIN_COMPLIANCE (70%) | WIP → SHARED transition |
| CDE PUBLISHED | CDE_PUBLISHED_MIN_COMPLIANCE (90%) | SHARED → PUBLISHED transition |
| COBie export | 60% minimum | Before COBie generation |
| Pre-revision | 80% recommended | Before revision creation |

---

## 12. Smart Tag Placement (Visual Annotations)

### Overview

Smart Tag Placement creates `IndependentTag` annotations in views — the visual tags that appear on drawings. This is separate from data tagging (which writes parameter values).

### 16-Position Placement System

```
       P13  P5  P14
    P12  P4  P1  P6  P15
         P3  ●  P2
    P16  P8  P7  P10 P9
       P11
```

- Ring 1 (P1-P8): Close positions (cardinal + diagonal)
- Ring 2 (P9-P16): Far positions for crowded areas
- Scale-aware offsets (configurable per scale tier)

### Commands

| Command | Location | Description |
|---------|----------|-------------|
| Smart Place Tags | TAGS → Placement | 16-position collision avoidance |
| Arrange Tags | TAGS → Placement | Auto-arrange to grid |
| Remove Tags | TAGS → Placement | Remove all annotation tags |
| Batch Place | TAGS → Placement | Place across multiple views |
| Learn Placement | TAGS → Placement | Analyze existing layouts |
| Apply Template | TAGS → Placement | Apply saved placement rules |
| Overlap Analysis | TAGS → Placement | Detect overlapping tags |
| Batch Text Size | TAGS → Placement | Set text size for all tags |
| Set Line Weight | TAGS → Placement | Category line weight control |
| Align Tag Bands | TAGS → Placement | Grid-align by Y coordinate |
| Switch Position | TAGS → Placement | 4-position switching |
| Export Positions | TAGS → Placement | CSV export of tag positions |
| Batch Linked | TAGS → Placement | Tags in linked model views |
| Export Manifest | TAGS → Placement | Linked model token manifest |
| Adjust Elbows | TAGS → Leader | Elbow angle control |
| Set Arrowhead | TAGS → Leader | Arrowhead style control |

### Collision Avoidance Algorithm

1. Calculate element center and bounding box in view
2. Generate 16 candidate positions (2 rings)
3. Score each candidate:
   - Distance from element (closer = better)
   - Overlap with existing tags (penalty)
   - Overlap with other elements (penalty)
   - Grid alignment bonus
   - Preferred side bias (per category)
4. Place at highest-scoring position
5. If all positions overlap, add leader and extend search
6. Register placed tag for future collision checks

---

## 13. Tag Style Engine (128 Combinations)

### Style Matrix

Tag families contain label rows controlled by `TAG_{SIZE}{STYLE}_{COLOR}_BOOL` parameters:

- **4 Sizes**: 2mm, 2.5mm, 3mm, 3.5mm text height
- **3 Styles**: NOM (normal), BOLD, ITALIC
- **8 Colors**: BLACK, BLUE, GREEN, RED, ORANGE, PURPLE, BROWN, GREY

Total: 4 × 3 × 8 = **96 combinations per tag** (with 32 additional extended styles = 128 total)

### Built-In Color Schemes

| Scheme | M | E | P | A | S | FP | LV | Use Case |
|--------|---|---|---|---|---|----|----|----------|
| Discipline | Blue | Gold | Green | Grey | Red | Orange | Purple | Standard |
| Warm | Red | Orange | Yellow | Cream | Brown | Coral | Peach | Heat/load |
| Cool | Navy | Blue | Cyan | Mint | Teal | Ice | Aqua | Cooling/flow |
| Monochrome | Black | DkGrey | MdGrey | LtGrey | Charcoal | Slate | Silver | Print |
| Red | All red tones | | | | | | | QA/checking |
| Yellow | All yellow tones | | | | | | | Highlighting |
| Blue | All blue tones | | | | | | | Presentation |
| Dark | All dark tones | | | | | | | Dark backgrounds |

### Commands

| Command | Location | Description |
|---------|----------|-------------|
| Apply Tag Style | TAGS → Style | Size → Style → Color dialog |
| Apply Color Scheme | TAGS → Style | Named scheme to active view |
| Clear Color Scheme | TAGS → Style | Remove all overrides |
| Tag Style Report | TAGS → Style | Current style per element type |
| Switch by Discipline | TAGS → Style | Auto-apply discipline scheme |
| Batch Apply Scheme | TAGS → Style | Apply across all views |
| Color by Variable | TAGS → Style | Color by any parameter value |
| Set Box Color | TAGS → Style | Individual box/border color |

---

## 14. Display Modes & Presentation

### 5 Display Modes

| Mode | Format | Example | Use Case |
|------|--------|---------|----------|
| 1 | SEQ only | 0003 | Clean plans, presentations |
| 2 | PROD-SEQ | AHU-0003 | Standard working drawings |
| 3 | DISC-SYS-SEQ | M-HVAC-0003 | Discipline coordination |
| 4 | DISC-PROD-SEQ | M-AHU-0003 | Detailed coordination |
| 5 | Full 8-segment | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 | Schedules, asset registers |

Set via: ORGANISE → Display Mode  
Result written to: `ASS_DISPLAY_TXT`

### Per-View Tag Style Routing

Set different color schemes per view via `STING_VIEW_TAG_STYLE`:
- Discipline views → Discipline scheme
- Coordination views → Monochrome scheme
- Presentation views → Custom scheme

Set via: Tag Studio → Style tab → Set View Tag Style

---

## 15. Real-Time Auto-Tagging

### How It Works

The `StingAutoTagger` is an `IUpdater` that monitors element creation in real-time:

1. **Trigger**: Element added to model (22 categories monitored)
2. **Check**: Workset ownership (skip if not owned in worksharing)
3. **Check**: Discipline filter (skip if filtered out)
4. **Pipeline**: Full 11-step `RunFullPipeline()` execution
5. **Visual**: Optional `IndependentTag` annotation placement
6. **Cache**: Performance via LRU eviction (10K entries)

### Enable/Disable

| Command | Location | What It Does |
|---------|----------|-------------|
| Auto-Tagger Toggle | CREATE → Setup | Enable/disable data auto-tagging |
| Auto-Tagger Visual | CREATE → Setup | Enable/disable annotation placement |
| Auto-Tagger Config | CREATE → Setup | Set discipline filter |

### Discipline Filter

Restrict auto-tagging to specific disciplines:
- Set via `AUTO_TAGGER_DISC_FILTER` in project_config.json
- Example: "M,E,P" — only tag Mechanical, Electrical, Plumbing
- Empty string = tag all disciplines

### Bulk Paste Queue

When >50 elements are pasted at once, they're queued for deferred processing rather than dropped. Queue drains during next Revit idle or document sync.

---

## 16. Stale Element Detection & Re-Tagging

### What Is "Stale"?

An element is **stale** when its physical context has changed since it was last tagged:
- Moved to a different level (LVL changed)
- Moved to a different room (LOC/ZONE changed)
- MEP system reassigned (SYS changed)
- Geometry significantly modified

### How Stale Detection Works

`StingStaleMarker` (IUpdater) monitors geometry changes:
1. Triggers on `Element.GetChangeTypeGeometry()` for 22 categories
2. Compares current LVL/LOC/ZONE/SYS against stored values
3. If mismatch detected → sets `STING_STALE_BOOL = 1`
4. 500ms debounce timer prevents thundering-herd during bulk operations
5. Stale elements appear in compliance dashboard and morning briefing

### Re-Tagging Stale Elements

**How**: CREATE → QA → Retag Stale (or via MorningHealthCheck workflow)

1. Finds all elements with `STING_STALE_BOOL = 1`
2. Runs full `RunFullPipeline()` on each
3. Clears stale flag after successful re-tag
4. Reports count and compliance improvement

### Select Stale Elements

**How**: SELECT → State → Select Stale

Selects all stale elements for visual inspection before re-tagging.

---

## 17. Tag Operations (ORGANISE Tab)

### Tag Operations (7 Commands)

| Command | Description |
|---------|-------------|
| Tag Selected | Tag selected elements only |
| Delete Tags | Clear all 15 tag params (with confirmation) |
| Renumber | Re-sequence within groups (spatial sort) |
| Copy Tags | Copy from first selected to all others |
| Swap Tags | Swap all values between 2 elements |
| Re-Tag | Force re-derive with overwrite |
| Fix Duplicates | Auto-increment SEQ on duplicates |

### Analysis (7 Commands)

| Command | Description |
|---------|-------------|
| Audit to CSV | Export 40+ column tag audit |
| Find Duplicates | Locate duplicate tag values |
| Highlight Invalid | Color-code missing (red) and incomplete (orange) |
| Clear Overrides | Reset graphic overrides |
| Select by Discipline | Select all elements of a discipline |
| Tag Statistics | Counts by discipline/system/level |
| Tag Register | Comprehensive asset register CSV |

### Annotation Colors (5 Commands)

| Command | Description |
|---------|-------------|
| Color by Discipline | Color-code annotation tags by discipline |
| Set Tag Text Color | Set text color for selected tags |
| Set Leader Color | Set leader line color |
| Split Tag/Leader | Different colors for leader vs text |
| Clear Colors | Remove all annotation color overrides |

### Tag Appearance (5 Commands)

| Command | Description |
|---------|-------------|
| Tag Appearance | Configure overall visual appearance |
| Set Box Appearance | Tag box/border settings |
| Quick Tag Style | Apply quick style presets |
| Set Line Weight | Tag annotation line weight |
| Color by Parameter | Color tags by any parameter value |

---

## 18. Leader Management (14 Commands)

| Command | Description |
|---------|-------------|
| Toggle Leaders | Turn leaders on/off for selected tags |
| Add Leaders | Add leaders to all selected tags |
| Remove Leaders | Remove leaders from selected tags |
| Align Tags | Align heads horizontally/vertically/row |
| Reset Positions | Move tags back to element centers |
| Toggle Orientation | Switch horizontal ↔ vertical |
| Snap Elbows | Snap to 45° or 90° angles |
| Auto-Align Text | Auto-align leader text positions |
| Flip Tags | Mirror across element center |
| Align Text | Left/center/right text alignment |
| Pin/Unpin | Lock tags in place |
| Nudge | Fine-adjust positions |
| Attach/Free | Attach leader end to host or set free |
| Select by Leader | Select tags with/without leaders |

---

## 19. Legend Building

### Legend Types (31 Commands)

| Legend | Description |
|--------|-------------|
| Discipline Legend | Color-coded discipline codes |
| System Legend | System types with colors |
| Tag Legend | Tag format explanation |
| Color Legend | Active color scheme reference |
| Material Legend | Material types with swatches |
| Equipment Legend | Equipment types catalogue |
| Fire Rating Legend | Fire rating classifications |
| Template Legend | View template reference |
| Master Pipeline | All legends in sequence |

### Legend Engine

Creates drafting view legends with:
- FilledRegion color swatches
- TextNote labels
- Multi-column grid layout
- Auto-sizing to content


---

## 20. Workflow Automation for Tagging

### Recommended Tagging Workflows

#### Initial Project Tagging

```
1. Master Setup (TEMP → Setup → Master Setup)
   └── Binds parameters, creates materials/types/schedules/templates

2. Tag & Combine (CREATE → Tag & Combine → Entire Project)
   └── Full pipeline on all elements

3. Validate Tags (CREATE → QA → Validate)
   └── Check compliance, identify gaps

4. Resolve All Issues (CREATE → More → Resolve All)
   └── Fix remaining placeholders/invalids

5. Validate Tags again (verify improvement)
```

#### Daily Incremental Tagging

```
1. Tag New Only (CREATE → More → Tag New Only)
   └── Tags elements added since last session

2. Retag Stale (CREATE → QA → Retag Stale)
   └── Re-derives tags for moved elements

3. Quick Validate (CREATE → QA → Dashboard)
   └── Check compliance status
```

#### Pre-Handover Tagging QA

```
1. Pre-Tag Audit (CREATE → More → Pre-Tag Audit)
   └── Dry-run to identify issues

2. Auto-Fix (click auto-fix button in audit results)
   └── AnomalyAutoFix → ResolveAllIssues

3. Validate Tags (CREATE → QA → Validate)
   └── Verify 4-bucket compliance

4. Tag Register Export (ORGANISE → Analysis → Tag Register)
   └── CSV for external review

5. COBie Export (BIM → COBie Export)
   └── Final deliverable
```

### Automated Workflow Presets

| Preset | When | Steps |
|--------|------|-------|
| DailyQA | Every morning | RetagStale → PreTagAudit → TagNew → Validate → Dashboard |
| PostTaggingQA | After major tagging | PreTagAudit → Validate → Dashboard → Register → ValidateTemplate |
| MorningHealthCheck | Start of day | RetagStale → WarningsFix → TagNew → Audit → Validate → Templates → Sheets → RevCheck |
| HandoverReadiness | Before handover | RetagStale → FullTag → Validate → Templates → COBie → Register → BOQ → BEP → Revision |
| WeeklyDataDrop | Weekly | RetagStale → Resolve → Validate → Register → COBie → Sheets → Register → Revision |

### Custom Workflow JSON

Create custom workflows in `data/WORKFLOW_MyWorkflow.json`:

```json
{
    "Name": "My Custom Tagging QA",
    "Description": "Custom QA for this project",
    "Steps": [
        {
            "CommandTag": "RetagStale",
            "Label": "Fix stale elements",
            "RequiresStaleElements": true
        },
        {
            "CommandTag": "TagNewOnly",
            "Label": "Tag new elements",
            "Conditions": ["has_untagged"],
            "ConditionLogic": "AND"
        },
        {
            "CommandTag": "ValidateTags",
            "Label": "Validate compliance"
        },
        {
            "CommandTag": "WarningsAutoFix",
            "Label": "Fix warnings",
            "Conditions": ["has_warnings"],
            "FallbackStep": "WarningsDashboard"
        },
        {
            "CommandTag": "CompletenessDashboard",
            "Label": "Show compliance dashboard",
            "MinCompliancePct": 50
        }
    ],
    "RollbackOnOptionalFailure": false
}
```

---

## 21. Cross-System Integration

### Tagging → Other Systems

| System | How Tags Integrate |
|--------|-------------------|
| **COBie** | All 8 tokens populate COBie Component fields |
| **Warnings** | Stale elements appear as synthetic warnings |
| **Issues** | Tags link elements to BCF issues |
| **Revisions** | Tag snapshots track changes between revisions |
| **Excel** | 30+ columns exported with full tag data |
| **Schedules** | Tags populate schedule fields |
| **Legends** | Tag data drives legend content |
| **BEP** | Compliance % enriches BEP auto-generation |
| **Transmittals** | Document naming from tag data |
| **4D/5D** | Tags identify elements for scheduling/costing |

### Compliance Scan Integration

The real-time compliance scan:
- Updates status bar every 30 seconds
- Per-discipline breakdown
- Container compliance tracking
- Stale element counting
- Phase-based compliance (per Revit phase)
- Feeds into morning briefing, BEP enrichment, CDE gates

---

## 22. Data Exchange (Excel/COBie)

### Excel Export Columns

| Group | Columns |
|-------|---------|
| Tags | DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ, TAG1 |
| Identity | ElementId, UniqueId, Category, Family, Type, Mark |
| Spatial | Level, Room, Grid Reference, Phase |
| MEP | Flow, Pressure, Voltage, Circuit, Power, Connected Load |
| Dimensions | Width, Height, Length, Area, Volume |
| Status | STATUS, REV, Stale, Display Mode |
| Classification | Uniformat, OmniClass, Keynote |
| Cost | Estimated Cost, Unit Rate |

### Excel Import Validation

7-token validation pipeline:
1. DISC against valid discipline codes
2. LOC against valid location codes
3. ZONE against valid zone codes
4. SYS against valid system codes
5. FUNC against valid function codes + SYS cross-check
6. PROD against valid product codes + FUNC cross-check
7. SEQ against numeric format

CLEAR sentinel: Type "CLEAR" in any cell to intentionally empty a field.

### COBie Integration

Tags populate COBie worksheets:
- **Component**: TAG1 → AssetIdentifier, tokens → Category fields
- **Type**: Family + Type → Type fields, PROD → Category
- **System**: SYS → SystemCategory, connected elements
- **Space**: Room + LOC + ZONE → Floor/Space sheets
- **Attribute**: 70+ STING parameters exported as attributes

---

## 23. Graitec-Style Numbering

### NumberingEngine

Template-based element numbering with 5 styles:

| Style | Format | Example |
|-------|--------|---------|
| Numeric | 001, 002, 003 | C-001, C-002 |
| Capital Letters | A, B, C, ... AA, AB | C-A, C-B |
| Lower Letters | a, b, c, ... aa, ab | C-a, C-b |
| Capital Romans | I, II, III, IV | C-I, C-II |
| Lower Romans | i, ii, iii, iv | C-i, C-ii |

### Grouping Algorithms

| Algorithm | Groups By | Sort Within |
|-----------|----------|------------|
| None | No grouping | Spatial (X,Y) |
| ByLevel | Level name | Spatial per level |
| ByType | Family type | Spatial per type |
| ByGridLine | Nearest grid | Along grid |
| ByLocation | Room/Zone | Spatial per zone |
| ByMark | Mark value | Spatial per mark |

### Configuration

```
Prefix: C-
Separator: -
Suffix: (none)
Start From: 1
Digits: 3
Increment: 1
Style: Numeric
Omit Already Numbered: true
```

Available in DWG-to-BIM wizard and Tag Studio.

---

## 24. Tag Export/Import Between Projects

### Export Tag Map

**How**: (dispatch: ExportTagMap)

Exports all tagged elements to `.sting_tagmap.json`:
- UniqueId, family, type
- XYZ location coordinates
- All 8 tokens + STATUS + REV
- Used for cross-project tag transfer

### Import Tag Map

**How**: (dispatch: ImportTagMap)

Matching strategy:
1. **UniqueId match** (exact — same element across models)
2. **Family + Type + Location** (fallback — 500mm radius tolerance)

Use cases:
- Transfer tags across linked models
- Maintain tags through model splits
- Carry tags forward to next project phase

---

## 25. Configuration Reference

### project_config.json Keys

| Key | Default | Description |
|-----|---------|-------------|
| TAG_PREFIX | "" | Prefix added to all tags |
| TAG_SUFFIX | "" | Suffix added to all tags |
| TAG_FORMAT.separator | "-" | Separator between segments |
| TAG_FORMAT.num_pad | 4 | SEQ zero-padding width |
| CATEGORY_SKIP | [] | Categories to skip during tagging |
| CATEGORY_FORCE_SYS | {} | Override SYS per category |
| CATEGORY_TOKEN_OVERRIDES | {} | Per-category token enforcement |
| SEQ_SCHEME | "Numeric" | Numbering scheme |
| SEQ_INCLUDE_ZONE | false | Include zone in SEQ grouping |
| COMPLIANCE_GATE_PCT | 80 | Post-tagging compliance gate |
| PROXIMITY_RADIUS_FT | 10.0 | CopyTokensFromNearest radius |
| CUSTOM_VALID_DISC | [] | Additional valid DISC codes |
| CUSTOM_VALID_SYS | [] | Additional valid SYS codes |
| CUSTOM_VALID_FUNC | [] | Additional valid FUNC codes |
| CUSTOM_VALID_LOC | [] | Additional valid LOC codes |
| CUSTOM_VALID_ZONE | [] | Additional valid ZONE codes |
| AUTO_TAGGER_VISUAL | false | Visual tag placement on auto-tag |
| AUTO_TAGGER_DISC_FILTER | "" | Discipline filter for auto-tagger |
| SEQ_RANGE_ALLOCATION | {} | Per-discipline SEQ ranges |
| DISCIPLINE_LEADS | {} | Discipline → lead name mapping |

---

## 26. Troubleshooting

### Common Issues

| Problem | Cause | Solution |
|---------|-------|----------|
| Tags show XX/ZZ/GEN | No rooms, missing Project Info | Place rooms, set building name in Project Info |
| Tags show 0000 SEQ | SEQ sidecar missing | Run Assign Numbers to rebuild counters |
| Containers empty | Parameters not bound | Run Load Params (TEMP → Setup) |
| FUNC wrong for SYS | Cross-discipline mismatch | Run Resolve All Issues |
| PROD shows GEN | Family name not recognized | Add family pattern to PROD mapping |
| Stale elements not detected | StingStaleMarker disabled | Ensure auto-tagger is loaded (check OnStartup) |
| Auto-tagger not firing | Disabled by default | CREATE → Setup → Auto-Tagger Toggle |
| SEQ numbers restart at 1 | Sidecar file deleted | Run Tag & Combine to rebuild counters |
| Tags overwritten unexpectedly | Collision mode = Overwrite | Use Skip or Auto-Increment mode |
| Token lock not working | ASS_TOKEN_LOCK_TXT not set | Set parameter value (e.g., "DISC,LOC") |
| Formula errors | Missing input parameters | Run NativeParamMapper first |
| Grid reference empty | No grids in model | Place column grids |

### Performance Tips

- **Tag New Only** is 10x faster than Batch Tag on partially-tagged models
- **View-scoped tagging** reduces element count on large models
- **Formula session cache** (5-min TTL) prevents redundant CSV reads
- **Grid line cache** (2-min TTL) prevents repeated collector scans
- **Incremental ComplianceScan** provides O(1) post-tag updates
- **Selective WriteContainers** skips irrelevant discipline containers

---

## 27. Complete Command Reference (35+ Commands)

### CREATE Tab

| # | Command | Class | Transaction | Description |
|---|---------|-------|-------------|-------------|
| 1 | Auto Tag | AutoTagCommand | Manual | Tag active view elements |
| 2 | Batch Tag | BatchTagCommand | Manual | Tag entire project |
| 3 | Tag & Combine | TagAndCombineCommand | Manual | Full pipeline + all containers |
| 4 | Tag New Only | TagNewOnlyCommand | Manual | Only untagged elements |
| 5 | Pre-Tag Audit | PreTagAuditCommand | ReadOnly | Dry-run prediction |
| 6 | Family-Stage Populate | FamilyStagePopulateCommand | Manual | Pre-populate 7 tokens |
| 7 | Set Discipline | SetDiscCommand | Manual | Set DISC code |
| 8 | Set Location | SetLocCommand | Manual | Set LOC code |
| 9 | Set Zone | SetZoneCommand | Manual | Set ZONE code |
| 10 | Set Status | SetStatusCommand | Manual | Set STATUS |
| 11 | Assign Numbers | AssignNumbersCommand | Manual | Sequential SEQ |
| 12 | Build Tags | BuildTagsCommand | Manual | Rebuild TAG1 from tokens |
| 13 | Combine Parameters | CombineParametersCommand | Manual | Write to containers |
| 14 | Validate Tags | ValidateTagsCommand | ReadOnly | 4-bucket compliance |
| 15 | Completeness Dashboard | CompletenessDashboardCommand | ReadOnly | Per-discipline RAG |
| 16 | Resolve All Issues | ResolveAllIssuesCommand | Manual | One-click fix |
| 17 | Fix Duplicates | FixDuplicateTagsCommand | Manual | Auto-increment SEQ |
| 18 | Tag Config | TagConfigCommand | ReadOnly | View configuration |
| 19 | Load Params | LoadSharedParamsCommand | Manual | Bind parameters |
| 20 | Configure | ConfigEditorCommand | ReadOnly | Edit project_config |
| 21 | Set SEQ Scheme | SetSeqSchemeCommand | Manual | Change numbering scheme |

### ORGANISE Tab

| # | Command | Description |
|---|---------|-------------|
| 22 | Tag Selected | Tag selected elements |
| 23 | Delete Tags | Clear all tag parameters |
| 24 | Renumber | Re-sequence within groups |
| 25 | Copy Tags | Copy from first to all |
| 26 | Swap Tags | Swap between 2 elements |
| 27 | Re-Tag | Force re-derive |
| 28 | Audit to CSV | Export tag audit |
| 29 | Find Duplicates | Locate duplicates |
| 30 | Highlight Invalid | Color-code invalid tags |
| 31 | Tag Statistics | Counts by disc/sys/level |
| 32 | Tag Register | 40+ column CSV export |
| 33 | Anomaly Auto-Fix | Detect and fix 8+ anomaly types |
| 34 | Retag Stale | Re-tag moved elements |
| 35 | Discipline Compliance | Per-discipline report |

### TAGS Tab (Tag Studio)

| # | Command | Description |
|---|---------|-------------|
| 36 | Smart Place Tags | 16-position collision avoidance |
| 37 | Arrange Tags | Auto-arrange to grid |
| 38 | Batch Place Tags | Multi-view placement |
| 39 | Apply Tag Style | Size/style/color dialog |
| 40 | Apply Color Scheme | Named scheme to view |
| 41 | Set Display Mode | 5 display mode options |
| 42 | Set Paragraph Depth | TAG7 visibility tiers |

---

## 28. Deep Insights: How the Tagging Engine Works Internally

This section provides deep technical insights for advanced users who want to understand, troubleshoot, or teach others about the tagging engine internals.

### 28.1 The 11-Step RunFullPipeline

Every tagged element passes through exactly 11 steps in `TagPipelineHelper.RunFullPipeline()`:

```
Step 1: Category Filter    → Skip elements in CategorySkipList
Step 2: Audit Trail        → Capture previous TAG1 value to ASS_TAG_PREV_TXT
Step 3: TypeTokenInherit   → Copy DISC/SYS/FUNC/PROD from family TYPE to instance
Step 4: PopulateAll        → Auto-derive all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
Step 5: CategoryForceSys   → Override SYS for specific categories (from config)
Step 6: CategoryOverrides  → Apply per-category token overrides (from config)
Step 7: Token Lock         → Restore user-locked tokens that were overwritten
Step 8: NativeParamMapper  → Map 30+ Revit built-in parameters to STING params
Step 9: FormulaEngine      → Evaluate 199 dependency-ordered formulas
Step 10: BuildAndWriteTag  → Assemble 8-segment tag with collision detection + write
Step 11: WriteContainers   → Write to all 53 discipline-specific containers + TAG7
```

**Key insight**: Steps 3-7 are about TOKEN POPULATION (deriving values). Steps 8-9 are about DATA ENRICHMENT (native params + formulas). Steps 10-11 are about TAG ASSEMBLY (building the final tag string).

### 28.2 Token Derivation Priority (How Each Token Gets Its Value)

Each token follows a priority chain — first non-empty value wins:

| Token | Priority 1 (Highest) | Priority 2 | Priority 3 | Priority 4 (Default) |
|-------|---------------------|-----------|-----------|---------------------|
| **DISC** | User-locked value | Type parameter | Category→DiscMap lookup | "GEN" |
| **LOC** | User-locked value | Type parameter | SpatialAutoDetect.DetectLoc (room name/number) | "BLD1" |
| **ZONE** | User-locked value | Type parameter | SpatialAutoDetect.DetectZone (room department) | "Z01" |
| **LVL** | User-locked value | — | GetLevelCode (element level, GF/L01/B1/RF) | "XX" |
| **SYS** | User-locked value | MEP System API name | ConnectorInherit (connected elements) | Category→SysMap lookup |
| **FUNC** | User-locked value | SYS→FuncMap lookup | CopyTokensFromNearest (10ft radius) | "GEN" |
| **PROD** | User-locked value | GetFamilyAwareProdCode (35+ family patterns) | Category→ProdMap lookup | "GEN" |
| **SEQ** | — | — | Auto-increment per (DISC, SYS, FUNC, PROD) group | "0001" |

### 28.3 SEQ Numbering Deep Dive

SEQ numbers are managed by a counter system that persists across sessions:

1. **Counter Key Format**: `{DISC}_{SYS}_{FUNC}_{PROD}` (e.g., `M_HVAC_SUP_AHU`)
2. **Counter Storage**: `.sting_seq.json` sidecar file alongside the `.rvt`
3. **Counter Merge**: On load, max-per-key between project params and sidecar
4. **Collision Detection**: O(1) HashSet lookup of all existing TAG1 values
5. **Collision Resolution**: Auto-increment SEQ until unique (max 10,000 attempts)
6. **Range Allocation**: Optional per-discipline ranges via `SEQ_RANGE_ALLOCATION` config

**Common pitfall**: If you change the SEQ scheme mid-project (e.g., Numeric → Alpha), existing counters under the old scheme are preserved but new elements start fresh. Use `SetSeqScheme` command to manage transitions.

### 28.4 Performance Characteristics

| Operation | 1K Elements | 10K Elements | 50K Elements |
|-----------|------------|-------------|-------------|
| AutoTag (view) | ~2 sec | ~15 sec | N/A (view-scoped) |
| BatchTag (project) | ~5 sec | ~45 sec | ~4 min |
| TagAndCombine | ~8 sec | ~60 sec | ~5 min |
| ComplianceScan | ~0.5 sec | ~3 sec | ~8 sec (timeout) |
| ValidateTags | ~1 sec | ~8 sec | ~30 sec |

**Performance tips**:
- Use `TagNewOnly` instead of `BatchTag` for incremental updates (10-100x faster)
- Enable `PERF_TRACKING_ENABLED` in project_config.json for element-level profiling
- The auto-tagger (IUpdater) has a 5-second context TTL to avoid constant rebuilds
- Formula session cache has 5-minute TTL — first tag command loads, subsequent commands reuse
- Grid line cache has 2-minute TTL per document

### 28.5 Caching Architecture

The tagging engine uses a multi-layer caching system:

| Cache | Scope | TTL | Invalidation |
|-------|-------|-----|--------------|
| PopulationContext | Per-batch | Batch lifetime | Built fresh per command |
| Formula CSV | Session | 5 minutes | InvalidateSessionCaches() |
| Grid lines | Per-document | 2 minutes | InvalidateSessionCaches() |
| ComplianceScan | Project | 30 seconds | InvalidateCache() |
| AutoTagger context | IUpdater | 5 seconds | InvalidateContext() |
| Parameter cache | Per-document | Session | ClearParamCache() on doc switch |
| Spatial candidates | Per-batch | Batch lifetime | Built in PopulationContext.Build() |
| SEQ sidecar | Persistent | File-based | SaveSeqSidecar() after commits |

**Critical rule**: After ANY tagging operation, call ALL THREE invalidation methods:
```
ComplianceScan.InvalidateCache();
StingAutoTagger.InvalidateContext();
TagConfig.SaveSeqSidecar(doc);
```

### 28.6 Common Troubleshooting Patterns

| Symptom | Root Cause | Fix |
|---------|-----------|-----|
| Tags show 0% compliance after config change | ComplianceScan using stale separator | Run any tag command (triggers InvalidateCache) |
| Duplicate SEQ numbers after session restart | Sidecar not saved | Ensure SaveSeqSidecar runs after tx.Commit() |
| Empty containers despite TAG1 populated | WriteContainers skipped | Run CombineParameters or Re-Tag |
| Auto-tagger not tagging new elements | IUpdater disabled | Check AutoTaggerToggle status |
| Stale elements not detected after move | StaleMarker overflow (>100 elements) | Elements now queued for deferred processing |
| FUNC always "GEN" for MEP elements | MEP system not connected | Connect to MEP system or use CopyTokensFromNearest |
| TAG7 narrative incomplete | Empty FUNC token | Populate FUNC before TAG7 generation |
| Token lock not working | Lock string format wrong | Use comma-separated token names: "DISC,SYS,PROD" |

---

*This guide is maintained alongside the STING Tools codebase. For the latest version, check `Data/TAGGING_GUIDE.md`.*

*Related guides:*
- [BIM Coordination Workflow Guide](BIM_COORDINATION_WORKFLOW_GUIDE.md)
- [DWG to BIM Guide](DWG_TO_BIM_GUIDE.md)
- [Tag Family Creation Guide](TAG_FAMILY_CREATION_GUIDE.md)
