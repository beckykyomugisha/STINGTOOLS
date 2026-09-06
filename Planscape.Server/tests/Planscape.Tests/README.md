# Planscape.Tests

Unit and integration tests for the Planscape server.

```bash
cd Planscape.Server/tests/Planscape.Tests
dotnet test
```

No Docker, no Postgres, no Redis, and no environment variables are required.
A clean checkout on a clean machine should reproduce CI exactly.

## Why no configuration is needed

### `Jwt:Key`

`Program.cs` fail-fasts when `Jwt:Key` is absent, so **every** host-building
test used to die at startup unless the developer happened to have `Jwt__Key`
exported in their shell. That is why an earlier "the suite is fixed" claim did
not reproduce: 265 passed / 155 failed on a clean machine, versus 347 / 73 with
the variable set.

`PlanscapeWebApplicationFactory` now supplies a throwaway key. Two details
matter if you ever touch it:

- It must go through `builder.UseSetting("Jwt:Key", …)`, **not**
  `ConfigureAppConfiguration`. `Program.cs` reads `builder.Configuration["Jwt:Key"]`
  while the host is still being *built*; `ConfigureAppConfiguration` callbacks
  are applied after that read, so injecting there leaves the fail-fast
  untouched.
- The value looks like line noise on purpose — it has to clear `Program.cs`'s
  guards (32+ chars, not in the banned list, 4+ distinct characters).

It is test-only and never leaves the in-process host: it signs tokens for an
in-memory database that is discarded when the factory is disposed.

**Overriding it:** environment configuration still wins, so
`Jwt__Key=… dotnet test` behaves as before. Nothing else reads the default.

### Redis, Postgres, Hangfire

The factory substitutes rather than deletes, so the production startup path
stays under test instead of being routed around:

| Production | Under test | Why |
|---|---|---|
| PostgreSQL | EF InMemory, unique DB per factory | isolation between test classes |
| Hangfire + Postgres storage | Hangfire + in-memory storage | `UseHangfireDashboard` and ~40 static `RecurringJob.AddOrUpdate` calls are unconditional in `Program.cs`; deleting the feature broke host construction outright |
| `RedisReplayGuard` | `TestReplayGuard` | no Redis is reachable, so the real guard always threw and every call took the caller's fail-open branch — the *blocking* half had no coverage |

Logging is **not** substituted, and asserting on log output does not work here.
Serilog's `Log.Logger` is process-global; when the suite runs many
`WebApplicationFactory` hosts concurrently they race over it, so a controller's
`ILogger` can resolve against a different host's pipeline and an
`ILoggerProvider` attached to your factory captures nothing. Such a test passes
in isolation and fails in the suite. See ROADMAP DEP-13 — same family as DEP-7.

Rate limiting is disabled (`RateLimiting:Enabled=false`). xunit runs test classes
in parallel and every request originates from the same loopback IP, so the
production "auth" policy (5 attempts / 5 min per IP) is exhausted almost
immediately and unrelated tests fail with 429 instead of their real assertion.

EF InMemory raises `TransactionIgnoredWarning` as an error by default, which
made any handler calling `BeginTransactionAsync` return 500. The factory
downgrades it. Isolation-level behaviour genuinely is not exercised here — that
belongs to the real-Postgres suite (`PostgresSequenceCounterTests`).

## The known-failing baseline

`../known-failing-tests.txt` lists tests that were already failing before the
CI workflow existed. CI fails on **new** failures only — gating on "zero
failures" would make it permanently red and therefore ignored.

`../check-new-failures.sh <test-output-file>` enforces this. It also prints a
notice when a baseline entry starts passing.

**If you fix one of these, delete its line in the same PR.** The check then
guards your fix from regressing. The file should only ever shrink.

Caveat, honestly stated: a few entries are order/parallelism dependent
(ROADMAP DEP-7 — `Program.cs` reads the process-wide `Hangfire.JobStorage.Current`
during host build). A test that passed when the file was generated can fail in
CI and be reported as "new". Confirm against `main` before treating it as a
regression.
