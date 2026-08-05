# STING Tools — BIM Management, Coordination & Document Management Workflow Guide

**Version**: 7.0 | **Standard**: ISO 19650-1/2/3, BS EN ISO 19650, PAS 1192-2/3  
**Last Updated**: 2026-03-24 | **Audience**: BIM Managers, BIM Coordinators, Information Managers  
**Applicable Regulations**: UK Building Safety Act 2022, Building Regulations Part B/L/M, CDM 2015

---

## Table of Contents

1. [Introduction & Purpose](#1-introduction--purpose)
2. [Roles & Responsibilities (ISO 19650)](#2-roles--responsibilities-iso-19650)
3. [Daily BIM Coordinator Workflow](#3-daily-bim-coordinator-workflow)
4. [Model Setup & Configuration](#4-model-setup--configuration)
5. [Tagging Workflow (ISO 19650 Asset Tags)](#5-tagging-workflow-iso-19650-asset-tags)
6. [Document Management & CDE State Machine](#6-document-management--cde-state-machine)
7. [Issue Management & BCF Coordination](#7-issue-management--bcf-coordination)
8. [Revision Management & Audit Trail](#8-revision-management--audit-trail)
9. [Coordination & Clash Detection](#9-coordination--clash-detection)
10. [Compliance & Quality Assurance](#10-compliance--quality-assurance)
11. [Warnings Management](#11-warnings-management)
12. [Workflow Automation Presets](#12-workflow-automation-presets)
13. [Data Exchange (Excel/COBie/IFC/BCF)](#13-data-exchange-excelcobieifc-bcf)
14. [Handover & FM Data](#14-handover--fm-data)
15. [BEP & Governance](#15-bep--governance)
16. [Reporting & Dashboards](#16-reporting--dashboards)
17. [International Standards Reference](#17-international-standards-reference)
18. [BIM Coordination Center (Unified Dashboard)](#18-bim-coordination-center-unified-dashboard)
19. [Meeting Management & Action Tracking](#19-meeting-management--action-tracking)
20. [4D/5D Scheduling & Cost Management](#20-4d5d-scheduling--cost-management)
21. [Troubleshooting & FAQ](#21-troubleshooting--faq)
22. [Command Quick Reference](#22-command-quick-reference)

---

## 1. Introduction & Purpose

### What This Guide Covers

This guide provides **step-by-step procedures** for every BIM management and coordination workflow available in STING Tools. It is designed for BIM Managers and Coordinators who need to:

- **Manage ISO 19650 compliance** across multi-discipline Revit models
- **Coordinate BIM deliverables** including COBie, IFC, BCF, and document packages
- **Track and resolve issues** through structured BCF-compatible workflows
- **Automate repetitive tasks** using 30+ built-in workflow presets
- **Generate reports** for client reviews, regulatory submissions, and handover

### Why Use STING Tools for BIM Coordination?

| Challenge | Without STING | With STING |
|-----------|--------------|------------|
| Asset tagging | Manual entry, inconsistent formats | Auto-tag 10,000+ elements in <60 seconds |
| Compliance checking | Visual inspection, human error | Real-time RAG dashboard, 45+ validation checks |
| COBie export | Manual spreadsheet assembly | One-click export with 22 project-type presets |
| Issue tracking | Email chains, lost context | Structured BCF with element linking |
| Document management | Folder chaos, naming inconsistency | ISO 19650 CDE state machine with audit trail |
| Coordination meetings | Unstructured, no follow-up | Auto-agenda, action tracking, SLA enforcement |

### Prerequisites

Before using this guide, ensure:
1. STING Tools plugin is loaded in Revit 2025/2026/2027
2. Shared parameters are bound (run **Load Params** or **Master Setup**)
3. `project_config.json` is configured for your project (or use **Project Setup Wizard**)
4. The STING dockable panel is visible (click **STING Panel** on ribbon)

---

## 2. Roles & Responsibilities (ISO 19650)

### ISO 19650 Role Definitions

STING Tools implements 14 ISO 19650 roles. Each role has specific CDE access rights, approval capabilities, and notification routing:

| Code | Role | CDE Write | CDE Approve | Typical Actions in STING |
|------|------|-----------|-------------|--------------------------|
| **A** | Architect | WIP, SHARED | No | Create/tag architectural elements, sheet management |
| **M** | MEP Engineer | WIP, SHARED | No | Tag MEP elements, system push, MEP schedules |
| **E** | Electrical Engineer | WIP, SHARED | No | Tag electrical, conduit/cable tray routing |
| **S** | Structural Engineer | WIP, SHARED | No | Tag structural, Excel structural import |
| **H** | Health & Safety | SHARED (read) | No | Review compliance reports, CDM compliance |
| **P** | Project Manager | All folders | SHARED→PUBLISHED | Workflow automation, compliance gates |
| **C** | BIM Coordinator | All folders | WIP→SHARED | Daily coordination, clash resolution, issue management |
| **I** | Information Manager | All folders | All transitions | CDE state management, BEP governance |
| **K** | Client Representative | PUBLISHED (read) | PUBLISHED→ARCHIVE | Review deliverables, approve handover |
| **Q** | QA Manager | All (read) | No | Validate compliance, audit trails |
| **F** | Facilities Manager | PUBLISHED, ARCHIVE | No | COBie review, asset management, maintenance |
| **W** | Cost Consultant | SHARED (read) | No | 5D cost data, BOQ export |
| **L** | Lead Designer | All folders | WIP→SHARED | Design review, template management |
| **Z** | Administrator | All folders | All transitions | User management, system configuration |

### Setting Your Role

1. Open **BIM Coordination Center** (BIM tab → Coordination Center)
2. Navigate to **PERMISSIONS** tab
3. Click **Edit Role** and select your ISO 19650 role code
4. Role is saved to `project_config.json` as `USER_ROLE`

**Why**: Your role determines CDE folder access, notification routing, and approval capabilities.

---

## 3. Daily BIM Coordinator Workflow

### Overview: The Coordinator's Day

A BIM coordinator's typical day follows this 6-phase cycle:

```
07:30  MORNING HEALTH CHECK ──→ Review overnight changes, stale elements, warnings
08:30  COORDINATION ──────────→ Clash detection, issue resolution, meetings
10:00  PRODUCTION SUPPORT ────→ Help discipline teams with tagging, templates
12:00  QUALITY ASSURANCE ─────→ Validate compliance, run audits, fix anomalies
14:00  DATA EXCHANGE ─────────→ COBie export, Excel round-trip, platform sync
16:00  END OF DAY SYNC ───────→ Create revision, save baselines, export reports
```

### Phase 1: Morning Health Check (07:30-08:30)

**Goal**: Understand model state, fix overnight issues, prepare for coordination.

#### Step 1: Open Model & Review Morning Briefing

When you open the Revit model, STING automatically:
- Runs `ComplianceScan` to assess tag compliance
- Shows **Morning Briefing Dialog** if any alerts exist:
  - Tag compliance % with RAG status (Red <50%, Amber 50-80%, Green >80%)
  - 7-day compliance trend (improving/stable/declining)
  - Stale element count (elements moved since last tag)
  - Warning count with severity breakdown
  - Overdue SLA violations

**Action**: Click **"Run Morning Health Check"** to execute the automated workflow.

#### Step 2: Run Morning Health Check Workflow

**How**: BIM tab → Workflows → "Morning Health Check" (or Coordination Center → WORKFLOWS → MorningHealthCheck)

This preset executes 8 steps automatically:
1. **Retag Stale** — Re-derives tags for elements that moved/changed
2. **Warnings Auto-Fix** — Resolves safe-to-fix warnings (duplicates, room tags, marks)
3. **Tag New** — Tags newly placed elements since last session
4. **Pre-Tag Audit** — Dry-run showing predicted issues
5. **Validate Tags** — Checks all tags against ISO 19650 codes
6. **Template Assign** — Auto-assigns view templates to unassigned views
7. **Tag Sheets** — Ensures all sheets have proper naming
8. **Revision Check** — Verifies revision tracking is current

**Time saved**: ~45 minutes vs manual checks

#### Step 3: Review Compliance Dashboard

**How**: BIM tab → Completeness Dashboard (or status bar at bottom of dockable panel)

The dashboard shows:
- **Overall compliance**: X% tagged, Y% fully resolved, Z% with placeholders
- **Per-discipline breakdown**: M=92%, E=87%, P=78%, A=95%, S=91%
- **Container compliance**: % of tagged elements with all discipline containers populated
- **Stale count**: Elements needing re-tagging
- **Warning health score**: 0-100 weighted score

**Decision tree**:
- Compliance >90%: Proceed to coordination
- Compliance 70-90%: Run targeted fixes (BatchTag, ResolveAllIssues)
- Compliance <70%: Investigate data quality issues before proceeding

### Phase 2: Coordination (08:30-10:00)

#### Step 4: Check Warnings Dashboard

**How**: BIM tab → Warnings Manager → Dashboard

Review:
- **Critical/High warnings**: Must be resolved before handover
- **Root-cause groups**: 200 "duplicate instance" warnings = 1 root cause
- **Deliverable impact**: Which warnings affect COBie/IFC/FM Handover?
- **SLA violations**: Which warnings have exceeded their time limit?

**Actions**:
- Click **Auto-Fix** to resolve safe categories (duplicates, room tags, marks)
- Click **Select Elements** to navigate to warning locations
- Click **Create Issue** to escalate critical warnings to issue tracker

#### Step 5: Run Clash Detection

**How**: BIM tab → Coordination → Clash Detection

Options:
- **Host model only**: Check MEP vs Structure within current model
- **Cross-model**: Include linked Revit models with transform-aware intersection
- **Category-specific**: e.g., Ducts vs Structural Framing only

Results:
- Clash count by category pair
- Zoom-to-3D for each clash
- One-click BCF export for external coordination

#### Step 6: Review & Resolve Issues

**How**: BIM tab → Coordination Center → ISSUES tab (or Document Management Center → ISSUES tab)

Issue lifecycle:
```
OPEN → IN_PROGRESS → RESOLVED → CLOSED
  ↓                      ↑
  └── ESCALATED ─────────┘
```

For each open issue:
1. **Double-click** to select linked elements in model
2. **Right-click → Zoom to 3D** to see affected area
3. Fix the issue in the model
4. **Update status** to RESOLVED with comment
5. STING auto-updates linked transmittals and meeting action items

#### Step 7: Prepare for Coordination Meeting

**How**: BIM tab → Coordination Center → MEETINGS tab → Auto Agenda

The **Auto Agenda** feature generates a meeting agenda from:
- Open issues grouped by type and priority
- Pending transmittals awaiting response
- Recent revisions and their scope
- Current compliance status per discipline
- Open action items from previous meetings

**Action**: Click **Auto Agenda** → Review → **New Meeting** → Set date/type → Save

### Phase 3: Production Support (10:00-12:00)

#### Step 8: Help Teams with Tagging

Common requests from discipline teams:

| Request | STING Command | Tab |
|---------|---------------|-----|
| "Tag my new elements" | Tag New Only | CREATE |
| "Fix my duplicate tags" | Fix Duplicates | CREATE → More |
| "Set discipline for selection" | Set Discipline | CREATE → Tokens |
| "Populate all tokens" | Family-Stage Populate | CREATE → More |
| "See tag preview" | Quick Tag Preview | CREATE → QA |

#### Step 9: Template Management

**How**: VIEW tab → Template Manager section

Key operations:
- **Auto-Assign Templates**: 5-layer algorithm matches views to templates
- **Template Audit**: Identifies unassigned or mismatched views
- **Compliance Score**: 10-point scale per view
- **Sync Overrides**: Push VG changes from template to all views

### Phase 4: Quality Assurance (12:00-14:00)

#### Step 10: Run Full Validation

**How**: Use the PostTaggingQA workflow preset, or manually:

1. **Pre-Tag Audit** (CREATE → More → Pre-Tag Audit)
   - Dry-run showing predicted tags without making changes
   - Identifies ISO violations, collision predictions, spatial detection gaps
   - Shows auto-fix action button if issues found

2. **Validate Tags** (CREATE → QA → Validate)
   - 4-bucket compliance: RESOLVED / COMPLETE_PLACEHOLDERS / INCOMPLETE / UNTAGGED
   - ISO 19650 code validation (DISC/SYS/FUNC/PROD/LOC/ZONE against code lists)
   - STATUS and REV population check
   - Per-discipline breakdown with RAG bars

3. **Validate Template** (TEMP → Data Pipeline → Validate Template)
   - 45 validation checks: data files, parameter consistency, material completeness
   - Schema validation against MATERIAL_SCHEMA.json
   - Formula dependency verification
   - Cross-reference audit

4. **Sheet Compliance** (DOCS → Templates & Compliance → ISO Compliance)
   - 10 ISO 19650 sheet rules: naming, numbering, duplicates, title block, viewport count

#### Step 11: Resolve Issues

**How**: CREATE → More → Resolve All Issues

This one-click command:
1. Runs full pipeline on all non-compliant elements (500-element batches)
2. Re-derives tokens from spatial/category/family data
3. Rebuilds tags with collision avoidance
4. Updates all 53 containers
5. Rebuilds TAG7 narrative
6. Saves SEQ counters to sidecar file
7. Shows before/after compliance improvement

### Phase 5: Data Exchange (14:00-16:00)

#### Step 12: Export COBie Data

**How**: BIM tab → COBie Export (or BIM → Coordination Center → PLATFORM tab)

Steps:
1. Select **project type preset** (22 options: Commercial Office, Healthcare NHS, etc.)
2. Choose **output format** (XLSX via ClosedXML)
3. Review **pre-flight check**: container staleness detection
4. Click **Export**

COBie output includes 19 worksheets:
- Instruction, Contact, Facility, Floor, Space, Zone
- Type, Component, System, Assembly, Connection
- Spare, Resource, Job, Impact, Document, Attribute
- Coordinate, Issue

**Pre-export compliance gate**: Blocks export below 60% tag compliance with detailed breakdown.

#### Step 13: Excel Round-Trip

**How**: BIM tab → Excel → Round-Trip

Steps:
1. **Export** to Excel with 30+ columns (tags, identity, spatial, MEP data)
2. **Edit** in Excel (FM team adds serial numbers, warranty dates, etc.)
3. **Import** back with validation:
   - Token validation against ISO 19650 code lists
   - Cross-validation (FUNC must match SYS, DISC must match SYS)
   - CLEAR sentinel support (type "CLEAR" to intentionally empty a field)
   - Change preview before commit

#### Step 14: Platform Sync

**How**: BIM tab → Platform → Platform Sync

Options:
- **ACC/BIM 360**: Publish deliverable package
- **CDE Package**: ISO 19650 folder structure generation
- **BCF Export/Import**: Issue exchange with external tools
- **SharePoint**: Corporate document repository sync

### Phase 6: End of Day Sync (16:00-17:00)

#### Step 15: Run End-of-Day Workflow

**How**: BIM tab → Workflows → "End of Day" (or Coordination Center → WORKFLOWS)

This preset executes 8 steps:
1. **Retag Stale** — Catch any elements moved during the day
2. **Validate Tags** — Final compliance check
3. **Save Baseline** — Warning baseline for tomorrow's comparison
4. **Export Registers** — Tag register + sheet register to CSV
5. **Model Health** — Dashboard snapshot
6. **Warnings Export** — CSV for external tracking
7. **Create Revision** — Day-end revision with compliance snapshot
8. **Compliance Dashboard** — Final status for daily report

#### Step 16: Create Revision

**How**: BIM tab → Revisions → Create Revision

Features:
- ISO 19650 naming (REV-001, REV-002, etc.)
- Pre-revision compliance gate (warns if <80%)
- Tag snapshot for change tracking across revisions
- Auto-creates revision entry in project

#### Step 17: Export Weekly Report (Friday)

**How**: BIM tab → Coordination Center → WORKFLOWS → Weekly Coordinator Report

Generates self-contained HTML report with:
- KPI cards (compliance, warnings, issues, stale)
- 7-day compliance trend with RAG bar
- Per-discipline compliance table
- Warning root-cause summary (top 10)
- Issue open/close metrics


---

## 4. Model Setup & Configuration

### 4.1 First-Time Project Setup

#### Option A: Project Setup Wizard (Recommended)

**How**: TEMP tab → Setup → Project Setup Wizard

The 7-page wizard walks through:

| Page | Configuration | What It Does |
|------|--------------|--------------|
| 1. Project Info | Name, number, client, address | Sets Revit Project Information parameters |
| 2. Discipline Config | Select active disciplines (M/E/P/A/S/FP/LV/G) | Controls which tag families are loaded |
| 3. Standards | ISO 19650, CIBSE, BS 7671, Uniclass | Enables standard-specific validation rules |
| 4. Tag Format | Separator, padding, segment order | Configures tag assembly format |
| 5. Spatial Config | Building codes, zone definitions | Sets LOC/ZONE auto-detection rules |
| 6. CDE Config | Status codes, suitability codes | Configures document management |
| 7. Review | Summary of all settings | Confirm and apply |

#### Option B: Master Setup (One-Click)

**How**: TEMP tab → Setup → Master Setup

Executes 18 steps automatically:
1. Load shared parameters (2-pass binding)
2. Create BLE materials (815 from CSV)
3. Create MEP materials (464 from CSV)
4. Create wall types
5. Create floor types
6. Create ceiling types
7. Create roof types
8. Create duct types
9. Create pipe types
10. Create cable tray types
11. Create conduit types
12. Batch create schedules (168 definitions)
13. Create filters (28 standard filters)
14. Create worksets (35 worksets)
15. Create view templates (23 templates)
16. Create line patterns (10 patterns)
17. Create phases (6 phases)
18. Validate template (45 checks)

**Time**: ~2-5 minutes depending on model size.

#### Option C: Manual Step-by-Step

For fine-grained control:

1. **Load Parameters**: TEMP → Setup → Create Parameters
2. **Check Data Files**: TEMP → Setup → Check Data Files
3. **Materials**: TEMP → Materials → Create BLE/MEP Materials
4. **Families**: TEMP → Families → Walls/Floors/Ceilings/Roofs
5. **Schedules**: TEMP → Schedules → Batch Create
6. **Templates**: TEMP → Templates → Create Filters/Worksets/View Templates

### 4.2 Project Configuration File

The `project_config.json` file (alongside your .rvt) controls all project-specific settings:

```json
{
    "TAG_PREFIX": "",
    "TAG_SUFFIX": "",
    "TAG_FORMAT": {
        "separator": "-",
        "num_pad": 4,
        "segment_order": ["DISC","LOC","ZONE","LVL","SYS","FUNC","PROD","SEQ"]
    },
    "CATEGORY_SKIP": ["Generic Models", "Parking"],
    "CATEGORY_FORCE_SYS": {
        "Sprinklers": "FP",
        "Cable Trays": "LV"
    },
    "CATEGORY_TOKEN_OVERRIDES": {
        "Structural Columns": { "DISC": "S", "SYS": "STR" }
    },
    "SEQ_SCHEME": "Numeric",
    "SEQ_INCLUDE_ZONE": false,
    "COMPLIANCE_GATE_PCT": 80,
    "PROXIMITY_RADIUS_FT": 10.0,
    "RESOLVE_BATCH_SIZE": 500,
    "SHEET_NAMING_STRICT_MODE": false,
    "COST_RATES_FILE": "cost_rates_5d.csv",
    "PROJECT_TYPE": "CommercialOffice",
    "USER_ROLE": "C",
    "CUSTOM_VALID_DISC": ["FP", "LV", "MR"],
    "CUSTOM_VALID_SYS": ["CCTV", "SOLAR"],
    "WARNING_SLA_CRITICAL_HOURS": 4,
    "WARNING_SLA_HIGH_HOURS": 24,
    "WARNING_SLA_MEDIUM_HOURS": 168,
    "WARNING_SLA_LOW_HOURS": 336,
    "CDE_SHARED_MIN_COMPLIANCE": 70,
    "CDE_PUBLISHED_MIN_COMPLIANCE": 90,
    "AUTO_TAGGER_VISUAL": false,
    "AUTO_TAGGER_DISC_FILTER": "",
    "PERF_TRACKING_ENABLED": false,
    "DISCIPLINE_LEADS": {
        "M": "John Smith",
        "E": "Jane Doe"
    }
}
```

**Edit**: CREATE → Setup → Configure (or use Guided Data Editor command)

### 4.3 Data File Inventory

Run **Check Data Files** (TEMP → Setup) to verify all 43 runtime data files are present with SHA-256 integrity checks:

| File | Purpose | Rows |
|------|---------|------|
| MR_PARAMETERS.txt | Shared parameter definitions | 2,307 params |
| BLE_MATERIALS.csv | Building element materials | 815 rows |
| MEP_MATERIALS.csv | MEP materials | 464 rows |
| MR_SCHEDULES.csv | Schedule definitions + TPL metadata | 168 schedules + 21 TPL entries |
| PARAMETER_REGISTRY.json | Master parameter registry | 638+ params |
| LABEL_DEFINITIONS.json | Label/legend definitions | 10,775 lines |
| TAG_CONFIG_v5_0_*.csv | Tag configuration (4 files) | 480+ rows |
| FORMULAS_WITH_DEPENDENCIES.csv | Formula definitions | 199 formulas |
| COBIE_*.csv | COBie reference data (8 files) | 444+ rows |
| cost_rates_5d.csv | 5D cost rates | 50+ rows |
| TAG_STYLE_RULES.json | Tag style rules | 128 combinations |
| WORKFLOW_*.json | Workflow presets | 3+ presets |

### 4.2 Schedule Column Alignment (TPL_Schedule_Metadata)

`MR_SCHEDULES.csv` includes **TPL_Schedule_Metadata** entries that define human-readable column headers for Revit schedules. When batch-creating schedules, these metadata entries map internal STING parameter names to display-friendly column aliases.

**Format**: Each alias field uses `PARAM_NAME=Column Header` syntax:

```
TPL_Schedule_Metadata,Text Style Schedule,...,TS_NAME_TXT=Style Name,TS_FONT_NAME_TXT=Font Name,...
```

**21 TPL_Schedule_Metadata entries** covering:

| Schedule | Key Aliases |
|----------|-------------|
| Text Style Schedule | `TS_NAME_TXT=Style Name`, `TS_FONT_NAME_TXT=Font Name` |
| Workset Schedule | `WS_NAME_TXT=Workset Name`, `WS_EDITABLE_DEFAULT_BOOL=Editable by Default` |
| Fill Pattern Schedule | `FP_NAME_TXT=Pattern Name`, `FP_PATTERN_TYPE_TXT=Pattern Type` |
| Line Style Schedule | `LS_NAME_TXT=Line Style`, `LS_LINE_WEIGHT_TXT=Line Weight` |
| View Template Schedule | `VT_NAME_TXT=Template Name`, `VT_DISCIPLINE_TXT=Discipline` |
| Arrowhead Schedule | `AH_NAME_TXT=Arrowhead Name` |
| Line Pattern Schedule | `LP_NAME_TXT=Pattern Name` |
| Line Weight Schedule | `LW_MODEL_WEIGHT_1_TXT=Model Weight 1` |
| Phase Filter Schedule | `PF_NEW_TXT=New`, `PF_EXISTING_TXT=Existing` |
| Schedule Parameter Schedule | `SP_NAME_TXT=Parameter Name` |
| Schedule Template Schedule | `ST_NAME_TXT=Template Name` |

These aliases are consumed by `ScheduleCommands.cs` to set `ScheduleField.ColumnHeading` during batch schedule creation, replacing cryptic parameter names with BIM-coordinator-friendly headers.

**Command**: TEMP → Schedules → Batch Create Schedules

---

## 5. Tagging Workflow (ISO 19650 Asset Tags)

### 5.1 Tag Format

Every element receives an 8-segment ISO 19650 tag:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
 M   - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

| Segment | Source | Auto-Detection Method |
|---------|--------|----------------------|
| DISC | Category → DiscMap (41 mappings) | Automatic from element category |
| LOC | Room → building code, or Project Info | `SpatialAutoDetect.DetectLoc()` |
| ZONE | Room → department, or name patterns | `SpatialAutoDetect.DetectZone()` |
| LVL | Element level → short code | `ParameterHelpers.GetLevelCode()` |
| SYS | MEP system name → CIBSE code | `GetMepSystemAwareSysCode()` (6-layer) |
| FUNC | SYS → function lookup | `GetFuncCode()` from CIBSE/Uniclass |
| PROD | Family name → 35+ patterns | `GetFamilyAwareProdCode()` |
| SEQ | Counter per (DISC,SYS,FUNC,PROD) group | Auto-increment with collision avoidance |

### 5.2 Tagging Commands

#### One-Click Full Automation

**Tag & Combine** (CREATE tab → Tag & Combine)

The most powerful single command — does everything:
1. Auto-detects LOC/ZONE from room data
2. Populates all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
3. Inherits type-level tokens first
4. Maps 30+ native Revit parameters
5. Evaluates 199 formulas
6. Builds ISO 19650 tag with collision detection
7. Writes to all 53 containers
8. Generates TAG7 rich narrative (A-F sub-sections)
9. Detects grid reference
10. Saves sequence counters

**Scope options**: Active view / Selected elements / Entire project
**Collision modes**: Skip existing / Overwrite / Auto-increment SEQ

#### Incremental Tagging

**Tag New Only** (CREATE tab → More → Tag New Only)

Tags only untagged elements — much faster for adding new elements to an already-tagged model.

**Batch Tag** (CREATE tab → Batch Tag)

Tags ALL elements in the entire project. Best for initial project setup.

**Auto Tag** (CREATE tab → Auto Tag)

Tags elements in the active view only. Good for view-by-view tagging.

#### Pre-Tagging Validation

**Pre-Tag Audit** (CREATE tab → More → Pre-Tag Audit)

Dry-run that predicts:
- What tags will be assigned
- Which tokens will have placeholders (GEN/XX/ZZ)
- Which codes violate ISO 19650
- Collision predictions
- Spatial detection coverage

Rich result panel with 8 sections and auto-fix action button.

### 5.3 Token Management

#### Individual Token Commands

| Command | Tab Location | What It Does |
|---------|-------------|--------------|
| Set Discipline | CREATE → Tokens | Set DISC code (M/E/P/A/S/FP/LV/G) |
| Set Location | CREATE → Tokens | Set LOC code (BLD1/BLD2/BLD3/EXT) |
| Set Zone | CREATE → Tokens | Set ZONE code (Z01-Z04) |
| Set Status | CREATE → Tokens | Set STATUS (NEW/EXISTING/DEMOLISHED/TEMPORARY) |
| Assign Numbers | CREATE → Tokens | Sequential numbering within groups |
| Build Tags | CREATE → Tokens | Rebuild TAG1 from existing tokens |
| Combine Parameters | CREATE → Tokens | Write to all discipline containers |

#### Bulk Operations

**Bulk Parameter Write** (SELECT tab → Bulk Param)

Unified dialog with 4 operations:
1. **Set Token**: Pick token type → pick value → apply to selection
2. **Auto-populate**: Run full token population on selection
3. **Clear Tags**: Remove all 15 tag parameters from selection
4. **Re-tag**: Force re-derive all tags with overwrite

### 5.4 Tag Collision Handling

When two elements would receive the same tag (same DISC-LOC-ZONE-LVL-SYS-FUNC-PROD group):

| Mode | Behavior | Use Case |
|------|----------|----------|
| **Skip** | Keep existing tag, skip element | Incremental tagging |
| **Overwrite** | Replace with new tag | Correcting mistakes |
| **Auto-Increment** | Assign next available SEQ | Default — recommended |

SEQ counters persist in `.sting_seq.json` sidecar file alongside the .rvt.

### 5.5 Tag Validation & Compliance

**Validate Tags** (CREATE → QA → Validate)

Four compliance buckets:
1. **RESOLVED** (Green): All 8 segments populated with valid codes — production-ready
2. **COMPLETE_PLACEHOLDERS** (Amber): 8 segments but contains GEN/XX/ZZ/0000
3. **INCOMPLETE** (Orange): Fewer than 8 segments
4. **UNTAGGED** (Red): No tag at all

Weighted compliance formula: `0.7 × COMPLETE_PLACEHOLDERS + 0.3 × INCOMPLETE`

### 5.6 Real-Time Auto-Tagging

**Enable**: CREATE → Setup → Auto-Tagger Toggle

When enabled, STING automatically tags elements as they are placed in the model:
- Triggers on `Element.GetChangeTypeElementAddition()` for 22 categories
- Runs full pipeline: tokens → tag → containers → TAG7
- Optional visual tag placement (annotation tags)
- Discipline filter restricts to specified codes
- Workset ownership check for worksharing safety

**Stale Detection**: Elements that move or change geometry are automatically marked with `STING_STALE_BOOL = 1` for re-tagging.

### 5.7 Smart Tag Placement

**How**: TAGS tab (Tag Studio) or CREATE → Smart Placement section

16-position placement system with collision avoidance:

```
       P13  P5  P14
    P12  P4  P1  P6  P15
         P3  ●  P2
    P16  P8  P7  P10 P9
       P11
```

Commands:
- **Smart Place Tags**: Priority-based placement with 8-position collision avoidance
- **Arrange Tags**: Auto-arrange placed tags into aligned grid
- **Batch Place Tags**: Place across multiple views
- **Learn Placement**: Analyze existing layouts to learn rules
- **Apply Template**: Apply saved placement rules
- **Tag Overlap Analysis**: Detect and report overlapping tags

### 5.8 Display Modes

5 display modes for tag text (`ASS_DISPLAY_TXT`):

| Mode | Format | Example | Use Case |
|------|--------|---------|----------|
| 1 | SEQ only | 0003 | Clean plans |
| 2 | PROD-SEQ | AHU-0003 | Standard views |
| 3 | DISC-SYS-SEQ | M-HVAC-0003 | Discipline views |
| 4 | DISC-PROD-SEQ | M-AHU-0003 | Detailed plans |
| 5 | Full 8-segment | M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 | Schedules/reports |

Set via: ORGANISE → Display Mode (or Tag Studio → Tokens tab)


---

## 6. Document Management & CDE State Machine

### 6.1 CDE Overview (ISO 19650-1 Section 12)

The Common Data Environment (CDE) is the single source of truth for all project information. STING implements the 7-state CDE lifecycle:

```
   WIP ──→ SHARED ──→ PUBLISHED ──→ ARCHIVE
    ↑         │
    └─────────┘ (Rework)
    
   SUPERSEDED    WITHDRAWN    OBSOLETE
   (Terminal)    (Terminal)   (Terminal)
```

### 6.2 Opening the Document Management Center

**How**: BIM tab → Document Management (or keyboard shortcut in Coordination Center)

The Document Management Center has 7 action tabs:

| Tab | Content | Key Actions |
|-----|---------|-------------|
| FILE/BULK | File operations, bulk CDE | Add/rename/delete files, bulk status change |
| DOCS/CDE | Document register, CDE status | Register documents, change CDE state |
| ISSUES | Issue tracker | Create/update/resolve issues, BCF export |
| REVISIONS | Revision management | Create/track/compare revisions |
| COORDINATION | Clash/review/exchange | Clashes, reviews, platform sync |
| HANDOVER | FM/O&M deliverables | COBie, handover manuals, asset reports |
| NOTES/BEP | BEP, sticky notes | Generate/update BEP, element notes |

### 6.3 CDE State Transitions

#### Valid Transitions

| From | To | Approver | Suitability | Compliance Gate |
|------|-----|----------|-------------|-----------------|
| WIP | SHARED | BIM Coordinator (C) | S3 (For Review) | ≥70% tag compliance |
| SHARED | PUBLISHED | Information Manager (I) | S4 (For Stage Approval) | ≥90% tag compliance |
| PUBLISHED | ARCHIVE | Client Rep (K) | IFR (Issued for Record) | N/A |
| SHARED | WIP | Any (Rework) | S0 (Initial Status) | N/A |

#### Compliance-Gated Transitions

STING enforces minimum compliance before allowing CDE transitions:

- **WIP → SHARED**: Requires `CDE_SHARED_MIN_COMPLIANCE` (default 70%)
- **SHARED → PUBLISHED**: Requires `CDE_PUBLISHED_MIN_COMPLIANCE` (default 90%)

If compliance is below threshold:
1. Dialog shows per-discipline breakdown
2. Stale element count
3. Suggested actions
4. Option to override with explicit acknowledgment

#### Bulk CDE Operations

Select multiple documents → right-click → **Bulk Update CDE**:
- Mixed CDE state warning for multi-select
- Terminal state blocking for ARCHIVE
- Audit trail logging (timestamp, old/new CDE, old/new suitability, username)

### 6.4 Suitability Codes (ISO 19650-2 UK NA)

| Code | Description | Typical CDE State |
|------|-------------|-------------------|
| S0 | Initial Status / WIP | WIP |
| S1 | Fit for Coordination | WIP |
| S2 | Fit for Information | SHARED |
| S3 | Fit for Review & Comment | SHARED |
| S4 | Fit for Stage Approval | SHARED/PUBLISHED |
| S5 | Fit for Costing | SHARED |
| S6 | Fit for Manufacturing | PUBLISHED |
| S7 | Fit for Construction | PUBLISHED |
| CR | As-Constructed Record | PUBLISHED/ARCHIVE |
| AB | Abandoned | Terminal |

### 6.5 ISO 19650 File Naming Convention

STING validates file names against the ISO 19650 pattern:

```
PROJECT-ORIGINATOR-VOLUME-LEVEL-TYPE-ROLE-NUMBER-SUITABILITY
Example: PROJ-ABC-ZZ-01-DR-A-00001-S3
```

Run **Sheet Naming Check** (DOCS → Automation → Sheet Naming Check) to audit all sheet numbers.

**Strict mode** (enabled via `SHEET_NAMING_STRICT_MODE`): Requires 5+ segments, validated document type code, and recognised role code.

### 6.6 Document Version Tracking

STING tracks document versions with CDE state timeline:

```
v1.0  WIP     → S0  (2025-03-01, John)
v1.1  SHARED  → S3  (2025-03-15, Jane — approved by BIM Coord)
v1.2  WIP     → S0  (2025-03-20, John — rework after review)
v2.0  SHARED  → S4  (2025-04-01, Jane)
v2.1  PUBLISHED → IFA (2025-04-15, Info Manager approved)
```

Supersession chains link old → new documents with ISO 19650 clause 12.2 compliance.

---

## 7. Issue Management & BCF Coordination

### 7.1 Issue Types

| Type | Code Pattern | Use Case | SLA (Default) |
|------|-------------|----------|---------------|
| RFI | RFI-0001 | Request for Information | 24h (HIGH) |
| NCR | NCR-0001 | Non-Conformance Report | 4h (CRITICAL) |
| SI | SI-0001 | Site Instruction | 168h (MEDIUM) |
| TQ | TQ-0001 | Technical Query | 24h (HIGH) |
| DS | DS-0001 | Design Suggestion | 336h (LOW) |
| CO | CO-0001 | Change Order | 24h (HIGH) |
| RV | RV-0001 | Review Comment | 168h (MEDIUM) |

### 7.2 Creating Issues

#### Quick Issue (From Document Management Center)

1. ISSUES tab → **Quick Issue**
2. Enter title, select priority (CRITICAL/HIGH/MEDIUM/LOW)
3. Issue auto-creates with:
   - Typed ID (e.g., RFI-0001)
   - Auto-detected revision
   - Discipline from current filter context
   - Full audit trail

#### From Warnings

1. Warnings Manager → Dashboard → **Create Issue**
2. Groups critical/high warnings by category
3. Auto-creates NCR for CRITICAL, SI for HIGH
4. Deduplicates against existing issues

#### From Element Selection

1. Select elements in model
2. BIM tab → Issues → Raise Issue
3. Auto-detects discipline from elements' DISC token
4. Auto-assigns to discipline lead from `DISCIPLINE_LEADS` config
5. Links element IDs for navigation

### 7.3 Issue Lifecycle

```
OPEN ──→ IN_PROGRESS ──→ RESOLVED ──→ CLOSED
  │                           ↑
  ├── ESCALATED ──────────────┘
  │
  └── ON_HOLD
```

SLA enforcement per priority:
- **CRITICAL**: 4 hours (configurable via `WARNING_SLA_CRITICAL_HOURS`)
- **HIGH**: 24 hours
- **MEDIUM**: 1 week (168 hours)
- **LOW**: 2 weeks (336 hours)

Overdue issues flagged with red background in Issue DataGrid.

### 7.4 BCF Export/Import

**Export**: BIM tab → Platform → BCF Export
- Generates BCF 2.1 XML with viewpoints
- Orthogonal camera data (CameraViewPoint, Direction, UpVector, ViewToWorldScale)
- Issue metadata (title, type, priority, status, assignee)

**Import**: BIM tab → Platform → BCF Import
- Deduplication detection against existing issues
- Auto-detected project revision
- Element linking from BCF component references

### 7.5 Cross-System Issue Automation

STING links issues to other systems:

| Trigger | Automation | Target |
|---------|-----------|--------|
| Critical warning created | Auto-create NCR issue | Issues |
| Issue resolved | Update linked transmittal | Transmittals |
| Overdue action item | Auto-escalate to NCR | Issues |
| Meeting creates action | Track to completion | Actions |
| Issue affects COBie | Flag in export pre-flight | COBie |

---

## 8. Revision Management & Audit Trail

### 8.1 Creating Revisions

**How**: BIM tab → Revisions → Create Revision

Features:
- ISO 19650 naming convention (REV-001, REV-002, etc.)
- Pre-revision compliance gate: warns if <80% compliant
- Shows discipline breakdown with tag/stale/untagged counts
- Tag snapshot capture for change tracking

### 8.2 Revision Tracking

**Tag Snapshot**: Each revision captures the current state of all tagged elements:
- All 8 token values
- STATUS and REV
- Workset and MEP system context
- Grid reference

**Revision Compare**: Compare two snapshots to identify:
- TOKEN_CHANGE: Source tokens modified
- CONTAINER_REGEN: Discipline containers regenerated
- NARRATIVE_CHANGE: TAG7A-F narrative changed
- STATUS_CHANGE: STATUS or REV changed
- TAG_REFORMAT: TAG1-TAG6 format change only

### 8.3 Revision Dashboard

**How**: BIM tab → Revisions → Dashboard

Shows:
- All revisions with dates and descriptions
- Revision cloud count per revision
- Change delta between revisions
- Elements modified per revision

### 8.4 Auto-Revision on Tag Change

**How**: BIM tab → Revisions → Auto on Tag Change

When enabled, STING automatically stamps elements with the current revision when their tag values change.

---

## 9. Coordination & Clash Detection

### 9.1 Clash Detection

**How**: BIM tab → Coordination → Clash Detection (or TEMP → Data Pipeline → Clash Detection)

Types:
- **Intra-model**: MEP vs Structure within current model
- **Cross-model**: Include linked Revit models with transform-aware intersection
- **Category-specific**: Filter to specific category pairs

Results exported to CSV with element IDs, locations, and clash severity.

### 9.2 Cross-Model Compliance

**How**: BIM tab → Federated Compliance

Scans all linked Revit models:
- Opens each linked document
- Runs ComplianceScan per model
- Aggregates into federated compliance percentage
- Per-link RAG status display

### 9.3 Model Health Dashboard

**How**: BIM tab → Model Health → Dashboard (or Coordination Center → MODEL HEALTH tab)

4-category weighted scoring (0-100):
1. **Warnings** (25 pts): From WarningsEngine health score
2. **Compliance** (25 pts): From ComplianceScan
3. **Data Quality** (25 pts): Container/TAG7/STATUS completeness
4. **Performance** (25 pts): Element count, groups, links

Actionable recommendations with inline "Fix" buttons.

---

## 10. Compliance & Quality Assurance

### 10.1 Compliance Scan (Real-Time)

The status bar at the bottom of the dockable panel shows live compliance:

```
🟢 92% tagged | 85% containers | 3 stale | Rev P02
```

- Updates every 30 seconds (cached)
- Per-discipline breakdown available on click
- RAG thresholds: Red <50%, Amber 50-80%, Green >80%

### 10.2 Compliance Gates

Compliance gates block operations when quality is insufficient:

| Gate | Threshold | Operation Blocked |
|------|-----------|-------------------|
| Post-tagging gate | `COMPLIANCE_GATE_PCT` (default 80%) | Shows warning after any tagging command |
| CDE SHARED gate | `CDE_SHARED_MIN_COMPLIANCE` (70%) | WIP → SHARED transition |
| CDE PUBLISHED gate | `CDE_PUBLISHED_MIN_COMPLIANCE` (90%) | SHARED → PUBLISHED transition |
| COBie export gate | 60% minimum | COBie export blocked with breakdown |
| Pre-revision gate | 80% recommended | Warning before revision creation |

### 10.3 Data Drop Readiness

**How**: BIM tab → Data Drop Readiness

Assesses model against ISO 19650 data drop milestones:

| Milestone | Compliance | COBie Sheets | Room/Type Presence |
|-----------|-----------|--------------|-------------------|
| DD1 (Brief) | ≥30% | Contact, Facility | Not required |
| DD2 (Concept) | ≥60% | + Floor, Space, Type | Room names required |
| DD3 (Design) | ≥85% | + Component, System | Full type data |
| DD4 (Handover) | ≥95% | All 19 worksheets | Complete asset data |

Auto-detects target DD from current compliance level.

### 10.4 45-Check Template Validation

**How**: TEMP → Data Pipeline → Validate Template

Checks include:
- Data file inventory and SHA-256 integrity
- Parameter consistency (registry vs CSV vs bound)
- Material completeness (BLE + MEP)
- Formula dependency verification (cycle detection)
- Schedule definition cross-reference
- Binding coverage matrix validation

Results shown in rich panel with pass/fail checklist and CSV export.


---

## 11. Warnings Management

### 11.1 Overview

STING's Warnings Manager goes beyond basic Revit warnings with:
- **87+ classification rules** mapping warnings to 8 BIM-domain categories
- **5-tier severity** (Critical/High/Medium/Low/Info)
- **10 auto-fix strategies** for safe batch resolution
- **SLA tracking** with per-severity time limits
- **Deliverable impact analysis** (COBie/IFC/FM Handover/Schedules/Clash)
- **Baseline trend tracking** with delta comparison

### 11.2 Warning Categories

| Category | Description | Examples |
|----------|-------------|---------|
| **Geometric** | Shape/position issues | Zero-length walls, self-intersecting sketches |
| **Spatial** | Room/space issues | Room not enclosed, room tag outside boundary |
| **MEP** | Mechanical/electrical/plumbing | Unconnected connectors, flow direction |
| **Structural** | Load/support issues | Deflection exceeded, bearing stress |
| **Annotation** | Tag/dimension issues | Duplicate tags, dimension obscured |
| **Data** | Parameter/binding issues | Missing parameters, duplicate marks |
| **Performance** | Model optimization | In-place families, excessive detail groups |
| **Compliance** | Code/standard violations | Fire rating, Part L thermal, Part M access |

### 11.3 Commands

| Command | Location | What It Does |
|---------|----------|--------------|
| **Warnings Dashboard** | BIM → Warnings | Full report: severity/category/discipline/level breakdown |
| **Warnings Auto-Fix** | BIM → Warnings | Batch-fix safe categories with dry-run preview |
| **Warnings Export** | BIM → Warnings | CSV export for external tracking (10 columns) |
| **Warnings Baseline** | BIM → Warnings | Save/compare baseline for trend tracking |
| **Warnings Select** | BIM → Warnings | Pick warning type → select affected elements |
| **Warnings Suppress** | BIM → Warnings | Hide warning patterns from dashboard |
| **Warnings Compliance** | BIM → Warnings | ISO 19650/CIBSE/BS 7671 compliance mapping |
| **Warnings Monitor** | BIM → Warnings | Pre/post-command regression detection |

### 11.4 Auto-Fix Strategies

| # | Strategy | What It Fixes | Safety |
|---|----------|---------------|--------|
| 1 | Delete shorter of 2 overlapping elements | Duplicate instances | Safe |
| 2 | Delete shorter room separation line | Room separation overlaps | Safe |
| 3 | Move tag to room centroid | Room tag outside boundary | Safe |
| 4 | Increment duplicate marks with collision-safe suffix | Duplicate marks | Safe |
| 5 | Unjoin non-intersecting geometry | Unjoined walls | Medium |
| 6 | Auto-join overlapping walls | Wall geometry overlap | Medium |
| 7 | Move room tag to room center | Room tag misplacement | Safe |
| 8 | Snap to nearest cardinal direction | Off-axis elements | Medium |
| 9 | Delete zero-length elements (<3mm) | Zero-length walls/pipes | Safe |
| 10 | Fix duplicate marks (full-model scan) | Duplicate parameter values | Safe |

**Running Auto-Fix**:
1. BIM → Warnings → Auto-Fix
2. Preview shows: category, count, fix strategy for each warning type
3. Single transaction — all-or-nothing
4. Post-fix verification: re-scans to confirm fixes resolved issues
5. Reports net warning reduction and any NEW warnings introduced

### 11.5 SLA Tracking

Default SLA thresholds (configurable in `project_config.json`):

| Severity | Default SLA | Healthcare | Data Centre |
|----------|-------------|------------|-------------|
| CRITICAL | 4 hours | 1 hour | 0.5 hours |
| HIGH | 24 hours | 4 hours | 2 hours |
| MEDIUM | 168 hours (1 week) | 72 hours | 48 hours |
| LOW | 336 hours (2 weeks) | 168 hours | 168 hours |

Morning briefing shows overdue SLA violations. Issue tracker enforces SLA per priority.

### 11.6 Warning Health Score

Weighted 0-100 score:
- Base: 100 points
- CRITICAL warning: -20 points each
- HIGH warning: -5 points each
- MEDIUM warning: -2 points each
- LOW warning: -1 point each

Additional factors: age of warnings, element concentration, auto-fixability ratio, category distribution.

### 11.7 Deliverable Impact Analysis

Maps classified warnings to 5 BIM deliverable areas:

| Deliverable | Warning Categories | Impact |
|-------------|-------------------|--------|
| COBie | Data, Spatial | Missing data in COBie export |
| IFC | Geometric, Data | Invalid geometry in IFC model |
| FM Handover | Spatial, MEP, Compliance | Incomplete handover data |
| Schedules | Data, Annotation | Incorrect schedule values |
| Clash Detection | Geometric, MEP, Structural | Undetected clashes |

Enables BIM coordinators to prioritise warning resolution by deliverable deadline.

---

## 12. Workflow Automation Presets

### 12.1 Built-In Workflows

STING includes 30+ built-in workflow presets. Each executes a sequence of commands with conditional logic:

#### Core Workflows

| Preset | Steps | Use Case | Typical Duration |
|--------|-------|----------|-----------------|
| **ProjectKickoff** | 26 | First-time project setup | 5-10 min |
| **DailyQA** | 11 | Daily quality check | 2-3 min |
| **DocumentPackage** | 6 | Export document package | 1-2 min |
| **PostTaggingQA** | 5 | Validate after tagging | 1-2 min |
| **MorningHealthCheck** | 8 | Morning coordinator routine | 3-5 min |
| **HandoverReadiness** | 9 | Pre-handover validation | 5-8 min |
| **WeeklyDataDrop** | 8 | ISO 19650 data exchange | 3-5 min |
| **EndOfDaySync** | 8 | End-of-day cleanup | 2-4 min |
| **ModelAuditDeep** | 8 | Deep model validation | 5-10 min |

#### Coordination Workflows

| Preset | Steps | Use Case |
|--------|-------|----------|
| **MEPCoordination** | 6 | MEP clash resolution cycle |
| **CDE_Submission** | 8 | ISO 19650 CDE package |
| **DesignReviewPrep** | 5 | Pre-review model cleanup |
| **FederatedModelAudit** | 7 | Multi-model compliance |
| **PreMeetingPrep** | 7 | Pre-meeting model state |
| **ClientReviewPrep** | 7 | Client presentation prep |
| **RegulatoryScan** | 5 | Code compliance audit |
| **IssueResolution** | 6 | Issue triage→fix→validate |

#### Sector-Specific Workflows

| Preset | Sector | Special Steps |
|--------|--------|---------------|
| **Healthcare_NHS** | Healthcare | HTM compliance, medical gas, infection zones |
| **DataCentre** | Data Centre | Power distribution, cooling, Uptime Institute |
| **CommercialOffice** | Commercial | BCO Guide, BREEAM, lease demise |
| **Residential** | Residential | Part L/M/B, plot numbering, sales schedules |
| **Education** | Education | BB103 area, DfE output spec, safeguarding |

### 12.2 Workflow Conditions

Each workflow step can have conditions that determine execution:

| Condition | What It Checks | Example |
|-----------|---------------|---------|
| `MinCompliancePct` | Skip if compliance ≥ threshold | Skip validate if >95% |
| `MaxCompliancePct` | Skip if compliance < threshold | Skip batch tag if <10% |
| `RequiresStaleElements` | Skip if no stale elements | Skip retag if none stale |
| `RequiresWorksharedModel` | Skip in non-workshared | Skip workset operations |
| `MinElementCount` | Skip on small models | Skip batch on <100 elements |
| `MaxElementCount` | Skip on large models | Skip full scan on >100K |
| `has_warnings` | Skip if zero warnings | Skip auto-fix |
| `has_critical_warnings` | Skip if no critical | Skip escalation |
| `has_open_issues` | Skip if no issues | Skip resolution |
| `has_links` | Skip if no linked models | Skip federated scan |
| `has_cad_imports` | Skip if no CAD | Skip DWG conversion |
| `has_stale` | Skip if no stale elements | Skip retag |
| `has_untagged` | Skip if all tagged | Skip batch tag |
| `has_placeholders` | Skip if no GEN/XX/ZZ | Skip placeholder resolution |
| `compliance_above_90` | Skip if >90% | Skip major tagging |
| `compliance_below_50` | Skip if <50% | Skip advanced operations |
| `MinDataDrop` | Skip below DD level | Gate by ISO 19650 milestone |
| `MinWarningHealthScore` | Skip above health threshold | Skip auto-fix when healthy |
| `TimeoutSeconds` | Cancel step after N seconds | Guard against hanging |

### 12.3 Compound Conditions

Steps can have multiple conditions with AND/OR logic:

```json
{
    "CommandTag": "BatchTag",
    "Label": "Tag remaining elements",
    "Conditions": ["has_untagged", "compliance_below_50"],
    "ConditionLogic": "AND"
}
```

### 12.4 Custom Workflow Creation

Create custom workflows via JSON files in `data/WORKFLOW_*.json`:

```json
{
    "Name": "My Custom Workflow",
    "Description": "Custom weekly QA check",
    "Steps": [
        {
            "CommandTag": "RetagStale",
            "Label": "Fix stale elements",
            "RequiresStaleElements": true
        },
        {
            "CommandTag": "ValidateTags",
            "Label": "Validate all tags",
            "MinCompliancePct": 50
        },
        {
            "CommandTag": "WarningsAutoFix",
            "Label": "Auto-fix warnings",
            "Conditions": ["has_warnings"],
            "FallbackStep": "WarningsDashboard"
        }
    ],
    "RollbackOnOptionalFailure": false
}
```

### 12.5 Running Workflows

**From Coordination Center**: WORKFLOWS tab → Quick Workflow buttons  
**From BIM tab**: Workflows section → Preset buttons  
**Repeat last**: "Repeat Last Workflow" button remembers last executed preset

Progress shown via `StingProgressDialog` with ETA and cancel support.

---

## 13. Data Exchange (Excel/COBie/IFC/BCF)

### 13.1 Excel Link

#### Export to Excel

**How**: BIM tab → Excel → Export

30+ columns exported:
- Tag tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)
- Identity (Family, Type, Mark, Category)
- Spatial (Level, Room, Grid Reference)
- MEP data (Flow, Pressure, Voltage, Circuit)
- Dimensions (Width, Height, Length, Area)
- Status (STATUS, REV, Stale flag)

#### Import from Excel

**How**: BIM tab → Excel → Import

Validation pipeline:
1. Individual token validation against ISO 19650 code lists
2. Cross-token validation (FUNC must match SYS, DISC must match SYS)
3. CLEAR sentinel support (type "CLEAR" to empty a field)
4. Change preview with approve/reject
5. Audit trail capture (ASS_TAG_PREV_TXT + timestamp)

#### Excel Round-Trip

**How**: BIM tab → Excel → Round-Trip

One-click: Export → (user edits) → Import with full validation.

### 13.2 COBie V2.4 Export

**How**: BIM tab → COBie Export

22 project-type presets:
- Commercial Office, Healthcare NHS, Healthcare Private
- Education School, Education University
- Residential Standard, Residential High-Rise
- Retail, Hotel, Data Centre, Industrial
- Transport Station, Transport Airport
- Defence MOD, Heritage, Mixed-Use
- Laboratory, Sports/Leisure, Cultural
- Modular/Off-Site, Infrastructure Civil/Water, Fit-Out

19 worksheets generated:
- Instruction, Contact, Facility, Floor, Space, Zone
- Type (with Warranty, Nominal dimensions, Material, Features)
- Component (with SerialNumber, InstallationDate, BarCode)
- System, Assembly, Connection, Spare, Resource
- Job, Impact, Document, Attribute, Coordinate, Issue

**Pre-export checks**:
- Container staleness sampling
- 60% minimum compliance gate
- Offers inline WriteContainers if containers stale

### 13.3 COBie Round-Trip Import

**How**: BIM tab → COBie Import

Reads COBie V2.4 Component worksheet:
- Matches by UniqueId (exact) then TAG1 (fallback)
- Updates 8 parameters: Description, SerialNumber, BarCode, etc.
- 10K row safety limit
- CLEAR sentinel support

### 13.4 IFC Export

**How**: TEMP → Data Pipeline → IFC Export

Features:
- IFC property set mapping from STING parameters
- ISO 16739 property validation
- Configurable export options

### 13.5 BCF 2.1 Exchange

**Export**: BIM tab → Platform → BCF Export  
**Import**: BIM tab → Platform → BCF Import  

BCF viewpoints include orthogonal camera data for accurate view recreation in external tools.

---

## 14. Handover & FM Data

### 14.1 COBie Handover Export

**How**: BIM tab → Handover → COBie Export (or DOCS → Handover section)

Comprehensive COBie V2.4 with 19 worksheets, tag integration, and project-type presets.

### 14.2 FM Handover Manual

**How**: DOCS → Handover → FM Handover Manual

Generates:
- Asset register (all tagged elements with full token data)
- Spatial summary (rooms by department, floor areas)
- System descriptions (MEP systems with CIBSE codes)
- Compliance report (ISO 19650 status)

### 14.3 Maintenance Schedule

**How**: DOCS → Handover → Maintenance Schedule

Based on PPM (Planned Preventive Maintenance) per ASTM E2018:
- Equipment maintenance intervals
- Asset condition grades (1-5)
- Priority-based scheduling

### 14.4 O&M Manual

**How**: DOCS → Handover → O&M Manual

Operational and Maintenance manual covering:
- Equipment specifications
- Maintenance procedures
- Warranty information
- Emergency procedures

### 14.5 Asset Health Report

**How**: DOCS → Handover → Asset Health

0-100 scoring per asset based on:
- Age and condition grade
- Maintenance history
- Compliance status
- Performance data

---

## 15. BEP & Governance

### 15.1 BEP Generation

**How**: BIM tab → BEP → Generate BEP (or Notes/BEP tab in Document Management Center)

22 project-type presets with template-driven BEP generation covering:
- Project information and scope
- Roles and responsibilities
- Software and hardware
- Information delivery milestones
- CDE strategy
- Data drops schedule (DD1-DD4)
- COBie requirements
- Asset management strategy
- Risk register (10 BIM-specific risks)
- Training and competency plan
- Golden Thread compliance (Building Safety Act 2022)
- CAFM integration

### 15.2 BEP Auto-Enrichment

When generating BEP, STING auto-enriches with live model data:
- Current compliance percentages per discipline
- Per-stage tag completeness targets
- Risk register entries based on compliance gaps
- Training recommendations based on team competency gaps

### 15.3 BEP Update

**How**: BIM tab → BEP → Update BEP

Updates an existing BEP with:
- Current compliance status
- Progress against milestones
- Issue summary
- Revision history

---

## 16. Reporting & Dashboards

### 16.1 Report Types

| Report | Command | Output |
|--------|---------|--------|
| Tag Register | ORGANISE → Analysis → Tag Register | CSV (40+ columns) |
| Sheet Register | DOCS → Templates → Sheet Register | CSV with compliance |
| Compliance Dashboard | CREATE → QA → Dashboard | On-screen RAG display |
| Validation Report | CREATE → QA → Validate | Rich panel + CSV |
| Warning Report | BIM → Warnings → Export | CSV (10 columns) |
| Warning Baseline | BIM → Warnings → Baseline | JSON sidecar |
| Model Health | BIM → Model Health → Dashboard | On-screen + metrics |
| Weekly Report | BIM → Weekly Report | Self-contained HTML |
| Drawing Register | DOCS → Automation → Drawing Register | Schedule in Revit |
| COBie Export | BIM → COBie Export | XLSX (19 worksheets) |
| BOQ Export | TEMP → Data Pipeline → BOQ | XLSX (6-column) |

### 16.2 Compliance Trend

7-day rolling compliance trend tracked in `.sting_compliance_trend.json`:
- Daily snapshots: compliance %, elements, tagged, stale, warnings, placeholders
- 90-day rolling window
- Trend direction: improving/stable/declining with delta %

---

## 17. International Standards Reference

### 17.1 Standards Implemented

| Standard | Coverage | STING Commands |
|----------|----------|----------------|
| **ISO 19650-1/2/3** | Information management framework | All BIM management commands |
| **BS EN ISO 19650** | UK National Annex | CDE state machine, suitability codes |
| **PAS 1192-2** (superseded) | Legacy BIM execution plan | BEP generation |
| **PAS 1192-3** (superseded) | Legacy asset management | COBie export |
| **BS 1192** | Collaborative working | File naming, CDE structure |
| **Uniclass 2015** | Classification system | SYS/FUNC code validation |
| **CIBSE TM40** | System classification | MEP system codes |
| **CIBSE Guide C** | Velocity limits | MEP validation |
| **BS 7671** | Electrical installations | Circuit protection checks |
| **BS 8300** | Accessibility | Space dimension checks |
| **Building Regs Part B** | Fire safety | Fire rating validation |
| **Building Regs Part L** | Energy conservation | U-value tracking |
| **Building Regs Part M** | Access | Stair/ramp dimensions |
| **CDM 2015** | Construction safety | Health & safety roles |
| **BS 5395** | Stairs | Tread/rise dimensions |
| **BS 6180** | Barriers | Railing height/spacing |
| **BS EN 12056** | Drainage | Gradient/vent checks |
| **BS EN 1992/1993/1997** | Eurocodes (RC/Steel/Geotech) | Structural analysis |
| **Building Safety Act 2022** | Golden Thread | BEP governance |

### 17.2 Custom Code Lists

Add project-specific codes to validation via `project_config.json`:

```json
{
    "CUSTOM_VALID_DISC": ["FP", "LV", "MR"],
    "CUSTOM_VALID_SYS": ["CCTV", "SOLAR", "EV"],
    "CUSTOM_VALID_FUNC": ["MON", "CHG"],
    "CUSTOM_VALID_LOC": ["SITE", "CAR_PARK"],
    "CUSTOM_VALID_ZONE": ["Z05", "Z06", "PLANT"]
}
```

These are merged with built-in ISO 19650 code lists.


---

## 18. BIM Coordination Center (Unified Dashboard)

### 18.1 Overview

The **BIM Coordination Center** is the unified corporate-style dashboard that consolidates 6 separate management views into a single tabbed interface.

**How to open**: BIM tab → Coordination Center

### 18.2 Tabs

| Tab | Content | Key Features |
|-----|---------|-------------|
| **OVERVIEW** | KPI cards, discipline table, action items | 5 KPI cards, compliance forecast, SLA violations |
| **MODEL HEALTH** | Health checks, recommendations | 4-factor scoring, actionable fix buttons |
| **WARNINGS** | TreeView, auto-fix, SLA | Severity-colored nodes, 3D zoom, root-cause groups |
| **ISSUES** | DataGrid, lifecycle | Color-coded rows, context menus, SLA tracking |
| **REVISIONS** | DataGrid, compare | Snapshot tracking, change delta |
| **PLATFORM** | 7 cloud platforms | ACC, BIM 360, Procore, Aconex, Trimble, Bentley, SharePoint |
| **WORKFLOWS** | Quick launch, history | 6 preset buttons, execution DataGrid |
| **QA DASHBOARD** | Token coverage, validation | Token matrix, anomaly detection |
| **4D/5D** | Cost/schedule | KPIs, milestone progress, cost breakdown |
| **ISSUES** (detailed) | Full issue management | Create, update, zoom, BCF export |
| **REVISIONS** (detailed) | Full revision management | Create, compare, export |
| **MEETINGS** | Meeting coordination | Agenda, actions, templates, history |
| **PERMISSIONS** | ISO 19650 roles | Role definitions, CDE permissions, transitions |

### 18.3 Interactive Features

- **Double-click** discipline rows to select all elements of that discipline
- **Right-click** issue/warning rows for context menu (Zoom to 3D, Select, Update)
- **Hover** KPI cards for drill-down tooltips
- **F5** to refresh, **Ctrl+E** to export, **Escape** to close
- **Auto-refresh** every 30 seconds

### 18.4 Action Required Panel

Priority-sorted clickable action items:
- Stale elements → Runs RetagStale
- Overdue issues → Opens Issue tracker
- Critical warnings → Runs WarningsAutoFix
- Untagged elements → Runs BatchTag
- Placeholder tokens → Runs ResolveAllIssues
- SLA violations → Opens Issue dashboard

### 18.5 3D Section Box Zoom

Double-clicking warnings, issues, or hotspot elements creates a `STING - Section Box Zoom` 3D view with 3ft padding around affected elements. Enables rapid visual inspection without manual navigation.

---

## 19. Meeting Management & Action Tracking

### 19.1 Meeting Types

| Type | Use Case | Typical Frequency |
|------|----------|-------------------|
| BIM Coordination | Multi-discipline coordination | Weekly |
| Design Review | Design team progress review | Bi-weekly |
| Client Review | Client presentation/approval | Monthly |
| Handover | FM team data review | Per milestone |
| Clash Resolution | Clash-specific resolution | As needed |

### 19.2 Meeting Workflow

```
PREPARE ──→ DURING ──→ REVIEW
```

#### Prepare Phase
- **New Meeting**: Create with type, date, attendees
- **Auto Agenda**: Generate from open issues, pending transmittals, revisions, compliance
- **Meeting Templates**: Reusable agenda templates

#### During Phase
- **Log Minutes**: Multi-line text editor
- **Add Action Item**: Description, assignee, due date
- **Quick Issue**: Create RFI/NCR directly from meeting context
- **Take Snapshot**: Capture model compliance state

#### Review Phase
- **Meeting History**: StingResultPanel with per-meeting sections
- **Open Actions**: Grouped by overdue/upcoming
- **Export Minutes**: Timestamped .txt file
- **Send Reminder**: Notification for upcoming meetings

### 19.3 Action Item Lifecycle

```
OPEN ──→ IN_PROGRESS ──→ COMPLETED
  │
  └── ESCALATED (auto-creates NCR issue when overdue)
```

Overdue actions automatically escalated to NCR issues via cross-system automation.

### 19.4 Cross-System Automation Rules

6 automation rules link meetings to other systems:

| Rule | Trigger | Action |
|------|---------|--------|
| Overdue Action → Issue | Action overdue | Auto-create HIGH NCR |
| Open Issues → Agenda | Next meeting prep | Auto-populate agenda items |
| Compliance Gate → Transmittal | Compliance ≥80% + Containers ≥80% | Auto-create SHARED transmittal |
| Meeting Closure → Follow-Up | Meeting ends with open actions | Auto-schedule follow-up |
| SLA Violation → Escalation | SLA exceeded | Auto-escalate priority |
| Stale Elements → Retag | Elements moved | Auto-retag stale elements |

---

## 20. 4D/5D Scheduling & Cost Management

### 20.1 4D Scheduling

**How**: BIM tab → 4D/5D section

Commands:
- **Auto Schedule 4D**: Auto-create 4D schedule from model data
- **Import MS Project**: Import schedule from MS Project
- **View Timeline**: Visualize construction sequence
- **Export Schedule**: Export to CSV/MS Project format

40-trade construction sequence covering all UK construction phases.

### 20.2 5D Cost Management

Commands:
- **Auto Cost 5D**: Calculate costs from model quantities
- **Import Cost Rates**: Load rates from `cost_rates_5d.csv`
- **Cost Report**: Generate cost breakdown
- **Cash Flow**: Cash flow forecasting
- **Element Cost Trace**: Per-element cost tracking

Cost percentages configurable in `project_config.json`:
- `COST_PRELIMINARIES_PCT` (default 10%)
- `COST_CONTINGENCY_PCT` (default 5%)
- `COST_OVERHEAD_PROFIT_PCT` (default 15%)

### 20.3 Earned Value Analysis

KPI metrics:
- Total estimated cost
- Earned value (% complete × budget)
- Milestone completion rate
- Cost breakdown by phase

---

## 21. Troubleshooting & FAQ

### Common Issues

| Problem | Cause | Solution |
|---------|-------|----------|
| Tags show XX/ZZ/GEN | Missing spatial data | Place rooms, set Project Info |
| SEQ numbers restart | Sidecar file missing | Run `AssignNumbers` to rebuild |
| Containers empty after tag | Container parameters not bound | Run `Load Params` |
| Compliance shows 0% | Parameters not bound | Run `Master Setup` |
| Auto-tagger not working | Disabled by default | CREATE → Setup → Auto-Tagger Toggle |
| Warnings not classified | Unknown warning pattern | Check `WarningsManager.cs` classification rules |
| COBie export incomplete | Low compliance | Run `ResolveAllIssues` first |
| Excel import rejected | Invalid token codes | Check ISO 19650 code lists |
| CDE transition blocked | Below compliance gate | Improve compliance or override |
| Morning briefing not showing | No alerts | Model is healthy — no action needed |

### Performance Tips

- Use **Tag New Only** instead of **Batch Tag** for incremental work
- Enable **PERF_TRACKING_ENABLED** to identify bottlenecks
- Use **view-scoped** tagging for large models (>50K elements)
- Run workflows during off-peak hours for large models
- Use **ComplianceScan incremental mode** (auto-detected)

---

## 22. Command Quick Reference

### By Workflow Phase

#### Setup (TEMP tab)
| Command | Action |
|---------|--------|
| Master Setup | One-click full project setup |
| Project Setup Wizard | 7-page guided setup |
| Load Parameters | Bind shared parameters |
| Check Data Files | Verify data file integrity |
| Create Materials | BLE (815) + MEP (464) |
| Batch Create Schedules | 168 schedule definitions with column aliases |
| Create Filters/Worksets/Templates | Standard project templates |

#### Tagging (CREATE tab)
| Command | Action |
|---------|--------|
| Auto Tag | Tag active view |
| Batch Tag | Tag entire project |
| Tag & Combine | Full pipeline + all containers |
| Tag New Only | Incremental tagging |
| Pre-Tag Audit | Dry-run prediction |
| Validate Tags | 4-bucket compliance check |
| Resolve All Issues | One-click fix |
| Family-Stage Populate | Pre-populate all tokens |

#### Coordination (BIM tab)
| Command | Action |
|---------|--------|
| Coordination Center | Unified dashboard |
| Document Management | CDE state machine |
| COBie Export | 19-worksheet export |
| Excel Round-Trip | Bidirectional data exchange |
| BCF Export/Import | Issue exchange |
| Warnings Dashboard | Warning analysis |
| Model Health | 4-factor health score |
| Weekly Report | HTML coordinator report |

#### Documents (DOCS tab)
| Command | Action |
|---------|--------|
| Sheet Manager | Dual-panel sheet management |
| Auto Layout | Shelf-packing viewport arrangement |
| Batch Print | PDF export (all/discipline/selection) |
| Sheet Compliance | ISO 19650 audit (10 rules) |
| Sheet Register | CSV export with compliance |
| FM Handover Manual | Full FM documentation |

---

## 23. Deep Insights: Understanding the BIM Coordination Engine

This section provides deep technical knowledge for BIM Managers who want to understand, troubleshoot, or teach others about the internal systems.

### 23.1 The Compliance Engine Architecture

The compliance system operates in real-time with a multi-layer architecture:

```
Layer 1: ComplianceScan (30-second cache)
  ├── Project-level scan: tagged/untagged/complete/incomplete counts
  ├── Per-discipline breakdown: ByDisc dictionary
  ├── Per-phase breakdown: ByPhase dictionary
  ├── Container completeness: ContainerCompletePct
  ├── Placeholder tracking: PlaceholderCount (GEN/XX/ZZ/0000)
  └── Revision tracking: RevisionComplete/Missing/Percent

Layer 2: ComplianceTrendTracker (daily snapshots)
  ├── 90-day rolling window in .sting_compliance_trend.json
  ├── 7-day trend direction (improving/stable/declining)
  └── Snapshot after every workflow execution

Layer 3: Compliance Gates (5 checkpoints)
  ├── Post-tagging gate: COMPLIANCE_GATE_PCT in project_config.json
  ├── CDE transition gate: 70% for SHARED, 90% for PUBLISHED
  ├── COBie export gate: 60% minimum with override
  ├── Pre-revision gate: 80% minimum with discipline breakdown
  └── Data drop gate: DD1=30%, DD2=60%, DD3=85%, DD4=95%
```

**Key insight**: The compliance system uses TWO metrics:
- **CompliancePercent** = tagged elements / total elements (includes placeholders)
- **StrictPercent** = fully resolved elements / total elements (excludes placeholders)

Always check StrictPercent for deliverable readiness; CompliancePercent for progress tracking.

### 23.2 The CDE State Machine

ISO 19650-2 defines a strict document lifecycle. STING enforces one-way transitions:

```
    WIP ──→ SHARED ──→ PUBLISHED ──→ ARCHIVE
     ↑         │
     └─────────┘  (rework path: SHARED → WIP only)

    SUPERSEDED ←── (from PUBLISHED when replaced)
    WITHDRAWN  ←── (from any state except ARCHIVE)
    OBSOLETE   ←── (from ARCHIVE only)
```

**Suitability codes auto-mapped per transition**:
- WIP → SHARED: Suitability = S3 (Suitable for review and comment)
- SHARED → PUBLISHED: Suitability = S4 (Suitable for stage approval)
- PUBLISHED → ARCHIVE: Suitability = S7 (Suitable for as-built/FM)

**Compliance-gated transitions** (configurable via project_config.json):
- WIP → SHARED requires `CDE_SHARED_MIN_COMPLIANCE` (default 70%)
- SHARED → PUBLISHED requires `CDE_PUBLISHED_MIN_COMPLIANCE` (default 90%)

### 23.3 Cross-System Data Flow

The BIM coordination engine connects 8 systems. Understanding the data flow is critical:

```
Tagging ──→ Compliance ──→ CDE State Machine
   │              │                 │
   ├──→ COBie Export          ├──→ Transmittals
   │              │                 │
   ├──→ Excel Link            ├──→ BCF/Issues
   │                                │
   └──→ Revision Tracking ←────────┘
              │
              └──→ Warnings Manager ←── Model Changes
```

**Automation rules (cross-system triggers)**:
1. **Stale → Auto-retag**: Geometry changes trigger StaleMarker IUpdater → elements queued for re-tagging
2. **Tag change → Revision**: AutoRevisionOnTagChange creates revision snapshots
3. **Compliance GREEN → Auto-close issues**: Open compliance issues auto-resolved
4. **Warning CRITICAL → Auto-create NCR**: Critical warnings become issues with SLA tracking
5. **Overdue action → Issue escalation**: Meeting actions past due become HIGH-priority NCRs
6. **Compliance ≥80% → Transmittal trigger**: Auto-creates SHARED transmittal for CDE submission

### 23.4 Workflow Engine Deep Dive

The WorkflowEngine supports 30+ presets with intelligent step execution:

**Condition types** (19 available):
| Condition | Description | Example Use |
|-----------|-------------|-------------|
| `minCompliancePct` | Skip if compliance below threshold | Skip COBie if <60% |
| `maxCompliancePct` | Skip if compliance above threshold | Skip tagging if >95% |
| `requiresStaleElements` | Skip if no stale elements | Skip Retag if nothing moved |
| `has_links` | Skip if no linked models | Skip federated audit |
| `has_warnings` | Skip if no warnings | Skip auto-fix |
| `has_open_issues` | Skip if no open issues | Skip issue review |
| `has_placeholders` | Skip if no GEN/XX/ZZ tokens | Skip placeholder resolution |
| `compliance_above_90` | Skip if already compliant | Skip validation |
| `MinWarningHealthScore` | Skip if warnings healthy | Skip warning fix |
| `MinDataDrop` | Skip if below DD level | Skip handover prep |
| `TimeoutSeconds` | Abort step after N seconds | Prevent long-running steps |

**Recommended daily workflow**:
```
Morning: Run "MorningHealthCheck" preset (8 steps, ~3 min)
  → Retag stale → Auto-fix warnings → Tag new → Validate → Template audit

Mid-day: Run "DailyQA" preset (11 steps, ~5 min)
  → Pre-tag audit → Batch tag → Validate → Completeness → Sheet naming

End of day: Run "EndOfDaySync" preset (8 steps, ~4 min)
  → Validate → Save baseline → Export registers → Model health → Create revision
```

### 23.5 Warning Classification System

The Warnings Manager classifies 150+ Revit warning types into 8 BIM categories:

| Category | Examples | Auto-Fix Available |
|----------|---------|-------------------|
| **Geometric** | Overlapping walls, zero-length, coincident | Yes (join, delete, snap) |
| **Spatial** | Room separation overlaps, no room | Yes (delete shorter line) |
| **MEP** | Unconnected pipes, undefined system | No (requires design review) |
| **Structural** | Beam deflection, eccentricity | No (requires engineering) |
| **Annotation** | Duplicate marks, orphan tags | Yes (auto-increment mark) |
| **Data** | Missing parameters, host deleted | Partial (parameter write) |
| **Performance** | Large groups, many linked models | No (requires model cleanup) |
| **Compliance** | Part B/L/M violations | No (requires design change) |

**10 auto-fix strategies**:
1. Delete duplicate instances (same location)
2. Delete shorter room separation line
3. Repair unjoined walls
4. Auto-increment duplicate marks (collision-safe)
5. Fix room tags outside boundary (move to center)
6. Join overlapping walls
7. Snap off-axis elements to cardinal direction
8. Delete zero-length elements (<3mm)
9. Fix duplicate marks with unique suffix
10. Unjoin non-intersecting geometry

### 23.6 Performance Guide for Large Models

| Model Size | Recommended Approach | Estimated Time |
|-----------|---------------------|---------------|
| <5K elements | BatchTag + TagAndCombine | <30 sec |
| 5K-20K elements | AutoTag per view + CombineAll | 2-5 min |
| 20K-50K elements | TagNewOnly + chunked workflows | 5-15 min |
| 50K+ elements | Discipline-filtered auto-tagger | Continuous |

**Critical performance tips**:
- Always use `TagNewOnly` for incremental work (skips already-tagged elements)
- Enable auto-tagger with discipline filter for real-time tagging
- ComplianceScan has 8-second timeout on very large models — partial results shown
- Formula cache persists for 5 minutes — first tag command loads CSV, subsequent reuse
- Avoid running BatchTag on >50K elements in single transaction — use workflow presets

### 23.7 Teaching Checklist

When training new BIM coordinators on STING Tools, cover these topics in order:

**Week 1: Fundamentals**
- [ ] Load shared parameters (Master Setup or Load Params)
- [ ] Understand 8-segment tag format (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)
- [ ] Run AutoTag on a single view
- [ ] Read the compliance dashboard (RAG status)
- [ ] Use TagAndCombine for one-click tagging

**Week 2: Quality Assurance**
- [ ] Run ValidateTags and understand 4-bucket report
- [ ] Use PreTagAudit before large tagging operations
- [ ] Fix anomalies with AnomalyAutoFix
- [ ] Export Tag Register for external review
- [ ] Understand compliance gates and when they fire

**Week 3: Document Management**
- [ ] Open Document Management Center
- [ ] Understand CDE state transitions (WIP → SHARED → PUBLISHED)
- [ ] Create and track issues (RFI/NCR/SI)
- [ ] Use revision management (snapshot → compare)
- [ ] Generate COBie export with appropriate preset

**Week 4: Automation**
- [ ] Run MorningHealthCheck workflow
- [ ] Configure project_config.json for project-specific settings
- [ ] Use BIM Coordination Center for daily monitoring
- [ ] Set up auto-tagger with discipline filter
- [ ] Create custom workflow JSON preset

## 24. Deep Workflow Insights

### 24.1 Cross-System Data Flow

The STING platform connects 8 subsystems through shared data:

```
Tagging Pipeline ──→ ComplianceScan ──→ Morning Briefing
       │                    │                   │
       ▼                    ▼                   ▼
 SEQ Sidecar          RAG Dashboard       Workflow Gating
       │                    │                   │
       ▼                    ▼                   ▼
 COBie Export ←── Container Write ←── Issue Creation
       │                    │                   │
       ▼                    ▼                   ▼
 CDE State Machine    Revision Snap      Transmittal Gen
```

**Key integration points:**
- ComplianceScan results gate CDE transitions (WIP→SHARED requires ≥70%, SHARED→PUBLISHED requires ≥90%)
- Issue creation auto-populates revision from `PhaseAutoDetect.DetectProjectRevision()`
- COBie export pre-flight checks container staleness and offers inline `WriteContainers`
- Workflow presets chain commands with per-step compliance gates

### 24.2 JSON Sidecar File Architecture

STING stores project-specific data in sidecar files alongside the `.rvt`:

| File | Purpose | Persistence |
|------|---------|-------------|
| `.sting_seq.json` | SEQ counter state | Per-session, merged on load |
| `_bim_manager/issues.json` | Issue tracker | Append-only with status history |
| `_bim_manager/transmittals.json` | Transmittal records | Linked to issues |
| `_bim_manager/meetings.json` | Meeting + action items | Per-meeting entries |
| `_bim_manager/approvals.json` | Document approval workflow | Sign-off audit trail |
| `_bim_manager/doc_versions.json` | CDE version history | Supersession chains |
| `.sting_warnings_baseline.json` | Warning baseline + first-seen | Per-warning SLA tracking |
| `.sting_compliance_trend.json` | Daily compliance snapshots | 90-day rolling window |
| `.sting_data_hash.json` | Data file change detection | Skip unchanged operations |
| `project_config.json` | Project settings | User-editable configuration |

**All sidecar writes use atomic temp-file + rename** to prevent corruption on crash.

### 24.3 Workflow Engine Internals

The `WorkflowEngine` orchestrates multi-step command sequences:

1. **Pre-flight check**: Validates all command tags resolve, checks element count thresholds
2. **Condition evaluation**: 19+ condition types with AND/OR compound logic
3. **Step execution**: Each step runs in its own `Transaction` within a `TransactionGroup`
4. **Result tracking**: Per-step `WorkflowStepResult` with timing, status, error messages
5. **Post-step cache invalidation**: Compliance + auto-tagger caches cleared after each step
6. **Failure handling**: Optional rollback-on-failure, retry with exponential backoff, fallback commands
7. **Persistence**: `WorkflowRunRecord` saved to JSONL log (capped at 100 records)

**Unknown conditions return FALSE** (fail-safe) to prevent typos from executing gated steps.

### 24.4 Compliance Engine Architecture

Three-layer compliance measurement:

| Layer | Metric | Weight | Description |
|-------|--------|--------|-------------|
| Tag compliance | `CompliancePercent` | 70% | % of taggable elements with non-empty TAG1 |
| Revision compliance | `RevisionPercent` | 30% | % of elements with STATUS + REV populated |
| Container compliance | `ContainerCompletePct` | Separate | % of tagged elements with all discipline containers |

**RAG thresholds** (configurable):
- GREEN: ≥80% weighted compliance
- AMBER: 50-80%
- RED: <50%

**Phase-based compliance**: `ComplianceScan.ByPhase` tracks compliance per Revit phase (Phase 1 existing vs Phase 2 new construction), enabling stage-gated quality checks.

### 24.5 Warnings Manager Deep Dive

The WarningsManager classifies 150+ Revit warning patterns into 8 BIM categories:

| Category | Examples | Auto-Fix Available |
|----------|----------|-------------------|
| Geometric | Zero-length walls, self-intersecting, coincident | Delete zero-length, join overlapping |
| Spatial | Room not enclosed, room separation overlap | Delete shorter separation line |
| MEP | Unconnected pipes, reverse flow, sizing mismatch | — (manual) |
| Structural | Deflection exceeded, eccentricity, load path | — (manual) |
| Annotation | Duplicate tags, overlapping text | Auto-increment duplicate marks |
| Data | Missing parameters, duplicate marks | Collision-safe mark suffix |
| Performance | Large groups, excessive links | — (manual) |
| Compliance | Fire rating, thermal bridge, accessibility | — (manual) |

**10 auto-fix strategies** operate in a single transaction with dry-run preview:
1. Delete duplicate instances (same location + type)
2. Delete shorter room separation line at overlaps
3. Auto-increment duplicate marks with collision-safe suffix
4. Unjoin non-intersecting geometry
5. (Reserved)
6. Join overlapping walls via `JoinGeometryUtils`
7. Move room tags to room center
8. Snap near-axis elements to nearest cardinal direction
9. Delete zero-length elements (<3mm)
10. Fix duplicate marks with full-model mark scan

**Warning health score** (0-100): Base 100, deductions per warning severity (Critical=-20, High=-5, Medium=-2, Low=-1).

**SLA thresholds** (configurable per project via `WARNING_SLA_*_HOURS` config keys):
- CRITICAL: 4 hours
- HIGH: 24 hours
- MEDIUM: 168 hours (1 week)
- LOW: 336 hours (2 weeks)

### 24.6 Performance Guide

| Operation | Optimization | Impact |
|-----------|-------------|--------|
| BatchTag 50K elements | Chunked 200-element transactions | Prevents Revit memory exhaustion |
| ComplianceScan | 8-second timeout, 30-second cache | Prevents UI freeze on large models |
| WriteContainers | Discipline-filtered (skip irrelevant) | 60-80% fewer writes per element |
| AutoTagger | 5-second context TTL, deferred bulk queue | Prevents thundering herd |
| StaleMarker | Room index 30-second cache, 20% LRU eviction | Prevents per-trigger room scan |
| Formula evaluation | 5-minute session cache | Single CSV parse per session |
| Grid reference | 2-minute document-scoped cache | Single collector per session |
| Warning classification | First-word index for O(1) pattern lookup | 60-80% faster on 500+ warnings |

**Critical performance rules:**
1. Never call `FilteredElementCollector` inside a per-element loop
2. Cache `GetSolidFillPattern()` — it's called 7+ times across commands
3. Use `ElementMulticategoryFilter` to pre-filter before iteration
4. Freeze all static `SolidColorBrush` instances for thread safety

---

*This guide is maintained alongside the STING Tools codebase. For the latest version, check `Data/BIM_COORDINATION_WORKFLOW_GUIDE.md` in the repository.*

*Related guides:*
- [Tagging Guide](TAGGING_GUIDE.md) — Detailed tagging workflow and command reference
- [DWG to BIM Guide](DWG_TO_BIM_GUIDE.md) — CAD conversion workflow
- [Tag Family Creation Guide](TAG_FAMILY_CREATION_GUIDE.md) — Creating custom tag families
