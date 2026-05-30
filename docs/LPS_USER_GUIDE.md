# STING — Lightning Protection (LPS) User Guide

A complete, plain-English guide to the STING Lightning Protection module —
from "what is lightning protection?" through to senior-level coordinated
design, documentation, and Revit family authoring.

This single document is three things at once:

1. **A beginner's introduction to lightning protection** — the physics, the
   five sub-systems, the standard, and the vocabulary (Part A).
2. **A start-to-finish tutorial** at three levels — beginner, intermediate
   and senior (Part C).
3. **An exhaustive reference** to every command, every panel control, the
   risk model internals, all the data files, the shared-parameter schema,
   and the full family-authoring workflow (Parts B, D, E, F).

If you have never designed an LPS, read Part A then do Tutorial 1. If you
already know the standard and just want the tool, skim Part B and jump to
the function reference (Part D) or the family authoring section (Part E).

> **Standard basis:** BS EN 62305 (parts 1–4) / IEC 62305 — the
> international lightning-protection standard. STING normalises everything
> to the BS EN 62305 **class** system (I / II / III / IV) and can display
> the equivalent IEC 62305 / NFPA 780 / NF C 17-102 class label.

> **Before you start — verification note.** STING's LPS code is developed in
> a Linux sandbox without the Revit API, so each release is committed
> "verify in Revit before merge." Treat first-run results on a new build as
> *indicative* until you've sanity-checked them in a live model. The
> calculators are strong design aids and first-pass sizing tools — a
> certified design still needs a full, signed BS EN 62305-2 study.

---

## Table of contents

**Part A — Learn lightning protection (beginner)**
- [1. Lightning protection in 60 seconds](#1-lightning-protection-in-60-seconds-the-layman-version)
- [2. A little more depth — how a strike is managed](#2-a-little-more-depth--how-a-strike-is-managed)
- [3. The standard, the classes, and the two questions](#3-the-standard-the-classes-and-the-two-questions)
- [4. The vocabulary you need (glossary)](#4-the-vocabulary-you-need-glossary)

**Part B — The STING LPS module tour**
- [5. Where the tools live](#5-where-the-tools-live)
- [6. The panel header context strip](#6-the-panel-header-context-strip-read-this-it-drives-everything)
- [7. The seven panel tabs](#7-the-seven-panel-tabs)

**Part C — Tutorials (beginner → senior)**
- [8. Tutorial 1 (Beginner): a small building, start to finish](#8-tutorial-1-beginner--a-small-building-start-to-finish)
- [9. Tutorial 2 (Intermediate): risk-driven class + SPD](#9-tutorial-2-intermediate--let-the-risk-assessment-drive-the-design)
- [10. Tutorial 3 (Senior): full coordinated design](#10-tutorial-3-senior--full-coordinated-design)

**Part D — Complete function reference**
- [11. Risk & class functions](#11-risk--class-functions)
- [12. Air-termination functions](#12-air-termination-functions)
- [13. Down-conductor & separation functions](#13-down-conductor--separation-functions)
- [14. Earth & bonding functions](#14-earth--bonding-functions)
- [15. SPD (surge protection) functions](#15-spd-surge-protection-functions)
- [16. Zone functions](#16-zone-functions)
- [17. Reporting, documentation & integration functions](#17-reporting-documentation--integration-functions)

**Part E — Building the LPS families (detailed)**
- [18. How family authoring works in STING (the mental model)](#18-how-family-authoring-works-in-sting-the-mental-model)
- [19. The 20 families at a glance](#19-the-20-families-at-a-glance)
- [20. The universal authoring recipe](#20-the-universal-authoring-recipe)
- [21. Tier-by-tier deep dive](#21-tier-by-tier-deep-dive-geometry-parameters-formulas)
- [22. Subcategories & colour conventions](#22-subcategories--colour-conventions)
- [23. SPD electrical connectors](#23-spd-electrical-connectors)
- [24. Injecting the shared parameters (what STING does for you)](#24-injecting-the-shared-parameters-what-sting-does-for-you)
- [25. Conformance check & testing](#25-conformance-check--testing)
- [26. Vendor families (the fast path)](#26-vendor-families-the-fast-path)
- [27. Authoring pitfalls](#27-authoring-pitfalls)

**Part F — Reference**
- [28. Data files & how to tune them](#28-data-files--how-to-tune-them)
- [29. Shared-parameter reference](#29-shared-parameter-reference)
- [30. Standards & class mapping](#30-standards--class-mapping)
- [31. Troubleshooting / FAQ](#31-troubleshooting--faq)
- [32. Quick command index](#32-quick-command-index)

---
---

# Part A — Learn lightning protection (beginner)

## 1. Lightning protection in 60 seconds (the layman version)

A lightning protection system (LPS) gives a lightning strike a safe,
deliberate path to ground so it doesn't blow a hole in the building, start a
fire, or kill the electronics inside. Think of it as a **planned shortcut**:
left to its own devices, the strike will tear through whatever is handiest —
a parapet, a steel beam, a cable. The LPS offers it something better.

It has **five parts**:

| Part | Plain English | Standard term |
|---|---|---|
| **Air termination** | The metal rods/wires on the roof that the lightning is *meant* to hit | BS EN 62305-3 §5.2 |
| **Down conductors** | The straps running down the walls that carry the current from roof to ground | §5.3 |
| **Earth termination** | The rods/plates/rings buried in the soil that dump the current into the earth | §5.4 |
| **Equipotential bonding** | Links every metal thing (pipes, steel, services) together so nothing is at a different voltage during a strike | §6.2 |
| **Surge protection (SPDs)** | Sacrificial devices in the electrical panels that clamp the voltage spike before it fries equipment | IEC 62305-4 / IEC 61643 |

The first three (air → down → earth) are the **external LPS**: they catch the
strike and route it to ground. The last two (bonding + SPDs) are the
**internal LPS / LEMP protection**: they stop the huge current and its
electromagnetic pulse from arcing across to people, services and electronics
on the way down.

**A useful analogy.** The external LPS is a *gutter and downpipe* for
lightning: the air terminals are the gutter (catching it at the top), the
down conductors are the downpipe (carrying it down the outside), and the
earth termination is the soakaway (getting rid of it into the ground). The
bonding and SPDs are the *flood defences inside* the building — they make
sure that even if some of the energy gets in, everything rises and falls
together so nothing sparks across or burns out.

---

## 2. A little more depth — how a strike is managed

A lightning strike to a structure can carry **tens of thousands of amps** in
a few microseconds. The LPS doesn't *stop* the strike — nothing can — it
*manages* it. Four jobs:

1. **Intercept** the strike at a known point (air termination) rather than
   letting it choose a random, vulnerable spot. Where the rods *can't* be
   relied on to catch it, the design proves coverage with one of three
   geometric methods (see §4).
2. **Conduct** the current down to earth on multiple, well-distributed paths
   (down conductors). More paths = less current per path = smaller magnetic
   field and smaller side-flash risk.
3. **Dissipate** the current into the soil (earth termination) with a low
   enough resistance that the structure doesn't rise to a dangerous voltage.
4. **Equalise** voltages inside (bonding) and **clamp** the residual surge on
   electrical/data lines (SPDs) so the energy that *does* couple in can't
   injure people or destroy equipment.

### The Lightning Protection Zone (LPZ) concept

Internal protection is organised by **zones** (IEC 62305-4). As you move from
outside to deep inside the building, each "shell" of shielding and surge
protection reduces the threat:

| Zone | Meaning |
|---|---|
| **LPZ 0A** | Outside, exposed to **direct strikes** and the full lightning electromagnetic field |
| **LPZ 0B** | Outside but **shielded from direct strikes** (inside the air-termination's protected volume), still full EM field |
| **LPZ 1** | Inside the structure — no direct strike, reduced surge current, partial EM shielding |
| **LPZ 2, 3 …** | Nested deeper inside — further reduced surge and field (e.g. a shielded equipment room, then a shielded cabinet) |

Every time a cable or pipe crosses a zone boundary it must be **bonded**
and/or fitted with an **SPD** so the threat drops to what the next zone can
tolerate. That is why SPDs come in a cascade — Type 1 at the LPZ 0/1 boundary
(the service entrance), Type 2 at LPZ 1/2 (sub-boards), Type 3 at LPZ 2/3
(final circuits, next to sensitive kit).

---

## 3. The standard, the classes, and the two questions

### The standard

**BS EN 62305 / IEC 62305** is in four parts:

- **Part 1 — General principles.** Lightning current parameters, damage
  types, protection levels.
- **Part 2 — Risk management.** The risk assessment that decides *whether*
  you need protection and *how good* it must be. This is what STING's **Class
  Setup / Run risk** implements.
- **Part 3 — Physical damage & life hazard.** The external LPS — air
  termination, down conductors, earth, bonding, separation distance. This is
  what STING's geometry/compliance checks implement.
- **Part 4 — Electrical & electronic systems (LEMP).** Zoning, shielding,
  SPD coordination. This is what STING's **SPD** and **Zones** tabs
  implement.

### The four classes (Lightning Protection Level, LPL)

The risk assessment outputs a **protection class** — I (most stringent) to IV
(basic). The class sets every physical parameter of the external LPS. These
come straight from `STING_LPS_CLASSES.json`:

| Class | Rolling-sphere radius | Mesh size | Down-conductor spacing | Min Cu cross-section | Inspection | Efficiency |
|---|---|---|---|---|---|---|
| **I** | 20 m | 5 × 5 m | 10 m | 50 mm² | 12 mo | 0.98 |
| **II** | 30 m | 10 × 10 m | 10 m | 50 mm² | 12 mo | 0.95 |
| **III** | 45 m | 15 × 15 m | 15 m | 50 mm² | 24 mo | 0.90 |
| **IV** | 60 m | 20 × 20 m | 20 m | 50 mm² | 24 mo | 0.80 |

Smaller class number ⇒ tighter geometry ⇒ catches more strikes ⇒ higher
"protection efficiency." Class I is for things like explosives stores and
hospitals; Class IV is a basic ordinary building.

### The two questions every LPS design answers

1. **Do I need protection, and how good must it be?**
   → a **risk assessment** (BS EN 62305-2) that produces a **protection
   class** (I…IV) and a recommended **SPD level**. STING: §11.
2. **Is my design actually compliant?**
   → geometry + parameter checks: rolling-sphere/mesh coverage, down-conductor
   spacing & size, earth resistance, separation distance, bonding, SPD
   coordination, inspection dates. STING: §§12–17.

STING does both, plus tagging, colour-coding, schedules, a single-line
diagram, embodied-carbon reporting, and a 3D coverage analyser.

---

## 4. The vocabulary you need (glossary)

Read this once; the rest of the guide assumes it.

| Term | What it means |
|---|---|
| **Air termination** | The strike-receiving metalwork on the roof: rods (Franklin), horizontal mesh tape, catenary wires, or masts. |
| **Rolling-sphere method** | Coverage test: imagine rolling a sphere of the class radius (20/30/45/60 m) over the structure. Anywhere the sphere touches *could* be struck; anywhere it can't reach is protected. The most general of the three methods. |
| **Mesh method** | Coverage by a grid of conductors on the roof at the class mesh size (5–20 m). Good for flat roofs. |
| **Protection angle method** | Coverage by a cone under each rod; the cone's half-angle depends on class **and** the rod's height above the surface (taller ⇒ narrower). Good for simple/tall structures. |
| **Down conductor** | The conductor carrying current from roof to earth. Multiple, evenly spaced, ideally one near each corner. |
| **Earth termination** | What dumps current into the soil. **Type A** = individual rods/plates at each down conductor. **Type B** = a ring (or foundation) electrode joining all down conductors — the preferred arrangement. |
| **Equipotential bonding** | Connecting all metalwork and services to the earthing system so that, during a strike, everything rises to the same voltage and nothing arcs across. |
| **Main Earth Bar (MEB)** | The central copper bar where the earthing and all bonding conductors meet. |
| **Separation distance (s)** | The minimum air/material gap an LPS conductor must keep from nearby metalwork so a dangerous side-flash can't jump across. `s = (ki / km) · kc · l`. |
| **ki** | Material/protection-level factor (0.04–0.08 by class) in the separation formula. |
| **km** | Insulating-medium factor: **air = 1.0**, solid (brick/concrete) = 0.5. Note: it's the *medium*, not the conductor metal. |
| **kc** | Current-sharing factor: how the lightning current splits between down conductors. More conductors + a ring + bonding ⇒ smaller kc ⇒ smaller separation distance. |
| **Ng** | **Ground flash density** — average lightning strikes per km² per year for the site. Drives how often the structure is hit. UK ≈ 0.5–1; tropics ≈ 6–20. |
| **N_D** | Expected number of dangerous events (strikes) to the structure per year, derived from Ng and the structure's *collection area* A_e. |
| **A_e (collection area)** | The effective "catchment" footprint of the structure for strikes — bigger/taller structures collect more. |
| **R1…R4 / Rt** | The four **risk** figures (R1 human life, R2 public service, R3 cultural heritage, R4 economic) and their **tolerable limits** Rt (e.g. R1 ≤ 1×10⁻⁵/yr). If a risk exceeds its Rt, you need protection. |
| **SPD** | **Surge Protective Device** — clamps transient overvoltages. **Type 1** (10/350 µs Iimp, service entry), **Type 2** (8/20 µs In, sub-boards), **Type 3** (final circuits). |
| **Up / Uc / Iimp / In** | SPD ratings: **Up** = let-through (protection) voltage, **Uc** = max continuous operating voltage, **Iimp** = Type-1 impulse current, **In** = Type-2 nominal discharge current. |
| **Uw** | **Equipment impulse withstand voltage** — how big a surge your electronics can survive (1.0–4.0 kV). The SPD's Up must be ≤ Uw. |
| **LPZ** | **Lightning Protection Zone** (0A/0B/1/2/3) — see §2. |

---
---

# Part B — The STING LPS module tour

## 5. Where the tools live

There are **two** surfaces for the LPS tools — they fire the *same* commands:

1. **The dedicated "⚡ STING LIGHTNING PROTECTION" dockable panel** (the
   interactive one, shown in the screenshots).
   - Open it from the Revit ribbon: **STING Tools tab → ⚡ LPS panel →
     "STING LPS"** button. It docks on the right, tabbed near Properties /
     the other STING panels (Electrical, HVAC, Plumbing).
   - It has a **header context strip** (§6) and **seven tabs** — RISK,
     AIR-TERM, CONDUCTORS, EARTH, SPD, ZONES, RPRT (§7) — each backed by a
     live grid that fills when you click **⟳ Load model** or run a check.
   - Workflow is identical to the other STING engineering panels: set the
     header context, pick a scope, click an action, read the grid.

2. **The "LIGHTNING PROTECTION" section in the main STING panel's BIM tab.**
   These are one-click shortcuts to the same commands; results pop up in a
   report window rather than a living grid.

> **Tip:** the dedicated ⚡ panel is the richer, interactive surface — start
> there. The first time you open it on a project it shows a yellow
> **"⚠ Set LPS class…"** banner: that's prompting you to run the risk
> assessment, which stamps the class the rest of the module reads.

The panel's top buttons are: **⚠ Set LPS class…** (`Lps_ClassSetup`),
**⟳ Load model** (`Lps_LoadModel`), and **⚙ Settings** (`Lps_OpenSettings`,
shows the data-file paths).

---

## 6. The panel header context strip (read this — it drives everything)

Above the tabs is a strip of project-wide settings. Every calculation reads
these, so set them once before you run anything. They are snapshotted into
the command handler before each action so a command running on the Revit API
thread sees a consistent context.

| Field | Options | What it controls |
|---|---|---|
| **Standard** | BS EN 62305 (UK) · IEC 62305 (Intl) · NFPA 780 (US) · NF C 17-102 (ESE) | Which standard's *class label* is displayed. STING computes in BS EN 62305 and translates the display (§30). |
| **LPS class** | Class I (R=20m · spacing 10m) · II · III · IV · **Auto-pick from RISK tab** | The active protection class. "Auto-pick" defers to whatever the risk assessment recommends. Drives rolling-sphere radius, mesh, spacing, min cross-section, earth target, inspection interval. |
| **Material** | Copper · Aluminium · Steel · Stainless | Default conductor metal — sets minimum cross-section (Cu/Steel 50 mm², Al 70 mm²), km in the separation formula, and carbon factors. |
| **Region (Ng)** | UK (≈0.5–1.0) · EU (≈1.0–4.0) · US (≈1.0–12) · Tropics (≈6–20) · Africa (≈8–25) | The default ground flash density band. Can be overridden per-project (Ng override on the RISK tab, or auto-derived from the project's climate-site latitude — §28). |
| **Uw (eq withstand)** | 1.0 kV (sensitive electronics) · 1.5 kV (typical 230/400 V) · 2.5 kV (motors) · 4.0 kV (heavy equipment) | Equipment impulse withstand. Drives the line-surge probabilities in the risk model and the **Up ≤ Uw** SPD coordination check. |
| **Soil ρ** | 50 Ωm (wet clay) · 100 Ωm (loam) · 500 Ωm (dry sand) · 1000 Ωm (gravel) · 5000 Ωm (rocky) | Soil resistivity — informs earth-electrode resistance expectations. |
| **Air-term method** | Rolling sphere · Mesh · Protection angle | Which coverage method the air-termination checks and visualisers use. Default rolling sphere. |
| **Scope** | All · Selection · Active view | What every action operates on. Default **Active view**. Each button respects this without re-prompting. |

---

## 7. The seven panel tabs

Each tab is a focused work surface. Buttons on a tab fire the commands
documented in Part D — the cross-reference is given per tab.

### RISK — Risk assessment (BS EN 62305-2)
The brain of the module. Expanders:
- **BUILDING DIMENSIONS + Ng** — L, W, H (m), Ng override, Rt, A_e override.
- **EARTHING + BONDING TOPOLOGY (kc Annex C.3)** — checkboxes: *Ring
  conductor at base (Type B earthing)*, *Equipotential bonding network
  present*, *Auto-apply recommended class to Project Information*.
- **C-FACTORS (catalogue-driven)** — dropdowns for Cb (building occupancy),
  Cc (internal contents), Cd (occupant hazard), Ce (consequence of failure),
  Cd_loc (location factor), and service-entry factor. Pick a description and
  the value column fills from `STING_LPS_RISK_FACTORS.json`.
- **Manual override (advanced)** — a grid to type a raw value for any
  C-factor, overriding the catalogue choice.
- **RISK BY LOSS TYPE** — results grid: Loss · Risk R · Rt · Status.
- **RESIDUAL RISK BY CLASS** — results grid: Class · Residual · Status · Pick.
- **RESULT** — read-outs: A_e, N_d, R1, Rt, LPS req?, Class.
- Buttons: **Run risk** (`Lps_RunRiskInline`), **Advanced wizard…**
  (`Lps_ClassSetup`), **Dashboard** (`Lps_Dashboard`). → §11

### AIR-TERM — Air-termination design (§5.2)
- Checkboxes: Franklin rods · Mesh tape · ESE terminals · Catenary.
- Grid: Tag · Height m · Family · R (m) · Status.
- Buttons: **Plan visualise** (`Lps_PlanVisualise`), **3D coverage**
  (`Lps_3DCoverage`), **Mark types** (`Lps_MarkTypes`). → §12

### CONDUCTORS — Down conductors + separation (§5.3 + §6.3)
- Grid: Tag · Length m · Material · Spacing m · s (mm) · CS mm² · Status.
- Buttons: **Check spacing** (`Lps_DownConductor`), **Sep distance**
  (`Lps_SepDistance`), **Recalc kc** (`Lps_RecalcKc`). → §13

### EARTH — Earth termination + bonding (§5.4 + §6.2)
- **EARTH ELECTRODES** grid: Tag · Type (A/B) · Length m · R (Ω) · Status.
- **EQUIPOTENTIAL BONDING** grid: Tag · From LPZ · To LPZ · Bond
  (DIRECT/SPD/ISOLATING_SPARK_GAP) · Status.
- Buttons: **Earth check** (`Lps_EarthCheck`), **Bonding inventory**
  (`Lps_Bonding`). → §14

### SPD — Surge protection coordination (IEC 62305-4 / 61643-11)
- Reminder note about the Type 1→2→3 cascade.
- Checkboxes: Type 1 · Type 2 · Type 3 · Combined · Data.
- Grid: Tag · Location · Type · Iimp kA · In kA · Up kV · Manu · Model ·
  Cable m · Status.
- Buttons: **Coordinate** (`Lps_SpdCoordinate`), **Recommend**
  (`Lps_SpdRecommend`), **Export BOM** (`Lps_SpdExportBom`), **Save to
  project** (`Lps_SpdSaveOverride`), **Spec sheets** (`Lps_SpdSpecSheet`). → §15

### ZONES — Lightning Protection Zones (§6.1)
- Note explaining LPZ 0A/0B/1/2/3.
- Grid: Room · Level · LPZ · Colour.
- Buttons: **Tag rooms** (`Lps_ZoneTagRooms`), **Colour zones**
  (`Lps_ColourZones`), **Clear colours** (`Lps_ClearColours`). → §16

### RPRT — Inspection + reporting (Annex E)
- **INSPECTION SCHEDULE** grid: Tag · Last test · Next due · Status.
- **RUN LOG** grid: # · Command · Status · Time.
- Buttons: **Compliance** (`Lps_Compliance`), **Full report**
  (`Lps_FullReport`), **Inspection schedule** (`Lps_InspectionSchedule`),
  **Create schedules** (`Lps_CreateSchedules`), **Sync to Planscape**
  (`Lps_SyncToServer`), **SLD generate** (`Lps_SldGenerate`), **SLD overlay**
  (`Lps_SldOverlay`), **TAG7** (`Lps_Tag7Paragraph`), **Stamp families**
  (`Lps_BatchFamilyStamper`), **Carbon** (`Lps_CarbonReport`), **Reload
  catalogue** (`Lps_ReloadCatalogue`). → §17

---
---

# Part C — Tutorials (beginner → senior)

These three tutorials build on each other. Do them in order the first time.

## 8. Tutorial 1 (Beginner) — a small building, start to finish

**Goal:** take a simple two-storey building, decide its class, model the
external LPS, and prove it's covered. ~30 minutes.

### Step 0 — Make sure parameters are bound
STING Tools → **Tags → Load Params**. This binds the `ELC_LPS_*` shared
parameters so the commands have somewhere to write. (Usually already done by
project setup — if Load model later shows blank grids, come back and do this.)

### Step 1 — Open the panel & set the header
Open the **⚡ STING LIGHTNING PROTECTION** panel. In the header strip set:
- **Standard:** BS EN 62305 (UK)
- **LPS class:** Auto-pick from RISK tab (we'll let the risk assessment decide)
- **Material:** Copper
- **Region (Ng):** your region
- **Uw:** 1.5 kV (typical)
- **Scope:** Active view

### Step 2 — Run the risk assessment
Go to the **RISK** tab.
1. Open **BUILDING DIMENSIONS + Ng** and type the plan **L**, **W** and the
   **H** (ridge height). Leave Ng override at 0 to use the region default.
2. Open **C-FACTORS** and pick the descriptions that match your building
   (e.g. Building = "Residential", Content = "Ordinary contents", Occupants =
   "Low — few occupants", Consequence = "Low", Location = "Isolated
   structure"). The value column fills automatically.
3. Tick **Auto-apply recommended class to Project Information**.
4. Click **Run risk**.

Read the **RESULT** panel: it tells you **LPS req? YES/NO**, the recommended
**Class**, A_e, N_d, R1 vs Rt. The **RESIDUAL RISK BY CLASS** grid shows each
class's residual risk and which one to pick. For a small isolated house you
will often see **Class IV** (or even "not required").

> If "LPS req?" is **NO**, you're done from a code standpoint — but many
> clients still want basic protection. Set the class manually if so.

### Step 3 — Model the external LPS
Place the STING LPS families (Part E) or vendor families:
- **Air termination:** Franklin rods near roof corners / high points, or mesh
  tape around the perimeter.
- **Down conductors:** at least two, ideally one per corner, spaced within the
  class limit (Class IV = 20 m).
- **Earth termination:** an earth rod (+ test clamp) at the base of each down
  conductor, or a ring earth around the building.
- **Test clamp** at ~1.5 m on each down conductor.

### Step 4 — Tell STING what each element is
**AIR-TERM tab → Mark types** (`Lps_MarkTypes`). This stamps
`ELC_LPS_ELEMENT_TYPE_TXT` on every element so all the other checks can
identify them. **Do this before any check.** Then click **⟳ Load model** — the
grids on every tab fill in.

### Step 5 — Prove the air-termination coverage
**AIR-TERM tab → 3D coverage** (`Lps_3DCoverage`). STING rolls the
class-radius sphere over the roof and drops **red marker cubes** on any roof
point a rod doesn't shield, in a new 3D view. Add or raise rods until the red
markers disappear. (Sanity check: the point directly under a single rod must
read *protected*.)

### Step 6 — Check the basics
**RPRT tab → Compliance** (`Lps_Compliance`). The all-in-one audit reports
PASS / WARN / FAIL: air terminals present? earth electrodes? ≥2 down
conductors within spacing? earth resistance under target? conductor sizes
adequate? It writes `ELC_LPS_COMPLIANCE_STATUS_TXT` on each element.

### Step 7 — Document
**RPRT tab → Create schedules** for native Revit schedules of every LPS
element, and **Full report** for the BS EN 62305 compliance write-up.

**You've done a complete basic LPS design.** Tutorials 2 and 3 add the
risk-driven SPD selection and the senior-level coordination.

---

## 9. Tutorial 2 (Intermediate) — let the risk assessment drive the design

**Goal:** a real building where surge protection matters. You'll see why the
risk model often gives "low class but real SPDs," and how to use the SPD tab.

### Step 1 — A richer risk run
On the **RISK** tab, in addition to dimensions and C-factors:
- Set **Uw** in the header to match your electronics (1.0 kV for a comms-heavy
  building; 1.5 kV typical office).
- In **EARTHING + BONDING TOPOLOGY**, tick **Ring conductor at base** if you'll
  use a Type B earth, and **Equipotential bonding network present**.
- Open the **Advanced wizard…** (`Lps_ClassSetup`) for the full five-section
  flow if you want to describe service lines (overhead/buried, length,
  HV/LV transformer, shielding), wiring routing/shielding, and fire/ground
  factors — these refine the result toward a stamped study.

Click **Run risk**. Now read **two** outputs:
- **Recommended class** — chosen by recomputing the risk with each class's
  physical-damage probability until R1–R4 all clear.
- **Recommended SPD level** — chosen *separately*, because surge protection,
  not the air-termination class, is what clears the *electronics-failure* and
  *line-surge* risks (R_C/R_M/R_W/R_Z).

> **This is the key intermediate insight.** You'll often see e.g.
> **"Class IV + SPD II"**: the building only needs basic air termination, but
> it needs real SPDs because the risk is dominated by surges arriving on long
> overhead service lines. The **dominant risk component** read-out tells you
> *which* component (and therefore which fix) is driving the requirement.

### Step 2 — Get an SPD recommendation
**SPD tab → Recommend** (`Lps_SpdRecommend`). STING reads the project class
and Uw, then proposes a coordinated set from the SPD catalogue: a Type 1 (or
Combined 1+2) at the service entry, Type 2 at sub-boards, Type 3 near
sensitive equipment — picking the lowest-Up product that meets the class's
minimum Iimp and Up ≤ Uw.

### Step 3 — Place the SPD families and coordinate
Place the SPD families (Type 1 at the main incomer, Type 2 at sub-DBs, Type 3
at final circuits). **Mark types**, **Load model**, then
**SPD tab → Coordinate** (`Lps_SpdCoordinate`). It validates:
- **SPD present** at each required location.
- **Up ≤ Uw** for every SPD (fails if any let-through exceeds equipment
  withstand).
- **Iimp ≥ class minimum** for every Type 1.
- **Cascade energy coordination** — each Type 1→Type 2 pair must be either
  ≥ 10 m of cable apart **or** the same manufacturer (manufacturer-paired).

### Step 4 — Procurement & specs
**SPD tab → Export BOM** for a purchase list, and **Spec sheets** for product
datasheets. **Save to project** writes your chosen SPD rows into a
project-scoped catalogue override (`_BIM_COORD/lps_spd_catalogue.json`) so
they persist and feed future recommendations.

---

## 10. Tutorial 3 (Senior) — full coordinated design

**Goal:** the complete BS EN 62305 + IEC 62305-4 workflow: separation
distance, kc optimisation, zoning, the single-line diagram, embodied carbon,
inspection regime, ISO 19650 tagging, and cloud sync.

### Step 1 — Optimise current sharing (kc) and separation distance
The **separation distance** `s = (ki/km)·kc·l` is the gap every LPS conductor
must keep from internal metalwork to avoid a side-flash. Two levers:
1. **CONDUCTORS tab → Recalc kc** (`Lps_RecalcKc`) — recomputes the
   current-sharing factor from the number of down conductors and the
   ring/bonding flags. More conductors + a ring + bonding ⇒ smaller kc.
   STING then re-stamps the separation distances. (kc: n=2 → 0.66, n=3 → 0.44,
   n=4 → 0.33, n≥5 → 1/n.)
2. **CONDUCTORS tab → Sep distance** (`Lps_SepDistance`) — computes and stamps
   `ELC_LPS_SEPARATION_DISTANCE_MM` per down conductor using ki (by class), km
   (by medium — air 1.0, solid 0.5) and the conductor length.

If a separation distance can't be achieved physically, the standard's remedy
is to **bond** the conductor to the nearby metalwork at that point — model a
bonding strap and re-run.

### Step 2 — Zone the building (LPZ)
**ZONES tab → Tag rooms** (`Lps_ZoneTagRooms`) assigns each room an LPZ
(`ELC_LPS_ZONE_TXT`). **Colour zones** colour-codes them on the active view;
**Clear colours** removes the override. Use this to verify every cable/pipe
crossing an LPZ boundary has the right SPD/bond (cross-check with **Bonding
inventory** on the EARTH tab).

### Step 3 — Earth verification
**EARTH tab → Earth check** (`Lps_EarthCheck`) compares each electrode's
*measured* resistance (`ELC_LPS_EARTH_RESISTANCE_OHM`, typed in from the site
megger test) against the 10 Ω design target and flags electrodes with no
reading. **Bonding inventory** lists every bonding bar/strap/spark gap and the
LPZ boundary it crosses.

### Step 4 — Generate the single-line diagram
**RPRT tab → SLD generate** (`Lps_SldGenerate`) builds a drafting view
("STING - LPS Single Line") with a 4-tier layout: air terminals top, down
conductors, the main earth bar, earth electrodes at the bottom, and SPDs
branching to the left. It's idempotent (re-run replaces its own content) and
draws a title block + legend. **SLD overlay** instead annotates SPDs/earths
onto an existing riser/SLD view. Layout is tunable via
`STING_LPS_SLD_RULES.json` (§28).

### Step 5 — Embodied carbon
**RPRT tab → Carbon** (`Lps_CarbonReport`) computes A1–A3 embodied carbon
(kg-CO₂e) for the LPS: down-conductor mass × material factor (Cu 3.0, Al 8.24,
Steel 1.46, Stainless 6.15 kg-CO₂e/kg, ICE v3.0), plus per-SPD (4.0) and
per-earth-electrode (1.6 → ×steel factor) flat rates. Feeds the project
sustainability total.

### Step 6 — Inspection regime & tagging
**RPRT tab → Inspection schedule** (`Lps_InspectionSchedule`) builds the
periodic-inspection schedule from `ELC_LPS_TEST_DATE_TXT` (visual yearly for
Class I/II, every 2 yr for III/IV; full test on the longer cycle). **TAG7**
(`Lps_Tag7Paragraph`) generates the ISO 19650 TAG7 narrative paragraph for LPS
elements.

### Step 7 — Sign-off & cloud
**RPRT tab → Full report** for the complete BS EN 62305 compliance pack, then
**Sync to Planscape** (`Lps_SyncToServer`) to push the LPS dataset to the
cloud platform for multi-user coordination and the mobile commissioning app.

You now have a fully coordinated, documented, certifiable-grade first-pass
LPS design. Hand it to the lightning-protection engineer for the signed
BS EN 62305-2 study.

---
---

# Part D — Complete function reference

Every LPS command, what it does, what it reads, and what it writes. Commands
appear on the ⚡ panel and/or the BIM tab; the **tag** is the internal
identifier (handy for workflow JSON and for finding things).

## 11. Risk & class functions

### `Lps_ClassSetup` — LPS Class Setup (the Advanced wizard)
**Class:** `LpsClassSetupCommand` · **Panel:** RISK → *Advanced wizard…*; also
the **⚠ Set LPS class…** banner button.

The full BS EN 62305-2 **component risk model** as a five-section wizard. It
sums **eight risk components** for **four loss types** and recommends a class
+ SPD level.

**The eight risk components** (BS EN 62305-2):

| Component | Meaning | Form |
|---|---|---|
| **R_A** | Flash *to* structure — injury (touch/step voltage) | N_D·P_TA·P_B·L_T |
| **R_B** | Flash *to* structure — physical damage / fire | N_D·P_B·L_F |
| **R_C** | Flash *to* structure — failure of internal systems | N_D·P_SPD·L_O |
| **R_M** | Flash *near* structure — failure of internal systems | N_M·P_M·L_O |
| **R_U** | Flash *to* a line — injury (touch) | N_L·P_TU·P_EB·P_LD·L_T |
| **R_V** | Flash *to* a line — physical damage / fire | N_L·P_EB·P_LD·L_F |
| **R_W** | Flash *to* a line — failure of internal systems | N_L·P_SPD·P_LD·L_O |
| **R_Z** | Flash *near* a line — failure of internal systems | N_I·P_SPD·P_LI·L_O |

These roll up into the four risks: **R1** (human life) = R_A+R_B+R_C+R_M+R_U+
R_V+R_W+R_Z (as applicable); **R2** (public service), **R3** (cultural),
**R4** (economic) use the relevant subsets. Each is compared to its tolerable
limit **Rt** (R1 ≤ 1×10⁻⁵, R2 ≤ 1×10⁻³, R3 ≤ 1×10⁻⁴, R4 ≤ 1×10⁻³ /yr).

**The C-factors** (catalogue-driven from `STING_LPS_RISK_FACTORS.json`; pick a
description on the panel, or type a raw override in the *Manual override*
grid):

| Factor | Name | Role |
|---|---|---|
| **Cb** | Building occupancy | Structure-type weighting on structural risk |
| **Cc** | Internal contents | Content-value weighting on economic loss |
| **Cd** | Occupant hazard | Human-occupancy hazard, affects L1 |
| **Ce** | Consequence of failure | Consequence-severity multiplier |
| **Cd_loc** | Location factor | Site isolation/terrain/shielding (taller objects nearby reduce strikes) |

**How class & SPD are chosen — important.** STING does **not** just multiply
by a protection-efficiency. It **re-computes the whole risk** with each
candidate (class, SPD-level) pair substituted in, and returns the *minimal*
pair whose recomputed R1–R4 all clear their tolerances:
- **P_B** (structure-strike damage probability) varies by class — only affects
  R_A/R_B/R_V.
- **P_SPD** (surge protection) and **P_EB** (line bonding) vary by SPD level —
  affect all internal-system components R_C/R_M/R_W/R_Z.
- Search order: class IV→III→II→I, SPD NONE→III-IV→II→I→better→best, by
  increasing combined protection level; first pair that clears wins.

That separation is exactly why you get "low class + real SPDs": air-termination
class fixes physical-damage risk; SPDs fix electronics/line-surge risk.

**Other inputs:** Ng (region or override, default 2.0; can auto-derive from the
project climate-site latitude — §28); A_e (computed from L/W/H or overridden);
spatial shielding P_MS = (K_S1·K_S2·K_S3·K_S4)² where K_S1/K_S2 use mesh
widths, K_S3 wiring routing, K_S4 = 1/Uw; service-line characteristics for the
N_L/N_I line terms.

**Output:** LPS required YES/NO, recommended class, recommended SPD level,
residual risk per class, dominant risk component. With *Auto-apply* ticked it
stamps the class onto Project Information.

> **Honest limitation:** loss values and any line parameters you don't supply
> fall back to the standard's typical figures. Strong first-pass tool — confirm
> with a signed BS EN 62305-2 study for certification.

### `Lps_RunRiskInline` — Run risk (inline)
**Class:** `LpsRiskAssessmentInlineCommand` · **Panel:** RISK → *Run risk*.

The same risk engine, run directly from the RISK tab's inline fields
(dimensions, C-factor dropdowns, topology checkboxes) without the full wizard.
Fills the **RISK BY LOSS TYPE**, **RESIDUAL RISK BY CLASS** and **RESULT**
grids in place. Use this for fast what-ifs; use the wizard for the detailed
line/wiring/fire refinements.

### `Lps_Dashboard` — Dashboard
**Class:** `LpsDashboardCommand` · **Panel:** RISK → *Dashboard*.

One-screen roll-up: active class, element counts (air terminals / down
conductors / earths / bonding / SPDs), compliance %, earth-resistance status,
last test date. Your at-a-glance project health for LPS.

---

## 12. Air-termination functions

### `Lps_MarkTypes` — Mark types
**Class:** `LpsMarkElementTypesCommand` · **Panel:** AIR-TERM → *Mark types*.

Stamps `ELC_LPS_ELEMENT_TYPE_TXT` (AIR_TERMINAL / AIR_TERMINAL_MESH /
DOWN_CONDUCTOR / EARTH_ELECTRODE / BONDING_BAR / BOND / SPD / TEST_CLAMP) on
selected or matched elements so every other command can identify each one.
**Run this first** on any model — most "panel is empty" / "everything reads
exposed" problems are unmarked elements.

### `Lps_3DCoverage` — 3D coverage (rolling sphere)
**Class:** `LpsRollingSphere3DCommand` · **Panel:** AIR-TERM → *3D coverage*.

The rigorous rolling-sphere test in 3D. Algorithm:
1. Load the class rolling-sphere radius R (20/30/45/60 m).
2. Collect air-terminal tip points and roof solids.
3. Sample each roof's upward-facing top faces on a ~2 m UV grid (falls back to
   a bounding-box top scan if no clean top face).
4. For each sample point P, test **protected vs exposed** using cooperative
   geometry: a terminal T *can reach* P if the horizontal distance ≤
   √(2R·H − H²) where H = T.Z − P.Z (0 < H < 2R). For each reaching T it builds
   the **apex sphere** (the unique radius-R sphere touching T and P from above)
   and checks whether any *other* terminal lies inside it (which would block
   that reach). **P is protected** iff every reaching terminal is blocked;
   **exposed** iff at least one reaching terminal's apex sphere is clear.
5. Drops **red DirectShape marker cubes** (~120 mm) at every exposed point in a
   dedicated 3D view "STING - LPS Coverage 3D."

**Output:** roofs analysed, terminal count, sample points, protected % /
exposed %, markers placed, and a METHOD note.

> **It's an air-terminal-only model.** It credits rods, *not* roof mesh,
> parapets, taller adjacent roofs or the ground. Sparse/low rods leave gaps
> that a mesh would actually cover — intentionally conservative. Sanity-check
> on a single mast (the point under it must be protected).

### `Lps_PlanVisualise` — Plan visualise
**Class:** `LpsPlanViewVisualizerCommand` · **Panel:** AIR-TERM →
*Plan visualise*.

Overlays the rolling-sphere "strike collection" footprint on a plan view — the
2D companion to 3D coverage, useful for a quick top-down read of which areas
each terminal protects.

---

## 13. Down-conductor & separation functions

### `Lps_DownConductor` — Check spacing
**Class:** `LpsDownConductorCheckerCommand` · **Panel:** CONDUCTORS →
*Check spacing*.

Two checks per the active class: (1) **spacing** between adjacent down
conductors vs the class limit (I/II = 10 m, III = 15 m, IV = 20 m); (2)
**minimum cross-section** by material (Cu 50, Al 70, Steel 50 mm² —
BS EN 62305-3 Table 6). Flags any conductor that's too far from its neighbour
or too thin.

### `Lps_SepDistance` — Sep distance
**Class:** `LpsSeparationDistanceCheckerCommand` · **Panel:** CONDUCTORS →
*Sep distance*.

Computes the side-flash separation distance `s = (ki/km)·kc·l` each down
conductor must keep from internal metalwork, and stamps
`ELC_LPS_SEPARATION_DISTANCE_MM`. **ki** by class (0.04–0.08), **km** by medium
(air 1.0, solid 0.5 — *the insulating medium, not the conductor metal*; external
runs default to air, the conservative choice), **kc** current-sharing, **l**
conductor length.

### `Lps_RecalcKc` — Recalc kc
**Class:** `LpsRecalcKcFactorCommand` · **Panel:** CONDUCTORS → *Recalc kc*.

Recomputes the current-sharing factor `kc` from the number of down conductors
plus whether there's a ring conductor and bonding network, then re-stamps the
separation distances. kc: n=2 → 0.66, n=3 → 0.44, n=4 → 0.33, n≥5 → 1/n. Run
this after adding/removing down conductors or changing the earthing topology —
more conductors + a ring + bonding give a smaller kc and therefore smaller
required separation. Writes `ELC_LPS_KC_FACTOR_NR`.

---

## 14. Earth & bonding functions

### `Lps_EarthCheck` — Earth check
**Class:** `LpsEarthResistanceValidatorCommand` · **Panel:** EARTH →
*Earth check*.

Compares each earth electrode's *measured* resistance
(`ELC_LPS_EARTH_RESISTANCE_OHM`) against the 10 Ω design target and flags any
electrode with no reading (needs a physical megger test on site). Earth
resistance is a measured value — type it in per electrode (EARTH tab grid or
the element parameter); the families' `_NominalResistance_ohm` formula is only
a 100 Ω·m-soil estimate.

### `Lps_Bonding` — Bonding inventory
**Class:** `LpsBondingInventoryCommand` · **Panel:** EARTH →
*Bonding inventory*.

Lists every bonding bar, strap and spark gap, with the LPZ boundary each
crosses (`ELC_LPS_FROM_LPZ_TXT` → `ELC_LPS_TO_LPZ_TXT`) and its bond type
(DIRECT / SPD / ISOLATING_SPARK_GAP). Use it to verify every zone-crossing
service is bonded.

---

## 15. SPD (surge protection) functions

All four read/write through the SPD catalogue
(`STING_LPS_SPD_CATALOGUE.json` + optional project override) via the
`SpdCoordinator` engine.

### `Lps_SpdCoordinate` — Coordinate
**Class:** `SpdCoordinationCheckCommand` · **Panel:** SPD → *Coordinate*.

Validates the installed SPD cascade (IEC 62305-4 / 61643-11):
- **SPD present** — a Type 1 at the main incomer and Type 2 at sub-DBs per the
  required-types table.
- **Up ≤ Uw** — every SPD's let-through voltage ≤ the header Uw (fails if not).
- **Iimp ≥ class minimum** — every Type 1 (or Combined 1+2) meets the class's
  minimum impulse current.
- **Cascade energy coordination** — each Type 1→Type 2 pair must be ≥ 10 m of
  cable apart **or** manufacturer-paired (same Manufacturer); warns if neither.

Returns a Pass/Warn/Fail item per rule.

### `Lps_SpdRecommend` — Recommend
**Class:** `SpdRecommendCommand` · **Panel:** SPD → *Recommend*.

For each required location, filters the catalogue by the location's required
type and the class's minimum Iimp, then returns the lowest-Up matching product.
Gives you a coordinated Type 1 / 2 / 3 set sized to the project class and Uw.

### `Lps_SpdExportBom` — Export BOM
**Class:** `SpdExportBomCommand` · **Panel:** SPD → *Export BOM*.

Exports the SPD schedule as a bill of materials for procurement
(manufacturer, model, type, ratings, location, quantity).

### `Lps_SpdSaveOverride` — Save to project
**Class:** `SpdSaveOverrideCommand` · **Panel:** SPD → *Save to project*.

Writes the current SPD grid to the project-scoped catalogue override
`<project>/_BIM_COORD/lps_spd_catalogue.json` so your chosen products persist
and feed future recommendations. (Override entries replace corporate entries
by `id`; new ones append. The engine watches this file and reloads on change.)

### `Lps_SpdSpecSheet` — Spec sheets
**Class:** `LpsSpdSpecSheetCommand` · **Panel:** SPD → *Spec sheets*.

Generates per-product SPD specification/datasheets from the catalogue data for
the submittal pack.

---

## 16. Zone functions

### `Lps_ZoneTagRooms` — Tag rooms
**Class:** `LpsRoomZoneTagCommand` · **Panel:** ZONES → *Tag rooms*.

Assigns each room a Lightning Protection Zone (`ELC_LPS_ZONE_TXT`) — LPZ 0A
(direct strike, full current), 0B (no direct strike), 1, 2, 3 as you move
deeper into the shielded structure.

### `Lps_ColourZones` — Colour zones
**Class:** `LpsColourZonesCommand` · **Panel:** ZONES → *Colour zones*.

Colour-codes rooms by their LPZ on the active view (graphic overrides).

### `Lps_ClearColours` — Clear colours
**Class:** `LpsClearZoneColoursCommand` · **Panel:** ZONES → *Clear colours*.

Removes the LPZ colour overrides from the active view.

---

## 17. Reporting, documentation & integration functions

### `Lps_Compliance` — Compliance
**Class:** `LpsComplianceCheckCommand` · **Panel:** RPRT → *Compliance*.

The all-in-one audit: air terminals present? earth electrodes? ≥2 down
conductors within class spacing? earth resistance under target? separation
distances stamped? rooms zoned? conductor cross-sections adequate? test date
current? Reports PASS / WARN / FAIL with offending elements selectable; writes
`ELC_LPS_COMPLIANCE_STATUS_TXT`.

### `Lps_FullReport` — Full report
**Class:** `LpsFullReportCommand` · **Panel:** RPRT → *Full report*.

Compiles the complete BS EN 62305 compliance report — risk summary + every
check + the element inventory — into one document.

### `Lps_InspectionSchedule` — Inspection schedule
**Class:** `LpsInspectionSchedulerCommand` · **Panel:** RPRT →
*Inspection schedule*.

Builds the periodic-inspection schedule from `ELC_LPS_TEST_DATE_TXT`: visual
inspection yearly for Class I/II, every 2 yr for III/IV; full test on the
longer cycle (per the class `inspectionIntervalMonths`). Populates the
INSPECTION SCHEDULE grid (Last test / Next due / Status).

### `Lps_CreateSchedules` — Create schedules
**Class:** `LpsCreateRevitScheduleCommand` · **Panel:** RPRT →
*Create schedules*.

Creates native Revit schedules of the LPS elements (air terminals, down
conductors, earths, bonding, SPDs) with their parameters — drops straight onto
sheets.

### `Lps_SldGenerate` — SLD generate
**Class:** `LpsSldGenerateCommand` · **Panel:** RPRT → *SLD generate*.

Generates a single-line diagram in a drafting view "STING - LPS Single Line."
4-tier vertical layout (feet, configurable via `STING_LPS_SLD_RULES.json`):
air terminals at top (row 50), down-conductor labels (row 20), main earth bar
(row 0), earth electrodes (row −25), SPDs branching left (row 5). Draws boxes,
connecting lines, labels (★ AT- / DC- / ⏚ EE- / ★ SPD-), a title block and a
legend. Idempotent — re-running clears and rebuilds its own marked content.
Stamps the drawing-type id `elec-lps-coverage-A3`.

### `Lps_SldOverlay` — SLD overlay
**Class:** `LpsSldOverlayCommand` · **Panel:** RPRT → *SLD overlay*.

Annotates SPDs and earth electrodes onto an **existing** SLD/riser drafting
view (one it finds by name containing "SLD" / "Single Line" / "Riser") rather
than building a new diagram. Drag the labels into place afterwards.

### `Lps_CarbonReport` — Carbon
**Class:** `LpsCarbonReportCommand` · **Panel:** RPRT → *Carbon*.

Computes A1–A3 embodied carbon (kg-CO₂e, ICE v3.0 factors): down-conductor mass
(cross-section × length × density) × material factor (Cu 3.0, Al 8.24, Steel
1.46, Stainless 6.15), plus per-SPD (4.0) and per-earth-electrode (1.6 × steel
factor) flat rates. Designed to roll into the project sustainability total.

### `Lps_Tag7Paragraph` — TAG7
**Class:** `LpsTag7ParagraphCommand` · **Panel:** RPRT → *TAG7*.

Generates the ISO 19650 **TAG7** narrative paragraph container
(`ELC_TAG_7_PARA_LPS_TXT`) for LPS elements — the rich, human-readable
description STING's tagging system uses.

### `Lps_BatchFamilyStamper` — Stamp families
**Class:** `LpsBatchFamilyStamperCommand` · **Panel:** RPRT → *Stamp families*.

Batch-injects the STING `ELC_LPS_*` shared parameters into every `.rfa` in the
`Families/LPS/` folder (recursively). See §24.

### `Lps_SyncToServer` — Sync to Planscape
**Class:** `LpsSyncToServerCommand` · **Panel:** RPRT → *Sync to Planscape*.

Pushes the LPS dataset (elements, class, compliance, SPDs, inspection) to the
Planscape cloud platform for multi-user coordination and the mobile
commissioning app.

### `Lps_ReloadCatalogue` — Reload catalogue
**Panel:** RPRT → *Reload catalogue*.

Drops the cached SPD/SLD/class data so edits to the JSON data files (or a
project override) are picked up without restarting Revit. (Special handler —
calls the engine reloads directly.)

### `Lps_OpenSettings` — Settings
**Panel:** header → *⚙ Settings*.

Opens a dialog showing the resolved data-file paths (classes, risk factors,
risk tables, flash density, SPD catalogue, SLD rules) so you know exactly which
JSON each calculation is reading.

### `Lps_LoadModel` — Load model
**Class:** `LpsLoadFromModelCommand` · **Panel:** header → *⟳ Load model*.

Scans the model (per the Scope setting) and fills every tab's grid from the
placed LPS elements. Run after **Mark types**, and any time the model changes.

---
---

# Part E — Building the LPS families (detailed)

This is the deep dive on **family authoring** — how STING's family functions
work, what they do for you, and exactly how to build (or stamp) the 20 Revit
families the module needs.

## 18. How family authoring works in STING (the mental model)

An LPS family in STING has **three layers**:

1. **Revit category + 3D geometry** — what the element *is* (Electrical
   Equipment or Generic Model) and what it looks like. **You author this** in
   the Family Editor.
2. **Type parameters** — the family's own engineering values (cross-section,
   rod length, Iimp, etc.), some driven by **formulas**. **You add these**
   (or *Create shells* adds them for you).
3. **STING shared parameters (`ELC_LPS_*` + ISO 19650 tokens)** — the bound,
   GUID-stable parameters the LPS commands read and write.
   **STING injects these for you.**

The single source of truth for all of this is
`Families/LPS/LPS_FAMILY_INVENTORY.json` — a machine-readable spec listing,
per family, the template, category, subcategories (with line weight + RGB),
type parameters (with formulas), instance/shared parameters, connectors and
geometry notes. The authoring commands and the geometry guide both read it.

### What STING can do *for* you — two automation commands

| Capability | Command | What it does |
|---|---|---|
| **Auto-create the empty family shells** (`.rfa`) from their `.rft` templates, **with** subcategories + type parameters (+ formulas) + all `ELC_LPS_*` shared parameters pre-built, geometry left empty | **Create shells** (`LpsCreateFamilyShellsCommand`) | For every family in the inventory: news up a `.rfa` from the named template, adds the subcategories, adds the type parameters in two passes (Pass 1 values, Pass 2 formulas — so formulas can reference params that don't yet exist), injects the shared parameters via `FamilyParamEngine.ProcessFamily` (Purge=None), and saves into the right tier folder. **Skips any existing `.rfa`** (never overwrites — safe to re-run, vendor/authored files protected). |
| **Inject the shared parameters** into an existing family — one (in the editor) or a whole folder | **Family Parameter Creator** / **Batch Family Parameter Stamper** (`LpsBatchFamilyStamperCommand`) | Detects the family category, reads the required `ELC_LPS_*` set from the inventory, and binds them by GUID (instance vs type per the inventory). Only adds what's missing. Geometry untouched. |

**So the division of labour is:** *STING builds the shells and all the
parameter scaffolding; you model the 3D solids (and the SPD connectors).*
Run **Create shells** once at project start, then open each shell and add
geometry.

### What `Create shells` builds vs leaves manual

| Built by Create shells | Left for you |
|---|---|
| `.rfa` from the correct `.rft` template | The 3D solid geometry |
| All subcategories (name + line weight + colour) | Assigning solids to subcategories |
| All type parameters + their formulas | The SPD electrical connector (needs a face to host) |
| All `ELC_LPS_*` shared parameters + ISO tokens | The "Insertion Point" reference plane |

---

## 19. The 20 families at a glance

Six conceptual tiers (the JSON groups them 1–5 with bonding split across
3/4). **Always use a 3D template — never `Metric Generic Annotation.rft`**,
which makes a flat 2D symbol the checks/coverage can't use.

| Tier | Family | Template | Category | Element type tag |
|---|---|---|---|---|
| **1 Air termination** | Franklin Rod | Metric Electrical Equipment | Electrical Equipment | AIR_TERMINAL |
| 1 | Mesh Tape (line) | Metric Generic Model line based | Generic Models | AIR_TERMINAL_MESH |
| 1 | Mesh Node (cross-connector) | Metric Generic Model | Generic Models | AIR_TERMINAL_MESH |
| 1 | ESE Air Terminal (NF C 17-102) | Metric Electrical Equipment | Electrical Equipment | AIR_TERMINAL_ESE |
| 1 | Lightning Mast / Pole | Metric Electrical Equipment | Electrical Equipment | AIR_TERMINAL |
| **2 Down conductors** | Bare Tape (line) | Metric Generic Model line based | Generic Models | DOWN_CONDUCTOR |
| 2 | Concealed (in column/wall) | Metric Generic Model line based | Generic Models | DOWN_CONDUCTOR |
| 2 | Test Clamp / Disconnect | Metric Electrical Equipment | Electrical Equipment | TEST_CLAMP |
| 2 | Roof Penetration | Metric Generic Model | Generic Models | DOWN_CONDUCTOR |
| **3 Earth termination** | Earth Rod (Type A) | Metric Electrical Equipment | Electrical Equipment | EARTH_ELECTRODE |
| 3 | Earth Plate | Metric Electrical Equipment | Electrical Equipment | EARTH_ELECTRODE |
| 3 | Ring Earth (Type B, line) | Metric Generic Model line based | Generic Models | EARTH_ELECTRODE |
| 3 | Foundation Earth | Metric Generic Model | Generic Models | EARTH_ELECTRODE |
| 3 | Main Earth Bar (MEB) | Metric Electrical Equipment | Electrical Equipment | BONDING_BAR |
| 3 | Earth Pit / Inspection Chamber | Metric Generic Model | Generic Models | EARTH_ELECTRODE |
| **4 Bonding** | Bonding Bar (sub-MEB) | Metric Electrical Equipment | Electrical Equipment | BONDING_BAR |
| 4 | Bonding Strap (line) | Metric Generic Model line based | Generic Models | BOND |
| 4 | Isolating Spark Gap | Metric Electrical Equipment | Electrical Equipment | BOND |
| **5 Surge protection** | SPD Type 1 (service entry) | Metric Electrical Equipment | Electrical Equipment | SPD |
| 5 | SPD Type 2 (sub-DB) | Metric Electrical Equipment | Electrical Equipment | SPD |
| 5 | SPD Type 3 (final circuit) | Metric Electrical Equipment | Electrical Equipment | SPD |
| 5 | SPD Combined Type 1+2 | Metric Electrical Equipment | Electrical Equipment | SPD |

**Minimum viable set for a Class II project (8 families):** Franklin Rod, Bare
Down Conductor, Test Clamp, Earth Rod, Main Earth Bar, Bonding Strap, SPD
Type 1, SPD Type 2. Author these first; the other 12 cover edge cases (ESE for
French/Spanish jobs, foundation earth for new builds, spark gaps for cathodic
protection, etc.).

---

## 20. The universal authoring recipe

For **every** family the steps are the same. Two starting points:

**Fast start (recommended):** run **Create shells** (⚡ panel → RPRT/family
section) once. It produces every shell with subcategories, type parameters,
formulas and shared parameters already built. Then for each shell, do steps
4 + 7 + 8 + 10 below (geometry, insertion plane, save) and skip the rest.

**From scratch:**

1. **New family from the right template.** Revit → **File → New → Family** →
   pick the `.rft` from the table above (Electrical Equipment, Generic Model,
   or Generic Model *line based*).
2. **Set the family category** (Generic Model templates only) via **Create →
   Family Category and Parameters**.
3. **Lay down Reference Planes / Reference Lines first.** These are the
   skeleton — every dimension locks to a reference (padlock), never hard-typed
   into an extrusion, otherwise type changes won't flex the geometry. Line-based
   families sweep their solid along the host Reference Line.
4. **Author the geometry** (extrusion / sweep / revolve) per the
   `geometryNotes` in the inventory for that family (§21).
5. **Create the subcategories** (Family Category and Parameters →
   Subcategories → New) with the **line weight + RGB colour** from the
   inventory (§22), and assign each solid to its subcategory.
6. **Add the type parameters** (Family Types → New Parameter): set storage
   type (Length / Number / Integer / Text), the default value, and **paste the
   formula** where given (§21). Lock geometry dimensions to these parameters.
7. **Add an "Insertion Point" reference plane** at the placement origin.
8. **Save** into the correct tier folder under `Families/LPS/` (§19/§22).
9. **Inject the STING shared parameters** (§24) — **after** step 6, because
   STING only adds the shared params that aren't already there and your
   formulas may reference them.
10. **Re-save.**

> ⚠ **Order matters:** parameter injection (step 9) must run *after* the type
> parameters exist (step 6). Otherwise a type-parameter formula could
> reference a shared parameter that isn't there yet.

---

## 21. Tier-by-tier deep dive (geometry, parameters, formulas)

The inventory carries the authoritative spec; here's the working detail.

### Tier 1 — Air termination (§5.2)

**Franklin Rod** *(author this first — the workhorse).*
Geometry: vertical pin of `TipDiameter_mm` at the top, a mast tapering from
`MastDiameter_mm` at the base to the tip over `MastLength_mm`, an ~80 mm round
base flange × 6 mm, hosted off a base Reference Plane (roof / top of column).
Type params: `TipDiameter_mm` (16), `MastDiameter_mm` (20), `MastLength_mm`
(1000), `TipMaterial`, `ManufacturerCode`, `ApprovalRef`. Subcategories: AT_Tip,
AT_Mast (orange 230,81,0), AT_Base (grey).

**Mesh Tape (line)** — line-based swept solid: a rectangular cross-section
(`TapeWidth_mm` × `TapeThickness_mm`) centred on and following the host line,
lying flat on the roof. Formulas: `CrossSection_mm2 = TapeWidth_mm *
TapeThickness_mm`; `MassPerMeter_kg = CrossSection_mm2 * 8.96 / 1000`. Set
*Always vertical* OFF, *Always horizontal* ON for flat roofs.

**Mesh Node** — a 60×60×6 mm bronze pad with through-bolt(s) at mesh-tape
intersections. Type params: `ClampType`, `BoltDiameter_mm` (8).

**ESE Air Terminal (NF C 17-102)** — domed head ~250 mm with a central pin, on
a mast `TipHeight_m` below. Protection-radius formula:
`_ProtectionRadius_m = TipHeight_m + 1.06 * AdvanceTime_us`. **Not BS EN 62305
compliant by itself** — only ship for French/Spanish jurisdictions; UK/IEC
projects use the Franklin rod.

**Lightning Mast / Pole** — a tall steel pole with a Franklin tip; for
site-wide protection of low buildings, tanks, comms kit. Type params:
`TotalHeight_m` (10), `MastDiameter_mm` (60), `FoundationType`. (Consider
building one parametric family with `TotalHeight_m` instead of a separate
Franklin + Mast.)

### Tier 2 — Down conductors (§5.3)

**Bare Tape (line)** — line-based; place by clicking start+end, geometry sweeps
along the host Reference Line. Formulas: `CrossSection_mm2 = TapeWidth_mm *
TapeThickness_mm`; `MassPerMeter_kg = CrossSection_mm2 / 1000000 *
Density_kgM3`. Min cross-section: Cu 50 / Al 70 / Steel 50 mm² (Table 6).

**Concealed (in column/wall)** — same topology as bare but a round bar
(`ConductorDiameter_mm` 10) inside a column/wall; render **dashed** (distinct
subcategory `STING_LPS_DownConductor_Concealed`).

**Test Clamp / Disconnect** — small two-bolt bronze pad at ~1.5 m AGL on each
down conductor for periodic testing (Annex E §E.4 requires a test joint between
every DC and its earth electrode). Carries `ELC_LPS_TEST_DATE_TXT` and
`ELC_LPS_EARTH_RESISTANCE_OHM`. Make it visible at 1:50 plan.

**Roof Penetration** — weatherproof seal (lead skirt / EPDM gasket on a
`FlangeOD_mm` 150 flange) where the DC drops from roof to wall/column.

### Tier 3 — Earth termination (§5.4)

**Earth Rod (Type A)** — vertical rod `RodLength_m` (2.4) × `RodDiameter_mm`
(16), driven into the ground, top ~100 mm above grade with a test-clamp
connection, internal-thread coupling for stacking. Formula (estimate only):
`_NominalResistance_ohm = 100 / (2 * 3.14159265 * RodLength_m)` (assumes
ρ=100 Ω·m loam) — the *measured* value goes in `ELC_LPS_EARTH_RESISTANCE_OHM`.

**Earth Plate** — square Cu plate (`PlateWidth_mm`×`PlateHeight_mm`×
`PlateThickness_mm`) buried 600 mm deep where rock/clay/frozen soil prevents
rod driving.

**Ring Earth (Type B, line)** — line-based closed loop around the perimeter,
buried 0.5 m, ≥1 m from foundations, ≥80% in direct soil contact; joins all DCs
into one equipotential ring (the preferred arrangement). Type B.

**Foundation Earth** — galvanised steel cast into the foundation slab (rebar
reused where cover ≥50 mm); the most efficient earth for new builds. Type B.

**Main Earth Bar (MEB)** — horizontal copper bar (`BarLength_mm` 300 ×
`BarWidth_mm` 40 × `BarThickness_mm` 5, `NumberOfWays` 6) on standoff
insulators; the central node of the bonding network (§6.2.2). Element type tag
BONDING_BAR; bond type DIRECT. Make it visually distinct (amber).

**Earth Pit / Inspection Chamber** — round access chamber housing the test
clamp + top of an earth rod, with a removable cover.

### Tier 4 — Equipotential bonding (§6.2)

**Bonding Bar (sub-MEB)** — a smaller bar (`BarLength_mm` 200, `NumberOfWays`
4) at each LPZ boundary, connecting local services (water/gas/steel) to the MEB.

**Bonding Strap (line)** — line-based insulated Cu cable (`ConductorCS_mm2` 16)
from a service/structure to a bonding bar. Min 16 mm² Cu for non-LPS bonding;
50 mm² where LPS current can flow (Table 9). Carries `ELC_LPS_FROM_LPZ_TXT` /
`ELC_LPS_TO_LPZ_TXT`.

**Isolating Spark Gap** — for bonding through DC-isolated systems (cathodic
protection, dielectric pipe couplings): fires at surge voltage but isolates DC
normally. `RatedImpulseCurrent_kA` (50), `SparkoverVoltage_V` (750). Bond type
ISOLATING_SPARK_GAP.

### Tier 5 — Surge protection (IEC 62305-4 / 61643)

All four are DIN-rail modular boxes; the differences are **type parameters**
(Iimp / In / Up / Uc) and box module count. Each **needs an electrical
connector** (§23).

| Family | Iimp kA | In kA | Up kV | Standard / notes |
|---|---|---|---|---|
| **SPD Type 1** | 25 | — | 1.5 | IEC 61643-11 Type 1 (10/350 µs), service entry, LPZ 0/1, ~4 modules |
| **SPD Type 2** | — | 20 | 1.4 | Type 2 (8/20 µs), sub-DB, LPZ 1/2 |
| **SPD Type 3** | — | 5 | 1.3 | Type 3 (1.2/50 + 8/20), within 10 m of sensitive kit, single-phase |
| **SPD Combined 1+2** | 12.5 | 20 | 1.5 | One device where there's only one DB between entry and equipment |

Author one master Type 1, then duplicate-and-edit for 2/3/Combined (most params
+ box shared). Iimp minimum by class: ≥25 kA class I, ≥20 kA class II, ≥12.5 kA
class III/IV.

### Formula quick-reference (paste verbatim into Family Types)

| Family | Type parameter | Formula |
|---|---|---|
| Mesh Tape | `CrossSection_mm2` | `TapeWidth_mm * TapeThickness_mm` |
| Mesh Tape | `MassPerMeter_kg` | `CrossSection_mm2 * 8.96 / 1000` |
| Down Conductor Bare | `CrossSection_mm2` | `TapeWidth_mm * TapeThickness_mm` |
| Down Conductor Bare | `MassPerMeter_kg` | `CrossSection_mm2 / 1000000 * Density_kgM3` |
| ESE Air Terminal | `_ProtectionRadius_m` | `TipHeight_m + 1.06 * AdvanceTime_us` |
| Earth Rod | `_NominalResistance_ohm` | `100 / (2 * 3.14159265 * RodLength_m)` |

Revit formula notes: `*` `/` `+` as usual; wrap negatives in parens `(-1)`;
the Family Types panel highlights errors in red — fix before save; Length
parameters auto-convert units (1000 internally = 1 m, displays "1000 mm").

---

## 22. Subcategories & colour conventions

Assign each solid to a subcategory so STING's view filters auto-colour the
elements (they match `STING_AEC_FILTERS.json` / the `corp-elec-lps` style
pack — no per-family work). Line weights and RGB from the inventory:

| Subcategory family | Line weight | RGB | Used by |
|---|---|---|---|
| `STING_LPS_AT_*` / `STING_LPS_Mesh_*` / `STING_LPS_Mast` | 3–4 | 230,81,0 (orange) | Air termination |
| `STING_LPS_ESE_Head` | 4 | 120,40,150 (violet) | ESE head |
| `STING_LPS_DownConductor` | 5 | 211,47,47 (red) | Bare down conductors |
| `STING_LPS_DownConductor_Concealed` | 4 dashed | 211,47,47 | Concealed DC runs |
| `STING_LPS_EarthRod / Plate / RingEarth / FoundationEarth` | 3–4 | 46,125,50 (green) | Earth electrodes |
| `STING_LPS_MEB / BondingBar / BondingStrap / SparkGap` | 3–4 | 255,193,7 (amber) | Bonding network |
| `STING_LPS_SPD` | 3–4 | 106,27,154 (purple) | SPD units |
| `STING_LPS_TestClamp` | 3 | 0,121,107 (teal) | Test joints |
| `STING_LPS_Penetration / EarthPit` | 2–3 | 120,120,120 (grey) | Penetrations / pits |

### Folder layout (where authored files go)

```
Families/LPS/
├── 1_AirTermination/   (Franklin, MeshTape, MeshNode, ESE, Mast)
├── 2_DownConductors/   (Bare, Concealed, TestClamp, RoofPenetration)
├── 3_Earth/            (EarthRod, EarthPlate, RingEarth, FoundationEarth, MainEarthBar, EarthPit)
├── 4_Bonding/          (BondingBar, BondingStrap, SparkGap)
└── 5_SPD/              (SPD_Type1, Type2, Type3, Combined)
```

STING finds them automatically — `LpsEngine.CollectLpsFamily()` and **Load
model** walk every `.rfa` recursively; no registration once they're in the
right tier folder. (Placed instances are picked up by the 5-min-cached
`LpsElementIndex`.)

---

## 23. SPD electrical connectors

SPDs are the one family that needs a **connector** so the panel-schedule and
voltage-drop engines can trace power through them. (Create shells can't add it —
a connector needs a hosting face, and the shell has no geometry yet, so add it
after you model the body.)

For each SPD variant:
1. Family Editor → **Create → Electrical Connector**, place on the back face.
2. Set parameters:
   - **System:** Power - Balanced (Type 1 / 2 / Combined) or Power - Other
     (single-phase Type 3).
   - **Voltage:** 400/230 V (3-phase) or 230 V (single-phase).
   - **Number of Poles:** 4 (3+N) for Type 1/2/Combined; 3 (L+N+PE) for
     single-phase Type 3.
   - **Apparent Load:** 0 VA (SPDs are passive when not firing).
3. *(Optional)* a second `Communication` connector for the status/alarm
   contact, useful for BMS integration.

---

## 24. Injecting the shared parameters (what STING does for you)

You never type GUIDs by hand. The injector (`FamilyParamEngine.ProcessFamily`)
reads the family's category, looks up the required `ELC_LPS_*` set + common
ISO 19650 tokens from the inventory, and binds each by its stable GUID
(instance vs type per the inventory) — adding only what's missing, leaving
geometry and existing params untouched. Two routes:

**Option A — one family, in the Family Editor:**
Ribbon → STING Tools → Tags → **Family Parameter Creator**. It detects the
category, lists the required parameters, and on **Inject** binds + saves.

**Option B — the whole library at once:**
Save every authored `.rfa` into its tier folder, then ⚡ panel → RPRT →
**Stamp families** (`Lps_BatchFamilyStamper`) — or ribbon → Temp → **Batch
Family Parameter Stamper** — and point it at `Families/LPS/`. It walks every
`.rfa` recursively, opens each, injects, saves, closes. Allow ~15 s per family;
Revit looks frozen during the run — don't switch documents.

**The parameters injected** (per the inventory) are the `ELC_LPS_*` family plus
the ISO 19650 tokens (`ASS_*`, `ELC_TAG_7_PARA_LPS_TXT`). The full schema is in
§29; the most important by family role:

| Parameter | Meaning |
|---|---|
| `ELC_LPS_ELEMENT_TYPE_TXT` | What this element is (AIR_TERMINAL / DOWN_CONDUCTOR / EARTH_ELECTRODE / BONDING_BAR / BOND / SPD / TEST_CLAMP / …) |
| `ELC_LPS_CLASS_TXT` | Protection class I/II/III/IV |
| `ELC_LPS_CONDUCTOR_MATERIAL_TXT` / `ELC_LPS_CONDUCTOR_CROSS_SECT_MM2` | Conductor metal + cross-section |
| `ELC_LPS_EARTH_TYPE_TXT` / `ELC_LPS_EARTH_RESISTANCE_OHM` | Type A/B earthing + measured ohms |
| `ELC_LPS_SEPARATION_DISTANCE_MM` / `ELC_LPS_KC_FACTOR_NR` | Computed side-flash separation + kc |
| `ELC_LPS_BOND_TYPE_TXT` / `ELC_LPS_FROM_LPZ_TXT` / `ELC_LPS_TO_LPZ_TXT` | Bonding details + LPZ crossing |
| `ELC_LPS_SURGE_PROTECTION_LVL_TXT` | SPD Type 1/2/3/Combined |
| `ELC_LPS_ZONE_TXT` | LPZ classification |
| `ELC_LPS_TEST_DATE_TXT` / `ELC_LPS_CERT_REF_TXT` / `ELC_LPS_COMPLIANCE_STATUS_TXT` | Commissioning + audit trail |
| `ELC_LPS_AIRTERM_TAG_TXT` / `…_DOWNCOND_…` / `…_EARTH_…` / `…_BOND_…` / `…_SPD_…` / `…_TESTCLAMP_…` | Per-type ISO 19650 tag containers |

---

## 25. Conformance check & testing

Before deploying authored (or vendor-stamped) families, run STING Tools → Tags
→ **Family Conformance Check** on the `Families/LPS/` folder. It scores each
family /100 against the STING contract: required shared params bound **by
GUID** (not just name), tag-style matrix present, canonical subcategory names,
connectors on SPDs, loads cleanly. **PASS ≥ 85** = production-ready; it writes
a CSV to `<project>/_BIM_COORD/`.

**Per-family smoke test** (7 checks): drop into a blank project; place an
instance — right Revit category? open the ⚡ panel → **Load model** — appears in
the right tab grid? run **Compliance** — sensible result? run **3D coverage** —
family registers? export **IFC** — BS EN 62305 pset lands? renders the right
colour on a `corp-elec-lps` view? All 7 pass ⇒ production-ready.

---

## 26. Vendor families (the fast path)

Don't want to author geometry from scratch? Download a vendor family (DEHN,
OBO, nVent ERICO, Furse/ABB, Indelec — all publish BIM libraries), drop it in
the right tier folder, and run **Stamp families** / **Family Parameter
Creator**. STING overlays its `ELC_LPS_*` parameters without touching the
vendor's geometry — the fastest route to a complete library.

- DEHN: https://www.dehn.com/en/dehn-bim
- OBO: https://www.obo.global/en/services/bim
- nVent ERICO: https://www.erico.com
- Furse / ABB: https://new.abb.com/products/earthing-lightning-protection

---

## 27. Authoring pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Wrong template | Element won't show on the LPS panel | Re-create from the inventory's template |
| Category = Generic Annotation | Element invisible in 3D | Change to Electrical Equipment / Generic Models |
| Type parameter mis-named | Formulas fail / panel can't read values | Match the inventory name exactly (case-sensitive) |
| Forgot a subcategory | Element draws in the default colour | Add the subcategory, assign the solid to it |
| Shared param injected before type params exist | Schedule columns empty / formula errors | Run injection *after* adding type params |
| Line family missing Reference Line | Geometry won't follow the host line | Create a Reference Line and lock the sweep to it |
| Unlocked dimensions | Geometry reverts after a type switch | Lock every dimension to a parameter (padlock) |
| SPD missing connector | Panel schedule / voltage drop can't trace it | Add the electrical connector (§23) |

---
---

# Part F — Reference

## 28. Data files & how to tune them

Everything the module computes is data-driven JSON — edit and re-run (or click
**Reload catalogue**); no recompile.

| File | What it controls |
|---|---|
| `Data/LPS/STING_LPS_CLASSES.json` | Per-class rolling-sphere radius, mesh size, protection angles (height-dependent), down-conductor spacing, min cross-sections (incl. per-material Cu/Al/Steel), ki factor, earth target (10 Ω), inspection interval, protection efficiency; plus material factors km (Cu 1.0 / Al 0.44 / Steel 0.5), tolerable risks Rt, and the standard-to-class display map |
| `Data/LPS/STING_LPS_RISK_FACTORS.json` | Building/content/occupant/consequence (Cb/Cc/Cd/Ce/Cd_loc) weightings, loss-type tolerable risks, service factors — drives the C-factor dropdowns |
| `Data/LPS/STING_LPS_RISK_TABLES.json` | BS EN 62305-2 probability (Annex B) + loss (Annex C) tables the component risk model consumes (RA…RZ) |
| `Data/LPS/STING_LPS_FLASH_DENSITY.json` | Ground flash density Ng by region |
| `Data/LPS/STING_LPS_SPD_CATALOGUE.json` | SPD product reference data (type, Iimp, In, Up, Uc, manufacturer) + required-location table + cascade separation threshold; layered with `<project>/_BIM_COORD/lps_spd_catalogue.json` |
| `Data/STING_LPS_SLD_RULES.json` | Single-line-diagram layout: row Y positions, column pitch, box sizes, SPD offsets, label prefixes, title/subtitle, legend toggles; layered with `<project>/_BIM_COORD/lps_sld_rules.json` |
| `Families/LPS/LPS_FAMILY_INVENTORY.json` | The 20-family spec (templates, subcategories, type params, formulas, shared params, connectors, geometry notes) |
| `Data/WORKFLOW_LpsCommissioning.json` | The LPS commissioning workflow steps |

**Ng resolution order:** the explicit Ng override on the RISK tab →
the project climate-site latitude (`LpsRegionalNg` maps latitude bands:
<23.5° → 12, 23.5–35° → 4, 35–50° → 1.5, ≥50° → 0.5 flashes/km²/yr, from the
HVAC engine's `ClimateRegistry`) → the regional default in
`STING_LPS_FLASH_DENSITY.json` → the risk-model fallback (2.0).

**Project overrides** layer over the corporate baseline for SPD catalogue and
SLD rules: an override entry with a matching `id` replaces the corporate one;
new entries append. The engines watch the override files and reload on change.

---

## 29. Shared-parameter reference

The `ELC_LPS_*` family (bound by stable GUID via STING's parameter registry).
Plus the ISO 19650 tokens every LPS family carries (`ASS_DISCIPLINE_COD_TXT`,
`ASS_LOC_TXT`, `ASS_ZONE_TXT`, `ASS_LVL_COD_TXT`, `ASS_SYSTEM_TYPE_TXT`,
`ASS_FUNC_TXT`, `ASS_PRODCT_COD_TXT`, `ASS_SEQ_NUM_TXT`, `ASS_TAG_1`,
`ASS_TAG_7_TXT`, `ELC_TAG_7_PARA_LPS_TXT`).

| Parameter | Type | Role |
|---|---|---|
| `ELC_LPS_ELEMENT_TYPE_TXT` | Text | Element discriminator (AIR_TERMINAL / AIR_TERMINAL_MESH / AIR_TERMINAL_ESE / DOWN_CONDUCTOR / EARTH_ELECTRODE / BONDING_BAR / BOND / SPD / TEST_CLAMP) |
| `ELC_LPS_CLASS_TXT` | Text | Protection class I/II/III/IV |
| `ELC_LPS_ROLLING_SPHERE_RADIUS_M` | Number | Class rolling-sphere radius |
| `ELC_LPS_PROTECTION_ANGLE_DEG` | Number | Protection-angle method angle |
| `ELC_LPS_CONDUCTOR_MATERIAL_TXT` | Text | COPPER / ALUMINIUM / STEEL / STAINLESS |
| `ELC_LPS_CONDUCTOR_CROSS_SECT_MM2` | Number | Conductor cross-section |
| `ELC_LPS_SEPARATION_DISTANCE_MM` | Number | Computed side-flash separation s |
| `ELC_LPS_KC_FACTOR_NR` | Number | Current-sharing factor kc |
| `ELC_LPS_EARTH_TYPE_TXT` | Text | A (individual) / B (ring/foundation) |
| `ELC_LPS_EARTH_RESISTANCE_OHM` | Number | Measured electrode resistance |
| `ELC_LPS_BOND_TYPE_TXT` | Text | DIRECT / SPD / ISOLATING_SPARK_GAP |
| `ELC_LPS_FROM_LPZ_TXT` / `ELC_LPS_TO_LPZ_TXT` | Text | LPZ boundary a bond crosses |
| `ELC_LPS_ZONE_TXT` | Text | Room LPZ (LPZ0A/0B/1/2/3) |
| `ELC_LPS_SURGE_PROTECTION_LVL_TXT` | Text | SPD Type 1 / 2 / 3 / 1+2 |
| `ELC_LPS_TEST_DATE_TXT` | Text | Last LPS test date |
| `ELC_LPS_CERT_REF_TXT` | Text | Certification reference |
| `ELC_LPS_COMPLIANCE_STATUS_TXT` | Text | PASS / WARN / FAIL verdict |
| `ELC_LPS_AIRTERM_TAG_TXT` etc. | Text | Per-type ISO 19650 tag containers (airterm / downcond / earth / bond / spd / testclamp) |

A parallel set of `WARN_ELC_LPS_*` parameters carry the validation warnings
(class missing, insufficient conductors, high earth resistance, separation
failure, inspection overdue, …).

---

## 30. Standards & class mapping

STING computes in BS EN 62305 and translates the **displayed** class label per
the Standard header setting (`standardToClassMap` in `STING_LPS_CLASSES.json`):

| BS EN / IEC 62305 | NFPA 780 | NF C 17-102 |
|---|---|---|
| I | I (≤23 m) | 1 |
| II | II (23–46 m) | 2 |
| III | III (46–77 m) | 3 |
| IV | IV (>77 m) | 4 |

(NFPA 780 bands by height; NF C 17-102 numbers its protection levels.)

---

## 31. Troubleshooting / FAQ

- **"The LPS panel is empty."** Click **⟳ Load model** — the grids fill from
  the model. Still empty? The elements aren't *typed* — run **Mark types**
  first. Still empty? The `ELC_LPS_*` parameters aren't bound — run
  Tags → **Load Params**.
- **"Everything reads exposed in 3D Coverage."** It's an air-terminal-only
  model; low/sparse rods leave gaps a roof mesh would cover. Check the class is
  set and verify on a single mast (point under it must be protected).
- **"Earth Check says 'no reading.'"** Earth resistance is a *measured* site
  value — type the megger reading into `ELC_LPS_EARTH_RESISTANCE_OHM` on each
  electrode (or via the EARTH tab grid).
- **"Separation distances look huge."** They assume air (km = 1.0) and the
  worst-case kc. Set the real medium and run **Recalc kc** with the correct
  down-conductor count + ring/bonding flags.
- **"The risk assessment recommends SPDs but only Class IV."** Correct and
  expected for an ordinary building with long overhead service lines — the
  surge risk (R_W/R_Z) is driven by the lines, which SPDs fix, not by the
  air-termination class. Check the *dominant risk component* read-out.
- **"SPD coordination warns about the cascade."** A Type 1→Type 2 pair must be
  ≥ 10 m of cable apart OR the same manufacturer (manufacturer-paired). Add
  cable length, swap to a paired set, or add a decoupling reactor.
- **"Schedule columns are blank."** The families are missing the shared
  parameters — run **Stamp families** / **Batch Family Parameter Stamper**.
- **"My edit to a JSON file didn't take effect."** Click **Reload catalogue**
  (RPRT tab) to drop the cache without restarting Revit.
- **"The SLD redrew over my manual notes."** **SLD generate** is idempotent and
  purges *its own* marked content on re-run; put manual annotations on a
  separate view or use **SLD overlay** onto your own riser view.

---

## 32. Quick command index

| Function | Tag | Panel location | Section |
|---|---|---|---|
| Risk assessment (full wizard) | `Lps_ClassSetup` | RISK → Advanced wizard / ⚠ banner | 11 |
| Risk assessment (inline) | `Lps_RunRiskInline` | RISK → Run risk | 11 |
| Dashboard | `Lps_Dashboard` | RISK → Dashboard | 11 |
| Mark element types | `Lps_MarkTypes` | AIR-TERM → Mark types | 12 |
| 3D rolling-sphere coverage | `Lps_3DCoverage` | AIR-TERM → 3D coverage | 12 |
| Plan strike overlay | `Lps_PlanVisualise` | AIR-TERM → Plan visualise | 12 |
| Down-conductor spacing/size | `Lps_DownConductor` | CONDUCTORS → Check spacing | 13 |
| Side-flash separation | `Lps_SepDistance` | CONDUCTORS → Sep distance | 13 |
| Recompute kc | `Lps_RecalcKc` | CONDUCTORS → Recalc kc | 13 |
| Earth resistance | `Lps_EarthCheck` | EARTH → Earth check | 14 |
| Bonding inventory | `Lps_Bonding` | EARTH → Bonding inventory | 14 |
| SPD cascade coordination | `Lps_SpdCoordinate` | SPD → Coordinate | 15 |
| SPD recommendation | `Lps_SpdRecommend` | SPD → Recommend | 15 |
| SPD BOM export | `Lps_SpdExportBom` | SPD → Export BOM | 15 |
| Save SPD to project | `Lps_SpdSaveOverride` | SPD → Save to project | 15 |
| SPD spec sheets | `Lps_SpdSpecSheet` | SPD → Spec sheets | 15 |
| Zone-tag rooms | `Lps_ZoneTagRooms` | ZONES → Tag rooms | 16 |
| Colour zones | `Lps_ColourZones` | ZONES → Colour zones | 16 |
| Clear zone colours | `Lps_ClearColours` | ZONES → Clear colours | 16 |
| Full compliance audit | `Lps_Compliance` | RPRT → Compliance | 17 |
| Full BS EN 62305 report | `Lps_FullReport` | RPRT → Full report | 17 |
| Inspection schedule | `Lps_InspectionSchedule` | RPRT → Inspection schedule | 17 |
| Native Revit schedules | `Lps_CreateSchedules` | RPRT → Create schedules | 17 |
| Generate single-line diagram | `Lps_SldGenerate` | RPRT → SLD generate | 17 |
| Overlay SLD annotations | `Lps_SldOverlay` | RPRT → SLD overlay | 17 |
| Embodied-carbon report | `Lps_CarbonReport` | RPRT → Carbon | 17 |
| TAG7 narrative paragraph | `Lps_Tag7Paragraph` | RPRT → TAG7 | 17 |
| Stamp families (batch) | `Lps_BatchFamilyStamper` | RPRT → Stamp families | 24 |
| Sync to Planscape | `Lps_SyncToServer` | RPRT → Sync to Planscape | 17 |
| Reload data caches | `Lps_ReloadCatalogue` | RPRT → Reload catalogue | 17 |
| Load model into grids | `Lps_LoadModel` | header → ⟳ Load model | 17 |
| Data-file paths | `Lps_OpenSettings` | header → ⚙ Settings | 17 |
| Create the 20 family shells | (Create shells) | ⚡ panel → family section | 18 |
| Inject family params (one) | (Family Parameter Creator) | ribbon → Tags | 24 |
| Audit authored families | (Family Conformance Check) | ribbon → Tags | 25 |

---

**Standard:** BS EN 62305 (2024) / IEC 62305-4 / IEC 61643-11
**Class data:** `Data/LPS/STING_LPS_CLASSES.json`
**Family spec:** `Families/LPS/LPS_FAMILY_INVENTORY.json`
**Authoring detail:** `Families/LPS/AUTHORING_GUIDE.md`
**Last updated:** 2026-05
