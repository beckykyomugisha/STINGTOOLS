# Gap-fix sprint summary

Maps the 67-day gap report to what actually shipped on this branch.
All items below are in place; CI + ops handle the external tasks documented
in `OPERATIONS_AND_PLAN.md §A`.

## Tier 1 — Critical path (site↔office end-to-end)

| ID | Gap | Shipped |
|----|-----|---------|
| **C1** | **No web dashboard** | Static HTML office dashboard at `/`. Login, project picker, Overview, Issues, Documents, Transmittals, Meetings, Workflows, Warnings, Models + viewer, Schedule, Cost. Serves from `Planscape.Server/src/Planscape.API/wwwroot/` via `UseDefaultFiles() + UseStaticFiles()`. |
| **C2** | **No real-time sync into the plugin** | `StingTools/BIMManager/PlanscapeRealtimeClient.cs` — singleton `HubConnection` wrapper, auto-reconnect, typed events (`IssueCreated`, `IssueUpdated`, `ComplianceChanged`, `DocumentUpdated`, `TransmittalUpdated`, `RevisionCreated`, `GenericNotification`). `PlanscapeServerClient.LoginAsync` fires it up; `Disconnect()` tears it down. `ComplianceController` + `DocumentsController` now emit matching events. |
| **C3** | **Plugin auto-sync incomplete** | `StingToolsApp.OnDocumentSaved` now populates `TagElements` via `CollectTagElements()` (capped at 5K/save) and triggers `SyncScheduler.Instance.SyncNowAsync()` immediately after enqueue. No more 5-minute delay. |
| **C4** | **Plugin only syncs tags + compliance** | `PlanscapeServerClient` gained: `GetDocumentsAsync`, `TransitionDocumentAsync`, `GetMeetingsAsync`, `CreateMeetingAsync`, `GetTransmittalsAsync`, `CreateTransmittalAsync`, `SendTransmittalAsync`, `GetWorkflowRunsAsync`, `LogWorkflowRunAsync`, `GetWarningsAsync`, `PushWarningsAsync`, `GetMimAssetsAsync`, `GetMimDashboardAsync`, `GetPlatformConnectionsAsync`, `GetModelsAsync`, `GetIssueCommentsAsync`, `AddIssueCommentAsync`. |
| **C5** | **Mobile UI for transmittals / meetings / workflows / warnings** | Four new stack routes: `app/transmittals/`, `app/meetings/`, `app/workflows/`, `app/warnings/`. Share the new `CoordinationListScreen<T>` component. Each has its own row renderer and empty state. |
| **C6** | **Notification preferences not honoured** | `NotificationService.NotifyUserAsync` now resolves `UserNotificationPreferences` via `IServiceScopeFactory`, drops the delivery when the per-category toggle is off, when the user is inside their quiet-hours window (with `sla_breach` / `critical` bypass), or when the `Channel` setting (`push` / `signalr` / `email`) excludes the active path. |

## Tier 2 — Production quality

| ID | Gap | Shipped |
|----|-----|---------|
| **P1** | **JWT key rotation** | `Program.cs` uses `IssuerSigningKeys` (plural) so tokens signed with either `Jwt:Key` or `Jwt:PreviousKey` validate during the overlap. `AuthController.GenerateJwt` stamps new tokens with `kid=current`. Rotation procedure documented in `appsettings.Production.template.json`. |
| **P2** | **Issue comments** | `IssueComment` entity + `IssueCommentsController` (GET/POST/PUT/DELETE) + migration `20260420000000_AddIssueComments`. POSTs emit `CommentAdded` to the project group and push a targeted notification to the mentioned user. Mobile client exposes `listIssueComments` + `addIssueComment`. |
| **P3** | **Drawing / sheet markup** | `DocumentMarkup` entity (JSONB shapes + version chain) + `MarkupsController` (CRUD) + migration `20260421000000_AddMarkupScheduleCost`. Mobile can post shape arrays; a future native canvas component reads the same shape schema. |
| **P4** | **Schedule / 4D** | `ScheduleTask` entity (RIBA stage, baseline / planned / actual dates, predecessors, `LinkedMetric`) + `ScheduleController` (CRUD + `/rollup` aggregation). Same migration as P3. |
| **P5** | **Cost tracking** | `CostItem` entity + `CostController` (CRUD + `/summary` aggregation by discipline × kind). Budget / Committed / Actual / Forecast dimensions. |
| **P6** | **FCM exponential retry** | `FirebasePushService` now retries transient 5xx / 429 up to 3 times with exponential backoff (500 ms / 1.5 s / 3.5 s) plus ±20 % jitter, and honours `Retry-After` when the server sends one. |
| **P7** | **Server-side IFC → glTF** | `IModelConverter` interface + `NullModelConverter` (default) + `IfcConvertConverter` CLI wrapper. Pluggable via `ModelConverter:Provider=ifcconvert`. `ModelDerivativeJob` Hangfire job (every 10 min) picks up unconverted IFC uploads, produces `.glb` sidecars via the registered converter, and saves them back as the canonical geometry. |
| **P8** | **Headless model thumbnails** | `IModelThumbnailGenerator` interface + `NullThumbnailGenerator` default. `ModelDerivativeJob` calls it automatically post-conversion; swap in a three.js headless micro-service by registering a real implementation. |
| **P9** | **Mobile project picker** | `src/stores/projectStore.ts` (already shipped) is now the single source of truth — dashboard's local `activeProject` `useState` migrated to `useProjectStore`. Model list, issues, and future screens all read from the same store. |
| **P10** | **Invite acceptance** | `app/accept-invitation.tsx` handles `planscape://accept-invitation?token=…` + the HTTPS universal link. Submits to `/api/auth/accept-invitation` and stores the returned tokens before routing to the tabs. |
| **P11** | **Universal links** | `app.json` — iOS `associatedDomains: ["applinks:planscape.example"]`, Android `intentFilters` covering `/accept-invitation`, `/reset-password`, `/issues`, `/documents`. |

## Follow-ups documented (not blockers)

- Replace `planscape.example` in `app.json` with the real production host before TestFlight / Play.
- Register a real `IModelConverter` (IfcConvert or APS) and an `IModelThumbnailGenerator` once infra is live.
- The static web dashboard is deliberately thin (no framework, no build step) — swap for Next.js / Blazor when the team has capacity; the JSON contract with the API is stable.
- Mobile invite-acceptance + universal-link plist files (`apple-app-site-association`, Android `assetlinks.json`) must be published at the production host. Ops task, not code.
- Controllers for revisions / transmittals / meetings can emit SignalR events in the same pattern as `IssuesController` / `ComplianceController` / `DocumentsController` — copy the two-line `_ = _hub.Clients.Group(...).SendAsync(...)` call; plugin + mobile subscribers already listen for them.
