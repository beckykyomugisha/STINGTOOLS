# STING Tools — BIM Management, Coordination & Document Management Workflow Guide

> **Version**: 1.0 | **Standard**: ISO 19650-1:2018, ISO 19650-2:2018, BS EN ISO 19650-3:2020
> **Audience**: BIM Managers, BIM Coordinators, Information Managers, Document Controllers

---

## Table of Contents

1. [Overview & Philosophy](#1-overview--philosophy)
2. [Daily BIM Coordinator Workflow](#2-daily-bim-coordinator-workflow)
3. [Model Setup & Configuration](#3-model-setup--configuration)
4. [Tagging Workflow (ISO 19650 Asset Tags)](#4-tagging-workflow-iso-19650-asset-tags)
5. [Document Management & CDE](#5-document-management--cde)
6. [Issue Management & BCF](#6-issue-management--bcf)
7. [Revision Management](#7-revision-management)
8. [Coordination & Clash Detection](#8-coordination--clash-detection)
9. [Compliance & Quality Assurance](#9-compliance--quality-assurance)
10. [Warnings Management](#10-warnings-management)
11. [Workflow Automation Engine](#11-workflow-automation-engine)
12. [Data Exchange (Excel, COBie, IFC, BCF)](#12-data-exchange)
13. [Handover & FM Data](#13-handover--fm-data)
14. [BEP & Project Governance](#14-bep--project-governance)
15. [Reporting & Dashboards](#15-reporting--dashboards)
16. [International Standards Reference](#16-international-standards-reference)
17. [Troubleshooting](#17-troubleshooting)

---

## 1. Overview & Philosophy

### Why STING Tools?

STING Tools transforms Revit into a **fully ISO 19650-compliant BIM platform** by adding:

- **Automated asset tagging** — Every element gets a unique 8-segment ISO 19650 tag
- **Document management** — CDE state machine (WIP → SHARED → PUBLISHED → ARCHIVE)
- **Issue tracking** — RFI/NCR/SI/TQ issue lifecycle with BCF integration
- **Compliance scanning** — Real-time RAG (Red/Amber/Green) compliance dashboard
- **Workflow automation** — 27+ named workflow presets with conditional execution
- **Data exchange** — Bidirectional Excel link, COBie V2.4, IFC, BCF 2.1
- **Warnings intelligence** — 100+ classified Revit warnings with auto-fix strategies

### The ISO 19650 Information Management Cycle

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   CAPTURE   │───→│   MANAGE    │───→│  EXCHANGE   │───→│  HANDOVER   │
│             │    │             │    │             │    │             │
│ Tag elements│    │ CDE states  │    │ COBie/IFC   │    │ FM/O&M data │
│ Populate    │    │ Issue track │    │ Excel link  │    │ Asset regs  │
│ tokens      │    │ Revisions   │    │ BCF export  │    │ Maintenance │
│ Validate    │    │ Compliance  │    │ Transmittals│    │ schedules   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
```

### Key Principles

| Principle | How STING Implements It |
|-----------|------------------------|
| **Single Source of Truth** | All tag data lives in Revit shared parameters — no external databases |
| **Data-driven, not hardcoded** | Tag maps, codes, and formats loaded from CSV/JSON config files |
| **Automation-first** | One-click workflows replace multi-step manual processes |
| **Incremental** | Tag only new/changed elements — don't re-process the entire model |
| **Auditable** | Every tag change recorded with `ASS_TAG_PREV_TXT` + `ASS_TAG_MODIFIED_DT` |
| **Standards-compliant** | ISO 19650, BS 1192, PAS 1192-2, COBie V2.4, BCF 2.1 |

---

## 2. Daily BIM Coordinator Workflow

### 2.1 Morning Health Check (08:30–09:00)

**Goal**: Assess overnight model changes, identify issues, plan the day.

**Automated Option**: Run workflow preset `MorningHealthCheck` (one-click):
- BIM tab → Workflows → **Morning Health Check**
- Or: BIM Coordination Center → WORKFLOWS → **Morning Health**

**Manual Steps**:

| Step | Action | Button Location | What to Look For |
|------|--------|-----------------|------------------|
| 1 | Open model | — | Morning briefing dialog appears automatically if alerts exist |
| 2 | Check compliance | Status bar (bottom of panel) | RAG status: Green (>80%), Amber (50-80%), Red (<50%) |
| 3 | Check stale elements | BIM tab → **Coordination Center** | Stale count in KPI cards — elements moved since last tag |
| 4 | Review warnings | BIM tab → **Warnings Dashboard** | New critical/high warnings since yesterday |
| 5 | Check SLA violations | BIM tab → **Coordination Center** → ISSUES | Overdue issues (Critical: 4h, High: 24h SLA) |
| 6 | Run retag stale | CREATE tab → **Retag Stale** | Fixes elements that moved/changed since last tag |
| 7 | Run pre-tag audit | ORGANISE tab → **Pre-Audit** | Predicts issues before committing tag changes |

**Morning Briefing Dialog** (automatic on document open):
- Tag compliance % with 7-day trend (improving/stable/declining)
- Stale element count
- Warning count
- Overdue SLA violations
- One-click "Run Morning Health Check" button

### 2.2 Mid-Morning Coordination (09:00–12:00)

**Goal**: Coordinate with design team, resolve issues, run quality checks.

| Task | How | Details |
|------|-----|---------|
| **Tag new elements** | ORGANISE → **Tag New** | Tags only new/untagged elements (incremental) |
| **Review issues** | BIM → **Coordination Center** → ISSUES | Filter by status/priority, double-click to zoom to 3D |
| **Raise new issues** | BIM → **Raise Issue** | Creates RFI/NCR/SI with element selection |
| **Run clash detection** | BIM → **Clash Detection** | Cross-discipline interference check |
| **Validate tags** | CREATE → **Validate** | ISO 19650 compliance check |
| **Fix warnings** | BIM → **Warnings Auto-Fix** | Auto-resolve known warning patterns |

### 2.3 Afternoon Production (12:00–16:00)

**Goal**: Data exchange, document preparation, transmittals.

| Task | How | Details |
|------|-----|---------|
| **Export COBie** | BIM → **COBie Export** | V2.4 spreadsheet for FM handover |
| **Export Excel** | BIM → **Export to Excel** | 30+ column tag data for review |
| **Create transmittal** | BIM → **Create Transmittal** | ISO 19650 document transmittal |
| **Update CDE status** | BIM → **Document Center** | WIP→SHARED→PUBLISHED transitions |
| **Print sheets** | DOCS → **Batch Print** | PDF export with ISO naming |

### 2.4 End of Day Sync (16:00–17:00)

**Automated Option**: Run workflow preset `EndOfDaySync`:

| Step | Action | Purpose |
|------|--------|---------|
| 1 | Retag stale | Fix moved elements |
| 2 | Validate | Check compliance |
| 3 | Save warning baseline | Track warning trends |
| 4 | Export registers | Tag register + sheet register CSV |
| 5 | Model health | Score the model (0-100) |
| 6 | Warnings export | CSV for BIM360/Aconex |
| 7 | Create revision | Snapshot element state |

---

## 3. Model Setup & Configuration

### 3.1 First-Time Project Setup

**Option A: One-Click Master Setup** (recommended)
- TEMP tab → **Master Setup**
- Runs 18 steps automatically: parameters → materials → types → schedules → templates → filters → worksets

**Option B: 7-Page Setup Wizard**
- TEMP tab → **★ Project Setup Wizard**
- Guided wizard: project info → disciplines → levels → materials → families → schedules → templates

**Option C: Step-by-Step Manual Setup**

| Order | Step | Button | What It Does |
|-------|------|--------|-------------|
| 1 | Load parameters | CREATE → **Load Shared Params** | Binds 200+ shared parameters to categories |
| 2 | Configure project | CREATE → **Project Config** | Set tag maps, codes, format in `project_config.json` |
| 3 | Create materials | TEMP → **Create BLE Materials** / **Create MEP Materials** | 815 BLE + 464 MEP materials from CSV |
| 4 | Create types | TEMP → Families → **Walls/Floors/etc.** | Wall, floor, ceiling, roof, duct, pipe types |
| 5 | Create schedules | TEMP → **Batch Schedules** | 168 schedule definitions from CSV |
| 6 | Create templates | TEMP → Templates → **View Templates** | 23 view template definitions |
| 7 | Create filters | TEMP → Templates → **Filters** | 28 parametric view filters |
| 8 | Create worksets | TEMP → Templates → **Worksets** | 35 workset definitions |

### 3.2 Project Configuration (`project_config.json`)

The project config file controls all tag behaviour. Located alongside the `.rvt` file or in the plugin Data folder.

**Key Settings**:

```json
{
  "TAG_FORMAT": {
    "SEPARATOR": "-",
    "NUM_PAD": 4,
    "SEGMENT_ORDER": ["DISC","LOC","ZONE","LVL","SYS","FUNC","PROD","SEQ"]
  },
  "TAG_PREFIX": "",
  "TAG_SUFFIX": "",
  "CATEGORY_SKIP": ["Generic Models", "Mass"],
  "CATEGORY_FORCE_SYS": {
    "Sprinklers": "FP",
    "Fire Alarm Devices": "FLS"
  },
  "CATEGORY_TOKEN_OVERRIDES": {
    "Structural Columns": { "DISC": "S", "SYS": "STR", "FUNC": "STR" },
    "Structural Framing": { "DISC": "S", "SYS": "STR", "FUNC": "STR" }
  },
  "SEQ_SCHEME": "Numeric",
  "SEQ_INCLUDE_ZONE": false,
  "COMPLIANCE_GATE_PCT": 70,
  "PROXIMITY_RADIUS_FT": 10.0,
  "RESOLVE_BATCH_SIZE": 500,
  "COST_RATES_FILE": "cost_rates_5d.csv",
  "CUSTOM_VALID_DISC": ["M","E","P","A","S","FP","LV","G","FLS","ICT"],
  "CUSTOM_VALID_SYS": [],
  "SHEET_NAMING_STRICT_MODE": false,
  "CDE_SHARED_MIN_COMPLIANCE": 70,
  "CDE_PUBLISHED_MIN_COMPLIANCE": 90,
  "USER_ROLE": "M"
}
```

| Key | Purpose | Default |
|-----|---------|---------|
| `TAG_FORMAT.SEPARATOR` | Character between tag segments | `-` |
| `TAG_FORMAT.NUM_PAD` | Zero-padding for SEQ numbers | `4` (→ 0001) |
| `CATEGORY_SKIP` | Categories excluded from tagging | Generic Models, Mass |
| `CATEGORY_FORCE_SYS` | Override SYS code per category | Sprinklers→FP, Fire Alarm→FLS |
| `CATEGORY_TOKEN_OVERRIDES` | Override any token per category | Structural→S/STR |
| `SEQ_SCHEME` | Numbering style: Numeric/Alpha/ZonePrefix/DiscPrefix | Numeric |
| `COMPLIANCE_GATE_PCT` | Minimum compliance % to pass gate | 70 |
| `CDE_SHARED_MIN_COMPLIANCE` | Min compliance to transition WIP→SHARED | 70 |
| `CDE_PUBLISHED_MIN_COMPLIANCE` | Min compliance to transition SHARED→PUBLISHED | 90 |
| `USER_ROLE` | ISO 19650 role code (A/M/E/S/H/P/C/I/K/Q/F/W/L/Z) | M |


---

## 4. Tagging Workflow (ISO 19650 Asset Tags)

### 4.1 Tag Format

Every element receives an **8-segment ISO 19650 asset tag**:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
 M   - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

| Segment | Description | Source | Example Codes |
|---------|-------------|--------|---------------|
| DISC | Discipline | Category map (41 mappings) | M, E, P, A, S, FP, LV, G |
| LOC | Location/building | Room name → Project Info → "XX" | BLD1, BLD2, EXT |
| ZONE | Zone | Room department → name patterns | Z01, Z02, ZZ |
| LVL | Level | Level name parsing | L01, GF, B1, RF |
| SYS | System | 6-layer intelligence (MEP→Circuit→Family→Room→Category) | HVAC, DCW, LV, FP |
| FUNC | Function | SYS→FUNC map (CIBSE/Uniclass) | SUP, HTG, PWR, SAN |
| PROD | Product | Family-aware (35+ patterns) | AHU, DB, FCU, WC |
| SEQ | Sequence | Auto-incremented per DISC/SYS/FUNC/PROD group | 0001, 0042 |

### 4.2 Tagging Methods (Choose One)

| Method | Button | When to Use | What It Does |
|--------|--------|-------------|-------------|
| **Full Auto** | CREATE → **Auto Populate** | First-time tagging, clean model | Tokens → Dimensions → MEP → Formulas → Tags → Containers → TAG7 |
| **Tag+Combine** | ORGANISE → **Tag+Combine** | Quick one-click for view/selection | Populate + Tag + Combine all containers |
| **Auto Tag** | ORGANISE → **Auto Tag** | Tag active view with collision control | Scope: view, collision mode: Skip/Overwrite/Increment |
| **Batch Tag** | ORGANISE → **Batch Tag** | Tag entire project | Full project scan with progress dialog |
| **Tag New** | ORGANISE → **Tag New** | Incremental after adding elements | Only processes untagged elements |
| **Re-Tag** | ORGANISE → **Re-Tag** | Force re-derive moved/changed elements | Overwrites all tokens and rebuilds tag |
| **Retag Stale** | CREATE → **Retag Stale** | Fix elements flagged as stale | Only processes STING_STALE_BOOL=1 elements |
| **Real-Time** | VIEW → **Auto-Tagger** | Continuous auto-tag on element placement | IUpdater fires on every new element |

### 4.3 The Full Pipeline (11 Steps)

When any tagging command runs, each element goes through `TagPipelineHelper.RunFullPipeline()`:

| Step | Operation | What It Does |
|------|-----------|-------------|
| 1 | Tag history | Saves current tag to `ASS_TAG_PREV_TXT`, timestamps `ASS_TAG_MODIFIED_DT` |
| 2 | Token lock check | Reads `ASS_TOKEN_LOCK_TXT` — locked tokens are preserved through the pipeline |
| 3 | TypeTokenInherit | Copies DISC/SYS/FUNC/PROD from family type to instance |
| 4 | PopulateAll | Derives all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV) |
| 5 | CategoryForceSys | Applies `CATEGORY_FORCE_SYS` overrides from config |
| 6 | CategoryTokenOverrides | Applies `CATEGORY_TOKEN_OVERRIDES` from config |
| 7 | NativeParamMapper | Maps 30+ Revit built-in parameters to STING shared params |
| 8 | FormulaEngine | Evaluates 199 dependency-ordered formulas (cost/flow/area/env) |
| 9 | BuildAndWriteTag | Assembles 8-segment tag with collision detection |
| 10 | WriteContainers | Writes tag to all 53 discipline-specific containers |
| 11 | WriteTag7All | Builds TAG7 rich narrative (A-F sub-sections) |

After commit: SEQ counters saved to `.sting_seq.json` sidecar file.

### 4.4 Collision Handling

When two elements get the same tag, the collision mode determines behaviour:

| Mode | Behaviour | When to Use |
|------|-----------|-------------|
| **Skip** | Leave existing tag unchanged | Safe mode — protect existing tags |
| **Overwrite** | Replace existing tag with new one | Model restructured — reset everything |
| **AutoIncrement** | Increment SEQ until unique | Default — ensures no duplicates |

### 4.5 Sequence Numbering Schemes

| Scheme | Format | Example | Config Key |
|--------|--------|---------|------------|
| Numeric | 0001, 0002... | M-BLD1-Z01-L01-HVAC-SUP-AHU-0001 | `Numeric` |
| Alpha | A, B, C... | M-BLD1-Z01-L01-HVAC-SUP-AHU-A | `Alpha` |
| ZonePrefix | Z01-0001 | M-BLD1-Z01-L01-HVAC-SUP-AHU-Z01-0001 | `ZonePrefix` |
| DiscPrefix | M-0001 | M-BLD1-Z01-L01-HVAC-SUP-AHU-M-0001 | `DiscPrefix` |

### 4.6 Validation

**Button**: CREATE → QA → **Validate**

Checks every element against ISO 19650 requirements:

| Check | What It Validates | Severity |
|-------|-------------------|----------|
| Tag completeness | All 8 segments present | ERROR |
| Placeholder detection | GEN/XX/ZZ/0000 values flagged | WARNING |
| DISC code | Valid against ISO 19650 code list | ERROR |
| SYS code | Valid against CIBSE/Uniclass list | ERROR |
| FUNC code | Valid and consistent with SYS | ERROR |
| DISC↔SYS consistency | e.g., SYS=LV requires DISC=E, not DISC=M | ERROR |
| SYS↔FUNC consistency | e.g., FUNC=PWR invalid for SYS=HVAC | ERROR |
| FUNC↔PROD consistency | e.g., FUNC=SUP incompatible with PROD=WC | WARNING |
| STATUS populated | NEW/EXISTING/DEMOLISHED/TEMPORARY present | WARNING |
| REV populated | Revision code present | WARNING |

**Four compliance buckets**:
1. **RESOLVED** — All 8 segments valid, no placeholders = production-ready
2. **COMPLETE_PLACEHOLDERS** — 8 segments present but contains GEN/XX/ZZ/0000
3. **INCOMPLETE** — Fewer than 8 segments
4. **UNTAGGED** — No tag at all

---

## 5. Document Management & CDE

### 5.1 The Common Data Environment (CDE)

ISO 19650-1 defines 4 CDE states. STING enforces these as a **one-way state machine**:

```
    ┌─────┐     ┌────────┐     ┌───────────┐     ┌─────────┐
    │ WIP │────→│ SHARED │────→│ PUBLISHED │────→│ ARCHIVE │
    └─────┘     └────────┘     └───────────┘     └─────────┘
       ↑            │
       └────────────┘ (rework)
```

| State | ISO Status Code | Suitability | Who Can Access | Purpose |
|-------|----------------|-------------|----------------|---------|
| **WIP** | S1 (Work in Progress) | S0-S2 | Author only | Active development |
| **SHARED** | S3 (Issued for Coordination) | S3 | Design team | Coordination between disciplines |
| **PUBLISHED** | S4 (Issued for Approval) | S4-S7 | Client/approver | Formal issue for review |
| **ARCHIVE** | — | CR/AB | All (read-only) | Long-term record |

**Additional states** (terminal):
- **SUPERSEDED** — Replaced by newer version
- **WITHDRAWN** — Removed from circulation
- **OBSOLETE** — No longer valid

### 5.2 Document Management Center

**Button**: BIM tab → **Document Center**

The Document Management Center is a 7-tab WPF dialog:

| Tab | Purpose | Key Operations |
|-----|---------|----------------|
| **FILE/BULK** | File management | Import, rename, auto-correct names, bulk operations |
| **DOCS/CDE** | CDE state management | Change CDE status, suitability codes, bulk transitions |
| **ISSUES** | Issue tracking | View/create/update RFI/NCR/SI/TQ issues |
| **REVISIONS** | Revision management | Create revisions, revision clouds, tag integration |
| **COORDINATION** | Clash detection | Run clashes, BCF export/import, review tracking |
| **HANDOVER** | FM data preparation | COBie export, FM handover, stage gate |
| **NOTES/BEP** | BEP management | Generate/update BEP, sticky notes |

### 5.3 CDE State Transitions

To change CDE status:
1. Open **Document Center** → DOCS/CDE tab
2. Select document(s) in the list
3. Click **Update CDE Status**
4. Valid transitions are shown based on current state
5. System enforces:
   - **WIP→SHARED** requires minimum 70% tag compliance (configurable)
   - **SHARED→PUBLISHED** requires minimum 90% tag compliance
   - **ARCHIVE** is terminal — no further transitions
   - **SHARED→WIP** allowed (rework path)

Each transition is logged in `status_history` with timestamp, old/new state, and username.

### 5.4 Suitability Codes (ISO 19650)

| Code | Meaning | Typical CDE State |
|------|---------|-------------------|
| S0 | Work in Progress | WIP |
| S1 | For Coordination | WIP |
| S2 | For Information | WIP |
| S3 | For Review and Comment | SHARED |
| S4 | For Stage Approval | PUBLISHED |
| S5 | For Manufacture | PUBLISHED |
| S6 | For PIM Authorization | PUBLISHED |
| S7 | For AIM Authorization | PUBLISHED |
| CR | As Constructed Record | ARCHIVE |
| AB | As Built Record | ARCHIVE |

### 5.5 ISO 19650 File Naming Convention

```
PROJECT-ORIGINATOR-VOLUME-LEVEL-TYPE-DISCIPLINE-NUMBER.EXT
```

Example: `HSP-ARC-ZZ-L01-DR-A-0001.pdf`

The **Sheet Naming Check** command (DOCS tab) validates sheet names against this format.

---

## 6. Issue Management & BCF

### 6.1 Issue Types

| Type | Code | Description | SLA |
|------|------|-------------|-----|
| **RFI** | RFI-NNNN | Request for Information | Priority-dependent |
| **NCR** | NCR-NNNN | Non-Conformance Report | Critical: 4h, High: 24h |
| **SI** | SI-NNNN | Site Instruction | Medium: 1 week |
| **TQ** | TQ-NNNN | Technical Query | Low: 2 weeks |
| **DN** | DN-NNNN | Design Note | Low: 2 weeks |
| **CVI** | CVI-NNNN | Confirmation of Verbal Instruction | High: 24h |
| **EWN** | EWN-NNNN | Early Warning Notice (NEC contract) | High: 24h |
| **CE** | CE-NNNN | Compensation Event (NEC contract) | High: 24h |

### 6.2 Creating Issues

**Button**: BIM tab → **Raise Issue**

1. Select elements in the model related to the issue
2. Click **Raise Issue**
3. Enter title and select priority (Critical/High/Medium/Low)
4. System auto-populates: revision, discipline (from element DISC tokens), affected elements
5. Issue saved to `_bim_manager/issues.json` with unique ID

**Quick Issue** (from Document Center):
- ISSUES tab → **Quick Issue** button
- Minimal form: title + priority → auto-creates with defaults

### 6.3 Issue SLA Enforcement

| Priority | SLA Threshold | Action on Breach |
|----------|--------------|------------------|
| CRITICAL | 4 hours | Auto-escalation notification |
| HIGH | 24 hours | Warning in morning briefing |
| MEDIUM | 1 week (168h) | Listed in weekly report |
| LOW | 2 weeks (336h) | Listed in monthly report |

SLA violations are checked:
- Automatically on document open (morning briefing)
- In BIM Coordination Center → ISSUES tab
- By `CheckSLAViolations()` in BIMManagerEngine

### 6.4 BCF Integration

**Export**: BIM tab → **BCF Export**
- Creates BCF 2.1 XML with viewpoints (camera position, direction, up vector)
- Includes issue metadata, affected elements, viewpoint screenshots

**Import**: BIM tab → **BCF Import**
- Reads BCF XML, creates issues in `issues.json`
- Deduplication: skips issues matching existing element ID overlap

---

## 7. Revision Management

### 7.1 Creating Revisions

**Button**: BIM tab → **Create Revision**

1. Pre-flight: checks compliance (warns if <80% with discipline breakdown)
2. Creates Revit Revision with ISO 19650 naming
3. Takes tag snapshot (stores all token values per element)
4. Logs to revision history

### 7.2 Revision Comparison

**Button**: BIM tab → **Revision Compare**

Compares current element tags against a previous revision snapshot:
- Shows elements added, removed, or changed
- Categorizes changes: TOKEN_CHANGE, CONTAINER_REGEN, NARRATIVE_CHANGE, STATUS_CHANGE, TAG_REFORMAT
- Enables targeted investigation of what changed between revisions

### 7.3 Auto-Revision on Tag Change

**Button**: BIM tab → **Auto Revision on Tag Change**

Automatically stamps revision clouds on elements whose tags have changed since the last revision. Useful for tracking tag updates across design iterations.

---

## 8. Coordination & Clash Detection

### 8.1 BIM Coordination Center

**Button**: BIM tab → **Coordination Center**

The unified coordination dashboard with 9+ tabs:

| Tab | KPI Cards | Key Actions |
|-----|-----------|-------------|
| **OVERVIEW** | Elements, Compliance %, Warnings, Issues, Containers | Quick actions, action required list, compliance forecast |
| **MODEL HEALTH** | Health Score (0-100), Coverage, Warnings, Stale | Health checks with Fix buttons, recommendations |
| **WARNINGS** | Total, Critical, Auto-Fixable, SLA Violations | TreeView by category, double-click → 3D zoom |
| **ISSUES** | Open, Overdue, Critical, Closed | DataGrid with filter/sort, context menu → zoom |
| **REVISIONS** | Total, Latest, Clouds, Pending | Revision history DataGrid, compare |
| **PLATFORM** | Sync status per platform | ACC, SharePoint, Procore, Aconex, Trimble |
| **WORKFLOWS** | Total Runs, Last Run, Compliance Δ | Quick workflow buttons, execution history |
| **QA DASHBOARD** | Placeholders, Anomalies, Stale, Errors | Token coverage matrix, cross-system integrity |
| **4D/5D** | Tasks, Cost, Milestones, Earned Value | Schedule and cost management |

### 8.2 Clash Detection

**Button**: BIM tab → **Clash Detection** (or Coordination Center → COORDINATION)

Checks for:
- MEP vs Structural intersections
- MEP vs Architectural conflicts
- Cross-linked model clashes (with transform-aware bounding box)

Results exported as BCF for external review tools.

### 8.3 Federated Model Compliance

**Button**: BIM tab → **Federated Compliance**

Scans all linked Revit models for tag compliance:
- Per-link RAG status
- Aggregate federated compliance %
- Identifies weakest link model

---

## 9. Compliance & Quality Assurance

### 9.1 Compliance Scanning

**Automatic**: Runs on document open, updates status bar every 30 seconds.

**Manual**: CREATE tab → QA → **Completeness %**

Metrics tracked:
| Metric | Formula | RAG Thresholds |
|--------|---------|----------------|
| Tag Compliance | Tagged / Total elements | Green >80%, Amber 50-80%, Red <50% |
| Strict Compliance | Fully resolved / Total | Higher bar than tag % |
| Container Compliance | Elements with all containers / Tagged | Ensures discipline containers populated |
| Revision Compliance | Elements with REV / Tagged | Ensures revision tracking |

### 9.2 Compliance Gates

Certain operations are blocked below compliance thresholds:

| Operation | Minimum Compliance | Override? |
|-----------|-------------------|-----------|
| WIP → SHARED transition | 70% (configurable) | Yes, with acknowledgment |
| SHARED → PUBLISHED | 90% (configurable) | Yes, with acknowledgment |
| COBie export | 60% | Yes, with warning |
| Create Revision | 80% (warning only) | Yes |
| Workflow completion | `COMPLIANCE_GATE_PCT` config | Shows discipline breakdown |

### 9.3 Pre-Tag Audit

**Button**: ORGANISE → **Pre-Audit** (or CREATE → QA → **Inspect Selection**)

Dry-run that predicts tags without writing them:
- Shows predicted tag for each element
- Identifies potential collisions
- Validates predicted tokens against ISO code lists
- Reports spatial detection results (LOC/ZONE auto-detect)
- Offers one-click auto-fix chain

### 9.4 Data Drop Readiness

**Button**: BIM tab → **Data Drop Readiness**

Assesses model against ISO 19650 data drop milestones:

| Milestone | Compliance Target | Required Data |
|-----------|------------------|---------------|
| DD1 (Brief) | 30% | Basic element presence, rooms defined |
| DD2 (Concept) | 60% | All elements tagged, types assigned |
| DD3 (Design) | 85% | Full tag + containers + TAG7 |
| DD4 (Handover) | 95% | Complete COBie data, maintenance schedules |


---

## 10. Warnings Management

### 10.1 Warning Classification

STING classifies all Revit warnings into 8 categories with 5 severity tiers:

| Category | Examples | Impact |
|----------|----------|--------|
| **Geometric** | Overlapping walls, zero-length elements, self-intersecting | Model integrity |
| **Spatial** | Room not enclosed, room separation overlap | Area calculations |
| **MEP** | Unconnected pipes, undefined classification, open connectors | System analysis |
| **Structural** | Beam deflection, eccentricity, bearing capacity | Structural safety |
| **Annotation** | Duplicate tags, missing tags, stale annotations | Documentation |
| **Data** | Duplicate marks, missing parameters, host deleted | Data quality |
| **Performance** | Too many groups, excessive linked models | Model performance |
| **Compliance** | Fire rating, Part M access, Part L thermal | Regulatory |

| Severity | Score Impact | SLA | Colour |
|----------|-------------|-----|--------|
| CRITICAL | -20 per warning | 4 hours | Red |
| HIGH | -5 per warning | 24 hours | Orange |
| MEDIUM | -2 per warning | 1 week | Yellow |
| LOW | -1 per warning | 2 weeks | Blue |
| INFO | 0 | None | Grey |

**Warning Health Score**: 100 - (sum of severity penalties). Score 0-100 where 100 = no warnings.

### 10.2 Warnings Dashboard

**Button**: BIM tab → **Warnings Dashboard**

Shows:
- Total warnings with trend vs baseline
- Breakdown by severity, category, discipline, level, workset
- Auto-fixable vs manual-review counts
- Top 10 hotspot elements (most warnings)
- Deliverable impact analysis (which exports are affected)

### 10.3 Auto-Fix Strategies

| Strategy | Warning Type | Fix Action |
|----------|-------------|------------|
| 1 | Duplicate instances | Delete the duplicate element |
| 2 | Room separation overlap | Delete shorter separation line |
| 3 | (reserved) | — |
| 4 | Duplicate marks | Append `_2`, `_3` suffix (collision-safe) |
| 5 | Unjoined geometry | Unjoin non-intersecting geometry |
| 6 | Overlapping walls | Auto-join via `JoinGeometryUtils` |
| 7 | Room tag outside boundary | Move tag to room center |
| 8 | Off-axis elements | Snap to nearest cardinal direction |
| 9 | Zero-length elements | Delete elements <3mm |
| 10 | Duplicate marks (full-model) | Full-model mark scan + suffix increment |

**Button**: BIM tab → **Warnings Auto-Fix**

Runs: scan → filter fixable → preview → confirm → single transaction → verify fix count.

### 10.4 Warning Baselines

**Button**: BIM tab → **Warnings Baseline**

Saves current warning count as `.sting_warnings_baseline.json`:
- Compare against previous baseline (added/removed/unchanged)
- Per-warning-type first-seen timestamps for SLA tracking
- Delta symbols in dashboard (↑ increase, ↓ decrease, → stable)

### 10.5 Warning-to-Issue Escalation

**Automatic**: Critical warnings auto-create NCR issues; High warnings create SI issues.
- Groups by warning type to avoid duplicate issues
- Deduplicates against existing open issues
- Max 20 issue types per scan

---

## 11. Workflow Automation Engine

### 11.1 How Workflows Work

The **WorkflowEngine** chains named command sequences with conditional execution:

```
Workflow Preset (JSON) → For each Step:
  1. Check conditions (compliance threshold, stale elements, workshared, etc.)
  2. Execute command via RunCommand<T>
  3. Report success/failure
  4. Save result to STING_WORKFLOW_LOG.json
```

Each workflow runs in an atomic `TransactionGroup` — if a critical step fails, user can rollback.

### 11.2 Built-In Workflow Presets (27+)

#### Daily Operations

| Preset | Steps | Purpose |
|--------|-------|---------|
| **MorningHealthCheck** | 10 | Retag stale → audit → tag new → validate → naming → health → templates → issues → revisions → dashboard |
| **DailyQA** | 11 | Enhanced daily QA with conditional steps (adaptive) |
| **PostTaggingQA** | 5 | PreTagAudit → Validate → Dashboard → Register → Template check |
| **EndOfDaySync** | 8 | Retag → validate → baseline → registers → health → warnings → revision |

#### Data Exchange

| Preset | Steps | Purpose |
|--------|-------|---------|
| **WeeklyDataDrop** | 10 | Retag → resolve → validate → audit → COBie → Excel → sheets → register → health → dashboard |
| **CDE_Submission** | 8 | Retag → resolve → validate → sheet naming → doc naming → registers → transmittal |
| **DocumentPackage** | 6 | Tag → combine → COBie → drawing register → transmittal → BEP |

#### Coordination

| Preset | Steps | Purpose |
|--------|-------|---------|
| **MEPCoordination** | 6 | Clashes → system push → retag → validate → warnings → compliance |
| **FederatedModelAudit** | 7 | Fed compliance → cross-model clash → naming → MEP clearance → spatial → warnings → report |
| **PreMeetingPrep** | 7 | Stale → warnings → validate → summary → issues → revisions → HTML report |

#### Handover

| Preset | Steps | Purpose |
|--------|-------|---------|
| **HandoverReadiness** | 9 | Stale → tag → validate → templates → COBie → register → BOQ → BEP → revision |
| **ModelAuditDeep** | 8 | Warnings → templates → pipeline → schedules → schema → tags → sheets → compliance |

#### Sector-Specific

| Preset | Steps | Purpose |
|--------|-------|---------|
| **Healthcare_NHS** | 8 | HTM compliance, medical gas, infection zones, COBie for CAFM |
| **DataCentre** | 7 | Power distribution, cooling, cable tray, Uptime Institute |
| **CommercialOffice** | 7 | BCO Guide, BREEAM, lease demise, occupancy |
| **Residential** | 7 | Part L/M/B, plot numbering, sales schedules |
| **Education** | 7 | BB103 area, DfE guidelines, safeguarding, FF&E |

### 11.3 Conditional Step Execution

Workflow steps can have conditions that skip them when not needed:

| Condition | Description | Example Use |
|-----------|-------------|-------------|
| `maxCompliancePct` | Skip if compliance already exceeds threshold | Skip re-tag if already 95% |
| `minCompliancePct` | Skip if compliance is below threshold | Skip export if <50% |
| `requiresStaleElements` | Skip if no stale elements | Skip retag if nothing changed |
| `has_warnings` | Skip if no warnings | Skip auto-fix if model clean |
| `has_critical_warnings` | Skip if no critical warnings | Skip escalation |
| `has_open_issues` | Skip if no open issues | Skip issue review |
| `has_links` | Skip if no linked models | Skip federated check |
| `has_untagged` | Skip if all elements tagged | Skip batch tag |
| `has_placeholders` | Skip if no GEN/XX/ZZ tokens | Skip resolve |
| `has_container_gaps` | Skip if containers ≥95% | Skip combine |

### 11.4 Running Workflows

**Method 1**: BIM tab → Workflows section → Click preset button
**Method 2**: BIM Coordination Center → WORKFLOWS tab → Quick buttons
**Method 3**: StingCommandHandler dispatch tag `RunWorkflow_PresetName`

### 11.5 Custom Workflow Creation

Create a JSON file in the Data folder named `WORKFLOW_YourName.json`:

```json
{
  "Name": "MyCustomWorkflow",
  "Label": "Custom Weekly Check",
  "Steps": [
    {
      "CommandTag": "RetagStale",
      "Label": "Re-tag stale elements",
      "requiresStaleElements": true
    },
    {
      "CommandTag": "ValidateTags",
      "Label": "Validate ISO compliance"
    },
    {
      "CommandTag": "WarningsAutoFix",
      "Label": "Auto-fix warnings",
      "has_warnings": true,
      "RetryCount": 2,
      "RetryDelayMs": 1000
    },
    {
      "CommandTag": "ExportCSV",
      "Label": "Export tag register"
    }
  ]
}
```

---

## 12. Data Exchange

### 12.1 Excel Link (Bidirectional)

**Export**: BIM tab → **Export to Excel**
- 30+ columns: all tags, identity, spatial, MEP, cost data
- Validation lists for DISC/SYS/FUNC/PROD/LOC/ZONE codes

**Import**: BIM tab → **Import from Excel**
- Validates all token values against ISO code lists
- Cross-validates: DISC↔SYS, SYS↔FUNC, FUNC↔PROD consistency
- `CLEAR` sentinel: type "CLEAR" in Excel cell to intentionally empty a field
- 10K row safety limit
- Audit trail: records all changes with timestamps

**Round-Trip**: BIM tab → **Excel Round Trip**
- One-click: export → user edits → import with validation

### 12.2 COBie V2.4 Export

**Button**: BIM tab → **COBie Export**

Generates a complete COBie V2.4 spreadsheet with 19 worksheets:

| Sheet | Content | Source |
|-------|---------|--------|
| Instruction | Export metadata, preset info, colour coding | Auto-generated |
| Contact | Project contacts | Project Information |
| Facility | Building data | Project Information + rooms |
| Floor | Level data | Revit Levels |
| Space | Room data | Revit Rooms |
| Zone | Zone classifications | 16 zone types (fire, HVAC, lighting, etc.) |
| Type | Equipment types + warranties | STING tags + COBie type map |
| Component | Individual assets | Tagged elements |
| System | Building systems | SYS parameter groups |
| Assembly | Compositions | Wall/floor layers |
| Connection | MEP connections | Connector graph |
| Attribute | 70+ STING parameters per component | Full parameter export |
| Impact | Environmental impact | Embodied carbon data |
| Document | O&M documents | Document register |
| Resource | Resources | Cost rates |
| Job | Maintenance tasks | SFG20/BS 8210 templates |
| Spare | Spare parts | Per equipment type |
| PickList | Controlled vocabularies | STING-specific code lists |

**22 Project Type Presets**: Commercial Office, Healthcare NHS, Education School, Data Centre, Residential, etc. — each preset configures which COBie sheets and fields are relevant.

**Pre-export checks**:
- Compliance must be ≥60% (configurable)
- Container staleness sampling — offers inline WriteContainers if stale

### 12.3 IFC Export

**Button**: TEMP tab → Data Pipeline → **IFC Export**

Exports with STING shared parameters mapped to IFC property sets. Validates against ISO 16739 Pset requirements.

### 12.4 BCF 2.1 Export/Import

**Export**: BIM tab → **BCF Export**
- BCF 2.1 XML with OrthogonalCamera viewpoints
- Issue metadata from `issues.json`

**Import**: BIM tab → **BCF Import**
- Parses BCF topics, creates issues
- Auto-revision detection

---

## 13. Handover & FM Data

### 13.1 FM Handover Manual

**Button**: DOCS tab → **FM Handover Manual**

Generates comprehensive FM handover documentation:
- Asset register with tag data
- Spatial summary (rooms, areas, departments)
- System descriptions per SYS code
- Compliance report per discipline
- Maintenance schedule references

### 13.2 Asset Health Report

**Button**: DOCS tab → **Asset Health Report**

Scores each asset 0-100 based on:
- Tag completeness
- Parameter population
- Maintenance schedule presence
- Documentation links
- Classification codes

### 13.3 Maintenance Scheduling

**Button**: BIM tab → **IoT/Maintenance** → **Maintenance Schedule**

Generates PPM (Planned Preventative Maintenance) schedules per SFG20/BS 8210:
- 47 job templates from `COBIE_JOB_TEMPLATES.csv`
- Frequency assignment by equipment type
- Asset condition assessment per ISO 15686

### 13.4 O&M Manual

**Button**: DOCS tab → **O&M Manual**

Generates Operations & Maintenance manual structure:
- Per-system documentation
- Manufacturer/model data from TAG7 Section A
- Maintenance requirements from maintenance schedules
- Warranty information from COBie Type data

---

## 14. BEP & Project Governance

### 14.1 BEP Generation

**Button**: BIM tab → **Generate BEP**

Generates a complete BIM Execution Plan from templates:
- **22 project type presets** (matching COBie presets)
- Sections: Project Info, Team, Roles, LOD, CDE, Standards, Deliverables
- COBie data drop schedule (DD1-DD4): per-stage sheets, commands, targets
- Asset management strategy, CAFM integration
- Golden Thread compliance (Building Safety Act 2022)
- Training and competency plan
- Risk register (10 BIM-specific risks)
- TIDP (Task Information Delivery Plan) content

### 14.2 Document Approval Workflow

**Per ISO 19650-2 Section 5.6:**

1. Author creates document (WIP)
2. Author requests approval → `RequestApproval()` creates record
3. Approvers sign off → `SignOff()` records decision
4. On all approvals: document transitions WIP→SHARED
5. Pending approvals visible in Coordination Center

### 14.3 Role-Based Access (ISO 19650)

| Code | Role | CDE Write | Approve | Issue |
|------|------|-----------|---------|-------|
| A | Architect | Models, Drawings | No | No |
| M | MEP Engineer | Models, Schedules | No | No |
| E | Electrical Engineer | Models, Schedules | No | No |
| S | Structural Engineer | Models, Drawings | No | No |
| I | Information Manager | All | Yes | Yes |
| K | BIM Coordinator | All | No | Yes |
| Q | QA/QC Manager | Reports | Yes | No |
| C | Client Representative | None (read-only) | Yes | No |

---

## 15. Reporting & Dashboards

### 15.1 Available Reports

| Report | Button | Format | Content |
|--------|--------|--------|---------|
| Tag Register | CREATE → **Export Tag Register** | CSV | 40+ columns per element |
| Sheet Register | DOCS → **Sheet Register** | CSV | Sheet data with compliance |
| Warning Report | BIM → **Warnings Export** | CSV | 10-column warning data |
| Model Health | BIM → **Model Health** | Dashboard | Health score + recommendations |
| Compliance Dashboard | CREATE → **Completeness %** | Dashboard | Per-discipline RAG breakdown |
| Weekly Coordinator Report | BIM → **Coordinator Report** | HTML | Self-contained corporate report |
| BOQ Export | TEMP → **BOQ Export** | XLSX | Bill of Quantities |
| Drawing Register | DOCS → **Drawing Register** | CSV/Schedule | Sheet listing |
| COBie Spreadsheet | BIM → **COBie Export** | XLSX | 19-sheet FM data |
| 4D Schedule | BIM → **Export 4D Timeline** | CSV | Construction sequence |
| 5D Cost Report | BIM → **Cost Report 5D** | Dashboard | Cost breakdown |

### 15.2 Compliance Trend Tracking

Compliance snapshots are saved daily to `.sting_compliance_trend.json` (90-day rolling window):
- Tag compliance %
- Total elements / tagged count
- Stale count / warning count / placeholder count

7-day trend analysis: improving / stable / declining (shown in morning briefing).

---

## 16. International Standards Reference

| Standard | How STING Implements It |
|----------|------------------------|
| **ISO 19650-1:2018** | CDE state machine, suitability codes, information containers |
| **ISO 19650-2:2018** | BEP generation, data drops DD1-DD4, approval workflows |
| **ISO 19650-3:2020** | Asset tagging, maintenance scheduling, FM handover |
| **BS 1192:2007+A2:2016** | File naming convention, drawing numbering |
| **PAS 1192-2:2013** | CDE workflow, information exchange, project stages |
| **BS EN ISO 16739** | IFC property set validation |
| **COBie V2.4** | Complete 19-worksheet export with 22 project presets |
| **BCF 2.1** | Issue export/import with viewpoints |
| **CIBSE TM40** | SYS→FUNC code mappings for MEP systems |
| **Uniclass 2015** | Classification codes for SYS/FUNC/PROD |
| **SFG20** | Maintenance job templates |
| **BS 8210** | Facility maintenance scheduling |
| **BS EN 1992/1993/1997** | Structural design validation (Eurocodes) |
| **BS 7671** | Electrical circuit protection checking |
| **BS 9999/9991** | Fire safety egress validation |
| **BS 8300** | Accessibility checking |
| **Part L (Building Regs)** | Thermal performance validation |
| **Part M (Building Regs)** | Accessibility compliance |
| **Part B (Building Regs)** | Fire safety compliance |

---

## 17. Troubleshooting

### Common Issues

| Problem | Solution |
|---------|----------|
| Status bar shows "0% compliant" | Run CREATE → **Load Shared Params** first to bind parameters |
| Tags show GEN/XX/ZZ placeholders | Run CREATE → **Resolve All Issues** to fix placeholder tokens |
| COBie export has empty columns | Run ORGANISE → **Tag+Combine** to populate discipline containers |
| Elements tagged but containers empty | Run CREATE → **Combine Parameters** to write to all 36 containers |
| SEQ numbers duplicated after restart | Check `.sting_seq.json` sidecar file exists alongside `.rvt` |
| Auto-tagger not firing | VIEW → **Auto-Tagger** to enable; check discipline filter in config |
| Compliance gate blocking CDE transition | Increase compliance above threshold or override with acknowledgment |
| Warnings baseline shows regression | Compare against baseline: BIM → **Warnings Baseline** |
| Morning briefing not showing | Requires alerts to exist (stale, overdue, warnings) — silent if healthy |
| WorkflowEngine step skipped | Check step conditions (compliance thresholds, stale elements) in JSON |

### Performance Tips

| Tip | Details |
|-----|---------|
| Use **Tag New** not **Batch Tag** | Incremental — only processes untagged elements |
| Use **Retag Stale** not **Re-Tag** | Only processes elements flagged by StingStaleMarker |
| Disable auto-tagger for bulk import | Toggle off before importing 500+ elements |
| Close other views during batch ops | Reduces Revit regeneration overhead |
| Use 200-element batch transactions | Commands auto-chunk for stability |

### Data File Locations

| File | Purpose | Location |
|------|---------|----------|
| `project_config.json` | Per-project tag configuration | Alongside `.rvt` file (preferred) or Data folder |
| `.sting_seq.json` | SEQ counter persistence | Alongside `.rvt` file |
| `.sting_warnings_baseline.json` | Warning baseline | Alongside `.rvt` file |
| `.sting_compliance_trend.json` | Compliance history | Alongside `.rvt` file |
| `.sting_data_hash.json` | Data change detection | Alongside `.rvt` file |
| `_bim_manager/issues.json` | Issue tracker data | `_bim_manager/` subfolder |
| `_bim_manager/meetings.json` | Meeting records | `_bim_manager/` subfolder |
| `_bim_manager/approvals.json` | Approval workflow records | `_bim_manager/` subfolder |
| `STING_WORKFLOW_LOG.json` | Workflow execution history | Alongside `.rvt` file |

---

*End of BIM Coordination Workflow Guide — STING Tools v1.0*
*For tagging-specific details, see TAGGING_GUIDE.md*
*For DWG-to-BIM conversion, see DWG_TO_BIM_GUIDE.md*
*For tag family creation, see TAG_FAMILY_CREATION_GUIDE.md*
