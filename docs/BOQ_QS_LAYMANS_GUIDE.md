# Quantity Surveying **&** Project Management with StingTools — A Layman's Guide
### For the Revit user who has never costed a thing, and just got handed a huge project

> **Living document.** StingTools is still being built. Anything marked **(partial)**
> or **(directional)** works but isn't the full professional tool yet; the **Honest
> gaps** section (§D6) lists what's thin so you're never surprised. We update this
> guide every time we ship a function.

---

## How to read this guide

You were handed a big model and told *"give us a cost, then help run the job."*
That's two jobs, so this guide has two halves plus orientation and a worked example:

- **Part A — Orientation.** What the panel is, every gauge, every toolbar control.
- **Part B — The QS job.** Turn the model into a priced Bill of Quantities (BOQ). Step by step, every button.
- **Part C — The PM job.** *Run* the build month to month — programme, certificates, variations, forecasting, closeout. Every button.
- **Part D — Putting it together.** A full worked tower, a monthly checklist, cheat sheets, honest gaps.

**You do not need to become a QS or a planner.** You need to keep the model honest,
press buttons in the right order, and understand ~24 words. This guide teaches all
three. Skim Part A once, then live in Parts B and C.

---
---

# PART A — ORIENTATION

## A0. The whole idea, in 90 seconds

You know how to model a building. A **Quantity Surveyor (QS)** turns that building
into **money**: *how much* of each thing there is, *what it costs*, and — once on
site — *how that cost moves*. A **Project Manager / commercial lead** then *runs*
that money: pays the contractor monthly, prices changes, and forecasts where the
job will land vs budget.

The **BOQ & Cost Manager** (a dockable panel in StingTools) does the heavy lifting
for both. It reads your Revit model, **counts everything** (a "take-off"),
**groups it** the way QSs expect, **multiplies by a rate**, and gives you a priced
**BOQ** — then a full toolkit for tendering, certifying payments, tracking changes,
and forecasting the final cost.

**One mental model:** *Model → Bill → Baseline → Run.* You build an honest model,
turn it into a bill, freeze it as a baseline, then run the job against that baseline.

## A1. The words you need (skim now, refer back)

**QS / cost words**

| Word | Plain meaning |
|---|---|
| **BOQ** (Bill of Quantities) | The priced, line-by-line shopping list of the whole building. |
| **Take-off** | Measuring quantities (lengths, areas, volumes, counts) from the model. The tool does this automatically. |
| **Rate** | The price for **one unit** (e.g. UGX 166,500 / m² of ceiling). Quantity × Rate = line cost. |
| **NRM2** | The UK standard "filing system" — which work goes in which numbered section. An agreed order so everyone reads the bill the same way. |
| **Measured work** | Items that come from real model geometry. The bulk of the bill. |
| **Provisional sum (PS)** | Money set aside for something not yet designed (e.g. "allow UGX 80M for lifts"). A placeholder you reconcile later. |
| **Daywork** | Work paid by time + materials (labour hours, plant, materials), not a fixed rate. |
| **Prelims / Contingency / Overhead & Profit** | Markups on top of measured cost: site running costs, a safety buffer, the contractor's margin. |
| **VAT** | Tax added at the end. |
| **Star rate** | A rate built from first principles (labour + plant + materials + OH + profit) when no standard rate fits. |

**PM / commercial words**

| Word | Plain meaning |
|---|---|
| **Contract sum** | The agreed price at award. Everything afterward is measured against it. Also the EVM **BAC** (Budget At Completion). |
| **Snapshot / baseline** | A frozen copy of the bill at a moment, so you can compare "then vs now". |
| **Payment certificate** | The monthly "the contractor has done X% — pay them this much" document. |
| **Retention** | A slice (e.g. 5%) withheld from each payment as security, released in two halves (moieties): at Practical Completion and end of Defects Liability. |
| **Variation (VO)** | An approved change to the works after the contract — adds or removes cost. Classified by *reason* and *liability* (who pays). |
| **EOT** | Extension of Time — extra days granted for delays; can trigger a money claim. |
| **CPM / float** | Critical Path Method: the chain of tasks that sets the finish date; **float** = slack a task has before it delays the job. |
| **S-curve / PV** | The planned cumulative spend over time (Planned Value) — the baseline EVM compares against. |
| **EVM** (Earned Value) | A health check: **CPI** (cost efficiency) and **SPI** (schedule efficiency); **EAC** = forecast final cost. |
| **CVR** | Cost-Value Reconciliation — are we over- or under-claiming this month? What's the margin? |
| **Commitment** | Money you've *promised* via a PO or sub-contract (even if not yet certified). |
| **Loss & Expense** | A time-related claim — delays cost money (prolongation + overheads + disruption). |
| **Fluctuations** | Price-rise recovery via an index (NEDO/BCIS formula or UBOS CPI). |
| **Final account** | The signed close-out statement: contract sum ± PS movement ± VOs ± fluctuations. |
| **AFC** | Anticipated Final Cost — where the job is heading if current trends hold. |
| **MIDP / TIDP** | Master / Task Information Delivery Plan — what's due, when, at what quality (ISO 19650). |

That's the whole vocabulary. Everything below uses only these words.

## A2. The golden rule: the bill is only as good as the model

The tool measures **what you modelled**:

- A wall modelled as a generic mass → measured as a mass, not blockwork.
- A room with no Room object → no floor finish measured.
- A placeholder family with no real geometry → no quantity.

You don't need a *perfect* model — you need an **honest** one, and the tool tells
you how honest with two numbers (**Coverage** and **BOQ Health**). **Garbage in,
garbage out** is the one law of QS. Your Revit skills are exactly what make the
cost good.

> **Do this once before anything else:** run **STING Tools → Create Parameters**
> (a.k.a. Load Shared Params). This binds the cost, carbon and progress parameters
> (rates, embodied carbon, % complete, etc.) onto your elements. Without it, the
> take-off can't write or read those values and prices/carbon come back blank.

## A3. Where everything lives — the panel map

Open Revit → the **STING Tools** ribbon/panel → **BOQ & Cost Manager**. It's a
docked window you keep open while you work. Top to bottom:

```
┌─ HEADER ──────────────────────────────────────────────────────────────┐
│  UGX | USD     ⛓ Links (N)   ↻ Refresh   ↻ Refresh (full)             │
│                Set Budget     ✦ Cost Setup                            │
├─ DASHBOARD (gauges) ──────────────────────────────────────────────────┤
│  Budget · Modeled cost · Provisional/manual · Variance · Coverage     │
│  BOQ Health · Embodied carbon          [amber DRIFT banner if any]    │
├─ SNAPSHOT ROW ────────────────────────────────────────────────────────┤
│  Snapshot: ▾   (diff vs selected)   Save snapshot ▾   Compare         │
├─ TOOLBAR ─────────────────────────────────────────────────────────────┤
│  Filter:[___]  ALL A S M E P FP PS  ⊞ ⊟  ¶ Description                │
│  Group:[Work section ▾]  ▦ Columns  Profile:[▾]   "N items match"     │
├─ TABS ────────────────────────────────────────────────────────────────┤
│  [ Bill of Quantities ] [ Materials ] [ Actions ] [ Schedule ]        │
│                                                                       │
│         (the active tab's content fills the body)                     │
│                                                                       │
├─ FOOTER ──────────────────────────────────────────────────────────────┤
│  Grand total UGX …   DRAFT/CERTIFIED   ＋Add Row  Reconcile  Import   │
│                                          ★ Export   ⤓ Export QTO IFC  │
└───────────────────────────────────────────────────────────────────────┘
```

## A4. The dashboard gauges, one by one

Learn these — they're your instrument panel. Glance at them after every **Refresh**.

| Gauge | What it tells you | What "good" looks like | If it's bad |
|---|---|---|---|
| **Project budget (incl. VAT)** | The target you set via **Set Budget** (or the wizard / a Cost Plan). | Set it early so **Variance** means something. | Set it. Variance is meaningless without it. |
| **Modeled cost** | Cost of everything measured from the model. | The core number. | Low vs expectation → model is thin or unpriced. |
| **Provisional / manual** | Cost of PS + daywork + manual rows you added. | Grows as you fill design gaps. | — |
| **Variance** | Budget − total. Over or under? (A progress bar shows fill vs budget.) | Near zero / under budget. | Over → cut scope, challenge rates, or re-baseline. |
| **Coverage** | % of model items that carry a proper rate/description. | Aim 100%. | Low = half the bill is unpriced → **Rate Gap Report**. |
| **BOQ Health** | A 0–100 trust score (shows as ✓ Excellent / Good / Fair / Poor). | 80+ / "Excellent". | Below ~70 → fix the model and rates before relying on it. |
| **Embodied carbon (A1–A3)** | The CO₂e "cost" of the materials. | Lower is better; informational. | Use **Carbon Gap Report** to find weak factors. |

Under the gauges you may see **"Description coverage 100% (3/3) · Avg rate
confidence 90"**. **Rate confidence** = how sure the tool is about each price
(90 = strong, e.g. you priced it; low = it guessed a default). **High coverage +
high confidence = a bill you can hand over.**

An amber **DRIFT banner** appears if the live bill has moved away from your last
snapshot — click it to run a **Drift check**.

## A5. The toolbar, control by control

| Control | Options / behaviour | Use it to… |
|---|---|---|
| **Filter:** box | Free-text over item names | Jump to "ceiling", "rebar", "VRF"… |
| **ALL · A · S · M · E · P · FP · PS** | Discipline toggles (Architecture, Structure, Mechanical, Electrical, Plumbing, Fire Protection, Prelim/Special). Ctrl-click multi-selects. | Review one trade at a time. |
| **⊞ Expand all / ⊟ Collapse all** | Open/close every section | Detail view vs totals-only. |
| **¶ Description** | Show/hide the NRM2 description strip on each row | Compact price-only vs full descriptive bill. |
| **Group:** ▾ | **Work section (NRM2)** · **Level** · **Zone** · **Location** · **Source model** · **WBS** · **CBS** | Re-file the whole bill by any dimension (see §C for WBS/CBS). |
| **▦ Columns** | Toggle: Source · Confidence · Carbon · CO₂ quality · Note · **Labour\|Plant\|Material** split · **Gross\|Deduct\|Waste\|Net** · WBS · CBS | Show only the columns you need for this task/export. |
| **Profile:** ▾ | Named column presets (corporate + project) | One-click column set for a specific report. |
| **"N items match"** | Live count | Confirms your filter caught what you expected. |

**Snapshot row** (just above the tabs):

| Control | What it does |
|---|---|
| **Snapshot:** ▾ | Pick a saved baseline to view against. The text beside it shows the delta (e.g. "↑ UGX +500M (+2.3%)"). |
| **Save snapshot ▾** | Menu: **Save as new** · **Overwrite** · **Mark as baseline**. Freezes the current bill (with checksum + optional server push). |
| **Compare** | Inline diff report between two snapshots: qty-moved / new / removed / rate-revised. |

## A6. Reading one BOQ row

`Ref · Item / description · Qty · Unit · Rate · Total · Level · Location · Src · Model · Conf · CO₂ · Note`

- **Ref** = NRM2 line number (e.g. 19.1.1).
- **Item / description** = the auto-written QS sentence ("Supply and fix suspended ceiling system…"). **Edit it inline.**
- **Qty / Unit** = measured amount (43.013 m²).
- **Rate** = price per unit — **type over it to price the item.**
- **Total** = Qty × Rate.
- **Src / Model** = where it came from. **Model** = your host model; a link name + `[Linked: …]` if from a linked model (those are read-only, see §D1).
- **Conf** = rate confidence (§A4).
- **CO₂** = embodied carbon for the line.
- **Note** = e.g. "Aggregated 5" (one row stands for 5 identical elements).

Right-click a row for the context menu (select element in model, etc.).

## A7. The four tabs at a glance

| Tab | Use it to… |
|---|---|
| **Bill of Quantities** | Read/price the bill, grouped into NRM2 sections (or any Group mode). The deliverable. |
| **Materials** | The same money grouped **by material/category** — procurement view ("how much concrete in total?"). |
| **Actions** | The workflow buttons — **every cost & PM task** in one scrolling, grouped list. Results render **inline** in the right-hand pane (no pop-ups). This is where Parts B and C happen. |
| **Schedule** | 4D/5D: phases, reporting periods, milestones, a cash-flow **S-curve**, and the live **EVM** strip. Your commercial cockpit on site (§C10). |

> **Where do results appear?** On the **Actions** tab, click a button and its result
> renders in the **right-hand pane**. Exports show an **Open file** button there.
> Headline actions are shown **bold green with a ★**.

---
---

# PART B — THE QS JOB: turn the model into a priced bill

## B0. The first-time playbook (overview)

Do these top to bottom the first time. Each step is detailed below.

| # | Step | Button(s) |
|---|---|---|
| 1 | Make the model costable | *(in Revit, before the panel)* + **Create Parameters** |
| 2 | Run the guided setup | **✦ Cost Setup** |
| 3 | Build the bill | **↻ Refresh** |
| 4 | Sanity-check quantities | **Group / discipline filters** |
| 5 | Price it | **Export/Import QS Bill**, **Fetch live rates**, inline **Rate**, **Rate Gap Report** |
| 6 | Add un-modelled money | **Add Manual / PS / Daywork** |
| 7 | Markups, budget, tender | **Set Budget**, **Preliminaries schedule**, **★ Export** (Tender dialog) |
| 8 | Validate, snapshot, sign off | **Validate Cost**, **Save snapshot**, **Record QS Sign-off** |
| 9 | Produce deliverables | **★ Export**, **Cost Plan**, **Export to ERP**, **ICMS3 Report**, **Export QTO IFC** |

## B1. Step 1 — Make the model costable (pre-flight)

Before you touch the panel, in Revit:

- **Place Rooms/Spaces** — they drive finishes and area-based items. No Room → no floor finish measured.
- **Set Phases** correctly (New / Existing / Demolished) — demolished items shouldn't be priced as new.
- **Use real types**, not generic masses, where it matters — a generic mass costs as a mass.
- **Run Create Parameters** (§A2) so cost/carbon/progress params are bound.
- *Don't chase 100%.* Just press **Refresh** and let **Coverage / Health** show you the holes.

## B2. Step 2 — Run the Cost Setup wizard (**✦ Cost Setup**)

This is the guided on-ramp (auto-offered once on a fresh project with no budget).
It's **6 inline pages** in the Actions pane — it *orchestrates the real buttons*, so
nothing here is magic you can't also do manually:

| Page | Title | What it asks / does | Why |
|---|---|---|---|
| 0 | **Welcome** | What this does + link to this guide. | Orientation. |
| 1 | **Model Readiness** | Scans rooms, phases, categories, linked elements → score (Excellent → Poor). | Tells you if the model is costable *before* you waste time. |
| 2 | **Project & Currency** | Type the **Budget (UGX)** and pick display **currency (UGX/USD)**. | Makes **Variance** meaningful. |
| 3 | **Pricing Path** | Choose **Auto rates / QS round-trip / Manual**. If QS, a one-click **⤓ Export QS Bill now**. | Picks how you'll price (§B5). |
| 4 | **Markups** | Shows flat **Prelims / Contingency / OH&Profit %**; button to open the itemised **Preliminaries schedule**. | Sets the top-up that turns measured cost into a tender figure. |
| 5 | **Ready to Finish** | Summary; **✓ Finish** saves a baseline snapshot. | You leave with a built, priced, baselined bill. |

Navigation: **‹ Back · Next › · ✓ Finish · Skip**. "Completed" is remembered so it
won't nag you again.

## B3. Step 3 — Build the bill: **↻ Refresh** vs **↻ Refresh (full)**

| Button | What it does | When |
|---|---|---|
| **↻ Refresh** | *Incremental* — re-takes-off only what changed since last build (fast). | Day to day. |
| **↻ Refresh (full)** | *Complete* re-walk of every element (slower, always correct). | After big model changes, or if a number looks stale/wrong. |

What happens under the hood: the tool walks elements, measures geometry, classifies
each into NRM2, writes a QS description, looks up a rate, and ends with a parameter-
write transaction. On a large host model (≥ ~1,500 elements) you'll see a
"Building bill of quantities…" progress dialog — it reads as working, not frozen.
**Refresh deliberately, not constantly.**

After it finishes, **read the seven gauges** (§A4). If **Coverage** or **Health** is
low, find the worst sections, fix the model, and Refresh again.

## B4. Step 4 — Sanity-check the **quantities** (not prices yet)

- **Group → Work section** and skim each section. Do the quantities look sane? (A 40-storey tower with 12 m² of concrete = something is wrong.)
- Use the **discipline filters** to review one trade at a time.
- Watch for **"Qty 1 each" junk rows** — usually 2D/annotation leaking in. Most are excluded already; flag anything odd.
- Want to see *how* a quantity was derived? **Actions → Measurement audit** shows the gross → net derivation (gross geometry, openings/voids deducted, wastage step, net measured quantity) per line.

## B5. Step 5 — Get prices in (every pricing button)

You have several ways; pick what suits the job.

**(a) Automatic** — the tool already applies rates from its rate library where it
can (that's why some rows arrive with a Rate + confidence). Nothing to do.

**(b) QS Round-Trip (Excel) — the professional way for a big job.**

| Button | How | Why |
|---|---|---|
| **★ Export QS Bill** | Exports the bill in NRM2 trade order (Ref/Desc/Unit/Qty/Rate/Amount), priced or unpriced. | Hand the Excel to a real QS (or a rate book) to fill the **Rate** column. |
| **Import QS Bill** | Re-import the priced workbook. Shows a diff (rate changes / new rows / model-qty drift) **before** applying; your model quantities are preserved, QS rates land as `RateSource = QS`. | Prices land back on the right rows — each carries a hidden **stable key**, so they survive a re-Refresh. |

> **This is how you price a huge job without being a QS — you borrow one via Excel.**

**(c) By hand** — click a **Rate** cell in the BOQ tab and type. Good for a handful
of items.

**(d) Live rate feeds** (optional, for connected projects):

| Button | How | Why |
|---|---|---|
| **Rate feeds** | Configure the **BCIS** feed (base URL · API key · TTL) and toggle the **Planscape** feed. Saved to the project file only — the API key is never committed. | Wire up live market rates. |
| **★ Fetch live rates** | Pull candidate rates for the current bill from every feed, side-by-side with the current rate + confidence. Accept the best per line or in bulk. **Manual overrides are protected.** | Refresh prices to the market without re-keying. |
| **Rate-source heatmap** | Colours the model by each element's rate provenance (rate-book / CSV / override / live feed / unpriced). Read-only. | *See* at a glance where prices come from — and what's still unpriced. |

**(e) Find the holes:** **Rate Gap Report** lists every modelled item that still
needs a price (no rate / low confidence / defaulted), totals the **value at risk**,
and exports a **CSV worklist** to hand the QS. Run this before you trust a coverage
number.

**(f) Tidy before export:** **Prep for Export** normalises the bill — resolves
outstanding NRM2 paragraphs, fills missing line refs, tidies descriptions (read-only
preview first).

## B6. Step 6 — Add what the model can't show

**Add Manual / PS / Daywork** (also the footer **＋ Add Row**) authors rows the model
can't carry — they **survive a model rebuild**:

- **Provisional sum (PS)** — "allow UGX 80M for the lift installation (design pending)".
- **Daywork** — time-and-material allowances.
- **Manual measured / PC sum** — anything real that isn't modelled (site clearance, a prime-cost sum).

These appear under **Provisional / manual** in the gauges. Later you replace PS
guesses with reality via **Reconcile Provisionals** (§C7).

## B7. Step 7 — Markups, budget & the tender figure

- **Set Budget** — type your project budget so **Variance** is real (incl. VAT).
- **Preliminaries schedule** (Actions) — keep the **flat prelims %** (default) or switch to an **itemised built-up** schedule (site set-up, staff, welfare, insurances…), each a fixed value or a %-of-works. It rolls into the grand total and exports as its own **Preliminaries** section.
- **★ Export** (footer) opens the **Tender setup dialog** — a 4-tab form that turns the bill into a professional tender document:

| Tab | Captures |
|---|---|
| **1 · Project** | Employer name/address, project name/number/address, RIBA work stage, project type (20 sectors), Gross & Net Internal Area (m²). |
| **2 · Team** | QS firm/contact, Architect, Structural & Services Engineers, Principal Designer (CDM), PM, Employer's Agent, Main Contractor. |
| **3 · Contract** | Form of contract (14: JCT SBC/Q…/NEC4 A·B·C / FIDIC Red·Yellow·Silver / PPDA / bespoke), contract period, possession date, sectional completion, liquidated damages, defects-liability period, retention scheme, fluctuations basis, bond. |
| **4 · Pricing & Output** | Priced/Unpriced, revision, watermark (DRAFT/TENDER/CONFIDENTIAL…), include BOQ / summary / carbon sheets, currency (8: UGX/USD/GBP/EUR/KES/TZS/RWF/ZAR). |

Fill it once — it's saved to the project. The **Grand Total** in the footer is the
real, marked-up number (modeled + provisional + prelims + contingency + OH&profit +
variations).

## B8. Step 8 — Validate, freeze, sign off

| Button | How / Why |
|---|---|
| **Validate Cost** | Runs the 5-validator chain (missing material / untyped category / unpriced PROD / zero qty / stale). Read-only — your export gate. Fix errors before shipping. |
| **Save snapshot ▾** | **Save as new** → name it "Tender baseline". (Or **Overwrite** / **Mark as baseline**.) Every future change is now measured against it with **Compare**. **Always snapshot before a milestone.** |
| **Record QS Sign-off** | Records the QS name + role against the current snapshot. Until then, **every export sheet is footer-stamped DRAFT — UNCERTIFIED and the tabs tint red**. After sign-off, exports of *that snapshot* print **CERTIFIED**. |

## B9. Step 9 — Produce the deliverables

| Button | Output | Use it for |
|---|---|---|
| **★ Export** (footer) | Professional multi-sheet **Excel** (bill + summary + provisional + carbon), via the Tender dialog. | The deliverable you hand over. |
| **Cost Plan (NRM1)** → **★ New Cost Plan** | A PERT 3-point elemental estimate from a building-type benchmark set; seeds the budget. | Early-stage £/m² planning before the model is detailed. |
| **Compare vs BOQ** | RAG variance: NRM1 plan vs live BOQ by element. | "Are we above/below the elemental plan per section?" |
| **Export Cost Plan** | XLSX of the full NRM1 breakdown. | The cost-plan document. |
| **★ Export to ERP** | Flat import-ready **CSV** (WBS · CBS · cost code · qty · rate · total · level · location · source · IfcGuid) + optional **Primavera P6** activity-cost XML. | Push the bill into an ERP / planning tool. (Set codes first via **WBS / CBS map**.) |
| **Stamp IFC Qto** / footer **⤓ Export QTO IFC** | Populates IFC4 `Qto_*BaseQuantities` + `Pset_StingCost`. | So Cost-X / CostOS / Candy / Bluebeam read cost straight from your IFC. |
| **★ ICMS3 Report** | £/UGX + kgCO₂e + cost-per-kgCO₂e per ICMS group (CSV). | International cost+carbon reporting. |

## B10. Housekeeping & automation (the "Automation" group)

| Button | What it does | When |
|---|---|---|
| **★ Run Cost Workflow** | Runs a `WORKFLOW_BOQ_*.json` preset end-to-end (see §C-Playbooks). | One-click the whole sequence. |
| **Reload Rules** | Re-reads rate cards, take-off rules, measurement rules, carbon factors from disk. | After you edit any pricing/rule JSON. |
| **Clear Stale Flags** | Resets the stale marker on every element after a clean build. | So change-tracking doesn't false-alarm. |
| **Toggle Stale Marker** | Turns the "flag rows stale on geometry change" updater on/off. | Silence it during bulk edits. |
| **Migrate UGX → Neutral** / **Migrate ES v1 → v2** | One-shot data migrations (idempotent — safe to re-run). | Once, on legacy projects. |

---
---

# PART C — THE PROJECT MANAGEMENT JOB: run the build

This is the half most guides skip. Once the contract is awarded, the panel stops
being a pricing tool and becomes your **commercial cockpit**. All of this lives on
the **Actions** tab (and the **Schedule** tab, §C10).

## C0. The PM mindset & the monthly rhythm

The job of cost control is a loop: **Baseline → Commit → Measure → Forecast →
Report → Repeat.** A typical month:

```
Award (once):  Set Contract Sum → "Award" snapshot      ← freeze the BAC
Set-up (once): Import Programme → Critical Path → Cash-Flow S-Curve
               Commitments Register (POs/sub-contracts)

Every month:
  1. Set % Complete (per section)        ← measure progress
  2. Issue Cert → Cert Document → Approve ← pay the contractor
  3. Variation from Diff (any changes)   ← price change
  4. Import Actuals → Calculate EVM       ← cost/time health
  5. CVR Report + Anticipated Final Cost  ← margin + forecast
  6. Save snapshot "Interim Mxx"          ← keep the time machine fed

At close:      Fluctuations → Reconcile Provisionals → Final Account
```

## C1. Freeze the baseline — **Set Contract Sum**

The instant the contract is awarded, click **Set Contract Sum**. It freezes the
VAT-inclusive grand total as `COST_CONTRACT_SUM_UGX` and saves an **"Award"**
snapshot. **Why it matters:** every downstream tool — Anticipated Final Cost, EVM's
BAC, the Final Account — anchors on this one frozen figure, so the model can keep
growing without quietly re-baselining your budget.

## C2. Build the programme — **Programme & Cash-Flow (CPM)**

| Button | How | Why |
|---|---|---|
| **Import Programme** | File-pick an **MS Project `.xml`** or **Primavera `.xer` / `.xml`** → one converged parser → `_BIM_COORD/schedule.json`. | Bring the planner's CPM in so cost and time share one source of truth. |
| **★ Critical Path** | Runs the CPM forward/backward pass on the **Uganda working calendar** (override at `_BIM_COORD/working_calendar.json`) → critical path, total & free float, finish date. CSV out. | See the bottlenecks and how much slack you have; underpins any **EOT** claim. |
| **Model % Complete** | Derives each task's % from model reality — element-linked tasks from `ASS_PMT_PCT_COMPLETE_NR`, phase-named tasks from a phase-reached proxy. | Sync programme progress to what's actually modelled/built — no manual re-entry. |
| **★ Cash-Flow S-Curve** | Time-phases each task's value over its own dates into a monthly **S-curve**. CSV out. | **This becomes the real EVM Planned Value (PV)** — so your schedule variance is genuine, not hand-keyed. |

## C3. Commit the spend — **Commitments Register**

Author your POs / sub-contracts in `<BIM manager>/commitments.json` (a template is
seeded if absent), then **Commitments Register** rolls them up against the live BOQ
budget by NRM2 section and flags **over-commits**. **Why:** the budget is what you
*planned*; commitments are what you've *promised*. The gap between them is your
remaining buying power.

## C4. The monthly valuation cycle — **Payment Certificates**

This is the heartbeat of cost control. Run it once a month, in order:

| # | Button | How | Why |
|---|---|---|---|
| 1 | **Set % Complete** | Pick a section + a % (0,5,…100). Stamps `ASS_PMT_PCT_COMPLETE_NR` on that section's elements. | The single input that drives both the cert and EVM. |
| 2 | **★ Issue Cert** | Pick the contract form (JCT/NEC4/FIDIC), cert number, period. Builds a draft interim cert: SOV from BOQ sections, weighted % complete, agreed variations appended; **retention auto-halves**. | The monthly "pay the contractor this much" document. |
| 3 | **Cert Document** | Renders a numbered interim certificate as a formatted **XLSX** (SOV + retention/MOS/previous/net/VAT/payable + signatures). | The formal certificate you issue to site. |
| 4 | **Approve Cert** | Advances the state machine: Draft → Issued → Agreed; stamps signer names. | Moves the cert through sign-off; "certified to date" feeds the Final Account. |
| 5 | **Cert Register** | CSV register of every cert (gross / retention / payable / signers / cumulative). | Your month-end payment audit trail. |
| — | **Release Retention** | Pick first or second **moiety**; surfaces the live retention balance. | Release the withheld fund at Practical Completion, then end of Defects Liability. |

> The Schedule tab's **⤿ Sync % from cert** copies the latest cert's overall % into
> the latest reporting period, so the programme and the certs stay one number (a
> "Cert %" chip turns green when they agree).

## C5. Handle change — **Variations & Star Rates**

When the design changes, you turn the change into money:

| Button | How | Why |
|---|---|---|
| **★ Variation from Diff** | Pick two snapshots (baseline A, revised B); the tool diffs them and mints a **draft VO** with all changed items. You set contract form, **kind** (numbered **VO-AI / VO-CE / VO-EI / VO-CC**), **reason**, **liability**, and **EOT days**. | Turn a model change into a numbered, classified VO — so the claim flows to the right party (employer absorbs design changes, contractor absorbs their own errors). |
| **Star Rate Build-Up** | Author a rate from first principles — labour (per task) + plant + materials + **OH %** + **profit %**. Saved as a reusable JSON sidecar. | Price a brand-new or one-off item when no standard rate fits. |
| **Approve / Reject / Incorporate** | Advance a VO's state (Draft→Approved→Incorporated, or Rejected). Records who + when. | Gate VOs: **approved** VOs flow straight into AFC & Final Account; rejected ones drop out of the forecast. |
| **VO Register** | CSV of every VO (number / kind / reason / liability / EOT / status / value / signers). | The contract's change log + EOT entitlement breakdown. |
| **Reclassify Legacy** | Walk old VOs still on default *(Other / Employer)* and set a real reason + liability. | Clean up migrated VOs so claims route correctly. |

## C6. Control & forecast the cost

This is where you answer *"where will this job land?"*

| Button | How | Why |
|---|---|---|
| **★ Calculate EVM** | Computes every PMI metric — **BAC** (frozen contract sum + agreed VOs), **PV** (S-curve), **EV** (% × BAC), **AC** (actuals) → CV, SV, **CPI**, **SPI**, **EAC**, ETC — with Green/Amber/Red gates at CPI 0.95/1.00. | The monthly one-glance health check: over/under on **cost** and **time**, and the forecast final cost. |
| **Import Actuals** | Sums the latest CSV under `_bim_manager/actuals/` (Date, Section, Amount; dedup by content). | Feed real spend (from accounts) into **AC**. |
| **Export S-Curve** | CSV of every EVM period (BAC/PV/EV/AC/CV/SV/CPI/SPI/EAC/…). | Drop into any chart tool for the board pack. |
| **★ CVR Report** | Cost-Value Reconciliation at today's cut-off: value of work done vs cost vs certified → gross margin, margin %, **WIP** (over/under-claim), forecast out-turn margin, claim position (Strong / At-risk / Over-claimed). | The commercial lead's monthly cash & margin position. |
| **Cost-to-Complete (lines)** | Per BOQ line: budget × remaining %, with a CPI-implied productivity factor where actuals exist. CSV. | Line-level forecast for detailed budget variance. |
| **★ Anticipated Final Cost** | Modelled works + manual/PS ± **agreed** VOs ± **pending** VOs ± PS movement ± fluctuations vs budget. Screen + XLSX waterfall. | The sponsor's monthly "what will this cost if trends hold?" report. |
| **Loss & Expense** | Sums EOT days from agreed VOs; you give a weekly prelims rate → prolongation + OH&P + disruption + finance. Suggests a CompensationEvent VO. | Quantify a delay claim — delays cost money, and this carries it into a cert. |

## C7. Index moves & close-out

| Button | How | Why |
|---|---|---|
| **Fluctuations** | Edit a basket at `_BIM_COORD/fluctuations.json` (indices, weights, adjustable %); computes recovery (NEDO/BCIS formula or UBOS CPI). | Recover (or give back) price-rise movement without manual guessing. |
| **Reconcile Provisionals** | For each PS, record the **final-account actual** against the **frozen original allowance** + a note. Σ(actual − original) feeds the AFC. | Replace PS guesses with reality as costs firm up. |
| **Final Account** | Signed reconciliation: contract sum ± PS movement ± agreed VOs ± fluctuations → Final Account. XLSX with a waterfall + variations & provisional annexures; checks "certified to date". | The signed close-out statement — what the employer owes at project close. |

## C8. Tender adjudication (when **you** award the work)

**Adjudicate Tenders** — file-pick **N priced QS-Bill returns** (one `.xlsx` per
bidder). The tool joins each to your live BOQ by the stable `_key`, runs
**arithmetic / zero-rate / outlier / front-loading** checks, and **ranks bidders by
corrected total** with a recommendation. Optionally **stamps the winner's rates** as
your contract baseline. **Why:** pick the genuinely most-advantageous bid (the
lowest *corrected* tender), with a defensible audit trail.

## C9. Delivery & risk (ISO 19650)

| Button | How | Why |
|---|---|---|
| **Raise Risk** | Raise a risk against a selected element/zone (or project-level): category + 5×5 likelihood/impact. Persists to `risks.json` + the tamper-evident audit log. | Capture risk where it lives, with an auditable trail. |
| **★ Risk Register** | Rolls up: RAG counts on **residual** (post-mitigation) score, open-red exposure, top risks. CSV. | The monthly risk report for the project board. |
| **MIDP Drift** | Load a MIDP/TIDP CSV (Code, Title, Discipline, Milestone, PlannedDate, RequiredSuitability), join to the live deliverables lifecycle → on-track / at-risk / overdue / suitability-short. CSV. | Track information-delivery against the plan (ISO 19650-2). |

## C10. The Schedule tab — your on-site cockpit

The Schedule tab puts programme + EVM in one place. Three editable grids, a toolbar,
an EVM strip, and an S-curve canvas.

**Toolbar:**

| Button | What it does |
|---|---|
| **As-of (date)** + **Actual cost (ACWP)** fields | Set the EVM cut-off and cumulative actual. |
| **↻ Recalculate** | Save edits + recompute every EVM metric and the S-curve. |
| **＋ Phase / ＋ Period / ＋ Milestone** | Add rows to the three grids. |
| **⤿ Sync % from cert** | Pull the latest cert's overall % into the latest period. |
| **⟳ Seed from Revit phases** | Bootstrap the Phases grid from the model's phases. |
| **⤓ Import MSP/P6 XML** | Same converged programme import as §C2. |
| **⤓ Import actuals** | Sum actuals CSV into the ACWP field. |
| **⛏ Stamp 4D dates on model** | Write phase dates onto model elements by task name. |
| **↗ Export schedule/EVM** | CSV/XLSX, optionally with the S-curve chart. |

**Grids:** **Phases** (name ★, start, end, % complete, planned cost) · **Periods**
(month-end, overall %, cumulative actual — drives the S-curve) · **Milestones**
(name, date, done — turns **red** if past-due and not done).

**EVM strip:** live PV · EV · AC · SPI · CPI · EAC · ETC · VAC (+ a Cert % match
chip). **S-curve canvas:** time vs cumulative cost — **PV blue, EV green, AC red**.

## Playbooks — one button runs the whole sequence

**Actions → ★ Run Cost Workflow** picks a preset. The shipped ones:

| Preset | Does | Use it… |
|---|---|---|
| **Full refresh and publish** | Validate → rebuild BOQ → snapshot → export XLSX → push to server. Halts on validation errors. | At a clean re-issue. |
| **Tender pack production** | Validation gate → full build → snapshot "Tender" → sheet register → professional XLSX with preambles → drawings register. | At RIBA Stage 4 tender. |
| **Monthly valuation** | Rebuild → snapshot "Interim" → XLSX + server → refresh 4D cash-flow. (Skips the full validator chain — assumes a clean baseline.) | Every month on a live job. |
| **Cost lifecycle** | Reload rules → rebuild → validate (export gate) → snapshot (checksum + push) → drift-check → route/export (ERP CSV + P6 XML). | The full manual cost sequence in one click. |

---
---

# PART D — PUTTING IT TOGETHER

## D1. Huge-project survival kit

A large project is usually **many linked Revit models** (arch + structure + MEP, or
building-by-building). The cost must cover all of them.

- **⛓ Links** (header) → tick exactly which **loaded** links to include. Your selection is **saved per project**. A model placed many times can optionally be counted **× the number of placements**.
- **Group → Source model** → read the bill as **one block per model** (host, then each link) — see each consultant's package separately.
- **Discipline filters** + **Level / Zone / Location / WBS / CBS** grouping → slice a massive bill into reviewable chunks.
- **Snapshots per data-drop** → track each model revision's cost impact with **Compare**.

> **Linked rows are read-only for cost write-back** (tagged `[Linked: <model>]`):
> they add to quantities, cost and carbon, but you can't cost-stamp or select them in
> your host model. That's deliberate and safe.

## D2. A full worked example — a 40-storey tower, tender → handover

> You've been handed a federated tower (arch + structure + MEP links) and told
> *"price it, then run the commercial side."*

**Pricing (Part B):**
1. **Create Parameters** (bind cost/carbon/progress). **⛓ Links** → tick all three.
2. **✦ Cost Setup** → Model Readiness = Good; Budget UGX 92bn; currency UGX; pricing path = QS round-trip.
3. **↻ Refresh (full)**. Health 78, Coverage 96%. **Rate Gap Report** → 140 items, UGX 3.1bn at risk → CSV to the QS.
4. **★ Export QS Bill** → QS prices it → **Import QS Bill** (review diff, apply). Avg confidence 90, Coverage 100%.
5. **Add Manual / PS** → "PS UGX 1.2bn — façade access". **Preliminaries schedule** → itemised (12 lines).
6. **★ Export** → Tender dialog (employer, team, JCT SBC/Q, 24-month period, 5% dual retention) → professional XLSX.
7. **Validate Cost** (clean). **Save snapshot "Tender baseline"**. **Record QS Sign-off** → exports print **CERTIFIED**.

**Award & set-up (Part C):**
8. Award. **Set Contract Sum** → "Award" snapshot (BAC frozen at UGX 90.4bn).
9. **Import Programme** (P6 `.xer`) → **★ Critical Path** (finish + float) → **★ Cash-Flow S-Curve** (PV).
10. Author `commitments.json` → **Commitments Register** (POs vs budget).

**Monthly (Part C, ×24):**
11. **Set % Complete** per section → **★ Issue Cert** (JCT, Cert 07) → **Cert Document** → **Approve Cert**.
12. A change lands: **★ Variation from Diff** (snapshot Award vs current) → VO-EI, employer liability, +10 EOT days → **Approve**.
13. **Import Actuals** → **★ Calculate EVM**: CPI 0.97 (amber), SPI 1.02. **★ CVR Report**: margin 6.1%, slightly under-claimed. **★ Anticipated Final Cost** → UGX 91.8bn (UGX 1.4bn over) — flagged to sponsor.
14. **Save snapshot "Interim M07"**. (Or just **Run Cost Workflow → Monthly valuation**.)

**Close-out:**
15. **Loss & Expense** on the 10 EOT days → CompensationEvent VO. **Fluctuations** (UBOS CPI). **Reconcile Provisionals** (façade PS actual). **Final Account** → signed XLSX. Done.

## D3. The monthly rhythm — one-page checklist

```
☐ Set % Complete (each section)            ☐ Variation from Diff (any changes) → Approve
☐ Issue Cert → Cert Document → Approve      ☐ Import Actuals → Calculate EVM
☐ Cert Register (file it)                   ☐ CVR Report  ☐ Anticipated Final Cost
☐ Sync % from cert (Schedule tab)           ☐ Risk Register  ☐ MIDP Drift
☐ Save snapshot "Interim Mxx"   — or just:  ☐ Run Cost Workflow → Monthly valuation
```

## D4. Measurement standards

**Set Standard** switches the "filing system": **NRM2** (UK new-rules, default),
**CESMM4**, **POMI**, **ICMS3** (international cost+carbon), or **MMHW** (highways).
**Standard Preview** shows how common categories get classified/unitised under each.
For most projects, **leave it on NRM2**; switch only if the client/contract demands.

## D5. Where everything is saved (so a PM knows the audit trail)

Under `<project>/_BIM_COORD/` (and sub-folders):

| File | Written by |
|---|---|
| `snapshots/*.json` | Save snapshot |
| `schedule.json`, `working_calendar.json`, `cash_flow_scurve.json` | Programme / CPM / S-curve |
| `payment_certs/*` | Issue / Approve cert |
| `variations/*.json`, `star_rates/*.json` | Variations / star rates |
| `commitments.json`, `final_account.json`, `fluctuations.json`, `tender_adjudication.json` | Commitments / final account / fluctuations / adjudication |
| `cost_plans/*.json`, `boq_prelims.json`, `boq_provisional_trail.json`, `boq_wbs_map.json` | Cost plan / prelims / PS trail / WBS-CBS |
| `risks.json`, `boq_ui_state.json` | Risk register / panel UI state |
| `actuals/*.csv` *(under `_bim_manager/`)* | Import Actuals |

Everything is plain JSON/CSV — auditable, diff-able, and survives reopen.

## D6. Honest gaps — what's still thin

We won't hide these:

1. **You still need a human QS / planner for contractual sign-off.** This tool does
   90% of the work fast and produces a strong, defensible picture — it does **not**
   replace professional certification. Every export is stamped **DRAFT — UNCERTIFIED**
   (red tabs) until a QS records a sign-off via **Record QS Sign-off**.
2. **The Schedule/EVM tab is directional, not Primavera/MSP.** Great for the monthly
   commercial view; not a replacement for full programme management.
3. **Some PM tools need a small JSON authored first.** Commitments
   (`commitments.json`), fluctuations basket (`fluctuations.json`) and the MIDP CSV
   are read from files you (or the QS) seed — the panel seeds a template, you fill it.
4. **`CostPlan_Open` is a diagnostic summary only** (the in-panel cost-plan editor is
   a follow-on); use **New / Compare / Export** for the full flow today.
5. **Contingency & overhead are still flat percentages** (prelims can now be a built-up
   schedule; the rest of the markup is a %). **SYS-token rate matching** is deferred.
6. **Big-model performance:** the take-off runs on Revit's single API thread (it must —
   it reads geometry and ends in a parameter-write), so a very large model takes a few
   seconds. A progress dialog shows it's working. **Refresh deliberately.**

## D7. Quick-reference cheat sheets

**QS — price the building**

| I want to… | Do this |
|---|---|
| Bind cost/carbon params | **Create Parameters** (once) |
| Get set up | **✦ Cost Setup** |
| Build/refresh the bill | **↻ Refresh** (full: **↻ Refresh (full)**) |
| Judge trust | Read **Coverage** + **BOQ Health** + confidence |
| Find unpriced items | **Rate Gap Report** |
| Price properly | **Export QS Bill → (QS fills) → Import QS Bill** |
| Price one item | Click its **Rate** cell, type |
| Live market rates | **Fetch live rates** |
| Add un-modelled money | **Add Manual / PS / Daywork** |
| Markups + tax | **Preliminaries schedule** + **★ Export** (Tender dialog) |
| Set the target | **Set Budget** |
| Freeze a version | **Save snapshot** |
| See what changed | **Compare** two snapshots |
| Hand it over | **★ Export** / **Export to ERP** / **Export QTO IFC** / **ICMS3 Report** |

**PM — run the build**

| I want to… | Do this |
|---|---|
| Lock the budget at award | **Set Contract Sum** |
| Bring in the programme | **Import Programme** → **★ Critical Path** → **★ Cash-Flow S-Curve** |
| Track promised spend | **Commitments Register** |
| Pay the contractor | **Set % Complete → ★ Issue Cert → Cert Document → Approve Cert** |
| Release retention | **Release Retention** |
| Price a change | **★ Variation from Diff** (+ **Star Rate Build-Up**) → **Approve** |
| Health check | **★ Calculate EVM** (CPI/SPI/EAC) |
| Margin & claim position | **★ CVR Report** |
| Forecast final cost | **★ Anticipated Final Cost** + **Cost-to-Complete (lines)** |
| Delay claim | **Loss & Expense** |
| Close out | **Fluctuations → Reconcile Provisionals → Final Account** |
| Award a tender | **Adjudicate Tenders** |
| Risk & delivery | **★ Risk Register** · **MIDP Drift** |
| One-click month | **Run Cost Workflow → Monthly valuation** |

---

*Found something confusing, or a button that doesn't behave as written here? That's
a gap — tell us and it goes in §D6 and on the build list. This guide grows with the
tool.*
