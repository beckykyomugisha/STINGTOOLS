# STING Documentation — Integration Index & Navigation Guide

> **Start here.** This index shows every guide in `docs/guides/`, what it covers, what
> it depends on, and what depends on it. Use it to find the right guide for your task
> and to understand the reading order before starting a new workflow.

---

## Reading Order (for a new user)

If you are new to STING, read the guides in this order:

```
1. MEP_FOUNDATION_GUIDE.md          ← Shared foundation. Read this first.
        ↓
2. DRAWING_PRODUCTION_SYSTEM_GUIDE.md  ← How drawings are produced.
        ↓
3. DOCUMENT_MANAGER_GUIDE.md           ← How documents are issued.
        ↓
Then pick your discipline:
   ├── ELECTRICAL_WORKFLOW_GUIDE.md
   ├── PLUMBING_WORKFLOW_GUIDE.md
   └── HEALTHCARE_WORKFLOW_GUIDE.md    ← Also requires the two above.
```

---

## Guide Inventory

### 1. MEP_FOUNDATION_GUIDE.md
**What it covers:** MEP symbol creation in the Revit Family Editor (step-by-step), wire annotation symbol creation and placement, placement family authoring rules, and the complete Placement Centre workflow including rehosting elements.

**Depends on:** Nothing. This is the foundation.

**Referenced by:**
- ELECTRICAL_WORKFLOW_GUIDE.md (symbols, wire annotation, placement)
- PLUMBING_WORKFLOW_GUIDE.md (symbols, family authoring, placement)
- HEALTHCARE_WORKFLOW_GUIDE.md (clinical equipment placement)
- DRAWING_PRODUCTION_SYSTEM_GUIDE.md (annotation during drawing production)

**Consolidates:**
- `docs/MEP_SYMBOL_GUIDE.md` ← redirect notice added
- `docs/PLACEMENT_FAMILY_AUTHORING.md` ← redirect notice added
- `docs/PLACEMENT_CENTRE_GUIDE.md` ← content incorporated

---

### 2. DRAWING_PRODUCTION_SYSTEM_GUIDE.md
**What it covers:** The complete 4-stage drawing production system — Slot Taxonomy (what spaces exist on a title block), Title Block Creation (building the .rfa family), Drawing Type Manager (wiring the recipe), and Managed View Templates (STING auto-generates and maintains Revit view templates).

**Depends on:** Nothing structural. Best read after MEP_FOUNDATION_GUIDE for annotation context.

**Referenced by:**
- ELECTRICAL_WORKFLOW_GUIDE.md (electrical drawing types)
- PLUMBING_WORKFLOW_GUIDE.md (drainage drawing types)
- HEALTHCARE_WORKFLOW_GUIDE.md (healthcare drawing types)
- DOCUMENT_MANAGER_GUIDE.md (drawings become deliverables)

**Consolidates:**
- `docs/title_blocks/SLOT_TAXONOMY.md`
- `docs/guides/TITLE_BLOCK_CREATION_GUIDE.md`
- `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md`
- `docs/STING_MANAGED_TEMPLATES_DESIGN.md` ← redirect notice added
- `StingTools/Data/DRAWING_TEMPLATE_GUIDE.md` ← redirect notice added

---

### 3. DOCUMENT_MANAGER_GUIDE.md
**What it covers:** Complete document management workflow — the `_BIM_COORD/` folder structure, first-time setup, all 16 document templates, CDE states (WIP/Shared/Published/Archive), every button in the Document Management Center, transmittal workflow, audit trail, distribution groups, and SLA-based workflow engine.

**Depends on:** DRAWING_PRODUCTION_SYSTEM_GUIDE.md (drawings become deliverables that are issued here).

**Referenced by:**
- ELECTRICAL_WORKFLOW_GUIDE.md (issuing electrical drawings)
- PLUMBING_WORKFLOW_GUIDE.md (issuing plumbing drawings)
- HEALTHCARE_WORKFLOW_GUIDE.md (issuing healthcare documents and commissioning records)

**Moves:**
- `docs/DOCUMENT_MANAGER_GUIDE.md` ← redirect notice added; canonical version is now in `docs/guides/`

---

### 4. ELECTRICAL_WORKFLOW_GUIDE.md
**What it covers:** Complete electrical workflow from blank floor plan to issued drawings — project setup, placing electrical equipment, panel schedules (the full BatchPanelSchedules system), circuit assignment, conduit/cable tray routing, lighting design with LightingGrid, wire annotation in electrical drawings, fire alarm and security, riser diagrams, and electrical QA/validation.

**Depends on:**
- MEP_FOUNDATION_GUIDE.md (symbols, family authoring, placement)
- DRAWING_PRODUCTION_SYSTEM_GUIDE.md (drawing production)
- DOCUMENT_MANAGER_GUIDE.md (document issue)

**Referenced by:**
- HEALTHCARE_WORKFLOW_GUIDE.md (EES, nurse call, fire alarm, operating theatre lighting)

**Engine reference (not this guide):**
- `docs/STING_ELECTRICAL_LAYMANS_GUIDE.md` — the technical engine/command reference; keep separate

---

### 5. PLUMBING_WORKFLOW_GUIDE.md
**What it covers:** Complete plumbing and public health workflow — plumbing system codes (DCW, DHW, HWS, SAN, RWD, GAS), pipe system setup, fixture placement, AutoPipeDrop, manual routing, slope requirements for drainage, insulation parameters, plumbing schedules, and QA validation.

**Depends on:**
- MEP_FOUNDATION_GUIDE.md (symbol creation, family authoring, placement)
- DRAWING_PRODUCTION_SYSTEM_GUIDE.md (drawing production)
- DOCUMENT_MANAGER_GUIDE.md (document issue)

**Referenced by:**
- HEALTHCARE_WORKFLOW_GUIDE.md (MGPS piping, water safety piping infrastructure)

---

### 6. HEALTHCARE_WORKFLOW_GUIDE.md
**What it covers:** Healthcare Pack layer on top of core STING — facility profiles, clinical zoning (CLN_* parameters), MGPS (medical gas network builder, flow solver, 12-step NFPA 99 verification), pressure regimes, anti-ligature design, radiation protection (NCRP 147 calculator), clinical equipment (CEQ_*), Room Data Sheets (RDS), the 16 healthcare validators, 8 commissioning workflow presets, 22 healthcare drawing types, and standards references (HTM, NFPA 99, NCRP 147, ASHRAE 170, IRR17, USP 797/800).

**Depends on:**
- MEP_FOUNDATION_GUIDE.md (placement, family authoring)
- ELECTRICAL_WORKFLOW_GUIDE.md (EES, nurse call, fire alarm, theatre lighting)
- PLUMBING_WORKFLOW_GUIDE.md (MGPS piping, water safety piping)
- DRAWING_PRODUCTION_SYSTEM_GUIDE.md (healthcare drawing types)
- DOCUMENT_MANAGER_GUIDE.md (commissioning records, document issue)

**Depends on nothing from this guide.**

---

## Cross-Reference Map

| I am doing this… | Read this guide | Jump to section |
|---|---|---|
| Creating an MEP symbol from scratch | MEP_FOUNDATION_GUIDE | Ch.1 — MEP Symbol Creation |
| Setting up the Revit Family Editor | MEP_FOUNDATION_GUIDE | §1.2 — Family Editor Walkthrough |
| Creating a wire annotation family | MEP_FOUNDATION_GUIDE | §1.4 — Wire Annotation Workflow |
| Preparing a family for auto-placement | MEP_FOUNDATION_GUIDE | Ch.2 — Placement Family Authoring |
| Using the Placement Centre | MEP_FOUNDATION_GUIDE | Ch.3 — The Placement Centre |
| Rehosting elements to a new host | MEP_FOUNDATION_GUIDE | §3.8 — Rehosting Elements |
| Understanding what slot types exist | DRAWING_PRODUCTION_SYSTEM_GUIDE | Stage 1 — Slot Taxonomy |
| Building a title block .rfa family | DRAWING_PRODUCTION_SYSTEM_GUIDE | Stage 2 — Title Block Creation |
| Configuring a Drawing Type | DRAWING_PRODUCTION_SYSTEM_GUIDE | Stage 3 — Drawing Type Manager |
| Setting up Managed View Templates | DRAWING_PRODUCTION_SYSTEM_GUIDE | Stage 4 — Managed View Templates |
| Understanding the _BIM_COORD/ folder | DOCUMENT_MANAGER_GUIDE | Part 1 — Setup |
| Issuing a deliverable | DOCUMENT_MANAGER_GUIDE | Part 3 — Daily Workflows |
| Creating a transmittal | DOCUMENT_MANAGER_GUIDE | §3.3 — Transmittal Workflow |
| Setting up panel schedules | ELECTRICAL_WORKFLOW_GUIDE | Ch.4 — Panel Schedules |
| Assigning circuits to devices | ELECTRICAL_WORKFLOW_GUIDE | Ch.5 — Circuit Assignment |
| Designing a lighting layout | ELECTRICAL_WORKFLOW_GUIDE | Ch.7 — Lighting Design |
| Routing conduit automatically | ELECTRICAL_WORKFLOW_GUIDE | Ch.6 — Conduit Routing |
| Setting up plumbing pipe systems | PLUMBING_WORKFLOW_GUIDE | Ch.4 — Pipe Systems |
| Running AutoPipeDrop | PLUMBING_WORKFLOW_GUIDE | §5.2 — AutoPipeDrop |
| Setting drainage pipe slopes | PLUMBING_WORKFLOW_GUIDE | §5.3 — Slope Requirements |
| Setting the healthcare facility profile | HEALTHCARE_WORKFLOW_GUIDE | Prerequisites |
| Configuring MGPS (medical gas) | HEALTHCARE_WORKFLOW_GUIDE | Ch.2 — MGPS |
| Running pressure regime audit | HEALTHCARE_WORKFLOW_GUIDE | Ch.3 — Pressure Regimes |
| Anti-ligature assessment | HEALTHCARE_WORKFLOW_GUIDE | Ch.4 — Anti-Ligature |
| Radiation shielding calculation | HEALTHCARE_WORKFLOW_GUIDE | Ch.5 — Radiation Protection |
| Generating Room Data Sheets | HEALTHCARE_WORKFLOW_GUIDE | Ch.7 — Room Data Sheets |
| Running healthcare commissioning | HEALTHCARE_WORKFLOW_GUIDE | Ch.8 — Commissioning |

---

## Integration Gaps Tracker

Items marked ⚠ are cross-references that point to content not yet fully written or that
needs expansion. Update this table when gaps are closed.

| Gap ID | Guide | Section | Gap description | Status |
|---|---|---|---|---|
| GAP-01 | MEP_FOUNDATION_GUIDE | §1.4 Wire Annotation | Wire annotation for lighting control wiring (DALI, KNX) not yet covered | ⚠ Open |
| GAP-02 | ELECTRICAL_WORKFLOW_GUIDE | Ch.6 Conduit | Two-phase conduiting workflow referenced in PLACEMENT_FAMILY_AUTHORING not fully developed | ⚠ Open |
| GAP-03 | PLUMBING_WORKFLOW_GUIDE | Ch.5 AutoPipeDrop | AutoDuctDrop covered in MEP Foundation but AutoPipeDrop integration with duct system not documented | ⚠ Open |
| GAP-04 | HEALTHCARE_WORKFLOW_GUIDE | Ch.6 CEQ | COBie integration for clinical equipment (50 types) not covered in workflow guide | ⚠ Open |
| GAP-05 | DRAWING_PRODUCTION_SYSTEM_GUIDE | Stage 3 | Phase III SheetManager integration with Drawing Types needs a worked example | ⚠ Open |
| GAP-06 | All guides | Quick Ref | Keyboard shortcuts for STING panel not documented in any guide | ⚠ Open |
| GAP-07 | ELECTRICAL_WORKFLOW_GUIDE | Ch.9 Fire Alarm | Fire alarm zoning and cause-and-effect matrix not covered | ⚠ Open |
| GAP-08 | HEALTHCARE_WORKFLOW_GUIDE | Ch.2 MGPS | HTM 02-01 zone valve panel commissioning workflow not yet detailed | ⚠ Open |
| GAP-09 | All guides | — | Mobile app (Planscape) workflow integration not cross-referenced in any discipline guide | ⚠ Open |
| GAP-10 | DOCUMENT_MANAGER_GUIDE | Part 6 | Planscape Server sync workflow (what happens when you push to cloud) needs a dedicated section | ⚠ Open |

To close a gap: write the missing content in the appropriate guide, update the Status
column to ✅ Closed with the date, and add a note to `docs/CHANGELOG.md`.

---

## Files Moved or Consolidated

| Original file | New location / status |
|---|---|
| `docs/MEP_SYMBOL_GUIDE.md` | Consolidated → `docs/guides/MEP_FOUNDATION_GUIDE.md` Ch.1 |
| `docs/PLACEMENT_FAMILY_AUTHORING.md` | Consolidated → `docs/guides/MEP_FOUNDATION_GUIDE.md` Ch.2 |
| `docs/PLACEMENT_CENTRE_GUIDE.md` | Consolidated → `docs/guides/MEP_FOUNDATION_GUIDE.md` Ch.3 |
| `docs/title_blocks/SLOT_TAXONOMY.md` | Consolidated → `docs/guides/DRAWING_PRODUCTION_SYSTEM_GUIDE.md` Stage 1 |
| `docs/guides/TITLE_BLOCK_CREATION_GUIDE.md` | Consolidated → `docs/guides/DRAWING_PRODUCTION_SYSTEM_GUIDE.md` Stage 2 |
| `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md` | Consolidated → `docs/guides/DRAWING_PRODUCTION_SYSTEM_GUIDE.md` Stage 3 |
| `docs/STING_MANAGED_TEMPLATES_DESIGN.md` | Consolidated → `docs/guides/DRAWING_PRODUCTION_SYSTEM_GUIDE.md` Stage 4 |
| `StingTools/Data/DRAWING_TEMPLATE_GUIDE.md` | Consolidated → `docs/guides/DRAWING_PRODUCTION_SYSTEM_GUIDE.md` (short embed kept as quick-start) |
| `docs/DOCUMENT_MANAGER_GUIDE.md` | Moved → `docs/guides/DOCUMENT_MANAGER_GUIDE.md` (redirect notice at original) |

---

*Last updated: 2026-05-14 — Phase 174 documentation consolidation*
