# KUT BIM Manager Playbook
### Your detailed, step-by-step guide to running the Kampala Uganda Temple (KUT) project as Information Manager

> **Who this is for:** the BIM / Information Manager (Planscape) on the Kampala Uganda Temple, appointed as sub-consultant to **Symbion Consulting** (the Lead Appointed Party), delivering for the Owner (the Church).
> **What this is:** a plain-English manual for running this job from mobilisation through design, construction and handover, mapped to the 49-month programme. It tells you *what* to do, *which tool*, *how*, and *when*, and just as importantly *how to lead and teach the team*. It is written for a first-time BIM Manager: nothing is assumed.
> **Status:** living document. Update it as KUT evolves.

> **A note on tools.** The contractual project stack is **Revit, Autodesk Construction Cloud (ACC), Navisworks Manage, Fohlio, RIB SpecLink, Microsoft Teams and Niagara**. Any private productivity or quality-assurance tooling you use to work faster is your own, is not part of the contract, and is **never named in project documents**. This playbook therefore describes the *work* in terms of the standard, contractual tools, so everything here is safe to show a consultant or the client. Where a task is tedious, do it however you like in private; the team only ever sees the standard process and the standard output.

---

## How to use this playbook

Read **Parts 0–6 first** (the fundamentals, the stack, the standards you will enforce). Use **Parts 7 onward as your working manual** once appointed. Parts 8–14 are the heart of this guide for a first job: how to lead, how to teach, your week-by-week rhythm, the gate checklists, troubleshooting, ready-to-use scripts, and how to keep yourself steady.

| If you need to… | Go to |
|---|---|
| Understand BIM / ISO 19650 in plain English | Part 1 |
| See how the whole project flows | Part 2 |
| Understand each tool | Part 3 |
| Compare the two Fohlio options | Part 4 |
| Produce the MIDP and TIDPs | Part 5 |
| Know and enforce the standards (file naming + the 8-segment element tag) | Part 6 |
| Follow the phase-by-phase delivery steps | Part 7 |
| Lead the team and run coordination meetings | Part 8 |
| Teach the team (kickoff + ongoing training) | Part 9 |
| Run your daily / weekly / monthly rhythm | Part 10 |
| Pass each gate (A / B / C / D) | Part 11 |
| Fix common problems | Part 12 |
| Grab an email / agenda / report script | Part 13 |
| Steady yourself in your first job | Part 14 |
| Quick-reference cheat sheets | Part 15 |

---

# PART 0 — The project on one page

| Item | Detail |
|---|---|
| **Project** | Kampala Uganda Temple — "KUT". Six buildings (~6,597 m2) on a six-acre site in Kampala's central business district. Developed from the Owner's prototype with site adaptation. |
| **Buildings** | Temple (2,449 m2), Meetinghouse (1,312 m2), Housing/Ancillary (2,554 m2), Grounds (93 m2), Utility (166 m2), Guard House (23 m2) |
| **Owner / Client** | The Church — the **Appointing Party** (sets the requirements, receives the asset) |
| **Lead consultant** | **Symbion Consulting** — the **Lead Appointed Party** (coordinates the team, owns the BEP). You report to Symbion. |
| **Your role** | **BIM / Information Manager** (Planscape) — you run the *information management function*: the CDE, the standards, coordination, data quality, and handover verification. You coordinate and verify; you do not author the discipline models. |
| **Programme** | **49 months** — Phase 2 (design + tender) = 11 months; Phase 3 (construction admin + handover) = 38 months |
| **Delivery standard** | ISO 19650. Authored in **Autodesk Revit**; the CDE is **Autodesk Construction Cloud (ACC)**; federation and clash run in **Navisworks Manage** |
| **Specifications** | **RIB SpecLink** |
| **FF&E / O&M** | **Fohlio** |
| **Meetings** | **Microsoft Teams** |
| **Operations / smart building** | **Niagara** (Tridium) BMS for the live building and the digital-twin phase |

**Your one-sentence job:** *"I make sure the right information, to the right quality, reaches the right person at the right time — coordinated, traceable, and ready for handover."*

---

# PART 1 — BIM fundamentals

You do not need to be the best modeller in the room. As BIM Manager you own the **process**. Here is the language you must be fluent in.

## 1.1 What is BIM, really?
BIM (Building Information Modelling) is not "3D drawings." It is a way of working where the building is built **digitally first** as a coordinated model that carries **data** (not just geometry), so that everyone — architect, engineer, contractor and the eventual operator — works from **one trusted source of information**.

> **Say it simply:** *"BIM is a process for creating and managing information across the whole life of an asset. The 3D model is the visible part; the value is the structured data and the coordinated way of working."*

## 1.2 What is ISO 19650?
ISO 19650 is the international standard for **managing information** on BIM projects. It says *who produces what, to what quality, when, and where it is stored*. It is the rulebook your job is built on.

| Part | Covers | You use it for |
|---|---|---|
| **ISO 19650-1** | Concepts and principles | The vocabulary |
| **ISO 19650-2** | **Delivery phase** (design and construction) | The day-to-day of Phases 2 and 3 |
| **ISO 19650-3** | **Operational phase** (running the building) | Handover and the Niagara / digital-twin stage |
| **ISO 19650-5** | Security-minded approach | Access control on a sensitive (temple) project |

> **Current note (accurate as of 2026):** a **draft revision (DIS) went out for public consultation in March 2026**, with the final expected in 2027. It merges the delivery and operational phases into a single whole-life information-management cycle, shifts the language from "BIM" to "information management," and renames the BEP the "Information Production Plan." Say it as *"a draft is out for consultation,"* not *"it has changed,"* and you are exactly right.

## 1.3 The roles (know exactly where you sit)

```
APPOINTING PARTY  (the Church / Owner)
        |  sets requirements (EIR), receives the asset
        v
LEAD APPOINTED PARTY  (Symbion Consulting)
        |  coordinates the team, owns the BEP
        v
APPOINTED PARTIES  (task teams: Arch, Struct, MEP, Civil ... and Planscape)
        |  produce information for their task
        v
   YOU = the Information Management function
   (delegated by the Lead to the Information Manager — that is you)
```

- **Appointing Party** = the client. Issues the **EIR** (what information they want).
- **Lead Appointed Party** = Symbion. Owns the **BEP** (how the team will deliver it).
- **Appointed Parties** = each discipline / task team.
- **You** = run the information management function on the Lead's behalf: the CDE, the standards, coordination, audits and handover verification.

## 1.4 The key documents (the paper trail of BIM)

| Acronym | Name | In plain English | Who owns it |
|---|---|---|---|
| **EIR** | Exchange Information Requirements | The client's shopping list of information | Appointing Party (Church) |
| **BEP** | BIM Execution Plan | The team's answer: "here is how we will deliver it" | Lead (Symbion) — drafted by **you** |
| **MIDP** | Master Information Delivery Plan | The master schedule of every deliverable + date | **You** |
| **TIDP** | Task Information Delivery Plan | Each discipline's slice of the MIDP | Each task team lead |
| **RACI** | Responsibility matrix | Who is Responsible / Accountable / Consulted / Informed | **You** |

> **Memory hook:** *EIR asks → BEP answers → MIDP schedules → TIDP delivers.*

## 1.5 The CDE — the heart of everything
The **Common Data Environment (CDE)** is the single online place where all project information lives. For KUT this is **ACC**. Every file moves through **four states**:

```
WIP  ──share for──▶  SHARED  ──approve for──▶  PUBLISHED  ──superseded──▶  ARCHIVED
(your team only)    (team can see          (client-approved,          (history, audit)
                     & coordinate)          contractual)
```

- **WIP** = work in progress, only your team sees it.
- **Shared** = released so other disciplines can coordinate against it.
- **Published** = formally issued / approved (contractual).
- **Archived** = previous versions, kept for the record.

> **Say it simply:** *"Nothing is issued by email. Everything moves through the CDE states with the right suitability and revision code, so we always know what is current and who approved it."*

## 1.6 LOD — how 'finished' the model is

| LOD | Meaning | KUT stage |
|---|---|---|
| **200** | Generic, approximate size / shape / location | 2.1 Basis of Design |
| **300** | Specific, accurate geometry | 2.2 Developed Design (50%) |
| **350** | + interfaces / connections to other elements | 2.3 Technical Design (100%) |
| **400** | + fabrication / installation detail | 3.1 / 3.2 Construction and FF&E |
| **500** | Verified **as-built** | 3.3 Close-out / handover |

> Modern term to use: **LOIN** (Level of Information Need) — it covers geometry **plus data plus documentation** together, not just the 3D detail.

## 1.7 The 30-second glossary

| Term | Meaning |
|---|---|
| **Federated model** | All discipline models combined into one coordination model |
| **Clash detection** | Finding where elements physically conflict (a duct through a beam) |
| **Data drop / Information Exchange** | A scheduled moment when information is formally delivered |
| **Worksets** | How a Revit model is split so several people can work at once |
| **Shared coordinates / project base point** | The agreed origin so every model lines up |
| **Suitability code** | A label (S2, A1…) saying what a file may be used for |
| **Transmittal** | A formal record of "these files were issued to these people on this date" |

---

# PART 2 — How the whole project flows

## 2.1 The ISO 19650 delivery cycle, mapped to KUT

```
1. ASSESSMENT & NEED    -> Client EIR (what information is required)        before you
2. TENDER / APPOINTMENT -> Your proposal, capability, the BEP              <- you are here
3. MOBILISATION         -> Stand up the CDE, standards, templates, train   Month 0-1
4. COLLABORATIVE PRODUCTION (the long middle)                              Month 1-49
   model -> share -> clash -> coordinate -> issue, repeat each cycle
5. INFORMATION MODEL DELIVERY -> each data drop (B, C, D)                  at each stage
6. HANDOVER / OPERATIONS -> Fohlio O&M + Niagara digital twin              Month 44-49+
```

## 2.2 The programme with timing
Months are relative (M1 = first month after appointment).

| Stage | Deliverable | LOD | Duration | Months |
|---|---|---|---|---|
| 2.1 | Basis of Design (BOD) — Deliverable A | 200 | 1 | M1 |
| 2.2 | Developed Design — Deliverable B (50%) | 300 | 3 | M2–M4 |
| 2.3 | Technical Design — Deliverable C (100%) | 350 | 4 | M5–M8 |
| 2.4 | Tender, negotiation, award | — | 3 | M9–M11 |
| 2.5 | Conformed set | 350 | within above | M11 |
| 3.1 | Building supervision | 400 | 32 | M12–M43 |
| 3.2 | FF&E installation supervision | 400 | 4 | M40–M43 |
| 3.3 | Close-out — Deliverable D | 500 | 2 | M44–M45 |

## 2.3 The repeating coordination cycle (your rhythm)
During the long middle you run the **same loop** continuously. In **design (Phase 2)** it runs **fortnightly**; in **construction (Phase 3)** it slows to **monthly**. This loop is roughly 80% of your day-to-day job.

```
publish -> federate + clash -> coordination meeting -> assign & track issues -> report
(teams ->  (you, Navisworks)  (Teams, live model)     (ACC Issues, BCF)        (monthly KPI)
 ACC Shared)
```

| Cadence | What happens | Tool |
|---|---|---|
| **Daily** | Discipline modelling, WIP saves, element tagging in the model | Revit |
| **Fortnightly (design) / monthly (construction)** | Publish to ACC Shared, federate and clash, coordination meeting, issue tracking | ACC + Navisworks + Teams |
| **Monthly** | Model-health and KPI report, data-quality audit, MIDP review | ACC + your register |
| **Per stage (A/B/C/D)** | Formal data drop, transmittal, client review | ACC |

---

# PART 3 — The technology stack (plain English)

Think of the tools as a line for information: **author → coordinate → specify/procure → operate.**

```
AUTHOR        COORDINATE                SPECIFY / PROCURE        OPERATE
Revit  --->   ACC (CDE, Issues)  <-->   Fohlio (FF&E + O&M)      Niagara (BMS / twin)
              Navisworks (federate,     RIB SpecLink (specs)
              detailed clash)           Microsoft Teams (meet)
```

## 3.1 Autodesk Revit — the authoring tool
**What it is:** the software each discipline uses to build the 3D model, drawings and schedules.
**Your job with it:** set the template, the shared coordinates, the levels and grids, worksets, the controlled family library, and the naming and tagging standards, so every model is consistent. You verify; you do not author.

## 3.2 Autodesk Construction Cloud (ACC) — the CDE + coordination
**What it is:** the cloud home for all files (Docs), a cloud clash engine (Model Coordination), and the issue tracker (Issues).
**Your job with it:** set up the folder structure with the four CDE states and per-state permissions, publish and share models, run continuous cloud clash, raise and track issues as BCF, and run the formal data drops and transmittals.
**Key parts you will use:** Docs (the CDE), Model Coordination (continuous cloud clash), Issues (BCF), Reviews and approvals, Transmittals.

## 3.3 Navisworks Manage — federation + detailed clash
**What it is:** the desktop tool where you combine every discipline model into one **federated model** and run **rule-based clash detection** with priorities and tolerances.
**Your job with it:** on your workstation, rebuild the federated model each cycle, run the clash matrix (which disciplines are checked against which), group and prioritise clashes, and publish the federated views and clash reports back into ACC for the team. ACC Model Coordination gives continuous cloud clash; Navisworks gives you the heavy, controlled, reportable clash for each cycle and gate.

## 3.4 Fohlio — FF&E specification, procurement and O&M
**What it is:** a platform for Furniture, Fittings and Equipment specs, procurement, and the operations/maintenance data that goes with them. It holds the product library, specs, budgets and supplier info.
**How KUT uses it:** the model carries the items; Fohlio carries the rich product / cost / supplier / maintenance data. One shared identifier links a model element to its Fohlio record so they never become two competing entries. See Part 4 for the two integration options.

## 3.5 RIB SpecLink — specifications
**What it is:** the Owner-required specification platform. All disciplines write their specs here.
**Your job with it:** SpecLink stays authoritative for specifications. You archive each issued spec set with the milestone deliverables, and reconcile spec sections against the model at each gate (a model element with no spec section, or a spec section with no model content, is a gap to surface before tender).

## 3.6 Microsoft Teams — meetings
**What it is:** the meeting and call platform. Weekly design coordination meetings and ad-hoc coordination calls run here.
**Your job with it:** chair the coordination session against the **live federated model** shared on screen, and upload the meeting materials to ACC immediately after, as required.

## 3.7 Niagara (Tridium) — the building's nervous system (operations)
**What it is:** a Building Management System framework that connects HVAC, lighting, metering, security and IoT devices, regardless of manufacturer, over protocols such as BACnet, Modbus, oBIX and REST.
**How KUT uses it:** in the operational phase and during commissioning, the live building's equipment is connected through Niagara. The MEP model defines what equipment should exist and its "points" (for example a BACnet sensor on an air-handling unit). At handover you export that point list from the model and **reconcile the model against what is actually live in Niagara** — the foundation of a digital twin.
> **Say it simply:** *"Revit tells us what equipment should exist; Niagara tells us what is actually running. Keeping the two in sync is the digital twin."*

## 3.8 How they connect

| From → To | What flows | How |
|---|---|---|
| Revit → ACC | Models, sheets | Publish to ACC Docs |
| Discipline models → Navisworks | Geometry | Federate; run clash |
| Navisworks → ACC Issues | Clashes as BCF | Export / push BCF; assign owners |
| Revit ↔ Fohlio | FF&E items + finishes | Revit add-in / CSV (Option A) or REST API (Option B) |
| Model → Niagara | Equipment "points" | Point list exported from the MEP model |
| Model → Operator | Asset / maintenance data | Fohlio O&M (COBie only if the Owner requires it) |

---

# PART 4 — Fohlio: the two integration options

## 4.1 Option A — file / add-in route (no API key needed) — available today
Uses Fohlio's official **Revit add-in** plus a simple **CSV export/import** you run each cycle. No developer API key — just a normal Fohlio account.

**Step by step (per cycle / data drop):**
1. In Revit, export the room finishes (floor / wall / ceiling / base) and the FF&E list to a CSV.
2. Upload that CSV into Fohlio (or push family data via the Fohlio Revit add-in).
3. In Fohlio, the team enriches: products, images, prices, suppliers, lead times, O&M data.
4. Export the enriched list from Fohlio (CSV).
5. Back in Revit, import it, matching by **Room Number**, preview the diff, and write the data back.

**Pros:** works now, no key, full control, you decide what syncs and when.
**Cons:** manual trigger each cycle, point-in-time (not live).

## 4.2 Option B — live REST API route (needs a Fohlio API key / tier)
Reads and writes items, specs and finishes directly through the Fohlio REST API — two-way and automatic.
**Pros:** automated, near real-time, no re-keying. **Cons:** needs an API key / higher tier and has vendor lead time.

## 4.3 Recommendation
**Use Option A from day one; pursue Option B in the background.** You never wait on a vendor key to start delivering; when the key lands, the same workflow switches to live sync with no rework. Ask Fohlio support for their **CSV import template / column spec** (free) and align your export columns to it, so Option A is nearly seamless.

---

# PART 5 — Master and Task schedules (MIDP / TIDP)

## 5.1 What they are
- **TIDP** = one discipline's list of "what we will deliver and when" (the MEP team's models, drawings, schedules + dates).
- **MIDP** = all the TIDPs combined into the project master schedule — the single calendar of every deliverable.

```
TIDP (Arch) ┐
TIDP (Struct)├── you aggregate ──▶ MIDP (master schedule, owned & maintained by YOU)
TIDP (MEP)  ┘
```

## 5.2 Who owns what

| Document | Drafted by | Approved by | Maintained by |
|---|---|---|---|
| TIDP | Each task team lead | You | Task team lead |
| MIDP | **You** | Lead (Symbion) | **You** |

## 5.3 When to produce / update

| Moment | Action |
|---|---|
| **Mobilisation (M0–M1)** | Collect a TIDP from every discipline; assemble and **baseline** the first MIDP |
| **Start of each stage** | Re-confirm TIDPs for the upcoming stage; reissue the MIDP |
| **At every data drop (A/B/C/D)** | Check actual vs planned; update status |
| **Monthly** | Light review: flag slippage, re-forecast |
| **On any scope / programme change** | Re-baseline and re-issue |

> **Rule of thumb:** TIDPs feed the MIDP; the MIDP is the truth. Never let them drift apart.

## 5.4 Your ready-made templates
You already have working templates in `KUT2026 / 00 Project Standards and Control`:
- **`KUT_MIDP_TEMPLATE.csv`** — the master schedule (Ref, Discipline, Originator, Deliverable, Type, Stage, LOD, Format, Suitability, CDE State, Planned/Actual, Responsible, TIDP Ref, RAG, Notes).
- **`KUT_TIDP_TEMPLATE.xlsx`** — one tab per discipline, pre-filled from the MIDP, with the same columns so completed tabs stack straight back into the MIDP. Hand each lead their tab; they confirm and extend it and return it to you.

> Keep the MIDP a live register, not a hand-maintained spreadsheet. Drive it from the drawing/document register so the deliverable list and the issued reality never disagree.

---

# PART 6 — The standards you enforce

This is what you set up at mobilisation and audit at every share. The full written rules are in **`KUT_Document_Control_Standard.docx`**; this is the working summary. There are **two naming systems and they are different things**: one names the **files** (6.1), the other tags the **elements inside the models** (6.2). Do not confuse them.

## 6.1 File / container naming — pure ISO 19650 (seven fields)
Every information container (model, drawing, schedule, document) is named with the ISO 19650 field convention. No legacy or numeric drawing-number scheme.

```
KUT - PLNS - TE - GF - DR - E - 0001
 |     |      |    |    |    |    +- Number (4-digit sequence)
 |     |      |    |    |    +------ Role / discipline (A,S,M,E,P,F,C,I,Q,Z)
 |     |      |    |    +----------- Type (M3 model, DR drawing, SH schedule, SP spec, RP report...)
 |     |      |    +---------------- Level / Location (B1, GF, 01, 02, RF, ZZ, XX)
 |     |      +--------------------- Volume / System = building (see codes)
 |     +---------------------------- Originator (PLNS Planscape; one 4-char code per firm)
 +---------------------------------- Project (KUT)
```

**Volume (building) codes:** `TE` Temple · `MH` Meetinghouse · `HS` Housing/Ancillary · `GB` Grounds · `UB` Utility · `GH` Guard House · `ZZ` project-wide · `XX` none/external.
Two metadata fields travel with every file but are not in the name: **suitability** (S2…) and **revision** (P01…). The register template builds this reference automatically from the fields.

## 6.2 Element / asset tagging — the ISO eight-segment tag
Every **element inside a model** (a duct, a panel, a door, an air-handling unit) carries an eight-segment tag held in shared parameters. This is **how the model carries data**, and it is what drives schedules, quantities, the asset register, O&M / handover and the Niagara point list. It is separate from the file name.

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
  M  - TE  - Z01  - GF  - HVAC - SUP  - AHU - 0001
```

| Segment | Meaning | Example codes |
|---|---|---|
| **DISC** | Discipline | M, E, P, A, S, F, C |
| **LOC** | Location / building | TE, MH, HS, GB, UB, GH, EXT |
| **ZONE** | Zone within the building | Z01–Z04, ZZ |
| **LVL** | Level | B1, GF, 01, 02, RF |
| **SYS** | System | HVAC, DCW, DHW, SAN, RWD, LV, FP |
| **FUNC** | Function | SUP, RET, EXH, HTG, PWR, LTG |
| **PROD** | Product code | AHU, FCU, DB, DR, WC, LUM |
| **SEQ** | 4-digit sequence | 0001, 0042 |

Worked example: `M-BLD1-Z01-GF-HVAC-SUP-AHU-0001` = a Mechanical, Temple, Zone 01, Ground Floor, HVAC system, Supply-function Air Handling Unit, number 1.

**Why it matters:** the eight-segment tag is the spine of your data. Quantities, the asset register, the O&M pack, the COBie export (if required) and the Niagara points all read from it. If elements are untagged, the model looks fine but carries no usable data, and the gap only shows up at handover when it is most expensive. **Tag and data completeness is checked before every Share** (target 95% or better). The exact code lists live in the project tag scheme; agree them at mobilisation and never let them drift.

## 6.3 Suitability codes and CDE states

| Code | Meaning | CDE state |
|---|---|---|
| **S0** | WIP / initial | WIP |
| **S1** | Shared — for coordination | Shared |
| **S2** | Shared — for information | Shared |
| **S3** | Shared — for review and comment | Shared |
| **S4** | Shared — for stage approval | Shared |
| **A1…An** | Published — authorised (contractual) | Published |
| **B1…Bn** | Published — with comments | Published |

**Revision codes:** `P01, P02…` while preliminary; `C01, C02…` once contractual.

## 6.4 LOD by stage

| Stage | LOD | What "done" means |
|---|---|---|
| 2.1 BOD | 200 | Massing, generic systems |
| 2.2 Dev Design (B) | 300 | Real geometry, located correctly |
| 2.3 Tech Design (C) | 350 | Connections / interfaces resolved |
| 3.1 / 3.2 Construction, FF&E | 400 | Fabrication / installation-ready |
| 3.3 Close-out (D) | 500 | Verified as-built |

## 6.5 The non-negotiables (these are in the BEP)
- One agreed **shared coordinate system / project base point** — set at mobilisation, never changed.
- **Units = millimetres**; agreed level and grid names.
- **Controlled family library only** — no rogue families, no imported CAD used as model geometry.
- Everything issued **through the CDE**, never by email.
- **Tag and data completeness checked before every Share** (95% or better).
- **Zero unresolved high-priority clashes** at each data drop.
- **Security-minded access** (ISO 19650-5) — temple project; control who sees what.

---

# PART 7 — The phase-by-phase delivery playbook

For each step: **what to do · which tool · how · when.**

## STAGE 0 — Mobilisation (Month 0–1) — before any modelling

| # | Action | Tool | How |
|---|---|---|---|
| 0.1 | Draft the **BEP** (answer the EIR) | Word + the standard | Use `KUT_BIM_Execution_Plan.docx` as the base; fill the `[FILL]` items; get it signed |
| 0.2 | Stand up the **CDE**: 4 states + per-state permissions | ACC Docs | Mirror the KUT2026 scaffold; WIP private, Shared = team, Published = client, Archived = read-only |
| 0.3 | Fix the **shared coordinate system** from the survey | Revit | Set the project base point + true north; **lock it** |
| 0.4 | Set the **Revit template** (coords, levels, grids, worksets, family library) | Revit | One project template; controlled families only |
| 0.5 | Apply the **naming + tag scheme** | the standard | File naming (6.1) + the 8-segment element tag (6.2), agreed and circulated |
| 0.6 | **Localise** the Owner's US standards to Ugandan practice | — | Paper sizes, units, code and terminology — a discrete task |
| 0.7 | Collect **TIDPs**, build and **baseline the MIDP** | the templates | One TIDP tab per discipline → aggregate |
| 0.8 | Set up **Navisworks** federation + the **clash matrix** | Navisworks | Which disciplines clash vs which; tolerances |
| 0.9 | Set up **ACC Model Coordination + Issues** | ACC | Continuous cloud clash + BCF issue tracking |
| 0.10 | **Train the consultants** (Part 9) | Teams | Half-day kickoff + sign the BEP |

> **Deliverable:** approved BEP + live CDE + locked coordinates + baselined MIDP + trained team. *Get this right and the rest runs itself.*

## STAGE 2.1 — Basis of Design · LOD 200 · Month 1
Each discipline models massing + generic systems to LOD 200 (Revit). First federation + gross clash sweep (Navisworks + ACC). Capture the BOD document. Baseline model-health — expect it to be low at first; that is normal. **Data drop A:** issue BOD at suitability S2/S3.

## STAGE 2.2 — Developed Design / Deliverable B / 50% · LOD 300 · Months 2–4
Develop real geometry to LOD 300 (all disciplines). **Tag elements and populate data** (the 8-segment tag, in the model). Run the fortnightly loop (Part 10). Push clashes to ACC Issues as BCF with owners + due dates. Produce the 50% drawings on the standard title block; keep the register live. Start the BOQ (NRM2). Monthly KPI report. **Data drop B:** federated model + 50% drawings + schedules + transmittal, suitability S2 → client review.

## STAGE 2.3 — Technical Design / Deliverable C / 100% · LOD 350 · Months 5–8
Resolve interfaces and connections to LOD 350. **Drive clashes to zero high-priority.** Complete the element tagging and data. Full drawing set + details. Final BOQ for tender. Begin FF&E specs in Fohlio (Option A) and the spec sets in SpecLink. Begin asset-data capture (the 8-segment tag is the foundation). **Data drop C:** 100% technical set, suitability S4 (stage approval) → A1 when authorised.

## STAGE 2.4–2.5 — Tender + Conformed Set · Months 9–11
Issue tender documents (ACC Published, A1). Answer tenderer queries through ACC Issues / RFIs; track formally. Produce the **Conformed Set** post-award (incorporate addenda; reissue). This stage is a **watching brief** — lighter loading, query support and model currency.

## STAGE 3.1 — Construction Administration · LOD 400 · Months 12–43
Maintain the CDE for construction (contractor uploads, RFIs, submittals). Re-clash on changes (monthly cycle). Process RFIs / submittals / variations through ACC Issues; link to the model. Track revisions (revision clouds + revision schedule). **Run the progressive as-built protocol monthly** — this is what saves the 60-day close-out: the contractor clouds and references changes; you audit that capture every month and route confirmed changes to the owning design consultant, so the record model accretes across construction rather than being rebuilt at the end. Quarterly model-health audit. Commissioning prep: export the equipment **point list** from the MEP model for Niagara.

## STAGE 3.2 — FF&E Installation · LOD 400 · Months 40–43
Finalise FF&E specs + procurement in Fohlio. Sync FF&E + finishes to the model (Option A round-trip). Track installation and snagging through ACC Issues.

## STAGE 3.3 — Close-Out / Deliverable D · LOD 500 · Months 44–45
- **Day 1–10:** reconcile the contractor's final as-built docs against the progressively maintained model and the engineers' record sets.
- **Day 10–35:** verify the LOD 500 record model discipline by discipline — element asset data (the 8-segment tag), parameter completeness, naming compliance.
- **Day 35–50:** assemble the native model files, calculation / energy files and system program files.
- **Day 50–60:** connect the live building — reconcile the model against the Niagara BMS (the digital-twin baseline) — and support submission to the Owner; archive everything.

**Deliverable D:** as-built model + O&M (Fohlio) + digital-twin baseline → Owner. (COBie only if the Owner requires it; confirm scope.)

---

# PART 8 — How to lead the team

This is the part a first-timer most needs and a template never gives you.

## 8.1 Where your authority comes from
You are new, and you may be coordinating people more senior than you. Your authority is **not** your title or your years. It comes from three things:
1. **The signed BEP.** Once every party signs it, the rules are theirs, not yours. When someone slips you do not say "because I say so," you say "the BEP we all signed says models publish by Tuesday close." Get it signed *before* modelling starts.
2. **Evidence.** "MEP has 47 open high-priority clashes, here is the list and the owners" is unarguable. "I feel MEP is behind" is not. Always lead with the register and the numbers.
3. **Making Symbion look good.** You report to Symbion. Every clean gate report you hand them is your reputation.

## 8.2 Running the coordination meeting
The meeting is where coordination either happens or quietly fails. Chair it tightly.
- **Before:** issue the prioritised clash report 48 hours ahead (Thursday for a Friday meeting) so people arrive having seen their clashes.
- **Agenda = the clash / issue list, by priority.** Nothing else. Walk it top down.
- **Assign and track, do not solve.** The disciplines solve the clash; you make sure every clash leaves the room with a named owner and a due date in ACC Issues.
- **Time-box** to 45–60 minutes. If a clash needs design debate, park it to a side call with the two disciplines; do not let it eat the meeting.
- **Output:** the updated issue register, shared immediately; upload the materials to ACC after.

> Keeping-control script: *"Let's stay on the list. Clash 12, duct vs beam at GF Temple — Mechanical and Structural, can you own it and close by Tuesday? Good. Next."*

## 8.3 Managing up to Symbion and the Owner
- Make Symbion look good and **never surprise them**. Flag risks early, each with a named owner and a recommended action, so a problem reaches them as a decision, not a crisis.
- The Owner (the Church) is prescriptive, and their review comments must be resolved before a phase completes. Treat the **comment close-out log as sacred** and chase it daily, not the week before a gate.
- When you do not know something, say *"I will confirm and come back,"* then close the loop. The reliable one who always follows up earns more trust than the one who always has an answer.

## 8.4 Handling the hard situations (what to say)

| Situation | What to do | What to say |
|---|---|---|
| A lead publishes late | Private message first, not a public call-out | "Your model isn't in Shared yet and the clash run is at 4pm — can you publish by 2, or should I run this cycle without you and flag it?" |
| Someone moved the shared origin | Stop; do not let anyone build on it | "The origin has moved — every model will mis-align. I'm reverting to the locked base point; please re-acquire coordinates before your next save." |
| Rogue families / CAD imported as model | Reject at QA, before Share | "This came in with imported CAD as geometry — it can't go to Shared. Use the controlled family and re-publish." |
| A discipline keeps slipping | Make it visible in the MIDP RAG; escalate with an action | "Structural is amber two cycles running on Deliverable B. Recommend Symbion confirm the date or re-resource." |
| Scope creep onto you | Hold the boundary; log it | "Happy to help, but authoring that model is outside my coordination role; I'll note it and we can quote it as an extra if you'd like." |

## 8.5 Hold the boundary
You coordinate and verify; you do not author. When a consultant says "just fix it in the model for me," say no, politely — that is their authoring responsibility and your liability if you touch it. Help with the process, not the authoring.

## 8.6 When and how to ask for help
You are a sub-consultant; you do not have to know everything. Lean on Symbion's experience and the Owner's design manager. Keep a short list of who to ask for what: Symbion for client and commercial questions, the discipline lead for discipline detail, the Owner's design manager for standards intent. A good question early is strength; bluffing and being caught is the only real failure.

## 8.7 Protect yourself
- **Keep a written trail.** Log late inputs, decisions and instructions. When the contractor's as-built data arrives late (it will), you want evidence you flagged it.
- **Nothing beyond your scope proceeds without written instruction.**
- **Build continuity.** Document your setup so someone could step in, and take the leave you need; a one-person metronome burns out.

---

# PART 9 — How to teach the team

You are judged on whether the **team** can follow the system, not just you. Keep it simple and visual.

## 9.1 The half-day kickoff (run it at mobilisation, before any modelling)

| Time | Topic | Lead |
|---|---|---|
| 0:00 | Project, roles, programme | You |
| 0:30 | ISO 19650 basics + the four CDE states | You |
| 1:00 | File naming (6.1) **and** the 8-segment element tag (6.2) | You |
| 1:30 | Coordinates, units, levels, grids, family library | You |
| 2:00 | Live demo: publish → federate → clash → issue (ACC + Navisworks) | You |
| 2:45 | Tagging and data: how to tag, what 95% means, when it is checked | You |
| 3:15 | MIDP / TIDP: each lead confirms their dates **out loud** | All |
| 3:45 | Q&A + sign the BEP | All |

> Getting each lead to commit their dates out loud, in front of each other, is your enforcement leverage later.

## 9.2 What every consultant must know on day one

| # | They must know | Why |
|---|---|---|
| 1 | The four CDE states (WIP→Shared→Published→Archived) | So nothing is issued wrongly |
| 2 | The **file naming** convention | So files are findable and valid |
| 3 | The **8-segment element tag** | So the model carries usable data |
| 4 | **Shared coordinates / origin** — never move it | So all models line up |
| 5 | **Units = mm**, agreed levels and grids | No scale / level chaos |
| 6 | How to **publish / share** a model in ACC | The cycle |
| 7 | The **clash and issue** workflow | How conflicts get fixed |
| 8 | **LOD per stage** (what "done" means now) | No over- or under-modelling |
| 9 | The **data-drop calendar** (A/B/C/D dates) | Everyone hits deadlines |
| 10 | **No email issuing, no rogue families** | Governance |

## 9.3 How to teach the two naming systems (the part people get wrong)
Teach them as two different things, with one test:
- **File name = the box the information comes in.** Seven fields. Example `KUT-PLNS-TE-GF-DR-E-0001`. Use the register template — it builds the name from the fields, so they cannot get it wrong.
- **Element tag = the label on each thing inside the model.** Eight segments. Example `M-BLD1-Z01-GF-HVAC-SUP-AHU-0001`. It lives in shared parameters and carries the data to schedules and handover.
> **The one-line test to give them:** *"If it's a file, seven fields. If it's a thing inside the model, eight segments."*

## 9.4 How to teach publishing in ACC
1. Save your WIP.
2. Run the **pre-publish check**: naming valid, tags 95%+, no rogue families.
3. Publish to ACC **Shared** with the right suitability (S2 for information).
4. The fortnightly cycle picks it up; never email a model. *If it's not in the CDE, it doesn't exist.*

## 9.5 How to teach the clash / issue workflow
You run the clash; they receive issues. Each issue has an owner and a due date. To close an issue: fix it in your model, re-publish, then mark the issue resolved with a comment. Do not close an issue you have not actually fixed in the model.

## 9.6 Discipline-specific must-dos

| Discipline | Extra essentials |
|---|---|
| Architecture | Room / space data, finishes, door / window schedules, FF&E hosting |
| Structure | Grids / levels authority, foundation interfaces, rebar at LOD 350+ |
| MEP | System naming, clash priority, equipment data for O&M and the Niagara points |
| QS / Cost | BOQ / NRM2 link, FF&E cost via Fohlio |
| FM / Operator | O&M requirements, the Niagara point list |

## 9.7 Golden-rules poster (pin it)
> 1. Issue **only** through the CDE. 2. **Never** move the origin. 3. Model to the **stage LOD** — no more, no less. 4. **Tag and data-check before you Share.** 5. **Zero** high-priority clashes at every data drop. 6. If it isn't named right, it doesn't exist.

## 9.8 Keep teaching
- **30-minute refresher before each gate (A/B/C):** what the gate audit checks, the current top findings, how to self-clear before submission.
- **Contractor induction at construction start:** the CDE, the as-built capture workflow (cloud changes, monthly), and what Deliverable D verification will test.

---

# PART 10 — Your operating rhythm (the runbook)

This is your week, written out. In **design (Phase 2)** the loop is **fortnightly**; in **construction (Phase 3)** it slows to **monthly**. Same shape, slower beat. Protect this rhythm above almost everything: the discipline of the cycle is what produces coordination.

## 10.1 Daily (15–30 minutes)
- Skim ACC Issues for anything overdue or newly assigned; nudge owners on anything due today.
- Glance at the comment close-out log if a client review is open.
- Keep your own WIP (the federated model, the register) current.

## 10.2 The fortnightly loop, day by day (design stage)

| Day | You do | Tool |
|---|---|---|
| **Mon** | Remind teams to publish to Shared by Tue close; prep the agenda | Teams |
| **Tue (close)** | Teams publish their models to ACC Shared | ACC |
| **Wed** | Rebuild the federated model; run the clash matrix; group + prioritise | Navisworks |
| **Thu** | Issue the prioritised clash report (48h before the meeting); push clashes to ACC Issues with owners + due dates | ACC |
| **Fri** | Chair the coordination meeting over Teams against the live model; every clash leaves with an owner + due date; upload materials to ACC after | Teams + ACC |
| **Next week** | Owners resolve in their models and re-publish; you verify closures; carry opens into the next cycle | ACC |

## 10.3 Monthly
- Run the model-health / KPI report and send it to Symbion (Part 13.4).
- Data-quality audit: tagging %, naming compliance, unresolved clashes.
- MIDP review: actual vs planned, RAG, re-forecast slippage.
- In construction: the monthly as-built capture audit.

## 10.4 Per stage / gate
- **2–3 weeks out:** pre-gate refresher to the leads; chase open issues and comments.
- **At the gate:** run the gate audit (Part 11), write the compliance report, assemble the data drop, transmit, and start the client review clock.

## 10.5 Your KPI set (the monthly report)
Open clash count + fortnight-on-fortnight burn-down by discipline; model-health score per model; naming + metadata compliance %; exchange punctuality (published on time?); review-comment close-out rate; and in construction, as-built capture currency (days between a site change and the model update).

---

# PART 11 — Gate-by-gate checklists

Run these as a literal checklist. The output of every gate is a **written compliance report** you hand to Symbion.

## 11.1 Every gate (the common checklist)
- [ ] All discipline models published to Shared at the right suitability.
- [ ] File naming 100% valid.
- [ ] Element tagging + data ≥ 95% (the 8-segment tag).
- [ ] Zero unresolved high-priority clashes.
- [ ] Coordinates / levels / grids intact; units mm.
- [ ] Deliverables match the MIDP for this stage; register updated.
- [ ] Drawings on the standard title block; revision + sign-off populated (not placeholder).
- [ ] Transmittal prepared; suitability + reason for issue recorded.
- [ ] Written compliance report produced.

## 11.2 Deliverable A — Basis of Design (LOD 200, M1)
Baseline audit. Massing + generic systems present. BOD document issued. Expect low data completeness; record the baseline. Suitability S2/S3.

## 11.3 Deliverable B — Developed Design / 50% (LOD 300, M4)
Real geometry, located correctly. 50% drawings + schedules. BOQ started. Tagging well underway. The common checklist applies; the higher LOD 300 requirement governs. Suitability S2.

## 11.4 Deliverable C — Technical Design / 100% (LOD 350, M8)
Interfaces resolved; clashes at zero high-priority. Full drawing set + details. Final BOQ. FF&E specs begun in Fohlio; spec sets in SpecLink. Suitability S4 → A1 when authorised.

## 11.5 Conformed set (LOD 350, M11)
Addenda incorporated post-award; reissue. Conformance audit. A1.

## 11.6 Deliverable D — Close-out (LOD 500, M44–45)
Record model verified discipline by discipline: as-built geometry, the 8-segment asset data complete, naming compliant. Native files + calc / energy / system files assembled. Model reconciled to the Niagara BMS (the digital-twin baseline). O&M via Fohlio. Final transmittal + archive. (COBie only if required.)

---

# PART 12 — Troubleshooting (common problems → what to do)

| Problem | Likely cause | What to do |
|---|---|---|
| Models don't line up in Navisworks | Origin moved or coordinates not acquired | Revert to the locked base point; have the discipline re-acquire shared coordinates; re-federate |
| Clash count explodes overnight | A discipline made a big change without flagging | Filter to the new clashes; raise issues; ask disciplines to flag major moves before publishing |
| Tagging completeness is RED | Elements modelled but not tagged | Hard-gate it: no Share until 95%; budget tagging into the TIDP; chase per discipline |
| A discipline keeps missing the publish window | Resourcing or habit | Make it visible in the MIDP RAG; private nudge; escalate with a recommended action |
| Review comments piling up unresolved | No daily close-out discipline | Treat the close-out log as sacred; chase owners daily; report the close-out rate |
| Register full of placeholders | Sheet revision / sign-off not populated in Revit | Make populating revision + drawn/checked/approved part of the publish check |
| Files named wrong | People typing names by hand | Make them use the register template that builds the name from the fields |
| The 60-day close-out is slipping | As-built capture left to the end