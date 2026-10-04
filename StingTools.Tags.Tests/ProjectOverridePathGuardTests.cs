using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ACC-3. Project overrides live in the consolidated coordination folder
    /// (<root>/_data/coord) on any project set up since consolidation; ten readers built the raw
    /// legacy path <rvtDir>/_BIM_COORD/... by hand and so never saw them. ClassificationStandard
    /// was the visible case: Set wrote the consolidated folder, Load read the raw one, and a chosen
    /// standard reverted on reopen. Every reader must resolve through
    /// ProjectFolderEngine.ResolveProjectOverridePath or StingPaths.MetaFile, which check both.
    /// </summary>
    public class ProjectOverridePathGuardTests
    {
        // Path.Combine(projDir, SomeRelConst) or Path.Combine(Path.GetDirectoryName(doc.PathName) ..., SomeRelConst)
        private static readonly Regex RawJoin = new Regex(
            @"Path\.Combine\(\s*(?:projDir|Path\.GetDirectoryName\(\s*doc\.PathName\s*\)[^,]*)\s*,\s*(?<c>\w*Rel\w*)\s*\)");

        [Fact]
        public void DetectorCatchesTheOriginalShape()
        {
            Assert.Matches(RawJoin, "string path = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? \"\", FileRel);");
            Assert.Matches(RawJoin, "string projPath = Path.Combine(projDir, ProjectOverrideRelPath);");
            Assert.DoesNotMatch(RawJoin, "ProjectFolderEngine.ResolveProjectOverridePath(doc, ProjectOverrideRelPath)");
        }

        [Fact]
        public void NoReaderBuildsTheRawBimCoordPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var sep = Path.DirectorySeparatorChar;
            var bad = Directory.EnumerateFiles(Path.Combine(dir.FullName, "StingTools"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(sep + "obj" + sep) && !p.Contains(sep + "bin" + sep))
                .SelectMany(p => File.ReadAllLines(p).Select((l, i) => (p, i, l)))
                .Where(t => RawJoin.IsMatch(t.l))
                .Select(t => $"{Path.GetFileName(t.p)}:{t.i + 1}")
                .ToList();
            Assert.True(bad.Count == 0, "raw <rvtDir>/_BIM_COORD override reads:\n" + string.Join("\n", bad));
        }
    }
}
