# Multi-Host Integration Plan — Bonsai · Revit · ArchiCAD · Tekla → Planscape

**Status:** Plan / advisory. No code is changed by this document.
**Decisions locked with the product owner:**

1. **Hard dependency on Bonsai** for the interactive Blender add-on (no in-Blender
   standalone mode). Headless/batch runs via `stingtools-core` + StingBridge with a
   pip-installed `ifcopenshell` — *not* by bundling ifcopenshell into the extension.
2. **Bidirectional sync** (push *and* pull) between every host and Planscape.
3. **ArchiCAD and Tekla are designed in now** so the architecture never has to be
   reworked to admit them — but both go through the **IFC route only**. *No native
   ArchiCAD plugin and no native Tekla plugin are in scope at this time.*
4. **Coordinate alignment of the federated model must be automatic.** Models authored
   in four different tools, in four different workspaces, must combine in Planscape
   into one correctly-positioned federation with minimal human intervention.

This plan has three parts:

- **Part 1 — Integration architecture** (the host-adapter contract + 2-way sync).
- **Part 2 — Federated coordinate alignment engine** (the deep section: how four tools'
  coordinate systems are reconciled automatically). *This is the highest-risk, highest-
  value part.*
- **Part 3 — Flexibility & maximum-automation gap review.**

A consolidated sequencing + risk table closes the document.

---

## 0. Where we are today (build-vs-gap baseline)

The integration is materially further along than `CLAUDE.md`'s Phase 186 entry implies.
What actually exists on this branch:

| Area | Built today | File(s) |
|---|---|---|
| Bonsai add-on | tagging (auto-tag, token writers, SEQ, full-tag), IDS/grammar validation, select, **live Planscape push** (`host=bonsai`, stdlib-only, installable) | `stingtools-bonsai/ops/*`, `planscape/{client,ingest}.py` |
| Shared core | pure-Python, no `bpy`: enums, psets, tag grammar, spatial check, IDS runner, Planscape client | `stingtools-core/python/stingtools_core/*` |
| Headless bridge | CLI (`python -m StingBridge.bridge`) — ArchiCAD live-link + **IFC drop-folder watcher** + Revit importer; pip deps incl. `ifcopenshell` | `StingBridge/*` |
| Server ingest | host-agnostic `/ifc/data` + cross-host lookup `/ifc/mappings`; `ExternalElementMapping` keyed on `(ProjectId, IfcGlobalId, Host, HostDocumentGuid)`; durability backstop | `IfcController`, `IfcIngestService`, `CrossHostMappingReconciliation` |
| Host registry | `MappingHosts.{Revit, Bonsai, ArchiCAD, Tekla}` already defined; ingester sniffs ArchiCAD/Tekla psets | `MappingHosts.cs`, `XbimIfcIngester.cs` |
| **Coordinate validation** | parses `IfcMapConversion`, `IfcProjectedCRS`, true north, units, `IfcSite` GUID; drift detection; cross-model coherence (unit / site / georef / north mismatch); analytical-stick-model detection | `IfcAlignmentValidator.cs` |
| **Coordinate anchor** | `ProjectCoordinateSystem` — canonical per-project CRS, origin E/N/elev, true north, unit, nominated reference model | `ProjectCoordinateSystem.cs` |
| **Auto-align (georef path)** | computes translation + Z-rotation + scale to bring a target into the reference frame from survey origins; persists `ProjectModelTransform`; emits SignalR to the federated viewer | `AutoAlignService.cs`, `AlignmentController.cs`, `FederatedModelHub.cs` |
| Level reconciliation | `ProjectLevel` with per-tool name mappings (`Revit "Level 01" ↔ ArchiCAD "01" ↔ Tekla "1st Floor"`) + elevation | `ProjectLevel.cs` |

**The honest summary:** the **LoGeoRef-50 happy path is essentially built** — if every model
exports a clean `IfcMapConversion` in the same CRS, Planscape already federates them
automatically. The gaps are (a) **every model that ships *below* LoGeoRef 50** — which is
the common case for Tekla, Bonsai, and many ArchiCAD/Revit exports — and (b) the **pull half
of 2-way sync**. Parts 2 and 3 target exactly those.

---

# Part 1 — Integration architecture

## 1.1 The one abstraction that prevents future rework

Define a single **Host Adapter Contract** in `stingtools-core`. Every host (Revit, Bonsai,
ArchiCAD-via-IFC, Tekla-via-IFC) satisfies the same contract; all host-specific behaviour
hides behind it and everything else is shared and tested once.

```
Host Adapter Contract  (identical seam for all four hosts)
  read_elements()             → iterate host elements
  global_id(el)               → 22-char IFC GlobalId          (the cross-host key)
  host_element_id(el)         → host-native id (ElementId / obj name / GUID)
  read_tag(el)                → STING 8-segment tag pset
  write_tag(el, tag)          → host-correct, undo-aware mutation
  apply_remote_change(delta)  → pull side: apply a Planscape change locally
  georef_descriptor()         → the model's coordinate evidence (see Part 2)
  host_name                   → "revit" | "bonsai" | "archicad" | "tekla"
```

- **Core owns:** tag grammar, enum/pset loading, IDS, the Planscape wire contract, the
  sync reconciliation logic, **and the coordinate-alignment math**.
- **Adapters own *only* the contract methods.** No tag rules, no enum logic, no
  validation, no alignment math in an adapter — ever. This boundary is enforced in CI
  (see §3.5). It is the single discipline that keeps the system sustainable for years and
  makes "add Tekla / ArchiCAD" cost one thin adapter rather than a platform change.

Because ArchiCAD and Tekla are **IFC-route only**, their "adapters" are not in-application
plugins — they are **IFC-ingest profiles** that run server-side / in StingBridge against an
exported `.ifc`. The same contract still applies (`global_id` = IFC GlobalId,
`host_element_id` = ArchiCAD/Tekla GUID), which is why they slot in without core changes.

## 1.2 Delivery surfaces

| Surface | Runs in | Gets `ifcopenshell` from | Needs Bonsai? | Hosts served |
|---|---|---|---|---|
| StingTools-for-Bonsai add-on | Blender, interactive | Bonsai (hard dependency) | **Yes** | Bonsai |
| StingBridge CLI / worker | Server / Docker / cron | `pip install ifcopenshell` | No | ArchiCAD-IFC, Tekla-IFC, IFC drops |
| Revit plugin | Revit | Revit API + IFC export | No | Revit |
| Planscape Server | Cloud | (receives pushes; xBIM/IfcOpenShell server-side) | No | all — federation owner |

Nothing bundles `ifcopenshell` *inside the Blender extension zip*: the add-on borrows
Bonsai's copy; the headless worker pip-installs its own. That avoids per-platform
native-wheel packaging entirely.

## 1.3 Foundation hardening (must land before new features)

| # | Item | Why |
|---|---|---|
| 1.3.1 | **Hard-dependency cleanup**: require Bonsai; delete the dead standalone fallback; clear "install Bonsai" message if absent | The advertised standalone mode can't work (stock Blender has no ifcopenshell) — stop promising it |
| 1.3.2 | **Fix the write path** — route mutations through Bonsai's `tool.Ifc.run` when present (real undo + UI refresh); `ifcopenshell.api.run` only headless | Current `BonsaiBridge.add_pset` calls bare `ifcopenshell.api.run` even with Bonsai loaded → Ctrl-Z and panel refresh silently don't work; contradicts `BONSAI_RELATIONSHIP.md` |
| 1.3.3 | **Extract the Host Adapter Contract** into core; point Bonsai ops at core | Establishes the seam ArchiCAD/Tekla reuse |
| 1.3.4 | **Substrate drift-check** on login — compare each host's `shared/ifc` SHA-256 manifest against the server's; warn on mismatch | One source of truth across four hosts |
| 1.3.5 | **Version pinning at every seam** — Bonsai range in manifest, ifcopenshell pin in StingBridge, versioned Planscape DTO | Unpinned seams are where "worked yesterday" bugs come from; ifcopenshell version skew (Bonsai's bundled copy vs pip copy) is the prime offender |

## 1.4 Bidirectional sync (push exists; build pull + reconcile in core)

- **1.4.1 Pull client** in `stingtools-core/planscape`: `GET /ifc/mappings` + issues/changes
  since a cursor. Host-agnostic.
- **1.4.2 Reconciliation engine** in core: diff remote vs local, resolve by
  **last-writer-wins on `LastModifiedUtc`** (the server already enforces stale-write
  protection — reuse that contract). Emits `apply_remote_change` deltas the adapter executes.
- **1.4.3 Conflict policy** written once: simultaneous edits to the same GlobalId default to
  LWW and **surface the loser as a Planscape issue** rather than silently clobbering.
- **1.4.4 Client-side chunking** on push (large models → batched POSTs, not one multi-MB body).
- **1.4.5 GlobalId stability fixture** (standing CI test): one element, Revit→IFC→Bonsai,
  assert identical GUID. This is the linchpin of *every* cross-host join and *every*
  coordinate transform keyed on a model — guard it permanently.

Pull is delivered on both live surfaces from the same core engine:

- **Bonsai add-on** → a **modal timer operator** polling on an interval + a manual "Sync now"
  button; applies deltas via `tool.Ifc.run` so undo/UI stay correct. (Blender can't easily
  hold a SignalR socket with stdlib; polling is the pragmatic first cut. SignalR is a later
  swap *inside* the pull client — no contract change.)
- **StingBridge** → a `sync` subcommand on a `--watch-interval` loop; applies deltas via
  `ifcopenshell.api.run` and writes the `.ifc` back.

---

# Part 2 — Federated coordinate alignment engine

> **The problem in one sentence:** four authoring tools each place geometry in their own
> internal coordinate frame, with their own idea of origin, north, elevation datum and units;
> Planscape must put all four into one correctly-positioned federation **automatically**, and
> it must do so even when a model arrives with poor or missing georeferencing — because we
> cannot fix the export at source with a native plugin for ArchiCAD or Tekla.

## 2.1 Why this is hard (the four-tool reality)

Each tool exposes a different set of "origins", and each exports IFC differently:

| Tool | Internal origin | "World" anchor concept | IFC export reality |
|---|---|---|---|
| **Revit** | Internal Origin (fixed, ±~20 mi limit) | Project Base Point (PBP) + **Survey Point** (SP) = shared coordinates | "Export in shared coordinates" / site-placement options decide whether the IFC carries the SP as `IfcMapConversion`. Project North vs True North. |
| **ArchiCAD** | Project Origin (0,0,0) | **Survey Point** (recent ArchiCAD) defines IFC global 0,0,0 + rotation | Survey Point export now supported, but only if configured in the IFC translator + Project Location. Project North separate. |
| **Tekla** | Model origin | **Base point(s)** — distinct base points for export vs import | Notorious: an IFC that imports correctly into Revit-at-origin is *wrong* elsewhere unless base-point offsets are matched. Often ships **no `IfcMapConversion`** at all. |
| **Bonsai / IfcOpenShell** | Blender world origin | Georeferencing via `ifcopenshell.api.georeference` → `IfcMapConversion` | Carries whatever georeferencing the authoring set; a freshly modelled Blender file usually has **none**. |

The consequences that break naïve federation:
- **Different internal origins** → models stack at the wrong relative position.
- **Project North vs True North** → models rotated relative to each other.
- **Different elevation datum** → floors at the wrong Z.
- **mm vs m unit mismatch** → 1000× scale blow-ups.
- **Missing georeferencing** → no shared frame exists to place into at all.

## 2.2 The governing standard — Level of Georeferencing (LoGeoRef 0–50)

buildingSMART/academia define a quality ladder for how well an IFC is georeferenced. We use
it as the **decision tree** for which placement strategy to apply per model:

| LoGeoRef | Evidence in the IFC | Placement quality | Typical source |
|---|---|---|---|
| **50** | `IfcMapConversion` (Eastings/Northings/OrthogonalHeight + XAxisAbscissa/Ordinate rotation + Scale) **+** `IfcProjectedCRS` (EPSG) | **Exact**, CRS-based — federation is deterministic | Well-configured Revit/ArchiCAD (IFC4) |
| **40** | `IfcGeometricRepresentationContext.WorldCoordinateSystem` + `TrueNorth` | Project-local with rotation, no CRS | Many IFC4 exports |
| **30** | `IfcSite` (or building) `ObjectPlacement` carries the world offset | Positioned, no CRS, datum implicit | Common |
| **20** | `IfcSite.RefLatitude/RefLongitude/RefElevation` | Coarse (metre-ish) geodetic | Older/lighter exports |
| **10** | `IfcPostalAddress` only | Address, not coordinates | Minimal |
| **0** | Nothing | **Unplaceable from metadata** | Fresh Bonsai/Tekla models |

**Today's `AutoAlignService` handles LoGeoRef 50 only** (it `Fail`s with "no IfcMapConversion
data" otherwise). Tekla and Bonsai routinely arrive at LoGeoRef 0–30. **Closing that span is
the core of this part.**

## 2.3 Target architecture — a tiered placement resolver

Introduce a **Federation Placement Resolver** in core (math) + server (orchestration) that,
for each uploaded model, picks the **best available** strategy and produces a single 4×4
**federation transform** (a similarity transform: translation + Z-rotation + uniform scale —
the correct model for BIM; no pitch/roll/shear) into the **canonical project frame** defined
by `ProjectCoordinateSystem`.

```
ingest model.ifc
   │
   ├─ IfcAlignmentValidator → georef descriptor + LoGeoRef tier   (extend existing)
   │
   ▼
Federation Placement Resolver  (pick highest tier available)
   ├─ T50  IfcMapConversion + CRS      → exact transform        ← BUILT (AutoAlignService)
   ├─ T40  WCS + TrueNorth             → transform (no CRS)      ← GAP
   ├─ T30  IfcSite ObjectPlacement     → transform (datum-implicit) ← GAP
   ├─ T20  Lat/Lon/Elev → CRS E/N      → coarse transform, flagged ← GAP
   ├─ T00  geometric registration      → control-point / grid / ICP ← GAP
   └─ MAN  coordinator picks 3 points  → exact manual transform  ← GAP (always-available escape hatch)
   │
   ▼
ProjectModelTransform  { matrix, confidence, source-tier, isConfirmed, isLocked }   (extend existing)
   │
   ▼
FederatedModelHub (SignalR) → live viewer + plugins refresh
```

### 2.3.1 Canonical frame (already the right design)

`ProjectCoordinateSystem` is the single source of truth: project CRS (EPSG), benchmark origin
(E/N/elev), true north, unit, and a nominated **reference model**. Every model is transformed
*into this frame*. Keep this; it is the correct anchor. The coordinator sets it once (or it is
seeded from the first georeferenced upload).

### 2.3.2 Tier resolvers to build (the gap)

- **T40 / T30 — placement-chain extraction.** Extend `IfcAlignmentValidator` (or move to a
  shared IfcOpenShell pass) to read `IfcSite`/`IfcBuilding` `ObjectPlacement` and the model
  `WorldCoordinateSystem` + `TrueNorth`, producing a translation+rotation even with no
  `IfcMapConversion`. `ifcopenshell.util.placement` gives the local-placement → 4×4 matrix
  directly; this is far more robust than the current header regex and should become the
  primary extractor, with the regex retained only as a no-IfcOpenShell fallback.
- **T20 — geodetic → CRS.** Convert `IfcSite` lat/lon/elev into the project CRS easting/
  northing (`ifcopenshell.util.geolocation` + a CRS transform), yielding a coarse but useful
  seed placement, flagged *low confidence* so the coordinator knows to verify.
- **T00 — geometric registration** (the hard, high-value one, because Tekla/Bonsai land here):
  1. **Shared control points** — if both the new model and the reference carry named control
     points / survey markers / a shared grid origin, solve the rigid transform from ≥3
     correspondences (Umeyama/Kabsch).
  2. **Grid-intersection matching** — match `IfcGrid` line intersections between models
     (structural grids are the most reliable common feature across Revit/Tekla/ArchiCAD).
  3. **Bounding-box / centroid pre-alignment + ICP refine** — coarse-align by centroid
     (the ingester already computes a centroid proxy), then iterative-closest-point refine on
     a decimated point sample. Lowest confidence; always offer manual confirmation.
- **MAN — coordinator manual override** (must exist regardless): a viewer tool to pick the
  same 3 points in the new model and the reference, solve the transform, **lock** it. This is
  the guaranteed escape hatch for any model the automatic tiers can't place — and the honest
  answer to "we can't fix Tekla's export at source."

### 2.3.3 Deterministic auto-corrections on ingest (don't make humans re-export)

Where the fix is unambiguous, **apply it server-side** instead of emitting a WARN and waiting:

- **Unit normalization** — if a model declares mm and the project is m (or vice-versa), apply
  the 1000× scale into its federation transform automatically (today this is only a WARN).
- **Server-side georeference stamping** — for a model that arrives at LoGeoRef 0 but for which
  we can derive the correct world placement (project CRS + a known control point, or an
  accepted manual alignment), **write `IfcMapConversion` + `IfcProjectedCRS` into a derived
  copy** via `ifcopenshell.api.georeference.add_georeferencing`. This upgrades a raw Tekla/
  Bonsai IFC to LoGeoRef 50 *inside Planscape* — the single most powerful automation available
  to us given that we have no native ArchiCAD/Tekla plugin to fix the source. The original
  upload is preserved; the georeferenced derivative is what federates and what downstream
  tools can re-download.

### 2.3.4 Confidence, provenance, lock (extend `ProjectModelTransform`)

Every transform must carry:
- **source tier** (T50 / T40 / T30 / T20 / T00 / MAN),
- **confidence** (exact / coarse / geometric / manual),
- **isConfirmed** (coordinator accepted) and **isLocked** (frozen — auto-align won't touch it),
- **residual/fit error** for geometric tiers (so a bad ICP is visible, not silent).

The federated viewer colour-codes models by confidence so a coordinator sees at a glance which
models are exactly placed vs. which need a human look. Maximum automation does **not** mean
"silently guess" — it means "place everything we can prove, flag the rest, and make the manual
path one click."

### 2.3.5 Level / storey reconciliation (Z axis)

`ProjectLevel` already maps tool level names. Add **auto-mapping by elevation proximity**: after
each model is placed, cluster its storeys' world elevations against existing project levels and
auto-bind names within a tolerance (e.g. ±150 mm), leaving only genuine conflicts for the
coordinator. This closes the "Revit Level 01 at +0.000 vs Tekla 1st Floor at +0.150" class of
mismatch that survives even perfect XY alignment.

## 2.4 The IFC-route pre-flight (our leverage without native plugins)

Because ArchiCAD/Tekla cannot be fixed at source by us, the IFC-route pre-flight is where we
maximize first-pass success. `IfcAlignmentValidator` already emits tool-specific guidance
(e.g. "Re-export from ArchiCAD: Options > Project Preferences > Survey Point" / "Revit: Manage >
Coordinates > Acquire Coordinates"). Extend it into a **per-tool export recipe card** surfaced
in the upload UI, plus a **machine-readable pre-flight verdict** so StingBridge can reject /
auto-correct a drop before it pollutes the federation:

- **Revit** — export in shared coordinates; PBP/SP set; True North set; IFC4; units = project unit.
- **ArchiCAD** — Survey Point placed + included in the IFC translator; Project Location set;
  Project North set; IFC4; working units = project unit.
- **Tekla** — export using the agreed base point; confirm base-point offset to project benchmark;
  prefer IFC4 with georeferencing; verify it isn't an analytical/stick export (the validator
  already detects ETABS/SAP/SAFE/RAM stick models — keep that gate).
- **Bonsai** — set georeferencing via Bonsai's georeference panel before export; project CRS +
  control point; IFC4. (For Bonsai we *can* do better than pre-flight: the add-on can read the
  project's `ProjectCoordinateSystem` from Planscape and write the matching `IfcMapConversion`
  directly — closing the loop the native-plugin-less hosts can't.)

## 2.5 Putting it together — the automatic federation pipeline

```
upload / drop  →  validate (LoGeoRef tier + coherence)
              →  auto-correct deterministic issues (units, project georef stamp)
              →  resolve placement at best tier  →  ProjectModelTransform (+confidence)
              →  auto-map levels by elevation
              →  publish to FederatedModelHub  →  viewer + plugins show the combined model
              →  only genuinely ambiguous placements queue for coordinator (manual point-pick)
```

The design goal: **a clean four-tool project federates with zero human alignment steps; a messy
one degrades gracefully to "confirm these N models" rather than "manually align everything."**

---

# Part 3 — Flexibility & maximum-automation gap review

Beyond coordinates, these are the logic gaps that limit flexibility and automation across the
whole multi-host pipeline.

| # | Gap | Today | Target (max automation) |
|---|---|---|---|
| 3.1 | **Findings are advisory, not actionable** | Validator emits WARN/FAIL; human must re-export | Auto-fix the deterministic subset (units, georef stamp); queue only the ambiguous. The validator becomes a *corrector*, not just a critic. |
| 3.2 | **Alignment is manual-triggered** | `AutoAlignService.ComputeAsync` is invoked on demand | Auto-trigger the full pipeline on ingest (validate → correct → place → level-map → publish) as a background job; idempotent re-runs |
| 3.3 | **Three federation axes resolved unevenly** | Identity (mapping) ✅, Coordinates (T50 only) ⚠️, Levels (names, no auto-bind) ⚠️ | All three auto-resolve with confidence + manual override on each |
| 3.4 | **One-way data flow** | Push only (Bonsai→Planscape) | 2-way (Part 1.4) so a Revit/coordinator change flows back to Bonsai/IFC hosts |
| 3.5 | **No boundary enforcement** | Logic could leak into adapters → future drift | CI lint forbidding tag/enum/validation/alignment logic in adapter files; the contract is the only extension point |
| 3.6 | **Geometric fallback absent** | Un-georeferenced models can't be placed at all | T00 registration + manual point-pick (Part 2.3.2) |
| 3.7 | **No server-side georef authoring** | We can only *ask* a tool to fix its export | Stamp `IfcMapConversion` into a derived IFC server-side (Part 2.3.3) — turns "please re-export" into "already placed" |
| 3.8 | **Confidence is invisible** | A transform is a transform | Tier + confidence + residual on every placement, colour-coded in the viewer (Part 2.3.4) |
| 3.9 | **Pre-flight is prose** | Human reads guidance text | Machine-readable per-tool verdict StingBridge can gate/auto-correct on (Part 2.4) |
| 3.10 | **Real-time transport is polling-bound** | Pull will poll | Keep polling as the portable default; allow SignalR swap inside the pull client for hosts that can hold a socket (server + viewer already use `FederatedModelHub`) |

**Flexibility principle threaded through all of the above:** every automatic decision (placement,
unit correction, level binding, conflict resolution) is **overridable and lockable** by the
coordinator, and every host is a thin adapter behind one contract. That combination is what makes
the product both maximally automatic *and* safe to trust on a live coordination project.

---

## 4. Sequencing

| Phase | Scope | Outcome |
|---|---|---|
| **A — Foundation** | §1.3 (hard-dep cleanup, `tool.Ifc.run` fix, extract Host Adapter Contract, substrate drift-check, pinning, CI boundary lint) | Clean, enforced architecture; the standalone promise retired |
| **B — 2-way sync** | §1.4 (core pull + reconciliation + chunking + GlobalId fixture) | Bidirectional on Bonsai + StingBridge from one core engine |
| **C — Coordinate engine core** | §2.3.2 T40/T30/T20 resolvers via `ifcopenshell.util.placement`/`geolocation`; §2.3.4 confidence/lock on `ProjectModelTransform`; §2.3.5 level auto-map; §3.2 auto-trigger pipeline | Most real-world models federate automatically, not just LoGeoRef-50 ones |
| **D — Robust fallback + corrections** | §2.3.2 T00 geometric registration + MAN point-pick; §2.3.3 unit auto-correct + server-side georef stamping; §2.4 machine-readable pre-flight | Tekla/Bonsai/raw exports place or degrade gracefully; "re-export" becomes "auto-placed" |
| **E — ArchiCAD-IFC & Tekla-IFC adapters** | §1.1 IFC-route adapters (no native plugins) reusing B/C/D verbatim | Both hosts coordinate end-to-end with **zero core change** — proof the abstraction held |

Phases A–D are the near-term build. E is deliberately last and deliberately thin: by the time we
reach it, ArchiCAD and Tekla are "configure an IFC-ingest profile," not "build a platform."

## 5. Explicitly deferred (scope honesty)

- **Native ArchiCAD plugin** and **native Tekla (Open API) plugin** — out of scope now; IFC route
  only. The Host Adapter Contract leaves the slot open to promote either later with no rework.
- **SignalR pull transport** — polling first; SignalR is an internal swap, not a redesign.
- **Full Helmert/7-parameter datum transforms** — we use a similarity transform (translation +
  Z-rotation + uniform scale), correct for building-scale BIM; geodetic datum shifts beyond the
  project CRS are out of scope.
- **Healthcare IDS overlay / extra psets** — additive later.

## 6. Top risks carried into delivery

| Risk | Severity | Mitigation in this plan |
|---|---|---|
| Logic creep into adapters | 🔴 | §3.5 CI boundary lint + the written contract |
| `ifcopenshell` version skew (Bonsai bundled vs pip) | 🟠 | §1.3.5 pinning + test both versions |
| GlobalId disagreement across hosts (breaks identity *and* per-model transforms) | 🟠 | §1.4.5 standing fixture test |
| Geometric registration places a model wrongly but silently | 🟠 | §2.3.4 residual/confidence surfaced; manual confirm required below a confidence floor |
| Coordinate drift between exports breaks BCF/clash history | 🟠 | already handled (`IfcAlignmentValidator` drift detection) — keep + extend to gate auto-republish |
| Tekla/ArchiCAD ship LoGeoRef 0 and we can't fix the source | 🟠 | §2.3.3 server-side georef stamping + §2.4 pre-flight + MAN point-pick escape hatch |
| 2-way edit conflicts | 🟡 | §1.4.3 LWW, written once in core, conflicts surfaced as issues |

---

## Sources (coordinate-alignment research)

- buildingSMART / Clemen & Görne — *Level of Georeferencing (LoGeoRef) using IFC for BIM*
  (LoGeoRef 0–50 ladder; `IfcMapConversion` + `IfcProjectedCRS` for LoGeoRef 50).
  https://jgcc.geoprevi.ro/docs/2019/10/jgcc_2019_no10_3.pdf ·
  https://github.com/dd-bim/IfcGeoRef/blob/master/Documentation_v3.md
- IfcOpenShell docs — `ifcopenshell.api.georeference`, `ifcopenshell.util.geolocation`
  (local↔global Helmert transform; auto-detection of map conversion; `Scale` semantics).
  https://docs.ifcopenshell.org/autoapi/ifcopenshell/api/georeference/index.html ·
  https://docs.ifcopenshell.org/autoapi/ifcopenshell/util/geolocation/index.html
- Tekla User Assistance — *Base point for coordination with Autodesk Revit* (base-point offset
  matching; IFC links land at Revit origin, not base point).
  https://support.tekla.com/article/tekla-structures-base-point-for-coordination-with-autodesk-revit
- Graphisoft Community — *Survey Point is now supported at IFC import/export* (ArchiCAD Survey
  Point defines IFC global 0,0,0 + rotation; recommended common anchor across Revit/Tekla).
  https://community.graphisoft.com/t5/Collaboration-with-other/Survey-Point-is-now-supported-at-IFC-import-export/ta-p/304009
- BIMcollab Help Center — *Coordinating IFC Models with World Coordinate System information*.
  https://helpcenter.bimcollab.com/en/articles/326917-coordinating-ifc-models-with-world-coordinate-system-information
- *Revit, IFC and coordinate systems* (BIM me UP!) — Revit's four reference points and IFC export
  behaviour. https://bim-me-up.com/en/revit-ifc-and-coordinate-systems/
