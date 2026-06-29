// StingTools — Building-use coverage guard (WS K4).
//
// For each catalog building use, reports whether a load profile, DHW value, water
// profile, and baseline (exact or nearest) exist — so a gap reads as a VISIBLE
// fallback, never a silent office substitution. Wired two ways: a unit test that
// fails if any use lacks load + DHW + water, and dashboard NOTES lines.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;

namespace StingTools.Core.Sustainability
{
    public class UseCoverageRow
    {
        public string Use            { get; set; } = "";
        public string LoadProfileId  { get; set; } = "";
        public bool   LoadExact      { get; set; }   // direct/alias/loose (not a fallback)
        public bool   HasDhw         { get; set; }
        public bool   WaterExact     { get; set; }
        public string BaselineKey    { get; set; } = "";
        public bool   BaselineExact  { get; set; }

        /// <summary>The hard requirement: the use resolves its OWN load profile
        /// (direct/alias/loose, not a fallback) carrying a DHW value, plus an exact
        /// water profile. The baseline may legitimately resolve by nearest-zone/global
        /// fallback (logged).</summary>
        public bool IsCovered => LoadExact && HasDhw && WaterExact;
        /// <summary>Surfaces (load / water / baseline) that resolved by fallback.</summary>
        public List<string> Fallbacks { get; } = new List<string>();
    }

    public class CoverageReport
    {
        public List<UseCoverageRow> Rows { get; } = new List<UseCoverageRow>();
        public IEnumerable<UseCoverageRow> Gaps => Rows.Where(r => !r.IsCovered);
        public bool AllCovered => Rows.All(r => r.IsCovered);
    }

    public static class BuildingUseCoverage
    {
        /// <summary>Check every use against the load/water/baseline registries.
        /// <paramref name="climateZone"/> only affects baseline exactness reporting.</summary>
        public static CoverageReport Check(
            IEnumerable<string> uses,
            LoadProfileLibrary profiles,
            WaterUsageProfileRegistry water,
            GreenBaselineRegistry baselines,
            string climateZone = "*")
        {
            var report = new CoverageReport();
            foreach (var use in uses ?? Enumerable.Empty<string>())
            {
                var row = new UseCoverageRow { Use = use };

                var pr = profiles?.ResolveForUse(use);
                if (pr?.Profile != null)
                {
                    row.LoadProfileId = pr.Profile.Id;
                    row.LoadExact = !pr.IsFallback;            // resolved its own profile
                    row.HasDhw = pr.Profile.DhwLPerPersonDay >= 0;   // DHW carried by the profile
                    if (pr.IsFallback) row.Fallbacks.Add($"load profile ({pr.FromTo})");
                }

                row.WaterExact = water?.Has(use) ?? false;
                if (!row.WaterExact) row.Fallbacks.Add($"water ({use} → office)");

                if (baselines != null)
                {
                    var br = baselines.Resolve("*", climateZone, use);
                    row.BaselineKey = br.MatchedKey;
                    row.BaselineExact = br.ExactMatch;
                    if (!br.ExactMatch) row.Fallbacks.Add($"baseline ({use} → {br.MatchedKey})");
                }

                report.Rows.Add(row);
            }
            return report;
        }
    }
}
