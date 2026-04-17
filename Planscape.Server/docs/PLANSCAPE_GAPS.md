# Planscape Gap Analysis: Mobile Collaboration, On-Site Readiness & Integration

**Date:** 2026-04-10
**Branch:** `claude/review-enhance-markdown-lGmRY`
**Scope:** Comprehensive analysis of gaps preventing Planscape from being used for real on-site BIM coordination

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Mobile App Gap Analysis](#2-mobile-app-gap-analysis)
3. [Server API Gap Analysis](#3-server-api-gap-analysis)
4. [Plugin Sync Integration Gap Analysis](#4-plugin-sync-integration-gap-analysis)
5. [On-Site Readiness Assessment](#5-on-site-readiness-assessment)
6. [Cost & Effort Estimates](#6-cost--effort-estimates)
7. [Prioritised Implementation Roadmap](#7-prioritised-implementation-roadmap)
8. [Architecture Recommendations](#8-architecture-recommendations)

---

## 1. Executive Summary

Planscape consists of three tiers:

| Tier | Technology | Status |
|------|-----------|--------|
| **StingTools Revit Plugin** | C# / .NET 8.0 / Revit API | Production-grade (755+ commands, 250K+ LOC) |
| **Planscape Server** | ASP.NET Core 8.0 / PostgreSQL / Redis / SignalR | Functional but incomplete (17 controllers, 29 DbSets) |
| **Planscape Mobile** | React Native / Expo v52 / TypeScript | Scaffold only (~40% complete) |

### Critical Finding

**The Planscape.PluginSync library (SyncClient.cs, OfflineQueue.cs, SyncScheduler.cs) is 100% dead code.** It is never instantiated or called by StingTools. The actual plugin-to-server communication uses a separate `PlanscapeServerClient` class in `PlatformLinkCommands.cs` that performs manual, on-demand sync only. This means:

- No automatic background sync exists between the Revit plugin and the server
- The `OnDocumentSaved` handler in `StingToolsApp.cs` queues data but nothing consumes it
- The 5-minute periodic sync documented in CLAUDE.md does not function

### Readiness Verdict

| Capability | Ready? | Blocking Issues |
|-----------|--------|-----------------|
| User authentication | Yes | — |
| View issues on mobile | Partial | No photo attachments, no GPS |
| Create issues on site | No | No camera capture, no GPS coordinates, no offline |
| View documents | Partial | No PDF viewer, no image thumbnails |
| QR code scanning | No | Scanner tab is a non-functional stub |
| Offline work | No | Queue exists but no conflict resolution, no retry |
| Real-time collaboration | No | SignalR hubs exist but mobile has no WebSocket client |
| Plugin ↔ Server sync | No | Dead code; manual sync only via separate client |
| Photo/snag documentation | No | No image upload, no annotation, no GPS tagging |

**Estimated effort to reach minimum viable on-site use: 12–16 developer-weeks.**

---

## 2. Mobile App Gap Analysis

### 2.1 Current Architecture

```
Planscape/
├── app/
│   ├── (tabs)/
│   │   ├── _layout.tsx          # Tab navigator (5 tabs)
│   │   ├── index.tsx            # Dashboard
│   │   ├── issues.tsx           # Issue list
│   │   ├── documents.tsx        # Document browser
│   │   ├── scanner.tsx          # QR scanner (STUB)
│   │   └── settings.tsx         # Settings
│   ├── _layout.tsx              # Root layout
│   └── login.tsx                # Auth screen
├── src/
│   ├── api/
│   │   ├── client.ts            # Axios HTTP client (97 lines)
│   │   └── endpoints.ts         # 11 API wrappers (100 lines)
│   ├── components/              # Shared UI components
│   ├── hooks/                   # Custom React hooks
│   ├── types/                   # TypeScript interfaces
│   └── utils/
│       ├── offlineQueue.ts      # AsyncStorage FIFO queue (124 lines)
│       └── theme.ts             # Theme constants
```

### 2.2 Critical Gaps

| ID | Gap | Severity | Description |
|----|-----|----------|-------------|
| MOB-01 | **QR Scanner non-functional** | CRITICAL | `scanner.tsx` (828 lines) imports `expo-camera` v16 but scanning result triggers `Alert.alert()` only — no API call, no element lookup, no tag resolution |
| MOB-02 | **No camera/photo capture** | CRITICAL | No image picker, no camera integration, no photo attachment to issues or documents |
| MOB-03 | **No GPS/location services** | CRITICAL | No `expo-location` dependency, no coordinate capture, no geofencing for site boundary |
| MOB-04 | **No offline sync engine** | CRITICAL | `offlineQueue.ts` supports 3 action types (CREATE_ISSUE, UPDATE_ISSUE, TRANSITION_CDE) but has no retry logic, no conflict resolution, no background drain |
| MOB-05 | **No state management** | HIGH | No Redux/Zustand/MobX — all state is local component state; no global cache, no optimistic updates |
| MOB-06 | **No real-time updates** | HIGH | No SignalR/WebSocket client — compliance changes, issue updates, and notifications require manual refresh |
| MOB-07 | **No PDF/document viewer** | HIGH | Documents tab lists files but cannot preview PDFs, images, or DWG thumbnails on device |
| MOB-08 | **No push notification handling** | HIGH | Server has Firebase push infrastructure but mobile app has no `expo-notifications` setup |
| MOB-09 | **No biometric/PIN auth** | MEDIUM | JWT tokens stored in AsyncStorage (insecure); no `expo-secure-store`, no biometric lock |
| MOB-10 | **No image compression** | MEDIUM | No client-side image resize before upload — site photos (12+ MP) would saturate mobile data |
| MOB-11 | **No dark mode** | LOW | Theme constants exist but no system theme detection or toggle |
| MOB-12 | **No accessibility** | LOW | No screen reader labels, no dynamic font scaling |

### 2.3 Missing API Endpoint Wrappers

The mobile `endpoints.ts` has 11 wrappers. Missing for on-site use:

| Endpoint | Purpose | Priority |
|----------|---------|----------|
| `POST /issues/{id}/attachments` | Photo upload to issues | CRITICAL |
| `GET /documents/{id}/download` | File download for offline | HIGH |
| `POST /notifications/subscribe` | Push token registration | HIGH |
| `GET /projects/{id}/compliance` | Compliance dashboard data | HIGH |
| `POST /tagsync/sync` | Element tag sync | MEDIUM |
| `GET /projects/{id}/meetings` | Meeting agenda on site | MEDIUM |
| `POST /projects/{id}/transmittals` | Create transmittal | MEDIUM |
| `GET /projects/{id}/warnings` | Warning dashboard | LOW |
| `GET /projects/{id}/workflows` | Workflow history | LOW |

---

## 3. Server API Gap Analysis

### 3.1 Current Server Architecture

**17 API Controllers** (+ AdminController = 18 total):

| Controller | Endpoints | Status |
|-----------|-----------|--------|
| AuthController | login, register, refresh, change/forgot/reset password, /me, license | **Complete** |
| ProjectsController | CRUD + settings + dashboard | **Complete** |
| ProjectMembersController | CRUD + invite | **Complete** |
| IssuesController | CRUD + SLA + attachments (upload/list/delete/link) | **Mostly complete** |
| DocumentsController | CRUD + CDE transition + upload/download | **Mostly complete** |
| TagSyncController | Bulk sync + single element + compliance push | **Functional but flawed** |
| ComplianceController | Snapshot push/pull + trend | **Complete** |
| SeqSyncController | Max-per-key merge | **Complete** |
| MeetingsController | Agenda + action items | **Complete** |
| TransmittalsController | CRUD + send | **Complete** |
| WarningsController | Reports + baseline | **Complete** |
| WorkflowsController | Run logging + trend | **Complete** |
| NotificationsController | Push subscribe/list/delete + test | **Complete** |
| SearchController | Cross-project search | **Complete** |
| PlatformController | BIM platform integrations | **Scaffold** |
| MimController | Asset lifecycle (Planscape MIM) | **Complete** |
| AdminController | Org + user + audit management | **Complete** |

### 3.2 Server Gaps for On-Site Use

#### CRITICAL Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-01 | **No image-specific handling** | File upload exists (50MB issues, 100MB docs) but no image thumbnailing, no EXIF extraction, no GPS coordinate parsing from photos | Site photos are unusable without thumbnails; GPS data in EXIF headers is discarded |
| SRV-02 | **Local filesystem storage** | `DocumentsController` saves to `{StoragePath}/{tenantSlug}/{projectCode}/` on local disk. No MinIO/S3/Azure Blob integration | Single-server deployment only; no CDN, no geographic distribution, no backup |
| SRV-03 | **No GPS coordinate fields** | No `Latitude`/`Longitude`/`Accuracy` on `BimIssue`, `DocumentRecord`, or any entity | Cannot place issues on site plans, cannot validate location-based access |
| SRV-04 | **TagSync blind overwrite** | `TagSyncController.cs:54-77`: `MapDtoToEntity(dto, existing, request.UserName)` overwrites without timestamp comparison or version tracking | Last-write-wins causes silent data loss in multi-user environments |
| SRV-05 | **No offline queue API** | No endpoint for batch replaying queued offline actions with conflict detection | Mobile offline work cannot sync reliably |

#### HIGH Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-06 | **No image thumbnailing** | No `ImageSharp`/`SkiaSharp` pipeline for generating preview thumbnails | Mobile must download full 12MP images for list views |
| SRV-07 | **No delta sync** | TagSync sends full element payloads; no `If-Modified-Since`, no change tokens, no `SyncWatermark` entity | Wastes bandwidth on site with poor connectivity |
| SRV-08 | **No batch operations endpoint** | Individual CRUD only for issues/documents; no bulk create/update/delete | Slow sync of 50+ offline-queued actions |
| SRV-09 | **No conflict resolution strategy** | No `ConflictResolution` entity, no merge policies, no user-facing conflict UI contract | Multi-device edits silently overwrite each other |
| SRV-10 | **No QR code generation** | No server-side QR code generation for element/asset tags | QR labels must be generated externally |

#### MEDIUM Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-11 | **No WebSocket auth for mobile** | SignalR hubs exist but no mobile-specific auth flow (JWT in query string for WebSocket upgrade) | Mobile cannot receive real-time updates |
| SRV-12 | **No rate limiting per device** | Rate limits are per-IP (10/min auth, 120/min API); no per-device-token limiting | Shared site WiFi could exhaust limits across all devices |
| SRV-13 | **No file versioning** | Document upload creates new records; no version chain, no diff, no rollback | Cannot track document revision history |
| SRV-14 | **No audit log for mobile actions** | `AuditLog` entity exists but no explicit mobile action tracking | Cannot distinguish office vs site actions in compliance reports |
| SRV-15 | **No geofence validation** | No project boundary polygon, no server-side location validation | Cannot enforce site-only access for sensitive documents |

#### LOW Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-16 | **No WebP/AVIF support** | Image uploads not validated for modern formats | Older Android devices may send unsupported formats |
| SRV-17 | **No bandwidth-aware responses** | No `Accept-Encoding` negotiation beyond default, no field selection (`?fields=id,title`) | Wastes bandwidth on constrained site networks |

### 3.3 Existing Server Strengths

These features are **already production-ready** and require no additional work:

- JWT authentication with 8-hour expiry + refresh tokens
- Multi-tenant isolation via `TenantResolutionMiddleware`
- Role-based access control (6 ISO 19650 roles)
- Redis-backed caching with 5-minute TTL
- Firebase push notification infrastructure
- Hangfire background jobs (compliance, SLA escalation, cleanup, platform sync)
- Swagger/OpenAPI documentation
- EF Core migrations (hand-written initial migration)
- CORS configuration for mobile origins
- Request rate limiting middleware

---

## 4. Plugin Sync Integration Gap Analysis

### 4.1 The Two Sync Systems

**This is the most critical architectural finding.** Two completely separate sync implementations exist:

#### System A: Planscape.PluginSync (DEAD CODE)

| File | Lines | Status |
|------|-------|--------|
| `SyncClient.cs` | 254 | Never instantiated |
| `OfflineQueue.cs` | 126 | Never instantiated |
| `SyncScheduler.cs` | 122 | Never instantiated |

- Designed for automatic 5-minute periodic sync
- File-backed offline queue (`%LOCALAPPDATA%/StingTools/sync_queue/`)
- Batch size: 2,000 elements per request
- Exponential backoff retry (1s, 2s, 4s)
- **Problem:** No code in `StingToolsApp.cs` or anywhere in StingTools ever creates instances of these classes

#### System B: PlanscapeServerClient (ACTUALLY USED)

| File | Lines | Status |
|------|-------|--------|
| `PlatformLinkCommands.cs` | 2,222 | Manual trigger only |

- Located at `StingTools/BIMManager/PlatformLinkCommands.cs`
- Triggered manually via `BIMManager.PlatformSyncCommand` button
- Dispatched at `StingCommandHandler.cs:1873-1876`
- Sends compliance snapshots, element data, issue sync
- No automatic scheduling, no offline queue, no retry

#### The Broken Bridge

`StingToolsApp.cs` (lines 508-557) contains an `OnDocumentSaved` handler that:
1. Runs a lightweight `ComplianceScan`
2. Queues data in `_pendingSyncDoc` / `_pendingSyncTime` static fields
3. Comments say "next SyncScheduler tick will pick this up"
4. **But `SyncScheduler` is never initialized** — data is queued into a void

### 4.2 Integration Gaps

| ID | Gap | Severity | Description |
|----|-----|----------|-------------|
| INT-01 | **PluginSync never wired** | CRITICAL | `SyncScheduler.Start()` never called in `StingToolsApp.OnStartup()`. The 5-min auto-sync documented everywhere does not exist |
| INT-02 | **Dual sync confusion** | CRITICAL | Two incompatible sync implementations create maintenance burden and developer confusion about which to use |
| INT-03 | **No conflict resolution** | CRITICAL | TagSync server endpoint (POST `/api/tagsync/sync`) does blind overwrite — no `LastModified` timestamp comparison, no version vector |
| INT-04 | **No incremental sync** | HIGH | Both systems send full element snapshots. No change tracking, no dirty flags, no sync watermark |
| INT-05 | **OnDocumentSaved data lost** | HIGH | Compliance data queued in static fields is never consumed by any sync mechanism |
| INT-06 | **No auth token management** | HIGH | `SyncClient.cs` has JWT token storage but `PlanscapeServerClient` handles auth separately with no token refresh |
| INT-07 | **No sync status UI** | MEDIUM | No visual indicator in the Revit dockable panel showing sync state (synced/pending/error/offline) |
| INT-08 | **No selective sync** | MEDIUM | Cannot sync only changed elements or specific disciplines — always full project |
| INT-09 | **Server accepts 50K elements** | MEDIUM | `TagSyncController` allows up to 50,000 elements per request but client batches at 2,000. No streaming/chunked transfer |
| INT-10 | **No mobile↔plugin coordination** | HIGH | Issues created on mobile have no mechanism to appear in the Revit plugin's `issues.json` sidecar and vice versa |

### 4.3 Feature Comparison: Server vs Plugin Support

| Feature | Server API | PluginSync (Dead) | PlanscapeServerClient | Mobile App |
|---------|-----------|-------------------|----------------------|------------|
| Element tag sync | ✅ POST /tagsync/sync | ✅ Implemented | ✅ Manual | ❌ None |
| Compliance push | ✅ POST /compliance | ✅ Implemented | ✅ Manual | ❌ None |
| Issue sync | ✅ CRUD /issues | ❌ None | ✅ Manual | ⚠️ Read-only |
| Document sync | ✅ Upload/download | ❌ None | ❌ None | ⚠️ List only |
| SEQ counter sync | ✅ POST /seq | ✅ Implemented | ❌ None | ❌ None |
| Offline queue | ❌ No batch replay | ✅ File-backed | ❌ None | ⚠️ AsyncStorage |
| Auto scheduling | N/A | ✅ 5-min timer | ❌ Manual only | ❌ None |
| Conflict resolution | ❌ Blind overwrite | ❌ None | ❌ None | ❌ None |
| Real-time push | ✅ SignalR hubs | ❌ None | ❌ None | ❌ None |
| Auth token refresh | ✅ /auth/refresh | ✅ Token store | ❌ Separate | ⚠️ Basic |

---

## 5. On-Site Readiness Assessment

### 5.1 Minimum Viable On-Site Use Cases

For Planscape to be usable on a construction site, it must support these core workflows:

#### Use Case 1: Site Issue Reporting (Snag List)
**Status: NOT READY**

| Step | Required | Current State |
|------|----------|---------------|
| 1. Open app on site | ✅ Login works | JWT auth functional |
| 2. Scan QR code on element | ❌ Scanner is stub | `Alert.alert()` only |
| 3. Take photo of issue | ❌ No camera | No `expo-image-picker` |
| 4. GPS-tag the location | ❌ No GPS | No `expo-location` |
| 5. Create issue with photo + location | ❌ No photo upload | Attachment endpoint exists but no mobile integration |
| 6. Assign to team member | ⚠️ Partial | Issue creation works but no member picker UI |
| 7. Works offline (no signal) | ❌ No offline | Queue exists but no drain mechanism |
| 8. Auto-syncs when back online | ❌ No auto-sync | No background task, no connectivity detection |

#### Use Case 2: Document Access on Site
**Status: PARTIALLY READY**

| Step | Required | Current State |
|------|----------|---------------|
| 1. Browse project documents | ✅ Works | Document list renders |
| 2. Filter by discipline/type | ⚠️ Basic | No advanced filter UI |
| 3. View PDF drawing | ❌ No viewer | No `react-native-pdf` |
| 4. View photo/image | ❌ No viewer | No image preview component |
| 5. Download for offline | ❌ No download | No file caching strategy |
| 6. Check CDE status | ⚠️ Partial | Status shown but no transition UI |

#### Use Case 3: Compliance Checking
**Status: NOT READY**

| Step | Required | Current State |
|------|----------|---------------|
| 1. View project compliance % | ❌ No dashboard | Compliance endpoint exists but no mobile UI |
| 2. View per-discipline breakdown | ❌ No UI | Data available via API |
| 3. View compliance trend | ❌ No chart | No charting library |
| 4. Receive compliance alerts | ❌ No push | Firebase infra exists but no mobile handler |

#### Use Case 4: Meeting Coordination
**Status: NOT READY**

| Step | Required | Current State |
|------|----------|---------------|
| 1. View upcoming meetings | ❌ No UI | Meetings API exists |
| 2. View agenda items | ❌ No UI | API returns agenda data |
| 3. Add action items on site | ❌ No UI | POST endpoint exists |
| 4. Mark actions complete | ❌ No UI | PUT endpoint exists |

### 5.2 Infrastructure Readiness

| Component | Ready? | Notes |
|-----------|--------|-------|
| Docker deployment | ✅ | docker-compose.yml with API + Postgres + Redis |
| HTTPS/TLS | ❌ | No SSL certificate configuration; required for site use |
| Domain/DNS | ❌ | No production domain configured |
| File storage | ❌ | Local filesystem only; needs S3/MinIO for production |
| Database backups | ❌ | No backup strategy configured |
| Monitoring/logging | ❌ | No Serilog sink, no health check dashboard |
| CI/CD pipeline | ❌ | No GitHub Actions/Azure DevOps pipeline |
| Load testing | ❌ | No performance benchmarks for concurrent site users |

---

## 6. Cost & Effort Estimates

### 6.1 Effort by Priority Phase

All estimates assume a single experienced full-stack developer familiar with React Native and ASP.NET Core.

#### Phase 1: Critical Path (4–5 weeks)
*Minimum to create/view issues with photos on site*

| Task | Effort | Description |
|------|--------|-------------|
| Wire PluginSync to StingToolsApp | 2 days | Call `SyncScheduler.Start()` in `OnStartup()`, remove `PlanscapeServerClient` duplication |
| TagSync conflict resolution | 3 days | Add `LastModified` timestamp comparison, version vectors, merge strategy |
| Mobile camera integration | 2 days | `expo-image-picker` + image compression + attachment upload |
| Mobile GPS integration | 1 day | `expo-location` + coordinate capture on issue creation |
| Server image thumbnailing | 2 days | `SixLabors.ImageSharp` pipeline for 3 thumbnail sizes |
| Server GPS fields | 1 day | Add `Latitude`/`Longitude`/`Accuracy` to `BimIssue` + `DocumentRecord` entities + migration |
| QR scanner completion | 3 days | Barcode parsing → element lookup → tag display → issue creation flow |
| Mobile offline drain | 3 days | Background task with connectivity detection, exponential retry, conflict handling |
| Mobile push notifications | 2 days | `expo-notifications` + token registration + handler routing |
| Mobile state management | 2 days | Zustand store with offline-first pattern, optimistic updates |
| **Phase 1 Total** | **~21 days** | |

#### Phase 2: High Priority (3–4 weeks)
*Full document access + real-time collaboration*

| Task | Effort | Description |
|------|--------|-------------|
| Mobile PDF viewer | 2 days | `react-native-pdf` + download manager + offline cache |
| Mobile image viewer | 1 day | Pinch-zoom, annotation overlay prep |
| Server delta sync | 3 days | `SyncWatermark` entity, `If-Modified-Since` support, change token tracking |
| Server batch operations | 2 days | Bulk create/update endpoints for issues, documents |
| Mobile compliance dashboard | 3 days | RAG bars, discipline breakdown, trend chart (`victory-native`) |
| Mobile meetings UI | 2 days | Agenda view, action item management, meeting timer |
| SignalR mobile client | 3 days | `@microsoft/signalr` + JWT auth in query string + reconnection |
| Server file versioning | 2 days | `DocumentVersion` entity + version chain + rollback |
| HTTPS/TLS setup | 1 day | Let's Encrypt + nginx reverse proxy |
| Production deployment | 2 days | Domain, DNS, MinIO/S3 integration, backup strategy |
| **Phase 2 Total** | **~21 days** | |

#### Phase 3: Medium Priority (2–3 weeks)
*Enhanced collaboration + compliance*

| Task | Effort | Description |
|------|--------|-------------|
| Mobile document annotation | 3 days | Draw-on-photo for snag markup |
| Server QR code generation | 2 days | ZXing.Net server-side QR for asset labels |
| Geofence validation | 2 days | Project boundary polygon + server-side location check |
| Mobile secure storage | 1 day | `expo-secure-store` for JWT tokens + biometric lock |
| Server audit log enhancement | 1 day | Mobile action tracking with device ID + GPS |
| Mobile dark mode | 1 day | System theme detection + toggle |
| Per-device rate limiting | 1 day | Device token in rate limit key |
| Bandwidth optimization | 2 days | Field selection, response compression, image lazy loading |
| **Phase 3 Total** | **~13 days** | |

#### Phase 4: Polish & Scale (2 weeks)
*Production hardening*

| Task | Effort | Description |
|------|--------|-------------|
| CI/CD pipeline | 2 days | GitHub Actions for server + Expo EAS Build for mobile |
| Load testing | 2 days | k6/Artillery benchmarks for 50 concurrent site users |
| Monitoring | 2 days | Serilog + Seq/Elastic, health checks, uptime alerts |
| Accessibility audit | 1 day | Screen reader labels, dynamic font scaling |
| Security audit | 2 days | OWASP mobile checklist, penetration testing |
| Documentation | 1 day | API docs update, mobile setup guide, deployment runbook |
| **Phase 4 Total** | **~10 days** | |

### 6.2 Total Cost Summary

| Phase | Weeks | Cost (£600/day) | Cumulative |
|-------|-------|-----------------|------------|
| Phase 1: Critical Path | 4–5 | £12,600 | £12,600 |
| Phase 2: High Priority | 3–4 | £12,600 | £25,200 |
| Phase 3: Medium Priority | 2–3 | £7,800 | £33,000 |
| Phase 4: Polish & Scale | 2 | £6,000 | £39,000 |
| **Total** | **11–14 weeks** | **£33,000–£39,000** | |

> **Note:** Costs assume a single developer. Parallelising Phase 1 server + mobile work across 2 developers could compress the timeline to 8–10 weeks but would increase coordination overhead.

---

## 7. Prioritised Implementation Roadmap

### Week 1–2: Foundation
```
[ ] Wire Planscape.PluginSync into StingToolsApp.OnStartup()
[ ] Add LastModified/Version fields to TaggedElement entity
[ ] Add Latitude/Longitude/Accuracy to BimIssue entity
[ ] EF Core migration for new fields
[ ] Install expo-image-picker, expo-location, expo-notifications
[ ] Set up Zustand state management with offline-first pattern
[ ] Implement mobile push notification handler
```

### Week 3–4: Core On-Site Features
```
[ ] Complete QR scanner: parse → lookup → display → create issue
[ ] Camera capture: photo → compress → attach to issue
[ ] GPS tagging: capture coordinates on issue/document creation
[ ] Server image thumbnailing pipeline (150px, 300px, 600px)
[ ] TagSync conflict resolution (timestamp + version vector)
[ ] Mobile offline queue drain with exponential retry
```

### Week 5–6: Document Access
```
[ ] PDF viewer with download + offline cache
[ ] Image viewer with pinch-zoom
[ ] Server delta sync with SyncWatermark
[ ] Server batch operations for issues/documents
[ ] HTTPS/TLS with Let's Encrypt
```

### Week 7–8: Real-Time Collaboration
```
[ ] SignalR mobile client with JWT auth
[ ] Compliance dashboard with RAG bars and charts
[ ] Meetings UI with agenda and action items
[ ] Server file versioning
[ ] Production deployment with MinIO/S3
```

### Week 9–10: Enhanced Features
```
[ ] Document annotation (draw on photo)
[ ] Server QR code generation for asset labels
[ ] Geofence validation
[ ] Secure token storage + biometric auth
[ ] Per-device rate limiting
```

### Week 11–12: Production Hardening
```
[ ] CI/CD pipeline (GitHub Actions + Expo EAS)
[ ] Load testing for 50 concurrent users
[ ] Monitoring (Serilog + health checks)
[ ] Security audit (OWASP mobile checklist)
[ ] Documentation and deployment runbook
```

---

## 8. Architecture Recommendations

### 8.1 Resolve the Dual Sync System

**Recommendation:** Retire `PlanscapeServerClient` and wire `Planscape.PluginSync` as the canonical sync path.

```
StingToolsApp.OnStartup()
  └── SyncScheduler.Start(apiUrl, authToken)
        └── Every 5 min:
              ├── Drain OfflineQueue (file-backed)
              ├── SyncClient.PushElements(batch)  // 2K per request
              ├── SyncClient.PushCompliance(snapshot)
              └── SyncClient.PullIssues() → merge into issues.json
```

Changes required:
1. `StingToolsApp.cs`: Add `SyncScheduler.Start()` after `OnStartup()` ribbon build
2. `StingToolsApp.cs`: Remove `_pendingSyncDoc`/`_pendingSyncTime` static fields; use `OfflineQueue.Enqueue()` instead
3. `PlatformLinkCommands.cs`: Redirect `PlatformSyncCommand` to call `SyncScheduler.SyncNow()` instead of `PlanscapeServerClient`
4. `StingCommandHandler.cs`: Update dispatch at line 1873–1876

### 8.2 Implement Optimistic Conflict Resolution

For the TagSync blind-overwrite problem:

```csharp
// TagSyncController.cs — replace blind overwrite
if (existing != null)
{
    if (dto.LastModifiedUtc <= existing.LastModifiedUtc)
    {
        conflicts.Add(new SyncConflict
        {
            ElementId = dto.RevitElementId,
            ServerValue = existing,
            ClientValue = dto,
            Resolution = "SERVER_WINS" // or CLIENT_WINS based on policy
        });
        continue;
    }
}
```

### 8.3 Mobile Offline-First Architecture

```
┌─────────────────────────────────────────┐
│              Zustand Store              │
│  ┌──────────┐  ┌──────────┐  ┌───────┐ │
│  │ Issues   │  │ Documents│  │ Comp. │ │
│  │ (cached) │  │ (cached) │  │ Data  │ │
│  └────┬─────┘  └────┬─────┘  └───┬───┘ │
│       │              │            │     │
│  ┌────▼──────────────▼────────────▼───┐ │
│  │         Offline Queue              │ │
│  │  AsyncStorage FIFO + retry count   │ │
│  └────────────────┬───────────────────┘ │
└───────────────────┼─────────────────────┘
                    │
          ┌─────────▼─────────┐
          │  Sync Manager     │
          │  - Connectivity   │
          │  - Batch replay   │
          │  - Conflict merge │
          └─────────┬─────────┘
                    │
          ┌─────────▼─────────┐
          │  Planscape API    │
          │  (HTTPS + SignalR)│
          └───────────────────┘
```

### 8.4 Server Storage Migration Path

```
Phase 1: Local filesystem (current — development only)
Phase 2: MinIO (self-hosted S3-compatible — staging/small teams)
Phase 3: Azure Blob Storage or AWS S3 (production — scalable)
```

Implementation: Add `IFileStorageService` interface with `LocalFileStorage`, `MinioFileStorage`, `AzureBlobStorage` implementations. Inject via DI based on environment configuration.

### 8.5 Database Schema Additions Required

```sql
-- Add GPS coordinates to issues
ALTER TABLE "BimIssues" ADD COLUMN "Latitude" double precision;
ALTER TABLE "BimIssues" ADD COLUMN "Longitude" double precision;
ALTER TABLE "BimIssues" ADD COLUMN "LocationAccuracy" double precision;
ALTER TABLE "BimIssues" ADD COLUMN "DeviceId" text;

-- Add GPS to documents
ALTER TABLE "DocumentRecords" ADD COLUMN "Latitude" double precision;
ALTER TABLE "DocumentRecords" ADD COLUMN "Longitude" double precision;

-- Add version tracking to elements
ALTER TABLE "TaggedElements" ADD COLUMN "Version" integer NOT NULL DEFAULT 1;
ALTER TABLE "TaggedElements" ADD COLUMN "LastModifiedUtc" timestamp with time zone;

-- Sync watermark for delta sync
CREATE TABLE "SyncWatermarks" (
    "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "TenantId" integer NOT NULL REFERENCES "Tenants"("Id"),
    "ProjectId" integer NOT NULL REFERENCES "Projects"("Id"),
    "DeviceId" text NOT NULL,
    "LastSyncUtc" timestamp with time zone NOT NULL,
    "ElementCount" integer NOT NULL DEFAULT 0,
    UNIQUE ("ProjectId", "DeviceId")
);

-- Document versions
CREATE TABLE "DocumentVersions" (
    "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "DocumentRecordId" integer NOT NULL REFERENCES "DocumentRecords"("Id"),
    "VersionNumber" integer NOT NULL,
    "FilePath" text NOT NULL,
    "FileHash" text,
    "FileSize" bigint,
    "UploadedBy" text,
    "UploadedAt" timestamp with time zone NOT NULL,
    "ChangeDescription" text
);

-- Conflict log for audit
CREATE TABLE "SyncConflicts" (
    "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "TenantId" integer NOT NULL,
    "ProjectId" integer NOT NULL,
    "ElementId" text NOT NULL,
    "ConflictType" text NOT NULL,
    "ServerValue" jsonb,
    "ClientValue" jsonb,
    "Resolution" text NOT NULL,
    "ResolvedAt" timestamp with time zone NOT NULL,
    "ResolvedBy" text
);
```

---

## Appendix A: File Reference

| File | Location | Lines | Role |
|------|----------|-------|------|
| SyncClient.cs | Planscape.Server/src/Planscape.PluginSync/ | 254 | HTTP sync client (DEAD CODE) |
| OfflineQueue.cs | Planscape.Server/src/Planscape.PluginSync/ | 126 | File-backed queue (DEAD CODE) |
| SyncScheduler.cs | Planscape.Server/src/Planscape.PluginSync/ | 122 | 5-min timer (DEAD CODE) |
| PlatformLinkCommands.cs | StingTools/BIMManager/ | 2,222 | Contains PlanscapeServerClient (ACTIVE) |
| StingCommandHandler.cs | StingTools/UI/ | 4,826+ | Dispatch at lines 1873-1876 |
| StingToolsApp.cs | StingTools/Core/ | 418+ | OnDocumentSaved at lines 508-557 |
| TagSyncController.cs | Planscape.Server/src/Planscape.API/Controllers/ | ~200 | Blind overwrite at lines 54-77 |
| endpoints.ts | Planscape/src/api/ | 100 | 11 API wrappers |
| offlineQueue.ts | Planscape/src/utils/ | 124 | AsyncStorage FIFO queue |
| scanner.tsx | Planscape/app/(tabs)/ | 828 | QR scanner (STUB) |
| client.ts | Planscape/src/api/ | 97 | Axios HTTP client |

## Appendix B: Glossary

| Term | Definition |
|------|-----------|
| **CDE** | Common Data Environment — ISO 19650 document management with WIP/SHARED/PUBLISHED/ARCHIVE states |
| **ISO 19650** | International standard for BIM information management |
| **Tag** | 8-segment ISO 19650 asset identifier: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ |
| **COBie** | Construction Operations Building Information Exchange — asset data format for FM handover |
| **RAG** | Red/Amber/Green compliance status indicator |
| **SLA** | Service Level Agreement — response time targets per issue priority |
| **BEP** | BIM Execution Plan — project-specific BIM methodology document |
| **DD1–DD4** | Data drops 1–4 — ISO 19650 information exchange milestones |
| **SignalR** | Microsoft real-time communication library (WebSocket/SSE/Long Polling) |
| **MinIO** | Open-source S3-compatible object storage |
