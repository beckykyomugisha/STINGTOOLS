using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ROADMAP ELEC-29: the plugin compiled against Revit 2025 only. Revit 2026 renamed
    /// UNIFORMAT_* / OMNICLASS_* and a Rebar argument; 2027 removed Curve.Intersect(out),
    /// Zone.Volume and ANALYTICAL_HEAT_TRANSFER_COEFFICIENT and ships a .NET 10 API. Naming any
    /// of those directly breaks one of the three builds, so the source must go through
    /// BipCompat / CurveCompat (or a version #if).
    /// </summary>
    public class RevitVersionCompatTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        [Fact]
        public void No_source_names_a_version_specific_Revit_member()
        {
            var bad = new Regex(
                @"BuiltInParameter\.(OMNICLASS_|UNIFORMAT_|ANALYTICAL_HEAT_TRANSFER_COEFFICIENT|CLASSIFICATION_CODE|CLASSIFICATION_DESCRIPTION|ASSEMBLY_CODE|ASSEMBLY_DESCRIPTION)"
              + @"|\.Intersect\([^;]*,\s*out\b|\bsuppressHooks(AndCranks)?\s*:|\bzone\.Volume\b");
            var src = Path.Combine(Root(), "StingTools");
            var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.EndsWith("CurveCompat.cs") && !f.EndsWith("BipCompat.cs"))
                .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, l, i)))
                .Where(x => !x.l.TrimStart().StartsWith("//") && bad.IsMatch(x.l))
                .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.i + 1}: {x.l.Trim()}")
                .ToList();
            Assert.True(hits.Count == 0, "Version-specific Revit API named directly:\n" + string.Join("\n", hits));
        }

        [Fact]
        public void Renamed_built_ins_list_the_newer_name_first_and_keep_the_2025_name()
        {
            Assert.Equal(new[] { "ASSEMBLY_CODE", "UNIFORMAT_CODE" }, BipNames.AssemblyCode);
            Assert.Equal(new[] { "ASSEMBLY_DESCRIPTION", "UNIFORMAT_DESCRIPTION" }, BipNames.AssemblyDescription);
            Assert.Equal(new[] { "CLASSIFICATION_CODE", "OMNICLASS_CODE" }, BipNames.ClassificationCode);
            Assert.Equal(new[] { "CLASSIFICATION_DESCRIPTION", "OMNICLASS_DESCRIPTION" }, BipNames.ClassificationDescription);
        }

        [Fact]
        public void The_project_switches_framework_and_defines_version_symbols_by_Revit_year()
        {
            string proj = File.ReadAllText(Path.Combine(Root(), "StingTools", "StingTools.csproj"));
            Assert.Contains("<TargetFramework Condition=\"'$(RevitYear)' == '2027'\">net10.0-windows</TargetFramework>", proj);
            Assert.Contains("REVIT2026_OR_GREATER", proj);
            Assert.Contains("REVIT2027_OR_GREATER", proj);
        }
    }
}
