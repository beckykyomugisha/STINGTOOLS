# KUT Project Delivery Playbook
### Kampala Uganda Temple — how the whole team delivers information, stage by stage

> **The issued document is `KUT_Project_Delivery_Playbook.docx`** (repo root), built by
> `tools/build_team_playbook.py`. This markdown is the working draft used to review content.
> Edit content here, then regenerate the `.docx` — do not hand-edit the Word file, or the two
> will drift apart.

**Issued by:** Planscape Consulting Engineers Ltd — Information Manager
**Audience:** every organisation and every person producing information on KUT — Architecture, Interiors, Structure, Mechanical, Electrical, Plumbing, Fire, Low-Voltage, Civil/Site, QS/Cost, and the Contractor and specialist subcontractors when they join.
**Status:** `[FILL: P01 — for issue at mobilisation]`
**Companion documents:** the BIM Execution Plan (`KUT_BEP_TEMPLATE.md`) is the contractual statement of *what* we do; this playbook is the working statement of *how* and *when* we do it. Where they disagree, the BEP wins and this playbook gets corrected.

---

## How to use this playbook

| If you are… | Read | Then keep open |
|---|---|---|
| Joining the project | §1, §2, §3, §4, §5 — then your discipline's row in §6 | §3 (numbering) and the pre-share checklist (§A1) |
| A Task Team Manager | All of it | §6 (your stage), §7 (the rhythm), §9 (gates) |
| Modelling day to day | §3, §5, §A1 | §A1 — pin it beside your screen |
| The Information Manager | All of it | §7, §9, §10 |
| The Contractor / specialists (from Stage 3.1) | §1, §3, §4, §6.6–6.8, §11 | §A1, §A3 |

**Three rules that make everything else work.** If you remember nothing else:

1. **Everything is issued through the CDE.** Never by email, never by WhatsApp, never on a stick. If it did not go through the CDE, it was not issued.
2. **Nothing is shared until it passes the pre-share checklist** (§A1). A model that fails the check wastes everyone's fortnight, not just yours.
3. **The numbering system is not negotiable and not improvisable** (§3). One wrong container name breaks the register, the transmittal, the clash report and the handover data at once.

---

# PART 1 — The project on one page

| | |
|---|---|
| **Project** | Kampala Uganda Temple (KUT) |
| **Appointing Party (Client)** | The Church — Special Projects Department |
| **Lead Appointed Party** | Symbion Consulting Group Studios |
| **Information Manager** | Planscape Consulting Engineers Ltd — Mayanja Davis |
| **Scope** | Temple + ancillary buildings, six volumes plus site |
| **Programme** | 49 months — Phase 2 (design) 11 months · Phase 3 (construction + close-out) 38 months |
| **CDE** | Autodesk Construction Cloud (ACC) — the single authoritative environment |
| **Authoring** | Autodesk Revit (2025), millimetres, shared coordinate system fixed at mobilisation |
| **Clash / coordination** | Navisworks + ACC Model Coordination; issues tracked as ACC Issues |
| **Review** | Bluebeam Studio sessions for Owner review; comments close out before a gate passes |
| **FF&E / finishes / O&M** | Fohlio — the Owner's single source of truth. The model **links** to it; it never duplicates it |
| **Specifications** | RIB SpecLink, CSI MasterFormat |
| **Building services operation** | Niagara (Tridium) BMS |
| **Handover data** | COBie 2.4 + O&M, aligned to the record model |

### The six volumes

| Code | Volume | Sheet/tag volume number |
|---|---|---|
| BLD1 | Temple | 01 |
| BLD2 | Meetinghouse | 02 |
| BLD3 | Housing / Ancillary | 03 |
| BLD4 | Grounds | 04 |
| BLD5 | Utility | 05 |
| BLD6 | Guard House | 06 |
| EXT | Site-wide / external works | 00 |

### The stages and what "done" means at each

| Stage | Name | Months | LOD | The gate |
|---|---|---|---|---|
| 0 | Mobilisation | M0–M1 | — | Kit issued, everyone trained, CDE live |
| 2.1 | Basis of Design — **Deliverable A** | M1 | 200 | Massing and generic systems coordinated |
| 2.2 | Developed Design — **Deliverable B (50%)** | M2–M4 | 300 | Real geometry, correctly located |
| 2.3 | Technical Design — **Deliverable C (100%)** | M5–M8 | 350 | Interfaces and connections resolved |
| 2.4 | Tender issue | M9–M10 | 350 | Tender set issued from the CDE |
| 2.5 | **Conformed set** | M11 | 350 | Addenda incorporated, set reissued |
| 3.1 | Construction administration | M12–M43 | 400 | Fabrication/installation-ready information |
| 3.2 | FF&E installation | M40–M43 | 400 | FF&E installed and reconciled to Fohlio |
| 3.3 | Close-out — **Deliverable D** | M44–M45 | 500 | Verified record model + handover data |

> **LOD 500 at Deliverable D.** This was previously stated as 400. It is now 500, with LOD 400 sitting at the construction stage. LOD 500 means *verified as-built* — the element matches what was actually installed, and carries its asset data (serial number, installation date). Plan for it from Stage 3.1, not from M44.

---

# PART 2 — Who does what

## 2.1 Roles

| Role | Held by | Owns |
|---|---|---|
| **Appointing Party** | The Church | The Exchange Information Requirements; acceptance of each deliverable |
| **Lead Appointed Party** | Symbion | The overall appointment; design leadership; chairs the design meetings |
| **Information Manager** | Planscape — Mayanja Davis | The CDE, the BEP, the MIDP, the standards, the QA gate, federation, clash management, registers, transmittals, handover data. **Coordinates and verifies — does not author design** |
| **Task Team Manager** (one per discipline) | Each consultant | Their model, their TIDP, their data quality, their sign-off before every share |
| **Modellers / technicians** | Each consultant | Day-to-day authoring to the standards in §3 and §5 |
| **QS / Cost** | `[FILL]` | Quantities and cost derived from the model |
| **Contractor** (from 3.1) | `[FILL]` | As-built capture, commissioning data, specialist models |
| **Controls / commissioning contractor** | `[FILL]` | Niagara station, point naming, commissioning records |
| **Interior Designer** | `[FILL]` | FF&E and finishes design, and the Fohlio record |

## 2.2 RACI — the activities that cross organisations

**R** = does the work · **A** = accountable · **C** = consulted · **I** = informed

| Activity | Info Mgr | Lead AP | Arch/Int | Struct | MEP | QS | Contractor |
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

**Read the Information Manager column carefully.** Planscape is accountable for *information*, not for design. If a clash needs a beam moved, the structural engineer moves it — the Information Manager only makes sure the clash is visible, tracked, and closed before the gate.

---

# PART 3 — The numbering system

This is the section people will come back to. Print it.

## 3.1 The container name — every file, model, drawing and document

```
KUT - PLN - 01 - GF - M3 - A - 0001
 │     │     │    │    │    │    └── Number       4 digits, sequential within its set
 │     │     │    │    │    └─────── Role         1 letter — the discipline (§3.3)
 │     │     │    │    └──────────── Type         2 chars — what kind of thing it is (§3.4)
 │     │     │    └───────────────── Level        2 chars — GF, 01, 02, B1, ZZ = all levels
 │     │     └────────────────────── Volume       2 digits — 01…06 per §1, 00 = site, ZZ = all
 │     └──────────────────────────── Originator   3 chars — the organisation that made it (§3.2)
 └────────────────────────────────── Project      always KUT
```

Separator is a hyphen. No spaces. Upper case throughout.

> **⚠ Decide this in Week 1: originator code length.** The automated check enforces **exactly 3 characters**, but Planscape's default code is the 4-character `PLNS`, and earlier draft guidance used `PLNS` in its examples. `KUT-PLNS-…` therefore fails the check today. Two options, and the Owner's register decides:
> **(a)** issue 3-character codes to every organisation (`PLN`, `SYM`, …) — cleaner, matches the check as written; or
> **(b)** widen the check to 3–6 characters, which is closer to normal ISO 19650 practice and lets firms keep recognisable codes.
> **Do not start numbering anything until this is settled.** Renumbering after Deliverable A is expensive and visible.

## 3.2 Originator codes

| Organisation | Code |
|---|---|
| Planscape Consulting Engineers | `[FILL: PLN or PLNS per the decision above]` |
| Symbion Consulting Group Studios | `[FILL]` |
| Architecture | `[FILL]` |
| Structure | `[FILL]` |
| MEP | `[FILL]` |
| Interiors | `[FILL]` |
| QS | `[FILL]` |
| Contractor | `[FILL]` |
| *(unknown / not applicable)* | `ZZZ` |

## 3.3 Role (discipline) codes — the permitted set

Only these eight are valid on KUT. A model or sheet carrying anything else fails the standards audit.

| Code | Discipline |
|---|---|
| `A` | Architecture / Interiors |
| `S` | Structural |
| `M` | Mechanical |
| `E` | Electrical |
| `P` | Plumbing / Public Health |
| `FP` | Fire protection |
| `LV` | Low voltage / communications |
| `G` | Civil / site |

## 3.4 Type codes

| Code | Meaning |
|---|---|
| `M3` | 3D model |
| `M2` | 2D model / drafting |
| `DR` | Drawing |
| `SH` | Sheet |
| `SC` | Schedule |
| `SP` | Specification |
| `RP` | Report |
| `CA` | Calculation |
| `RD` | Room data sheet |
| `MS` | Method statement |
| `PP` | Presentation |
| `CR` | Clash / coordination report |

## 3.5 Level codes

`B1` basement 1 · `GF` ground floor · `01`, `02`, `03`… upper floors · `RF` roof · `ZZ` all levels / not level-specific · `XX` not applicable

Level codes must match the Revit level names exactly as set in the project template. Do not invent local variants (`GRD`, `Ground`, `L00`).

## 3.6 Revision codes

| While… | Use | Example |
|---|---|---|
| Preliminary (pre-contract) | `P01`, `P02`, `P03`… | `P02` |
| Contractual (published) | `C01`, `C02`, `C03`… | `C01` |

The revision changes **only** when the container is re-issued through the CDE. Working saves do not consume revisions.

## 3.7 Suitability codes — what a file may be used for

| Code | Meaning | CDE state |
|---|---|---|
| `S0` | Work in progress — not for use by others | WIP |
| `S1` | Shared — for coordination | Shared |
| `S2` | Shared — for information | Shared |
| `S3` | Shared — for review and comment | Shared |
| `S4` | Shared — for stage approval | Shared |
| `A1`…`An` | Published — authorised, contractual | Published |
| `B1`…`Bn` | Published — authorised with comments | Published |

**A suitability code is a promise about how others may use your file.** `S2` means "you may read this but do not build on it". Marking WIP work as `S1` because a deadline is close is the single most damaging thing anyone can do on this project.

## 3.8 The asset tag — every modelled element

Every element carries an eight-segment identifier, built automatically from the data on the element:

```
M - BLD1 - Z01 - L02 - HVAC - SUP - AHU - 0003
│    │      │     │     │      │     │      └── Sequence, 4 digits
│    │      │     │     │      │     └───────── Product code (AHU, DB, DR…)
│    │      │     │     │      └─────────────── Function (SUP, HTG, PWR…)
│    │      │     │     └────────────────────── System (HVAC, DCW, SAN, LV…)
│    │      │     └──────────────────────────── Level
│    │      └────────────────────────────────── Zone
│    └───────────────────────────────────────── Location / volume
└────────────────────────────────────────────── Discipline
```

**What the team must do:** model in the right workset, in the right volume, with rooms placed, and the systems actually connected. The tag then fills itself. **What breaks it:** elements floating outside any room or volume, MEP elements not connected to a system, and copies pasted between volumes.

### The one modelling rule that makes tagging trustworthy

Every element must be attributable to a volume. Choose **one** per model and tell the Information Manager which:

- **Per-volume worksets** — name worksets `BLD2_Mechanical`, `BLD3_Architecture`, etc. *(preferred)*
- **One model per volume** — set the volume once on Project Information

Either way, **place rooms before the first coordination share.** Rooms are the strongest signal in the model. Elements with no room, no workset and no volume are silently assigned to BLD1 (Temple) and will be reported as low-confidence at every gate until fixed.

## 3.9 Worked examples

| Thing | Container name |
|---|---|
| Temple architectural 3D model, all levels | `KUT-XXX-01-ZZ-M3-A-0001` |
| Meetinghouse mechanical model | `KUT-XXX-02-ZZ-M3-M-0001` |
| Temple ground-floor GA plan sheet | `KUT-XXX-01-GF-SH-A-0100` |
| Site-wide drainage drawing | `KUT-XXX-00-ZZ-DR-P-0050` |
| Federated coordination model | `KUT-PLN-ZZ-ZZ-M3-Z-0001` |
| Clash report, cycle 07 | `KUT-PLN-ZZ-ZZ-CR-Z-0007` |
| Temple level 2 room data sheet | `KUT-XXX-01-02-RD-A-0210` |

---

# PART 4 — The CDE, and how information moves

## 4.1 The four states

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
- **ARCHIVED** — superseded, retained. Nothing is ever deleted.

## 4.2 What moves a file between states

| Move | Who authorises | What must be true |
|---|---|---|
| WIP → Shared | Task Team Manager | Pre-share checklist (§A1) passed and recorded |
| Shared → Published | Information Manager, on Lead AP approval | Gate audit passed (§9); review comments closed; register updated |
| Published → Archived | Information Manager | A superseding revision has been published |

## 4.3 Non-negotiables

- **Everything through the CDE.** Email is for conversation, never for issue.
- **One shared coordinate system and project base point**, fixed at mobilisation, never changed. Every model uses "Shared Coordinates" on link.
- **Units are millimetres.** Level and grid names come from the project template and are not renamed locally.
- **No rogue families.** Content comes from the issued library. New content is submitted for checking before use.
- **No CAD as model.** Imported CAD may underlay; it may never be the deliverable geometry.
- **Security-minded information management (ISO 19650-5).** This is a temple project. Access is by need. Do not post model images, plans or renders publicly, on social media, or in portfolios without written permission.

---

# PART 5 — Modelling standards every author follows

| Topic | The rule |
|---|---|
| Origin | Shared coordinate system from the template. Never move the project base point or survey point |
| Units | Millimetres |
| Levels & grids | From the template. Renaming requires the Information Manager's agreement — it breaks every level-coded name |
| Worksets | Per §3.8. Never model on `Workset1` |
| Rooms | Placed and named before the first coordination share; bounding elements correct |
| Families | From the issued library. Loadable families carry the project's shared parameters |
| Systems | MEP elements must be connected into real systems — system data drives the tag, the schedules and the point list |
| Detail level | Model to the stage LOD (§1); do not over-model early |
| Phases | Use the project phases as issued; do not create local phases |
| Linked models | By Shared Coordinates, pinned, and never bound into your model |
| Purge | Purge unused before every share; report file size in the share note |
| Warnings | Review Revit warnings before sharing; zero critical warnings at a gate |

---

# PART 5B — Information requirements by stage

Geometry alone does not satisfy a stage. The data below must be present **on the element**, and is
checked automatically at every gate.

## 5B.1 General requirement at each stage

| LOD | Stage | Geometry | Data required on every element |
|---|---|---|---|
| 200 | Deliverable A | Present; generic/placeholder families permitted | Asset identifier |
| 300 | Deliverable B | Present; **no placeholder or generic families** — a real type is required | Asset identifier |
| 350 | Deliverable C / conformed | As 300 | Asset identifier, product code |
| 400 | Construction | As 350; a **manufacturer type** is required | Asset identifier, product code, model reference |
| 500 | Deliverable D | As 400, **verified against the installed element** | As 400, plus §5B.3 |

## 5B.2 Additional requirements by category

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

> **Plumbing fixtures gain a maintenance-type requirement at LOD 400** that did not apply earlier.
> The construction gate is the first point at which it is tested — start capturing it at the
> beginning of Stage 3.1.

## 5B.3 Asset data for handover (LOD 500)

Captured **during construction**. It cannot be reconstructed at close-out.

| Category | Additional data at LOD 500 |
|---|---|
| Mechanical / Electrical equipment | Serial number, installation date |
| Lighting fixtures, plumbing fixtures | Serial number, installation date |
| Air terminals, sprinklers, fire alarm devices | Serial number, installation date |
| Specialty equipment | Serial number, installation date |
| Furniture, furniture systems | Installation date, FF&E reference |

> A programme that leaves this to Stage 3.3 will not achieve Deliverable D within the 60 days
> following furniture installation.

---

# PART 6 — The stages, in order

Each stage below states: **entry** (what must be true to start), **who does what**, **deliverables**, and **exit** (the gate you must pass).

## 6.0 STAGE 0 — Mobilisation · M0–M1

**Entry:** appointment in place; CDE provisioned.

| Who | Does |
|---|---|
| Information Manager | Stands up the CDE folders and permissions; fixes the shared coordinate system, levels and grids; issues the project template, family library and title blocks; issues this playbook, the BEP and the MIDP; runs the kickoff |
| Lead AP | Confirms the design programme and the volume/level register; nominates Task Team Managers |
| Every consultant | Nominates a Task Team Manager; returns a TIDP; confirms software versions; attends the kickoff; sets up their WIP area |
| Owner | Issues the Exchange Information Requirements and their BIM standards; confirms the originator-code register |

**Deliverables:** BEP · MIDP · this playbook · project template + family library + title blocks · TIDPs from every discipline · CDE live with permissions set.

**Exit / gate:** every discipline has produced a *test model* from the issued template, shared it once through the CDE, and passed the pre-share checklist. **A discipline that has not done this does not start Stage 2.1.**

---

## 6.1 STAGE 2.1 — Basis of Design · Deliverable A · LOD 200 · M1

**Entry:** Stage 0 exit met.

| Who | Does |
|---|---|
| Architecture | Massing, volumes, primary circulation, gross areas; rooms placed |
| Structure | Primary grid, indicative frame and foundations |
| MEP | Plant space allocation, primary routes, indicative loads |
| Civil/Site | Site model, levels, access, drainage strategy |
| QS | First order of cost from the model |
| Information Manager | First federation; first clash run (gross clashes only); baseline model-health report; area/programme audit against the Owner's brief |

**Deliverables:** discipline models at LOD 200 · federated model · area schedule vs brief · first clash report · Deliverable A drawing set.

**Exit / gate:** gross spatial clashes resolved; areas reconciled to the brief; naming and data audit passed; Owner review comments closed.

---

## 6.2 STAGE 2.2 — Developed Design · Deliverable B (50%) · LOD 300 · M2–M4

**Entry:** Deliverable A signed off.

| Who | Does |
|---|---|
| Architecture / Interiors | Real geometry, correctly located; door/window schedules begin; room finishes structured; FF&E begins |
| Structure | Sized members, real foundations, penetrations coordinated with MEP |
| MEP | Real equipment, sized primary distribution, plant rooms coordinated, risers fixed |
| Fire / LV | Detection, suppression and containment strategies modelled |
| QS | BOQ from the model |
| Information Manager | Fortnightly federate → clash → issue cycle in force; drawing production; register and transmittal for the B drop; monthly status report; first FF&E/finishes export to Fohlio |

**Deliverables:** models at LOD 300 · 50% drawing set · BOQ · clash reports with issues closed · updated MIDP · Deliverable B transmittal.

**Exit / gate:** zero unresolved high-priority clashes; LOD 300 verification passed; naming/data audit passed; review comments closed; BOQ issued.

---

## 6.3 STAGE 2.3 — Technical Design · Deliverable C (100%) · LOD 350 · M5–M8

**Entry:** Deliverable B signed off.

| Who | Does |
|---|---|
| All disciplines | Interfaces and connections resolved; builders' work and penetrations agreed; details modelled where they drive coordination |
| Interiors | FF&E and finishes complete in the model and reconciled with Fohlio; room data sheets for key spaces |
| MEP | Systems complete and connected; equipment carries manufacturer/model data; BMS points identified |
| Specification lead | CSI sections assigned; SpecLink table of contents reconciled against the model |
| QS | Tender BOQ |
| Information Manager | Full drawing production; LOD 350 verification; specification reconciliation; FF&E currency check; gate pack |

**Deliverables:** models at LOD 350 · 100% drawing set · tender BOQ · room data sheets · FF&E schedule · specification reconciliation report · Deliverable C transmittal.

**Exit / gate:** LOD 350 verification passed; zero unresolved clashes; specification gaps closed or accepted; FF&E linked; review comments closed.

---

## 6.4 STAGE 2.4 — Tender · M9–M10

Tender set issued from the CDE at `A1`. Queries answered as formal RFIs and logged. No model changes except by instruction.

**Exit:** tender issued and receipted; RFI log open and current.

## 6.5 STAGE 2.5 — Conformed set · M11

Addenda and tender-stage changes incorporated; the set is regenerated and reissued as the conformed baseline against which construction proceeds.

**Exit:** conformed set published; the register shows every superseded revision archived.

---

## 6.6 STAGE 3.1 — Construction administration · LOD 400 · M12–M43

**Entry:** conformed set published; contractor mobilised.

| Who | Does |
|---|---|
| Contractor | Shop drawings and fabrication models; as-built capture as work proceeds; RFIs through the CDE |
| Design team | RFI responses; revisions issued with clouds and revision data; site queries |
| Specialists | Fabrication-level models (steel, MEP modules, façade) linked into the federation |
| Controls contractor | Niagara station build; point naming agreed against the model |
| Information Manager | Monthly federation and clash; revision control; register maintenance; monthly status report; commissioning point list prepared from the model |

**Note on LOD 400:** at this stage elements must be fabrication/installation-ready. For Plumbing Fixtures this now includes a maintenance-type value on every fixture — a requirement that did not apply at earlier stages. Check it early rather than at the gate.

**Exit:** construction information complete; as-built capture current to within one month.

## 6.7 STAGE 3.2 — FF&E installation · M40–M43

FF&E installed; the model and the Fohlio record reconciled item by item; finishes verified against the installed condition.

**Exit:** FF&E schedule reconciled; no unlinked FF&E items; O&M data collected in Fohlio.

## 6.8 STAGE 3.3 — Close-out · Deliverable D · LOD 500 · M44–M45

Within 60 days of furniture installation.

| Who | Does |
|---|---|
| Contractor | Final as-built information; commissioning records; warranties and O&M documents |
| Controls contractor | Live Niagara station reconciled against the model's equipment and points |
| Design team | Verification that the record model reflects the constructed building |
| Information Manager | LOD 500 verification; asset data completeness; COBie handover data; final register, transmittal and archive |

**Deliverables:** verified record model at LOD 500 · asset/equipment register · reconciled BMS point register · COBie 2.4 + O&M · final drawing register · archive.

**Exit / gate:** LOD 500 verification passed; asset data complete; handover data accepted by the Owner.

---

# PART 7 — The operating rhythm

## 7.1 The calendar

| Cadence | What happens | Who | Output |
|---|---|---|---|
| **Daily** | Author in WIP. Nothing leaves WIP without the checklist | Task teams | — |
| **Weekly (Tue)** | Task Team Managers post a short progress note to the CDE: what changed, what is blocked, what is coming | TTMs | Progress note |
| **Fortnightly (Wed) — the coordination cycle** | Share by 12:00 → federate → clash → grouped report issued by 17:00 | All + Info Mgr | Clash report + issues |
| **Fortnightly (Fri) — coordination meeting** | Walk the open issues in the federated model; assign and date every one | Lead AP chairs | Minutes + issue assignments |
| **Monthly** | Status report: model health, compliance, clash burn-down, review close-out, FF&E currency | Info Mgr | Monthly report |
| **Per gate** | Gate audit, sign-off pack, transmittal, publish | Info Mgr + Lead AP | Gate pack |
| **Per drop** | MIDP updated; register reissued | Info Mgr | Updated MIDP + register |

## 7.2 The fortnightly cycle in detail

```
Mon ─ Tue        author in WIP
Wed 12:00        SHARE — every discipline shares to the CDE at S1
Wed 12:00-17:00  Information Manager federates, runs clash, groups and prioritises
Wed 17:00        clash report + issues issued; 48 hours to pre-read
Thu ─ Fri        disciplines review their issues
Fri (meeting)    coordination meeting — walk the model, assign, date
Following week   resolve; re-share on the next cycle
```

**The 48-hour rule.** The report is issued 48 hours before the meeting so people arrive having read it. A meeting spent discovering clashes is a wasted meeting.

## 7.3 Meetings — who attends and what happens

| Meeting | Frequency | Chair | Attendees | Purpose |
|---|---|---|---|---|
| Coordination | Fortnightly | Lead AP | All TTMs + Info Mgr | Resolve clashes and interfaces |
| Design team | Weekly | Lead AP | Design leads | Design decisions (not a BIM meeting) |
| BIM / information | Monthly | Info Mgr | TTMs | Standards, data quality, MIDP, lessons |
| Owner review | Per gate | Owner | All | Review and comment (Bluebeam session) |
| Gate sign-off | Per gate | Lead AP | Owner + Info Mgr + leads | Accept the deliverable |
| Site progress | Weekly (3.1+) | Contractor | Site team + design | Construction issues |

---

# PART 8 — MIDP and TIDP

**TIDP — Task Information Delivery Plan.** One per discipline. Your list of what you will deliver, when, at what LOD, in what format. Owned by the Task Team Manager.

**MIDP — Master Information Delivery Plan.** All TIDPs aggregated into the project master. Owned by the Information Manager. It is the single answer to "what is due, from whom, when?"

### When they are produced and updated

| When | Action |
|---|---|
| Mobilisation | Every discipline returns a TIDP; Info Mgr aggregates into the MIDP baseline |
| Each stage start | TIDPs reviewed and re-baselined for the stage |
| Each data drop | Actual dates recorded; RAG status updated |
| Monthly | MIDP reissued with the status report |
| On change | Any date change is agreed and re-baselined — never silently slipped |

### TIDP columns (use exactly these)

`Ref · Discipline · Originator · Deliverable · Type · Stage · LOD · Format · Suitability · CDE State · Planned Rel Month · Planned Date · Actual Date · Responsible · TIDP Ref · RAG · Notes`

A starter file with these columns and the project's baseline rows is issued at mobilisation (`KUT_MIDP_TEMPLATE.csv`).

---

# PART 9 — Quality gates: what "passed" actually means

A gate is passed when **all** of the following are true and evidenced in the gate pack:

| # | Check | Evidence |
|---|---|---|
| 1 | Every deliverable in the MIDP for this stage is present at the right suitability | MIDP with actual dates |
| 2 | Naming and container compliance | Standards audit report — zero errors |
| 3 | Asset data completeness for the stage | Completeness report |
| 4 | LOD verification at the stage's LOD | LOD verification report + CSV |
| 5 | Zero unresolved high-priority clashes | Clash report with issue status |
| 6 | Owner review comments closed | Review close-out report |
| 7 | Model health within tolerance | Model-health report |
| 8 | Register and transmittal issued | Drawing register + transmittal receipt |
| 9 | *(2.3 onward)* Specification reconciled | Spec gap report |
| 10 | *(2.3 onward)* FF&E linked and current | FF&E currency report |

> **An empty check is not a pass.** If a report says "100%" over zero elements, that is a scope error, not a green light. Every report in the gate pack must state how many elements it examined.

The Information Manager runs the gate audit and issues the pack; the Lead Appointed Party and the Owner accept.

---

# PART 10 — Clash and coordination

## 10.1 Priorities

| Priority | Definition | Must be resolved |
|---|---|---|
| **P1 — Critical** | Hard clash between permanent elements; or a clash blocking construction sequence | Before the next gate. Always |
| **P2 — Major** | Hard clash resolvable by routing/offset; access or maintenance space compromised | Within two cycles |
| **P3 — Minor** | Soft clash, tolerance or clearance issue | Before the stage gate |
| **P4 — Note** | Observation, no action required yet | Logged only |

## 10.2 Process

1. Information Manager federates and runs the clash on the shared models.
2. Clashes are **grouped** (one issue per real problem, not per intersection) and prioritised.
3. Each issue is assigned to a discipline with a date, and tracked as an ACC Issue.
4. Report issued 48 hours before the coordination meeting.
5. Meeting walks the open issues in the model; every issue leaves with an owner and a date.
6. Resolution appears in the next share; the issue is closed with evidence.

**Clashes are not a scoreboard.** An issue assigned to your discipline is not a criticism; an issue hidden until the gate is.

## 10.3 Tolerances

| Interface | Clearance |
|---|---|
| Structure vs MEP | `[FILL]` mm hard, plus maintenance access |
| MEP vs MEP | `[FILL]` mm |
| Access / maintenance space around plant | Per manufacturer, minimum `[FILL]` mm |
| Ceiling void allocation | Per the agreed services zoning drawing |

---

# PART 11 — The specialist information streams

## 11.1 FF&E and finishes — Fohlio

**Principle: link, never duplicate.** Fohlio is the Owner's source of truth for FF&E, finishes and O&M. The model carries a reference to the Fohlio record; it does not attempt to hold a competing copy.

| Stage | What happens |
|---|---|
| Mobilisation | Room finish parameters and FF&E parameters bound; the field mapping agreed with Fohlio; one shared identifier agreed per element |
| 2.2 onward | Each cycle: finishes and FF&E exported in Fohlio's shape → Interior Designer enriches in Fohlio (products, images, prices, suppliers, lead times, O&M) → enriched data imported back, matched by **Room Number**, with a diff preview before anything is written |
| 2.3 | Room data sheets and the FF&E schedule generated from the reconciled data |
| Monthly | Currency check — model vs Fohlio — reported as a KPI line |
| 3.2–3.3 | Installed FF&E reconciled; asset identifiers aligned for handover |

**Room numbers are the key.** A room renumbered without telling the Information Manager silently breaks the FF&E match for that room.

## 11.2 Specifications — CSI MasterFormat and SpecLink

CSI sections are assigned to model elements from 2.3. The SpecLink table of contents is reconciled against the model each gate, producing three lists: **specified but not modelled**, **modelled but not specified**, and **title mismatches**. Each is closed or formally accepted before the gate passes.

## 11.3 Building services operation — Niagara

The model states what equipment and points *should* exist; the Niagara station states what *is* running. Keeping the two aligned is the digital twin.

| Stage | What happens |
|---|---|
| 2.3 | Serviceable MEP elements carry their BMS data (point name, protocol, system). Point-naming convention agreed with the MEP team and the controls contractor |
| 3.1 (~M40) | Commissioning point list exported from the model for the controls contractor to load — model-driven, not hand-built |
| 3.3 | Reconcile: model equipment and points vs the live station. Differences (missing, renamed, extra) resolved with the controls contractor |

**Deliverable:** a reconciled record model plus an equipment/point register — the digital-twin baseline.

## 11.4 Handover data — COBie 2.4

COBie is produced from the model at close-out, covering Facility, Floor, Space, Type, Component, System, Spare, Job and Document. It is only as good as the asset data captured during Stages 3.1–3.3 — **which is why asset data is a construction-stage activity, not a close-out scramble.**

---

# PART 12 — Registers, templates and where things live

| Item | Owner | Issued |
|---|---|---|
| BIM Execution Plan | Info Mgr | Mobilisation, updated per stage |
| This playbook | Info Mgr | Mobilisation, updated as needed |
| MIDP | Info Mgr | Mobilisation, updated monthly and per drop |
| TIDP (per discipline) | Task Team Manager | Mobilisation, re-baselined per stage |
| Project Revit template | Info Mgr | Mobilisation |
| Family library | Info Mgr | Mobilisation, added to on request |
| A1 / A3 title blocks | Info Mgr | Mobilisation |
| Drawing register | Info Mgr | Every drop |
| Transmittal | Info Mgr | Every issue |
| Clash / coordination report | Info Mgr | Every cycle |
| RFI / technical query log | Lead AP | Continuous |
| Room data sheets | Arch/Interiors + Info Mgr | 2.3 onward |
| FF&E schedule | Interiors + Info Mgr | 2.2 onward |
| Monthly status report | Info Mgr | Monthly |
| COBie + O&M | Info Mgr + Contractor | 3.3 |

---

# PART 13 — Joining the project (day one)

Do these six things before you model anything:

1. Get your CDE access and confirm you can see WIP, Shared and Published.
2. Read §3 (numbering), §4 (CDE) and §5 (modelling standards) — 30 minutes.
3. Download the project template, family library and title blocks. **Start from the template.** Do not migrate an old project file.
4. Confirm your originator code and your volume/workset convention with your Task Team Manager.
5. Produce a test model — one element, correctly named, tagged, in the right workset and volume — and share it once.
6. Attend the next coordination meeting as an observer.

---

# PART 14 — Change, risk and escalation

## 14.1 Changing a standard

Any change to numbering, the template, the family library or the LOD matrix goes through the Information Manager, is recorded in the BEP, and is issued to everyone. **No local variants.** If something in this playbook does not work for your discipline, raise it — do not work around it silently.

## 14.2 Escalation

| Situation | Raise to | Within |
|---|---|---|
| A standard is unclear or unworkable | Information Manager | Immediately |
| You will miss a share | Task Team Manager → Information Manager | Before the deadline, not after |
| A clash cannot be resolved within your discipline | Coordination meeting | Same cycle |
| A design decision is blocking information | Lead Appointed Party | Same week |
| A gate is at risk | Information Manager → Lead AP → Owner | Two weeks before the gate |

## 14.3 The risks we are actively managing

| Risk | Mitigation |
|---|---|
| Owner's standards arrive after mobilisation and change naming | Standards are held as configuration, not hand-work — adopting them is an edit, not a rework |
| Originator-code length unresolved (§3.1) | Decide in Week 1, before any numbering |
| Volume attribution weak (no rooms/worksets) | Rooms placed before the first share; confidence reported at every gate |
| FF&E drift between model and Fohlio | Monthly currency check reported as a KPI |
| Asset data left to close-out | Asset data is a 3.1 activity with monthly reporting |
| As-built capture lagging | Capture current to within one month, checked monthly |
| Late specialist models (steel, façade, MEP modules) | Named in the MIDP with dates from 2.3 |

---

# APPENDICES

## A1 — Pre-share checklist (pin this)

Before moving anything from WIP to Shared:

- [ ] Model opens from the issued template lineage; coordinates unchanged
- [ ] Units millimetres; levels and grids unmodified
- [ ] Correct workset / volume for every element
- [ ] Rooms placed and named
- [ ] MEP elements connected into real systems
- [ ] No rogue families; no CAD acting as model geometry
- [ ] Purged; file size reported
- [ ] Revit warnings reviewed; no critical warnings
- [ ] Container named exactly per §3
- [ ] Suitability code set honestly (§3.7)
- [ ] Revision incremented
- [ ] Asset data / tag completeness checked
- [ ] Share note written: what changed, what is not yet resolved

## A2 — Gate pack contents

- MIDP extract for the stage with actual dates
- Standards / naming audit report
- Data completeness report (with element counts)
- LOD verification report + CSV
- Clash report with issue status and burn-down
- Model-health report
- Review comment close-out report
- Drawing register + transmittal receipt
- *(2.3+)* Specification reconciliation report
- *(2.3+)* FF&E currency report

## A3 — Kickoff agenda (half day)

| Time | Item |
|---|---|
| 0:00 | Project, programme, gates, and who is who |
| 0:20 | ISO 19650 in fifteen minutes: CDE, states, suitability, revisions |
| 0:35 | **The numbering system** (§3) — worked examples, then a live exercise |
| 1:05 | The CDE: folders, permissions, how to share, how to publish |
| 1:25 | Break |
| 1:35 | The modelling standards (§5) and the pre-share checklist (§A1) |
| 2:00 | The fortnightly cycle and the meeting rhythm |
| 2:20 | Clash: priorities, grouping, how issues are assigned and closed |
| 2:40 | TIDPs: what we need back and by when |
| 3:00 | Discipline breakouts — one-to-one with each team |
| 4:00 | Close: the three rules, and where to get help |

## A4 — Golden rules (poster)

1. **Start from the template.** Never migrate an old file.
2. **Name it right, first time.** §3 is not a guideline.
3. **Rooms before you share.**
4. **Connect your systems.**
5. **Set suitability honestly.** `S1` is a promise.
6. **Issue through the CDE. Always.**
7. **Run the checklist before you share.**
8. **Read the clash report before the meeting.**
9. **Never move the origin.**
10. **Raise it early.** Nothing gets cheaper by being hidden.

---

*Issued by Planscape Consulting Engineers Ltd as Information Manager for the Kampala Uganda Temple project. Questions to the Information Manager. Where this playbook and the BEP disagree, the BEP prevails and this document is corrected.*
