# System Status

Per-host / per-surface status of the Planscape federation, with the
evidence each "WORKING" claim rests on. CI-green is the gate for code;
the rows below additionally record real runtime verification.

| Surface | Status | Evidence |
|---|---|---|
| **Bonsai (Blender) → Planscape push** | ✅ **WORKING** | Verified end-to-end in Blender 4.2.21 + Bonsai (ifcopenshell 0.8.4) against a live server — see below |
| Revit → Planscape (`/tagsync`, `/ifc/data`) | ✅ working | existing |
| ArchiCAD → Planscape (`/ifc/ingest`) | ✅ working | existing |
| Cross-host resolve (`/ifc/mappings`) | ✅ working | bonsai + revit proven below |

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
