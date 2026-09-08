# KUT BIM Managing & Delivery Playbook
### Kampala Uganda Temple — how the information is managed and how the whole team delivers it, stage by stage

| Field | Value |
|---|---|
| **Project** | Kampala Uganda Temple (KUT) |
| **Document** | KUT BIM Managing & Delivery Playbook |
| **Revision** | P01 |
| **Author** | Mayanja Davis (BIM Manager) |
| **Status** | Internal working guide |
| **Date** | [FILL: yyyy-mm-dd] |

> **What this is.** One combined manual: the *managing* side (how the information-management function runs — CDE, standards, numbering, coordination, QA, handover) and the *delivery* side (what every discipline produces at each stage, and when). It is written to be followed from mobilisation through design, construction and handover, mapped to the 49-month Owner Work Program.
>
> **Where it sits against the issued pack.** This is an internal working guide, not a contractual document. The **BIM Execution Plan (BEP)** is the contractual statement of *what* we do; this playbook is the working statement of *how* and *when*. **Where they disagree, the BEP prevails and this playbook gets corrected.** The full modelling method lives in the companion **KUT_BIM_MODELLING_PLAYBOOK.md** — this playbook only summarises it.

---

## Contents

1. [How to use this guide](#1--how-to-use-this-guide)
2. [The project on one page](#2--the-project-on-one-page)
3. [BIM & ISO 19650 fundamentals](#3--bim--iso-19650-fundamentals)
4. [Roles & RACI](#4--roles--raci)
5. [The numbering system](#5--the-numbering-system)
6. [The CDE & the four states](#6--the-cde--the-four-states)
7. [The technology stack & integration map](#7--the-technology-stack--integration-map)
8. [Modelling standards (summary)](#8--modelling-standards-summary)
9. [Information requirements by stage](#9--information-requirements-by-stage)
10. [The stages, in order](#10--the-stages-in-order)
11. [MIDP & TIDP](#11--midp--tidp)
12. [The operating rhythm & coordination cycle](#12--the-operating-rhythm--coordination-cycle)
13. [QA, clash & validation](#13--qa-clash--validation)
14. [Deliverables & handover](#14--deliverables--handover)
15. [Leading and teaching the team](#15--leading-and-teaching-the-team)
- [Appendices](#appendices) · [Companion documents](#companion-documents)

---

## 1 — How to use this guide

| If you are… | Read | Then keep open |
|---|---|---|
| Joining the project | §2, §3, §4, §5, §6 — then your discipline's row in §10 | §5 (numbering) and the pre-share checklist (App. A1) |
| A Task Team Manager | All of it | §10 (your stage), §12 (the rhythm), §13 (gates) |
| Modelling day to day | §5, §8, App. A1 — plus the companion modelling playbook | App. A1 — pin it beside your screen |
| The BIM Manager | All of it | §11, §12, §13, §14 |
| The Contractor / specialists (from Stage 3.1) | §2, §5, §6, §10.6–10.8, §14 | App. A1, App. A3 |

**Three rules that make everything else work.** If you remember nothing else:

1. **Everything is issued through the CDE.** Never by email, WhatsApp, or a USB stick. If it did not go through the CDE, it was not issued.
2. **Nothing is shared until it passes the pre-share checklist** (App. A1). A model that fails the check wastes everyone's fortnight, not just yours.
3. **The numbering system is not negotiable and not improvisable** (§5). One wrong container name breaks the register, the transmittal, the clash report and the handover data at once.

---

## 2 — The project on one page

| | |
|---|---|
| **Project** | Kampala Uganda Temple (KUT) |
| **Appointing Party (Client / Owner)** | The Church — Special Projects Department. Sets the requirements (EIR) and accepts each deliverable |
| **Lead Appointed Party** | Symbion Consulting Group Studios — coordinates the design team, chairs design meetings, owns the appointment |
| **BIM Manager (information-management function)** | Symbion Consulting Group Studios — **Mayanja Davis**. Runs the CDE, standards, numbering, coordination, QA gate, registers, transmittals and handover data. **Coordinates and verifies information — does not author design** |
| **Scope** | Temple plus ancillary buildings — six volumes plus site-wide works |
| **Programme** | **49 months** — Phase 2 (design & tender) 11 months · Phase 3 (construction admin, FF&E & close-out) 38 months |
| **Delivery standard** | ISO 19650 (BS EN ISO 19650-1/-2/-3/-5) for information management; US National CAD Standard (Uniform Drawing System) for sheet numbering |
| **CDE** | **Autodesk Construction Cloud (ACC)** — the single authoritative environment |
| **Authoring** | **Autodesk Revit** — millimetres, one shared coordinate system fixed at mobilisation |
| **Federation / clash** | **Navisworks** + **ACC Model Coordination**; issues tracked as ACC Issues (BCF); in-Revit pre-checks via **STINGTOOLS** |
| **Automation / QA / tagging / BOQ** | **STINGTOOLS** (Revit plug-in) — auto-tagging, standards enforcement, drawing production, BOQ, KPI/model-health, register exports |
| **Interoperability / web viewer** | **Speckle** — live, versioned data streams and a browser viewer for non-Revit stakeholders (client, QS, FM) |
| **Specifications** | **RIB SpecLink** (written specifications), CSI MasterFormat — a specification tool, *not* a 3D viewer |
| **FF&E / finishes / O&M** | **Fohlio** — the Owner's single source of truth for FF&E and finishes. The model **links** to it; it never duplicates it |
| **Building-services operation** | **Niagara** (Tridium) BMS — the live building / digital-twin phase |
| **Handover data** | **COBie 2.4 + O&M**, aligned to the record model — **produced only if the Owner requires it** in the EIR |

**The BIM Manager's one-sentence job:** *"I make sure the right information, to the right quality, reaches the right person at the right time — coordinated, traceable, and ready for handover."*

### The six buildings (volumes)

| Volume code | Building |
|---|---|
| `01` | `BLD1` | Temple |
| `02` | `BLD2` | Meetinghouse |
| `03` | `BLD3` | Housing / Ancillary |
| `04` | `BLD4` | Grounds |
| `05` | `BLD5` | Utility |
| `06` | `BLD6` | Guard House |
| `ZZ` | Project-wide / all volumes |
| `XX` | Not applicable |

### The programme (Owner Work Program, June 2026)

Months are **relative** — M1 is the first month after appointment. Stages run in sequence, which is what makes the 11 + 38 subtotals reach 49. Calendar dates are derived from the confirmed appointment date on the MIDP Programme sheet — that is the one to trust.

| Stage | Name | LOD | Months | The gate (what "done" means) |
|---|---|---|---|---|
| 0 | Mobilisation | — | M0–M1 | Kit issued, everyone trained, CDE live |
| 2.1 | Basis of Design — **Deliverable A / BOD** | 200 | M1 | Massing & generic systems coordinated |
| 2.2 | Developed Design — **Deliverable B (50%)** | 300 | M2–M4 | Real geometry, correctly located |
| 2.3 | Technical Design — **Deliverable C (100%)** | 350 | M5–M8 | Interfaces & connections resolved |
| 2.4 | Tender issue | 350 | M9–M10 | Tender set issued from the CDE |
| 2.5 | **Conformed set** | 350 | M11 | Addenda incorporated, set reissued |
| 3.1 | Construction administration | 400 | M12–M43 | Fabrication/installation-ready information |
| 3.2 | FF&E installation | 400 | M44–M47 | FF&E installed & reconciled to Fohlio |
| 3.3 | Close-out — **Deliverable D** | 500 | M48–M49 | Verified record model + handover data |

Tender action, negotiation and award occupy the **M9–M11** window; the tender set issues at 2.4 (M9–M10) and the conformed set is published at 2.5 (M11).

> **LOD 500 at Deliverable D.** LOD 400 sits at construction (3.1). LOD 500 means *verified as-built* — the element matches what was actually installed and carries its asset data (serial number, installation date). **Plan for it from Stage 3.1, not from M48.**

---

## 3 — BIM & ISO 19650 fundamentals

You do not need to be the best modeller in the room. The BIM Manager owns the **process**. Here is the language everyone should be fluent in.

### 3.1 What BIM really is

**BIM (Building Information Modelling)** is not "3D drawings." It is a way of working where the building is built **digitally first** as a coordinated model carrying **data** (not just geometry), so that everyone — architect, engineer, contractor, and the eventual operator — works from **one trusted source of information.** The 3D model is the visible part; the value is the structured data and the coordinated way of working.

### 3.2 ISO 19650 — the rulebook

**ISO 19650** is the international standard for **managing information** on BIM projects: *who produces what, to what quality, when, and where it is stored.*

| Part | Covers | Used for |
|---|---|---|
| **ISO 19650-1** | Concepts & principles | The vocabulary below |
| **ISO 19650-2** | Delivery phase (design & construction) | The day-to-day of Phases 2 & 3 |
| **ISO 19650-3** | Operational phase (running the building) | Handover & the Niagara / digital-twin stage |
| **ISO 19650-5** | Security-minded approach | Access control on a sensitive (temple) project |

### 3.3 The roles (where the BIM Manager sits)

```
APPOINTING PARTY  (the Church / Owner)
        │  sets requirements (EIR), accepts the asset
        ▼
LEAD APPOINTED PARTY  (Symbion Consulting Group Studios)
        │  coordinates the team, owns the BEP
        ▼
APPOINTED PARTIES  (task teams: Arch/Int, Struct, MEP, Fire, LV, Civil, QS …)
        │  produce information for their task
        ▼
   INFORMATION MANAGEMENT FUNCTION = the BIM Manager (Mayanja Davis)
   runs the CDE, standards, coordination and audits on behalf of the Lead
```

- **Appointing Party** = the Church. Issues the **EIR** (what information is required).
- **Lead Appointed Party** = Symbion. Owns the **BEP** (how the team delivers it).
- **Appointed Parties** = each discipline / task team.
- **BIM Manager** = runs the information-management function for the Lead — the CDE, standards, coordination, audits.

### 3.4 The key documents (the "paper trail")

| Acronym | Name | In plain English | Owned by |
|---|---|---|---|
| **EIR** | Exchange Information Requirements | The Owner's shopping list of information | Appointing Party (Church) |
| **BEP** | BIM Execution Plan | The team's answer: how we will deliver it | Lead Appointed Party (Symbion), drafted by the **BIM Manager** |
| **MIDP** | Master Information Delivery Plan | The master schedule of every deliverable + date | **BIM Manager** |
| **TIDP** | Task Information Delivery Plan | Each discipline's slice of the MIDP | Each Task Team Manager |
| **RACI** | Responsibility matrix | Who is Responsible / Accountable / Consulted / Informed | **BIM Manager** |

> **Memory hook:** *EIR asks → BEP answers → MIDP schedules → TIDP delivers.*

### 3.5 The CDE (in one line)

The **Common Data Environment (CDE)** is the single online place where all project information lives. For KUT it is **Autodesk Construction Cloud (ACC)**, and every file moves through four states — see §6.

### 3.6 LOD — how "finished" the model is

**LOD (Level of Development / Detail)** tells everyone how much to trust a model element at each stage.

| LOD | Meaning | KUT stage |
|---|---|---|
| **200** | Generic, approximate size / shape / location | 2.1 Basis of Design |
| **300** | Specific, accurate geometry, correctly located | 2.2 Developed Design (Deliverable B) |
| **350** | + interfaces / connections to other elements | 2.3 Technical Design (Deliverable C) |
| **400** | + fabrication / installation detail | 3.1 Construction / 3.2 FF&E |
| **500** | Verified **as-built** with asset data | 3.3 Close-out (Deliverable D) |

> **LOIN (Level of Information Need)** is the modern ISO 19650 term — it covers **geometry + data + documentation** together, not just 3D detail. The information-requirement tables in §9 are the LOIN for KUT.

### 3.7 30-second glossary

| Term | Meaning |
|---|---|
| **Federated model** | All discipline models combined into one coordination model |
| **Clash detection** | Finding where elements physically conflict (a duct through a beam) |
| **Data drop / Information Exchange** | A scheduled moment when information is formally delivered |
| **Worksets** | How a Revit model is split so several people can work at once |
| **Shared coordinates / project base point** | The agreed origin so every model lines up |
| **Suitability code** | A label (S2, A1…) saying what a file may be used for |
| **Transmittal** | A formal record of which files were issued, to whom, on what date |
| **COBie** | A spreadsheet standard for handing over asset / maintenance data |

---

## 4 — Roles & RACI

### 4.1 Roles

| Role | Held by | Owns |
|---|---|---|
| **Appointing Party** | The Church | The EIR; acceptance of each deliverable |
| **Lead Appointed Party** | Symbion Consulting Group Studios | The overall appointment; design leadership; chairs the design meetings |
| **BIM Manager** | Symbion Consulting Group Studios — Mayanja Davis | The CDE, the BEP, the MIDP, the standards, the QA gate, federation, clash management, registers, transmittals, handover data. **Coordinates and verifies — does not author design** |
| **Task Team Manager** (one per discipline) | Each consultant | Their model, their TIDP, their data quality, their sign-off before every share |
| **Modellers / technicians** | Each consultant | Day-to-day authoring to the standards in §5 and §8 |
| **QS / Cost** | `[FILL]` | Quantities and cost derived from the model |
| **Interior Designer** | `[FILL]` | FF&E and finishes design, and the Fohlio record |
| **Contractor** (from 3.1) | `[FILL]` | As-built capture, commissioning data, specialist models |
| **Controls / commissioning contractor** | `[FILL]` | Niagara station, point naming, commissioning records |

### 4.2 RACI — the activities that cross organisations

**R** = does the work · **A** = accountable · **C** = consulted · **I** = informed

| Activity | BIM Mgr | Lead AP | Arch/Int | Struct | MEP | QS | Contractor |
|---|---|---|---|---|---|---|---|
| Maintain the CDE and its states | **A/R** | A | I | I | I | I | C |
| Issue and maintain the BEP | **A/R** | C | C | C | C | I | I |
| Maintain the MIDP | **A/R** | C | C | C | C | C | C |
| Produce and maintain a TIDP | C | A | **R** | **R** | **R** | **R** | **R** |
| Author discipline model | I | A | **R** | **R** | **R** | I | C |
| Pre-share QA check | C | I | **R** | **R** | **R** | I | **R** |
| Federate the models | **R** | A | C | C | C | I | C |
| Run clash detection | **R** | A | C | C | C | I | C |
| Resolve a clash | C | A | **R** | **R** | **R** | I | **R** |
| Chair the coordination meeting | C | **A/R** | C | C | C | I | C |
| Produce drawings and sheets | C | A | **R** | **R** | **R** | I | C |
| Drawing register and transmittals | **A/R** | C | C | C | C | I | I |
| Quantities / BOQ | C | I | C | C | C | **A/R** | C |
| FF&E and finishes data (Fohlio) | **R** | A | **R** (Interiors) | I | I | C | C |
| Specification reconciliation | **R** | A | **R** | **R** | **R** | C | I |
| Gate audit and sign-off pack | **A/R** | A | C | C | C | C | C |
| As-built capture | C | A | C | C | C | I | **R** |
| Commissioning point list | **R** | A | I | I | **C** | I | **R** |
| Handover data (COBie / O&M) | **A/R** | A | C | C | C | I | **R** |

> **Read the BIM Manager column carefully.** The BIM Manager is accountable for *information*, not for design. If a clash needs a beam moved, the structural engineer moves it — the BIM Manager only makes sure the clash is visible, tracked, and closed before the gate.

The issued RACI matrix lives at `RACI and Roles/KUT_RACI_Responsibility_Matrix.xlsx`.

---

## 5 — The numbering system

This is the section people come back to. Print it. The authoritative source is the companion **KUT_Drawing_and_Document_Numbering_Convention.md**; this is the working summary.

### 5.1 The principle

The number does not try to carry everything. In ISO 19650 the container name already states the project, originator, building, level and discipline in their own fields — so the **Number** field has one job left: to say **what type of drawing it is, and which one in the sequence.** That is why numbers "jump": plans in one band, sections in another, schedules in another. You file each drawing in the drawer for its type; you do not count them 1, 2, 3 in the order drawn.

### 5.2 The container name — every file, model, drawing and document

```
KUT - [ORG] - TE - GF - DR - A - 1001
 │      │      │    │    │    │    └── Number     4 digits — type band + sequence (§5.5)
 │      │      │    │    │    └─────── Role        discipline (§5.4)
 │      │      │    │    └──────────── Type        what kind of thing it is (§5.3)
 │      │      │    └───────────────── Level       GF, 01, B1, ZZ = all, XX = n/a
 │      │      └────────────────────── Volume      building code (§2) — ZZ = project-wide
 │      └───────────────────────────── Originator  the authoring company — [ORG], assigned at mobilisation
 └──────────────────────────────────── Project     always KUT
```

Separator is a hyphen. No spaces. Upper case throughout. Because Volume and Role are their own fields, the **same number repeats across buildings and disciplines** and the other fields keep it unique — `KUT-[ORG]-01-GF-DR-A-1001` and `KUT-[ORG]-02-GF-DR-A-1001` are both "architectural floor-plan sheet 1", one Temple, one Meetinghouse.

> **⚠ Originator codes.** Do not start numbering until the originator register is issued by the Lead Appointed Party. Every appointed party (including sub-consultants, with a block reserved for the contractor and specialists) gets a code assigned **at mobilisation**. Until then, examples carry the placeholder `[ORG]`. Renumbering after Deliverable A is expensive and visible in every document already sent.

### 5.3 Type codes

| Code | Meaning | Code | Meaning |
|---|---|---|---|
| `DR` | Drawing | `RP` | Report |
| `M3` | 3D model | `RP` | Document |
| `SH` | Schedule / register | `CP` | Bill of quantities |
| `SP` | Specification | `IE` | Transmittal / notice |

### 5.4 Role (discipline) codes

| Code | Discipline | Code | Discipline |
|---|---|---|---|
| `A` | Architecture | `Y` | Specialist Designer — fire protection, low voltage and communications|
| `S` | Structural | `C` | Civil / site |
| `M` | Mechanical | `I` | Interiors |
| `E` | Electrical | `L` | Low voltage / communications |
| `P` | Plumbing / Public Health | `Z` | Coordination / multi-discipline |

### 5.5 The Number field — type-banded (drawings, Type `DR`)

Four digits: the **first digit is the drawing-type band**, the **last three are the sequence** (001–999) within that band.

| First digit | Drawing type | Starts at |
|---|---|---|
| `0` | General — cover, drawing list, location & key plans, legends, notes | 0001 |
| `1` | Plans — floor, site, roof, reflected-ceiling (all horizontal views) | 1001 |
| `2` | Elevations | 2001 |
| `3` | Sections | 3001 |
| `4` | Large-scale / enlarged views | 4001 |
| `5` | Details | 5001 |
| `6` | Schedules and diagrams | 6001 |
| `7` | KUT-defined — schematics, risers, single-line & system diagrams | 7001 |
| `8` | Reserved (coordination, sketches, mark-ups) | 8001 |
| `9` | 3D — isometrics, axonometrics, perspectives, visualisations | 9001 |

Sort any folder or register by number and the drawings fall into type groups automatically. A new drawing takes the next free number in its band; existing drawings keep their numbers for life. **Never reuse a number** — a superseded or cancelled drawing keeps its number and it is retired with it.

**Non-drawing information** (models, schedules, documents) uses a **simple four-digit sequence** from 0001 within each type — e.g. `KUT-[ORG]-01-ZZ-M3-A-0001` (Temple architectural model), `KUT-[ORG]-ZZ-XX-RP-Z-0002` (a report in the RP series).

### 5.6 Level, revision and suitability codes

- **Level:** `B1` basement · `GF` ground floor · `01`, `02`, `03`… upper floors · `RF` roof · `ZZ` all levels · `XX` not applicable. Level codes must match the Revit level names exactly — do not invent local variants (`GRD`, `Ground`, `L00`).
- **Revision:** `P01, P02…` while **preliminary** (pre-contract); `C01, C02…` once **contractual / published**. The revision changes **only** when the container is re-issued through the CDE — working saves do not consume revisions.
- **Suitability** — see §6.3.

### 5.7 The asset (element) tag — every modelled element

Every element carries an eight-segment identifier, built automatically from the data on the element:

```
M - TE - Z01 - L02 - HVAC - SUP - AHU - 0003
│    │     │     │     │      │     │      └── SEQ    sequence, 4 digits
│    │     │     │     │      │     └───────── PROD   product code (AHU, DB, DR…)
│    │     │     │     │      └─────────────── FUNC   function (SUP, HTG, PWR…)
│    │     │     │     └────────────────────── SYS    system (HVAC, DCW, SAN, LV…)
│    │     │     └──────────────────────────── LVL    level
│    │     └────────────────────────────────── ZONE   zone
│    └──────────────────────────────────────── LOC    location / volume
└───────────────────────────────────────────── DISC   discipline
```

Segment order: **DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ.**

**What the team must do:** model in the right workset, in the right volume, with rooms placed, and the systems actually connected. The tag then fills itself. **What breaks it:** elements floating outside any room or volume, MEP elements not connected to a system, and copies pasted between volumes. STINGTOOLS auto-tags and audits this on every share.

### 5.8 Worked examples

| Thing | Container name |
|---|---|
| Architectural drawing list / cover, project-wide | `KUT-[ORG]-ZZ-XX-DR-A-0001` |
| Temple, ground floor, architectural floor plan, sheet 1 | `KUT-[ORG]-01-GF-DR-A-1001` |
| Temple, architectural elevation, sheet 1 | `KUT-[ORG]-01-XX-DR-A-2001` |
| Temple, architectural section, sheet 1 | `KUT-[ORG]-01-XX-DR-A-3001` |
| Temple, architectural detail, sheet 1 | `KUT-[ORG]-01-XX-DR-A-5001` |
| Meetinghouse, ground floor, structural framing plan | `KUT-[ORG]-02-GF-DR-S-1001` |
| Temple electrical single-line / riser diagram | `KUT-[ORG]-01-XX-DR-E-7001` |
| Temple architectural 3D model, all levels | `KUT-[ORG]-01-ZZ-M3-A-0001` |
| Federated coordination model | `KUT-[ORG]-ZZ-ZZ-M3-Z-0001` |
| Clash / coordination report, cycle 07 | `KUT-[ORG]-ZZ-ZZ-RP-Z-0007` |
| Transmittal | `KUT-[ORG]-ZZ-XX-IE-Z-0001` |

---

## 6 — The CDE & the four states

### 6.1 The four states

```
   WIP  ──►  SHARED  ──►  PUBLISHED  ──►  ARCHIVED
    │          │              │              │
 your team   everyone     contractual    superseded,
   only      can see        issue         kept for
                                          the record
```

- **WIP** — your own team's working area. Nobody else may use anything here. Suitability `S0`.
- **SHARED** — coordination-ready. Suitability `S1`–`S4`. This is where the fortnightly cycle happens.
- **PUBLISHED** — authorised and contractual. Suitability `A1`/`B1`. Requires the gate to have passed.
- **ARCHIVED** — superseded, retained. **Nothing is ever deleted.**

### 6.2 What moves a file between states

| Move | Who authorises | What must be true |
|---|---|---|
| WIP → Shared | Task Team Manager | Pre-share checklist (App. A1) passed and recorded |
| Shared → Published | BIM Manager, on Lead AP approval | Gate audit passed (§13); review comments closed; register updated |
| Published → Archived | BIM Manager | A superseding revision has been published |

### 6.3 Suitability codes — what a file may be used for

| Code | Meaning | CDE state |
|---|---|---|
| `S0` | Work in progress — not for use by others | WIP |
| `S1` | Shared — for coordination | Shared |
| `S2` | Shared — for information | Shared |
| `S3` | Shared — for review and comment | Shared |
| `S4` | Shared — for stage approval | Shared |
| `A1`…`An` | Published — authorised, contractual | Published |
| `B1`…`Bn` | Published — authorised with comments | Published |

> **A suitability code is a promise about how others may use your file.** `S2` means "you may read this but do not build on it". Marking WIP work as `S1` because a deadline is close is the single most damaging thing anyone can do on this project.

### 6.4 Non-negotiables

- **Everything through the CDE.** Email is for conversation, never for issue.
- **One shared coordinate system and project base point**, fixed at mobilisation, never changed. Every model uses "Shared Coordinates" on link.
- **Units are millimetres.** Level and grid names come from the project template and are not renamed locally.
- **No rogue families.** Content comes from the issued library; new content is submitted for checking before use.
- **No CAD as model.** Imported CAD may underlay; it may never be the deliverable geometry.
- **Security-minded information management (ISO 19650-5).** This is a temple project. Access is by need. Do not post model images, plans or renders publicly, on social media, or in portfolios without written permission.

---

## 7 — The technology stack & integration map

Think of the tools as a **factory line for information**:

```
  AUTHOR       COORDINATE         AUTOMATE/QA        SPECIFY        FF&E/O&M       OPERATE
 ┌──────┐   ┌───────────────┐   ┌────────────┐   ┌──────────┐   ┌──────────┐   ┌─────────┐
 │Revit │──▶│ ACC + Navis-  │◀─▶│ STINGTOOLS │   │   RIB    │   │  Fohlio  │   │ Niagara │
 │(model)│  │ works (CDE,   │   │(tag, QA,   │   │ SpecLink │   │ (FF&E +  │   │  (BMS / │
 └──────┘   │ federate,clash│   │ BOQ, draw- │   │ (written │   │  O&M)    │   │  twin)  │
     │      │ +ACC Issues)  │   │ ings,KPI)  │   │  specs)  │   └──────────┘   └─────────┘
     │      └───────────────┘   └────────────┘   └──────────┘        ▲              ▲
     └──────────── Speckle (live data interchange + web viewer) ─────┴──────────────┘
```

### 7.1 Autodesk Revit — the authoring tool
Each discipline builds its 3D model + drawings + schedules in Revit. The BIM Manager sets the template, shared coordinates, levels/grids, worksets, and naming/tagging standards so every model is consistent.

### 7.2 Autodesk Construction Cloud (ACC) — the CDE
The cloud home for all files (Docs), with the folder structure carrying the four CDE states, formal data drops, reviews/approvals and transmittals. Clashes and coordination issues are tracked here as **ACC Issues** (BCF).

### 7.3 Navisworks — federation & clash
Discipline models are federated and clash-tested in **Navisworks** alongside **ACC Model Coordination**. Results are grouped, prioritised and pushed to ACC Issues. STINGTOOLS runs in-Revit clash pre-checks so problems are caught before the share.

### 7.4 STINGTOOLS — automation, QA, tagging & BOQ (the Revit plug-in)
A single Revit plug-in that automates the tedious, error-prone parts of BIM management and enforces the standards. It is the force-multiplier.

| Need | STINGTOOLS feature |
|---|---|
| Apply ISO 19650 tags/data automatically | Auto Tag / Batch Tag / Tag & Combine |
| Phased tagging (first line now, tiers later) | `TAG1_ONLY` flag + Scaffold Tiers |
| Drawing/sheet production to the type-banded standard | Drawing Template / Register Manager |
| In-Revit clash + push to ACC | Clash engine + `ACC_PullClashes` / push BCF |
| Bill of Quantities / cost | BOQ export (NRM2) |
| FF&E + finishes round-trip with Fohlio | `Fohlio_Export/ImportFinishes` |
| Live KPI / model-health dashboard | KUT KPI Dashboard |
| Building-services data → BMS | Niagara bridge (`Niagara_ExportPoints` / `Reconcile`) |
| Handover data | COBie 2.4 export (if the Owner requires COBie) |
| Project standards profile | KUT owner-standards overlay (`_BIM_COORD/`) |

### 7.5 Speckle — interoperability & web viewer
An open-source data platform that moves BIM data as **live, versioned streams** between tools instead of emailing files, and gives a **lightweight browser viewer** so non-Revit stakeholders (client, QS, FM) can see the model with no licence. Because Speckle is not in the Owner-mandated stack, it is treated as an **internal** hub with our own localised naming/data rules. *(Speckle is a data-and-viewer platform — do not confuse it with RIB SpecLink, which is the written-specification tool.)*

### 7.6 RIB SpecLink — written specifications
The specification authoring tool. CSI MasterFormat sections are written and maintained here and reconciled against the model at every gate from Stage 2.3. **RIB SpecLink produces the written specs — it is not a 3D viewer.**

### 7.7 Fohlio — FF&E specification, procurement & O&M
The Owner's single source of truth for **Furniture, Fittings & Equipment**, finishes, and the **operations/maintenance** data that goes with them. The Revit model **links** to the Fohlio record by a shared identifier; it never holds a competing copy. See §14.1.

### 7.8 Niagara (Tridium) — the building's nervous system (operations)
A Building Management System framework that connects HVAC, lighting, metering, security and IoT devices over BACnet, Modbus, oBIX and REST. The model states what equipment and points *should* exist; the Niagara station states what *is* running. Keeping the two aligned is the **digital twin**. See §14.3.

### 7.9 COBie — the handover spreadsheet (if required)
A standardised spreadsheet/schema for delivering asset information (Facility, Floor, Space, Type, Component, System, Spare, Job, Document) to the operator. Produced from the model at close-out via STINGTOOLS — **only if the Owner requires COBie in the EIR.**

### 7.10 The integration map

| From → To | What flows | How |
|---|---|---|
| Revit → ACC | Models, sheets | Publish to ACC Docs |
| Revit ↔ STINGTOOLS | Tags, QA, drawings, BOQ, COBie | Native plug-in |
| Revit / STINGTOOLS ↔ Navisworks / ACC | Federation, clashes, issues (BCF) | Navisworks + `ACC_PullClashes` / push issues |
| Revit ↔ Fohlio | FF&E items + finishes | STINGTOOLS CSV round-trip (matched by Room Number) |
| Model ↔ RIB SpecLink | Spec ↔ model reconciliation | CSI section mapping, gate reconciliation |
| Any tool ↔ Speckle | Live geometry + data, web viewing | Speckle connectors |
| Revit / STINGTOOLS → Niagara | Equipment "points" for the BMS | Niagara bridge (oBIX / BACnet) |
| Revit / STINGTOOLS → Operator | Asset / maintenance data | COBie (if required) + Fohlio O&M |

---

## 8 — Modelling standards (summary)

Every author follows these. **The full modelling method — worksets, family rules, parameters, phases, view templates and the tagging workflow — is in the companion `KUT_BIM_MODELLING_PLAYBOOK.md`.** This is the summary.

| Topic | The rule |
|---|---|
| Origin | Shared coordinate system from the template. Never move the project base point or survey point |
| Units | Millimetres |
| Levels & grids | From the template. Renaming requires the BIM Manager's agreement — it breaks every level-coded name |
| Worksets | Per §5.7 (per-volume worksets preferred). Never model on `Workset1` |
| Rooms | Placed and named before the first coordination share; bounding elements correct |
| Families | From the issued library. Loadable families carry the project's shared parameters |
| Systems | MEP elements must be connected into real systems — system data drives the tag, the schedules and the point list |
| Detail level | Model to the stage LOD (§2); do not over-model early |
| Phases | Use the project phases as issued; do not create local phases |
| Linked models | By Shared Coordinates, pinned, and never bound into your model |
| Purge | Purge unused before every share; report file size in the share note |
| Warnings | Review Revit warnings before sharing; zero critical warnings at a gate |

**The one rule that makes tagging trustworthy:** every element must be attributable to a volume. Choose **one** method per model and tell the BIM Manager which — **per-volume worksets** (`TE_Mechanical`, `HS_Architecture`…) *(preferred)*, or **one model per volume** (set the volume once on Project Information). Either way, **place rooms before the first coordination share.** Elements with no room, no workset and no volume are silently assigned to `TE` (Temple) and reported as low-confidence at every gate until fixed.

---

## 9 — Information requirements by stage

Geometry alone does not satisfy a stage. The data below must be present **on the element**, and is checked automatically at every gate. This is the LOIN for KUT.

### 9.1 General requirement at each stage

| LOD | Stage | Geometry | Data required on every element |
|---|---|---|---|
| 200 | Deliverable A | Present; generic/placeholder families permitted | Asset identifier |
| 300 | Deliverable B | Present; **no placeholder or generic families** — a real type is required | Asset identifier |
| 350 | Deliverable C / conformed | As 300 | Asset identifier, product code |
| 400 | Construction | As 350; a **manufacturer type** is required | Asset identifier, product code, model reference |
| 500 | Deliverable D | As 400, **verified against the installed element** | As 400, plus §9.3 |

### 9.2 Additional requirements by category

Categories not listed follow the general rule above.

| Category | From LOD 300 | From LOD 350 | From LOD 400 |
|---|---|---|---|
| Mechanical / Electrical equipment | System type | Product code | Model ref, manufacturer |
| Lighting fixtures | System type | Product code | Model ref, manufacturer |
| **Plumbing fixtures** | System type | Product code | Model ref, manufacturer, **maintenance type** |
| Air terminals, sprinklers, fire alarm devices | System type | Product code | Model ref, manufacturer |
| Electrical fixtures | System type | Product code | Model ref, manufacturer |
| Ducts, pipes, conduits, cable trays + fittings | System type | — | — |
| Doors, windows | — | Product code | Model ref, manufacturer |
| Casework, specialty equipment, furniture | — | Product code | Model ref, manufacturer |
| Curtain panels and mullions | — | Product code | Model ref, manufacturer |
| Walls, floors, roofs, stairs, framing, columns, foundations | — | Product code | — |
| Ceilings, railings, ramps | — | — | Product code |

> **Plumbing fixtures gain a maintenance-type requirement at LOD 400** that did not apply earlier. The construction gate is the first point at which it is tested — start capturing it at the beginning of Stage 3.1.

### 9.3 Asset data for handover (LOD 500)

Captured **during construction**, reported monthly, verified at the Deliverable D gate. It cannot be reconstructed at close-out.

The requirement is **tiered by what the asset is**. A requirement applied uniformly to every element cannot be met: a serial number on each of several thousand luminaires produces a register completed to perhaps 40%, which the FM team cannot rely on for any of it. A smaller requirement met in full is worth more than a complete one met in part. Full schedule: **BEP §14**.

| Tier | Covers | Categories |
|---|---|---|
| **A — Serialised plant** | Individually commissioned, carries a nameplate, under a service contract or on the BMS | Mechanical equipment, electrical equipment, specialty equipment (incl. baptistry plant, AV, lift equipment) |
| **B — Maintainable devices** | High quantity, maintained, but no meaningful individual serial | Lighting fixtures, plumbing fixtures, air terminals, sprinklers, fire alarm devices, electrical fixtures |
| **C — Warranted fabric** | No serial, no maintenance regime, but a warranty to claim against | Roofs, curtain panels and mullions, doors, windows, casework and joinery |
| **FF&E** | Reconciled to the FF&E record | Furniture, furniture systems |
| **D — Everything else** | Identified, not asset-managed | All other categories |

| Data | A | B | C | FF&E |
|---|---|---|---|---|
| Asset identifier | ✓ | ✓ | ✓ | ✓ |
| Manufacturer and model | ✓ | ✓ | — | ✓ |
| Unique asset reference | ✓ | — | — | — |
| **Serial number** | ✓ | — | — | — |
| **Loop and address** | — | fire alarm devices only | — | — |
| Installation date | ✓ | ✓ | ✓ | ✓ |
| Supplier | ✓ | ✓ | ✓ | ✓ |
| Warranty guarantor | ✓ | — | ✓ | — |
| Warranty duration | ✓ | ✓ | ✓ | ✓ |
| Warranty expiry date | ✓ | — | ✓ | — |
| Expected service life | ✓ | ✓ | — | — |
| Maintenance interval | ✓ | — | — | — |
| Recommended spares | ✓ | — | — | — |
| Commissioning date | ✓ | — | — | — |
| FF&E reference | — | — | — | ✓ |

> **Fire alarm devices carry loop and address instead of a serial number.** That is the identifier the cause-and-effect schedule, the panel and future maintenance actually use.

**Conventions.** Dates are `YYYY-MM-DD` (held as text — a mixed convention only surfaces at close-out, so hold the line early). Warranty durations are whole years, parts and labour recorded separately where they differ. Warranty **expiry** is recorded rather than start: it is the date that triggers action, and duration is captured alongside it so the start remains derivable.

**Who does what.** The Contractor captures at installation and commissioning; the BIM Manager verifies completeness monthly by tier and volume. **A tier below 95% at the Deliverable D gate is a gate failure, not an observation.** A programme that leaves this to Stage 3.3 will not achieve Deliverable D within the 60 days following furniture installation.

---

## 10 — The stages, in order

Each stage states: **entry** (what must be true to start), **who does what**, **deliverables**, and **exit** (the gate to pass).

### 10.0 Stage 0 — Mobilisation · M0–M1

**Entry:** appointment in place; CDE provisioned.

| Who | Does |
|---|---|
| BIM Manager | Stands up the CDE folders and permissions; fixes the shared coordinate system, levels and grids; issues the project template, family library and title blocks; applies the KUT standards overlay (`_BIM_COORD/`); issues this playbook, the BEP and the MIDP; wires the STINGTOOLS ↔ ACC sync and the ACC Model Coordination clash matrix; runs the kickoff |
| Lead AP | Confirms the design programme and the volume/level register; **issues the originator-code register**; nominates Task Team Managers |
| Every consultant | Nominates a Task Team Manager; returns a TIDP; confirms software versions; attends the kickoff; sets up their WIP area |
| Owner | Issues the EIR and their BIM standards; confirms the originator-code register |

**Deliverables:** BEP · MIDP · this playbook · project template + family library + title blocks · TIDPs from every discipline · CDE live with permissions set.

**Exit / gate:** every discipline has produced a *test model* from the issued template, shared it once through the CDE, and passed the pre-share checklist. **A discipline that has not done this does not start Stage 2.1.**

### 10.1 Stage 2.1 — Basis of Design · Deliverable A / BOD · LOD 200 · M1

**Entry:** Stage 0 exit met.

| Who | Does |
|---|---|
| Architecture | Massing, volumes, primary circulation, gross areas; rooms placed |
| Structure | Primary grid, indicative frame and foundations |
| MEP | Plant-space allocation, primary routes, indicative loads |
| Civil / Site | Site model, levels, access, drainage strategy |
| QS | First order of cost from the model |
| BIM Manager | First federation; first clash run (gross clashes only); baseline model-health report; area/programme audit against the Owner's brief; generate the BOD document |

**Deliverables:** discipline models at LOD 200 · federated model · area schedule vs brief · first clash report · Deliverable A drawing set.

**Data drop / exit:** issue at suitability `S2`/`S3`; gross spatial clashes resolved; areas reconciled to the brief; naming and data audit passed; Owner review comments closed.

### 10.2 Stage 2.2 — Developed Design · Deliverable B (50%) · LOD 300 · M2–M4

**Entry:** Deliverable A signed off.

| Who | Does |
|---|---|
| Architecture / Interiors | Real geometry, correctly located; door/window schedules begin; room finishes structured; FF&E begins |
| Structure | Sized members, real foundations, penetrations coordinated with MEP |
| MEP | Real equipment, sized primary distribution, plant rooms coordinated, risers fixed |
| Fire / LV | Detection, suppression and containment strategies modelled |
| QS | BOQ from the model (NRM2) |
| BIM Manager | Fortnightly federate → clash → issue cycle in force; drawing production; register and transmittal for the B drop; monthly status report; first FF&E/finishes export to Fohlio |

**Deliverables:** models at LOD 300 · 50% drawing set · BOQ · clash reports with issues closed · updated MIDP · Deliverable B transmittal (suitability `S2`).

**Exit / gate:** zero unresolved high-priority clashes; LOD 300 verification passed; naming/data audit passed; review comments closed; BOQ issued.

### 10.3 Stage 2.3 — Technical Design · Deliverable C (100%) · LOD 350 · M5–M8

**Entry:** Deliverable B signed off.

| Who | Does |
|---|---|
| All disciplines | Interfaces and connections resolved; builders' work and penetrations agreed; details modelled where they drive coordination |
| Interiors | FF&E and finishes complete in the model and reconciled with Fohlio; room data sheets for key spaces |
| MEP | Systems complete and connected; equipment carries manufacturer/model data; BMS points identified |
| Specification lead | CSI sections assigned; **RIB SpecLink** table of contents reconciled against the model |
| QS | Tender BOQ |
| BIM Manager | Full drawing production; LOD 350 verification; specification reconciliation; FF&E currency check; begin COBie population if required; gate pack |

**Deliverables:** models at LOD 350 · 100% drawing set · tender BOQ · room data sheets · FF&E schedule · specification reconciliation report · Deliverable C transmittal (suitability `S4` for stage approval → `A1` when authorised).

**Exit / gate:** LOD 350 verification passed; zero unresolved clashes; specification gaps closed or accepted; FF&E linked; review comments closed.

### 10.4 Stage 2.4 — Tender · M9–M10

Tender set issued from the CDE at `A1`. Queries answered as formal RFIs and logged. No model changes except by instruction.

**Exit:** tender issued and receipted; RFI log open and current.

### 10.5 Stage 2.5 — Conformed set · M11

Addenda and tender-stage changes incorporated; the set is regenerated and reissued as the conformed baseline against which construction proceeds.

**Exit:** conformed set published; the register shows every superseded revision archived.

### 10.6 Stage 3.1 — Construction administration · LOD 400 · M12–M43

**Entry:** conformed set published; contractor mobilised.

| Who | Does |
|---|---|
| Contractor | Shop drawings and fabrication models; as-built capture as work proceeds; RFIs through the CDE; asset data capture at installation/commissioning |
| Design team | RFI responses; revisions issued with clouds and revision data; site queries |
| Specialists | Fabrication-level models (steel, MEP modules, façade) linked into the federation |
| Controls contractor | Niagara station build; point naming agreed against the model |
| BIM Manager | Monthly federation and clash; revision control; register maintenance; monthly status report; monthly asset-data completeness by tier/volume; commissioning point list prepared from the model |

**Note on LOD 400:** elements must be fabrication/installation-ready. For plumbing fixtures this now includes a **maintenance-type** value on every fixture — check it early rather than at the gate.

**Exit:** construction information complete; as-built capture current to within one month.

### 10.7 Stage 3.2 — FF&E installation · LOD 400 · M44–M47

FF&E installed; the model and the Fohlio record reconciled item by item; finishes verified against the installed condition.

**Exit:** FF&E schedule reconciled; no unlinked FF&E items; O&M data collected in Fohlio.

### 10.8 Stage 3.3 — Close-out · Deliverable D · LOD 500 · M48–M49

Within 60 days of furniture installation.

| Who | Does |
|---|---|
| Contractor | Final as-built information; commissioning records; warranties and O&M documents |
| Controls contractor | Live Niagara station reconciled against the model's equipment and points |
| Design team | Verification that the record model reflects the constructed building |
| BIM Manager | LOD 500 verification; asset-data completeness; COBie handover data (if required); final register, transmittal and archive; digital-twin baseline |

**Deliverables:** verified record model at LOD 500 · asset/equipment register · reconciled BMS point register · COBie 2.4 + O&M (if required) · final drawing register · archive → **Owner**.

**Exit / gate:** LOD 500 verification passed; asset data complete (every tier ≥95%); handover data accepted by the Owner.

---

## 11 — MIDP & TIDP

**TIDP — Task Information Delivery Plan.** One per discipline. The list of what you will deliver, when, at what LOD, in what format. Owned by the **Task Team Manager**.

**MIDP — Master Information Delivery Plan.** All TIDPs aggregated into the project master. Owned by the **BIM Manager**. It is the single answer to "what is due, from whom, when?"

```
  TIDP (Arch/Int) ┐
  TIDP (Struct)   ├──── the BIM Manager aggregates ────▶  MIDP (master schedule)
  TIDP (MEP)      ┘                                        owned & maintained by the BIM Manager
  TIDP (…)        ┘
```

### 11.1 Who owns what

| Document | Drafted by | Approved by | Maintained by |
|---|---|---|---|
| TIDP | Each Task Team Manager | BIM Manager | Task Team Manager |
| MIDP | **BIM Manager** | Lead Appointed Party (Symbion) | **BIM Manager** |

### 11.2 When they are produced and updated

| When | Action |
|---|---|
| Mobilisation (M0–M1) | Every discipline returns a TIDP; BIM Manager aggregates into the **MIDP baseline** |
| Each stage start (2.1, 2.2, 2.3, 3.x) | TIDPs reviewed and re-baselined for the stage; MIDP reissued |
| Each data drop (B, C, D) | Actual dates recorded; RAG status updated |
| Monthly | MIDP reissued with the status report — flag slippage, re-forecast |
| On any scope/programme change | Any date change is agreed and **re-baselined — never silently slipped** |

> **Rule of thumb:** TIDPs feed the MIDP; the MIDP is the truth. Never let them drift apart.

### 11.3 TIDP columns (use exactly these)

`Ref · Discipline · Originator · Deliverable · Type · Stage · LOD · Format · Suitability · CDE State · Planned Rel Month · Planned Date · Actual Date · Responsible · TIDP Ref · RAG · Notes`

A starter file with these columns and the project's baseline rows is issued at mobilisation: `MIDP/KUT_MIDP_TEMPLATE.csv` (TIDP template: `TIDPs/KUT_TIDP_TEMPLATE.xlsx`). STINGTOOLS' register exports keep the deliverable register in sync so the MIDP is not a hand-maintained spreadsheet.

---

## 12 — The operating rhythm & coordination cycle

During the long middle you run the **same loop** continuously — this is most of the day-to-day. The coordination cycle is **fortnightly** (weekly progress notes keep it visible between cycles).

### 12.1 The calendar

| Cadence | What happens | Who | Output |
|---|---|---|---|
| **Daily** | Author in WIP. Nothing leaves WIP without the checklist. Auto-tagging and WIP saves | Task teams (Revit + STINGTOOLS) | — |
| **Weekly (Tue)** | Task Team Managers post a short progress note to the CDE: what changed, what is blocked, what is coming | TTMs | Progress note |
| **Fortnightly (Wed) — the coordination cycle** | Share by 12:00 → federate → clash → grouped report issued by 17:00 | All + BIM Mgr (ACC + Navisworks + STINGTOOLS) | Clash report + issues |
| **Fortnightly (Fri) — coordination meeting** | Walk the open issues in the federated model; assign and date every one | Lead AP chairs | Minutes + issue assignments |
| **Monthly** | Status report: model health, compliance, clash burn-down, review close-out, FF&E currency, asset-data completeness | BIM Mgr (KPI dashboard) | Monthly report |
| **Per gate** | Gate audit, sign-off pack, transmittal, publish | BIM Mgr + Lead AP | Gate pack |
| **Per drop** | MIDP updated; register reissued | BIM Mgr | Updated MIDP + register |

### 12.2 The fortnightly cycle in detail

```
Mon ─ Tue        author in WIP
Wed 12:00        SHARE — every discipline shares to the CDE at S1
Wed 12:00-17:00  BIM Manager federates (Navisworks + ACC), runs clash, groups and prioritises
Wed 17:00        clash report + issues issued; 48 hours to pre-read
Thu ─ Fri        disciplines review their issues
Fri (meeting)    coordination meeting — walk the model, assign, date
Following week   resolve; re-share on the next cycle
```

**The 48-hour rule.** The report is issued 48 hours before the meeting so people arrive having read it. A meeting spent discovering clashes is a wasted meeting.

### 12.3 Meetings — who attends and what happens

| Meeting | Frequency | Chair | Attendees | Purpose |
|---|---|---|---|---|
| Coordination | Fortnightly | Lead AP | All TTMs + BIM Mgr | Resolve clashes and interfaces |
| Design team | Weekly | Lead AP | Design leads | Design decisions (not a BIM meeting) |
| BIM / information | Monthly | BIM Mgr | TTMs | Standards, data quality, MIDP, lessons |
| Owner review | Per gate | Owner | All | Review and comment |
| Gate sign-off | Per gate | Lead AP | Owner + BIM Mgr + leads | Accept the deliverable |
| Site progress | Weekly (3.1+) | Contractor | Site team + design | Construction issues |

---

## 13 — QA, clash & validation

### 13.1 Quality gates — what "passed" actually means

A gate is passed when **all** of the following are true and evidenced in the gate pack:

| # | Check | Evidence |
|---|---|---|
| 1 | Every deliverable in the MIDP for this stage is present at the right suitability | MIDP with actual dates |
| 2 | Naming and container compliance | Standards audit report — zero errors |
| 3 | Asset data completeness for the stage | Completeness report (with element counts) |
| 4 | LOD verification at the stage's LOD | LOD verification report + CSV |
| 5 | Zero unresolved high-priority clashes | Clash report with issue status |
| 6 | Owner review comments closed | Review close-out report |
| 7 | Model health within tolerance | Model-health report |
| 8 | Register and transmittal issued | Drawing register + transmittal receipt |
| 9 | *(2.3 onward)* Specification reconciled | Spec gap report (RIB SpecLink vs model) |
| 10 | *(2.3 onward)* FF&E linked and current | FF&E currency report |

> **An empty check is not a pass.** If a report says "100%" over zero elements, that is a scope error, not a green light. Every report in the gate pack must state how many elements it examined.

The BIM Manager runs the gate audit and issues the pack; the Lead Appointed Party and the Owner accept.

### 13.2 Clash priorities

| Priority | Definition | Must be resolved |
|---|---|---|
| **P1 — Critical** | Hard clash between permanent elements; or a clash blocking construction sequence | Before the next gate. Always |
| **P2 — Major** | Hard clash resolvable by routing/offset; access or maintenance space compromised | Within two cycles |
| **P3 — Minor** | Soft clash, tolerance or clearance issue | Before the stage gate |
| **P4 — Note** | Observation, no action required yet | Logged only |

### 13.3 Clash process

1. BIM Manager federates and runs the clash on the shared models (Navisworks + ACC Model Coordination).
2. Clashes are **grouped** (one issue per real problem, not per intersection) and prioritised.
3. Each issue is assigned to a discipline with a date, and tracked as an ACC Issue (BCF).
4. Report issued 48 hours before the coordination meeting.
5. Meeting walks the open issues in the model; every issue leaves with an owner and a date.
6. Resolution appears in the next share; the issue is closed with evidence.

> **Clashes are not a scoreboard.** An issue assigned to your discipline is not a criticism; an issue hidden until the gate is.

### 13.4 Tolerances

| Interface | Clearance |
|---|---|
| Structure vs MEP | `[FILL]` mm hard, plus maintenance access |
| MEP vs MEP | `[FILL]` mm |
| Access / maintenance space around plant | Per manufacturer, minimum `[FILL]` mm |
| Ceiling void allocation | Per the agreed services zoning drawing |

---

## 14 — Deliverables & handover

### 14.1 FF&E and finishes — Fohlio

**Principle: link, never duplicate.** Fohlio is the Owner's source of truth for FF&E, finishes and O&M. The model carries a **reference** to the Fohlio record; it does not hold a competing copy.

| Stage | What happens |
|---|---|
| Mobilisation | Room-finish and FF&E parameters bound; the field mapping agreed with Fohlio (`_BIM_COORD/fohlio_map.json`); one shared identifier agreed per element |
| 2.2 onward | Each cycle: finishes and FF&E exported in Fohlio's shape via STINGTOOLS → Interior Designer enriches in Fohlio (products, images, prices, suppliers, lead times, O&M) → enriched data imported back, **matched by Room Number**, with a diff preview before anything is written |
| 2.3 | Room data sheets and the FF&E schedule generated from the reconciled data |
| Monthly | Currency check — model vs Fohlio — reported as a KPI line |
| 3.2–3.3 | Installed FF&E reconciled; asset identifiers aligned for handover |

> **Room numbers are the key.** A room renumbered without telling the BIM Manager silently breaks the FF&E match for that room.

### 14.2 Specifications — CSI MasterFormat and RIB SpecLink

CSI sections are assigned to model elements from Stage 2.3. The **RIB SpecLink** table of contents is reconciled against the model each gate, producing three lists: **specified but not modelled**, **modelled but not specified**, and **title mismatches**. Each is closed or formally accepted before the gate passes. *(RIB SpecLink authors the written specification — it is separate from Speckle, which is the data/viewer layer.)*

### 14.3 Building-services operation — Niagara

The model states what equipment and points *should* exist; the Niagara station states what *is* running. Keeping the two aligned is the digital twin.

| Stage | What happens |
|---|---|
| 2.3 | Serviceable MEP elements carry their BMS data (point name, protocol, system). Point-naming convention agreed with the MEP team and controls contractor |
| 3.1 (~M40) | Commissioning point list exported from the model (`Niagara_ExportPoints`) for the controls contractor to load — model-driven, not hand-built |
| 3.3 | Reconcile (`Niagara_Reconcile`): model equipment and points vs the live station. Differences (missing, renamed, extra) resolved with the controls contractor |

**Deliverable:** a reconciled record model plus an equipment/point register — the **digital-twin baseline**.

### 14.4 Handover data — COBie 2.4 (if the Owner requires it)

If COBie is required in the EIR, it is produced from the model at close-out — covering Facility, Floor, Space, Type, Component, System, Spare, Job and Document. It is only as good as the asset data captured during Stages 3.1–3.3 — **which is why asset data is a construction-stage activity, not a close-out scramble.**

### 14.5 Registers, templates and where things live

| Item | Owner | Issued | Location |
|---|---|---|---|
| BIM Execution Plan | BIM Mgr | Mobilisation, updated per stage | `BEP/` |
| This playbook | BIM Mgr | Mobilisation, updated as needed | `00 Project Standards and Control/` |
| MIDP | BIM Mgr | Mobilisation, monthly & per drop | `MIDP/` |
| TIDP (per discipline) | Task Team Manager | Mobilisation, re-baselined per stage | `TIDPs/` |
| Numbering convention | BIM Mgr | Mobilisation | `KUT_Drawing_and_Document_Numbering_Convention.md` |
| Document Control Standard | BIM Mgr | Mobilisation | `KUT_Document_Control_Standard.docx` |
| Drawing register & transmittal | BIM Mgr | Every drop / every issue | `KUT_Drawing_Register_and_Transmittal_TEMPLATE.xlsx`, `Document Templates/` |
| Standards overlay (STINGTOOLS) | BIM Mgr | Mobilisation | `_BIM_COORD/` (owner_standards, lod_matrix, tag_schemes, fohlio_map) |
| Clash / coordination report | BIM Mgr | Every cycle | ACC |
| Room data sheets | Arch/Interiors + BIM Mgr | 2.3 onward | ACC |
| FF&E schedule | Interiors + BIM Mgr | 2.2 onward | Fohlio ↔ ACC |
| Monthly status report | BIM Mgr | Monthly | ACC |
| COBie + O&M (if required) | BIM Mgr + Contractor | 3.3 | ACC |

---

## 15 — Leading and teaching the team

You are judged on whether the **team** can follow the system, not just you. Keep onboarding simple and visual.

### 15.1 Day-one essentials (every consultant must know)

| # | They must know | Why |
|---|---|---|
| 1 | The CDE states (WIP→Shared→Published→Archived) | So nothing is issued wrongly |
| 2 | The container numbering (§5) | So files are findable & valid |
| 3 | Shared coordinates / origin — never move it | So all models line up |
| 4 | Units = mm, agreed levels & grids | No scale/level chaos |
| 5 | How to share/publish in ACC | The fortnightly rhythm |
| 6 | The clash & issue workflow | How conflicts get fixed |
| 7 | Tagging/data via STINGTOOLS (one click) | Consistent data |
| 8 | LOD per stage (what "done" means now) | No over/under-modelling |
| 9 | The data-drop calendar (A/B/C/D dates) | Everyone hits deadlines |
| 10 | No email issuing, no rogue families | Governance |

### 15.2 Joining the project (day one) — do these six things before modelling

1. Get your CDE access and confirm you can see WIP, Shared and Published.
2. Read §5 (numbering), §6 (CDE) and §8 (modelling standards) — plus the companion modelling playbook. 30 minutes.
3. Download the project template, family library and title blocks. **Start from the template.** Do not migrate an old project file.
4. Confirm your `[ORG]` originator code and your volume/workset convention with your Task Team Manager.
5. Produce a test model — one element, correctly named, tagged, in the right workset and volume — and share it once.
6. Attend the next coordination meeting as an observer.

### 15.3 Change, risk and escalation

**Changing a standard.** Any change to numbering, the template, the family library or the LOD matrix goes through the BIM Manager, is recorded in the BEP, and is issued to everyone. **No local variants.** If something here does not work for your discipline, raise it — do not work around it silently.

| Situation | Raise to | Within |
|---|---|---|
| A standard is unclear or unworkable | BIM Manager | Immediately |
| You will miss a share | Task Team Manager → BIM Manager | Before the deadline, not after |
| A clash cannot be resolved within your discipline | Coordination meeting | Same cycle |
| A design decision is blocking information | Lead Appointed Party | Same week |
| A gate is at risk | BIM Manager → Lead AP → Owner | Two weeks before the gate |

**The risks we are actively managing:**

| Risk | Mitigation |
|---|---|
| Owner's standards arrive after mobilisation and change naming | Standards are held as configuration (`_BIM_COORD/`), not hand-work — adopting them is an edit, not a rework |
| Originator codes unresolved | Assigned in Week 1, before any numbering |
| Volume attribution weak (no rooms/worksets) | Rooms placed before the first share; confidence reported at every gate |
| FF&E drift between model and Fohlio | Monthly currency check reported as a KPI |
| Asset data left to close-out | Asset data is a 3.1 activity with monthly tier/volume reporting |
| As-built capture lagging | Capture current to within one month, checked monthly |
| Late specialist models (steel, façade, MEP modules) | Named in the MIDP with dates from 2.3 |

---

## Appendices

### A1 — Pre-share checklist (pin this)

Before moving anything from WIP to Shared:

- [ ] Model opens from the issued template lineage; coordinates unchanged
- [ ] Units millimetres; levels and grids unmodified
- [ ] Correct workset / volume for every element
- [ ] Rooms placed and named
- [ ] MEP elements connected into real systems
- [ ] No rogue families; no CAD acting as model geometry
- [ ] Purged; file size reported
- [ ] Revit warnings reviewed; no critical warnings
- [ ] Container named exactly per §5
- [ ] Suitability code set honestly (§6.3)
- [ ] Revision incremented
- [ ] Asset data / tag completeness checked (STINGTOOLS audit)
- [ ] Share note written: what changed, what is not yet resolved

### A2 — Gate pack contents

- MIDP extract for the stage with actual dates
- Standards / naming audit report
- Data completeness report (with element counts)
- LOD verification report + CSV
- Clash report with issue status and burn-down
- Model-health report
- Review comment close-out report
- Drawing register + transmittal receipt
- *(2.3+)* Specification reconciliation report (RIB SpecLink vs model)
- *(2.3+)* FF&E currency report

### A3 — Kickoff agenda (half day)

| Time | Item | Lead |
|---|---|---|
| 0:00 | Project, programme, gates, and who is who | BIM Mgr |
| 0:20 | ISO 19650 in fifteen minutes: CDE, states, suitability, revisions | BIM Mgr |
| 0:35 | **The numbering system** (§5) — worked examples, then a live exercise | BIM Mgr |
| 1:05 | The CDE: folders, permissions, how to share, how to publish | BIM Mgr |
| 1:25 | Break | — |
| 1:35 | The modelling standards (§8) and the pre-share checklist (App. A1) | BIM Mgr |
| 2:00 | Live demo: publish → clash → issue in ACC; STINGTOOLS tagging | BIM Mgr |
| 2:20 | The fortnightly cycle and the meeting rhythm | BIM Mgr |
| 2:40 | Clash: priorities, grouping, how issues are assigned and closed | BIM Mgr |
| 3:00 | TIDPs: what we need back and by when — each lead confirms | All |
| 3:30 | Discipline breakouts — one-to-one with each team | All |
| 4:00 | Close: the three rules, and where to get help | BIM Mgr |

### A4 — Golden rules (poster)

1. **Start from the template.** Never migrate an old file.
2. **Name it right, first time.** §5 is not a guideline.
3. **Rooms before you share.**
4. **Connect your systems.**
5. **Set suitability honestly.** `S1` is a promise.
6. **Model to the stage LOD** — no more, no less.
7. **Tag / data-check before you share.**
8. **Zero high-priority clashes at every data drop.**
9. **Read the clash report before the meeting.**
10. **Never move the origin.**
11. **Issue through the CDE. Always.**
12. **Raise it early.** Nothing gets cheaper by being hidden.

### A5 — Acronyms

| | | | |
|---|---|---|---|
| **BIM** | Building Information Modelling | **CDE** | Common Data Environment |
| **EIR** | Exchange Information Requirements | **BEP** | BIM Execution Plan |
| **MIDP / TIDP** | Master / Task Information Delivery Plan | **LOD / LOIN** | Level of Development / Level of Information Need |
| **ACC** | Autodesk Construction Cloud | **RACI** | Responsible / Accountable / Consulted / Informed |
| **COBie** | Construction-Operations Building information exchange | **BMS** | Building Management System (Niagara) |
| **RFI / TQ** | Request for Information / Technical Query | **RDS** | Room Data Sheet |

---

## Companion documents

Kept in the same folder (`00 Project Standards and Control/`) — read this playbook alongside them:

- **KUT_BIM_MODELLING_PLAYBOOK.md** — the full modelling method (worksets, families, parameters, phases, tagging workflow). This playbook only summarises it (§8).
- **BIM Execution Plan (BEP)** — `BEP/` — the contractual statement of *what* we do. Where this playbook and the BEP disagree, **the BEP prevails** and this playbook is corrected.
- **MIDP** — `MIDP/` — the master schedule of every deliverable and date.
- **KUT_Drawing_and_Document_Numbering_Convention.md** — the authoritative numbering standard (§5 is its working summary).
- Supporting: `KUT_Document_Control_Standard.docx`, `RACI and Roles/KUT_RACI_Responsibility_Matrix.xlsx`, `Document Templates/` (transmittal & notices), `_BIM_COORD/` (STINGTOOLS standards overlay).

---

*Internal working guide for the Kampala Uganda Temple project. Author: Mayanja Davis (BIM Manager), Symbion Consulting Group Studios. Questions to the BIM Manager. Where this playbook and the BEP disagree, the BEP prevails and this document is corrected. Fields marked `[FILL]` / `[ORG]` are completed at mobilisation.*
