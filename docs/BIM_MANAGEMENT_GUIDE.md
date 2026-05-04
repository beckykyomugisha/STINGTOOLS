# STING Tools — BIM Management, Document & Coordination Workflows Guide

> Step-by-step operational guide for ISO 19650 BIM management, document coordination,
> CDE lifecycle, COBie export, and project delivery using StingTools.
> Version 2.0 — March 2026

---

## Table of Contents

1. [Overview](#1-overview)
2. [BIM Execution Plan (BEP)](#2-bim-execution-plan-bep)
3. [Document Management Center](#3-document-management-center)
4. [CDE Lifecycle & State Machine](#4-cde-lifecycle--state-machine)
5. [Issue Management](#5-issue-management)
6. [Revision Management](#6-revision-management)
7. [COBie V2.4 Export](#7-cobie-v24-export)
8. [Transmittal Management](#8-transmittal-management)
9. [Excel Data Exchange](#9-excel-data-exchange)
10. [Platform Integration (ACC/BCF/SharePoint)](#10-platform-integration-accbcfsharepointlp)
11. [4D Scheduling & 5D Costing](#11-4d-scheduling--5d-costing)
12. [Warnings Management](#12-warnings-management)
13. [BIM Coordination Center](#13-bim-coordination-center)
14. [Workflow Automation Engine](#14-workflow-automation-engine)
15. [Model Health & Compliance](#15-model-health--compliance)
16. [Daily BIM Coordinator Workflows](#16-daily-bim-coordinator-workflows)
17. [ISO 19650 Role-Based Access Control](#17-iso-19650-role-based-access-control)
18. [Performance & Gap Analysis](#18-performance--gap-analysis)

---

## 1. Overview

StingTools BIM Management provides a comprehensive ISO 19650-compliant project coordination platform within Autodesk Revit. All commands are accessible from the **BIM tab** in the dockable panel or the **BIM Coordination Center** unified dialog.

### Core Capabilities

| System | Description |
|--------|-------------|
| **BEP Management** | Create, update, export, and validate BIM Execution Plans |
| **Document Management** | ISO 19650 CDE folder structure, document register, file tracking |
| **Issue Management** | RFI/NCR/SI/CLASH issue tracking with SLA enforcement |
| **Revision Management** | ISO 19650 revision numbering with tag snapshot tracking |
| **COBie Export** | V2.4 compliant spreadsheet generation with 22 project presets |
| **Transmittals** | ISO 19650 information packages with cover sheets |
| **Excel Exchange** | Bidirectional tag data exchange with validation |
| **Platform Integration** | ACC, BCF 2.1, SharePoint, CDE packaging |
| **4D/5D Scheduling** | Auto-generated construction schedules with cost estimation |
| **Warnings Management** | Classified warning system with auto-fix and SLA tracking |
| **Coordination Center** | Unified 13-tab dashboard for project-wide coordination |
| **Workflow Automation** | JSON-based workflow presets with conditional step execution |

### Data Storage

All BIM management data is stored in a `_bim_manager/` folder alongside the `.rvt` file:

```
ProjectName.rvt
ProjectName_bim_manager/
├── issues.json              # Issue register
├── documents.json           # Document register
├── transmittals.json        # Transmittal register
├── meetings.json            # Meeting records + action items
├── approvals.json           # Document approval workflow
├── doc_versions.json        # Document version history
├── schedule_4d.json         # 4D schedule data
├── cost_rates_5d.json       # 5D cost rates
├── snapshots/               # Revision tag snapshots
│   └── snapshot_REV-001.json
├── .sting_seq.json          # SEQ counter sidecar
├── .sting_warnings_baseline.json
├── .sting_compliance_trend.json
└── .sting_data_hash.json
```

---

## 2. BIM Execution Plan (BEP)

### When to Use
- Project initiation — define BIM strategy
- ISO 19650 compliance documentation
- Client deliverable for BEP submission

### Procedure: Create BEP

**Location:** BIM tab → BEP → Create BEP

#### Step 1: Select Project Type Preset

Choose from 22 built-in presets, each pre-configured with appropriate:
- LOD requirements per stage
- COBie data drop schedules
- Discipline-specific requirements
- Regulatory compliance checks

| Preset Category | Presets |
|----------------|---------|
| **Commercial** | Commercial Office, Retail, Hotel, Mixed-Use |
| **Healthcare** | Healthcare NHS, Healthcare Private |
| **Education** | Education School, Education University |
| **Residential** | Residential Standard, Residential High-Rise |
| **Infrastructure** | Transport Station, Transport Airport, Infrastructure Civil, Infrastructure Water |
| **Specialist** | Data Centre, Industrial, Laboratory, Sports/Leisure, Cultural, Defence MOD, Heritage |
| **Construction** | Modular/Off-Site, Fit-Out Interior |

#### Step 2: Configure BEP Sections

The BEP wizard generates these sections:

| Section | Content |
|---------|---------|
| 1. Project Information | Name, number, client, lead designer, BIM manager |
| 2. Project Team | Roles, responsibilities, contact details |
| 3. BIM Goals & Uses | Project-specific BIM objectives |
| 4. BIM Standards | ISO 19650 compliance, naming conventions, classification |
| 5. Information Delivery | MIDP, TIDP, responsibility matrix |
| 6. CDE Strategy | Folder structure, naming convention, access control |
| 7. Software & Formats | Revit version, IFC schema, COBie format |
| 8. LOD Requirements | Per-stage LOD specifications |
| 9. Coordination Procedures | Clash detection, review meetings, RFI process |
| 10. Data Drops | DD1-DD4 schedules with STING command mappings |
| 11. Quality Assurance | Validation procedures, compliance gates |
| 12. Asset Management | COBie strategy, FM integration, Golden Thread |
| 13. Risk Register | 10 BIM-specific risks with mitigation |
| 14. Training Plan | Role-based competency requirements |

#### Step 3: Auto-Enrichment

The BEP is automatically enriched with live model data:
- Current tag compliance percentage (per-discipline breakdown)
- Stage-gated compliance targets
- Risk register entries updated with compliance status
- Data drop schedules with STING commands for each stage

#### Step 4: Export

Export options:
- **JSON** — Machine-readable `project_bep.json`
- **HTML** — Formatted BEP document with corporate styling

### Related Commands

| Command | Description |
|---------|-------------|
| **Update BEP** | Refresh BEP with current model compliance data |
| **Export BEP** | Export BEP to file |
| **Generate BEP** | Auto-generate BEP from project type wizard |
| **Validate BEP** | Check BEP compliance against ISO 19650 requirements |


---

## 3. Document Management Center

### When to Use
- Managing project documents across ISO 19650 CDE states
- Tracking document status, suitability, and revision history
- File import, bulk CDE updates, and transmittal creation

### Opening the Document Management Center

**Location:** BIM tab → Document Center button
- Or from BIM Coordination Center → PLATFORM tab

The Document Management Center is a 7-tab WPF dialog that stays open across multiple operations.

### Interface Layout

```
┌─────────────────────────────────────────────────────────┐
│  STING Document Management Center              [X]      │
├─────────────────────────────────────────────────────────┤
│ [Create Folders] [Import File] [Set Output] [Refresh]   │
│ [Watch Folder]                                          │
├──────────┬──────────────────────────────────────────────┤
│ Navigator│  Dashboard Strip (RAG cards)                 │
│ Tree     │──────────────────────────────────────────────│
│          │ ┌──────────────────────────────────────────┐ │
│ ▸ WIP    │ │ FILE/BULK │ DOCS/CDE │ ISSUES │ REV... │ │
│ ▸ SHARED │ │──────────────────────────────────────────│ │
│ ▸ PUBLISH│ │                                          │ │
│ ▸ ARCHIVE│ │  Document ListView (sortable, filterable)│ │
│ ▸ By Type│ │                                          │ │
│ ▸ By Disc│ │  Right-click → Context Menu              │ │
│          │ │                                          │ │
├──────────┴──┴────────────────────────────────────────────┤
│ Status Bar: 142 documents │ Search: [________] │ Ctrl+L │
└──────────────────────────────────────────────────────────┘
```

### Tab Functions

| Tab | Operations |
|-----|-----------|
| **FILE/BULK** | Import files, create folder structure, bulk rename, bulk move |
| **DOCS/CDE** | Document register, CDE state transitions, suitability codes, bulk CDE update |
| **ISSUES** | Quick issue creation (RFI/NCR/SI), issue list, priority management |
| **REVISIONS** | Create revision, revision dashboard, auto-revision cloud, tag integration |
| **COORDINATION** | Clash detection, BCF export/import, review tracker, Excel exchange |
| **HANDOVER** | COBie export, FM handover, stage gate, tag register, BOQ export |
| **NOTES/BEP** | Sticky notes, BEP generation/update, model health dashboard |

### Key Operations

#### Create ISO 19650 Folder Structure
1. Click **Create Folders** in header
2. Creates standard CDE structure:
   ```
   01_WIP/
   02_SHARED/
   03_PUBLISHED/
   04_ARCHIVE/
   05_MODELS/
   06_DRAWINGS/
   07_SCHEDULES/
   08_COBie/
   09_BEP/
   10_ISSUES/
   11_CLASHES/
   12_HANDOVER/
   ```

#### Import Files
1. Click **Import File** in header
2. Multi-select files (PDF, XLSX, DWG, IFC, BCF, PNG, etc.)
3. Choose destination folder (mapped to CDE state)
4. Files are tracked in `documents.json` with metadata

#### Quick Transmittal
1. Select document items in the list
2. Click **Quick Transmittal** (DOCS/CDE tab)
3. Enter recipient name
4. Auto-generates transmittal record with:
   - Unique TX-NNNN ID
   - Date, document list, creator
   - DRAFT status
   - Status history audit trail

#### Quick Issue Creation
1. Click **Quick Issue** (ISSUES tab)
2. Enter title and select priority
3. Auto-generates issue with:
   - Typed ID (e.g., RFI-0001, NCR-0002)
   - Auto-detected revision and discipline
   - Audit trail

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **F5** | Refresh data |
| **F2** | Rename selected item |
| **Delete** | Delete selected item |
| **Escape** | Close dialog |
| **Ctrl+E** | Export visible items to CSV |
| **Ctrl+L** | Show ISO 19650 code legend |
| **Ctrl+F** | Focus search box |

### Code Legend (Ctrl+L)

Displays comprehensive quick reference for:
- CDE status codes (WIP/SHARED/PUBLISHED/ARCHIVE)
- Suitability codes (S0-S7, CR, AB)
- Document status and type codes
- Issue types (14 BCF + NEC/JCT codes)
- Priority and SLA thresholds
- Discipline codes
- Data drop milestones (DD1-DD4)
- ISO 19650 file naming convention

---

## 4. CDE Lifecycle & State Machine

### ISO 19650 CDE States

```
  ┌──────┐     ┌────────┐     ┌───────────┐     ┌─────────┐
  │  WIP │────▶│ SHARED │────▶│ PUBLISHED │────▶│ ARCHIVE │
  └──────┘     └────────┘     └───────────┘     └─────────┘
      ▲             │                │
      └─────────────┘ (rework)       │
                                     ├──▶ SUPERSEDED
                                     ├──▶ WITHDRAWN
                                     └──▶ OBSOLETE
```

### Valid State Transitions

| From | To | Suitability | Compliance Gate |
|------|----|-------------|-----------------|
| WIP | SHARED | S3 (Coordination) | ≥ 70% tag compliance |
| SHARED | PUBLISHED | S4 (Approval) | ≥ 90% tag compliance |
| SHARED | WIP | (rework) | No gate |
| PUBLISHED | ARCHIVE | IFR (Record) | No gate |
| Any | SUPERSEDED | — | New version exists |
| Any | WITHDRAWN | — | Manual decision |
| Any | OBSOLETE | — | Manual decision |

### Compliance-Gated CDE Transitions

CDE state changes enforce minimum tag compliance:
- **WIP → SHARED**: Blocked below 70% (configurable via `CDE_SHARED_MIN_COMPLIANCE`)
- **SHARED → PUBLISHED**: Blocked below 90% (configurable via `CDE_PUBLISHED_MIN_COMPLIANCE`)

When blocked, shows:
- Per-discipline compliance breakdown
- Stale element count
- Option to override with explicit acknowledgment

### Procedure: Update CDE Status

1. Navigate to **BIM tab → CDE Status**
2. Select elements or documents to transition
3. Choose target CDE state from valid transitions
4. System validates:
   - State transition is valid (one-way progression)
   - Compliance gate is met
   - Terminal state blocking (ARCHIVE cannot be changed)
5. On success:
   - Status history logged with timestamp, old/new states, username
   - Suitability code auto-mapped

### Suitability Codes

| Code | Description | Typical CDE State |
|------|-------------|-------------------|
| S0 | Work In Progress | WIP |
| S1 | Fit for Coordination | WIP → SHARED |
| S2 | Fit for Information | SHARED |
| S3 | Fit for Review | SHARED |
| S4 | Fit for Stage Approval | SHARED → PUBLISHED |
| S5 | Fit for Manufacturing | PUBLISHED |
| S6 | Fit for PIM Authorization | PUBLISHED |
| S7 | Fit for AIM Authorization | PUBLISHED → ARCHIVE |
| CR | As-Constructed Record | ARCHIVE |
| AB | Abandoned | OBSOLETE |

---

## 5. Issue Management

### When to Use
- Tracking RFIs, NCRs, design issues, clashes
- SLA-driven priority management
- Cross-referencing issues to model elements

### Issue Types

| Type | ID Format | Description |
|------|-----------|-------------|
| RFI | RFI-0001 | Request for Information |
| NCR | NCR-0001 | Non-Conformance Report |
| SI | SI-0001 | Site Instruction |
| CLASH | CLASH-0001 | Model coordination clash |
| DESIGN | DESIGN-0001 | Design query or change |
| CHANGE | CHANGE-0001 | Change request |
| RISK | RISK-0001 | Risk item |
| ACTION | ACTION-0001 | Action item |
| SNAGGING | SNAG-0001 | Snagging/defect item |
| SITE | SITE-0001 | Site observation |

### Procedure: Raise an Issue

**Location:** BIM tab → Issues → Raise Issue

1. Click **Raise Issue**
2. Enter issue details:
   - **Title**: Descriptive summary
   - **Type**: Select from issue types above
   - **Priority**: CRITICAL / HIGH / MEDIUM / LOW / INFO
   - **Description**: Full details
   - **Assignee**: Auto-detected from discipline leads config
3. Optionally select model elements to link
4. System auto-generates:
   - Unique typed ID (e.g., RFI-0003)
   - Current revision code
   - Discipline from selected elements' DISC token
   - Created timestamp and creator
5. Issue saved to `issues.json`
6. Notification sent (if configured)

### SLA Enforcement

| Priority | SLA Threshold | Auto-Escalation |
|----------|--------------|-----------------|
| CRITICAL | 4 hours | Auto-escalate to HIGH after SLA breach |
| HIGH | 24 hours | Auto-escalate priority |
| MEDIUM | 7 days (168 hours) | Warning notification |
| LOW | 14 days (336 hours) | Warning notification |

SLA violations are:
- Checked on document open (morning briefing)
- Displayed in BIM Coordination Center overview
- Can auto-create NCR issues from overdue actions

### Procedure: Issue Dashboard

**Location:** BIM tab → Issues → Issue Dashboard

Shows:
- Open / closed / critical / overdue counts
- Issues by type, priority, assignee
- SLA violation list
- Element selection for issue-linked elements

### BCF Integration

Issues can be exported/imported as BCF 2.1:

**Export:** BIM tab → Platform → BCF Export
- Creates `.bcfzip` with viewpoint camera data
- Maps issue types to BCF types
- Includes comments and priority

**Import:** BIM tab → Platform → BCF Import
- Deduplicates against existing issues
- Auto-detects revision
- Maps BCF priorities to STING priorities


---

## 6. Revision Management

### When to Use
- Creating formal revision milestones
- Tracking tag changes between revisions
- Producing revision comparison reports

### Procedure: Create Revision

**Location:** BIM tab → Revisions → Create Revision

#### Step 1: Pre-Revision Compliance Gate
- System checks current tag compliance
- If below 80%, shows warning with:
  - Compliance %, tagged/untagged/stale counts
  - Per-discipline breakdown
  - Options: "Create anyway" or "Cancel — tag first"

#### Step 2: Enter Revision Details
- **Description**: Revision purpose
- **Numbering**: Auto-assigned per ISO 19650:
  - P## (Preliminary): P01, P02 ... P99
  - C## (Construction): C01, C02 ... C99
  - A-Z (As-Built): Single letter series

#### Step 3: Snapshot Creation
- Complete tag state captured for ALL tagged elements (25+ parameters per element)
- Snapshot saved to `snapshots/snapshot_REV-{id}.json`
- Parameters captured:
  - 8 source tokens (DISC through SEQ)
  - TAG1-TAG7 + TAG7A-TAG7F
  - STATUS, REV
  - Phase, Level, Category (for change classification)

#### Step 4: Revision Stamping
- All tagged elements receive the new revision code
- Revision clouds optionally auto-created on modified elements

### Procedure: Revision Compare

**Location:** BIM tab → Revisions → Revision Compare

1. Select two snapshots to compare
2. System calculates deltas per element:
   - **Added**: New elements since previous snapshot
   - **Modified**: Token values changed
   - **Deleted**: Elements removed
3. Changes classified into 5 categories:

| Category | Description | Examples |
|----------|-------------|---------|
| **TOKEN_CHANGE** | Source token modified | DISC changed, SYS changed |
| **CONTAINER_REGEN** | Discipline container regenerated | HVC_EQP_TAG updated |
| **NARRATIVE_CHANGE** | TAG7 sub-section changed | TAG7A identity updated |
| **STATUS_CHANGE** | STATUS or REV modified | NEW → EXISTING |
| **TAG_REFORMAT** | TAG1-TAG6 reformatted | Separator change |

4. Report shows:
   - Per-discipline change summary
   - Significance classification (Minor/Standard/Major)
   - Element selection for changed items

### Revision Significance Classification

| Level | Criteria |
|-------|---------|
| **Minor** | 0-5 changes, tag modifications only |
| **Standard** | 6-20 changes, structural changes (SYS/FUNC) |
| **Major** | >20 changes, identity changes (DISC/PROD), >5 deletions |

### Other Revision Commands

| Command | Description |
|---------|-------------|
| **Revision Dashboard** | View all revisions with change analytics |
| **Auto Revision Cloud** | Auto-create revision clouds on modified elements |
| **Revision Schedule** | Create Revit schedule from revision data |
| **Track Element Revisions** | Per-element revision history |
| **Issue Sheets for Revision** | Associate revision with sheet set |
| **Revision Naming Enforce** | Validate revision naming convention |
| **Auto Revision on Tag Change** | Auto-stamp elements when tags change |
| **Revision Export** | Export revision data to CSV/JSON |
| **Bulk Revision Stamp** | Apply revision to batch of elements |

---

## 7. COBie V2.4 Export

### When to Use
- FM/O&M data drops (DD1-DD4)
- Contractual COBie deliverables
- CAFM system population

### Procedure: COBie Export

**Location:** BIM tab → Handover → COBie Export

#### Step 1: Pre-Export Compliance Gate
- Blocks export below 60% tag compliance
- Shows breakdown: tagged/untagged/stale/placeholders
- User can override with explicit acknowledgment

#### Step 2: Container Staleness Check
- Samples elements for TAG1 with empty discipline containers
- Offers to run WriteContainers inline before export
- Ensures containers match current token values

#### Step 3: Select Preset

22 project type presets, each configuring:
- Required COBie worksheets
- Asset type emphasis
- Maintenance requirements
- Regulatory references

Example presets:
- **Healthcare NHS**: Medical gas, infection zones, HTM compliance
- **Data Centre**: Power distribution, cooling, Uptime Institute
- **Commercial Office**: BCO Guide, BREEAM, lease demise
- **Residential**: Part L/M/B, plot numbering, sales schedules

#### Step 4: Configure Sheets

| Sheet | Content | Source |
|-------|---------|--------|
| **Instruction** | Generation metadata, preset, colour coding | Auto-generated |
| **Contact** | Project team contacts | BEP / Project Info |
| **Facility** | Building data | Project Information |
| **Floor** | Level data | Revit Levels |
| **Space** | Room data | Revit Rooms |
| **Zone** | Zone classifications | 16 zone types |
| **Type** | Equipment types with specs | Families + STING tokens |
| **Component** | Asset instances | Elements + tags |
| **System** | Building systems | Grouped by SYS token |
| **Assembly** | Compound elements | Walls/floors with layers |
| **Connection** | MEP connections | Connector graph |
| **Spare** | Spare parts | COBIE_SPARE_PARTS.csv |
| **Resource** | Resources | COBIE_JOB_TEMPLATES.csv |
| **Job** | Maintenance tasks | SFG20/BS 8210 templates |
| **Impact** | Environmental impact | Embodied carbon data |
| **Document** | O&M documents | COBIE_DOCUMENT_TYPES.csv |
| **Attribute** | Extended attributes | 70+ STING parameters |
| **Coordinate** | Element coordinates | XYZ positions |
| **Issue** | Open issues | issues.json |
| **PickLists** | Controlled vocabularies | COBIE_PICKLISTS.csv |

#### Step 5: Export
- ClosedXML generates `.xlsx` file
- Summary shows component count, system count, type count
- Instruction sheet includes:
  - Source revision and compliance %
  - Export timestamp and model title
  - Column colour coding guide

### COBie Round-Trip Import

**Location:** BIM tab → Handover → COBie Import

Reads COBie V2.4 Component worksheet and updates Revit elements:
- Matches by UniqueId (exact) or TAG1 (fallback)
- Updates: Description, SerialNumber, BarCode, AssetIdentifier, Warranty, InstallationDate
- Supports `CLEAR` sentinel for intentional emptying
- 10K row safety limit

### Streaming COBie Export

For very large models (50K+ elements):
- Processes in configurable batch size (default 5000, via `COBIE_STREAM_BATCH_SIZE`)
- Reduces memory pressure
- Progress dialog with cancellation

---

## 8. Transmittal Management

### When to Use
- Formal document exchange per ISO 19650
- Information packages for CDE submission
- Audit trail of document deliveries

### Procedure: Create Transmittal

**Location:** BIM tab → Transmittals → Create Transmittal

1. Select documents to include
2. Enter recipient and purpose
3. System generates:
   - Unique TX-NNNN ID
   - Cover sheet with document list
   - SHA256 hash for integrity verification
   - Status history (DRAFT → SENT → ACKNOWLEDGED)
4. Saved to `transmittals.json`

### Compliance-Gated Transmittals

Transmittal creation can be gated on compliance:
- Blocks if tag compliance below threshold
- Shows warning with option to override

---

## 9. Excel Data Exchange

### When to Use
- External stakeholder review of tag data
- Bulk corrections in spreadsheet form
- FM team data population

### Procedure: Full Round-Trip

**Location:** BIM tab → Excel section

#### Step 1: Export
1. Click **Export to Excel**
2. Choose scope (selection or project)
3. Generated Excel contains:
   - **30+ columns**: Identity (read-only), tokens (editable), geometry, context
   - **Data validation**: Dropdown lists for DISC, SYS, LOC, ZONE, FUNC, PROD codes
   - **Conditional formatting**: Pale red for empty tokens
   - **Read-only protection**: Grey background on identity columns
   - **Metadata sheet**: Export date, project GUID, element count, version

#### Step 2: Edit in Excel
- Modify editable token columns
- DISC, LOC, ZONE, SYS, FUNC, PROD have dropdown validation
- Type `CLEAR` to intentionally empty a field
- Do NOT modify read-only columns

#### Step 3: Import
1. Click **Import from Excel**
2. Select edited file
3. Validation runs:
   - Individual token validation (code list checks)
   - Cross-token validation (FUNC↔SYS, DISC↔SYS consistency)
   - Change preview with before/after values
4. On confirmation:
   - TypeTokenInherit + PopulateAll + NativeMapper + FormulaEngine
   - BuildAndWriteTag with collision detection
   - Audit trail captured
   - SEQ sidecar saved

#### One-Click Round-Trip
Click **Round-Trip** to export → open Excel → wait for edit → import changes in one flow.

### Other Excel Commands

| Command | Description |
|---------|-------------|
| **Export Schedules to Excel** | Export all ViewSchedules |
| **Import Schedules from Excel** | Import schedule data back |
| **Export Template** | Blank template with validation lists |


---

## 10. Platform Integration (ACC/BCF/SharePoint)

### ACC/BIM 360 Publishing

**Location:** BIM tab → Platform → ACC Publish

Packages project deliverables for Autodesk Construction Cloud:
- Collects all STING deliverables (BEP, registers, COBie, model health)
- Generates deliverable package with SHA256 integrity hashing
- ISO 19650 file naming validation

### BCF 2.1 Export/Import

**Export:** BIM tab → Platform → BCF Export
- Creates `.bcfzip` with BCF 2.1 compliant topics
- Includes orthogonal camera viewpoints (CameraViewPoint, Direction, UpVector, ViewScale)
- Maps STING issue types to BCF types (RFI→Request, CLASH→Clash, NCR→Issue)
- Includes comments and priority mapping

**Import:** BIM tab → Platform → BCF Import
- Reads `.bcfzip` files from external tools (Navisworks, Solibri, etc.)
- Deduplication against existing issues (by GUID)
- Auto-detects project revision
- Creates new issues with full BCF metadata

### CDE Package Generation

**Location:** BIM tab → Platform → CDE Package

Creates ISO 19650 CDE manifest:
```json
{
  "schema_version": "1.0",
  "standard": "BS EN ISO 19650",
  "project": { ... },
  "deliverables": [
    {
      "filename": "...",
      "type": "BEP",
      "sha256": "...",
      "cde_state": "SHARED"
    }
  ]
}
```

### SharePoint Export

**Location:** BIM tab → Platform → SharePoint Export

Pushes deliverables with:
- `metadata.xml` — SharePoint column definitions
- `index.html` — Dashboard with summary cards and styled tables
- ISO 19650 file naming enforcement

### Platform Sync

**Location:** BIM tab → Platform → Platform Sync

Bidirectional delta sync with external platforms:
- Detects changes since last sync
- Pushes new/modified deliverables
- Pulls external updates
- Reports sync delta (added/modified/unchanged)

---

## 11. 4D Scheduling & 5D Costing

### When to Use
- Auto-generating construction schedules from model element counts
- Estimating project costs from category-based rates
- Timeline visualization and milestone tracking

### Procedure: Auto-Schedule 4D

**Location:** BIM tab → 4D/5D → Auto Schedule 4D

#### How It Works

1. **Collect elements** — Filter by phase (excludes demolished/temporary)
2. **Group by trade** — 40 construction trades in weighted sequence:

| Weight | Phase | Trades |
|--------|-------|--------|
| 0-130 | Substructure | Excavation → Piling → Foundations → Ground Beams → Basement → DPC → Membrane |
| 100-230 | Superstructure | Framing → Columns → Floors → Concrete Topping |
| 300-320 | Envelope | Walls → Curtain Panels → Roofs |
| 400-410 | External | Windows → Doors |
| 500 | Interior | Ceilings |
| 600-650 | MEP 1st Fix | Ducts → Pipes → Cable Trays → Sprinklers |
| 700-730 | MEP Equipment | Mechanical → Electrical → Plumbing → Lighting |
| 800-850 | MEP 2nd Fix | Air Terminals → Electrical → Comms → Fire Alarm → Security |
| 900-930 | FF&E | Furniture → Systems → Equipment → Casework |
| 950-980 | Completion | Commissioning → Handover |

3. **Generate tasks per level** — Bottom-to-top construction sequence
4. **Calculate durations** — Base days × (1 + element_count/50), min 1, max 30 days
5. **Apply sequencing logic**:
   - Structure tasks: sequential
   - MEP tasks: 50% overlap with previous
   - Finishes: parallel where possible
   - 2-day buffer between levels
6. **Add completion tasks**: Testing (14d) → Snagging (7d) → Handover (5d)

#### Output
- Schedule saved to `schedule_4d.json`
- Each task includes: WBS, name, start/finish dates, duration, element count, predecessors

### Procedure: Auto-Cost 5D

**Location:** BIM tab → 4D/5D → Auto Cost 5D

1. Loads cost rates from `cost_rates_5d.csv` (180+ categories)
2. Matches Revit elements to cost rates by category
3. Applies unit rates:
   - Per-metre rates for linear elements (ducts, pipes, walls)
   - Per-item rates for discrete elements (equipment, fixtures)
   - Per-m² rates for area elements (floors, ceilings)
4. Generates cost report:
   - Subtotal by category
   - Preliminaries (configurable %, default from `COST_PRELIMINARIES_PCT`)
   - Contingency (configurable %, default from `COST_CONTINGENCY_PCT`)
   - Overhead & Profit (configurable %, default from `COST_OVERHEAD_PROFIT_PCT`)
   - Grand total

### MS Project Import/Export

**Import:** BIM tab → 4D/5D → Import MS Project
- Reads MS Project XML with ISO 8601 duration parsing
- Preserves task hierarchy, predecessors, WBS
- Auto-links tasks to model elements by category/level matching

**Export:** BIM tab → 4D/5D → Export Schedule 4D
- Exports schedule to CSV/JSON for external tools

### Other 4D/5D Commands

| Command | Description |
|---------|-------------|
| **View Timeline 4D** | Gantt-style timeline visualization |
| **Cost Report 5D** | Detailed cost breakdown report |
| **Cash Flow 5D** | Cash flow forecast by month |
| **Phase Filter** | Filter model elements by schedule phase |
| **Phase Summary** | Summary statistics per phase |
| **Milestone Register** | Track project milestones |
| **Working Calendar** | Configure working days/holidays |

---

## 12. Warnings Management

### When to Use
- Managing Revit model warnings systematically
- Prioritizing fix actions by severity and deliverable impact
- Auto-fixing common warning types
- Tracking warning trends against baselines

### Opening the Warnings Manager

**Location:** BIM tab → Warnings Manager section (8 buttons)

### Warning Classification

All Revit warnings are automatically classified into:

| Category | Examples | Typical Severity |
|----------|---------|-----------------|
| **Geometric** | Overlaps, off-axis, duplicate instances | Medium-High |
| **Spatial** | Rooms not enclosed, multiple rooms | Critical-High |
| **MEP** | Unconnected pipes/ducts, undefined systems | High |
| **Structural** | Analytical alignment, deflection, bearing | Medium-Critical |
| **Annotation** | Tag/dimension issues, hidden elements | Low-Info |
| **Data** | Duplicate marks, formula errors | High-Medium |
| **Performance** | DWG imports, in-place families, groups | Medium-Low |
| **Compliance** | Fire rating, accessibility, energy code | High-Critical |

### Procedure: Warnings Dashboard

**Location:** BIM tab → Warnings → Dashboard

Shows:
- Total warnings with trend vs baseline (↑/↓/→)
- Severity breakdown (Critical/High/Medium/Low/Info)
- Category breakdown with counts
- Discipline/level/workset breakdown
- Auto-fixable vs manual-review counts
- Top 10 hotspot elements (most warnings per element)
- Warning health score (0-100)

### Procedure: Auto-Fix Warnings

**Location:** BIM tab → Warnings → Auto Fix

1. Scans all model warnings
2. Filters to auto-fixable warnings
3. Shows preview of fix strategies:

| Strategy | Warning Type | Fix Action |
|----------|-------------|-----------|
| 1 | Duplicate instances | Delete duplicate at same location |
| 2 | Room separation overlap | Delete shorter line |
| 3 | Duplicate marks | Auto-increment suffix (_2, _3, ...) |
| 4 | Unjoined geometry | Unjoin non-intersecting elements |
| 5 | Overlapping walls | Auto-join via JoinGeometryUtils |
| 6 | Room tags outside boundary | Move to room center |
| 7 | Elements off axis | Snap to nearest cardinal direction |
| 8 | Zero-length elements | Delete (<3mm) |
| 9 | Duplicate mark collision | Collision-safe suffix with HashSet |

4. Executes fixes in single transaction
5. **Verification**: Re-scans warnings after fix to confirm reduction
6. Reports: attempted, fixed, skipped, failed, net reduction

### Warning SLA Tracking

Warnings have severity-based SLA thresholds:
- **Critical**: 4 hours
- **High**: 24 hours
- **Medium**: 7 days
- **Low**: 14 days

Per-warning first-seen timestamps are tracked in extended baseline for individual SLA calculation.

### Warning Deliverable Impact

`AnalyseDeliverableImpact()` maps warnings to 5 deliverable areas:
- **COBie**: Data quality warnings affect export accuracy
- **IFC**: Geometric warnings affect model exchange
- **FM Handover**: Spatial/MEP warnings affect asset data
- **Schedules**: Data warnings affect schedule accuracy
- **Clash Detection**: Geometric/MEP warnings create false clashes

### Other Warning Commands

| Command | Description |
|---------|-------------|
| **Export Warnings** | CSV export (10 columns) for external tracking |
| **Save Baseline** | Save current warning count for trend tracking |
| **Select Elements** | Pick warning type → select affected elements |
| **Suppress Warnings** | Add patterns to suppression list |
| **Compliance Report** | ISO 19650 / CIBSE / BS 7671 mapping |
| **Monitor Warnings** | Pre/post-command regression detection |


---

## 13. BIM Coordination Center

### When to Use
- Daily BIM coordinator dashboard
- Project-wide health overview
- Cross-system coordination (tags, warnings, issues, revisions)

### Opening

**Location:** BIM tab → Coordination Center button

The BIM Coordination Center is a unified 13-tab WPF dialog that stays open across operations.

### Tab Overview

| Tab | Content | Key Metrics |
|-----|---------|-------------|
| **OVERVIEW** | Project health at a glance | 5 KPI cards, compliance forecast, action required |
| **MODEL HEALTH** | Element and data quality | Health score (0-100), actionable check list |
| **WARNINGS** | Warning analysis and management | Interactive TreeView, severity breakdown |
| **ISSUES** | Issue tracking with DataGrid | Open/closed/overdue, SLA violations |
| **REVISIONS** | Version history | Revision timeline, change counts |
| **PLATFORM** | External system sync | 7 platforms, sync status |
| **WORKFLOWS** | Automation run history | Quick presets, execution DataGrid |
| **QA DASHBOARD** | Validation and anomaly detection | Token coverage matrix, placeholder counts |
| **4D/5D** | Scheduling and costing | KPI cards, cost breakdown, milestones |
| **DELIVERABLES** | ISO 19650 data drop tracking | DD1-DD4 readiness, deliverable status |
| **COORD LOG** | Action audit trail | Searchable, filterable log |
| **TEAM** | Workload visualization | Stacked bar chart, per-member metrics |
| **MEETINGS** | Meeting coordination | Upcoming meetings, action items, automation rules |

### Overview Tab — Key Features

#### 5 KPI Cards
- **Total Elements**: Count with breakdown
- **Tag Compliance %**: With RAG status bar
- **Warnings**: Total with health score
- **Open Issues**: With critical/overdue breakdown
- **Container Compliance**: Discipline container completeness

#### Compliance Forecast
Projects compliance 3 cycles ahead using linear trend from last 5 workflow runs:
- Shows trending up/down/stable with projected percentage

#### Action Required Panel
Priority-sorted clickable action items:
- Stale elements → Retag Stale command
- Overdue issues → Issue Dashboard
- Critical warnings → Warnings Auto-Fix
- Untagged elements → Tag New Only
- Placeholder tokens → Resolve All Issues
- SLA violations → Issue update

### Interactive Features

| Feature | Description |
|---------|-------------|
| **Double-click discipline row** | Select all elements of that discipline |
| **Double-click warning** | 3D section box zoom to affected elements |
| **Double-click issue** | 3D section box zoom and element selection |
| **Right-click context menus** | Zoom to 3D, Select Elements, Update Status |
| **Hover tooltips** | Drill-down details on all KPI cards |
| **F5** | Refresh all data |
| **Ctrl+E** | Export current tab data |

### 3D Section Box Zoom

Double-clicking warnings, issues, or hotspot elements:
1. Creates/reuses a `STING - Section Box Zoom` 3D view
2. Computes aggregate bounding box across affected elements
3. Adds 3ft padding around elements
4. Sets section box for focused viewing

### Cross-System Automation Rules (MEETINGS tab)

6 automation rules with real-time status evaluation:

| Rule | Trigger | Action |
|------|---------|--------|
| **Overdue Action → Issue Escalation** | Action item past due date | Auto-create HIGH-priority NCR |
| **Open Issues → Next Meeting Agenda** | Issues exist before meeting | Auto-populate meeting agenda |
| **Compliance Gate → Transmittal Trigger** | Compliance ≥80%, containers ≥80%, 0 critical warnings | Auto-create SHARED transmittal |
| **Meeting Closure → Follow-Up Scheduling** | Meeting has open actions | Auto-schedule follow-up |
| **SLA Violation → Priority Escalation** | SLA threshold exceeded | Auto-escalate issue priority |
| **Stale Elements → Auto-Retag** | Elements moved/changed | Auto-retag stale elements |

---

## 14. Workflow Automation Engine

### When to Use
- Automating repetitive multi-step BIM coordination tasks
- Enforcing consistent QA procedures
- Reducing manual effort for daily/weekly routines

### Architecture

Workflows are JSON-based command sequences with conditional step execution:

```json
{
  "name": "DailyQA",
  "description": "Daily quality assurance routine",
  "rollbackOnFailure": false,
  "steps": [
    {
      "commandTag": "RetagStale",
      "label": "Fix stale elements",
      "optional": true,
      "condition": "has_stale"
    },
    {
      "commandTag": "TagNewOnly",
      "label": "Tag new elements",
      "optional": false,
      "condition": "has_untagged"
    },
    {
      "commandTag": "ValidateTags",
      "label": "Validate all tags",
      "optional": false,
      "maxCompliancePct": 95
    }
  ]
}
```

### Step Conditions

| Condition | Skip When |
|-----------|----------|
| `has_stale` | No stale elements exist |
| `has_untagged` | No untagged elements exist |
| `has_warnings` | Zero model warnings |
| `has_critical_warnings` | No critical-severity warnings |
| `has_open_issues` | No open issues |
| `has_links` | No Revit links |
| `has_cad_imports` | No CAD imports |
| `has_placeholders` | No GEN/XX/ZZ/0000 tokens |
| `has_container_gaps` | Containers ≥95% complete |
| `compliance_above_90` | Already ≥90% compliant |
| `compliance_below_50` | Model too early-stage |
| `workshared` | Model is not workshared |

### Step Properties

| Property | Description |
|----------|-------------|
| `commandTag` | Dispatch key (130+ available commands) |
| `label` | Display name |
| `optional` | Can fail without halting workflow |
| `retryCount` | 0-3 retry attempts for transient failures |
| `retryDelayMs` | Delay between retries (default 500ms) |
| `timeoutSeconds` | Max execution time (default 300s) |
| `minCompliancePct` | Skip if compliance below threshold |
| `maxCompliancePct` | Skip if compliance above threshold |
| `requiresStaleElements` | Skip if no stale elements |
| `skipIfPreviousSkipped` | Cascade skip from prior step |
| `minWarningHealthScore` | Skip if health exceeds threshold |
| `requiresWorksharedModel` | Skip if not workshared |
| `minElementCount` / `maxElementCount` | Element count range |

### Built-in Workflow Presets

| Preset | Steps | Use Case |
|--------|-------|----------|
| **ProjectKickoff** | 6+ | Full project initialization |
| **DailyQA** | 8 adaptive | Daily coordinator QA routine |
| **MorningHealthCheck** | 10 | BIM coordinator morning startup |
| **PostTaggingQA** | 5 | After tagging session validation |
| **WeeklyDataDrop** | 10 | ISO 19650 information exchange |
| **DocumentPackage** | 6 | Documentation production |
| **ModelAuditDeep** | 8 | Comprehensive model audit |
| **MEPCoordination** | 6 | MEP system coordination |
| **CDE_Submission** | 8 | CDE document submission |
| **DesignReviewPrep** | 5 | Pre-review preparation |
| **HandoverReadiness** | 9 | Pre-handover validation |
| **IssueResolution** | 4 | Retag → fix → resolve → validate |
| **ClientReviewPrep** | 5 | Client presentation preparation |
| **RegulatoryScan** | 5 | Standards compliance check |
| **EndOfDaySync** | 8 | End-of-day save and export |
| **FederatedModelAudit** | 7 | Multi-model coordination |
| **PreMeetingPrep** | 7 | Pre-meeting data preparation |
| **Healthcare_NHS** | Sector-specific | NHS-specific BIM requirements |
| **DataCentre** | Sector-specific | Data centre BIM requirements |
| **CommercialOffice** | Sector-specific | Commercial office BIM |
| **Residential** | Sector-specific | Residential BIM |
| **Education** | Sector-specific | Education sector BIM |

### Procedure: Run a Workflow

1. Navigate to **BIM tab → Workflows** or **Coordination Center → WORKFLOWS tab**
2. Choose preset from quick-start buttons or dropdown
3. Pre-flight check validates:
   - Element count meets step requirements
   - Worksharing requirements satisfied
   - All command tags resolve to actual commands
   - Data directory accessible
4. Workflow executes with:
   - Per-step progress reporting
   - Escape key cancellation between steps
   - Compliance cache updated after each step
   - Transaction group with optional rollback
5. Results show:
   - Per-step pass/fail/skip status with duration
   - Before/after compliance delta
   - Total execution time
   - Error messages for failed steps

### Workflow Run Records

Execution history saved to `STING_WORKFLOW_LOG.json`:
- Timestamp, preset name, user
- Per-step results (tag, label, status, duration, error)
- Compliance before/after
- Capped at 100 records (JSONL rotation)

### Last Workflow Memory

- Last executed workflow name, result, and time persisted
- "Repeat Last Workflow" button in Coordination Center
- Saved to `project_config.json` for cross-session persistence


---

## 15. Model Health & Compliance

### Model Health Scoring

**Location:** BIM tab → Model Health → Dashboard

Weighted 0-100 score across 4 categories (25 points each):

| Category | Factors | Weight |
|----------|---------|--------|
| **Warnings** | Warning count, severity distribution, auto-fixable ratio | 25 |
| **Compliance** | Tag compliance %, strict %, placeholder count | 25 |
| **Data Quality** | Container completeness, TAG7 coverage, STATUS population | 25 |
| **Performance** | Element count, group count, linked model count | 25 |

RAG status: GREEN (≥80), AMBER (50-80), RED (<50)

### Compliance Scan

The live compliance scan provides:

| Metric | Description |
|--------|-------------|
| **CompliancePercent** | Tagged elements / total elements |
| **StrictPercent** | Fully resolved elements (no placeholders) / total |
| **ContainerCompletePct** | Elements with all applicable containers populated |
| **RevisionPercent** | Elements with revision codes |
| **PlaceholderCount** | Elements with GEN/XX/ZZ/0000 tokens |
| **StaleCount** | Elements with STING_STALE_BOOL = 1 |
| **ByDisc** | Per-discipline breakdown |
| **ByPhase** | Per-construction-phase breakdown |
| **EmptyTokenCounts** | Per-token empty/placeholder counts |

### Compliance Trend Tracking

Daily compliance snapshots saved to `.sting_compliance_trend.json`:
- 90-day rolling window
- 7-day trend direction (improving/stable/declining)
- Used for compliance forecasting in Coordination Center

### Data Drop Readiness

**Location:** BIM tab → Deliverables → Data Drop Readiness

Assesses model against DD1-DD4 milestones per PAS 1192-2:

| Milestone | Required Compliance | Required Data |
|-----------|-------------------|---------------|
| **DD1** (Brief) | ≥30% | Basic spatial data, room types |
| **DD2** (Concept) | ≥60% | System types, equipment types |
| **DD3** (Design) | ≥85% | Full tag data, COBie types |
| **DD4** (Handover) | ≥95% | Complete COBie, O&M data |

### Morning Briefing

On document open, if alerts exist, shows:
- Tag compliance with RAG status
- 7-day trend direction
- Stale element count
- Model warning count
- SLA violation summary
- One-click "Run Morning Health Check" button
- Silent when model is healthy (no dialog)

---

## 16. Daily BIM Coordinator Workflows

### Morning Routine

```
08:30  Open model
       ↓ Morning briefing dialog shows automatically
       ↓ Review: compliance %, stale count, SLA violations
       
08:35  Run MorningHealthCheck workflow (one-click)
       Steps: Retag stale → Warnings auto-fix → Tag new → Pre-tag audit
              → Validate → Template assign → Sheet numbering → Revision check
       
08:45  Open BIM Coordination Center
       ↓ Review OVERVIEW tab: KPI cards, action required items
       ↓ Check WARNINGS tab: any new critical warnings?
       ↓ Check ISSUES tab: any SLA violations?
       
09:00  Address critical items from action required panel
       ↓ Click action items to dispatch fix commands
       
09:15  Review continues with normal modeling/coordination
```

### Pre-Meeting Routine

```
Run PreMeetingPrep workflow:
  1. Clear stale elements
  2. Auto-fix warnings
  3. Validate tags
  4. Generate warning summary
  5. Review issues
  6. Check revisions
  7. Generate HTML coordinator report
```

### End-of-Day Routine

```
Run EndOfDaySync workflow:
  1. Retag stale elements
  2. Validate tags
  3. Save warning baseline
  4. Export tag register
  5. Export sheet register
  6. Run model health check
  7. Export warnings
  8. Create revision
```

### Weekly Data Drop

```
Run WeeklyDataDrop workflow:
  1. Retag stale → Resolve placeholders → Validate
  2. Audit CSV data → COBie export → Excel export
  3. Sheet compliance → Sheet register → Model health
  4. Full compliance dashboard
```

### Pre-Handover Checklist

```
Run HandoverReadiness workflow:
  1. Retag all stale elements
  2. Full batch tag
  3. Validate ISO 19650 compliance
  4. Validate template assignments
  5. Export COBie V2.4
  6. Generate drawing register
  7. Export BOQ
  8. Update BEP
  9. Create handover revision
```

---

## 17. ISO 19650 Role-Based Access Control

### Roles

| Code | Role | CDE Access | Can Approve | Can Issue |
|------|------|-----------|-------------|-----------|
| A | Architect | WIP, SHARED | Yes | Yes |
| M | Mechanical Engineer | WIP, SHARED | No | Yes |
| E | Electrical Engineer | WIP, SHARED | No | Yes |
| S | Structural Engineer | WIP, SHARED | No | Yes |
| H | Client / Employer | SHARED, PUBLISHED | Yes | Yes |
| P | Project Manager | WIP, SHARED | Yes | Yes |
| C | BIM Coordinator | WIP, SHARED, PUBLISHED | Yes | Yes |
| I | Information Manager | All | Yes | Yes |
| K | Contractor | SHARED | No | No |
| Q | QS / Cost Consultant | SHARED | No | No |
| F | FM / Facilities | PUBLISHED, ARCHIVE | No | No |
| W | Specialist / Sub-contractor | WIP | No | No |
| L | Lead Designer | WIP, SHARED | Yes | Yes |
| Z | General / Unassigned | WIP | No | No |

### CDE Folder Permissions

| Folder | Read Roles | Write Roles | Approve Roles |
|--------|-----------|-------------|---------------|
| 01_WIP | A,M,E,S,P,C,I,W,L | A,M,E,S,C,I,W,L | I,C |
| 02_SHARED | All | C,I,L | I,C,P |
| 03_PUBLISHED | All | I | I,H |
| 04_ARCHIVE | All | I | I |
| 05_MODELS | A,M,E,S,C,I | A,M,E,S,C | I,C |
| 06_DRAWINGS | All | A,M,E,S,C | I,C,P |
| 08_COBie | H,P,C,I,F | C,I | I,H |

### Setting Your Role

1. Open BIM Coordination Center → PERMISSIONS tab
2. Click **Edit Role**
3. Select your ISO 19650 role
4. CDE permission preview shows your access
5. Saved to `USER_ROLE` in `project_config.json`

---

## 18. Performance & Gap Analysis

### Performance Recommendations

| Area | Recommendation | Impact |
|------|---------------|--------|
| **COBie Export** | Use streaming export for 50K+ element models | Prevents memory exhaustion |
| **Compliance Scan** | Incremental update (O(1)) vs full scan (O(n)) | Reduces dashboard update from 3s to <1ms |
| **Container Writes** | Selective write by discipline prefix | 60-80% fewer writes per element |
| **Formula Evaluation** | 5-minute TTL session cache | Prevents 40+ redundant CSV reads |
| **Grid Lines** | 2-minute TTL cache | Prevents repeated FilteredElementCollector scans |
| **Workflow Engine** | Cached stale/compliance checks per workflow | Avoids repeated scans between steps |
| **Warning Scans** | Root-cause grouping reduces 200 warnings to 1 group | More actionable dashboard |
| **Excel Import** | 10K row guard with header auto-detection | Prevents memory/performance issues |

### Identified Efficiency Gaps

| Gap | Description | Recommendation |
|-----|-------------|----------------|
| **Manual meeting scheduling** | Meetings require manual creation | Auto-schedule recurring coordination meetings from BEP |
| **No real-time multi-user sync** | Single-machine data storage | Deploy StingBIM Server for cloud-based multi-user sync |
| **Warning baseline manual** | Must manually save baseline | Auto-save baseline on document close and revision creation |
| **No automated email/Teams notifications** | Notification framework exists but requires external config | Integrate with Microsoft Graph API for Teams notifications |
| **COBie import limited** | Only Component sheet import | Extend to Type, System, Job worksheet import |
| **4D schedule manual linking** | Task-to-element matching by category | Implement Navisworks TimeLiner export for visual 4D |
| **Document version tracking** | Version engine exists but UI limited | Add visual timeline/diff view for document version history |
| **Clash detection basic** | Bounding box intersection only | Integrate with Navisworks for geometry-accurate clash detection |
| **SLA thresholds fixed** | 4h/24h/7d/14d hardcoded | Make SLA thresholds configurable via project_config.json |
| **No dashboard export** | Coordination Center is view-only | Add PDF/HTML export of current dashboard state |

### Alignment Recommendations

| Area | Current State | Recommended Improvement |
|------|--------------|------------------------|
| **BEP ↔ Compliance** | BEP enriched with compliance data | Auto-validate BEP compliance targets vs actual per RIBA stage |
| **Issues ↔ Revisions** | Issues and revisions tracked separately | Auto-link issue resolution to revision snapshots |
| **Warnings ↔ COBie** | Impact analysis identifies COBie-affecting warnings | Block COBie export when critical warnings affect data quality |
| **Workflows ↔ Data Drops** | Workflows can run any command sequence | Create data-drop-specific workflows auto-configured from BEP |
| **Meetings ↔ Issues** | Cross-system automation exists | Auto-generate meeting minutes from issue resolution activities |
| **Tags ↔ Revisions** | Tag snapshots captured per revision | Add diff visualization between tag revisions |

---

*This guide reflects the StingTools codebase as of March 2026. All procedures, commands, and configuration options are subject to change. Refer to the CLAUDE.md file for the authoritative technical reference.*

