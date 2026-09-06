using Planscape.PluginSync;

namespace Planscape.Tests;

/// <summary>
/// The delta channel's coalescing contract: N dirty notifications for the same
/// element collapse to ONE row, and a whole burst drains as ONE batch.
///
/// The sibling LiveClashUpdater.GeometrySyncQueue is a ConcurrentQueue and does
/// NOT have this property — dragging a wall thirty times enqueues that wall
/// thirty times. These tests exist so the set semantics that replaced it cannot
/// silently regress back to queue semantics.
///
/// Tests share process-wide static state, so each uses its own doc key and
/// clears first.
/// </summary>
[Collection("SyncDirtyTracker")]
public class SyncDirtyTrackerTests
{
    private static string NewDoc() => "doc-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Repeated_edits_to_one_element_collapse_to_a_single_entry()
    {
        var doc = NewDoc();

        // Dragging one wall thirty times.
        for (int i = 0; i < 30; i++) SyncDirtyTracker.Mark(doc, new long[] { 12345 });

        Assert.Equal(1, SyncDirtyTracker.PendingCount(doc));
        Assert.Single(SyncDirtyTracker.Drain(doc));
    }

    [Fact]
    public void A_bulk_burst_drains_as_one_batch_not_one_per_element()
    {
        var doc = NewDoc();

        // A group move / workset assign / filter apply touching 5,000 elements,
        // reported across many updater callbacks.
        for (int batch = 0; batch < 50; batch++)
            SyncDirtyTracker.Mark(doc, Enumerable.Range(batch * 100, 100).Select(i => (long)i));

        Assert.Equal(5_000, SyncDirtyTracker.PendingCount(doc));

        // ONE drain yields the whole burst — the caller builds ONE payload from it.
        var drained = SyncDirtyTracker.Drain(doc);
        Assert.Equal(5_000, drained.Count);
        Assert.Equal(5_000, drained.Distinct().Count());

        // And the set is empty afterwards, so the next push sends nothing.
        Assert.Equal(0, SyncDirtyTracker.PendingCount(doc));
        Assert.Empty(SyncDirtyTracker.Drain(doc));
    }

    [Fact]
    public void Deletion_supersedes_a_pending_edit_for_the_same_element()
    {
        var doc = NewDoc();

        SyncDirtyTracker.Mark(doc, new long[] { 7 });   // edited
        SyncDirtyTracker.Mark(doc, new long[] { -7 });  // then deleted

        var drained = SyncDirtyTracker.Drain(doc);

        // Exactly one row, and it is the delete sentinel — never both an update
        // row and a delete row for the same element in one batch.
        Assert.Equal(new List<long> { -7 }, drained);
    }

    [Fact]
    public void An_edit_after_a_deletion_does_not_resurrect_the_element()
    {
        var doc = NewDoc();

        SyncDirtyTracker.Mark(doc, new long[] { -7 });
        SyncDirtyTracker.Mark(doc, new long[] { 7 });

        Assert.Equal(new List<long> { -7 }, SyncDirtyTracker.Drain(doc));
    }

    [Fact]
    public void Documents_are_tracked_independently()
    {
        var a = NewDoc();
        var b = NewDoc();

        SyncDirtyTracker.Mark(a, new long[] { 1, 2, 3 });
        SyncDirtyTracker.Mark(b, new long[] { 9 });

        Assert.Equal(3, SyncDirtyTracker.PendingCount(a));
        Assert.Equal(1, SyncDirtyTracker.PendingCount(b));

        SyncDirtyTracker.Drain(a);
        Assert.Equal(0, SyncDirtyTracker.PendingCount(a));
        Assert.Equal(1, SyncDirtyTracker.PendingCount(b));
    }

    [Fact]
    public void Not_due_immediately_after_an_edit()
    {
        var doc = NewDoc();
        SyncDirtyTracker.Mark(doc, new long[] { 1 });

        // The whole point of the debounce: a push must not fire mid-burst.
        Assert.False(SyncDirtyTracker.IsDue(doc));
    }

    [Fact]
    public void Not_due_when_nothing_is_dirty()
    {
        var doc = NewDoc();
        Assert.False(SyncDirtyTracker.IsDue(doc));
        Assert.False(SyncDirtyTracker.AnyDue());
    }

    [Fact]
    public void Debounce_windows_are_the_documented_values()
    {
        // Guards the pacing contract: ~3s quiet period, ~30s hard ceiling.
        Assert.Equal(3_000, SyncDirtyTracker.QuietPeriodMs);
        Assert.Equal(30_000, SyncDirtyTracker.MaxHoldMs);
        Assert.True(SyncDirtyTracker.MaxHoldMs > SyncDirtyTracker.QuietPeriodMs,
            "the hard ceiling must be longer than the quiet period or it would fire first every time");
    }

    [Fact]
    public void Drain_of_an_unknown_document_is_empty_not_null()
    {
        Assert.Empty(SyncDirtyTracker.Drain("never-seen"));
        Assert.Equal(0, SyncDirtyTracker.PendingCount("never-seen"));
    }
}
