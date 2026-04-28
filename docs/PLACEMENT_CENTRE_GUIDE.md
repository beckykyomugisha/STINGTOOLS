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

## 13. Phase 139 deeper dive — support engines

Beyond the four headline algorithms in §12.4, the v2 release ships
seven support engines that quietly back the Centre's behaviour. You
do not call them directly; rules and the engine pick them up
automatically based on rule fields. Knowing they exist makes the
behaviour transparent rather than mysterious.

### 13.1 LightingGridCalculator (BS EN 12464-1 lumen method)

`Core/Placement/LightingGridCalculator.cs`. Triggered by any rule
whose `AnchorType = LIGHTING_GRID`. Calculation basis:

```
N = (E_target × A) / (LUMENS × UF × MF)
```

| Symbol | Meaning |
|---|---|
| `E_target` | Target maintained lux from `LUX_TARGETS_EN12464.csv` (e.g. 500 lx for office, 750 lx for laboratory, 1000 lx for surgery) |
| `A` | Room floor area in m² |
| `LUMENS` | Lumen output of the resolved luminaire family symbol |
| `UF` | Utilisation factor (room index + reflectance lookup) |
| `MF` | Maintenance factor — `0.80` typical for a clean LED |
| `N` | Required luminaire count (ceiled, snapped to grid) |

The calculator classifies the room via `ROOM_TYPE_CLASSIFIER.csv`,
picks the target lux, and returns a list of XYZ candidate points
already aligned to the ceiling tile grid. Beautifully — no manual
spacing tables.

### 13.2 CeilingGridSnap

`Core/Placement/CeilingGridSnap.cs`. Snaps any candidate XYZ to the
nearest tile-grid intersection on the host ceiling, and orients
rectangular luminaires (troffers) along the room's long axis so they
line up with tile seams instead of cutting across them.

Reads `Tile Width` / `Tile Height` from the host ceiling type
parameters. Defaults to 600 × 600 mm if the ceiling type is
non-modular (then snapping is effectively disabled). Long-axis
orientation comes from a 2D oriented bounding box of the room's
boundary curve loop — square-ish rooms preserve the existing XY.

Triggered automatically when `MountingReference = CEILING` and the
host ceiling has a `Tile Width` parameter. No rule field required.

### 13.3 ObstructionIndex

`Core/Placement/ObstructionIndex.cs`. A spatial index that lets
candidates query "is there an obstruction within X mm of me?" in
O(log n). Backs the `ObstructionClearanceMm` rule field.

The index is built once per Run from every Furniture, Casework,
Structural Column, Plumbing Fixture and Mechanical Equipment instance
visible in scope. Candidates that fail the obstruction check are
either rejected (default) or, if `GuaranteeCoverage = true`, accepted
with a `CLEARANCE_BREACH` warning so the validator can flag them later.

Exposes the `ExclusionRect` value-type used by `WetZoneExclusionChecker`
and `CoverageGridGenerator`.

### 13.4 PlacementHostPreflight

`Core/Placement/PlacementHostPreflight.cs`. Bridges `AnchorType` to
the family's `FamilyPlacementType`. Revit's `NewFamilyInstance`
overloads are placement-type-specific — calling the wrong one either
throws or places a free-standing instance whose Host is null
(schedules then silently miss it).

The pre-flight picks the right overload up front:

| Family placement type | Overload picked |
|---|---|
| `OneLevelBased` / `OneLevelBasedHosted` | `(point, symbol, level, st)` |
| `WorkPlaneBased` | `(reference, point, refDir, symbol)` |
| `WallBased` | `(point, symbol, hostWall, level, st)` |
| `CeilingBased` / `FloorBased` / `RoofBased` | `(point, symbol, hostElement, level, st)` |
| `ViewBased` | rejected — out of engine scope |

When a host is required, the pre-flight locates the nearest sensible
candidate (nearest ceiling for `CeilingBased`, nearest wall for
`WallBased`, …) at the placement point. If anchor intent and family
template disagree (e.g. a `WALL_MIDPOINT` rule resolving to a
`CeilingBased` family), it surfaces a clear warning rather than
silently failing.

### 13.5 PlacementParamReader

`Core/Placement/PlacementParamReader.cs`. The single entry point for
reading rule-friendly values off Revit elements. Wraps:

- Type vs instance disambiguation (some rules want `LUMENS` from the
  type; some want `OCCUPANT_COUNT` from the instance).
- Unit conversion (every value comes back in **mm** for distances,
  **m²** for areas, **lumens** for flux — so the engine never has
  to think in feet).
- Null safety (returns `0`, `""`, `false` defaults rather than
  throwing on missing parameters).

If a rule starts misbehaving and you suspect "the engine isn't
seeing the parameter on the element", this is the file to put a
breakpoint in.

### 13.6 PostPlacementHooks (PC-17)

`Core/Placement/PostPlacementHooks.cs`. Static toggles that fire
side-effects after every placed instance commits *inside* the
engine's transaction. Three hooks, each on/off via Run Options:

| Hook | Toggle | What it does |
|---|---|---|
| `RunDataTagPipeline` | "Run data-tag pipeline on each placement" | Calls `TagPipelineHelper.RunFullPipeline` so the placed instance lands fully ISO-19650 8-segment tagged in the same transaction |
| `SeedCobieComponent` | "Seed COBie component fields from the rule" | Copies `StandardRef` → `COBIE_COMPONENT_NAME` and `Notes` → `COBIE_COMPONENT_DESCRIPTION` so COBie export is non-empty |
| `AssignMepSystem` | "Probe MEP connectors after placement" | Records a warning if the instance has unconnected connectors so `MEPSystemBuilder` can pick them up later (full system traversal pending) |

Because hooks run inside the placement transaction, **a single Ctrl+Z
reverts placement + hook side-effects atomically** — no half-tagged,
half-untagged elements left behind on undo.

### 13.7 GenerativeDesignBridge

`Core/Placement/GenerativeDesignBridge.cs`. The runtime hook the
`STING_FIXTURE_PLACEMENT.dyn` Dynamo / Generative Design study calls
when iterating placement variants. Exposes:

| Surface | What it does |
|---|---|
| `BridgeContext.LoadRules(scopeBox?)` | Reads Layer 1 → Layer 4 rules for the active project |
| `BridgeContext.RunVariant(weightOverrides)` | Runs the engine with perturbed scoring weights, returns `(coverage, spacingVariance, clearanceViolations)` for the Pareto front |
| `BridgeContext.CommitVariant(variantId)` | If GD picked a winner, applies it to the live model |

You don't usually drive the bridge by hand; it's the integration
point the `GD Study` toolbar button (§4.12) advertises.

---

## 14. Phase 139 deeper dive — the full v2 rule schema

The PlacementRule POCO grew from ~30 to ~70 fields. Cheat-sheet so
you know which field belongs in which group of the editor:

### 14.1 Identity & filter (Rule Core / Scoping cards)

| Field | Default | Plain English |
|---|---|---|
| `RuleId` | `""` | Stable identifier (use this in `DependsOn`, `CoPlaceWith`, `ConflictsWith`) |
| `RuleKind` | `Point` | `Point` / `Density` / `Linear` |
| `CategoryFilter` | `""` | Revit category name (must exist in active doc) |
| `VariantHint` | `""` | Comma-separated FamilySymbol hint (`FLUSH,SURFACE`) or regex (`^IP6[5-7]$`) |
| `FamilyTypeRegex` | `""` | Stricter regex on full Family.Symbol name |
| `RoomFilter` | `""` | Case-insensitive regex on Room.Name |
| `ExcludeRoomFilter` | `""` | Skip rooms matching this regex (negation overlay) |
| `RoomDepartmentFilter` | `""` | Match Room.Department |
| `MinAreaM2` / `MaxAreaM2` | `0` / `0` | Area gate (`0` = unbounded) |
| `LevelFilter` / `PhaseFilter` / `WorksetFilter` | `""` | Scoping filters |

### 14.2 Geometry & placement (Geometry card)

| Field | Default | Plain English |
|---|---|---|
| `AnchorType` | `ROOM_CENTRE` | One of the 42 anchors (legacy + Phase 139) |
| `MountingReference` | `FFL` | `FFL` / `SLAB` / `CEILING` / `SOFFIT` |
| `OffsetXMm` / `OffsetYMm` / `OffsetZMm` | `0` | Anchor-local offsets (mm) |
| `RotationDeg` | `0` | Spin about Z |
| `ToleranceMm` | `25` | Acceptable XYZ wobble before re-scoring |
| `MountingHeightMm` | `300` | Height above MountingReference |
| `SideConstraint` | `EITHER` | `EITHER` / `LEFT` / `RIGHT` / `FRONT` / `BACK` / `HINGE_SIDE` / `LATCH_SIDE` |
| `MinSpacingMm` | `1000` | Centre-to-centre minimum within rule |
| `MaxSpacingMm` | `0` | NEW v2 — max spacing for uniformity (`0` = no cap) |
| `MaxPerRoom` | `0` | Hard cap (`0` = unlimited) |

### 14.3 Density & linear (Rule Kind card)

| Field | Default | Plain English |
|---|---|---|
| `PerAreaM2` | `0` | "1 per X m²" |
| `PerOccupant` | `0` | "1 per X occupants" — reads `STING_OCC_COUNT_INT` |
| `PerLinearMetre` | `0` | "1 every X m of perimeter" |
| `PerBed` | `0` | NEW v2 — healthcare ratio (1 per X beds) |
| `PerWorkstation` | `0` | NEW v2 — office ratio |
| `PerPupil` | `0` | NEW v2 — schools ratio |
| `PerToiletCubicle` | `0` | NEW v2 — sanitary ratio (BS 6465) |
| `OccupancyParamName` | `""` | NEW v2 — read occupancy from a custom param rather than `STING_OCC_COUNT_INT` |

### 14.4 Dependencies (Dependencies card)

| Field | Plain English |
|---|---|
| `DependsOn` | RuleId of predecessor (this rule fires only after that one placed ≥1) |
| `RelativeTo` | `previous` / `first` / `self` |
| `CoPlaceWith` | List of RuleIds to fire alongside |
| `ConflictsWith` | List of RuleIds whose presence suppresses this rule |
| `Priority` | 0–100, higher wins ties |

### 14.5 Standards & profile (Standards card + project profile gate)

| Field | Default | Plain English |
|---|---|---|
| `StandardRef` | `""` | Free-text citation (`BS 5266-1 §5`, `Approved Doc M`, `HTM 02-01`) |
| `UniclassPr` | `""` | Uniclass 2015 product code |
| `Notes` | `""` | Free-text comment |
| `SourcePack` | `""` | NEW v2 — auto-populated from the JSON file the rule loaded from |
| `BuildingType` | `""` | NEW v2 — building-type scope (`Office`, `Hospital`, `School`, …); empty = any |
| `ApplicableStandards` | `[]` | NEW v2 — list of standard codes; project profile filters out non-matching rules |
| `IpRatingMin` | `""` | NEW v2 — minimum IP rating on the family (e.g. `IP65`) |

### 14.6 Coverage, clearance, accessibility (NEW v2 Coverage card)

| Field | Default | Plain English |
|---|---|---|
| `CoverageRadiusMm` | `0` | Radius the fixture covers (smoke detector 7500, MCP 22500, lit grid 4500) |
| `GuaranteeCoverage` | `false` | If true, accept candidates below score threshold rather than leave coverage gap |
| `WallClearanceMm` | `0` | Min distance to nearest wall |
| `ObstructionClearanceMm` | `0` | Min distance to nearest obstruction (queries `ObstructionIndex`) |
| `WetZoneExclusion` | `NONE` | `NONE` / `Z0` / `Z1` / `Z2` per BS 7671 §701 |
| `AccessibilityCheck` | `false` | If true, `AccessibilityAuditor` validates reach against `HeightStandardsTable` |
| `HeightStandard` | `""` | Specific row from `STING_HEIGHT_STANDARDS.json` (`BS8300_LIGHT_SWITCH`, `DOC_M_SOCKET`, …) |
| `MaintenanceClearance` | `""` | NEW v2 — string spec read by `MaintenanceAccessValidator` (`R600`, `1000x600x2000`) |

### 14.7 Routing (NEW v2 Routing card)

| Field | Default | Plain English |
|---|---|---|
| `RoutingMode` | `NONE` | `NONE` / `WALL_FOLLOWER` / `CEILING_PERIMETER` / `RISER` / `DROP` |
| `RouteOffsetMm` | `0` | Offset from the routing surface (e.g. 50 mm above a perimeter trunking) |
| `RouteFace` | `INTERIOR` | `INTERIOR` / `EXTERIOR` |
| `RouteMinBendRadiusMm` | `0` | Rejects candidates that would require a tighter bend |
| `RouteSegmentCategory` | `""` | Category used to draw the routing skeleton (Cable Tray, Conduit, Duct, Pipe) |

### 14.8 Window/glazing (NEW v2 Window card)

Used by the `windows-glazing` pack and any rule with `AnchorType = WINDOW_*`:

| Field | Plain English |
|---|---|
| `SillHeightMm` | Sill above FFL |
| `HeadHeightMm` | Head above FFL |
| `CillToFloorMm` | NEW v2 — distance cill-to-floor for child-safety check |
| `ToughenedGlazingRequired` | If true, validator checks Glazing.Type for "TGH" / "Toughened" |
| `GlazingSpec` | Free-text glazing call-up (`6 mm TGH/12 Ar/6 mm TGH soft-coat`) |

### 14.9 Post-placement audit (NEW v2 Audit card)

| Field | Plain English |
|---|---|
| `PostAuditTag` | Free-text tag attached to every placement so the Audit panel can group them (e.g. `"Phase-2 emergency lighting Q3"`) |
| `RequiresCOBieFields` | If true, validator confirms COBIE_* fields are populated before issue |
| `RequiresIfcMapping` | If true, validator confirms IFC_* mapping fields are populated before IFC export |

---

## 15. Phase 139 deeper dive — three more walk-throughs

### 15.1 *"Auto-route a perimeter cable tray around an office"*

1. Centre ▸ search `perimeter cable tray`. Pick `route-cable-tray-perimeter-office`.
2. Confirm:
   - `RuleKind = Linear`
   - `AnchorType = PERIMETER_OFFSET`
   - `RoutingMode = WALL_FOLLOWER`
   - `RouteOffsetMm = 50` (50 mm proud of the wall finish)
   - `RouteSegmentCategory = "Cable Tray"`
   - `RouteMinBendRadiusMm = 150`
   - `MountingReference = SOFFIT`
   - `MountingHeightMm = -100` (100 mm below the soffit)
3. Run Options ▸ Scope = Active view.
4. `Preview` → blue tray segments appear hugging every wall, breaking on doors and structural columns where the bend radius can't be honoured.
5. `Run Placement` → cable tray segments + tees + crosses + reducers + bends materialise as real `CableTraySegment` / `CableTrayFitting` instances.
6. `Validate` → `Connectivity` validator confirms every endpoint is joined; `Fill` validator confirms tray fill ≤ published BS 7671 capacity.

### 15.2 *"Toughened glazing where the cill-to-floor is below 800 mm"*

A pure post-placement audit, no new geometry:

1. Centre ▸ search `toughened glazing`. Pick `glaze-tgh-low-cill-audit`.
2. The rule has:
   - `RuleKind = Point` (not for placement; the `AuditOnly` flag in
     the JSON disables candidate generation)
   - `CillToFloorMm = 800` (the trigger threshold)
   - `ToughenedGlazingRequired = true`
   - `GlazingSpec = "6 mm TGH/16 Ar/6 mm TGH soft-coat"`
   - `PostAuditTag = "Approved Doc K Part N"`
3. `Validate` (Run Options ▸ Validators ▸ `Spec` ticked).
4. The `Spec` validator scans every Window in scope, reads its
   `Glazing.Type`, and reports any window with cill < 800 mm whose
   glazing is not toughened — citing Approved Doc K Part N.

### 15.3 *"Hospital ward pack — bedhead trunking + outlets + nurse call"*

A worked dependency chain across three packs:

1. Set the project profile:
   ```json
   { "BuildingType": "Hospital",
     "ApplicableStandards": ["HTM 02-01","HBN 04-01","BS 5839-1"] }
   ```
2. Open Centre. Status bar reports `~58 rules visible after profile filter`.
3. Confirm rule chain:
   ```
   med-gas-bedhead-trunking-01      ← parent (anchor WALL_MIDPOINT)
   med-gas-O2-outlet                ← CoPlaceWith=parent, OffsetX=0
   med-gas-VAC-outlet               ← CoPlaceWith=parent, OffsetX=150
   med-gas-N2O-outlet               ← CoPlaceWith=parent, OffsetX=300
   nurse-call-handset-01            ← DependsOn=parent, RelativeTo=previous, OffsetX=-450
   power-socket-bedhead-01          ← DependsOn=parent, RelativeTo=previous, OffsetX=-300, MountingHeightMm=300
   ```
4. Run Options ▸ Scope = Project; tick `Run validators after placement`.
5. `Preview` shows one teal cluster per bed bay.
6. `Run Placement`. Engine fires bedhead trunking first (~30 placements), then six co-placed/dependent rules in the same room — every bed gets its full bedhead kit in one transaction.
7. Result panel: `Placed: 210 (35 bedhead × 6 kit items)`. Every placement carries `PostAuditTag = "HTM 02-01 §6"` for the FM team's audit pack.

---

## 16. Phase 139 deeper dive — extended cheat-sheet

| Want to… | Click / set |
|---|---|
| See what's filtered by project profile | Status bar text — `260 rule(s); X hidden by profile` |
| Filter rule grid to one pack | Search box: `pack:medical-gases` |
| Filter to one standard | Search box: `std:BS 8300-2` |
| Bulk-edit 50 rules' MinSpacing | Toolbar ▸ Export to Excel… → edit → Import from Excel… |
| Run only the new validators | Run Options ▸ Validators ▸ untick everything except `Uniformity` + `Maintenance` |
| Disable wet-zone gating temporarily | Set rule `WetZoneExclusion = NONE` (do not delete the field — the validator still surfaces it as a warning) |
| Force coverage even if score is low | Set `GuaranteeCoverage = true` |
| Surface obstruction breaches without rejecting | Set `GuaranteeCoverage = true` and let `MaintenanceAccessValidator` flag them later |
| Run the engine inside a generative-design study | Toolbar ▸ GD Study ▸ open `STING_FIXTURE_PLACEMENT.dyn` |

---

## 17. Phase 139 deeper dive — common pitfalls

| Symptom | Likely cause | Fix |
|---|---|---|
| All rules suddenly hidden after editing the profile | `BuildingType` mismatch — your `Hospital` rule is hidden when project profile = `Office` | Either widen the rule's `BuildingTypes` list or change the project profile |
| Coverage validator says 100 % but visually I see gaps | `CoverageRadiusMm` lower than the standard radius | Lift `CoverageRadiusMm`; the validator measures against the rule's value, not the standard |
| Wet-zone exclusion fired in a non-wet room | The room name regex matched something unintended (e.g. `wash-up`) | Tighten the rule's `RoomFilter` |
| Excel import shows `SourcePack` blank for every row | The export was edited and `SourcePack` column was removed | Re-export, edit, re-import — keep all hidden columns |
| WallFollowerRouter fails on curved walls | Not yet implemented for arc walls | Use `LIGHTING_GRID` or `PERIMETER_OFFSET` instead, or split the arc wall into segments |
| Lighting calculator picks the wrong fixture | `VariantHint` resolves to a non-luminaire family | Restrict `CategoryFilter = "Lighting Fixtures"` and add a tighter `FamilyTypeRegex` |
| Post-placement hooks didn't fire | Toggles default OFF for performance; tick them in Run Options before Run | Run Options ▸ tick `Run data-tag pipeline on each placement` |

---

*v2 deep-dive complete. Sections 0–11 remain the everyday reference;
12–17 cover what changed and how to use it.*

---

## 18. Family-authoring guide — make every category 100 % placement-perfect

The Placement Centre is only as good as the families it places.
**A perfectly authored family resolves to one anchor, one orientation,
one host type, one weight, one envelope, one insertion XYZ — every
time.** A loosely authored family forces the engine to guess, and
guessing produces the off-by-50 mm misplacements that haunt the
co-ordination meeting.

This section is the **family author's contract** with the Centre.
Follow it, and the Centre lands every fixture exactly where the rule
says, with the right host, the right clearance and the right COBie /
IFC mapping. Skip a row, and you get warnings or silent fall-backs.

### 18.1 The mental model — why family authoring matters

Every placement is a five-step reconciliation between the **rule**
(what the standard says) and the **family** (what Revit can actually
produce):

1. The rule says *"PLACE light-switch on hinge side, 1100 mm AFF, 300 mm from door"*.
2. The engine asks the family: *"are you wall-hosted? what's your insertion point? how heavy are you? what variant? what clearance?"*.
3. The family answers via 7 `PLACE_*` parameters (read by `PlacementParamReader`) plus the legacy clearance / envelope / weight block.
4. `PlacementHostPreflight` picks the right `NewFamilyInstance` overload for the family's `FamilyPlacementType`.
5. The fixture lands. `PostPlacementHooks` (if enabled) tag, COBie-seed and connector-probe in the same transaction.

Steps 2-4 only succeed if the family is authored to the conventions
below. Otherwise the engine falls back to defaults — which work, but
not 100 %.

### 18.2 The seven `PLACE_*` placement-hint parameters

Bind these as **type parameters** on every family the Centre places.
They live in `MR_PARAMETERS.txt` group `PLACEMENT` with stable GUIDs
so they survive family migration. Read by `PlacementParamReader.Read`
(type wins; instance is fallback).

| Parameter | Type | Allowed values | What the engine does with it |
|---|---|---|---|
| `PLACE_HOST_TYPE_TXT` | TEXT | `WorkPlane` / `WallHosted` / `CeilingHosted` / `FaceBased` / `FloorHosted` / `Free` | `PlacementHostPreflight` picks the matching `NewFamilyInstance` overload. **Mismatched value = silent free-standing instance with null Host.** |
| `PLACE_MOUNT_HEIGHT_MM` | NUMBER | mm above FFL (or below ceiling per rule's `MountingReference`) | Default mounting height when no rule-level override is present (e.g. switch 1100, socket 300, MCP 1400) |
| `PLACE_SPACING_RULE_TXT` | TEXT | `Grid(Wmm,Hmm)` / `Perimeter(stepMm)` / `PerArea(1per10sqm)` / `WallPitch(stepMm)` / `Free` | Hints the auto-density mode when a rule has `RuleKind = Density` but no per-area value |
| `PLACE_ORIENTATION_RULE_TXT` | TEXT | `FaceAccessDoor` / `FaceNorth` / `FaceWallNormal` / `FaceRoomCentre` / `Free` | Engine rotates the placed instance so the family's "front" faces the named direction |
| `PLACE_LEVEL_HINT_TXT` | TEXT | Comma/semi/pipe-separated keywords with `*` wildcard (`Plant*`, `Roof`, `B*`) | Boosts `PlacementScorer` when the candidate's level matches; mismatch costs 0.4 |
| `PLACE_GROUP_KEY_TXT` | TEXT | Free-form id for grouped placements (`RCP-MODULE-01`, `BEDHEAD-KIT-A`) | Co-place rules use this to keep grouped fixtures together |
| `PLACE_WEIGHT_KG` | NUMBER | mass in kg | Triggers `MaintenanceAccessValidator` structural pre-flight + selects appropriate hanger family |

> **Layman tip:** the PLACE_ block is *what the family knows about
> itself*. The rule is *what the standard says*. The engine
> reconciles the two. Empty PLACE_ values mean "I have no opinion —
> trust the rule" and that is fine for most categories. Non-empty
> values lock the family to a specific behaviour and make placement
> deterministic.

### 18.3 The clearance / envelope / weight block

Eight more type parameters used by validators and by `Push to
Families` (toolbar §4.9). Author once per family-type; the Centre
writes the rule's values into them so all downstream tools (clash
detection, structural pre-flight, COBie export, generative design)
see one consistent number.

| Parameter | Type | Used by |
|---|---|---|
| `STING_CLEARANCE_MM` | NUMBER | Omnidirectional clearance (Maintenance + Clearance validators) |
| `STING_CLEARANCE_FRONT_MM` | NUMBER | Service-access clearance in front (AHUs, panels) |
| `STING_CLEARANCE_BACK_MM` | NUMBER | Behind clearance (cable bend radii) |
| `STING_CLEARANCE_SIDE_MM` | NUMBER | Side clearance (bend, door swing, neighbouring kit) |
| `STING_CLEARANCE_TOP_MM` | NUMBER | Overhead clearance (lifting, bird-cage scaffold) |
| `MNT_ENV_W_MM` / `_D_MM` / `_H_MM` | NUMBER | Mounting envelope (bounding box) — clash + routing |
| `FIRE_SEP_MM` | NUMBER | Distance to keep from fire-rated surfaces |
| `PLACE_WEIGHT_KG` | NUMBER | Same as §18.2 — duplicated here so structural pre-flight reads it without parsing PLACE_ block |

> **Layman tip:** even if you don't intend to use the Centre, fill in
> these parameters once per type. They are read by every other
> downstream tool. The Centre is just the easiest way to populate
> them for a whole library at once via Push to Families.

### 18.4 The variant-hint contract: `STING_FIXTURE_VARIANT_TXT`

A *type* parameter every placement-eligible family carries. The
Centre's `VariantHint` resolver walks every family type in the rule's
category, reads this parameter, and matches against the rule's
`VariantHint` chain or regex.

| Convention | Example value | Reason |
|---|---|---|
| Use UPPER-SNAKE | `FLUSH`, `SURFACE`, `RECESSED`, `IP65`, `EM`, `EM_IP65`, `DALI_DT8` | Stable text comparison |
| One concept per token | `IP65` not `IP65_OUTDOOR` (split into `IP65` + `OUTDOOR`) | Lets rules combine concepts orthogonally |
| Provide a sensible default | If the family has only one variant, set value to `STANDARD` | Empty means "untagged" — engine treats as wildcard |
| Name only what changes | The variant tag changes between types of the same family | Don't repeat brand / model / size — those are other parameters |

#### Common variant vocabulary (use these strings)

| Domain | Tokens |
|---|---|
| Mounting | `FLUSH`, `SURFACE`, `RECESSED`, `PENDANT`, `STEM`, `WALL`, `CEILING`, `FLOOR`, `DESK` |
| IP rating | `IP20`, `IP40`, `IP44`, `IP54`, `IP55`, `IP65`, `IP66`, `IP67`, `IP68` |
| Emergency | `EM`, `EM_3HR`, `EM_1HR`, `NEM` |
| Control | `DALI`, `DALI_DT6`, `DALI_DT8`, `KNX`, `0_10V`, `PHASE_DIM`, `MANUAL` |
| Safety | `ATEX_ZONE1`, `ATEX_ZONE2`, `EX_NA` |
| Healthcare | `MEDICAL`, `ANTIBAC`, `CLEAN_ROOM` |
| Acoustic | `ACOUSTIC`, `STD_ACOUSTIC` |
| Use a wildcard | `*` or empty | Wildcard tells the resolver "any variant accepts me" |

> **Layman tip:** if a rule says `VariantHint = FLUSH,SURFACE` and
> you author both flush and surface variants of the same family,
> tag them with `STING_FIXTURE_VARIANT_TXT = FLUSH` and `= SURFACE`
> respectively. The Centre will pick FLUSH first, fall back to
> SURFACE if no FLUSH variant is loaded.

### 18.5 Insertion point + reference plane conventions

The single most common source of "off by 50 mm" misplacement is a
family whose **insertion point is not where the rule expects**.

#### 18.5.1 Per family-template insertion convention

| FamilyPlacementType | Required insertion location | Reference plane name to use |
|---|---|---|
| `OneLevelBased` (free-standing) | Centroid of plan footprint, on FFL | `Centre (Front/Back)` × `Centre (Left/Right)` |
| `WallBased` | On the **wall face**, at the family centroid in elevation | Wall hosting plane is the back face by default — flip if back-to-wall fixtures need to mount with their *front* on the wall |
| `CeilingBased` | On the **ceiling face**, at the family centroid in plan | Ceiling hosting plane on top of the family |
| `FaceBased` (work-plane based) | Work-plane is the active face. Origin should be the *visible* centre when projected to plan | Use `Centre (Front/Back)` × `Centre (Left/Right)` aligned to plan view |
| `WorkPlaneBased` (non-hosted) | Free placement at the chosen work plane. Origin = centroid | Author against named ref planes so type swaps preserve origin |

#### 18.5.2 Origin-rule cheat sheet

| Family kind | Origin location | Why |
|---|---|---|
| Wall switch / socket | On wall face, at *electrical centre* (centre of the gang plate) | Rule offsets are measured from this point |
| Light switch (bank) | Back-box centre — NOT the centre of all gangs | Engine adds `OffsetXMm` from the back-box centre |
| Ceiling-mounted luminaire (recessed) | Centre of light aperture (not the housing) | The aperture is what aligns with the ceiling tile grid |
| Pendant luminaire | Stem top (where it joins the ceiling) — *not* the lamp centre | Rule mounting height is the stem-top elevation |
| Ceiling diffuser | Centre of the throat (face of suspended ceiling) | Throat alignment with ductwork is what matters |
| WC pan | Floor-level centre of the pan, at the back wall | BS 6465 measures from this point |
| Basin | Floor-level centre directly below the bowl centre | Reach (Approved Doc M) is measured from the bowl centre |
| Door | At the active leaf hinge, on the wall reference plane | Approved Doc M switch placement is "300 mm from hinge" |
| Window | Centre of the opening, at sill level | `WINDOW_SILL` anchors land here |
| Smoke detector | Centre of the head, on the soffit face | `CEILING_TILE_CORNER` snaps to this point |
| MCP / break-glass | Centre of the front face | BS 5839-1 height is to the centre of the unit |
| Fire extinguisher | Centre of bracket, on the wall face | BS 5306-8 travel is centre-to-centre |
| Sprinkler head | Centre of the deflector, on the soffit face | BS EN 12845 spacing measured deflector-to-deflector |

#### 18.5.3 Reference-plane discipline

Every family type the Centre touches must have **two named reference
planes** flagged `Defines Origin = Yes` perpendicular to each other:

```
Reference Plane "Origin Front-Back"   ← Defines Origin = Yes
Reference Plane "Origin Left-Right"   ← Defines Origin = Yes
```

If you ever swap the family from one template to another (rare but
happens during fabrication) the origin stays put because it is keyed
to the named planes, not to coincidental geometry.

### 18.6 The complete category × family-template matrix

For every Revit category the Centre places, this is the canonical
authoring recipe. Use it as a cookbook: pick the row, set the columns
verbatim, and the Centre lands the family with 100 % alignment.

| Category | FamilyPlacementType (`.rft` template) | `PLACE_HOST_TYPE_TXT` | Origin convention | Default `PLACE_MOUNT_HEIGHT_MM` | Common `STING_FIXTURE_VARIANT_TXT` | Default rule anchor | Notes |
|---|---|---|---|---|---|---|---|
| **Lighting Fixtures** (recessed) | Ceiling-based metric.rft | `CeilingHosted` | Centre of aperture | `0` (taken from ceiling) | `RECESSED,IP20,IP44,IP65` | `LIGHTING_GRID` or `CEILING_TILE_CORNER` | Aperture aligned to tile grid; lumen output on type param `LUMENS` |
| **Lighting Fixtures** (pendant) | Ceiling-based metric.rft | `CeilingHosted` | Stem top | `2400` (drop = ceiling–rule) | `PENDANT,STEM` | `ROOM_CENTRE` or `LIGHTING_GRID` | Stem length parameter drives lamp height, *not* PLACE_MOUNT_HEIGHT |
| **Lighting Fixtures** (wall) | Wall-based metric.rft | `WallHosted` | Wall face, centroid | `2200` | `WALL,IP44,IP65,EM` | `WALL_MIDPOINT` | EM batteries: tag with `EM` variant |
| **Lighting Devices** (switch) | Wall-based metric.rft | `WallHosted` | Centre of gang plate | `1100` | `FLUSH,SURFACE,GRID` | `DOOR_HINGE` | Approved Doc M: hinge side, 1100 mm AFF |
| **Lighting Devices** (occupancy sensor) | Ceiling-based metric.rft | `CeilingHosted` | Centre of head | `0` | `PIR,DUAL_TECH,DALI` | `CEILING_TILE_CORNER` | PIR coverage circle visible only at coarse detail |
| **Electrical Fixtures** (socket) | Wall-based metric.rft | `WallHosted` | Centre of gang plate | `300` | `FLUSH,SURFACE,FLOOR_BOX,IP65` | `WALL_MIDPOINT` or `PERIMETER_OFFSET` | Floor-box variant uses `FloorHosted` host type |
| **Electrical Fixtures** (data outlet) | Wall-based metric.rft | `WallHosted` | Centre of gang plate | `300` | `RJ45_CAT6,RJ45_CAT6A,FIBRE_LC` | `WALL_MIDPOINT` co-placed with socket | Use `PLACE_GROUP_KEY_TXT="DESK-OUTLET"` |
| **Electrical Equipment** (DB / MCB panel) | Wall-based metric.rft | `WallHosted` | Centre of cabinet | `1200` (top of cabinet at ~2.0 m) | `STD,RECESSED,IP44` | `WALL_CORNER` | Specify front clearance ≥ 800 mm via `STING_CLEARANCE_FRONT_MM` |
| **Electrical Equipment** (switchboard) | Free-standing metric.rft | `Free` | Centre of plinth | `0` | `STD,IP44,IP54` | `WALL_CORNER` | Set front clearance ≥ 1100 mm; back ≥ 600 mm |
| **Mechanical Equipment** (AHU) | Free-standing metric.rft | `Free` | Centre of unit base | `0` | `INDOOR,OUTDOOR,IP54` | `GRID_INTERSECTION` | Set front clearance to coil-pull length; weight ≥ unit dry mass |
| **Mechanical Equipment** (FCU) | Ceiling-based metric.rft | `CeilingHosted` | Centre of return | `0` | `EXPOSED,RECESSED,DUCTED` | `ROOM_CENTRE` or `CEILING_TILE_CORNER` | Top clearance for filter pull |
| **Air Terminals** (diffuser) | Ceiling-based metric.rft | `CeilingHosted` | Centre of throat | `0` | `4WAY,LINEAR,SLOT,EGGCRATE` | `CEILING_TILE_CORNER` | Throat aligns with tile grid; flow-rate via `FLOW_CFM` parameter |
| **Air Terminals** (extract grille) | Ceiling-based metric.rft | `CeilingHosted` | Centre of grille | `0` | `EGGCRATE,LINEAR,DISC` | `CEILING_TILE_CORNER` | – |
| **Plumbing Fixtures** (WC pan) | Wall-based metric.rft (or floor-based for back-to-wall) | `WallHosted` or `FloorHosted` | Floor-level centre | `0` | `FLOOR_MOUNTED,WALL_HUNG,DDA` | `WALL_CORNER` | DDA variant has different reach |
| **Plumbing Fixtures** (basin) | Wall-based metric.rft | `WallHosted` | Centre of bowl | `850` (rim height) | `WALL_HUNG,VANITY,DDA,SURGEON` | `OPPOSITE_WALL` | Approved Doc M reach is from bowl centre |
| **Plumbing Fixtures** (shower) | Floor-based metric.rft | `FloorHosted` | Centre of shower tray | `0` | `TRAY,WALK_IN,DDA` | `WALL_CORNER` | Wet-zone Z0 — never place sockets / switches here |
| **Plumbing Fixtures** (urinal) | Wall-based metric.rft | `WallHosted` | Centre of bowl | `650` (rim) | `BOWL,STALL,WATERLESS` | `WALL_MIDPOINT` | BS 6465 spacing 700 mm centre-to-centre |
| **Sprinklers** (pendant) | Ceiling-based metric.rft | `CeilingHosted` | Centre of deflector | `0` | `PENDANT,UPRIGHT,RECESSED,SIDEWALL` | `CEILING_TILE_CORNER` | BS EN 12845 — 4 m centres for OH1 |
| **Sprinklers** (sidewall) | Wall-based metric.rft | `WallHosted` | Centre of deflector | `2200` | `SIDEWALL,EXTENDED_COVERAGE` | `WALL_MIDPOINT` | – |
| **Fire Alarm Devices** (smoke detector) | Ceiling-based metric.rft | `CeilingHosted` | Centre of head | `0` | `OPTICAL,IONISATION,HEAT,MULTI` | `CEILING_TILE_CORNER` | BS 5839-1 7.5 m radius — set `CoverageRadiusMm = 7500` |
| **Fire Alarm Devices** (MCP) | Wall-based metric.rft | `WallHosted` | Centre of front face | `1400` | `STD,IP65,DDA` | `DOOR_STRIKE_SIDE` (Phase 139) | BS 5839-1 §22 height 1.4 m |
| **Fire Alarm Devices** (sounder) | Wall-based or ceiling-based | as template | Centre of grille | `2200` (wall) / `0` (ceiling) | `WALL,CEILING,VAD,VAS` | `WALL_MIDPOINT` | VAD = visual; VAS = combined sounder/visual |
| **Communication Devices** (CCTV) | Ceiling-based metric.rft | `CeilingHosted` | Centre of dome | `0` | `DOME,BULLET,PTZ` | `CEILING_TILE_CORNER` or `CORRIDOR_JUNCTION` (Phase 139) | Set rotation by `PLACE_ORIENTATION_RULE_TXT="FaceRoomCentre"` |
| **Security Devices** (PIR) | Wall-based or ceiling-based | as template | Centre of head | `2200` (wall) / `0` (ceiling) | `WALL,CEILING_360,DUAL_TECH` | `WALL_CORNER` | Coverage cone visible only at coarse detail |
| **Doors** | Door.rft | `WallHosted` | Hinge side, on wall ref plane | `0` (sill) | `STD,FE30,FE60,FE90,DDA` | n/a (built-in placement) | Family must expose hinge-side ref plane named `Hinge` so anchor `DOOR_HINGE` finds it |
| **Windows** | Window.rft | `WallHosted` | Centre of opening, at sill | reads `SillHeightMm` from rule | `STD,DG,TGH,FE30,LAMINATED` | n/a | Sill at 800 mm or higher: tag `STD`; below 800 mm: tag `TGH` |
| **Furniture** (desk) | Free-standing | `Free` | Centre of work surface | `0` | `LINEAR,L_SHAPE,BENCH` | n/a (placed by FF&E layout, not Centre) | Desk corner reference for socket co-placement |
| **Casework** (kitchen units) | Wall-based or floor-based | as template | Front centre, at base | `0` (base) / `2400` (wall) | `STD,SOFT_CLOSE,DDA` | `WALL_MIDPOINT` | DDA wall units: lower mounting height |
| **Specialty Equipment** (medical bedhead) | Wall-based metric.rft | `WallHosted` | Centre of bedhead, at bed-deck level | `1300` | `STD,HTM02,HTM01_5` | `WALL_MIDPOINT` | Co-place O₂ / VAC / N₂O / power / nurse-call |
| **Generic Models** (grab rail, blind motor, …) | Work-plane based | `WorkPlane` | Family centroid | varies | varies | varies | Default fall-back when no specific category fits |

### 18.7 Subcategory + visibility discipline

The Centre's `ObstructionIndex` and the validators all read element
geometry in plan view. That fails silently when a family hides its
geometry at the project's view scale. Author every family with these
visibility rules:

| Geometry layer | Subcategory | Visible at | Rationale |
|---|---|---|---|
| Symbolic 2D plan annotation | `<Family>_Symbol` | Coarse + Medium + Fine | Plan recognition at 1:200 |
| 3D housing / box | `<Family>_Housing` | Medium + Fine | Clash detection |
| 3D internals (lamp, transformer, coil) | `<Family>_Internals` | Fine only | Reduces clutter |
| Coverage / spread cone | `<Family>_Coverage` | Coarse only | Audit / commissioning |
| Connector points | (no subcategory) | always | Required for system traversal |

Turn off `Cut` for purely symbolic 2D representations — Centre's
plan-cut bounding-box reads can otherwise grab a stray symbol line and
misjudge the obstruction footprint.

### 18.8 Connector authoring (MEP families only)

Every MEP family the Centre places must have at least one connector
authored on the host plane:

| Domain | Connector type | Direction | Notes |
|---|---|---|---|
| Electrical | Electrical (Power / Data / Comms / FireAlarm / Security / Nurse) | In | One connector per circuit |
| Hydronic / Domestic / Sanitary | Pipe (Hydronic Supply, Hydronic Return, Domestic Cold, Domestic Hot, Sanitary, …) | In/Out per role | Flow direction matters for slope validation |
| Air | Duct (Supply Air / Return Air / Exhaust Air) | In/Out | Diffuser = In; grille = Out |
| Cable Tray / Conduit | Cable Tray / Conduit | none | Continuous run — connectors auto-snap |

`PostPlacementHooks.AssignMepSystem` (Run Options ▸ "Probe MEP
connectors after placement") records a warning if a placed instance
has unconnected connectors. Set the connector primary flag (one per
family) so `MEPSystemBuilder` can find the head of the system.

### 18.9 Pre-flight checklist (use before shipping a family)

Run through this list once per family-type before adding it to the
office library:

```
[ ] FamilyPlacementType matches PLACE_HOST_TYPE_TXT
[ ] Origin reference planes named + flagged "Defines Origin = Yes"
[ ] PLACE_HOST_TYPE_TXT  filled
[ ] PLACE_MOUNT_HEIGHT_MM filled (default for the category)
[ ] PLACE_SPACING_RULE_TXT filled if rule will use Density mode
[ ] PLACE_ORIENTATION_RULE_TXT filled if facing matters
[ ] PLACE_LEVEL_HINT_TXT  filled if family is level-restricted
[ ] PLACE_GROUP_KEY_TXT   filled if family is part of a kit
[ ] PLACE_WEIGHT_KG       filled (mass in kg)
[ ] STING_FIXTURE_VARIANT_TXT filled (one of §18.4 vocabulary)
[ ] STING_CLEARANCE_MM    filled
[ ] STING_CLEARANCE_FRONT_MM (if applicable)
[ ] STING_CLEARANCE_BACK / SIDE / TOP   (if applicable)
[ ] MNT_ENV_W/D/H_MM      (bounding envelope)
[ ] FIRE_SEP_MM           (if family interacts with fire-rated surfaces)
[ ] LUMENS                (if Lighting Fixture)
[ ] FLOW_CFM / _LPS       (if Air Terminal / Mechanical Equipment)
[ ] Connectors authored + primary flagged (MEP categories)
[ ] Subcategories per §18.7
[ ] Visibility rules per family detail level
```

Drop the family into a test project, run **DOCS ▸ Sheet Manager ▸
ISO Compliance** then **Placement Centre ▸ Validate**. Both should
report zero warnings on the family's category.

### 18.10 Worked example — author a 600 × 600 LED troffer

Cookbook walk-through. Should take ~10 minutes once you know the recipe.

1. **File ▸ New ▸ Family ▸ Lighting Fixture (ceiling-based metric).rft**.
2. Family Types → set:
   - `LUMENS` = `4000`
   - `STING_FIXTURE_VARIANT_TXT` = `RECESSED,IP20`
   - `PLACE_HOST_TYPE_TXT` = `CeilingHosted`
   - `PLACE_MOUNT_HEIGHT_MM` = `0` (taken from ceiling)
   - `PLACE_ORIENTATION_RULE_TXT` = `FaceWallNormal` (so troffers align long-axis with walls)
   - `PLACE_WEIGHT_KG` = `4`
   - `STING_CLEARANCE_MM` = `100`
   - `MNT_ENV_W_MM/D_MM/H_MM` = `595 / 595 / 80`
3. Author origin reference planes:
   - `Origin Front-Back` (Defines Origin = Yes)
   - `Origin Left-Right` (Defines Origin = Yes)
   - Origin sits at the **centre of the light aperture**, not the housing centre.
4. Subcategories:
   - `Lighting_Symbol` (visible Coarse / Medium / Fine, no cut)
   - `Lighting_Housing` (visible Medium / Fine, cut)
   - `Lighting_Internals` (visible Fine only)
5. Add an Electrical Power connector at the housing top, flagged primary.
6. Save as `STING_LIGHT_RECESSED_4000LM.rfa`, load into a test project.
7. Open the Centre. Pick rule `lighting-ceiling-recessed-office`. The
   resolver finds the new family because:
   - Category matches (`Lighting Fixtures`).
   - Variant `RECESSED` matches the rule's `VariantHint`.
8. `Preview` lays luminaires on the EN 12464-1 grid, `CeilingGridSnap`
   snaps each to the nearest tile corner, troffers orient long-axis
   along the room.
9. `Run Placement` → 100 % perfect alignment, every troffer flush
   with the ceiling tile, every primary connector ready for system
   wiring.

> **Layman tip:** if your placement is even slightly off — a 50 mm
> shift, a wrong rotation, a missing host — go back to §18.5 first.
> The origin is right 9 times out of 10. The other time it is the
> variant tag.

### 18.11 What goes wrong if you skip family authoring

A summary of the cost of each missing rule:

| Skipped row | Symptom |
|---|---|
| `PLACE_HOST_TYPE_TXT` empty | Engine guesses from family template; if template doesn't match rule's anchor, fixture lands free-standing with null Host |
| `PLACE_MOUNT_HEIGHT_MM` empty | Engine uses rule default; fine, but if rule itself is empty fixture lands at FFL = 0 |
| Origin reference plane unnamed | Type swap relocates the origin; placement drifts |
| `STING_FIXTURE_VARIANT_TXT` empty | Resolver picks alphabetical first family in the category; almost certainly wrong |
| `STING_CLEARANCE_MM` = 0 | Maintenance validator passes everything; service-tech can't actually reach the kit |
| Connectors missing | `PostPlacementHooks.AssignMepSystem` warns; `Connectivity` validator fails |
| Subcategory wrong | `ObstructionIndex` misreads bounding box; clash detection misses the family |
| `LUMENS` empty (Lighting Fixtures only) | `LightingGridCalculator` divides by zero, falls back to 1 luminaire per 25 m² (under-lit room) |

> **Layman version:** every empty row costs 10 minutes per project.
> Author the family once with §18.9's checklist and save 10 minutes
> on every subsequent project that loads it.
