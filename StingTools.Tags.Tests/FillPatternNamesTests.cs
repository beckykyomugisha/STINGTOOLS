// DTW-165 — pattern names in the shipped filter / pack data must resolve.
//
// "Solid fill" never matched Revit's "<Solid fill>", and the hatch names the
// data uses were created by nothing STING runs. These tests pin solid-by-meaning,
// the colour-implies-solid rule, and that every hatch name the data uses can
// land on a pattern STING's own CreateFillPatterns makes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class FillPatternNamesTests
    {
        /// <summary>
        /// Data pattern names with no STING-created equivalent. They resolve only in a
        /// project (or template) that has a pattern of that name; a miss is warned.
        /// </summary>
        private static readonly Dictionary<string, string> ProjectOnly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vegetation"] = "Landscape hatch; no STING pattern. Revit's default templates do not ship one either.",
            ["Wood - End"] = "End-grain model pattern; tried as Revit's 'Wood 1'. No STING pattern.",
        };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_AEC_FILTERS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        [Theory]
        [InlineData("Solid fill")]
        [InlineData("<Solid fill>")]
        [InlineData("solid")]
        [InlineData(" <SOLID FILL> ")]
        public void SolidIsRecognisedHoweverItIsSpelt(string name) => Assert.True(FillPatternNames.IsSolidName(name));

        [Theory]
        [InlineData("Crosshatch")]
        [InlineData("Solid line")]
        [InlineData("")]
        [InlineData(null)]
        public void OtherNamesAreNotSolid(string name) => Assert.False(FillPatternNames.IsSolidName(name));

        [Fact]
        public void AColourWithNoPatternMeansSolid()
        {
            Assert.True(FillPatternNames.IsSolidName(FillPatternNames.EffectivePattern(null, "#FF0000")));
            Assert.Equal("Concrete", FillPatternNames.EffectivePattern("Concrete", "#FF0000"));
            Assert.Null(FillPatternNames.EffectivePattern(null, null));
        }

        [Fact]
        public void TheNameAsWrittenIsTriedFirst()
            => Assert.Equal("Concrete", FillPatternNames.Candidates("Concrete").First());

        [Fact]
        public void EveryShippedHatchNameCanLandOnAStingPattern()
        {
            var root = RepoRoot();
            var src = File.ReadAllText(Path.Combine(root, "StingTools", "Temp", "TemplateManagerCommands.cs"));
            var created = new HashSet<string>(
                Regex.Matches(src, "\\(\"(STING - [^\"]+)\", FillPatternTarget\\.").Cast<Match>().Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);
            Assert.True(created.Count >= 10, "could not read TemplateManager.FillPatternDefs");

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filters = JObject.Parse(File.ReadAllText(Path.Combine(root, "StingTools", "Data", "STING_AEC_FILTERS.json")));
            foreach (var o in filters["filters"].Select(f => f["override"]).OfType<JObject>())
                foreach (var p in o.Properties().Where(p => p.Name.EndsWith("Pattern") && !p.Name.Contains("Line")))
                    used.Add((string)p.Value);
            var packs = JObject.Parse(File.ReadAllText(Path.Combine(root, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            foreach (var r in packs.Descendants().OfType<JProperty>()
                         .Where(p => (p.Name.EndsWith("FgPattern") || p.Name.EndsWith("BgPattern")) && p.Value.Type == JTokenType.String))
                used.Add((string)r.Value);

            var unresolved = used
                .Where(n => !FillPatternNames.IsSolidName(n) && !ProjectOnly.ContainsKey(n))
                .Where(n => !FillPatternNames.Candidates(n).Any(created.Contains))
                .ToList();
            Assert.True(unresolved.Count == 0,
                "Pattern names with no STING-created equivalent (add an alias or a ProjectOnly reason): " + string.Join(", ", unresolved));
        }
    }
}
