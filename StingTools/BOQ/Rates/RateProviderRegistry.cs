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
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.BIMManager;
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
        private readonly RatePolicy _policy;
        private readonly double _ugxPerUsd;
        private readonly double _ugxPerGbp;

        private RateProviderRegistry(IEnumerable<IRateProvider> providers,
                                     RatePolicy policy,
                                     double ugxPerUsd, double ugxPerGbp)
        {
            _policy = policy ?? RatePolicy.Empty;
            _providers = providers
                .Where(p => p != null)
                // Phase 195 — order by the POLICY-effective priority so a
                // project-scoped boq_rate_policy.json can re-rank the chain
                // without recompiling. Disabled providers stay in the list but
                // are skipped at Resolve time (keeps ResolveAll diagnostics honest).
                .OrderByDescending(EffPriority)
                .ToList();
            _ugxPerUsd = ugxPerUsd > 0 ? ugxPerUsd : 3700.0;
            _ugxPerGbp = ugxPerGbp > 0 ? ugxPerGbp : 4700.0;
        }

        /// <summary>Policy-effective priority for a provider (override or baseline).</summary>
        private int EffPriority(IRateProvider p) => _policy.EffectivePriority(p.Id, p.Priority);

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
            return _cache.GetOrAdd(key, _ =>
            {
                var policy = LoadPolicy(doc);
                return Build(csvRates, cobieCostCodes, policy, ugxPerUsd, ugxPerGbp);
            });
        }

        /// <summary>
        /// Resolve <c>&lt;project&gt;/_BIM_COORD/boq_rate_policy.json</c> via the same
        /// path convention ProjectRateCardProvider uses, and parse it. Never
        /// throws — a missing or malformed file yields an empty (no-op) policy.
        /// </summary>
        private static RatePolicy LoadPolicy(Document doc)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string projectDir = Path.GetDirectoryName(bimDir);
                var policy = RatePolicy.Load(projectDir);
                if (policy?.Providers != null && policy.Providers.Count > 0)
                    StingLog.Info($"RateProviderRegistry: applied boq_rate_policy.json overlay ({policy.Providers.Count} provider override(s)).");
                return policy ?? RatePolicy.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RateProviderRegistry.LoadPolicy: {ex.Message}");
                return RatePolicy.Empty;
            }
        }

        /// <summary>
        /// Force-clear the cache. Called by Cost_ReloadRates after the
        /// underlying CSV / takeoff rules are edited on disk.
        /// </summary>
        public static void Invalidate() => _cache.Clear();

        private static RateProviderRegistry Build(
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            RatePolicy policy,
            double ugxPerUsd, double ugxPerGbp)
        {
            var providers = new List<IRateProvider>
            {
                new ParameterOverrideRateProvider(),
                // Phase C (KUT lifecycle) — Owner FF&E procurement price (96), above
                // material-library/CSV, below inline override (100).
                new FohlioRateProvider(),
                new ExtensibleStorageRateProvider(),
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
            return new RateProviderRegistry(providers, policy, ugxPerUsd, ugxPerGbp);
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
            // Re-sort by policy-effective priority so an externally-registered
            // provider (e.g. bcis-http) lands at its overlaid rank, or is
            // sorted as baseline when the policy doesn't name it.
            _providers.Sort((a, b) => EffPriority(b).CompareTo(EffPriority(a)));
            StingLog.Info($"RateProviderRegistry: registered {provider.Id} (priority {EffPriority(provider)}).");
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
                // Phase 195 — a project policy may switch a provider off
                // (e.g. disable the live BCIS feed for a deterministic tender).
                if (!_policy.IsEnabled(provider.Id)) continue;
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
                MatchedKey = lookup.MatchedKey,
                RateIncludesOhp = lookup.RateIncludesOhp
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
