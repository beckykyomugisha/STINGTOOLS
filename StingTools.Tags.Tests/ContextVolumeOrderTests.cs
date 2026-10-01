using System;
using System.IO;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-141 — ApplyContextVolume runs on every token Build(). The scope-box
    /// collector, plan read and LOC index must only run once the pattern is known to use {vol}.</summary>
    public class ContextVolumeOrderTests
    {
        [Fact]
        public void PatternIsCheckedBeforeTheContextBoxIsResolved()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", "DrawingTokenContext.cs"));
            int method = src.IndexOf("public static string ApplyContextVolume(", StringComparison.Ordinal);
            Assert.True(method >= 0);
            int check = src.IndexOf("SheetNumberPolicy.PatternUsesVolume(", method, StringComparison.Ordinal);
            int resolve = src.IndexOf("ResolveContextLoc(doc, scopeBox, tag)", method, StringComparison.Ordinal);
            Assert.True(check > 0 && resolve > 0);
            Assert.True(check < resolve, "PatternUsesVolume must gate ResolveContextLoc");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "DrawingTokenContext.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            return dir.FullName;
        }
    }
}
