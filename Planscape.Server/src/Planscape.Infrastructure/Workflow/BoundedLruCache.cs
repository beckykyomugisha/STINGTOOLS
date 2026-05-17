namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 150 — small thread-safe bounded LRU cache used by
/// <see cref="DeliverableStateMachine.RoleOf"/> to memoise inferred
/// roles for unknown state names without growing without bound. Pure
/// in-process — no MemoryCache / IMemoryCache dependency so it ships
/// with the same NuGet footprint as the rest of the workflow layer.
///
/// Implementation is the textbook dict-of-linked-list-nodes pattern:
/// O(1) <see cref="GetOrAdd"/> via dictionary lookup; eviction shifts
/// the node to the head on touch and drops the tail when capacity is
/// exceeded. Thread safety is a single coarse lock — write contention
/// is low for the state-machine use case (one writer per machine
/// instance, small key set), so a striped lock or RWLock would be
/// over-engineering here.
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index;
    private readonly LinkedList<Entry> _order = new();
    private readonly object _gate = new();

    /// <summary>The policy: when this many entries are present, the
    /// next insertion evicts the least-recently-used entry.</summary>
    public int Capacity => _capacity;

    /// <summary>Current number of cached entries. Cheap, but takes
    /// the lock so callers should treat it as a snapshot.</summary>
    public int Count
    {
        get { lock (_gate) return _order.Count; }
    }

    public BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be >= 1");
        _capacity = capacity;
        _index = new Dictionary<TKey, LinkedListNode<Entry>>(comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>
    /// Atomic get-or-add. Touching an existing entry promotes it to the
    /// most-recently-used position. The factory is invoked under the
    /// lock; keep it cheap — for slow factories use the two-step
    /// pattern (TryGetValue then add).
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                return existing.Value.Value;
            }

            var value = factory(key);
            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddFirst(node);
            _index[key] = node;

            if (_order.Count > _capacity)
            {
                var lru = _order.Last;
                if (lru != null)
                {
                    _order.RemoveLast();
                    _index.Remove(lru.Value.Key);
                }
            }
            return value;
        }
    }

    /// <summary>
    /// Test-only: peek without promoting. Used by the unit tests to
    /// assert eviction order. Production callers should always use
    /// <see cref="GetOrAdd"/>.
    /// </summary>
    internal bool TryPeek(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
