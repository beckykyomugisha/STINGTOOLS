# Placement Centre — Review of Flexibility, Functionality & Automation Gaps

> **Looking for the user guide?** This document is a developer-facing gap audit.
> The plain-English walk-through for end users lives at [`PLACEMENT_CENTRE_GUIDE.md`](PLACEMENT_CENTRE_GUIDE.md).

> Review date: 2026-04-25 · Branch: `claude/placement-centre-review-cKDOD`
>
> Scope: `StingTools/UI/PlacementCenter/*`, `StingTools/Core/Placement/*`,
> `StingTools/Commands/Placement/*`, `StingTools/Data/Placement/*`,
> `StingTools/Data/Schemas/STING_PLACEMENT_RULES.schema.json`,
> ancillary `Commands/PlacementExt/PlacementExtCommands.cs`,
> `Data/LUX_TARGETS_EN12464.csv`, `Data/ROOM_TYPE_CLASSIFIER.csv`.
>
> Out of scope: routing engines (`Core/Routing/*`), validators
> (`Core/Validation/*`), AVF heat-map, hanger placement, sleeve placement,
> structural placement engines.

---

## 1. Snapshot of the as-shipped Centre

| Surface | Today's behaviour |
|---|---|
| Toolbar | Reload Defaults · Import · Export · Save Project · Run Placement · Preview · Validate · Undo last run · Push to Families · Heat-map · Save view preset · GD Study · Close |
| Left rail | Filterable rule grid (search · `● Dirty` chip · `✗ Invalid` chip · context menu: Clone / Delete / Select invalid / Select dirty) |
| Rule Core | CategoryFilter (editable combo) · VariantHint · RoomFilter (regex) · AnchorType · Side · Priority |
| Geometry | OffsetXMm · MountingHeightMm · MinSpacingMm · MaxPerRoom |
| Notes | Free-text |
| Family Defaults & Clearance | Read-only grid via `FamilyHintsBridge.Inspect` (24 PLACE_*/STING_*/MNT_*/CLASH_*/FIRE_SEP params, type-source preferred over instance) |
| Run Options | Stamp provenance · Honour learned offsets · Run validators · Auto-paint AVF heat-map · Scope (ActiveView / Selection / Project) |
| History & Provenance | Last 30 hourly buckets from `StingProvenanceSchema`, double-click to select |
| Persistence | `<project>/STING_PLACEMENT_RULES.project.json` (ES counterpart deferred) |
| Engine | `FixturePlacementEngine.PlaceFixturesInScope` + `PlacementScorer` (5-axis composite) + `PlacementHostPreflight` (level-based / hosted overload picker) |
| Data | 43 shipped rules across 14 categories in `STING_PLACEMENT_RULES.json` |


## 2. Category coverage — what's shipped vs what Revit has

### 2.1 Currently covered (14 categories, 43 rules)

| # | Revit category | Rules | Anchor flavours used |
|---|---|---:|---|
| 1 | **Air Terminals** (`OST_DuctTerminal`) | 3 | CEILING_CENTRE |
| 2 | **Communication Devices** (`OST_CommunicationDevices`) | 2 | WALL_MIDPOINT |
| 3 | **Data Devices** (`OST_DataDevices`) | 5 | WALL_MIDPOINT · ROOM_CENTRE · WALL_CORNER |
| 4 | **Electrical Equipment** (`OST_ElectricalEquipment`) | 1 | WALL_MIDPOINT |
| 5 | **Electrical Fixtures** (`OST_ElectricalFixtures`) | 6 | WALL_MIDPOINT · WALL_CORNER |
| 6 | **Fire Alarm Devices** (`OST_FireAlarmDevices`) | 4 | CEILING_CENTRE · DOOR_JAMB |
| 7 | **Lighting Devices** (`OST_LightingDevices`) | 3 | DOOR_HINGE |
| 8 | **Lighting Fixtures** (`OST_LightingFixtures`) | 4 | CEILING_CENTRE · DOOR_JAMB |
| 9 | **Mechanical Equipment** (`OST_MechanicalEquipment`) | 1 | WALL_CORNER |
| 10 | **Nurse Call Devices** (`OST_NurseCallDevices`) | 2 | WALL_MIDPOINT · WALL_CORNER |
| 11 | **Plumbing Fixtures** (`OST_PlumbingFixtures`) | 5 | WALL_MIDPOINT · WALL_CORNER |
| 12 | **Security Devices** (`OST_SecurityDevices`) | 4 | CEILING_CENTRE · DOOR_JAMB · WALL_MIDPOINT |
| 13 | **Specialty Equipment** (`OST_SpecialityEquipment`) | 1 | WALL_MIDPOINT |
| 14 | **Sprinklers** (`OST_Sprinklers`) | 2 | CEILING_CENTRE |

### 2.2 Categories the Centre **should** support (placement-relevant gaps)

The Centre is hard-coded to fixture-style families today, but every
category below is routinely placed in real BIM coordination workflows
and has clear standard-driven rules. Each row shows the category name as
it appears in Revit (suitable for `CategoryFilter`), the
`BuiltInCategory` enum, and the rule-class the Centre would unlock.

#### Architecture & FF&E
| Category name | Enum | Why it belongs in the Centre |
|---|---|---|
| Doors | `OST_Doors` | Door-set placement by room type (Approved Doc M door widths, fire ratings); accessible thresholds; opposite-wall WC layouts. |
| Windows | `OST_Windows` | Cill height by room (BS 8233 daylight, residential vs office), façade rhythm, trickle-vent count per room (Part F). |
| Furniture | `OST_Furniture` | Desk/chair/locker placement by room type (BS 6396 ergonomic spacing, BCO desk pitch 1.6 m, BS 8300 turning circles). |
| Furniture Systems | `OST_FurnitureSystems` | Open-plan workstation pods — pitch + back-to-back clearance. |
| Casework | `OST_Casework` | Kitchen base/wall units, lab bench modules, nurse station counters, classroom storage (BB103). |
| Generic Models | `OST_GenericModel` | Coat hooks, pinboards, signage, dispensers, bedhead trunking — unhosted family bin used by most projects. |
| Specialty Equipment | `OST_SpecialityEquipment` | Already covered by 1 kitchen rule; needs coverage for AV/IT, gym, lab autoclaves, dental chairs, etc. |
| Planting | `OST_Planting` | Biophilic-design planters per room type (WELL v2 N04). |
| Site | `OST_Site` | External bins, bollards, benches — corridor/external rules. |
| Parking | `OST_Parking` | Accessible-bay quotas (BS 8300 6%, EV bay ratios — Building Regs 2022). |
| Stairs | `OST_Stairs` | Hand-rail extensions, nosing strips (Doc K). |
| Railings | `OST_StairsRailing` | Handrail returns, balcony railings (BS 6180). |
| Curtain Panels | `OST_CurtainWallPanels` | Vision/spandrel pattern by elevation rule. |

#### Mechanical / HVAC
| Category name | Enum | Why it belongs in the Centre |
|---|---|---|
| Duct Accessories | `OST_DuctAccessory` | VCDs, fire/smoke dampers (BS EN 1366-2 in fire compartment walls), attenuators, terminal boxes — placement is *rule-driven*, not routed. |
| Mechanical Control Devices | `OST_MechanicalControlDevices` | Thermostats, humidistats — BS 8300 reach height 1200 mm; one per zone. |
| Plumbing Equipment | `OST_PlumbingEquipment` | Hot-water cylinders, expansion vessels, calorifiers — plant-room corner anchor with service zone. |
| HVAC Zones | `OST_HVAC_Zones` | Zone seed-points used to drive VAV/FCU placement (rule consumes zone, not the zone object). |

#### Electrical / Comms
| Category name | Enum | Why it belongs in the Centre |
|---|---|---|
| Telephone Devices | `OST_TelephoneDevices` | Legacy POTS handsets in healthcare / retail (BS 6259). |
| Cable Trays | `OST_CableTray` | Routed today, but *cable-tray drop point* placement (riser to corridor) is a distinct rule class. |

#### Fire & Life-Safety
| Category name | Enum | Why it belongs in the Centre |
|---|---|---|
| Fire Protection | `OST_FireProtection` | Extinguishers (BS 5306-8: 30 m travel for class A), fire blankets, hose reels, dry-riser inlets — already partly covered by `PlacementExtCommands.FireExtinguisherCommand` but as a one-shot dialog, not a Centre rule. |

#### Healthcare & Education
| Category name | Enum | Why it belongs in the Centre |
|---|---|---|
| Medical Equipment | typically `OST_SpecialityEquipment` | HTM 08-03 fixtures: bed-head trunking, IV poles, ceiling pendants, oxygen outlets — needs a *room-type-aware* rule class. |
| AV / Lectern | typically `OST_SpecialityEquipment` | BB103 classroom: lectern, document camera, IFP wall-plate. |

### 2.3 Categories the Centre should **not** target

| Category | Enum | Reason |
|---|---|---|
| Walls / Floors / Ceilings / Roofs | `OST_Walls`, `OST_Floors`, `OST_Ceilings`, `OST_Roofs` | Geometry hosts, not placement targets. The Centre uses them as *anchors*, not subjects. |
| Ducts / Pipes / Conduits | `OST_DuctCurves`, `OST_PipeCurves`, `OST_Conduit` | Belong in the routing engines (`Core/Routing/*`). |
| Duct/Pipe Fittings | `OST_DuctFitting`, `OST_PipeFitting` | Generated by the routing engine — placement is implicit. |
| Cable Tray Fittings, Conduit Fittings | `OST_CableTrayFitting`, `OST_ConduitFitting` | Same as above. |
| Structural Framing / Columns | `OST_StructuralFraming`, `OST_StructuralColumns` | Owned by `StructuralModelingCommands` + `StructuralDWGEngine`. |
| Mass / Topography / Roads | `OST_Mass`, `OST_Topography`, `OST_Roads` | Conceptual / site geometry, not the Centre's remit. |
| Annotation / View categories | `OST_Tags`, `OST_Sections`, etc. | Tag placement lives in `SmartTagPlacementCommand` + `TagPlacementEngine`. |

---

## 3. Schema vs implementation drift

Three concrete inconsistencies between the JSON schema, the rule POCO,
and the engine — every one will silently be wrong if a user opens the
file in an IDE that honours the `$id` schema.

### 3.1 Anchor enum disagrees in three places
| Layer | Anchor list |
|---|---|
| `STING_PLACEMENT_RULES.schema.json` | `ROOM_CENTRE`, `WALL_CENTRELINE`, `DOOR_OPP_WALL`, `RCP_GRID`, `FLOOR_GRID`, `USER_PICK` |
| `PlacementRule.AnchorType` doc-comment | `DOOR_HINGE`, `ROOM_CENTRE`, `WALL_MIDPOINT`, `CEILING_CENTRE`, `WALL_CORNER`, `DOOR_JAMB`, `WINDOW_SILL`, `ROOM_CENTROID` |
| `PlacementScorer.GenerateAnchorPoints` switch | `ROOM_CENTRE`, `ROOM_CENTROID`, `CEILING_CENTRE`, `LIGHTING_GRID`, `LUX_GRID`, `EN12464`, `WALL_MIDPOINT`, `WALL_CORNER`, `DOOR_HINGE`, `DOOR_JAMB`, `WINDOW_SILL` |
| `PlacementRulesViewModel.AnchorTypes` | matches the scorer (good — UI surfaces the set the engine actually understands) |

**Fix**: rewrite `STING_PLACEMENT_RULES.schema.json` against the scorer
list. Keep the schema as the source of truth and add a build-time
generator so the VM, scorer switch, and schema can never drift again.

### 3.2 Priority range
- Schema: `minimum 0, maximum 10`
- VM: `0..100`
- Shipped JSON: 50, 55, 60, 65, 70, 75, 80
- Centre tooltip: `Priority (0–100)`

**Fix**: schema must be `0..100`; rule POCO already defaults to 50.

### 3.3 PascalCase vs camelCase JSON keys
- Schema declares `categoryFilter`, `roomFilter`, `anchorType`, …
- Shipped data file uses `CategoryFilter`, `RoomFilter`, `AnchorType`, …
- `PlacementRule` POCO uses PascalCase properties; Newtonsoft.Json with
  default settings round-trips PascalCase, so the *implementation*
  is PascalCase and the *schema* is wrong.

**Fix**: align the schema to PascalCase (the de-facto wire format).

---

## 4. Flexibility gaps (rule expressiveness)

### 4.1 Geometry editor is 1-D
| Field today | What's missing |
|---|---|
| `OffsetXMm` (single signed offset) | `OffsetYMm`, `OffsetZMm` (separate from MountingHeight), rotation about Z, tilt about local X (e.g., wall-mounted CCTV downturn). |
| `MountingHeightMm` | No "elevation reference" picker — value is implicitly above FFL; user can't say "from ceiling soffit" or "from structural slab", which BS 8300 / Approved Doc M demand for accessible reach. |
| Single point anchor | No support for *line* anchors (perimeter rail of sockets at 1 m centres along a wall) or *area* anchors (every `n` m² of floor). |
| No tolerance | No "+/- mm" envelope; first valid point wins. Production drawings need an explicit tolerance so two engineers running the same rule produce the same fixture grid. |

### 4.2 Side constraint — only EITHER / LEFT / RIGHT actually used
| Today | Missing |
|---|---|
| `EITHER`, `LEFT`, `RIGHT` (used in 43 rules) | `FRONT`, `BACK` declared in VM but never honoured by the scorer (scoring degrades to 0.85 for any non-EITHER), `NORTH/SOUTH/EAST/WEST` cardinal anchors, `ABOVE/BELOW` for stacked devices, `HINGE_SIDE/LATCH_SIDE` for door-relative placement (Approved Doc M switch placement on hinge side is hard-coded in rule 9 but not parameterised). |

### 4.3 Anchor types — useful gaps
| Anchor | What it would unlock |
|---|---|
| `OPPOSITE_WALL` | "WC pan opposite the door" (BS 6465) — currently approximated by `WALL_CORNER`. |
| `GRID_INTERSECTION` | Place near nearest structural grid intersection — universal for plant-room equipment. |
| `COLUMN_FACE` | "Local isolator on column face" — common in industrial fit-outs. |
| `PERIMETER_OFFSET` | Continuous run of sockets at 600 mm centres along every wall in the room (Part M trunking). |
| `SUSPENSION_TRACK` | Track-mount luminaires / curtain rails. |
| `RAISED_FLOOR_TILE` | Floor-box on the nearest 600 mm raised-access tile centre. |
| `RELATIVE_TO` | "Switch within 1 m of door"; "isolator above DB". A single rule cites another rule's output as its anchor. |
| `EQUIPMENT_PAIR` | TV-outlet 100 mm to the LEFT of the data outlet. |
| `DOOR_HEAD` | Exit sign (already cited in rule 22 via `DOOR_JAMB` 2200 mm AFF — really `DOOR_HEAD`). |
| `STAIR_NOSING` | Photoluminescent step edges (BS ISO 16069). |
| `ESCAPE_ROUTE_CENTRELINE` | Emergency luminaires on centreline of every escape corridor (BS 5266-1). |
| `WORKPLANE_ABOVE` | Surface-mount luminaire on a worktop (kitchen under-cupboard tape lighting). |

### 4.4 Room filter — positive regex only
| Today | Missing |
|---|---|
| Single regex match against `ROOM_NAME` | `ExcludeRoomFilter` (negative regex), match by Department, match by Room Number range, match by `STING_ROOM_TYPE_FILTER_TXT` (already read by `FamilyHintsBridge.Inspect` but not surfaced as a rule input), Level filter (`L01\|L02`), Phase filter (place only in *New Construction*), Workset filter, minimum Room Area filter (`AreaMin > 6 m²`). |
| First-match wins via priority | No "match any of N filters" group — long disjunctions like `(?i)bathroom\|wc\|toilet\|shower` are repeated across rule pairs (rules 3, 6, 28, 29, 30, 39, 41, 47). |

### 4.5 Variant hint — single string
| Today | Missing |
|---|---|
| Single `VariantHint` matched verbatim against `STING_FIXTURE_VARIANT_TXT` | Comma-separated fallback chain (`"DALI,DALI-2,LED-DRIVER"`), regex variant (`"^IP6[5-7]$"`), multi-criteria (variant + manufacturer + model), match by `Type Mark`, Cost code, Uniclass `Pr` code. |

### 4.6 Rule chaining and dependencies
The engine treats every rule independently. Real layouts have *graph*
relationships: place luminaires first, then sensors at midpoint of every
3rd luminaire pair; place WC, then the basin opposite, then the grab
rail to its right side at 680 mm. None of this is expressible today.

| Pattern | Today | Suggested addition |
|---|---|---|
| Predecessor / successor | None | `DependsOn` field — rule B only runs after rule A produces ≥1 placement, with a *RelativeTo* anchor referencing A's last placement. |
| Mutual exclusion | None | `ConflictsWith` — skip rule B in any room where rule A produced a placement (e.g., MotionSensor vs CCTV in private offices). |
| Co-placement | None | `CoPlaceWith` — when rule A fires, fire rule B at the same point with an offset (data + power + AV trio). |
| Chain abort | None | `MaxBudget` — stop placing in a room once total fixture cost exceeds £X. |

---

## 5. Functionality gaps (Centre UI / engine)

### 5.1 Run Options
| Surface today | Missing |
|---|---|
| Stamp provenance | Per-run *operator note* persisted alongside the bucket. |
| Honour learned offsets | The toggle is *reserved*; `LearnPlacementV4Command` is a stub TaskDialog. Either ship the learn pass or hide the checkbox until it works. |
| Run validators after placement | No selection of *which* validators (Clearance, MaintenanceClash, Separation, …) — runs all every time. |
| Auto-paint AVF heat-map | No control over *what variable* — coverage? compliance? rule-density? |
| Scope: Active view / Selection / Project | No room-area filter, level filter, phase filter, workset filter, "skip rooms with existing fixtures" gate. |
| (none) | No "dry-run preview first" gate inside the Centre — the historic Fixtures-tab `PromptDryRunChoice` flow only fires from the ribbon command. |
| (none) | No transaction-name override — every run lands as `STING v4 Place Fixtures`, hard to forensic-trace in undo. |
| (none) | No per-rule scope override (e.g., "this rule applies to Active view only, even in a Project run"). |

### 5.2 Live preview
- `Preview` button calls `PreviewController.Start` with a one-shot
  `PlacementPreviewSource`. Editing a rule does **not** refresh the
  preview — user must click Preview again. A debounced auto-refresh on
  rule edit would close the loop.
- No per-rule colour in the preview — every dot looks identical, so a
  user can't see which rule produced which candidate.
- No score display on hover.
- No "show rejected candidates with reason" toggle (debug mode).

### 5.3 Validation surface
- `ShowFindings` enumerates `ClearanceValidator` + `MaintenanceClashValidator` only.
  v4/v6 ship `ConnectivityValidator`, `FillValidator`, `SpecValidator`,
  `TerminationValidator`, `SlopeValidator`, and `SeparationValidator` —
  none are reachable from the Centre.
- Rule-time validation is shallow: `PlacementRuleViewModel.Validate`
  checks empty `CategoryFilter`, non-negative `MinSpacing`, priority
  range, non-negative `MaxPerRoom`. It does *not* validate:
  - regex compilability of `RoomFilter`
  - anchor name being in the engine's accepted enum
  - that `CategoryFilter` is a real Revit category (compare against
    `Category.Categories` for the active doc)
  - that a `FamilySymbol` exists for the category (warns at run-time
    only via `ResolveSymbol` warnings list)
  - that `MountingHeight` is plausible (e.g., > room height)
  - that `MinSpacing` < `RoomDiameter` (otherwise no point will fit)

### 5.4 History / provenance
- 30-bucket cap is hard-coded; no UI to lift it.
- No export-to-CSV of a bucket.
- No diff between two buckets (what changed between v1 and v2 of the same rule run).
- No "promote bucket to baseline" so future runs can compare.
- "Undo last run" deletes elements only; STING does not roll back any
  parameter writes the run made on host families ("Push to Families").

### 5.5 Family resolution & "Push to Families"
- `FixturePlacementEngine.ResolveSymbol` picks the *first* `FamilySymbol`
  whose `Category.Name` matches when no `VariantHint` is set. There's no
  fallback chain: missing variant → first symbol → `result.Warnings`.
- No auto-load from family library — if the project has no symbol of a
  category, the rule silently does nothing. Tag-side has
  `FamilyResolver` (in `ModelEngine`) that auto-loads from
  `Families/`; Centre should use the same.
- `FamilyHintsBridge.PushRuleToFamilyTypes` writes 11 PLACE_*/STING_*
  parameters — but does **not** propagate the directional clearances
  (`STING_CLEARANCE_FRONT_MM`, `STING_CLEARANCE_BACK_MM`,
  `STING_CLEARANCE_SIDE_MM`, `STING_CLEARANCE_TOP_MM`),
  weight (`PLACE_WEIGHT_KG`), envelope (`MNT_ENV_*_MM`), or fire
  separation (`FIRE_SEP_MM`). Inspect *reads* them, but the rule has no
  edit field for them.
- No "preview pushes" pre-flight (which family types would change?)
- No undo for the push beyond a global Revit Undo step.

### 5.6 Import / Export
- `ImportFromFile` skips rules whose `MergeKey` already exists — silent
  collision behaviour. Should offer per-row Skip / Overwrite / Rename.
- `ExportToFile` writes only valid rules — invalid rules silently
  vanish, can't share work-in-progress with a colleague.
- No JSON-Schema validation on import; malformed files surface as
  generic `Newtonsoft.Json` exceptions.

### 5.7 Concurrency / multi-doc
- `StingPlacementCenter._instance` is a singleton; switching documents
  closes-and-reopens. Two open Revit windows on different docs can't
  use the Centre simultaneously. Acceptable today, but the singleton
  should be keyed by `Document.PathName` or `UIApplication`.

---

## 6. Automation logic gaps (engine intelligence)

### 6.1 Anchor generation is coarse
`PlacementScorer.GenerateAnchorPoints` for `WALL_*` / `DOOR_*` /
`WINDOW_SILL` returns **four cardinal offsets from the room location
point** (`r = 3 ft`). Comment line 219:

```
// TODO-VERIFY-API: full wall/door geometry inspection happens in
// FixturePlacementEngine via BoundarySegment + wall face. For scoring
// we sample 4 cardinal offsets from the room centre.
```

Real wall-face placement doesn't exist yet. Consequence: a rule that
says "WC pan 300 mm from sidewall" will land 914 mm from the room
*centroid* in the +X direction, not 300 mm from any wall. The scorer's
`SideScore` degrades to 0.85 for any non-EITHER constraint without ever
checking which wall face the point sits on.

**Fix priority HIGH** — without real boundary-segment + door/window
location resolution, every WALL_* and DOOR_* rule is approximate.

### 6.2 Collision & clearance
| Today | Missing |
|---|---|
| `ObstructionIndex` covers 7 categories with a 350 mm buffer (CIBSE Guide B4). | No fixture-to-fixture clearance from `STING_CLEARANCE_*` (Pack 2 directional clearances are *read* by the inspector but never *enforced* by the scorer). |
| `RejectInsideWall` filter via `ElementIntersectsSolidFilter`. | No "inside furniture" check — placing a desk-side socket inside a desk solid is allowed. |
| `MinSpacingMm` is rule-internal (within rule), not cross-rule. | No cross-rule spacing — two rules in the same room don't see each other's placements when scoring. |
| Per-room `_obstructionCache` and `_wallSolidCache`. | Caches are dropped at end of `Score` for a single rule? **No** — they live on the Scorer instance for the whole engine run. Good. But not pickled between runs, so a Preview-then-Run runs the collector twice. |

### 6.3 Lighting grid is partly wired
- `LightingGridCalculator` exists and is referenced by the
  `LIGHTING_GRID/LUX_GRID/EN12464` anchors in the scorer.
- But `LightingGridCommand` is still a TaskDialog *stub* that says
  "S2.10 will: ...". The Centre's GD Study button references a `.dyn`
  file but does not run the calculator either.
- `CeilingGridSnap` exists and snaps points to ceiling-tile grids, but
  `PlacementScorer` never calls it — luminaires generated by
  `LIGHTING_GRID` anchors are not snapped to tiles.
- `DefaultLumensPerLuminaire = 4000`, `UF = 0.60`, `MF = 0.80` are
  hard-coded. No way to override per fixture type, no way to read the
  fixture's `Initial Intensity` or `Luminous Flux` parameter.

### 6.4 Learn-from-model not implemented
`LearnPlacementV4Command` is a TaskDialog stub. The Centre's
"Honour learned offsets per category" toggle is therefore a no-op.
Either implement (the comment block lays out the design — collect
existing fixtures, derive anchor + offset + height per
`(Category, RoomName)` cluster, emit project-override JSON with
`Priority=90`) or hide the toggle until S2.8 graduates from stub.

### 6.5 Generative Design bridge is not a real loop
`FixturePlacementEngine.RunStudy` (Pack 11) computes:
- `CoveragePct` via the trivial proxy "room is covered if any rule accepts it"
- `SpacingVariance` from an empty `distances` list (always 0)
- `ClearanceViolations` by re-running `ClearanceValidator` on the *current* document state, not a simulated trial.

Net effect: the GD study can't actually compare trials. Either wire a
true in-memory placement simulation, or remove the GD button until
the harness is real.

### 6.6 Standards-driven density not modelled
Today every rule is a single point-of-placement. Real standards drive
**counts**:
| Standard | Density rule | Today |
|---|---|---|
| BS 7671 / Approved Doc M | 1 socket per 3 m of wall + 4 above worktop | rule 1 says "Default socket, 300mm AFF per BS 7671" but produces 1 socket; user must clone rule N times |
| BS 6465 | 1 WC per 1-15 staff male, 1 per 1-5 female | not modelled — `MaxPerRoom` is a cap, not a function of occupancy |
| BS 5266-1 | Emergency luminaire on every change of direction | not modelled — single rule per category |
| BS 5839-1 | Smoke detector max 7.5 m radius | rule 17 hard-codes 10 500 mm `MinSpacing` (= 10.5 m, a max of 10.5 m between heads, so radius is half ≈ 5.25 m). Off by the right factor of 2 *only because* "min spacing" is centre-to-centre. Comment says "max 7.5 m radius" — maths is correct for a quincunx grid (head spacing = 7.5 × √2 ≈ 10.6 m) but should be derived, not hard-coded. |

A `density` rule class — "1 of *category* per N m² / N occupants /
linear metre of wall" — would close the bulk of the catalogue with
half the rule count.

### 6.7 Performance & I/O
- `PlacementScorer.ResolveSampleInstanceForRule` runs a whole-document
  collector with no early exit — once per rule per room. For
  43 rules × 200 rooms = 8 600 collector walks, each ~O(elements). Add
  a `Document` → `Category.Name` → first-instance cache (one-shot per
  Score session).
- `PlacementRuleLoader.LoadDefaults` re-reads the JSON every call
  (no caching). Cheap (43 lines today) but will bite when projects
  hit hundreds of rules.
- `ObstructionIndex.BuildForRoom` iterates the whole document
  pre-bbox-filter; ok today, but no guard against 100 k-element models.
- `HistoryBridge.ReadHistory` walks every `Element` in the document
  with `WhereElementIsNotElementType` — fine on small models, but
  unbounded otherwise. A category-allow-list pulled from the Centre's
  rule set would shrink the scan by an order of magnitude.

---

## 7. Cross-system integration gaps

| Centre needs | Where it lives today |
|---|---|
| MEP system (HVAC duct, hydraulic loop, electrical circuit) auto-assignment after placement | None — placed fixtures are unconnected. Engine should optionally pair with `MEPSystemBuilder` so each placed fixture lands on an existing system or seeds a new one. |
| BOQ / cost rates | `cost_rates_5d.csv` exists but is consumed by `Scheduling4DEngine`, not the Centre. Place + estimate per-rule cost in the result panel. |
| COBie integration | Each placed instance should optionally seed `COBie.Component.Name` from rule notes. `BIMManagerCommands.COBieExport` already knows how. |
| Tag pipeline | After placement, `RunFullPipeline` is *not* invoked — placed fixtures land untagged. `StingAutoTagger` IUpdater catches the addition only if the toggle is on. The Centre should optionally call `TagPipelineHelper.RunFullPipeline()` post-commit. |
| Asset register / ISO 19650 | No automatic propagation of `LOC/ZONE/LVL` tokens to the placed instance — same gap as tagging. |
| Clash detection | No auto-publish of placement result to BCF. |
| ACC / CDE upload | None. |
| Mobile preview | The Planscape mobile app has no Centre-equivalent. |

---

## 8. Recommended catalogue (so the Centre ships with breadth)

The shipped 43 rules cover MEP fixtures only. To match the breadth of
the rest of STING (architectural model creation, COBie 22 presets,
BIM coordination, etc.), the Centre baseline JSON should grow to cover
the categories below. Every row is a *single rule* on top of the
categories already in §2.2.

### 8.1 Architecture seeds (≈25 new rules)
| Category | Rule sketch | Standard |
|---|---|---|
| Doors | 1 leaf per office; 1.5-leaf set on lift lobby; accessible 1000 mm leaf for accessible WC | Approved Doc M, BS 8300 |
| Windows | Cill 800 mm in residential, 1100 mm offices, full-height in atria | BS 8233 daylight |
| Furniture / FurnitureSystems | 1 desk + chair per occupant; 6.4 m² per desk-pod (BCO) | BCO 2019, BS 6396 |
| Casework | 600 mm base unit per 2 m of kitchen wall; lab benches at 1500 mm centres | HTM 06-01, BB103 |
| Generic Models | Coat hooks on door wall every 600 mm; signage at 1500 mm AFF | BS 8300 |
| Specialty Equipment | Lectern at room-front centreline (classroom); IFP wall-plate at 1100 mm AFF; vending machine in canteen | BB103, WELL v2 |
| Parking | 6 % accessible bays + 25 % EV-ready (UK Building Regs 2022 Part S) | BS 8300, Part S |

### 8.2 Mechanical seeds (≈12 new rules)
| Category | Rule sketch | Standard |
|---|---|---|
| Duct Accessories | Fire damper at every wall penetration (rule chained to wall-line + duct-line intersection) | BS EN 1366-2 |
| Plumbing Equipment | Hot-water cylinder in plant-room corner, 1500 mm clearance | BS 6700 |
| Mechanical Control Devices | Thermostat on internal wall opposite door, 1200 mm AFF | BS 8300 reach |
| Air Terminals (extra) | Extract grille per WC compartment (rule 26 covered); supply diffuser on a 4 m × 4 m grid for offices (rule 25 covered); kitchen extract per BS EN 16282 hood | CIBSE Guide B1 |

### 8.3 Electrical seeds (≈18 new rules)
| Category | Rule sketch | Standard |
|---|---|---|
| Electrical Fixtures | Continuous perimeter trunking sockets at 600 mm centres (Approved Doc M trunking) | Approved Doc M, BS 7671 |
| Electrical Equipment | 1 DB per floor for areas < 500 m²; 1 per 250 m² above | BS 7671 |
| Electrical Equipment | Sub-main disconnector at 1.2 m AFF in plant rooms | BS 7671 |
| Lighting Devices | 2-way switch pair on stairs (rule 11 covered); rocker dimmer in conference rooms; key-switch in plant rooms | BS 8300 |
| Lighting Fixtures | Office 500 lux on 2.4 m grid (rule 36 covered); corridor 100 lux on 3 m grid (rule 37 covered); emergency luminaires every 8 m on escape routes (rule 21 covered) — but escape *signs* on every change of direction missing | BS EN 12464-1, BS 5266-1 |
| Communication Devices | Public-address speaker per 60 m² of corridor, 1 per WC | BS 5839-8 |
| Data Devices | Twin RJ45 per workstation (rule 12 covered); IP-camera uplink with PoE — separate rule from CCTV mount | BICSI |

### 8.4 Healthcare / Education seeds (≈10 new rules)
| Category | Rule sketch | Standard |
|---|---|---|
| Specialty Equipment | Bedhead trunking centred above bed; oxygen + suction outlets at 1500 mm AFF | HTM 02-01, HTM 08-03 |
| Nurse Call Devices | Bedside button (rule 33 covered); ceiling pull cord in bathroom (rule 34 covered); reset button at door | HTM 08-03 |
| Furniture | 600 mm clearance around hospital bed for transfer | HBN 04-01 |
| Casework | Teaching whiteboard wall-centred, bottom edge 800 mm AFF (KS1) / 950 mm AFF (KS3) | BB103 |

---

## 9. Prioritised remediation backlog

Bands are ordered by *value-per-week*; each entry names the file
where the change lands.

### Band P0 — schema & correctness (1 week)
1. **PC-01** Align `STING_PLACEMENT_RULES.schema.json` with the engine's accepted enums (anchor types, side, priority 0–100, PascalCase keys). Generate the schema from `PlacementRule` properties via a unit test so it can never drift again. *(`Data/Schemas/STING_PLACEMENT_RULES.schema.json` + new `T4` template.)*
2. **PC-02** Add full `RoomFilter` regex compilability check + anchor-name + side-name validation in `PlacementRuleViewModel.Validate`. *(`UI/PlacementCenter/PlacementRuleViewModel.cs`.)*
3. **PC-03** Validate `CategoryFilter` against `Document.Settings.Categories` on load + on edit; surface unknown categories as Invalid chip filter. *(`PlacementRuleViewModel`, requires `Document` injection.)*
4. **PC-04** Replace 4-cardinal anchor approximation in `GenerateAnchorPoints` with real boundary-segment + door/window face look-up. Currently every WALL_*/DOOR_* rule is approximate. *(`Core/Placement/PlacementScorer.cs`.)*
5. **PC-05** Hide the *Honour learned offsets* toggle until `LearnPlacementV4Command` graduates. *(`UI/PlacementCenter/StingPlacementCenter.xaml`.)*

### Band P1 — flexibility (2 weeks)
6. **PC-06** Add `OffsetYMm`, `OffsetZMm`, `RotationDeg`, `MountingReference` (FFL / SOFFIT / SLAB) to `PlacementRule` + Centre Geometry editor. *(`Core/Placement/PlacementRule.cs`, `UI/PlacementCenter/StingPlacementCenter.xaml`.)*
7. **PC-07** Add `ExcludeRoomFilter`, `LevelFilter`, `PhaseFilter`, `WorksetFilter`, `MinAreaM2`, `RoomDepartmentFilter` to `PlacementRule`. Multiple positive filters → AND; exclude → AND-NOT. *(same file.)*
8. **PC-08** Promote VariantHint to a comma-separated fallback chain, allow regex via leading `^…$`. *(`PlacementScorer.ResolveSymbol`.)*
9. **PC-09** Implement `OPPOSITE_WALL`, `GRID_INTERSECTION`, `COLUMN_FACE`, `PERIMETER_OFFSET`, `DOOR_HEAD`, `RAISED_FLOOR_TILE` anchor generators. *(`PlacementScorer.GenerateAnchorPoints`.)*
10. **PC-10** Wire `CeilingGridSnap.SnapToCeilingGrid` into the LIGHTING_GRID anchor path. *(`PlacementScorer.GenerateAnchorPoints`.)*
11. **PC-11** Surface `STING_CLEARANCE_*` (front/back/side/top), `PLACE_WEIGHT_KG`, `MNT_ENV_*_MM`, `FIRE_SEP_MM` as editable rows on the Centre's Family Defaults & Clearance grid (not just read-only). Push-to-Families propagates them. *(`UI/PlacementCenter/FamilyHintsBridge.cs`, XAML.)*

### Band P2 — automation (3 weeks)
12. **PC-12** Add a `density` rule kind: `RuleKind = Point | Density | Linear`. Density rules carry `PerArea_m2`, `PerOccupant`, or `PerLinearMetre` and let the engine compute count from the room rather than `MaxPerRoom`. *(`PlacementRule` + `FixturePlacementEngine.ProcessRoomRule`.)*
13. **PC-13** Add `DependsOn` / `ConflictsWith` / `CoPlaceWith` rule fields and an iterative pass in the engine that resolves the dependency DAG before running. *(`PlacementRule` + `FixturePlacementEngine`.)*
14. **PC-14** Implement `LearnPlacementV4Command` per the design comment in the stub: collect, cluster, emit `Priority=90` overrides, surface a "review proposed rules" dialog. *(`Commands/Placement/LearnPlacementV4Command.cs`.)*
15. **PC-15** Real Generative Design loop: `RunStudy` runs an in-memory placement against a *copy* of the rule set with weight perturbations, returns the three objective scalars per trial. *(`Core/Placement/GenerativeDesignBridge.cs`.)*
16. **PC-16** Auto-load missing family symbols from `Families/<discipline>/<category>/` when `ResolveSymbol` finds nothing. Reuse `ModelEngine.FamilyResolver`. *(`FixturePlacementEngine.ResolveSymbol`.)*
17. **PC-17** Post-placement hooks: optional `RunFullPipeline` per placed instance (data tags), optional `MEPSystemBuilder.Connect`, optional `COBie.Component` seeding. *(`FixturePlacementEngine`, new `PostPlacementHooks` static.)*

### Band P3 — catalogue (2 weeks; data-only; no code)
18. **PC-18** Add the ≈ 65 baseline rules from §8 (architecture, mechanical, electrical, healthcare/education) to `STING_PLACEMENT_RULES.json`. Total target ≈ 110 rules across ≈ 25 categories. *(`Data/Placement/STING_PLACEMENT_RULES.json`.)*
19. **PC-19** Cross-check every standard cited in `Notes` against the standards module (`StandardsEngine`) so the rule notes always reference the right clause. *(data-only.)*
20. **PC-20** Ship per-discipline rule packs as separate JSON files (`STING_PLACEMENT_RULES.healthcare.json`, `…office.json`, `…education.json`, `…residential.json`) and have the Centre offer a sector picker on first run. *(`PlacementRuleLoader` + Centre toolbar.)*

### Band P4 — UX polish (1-2 weeks)
21. **PC-21** Live preview refresh on rule edit (debounced 500 ms). *(`StingPlacementCenter.xaml.cs`.)*
22. **PC-22** Per-rule colour in the DirectContext3D preview (hash CategoryFilter → distinct hue). *(`Core/Visualization/PlacementPreviewSource.cs`.)*
23. **PC-23** Validator picker (multiselect: Clearance, Maintenance, Connectivity, Fill, Spec, Termination, Slope, Separation). *(Run Options pane + `PlacementCenterBridge.RunValidators`.)*
24. **PC-24** Dock the Centre into the WPF dockable panel (currently a modeless `Window`) so it sits alongside the other 9 STING tabs. *(`UI/StingDockPanel.xaml` + provider.)*
25. **PC-25** Move *Run / Preview / Validate* keyboard shortcuts off Ctrl+R/P/V (collisions with Revit defaults) and document in tooltip — current shortcuts work but overload Revit's Ctrl+P print dialog. *(`PlacementCentreCommands.cs`.)*

---

## 10. Suggested PlacementRule schema (target)

For Band P0–P2, the rule POCO grows from 11 fields to ≈ 25.
Keys are illustrative; the canonical names should be set when the
schema generator (PC-01) lands.

```jsonc
{
  // ── identity ─────────────────────────────────────────
  "ruleId": "elec-wc-shaver-01",          // stable id (was implicit MergeKey)
  "ruleKind": "Point",                     // Point | Density | Linear
  "categoryFilter": "Electrical Fixtures",
  "variantHints": "FLUSH,IP44",            // comma-separated fallback chain
  "familyTypeRegex": "^Shaver.*$",         // optional symbol-name regex

  // ── room scoping ─────────────────────────────────────
  "roomFilter": "(?i)bathroom|wc|toilet",
  "excludeRoomFilter": "(?i)plant|riser",
  "roomDepartmentFilter": "Sanitary",
  "minAreaM2": 1.5,
  "maxAreaM2": 0,                          // 0 = no cap
  "levelFilter": "L0[1-9]|GF",
  "phaseFilter": "New Construction",
  "worksetFilter": "Electrical",

  // ── geometry ─────────────────────────────────────────
  "anchorType": "WALL_MIDPOINT",
  "mountingReference": "FFL",              // FFL | SOFFIT | SLAB | CEILING
  "mountingHeightMm": 450,
  "offsetXMm": 0,
  "offsetYMm": 0,
  "offsetZMm": 0,
  "rotationDeg": 0,
  "tolerancesMm": 25,
  "sideConstraint": "EITHER",

  // ── density / linear (when ruleKind != Point) ────────
  "perAreaM2":      0,                      // 1 socket per 10 m²
  "perOccupant":    0,                      // 1 WC per 15 occupants
  "perLinearMetre": 0,                      // 1 socket per 3 m of wall

  // ── spacing & cap ────────────────────────────────────
  "minSpacingMm": 800,
  "maxPerRoom": 2,

  // ── chaining ─────────────────────────────────────────
  "dependsOn": "elec-wc-basin-01",          // ruleId reference
  "relativeTo": "previous",                 // previous | first | self
  "coPlaceWith": ["data-wc-01"],
  "conflictsWith": [],

  // ── reporting ────────────────────────────────────────
  "priority": 70,
  "standardRef": "BS 7671 §701",
  "uniclassPr": "Pr_70_70_05_84",
  "notes": "Shaver/SELV outside Zone 2"
}
```

---

## 11. Quick-win (≤ 1 day) checklist

These are the highest-leverage fixes that can land in a single
half-day each without architectural change:

- [ ] **Schema fix** — anchor enum + priority range + PascalCase keys (PC-01).
- [ ] **Regex compilability** validator on `RoomFilter` (PC-02 partial).
- [ ] **Anchor validation** — show `✗ Invalid` chip when `AnchorType` not in `VM.AnchorTypes` (PC-02 partial).
- [ ] **Hide the dead toggle** "Honour learned offsets" until S2.8 lands (PC-05).
- [ ] **Document → Category early-exit cache** in `ResolveSampleInstanceForRule` (Band P0 perf).
- [ ] **Add 3 missing baseline rules**: kitchen worktop sockets density, escape-route emergency luminaire, perimeter trunking — any user opening a generic office project today gets only the existing 43 single-point rules.
- [ ] **Fix 8 `(?i)bathroom|wc|toilet` duplications** — collapse to a shared regex constant or named filter (PC-07 stepping-stone).
- [ ] **Tooltip on toolbar** — "GD Study" currently shows a static TaskDialog; either make it open the .dyn or rename to "GD Study (manual)".

---

## 12. References

- `CLAUDE.md` §"v4 MVP" — placement engine and 12 commands.
- `docs/CHANGELOG.md` — Phase 127-A/B/C/D introduces the Centre; v6 phase
  introduces `CeilingGridSnap` (S2.15 / L-G1) and `ObstructionIndex`
  (S2.16 / L-G2).
- BS EN 12464-1:2021 — lux targets.
- BS 7671:2018+A2 — electrical socket / DB placement.
- Approved Doc M (2015) — accessible reach ranges, switch hinge-side.
- BS 8300-2:2018 — accessible buildings.
- BS 6465 — sanitary installations.
- BS 5266-1 / BS EN 1838 — emergency lighting.
- BS 5839-1 — fire detection.
- BS EN 12845 / NFPA 13 — sprinklers.
- HTM 08-03 — bedhead services.
- BB103 — area guidelines for mainstream schools.
- BCO 2019 Guide to Specification.

---

*End of review.*

---

## 13. Resolution log (2026-04-25)

Every backlog item from §9 has been worked through. Status:

| ID | What landed | Where |
|---|---|---|
| PC-01 | Schema rewritten with engine-accepted enums, PascalCase keys, priority 0–100, all new fields | `StingTools/Data/Schemas/STING_PLACEMENT_RULES.schema.json` |
| PC-02 | `PlacementRuleViewModel.Validate` compiles every regex, checks all enum memberships | `StingTools/UI/PlacementCenter/PlacementRuleViewModel.cs` |
| PC-03 | `PlacementRulesViewModel.BuildValidCategoryNames` validates `CategoryFilter` against the live document | same root VM file |
| PC-04 | `GenerateAnchorPoints` reads real boundary segments + door / window instances | `StingTools/Core/Placement/PlacementScorer.cs` |
| PC-05 | Dead toggle restored once PC-14 graduated; honours `STING_PLACEMENT_RULES.learned.json` | `StingPlacementCenter.xaml` + `PlacementRuleLoader.cs` |
| PC-06 | `OffsetYMm`, `OffsetZMm`, `RotationDeg`, `ToleranceMm`, `MountingReference` (FFL/SOFFIT/SLAB/CEILING) | `PlacementRule.cs`, scorer, XAML, code-behind |
| PC-07 | `ExcludeRoomFilter`, `RoomDepartmentFilter`, `MinAreaM2`, `MaxAreaM2`, `LevelFilter`, `PhaseFilter`, `WorksetFilter`; `RoomMatchesScope` enforces the suite in one pass | `PlacementRule.cs`, `PlacementScorer.cs` |
| PC-08 | Variant fallback chain (comma-separated) + regex hint + `FamilyTypeRegex` | `FixturePlacementEngine.ResolveSymbol` |
| PC-09 | New anchor types: `OPPOSITE_WALL`, `GRID_INTERSECTION`, `COLUMN_FACE`, `PERIMETER_OFFSET`, `RAISED_FLOOR_TILE`, `DOOR_HEAD`, `STAIR_NOSING`, `ESCAPE_ROUTE_CENTRELINE`, `RELATIVE_TO`, `EQUIPMENT_PAIR` | `PlacementScorer.cs` |
| PC-10 | `EmitLightingGridPoints` pipes lux-grid points through `CeilingGridSnap.SnapToCeilingGrid` | `PlacementScorer.cs` |
| PC-11 | Editable Clearance / Envelope / Weight / Fire-sep group; `FamilyHintsBridge.PushExtras` writes 11 extra params | `StingPlacementCenter.xaml` + `FamilyHintsBridge.cs` |
| PC-12 | `PlacementRuleKind` (Point/Density/Linear) + `PerAreaM2` / `PerOccupant` / `PerLinearMetre`; engine `ComputeCap` derives count from area, occupancy or perimeter | `PlacementRule.cs`, `FixturePlacementEngine.cs` |
| PC-13 | `RuleId`, `DependsOn`, `RelativeTo`, `CoPlaceWith`, `ConflictsWith`; engine maintains per-room state, fires co-rules at the predecessor's last point | `PlacementRule.cs`, `FixturePlacementEngine.cs` |
| PC-14 | `LearnPlacementV4Command` walks 19 categories, clusters by (Category, RoomKeyword), emits `STING_PLACEMENT_RULES.learned.json` with Priority 90 | `Commands/Placement/LearnPlacementV4Command.cs` |
| PC-15 | `RunStudy` clones rules, perturbs `MinSpacingMm` / `Priority`, runs the engine in dry-run mode, returns real `CoveragePct` and stddev-based `SpacingVariance` | `Core/Placement/GenerativeDesignBridge.cs` |
| PC-16 | `TryAutoLoadFromLibrary` searches `Families/**/*.rfa` for a category match and loads the first hit | `FixturePlacementEngine.cs` |
| PC-17 | New `PostPlacementHooks` static (RunDataTagPipeline / SeedCobieComponent / AssignMepSystem); engine fires hooks after each placement | `Core/Placement/PostPlacementHooks.cs` |
| PC-18 | 58 new baseline rules with `RuleId` + `StandardRef` across 4 packs | `Data/Placement/STING_PLACEMENT_RULES.{architecture,mechanical,electrical,healthcare-education}.json` |
| PC-19 | Every new rule cites a UK/BS/CIBSE standard or guidance document | same packs |
| PC-20 | `PlacementRuleLoader.LoadDefaults` auto-merges baseline + 4 packs (~100 rules out of the box); `.project.json` and `.learned.json` still win | `PlacementRuleLoader.cs` |
| PC-21 | `chkLivePreview` toggle + 500 ms `DispatcherTimer` debounce on `CommitField` triggers a Preview after each rule edit | `StingPlacementCenter.xaml.cs` |
| PC-22 | Per-rule HSV → ARGB colour in `PlacementPreviewSource`; per-room × rule scoring loop replaces the old "cross at room centroid" placeholder | `Core/Visualization/PlacementPreviewSource.cs` |
| PC-23 | 8-validator picker (Clearance / Maintenance / Connectivity / Fill / Spec / Termination / Slope / Separation) honoured by `RunValidators(doc, mask)` | `StingPlacementCenter.xaml` + `PlacementCenterBridge.cs` |
| PC-24 | Already partly wired: the `Placement_OpenCentre` button on the v4 Fixtures tab opens the Centre from the dockable panel. Embedding the editor as a tab is a deferred follow-up (Window→UserControl refactor). | `StingDockPanel.xaml` |
| PC-25 | Run / Preview / Validate shortcuts moved from Ctrl+R/P/V (Revit conflicts) to Alt+R/P/V; tooltips updated | `StingPlacementCenter.xaml` |

