// ══════════════════════════════════════════════════════════════════════════
//  ProjectRateCardProvider.cs — Project-specific rate card.
//
//  Reads <project>/_BIM_COORD/rate_card.json with the shape:
//    [
//      { "category": "Walls", "unitRate": 95.0, "currency": "GBP",
//        "unit": "m2", "note": "Negotiated with sub-contractor X" },
//      ...
//    ]
//
//  Priority 93 (Phase 195 re-rank) — a negotiated project rate card is a
//  commercial commitment that should beat the generic CSV category rate (90),
//  the material library (85), COBie type-map (75) and 4D default (60). It
//  still sits below the ES per-element manual correction (98), Fohlio PO
//  price (96) and the inline parameter override (100). Override via
//  _BIM_COORD/boq_rate_policy.json (RatePolicy overlay).
//
//  P8 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core;
using Autodesk.Revit.DB;

namespace StingTools.BOQ.Rates.Providers
{
    public sealed class ProjectRateCardProvider : IRateProvider
    {
        public string Id => "project-rate-card";
        public int Priority => 93;
        public bool RequiresNetwork => false;

        private readonly Dictionary<string, RateLookup> _byCategory;

        private ProjectRateCardProvider(Dictionary<string, RateLookup> byCategory)
        {
            _byCategory = byCategory;
        }

        public static ProjectRateCardProvider Load(Document doc)
        {
            var map = new Dictionary<string, RateLookup>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return new ProjectRateCardProvider(map);
                string path = Path.Combine(parent, "_BIM_COORD", "rate_card.json");
                if (!File.Exists(path)) return new ProjectRateCardProvider(map);

                var entries = JsonConvert.DeserializeObject<List<RateCardEntry>>(
                    File.ReadAllText(path));
                if (entries == null) return new ProjectRateCardProvider(map);

                foreach (var e in entries)
                {
                    if (string.IsNullOrEmpty(e.Category) || e.UnitRate <= 0) continue;
                    map[e.Category] = new RateLookup
                    {
                        UnitRate = e.UnitRate,
                        CurrencyCode = string.IsNullOrEmpty(e.Currency) ? "GBP" : e.Currency,
                        Unit = string.IsNullOrEmpty(e.Unit) ? "each" : e.Unit,
                        SourceId = "project-rate-card",
                        Confidence = 87,
                        Provenance = string.IsNullOrEmpty(e.Note)
                            ? "Project rate card"
                            : $"Project rate card: {e.Note}",
                        MatchedKey = e.Category
                    };
                }
                StingLog.Info($"ProjectRateCardProvider: loaded {map.Count} entries from {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectRateCardProvider.Load: {ex.Message}");
            }
            return new ProjectRateCardProvider(map);
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.CategoryName)) return null;
            return _byCategory.TryGetValue(req.CategoryName, out var lookup) ? lookup : null;
        }

        private class RateCardEntry
        {
            public string Category { get; set; }
            public double UnitRate { get; set; }
            public string Currency { get; set; }
            public string Unit { get; set; }
            public string Note { get; set; }
        }
    }
}
