# A Beginner's Guide to Sustainability & Carbon Costing in STINGTOOLS

*From "what does green even mean?" to mastering the STING Sustainability Center.*

This guide assumes **zero prior knowledge**. If you have never heard of LEED, EDGE,
embodied carbon or an "EUI", you are in the right place. We start with the very
basics, build up the ideas one at a time, then show you exactly how to use the
STING Sustainability Center to put real numbers against a building. The last third
of the guide ("Mastering the topic") goes deep enough to make you the person in the
room who actually understands what the numbers mean.

> **How to read this guide.** Read Parts 1–3 once to understand the *ideas*. Keep
> Part 4 open beside Revit the first few times you use the panel. Come back to
> Part 5 when you want to stop trusting the tool blindly and start judging its
> answers yourself.

---

## Table of contents

1. [Part 1 — Why any of this matters (the absolute basics)](#part-1)
2. [Part 2 — Green-building certifications: LEED and EDGE](#part-2)
3. [Part 3 — The engineering ideas behind the numbers](#part-3)
4. [Part 4 — Carbon costing with STINGTOOLS (the hands-on walkthrough)](#part-4)
5. [Part 5 — Mastering the topic (advanced lessons)](#part-5)
6. [Part 6 — Glossary, checklists & references](#part-6)

---

<a name="part-1"></a>
## Part 1 — Why any of this matters (the absolute basics)

### 1.1 What is a "green building"?

A green building is simply a building that does **more with less harm**: it uses
less energy and water to run, and it is built from materials that took less damage
to the planet to produce. That's it. Everything else — LEED, EDGE, carbon
factors, EUIs — is just a way of **measuring** how green a building actually is,
so you can prove it instead of just claiming it.

Think of it like the fuel-economy sticker on a car. A car salesman can *say* a car
is "efficient", but the sticker gives you a number (litres per 100 km) you can
compare against other cars. Green-building certifications are the fuel-economy
sticker for buildings.

### 1.2 Why should a designer care?

Buildings are responsible for roughly **40% of global energy-related carbon
emissions**. Roughly:

- **~28%** is *operational* — the energy a building burns every day to run lights,
  air-conditioning, lifts, hot water.
- **~11%** is *embodied* — the carbon released **once**, up front, to make and
  transport the concrete, steel, glass, brick and insulation the building is made of.

So a building has **two carbon bills**:

| Bill | When it's "paid" | Driven by |
|---|---|---|
| **Operational carbon** | Every year, for the building's whole life | How efficient the design is (insulation, glazing, plant, lighting) |
| **Embodied carbon** | Once, before anyone moves in | How much material you use and *which* materials |

A real client increasingly cares about both — because of regulations (UK Part L,
EU EPBD, NZ Building Code), because of funders (the World Bank, IFC and green-bond
investors *require* it), and because of cost (energy is expensive and getting more so).

### 1.3 What is "carbon costing"?

"Carbon costing" means **putting a number on the carbon a design will emit**, the
same way "cost estimating" puts a number on the money a design will cost. The unit
is **kgCO₂e** — kilograms of carbon-dioxide *equivalent*. The little "e" matters:
buildings emit several greenhouse gases (CO₂, methane, refrigerants), and we convert
them all into "the equivalent amount of CO₂" so we can add them up into one number.

There are two things people mean by "carbon cost":

1. **The carbon itself**, measured in kgCO₂e (or tonnes). This is what STINGTOOLS
   calculates — embodied + operational.
2. **The money**, measured in your currency. This is the *life-cycle cost* (LCC):
   what a sustainability feature costs to install versus how much money it saves
   over the building's life. STINGTOOLS calculates this too, and feeds it into
   the BOQ / Cost Manager.

This guide covers both, because in practice the question a client asks is always
*"What does it cost me — in carbon AND in money — to make this building greener?"*

### 1.4 The one mental model to remember

```
            ┌─────────────────────────────────────────────┐
            │              A BUILDING'S CARBON              │
            ├───────────────────────┬─────────────────────-┤
            │   EMBODIED (one-off)   │  OPERATIONAL (yearly) │
            │                        │                       │
            │  concrete, steel,      │  electricity, gas,    │
            │  glass, brick,         │  cooling, heating,    │
            │  insulation, finishes  │  fans, lighting, DHW  │
            │                        │                       │
            │  measured in kgCO₂e    │  measured in kgCO₂e/yr │
            │  (A1–A3 product stage) │  (grid factor × kWh)   │
            └───────────────────────┴─────────────────────-─┘
                        ▲                        ▲
                you reduce it by             you reduce it by
              using less / smarter         better fabric, plant,
                  material                   PV, efficient systems
```

Hold that picture. Everything below hangs off it.

---

<a name="part-2"></a>
## Part 2 — Green-building certifications: LEED and EDGE

A **certification** is a recognised, third-party scorecard that says "this building
meets a defined standard of greenness." Two matter most for the projects
STINGTOOLS serves: **LEED** (global, premium, points-based) and **EDGE**
(emerging-market focused, simple, threshold-based). STINGTOOLS supports both, and —
importantly — treats them as **data, not code**: the rules live in JSON files, so a
third scheme (BREEAM, Green Star) could be added without changing the engine.

### 2.1 LEED — the global gold standard

**LEED** = **L**eadership in **E**nergy and **E**nvironmental **D**esign. It is run
by the **U.S. Green Building Council (USGBC)** and is the most widely recognised
green-building label in the world (you'll see "LEED Platinum" on trophy office
towers).

**How LEED works — a points game.** You *earn points* across several categories.
The more points, the higher the award level:

| LEED level | Points (out of 110) |
|---|---|
| Certified | 40–49 |
| Silver | 50–59 |
| Gold | 60–79 |
| Platinum | 80+ |

The point categories (LEED v4 / v5 BD+C) are:

- **Location & Transportation** — is the site walkable, near transit?
- **Sustainable Sites** — rainwater, heat-island, light pollution.
- **Water Efficiency** — low-flow fixtures, no potable water for irrigation.
- **Energy & Atmosphere** — *the big one* — energy efficiency, renewables, refrigerants.
- **Materials & Resources** — **embodied carbon (WBLCA)**, recycled content, EPDs.
- **Indoor Environmental Quality** — daylight, air quality, acoustics.
- **Innovation** and **Regional Priority** — bonus points.

**The key idea:** LEED is *flexible*. You don't have to be good at everything — you
trade off. Weak on water? Make it up on energy and materials. There is a
**prerequisite** in Materials & Resources to complete a **Whole-Building Life-Cycle
Assessment (WBLCA)** — i.e. you *must* calculate embodied carbon to play. That's
exactly the number STINGTOOLS produces.

**Where LEED sits in design:** LEED is registered early, but points are *documented*
across all RIBA stages, with the big modelling effort (energy model, WBLCA) around
**RIBA Stage 3–4 (Developed/Technical Design)**.

### 2.2 EDGE — deep dive (the one to really understand)

**EDGE** = **E**xcellence in **D**esign for **G**reater **E**fficiencies. It was
created by **IFC**, the private-sector arm of the **World Bank Group**, specifically
for **emerging markets** (Africa, South Asia, Latin America) where LEED's cost and
complexity are barriers. If you work on a World Bank / IFC / green-bond financed
project, EDGE is very often **mandatory**.

EDGE's genius is **simplicity**. Forget 110 points across nine categories. EDGE has
**three gates**, and you must clear a single percentage on each:

```
                 ┌──────────────────────────────────────────┐
                 │                THE EDGE 3 GATES            │
                 ├──────────┬──────────────┬─────────────────┤
                 │  ENERGY  │     WATER     │    MATERIALS    │
                 │  ≥ 20%   │    ≥ 20%      │     ≥ 20%       │
                 │ savings  │   savings     │  savings (in    │
                 │  vs base │   vs base     │  embodied       │
                 │          │               │  ENERGY, MJ)    │
                 └──────────┴──────────────┴─────────────────┘
                  ALL THREE must pass → EDGE Certified
```

**What "savings vs base" means.** EDGE imagines a **base case**: a "typical"
building of the same type, in the same city, built to standard local practice. Your
design is compared against that base case. If your design uses **20% less energy,
20% less water, and 20% less embodied energy in its materials** than the base case,
you earn **EDGE Certified**.

**The three EDGE levels:**

| EDGE level | Requirement |
|---|---|
| **EDGE Certified** | ≥20% energy **and** ≥20% water **and** ≥20% materials savings |
| **EDGE Advanced** | ≥**40%** energy savings (water & materials still ≥20%) |
| **EDGE Zero Carbon** | EDGE Advanced **plus** the remaining operational carbon is covered by on-site or off-site renewables / offsets |

**Why 20%?** It's a deliberately *achievable* threshold — enough to make a real
difference, low enough that good design (not exotic technology) gets you there. This
is why EDGE works in markets where Platinum-level spending isn't realistic.

**EDGE's three big differences from LEED:**

1. **Threshold, not points.** You either clear 20% on all three gates or you don't.
   No trading off. This makes it brutally clear what to fix.
2. **Whole-building, not credits.** EDGE cares about the building's *total* energy,
   water and material-energy performance, not a checklist of features.
3. **One free app owns the certified number.** EDGE provides a **free web app**
   (app.edgebuildings.com) with the official base-case data for each country. You
   enter your building's parameters, and the app computes the *certified* %.

> **This last point is crucial and it shapes how STINGTOOLS works.** STINGTOOLS does
> **not** pretend to issue the certified EDGE number — only the EDGE app can do that.
> What STINGTOOLS gives you is an **indicative** figure during design ("you're
> around 32% on energy") so you can steer the design *before* you ever open the
> EDGE app, plus an **export** that hands the EDGE app the model quantities it needs.
> When you later get the official EDGE app number, you can type it back into
> STINGTOOLS and it will show you the *real* EDGE level. (More on this in Part 4.)

**The EDGE base case (baseline) — why it's climate-driven.** A "20% saving" only
means something relative to a baseline. EDGE's baseline depends on **building type +
climate** — a hospital in hot-humid Bangui has a very different base energy use than
an office in temperate London. STINGTOOLS mirrors this: it resolves a baseline by
**climate zone** (not country), using a four-hop fallback
(`country+zone+use → zone+use → use → global`) and always labels the result
"indicative" with a provenance log, so you can see *where the baseline came from*.

### 2.3 LEED vs EDGE at a glance

| | **EDGE** | **LEED** |
|---|---|---|
| Run by | IFC / World Bank Group | USGBC |
| Best for | Emerging markets, fast/affordable | Premium / trophy projects, global recognition |
| Scoring | 3 thresholds (≥20% each) | Points (40–110) → Certified/Silver/Gold/Platinum |
| Embodied carbon | "Materials" gate (embodied **energy**, MJ) | WBLCA prerequisite + Reduce-EC credits (embodied **carbon**, kgCO₂e) |
| Certified number from | The free EDGE web app | Documentation reviewed by GBCI |
| Cost & effort | Low | High |

> STINGTOOLS reports **both metrics** for materials and never conflates them:
> embodied **carbon (kgCO₂e/m²)** for LEED and dashboards, and embodied **energy
> (MJ/m²)** for the EDGE materials gate. They are different numbers and live in
> different columns.

### 2.4 Why certify *in design*, not after?

Because **the cheapest time to change a building is when it only exists on a
screen**. This is the famous "cost of change" curve:

```
  cost / effort to change the design
        │                                              ● Construction
        │                                         ●
        │                                  ●  Technical design
        │                        ●  Developed design
        │             ●  Concept
        │   ●  Brief
        └──────────────────────────────────────────────────► project time
```

A decision made at concept (orientation, form, glazing ratio, structural material)
locks in the *majority* of a building's lifetime carbon — and costs almost nothing
to change at that point. By the time you're on site, changing the structure to
reduce embodied carbon is ruinously expensive. So sustainability is a **design-stage
discipline**, and a design-stage tool (like the STING Sustainability Center inside
Revit) is exactly where it belongs.

---

<a name="part-3"></a>
## Part 3 — The engineering ideas behind the numbers

You don't need to be an engineer to use the panel, but understanding these five
ideas turns the panel's output from "magic numbers" into "numbers I trust."

### 3.1 EUI — Energy Use Intensity

**EUI** = the energy a building uses **per square metre per year**, written
**kWh/m²·yr**. It is the single most useful efficiency number, because it lets you
compare a small house and a huge tower fairly (you've divided out the size).

- A leaky old office might be **250 kWh/m²·yr**.
- A good new office might be **90 kWh/m²·yr**.
- A Passivhaus might be **40 kWh/m²·yr**.

EDGE's energy gate is really *"is your design's EUI at least 20% below the base
case's EUI?"* STINGTOOLS computes a **design EUI** from your model and compares it
to a **baseline EUI**.

### 3.2 Baselines and "% savings"

A **saving %** is always `(baseline − design) ÷ baseline × 100`.

- Baseline EUI 200, design EUI 150 → `(200−150)/200 = 25%` saving. 
- Design EUI *higher* than baseline → a **negative** saving ("over baseline").

STINGTOOLS guards this calculation against the classic traps: if the baseline is
zero or unknown it returns "not meaningful" rather than a nonsense number, and an
over-baseline design is phrased plainly ("design 240 vs baseline 200") instead of a
confusing "−20%".

### 3.3 Operational carbon — turning kWh into kgCO₂e

Energy isn't carbon until you multiply it by a **grid carbon factor** — how much CO₂
your local electricity grid emits per kWh:

```
operational carbon (kgCO₂e/yr) = energy used (kWh/yr) × grid factor (kgCO₂e/kWh)
```

- A coal-heavy grid might be **0.8 kgCO₂e/kWh**.
- A clean/hydro grid might be **0.05 kgCO₂e/kWh**.
- The UK grid is around **0.2** and falling.

This is why **the same building emits different carbon in different countries** — and
why **on-site solar (PV)** and **fuel choice** (electric vs gas heating) matter so
much. STINGTOOLS' supply layer takes your design's energy demand, subtracts on-site
PV generation, then applies the grid (or diesel, or a hybrid blend) factor to the
**net imported** energy.

### 3.4 Embodied carbon — A1–A3, fossil vs biogenic, EPDs

When you make a tonne of concrete or steel, you emit carbon **before it ever reaches
site**. Engineers split a material's whole life into stages (A, B, C, D). The part
STINGTOOLS focuses on is **A1–A3 — the "product stage":**

- **A1** raw material supply (quarrying, mining)
- **A2** transport to the factory
- **A3** manufacturing (the kiln, the furnace)

This "**upfront carbon**" is what you can actually influence at design time, and it's
the headline for benchmarks like **RIBA 2030**, **LETI** and **RICS WLCA**.

Two refinements that STINGTOOLS handles correctly (and many tools get wrong):

- **Fossil vs biogenic carbon.** Timber is special: growing the tree *absorbs* CO₂
  (a **biogenic** credit, a negative number), while processing it still emits
  **fossil** carbon. The professional convention (RICS, RIBA, LETI) is to report the
  **fossil/upfront** figure as the headline and show the **biogenic** credit
  *separately*. STINGTOOLS tracks both: it credits the timber sequestration into the
  *net* figure but keeps a separate fossil column so a timber scheme isn't
  flattered.
- **Carbon factors and EPDs.** A **carbon factor** says "this material emits X
  kgCO₂e per m³ (or per kg)." Generic factors come from databases like the **ICE
  database**. A product-specific **EPD** (Environmental Product Declaration) is a
  manufacturer's published, verified factor for *their* product — more accurate, and
  preferred when available. STINGTOOLS resolves factors through a chain: a
  material-stamped EPD value first, then a corporate library, then generic
  per-kg data (applied via material density).

### 3.5 Water — per-person-day, low-flow, rainwater

Water is the simplest gate. The base case assumes "standard" fixtures (a 6 L/flush
WC, a 10 L/min tap). Your design uses **low-flow fixtures** (a 4.5 L dual-flush WC, a
aerated tap), so it consumes fewer **litres per person per day**. EDGE also credits
**alternative water**: rainwater harvesting (catching roof runoff) and greywater
reuse (recycling basin/shower water for flushing). STINGTOOLS reads real low-flow
fixtures from the model when present, computes a rainwater-harvesting yield from roof
area + local rainfall (BS 8515), and folds that alternative water into the EDGE water
% — not just fixture efficiency.

### 3.6 The two material metrics, side by side

This trips everyone up, so it gets its own box:

| Metric | Unit | Standard | Used for |
|---|---|---|---|
| Embodied **carbon** | kgCO₂e/m² (GWP) | EN 15978 | LEED Reduce-EC, dashboards, "carbon cost" |
| Embodied **energy** | MJ/m² (CED) | — | The **EDGE materials gate** (indicative) |

They are correlated but **not the same number**, and STINGTOOLS keeps them in
separate tracks on purpose.

---

<a name="part-4"></a>
## Part 4 — Carbon costing with STINGTOOLS (the hands-on walkthrough)

Now the practical bit. This section assumes the STING plugin is installed and you
have a Revit model open. We'll go control-by-control through the **STING
Sustainability Center**.

### 4.0 Opening the panel

On the Revit ribbon: **STING Tools** tab → **STING Panels** group → **STING
Sustain**. A dockable panel appears on the right (look for the **♻ STING
Sustainability** title). It has **four tabs**:

```
  ┌────────────────────────────────────────────────────────┐
  │  ♻ STING Sustainability                                │
  │  [ SETUP ] [ DASHBOARD ] [ MATERIALS ] [ COST ]        │
  └────────────────────────────────────────────────────────┘
```

The flow is left to right: **SETUP** (tell it about your project) →
**DASHBOARD** (run it, read the EDGE result) → **MATERIALS** (dig into carbon) →
**COST** (turn savings into money).

> **The golden rule of green tools:** *garbage in, garbage out.* The five minutes
> you spend on the SETUP tab decide whether every number after it is meaningful.

### 4.1 The SETUP tab — tell STING about your project

This tab is the "zero-hardcoding options surface." Nothing about your project is
assumed in code; you declare it here. Field by field:

**Schemes & target**
- **EDGE / LEED checkboxes** — tick the scheme(s) you're chasing. You can do both.
- **EDGE level** — your target: *Certified*, *Advanced* or *ZeroCarbon*.
- **Units** — *SI* (metric: kWh/m²·yr, litres, m²) or *IP* (imperial: kBtu/ft²·yr,
  gallons, ft²). LEED reports are often IP; EDGE is SI. The whole panel + exports
  convert live when you switch.

**Location (drives the baseline & climate)**
- **Country** — ISO code (or `*` for "any"). Editable; type a code your baseline
  catalogue knows (e.g. `UGA`, `CAF`, `GBR`).
- **Climate site id** — the city whose monthly weather drives PV yield and the
  energy estimate. Leave blank to use the project's stamped site.
- **Climate zone (ASHRAE 169)** — `0A`/`1A` = very hot humid (e.g. Bangui), `4A`/`5A`
  = temperate. This drives the **baseline**. *Leave it blank and STING auto-derives
  it* from the site's heating/cooling degree-days, telling you what it assumed.

**Building**
- **Building use** — office / residential / healthcare / retail / hotel / education
  / warehouse / lab / restaurant / industrial. **This list is data-driven** — it's
  the union of what the baseline + water-profile data files actually support, so it
  grows automatically as data is added.
- **Floor area (GFA)** and **Occupancy** — needed for the per-m² and per-person
  numbers. Don't have them to hand? Click **⤓ From model (area + occupancy)** and
  STING reads them from the model's Spaces/Rooms.

**Mixed-use zones (optional)**
- A real building is often mixed-use (ground-floor retail + offices above). The
  **ZONES** grid lets you add a row per use (Use / Area / Occupancy / COP).
  When you add rows, they **override** the single building-use above; the result
  rolls up area-weighted (energy/materials) and occupancy-weighted (water). For a
  simple single-use building, ignore the grid entirely.

**Plant & supply**
- **Cooling COP/SEER** — how efficient your cooling plant is (higher = better; blank
  = use the baseline). A heat-pump might be 3–4; old kit ~2.
- **Supply mode** — `grid_tied`, `off_grid` (diesel) or `hybrid`.
- **PV (kWp)** and **PV performance ratio** — on-site solar size and a derating
  factor (~0.75 typical). This offsets imported energy.
- **Grid / Diesel carbon (kgCO₂e/kWh)** and **Diesel fraction** — the carbon factors
  for the energy you import. This is what turns kWh into kgCO₂e (§3.3).

Click **Save setup** (and **Save supply**) to persist everything to the project. The
settings live in `<project>/_BIM_COORD/sustainability/project_setup.json`.

### 4.2 The DASHBOARD tab — run it and read the EDGE result

Click **Run dashboard**. STINGTOOLS now:

1. Resolves the **climate** (monthly weather for your site).
2. Resolves the **baseline** (by climate zone + building use, with provenance).
3. Runs the **energy** estimator (annual kWh → design EUI → energy saving %).
4. Runs the **water** estimator (litres/person·day → water saving %).
5. Runs the **materials** rollup (model material volumes → embodied carbon kgCO₂e/m²
   + embodied energy MJ/m²).
6. Evaluates **each scheme** (EDGE's three gates, LEED's points).

The **EDGE GATES** section shows, for each gate, the **STING-indicative %** beside an
**EDGE-official %** box (which you fill in later — see §4.5). A headline line tells
you the **STING-determinable EDGE level** and whether you're on target.

**Reading the result — the "Computed" idea.** A gate only shows a real **pass** if
its number was genuinely computed from model data. If your model has no Spaces and no
floor area, the energy estimate can't be trusted, so STING marks the gate
**not computed** and refuses to flash a green "pass" off a meaningless 100%. The
**warnings** box at the bottom explains every not-computed gate ("no Spaces / floor
area — add Spaces or enter GFA in Setup"). *Trust the warnings; they're telling you
what data the model is missing.*

**Set baseline** — if you have a project-specific baseline (e.g. a client's reference
building), set it here; otherwise STING uses the climate-zone baseline.

### 4.3 The MATERIALS tab — where the carbon cost lives

This is the heart of "carbon costing." STINGTOOLS takes the **same takeoff the BOQ
uses** (so the carbon matches the bill of quantities — one source of truth), walks
the model's structural + enclosure elements (walls, floors, roofs, columns, framing,
foundations, rebar, glazing), and for each material:

1. Measures the **volume** (m³), grossed up by the **same waste allowance** the BOQ
   applies.
2. Resolves a **carbon factor** (EPD → corporate library → generic per-kg via
   density).
3. Computes **net carbon** = fossil + biogenic (so timber gets its sequestration
   credit).
4. Computes **embodied energy** (MJ) for the EDGE gate.

The tab shows the **three biggest carbon "hotspots"** — the materials responsible for
most of your embodied carbon. **This is the most useful output in the whole panel**,
because it tells you *where to act*: concrete and steel are almost always #1 and #2,
so a small % cut in your concrete mix (e.g. cement replacement) beats a big cut in
something trivial.

Buttons here:
- **EDGE export** — produces a ClosedXML workbook of model quantities + selections,
  formatted for upload to the **official EDGE app**. This is your bridge to the
  certified number.
- **EPD register** — manage product-specific EPD references on materials (better
  accuracy than generic factors).
- **LEED scorecard** — a LEED-oriented view when LEED is selected.

### 4.4 The COST tab — carbon savings in money (LCC)

Carbon is one cost; **money** is the other. The COST tab runs **Life-Cycle Costing
(LCC)** on the green **measures** (PV, low-flow fixtures, better glazing, LED, etc.):

- For each measure it estimates a **capex** (install cost) sized from **real model
  quantities** where possible (PV kWp, glazing m², fixture count) — not crude
  guesses.
- It estimates an **annual operational saving** (energy/water cost avoided).
- Over a 25-year analysis it computes the **lifetime saving** and **net benefit**.

Crucially, these rows are **fed into the BOQ Cost Manager** as real cost lines (not
just a side spreadsheet), so the "sustainability targets in the Cost/Budget Estimate"
become contractual numbers. Click **LCC benefit** to run it; the grid fills with
per-measure capex / annual saving / lifetime saving / net benefit, and the rows land
in the cost model.

### 4.5 The EDGE round-trip (indicative → official)

This is the workflow that makes STINGTOOLS honest about EDGE:

```
  1. Design in Revit ──► 2. Run dashboard (STING indicative %)
            │                          │
            │                          ▼
            │                3. Steer the design until the
            │                   indicative % clears the gates
            │                          │
            ▼                          ▼
  4. EDGE export ──► upload to the official EDGE app ──► 5. EDGE app
                                                            gives the
                                                            CERTIFIED %
                                                              │
                                                              ▼
            6. Type the certified % into the "EDGE official"
               boxes on the DASHBOARD → STING shows the REAL
               EDGE level (the official number now drives the
               level determination, not the indicative one)
```

Step 6 is **EDGE-official feedback**: once you record the certified figure, STING
stops treating that gate as "the EDGE app's business" and uses your official number
to determine the real, defensible EDGE level.

### 4.6 (Optional) Publish to your team — server sync

If your firm runs the Planscape Server, **Publish to server** (DASHBOARD tab) pushes
the latest dashboard snapshot to the cloud so the whole team / the mobile app can see
the project's live sustainability state and trend it over time. It's gated behind the
project's offline-mode flag — a single-machine project is unaffected and keeps a
local log.

### 4.7 A complete first-run, step by step

1. Open the model. Make sure it has **Rooms or MEP Spaces** and real **materials**
   on walls/floors/roofs (or at least enter GFA + occupancy in SETUP).
2. **SETUP:** tick **EDGE**, level **Advanced**; set country, climate zone (or leave
   blank to auto-derive), building use; click **From model** for area + occupancy;
   set cooling COP and PV if known. **Save setup**.
3. **DASHBOARD:** click **Run dashboard**. Read the three EDGE gate %s and the
   headline level. Read the **warnings** — fix any "not computed" causes.
4. **MATERIALS:** look at the three carbon **hotspots**. Note your embodied carbon
   (kgCO₂e/m²) and embodied energy (MJ/m²).
5. **COST:** click **LCC benefit** to see what the green measures cost vs save.
6. Iterate the design (more insulation, better glazing, PV, low-carbon concrete) and
   **Run dashboard** again — watch the %s move.
7. When close, **EDGE export** → upload to the EDGE app → get the certified % → type
   it back into the **EDGE official** boxes.

---

<a name="part-5"></a>
## Part 5 — Mastering the topic (advanced lessons)

This section is for when you want to *judge* the numbers, not just read them.

### 5.1 How the energy number is actually computed

STINGTOOLS' `AnnualEnergyEstimator` is a **monthly quasi-steady-state energy
balance** following **EN ISO 13790** (the same family of method the EDGE app uses
internally). For each zone, for each of 12 months, it balances:

- **Gains** — internal (people, lighting, equipment, from the ASHRAE/CIBSE load
  profile for that space type) + **solar** (monthly irradiance projected onto each
  glazing façade *by its orientation* — south glass gains more than north).
- **Losses/transfer** — conduction through the envelope (U-value × area) + ventilation
  + infiltration, driven by the indoor-vs-outdoor temperature difference.
- A **gain/loss utilisation factor** (the real EN ISO 13790 factor, dependent on the
  gain/loss ratio and the building's thermal mass / time constant) so that gains and
  losses aren't naively double-counted.

Thermal demand is then converted to **delivered energy** via the cooling **COP** and
the **heating efficiency** (electric resistance = 1.0, gas boiler ~0.9, heat-pump =
its COP). Non-electric heating fuel is tracked separately so PV/grid carbon isn't
wrongly applied to a gas boiler. The supply layer subtracts PV and applies the right
fuel's carbon factor.

**Why this matters to you:** a *hot* climate and a *cold* climate produce
sensibly different EUIs from the *same* model — because the climate, orientation and
plant efficiency genuinely drive the result. If two very different projects give
suspiciously identical EUIs, your inputs (climate site, building use, COP) are
probably wrong.

### 5.2 The "indicative vs certified" discipline

Internalise this: **STINGTOOLS never claims to issue a certified number.** Three
mechanisms enforce it:

- **The `Computed` flag** — a gate can only show a pass if it was computed from real
  model data. Zero-design artefacts (100% saving from an empty model) and hardcoded
  defaults can never read as a pass.
- **"Delegated" gates** — the EDGE *materials* gate is the EDGE app's to certify, so
  by default it's caveated, not blocking — *unless* you supply the official figure.
- **Provenance everywhere** — the baseline always logs where it came from
  ("fell back to climate-zone 0A office proxy"), and material lines carry their
  factor source (EPD / library / generic).

When you present numbers to a client, say "indicative" until you have the EDGE app's
certified figure. The tool is built to keep you honest here; don't fight it.

### 5.3 FactorSources — controlling which carbon dataset wins

Different projects/regions prefer different carbon databases (EPD-specific, EC3,
ICE, Ecoinvent). The **FactorSources** order (a project setting) drives which source
the materials resolver prefers, and whether a **mass-based (per-kg) database** is
even permitted. Remove the mass DBs from the order and a material that *only* has a
per-kg factor resolves to zero carbon (with a clear note) instead of silently using
a dataset your project disallows. This is how a QS / sustainability lead enforces a
data policy across a job.

### 5.4 Single source of truth — BOQ ↔ carbon

The materials carbon uses the **same takeoff, waste factor, density and
fossil/biogenic split** as the BOQ Cost Manager. Practical consequence: **the carbon
figure and the bill of quantities agree** for the same model. If they ever diverge,
something is wrong (different category scope, a stale BOQ). This is deliberate —
"carbon costing" and "cost estimating" should measure the *same building*.

### 5.5 Mixed-use & per-room realism

For a mixed-use tower, the **zones grid** (SETUP) lets each use carry its own area,
occupancy and COP; energy/materials roll up **area-weighted**, water **occupancy-
weighted**. For a Room-based architectural model with no MEP Spaces, the engine falls
back to using **Rooms as zones** (real per-room areas) before the single synthetic
zone — so an architect's model still gets a per-room load picture.

### 5.6 Reading hotspots like a pro

The three carbon hotspots almost always rank: **concrete → steel → glass/finishes**.
The professional moves, in order of impact:

1. **Concrete mix** — replace cement with GGBS/fly-ash (can cut concrete carbon
   30–50%). Biggest single lever on most buildings.
2. **Structural efficiency** — less material for the same performance (post-tensioning,
   voided slabs, right-sizing).
3. **Steel** — high recycled content (EAF), efficient sections.
4. **Material substitution** — timber (CLT/glulam) where appropriate, for its
   biogenic credit.

Then re-run and watch the hotspots and the kgCO₂e/m² fall.

### 5.7 Benchmarks to aim at (embodied / upfront carbon)

| Benchmark | Office upfront carbon (A1–A5) |
|---|---|
| Typical (business as usual) | ~1000+ kgCO₂e/m² |
| RIBA 2030 target | < 750 kgCO₂e/m² |
| LETI "good" | < 600 kgCO₂e/m² |
| Best practice / 2030-aligned | < 500 kgCO₂e/m² |

(STINGTOOLS reports A1–A3 product-stage upfront carbon; A4 transport-to-site and A5
construction are smaller and project-specific.)

### 5.8 Common pitfalls (and what the warnings are telling you)

| Symptom | Cause | Fix |
|---|---|---|
| Energy gate "not computed" | No Spaces/Rooms, no GFA | Place Spaces/Rooms or enter GFA in SETUP |
| Materials 0 carbon-stamped | Materials have no carbon factor | Run a carbon pass / add EPDs / use the corporate library |
| Water always shows ~25% indicative | No low-flow fixture data in the model | Stamp `PLM_*` flows or accept the indicative default |
| Suspiciously identical EUIs | Wrong climate site / building use / COP | Fix the SETUP inputs |
| Savings reads "over baseline" | Design worse than the base case | That's real — improve the design, don't blame the tool |

---

<a name="part-6"></a>
## Part 6 — Glossary, checklists & references

### 6.1 Glossary

- **A1–A3 / upfront / embodied carbon** — carbon emitted making + transporting +
  manufacturing materials, before site. Measured in kgCO₂e.
- **Baseline / base case** — the "typical" reference building you're compared against.
- **Biogenic carbon** — CO₂ absorbed by growing bio-based materials (timber); a
  credit (negative), reported separately from fossil carbon.
- **COP / SEER** — cooling/heat-pump efficiency (output ÷ input; higher = better).
- **EDGE** — IFC/World Bank green-building certification: 3 gates (energy/water/
  materials ≥20%), levels Certified/Advanced/Zero Carbon.
- **EPD** — Environmental Product Declaration; a manufacturer's verified carbon
  factor for a specific product.
- **EUI** — Energy Use Intensity, kWh/m²·yr.
- **GFA** — Gross Floor Area.
- **Grid factor** — kgCO₂e emitted per kWh of grid electricity.
- **kgCO₂e** — kilograms of CO₂ *equivalent* (all greenhouse gases converted to a
  CO₂ basis).
- **LCC** — Life-Cycle Cost; capex vs lifetime operational savings, in money.
- **LEED** — USGBC points-based certification (Certified/Silver/Gold/Platinum).
- **Operational carbon** — carbon from running the building each year (energy × grid
  factor).
- **WBLCA** — Whole-Building Life-Cycle Assessment; the embodied-carbon calculation
  LEED requires.

### 6.2 First-project checklist

- [ ] Model has Rooms/Spaces **or** GFA + occupancy entered in SETUP.
- [ ] Walls/floors/roofs carry real **materials**.
- [ ] SETUP: scheme, level, country, climate zone (or auto-derive), building use set.
- [ ] Plant & supply: cooling COP, supply mode, PV entered.
- [ ] **Save setup** clicked.
- [ ] **Run dashboard** — all gates **computed** (no blocking warnings).
- [ ] **MATERIALS:** carbon factors present (no "0 carbon-stamped").
- [ ] **COST:** LCC run, measures sized from model quantities.
- [ ] **EDGE export** generated; certified % from the EDGE app typed back into the
      **EDGE official** boxes before quoting an EDGE level to the client.

### 6.3 Where things live (for the curious)

- Panel: ribbon **STING Tools → STING Panels → STING Sustain** (♻ pane).
- Project settings: `<project>/_BIM_COORD/sustainability/project_setup.json`.
- KPI history (for trends): `<project>/_BIM_COORD/sustainability/edge_kpi_log.jsonl`.
- Corporate data (schemes, baselines, water profiles, measures, monthly climate):
  the `STING_GREEN_*.json` / `STING_WATER_USAGE_PROFILES.json` /
  `STING_CLIMATE_MONTHLY.json` data files (project overrides in `_BIM_COORD/`).

### 6.4 External references

- **EDGE** — edgebuildings.com (free app + country base-case data; IFC/World Bank).
- **LEED** — usgbc.org (USGBC) / gbci.org (review body).
- **RICS** — "Whole Life Carbon Assessment for the Built Environment", 2nd ed.
- **RIBA 2030 Climate Challenge** — upfront-carbon targets.
- **LETI** — Embodied Carbon Primer; Climate Emergency Design Guide.
- **ICE database** — Inventory of Carbon & Energy (generic material factors).
- **EN ISO 13790 / EN 15978** — the energy-balance and LCA standards behind the
  engines.

---

*This guide describes the STING Sustainability Center (Phase 195). The figures it
produces during design are **indicative** — the EDGE app and a qualified assessor
own the certified result. Use STINGTOOLS to steer the design early and cheaply, then
confirm with the official tools before you certify.*
