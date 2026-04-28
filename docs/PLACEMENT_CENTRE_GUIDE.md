# The Placement Centre — A Plain-English Guide

> A walk-through for site engineers, architects, designers and BIM
> coordinators who want to drive the STING Placement Centre without
> reading any code.
>
> **Who this is for:** anyone who has used Revit but has not built it.
> **What it covers:** every button, every box, every checkbox in the
> Centre, plus the background you need to know *why* each one exists.
> **What it doesn't cover:** how the engine code is wired internally —
> see [`PLACEMENT_CENTRE_REVIEW.md`](PLACEMENT_CENTRE_REVIEW.md) and
> [`CHANGELOG.md`](CHANGELOG.md) for that.

---

## 0. The 30-second pitch

The Placement Centre tells Revit **where to put things**.

Tell it once: *"a light switch lives 300 mm from the door hinge,
1100 mm above the floor, on the hinge side"*. After that, every time
you point the Centre at a room (or a hundred rooms), it puts the
switch in the right place automatically — and it cites a UK
standard while it does it.

You can think of the Centre as a **recipe book**:

* **Rules** are the recipes (one per kind of fixture in one kind of
  room).
* The **engine** is the cook — it reads the recipes, looks at the
  rooms, and places real Revit families.
* The **history panel** is the receipt — it records exactly what was
  placed, by which rule, when, and lets you undo it in one click.

If you are the kind of person who has spent a Friday afternoon
manually placing 400 sockets, you will appreciate why this exists.


---

## 1. Why does the Placement Centre exist?

### The problem the Centre solves

In an average UK office fit-out, **a single floor** can need:

* ~200 wall sockets (Approved Doc M perimeter trunking, BS 7671
  worktop sockets, Cat-A floor boxes, AV / IT outlets at desks…).
* ~80 light switches (one or two per door, two-way pairs on stairs,
  rocker dimmers in conference rooms, key-switches in plant…).
* ~120 luminaires laid on a 2.4 m grid (BS EN 12464-1 office 500 lux).
* ~40 emergency luminaires every 8 m on escape routes (BS 5266-1).
* ~30 smoke detectors at 7.5 m radius (BS 5839-1).
* ~20 sprinklers at 4 m centres (BS EN 12845).
* ~40 air diffusers, 20 thermostats, 10 fire dampers…

That is **~600 placements per floor**. On a 6-storey building,
~3 600. Every single one is supposed to be in the right place per
the relevant British/European/ISO standard.

Doing this by hand is:

1. **Slow** — easily a week per floor, repeated every revision.
2. **Error-prone** — no human consistently places 3 600 sockets at
   exactly 600 mm centres, 300 mm AFF.
3. **Audit-painful** — when the M&E reviewer asks "*why is this
   socket here?*" the only answer in a manual model is "*because the
   modeller put it there*".

The Centre flips this:

* The placement is **codified** ("300 mm from door hinge, hinge side,
  1100 mm AFF, BS 8300-2 reach") not memorised.
* The same rules apply to **every project** without re-typing.
* Every placement carries a **provenance stamp** linking it back to
  the rule that produced it, so your auditor gets an immediate answer.

### How it relates to the rest of STING

| STING module | Job | How the Centre fits |
|---|---|---|
| **Tagging pipeline** | Names every element with an ISO 19650 8-segment tag (`M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`) | Centre can *optionally* call the tagger after each placement so fixtures land already tagged |
| **Routing engines** | Draw the duct / pipe / conduit between fixtures | Routing needs end-points; the Centre provides them |
| **Validators** | Check spacing, fill, slope, fire separation | Centre runs them automatically after a placement run |
| **COBie export** | Builds the FM hand-over spreadsheet | Centre seeds COBIE_COMPONENT_NAME / DESCRIPTION so the export comes out non-empty |
| **Generative Design** | Tries thousands of variants, picks the best | Centre exposes a "GD Study" button that re-runs the engine under perturbed weights |


---

## 2. The mental model

Open the Centre and you see four big areas:

```
┌───────────────────────────────────────────────────────────────┐
│  Toolbar  (Reload · Import · Save · Run · Preview · Validate) │
├──────────────┬────────────────────────────────────────────────┤
│              │                                                │
│   Rule       │   Rule editor                                  │
│   list       │   (Core, Geometry, Scoping, Density,           │
│   (left)     │   Dependencies, Standards, Notes,              │
│              │   Clearance, Family defaults, Run options,     │
│              │   History)                                     │
│              │                                                │
├──────────────┴────────────────────────────────────────────────┤
│  Status bar  (rule count · invalid · unsaved · project file)  │
└───────────────────────────────────────────────────────────────┘
```

The flow is always the same:

1. **Pick a rule on the left** — or click the `+` to make a new one.
2. **Edit it on the right** — the panels are grouped: *what* is being
   placed (Rule Core), *where* it goes (Geometry / Scoping), *how
   many* (Density / Linear), *which other rules it depends on*
   (Dependencies), and *what to record* (Standards, Notes).
3. **Click Run Placement** — the engine reads every rule, walks
   every room in scope, and places real Revit elements.
4. **Read the history** — every placement carries a stamp; the
   history panel groups them into hourly buckets you can undo.

### The three things that make the Centre powerful

1. **Rules are JSON, not code.** Edit them in the Centre, in any text
   editor, or generate them from a spreadsheet. They live on disk
   beside the .rvt so they travel with the model.
2. **Rules can depend on each other.** *"Place the WC. Then place the
   basin opposite it. Then place the grab rail to the right of the
   WC."* Drives a real-world placement sequence, not just one-shot
   rules.
3. **Rules quote a standard.** Every shipped rule cites a British /
   European / ISO clause. When the regulator asks "why?", the answer
   is one click away.


---

## 3. Background you need before the tour

A few terms get used over and over. Worth getting these straight before
the button-by-button tour.

### 3.1 What is a "Family" / "FamilySymbol" / "FamilyInstance" in Revit?

* **Family** — a `.rfa` file. Think *brand*: e.g. "Schneider Twin Switched Socket".
* **Family Symbol** — a *variant* inside a family. Same `.rfa`, different parameters: `FLUSH`, `SURFACE`, `IP65`, `EM` (emergency-rated). Sometimes called a *Type*.
* **Family Instance** — a copy you actually placed in the model. The thing the surveyor sees on a wall.

The Centre's job is: *given a rule, decide which Family + which Symbol + at which XYZ to make a new Instance*.

### 3.2 What is an "Anchor"?

An anchor is the **reference point** the rule measures from. Think of it as the answer to the question *"X mm from what?"*.

| Anchor | Plain English | Used for |
|---|---|---|
| `ROOM_CENTRE` | The middle of the room | Air diffusers, ceiling fans |
| `CEILING_CENTRE` | Same X/Y as room centre, but on the ceiling | Smoke detectors, downlights |
| `WALL_MIDPOINT` | Half-way along each wall | Wall sockets, TV outlets |
| `WALL_CORNER` | The corner where two walls meet | WC pans, plant-room equipment |
| `DOOR_HINGE` | The hinge side of a door | Light switches (Approved Doc M) |
| `DOOR_JAMB` | The latch / handle side of a door | Access-control readers, MCPs |
| `DOOR_HEAD` | Above the door, at lintel height | Exit signs (BS 5266-1) |
| `WINDOW_SILL` | The bottom of a window | Trickle vents, radiators |
| `LIGHTING_GRID` | A computed BS EN 12464-1 lux grid | Office luminaires |
| `OPPOSITE_WALL` | The wall furthest from the first door | Thermostats, presentation screens |
| `GRID_INTERSECTION` | The nearest structural grid intersection | Plant-room equipment |
| `COLUMN_FACE` | Beside the nearest structural column | Local isolators |
| `PERIMETER_OFFSET` | Spaced along every wall in the room | Continuous trunking sockets |
| `RAISED_FLOOR_TILE` | The nearest 600 mm raised-access tile centre | Floor boxes |
| `STAIR_NOSING` | The edge of every stair tread | Photoluminescent edge strips |
| `ESCAPE_ROUTE_CENTRELINE` | Down the middle of every escape corridor | Emergency luminaires |
| `RELATIVE_TO` | The XYZ of a *previous* rule's last placement | Co-located fixtures (data + power) |
| `EQUIPMENT_PAIR` | Same as `RELATIVE_TO`; reads better in pairs | TV-outlet next to data outlet |

### 3.3 What does "MountingHeightMm" measure from?

By default, **above the finished floor (FFL)**. So `MountingHeightMm = 1100` means *1.1 m above the floor*.

If you're placing something below the ceiling — a smoke detector, a downlight — you'd rather measure *down from the ceiling*. The new `MountingReference` field lets you say so:

| MountingReference | Means |
|---|---|
| `FFL` *(default)* | Above the finished floor |
| `SLAB` | Above the structural slab (≈ FFL minus screed) |
| `CEILING` | Below the suspended ceiling line |
| `SOFFIT` | Below the structural soffit (above any ceiling) |


### 3.4 What is a "Room Filter (regex)"?

A room filter is a tiny pattern that says *"only run this rule in rooms whose name matches this"*.

`regex` ("regular expression") is a notation for *"these characters in this order"*. Two pieces of regex you'll see in every rule:

* `(?i)` at the start = "ignore case" — `OFFICE`, `office`, `Office` all match.
* `|` between words = "or" — `(?i)bathroom|wc|toilet|shower` matches any room whose name contains *bathroom*, *WC*, *toilet* or *shower*.

You don't need to learn regex to use the Centre — every shipped rule already has a sensible filter. But knowing what those characters mean stops them looking scary.

### 3.5 What is a "Variant Hint"?

A variant hint says *"prefer this kind of FamilySymbol"*. The Centre's symbol resolver walks every Family in the project that's in the rule's category, reads each Symbol's `STING_FIXTURE_VARIANT_TXT` parameter, and picks the first match.

You can chain hints with commas — `FLUSH,SURFACE,RECESSED` means *"prefer FLUSH; if no FLUSH then SURFACE; if neither then RECESSED"*. Useful when a project has a manufacturer's family with FLUSH only and a generic family with SURFACE only — the Centre tries the preferred one first.

You can also give a regex hint — `^IP6[5-7]$` means *"any IP rating between IP65 and IP67"* (useful for outdoor kit).

### 3.6 What is a "Standard reference" and why do rules cite one?

Every shipped rule has a `StandardRef` field — for example `BS 5266-1` for emergency lighting. These are the published British / European / ISO documents that say *the legal minimum is X*.

Citing the standard does three things:

1. **Audit trail** — when an inspector challenges a placement, the rule already names the clause.
2. **Liability** — if a designer overrides the rule, they're consciously departing from the cited code.
3. **Search** — the Centre's results panel surfaces the citation, so an "Errors" list reads *"M-LTG-EM-0034 missed BS 5266-1 max 8 m spacing"* rather than *"some light is far from another light"*.

Common ones you'll see:

| Code | Topic |
|---|---|
| **BS 7671** | UK electrical wiring regulations (sockets, isolators, DBs) |
| **BS 5266-1** | Emergency lighting — escape route luminance |
| **BS 5839-1** | Fire detection and alarm — detector spacing, MCP travel |
| **BS 5306-8** | Fire extinguisher placement |
| **BS EN 12464-1** | Workplace lighting — target lux per task |
| **BS EN 12845** | Sprinkler systems — head spacing |
| **BS 6465** | Sanitary installations — WC, basin, urinal counts |
| **BS 8300-2** | Accessible buildings — reach ranges, manoeuvring space |
| **Approved Doc M** | Access to and use of buildings (UK Building Regs) |
| **Approved Doc F** | Ventilation rates |
| **Approved Doc K** | Protection from falling, stair geometry |
| **HTM 02-01 / 06 / 08-03** | NHS technical memoranda — bedhead trunking, oxygen, nurse call |
| **HBN 04-01** | Hospital wards — clearances around beds |
| **BB103** | UK schools — area allowances, classroom layout |
| **BCO 2019** | UK office space planning — desk pitch, occupancy |
| **CIBSE Guide B1/B4** | Building services — diffuser spacing, luminaire clearance |

### 3.7 What is "provenance" and why does it matter?

Every time the engine places a fixture, it stamps that fixture with a tiny invisible record:

```
Engine: FixturePlacementEngine
Rule:   elec-light-emergency-route
When:   2026-04-25 14:30 UTC
Operator: STING-User-becky
```

This stamp is invisible in normal Revit views — but the History panel reads it back and groups placements into "buckets". So you can:

* See what was placed in the last hour.
* Double-click a bucket to select all those elements in Revit.
* Click "Undo last run" to delete the whole bucket as one transaction.

Without provenance, the only way to undo an over-eager placement run is to manually pick out 600 fixtures from the project browser. With provenance, it's one click.


---

## 4. Tour of the toolbar

The toolbar runs across the top of the Centre and groups every action you take *on the rule set as a whole* (saving, importing, running, validating). Every button below has a keyboard shortcut shown in its tooltip.

### 4.1 `Reload Defaults`

**What it does:** throws away every rule currently in memory and reloads the shipped baseline (~100 rules from the four discipline packs).

**Why it exists:** if you have made a mess editing rules and you want to start over, click this. It does **not** touch the project's saved overrides on disk — those are loaded back next time you open the Centre. Only the in-memory edits are nuked.

**When to use it:** "I broke something; give me back the factory settings."

**Confirmation prompt:** if you have unsaved edits, the Centre warns you before discarding them.

### 4.2 `Import…`

**What it does:** opens a file picker and merges rules from another `.json` into the current set.

**Why it exists:** rule files are portable. If a colleague hands you a JSON with their school-fit-out rules, this is how you ingest them.

**Behaviour:** rules whose `MergeKey` already exists are *skipped silently* — so an import can never overwrite a rule you already have. You'll see a status message like *"Imported 12 rule(s); 3 skipped (already present)"*.

**When to use it:** sharing rule sets across teams; bootstrapping a sector-specific project.

### 4.3 `Export…`

**What it does:** writes every *valid* rule in the Centre out to a single JSON.

**Why it exists:** to share, version-control, or back up your rule set without affecting the project's `STING_PLACEMENT_RULES.project.json`.

**Note:** only rules that pass validation are exported. Invalid rules stay in memory so you can still fix them, but they're skipped to keep the exported file clean.

### 4.4 `Save Project`

**What it does:** writes the rule set to `<project>/STING_PLACEMENT_RULES.project.json` next to the `.rvt`.

**Why it exists:** project overrides win over the shipped baseline. So saving a rule with `RuleId = elec-corridor-light` overrides any baseline rule with the same id.

**When you'll use it:** every time you change a rule. Centre highlights unsaved rules with a `●` dot in the first column of the rule grid; the status bar reports an unsaved count.

**Keyboard shortcut:** Ctrl+S.

### 4.5 `Run Placement` *(Alt+R)*

**What it does:** for every room in scope, the engine iterates every rule (in priority order), generates candidate XYZs, scores them against collision and spacing rules, and creates real `FamilyInstance` objects in a single Revit transaction.

**Why it exists:** this is the actual point of the tool — turning rules into placed elements.

**What you get back:** a `StingResultPanel` summary with totals (rooms visited, candidates evaluated, placed, skipped, warnings). The placed elements are also selected automatically in Revit so you can immediately see them.

**Important — undoability:** the entire run is wrapped in a single Revit `TransactionGroup`, so a single Ctrl+Z reverts the whole batch. You can also use the Centre's "Undo last run" button (see 4.7).


### 4.6 `Preview` *(Alt+P)*

**What it does:** runs the engine in *dry-run* mode and paints a coloured 3D overlay (DirectContext3D) showing where every candidate XYZ would land — without touching the model.

**Why it exists:** before you commit to placing 600 fixtures, see them on the canvas. Each rule gets its own colour (a stable HSV-derived hue), so you can immediately tell *"those red dots are the smoke detectors, those blue ones are the data outlets, those green ones are the sockets."*

**Tip:** turn on the "Live preview while editing rules" checkbox in Run Options. The preview then auto-refreshes 500 ms after every rule edit, so you can see your changes land in real time.

**To clear the preview:** press Esc. The DirectContext3D overlay is purely visual, so clearing it never affects the model.

### 4.7 `Validate` *(Alt+V)*

**What it does:** runs the validators picked in Run Options against the document (or the elements just placed) and surfaces the findings in a result panel.

**Why it exists:** validators answer the *"is this BIM model legal?"* question:

| Validator | Checks |
|---|---|
| **Clearance** | Are placed fixtures too close to one another, or to walls / doors? |
| **Maintenance** | Can a service technician physically reach every component? |
| **Connectivity** | Are pipe / duct / conduit endpoints joined to a system? |
| **Fill** | Are conduits / cable trays filled within their published capacity? |
| **Spec** | Do family parameters match the project specification? |
| **Termination** | Are open ends of services capped or terminated? |
| **Slope** | Do drainage runs respect minimum gradients? |
| **Separation** | Are services kept apart per BS EN 50174-2 / HTM 02-01 etc? |

**Two scopes:**

* **Scoped to the last run** — only checks elements just placed. Runs after a `Run Placement` if "Run validators after placement" is on. Faster, more focused.
* **Project-wide** — clicking the Validate toolbar button alone runs every selected validator on the whole document.

### 4.8 `Undo last run`

**What it does:** deletes every element placed by the most recent run as one Revit transaction.

**Why it exists:** Revit's own Ctrl+Z is fragile in batch operations — sometimes the transaction group doesn't bunch correctly, or you've done other work since. The Centre's undo reads the **provenance stamps** to find every element belonging to the last bucket and removes them in one go.

**Confirmation prompt:** *"Delete N element(s)?"* — read the count carefully before clicking Yes.

**What it can't undo:** parameter writes done by `Push to Families` (4.9). That uses a separate transaction; use Revit's regular Undo for those.

### 4.9 `Push to Families`

**What it does:** takes the *currently-selected rule's* values and writes them onto **every Family Type in that category** in the project.

**Why it exists:** the rule library is the *source of truth*, but family types in the project carry parameters like `PLACE_MOUNT_HEIGHT_MM` and `STING_FIXTURE_VARIANT_TXT`. Pushing the rule keeps the family-type parameters in sync, so other tools (schedules, COBie, generative design) read the same numbers.

**What gets written:** anchor type, side constraint, mounting height + reference, X/Y/Z offsets, min spacing, priority, max-per-room, the rule's standard reference, free-text notes — and (if you typed any in the Clearance / Envelope / Weight group) directional clearances, weight, envelope dimensions, fire separation.

**Confirmation prompt:** lists every parameter it's about to touch.

### 4.10 `Heat-map`

**What it does:** paints an Analysis Visualization Framework (AVF) compliance heat-map onto the active view — green where rules placed everything they were supposed to, red where rooms have unmet rules.

**Why it exists:** for managers and reviewers who want a single page that says *"these areas are done, these aren't"*. The heat-map is computed live; it doesn't add geometry to the model.

**Tip:** turn on "Auto-paint AVF compliance heat-map on commit" in Run Options. The heat-map refreshes after every Run Placement.

### 4.11 `Save view preset`

**What it does:** stamps the active view's scope / template / scale / phase as a named preset (Pack 125/M `StingViewPresetSchema`) so it travels with the model.

**Why it exists:** placement runs depend on what the active view shows. If a colleague opens the project and runs the Centre against an arbitrary view, they may get a different result. View presets fix that — the preset name reads `PlacementCentre/<scope>/<timestamp>`.

**When to use it:** at the end of a successful placement run that you want to be reproducible later.

### 4.12 `GD Study`

**What it does:** opens a TaskDialog explaining how to launch the bundled Generative Design study. The `.dyn` file ships at `Data/GenerativeDesign/STING_FIXTURE_PLACEMENT.dyn`.

**Why it exists:** generative design searches *thousands* of placement variants (perturbing min-spacing, priority, etc.) and produces a Pareto front of *coverage vs spacing-variance vs clearance-violations*. Useful when you don't yet know the right rule weights.

**How to actually run it:** open Revit's *Manage › Generative Design › Create Study*, pick the STING study from the list, tune `SpacingBias` / `CoverageTarget` / `ClearancePenalty`, and click Run. The study reuses the in-memory rule set the Centre is editing.

### 4.13 `Close`

Closes the Centre. Your unsaved edits are kept in memory **and lost when Revit shuts down** — make sure to click Save Project first.


---

## 5. The rule grid (left rail)

The grid runs down the left side of the Centre. Every row is one rule.

### 5.1 Search box

Filters the grid by *category*, *variant hint*, *room filter* or *notes*. Type *"door"* and only rules mentioning doors stay visible. Clear the box to see everything again.

### 5.2 The chips: `● Dirty` and `✗ Invalid`

Two toggle buttons just below the search box.

* **`● Dirty`** — show only rules with unsaved edits. Useful when you've made a dozen changes and want to remind yourself which ones still need saving.
* **`✗ Invalid`** — show only rules that fail validation. The Centre revalidates every rule whenever you edit it, so a chip with rules in it is your "errors to fix" list.

Toggling both chips off (the default) shows every rule.

### 5.3 The grid columns

| Column | Meaning |
|---|---|
| `●` | Dirty marker — an orange dot means the rule has unsaved edits. |
| `Category` | Revit category name (e.g. `Lighting Devices`). |
| `Variant` | The variant hint (or blank). |
| `Room` | The room-filter regex (or blank for "all rooms"). |
| `Anchor` | The anchor type (e.g. `WALL_MIDPOINT`, `DOOR_HINGE`). |
| `Pri` | Priority 0-100. Higher wins room capacity slots first. |

### 5.4 Add `+` and remove `-` buttons

`+` creates a new blank rule and selects it on the right. The new rule is flagged dirty and named `(new category)` — you must fill in a real CategoryFilter before saving.

`-` deletes the currently-selected rule from memory (with a confirmation prompt). Save Project to make the deletion permanent.

### 5.5 Right-click context menu

Right-click any row (or multi-select with Ctrl/Shift first):

* **Clone selected** — duplicate the picked rules; the copies are flagged dirty and renamed *"… (copy)"*.
* **Delete selected** — remove the picked rules with one prompt.
* **Select invalid** — replaces the current selection with every invalid rule. Combine with `Delete selected` to clean house in one go.
* **Select dirty** — same idea for unsaved rules.


---

## 6. The rule editor (right side) — group by group

Every group below is a `<GroupBox>` on the right pane. They describe the same rule, just split into thematic chunks so the editor doesn't become an unreadable wall of fields.

### 6.1 Rule Core

The minimum every rule needs to exist.

| Field | What it means | Why it matters |
|---|---|---|
| **Category Filter** | Revit category name, exactly as it appears in `Element.Category.Name` (`Lighting Devices`, `Plumbing Fixtures`, …) | The engine filters rules by category before scoring — a rule in `Doors` will never produce a placement for `Windows`. The Centre validates this against your live document and flags unknown categories `✗ Invalid`. |
| **Variant Hint** | Comma-separated fallback chain or regex (`FLUSH,SURFACE,RECESSED`, `^IP6[5-7]$`). | Picks which family-type variant the engine prefers when it has a choice. |
| **Room Filter (regex)** | Case-insensitive regex matched against the room name. Empty = match every room. | First gate the engine applies — if the regex doesn't match, the rule never runs for that room. |
| **Anchor** | Pick from the dropdown (see §3.2). | Decides *what* the offsets are measured from. Defaults to `ROOM_CENTRE`. |
| **Side** | `EITHER`, `LEFT`, `RIGHT`, `FRONT`, `BACK`, `HINGE_SIDE`, `LATCH_SIDE`. | For door anchors, picks which side of the door the fixture lands on (Approved Doc M switch placement is `HINGE_SIDE`). For wall anchors, scoring softly prefers the picked side. `EITHER` = any. |
| **Priority (0–100)** | Number; higher = more important. | When two rules compete for a room's `MaxPerRoom` slots, the higher-priority rule wins. Ties broken by the candidate score. |

### 6.2 Geometry

The *exactly where* details. Every dimension is in **millimetres**.

| Field | What it means | Typical values |
|---|---|---|
| **Offset X (mm)** | Signed offset along the anchor's local +X. For a wall anchor, that's *along the wall*. | `0` for centred; `300` for "300 mm right of the door hinge". |
| **Offset Y (mm)** | Signed offset along the anchor's local +Y (perpendicular to the wall, *into* the room). | `0` for "on the wall"; `200` for "200 mm into the room from the wall face". |
| **Offset Z (mm)** | Extra Z offset on top of MountingHeight. Useful for sloped ceilings or tilt rigs. | `0` most of the time. |
| **Mount Height (mm)** | Height above the chosen MountingReference. | `300` for sockets; `1100` for switches; `1400` for MCPs; `2200` for exit signs. |
| **Mount Reference** | What height is measured from: `FFL` (default), `SLAB`, `CEILING`, `SOFFIT`. | `FFL` for wall fixtures; `CEILING` for downlights so you can say "100 mm below ceiling". |
| **Rotation (°)** | Spin about Z. | `0` most of the time; non-zero for angled CCTV brackets. |
| **Min Spacing (mm)** | Minimum centre-to-centre distance between two fixtures placed by *this* rule in the *same* room. | `1500` for desks; `8000` for emergency luminaires; `10500` for smoke detectors per BS 5839-1. |
| **Max per Room (0 = ∞)** | Hard cap. `0` means "no limit". | `1` for thermostats; `2` for two-way switch pairs; `0` for "as many as the room takes". |

### 6.3 Room Scoping (PC-07)

Lets the rule narrow down the rooms it runs in beyond the basic Room Filter.

| Field | What it means | Example |
|---|---|---|
| **Exclude room (regex)** | Skip rooms matching this regex even if Room Filter matched. | `(?i)plant\|riser` to exclude plant rooms from a generic office rule. |
| **Department (regex)** | Match against the Room's Department parameter. | `(?i)sanitary` to limit a rule to sanitary departments. |
| **Level (regex)** | Match against the Room's Level name. | `L0[1-3]` to limit to levels 01-03. |
| **Phase (regex)** | Match against the Room's CreatedPhaseId.Name. | `New Construction` to exclude existing rooms. |
| **Workset (regex)** | Match against the Room's Workset name. | `Electrical` if the rule should only run on electrical-discipline rooms. |
| **Min/Max Area (m²)** | Skip rooms whose area is outside this range. | Set Min Area = 6 to skip cupboards smaller than a desk pod. |

All filters AND together — a rule with both a Level filter and a Department filter only fires when both match.

### 6.4 Rule Kind / Density / Linear (PC-12)

Tells the engine **how many** to place per room.

| Kind | Plain English | Count formula |
|---|---|---|
| **Point** *(default)* | One placement at the anchor (or at every candidate the anchor produces). | Capped by Max per Room. |
| **Density** | One per X m², or one per X occupants. | `count = ceil(area_m² ÷ PerAreaM2)` *or* `ceil(occupants ÷ PerOccupant)` — whichever is bigger. |
| **Linear** | One every X metres of room perimeter. | Engine samples the boundary at `PerLinearMetre` step. Pair with the `PERIMETER_OFFSET` anchor. |

| Field | When you fill it |
|---|---|
| **Per area (m²)** | Density rules: "1 socket per 10 m²" → set to 10. |
| **Per occupant** | Density rules: "1 WC per 15 occupants" → set to 15. The engine reads `STING_OCC_COUNT_INT` from the room. |
| **Per linear m** | Linear rules: "1 socket per 3 m of wall" → set to 3. |

`Max per Room` (in the Geometry group) still applies as a hard cap regardless of kind, so you can say "1 per 10 m² *but never more than 8*".

### 6.5 Rule Dependencies (PC-13)

Lets rules **depend on other rules**. This is what turns the Centre from "place each thing in isolation" into "place things in the right order".

| Field | What it means | Example |
|---|---|---|
| **Rule Id** | Stable identifier for this rule. Defaults to MergeKey when empty. | `elec-wc-shaver-01` |
| **Depends on** | Rule Id of a predecessor; this rule fires only after that one placed ≥1 instance in the same room. | `elec-wc-basin-01` |
| **Relative to** | When DependsOn set, anchor relative to which placement of the predecessor: `previous`, `first`, `self`. | `previous` is most common — "use the predecessor's last point". |
| **Co-place** | Comma-separated Rule Ids to fire alongside this rule at the same XYZ. | `data-wc-01` to fire a data-outlet rule next to every primary placement. |
| **Conflicts with** | Comma-separated Rule Ids whose presence in the same room *suppresses* this rule. | `motion-pir-room` so a CCTV rule doesn't fire in rooms that already got a PIR. |

**A worked dependency chain:**

```
elec-wc-pan-01     → places the WC pan first
  ├─ DependsOn=null
elec-wc-basin-01   → places the basin opposite
  ├─ DependsOn=elec-wc-pan-01
  ├─ Anchor=OPPOSITE_WALL
elec-wc-grab-rail  → grab rail to the right of the WC
  ├─ DependsOn=elec-wc-pan-01
  ├─ Anchor=RELATIVE_TO  (relative to the pan's last point)
  ├─ OffsetXMm=400
  ├─ Side=RIGHT
```

Without dependencies, you'd model these as three independent rules and hope the rooms happen to be wired the same way every time. With dependencies, the engine guarantees the basin lands opposite the pan and the rail lands beside the pan, regardless of room shape.


### 6.6 Standards & Classification

Two short fields, big audit value.

| Field | What it means |
|---|---|
| **Standard ref** | Free-text citation surfaced in result panels (`BS 7671 §701`, `Approved Doc M`, `HTM 08-03`). |
| **Uniclass Pr** | Optional Uniclass 2015 product code (`Pr_70_70_05_84`). Used by the COBie export to group fixtures by classification. |

Filling these in is what makes a rule auditable. A reviewer reading the result panel sees not just *"M-LTG-EM-0034"* but *"M-LTG-EM-0034 (BS 5266-1, max 8 m spacing)"*.

### 6.7 Notes

Free-text. Goes onto every Family Type's `STING_PLACEMENT_NOTES_TXT` parameter when you click `Push to Families`. Treat it like a comment: *why* this rule exists, *who* approved the deviation, *what* to remember next revision.

### 6.8 Clearance / Envelope / Weight (PC-11, push to family types)

This group is **not** part of the rule itself — the values you type here are used **only** when you click `Push to Families`. They write directional clearance, envelope dimensions, weight and fire separation onto every Family Type in the rule's category.

| Field | Writes to | What's it for |
|---|---|---|
| Clearance (mm) | `STING_CLEARANCE_MM` | Omnidirectional working clearance. |
| Front / Back (mm) | `STING_CLEARANCE_FRONT_MM` / `_BACK_MM` | Service-access clearance for things with a front (e.g. AHUs). |
| Side / Top (mm) | `STING_CLEARANCE_SIDE_MM` / `_TOP_MM` | Side-bend / overhead clearance. |
| Weight (kg) | `PLACE_WEIGHT_KG` | Drives structural pre-flight (a 200 kg AHU can't sit on a 50 kg/m² ceiling tile). |
| W × D × H (mm) | `MNT_ENV_W_MM` / `_D_MM` / `_H_MM` | Mounting envelope used by clash detection and routing. |
| Fire separation (mm) | `FIRE_SEP_MM` | Distance to keep from fire-rated surfaces. |

Empty fields are left untouched on the family — so you can update one number without touching the rest.

### 6.9 Family Defaults & Clearance (read-only)

A diagnostic grid. Click the `Inspect` button at the top of this group; the Centre walks every Family Type in the rule's category and lists the current value of each of the placement / clearance parameters. The `Source` column shows whether the value lives on the Type or on a sample Instance.

**Why it matters:** before you push new clearance numbers, you might want to check what's already there.


### 6.10 Run Options

Settings that govern *how* the next `Run Placement` behaves. They apply to *every* rule, not just the selected one.

| Checkbox / option | What it does |
|---|---|
| **Stamp provenance on each placement (Pack 123/E)** | Default ON. Writes the invisible "who placed me, with which rule, when" stamp on every placed instance. Turn off only for one-off placements you don't want to track. |
| **Honour learned offsets per category (Pack 10 / PC-14)** | Default ON. When `STING_PLACEMENT_RULES.learned.json` exists next to the .rvt (produced by `LearnPlacementV4Command`), its rules win over the shipped baseline. |
| **Run validators after placement** | Default ON. Immediately after a successful run, fires the validators picked in 6.11 — scoped to the elements just placed (faster than a project-wide audit). |
| **Auto-paint AVF compliance heat-map on commit** | Default OFF. When ON, the heat-map (4.10) refreshes immediately after each run. |
| **Run data-tag pipeline on each placement (PC-17)** | Default OFF. Calls `TagPipelineHelper.RunFullPipeline` on every placed instance so it lands ISO 19650 tagged. Slows large runs but produces a fully tagged model in one pass. |
| **Seed COBie component fields from the rule (PC-17)** | Default OFF. Copies StandardRef, Notes, UniclassPr onto each placed instance's `COBIE_COMPONENT_*` parameters so the COBie export comes out non-empty. |
| **Probe MEP connectors after placement (PC-17)** | Default OFF. Logs any unconnected connectors so MEPSystemBuilder can pick them up later. |
| **Live preview while editing rules (PC-21)** | Default OFF. When ON, the Centre re-runs Preview 500 ms after every rule edit so the canvas tracks your changes in real time. |
| **Scope:** Active view / Selection / Project | Picks which rooms the engine considers. Active view = the rooms visible in the current Revit view; Selection = whatever you've picked in Revit; Project = every room in the model. |

### 6.11 Validators (PC-23)

Below the Scope row, a panel of eight checkboxes lets you pick which validators run when you hit `Validate` (or when *Run validators after placement* is on).

| Validator | Use it for |
|---|---|
| **Clearance** *(default)* | Working / service clearances. |
| **Maintenance** *(default)* | Reach for a service tech. |
| **Connectivity** | Pipe / duct / conduit endpoints joined to a system. |
| **Fill** | Cable tray fill within published capacity. |
| **Spec** | Family parameters match project specification. |
| **Termination** | Open service ends capped or terminated. |
| **Slope** | Drainage gradients above the minimum. |
| **Separation** | Service-to-service separation per BS EN 50174-2 / HTM 02-01. |

Pick fewer validators for a faster check; pick more for a comprehensive audit.

### 6.12 History & Provenance

A grid showing the last 30 hourly buckets of placement runs in the project, newest first.

| Column | Means |
|---|---|
| When (UTC) | The hour of the run, rounded down. |
| Engine | `FixturePlacementEngine` / `FixturePlacementEngine.CoPlace` etc. |
| Rule | The rule's MergeKey / Rule Id. |
| Operator | The Windows user who ran the engine. |
| Count | How many instances landed in that bucket. |

**Double-click any row** to select all that bucket's elements in Revit (useful when "did I really mean to place those?" comes up).

**Refresh** button (or F5) re-reads provenance from the model — useful if you've placed elements outside the Centre and want them to appear in the grid.

### 6.13 The status bar (very bottom)

`43 rule(s) · 14 categor(ies) · 1 unsaved · 1 invalid · MyProject.rvt`

A glance tells you:

* How many rules total.
* How many distinct Revit categories they target.
* Whether you have unsaved edits.
* Whether anything's invalid.
* Which file's overrides will be saved when you click Save Project.


---

## 7. Worked walk-throughs

Five end-to-end scenarios. Read the one that matches what you want to do; ignore the rest.

### 7.1 *"Place emergency luminaires every 8 m down corridors per BS 5266-1"*

This rule already ships in `STING_PLACEMENT_RULES.electrical.json`. To run it:

1. Open a sheet or floor plan that **contains the corridor rooms**.
2. Open the Centre.
3. Find the rule `elec-light-emergency-route` in the left-rail grid (type *emergency* into the search box).
4. Verify the Geometry: `MountingHeightMm = 0` (luminaires are ceiling-mounted; height comes from the ceiling), `MinSpacingMm = 8000` (BS 5266-1 max 8 m), `RuleKind = Linear`, `PerLinearMetre = 8`.
5. Set Run Options scope = `Active view`.
6. Click `Preview`. You should see one teal cross every 8 m down each corridor.
7. Happy with the layout? Click `Run Placement`.
8. Read the result panel: *"Rooms visited: N · Placed: M · BS 5266-1 / BS EN 1838"*.

### 7.2 *"Add a row of perimeter sockets at 600 mm centres in offices"*

Slightly trickier — needs a Linear rule with `PERIMETER_OFFSET` anchor.

1. Centre → click `+`.
2. Set:
   * Category Filter: `Electrical Fixtures`
   * Variant Hint: `FLUSH,SURFACE` (try flush first, fall back to surface)
   * Room Filter (regex): `(?i)office|study|workstation`
   * Anchor: `PERIMETER_OFFSET`
   * Mount Reference: `FFL`
   * Mount Height (mm): `1100`
3. In the *Rule Kind* group, set Kind = `Linear` and `Per linear m` = `0.6`.
4. In *Standards & Classification*, set Standard Ref = `Approved Doc M / BS 7671`, Notes = `Continuous perimeter trunking sockets at 600 mm centres, 1.1 m AFF`.
5. Click `Save Project`.
6. Click `Preview` against the Active view to see the dotted line of sockets along every wall.
7. Click `Run Placement`.

### 7.3 *"WC pan, basin opposite, grab rail to its right"* (rule chain)

1. Centre → `+` to make rule 1:
   * Rule Id: `wc-pan-01`
   * Category: `Plumbing Fixtures`
   * Room Filter: `(?i)bathroom|wc|toilet`
   * Anchor: `WALL_CORNER`
   * Mount Height: `400`
   * Max per Room: `1`
2. `+` again for rule 2:
   * Rule Id: `wc-basin-01`
   * Category: `Plumbing Fixtures`
   * Variant Hint: `BASIN`
   * Room Filter: `(?i)bathroom|wc|toilet`
   * Anchor: `OPPOSITE_WALL`
   * Mount Height: `850`
   * **Depends on:** `wc-pan-01`
   * Max per Room: `1`
3. `+` again for rule 3:
   * Rule Id: `wc-grab-rail-01`
   * Category: `Generic Models`
   * Variant Hint: `GRAB,RAIL`
   * Room Filter: `(?i)accessible|disabled|ambulant`
   * Anchor: `RELATIVE_TO`
   * **Depends on:** `wc-pan-01`
   * Relative to: `previous`
   * Offset X (mm): `400`
   * Mount Height: `680`
   * Side: `RIGHT`
   * Max per Room: `1`
   * Standard Ref: `BS 8300-2 §17`
4. Save Project. Run Placement.
5. The engine runs them in dependency order: pan → basin (looks up the pan's last point) → rail (offsets from the pan).

### 7.4 *"Learn from existing placements"*

Useful when a project's already half-modelled and you want the Centre's rule library to match what's actually on site.

1. Make sure your model contains a representative sample of fixtures already placed in real rooms.
2. From the STING dock panel, click `Placement_Learn` (or run the Revit command `Placement_Learn` from `R-keytip-search`).
3. The command walks the model, clusters fixtures by `(Category, RoomKeyword)`, and writes `<project>/STING_PLACEMENT_RULES.learned.json` with Priority 90.
4. Open the Centre and click `Reload Defaults` (which now reads the learned file thanks to the `Honour learned offsets` toggle in Run Options).
5. Inspect the new high-priority rules. Edit / save to keep what's good.

### 7.5 *"Import a discipline pack from a colleague"*

1. Colleague hands you `MyHospitalPack.json` (a JSON in the same shape as `STING_PLACEMENT_RULES.json`).
2. Centre → `Import…` → pick the file.
3. New rules appear in the grid, all flagged dirty (orange dot in column 1).
4. Review them, edit anything that doesn't fit your project conventions.
5. Click `Save Project` to persist them to your project's `STING_PLACEMENT_RULES.project.json`.


---

## 8. Where the rules actually live on disk

When the Centre loads, it reads four *layers* in order. Each layer can override the one below.

```
Layer 4  <project>/STING_PLACEMENT_RULES.project.json     ← your project edits (highest priority)
Layer 3  <project>/STING_PLACEMENT_RULES.learned.json     ← LearnPlacementV4 output (Pri 90)
Layer 2  data/STING_PLACEMENT_RULES.architecture.json     ← shipped per-discipline packs
         data/STING_PLACEMENT_RULES.mechanical.json
         data/STING_PLACEMENT_RULES.electrical.json
         data/STING_PLACEMENT_RULES.healthcare-education.json
Layer 1  data/STING_PLACEMENT_RULES.json                  ← shipped baseline (~43 rules)
```

* Rules with the same `RuleId` (or `MergeKey`) overwrite each other — last one wins.
* A rule that exists only in Layer 4 is project-specific.
* A rule in Layer 1 with the same id as one in Layer 4 means: the project has overridden that baseline rule.

When you click `Save Project`, the Centre writes only Layer 4. The other layers are read-only from the Centre's point of view; edit them with a text editor or a fresh build.

---

## 9. Glossary

| Term | Plain English |
|---|---|
| **AFF** | Above Finished Floor — the height datum used by Approved Doc M, BS 8300-2 etc. |
| **AVF** | Analysis Visualization Framework — Revit's heat-map overlay system. |
| **BIM** | Building Information Modelling. The 3D model + parameters that go to construction. |
| **BS** | British Standard. |
| **CDE** | Common Data Environment — the shared platform every party (architect, engineer, contractor) writes to. |
| **CIBSE** | Chartered Institution of Building Services Engineers — UK building-services guidance. |
| **COBie** | Construction-Operations Building Information Exchange — the FM hand-over spreadsheet (BS 1192-4). |
| **DALI** | Digital Addressable Lighting Interface — addressable lighting bus. |
| **EM** | Emergency-rated luminaire (with battery backup). |
| **FFL** | Finished Floor Level — top of the floor finish, the reference for most mounting heights. |
| **HBN / HTM** | Health Building Note / Health Technical Memorandum — NHS estates standards. |
| **IP65 / IP66 / IP67** | Ingress Protection rating — how dust- and water-tight a fixture is. |
| **IFP** | Interactive Flat Panel — the modern classroom whiteboard. |
| **MCP** | Manual Call Point — fire-alarm break-glass. |
| **MEP** | Mechanical, Electrical, Plumbing. |
| **PIR** | Passive Infra-Red — motion sensor. |
| **PoE** | Power-over-Ethernet — Cat 6 with power on it (used by IP cameras, APs). |
| **Provenance** | The invisible "who/why/when" stamp on a placed element. |
| **RCP** | Reflected Ceiling Plan — the view that shows fixtures hanging from the ceiling. |
| **Regex** | Regular expression — a pattern for matching text. |
| **Revit family** | A reusable component: a door, a desk, a luminaire. |
| **Uniclass 2015** | UK construction classification — every product / system / activity has a Uniclass code. |
| **VCD** | Volume Control Damper. |
| **VAV** | Variable Air Volume — air-conditioning zone control. |


---

## 10. Troubleshooting

### *"My rule says it's invalid but I can't see why."*

Look at the bottom of the right pane — the orange `txtRuleError` banner shows the exact reason. Common culprits:

* **`CategoryFilter` is required** — type a real Revit category name into the *Category Filter* combo.
* **Category 'Foo' is not present in the active document** — the Centre validates against `Document.Settings.Categories`. If your project doesn't have that category enabled, the rule can't run. Either change the category, or load a family that brings the category in.
* **AnchorType 'XYZ' is not recognised** — pick from the dropdown, don't type freehand.
* **RoomFilter regex: parsing "…" — Unterminated [] set** — your regex is malformed; remove the square bracket or escape it as `\[`.
* **Density rule needs PerAreaM2 > 0 or PerOccupant > 0** — when you switched RuleKind to Density, you need to fill at least one of the per-X fields.

### *"Run Placement says 'No FamilySymbol found for category X.'"*

Three options:

1. Load a family of that category into the project (Insert › Load Family).
2. Or drop a `.rfa` of the right category into `Families/<discipline>/`. The Centre's **PC-16 auto-load** will pick it up next run.
3. Or change the rule's `CategoryFilter` to a category the project already has.

### *"The Preview shows no candidates for my rule."*

* Is the room visible in the Active view? Switch Run Options scope to *Project* to test.
* Is the Room Filter regex too strict? Empty the *Room Filter* box and re-preview.
* Has another rule already eaten the room's Max-per-room slots? Look at *Conflicts with* and *Priority*.
* Is the room area below your *Min Area* threshold?

### *"Run Placement put fixtures in the wrong place."*

Run `Validate` immediately after — the validator findings will tell you whether it's a clearance violation, a min-spacing miss, or a wrong-anchor issue. From there:

* Adjust the rule's `OffsetX/Y/Z` to nudge the position.
* Switch the anchor (e.g. `WALL_MIDPOINT` → `WALL_CORNER`).
* Tighten the validators with PC-23's checkbox panel and re-run.

If all else fails, click `Undo last run`, edit the rule, and try again.

### *"Where can I find the actual code that runs?"*

| Concern | Code path |
|---|---|
| The Centre window | `StingTools/UI/PlacementCenter/StingPlacementCenter.xaml(.cs)` |
| The rule POCO | `StingTools/Core/Placement/PlacementRule.cs` |
| The placement engine | `StingTools/Core/Placement/FixturePlacementEngine.cs` |
| Anchor-point generation | `StingTools/Core/Placement/PlacementScorer.cs` |
| Loader / disk layers | `StingTools/Core/Placement/PlacementRuleLoader.cs` |
| The shipped baseline + packs | `StingTools/Data/Placement/STING_PLACEMENT_RULES*.json` |
| The schema | `StingTools/Data/Schemas/STING_PLACEMENT_RULES.schema.json` |
| Learn pass | `StingTools/Commands/Placement/LearnPlacementV4Command.cs` |
| Generative-design bridge | `StingTools/Core/Placement/GenerativeDesignBridge.cs` |

The architecture-level review lives in [`PLACEMENT_CENTRE_REVIEW.md`](PLACEMENT_CENTRE_REVIEW.md); the change history in [`CHANGELOG.md`](CHANGELOG.md) (Phase 128).

---

## 11. Cheat-sheet

| Want to… | Click |
|---|---|
| Make a new rule | Left-rail `+` button |
| Delete a rule | Left-rail `-` button (or right-click `Delete selected`) |
| See where rules would land *before* placing | Toolbar `Preview` (Alt+P) |
| Place fixtures into the model | Toolbar `Run Placement` (Alt+R) |
| Undo what you just placed | Toolbar `Undo last run` |
| Audit the model against codes | Toolbar `Validate` (Alt+V) |
| Ship the rule values to family types | Toolbar `Push to Families` |
| Persist your edits | Toolbar `Save Project` (Ctrl+S) |
| Bring rules from another project | Toolbar `Import…` |
| Throw away unsaved edits | Toolbar `Reload Defaults` |
| Wipe the canvas overlay | Esc |
| Refresh history | F5 |

---

*End of guide. If something here was unclear, file an issue against the
`docs/PLACEMENT_CENTRE_GUIDE.md` file — the language can always improve.*

---

## 12. Phase 139 — Placement Centre v2 (April 2026)

The Centre received a major v2 expansion in Phase 139. Five additive
changes — the entire Phase 128 workflow above still works unchanged —
plus four new toolbar buttons and one new dialog tab.

### 12.1 What changed at a glance

| Area | Phase 128 (v1) | Phase 139 (v2) |
|---|---|---|
| Rule schema | ~30 fields | ~60 fields (`SourcePack`, `BuildingTypes`, `ApplicableStandards`, `GuaranteeCoverage`, `MinCoverageRatio`, `WetZoneExclusion`, `MaintenanceAccessRadiusMm`, `RoutingPreference`, `LinearAnchorMode`, `WindowSillCategory`, `DensityModifier`, `PostPlacementAuditRules`, …) |
| Shipped rule count | ~43 baseline + 4 packs | ~260 (43 baseline + 217 across 7 new packs) |
| Anchor types | ~20 | ~42 (22 new) |
| Validators | 5 stubs + 3 implemented | 8 implemented + 2 new |
| Building context | implicit | explicit `placement_profile.json` per project |
| Excel I/O | none | full export + import roundtrip |
| Scoring threshold | 0.40 | 0.35 (lowered, plus `CoverageContribution` weight) |

### 12.2 The seven new rule packs (217 rules)

The ~43 baseline rules now ship alongside seven discipline / context
packs. Each pack tags its rules with `SourcePack` so the Centre can
filter the rule grid by pack. Packs ship in `StingTools/Data/Placement/`:

| Pack JSON file | Rules | Covers |
|---|---|---|
| `STING_PLACEMENT_RULES.windows-glazing.json` | 30 | Trickle vents, restrictors, blinds, sills, bay-window seating, restrictor cables, child-safety latches |
| `STING_PLACEMENT_RULES.routing.json` | 15 | Containment routing — cable tray riser, conduit drop, duct riser, pipe loop, dado trunking, perimeter trunking |
| `STING_PLACEMENT_RULES.medical-gases.json` | 15 | HTM 02-01 — bedhead trunking, oxygen / vacuum / nitrous outlets, AGSS, terminal units, isolation valves |
| `STING_PLACEMENT_RULES.accessibility.json` | 20 | BS 8300-2 / Approved Doc M — WC reach ranges, grab rails, hearing loops, induction signage, AT-WC transfer space |
| `STING_PLACEMENT_RULES.commissioning.json` | 20 | T&C / FM — test points, balancing valves, pressure gauges, flow meters, witness-test access tags |
| `STING_PLACEMENT_RULES.baseline-extensions.json` | 40 | Office / education extensions — desk pods, IFP whiteboards, pinboards, projectors, AV racks, hot-desk hubs |
| `STING_PLACEMENT_RULES.baseline-extensions2.json` | 77 | Healthcare / hospitality / retail / industrial extensions — nurse call, dirty utility, clean utility, kitchen pass, server-rack PDU, retail merchandiser plinth |

> **Layman tip:** every shipped rule already names the BS / EN / HTM /
> HBN clause it satisfies (`StandardRef`). When you filter the rule
> grid by `SourcePack = medical-gases`, every visible row already
> cites HTM 02-01.

### 12.3 The 22 new anchors

`PlacementScorer.AnchorTypes.cs` (a partial-class extension) adds 22
anchors covering windows, doors, structure, ceiling tiles, escape
routes, MEP system nodes and zone boundaries:

| Anchor | Plain English | Used for |
|---|---|---|
| `WINDOW_SILL_KITCHEN` | Sill of a window in a kitchen room | Tap, bin sensor, splash-back socket |
| `WINDOW_SILL_WET_ROOM` | Sill in a bathroom / WC | Anti-condensation heater, mirror demister |
| `WINDOW_SILL_RESIDENTIAL` | Sill in a residential dwelling | Trickle vent, child-safety latch, restrictor |
| `WINDOW_SILL_COMMERCIAL` | Sill in a commercial space | Office trickle vent (Doc F), motorised blind |
| `WINDOW_SILL_HOSPITAL` | Sill in a healthcare bedroom | HBN 04-01 vent + curtain track |
| `WINDOW_HEAD` | Top of a window | Curtain track, blind motor, head-flashing detail |
| `DOOR_STRIKE_SIDE` | Latch / strike side of door | Access-control reader, MCP, panic strike |
| `DOOR_CLOSER_ZONE` | Top centre of door + 50 mm clearance | Door closer, hold-open magnet |
| `BEAM_SOFFIT` | Underside of nearest structural beam | Cable tray hanger, suspended luminaire eye-bolt |
| `COLUMN_FACE_NEAREST` | Closest face of nearest structural column | Local isolator, fire extinguisher bracket |
| `CEILING_TILE_CORNER` | Nearest 600 × 600 ceiling-tile corner | Suspended diffuser, recessed downlight, smoke detector |
| `CURTAIN_PANEL_CENTRE` | Centre of a curtain-wall panel | Spider fitting, photovoltaic film mount |
| `SLAB_PERIMETER_EDGE` | Perimeter of a slab edge | Edge trim, drip detail, perimeter heater |
| `ESCAPE_DOOR_BOTH_SIDES` | Both sides of every fire-rated escape door | Exit signs (paired), emergency luminaires |
| `STAIR_LANDING_EDGE` | Outer edge of every stair landing | Hand-rail return, photoluminescent strip |
| `STAIR_FLIGHT_MID` | Mid-point of a stair flight | Mid-flight luminaire (BS 5266-1 §5) |
| `CORRIDOR_JUNCTION` | Centre of every corridor T- or X-junction | Directional exit signs, smoke detector |
| `FIRE_EXTINGUISHER_TRAVEL` | 30 m max-travel grid (BS 5306-8) | Fire extinguisher cabinet |
| `CALL_POINT_TRAVEL` | 45 m max-travel grid (BS 5839-1) | MCP call point |
| `RAISED_FLOOR_TILE_EDGE` | Edge of nearest 600 mm raised-access tile | Floor-box edge mount |
| `NEAREST_MEP_SYSTEM_NODE` | XYZ of nearest connector on a named MEP system | Co-located access valve, drain point |
| `ZONE_BOUNDARY` | Boundary of an HVAC / fire / acoustic zone | VAV terminal, fire damper, zone shut-off |

Pick any of these from the **Anchor** dropdown in the rule editor's
Rule Core card. Each anchor produces a different candidate-XYZ pattern;
combine with `OffsetX/Y/Z`, `Side` and `MountingReference` to land
fixtures exactly where the standard demands.

### 12.4 Four new placement algorithms

`Core/Placement/` ships four new helper engines the rule pipeline can
delegate to. They become available the moment a rule sets the relevant
field (`RoutingPreference`, `MinCoverageRatio`, `MaintenanceAccessRadiusMm`,
`WetZoneExclusion`).

| Algorithm | File | What it does |
|---|---|---|
| **WallFollowerRouter** | `WallFollowerRouter.cs` | Walks every wall in a room, returns continuous candidate-XYZs at fixed step. Used by perimeter trunking, dado, skirting. Honours `RoutingPreference` (`Perimeter`, `Inset`, `Centreline`, `Diagonal`). |
| **CoverageGridGenerator** | `CoverageGridGenerator.cs` | Lays a uniform grid over the room polygon, returns one candidate per grid cell. Used by `MinCoverageRatio` rules so the engine can guarantee 100 % spatial coverage (smoke detection, lighting). |
| **TravelDistanceSolver** | `TravelDistanceSolver.cs` | Dijkstra over the room graph; returns candidates within travel-distance from anchor (BS 5306-8 30 m, BS 5839-1 45 m). Backs `FIRE_EXTINGUISHER_TRAVEL` and `CALL_POINT_TRAVEL` anchors. |
| **WetZoneExclusionChecker** | `WetZoneExclusionChecker.cs` | Hard-rejects candidates inside BS 7671 §701 wet zones (Z0 inside bath, Z1 0–225 mm above bath, Z2 within 600 mm). Used automatically when `WetZoneExclusion = true`. |

Two previously-stubbed validators are now implemented:

| Validator | File | Checks |
|---|---|---|
| **UniformityValidator** | `UniformityValidator.cs` | BS EN 12464-1 uniformity ratio across the lit grid (Emin / Eaverage ≥ 0.6 for offices, ≥ 0.7 for surgery) |
| **MaintenanceAccessValidator** | `MaintenanceAccessValidator.cs` | Confirms every fixture has a clear cylinder of `MaintenanceAccessRadiusMm` for service technician reach |

`AccessibilityAuditor` was upgraded to read from
`STING_HEIGHT_STANDARDS.json` (18 entries — BS 8300-2, Approved Doc M,
BS 5839-1, BS 5266-1, HTM 02-01, BB103, BS 6465 reach ranges) so reach
audits cite the right clause for the right room type.

### 12.5 Project Building Profile

Until v2 the Centre had no idea *what kind of building* it was placing
fixtures in. A hospital rule (HBN 04-01 bedhead trunking) would
happily fire in an office model if the room name matched, producing a
nonsensical placement. Phase 139 fixes that with an explicit
**building profile** persisted per project.

#### 12.5.1 The profile file

`<project>/_BIM_COORD/placement_profile.json` — created once per
project. Fields:

| Field | Plain English | Example |
|---|---|---|
| `BuildingType` | Single string from a closed enum | `Office`, `School`, `Hospital`, `Hotel`, `Retail`, `Residential`, `Industrial`, `Laboratory`, `Mixed` |
| `ApplicableStandards` | List of standard codes the project must obey | `["BS 8300-2","Approved Doc M","BS 5839-1","BS 5266-1","BS 7671"]` |
| `ProjectStage` | RIBA stage | `4` |
| `OccupancyMode` | Day / 24-hr / shift | `24h` |
| `FireStrategyRef` | Reference to the project's fire engineer's strategy | `FS-2026-Rev2` |

#### 12.5.2 How rules consume the profile

Each rule carries two new fields:

| Field | Behaviour |
|---|---|
| `BuildingTypes` | List of building types where the rule is allowed. Empty = any. A medical-gases rule lists `["Hospital","Laboratory"]`; a school rule lists `["School"]`. |
| `ApplicableStandards` | List of standard codes the rule satisfies. The Centre filters out rules whose `ApplicableStandards` are not in the project's `ApplicableStandards` list. |

`PlacementRuleLoader.FilterByProfile` runs once at load time and
**hides** non-matching rules from the engine entirely. The Centre's
status bar reports `260 rule(s); 198 hidden by profile`.

#### 12.5.3 Editing the profile

For now, edit `placement_profile.json` in any text editor — a UI card
in the Centre's Run Options group is tracked as Phase 139.5. The
existing rule grid `Search` box accepts `pack:medical-gases` to filter
by `SourcePack`, so you can still inspect every hospital rule even
without the UI.

### 12.6 Updated scoring model

The candidate scorer was rebalanced:

| Component | v1 weight | v2 weight | Why |
|---|---|---|---|
| Geometry fit | 0.45 | 0.35 | Shared with new Coverage component |
| Anchor distance | 0.25 | 0.20 | Slightly less aggressive |
| Side preference | 0.15 | 0.15 | Unchanged |
| Min spacing | 0.10 | 0.15 | More important for uniformity |
| Standard compliance | 0.05 | 0.05 | Unchanged |
| **Coverage contribution** | – | **0.10** | **NEW**: rewards candidates that close coverage gaps |

The rejection threshold dropped from `0.40` to `0.35` so marginal
candidates squeak through (especially in awkward corner rooms). Any
rule with `GuaranteeCoverage = true` (smoke detection, emergency
lighting) **never rejects a candidate** — the engine accepts the best
it has even if the score is low, because losing coverage is worse
than placing slightly off-anchor.

> **Layman version:** the engine used to be stricter than the standards.
> Now it is exactly as strict as the standards, and it never bails on
> mandatory coverage rules.

### 12.7 Excel I/O

Two new toolbar buttons (right of `Save Project`):

| Button | What it does |
|---|---|
| **Export to Excel…** | `PlacementRulesExcelExporter` writes every rule (in priority order) to a styled `.xlsx`. One worksheet per `SourcePack`. Header row frozen, conditional formatting on `Priority`, `MinSpacingMm`, validator-fail count. Use to share with non-BIM stakeholders or to bulk-edit in Excel. |
| **Import from Excel…** | `PlacementRulesExcelImporter` round-trips the file. Imported rules write to `<project>/STING_PLACEMENT_RULES.project.json` (the Layer-4 project overlay, see §8). Conflicts on `RuleId` show the user a 3-pane diff (Excel value / project value / corporate value) and ask which side to keep. |

The Excel format is the same shape as the Phase 139.5 future bulk-import
tool, so today's exports are forward-compatible.

#### Workflow — round-trip with a discipline lead

```
1. Centre ▸ Export to Excel…
2. Send STING_PLACEMENT_RULES_<project>.xlsx to the discipline lead
3. Lead edits MountingHeightMm / MinSpacingMm / Priority across 50 rows
4. Lead returns the file
5. Centre ▸ Import from Excel… → 3-pane diff → accept all
6. Centre ▸ Save Project
```

Done. The 50 rows land in the project overlay and override the
shipped baseline.

### 12.8 Two new worked walk-throughs

#### 12.8.1 *"Place medical-gas terminal units in every hospital bedroom per HTM 02-01"*

1. One-off setup: edit `<project>/_BIM_COORD/placement_profile.json`:
   ```json
   {
     "BuildingType": "Hospital",
     "ApplicableStandards": ["HTM 02-01","HBN 04-01","BS EN ISO 7396-1"],
     "ProjectStage": "4"
   }
   ```
2. Open the Centre. Status bar reports `~32 rules; ~228 hidden by profile`.
3. Search box: `pack:medical-gases` filters the grid to the 15 medical-gas rules.
4. Pick `med-gas-bedhead-trunking-01` — anchor `WALL_MIDPOINT`, side `EITHER`,
   mount height `1300 mm`, min spacing `2400 mm` (one bay per bed),
   StandardRef `HTM 02-01 §6`.
5. Run Options ▸ Scope = Project.
6. Click `Preview`. Teal crosses appear at every bed position in every
   ward and bedroom.
7. `Run Placement` → bedhead trunking lands; co-place rules
   (`med-gas-O2-outlet`, `med-gas-VAC-outlet`, `med-gas-AGSS`,
   `med-gas-N2O-outlet`) chain on automatically.
8. `Validate` → `MaintenanceAccessValidator` confirms 600 mm clear
   reach in front of every terminal.

Time: ~2 minutes from open-project to issued model.

#### 12.8.2 *"Guarantee 100 % smoke-detection coverage on a complex floor"*

1. Centre ▸ search `smoke detector`. Pick `fire-smoke-detector-route`.
2. Confirm:
   - `Anchor = CEILING_TILE_CORNER`
   - `MinSpacingMm = 10500` (BS 5839-1 §22.5)
   - `MountingReference = CEILING`
   - `MountHeightMm = 0`
   - **`GuaranteeCoverage = true`**
   - **`MinCoverageRatio = 1.0`** (100 %)
3. Run Options ▸ Scope = Active view.
4. `Preview` → `CoverageGridGenerator` lays a 7.5 m radius grid;
   detector candidates appear at every uncovered cell centroid.
5. `Run Placement`.
6. `Validate` → `UniformityValidator` confirms zero gaps.

Even in awkward L-shaped rooms or corridor / atrium combinations,
the coverage grid forces the engine to never leave a gap. If a
candidate's score sits below 0.35, the rule's `GuaranteeCoverage`
flag accepts it anyway — better a slightly-off detector than a
missing one.

### 12.9 Updated file map

| Concern | Code path |
|---|---|
| Rule POCO (60+ fields) | `StingTools/Core/Placement/PlacementRule.cs` |
| Anchor types (legacy + Phase 139) | `StingTools/Core/Placement/PlacementScorer.cs`, `PlacementScorer.AnchorTypes.cs` |
| Routing engine | `StingTools/Core/Placement/WallFollowerRouter.cs` |
| Coverage grid | `StingTools/Core/Placement/CoverageGridGenerator.cs` |
| Travel-distance solver | `StingTools/Core/Placement/TravelDistanceSolver.cs` |
| Wet-zone check | `StingTools/Core/Placement/WetZoneExclusionChecker.cs` |
| Building profile | `StingTools/Core/Placement/ProjectBuildingProfile.cs` |
| Height standards table | `StingTools/Core/Placement/HeightStandardsTable.cs` (loads `STING_HEIGHT_STANDARDS.json`) |
| Validators | `StingTools/Core/Validation/UniformityValidator.cs`, `MaintenanceAccessValidator.cs`, `AccessibilityAuditor.cs` |
| Excel I/O | `StingTools/Core/Placement/Excel/PlacementRulesExcelExporter.cs`, `PlacementRulesExcelImporter.cs` |
| Excel commands | `StingTools/UI/PlacementCenter/PlacementExcelCommands.cs` |
| Loader (with profile filter + pack tagging) | `StingTools/Core/Placement/PlacementRuleLoader.cs` |
| Seven new rule packs | `StingTools/Data/Placement/STING_PLACEMENT_RULES.{windows-glazing,routing,medical-gases,accessibility,commissioning,baseline-extensions,baseline-extensions2}.json` |
| Building profile (per project) | `<project>/_BIM_COORD/placement_profile.json` |

### 12.10 Migration notes

Opening an existing project in v2 is automatic and idempotent:

1. Layer 4 / Layer 3 rule files are loaded as before.
2. Any rule missing v2 fields gets sensible defaults (`GuaranteeCoverage = false`, `MinCoverageRatio = 0`, empty `BuildingTypes`/`ApplicableStandards`).
3. The first time the Centre runs against a project that has no `placement_profile.json`, the file is **not** auto-created — the Centre treats every rule as in-scope. Add the profile manually when you want to start gating by building type.
4. The `Preview`, `Run Placement`, `Validate`, `Undo last run`, `Push to Families` toolbar buttons all work identically; no muscle memory lost.

> **Layman version:** v2 adds power without taking anything away. Old
> rules still work. New rules give you 4× the coverage, building-type
> awareness, Excel round-trip and 22 new anchors — opt in when you
> need them.

---

*End of v2 update. v1 of this guide (sections 0 – 11) is the primary
reference for everyday work; this v2 section is what changed.*

---

## 13. Phase 139.2–139.6 — MK alignment, in-wall chase routing, setup audit (April 2026)

Phase 139.2 onward grew out of MK Electric's BIM library and the
shop-floor reality that designers needed three new things at the same
time:

* **Manufacturer-true placement** — the engine should land an MK
  socket exactly where the data-sheet says, not at the rule's hard-
  coded centroid.
* **Conduiting phase awareness** — first-fix boxes need to land in
  one phase, second-fix devices in another, and the matcher must
  survive the project's coordination model getting swapped.
* **In-wall pipe chases** — designers wanted a one-click way to
  route a 15 mm cold-water pipe inside a wall *parallel to the wall*
  with auto-sleeves, structural avoidance, and a depth-fits sanity
  check.

This chapter walks every new field, command, anchor, and engine.
For the deeper background see `CHANGELOG.md` Phase 139.2 → 139.6.

### 13.1 At a glance

| Phase | What landed |
|---|---|
| 139.2 | MK manufacturer catalogue, compound cluster placer, plaster-offset resolver, two-phase box placer, ceiling tile snap, 8 new anchor types, 70 new rules across 3 packs (mk-electrical, ceiling-pendants, conduiting-phase). |
| 139.3 | Structural awareness adapter (load-bearing + junction + opening), in-wall chase router, 5 chase rules (cold/hot water, radiator, waste, conduit), HTM healthcare FCU, BS 5306 sprinkler separation, workset-keyed two-phase matching. |
| 139.4 | 12-finding workflow audit pass, auto-sleeve via SleeveEngine, density-rule cap safety, post-placement hooks for two-phase, duplicate-RuleId detection, density / routing rule loader validation, manufacturer category check, OfCategory pre-filter. |
| 139.5 | Curved-wall midpoints + corners, Linear cap from perimeter ÷ PerLinearMetre, two-phase backward-compat, Revit 2024+ phase API, sandwich-panel chase depth, Substrate guard, arc-length cluster slots, sprinkler nudge-not-drop, batched sleeve mode, regex-pre-bucketed rule iteration. |
| 139.6 | RotationDeg honoured + auto-flip wall-hosted toward room, MinSpacing enforced in lux grid, RoomFilter regexes word-bounded, Placement Setup Audit command, this guide section. |

### 13.2 New rule fields (PlacementRule)

Phase 139.2 added nine field blocks to `PlacementRule`. Each is
optional; rules that don't set them keep working unchanged.

#### 13.2.1 Manufacturer hint

| Field | What it does |
|---|---|
| `ManufacturerCode` | "MK", "Schneider", "Generic" — keyed against `STING_MANUFACTURER_CATALOGUE.json`. |
| `CatalogueRef` | SKU code (e.g. `SP7781ALM`). |
| `BoxDepthMm` | Flush-box recess depth. |
| `ModulePitchMm` | 25.4 mm for MK Grid Plus; 0 for ordinary plates. |
| `GangCount` | 1, 2, 3 … |
| `MountType` | `Flush` / `Surface` / `Ceiling` / `Floor`. |
| `IpRatingMin` | `IP2X` / `IP44` / `IP66`. |
| `InsertionOrigin` | `FixingCentreMidpoint` / `BoxCentre` — tells the router where the family origin is. |

#### 13.2.2 Two-phase conduiting

| Field | What it does |
|---|---|
| `TwoPhaseEnabled` | Switch on the first-fix → second-fix routing pass. |
| `ConstructionPhase` | Phase name where first-fix boxes land. |
| `CompletionPhase` | Phase name where second-fix devices land. |
| `BoxFamilyTypeRegex` | Regex matching the first-fix family/type names. |
| `BoxLocationIdParam` | Shared parameter linking first-fix to second-fix (default `STING_BOX_LOCATION_ID`). |

#### 13.2.3 Compound cluster

| Field | What it does |
|---|---|
| `IsClusterMember` | Mark this rule as part of a multi-module frame. |
| `ClusterGroupId` | Identifier shared by every member of the same frame. |
| `ClusterSlotIndex` | 0-based position of this module within the frame. |
| `ClusterTotalSlots` | Total module count in the frame (used for centring math). |
| `ClusterFrameWidthMm` | `ClusterTotalSlots × ModulePitchMm` (informational). |

Example use: an MK Grid Plus 4-module bedhead in a hospital ward —
slot 0 power, slot 1 data, slot 2 nurse-call, slot 3 dimmer.

#### 13.2.4 Plaster / finish-face offset

| Field | What it does |
|---|---|
| `PlasterOffsetMode` | `Auto` (read compound structure), `Fixed` (use `PlasterOffsetFixedMm`), `None`. |
| `PlasterOffsetFixedMm` | Fixed offset for `Fixed` mode. |

`Auto` walks `Wall.WallType.GetCompoundStructure()`, sums interior
finish layers (Function = `Finish1` / `Finish2` / `Membrane`, or a
material name matching `\b(plaster|skim|render|plasterboard|gypsum|MF\s+ceiling|MF\s+lining)\b`), and pushes the family origin out by
that distance along the wall normal.

Phase 139.5 added a Substrate guard: layers flagged
`Function = Substrate` (steel-stud drywall partitions) only count as
finish when the material name explicitly matches the regex above —
otherwise the studs are skipped, so sockets don't push 100 mm into
the wall.

#### 13.2.5 Ceiling tile snap

| Field | What it does |
|---|---|
| `CeilingTileSnap` | After the lumen-method grid is computed, snap each point to the nearest ceiling tile centre. |
| `TileGridSpacingXMm` | Tile X dimension (default 600 or 1200). |
| `TileGridSpacingYMm` | Tile Y dimension (default 600). |

Office rooms with a 1200×600 plasterboard tile grid get pendants on
the long edge of the tile; classrooms with 600×600 tiles get one
luminaire per cell. Rules that set `CeilingTileSnap = true` should
also set `Document.Settings.Categories.Ceilings` properly so the
calculator can detect the grid origin.

#### 13.2.6 Structural fixing check

| Field | What it does |
|---|---|
| `StructuralFixingCheck` | Find a joist within `JoistClearanceMm` for every grid point. |
| `JoistClearanceMm` | Max distance to the nearest joist (default 300 mm). |
| `EmitNogginRequirement` | When no joist found, write `STING_NOGGIN_REQUIRED = 1` so the noggin export picks it up. |

Use the **`Placement_ExportNogginRequirements`** command to dump a
CSV of every pendant needing a structural noggin — the site team
fits the noggins before the false ceiling goes up.

#### 13.2.7 Wet-zone exclusion

| Field | What it does |
|---|---|
| `WetZoneExclude` | When true, reject placement within the rule's `WetZoneClass` (BS 7671 Part 7-701). |
| `WetZoneClass` | `Zone0` / `Zone1` / `Zone2` / `Zone3`. |

Sockets and switches with `WetZoneExclude = true` automatically skip
the bath / shower zones in residential plumbing rooms.

#### 13.2.8 Height standard alias

| Field | What it does |
|---|---|
| `HeightStandardRef` | Key into `STING_HEIGHT_STANDARDS.json` (e.g. `BS7671_SOCKET_STD`, `HTM0601_BEDHEAD_SOCKET`). |

When the rule's `MountingHeightMm` is 0, the engine reads the
preferred height from the standards table; saves repeating the
500 mm / 1350 mm number across thirty residential rules.

#### 13.2.9 Coverage & manufacturer scoring (already in 139.0)

* `CoverageRadiusMm` — engine fills room to 100 % coverage when
  `GuaranteeCoverage = true`.
* `MaxSpacingMm` — upper bound on centre-to-centre spacing.
* Phase 139.4 added a `ScoreManufacturerResolution` component to the
  composite score; rules whose `CatalogueRef` resolves to a loaded
  family score 1.0, mismatched-category families score 0.5, missing
  catalogue scores 0.

### 13.3 Eight new anchor types

Phase 139.2 added eight anchors to the existing Phase 139 set.

| Anchor | Use case |
|---|---|
| `STRUCTURAL_SOFFIT` | BESA box for a pendant — point on the structural slab top above the room centroid. |
| `CEILING_TILE_CENTRE` | Single pendant or detector on the nearest tile centre. |
| `WALL_FACE_OFFSET` | Wall-mounted device with `PlasterOffsetMode = Auto` applied along the inward wall normal. |
| `DOOR_LATCH_SIDE` | Light switch on the latch side, 150 mm from the frame. |
| `DOOR_HINGE_SIDE_150` | Two-way switch on the hinge side, 150 mm from the frame. |
| `CONDUIT_BOX_MATCHED` | Second-fix pass: anchor against an already-placed first-fix box that has `STING_BOX_LOCATION_ID` set. |
| `CEILING_VOID_ABOVE_BOX` | Junction box at ceiling void above the mid-span of two outlet positions. |
| `FLOOR_SLAB_PENETRATION` | Floor box on the structural slab top. |

Phase 139.5 then made `EmitWallMidpoints` / `EmitWallCorners` work
on **curved walls** (Arc / NurbSpline boundary segments) — they used
to silently drop, leaving curved-wall rooms with zero anchors.

### 13.4 Six new engines

Phase 139.2–139.6 added six new engine classes. Each is small,
stateless from the caller's perspective, and never throws — failure
collects warnings.

#### 13.4.1 ManufacturerCatalogueRegistry

`Core/Placement/ManufacturerCatalogueRegistry.cs`

Loads `STING_MANUFACTURER_CATALOGUE.json` (27 entries shipping with
the plug-in: MK Logic Plus 1G/2G/3G flush + surface, Grid Plus 2/4/6/8
modules, Metal Clad IP2X/IP66, BESA round 36/47, square outlet 44/57,
MK junction boxes). Each entry carries `BoxDepthMm`,
`BoxExternalLMm`, `BoxExternalWMm`, `FixingCentresMm`, `ModulePitchMm`,
`IpRating`, `RevitFamilyName`, `RevitTypeName`, `InsertionOrigin`,
`FaceplateStandard`.

Key APIs:

* `Resolve(manufacturerCode, catalogueRef)` — returns the entry or null.
* `GetForRule(rule)` — convenience wrapper.
* `AutoPopulateFromFamilies(doc)` — walks every loaded `FamilySymbol`,
  reads the `MK_*` shared parameters, upserts entries to disk. Run
  this once after loading the MK content library.
* `Reload()` — force a re-read after editing the JSON externally.

The registry is per-document via path resolution; opening project A
then project B drops the cache and re-resolves automatically (Phase
139.3 fix).

#### 13.4.2 PlasterOffsetResolver

`Core/Placement/PlasterOffsetResolver.cs`

Reads the host wall's compound structure, sums the interior finish
layers, returns the offset in feet. Three modes:

* `Auto` — walk the wall's `CompoundStructure.GetLayers()` from
  the interior side, accumulate finish/membrane layers, stop at
  `Function = Structure` or the structural-core layer index.
* `Fixed` — return `PlasterOffsetFixedMm × MmToFt`.
* `None` — return 0.

Applied at placement time by the engine when the rule's anchor is a
wall anchor (`WALL_FACE_OFFSET`, `WALL_MIDPOINT`, etc.).

#### 13.4.3 CompoundClusterPlacer

`Core/Placement/CompoundClusterPlacer.cs`

Distributes multi-module frame slots along the wall location curve.
Three modes:

* Straight wall — slots offset along the wall tangent at
  `(slot − (totalSlots − 1) / 2) × ModulePitchMm`.
* Curved wall (Arc / NurbSpline) — Phase 139.5 added arc-length
  parameter sampling around the frame centre so slots follow the
  curve instead of walking off-tangent.
* Pitch-less group — collapses every slot to the frame centre.

Used by every rule whose `IsClusterMember = true`.

#### 13.4.4 TwoPhaseBoxPlacer

`Core/Placement/TwoPhaseBoxPlacer.cs`

Orchestrates the conduiting-phase workflow:

1. **Pre-flight** — `ValidateSharedParams(doc, rules)` warns
   (doesn't fail) when `STING_BOX_LOCATION_ID` isn't bound.
2. **Pass 1 (Construction phase)** — `PlaceFirstFixBoxes(doc, roomIds, rules, result)`
   places every box for `TwoPhaseEnabled` rules and stamps each with
   a fresh GUID. Phase 139.3 added a `|ws=<workset>` suffix so
   coordination-model swaps survive workset changes; Phase 139.5
   added the second-fix matcher's `|ws=*` strip for backward compat
   with pre-139.3 first-fix boxes.
3. **Pass 2 (Completion phase)** — `PlaceSecondFixDevices(doc, roomIds, rules, firstFixIndex, result)`
   matches by XYZ proximity within `ToleranceMm` (default 50 mm),
   pre-bucketed by containing room (Phase 139.4 fix turning the
   match from O(rules × rooms × boxes) to O(boxes-in-room) per
   rule).

Both passes call `PostPlacementHooks.RunFor` on every placed box
(Phase 139.4 fix), so two-phase output gets the same data-tag /
COBie / system pipeline as a normal Place run.

#### 13.4.5 StructuralAwareness

`Core/Placement/StructuralAwareness.cs`

Wraps the existing `StructuralModelingEngine.AnalyzeLoadPaths` and
`StructuralCADPipeline.DetectJunctions` plus a live-model
`Wall.FindInserts` lookup:

* `IsLoadBearing(el)` — true for columns, beams, foundations, and
  walls flagged `StructuralWallUsage != NonBearing`.
* `IsNearJunction(point, clearanceFt)` — true when a beam-junction
  cluster or column centre sits within range.
* `GetWallOpenings(wall)` — returns world-AABB of every door /
  window / opening in the wall.
* `PointIsInOpening(wall, point)` — chase router uses this to permit
  routing through openings.
* `SegmentIsRoutable(hostWall, a, b, clearanceFt)` — composite
  routing check.

Used by `InWallChaseRouter` and (optionally) by future placement
rules that want to avoid placing on load-bearing structure.

#### 13.4.6 InWallChaseRouter

`Core/Placement/InWallChaseRouter.cs`

Routes Pipe / Conduit segments parallel to a host wall's location
curve at a controllable inset from the room finish face. Workflow:

1. **Compound-structure depth check** — read the wall's layer
   stack, sum interior finishes, reject if (pipe OD + 2 × insulation
   + clearance) > available chase depth. Phase 139.5 distinguishes
   "no compound structure" (warn-and-continue) from "pipe doesn't
   fit" (reject). Phase 139.5 also stops summing at the
   `CompoundStructure.StructuralMaterialIndex` for sandwich panels.
2. **Project endpoints onto the wall location curve** — true
   parallel; not centroid-biased like the older `WallFollowerRouter`.
3. **Apply the rule's `MountingHeightMm`** — Phase 139.5 fix; Z is
   set to wall level + height instead of preserving the picked Z.
4. **Validate every segment** — `StructuralAwareness.SegmentIsRoutable`
   rejects segments through load-bearing zones; segments that span
   wall openings (door head, window cripple) are allowed.
5. **Create pipe/conduit segments** — falls back to `Conduit.Create`
   when `RouteSegmentCategory = "Conduit"`.
6. **Auto-sleeve** — `SleeveEngine.PlaceSleeves` cuts the host wall
   and drops a `STING_SLEEVE_ROUND` family at every penetration.
   Phase 139.5 added `BatchSleevesAtEnd` + `FlushSleeves()` so an
   engine-level run with 50 chase routes makes one sleeve pass
   instead of 50.

Reported on `ChaseRouteResult`:
`CreatedSegments`, `RejectedSegments`, `SleevesPlaced`,
`AvailableChaseDepthMm`, `RequiredChaseDepthMm`, `Warnings`.

Run it with the **`Placement_RunWallChase`** command (Section
13.5.3).

#### 13.4.7 LightingGridCalculator extensions

The lumen-method calculator picked up four post-process passes:

1. `SnapToCeilingTileGrid(room, r, rule)` — Phase 139.2; nudges each
   computed grid point to the nearest ceiling-tile centre (active
   when `rule.CeilingTileSnap = true`).
2. `CheckStructuralFixing(room, r, rule)` — Phase 139.2; for every
   point, find a joist within `JoistClearanceMm`. When none and
   `EmitNogginRequirement = true`, the point is added to
   `r.NogginRequiredPoints` and stamped on the placed instance.
3. `CheckSprinklerSeparation(room, r)` — Phase 139.3; BS 5306-2 /
   BS EN 12845 ≥ 600 mm rule. Phase 139.5 changed the algorithm from
   "drop conflicting points" to "nudge tangentially first, drop
   only if no clear position exists" so the grid keeps its fixture
   count.
4. `EnforceMinSpacing(r, rule)` — Phase 139.6; drops points closer
   than `rule.MinSpacingMm`. Stops lights stacking in narrow
   rooms.

Plus `ComputeUniformityRatio` runs at the end to estimate Uo and
warn when below BS EN 12464-1's 0.40 minimum for general areas.

### 13.5 Five new commands

All five are dispatched through `StingCommandHandler` and invocable
from the BIM ribbon's Placement group.

#### 13.5.1 `Placement_AutoPopulateCatalogue`

Walks every `FamilySymbol` in the document, harvests the `MK_*`
shared parameters, and upserts entries into
`STING_MANUFACTURER_CATALOGUE.json`. Run this once after loading
your MK BIM library; the registry then resolves catalogue refs
against the loaded families for every placement run.

Output: TaskDialog showing `<n> new`, `<m> updated`, top 20
contributing families.

#### 13.5.2 `Placement_ExportNogginRequirements`

Collects every `Lighting Fixtures` / `Electrical Fixtures` /
`Lighting Devices` instance with `STING_NOGGIN_REQUIRED = 1`,
writes a CSV (`Room, Level, X_mm, Y_mm, Z_mm, BoxType,
CatalogueRef, FixingDate`), and optionally drops a
`STING_NogginMarker` generic-model element at each point if the
family is loaded.

Output: TaskDialog with the row count + CSV path.

#### 13.5.3 `Placement_RunWallChase`

Pick a host wall + two endpoints. Phase 139.4 added a preview pass
inside a rolled-back TransactionGroup before committing — the user
gets a "Created N pipes, M sleeves, depth available X / required Y;
commit?" Yes/No before any geometry is committed.

#### 13.5.4 `Placement_ExportRulesExcel` / `Placement_ImportRulesExcel`

Round-trip the active rule set through Excel. The exporter writes one
worksheet per `SourcePack` (Baseline / MK_Electrical / Ceiling_Pendants
/ Conduiting_Phase / InWall_Chase / Architecture / Mechanical /
Electrical / etc.) plus a SCHEMA worksheet listing every column type.

The importer reads the workbook back, validates on field types, and
upserts into `STING_PLACEMENT_RULES.project.json` next to the .rvt.

Use this to hand a discipline lead the rules in a workbook, get
edits back, and merge them without dropping into JSON.

#### 13.5.5 `Placement_AuditSetup` (Phase 139.6)

Walks the active document and reports every gap from the authoring
checklist (Section 13.6). Five passes:

* Shared parameters bound (`STING_BOX_LOCATION_ID`,
  `STING_NOGGIN_REQUIRED`, `STING_FIXTURE_VARIANT_TXT`,
  `MK_CATALOGUE_REF`).
* Critical families loaded (BESA round box, square outlet, MK Logic
  Plus / Grid Plus / Metal Clad, sleeves, junction boxes).
* Critical category symbols present (Sprinklers, Fire Alarm Devices,
  Air Terminals, Lighting Fixtures).
* Phases declared (`Construction`, `Handover`).
* Manufacturer catalogue populated, rule packs load, view-style pack
  discoverable.

Output: TaskDialog grouped by Error / Warning / Info (top 40), and a
**CSV deliverable** at
`OutputLocationHelper.GetOutputPath(doc, "PlacementSetupAudit")` so
the audit can be committed to the federation model record.

**Run this BEFORE the first Place Fixtures run on any project.**

### 13.6 Family-Authoring Requirements — STING Auto-Placement

This is the single source of truth for how every Revit family must be
authored before the auto-placement engine, the lux grid, the
two-phase conduiting workflow, and the in-wall chase router can land
the geometry cleanly.

If you only read one section in this chapter, read this one.

The full reference (with worked examples) lives in
[`PLACEMENT_FAMILY_AUTHORING.md`](PLACEMENT_FAMILY_AUTHORING.md).

#### 13.6.1 Universal authoring rules (every category)

| Requirement | Why |
|---|---|
| Family origin = the rule's expected insertion point (NOT geometry centre). For a wall socket, the fixing-centre midpoint, not the box back. | `CompoundClusterPlacer` and `WALL_FACE_OFFSET` offset from the family origin. |
| Reference Plane `Center (Front/Back)` = wall-face plane, `Defines Origin = Yes`, `Reference = Strong`. | Lets `WALL_FACE_OFFSET` apply `PlasterOffsetMode = Auto` against the right datum. |
| `Always Vertical = Yes` for vertical fittings; `Cuts with Voids When Loaded = Yes` for sleeves, anchors, pendants. | `SleeveEngine` won't cut the wall otherwise. |
| Shared parameters loaded: `STING_BOX_LOCATION_ID`, `STING_NOGGIN_REQUIRED`, `STING_AUTO_PLACED_BOOL`, the `MK_*` catalogue params. | Required for two-phase matching, noggin export, catalogue auto-populate. |
| `STING_FIXTURE_VARIANT_TXT` = rule's `VariantHint` ("FLUSH", "DIMMER", "IP65"). | `FixturePlacementEngine.ResolveSymbol` chains on this value. |
| Type name starts with the gang/size/IP code — e.g. `1G_25mm_IP2X_FlushWhite`. | `FamilyTypeRegex` patterns rely on the type name. |

#### 13.6.2 Per-category authoring matrix

| Category | Hosting | Origin / facing | Required reference planes | Key parameters | Engine rotation | Common mistake |
|---|---|---|---|---|---|---|
| **Electrical Fixtures** (sockets, FCUs) | Wall-Hosted | Fixing-centre midpoint of front face; faceplate normal points into room | `Center (Front/Back)` = wall face, `Center (Left/Right)` symmetry | `MK_BOX_DEPTH_MM`, `MK_FIXING_CENTRES_MM`, `MK_GANG_COUNT`, `MK_CATALOGUE_REF`, `STING_FIXTURE_VARIANT_TXT` | Auto-flip facing to inward room normal (Phase 139.6) | Origin at box back → socket sits 25 mm into wall |
| **Lighting Devices** (switches, dimmers) | Wall-Hosted | Faceplate midpoint; rocker faces room | `Center (Front/Back)` = wall face | `MK_BOX_DEPTH_MM`, `STING_FIXTURE_VARIANT_TXT` (`DIMMER`/`DALI`/`2-WAY`); `Family Placement Type = One Level Based Hosted` | Auto-rotate so rocker faces room | Family not `Always Vertical` → switch lies flat at door head |
| **Lighting Fixtures** (pendants, downlights, panels) | Ceiling-Hosted (surface), Face-Based (soffit), Floor/Roof-hosted (slab pendants) | Visible drop centre; photometric axis −Z | `Ceiling`, `Defines Origin = Yes` | `MK_CATALOGUE_REF`, `STING_PHOTOMETRIC_LM`, IES file, `IS_INDIVIDUAL_LUMINAIRE = Yes` | None (face-based families inherit ceiling normal) | Pendant authored as un-hosted `OneLevelBased` → engine drops to room centre and stacks |
| **Plumbing Fixtures** (WC, basin, shower) | Wall-Hosted (WC/basin), Floor-Based (shower trays) | Footprint centre, not bowl | `Center (Left/Right)`, `Center (Front/Back)`, `Reference = Strong` | `IS_FIXTURE_BACK_TO_WALL = Yes`, `STING_FIXTURE_VARIANT_TXT` (`WC`/`BASIN`/`SHOWER`) | None | Origin on bowl rather than footprint → chase pipe lands inside cistern |
| **Communication / Data Devices** | Wall-Hosted | Faceplate midpoint, faces room | Same as Electrical Fixtures | `STING_FIXTURE_VARIANT_TXT` (`HDMI`/`USB-C`/`RJ45`/`RJ12`); `MK_MODULE_PITCH_MM = 25.4` for Grid Plus | Inward room normal | Authored as Generic Model → category filter rejects |
| **Fire Alarm Devices** | Wall-Hosted (MCP, sounder, beacon), Ceiling-Hosted (smoke, heat) | Faceplate or sensor head | `Center (Front/Back)` | `BS5839_DEVICE_KIND` (`MCP`/`SOUNDER`/`SMOKE`/`HEAT`), `STING_FIXTURE_VARIANT_TXT`; smoke `Coverage Radius = 7500 mm`, heat = `5300 mm` | MCP/sounder face room; smoke/heat point down | Smoke as Wall-Hosted → no ceiling host found, all coverage rules skip |
| **Sprinklers** | Ceiling-Hosted (or Face-Based) | Deflector centre; K-factor in type | `Ceiling`, `Defines Origin = Yes` | `BS_5306_K_FACTOR`, `Coverage Radius`, `STING_FIXTURE_VARIANT_TXT` (`UPRIGHT`/`PENDANT`/`CONCEALED`) | Down for pendants, up for upright | Family not loaded → 600 mm BS 5306 separation check is silent no-op |
| **Conduits** (first-fix square / BESA round) | Wall-Hosted (square box), Face-Based (BESA on slab soffit) | Box centre | `Center (Front/Back)` = wall face / soffit face | `STING_BOX_LOCATION_ID`, `MK_CATALOGUE_REF`. Family name MUST match `BoxFamilyTypeRegex` (`Conduit_BESA_Round`, `Conduit_Square_Outlet`) | Match host face | Family naming mismatch → "no first-fix box family matched" warnings |
| **Junction Boxes** | Face-Based (slab soffit / wall) | Box centre | `Ceiling` or `Wall` reference plane | `STING_FIXTURE_VARIANT_TXT` (`5A_3T`/`20A_5T`/`30A_3T`/`20A_WP_IP66`), `MK_CATALOGUE_REF` | Default | Authored as Generic Model rather than Junction Boxes / Electrical Fixtures |
| **Air Terminals** | Face-Based (ceiling / wall) | Diffuser centre; throw axis along longest dimension | `Diffuser face` = `Defines Origin` | `STING_FIXTURE_VARIANT_TXT` (`LINEAR_SLOT`/`4-WAY`/`JET`/`EXTRACT`), `Air Flow` | 4-way none; linear slots align to longest wall | Family not loaded → all Mechanical-pack rules skip silently |
| **Pipes** (chase / waste / cold-water) | Self-hosted segments | n/a | n/a | `Pipe Outer Diameter`, `Insulation Thickness`, `Connection Type`. PipeType MUST have Routing Preferences elbow/tee/cross fittings configured. | n/a | Default PipeType missing fittings → segments don't auto-elbow at corners → orphan runs |

#### 13.6.3 Project setup checklist

| Item | What it does | Symptom if missing |
|---|---|---|
| Run `Placement_AutoPopulateCatalogue` after loading MK families | Walks every loaded `FamilySymbol`, harvests `MK_*` shared params | `ScoreManufacturerResolution` returns 0/0.5 for every rule |
| Bind `STING_BOX_LOCATION_ID` to MEP / Lighting / Electrical / Junction-Box categories | Two-phase first-fix → second-fix matching | Engine warns "Two-phase matching will run in degraded mode" |
| Bind `STING_NOGGIN_REQUIRED` (Yes/No) to Lighting Fixtures | Noggin export tracks pendants needing structural fixings | `NogginRequirementExportCommand` returns zero rows |
| Load BESA round box family (`Conduit_BESA_Round`, types `57D_36mm`, `57D_47mm`) | First-fix box for every pendant / downlight | 14 rules silently skipped |
| Load `STING_SLEEVE_ROUND` family | `InWallChaseRouter.AutoSleeve` post-step | `SleeveEngine` falls back to dry-run; chase pipes leave uncut walls |
| Add Phases `Construction` and `Handover` | Two-phase routing decoupling | First-fix and second-fix both land in active phase — coordination breaks |
| Set Project `BuildingType` (Residential / Office / Healthcare / Education) via Centre profile | Filters discipline packs at load time | Healthcare HTM rules fire on residential projects |

Run **`Placement_AuditSetup`** to cross-check every item on this list against the live model before the first Place Fixtures run. The
audit writes a CSV deliverable so the gap list can be tracked.

### 13.7 The recommended setup workflow

The first time you point the Centre at a project:

1. **Load the family library.** MK content from NBS or BIMobject for
   sockets / switches / Grid Plus; the BESA round box family
   (`Conduit_BESA_Round`); the square outlet family
   (`Conduit_Square_Outlet`); your standard lux-method luminaires;
   `STING_SLEEVE_ROUND` for chase routing.
2. **Bind shared parameters.** Either run the existing
   `Tags > Load Shared Parameters` command or manually bind
   `STING_BOX_LOCATION_ID`, `STING_NOGGIN_REQUIRED`,
   `STING_FIXTURE_VARIANT_TXT`, `MK_CATALOGUE_REF` to MEP /
   Lighting / Electrical / Junction Boxes / Plumbing categories.
3. **Add phases.** Manage > Phases > New: `Construction` and
   `Handover` if not already present.
4. **Run `Placement_AutoPopulateCatalogue`.** Once.
5. **Run `Placement_AuditSetup`.** Read the CSV. Fix every Error
   and as many Warnings as you can.
6. **Set the Project Building Profile** in the Centre's profile
   selector (Residential / Office / Healthcare / Education / etc.).
7. **Open the Placement Centre.** Verify the rule pack count in
   the toolbar matches what you expect (should be 200+ across all
   discipline packs).
8. **Run a Preview** on one room before doing the whole project.
9. **Run Placement.** Tag the audit CSV onto the run record.

### 13.8 Phase 139.6 troubleshooting

If your Place run produces messy output, walk this list before
filing a bug.

| Symptom | Cause | Fix |
|---|---|---|
| Switches landing facing up at door centre | Family not Always Vertical, or anchor isn't a wall anchor | Set `Always Vertical = Yes` on the family; verify rule's `AnchorType` is one of `WALL_*` or `DOOR_*`. Phase 139.6 auto-flips facing toward the room. |
| Lights stack vertically with no spacing | Rule's `MinSpacingMm` is 0 | Set `MinSpacingMm` to the realistic minimum (e.g. 1500 mm for office pendants); Phase 139.6 enforces it. |
| Toilets in walk-in closets / wardrobes | Loose RoomFilter regex | Phase 139.6 word-bounded the baseline. If you wrote a custom rule, use `(?i)\b(wc\|toilet\|bathroom\|en-suite)\b`. |
| "TwoPhase: no first-fix box family matched" | BESA / square outlet family not loaded | Load `Conduit_BESA_Round.rfa` and `Conduit_Square_Outlet.rfa`. Run `Placement_AuditSetup` to confirm. |
| "STING_BOX_LOCATION_ID not bound" | Shared parameter not bound | Bind to MEP / Lighting / Electrical / Junction Boxes / Plumbing categories. |
| "No FamilySymbol found for category 'Sprinklers'" | Sprinkler family not loaded | Load any sprinkler family — needed for the BS 5306 separation check even if you don't place sprinklers. |
| Chase router rejected with "exceeds available chase depth" | Wall is too thin for the pipe + insulation + clearance | Either thicken the wall, switch to a thinner pipe via the catalogue, or set `PlasterOffsetMode = Fixed` with a known offset. |
| Chase pipes leave uncut walls | Sleeve family not loaded | Load `STING_SLEEVE_ROUND.rfa`. The router falls back to dry-run sleeve placement and warns. |
| Curved-wall room produces zero anchors | Pre-139.5 only — Arc/NurbSpline boundaries were dropped | Pull current main; Phase 139.5 routes any boundary curve through `Curve.Evaluate(0.5, true)`. |

### 13.9 Updated file map (Phase 139.2 → 139.6)

```
StingTools/Core/Placement/
├── ManufacturerCatalogueEntry.cs       — POCO
├── ManufacturerCatalogueRegistry.cs    — JSON-backed catalogue
├── PlasterOffsetResolver.cs            — compound-structure walker
├── CompoundClusterPlacer.cs            — multi-module frame distributor
├── TwoPhaseBoxPlacer.cs                — first-fix / second-fix workflow
├── StructuralAwareness.cs              — load-bearing + junction + opening adapter
├── InWallChaseRouter.cs                — chase pipe routing + auto-sleeve
└── (plus the earlier Phase 139 files, unchanged)

StingTools/Commands/Placement/
├── ManufacturerCatalogueAutoPopulateCommand.cs
├── NogginRequirementExportCommand.cs
├── PlacementRulesExcelCommands.cs       — export + import
├── RunWallChaseCommand.cs
└── PlacementSetupAuditCommand.cs        — Phase 139.6

StingTools/Data/Placement/
├── STING_MANUFACTURER_CATALOGUE.json    — 27 SKUs
├── STING_PLACEMENT_RULES.mk-electrical.json     — 36 rules
├── STING_PLACEMENT_RULES.ceiling-pendants.json  — 20 rules
├── STING_PLACEMENT_RULES.conduiting-phase.json  — 16 rules
├── STING_PLACEMENT_RULES.in-wall-chase.json     — 5 rules
└── STING_HEIGHT_STANDARDS.json                  — 33 keys

StingTools/Data/
└── STING_VIEW_STYLE_PACKS.json          — Lighting Devices, Junction Boxes, STING-First-Fix-Phase, STING-Noggin-Required filter rules added in Phase 139.3

docs/
├── PLACEMENT_CENTRE_GUIDE.md            — this file
├── PLACEMENT_FAMILY_AUTHORING.md        — Phase 139.6 reference (Section 13.6 mirrors this)
├── PLACEMENT_CENTRE_REVIEW.md
└── CHANGELOG.md                         — Phase 139.2 → 139.6 entries
```

### 13.10 Migration notes

* **No JSON breaking changes.** Pre-139.2 rule packs continue to load
  unchanged; the new fields default to safe values (cluster off,
  two-phase off, plaster auto-on for any rule that sets it).
* **Existing first-fix boxes (pre-139.3 BoxLocationId)** still match
  on second-fix because Phase 139.5 strips the `|ws=*` workset
  suffix before stamping.
* **Old `WallFollowerRouter`** is preserved alongside the new
  `InWallChaseRouter`. Use whichever fits: WallFollow routes on the
  face (containment / surface conduit), InWallChase routes inside
  the wall finishes (chase pipes / first-fix conduit). Future
  unification is on the deferred list.
