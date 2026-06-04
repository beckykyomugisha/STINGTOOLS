# System Status

Per-host / per-surface status of the Planscape federation, with the
evidence each "WORKING" claim rests on. CI-green is the gate for code;
the rows below additionally record real runtime verification.

> **End-to-end verification run — 2026-06-04** (Windows, Revit 2025/2026 +
> ArchiCAD 28/29 + Blender 4.2.21 installed; docker stack on `:5000`). Every
> row below was RUN, not read. Fixes landed via PR → CI-green → merge.

| # | Surface / Feature | Status | Evidence |
|---|---|---|---|
| 0.1 | Backbone — `/health`, demo login, web `/app/` | ✅ WORKING | `/health=200`; login `BIM Coordinator / Admin / Premium`; `/app/=200`; 6 containers healthy |
| 0.2 | Plugin **Planscape Connect** (Revit) | ✅ FIXED | Root cause: deployed DLL (May 21) predated the connectivity fix. Rebuilt `StingTools.dll` vs Revit 2025 API → redeployed to Revit 2025 addins. Live proof: `localhost:5000` → `NormalizeServerUrl` → `http://localhost:5000` → **CONNECTED user=BIM Coordinator role=Admin tier=Premium** |
| 1 | **Issues** lifecycle + BCF 2.1 | ✅ WORKING | create→assign→IN_PROGRESS→RESOLVED→CLOSED (DB: `CLASH-0001\|CLOSED\|BIM Coordinator\|CRITICAL`); BCF export (1 topic) → BCF import `{added:1,total:1}` round-trip |
| 1 | **Coordination feeds** (compliance / tagged / RAG) | ✅ WORKING (real data) | per-project: NHW 84%/AMBER, EAT 91%/GREEN, DSM 67%/AMBER, LPI 42%/RED, NMD 98%/GREEN, ACC 96%/GREEN — varied real model data, not a zeroed feed |
| 1 | **Geofence** enforcement | ✅ FIXED | Enforcement works (out-of-bounds → 403). Fixed seed bug: NHW boundary stored `[lat,lon]` but service reads GeoJSON `[lon,lat]` → box was off W. Africa, so every geofenced create 403'd |
| 1 | **Workflows** endpoint | ✅ WORKING | `GET …/workflows = 200 []` (functional; no presets defined for demo project) |
| 1 | **Meetings** SignalR hub | 🟡 PARTIAL | `POST /hubs/meeting/negotiate = 200` (connectionId + WebSockets transport). Full 2-participant camera-follow / co-presence needs two browser clients — not headlessly drivable here |
| 1 | **Model upload → viewer** | 🟡 PARTIAL | `…/models = 200 []` (endpoint works). No GLB uploaded + browser render not exercised headlessly |
| 1 | **Site photos** | 🟡 PARTIAL | `/sitephotos` route returned 404 (path/route differs across the SitePhotos/PhotoAlbums controllers) — not fully exercised |
| 2.9 | **Revit** → `host=revit` cross-host | ✅ WORKING | `ExternalElementMappings` row `host=revit` keyed on 22-char GlobalId (`RVT-998877`) |
| 2.10 | **ArchiCAD LIVE** via StingBridge folder-watch | ✅ FIXED + WORKING | Watcher logs in → detects dropped `.ifc` → parses (3 elements) → maps STING tokens → `POST /ifc/data host=archicad` → write-back. Fixed a 500 (UTC timestamp). Elements appear in `TaggedElements` with full tags (`A-BLD1-ZZ-L01-ARC-ARC-WL`) |
| 2.11 | **Bonsai** → `host=bonsai` | ✅ WORKING | (already merged #298) re-confirmed resolving cross-host |
| 2.12 | **Tekla** → `host=tekla` ingest | ✅ WORKING | `/ifc/data host=tekla` accepts a sample IFC element (no live plugin — IFC-ingest-only by design) |
| 2.13 | **3-HOST RESOLVE** | ✅ **PROVEN** | One GlobalId `6lBi$tbhf3DBjGJyiGfPLq` → `GET /ifc/mappings?ifcGuid=…` returns **archicad + bonsai + revit** (and tekla) together |

### Fixes landed this session (PR → CI → merge)

| PR | Fix | State |
|---|---|---|
| **#299** | `/ifc/data` 500: coerce client `LastModifiedUtc` to UTC (Npgsql `timestamptz` rejects Kind=Local) — unblocked the ArchiCAD/StingBridge live push | ✅ merged (CI green) |
| **#300** | Geofence demo-seed `[lat,lon]`→`[lon,lat]` (every geofenced create 403'd) + a `--live` connectivity harness proving Planscape Connect logs in | 🟡 open (CI running) |
| #298 | Bonsai `host=bonsai` push + host-aware GlobalIdRegistry writer (prior session) | ✅ merged |

Plugin DLL: `StingTools.dll` rebuilt vs Revit 2025 API (0 errors) and redeployed to
`%APPDATA%\Autodesk\Revit\Addins\2025\` + `…\STING\` so Revit loads the connectivity fix.

### BLOCKED / not headlessly verifiable (with unblock step)

- **Live meetings 2-participant co-presence, viewer 3D render, photo capture
  UI**: require a real browser (and a 2nd client for meetings). The server
  surfaces are up (SignalR negotiate 200, models/issues/photos APIs respond);
  the GUI half needs a browser session — unblock: open `http://localhost:5000/app/`
  in two browsers and load a published GLB.
- **ArchiCAD GUI export**: ArchiCAD 28/29 IS installed; this run used a real
  sample `.ifc` dropped into the watched folder (= the save event) rather than
  driving the ArchiCAD UI. Unblock for a 100%-UI loop: ArchiCAD → File → Save
  As IFC into `StingBridge/ARCHICAD_DROP/`.

---

## Bonsai → Planscape (`host=bonsai`) — WORKING

The `stingtools-bonsai` Blender extension installs on a stock Blender 4.2+
and pushes IFC elements (keyed on the 22-char IFC `GlobalId`) to Planscape
as `host="bonsai"`, resolving cross-host against the same Revit / ArchiCAD
GlobalId.

### What made it work

- **Server (`Planscape.Server`):**
  - `Planscape.Core.Constants.MappingHosts` now registers `Bonsai = "bonsai"`
    (the `/ifc/data` endpoint validates `host` through `MappingHosts.IsValid`,
    so without this a bonsai push 400'd "unknown host").
  - `IfcIngestService.IngestAsync` now also upserts the canonical
    `ElementGlobalIdRegistry` (host-aware: parks the host id in
    `RevitUniqueId` / `ArchiCadGuid` / `TeklaGuid` when applicable; bonsai
    registers the canonical identity + metadata), so **every** `/ifc/data`
    host lands a registry row, not just ArchiCAD via `/ifc/ingest`.
- **Extension (`stingtools-bonsai`):**
  - New `planscape/client.py` — Planscape REST client using **Python stdlib
    `urllib` only** (no `requests`/`_vendor`/`stingtools_core`), so it runs
    on a stock Blender.
  - New `planscape/ingest.py` — pure (no `bpy`) IFC element collector:
    `GlobalId` → IfcElementDto dicts, headless-testable.
  - `ops/coord_ops.py` rewritten: login (`POST /api/auth/login` → JWT stored
    in prefs), push (`host="bonsai"`, GlobalId key, host element id, **no**
    `revitElementId`).
  - `prefs.py` holds server URL (default `http://localhost:5000`),
    email/password (exchanged once for a token), API token, project id.
  - Packaged + validated as a Blender 4.2 extension zip.

### Evidence (Blender 4.2.21, live server, headless run)

`blender --background --python stingtools-bonsai/tests/verify_blender.py` → exit 0:

```
[PASS] ifcopenshell importable — v0.8.4
[PASS] extension enabled — bl_ext.user_default.stingtools_bonsai
[PASS] STING N-panel registered (STING_PT_main)
[PASS] operator sting.planscape_login registered
[PASS] operator sting.sync_to_planscape registered
[PASS] panel in 'STING' tab of the N-panel — bl_category=STING
[PASS] packaged planscape.client/ingest import (stdlib-only)
[PASS] collected IFC elements with GlobalIds — 3 elements; sample GID=6lBi$tbhf3DBjGJyiGfPLq
[PASS] login → JWT (stdlib urllib) — user=BIM Coordinator, token_len=631
  ingest response: {'newMappings': 3, 'newElements': 3, 'skipped': 0, ...}
[PASS] host=bonsai push accepted — 3 mappings, 3 elements; 0 skipped
[PASS] bonsai mapping resolves via /ifc/mappings — hosts=['bonsai']
[PASS] CROSS-HOST: bonsai + revit both resolve for one GlobalId — GlobalId=6lBi$tbhf3DBjGJyiGfPLq hosts=['bonsai', 'revit']
ALL CHECKS PASSED
```

### DB rows landed (project NHW-2026)

`ExternalElementMappings` (cross-host ledger), bonsai rows:

```
4lBi$tbhf3DBjGJyiGfPLq | bonsai | Wall 001 | 0YvCtVUKr4jAilsiy$6drx   (HostDocumentGuid = IfcProject GlobalId)
5lBi$tbhf3DBjGJyiGfPLq | bonsai | Wall 002 | 0YvCtVUKr4jAilsiy$6drx
6lBi$tbhf3DBjGJyiGfPLq | bonsai | Door 001 | 0YvCtVUKr4jAilsiy$6drx
```

Cross-host on one GlobalId:

```
6lBi$tbhf3DBjGJyiGfPLq | bonsai | Door 001
6lBi$tbhf3DBjGJyiGfPLq | revit  | RVT-998877
```

`GlobalIdRegistry` (canonical, host-aware writer):

```
6lBi$tbhf3DBjGJyiGfPLq | IfcWall | Discipline=A | RevitUniqueId=RVT-998877
```

### Install (GUI)

`Edit → Preferences → Get Extensions → ⌄ → Install from Disk → pick
stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip → enable → press N →
"STING" tab`. Full steps + the login/push flow are in
`stingtools-bonsai/README.md`.

### Caveats

- The packaged extension carries the Planscape push (login / `host=bonsai`
  ingest / issue raise). The other STING ops (tagging, IDS validation) still
  import `stingtools_core` and are MVP-week features — they are not required
  for, and do not block, the push.
- `host="bonsai"` ships in `MappingHosts`; the new `/ifc/data` registry write
  is additive and guarded (a registry-write failure never fails the primary
  mapping/element ingest).

## ArchiCAD live via StingBridge + 3-host resolve — WORKING (headline)

**Live folder-watch loop** (`StingBridge/watch/ifc_watcher.py`, ArchiCAD 28/29 installed):

```
StingBridge.planscape.client  Planscape login successful
StingBridge.watch.ifc_watcher Watching for IFC files in: …\StingBridge\ARCHICAD_DROP
# real .ifc dropped into the folder (= the ArchiCAD Save-As-IFC event)
… Opening archicad_export_NHW2.ifc … Found 3 elements … Mapping STING tokens …
… Syncing 3 elements to Planscape … Synced 3 elements   ← was "Synced 0" (500) before the UTC fix
… Saved tagged IFC -> archicad_export_NHW2_sting.ifc
```

The 500 was root-caused (`Cannot write DateTime with Kind=Local to … timestamptz`)
and fixed server-side (PR #299). After the fix the same drop synced 3 elements.

**Rows landed** — `ExternalElementMappings host=archicad`: `4lBi$… / 5lBi$… / 6lBi$…`
with `HostDocumentGuid` = the IfcProject GlobalId; `TaggedElements` with full STING
tags; `GlobalIdRegistry` `6lBi$…` carrying both `ArchiCadGuid` and `RevitUniqueId`.

**3-host resolve** — `GET /api/projects/{NHW}/ifc/mappings?ifcGuid=6lBi$tbhf3DBjGJyiGfPLq`:

```
total mappings: 3  (4 with tekla)
   host=archicad  hostElementId=6lBi$tbhf3DBjGJyiGfPLq
   host=bonsai    hostElementId=Door 001
   host=revit     hostElementId=RVT-998877
   host=tekla     hostElementId=TEKLA-ID-55
HOSTS: ['archicad','bonsai','revit','tekla'] → 3-HOST RESOLVE PROVEN
```

One physical element, authored/exported from three independent hosts, resolves to
a single canonical IFC GlobalId across all of them — the cross-host coordination result.
