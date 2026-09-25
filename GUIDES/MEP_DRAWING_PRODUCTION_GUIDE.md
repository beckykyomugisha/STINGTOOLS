# MEP Drawing Production with StingTools: the Fastest Order

**Written:** 2026-09-24, from a code-level review of the production path. **This has not been
run end-to-end in Revit.** Where a step depends on code nobody has run, it says so. File
references are for whoever maintains this guide.

The one rule behind the order: **every step feeds the next.** Levels name the sheets. Project
codes number the sheets. Parameters must exist before anything can write them. Tags must exist
before the annotation pass reads them. Sheets must exist before match lines can quote their
numbers. Do the steps out of order and the later ones run without errors but produce blanks.

---

## Critical path (one line)

named levels → project codes → Load Params → title blocks → styles → scope boxes →
systems / circuits → tag → **produce** → match lines → panel schedules / SLD on sheets → QA →
issue

---

## Step 0: Before you open STING (10 min, saves hours)

| Do | Why |
|---|---|
| Name levels as short ISO codes **with no spaces**: `B1`, `L00`, `L01` … | `{lvl}` in sheet numbers is the raw level name. The scope-box name grammar forbids spaces. Scope-box production matches the level name **exactly**. |
| Fill in **Project Information → Number**, plus `PRJ_PROJECT_COD_TXT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` | Sheet patterns that use `{project}` / `{originator}` otherwise leave literal braces. Revit rejects that number and the sheet keeps its default number. This hits `mep-plan`, `elec-power` and the spool types. |
| Draw scope boxes **unrotated, edge to edge, not overlapping** | Match lines are generated only where two boxes' faces touch (within 1 mm) and overlap by ≥ 100 mm. |

---

## Step 1: Project Setup Wizard (SETUP → ★ Project Setup Wizard)

It runs units → project information → levels → grids → worksets → **Load Params** → materials
→ MEP family types → schedules → styles, filters and view templates.

**Untick three options:**
- **Create Views / Dependents / Sheets.** That is the older production path. Its sheets carry
  no drawing-type stamp, so Doctor, Renumber, Heal Title Blocks and Produce & Export all ignore
  them, and Step 8 then produces a second, duplicate set.
- **Rename scope boxes.** Its default pattern `{BLD}-{ZONE}-{INDEX}` is understood by nothing
  else in STING. Name boxes in Step 5 instead.

## Step 2: Load Params (CREATE TAGS → Load Params)

This already ran inside the wizard. **Run it again after every plugin update**, because new
features add parameters. For example, the BS 7671 check column needs `ELC_CKT_CHECK_TXT`. A
parameter that isn't bound makes every write to it a silent no-op.

## Step 3: Title blocks (DOCS → title blocks → Create all): once per machine

Writes the STING title-block `.rfa` files. Drawing production then maps the logical name
(`STING_TB_SHEET_A1`) to the real family and loads it automatically.

## Step 4: Styles

1. **DOCS → AEC Filters → Create.**
2. **HVAC panel → SYS → Build System Types**, then **Generate System Filters.**
3. **DOCS → Drawing Types → Audit style refs** (read-only).

Known gap: nothing creates the MEP view templates that the drawing types name
(`STING - HVAC Duct`, `STING - Power Layout`, `STING - Lighting Layout`, `STING - Fire Alarm`,
`STING - Drainage`, `STING - MEP Plan`). Production warns once per view and falls back to the
style pack. The drawings still come out; the warnings are noise. To get real templates, run
**Convert to managed / Regenerate templates** once.

## Step 5: Scope boxes, the automation multiplier

One scope box named for a drawing type, a level and a zone produces a cropped, numbered sheet
for that combination in one click.

**Naming:** `STING::<drawing-type-id>::<level>::<zone-tag>`
- Example: `STING::elec-power-A1-1to100::L01::Z1`
- Allowed characters: A–Z, 0–9, `.`, `_`, `-`. No spaces.
- A malformed name is reported, never silently skipped.
- Use **DOCS → Scope Box Manager**: it has a drawing-type dropdown and validates names.

**Three naming conventions exist. One box can carry only one of them:**

| Name | What reads it | Effect |
|---|---|---|
| `STING::<dt>::<level>::<tag>` | View production | Crop + view + sheet per box |
| `STING-LOC::<code>` | Tagging | Sets the LOC (building) token. The smallest containing box wins; used only when room / workset detection falls back to the project default. The box must be unrotated. |
| `{BLD}-{ZONE}-{INDEX}` (wizard default) | Nothing | Nothing. Don't use it. |

**How to get the most from them:**
- One box per **drawing type × level × zone**, edge to edge, so match lines generate.
- For multi-building sites, add separate `STING-LOC::BLD1`, `STING-LOC::BLD2` footprint boxes,
  so elements outside rooms still get the right LOC.
- ZONE is **never** read from scope boxes. It comes from room Department/Name/Number or the
  workset name, otherwise `Z01`. Risers and ceiling voids often fall to `Z01`: check with
  **Token Confidence Audit** in Step 7.

**Known limits:**
- Box count grows quickly: 4 disciplines × 5 levels × 3 zones = 60 hand-named boxes. There is
  no fan-out command yet.
- The "Duplicate as Dependent" option in the production dialog is currently **ignored**.
- Match lines pair every view on box A with every view on box B, across disciplines.

## Step 6: Model → data

| Discipline | Order |
|---|---|
| Placement | Placement Center / Place Fixtures → Routing Auto-Drop |
| Systems | MEP → Build Systems |
| Electrical | Auto-Group Circuits (**review it**: it does no load or phase balancing) → Build Circuits → **Electrical panel CALCS → ▶ Recalculate All** (voltage drop) → **PNLS → ⚡ Batch Create Schedules** (answer **Yes** to "create the STING templates first") → **✅ Compliance check** → **▶ Apply Balance** |
| HVAC | `WORKFLOW_HVACDesign` preset (duct auto-size is run manually from the HVAC panel) |
| Plumbing | `WORKFLOW_PlumbingDesign` preset |

## Step 7: Tag **before** you produce

**TAGGING → Tag & Combine** (or Full Auto-Populate), then **Token Confidence Audit** to find
elements that fell back to default ZONE or LOC values. The production annotation pass and the
rich TAG7 tags read these tokens. Tagging after production means re-running production.

## Step 8: Produce

Use **DOCS → Drawing Types → Produce from Scope Boxes** (zoned) or **Produce per Level** (whole
floor). Tick **only** the MEP types:

- `mep-hvac-duct-A1-1to100`, `mep-coord-A1-1to50`, `mep-plantroom-A1-1to50`
- `elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100`
- `plumb-drainage`, `plumb-ag-drainage`, `plumb-rwd-layout`

One click does: create the view, stamp it, crop it, apply the template / style pack,
auto-annotate, create the sheet, and fill the title block. **Re-runs reuse** existing views and
sheets, so re-run freely after model changes.

Then, in this order:
1. **Match Line → Generate.** Needs the sheets, because the captions quote sheet numbers.
2. **Panel schedules → Place on sheets.**
3. **SLD → Generate** (build the SLD symbols first).
4. The schedule drawing types: `mech-equip-schedule-A3`, `elec-panel-schedule-A3`,
   `valve-schedule-A3`.

## Step 9: QA

Run: Drawing Types **Doctor** → **Sheet Compliance** → **Validation: Run All** →
**Validate Tags** → **Match Line: Validate**. Then run **Heal Title Blocks** / **Renumber** only
if they reported something.

## Step 10: Issue

`WORKFLOW_RevisionIssue` (Create Revision → Auto Revision Cloud → Issue Sheets → Revision Sync →
Revision Schedule). Then **Produce & Export → option 2, "Finalize + Export"**, which writes PDFs
plus a register CSV of the stamped sheets. Then **BIM → Issue Deliverable / Create Transmittal**.

---

## Do-not list

| Don't | Because |
|---|---|
| Choose **option 1** of Produce & Export | It produces **every** Plan drawing type (49) × every level: architectural, structural, presentation, healthcare. |
| Use **HVAC panel → SYS → Produce MEP views by level** for issue sheets | It uses the first loaded title block, leaves Electrical sheets with default numbers, doesn't stamp sheets (so Export skips them), and has no fire protection. Use it for coordination views only. |
| Run the wizard's Create Views **and** Drawing Types production | You get two sets of views and sheets. |
| Generate match lines before sheets exist | The captions are left empty. |
| Tag after producing | The annotations are blank. |

## Gaps worth knowing (logged for fixing)

- No fire-protection drawing types (sprinkler layout, fire riser), and no plumbing
  water-supply (DCW/DHW) plan type.
- The routing rule `E / PLAN` points to `elec-riser-A3-1to200`, a section.
- Sheet-number styles are mixed within one set (full ISO vs short).
- Production commands can't be chained in a workflow preset yet (not in
  `WorkflowEngine.ResolveCommand`, and they open a dialog).
- `STING_SCOPE_BOX_TAG_TXT` isn't a registered parameter, so the `::<tag>` stamp is a no-op.
- Style sync and re-runs re-apply the crop without the scope-box context. **Not yet checked
  in Revit.**

## Proposed next automations (highest payoff first)

1. **Scope-box fan-out:** copy one master zone box to every level × drawing type, correctly
   named.
2. **Discipline-aware match lines:** pair only views of the same drawing type; skip
   `STING-LOC::` boxes.
3. **Workflow presets** once the producers can run headless: `WORKFLOW_MEPDrawingSetup`,
   `WORKFLOW_MEPDrawingProduction`, `WORKFLOW_MEPPreIssue`.
4. **ZONE from `STING-ZONE::` boxes,** mirroring LOC.
5. **Honour "Duplicate as Dependent":** one parent view per level plus a dependent per box.
6. **FP drawing types**, a water-supply plan type, and the `E / PLAN` routing fix.
7. **Wizard:** default scope-box names to the `STING::` grammar; warn on level names that
   contain spaces.
