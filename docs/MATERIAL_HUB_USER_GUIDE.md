# Material Hub — User Guide

A complete, plain-English guide to every button, menu, and shortcut in the STING Material Hub. Read top-to-bottom for a first-time orientation, or use the table of contents to jump to the panel you're working in.

---

## What is the Material Hub?

The Material Hub is a single panel inside Revit that pulls together everything you need to do with project materials: review what's defined, see where each material is used, edit costs and carbon, apply textures and hatches, run quality gates, export to spreadsheets, and sync with COBie / schedules / external libraries.

It opens as a **dockable panel** — by default it docks to the right of the Revit drawing area, but you can drag it anywhere or float it on a second monitor. It stays open while you work in Revit; selections in the panel keep up with selections in the drawing.

**How to open it**: STING ribbon → MAT tab → Material Manager button, or run the `MaterialManager` command.

---

## Panel layout at a glance

```
┌─────────────────────────────────────────────────────────────────────┐
│ STING Material Hub  [ Search ]  [ Region ▾ ] [⟳] [⚙] [?]            │ ← Header bar
├─────────────────────────────────────────────────────────────────────┤
│ Materials  Σ Cost   Σ Carbon   EPD    Unused   Stale   Peers   ... │ ← KPI strip
├──────────┬──────────────────────────────────────────────┬───────────┤
│ NAVIGATE │ [filter chips strip]                         │ INSPECTOR │
│          │ ┌─ Grid of materials ─────────────────────┐ │   cards   │
│ ALL      │ │ Color State Name Class Origin Used … │ │           │
│ BY CLASS │ │  ...                                     │ │  Identity │
│ ISSUES   │ │  ...                                     │ │  Cost     │
│ PACKS    │ └──────────────────────────────────────────┘ │  Carbon   │
│          │                                                │  Appear. │
│          │                                                │  PBR Tex.│
│          │                                                │  Assets  │
│          │                                                │  Life... │
│          │                                                │  Actions │
├──────────┴──────────────────────────────────────────────┴───────────┤
│ FILE   LIBRARY   AUTOMATION   GATES   PIVOT   CONNECT   TEXTURES   │ ← Action bar
├─────────────────────────────────────────────────────────────────────┤
│ Status: Ready                                  [activity feed chips]│ ← Status bar
└─────────────────────────────────────────────────────────────────────┘
```

The layout has three resizable panes (drag the vertical splitters):
- **Left** — Navigate tree (faceted browsing)
- **Middle** — Filter chip bar + the data grid (all materials)
- **Right** — Inspector card stack (details for the selected material)

---

## Header bar (top)

| Control | What it does |
|---|---|
| **Search box** | Filters the grid in real time. Searches the material's name, class, manufacturer, EPD source, and Uniclass code. Clear the box to see all materials. |
| **Region ▾** | UK / EU / US / AU / Africa. Switches the currency symbol (£, €, $, etc.) and number formatting in the Cost column and KPI strip. Doesn't change material data — only how it's displayed. |
| **⟳ (Refresh)** | Re-reads materials from the Revit project. Use after creating new materials, renaming, or when the panel seems out of date. Same as pressing **F5**. |
| **⚙ (Settings)** | Opens panel preferences (under construction — currently shows a placeholder toast). |
| **? (Help)** | Pops a one-line tip: `F5 refresh · Ctrl+F search · right-click row for actions`. |

---

## KPI strip (top — project health snapshot)

Eight rolling counters refresh on every load and every edit. Click any KPI for a quick filter (where supported).

| Tile | What the number means |
|---|---|
| **Materials** | Total count of materials in the project. |
| **Σ Cost** | Sum of (supply + install + VAT) across every material, in the selected region's currency. Footer shows `N/M costed` — how many materials have a cost set. |
| **Σ Carbon** | Total embodied carbon in tCO₂e (tonnes CO₂-equivalent). Footer shows how many materials have a carbon factor. |
| **EPD** | Environmental Product Declaration freshness counts. `✓N` = fresh; `△N` = stale (older than recommended); `✗N` = expired; `—N` = missing entirely. |
| **Unused** | Count of materials defined in the project but not actually applied to any element. Candidates for purging. |
| **Stale** | (Placeholder for now.) Will count materials with stale tags / costs once the staleness detector ships. |
| **Peers** | (Placeholder for now.) Will show how many peer users have edited materials since your last refresh, in workshared projects. |
| **Coverage** | Percentage of elements that have a non-empty material assignment. Higher = better. |

---

## Left pane — NAVIGATE tree

A faceted browser. Click any leaf to filter the grid by that facet. Selected facets show up as **filter chips** above the grid; click the **×** on a chip to remove the filter. Multiple chips combine.

**ALL MATERIALS** — split by origin:
- **STING-origin** — materials whose name begins with `STING` (created by the STING toolkit).
- **BLE-origin** — materials whose name begins with `BLE_` (from the 815-row BLE library).
- **MEP-origin** — materials whose name begins with `MEP_` (from the 464-row MEP library).
- **Other** — anything else (legacy, Revit out-of-the-box, manually added).

**BY CLASS** — populated dynamically on refresh. Each material class (Concrete, Metal, Timber, Gypsum, Insulation, etc.) becomes a leaf with a count.

**ISSUES** — quick filters for common quality problems:
- **Unused** — count of materials applied to 0 elements.
- **Missing EPD** — materials with no Environmental Product Declaration.
- **Stale EPD** — materials whose EPD is older than the recommended refresh window.
- **Off-baseline** — materials that aren't part of the corporate library.

**PACKS** — populated dynamically. A "pack" is a curated subset of materials (e.g. a project palette, a discipline pack, a finish schedule). Click a pack to filter to its members.

---

## Middle pane — the data grid

One row per material. Columns left-to-right:

| Column | What it shows |
|---|---|
| **Color** | Coloured swatch of the material's surface colour. Visual at-a-glance check. |
| **State** | Lifecycle / health icon — bookmarked star, frozen lock, etc. |
| **Name** | Material name (read-only; rename via the Identity card). |
| **Class** | Concrete / Metal / Timber / Gypsum / etc. **Editable** — type a new class and press Enter. |
| **Origin** | STING · BLE · MEP · Other (read-only). |
| **Uniclass** | Uniclass 2015 product code (read-only). |
| **Used** | How many elements in the project use this material. |
| **Cost** | Total per-unit cost. **Editable** — click, type, press Enter. |
| **kgCO₂e** | Embodied carbon factor. **Editable** — same edit-then-Enter pattern. |
| **EPD** | Freshness badge — green ✓ (fresh), amber △ (stale), red ✗ (expired), dash — (missing). |

**Sorting**: Click any column header. Click again to reverse.

**Selecting**: Single-click selects one row → the inspector refreshes. Ctrl-click / Shift-click for multi-select (used by Apply to Selection, Compare, etc.).

**Editing a cell**: Press **Enter** or **Ctrl+E** to start editing; **Enter** commits, **Esc** cancels.

**Double-click**: Opens the native Revit Materials dialog for that material.

---

## Right pane — Inspector cards

The inspector rebuilds on every row selection. Cards top-to-bottom:

### 1. Identity card

| Row | Meaning |
|---|---|
| 64 px colour swatch | Live preview of the material's surface colour. |
| **Name** | Material name. |
| **Class** | Material class. |
| **Origin** | STING / BLE / MEP / Other. |
| **Uniclass** | Uniclass 2015 product code. |
| **Used** | Element count. |

### 2. Cost card

Sums per the active locale (region symbol from the header).

| Row | Meaning |
|---|---|
| **Supply** | Material purchase cost, per unit. |
| **Install** | Labour cost to install, per unit. |
| **VAT %** | Sales-tax percentage applied to the line. |
| **Total** | Computed: (Supply + Install) × (1 + VAT). |

### 3. Carbon (EPD) card

| Row | Meaning |
|---|---|
| **Factor** | Embodied carbon, in kg CO₂e per m³ (or per unit if specified). `(none)` = no factor set. |
| **EPD source** | Free-text — manufacturer, ICE database row, EN 15804 reference. |
| **EPD date** | Date the EPD was issued. |
| **Freshness banner** | `Fresh` (green), `Stale` (amber), `Expired` (red), `Missing` (grey). Calculated from EPD date vs. corporate refresh window. |

### 4. Appearance / Hatch card

Inline pickers for surface colour and the four hatch slots Revit exposes.

| Row | What it does |
|---|---|
| **Texture · Browse…** | Opens the legacy single-bitmap picker. For a full PBR pipeline use the **PBR Textures card** below instead. |
| **Surface FG · Pattern…** | Pick the surface foreground hatch (e.g. solid fill, brick pattern). |
| **Surface BG · Pattern…** | Pick the surface background hatch. |
| **Cut FG · Pattern…** | Pick the cut foreground hatch (shows when a wall/floor is sectioned). |
| **Color picker…** | Opens a colour picker bound to `Material.Color`. |
| **Reset hatch** | Wipes the Surface FG pattern back to none. |

### 5. PBR Textures card  *(new)*

The full Physically Based Rendering pipeline — applies up to 10 maps (base colour, normal, roughness, metalness, AO, bump, displacement, opacity, emission, anisotropy) to a single material.

**Schema pill (top of card)**
- `Prism (full PBR)` — green pill — the material uses the modern Autodesk Standard Surface schema and can hold all 10 maps.
- `Generic (lossy)` — amber pill — the material is on the legacy Generic schema. Roughness / Metalness / AO will be **dropped on apply**. You'll see two buttons:
  - **Convert in place** — mutates the material's appearance asset to a fresh Prism asset. If other materials share this asset, they're affected too.
  - **Duplicate → new** — creates a separate `<name> (PBR)` material on the new Prism asset, leaves the original alone.
  - **Choose differently…** — appears once you've made a choice. Resets STING's "remember my answer" so the prompt fires again next time.

**10-slot map grid**

Each row has the slot name, a preview pill showing the current file (or `—` if empty), and a **Map…** button that lets you pick a single image for that slot.

| Slot | Visible in |
|---|---|
| Base color | All view modes |
| Normal | All view modes |
| Roughness | Realistic + raytrace |
| Metalness | Realistic + raytrace |
| AO | Realistic + raytrace |
| Bump | Realistic + raytrace (fake relief) |
| Displacement | **Raytrace only** (true geometric offset, slow) |
| Opacity / cutout | All view modes |
| Emission | All view modes |
| Anisotropy | Realistic + raytrace (brushed metal, hair) |

**UV (real-world) controls**

| Field | What it does |
|---|---|
| **Scale X (mm)** | Real-world width of one tile of the texture, in millimetres. 1000 = 1 m. Smaller numbers = the texture appears smaller on the surface. |
| **Scale Y (mm)** | Same for height. |
| **Rotation (°)** | Rotation in degrees, applied to all maps together. |

Edit a value and tab/click away to save — the change is held in memory and will be written when you Re-apply or apply a new pack.

**Sliders**

| Slider | Range | What it does |
|---|---|---|
| **Bump amount** | 0 – 10 | Multiplier on the bump effect. 0 = flat, 1 = default, higher = exaggerated relief. |
| **Normal intensity** | 0 – 4 | How strongly the normal map perturbs surface normals. |

**Displacement (raytrace only) checkbox**

When the active pack carries a displacement map, the toggle is enabled and you can switch true geometric displacement on for raytraced render. **Slow** — adds 30–120 s per render frame. When the pack has no displacement map, the toggle is greyed out and labelled "no map in pack" so you know there's nothing to apply.

**Action bar (bottom of card)**

| Button | What it does |
|---|---|
| **Browse library…** | Opens the PBR Provider Browser (see next section). Pick a CC0 pack from Poly Haven or ambientCG, hit Download + Apply. |
| **Apply pack…** | Pick any file inside a pack folder on disk. STING reads the whole folder and applies what it finds. Use for packs you've manually downloaded (Architextures Pro, Megascans extracts, in-house libraries). |
| **Re-apply** | Re-applies the active pack to the selected material — handy after editing UV controls or sliders. |
| **Clear maps** | Disconnects bitmaps from every PBR slot of the selected material. Doesn't delete files — only the material's reference to them. |
| **Open folder** | Opens `_BIM_COORD/textures/` in Windows Explorer so you can drop a pack folder yourself. |

**Footer caveat**: *"Realistic view approximates PBR. Raytraced render shows true bump + displacement + AO."* — this is a fundamental Revit limitation; reach for raytraced rendering when you want to actually evaluate a material.

### 6. Assets card

Tells you whether the material's Appearance / Physical / Thermal assets are unique to this material or shared with others. Sharing is normal in well-organised projects.

| Row | What it means |
|---|---|
| **Appearance shared by** | Count of OTHER materials that use the same appearance asset. |
| **Physical shared by** | Count of others sharing the physical asset (density, modulus, etc.). |
| **Thermal shared by** | Count of others sharing the thermal asset (conductivity, specific heat). |
| **Detach** | Break the link so this material has its own private appearance asset. |
| **Repoint…** | Choose a different appearance asset from the project to point this material at. |

### 7. Lifecycle card

A four-pill row showing the material's review state. Click a pill to advance / change state.

`Draft` → `Reviewed` → `Approved` → `Frozen`

- **Draft** — being worked on; no audit weight.
- **Reviewed** — has had a peer review.
- **Approved** — signed off for use.
- **Frozen** — locked. STING's PBR pipeline and the BlockerChain will refuse to mutate Frozen materials (unfreeze first or duplicate).

### 8. Actions card

Quick buttons for the most common per-material operations:

| Button | What it does |
|---|---|
| **Apply → Sel** | Apply the selected material to every Revit element currently selected in the drawing. |
| **Eyedropper** | Click a face in the drawing → the panel highlights that face's material. |
| **Where Used** | List every element using this material; selects them in the drawing on click. |
| **Edit Identity** | Open the native Revit Material editor. |
| **Make Legend** | Generate a drafting view legend for materials grouped by class (Concrete / Metal / Timber / etc.) with filled-region swatches. |

---

## Action bar (bottom — project-wide operations)

Seven grouped sections. Each section title (FILE / LIBRARY / AUTOMATION / GATES / PIVOT / CONNECT / TEXTURES) is shown above its buttons.

### FILE

| Button | What it does |
|---|---|
| **Export CSV** | Dumps every material with its full property set (cost / carbon / uniclass / class / origin / usage count) to a CSV file. Pick a folder; STING writes `materials_<timestamp>.csv`. |
| **Import CSV…** | Pick a CSV; STING reconciles it against the project, updating costs / carbon / class / uniclass on existing materials. Doesn't create new materials. |
| **Open Template** | Opens the CSV template (the right column headers + an example row) so you can structure your own import file correctly. |
| **Audit Family…** | Pick a folder of `.rfa` family files; STING scans each one for materials and reports which families reference which library codes. |
| **Generate RFQ** | Builds a Request-for-Quote spreadsheet — one row per material with supply / install / VAT columns blank for the supplier to fill. |

### LIBRARY

| Button | What it does |
|---|---|
| **Edit Overrides** | Open the project-scoped override file for the material library (cost / EPD / uniclass values that differ from the corporate baseline). |
| **Reload** | Re-read the corporate `BLE_MATERIALS.csv` + `MEP_MATERIALS.csv` from disk and apply any updates to project materials. Use after a corporate refresh. |
| **Push Corporate** | Push the selected material's identity (cost / EPD / uniclass) back up to the corporate library. Requires write access — usually restricted to the BIM-manager role. |
| **Load Pack…** | Pick a JSON pack file (a curated bundle of materials). STING creates any missing materials in the project. |
| **Normalise Classes** | Run the class-normaliser — translates loose class names ("conc.", "concrete", "CONCRETE") to the canonical form. |

### AUTOMATION

| Button | What it does |
|---|---|
| **⚙ Auto-Apply** | Toggle. When ON, STING watches for newly placed Revit elements and assigns the most likely material from your library (based on category + family rules). |
| **⚙ Auto-Fill** | Toggle. When ON, STING fills missing cost / carbon / class fields as soon as you create a material, using the corporate baseline as the source. |
| **Edit Rules** | Open the auto-apply / auto-fill rule editor. |
| **Rebuild Caches** | Drop and rebuild STING's internal indexes (material-name cache, usage index). Use after a heavy edit session if the panel feels stale. |

### GATES (quality checks)

Each gate scans the project and reports findings of three severities: **Info** (green, FYI), **Warning** (amber, should fix), **Block** (red, must fix before shipping). Findings appear in a list dialog with click-to-jump-to-element.

| Button | What it checks |
|---|---|
| **Coverage** | Are there elements without a material? Are there material assignments to elements that shouldn't have them? |
| **Sustainability** | Embodied carbon thresholds (BREEAM / RIBA 2030 targets), Environmental Product Declaration freshness, missing factors. |
| **Healthcare** | Healthcare-specific rules — HTM 03-01 ventilation surfaces, HTM 05-02 fire compartmentation walls, anti-microbial coatings on clinical surfaces. |
| **Fire-Wall** | Fire-rated wall compositions — every layer in a 60/90/120 min wall must have its rating in the material spec. |
| **EPD Format** | Schema check on the EPD source string — is it a recognisable reference (ICE / manufacturer EPD / EN 15804 ID)? |

### PIVOT

| Button | What it does |
|---|---|
| **BOQ by Material** | Generate a Bill of Quantities pivot — one row per material with total quantity, cost, carbon, summed across the whole project. |
| **What-If…** | Open the what-if swap dialog — pick a "from" material and a "to" material, see what changes in cost / carbon / coverage if you swap project-wide. |
| **Carbon by Phase** | Pivot embodied carbon by Revit construction phase. Useful for stage-of-works reporting. |

### CONNECT

| Button | What it does |
|---|---|
| **Sync COBie** | Push material data to the COBie export — fills the Type sheet's Material column, the Manufacturer column, the Warranty fields where known. |
| **Linked Materials** | Scan Revit linked models for materials and report which ones are missing from the host. |
| **Enrich Schedules** | Look for existing Revit material schedules and add STING-known columns (cost, carbon, uniclass) where they're absent. |
| **Make Legend** | Same as the Inspector Action button — drafting-view legend grouped by class. |

### TEXTURES *(new)*

| Button | What it does |
|---|---|
| **Browse Library…** | Same as Inspector → Browse library. Opens the PBR Provider Browser (4 providers: Poly Haven, ambientCG, Architextures Pro, user-supplied folder). |
| **Bulk Apply** | Walk every pack folder under `_BIM_COORD/textures/` and apply each one to the project material whose name matches the folder name (or the closest fuzzy match). |
| **Apply Pack…** | Same as Inspector → Apply pack. Folder picker for a specific pack. |
| **Reload Providers** | Re-read `STING_TEXTURE_PROVIDERS.json` after you've edited it on disk. Use after dropping a new provider into the project-scoped `_BIM_COORD/texture_providers.json` override. |

---

## Right-click context menu (in the grid)

Right-click any row for quick access to the most common operations without leaving the grid:

- **Apply to Selection** → `MAT_Apply`
- **Where Used** → `MAT_WhereUsed`
- **Eyedropper from face** → `MAT_Eyedropper`
- (separator)
- **Edit Identity** → `MAT_EditIdentity`
- **Detach asset** → `MAT_DetachAsset`
- **Repoint asset to…** → `MAT_RepointAsset`
- (separator)
- **Add to pack…** → `HUB_AddToPack` (placeholder — coming)
- **Bookmark** → `HUB_Bookmark` — toggle a star on the row for quick re-find.
- **Compare with selected** → `HUB_Compare` (placeholder — coming)

---

## Keyboard shortcuts

| Key | What it does |
|---|---|
| **F5** | Refresh the panel — re-read every material from the project. |
| **Ctrl+F** | Move focus to the header Search box. |
| **Ctrl+E** | Start editing the currently-selected cell. |
| **Enter** | Commit a cell edit (Cost / kgCO₂e / Class). |
| **Esc** | Cancel a cell edit. |
| **Double-click** | Open the native Revit Materials dialog for the row. |

---

## Status bar (bottom)

| Element | Meaning |
|---|---|
| **Left status text** | What STING is doing right now — `Ready`, `Loading 1,247 materials…`, `PBR applied to Concrete C30/37`, etc. |
| **Activity feed chips** (right side) | The last 5 material events in this session — each chip shows `<kind>·<material>`. Click any chip to jump to that material in the grid. Mouse-hover shows time + description. New events appear instantly when actions complete (PBR applies, hatch changes, conversions, etc.). |

---

## PBR Provider Browser (the modal that opens from "Browse Library…")

This is its own dialog with its own surfaces. Worth its own section because it's the gateway to the texture libraries.

```
┌─────────────────────────────────────────────────────────────────┐
│ STING PBR Texture Library                                       │
├─────────────────────────────────────────────────────────────────┤
│ Search: [...........]  Category: [▾]  Resolution: [2k▾]  Format: [png▾]  [Search]  [Open provider site]
├──────────────┬──────────────────────────────────────────────────┤
│ PROVIDERS    │                                                  │
│              │  ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐              │
│ Poly Haven   │  │thmb│ │thmb│ │thmb│ │thmb│ │thmb│              │
│ CC0 · free   │  └────┘ └────┘ └────┘ └────┘ └────┘              │
│              │                                                  │
│ ambientCG    │  (thumbnail grid scrolls)                        │
│ CC0 · free   │                                                  │
│              │                                                  │
│ Architext..  │                                                  │
│ Sub · $55/yr │                                                  │
│              │                                                  │
│ User folder  │                                                  │
│ Local drops  │                                                  │
├──────────────┴──────────────────────────────────────────────────┤
│ Status: 142 assets · ambientCG                                  │
├─────────────────────────────────────────────────────────────────┤
│                              [Download + Apply]  [Cancel]       │
└─────────────────────────────────────────────────────────────────┘
```

### Provider rail (left)

Click a provider to switch its asset list into the centre grid.

| Provider | License | What clicking it does |
|---|---|---|
| **Poly Haven** | CC0 (free, no attribution) | Fetches the asset list via the Poly Haven API. Thumbnails lazy-load. Pick + Download = the pack downloads to `_BIM_COORD/textures/polyhaven/<asset_id>/`. |
| **ambientCG** | CC0 (free, no attribution) | Fetches via ambientCG's `full_json` endpoint. Download = pulls one zip and unzips it. |
| **Architextures Pro** | Subscription ($55/yr) | No inline browsing — the status area tells you to click **Open provider site**, then drop the manually-downloaded pack into `_BIM_COORD/textures/architextures/` and use the inspector's **Apply pack…** button. |
| **User-supplied folder** | Varies | Walks `_BIM_COORD/textures/` and surfaces every pack folder you've already dropped. Use for Megascans, Substance exports, in-house libraries. |

### Toolbar (top)

| Control | What it does |
|---|---|
| **Search** | Free-text filter on asset name + tags + category. |
| **Category ▾** | Per-provider categories (concrete, wood, metal, fabric, stone, etc.). `(all categories)` = no filter. |
| **Resolution ▾** | 1k / 2k / 4k / 8k. Tells the provider client which size to download. 2k is a good default for most uses; 4k+ for hero shots. |
| **Format ▾** | png / jpg / exr. png is the safest default. |
| **Search** (button) | Re-runs the asset query with the current filters. |
| **Open provider site** | Launches the active provider's homepage in your default browser. Use to authenticate paid providers or hand-pick assets the API doesn't surface. |

### Thumbnail grid (centre)

Each tile shows the asset thumbnail + display name. Click a tile to select it (border turns blue). The status line at the bottom shows the picked asset's name, provider, license, and resolution.

### Buttons (bottom)

- **Download + Apply** — Downloads the pack to `_BIM_COORD/textures/<provider>/<asset>/`, runs the suffix-detection ingester to figure out which file goes in which slot, then applies the resulting pack to the material that was selected in the Material Hub when you opened the browser. Long packs (4K+) take 10–60 seconds. The button greys out while the download is in flight; close the window to cancel.
- **Cancel** — Close without applying.

---

## How a few common workflows actually play out

### "I want to colour-code structural materials by carbon factor"
1. Filter the grid via **ISSUES → Missing EPD** to see which materials need data.
2. Edit the **kgCO₂e** column inline for each (Enter to commit).
3. Use **PIVOT → Carbon by Phase** to see the project-wide picture.
4. Use **GATES → Sustainability** to verify you're under your RIBA 2030 target.

### "I want photorealistic walls for a render"
1. Select the wall material in the grid.
2. Inspector → **PBR Textures** card → **Browse library…**.
3. Pick a 4k pack from Poly Haven (e.g. "concrete_floor_worn_001").
4. Click **Download + Apply**. STING handles Generic→Prism conversion if needed.
5. Tune UV Scale (mm) so the pattern reads at the right size.
6. Toggle **Displacement** on for hero shots → render in raytraced mode.

### "I want to swap one finish for another project-wide"
1. Action bar → **PIVOT → What-If…**.
2. Pick the "from" material and "to" material.
3. Review the cost / carbon delta.
4. Confirm → STING reassigns every element and reports the count.

### "Material I just created looks washed out in Realistic view"
- The Realistic view in Revit is an approximation. PBR effects (true metalness, displacement, AO) only show in **raytraced render**. Switch the view's Visual Style to Raytraced and look again. The footer caveat on the PBR card reminds you of this.

### "I need to audit a vendor's family folder"
1. Action bar → **FILE → Audit Family…**.
2. Pick the folder.
3. STING scans every `.rfa` and produces a CSV: family name × material name × library origin.
4. Use the output to spot vendor families that reference materials you don't have in the corporate library.

### "Bulk-apply a folder of PBR packs to many materials at once"
1. Drop your pack folders into `<project>/_BIM_COORD/textures/`. Folder names should match Revit material names (case-insensitive; light fuzzy match for spaces / underscores).
2. Action bar → **TEXTURES → Bulk Apply**.
3. STING walks every pack, matches by name, applies. Reports `applied / skipped / blocked / failed` counts.
4. Materials that are Frozen lifecycle state or that healthcare gates block on are surfaced in the "Blocked" count — fix and re-run.

---

## Things you can do that don't have a button

These are surface-area behaviours worth knowing about:

- **The panel auto-refreshes its activity feed** whenever any STING action mutates a material. You don't have to press F5 to see the chip appear.
- **Drag the splitters** between the three panes to make navigation / grid / inspector wider or narrower. The split survives panel hide / show.
- **The header search filters the grid live** — no Enter required. Clear the box to release.
- **Filter chips combine** — picking "BY CLASS → Concrete" then "ISSUES → Missing EPD" shows only concrete materials without an EPD.
- **Cost edits commit on Enter or focus loss** — you don't have to confirm anywhere.
- **A failed action toasts an explanation** at the bottom-left status bar. Hover the activity-feed chip for full detail.
- **The KPI strip updates live** after every edit so you can watch coverage / cost / carbon rise and fall as you work.

---

## Per-project configuration files

| File (under `<project>/_BIM_COORD/`) | What it does |
|---|---|
| `texture_providers.json` | Project-scoped overrides to the corporate PBR provider catalogue. Add in-house libraries, custom suffix rules, alternative providers. Edit, then click **TEXTURES → Reload Providers**. Master Setup Step 21 seeds a commented template. |
| `textures/` | Drop folder for PBR packs. Each sub-folder is one pack; STING auto-detects PBR maps by suffix. **Not source-controlled.** |
| `audit_log_*.jsonl` | Append-only, tamper-evident log of every material change (PBR applies, conversions, hatch / colour edits, lifecycle transitions). One line per event. SHA-256 chained. |
| Material override JSON | Project-specific cost / EPD / class overrides on top of the corporate library. Edit via Action bar → **LIBRARY → Edit Overrides**. |

---

## Glossary

| Term | Meaning |
|---|---|
| **Appearance asset** | The Revit element that holds a material's visual properties (colour, texture, bump, roughness). Multiple materials can share one appearance asset. |
| **BLE** | Building Lifecycle Elements — STING's 815-material library covering walls, floors, roofs, ceilings. |
| **CC0** | Creative Commons Zero — no rights reserved. The CC0 packs from Poly Haven and ambientCG are usable commercially without attribution. |
| **COBie** | Construction-Operations Building information exchange — the standard FM handover format. |
| **EPD** | Environmental Product Declaration — a verified statement of a product's environmental impact. |
| **Generic schema** | The older Revit appearance schema. Holds diffuse + bump only. STING's PBR pipeline can convert it to Prism. |
| **Hatch** | Fill pattern shown on a material's cut and surface representations in plan / section. |
| **MEP** | Mechanical · Electrical · Plumbing — STING's 464-material library covering services. |
| **PBR** | Physically Based Rendering — the modern multi-map appearance model (base colour + normal + roughness + metalness + AO + bump + displacement + opacity + emission + anisotropy). |
| **Prism schema** | The modern Revit appearance schema, aka Autodesk Standard Surface. Holds all 10 PBR slots. |
| **Uniclass 2015** | UK construction classification system; the "Pr_25_..." codes you see in the Uniclass column. |
| **Raytraced render** | Revit's high-quality render mode. The only mode that shows true PBR effects (bump + displacement + AO + true metalness). |
