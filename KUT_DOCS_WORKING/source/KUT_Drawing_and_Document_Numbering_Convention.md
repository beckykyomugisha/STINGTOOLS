**Kampala Uganda Temple**

```{=openxml}
<w:p><w:pPr><w:spacing w:before="160" w:after="220"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="44"/></w:rPr><w:t>Drawing and Document Numbering Convention</w:t></w:r></w:p>
```

| Field | Value |
|---|---|
| Document number | KUT-[ORG]-ZZ-XX-RP-Z-0004 |
| Revision | P01 |
| Status / suitability | S2 (shared — for information) |
| Author | Author |
| Standard | BS EN ISO 19650-2; US National CAD Standard (Uniform Drawing System) |
| Date | [FILL: yyyy-mm-dd] |

# 1. Purpose and principle

A numbering scheme has to do three things well, for the life of the project: be **predictable** (anyone can find a drawing from its number), **flexible** (a new drawing slots in without renumbering anything), and **sustainable** (the same logic works across every building and discipline, from now to handover).

The single idea that delivers all three: **the number does not try to carry everything.** In ISO 19650 the file name already states the project, the originator, the building, the level and the discipline in their own fields. So the Number field has only one job left — to say **what type of drawing it is, and which one in the sequence.** That is why numbers "jump": plans live in one band, sections in another, schedules in another. You are not counting drawings 1, 2, 3 in the order they happen to be drawn; you are filing each one in the drawer for its type.

This is the basis of the US National CAD Standard, it is what Tilenga did (their schedules sit in a 3xx band, their drawings in a 1xxxxx series separate from documents in 0xxxxx), and it is what this convention adopts for KUT.

# 2. The information container name (recap)

Every drawing, model, schedule and document is named:

`KUT - [ORG] - Volume - Level - Type - Role - Number`
example: `KUT-[ORG]-01-GF-DR-A-1001`

| Field | Meaning | KUT codes |
|---|---|---|
| Project | Fixed | KUT |
| [ORG] | Originator (authoring company) | assigned per company at mobilisation |
| Volume | Building | TE, MH, HS, GB, UB, GH, ZZ (project-wide), XX |
| Level | Floor / level | B1, GF, 01, 02, RF, ZZ (multi-level), XX |
| Type | Information type | DR drawing, M3 model, SH schedule, SP specification, RP report, DC document, BQ BOQ, TR transmittal |
| Role | Discipline | A, S, M, E, P, F, C, I, L, Z |
| Number | Type band + sequence (this document) | 4 digits, see Section 3 |

Because the building (Volume) and the discipline (Role) are already separate fields, the **same number repeats across buildings and disciplines** and the other fields keep it unique. `KUT-[ORG]-01-GF-DR-A-1001` and `KUT-[ORG]-02-GF-DR-A-1001` are both "architectural floor-plan sheet 1", one for the Temple and one for the Meetinghouse. The number never has to grow to keep them apart.

# 3. The Number field — type-banded (the recommendation)

The number is **four digits**: the **first digit is the drawing-type band**, the **last three are the sequence** (001–999) within that band.

| First digit | Drawing type | Starts at | Holds |
|---|---|---|---|
| 0 | General — cover, drawing list, location & key plans, legends, general notes | 0001 | up to 999 |
| 1 | Plans — floor plans, site plans, roof plans, reflected ceiling plans (all horizontal views) | 1001 | up to 999 |
| 2 | Elevations | 2001 | up to 999 |
| 3 | Sections | 3001 | up to 999 |
| 4 | Large-scale / enlarged views | 4001 | up to 999 |
| 5 | Details | 5001 | up to 999 |
| 6 | Schedules and diagrams | 6001 | up to 999 |
| 7 | User-defined — for KUT: schematics, risers, single-line and system diagrams | 7001 | up to 999 |
| 8 | User-defined — reserved (e.g. coordination, sketches, mark-ups) | 8001 | up to 999 |
| 9 | 3D — isometrics, axonometrics, perspectives, visualisations | 9001 | up to 999 |

This is exactly the "jump" you described: a floor plan is in the **1000s**, a section is in the **3000s**, a schedule is in the **6000s**, a riser diagram is in the **7000s**. To add a new section you take the next free number in the 3000s; the plans in the 1000s are never touched. Sorting a folder by number automatically groups every drawing by type.

Bands 7 and 8 are deliberately left "user-defined" by the standard so a project can carve out its own groups without breaking the scheme. KUT uses 7 for the MEP schematics, risers and single-line diagrams (heavily used and worth separating from the 6000 schedules); 8 stays in reserve.

# 4. Worked examples (KUT)

| Container name | Reads as |
|---|---|
| KUT-[ORG]-ZZ-XX-DR-A-0001 | Architectural drawing list / cover, project-wide |
| KUT-[ORG]-01-GF-DR-A-1001 | Temple, ground floor, architectural floor plan, sheet 1 |
| KUT-[ORG]-01-01-DR-A-1002 | Temple, level 1, architectural floor plan, sheet 2 |
| KUT-[ORG]-01-XX-DR-A-2001 | Temple, architectural elevation, sheet 1 |
| KUT-[ORG]-01-XX-DR-A-3001 | Temple, architectural section, sheet 1 |
| KUT-[ORG]-01-XX-DR-A-5001 | Temple, architectural detail, sheet 1 |
| KUT-[ORG]-02-GF-DR-S-1001 | Meetinghouse, ground floor, structural framing plan, sheet 1 |
| KUT-[ORG]-01-GF-DR-E-1001 | Temple, ground floor, electrical (power/lighting) plan, sheet 1 |
| KUT-[ORG]-01-XX-DR-E-6001 | Temple, electrical schedule (e.g. panel schedule), sheet 1 |
| KUT-[ORG]-01-XX-DR-E-7001 | Temple, electrical single-line / riser diagram, sheet 1 |
| KUT-[ORG]-ZZ-XX-DR-C-1001 | Site, civil layout plan, sheet 1 |

# 5. Non-drawing information (models, schedules, documents)

The type band applies to **drawings** (Type `DR`). Other information types use a **simple four-digit sequence** starting at 0001 within each type, because they are not organised by sheet type:

| Type | Numbering | Example |
|---|---|---|
| M3 model | 0001 per discipline per building | KUT-[ORG]-01-ZZ-M3-A-0001 (Temple architectural model) |
| SH schedule (standalone register/schedule) | 0001+ | KUT-[ORG]-ZZ-XX-SH-Z-0001 |
| SP specification | 0001+ (or the CSI section number) | KUT-[ORG]-ZZ-XX-SP-Z-0001 |
| RP report / DC document | 0001+ per type | KUT-[ORG]-ZZ-XX-RP-Z-0002 (this BEP series) |
| BQ bill of quantities | 0001+ | KUT-[ORG]-ZZ-XX-CP-Z-0001 |
| TR transmittal / notice | 0001+ | KUT-[ORG]-ZZ-XX-IE-Z-0001 |

# 6. Why this is flexible and sustainable

- **Room to grow.** 999 numbers in every band, in every discipline, in every building. The project will never run out, so numbers never have to be restructured later.
- **No renumbering, ever.** A new drawing takes the next free number in its band. Existing drawings keep their numbers for life, which protects every reference, transmittal and approval already made against them.
- **Self-sorting.** Sort any folder or register by number and the drawings fall into type groups automatically (all plans, then all elevations, then sections, and so on).
- **One logic, everywhere.** The same bands apply to architecture, structure and every MEP discipline, and to all six buildings. A newcomer learns it once.
- **Standards-aligned.** It is the US National CAD Standard sheet-type logic carried inside the ISO 19650 Number field, so it reads correctly to any consultant, contractor or reviewer who knows either standard.

# 7. Rules (the dos and don'ts)

- Keep the number **four digits, fixed length, with leading zeros** (ISO 19650: the number length is fixed for the project).
- **Do not** put the building or the discipline in the number — they are separate fields (Volume and Role). Putting them in twice is the most common mistake and it breaks sorting.
- **Never reuse a number.** A superseded or cancelled drawing keeps its number; the number is retired with it (see the Document Control Standard and the notice templates).
- Assign numbers **within the band in issue order**; small gaps are fine and expected (they leave room for related sheets).
- Confirm the **[ORG] originator codes** for every company at mobilisation; until then examples carry `[ORG]`.

# 8. References

- US National CAD Standard, Uniform Drawing System — Sheet Identification (sheet-type designators 0–9).
- BS EN ISO 19650-2 — information container identification; the Number field (fixed length, leading zeros, project-defined).
- Tilenga EPC project document and drawing numbering (worked regional precedent: documents in the 0xxxxx series, drawings type-banded).

*Controlled document. The authoritative copy is the version in ACC. Fields marked `[FILL]` / `[ORG]` are completed at mobilisation.*
