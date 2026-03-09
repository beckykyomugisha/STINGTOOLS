# STING Tag Family Creation Guide

## Complete Workflow for Creating, Configuring, and Deploying ISO 19650 Tag Families

This guide covers the end-to-end process for building STING tag families that display ISO 19650 asset tags (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ) on Revit model elements. It spans four commands, one data file, one engine, and manual Family Editor steps.

---

## Table of Contents

1. [Overview and Architecture](#1-overview-and-architecture)
2. [Prerequisites](#2-prerequisites)
3. [Phase 1 — Create Tag Families](#3-phase-1--create-tag-families)
4. [Phase 2 — Configure Labels (Family Editor)](#4-phase-2--configure-labels-family-editor)
5. [Phase 3 — Load Tag Families](#5-phase-3--load-tag-families)
6. [Phase 4 — Audit Tag Coverage](#6-phase-4--audit-tag-coverage)
7. [Phase 5 — Presentation Mode and Paragraph Depth](#7-phase-5--presentation-mode-and-paragraph-depth)
8. [Phase 6 — Tag Style Engine (Visual Appearance)](#8-phase-6--tag-style-engine-visual-appearance)
9. [Phase 7 — Seed Family Strategy (Skip Phase 2 Next Time)](#9-phase-7--seed-family-strategy-skip-phase-2-next-time)
10. [Batch Family Parameter Binding](#10-batch-family-parameter-binding)
11. [Category Reference Table](#11-category-reference-table)
12. [Parameter Reference](#12-parameter-reference)
13. [Troubleshooting](#13-troubleshooting)
14. [File Reference](#14-file-reference)

---

## 1. Overview and Architecture

### What Is a STING Tag Family?

A STING tag family is a Revit annotation family (.rfa) that displays ISO 19650 asset tag data stored in shared parameters. Each of the 50 taggable Revit categories gets its own tag family (e.g., "STING - Mechanical Equipment Tag", "STING - Walls Tag").

### How Tag Families Work in Revit

```
Element (e.g. Air Handling Unit)
  └── Shared Parameters (written by STING tagging pipeline)
       ├── ASS_TAG_1_TXT = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"   ← Full 8-segment tag
       ├── ASS_TAG_2_TXT = "SUP-AHU-0003"                        ← Short tag
       ├── ASS_TAG_7A_TXT = "AHU | Carrier 39M | Model 060"      ← Identity header
       ├── ASS_TAG_7B_TXT = "HVAC Supply | Heating/Cooling"       ← System & function
       ├── TAG_PARA_STATE_1_BOOL = true                            ← Visibility tier 1
       ├── TAG_PARA_STATE_2_BOOL = true                            ← Visibility tier 2
       └── TAG_2BOLD_BLUE_BOOL = true                              ← Style: 2mm Bold Blue
            │
            ▼
Tag Family (STING - Mechanical Equipment Tag.rfa)
  └── Label (Edit Label configuration)
       ├── Row 1: ASS_TAG_1_TXT                     [Tier 1 — always visible]
       ├── Row 2: ASS_DESCRIPTION_TXT               [Tier 1 — always visible]
       ├── Row 3: BLE dimensions (calculated value)  [Tier 2 — gated by STATE_2_BOOL]
       ├── Row 4: ASS_TAG_7A_TXT (calculated value)  [Tier 3 — gated by STATE_3_BOOL]
       ├── Row 5: ASS_TAG_7B_TXT (calculated value)  [Tier 3 — gated by STATE_3_BOOL]
       └── Row N: TAG_{SIZE}{STYLE}_{COLOR}_BOOL     [Style matrix — one row per combo]
            │
            ▼
View (Floor Plan Level 02)
  └── IndependentTag annotation
       └── Displays: "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"
```

### The Four Commands

| # | Button Name | Location in Dockable Panel | What It Does |
|---|-------------|---------------------------|--------------|
| 1 | **`Create Tag Families`** | CREATE tab → ⚙ SETUP section (row 2) | Creates 50 .rfa files from Revit .rft templates, adds shared parameters |
| 2 | **`Configure Labels`** | CREATE tab → ⚙ SETUP section (row 2) | Opens each family in Family Editor with exact Edit Label instructions |
| 3 | **`Load`** | CREATE tab → ⚙ SETUP section (row 2, after Configure Labels) | Batch-loads .rfa files into the current project |
| 4 | **`Audit`** | CREATE tab → ⚙ SETUP section (row 2, last button) | Reports which categories have STING tags vs. other tags vs. none |

### Data Flow

```
LABEL_DEFINITIONS.json ──┐
                         ├──→ Configure Labels wizard (per-category Edit Label spec)
MR_PARAMETERS.txt ───────┤
                         ├──→ Create Tag Families (shared param definitions + GUIDs)
PARAMETER_REGISTRY.json ─┘
                         ├──→ ParamRegistry (parameter names, containers, GUIDs)
                         └──→ TagStyleEngine (BOOL parameter matrix for styles)
```

---

## 2. Prerequisites

Before creating tag families, ensure these steps are complete:

### Step 1: Verify Data Files

Run **`Check Data`** button (TEMP tab → ⚙ SETUP section) to confirm all data files are present:

| File | Purpose | Required |
|------|---------|----------|
| `MR_PARAMETERS.txt` | Shared parameter definitions (200+ params with GUIDs) | **Yes** |
| `PARAMETER_REGISTRY.json` | Master registry: param names, containers, category bindings | **Yes** |
| `LABEL_DEFINITIONS.json` | Edit Label specs: tiers, prefixes, suffixes, paragraph templates | **Yes** |

### Step 2: Load Shared Parameters

Run **`Load Shared Params`** button (CREATE tab → ⚙ SETUP section, row 1) to bind shared parameters to categories.
This is a 2-pass process:
- **Pass 1**: Universal parameters (ASS_TAG_1_TXT through ASS_TAG_7F_TXT, visibility BOOLs) → all 53 categories
- **Pass 2**: Discipline-specific parameters (BLE dimensions, MEP data, electrical, plumbing) → relevant categories only

### Step 3: Confirm Revit Version

Tag family templates (.rft) must match your Revit version. The plugin searches:
1. `app.FamilyTemplatePath` (Revit's configured path)
2. `English/Annotations/`, `English_I/Annotations/`, `Metric/Annotations/` subdirectories
3. Common install paths: `C:\ProgramData\Autodesk\RVT 2025/2026/2027\Family Templates\`

---

## 3. Phase 1 — Create Tag Families

**Command**: `CreateTagFamiliesCommand`
**Button**: **`Create Tag Families`** — CREATE tab → ⚙ SETUP section (row 2, blue button)
**Transaction**: Manual

### What Happens

1. **Locate templates** — Finds the Revit annotation tag template directory (.rft files)
2. **Check existing** — Scans project for already-loaded STING families (skips them)
3. **Check seeds** — Looks for pre-configured seed families in `Data/TagFamilies/Seeds/` (uses them directly if found)
4. **Check disk** — Looks for .rfa files from a previous run in `Data/TagFamilies/` (loads without recreating)
5. **Create new** — For each missing category:
   a. Opens the category-specific .rft template (e.g., "Mechanical Equipment Tag.rft")
   b. Falls back to "Generic Tag.rft" → "Multi-Category Tag.rft" → "Generic Annotation.rft" if specific template not found
   c. Adds shared parameters via `FamilyManager.AddParameter()`:
      - **13 tag parameters**: ASS_TAG_1_TXT through ASS_TAG_7F_TXT (the 6 sub-sections)
      - **4 visibility BOOLs**: TAG_PARA_STATE_1/2/3_BOOL, TAG_WARN_VISIBLE_BOOL
      - **ASS_DESCRIPTION_TXT**: Element description
      - **Category-specific params**: From `LABEL_DEFINITIONS.json` (BLE dimensions, MEP data, etc.)
   d. Attempts to rebind the template's default Label to ASS_TAG_1_TXT (partial success — Revit API limitation)
   e. Saves to `Data/TagFamilies/STING - {Category} Tag.rfa`
   f. Loads into the current project via `doc.LoadFamily()`

### Output

```
STING Tag Family Creation Report
==================================================
Template directory: C:\ProgramData\Autodesk\RVT 2025\Family Templates\English\Annotations
Output directory: C:\...\data\TagFamilies

  [SKIP] Mechanical Equipment — already loaded
  [SEED] Ducts — loaded pre-configured seed family
  [LOAD] Duct Fittings — loaded from existing .rfa
  [OK]   Air Terminals — created and loaded (with params)
  [MISS] Parking — no template found
  [FAIL] Ramps — NewFamilyDocument returned null

--------------------------------------------------
Created:  38
Loaded:   42
Skipped:  3 (already loaded)
Missing:  2 (no template)
Failed:   1

NEXT STEP:
Run 'Configure Labels' to open each family in the
Family Editor and set the Label to ASS_TAG_1_TXT.
```

### Family Naming Convention

All families follow the pattern: `STING - {Category Display Name} Tag`

Examples:
- `STING - Mechanical Equipment Tag`
- `STING - Walls Tag`
- `STING - Lighting Fixtures Tag`
- `STING - Cable Trays Tag`

### Priority Resolution for Each Category

```
1. Seed family exists in Data/TagFamilies/Seeds/   → Load directly (best — labels pre-configured)
2. .rfa exists in Data/TagFamilies/                 → Load from disk (from previous Create run)
3. Category-specific .rft template found            → Create new + add params
4. Generic Tag.rft / Multi-Category Tag.rft found   → Create new with fallback template
5. Any .rft file found                              → Create new with any available template
6. No template at all                               → Skip with [MISS] status
```

---

## 4. Phase 2 — Configure Labels (Family Editor)

**Command**: `ConfigureTagLabelsCommand`
**Button**: **`Configure Labels`** — CREATE tab → ⚙ SETUP section (row 2, orange button)
**Transaction**: Manual

### Why This Step Is Needed

The Revit API does not provide a programmatic way to set the Label binding in annotation tag families. The `FamilyLabel` property on `Dimension` elements works for dimensional labels but not for text annotation labels. Therefore, this step requires manual interaction in the Family Editor.

### What the Wizard Does

For each loaded STING tag family:

1. Shows the family name and remaining count
2. Provides the **exact Edit Label specification** from `LABEL_DEFINITIONS.json`
3. Opens the family in the Family Editor via `doc.EditFamily(fam)`
4. Displays a reminder with the full parameter table

### The Edit Label Specification

Each category has a detailed spec with three tiers, formatted as a table:

```
EDIT LABEL SPECIFICATION: Mechanical Equipment
────────────────────────────────────────────────────────────────

── TIER 1 (Always Visible) ──
Add these parameters directly:

  Parameter                    | Prefix         | Suffix         | Spc | Brk
  ────────────────────────────────────────────────────────────────────────────────
  ASS_TAG_1_TXT                |                |                | 0   | YES
  ASS_DESCRIPTION_TXT          |                |                | 0   | YES

── TIER 2 (Standard+) ──
Use CALCULATED VALUES for each parameter:
  Formula: if(TAG_PARA_STATE_2_BOOL, <param>, "")

  Parameter                    | Prefix         | Suffix         | Spc | Brk
  ────────────────────────────────────────────────────────────────────────────────
  BLE_WALL_THICKNESS_MM        | "Thickness: "  | " mm"          | 0   | YES
  BLE_WALL_UVALUE              | "U-value: "    | " W/m²K"       | 0   | YES

── TIER 3 (Comprehensive) ──
Use CALCULATED VALUES for each parameter:
  Formula: if(TAG_PARA_STATE_3_BOOL, <param>, "")

  (category-specific additional parameters)

── TAG7 NARRATIVE (Rich Description — Auto-Generated) ──
Add these TAG7 sub-section parameters to the label:

  Parameter                    | Content           | Style    | Brk
  ────────────────────────────────────────────────────────────────────────
  ASS_TAG_7A_TXT               | Identity Header   | Bold     | YES
  ASS_TAG_7B_TXT               | System & Function | Italic   | YES
  ASS_TAG_7C_TXT               | Spatial Context   | Normal   | YES
  ASS_TAG_7D_TXT               | Lifecycle/Status  | Normal   | YES
  ASS_TAG_7E_TXT               | Technical Specs   | Bold     | YES
  ASS_TAG_7F_TXT               | Classification    | Italic   | YES

  For all TAG7 rows, use Calculated Values:
    if(TAG_PARA_STATE_3_BOOL, ASS_TAG_7x_TXT, "")

Settings: Check 'Wrap between parameters only'
```

### Step-by-Step Manual Process (Per Family)

1. **Click the Label text element** — In the family view, click the default label text (usually shows "Type Mark" or similar)

2. **Open Edit Label** — In the Properties panel, click "Edit Label"

3. **Remove default parameter** — Select the existing parameter row → click "Remove" (×)

4. **Add Tier 1 parameters** — Click "Add Parameter" (→) for each:
   - `ASS_TAG_1_TXT` — No prefix, no suffix, Break = YES
   - `ASS_DESCRIPTION_TXT` — No prefix, no suffix, Break = YES

5. **Add Tier 2 parameters** (with calculated value gating):
   - Click "Add Parameter" → select the parameter
   - Click the "fv" (formula value) button next to it
   - Enter: `if(TAG_PARA_STATE_2_BOOL, <actual_param_name>, "")`
   - Set Prefix and Suffix as specified in the tier table
   - Set Break = YES for line wrapping

6. **Add Tier 3 / TAG7 parameters** (same pattern with STATE_3 gate):
   - Formula: `if(TAG_PARA_STATE_3_BOOL, <actual_param_name>, "")`

7. **Add style matrix rows** (for TagStyleEngine — optional, for advanced multi-style families):
   - Each `TAG_{SIZE}{STYLE}_{COLOR}_BOOL` parameter controls visibility of a specific style row
   - Only one BOOL is true at a time per element type

8. **Set wrapping** — Check "Wrap between parameters only"

9. **Load into Project** — Click "Load into Project and Close" in the ribbon

### Calculated Value Formulas

| Gate | Formula | When Visible |
|------|---------|-------------|
| Tier 2 | `if(TAG_PARA_STATE_2_BOOL, <param>, "")` | Technical, Presentation, BOQ, Full Spec modes |
| Tier 3 | `if(TAG_PARA_STATE_3_BOOL, <param>, "")` | Full Specification mode only |
| Warning | `if(TAG_WARN_VISIBLE_BOOL, <param>, "")` | Technical and Full Specification modes |
| TAG7 A-F | `if(TAG_PARA_STATE_3_BOOL, ASS_TAG_7x_TXT, "")` | Full Specification mode only |

### TAG7 Sub-Section Styling

When adding TAG7 rows to Edit Label, set these text format options:

| Parameter | Content | Bold | Italic | Underline | Color Hint |
|-----------|---------|------|--------|-----------|------------|
| ASS_TAG_7A_TXT | Identity Header | **Yes** | No | No | Blue |
| ASS_TAG_7B_TXT | System & Function | No | *Yes* | No | Green |
| ASS_TAG_7C_TXT | Spatial Context | No | No | No | Orange |
| ASS_TAG_7D_TXT | Lifecycle/Status | No | No | No | Red |
| ASS_TAG_7E_TXT | Technical Specs | **Yes** | No | No | Purple |
| ASS_TAG_7F_TXT | Classification | No | *Yes* | No | Grey |

The TAG7 heading (ASS_TAG_2_TXT) style can be controlled via the **`TAG7 Heading Style`** button (CREATE tab → ⚙ PARAGRAPH & PRESENTATION section, orange button):
- Default Tier 2: Underline only
- Default Tier 3: Bold + Underline
- Options: Bold Only, Italic Only, Underline Only, Bold + Underline, Bold + Italic, All

---

## 5. Phase 3 — Load Tag Families

**Command**: `LoadTagFamiliesCommand`
**Button**: **`Load`** — CREATE tab → ⚙ SETUP section (row 2, after Configure Labels)
**Transaction**: Manual (single transaction for all families — crash-safe)

### When to Use

- After creating and configuring families via Phase 1 (`Create Tag Families`) + Phase 2 (`Configure Labels`)
- When opening a new project that needs STING tags
- After manually editing .rfa files outside of Revit

### What It Does

1. Scans `Data/TagFamilies/` for `STING - *.rfa` files
2. Checks which are already loaded in the project (skips duplicates)
3. Loads all new families in a **single transaction** (prevents Revit crash from rapid-fire commits)
4. Uses `TagFamilyLoadOptions` which always overwrites existing families with updated versions

### Output

```
Loaded 38 tag families

Found: 44 .rfa files
Loaded: 38
Skipped: 3 (already loaded)
Failed: 3
```

---

## 6. Phase 4 — Audit Tag Coverage

**Command**: `AuditTagFamiliesCommand`
**Button**: **`Audit`** — CREATE tab → ⚙ SETUP section (row 2, last button)
**Transaction**: ReadOnly

### What It Reports

For each of the 50 taggable categories:

| Status | Meaning |
|--------|---------|
| `[STING]` | STING tag family loaded — full ISO 19650 tag support |
| `[OTHER]` | Non-STING tag family loaded (e.g., default Revit tags) — tags work but not STING-formatted |
| `[NONE]` | No tag family loaded — category cannot be visually tagged |

### Output

```
STING Tag Family Audit
==================================================
  [STING] Mechanical Equipment
  [STING] Ducts
  [STING] Duct Fittings
  [OTHER] Air Terminals — using 'M_Air Terminal Tag'
  [NONE]  Parking — NO tag family loaded

--------------------------------------------------
STING tags: 42
Other tags: 5
Missing:    3
Coverage:   94%

NOTE: 44 .rfa files exist on disk but only 42 loaded.
Run the `Load` button to load them.
```

---

## 7. Phase 5 — Presentation Mode and Paragraph Depth

### Presentation Modes

**Command**: `SetPresentationModeCommand`
**Button**: **`Presentation Mode`** — CREATE tab → ⚙ PARAGRAPH & PRESENTATION section (green button)

One-click switching between corporate-standard tag display modes:

| Mode | State 1 | State 2 | State 3 | Warnings | Use Case |
|------|---------|---------|---------|----------|----------|
| **Compact** | ON | OFF | OFF | OFF | Quick labels — tag code + type name only |
| **Technical** | ON | ON | OFF | ON | Engineering docs — key properties + threshold warnings |
| **Full Specification** | ON | ON | ON | ON | Detail sheets — complete paragraph with all tiers + TAG7 |
| **Presentation** | ON | ON | OFF | OFF | Client presentation — clean tags without warnings |
| **BOQ** | ON | ON | OFF | OFF | Bill of Quantities — identity + quantities for cost schedules |

These set `TAG_PARA_STATE_1/2/3_BOOL` and `TAG_WARN_VISIBLE_BOOL` as **Type parameters** on all element types in the project. All instances of the same type change simultaneously.

### Paragraph Depth (Granular)

**Command**: `SetParagraphDepthCommand`
**Button**: **`Paragraph Depth`** — CREATE tab → ⚙ PARAGRAPH & PRESENTATION section (blue button)

For finer control, set paragraph depth per selection or project-wide:
- **Compact (State 1)** — Tier 1 only: basic identity and dimensions
- **Standard (State 2)** — Tiers 1+2: adds materials, thermal, acoustic data
- **Comprehensive (State 3)** — Tiers 1+2+3: full specification with regulatory, sustainability, QA

### Extended Paragraph Depth (1-10 Tiers)

**Command**: `SetParagraphDepthExtCommand`
**Button**: **`Set Depth (1-10)`** — VIEW tab → TAG STYLE ENGINE section → PARAGRAPH DEPTH sub-section

For even finer control with up to 10 visibility tiers:
- Tiers 1-3: Original compact/standard/comprehensive
- Tiers 4-6: Extended detail (MEP specs, material properties, classification codes)
- Tiers 7-10: Full specification (complete BOQ data, maintenance info, lifecycle data)

### Warning Visibility

**Command**: `ToggleWarningVisibilityCommand`
**Button**: **`Toggle Warnings`** — CREATE tab → ⚙ PARAGRAPH & PRESENTATION section (orange button)

Shows/hides threshold warning text in tags. 34 warning checks cover:
- U-values, voltage drop, conduit fill, pipe velocity
- Fire rating, stair dimensions, ramp slopes, acoustic levels

Example warning text: `[!U > 0.70]`, `[!VD > 4%]`

---

## 8. Phase 6 — Tag Style Engine (Visual Appearance)

The TagStyleEngine controls tag appearance through a **BOOL parameter matrix**. Each tag family can have multiple label rows, each gated by a different BOOL. Only one BOOL is true at a time, making exactly one label row visible.

### The BOOL Parameter Matrix

Parameter naming convention: `TAG_{SIZE}{STYLE}_{COLOR}_BOOL`

| Component | Options | Examples |
|-----------|---------|---------|
| **Size** | 2, 2.5, 3, 3.5 (mm) | TAG_**2**BOLD_BLUE_BOOL |
| **Style** | NOM (Normal), BOLD, ITALIC | TAG_2**BOLD**_BLUE_BOOL |
| **Color** | BLACK, BLUE, GREEN, RED | TAG_2BOLD_**BLUE**_BOOL |

This produces **128 combinations** (4 sizes × 3 styles × ~10 colors).

### Style Commands

All buttons are in the **VIEW tab → TAG STYLE ENGINE** section:

| Button Name | Sub-Section | What It Does |
|-------------|-------------|--------------|
| **`Apply Tag Style`** | TAG APPEARANCE | Pick size → style → color → sets one BOOL true on all element types |
| **`By Discipline`** | TAG APPEARANCE | Auto-set tag styles by discipline: M=Blue Bold, E=Red Bold, P=Green Bold, A=Black Normal |
| **`Style Report`** | TAG APPEARANCE | Report which styles are currently active |
| **`Apply Scheme`** | VIEW COLOUR SCHEME | Apply named scheme (Discipline/Warm/Cool/Red/Yellow/Blue/Mono/Dark) — sets element graphic overrides + tag styles per discipline |
| **`Batch Scheme`** | VIEW COLOUR SCHEME | Apply scheme to ALL views in project |
| **`Clear Scheme`** | VIEW COLOUR SCHEME | Remove all graphic overrides from view |
| **`By Variable`** | COLOUR BY VARIABLE | Color by any variable (System/Status/Zone/Level/Function/Location) with element colors + tag styles + box colors |
| **`Box Color`** | COLOUR BY VARIABLE | Control tag bounding box fill color independently of text color |
| **`Set Depth (1-10)`** | PARAGRAPH DEPTH | Extended paragraph depth with 10 visibility tiers |

### Built-In Color Schemes

| Scheme | Description | Discipline Colors |
|--------|-------------|-------------------|
| **Discipline** | Standard ISO 19650 | M=Blue, E=Gold, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple, G=Brown |
| **Warm** | Salmon/terracotta tones | Architectural presentation |
| **Cool** | Green/teal tones | MEP presentation |
| **Red** | Bold red tones | Structural emphasis |
| **Yellow** | Amber/gold tones | Electrical emphasis |
| **Blue** | Slate/navy tones | Plumbing/MEP emphasis |
| **Mono** | Black and white | Print-ready |
| **Dark** | Inverted (light on dark) | Dark background presentation |

### Variable-Driven Color Schemes

Color elements by **any** tag variable, not just discipline:

| Variable | Parameter | Values | Semantic Colors |
|----------|-----------|--------|-----------------|
| **System** | ASS_SYSTEM_TYPE_TXT | HVAC, DCW, SAN, HWS, FP, LV, FLS... | 16 distinct colors per system |
| **Status** | ASS_STATUS_TXT | NEW, EXISTING, DEMOLISHED, TEMPORARY | Green, Blue, Red, Orange |
| **Zone** | ASS_ZONE_TXT | Z01, Z02, Z03, Z04 | Blue, Green, Orange, Red |
| **Level** | ASS_LVL_COD_TXT | GF, L01, L02, B1, RF | Green, Blue, Purple, Red, Orange |
| **Function** | ASS_FUNC_TXT | SUP, HTG, DCW, PWR, LTG... | 12 function-specific colors |
| **Location** | ASS_LOC_TXT | BLD1, BLD2, BLD3, EXT | Blue, Green, Orange, Purple |

### How Styles Work in Tag Families

In the Family Editor, each style combination has its own label row:

```
Label Rows in "STING - Mechanical Equipment Tag.rfa":
  ┌─────────────────────────────────────────────────────────┐
  │ Row 1: ASS_TAG_1_TXT          [Visible if: always]     │ ← Tier 1
  │ Row 2: ASS_DESCRIPTION_TXT    [Visible if: always]     │ ← Tier 1
  │ Row 3: BLE dimensions         [Visible if: STATE_2]    │ ← Tier 2
  │ Row 4: TAG7 narrative          [Visible if: STATE_3]    │ ← Tier 3
  ├─────────────────────────────────────────────────────────┤
  │ Style Row A: 2mm Normal Black  [Visible if: TAG_2NOM_BLACK_BOOL]   │
  │ Style Row B: 2mm Bold Blue     [Visible if: TAG_2BOLD_BLUE_BOOL]   │
  │ Style Row C: 2.5mm Bold Red    [Visible if: TAG_2.5BOLD_RED_BOOL]  │
  │ Style Row D: 3mm Italic Green  [Visible if: TAG_3ITALIC_GREEN_BOOL]│
  │ ... (one row per style combo)                                       │
  └─────────────────────────────────────────────────────────┘
```

When `ApplyTagStyle()` is called, it:
1. Sets ALL style BOOLs to `false` on the element type
2. Sets exactly ONE BOOL to `true` (the chosen style)
3. Only that label row becomes visible

---

## 9. Phase 7 — Seed Family Strategy (Skip Phase 2 Next Time)

### What Are Seed Families?

Seed families are pre-configured .rfa files with labels already bound to STING parameters. They eliminate the manual Family Editor step (Phase 2) for future deployments.

### Creating Seeds

After configuring all labels in Phase 2:

1. Navigate to `Data/TagFamilies/`
2. Create a `Seeds/` subdirectory
3. Copy all configured .rfa files into `Seeds/`
4. Next time `Create Tag Families` runs, it will load seeds directly with `[SEED]` status

### Seed Search Order

```
Data/TagFamilies/Seeds/STING - Mechanical Equipment Tag.rfa     ← Priority 1
Data/TagFamilies/Seeds/STING - Mechanical Equipment Tag_seed.rfa ← Priority 2 (alt naming)
Data/TagFamilies/STING - Mechanical Equipment Tag.rfa            ← Priority 3 (regular output)
```

### Seed Distribution

Seeds can be distributed with the plugin for zero-configuration deployments:
- Package seed .rfa files in the `Data/TagFamilies/Seeds/` directory
- When the plugin is installed, `Create Tag Families` will find and load them automatically
- No Family Editor steps required

---

## 10. Batch Family Parameter Binding

Two commands handle adding shared parameters to families in bulk — one works on the **project** binding map, the other opens and modifies **.rfa files** directly.

### 10a. Batch Add Family Params (Project Bindings)

**Command**: `BatchAddFamilyParamsCommand`
**Button**: **`Fam Params`** — TEMP tab → TEMPLATE MANAGER section
**Transaction**: Manual

Reads `FAMILY_PARAMETER_BINDINGS.csv` (4,686 entries) and binds shared parameters to categories in the **current project's** BindingMap. This complements the `Load Shared Params` button by covering the full parameter-to-category matrix.

**What It Does**:

1. Opens `MR_PARAMETERS.txt` to get shared parameter definitions
2. Loads `FAMILY_PARAMETER_BINDINGS.csv` — each row maps a parameter name + GUID to a category and binding type (Instance/Type)
3. Groups bindings by parameter name for batch efficiency
4. For each parameter:
   - Resolves the `ExternalDefinition` by name, then GUID fallback
   - Builds a `CategorySet` from all categories listed for that parameter
   - Checks if already bound (skips if so — idempotent)
   - Creates `InstanceBinding` or `TypeBinding` per the CSV's `bindingType` column
   - Inserts into the project's `BindingMap`
5. Reports per-category coverage with binding counts

**Data File**: `FAMILY_PARAMETER_BINDINGS.csv` — columns: `group`, `name`, `guid`, `dataType`, `bindingType`, `description`, `category`, `sharedGuid`

### 10b. Family Parameter Processor (Modify .rfa Files)

**Command**: `FamilyParameterProcessorCommand`
**Button**: **`Fam Processor`** — TEMP tab → TEMPLATE MANAGER section (next to Fam Params)
**Transaction**: Manual

Opens .rfa family files on disk, adds shared parameters via `FamilyManager.AddParameter()`, applies formulas via `FamilyManager.SetFormula()`, creates backups, and saves the modified families.

**What It Does**:

1. User selects a **single .rfa file** or a **folder** of .rfa files (recursive)
2. For each family file:
   a. Opens with `app.OpenDocumentFile()`
   b. Reads `OwnerFamily.FamilyCategory` to determine the Revit category
   c. Looks up applicable parameters from `FAMILY_PARAMETER_BINDINGS.csv` by category name
   d. Adds shared parameters via `FamilyManager.AddParameter()` (skips already-existing)
   e. Looks up applicable formulas from `FORMULAS_WITH_DEPENDENCIES.csv` by parameter name
   f. Applies formulas via `FamilyManager.SetFormula()` (only for parameters that exist in the family)
   g. Backs up the original .rfa to a `_param_backups/` subfolder
   h. Saves the modified family
3. Reports per-family results: parameters added, formulas applied, skipped, failed

**Data Files Used**:

| File | Purpose |
|------|---------|
| `MR_PARAMETERS.txt` | Shared parameter definitions (name → GUID resolution) |
| `FAMILY_PARAMETER_BINDINGS.csv` | 4,686 category-to-parameter mappings |
| `FORMULAS_WITH_DEPENDENCIES.csv` | 199+ formula definitions (applied after param add) |

**Typical Use Case**: Preparing a library of .rfa families with all STING parameters pre-loaded, so they work immediately when loaded into a STING-enabled project. This is especially useful for:
- Company family libraries (batch-process hundreds of .rfa files)
- Tag family preparation (add parameters before configuring labels)
- Template family enrichment (ensure all families have the full parameter set)

### Relationship to Tag Family Creation

```
Create Tag Families ─── Creates new .rfa tag families from .rft templates
  └── adds 13 tag + 4 visibility + category-specific params via FamilyManager

Fam Params ─────────── Binds params to categories in the project BindingMap
  └── reads FAMILY_PARAMETER_BINDINGS.csv (4,686 entries)

Fam Processor ──────── Opens existing .rfa files and adds params + formulas
  └── reads FAMILY_PARAMETER_BINDINGS.csv + FORMULAS_WITH_DEPENDENCIES.csv
  └── works on any .rfa family, not just tag families
```

---

## 11. Category Reference Table

### 50 Taggable Categories with Template Mapping

| # | Category | BuiltInCategory | .rft Template | Discipline |
|---|----------|-----------------|---------------|------------|
| 1 | Mechanical Equipment | `OST_MechanicalEquipment` | Mechanical Equipment Tag.rft | M |
| 2 | Ducts | `OST_DuctCurves` | Duct Tag.rft | M |
| 3 | Duct Fittings | `OST_DuctFitting` | Duct Fitting Tag.rft | M |
| 4 | Duct Accessories | `OST_DuctAccessory` | Duct Accessory Tag.rft | M |
| 5 | Air Terminals | `OST_DuctTerminal` | Air Terminal Tag.rft | M |
| 6 | Flex Ducts | `OST_FlexDuctCurves` | Flex Duct Tag.rft | M |
| 7 | Pipes | `OST_PipeCurves` | Pipe Tag.rft | P |
| 8 | Pipe Fittings | `OST_PipeFitting` | Pipe Fitting Tag.rft | P |
| 9 | Pipe Accessories | `OST_PipeAccessory` | Pipe Accessory Tag.rft | P |
| 10 | Flex Pipes | `OST_FlexPipeCurves` | Flex Pipe Tag.rft | P |
| 11 | Plumbing Fixtures | `OST_PlumbingFixtures` | Plumbing Fixture Tag.rft | P |
| 12 | Sprinklers | `OST_Sprinklers` | Sprinkler Tag.rft | FP |
| 13 | Fire Alarm Devices | `OST_FireAlarmDevices` | Fire Alarm Device Tag.rft | FP |
| 14 | Electrical Equipment | `OST_ElectricalEquipment` | Electrical Equipment Tag.rft | E |
| 15 | Electrical Fixtures | `OST_ElectricalFixtures` | Electrical Fixture Tag.rft | E |
| 16 | Lighting Fixtures | `OST_LightingFixtures` | Lighting Fixture Tag.rft | E |
| 17 | Lighting Devices | `OST_LightingDevices` | Lighting Device Tag.rft | E |
| 18 | Conduits | `OST_Conduit` | Conduit Tag.rft | E |
| 19 | Conduit Fittings | `OST_ConduitFitting` | Conduit Fitting Tag.rft | E |
| 20 | Cable Trays | `OST_CableTray` | Cable Tray Tag.rft | E |
| 21 | Cable Tray Fittings | `OST_CableTrayFitting` | Cable Tray Fitting Tag.rft | E |
| 22 | Communication Devices | `OST_CommunicationDevices` | Communication Device Tag.rft | LV |
| 23 | Data Devices | `OST_DataDevices` | Data Device Tag.rft | LV |
| 24 | Nurse Call Devices | `OST_NurseCallDevices` | Nurse Call Device Tag.rft | LV |
| 25 | Security Devices | `OST_SecurityDevices` | Security Device Tag.rft | LV |
| 26 | Telephone Devices | `OST_TelephoneDevices` | Telephone Device Tag.rft | LV |
| 27 | Doors | `OST_Doors` | Door Tag.rft | A |
| 28 | Windows | `OST_Windows` | Window Tag.rft | A |
| 29 | Walls | `OST_Walls` | Wall Tag.rft | A |
| 30 | Floors | `OST_Floors` | Floor Tag.rft | A |
| 31 | Ceilings | `OST_Ceilings` | Ceiling Tag.rft | A |
| 32 | Roofs | `OST_Roofs` | Roof Tag.rft | A |
| 33 | Rooms | `OST_Rooms` | Room Tag.rft | A |
| 34 | Furniture | `OST_Furniture` | Furniture Tag.rft | A |
| 35 | Furniture Systems | `OST_FurnitureSystems` | Furniture System Tag.rft | A |
| 36 | Casework | `OST_Casework` | Casework Tag.rft | A |
| 37 | Stairs | `OST_Stairs` | Stair Tag.rft | A |
| 38 | Ramps | `OST_Ramps` | Ramp Tag.rft | A |
| 39 | Structural Columns | `OST_StructuralColumns` | Structural Column Tag.rft | S |
| 40 | Structural Framing | `OST_StructuralFraming` | Structural Framing Tag.rft | S |
| 41 | Structural Foundations | `OST_StructuralFoundation` | Structural Foundation Tag.rft | S |
| 42 | Generic Models | `OST_GenericModel` | Generic Model Tag.rft | G |
| 43 | Specialty Equipment | `OST_SpecialityEquipment` | Specialty Equipment Tag.rft | G |
| 44 | Parking | `OST_Parking` | Parking Tag.rft | A |
| 45 | Site | `OST_Site` | Site Tag.rft | A |

### Template Fallback Chain

If the specific .rft template is not found:

```
1. {Category} Tag.rft                  (e.g., "Mechanical Equipment Tag.rft")
2. Metric {Category} Tag.rft           (e.g., "Metric Mechanical Equipment Tag.rft")
3. {Category}Tag.rft                   (no spaces variant)
4. Generic Tag.rft
5. Metric Generic Tag.rft
6. Multi-Category Tag.rft
7. Metric Multi-Category Tag.rft
8. Generic Annotation.rft
9. Metric Generic Annotation.rft
10. Any *Tag.rft file in directory
11. Any *.rft file in directory
```

---

## 12. Parameter Reference

### Tag Container Parameters (13 — added to every family)

| Parameter | Content | Example Value |
|-----------|---------|---------------|
| `ASS_TAG_1_TXT` | Full 8-segment ISO 19650 tag | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |
| `ASS_TAG_2_TXT` | Short tag (SYS-FUNC-PROD-SEQ) | SUP-AHU-0003 |
| `ASS_TAG_3_TXT` | System tag extended | HVAC-SUP-AHU-0003 |
| `ASS_TAG_4_TXT` | Short label (PROD-SEQ) | AHU-0003 |
| `ASS_TAG_5_TXT` | Line 1 (DISC-LOC-ZONE-LVL) | M-BLD1-Z01-L02 |
| `ASS_TAG_6_TXT` | Line 2 (SYS-FUNC-PROD-SEQ) | HVAC-SUP-AHU-0003 |
| `ASS_TAG_7_TXT` | Full rich narrative | (long multi-section text) |
| `ASS_TAG_7A_TXT` | Section A: Identity Header | AHU \| Carrier 39M \| Model 060 |
| `ASS_TAG_7B_TXT` | Section B: System & Function | HVAC Supply \| Heating/Cooling |
| `ASS_TAG_7C_TXT` | Section C: Spatial Context | Room 201 \| Level 02 \| Zone Z01 |
| `ASS_TAG_7D_TXT` | Section D: Lifecycle/Status | NEW \| Rev A \| Phase: New Construction |
| `ASS_TAG_7E_TXT` | Section E: Technical Specs | 15kW \| 2.5 m³/s \| ΔP 450Pa |
| `ASS_TAG_7F_TXT` | Section F: Classification | Ss_45_10_25 \| 23-33 11 13 |

### Visibility Control Parameters (4 — added to every family)

| Parameter | Type | Purpose |
|-----------|------|---------|
| `TAG_PARA_STATE_1_BOOL` | Type / Yes-No | Tier 1 visibility (always ON) |
| `TAG_PARA_STATE_2_BOOL` | Type / Yes-No | Tier 2 visibility (Standard+) |
| `TAG_PARA_STATE_3_BOOL` | Type / Yes-No | Tier 3 visibility (Comprehensive) |
| `TAG_WARN_VISIBLE_BOOL` | Type / Yes-No | Warning text visibility |

### Style Matrix Parameters (128 — for advanced multi-style families)

Pattern: `TAG_{SIZE}{STYLE}_{COLOR}_BOOL`

Examples:
- `TAG_2NOM_BLACK_BOOL` — 2mm Normal Black
- `TAG_2BOLD_BLUE_BOOL` — 2mm Bold Blue
- `TAG_2.5BOLD_RED_BOOL` — 2.5mm Bold Red
- `TAG_3ITALIC_GREEN_BOOL` — 3mm Italic Green
- `TAG_3.5NOM_BLACK_BOOL` — 3.5mm Normal Black

---

## 13. Troubleshooting

### "Cannot find Revit annotation tag templates (.rft)"

**Cause**: Revit's Family Templates directory is missing or not in a recognized location.

**Fix**:
- Ensure Revit is installed with the "Family Templates" content option
- Check `app.FamilyTemplatePath` points to a valid directory
- Manually verify .rft files exist in one of the searched paths
- As last resort, copy .rft files from another Revit installation to `C:\ProgramData\Autodesk\RVT {version}\Family Templates\English\Annotations\`

### "Cannot find MR_PARAMETERS.txt"

**Cause**: Shared parameter file not deployed with the plugin.

**Fix**:
- Run `Check Data` button (TEMP tab → ⚙ SETUP) to verify data files
- Ensure the `data/` directory alongside `StingTools.dll` contains `MR_PARAMETERS.txt`
- The file must contain the STING parameter GUIDs matching `PARAMETER_REGISTRY.json`

### "Label still shows Type Mark instead of ASS_TAG_1_TXT"

**Cause**: The programmatic Label rebind did not succeed (Revit API limitation).

**Fix**: This is expected. Run `Configure Labels` button (CREATE tab → ⚙ SETUP, row 2) and manually set up Edit Label in the Family Editor as described in Phase 2.

### "Created but load failed [PART]"

**Cause**: The .rfa was created and saved but could not be loaded into the project.

**Fix**:
- The .rfa file exists on disk — try loading manually via Insert → Load Family
- Check if the category already has a conflicting tag family
- Run `Load` button (CREATE tab → ⚙ SETUP, row 2) separately to retry

### "Seed load failed"

**Cause**: The seed .rfa file exists but is corrupted or from a different Revit version.

**Fix**:
- Delete the seed file from `Data/TagFamilies/Seeds/`
- Recreate via `Create Tag Families` + `Configure Labels` buttons
- Save new seed from `Data/TagFamilies/`

### Tag shows empty/blank text

**Cause**: The element has no tag data populated.

**Fix**: Run the tagging pipeline first:
1. `[Brain] Smart Tokens` button (CREATE tab → ⚙ POPULATE TOKENS) — pre-populate 7 tokens
2. `▶ Auto Populate` button (CREATE tab → ⚙ POPULATE TOKENS) — assign SEQ + build tags + combine to containers

### Style not changing when applying color scheme

**Cause**: Tag family does not have the BOOL style parameters.

**Fix**:
- Run `Audit` button (CREATE tab → ⚙ SETUP, row 2) to check parameter coverage
- Recreate families with `Create Tag Families` button (will add all style BOOLs)
- Or manually add the needed `TAG_{SIZE}{STYLE}_{COLOR}_BOOL` parameters in Family Editor

---

## 14. File Reference

### Source Files

| File | Lines | Purpose |
|------|-------|---------|
| `Tags/TagFamilyCreatorCommand.cs` | 1,565 | 4 commands + TagFamilyConfig + TagFamilyLoadOptions + tag family creation engine |
| `Tags/TagStyleEngine.cs` | 1,007 | BOOL parameter matrix, color schemes, paragraph depth, variable-driven schemes |
| `Tags/TagStyleCommands.cs` | 752 | 9 tag style commands (ApplyStyle, ColorScheme, ClearScheme, ParagraphDepth, Report, DiscStyle, BatchStyle, ColorByVariable, SetBoxColor) |
| `Tags/PresentationModeCommand.cs` | 926 | 4 commands (SetMode, ViewLabelSpec, ExportLabelGuide, SetTag7HeadingStyle) + LabelDefinitionHelper |
| `Tags/ParagraphDepthCommand.cs` | 213 | 2 commands (SetParagraphDepth, ToggleWarningVisibility) |
| `Temp/TemplateManagerCommands.cs` | 3,892 | BatchAddFamilyParamsCommand (line 2452) + FamilyParameterProcessorCommand (line 2702) |

### Data Files

| File | Purpose |
|------|---------|
| `Data/LABEL_DEFINITIONS.json` | Master label spec: presentation modes, calculated value templates, parameter text (prefix/suffix), category-specific tier layouts, paragraph templates, TAG7 heading styles |
| `Data/MR_PARAMETERS.txt` | Revit shared parameter file (200+ params with GUIDs) |
| `Data/PARAMETER_REGISTRY.json` | Parameter names, containers, category bindings, tag format config |
| `Data/FAMILY_PARAMETER_BINDINGS.csv` | 4,686 parameter-to-category mappings for batch binding (used by `Fam Params` and `Fam Processor`) |
| `Data/FORMULAS_WITH_DEPENDENCIES.csv` | 199 formula definitions applied by `Fam Processor` to family parameters |

### Output Directories

| Path | Content |
|------|---------|
| `Data/TagFamilies/` | Generated .rfa tag family files |
| `Data/TagFamilies/Seeds/` | Pre-configured seed families (labels already bound) |
| `Data/LABEL_CONFIGURATION_GUIDE.txt` | Exported Edit Label guide (generated by `Export Label Guide` button in CREATE tab → ⚙ PARAGRAPH & PRESENTATION) |

---

## Quick Reference — Complete Workflow Checklist

```
□  1. TEMP tab    → ⚙ SETUP              → [Check Data]            Verify MR_PARAMETERS.txt, LABEL_DEFINITIONS.json
□  2. CREATE tab  → ⚙ SETUP (row 1)      → [Load Shared Params]    Bind 200+ shared params to 53 categories
□  3. TEMP tab    → TEMPLATE MANAGER      → [Fam Params]            Batch-bind params to categories from CSV (4,686 entries)
□  4. CREATE tab  → ⚙ SETUP (row 2)      → [Create Tag Families]   Create 50 .rfa files from .rft templates
□  5. TEMP tab    → TEMPLATE MANAGER      → [Fam Processor]         Add params + formulas to .rfa files (optional — bulk prep)
□  6. CREATE tab  → ⚙ SETUP (row 2)      → [Configure Labels]      Open each family → Edit Label → add params
□  7. CREATE tab  → ⚙ SETUP (row 2)      → [Load]                  Batch-load all .rfa into project
□  8. CREATE tab  → ⚙ SETUP (row 2)      → [Audit]                 Verify coverage: 50/50 categories
□  9. CREATE tab  → ⚙ PARAGRAPH & PRES.  → [Presentation Mode]     Choose Compact / Technical / Full Spec / Presentation
□ 10. VIEW tab    → TAG STYLE ENGINE      → [Apply Tag Style]       Set size × style × color for discipline
□ 11. Manual: Copy configured .rfa to Data/TagFamilies/Seeds/        Seed for future zero-config deployments
```

**One-click alternative**: If seed families exist, steps 3-5 collapse into a single `Create Tag Families` button press.
