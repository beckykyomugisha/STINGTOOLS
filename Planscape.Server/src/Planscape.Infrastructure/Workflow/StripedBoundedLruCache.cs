namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 151 — striped variant of <see cref="BoundedLruCache{TKey,TValue}"/>.
///
/// The single coarse lock in the un-striped version is fine when one
/// thread queries <c>RoleOf</c> per request, but a future codepath that
/// hits the same machine instance from many threads (e.g. a batch
/// validator running across a 100k-deliverable project) would serialise
/// on it. This variant fans the work out across N stripes — each stripe
/// is its own self-contained <see cref="BoundedLruCache{TKey,TValue}"/>
/// with its own lock, so concurrent reads/writes targeting different
/// keys don't contend.
///
/// Total capacity = stripe count × per-stripe capacity. Eviction is
/// per-stripe — a heavily-trafficked stripe may evict an entry while
/// another stripe has slack — but for the short-lived per-instance
/// state-machine cache this is fine.
///
/// Stripe count is rounded up to the next power of two so the
/// modulo-via-mask path is one AND instead of a div.
/// </summary>
internal sealed class StripedBoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly BoundedLruCache<TKey, TValue>[] _stripes;
    private readonly int _stripeMask;
    private readonly IEqualityComparer<TKey> _comparer;

    /// <summary>Number of stripes (always a power of two).</summary>
    public int StripeCount => _stripes.Length;

    /// <summary>Approximate live entry count across all stripes. Cheap
    /// but takes each stripe's lock individually, so callers should
    /// treat it as a snapshot.</summary>
    public int Count
    {
        get
        {
            var total = 0;
            for (int i = 0; i < _stripes.Length; i++) total += _stripes[i].Count;
            return total;
        }
    }

    public StripedBoundedLruCache(int totalCapacity, int stripeCount = 16, IEqualityComparer<TKey>? comparer = null)
    {
        if (totalCapacity < 1) throw new ArgumentOutOfRangeException(nameof(totalCapacity), "totalCapacity must be >= 1");
        if (stripeCount < 1) throw new ArgumentOutOfRangeException(nameof(stripeCount), "stripeCount must be >= 1");

        // Round to next pow2 so the mask-based dispatch is correct.
        var rounded = NextPowerOfTwo(stripeCount);
        // Per-stripe capacity rounds up so the sum is >= totalCapacity.
        var perStripe = Math.Max(1, (totalCapacity + rounded - 1) / rounded);

        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _stripes = new BoundedLruCache<TKey, TValue>[rounded];
        for (int i = 0; i < rounded; i++)
            _stripes[i] = new BoundedLruCache<TKey, TValue>(perStripe, _comparer);
        _stripeMask = rounded - 1;
    }

    /// <summary>O(1) get-or-add with stripe-local locking. Factory runs
    /// inside the stripe lock — keep it cheap. Threads contending on
    /// different stripes don't block each other.</summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        var stripe = _stripes[StripeFor(key)];
        return stripe.GetOrAdd(key, factory);
    }

    /// <summary>Test-only: peek without promoting. See
    /// <see cref="BoundedLruCache{TKey,TValue}.TryPeek"/>.</summary>
    internal bool TryPeek(TKey key, out TValue value) =>
        _stripes[StripeFor(key)].TryPeek(key, out value);

    private int StripeFor(TKey key) =>
        _comparer.GetHashCode(key) & _stripeMask;

    private static int NextPowerOfTwo(int v)
    {
        if (v <= 1) return 1;
        v--;
        v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
        return v + 1;
    }
}
