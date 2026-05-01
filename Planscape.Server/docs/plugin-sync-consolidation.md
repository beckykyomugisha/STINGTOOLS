# Plugin sync consolidation (S3.5)

**Goal**: one HTTP client between the Revit plugin and the server, not two.

## Today

| Layer | Lives in | Used by |
|---|---|---|
| `PlanscapeServerClient` | `StingTools/BIMManager/PlanscapeServerClient.cs` (~970 lines) | Manual on-demand sync from BIM Coordination Center buttons; called everywhere new |
| `Planscape.PluginSync.SyncClient` + `SyncScheduler` + `OfflineQueue` | `Planscape.Server/src/Planscape.PluginSync/` | The background tick that fires every 5 minutes; wired in `PlatformLinkCommands.cs` line 2039+ |

Both stacks issue `Authorization: Bearer <jwt>` requests against the same API. The duplication is the legacy of two phases of work: PluginSync was built first as a stand-alone library, then `PlanscapeServerClient` grew up beside it as the single entry point for new endpoints.

## Target

One client in the plugin, owned by `StingTools.BIMManager.PlanscapeServerClient`:

- Consolidates HTTP + auth + retry + offline queue
- Exposes a public `RegisterBackgroundTick(TimeSpan period, Action onTick)` so the existing `PlatformLinkCommands` wiring can subscribe without depending on `Planscape.PluginSync`
- Owns the `OfflineQueue` (file-backed, store-and-forward) — currently maintained in two places

## Migration steps

1. **Mark deprecated** _(this commit)_. Add `[Obsolete(...)]` + `<remarks>` headers to the three classes in `Planscape.PluginSync` so any new reference fails the compile with a clear pointer.
2. **Move offline-queue logic** into `PlanscapeServerClient`. Wrap every write call with `EnqueueOnFailure(...)`; on next successful tick, drain the queue.
3. **Migrate `SyncScheduler` ticks** to a `PlanscapeServerClient.RegisterBackgroundTick` API. Update `PlatformLinkCommands.cs:2039+` to subscribe through the new surface.
4. **Delete the project**. Remove the `ProjectReference` from `StingTools.csproj`, the `<Project>` node from `Planscape.sln`, the `COPY` line from the API Dockerfile, and the directory itself.

Each step is a separate commit so any regression is easy to bisect.

## Why deprecate, not delete?

`Planscape.PluginSync.SyncScheduler` is currently the only thing driving the plugin's 5-minute sync ticks. Removing it before the migration would silently break background sync for every connected author. Deprecation now flags new uses; the migration commits land when there's bandwidth to test against a real Revit session.

## Compile-time enforcement

The `[Obsolete(...)]` attribute makes the compiler emit a warning at every call site. Once warnings-as-errors is enabled in CI (a pre-existing TODO in `Directory.Build.props`), any new use will fail the build — exactly what we want.
