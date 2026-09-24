using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// T-6 — title-block family resolution, table-tested against the real
    /// catalogue (STING_TITLE_BLOCKS.json) rather than a hand-kept list.
    /// </summary>
    public class TitleBlockFamilyNamingTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_TITLE_BLOCKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static readonly Lazy<HashSet<string>> Catalogue = new Lazy<HashSet<string>>(() =>
        {
            var root = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_TITLE_BLOCKS.json")));
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in root["families"] ?? new JArray())
                if (!(f.Value<bool?>("abstract") ?? false) && !string.IsNullOrWhiteSpace(f.Value<string>("id")))
                    set.Add(f.Value<string>("id"));
            return set;
        });

        private static TitleBlockResolution R(string declared, string paper, string orient = "Landscape", string mode = "BIM")
            => TitleBlockFamilyNaming.Resolve(declared, paper, orient, mode, Catalogue.Value);

        [Theory]
        [InlineData("STING_TB_SHEET_A1", "A1", "Landscape", "BIM",    "STING_TB_A1_BIM_v2.0")]
        [InlineData("STING_TB_SHEET_A1", "A1", "Portrait",  "BIM",    "STING_TB_A1_PORT_BIM_v2.0")]
        [InlineData("STING_TB_SHEET_A3", "A3", "Portrait",  "NONBIM", "STING_TB_A3_PORT_NONBIM_v2.0")]
        [InlineData("",                  "A0", "Landscape", "BIM",    "STING_TB_A0_BIM_v2.0")]
        [InlineData("STING_TB_SHEET",    "A2", "Landscape", "BIM",    "STING_TB_A2_BIM_v2.0")]   // was null → dangling name
        [InlineData("STING_TB_ASSEMBLY_PIPE", "A1", "Landscape", "BIM", "STING_TB_ASSEMBLY_PIPE_v1.0")]
        [InlineData("STING_TB_A1_BIM_v2.0", "A3", "Landscape", "BIM", "STING_TB_A1_BIM_v2.0")]   // already concrete
        [InlineData("STING - Healthcare Title Block A1", "A1", "Landscape", "BIM", "STING - Healthcare Title Block A1")]
        public void Resolves_cleanly(string declared, string paper, string orient, string mode, string expected)
        {
            var r = R(declared, paper, orient, mode);
            Assert.Equal(expected, r.Family);
            Assert.Empty(r.Warnings);
        }

        [Fact]
        public void Presentation_A1_uses_the_presentation_family()
        {
            var r = R("STING_TB_SHEET_A1_PRESENTATION", "A1");
            Assert.Equal("STING_TB_PRESENT_A1_v1.0", r.Family);
            Assert.Empty(r.Warnings);
        }

        [Fact]
        public void Presentation_keeps_the_paper_size_and_says_so()
        {
            // Was: any *PRESENT* name → STING_TB_PRESENT_A1_v1.0 regardless of paper.
            var r = R("STING_TB_SHEET_A3_PRESENTATION", "A3");
            Assert.Equal("STING_TB_A3_BIM_v2.0", r.Family);
            Assert.Contains(r.Warnings, w => w.Contains("no presentation title block exists at A3"));
        }

        [Fact]
        public void Blank_paper_is_not_silently_A1()
        {
            var r = R("", "");
            Assert.Null(r.Family);
            Assert.Contains(r.Warnings, w => w.Contains("paperSize is blank"));
        }

        [Fact]
        public void Unsupported_size_is_reported_not_passed_through()
        {
            var r = R("STING_TB_SHEET", "A4");
            Assert.Null(r.Family);
            Assert.Contains(r.Warnings, w => w.Contains("no STING title block for A4"));
        }

        [Fact]
        public void Name_and_paper_disagreement_is_reported()
        {
            var r = R("STING_TB_SHEET_A3", "A1");
            Assert.Equal("STING_TB_A3_BIM_v2.0", r.Family);
            Assert.Contains(r.Warnings, w => w.Contains("names A3 but paperSize is A1"));
        }

        [Fact]
        public void Every_shipped_STING_TB_profile_resolves_without_warnings()
        {
            var root = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var bad = new List<string>();
            int seen = 0;
            foreach (var dt in root["drawingTypes"] ?? new JArray())
            {
                var fam = dt.Value<string>("titleBlockFamily") ?? "";
                if (!fam.StartsWith("STING_TB_", StringComparison.OrdinalIgnoreCase) && fam.Length > 0) continue;
                seen++;
                var r = R(fam, dt.Value<string>("paperSize"), dt.Value<string>("orientation"));
                if (!r.IsResolved || r.Warnings.Count > 0)
                    bad.Add($"{dt["id"]}: {fam} -> {r.Family ?? "(unresolved)"} {string.Join(" | ", r.Warnings)}");
            }
            Assert.True(seen > 0);
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }
    }
}
