// The excludedCategories key lives in the SAME file as the presets, layered corporate →
// project, so a project overrides it through the path that already exists. Three things
// here are easy to get wrong and would each fail silently on a user's model:
//
//   · null (key absent) and [] (key present, empty) must mean different things;
//   · saving a preset must not delete a project's exclusions from the file it shares;
//   · the SHIPPED baseline must actually carry the key, not just describe it.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityExclusionStoreTests
    {
        private static string Temp(string json)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "sting_vis_excl_" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void BaselineExclusions_AreUsedWhenTheProjectFileSaysNothing()
        {
            string baseline = Temp("{ \"excludedCategories\": [ \"OST_Cameras\" ], \"presets\": [] }");
            string project = Temp("{ \"presets\": [] }");
            try
            {
                var list = VisibilityPresetStore.LoadExcludedCategories(baseline, project);
                Assert.Equal(new[] { "OST_Cameras" }, list.ToArray());
            }
            finally { File.Delete(baseline); File.Delete(project); }
        }

        [Fact]
        public void ProjectExclusions_ReplaceTheBaseline()
        {
            string baseline = Temp("{ \"excludedCategories\": [ \"OST_Cameras\" ], \"presets\": [] }");
            string project = Temp("{ \"excludedCategories\": [ \"OST_Grids\" ], \"presets\": [] }");
            try
            {
                var list = VisibilityPresetStore.LoadExcludedCategories(baseline, project);
                Assert.Equal(new[] { "OST_Grids" }, list.ToArray());
            }
            finally { File.Delete(baseline); File.Delete(project); }
        }

        [Fact]
        public void AnExplicitEmptyProjectList_MeansExcludeNothing()
        {
            // This is the whole reason ExcludedCategories defaults to null rather than to an
            // empty list: an empty default would make "[]" indistinguishable from "absent",
            // and the key would be impossible to override downward.
            string baseline = Temp("{ \"excludedCategories\": [ \"OST_Cameras\" ], \"presets\": [] }");
            string project = Temp("{ \"excludedCategories\": [], \"presets\": [] }");
            try
            {
                Assert.Empty(VisibilityPresetStore.LoadExcludedCategories(baseline, project));
            }
            finally { File.Delete(baseline); File.Delete(project); }
        }

        [Fact]
        public void NeitherFileDeclaringTheKey_FallsBackToTheShippedDefaults()
        {
            var list = VisibilityPresetStore.LoadExcludedCategories(null, null);
            Assert.Equal(VisibilityCategoryTreeBuilder.DefaultExclusions, list.ToArray());
        }

        [Fact]
        public void BlanksAndDuplicatesAreCleanedOut()
        {
            string baseline = Temp(
                "{ \"excludedCategories\": [ \"OST_Cameras\", \" \", \"ost_cameras\", \" OST_Views \" ], \"presets\": [] }");
            try
            {
                var list = VisibilityPresetStore.LoadExcludedCategories(baseline, null);
                Assert.Equal(new[] { "OST_Cameras", "OST_Views" }, list.ToArray());
            }
            finally { File.Delete(baseline); }
        }

        [Fact]
        public void SavingAPreset_DoesNotDeleteTheProjectsExclusions()
        {
            // Presets and exclusions share one file. Save() rewrites that file, so without an
            // explicit carry-over the first "Save preset…" would silently wipe the project's
            // category exclusions.
            string project = Temp("{ \"excludedCategories\": [ \"OST_Grids\" ], \"presets\": [] }");
            try
            {
                var sets = new List<VisibilitySet>
                {
                    new VisibilitySet { Name = "mine", Origin = "project" }
                };
                Assert.True(VisibilityPresetStore.Save(project, sets));

                var reread = VisibilityPresetStore.Parse(File.ReadAllText(project));
                Assert.NotNull(reread.ExcludedCategories);
                Assert.Equal(new[] { "OST_Grids" }, reread.ExcludedCategories.ToArray());
                Assert.Equal("mine", Assert.Single(reread.Presets).Name);
            }
            finally { File.Delete(project); }
        }

        [Fact]
        public void TheShippedBaselineActuallyCarriesTheKey()
        {
            // Round-trips the REAL file, not a copy — a typo in it fails here rather than on
            // a user's Friday.
            string path = Path.Combine(
                Path.GetDirectoryName(typeof(VisibilityExclusionStoreTests).Assembly.Location),
                "Data", VisibilityPresetStore.BaselineFileName);
            Assert.True(File.Exists(path), $"Shipped baseline not found at {path}");

            var lib = VisibilityPresetStore.Parse(File.ReadAllText(path));
            Assert.NotNull(lib.ExcludedCategories);
            foreach (var expected in VisibilityCategoryTreeBuilder.DefaultExclusions)
                Assert.Contains(expected, lib.ExcludedCategories);
        }
    }
}
