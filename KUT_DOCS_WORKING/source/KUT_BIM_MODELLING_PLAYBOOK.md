# KUT — BIM Modelling & Documentation Playbook

| Field | Value |
|---|---|
| **Project** | Kampala Uganda Temple (KUT) |
| **Document** | KUT BIM Modelling Playbook |
| **Revision** | P01 |
| **Author** | Mayanja Davis (BIM Manager) |
| **Status** | Internal working guide |
| **Date** | [FILL: yyyy-mm-dd] |

> **Purpose:** the decisions to take *before* the first wall is drawn, the order to model in, and the data discipline that makes the BOQ come out accurate and correctly named — across all six KUT buildings.
> **Scope:** the Kampala Uganda Temple. Six buildings, each modelled separately with its own setting-out point and federated on a single site: **Temple (BLD1, 2,449 m²)**, **Meetinghouse (BLD2, 1,312 m²)**, **Housing/Ancillary (BLD3, 2,554 m²)**, **Grounds Building (BLD4, 93 m²)**, **Utility Building (BLD5, 166 m²)**, **Guard House (BLD6, 23 m²)**. Total ≈ **6,597 m²** on ≈ **six acres** in Kampala's central business district. Developed from the Owner prototype (engineering references the **1–40F prototype**). Concept and schematic complete.
> **Status of this file:** internal working guide. It names the private automation this team uses to work faster — **STINGTOOLS** (the Revit plugin for tagging, QA/validation, BOQ and batch production) and **Speckle** (interoperability + web viewer). Those tools are *ours*; they are never named in contractual project documents. The contractual project stack is Revit, ACC, Navisworks Manage, Fohlio, RIB SpecLink, Microsoft Teams and Niagara. Tool gaps found during review are listed in Part 8.

---

## The tool stack — what does what, so nothing gets confused

| Tool | Role on KUT | Class |
|---|---|---|
| **Autodesk Revit** | Model authoring — every discipline model of every building | Contractual |
| **Autodesk Construction Cloud (ACC)** | The **CDE** — WIP → SHARED → PUBLISHED, transmittals, the single source of truth | Contractual |
| **Navisworks Manage** | Federation and clash detection across the six buildings + site | Contractual |
| **Fohlio** | FF&E and O&M data (finishes, furniture, equipment registers into handover) | Contractual |
| **RIB SpecLink** | **Written specifications** — a specification-authoring tool, **not** a 3D viewer | Contractual |
| **Niagara (Tridium)** | BMS / building operations and the digital-twin phase | Contractual |
| **STINGTOOLS** | Internal Revit plugin: ISO 19650 tagging, QA/validation, BOQ take-off, batch drawing production | **Internal working tooling** |
| **Speckle** | Internal interoperability + web viewer: quick model exchange and browser-based review/sharing of federated data | **Internal working tooling** |

> **Do not confuse Speckle with RIB SpecLink.** Speckle is a geometry/data interoperability layer with a web viewer. RIB SpecLink writes the specification text. Different tools, different jobs, similar-looking names.

---

## Part 0 — What the incoming information actually is

Read from what has been issued, not assumed. Where a fact is not yet in hand it is marked `[FILL]` — do not model past a `[FILL]` that blocks geometry.

| Fact | Value |
|---|---|
| Design stage | **Concept and schematic complete.** The team is moving into design development / detailed design. |
| Source of design | The **Owner prototype**. Engineering references the **1–40F prototype**. KUT is the prototype **adapted to this site** — so every element is "prototype until proven site-specific." |
| Incoming CAD | **Consultant AutoCAD sets** (per discipline) + the prototype. Request native `.dwg`, not PDF, for anything you will measure or set out from. |
| Site | Kampala CBD, ≈ **six acres**. Urban context. |
| Site survey | `[FILL: confirm — topographic survey in DWG/CSV, coordinate system, vertical datum, number of levelled points, level range/fall]` |
| Buildings | Six: BLD1, BLD2, BLD3, BLD4, BLD5, BLD6 (areas above). Each is its **own** building with its own footprint, level set and setting-out point. |

### The building programme

| Tag location · container volume | Building | Area | Note |
|---|---|---|---|
| **BLD1** · `01` | Temple | 2,449 m² | The lead building. Roof-led / architecturally driven; heaviest specification. `[FILL: confirm spire/steeple design]` — if a spire exists it is a roof-led element (see Part 5). |
| **BLD2** · `02` | Meetinghouse | 1,312 m² | Assembly building; from the meetinghouse prototype. |
| **BLD3** · `03` | Housing / Ancillary | 2,554 m² | Largest by area; residential/ancillary accommodation. |
| **BLD4** · `04` | Grounds Building | 93 m² | Small ancillary. |
| **BLD5** · `05` | Utility Building | 166 m² | Services / plant. Services-heavy for its size. |
| **BLD6** · `06` | Guard House | 23 m² | Smallest structure; perimeter/security. |
| — | External works | — | Roads, parking, paths, boundary/perimeter, landscaping, drainage, water and power reticulation across the six-acre site. |

> The old site codes TE, MH, HS, GB, UB and GH are withdrawn. Element tags use the location `BLD1`–`BLD6` (`EXT` for site-wide); container names use the volume `01`–`06` (`00` site-wide, `ZZ` all volumes). See `KUT_NAMING_MIGRATION_MAP.md` §1.

### Glossary — the abbreviations used throughout

| Term | Means |
|---|---|
| **mAOD** | **metres Above Ordnance Datum** — height above a national sea-level datum. It is the surveyor's absolute height, as against a *project* level like `+0.000 FFL` which is relative to your own datum. Uganda uses its own national datum, so strictly this is "metres above datum" — but mAOD is the term everyone understands. **Always state which datum on the drawing.** |
| **FFL** | Finished Floor Level — the top of the finish you walk on |
| **SSL** | Structural Slab Level — the top of the structural slab, under the screed and finish |
| **SOP** | Setting-Out Point — the one permanent point a building is set out from |
| **LOD** | Level of Detail / Development — how much is modelled and how reliably |
| **CDE** | Common Data Environment — on KUT, **ACC** (WIP → SHARED → PUBLISHED) |
| **SMM / NRM2 / POMI** | Rules for how a bill of quantities is measured and described |
| **Qto / Pset** | IFC quantity set / property set — how quantities and data travel to a cost tool |

### Codes on the incoming CAD — **map them into our standard, do not adopt them**

The consultant CAD and the prototype carry their own nomenclature — door/window codes, finish abbreviations, supplier references. **We enforce our naming standard; we do not bend it to the incoming drawing.**

A supplier or prototype code carries no meaning outside its origin, and a type library named after one consultant's habits cannot be reused across the six buildings or on the next Church project. The incoming codes are **input data**, not a standard.

**What to do instead: keep both, in different fields.**

| Field | Holds | Example |
|---|---|---|
| **Revit Type Name** | *our* standard, enforced | `DR-SGL-TIMBER-0900x2100-FD30` |
| `ASS_LEGACY_REF_TXT` (or Type Comments) | the consultant/prototype original code, preserved verbatim | `[FILL: incoming code]` |
| Door/window **Mark** | the instance number only | `BLD1-D-001` |

That way the bill, the schedules and the type library all speak our language, and you can still cross-reference any line back to the incoming drawing or the prototype — which is what you need at an RFI or a valuation.

**Build the mapping table once, on day one**, and issue it with the first drawing set so the consultants can confirm it. `[FILL: build the architect/consultant-code → our-type mapping table from the incoming consultant CAD and the prototype schedules.]` The point is the *shape* of the name, not placeholder numbers.

### What you must obtain before modelling

1. **Sections, elevations and the roof/spire design** for each building — no vertical information can be assumed from a plan. The Temple in particular is roof-led (Part 5).
2. **Construction specification** — wall build-ups, slab thicknesses, roof covering, foundation type. From the prototype specification and RIB SpecLink, reconciled to Uganda materials.
3. **The survey in native format** — `.dwg` / `.csv` point file. **Do not trace a PDF.** Tracing a scaled PDF puts a 100–300 mm error into every setting-out dimension and it follows you to the earthwork quantities.
4. **A per-building finished floor level**, set against the site survey — each of the six buildings sits on its own platform at its own FFL.
5. **The 1–40F prototype engineering references**, so the MEP and structural models start from the prototype rather than being re-invented.

---

## Part 1 — The decisions to take before opening Revit

These are cheap now and expensive later. Take them in this order and write them into the BEP.

### D1 — Project code

Fix the code **first**, before the first save. In STINGTOOLS the code is derived from **Revit Project Information → Project Number**, sanitised to ≤ 8 characters, and then **stamped into ExtensibleStorage** so the whole output tree stays put even if someone edits the number later. It is also the suffix on every folder name and the first field of every ISO 19650 file name.

**Decision:** the project code is **`KUT`** — 3 characters, unambiguous, no spaces, no punctuation, and it matches the container prefix already fixed in the KUT numbering convention (`KUT-[ORG]-…`).

**The exact mechanism (from the STING code):**
- `DetectProjectCode` takes `ProjectInformation.Number`, strips everything but letters/digits/`_`/`-`, uppercases, **truncates to 8 characters**. No Number → first 3 letters of Name → `"PRJ"`.
- The root is resolved on **`DocumentOpened`**, before you touch anything, and the resolved path is written to an **ExtensibleStorage stamp** on ProjectInformation.
- **The stamp is what protects you from a later rename.** If it exists and its folder still exists, editing the Project Number changes nothing. If the stamp is missing (unsaved document, read-only, or the folder was moved), a rename **mints a brand-new sibling tree and your exports fork into it**.

**Therefore the correct opening move is:** set Project Number and Name → **save the file** → **close and reopen it**, so `DocumentOpened` runs against a saved document and the stamp lands. Do this before any other STING command, on every one of the six building models. Thirty seconds; it removes an entire class of "where did my exports go" failure.

Once fixed, it appears in the project folder name, every folder display name, every file name (`KUT-[ORG]-01-ZZ-M3-A-0001`) and every ISO 19650 asset tag written by STING.

#### There are TWO project-code fields, and nothing reconciles them

STING reads the project code from **two different places**, for two different purposes, and **no code anywhere checks that they agree.**

| Field | Read by | Drives |
|---|---|---|
| `ProjectInformation.Number` | `ProjectFolderEngine.DetectProjectCode` | the **folder tree**, the ES root stamp, folder display names |
| `PRJ_ORG_PROJECT_CODE_TXT` | 9 files incl. `TitleBlockParamApplier`, `TemplateManifest`, `ServerPublisher`, `TagSchemeEngine` | **ISO 19650 document numbers**, transmittals, rendered file names |

**Set both, to the same value: `KUT`.**

Three measured facts before you rely on either:

1. **Nothing in STING writes `PRJ_ORG_PROJECT_CODE_TXT`.** All nine consumers read it; there is no writer. **You must type it by hand** — Manage → Project Information.
2. **If you leave it blank, one consumer silently falls back to the Number** and the others get nothing. That is how the two drift: documents numbered from one field, folders from the other.
3. **`DrawingDispatcher.ReadProjectCode` is broken** — it looks up `PRJ_ORG_PROJECT_CODE` without the `_TXT` suffix, so it always returns null. Do not rely on drawing-type routing by project code until that is fixed (gap G-21).

#### The Project Information checklist — do this first, in this order, in every model

| Field | Value | Why |
|---|---|---|
| **Project Number** | **`KUT`** | the source. 3 characters, inside the ≤ 8 limit |
| Organization Name | `[FILL: confirm — the org on the issued title block]` | whose drawing this is (see the KUT numbering convention: `[ORG]` codes are confirmed at mobilisation) |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | `[FILL: your originator code, confirmed at mobilisation]` | the ISO 19650 **originator** is whoever authored the container. Organization Name is *whose drawing this is*; originator is *who produced this file*. They may differ. |
| Project Name | `Kampala Uganda Temple` | |
| `PRJ_PROJECT_COD_TXT` | `KUT` | feeds `{project}` in sheet numbers |
| `PRJ_ORG_PROJECT_CODE_TXT` | `KUT` | feeds the template engine / ISO doc numbers |
| `PRJ_NR_TXT` | `KUT` | title-block label + schedules |

**Order:** set Project Number → save → close → reopen (so the ES root stamp lands) → **Load Shared Parameters** → set the remaining fields in Manage → Project Information.

#### If you see "PRJ" anywhere, stop

`PRJ` is the placeholder used when Project Information has **neither** a Number nor a Name. It is not a valid project code. If you see it: do **not** carry on and rename later — the root is stamped on first resolve, so a rename gives you a **second** tree, not a moved one. Set the Number, save, close, reopen, and check that the `KUT` folder exists and no `PRJ` folder does. Several models opened without a Number all resolve to the **same** `PRJ` folder and overwrite each other's coordination data.

#### The title block's sample values are asset tags, not sheet numbers

If the current title block's Sheet Number sample value or **DWG NO.** field carries an **8-segment ASSET TAG** pattern (`DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`), that is the tag grammar for *a duct or a door*, not the ISO 19650 grammar for a *drawing*, which is `{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq}`.

**Recommended sample value:** `KUT-[ORG]-01-GF-DR-A-1001`

Do not edit the `.rfa` to fix a sample value — it is a family default, an operator step, not a code change. Read the label's parameter binding in the Family Editor; a `.rfa` label cannot be read statically.

### D2 — Project folder, and when to create it

**Create the folder the moment you have a code and a first `.rvt` — before modelling, not after.** STING resolves *every* output path (exports, coordination stores, BOQ, transmittals, revision registers) from the project root. If the root does not exist when the first command runs, data lands in ad-hoc places and you spend a day consolidating.

**Recommendation:** `CdeFirst` layout — KUT is greenfield, and the CDE states (WIP → SHARED → PUBLISHED) are what the team, the Owner and the QS actually consume. This maps directly onto ACC.

**But the Project Setup wizard never asks you.** Mode selection is automatic. The only place you get to choose is the **`CreateFolders`** command (dock tab **BIM**, "Folder + setup ops"), which opens `ProjectFolderSetupDialog` with radio buttons. So run **`CreateFolders`** deliberately and confirm the mode you got before anything else writes. Confirm `FOLDER_CODE_SUFFIX` at the same time — it is baked into folder display names at first setup: *set it before a project's first setup, not mid-project.*

**Nothing auto-fills the `PRJ_ORG_*` parameters.** They are bound by `LoadSharedParams` and read everywhere (title blocks, drawing tokens, export naming), but almost nothing writes them. **Type `PRJ_ORG_PROJECT_CODE_TXT`, `PRJ_ORG_ORIGINATOR_CODE_TXT`, `PRJ_ORG_CLIENT_NAME_TXT`, `PRJ_ORG_COMPANY_NAME_TXT`, `PRJ_ORG_PHASE_TXT` into Project Information by hand as a mobilisation task.** Left blank, title blocks silently fall back to defaults and the raw Project Number.

**Rules of use, once created (aligned to ACC):**
- `00_WIP` — your working models and unissued views. Nobody else reads this.
- `01_SHARED` — models issued *to the team* for coordination (suitability S1–S3).
- `02_PUBLISHED` — what goes to the Owner / QS / contractor (S4, A-codes).
- `_data/` — machine state only. **No deliverables ever go in here.**
- Never hand-build a path into these folders. If you find yourself typing `_BIM_COORD` into a dialog, stop — that is the symptom of a call site that should have gone through the path resolver.

### D3 — Coordinates and levels

Three separate things — do not conflate them:

| Thing | Set it to |
|---|---|
| **Survey Point** | The real surveyed coordinate + true elevation (mAOD). The link to the surveyor's world. `[FILL: site datum/level]` |
| **Project Base Point** | A clean round number near the Temple — a whole metre near the main building — so plan dimensions and level annotations read cleanly. `[FILL]` |
| **Internal origin** | Leave alone. Keep every model within ~10 km of it (trivially satisfied). |

Then **Acquire Coordinates** from the site model into every one of the six building models. Do this **once, early.** Re-coordinating populated linked models is a day of rework and a source of silent misplacement.

Annotate levels **twice** on drawings: project level (`+0.000 FFL`) *and* the mAOD in brackets. A level without a datum is a hazard on a multi-building site.

### D4 — Federation: links, not groups

Six distinct buildings on one shared site, each with its own setting-out point, level set and grid.

**Recommendation: one model per building, linked into a site model. One level of linking. No nesting (see Part 1A).**

| Model | Contents |
|---|---|
| `KUT-[ORG]-00-ZZ-M3-A` (site) | Toposolid, boundary/perimeter, roads, parking, paths, retaining, external drainage, reticulation. Hosts all links. |
| `KUT-[ORG]-01-ZZ-M3-A` | Temple |
| `KUT-[ORG]-02-ZZ-M3-A` | Meetinghouse |
| `KUT-[ORG]-03-ZZ-M3-A` | Housing / Ancillary |
| `KUT-[ORG]-04-ZZ-M3-A` | Grounds Building |
| `KUT-[ORG]-05-ZZ-M3-A` | Utility Building |
| `KUT-[ORG]-06-ZZ-M3-A` | Guard House |
| `KUT-[ORG]-ZZ-ZZ-M3-A` (federated) | Federation / coordination model — links only, no native geometry |

Each building carries its own discipline models (roles A, S, M, E, P, C, and Y for fire protection and low voltage) as separate `.rvt` where the discipline split warrants it; the table above shows the architectural spine.

**Why links and not groups, specifically here:**
- Groups misbehave when instances sit on different levels — and each building sits on its own platform at its own FFL.
- Grid names, room numbers, door marks and window marks repeat between buildings. In one model those are duplicate-name collisions; **links keep the mark and grid namespace per building.**
- Revit **2022 and later can schedule elements in linked models**, so the historic reason to avoid links for quantities no longer applies. Your BOQ can read all six buildings out of the federated model.
- Performance: six buildings plus a site toposolid in one file is slow. Links let you unload what you are not working on.

**The one thing links cost you:** you must maintain a **setting-out schedule** by hand (see D5), because each link's position, rotation and base elevation is instance data.

**A note on repetition.** Unlike a site with repeated identical units, KUT's six buildings are each **unique** — there is no building linked more than once — so the ×N link-multiplier trap in Part 3B is not a live risk here. Keep the rule in mind only if a repeated pod (e.g. a repeated housing unit, a repeated ordinance-room fit-out) is ever authored once and placed many times.

### D5 — Setting out the six buildings

Maintain one table in the site model as a Revit schedule (or key schedule), reproduced on the setting-out drawing:

| Building | Easting | Northing | Rotation | FFL (mAOD) | Platform cut/fill |
|---|---|---|---|---|---|
| BLD1 | … | … | … | `[FILL]` | … |
| BLD2 | … | … | … | `[FILL]` | … |
| BLD3 | … | … | … | `[FILL]` | … |
| BLD4 / BLD5 / BLD6 | … | … | … | `[FILL]` | … |

Each building model is authored with **±0.000 = its own FFL**. The link carries the real elevation. That way each building model stays clean and only the table changes.

### D6 — Wall cores vs finishes

**Yes, separate them — but the *right* way, which is not "model a second wall".**

Measurement rules bill them separately. Blockwork is billed by m² of wall, by thickness and height band. Plaster/render is billed by m², **per face**, internal and external distinguished. If plaster is buried inside a compound wall type you cannot get per-face areas out, only a volume.

But modelling a separate 15 mm wall on each face of every wall doubles or triples your element count and creates thousands of junction failures.

**In order of preference:**

1. **Core wall = one Revit wall, with the *structural core* correctly identified in the compound structure.** Type name carries the core: `WL-BLK-200-PL2`. Set **Structural Material** on the core layer.
2. **Use Revit Parts to split the wall by layer for takeoff.** Parts give you a schedulable, individually-materialled object per layer, with real areas and volumes — without exploding the model. This is the correct answer for "m² of plaster per face" and it is grossly under-used. *(Caveat: STING's takeoff does not measure Parts unless you author a rule for `OST_Parts` — see Part 3A.)*
3. **Model a finish as a separate element only where it is atypical** — a feature stone facing, a tiled wet area, timber lining. Few of them, genuinely separate products with separate rates.
4. **Room-based finish data for everything schedule-driven** (Part 3A).

**Floors — separate, always.** Do not put slab + screed + finish in one floor type. Model structural slab, screed and finish as separate floors (Part 3A). Three floors, three correct bill lines instead of one wrong one.

**Roofs:** structure and covering separate, for the same reason.

**The rule to write in the BEP:**
> *An element is modelled separately when it is measured separately, or when it is bought separately. Otherwise it is a layer.*

### D7 — Measurement standard — a decision, and a gap

A Ugandan QS will expect the **Standard Method of Measurement of Building Works for Eastern Africa (2nd Edition, 2008)**, or the AAQS *Standard Method of Measuring Building Work for Africa*. Those are the regional standards.

STINGTOOLS currently ships **NRM2, CESMM4, POMI, ICMS 3 and MMHW**. **East African SMM is not among them.**

**Recommendation:**
- **Set the active standard to POMI** (RICS Principles of Measurement International) — closest in structure to the East African SMM, and it will not produce UK-specific section headings that confuse the local QS.
- **Agree the bill structure with the QS in writing before you export anything.** Get their preferred section order and description wording, and map it once.
- Log the missing SMM-EA rule set as a tool gap (Part 8).

### D8 — Level of detail, and what you will deliberately *not* model

Half of BOQ disputes are about what the model was supposed to contain. Write it down.

| Model it | Do **not** model — bill by provisional sum / manual measured addition |
|---|---|
| Walls, floors, roofs, slabs, columns, beams | Excavation to formation, working space, disposal |
| Doors, windows, and their ironmongery *schedules* | Temporary works, scaffolding, propping |
| Sanitaryware, mechanical/kitchen equipment (as generic types) | Prelims, site establishment, insurances |
| Room finishes | Painting *specification* detail (billed off the finishes schedule) |
| Roof structure and covering; spire/steeple structure | Small builder's work, chases, holes < threshold |
| Retaining walls, steps, paths, hardstanding, parking | Landscaping and planting (provisional) |
| Below-ground drainage runs and chambers | Connections to authority mains (provisional) |

Anything in the right-hand column enters the bill as a **measured addition** or a provisional sum — the BOQ engine supports rows not backed by model elements; use it rather than fudging geometry.

### D9 — Classification

Two layers, both cheap at family/type creation, expensive retro-fitted:

1. **STING ISO 19650 tag** — the 8-segment `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` asset tag. Your identity spine; it drives the auto-tagger, validation and handover data. On KUT: `LOC` = the building (`BLD1`, `BLD2`, `BLD3`, `BLD4`, `BLD5`, `BLD6`), `ZONE` = functional wing/area within the building.
2. **A commercial classification** — **Uniclass 2015 `Ss` (systems) and `Pr` (products)**. `Ss` maps almost one-to-one onto how a QS groups measured work. Assign it at the **Type**, never the instance. STING can also write CSI MasterFormat if the Owner asks.

Do **not** rely on Revit's Assembly Code / Uniformat unless the QS asks — it is a US table and will not match a Ugandan bill.

### D10 — Naming, once, for everything

**Files (ISO 19650):** `KUT-[ORG]-01-ZZ-M3-A-0001`
Project – Originator – Volume – Level – Type – Role – Number. (This is the container name already fixed in the KUT numbering convention.)

**Revit types** — the type name is the bill description. Build it so it reads as one:
```
<Element>-<Material/Spec>-<Size>-<Variant>
WL-BLK-200-PL2          200mm blockwork wall, plastered both faces
WL-BLK-150-PL1-EXT      150mm blockwork, plastered one face, external
FL-RC-150               150mm reinforced concrete slab
FL-SCR-50               50mm cement screed
FL-FIN-TILE-10          10mm ceramic tile finish
DR-SGL-TIMBER-0900x2100 Single leaf timber door 900x2100
WN-CSMT-ALU-1500x1200   Aluminium casement window
RF-XXX                  [FILL: roof covering — pending specification]
```
**Rooms:** `BLD1-01 Foyer`, `BLD2-05 Hall`, `BLD3-12 Unit 3 Living`. Prefix with the building. Never rely on Revit's auto-number.

**Views:** let the drawing-type engine name them from the template so the sheet number and view name cannot drift apart.

### The one-page naming standard for KUT

`KUT` is the project code; `BLD1, BLD2, BLD3, BLD4, BLD5, BLD6` are the LOC codes and `EXT` is site-wide; `XX` = none. (In a container NAME the same buildings are the volumes `01`–`06`, `00` site-wide and `ZZ` all volumes — a different field.) Use the same LOC code in all seven places below — that single consistency is what makes the automation work.

| Thing | Pattern | Example |
|---|---|---|
| **Model file** (ISO 19650) | `PROJ-ORIG-VOL-LVL-TYPE-ROLE-NUM` | `KUT-[ORG]-01-ZZ-M3-A-0001.rvt` |
| **Drawing file** | same, `TYPE=DR` | `KUT-[ORG]-01-GF-DR-A-1001.pdf` |
| **Sheet number (the Number field)** | 4 digits: type band + sequence — never typed by hand | `1001` (plan), `3001` (section), `6001` (schedule) |
| **Scope box** | `STING::<drawing-type-id>::<level>::<tag>` — **hard regex, no spaces** | `STING::arch-plan-A1-1to100::GF::BLD1` |
| **Level** | plain, parseable prose — **not** prefixed | `Ground`, `Level 01`, `Roof`, `Basement 1` |
| **Grid** | per building model | `A`–`…`, `1`–`…` |
| **Room** | `<LOC>-<nn> <Name>` | `BLD1-01 Foyer` |
| **Wall type** | `WL-<core>-<thk>-<finish>` | `WL-BLK-200-PL2` |
| **Floor type** | `FL-<material>-<thk>` | `FL-RC-150`, `FL-SCR-50`, `FL-FIN-TILE-10` |
| **Door / window type** | `DR-<code>-<w>x<h>` / `WN-<code>-<w>x<h>` | `DR-SGL-TIMBER-0900x2100` |
| **Material** | ALL-CAPS, `<TYPE> <QUALIFIER> <SIZE>` — chosen so the right carbon/waste keyword fires first (Part 4A) | `STANDARD CEMENT SCREED 50MM` |
| **View** | generated by the drawing type | `STING - arch-plan-A1-1to100 - BLD1` |
| **Workset** | discipline + element group, no building prefix in a one-building model (Part 1C.4; prefix is decision OD-2) | `A-ARCH-Walls`, `S-STRC-Framing`, `M-MECH-Ducts` |
| **Asset tag** | the 8-segment STING tag, auto-built | `M-BLD1-Z01-GF-HVAC-SUP-AHU-0001` |

### The number field (recap of the KUT convention)

The Number is **four digits**: the **first digit is the drawing-type band**, the last three are the sequence within that band.

| First digit | Type | Starts | | First digit | Type | Starts |
|---|---|---|---|---|---|---|
| 0 | General (cover, lists, legends) | 0001 | | 5 | Details | 5001 |
| 1 | Plans (floor/site/roof/RCP) | 1001 | | 6 | Schedules & diagrams | 6001 |
| 2 | Elevations | 2001 | | 7 | Schematics / risers / single-line | 7001 |
| 3 | Sections | 3001 | | 8 | Reserved | 8001 |
| 4 | Large-scale / enlarged | 4001 | | 9 | 3D / visualisations | 9001 |

Because Volume and Role are separate fields, `KUT-[ORG]-01-GF-DR-A-1001` and `KUT-[ORG]-02-GF-DR-A-1001` are both "architectural floor-plan sheet 1", one Temple, one Meetinghouse — the number never has to grow to keep them apart. Non-drawing types (M3, SH, SP, RP, BQ, TR) use a plain 4-digit sequence from 0001.

### Sheet numbers as the full ISO code — exactly how

You want `KUT-[ORG]-01-GF-DR-A-1001`. Every field is reachable; three things need setting and one has a trap.

**1. Project Information shared parameters** (once per model, by hand — nothing auto-fills these):

| Parameter | Value | Feeds |
|---|---|---|
| `PRJ_PROJECT_COD_TXT` | `KUT` | `{project}` |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | `[FILL: originator]` | `{originator}` |

> **You must re-run Load Shared Parameters first — including on a model that is already set up.** Until the binding rows run, `{project}` and `{originator}` read as null and the segment vanishes from the sheet number *with no warning*. Order on an existing project: deploy current build → **STING → Setup → Load Shared Parameters** (idempotent) → **Manage → Project Information**, type the values → re-run sheet numbering.

Every sheet STING produces carries the seven segments individually (`PRJ_SHEET_PROJECT_TXT`, `…_ORIG_TXT`, `…_VOLUME_TXT`, `…_LEVEL_TXT`, `…_TYPE_TXT`, `…_ROLE_TXT`, `…_SEQ_TXT`) plus their join `PRJ_SHEET_FULL_REF_TXT`. **Selective display is a title-block label edit, yours to make** — a busy 1:100 plan can show `A-GF-1001`, a transmittal stamp can show the full ref.

> **If a label shows `{project}` or `{lvl}` in braces, that segment never resolved.** It is not a rendering fault — the sheet number was rejected too. Read `StingTools.log`, which names the token and the parameter to set. Braces are deliberate: a blank would read as "no volume code" instead of "never filled in".

**2. The drawing-type profile** — in `_BIM_COORD/drawing_types.json` set `sheetNumberPattern` and `isoNaming.volume/type/role` per building.

**3. Drop `-{suit}-{rev}` from the shipped pattern** if you do not want `…-S2-P01` appended — remove them from the *pattern*, not just from `isoNaming`, or they render as trailing `--`.

> **The trap — `{lvl}` has no profile fallback.** `vol`, `type` and `role` fall back to `IsoNaming`; `{lvl}` does not, because `IsoNaming` has no Level field. The producing command must pass the level code. If it passes nothing you get `KUT-[ORG]-01--DR-A-1001` with an empty segment and no warning (gap K-7).

**On level codes:** the KUT numbering convention standardises on **`GF`, `01`, `02`, `RF`, `B1`** (not the ISO Annex numeric `00`). **Use `GF` throughout — in both the file/sheet name and the STING tag** — because the house standard already fixes this. That neutralises most of the five-vocabularies level gap (Part 1B) for KUT; you still must name levels as plain prose the parser can read and keep the building code out of the level name.

Because each building needs its own volume code, you need **one drawing-type variant per building** (`arch-plan-01`, `arch-plan-02`, …) differing only in `isoNaming.volume`. Tedious but honest; the alternative is a code change to source `{vol}` from the element's LOC (gap F-7).

### Door and window marks — controlled tokens, thin marks

Keep the Mark thin; put the detail in the schedule.

**Nothing writes Mark from STING tokens.** The traffic is one-way the other direction: `NativeParamMapper` copies **Mark → `ASS_ID_TXT`** during tagging.

| Carries | Where | Example |
|---|---|---|
| Instance identity only | **Mark** | `BLD1-D-001` |
| Function / system / product codes | STING tokens | `ARC`, `FIT`, `DR` |
| Type, size, fire rating, ironmongery, finish | **type parameters, shown in the schedule** | `DR-SGL-TIMBER-0900x2100` |
| The consultant's original code | `ASS_LEGACY_REF_TXT` | `[FILL]` |

**The codes themselves, measured against the shipped maps — do not guess these** (read from `TagConfig.Defaults.cs`):

| Token | Door | Window | Source |
|---|---|---|---|
| **SYS** (`ASS_SYSTEM_TYPE_TXT`) | `ARC` | `ARC` | `SysMap` — both architectural fit-out |
| **FUNC** (`ASS_FUNC_TXT`) | `FIT` | `FIT` | `FuncMap`, keyed by SYS → `ARC→FIT` |
| **PROD** (`ASS_PRODCT_COD_TXT`) | `DR` | **`WIN`** | `ProdMap` — note `WIN`, three letters, *not* `WN` |

Two traps this closes:
- **`EXT` and `INT` are not FUNC values.** `EXT` is a **LOC** code meaning external works. Writing `EXT` into `ASS_FUNC_TXT` puts a location code in a function field, and nothing rejects it.
- **FUNC cannot distinguish a door from a window** — both are `FIT`. Only **PROD** separates them (`DR` / `WIN`).

Worked example, a door in the Temple, ground floor, zone 1:

```
A-BLD1-Z01-GF-ARC-FIT-DR-0001
│ │  │   │  │   │   │  └ SEQ
│ │  │   │  │   │   └── PROD  DR   (WIN for a window)
│ │  │   │  │   └────── FUNC  FIT  (derived from SYS)
│ │  │   │  └────────── SYS   ARC
│ │  │   └───────────── LVL   GF
│ │  └───────────────── ZONE  Z01
│ └──────────────────── LOC   BLD1
└────────────────────── DISC  A
```

And a mechanical worked example (matches the KUT tag grammar): a supply AHU in the Temple, ground floor, zone 1 → `M-BLD1-Z01-GF-HVAC-SUP-AHU-0001`. The `HVAC / SUP / AHU` codes come from the SYS/FUNC/PROD maps in the plugin source — verify them against `TagConfig.Defaults.cs` rather than documentation before relying on them.

**On making tokens read-only — you cannot.** Revit does not allow a project shared parameter to be read-only in the palette. `ASS_TOKEN_LOCK_TXT` exists but is **snapshot-and-restore, not prevention**: it records the value before auto-population, lets everything overwrite it, then writes it back. Comma-separated token list, e.g. `LVL,SYS,PROD`. Nine tokens lockable (`DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV`); `SEQ` is not. It only snapshots **non-empty** values, and there is **no UI** — type it by hand. `BatchTagCommand`'s own lock check is dead code (gap K-9); use `TagAndCombine`.

**Two things to keep straight:**
- The **level code** is derived from the level *name* by a parser. Name levels so the parser can read them, and never put the building code in the level name — `BLD1-L01-FFL` misparses.
- The **`{vol}`** field in ISO file names comes from the *drawing type's* JSON profile, not from the element's LOC (gap F-7).

---

## Part 1A — Can I model one building and nest links?

**No. Link each building once, directly, into the site model. Do not nest.**

A **nested link** (a building linked into an intermediate model, which is then linked into the site) costs you the two things this project depends on:

1. **Nested links do not schedule reliably.** "Include elements in links" reaches *one* level. A nested link drops out of link schedules — your quantities silently lose a level of depth.
2. **Visibility becomes conditional.** A nested link only appears in the grandparent if its reference type is **Attach**, not **Overlay**, and even then it is often visible only through a linked view.

### The structure to use

```
KUT-[ORG]-00-ZZ-M3-A (site, host)
├── KUT-[ORG]-01   × 1
├── KUT-[ORG]-02   × 1
├── KUT-[ORG]-03   × 1
├── KUT-[ORG]-04   × 1
├── KUT-[ORG]-05   × 1
├── KUT-[ORG]-06   × 1
└── survey DWG
```

One level of linking, six unique buildings, one instance each. Each carries its own position, rotation and base elevation through its Shared Site.

**If you ever must nest** (e.g. a supplier-authored sub-assembly), set the reference type to **Attach** and verify quantities by hand — do not trust the schedule.

---

## Part 1B — The levels are ISO-coded, in one place and not another

This is a real inconsistency in the tool, not a misunderstanding.

**ISO 19650 (UK Annex) level codes are two characters:** `ZZ` (many levels), `XX` (no level), `00` (ground), `01/02…`, `B1/B2`, `RF/MZ/PH`. Note **`00`, not `GF`**.

**STINGTOOLS carries both, in different files, and nothing reconciles them:**
- `Iso19650Vocabulary.cs` has the correct ISO list (`ZZ XX B2 B1 00 01 …`). This is what file/sheet names use.
- `ParameterHelpers.GetLevelCode` produces the STING **tag** vocabulary (`GF L01 …`). This is what goes in your asset tags.

So the same level would be `00` in a file name and `GF` in a tag — five level vocabularies in the tree, and a name the parser cannot read becomes a 12-character passthrough that then fails STING's own 4-character validator (gaps F-4, F-6).

**What to do on KUT:** the **KUT numbering convention already standardises on `GF`** for both file names and tags, so use `GF` everywhere and the gap barely bites. Name levels as plain prose the parser can read (`Ground`, `Level 01`, `Roof`), and keep the building code out of the level name. `[FILL: confirm the level set per building — BLD3 is multi-storey; BLD1/BLD2 per prototype.]`

---

## Part 1C — Worksharing: where the central models live, how a desktop connects, and how the team works at once

> **How to read this part.** Statements marked **(measured)** were read from the STINGTOOLS source on 2026-10-04 and have **not** been exercised in Revit on a real worksharing setup. Statements marked **(recommendation)** are advice for the fastest delivery, not a verified fact. Values nobody has confirmed yet are written `REPLACE_WITH_…` or `TODO(KUT):`. The owner decisions this part needs are listed in **1C.8** — nothing here is final until they are taken and written into the BEP.

### 1C.1 How many central models — one per discipline per building

The federation rule in D4 / Part 1A (one model per building, linked once into the site model, no nesting) applies to **every discipline**, not only architecture. **(recommendation)**

| Volume | Building | Discipline models (role code) | Workshared? |
|---|---|---|---|
| `00` | Site / external works | `A` (site, toposolid, external works), `C` (civil), plus `E` / `P` / `Y` reticulation **only if** those consultants model site services — `TODO(KUT): confirm from the TIDPs` | yes, if more than one person edits |
| `01` | Temple (BLD1) | `A`, `S`, `M`, `E`, `P`, `Y` | yes |
| `02` | Meetinghouse (BLD2) | `A`, `S`, `M`, `E`, `P`, `Y` | yes |
| `03` | Housing / Ancillary (BLD3) | `A`, `S`, `M`, `E`, `P`, `Y` | yes |
| `04` / `05` / `06` | Grounds / Utility / Guard House | same six roles | only if two people edit at once — a one-person model can stay non-workshared |
| `ZZ` | Federated coordination model (D4) | links only | **no** — one owner, the Information Manager (see 1C.6 for why this matters to STING) |

- **File name = container name.** `KUT-[ORG]-01-ZZ-M3-M-0001.rvt` for the Temple mechanical central, and so on. Revit appends `_<username>` to each local copy by itself; never rename a central.
- **Keep the matrix uniform even for the Guard House (23 m²).** A small file costs nothing; an exception costs a naming rule that every automation then has to know about.
- **Why per discipline rather than one multi-discipline model per building:** each consultant is a different organisation (Part 0, managing playbook §4). A container has one originator and one role; a shared multi-discipline central would need every firm on one server, which is the thing 1C.2 is about.

### 1C.2 Where the centrals live — three options

| | **A. Office file server (LAN)** | **B. Revit Server** | **C. ACC cloud worksharing (Revit Cloud Models)** |
|---|---|---|---|
| How it works | Central `.rvt` on an SMB share in the firm's office; locals on each PC; sync over the office LAN | A Revit Server host plus Accelerators in each office; for one firm with several offices on a WAN | Central lives in ACC Docs; locals are cached on each PC; every sync goes over the internet |
| Licence | Revit only | Revit only (server role from the Revit installer) | Every author who edits a cloud model needs the Autodesk subscription that includes cloud worksharing (BIM Collaborate Pro at the time of writing — **confirm with the reseller; this file does not claim which licences anyone holds**) |
| Internet dependence | None for modelling; only for the ACC exchange | WAN between offices | **Every sync.** A slow or dropped link mid-sync is lost time for the whole team |
| Several firms in one model | Only by VPN into one firm's server — avoid | Same | Yes — this is what it is for |
| STING project folder | Works, with the caveat in 1C.6 | Same caveat | **Unverified** — see 1C.6 |
| Verdict for KUT | **Use within each firm** | Only if one firm has two offices editing the same model | **Pilot before adopting** |

**Recommendation — the fastest arrangement that does not depend on a licence decision:**

1. **Each firm hosts its own centrals on its own office server (option A)** — LAN syncs take seconds, and nothing waits on the internet.
2. **Firms exchange through ACC, not through each other's servers.** Every task team publishes its models to the ACC **SHARED** folder on the agreed cycle (fortnightly by default, weekly in a design crunch); other firms download that issued copy into their own `Incoming` folder (1C.3) and link **that**. Everyone then coordinates against the same issued state, which is exactly what ISO 19650 asks for.
3. **Adopt cloud worksharing (option C) only after a two-week pilot** on one small model (`06` Guard House is the obvious candidate), and only if every editing author is licensed and each office's measured upload speed is acceptable. `TODO(KUT): measure upload/download at each office before deciding — do not assume.` If the pilot is good, C removes the VPN and the weekly re-download, and ACC Design Collaboration packages replace the manual exchange.
4. **Do not use Revit Server** unless one firm genuinely has two offices editing the same model — it adds a server to administer for no gain on a single-office team.

### 1C.3 The office server and how a desktop connects to it

**Server folder (one per firm, per project)** — names are a recommendation; the server and share are `REPLACE_WITH_SERVER` / `REPLACE_WITH_SHARE`:

```
\\REPLACE_WITH_SERVER\REPLACE_WITH_SHARE\KUT\
├── Central\         every central .rvt of this firm, ISO-named, nothing else
├── Coordination\    the federated model (IM's firm only) — STING's project root is created beside it (1C.6)
├── Incoming\        SHARED copies downloaded from ACC from the other firms — link these, never their centrals
└── Library\         the issued template, families, title blocks
```

**Connecting a desktop (recommendation, standard Revit practice):**

| Do | Why |
|---|---|
| Join the PC to the office domain; remote staff connect by **VPN** first, then open Revit | the central must be reachable by the same path from every seat |
| Open and link by the **UNC path** (`\\REPLACE_WITH_SERVER\…`), **never a mapped drive letter** | drive letters differ from PC to PC; a central or link saved as `Z:\…` breaks on the next machine |
| Revit ▸ **File** ▸ **Options** ▸ **File Locations** ▸ *Default path for user files* → a fixed **local SSD** folder, the same on every PC (`TODO(KUT): e.g. C:\KUT_Local\`) | local copies must be on the workstation, never on the server; a fixed path is also what makes the STING workaround in 1C.6 work |
| **Never** put a central in a folder synchronised by OneDrive, Dropbox, Google Drive or the Autodesk Desktop Connector | sync clients copy a half-written central and corrupt it |
| Server: SMB share, **nightly backup by IT**, the firm's normal antivirus policy agreed with IT | Revit's own backup folders are not a backup of the server |

**The daily worksharing routine — every modeller:**

| When | Revit location | What |
|---|---|---|
| Start of day | **File ▸ Open** → browse the UNC path → select the central → tick **Create New Local** → *Open* (use **Specify…** to open only the worksets you need) | a fresh local every morning avoids a stale local |
| Before placing anything | **Collaborate ▸ Manage Collaboration ▸ Active Workset** drop-down (or the status bar) | elements land on the active workset |
| While working | edit freely — Revit **borrows** elements; make a whole workset editable only for a task that needs it | borrowing is what lets two people work in one model |
| Every 1–2 hours, before any break, before closing | **Collaborate ▸ Synchronize ▸ Synchronize with Central** (with the dialog: tick *Relinquish* and add a comment) | small, frequent syncs are fast; big ones block everyone |
| Someone needs your element | **Collaborate ▸ Synchronize ▸ Editing Requests** | grant, or sync and relinquish |
| See who owns what | the **Worksharing Display** control on the view control bar (Owners / Model Updates / Worksets); the Worksharing Monitor where installed for your Revit version | |
| End of day | Synchronize with Central → **Relinquish All Mine** → close | nobody is locked out overnight |

**Weekly — the model's owner (Task Team Manager or BIM coordinator), outside working hours:** open the central with **Audit** ticked, then Synchronize with **Compact Central Model** ticked. **(recommendation)**

**Do not use STING's LAN buttons for this** — use Revit's own commands above (see 1C.6 for what is wrong with them).

### 1C.4 Worksets — what STING creates, and the scheme to use

**What STING creates (measured):**

| Button | Creates |
|---|---|
| STING Panel ▸ **SETUP** ▸ VIEW TEMPLATES ▸ **"Worksets"** (`CreateWorksets`, `Temp/TemplateCommands.cs`) | **36** worksets (the button tooltip, the workflow label and the readiness card all say 35): `A-ARCH-Walls / Floors / Ceilings / Roofs / Doors / Windows / Furniture / Stairs / Fitout`, `S-STRC-Columns / Framing / Foundation / Slabs`, `M-MECH-Equipment / Ducts / AirTerminals`, `P-PLMB-Equipment / HotWater / ColdWater / Drainage / Fixtures`, `E-ELEC-Equipment / Lighting / CableTrays / Conduits / LowVoltage`, `FP-FPRT-Sprinklers / Pipes / Devices`, `A-SITE-Topography / Parking`, `Z-Linked-Architectural / Structural / MEP`, `X-SHARED-Grids / Levels`. It refuses on a non-workshared model. **It creates all 36 in every model** — every discipline's worksets, whatever model you run it in. |
| STING Panel ▸ **INTEROP** ▸ LAN COLLABORATION ▸ **"Enable WS"** (`LANEnableWorksharing`) | enables worksharing **and** a *different* set of 10: `Architecture`, `Structure`, `MEP-Electrical`, `MEP-Mechanical`, `MEP-Plumbing`, `MEP-Fire Protection`, `Interior`, `Site`, `Shared Levels and Grids`, `Links`. **Do not use it on KUT** — two schemes in one project is how workset audits stop meaning anything. |

**The scheme to use (recommendation):** enable worksharing with Revit (**Collaborate ▸ Manage Collaboration ▸ Collaborate** → *Within your network*), run **SETUP ▸ VIEW TEMPLATES ▸ "Worksets"** once in the central, then in **Collaborate ▸ Worksets** delete the ones that do not belong to that discipline:

| Model | Keep | Add |
|---|---|---|
| Architecture `A` (01–06) | `A-ARCH-*`, `X-SHARED-*`, `Z-Linked-*` | — |
| Site `A` (00) | `A-SITE-*`, `X-SHARED-*`, `Z-Linked-*` | `A-SITE-Paths`, `A-SITE-Boundary` as needed |
| Structure `S` | `S-STRC-*`, `X-SHARED-*`, `Z-Linked-*` | — |
| Mechanical `M` | `M-MECH-*`, `X-SHARED-*`, `Z-Linked-*` | `M-MECH-Pipework` (chilled/heating water, refrigerant) — the shipped set has none |
| Electrical `E` | `E-ELEC-*` (LowVoltage covers ICT/security), `X-SHARED-*`, `Z-Linked-*` | — |
| Plumbing / public health `P` | `P-PLMB-*`, `X-SHARED-*`, `Z-Linked-*` | — |
| Fire `Y` | `FP-FPRT-*`, `X-SHARED-*`, `Z-Linked-*` | fire-alarm devices go in `FP-FPRT-Devices` |

- **One link per `Z-Linked-*` workset**, so anyone can unload a link by closing its workset on open.
- **Never model on `Workset1`.** Rename it to the discipline's main workset when you enable worksharing.
- **The building is carried by the file, not by the workset name.** In a one-building model set **Manage ▸ Project Information ▸ Building Name** to the LOC code (`BLD1` … `BLD6`, `EXT` for the site model). **(measured)** STING's LOC detection reads the room first, then the workset name, then Project Information **Building Name → Name → Address** (`ParameterHelpers.DetectLocSpatial` / `DetectProjectLocOrNull`), so an element with no room and an un-prefixed workset still lands on the right building. Without it, it falls to the token policy fallback (`BLD1`) — the trap in Part 6.
- **The conflict you must resolve (OD-2):** the issued Project Delivery Playbook §4.7 and the managing playbook §8 prefer **per-volume worksets** (`BLD2_Mechanical`), and the corporate owner-standards rule `workset-building-prefix` (`^(BLD[1-6]|EXT)_`, WARN) is **enabled on `main` today** — the pending KUT review branch disables it in the corporate baseline, but it has not merged. With one model per building, that rule warns on **every** workset above. Either adopt prefixed names (`BLD2_M-MECH-Ducts` — STING's `CreateWorksets` will not make them for you) **or** keep the scheme above and set the rule `"enabled": false` in the project overlay `_BIM_COORD/owner_standards.json`. **Recommendation:** the latter — the prefix exists only to locate elements in a model that holds more than one building, and this model set never does. The KUT Mobilisation workflow's step label "Per-building worksets (BLD1..BLD6 / EXT prefix)" is wrong either way: the command it runs makes no prefixed worksets.

### 1C.5 Linking — who links what, and how

**Coordinates first (D3).** In each model: **Manage ▸ Project Location ▸ Coordinates ▸ Acquire Coordinates** from the site model, once, before anyone links anything else. Only the site model's owner ever **Publishes** coordinates.

**Every link (Insert ▸ Link ▸ Link Revit):** Positioning **Auto – By Shared Coordinates** · reference type **Overlay** (Attachment only for a deliberate nest — Part 1A) · placed on its own `Z-Linked-*` workset · **pinned** · never bound, never imported. Manage reload paths in **Manage ▸ Manage Links**.

| Model | Links (Overlay) | Owns | Copy/Monitors |
|---|---|---|---|
| Architecture `0n` | `S`, `M`, `E`, `P`, `Y` of the same building | **levels and grids** of its building | — |
| Structure `0n` | `A` (and MEP for openings) | structural elements | levels and grids from `A` |
| MEP (`M` / `E` / `P` / `Y`) `0n` | `A`, `S`, and the other MEP roles of the same building | its systems | levels and grids from `A` (or from `S` if the BEP names structure as grid owner — `TODO(KUT): confirm in BEP`) |
| Site `00` | all six buildings' `A`, plus `C` and site services | toposolid, external works, **shared coordinates** | — |
| Federated `ZZ` | every model, via the ACC SHARED copies | nothing — links only | — |

- **Copy/Monitor:** **Collaborate ▸ Manage Models ▸ Copy/Monitor ▸ Select Link** → pick the architect's link → copy levels and grids. When the architect moves one, **Collaborate ▸ Manage Models ▸ Coordination Review** in your model shows it; accept or reject — never silently re-draw.
- **Inside a firm**, linking a colleague's *central* is fine — Revit shows its last synchronised state. **Between firms**, link only the ACC SHARED copy in `Incoming\` (1C.2 rule 2).
- **Federated checking:** Navisworks Manage and ACC Model Coordination are the system of record for clash (managing playbook §13); STING's clash run and `ACC_PullClashes` are the in-Revit pre-check and the ranked pull — buttons in Part 9.

### 1C.6 What worksharing changes for STING

**Everyone runs the same plugin build.** The Information Manager issues one STINGTOOLS build to every seat that runs it; nobody installs their own. Two builds tagging the same central is two sets of rules writing one model.

**What syncs with the central (measured — it is all stored in the model):** Project Information values (`Number`, Building Name, every `PRJ_ORG_*`), every tag and token parameter, the ES project-root stamp (`StingProjectRootSchema`), and the SEQ counter element (`StingSeqLockStore`). Set D1's Project Information checklist **once in the central**, synchronise, and every local inherits it.

**⚠ What does NOT follow the central: STING's project folder.** **(measured, not yet exercised in Revit)**

- `ProjectFolderEngine.GetRootPath` resolves the root from `doc.PathName` — the folder of **the file you have open**. In a workshared model that is your **local copy**, not the central. The ES root stamp is stored *relative to* that folder, and `TagConfig.LocateProjectConfig` reads `project_config.json` beside the open file, or from that root's `_BIM_COORD`. Nothing in the folder engine reads `GetWorksharingCentralModelPath`.
- **Consequence on a server-hosted central:** each modeller's `KUT\` tree is created **under their local-file folder** (e.g. `C:\KUT_Local\KUT\`); the owner pack copied next to the central (`project-templates/KUT/README.md` §5 step 1 says exactly that) is **not read** by anyone working in a local; per-workstation `_BIM_COORD` stores (issues, registers, transmittals, `acc/acc_settings.json`, KPI log) **diverge silently**. Tagging without the pack's `project_config.json` uses the built-in LOC list `BLD1–BLD3, EXT` — so `BLD4`–`BLD6` fail `ValidateTags`.
- **On a cloud model (option C) the behaviour is unknown** — `PathName` is not a Windows folder, and the most likely result is the `Documents\<code>` fallback (GetRootPath step 5) on each PC. Test it in the pilot.

**Until that is fixed, work like this (recommendation):**

1. **One project root, on the server.** The federated model `ZZ` is saved (not workshared) in `\\REPLACE_WITH_SERVER\…\KUT\Coordination\`. Open it, run **BIM ▸ Folder + setup ops ▸ "⚙ Setup"** (`CreateFolders`), and copy the **whole** KUT pack (`project-templates/KUT/_BIM_COORD/`) into that root's `_BIM_COORD`. Every **project-level** STING command — registers, transmittals, issues, ACC pull/sync/upload, Fohlio, Niagara, SpecLink, KPI, gate workflows, BOQ export, CDE folders — is run **from that model on that workstation**, and nowhere else.
2. **On every modelling PC**, copy the pack's `project_config.json` into the fixed local-file folder from 1C.3 (beside the local copies). Check it took: STING Panel ▸ **CREATE TAGS** ▸ *Advanced setup, schema & migration* ▸ **"Project Cfg"** (`ConfigEditor`) names the file in force. If it names none, or a different one, stop.
3. **Modellers use STING only for in-model work** — tagging, validation, QA, MEP calculations, drawing production. Their results are parameters and views in the model, so they reach the central on sync; nothing they need lives in the per-PC folder.
4. **Day-0 proof (Information Manager):** two PCs, one test central. On each, open a local and run **Project Cfg**; then tag one element on each PC without syncing, sync both, and run **CREATE TAGS ▸ ⚙ QUALITY ASSURANCE ▸ "Valid"** (`ValidateTags`). Record what you see in the mobilisation log. This is the test that turns "measured" into "verified".

**SEQ numbers in a workshared model (measured — `StingSeqLockStore`).** STING keeps the SEQ counters in one model element and **borrows it** before handing out a new number. If someone else holds it, or central has a newer copy, the new number is **blocked** (default `SEQ_LOCK_MODE: block`): the auto-tagger defers the element, Batch Tag reports it. The fix is always the same — the holder synchronises, you **Reload Latest**. One window is not covered: two people tagging for the **first time** before either has synced. So the first tagging pass on a new central is done by **one person**, who synchronises before anyone else tags. Duplicates from that window are repaired by **TAG STUDIO ▸ Tools ▸ *Advanced placement ops* ▸ "Repair dupes"** (`RepairDuplicateSeq`). The lock mode is set under **TAGGING ▸ DATA TAGGING (ISO 19650) ▸ "Tag Rules"** (`TagBehaviour`).

**Batch operations need the elements free.** `BatchTag`, `TagAndCombine` and any project-wide writer must borrow every element they touch. Run them when the others have synchronised and relinquished (lunchtime or end of day), and synchronise immediately after.

**STING LAN buttons — do not use (measured):**

| Button | What is wrong |
|---|---|
| INTEROP ▸ LAN COLLABORATION ▸ **"Auto-Sync"** (`LANAutoSyncToggle`) | it starts a timer that **only writes a log line** — it never synchronises (`LANCollaborationEngine.StartAutoSync`: *"actual sync must be dispatched to Revit main thread"*), yet the Team dashboard reports Auto-Sync **Running**. A user who trusts it does not sync at all. |
| INTEROP ▸ LAN COLLABORATION ▸ **"Sync"** (`LANSyncToCentral`) | works, but writes its own `<central>.rvt.lock` file next to the central and refuses to sync while that file exists; a crash mid-sync leaves a stale lock that must be deleted by hand. Revit's own Synchronize with Central needs none of this. |
| INTEROP ▸ LAN COLLABORATION ▸ **"Enable WS"** (`LANEnableWorksharing`) | the second workset scheme (1C.4) |

**The auto-tagger** (SETUP ▸ WORKFLOW AUTOMATION ▸ **"AutoTag"**, `AutoTaggerToggle`) is per session and starts **off**. Turn it on only after the first tagging pass has been synchronised (the SEQ window above). It skips elements on worksets you do not own (`StingAutoTagger`).

### 1C.7 Speed rules — what actually makes the team fast

1. **Small, frequent syncs.** A two-hour sync takes seconds; a two-day sync takes minutes and fails more.
2. **Open only the worksets you need** (Specify… on open), and close the `Z-Linked-*` worksets you are not coordinating against.
3. **Borrow, do not own.** Workset ownership is for the length of one task.
4. **One owner per model** (the Task Team Manager or a named coordinator) for audit/compact, link management and STING batch passes.
5. **Datum is frozen at mobilisation.** Levels, grids and shared coordinates move only through the owner and Coordination Review — every unannounced move is a day of rework downstream.
6. **The exchange cycle is fixed.** Everyone publishes to ACC SHARED on the same day (managing playbook §12), so the federated model is never half old, half new.
7. **Model to the gate's LOD, no further** (Part 3 LOD ladder).

### 1C.8 Owner decisions this part needs

| # | Decision | Recommendation | Recorded in |
|---|---|---|---|
| OD-1 | Where centrals are hosted: office server per firm (A), Revit Server (B) or ACC cloud worksharing (C) | **A now; pilot C on `06`** before adopting it project-wide | BEP §4.4 and §6.1 (software) |
| OD-2 | Workset naming: discipline-only (building carried by the file + Building Name) **or** `BLDn_` prefix | **Discipline-only**; disable `workset-building-prefix` in the project overlay | BEP §4.4–4.5; `_BIM_COORD/owner_standards.json` |
| OD-3 | Licences: which authors (if any) hold the cloud-worksharing subscription | needed only for C | BEP §6.1 |
| OD-4 | Exchange cadence to ACC SHARED: fortnightly, or weekly in design peaks | fortnightly, weekly from Deliverable B to C | MIDP / managing playbook §12 |
| OD-5 | Grid and level owner per building: architecture or structure | architecture | BEP |
| OD-6 | The coordination workstation and the server path that holds STING's project root (1C.6) | the IM's PC, federated model in `KUT\Coordination\` | mobilisation log |
| OD-7 | Who installs and updates the STING build on each seat | the IM, one build for all | mobilisation log |

---

## Part 2 — Zoning: when, why, and how to zone KUT

### Zone is not the same as Volume or Level

| Axis | Answers | On KUT |
|---|---|---|
| **Volume / Location** (`LOC`) | *Which building?* | `BLD1, BLD2, BLD3, BLD4, BLD5, BLD6`, `EXT` (container volume `01`–`06`, `00`) |
| **Level** (`LVL`) | *Which storey?* | `GF, 01, 02, RF, B1` |
| **Zone** (`ZONE`) | *Which functional or management area?* | see below |

A zone is a **management** boundary, not a geometric one. You zone so that a person can be given a slice and own it — for coordination, phasing, a work package, a tender section.

### When to create zones

**Once the layout is frozen and before you tag anything.** Zones are written into every asset tag; changing them later means a re-tag.

**Do not zone** a single simple building with one storey, one contractor, one package. **Do zone** when — and these hold on KUT:
- more than one building on a shared site ✔
- packages tendered or built separately ✔
- distinct operational areas ✔
- phased construction (49-month programme) ✔

### The zoning for KUT

Zone identifies the **functional wing/area within a building** (which is what the tag grammar `…-BLD1-Z01-…` expresses). Deliberately few — a zone you cannot explain in one sentence is a zone you do not need.

| Building | Suggested zones | Notes |
|---|---|---|
| **BLD1** (Temple) | `Z01`…`Zn` `[FILL: from the Temple plan / prototype — e.g. entry/foyer, ordinance areas, baptistry, support]` | The Temple has clearly distinct functional zones; confirm them from the prototype before tagging. |
| **BLD2** (Meetinghouse) | `Z01` assembly/chapel, `Z02` classrooms/offices `[FILL: confirm]` | |
| **BLD3** (Housing) | `Z01`…`Zn` per unit/block `[FILL: confirm]` | |
| **BLD4 / BLD5 / BLD6** | usually a single zone each (`Z01`) | Small buildings — one zone. |

**At the site/federated level**, use zone (or the LOC + a package key) to separate what you want reported as a *package*: BOQ grouping by package, per-package carbon, per-package programme.

### How to apply them

1. Put the codes in `project_config.json` under **both** `ZONE_CODES` and `CUSTOM_VALID_ZONE` — same split-vocabulary problem as LOC.
2. Zone is auto-derived by `SpatialAutoDetect.DetectZone` from the room and element position, so it **mostly fills itself** once rooms exist. Check with `PreTagAudit`, correct strays with the token writer.
3. Use the zone, not the building, for anything reported as a package.

**What zoning is not for:** do not use zones to control drawing extents — that is what scope boxes do. Do not use zones as a substitute for LOC.

---

## Part 2 (cont.) — Scope boxes: when, and how to name them

**What they are for:** controlling *view extents* consistently across many views, and cropping plans to a building or a zone. A documentation device, not a data device.

**When to create them:** **after** shared coordinates are set and the layout is frozen; **before** you create any views or sheets. Creating them late means re-cropping every view.

**On KUT you need few**, because each building is its own model/link:

| Scope box | Purpose |
|---|---|
| Whole site at 1:500 | the site plan |
| Site split (match line) | large site sheets if 1:500 will not fit A1 |
| One per building | setting-out and platform drawings |

### The naming rule is a hard contract

If you want STING to generate views/sheets from scope boxes, the name must match exactly:

```
STING::<drawing-type-id>[::<level-code>][::<tag>]
```

Regex-enforced. Legal characters inside a segment are **letters, digits, `.`, `_`, `-` only — a space breaks it.** For KUT:

```
STING::arch-site-A1-1to500::ZZ::SITE
STING::arch-setting-out-A1-1to100::GF::BLD1
STING::arch-setting-out-A1-1to100::GF::BLD2
STING::arch-plan-A1-1to100::GF::BLD3
STING::arch-plan-A1-1to100::GF::BLD4
```

The middle segment is the **drawing type id** and must exist in `STING_DRAWING_TYPES.json` — an unknown id is warned and skipped, not guessed. `DrawingTypes_FromScopeBoxes` is **idempotent** (indexes by `(drawingTypeId, scopeBoxId)` and updates rather than duplicating); `DrawingTypes_SuggestFromScopeBoxes` is the dry run — use it first.

> A plain `SB-BLD1` name is fine for a purely manual workflow but does nothing in STING. If you are going to use the automation, use the `STING::` form from the start.

Inside each **building model**, you generally do not need scope boxes — the building is the extent.

---

## Part 3 — The step-by-step sequence

### Stage A — Mobilise (before Revit)

1. Fix the **project code** `KUT` (D1).
2. Agree the **measurement standard and bill structure with the QS** in writing (D7).
3. Obtain: survey in DWG/CSV, sections/elevations, roof/spire design, outline specification, the 1–40F prototype references. Raise the RFI list in Part 7.
4. Write a short BEP: these decisions, the file naming, the LOD table (tied to the programme, below), who models what.

### Stage B — Set up the container

5. In the first `.rvt`: set **Project Number `KUT` and Name** → **save** → **close and reopen** so the root gets its ES stamp (D1).
6. Run **`CreateFolders`** and pick **CdeFirst** explicitly. Confirm the code-suffix setting.
7. Add **all three** LOC keys — `LOC_CODES`, `CUSTOM_VALID_LOC`, `LOC_CODES_EXTRA` — to `project_config.json` with the same codes (`BLD1, BLD2, BLD3, BLD4, BLD5, BLD6, EXT`), *before* anything is tagged (Part 6). The KUT overlay pack's `_BIM_COORD/project_config.json` already carries all three; check that no older `project_config.json` sits beside the `.rvt`, because that copy takes precedence.
8. Run **`LoadSharedParams`.** Everything downstream depends on it, and `SetString` silently no-ops on unbound parameters.
9. Fill the **`PRJ_ORG_*`** parameters in Project Information by hand.
10. Run the **`ProjectSetup`** wizard for levels, grids, disciplines, standards.
11. Start every model from the **corporate template**, so bindings and types come with it.
12. Create the models of D4 (six buildings + site + federated), each with Project Information filled in — one central per discipline per building, hosted and workshared per **Part 1C** (decisions OD-1 … OD-7 first), with **Building Name** set to the LOC code.
13. Set coordinates in the site model; **Acquire Coordinates** into each building model (D3).

### Stage C — Ground first

14. Import the survey **points file** (not the PDF). Build the **Toposolid** from it. Set contour display to match the survey so you can verify visually against the issued sheet.
15. Model the **boundary/perimeter** and existing features that matter — existing structures, trees to retain, existing services, the access from the CBD road network.
16. Model the **platforms** — in Revit 2026 use **toposolid subdivisions with a negative offset** (they excavate the host automatically, are individually selectable, own subcategory in V/G). One subdivision per building platform, one per parking/hardstanding apron.
17. **Cut/fill.** Elements that intersect a toposolid can excavate it, and the excavated volumes schedule — a defensible earthwork quantity. It does **not** work for masses or generic models. Build platforms from toposolids or floors, never masses.
18. Retaining walls, steps, ramps, paths, parking. Model at the same LOD as the buildings.

### Stage D — The lead building (the Temple)

19. Model **BLD1 completely and correctly first** — it is the specification benchmark for the site.
    - Grids per prototype/consultant CAD.
    - External and internal walls, one wall each, cores identified.
    - Doors and windows as typed families with our type names.
    - Roof / spire — **this is where you need the missing information** (Part 5).
    - Rooms, with finish codes from the specification.
    - Sanitaryware, mechanical and FF&E as scheduled families, not decoration.
    - Full parameter/type naming per D10 as you go — *not* as a clean-up pass.
20. Run the **tagging and validation pass on BLD1 alone.** Fix every warning. Only then move on.
21. Link BLD1 into the site model, position and rotate per the setting-out schedule.

### Stage E — The other five buildings

22. **Meetinghouse (BLD2)** — from the meetinghouse prototype.
23. **Housing / Ancillary (BLD3)** — largest by area; the repeated unit/block, if any, is a candidate for a group *inside* that one model where rooms share a level.
24. **Utility Building (BLD5)** — the most services-heavy for its size; coordinate mechanical/electrical/plumbing extract, plant and drainage early against the 1–40F prototype.
25. **Grounds Building (BLD4)** and **Guard House (BLD6)** — small; model to the same standard.

### Stage F — Federate and check (Navisworks)

26. Build the **federated model**: links only. Federate for clash in **Navisworks Manage**; use **Speckle** for quick browser review/sharing of the federated data with the team.
27. Clash and coordination pass. On this site the real clashes include **building-vs-ground** (platforms, doors onto drops, path gradients, drainage falls) as well as MEP-vs-structure. Check both explicitly.
28. Produce and issue the **setting-out drawing** with the coordinate table.

### Stage G — Data completeness before documentation

29. Run the **pre-tag audit** (dry run) → fix → **batch tag** → **validate**. Nothing goes to BOQ or to sheets until validation is clean. Data first, drawings second.
30. Check the room, door and window schedules are complete and unique-marked **per building.**

### Stage H — Documentation

31. Create **scope boxes** (Part 2).
32. Apply the **drawing types** (site, setting-out, plans, sections, elevations, details, floor-finishes, schedules). Each carries sheet size, title block, scale, view template, crop strategy and sheet-number pattern as one bundle.
33. Produce sheets from the drawing types, not by hand. Let the sheet numbering pattern generate the numbers (4-digit type bands).
34. Run the **ISO 19650 sheet compliance check** before the first issue.

### Stage I — BOQ

35. Set the standard with **`Cost_SetMeasurementStandard`** (POMI, per D7); author `_BIM_COORD\takeoff_rules.json` for any bespoke element with no corporate rule; run **`Cost_ReloadRules`.**
36. Put the project rate card at `_BIM_COORD\rate_card.json` and the project bill descriptions at `_bim_manager\boq_custom_templates.json`.
37. Run **`BOQPrepForExport`** → **`BOQ_RateGapReport`** → fix gaps → **`BOQExportProfessional`.** Sanity-check total walling and roofing m² by hand before you believe the total (Part 4).
38. Add the **measured additions** (`BOQAddManualRow`) for everything in the right-hand column of D8.
39. **`BOQSnapshotSave`** on every issue. From the second issue on, **`BOQSnapshotCompare`** — the single most valuable BOQ artefact for the Owner: it answers "what changed and why did the price move."

### Stage J — Issue and control

40. Transmittals for every issue through ACC. Revision management on every re-issue. Nothing leaves `02_PUBLISHED` without a transmittal record.

### LOD ladder mapped to the Owner Work Program (June 2026)

Write the LOD ladder into the BEP against the 49-month programme so everyone knows what "done" means at each gate.

| Programme point | Month | LOD |
|---|---|---|
| BOD / **Deliverable A** | M1 | **LOD 200** — generic geometry, approximate size/shape/location |
| **Deliverable B** / 50% | M4 | **LOD 300** — specific geometry, dimensioned, coordinated |
| **Deliverable C** / 100% | M8 | **LOD 350** — with interfaces/connections to other systems modelled |
| Tender | M9–M11 | (design frozen; production information) |
| Construction | M12–M43 | **LOD 400** — fabrication/installation detail |
| FF&E | M44–M47 | (Fohlio FF&E and O&M data) |
| Close-out / **Deliverable D** | M48–M49 | **LOD 500** — verified as-built, handover-ready |

Do not model *ahead* of the LOD the gate requires — LOD 300 detail at Deliverable A is wasted effort that will be re-cut. Do not fall *behind* it either; the gate checklist tests it.

---

## Part 3 (cont.) — Every file you need, listed

### From the surveyor — ask for all six

| # | File | Format | Why | If you don't get it |
|---|---|---|---|---|
| 1 | **Point file** | `.csv`/`.txt` (Point, Easting, Northing, Elevation, Code) | The only correct source for a toposolid | You cannot build accurate topography. Blocking |
| 2 | **Survey drawing** | `.dwg` (not PDF) | Boundary, contours, existing structures, trees, services on real coordinates | You trace, and every dimension inherits 100–300 mm error |
| 3 | **Control point schedule** | `.pdf`/`.csv` | The permanent station(s) you set out from | No check on whether the model is on the right grid |
| 4 | **Coordinate system statement** | text/email | Which projection and vertical datum (UTM 36N? Arc 1960? Local grid?) | Your mAOD values are numbers with no meaning |
| 5 | **Boundary / title deed plan** | `.pdf` + `.dwg` | The legal boundary, not always the fence line | You may set out over a boundary — critical on a tight CBD site |
| 6 | **Existing services / utilities** | `.dwg`/markup | Existing water, power, drainage, telecoms on a CBD site | You design through a live main |

**One email covers it.** State that the point file must include the elevation column.

### From the consultants / prototype

| File | Format | Status |
|---|---|---|
| Floor plans (all six buildings) | `.dwg` | `[FILL: have as… — request native DWG]` |
| Sections | `.dwg` | `[FILL]` |
| Elevations | `.dwg` | `[FILL]` |
| Roof / spire design | `.dwg` + detail | `[FILL — likely blocking for BLD1]` |
| Outline specification | RIB SpecLink / `.pdf` | `[FILL]` |
| Door & window schedule | `.xlsx` | `[FILL]` |
| Finishes schedule | `.xlsx` | `[FILL]` |
| 1–40F prototype MEP references | prototype set | `[FILL — needed to seed MEP models]` |

### What you will produce

| Category | Files | Count |
|---|---|---|
| **Revit models** | six building models + site + federated | **8 `.rvt`** (more if disciplines split per building) |
| **Project data** | `project_config.json`; `rate_card`, `takeoff_rules`, `carbon_factors_ug`, `boq_links`, `boq_custom_templates`, `drawing_types` in `_data/coord/` | JSON set |
| **Drawings** | site, setting-out, per-building plans/sections/elevations/details/schedules | `.pdf` + `.dwg` per issue |
| **Bill** | BOQ workbook, snapshot per issue | `.xlsx` |
| **Data exchange** | IFC4 per building + federated; Speckle streams for review | `.ifc` / Speckle |
| **Coordination** | setting-out schedule, transmittals, revision register | `.csv` / `.json` (through ACC) |

**Eight models, one JSON set, one CSV that matters more than any of them** — the setting-out schedule.

---

## Part 3A — Floors and finishes: the definitive method

**Layer by trade, not by room — and align by top face, never by centre.**

### The rule

| Element | One per… | Sketch extent | Top of element |
|---|---|---|---|
| **Structural slab / oversite** | pour | the whole footprint | SSL |
| **Screed** | screed zone (dry vs wet-to-falls) | the zones sharing a screed spec | FFL − finish thickness |
| **Floor finish** | **room** | that room only | **FFL** |

Three floor elements stacked, each a *separate* Revit floor. Not one compound. Not a compound per room.

**Why not one compound floor:** the moment room A is tile and room B is screed you must split the sketch anyway, so the compound bought you nothing and cost you per-room finish scheduling. A compound also gives you exactly one variable-thickness layer, which you will want for screed-to-falls in wet areas — and you cannot then also vary the structure.

**Why not a compound per room:** element bloat, every room boundary becomes a floor edge to maintain, and you re-declare the structural slab in every room (double-counting concrete unless you strip it from all but one).

### Alignment — top face, not centre

Revit draws a floor **downward from its level**, so the level plane is the *top* of the floor. Set **Level = FFL** for every room, then:

| Element | Height Offset From Level |
|---|---|
| Finish floor | `0` — top sits at FFL |
| Screed | `−(finish thickness)` |
| Structural slab | `−(finish + screed)` — its top is SSL |

For 10 mm tile on 50 mm screed on a 150 mm slab: finish `0`, screed `−10`, slab `−60`. Top of slab lands at −60 = SSL — the number the structural engineer and the setting-out drawing both use. **Centre-to-centre alignment is wrong** and drifts thresholds, falls and level annotations with every thickness change.

### Four settings that matter

1. **Room Bounding — OFF** on the screed and finish floors. Left on, they slice room volumes and corrupt the finishes schedule.
2. **Structural — ON** for the slab only.
3. **Screed to falls** — a floor type with a variable-thickness layer, then Modify Sub Elements to add points at the gully. Real volume for the falls, not a flat average.
4. **Slab thickenings, downstands, edge beams** — separate elements, separately measured and poured.

### Skirtings

Do not model 100 mm walls. Use a **wall sweep** hosted on the wall, or take the length from the Room perimeter. A skirting is a linear bill item; it needs a length, not a solid.

### Use STING's finishes engine rather than doing this by hand

Dock tab **MODEL → "Plaster, render, paint & coatings"**:

| Button | Tag | What it does |
|---|---|---|
| **★★ Smart Covering** | `CoveringSmartApply` | substrate detect → mix design (BS EN 13914) → coverage → injects the finish layer into the compound type → QA → tags |
| **★ Batch All** | `CoveringBatchApply` | the same across all walls, beams, columns |
| **Room Finishes** | `CoveringRoomSchedule` | builds the room-based wall/floor/ceiling finish schedule and writes it back to Rooms |

> **Watch this default.** `RoomFinishScheduler.GenerateSchedule` falls back to the literal `"Power-floated concrete + carpet/vinyl"` when the floor finish is empty. That is a UK office default and it is wrong for KUT. Populate `BLE_ROOM_FINISH_FLOOR_TXT` from the specification **before** you run Room Finishes, or you bill carpet you are not laying.

### Room-based finish data — the full method, and the trap in it

#### ⚠ There are TWO finish parameter families and they do not talk to each other

| Family | Written by | Read by |
|---|---|---|
| **Revit built-ins** — `Floor/Wall/Ceiling/Base Finish` | only the **Fohlio importer** | the **`ISBRoomFinish`** schedule |
| **STING shared params** — `BLE_ROOM_FINISH_FLOOR_TXT` etc. | **`CoveringRoomSchedule`** | the covering engine, the BOQ description tokens |

Run "Room Finishes" then create the ISB schedule and the schedule is empty — they write and read different parameters. There is a one-way bridge: `NativeParamMapper` copies **built-in → STING** during tagging (`SetIfEmpty`). Nothing goes STING → built-in.

**Therefore: fill the Revit built-ins first** (a native Revit schedule, an IFC export, the ISB schedule and — importantly on KUT — **Fohlio** can all see them, and the tag pipeline copies them into the STING params for you).

#### The sequence

1. **Fill the Revit built-in finish parameters, per room** — via a room schedule with the four columns, or paste from Excel. Use **your** finish codes, not prose.
2. **Run the tag pipeline once** — `TagAndCombine` / `BatchTag`. `NativeParamMapper.MapAll` copies the four built-ins into `BLE_ROOM_FINISH_*_TXT`.
3. **Generate and write back the finish schedule** — `CoveringRoomSchedule`. It fills *missing* finishes with `SetIfEmpty`, never overwriting step 1. (Its "N rooms updated" count increments even when nothing changed — that number is not evidence.)
4. **Build the finish schedule view** — INTEROP → ISB drop-down → "Room finish".
5. **Cross-check against the geometry** — compare the room schedule's floor areas against the sum of your per-room finish floors. A mismatch means a missing finish floor or a room boundary that does not match the walls. The text tells you the *intent*; the geometry tells you the *quantity*; the discrepancy tells you where the model is wrong.

#### Finish codes — there is no code list in the tool, so define one

STINGTOOLS ships no room-finish code legend; every finish parameter is free text. Define KUT's codes and put them in the finish parameters, description carried by the material:

| Code | Finish | Maps to material |
|---|---|---|
| `FL-01` | Ceramic/porcelain tile | `CERAMIC TILES 300X300MM 10MM` |
| `FL-02` | Cement screed, power-floated | `STANDARD CEMENT SCREED 50MM` |
| `FL-03` | Terrazzo (in-situ or tile) | `[FILL: from spec]` |
| `FL-04` | External paver / stone | `CONCRETE PAVER 80MM` |
| `WL-01` | Cement-sand render + emulsion | `CEMENT SAND RENDER 1-4 (EXTERNAL STANDARD)` |
| `WL-02` | Ceramic tile, wet areas | `CERAMIC TILES 300X300MM 10MM` |
| `CL-01` | Plasterboard + skim + emulsion | `GYPSUM BOARD STANDARD 12.5MM` |
| `SK-01` | Skirting | `[FILL: from spec]` |

> Actual KUT finishes come from the prototype/consultant finishes schedule — `[FILL: reconcile this list to the issued finishes schedule.]` Put the **code** in the Revit finish parameter and let the schedule carry a description keyed off it: a code is filterable, sortable, checkable; a sentence is not.

> **What the tool cannot do:** nothing reads a room's finish and creates the matching **floor finish element** — your per-room finish floors are drawn by hand (gap K-3).

> **Note on Parts.** STING's takeoff does not measure Revit *Parts* — `OST_Parts` never appears in a takeoff rule. If you use Parts, they are for your own schedules; the BOQ will not see them unless you author a rule for the category.

---

## Part 3B — Do schedules include linked elements? Yes, twice over — and one can under-count you

**Two separate mechanisms. Do not confuse them.**

### 1. Revit's own schedules

Schedule → Properties → Fields → Edit → tick **"Include elements in links".** Supported for model-element schedules and drawing lists; **not** for note blocks, view lists or key schedules.

The limitation that bites: once links are included, the **Family, Type, Family and Type, Level and Material parameters become read-only — and you cannot filter the schedule by them.** So "one door schedule, filtered by building" does not work across links. Filter on a STING parameter instead — `ASS_LOC_TXT` stays filterable. That alone is a reason to have the LOC vocabulary right before you start.

Room-to-element relationships also break across links — keep rooms and the elements they describe in the same model.

### 2. STING's BOQ — it walks the links itself

The BOQ engine does **not** depend on the Revit checkbox. `BOQCostManager.CollectLinkedItems` enumerates every loaded `RevitLinkInstance`, opens the link document, and runs the full takeoff inside it. Rows are tagged `[Linked: <model>]`, carry a `SourceModel` value, and can be grouped by it. Configure it from the **BOQ Cost Manager panel → "Linked models in takeoff"** picker; persisted to `_BIM_COORD\boq_links.json`.

### ⚠ The de-duplication trap

```csharp
if (!seenTitles.Add(linkName)) continue;
```

**A link placed more than once is taken off exactly ONCE by default** — the engine de-duplicates by model title, because the common case is a shared reference placed once.

**On KUT this is not a live risk**, because the six buildings are each unique and each linked once. But keep it in mind: **if you ever author a repeated unit** (a housing block, a repeated fit-out) once and place it *N* times, you must **tick the per-link multiplier** (*"Multiply repeated links…"*), or the bill contains one and the rest are free. Ticking it multiplies `Quantity`, `EmbodiedCarbonKg` and `BiogenicKg` by the instance count and tags the row `[Linked: … ×N]`.

Two consequences worth knowing:
- **Linked rows are read-only** — not cost-stamped, so no `CST_*` parameters are written back into the building model, and you cannot select the element in the host from the BOQ row. Cost write-back only happens in the host.
- The link takeoff is **cached per link path**, and the multiplier is applied *after* the cache.

---

## Part 3C — Topography: how to get defensible cut and fill

Revit gives cut and fill only through one specific workflow. Anything else gives a number that is not a quantity.

### The graded-region method — the only one that works

1. Build the **existing** toposolid from the surveyor's point file. **Phase Created = Existing**, **Phase Demolished = None.**
2. Use a toposolid **type with a variable-thickness material** in its structure. Not optional — the Boolean that produces the volumes needs the existing ground to be a real solid with thickness.
3. Run **Graded Region** on it. Choose **all points**, not perimeter-only, for accuracy.
4. Grade the *new* copy: move points, add subdivisions, form the platforms.
5. Select the graded toposolid → **Cut** and **Fill** appear in Properties, both schedulable.

Accuracy is about **±2 %** — good enough to price against, and say so on the drawing rather than implying millimetre precision.

### Platforms

In Revit 2026 a **toposolid subdivision with a negative offset** excavates the host automatically, is individually selectable, and is its own subcategory in V/G. One subdivision per building platform, one per parking/hardstanding apron. Floors, roofs and other toposolids that intersect a toposolid excavate it and those volumes schedule; **masses and generic models cannot** — build every platform from a toposolid or a floor, never a mass.

### Step by step, in Revit

**Building the ground:**
1. Open the **site model.** `Insert → Link CAD`, pick the survey DWG. Positioning **Auto – Origin to Internal Origin**, units **Auto-Detect**, uncheck *Orient to View.*
2. Check it landed — place a **Spot Coordinate** on a known survey point. Hundred-thousands = real coordinates; small numbers = the DWG was moved, stop and go back to the surveyor.
3. `Massing & Site → Toposolid → Create from Import → Specify Points File`, choose the `.csv`, units **Metres** (or *Create from Import → Select Import Instance* if the DWG has 3D contours).
4. Check the extremes against the survey. `[FILL: expected low/high levels from the KUT survey.]`
5. Toposolid → Properties → **Phase Created = Existing.**
6. Type Properties → **Edit structure** → ensure a **Variable** thickness layer. Without it the cut/fill Boolean has nothing to work with and you get zeros.
7. V/G → Toposolid → Contours → set the interval to match the surveyor's sheet and compare.

**Platforms and cut/fill:**
8. Toposolid → **Graded Region** → *"…based on perimeter and interior points"* (all points).
9. On the **new** toposolid: `Modify → Sub-elements` to drag points, or **Add Subdivision**, sketch the platform, set **Height Offset** negative to cut into the host.
10. Repeat per platform: six building pads plus parking/apron areas.
11. Select the graded toposolid → read **Cut** and **Fill.**
12. Toposolid schedule with **Name, Cut, Fill, Net** — that table is your earthwork quantity.

**Making the numbers defensible:** state ±2 % on the drawing; never build a platform from a Mass or Generic Model; grade once after the layout is frozen; cross-check one platform by hand (area × average depth) — if Revit disagrees by more than ~5 % the variable-thickness layer is missing or you graded the wrong surface.

### What STING does *not* do — plan for it

STING prices `Toposolid` at a per-**m²** rate (area, not volume). There is **no takeoff rule, no command, no BOQ path that turns cut and fill volumes into bill lines.** So earthworks will not appear in an automated BOQ. Handle it deliberately: schedule cut/fill from the graded region; enter them as **measured additions** (`BOQAddManualRow`) with your own m³ rates for excavate / cart away / fill / compact; or author a project takeoff rule targeting the toposolid category with `quantitySource: SolidVolume`. Do not let it fall through silently (gap 7).

---

## Part 3D — AutoCAD → Revit: each building's setting-out point and shared coordinates

The goal: every one of the six models opens at the right place, rotation and elevation, and exports back to the surveyor's coordinates without anyone typing a number twice.

### Step 1 — Harvest the setting-out points in AutoCAD

For each building, decide the **SOP** — one unambiguous, permanent point (a named structural grid intersection is best for the rectangular KUT buildings). `ID` at each point returns X and Y (Easting and Northing). Record them with the rotation and intended FFL:

| Building | SOP description | Easting | Northing | Rotation | FFL (mAOD) |
|---|---|---|---|---|---|
| BLD1 | grid A/1 intersection | … | … | … | `[FILL]` |

That table is a deliverable — it is what the setting-out engineer works from on site.

### Step 2 — Make the site model the single source of truth

1. In the **site model**, link the survey DWG **Origin to Internal Origin**, check the DWG units.
2. Reveal the **Survey Point.** Un-clip it, **Manage → Coordinates → Specify Coordinates at Point**, pick a known control point and type its real E / N / elevation. Now the site model speaks the surveyor's language.
3. Set **True North** from the survey's north.
4. Put the **Project Base Point** somewhere convenient and round near the Temple.

Do this once. Everything else acquires from here.

### Step 3 — Push coordinates into every building model

For each building model: link the site model **Origin to Internal Origin**, then **Manage → Coordinates → Acquire Coordinates**, pick the site link. Then link the building models into the site with **Auto — By Shared Coordinates.** They land in the right place with no manual moving.

### Step 4 — Publish coordinates back

Once each building has acquired coordinates, run **Publish Coordinates** from the site model into each link. This writes the position into the building model, so an IFC or DWG exported from the building model alone still lands on the surveyor's grid.

> **KUT has no repeated building**, so you do **not** need the multi-Shared-Site trick that a repeated prototype unit would require. Each building has one position. (Keep Shared Sites in mind only if a repeated housing unit is authored once and placed several times — then each placement needs its own named Site.)

### Step 5 — Round-trip back to the surveyor

Export → Options → **Units & Coordinates → Coordinate System Basis = Shared.** The DWG lands on the surveyor's grid with no manual alignment. Same for IFC via the site/survey point setting.

### Measuring the rotation angle — three ways, in order of reliability

**Method 1 — from two coordinates (most reliable).** Pick two points on the same datum line of the building. `ID` both. `bearing = ATAN2(N₂−N₁, E₂−E₁)`. In Excel `=DEGREES(ATAN2(E2-E1, N2-N1))` — Excel's `ATAN2` takes **(x, y)**, so `ATAN2(ΔE, ΔN)` gives the angle anticlockwise from **east**, which is Revit's convention. Drops straight in.

**Method 2 — AutoCAD `DIST`**, read **Angle in XY plane.** Same convention.

**Method 3 — Revit `Align` + `Rotate`** — use to *check*, not to *derive*; it inherits your snapping accuracy.

**State the convention on the drawing.** "X° anticlockwise from grid east" is unambiguous; "X°" is not.

**A check that costs nothing:** after placing all six links, put a **Spot Coordinate** on each building's SOP in the site model and compare against your setting-out table. If any differs, that link was moved by hand instead of by its shared coordinates.

### Three checks before you trust the CAD

1. **Is the DWG on real coordinates, or near the origin?** `ID` on any surveyed point — hundreds of thousands = real grid; small numbers = someone moved it, go back to the surveyor.
2. **Are the units what you think?** A DWG in metres linked as millimetres lands 1,000× out.
3. **Is Z populated?** Many site DWGs carry levels only as text, flat at Z=0. If so, the contours are annotation, not geometry — you need the point file.

**Recording it.** One table, three places, all from the same source: a Revit **schedule** in the site model; the **setting-out drawing**; a **CSV** the contractor can load into a total station.

### The two-minute check that catches almost everything

Pick one known survey point. Place a spot coordinate on it. If the E, N and elevation match the surveyor's schedule to the millimetre, your coordinate chain is sound. If not, stop and fix it — every quantity, setting-out dimension and cut/fill volume downstream depends on this one thing.

---

## Part 3E — Tagging: the procedure, and the one caveat that will bite you

### The order matters

A tag renders whatever the element's tokens say, so the tokens must be right *before* the tag goes on.

1. **Load Shared Parameters** — once per model, before anything else. Without it the tokens have nowhere to land and every later step silently writes nothing.
2. **Set the project tokens** — DISC, LOC, ZONE per Part 1's naming standard. `Set Disc` / `Set Loc` / `Set Zone` from the CREATE tab.
3. **Tag & Combine** — the one-click path. It runs the full nine-step pipeline: category filter → type-token inherit → populate tokens → native-parameter map → formulas → build the ISO 19650 tag → write containers → TAG7 narrative → grid reference.
4. **Validate Tags** — read-only. Fix what it reports before issuing.

Do not place tags by hand from the Revit Annotate tab. A hand-placed tag carries no tokens, passes no validation, and reads blank on the sheet.

### Rooms need their own tag

Revit does not allow a Multi-Category tag to tag a Room, Space or Area — a Revit restriction, not a STING one. Use **`STING - Room Tag.rfa`** (already in the content library, manifest `tag-room`).

### The reload caveat — the one that will bite you

If anyone edits a tag family and loads it back, Revit offers **"Overwrite the existing version and its parameter values".** That wording is easy to skim past. It discards project-set values for **every** parameter of that family — not just the one edited. On a model where tag depth, style and warning visibility have been tuned, that is a project-wide regression.

**Before** editing any tag family: export a schedule of the tag category with the gate and style parameters as columns, and note the active mode from **Tag Studio → Presentation Mode → Report.** **After** reloading: re-apply the presentation mode first, then reconcile against the schedule.

### Door and window marks — a short mark on the plan, the full tag on the instance

A door shows a **type mark** (`DR-01`) on the drawing and carries the **full 8-segment tag** on the instance (`A-BLD1-Z01-GF-ARC-FIT-DR-0001`). The remaining segments are schedule columns, not label text — nobody reads an eight-field code off a 1:100 plan.

**Why the mark is per TYPE, not per instance:** a per-instance mark puts a unique number on every door to describe products that repeat; the schedule bloats and any type change needs many edits. The type mark is what the contractor orders against.

**The codes, measured from `TagConfig.Defaults.cs`:** PROD `DR` / `WIN` (note `WIN`, three characters); SYS `ARC` for both; FUNC `FIT` for both — so **FUNC cannot distinguish a door from a window; PROD is the discriminator.** (FUNC is not `EXT`/`INT`; `EXT` is a *LOC* code.)

**What is NOT automated:** nothing generates `DR-01`, `DR-02`, `WIN-01` sequences per type. `ASS_TYPE_MARK_TXT` is a one-way mirror of `ALL_MODEL_TYPE_MARK`. **Type marks are entered by hand** (or via a schedule with Type Mark as an editable column — far faster) (gap G-20).

**The exception — unique plant shows the full tag.** An AHU or a distribution board is one-of-one; its tag *is* its identity and it must show the full 8-segment code on the drawing (e.g. `M-BLD1-Z01-GF-HVAC-SUP-AHU-0001`). Rule of thumb: if you would order more than one from a schedule, short mark; if it appears once on a commissioning sheet, full tag.

### Tag depth — not yet settled

> **PLACEHOLDER — do not fill from memory.** Whether tag detail tiers (`TAG_PARA_STATE_1..10_BOOL`) are controlled per tag **type** or per tag **instance** is an open decision. Current evidence favours per-**type**. Until settled, set depth once for the project via **Tag Studio → Presentation Mode** and do not vary it per tag.

---

## Part 4A — Materials: exactly what to use and what to edit

### How the naming actually works

Two names per material, doing different jobs:

| Column | Becomes | Example |
|---|---|---|
| `MAT_ISO_19650_ID` | Revit **Keynote** | `A-FLR-CEMENT-SCREED-50MM-INT-SC01` |
| **`MAT_NAME`** | **Revit `Material.Name`** — the name everything joins on | `STANDARD CEMENT SCREED 50MM` |

`MAT_NAME` is **ALL-CAPS free text**, loosely `<MATERIAL/TYPE> <QUALIFIER> <SIZE>`. Not a controlled vocabulary — the same product may appear as `HOLLOW CONCRETE BLOCK 8IN (200MM)` in one sheet and `BLOCK HOLLOW 200MM` in another. That inconsistency has consequences (below). Load with `CreateBLEMaterials` and `CreateMEPMaterials` (they skip any material whose name already exists).

### The three things a material name controls

1. **Carbon** — matched by **substring, first-hit-in-file-order** against `byKeyword` in `STING_CARBON_FACTORS_UG.json`, then by exact `MaterialClass`.
2. **Waste %** — matched by **substring, first-hit** against `WasteTable`'s keywords, *on the carbon path only*.
3. **The bill description** — the identity class is prepended as the leading noun: *"Supply and fix **masonry** walls."*

So the rule: **name a material so that the first keyword it hits is the one you meant.**

### Rules folded in from the material-data alignment pass

A library alignment run (applied) fixed **580 material classes, 34 name normalisations and inferred 1,066 cost units.** Carry those rules into KUT:

1. **Normalise `MAT_NAME` dimension separators to `X`.** `CLAY BRICK STANDARD (225×112.5×75MM)` → `(225X112.5X75MM)`. The `×` character breaks joins and lookups. Use a plain capital `X` in every size string.
2. **Populate `MAT_COST_UNIT_OF_MEASURE` on every material you will use.** The alignment run added this column and inferred units (`m2`, `m`, `each`, `L`, `m3`, `kg`). A material with no cost unit prices ambiguously. Inference rules of thumb from the run: boards/renders/tiles → `m2`; linear runs/skirtings/pipe/wire → `m`; fittings/sanitaryware/boards-as-items → `each`; paints/primers → `L`; screeds/concrete/fill → `m3`; grout/adhesive by weight → `kg`. Set it explicitly; do not leave it blank.
3. **Get the identity class right — `Generic` resolves no carbon and bills as "generic".** In the source library `Generic` was ~29 % of rows; every one bills as *"Supply and fix **generic** walls"* and resolves carbon at a flat 200 default. Move materials to a real class: gypsum board → **Gypsum**; paints → **Liquid**; renders/plaster → **Gypsum** or **Concrete** as appropriate; blocks → **Masonry**/**Concrete**; tiles → **Ceramic**; stone → **Stone**; timber → **Wood**; insulation → **Insulation**; metal fittings/LED fixtures → **Metal**. `Ceiling` and `Flooring` are **element types, not materials** — they belong in `MAT_ELEMENT_TYPE`, not the class.
4. **Fix outright class errors before you bill or report carbon** — e.g. a lightweight screed misclassed **Metal** carbon-counts ~42× too high; block-paving misclassed **Wood**. Terrazzo, clay roof tiles, roofing felt and mineral-fibre ceiling tiles classed `Generic` all bill as *"generic roofs"* — set `Ceramic`/`Stone`/`Masonry`.
5. **A handful of classes and cost units cannot be inferred and must be decided by hand** (acoustic panels, moisture-resistant boards, some MEP boards, taps, flexible ducts). Decide these on the KUT material list before issue rather than shipping `Generic`.

### ⚠ Before you price anything: the material library rates are broken

`ALL_MODEL_COST` is written from `MAT_COST_UNIT_USD` but read as **UGX** with FX suppressed — every library material prices at roughly **1/3,700 of its real rate**, and because `MaterialLibraryRateProvider` sits at priority 95 (above the correct category rate at 90), **the wrong number wins.** Measured: `MAT_COST_UNIT_UGX` is exactly `MAT_COST_UNIT_USD × 3700` — the UGX column is derived, not independent, so it bakes a stale FX rate.

**For KUT, do not use material-library rates at all.** Put your rates in `_BIM_COORD\rate_card.json`, keyed on the **exact Revit category name**, case-insensitive:

```json
[
  { "Category": "Walls",  "UnitRate": 68000,  "Currency": "UGX", "Unit": "m2", "Note": "200mm hollow block, 1:4 render both faces" },
  { "Category": "Floors", "UnitRate": 145000, "Currency": "UGX", "Unit": "m2" },
  { "Category": "Roofs",  "UnitRate": 92000,  "Currency": "UGX", "Unit": "m2", "Note": "[FILL: KUT roof covering]" }
]
```
Rates above are placeholders — `[FILL: KUT rate card from the QS.]` For per-element precision use the priority-100 route: `CST_RATE_SOURCE = "Override"` and `CST_UNIT_RATE_UGX` on the element.

### What the library already has for you (Uganda-tuned)

Blockwork (`HOLLOW CONCRETE BLOCK 4IN…10IN`, solids, `AAC`, `INTERLOCKING`, 400×200×200 *"MOST COMMON SIZE IN UGANDA"*); screed (`STANDARD CEMENT SCREED 50MM` 1:4, `HEAVY DUTY 75MM` 1:3, `GRANOLITHIC 40MM`); render (`CEMENT SAND RENDER 1-2`…`1-6`); roofing (`IRON SHEET 26/28/30 GAUGE`, `BOX PROFILE`, `LONGSPAN`, `ZINCALUME`); terrazzo; tiles, T&G timber ceilings, emulsion and weatherguard paints; manufacturer `HIMA CEMENT`, standards `UNBS 822-1` / `US 28-2001`.

### Two behaviours to know

- **Material waste never reaches the price.** All three cost call sites pass `material = null`, so only the *category* keyword is tried. A tiled floor is carbon-counted at 10 % waste and priced at 5 %. Build the difference into your rate.
- **A rate miss is silent.** An unresolved material falls to a flat category rate — whatever the construction. Run `BOQ_RateGapReport` and read the provenance column.

---

## Part 4 — What makes a BOQ line correct

A bill line is correct when **six** things are true. Any one missing and the line is wrong, or silently absent.

1. **The element exists and is the right category.** A wall modelled as a generic model does not appear in a wall bill.
2. **The type is named as the description reads.** `Basic Wall 1` produces `Basic Wall 1` in your bill.
3. **The material is assigned** — on the layer, not just as a graphic override. Material drives the take-off, the carbon factor and the rate lookup.
4. **The classification is on the Type** (Uniclass `Ss`/`Pr`), so the line lands in the right bill section.
5. **The STING asset tag is complete** — all tokens resolved.
6. **A rate resolves** — from the rate card, the material library, or an explicit override. A line with no rate is a hole in the price.

### ⚠ Do not let formula-derived parameters into this bill

Step 7 of the tag pipeline evaluates formulas and writes results onto elements. **Most of the ones that matter for a BOQ are broken, and every one fails by writing a zero.** From the take-off findings review:

- **`lookup()` is not implemented.** ~27 formulas call it; it resolves to 0 *and discards the rest of the expression.* That is all cement, sand, aggregate and water take-off, all block and brick counts, all paint/putty litres, tile adhesive, grout and plaster volume.
- **Quoted literals are stripped by the CSV reader**, so `A + "-" + B` produces `AB`, and a string comparison against a literal silently evaluates true.
- **Unit conversion is applied by one of eight callers**, so the same parameter holds a different number depending on which command last ran.
- **There is no screed formula, no screed parameter, and no skirting length parameter at all.**
- Every failure path returns `0`, and `0` gets written.
- **Two block-count formulas disagree by ~16 %** — one uses **net** wall area with a hardcoded 3 % waste, the other **gross** area with a parameter waste. **The correct answer for a priced bill is *net area with a project-tunable waste parameter* — neither formula as written.** Blocks are not bought for the door openings, and a waste percentage should not be frozen in a formula when a wastage parameter exists. (Do not bind the second block formula while the first is live, or every wall shows two disagreeing counts.)
- **Two finishes formulas are broken on their face:** the tile-quantity formula divides by family-local `Tile_Width`/`Tile_Height` that do not resolve on a project element (divide-by-zero or silent fail), and the grout-weight formula keys on a **numeric** joint-width used as a lookup key (`3`, `3.0`, `3 mm` may not match one row). **Verify any tile or grout quantity by hand before trusting it.**
- **A binding subtlety (finding G-8):** the parameter CSV declares many `ASS_*`/`MAT_CODE` params as *Type*-bound, but the command everyone runs binds them **Instance**. Instance is correct — `ASS_TAG_1_TXT` and the identity set *must* be per-instance (a Type binding would give every door of one type the same asset tag). **Do not "fix" bindings to match the declaration.** `Element.LookupParameter` finds instance-bound params fine; if a PROD-code or system-type rate pass returns empty, the cause is almost certainly **empty values** (tagging not yet run), not the binding — re-test against genuinely tagged elements.

**What to do on KUT:**
1. **Measure geometry, not formulas.** Let takeoff rules read `HOST_AREA_COMPUTED`, `HOST_VOLUME_COMPUTED`, `Length` and solid volume directly off the elements.
2. **Ignore any `CST_S_*` / `BLE_FINISH_*` quantity parameter** you did not personally verify against a hand calculation.
3. **Screed and skirting are manual** — screed volume from your screed floors, skirting length from room perimeters. Enter both as measured additions.
4. **Spot-check for zeros** — after the tag pass, schedule the quantity parameters and sort ascending. A block of exact zeros is the signature of this failure.

**The QA gate, in order:** `PreTagAudit` → `TagAndCombine`/`BatchTag` → `ValidateTags` → `BOQPrepForExport` → `BOQ_RateGapReport` → `BOQExportProfessional` → `BOQSnapshotSave`, then `BOQSnapshotCompare` at the next issue.

`BOQPrepForExport` is the real gate, with published thresholds — compliance ≥ 80 %, container completeness ≥ 80 %, **zero stale elements**, BOQ data-quality ≥ 65, paragraph coverage ≥ 80 %, rate fill ≥ 90 %, zero critical warnings, placeholders < 5 %. Treat those as "ready to price".

### Three traps specific to this engine

**1. A failed quantity gives you a zero, not an error.** `FallbackQuantity` returns `1.0` for `each`/`item`/`nr`/`no` and **`0.0` for everything measured — m, m², m³, kg.** A wall whose area does not resolve produces a line with a description, a rate, and a quantity of zero — it looks cheap, not broken. Always sanity-check total m² of walling and roofing against a hand calculation before issuing.

**2. Parameters that must be present or the row is mis-named:** `ASS_DISCIPLINE_COD_TXT` (missing → discipline `"X"`), `ASS_PRODCT_COD_TXT` (drives the takeoff rule match → unit *and* section), `ASS_SYSTEM_TYPE_TXT` (`Category|System` rate lookup), a real assigned **Material**, Level, and `ASS_LOC_TXT` or an enclosing Room. `ASS_BOQ_LINE_REF` is **write-once** — never overwritten once set, which keeps line references stable between issues.

**3. Descriptions come from a category-keyed template library, and unfilled tokens are visible.** `BOQ_DESCRIPTIONS.json` is keyed on **category** (not section code), each with `[material]`, `[element_type]`, `[location]`, `[fixings]`, `[standard]` placeholders. Anything unresolved falls back to a generic *"Supply, deliver and install…"* sentence. Three override layers — built-in, company, and **project** (`_bim_manager\boq_custom_templates.json`). Write KUT's own descriptions into the project layer once, early.

The summary-sheet currency is **hard-coded UGX** with USD derived at `UGX_PER_USD` (default 3700, settable in `project_config.json`). Set the agreed rate before the first export.

### Excluding modelled elements from the bill

**There is no per-element BOQ opt-out in STINGTOOLS** — everything modelled in a billable category is billed. The one clean mechanism is **`COST_TAKEOFF_EXCLUDE_CATEGORIES`** in `project_config.json` (comma-separated, project-wide). For KUT, list `Entourage, Planting, Furniture, Furniture Systems, Casework, Mass, Specialty Equipment`. For a one-off element you want visible but not priced, **override the rate to zero and put the reason in the Note** — it leaves an auditable trace. **Do not model temporary works, scaffolding or props in the main model at all** — the exclusion decision belongs at modelling time (gap K-4).

### Things that quietly corrupt a take-off

- Openings and voids — confirm whether your standard deducts them and above what threshold.
- Waste factors — applied per material; agree them with the QS.
- Duplicate elements — two walls in the same place bill twice and are invisible in plan.
- In-place families — they escape most take-off rules; avoid.
- **Linked-model quantities** — confirm your schedules include linked elements, or a building will simply not be in the bill.

---

## Part 5 — Roof, and why it is a project risk

The Temple is a roof-led / architecturally driven building, and `[FILL: confirm spire/steeple design]`. Whatever its final form, the roof (and any spire) is:
- among the largest single material quantities on the building
- the thing that determines wall heights, and therefore all the wall areas
- often the last piece of the incoming information to arrive

**Do not model wall heights speculatively.** Get the roof/spire design first, or model to an explicitly stated assumed height and label every affected quantity as provisional. If you guess, every wall area, plaster area, paint area and roof area in the bill is wrong by the same unknown factor. This applies to all six buildings but bites hardest on BLD1.

---

## Part 6 — Practical notes on this specific site

- **KUT is a tight, urban, six-acre CBD site with six separate buildings.** External works — access, parking, boundary/perimeter security, drainage and reticulation between buildings — are a real package. Model retaining, steps, paths, parking and surface drainage at the same LOD as the buildings.
- **Site levels and falls:** `[FILL: confirm from the KUT topographic survey.]` Foul drainage runs downhill and water supply runs uphill — check falls, invert levels and the plant/tank locations early; they are site-planning decisions, not services details.
- **Existing site features:** `[FILL: confirm existing structures, trees, and services to retain/demolish/divert on the CBD plot — each changes the demolition bill and phasing.]`
- **You have more buildings than STING's default location vocabulary allows, and extending it takes THREE config keys, not one.** `ASS_LOC_TXT` ships with only `BLD1`–`BLD3` and `EXT`. KUT needs `BLD1`–`BLD6` and `EXT`; the overlay pack carries them. Set all three keys in `project_config.json`, with identical content, before tagging anything:

  | Key | Who honours it |
  |---|---|
  | **`LOC_CODES`** | `TagConfig.LocCodes` — the tag writer, the Excel round-trip validator (a **hard fail**, case-sensitive), the token-writer UI, the published picklists |
  | **`CUSTOM_VALID_LOC`** | `ISO19650Validator` — **the only key `ValidateTags` accepts** |
  | **`LOC_CODES_EXTRA`** | only `FederationReview` and `BuildingAwareCDEFolders` |

  Setting only `LOC_CODES_EXTRA` leaves the validator and the Excel importer rejecting your codes, and logs an "unknown config key" warning on every load (gap F-1).

- **⚠ Untagged elements are silently filed under your first building.** When LOC is empty or `XX`, STING rewrites it to the first non-`XX` LOC code. On KUT that means **every element STING cannot place lands in `BLD1`** (or whichever code is first in the list) — with no warning. The Temple will appear to cost more than it should and you will spend a day looking for the difference. Run `PreTagAudit` and check the LOC distribution before you believe any per-building cost split.
- **Do not let `BuildingCodeSeed` name your levels.** It produces `<CODE>-L01-FFL`, which the level-code parser then misreads (it strips the prefix, extracts digits, and returns the wrong code). Name levels as plain prose the parser can read (`Level 01`, `Ground`, `Roof`, `Basement 1`) and carry the building code in the model/LOC, not the level name.
- **`BuildingCodeSeed`** will generate per-building levels and grids — useful for BLD3 (multi-storey) and the Utility Building; for the single-storey ancillaries it is optional.
- **`BuildingAwareCDEFolders`** creates `<state>\<LOC>\{MODELS,DRAWINGS,SCHEDULES,BOQ,COBie,REPORTS}` per CDE state — run it after settling the LOC vocabulary for per-building issue folders in ACC.
- **Phases are audit-only** in STING (an API limitation) — create phases in Revit yourself. Demolished elements are correctly excluded from tagging and BOQ.
- **Bespoke elements need a project takeoff rule.** Anything with no matching rule falls through to the discipline default section. `[FILL: identify KUT bespoke elements — e.g. specialist Temple fit-out, water features, plant — and author project takeoff rules for them in _BIM_COORD\takeoff_rules.json before they land in the wrong bill section.]`
- **Rotation.** Each building sits at its own angle; setting-out on site will be by coordinate — another reason the survey must be in native coordinates.

---

## Part 7 — RFI list to issue now

| # | Query | Blocks |
|---|---|---|
| RFI-01 | Survey in DWG/CSV with the coordinate system and vertical datum stated | Everything. Highest priority |
| RFI-02 | Roof / spire design and construction for the Temple (and roof design for BLD2/BLD3) | BLD1/BLD2/BLD3 models, all wall/roof quantities |
| RFI-03 | Sections and elevations for each of the six buildings | Building models, all vertical quantities |
| RFI-04 | Outline / prototype specification: wall build-ups, slab, screed, roof covering, foundations | Type naming, BOQ, rates |
| RFI-05 | Finished floor level intended for each of the six building platforms | Setting out, platforms, cut/fill |
| RFI-06 | Reconciliation of the 1–40F prototype MEP references to the KUT site adaptation | MEP models, coordination |
| RFI-07 | Existing structures, trees and services on the CBD plot — retain / demolish / divert? | Demolition bill, phasing, drainage |
| RFI-08 | Which measurement standard will the QS bill to? | BOQ structure — D7 |
| RFI-09 | Consultant CAD legend and door/window/finish code schedules | Schedules, bill descriptions, code mapping |
| RFI-10 | Confirmed originator (`[ORG]`) codes for every authoring company | File naming, sheet numbering |

`[FILL: extend/confirm this RFI list against the actual state of the incoming KUT information.]`

---

## Part 8 — Tool gaps found during review

Logged here, not fixed. These are STINGTOOLS facts, project-agnostic.

1. **No East African / AAQS Standard Method of Measurement.** STING implements NRM2, CESMM4, POMI, ICMS 3 and MMHW. For East African work the regional standard is the *SMM of Building Works for Eastern Africa (2nd ed., 2008)* / the AAQS African SMM. POMI is a workable stand-in, but a native `SmmEaStandard` would be a genuine differentiator for STING's home market.
2. **`CLAUDE.md` names BOQ command tags that do not exist** (`BOQ_RateAudit`, `BOQ_Validate`, `BOQ_DeltaReport`). The real dispatch table has `BOQRefresh`, `BOQExport`, `BOQExportProfessional`, `BOQ_RateGapReport`, `BOQSnapshotSave`/`Compare`, `BOQPrepForExport`, `BOQAddManualRow`, etc. Anyone following the documentation looks for buttons that are not there.
3. **`TakeoffRule.FallbackQuantity` returns 0 for every measured unit.** A silent zero on a measured line is worse than a loud failure — the row still carries a description and a rate. It should log a warning and mark the row's confidence so `BOQPrepForExport` can gate on it.
4. **Two Uniclass parameter sets that never meet.** `UniclassClassify` writes `ASS_CLASS_COD_TXT`/`ASS_CLASS_DESC_TXT` from a 21-entry hard-coded map, but the canonical reader used by BOQ/COBie/handover/IFC reads `UNICLASS_PR_TXT`/`UNICLASS_SS_TXT`/`UNICLASS_EF_TXT`. The automatic command does not populate the parameters the reader consumes.
5. **Documentation drift.** The project rate card is `_BIM_COORD/rate_card.json` (not `boq_rate_card.json`); the rate priority chain is the reverse of what the docs state; `BOQ_DESCRIPTIONS.json` is keyed by **category**, not section code.
6. **`MultiBuilding_*` command tags do not exist.** The real tags are `BuildingCodeSeed`, `PrjVolumeCodeAuto`, `SeqRangeValidation`, `BuildingAwareCDEFolders`, `FederationReview`.
7. **No topography or site-modelling command.** Cut/fill and platform setting-out are hand-rolled Revit schedules; there is no STING command to stamp platform cut/fill onto a BOQ row.
8. **The project folder follows the open file, not the central** (Part 1C.6). `ProjectFolderEngine.GetRootPath`, the ES root stamp and `TagConfig.LocateProjectConfig` all resolve from `doc.PathName`, which in a workshared model is the user's local copy; nothing in the folder engine reads `GetWorksharingCentralModelPath`. Each workstation gets its own `_BIM_COORD`, and an owner pack placed beside the central is not read from a local. Behaviour on a cloud model is untested.
9. **INTEROP ▸ LAN COLLABORATION ▸ "Auto-Sync" never synchronises.** `LANCollaborationEngine.StartAutoSync` starts a timer whose handler only logs; the Team dashboard still reports it as Running.
10. **Two workset schemes, and the KUT labels disagree with both.** `CreateWorksets` makes 36 discipline worksets (labelled "35" in its tooltip, the workflow step and the readiness card) in every model regardless of discipline; `LANEnableWorksharing` makes a different 10. The KUT Mobilisation step is labelled "Per-building worksets (BLD1..BLD6 / EXT prefix)" but nothing creates prefixed worksets, and the corporate `workset-building-prefix` rule (enabled on `main`) warns on every workset `CreateWorksets` makes.
11. **INTEROP ▸ LAN COLLABORATION ▸ "Sync" writes its own `<central>.rvt.lock`** beside the central and refuses while it exists; a crash leaves a stale lock to delete by hand.

---

## Part 9 — The team operating guide: who does what, when, and where the button is

Every role, every rhythm, one table each. The *what* and *when* follow the managing playbook (§4 RACI, §10 stages, §12 rhythm, §13 gates); this part adds the *how* — the click path.

### 9.0 How to read a button location

`STING Panel ▸ BIM ▸ LOD VERIFICATION ▸ "LOD Verify"` (`LOD_Verify`) means: open the STING Panel → click the **BIM** tab → find the **LOD VERIFICATION** heading → click the button labelled **LOD Verify**. The code in brackets is the command tag — quote it when you report a problem.

- **Opening the panels:** Revit ribbon ▸ **STING Tools** tab ▸ **STING Panels** group ▸ **STING Panel** (main), **STING Electrical**, **STING Plumbing**, **STING HVAC**.
- **Main panel tabs**, left to right: SELECT · TAGGING · DOCS · SETUP · CREATE TAGS · MODEL · BIM · TAG STUDIO · INTEROP · HEALTHCARE. A heading in CAPITALS is a section on the tab; *italic* is a collapsible expander (click it to open).
- **Two windows open from the BIM tab:** the **BIM Coordination Center** (BIM ▸ **"★ OPEN COORDINATION CENTER"**, `BIMCoordinationCenter` — tabs OVERVIEW · MODEL HEALTH · WARNINGS · ISSUES · REVISIONS · PLATFORM · WORKFLOWS · QA DASHBOARD · 4D/5D · DELIVERABLES · MEETINGS · PROJECT MEMBERS · COORD LOG …) and the **Document Management Center** (BIM ▸ DOCUMENT MANAGEMENT CENTER ▸ **"Document Manager"**, `DocumentManager` — tabs FILE / BULK · FOLDERS · DOCS / CDE · ISSUES · REVISIONS · COORDINATION · HANDOVER · NOTES / BEP · MEETINGS).
- **Workflow presets** (KUT Mobilisation, KUT Coordination Cycle, KUT Gate Audit, KUT Deliverable A–D, KUT Monthly Report, KUT FF&E Sync): STING Panel ▸ **SETUP** ▸ WORKFLOW AUTOMATION ▸ **"Run"** (`RunWorkflow`) → pick the preset by name (the picker pages in threes — use *More…*).
- Every location below was read from the panel XAML and its tag checked against the dispatcher on 2026-10-04. CLAUDE.md's tab lists are out of date — trust this part, and the panel itself, over it.

### 9.1 Mobilisation — Day 0 to end of Month 1 (once)

| # | Who | What | How |
|---|---|---|---|
| 1 | Information Manager (IM) | Take OD-1 … OD-7 (Part 1C.8) and write them into the BEP | — no button |
| 2 | Each firm's IT + IM | Server share, nightly backup, UNC path, VPN, fixed local-file folder on every PC (1C.3) | — no button (Windows / Revit Options) |
| 3 | IM | Issue one STING build to every seat that runs it | — no button |
| 4 | ACC admin + Owner account holder | Create the ACC project, folders for WIP / SHARED / PUBLISHED / ARCHIVED per firm, member permissions; create the APS app (Traditional Web App, callback `http://localhost:8910/callback`) — `docs/KUT_LIVE_VERIFICATION_RUNBOOK.md` B1 | — no button (ACC web + APS portal) |
| 5 | IM | Connect STING to ACC: BIM Coordination Center ▸ **PLATFORM** ▸ **ACC** card ▸ Client ID / Secret / Project ID → **"💾 Save Credentials"** → **"🔓 Sign in with Autodesk"** → **"🔌 Test / Refresh Token"**; if the project id is unknown, **"🔎 Find my ACC project"** | BIM ▸ **"★ OPEN COORDINATION CENTER"** (`BIMCoordinationCenter`) |
| 6 | Architect (site model owner) | Site model: survey, Survey Point, Project Base Point (D3) | — no button (Revit) |
| 7 | Every model owner | New central from the issued template; Project Information checklist (D1) **in the central**, plus **Building Name** = `BLDn` / `EXT` (1C.4); save → close → reopen | — no button (Manage ▸ Project Information) |
| 8 | Every model owner | Bind parameters | STING Panel ▸ **CREATE TAGS** ▸ ⚙ SETUP ▸ **"Load Params"** (`LoadSharedParams`) |
| 9 | Every model owner | Enable worksharing; create worksets; delete the other disciplines' (1C.4) | Collaborate ▸ Collaborate (Revit), then STING Panel ▸ **SETUP** ▸ VIEW TEMPLATES ▸ **"Worksets"** (`CreateWorksets`), then Collaborate ▸ Worksets |
| 10 | Every model owner | View filters | STING Panel ▸ **SETUP** ▸ VIEW TEMPLATES ▸ **"Filters"** (`CreateFilters`) |
| 11 | Every model owner | Acquire coordinates from the site model; link per 1C.5; Copy/Monitor levels and grids | — no button (Manage ▸ Project Location; Insert ▸ Link Revit; Collaborate ▸ Copy/Monitor) |
| 12 | IM | Coordination root on the server (1C.6 step 1): open the federated model in `KUT\Coordination\` → choose **CdeFirst** | STING Panel ▸ **BIM** ▸ *Folder + setup ops* ▸ **"⚙ Setup"** (`CreateFolders`) |
| 13 | IM | Copy the KUT pack into that root's `_BIM_COORD`; copy `project_config.json` into every PC's local-file folder (1C.6 step 2) | — no button (file copy) |
| 14 | Every modeller | Prove the config in force | STING Panel ▸ **CREATE TAGS** ▸ *Advanced setup, schema & migration* ▸ **"Project Cfg"** (`ConfigEditor`) |
| 15 | IM | Per-building CDE folders, once the LOC list is settled | STING Panel ▸ **MODEL** ▸ *Multi-building site (§B1-B6)* ▸ **"CDE Folders"** (`BuildingAwareCDEFolders`) |
| 16 | IM | Volume code from the file name, per model | STING Panel ▸ **MODEL** ▸ *Multi-building site (§B1-B6)* ▸ **"Set Volume Code"** (`PrjVolumeCodeAuto`) |
| 17 | IM | Run the **KUT Mobilisation** preset on the federation host (its worksets step will skip there — the federated model is not workshared; that is correct) | SETUP ▸ WORKFLOW AUTOMATION ▸ **"Run"** (`RunWorkflow`) |
| 18 | IM | Day-0 worksharing proof (1C.6 step 4) and first live ACC pull with a model set that has clashes | BIM ▸ COORDINATION CHECKS ▸ **"ACC Pull"** (`ACC_PullClashes`) |
| 19 | Each task team | Test model — one element, named, tagged, in the right workset — shared once through ACC (managing playbook §15.2) | tagging buttons in 9.2 |

### 9.2 Every day — every modeller, every discipline

| When | What | How |
|---|---|---|
| Start | Fresh local, open only your worksets | 1C.3 — Revit (no STING button) |
| As you model | Rooms placed and named (`BLDn-nn Name`); MEP connected into systems; correct active workset | Revit |
| After a block of new elements | Dry-run what tagging would write | STING Panel ▸ **TAGGING** ▸ DATA TAGGING (ISO 19650) ▸ **"Pre-Audit"** (`PreTagAudit`) |
| Then | Tag the active view | TAGGING ▸ DATA TAGGING (ISO 19650) ▸ **"Auto Tag"** (`AutoTag`) — or **"Tag+Combine"** (`TagAndCombine`) to write the containers too |
| Fix a single token by hand | DISC / LOC / ZONE … | **CREATE TAGS** ▸ *Manual token overrides (13 tokens + secondary ops)* ▸ **"DISC"** (`SetDisc`), **"LOC"** (`SetLoc`) … |
| Before every sync | Validate your tags | **CREATE TAGS** ▸ ⚙ QUALITY ASSURANCE ▸ **"Valid"** (`ValidateTags`) |
| Every 1–2 h and at end of day | Synchronize with Central, relinquish | Revit Collaborate ▸ Synchronize (**not** the STING LAN buttons — 1C.6) |

### 9.3 Every week — Task Team Managers and the BIM coordinator

| What | How |
|---|---|
| Audit + compact each central (out of hours) | Revit (1C.3) — no STING button |
| Project-wide tag pass (when others have relinquished — 1C.6) | TAGGING ▸ DATA TAGGING (ISO 19650) ▸ **"Batch Tag"** (`BatchTag`) |
| Token confidence — silent defaults, elements filed under `BLD1` | CREATE TAGS ▸ ⚙ QUALITY ASSURANCE ▸ **"Token Conf"** (`TokenConfidenceAudit`) |
| Completeness by discipline | CREATE TAGS ▸ ⚙ QUALITY ASSURANCE ▸ **"Complete %"** (`CompletenessDashboard`) |
| Duplicate SEQ repair, if Valid reports any | TAG STUDIO ▸ Tools ▸ *Advanced placement ops* ▸ **"Repair dupes"** (`RepairDuplicateSeq`) |
| Worksets and links audit | BIM ▸ WORKSET and LINK AUDIT ▸ **"WS Audit"** (`WorksetAudit`), **"Link Audit"** (`LinkAudit`), **"Link Stats"** (`LinkStats`) |
| Warnings | BIM ▸ ⚠ WARNINGS MANAGER ▸ **"⚠ WARNINGS DASHBOARD"** (`WarningsDashboard`) |
| Model health | BIM ▸ MODEL HEALTH & COMPLIANCE ▸ **"Health"** (`ModelHealthDashboard`) |
| Purge unused before the share | Revit Manage ▸ Purge Unused — no STING button |

### 9.4 Every fortnight — the coordination cycle (IM, on the coordination workstation)

Run the **KUT Coordination Cycle** preset (SETUP ▸ WORKFLOW AUTOMATION ▸ **"Run"**), or the steps by hand:

| Step | Who | How |
|---|---|---|
| 1 | Each Task Team Manager | Pre-share checklist (managing playbook App. A1); publish the model to its ACC **SHARED** folder at the agreed suitability | ACC / Revit publish — no STING button for the model itself; **BIM** ▸ COORDINATION CHECKS ▸ **"ACC Upload"** (`ACC_UploadModel`) uploads a chosen file |
| 2 | IM | Download the SHARED copies into `Incoming\`; reload links in the federated model | — no button (ACC + Manage Links) |
| 3 | IM | Federate and clash in Navisworks / ACC Model Coordination | — no STING button |
| 4 | IM | Pull + triage ACC clashes; reconcile closed issues | BIM ▸ COORDINATION CHECKS ▸ **"ACC Pull"** (`ACC_PullClashes`), **"ACC Sync"** (`ACC_SyncIssueStatus`) |
| 5 | IM | STING rule-based clash → BCF | BIM ▸ COORDINATION CHECKS ▸ **"Run"** (`ClashRun`) → **"BCF"** (`ClashBcfExport`) |
| 6 | IM | Cross-building review of links | MODEL ▸ *Multi-building site (§B1-B6)* ▸ **"Federation Review"** (`FederationReview`) |
| 7 | IM | Package the coordination bundle; upload it | Document Manager ▸ **HANDOVER** ▸ PUBLISH ▸ **"ACC Publish"** (`ACCPublish` — builds a **local** bundle only), then BIM ▸ COORDINATION CHECKS ▸ **"ACC Upload Bundle"** (`ACC_UploadLastBundle`) |
| 8 | IM | Issues: track and assign | BIM ▸ ISSUE / RFI TRACKER ▸ **"★ Issue Dashboard"** (`IssueTrackerDashboard`); BIM Coordination Center ▸ **ISSUES** |
| 9 | Lead AP chairs; IM runs | Coordination meeting (Teams) | Document Manager ▸ **MEETINGS**; BIM Coordination Center ▸ **MEETINGS** |
| 10 | Each discipline | Resolve its clashes in its own model; close the ACC issue | Revit — no STING button |

### 9.5 Per issue — the document controller

| What | How |
|---|---|
| Produce sheets from drawing types | SETUP ▸ DRAWING PRODUCTION ▸ **"★ Set up drawing production"** (`DrawingTypes_SetupProduction`) once per model; then DOCS ▸ 📐 DRAWING TYPES, *Production* row ▸ **"Produce Per Level"** (`DrawingTypes_ProducePerLevel`) / **"Produce From Scope Boxes"** (`DrawingTypes_ProduceFromScopeBoxes`) |
| Manage sheets and numbering | DOCS ▸ SHEET MANAGER ▸ **"Sheet Manager"** (`SheetManager`) |
| ISO 19650 sheet check before issue | DOCS ▸ *Advanced layout & batch ops* ▸ **"ISO Check"** (`SheetComplianceCheck`) |
| Revision | BIM ▸ REVISION MANAGEMENT ▸ **"Create"** (`CreateRevision`) |
| Export PDFs / DWG / IFC filed by CDE state | DOCS ▸ EXPORT CENTER ▸ **"★ Export Center"** (`ExportCenter`) |
| Set CDE state / suitability | BIM ▸ CDE — DOCUMENT CONTROL ▸ **"Set CDE Status"** (`CDEStatus`) |
| Sheet register | DOCS ▸ *Advanced layout & batch ops* ▸ **"Register"** (`ExportSheetRegister`) |
| Transmittal (rendered + workflow + audit) | BIM ▸ TRANSMITTAL (orchestrated) ▸ **"New Transmittal"** (`CreateTransmittalOrchestrated`) |
| The formal ACC transmittal and the move to PUBLISHED | ACC web — no STING button |

### 9.6 Per gate — Deliverables A, B, C, the conformed set, D (IM)

| Step | How |
|---|---|
| 1. Read-only pre-check | **KUT Gate Audit** preset (SETUP ▸ WORKFLOW AUTOMATION ▸ **"Run"**) — or by hand: BIM ▸ LOD VERIFICATION ▸ **"Owner Standards"** (`OwnerStandards_Audit`), **"Program Audit"** (`Program_Audit`), **"LOD Verify"** (`LOD_Verify`) |
| 2. Classification and specification (from Deliverable B) | BIM ▸ CSI / SPECLINK ▸ **"CSI Assign"** (`CSI_Assign`) **then** **"SpecLink Reconcile"** (`SpecLink_Reconcile`) — in that order |
| 3. FF&E and finishes | BIM ▸ FOHLIO FF&E ▸ **"Fohlio Export"** / **"Fohlio Import"** / **"Finishes Export"** / **"Finishes Import"** / **"Fohlio Audit"** |
| 4. Review comments (Bluebeam) | BIM ▸ REVIEW COMMENTS (BLUEBEAM) ▸ **"Import"** (`ReviewComments_Import`) |
| 5. The gate preset itself | **KUT Deliverable A / B / C / D** (SETUP ▸ WORKFLOW AUTOMATION ▸ **"Run"**) |
| 6. Stamp passing elements | BIM ▸ LOD VERIFICATION ▸ **"LOD Stamp"** (`LOD_Stamp`) |
| 7. BOQ for the QS | BIM ▸ 5D — COST ESTIMATION ▸ **"✓ Prep Export"** (`BOQPrepForExport`) → **"★ Tender BOQ"** (`BOQExportProfessional`); issue-to-issue difference: BIM ▸ *BOQ + 5D ops* ▸ **"Snap Diff"** (`BOQSnapshotCompare`) |
| 8. Monthly status (every month, not only at gates) | **KUT Monthly Report** preset, or BIM ▸ OWNER KPI DASHBOARD ▸ **"KPI Dashboard"** (`Owner_KpiDashboard`) |

### 9.7 Construction to handover (Stage 3.1 – 3.3)

| What | Who | How |
|---|---|---|
| BMS point list for the controls contractor; reconcile against the station export | IM + MEP | BIM ▸ BMS (NIAGARA) ▸ **"Export Points"** (`Niagara_ExportPoints`), **"Reconcile"** (`Niagara_Reconcile`) |
| Lifecycle join (model ↔ BMS ↔ ACC) | IM | BIM ▸ LIFECYCLE JOIN ▸ **"Lifecycle Reconcile"** (`KUT_LifecycleReconcile`), **"Push Gaps to ACC"** (`KUT_PushLifecycleGapsToAcc`) |
| LOD 500 verification | IM | **KUT Deliverable D** preset (it refreshes the Fohlio link first) |
| COBie — only if the Owner's EIR requires it | IM | BIM ▸ COBie V2.4 — FM HANDOVER ▸ **"COBie Export"** (`COBieExport`), **"FM Handover"** (`HandoverManual`) |
| As-built capture on site | Contractor | — no STING button |

### 9.8 Role cards — what is yours

**Information Manager / BIM Manager** — owns 9.1, 9.4, 9.6, 9.7; the coordination workstation and its root (1C.6); the STING build; OD-1 … OD-7. Coordinates and verifies — does **not** author design.

**BIM coordinator (per firm)** — owns 9.3 for their firm's models: audit/compact, link reloads, batch tag pass, the weekly QA buttons, and first-line help for the modellers.

**Architect** (`A`, volumes `00`–`06`) — owns levels, grids and rooms of each building and the site's shared coordinates. Daily 9.2. Extra buttons: materials — SETUP ▸ *Bulk creation + PBR library* ▸ **"BLE (815)"** (`CreateBLEMaterials`); finishes (Part 3A) — MODEL ▸ COVERINGS ▸ **"★★ Smart Covering"** (`CoveringSmartApply`), **"Room Finishes"** (`CoveringRoomSchedule`); family conformance before bulk-loading vendor content — MODEL ▸ *Advanced family ops* ▸ **"Conformance"** (`FamilyConformanceCheck`).

**Structural** (`S`) — Copy/Monitors the architect's levels and grids (1C.5). Daily 9.2. STING's structural creators and checks are under MODEL ▸ STRUCTURAL AUTOMATION (**"★★★ Diagnostics"**, `StrDiagnostics`, is the read-only one) and the MODEL ▸ *Structural analysis* expander. **Structural design and analysis remain the engineer's, in their own analysis software** — STING's checks are model QA, not a design.

**Mechanical** (`M`) — STING Electrical/HVAC/Plumbing centres are **optional authoring aids**, not part of the KUT information-management scope (`project-templates/KUT/README.md`). If used: STING HVAC ▸ **SYS** ▸ **"Build system"** (`Hvac_SystemAudit`), **EQPT** ▸ **"Connect"** (`Hvac_ConnectionAudit`), **CALCS** ▸ **"Auto-size"** (`Hvac_AutoSizeDuct`) and **"Run all validators"** (`Hvac_RunAllValidators`), **SYS** ▸ **"Auto-FD"** (`Hvac_AutoFireDamper`). The one obligation that is not optional: **every element connected into a real system** before the share.

**Electrical** (`E`, including low voltage / ICT in `E-ELEC-LowVoltage`) — if used: STING Electrical ▸ **PNLS** (panel schedules, top button row), **CIRCTS** ▸ *CIRCUIT WIZARD* ▸ **"▶ Launch Wizard…"** (`Circuit_CreateWizard`), **CALCS** ▸ *VOLTAGE DROP* ▸ **"▶ Recalculate All"** (`Calc_VoltageDrop`), **CABLE** ▸ *CABLE / CONDUCTOR SIZER* ▸ **"▶ Calculate"** (`Cable_Calculate`), **SLD** ▸ **"⚡ Generate"** (`SLD_Generate`), **RPRT** ▸ *COMPLIANCE REPORT* ▸ **"▶ Full Compliance Audit"** (`Rprt_Audit`). **The electrical engines are built on BS 7671 (NEC only where a command says so), while the Owner works to US standards** (the reason for the plumbing-code step below) — agree the standard with the Owner before relying on any STING number.

**Plumbing / public health** (`P`) — **first set Project Information ▸ `PLM_PRJ_PLUMBING_CODE_TXT` = `IPC-US`** (`project-templates/KUT/README.md` §5 step 3 — blank silently means BS EN 12056). If used: STING Plumbing ▸ **SUPPLY** ▸ FIXTURE UNIT SCAN ▸ **"Scan Fixtures (DU / LU / WSFU)"** (`Plumb_ScanFixtures`), **SUPPLY** ▸ **"▶ Size DCW / DHW pipes"** (`Plumb_SizeSupply`), **DRAINAGE** ▸ **"▶ Auto-Size Drainage"** (`Plumbing_AutoSizeDrainage`), **ROUTE** ▸ PENETRATIONS / SLEEVES & HANGERS ▸ **"Place Sleeves"** (`Plumb_PlaceSleeves`), **AUDIT** ▸ **"▶ Run Full Audit"** (`Plumb_FullAudit`).

**Fire** (`Y`) — worksets `FP-FPRT-*`. If used: STING Plumbing ▸ **SPECIALTY** ▸ FIRE SPRINKLERS ▸ **"Sprinkler Hydraulics"** (`Fire_SprinklerHydraulics`); STING Electrical ▸ **SLD** ▸ *SCHEMATICS* ▸ **"▶ Fire Alarm Schematic"** (`FireAlarm_Schematic`); fire dampers from STING HVAC ▸ **SYS** ▸ **"Auto-FD"**.

**Document controller** — owns 9.5; the register and transmittals; nothing leaves `02_PUBLISHED` without a transmittal (Stage J).

**ACC admin** — owns 9.1 step 4: project, folders per state per firm, permissions (ISO 19650-5: access by need), the APS app, and the Model Coordination model set the cycle pulls. STING has **no** button for any of this; it only reads what the admin set up.

### 9.9 Steps with no STING button

Worksharing itself (enable, Create New Local, synchronise, relinquish, borrow, audit/compact — Revit **Collaborate** tab); Acquire / Publish Coordinates; linking, Copy/Monitor, Coordination Review; Purge Unused; Project Information values including **Building Name**, `PRJ_ORG_*` and `PLM_PRJ_PLUMBING_CODE_TXT`; Revit Options file locations; copying the owner pack; the server, VPN and backups; every ACC administration task and the APS app; publishing a model into an ACC folder and the formal ACC transmittal / state change; Navisworks federation and clash tests; resolving a clash; as-built capture.

---

## Appendix — verified command tags (read from source, not from documentation)

> Tags below are **read from the plugin source**, not from any documentation file — where the two disagree, the source wins (see gaps 2, 5, 6).

| Stage | Tag | Class |
|---|---|---|
| Project setup | `ProjectSetup` | `Temp.ProjectSetupCommand` |
| Folder consolidation | `Folders_ConsolidateAll` | `Commands.Folders.FolderConsolidateCommand` |
| Bind shared parameters | `LoadSharedParams` | `Tags.LoadSharedParamsCommand` |
| Dry-run tag audit | `PreTagAudit` | `Tags.PreTagAuditCommand` |
| Tag + combine | `TagAndCombine` | `Tags.TagAndCombineCommand` |
| Project-wide tag | `BatchTag` | `Tags.BatchTagCommand` |
| Validate tags | `ValidateTags` | `Tags.ValidateTagsCommand` |
| Set measurement standard | `Cost_SetMeasurementStandard` | `Commands.Cost.CostSetMeasurementStandardCommand` |
| Classification | `CSI_Assign` | `Commands.Classification.CsiAssignCommand` |
| BOQ refresh / export | `BOQRefresh`, `BOQExport`, `BOQExportProfessional` | `BOQ.*` |
| Rate gaps | `BOQ_RateGapReport` | `BOQ.BOQRateGapReportCommand` |
| Issue-to-issue delta | `BOQSnapshotSave` then `BOQSnapshotCompare` | `BOQ.*` |
| Manual measured additions | `BOQAddManualRow`, `ReconcileProvisionals` | `BOQ.*` |
| LOD check | `LODValidation` | `Core.LODValidationCommand` |
| Create materials from the library | `CreateBLEMaterials`, `CreateMEPMaterials` | `Temp.Create*MaterialsCommand` |
| Finishes / plaster / paint | `CoveringSmartApply`, `CoveringBatchApply`, `CoveringRoomSchedule` (+8 in the expander) | `Model.Covering*Command` |
| Choose folder mode (only place you can) | `CreateFolders` | `UI.ProjectFolderSetupDialog` |
| Per-building CDE folders | `BuildingAwareCDEFolders` | `Core.BuildingAwareCDEFoldersCommand` |
| Seed per-building levels + grids | `BuildingCodeSeed` | `Core.BuildingCodeSeedCommand` |
| Volume code from filename | `PrjVolumeCodeAuto` | writes `PRJ_VOLUME_CODE` |
| Federation review across links | `FederationReview` | `FederationCoordinationReviewCommand` |
| Scope-box dry run / generate | `DrawingTypes_SuggestFromScopeBoxes`, `DrawingTypes_FromScopeBoxes` | `Commands.Drawing.*` |
| Uniclass / CSI | `UniclassClassify`, `CSI_Assign` | see gap 4 |
| Reload takeoff + measurement rules | `Cost_ReloadRules` | `Commands.Cost.*` |
| Worksets (36, all disciplines — Part 1C.4) | `CreateWorksets` | `Temp.CreateWorksetsCommand` |
| Workset / link audit | `WorksetAudit`, `LinkAudit`, `LinkStats` | `BIMManager.*` |
| Which `project_config.json` is in force | `ConfigEditor` ("Project Cfg") | `Tags.ConfigEditorCommand` |
| Run a workflow preset (KUT …) | `RunWorkflow` | `Core.WorkflowPresetCommand` |
| ACC clash pull / issue reconcile | `ACC_PullClashes`, `ACC_SyncIssueStatus` | `Core.Clash.AccPullClashesCommand`, `Core.Clash.AccSyncIssueStatusCommand` |

### Files you author by hand for this project

| Path | Purpose |
|---|---|
| `project_config.json` (beside the `.rvt`) | `LOC_CODES` / `CUSTOM_VALID_LOC` / `LOC_CODES_EXTRA` for the six building codes, `ZONE_CODES`/`CUSTOM_VALID_ZONE`, `UGX_PER_USD`, `FOLDER_CODE_SUFFIX`, `COST_TAKEOFF_EXCLUDE_CATEGORIES` |
| `<project>\_BIM_COORD\takeoff_rules.json` | project takeoff rules — **prepended** over corporate, needed for any bespoke element |
| `<project>\_BIM_COORD\rate_card.json` | the project rate card (**not** `boq_rate_card.json`) |
| `<project>\_bim_manager\boq_custom_templates.json` | project bill descriptions |
| `<project>\_BIM_COORD\drawing_types.json` | project drawing-type overrides (one variant per building for `{vol}`) |
| `<project>\_BIM_COORD\carbon_factors_ug.json` | project carbon factor overrides (do not edit the shipped file) |

---

*Internal working guide. The authoritative controlled standards for KUT are the versions in ACC (numbering convention, document control, BEP). Fields marked `[FILL]` / `[ORG]` are completed at mobilisation or when the incoming information is confirmed.*
