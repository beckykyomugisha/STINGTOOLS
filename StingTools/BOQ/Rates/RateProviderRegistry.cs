// ══════════════════════════════════════════════════════════════════════════
//  RateProviderRegistry.cs — Composition root for IRateProvider.
//
//  Single entry point that BOQCostManager.ResolveRate calls. Maintains a
//  priority-ordered list, walks it on each request and returns the first
//  non-null lookup. Cached per Document so the CSV + COBie tables are
//  loaded once per BuildBOQDocument run.
//
//  Currency adapter: providers may return rates in any currency
//  (GBP from ES override, UGX from CSV). The registry converts to the
//  requested CurrencyCode using the FX rate carried by the document, so
//  the caller always receives one consistent currency.
//
//  P0 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ.Rates
{
    internal sealed class RateProviderRegistry
    {
        // Per-document cache. Each Document gets its own registry so the
        // CSV/COBie tables are loaded once per run and providers don't
        // leak between projects.
        private static readonly ConcurrentDictionary<string, RateProviderRegistry> _cache
            = new ConcurrentDictionary<string, RateProviderRegistry>(StringComparer.OrdinalIgnoreCase);

        private readonly List<IRateProvider> _providers;
        private readonly double _ugxPerUsd;
        private readonly double _ugxPerGbp;

        private RateProviderRegistry(IEnumerable<IRateProvider> providers,
                                     double ugxPerUsd, double ugxPerGbp)
        {
            _providers = providers
                .Where(p => p != null)
                .OrderByDescending(p => p.Priority)
                .ToList();
            _ugxPerUsd = ugxPerUsd > 0 ? ugxPerUsd : 3700.0;
            _ugxPerGbp = ugxPerGbp > 0 ? ugxPerGbp : 4700.0;
        }

        /// <summary>
        /// Acquire the registry for this document. Builds it lazily from
        /// the CSV + COBie tables on first call; subsequent calls hit the
        /// cache.
        /// </summary>
        public static RateProviderRegistry Get(
            Document doc,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            double ugxPerUsd,
            double ugxPerGbp = 0)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Build(doc, csvRates, cobieCostCodes, ugxPerUsd, ugxPerGbp));
        }

        /// <summary>
        /// Force-clear the cache. Called by Cost_ReloadRates after the
        /// underlying CSV / takeoff rules are edited on disk.
        /// </summary>
        public static void Invalidate() => _cache.Clear();

        private static RateProviderRegistry Build(
            Document doc,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            double ugxPerUsd, double ugxPerGbp)
        {
            var providers = new List<IRateProvider>
            {
                new ParameterOverrideRateProvider(),
                new ExtensibleStorageRateProvider(),
                // P3.4 — project rate card (incl. QS-Bill-imported rates at
                // <project>/_BIM_COORD/rate_card.json). Priority 87 sits above
                // CSV so a QS-priced category beats the corporate default.
                // Returns null when the file is absent, so legacy projects are
                // unaffected.
                Providers.ProjectRateCardProvider.Load(doc),
                // N+8 — Material-library rate (priority 95). Sits above CSV
                // category match so a project that has curated material cost
                // in the MAT panel always wins over the cost_rates_5d.csv
                // category rate. Falls through to CSV when no material rate
                // is set, so legacy projects keep working.
                new MaterialLibraryRateProvider(),
                // Phase 184j / P8: external + project rate-card providers
                // are added lazily by RegisterExternalProviders so the
                // registry doesn't fail when a project hasn't configured
                // them yet. See Get(doc, ...) below.
                new CsvRateProvider(csvRates),
                new CobieRateProvider(cobieCostCodes, csvRates),
                new DefaultRateProvider()
            };

            // Phase 2B — live-rate feeds, configured inline via the "Rate feeds"
            // action (persisted to _BIM_COORD/rate_feeds.json). Added here so the
            // priority chain — and ResolveAll (the Fetch live rates surface) —
            // include them automatically. Disabled feeds add nothing (legacy
            // projects unaffected); a misconfigured / unreachable feed simply
            // returns null at Resolve time (offline-safe).
            try
            {
                AddConfiguredFeeds(providers, doc);
            }
            catch (Exception ex) { StingLog.Warn($"RateProviderRegistry feeds: {ex.Message}"); }

            return new RateProviderRegistry(providers, ugxPerUsd, ugxPerGbp);
        }

        /// <summary>
        /// Phase 2B — just the configured live-rate feed providers (BCIS /
        /// Planscape), with NO model-reading providers. The Fetch-live-rates
        /// surface calls Resolve on these off the UI thread; they only touch the
        /// network (category/unit), never the Revit API — safe to run off the
        /// API thread. Returns an empty list when no feed is enabled.
        /// </summary>
        public static List<IRateProvider> GetLiveFeedProviders(Document doc)
        {
            var list = new List<IRateProvider>();
            try { AddConfiguredFeeds(list, doc); }
            catch (Exception ex) { StingLog.Warn($"RateProviderRegistry.GetLiveFeedProviders: {ex.Message}"); }
            return list;
        }

        /// <summary>Append the BCIS / Planscape live-rate providers when the
        /// project's rate_feeds.json enables them.</summary>
        private static void AddConfiguredFeeds(List<IRateProvider> providers, Document doc)
        {
            if (doc == null) return;
            var cfg = RateFeedsStore.Load(doc);
            if (cfg == null) return;

            if (cfg.BcisEnabled && !string.IsNullOrWhiteSpace(cfg.BcisBaseUrl))
            {
                string cacheDir = null;
                try
                {
                    string bimDir = StingTools.BIMManager.BIMManagerEngine.GetBIMManagerDir(doc);
                    if (!string.IsNullOrEmpty(bimDir))
                        cacheDir = System.IO.Path.Combine(bimDir, "rate_cache");
                }
                catch (Exception ex) { StingLog.Warn($"RateProviderRegistry BCIS cacheDir: {ex.Message}"); }

                providers.Add(new Providers.BcisHttpRateProvider(
                    cfg.BcisBaseUrl, cfg.BcisApiKey, cfg.BcisTtlMinutes, cacheDir));
                StingLog.Info("RateProviderRegistry: BCIS live feed enabled.");
            }

            if (cfg.PlanscapeEnabled)
            {
                Guid pid = Guid.Empty;
                try { pid = StingTools.BIMManager.PlanscapeServerClient.Instance?.CurrentProjectId ?? Guid.Empty; }
                catch (Exception ex) { StingLog.Warn($"RateProviderRegistry Planscape pid: {ex.Message}"); }
                providers.Add(new Providers.PlanscapeRateProvider(pid));
                StingLog.Info("RateProviderRegistry: Planscape live feed enabled.");
            }
        }

        /// <summary>
        /// Inject external providers (BCIS HTTP, Spon's, project rate
        /// card) after the registry is built. Called from
        /// Cost_ReloadRules when network providers are configured. The
        /// providers are sorted into the priority chain on insertion.
        /// </summary>
        public void RegisterExternalProvider(IRateProvider provider)
        {
            if (provider == null) return;
            _providers.Add(provider);
            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            StingLog.Info($"RateProviderRegistry: registered {provider.Id} (priority {provider.Priority}).");
        }

        /// <summary>
        /// Resolve a rate request through the priority chain. Returns a
        /// non-null lookup in the requested currency, or a zero-rate
        /// sentinel (confidence 20) when no provider matched.
        /// </summary>
        public RateLookup Resolve(RateRequest req)
        {
            if (req == null) return ZeroLookup("");
            foreach (var provider in _providers)
            {
                try
                {
                    var lookup = provider.Resolve(req);
                    if (lookup == null || lookup.UnitRate <= 0) continue;
                    return ConvertCurrency(lookup, req.CurrencyCode);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RateProviderRegistry {provider.Id}: {ex.Message}");
                }
            }
            return ZeroLookup(req.CategoryName);
        }

        /// <summary>
        /// Diagnostic — returns every provider's response (including
        /// nulls) so the rate-source heat-map can show which providers
        /// matched and which were skipped.
        /// </summary>
        public IReadOnlyList<(IRateProvider provider, RateLookup result)> ResolveAll(RateRequest req)
        {
            var results = new List<(IRateProvider, RateLookup)>(_providers.Count);
            foreach (var provider in _providers)
            {
                try { results.Add((provider, provider.Resolve(req))); }
                catch (Exception ex)
                {
                    StingLog.Warn($"RateProviderRegistry.ResolveAll {provider.Id}: {ex.Message}");
                    results.Add((provider, null));
                }
            }
            return results;
        }

        private RateLookup ConvertCurrency(RateLookup lookup, string targetCurrency)
        {
            if (lookup == null) return null;
            if (string.IsNullOrEmpty(targetCurrency) ||
                string.Equals(lookup.CurrencyCode, targetCurrency, StringComparison.OrdinalIgnoreCase))
                return lookup;

            // Convert via UGX as the base. Supported: UGX, USD, GBP.
            double rateInUgx = lookup.UnitRate;
            switch ((lookup.CurrencyCode ?? "").ToUpperInvariant())
            {
                case "UGX": rateInUgx = lookup.UnitRate; break;
                case "USD": rateInUgx = lookup.UnitRate * _ugxPerUsd; break;
                case "GBP": rateInUgx = lookup.UnitRate * _ugxPerGbp; break;
                default:
                    StingLog.Warn($"RateProviderRegistry: unsupported source currency {lookup.CurrencyCode}, treating as UGX");
                    rateInUgx = lookup.UnitRate;
                    break;
            }

            double targetRate = rateInUgx;
            switch ((targetCurrency ?? "").ToUpperInvariant())
            {
                case "UGX": targetRate = rateInUgx; break;
                case "USD": targetRate = _ugxPerUsd > 0 ? rateInUgx / _ugxPerUsd : 0; break;
                case "GBP": targetRate = _ugxPerGbp > 0 ? rateInUgx / _ugxPerGbp : 0; break;
                default:
                    StingLog.Warn($"RateProviderRegistry: unsupported target currency {targetCurrency}");
                    return lookup;
            }

            return new RateLookup
            {
                UnitRate = targetRate,
                CurrencyCode = targetCurrency.ToUpperInvariant(),
                Unit = lookup.Unit,
                SourceId = lookup.SourceId,
                FetchedUtc = lookup.FetchedUtc,
                Confidence = lookup.Confidence,
                Provenance = $"{lookup.Provenance} (FX {lookup.CurrencyCode}→{targetCurrency})",
                MatchedKey = lookup.MatchedKey
            };
        }

        private RateLookup ZeroLookup(string key) => new RateLookup
        {
            UnitRate = 0,
            CurrencyCode = "UGX",
            Unit = "each",
            SourceId = "none",
            Confidence = 20,
            Provenance = "No provider matched",
            MatchedKey = key
        };
    }
}
