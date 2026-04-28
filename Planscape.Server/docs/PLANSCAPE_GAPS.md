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

> **2026-04-27 update (Phase 141):** the dead-code claim below is superseded.
> `Planscape.PluginSync.SyncScheduler` is now wired in two places:
>   1. `StingTools/UI/StingDockPanel.xaml.cs` (`SyncIndicator_Click` /
>      `RefreshSyncIndicator`) — the sync chip in the dock panel header
>      reads `SyncScheduler.Instance.Status` and calls `SyncNow()` on click.
>   2. `StingTools/BIMManager/PlatformLinkCommands.cs` (`PlatformSyncCommand`)
>      — lazy-starts the scheduler on the first manual sync if it wasn't
>      already running, and uses `SyncScheduler.SyncNow(payload)` as the
>      primary sync entry point.
>
> The 5-minute periodic sync DOES function once the user has logged in to
> Planscape via the dock panel. The remaining true gap is that
> `PlanscapeServerClient` and `Planscape.PluginSync.SyncClient` are still
> two parallel HTTP layers — INT-01 in `docs/ROADMAP.md` tracks the
> consolidation work but the "delete PluginSync" recommendation is closed.

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

> **Phase 141 closures (2026-04-27):** the table below is heavily out of date. The
> mobile app shipped most of these items in earlier phases; the audit only found one
> genuine gap (MOB-08 — push subscribe never POSTed to the server). That is now closed.
> Status column added to track active state.

| ID | Gap | Severity | Description | Status |
|----|-----|----------|-------------|--------|
| MOB-01 | **QR Scanner non-functional** | CRITICAL | `scanner.tsx` (828 lines) imports `expo-camera` v16 but scanning result triggers `Alert.alert()` only — no API call, no element lookup, no tag resolution | **Closed** — `useQrScan` hook + element lookup + issue pre-fill via `router.push` |
| MOB-02 | **No camera/photo capture** | CRITICAL | No image picker, no camera integration, no photo attachment to issues or documents | **Closed** — `expo-image-picker` + `expo-image-manipulator` + multipart upload to `/issues/{id}/attachments` |
| MOB-03 | **No GPS/location services** | CRITICAL | No `expo-location` dependency, no coordinate capture, no geofencing for site boundary | **Closed** — `expo-location` integrated; coordinates POSTed in issue create body |
| MOB-04 | **No offline sync engine** | CRITICAL | `offlineQueue.ts` supports 3 action types (CREATE_ISSUE, UPDATE_ISSUE, TRANSITION_CDE) but has no retry logic, no conflict resolution, no background drain | **Closed** — NetInfo listener auto-drains; SUBSCRIBE_PUSH added to action enum |
| MOB-05 | **No state management** | HIGH | No Redux/Zustand/MobX — all state is local component state; no global cache, no optimistic updates | **Closed** — `src/stores/` (authStore, issueStore, projectStore, tenantStore, notificationStore) |
| MOB-06 | **No real-time updates** | HIGH | No SignalR/WebSocket client — compliance changes, issue updates, and notifications require manual refresh | **Closed** — `realtimeClient.ts` with `@microsoft/signalr` |
| MOB-07 | **No PDF/document viewer** | HIGH | Documents tab lists files but cannot preview PDFs, images, or DWG thumbnails on device | **Closed** — `react-native-pdf` + `react-native-webview` |
| MOB-08 | **Push token never registered with server** | HIGH | `expo-notifications` retrieves the Expo push token but `notificationService.subscribe()` was never called from the auth flow — `DevicePushToken` table stayed empty | **Closed** — Phase 141: `useAuth.login` / `useAuth.restoreSession` / `_layout.checkAuth` all call `notificationService.register()` |
| MOB-09 | **No biometric/PIN auth** | MEDIUM | JWT tokens stored in AsyncStorage (insecure); no `expo-secure-store`, no biometric lock | **Closed** — `expo-secure-store` + `expo-local-authentication` + `biometricLock.ts` |
| MOB-10 | **No image compression** | MEDIUM | No client-side image resize before upload — site photos (12+ MP) would saturate mobile data | **Closed** — `imageService.ts` resizes to 1920px / JPEG 75% before upload |
| MOB-11 | **No dark mode** | LOW | Theme constants exist but no system theme detection or toggle | Open |
| MOB-12 | **No accessibility** | LOW | No screen reader labels, no dynamic font scaling | Partial — `accessibilityLabel` props added in issue detail / scanner / settings; full audit pending |
| MOB-13 | **No "My Actions" inbox** | HIGH | Issues, meeting actions, and document approvals each required a separate screen to find what was assigned to the caller | **Closed** — Phase 142: `MyActionsController` aggregator + `app/inbox` screen + dashboard CTA card |
| MOB-14 | **No daily site diary** | HIGH | Standard CIOB end-of-day report (weather, manpower, narrative, safety, delays) was completely absent | **Closed** — Phase 142: `SiteDiary` entity + migration + controller + 3 mobile screens (list / new / detail) with submit + acknowledge lifecycle |
| MOB-15 | **No GPS map deep-link** | MEDIUM | Coordinates captured at issue creation (Phase 141) were never surfaced or linkable | **Closed** — Phase 142: GPS strip on issue-detail with one-tap to native map app (`geo:` / `maps://` / Google Maps web fallback) |
| MOB-16 | **Issues list lacks "Mine" filter** | MEDIUM | Managers had to scan the whole list or fall back to search to find their own assignments | **Closed** — Phase 142: Mine toggle chip; FK / email / display-name match for legacy rows |
| MOB-17 | **Quick-action access to sub-routes** | LOW | Meetings / Transmittals / Warnings / Site Diary were only reachable via deep-link from a push tap | **Closed** — Phase 142: 4-button quick-action row on the dashboard |
| MOB-18 | **No sync-conflict triage UI** | HIGH | `TagSyncController` was writing `SyncConflict` rows that no UI ever surfaced; managers had to query the database directly | **Closed** — Phase 143: `SyncConflictsController` + mobile `app/conflicts` with per-row + bulk resolve |
| MOB-19 | **No federation freshness view** | MEDIUM | Per-discipline latest model + days-since-upload were not aggregated; manager had no "is everyone up to date?" dashboard | **Closed** — Phase 143: `GET /federation-status` aggregator + dashboard tile with RAG status |
| MOB-20 | **Naming convention not enforced server-side** | HIGH | `BIMManagerEngine.ValidateDocumentName` existed in the plugin but the server accepted any file name on upload | **Closed** — Phase 143: `Iso19650NamingValidator` + per-project `EnforceIso19650Naming` toggle + dry-run validate-name endpoint |
| MOB-21 | **No project admin settings UI** | MEDIUM | `EnforceIso19650Naming` had no UI surface, so flipping it required a SQL update or a raw PUT call | **Closed** — Phase 144: `app/project-settings` screen wired off the Settings tab; PUT routes admin booleans to first-class columns, soft prefs to ConfigJson |
| MOB-22 | **Bulk ACCEPT_CLIENT for sync conflicts unsupported** | LOW | Bulk-resolve only handled ACCEPT_SERVER / MERGED; the manager could only re-apply a client edit one conflict at a time | **Closed** — Phase 144: `bulk-resolve-with-fields` endpoint takes per-conflict field maps (cap 250); idempotent against already-resolved rows |
| MOB-23 | **No tag completeness heatmap** | MEDIUM | `byDiscipline` total in the dashboard hid which token (DISC/LOC/SYS/...) was actually slipping per discipline | **Closed** — Phase 144: `GET /compliance/tag-heatmap` endpoint returns the full discipline × 10-token matrix; `app/heatmap` renders a 4-tier RAG grid |
| MOB-24 | **No stage gate / MIDP tracking** | HIGH | The BIM Manager had nowhere to record RIBA stage gates or the information-exchange deliverables due against them; everything lived in spreadsheets | **Closed** — Phase 144: `StageGate` + `InformationDeliverable` entities + migration + two controllers + mobile timeline + per-deliverable state machine; RIBA 0–7 seed available for one-click setup |
| MOB-25 | **Heatmap aggregated app-tier** | LOW | `byDiscipline × token` percent grid pulled slim DTO rows into the API process and `GroupBy`'d there; fine to ~50k elements but a 200k federated model would stream ~4 MB per call | **Closed** — Phase 145: switched to PG `count(*) FILTER (WHERE …)` raw SQL grouped by `COALESCE(NULLIF(BTRIM(Disc), ''), '(unset)')` so response time stays flat at scale |
| MOB-26 | **Bulk ACCEPT_CLIENT capped at 250** | LOW | Per-row writes made the larger payload timeout-prone on poor site connections, so the cap was half what ACCEPT_SERVER allowed | **Closed** — Phase 145: cap raised to 500; work runs in 50-row batches with one `SaveChangesAsync` each, failed batch is rolled back and reported in `failedBatches[]` so caller can replay the unresolved tail |
| MOB-27 | **Stage gate criteria were free-form JSON only** | MEDIUM | `CriteriaJson` was a JSONB blob with no API surface for criterion-by-criterion sign-off; mobile couldn't render a checklist | **Closed** — Phase 145: structured `CriterionDto` + 3 new endpoints (list / replace / signoff) on `StageGatesController` + `app/stages/criteria` checklist screen with seed-defaults button |
| MOB-28 | **Deliverable state machine hard-coded** | MEDIUM | Tenants outside the canonical ISO 19650 6-state flow had no way to override `IsValidTransition` short of a code change | **Closed** — Phase 145: `Project.CustomDeliverableStateMachineJson` (jsonb) + `DeliverableStateMachine.LoadOrDefault` parser + `GET /deliverables/state-machine` endpoint; mobile renders contextual buttons from the resolved machine |
| MOB-29 | **Heatmap was PG-only** | LOW | Raw-SQL heatmap aggregator used `BTRIM` + `FILTER`; SQLite test suite, SQL Server, in-memory provider all 500'd | **Closed** — Phase 146: provider-aware dispatch on `_db.Database.ProviderName`; non-PG path falls back to LINQ aggregator with identical response shape |
| MOB-30 | **Custom state machines lost side-effects** | MEDIUM | Side-effect logic (SubmittedAt/AcceptedAt/RejectionReason stamps) keyed off literal canonical state names, so a custom flow renaming SUBMITTED → UNDER_REVIEW silently lost the metadata writes | **Closed** — Phase 146: `SemanticRoles` map on `DeliverableStateMachine` + per-state `roles` block in custom JSON; controller switches on `RoleOf(target)` so bespoke names inherit the canonical side-effects |
| MOB-31 | **Criterion sign-off rewrote whole array** | LOW | Per-key signoff serialised the full criteria list back to the JSONB column on every call; sub-optimal for >200-item checklists | **Closed** — Phase 146: normalised `StageGateCriterion` table + migration `20260427400000_AddStageGateCriteria`; per-key signoff writes a single row in O(1); legacy JSONB column kept for read-fallback |
| MOB-32 | **RIBA seed templates only covered stages 3–7** | LOW | Stages 0–2 had no built-in checklist; mobile "Seed defaults" button was a no-op for early-stage projects | **Closed** — Phase 146: added templates for RIBA-0 (Strategic Definition), RIBA-1 (Preparation and Briefing), RIBA-2 (Concept Design); coverage now 0–7 |
| SRV-33 | **Stage gate criteria dual-written to JSONB blob + normalised table** | LOW | Phase 146 introduced the normalised table but the controller still wrote to `StageGate.CriteriaJson` for read-fallback compat | **Closed** — Phase 147: backfill migration `20260427500000_BackfillStageGateCriteria` copies any remaining JSONB criteria into the table; PUT is now a single-write to the table; legacy column kept for read-only deprecation window |
| SRV-34 | **Custom state machine without roles block lost side-effects** | MEDIUM | Phase 146 required tenants to author a `"roles"` block to get metadata stamps; tenants that just reordered transitions on canonical state names silently lost SubmittedAt / AcceptedAt | **Closed** — Phase 147: `CanonicalRoles` lookup infers roles from state names (incl. synonyms like `DRAFT`, `APPROVED`, `PUBLISHED`) when the JSON omits a `"roles"` block; explicit roles still win |
| SRV-35 | **No automated coverage for state machine + naming validator** | LOW | New code paths in Phases 143 / 145 / 146 had no unit tests; regressions would only surface in production use | **Closed** — Phase 147: 27 xUnit facts/theories across `DeliverableStateMachineTests` (16) and `Iso19650NamingValidatorTests` (11) wired into the existing `Planscape.Tests` project |
| SRV-36 | **Backfill migration depended on pgcrypto preinstalled** | LOW | `gen_random_uuid()` requires pgcrypto on PG 12. Every Planscape deployment already had it, but the migration silently relied on the side-effect of an earlier migration | **Closed** — Phase 148: `CREATE EXTENSION IF NOT EXISTS pgcrypto;` at the head of the backfill migration so a fresh PG 12 cold-start no longer depends on a previous migration's side-effect |
| SRV-37 | **Bespoke state vocabularies got no role inference** | LOW | Phase 147's `CanonicalRoles` lookup only matched exact names + a small synonym list; truly bespoke names (e.g. `ARCH_HAS_REVIEWED`, `ME_FINAL_APPROVAL`) needed an explicit `"roles"` block to get metadata side-effects | **Closed** — Phase 148: substring-keyword fallback in `InferRoleByKeyword` with 6 priority-ordered vocabularies (rejecting > accepting > submitting > terminal > working > initial). Bespoke names now infer sensible roles; explicit `"roles"` blocks still win |
| SRV-38 | **Phase 148 vocabulary missed common JCT/NEC state words** | LOW | `ON_HOLD`, `LOCKED`, `BLOCKED`, `FROZEN`, `ESCALATED`, `WITHDRAWN` etc. weren't in any of the built-in keyword buckets, so projects using them still needed an explicit `"roles"` block | **Closed** — Phase 149: extended working/submitting/terminal vocabularies with the missing words; added an optional `"keywords"` JSON extension block so tenants can override or extend the built-in vocabulary per-project (e.g. `LOCKED → working` for a project where LOCKED means "engineer is editing") |
| SRV-39 | **`RoleOf` for unknown states wasn't memoised** | LOW | A future caller hitting `RoleOf` with the same unknown state in a tight loop would pay the substring scan each time | **Closed** — Phase 149: per-instance `ConcurrentDictionary` cache amortises repeated lookups to O(1); pre-computed states still hit the precomputed table on first lookup |
| SRV-40 | **Runtime memoisation cache was unbounded** | LOW | The Phase 149 `ConcurrentDictionary` had no cap; a long-lived singleton machine querying many unique state names could grow without bound | **Closed** — Phase 150: replaced with `BoundedLruCache<string,string>` capped at 256 entries with LRU eviction; ~20 KB worst case per instance |
| SRV-41 | **Tenant keyword extensions were project-scoped only** | LOW | Operators couldn't set platform-wide keyword vocabularies that apply to every tenant; common state words had to be repeated in every project's JSON | **Closed** — Phase 150: `IPlatformKeywordRegistry` reads from `DeliverableStateMachine:Keywords` in appsettings; `LoadOrDefault` overload merges platform layer below project layer with project-wins semantics |
| SRV-42 | **No tenant-scoped middle layer** | LOW | Phase 150 had only project + platform layers; a multi-tenant deployment couldn't set tenant-wide keyword extensions without writing them per-project | **Closed** — Phase 151: `Tenant.KeywordExtensionsJson` (jsonb) + `ITenantKeywordResolver` with read-through static striped-LRU cache + `MergeKeywordLayers` n-ary helper. Net priority: project > tenant > platform > built-ins. Admin endpoints `GET/PUT /api/admin/tenant-keywords` to manage per-tenant config |
| SRV-43 | **LRU runtime cache used a single coarse lock** | LOW | High-throughput concurrent `RoleOf` callers on the same machine instance would serialise on the cache's single lock | **Closed** — Phase 151: new `StripedBoundedLruCache` with N power-of-two stripes (default 8 × 32 = 256 total). Concurrent gets to different keys don't contend. Phase 150 LRU semantics preserved per stripe |
| SRV-44 | **Tenant resolver cache was process-local only** | LOW | The Phase 151 static striped LRU survived across requests but not process restarts; horizontal-scaled API instances each paid the parse cost cold | **Closed** — Phase 152: optional `IDistributedCache` (Redis-backed in production) L2 cache layered above the existing L1. Read-through on L1 miss, write-through on DB hit, hash-keyed so admin edits self-invalidate. L2 errors degrade gracefully to L1 + DB |
| SRV-45 | **Admin endpoints had only role-level auth** | LOW | Tenant-keywords endpoints required `Admin` or `Owner`; a BIM Manager (ISO 19650 role K) couldn't curate vocabulary without being promoted | **Closed** — Phase 152: new `BimManagerOrAdmin` authorisation policy (Admin/Owner short-circuits; otherwise DB lookup for `ProjectMember.Iso19650Role == "K"`); endpoints moved into `TenantKeywordsController` so the policy is the only auth gate |
| SRV-46 | **No dashboard UI for tenant keywords** | LOW | The Phase 151 admin endpoints only had CLI / curl access; no operator-friendly editor | **Closed** — Phase 152: new "Tenant keywords" sidebar entry on the office dashboard with formatted-JSON textarea editor, Save / Clear / Reset buttons, server-side validation surfaced inline |
| SRV-47 | **L2 TTL was fixed at 14 days, no sliding refresh** | LOW | Active tenants paid the parse cost every fortnight; long-idle tenants squatted on Redis memory until absolute expiry | **Closed** — Phase 153: configurable absolute + sliding TTL (`DeliverableStateMachine:Cache:AbsoluteTtlDays` / `SlidingTtlDays`); active tenants stay hot indefinitely, idle ones still bounded by the absolute cap |
| SRV-48 | **Dashboard editor accepted free-form JSON** | LOW | Inline feedback was server-round-trip-only; users discovered typos by hitting Save | **Closed** — Phase 153: `validateTenantKeywordsJson` JS validator mirrors the server's `ParseForValidation` rules and runs on every keystroke; Save button is disabled on hard errors with a specific in-line message |
| SRV-49 | **BIM Manager grant was hardcoded to ISO role "K"** | LOW | Tenants whose `ProjectMember.Iso19650Role` rows weren't populated needed Admin/Owner promotion to curate vocabulary | **Closed** — Phase 153: configurable `Authorization:BimManagerIso19650Roles` list (default `["K"]`); new AppUser-level grant path checks `AppUser.Iso19650Role` so a BIM Manager flagged at onboarding gets access on day one without per-project membership |
| SRV-50 | **Dashboard JS validator was a manual mirror of server rules** | LOW | The role-bucket list lived in three C# files + the JS validator; if a 7th bucket ever landed, the JS would silently lag | **Closed** — Phase 154: new `RoleBuckets` static class is the single source of truth (replaces 3 duplicates); new `GET /api/state-machine/role-buckets` endpoint exposes the list to clients; dashboard JS fetches once per session with a hardcoded fallback for transient failures |
| SRV-51 | **Configurable BIM Manager roles were deployment-global** | LOW | A multi-tenant deployment couldn't grant tenant-coordinator (C) keyword-edit rights on one tenant without affecting all others | **Closed** — Phase 154: `Tenant.BimManagerIso19650RolesJson` (jsonb) override layer; handler reads tenant override → falls back to deployment appsettings → falls back to hardcoded `["K"]`. Forgiving parser: malformed override falls back gracefully so a bad row can't lock tenant out of admin |
| SRV-52 | **Tenant override JSON parsed on every authorisation request** | LOW | Phase 154 handler did a fresh DB read + JSON parse for every request that hit the policy | **Closed** — Phase 155: cached `ITenantBimManagerRoleResolver` (static striped LRU keyed on `(TenantId, hash)`); handler is one O(1) cache lookup per request once warm |
| SRV-53 | **No admin UI for tenant BIM-Manager role override** | LOW | The Phase 154 column was set via SQL only | **Closed** — Phase 155: new `GET / PUT /api/admin/tenant-bim-manager-roles` + dashboard "BIM-Manager roles" sidebar entry with textarea editor, inline keystroke validation, Save / Clear / Reset |
| SRV-54 | **Role-block vs keyword-block asymmetry undocumented in API** | LOW | The dashboard JS validator had to know that "none" was legal in `"roles"` blocks but not in `"keywords"` blocks via comments alone | **Closed** — Phase 155: `GET /api/state-machine/role-buckets` now returns `keywordBlockBuckets` (six canonical) + `rolesBlockKeys` (six + "none") so the JS validator can pick the right list per context. Legacy `buckets` / `priorityOrder` aliases preserved |
| SRV-55 | **Tenant role override cache was process-local only** | LOW | Phase 155 cache survived across requests but not process restarts; horizontal-scaled API instances each paid the parse cost cold | **Closed** — Phase 156: optional `IDistributedCache` L2 (Redis-backed in production) layered above the existing L1, sliding + absolute TTLs configurable. Read-through on L1 miss, write-through on DB hit, hash-keyed so admin edits self-invalidate. Mirrors Phase 152's keyword resolver |
| SRV-56 | **JWT auth had no revocation lag mitigation** | MEDIUM | A user demoted from BIM Manager retained policy-gated access until token expiry; standard JWT pattern with no built-in revocation | **Closed** — Phase 156: `IPermissionRevocationStore` records a per-user "minimum acceptable iat" floor in Redis; auth handler compares the JWT's `iat` claim and denies stale tokens immediately. `AdminController.UpdateUser` bumps the floor on permission-changing edits (Role / Iso19650Role / IsActive). Falls back to legacy lag if Redis is unavailable |
| SRV-57 | **role-buckets endpoint had no ETag negotiation** | LOW | The static payload was returned in full on every call; clients couldn't avoid revalidating at the application layer | **Closed** — Phase 156: strong ETag computed from SHA-256 of the canonical payload; endpoint sets `Cache-Control: private, max-age=3600` and answers matching `If-None-Match` with body-less 304. Dashboard browsers + mobile HTTP caches honour the tag transparently |
| SRV-58 | **Auth handler did revocation + tenant-override reads serially** | LOW | Phase 156 added the revocation lookup as an extra Redis GET on the policy-gated path; combined with the existing tenant-override lookup the worst-case auth latency was the sum of both | **Closed** — Phase 157: both lookups launch concurrently via `Task.WhenAll` since they target distinct keys; auth path latency drops to roughly the slower leg. Each consumer keeps its own L1/L2/DB fallback chain |
| SRV-59 | **No discrete "revoke session" admin action** | LOW | Token revocation only happened as a side-effect of `UpdateUser` permission edits; SOC2 / ISO 27001 audits expected a distinct "session termination" event | **Closed** — Phase 157: new `POST /api/admin/users/{userId}/revoke-tokens` bumps the iat floor + audit-logs under `USER_REVOKE`. Idempotent; returns user email + revocation timestamp on success |
| SRV-60 | **Concurrency test was timing-dependent** | LOW | Phase 157 test asserted `<350ms` wall-clock to detect serial dispatch; CI under load could exceed it | **Closed** — Phase 158: barrier-based test using paired `TaskCompletionSource` gates — serial dispatch deadlocks (caught by 2-second timeout), concurrent dispatch completes. Zero timing dependency |
| SRV-61 | **Revoke-tokens audit reason was hardcoded** | LOW | All revokes logged with the same "explicit admin revoke" string; SOC2 trails couldn't capture *why* (suspected leak, offboarding, etc.) | **Closed** — Phase 158: endpoint accepts optional `{ reason, category }` body; audit-log captures both, with category defaulting to `unspecified`. Reason capped at 500 chars server-side |
| SRV-62 | **No SecurityOfficer separation-of-duties role** | MEDIUM | Revoke endpoint required Admin/Owner; SOC2 audits often want a dedicated security persona that can terminate sessions without holding tenant-admin powers | **Closed** — Phase 158: new `UserRole.SecurityOfficer` + `SecurityOfficerOrAdmin` policy; revoke moved to `SecurityController` under the new policy. Backwards-compatible (existing operators see no change because Admin/Owner still grants) |
| SRV-63 | **No recommended SOC2 audit-category list** | LOW | Phase 158 left the revoke-tokens `category` field as free-form text; dashboards / mobile clients had no canonical taxonomy to render in a dropdown, so each operator could invent their own classification and SOC2 reviews couldn't filter by category | **Closed** — Phase 159: new `GET /api/audit/categories` returns built-in SOC2 categories merged with operator-configured `Audit:Categories` from appsettings (case-insensitive dedupe, configured entries surface ahead of built-ins). ETag/304 negotiation, `[Authorize]` only (any user can read). The category field stays free-form server-side; the endpoint is advisory only |
| SRV-64 | **Phase 157 revoke route hard-cut, no deprecation window** | LOW | Phase 158 removed the original `/api/admin/users/{id}/revoke-tokens` route the same window it was added in (Phase 157). Safe at the time because nothing depended on it, but precluded any client that did integrate during the brief window from following its own deprecation cadence | **Closed** — Phase 159: backward-compat alias on `SecurityController.RevokeTokens` adds `[HttpPost("/api/admin/users/{userId}/revoke-tokens")]` (leading slash ⇒ absolute path overrides the class-level `[Route("api/security")]` prefix). Same handler, same `SecurityOfficerOrAdmin` policy, same audit `via` marker. Old clients keep working through the deprecation window |

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

> **Phase 141 closures (2026-04-27):** SRV-01 (EXIF write-through), SRV-03 (GPS columns
> on `BimIssue`), SRV-04 (delta sync via `LastModifiedPlugin`/`LastModifiedServer`),
> SRV-07 (project-scoped notifications via `NotifyProjectAsync`),
> SRV-11 (X-Client-Type audit Source classification + filterable audit endpoint),
> and SRV-08 partial (HSTS + UseHttpsRedirection in `Program.cs`).
> SRV-02 (MinIO) is implemented via `IFileStorageService` with both
> `S3FileStorageService` and `LocalFileStorageService` configured by appsettings.

#### CRITICAL Priority

| ID | Gap | Description | Impact | Status |
|----|-----|-------------|--------|--------|
| SRV-01 | **No image-specific handling** | File upload exists (50MB issues, 100MB docs) but no image thumbnailing, no EXIF extraction, no GPS coordinate parsing from photos | Site photos are unusable without thumbnails; GPS data in EXIF headers is discarded | **Closed** — `ImageSharpThumbnailService` generates 150/300/600 px thumbnails; EXIF GPS extracted and now written through to `BimIssue` Lat/Lng (Phase 141) |
| SRV-02 | **Local filesystem storage** | `DocumentsController` saves to `{StoragePath}/{tenantSlug}/{projectCode}/` on local disk. No MinIO/S3/Azure Blob integration | Single-server deployment only; no CDN, no geographic distribution, no backup | **Closed** — `IFileStorageService` with `S3FileStorageService` (MinIO-compatible) and `LocalFileStorageService` selected by `Storage:Provider` config |
| SRV-03 | **No GPS coordinate fields** | No `Latitude`/`Longitude`/`Accuracy` on `BimIssue`, `DocumentRecord`, or any entity | Cannot place issues on site plans, cannot validate location-based access | **Closed** — migration `20250417000000_AddIssueGpsAndAssigneeFk` |
| SRV-04 | **TagSync blind overwrite** | `TagSyncController.cs:54-77`: `MapDtoToEntity(dto, existing, request.UserName)` overwrites without timestamp comparison or version tracking | Last-write-wins causes silent data loss in multi-user environments | **Closed** — `LastModifiedPlugin`/`LastModifiedServer` columns + `SyncConflict` table |
| SRV-05 | **No offline queue API** | No endpoint for batch replaying queued offline actions with conflict detection | Mobile offline work cannot sync reliably | Open — requires server-side batch endpoint design |

#### HIGH Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-06 | **No image thumbnailing** | No `ImageSharp`/`SkiaSharp` pipeline for generating preview thumbnails | Mobile must download full 12MP images for list views | **Closed** — `ImageSharpThumbnailService` |
| SRV-07 | **Notifications fan out tenant-wide** | `NotifyAsync(tenantId, …)` reaches members of unrelated projects in the same tenant | Issue + SLA pushes leak across projects | **Closed** — Phase 141: `NotifyProjectAsync` filters SignalR + push by `ProjectMembers` |
| SRV-08 | **No HTTPS / HSTS enforcement** | App listens on cleartext, no redirect, no HSTS | Hostile networks can downgrade and intercept | **Partial close** — Phase 141: `UseHttpsRedirection` + `UseHsts` (1 yr, IncludeSubDomains). Reverse-proxy TLS termination + cert lifecycle still on the deployer |
| SRV-09 | **No conflict resolution strategy** | No `ConflictResolution` entity, no merge policies, no user-facing conflict UI contract | Multi-device edits silently overwrite each other | **Closed** for tags — `SyncConflict` table now tracks them |
| SRV-10 | **No QR code generation** | No server-side QR code generation for element/asset tags | QR labels must be generated externally | Open |

#### MEDIUM Priority

| ID | Gap | Description | Impact |
|----|-----|-------------|--------|
| SRV-11 | **No audit log Source classification** | `AuditLog` entity has a `Source` column but it always defaults to `"desktop"` — no User-Agent or X-Client-Type sniffing | Cannot distinguish office vs site vs plugin actions in compliance reports | **Closed** — Phase 141: `MobileContextMiddleware` reads `X-Client-Type` (mobile/plugin/web) or sniffs User-Agent; `AuditService` writes the resolved value; `GET /api/admin/audit?source=…` filter added |
| SRV-12 | **No rate limiting per device** | Rate limits are per-IP (10/min auth, 120/min API); no per-device-token limiting | Shared site WiFi could exhaust limits across all devices | **Closed** — `AddRateLimiter` policies now key on device id + auth user |
| SRV-13 | **No file versioning** | Document upload creates new records; no version chain, no diff, no rollback | Cannot track document revision history | **Closed** — `DocumentVersion` table |
| SRV-14 | **No mobile WebSocket auth** | SignalR hubs exist but no mobile-specific auth flow (JWT in query string for WebSocket upgrade) | Mobile cannot receive real-time updates | **Closed** — `realtimeClient.ts` uses `accessTokenFactory` |
| SRV-15 | **No geofence validation** | No project boundary polygon, no server-side location validation | Cannot enforce site-only access for sensitive documents | **Closed** — `IGeofenceValidationService` |

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
