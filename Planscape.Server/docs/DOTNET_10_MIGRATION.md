# .NET 10 LTS Migration Plan

> Status: PLAN — not yet executed.
> Target: complete migration before **Q3 2026**.
> Authority: Phase 175 audit, web research recommendation.

## Why this matters

| Release | Type | GA          | End of support | Status today |
|---|---|---|---|---|
| .NET 8  | LTS  | Nov 2023    | **10 Nov 2026** | We're here |
| .NET 9  | STS  | Nov 2024    | 12 May 2026 (24-mo STS, see [DevBlogs](https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/)) | Skip — not LTS |
| .NET 10 | LTS  | Nov 2025    | Nov 2028        | Migration target |

Sources: [endoflife.date/dotnet](https://endoflife.date/dotnet), [Microsoft .NET release schedule](https://dotnet.microsoft.com/platform/support/policy/dotnet-core).

We need to be off .NET 8 before 10 Nov 2026 to keep receiving security patches. Q3 2026 (deploy-by 30 Sep 2026) gives us a 6-week buffer for unexpected issues.

## Current dependency inventory

All seven projects target `net8.0`:

```
Planscape.API/Planscape.API.csproj
Planscape.Core/Planscape.Core.csproj
Planscape.Infrastructure/Planscape.Infrastructure.csproj
Planscape.MIM/Planscape.MIM.csproj
Planscape.PluginSync/Planscape.PluginSync.csproj
Planscape.Shared/Planscape.Shared.csproj
tests/Planscape.Tests/Planscape.Tests.csproj
```

Top-level NuGet dependencies that lock us to a specific .NET line:

| Package | Current | .NET 10 status (anticipated) | Action |
|---|---|---|---|
| `Microsoft.AspNetCore.*` (Authentication, Authorization, SignalR, Mvc.Testing) | `8.0.11` | Pin to `10.0.x` after .NET 10 GA | Bump as a group |
| `Microsoft.EntityFrameworkCore` + Design + InMemory | `8.0.11` | Pin to `10.0.x` | Bump; review breaking changes — EF 9 already removed lazy-load proxies for owned types, EF 10 likely brings more |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `8.0.11` | Track upstream; usually 6-week lag after EF GA | Wait until matching `10.0.x` |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `8.0.11` | Bumps with ASP.NET line | Bump |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | `8.0.11` | Bumps with ASP.NET line | Bump |
| `Microsoft.Extensions.Hosting.Abstractions` | `8.0.1` | Bumps with .NET line | Bump |
| `Hangfire.AspNetCore` | `1.8.17` | Hangfire 1.8.x targets `netstandard2.0` — will run on .NET 10 unchanged | Smoke-test only |
| `Hangfire.PostgreSql` | `1.20.10` | Same — netstandard2.0 | Smoke-test only |
| `Serilog.AspNetCore` | `8.0.3` | Serilog usually GAs .NET 10 support within 4 weeks of .NET 10 RC | Bump to matching major |
| `OpenTelemetry.*` | `1.9.0` / various | OTel maintains .NET 10 support quickly | Bump to latest |
| `prometheus-net.AspNetCore` | `8.2.1` | Track upstream | Bump |
| `RedisRateLimiting.AspNetCore` | `1.2.0` | Targets `net8.0` — verify upstream supports .NET 10 | Watch — may need fork |
| `BCrypt.Net-Next` | `4.0.3` | netstandard2.0 — works on any .NET | No action |
| `MailKit` | `4.8.0` | Bumps with .NET | Bump |
| `ClosedXML` | `0.104.2` | netstandard2.0 — works | No action |
| `Newtonsoft.Json` | `13.0.3` | netstandard2.0 — works | No action |
| `QuestPDF` | `2024.10.2` | Bumps yearly; check .NET 10 compat | Bump |
| `ZXing.Net` | `0.16.9` | netstandard2.0 — works | No action |
| `SixLabors.ImageSharp` | `3.1.6` | Track upstream | Bump |
| `Microsoft.NET.Test.Sdk` | `17.8.0` | Bump to 17.12+ for .NET 10 | Bump |
| `xunit` | `2.5.3` | xUnit v3 lands soon — evaluate | Likely bump to 2.9+ |

## Risk matrix

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Hangfire reflection breaks under stricter trimming | Low | High | We're not trimming — keep `PublishTrimmed=false`. Document. |
| EF Core 10 query translation regressions | Medium | High | Run full integration test suite + spot-check the 5 hottest endpoints (issues, documents, dashboard, transmittals, schedule). |
| Npgsql 10 prepared-statement / pgbouncer interaction | Medium | High | Verify in staging with the same PgBouncer version we run in prod. See `PLANSCAPE_GAPS.md` for the existing pgbouncer compat note. |
| ASP.NET Core 10 changes default request limits | Low | Medium | Diff `Program.cs` middleware order against the new template. |
| OpenTelemetry instrumentation packages lag | Medium | Low | We can drop OTel temporarily — telemetry is observability, not correctness. |
| `RedisRateLimiting.AspNetCore` stalls upstream | Low | Medium | Library is small (~600 LOC). Worst case fork it. |
| Container base image change | Low | Low | Bump `mcr.microsoft.com/dotnet/sdk:8.0` → `:10.0` and `aspnet:8.0` → `:10.0` in `Planscape.Server/docker/Dockerfile`. |
| Test SDK + xUnit version drift breaks the test runner | Low | Medium | Bump as a group, run tests locally before CI. |

## Plan

### Phase A — Preparation (Q4 2025 / Q1 2026)

1. **Watch .NET 10 RC2** (expected Oct 2025). Spin up a throwaway branch:
   ```bash
   git checkout -b dotnet10-spike
   sed -i 's|<TargetFramework>net8.0</TargetFramework>|<TargetFramework>net10.0</TargetFramework>|g' \
     Planscape.Server/src/*/*.csproj Planscape.Server/tests/*/*.csproj
   ```
2. Run `dotnet restore` against a NuGet feed that has the RC packages. Capture the list of packages that can't be restored — those are the ones we wait on.
3. Run the full test suite (`dotnet test`). Triage failures by package.
4. **Decision gate**: by 15 Jan 2026, confirm that all blocking packages have a .NET 10 build path (released, in-RC, or known fork). If any blocker has no path, plan around it (drop the dependency, fork, or stay on .NET 8 until the blocker resolves).

### Phase B — Migration (Q2 2026)

5. **Branch**: `claude/dotnet-10-migration`. Same `sed` flip + bump every package version to its matching major:
   - `Microsoft.AspNetCore.*` → `10.0.x`
   - `Microsoft.EntityFrameworkCore.*` → `10.0.x`
   - `Npgsql.EntityFrameworkCore.PostgreSQL` → `10.0.x`
   - `Microsoft.Extensions.*` → `10.0.x`
6. **Dockerfile**: bump `mcr.microsoft.com/dotnet/sdk:8.0` → `:10.0` and `aspnet:8.0` → `:10.0`.
7. **Build + test locally**. Fix every analyzer warning that's now an error (the .NET upgrade tooling promotes some warnings).
8. **CI**: temporarily run the test suite against both `net8.0` and `net10.0` (multi-targeting) so PRs into `master` keep both green for the transition. Drop `net8.0` once the migration PR merges.
9. **Run migrations in a sandbox database** (clone of prod). Confirm all EF migrations still apply cleanly under EF 10 — no schema diff drift.
10. **Smoke-test the 5 hottest endpoints** with a load tool (`k6` or `bombardier`) at 2× expected peak QPS. Compare p50/p95/p99 against the .NET 8 baseline.
11. **Soak test for 48h** in a staging environment that mirrors prod (same Postgres, Redis, MinIO, Hangfire workload).

### Phase C — Rollout (Q3 2026)

12. **Canary** to 10% of pods for 1 week. Monitor: error rate, p99 latency, Hangfire job-success rate, Postgres connection counts, GC time. Roll back automatically on RAG-amber metrics.
13. **Promote to 100%** if canary clears. Keep the .NET 8 image on the registry for 30 days as a rollback escape hatch.
14. **Drop multi-targeting**. Remove `net8.0` from csproj `<TargetFramework>` lines and from CI matrices.
15. **Update CLAUDE.md** Tech Stack section: `.NET 8.0 → .NET 10.0`.

### Phase D — Post-migration cleanup (Q4 2026)

16. Adopt .NET 10-only features that we couldn't use under 8:
    - Improved `System.Threading.Lock` for hot paths (replaces `lock(obj)` with a typed lock object).
    - .NET 10 ASP.NET Core `RequireAuthorization` policy improvements.
    - `IAsyncEnumerable` better integration with EF Core streaming.
17. Re-evaluate Native AOT for narrow side-services (the converter sidecar). Main API stays JIT — Hangfire reflection + EF dynamic LINQ rule out AOT for the foreseeable future.
18. Delete this document.

## Out of scope

- **.NET 9** — STS only. Don't ship it to production.
- **Native AOT** for the main API — incompatible with Hangfire and EF Core dynamic LINQ.
- **Switching to `dotnet/runtime` from `dotnet/aspnet` base images** — not a .NET 10 migration concern.

## Quick local-spike command

For anyone curious before Q4 2025:

```bash
# Requires .NET 10 RC SDK
git checkout -b dotnet10-spike
find Planscape.Server -name '*.csproj' -exec \
  sed -i 's|<TargetFramework>net8.0</TargetFramework>|<TargetFramework>net10.0</TargetFramework>|g' {} +
dotnet restore Planscape.Server/Planscape.sln
dotnet build  Planscape.Server/Planscape.sln /p:TreatWarningsAsErrors=false
dotnet test   Planscape.Server/tests/Planscape.Tests/Planscape.Tests.csproj
```
