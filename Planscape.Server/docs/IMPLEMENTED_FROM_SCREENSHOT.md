# Screenshot follow-up — implementation notes

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

## Out-of-scope (still deferred)

| Row | Reason | Unblocked by |
|---|---|---|
| Apple / Play / Firebase / DNS | External accounts | Following §A.1-A.4 of `OPERATIONS_AND_PLAN.md` (lead time 1–7 days) |
| `dotnet build` verification | No .NET SDK in sandbox; `https://dot.net` blocked by allowlist | CI workflow `.github/workflows/planscape-server.yml` handles this on PR open |
| ACC OAuth, BCF, COBie w/ attachments | Need vendor credentials + test files | Dedicated PR per connector |
| OCR / NLP auto-link | ML model choice pending | Product decision + spike |
| Tenant switcher, custom fields UI | UX redesign | Product decision |
