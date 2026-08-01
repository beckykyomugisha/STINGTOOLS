using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time SignalR broadcasts are best-effort — a client that misses one
/// gets the same data on its next reconnect/refresh. They must never take
/// down the request that triggered them: by the time a controller broadcasts
/// a change, the actual state change is already committed to the database.
///
/// Before this existed, every `await hub.Clients.Group(...).SendAsync(...)`
/// call site propagated a Redis backplane blip into an unhandled 500 — e.g.
/// a document CDE transition failing with InternalServerError even though
/// the transition itself succeeded and was already saved. Same class of bug
/// as ProjectAccessAttribute's Redis-cache fail-open; this is the
/// SignalR-broadcast equivalent.
///
/// IMPORTANT — this takes a <see cref="Func{Task}"/>, not a <see cref="Task"/>.
/// A first cut of this helper accepted the already-started Task and wrapped
/// only the await in try/catch — that does NOT work:
/// RedisHubLifetimeManager.EnsureRedisServerConnection() (reached via
/// SendAsync → PublishAsync) throws SYNCHRONOUSLY while the Redis backplane
/// is unreachable, before SendAsync ever returns a Task. That exception
/// propagates from evaluating the call-site expression itself — a `Task`
/// parameter is already too late to catch it, because it must be fully
/// evaluated (including that synchronous throw) before the helper is even
/// entered. Deferring the whole call behind a lambda, invoked only once
/// we're inside the try block, is the only shape that actually catches it.
/// Confirmed against the failing case: without the lambda, a live test
/// (Documents_TransitionState_WipToShared) still 500'd; with it, it passes.
///
/// Usage — wrap the ENTIRE call, not just the SendAsync tail:
///   await HubBroadcastExtensions.SafeAsync(
///       () => _hub.Clients.Group(group).SendAsync("Event", payload),
///       _logger, "Event");
/// The logger is optional — controllers without one can omit it and the
/// failure is swallowed silently, matching the precedent already set by
/// RedisPermissionRevocationStore and the JWT OnTokenValidated handler.
/// </summary>
public static class HubBroadcastExtensions
{
    public static async Task SafeAsync(Func<Task> broadcast, ILogger? logger = null, string? eventName = null)
    {
        try
        {
            await broadcast().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[signalr] broadcast '{Event}' failed — continuing (best-effort, real-time update degraded)",
                eventName ?? "(unnamed)");
        }
    }
}
