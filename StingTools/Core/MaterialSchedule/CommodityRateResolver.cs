// ══════════════════════════════════════════════════════════════════════════
//  CommodityRateResolver.cs — MAT-SCHED commodity price list.
//
//  WHY THIS EXISTS: the BOQ's IRateProvider chain is element-scoped
//  (RateRequest.Element) and both shipped rate CSVs key on Revit CATEGORY, so
//  nothing in the codebase can price "one bag of cement". Constituent rows
//  currently resolve to (0, "None", 20) for every constituent.
//
//  An unpriced commodity stays visibly unpriced. Borrowing a neighbouring rate
//  would put a confident-looking number in a tender document.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class CommodityRate
    {
        public string CommodityKey = "";
        public string SupplierUnit = "";
        public double RateUGX;
        public string Source = "";      // "baseline" / "project" / "unpriced"
    }

    public sealed class CommodityRateResolver
    {
        private readonly Dictionary<string, CommodityRate> _baseline =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CommodityRate> _project =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unpriced =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CommodityRateResolver(IEnumerable<CommodityRate> baseline,
                                     IEnumerable<CommodityRate> projectOverrides)
        {
            foreach (var r in baseline ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _baseline[r.CommodityKey] = r;
            foreach (var r in projectOverrides ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _project[r.CommodityKey] = r;
        }

        /// <summary>Commodity keys asked for but not priced. Drives the export gate.</summary>
        public IReadOnlyCollection<string> UnpricedKeys => _unpriced;

        public CommodityRate Resolve(string commodityKey)
        {
            if (string.IsNullOrWhiteSpace(commodityKey))
                return new CommodityRate { CommodityKey = "", RateUGX = 0, Source = "unpriced" };

            if (_project.TryGetValue(commodityKey, out var p) && p.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = p.CommodityKey, SupplierUnit = p.SupplierUnit,
                    RateUGX = p.RateUGX, Source = "project"
                };

            if (_baseline.TryGetValue(commodityKey, out var b) && b.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = b.CommodityKey, SupplierUnit = b.SupplierUnit,
                    RateUGX = b.RateUGX, Source = "baseline"
                };

            _unpriced.Add(commodityKey);
            return new CommodityRate { CommodityKey = commodityKey, RateUGX = 0, Source = "unpriced" };
        }

        /// <summary>
        /// Parse the shipped CSV: CommodityKey,SupplierUnit,RateUGX,Description.
        /// '#' comment lines and blank lines are skipped; unparseable rows are
        /// skipped and reported through <paramref name="skipped"/> rather than
        /// silently dropped.
        /// </summary>
        public static List<CommodityRate> ParseCsv(IEnumerable<string> lines, out List<string> skipped)
        {
            var outList = new List<CommodityRate>();
            skipped = new List<string>();
            bool headerSeen = false;

            foreach (string raw in lines ?? Enumerable.Empty<string>())
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var parts = line.Split(',');
                if (!headerSeen && parts[0].Trim().Equals("CommodityKey", StringComparison.OrdinalIgnoreCase))
                { headerSeen = true; continue; }

                if (parts.Length < 3) { skipped.Add(line); continue; }
                if (!double.TryParse(parts[2].Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double rate))
                { skipped.Add(line); continue; }

                outList.Add(new CommodityRate
                {
                    CommodityKey = parts[0].Trim(),
                    SupplierUnit = parts[1].Trim(),
                    RateUGX = rate,
                    Source = "baseline"
                });
            }
            return outList;
        }
    }
}
