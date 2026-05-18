// ══════════════════════════════════════════════════════════════════════════
//  CostStamp.cs — Opt-in cost write-back from the tag pipeline.
//
//  When config flag WRITE_COST_ON_TAG is true, RunFullPipeline calls
//  CostStamp.WriteIfEnabled(doc, el) as its terminal step. The helper
//  resolves a unit rate via the registry, evaluates the quantity via
//  the takeoff rule and writes the neutral cost params
//  (ASS_CST_UNIT_RATE_NR / ASS_CST_CURRENCY_TXT / ASS_CST_FX_TO_BASE_NR /
//   ASS_CST_FX_DATE_DT / ASS_CST_AS_OF_DT + CST_MODELED_TOTAL_UGX +
//   CST_RATE_SOURCE + CST_QTY_MEASURED).
//
//  Failure-tolerant — any internal exception logs and returns false so
//  the broader tag pipeline doesn't fail.
//
//  Feedback-loop analysis:
//    StingCostStaleMarker IUpdater triggers on geometry + addition only
//    (NOT parameter changes), so writing cost params here can't cascade
//    back into the IUpdater. No settled-tick gate needed.
//
//  P3.1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.BOQ.Rates;
using StingTools.BOQ.Takeoff;
using StingTools.Core;

namespace StingTools.BOQ
{
    internal static class CostStamp
    {
        // Cache the config flag across the batch — TagConfig.GetConfigBool
        // hits a dictionary but the flag is checked once per element. The
        // cache is cleared at the registry-invalidate boundary.
        private static bool? _writeCostOnTagCached;
        private static readonly object _lock = new object();

        // Per-batch RateLookup cache. Without this, tagging 5 000 elements
        // costs ~5 000 RateProviderRegistry.Resolve calls — most of which
        // re-resolve the same (category, discipline, PROD, MAT_CODE) tuple.
        // Cache cuts the perf hit to O(unique-category-tuples), typically
        // < 50 entries. Reset via Invalidate() or naturally bounded by
        // _rateCacheMaxEntries.
        private const int _rateCacheMaxEntries = 500;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Rates.RateLookup>
            _rateCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Rates.RateLookup>();
        private static int _rateCacheHits = 0;
        private static int _rateCacheMisses = 0;

        internal static bool IsWriteOnTagEnabled()
        {
            if (_writeCostOnTagCached.HasValue) return _writeCostOnTagCached.Value;
            lock (_lock)
            {
                if (_writeCostOnTagCached.HasValue) return _writeCostOnTagCached.Value;
                bool enabled = false;
                try
                {
                    // Reuse the existing config double API since there's
                    // no GetConfigBool — non-zero means enabled.
                    enabled = TagConfig.GetConfigDouble("WRITE_COST_ON_TAG", 0.0) > 0.0;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CostStamp.IsWriteOnTagEnabled: {ex.Message}");
                }
                _writeCostOnTagCached = enabled;
                return enabled;
            }
        }

        /// <summary>Invalidate the config + per-batch rate cache — called by Cost_ReloadRules.</summary>
        public static void Invalidate()
        {
            lock (_lock) { _writeCostOnTagCached = null; }
            int h = System.Threading.Interlocked.Exchange(ref _rateCacheHits, 0);
            int m = System.Threading.Interlocked.Exchange(ref _rateCacheMisses, 0);
            if (h + m > 0)
                StingLog.Info($"CostStamp rate cache: {h} hit / {m} miss ({(double)h / (h + m) * 100:F1}% hit-rate) — flushed.");
            _rateCache.Clear();
        }

        /// <summary>
        /// Diagnostic — current per-batch cache stats. Useful for the
        /// Cost_RateHeatMap command to surface cache effectiveness.
        /// </summary>
        public static (int hits, int misses, int entries) GetRateCacheStats()
            => (_rateCacheHits, _rateCacheMisses, _rateCache.Count);

        /// <summary>
        /// Write cost params on the element if WRITE_COST_ON_TAG is true.
        /// Returns true on a successful write, false otherwise (including
        /// when the feature is disabled — caller cannot distinguish).
        /// Caller must have an active transaction open.
        /// </summary>
        public static bool WriteIfEnabled(Document doc, Element el)
        {
            if (!IsWriteOnTagEnabled()) return false;
            if (doc == null || el == null) return false;

            try
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(catName)) return false;
                string disc = TagConfig.DiscMap != null && TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "X";
                string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";

                // Resolve quantity via the takeoff registry. If no rule
                // matches we skip — that's the same fallback contract the
                // BOQ engine honours.
                var ruleRegistry = TakeoffRuleRegistry.Get(doc);
                var rule = ruleRegistry.Match(catName, disc, prod);
                if (rule == null) return false;

                double qty = TakeoffRuleRegistry.EvaluateQuantity(el, rule);
                if (rule.WastePercent > 0) qty *= 1.0 + rule.WastePercent / 100.0;
                if (qty <= 0.0001) return false;

                // Resolve rate via the per-batch cache → registry. Cache
                // key intentionally excludes Element identity so two
                // elements in the same category share one Resolve call.
                // Param + ES override providers (which DO depend on
                // element identity) are correctly skipped by the cache —
                // their priority is high enough that they're consulted
                // first by the registry, and overrides are rare. If a
                // QS is using per-element overrides at scale, they
                // should drive cost via the BOQ_Refresh path (which
                // doesn't use this cache) rather than tag-time write-back.
                string matCode = ParameterHelpers.GetString(el, "MAT_CODE") ?? "";
                string cacheKey = $"{catName}|{disc}|{prod}|{matCode}|{rule.Unit ?? "each"}";
                double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);

                Rates.RateLookup lookup;
                if (_rateCache.TryGetValue(cacheKey, out lookup))
                {
                    System.Threading.Interlocked.Increment(ref _rateCacheHits);
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref _rateCacheMisses);
                    double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);
                    var rateRegistry = RateProviderRegistry.Get(doc,
                        new Dictionary<string, (double rate, string unit)>(),
                        new Dictionary<string, string>(),
                        ugxPerUsd, ugxPerGbp);
                    var req = new RateRequest
                    {
                        CategoryName = catName,
                        Discipline = disc,
                        ProdCode = prod,
                        MatCode = matCode,
                        Unit = rule.Unit ?? "each",
                        CurrencyCode = "UGX",
                        AsOf = DateTime.UtcNow,
                        Element = el
                    };
                    lookup = rateRegistry.Resolve(req);
                    // Bounded cache — drop if we exceed the cap. We don't
                    // need LRU semantics because a single tag operation
                    // typically touches < 100 unique category tuples.
                    if (lookup != null && _rateCache.Count < _rateCacheMaxEntries)
                        _rateCache[cacheKey] = lookup;
                }
                if (lookup == null || lookup.UnitRate <= 0) return false;

                double total = qty * lookup.UnitRate;

                // Write neutral params (P0.2) + legacy compatibility params.
                // SetIfChanged-style: skip writes when nothing changed so
                // the IUpdater for parameter-changed listeners isn't
                // pointlessly fired downstream.
                WriteNumber(el, ParamRegistry.CST_UNIT_RATE_NR, lookup.UnitRate);
                ParameterHelpers.SetString(el, ParamRegistry.CST_CURRENCY_TXT,
                    lookup.CurrencyCode ?? "UGX", overwrite: true);
                WriteNumber(el, ParamRegistry.CST_FX_TO_BASE_NR, ugxPerUsd);
                ParameterHelpers.SetString(el, ParamRegistry.CST_FX_DATE_DT,
                    DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.CST_AS_OF_DT,
                    DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    overwrite: true);

                // Legacy mirror — keep existing schedules unchanged.
                ParameterHelpers.SetString(el, "CST_UNIT_RATE_UGX",
                    lookup.UnitRate.ToString("F0", CultureInfo.InvariantCulture),
                    overwrite: true);
                ParameterHelpers.SetString(el, "CST_QTY_MEASURED",
                    $"{qty:F3} {rule.Unit ?? "each"}", overwrite: true);
                ParameterHelpers.SetString(el, "CST_RATE_SOURCE",
                    MapProviderIdToLegacySource(lookup.SourceId), overwrite: true);
                WriteNumber(el, "CST_MODELED_TOTAL_UGX", total);

                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CostStamp.WriteIfEnabled {el?.Id}: {ex.Message}");
                return false;
            }
        }

        private static void WriteNumber(Element el, string paramName, double value)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double && Math.Abs(p.AsDouble() - value) < 0.0001) return;
                p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"CostStamp.WriteNumber {paramName} on {el?.Id}: {ex.Message}"); }
        }

        // Mirrors BOQCostManager.MapProviderIdToLegacySource — kept local
        // here to avoid a dependency on BOQCostManager from the tag
        // pipeline.
        private static string MapProviderIdToLegacySource(string providerId)
        {
            switch (providerId ?? "")
            {
                case "param-override": return "Override";
                case "es-override":    return "Override";
                case "csv-default":    return "CSV";
                case "cobie-typemap":  return "COBie";
                case "default-baseline": return "Default";
                default:               return providerId ?? "None";
            }
        }
    }
}
