using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the seam between the two shipped data files.
    ///
    /// STING_SUPPLIER_UNITS.json and STING_MATERIAL_STAGES.json were each valid
    /// on their own and each covered by tests, yet disagreed: rules matching
    /// category "Walls" and "Floors" inherited those categories' stage, so wall
    /// paint and floor tiles routed to SUPERSTRUCTURE instead of FINISHES.
    /// Nothing compared the two files, so a green build shipped a schedule that
    /// filed finishes under the frame.
    ///
    /// These tests are that comparison.
    /// </summary>
    public class ShippedDataIntegrityTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(
                File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        [Fact]
        public void Every_Rule_Is_Reachable()
        {
            // A rule matching neither a constituent kind nor a category can never
            // fire. Dead config is worse than absent config: it advertises
            // coverage the export does not have.
            foreach (var r in Units().Rules)
                Assert.True(
                    (r.MatchKinds?.Count ?? 0) > 0 || (r.MatchCategories?.Count ?? 0) > 0,
                    $"commodity '{r.CommodityKey}' matches no kind and no category — it can never fire");
        }

        [Fact]
        public void Every_Category_Rule_Declares_Its_Own_Stage()
        {
            // Without an explicit stage the commodity silently inherits whatever
            // stage the ELEMENT's category routes to — which is how paint ended
            // up in the superstructure. Declaring it makes that impossible.
            foreach (var r in Units().Rules.Where(x => (x.MatchCategories?.Count ?? 0) > 0))
                Assert.False(string.IsNullOrWhiteSpace(r.StageId),
                    $"commodity '{r.CommodityKey}' matches by category but declares no stageId — "
                    + "it would inherit the element's stage");
        }

        [Fact]
        public void Every_Declared_Stage_Exists_In_The_Stage_Library()
        {
            var known = Stages().Stages.Select(s => s.StageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Units().Rules.Where(x => !string.IsNullOrWhiteSpace(x.StageId)))
                Assert.True(known.Contains(r.StageId),
                    $"commodity '{r.CommodityKey}' declares stage '{r.StageId}', which is not in the stage library");
        }

        [Fact]
        public void No_Finish_Commodity_Is_Filed_Under_The_Frame()
        {
            // The specific regression: a commodity whose name says "finish" must
            // not sit in a structural stage.
            var structural = new[] { "substructure", "superstructure" };
            foreach (var r in Units().Rules.Where(x =>
                         x.CommodityKey.Contains("paint") || x.CommodityKey.Contains("tile")))
            {
                if (string.IsNullOrWhiteSpace(r.StageId)) continue;
                Assert.DoesNotContain(r.StageId, structural, StringComparer.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Every_Commodity_Rule_Has_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out var skipped);
            Assert.Empty(skipped);

            var resolver = new CommodityRateResolver(rates, null);
            foreach (var r in Units().Rules)
                Assert.True(resolver.Resolve(r.CommodityKey).RateUGX > 0,
                    $"commodity '{r.CommodityKey}' is measurable but has no baseline rate");
        }

        [Fact]
        public void No_Rate_Is_Orphaned()
        {
            // A rate with no rule is unreachable in the other direction — it
            // suggests a commodity the schedule can price but can never produce.
            var table = Units();
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out _);

            foreach (var rate in rates)
                Assert.True(table.ResolveByCommodityKey(rate.CommodityKey) != null,
                    $"rate '{rate.CommodityKey}' has no supplier-unit rule — nothing can ever produce it");
        }
    }
}
