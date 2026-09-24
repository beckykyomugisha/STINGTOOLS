// Viewport naming — the drawing-type catalogue and the sheet manager's
// viewport-type rules must ask for the same names.
//
// At 9775cb213 all 93 drawing types said "STING - Standard Viewport" while
// SheetManagerEngineExt.DefaultViewportTypeRules asked for "STING Viewport",
// "STING Section Viewport", ... and nothing created either. StingViewportTypes
// is now the one list; these tests hold both consumers to it.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ViewportTypeNamingTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        [Fact]
        public void EveryCatalogueViewportTypeNameIsCanonical()
        {
            var lib = JObject.Parse(File.ReadAllText(Path.Combine(Root(), "StingTools", "Data", "STING_DRAWING_TYPES.json")));
            var types = (JArray)lib["drawingTypes"];
            Assert.True(types != null && types.Count > 50, "catalogue did not parse");
            var bad = types
                .Select(t => new { id = (string)t["id"], vp = (string)t["viewportTypeName"] })
                .Where(x => !string.IsNullOrWhiteSpace(x.vp) && !StingViewportTypes.IsCanonical(x.vp))
                .Select(x => $"{x.id}: '{x.vp}'").ToList();
            Assert.True(bad.Count == 0, "non-canonical viewportTypeName:\n  " + string.Join("\n  ", bad));
        }

        [Fact]
        public void EverySlotViewportTypeInTheCatalogueIsCanonical()
        {
            var lib = JObject.Parse(File.ReadAllText(Path.Combine(Root(), "StingTools", "Data", "STING_DRAWING_TYPES.json")));
            var bad = lib.Descendants().OfType<JProperty>()
                .Where(p => p.Name == "viewportType" && p.Value.Type == JTokenType.String)
                .Select(p => (string)p.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v) && v.StartsWith("STING", StringComparison.OrdinalIgnoreCase)
                            && !StingViewportTypes.IsCanonical(v))
                .Distinct().ToList();
            Assert.True(bad.Count == 0, "non-canonical STING slot viewportType: " + string.Join(", ", bad));
        }

        [Fact]
        public void SheetManagerRulesTargetCanonicalNames_AndPlansUseTheCatalogueName()
        {
            Assert.All(StingViewportTypes.ByViewType, kv => Assert.True(StingViewportTypes.IsCanonical(kv.Value), kv.Value));
            // Plans are what the catalogue's 93 types name; the rule for a plan
            // must land on the same type or the two surfaces style differently.
            Assert.Equal(StingViewportTypes.Standard, StingViewportTypes.ByViewType.First(kv => kv.Key == "FloorPlan").Value);
        }

        [Fact]
        public void SheetManagerSourceHasNoHardCodedStingViewportName()
        {
            // The rules must be built from StingViewportTypes, not re-typed.
            var src = File.ReadAllText(Path.Combine(Root(), "StingTools", "Docs", "SheetManagerEngineExt.cs"));
            var literals = Regex.Matches(src, "\"STING[^\"]*Viewport\"").Select(m => m.Value).ToList();
            Assert.True(literals.Count == 0, "hard-coded viewport names in SheetManagerEngineExt.cs: " + string.Join(", ", literals));
        }

        [Theory]
        [InlineData("STING Viewport", StingViewportTypes.Standard)]
        [InlineData("sting section viewport", StingViewportTypes.Section)]
        [InlineData("STING - 3D Viewport", StingViewportTypes.ThreeD)]
        [InlineData("Title w Line", null)]
        public void LegacyNamesMapToCanonical(string name, string canonical)
            => Assert.Equal(canonical, StingViewportTypes.CanonicalFor(name));

        [Fact]
        public void CandidatesTryTheRequestedNameFirstAndNeverSwapAUserName()
        {
            var c = StingViewportTypes.Candidates(StingViewportTypes.Section);
            Assert.Equal(StingViewportTypes.Section, c[0]);
            Assert.Contains("STING Section Viewport", c);
            Assert.Equal(new[] { "Title w Line" }, StingViewportTypes.Candidates("Title w Line"));
            var legacy = StingViewportTypes.Candidates("STING Viewport");
            Assert.Equal("STING Viewport", legacy[0]);           // an existing legacy type is used as-is
            Assert.Equal(StingViewportTypes.Standard, legacy[1]);
        }

        [Fact]
        public void EveryCanonicalNameHasALegacyAliasOrIsNew()
        {
            // Every alias must point at a canonical name (no dangling alias).
            Assert.All(StingViewportTypes.LegacyAliases, kv => Assert.True(StingViewportTypes.IsCanonical(kv.Value), kv.Key));
        }
    }
}
