using System.IO;
using System;
using StingTools.UI;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-2 — the corrected sand/cement/aggregate/mortar/plaster/screed ratios in
    /// the shipped MATERIAL_LOOKUP.csv. Runs against the real file (copied to the
    /// test output) through the same MaterialLookupParser the runtime uses.
    /// </summary>
    public class MaterialRatioMat2Tests
    {
        // bag = 50 kg; cement loose density 1440 kg/m³ → cement volume per bag.
        private const double CementVolPerBag = 50.0 / 1440.0; // 0.034722 m³

        private static System.Collections.Generic.Dictionary<string, MaterialLookupRow> Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "MATERIAL_LOOKUP.csv");
            return MaterialLookupParser.Parse(File.ReadAllLines(path));
        }

        private static double P(MaterialLookupRow r, string prop)
        {
            Assert.NotNull(r);
            Assert.NotNull(r.Properties);
            Assert.True(r.Properties.ContainsKey(prop), $"missing property {prop}");
            return r.Properties[prop];
        }

        [Theory]
        // mix N, cement bags, expected sand ≈ N × bags × 0.034722
        [InlineData("1:3", 12, 1.25)]
        [InlineData("1:4", 9, 1.25)]
        [InlineData("1:5", 7.5, 1.30)]
        [InlineData("1:6", 6, 1.25)]
        [InlineData("1:8", 4.5, 1.25)]
        public void Mortar_Sand_Matches_True_Mix_Ratio(string mix, double bags, double approxSand)
        {
            var r = Load()[$"MORTAR {mix}"];
            int n = int.Parse(mix.Split(':')[1]);
            double expected = n * bags * CementVolPerBag;
            Assert.Equal(expected, P(r, "SAND_RATIO"), 2);
            Assert.Equal(approxSand, P(r, "SAND_RATIO"), 1);
        }

        [Fact]
        public void Mortar_1to6_Sand_Is_Corrected_Not_Old_0_86()
        {
            double sand = P(Load()["MORTAR 1:6"], "SAND_RATIO");
            Assert.True(sand > 1.2 && sand < 1.3, $"expected ~1.25, got {sand}");
            Assert.NotEqual(0.86, sand, 2);
        }

        [Theory]
        [InlineData("CONCRETE C35")]
        [InlineData("CONCRETE C40")]
        [InlineData("CONCRETE C45")]
        public void Rich_Concrete_Dry_Volume_Is_About_1_54(string key)
        {
            var r = Load()[key];
            double cementVol = P(r, "CEMENT_BAGS_PER_M3") * CementVolPerBag;
            double dry = cementVol + P(r, "SAND_RATIO") + P(r, "AGGREGATE_RATIO");
            Assert.True(dry >= 1.52 && dry <= 1.57, $"{key} dry volume {dry:F3} not in 1.52-1.57");
        }

        [Fact]
        public void Concrete_C45_Dry_Volume_Is_Approximately_1_54()
        {
            var r = Load()["CONCRETE C45"];
            double dry = P(r, "CEMENT_BAGS_PER_M3") * CementVolPerBag + P(r, "SAND_RATIO") + P(r, "AGGREGATE_RATIO");
            Assert.Equal(1.54, dry, 1);
        }

        [Fact]
        public void Plaster_Cement_And_Sand_Are_Derivable()
        {
            var r = Load()["PLASTER STANDARD"];
            Assert.True(P(r, "MIX_CEMENT_BAGS_PER_M3") > 0);
            Assert.True(P(r, "MIX_SAND_RATIO") > 0);
        }

        [Fact]
        public void Screed_Section_Exists_With_Mix()
        {
            var r = Load()["SCREED STANDARD"];
            Assert.True(P(r, "MIX_CEMENT_BAGS_PER_M3") > 0);
            Assert.True(P(r, "MIX_SAND_RATIO") > 0);
            Assert.True(P(r, "THICKNESS_M") > 0);
        }

        [Fact]
        public void One_Brick_Bonds_Have_Doubled_Mortar()
        {
            var d = Load();
            Assert.True(P(d["BRICK_BOND HEADER"], "MORTAR_RATIO") >= 0.05);
            Assert.True(P(d["BRICK_BOND ENGLISH"], "MORTAR_RATIO") >= 0.045);
            Assert.True(P(d["BRICK_BOND FLEMISH"], "MORTAR_RATIO") >= 0.05);
            // Half-brick stretcher unchanged.
            Assert.Equal(0.025, P(d["BRICK_BOND STRETCHER"], "MORTAR_RATIO"), 3);
        }

        [Fact]
        public void Lean_Concrete_Grades_Added()
        {
            var d = Load();
            Assert.True(d.ContainsKey("CONCRETE C10"));
            Assert.True(d.ContainsKey("CONCRETE C7.5"));
            Assert.True(P(d["CONCRETE C10"], "CEMENT_BAGS_PER_M3") > 0);
            Assert.Equal(0, P(d["CONCRETE C10"], "STEEL_KG_PER_M3"), 3); // unreinforced
        }
    }
}
