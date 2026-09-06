#pragma warning disable CS0618 // SyncClient is deprecated but is still the drain path the plugin uses.
using Planscape.PluginSync;
using Planscape.Shared.Models;

namespace Planscape.Tests;

/// <summary>
/// The offline queue decides whether queued work is retried or thrown away, and
/// shipped with no coverage at all. These tests pin the behaviour that matters:
/// fatal-vs-transient handling, one-payload-one-file, and the file-naming
/// collision that used to lose payloads silently.
/// </summary>
public class OfflineQueueTests : IDisposable
{
    private readonly string _dir;

    public OfflineQueueTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "planscape-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static PluginSyncPayload Payload(int elementCount = 1, Guid? projectId = null)
    {
        var elements = Enumerable.Range(1, elementCount)
            .Select(i => new TagElementSync { RevitElementId = i, Tag1 = $"T{i}" })
            .ToList();
        return new PluginSyncPayload
        {
            ProjectId = projectId ?? Guid.NewGuid(),
            UserName = "sting",
            TagElements = elements,
        };
    }

    private int FileCount() => Directory.GetFiles(_dir, "sync_*.json").Length;

    /// <summary>Scripted client: returns a queued result per call.</summary>
    private sealed class ScriptedClient : SyncClient
    {
        private readonly Queue<SyncResult> _results;
        public int Calls { get; private set; }
        public List<PluginSyncPayload> Received { get; } = new();

        public ScriptedClient(params SyncResult[] results)
            : base("http://localhost:0") => _results = new Queue<SyncResult>(results);

        public override Task<SyncResult> SyncAsync(PluginSyncPayload payload)
        {
            Calls++;
            Received.Add(payload);
            var r = _results.Count > 0 ? _results.Dequeue() : new SyncResult { Success = true, StatusCode = 200 };
            return Task.FromResult(r);
        }
    }

    private static SyncResult Ok() => new() { Success = true, StatusCode = 200 };
    private static SyncResult Fatal(int code = 404) => new() { Success = false, StatusCode = code, ErrorMessage = "fatal" };
    private static SyncResult Transient(int code = 503) => new() { Success = false, StatusCode = code, ErrorMessage = "transient" };

    // ── one payload → one file ────────────────────────────────────────────

    [Fact]
    public void One_payload_of_many_elements_is_exactly_one_queue_file()
    {
        var queue = new OfflineQueue(_dir);

        // The delta channel coalesces N dirty elements into ONE payload and
        // hands the queue one file — never one enqueue per element.
        queue.Enqueue(Payload(elementCount: 5_000));

        Assert.Equal(1, FileCount());
        Assert.Equal(1, queue.Count);

        var peeked = queue.PeekAll();
        Assert.Single(peeked);
        Assert.Equal(5_000, peeked[0].Payload.TagElements!.Count);
    }

    [Fact]
    public void Rapid_successive_enqueues_do_not_overwrite_each_other()
    {
        var queue = new OfflineQueue(_dir);

        // Queue file names were built from a millisecond timestamp alone, so two
        // enqueues inside the same millisecond produced the same path and the
        // second File.WriteAllText silently overwrote the first. This loop
        // reliably lands several writes in one millisecond.
        const int n = 200;
        for (int i = 0; i < n; i++) queue.Enqueue(Payload(elementCount: 1, projectId: Guid.NewGuid()));

        Assert.Equal(n, FileCount());
        Assert.Equal(n, queue.Count);

        // All distinct payloads survived.
        var ids = queue.PeekAll().Select(p => p.Payload.ProjectId).ToHashSet();
        Assert.Equal(n, ids.Count);
    }

    [Fact]
    public void PeekAll_returns_payloads_in_enqueue_order()
    {
        var queue = new OfflineQueue(_dir);
        var expected = new List<Guid>();
        for (int i = 0; i < 50; i++)
        {
            var p = Payload(1, Guid.NewGuid());
            expected.Add(p.ProjectId);
            queue.Enqueue(p);
        }

        // FIFO ordering is lexicographic on the file name, which the collision
        // suffix must not disturb.
        Assert.Equal(expected, queue.PeekAll().Select(p => p.Payload.ProjectId).ToList());
    }

    // ── fatal vs transient ────────────────────────────────────────────────

    [Fact]
    public async Task Fatal_4xx_drops_the_payload_and_keeps_draining()
    {
        var queue = new OfflineQueue(_dir);
        queue.Enqueue(Payload());
        queue.Enqueue(Payload());
        queue.Enqueue(Payload());

        var client = new ScriptedClient(Fatal(), Ok(), Ok());
        int synced = await queue.DrainAsync(client);

        // The bad payload can't succeed on retry (malformed body, revoked token,
        // vanished tenant), so it is discarded rather than blocking the queue —
        // but the two good ones behind it still go.
        Assert.Equal(3, client.Calls);
        Assert.Equal(2, synced);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Transient_5xx_stops_the_drain_and_keeps_the_payload()
    {
        var queue = new OfflineQueue(_dir);
        queue.Enqueue(Payload());
        queue.Enqueue(Payload());
        queue.Enqueue(Payload());

        var client = new ScriptedClient(Transient());
        int synced = await queue.DrainAsync(client);

        // Server is down — stop immediately and retry everything next tick.
        Assert.Equal(1, client.Calls);
        Assert.Equal(0, synced);
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public async Task Network_failure_with_no_status_is_treated_as_transient()
    {
        var queue = new OfflineQueue(_dir);
        queue.Enqueue(Payload());

        // StatusCode 0 = network failure, not a server verdict.
        var client = new ScriptedClient(new SyncResult { Success = false, StatusCode = 0 });
        int synced = await queue.DrainAsync(client);

        Assert.Equal(0, synced);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task Successful_drain_empties_the_queue()
    {
        var queue = new OfflineQueue(_dir);
        for (int i = 0; i < 5; i++) queue.Enqueue(Payload());

        int synced = await queue.DrainAsync(new ScriptedClient());

        Assert.Equal(5, synced);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void SyncResult_classifies_status_codes_the_way_the_drain_depends_on()
    {
        Assert.True(new SyncResult { StatusCode = 0 }.IsTransient);
        Assert.True(new SyncResult { StatusCode = 500 }.IsTransient);
        Assert.True(new SyncResult { StatusCode = 503 }.IsTransient);

        Assert.True(new SyncResult { StatusCode = 400 }.IsFatalRequestError);
        Assert.True(new SyncResult { StatusCode = 404 }.IsFatalRequestError);
        Assert.False(new SyncResult { StatusCode = 500 }.IsFatalRequestError);
        Assert.False(new SyncResult { StatusCode = 404 }.IsTransient);
    }

    // ── discipline filter ─────────────────────────────────────────────────

    [Fact]
    public async Task Discipline_filter_narrows_elements_but_preserves_payload_fields()
    {
        var queue = new OfflineQueue(_dir);
        var payload = new PluginSyncPayload
        {
            ProjectId = Guid.NewGuid(),
            UserName = "sting",
            PluginVersion = "2.2.0",
            RevitVersion = "2025",
            Compliance = new ComplianceSync { TotalElements = 3, RagStatus = "AMBER" },
            SeqCounters = new Dictionary<string, int> { ["M"] = 1 },
            TagElements = new List<TagElementSync>
            {
                new() { RevitElementId = 1, Disc = "M" },
                new() { RevitElementId = 2, Disc = "E" },
                new() { RevitElementId = 3, Disc = "" }, // untagged — always included
            },
        };
        queue.Enqueue(payload);

        var client = new ScriptedClient();
        await queue.DrainAsync(client, new HashSet<string> { "M" });

        var sent = Assert.Single(client.Received);
        Assert.Equal(new long[] { 1, 3 }, sent.TagElements!.Select(e => e.RevitElementId).ToArray());

        // The filtered rebuild must not lose the rest of the payload — it used to
        // be a hand-written field list that would silently drop new fields.
        Assert.Equal(payload.ProjectId, sent.ProjectId);
        Assert.Equal("sting", sent.UserName);
        Assert.Equal("2.2.0", sent.PluginVersion);
        Assert.Equal("2025", sent.RevitVersion);
        Assert.NotNull(sent.Compliance);
        Assert.Equal("AMBER", sent.Compliance!.RagStatus);
        Assert.NotNull(sent.SeqCounters);
    }

    [Fact]
    public async Task Payload_matching_no_discipline_is_left_for_a_later_wider_drain()
    {
        var queue = new OfflineQueue(_dir);
        queue.Enqueue(new PluginSyncPayload
        {
            ProjectId = Guid.NewGuid(),
            TagElements = new List<TagElementSync> { new() { RevitElementId = 1, Disc = "E" } },
        });

        var client = new ScriptedClient();
        int synced = await queue.DrainAsync(client, new HashSet<string> { "M" });

        Assert.Equal(0, client.Calls);
        Assert.Equal(0, synced);
        Assert.Equal(1, queue.Count); // retained, not discarded
    }

    // ── cap + drop counter ────────────────────────────────────────────────

    /// <remarks>
    /// The cap assertions are deliberately combined into one test. Reaching the
    /// 500-file cap means ~500 enqueues, each of which lists the whole queue
    /// directory, so tripping it is quadratic in file operations. Doing that
    /// once instead of twice keeps this file from adding noticeable parallel IO
    /// load to the rest of the suite.
    /// </remarks>
    [Fact]
    public void Queue_is_capped_and_the_loss_is_counted_and_persisted()
    {
        var queue = new OfflineQueue(_dir);

        // 500-file cap. Going past it must drop the OLDEST and record the loss,
        // so the dock panel can warn instead of quietly losing offline work.
        for (int i = 0; i < 502; i++) queue.Enqueue(Payload());

        Assert.True(queue.Count <= 500, $"queue grew past its cap: {queue.Count}");
        int dropped = queue.DroppedSinceLastDrain;
        Assert.True(dropped > 0, "dropped payloads must be counted, not lost silently");

        // A Revit restart must not erase the fact that offline data was lost.
        Assert.Equal(dropped, new OfflineQueue(_dir).DroppedSinceLastDrain);

        queue.AcknowledgeDrops();
        Assert.Equal(0, queue.DroppedSinceLastDrain);
    }

    [Fact]
    public void Corrupt_queue_files_are_skipped_not_fatal()
    {
        var queue = new OfflineQueue(_dir);
        queue.Enqueue(Payload());
        File.WriteAllText(Path.Combine(_dir, "sync_20260802_101010_111_000001.json"), "{ this is not json");

        var peeked = queue.PeekAll();
        Assert.Single(peeked); // the good one survives; the corrupt one is ignored
    }
}
