using Planscape.Core.Interfaces;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Planscape.Tests;

/// <summary>
/// In-memory <see cref="IReplayGuard"/>. Claims behave exactly like Redis
/// SET NX — first caller wins, later callers are told the key is taken — but
/// without a server, so a WebApplicationFactory test can reach the branch that
/// rejects a replay.
///
/// TTL is accepted and ignored: no test needs a claim to age out mid-run, and a
/// timer here would make the suite time-dependent for no gain.
/// </summary>
public sealed class TestReplayGuard : IReplayGuard
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new();

    /// <summary>
    /// When set, every call throws <see cref="RedisException"/> — the store
    /// being unreachable. Chosen to match the exception filter the production
    /// call site actually catches, so the test exercises the real fail-open
    /// branch rather than a broader one.
    /// </summary>
    public volatile bool SimulateOutage;

    /// <summary>Number of claim attempts, successful or not.</summary>
    public int Attempts;

    public Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Attempts);
        if (SimulateOutage) throw new RedisException("simulated Redis outage");
        return Task.FromResult(_claimed.TryAdd(key, 1));
    }

    /// <summary>Forget all claims — lets a test reuse a key deliberately.</summary>
    public void Reset()
    {
        _claimed.Clear();
        SimulateOutage = false;
        Interlocked.Exchange(ref Attempts, 0);
    }
}

