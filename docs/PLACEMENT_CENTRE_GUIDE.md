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
