# STING Tools — Tagging Procedures & Workflows Guide

> Step-by-step operational guide for ISO 19650-compliant asset tagging in Autodesk Revit using StingTools.
> Version 2.0 — March 2026

---

## Table of Contents

1. [Overview & Concepts](#1-overview--concepts)
2. [Prerequisites & Initial Setup](#2-prerequisites--initial-setup)
3. [Procedure 1: First-Time Project Tagging](#3-procedure-1-first-time-project-tagging)
4. [Procedure 2: Incremental Tagging (New Elements)](#4-procedure-2-incremental-tagging-new-elements)
5. [Procedure 3: Real-Time Auto-Tagging](#5-procedure-3-real-time-auto-tagging)
6. [Procedure 4: Manual Token Adjustment](#6-procedure-4-manual-token-adjustment)
7. [Procedure 5: Stale Element Re-Tagging](#7-procedure-5-stale-element-re-tagging)
8. [Procedure 6: Tag Validation & QA](#8-procedure-6-tag-validation--qa)
9. [Procedure 7: Visual Tag Annotation](#9-procedure-7-visual-tag-annotation)
10. [Procedure 8: Tag Style & Presentation](#10-procedure-8-tag-style--presentation)
11. [Procedure 9: Tag Register & Reporting](#11-procedure-9-tag-register--reporting)
12. [Procedure 10: Resolve All Issues (One-Click Fix)](#12-procedure-10-resolve-all-issues-one-click-fix)
13. [Procedure 11: Excel Round-Trip Tagging](#13-procedure-11-excel-round-trip-tagging)
14. [Procedure 12: Federated Model Tagging](#14-procedure-12-federated-model-tagging)
15. [Tag Format Reference](#15-tag-format-reference)
16. [Configuration Reference](#16-configuration-reference)
17. [Troubleshooting](#17-troubleshooting)
18. [Performance & Efficiency Recommendations](#18-performance--efficiency-recommendations)

---

## 1. Overview & Concepts

### What Is STING Tagging?

STING Tools assigns every element in a Revit model an **8-segment ISO 19650-compliant asset tag** that uniquely identifies it across its lifecycle:

```
 DISC - LOC - ZONE - LVL - SYS  - FUNC - PROD - SEQ
  M   - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

This tag is stored in the shared parameter `ASS_TAG_1_TXT` and propagated to up to **53 discipline-specific container parameters** for use in schedules, tag families, COBie export, and IFC deliverables.

### Key Principles

| Principle | Description |
|-----------|-------------|
| **Guaranteed non-empty** | Every token receives a valid value — no segment is ever blank |
| **Non-destructive by default** | `SetIfEmpty` preserves manually-set values unless overwrite is chosen |
| **Collision-safe** | O(1) HashSet-based duplicate detection with auto-increment fallback |
| **Session-persistent** | SEQ counters saved to `.sting_seq.json` sidecar for crash recovery |
| **MEP-intelligent** | 6-layer system detection via connector graph traversal |
| **Spatially-aware** | Room, workset, and project info used for LOC/ZONE auto-detection |
| **Audit-trailed** | Previous tag + timestamp recorded per element on every change |

### The 11-Step Pipeline

Every tagging command delegates to `TagPipelineHelper.RunFullPipeline()`, which executes these steps in order:

| Step | Action | Detail |
|------|--------|--------|
| 1 | **Category filter** | Skip elements in `CATEGORY_SKIP` list |
| 2 | **Audit trail** | Capture previous TAG1 → `ASS_TAG_PREV_TXT` + timestamp |
| 3 | **Type token inheritance** | Copy DISC/SYS/FUNC/PROD from family type to instance |
| 4 | **Lock snapshot** | Snapshot locked tokens from `ASS_TOKEN_LOCK_TXT` |
| 5 | **Token population** | Derive all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV) |
| 6 | **Category force SYS** | Apply `CATEGORY_FORCE_SYS` overrides from config |
| 7 | **Category token overrides** | Apply full per-category token overrides |
| 8 | **Restore locked tokens** | Restore any user-locked tokens that were overridden |
| 9 | **Native parameter mapping** | Bridge 30+ Revit built-in params to STING shared params |
| 10 | **Formula evaluation** | Evaluate 199 dependency-ordered formulas |
| 11 | **Build & write tag** | Assemble TAG1, handle collisions, write containers, TAG7, grid ref |


---

## 2. Prerequisites & Initial Setup

### 2.1 System Requirements

- Autodesk Revit 2025, 2026, or 2027
- StingTools plugin installed (StingTools.addin + StingTools.dll + data/ folder)
- .NET 8.0 runtime (bundled with Revit 2025+)

### 2.2 Verify Plugin Installation

1. Launch Revit
2. Look for the **STING Tools** tab in the ribbon, or the **STING Panel** docked to the right
3. If neither appears, verify:
   - `StingTools.addin` is in `%APPDATA%\Autodesk\Revit\Addins\2025\`
   - `StingTools.dll` path in the `.addin` file matches the actual DLL location
   - The `data/` folder is alongside `StingTools.dll`

### 2.3 First-Time Setup Checklist

| Step | Command | Location | Required? |
|------|---------|----------|-----------|
| 1 | **Load Shared Parameters** | CREATE tab → Setup → Load Params | **Yes** (must be first) |
| 2 | Create BLE Materials | TEMP tab → Materials → BLE | Recommended |
| 3 | Create MEP Materials | TEMP tab → Materials → MEP | Recommended |
| 4 | Create Family Types | TEMP tab → Families → (type) | Recommended |
| 5 | Create Schedules | TEMP tab → Schedules → Batch Create | Recommended |
| 6 | **Configure project_config.json** | CREATE tab → Setup → Configure | Optional (defaults work) |

> **Shortcut:** Run **Master Setup** (TEMP tab → Setup → Master Setup) to execute all 17 setup steps in one click.

> **Better shortcut:** Run **Project Setup Wizard** (TEMP tab → Setup → ★ Project Setup Wizard) for a guided 7-page WPF wizard.

### 2.4 Loading Shared Parameters

**This step is mandatory before any tagging.**

**Steps:**
1. Open your Revit project
2. Navigate to **CREATE tab → Setup → Load Params**
3. The command automatically:
   - Locates `MR_PARAMETERS.txt` in the plugin's data directory
   - **Pass 1:** Binds universal ASS_MNG parameters to all 54 categories
   - **Pass 2:** Binds discipline-specific containers (HVC_EQP_TAG, ELC_EQP_TAG, etc.) to their correct category subsets
4. Wait for completion — this binds 200+ parameters across 54 categories
5. A summary dialog shows parameters bound and any errors

**What happens if you skip this?**
- Tagging commands will fail silently or produce empty results
- Parameters won't exist on elements, so token values can't be written

**Re-running is safe:** Already-bound parameters are skipped automatically.

---

## 3. Procedure 1: First-Time Project Tagging

### When to Use
- New project with no existing STING tags
- Model has elements placed but no ISO 19650 tagging

### Recommended Approach: Tag & Combine

**Command:** Tag & Combine
**Location:** CREATE tab → Tag & Combine button

### Step-by-Step

#### Step 1: Prepare the Model
- Ensure rooms are placed and named (improves LOC/ZONE detection)
- Ensure levels are named consistently (Ground Floor, Level 1, etc.)
- Ensure MEP systems are defined (improves SYS detection)

#### Step 2: Run Pre-Tag Audit (Optional but Recommended)
1. Navigate to **CREATE tab → QA → Pre-Tag Audit**
2. Review the dry-run report:
   - **Tag Prediction**: How many elements will be tagged
   - **Spatial Intelligence**: LOC/ZONE detection accuracy
   - **Family-Aware PROD**: Product codes derived from family names
   - **ISO 19650 Compliance**: Any invalid predicted codes
3. If issues found, fix model data first (add rooms, name levels, etc.)

#### Step 3: Choose Scope
1. Click **Tag & Combine** button
2. Select scope:
   - **Active View** — Tags only elements visible in current view
   - **Selected Elements** — Tags only selected elements
   - **Entire Project** — Tags all taggable elements in the model

#### Step 4: Monitor Progress
- A progress dialog shows element count, current element, and ETA
- Press **Escape** to cancel (already-committed batches are preserved)
- Elements are processed in 200-element batches

#### Step 5: Review Results
The completion dialog shows:
- Total tagged / skipped / errors
- Breakdown by discipline (M, E, P, A, S, FP, LV, G)
- Breakdown by system type
- Breakdown by level
- Collision resolution stats (if any duplicates were found)
- Compliance percentage

#### Step 6: Validate
1. Run **CREATE tab → QA → Validate** to check ISO 19650 compliance
2. Review the 4-bucket report:
   - **RESOLVED**: Production-ready tags (all tokens valid)
   - **COMPLETE WITH PLACEHOLDERS**: 8 segments but contains GEN/XX/ZZ/0000
   - **INCOMPLETE**: Fewer than 8 segments
   - **UNTAGGED**: No tag at all
3. If placeholders exist, see [Procedure 4: Manual Token Adjustment](#6-procedure-4-manual-token-adjustment)

### What Gets Written Per Element

| Parameter | Example Value | Description |
|-----------|--------------|-------------|
| `ASS_DISCIPLINE_COD_TXT` | M | Discipline code |
| `ASS_LOC_TXT` | BLD1 | Location code |
| `ASS_ZONE_TXT` | Z01 | Zone code |
| `ASS_LVL_COD_TXT` | L02 | Level code |
| `ASS_SYSTEM_TYPE_TXT` | HVAC | System type |
| `ASS_FUNC_TXT` | SUP | Function code |
| `ASS_PRODCT_COD_TXT` | AHU | Product code |
| `ASS_SEQ_NUM_TXT` | 0003 | Sequence number |
| `ASS_STATUS_TXT` | NEW | Construction status |
| `ASS_REV_TXT` | P01 | Project revision |
| `ASS_TAG_1_TXT` | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 | Full 8-segment tag |
| `ASS_TAG_2_TXT` through `ASS_TAG_6_TXT` | (various formats) | Multi-line containers |
| `ASS_TAG_7_TXT` | (rich narrative) | TAG7 full description |
| `ASS_TAG_7A_TXT` through `ASS_TAG_7F_TXT` | (sub-sections) | TAG7 sections A-F |
| `HVC_EQP_TAG` (discipline-specific) | M-HVAC-SUP-AHU-0003 | HVAC equipment tag |
| `ASS_TAG_PREV_TXT` | (empty on first tag) | Previous tag (audit) |
| `ASS_TAG_MODIFIED_DT` | 2026-03-27 14:30 | Last modified timestamp |
| `ASS_GRID_REF_TXT` | A/3 | Nearest grid intersection |


---

## 4. Procedure 2: Incremental Tagging (New Elements)

### When to Use
- Model already has STING tags from a previous tagging run
- New elements have been added and need tags
- You do NOT want to re-tag existing elements

### Command: Tag New Only
**Location:** CREATE tab → More → Tag New Only

### Step-by-Step

1. **Choose scope**: Active View or Entire Project
2. The command automatically:
   - Filters to elements where `ASS_TAG_1_TXT` is empty
   - Skips all already-tagged elements (no re-derivation)
   - Runs the full 11-step pipeline on each untagged element
   - Merges SEQ counters with existing sidecar (continues from highest existing number)
3. **Review results**: Shows count of newly tagged elements by discipline

### Performance Advantage

| Scenario | Tag & Combine | Tag New Only |
|----------|--------------|--------------|
| 10,000 elements, 50 new | Processes 10,000 | Processes 50 |
| Typical time | 45-90 seconds | 1-3 seconds |

### Alternative: AutoTag (Active View)
**Location:** CREATE tab → Auto Tag

Same pipeline but scoped to the active view. Prompts for collision mode:
- **Skip** (default): Never modify existing tags — safe for re-runs
- **Overwrite**: Re-derive ALL tokens and reassign SEQ — use for corrections
- **AutoIncrement**: If collision found, increment SEQ until unique

---

## 5. Procedure 3: Real-Time Auto-Tagging

### When to Use
- During active modeling sessions
- You want elements tagged automatically as they are placed
- Zero-touch workflow for design-phase modeling

### Setup

1. **Enable data auto-tagger:**
   - Navigate to **CREATE tab → Auto-Tag → Toggle Auto-Tagger**
   - Status bar shows "Auto-Tagger: ON"

2. **Optionally enable visual tagging:**
   - Navigate to **CREATE tab → Auto-Tag → Toggle Visual**
   - Creates `IndependentTag` annotations alongside data tags

3. **Optionally set discipline filter:**
   - Navigate to **CREATE tab → Auto-Tag → Configure**
   - Restrict auto-tagging to specific disciplines (e.g., only M and E)

### How It Works

```
User places element in Revit
    ↓
StingAutoTagger IUpdater fires (element addition trigger)
    ↓
Check: Is element in a monitored category? (22 categories)
    ↓  YES
Check: Already processed recently? (HashSet deduplication)
    ↓  NO
Check: Workset owned by current user? (worksharing safety)
    ↓  YES
Build/reuse cached PopulationContext (5-second TTL)
    ↓
Run full 11-step pipeline (RunFullPipeline)
    ↓
Element is tagged immediately with ISO 19650 tag
```

### Monitored Categories (22)

Air Terminals, Cable Trays, Cable Tray Fittings, Casework, Ceilings, Communication Devices, Conduits, Conduit Fittings, Data Devices, Doors, Ducts, Duct Accessories, Duct Fittings, Electrical Equipment, Electrical Fixtures, Fire Alarm Devices, Flex Ducts, Floors, Furniture, Lighting Devices, Lighting Fixtures, Mechanical Equipment, Nurse Call Devices, Pipe Accessories, Pipe Fittings, Pipes, Plumbing Fixtures, Roofs, Security Devices, Sprinklers, Structural Columns, Structural Framing, Telephone Devices, Walls, Windows

### Bulk Paste Handling

When pasting 50+ elements at once:
- Elements are queued to a deferred processing list
- Processing happens in batches to avoid Revit UI freezing
- All queued elements are processed before the next user interaction

### Worksharing Safety

- Elements on worksets owned by other users are queued to a **deferred queue**
- The deferred queue is drained after `DocumentSynchronizedWithCentral` events
- Queue is capped at 5,000 elements to prevent unbounded memory growth

### Stale Detection (StingStaleMarker)

A companion IUpdater monitors geometry changes on tagged elements:
- If an element moves to a different level, LOC, or ZONE → marked as stale
- If a MEP element's system changes → marked as stale
- Stale elements have `STING_STALE_BOOL = 1`
- See [Procedure 5](#7-procedure-5-stale-element-re-tagging) for re-tagging stale elements

### Persistence

Auto-tagger enabled/visual/discipline-filter state is saved to `project_config.json` and restored on document open.

---

## 6. Procedure 4: Manual Token Adjustment

### When to Use
- Specific elements need a different LOC, ZONE, or STATUS than auto-detected
- Correcting placeholder values (GEN, XX, ZZ)
- Overriding auto-detected SYS or FUNC codes

### Individual Token Commands

**Location:** CREATE tab → Tokens section

| Command | What It Does | Typical Use |
|---------|-------------|-------------|
| **Set Discipline** | Set DISC token (M, E, P, A, S, FP, LV, G) | Reclassify element discipline |
| **Set Location** | Set LOC token (BLD1, BLD2, BLD3, EXT) | Override building assignment |
| **Set Zone** | Set ZONE token (Z01-Z04) | Override zone assignment |
| **Set Status** | Set STATUS (NEW, EXISTING, DEMOLISHED, TEMPORARY) | Override lifecycle status |
| **Assign Numbers** | Re-sequence SEQ within (DISC, SYS, LVL) groups | Fix numbering gaps |
| **Build Tags** | Rebuild TAG1 from existing tokens | After manual token edits |

### Step-by-Step: Override a Token

1. **Select elements** in Revit (one or multiple)
2. Click the appropriate token command (e.g., **Set Location**)
3. Choose the new value from the dialog
4. The command:
   - Writes the new token value to selected elements
   - Rebuilds TAG1 from all current token values
   - Updates all containers and TAG7
   - Saves SEQ sidecar
   - Invalidates compliance cache

### Token Locking

To **prevent** a token from being overwritten by future auto-tagging:

1. Set the `ASS_TOKEN_LOCK_TXT` parameter on the element
2. Format: comma-separated token names, e.g., `"LOC,ZONE,SYS"`
3. Locked tokens are snapshot before pipeline execution and restored afterward
4. Works with all tagging commands including auto-tagger

### Bulk Parameter Write

**Location:** SELECT tab → Bulk Param Write

For large-scale token changes across many elements:

1. Click **Bulk Param Write**
2. The WPF dialog offers 4 operations:
   - **Set Token**: Choose token type + value, apply to selection
   - **Auto-populate**: Run full token population on selection
   - **Clear Tags**: Remove all 15 tag params from selection (with confirmation)
   - **Re-tag**: Force re-derive and overwrite all tags on selection
3. Preview shows affected element count before committing

### Cross-Discipline Token Validation

When setting DISC, the system checks for cross-discipline mismatches:
- If DISC changes from M to E but SYS is still HVAC → offers to auto-update SYS/FUNC
- Prevents invalid combinations like DISC=E with SYS=HVAC


---

## 7. Procedure 5: Stale Element Re-Tagging

### When to Use
- Elements have been moved to different levels or rooms
- MEP system connections have changed
- The `StingStaleMarker` IUpdater has flagged elements as stale

### How Stale Detection Works

The `StingStaleMarker` IUpdater runs continuously and detects:
- **Level changes**: Element moved to a different Revit level
- **LOC changes**: Element moved to a room in a different building
- **ZONE changes**: Element moved to a different zone
- **SYS changes**: MEP element's connected system type changed

When detected, `STING_STALE_BOOL` is set to `1` on the affected element.

### Command: Retag Stale Elements
**Location:** CREATE tab → QA → Retag Stale

### Step-by-Step

1. Click **Retag Stale**
2. The command finds all elements with `STING_STALE_BOOL = 1`
3. For each stale element:
   - Re-runs the full 11-step pipeline
   - Re-derives LVL, LOC, ZONE, SYS from current spatial context
   - Rebuilds TAG1 with corrected tokens
   - Clears `STING_STALE_BOOL = 0`
4. Results show how many elements were re-tagged

### Command: Select Stale Elements
**Location:** CREATE tab → QA → Select Stale

Selects all stale elements without modifying them — useful for visual review before re-tagging.

### Delta Sync (TagChanged)

For detecting token drift without the stale marker:
1. Scans all tagged elements
2. Re-derives current LVL/LOC/ZONE/SYS/FUNC/PROD from element context
3. Compares derived values against stored token values
4. Reports mismatches for selective review

---

## 8. Procedure 6: Tag Validation & QA

### When to Use
- After any tagging operation
- Before deliverables (COBie, IFC, transmittal)
- As part of daily QA workflow

### Primary Validation Command
**Location:** CREATE tab → QA → Validate

### Step-by-Step

1. Click **Validate**
2. The rich result panel shows:

**Section 1: Three-Bucket Compliance**
- **RESOLVED**: Tags with all 8 valid tokens (no placeholders)
- **COMPLETE WITH PLACEHOLDERS**: 8 segments but contains GEN/XX/ZZ/0000
- **INCOMPLETE**: Fewer than 8 segments
- **UNTAGGED**: No tag at all

**Section 2: ISO 19650 Code Compliance**
- Individual token validation against allowed code lists
- Cross-validation: DISC↔SYS, SYS↔FUNC, FUNC↔PROD consistency
- Invalid codes flagged with element count

**Section 3: Construction Status & Phasing**
- STATUS distribution (NEW/EXISTING/DEMOLISHED/TEMPORARY)
- Elements missing STATUS parameter

**Section 4: Revision Tracking**
- REV distribution
- Elements missing revision codes

**Section 5: Empty Token Analysis**
- Per-token empty count (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)

### QA Command Reference

| Command | Location | Type | Output |
|---------|----------|------|--------|
| **Validate Tags** | CREATE → QA → Validate | ReadOnly | Rich result panel with RAG bars |
| **Pre-Tag Audit** | CREATE → QA → Pre-Tag Audit | ReadOnly | Dry-run prediction report |
| **Completeness Dashboard** | CREATE → QA → Dashboard | ReadOnly | Per-discipline compliance % |
| **Find Duplicates** | ORGANISE → Analysis → Find Duplicates | ReadOnly | Duplicate tags, selects elements |
| **Highlight Invalid** | ORGANISE → Analysis → Highlight Invalid | Manual | Red=missing, Orange=incomplete |
| **Audit to CSV** | ORGANISE → Analysis → Audit to CSV | ReadOnly | Full 40+ column CSV export |
| **Tag Statistics** | ORGANISE → Analysis → Tag Stats | ReadOnly | Quick counts by discipline/system/level |
| **Tag Register Export** | ORGANISE → Analysis → Tag Register | ReadOnly | Comprehensive asset register CSV |
| **Quick Tag Preview** | CREATE → QA → Quick Preview | ReadOnly | Shows predicted tag for selected elements |
| **Container Pre-Check** | CREATE → QA → Container Check | ReadOnly | Verifies all container params are bound |

### Compliance Thresholds

| RAG Status | Threshold | Meaning |
|------------|-----------|---------|
| **GREEN** | ≥ 80% | Production-ready |
| **AMBER** | 50–80% | Needs attention |
| **RED** | < 50% | Critical — block deliverables |

### Compliance Gate

After tagging operations, a compliance gate automatically checks:
- If compliance is below `COMPLIANCE_GATE_PCT` (configurable, default varies)
- Shows per-discipline breakdown with stale count
- Suggests prioritized actions

---

## 9. Procedure 7: Visual Tag Annotation

### When to Use
- Producing annotated views for coordination
- Placing `IndependentTag` elements that display tag values
- Creating presentation-quality drawings with ISO 19650 tags

### Smart Tag Placement
**Location:** TAGS tab → Placement section → Smart Place Tags

### Step-by-Step

1. **Select scope**: Active view elements or selected elements
2. Click **Smart Place Tags**
3. The `TagPlacementEngine` runs:
   - Calculates element center points and bounding boxes
   - Generates 8 candidate positions (N, NE, E, SE, S, SW, W, NW)
   - Scores each position for:
     - Distance from element center (closer = better)
     - Overlap with existing tags (penalty)
     - Overlap with other elements (penalty)
     - Alignment with nearby tags (bonus)
     - Preferred side bias (configurable per category)
   - Places tag at highest-scoring position
   - Adds leader line if displacement exceeds threshold
4. **Review**: Tags appear as `IndependentTag` annotations in the view

### Data Prerequisite
The `SmartPlaceTags` command auto-runs `RunFullPipeline` on any untagged elements before placing visual annotations, ensuring data tags exist first.

### Tag Annotation Commands

| Command | Location | Description |
|---------|----------|-------------|
| **Smart Place Tags** | TAGS → Placement | 8-position collision-free placement |
| **Arrange Tags** | TAGS → Placement | Auto-arrange placed tags into aligned grids |
| **Remove Annotation Tags** | TAGS → Placement | Remove all IndependentTag annotations from view |
| **Batch Place Tags** | TAGS → Placement | Place across multiple views |
| **Learn Placement** | TAGS → Placement | Analyze existing placements to learn rules |
| **Apply Tag Template** | TAGS → Placement | Apply saved placement template |
| **Tag Overlap Analysis** | TAGS → Placement | Detect and report overlapping tags |
| **Batch Tag Text Size** | TAGS → Placement | Set text size for all tags in view |
| **Align Tag Bands** | TAGS → Placement | Grid-align tags by Y coordinate |

### Leader Management

| Command | Location | Description |
|---------|----------|-------------|
| **Toggle Leaders** | ORGANISE → Leaders | Toggle leaders on/off for selected tags |
| **Add/Remove Leaders** | ORGANISE → Leaders | Add or remove leaders from tags |
| **Align Tags** | ORGANISE → Leaders | Align tag heads horizontally/vertically |
| **Reset Tag Positions** | ORGANISE → Leaders | Move tags back to element centers |
| **Snap Leader Elbows** | ORGANISE → Leaders | Snap elbows to 45° or 90° angles |
| **Equalize Leader Lengths** | ORGANISE → Leaders | Match all leader lengths to median |
| **Flip Tags** | ORGANISE → Leaders | Mirror tag position across element center |
| **Pin/Unpin Tags** | ORGANISE → Leaders | Lock/unlock tag positions |
| **Nudge Tags** | ORGANISE → Leaders | Fine-adjust positions by small increments |

---

## 10. Procedure 8: Tag Style & Presentation

### When to Use
- Creating presentation views with discipline-colored tags
- Switching tag visual appearance for different audiences
- Applying paragraph depth control for TAG7 display

### Tag Style Commands
**Location:** TAGS tab → Style section

### Step-by-Step: Apply Color Scheme

1. Navigate to **TAGS → Style → Apply Color Scheme**
2. Choose a built-in scheme:

| Scheme | M | E | P | A | S | FP | LV | G |
|--------|---|---|---|---|---|----|----|---|
| **Discipline** | Blue | Yellow | Green | Grey | Red | Orange | Purple | Brown |
| **Warm** | Red shades progressing to yellow/cream |
| **Cool** | Navy to blue to cyan to mint |
| **Monochrome** | Black/grey scale (print-friendly) |
| **Dark** | Dark saturated tones |

3. Elements receive `OverrideGraphicSettings` with discipline-appropriate colors
4. Optionally switch tag family types to match discipline colors

### Display Modes

Set which token segments are visible in the display tag:

| Mode | Display | Example |
|------|---------|---------|
| 1 | SEQ only | 0003 |
| 2 | PROD-SEQ | AHU-0003 |
| 3 | DISC-SYS-SEQ | M-HVAC-0003 |
| 4 | DISC-PROD-SEQ | M-AHU-0003 |
| 5 | Full 8-segment | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 |

**Command:** TAGS → Style → Set Display Mode

### Paragraph Depth (TAG7)

Control how much TAG7 narrative is shown via 10 tiers:
- **Tier 1-3**: Compact (Identity only)
- **Tier 4-6**: Extended (Identity + System + Spatial)
- **Tier 7-8**: Technical (adds lifecycle + specs)
- **Tier 9-10**: Full Specification (all 6 sections)

**Command:** TAGS → Style → Set Paragraph Depth (slider dialog)


---

## 11. Procedure 9: Tag Register & Reporting

### When to Use
- Producing asset registers for FM/O&M handover
- COBie data drops
- Compliance reporting for BIM coordinators

### Tag Register Export
**Location:** ORGANISE → Analysis → Tag Register Export

Exports a comprehensive CSV with 40+ columns:

| Column Group | Columns |
|-------------|---------|
| **Identity** | ElementId, Category, Family, Type, Level, Room |
| **Source Tokens** | DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ |
| **Tags** | TAG1-TAG7 |
| **Status** | STATUS, REV, STALE |
| **Spatial** | Grid Ref, Room Name, Room Number, Department |
| **MEP** | Flow, Pressure, Voltage, Power, Circuit |
| **Dimensions** | Width, Height, Area, Volume, Length |
| **Cost** | Unit Rate, Total Cost |
| **Validation** | IsComplete, IsValid, MissingTokens, ISOViolations |

### Legend Generation
**Location:** CREATE tab → Legends section

31 legend commands for auto-generating color/tag/discipline/system legends:
- **Discipline Legend**: Color-coded discipline breakdown
- **System Legend**: System type with element counts
- **Tag Legend**: Tag format reference
- **Material Legend**: Material-to-color mapping
- **Equipment Legend**: Equipment types with counts
- **Fire Rating Legend**: Fire resistance ratings

---

## 12. Procedure 10: Resolve All Issues (One-Click Fix)

### When to Use
- Quick compliance improvement before a deadline
- Resolving placeholder tokens (GEN, XX, ZZ)
- Fixing anomalies detected by Pre-Tag Audit

### Command: Resolve All Issues
**Location:** CREATE tab → QA → Resolve All Issues

### What It Does

1. Finds all elements with incomplete or invalid tags
2. Processes in 500-element batches (configurable via `RESOLVE_BATCH_SIZE`)
3. Per element:
   - Runs `RunFullPipeline()` (full 11-step canonical pipeline)
   - Re-derives all tokens from current spatial/system context
   - Validates ISO 19650 codes and fixes violations
   - Rebuilds TAG1, containers, and TAG7
4. Shows progress dialog with cancel support
5. Reports before/after compliance improvement

### Anomaly Auto-Fix Chain

The Pre-Tag Audit command offers a one-click auto-fix that chains:
1. **AnomalyAutoFix** — Fixes DISC, LOC, ZONE, LVL, SYS, FUNC, PROD anomalies
2. **ResolveAllIssues** — Runs full pipeline on remaining issues
3. Shows before/after compliance delta

---

## 13. Procedure 11: Excel Round-Trip Tagging

### When to Use
- BIM coordinator needs to review/edit tags in Excel
- Bulk corrections that are easier in spreadsheet form
- External stakeholder review of tag assignments

### Step-by-Step

#### Export
1. Navigate to **BIM tab → Excel → Export to Excel**
2. Choose scope (selected elements or entire project)
3. Excel file is created with:
   - 30+ columns (identity, tokens, tags, geometry, context)
   - Read-only columns highlighted in grey
   - Editable token columns in white
   - Conditional formatting (pale red for empty tokens)
   - Data validation dropdowns for DISC, SYS, LOC, ZONE codes
   - Metadata worksheet with export date, project GUID

#### Edit in Excel
- Modify token values (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD)
- Type `CLEAR` to intentionally empty a field
- Do NOT modify read-only columns (ElementId, Category, Family, etc.)

#### Import
1. Navigate to **BIM tab → Excel → Import from Excel**
2. Select the edited Excel file
3. The import engine:
   - Validates all token values against ISO 19650 code lists
   - Cross-validates FUNC vs SYS and DISC vs SYS consistency
   - Shows change preview (additions, modifications, deletions)
   - Reports validation warnings before committing
4. On confirmation:
   - Applies changes to Revit elements
   - Runs TypeTokenInherit + PopulateAll + NativeMapper + BuildAndWriteTag
   - Captures audit trail (ASS_TAG_PREV_TXT + timestamp)
   - Saves SEQ sidecar

#### One-Click Round-Trip
**Command:** BIM tab → Excel → Round-Trip
- Export → opens Excel → waits for user to edit and save → imports changes

---

## 14. Procedure 12: Federated Model Tagging

### When to Use
- Multi-discipline federated models with Revit links
- Ensuring consistent tagging across linked models
- Visual tag annotations for linked elements

### Tag Export/Import Between Projects

**Export Tags:**
1. Navigate to **CREATE tab → Tools → Export Tag Map**
2. Exports all tagged elements to `.sting_tagmap.json`:
   - UniqueId, family, type, XYZ location
   - All 8 tokens + STATUS + REV

**Import Tags:**
1. In the target project, navigate to **CREATE tab → Tools → Import Tag Map**
2. Select the `.sting_tagmap.json` file
3. Matching strategy:
   - **Primary**: Match by Revit UniqueId (exact match)
   - **Fallback**: Match by family + type + nearest location (500mm radius)

### Linked Model Tag Placement

**Command:** TAGS → Placement → Batch Place Linked Tags
- Creates `IndependentTag` annotations for elements in linked Revit models
- Uses `Reference.CreateLinkReference()` for cross-model tag references
- Exports linked token manifest for coordination

### Federated Compliance Scanning

**Command:** BIM tab → Coordination → Federated Compliance
- Iterates all `RevitLinkInstance` objects
- Opens each linked document and runs ComplianceScan
- Returns per-link RAG status and aggregate federated compliance %

### SEQ Namespace Range Allocation

To prevent duplicate SEQ numbers across federated models:
1. Configure `SEQ_RANGE_ALLOCATION` in `project_config.json`:
```json
{
  "SEQ_RANGE_ALLOCATION": {
    "Architectural": [1, 2999],
    "Mechanical": [3000, 5999],
    "Electrical": [6000, 7999],
    "Plumbing": [8000, 8999],
    "Structural": [9000, 9999]
  }
}
```
2. Each discipline model uses only its allocated SEQ range
3. `ValidateSeqRange()` warns if SEQ falls outside allocation


---

## 15. Tag Format Reference

### 8-Segment Format

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
```

### Token Derivation Summary

| Token | Source | Default | Intelligence Layer |
|-------|--------|---------|-------------------|
| **DISC** | Revit category → DiscMap | A | Pipe correction (M→P for plumbing systems) |
| **LOC** | Room name/number → Project Info → Workset | BLD1 | Multi-pattern recognition |
| **ZONE** | Room department → Room name → Workset | Z01 | Directional mapping (N/S/E/W → Z01-Z04) |
| **LVL** | Revit level name parsing | L00 | Ground/Basement/Roof detection |
| **SYS** | 6-layer MEP connector traversal | Category default | Connected system detection |
| **FUNC** | SYS → FuncMap + HVAC subsystem detection | GEN | Supply/Return/Exhaust differentiation |
| **PROD** | Family name (35+ patterns) → Category fallback | GEN | FCU, VAV, RTU, DB, MCB, etc. |
| **SEQ** | Auto-incremented per (DISC, SYS, LVL) group | 0001 | Sidecar-persistent, collision-safe |

### Collision Handling

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| **Skip** | Never modify already-complete tags | Safe re-run, preserve edits |
| **Overwrite** | Force re-derive ALL tokens and reassign SEQ | Fresh start, fix bad data |
| **AutoIncrement** | Increment SEQ until tag is unique (max 100 attempts) | Default — safest for batch |

### SEQ Numbering Schemes

| Scheme | Format | Example |
|--------|--------|---------|
| **Numeric** | 4-digit padded | 0001, 0042, 0815 |
| **Alpha** | Letter sequence | A, B, ... Z, AA, AB |
| **ZonePrefix** | Zone + number | Z1-0042 |
| **DiscPrefix** | Discipline + number | M-0042 |

### Tag Examples by Discipline

```
Mechanical AHU on Level 2:         M-BLD1-Z01-L02-HVAC-SUP-AHU-0003
Electrical Distribution Board:     E-BLD1-Z01-GF-LV-PWR-DB-0001
Plumbing Fixture in Basement:      P-BLD1-Z02-B1-SAN-SAN-FIX-0003
Fire Sprinkler on Level 1:         FP-BLD1-Z01-L01-FP-FP-SPR-0001
Architectural Door Level 3:        A-BLD1-Z01-L03-ARC-FIT-DR-0012
External Lighting:                 E-EXT-Z01-L00-LV-PWR-LUM-0001
Structural Column Ground Floor:    S-BLD1-Z01-GF-STR-STR-COL-0001
Security Camera Level 1:           LV-BLD1-Z03-L01-SEC-SEC-SEC-0001
Nurse Call Device Level 2:         LV-BLD1-Z01-L02-NCL-NCL-NCL-0001
Data Device (Server Room):         LV-BLD1-Z03-L01-ICT-ICT-DAT-0005
```

---

## 16. Configuration Reference

### project_config.json

Located alongside the `.rvt` file (preferred) or in the plugin's data directory.

```json
{
  "DISC_MAP": { "Doors": "A", "Custom Category": "X" },
  "SYS_MAP": { "HVAC": ["Air Terminals", "Ducts"], "CUSTOM": ["Custom"] },
  "PROD_MAP": { "Custom Category": "CUS" },
  "FUNC_MAP": { "CUSTOM": "CST" },
  "LOC_CODES": ["BLD1", "BLD2", "BLD3", "EXT"],
  "ZONE_CODES": ["Z01", "Z02", "Z03", "Z04"],
  "TAG_FORMAT": {
    "separator": "-",
    "num_pad": 4,
    "segment_order": ["DISC","LOC","ZONE","LVL","SYS","FUNC","PROD","SEQ"]
  },
  "TAG_PREFIX": "",
  "TAG_SUFFIX": "",
  "CATEGORY_SKIP": ["Topography", "Site"],
  "CATEGORY_FORCE_SYS": { "Sprinklers": "FP" },
  "CATEGORY_TOKEN_OVERRIDES": {
    "Sprinklers": { "DISC": "FP", "SYS": "FP", "FUNC": "FP" }
  },
  "SEQ_SCHEME": "Numeric",
  "SEQ_INCLUDE_ZONE": false,
  "COMPLIANCE_GATE_PCT": 80,
  "PROXIMITY_RADIUS_FT": 10.0,
  "RESOLVE_BATCH_SIZE": 500,
  "CUSTOM_VALID_DISC": ["X", "Y"],
  "CUSTOM_VALID_SYS": ["CUSTOM"],
  "CUSTOM_VALID_FUNC": ["CST"],
  "CUSTOM_VALID_LOC": ["SITE1"],
  "CUSTOM_VALID_ZONE": ["Z05", "Z06"],
  "AUTO_TAGGER_ENABLED": false,
  "AUTO_TAGGER_VISUAL": false,
  "AUTO_TAGGER_DISC_FILTER": "",
  "PERF_TRACKING_ENABLED": false
}
```

### Key Configuration Options

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `TAG_FORMAT.separator` | string | `-` | Character between segments |
| `TAG_FORMAT.num_pad` | int | 4 | SEQ padding width |
| `TAG_PREFIX` | string | `""` | Prepend to every TAG1 |
| `TAG_SUFFIX` | string | `""` | Append to every TAG1 |
| `CATEGORY_SKIP` | array | `[]` | Categories to exclude from tagging |
| `SEQ_SCHEME` | string | `Numeric` | Numeric/Alpha/ZonePrefix/DiscPrefix |
| `SEQ_INCLUDE_ZONE` | bool | `false` | Include ZONE in SEQ counter key |
| `COMPLIANCE_GATE_PCT` | int | 80 | Minimum compliance % for gate |
| `PROXIMITY_RADIUS_FT` | float | 10.0 | Radius for CopyTokensFromNearest |
| `RESOLVE_BATCH_SIZE` | int | 500 | Elements per batch in ResolveAll |

---

## 17. Troubleshooting

### Common Issues

| Symptom | Cause | Solution |
|---------|-------|----------|
| "No parameters found" | Shared params not loaded | Run Load Params first |
| All elements get DISC=A | Category not in DiscMap | Add custom mapping to project_config.json |
| LOC always BLD1 | No rooms placed | Place and name rooms, or set LOC in config |
| ZONE always Z01 | No department data on rooms | Set Room.Department parameter |
| SYS defaults to GEN | MEP systems not defined | Create/assign piping/duct systems |
| Duplicate SEQ numbers | Sidecar file missing/corrupt | Delete `.sting_seq.json` and re-tag |
| Tags not appearing | Visual tagging disabled | Enable via Toggle Visual command |
| Auto-tagger not firing | Updater disabled | Enable via Toggle Auto-Tagger |
| Elements skipped | Workset owned by another user | Sync with central first |
| TAG7 empty | RunFullPipeline not completing | Check StingTools.log for errors |
| Stale elements accumulating | StingStaleMarker detecting changes | Run Retag Stale periodically |
| Compliance stuck at 0% | ComplianceScan cache stale | Any tagging command auto-invalidates |

### Log File

All operations are logged to `StingTools.log` alongside the DLL:
- **Info**: Normal operations, element counts, timing
- **Warn**: Non-critical issues (read-only params, missing data)
- **Error**: Failures with stack traces

---

## 18. Performance & Efficiency Recommendations

### Model Preparation

1. **Place rooms before tagging** — LOC and ZONE auto-detection accuracy improves dramatically
2. **Name levels consistently** — Use "Ground Floor", "Level 1", "Basement 1" etc.
3. **Define MEP systems** — Connect ducts/pipes to named systems for SYS accuracy
4. **Use standard family names** — PROD code detection relies on family name patterns

### Tagging Strategy

1. **Use Tag & Combine for first-time tagging** — Single command, full pipeline
2. **Use Tag New Only for incremental work** — 10-100x faster than re-tagging everything
3. **Enable auto-tagger during modeling** — Zero-touch tagging, no batch runs needed
4. **Run Pre-Tag Audit before large batches** — Catch issues before committing
5. **Lock critical tokens** — Use `ASS_TOKEN_LOCK_TXT` for manually-set values

### Performance Optimization

| Technique | Impact | How |
|-----------|--------|-----|
| **Enable formula caching** | 40% fewer CSV reads | Automatic (5-min TTL) |
| **Enable grid line caching** | Skip repeated grid scans | Automatic (2-min TTL) |
| **Disable performance tracking** | Reduce per-element overhead | Set `PERF_TRACKING_ENABLED: false` |
| **Use discipline filter on auto-tagger** | Process fewer elements | Configure via Auto-Tagger Config |
| **Use selective WriteContainers** | 60-80% fewer container writes | Automatic (discipline-prefix filtering) |
| **Chunked transactions** | Avoid Revit memory pressure | Automatic (200-element batches) |

### Workflow Automation

Use built-in workflow presets for common sequences:

| Preset | Steps | Use Case |
|--------|-------|----------|
| **DailyQA** | Retag stale → Pre-tag audit → Tag new → Validate | Daily coordinator routine |
| **MorningHealthCheck** | 10 adaptive steps with compliance gates | Morning startup |
| **PostTaggingQA** | Audit → Validate → Dashboard → Register → Template check | After tagging session |
| **WeeklyDataDrop** | Retag → Resolve → Validate → COBie → Excel → Register | ISO 19650 data exchange |

---

*This guide reflects the StingTools codebase as of March 2026. All procedures, commands, and configuration options are subject to change. Refer to the CLAUDE.md file for the authoritative technical reference.*

