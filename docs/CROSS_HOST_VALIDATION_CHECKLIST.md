# Cross-host validation checklist (Revit + ArchiCAD + Planscape)

**Status:** Runnable manual test script — **NOT yet executed**. Written for a
human on a Windows machine with Revit, ArchiCAD, and the Planscape stack.
Produced as Pre-merge **Gate 3** for branch `claude/upbeat-cori-vdOPA`.

This script validates the plugin-side cross-host pieces that compile but have
**never executed against a real Revit/ArchiCAD API** in this branch:

- **BLK-1** — geometry sync reads the canonical 22-char IFC GlobalId from
  `IFC_GLOBAL_ID_TXT` (not Revit's 45-char `UniqueId`).
  `StingTools/Commands/IFC/GeometrySyncHandler.cs:157`
- **H-1** — the Revit `/ifc/data` producer `PushIfcDataAsync`.
  `StingTools/BIMManager/PlanscapeServerClient.IfcData.cs:41`
- **BLK-3** — cross-host round-trip: same GlobalId resolves to multiple hosts
  via `GET /api/projects/{id}/ifc/mappings`.
  `Planscape.Server/src/Planscape.API/Controllers/IfcController.cs:93`
- **BLK-2** — explicit-axis cross-host model transform / federation overlay.
  `Planscape.Server/src/Planscape.API/Controllers/ModelTransformController.cs:17`

> Server-side, the cross-host identity contract (H-1 ingest + BLK-3 resolution)
> and BLK-2 transform math are already machine-verified — see
> `docs/COORDINATION_AUDIT_FINDINGS.md` → "Pre-merge gates". This checklist
> covers the remaining gap: the **plugin** halves running against the **real
> host APIs**, which cannot be exercised in a Linux/headless sandbox.

---

## 0. Prerequisites & environment

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 0.1 | Start the stack: `cd Planscape.Server/docker && $env:JWT_KEY=(openssl rand -base64 48); docker compose up -d postgres redis api` | `docker ps` shows `postgres (healthy)`, `redis`, `api` up | ☐ |
| 0.2 | `curl http://localhost:5000/health` | `{"status":"healthy", ..."database":{"healthy":true}...}` | ☐ |
| 0.3 | Tail the API log in a second terminal: `docker compose logs -f api` (keep open — you will grep `[ifc-ingest]` here) | live log stream | ☐ |
| 0.4 | In Revit: STING dock panel → **BIM** tab → connect to Planscape (sign in as `admin@planscape.demo` / `admin123` or your tenant account). Note the **Project** you bind to and copy its **projectId** (GUID) from the dashboard URL or `GET /api/projects`. | Plugin reports "Connected"; you have a `projectId` | ☐ |
| 0.5 | Install the STING shared parameters (dock panel → **Setup → Load Params**) so `IFC_GLOBAL_ID_TXT` is bound. | `IFC_GLOBAL_ID_TXT` appears under Manage → Shared Parameters | ☐ |

> Substitute `:5000` (docker) or `:5080` (local `dotnet run`) consistently below.
> Set `$P = "<projectId>"` and `$T = "<bearer token from login>"` in your shell.

---

## 1. Revit — stabilise GlobalIds and confirm 22-char canonical key (BLK-1)

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 1.1 | Open a real Revit model with modelled elements. STING dock → **BIM** tab → run **Stabilize IFC GUIDs** (command tag `IFC_StabilizeGuids`, `StabilizeIfcGuidsCommand`). | TaskDialog reports *N written, 0 conflicts* (or lists conflicts to resolve). | ☐ |
| 1.2 | Select any wall → Properties → find `IFC_GLOBAL_ID_TXT`. | Value is present and **exactly 22 characters** (IFC base64 GlobalId), e.g. `3qoVHv8R0kg5pZWvTabcDE`. **Not** a 45-char Revit UniqueId, **not** empty. | ☐ |
| 1.3 | Export the model to IFC (File → Export → IFC). Open the `.ifc` in a text editor, find that same wall's `IFCWALL(...)` line. | The IFC `GlobalId` on that entity **equals** the `IFC_GLOBAL_ID_TXT` value from 1.2, character-for-character. | ☐ |
| 1.4 | Note the wall's GlobalId — call it **`$G`** (you reuse it in §3). | `$G` is a 22-char string. | ☐ |

**BLK-1 (geometry-sync key):** STING dock → enable **Geometry Sync** (or
trigger a sync/Sync-with-Central). Geometry deltas are pushed via
`GeometrySyncHandler` → `PostGeometryDeltaAsync`, which keys each element on
`IFC_GLOBAL_ID_TXT` (the 22-char value), **not** `Element.UniqueId`.

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 1.5 | Nudge the wall 100 mm and let geometry sync fire (or run **Push Model** / `IFC_PushModel`). Watch the API log. | Sync completes without error; the federated model updates. | ☐ |
| 1.6 | Confirm the key used is the 22-char GlobalId, not the 45-char UniqueId (inspect the push payload in the plugin log, or that the resulting mapping in §3 carries `$G`). | The cross-host key is `$G` (22 chars). | ☐ |

---

## 2. Revit — push IFC data via H-1 (`PushIfcDataAsync`)

`PushIfcDataAsync` (H-1) is the Revit producer for
`POST /api/projects/{id}/ifc/data`. It sets `host="revit"` and carries each
element's `ifcGlobalId` (= `IFC_GLOBAL_ID_TXT`) + `hostElementId`. This is what
populates the cross-host `ExternalElementMapping` table with `Host="revit"`.

> **Note (branch state):** as of this branch `PushIfcDataAsync` is the wired
> *producer method* and its `/ifc/data` contract is server-verified, but it may
> not yet have a dedicated dock-panel button (the dock currently exposes
> geometry/model push). Use whichever applies:
>
> - **(a)** If a "Push IFC Data" / equivalent button exists in your build, click it.
> - **(b)** Otherwise reproduce the exact plugin wire-contract with a REST call
>   (identical body shape to `PushIfcDataAsync`), substituting `$G`/`$P`/`$T`:
>
>   ```powershell
>   $body = @{ host="revit"; hostDocumentGuid="<RVT-doc-guid>"; pluginVersion="stingtools-revit";
>     elements=@(@{ ifcGlobalId="$G"; hostElementId="<Revit ElementId of the wall>";
>       hostDisplayLabel="Wall.042"; discipline="A"; fullTag="A-BLD1-Z01-L01-ARC-WALL-EXT-0042" }) } | ConvertTo-Json -Depth 6
>   Invoke-RestMethod "http://localhost:5000/api/projects/$P/ifc/data" -Method Post `
>     -ContentType "application/json" -Headers @{Authorization="Bearer $T"} -Body $body
>   ```

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 2.1 | Trigger the Revit `/ifc/data` push (2a or 2b). | Response `{ newMappings>=1, newElements>=1, skipped=0 }`. | ☐ |
| 2.2 | In the API log, find the new instrumentation line. | `[ifc-ingest] cross-host upsert host=revit project=$P ... keys=1 ... sampleKeys=[$G]` — confirms the resolved key + host. | ☐ |
| 2.3 | Confirm the mapping row landed with the right host + key: `GET /api/projects/$P/ifc/mappings?ifcGuid=$G`. | One item with `host=revit`, `ifcGlobalId=$G`, `hostElementId=<your Revit ElementId>`. | ☐ |

---

## 3. ArchiCAD / StingBridge — push the same project, same key (BLK-3)

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 3.1 | Open the matching model in ArchiCAD (the same building so element `$G` exists when exported to IFC). Confirm the StingBridge add-on is connected to the same Planscape **project** (`$P`). | Bridge reports connected to project `$P`. | ☐ |
| 3.2 | In ArchiCAD, identify the element whose IFC GlobalId equals `$G` (Element Settings → IFC Manager / tags). If ArchiCAD assigns a different GlobalId than Revit for the "same" physical element, that is the real-world cross-host gap — record it; the cross-host key is only meaningful when both hosts agree on `$G` (e.g. a coordinated IFC GlobalId). | The ArchiCAD element carries GlobalId `$G`. | ☐ |
| 3.3 | Push from StingBridge to `/ifc/data` (host is set to `archicad` by the bridge). If pushing manually, reuse the §2(b) body with `host="archicad"`, `hostElementId="<ArchiCAD GUID>"`, same `ifcGlobalId="$G"`. | Response `{ newMappings>=1 }`. | ☐ |
| 3.4 | API log shows the second ingest line. | `[ifc-ingest] cross-host upsert host=archicad project=$P ... sampleKeys=[$G]`. | ☐ |
| 3.5 | **Cross-host resolution:** `GET /api/projects/$P/ifc/mappings?ifcGuid=$G`. | `totalCount=2`; items include **both** `host=revit` and `host=archicad` for the same `$G`, each with its own `hostElementId`. | ☐ |
| 3.6 | Reverse direction also resolves: `GET /api/projects/$P/ifc/resolve?guid=$G`. | Returns host element refs for both hosts. | ☐ |

> **Pass condition for BLK-3:** a single 22-char GlobalId `$G` returns Revit
> **and** ArchiCAD element references from one project — an issue raised in one
> host can be located in the other.

---

## 4. Federation overlay — two world-spaces, ModelTransform (BLK-2)

This exercises BLK-2 (explicit-axis cross-host transform) on **real geometry**,
beyond the existing deterministic unit test.

| # | Step | Expected | Pass/Fail |
|---|------|----------|-----------|
| 4.1 | Publish **two** models to project `$P` authored in **different world spaces** — e.g. the Revit model (survey/shared coords) and the ArchiCAD model (project origin offset by a known translation + rotation). Use STING dock → **Publish Model to Planscape** (`PublishModelToPlanscape`) for each, or upload GLB/IFC via `POST /api/projects/$P/models`. | Two model rows: `GET /api/projects/$P/models` returns both, each streams (`/file` → 200 + `Content-Length`). | ☐ |
| 4.2 | Open the web viewer for project `$P`. Both models load. | Initially the two models are **offset/rotated** relative to each other (they don't line up). | ☐ |
| 4.3 | Apply the alignment transform to the second model: `PUT /api/projects/$P/models/{modelBId}/transform` with the explicit-axis translation + rotation (or use the viewer's auto-align / align UI). | `200`; transform persisted (`GET …/transform` echoes it). | ☐ |
| 4.4 | Reload / refresh the viewer (the `/hubs/model` `ModelUpdated` event should also push the change live). | The two models now **visually overlay** — shared grids/levels/columns line up in 3D. | ☐ |
| 4.5 | Spot-check a coincident element (e.g. a shared column) — it should sit in the same world position from both models after the transform. | Coincident geometry overlaps within tolerance. | ☐ |

---

## 5. Result summary

| Block | What was validated | Verdict |
|-------|--------------------|---------|
| BLK-1 | `IFC_GLOBAL_ID_TXT` = 22-char GlobalId = exported IFC GlobalId; geometry sync keys on it | ☐ PASS / ☐ FAIL |
| H-1 | Revit `/ifc/data` push lands `ExternalElementMapping` rows `host=revit` keyed on `$G` | ☐ PASS / ☐ FAIL |
| BLK-3 | Same `$G` resolves to **both** `revit` and `archicad` via `/ifc/mappings` | ☐ PASS / ☐ FAIL |
| BLK-2 | Two differently-world-spaced models visually overlay after `ModelTransform` | ☐ PASS / ☐ FAIL |

**Tester:** ____________________  **Date:** ____________  **Build/commit:** ____________

Record any FAIL with the API-log `[ifc-ingest]` line, the `/ifc/mappings`
response body, and a viewer screenshot, then file against this branch before
merge.
