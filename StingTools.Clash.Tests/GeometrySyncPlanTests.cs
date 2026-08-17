using System.Linq;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests;

/// <summary>
/// TRACK C1/C2 — the queue encoding and the retry rule for Planscape geometry
/// sync.
///
/// WHY THESE TWO THINGS AND NOT THE HANDLER
/// ----------------------------------------
/// GeometrySyncHandler is an IExternalEventHandler that needs a UIApplication
/// and a live Document, so nothing in it is reachable from a pure-logic test
/// project (this one deliberately does not reference the Revit API). The two
/// decisions that actually govern whether a change reaches the server are plain
/// arithmetic over 64-bit element ids, so they were extracted into
/// GeometrySyncPlan and are pinned here.
///
/// NOT COVERED: the handler's ordering (check the project link BEFORE draining),
/// the re-queue call itself, and the new GeometrySyncUpdater's trigger scope.
/// Those need Revit. Stated so a green run is not read as proof that a delta
/// survives a failed upload in a real session.
///
/// THE DEFECT THIS GUARDS
/// ----------------------
/// The queue packs a deletion as a NEGATED element id. The sign is therefore the
/// semantics: read it backwards and every deletion becomes an attempt to
/// tessellate an element that no longer exists, while every edit becomes a
/// tombstone that erases live geometry from the server.
/// </summary>
public class GeometrySyncPlanTests
{
    // ── the sign convention ─────────────────────────────────────────────────

    [Fact]
    public void Positive_ids_are_changes_and_negative_ids_are_deletions()
    {
        var (changed, deleted) = GeometrySyncPlan.Partition(new long[] { 10, -20, 30, -40 });

        Assert.Equal(new long[] { 10, 30 }, changed);
        // Deletions come back POSITIVE — the sign was encoding, not data.
        Assert.Equal(new long[] { 20, 40 }, deleted);
    }

    [Fact]
    public void A_large_modified_id_stays_a_change_not_a_tombstone()
    {
        // THE 64-bit regression. Revit 2024+ ElementId.Value can exceed
        // int.MaxValue. The old (int) cast wrapped such an id NEGATIVE, and a
        // negative id is the deletion sentinel — so a large modified/added
        // element was mis-split as a tombstone and the server soft-deleted live
        // geometry the user had just edited. With 64-bit ids it stays positive
        // (a change), and a genuine deletion of the same id still round-trips as
        // a tombstone.
        long big = (long)int.MaxValue + 5;   // wraps to a negative int

        var (changed, deleted) = GeometrySyncPlan.Partition(new[] { big });
        Assert.Equal(new[] { big }, changed);
        Assert.Empty(deleted);

        var (c2, d2) = GeometrySyncPlan.Partition(new[] { -big });
        Assert.Empty(c2);
        Assert.Equal(new[] { big }, d2);
    }

    [Fact]
    public void A_zero_id_is_dropped_rather_than_guessed()
    {
        // 0 is not a valid Revit element id. Treating it as "changed" would try
        // to tessellate nothing; treating it as "deleted" would tombstone
        // element 0. Neither is right, so it goes nowhere.
        var (changed, deleted) = GeometrySyncPlan.Partition(new long[] { 0, 7 });

        Assert.Equal(new long[] { 7 }, changed);
        Assert.Empty(deleted);
    }

    [Fact]
    public void An_empty_or_null_drain_is_empty()
    {
        var (c1, d1) = GeometrySyncPlan.Partition(new long[0]);
        Assert.Empty(c1);
        Assert.Empty(d1);

        var (c2, d2) = GeometrySyncPlan.Partition(null);
        Assert.Empty(c2);
        Assert.Empty(d2);
    }

    // ── the retry rule ──────────────────────────────────────────────────────

    [Fact]
    public void The_retry_set_round_trips_through_the_queue_encoding()
    {
        // What goes back on the queue must partition again into what went in —
        // otherwise a retry silently changes a deletion into an edit.
        var retry = GeometrySyncPlan.BuildRetrySet(new long[] { 10, 30 }, new long[] { 20, 40 });
        var (changed, deleted) = GeometrySyncPlan.Partition(retry);

        Assert.Equal(new long[] { 10, 30 }, changed);
        Assert.Equal(new long[] { 20, 40 }, deleted);
    }

    [Fact]
    public void Only_extracted_changes_are_retried()
    {
        // THE rule. An element whose geometry could not be extracted (deleted
        // since the edit, no solid, view-specific) is deliberately not retried:
        // it would fail extraction again on every save, converting one lost
        // delta into an infinite retry that also drags the genuinely retryable
        // ids around with it forever.
        //
        // Caller drained {10, 20, 30} as changes but only extracted {10, 30}.
        var retry = GeometrySyncPlan.BuildRetrySet(new long[] { 10, 30 }, new long[0]);

        Assert.Equal(new long[] { 10, 30 }, retry);
        Assert.DoesNotContain(20L, retry);
    }

    [Fact]
    public void Every_deletion_is_retried()
    {
        // A tombstone needs no geometry, so nothing can fail to extract — a
        // deletion that did not reach the server is always worth sending again.
        // Losing one is the worst case of the three: the element stays visible
        // in the federated model forever, and a coordinator sees geometry that
        // no longer exists in the source.
        var retry = GeometrySyncPlan.BuildRetrySet(new long[0], new long[] { 20, 40 });

        Assert.Equal(new long[] { -20, -40 }, retry);
    }

    [Fact]
    public void Nothing_sendable_means_nothing_to_retry()
    {
        Assert.Empty(GeometrySyncPlan.BuildRetrySet(new long[0], new long[0]));
        Assert.Empty(GeometrySyncPlan.BuildRetrySet(null, null));
    }

    [Fact]
    public void Non_positive_inputs_never_reach_the_retry_set()
    {
        // Defensive: a negative "changed" id would be re-queued as a deletion,
        // which would erase the element on the next successful sync.
        var retry = GeometrySyncPlan.BuildRetrySet(new long[] { -5, 0, 7 }, new long[] { -3, 0, 9 });

        Assert.Equal(new long[] { 7, -9 }, retry);
        Assert.All(retry, id => Assert.NotEqual(0L, id));
    }
}
