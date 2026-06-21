# KUT BIM Manager Playbook
### Your personal, step-by-step guide to delivering the LDS Kampala Uganda Temple (KUT) project

> **Who this is for:** Mayanja Davis (Planscape Consulting Engineers Ltd), appointed BIM / Information Manager on the Kampala Uganda Temple, sub-consultant to **Symbion Consulting**.
> **What this is:** A plain-English, end-to-end manual you can follow from interview → mobilisation → design → construction → handover. It explains *what* to do, *which tool* to use, *how* to use it, and *when* — mapped to the revised 49-month work programme.
> **Status:** Living document. Update it as the project evolves.

---

## How to use this guide

Read **Part 1–3 before your interview** (the fundamentals and the answers you'll be asked for). Use **Part 4 onward as a working manual** once you're appointed. Every section tells you the **tool**, the **action**, and the **timing**.

| If you need to… | Go to |
|---|---|
| Explain BIM / ISO 19650 in the interview | Part 1 |
| Show how the whole project flows | Part 2 |
| Explain each software tool simply | Part 3 |
| Compare the two Fohlio options | Part 4 |
| Produce the Master & Task schedules (MIDP/TIDP) | Part 5 |
| Know the client's required standards | Part 6 |
| Follow the phase-by-phase delivery steps | Part 7 |
| Grab a document template | Part 8 |
| Train the consultants | Part 9 |
| Prepare for interview questions | Part 10 |
| Quick-reference cheat sheets | Part 11 |

---

# PART 0 — The project on one page

| Item | Detail |
|---|---|
| **Project** | LDS (Latter-day Saints) Kampala Uganda Temple — "KUT" |
| **Owner / Client** | The Church (the **Appointing Party** — the one who sets the requirements and receives the asset) |
| **Lead consultant** | **Symbion Consulting** (the **Lead Appointed Party** — coordinates the design team) |
| **Your role** | **BIM / Information Manager** (Planscape) — you run the *information management function*: the CDE, standards, coordination, data quality, and handover |
| **Total programme** | **49 months** — Phase 2 (Design & Tender) = 11 months, Phase 3 (Construction Admin & Handover) = 38 months |
| **Delivery standard** | ISO 19650 (information management), authored in **Autodesk Revit**, coordinated on **Autodesk Construction Cloud (ACC)**, tooled with **STINGTOOLS** |
| **FF&E / O&M** | **Fohlio** (furniture, fittings & equipment + operations/maintenance data) |
| **Interoperability** | **Speckle** ("Speck-Link") for live data sharing + localised standards |
| **Operations / smart building** | **Niagara** (Tridium) BMS for the live building / digital-twin phase |

**Your one-sentence job:** *"I make sure the right information, to the right quality, reaches the right person at the right time — and that it's coordinated, traceable, and ready for handover."*

---

# PART 1 — BIM fundamentals (read before the interview)

You don't need to be the best modeller in the room. As **BIM Manager** you need to own the **process**. Here is the language you must be fluent in.

## 1.1 What is BIM, really?

**BIM (Building Information Modelling)** is not "3D drawings." It is a way of working where the building is built **digitally first** as a coordinated model carrying **data** (not just geometry), so that everyone — architect, engineer, contractor, and the eventual building operator — works from **one trusted source of information**.

> **Interview soundbite:** *"BIM is a process for creating and managing information across the whole life of an asset. The 3D model is just the visible part; the value is the structured data and the coordinated way of working."*

## 1.2 What is ISO 19650?

**ISO 19650** is the international standard for **managing information** on BIM projects. It tells you *who produces what, to what quality, when, and where it's stored*. It is the rulebook your whole job is built on.

The parts you'll use:

| Part | Covers | You use it for |
|---|---|---|
| **ISO 19650-1** | Concepts & principles | The vocabulary (below) |
| **ISO 19650-2** | **Delivery phase** (design & construction) | The day-to-day of Phases 2 & 3 |
| **ISO 19650-3** | **Operational phase** (running the building) | Handover & the Niagara/digital-twin stage |
| **ISO 19650-5** | Security-minded approach | Access control on a sensitive (temple) project |

> **2026 note (say this and you'll sound current):** *"There's a 2026 revision in progress that merges the delivery and operational phases into a single unified information-management cycle — so I plan the project as one continuous flow from design to operations, not two silos."*

## 1.3 The roles (know exactly where you sit)

```
APPOINTING PARTY  (the Church / Owner)
        │  sets requirements (EIR), receives the asset
        ▼
LEAD APPOINTED PARTY  (Symbion Consulting)
        │  coordinates the team, responds with the BEP
        ▼
APPOINTED PARTIES  (task teams: Arch, Struct, MEP, … and Planscape)
        │  produce information for their task
        ▼
   YOU = the Information Management function
   (often delegated to the BIM Manager — that's you)
```

- **Appointing Party** = the client. Issues the **EIR** (what information they want).
- **Lead Appointed Party** = Symbion. Owns the **BEP** (how the team will deliver it).
- **Appointed Parties** = each discipline/task team.
- **You** = run the *information management function* on behalf of the lead — the CDE, the standards, the coordination, the audits.

## 1.4 The key documents (the "paper trail" of BIM)

| Acronym | Name | In plain English | Who owns it |
|---|---|---|---|
| **EIR** | Exchange Information Requirements | The client's shopping list of information | Appointing Party (Church) |
| **BEP** | BIM Execution Plan | The team's answer: "here's how we'll deliver it" | Lead Appointed Party (Symbion) — drafted by **you** |
| **MIDP** | Master Information Delivery Plan | The **master schedule** of every deliverable + date | **You** |
| **TIDP** | Task Information Delivery Plan | Each discipline's slice of the MIDP | Each task team lead |
| **RACI / Responsibility Matrix** | Who's Responsible/Accountable/Consulted/Informed | Who does what | **You** |

> **Memory hook:** *EIR asks → BEP answers → MIDP schedules → TIDP delivers.*

## 1.5 The CDE — the heart of everything

The **Common Data Environment (CDE)** is the single online place where **all** project information lives. For KUT this is **Autodesk Construction Cloud (ACC)**. Every file moves through **four states**:

```
┌──────────┐   share for    ┌──────────┐  approve for  ┌────────────┐   when      ┌──────────┐
│   WIP    │──coordination─▶│  SHARED  │──stage/issue─▶│ PUBLISHED  │─superseded─▶│ ARCHIVED │
│ (your    │                │ (team can│               │ (client-   │             │ (history,│
│  team    │                │  see &   │               │  approved, │             │  audit)  │
│  only)   │                │ coordinate)│             │ contractual)│            │          │
└──────────┘                └──────────┘               └────────────┘             └──────────┘
```

- **WIP** = work in progress, only your team sees it.
- **Shared** = released so other disciplines can coordinate against it.
- **Published** = formally issued/approved (contractual).
- **Archived** = previous versions kept for the record.

> **Interview soundbite:** *"Nothing is 'issued' by email. Everything moves through the CDE states with the right suitability and revision code, so we always know what's current and who approved it."*

## 1.6 LOD — how 'finished' the model is

The work programme uses **LOD (Level of Development / Detail)**. It tells everyone how much to trust a model element at each stage.

| LOD | Meaning | KUT stage |
|---|---|---|
| **LOD 200** | Generic, approximate size/shape/location | 2.1 Basis of Design |
| **LOD 300** | Specific, accurate geometry | 2.2 Developed Design (50%) |
| **LOD 350** | + interfaces/connections to other elements | 2.3 Technical Design (100%) |
| **LOD 400** | + fabrication/installation detail | 3.1/3.2 Construction & FF&E |
| **LOD 500** | Verified **as-built** | 3.3 Close-out / handover |

> Modern term to drop: **LOIN** (Level of Information Need) — ISO 19650 prefers this because it covers **geometry + data + documentation** together, not just the 3D detail.

## 1.7 The 30-second glossary (for nerves on the day)

| Term | Meaning |
|---|---|
| **Federated model** | All discipline models combined into one coordination model |
| **Clash detection** | Finding where elements physically conflict (e.g., a duct through a beam) |
| **COBie** | A spreadsheet standard for handing over asset/maintenance data |
| **Data drop / Information Exchange** | A scheduled moment when information is formally delivered |
| **Worksets** | How a Revit model is split so several people can work at once |
| **Shared coordinates / Project base point** | The agreed origin so every model lines up |
| **Suitability code** | A label (S2, A1…) saying what a file may be used for |
| **Transmittal** | A formal record of "these files were issued to these people on this date" |

---

# PART 2 — How the whole project flows (the big picture)

## 2.1 The ISO 19650 delivery cycle, mapped to KUT

```
  ┌─────────────────────────────────────────────────────────────────────┐
  │ 1. ASSESSMENT & NEED   → Client EIR (what info is required)          │  before you
  │ 2. TENDER / APPOINTMENT → Your BIM proposal, capability, the BEP     │  ← you are here
  ├─────────────────────────────────────────────────────────────────────┤
  │ 3. MOBILISATION  → Set up CDE, standards, templates, train team      │  Month 0–1
  ├─────────────────────────────────────────────────────────────────────┤
  │ 4. COLLABORATIVE PRODUCTION (the long middle)                        │
  │    model → share → clash → coordinate → issue, repeat each cycle     │  Month 1–49
  ├─────────────────────────────────────────────────────────────────────┤
  │ 5. INFORMATION MODEL DELIVERY → each data drop (B, C, D)             │  at each stage
  ├─────────────────────────────────────────────────────────────────────┤
  │ 6. HANDOVER / OPERATIONS → COBie + Fohlio O&M + Niagara digital twin │  Month 47–49+
  └─────────────────────────────────────────────────────────────────────┘
```

## 2.2 The revised work programme with timing

Durations are from your revised work plan. Months are **relative** (M1 = first month after appointment); the illustrative calendar assumes appointment ≈ **Sept 2026** — adjust to the real award date.

| Stage | Activity / Deliverable | LOD | Duration | Relative months | Illustrative dates |
|---|---|---|---|---|---|
| **PHASE 2 — Contract Documents, Drawings & Specs** | | | **11 mo** | | |
| 2.1 | Basis of Design (BOD) | LOD 200 | 1 | M1 | Sep 2026 |
| 2.2 | 50% Documentation / **Deliverable B** / Developed Design | LOD 300 | 3 | M2–M4 | Oct 2026 – Dec 2026 |
| 2.3 | 100% Documentation / **Deliverable C** / Technical Design | LOD 350 | 4 | M5–M8 | Jan 2027 – Apr 2027 |
| 2.4 | Tender Action, Negotiation & Award | — | 3 | M9–M11 | May 2027 – Jul 2027 |
| 2.5 | Conformed Set / Construction Documentation | — | (within above) | M11 | Jul 2027 |
| **PHASE 3 — Construction Administration** | | | **38 mo** | | |
| 3.1 | Supervise the Building Construction Contract | LOD 400 | 32 | M12–M43 | Aug 2027 – Mar 2030 |
| 3.2 | Supervise Furniture, Fittings & Equipment (FF&E) | LOD 400 | 4 | M40–M43* | Dec 2029 – Mar 2030 |
| 3.3 | Provide Close-Out Documents / **Deliverable D** | LOD 500 | 2 | M44–M45 | Apr 2030 – May 2030 |
| | **TOTAL** | | **49 mo** | | ≈ Sep 2026 – ~mid 2030 |

\* 3.2 FF&E overlaps the tail of 3.1 (fit-out happens near completion).

## 2.3 The repeating "coordination cycle" (your weekly rhythm)

During the long middle, you run the **same loop** continuously. This is 80% of your day-to-day job:

```
  MONDAY ────────────────────────────────────────────────────────► FRIDAY
   │           │              │              │             │
   ▼           ▼              ▼              ▼             ▼
 Teams      You run        Coordination   Issues       Weekly
 publish    clash          meeting        assigned &   model-health
 WIP→Shared (ACC + STING)  (review        tracked      report +
 models                    clashes)       to owners    dashboard
```

| Cadence | What happens | Tool |
|---|---|---|
| **Daily** | Auto-tagging, model authoring, WIP saves | Revit + STINGTOOLS |
| **Weekly** | Share models, run clash, coordination meeting, issue tracking | ACC + STINGTOOLS |
| **Monthly** | Model-health + KPI report, data-quality audit, MIDP review | STINGTOOLS KPI dashboard |
| **Per stage (B/C/D)** | Formal data drop, transmittal, client review | ACC + STINGTOOLS templates |

---

# PART 3 — The technology stack explained (plain English)

Think of the tools as a **factory line for information**:

```
  AUTHOR          COORDINATE         ENRICH/AUTOMATE        SPECIFY/PROCURE       OPERATE
  ┌──────┐        ┌──────────┐       ┌────────────┐        ┌──────────┐         ┌─────────┐
  │Revit │──────▶ │   ACC    │◀────▶ │ STINGTOOLS │◀─────▶ │  Fohlio  │         │ Niagara │
  │(model)│       │(CDE+clash│       │(tagging,QA,│        │ (FF&E +  │         │  (BMS / │
  └──────┘        │ +issues) │       │ BOQ,drawings│       │  O&M)    │         │  twin)  │
      │           └──────────┘       │ COBie,KPI) │        └──────────┘         └─────────┘
      │                 ▲            └────────────┘             ▲                    ▲
      └────────── Speckle (live data interchange between all tools) ────────────────┘
```

## 3.1 Autodesk Revit — *the authoring tool*
**What it is:** The software each discipline uses to build the 3D model + drawings + schedules.
**Your job with it:** Set the template, the shared coordinates, the levels/grids, worksets, and the naming/tagging standards so every model is consistent.

## 3.2 Autodesk Construction Cloud (ACC) — *the CDE + coordination*
**What it is:** The cloud home for all files (Docs), the clash engine (Model Coordination), and the issue tracker (Issues/Build).
**Your job with it:** Set up the folder structure with the four CDE states, publish/share models, run **clash detection**, raise and track **issues**, and run the formal **data drops**.
**Key features you'll use:** Docs (CDE), Model Coordination (auto-clash), Issues (BCF), Reviews/approvals, Transmittals.

## 3.3 STINGTOOLS — *your automation + QA engine* (the Revit plugin in this repo)
**What it is:** A single Revit plugin that automates the tedious, error-prone parts of BIM management and enforces your standards. It is your force-multiplier.
**What it does for KUT (highlights):**

| Need | STINGTOOLS command/feature |
|---|---|
| Apply ISO 19650 tags/data automatically | Auto Tag / Batch Tag / Tag & Combine |
| Phased tagging (first line now, tiers later) | `TAG1_ONLY` flag + **Scaffold Tiers** |
| Drawing/sheet production to a corporate standard | Drawing Template Manager (90 drawing types) |
| Clash detection inside Revit + push to ACC | Clash engine + **ACC_PullClashes / push BCF** |
| Bill of Quantities / cost | BOQ export (NRM2) |
| Handover data | COBie 2.4 export |
| FF&E + finishes round-trip with Fohlio | `Fohlio_Export/Import` + **Fohlio finishes** |
| Live KPI / model-health dashboard | **KUT KPI Dashboard** |
| Building-services data → BMS | **Niagara bridge** (Export Points / Reconcile) |
| Project standards profile | KUT owner-standards overlay (`_BIM_COORD/`) |

> **Why it matters for the interview:** *"I'm not doing this by hand. I've got a tooled, standardised pipeline so the team produces consistent, coordinated, audit-ready information at speed."*

## 3.4 Speckle ("Speck-Link") — *the interoperability layer*
**What it is:** An **open-source data platform** that moves BIM data as **live, versioned streams** between tools (Revit, Rhino, Grasshopper, web) instead of emailing files. It breaks models into objects in a database, so you can diff changes, view in a browser, and **automate** workflows (Speckle Automate can, e.g., auto-run a check whenever a model changes).
**How KUT uses it:**
- A **lightweight web viewer** so non-Revit people (client, QS, FM) can see the model in a browser — no licence needed.
- **Live data hand-off** between tools that don't talk natively.
- **Localised standards**: because Speckle isn't in the Owner-mandated stack, we treat it as an **internal** hub and apply our own localised naming/data rules there.
> **Plain English:** *"Speckle is our internal 'data pipe and web viewer' — it lets everyone see and reuse the model data without everyone needing Revit, and it can run automatic checks when models change."*

## 3.5 Fohlio — *FF&E specification, procurement & O&M*
**What it is:** A specification + procurement platform for **Furniture, Fittings & Equipment**, and the **operations/maintenance** data that goes with them. It holds the product library, specs, budgets, and supplier info.
**How KUT uses it:** Phase 3.2 (FF&E) and handover (O&M). The Revit model carries the items; Fohlio carries the **rich product/cost/supplier/maintenance** data. (See **Part 4** for the two integration options.)

## 3.6 Niagara (Tridium) — *the building's nervous system (operations)*
**What it is:** A **Building Management System framework** that connects HVAC, lighting, metering, security and IoT devices — regardless of manufacturer — over protocols like **BACnet, Modbus, oBIX, REST**. ~1 million instances run worldwide.
**How KUT uses it:** In the **operational phase** (and during commissioning), the live building's equipment is connected through Niagara. STINGTOOLS' **Niagara bridge** exports the building-services "points" (e.g., a BACnet sensor on an AHU) from the BIM model, and reconciles the model against what's live in the BMS — the foundation of a **digital twin**.
> **Plain English:** *"Revit tells us what equipment should exist; Niagara tells us what's actually running. The bridge keeps the two in sync — that's the digital twin."*

## 3.7 COBie — *the handover spreadsheet*
**What it is:** A standardised spreadsheet (and data schema) for delivering asset information (spaces, equipment, spares, maintenance, documents) to the building operator. STINGTOOLS exports it.

## 3.8 How they connect (the integration map)

| From → To | What flows | How |
|---|---|---|
| Revit → ACC | Models, sheets | Publish to ACC Docs |
| Revit ↔ STINGTOOLS | Tags, QA, drawings, BOQ, COBie | Native plugin |
| STINGTOOLS ↔ ACC | Clashes, issues (BCF) | ACC API (`ACC_PullClashes`, push issues) |
| Revit/STINGTOOLS ↔ Fohlio | FF&E items + finishes | **Revit Add-in / CSV** (Option A) or **REST API** (Option B) |
| Any tool ↔ Speckle | Live geometry + data, web viewing | Speckle connectors / Automate |
| Revit/STINGTOOLS → Niagara | Equipment "points" for the BMS | Niagara bridge (oBIX/BACnet) |
| Revit/STINGTOOLS → Operator | Asset/maintenance data | COBie + Fohlio O&M |

---

# PART 4 — Fohlio: the TWO integration options (compared)

You asked specifically about the option that automates Fohlio **without** its custom API. Here is exactly how each works, side by side.

## 4.1 Option A — File / Add-in route (NO custom API key needed) ✅ available today

This uses **Fohlio's official Revit Add-in** + **STINGTOOLS' CSV round-trip**. It needs **no developer API key** — just a normal Fohlio account.

**How it flows:**
```
   REVIT MODEL                         FOHLIO
   ┌──────────────┐   export add-in /   ┌──────────────┐
   │ FF&E families│──CSV (STINGTOOLS)──▶│ specs, budget,│
   │ + room       │                     │ products,     │
   │ finishes     │◀──CSV / add-in pull─│ suppliers,    │
   └──────────────┘   (images, URLs,    │ O&M data      │
                       prices)          └──────────────┘
```
**Step by step (per coordination cycle / data drop):**
1. In Revit, run STINGTOOLS **`Fohlio_ExportFinishes`** (room floor/wall/ceiling/base) and the **FF&E export** → produces a CSV.
2. Upload that CSV into Fohlio (or push family data via Fohlio's **Revit Add-in**).
3. In Fohlio, the team enriches: products, images, prices, suppliers, lead times, O&M data.
4. Export the enriched list from Fohlio (CSV).
5. Back in Revit, run STINGTOOLS **`Fohlio_ImportFinishes`** → matches by **Room Number**, shows a diff preview, and writes the data back (fill-empty or overwrite).

**Pros:** Works **now**, no key, no waiting, full control, you decide exactly what syncs and when.
**Cons:** **Manual trigger** (someone clicks export/import each cycle), point-in-time (not live), no automatic conflict detection between syncs.

## 4.2 Option B — Live REST API route (needs a Fohlio API key/tier)

STINGTOOLS calls the **Fohlio REST API** directly to read/write items, specs and finishes — **two-way, automatic**.

**How it flows:**
```
   REVIT / STINGTOOLS  ◀── REST API (auto, scheduled/live) ──▶  FOHLIO CLOUD
```
**Pros:** **Automated** (no manual files), near real-time, removes re-keying, supports scheduled/headless sync.
**Cons:** Needs an **API key / higher subscription tier**, **lead time** (a human at Fohlio enables it — that's why you started the request), and an external dependency.

## 4.3 Side-by-side decision table

| Factor | **Option A — File / Add-in** | **Option B — Live API** |
|---|---|---|
| API key required | ❌ No | ✅ Yes |
| Available today | ✅ Yes (merged in STINGTOOLS) | ⏳ After Fohlio enables it |
| Automation level | Manual trigger per cycle | Automatic / scheduled |
| Currency | Point-in-time (per export) | Near real-time |
| Effort per cycle | A few clicks | Zero (runs itself) |
| Control | Total (you choose what syncs) | Rules-based |
| Cost | Standard Fohlio account | Possibly higher tier |
| Risk | Very low | Dependent on vendor |
| Best for | **Now → most of the project** | **Later, once the key lands** |

## 4.4 Recommendation (and what to say)

> **Use Option A from day one. Pursue Option B in the background.**
> *"We don't wait on a vendor key to start delivering. We run the file/add-in round-trip from day one — it's already built into our toolset. In parallel I've requested API access; when it arrives, the same commands switch to live sync with no rework. So Fohlio is never a blocker."*

**Tip:** ask Fohlio support for their **CSV import template / column spec** (free, no API tier). Aligning STINGTOOLS' `fohlio_map.json` to it makes Option A nearly seamless.

---

# PART 5 — Master & Task schedules (MIDP / TIDP): how & when

This is one of the things they'll judge you on. Get it crisp.

## 5.1 What they are (again, simply)

- **TIDP (Task Information Delivery Plan)** = *one discipline's* list of "what we'll deliver and when" (e.g., the MEP team's drawings, models, schedules + dates).
- **MIDP (Master Information Delivery Plan)** = **all the TIDPs combined** into the project master schedule. It's the single calendar of every deliverable.

```
  TIDP (Arch) ┐
  TIDP (Struct)├──── you aggregate ────▶  MIDP (master schedule)
  TIDP (MEP)  ┘                            owned & maintained by YOU
  TIDP (...)  ┘
```

## 5.2 Who owns what

| Document | Drafted by | Approved by | Maintained by |
|---|---|---|---|
| TIDP | Each task team lead | You (BIM Manager) | Task team lead |
| MIDP | **You** | Lead Appointed Party (Symbion) | **You** |

## 5.3 WHEN to produce / update them

| Moment | Action |
|---|---|
| **Mobilisation (M0–M1)** | Collect a TIDP from every discipline; assemble the **first MIDP**. Baseline it. |
| **Start of each phase (2.1, 2.2, 2.3, 3.x)** | Re-confirm TIDPs for the upcoming stage; reissue MIDP. |
| **At every data drop (B, C, D)** | Check actual vs planned; update status. |
| **Monthly** | Light review — flag slippage, re-forecast. |
| **On any scope/programme change** | Re-baseline and re-issue. |

> **Rule of thumb:** *TIDPs feed the MIDP; the MIDP is the truth. You never let them drift apart.*

## 5.4 TIDP template (per discipline)

| # | Deliverable | Type (Model/Drawing/Schedule/Doc) | LOD/LOIN | CDE state target | Suitability | Planned date | Responsible | Status |
|---|---|---|---|---|---|---|---|---|
| 1 | Architectural model | Model | 300 | Shared | S2 | M4 | Arch lead | |
| 2 | GA floor plans | Drawing | 300 | Shared | S2 | M4 | Arch lead | |
| 3 | Door schedule | Schedule | 300 | Shared | S2 | M4 | Arch lead | |
| … | … | … | … | … | … | … | … | |

## 5.5 MIDP template (project master)

| Ref | Discipline | Deliverable | Stage (B/C/D) | LOD | Format | Suitability | Planned | Actual | RAG |
|---|---|---|---|---|---|---|---|---|---|
| A-001 | Arch | Federated arch model | B | 300 | RVT/IFC | S2 | M4 | | 🟢 |
| S-001 | Struct | Structural model | B | 300 | RVT/IFC | S2 | M4 | | 🟢 |
| M-001 | MEP | MEP model | B | 300 | RVT/IFC | S2 | M4 | | 🟡 |
| … | | | | | | | | | |

> **STINGTOOLS help:** the Drawing Register / schedule exports and the Template Engine can generate and keep the deliverable register in sync — use them so the MIDP isn't a hand-maintained spreadsheet.

---

# PART 6 — The standards the client requires (and how you enforce them)

Derived from ISO 19650 + the KUT scope documents. These live in your **KUT owner-standards overlay** (`project-templates/KUT/_BIM_COORD/owner_standards.json`, `lod_matrix.json`, `tag_schemes.json`, `fohlio_map.json`) so STINGTOOLS enforces them automatically.

## 6.1 File / container naming (ISO 19650)

Fields separated by hyphens — **Project-Originator-Volume/System-Level-Type-Role-Number**:

```
KUT - PLNS - ZZ - XX - M3 - A - 0001
 │     │      │    │    │    │    └ number
 │     │      │    │    │    └ role/discipline (A=Arch, S=Struct, M=Mech…)
 │     │      │    │    └ type (M3=3D model, DR=drawing, SH=sheet…)
 │     │      │    └ level (GF, 01, ZZ=all)
 │     │      └ volume/zone (ZZ=whole)
 │     └ originator (PLNS = Planscape; each firm gets a code)
 └ project code (KUT)
```

## 6.2 CDE suitability codes (what a file may be used for)

| Code | Meaning | CDE state |
|---|---|---|
| **S0** | WIP / initial | WIP |
| **S1** | Shared — for coordination | Shared |
| **S2** | Shared — for information | Shared |
| **S3** | Shared — for review & comment | Shared |
| **S4** | Shared — for stage approval | Shared |
| **A1…An** | Published — authorised (contractual) | Published |
| **B1…Bn** | Published — with comments | Published |

**Revision codes:** `P01, P02…` while preliminary (pre-contract); `C01, C02…` once contractual/published.

## 6.3 LOD by stage (your `lod_matrix.json`)

| Stage | LOD | What "done" means |
|---|---|---|
| 2.1 BOD | 200 | Massing, generic systems |
| 2.2 Dev Design (B) | 300 | Real geometry, located correctly |
| 2.3 Tech Design (C) | 350 | Connections/interfaces resolved |
| 3.1/3.2 Construction (FF&E) | 400 | Fabrication/installation-ready |
| 3.3 Close-out (D) | 500 | Verified as-built |

## 6.4 The non-negotiables (put these in the BEP)

- One agreed **shared coordinate system / project base point** — set at mobilisation, never changed.
- **Units = millimetres**; agreed level & grid names.
- **No rogue families / no imported CAD as model** — controlled family library only.
- Everything issued **through the CDE**, never by email.
- **Tag/data completeness** checked before every share (STINGTOOLS audit).
- **Clash-free** at each data drop (zero unresolved high-priority clashes).
- **Security-minded** access (ISO 19650-5) — temple project; control who sees what.

---

# PART 7 — The phase-by-phase delivery playbook

This is the working manual. For each step: **what to do · which tool · how · when.**

## STAGE 0 — Mobilisation (Month 0–1) — *do this before any modelling*

| # | Action | Tool | How |
|---|---|---|---|
| 0.1 | Draft the **BEP** (answer the client's EIR) | STINGTOOLS BEP Wizard | Capture standards, roles, CDE, MIDP approach |
| 0.2 | Stand up the **CDE** folders + 4 states | ACC Docs | Create WIP/Shared/Published/Archived structure |
| 0.3 | Apply the **KUT standards overlay** | STINGTOOLS | Drop `owner_standards.json` etc. in `_BIM_COORD/` |
| 0.4 | Set the **Revit template** (coords, levels, grids, worksets, families) | Revit + STINGTOOLS | Project Setup Wizard |
| 0.5 | Collect **TIDPs**, build the **MIDP** | STINGTOOLS / spreadsheet | One per discipline → aggregate |
| 0.6 | **Train the consultants** (Part 9) | — | Half-day kickoff |
| 0.7 | Set up **ACC Model Coordination** + clash matrix | ACC | Define which disciplines clash against which |
| 0.8 | Wire **STINGTOOLS ACC sync** (clash/issues) | STINGTOOLS | `ACC_PullClashes`, push BCF |

> **Deliverable:** approved BEP + live CDE + baselined MIDP + trained team. *Get this right and the rest runs itself.*

## STAGE 2.1 — Basis of Design (BOD) · LOD 200 · Month 1

| Action | Tool | How |
|---|---|---|
| Massing + generic systems | Revit | Each discipline models to LOD 200 |
| Early spatial coordination | ACC + STINGTOOLS | First clash sweep (gross clashes only) |
| Capture design intent doc (BOD) | STINGTOOLS Template Engine | Generate the BOD document |
| First data check | STINGTOOLS KPI dashboard | Baseline model-health |

**Data drop:** Issue BOD (suitability **S2/S3**).

## STAGE 2.2 — Developed Design / Deliverable B / 50% · LOD 300 · Months 2–4

| Action | Tool | How |
|---|---|---|
| Develop real geometry (LOD 300) | Revit | All disciplines |
| Auto-tag + populate data | STINGTOOLS | Tag & Combine / Batch Tag (use `TAG1_ONLY` if tiering later) |
| Weekly clash + coordination | ACC + STINGTOOLS | Run loop from Part 2.3 |
| Push/triage clashes → issues | STINGTOOLS → ACC | `ACC_PullClashes` + push BCF; assign owners |
| Produce 50% drawings | STINGTOOLS Drawing Manager | Drawing types/sheets |
| Start BOQ | STINGTOOLS BOQ | NRM2 export |
| Monthly KPI report | STINGTOOLS KPI dashboard | Model-health + completeness |

**Data drop B:** federated model + 50% drawings + schedules, transmittal, suitability **S2** → client review.

## STAGE 2.3 — Technical Design / Deliverable C / 100% · LOD 350 · Months 5–8

| Action | Tool | How |
|---|---|---|
| Resolve interfaces/connections (LOD 350) | Revit | Coordinate junctions |
| Drive clashes to **zero high-priority** | ACC + STINGTOOLS | Tight coordination loop |
| Complete tagging tiers (if phased) | STINGTOOLS | **Scaffold Tiers** → colleagues fill labels |
| Full drawing set + details | STINGTOOLS Drawing Manager | 100% sheets |
| Final BOQ for tender | STINGTOOLS BOQ | NRM2 |
| Specifications | Fohlio (FF&E) + spec docs | Begin FF&E specs in Fohlio (Option A) |
| Begin COBie population | STINGTOOLS COBie | Start capturing asset data early |

**Data drop C:** 100% technical set, suitability **S4** (for stage approval) → **A1** when authorised.

## STAGE 2.4–2.5 — Tender & Conformed Set · Months 9–11

| Action | Tool | How |
|---|---|---|
| Issue tender documents | ACC | Published container, **A1** |
| Answer tenderer queries | ACC Issues / RFIs | Track formally |
| Produce **Conformed Set** post-award | STINGTOOLS Drawing Manager | Incorporate addenda → reissue |

## STAGE 3.1 — Construction Administration · LOD 400 · Months 12–43

| Action | Tool | How |
|---|---|---|
| Maintain the CDE for construction | ACC | Contractor uploads, RFIs, submittals |
| Construction-stage clash / change control | ACC + STINGTOOLS | Re-clash on changes |
| Process RFIs / submittals / variations | ACC Issues + STINGTOOLS | Track, link to model |
| Track revisions | STINGTOOLS Revision Mgmt | Clouds, revision schedules |
| Progress reporting (4D optional) | STINGTOOLS 4D/KPI | Monthly |
| Commissioning prep → **Niagara** | STINGTOOLS Niagara bridge | `Niagara_ExportPoints` for BMS |

## STAGE 3.2 — FF&E Installation · LOD 400 · Months 40–43

| Action | Tool | How |
|---|---|---|
| Finalise FF&E specs + procurement | Fohlio | Option A round-trip (Option B if key ready) |
| Sync FF&E + finishes to model | STINGTOOLS | `Fohlio_Import/ExportFinishes` |
| Track installation/snagging | ACC Issues | On-site |

## STAGE 3.3 — Close-Out / Deliverable D · LOD 500 · Months 44–45

| Action | Tool | How |
|---|---|---|
| Capture **as-built** (LOD 500) | Revit | Update model to as-built |
| Generate **COBie** handover | STINGTOOLS COBie | 2.4 export |
| Export **O&M** + asset data | Fohlio + STINGTOOLS | Handover pack |
| Connect live building | **Niagara** + STINGTOOLS bridge | `Niagara_Reconcile` model vs BMS = digital twin |
| Final handover transmittal | STINGTOOLS / ACC | Archive everything |

**Deliverable D:** as-built model + COBie + O&M + digital-twin baseline → **Owner**.

---

# PART 8 — Document templates (use these / generate with STINGTOOLS)

Most of these STINGTOOLS can **generate or export** — don't build from scratch. Skeletons below for quick reference.

## 8.1 BIM Execution Plan (BEP) — outline
1. Project info & objectives 2. Roles & responsibilities (RACI) 3. Information requirements (EIR response) 4. Standards (naming, CDE, LOD/LOIN) 5. CDE & workflows 6. Software & versions 7. Coordinate system & model origin 8. MIDP/TIDP approach 9. Coordination & clash process 10. QA & data-drop procedure 11. Security (ISO 19650-5) 12. Handover (COBie/O&M).
> **Generate:** STINGTOOLS **BEP Wizard**.

## 8.2 MIDP / TIDP — see Part 5 templates.

## 8.3 Transmittal — template
| Field | Value |
|---|---|
| Transmittal no. | KUT-PLNS-TR-0001 |
| Date | |
| From / To | |
| Purpose / suitability | S2 / A1 … |
| Files (no., rev, format) | |
| Notes | |
> **Generate:** STINGTOOLS Transmittal / `CreateTransmittalOrchestrated`.

## 8.4 Clash / Coordination report — template
| Clash ID | Disciplines | Location/level | Element A / B | Priority | Assigned to | Status | Due |
|---|---|---|---|---|---|---|---|
> **Generate:** STINGTOOLS clash export (CSV/BCF) → push to ACC Issues.

## 8.5 RFI / Technical Query — template
| RFI no. | Date | Raised by | Question | Reference (model/dwg) | Response | Responder | Status |
|---|---|---|---|---|---|---|---|
> **Generate:** STINGTOOLS Template Engine (RFI/TQ) + ACC Issues.

## 8.6 Room Data Sheet (RDS) — for key spaces
| Room | Number | Area | Finishes (F/W/C/Base) | FF&E | Services | Notes |
|---|---|---|---|---|---|---|
> **Generate:** STINGTOOLS RDS engine; finishes via `Fohlio_ExportFinishes`.

## 8.7 FF&E schedule — template
| Item code | Description | Room | Qty | Spec (Fohlio ref) | Supplier | Unit cost | Lead time |
|---|---|---|---|---|---|---|---|
> **Generate:** STINGTOOLS FF&E export ↔ Fohlio.

## 8.8 COBie handover — STINGTOOLS COBie 2.4 export (Facility, Floor, Space, Type, Component, System, Spare, Job, Document…).

## 8.9 Progress / model-health report — template
| Metric | Target | This month | Trend |
|---|---|---|---|
| Tag/data completeness % | ≥95% | | |
| Open high-priority clashes | 0 | | |
| Deliverables on time (MIDP) | 100% | | |
| Open RFIs | — | | |
> **Generate:** STINGTOOLS **KPI Dashboard**.

## 8.10 Drawing register — STINGTOOLS Drawing Register export (number, name, rev, suitability, status, date).

---

# PART 9 — Training the consultants (your kickoff pack)

You'll be judged on whether the **team** can follow the system, not just you. Keep onboarding simple and visual.

## 9.1 The "first day" essentials (what every consultant must know)

| # | They must know | Why |
|---|---|---|
| 1 | The **CDE states** (WIP→Shared→Published→Archived) | So nothing is issued wrongly |
| 2 | The **file naming** convention | So files are findable & valid |
| 3 | **Shared coordinates / origin** — never move it | So all models line up |
| 4 | **Units = mm**, agreed levels & grids | No scale/level chaos |
| 5 | How to **publish/share** a model in ACC | The weekly rhythm |
| 6 | The **clash & issue** workflow | How conflicts get fixed |
| 7 | **Tagging/data** via STINGTOOLS (one click) | Consistent data |
| 8 | **LOD per stage** (what "done" means now) | No over/under-modelling |
| 9 | The **data-drop calendar** (B/C/D dates) | Everyone hits deadlines |
| 10 | **No email issuing, no rogue families** | Governance |

## 9.2 Half-day kickoff agenda

| Time | Topic | Lead |
|---|---|---|
| 0:00 | Project + roles + programme (Part 0/2) | You |
| 0:30 | ISO 19650 basics + CDE states | You |
| 1:00 | Naming, LOD, coordinates, units | You |
| 1:30 | Live demo: publish → clash → issue in ACC | You |
| 2:15 | STINGTOOLS: tagging, drawings, QA (hands-on) | You |
| 3:00 | MIDP/TIDP: each lead confirms their dates | All |
| 3:30 | Q&A + sign the BEP | All |

## 9.3 Discipline-specific "must-dos"

| Discipline | Extra essentials |
|---|---|
| Architecture | Room/space data, finishes, door/window schedules, FF&E hosting |
| Structure | Grids/levels authority, foundation interfaces, rebar at LOD 350+ |
| MEP | System naming, clash priority, equipment data for COBie/Niagara |
| QS/Cost | BOQ/NRM2 link, FF&E cost via Fohlio |
| FM/Operator | COBie/O&M requirements, Niagara points |

## 9.4 Golden rules poster (pin it)
> 1. Issue **only** through the CDE. 2. **Never** move the origin. 3. Model to the **stage LOD** — no more, no less. 4. **Tag/data-check before you share.** 5. **Zero** high-priority clashes at every data drop. 6. If it's not named right, it doesn't exist.

---

# PART 10 — Interview preparation (your first job — you've got this)

## 10.1 Likely questions + strong answers

| Question | Your answer (short) |
|---|---|
| *"What is BIM management?"* | "Owning the **process** so the right information, at the right quality, reaches the right person at the right time — coordinated, traceable, handover-ready. Per ISO 19650." |
| *"How would you set up the project?"* | "Mobilisation first: BEP, CDE with four states in ACC, agreed standards/coordinates, collect TIDPs into a baselined MIDP, then train the team — before any modelling." |
| *"How do you manage coordination/clashes?"* | "A weekly loop: teams share to the CDE, I run clash in ACC **and** STINGTOOLS, push issues with BCF, assign owners, and drive high-priority clashes to zero before each data drop." |
| *"How do you ensure quality?"* | "Standards enforced by tooling (STINGTOOLS), data-completeness checks before every share, and a monthly model-health KPI dashboard with RAG status." |
| *"What's your data-drop plan?"* | "Deliverables B (Dev Design), C (Tech Design), D (Close-out), each with a transmittal, correct suitability, and client review — tracked against the MIDP." |
| *"How do you handle FF&E and O&M?"* | "FF&E and finishes round-trip with **Fohlio** — file/add-in now, live API later; O&M and COBie at handover; live building via **Niagara** for the digital twin." |
| *"How will you handle the team?"* | "A half-day kickoff, a one-page golden-rules poster, discipline-specific must-dos, and the MIDP so everyone owns their dates." |
| *"Biggest risk and mitigation?"* | "Uncoordinated information. Mitigation: strict CDE governance + automated QA + weekly coordination — so problems surface in the model, not on site." |
| *"Why you?"* | "I pair ISO 19650 process discipline with a tooled, automated pipeline (STINGTOOLS) — so we deliver consistent, coordinated information faster and with fewer errors." |

## 10.2 Your 30-60-90 day plan (say this unprompted)

| Days | Focus |
|---|---|
| **0–30** | BEP signed, CDE live, standards + template set, MIDP baselined, team trained |
| **30–60** | Coordination loop running weekly, first KPI report, BOD/early Dev Design coordinated |
| **60–90** | Deliverable B on track, clash trend down, data-completeness ≥95%, MIDP green |

## 10.3 Confidence notes
- You **don't** need to be the fastest modeller — you own the **system**.
- Lean on the **tooling**: it shows you've industrialised the process.
- Speak in **outcomes** (coordinated, on time, handover-ready), not features.

---

# PART 11 — Quick-reference cheat sheets

## 11.1 Acronyms
| | |
|---|---|
| **BIM** | Building Information Modelling |
| **CDE** | Common Data Environment |
| **EIR** | Exchange Information Requirements |
| **BEP** | BIM Execution Plan |
| **MIDP / TIDP** | Master / Task Information Delivery Plan |
| **LOD / LOIN** | Level of Development / Level of Information Need |
| **COBie** | Construction-Operations Building information exchange |
| **RFI / TQ** | Request for Information / Technical Query |
| **RDS** | Room Data Sheet |
| **ACC** | Autodesk Construction Cloud |
| **BMS** | Building Management System (Niagara) |

## 11.2 CDE states & codes
`WIP → SHARED (S0–S4) → PUBLISHED (A1/B1) → ARCHIVED` · Rev `P0x` (prelim) / `C0x` (contractual)

## 11.3 File name pattern
`KUT-PLNS-ZZ-XX-M3-A-0001` = Project-Originator-Volume-Level-Type-Role-Number

## 11.4 The weekly loop
`Share → Clash (ACC+STING) → Coordinate → Issue → Report`

## 11.5 The data drops
`B = Dev Design (M4) · C = Tech Design (M8) · D = Close-out (M45)`

## 11.6 Tool → job
`Revit=author · ACC=CDE+clash · STINGTOOLS=automate/QA · Speckle=share/view · Fohlio=FF&E/O&M · Niagara=operate/twin`

---

## Appendix A — Sources & further reading
- ISO 19650 (Parts 1, 2, 3, 5) — information management; 2026 revision merges delivery + operations into one cycle.
- Fohlio — official **Revit Add-in** + CSV (no-API route) and REST API (live route).
- Speckle — open-source live data streams + **Automate**; web viewer for non-Revit stakeholders.
- Tridium **Niagara 4** — universal BMS integration (BACnet/Modbus/oBIX/REST); foundation for the digital twin.
- STINGTOOLS — see this repository's `CLAUDE.md`, `docs/guides/`, and the KUT overlay in `project-templates/KUT/_BIM_COORD/`.

## Appendix B — Where the KUT settings live (in this repo)
| File | Purpose |
|---|---|
| `project-templates/KUT/_BIM_COORD/owner_standards.json` | Client standards profile |
| `project-templates/KUT/_BIM_COORD/lod_matrix.json` | LOD per stage |
| `project-templates/KUT/_BIM_COORD/tag_schemes.json` | Tag/naming schemes |
| `project-templates/KUT/_BIM_COORD/fohlio_map.json` | Fohlio field mapping (Option A) |
| `StingTools/Data/WORKFLOW_KUT_*.json` | KUT workflow presets (mobilisation, coordination, deliverables, monthly report, FF&E sync) |

---

*End of playbook. Keep it close, update it as KUT evolves, and walk into that interview owning the process.*
