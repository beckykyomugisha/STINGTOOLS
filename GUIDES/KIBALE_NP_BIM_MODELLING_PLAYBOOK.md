# Kibale NP Lodge — BIM Modelling & Documentation Playbook

> **Project:** Kibale National Park lodge, Kabaale — client file `D:\Work 2026\Tayebwa 2026\KABAALE NP\Kibale NP.pdf`
> **Author:** Planscape Consulting Engineers Ltd
> **Purpose:** the decisions to take *before* the first wall is drawn, the order to model in, and the data discipline that makes the BOQ come out accurate and correctly named.
> **Status:** advisory. No code changes. Tool gaps found during the review are listed in Part 8.

---

## Part 0 — What the incoming information actually is

Read directly out of the PDF, not assumed:

| Fact | Value |
|---|---|
| Sheets | 3, each 3370 × 2384 pt = **A0 landscape** |
| Stated scale | **1:300 (A3 paper)** — i.e. the sheet was set up for A3 and is being issued at A0. Scale note and sheet size disagree; confirm before anyone measures off it |
| Sheet 1 | Site layout — all buildings, boundary, contours |
| Sheets 2–3 | Topographic survey — 0.5 m contour interval, spot levels, point table |
| Survey points | **159 levelled points** |
| Level range | **1471.521 → 1499.268** → **27.75 m of fall** across the site |
| Boundary | Irregular 9-sided polygon, roughly NE–SW |
| Existing on site | One hatched structure (pink) mid-site, plus surveyed trees — `mango1-4`, `ovacado1-4`, `tree1-4`, `pltn` (plantation), `bd1-6`, `garden1-9`, `house1-4`, and a `road` chain of 21 points |

### The building programme, as drawn

| # | Building | Evidence from the drawing |
|---|---|---|
| 7 | **Typical cottage** (round pavilion) | `R5795` → **Ø 11.59 m** circular footprint. Radial grid **A–G / 1–6**. Contains *Executive Room*, *Lounge*, *Study Area*, two en-suites (`whb`, `wc`, `sh`, `D5`), `duct` risers, `luggage rack`, `tv`, `pv`. Radial geometry set out at **22°** and **45°** |
| 1 | **Twin cottage** | Two of the above mirrored about a shared spine: *Twin Bedroom* + *Deluxe Room* on one side, *Executive Room* + *Lounge* + *Study Area* on the other. Same `R5795`, same A–G/1–6 grid, `bt` (bath) added |
| 1 | **Staff / workers' lodge** | Linear block, rotated. **10 × `ROOM`** on a **3600 mm** module, `W1/pvo` windows, `D4/pvo` doors, shared **`janitor`**, `wc`, `sh.`, `whb` cores at each end with `W2/pvo` / `W3/pvo`. Internal dims read 3600 / 2700 / 1500 / 1200 / 1090 / 1910 / 3000 / 10800 / 2555 |
| 1 | **Reception** | Small block, hard against the kitchen/dining |
| 1 | **Kitchen & Dining** | Largest rectangular block, NE of site, with a **`back space`** service yard behind |
| 1 | **Laundry cage** | Adjacent to the staff block |
| 1 | **Swimming pool** | Rectangular, between kitchen/dining and the cottages |
| 1 | **Camp fire** | Circular, on a paved/`brick` terrace |
| — | External works | Access road chain, paths, boundary, retaining to suit the 27.75 m fall |

### Glossary — the abbreviations used throughout

| Term | Means |
|---|---|
| **mAOD** | **metres Above Ordnance Datum** — height above a national sea-level datum. `1487.500 mAOD` is 1,487.5 m above that zero. It is the surveyor's absolute height, as against a *project* level like `+0.000 FFL` which is relative to your own datum. Uganda uses its own national datum rather than the British Ordnance Datum, so strictly this is "metres above datum" — but mAOD is the term the drawings will use and everyone will understand. **Always state which datum on the drawing.** |
| **FFL** | Finished Floor Level — the top of the finish you walk on |
| **SSL** | Structural Slab Level — the top of the structural slab, under the screed and finish |
| **SOP** | Setting-Out Point — the one permanent point a building is set out from |
| **LOD** | Level of Detail / Development — how much is modelled and how reliably |
| **CDE** | Common Data Environment — the WIP → SHARED → PUBLISHED folder structure |
| **NRM2 / SMM** | Rules for how a bill of quantities is measured and described |
| **Qto / Pset** | IFC quantity set / property set — how quantities and data travel to a cost tool |

### Codes established by the architect — **map them into our standard, do not adopt them**

The CAD carries its own nomenclature — `D2/pvo`, `W1/pvo`, `D5`, `timber panquette ff`. **We enforce our naming standard; we do not bend it to the incoming drawing.**

> **Correction to my earlier advice.** I first suggested adopting the architect's codes verbatim as Revit type names. That was wrong for a project you intend to use as the template for future work. A supplier code like `/pvo` carries no meaning outside this one job, `panquette` is a misspelling of *parquet*, and a type library named after one architect's habits cannot be reused. The architect's codes are **input data**, not a standard.

**What to do instead: keep both, in different fields.**

| Field | Holds | Example |
|---|---|---|
| **Revit Type Name** | *our* standard, enforced | `DR-SGL-TIMBER-0900x2100-FD30` |
| `ASS_LEGACY_REF_TXT` (or Type Comments) | the architect's original code, preserved verbatim | `D2/pvo` |
| Door/window **Mark** | the instance number only | `COT01-D-001` |

That way the bill, the schedules and the type library all speak our language, and you can still cross-reference any line back to the incoming drawing — which is what you need at an RFI or a valuation.

**Build the mapping table once, on day one**, and issue it with the first drawing set so the architect can confirm it:

| Architect | Our type | Description |
|---|---|---|
| `D2/pvo` | `DR-SGL-TIMBER-0900x2100` | Single leaf timber door, 900 × 2100 |
| `D4/pvo` | `DR-SGL-TIMBER-0800x2100` | Single leaf timber door, 800 × 2100 |
| `D5` | `DR-SGL-TIMBER-0700x2100-WC` | En-suite door |
| `W1/pvo` | `WN-CSMT-ALU-1500x1200` | Casement window |
| `W2/pvo` | `WN-CSMT-ALU-0600x0600` | Small casement, WC |
| `W3/pvo` | `WN-CSMT-ALU-0900x0600` | Casement, shower |
| `timber panquette ff` | `FL-FIN-PARQ-020` | 20 mm timber parquet finish |
| `cem. screed ff` | `FL-SCR-CEM-050` | 50 mm cement screed, finished |

Sizes and specifications above are **placeholders pending RFI-01 and RFI-04** — the point is the shape of the name, not the numbers.

The original CAD annotations, for reference:

- **Doors:** `D2/pvo`, `D4/pvo`, `D5`
- **Windows:** `W1/pvo`, `W2/pvo`, `W3/pvo`
- **Floor finishes:** `timber panquette ff` (parquet — spelling to be corrected), `cem. screed ff`
- **Fittings:** `whb`, `wc`, `sh`, `bt`, `tv`, `luggage rack`, `pv`, `duct`

> **Query to the architect (RFI-01):** what does the `/pvo` suffix denote — *pivot*, *PVC opening*, or a supplier code? It will appear in every door and window bill line, so it must be right.

### What the drawing does *not* give you, and therefore what you must obtain before modelling

1. **Sections and elevations.** No vertical information exists in this set — no wall heights, no roof pitch, no roof construction, no floor-to-ceiling. A round pavilion at Ø11.59 m is a *roof-led* building; you cannot model it from a plan.
2. **Construction specification.** Wall build-ups, slab thicknesses, roof covering (thatch? shingle? makuti?), foundation type.
3. **The survey in native format.** You have a PDF. **Do not trace it.** Ask for the `.dwg` / `.csv` point file. Tracing a 1:300 PDF puts a 100–300 mm error into every setting-out dimension and it will follow you all the way to the earthwork quantities.
4. **A per-cottage finished floor level.** Across 27.75 m of fall, the seven "typical" cottages are only typical *above* their floor slab. Below it, every one is different.

---

## Part 1 — The ten decisions to take before opening Revit

These are cheap now and expensive later. Take them in this order and write them into the BEP.

### D1 — Project code

Fix the code **first**, before the first save. In STINGTOOLS the code is derived from **Revit Project Information → Project Number**, sanitised to ≤ 8 characters, and then **stamped into ExtensibleStorage** so the whole output tree stays put even if someone edits the number later. It is also the suffix on every folder name and the first field of every ISO 19650 file name.

**Recommendation:** `KNP26` (Kibale, 2026) or `KIBNP26`. Short, unambiguous, no spaces, no punctuation.

**The exact mechanism, from the code** (`Core/ProjectFolderEngine.cs:758-784`, `Core/Storage/StingProjectRootSchema.cs`):
- `DetectProjectCode` takes `ProjectInformation.Number`, strips everything but letters/digits/`_`/`-`, uppercases, **truncates to 8 characters**. No Number → first 3 letters of Name → `"PRJ"`.
- The root is resolved on **`DocumentOpened`**, before you touch anything, and the resolved path is written to an **ExtensibleStorage stamp** on ProjectInformation.
- **The stamp is what protects you from a later rename.** If it exists and its folder still exists, editing the Project Number changes nothing. If the stamp is missing (unsaved document, read-only, or the folder was moved), a rename **mints a brand-new sibling tree and your exports fork into it**.

**Therefore the correct opening move is:** set Project Number and Name → **save the file** → **close and reopen it**, so `DocumentOpened` runs against a saved document and the stamp lands. Do this before any other STING command. It takes thirty seconds and it removes an entire class of "where did my exports go" failure.

Once fixed, it appears in:
- the project folder name `<rvtDir>/KNP26/`
- every folder display name (`01_WIP_KNP26`, …) if `FOLDER_CODE_SUFFIX` is on
- every file name: `KNP26-PLN-COT01-ZZ-DR-A-1001`
- every ISO 19650 asset tag written by STING

#### There are TWO project-code fields, and nothing reconciles them

This is the part that bites. STING reads the project code from **two different places**, for two
different purposes, and **no code anywhere checks that they agree**.

| Field | Read by | Drives |
|---|---|---|
| `ProjectInformation.Number` | `ProjectFolderEngine.DetectProjectCode` (`:822-836`) | the **folder tree**, the ES root stamp, folder display names |
| `PRJ_ORG_PROJECT_CODE_TXT` | 9 files incl. `TitleBlockParamApplier`, `TemplateManifest`, `ServerPublisher`, `TagSchemeEngine` | **ISO 19650 document numbers**, transmittals, rendered file names |

**Set both, to the same value.** For Kibale: **`KIBALE`** — 6 characters, within the 8-character limit,
no punctuation. (This supersedes the `KNP26` / `KIBNP26` suggestion above; the decision is `KIBALE`.)

Three measured facts you need to know before relying on either:

1. **Nothing in STING writes `PRJ_ORG_PROJECT_CODE_TXT`.** All nine consumers read it; there is no
   `SetString`/`Set` anywhere. The Project Setup wizard reads `ProjectInformation.Number` but does not
   populate this field. **You must type it by hand** — Manage → Project Information.
2. **If you leave it blank, one consumer silently falls back to the Number** (`TemplateManifest.cs:198`,
   `ReadParam(ORG_PROJECT_CODE) ?? info.Number`) and the others get nothing. That is how the two drift:
   documents numbered from one field, folders from the other.
3. **`DrawingDispatcher.ReadProjectCode` (`:106`) is broken** — it looks up `PRJ_ORG_PROJECT_CODE`
   without the `_TXT` suffix, which is not a declared parameter, so it always returns null. Do not rely
   on drawing-type routing by project code until that is fixed (registered as **G-21**).

#### The Project Information checklist — do this first, in this order

Four fields carry the project code. **`ProjectInformation.Number` is the source; the other three are
derived stamps** written at setup (K-11c). Do not type them independently — a divergence between any
stamp and Number is now warned about, but the warning is a safety net, not a workflow.

| Field | Value for this project | Why |
|---|---|---|
| **Project Number** | **`KNP26`** | the source. 5 characters, inside the ≤8 limit |
| Organization Name | **`ACE`** — leave it | ACE are the architects. Planscape models and documents **for** them and issues on **ACE's** title block, so the organisation on the sheet is ACE. This is not a template leftover. |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | **`PLN`** *(pending ACE confirmation)* | A different question from the one above. The ISO 19650 **originator** is whoever authored the information container — Planscape did. Organization Name is *whose drawing this is*; originator is *who produced this file*. They are allowed to differ, and here they do. One line of written confirmation from ACE settles it. |
| Project Name | Kibale… — **`KIBALE`** spelling | not `KIBAALE` |
| `PRJ_PROJECT_COD_TXT` | `KNP26` | derived — feeds `{project}` in sheet numbers |
| `PRJ_ORG_PROJECT_CODE_TXT` | `KNP26` | derived — feeds the template engine / ISO doc numbers |
| `PRJ_NR_TXT` | `KNP26` | derived — title-block label + schedules |

**Order:** set Project Number → save → close → reopen (so the ES root stamp lands against a saved
document) → **Load Shared Parameters** → set the remaining fields in Manage → Project Information.

`KNP26` is 5 characters. The limit is 8 — `DetectProjectCode` truncates silently past that, so a
longer code would fork the folder tree from the document number without saying anything.

#### The title block's sample values are asset tags, not sheet numbers

The Sheet Number sample value and the **DWG NO.** field on the current title block both carry
**8-segment ASSET TAG** patterns (`DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`). That is the tag grammar for
a *duct or a door*, not the ISO 19650 grammar for a *drawing*, which is
`{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq}` — 7 segments.

**Recommended sample value:** `KNP26-PLN-COT01-00-DR-A-1001`

> **What feeds DWG NO. could not be traced from the repo.** No STING-authored title block declares a
> `DWG NO.` field — it appears only in `MR_SCHEDULES.csv` and `ScheduleCommands.cs`, neither of which
> writes a title-block label. The field is therefore on **your** title block, inherited with the ACE
> template. Open it in the Family Editor and read the label's parameter binding; under K-11e that is
> the only way to establish it, because a `.rfa` label cannot be read statically.

**Do not edit the .rfa to fix this** — the sample value is a family default, and changing it is an
operator step, not a code change.

#### If you see "PRJ" anywhere, stop

`PRJ` is the placeholder used when Project Information has **neither** a Number nor a Name. It is not a
valid project code. STING now warns when a tree is about to be minted under it — a log line plus one
dialog per model. If you see it:

- Do **not** carry on and rename later. The root is stamped into ExtensibleStorage on first resolve, so
  a rename gives you a **second** tree, not a moved one.
- Set the Number, save, close, reopen. Then check that `<rvtDir>/KIBALE/` exists and no `PRJ/` does.

This matters beyond tidiness: several projects opened without a Number all resolve to the **same**
`PRJ` folder and write over each other's coordination data.

### D2 — Project folder, and when to create it

**Create the folder the moment you have a code and a first `.rvt` — before modelling, not after.** Two reasons:

- STING resolves *every* output path (exports, coordination stores, BOQ, transmittals, revision registers) from the project root. If the root does not exist when the first command runs, the data lands in ad-hoc places and you spend a day consolidating later.
- The layout mode (`CdeFirst` / `BIM` / `Mini`) is **persisted at first setup** and the folder display names are baked in. Changing your mind afterwards leaves you with two parallel trees.

**Recommendation for this project:** `CdeFirst`. It is greenfield, single-originator, and the CDE states (WIP → SHARED → PUBLISHED) are what the client and the QS will actually consume.

**But the Project Setup wizard never asks you.** Mode selection is automatic (`ProjectFolderEngine.cs:380-404`): `CdeFirst` if `TagConfig.CdeFirstLayout` is true *and* the project is greenfield (no ES stamp, no existing `<projDir>\<CODE>`, no legacy sibling folders), otherwise `BIM`. The only place you get to choose is the **`CreateFolders`** command (dock tab **BIM**, "Folder + setup ops"), which opens `ProjectFolderSetupDialog` with radio buttons.

So: run **`CreateFolders`** deliberately, and confirm the mode you got, before you let anything else write. Confirm `FOLDER_CODE_SUFFIX` at the same time — it is baked into the folder display names at first setup, and the code comment is explicit: *"set it before a project's first setup, not mid-project."*

**Also: nothing auto-fills the `PRJ_ORG_*` parameters.** They are bound by `LoadSharedParams` and read all over the place (title blocks, drawing tokens, export naming), but the only writer in the whole plugin is the Ugandan-defaults command, which writes one of them. `ProjectSetup` writes exactly one — the healthcare profile. **Type `PRJ_ORG_PROJECT_CODE_TXT`, `PRJ_ORG_ORIGINATOR_CODE_TXT`, `PRJ_ORG_CLIENT_NAME_TXT`, `PRJ_ORG_COMPANY_NAME_TXT`, `PRJ_ORG_PHASE_TXT` into Project Information by hand as a mobilisation task.** Left blank, title blocks silently fall back to `"PLNS"` and the raw Project Number.

**Rules of use, once created:**
- `00_WIP` — your working models and unissued views. Nobody else reads this.
- `01_SHARED` — models issued *to the team* for coordination (suitability S1–S3).
- `02_PUBLISHED` — what goes to the client / QS / contractor (S4, A-codes).
- `_data/` — machine state only. **No deliverables ever go in here.** It holds the coordination bucket, staging, recycle, and `project_setup.json`.
- Never hand-build a path into these folders. If you find yourself typing `_BIM_COORD` into a dialog, stop — that is the symptom of a call site that should have gone through the path resolver.

### D3 — Coordinates and levels

The site sits at ~1471–1499 m. Three separate things, do not conflate them:

| Thing | Set it to |
|---|---|
| **Survey Point** | The real surveyed coordinate + true elevation (e.g. 1483.500 mAOD). This is the link to the surveyor's world. |
| **Project Base Point** | A clean round number near the main building — say **1485.000 mAOD** — so that plan dimensions and level annotations are readable. |
| **Internal origin** | Leave alone. Keep every model within ~10 km of it (trivially satisfied here). |

Then **Acquire Coordinates** from the site model into every building model. Do this **once, early**. Re-coordinating eight linked models after they are populated is a day of rework and a source of silent misplacement.

Annotate levels **twice** on drawings: project level (`+0.000 FFL`) *and* the mAOD in brackets. On a 27.75 m site, a level without a datum is a hazard.

### D4 — Federation: links, not groups

You have seven identical cottages at seven different elevations and seven different rotations, plus four one-off buildings.

**Recommendation: one model per building typology, linked into a site model.**

| Model | Contents |
|---|---|
| `KNP26-PLN-SITE-ZZ-M3-A` | Toposolid, boundary, roads, paths, retaining, pool, camp fire terrace, external drainage. Hosts all links. |
| `KNP26-PLN-COT01-ZZ-M3-A` | The **typical cottage** — authored once, linked **7×** |
| `KNP26-PLN-C08-ZZ-M3-A` | The **twin cottage** |
| `KNP26-PLN-STF-ZZ-M3-A` | Staff lodge + laundry cage |
| `KNP26-PLN-KDR-ZZ-M3-A` | Kitchen, dining, reception, back space |
| `KNP26-PLN-FED-ZZ-M3-A` | Federated / coordination model — links only, no native geometry |

**Why links and not groups, specifically here:**
- Groups misbehave when instances sit on different levels — which is exactly your situation with 27.75 m of fall.
- Grid names repeat (**A–G / 1–6 in every cottage**). In one model that is a duplicate-name collision you have to solve by renaming grids per building. In links, each model keeps its own clean A–G / 1–6.
- Room numbers, door marks and window marks repeat likewise. Links keep the mark namespace per building; groups do not.
- Revit **2022 and later can schedule elements in linked models**, so the historic reason to avoid links for quantities no longer applies. Your BOQ can read all eight cottages out of the federated model.
- Performance: a single model containing eight round pavilions of curved walls and a 159-point toposolid will be slow. Links let you unload what you are not working on.

**The one thing links cost you:** you must maintain a **setting-out schedule** by hand (see D5), because each link's position, rotation and base elevation is instance data, not model data.

**When groups would have been right:** if this were a single flat site with identical FFLs and a single modeller, groups are simpler. Note the rule for next time — *decide before you model, never after*.

### D5 — Setting out on a hilly site

Maintain one table, kept in the site model as a Revit schedule or a key schedule, and reproduced on the setting-out drawing:

| Unit | Easting | Northing | Rotation | FFL (mAOD) | Platform cut/fill |
|---|---|---|---|---|---|
| COT01 | … | … | … | 1487.500 | … |
| C02 | … | … | … | 1491.000 | … |

Every cottage model is authored with **±0.000 = its own FFL**. The link carries the real elevation. That way the cottage model stays genuinely typical and only the table changes.

### D6 — Wall cores vs finishes — the question you asked

**Short answer: yes, separate them — but separate them the *right* way, which is not "model a second wall".**

The reason to separate is that measurement rules bill them separately. Blockwork is billed by m² of wall, by thickness and by height band. Plaster/render is billed by m², **per face**, internal and external distinguished, with different rules for narrow widths and for work to curved surfaces — and this project is *full* of curved surfaces at `R5795`. If plaster is buried inside a compound wall type you cannot get per-face areas out, you can only get a volume.

But modelling a separate 15 mm wall on each face of every wall doubles or triples your element count, creates thousands of junction failures on curved walls, and makes the model unusable.

**The recommendation, in order of preference:**

1. **Core wall = one Revit wall, with the *structural core* correctly identified in the compound structure.** Type name carries the core: `WL-BLK-200-PL2` (200 blockwork, plastered both faces). Set **Structural Material** on the core layer.
2. **Use Revit Parts to split the wall by layer for takeoff.** Parts give you a schedulable, individually-materialled object per layer, per wall, with real areas and volumes — without exploding the model into separate wall elements. This is the correct answer for "I need m² of plaster per face" and it is grossly under-used.
3. **Model a finish as a separate element only where it is atypical** — a feature stone facing, a tiled wet area in the en-suites, timber lining in the lounge. Those are genuinely separate products with separate rates, and there are few of them.
4. **Room-based finish data for everything schedule-driven.** The architect has already annotated `timber panquette ff` and `cem. screed ff` room by room. Put those into the Room's finish parameters (Floor Finish / Wall Finish / Ceiling Finish / Base Finish) plus a finish *code*. That drives the Room Finish Schedule directly and cross-checks the geometric take-off.

**Floors — separate, always:**
Do **not** put slab + screed + finish in one floor type. Model:
- **Structural slab** (150 mm RC, or as designed) — the structural engineer's element
- **Screed** — separate floor, its own thickness
- **Finish** — separate thin floor per room, so parquet and screed areas are exact and per-room

This costs you three floors instead of one and buys you three correct bill lines instead of one wrong one. On a lodge where the floor finish *is* the product the client is buying, that is a good trade.

**Roofs:** structure and covering separate, for the same reason.

**The rule to write in the BEP:**
> *An element is modelled separately when it is measured separately, or when it is bought separately. Otherwise it is a layer.*

### D7 — Measurement standard — **this needs a decision and there is a gap**

A Ugandan QS will expect the **Standard Method of Measurement of Building Works for Eastern Africa (2nd Edition, 2008)**, or the AAQS *Standard Method of Measuring Building Work for Africa*. Those are the regional standards, harmonised across Kenya / Uganda / Tanzania / Rwanda.

STINGTOOLS currently ships **NRM2, CESMM4, POMI, ICMS 3 and MMHW** — verified in `StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs`. **East African SMM is not among them.**

**Recommendation:**
- **Set the active standard to POMI** (RICS Principles of Measurement International). It is the closest in structure to the East African SMM — trade-level classes, international convention — and it will not produce UK-specific section headings that confuse the local QS.
- **Agree the bill structure with the QS in writing before you export anything.** Get their preferred section order and their standard description wording, and map it once.
- Log the missing SMM-EA rule set as a tool gap (Part 8).

### D8 — Level of detail, and what you will deliberately *not* model

Write this down, because half of BOQ disputes are about what the model was supposed to contain.

| Model it (LOD 300) | Do **not** model — bill by provisional sum / manual measured addition |
|---|---|
| Walls, floors, roofs, slabs, columns, beams | Excavation to formation, working space, disposal |
| Doors, windows, and their ironmongery *schedules* | Temporary works, scaffolding, propping |
| Sanitaryware, kitchen equipment (as generic types) | Prelims, site establishment, insurances |
| Room finishes | Painting *specification* detail (billed off the finishes schedule) |
| Roof structure and covering | Small builder's work, chases, holes < threshold |
| Pool shell, coping, plant room | Pool filtration equipment and pipework (specialist PC sum) |
| Retaining walls, steps, paths, hardstanding | Landscaping and planting (provisional) |
| Below-ground drainage runs and chambers | Connections to authority mains (provisional) |

Anything in the right-hand column enters the bill as a **measured addition** or a provisional sum — the BOQ engine supports rows not backed by model elements, and you should use it rather than fudging geometry.

### D9 — Classification

Two layers, both cheap if done at family/type creation and expensive if retro-fitted:

1. **STING ISO 19650 tag** — the 8-segment `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` asset tag. This is your identity spine and it drives the auto-tagger, validation, and handover data. For this project: `LOC` = the building (`COT01`…`C08`, `STF`, `KDR`, `SITE`), `ZONE` = suite/wing where meaningful.
2. **A commercial classification** — **Uniclass 2015 `Ss` (systems) and `Pr` (products)** is the right choice for a bill, because `Ss` maps almost one-to-one onto how a QS groups measured work. Assign it at the **Type**, never the instance. STING can also write CSI MasterFormat if the client asks for it.

Do **not** rely on Revit's Assembly Code / Uniformat unless the QS asks for it — it is a US table and it will not match a Ugandan bill.

### D10 — Naming, once, for everything

**Files (ISO 19650):** `KNP26-PLN-COT01-ZZ-M3-A-0001`
Project – Originator – Volume – Level – Type – Role – Number.

**Revit types** — the type name is the bill description. Build it so it reads as one:
```
<Element>-<Material/Spec>-<Size>-<Variant>
WL-BLK-200-PL2          200mm blockwork wall, plastered both faces
WL-BLK-150-PL1-EXT      150mm blockwork, plastered one face, external
FL-RC-150               150mm reinforced concrete slab
FL-SCR-50               50mm cement screed
FL-FIN-PARQ-20          20mm timber parquet finish
DR-D2-PVO-900x2100      Door type D2/pvo
WN-W1-PVO-1500x1200     Window type W1/pvo
RF-THATCH-XXX           (pending specification)
```
**Rooms:** `COT01-01 Executive Room`, `COT01-02 Lounge`, `STF-05 Room 5`. Prefix with the building. Never rely on Revit's auto-number.

**Views:** let the drawing-type engine name them from the template so the sheet number and the view name cannot drift apart.

### The one-page naming standard for this project

Every name on the job, in one place. **`KNP26` is the project code; `COT01`…`COT08`, `STF`, `KDR`, `POOL`, `EXT` are the LOC codes.** Use the same LOC code in all seven places below — that single consistency is what makes the automation work.

| Thing | Pattern | Example |
|---|---|---|
| **Model file** (ISO 19650) | `PROJ-ORIG-VOL-LVL-TYPE-ROLE-NUM` | `KNP26-PLN-COT01-ZZ-M3-A-0001.rvt` |
| **Drawing file** | same, `TYPE=DR` | `KNP26-PLN-COT01-GF-DR-A-1001.pdf` |
| **Sheet number** | from the drawing type's pattern — never typed | `A-GF-001` |
| **Scope box** | `STING::<drawing-type-id>::<level>::<tag>` — **hard regex, no spaces** | `STING::arch-setting-out-A1-1to50::GF::COT01` |
| **Level** | plain, parseable prose — **not** prefixed | `Ground`, `Level 01`, `Roof`, `Basement 1` |
| **Grid** | per building model, keep the architect's | `A`–`G`, `1`–`6` |
| **Room** | `<LOC>-<nn> <Name>` | `COT01-01 Executive Room` |
| **Wall type** | `WL-<core>-<thk>-<finish>` | `WL-BLK-200-PL2` |
| **Floor type** | `FL-<material>-<thk>` | `FL-RC-150`, `FL-SCR-50`, `FL-FIN-PARQ-20` |
| **Door / window type** | `DR-<code>-<w>x<h>` / `WN-<code>-<w>x<h>` | `DR-D2-PVO-900x2100`, `WN-W1-PVO-1500x1200` |
| **Material** | ALL-CAPS, `<TYPE> <QUALIFIER> <SIZE>` — chosen so the right carbon/waste keyword fires first (Part 4A) | `MAKUTI THATCH ROOFING 150MM` |
| **View** | generated by the drawing type | `STING - arch-plan-A1-1to100 - COT01` |
| **Workset** | discipline only, if worksharing at all | `A-Architecture`, `S-Structure` |
| **Asset tag** | the 8-segment STING tag, auto-built | `A-COT01-Z01-GF-WAL-ENC-BLK-0001` |

### Sheet numbers as the full ISO code — exactly how

You want `KNP26-PLN-COT01-GF-DR-A-1001`. Every field is reachable; three things need setting and one has a trap.

**1. Project Information shared parameters** (once per model, by hand — nothing auto-fills these):

| Parameter | Value | Feeds |
|---|---|---|
| `PRJ_PROJECT_COD_TXT` | `KIBALE` | `{project}` |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | `PLN` | `{originator}` |

> **You must re-run Load Shared Parameters first — including on a model that is already set up.**
>
> Until K-11 these two parameters were **declared but never bound**: they had no row in
> `CATEGORY_BINDINGS.csv`, so they never appeared in Manage → Project Information and
> `DrawingTokenContext` read them as null on every model. K-11 added the binding rows (36 `PRJ_*` in
> total), but a binding row only takes effect when the binder runs.
>
> **Order, on an existing project:**
> 1. Deploy the current build (`deploy.bat`), restart Revit.
> 2. Open the model → **STING → Setup → Load Shared Parameters**. It is idempotent: it skips what is
>    already bound and inserts what is not, so it is safe on a live model.
> 3. **Manage → Project Information.** Both parameters now appear. Type the values.
> 4. Re-run your sheet numbering. `{project}` and `{originator}` now resolve.
>
> If you skip step 2 the fields will still not be there, and the failure is silent — the sheet number
> renders with the segment simply missing, `-PLN-COT01-…` with nothing before the first dash.

#### Showing only part of the code on a busy sheet — the answer to "can I hide segments like tags do"

Yes, and it is better than the tag mechanism, because **each segment is its own parameter** rather
than a preset selecting a subset of one string.

Every sheet STING produces now carries the seven segments individually, plus their join:

| Parameter | Holds |
|---|---|
| `PRJ_SHEET_PROJECT_TXT` | `KIBALE` |
| `PRJ_SHEET_ORIG_TXT` | `PLN` |
| `PRJ_SHEET_VOLUME_TXT` | `COT01` |
| `PRJ_SHEET_LEVEL_TXT` | `GF` |
| `PRJ_SHEET_TYPE_TXT` | `DR` |
| `PRJ_SHEET_ROLE_TXT` | `A` |
| `PRJ_SHEET_SEQ_TXT` | `1001` |
| `PRJ_SHEET_FULL_REF_TXT` | `KIBALE-PLN-COT01-GF-DR-A-1001` |

**Nothing new is derived** — these are the same values the sheet number was built from, which used
to be discarded after substitution.

**Selective display is a title-block label edit, and it is yours to make** — no re-authoring, no new
family, no code:

- **Busy plan at 1:100** — a label showing `PRJ_SHEET_ROLE_TXT` + `PRJ_SHEET_LEVEL_TXT` +
  `PRJ_SHEET_SEQ_TXT` gives `A-GF-1001`: enough to find the sheet, short enough to read.
- **CDE / transmittal stamp** — one label bound to `PRJ_SHEET_FULL_REF_TXT` gives the complete ISO
  reference in a single field.
- **Drawing register** — schedule the segments as separate columns and sort by any of them.

> **If a label shows `{project}` or `{lvl}` in braces, that segment never resolved.** It is not a
> rendering fault. The sheet number will have been rejected too — see the warning in
> `StingTools.log`, which names the token and the parameter to set. Braces are deliberate: a blank
> would read as "this project has no volume code" instead of "this was never filled in".

**Why this was worth fixing.** `DrawingTokenContext.cs:66` does
`ReadProjectInfo(doc, "PRJ_PROJECT_COD_TXT")` with **no fallback** — unlike `{lvl}`, which was given
an `IsoNaming.Level` fallback in K-7. An unbound read returns empty, the applier substitutes it
happily, and the segment vanishes. There is no warning, and the sheet looks plausible.

**2. The drawing-type profile** — in your project override `_BIM_COORD/drawing_types.json`:

```json
"sheetNumberPattern": "{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}",
"isoNaming": { "volume": "COT01", "type": "DR", "role": "A" }
```

**3. Drop `-{suit}-{rev}` from the shipped pattern.** The corporate `arch-plan-A1-1to100` ships as `…-{seq:D4}-{suit}-{rev}` with `suitability: "S2"`, `revision: "P01"` — so out of the box you get `…-A-0001-S2-P01`, not your target. Removing them from `isoNaming` alone is **not enough**: the tokens would render empty and leave trailing `--`. Remove them from the *pattern*.

> **The trap — `{lvl}` has no profile fallback.** `vol`, `type` and `role` all fall back to `IsoNaming`; `{lvl}` does not, because `IsoNaming` has **no Level field** (`DrawingType.cs:427-440`). `DrawingTokenContext.cs:57` is `{ "lvl", levelCode ?? "" }` — the producing command must pass it. If it passes nothing you get `KNP26-PLN-COT01--DR-A-1001` with an empty segment and no warning. Gap K-7.

**On `GF` vs `00`:** the ISO 19650 UK Annex level field is `00` for ground, and `Iso19650Vocabulary.LevelCodes` correctly ships `ZZ XX B2 B1 00…20 RF MZ PH`. `GF` is **not** in it. Since you want this project as a template for future work, **use `00`** in the sheet number and keep `GF` only as the STING tag code. That is the alignment you asked for, and it costs nothing here — one storey.

Since each cottage needs its own volume code, you need **one drawing-type variant per building** (`arch-plan-COT01`, `arch-plan-COT02`, …) differing only in `isoNaming.volume`. Tedious but honest; the alternative is a code change to source `{vol}` from the element's LOC.

### Door and window marks — controlled tokens, thin marks

Your instinct is right: keep the Mark thin and put the detail in the schedule.

**Nothing writes Mark from STING tokens** — `DOOR_NUMBER` appears nowhere in the codebase, and the only writers of `ALL_MODEL_MARK` are the duplicate-mark warning resolver, the material loader, and the family-symbol builder. The traffic is one-way the other direction: `NativeParamMapper` copies **Mark → `ASS_ID_TXT`** during tagging.

**So the split is:**

| Carries | Where | Example |
|---|---|---|
| Instance identity only | **Mark** | `COT01-D-001` |
| Function / system / product codes | STING tokens `ASS_FUNC_TXT`, `ASS_SYSTEM_TYPE_TXT`, `ASS_PRODCT_COD_TXT` | `ACC`, `INT`, `DR` |
| Type, size, fire rating, ironmongery, finish | **type parameters, shown in the schedule** | `DR-SGL-TIMBER-0900x2100` |
| The architect's original code | `ASS_LEGACY_REF_TXT` | `D2/pvo` |

The drawing then shows a short mark; the door schedule carries the columns.

**On making tokens read-only — you cannot, and the mechanism that looks like it does is half-built.** Revit does not allow a project shared parameter to be read-only in the Properties palette, and nothing in STINGTOOLS attempts it.

`ASS_TOKEN_LOCK_TXT` exists and works, but it is **snapshot-and-restore, not prevention**: it records the value before auto-population, lets everything overwrite it, then writes it back. Format is a comma-separated token list, e.g. `LVL,SYS,PROD`. Nine tokens are lockable — `DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV`. `SEQ` is not.

Two limits worth knowing: it only snapshots **non-empty** values, so locking an empty token does nothing; and **there is no UI anywhere** to set it — you type into the parameter by hand. Also `BatchTagCommand`'s own lock check is **dead code** (gap K-9), so use `TagAndCombine`, which routes through the working path in `BuildAndWriteTag`.

**Two things to keep straight**, because the code does not:
- The **level code** (`GF`, `L01`) is derived from the level *name* by a parser. Name levels so the parser can read them, and never put the building code in the level name — `BLD2-L01-FFL` parses to `L201`.
- The **`{vol}`** field in ISO file names comes from the *drawing type's* JSON profile, not from the element's LOC. With eleven buildings you either hand-author drawing-type variants per building or accept that the volume field does not distinguish them. Gap F-7.

---

## Part 1A — "Can I model COT01 and have the rest be links, which are again linked?"

**No. Link COT01 seven times directly into the site model. Do not nest.**

What you are describing is a **nested link** — COT01 linked into some intermediate model, which is then linked into the site. Revit supports it, but it costs you the two things this project depends on:

1. **Nested links do not schedule reliably.** "Include elements in links" reaches *one* level. A nested link has no true condition set for schedule filtering, so it drops out of link schedules. Your quantities would silently lose a level of depth — the same failure mode as the ×1 multiplier, but harder to spot.
2. **Visibility becomes conditional.** A nested link only appears in the grandparent if its reference type is **Attach**, not **Overlay**, and even then it is often visible only through a linked view rather than a host view. You would spend real time fighting visibility graphics that a flat structure never raises.

And nesting buys you nothing here. The thing you actually want — *one model, seven positions, edit once, propagate everywhere* — is what a **flat link with seven Shared Sites** already gives you.

### The structure to use

```
KNP26-PLN-SITE  (host)
├── KNP26-PLN-COT01   × 7 instances, each on a different Shared Site
├── KNP26-PLN-COT08   × 1   (twin cottage)
├── KNP26-PLN-STF     × 1
├── KNP26-PLN-KDR     × 1
└── survey DWG
```

One level of linking. Seven instances of one file. Each instance carries its own position, rotation and base elevation through its Shared Site.

**The only legitimate reason to nest** would be if the cottage itself contained a sub-assembly authored by someone else — a modular bathroom pod from a supplier, say. You do not have that.

**If you ever must nest**, set the reference type to **Attach** so the nested model reaches the grandparent, and verify your quantities by hand — do not trust the schedule.

---

## Part 1B — "I thought the levels would be ISO coded"

They are, in one place, and not in another. This is a real inconsistency in the tool, not a misunderstanding on your part.

**ISO 19650 (UK Annex) level codes are two characters:**

| Code | Meaning |
|---|---|
| `ZZ` | Applies to many levels — a site section, a long elevation |
| `XX` | Applies to no level — an equipment data sheet, a specification |
| `00` | Ground / base level |
| `01`, `02`, … | Upward, incrementally |
| `B1`, `B2` | Basements |
| `RF`, `MZ`, `PH` | Roof, mezzanine, penthouse |

Note **`00`, not `GF`**. The Annex is numeric above ground.

**STINGTOOLS carries both, in different files, and nothing reconciles them:**

- `Core/Drawing/Iso19650Vocabulary.cs:257-269` has the **correct ISO list** — `ZZ XX B2 B1 00 01 … 20 RF MZ PH`. This is what file and sheet names use.
- `ParameterHelpers.GetLevelCode` produces the **STING tag vocabulary** — `GF L01 LG UG SB PH AT TR POD MZ PL`. This is what goes in your asset tags.

So the same level is `00` in the file name and `GF` in the tag. Both are defensible in isolation; having both with no mapping is the gap (F-4, F-6). There are actually **five** level vocabularies in the tree, and a level name the parser cannot read becomes a 12-character passthrough code that then **fails STING's own 4-character validator**.

**What to do on this project:** it barely bites you, because the cottages are single-storey. Use `GF` in tags and `00`/`ZZ` in file names, name levels as plain prose the parser can read (`Ground`, `Roof`), and keep the building code out of the level name. The proposed unified registry — with `code` and `isoCode` on one entry — is authored for you at `GUIDES/kibale-project-config/spatial_codes.json`.

---

## Part 2 — Zoning: when, why, and how to zone this project

### Zone is not the same as Volume or Level

ISO 19650 gives you a **spatial breakdown** with more than one axis, and the commonest mistake is collapsing them:

| Axis | Answers | On this project |
|---|---|---|
| **Volume / Location** (`LOC`) | *Which building?* | `COT01`…`COT08`, `STF`, `KDR`, `POOL`, `EXT` |
| **Level** (`LVL`) | *Which storey?* | `GF`, `RF` — trivial here |
| **Zone** (`ZONE`) | *Which functional or management area?* | see below |

A zone is a **management** boundary, not a geometric one. You zone so that a person can be given a slice of the project and own it — for coordination, for phasing, for a work package, for a tender section.

### When to create zones

**Create them once the site layout is frozen and before you tag anything.** Zones are written into every asset tag; changing them later means a re-tag. They cost nothing to define and are expensive to change.

**Do not zone if:** the project is one building, one storey, one contractor, one package. Zoning a simple project adds a token that carries no information.

**Do zone if** any of these is true — and three are true here:
- more than one building on a shared site ✔
- packages that will be tendered or built separately ✔
- distinct operational areas with different clients-in-use (guests vs staff) ✔
- phased construction

### The zoning for Kibale

Four zones. Deliberately few — a zone you cannot explain in one sentence is a zone you do not need.

| Zone | Name | Contains | Why it is its own zone |
|---|---|---|---|
| **Z01** | Guest accommodation | the 7 typical cottages + the twin cottage | one repeated product, one package, one quality benchmark; the client will want its cost isolated per key |
| **Z02** | Public / hospitality | reception, kitchen & dining, back space, swimming pool, camp fire terrace | the guest-facing serviced core — heaviest MEP, most coordination, likely a specialist kitchen subcontract |
| **Z03** | Back of house | staff lodge, laundry cage | different standard, different client-in-use, and often a different budget line the owner wants seen separately |
| **Z04** | External works & infrastructure | roads, paths, steps, retaining, platforms, drainage, water, power reticulation | on 27.75 m of fall this is a major package that must be priced and managed on its own, not smeared across the buildings |

### How to apply them

1. Put the codes in `project_config.json` under **both** `ZONE_CODES` and `CUSTOM_VALID_ZONE` — same split-vocabulary problem as LOC. Already authored for you.
2. Zone is auto-derived by `SpatialAutoDetect.DetectZone` from the room and the element position, so **it mostly fills itself** once rooms exist. Check it with `PreTagAudit` and correct the strays with the token writer.
3. Each cottage sits wholly inside Z01, so at cottage level the zone is constant — its value is at the *site* level, where it separates the four packages.
4. Use the zone, not the building, for anything you want reported as a package: BOQ grouping by zone, per-zone carbon, per-zone programme.

### What zoning is not for

**Do not use zones to control drawing extents** — that is what scope boxes do. Do not use zones as a substitute for the LOC code; a bill grouped by zone tells you what the *packages* cost, a bill grouped by LOC tells you what each *cottage* costs, and you will want both.

---

## Part 2 — Scope boxes: when, and how to name them

**What they are for:** controlling *view extents* consistently across many views, and cropping plans to a building or a zone. They are a documentation device, not a data device. Do not use them to carry information — that is what the `LOC`/`ZONE` tokens are for.

**When to create them:** **after** shared coordinates are set and the site layout is frozen; **before** you create any views or sheets. Creating them late means re-cropping every view.

**On this project you need surprisingly few**, because each building is its own model and its own link. You need:

| Scope box | Purpose |
|---|---|
| Whole site at 1:500 | the site plan |
| Site split N / S | two A1 sheets at 1:200 with a match line |
| One per cottage | setting-out and platform drawings — **scope boxes rotate in plan**, so align each to its cottage's angle |
| Staff block, kitchen/dining, pool | the one-off buildings |

### The naming rule is a hard contract, not a convention

If you want STING to generate views and sheets from scope boxes, the name must match this exactly (`Core/Drawing/ScopeBoxBinder.cs:46-53`):

```
STING::<drawing-type-id>[::<level-code>][::<tag>]
```

Regex-enforced. Legal characters inside a segment are **letters, digits, `.`, `_`, `-` only — a space breaks it**. So for this project:

```
STING::arch-site-A1-1to500::ZZ::SITE
STING::arch-setting-out-A1-1to50::GF::COT01
STING::arch-setting-out-A1-1to50::GF::COT02
…
STING::arch-setting-out-A1-1to50::GF::COT08
STING::arch-plan-A1-1to100::GF::STF
STING::arch-plan-A1-1to100::GF::KDR
STING::arch-plan-A1-1to100::GF::POOL
```

The middle segment is the **drawing type id**, and it must be one that actually exists in `STING_DRAWING_TYPES.json` — an unknown id is reported as a warning and skipped, not guessed at. Names that begin `STING::` but fail the pattern surface as named warnings in the command dialog rather than being silently dropped, which is good behaviour; names without the prefix are simply ignored.

`DrawingTypes_FromScopeBoxes` is **idempotent** — it indexes existing views by `(drawingTypeId, scopeBoxId)` and updates rather than duplicating, so you can re-run it safely as the site layout settles. `DrawingTypes_SuggestFromScopeBoxes` is the dry run; use it first.

The Project Setup wizard can also bulk-rename scope boxes to a pattern **before** it lays out grids, and can auto-generate two perpendicular sections per scope box — useful here, because it handles tilted boxes correctly via the box transform, which is exactly what you need for eight rotated cottages.

> **Correction to conventional practice:** a plain `SB-COT01` name is fine for a purely manual workflow, but it does nothing in STING. If you are going to use the automation, use the `STING::` form from the start — renaming scope boxes after views exist re-runs the crop on everything.

Inside each **building model**, you generally do not need scope boxes at all — the building is the extent.

---

## Part 3 — The step-by-step sequence

### Stage A — Mobilise (before Revit)

1. Fix the **project code** (D1).
2. Agree the **measurement standard and bill structure with the QS** in writing (D7).
3. Obtain: survey in DWG/CSV, sections/elevations, outline specification, roof design. Raise the RFI list in Part 7.
4. Write a short BEP: the ten decisions above, the file naming, the LOD table, who models what.

### Stage B — Set up the container

5. In the first `.rvt`: set **Project Number and Name** → **save** → **close and reopen** so the root gets its ExtensibleStorage stamp (D1).
6. Run **`CreateFolders`** and pick **CdeFirst** explicitly — the setup wizard will not ask you. Confirm the code-suffix setting at the same time.
7. Add **all three** LOC keys — `LOC_CODES`, `CUSTOM_VALID_LOC`, `LOC_CODES_EXTRA` — to `project_config.json` with the same eleven building codes, *before* anything is tagged (Part 6).
8. Run **`LoadSharedParams`**. Everything downstream depends on it, and `SetString` silently no-ops on unbound parameters — model first and bind later and you lose the data with only a log warning.
9. Fill the **`PRJ_ORG_*`** parameters in Project Information by hand. No command does this.
10. Run the **`ProjectSetup`** wizard for levels, grids, disciplines, standards.
11. Start every model from the **corporate template**, so the bindings and types come with it rather than being retro-fitted per model.
12. Create the six models of D4, each with Project Information filled in (number, name, client, address, originator).
13. Set coordinates in the site model; **Acquire Coordinates** into each building model (D3).

### Stage C — Ground first

14. Import the survey **points file** (not the PDF). Build the **Toposolid** from it. Set the contour display to **0.5 m** to match the survey so you can visually verify against the issued sheet.
15. Model the **boundary** and the existing features that matter: the existing structure, the surveyed trees you are keeping (`mango`, `ovacado`), the access road.
16. Model the **platforms**. In Revit 2026 use **toposolid subdivisions with a negative offset** — they excavate the host toposolid automatically, are individually selectable, and are a separate subcategory in Visibility/Graphics so you can style them. One subdivision per cottage platform, one for the pool terrace, one for the kitchen/dining apron, one for the camp fire terrace.
17. **Cut/fill.** Elements that intersect a toposolid (floors, roofs, other toposolids) can excavate it, and the excavated volumes schedule. This gives you an earthwork quantity you can defend. It does **not** work for masses or generic models — only total cut/fill is reported for those — so build platforms from toposolids or floors, never from masses.
18. Retaining walls, steps, ramps, paths. On 27.75 m of fall these are a real cost item; model them properly at LOD 300.

### Stage D — The typical cottage (the highest-leverage hour of the project)

19. Model **COT01 completely and correctly**, because you are about to multiply every mistake by seven.
    - Radial grid A–G / 1–6, set out at the drawn **22°** and **45°**.
    - `R5795` external wall — one curved wall, not a polygon of segments.
    - Internal partitions, en-suites, `duct` risers.
    - Doors `D2/pvo`, `D5`; window/opening types as drawn.
    - Roof — **this is where you need the missing information**.
    - Rooms, with finish codes from the annotation (`timber panquette ff`, `cem. screed ff`).
    - Sanitaryware and FF&E as scheduled families, not as decoration.
    - Full parameter/type naming per D10 as you go — *not* as a clean-up pass.
20. Run the **tagging and validation pass on COT01 alone**. Fix every warning. Only then multiply.
21. Link COT01 into the site model **7×**, position and rotate per the setting-out schedule.

### Stage E — The one-offs

22. **Twin cottage** — start as a copy of the COT01 model, mirror the spine, add the `bt`.
23. **Staff lodge** — the 3600 module repeats 10×; here a group *inside* that one model is appropriate, because all rooms share a level.
24. **Kitchen / dining / reception / back space** — the most services-heavy building; coordinate extract, gas, grease, drainage early.
25. **Pool, camp fire terrace, laundry cage.**

### Stage F — Federate and check

26. Build the **federated model**: links only.
27. Clash and coordination pass. On this project the real clashes are not duct-vs-beam, they are **building-vs-ground**: platforms that do not work, doors that open onto a 900 mm drop, paths steeper than 1:12, drainage that has to run uphill. Check those explicitly.
28. Produce and issue the **setting-out drawing** with the coordinate table.

### Stage G — Data completeness before documentation

29. Run the **pre-tag audit** (dry run) → fix → **batch tag** → **validate**. Nothing goes to BOQ or to sheets until validation is clean. Data first, drawings second — a drawing produced from incomplete data is a drawing you will re-issue.
30. Check the room schedule, door schedule and window schedule are complete and unique-marked **per building**.

### Stage H — Documentation

31. Create **scope boxes** (Part 2).
32. Apply the **drawing types**: `arch-site-A1-1to500` for the site plan, `arch-setting-out-A1-1to50` for cottage setting-out, `arch-plan-A1-1to100`, `arch-section-A1-1to50`, `arch-elev-A1-1to100`, `arch-detail-A3-1to20`, `arch-floor-finishes-A1-1to100`, `door-schedule-A3`, `arch-window-schedule-A3`. These carry the sheet size, title block, scale, view template, crop strategy and sheet-number pattern as one bundle, so every drawing of a type comes out identical.
33. Produce sheets from the drawing types, not by hand. Let the sheet numbering pattern generate the numbers.
34. Run the **ISO 19650 sheet compliance check** before the first issue.

### Stage I — BOQ

35. Set the standard with **`Cost_SetMeasurementStandard`** (POMI, per D7); author `_BIM_COORD\takeoff_rules.json` for the pool and anything else with no corporate rule; run **`Cost_ReloadRules`**.
36. Put the project rate card at **`_BIM_COORD\rate_card.json`** and the project bill descriptions at **`_bim_manager\boq_custom_templates.json`**.
37. Run **`BOQPrepForExport`** → **`BOQ_RateGapReport`** → fix gaps → **`BOQExportProfessional`**. Sanity-check total walling and roofing m² by hand before you believe the total (Part 4, trap 1).
38. Add the **measured additions** (`BOQAddManualRow`) for everything in the right-hand column of D8.
39. **`BOQSnapshotSave`** on every issue. From the second issue on, **`BOQSnapshotCompare`** — that is the single most valuable BOQ artefact for a client, because it answers "what changed and why did the price move".

### Stage J — Issue and control

40. Transmittals for every issue. Revision management on every re-issue. Nothing leaves `02_PUBLISHED` without a transmittal record.

---

## Part 2B — Every file you need, listed

### From the surveyor — ask for all six

| # | File | Format | Why you need it | If you don't get it |
|---|---|---|---|---|
| 1 | **Point file** | `.csv` or `.txt`, columns `Point, Easting, Northing, Elevation, Code` | The **only** correct source for a toposolid. 159 points exist — you have them as a table in the PDF, but retyped is not surveyed | You cannot build accurate topography. Blocking |
| 2 | **Survey drawing** | `.dwg` (not PDF) | Boundary, contours, existing structures, trees, road — on real coordinates | You trace, and every dimension inherits a 100–300 mm error |
| 3 | **Control point schedule** | `.pdf` or `.csv` | The permanent station(s) you set out from, with coordinates and a description | You have no check on whether the model is on the right grid |
| 4 | **Coordinate system statement** | text/email is fine | Which projection and which vertical datum — UTM 36N? Arc 1960? Local grid? | Your mAOD values are numbers with no meaning |
| 5 | **Boundary / title deed plan** | `.pdf` + `.dwg` | The legal boundary, which is not always the surveyed fence line | You may set out a building over a boundary |
| 6 | **Existing services / utilities** | `.dwg` or markup | Existing water, power, drainage runs on site | You design a drain through a live main |

**One email covers it.** Ask for all six at once and state that the point file must include the elevation column — surveyors often send an XY-only file.

### From the architect

| File | Format | Status |
|---|---|---|
| Floor plans | `.dwg` | ✅ have (as PDF — request DWG) |
| **Sections** | `.dwg` | ❌ **missing — blocking** (RFI-03) |
| **Elevations** | `.dwg` | ❌ **missing — blocking** |
| **Roof design / layout** | `.dwg` + sketch | ❌ **missing — blocking** |
| Outline specification | `.docx` / `.pdf` | ❌ missing (RFI-04) |
| Door & window schedule | `.xlsx` | ❌ missing — needed to resolve `/pvo` |
| Finishes schedule | `.xlsx` | ❌ missing — needed for the room finish codes |

### What you will produce

| Category | Files | Count |
|---|---|---|
| **Revit models** | site, cottage, twin cottage, staff, kitchen/dining, federated | **6 `.rvt`** |
| **Project data** | `project_config.json` beside the .rvt; `rate_card`, `takeoff_rules`, `carbon_factors_ug`, `boq_links`, `boq_custom_templates`, `drawing_types` in `_data/coord/` | **7 `.json`** — all authored for you in `GUIDES/kibale-project-config/` |
| **Drawings** | site, setting-out, per-building plans/sections/elevations, details, schedules | `.pdf` + `.dwg` per issue |
| **Bill** | BOQ workbook, snapshot per issue | `.xlsx` |
| **Data exchange** | IFC4 per building + federated | `.ifc` |
| **Coordination** | setting-out schedule, transmittals, revision register | `.csv` / `.json` |
| **Backups** | Revit `.rvt` backups — keep out of the CDE folders | — |

**Six models, seven JSONs, one CSV that matters more than any of them** — the setting-out schedule.

---

## Part 3A — Floors and finishes: the definitive method

> Your question: *several rooms, several finishes — different compound floors aligned centre-to-centre, or one oversite plus more layers of screed and tile?*

**Neither. Layer by trade, not by room — and align by top face, never by centre.**

### The rule

| Element | One per… | Sketch extent | Top of element |
|---|---|---|---|
| **Structural slab / oversite** | pour | the whole footprint | SSL |
| **Screed** | screed zone (dry areas vs wet areas to falls) | the zones that share a screed spec | FFL − finish thickness |
| **Floor finish** | **room** | that room only | **FFL** |

Three floor elements stacked, each a *separate* Revit floor. Not one compound. Not a compound per room.

### Why not one compound floor

A compound floor is one sketch. The moment room A is parquet and room B is screed, you must split the sketch anyway — so the compound bought you nothing and cost you the ability to schedule finishes per room. A compound also gives you exactly **one** variable-thickness layer, which you will want for screed-to-falls in the en-suites, and you cannot then also vary the structure.

Compound floors are right when the build-up is genuinely uniform across the whole slab. On this project, the architect has already annotated `timber panquette ff` in the bedrooms and lounges and `cem. screed ff` in the wet areas and circulation. The finishes are *not* uniform. That settles it.

### Why not a separate compound per room

Element bloat with no benefit, and every room boundary becomes a floor edge you have to maintain. You would also be re-declaring the structural slab in every room, which double-counts concrete unless you remember to strip it out of all but one type — a mistake waiting to happen.

### Alignment — top face, not centre

Revit draws a floor **downward from its level**, so the level plane is the *top* of the floor. Set **Level = FFL** for every room, then:

| Element | Height Offset From Level |
|---|---|
| Finish floor | `0` — top sits exactly at FFL |
| Screed | `−(finish thickness)` |
| Structural slab | `−(finish + screed)` — its top is SSL |

So for a 20 mm parquet on 50 mm screed on a 150 mm slab: finish `0`, screed `−20`, slab `−70`. Top of slab lands at −70 = SSL, which is the number the structural engineer and the setting-out drawing both use.

**Centre-to-centre alignment is wrong** and will cost you money. It puts the finish surface at an arbitrary height that varies with every thickness change, so door thresholds, sanitary falls and level annotations all drift. Align to the face people walk on.

### Four settings that matter

1. **Room Bounding — OFF** on the screed and finish floors. Left on, they slice your room volumes and your Room areas go wrong, which corrupts the finishes schedule you are trying to build.
2. **Structural — ON** for the slab only. This is what puts it in the structural engineer's world and the concrete bill.
3. **Screed to falls** — use a floor type with a **variable-thickness layer**, then Modify Sub Elements to add points at the gully. You get a real volume for the falls instead of a flat average, which is the difference between a right and a wrong screed quantity in eight en-suites.
4. **Slab thickenings, downstands, edge beams** — separate elements. They are separately measured and separately poured.

### Skirtings

Do **not** model 100 mm walls. Use a **wall sweep** hosted on the wall, or take the length from the Room perimeter. A skirting is a linear item in the bill; it needs a length, not a solid.

### Use STING's finishes engine rather than doing this by hand

There is a purpose-built covering/plastering pipeline you should be using. **Dock tab MODEL → section "Plaster, render, paint & coatings"** (`UI/StingDockPanel.xaml:2774-2793`):

| Button | Tag | What it does |
|---|---|---|
| **★★ Smart Covering** | `CoveringSmartApply` | substrate detect → mix design (BS EN 13914) → coverage → **injects the finish layer into the compound type** → QA → tags |
| **★ Batch All** | `CoveringBatchApply` | the same across all walls, beams, columns |
| **Room Finishes** | `CoveringRoomSchedule` | builds the room-based wall/floor/ceiling finish schedule and writes it back to the Rooms |
| Materials / Substrate / Paint Sys / Coverage / QA / Export / Fire / Moisture | `CoveringMaterialBrowser`, `CoveringSubstrateAnalyze`, `CoveringPaintSystem`, `CoveringCoverageCalc`, `CoveringQualityCheck`, `CoveringScheduleExport`, `CoveringFireRating`, `CoveringMoistureRisk` | in the "Advanced covering ops" expander |

The engine is `Model/PlasteringEngine.cs` — 12 algorithm classes including `CompoundLayerInjector`, `ElementCoverageCalculator` and `RoomFinishScheduler`. It writes `BLE_ROOM_FINISH_FLOOR_TXT` / `_WALL_` / `_CEILING_` onto Rooms.

> **Watch this default.** `RoomFinishScheduler.GenerateSchedule` falls back to the literal string `"Power-floated concrete + carpet/vinyl"` when the floor finish is empty (`Model/PlasteringEngine.cs:982-983`). That is a UK office default and it is wrong for every room in this lodge. Populate `BLE_ROOM_FINISH_FLOOR_TXT` from the architect's annotations (`timber panquette ff`, `cem. screed ff`) **before** you run Room Finishes, or you will bill carpet you are not laying.

### Room-based finish data — the full method, and the trap in it

This is how the architect's room-by-room annotations become schedulable data and a cross-check on your geometric take-off.

#### ⚠ First, the trap: there are TWO finish parameter families and they do not talk to each other

| Family | Written by | Read by |
|---|---|---|
| **Revit built-ins** — `Floor Finish`, `Wall Finish`, `Ceiling Finish`, `Base Finish` | only the **Fohlio importer** (`ExLink/FohlioFinishesCommands.cs:26-32`) | the **`ISBRoomFinish`** schedule (`ExLink/ISBAppsCommands.cs:218-236`) — its fields are literally `"Floor Finish"`, `"Wall Finish"`, … |
| **STING shared params** — `BLE_ROOM_FINISH_FLOOR_TXT` etc. (`MR_PARAMETERS.txt:1593-1596`) | **`CoveringRoomSchedule`** (`PlasteringEngine.cs:996-1014`) | the covering engine, the BOQ description tokens |

**So if you run "Room Finishes" and then create the ISB room finish schedule, the schedule is empty.** They write and read different parameters.

There is a one-way bridge — `NativeParamMapper` copies **built-in → STING** during tagging (`ParameterHelpers.cs:2907-2914`), using `SetIfEmpty`. Nothing ever goes STING → built-in.

**Therefore: fill the Revit built-ins first.** They are the ones a native Revit schedule, an IFC export and the ISB schedule can all see, and the tag pipeline will copy them into the STING params for you.

#### The sequence

**Step 1 — Fill the Revit built-in finish parameters, per room.**
Select the rooms → Properties palette → `Floor Finish` / `Wall Finish` / `Ceiling Finish` / `Base Finish`. Fastest route is a room schedule with those four columns and type down it, or paste from Excel.

Use **your** finish codes, not prose (see the code table below).

**Step 2 — Run the tag pipeline once.**
Dock tab **TAGGING → `TagAndCombine`** (or `BatchTag` for the whole project). `NativeParamMapper.MapAll` copies the four built-ins into `BLE_ROOM_FINISH_*_TXT` as step 4 of the pipeline.

**Step 3 — Generate and write back the finish schedule.**
Dock tab **MODEL → section `COVERINGS`** (`StingDockPanel.xaml:2772`) → **"Room Finishes"** (`CoveringRoomSchedule`, button at `:2781`).

It computes wall area from the room's boundary segments × room height, and writes any *missing* finishes with `SetIfEmpty` — so **it never overwrites what you entered in step 1**. That is the behaviour you want; it fills gaps only.

> Two warnings. Its hardcoded default for an empty floor finish is `"Power-floated concrete + carpet/vinyl"` — a UK office default that is wrong for every room here, so **fill step 1 first**. And its "N rooms updated" count increments per room even when `SetIfEmpty` changed nothing, so that number is not evidence anything was written.

**Step 4 — Build the finish schedule view.**
Dock tab **INTEROP → the ISB drop-down → "Room finish"** (`ISBRoomFinish`, `StingDockPanel.xaml:5459` — it is a **combo-box item, not a button**). Produces *"STING ISB - Room Finish Schedule"* with Number, Name, Level, Department, Area, and the four finish columns.

**Step 5 — Cross-check against the geometry.**
Compare the room schedule's floor areas against the sum of your per-room finish floors. A mismatch means either a missing finish floor or a room boundary that does not match the walls. This is the whole reason for doing both — the text tells you the *intent*, the geometry tells you the *quantity*, and the discrepancy tells you where the model is wrong.

#### Finish codes — there is no code list in the tool, so here is one

STINGTOOLS ships **no room-finish code legend**. `ROOM_TYPE_CLASSIFIER.csv` is a *lighting* classifier; `CODE_LEGEND.json`'s `FIN` is a 4D *trade* code. Every finish parameter is free text with no picklist.

So define the project's codes and put them in the finish parameters, with the description carried by the material:

| Code | Finish | Maps to material |
|---|---|---|
| `FL-01` | Timber parquet, 20 mm, sealed | `TIMBER PARQUET 20MM` |
| `FL-02` | Cement screed, power-floated | `STANDARD CEMENT SCREED 50MM` |
| `FL-03` | Ceramic tile 300×300, wet areas | `CERAMIC TILES 300X300MM 10MM` |
| `FL-04` | External paved / stone | `CONCRETE PAVER 80MM` |
| `WL-01` | Cement-sand render + emulsion | `CEMENT SAND RENDER 1-4 (EXTERNAL STANDARD)` |
| `WL-02` | Ceramic tile to 2100 mm, wet areas | `CERAMIC TILES 300X300MM 10MM` |
| `WL-03` | Timber lining | `TIMBER T&G CEILING 15MM` |
| `CL-01` | Timber tongue-and-groove ceiling | `TIMBER T&G CEILING 15MM` |
| `CL-02` | Plasterboard + skim + emulsion | `GYPSUM BOARD STANDARD 9.5MM` |
| `SK-01` | Timber skirting 100 mm | `HARDWOOD SKIRTING 100MM` |
| `SK-02` | Cement skirting 100 mm | `CEMENT SKIRTING STANDARD 100MM` |

Put the **code** in the Revit finish parameter (`FL-01`), and let the schedule carry a description column keyed off it. A code is filterable, sortable and can be checked; a sentence cannot.

> **What the tool cannot do:** nothing reads a room's finish and creates the matching **floor finish element**. `CoveringSmartApply` injects finish layers into *wall* compound types and writes parameters on beams and columns — it does not create Floors from room data. Your per-room finish floors are drawn by hand. Logged as gap K-3.

> **Note on Parts.** Revit *Parts* are an elegant way to get per-layer areas out of a compound element. But STING's takeoff does not measure them — `OST_Parts` appears only in the category registries, never in a takeoff rule. If you use Parts, they are for your own schedules; the BOQ will not see them unless you author a rule for the category.

---

## Part 3B — Do schedules include linked elements? Yes, twice over — and one of them will under-count you

**Two separate mechanisms. Do not confuse them.**

### 1. Revit's own schedules

Open the schedule → Properties → Fields → **Edit…** → tick **"Include elements in links"**. Supported for schedules of model elements (walls, floors, roofs) and drawing lists. **Not** supported for note blocks, view lists or key schedules.

The limitation that will bite you: once links are included, the **Family, Type, Family and Type, Level, and Material parameters become read-only — and you cannot filter the schedule by any of them**. So the obvious plan of "one door schedule, filtered by building" does not work across links. Filter on a STING parameter instead — `ASS_LOC_TXT` is a shared parameter and stays filterable. That alone is a good reason to have the LOC vocabulary right before you start.

Room-to-element relationships also break across links: elements in one model cannot see rooms in another, so room-dependent parameters come back empty. Keep rooms and the elements they describe in the same model.

### 2. STING's BOQ — it walks the links itself

The BOQ engine does **not** depend on the Revit checkbox. `BOQCostManager.CollectLinkedItems` (`BOQ/BOQCostManager.cs:3219-3323`) enumerates every loaded `RevitLinkInstance`, opens the link document, and runs the full takeoff inside it. Rows are tagged `[Linked: <model>]`, carry a `SourceModel` value, and can be grouped by it (`BoqGroupingMode.SourceModel`).

Configure it from the **BOQ Cost Manager panel → "Linked models in takeoff"** picker: tick the links whose quantities you want. Persisted to `<project>\_BIM_COORD\boq_links.json`.

### ⚠ The trap that would have cost you six cottages

Read this line carefully (`BOQCostManager.cs:3251`):

```csharp
if (!seenTitles.Add(linkName)) continue;
```

**A link placed seven times is taken off exactly ONCE by default.** The engine deliberately de-duplicates by model title, because the common case is a shared reference model placed once.

To get ×7 you must **opt in per link**. After you tick a link for inclusion, if it is placed more than once the panel offers a second picker — *"Multiply repeated links… tick a link to multiply its quantities (and cost / carbon) by its instance count"* (`UI/BOQCostManagerPanel.cs:686-708`). Ticking it multiplies `Quantity`, `EmbodiedCarbonKg` and `BiogenicKg` by the loaded-instance count and tags the row `[Linked: COT01 ×7]`.

**On this project: link COT01 seven times, then tick the multiplier.** Skip that one checkbox and your bill contains one cottage and seven-eighths of your accommodation is free.

Two consequences worth knowing:
- **Linked rows are read-only.** They are not cost-stamped, so no `CST_*` parameters are written back into the cottage model, and you cannot select the element in the host from the BOQ row. Cost write-back only happens in the host.
- The link takeoff is **cached per link path**, and the multiplier is applied *after* the cache, so changing the ×N setting takes effect on the next refresh without re-walking the linked models.

---

## Part 3C — Topography: how to get defensible cut and fill

Revit will give you cut and fill volumes, but only through one specific workflow. Anything else gives you a number that is not a quantity.

### The graded-region method — the only one that works

1. Build the **existing** toposolid from the surveyor's point file. Set its **Phase Created = Existing**, **Phase Demolished = None**.
2. Use a toposolid **type with a variable-thickness material** in its structure. This is not optional — the Boolean that produces the volumes needs the existing ground to be a real solid with thickness.
3. Run **Graded Region** on it. Revit copies the surface into the current phase; choose **all points**, not just perimeter points, for accuracy — perimeter-only is for simple sites and this one is not simple.
4. Grade the *new* copy: move points, add subdivisions, form the platforms.
5. Select the graded toposolid → **Cut** and **Fill** appear in Properties, and both are schedulable in a toposolid schedule.

Accuracy is about **±2 %** — good enough to price against, and you should say so on the drawing rather than implying millimetre precision.

### Platforms

In Revit 2026 a **toposolid subdivision with a negative offset** excavates the host automatically, is individually selectable, and is its own subcategory in Visibility/Graphics — so you can give the cottage platforms a distinct colour on the site plan. One subdivision per platform: eight cottage pads, the kitchen/dining apron, the pool terrace, the camp fire terrace, the staff block.

Floors, roofs and other toposolids that intersect a toposolid can excavate it, and those individual excavated volumes schedule. **Masses and generic models cannot** — with those you get only a total. So build every platform, pond and pool excavation from a toposolid or a floor, never from a mass.

### Step by step, in Revit — the version to follow at the keyboard

**Building the ground (about 40 minutes once you have the point file)**

1. Open the **site model**. `Insert → Link CAD`, pick the survey DWG. Positioning **Auto – Origin to Internal Origin**, units **Auto-Detect**, uncheck *Orient to View*. OK.
2. Check it landed: type `ID` equivalent — place a **Spot Coordinate** (`Annotate → Spot Coordinate`) on a known survey point. If the numbers are in the hundred-thousands you have real coordinates. If they are small, the DWG has been moved — stop, go back to the surveyor.
3. `Massing & Site → Toposolid → Create from Import → Select Import Instance`… **only if the DWG has 3D contours**. Otherwise use **Toposolid → Create from Import → Specify Points File**, choose the `.csv`, set units to **Metres**.
4. Revit builds the surface. Check the extremes against the survey: your lowest should be ≈ **1471.5**, highest ≈ **1499.3**. If they are not, the units are wrong.
5. Select the toposolid → Properties → **Phase Created = Existing**, **Phase Demolished = None**.
6. Type Properties → **Edit** the structure → make sure the type has a **Variable** thickness layer. Without this the cut/fill Boolean has nothing to work with and you will get zeros.
7. `View → Visibility/Graphics → Toposolid → Contours` → set interval **0.5 m** so it matches the surveyor's sheet, and compare visually. This is your proof the surface is right.

**Platforms and cut/fill (about 20 minutes per building)**

8. Select the toposolid → `Modify | Toposolid → Graded Region`. Choose **"Create a new toposolid based on the perimeter and interior points"** — *all points*, not perimeter only. This copies the surface into the current phase; the existing one stays behind as the "before".
9. On the **new** toposolid: `Modify → Sub-elements` to drag points to your platform level, or `Modify | Toposolid → Add Subdivision`, sketch the platform outline, and set its **Height Offset** to a **negative** value to cut into the host (Revit 2026).
10. Repeat per platform: eight cottage pads, kitchen/dining apron, pool terrace, camp fire terrace, staff block.
11. Select the graded toposolid → Properties → read **Cut** and **Fill**. Both are there, both schedule.
12. `View → Schedules → Schedule/Quantities → Toposolid`, add fields **Name, Cut, Fill, Net cut/fill**. That table is your earthwork quantity.

**Making the numbers defensible**

- Accuracy is about **±2 %**. Put that note on the drawing rather than implying millimetre precision.
- **Never build a platform from a Mass or a Generic Model.** Those give only a *total* cut/fill, not a per-element volume. Toposolid subdivisions or Floors only.
- Do the graded region **once**, after the layout is frozen. Re-grading resets the comparison.
- Cross-check one platform by hand: area × average depth. If Revit disagrees by more than ~5 %, the variable-thickness layer is missing or you graded the wrong surface.

### What STING does *not* do — plan for it

`Data/STING_DEFAULT_COST_RATES.csv:115` prices `Toposolid` at **60 per m²**. Area, not volume. There is no takeoff rule, no command, and no BOQ path that turns cut and fill **volumes** into bill lines.

So on a site with 27.75 m of fall, earthworks — arguably the largest single risk item — will not appear in an automated BOQ. Handle it deliberately:
- Schedule cut and fill from the graded region in Revit.
- Enter them as **measured additions** (`BOQAddManualRow`) with your own m³ rates for excavate / cart away / fill / compact.
- Or author a project takeoff rule in `_BIM_COORD\takeoff_rules.json` targeting the toposolid category with `quantitySource: SolidVolume` and `unitConversion: ft3_to_m3`.

Do not let it fall through silently. Logged as gap 7.

---

## Part 3D — AutoCAD → Revit: getting each building's setting-out point and shared coordinates

You have a surveyed CAD site. The goal is that every one of the six models opens at the right place, at the right rotation, at the right elevation, and exports back out to the surveyor's coordinates without anyone typing a number twice.

### Step 1 — Harvest the setting-out points in AutoCAD

For each building, decide the **SOP (setting-out point)** — one unambiguous, permanent point. For the round cottages the obvious choice is **the centre of the circle**, because everything is radial from it and a centre is unarguable. For the rectangular buildings use a specific structural grid intersection, named on the drawing.

In AutoCAD, `ID` at each point returns X and Y (Easting and Northing). Record them in the setting-out table, together with the rotation and the intended FFL:

| Unit | SOP description | Easting | Northing | Rotation | FFL (mAOD) |
|---|---|---|---|---|---|
| COT01 | centre of circle | … | … | … | 1487.500 |

That table is a deliverable in its own right — it is what the setting-out engineer works from on site.

### Step 2 — Make the site model the single source of truth

1. In the **site model**, link the survey DWG **Origin to Internal Origin**, and check the DWG units.
2. Reveal the **Survey Point**. Un-clip it, and use **Manage → Coordinates → Specify Coordinates at Point**, picking a known survey control point and typing its real E / N / elevation. Now the site model speaks the surveyor's language.
3. Set **True North** from the survey's north.
4. Put the **Project Base Point** somewhere convenient and round — 1485.000 mAOD near the main building — so plan dimensions read sensibly.

Do this once. Everything else acquires from here.

### Step 3 — Push coordinates into every building model

For each building model: link the site model **Origin to Internal Origin**, then **Manage → Coordinates → Acquire Coordinates**, pick the site link. The building model now shares the site's coordinate system. Unload the site link afterwards if you like; the coordinates persist.

Then link the building models into the site with **Auto — By Shared Coordinates**. They land in the right place with no manual moving.

### Step 4 — The seven cottages: use Shared Sites

This is the part most people get wrong. One cottage model placed seven times needs **seven named Sites**.

Inside the COT01 model, **Manage → Coordinates → Location → Site** — create a named site per position: `COT01`, `C02`, … `C07`. Each carries its own E/N/elevation/rotation. Then when you link COT01 into the site model seven times, set each instance to a different **Shared Site**.

The payoffs:
- one model, seven correct positions, and a rename or a design change propagates to all seven
- `Publish Coordinates` writes the position back into the cottage model, so the cottage model itself knows where all seven of it are
- exports to DWG or IFC with **Coordinate System Basis = Shared** come out in surveyor coordinates automatically

### Step 5 — Round-trip back to the surveyor

Export → Options → **Units & Coordinates → Coordinate System Basis = Shared**. The DWG lands on the surveyor's grid with no manual alignment. Same for IFC via the site/survey point setting.

### Measuring the rotation angle — three ways, in order of reliability

Every cottage sits at its own angle, and you need that number to place the link. How to get it:

**Method 1 — from two coordinates (most reliable).**
Pick two points on the same datum line of the building — for a cottage, the centre and grid intersection A/1. `ID` both in AutoCAD. Then:

```
bearing = ATAN2(N₂ − N₁, E₂ − E₁)   →  degrees
```

In Excel: `=DEGREES(ATAN2(E2-E1, N2-N1))`. Note Excel's `ATAN2` takes **(x, y)** — so `ATAN2(ΔE, ΔN)` gives the angle anticlockwise from **east**. Revit's rotation is also anticlockwise from east, so this drops straight in.

**Method 2 — AutoCAD `DIST`.**
`DIST`, pick the two points, read the **Angle in XY plane** from the output. Same convention. Quick, and it agrees with method 1.

**Method 3 — Revit `Align` + `Rotate`.**
Link the cottage, then `Modify → Rotate`, place the centre of rotation on the SOP, click along the reference edge, then click along the target edge in the survey. Revit reports the angle as you go. Use this to *check*, not to *derive* — it is a picked angle and inherits your snapping accuracy.

**State the convention on the drawing.** "22° anticlockwise from grid east" is unambiguous; "22°" is not. The architect's plan already annotates **22°** and **45°** on the radial geometry — confirm whether those are from grid north or from the building datum before you rely on them (add to RFI-03).

**A check that costs nothing:** after placing all eight links, put a **Spot Coordinate** on each cottage's SOP in the site model and compare against your setting-out table. If any differs, that link was moved by hand instead of by its Shared Site.

### Step 6 — Harvesting the coordinates properly

"Harvesting" is the part people skip, and it is why setting-out disputes happen. You are extracting a small, permanent, checkable table from a large CAD file, and that table becomes a contract deliverable.

**What to harvest, per building:**

| Field | From | Why it matters |
|---|---|---|
| SOP description | your decision, written down | *"centre of circle"* is unarguable; *"corner of building"* is not — which corner? |
| Easting, Northing | AutoCAD `ID` at that point | the setting-out engineer's only input |
| Elevation of the SOP | the survey, not the CAD linework | CAD Z is frequently 0 even when the survey has levels |
| Rotation | AutoCAD `DIST` along a datum edge, or the `ROTATE` reference angle | must be stated in the same convention as Revit's — decimal degrees, anticlockwise from east, or bearing; **say which** |
| FFL (mAOD) | the design, cross-checked against the platform | the number that ties the building to the ground |
| Platform level | the toposolid subdivision | drives cut/fill |

**Three checks before you trust the CAD:**

1. **Is the DWG on real coordinates, or near the origin?** Run `ID` on any surveyed point. If it returns numbers in the hundreds of thousands, you have real UTM/national-grid coordinates. If it returns small numbers, someone has moved the drawing and the coordinates are meaningless — go back to the surveyor.
2. **Are the units what you think?** A DWG authored in metres linked as millimetres lands 1,000× out. Check a known dimension after linking.
3. **Is Z populated?** Many site DWGs carry levels only as text, with all linework flat at Z=0. If so, the contours are annotation, not geometry, and you cannot build a toposolid from them — you need the point file.

**Recording it.** One table, three places, all fed from the same source:

- a Revit **schedule** in the site model, so it cannot drift from the model
- the **setting-out drawing** (`arch-setting-out-A1-1to50`), which is the issued deliverable
- a **CSV in the project folder**, so the contractor can load it into a total station

**Publishing back.** Once each building model has acquired coordinates, run **Publish Coordinates** from the site model into each link. This writes the position *into the building model*, so the cottage model itself knows where all seven instances of it are — and an IFC or DWG exported from the cottage model alone still lands on the surveyor's grid.

**The failure to watch for.** If someone moves a link with the Move tool instead of editing its Shared Site, the model position and the named site silently disagree. Revit will not warn you. Symptom: the setting-out schedule and the drawing show different numbers. Fix: re-associate the instance with its Shared Site rather than nudging it.

### The two-minute check that catches almost everything

Pick one known survey point. In Revit, place a spot coordinate on it. If the E, N and elevation match the surveyor's schedule to the millimetre, your coordinate chain is sound. If it does not, stop and fix it — every quantity, every setting-out dimension and every cut/fill volume downstream depends on this one thing being right.

---

## Part 3E — Tagging: the procedure, and the one caveat that will bite you

### The order matters

Tagging is not "place a tag". A tag renders whatever the element's tokens say, so the tokens must be
right *before* the tag goes on. The pipeline does this for you in one step, but only if you call it in
the right order.

1. **Load Shared Parameters** — once per model, before anything else. Without it the tokens have
   nowhere to land and every later step silently writes nothing.
2. **Set the project tokens** — DISC, LOC, ZONE per Part 1's naming standard. `Set Disc` / `Set Loc` /
   `Set Zone` from the CREATE tab.
3. **Tag & Combine** — the one-click path. It runs the full nine-step pipeline: category filter →
   type-token inherit → populate all tokens → native-parameter map → formulas → build the ISO 19650
   tag → write containers → TAG7 narrative → grid reference.
4. **Validate Tags** — read-only. Fix what it reports before issuing anything.

Do not place tags by hand from the Revit Annotate tab on this project. A hand-placed tag carries no
tokens, passes no validation, and will read blank on the sheet.

### Rooms need their own tag

Revit does not allow a Multi-Category tag to tag a Room, a Space or an Area — this is a Revit
restriction, not a STING one, and no parameter setting works around it.

Use **`STING - Room Tag.rfa`** for rooms. It is already in the content library (manifest entry
`tag-room`), so nothing needs creating. If this project ends up scheduling Spaces or Areas as well,
they need their own tag families on the same basis.

### The reload caveat — the one that will bite you

If anyone edits a tag family and loads it back, Revit offers **"Overwrite the existing version and its
parameter values"**. That wording is easy to skim past. It discards project-set values for **every**
parameter of that family — not just the one that was edited. On a model where tag depth, style and
warning visibility have been tuned, that is a visible, project-wide regression.

**Before** editing any tag family:
- export a schedule of the tag category with the gate and style parameters as columns, and
- note the active mode from **Tag Studio → Presentation Mode → Report**.

**After** reloading, re-apply the presentation mode first — it restores most of it in one pass — then
reconcile against the schedule.

Full procedure, including the parameter list: [`docs/UNIVERSAL_TAG_CONFORMANCE.md`](../docs/UNIVERSAL_TAG_CONFORMANCE.md) → Operator sheet.

### Door and window marks — a short mark on the plan, the full tag on the instance

**The rule.** A door shows a **type mark** on the drawing (`DR-01`) and carries the **full 8-segment
tag** on the instance (`A-COT-Z01-GF-ARC-FIT-DR-0001`). The remaining segments are schedule columns,
not label text. Nobody reads an eight-field code off a 1:100 plan.

**Why the mark is per TYPE, not per instance.** Kibale has 7 identical cottages. That is roughly
**12 door types against ~96 door instances**. A per-instance mark would put 96 unique numbers on the
drawings to describe 12 actual products; the schedule would have 96 rows where 12 would do, and any
change to a door type would need 8 edits. The type mark is what the contractor orders against.

**The codes, measured from `TagConfig.Defaults.cs`:**

| | Doors | Windows |
|---|---|---|
| PROD (`ASS_PRODCT_COD_TXT`) | **`DR`** | **`WIN`** |
| SYS (`ASS_SYSTEM_TYPE_TXT`) | `ARC` | `ARC` |
| FUNC (`ASS_FUNC_TXT`) | `FIT` | `FIT` |

Note **`WIN`, not `WN`** — three characters. And note that **FUNC is `FIT` for both**, so FUNC cannot
distinguish a door from a window. **PROD is the discriminator.** (FUNC is not `EXT`/`INT` at all —
`EXT` is a *LOC* code, from `DefaultLocCodes`.)

**The configuration.** Which tokens a container renders is set by a named **preset** in
`Data/PARAMETER_REGISTRY.json` → `token_presets`, referenced by a container's `tokens` field
(`ParamRegistry.cs:2092`, `ResolveTokenPreset:1733`). Token order is
`0 DISC · 1 LOC · 2 ZONE · 3 LVL · 4 SYS · 5 FUNC · 6 PROD · 7 SEQ`.

An existing container already gives a usable short mark:

- **`ASS_TAG_2_TXT`** uses preset `short_id` = `[0,6,7]` = **DISC-PROD-SEQ** → `A-DR-0001`.

If you want the bare `DR-0001` without the discipline letter, add a **new** preset rather than
repurposing `short_id` (which other containers rely on):

```json
"token_presets": {
  "all":      [0,1,2,3,4,5,6,7],
  "short_id": [0,6,7],
  "prod_seq": [6,7],
  "location": [1,2,3],
  "system":   [4,5],
  "sys_ref":  [4,5,6],
  "line1":    [0,1,2,3],
  "line2":    [4,5,6,7]
}
```

and point a container at it:

```json
{ "param_name": "ASS_TAG_4_TXT", "tokens": "prod_seq", "separator": "-" }
```

> **Correction to an in-code comment.** `TagFamilyCreatorCommand` describes `TAG4` as
> "Short label (PROD-SEQ)". It is not — `ASS_TAG_4_TXT` currently binds preset `system` = `[4,5]` =
> SYS-FUNC. The comment describes an intent the configuration never carried. Repointing `TAG_4` at
> `prod_seq` as above makes the comment true, but check nothing in your project is reading SYS-FUNC
> from `TAG_4` first.

**One caveat before you rely on this.** Changing a preset changes what the container *contains*. For
that to appear on a drawing, the tag family must have a **label bound to that container parameter**.
The universal tag carries `ASS_TAG_1`–`ASS_TAG_7` (`TagFamilyConfig.TagParams`), so `TAG_2` and `TAG_4`
are available — but whether a given tag *type* displays them is a family-authoring question, not a
configuration one.

**What is NOT automated — do not expect it.** Nothing in STING generates `DR-01`, `DR-02`, `WIN-01`
sequences per type. `ASS_TYPE_MARK_TXT` is only a **one-way mirror**: `ParameterHelpers.cs:3884` copies
an existing `ALL_MODEL_TYPE_MARK` from the type into the STING parameter. If the Type Mark is blank,
the STING parameter stays blank. **Type marks are entered by hand** (or via a schedule, which is far
faster — make a door type schedule with Type Mark as an editable column and fill it down). Registered
as **G-20**.

**The exception — unique assets show the full tag.** The short-mark rule applies to *repeat* items:
doors, windows, sanitaryware, furniture. **Unique plant does the opposite** — an AHU or a distribution
board is one-of-one, its tag *is* its identity, and it must show the full 8-segment code on the
drawing. Rule of thumb: if you would order more than one of it from a schedule, short mark; if it
appears once on a commissioning sheet, full tag.

### Tag depth — **not yet documented, deliberately**

> **PLACEHOLDER — do not fill this in from memory.**
>
> How many tiers of detail a tag shows (`TAG_PARA_STATE_1..10_BOOL`) is controlled either per tag
> **type** or per tag **instance**, and which one is correct for this project is an open decision as
> of 2026-08-10. The two answers imply different day-to-day procedures for the modeller, so writing
> either one now would put a wrong instruction in front of the team.
>
> Tracked in [`docs/UNIVERSAL_TAG_CONFORMANCE.md`](../docs/UNIVERSAL_TAG_CONFORMANCE.md) §C.
> Current evidence favours per-**type**. Until it is settled, set depth once for the project via
> **Tag Studio → Presentation Mode** and do not vary it per tag.

---

## Part 4A — Materials: exactly what to use and what to edit

### How the naming actually works

There are **two** names per material and they do different jobs:

| Column | Becomes | Example |
|---|---|---|
| `MAT_ISO_19650_ID` | Revit **Keynote** | `A-FLR-CEMENT-SCREED-50MM-INT-SC01` |
| **`MAT_NAME`** | **Revit `Material.Name`** — the name everything joins on | `STANDARD CEMENT SCREED 50MM` |

`MAT_NAME` is **ALL-CAPS free text**, loosely `<MATERIAL/TYPE> <QUALIFIER> <SIZE>`. It is not a controlled vocabulary — the same product appears as `HOLLOW CONCRETE BLOCK 8IN (200MM)` in one sheet and `BLOCK HOLLOW 200MM` in another. That inconsistency has consequences (below).

**Load them with:** `CreateBLEMaterials` and `CreateMEPMaterials` (`StingCommandHandler.cs:1338-1339`). They set Class, Description, Manufacturer, Model, Cost, Keynote, Mark, URL, appearance, thermal and structural assets — and skip any material whose name already exists.

### The three things a material name controls

1. **Carbon** — matched by **substring, first-hit-in-file-order** against `byKeyword` in `STING_CARBON_FACTORS_UG.json`, then by exact `MaterialClass`.
2. **Waste %** — matched by **substring, first-hit** against `WasteTable`'s 48 keywords… *on the carbon path only*. See the warning below.
3. **The bill description** — `BLE_APP-IDENTITY-CLASS` is prepended as the leading noun: *"Supply and fix **masonry** walls."*

So the rule is: **name a material so that the first keyword it hits is the one you meant.**

### ⚠ Before you price anything: the material library rates are broken

`ALL_MODEL_COST` is written from `MAT_COST_UNIT_USD` (e.g. `8.0`) but read as **UGX** with FX suppressed. Every library material therefore prices at roughly **1/3,700 of its real rate** — and because `MaterialLibraryRateProvider` sits at priority 95, above the correct category rate at 90, **the wrong number wins**.

**Measured, across all 1,279 rows:** `MAT_COST_UNIT_UGX` is exactly `MAT_COST_UNIT_USD × 3700` — every row in BLE, and 441 of 464 in MEP. The UGX column is **derived, not independent**. So the library's real price is USD at a frozen 2026 exchange rate, and the durable fix is to label it USD and let the FX layer convert — not to switch to the UGX column, which would bake a stale rate in permanently. A proposed price book that fixes this for every future project is authored at [`GUIDES/kibale-project-config/material_price_book.json`](kibale-project-config/material_price_book.json).

**For this project, do not use material-library rates at all.** Put your rates in:

`<project>\_BIM_COORD\rate_card.json` — keyed on the **exact Revit category name**, case-insensitive:

```json
[
  { "Category": "Walls",  "UnitRate": 68000,  "Currency": "UGX", "Unit": "m2", "Note": "200mm hollow block, 1:4 render both faces" },
  { "Category": "Floors", "UnitRate": 145000, "Currency": "UGX", "Unit": "m2" },
  { "Category": "Roofs",  "UnitRate": 92000,  "Currency": "UGX", "Unit": "m2", "Note": "makuti on eucalyptus purlins" }
]
```

For per-element precision use the priority-100 route: set `CST_RATE_SOURCE = "Override"` and `CST_UNIT_RATE_UGX` on the element.

### What the library already has for you

Genuinely Uganda-tuned, and better than you might expect:

- **Blockwork** — `HOLLOW CONCRETE BLOCK 4IN…10IN`, solid equivalents, `AAC BLOCK`, `INTERLOCKING BLOCK`, sized 400×200×200 and annotated *"MOST COMMON SIZE IN UGANDA"*
- **Screed** — `STANDARD CEMENT SCREED 50MM` (1:4), `HEAVY DUTY CEMENT SCREED 75MM` (1:3), `GRANOLITHIC SCREED 40MM`
- **Render** — `CEMENT SAND RENDER 1-2` through `1-6`, with cement content per m³
- **Roofing** — `IRON SHEET 26/28/30 GAUGE`, `BOX PROFILE`, `LONGSPAN`, `ZINCALUME`, `CUSTOM ORB`
- **Terrazzo** — in-situ, tiles, epoxy, skirting
- **Tiles, parquet, T&G timber ceilings, emulsion and weatherguard paints**
- Manufacturer `HIMA CEMENT`, standards `UNBS 822-1` / `US 28-2001` throughout

### What you must add — and the exact names to use

Missing entirely from the library: **murram, hardcore, makuti/thatch, eucalyptus, mvule**. Append to `StingTools\Data\BLE_MATERIALS.csv` after the last row — 72 comma-separated fields, header order from line 2, `MAT_NAME` must be unique. Copy the nearest existing row of the same family and edit.

**Use these names precisely** — each is chosen so the right keyword fires first:

| `MAT_NAME` to use | Why this exact string |
|---|---|
| `HARDCORE STONE FILL 150MM` | `"hardcore"` → waste 7.5 % ✔; `"stone"` → 90 kgCO₂e/m³ ✔ |
| `MURRAM COMPACTED FILL 200MM` | no carbon keyword exists — **you must also set `BLE_APP-IDENTITY-CLASS = Earth`** so `byMaterialClass["Earth"] = 40` fires, or add a `"murram"` keyword |
| `MAKUTI THATCH ROOFING 150MM` | **note "ROOFING", not "ROOF"** — `WasteTable` has `"roofing"` (7.5 %) and no `"roof"`. Add a `"thatch"` carbon keyword or it falls to class `Wood` = 160 |
| `EUCALYPTUS TIMBER POLE 100MM` | `"timber"` → waste 10 % ✔, carbon 160 ✔ |
| `MVULE HARDWOOD TIMBER 50MM` | `"hardwood"` (260) is checked **before** `"timber"` (160) — which is what you want for mvule ✔ |

Then add to `<project>\_BIM_COORD\carbon_factors_ug.json` (**the project override — do not edit the shipped file**), inserted *before* the generic entries because order is priority:

```json
{ "contains": "murram",     "perM3": 40,  "note": "lateritic gravel sub-base, locally won" },
{ "contains": "makuti",     "perM3": 55,  "note": "palm thatch" },
{ "contains": "thatch",     "perM3": 55 },
{ "contains": "mvule",      "perM3": 260, "note": "tropical hardwood" },
{ "contains": "eucalyptus", "perM3": 160 },
{ "contains": "hardcore",   "perM3": 90  }
```

### Three data fixes to make before you bill

1. **Rename the `09_WALL_CORES` block family.** `BLOCK HOLLOW 200MM` matches **no** carbon keyword (there is no bare `"block"`), so it falls to class `Masonry` = 250 instead of `concrete block` = 140 — a **79 % over-count**. Rename to `CONCRETE BLOCK HOLLOW 200MM` and do the same for its dense/lightweight/aircrete/solid siblings.
2. **Delete or rename the block family you are not using.** The two families price at UGX 2,220 (per block) and UGX 96,200–125,800 (per m²), and nothing records which unit is which. `MaterialNameCache` picks the first name it finds.
3. **`Generic` is 29 % of the library — 373 of 1,279 rows.** Every one bills as *"Supply and fix **generic** walls"* and resolves no carbon class. Nine class values (`Generic, Ceiling, Paint, Flooring, Plaster, Fabric, Carpet, Lining`) are not valid carbon keys, and `Ceiling`/`Flooring` are element types, not materials — they belong in `MAT_ELEMENT_TYPE`, which already exists. Overall **302 rows (23.6 %) resolve carbon at the flat 200 default**, and **438 rows (34 %) have zero density and zero carbon**. Fix the classes on the materials you will actually use before you issue a bill or a carbon report.
4. **Fix the two outright class errors.** Terrazzo, clay roof tiles, roofing felt and mineral-fibre ceiling tiles are all classed `Generic`, so they bill as *"Supply and fix generic roofs."* Set `Ceramic` / `Stone` / `Masonry` — all valid carbon-class keys. Also fix two outright errors: `LIGHTWEIGHT SCREED 40MM` is classed **`Metal`** (carbon 12,200 instead of 290 — **42×**) and `HERRINGBONE BLOCK PAVING 65MM` is classed **`Wood`**.

### Two behaviours to know

- **Material waste never reaches the price.** All three cost call sites pass `material = null`, so only the *category* keyword is tried. A tiled floor is carbon-counted at 10 % waste and priced at 5 %. Build the difference into your rate.
- **A rate miss is silent.** An unresolved material falls to a flat category rate — `Walls = 315,000/m²` — whatever the construction. Nothing warns you. Run `BOQ_RateGapReport` and read the provenance column.

---

## Part 4 — What makes a BOQ line correct

A bill line is correct when **six** things are true. Any one missing and the line is wrong, or silently absent.

1. **The element exists and is the right category.** A wall modelled as a generic model does not appear in a wall bill.
2. **The type is named as the description reads.** The type name is doing the work of the bill description. `Basic Wall 1` produces `Basic Wall 1` in your bill.
3. **The material is assigned** — on the layer, not just as a graphic override. Material drives the material take-off, the carbon factor, and the rate lookup.
4. **The classification is on the Type** (Uniclass `Ss`/`Pr`), so the line lands in the right bill section.
5. **The STING asset tag is complete** — all tokens resolved. An element with a broken tag is an element the validation pass will flag and the take-off may group wrongly.
6. **A rate resolves.** Every line needs a rate from the rate card, the material library, or an explicit manual override. A line with no rate is a hole in the price, and the rate audit exists to find them.

### Excluding modelled elements from the bill — your options today

You asked for flexibility here. The honest position: **there is no per-element BOQ opt-out in STINGTOOLS.** No `CST_EXCLUDE_BOOL`, no `ASS_BOQ_EXCLUDE`, no ExtensibleStorage suppression, and the cost panel's `DeleteRow` hard-returns on any row whose source is `Model` (`UI/BOQCostManagerPanel.cs:4888-4890`). Everything modelled in a billable category is billed.

What the collector actually filters on (`BOQCostManager.CollectCandidateElements:3325-3380`): CAD imports, non-primary design options, a hard-coded non-measurable category list, the `COST_TAKEOFF_EXCLUDE_CATEGORIES` config key, categories absent from the rate table, and Rooms/Spaces/Areas. Plus demolished elements, dropped one level up. **Not** filtered on: workset, view, phase-created, or any parameter.

**The eight routes available, ranked by how much I would trust them:**

| # | Route | Grain | Verdict |
|---|---|---|---|
| 1 | **`COST_TAKEOFF_EXCLUDE_CATEGORIES`** in `project_config.json` (comma-separated) | category, project-wide | **The one clean mechanism.** Use it for Entourage, Planting, Furniture, Mass, Site if you do not want them billed |
| 2 | **Non-primary design option** | element | Works, but it removes the element from the main model — only right if it genuinely is an alternative |
| 3 | **Phase Demolished** | element | Works, but it demolishes the element. Correct for the existing mid-site structure, wrong as a general trick |
| 4 | **Remove the category from the rate table** | category | Works by accident — `!knownCategories.Contains(cat)` — and is invisible to the next person. Avoid |
| 5 | `CST_PROVISIONAL_SUM = 1` | element | **Reclassifies, does not exclude.** Moves the row to the Provisional Sums sheet. Genuinely useful, just not for exclusion |
| 6 | Rate override to 0 via the panel | element | Row still appears, priced nil. Honest and visible — often the right answer |
| 7 | Model it as an in-place/generic category | element | Silently drops it. Do not |
| 8 | Delete the row after export, in Excel | bill row | Breaks the model↔bill link and does not survive the next refresh |

**My advice for this project:**

- Put `COST_TAKEOFF_EXCLUDE_CATEGORIES` in `project_config.json` now, listing `Entourage, Planting, Furniture, Furniture Systems, Casework, Mass, Specialty Equipment`. Add `Generic Models` **only if** you have not used it for the pool.
- For a one-off element you want visible but not priced, **use route 6** — override the rate to zero and put the reason in the Note. It leaves an auditable trace, which "excluded" never does.
- Never use route 3 or 4 to solve a bill problem. Both lie about the model.
- **Do not model temporary works, scaffolding or props** in the main model at all. That is the real answer — the tool bills what you model, so the exclusion decision belongs at modelling time, not at bill time.

**The gap to close** (K-4): a `CST_BOQ_EXCLUDE_BOOL` parameter honoured in `CollectCandidateElements`, plus an `Exclude` flag on `BOQModelOverride` — the sidecar at `_bim_manager/project_boq_model_overrides.json` that already survives refresh, re-open and restart, and already carries `RateUGX/RateUSD/NRM2Paragraph/Note/RateSource`. Adding one boolean to it would give a per-element, persistent, auditable exclusion with a reason field. The `StingValidatorSuppressionSchema` proves the pattern already works in this codebase.

### ⚠ Do not let formula-derived parameters into this bill

Step 7 of the tag pipeline evaluates 270 formulas and writes the results onto your elements. **Most of the ones that matter for a BOQ are broken, and every one of them fails by writing a zero.** Full evidence in the gaps register, Part G; the short version:

- **`lookup()` is not implemented.** 27 formulas call it. It resolves to 0 *and discards the rest of the expression*. That is all cement, sand, aggregate and water take-off, all block and brick counts, all paint and putty litres, tile adhesive, grout and plaster volume.
- **Quoted literals are stripped by the CSV reader**, so `A + "-" + B` produces `AB`, and a string comparison against `"Standard Response"` silently evaluates to `0 == 0` — true.
- **Unit conversion is applied by one of eight callers**, so the same parameter holds a different number depending on whether you last ran Master Setup or Batch Tag.
- **There is no screed formula, no screed parameter, and no skirting length parameter at all.**
- Every failure path — divide-by-zero, unknown identifier, unresolved function, partial context — returns `0`, and `0` gets written.

**What to do on this project:**

1. **Measure geometry, not formulas.** Let the takeoff rules read `HOST_AREA_COMPUTED`, `HOST_VOLUME_COMPUTED`, `Length` and solid volume directly off the elements. That path is sound.
2. **Ignore any `CST_S_*` / `BLE_FINISH_*` quantity parameter** you did not personally verify against a hand calculation.
3. **Screed and skirting are manual.** Screed volume comes from your screed floors' own volumes; skirting length from the room perimeters. Enter both as measured additions.
4. **Spot-check for zeros.** After the tag pass, schedule the quantity parameters and sort ascending. A block of exact zeros is the signature of this class of failure.

**The QA gate, in order:** `PreTagAudit` → `TagAndCombine`/`BatchTag` → `ValidateTags` → `BOQPrepForExport` → `BOQ_RateGapReport` → `BOQExportProfessional` → `BOQSnapshotSave`, then `BOQSnapshotCompare` at the next issue.

`BOQPrepForExport` is the real gate and it has published thresholds — compliance ≥ 80 %, container completeness ≥ 80 %, **zero stale elements**, BOQ data-quality ≥ 65, paragraph coverage ≥ 80 %, rate fill ≥ 90 %, zero critical warnings, placeholders < 5 % of tagged. Treat those as the definition of "ready to price".

### Three traps specific to this engine

**1. A failed quantity gives you a zero, not an error.** `TakeoffRule.FallbackQuantity` returns `1.0` for `each`/`item`/`nr`/`no`, and **`0.0` for everything measured — m, m², m³, kg**. So a wall whose area parameter does not resolve produces a line with a description, a rate, and a quantity of zero. It will not look broken; it will look cheap. Always sanity-check the total m² of walling and roofing against a hand calculation before issuing.

**2. Parameters that must be present or the row is mis-named.** Beyond geometry: `ASS_DISCIPLINE_COD_TXT` (missing → discipline `"X"` and the row groups wrongly), `ASS_PRODCT_COD_TXT` (drives the takeoff rule match, therefore unit *and* NRM2 section), `ASS_SYSTEM_TYPE_TXT` (used for the `Category|System` rate lookup), a real assigned **Material** (the dominant material by volume drives the description qualifier and the carbon factor), Level, and `ASS_LOC_TXT` or an enclosing Room. `ASS_BOQ_LINE_REF` is **write-once** — it is never overwritten once set, which is what keeps line references stable between issues.

**3. Descriptions come from a category-keyed template library, and unfilled tokens are visible.** `BOQ_DESCRIPTIONS.json` entries are keyed on **category** (not on section code, whatever the documentation says), each with a paragraph containing `[material]`, `[element_type]`, `[location]`, `[fixings]`, `[standard]` placeholders. Those resolve from your parameters — `[material]` from the dominant material, `[element_type]` from the type name, `[location]` from Room + `ASS_LOC_TXT`/`ASS_ZONE_TXT`/`ASS_LVL_COD_TXT`, and so on. Anything unresolved falls back to a generic *"Supply, deliver and install…"* sentence, which reads poorly in a client bill. There are three override layers — built-in, company (`%APPDATA%\STING`), and **project** (`<project>\_bim_manager\boq_custom_templates.json`). Write the lodge's own descriptions into the project layer once, early, and every issue inherits them.

The currency in the summary sheet is **hard-coded UGX** with USD derived at `UGX_PER_USD` (default 3700, settable in `project_config.json`). Set the rate you have agreed before the first export.

**Things that quietly corrupt a take-off:**
- Openings and voids: confirm whether your standard deducts them and above what threshold. Different standards give different nets for the same geometry.
- Waste factors: applied per material. Agree them with the QS; do not leave the defaults unexamined.
- Duplicate elements — two walls in the same place bill twice and are invisible in plan.
- In-place families — they escape most take-off rules. Avoid them; where unavoidable, flag them.
- Model groups whose instances have drifted — one edited instance means the quantities no longer match the drawing.
- **Linked-model quantities**: confirm your schedules are set to include linked elements, or seven cottages will simply not be in the bill.

---

## Part 5 — Roof, and why it is the project risk

Seven Ø11.59 m round pavilions plus a twin. The roof is:
- the largest single material quantity on the cottages
- the thing that determines wall heights, and therefore all the wall areas
- the thing that determines whether the 22°/45° radial grid means anything
- entirely absent from the information you have been given

**Do not model wall heights speculatively.** Get the roof design first, or model to an explicitly stated assumed height and label every affected quantity as provisional. If you guess, every wall area, every plaster area, every paint area and every roof area in the bill is wrong by the same unknown factor.

---

## Part 6 — Practical notes on this specific site

- **27.75 m of fall on a lodge site means the external works may cost more than one of the cottages.** Model retaining, steps, paths and surface drainage at the same LOD as the buildings, not as an afterthought.
- **Foul drainage runs downhill.** With the kitchen/dining at the high end (NE, ~1490+) and cottages spread down to ~1477, check the falls and the invert levels early — the septic/treatment location is a site-planning decision, not a services detail.
- **Water supply runs uphill.** Pressure and pump/tank location, likewise.
- **The surveyed trees are an asset.** `mango1-4`, `ovacado1-4`, the plantation strip — a lodge in Kibale sells shade and screening. Model the retained trees so they are in the setting-out drawing and cannot be cleared by accident.
- **The existing hatched structure** mid-site: retain, demolish, or convert? It changes the demolition bill and the phasing.
- **You have more buildings than STING's default location vocabulary allows, and extending it takes THREE config keys, not one.** `ASS_LOC_TXT` ships with `BLD1, BLD2, BLD3, EXT`. You need at least eleven codes (`COT01`–`COT08`, `STF`, `KDR`, `POOL`, plus `EXT`).

  There are three separate LOC vocabularies in the code and **they do not talk to each other**. Set all three in `project_config.json`, with identical content, before tagging anything:

  | Key | Who honours it |
  |---|---|
  | **`LOC_CODES`** | `TagConfig.LocCodes` — the tag writer, the Excel round-trip validator (`ExcelLinkCommands.cs:143`, a **hard fail**, case-sensitive), the token-writer UI, the published picklists |
  | **`CUSTOM_VALID_LOC`** | `ISO19650Validator` (`:161-179`) — **this is the only key `ValidateTags` accepts**, not `LOC_CODES_EXTRA` |
  | **`LOC_CODES_EXTRA`** | only `FederationReview` and `BuildingAwareCDEFolders` (`MultiBuildingCommands.cs:252`, `MultiBuildingExtraCommands.cs:90`) |

  Setting only `LOC_CODES_EXTRA` — which is what the code comment tells you to do — leaves the validator and the Excel importer rejecting your codes. It is also **not in `TagConfig`'s known-keys list**, so it logs an "unknown config key" warning on every load. Gap F-1.

- **⚠ Untagged elements are silently filed under your first building.** `TagConfig.cs:2295-2300`: when LOC is empty or `XX`, it is rewritten to `LocCodes.FirstOrDefault(c => c != "XX")`. On this project that means **every element STING cannot place lands in `COT01`** — with no warning. Cottage 1 will appear to cost more than the identical cottages 2–7 and you will spend a day looking for the difference. Run `PreTagAudit` and check the LOC distribution before you believe any per-building cost split.

- **Do not let `BuildingCodeSeed` name your levels.** It produces `BLD2-L01-FFL`. That string is then re-parsed by `GetLevelCode`, which does not recognise the prefix, falls through to "extract the digits", gets `201`, and returns **`L201`** (`ParameterHelpers.cs:483-571`). The seeder's own output defeats the parser. Name levels so the parser can read them — `Level 01`, `Ground`, `Roof`, `Basement 1` — and carry the building code in the model/LOC, not in the level name.
- **`BuildingCodeSeed`** will generate per-building levels and grids named `<CODE>-GF-FFL`, `<CODE>-L01-FFL`, `<CODE>-1..N` / `<CODE>-A..`. Useful for the staff block and kitchen/dining; for the cottages, the linked-model strategy already gives you clean per-building namespaces, so seeding is optional.
- **`BuildingAwareCDEFolders`** creates `<state>\<LOC>\{MODELS,DRAWINGS,SCHEDULES,BOQ,COBie,REPORTS}` per CDE state — run it after you have settled the LOC vocabulary, and you get per-cottage issue folders for free.
- **Phases are audit-only.** The phase-creation step is labelled as such in the code because of an API limitation; create phases in Revit yourself. Demolished elements are correctly excluded from both tagging and the BOQ, so use a demolition phase properly if the existing mid-site structure is coming down.
- **The swimming pool has no takeoff rule.** With no rule matching it, `DeriveNrm2Section` falls through to the architectural discipline default, section **23 — Building fabric sundries**. A pool is not a sundry. Author a project takeoff rule for it in `_BIM_COORD\takeoff_rules.json` (project rules are prepended and win over corporate ones), or it will land in the wrong bill section every time.
- **Rotation.** Every cottage sits at its own angle. Setting-out on site will be by coordinate, not by offset from a boundary — which is another reason the survey must be in native coordinates.

---

## Part 7 — RFI list to issue now

| # | Query | Blocks |
|---|---|---|
| RFI-01 | What does `/pvo` mean in the door and window codes? | Door & window schedules, bill descriptions |
| RFI-02 | Survey in DWG/CSV with coordinate system stated | Everything. Highest priority |
| RFI-03 | Sections, elevations, roof design for the typical cottage | Cottage model, all wall/roof quantities |
| RFI-04 | Outline specification: wall build-ups, slab, screed, roof covering | Type naming, BOQ, rates |
| RFI-05 | Finished floor level intended for each of the 8 cottage positions | Setting out, platforms, cut/fill |
| RFI-06 | Existing structure mid-site — retain / demolish / convert? | Demolition bill, phasing |
| RFI-07 | Scale note says 1:300 on A3 but sheets are A0 — confirm | Any dimension taken off the sheet |
| RFI-08 | Which measurement standard will the QS bill to? | BOQ structure — Part 1, D7 |
| RFI-09 | Pool: shell construction and plant specification | Pool package, PC sum |
| RFI-10 | Are the seven cottages genuinely identical above FFL? | Whether the link strategy holds |

---

## Part 8 — Tool gaps found during this review

Logged here, not fixed — this session is advisory.

1. **No East African / AAQS Standard Method of Measurement.** `StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs` implements `Nrm2Standard`, `Cesmm4Standard`, `PomiStandard`, `Icms3Standard`, `MmhwStandard`. For East African work the regional standard is the *SMM of Building Works for Eastern Africa (2nd ed., 2008)* / the AAQS African SMM. POMI is a workable stand-in, but a native `SmmEaStandard` implementing `IMeasurementStandard` would be a genuinely differentiating addition for the Uganda/Kenya/Tanzania market — which is STINGTOOLS' home market.

2. **`CLAUDE.md` names BOQ command tags that do not exist.** It lists `BOQ_RateAudit`, `BOQ_Validate`, `BOQ_DeltaReport`. The real dispatch table in `StingTools/UI/StingCommandHandler.cs:3551-3579` has `BOQRefresh`, `BOQExport`, `BOQExportProfessional`, `BOQExportIfcQto`, `BOQQsExport` / `BOQQsImport`, `BOQ_RateGapReport`, `BOQSnapshotSave` / `BOQSnapshotCompare`, `BOQ_SignOff`, `BOQ_LabourRollup`, `BOQ_CarbonGapReport`, `BOQAddManualRow`, `ReconcileProvisionals`, `BOQWriteItemParams`, `BOQRateHeatMap`, `BOQPrepForExport`. Anyone following the documentation will look for buttons that are not there.

3. **`TakeoffRule.FallbackQuantity` returns 0 for every measured unit.** `BOQ/Takeoff/TakeoffRule.cs:224-232` — `each`/`item`/`nr`/`no` fall back to `1.0`, `m`/`m²`/`m³`/`kg` fall back to `0.0`. A silent zero on a measured line is worse than a loud failure: the row still carries a description and a rate, so it reads as a real, cheap item. It should at minimum log a warning and mark the row's confidence, so `BOQPrepForExport` can gate on it.

4. **Two Uniclass parameter sets that never meet.** `UniclassClassify` writes `ASS_CLASS_COD_TXT` / `ASS_CLASS_DESC_TXT` from a **21-entry dictionary hard-coded in `Temp/StandardsEngine.cs`** — not from a data file, and not extensible without a rebuild. But `Core/Classification/ClassificationReader.cs` — the canonical resolver used by BOQ, COBie, handover and IFC export — reads `UNICLASS_PR_TXT`, `UNICLASS_SS_TXT`, `UNICLASS_EF_TXT`. **The automatic command does not populate the parameters the reader consumes.** So running "Uniclass classify" gives you classification data that the BOQ never sees, and the fallback chain drops through to `Native.Family`. Either the writer should target the reader's parameters, or the 21-entry map should move to `Data/` and be extended to the reader's schema.

5. **Documentation drift beyond the BOQ tags.** `CLAUDE.md` states the project rate card is `_BIM_COORD/boq_rate_card.json`; the code reads **`_BIM_COORD/rate_card.json`** (`Rates/Providers/ProjectRateCardProvider.cs:50`). It describes the rate chain as "BCIS → project rate card → material library → manual override"; the actual priority order is the reverse — manual override (100) → ES override (95) → material library (95) → CSV (90) → project rate card (87) → COBie (75) → default (60), with BCIS lazily registered elsewhere. It says `BOQ_DESCRIPTIONS.json` is "keyed by section code"; it is keyed by **category**. And `BOQSupportCommands.cs` is 984 lines, not 506. These are the kind of errors that send a modeller looking for a file that is not there.

6. **`MultiBuilding_*` command tags do not exist.** `CLAUDE.md` lists `MultiBuilding_SetBldgCode`, `MultiBuilding_AuditCodes`, `MultiBuilding_SyncTags`, `MultiBuilding_Export`. The real tags are `BuildingCodeSeed`, `PrjVolumeCodeAuto`, `SeqRangeValidation`, `BuildingAwareCDEFolders`, `FederationReview`.

7. **No topography or site-modelling command at all.** For a lodge on a 27.75 m slope, cut/fill and platform setting-out are first-class deliverables. Toposolid quantities are currently entirely a hand-rolled Revit schedule; there is no STING command to stamp platform cut/fill onto a BOQ row. Given STINGTOOLS' market is East African hillside sites, this is a conspicuous hole.

---

## Appendix — verified command tags (read from source, not from documentation)

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
| Folder health / migrate / open | `FolderHealth`, `FolderMigrate`, `OpenProjectFolder` | — |
| Per-building CDE folders | `BuildingAwareCDEFolders` | `Core.BuildingAwareCDEFoldersCommand` |
| Seed per-building levels + grids | `BuildingCodeSeed` | `Core.BuildingCodeSeedCommand` |
| Volume code from filename | `PrjVolumeCodeAuto` | writes `PRJ_VOLUME_CODE` |
| Federation review across links | `FederationReview` | `FederationCoordinationReviewCommand` |
| Scope-box dry run / generate | `DrawingTypes_SuggestFromScopeBoxes`, `DrawingTypes_FromScopeBoxes` | `Commands.Drawing.*` |
| Create AEC filters in model | `AecFilters_Create` (`_Inspect`, `_Reload`) | `Commands.Drawing.AecFilters*Command` |
| Uniclass / CSI | `UniclassClassify`, `CSI_Assign` | see gap 4 |
| Reload takeoff + measurement rules | `Cost_ReloadRules` | `Commands.Cost.*` |

### Where the buttons live (dock panel tabs)

| Tab | Section | What is there |
|---|---|---|
| **SETUP** | ⚙ SETUP | `ProjectSetup` wizard |
| **SETUP** | Workflow automation | `AutoTaggerToggle` |
| **SETUP** | Batch & export | `BOQExport` |
| **CREATE TAGS** | ⚙ SETUP | `LoadSharedParams`, `PurgeSharedParams` |
| **CREATE TAGS** | ⚙ Quality assurance | `ValidateTags` |
| **TAGGING** | Data tagging (ISO 19650) | `AutoTag`, `BatchTag`, `TagAndCombine`, `PreTagAudit`, `TagNewOnly`, `ReTag` |
| **DOCS** | 📐 **DRAWING TYPES** (`StingDockPanel.xaml:1413`) | `DrawingTypes_Editor`, `_Inspect`, `_Reload`, `_PresentationSetup`, `_SuggestFromScopeBoxes`, `_FromScopeBoxes`, `_ExportExcel`/`_ImportExcel`, + "Advanced drawing-type ops" expander, + Production sub-panel |
| **DOCS** | Documentation automation → Manual view/sheet creation → Management (`:1709`) | `ScopeBoxManager`, `ViewTemplateAssigner`, `ProjectBrowserOrganizer` |
| **MODEL** | Plaster, render, paint & coatings (`:2774`) | `CoveringSmartApply`, `CoveringBatchApply`, `CoveringRoomSchedule` + 8 advanced |
| **MODEL** | Multi-building site (§B1-B6) (`:2557`) | `BuildingCodeSeed`, `PrjVolumeCodeAuto`, `SeqRangeValidation`, `BuildingAwareCDEFolders`, `FederationReview` |
| **BIM** | Folder + setup ops (`:3110`) | `CreateFolders`, `OpenProjectFolder`, `FolderHealth`, `FolderMigrate` |
| **BIM** | 5D — cost estimation (`:3185`) | `BOQCostManager` panel, `BOQExportProfessional`, `BOQPrepForExport`, and the whole BOQ/Cost family |
| **BIM** | CSI / SpecLink (`:3682`) | `CSI_Assign`, `SpecLink_Reconcile` |
| **INTEROP** | Data pipeline — formats | `BOQExport` |

### Files you author by hand for this project

| Path | Purpose |
|---|---|
| `project_config.json` (beside the `.rvt`) | `LOC_CODES_EXTRA` for the 11+ building codes, `UGX_PER_USD`, `FOLDER_CODE_SUFFIX`, `WRITE_COST_ON_TAG` |
| `<project>\_BIM_COORD\takeoff_rules.json` | project takeoff rules — **prepended** over corporate, needed for the pool and any bespoke element |
| `<project>\_BIM_COORD\rate_card.json` | the project rate card (**not** `boq_rate_card.json`) |
| `<project>\_bim_manager\boq_custom_templates.json` | project bill descriptions |
| `<project>\_BIM_COORD\drawing_types.json` | project drawing-type overrides |

---

## Sources consulted

- [Extracting BOQ from a Revit model](https://medium.com/@Desapex/extracting-boq-from-revit-model-an-in-depth-exploration-53e3eb0df796)
- [Revit schedules & material take-offs](https://us.getrenewedtech.com/2025/08/24/revit-schedules-and-quantities-extracting-accurate-material-takeoffs-from-your-bim-model/)
- [BIM-based BOQ generator following POMI and NRM2](https://www.researchgate.net/publication/342625082_BIM-Based_Bill_of_Quantities_Generator_following_POMI_and_NRM2_Methods_of_Measurement)
- [Standard Method of Measurement for Eastern Africa](https://goldberry.co.ke/newsroom/standard-method-of-measurement-eastern-africa)
- [AAQS Standard Method of Measuring Building Work for Africa](https://aaqs.org/wp-content/uploads/2020/05/StandardMethodsAAQS.pdf)
- [Uniclass 2015 classification](https://rebim.io/classification-systems-uniclass-2015/)
- [Model groups vs linked models — decision rules](https://novedge.com/blogs/design-news/revit-tip-model-groups-vs-linked-models-revit-decision-rules)
- [Revit links vs groups](https://www.modelical.com/en/gdocs/revit-links-vs-groups-which-is-better/)
- [Revit 2026 toposolid enhancements](https://www.manandmachine.co.uk/revit-2026-toposolid-enhancements/)
- [Calculating cut and fill for toposolids](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-calculate-cut-and-fill-for-toposolids.html)
- [Scheduling volumes of elements excavating a toposolid](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-schedule-volumes-of-individual-elements-cutting-or-excavating-the-Toposolid-in-Revit.html)
- [Accurate material take-off with stacked walls](https://chxnathanaels.home.blog/2024/09/24/revit-tips-1-accurate-material-takeoff-with-revit-stacked-walls/)
- [ISO 19650 for Revit teams](https://www.s15studio.com/post/revit-iso-19650-standard-explained)
- [Federation strategy](https://integratedprojectdesign.com/ddd/federation-strategy/)
