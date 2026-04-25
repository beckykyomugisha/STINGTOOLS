# STING Tools — Tagging Workflow Guide (Plain English)

> **Who this is for:** BIM coordinators, modellers, and reviewers who tag elements day-to-day.
> **What this is:** A step-by-step walkthrough — in plain language — from *"I've just started a project"* through to *"we're handing over to the client"*.
> **What this is NOT:** The full technical reference. For parameter lists, formulas, and pipeline internals see `StingTools/Data/TAGGING_GUIDE.md`.

---

## What is a "tag" and why do we care?

In Revit, a **tag** is a label you stick on a model element — a door, a radiator, a pump — so the drawing can name it. STING's job is to make every tag:

1. **Unique** in the project (no two pumps called "P-01"),
2. **Consistent** with ISO 19650 (the British BIM standard the client's FM team will read), and
3. **Rich** — so the tag also carries spec, location, status, cost, carbon, etc. — ready for COBie and handover.

Every STING tag follows the same 8-segment shape:

```
DISC - LOC - ZONE - LVL - SYS - FUNC - PROD - SEQ
  │     │     │     │     │     │      │     │
  │     │     │     │     │     │      │     └─ 0001…9999 (unique number)
  │     │     │     │     │     │      └────── AHU, DR, WC… (product code)
  │     │     │     │     │     └───────────── SUP, HTG, PWR… (function)
  │     │     │     │     └─────────────────── HVAC, DCW, LV… (system)
  │     │     │     └───────────────────────── GF, L01, B1… (level)
  │     │     └─────────────────────────────── Z01, Z02… (zone)
  │     └───────────────────────────────────── BLD1, EXT… (building/location)
  └─────────────────────────────────────────── M, E, P, A, S… (discipline)
```

Example: `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003` reads *"third Mechanical Supply Air Handling Unit in Zone 01, Level 2, Building 1"*.

**Why bother?** Because on a 50,000-element model you cannot name things by hand. The 8 segments are the only way the client's Computer-Aided Facility Management (CAFM) software will later find *this specific pump* out of the thousand on site.

---

## When do you tag? — The project timeline

| RIBA Stage | What's happening | What STING does |
|---|---|---|
| **0–1 Strategy / Brief** | No model yet. Team setup. | Run **Project Setup Wizard** once. |
| **2 Concept** | Massing, zoning. | Skip tagging. Start labelling rooms/zones. |
| **3 Developed Design** | Systems routed. | First **Batch Tag** run. Expect 60–75% compliance. |
| **4 Technical Design** | Products selected. | Weekly tagging + issue resolution. Target 85%+. |
| **5 Manufacturing/Construction** | Subcontractor shop-drawings. | Tag as-built updates. 95% compliance for handover. |
| **6 Handover** | COBie, O&M manuals. | **COBie Export**, **FM Handover Manual**. |
| **7 In Use** | Client FM. | Model frozen. |

**Rule of thumb:** tag once the element's *position and type are stable*. Tagging too early wastes effort — tokens change as design shifts.

---

## Part 1 — Getting Ready (do these once per project)

### Step 1.1 — Run Project Setup

- **Dockable panel → TEMP tab → Project Setup Wizard** (the one with a star ⭐).
- Seven pages. Fill in project name, number, BIM coordinator, stage, disciplines.
- *Why:* this creates the shared parameter file, folder structure, BEP skeleton, and `project_config.json` everyone on the team will share.
- *When:* day one of the project. Never again unless the brief changes.

### Step 1.2 — Load Shared Parameters

- **Panel → TAGS → Setup → Load Params**.
- *Why:* binds STING's ~2,360 shared parameters to all relevant categories (walls, doors, pipes, …). Without this step elements have nowhere to store tag data.
- *When:* immediately after Project Setup. Re-run any time you upgrade STING.

### Step 1.3 — Create/Load Tag Families (the .rfa files)

- **Panel → TAGS → Setup → Create Tag Families** (first time ever) *or* **Load Tag Families** (use the pre-built ones).
- *Why:* every Revit category that can be tagged needs its own tag family (.rfa). STING ships 137 of them.
- *When:* once per Revit installation. If you see *"Family not loaded"* warnings, re-run **Load Tag Families**.

### Step 1.4 — Configure defaults

- **Panel → TAGS → Setup → Configure** → open `project_config.json`.
- The handful of keys you might touch:
  - `TAG_FORMAT.separator` — "-" by default; some firms use "."
  - `CATEGORY_SKIP` — categories you never want tagged (e.g., Entourage)
  - `DISCIPLINE_LEADS` — map DISC code to the person issues get auto-assigned to
  - `COMPLIANCE_GATE_PCT` — below this % STING warns before allowing handover operations
- *Why:* these tune STING to your office's conventions.
- *When:* once at project kick-off, again if standards change.

---

## Part 2 — Before You Tag (what makes tagging easy later)

### Model hygiene is half the battle

STING reads the model to *derive* most tokens automatically. If the model is tidy, tagging is free. If it's messy, every tag needs babysitting.

Do these BEFORE your first batch tag:

1. **Name rooms** — Room → Name and Number filled. LOC and ZONE come from room data. An unnamed room means blank tokens.
2. **Name levels sensibly** — `L01 Ground`, `L02 First`, not `Level 01 (1)`. STING's LVL-code detector wants GF/L01/B1/RF style names.
3. **Place elements inside rooms** — free-floating ducts can't inherit spatial context.
4. **Assign MEP systems** — a pipe with no *system type* can't derive SYS/FUNC tokens.
5. **Set phases** — *Existing / New Construction / Demolished*. STATUS token comes from here.
6. **Fill Project Information** — Project Number, Name, Status, Revision, Organization. Several tokens cascade from these.

### Pre-flight sanity check

- **Panel → TAGS → More → Pre-Tag Audit**
- A *dry run* — nothing is modified. STING shows you what tags it WOULD create if you ran Batch Tag now, plus the collisions, missing rooms, invalid ISO codes, placeholder risk, and a per-discipline forecast.
- *Why:* spotting 200 empty LOC tokens here is much cheaper than re-doing 10,000 tags later.
- *When:* always. Before every large tag run.

---

## Part 3 — Your First Tag Run

You have three "big tagging" buttons. Pick the one that matches your situation.

| Button | What it does | When to use |
|---|---|---|
| **Auto Tag** | Tags everything **visible in the active view**. Fast, focused. | Daily drafting — you're working on Level 2 RCP and want its ceilings tagged. |
| **Batch Tag** | Tags every taggable element **in the whole project**. Slow, exhaustive. | First big run. Weekly catch-up. Pre-milestone sweeps. |
| **Tag & Combine** | Auto-detects everything + writes ALL 53 discipline containers + TAG7 narrative. The nuclear option. | Project kick-off, pre-handover, when compliance has drifted badly. |

### What actually happens when you press the button

STING runs an 11-step pipeline on every element (`TagPipelineHelper.RunFullPipeline`):

1. Is this category on the skip list? → skip.
2. Copy any tokens already set on the **family type** down to this instance.
3. Lock any tokens the user has pinned (nothing below can overwrite them).
4. Auto-fill the 8 source tokens from spatial/system/category data.
5. Apply per-category overrides from `project_config.json`.
6. Map 30+ Revit built-in properties to STING params (Width, Flow, Voltage…).
7. Evaluate 199 formulas in dependency order (cost, carbon, area, pressure drop).
8. Assemble TAG1 (the 8-segment tag) with **collision resolution**.
9. Write TAG1 into 53 discipline containers (HVC_EQP_TAG, ELC_EQP_TAG…).
10. Build the 6-section TAG7 narrative (A-F).
11. Detect nearest grid and write GRID_REF.

### Collision modes — the first dialog you see

When TAG1 is about to be written, STING checks if that exact string already exists on another element. If it does, you pick:

- **Skip** — leave the existing tag, the new element stays un-numbered. Safest for "top-up" tagging.
- **Overwrite** — wipe the previous SEQ and renumber. Rare.
- **Auto-increment** — bump SEQ by 1 until unique. **This is the usual choice.**

### Reading the result panel

After any tag run you get a panel with 4 buckets:

- **✅ Fully resolved** — 8 segments, no placeholders, STATUS and REV populated. Ready for COBie.
- **🟡 Complete with placeholders** — 8 segments but containing `GEN`, `XX`, `ZZ`, or `0000`. Needs follow-up.
- **🟠 Incomplete** — fewer than 8 segments. Missing data.
- **⚪ Untagged** — category wasn't touched or was on skip list.

Target: **Fully resolved ≥ 85%** by Stage 4, **≥ 95%** by handover.

---

## Part 4 — Dealing with Placeholders

Placeholder tokens are STING's way of saying *"I could not derive this value, here's a stub so the tag still has 8 segments."*

| Token | Placeholder means | How to fix |
|---|---|---|
| DISC | No discipline mapping for this category | Add the category to `project_config.json → CATEGORY_DISCIPLINE` |
| LOC = **XX** | Element isn't in a room, no project-level default | Name the room, OR set Project Info → Location |
| ZONE = **ZZ** | Room has no Department / zone name | Fill Room *Department* parameter (e.g. "Z01") |
| LVL = **XX** | Level couldn't be code-converted | Rename the level to standard form (L01, GF, B1, RF) |
| SYS = **GEN** | Not in an MEP system; category fallback | Connect MEP system, or add category to `CATEGORY_FORCE_SYS` in config |
| FUNC | SYS couldn't map | Usually follows SYS fix; otherwise manual via Tokens panel |
| PROD | Unknown family name | Rename family (STING reads keywords), or map via `GetFamilyAwareProdCode` |
| SEQ = **0000** | Assignment never ran | Re-run **Assign Numbers** |

### The two tools that resolve placeholders

- **Fix & Resolve All** (**Panel → TAGS → QA → Resolve All Issues**) — walks the whole model, re-populates, re-validates. Takes minutes on large models — it's batched and cancellable.
- **Anomaly Auto-Fix** (**Panel → ORGANISE → Tag Ops → Fix Duplicates**) — tighter scope: bad SEQ numbers, tokens that don't match their category (e.g. a door tagged with SYS=HVAC), empty marks, stale flags.

---

## Part 5 — Placing the Visual Tag on the View

There's a difference between **data tags** (parameters on the element) and **visual tags** (the text you see on a drawing).

Everything above deals with data. You still need to put text on the sheet.

### The easy way — Smart Place

1. Activate the view (plan, section, 3D — all work).
2. **Panel → TAGS → Smart Place Tags**.
3. Pick scope: active view, selection, or project.
4. STING uses `TagPlacementEngine`:
   - Finds the element centre.
   - Tries 16 positions (N, NE, E, SE, S, SW, W, NW × Ring 1 + Ring 2).
   - Scores each by distance from host, overlap with other tags, crop edge, alignment bonus.
   - Adds a leader line only when it has to.
5. Report: *Placed 483 / Skipped 12 / With leader 47*.

### Switching positions after the fact

Each tag family has 16 named types (`2.5mm - 1x-N`, `2.5mm - 1x-E`, …, `2.5mm - 1.5x-NW`). **Default active type is `2.5mm - 1x-N`** — North, Ring 1, 2.5 mm text. To move a tag:

- **Panel → TAGS → Position → Switch Tag Position**, then pick Above / Right / Below / Left.
- The engine just swaps the family type — no leader breaks, no position drift.

### Aligning tags into bands

- Select several tags → **Align Tag Bands**. STING sorts them spatially (by Y on plan) and snaps their head positions to a grid so they read like an orderly list.

---

## Part 6 — Making Tags Look Right (colours, sizes, styles)

STING's **Tag Style Engine** has one idea: instead of many tag families, have ONE family with many label rows and turn exactly ONE row on at a time.

### The 128-BOOL matrix

Every tag family carries 128 `Yes/No` parameters named `TAG_{SIZE}{STYLE}_{COLOR}_BOOL`:

- 4 sizes: `2`, `2.5`, `3`, `3.5` mm
- 4 styles: `NOM`, `BOLD`, `ITALIC`, `BOLDITALIC`
- 8 colours: `BLACK`, `BLUE`, `GREEN`, `RED`, `ORANGE`, `PURPLE`, `GREY`, `WHITE`

Exactly **one** is true per element type. Only the label row gated by that BOOL is visible.

### Applying a style

- **Panel → VIEW → Tag Appearance → Apply Tag Style** → pick size → pick style → pick colour. Done.
- Or **Panel → VIEW → Tag Appearance → By Discipline** — one click, sets M=Blue Bold, E=Red Bold, P=Green Bold, A=Black Normal etc.

### Pre-defined colour schemes (8 built-in)

`Discipline`, `Warm`, `Cool`, `Red`, `Yellow`, `Blue`, `Monochrome`, `Dark`. Pick one under **Apply Color Scheme**.

### Per-view override

- You can force a view to use its own style: **Panel → VIEW → Set View Tag Style**. Useful for presentation views where everything should be grey.

### TAG7 sub-section styling (built-in defaults)

The rich TAG7 narrative has 6 sub-sections. STING ships with a default palette (per the v5.2 CSV preamble):

| Section | Content | Style |
|---|---|---|
| 7A Identity Header | Name / Manufacturer / Model | Bold Blue |
| 7B System & Function | HVAC Supply, DCW Return, etc. | Italic Green |
| 7C Spatial Context | Room / Department / Grid ref | Normal Orange |
| 7D Lifecycle & Status | NEW / EXISTING / DEMOLISHED / REV | Normal Red |
| 7E Technical Specs | Capacity / Flow / Voltage | Bold Purple |
| 7F Classification | Uniclass / OmniClass / Cost codes | Italic Grey |

Override any of these by filling the Style/Color/Size columns next to the row in the tag config CSV (v5.2 schema).

---

## Part 7 — Validating and Fixing

### Daily QA workflow (5 minutes)

1. **Compliance Dashboard** (**Panel → TAGS → QA → Completeness Dashboard**) — RAG status per discipline.
2. **Validate Tags** — runs ISO 19650 code checks, cross-validates DISC↔SYS↔FUNC↔PROD pairs, flags placeholders.
3. **Highlight Invalid** — paints missing (red) and incomplete (orange) elements in the active view.
4. If there's a cluster, **Resolve All Issues** does the heavy lifting.

### The 19650 code cross-checks

STING catches mismatches the eye misses:

- ✗ `DISC=M` but `SYS=LV` → discipline says Mechanical, system says Low Voltage — impossible.
- ✗ `SYS=HVAC` but `FUNC=PWR` → Power isn't an HVAC function.
- ✗ `FUNC=SUP` but `PROD=WC` → "Supply" doesn't match "Water Closet".

Each mismatch becomes an audit row with a suggested fix.

### Stale elements — things that moved

Every time geometry changes, `StingStaleMarker` (a Revit IUpdater STING runs in the background) checks whether the element's LOC/ZONE/LVL/SYS still match the current context. If not, it writes `STING_STALE_BOOL = 1`.

- **Select Stale Elements** to see them.
- **Re-Tag Stale** to re-derive tokens and clear the flag.

Run this weekly. It's also the first step of the Morning Health Check and Daily QA presets.

---

## Part 8 — Workflows (one-click sequences)

Instead of clicking 8 buttons in order, use a **workflow preset** — a recorded sequence of commands with conditional logic.

### Built-in workflows worth knowing

| Name | Steps | When to run |
|---|---|---|
| **Morning Health Check** | RetagStale → PreTagAudit → BatchTag (new only) → Validate → SheetNamingCheck → ModelHealth → …(10 steps) | Start of each day |
| **Daily QA** | RetagStale → PreTagAudit (skip if ≥95%) → …(8 adaptive steps) | Lunchtime or before issuing |
| **Weekly Data Drop** | All of above + COBieExport + register export + model health report | Friday, pre-issue |
| **COBie Readiness** | Resolve → WriteContainers → Validate → SchemaValidate → COBie export | Before any COBie deliverable |
| **Pre-Meeting Prep** | Stale fix → Warnings fix → Validate → reports | 15 min before design review |
| **Handover Readiness** | Full 9-step gauntlet ending in a new revision | Stage 6 handover |

### How they adapt

Steps can skip themselves based on model state:

- `maxCompliancePct: 95` — skip if already 95% compliant (don't waste time).
- `requiresStaleElements` — skip if nothing has moved.
- `has_overdue_issues`, `has_high_severity_warnings`, `compliance_below_70` — conditional gates.

### Running a workflow

- **Panel → BIM → Workflows → Run Workflow Preset** → pick from list.
- Each step reports timing; you can hit Escape to cancel. Failures can optionally roll back the whole sequence (TransactionGroup).
- Results saved to `STING_WORKFLOW_LOG.json` alongside the project file.

---

## Part 9 — Managing Tags Across Revisions

Every tag change on every element is logged to `ASS_TAG_PREV_TXT` + `ASS_TAG_MODIFIED_DT` + `ASS_TAG_MODIFIED_BY_TXT` (the last one is the user, ISO 19650 §5.2 requires this).

### Creating a revision

- **Panel → BIM → Revision → Create Revision**. Name follows ISO 19650 (`P01`, `P02.1`, `C01` for Construction…).
- STING takes a *snapshot* of all tags at that moment into `_bim_manager/revisions/`.
- Before writing the revision it runs a **compliance gate** — if tag completeness is below 80%, STING shows a per-discipline breakdown and asks you to confirm or cancel.

### Comparing revisions

- **Revision Compare** shows which elements' tokens changed between two snapshots. Token-level diff: old DISC/LOC/ZONE… vs new.
- Output CSV groups changes as TOKEN_CHANGE, CONTAINER_REGEN, NARRATIVE_CHANGE, STATUS_CHANGE, TAG_REFORMAT.

### Auto-cloud on tag change

- **Auto Revision on Tag Change** — once enabled, every element whose TAG1 changes since the last revision gets a revision cloud in the active sheet automatically. Leaves a clean trail for the checker.

### Tag export / import between models

- `Export Tag Map` writes a `.sting_tagmap.json` with every element's UniqueId, family/type, XYZ location, 8 tokens, STATUS, REV.
- `Import Tag Map` into another project matches by UniqueId (exact), then family+type+nearest-location fallback (500 mm radius). Used when splitting a federated model or carrying tags across phases.

---

## Part 10 — Day-to-Day Coordinator Checklist

### Morning (15 min)

1. ⬜ Open model. Let the compliance scan finish (status bar shows RAG).
2. ⬜ Check morning briefing dialog — if shown, click "Run Morning Health Check".
3. ⬜ Quick look at **Issues tab** in BIM Coordination Center. Any overdue SLA?
4. ⬜ Skim the warnings tab. Critical/High over the weekend?

### During the day (as needed)

- Modelling a new section → tag as you go with **Auto Tag** after each group of additions.
- Got handed a dirty model? → Run **Pre-Tag Audit** first to see the damage, then **Resolve All Issues**.
- Unfamiliar discipline? → Use **By Discipline** tag style so reviewers can see what's theirs at a glance.

### End of day (5 min)

1. ⬜ Run **End of Day Sync** workflow (retag stale → validate → save baseline → model health).
2. ⬜ If issues were closed today, link them to the current revision.
3. ⬜ Sync with central.

### Weekly

- Friday: **Weekly Data Drop** workflow → COBie + register exports → issue a new revision (even just `P02`).

### Monthly

- Review **Parameter Duplicate** audit (`docs/PARAMETER_DUPLICATES.md`).
- Check **Data Drop Readiness** against the current RIBA stage.
- Update BEP if governance changed.

---

## Part 11 — Handover (the big one)

When the client says "ship it":

1. **Clear the decks**
   - ⬜ No stale elements (`Select Stale Elements` returns 0).
   - ⬜ Zero critical warnings (`Warnings Dashboard` green).
   - ⬜ Four-bucket compliance: Fully resolved ≥ 95%.
2. **Freeze the revisions**
   - ⬜ Create final revision (`C01` for Construction, or whatever the project demands).
   - ⬜ Auto-revision clouds reviewed.
3. **Run the handover gauntlet**
   - ⬜ **Handover Readiness** workflow — it will block if compliance is below the gate.
4. **Export deliverables**
   - ⬜ **COBie V2.4 Export** (**Panel → BIM → COBie**). 18 worksheets, project-type preset (Commercial / Healthcare / Data Centre etc.)
   - ⬜ **Drawing Register** (`.csv`) and **Sheet Register** (`.csv`).
   - ⬜ **FM Handover Manual** — asset register, maintenance schedule, O&M content, space report.
   - ⬜ **Weekly Coordinator Report** (`.html`) — one self-contained file to attach to the transmittal.
5. **Transmit**
   - ⬜ Create a transmittal (**Panel → BIM → Transmittals → New**) with CDE state `PUBLISHED`.
   - ⬜ Linked issues and document register attached automatically.

---

## Quick Command Finder

### "I need to…"

| Goal | Go to |
|---|---|
| Tag everything in my view | **TAGS → Auto Tag** |
| Tag the whole model | **TAGS → Batch Tag** |
| Preview what would be tagged | **TAGS → More → Pre-Tag Audit** |
| Fix placeholders | **TAGS → QA → Resolve All Issues** |
| Fix duplicate SEQ numbers | **ORGANISE → Tag Ops → Fix Duplicates** |
| Place visual tags on a view | **TAGS → Smart Place Tags** |
| Move a tag | **TAGS → Position → Switch Tag Position** |
| Change size / colour / style | **VIEW → Tag Appearance → Apply Tag Style** |
| Colour by discipline | **VIEW → Tag Appearance → By Discipline** |
| See how compliant I am | **TAGS → QA → Completeness Dashboard** |
| Check what's stale | **SELECT → State → Stale** |
| Re-tag things that moved | **TAGS → More → Re-Tag Stale** |
| Export to Excel for the designer | **BIM → Excel → Export to Excel** |
| Import their edits back | **BIM → Excel → Import from Excel** |
| Generate COBie | **BIM → COBie Export** |
| Create a revision | **BIM → Revision → Create Revision** |
| Run a scripted workflow | **BIM → Workflows → Run Workflow Preset** |
| Open BIM Coordination Center | **BIM → Coordination Center** |

### "I got a warning that said…"

| Warning | What it means | Fix |
|---|---|---|
| `LOC=XX, ZONE=ZZ` on most tags | Elements aren't in rooms / no project LOC | Name rooms, or set `DEFAULT_LOC` in config |
| `SYS=GEN` on MEP elements | MEP system not assigned | Connect MEP system in Revit, or add force-SYS rule |
| `SEQ=0000` | Assignment never ran | Run `Assign Numbers` |
| `TAG collision` | Two elements compute the same TAG1 | Pick Auto-increment collision mode; then Validate |
| `Stale` count high | Geometry changed after last tag run | Re-Tag Stale |
| `Compliance gate failed` | Tag % below threshold before COBie/handover | Resolve All Issues, re-run |
| `Container empty` | TAG1 exists but discipline containers blank | Run Combine Parameters, or the Tag & Combine button |

---

## Glossary (the ten terms worth remembering)

- **Token** — one of the 8 segments of a tag. Everything else follows.
- **Container** — one of 53 parameters that STORE the assembled TAG1 (HVC_EQP_TAG, ELC_EQP_TAG, …). Every discipline has one.
- **TAG7** — the rich human-readable narrative. Six styled sub-sections A-F.
- **Placeholder** — a stub value (GEN, XX, ZZ, 0000) that lets an 8-segment tag exist even when data is missing.
- **Fully resolved** — a tag with zero placeholders AND STATUS+REV populated. What the FM team needs.
- **Stale** — element moved, geometry changed, or system reassigned since the last tag run.
- **Compliance Gate** — the threshold (default 80%) below which STING blocks handover-level operations.
- **Workflow preset** — a JSON-described sequence of commands with conditions. Run from the BIM tab.
- **Revision snapshot** — a frozen JSON copy of every tag at revision-creation time. Enables diff between revisions.
- **Tag Style BOOL** — one of 128 Yes/No params that gates a label row's visibility. Exactly one true = exactly one visible row.

---

## Troubleshooting — "Why isn't my tag…?"

### … showing up on the view
- Is the element inside the view's crop region? Crop it to check.
- Is the tag visibility turned on in the view template?
- Have you actually placed a visual tag? Remember data tags ≠ visual tags. Run **Smart Place Tags**.

### … using the right colour
- Did you apply the style? Run **Apply Tag Style** or **By Discipline**.
- Has the view got a per-view override? Check **Set View Tag Style**.
- Is the BOOL actually bound in the family? Re-run **Fam Params** with *Purge + Inject* default.

### … numbering sequentially
- SEQ counters are per `(DISC, SYS, FUNC, PROD)` group. If you change any of those tokens, you'll get a new counter.
- The `.sting_seq.json` sidecar next to the `.rvt` remembers counters between sessions. If you wipe it, numbers restart from 1.

### … respecting the compliance gate
- Check `project_config.json → COMPLIANCE_GATE_PCT`.
- Run the relevant audit — if compliance is genuinely below the gate, **Resolve All Issues**.
- If the gate is too strict for your stage, lower the config value.

### … updating after I change phases
- Phases change STATUS. Re-tagging the affected elements updates STATUS.
- StingStaleMarker catches this; **Re-Tag Stale** is the quick fix.

### … matching what Excel shows
- Excel import has a special `CLEAR` sentinel — type the word CLEAR in a cell to intentionally blank that parameter.
- STING validates FUNC↔SYS and DISC↔SYS on import and warns before writing mismatches.

---

## Further Reading

- **Technical deep-dive:** `StingTools/Data/TAGGING_GUIDE.md` — 27 sections, full pipeline internals, every command.
- **BIM coordinator handbook:** `StingTools/Data/BIM_COORDINATION_WORKFLOW_GUIDE.md` — 22 sections, ISO 19650 roles, CDE, data drops.
- **Family authoring:** `StingTools/Data/TAG_FAMILY_CREATION_GUIDE.md` — how to hand-build a tag family.
- **DWG → BIM conversion:** `StingTools/Data/DWG_TO_BIM_GUIDE.md`.
- **BIM Coordination Center reference:** `docs/bcc-guide.md` — every tab, every button.
- **Parameter consolidation audit:** `docs/PARAMETER_DUPLICATES.md` — which old param names were deprecated.

---

*Last updated: 2026-04-16. Schema: Tag Config v5.2, MR_PARAMETERS 2,361 entries, 137 tag families.*
