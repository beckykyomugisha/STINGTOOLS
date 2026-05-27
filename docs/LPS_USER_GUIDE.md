# STING — Lightning Protection (LPS) User Guide

A plain-English, end-to-end guide to the STING Lightning Protection module:
what it does, how to use every function, and exactly how to build the
Revit families it needs in the Family Editor.

**Standard basis:** BS EN 62305 (parts 1–4) / IEC 62305 — the international
lightning protection standard. STING normalises everything to the BS EN
62305 class system (I / II / III / IV).

> **Before you start — verification note.** STING's LPS code is developed
> in a Linux sandbox without the Revit API, so each release is committed
> "verify in Revit before merge." Treat first-run results on a new build as
> *indicative* until you've sanity-checked them in a live model.

---

## 1. Lightning protection in 60 seconds (the layman version)

A lightning protection system (LPS) gives a lightning strike a safe,
deliberate path to ground so it doesn't blow a hole in the building, start
a fire, or kill the electronics inside. It has five parts:

| Part | Plain English | Standard term |
|---|---|---|
| **Air termination** | The metal rods/wires on the roof that the lightning is *meant* to hit | §5.2 |
| **Down conductors** | The straps running down the walls that carry the current from roof to ground | §5.3 |
| **Earth termination** | The rods/plates/rings buried in the soil that dump the current into the earth | §5.4 |
| **Equipotential bonding** | Links every metal thing (pipes, steel, services) together so nothing is at a different voltage during a strike | §6.2 |
| **Surge protection (SPDs)** | Little sacrificial devices in the electrical panels that clamp the voltage spike before it fries equipment | IEC 62305-4 |

**The two questions every LPS design answers:**

1. **Do I need protection, and how good must it be?** → a *risk assessment*
   (BS EN 62305-2) that produces a **protection class** (I = best … IV =
   basic) and a **surge protection level**.
2. **Is my design actually compliant?** → geometry + parameter checks (down
   conductor spacing, earth resistance, separation distance, coverage,
   conductor sizes, inspection dates).

STING does both, plus tagging, colour-coding, schedules, reports, and a
3D coverage analyser.

---

## 2. Where the tools live

There are **two** places LPS tools appear:

1. **The dedicated "⚡ LPS" dockable panel** (the interactive one).
   - Open it from the Revit ribbon: **STING Tools tab → ⚡ LPS panel → "STING LPS"** button.
   - It docks on the right (tabbed near Properties). It has its own tabs —
     RISK, AIR TERMINATION, DOWN CONDUCTORS, EARTH, BONDING, SPD, ZONES,
     INSPECTION — each backed by a live grid that fills in when you click
     **Load model** or run a check. This panel works exactly like the
     Electrical / HVAC / Plumbing panels: pick a header context, choose a
     scope, click an action, and results appear in the grid.
2. **The "LIGHTNING PROTECTION" section in the main STING panel's BIM tab**
   (the buttons in your screenshot). These are one-click shortcuts to the
   same commands; results pop up in a report window.

> **Tip:** If you didn't know the dedicated ⚡ LPS panel existed — that's
> the interactive surface. The BIM-tab buttons fire the same commands but
> show transient pop-up reports instead of living grids.

---

## 3. The recommended workflow (start to finish)

Follow this order on a new project:

1. **Load parameters** — STING Tools → Tags → **Load Params** (binds the
   `ELC_LPS_*` shared parameters so the LPS commands have somewhere to
   write). Usually already done by project setup.
2. **LPS Class Setup** — run the risk assessment (Section 4). It recommends
   a protection **class** and **SPD level** and stamps the class onto the
   project.
3. **Model the LPS elements** — place air terminals, down conductors, earth
   electrodes, bonding bars and SPDs using the STING LPS families
   (Section 7) or vendor families.
4. **Mark Types** — stamp `ELC_LPS_ELEMENT_TYPE_TXT` on every element so
   the checks know what each one is.
5. **Zone Tag Rooms** — classify spaces into Lightning Protection Zones
   (LPZ 0 / 1 / 2…).
6. **Run the checks** — Compliance Check, Down Conductors, Earth Check,
   Separation Distance, 3D Coverage (Section 5).
7. **Visualise** — Colour Zones, Plan Visualise, 3D Coverage markers.
8. **Document** — Create Schedules, Inspection Schedule, LPS Full Report.
9. **Author / stamp families** as needed (Section 7).
10. **Sync to Planscape** if you're using the cloud platform.

---

## 4. The risk assessment (LPS Class Setup)

**Button:** `LPS Class Setup` (BIM tab) or the RISK tab on the ⚡ LPS panel.

This is the brain of the module. It implements the full BS EN 62305-2
**component risk model**: it adds up eight risk components (RA, RB, RC, RM,
RU, RV, RW, RZ — strikes *to* and *near* the building, and *to* and *near*
the incoming power/telecom lines) for four loss types:

| Risk | Plain English | Tolerable limit (Rt) |
|---|---|---|
| **R1** | Loss of human life | 1 × 10⁻⁵ /yr |
| **R2** | Loss of public service | 1 × 10⁻³ /yr |
| **R3** | Loss of cultural heritage | 1 × 10⁻⁴ /yr |
| **R4** | Economic loss | 1 × 10⁻³ /yr |

### What you type in

The Class Setup wizard has five sections:

1. **Structure** — building name, height, plan area, perimeter, and the
   building type / contents / occupant hazard / consequence dropdowns.
2. **Location & risk** — region (sets the ground flash density Ng, i.e. how
   many lightning strikes per km² per year), optional project Ng override
   (from a lightning-location service), primary loss type, and which
   services (power / telecom / gas / water) connect to the building.
3. **Envelope, wiring & lines (optional, improves accuracy)** — the
   refinements that move the standard's sub-factors:
   - **Equipment impulse withstand U_w (kV)** — how tough your electronics
     are. Drives K_S4 and the line-surge probabilities.
   - **Internal wiring routing/shielding** — K_S3 (unshielded vs shielded /
     conduit, loop precautions).
   - **Shield mesh widths** — K_S1 / K_S2 if the structure / inner zones
     have a spatial magnetic shield.
   - **Fire-protection provisions** (r_p), **ground surface** (r_t),
     **fire-risk** (r_f) and **special-hazard / panic** (h_z) overrides.
   - **Service-line characteristics** — length, overhead/buried, rural /
     urban, HV/LV transformer, shielding — applied to the power & telecom
     lines.
4. **Class confirmation** — accept or override the recommended class.
5. **Apply** — stamps the class onto Project Information.

> Leave the section-3 fields at their defaults and you still get a valid
> screening result; fill them in for a result closer to a stamped study.

### What you get out

- **LPS required? YES/NO** — does any loss type exceed its tolerable limit.
- **Recommended LPS class** (I/II/III/IV) — chosen by *re-computing* the
  risk with each class's physical-damage probability (P_B) until every
  risk clears its limit.
- **Recommended SPD level** — chosen *separately*, because surge protection
  (not the air-termination class) is what clears the electronics-failure
  and line-surge risks. This is why you'll often see e.g. "Class IV + SPD
  III-IV": the building only needs basic air termination but real SPDs.
- **Residual risk per class** and the **dominant risk component** so you
  can see *what* is driving the requirement.

> **Honest limitation:** loss values and any line parameters you don't
> supply fall back to the standard's typical figures. It's a strong design
> aid and first-pass sizing tool — confirm with a full BS EN 62305-2 study
> for certification.

---

## 5. The compliance & engineering checks

Each of these reads the model, compares against the project's LPS class,
and reports PASS / WARN / FAIL with the offending elements selectable.

| Button | What it checks (plain English) | Writes |
|---|---|---|
| **Compliance Check** | The all-in-one audit: are there air terminals? earth electrodes? at least 2 down conductors within the class spacing? earth resistance under target? separation distances stamped? rooms zoned? conductor cross-sections adequate? test date current? | `ELC_LPS_COMPLIANCE_STATUS_TXT` |
| **Down Conductors** | Spacing between down conductors vs the class limit (I/II = 10 m, III = 15 m, IV = 20 m) + minimum cross-section by material | — |
| **Earth Check** | Each earth electrode's measured resistance vs the 10 Ω design target; flags electrodes with no reading (need a physical test) | reads `ELC_LPS_EARTH_RESISTANCE_OHM` |
| **Sep Distance** | The separation distance *s = ki·(kc/km)·l* every down conductor must keep from internal metalwork to avoid a dangerous side-flash | `ELC_LPS_SEPARATION_DISTANCE_MM` |
| **kc Recalculate** | Recomputes the current-sharing factor *kc* from the number of down conductors + whether there's a ring conductor and bonding, then re-stamps separation distances | `ELC_LPS_KC_FACTOR_NR` |
| **3D Coverage** | The rolling-sphere test: rolls a sphere (radius = 20/30/45/60 m by class) over the roof and flags any roof point an air terminal *doesn't* shield, with red 3D markers | creates a 3D view + markers |

### A word on the inputs that drive the checks

- **Protection class** sets the rolling-sphere radius, mesh size, down
  conductor spacing, minimum conductor cross-section, earth-resistance
  target and inspection interval (all from `STING_LPS_CLASSES.json`).
- **Separation distance** uses *km = the insulation medium* (air = 1.0,
  solid like concrete/brick = 0.5) — **not** the conductor metal — per
  Table 5. Externally-routed conductors default to air (the conservative
  choice).
- **3D Coverage** is an *air-terminal-only* model: it credits protection
  from rods, not from roof mesh, parapets, taller adjacent roofs or the
  ground. Gaps between sparse/low rods will read as exposed even where a
  roof-level mesh would protect them — that's intentional and conservative.
  Sanity-check it on a single mast first (the point directly under the mast
  must read *protected*).

---

## 6. Tagging, zoning, visualisation & documentation

| Button | What it does |
|---|---|
| **Mark Types** | Stamps `ELC_LPS_ELEMENT_TYPE_TXT` (AIR_TERMINAL / DOWN_CONDUCTOR / EARTH_ELECTRODE / BONDING_BAR / SPD …) on selected or matched elements so every other command can identify them. **Run this early.** |
| **Zone Tag Rooms** | Assigns each room a Lightning Protection Zone (`ELC_LPS_ZONE_TXT`) — LPZ 0A (direct strike, full current), 0B (no direct strike), 1, 2… as you move deeper inside the shielded structure. |
| **Bonding Inventory** | Lists every bonding bar / strap / spark gap and the LPZ boundary it crosses. |
| **Colour Zones** | Colour-codes rooms by their LPZ on the active view. |
| **Clear Colours** | Removes the LPZ colour overrides. |
| **Plan Visualise** | Overlays the rolling-sphere "strike collection" footprint on a plan view. |
| **Inspection Schedule** | Builds the periodic-inspection schedule (visual every 1 yr for class I/II, 2 yr for III/IV; full test on the longer cycle) from `ELC_LPS_TEST_DATE_TXT`. |
| **Create Schedules** | Creates native Revit schedules of the LPS elements (air terminals, down conductors, earths, bonding, SPDs) with their parameters. |
| **Dashboard** | A one-screen roll-up: class, element counts, compliance %, earth-resistance status, last test date. |
| **LPS Full Report** | Compiles the complete BS EN 62305 compliance report (risk summary + all checks + inventory). |
| **Sync to Planscape** | Pushes the LPS data to the Planscape cloud platform (`/api/.../lps`). |
| **SPD Coordination** | Checks the Type 1 → Type 2 → Type 3 SPD cascade is correctly coordinated by let-through voltage and energy. |

---

## 7. Building the LPS families (the Family Editor part)

STING ships a **machine-readable inventory** of the 20 families it expects
— `Families/LPS/LPS_FAMILY_INVENTORY.json` — across six tiers. STING can
**inject all the shared parameters for you**; **you author the 3D
geometry** in the Revit Family Editor.

### 7.1 What "we create the families" means today (read this first)

| Capability | Status |
|---|---|
| **Inject all required shared parameters** into a family (from the right template's category), leaving geometry untouched | ✅ **Fully supported** — `Family Parameter Creator` (one family, in the editor) or `Batch Family Parameter Stamper` (a whole folder). |
| **Auto-create the empty family shells** (`.rfa`) from their `.rft` templates with subcategories + type parameters (+ formulas) + shared parameters pre-built, ready for you to add geometry | ✅ **Supported** — the **"Create shells"** button (⚡ LPS panel, RPRT/family section). |

So: **STING builds the shells *and* the parameter scaffolding for you; you
add the 3D geometry.** Run **Create shells** once at the start of a project:
for every family in the inventory it creates a new `.rfa` from the named
`.rft` template, adds the subcategories (with line weight + colour), the
type parameters with their formulas, and all the `ELC_LPS_*` shared
parameters — then leaves the geometry empty. Existing `.rfa` files are
**skipped** (never overwritten), so it's safe to re-run as the inventory
grows, and authored / vendor families are protected. After it runs, open
each shell in the Family Editor and model the solids (Section 7.3, steps
3–8), then re-save.

> **What's still manual after Create shells:** the 3D geometry itself, and
> the SPD **electrical connector** (a connector needs a face to host, and
> the shell has no geometry yet). Everything else — category, subcategories,
> type params, formulas, shared params — is pre-built.

### 7.2 The 20 families at a glance

| Tier | Family | Revit template | Category |
|---|---|---|---|
| 1 Air termination | Franklin Rod | Metric Electrical Equipment | Electrical Equipment |
| 1 | Mesh Tape (line) | Metric Generic Model line based | Generic Models |
| 1 | Mesh Node (cross-connector) | Metric Generic Model | Generic Models |
| 1 | ESE Air Terminal (NF C 17-102) | Metric Electrical Equipment | Electrical Equipment |
| 1 | Lightning Mast / Pole | Metric Electrical Equipment | Electrical Equipment |
| 2 Down conductors | Bare Tape (line) | Metric Generic Model line based | Generic Models |
| 2 | Concealed (in column/wall) | Metric Generic Model line based | Generic Models |
| 2 | Test Clamp / Disconnect | Metric Electrical Equipment | Electrical Equipment |
| 2 | Roof Penetration | Metric Generic Model | Generic Models |
| 3 Earth termination | Earth Rod (Type A) | Metric Electrical Equipment | Electrical Equipment |
| 3 | Earth Plate | Metric Electrical Equipment | Electrical Equipment |
| 3 | Ring Earth (Type B, line) | Metric Generic Model line based | Generic Models |
| 3 | Foundation Earth | Metric Generic Model | Generic Models |
| 3 | Main Earth Bar (MEB) | Metric Electrical Equipment | Electrical Equipment |
| 3 | Earth Pit / Inspection Chamber | Metric Generic Model | Generic Models |
| 4 Bonding | Bonding Bar (sub-MEB) | Metric Electrical Equipment | Electrical Equipment |
| 4 | Bonding Strap (line) | Metric Generic Model line based | Generic Models |
| 4 | Isolating Spark Gap | Metric Electrical Equipment | Electrical Equipment |
| 5 Surge protection | SPD Type 1 (service entry) | Metric Electrical Equipment | Electrical Equipment |
| 5 | SPD Type 2 (sub-DB) | Metric Electrical Equipment | Electrical Equipment |
| 5 | SPD Type 3 (final circuit) | Metric Electrical Equipment | Electrical Equipment |
| 5 | SPD Combined Type 1+2 | Metric Electrical Equipment | Electrical Equipment |

> **Always use a 3D template.** Never `Metric Generic Annotation.rft` — that
> makes a flat 2D symbol, and the checks/coverage need real 3D geometry.

### 7.3 Manual authoring — the universal recipe

For **every** family, the steps are the same:

1. **New family from the right template.**
   Revit → **File → New → Family** → pick the `.rft` from the table above
   (Electrical Equipment, Generic Model, or Generic Model *line based*).
2. **Set the family category** (Generic Model templates only) via
   **Create → Family Category and Parameters**.
3. **Lay down Reference Planes / Reference Lines first.** These are the
   skeleton. Every dimension should lock to a reference, never be
   hard-typed into an extrusion — otherwise type changes won't flex the
   geometry. For line-based families, the geometry sweeps along the host
   Reference Line.
4. **Author the geometry** (solid extrusion / sweep / revolve) following the
   `geometryNotes` in the inventory for that family. Example for the
   Franklin Rod: a vertical pin at the top, a mast tapering from base
   diameter to tip, an ~80 mm round base flange, hosted off a base
   Reference Plane.
5. **Create the subcategories** listed for the family
   (**Family Category and Parameters → Subcategories → New**) and set their
   **line weight + RGB colour** from the inventory. Then assign each solid
   to its subcategory. The colours match STING's view filters, so views
   auto-colour-code without extra work:
   - Air termination = orange (230,81,0)
   - Down conductor = red (211,47,47)
   - Earth = green (46,125,50)
   - Bonding = amber (255,193,7)
   - SPD = purple (106,27,154)
   - Test clamp = teal (0,121,107)
6. **Add the type parameters** from the inventory
   (**Family Types → New Parameter**): set the storage type (Length /
   Number / Integer / Text), the default value, and **paste the formula**
   where one is given. Common formulas:
   - Mesh Tape / Bare DC `CrossSection_mm2 = TapeWidth_mm * TapeThickness_mm`
   - `MassPerMeter_kg = CrossSection_mm2 / 1000000 * Density_kgM3`
   - ESE `_ProtectionRadius_m = TipHeight_m + 1.06 * AdvanceTime_us`
   - Earth Rod `_NominalResistance_ohm = 100 / (2 * 3.14159265 * RodLength_m)`
   - Lock the geometry dimensions to these type parameters (padlock icon).
7. **Add an "Insertion Point" reference plane** at the placement origin.
8. **Save** into the correct tier folder under `Families/LPS/` (see 7.5).
9. **Inject the STING shared parameters** — Section 7.4. **Do this AFTER
   step 6**, because STING only adds the shared parameters that aren't
   already there, and your type-parameter formulas may reference them.
10. **Re-save.**

### 7.4 Injecting the shared parameters (STING does this)

You do **not** type GUIDs by hand. Two routes:

**Option A — one family, while it's open in the Family Editor:**
- Ribbon → STING Tools → Tags → **Family Parameter Creator**.
- It detects the family's category and lists the required `ELC_LPS_*`
  shared parameters from the inventory.
- Click **Inject** → STING binds them (instance vs type per the inventory)
  and saves.

**Option B — the whole library in one go:**
- Save every authored `.rfa` into its tier folder under `Families/LPS/`.
- Ribbon → STING Tools → Temp → **Batch Family Parameter Stamper**.
- Point it at `Families/LPS/`. It walks every `.rfa` recursively, opens
  each, injects the parameters, saves, and closes. (Allow ~15 s per family;
  Revit looks frozen during the run — don't switch documents.)

The parameters injected (per the inventory) are the `ELC_LPS_*` family of
~24 parameters plus the STING tag tokens (`ASS_*`, `ELC_TAG_7_PARA_LPS_TXT`).
Key ones:

| Parameter | Meaning |
|---|---|
| `ELC_LPS_ELEMENT_TYPE_TXT` | What this element is (AIR_TERMINAL / DOWN_CONDUCTOR / EARTH_ELECTRODE / BONDING_BAR / BOND / SPD / TEST_CLAMP) |
| `ELC_LPS_CLASS_TXT` | Protection class I/II/III/IV |
| `ELC_LPS_CONDUCTOR_MATERIAL_TXT` / `ELC_LPS_CONDUCTOR_CROSS_SECT_MM2` | Conductor metal + cross-section |
| `ELC_LPS_EARTH_TYPE_TXT` / `ELC_LPS_EARTH_RESISTANCE_OHM` | Type A/B earthing + measured ohms |
| `ELC_LPS_SEPARATION_DISTANCE_MM` | Computed side-flash separation |
| `ELC_LPS_BOND_TYPE_TXT` / `ELC_LPS_FROM_LPZ_TXT` / `ELC_LPS_TO_LPZ_TXT` | Bonding details |
| `ELC_LPS_SURGE_PROTECTION_LVL_TXT` | SPD Type 1/2/3/Combined |
| `ELC_LPS_TEST_DATE_TXT` / `ELC_LPS_CERT_REF_TXT` / `ELC_LPS_COMPLIANCE_STATUS_TXT` | Commissioning + audit trail |

### 7.5 Folder layout (where authored files go)

```
Families/LPS/
├── 1_AirTermination/   (Franklin, MeshTape, MeshNode, ESE, Mast)
├── 2_DownConductors/   (Bare, Concealed, TestClamp, RoofPenetration)
├── 3_Earth/            (EarthRod, EarthPlate, RingEarth, FoundationEarth, MainEarthBar, EarthPit)
├── 4_Bonding/          (BondingBar, BondingStrap, SparkGap)
└── 5_SPD/              (SPD_Type1, Type2, Type3, Combined)
```

STING finds them automatically — `Load model` and the checks search every
`.rfa` recursively; no registration needed once they're in the right folder.

### 7.6 SPDs need an electrical connector

SPDs are the one family that needs a **connector** so the panel-schedule
and voltage-drop engines can trace power through them:
- Family Editor → **Create → Electrical Connector**, place on the back face.
- Set **System** = Power - Balanced (Type 1/2/Combined) or Power - Other
  (single-phase Type 3), **Voltage** = 400/230 V or 230 V, **Poles** = 4
  (3+N) or 3 (L+N+PE), **Apparent Load** = 0 VA (passive).

### 7.7 Don't want to author from scratch?

Download a vendor family (DEHN, OBO, nVent ERICO, Furse/ABB — all publish
BIM libraries), drop it in the right tier folder, and run the **Batch
Family Parameter Stamper**. STING overlays its parameters without touching
the vendor's geometry. This is the fastest route.

### 7.8 Conformance check (before you deploy)

Run STING Tools → Tags → **Family Conformance Check** on the
`Families/LPS/` folder. It scores each family /100 against the STING
contract (shared params bound by GUID, subcategory names, connectors on
SPDs, loads cleanly). **PASS ≥ 85** = production-ready; it writes a CSV to
`<project>/_BIM_COORD/`.

### 7.9 Suggested authoring order & minimum set

Author the simple ones first to learn the workflow: Franklin Rod → Test
Clamp → Earth Rod → Main Earth Bar → Bare Down Conductor → SPD Type 1
(then duplicate-and-edit for 2/3/Combined) → Mesh Tape + Node → the rest.

**Minimum viable set for a Class II project (8 families):** Franklin Rod,
Bare Down Conductor, Test Clamp, Earth Rod, Main Earth Bar, Bonding Strap,
SPD Type 1, SPD Type 2.

### 7.10 Common authoring pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Wrong template | Element won't show on the LPS panel | Re-create from the inventory's template |
| Type parameter mis-named | Formulas fail / panel can't read values | Match the inventory name exactly (case-sensitive) |
| Forgot a subcategory | Element draws in the default colour | Add the subcategory, assign the solid to it |
| Shared param injected before type params exist | Schedule columns empty / formula errors | Run Family Parameter Creator *after* adding type params |
| Category = Generic Annotation | Element invisible in 3D | Change category to Electrical Equipment / Generic Models |
| Line family missing Reference Line | Geometry won't follow the host line | Create a Reference Line and lock the sweep to it |
| SPD missing connector | Panel schedule / voltage drop can't trace it | Add the electrical connector (7.6) |

---

## 8. Parameter & data-file reference

| File | What it controls |
|---|---|
| `Data/LPS/STING_LPS_CLASSES.json` | Per-class rolling-sphere radius, mesh size, down-conductor spacing, min cross-sections, ki factor, earth target, inspection interval, protection efficiency |
| `Data/LPS/STING_LPS_RISK_FACTORS.json` | Building/content/occupant/consequence weightings, loss-type tolerable risks, service factors |
| `Data/LPS/STING_LPS_RISK_TABLES.json` | The BS EN 62305-2 probability (Annex B) + loss (Annex C) tables the component risk model uses |
| `Data/LPS/STING_LPS_FLASH_DENSITY.json` | Ground flash density Ng by region |
| `Data/LPS/STING_LPS_SPD_CATALOGUE.json` | SPD product reference data |
| `Data/STING_LPS_SLD_RULES.json` | Single-line-diagram drawing rules for LPS |
| `Families/LPS/LPS_FAMILY_INVENTORY.json` | The 20-family spec (templates, subcategories, type params, formulas, shared params, geometry notes) |
| `Data/WORKFLOW_LpsCommissioning.json` | The commissioning workflow steps |

To change a class threshold (e.g. a revised minimum cross-section), edit
the JSON and re-run the relevant command — no recompile needed.

---

## 9. Troubleshooting / FAQ

- **"The LPS panel is empty."** Click **Load model** (or run a check) — the
  grids fill from the model. If still empty, the elements may not be
  *typed* — run **Mark Types** first.
- **"Everything reads exposed in 3D Coverage."** It's an air-terminal-only
  model; low/sparse rods leave gaps. Check the protection class is set, and
  remember roof mesh isn't credited. Verify on a single mast.
- **"Earth Check says 'no reading.'"** Earth resistance is a *measured* site
  value — type the megger reading into `ELC_LPS_EARTH_RESISTANCE_OHM` on
  each electrode (or via the EARTH tab grid).
- **"Separation distances look huge."** They assume an air medium (km = 1.0)
  and the worst-case kc. Set the real medium and run **kc Recalculate** with
  the correct down-conductor count + ring/bonding flags.
- **"The risk assessment recommends SPDs but only Class IV."** That's
  correct and expected for an ordinary building with long overhead service
  lines — the surge risk (R2/R4) is driven by the lines, which SPDs fix,
  not by the air-termination class.
- **"Schedule columns are blank."** The families are missing the shared
  parameters — run the **Batch Family Parameter Stamper**.

---

## 10. Quick command index

| Function | Where | Section |
|---|---|---|
| Risk assessment / class + SPD recommendation | LPS Class Setup | 4 |
| Full compliance audit | Compliance Check | 5 |
| Down-conductor spacing / size | Down Conductors | 5 |
| Earth resistance | Earth Check | 5 |
| Side-flash separation | Sep Distance / kc Recalculate | 5 |
| Rolling-sphere coverage (3D) | 3D Coverage | 5 |
| Identify each element | Mark Types | 6 |
| Zone classification | Zone Tag Rooms / Colour Zones | 6 |
| Inventory of bonding | Bonding Inventory | 6 |
| Plan strike overlay | Plan Visualise | 6 |
| Inspection cycle | Inspection Schedule | 6 |
| Native Revit schedules | Create Schedules | 6 |
| One-screen status | Dashboard | 6 |
| Full BS EN 62305 report | LPS Full Report | 6 |
| SPD cascade coordination | SPD Coordination | 6 |
| Create the 20 family shells from templates | Create shells (⚡ LPS panel) | 7.1 |
| Inject family parameters (one) | Family Parameter Creator | 7.4 |
| Inject family parameters (batch) | Batch Family Parameter Stamper | 7.4 |
| Audit authored families | Family Conformance Check | 7.8 |
| Push to cloud | Sync to Planscape | 6 |

---

**Standard:** BS EN 62305 (2024) / IEC 62305-4 / IEC 61643-11
**Family spec:** `Families/LPS/LPS_FAMILY_INVENTORY.json`
**Authoring detail:** `Families/LPS/AUTHORING_GUIDE.md`
