# Quantity Surveying with StingTools — A Layman's Guide
### For the Revit user who has never costed a thing, and just got handed a huge project

> **Living document.** StingTools is still being built. Features marked
> **(coming)** don't exist yet; the **Gaps** section at the end lists what's thin
> so you're never surprised. We update this guide every time we ship a new
> function.

---

## 0. Don't panic — here's the whole idea in 60 seconds

You know how to model a building in Revit. A **Quantity Surveyor (QS)** is the
person who turns that building into **money**: *how much* of each material there
is, *what it costs*, and *how that cost moves* as the job runs.

The **BOQ & Cost Manager** is the bridge. It reads your Revit model, **counts
everything** (a "take-off"), **groups it** the way QSs expect, **multiplies by a
rate**, and gives you a priced **Bill of Quantities (BOQ)** plus the tools to run
the cost side of the whole project — tendering, paying contractors, tracking
changes.

**You do not need to become a QS.** You need to: keep your model honest, press a
few buttons in the right order, and understand ~12 words. This guide teaches both.

---

## 1. The 12 words you need (skim now, refer back later)

| Word | Plain meaning |
|---|---|
| **BOQ** (Bill of Quantities) | The priced shopping list of the whole building, line by line. |
| **Take-off** | Measuring quantities (lengths, areas, volumes, counts) from the model. The tool does this automatically. |
| **Rate** | The price for **one unit** (e.g. UGX 166,500 per m² of ceiling). Quantity × Rate = line cost. |
| **NRM2** | The UK standard "filing system" for a BOQ — which work goes in which numbered section (concrete, masonry, finishes…). It's just an agreed order so everyone reads the bill the same way. |
| **Measured work** | Items that come from the model (real geometry). The bulk of the bill. |
| **Provisional sum (PS)** | Money set aside for something not yet designed/known (e.g. "allow UGX 50M for external works"). A placeholder you reconcile later. |
| **Daywork** | Work paid by time + materials (labour hours, plant, materials) rather than a fixed rate — for odd jobs. |
| **Prelims / Contingency / Overhead** | Markups added on top of the measured cost: site running costs, a safety buffer, and the contractor's margin. |
| **VAT** | Tax added at the end. |
| **Snapshot** | A frozen copy of the bill at a moment in time, so you can compare "then vs now". |
| **Variation (VO)** | An approved change to the works after the contract — adds or removes cost. |
| **Payment certificate** | The monthly "the contractor has done X% — pay them this much" document. |
| **EVM** (Earned Value) | A health check: are we ahead/behind on cost and on time? |

That's the whole vocabulary. Everything below uses only these words.

---

## 2. The golden rule: the bill is only as good as the model

The tool measures **what you modelled**. So:

- A wall modelled as a generic mass → measured as a mass, not as blockwork.
- A room with no Room object → no floor finish measured.
- A placeholder family with no real geometry → no quantity.

You don't need a perfect model — you need an **honest** one, and the tool tells you
how honest it is with two numbers (next section): **Coverage** and **BOQ Health**.
**Garbage in, garbage out** is the one law of QS. Your Revit skills are exactly
what make the cost good.

---

## 3. Open the workspace and read the dashboard

Open Revit → the **STING Tools** ribbon/panel → **BOQ & Cost Manager**. (It's a
docked window you can keep open while you work.)

Across the top you get **seven gauges**. Learn these — they're your instrument
panel:

| Gauge | What it tells you | What "good" looks like |
|---|---|---|
| **Project Budget** | The target you typed in (via **Set Budget**). | Set it early so **Variance** means something. |
| **Modeled Cost** | Cost of everything measured from the model. | The core number. |
| **Provisional / Manual** | Cost of PS + daywork + manual rows you added. | Grows as you fill design gaps. |
| **Variance** | Budget − total. Are you over or under? | Near zero / under budget. |
| **Coverage** | % of items that have a proper description/classification. | Aim 100%. Low = the bill is half-labelled. |
| **BOQ Health** | A 0–100 score of how trustworthy the bill is. | 80+. Below ~70, fix the model/rates first. |
| **Embodied Carbon** | The CO₂ "cost" of the materials (sustainability). | Lower is better; informational. |

Just under the gauges: **"Description coverage 100% (3/3) · Avg rate confidence 90"**.
**Rate confidence** is *how sure the tool is about each price* (90 = strong,
e.g. you priced it; low = it guessed from a default). High confidence + high
coverage = a bill you can hand over.

The header also has: **UGX / USD** (currency toggle), **⛓ Links** (include other
linked Revit models — see §8), **Refresh** (rebuild the bill from the live model),
and **Set Budget**.

---

## 4. The four tabs (what each is for)

| Tab | Use it to… |
|---|---|
| **Bill of Quantities** | Read/price the bill, grouped into NRM2 work sections. This is the deliverable. |
| **Materials** | See the same money grouped **by material/category** instead of work section — handy for procurement ("how much concrete in total?"). |
| **Actions** | The workflow buttons — every cost task (tender, certs, variations…) in one place. Results show **inline** in the right-hand pane. |
| **Schedule** | 4D/5D: phase dates, a cash-flow curve, and EVM (cost/time health). *(New — basic; growing.)* |

### Reading one BOQ row
`Ref · Item / description · Qty · Unit · Rate · Total · Level · Location · Src · Model · Conf · CO₂ · Note`

- **Ref** = the NRM2 line number (e.g. 19.1.1).
- **Item / description** = the auto-written, QS-style sentence ("Supply and fix
  suspended ceiling system…"). You can edit it inline.
- **Qty / Unit** = measured amount (43.013 m²).
- **Rate** = price per unit — **you can type over it** to price the item.
- **Total** = Qty × Rate.
- **Src / Model** = where it came from: **Model** = your host model; **Host** or a
  link name if it came from a linked model.
- **Conf** = rate confidence (see §3).
- **Note** = e.g. "Aggregated 5" (this one row stands for 5 identical elements).

### The toolbar above the tabs
- **Filter** box + **ALL / A / S / M / E / P / FP / PS** = show only one discipline
  (A=Architecture, S=Structure, M=Mechanical, E=Electrical, P=Plumbing…).
- **Group** dropdown = re-file the bill: **Work section (NRM2)**, **Level**,
  **Zone**, **Location**, or **Source model** (host vs each link as its own block).
- **Expand all / Collapse all**, **Description** (show/hide the long text),
  **Columns** (show/hide columns like Model, Carbon, Confidence), **Profile**.
- **Snapshot** dropdown + **Save snapshot** + **Compare** (see §7).

---

## 5. Your step-by-step playbook for a huge project

The **Actions** tab is organised as phases **P0 → P8**. Here's the human order to
work through them. Do them top to bottom the first time.

### Step 1 — Make the model costable (before you touch the panel)
- Place **Rooms/Spaces** (drives finishes, area-based items).
- Set **Phases** correctly (New / Existing / Demolished) — demolished items
  shouldn't be priced as new.
- Make sure elements use **real types**, not generic masses, where it matters.
- *Tip:* you don't need 100% — just press **Refresh** and let **Coverage / Health**
  tell you where the holes are.

### Step 2 — Build the bill: **Refresh**
Press **↻ Refresh**. The tool takes off everything and fills the BOQ tab. Read the
seven gauges. If **Coverage** or **Health** is low, look at the worst sections and
fix the model, then Refresh again.

### Step 3 — Sanity-check the quantities (not the prices yet)
- Use **Group → Work section** and skim each section. Do the quantities look sane?
  (A 40-storey tower with 12 m² of concrete = something's wrong.)
- Use the **discipline filters** to review one trade at a time.
- Watch for "**Qty 1 each**" junk rows — those are usually 2D/annotation leaking in
  (the tool already excludes most, but flag anything odd).

### Step 4 — Get prices in (three ways, pick what suits)
1. **Automatic** — the tool already applies rates from its rate library where it
   can (that's why some rows already have a Rate + confidence). Nothing to do.
2. **QS Round-trip (P3)** — the professional way: **Export QS Bill** → hand the
   Excel to a real QS (or a rate book) → they fill the **Rate** column → **Import
   QS Bill**. Each row carries a hidden stable key, so prices land back on the
   right rows even after you re-Refresh. **This is how you price a big job without
   being a QS — you borrow one via Excel.**
3. **By hand** — click a **Rate** cell in the BOQ tab and type the price. Good for a
   handful of items.

### Step 5 — Add what the model can't show (P3)
**Add Manual / PS / Daywork** for:
- **Provisional sums** — "allow UGX 80M for the lift installation (design pending)".
- **Dayworks** — time-and-material allowances.
- **Manual rows** — anything real that isn't modelled (e.g. site clearance).
These show up under **Provisional / Manual** in the gauges.

### Step 6 — Add the markups and a target (P4 / Tender)
- **Set Budget** — type your project budget so **Variance** is meaningful.
- **Tender BOQ** (footer) / the tender settings — add **prelims %, contingency %,
  overhead/profit %, VAT**. The **Grand Total** at the bottom is the real,
  marked-up number you'd put in a tender.

### Step 7 — Freeze a baseline: **Save snapshot**
**Save snapshot** → name it "Tender baseline". Now every future change can be
measured against it with **Compare**. *Always snapshot before a milestone.*

### Step 8 — Produce the deliverable
- **Export** (footer) → a multi-sheet **Excel BOQ** (bill + materials + provisional
  sums + carbon).
- **Cost Plan — NRM1 (P4)** → **New Cost Plan** for early-stage elemental planning
  (£/m² benchmarks by building type), **Compare vs BOQ**, **Export Cost Plan**.
- **QTO IFC** (footer) → an IFC4 file with quantities for an external estimator
  (CostX / iTWO).

### Then — run the job (ongoing, monthly)
- **Payment Certs (P5.1)** — **Set % Complete** → **Issue Cert** → **Approve Cert**.
  The monthly "pay the contractor this much" cycle.
- **Variations + Star Rates (P5.2)** — when the design changes: **Variation from
  Diff** auto-detects the change between two snapshots and mints a VO; **Star Rate
  Build-Up** prices a brand-new item from first principles.
- **EVM (P5.3)** — **Calculate EVM** → are you ahead/behind on cost and time
  (CPI/SPI)? **Import Actuals** to feed real spend.
- **Cost Report (P4.4)** — **Anticipated Final Cost**: where is this project
  heading vs budget? **Reconcile Provisionals**: replace PS guesses with real
  costs as they firm up.

> **Where do results appear?** On the **Actions** tab, click a button and its
> result renders in the **right-hand pane** (no pop-ups). Exports show an **Open
> file** button there.

---

## 6. Measurement standards (P6) — NRM2 vs the rest

**Set Standard** lets you switch the "filing system": **NRM2** (UK new-rules, the
default), **SMM7** (older UK), or **CIOS/others**. **Standard Preview** shows how a
few items would be described/grouped under each. For most projects, **leave it on
NRM2**. Switch only if your client demands a specific standard.

---

## 7. Snapshots & Compare — your time machine

- **Save snapshot** at every milestone (tender, each design freeze, monthly).
- **Compare** two snapshots → see exactly which lines changed and by how much.
- This is how you answer "**why did the cost go up UGX 200M since last month?**" —
  the lifeblood of cost control on a big job.

---

## 8. Huge-project survival kit

A large project is usually **many linked Revit models** (architecture + structure +
MEP, or building-by-building). The cost must cover all of them.

- **⛓ Links** (header) → tick exactly which **loaded** linked models to include in
  the bill. Your selection is **saved per project** (survives reopen). A model
  placed many times can optionally be counted **× the number of placements**.
- **Group → Source model** → read the bill as **one block per model** (Host model,
  then each link) — so you can see each consultant's package separately.
- **Discipline filters** + **Level / Zone / Location** grouping → slice a massive
  bill into reviewable chunks.
- **Snapshots** per data-drop → track each model revision's cost impact.

> **Note on linked rows:** items from links are **read-only** for the cost
> write-back (they're tagged `[Linked: <model>]`) — they add to quantities, cost
> and carbon, but you can't cost-stamp or select them in your host model. That's
> deliberate and safe.

---

## 9. A 6-line worked example

> You've been handed a modelled tower and told "give us a cost".
1. **Refresh.** Health = 78. Coverage = 100%. Good enough to start.
2. **Group → Work section**, skim: concrete, masonry, finishes all look sane.
3. **Export QS Bill** → email to the QS → they price it → **Import QS Bill**.
   Avg confidence jumps to 90.
4. **Add Manual / PS** → "Provisional UGX 80M — lifts". **Set Budget** = UGX 9.5bn.
5. **Tender BOQ** → prelims 12%, contingency 10%, overhead 8%, VAT. Grand Total
   = UGX 9.31bn. **Save snapshot "Tender baseline"**.
6. **Export** → hand over the Excel BOQ. Done. Next month: **Set % Complete →
   Issue Cert**.

---

## 10. Honest gaps — what's thin today (we're still building)

This is the part a real guide hides and you find out the hard way. We won't.

1. ~~**Rate-library coverage is partial.**~~ ✓ **Closed (G1).** Coverage is still
   partial — but **Actions → Rate Gap Report** now lists exactly which modelled
   items still need a price (no rate / low confidence / defaulted), totals the
   *value at risk*, and exports a CSV worklist to hand the QS. Still **do the QS
   Round-trip** (Step 4.2) on any real job.
2. ~~**No labour/plant/material split per rate yet.**~~ ✓ **Closed (G2 of this list /
   G4).** A rate can now carry an optional **labour + plant + material** split where
   the source provides one — add `labour` / `plant` / `material` columns to a
   project rate-card entry (`_BIM_COORD/rate_card.json`). Rows with a split show
   optional **Labour / Plant / Material** grid columns (toggle via **▦ Columns**)
   and export to the Item Schedule; rows without a split are unchanged (rate stays
   one number). **Actions → Labour rollup** sums the split by NRM2 section and
   rolls up labour **hours by trade**.
3. ~~**Prelims/contingency/overhead are flat percentages.**~~ ✓ **Closed (G3) for
   prelims.** Prelims can now be a **built-up schedule**: **Actions → Preliminaries
   schedule** lets you keep the flat % (still the default) or switch to itemised
   lines (site set-up, staff, welfare, insurances…), each a fixed value or a
   %-of-works. The active basis rolls into the grand total and exports as its own
   **Preliminaries** section in the XLSX. *(Contingency + overhead are still flat
   percentages.)*
4. **Carbon factors are a starter set** (ICE database + overrides). Treat embodied
   carbon as *indicative*, not a verified EPD-grade figure.
5. **The Schedule / EVM tab (P2) is new and basic** — phase grid + a simple
   S-curve + headline EVM. It is **not** a full 4D programme tool; treat SPI/CPI as
   a directional health check until it matures.
6. ~~**Big-model performance.**~~ ✓ **Closed (G8).** On a large host model
   (≥ 1,500 elements, configurable via `COST_BIG_MODEL_THRESHOLD`), **Refresh** now
   shows a "Building bill of quantities…" progress dialog so it reads as working,
   not frozen. The take-off itself still runs on Revit's API thread — it must,
   because it reads element parameters/geometry and ends with a parameter-write
   transaction, and the Revit API is single-threaded — so a very large model can
   still take a few seconds. A full background rebuild was deliberately deferred:
   the per-element costing IS the heavy compute and is inseparable from the API
   reads, so it can't move off-thread without re-architecting the take-off engine.
   Refresh deliberately, not constantly.
7. **No first-run wizard.** There's no guided "set up costing" walkthrough yet —
   this document is the stand-in. *(Coming.)*
8. ~~**Provisional-sum reconciliation** is light — you replace the number.~~
   ✓ **Closed (G2).** **Actions → Reconcile Provisionals** now opens an inline
   editable trail: each provisional sum keeps its **frozen original allowance**
   and you record the **final-account actual** + a note. Each save appends a dated
   adjustment to `_BIM_COORD/boq_provisionals.json` (persists across reopen), and
   Σ(actual − original) — the **provisional-sum movement** — shows in the coverage
   strip and is folded into the **Anticipated Final Cost**.
9. **You still need a human QS for contractual sign-off.** This tool produces a
   strong, defensible cost picture fast — it does **not** replace professional
   certification on a real contract. Use it to do 90% of the work and to make the
   QS's job an hour, not a week. *(G9 closed: every export is now stamped
   **DRAFT — UNCERTIFIED** until a QS records a sign-off via **Actions → Record
   QS Sign-off**, which prints exports of that snapshot as **CERTIFIED**. The
   draft/certified mark now appears as a **footer on every sheet** — and unsigned
   exports tint every sheet tab red — so the status can't be missed on any tab.)*

---

## 11. Quick-reference cheat sheet

| I want to… | Do this |
|---|---|
| Build/refresh the bill | **↻ Refresh** |
| See how trustworthy it is | Read **Coverage** + **BOQ Health** + **rate confidence** |
| Price it properly | **Export QS Bill → (QS fills Excel) → Import QS Bill** |
| Price one item | Click its **Rate** cell, type |
| Add money for un-modelled stuff | **Add Manual / PS / Daywork** |
| Add markups + tax | **Tender BOQ** / tender settings |
| Set the target | **Set Budget** |
| Freeze a version | **Save snapshot** |
| See what changed | **Compare** two snapshots |
| Include other models | **⛓ Links** → tick them |
| Read per-consultant | **Group → Source model** |
| Hand it over | **Export** (Excel) / **QTO IFC** (estimator) |
| Pay the contractor | **Set % Complete → Issue Cert** |
| Handle a change | **Variation from Diff** |
| Check project health | **Calculate EVM** / **Anticipated Final Cost** |

---

*Found something confusing, or a button that doesn't behave as written here? That's
a gap — tell us and it goes in §10 and on the build list. This guide grows with the
tool.*
