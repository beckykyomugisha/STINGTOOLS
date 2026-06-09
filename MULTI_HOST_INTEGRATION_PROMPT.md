# Prompt — Implement the Multi-Host Integration & Federated Coordinate Alignment Engine

Hand this to a **build-capable agent** (.NET 8 + Python 3.11 + Node + Blender 4.2/Bonsai).
Mission: implement the plan in **`docs/MULTI_HOST_INTEGRATION_PLAN.md`** — make Planscape
combine Revit · Bonsai · ArchiCAD · Tekla models into one correctly-positioned federation
**automatically**, with **bidirectional** host sync, behind **one host-adapter contract**.

`docs/MULTI_HOST_INTEGRATION_PLAN.md` is the **authoritative spec**. This prompt is the
**executable runner**: concrete files, APIs, acceptance criteria, and build gates. Where the two
disagree, the plan wins — and you STOP and flag the discrepancy rather than guessing.

Repo: `beckykyomugisha/stingtools`. Branch off the latest default branch; never commit to it
directly. Re-enumerate the live codebase — trust the tree, not this doc's line numbers.

---

## 0. Locked product decisions (do not relitigate)

1. **Hard dependency on Bonsai** for the interactive Blender add-on. **Delete** the standalone
   (no-Bonsai) fallback. Stock Blender has no `ifcopenshell`, so "standalone in Blender" is a
   dead promise — remove it from code, manifest, and `BONSAI_RELATIONSHIP.md`.
2. **Headless/batch** runs via `stingtools-core` + `StingBridge` with a **pip-installed**
   `ifcopenshell`. **Never bundle `ifcopenshell` into the Blender extension zip.**
3. **Bidirectional** sync (push + pull) for every host.
4. **ArchiCAD and Tekla are IFC-route only.** **No native ArchiCAD plugin. No native Tekla
   plugin.** Their "adapters" are server/StingBridge IFC-ingest profiles, not in-app plugins.
5. **Coordinate alignment must be automatic**, degrading gracefully to a one-click manual
   point-pick — never to "manually align everything."
6. **Hub-and-spoke, never host pairs.** Each host ⇅ Planscape is bidirectional **once**; any
   combination of hosts (Revit+Bonsai, Revit+ArchiCAD, all four, any subset, mixed teams) then
   coordinates **and runs model reviews** through the hub for free. Building any pairwise
   host-to-host link is a **failure** — a host only ever talks to Planscape. The currency through
   the hub, all keyed on IFC **GlobalId**: federation transforms + identity mapping +
   **issues/BCF/clash round-trip**.
7. **Model review is a first-class outcome.** Planscape already has the review surface
   (`FederatedModelController`/`Hub`, `IssuesController` + comments/audio/heatmap,
   `BcfController`/`BcfApiController`/`OpenCdeController`, `ClashesController`,
   `ModelMarkupsController`, `MeetingsController`, `GlobalIdRegistryController`). **Extend it to
   flow across any host combination — do not rebuild it.**

---

## 1. Hard rules (violating any is a failure)

1. **Build-gate every phase.** A phase is not "done" until its target projects compile/lint and
   its new tests pass. Red and not cleanly fixable → stop, report, do not push broken work.
2. **Additive, not destructive.** Don't break existing endpoints, the `AutoAlignService`
   LoGeoRef-50 path, the live Bonsai push (`host=bonsai`), or `IfcController`/`TagSyncController`.
   Extend; don't replace working code.
3. **Core/adapter boundary is sacred.** Tag grammar, enum/pset logic, IDS, alignment math, and
   the wire contract live in `stingtools-core` (Python) / shared server services (.NET). Host
   adapters contain **only** the contract methods — no business logic. You will also *add the CI
   lint that enforces this* (Phase A). If you find yourself writing alignment math in an
   operator file, you're in the wrong file.
4. **One source of truth.** Read enums/psets/IDS from `shared/ifc/`. Never fork them per host.
5. **No secrets, no force-push, no PR unless asked.** Commit to the working branch with clear
   messages; push with `-u origin <branch>`; retry network failures with backoff.
6. **Confidence over silent guessing.** Every automatic coordinate placement records a
   confidence tier + residual. Below the confidence floor, require human confirmation — do not
   silently place a model wrongly.
7. **EF migrations are explicit.** Any entity/schema change ships a named migration
   (`dotnet ef migrations add <Name>`), noted in the phase report. Never edit a shipped migration.
8. **When genuinely ambiguous, STOP and ask** via the report — don't fabricate a decision the
   plan didn't authorize (e.g. choosing a CRS, a confidence threshold the plan didn't set).

---

## 2. Environment & build gates

```bash
# .NET server
dotnet build  Planscape.Server/Planscape.Server.sln           # must be green
dotnet test   Planscape.Server                                 # new + existing tests pass
dotnet ef migrations add <Name> -p <Infra> -s <API>            # for any schema change

# Python core (pure, no bpy)
cd stingtools-core/python && python -m pytest                  # must be green, headless
pip install -e .                                               # importable

# StingBridge (headless host worker)
cd StingBridge && pip install -r requirements.txt && python -m pytest

# Bonsai add-on — headless smoke (no GUI)
blender --background --python stingtools-bonsai/tests/verify_blender.py
```

If a real Blender/Revit/Bonsai isn't available in the sandbox, **say so in the report** and mark
those checks ❓ rather than ✅ — do not claim verification you didn't run. (This repo's
convention is to note "built without dotnet build / Blender verification" honestly.)

---

## 3. Current baseline (verify before extending)

| Built | Where |
|---|---|
| LoGeoRef-50 auto-align (translation+Z-rot+scale from `IfcMapConversion`) | `AutoAlignService.ComputeAsync` |
| Canonical anchor (EPSG, origin E/N/elev, true north, unit, reference model) | `ProjectCoordinateSystem` |
| Similarity transform persistence | `ProjectModelTransform` (TranslationX/Y/Z, RotationDeg, ScaleFactor, IsAutoComputed, IsConfirmed) |
| Header-parse validator (map conversion, CRS, true north, units, drift, coherence, stick-model) | `IfcAlignmentValidator` |
| Endpoints | `AlignmentController` (List, GetForModel, RunCoherence, AutoAlign), `CoordinateSystemController` (Get/Create/Update/Delete) |
| Live federation push | `FederatedModelHub` (SignalR) |
| Cross-tool level names | `ProjectLevel.ToolMappingsJson` + `ElevationM` |
| Cross-host identity | `ExternalElementMapping`, `IfcController` `/ifc/data` + `/ifc/mappings`, `CrossHostMappingReconciliation` |
| Bonsai add-on | `stingtools-bonsai/ops/{tagging,validation,select,coord}_ops.py`, `planscape/{client,ingest}.py` |
| Pure core | `stingtools-core/python/stingtools_core/*` (no `bpy`) |
| Headless worker | `StingBridge/*` (IFC drop-watcher, ArchiCAD/IFC, pip `ifcopenshell`) |

**The gap:** everything below LoGeoRef 50, the pull half of 2-way sync, and the
core/adapter/boundary discipline. Phases A–E close it.

---

## PHASE A — Foundation hardening (do first; everything sits on it)

**A1. Hard-dependency cleanup** — `stingtools-bonsai/`
- Require Bonsai at registration; if absent, render an N-panel banner "Install Bonsai" and
  disable STING ops. Remove the standalone `ifcopenshell.api.run` fallback paths and the
  "standalone mode" prose in `BONSAI_RELATIONSHIP.md` + `blender_manifest.toml`.

**A2. Fix the write path** — `stingtools-bonsai/core/bonsai.py`
- `BonsaiBridge.add_pset` / `edit_attribute` must route through Bonsai's transaction-aware
  layer when present: prefer `bonsai.tool.Ifc.run("pset.add_pset", …)` /
  `tool.Ifc.run("pset.edit_pset", …)` (try the `bonsai` → `bonsai_bim` → `blenderbim` module
  names, mirroring the existing defensive probing). Only fall back to bare
  `ifcopenshell.api.run` in the headless worker, never in-Blender. **Acceptance:** a STING tag
  write in Blender is undoable with Ctrl-Z and refreshes Bonsai's property panel.

**A3. Extract the Host Adapter Contract** — `stingtools-core/python/stingtools_core/hosts/`
- Define an abstract `HostAdapter` with exactly these methods:
  `read_elements()`, `global_id(el)`, `host_element_id(el)`, `read_tag(el)`,
  `write_tag(el, tag)`, `apply_remote_change(delta)`, `georef_descriptor()`, `host_name`.
- Move tag-assembly / discipline-inference / SEQ logic out of `ops/tagging_ops.py` into core;
  the Bonsai op becomes thin glue calling core. Provide a `BonsaiHostAdapter` and an
  `IfcFileHostAdapter` (headless) implementing the contract. ArchiCAD/Tekla reuse
  `IfcFileHostAdapter` (Phase E) — no new adapter logic.

**A4. Substrate drift-check** — core + server
- On login, compare the host's `shared/ifc` SHA-256 manifest against a server endpoint
  (`GET /api/substrate/manifest`); warn on mismatch. Reuse the existing checksum locks.

**A5. Version pinning** — pin a Bonsai version range in `blender_manifest.toml`, pin
`ifcopenshell` in `StingBridge/requirements.txt` to the version Bonsai bundles, and version the
Planscape ingest DTO.

**A6. Boundary CI lint** — `.github/workflows/`
- A check that **fails** if adapter files (`stingtools-bonsai/ops/*`, future host adapters)
  import or define tag-grammar / enum / IDS / alignment logic, or import anything other than
  `bpy` + `stingtools_core` + stdlib. This is the rule that keeps the system sustainable.

**Phase A DoD:** Bonsai requires Bonsai; writes are undo-aware; contract exists with two
adapters; drift-check live; pins in place; boundary lint green.

---

## PHASE B — Bidirectional sync (push exists; build pull + reconcile in core)

**B1. Pull client** — `stingtools-core/python/stingtools_core/planscape/`
- `pull_changes(project_id, since_cursor) -> list[ChangeDelta]` over `GET /ifc/mappings` +
  **issues/BCF/clash** changes. Stdlib `urllib` only (match existing `client.py`). Host-agnostic.
- **Carry the review payload, not just tags** — issue create/update/close, BCF topic + viewpoint,
  clash status — all keyed on GlobalId. This is what makes model review work across *any*
  combination of hosts: a review action in the hub or any host round-trips to every other host.
  Reuse existing server controllers (`IssuesController`, `BcfApiController`/`OpenCdeController`,
  `ClashesController`); do **not** add pairwise host-to-host paths.

**B2. Reconciliation engine** — core
- `reconcile(adapter, remote_deltas) -> list[apply]`, resolving by **last-writer-wins on
  `LastModifiedUtc`** (server already enforces stale-write protection — reuse that contract,
  don't reinvent). Conflicts → emit a Planscape issue, never silently clobber.

**B3. Chunking** — chunk push payloads (the server batches at 500; the client must not POST a
100k-element body in one request — keep each request inside the 30s timeout).

**B4. GlobalId stability fixture** — `tests/`
- A standing test: one element exported Revit→IFC and opened via ifcopenshell, assert its
  `GlobalId` is identical to what the Revit push carried. This guards every cross-host join
  **and** every per-model transform. Mark ❓ if no Revit fixture is available, but scaffold it.

**B5. Pull on both live surfaces** (same core engine):
- **Bonsai** — a **modal timer operator** (`bpy.types.Operator` with `modal()` + a
  `wm.event_timer_add`) polling at an interval + a manual "Sync now" button; applies deltas via
  `tool.Ifc.run` so undo/UI stay correct.
- **StingBridge** — a `sync` subcommand on `--watch-interval`; applies deltas via
  `ifcopenshell.api.run` and writes the `.ifc` back.

**Phase B DoD:** a change made in one host (or by a coordinator) reaches another host
automatically; an **issue/BCF/clash raised in one host or in the hub viewer surfaces in every
other host on the project** (keyed on GlobalId); conflicts surface as issues; chunking handles
large models. No pairwise host-to-host code exists — everything routes through Planscape.

---

## PHASE C — Federated coordinate engine core (the heart)

Implement a **Federation Placement Resolver** that, per model, picks the **best available
LoGeoRef tier** and produces one similarity transform into the canonical project frame. Build the
math in a shared place so server (.NET) and StingBridge (Python) both use it. Prefer **IfcOpenShell
in a Python extraction service** over the current header-regex — the regex stays only as a
no-IfcOpenShell fallback.

**C1. Georef descriptor + tier classification** — extend `IfcAlignmentValidator` (or add a
Python `GeorefExtractor` invoked at ingest) to emit a `LoGeoRefTier` (0/20/30/40/50) plus the
evidence for each. Use:
- `ifcopenshell.util.placement.get_local_placement(site.ObjectPlacement)` → 4×4 numpy matrix
  (LoGeoRef 30/40 — robust, replaces regex).
- `ifcopenshell.util.geolocation.auto_local2global` / `auto_global2local` (auto-detect
  `IfcMapConversion`; return 4×4) and `auto_xyz2enh` / `xyz2enh` / `auto_z2e` for LoGeoRef 50.
- `IfcSite.RefLatitude/RefLongitude/RefElevation` → project CRS for LoGeoRef 20.

**C2. Tier resolvers** — for each tier, compute translation + Z-rotation + uniform scale into the
`ProjectCoordinateSystem` frame:
- **T50** — keep existing `AutoAlignService` math; route it through the new resolver.
- **T40/T30** — from the site/WCS placement matrix + `TrueNorth`.
- **T20** — geodetic → CRS easting/northing (coarse; flag low confidence).

**C3. Extend `ProjectModelTransform`** (+ EF migration `AddTransformConfidence`):
- `SourceTier` (enum string: `T50|T40|T30|T20|T00|MAN`), `Confidence`
  (`Exact|Coarse|Geometric|Manual`), `ResidualMm` (nullable), `IsLocked` (bool).
- Auto-align must **never** overwrite a transform with `IsLocked=true`.

**C4. Level auto-map (Z axis)** — after placement, cluster each model's storey world-elevations
against `ProjectLevel.ElevationM` within a tolerance (default ±150 mm, configurable on
`ProjectCoordinateSystem`); auto-bind names into `ToolMappingsJson`; leave only genuine conflicts
for the coordinator.

**C5. Auto-trigger pipeline** — on ingest, run `validate → resolve placement → level-map →
publish to FederatedModelHub` as a background job (Hangfire, matching existing patterns).
Idempotent; re-runnable; skips locked transforms.

**Phase C DoD:** a model with *any* of LoGeoRef 20–50 auto-places (not just 50); transforms carry
tier+confidence; levels auto-bind; the viewer updates live.

---

## PHASE D — Robust fallback + deterministic corrections (covers Tekla/Bonsai/raw)

**D1. Geometric registration (T00)** — Python service, used when LoGeoRef = 0:
1. **Shared control points / survey markers / shared grid origin** → solve the rigid (or
   similarity) transform from ≥3 correspondences via **Umeyama/Kabsch** (least-squares,
   numpy/SVD). Record `ResidualMm`.
2. **`IfcGrid` intersection matching** between target and reference (structural grids are the
   most reliable cross-tool feature).
3. **Centroid pre-align + ICP refine** on a decimated point sample (lowest confidence).
- Below a confidence floor, set `IsConfirmed=false` and require coordinator confirmation.

**D2. Manual point-pick (MAN)** — endpoint + viewer tool: coordinator picks ≥3 matching points in
target and reference; server solves Umeyama → writes a `MAN`, `IsConfirmed=true`,
`IsLocked=true` transform. This is the guaranteed escape hatch; it must exist regardless of D1.

**D3. Deterministic auto-corrections on ingest** (don't make humans re-export):
- **Unit normalization** — if the model unit ≠ project unit, fold the 1000× (or 0.001×) scale
  into the transform automatically (today only a WARN).
- **Server-side georeference stamping** — for a placeable LoGeoRef-0 model (project CRS + a known
  control point, or an accepted MAN alignment), write `IfcMapConversion` + `IfcProjectedCRS`
  into a **derived** IFC via `ifcopenshell.api.georeference.add_georeferencing` /
  `edit_georeferencing`. Preserve the original upload; the georeferenced derivative is what
  federates and what hosts can re-download. **This is the top automation for the plugin-less
  hosts** — it upgrades raw Tekla/Bonsai exports to LoGeoRef 50 inside Planscape.

**D4. Machine-readable per-tool pre-flight** — extend `IfcAlignmentValidator` findings into a
structured verdict + per-tool export-recipe cards (Revit / ArchiCAD / Tekla / Bonsai — see the
plan §2.4) so StingBridge can gate/auto-correct a drop before it pollutes the federation. Keep
the existing analytical/stick-model gate.

**D5. Bonsai closes its own loop** — the add-on reads the project `ProjectCoordinateSystem` from
Planscape and writes matching `IfcMapConversion` *before* export (via `tool.Ifc.run` /
`ifcopenshell.api.georeference`), so Bonsai-authored models ship LoGeoRef 50 natively.

**Phase D DoD:** an un-georeferenced Tekla/Bonsai IFC either auto-places (geometric or stamped) or
degrades to a one-click point-pick; unit mismatches self-correct; pre-flight is machine-readable.

---

## PHASE E — ArchiCAD-IFC & Tekla-IFC adapters (thin, IFC-route only)

- Implement ArchiCAD and Tekla as **IFC-ingest profiles** reusing `IfcFileHostAdapter` +
  Phases B/C/D **verbatim**. `global_id` = IFC GlobalId; `host_element_id` = ArchiCAD/Tekla GUID.
  Wire `MappingHosts.ArchiCAD` / `MappingHosts.Tekla` (already defined) through the same
  validate→place→sync pipeline. The existing `XbimIfcIngester` ArchiCAD/Tekla pset sniffing
  stays the source detector.
- **No native plugin code.** If Phase E requires any change to `stingtools-core` or the server
  alignment math, **stop** — that means the contract abstraction failed and the plan must be
  revisited before proceeding.

**Phase E DoD:** an ArchiCAD IFC and a Tekla IFC each federate end-to-end (placed + identity-
mapped + 2-way synced) with **zero change** to core or alignment services — proving the
abstraction held.

---

## 4. Definition of Done — verification matrix (fill this in the report)

| Check | Phase | ✅/❓/❌ |
|---|---|---|
| Bonsai requires Bonsai; standalone removed | A | ✅ code · ❓ Blender render |
| STING writes are undo-aware (`tool.Ifc.run`) | A | ✅ code · ❓ Blender undo |
| Host Adapter Contract + 2 adapters; boundary lint green | A | ✅ (50 core tests) |
| Substrate drift-check + version pins | A | ◐ pins ✅ · core hash ✅ · server `/api/substrate/manifest` ❌ (needs .NET) |
| Core pull + LWW reconcile + chunking | B | |
| Pull carries issues/BCF/clash review payload (cross-host review) | B | |
| Any host combination coordinates + reviews via hub; **no pairwise code** | B/E | |
| GlobalId stability fixture | B | |
| Bonsai modal-timer pull + StingBridge `sync` | B | |
| LoGeoRef 20/30/40 resolvers (ifcopenshell) | C | |
| `ProjectModelTransform` tier/confidence/residual/lock + migration | C | |
| Level auto-map by elevation | C | |
| Auto-trigger ingest pipeline | C | |
| Geometric registration (Umeyama/grid/ICP) + residual | D | |
| Manual point-pick endpoint + lock | D | |
| Unit auto-correct + server-side georef stamping | D | |
| Machine-readable per-tool pre-flight | D | |
| Bonsai writes project georef before export | D | |
| ArchiCAD-IFC + Tekla-IFC federate, zero core change | E | |
| All build gates green (or ❓ with reason) | all | |

---

## 5. Guardrails / known traps

- **Similarity transform only** (translation + Z-rotation + uniform scale) — correct for
  building-scale BIM. Do **not** introduce pitch/roll/shear or full 7-parameter datum shifts
  (out of scope per the plan).
- **mm vs m** — `ProjectModelTransform` is in project units (mm default). Survey origins in
  `IfcMapConversion` are metres. Keep the ×1000 discipline; don't double-apply scale.
- **Don't overwrite locked/confirmed transforms** in any auto path.
- **ifcopenshell version skew** — the headless `ifcopenshell` must match Bonsai's bundled
  version; geolocation function signatures shifted across 0.7→0.8 (`auto_*` helpers landed in
  0.8). Pin and test.
- **GlobalId is the linchpin** — if Revit and Bonsai disagree on an element's GUID, both identity
  and per-model transforms break. The B4 fixture is non-negotiable.
- **Preserve the live `host=bonsai` push** and both existing ingest paths.

## 6. Reporting

End with a single report: the verification matrix above (honestly marked), the EF migrations
added, any ❓/❌ with the reason, any place you had to STOP for an authorization gap, and the
commit/branch. Note clearly anything built **without** real Blender/Revit/dotnet verification.

---

### Reference APIs (pinned for the implementer)

- `ifcopenshell.util.placement.get_local_placement(placement) -> np.ndarray (4×4)` —
  site/element world matrix (LoGeoRef 30/40).
- `ifcopenshell.util.geolocation.{auto_local2global, auto_global2local, auto_xyz2enh,
  auto_enh2xyz, xyz2enh, enh2xyz, auto_z2e, get_wcs}` — map-conversion-aware transforms
  (LoGeoRef 50); `auto_*` auto-detect `IfcMapConversion` and return the matrix unchanged when
  absent. https://docs.ifcopenshell.org/autoapi/ifcopenshell/util/geolocation/index.html
- `ifcopenshell.api.georeference.{add_georeferencing, edit_georeferencing}` — stamp
  `IfcMapConversion` + `IfcProjectedCRS` server-side (Phase D3).
  https://docs.ifcopenshell.org/autoapi/ifcopenshell/api/georeference/index.html
- Bonsai write path: `bonsai.tool.Ifc.run("pset.edit_pset", …)` (undo + UI aware).
- Umeyama/Kabsch (similarity transform from ≥3 point correspondences via SVD) for geometric
  registration — standard least-squares; record RMS residual as `ResidualMm`.
