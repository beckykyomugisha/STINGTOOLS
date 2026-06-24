# BIM Execution Plan (BEP) — Kampala Uganda Temple (KUT)

> **Template type:** ISO 19650-2 BIM Execution Plan (combined pre- and post-appointment).
> **How to use:** Replace every `[FILL: …]` placeholder. Delete guidance shown in *italics/blockquotes* once filled. Where a section says **"STINGTOOLS generates this"**, attach or reference the generated artefact instead of hand-writing it.
> **Owner:** Lead Appointed Party (Symbion Consulting). **Drafted/maintained by:** BIM/Information Manager (Planscape — Mayanja Davis).
> **Status:** `[FILL: Draft / Issued]`  ·  **Revision:** `[FILL: P01]`  ·  **Date:** `[FILL: yyyy-mm-dd]`

---

## Document control

| Field | Value |
|---|---|
| Project | Kampala Uganda Temple (KUT) |
| Project number | `[FILL]` |
| Appointing Party (Client) | The Church |
| Lead Appointed Party | Symbion Consulting |
| Information Manager | Planscape Consulting Engineers Ltd — Mayanja Davis |
| BEP type | `[FILL: Pre-appointment / Post-appointment]` |
| Revision | `[FILL: P01]` |
| Author | `[FILL]` |
| Checked / Approved | `[FILL]` |
| Date | `[FILL]` |

### Revision history
| Rev | Date | Author | Summary of change |
|---|---|---|---|
| P01 | `[FILL]` | `[FILL]` | First issue |
| | | | |

---

## 1. Introduction & project information

### 1.1 Purpose of this BEP
*This BEP sets out how the project team will produce, manage, exchange and hand over information for KUT in accordance with ISO 19650 and the Appointing Party's Exchange Information Requirements (EIR).*

### 1.2 Project details
| Item | Detail |
|---|---|
| Project name | Kampala Uganda Temple |
| Location | `[FILL: address, Kampala, Uganda]` |
| Description | `[FILL: temple + ancillary buildings, GFA, storeys]` |
| Procurement route | `[FILL: traditional / design-bid-build / …]` |
| Contract form | `[FILL]` |
| Programme | 49 months (Phase 2 = 11; Phase 3 = 38) — see §9 / MIDP |
| Key dates | `[FILL: appointment, Deliverable B, C, D, completion]` |

### 1.3 Project BIM objectives & uses
*State why BIM is used and the measurable goals. Edit to suit.*
| # | BIM objective | BIM use | Success measure |
|---|---|---|---|
| 1 | Coordinated, clash-free design | 3D coordination / clash detection | 0 unresolved high-priority clashes at each data drop |
| 2 | Reliable quantities & cost | Quantity take-off (BOQ/NRM2) | BOQ from model at B/C |
| 3 | Efficient documentation | Drawing production | Drawings auto-produced to corporate standard |
| 4 | Smooth FF&E + handover | FF&E spec + COBie + O&M | COBie + O&M delivered at D |
| 5 | Operational readiness / twin | BMS integration (Niagara) | Model reconciled to live points at handover |
| 6 | `[FILL]` | `[FILL]` | `[FILL]` |

---

## 2. Roles, responsibilities & authorities

### 2.1 Information management roles (ISO 19650)
| Role | Organisation | Name | Contact |
|---|---|---|---|
| Appointing Party | The Church | `[FILL]` | `[FILL]` |
| Lead Appointed Party | Symbion Consulting | `[FILL]` | `[FILL]` |
| Information Manager | Planscape | Mayanja Davis | davis@planscape.build |
| BIM Coordinator(s) | `[FILL]` | `[FILL]` | `[FILL]` |
| Task Team Manager — Architecture | `[FILL]` | `[FILL]` | `[FILL]` |
| Task Team Manager — Structure | `[FILL]` | `[FILL]` | `[FILL]` |
| Task Team Manager — MEP | `[FILL]` | `[FILL]` | `[FILL]` |
| Task Team Manager — `[FILL]` | `[FILL]` | `[FILL]` | `[FILL]` |

### 2.2 Responsibility matrix (RACI) — summary
*R=Responsible, A=Accountable, C=Consulted, I=Informed. Expand per deliverable in the MIDP/TIDPs.*
| Activity | Info Mgr | Lead AP | Arch | Struct | MEP | QS | Contractor |
|---|---|---|---|---|---|---|---|
| Maintain CDE | A/R | A | I | I | I | I | C |
| Set standards & template | R | A | C | C | C | I | I |
| Produce discipline models | C | A | R | R | R | I | I |
| Run clash & coordination | R | A | C | C | C | I | C |
| Approve data drops | C | A/R | C | C | C | C | I |
| FF&E (Fohlio) | C | A | R | I | C | C | I |
| Handover (COBie/O&M) | R | A | C | C | C | I | C |

---

## 3. Information requirements (response to the EIR)

### 3.1 Organisational/Project/Asset information requirements
| Requirement source | Reference | How we satisfy it |
|---|---|---|
| EIR (client) | `[FILL: doc ref]` | This BEP + MIDP |
| Project Information Requirements (PIR) | `[FILL]` | `[FILL]` |
| Asset Information Requirements (AIR) | `[FILL]` | COBie + O&M + Niagara at handover |

### 3.2 Level of Information Need (LOIN) by stage
*Geometry (LOD) + alphanumeric data + documentation, per stage. See `lod_matrix.json`.*
| Stage | LOD (geometry) | Data (alphanumeric) | Documentation |
|---|---|---|---|
| 2.1 BOD | 200 | Basic identity/spatial | BOD report |
| 2.2 Dev Design (B) | 300 | Discipline params + classification | 50% drawings, schedules |
| 2.3 Tech Design (C) | 350 | Full specification data | 100% set, BOQ |
| 3.1/3.2 Construction/FF&E | 400 | Manufacturer/installation data | Shop/fab info, FF&E specs |
| 3.3 Close-out (D) | 500 | Verified as-built + O&M | COBie, O&M, as-builts |

---

## 4. Standards, methods & procedures

### 4.1 Standards adopted
| Topic | Standard |
|---|---|
| Information management | ISO 19650-1/-2/-3/-5 |
| Classification | Uniclass 2015 (Ss systems · Pr products · EF elements); CSI MasterFormat as a secondary cross-reference where the Owner's specification requires it |
| Naming | ISO 19650 field-based (see §4.2) |
| Quantities/cost | `[FILL: NRM2 / other]` |
| Handover | COBie 2.4 |
| Security | ISO 19650-5 (see §8) |

### 4.2 File / container naming convention
`[Project]-[Originator]-[Volume/System]-[Level]-[Type]-[Role]-[Number]`
Example: `KUT-PLNS-ZZ-XX-M3-A-0001`
| Field | Codes |
|---|---|
| Project | `KUT` |
| Originator | `[FILL: PLNS, SYM, …per firm]` |
| Volume/System | `[FILL: ZZ, Z1…]` |
| Level | `[FILL: GF, 01, ZZ]` |
| Type | `[FILL: M3, DR, SH, SP…]` |
| Role | A, S, M, E, P, FP, G, Z |
| Number | 0001… |

### 4.3 CDE states & suitability codes
`WIP → SHARED (S0–S4) → PUBLISHED (A1/B1) → ARCHIVED` · Revisions `P0x` (preliminary) / `C0x` (contractual).
| Code | Meaning |
|---|---|
| S0–S4 | Shared (WIP / coordination / information / review / stage approval) |
| A1…An | Published — authorised |
| B1…Bn | Published — with comments |

### 4.4 Modelling standards (non-negotiables)
- Shared coordinate system / project base point: `[FILL: define once at mobilisation — never change]`
- Units: **millimetres**. Levels/grids: `[FILL: naming]`.
- Controlled family library only — no rogue families, no CAD-as-model.
- Worksets strategy: `[FILL]`.
- Tag/data completeness checked (STINGTOOLS) before every Share.
- Zero unresolved high-priority clashes at each data drop.

---

## 5. Common Data Environment (CDE)

| Item | Detail |
|---|---|
| CDE platform | Autodesk Construction Cloud (ACC) |
| Folder structure | WIP / Shared / Published / Archived |
| Access control | `[FILL: per ISO 19650-5, role-based]` |
| Issue/approval workflow | ACC Issues + Reviews; STINGTOOLS BCF push |
| Transmittals | STINGTOOLS / ACC — formal record of every issue |
| Interoperability/viewer | Speckle (internal live data + web viewer) |
| Backup/retention | `[FILL]` |

---

## 6. Software, exchange formats & coordinate system

### 6.1 Software & versions
| Function | Software | Version |
|---|---|---|
| Authoring | Autodesk Revit | `[FILL: 2025/2026/2027]` |
| CDE / coordination | Autodesk Construction Cloud | current |
| Automation / QA | STINGTOOLS | current |
| Interoperability / viewer | Speckle | current |
| FF&E / O&M | Fohlio | current |
| BMS / operations | Tridium Niagara | `[FILL]` |

### 6.2 Exchange formats
| Purpose | Format |
|---|---|
| Native models | RVT |
| Open exchange | IFC `[FILL: 2x3 / 4]` |
| Coordination | NWC / IFC / ACC models |
| Drawings | PDF (+ DWG if required) |
| Quantities | XLSX (NRM2) |
| Handover | COBie 2.4 (XLSX), O&M (Fohlio export) |

### 6.3 Coordinate system
`[FILL: agreed survey datum, project base point, true north, shared coordinates origin]` — defined at mobilisation, locked thereafter.

---

## 7. Collaboration & coordination process

### 7.1 The coordination cycle
`Teams Share (WIP→Shared) → Clash (ACC + STINGTOOLS) → Coordination meeting → Issues assigned & tracked → Weekly model-health report`
| Cadence | Activity |
|---|---|
| Daily | Authoring, auto-tagging, WIP saves |
| Weekly | Share, clash, coordination meeting, issue tracking |
| Monthly | KPI/model-health report, data-quality audit, MIDP review |
| Per stage | Formal data drop + transmittal + client review |

### 7.2 Clash management
| Item | Detail |
|---|---|
| Tools | ACC Model Coordination + STINGTOOLS clash; BCF to ACC Issues |
| Clash matrix | `[FILL: which disciplines clash vs which]` |
| Priority/tolerance | `[FILL: e.g., hard clash 0 mm; clearance rules]` |
| Acceptance | 0 unresolved high-priority clashes at each data drop |
| Reporting | Clash/coordination report (STINGTOOLS export) |

### 7.3 Coordination meetings
| Meeting | Frequency | Chair | Attendees | Output |
|---|---|---|---|---|
| BIM coordination | Weekly | Info Mgr | Discipline leads | Issue list, actions |
| Design review | `[FILL]` | Lead AP | All | Decisions log |

---

## 8. Security-minded approach (ISO 19650-5)
*Temple project — treat as sensitive.*
| Item | Approach |
|---|---|
| Access control | Role-based CDE permissions; least privilege |
| Sensitive information | `[FILL: what is restricted, e.g., security/MEP/ritual spaces]` |
| Data handling | `[FILL: storage, transfer, NDAs]` |
| Breach procedure | `[FILL]` |

---

## 9. Information delivery planning (MIDP / TIDP)

- **TIDPs:** each task team submits a TIDP (deliverables + dates + LOD + responsible). Collected at mobilisation, updated each stage.
- **MIDP:** the Information Manager aggregates TIDPs into the Master Information Delivery Plan (see companion `KUT_MIDP_TEMPLATE.csv`). Baselined at mobilisation; reissued at each stage and data drop.
- **Data drops:** Deliverable B (M4), Deliverable C (M8), Deliverable D (M45).

| Milestone | Stage | LOD | Planned (rel. month) |
|---|---|---|---|
| BOD | 2.1 | 200 | M1 |
| Deliverable B (Dev Design) | 2.2 | 300 | M4 |
| Deliverable C (Tech Design) | 2.3 | 350 | M8 |
| Tender issue / award | 2.4 | — | M9–M11 |
| Construction (ongoing) | 3.1 | 400 | M12–M43 |
| FF&E | 3.2 | 400 | M40–M43 |
| Deliverable D (Close-out) | 3.3 | 500 | M45 |

---

## 10. Quality assurance & model validation
| Check | Tool | When |
|---|---|---|
| Naming compliance | STINGTOOLS / ACC | Before every Share |
| Tag/data completeness (≥95%) | STINGTOOLS audit | Before every Share |
| Clash-free (high-priority) | ACC + STINGTOOLS | Before every data drop |
| Coordinate/level integrity | Revit/STINGTOOLS | Weekly |
| Deliverables vs MIDP | MIDP review | Monthly |
| Model health / KPI | STINGTOOLS KPI dashboard | Monthly |

---

## 11. FF&E, handover & operations
| Item | Approach |
|---|---|
| FF&E | Fohlio — file/Add-in round-trip now (`Fohlio_Export/ImportFinishes`); live API later. See playbook Part 4. |
| Quantities/cost | STINGTOOLS BOQ (NRM2) |
| Handover data | COBie 2.4 (STINGTOOLS) + O&M (Fohlio) |
| Digital twin / BMS | Niagara bridge (`Niagara_ExportPoints` / `Niagara_Reconcile`) at commissioning/handover |
| As-built | Model updated to LOD 500 at Deliverable D |

---

## 12. Training & competence
| Audience | Training | When |
|---|---|---|
| All task teams | Half-day kickoff (CDE, naming, LOD, clash, STINGTOOLS) | Mobilisation |
| Discipline leads | TIDP + data requirements | Mobilisation + each stage |
| FM/Operator | COBie/O&M/Niagara requirements | Pre-handover |

*See playbook Part 9 for the full kickoff pack.*

---

## 13. Risks & mitigation
| Risk | Likelihood | Impact | Mitigation | Owner |
|---|---|---|---|---|
| Uncoordinated information | `[FILL]` | High | CDE governance + weekly clash + automated QA | Info Mgr |
| Late deliverables | `[FILL]` | High | MIDP tracking + monthly RAG | Info Mgr |
| Fohlio API delay | Medium | Low | Use file/Add-in route now (Option A) | Info Mgr |
| Inconsistent standards | `[FILL]` | Medium | STINGTOOLS enforces standards | Info Mgr |
| `[FILL]` | | | | |

---

## 14. Appendices
- A: EIR (client) — `[attach]`
- B: MIDP — `KUT_MIDP_TEMPLATE.csv`
- C: TIDPs (per discipline) — `[attach]`
- D: Responsibility matrix (full) — `[attach]`
- E: Clash matrix — `[attach]`
- F: Standards references / KUT overlay — `project-templates/KUT/_BIM_COORD/`

---
*BEP template — keep under revision control; reissue when standards, team, programme or scope change.*
