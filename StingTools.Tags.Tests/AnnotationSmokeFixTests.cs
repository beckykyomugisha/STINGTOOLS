using System;
using System.Linq;
using StingTools.Core.Drawing;
using StingTools.Core.Drawing.Dimensioning;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The two in-Revit smoke failures of 2026-10-01 (a7b7cff50), pinned on their
    /// Revit-free halves:
    ///  * a curved wall was skipped by AutoDimWallLength with no warning at all;
    ///  * drainage IL notes whose provenance stamp could not be written were
    ///    treated as old unstamped orphans after the pipe moved, and nothing in
    ///    the run said the stamp had failed.
    /// </summary>
    public class AnnotationSmokeFixTests
    {
        // ── Wall axis: a skipped wall is always named ─────────────────────

        public static TheoryData<WallLocationKind> NonStraightKinds()
        {
            var d = new TheoryData<WallLocationKind>();
            foreach (var k in Enum.GetValues(typeof(WallLocationKind)).Cast<WallLocationKind>())
                if (k != WallLocationKind.Line) d.Add(k);
            return d;
        }

        [Theory]
        [MemberData(nameof(NonStraightKinds))]
        public void Every_non_straight_wall_kind_is_named_in_a_warning(WallLocationKind kind)
        {
            Assert.False(WallAxisRules.HasAxis(kind));
            var w = WallAxisRules.SkipWarning("AutoDimWallLength", "1658704", kind);
            Assert.NotNull(w);
            Assert.Contains("1658704", w);
            Assert.StartsWith("AutoDimWallLength", w);
        }

        [Fact]
        public void Curved_wall_warning_says_it_is_curved()
        {
            var w = WallAxisRules.SkipWarning("AutoDimWallLength", "42", WallLocationKind.Arc);
            Assert.NotNull(w);
            Assert.Contains("curved", w, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Straight_wall_has_an_axis_and_no_warning()
        {
            Assert.True(WallAxisRules.HasAxis(WallLocationKind.Line));
            Assert.Null(WallAxisRules.SkipWarning("AutoDimWallLength", "42", WallLocationKind.Line));
        }

        // ── Provenance stamps: a failed stamp reaches the run's warnings ──

        [Fact]
        public void All_stamps_written_means_no_warning()
        {
            var t = new ProvenanceStampTally();
            t.Record(true); t.Record(true); t.Record(true);
            Assert.Equal(3, t.Attempted);
            Assert.Equal(0, t.Failed);
            Assert.Null(t.Warning("AutoSpotInvert"));
        }

        [Fact]
        public void A_failed_stamp_is_reported_with_its_count_and_reason()
        {
            var t = new ProvenanceStampTally();
            t.Record(false, "Writing of Entities of this Schema is not allowed to the current add-in.");
            t.Record(false, "second reason is not repeated");
            t.Record(true);
            var w = t.Warning("AutoSpotInvert");
            Assert.NotNull(w);
            Assert.StartsWith("AutoSpotInvert", w);
            Assert.Contains("2 of 3", w);
            Assert.Contains("not allowed to the current add-in", w);
            Assert.DoesNotContain("second reason", w);
            // It must say what the user will see next run, or the next run's
            // "older, unstamped" message still reads as a matching bug.
            Assert.Contains("unstamped", w);
        }

        [Fact]
        public void A_vendor_refusal_names_the_vendor_the_schema_requires()
        {
            var w = AnnotationProvenance.StampFailureWarning("AutoSpotInvert", 3, 3,
                "Writing of Entities of this Schema is not allowed to the current add-in.");
            Assert.NotNull(w);
            Assert.Contains("Planscape", w);
        }

        [Fact]
        public void Any_other_failure_does_not_guess_at_the_vendor()
        {
            var w = AnnotationProvenance.StampFailureWarning("AutoSpotInvert", 1, 1, "Attempt to modify the model outside of transaction.");
            Assert.NotNull(w);
            Assert.DoesNotContain("Planscape", w);
        }

        [Fact]
        public void Schema_vendor_named_in_the_warning_is_the_one_the_plugin_ships_with()
        {
            // The warning tells the user which VendorId the schema needs. If the
            // manifest or StingSchemaBuilder ever changes it, this text would lie.
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StingTools.addin")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools.addin");
            var addin = System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "StingTools.addin"));
            Assert.Contains($"<VendorId>{AnnotationProvenance.SchemaVendorId}</VendorId>", addin);
            var builder = System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "StingTools", "Core", "Storage", "StingSchemaBuilder.cs"));
            Assert.Contains($"VendorId = \"{AnnotationProvenance.SchemaVendorId}\"", builder);
        }

        // ── Drainage notes of a deleted pipe are removed ──────────────────

        [Fact]
        public void Pass_runs_when_the_last_drain_is_gone_but_its_notes_remain()
        {
            // The smoke fixture's only drain was deleted: no drainage pipe left in
            // the view, three stamped notes still in it. The pass used to return.
            Assert.True(InvertNoteRules.NeedsPass(drainagePipesInView: 0, stampedNotesInView: 3));
        }

        [Fact]
        public void Pass_runs_when_there_are_pipes_and_skips_an_empty_view()
        {
            Assert.True(InvertNoteRules.NeedsPass(1, 0));
            Assert.False(InvertNoteRules.NeedsPass(0, 0));
        }

        [Fact]
        public void Notes_of_a_deleted_pipe_are_removed_even_when_no_pipe_was_processed()
        {
            var keys = new[]
            {
                AnnotationProvenance.Key("drain-1", "US"),
                AnnotationProvenance.Key("drain-1", "DS"),
                AnnotationProvenance.Key("drain-1", "GRAD"),
            };
            var remove = InvertNoteRules.KeysToRemove(keys,
                new System.Collections.Generic.HashSet<string>(),
                new System.Collections.Generic.HashSet<string>(),
                host => false);
            Assert.Equal(keys.OrderBy(k => k), remove.OrderBy(k => k));
        }

        [Fact]
        public void Notes_of_an_existing_pipe_not_in_this_run_are_left_alone()
        {
            var keys = new[] { AnnotationProvenance.Key("drain-2", "US") };
            var remove = InvertNoteRules.KeysToRemove(keys,
                new System.Collections.Generic.HashSet<string>(),
                new System.Collections.Generic.HashSet<string>(),
                host => true);
            Assert.Empty(remove);
        }

        [Fact]
        public void A_pipe_made_level_loses_its_downstream_il_and_gradient_only()
        {
            var us = AnnotationProvenance.Key("drain-3", "US");
            var ds = AnnotationProvenance.Key("drain-3", "DS");
            var grad = AnnotationProvenance.Key("drain-3", "GRAD");
            var remove = InvertNoteRules.KeysToRemove(new[] { us, ds, grad },
                new System.Collections.Generic.HashSet<string> { us },
                new System.Collections.Generic.HashSet<string> { "drain-3" },
                host => true);
            Assert.Equal(new[] { ds, grad }.OrderBy(k => k), remove.OrderBy(k => k));
        }

        [Fact]
        public void A_note_written_this_run_is_never_removed()
        {
            var us = AnnotationProvenance.Key("drain-4", "US");
            var remove = InvertNoteRules.KeysToRemove(new[] { us },
                new System.Collections.Generic.HashSet<string> { us },
                new System.Collections.Generic.HashSet<string> { "drain-4" },
                host => false);
            Assert.Empty(remove);
        }

        [Fact]
        public void A_failure_without_a_reason_still_warns()
        {
            var t = new ProvenanceStampTally();
            t.Record(false);
            Assert.NotNull(t.Warning("AutoDimWallLength"));
        }
    }
}
