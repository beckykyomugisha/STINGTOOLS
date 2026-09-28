// V-11 — ApplyWorksetVisibility warned "document is not workshared — skipped"
// BEFORE checking whether the pack asked for anything, so every apply on every
// non-workshared project raised a warning for a setting no shipped pack uses.
// The decision now lives in the Revit-free WorksetVisibilityPlan.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WorksetVisibilityPlanTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NoModeMeansNoActionAndNoWarning_EvenWhenNotWorkshared(string mode)
        {
            Assert.Equal(WorksetVisibilityAction.None, WorksetVisibilityPlan.Decide(mode, isWorkshared: false));
            Assert.Equal(WorksetVisibilityAction.None, WorksetVisibilityPlan.Decide(mode, isWorkshared: true));
        }

        [Fact]
        public void StatedModeOnNonWorksharedDocWarns()
            => Assert.Equal(WorksetVisibilityAction.WarnNotWorkshared, WorksetVisibilityPlan.Decide("HideAll", isWorkshared: false));

        [Fact]
        public void StatedModeOnWorksharedDocApplies()
        {
            Assert.Equal(WorksetVisibilityAction.Apply, WorksetVisibilityPlan.Decide("ShowAll", isWorkshared: true));
            Assert.True(WorksetVisibilityPlan.Hides(" hideall "));
            Assert.False(WorksetVisibilityPlan.Hides("ShowAll"));
        }

        [Fact]
        public void NoShippedPackWarnsAboutWorksetsOnANonWorksharedProject()
        {
            // The measurement behind V-11, kept as a gate: with the shipped
            // library, a non-workshared project must see zero workset warnings.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(
                File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            Assert.True(lib?.Packs != null && lib.Packs.Count > 30, "shipped pack library did not deserialise");

            var noisy = lib.Packs
                .Where(p => WorksetVisibilityPlan.Decide(p.WorksetVisibility, isWorkshared: false) != WorksetVisibilityAction.None)
                .Select(p => p.Id).ToList();
            Assert.True(noisy.Count == 0,
                "packs that state worksetVisibility (fine if intended - then update this gate): " + string.Join(", ", noisy));
        }
    }
}
