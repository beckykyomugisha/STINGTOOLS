# STING — Build a Real Working Test Template (symbols · title blocks · wire annotation · SLD)

Goal: a single Revit template (`.rte`) that, when opened by your one tester, has
**real families loaded** — tag families, MEP/SLD symbols, title blocks, wire
annotation tags — so you can test **accuracy** (do the numbers/tags come out
right?) instead of debugging missing-family blanks.

This is written for the one-machine, one-helper reality. Do it once on the
machine that has STING installed, save the `.rte`, and reuse it on every job.

---

## 0. The single root cause of "it looks like stubs"

STING **generates most families at runtime** from JSON/spec files instead of
shipping hundreds of `.rfa`. Two generators do the heavy lifting:

| Generator | Command (button) | Produces |
|---|---|---|
| Symbol Library | **TAGS tab → Symbols → ★ Create All** (`Symbols_CreateAll`) | SLD + MEP + fire + plumbing + earthing symbols → `<project>/_BIM_COORD/Families/Symbols/…` |
| Title Block Factory | **`TitleBlock_CreateAll`** | Assembly + authority title blocks |

**Both silently produce ZERO families if Revit's Family Template path is not
set.** That is the number-one reason a "finished" setup renders blank. Fix this
FIRST:

> Revit → **Options → File Locations → Places / Family Template Files** →
> point at the folder that contains `Metric Generic Annotation.rft`,
> `Metric Detail Item.rft`, and `Metric Title Block.rft`
> (usually `C:\ProgramData\Autodesk\RVT 2025\Family Templates\English`).

If that folder is missing, the templates ship with Revit — re-run the Revit
installer's "Family Templates" content, or copy the `English` folder from the
tester's machine. **Nothing below works without this.**

---

## 1. What is REAL vs GENERATED vs MANUAL (the honest gap map)

| Layer | State in repo | How you get it |
|---|---|---|
| **Tag families** (label tags) | ✅ 206 real `.rfa` in `StingTools/Data/TagFamilies/` | Already there — load into template |
| **SLD symbols** (IEC/IEEE/BS/NFPA/CIBSE) | ⚙ JSON only (`Data/Symbols/STING_SLD_SYMBOLS*.json`) | Run **★ Create All** — needs Generic Annotation `.rft` |
| **MEP plan symbols** | ⚙ JSON (auto) + 17 that need hand-authoring | Run **★ Create All**, then author the 17 below |
| **Title blocks** (assembly + KCCA/ERA/NEMA) | 📝 param specs only (`Families/AssemblyTitleBlocks/*.params.txt`) | Run `TitleBlock_CreateAll` — needs Title Block `.rft` |
| **Wire annotation tag** | 📝 spec only (`Families/Annotation/STING_WIRE_ANNOTATION_TAG.params.txt`) | Author 1 `.rfa` by hand (below) |
| **Matchline tag** | 📝 spec only | Author 1 `.rfa` by hand |
| **MedGas equipment families** | 📝 spec only | Manufacturer `.rfa` (Beaconmedaes/GCE/etc.) — skip unless doing healthcare |
| **Seed MEP model families** (AHU/socket/luminaire 3D) | ⚙ built as **2D symbolic placeholders**, not 3D | `Symbols_BuildSeeds`; real 3D comes from manufacturers |

Key truth to hold onto: **STING is an organiser/engine, not a family vendor.**
The tags, calcs, schedules, BOQ and SLD logic are real. The 3D geometry of MEP
kit is deliberately not shipped. For *accuracy* testing that's fine — you test
whether STING reads/derives/tags/schedules correctly, using placeholder or your
own manufacturer geometry.

---

## 2. Build the template — do this in order (~60–90 min, once)

### Step A — Fix family template path (§0). Do not skip.

### Step B — Start from a real project, not a blank template
Open an actual small test model (a few walls, a room, a handful of MEP
elements per discipline). You need real elements to test accuracy against.

### Step C — Load shared parameters
DOCS/CREATE tab → **Load Shared Params** (`LoadSharedParams`). This binds the
`ASS_*`, `ELC_*`, `PLM_*`, `HVC_*` parameters. Without this, tags read blank
and every calc writes to nothing.

### Step D — Generate the symbol library
TAGS tab → Symbols → **★ Create All**. Watch the build report:
- It **names any catalogue that produced 0 families** and prints the exact
  fix. If you see zeros → family template path (§0) is still wrong.
- Success = `.rfa` appear under `<project>/_BIM_COORD/Families/Symbols/SLD/…`,
  `/HVAC/…`, `/ELEC/…` etc.

> Tip: set env var `STING_SYMBOL_LIB` (or `symbol_library_root` in
> `%APPDATA%/STING/sting_symbols.json`) to a network/shared folder so you build
> symbols **once** and every future project reads them — no per-project rebuild.

### Step E — Generate title blocks
Run `TitleBlock_CreateAll`. If title-block `.rft` is missing you'll get
errors naming it. On success the assembly + KCCA/ERA/NEMA title blocks load.
(If missing at sheet time, `ShopDrawingComposer` falls back to the first title
block in the project so the pipeline still runs — but you want the real ones.)

### Step F — Load the tag families you actually use
Load the relevant `.rfa` from `StingTools/Data/TagFamilies/` for your
disciplines (Door, Air Terminal, Cable Tray, Conduit, Communication Device,
etc.). You don't need all 206 — load per discipline you're testing.

### Step G — Author the 3 hand-made annotation families (§4)

### Step H — Save As Template (`.rte`)
This is your reusable "STING test bed". Save a copy to the shared symbol
location too.

---

## 3. Then test ACCURACY (not stubs) — smoke-test order

Run these against real elements and check the OUTPUT is correct, not just that
the command runs:

1. **Tagging** — CREATE → `TagAndCombine` on a selection → confirm the
   8-segment tag `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` is correct for each
   element (right discipline letter, right system code, sequential SEQ).
2. **Validate** — `ValidateTags` → the report should reconcile with what you
   see. Mismatches = config gap, not a family gap.
3. **Schedules/BOQ** — run a BOQ export; confirm quantities and rates are
   sane against a hand-take-off of 3–4 elements.
4. **Electrical** — Circuit a small board → Cable Sizer → Voltage Drop →
   check a cable size by hand against BS 7671 tables for one circuit.
5. **SLD** — `SLD_Generate` on that board → symbols should render (proves
   Step D worked) and topology should match the circuits.
6. **Wire annotation** — annotate one circuit → confirm the wire tag reads
   the circuit's conductor data.
7. **Sheets** — `CreateFromTemplate` → real title block populates from
   project info.

Anything that renders **blank** → family/symbol not loaded (go back to §2).
Anything that renders **wrong values** → real accuracy bug worth reporting.
Keep those two categories separate — that's the whole point of testing.

---

## 4. Families you must author by hand (no generator)

Author from `Metric Detail Item.rft` (symbols) or the matching tag template,
following the parameter contract in the `.params.txt` files. Priority:

**Two annotation tags (do these — they block wire/matchline testing):**
- `STING_WIRE_ANNOTATION_TAG.rfa` — spec: `Families/Annotation/STING_WIRE_ANNOTATION_TAG.params.txt`
- `STING_TAG_MATCHLINE.rfa` — spec: `Families/Annotation/STING_TAG_MATCHLINE.params.txt`

**MEP plan symbols not covered by JSON (author as time allows, in this order —
highest frequency on real drawings):**
`STING_SYM_HVAC_AHU_PLAN`, `_FCU_PLAN`, `_SAD_SQ_PLAN`, `_SAD_RND_PLAN`,
`_RAG_PLAN`, `_EAG_PLAN`, `STING_SYM_PIPE_PUMP_PLAN`, `_CALORIFIER`,
`STING_SYM_ELEC_PANEL_PLAN`, `STING_SYM_LTG_DOWNLIGHT`, `_STRIP`, `_EMRG`,
`STING_SYM_FP_SPRINKLER_PEND`, `_DETECTOR_SMOKE`, `_MCP`,
`STING_SYM_PLM_WC_FLOOR`, `_BASIN_WALL`.
Name them **exactly** as the `family_filename` column in
`Data/MEP/STING_MEP_SYMBOLS_INDEX.csv` or `MepSymbolEngine` won't find them.

**Vendor / manufacturer families:** when you drop in a real manufacturer
`.rfa`, run **`FamilyConformanceCheck`** first — it scores the `.rfa` folder
0–100 against the STING contract (PASS ≥85 / WARN 70–84 / BLOCK <85) so you
know it'll tag correctly before you bulk-stamp.

---

## 5. About "I only filled asset tag 1"

There are **53 tag containers** (`ASS_TAG_1_TXT` … up to the full set). You do
**not** need to configure them all to test.

- `ASS_TAG_1_TXT` is the **canonical/primary** container — the pipeline builds
  it first and everything (validation, SLD, BOQ back-refs) keys off it.
- The other containers are discipline-specific display copies. The tag pipeline
  auto-writes them via `WriteContainers` — you don't hand-fill them.

**So one container is enough to start real testing.** Configure the extra
containers only for disciplines where you want a differently-formatted display
tag. Don't let this block you — move to §2.

---

## 6. The one-helper workflow

Because only one machine has STING:
1. **You** iterate config/JSON on the dev machine (no Revit needed for JSON).
2. **Tester** pulls the branch, runs §2 once, saves the `.rte`, and works from
   it. Rebuilding symbols is only needed when a symbol JSON changes.
3. Put the built symbol library on a shared/network folder (§2 Step D tip) so
   the tester never rebuilds unless catalogues change.
4. Report findings as **blank (missing family)** vs **wrong (accuracy bug)** —
   two different fix paths.

---

### Quick reference — commands to run (in order)
`LoadSharedParams` → `Symbols_CreateAll` → `TitleBlock_CreateAll` →
load tag `.rfa` → author 2 annotation tags → `TagAndCombine` →
`ValidateTags` → `SLD_Generate` → `CreateFromTemplate`.
