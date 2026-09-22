// Spare-capacity targets are per-sector, and there is now one place that says
// what they are.
//
// The answer used to live in two places that disagreed: LoadDemandEngine read
// spareTargetsPct (Commercial 25 ... Healthcare 35), while the
// WARN_ELC_PNL_SPARE_WAYS tag warning used a flat 20. Flat 20 matches exactly
// one sector, Residential - so on a hospital the warning stayed quiet at 22%
// spare against a 35% target, silent precisely where the requirement is
// strictest.
//
// A third number, 10%, was found filed against a test clamp and deleted as
// misfiled on 2026-09-22; finding it is what surfaced the other two.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SpareCapacityTargetTests
    {
        private static Dictionary<string, double> Table() => new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Commercial"] = 25, ["Industrial"] = 30, ["Residential"] = 20,
            ["Healthcare"] = 35, ["Education"] = 25, ["Retail"] = 25,
        };

        // ── the number ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("Healthcare", 35)]
        [InlineData("Industrial", 30)]
        [InlineData("Commercial", 25)]
        [InlineData("Residential", 20)]
        [InlineData("healthcare", 35)]           // sector names are not case-sensitive
        public void EachSectorGetsItsOwnTarget(string sector, double expected)
            => Assert.Equal(expected, SpareCapacityTarget.TargetPct(sector, Table()));

        [Fact]
        public void TheOldFlatTwentyOnlyEverMatchedResidential()
        {
            // The whole reason for the change, stated as a test so it cannot be
            // quietly undone by someone "simplifying" the table back to one
            // number.
            var off = Table().Where(kv => Math.Abs(kv.Value - 20) > 0.01)
                             .Select(kv => kv.Key).OrderBy(k => k).ToList();

            Assert.Equal(new[] { "Commercial", "Education", "Healthcare", "Industrial", "Retail" }, off);
        }

        [Theory]
        [InlineData("Datacentre")]               // a real sector nobody has written a target for
        [InlineData("")]
        [InlineData(null)]
        public void AnUnknownSectorFallsBackToTheDefaultAndNotToAnotherSector(string sector)
        {
            // It must not silently take Healthcare's 35 or Residential's 20 -
            // either would be a specific claim about a building nobody
            // classified.
            Assert.Equal(Table()[SpareCapacityTarget.DefaultSector],
                         SpareCapacityTarget.TargetPct(sector, Table()));
        }

        [Fact]
        public void AMissingTableFallsBackToTheDefaultSectorsNumber()
        {
            // 25 is Commercial's value, so a lost data file degrades to the
            // default SECTOR rather than to a number found nowhere in the
            // standards.
            Assert.Equal(SpareCapacityTarget.FallbackPct,
                         SpareCapacityTarget.TargetPct("Healthcare", null));
            Assert.Equal(SpareCapacityTarget.FallbackPct,
                         SpareCapacityTarget.TargetPct("Healthcare", new Dictionary<string, double>()));
            Assert.Equal(25, SpareCapacityTarget.FallbackPct);
        }

        // ── the classification ───────────────────────────────────────────────

        [Theory]
        [InlineData("Kampala Regional Hospital", "Healthcare")]
        [InlineData("Outpatients Clinic Block B", "Healthcare")]
        [InlineData("St Mary's Primary School", "Education")]
        [InlineData("Distribution Warehouse 4", "Industrial")]
        [InlineData("Riverside Housing Phase 2", "Residential")]
        [InlineData("Garden City Shopping Mall", "Retail")]
        [InlineData("Office Fit-Out Level 3", "Commercial")]
        [InlineData("", "Commercial")]
        [InlineData(null, "Commercial")]
        public void SectorIsReadFromTheProjectText(string text, string expected)
            => Assert.Equal(expected, SpareCapacityTarget.SectorFromText(text));

        [Fact]
        public void AUniversityHospitalIsHealthcareNotEducation()
        {
            // Order matters, and this is the case that decides it: the
            // electrical requirement follows the clinical function, and getting
            // it wrong costs 10 percentage points of spare capacity.
            Assert.Equal("Healthcare", SpareCapacityTarget.SectorFromText("University Teaching Hospital"));
            Assert.Equal(35, SpareCapacityTarget.TargetPct(
                SpareCapacityTarget.SectorFromText("University Teaching Hospital"), Table()));
        }

        // ── the shipped data ─────────────────────────────────────────────────

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        [Fact]
        public void TheShippedTableCoversEverySectorTheClassifierCanReturn()
        {
            // A classifier that returns a sector the table has no row for sends
            // every such project to the fallback, silently. The two halves have
            // to agree, and nothing but this checks that they do.
            string p = Path.Combine(RepoRoot().FullName, "StingTools", "Data", "STING_DIVERSITY_FACTORS.json");
            var root = JObject.Parse(File.ReadAllText(p));
            var shipped = ((JObject)root["spareTargetsPct"]).Properties().Select(x => x.Name).ToList();

            var reachable = new[]
            {
                "Kampala Hospital", "City College", "Steel Factory",
                "Lakeside Housing", "Retail Park", "Head Office",
            }.Select(SpareCapacityTarget.SectorFromText).Distinct().ToList();

            var missing = reachable.Where(s => !shipped.Contains(s, StringComparer.OrdinalIgnoreCase))
                                   .OrderBy(s => s).ToList();

            Assert.True(missing.Count == 0,
                "the classifier can return sector(s) the shipped table has no target for, so those " +
                "projects fall back silently: " + string.Join(", ", missing));
        }

        [Fact]
        public void TheWarningIsDeclaredToUseTheTable()
        {
            // Without threshold_table the evaluator keeps the fixed 20 and the
            // whole change is inert - a green build and no behaviour.
            string p = Path.Combine(RepoRoot().FullName, "StingTools", "Data", "PARAMETER_REGISTRY.json");
            var root = JObject.Parse(File.ReadAllText(p));

            var def = ((JArray)root["warning_thresholds"])
                .OfType<JObject>()
                .FirstOrDefault(w => (string)w["param_name"] == "WARN_ELC_PNL_SPARE_WAYS");

            Assert.True(def != null, "WARN_ELC_PNL_SPARE_WAYS has no warning_thresholds entry");
            Assert.Equal("spareTargetsPct", (string)def["threshold_table"]);

            // The fixed threshold stays as the fallback: a table that cannot be
            // read must degrade to the old check, not to no check.
            Assert.Equal("20", (string)def["threshold"]);

            // "minimum" is what makes EvaluateWarning fire when the value is
            // BELOW the threshold. Lose that word and the check inverts - it
            // would warn about panels with plenty of spare and stay quiet about
            // full ones.
            Assert.Contains("minimum", (string)def["description"], StringComparison.OrdinalIgnoreCase);
        }
    }
}
