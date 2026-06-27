# Sustainable Perfect Placement: Real Geometry + Host-First — Advisory

> Research + recommendation. Grounded in the symbol/seed and placement code.
> The wrong pattern (diagonal, scattered, generic boxes) and the "no real
> geometry" complaint are **two problems with one root**: the architecture
> treats fixtures as generic, non-hosted, schematic objects.

---

## 1. Diagnosis — why the pattern is wrong

The single biggest flaw (from the engine deep-dive): **fixtures are created
level-based at a world XYZ, never hosted on the wall face, then a post-hoc
rotation tries to align them — and that rotation is unreliable.**

- `PlacementHostPreflight` creates most fixtures with
  `doc.Create.NewFamilyInstance(position, symbol, level, NonStructural)` — a
  bare point, **no host, no face, no orientation**.
- `OrientPlacedInstance` then tries to fix facing, but `FamilyInstance.FacingOrientation`
  is **zero/invalid for a freshly-created non-hosted instance until the document
  regenerates**, so the flip/align no-ops → the fixture keeps its world-X facing
  → **diagonal on any non-orthogonal wall**.
- The wall-snap projects to the wall **centreline, not the face**, so fixtures sit
  in/!on the wall.
- **Scattered/over-placed**: `WALL_MIDPOINT`/`PERIMETER_OFFSET` emit dense
  candidates per segment; with `MinSpacingMm=0` on ~290 rules the spacing gate is
  loose; overlapping rules each place their own set. (The Phase-195 crowding guard
  + floor mitigates this but can't fully fix a position-first design.)

**Conclusion:** post-hoc rotation of non-hosted instances is the wrong model.
Revit already orients hosted/face-based families correctly — the engine should
let it.

## 2. Diagnosis — why the geometry is schematic

- Seeds are **Track B (2D Generic-Annotation symbols)** + an optional **single
  bounding-box** 3D solid (one `NewExtrusion` — box or cylinder).
- The creator has **no `NewSweep` / `NewRevolution` / `NewBlend` / `NewLoft` /
  boolean** and **no per-variant geometry** (`TypeVariantDefinition` only
  overrides params/connectors). So `SOCKET_1G…4G` all share one box.
- Building a full parametric 3D generator is **7–9 weeks** and still won't equal
  a real product family. That is the wrong investment.

## 3. The sustainable architecture (recommended)

Three tiers, each cheap and durable:

### Tier 1 — Host-first placement (engine) — *kills the diagonal pattern*
Make placement **host-first, not position-first**:
1. Seeds for wall devices authored **FaceBased / WallBased**; ceiling devices
   **CeilingBased**. (Hosting field already exists in the seed schema.)
2. The engine resolves the **wall face reference** (or ceiling) at the anchor and
   creates via the **hosted overload** `NewFamilyInstance(faceRef, point, refDir, symbol)`
   so **Revit auto-orients** — no post-hoc rotation.
3. Where a host can't be found, **regenerate before** reading `FacingOrientation`
   (so the existing fallback actually works), then snap to the **face plane**.

This is the highest-value fix: it removes the diagonal problem at the source and
makes every wall fixture sit flush, oriented, by construction.

### Tier 2 — ~20 real, hand-authored seed families — *gives real geometry, once*
Do **not** build a geometry generator. Author (or acquire free) **one real
face-based/ceiling-based family per common category** — simple, recognizable
geometry that's a few hours each, done once, `.sting-finalized`:

| Category | Geometry (simple, real-enough) | Host |
|---|---|---|
| Socket / switch / data | faceplate + back box (plate + extrusion + 2 cutouts) | FaceBased (wall) |
| Downlight | bezel ring + recessed cylinder | CeilingBased |
| Pendant / batten | cylinder/box + drop | CeilingBased |
| Diffuser / grille | square frame + neck | CeilingBased |
| Sprinkler | body + deflector | CeilingBased |
| Smoke/heat detector | disc + base | CeilingBased |

The **generator's real value is the parameters + variants + connectors + GUIDs**,
not the geometry — keep using it to *stamp* these families, but let humans (or
vendors) own the shape. This is Track A done deliberately for the ~20 categories
that actually get placed in a dwelling/commercial fit-out.

### Tier 3 — Manufacturer swap — *real product reality* (already built)
The non-destructive swap bridge takes a placed seed → the real vendor `.rfa`,
preserving position/host/parameters. This is the path to photoreal/BIM-LOD-400
geometry when the project has vendor families.

### The "perfect placement" formula
`right family (hosted, real)` + `right host (wall face / ceiling)` +
`right anchor (door-latch switch, perimeter socket, lux-grid light)` +
`right count (density/spacing)` + `crowding dedup`. Everything except **host-first
creation** already exists — Tier 1 is the missing keystone.

## 4. Why this is sustainable + maximally flexible
- **Host-first** means orientation is Revit's job, not fragile math — robust on
  any wall angle, forever.
- **~20 finalized families** is a bounded, one-time asset (not per-project work),
  versioned in `Families/Seeds/`, protected by `.sting-finalized`.
- **Swap** keeps the door open to any vendor library without re-placing.
- The **rule schema already carries** anchor/height/spacing/variant/hosting — no
  schema churn needed for Tier 1; Tier 2 is pure family authoring.

## 5. Recommended plan (phased)
1. **Tier 1a (engine, ~1–2 days):** regenerate-before-orient + prefer the hosted/
   face overload when a wall/ceiling is found; snap to face plane. Verify the
   diagonal pattern disappears even with the current schematic seeds.
2. **Tier 1b (engine):** ensure the seed build authors wall devices as FaceBased
   and ceiling devices as CeilingBased (set `hosting` in the seed JSON +
   template pick).
3. **Tier 2 (authoring, ~1–2 weeks):** hand-author the ~20 real seed families,
   stamp with the generator, finalize, drop in `Families/Seeds/`.
4. **Tier 3:** populate the swap registry as vendor families arrive.

Generator extension (sweep/revolve/blend) is **explicitly not recommended** as the
path — it's the expensive way to a worse result than Tier 2.

## 6. The one-line answer
**Stop placing non-hosted boxes and rotating them. Place hosted, face-based real
families and let Revit orient them — author ~20 of those once, and swap to vendor
families when you have them.** Host-first placement is the keystone; real geometry
is ~20 finalized families, not a 9-week generator.
