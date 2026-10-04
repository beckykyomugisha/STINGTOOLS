using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The KUT owner pack deploys project_config.json in _BIM_COORD/, and before
    /// ProjectConfigLocator nothing read it there: the tagger ran on built-in defaults
    /// (BLD1–BLD3 + EXT) and the smoke test still passed, because it only checked the
    /// file was present.
    /// </summary>
    public class ProjectConfigLocatorTests
    {
        private const string Beside = @"C:\KUT\project_config.json";
        private const string Overlay = @"C:\KUT\_BIM_COORD\project_config.json";

        private static Func<string, bool> Exists(params string[] present)
        {
            var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
            return p => set.Contains(p);
        }

        [Fact]
        public void AnOverlayAloneIsLoaded()
        {
            var c = ProjectConfigLocator.Choose(Beside, Overlay, Exists(Overlay));
            Assert.Equal(Overlay, c.Path);
            Assert.Contains("_BIM_COORD", c.Source);
            Assert.Null(c.Shadowed);
        }

        [Fact]
        public void TheCopyBesideTheModelWinsAndTheOverlayIsReportedShadowed()
        {
            var c = ProjectConfigLocator.Choose(Beside, Overlay, Exists(Beside, Overlay));
            Assert.Equal(Beside, c.Path);
            Assert.Equal(Overlay, c.Shadowed);
        }

        [Fact]
        public void BesideTheModelAloneIsLoadedWithNothingShadowed()
        {
            var c = ProjectConfigLocator.Choose(Beside, Overlay, Exists(Beside));
            Assert.Equal(Beside, c.Path);
            Assert.Null(c.Shadowed);
        }

        [Fact]
        public void NeitherMeansNoProjectConfig()
        {
            var c = ProjectConfigLocator.Choose(Beside, Overlay, Exists());
            Assert.Null(c.Path);
            Assert.Null(c.Source);
        }

        [Fact]
        public void AMissingOverlayCandidateIsNotAnError()
        {
            // ResolveProjectOverridePath returns null when neither overlay location exists.
            var c = ProjectConfigLocator.Choose(Beside, null, Exists(Beside));
            Assert.Equal(Beside, c.Path);
            Assert.Null(c.Shadowed);
        }

        /// <summary>The overlay the locator now reads must actually carry the six
        /// buildings — otherwise reading it changes nothing.</summary>
        [Fact]
        public void TheShippedKutOverlayCarriesAllSixBuildings()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "project-templates", "KUT")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate project-templates/KUT");
            string path = Path.Combine(dir.FullName, "project-templates", "KUT",
                                       ProjectConfigLocator.OverlayRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "the KUT pack no longer ships " + ProjectConfigLocator.OverlayRelativePath);
            var loc = JObject.Parse(File.ReadAllText(path))["LOC_CODES"].Select(t => (string)t).ToList();
            foreach (var code in new[] { "BLD1", "BLD2", "BLD3", "BLD4", "BLD5", "BLD6", "EXT" })
                Assert.Contains(code, loc);
        }
    }
}
