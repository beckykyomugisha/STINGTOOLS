using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Static electrical result caches (Fault Current, Feeder Sizing, Arc Flash) are consumed
    /// by element id. Without the document they came from, one model's study was applied to
    /// another — a detached copy keeps the original's ids.
    /// </summary>
    public class ElecResultScopeTests
    {
        [Fact]
        public void Same_document_matches()
            => Assert.True(ElecResultScope.Matches(ElecResultScope.Key(@"C:\P\A.rvt", "A"), ElecResultScope.Key(@"c:\p\a.rvt", "A")));

        [Fact]
        public void A_detached_copy_with_the_same_ids_does_not_match_the_original()
        {
            string original = ElecResultScope.Key(@"C:\P\Kampala.rvt", "Kampala");
            string detached = ElecResultScope.Key("", "Kampala_detached");
            Assert.False(ElecResultScope.Matches(original, detached));
            Assert.Contains("another model (KAMPALA)", ElecResultScope.Refusal("Fault Current", original));
        }

        [Fact]
        public void Two_unsaved_models_differ_by_title()
            => Assert.False(ElecResultScope.Matches(ElecResultScope.Key("", "Project1"), ElecResultScope.Key("", "Project2")));

        [Fact]
        public void No_recorded_document_never_matches()
        {
            Assert.False(ElecResultScope.Matches(null, ElecResultScope.Key("", "Project1")));
            Assert.Equal("Run Fault Current on this model first.", ElecResultScope.Refusal("Fault Current", null));
        }

        /// <summary>Every reader of a LastResults cache checks the document it came from.</summary>
        [Theory]
        [InlineData("Commands/Electrical/FaultCurrent/FaultCurrentCommand.cs", "class AicRatingCommand")]
        [InlineData("Commands/Electrical/ArcFlash/ArcFlashCommand.cs", "class ArcFlashCommand")]
        [InlineData("Commands/Electrical/ArcFlash/ArcFlashLabelSheetCommand.cs", "class ArcFlashLabelSheetCommand")]
        [InlineData("Commands/Electrical/ElectricalSnapshotBuilder.cs", "FeederSizerCommand.LastResults")]
        [InlineData("Commands/Electrical/Export/ExternalExportEngine.cs", "FaultCurrentCommand.LastResults")]
        public void Cache_readers_check_the_document(string file, string anchor)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", file.Replace('/', Path.DirectorySeparatorChar)));
            int at = src.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(at >= 0, anchor);
            Assert.Contains("ElecResultScope.Matches", src.Substring(Math.Max(0, at - 2000), Math.Min(src.Length - Math.Max(0, at - 2000), 6000)));
        }

        /// <summary>
        /// ELEC-31: actions on the Electrical panel's rows (PNLS Save, SLD zoom / open schedule)
        /// use element ids read when the panel was filled; after a model switch the Save wrote
        /// onto whatever element held that id.
        /// </summary>
        [Theory]
        [InlineData("Commands/Electrical/ElectricalPanelCommands.cs", "class ElecPanelWriteParamsCommand", "snap.DocKey")]
        [InlineData("UI/StingElectricalCommandHandler.cs", "private void ZoomToSelectedSld", "PanelShowsThisModel(doc)")]
        [InlineData("UI/StingElectricalCommandHandler.cs", "private void OpenScheduleForSelectedSld", "PanelShowsThisModel(doc)")]
        [InlineData("Commands/Electrical/ElectricalSnapshotBuilder.cs", "public static ElectricalPanelSnapshot Build", "snap.DocKey =")]
        public void Panel_row_actions_check_the_model_the_rows_came_from(string file, string anchor, string mustAppear)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", file.Replace('/', Path.DirectorySeparatorChar)));
            int at = src.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(at >= 0, anchor);
            Assert.Contains(mustAppear, src.Substring(at, Math.Min(1500, src.Length - at)));
        }

        /// <summary>The panel grid's caches: the assignment itself is scoped to this document.</summary>
        [Theory]
        [InlineData("snap.Feeders")]
        [InlineData("snap.FaultResults")]
        [InlineData("snap.ConduitFills")]
        [InlineData("snap.EmergAudit")]
        [InlineData("snap.LpdRows")]
        public void Panel_grid_caches_are_scoped_to_the_document(string assignment)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Commands", "Electrical", "ElectricalSnapshotBuilder.cs"));
            var m = new Regex(Regex.Escape(assignment) + @"\s*=(?<rhs>[^;]*);", RegexOptions.Singleline).Match(src);
            Assert.True(m.Success, assignment);
            Assert.True(m.Groups["rhs"].Value.Contains("Matches") || m.Groups["rhs"].Value.Contains("Here"),
                $"{assignment} is not scoped to the document: {m.Groups["rhs"].Value.Trim()}");
        }
    }
}
