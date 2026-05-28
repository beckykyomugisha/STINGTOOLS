# STING / Planscape — Platform Enhancement Proposal & Implementation Plan

**Scope:** Live 3D Meeting Viewer · IoT / Digital Twin · Cross-Platform Automation Spine · FM Lifecycle Continuity
**Surfaces:** STING (Revit plugin) · BCC (in-Revit) · Planscape Server · Planscape Web · Planscape Mobile (primary)
**Author:** Platform engineering · **Date:** 27 May 2026 · **Status:** Proposal v1.0 — for sign-off
**Companion docs:** Specification v1.0 (chat) · Phase 3A task breakdown (chat) · `docs/PHASE_186_BONSAI_INTEGRATION.md`

---

## 0. Recommendation in one line

**Lock this proposal as the contract, lock the five decisions (defaults below), then start immediately on the three keystones (K1–K3) before any feature work** — because IoT, meeting-sync, and FM continuity all reuse them. Build foundations once; inherit them everywhere.

We do **not** rebuild the viewer (Three.js + `coordination-viewer.js` + `RevitGltfExporter` + `XbimIfcIngester` already production). We wire, unify, and extend.

---

## 1. Why a doc-first-then-build approach (not "straight away")

| Risk of building features first | Mitigated by keystones-first |
|---|---|
| IoT invents a 3rd element-identity system | K1 unifies identity once → IoT inherits it |
| Every feature writes back to Revit its own way | K2 (event spine) → one durable, idempotent channel |
| Each overlay (diff/heatmap/twin/CX) is bespoke render code | K3 (`ViewerOverlayProfile`) → one render path, N feeds |
| Rework across 6 phases when foundations shift | Foundations frozen before dependents start |

The keystones are ~3 weeks combined and unblock everything. This is the highest-leverage sequencing.

---

## 2. Decisions — assumed defaults (confirm or override)

These are taken as **accepted defaults** so the plan is actionable. Flag any to change.

| # | Decision | Default (recommended) | Consequence if changed |
|---|---|---|---|
| D1 | Time-series store | **TimescaleDB extension on existing Postgres 16** | Influx = new infra + new client; avoid |
| D2 | Edge gateway | **`Planscape.Edge` thin .NET agent** (store-and-forward + protocol normalization) in front of **EMQX/MQTT** transport | Off-the-shelf-only = no offline buffer guarantee |
| D3 | IoT protocols v1 | **MQTT + BACnet/IP + Modbus** first; OPC-UA / LoRaWAN / KNX as later adapters | Reorders adapter backlog only |
| D4 | Model write-back conflict policy | **Reject-stale + merge prompt** (carry `BaseRevisionId`); never blind last-writer-wins | LWW risks silent model corruption |
| D5 | Web (browser) surface | **Full parity** for 3D + clash + minutes (shares `viewer.html`/`meeting-sync.js`); media UX only differs | Media-light secondary = lower BIM-manager value |
| D6 | Media provider | **Daily.co behind `IMediaProvider`**; migrate to LiveKit at 500+ mtg-hrs/mo as an adapter swap | None — abstraction is cheap insurance |

---

## 3. Workstream map (4 pillars + 3 keystones)

```
KEYSTONES (build first, ~3 wks, gate everything)
 K1  Unified element identity        (ExternalElementMapping is the one GUID)
 K2  Platform event spine            (PlatformEventLog + IExternalEventHandler drainer)
 K3  ViewerOverlayProfile contract   (one render path, N data feeds)

PILLAR A — Live 3D Meeting Viewer      (extends existing stack)
PILLAR B — IoT / Digital Twin          (the new frontier; reuses K1/K2/K3)
PILLAR C — Cross-Platform Automation   (closes every loop via K2)
PILLAR D — FM Lifecycle Continuity     (handover→operations seam)
```

---

## 4. Keystones (Phase 0 — start immediately, ~3 weeks)

### K1 — Unified element identity (I-gap G10)
**Goal:** viewer GUID map, Revit ElementId, federation hosts, and IoT devices all resolve through `ExternalElementMapping` (Phase 186).
- Server: `IdentityResolverService` over `ExternalElementMapping`; add `host="IoT"` as a first-class host.
- Viewer: `elementMeshes` lookups route through a resolver shim so a highlighted Bonsai/Tekla element maps back to the correct Revit element for write-back.
- Acceptance: highlight an element in any surface → resolve to the same canonical IFC GUID + Revit ElementId round-trip.

### K2 — Platform event spine (I/A/C-gaps S1–S4)
**Goal:** one durable, ordered, tenant-scoped channel for *every* cross-surface action.
- New entity `PlatformEvent` (id, tenant, project, source surface, type, payload JSON, `BaseRevisionId`, status, applied-at) + SHA-256 chain (reuse `AuditLog` pattern).
- `PlatformEventController` (append + ack); SignalR fan-out primary; plugin poll fallback (adaptive interval, D-confirmed).
- STING side: `IApplyPlatformEvent` interface; one `IExternalEventHandler` drainer applies under a single Transaction; idempotent by event id; **reject-stale** via `BaseRevisionId` (D4) → merge prompt.
- Acceptance: a mobile action lands as a Revit-side change with ack + audit row; replaying the same event is a no-op; stale event is rejected, not silently merged.

### K3 — `ViewerOverlayProfile` contract (F-gap G11/F1)
**Goal:** collapse diff / heatmap / HVAC-sizing / CX / QA / **twin** coloring into one render path.
- Contract: `{ mode, guidColorMap, legend[], source, updatedAt }` (JSON over the WebView bridge + SignalR).
- Viewer: single `applyOverlay(profile)` replacing per-feature coloring; legend overlay reused.
- Acceptance: diff, heatmap, and a synthetic twin feed all render through the same call with no feature-specific branches.

---

## 5. Pillar A — Live 3D Meeting Viewer

Builds on Specification v1.0 gaps G1–G8 **plus** the newly-found G9, G12–G14. (G10/G11 absorbed into keystones.)

| Phase | Delivers | Key items |
|---|---|---|
| **3A** Foundation (3 wks) | G1 camera sync, G2 highlight broadcast, G3 meeting-viewer binding, G8 BCC publish/open, **G9 conversion-status SignalR** | `MeetingSession`/`MeetingParticipant`, `MeetingRoomController`, `MeetingHub`, `meeting-sync.js` skeleton, dual-WebView mobile screen, Daily.co behind `IMediaProvider` (D6) |
| **3B** Annotations + diff (3 wks) | G4 annotation sync, G5 model-diff viz (via K3), **G13 replayable snapshots** | persist annotations + MinIO snapshot; `/api/models/{id}/diff` over `IfcDeltaService`; auto-diff on DrawingReview |
| **3C** Clash + 4D (4 wks) | G6 4D timeline activation, G7 clash-navigator-as-flow, **G12 meeting→issue→Revit loop via K2** | `Scheduling4DEngine` → `/4d-phases`; clash advance/resolve SignalR; post-meeting summary → minutes → deferred clashes auto-become `BimIssue` → spine → Revit |
| **3D** Engineering overlays (3 wks) | STING tag overlay, HVAC sizing mode, CX witness mode, QA punch navigator (all via K3) | each is just a `ViewerOverlayProfile` feed + a navigator pattern reuse |
| **3E** Web parity (1 wk) | G14 | browser tab gets full 3D+clash+minutes; only media UX gated |

The Phase 3A file-level task breakdown already produced in chat stands as the authoritative sub-plan for 3A.

---

## 6. Pillar B — IoT / Digital Twin (the differentiator)

Today: `Core/Twin/IoTDeviceRegistry`, `TwinReadback` (BACnet/OPC-UA **stubs**), `IoTMaintenanceCommands`, `QRCommissioningWorkflow`. We make it a running twin.

### 6.1 Architecture (reuses stack, no new DB, no new identity)
```
Field devices ──▶ Planscape.Edge (protocol adapters + store-and-forward)
   ──MQTT/TLS──▶ Planscape Server
       TelemetryIngestController ─▶ TimescaleDB hypertable (D1)
       DeviceTwinService     (last-known-state)
       TwinBindingService    (Device ↔ IFC GUID via ExternalElementMapping — K1)
       TwinRuleEngine        (STING_TWIN_RULES.json + project overlay)
       WorkOrderAutomator    (rule event → FM work order → K2 spine)
       SignalR TwinHub       (live state → viewer overlay K3 / mobile)
```

### 6.2 Phased build
| Phase | Delivers | Items (T-gaps) |
|---|---|---|
| **5A** Twin core (4 wks) | ingest + bind + live state | TimescaleDB migration; `ITelemetryAdapter` registry (T4) MQTT first; `TwinBindingService` on K1; `DeviceTwinService`; `TwinHub` |
| **5B** Edge + continuity (3 wks) | offline-safe ingest + handover seed | `Planscape.Edge` store-and-forward (T5); COBie `Component` → device registry seed (T3); CX sign-off → twin provision (T1) |
| **5C** Protocols (2 wks) | BACnet/IP + Modbus adapters (D3) | new adapters behind `ITelemetryAdapter`, zero core change (F-pattern) |
| **6A** Twin intelligence (4 wks) | rules + work orders + anomaly | `TwinRuleEngine` (T2); `WorkOrderAutomator`→K2; `TwinAnomalyDetector` EWMA/z-score (T8) |
| **6B** Condition-based maintenance (3 wks) | telemetry-driven PPM | `ConditionBasedMaintenancePlanner` advances/defers `COBIE_JOB_TEMPLATES` (T6) |
| **6C** Live healthcare compliance (3 wks) | continuous HTM evidence | feed `HealthcarePressureLog` / HTM-04-01 water-temp / NFPA-110 gen-test from telemetry (T7) → auto compliance certs |

### 6.3 Data model (new entities)
- `DeviceTwin` (id, projectId, externalMappingId→K1, protocol, assetTag, serial, lastSeen, healthState).
- `TelemetryPoint` (Timescale hypertable: deviceId, metric, value, unit, ts) — partitioned by time.
- `TwinRule` / `TwinAlert` (threshold/anomaly defs + fired events).
- `WorkOrder` (auto-raised, links device + element + assignee + due + status) — flows through K2.

### 6.4 UX
- **Mobile "Live" tab** (primary): RAG asset list, push alerts, one-tap acknowledge / raise-WO / view-in-3D (`viewer.html?overlay=twin&focusGuid=`).
- **Viewer**: `TwinHub` → K3 overlay updates element colors live (in normal or meeting sessions).
- **BCC**: "Building Live" panel beside model-health dashboards (the FM bookend).

---

## 7. Pillar C — Cross-Platform Automation Spine

K2 *is* the spine. Pillar C is the set of closed loops built on it:
1. Meeting deferred-clash → `BimIssue` → Revit element (G12).
2. Twin alert → work order → mobile + 3D pin → on-fix → Revit param stamp (T2).
3. Model publish → notify invitees + auto-diff vs last meeting version.
4. CX sign-off → twin provision (T1).
5. Telemetry trend → PPM advance/defer (T6).
6. Twin-binding drift detection (element deleted in Revit but device still reports → orphan flag) — extends your existing IUpdater drift pattern.

Each loop = a `PlatformEvent` type + an `IApplyPlatformEvent` handler. No bespoke channels.

---

## 8. Pillar D — FM Lifecycle Continuity

Closes the **handover→operations seam** (the only broken seam):
- **Seam 1 (handover):** COBie export *also* seeds `DeviceTwin` registry (T3) — register becomes live source-of-truth, not a dead file.
- **Seam 2 (operations):** as-maintained writeback (WO completion → Revit params via K2); asset-health dashboards fed by live telemetry; regulatory continuity (HTM/NFPA auto-evidenced — T7).

---

## 9. Critical path & sequencing

```
Phase 0  K1 ─┬─ K2 ─┬─ K3        (3 wks, all parallelizable after K1 day-1 stub)
             │       │
   ┌─────────┘       └──────────────┐
   ▼                                ▼
Pillar A 3A→3E (viewer)        Pillar B 5A→6C (IoT)      ── parallel tracks
   │                                │
   └──────────── Pillar C loops ────┘   (need K2; built incrementally per feature)
                       │
                       ▼
              Pillar D continuity (needs B + C)
```
- **K1 is the true critical-path root** — start it day 1.
- After Phase 0, **Pillar A and Pillar B run as parallel tracks** (different teams/areas).
- Pillar C grows organically as each A/B feature lands its loop.
- Pillar D is last (depends on B + C).

**Indicative timeline (single small team, sequential-ish):** ~7–8 months to D-complete. **With 2 parallel tracks post-Phase-0:** ~5 months.

---

## 10. Effort summary

| Phase | Weeks | Track |
|---|---|---|
| 0 — Keystones K1/K2/K3 | 3 | Foundation |
| 3A–3E — Viewer | 14 | Track A |
| 5A–5C — Twin core/edge/protocols | 9 | Track B |
| 6A–6C — Twin intelligence/CBM/healthcare | 10 | Track B |
| Pillar C loops | (folded into A/B) | both |
| Pillar D continuity | 4 | post |

---

## 11. Risks & mitigations

| Risk | L | I | Mitigation |
|---|---|---|---|
| Editing 4,802-line `coordination-viewer.js` regresses | M | H | All meeting/overlay logic external via `window.STING_VIEWER` + `meeting-sync.js`; never touch internals |
| Element-identity unification ripples widely | M | H | K1 first, behind a resolver shim with fallback to legacy GUID map; feature-flag |
| Telemetry volume swamps Postgres | M | M | TimescaleDB hypertable + retention/continuous-aggregates; edge pre-aggregation |
| WAN loss = telemetry loss | H | H | `Planscape.Edge` store-and-forward (T5) — non-negotiable for v1 |
| Model write-back conflicts corrupt model | L | H | D4 reject-stale + merge prompt; `BaseRevisionId` on every event |
| Built without `dotnet build` (Linux sandbox) | H | M | Per house convention: mark caveat in commits/CHANGELOG; verify in Revit before merge to `main` |
| Daily.co cost at scale | M | L | `IMediaProvider` (D6) → LiveKit swap |

---

## 12. Acceptance gates (per phase, before next starts)

- **Phase 0:** cross-surface highlight round-trips one canonical GUID (K1); mobile action lands in Revit with ack + audit + idempotency + stale-reject (K2); diff/heatmap/synthetic-twin render via one `applyOverlay` (K3).
- **Pillar A:** two devices follow-host in sync; deferred clash becomes a Revit `BimIssue` (G12); snapshot replays full overlay+section+phase (G13).
- **Pillar B:** real telemetry binds to an element and colors it live; threshold breach auto-raises a work order + mobile push + 3D pin; edge survives a WAN cut and replays.
- **Pillar D:** WO completion stamps an as-maintained Revit param; HTM pressure/water continuously evidenced.

Every phase logged in `docs/CHANGELOG.md`; open items tracked in `docs/ROADMAP.md`.

---

## 13. What we start with this week (if approved)

1. **K1** — `IdentityResolverService` + `host="IoT"` on `ExternalElementMapping`; viewer resolver shim (feature-flagged).
2. **K2** — `PlatformEvent` entity + EF migration + `PlatformEventController` + `IApplyPlatformEvent` skeleton + one drainer in STING.
3. **K3** — `ViewerOverlayProfile` contract + `applyOverlay()` in `viewer.html`, migrate the existing heatmap to it as proof.

These three land the foundation; Pillar A 3A and Pillar B 5A then start in parallel.

---

## 14. Confirmations requested

1. Approve **doc-first → keystones → parallel A/B** sequencing (§1, §9).
2. Confirm or override the six defaults D1–D6 (§2).
3. Confirm starting this week with K1–K3 (§13), or specify a different first slice.
