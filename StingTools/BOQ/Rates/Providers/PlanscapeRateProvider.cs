// ══════════════════════════════════════════════════════════════════════════
//  PlanscapeRateProvider.cs — Phase 2B. Live rates from the Planscape server.
//
//  Reuses the existing PlanscapeServerClient singleton (auth + base URL). When
//  the client is connected and a project id is known, queries the server's BOQ
//  rate endpoint for a category/PROD rate. Returns null when offline / not
//  connected / no project / no match, so the registry continues down the chain
//  and nothing throws (offline-safe). Priority 55 sits just above BCIS (50).
//
//  The server rate endpoint is optional — a 404 / non-success degrades to null
//  exactly like an unconfigured feed, so this provider is safe to register
//  whenever the Planscape feed toggle is on.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ.Rates.Providers
{
    public sealed class PlanscapeRateProvider : IRateProvider
    {
        public string Id => "planscape";
        public int Priority => 55;
        public bool RequiresNetwork => true;

        private readonly Guid _projectId;

        // Per-build hot cache keyed by category|unit so we don't re-hit the
        // server for every element of the same category.
        private readonly ConcurrentDictionary<string, RateLookup> _hot
            = new ConcurrentDictionary<string, RateLookup>(StringComparer.OrdinalIgnoreCase);

        public PlanscapeRateProvider(Guid projectId)
        {
            _projectId = projectId;
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.CategoryName)) return null;
            if (_projectId == Guid.Empty) return null;

            var client = PlanscapeServerClient.Instance;
            if (client == null || !client.IsConnected) return null;

            string cacheKey = $"{req.CategoryName}|{req.Unit}|{req.ProdCode}";
            if (_hot.TryGetValue(cacheKey, out var cached)) return cached;

            try
            {
                var json = client.GetBoqRateAsync(_projectId, req.CategoryName, req.Unit ?? "", req.ProdCode ?? "")
                    .GetAwaiter().GetResult();
                if (json == null) { _hot[cacheKey] = null; return null; }

                // Tolerate either a bare object or a { rate: {...} } envelope.
                var node = json["rate"] ?? json;
                double rate = node.Value<double?>("unitRate") ?? node.Value<double?>("rate") ?? 0;
                if (rate <= 0) { _hot[cacheKey] = null; return null; }

                var lookup = new RateLookup
                {
                    UnitRate = rate,
                    CurrencyCode = node.Value<string>("currency") ?? "UGX",
                    Unit = node.Value<string>("unit") ?? (req.Unit ?? "each"),
                    SourceId = Id,
                    Confidence = node.Value<int?>("confidence") ?? 60,
                    Provenance = $"Planscape live ({DateTime.UtcNow:yyyy-MM-dd})",
                    MatchedKey = req.CategoryName,
                    FetchedUtc = DateTime.UtcNow
                };
                _hot[cacheKey] = lookup;
                return lookup;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("PlanscapeRate", $"PlanscapeRateProvider {req.CategoryName}: {ex.Message}");
                _hot[cacheKey] = null;
                return null;
            }
        }
    }
}
