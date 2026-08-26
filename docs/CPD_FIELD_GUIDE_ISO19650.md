# The ISO 19650 Field Guide

**A desk reference for delivering compliant information**
Planscape Ltd · Course PLNS-CPD-01 · Version 1.0 · August 2026

---

## How to use this guide

This is not a summary of ISO 19650. It is the reference you keep beside your
keyboard for the decisions the standard forces you to make every week: what to call
a file, what code to put on it, which folder it belongs in, and who is allowed to
move it.

**One rule governs everything in here.** Where this guide and your project's BIM
Execution Plan disagree, **the BEP wins.** Codes, tables and conventions vary
legitimately between national annexes, clients and projects. The tables below are
the most widely used set — they are a sound default and a good starting point for
drafting, but they are not law. The BEP is.

---

## 1. The three parties

Every ISO 19650 appointment has the same three roles, whatever the contract calls
them.

| Party | Who this usually is | What they do |
|---|---|---|
| **Appointing party** | The client. The university, ministry, developer or owner. | Commissions the work. Issues the Exchange Information Requirements. Receives and accepts the information. |
| **Lead appointed party** | The lead consultant or main contractor. | Responds to the EIR with a BEP. Coordinates all the appointed parties. Compiles the MIDP. Answerable to the appointing party for the whole information deliverable. |
| **Appointed party** | Sub-consultants, specialist designers, subcontractors. | Produces information for its own task. Delivers to a TIDP. Answerable to the lead appointed party. |

**The trap.** "Lead appointed party" sounds like the person in charge, so people
assume it means the client. It does not — it means the lead *supplier*. The client
is the appointing party. This is the single most common misunderstanding of ISO
19650 vocabulary.

**Information manager** is a *function*, not a party. It may be performed by the
appointing party, the lead appointed party, or a third party appointed for the
purpose. Do not treat it as a fourth box on the diagram.

---

## 2. The information requirement chain

Requirements cascade from the broadest organisational need down to a single
exchange.

```
OIR  →  AIR  →  PIR  →  EIR
```

| Code | Name | Question it answers | Set by |
|---|---|---|---|
| **OIR** | Organizational Information Requirements | What information does this organisation need to run itself? | The organisation |
| **AIR** | Asset Information Requirements | What information do we need about the asset once it is operating? | The asset owner/operator |
| **PIR** | Project Information Requirements | What information do we need from this project, to make decisions at each stage? | The appointing party |
| **EIR** | Exchange Information Requirements | What exactly must be handed over, at this specific exchange, in what form? | The appointing party, to the lead appointed party |

**The response to the EIR is the BEP.** The EIR is the demand; the BEP is the offer.
If you have received an EIR and not written a BEP, you have not responded to your
client.

Note that the chain can repeat one level down: a lead appointed party may issue its
own EIR to each appointed party.

---

## 3. Level of Information Need

**Level of Information Need** defines how much information is enough — and, just as
importantly, sets a ceiling so you do not deliver more than the purpose requires.

It is determined by **purpose**. Ask: *what decision is this information going to
support?* Then supply what that decision needs — geometrical detail, alphanumerical
data, and documentation — and no more.

**Over-delivery is a failure, not generosity.** Modelling a door to manufacturing
detail at concept stage costs fee, slows the model, and gives the recipient false
confidence in information that is not yet reliable.

*You may encounter "LOD" (Level of Detail / Development) on projects using older or
American conventions. ISO 19650 uses Level of Information Need. If your BEP says
LOD, use LOD — and check what its numbers actually mean on that project, because
they vary.*

---

## 4. The information container name

Every information container — model, drawing, schedule, report — carries a
structured name built from seven fields.

```
Project - Originator - Volume/System - Level/Location - Type - Role - Number
```

**Worked example**

```
KIH - PLNS - ZZ - 02 - DR - M - 0017
 │      │     │    │    │    │    └── 17th container in this series
 │      │     │    │    │    └─────── Role: Mechanical
 │      │     │    │    └──────────── Type: Drawing
 │      │     │    └───────────────── Level 02
 │      │     └────────────────────── Whole project, no zone subdivision
 │      └──────────────────────────── Originator: Planscape
 └─────────────────────────────────── Project: Kigali Innovation Hub
```

| Field | What it is | Notes |
|---|---|---|
| **Project** | Short project code | 3–6 characters. Fixed for the life of the project. |
| **Originator** | The organisation that authored it | 3–4 characters. **Not** the individual, and not the discipline — the *firm*. |
| **Volume/System** | Spatial or system subdivision | `ZZ` = whole project / not subdivided. Use zone codes only where the project defines them. |
| **Level/Location** | Floor or location | `ZZ` = all levels. `XX` = not applicable. Otherwise `00`, `01`, `02`, or `GF`, `B1` per the BEP. |
| **Type** | What kind of container it is | See §5. |
| **Role** | Discipline of the originator | See §6. |
| **Number** | Sequential | Zero-padded, usually to four digits. `0017`, never `17`. |

**Suitability and revision are not fields in the name.** They are metadata
attributes that travel *with* the container in the CDE. Some organisations append
them to filenames as a convenience; that is a local convention, not part of the
required name. Do not list them if asked to name the seven fields.

---

## 5. Type codes

The most commonly used set. **Confirm against your project BEP.**

| Code | Meaning | | Code | Meaning |
|---|---|---|---|---|
| `M2` | 2D model | | `PR` | Programme |
| `M3` | 3D model | | `RD` | Room data sheet |
| `CM` | Combined / federated model | | `RI` | Request for information |
| `DR` | Drawing | | `RP` | Report |
| `SH` | Schedule | | `SP` | Specification |
| `SP` | Specification | | `SU` | Survey |
| `BQ` | Bill of quantities | | `CA` | Calculations |
| `CP` | Cost plan | | `CO` | Correspondence |
| `CR` | Clash report | | `MI` | Minutes / action list |
| `CS` | Cost estimate | | `MS` | Method statement |
| `CT` | Contract / legal | | `CN` | Concept / sketch |
| `CX` | Commissioning | | `CF` | Certificate |
| `CD` | Case document | | `FN` | File note |
| `HS` | Health and safety | | `IE` | Information exchange file |
| `IF` | Image file | | `AF` | Animation file |
| `SN` | Snagging list | | `VS` | Validation report |

**`DR` versus `M3` is the mistake people make.** A drawing is a `DR` even when it
was produced from a model. `M3` is the model file itself.

---

## 6. Role codes

| Code | Role | | Code | Role |
|---|---|---|---|---|
| `A` | Architect | | `M` | Mechanical engineer |
| `B` | Building surveyor | | `P` | Public health engineer |
| `C` | Civil engineer | | `Q` | Quantity surveyor |
| `D` | Drainage / highways engineer | | `S` | Structural engineer |
| `E` | Electrical engineer | | `T` | Town planner |
| `F` | Facilities manager | | `W` | Contractor |
| `G` | Geographical / land surveyor | | `X` | Subcontractor |
| `H` | Heating / ventilation engineer | | `Y` | Specialist designer |
| `I` | Interior designer | | `Z` | General — non-disciplinary |
| `K` | Client | | | |
| `L` | Landscape architect | | | |

Projects frequently add codes — `FP` for fire protection is a common local
addition. Additions are legitimate provided the BEP defines them.

---

## 7. Suitability codes — for SHARED information

**Suitability answers one question: what may the recipient DO with this?**

| Code | Meaning | What the recipient may do |
|---|---|---|
| **S0** | Initial status / work in progress | Nothing. Not for issue. Sits in WIP. |
| **S1** | Suitable for **coordination** | Position and design their own work against it. Expect it to change. |
| **S2** | Suitable for **information** | Be aware of it. **Not** an invitation to design against. |
| **S3** | Suitable for **review and comment** | Review it and return comments. |
| **S4** | Suitable for **stage approval** | Approve, or reject, at a stage gate. |

**S1 versus S2 is the distinction that matters commercially.** S1 carries an
*expectation of use* — you are inviting other teams to commit design decisions
against your information. S2 carries no such expectation. Issuing at the wrong code
is how coordination disputes start.

**Codes above S4 vary.** Different national annexes and different project protocols
define, number and word them differently. Some projects use them; many do not.
**Check the BEP.** If someone tells you their S-code table is the correct one and
yours is wrong, they are mistaken about how the standard works.

---

## 8. Authorization codes — for PUBLISHED information

**This is a different code family, and it is where most people go wrong.**

Once information is authorised and moves into the Published state, it no longer
carries a suitability code. It carries an **authorization code**:

| Code | Meaning |
|---|---|
| **A1, A2 … An** | **Accepted.** Authorised for the stated purpose. The number denotes the purpose or stage, per the BEP. |
| **B1, B2 … Bn** | **Accepted with comments.** Authorised, but the originator must address the comments in the next revision. |

**The rule to remember:**

> **S codes describe the suitability of SHARED information.**
> **A and B codes describe the authorization status of PUBLISHED information.**

They coexist. They are not two versions of the same table, and a container does not
carry both at once. A drawing shared for coordination is `S1`. The same drawing,
once approved and published, is `A1`.

If you have seen a table mapping S4, S5, S6 and S7 to the Published container, you
have seen a simplification. It is not wrong in every context — some project
protocols genuinely work that way — but it is not the general case, and it is not
what the A/B codes are for.

---

## 9. Revision codes

| Prefix | Stage | Example sequence |
|---|---|---|
| **P** | Preliminary / pre-construction | `P01`, `P02`, `P03` … |
| **C** | Contractual / construction issue | `C01`, `C02` … |

**Crossing from P into C restarts the numbering under the new letter.** A drawing at
`P03` issued for construction for the first time becomes **`C01`**, not `P04`. The
letter change carries the meaning; the number is just a count within that stage.

Minor revisions may be expressed as `P01.01`, `P01.02` where the project uses them.
Some projects add an as-built series. **Check the BEP.**

---

## 10. The Common Data Environment

### The four states

| State | Who may write | Who may read | What lives here |
|---|---|---|---|
| **WIP** | The author only | The author's own team | Daily work. Nobody else's business. |
| **Shared** | The author, by publishing into it | All project members | Information issued for coordination, information, or review |
| **Published** | Only the authorised approver, under the approval process | All members, and external parties | Contract-grade, authorised information |
| **Archived** | Nobody — system-controlled | Read-only, all | The historical record. Every superseded revision. |

### The transitions are the point

Information does not *sit* in a CDE, it *moves* through one — and every move is a
controlled, recorded event with a named person behind it.

```
WIP ──── share ────► SHARED ──── authorise ────► PUBLISHED ──── supersede ────► ARCHIVED
        (author)              (approver)                        (system)
```

**Nothing skips a state.** Information does not go from WIP straight to Published,
however urgent the request.

---

## 11. What actually makes a CDE

A folder is not a CDE. Test yours against these five:

| # | Test | If it fails |
|---|---|---|
| 1 | **State transitions exist and are enforced** — you cannot simply drag a file into "Published" | The status of every file is a matter of opinion |
| 2 | **Access control is tied to state** — nobody can overwrite approved information | Your contract-grade information is one careless drag from being lost |
| 3 | **There is an audit trail** — you can answer *who approved this, and when* | You cannot defend a submission that is challenged |
| 4 | **Suitability and revision metadata travel with the container** | Nobody downstream knows what they are allowed to do with it |
| 5 | **There is a single source of truth** — one authoritative version, unambiguously | Teams coordinate against different versions and the clashes never converge |

> **A shared folder is a place to put files. A CDE is a set of rules about who may
> change a file's status, and a record of every time it happened. If you cannot
> answer "who approved this, and when", you do not have a CDE — you have a folder
> with an ambitious name.**

Naming your files correctly is **necessary but not sufficient.** Most non-compliant
projects have good filenames.

---

## 12. The six-point self-audit

Run this against your current project. It takes fifteen minutes and it is not
comfortable.

| # | Question | ✅ / ❌ |
|---|---|---|
| **1** | Does the project have a **BEP** — and has anyone read it in the last three months? | |
| **2** | Can you produce a **MIDP** showing every deliverable, its owner and its date? | |
| **3** | Do your information containers carry **compliant names**, consistently, including the ones produced last week? | |
| **4** | Are there **four distinct CDE states** with **enforced** permissions — not four folders anyone can write to? | |
| **5** | Does every shared container carry a **suitability code**, and every published one an **authorization code**? | |
| **6** | Could you show an auditor **who authorised** the current published revision of any given drawing, **and when**? | |

**Scoring is honest, not generous.** A ❌ on any of 4, 5 or 6 means the project is
not running an ISO 19650 information management process, whatever the BEP claims.

**Question 6 is the one that matters.** It is the question you will be asked when a
submission is rejected or a dispute reaches a lawyer.

---

## 13. The BEP

### Two kinds

| | **Pre-appointment BEP** | **Post-appointment BEP** |
|---|---|---|
| **When** | Submitted with the tender | After the contract is awarded |
| **Purpose** | Show you *can* deliver — proposed approach, capability and capacity | Confirm how you *will* deliver, in detail |
| **Contains** | Proposed methods, team, capability assessment | Confirmed methods, MIDP, agreed codes and conventions |

### What it must settle

A BEP that does not answer these has not done its job:

- The **information delivery strategy** and how it responds to the EIR
- **Roles and responsibilities**, including who performs the information management function
- **Standards, methods and procedures** — including **the naming convention and the code tables this project uses**
- The **CDE**: platform, states, permissions, transition and approval process
- **Software, versions and exchange formats**
- The **coordinate system and units** — agreed once, at mobilisation, never changed
- **The MIDP**, or a commitment to produce one and when
- **Quality assurance and model validation** — how compliance will be checked, by whom, how often
- **Security requirements** per ISO 19650-5

**The BEP is the project's dictionary.** When this field guide and the BEP disagree,
the BEP is right — but only if it actually says something. A BEP with `[FILL: …]`
still in it settles nothing.

---

## 14. MIDP and TIDP

| | **TIDP** | **MIDP** |
|---|---|---|
| **Task Information Delivery Plan** | **Master Information Delivery Plan** |
| **Produced by** | Each task team, for its own work | The lead appointed party |
| **Covers** | One team's deliverables | The whole project |
| **How it is built** | Written by the team | **Aggregated from all the TIDPs** |

The relationship is the point: **TIDPs are the inputs; the MIDP is the consolidated
output.** A MIDP that was not built from TIDPs is a wish list — nobody on a task
team has committed to anything in it.

### The minimum viable MIDP row

Every row must carry at least:

1. **The deliverable** — what container is to be produced
2. **The responsible party** — who owes it
3. **The date** — when it is due

Add, as the project requires: suitability at delivery, CDE state, stage or
milestone, format, TIDP reference, and a RAG status. But **what / who / when** is
the irreducible core. A row missing any of the three cannot control delivery.

---

## 15. Security — ISO 19650-5

Often skipped, occasionally contractual, and increasingly asked about on
government, defence, healthcare and utilities work.

ISO 19650-5 requires a **security-minded approach**: assess whether the project,
asset or its information is sensitive, and if so apply proportionate controls to
who can access what, how information is transmitted, and what happens to it at the
end of the appointment.

**The practical minimum:** know whether your project has been assessed as sensitive.
If it has, the BEP must set out the security controls, and "we email drawings to
whoever asks" is not one of them.

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **AIM** | Asset Information Model — the information set for the asset in operation |
| **AIR** | Asset Information Requirements |
| **BEP** | BIM Execution Plan |
| **CDE** | Common Data Environment |
| **EIR** | Exchange Information Requirements |
| **Information container** | Any named, structured set of information — a model, drawing, schedule, document |
| **LOIN** | Level of Information Need |
| **MIDP** | Master Information Delivery Plan |
| **OIR** | Organizational Information Requirements |
| **PIM** | Project Information Model — the information set produced during delivery |
| **PIR** | Project Information Requirements |
| **Suitability** | What a recipient may do with shared information |
| **Task team** | A group delivering a defined package of information |
| **TIDP** | Task Information Delivery Plan |

---

## 17. The ten mistakes

1. Calling the client the "lead appointed party". They are the **appointing party**.
2. Listing suitability or revision as a **field in the container name**. They are metadata.
3. Naming a drawing `M3`. A drawing is `DR`, even when generated from a model.
4. Writing `17` instead of **`0017`**.
5. Inventing a zone code when the project is not subdivided. Use **`ZZ`**.
6. Issuing at **S2** what you meant to issue at **S1** — or the reverse.
7. Putting an **S code on published information**. Published information carries **A** or **B**.
8. Going `P03 → P04` when the drawing has gone to construction. It becomes **`C01`**.
9. Calling a shared folder a CDE because the filenames are tidy.
10. Writing a BEP once, filing it, and never opening it again.

---

## 18. Where to look next

- **Your project BEP.** First, always, for anything in this guide that is
  project-specific — codes, levels, zones, suitability schedules, approval process.
- **ISO 19650-1:2018** — concepts and principles
- **ISO 19650-2:2018** — the delivery phase of assets
- **ISO 19650-3** — the operational phase
- **ISO 19650-5** — security-minded approach
- **Your national annex**, where one exists, for the code tables that apply locally

---

*Planscape Ltd · Kampala, Uganda · Course PLNS-CPD-01*
*This guide is issued to course delegates and may be reproduced for use within the
delegate's own practice.*
