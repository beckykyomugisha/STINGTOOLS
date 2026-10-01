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
//  Priority 93 (DSCH-23). The chain resolves HIGHEST first and takes the
//  first non-null rate. At its old 87 the card sat below the corporate CSV
//  category rate (90), so a project's negotiated rates only priced
//  categories the corporate card did not - in practice nothing. 93 puts the
//  project above every corporate default and below the curated per-material
//  library (95), which is more specific than a per-category card. A project's
//  boq_rate_policy.json can still re-rank it (KUT moves the library to 85).
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
        public const int DefaultPriority = 93;
        public int Priority => DefaultPriority;
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
                string path = StingPaths.MetaFile(doc, "_BIM_COORD", "rate_card.json");
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
                        // CA-1 — a rate-card row without a currency is project base
                        // (UGX), not GBP. Explicit e.Currency still wins.
                        CurrencyCode = string.IsNullOrEmpty(e.Currency) ? RateCurrency.Base : e.Currency,
                        Unit = string.IsNullOrEmpty(e.Unit) ? "each" : e.Unit,
                        SourceId = "project-rate-card",
                        Confidence = DefaultPriority,
                        Provenance = string.IsNullOrEmpty(e.Note)
                            ? "Project rate card"
                            : $"Project rate card: {e.Note}",
                        MatchedKey = e.Category,
                        // G4 — optional L/P/M split (only when the entry provides one).
                        LabourRate = e.Labour > 0 ? e.Labour : (double?)null,
                        PlantRate = e.Plant > 0 ? e.Plant : (double?)null,
                        MaterialRate = e.Material > 0 ? e.Material : (double?)null
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
            // G4 — optional per-unit labour / plant / material split.
            public double Labour { get; set; }
            public double Plant { get; set; }
            public double Material { get; set; }
        }
    }
}
