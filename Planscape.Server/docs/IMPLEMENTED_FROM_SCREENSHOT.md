# Screenshot follow-up — implementation notes

> **Update (round 2):** The four previously-deferred "wants product decision"
> items (OCR, NLP auto-link, Tenant switcher, Custom fields UI) were greenlit
> after the product-decision survey. See sections 4–7 below.



Reference: the "What remains and why I didn't ship it" review table called out
eight items that had been deferred from the previous sprint. This branch closes
four of them; the remaining four stay deferred for the reasons already captured
in [`OPERATIONS_AND_PLAN.md`](./OPERATIONS_AND_PLAN.md).

| Screenshot row | Status | Notes |
|----|----|----|
| Apple / Play / Firebase / DNS | **Still deferred** | External accounts required — nothing the sandbox can reach. Process + credentials already documented in `OPERATIONS_AND_PLAN.md §A.1–A.4`. |
| Server `dotnet build` verification | **Still deferred (but CI gate in place)** | Sandbox has no .NET SDK and `https://dot.net` is out of allowlist. Any C# change pushed to GitHub is compile-gated by `.github/workflows/planscape-server.yml`, which runs `dotnet build` + `dotnet test` on every PR. |
| ACC OAuth, BCF round-trip, COBie w/ attachments | **Still deferred** | Need real third-party credentials + test data — both out of scope for this sandbox. |
| OCR, NLP auto-link to Revit | **Still deferred** | 3-day pieces each; better as focused PRs with a specific model/vendor decision. |
| Tenant switcher, custom-fields UI | **Still deferred** | Significant UX — wants product direction first. |
| **Tenant branding + email templates** | **Shipped ✅** | See below. |
| **i18n** | **Shipped (scaffolding + 3 locales) ✅** | See below. |
| **Monitoring stack wiring (Seq/Elastic/Prometheus/Grafana)** | **Shipped ✅** | See below. |

---

## 1. Monitoring stack (MON-01, MON-02, MON-03)

**Files added/changed**

- `src/Planscape.API/Planscape.API.csproj` — `Serilog.Sinks.Seq`, `Serilog.Sinks.Elasticsearch`, `prometheus-net.AspNetCore`.
- `src/Planscape.API/Program.cs` — conditional Seq + Elastic sinks on top of the existing console/file sinks; `UseHttpMetrics()` + `MapMetrics("/metrics")`.
- `src/Planscape.API/appsettings.json` — `Serilog:Seq`, `Serilog:Elastic`, `Monitoring:ExposeMetrics` sections (all inert until configured).
- `src/Planscape.API/appsettings.Production.template.json` — production overlay.
- `docker/docker-compose.yml` — `seq`, `prometheus`, `grafana` services behind the `observability` profile.
- `docker/monitoring/prometheus.yml` — API scrape config.
- `docker/monitoring/grafana/provisioning/datasources/prometheus.yml` — auto-configured datasource.
- `docker/monitoring/grafana/provisioning/dashboards/planscape-api.json` — starter dashboard (request rate, p95 latency, error rate, in-flight).

**Activating locally**

```bash
cd Planscape.Server/docker
docker compose --profile observability up -d seq prometheus grafana

# Tell the API to log into Seq too:
export Serilog__Seq__ServerUrl=http://localhost:5341
dotnet run --project ../src/Planscape.API
```

Seq UI on <http://localhost:5341>, Grafana on <http://localhost:3001> (`admin` /
`$GRAFANA_ADMIN_PASSWORD`, default `planscape`), Prometheus on
<http://localhost:9090>. `/metrics` on the API exposes Prometheus exposition
format — set `Monitoring:ExposeMetrics=false` if you want to bind-block it at
the reverse proxy instead.

## 2. Tenant branding + email templates (FLEX-03, FLEX-07)

**Files added**

- `src/Planscape.Core/Entities/TenantBranding.cs` — per-tenant row.
- `src/Planscape.Core/Interfaces/ITenantBrandingService.cs` — resolves branding with the fallback chain `row → appsettings → hardcoded`.
- `src/Planscape.Infrastructure/Services/TenantBrandingService.cs` — DB-backed implementation with 5-minute cache.
- `src/Planscape.Infrastructure/Services/EmailTemplateRenderer.cs` — minimal `{{placeholder}}` / `{{#if}}` engine. Zero external deps.
- `src/Planscape.API/EmailTemplates/_layout.en.html` — shared layout.
- `src/Planscape.API/EmailTemplates/invite.*`, `password-reset.*`, `issue-assigned.*` — default templates (subject, html, txt).
- `src/Planscape.API/Controllers/TenantBrandingController.cs` — `GET/PUT/DELETE /api/tenant/branding` + `POST /api/tenant/branding/templates/reload`.
- `src/Planscape.Infrastructure/Data/Migrations/20260417000000_AddTenantBranding.cs` — EF migration for the new table.

**Files changed**

- `src/Planscape.Infrastructure/Services/SmtpEmailService.cs` — renders via the new engine, pulls branding from `ITenantBrandingService`, respects per-tenant `EmailFromName`/`EmailFromAddress`. Falls back to a built-in legacy layout when the renderer is unavailable, so a misconfigured host never silently fails to send.
- `src/Planscape.Infrastructure/Data/PlanscapeDbContext.cs` — new DbSet + model config.
- `src/Planscape.API/appsettings*.json` — `Tenant:DefaultBranding` section (ProductName, AccentColor, HeaderColor, LogoUrl, SupportEmail, EmailSignature, DefaultLanguage).
- `src/Planscape.API/Planscape.API.csproj` — `<None Include="EmailTemplates/**" CopyToOutputDirectory="PreserveNewest" />`.
- `src/Planscape.API/Program.cs` — DI registrations.

**Ready to extend**

- Add a locale: drop `invite.fr.html`/`.subject`/`.txt` into `EmailTemplates/` and it gets picked up automatically (fallback: language → `en` → no-suffix).
- Tenant admin can override any field via `PUT /api/tenant/branding`; non-set fields inherit from `Tenant:DefaultBranding` and ultimately hardcoded defaults. `InvalidateCache()` is called automatically on save.

## 3. i18n (FLEX-15)

**Server**

- `src/Planscape.API/I18n/{en,de,es}.json` — dotted-key resource bundles (errors, notifications, audit verbs, email subjects).
- `src/Planscape.Core/Interfaces/II18nService.cs`.
- `src/Planscape.Infrastructure/Services/I18nService.cs` — flat-key JSON loader, `{placeholder}` substitution, fallback through `language → "en" → literal key`.
- `src/Planscape.API/Middleware/LocaleMiddleware.cs` — resolves the caller's language from (`X-Language` header → `?language=` → `Accept-Language` → `en`), stores it on `HttpContext.Items["Language"]`, and echoes it on the `Content-Language` response header.
- `src/Planscape.API/Controllers/I18nController.cs` — `GET /api/i18n`, `GET /api/i18n/{lang}`, `POST /api/i18n/reload`.
- `src/Planscape.API/Planscape.API.csproj` — `<None Include="I18n/**" CopyToOutputDirectory="PreserveNewest" />`.
- `Program.cs` wires both the service and the middleware.

**Mobile**

- `Planscape/src/i18n/index.ts` — zero-dependency `t(key, vars)` / `useT()` / `setLanguage()` / `getLanguage()` / `initI18n()`. Uses AsyncStorage for persistence and falls back to the device locale.
- `Planscape/src/i18n/locales/{en,de,es}.json` — bundle set.
- `Planscape/src/i18n/README.md` — how to add a language.
- `Planscape/src/api/client.ts` — sends `X-Language: {getLanguage()}` on every authenticated request so the server returns locale-aware errors and server-initiated pushes.
- `Planscape/app/_layout.tsx` — calls `initI18n()` before the first render.
- `Planscape/tsconfig.json` — `resolveJsonModule: true`.

**Ready to extend**

- Add `fr.json` on either side — no code change on the mobile side besides `import fr from "./locales/fr.json"`; server picks it up automatically from the filesystem.
- Swap the mobile implementation for `i18next` later without touching call sites — the public API (`t`, `useT`, `setLanguage`, `getLanguage`, `initI18n`) is the same shape.

---

## 4. OCR on-device scaffold (decisions 1.1=b, 1.2=a, 1.3=tag+serial+drawing+mfr, 1.4=issue-upload, 1.5=b/90%)

Zero-dependency scaffold that activates as soon as a native OCR module is
added. Picks up `NativeModules.PlanscapeVisionOcr` (iOS) or
`NativeModules.MLKitTextRecognition` (Android); cleanly no-ops otherwise so
Expo Go + existing builds keep working.

**Files added**

- `Planscape/src/services/ocr.ts` — abstraction + ISO tag regex (O→0, I→1 fix-ups), drawing number, serial, manufacturer heuristics.
- `Planscape/src/services/ocr.md` — wiring guide for Apple Vision Swift module + `@react-native-ml-kit/text-recognition`.
- `Planscape/src/components/OcrConfirmModal.tsx` — "did you mean?" UI shown when `extractionConfidence < 0.9`.

## 5. NLP auto-link (decisions 2.1=d rules+LLM, 2.2=issue+search, 2.3=tag+grid+fuzzy, 2.4=b, 2.5=redact)

Rule-based resolver with optional LLM fallback (off by default). Redacts PII
before any cloud call. Auto-link threshold = 0.9; candidates below auto-link
still come back ranked for the "did you mean?" picker.

**Files added**

- `Planscape.Server/src/Planscape.Core/Interfaces/INlpResolver.cs` — `NlpResolution`, `NlpCandidate`, `INlpLlmResolver` interfaces.
- `Planscape.Server/src/Planscape.Infrastructure/Services/NlpResolver.cs` — regex (ISO tag, grid) + Levenshtein fuzzy family match + LLM fallback hook.
- `Planscape.Server/src/Planscape.Infrastructure/Services/PiiRedactor.cs` — email / phone / NI / card / guid / long-digit runs → `[redacted]`.
- `Planscape.Server/src/Planscape.API/Controllers/NlpController.cs` — `POST /api/nlp/resolve`.
- `NullLlmResolver` in the same file — default no-op; swap in Azure OpenAI / OpenAI / Anthropic impl when credentials land.

Config: `Nlp:EnableLlmFallback=true` gates the LLM path even when a real
provider is registered. Per-request `allowCloudFallback: false` lets clients
opt out per call.

## 6. Tenant switcher (decisions 3.1=c adaptive, 3.2=b per-tenant JWTs, 3.3=drain+warn)

Server endpoints re-issue a JWT for any tenant the user's email belongs to.
Mobile stores tokens per tenant in SecureStore and offers an adaptive UI
(hidden at 1, list at 2-5, search at 6+).

**Server files**

- `Planscape.Server/src/Planscape.API/Controllers/AuthController.cs` — added `GET /api/auth/tenants` + `POST /api/auth/switch-tenant`.
  - Security: `SwitchTenant` returns 403 if no active `AppUser` row exists for the user's email + target `tenantId` + `IsActive=true`.

**Mobile files**

- `Planscape/src/stores/tenantStore.ts` — Zustand store with `presentation()` (`hidden`/`list`/`search`).
- `Planscape/src/api/tenants.ts` — `fetchMemberships`, `switchTenant`, per-tenant `SecureStore` key helpers (`planscape_token:{tenantId}`).
- `Planscape/src/components/TenantSwitcher.tsx` — header badge + adaptive modal picker with mid-switch offline-queue warning.
- `Planscape/app/(tabs)/_layout.tsx` — `headerRight: () => <TenantSwitcher />` + memberships bootstrap on tab mount.

Mid-switch behaviour: checks `pendingCount()` in the offline queue, warns
before proceeding, then clears the queue on confirmation so the next tenant
doesn't replay the previous tenant's actions.

## 7. Custom fields (decisions 4.1=issues-only, 4.2=9 types, 4.3=b simple table, 4.4=b collapsible, 4.5=c JSONB+GIN, 4.6=archive+rename-in-place+banner+exports-include)

Per-project schema table + JSONB column on `BimIssue` with a GIN index for
fast path queries. Admin CRUD is `Admin,Owner` only; end users see only the
active-schema list via `GET`.

**Server files**

- `Planscape.Server/src/Planscape.Core/Entities/IssueCustomFieldSchema.cs` — schema entity + `CustomFieldType` enum (9 types).
- `Planscape.Server/src/Planscape.Core/Entities/BimIssue.cs` — added `CustomFields` string column.
- `Planscape.Server/src/Planscape.Infrastructure/Data/PlanscapeDbContext.cs` — entity config + GIN index declaration.
- `Planscape.Server/src/Planscape.Infrastructure/Data/Migrations/20260418000000_AddIssueCustomFields.cs` — migration + raw SQL for the GIN index.
- `Planscape.Server/src/Planscape.API/Controllers/IssueCustomFieldsController.cs` — `GET/POST/PUT/DELETE/POST:reorder` under `/api/projects/{projectId}/custom-fields`.
- `Planscape.Server/src/Planscape.Infrastructure/Services/CustomFieldsPurgeJob.cs` — nightly Hangfire job purges fields deleted > 30 days ago and scrubs their values from `BimIssue.CustomFields`.

**Mobile files**

- `Planscape/src/types/customFields.ts` — types + `parseOptions()` + `validateAgainstSchema()`.
- `Planscape/src/api/customFields.ts` — `fetchSchema` for end users + admin CRUD helpers.
- `Planscape/src/components/CustomFieldInput.tsx` — dynamic renderer for all 9 field types + `<CustomFieldsSection>` collapsible wrapper.

Admin rename-in-place is supported — unique `(ProjectId, Key)` constraint
blocks duplicates. Deleting a field soft-archives it (`IsActive=false`,
`DeletedAt=now`); values are preserved on existing issues until the 30-day
purge job runs (schedule: `15 3 * * *` UTC). Exports include custom fields by
default — `BimIssue.CustomFields` is a string column so whatever CSV/COBie/BCF
exporter serialises the entity already picks it up.

---

## Out-of-scope (still deferred)

| Row | Reason | Unblocked by |
|---|---|---|
| Apple / Play / Firebase / DNS | External accounts | Following §A.1-A.4 of `OPERATIONS_AND_PLAN.md` (lead time 1–7 days) |
| `dotnet build` verification | No .NET SDK in sandbox; `https://dot.net` blocked by allowlist | CI workflow `.github/workflows/planscape-server.yml` handles this on PR open |
| ACC OAuth, BCF, COBie w/ attachments | Need vendor credentials + test files | Dedicated PR per connector |
