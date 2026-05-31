// ══════════════════════════════════════════════════════════════════════════
//  RateProviders.cs — Concrete IRateProvider implementations.
//
//  Five providers preserve the exact priority order of the legacy
//  BOQCostManager.ResolveRate fallback chain so behaviour is byte-for-byte
//  identical after the P0 refactor. New providers (BCIS, Spon's, project
//  rate card) slot in alongside without editing existing code.
//
//  P0 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.Core.Storage;

namespace StingTools.BOQ.Rates
{
    // ──────────────────────────────────────────────────────────────────────
    //  1. Parameter override (priority 100)
    //  Replaces ResolveRate Pass 0 — user wrote CST_UNIT_RATE_UGX directly
    //  via the BOQ panel edit flow, marking CST_RATE_SOURCE = "Override".
    //  Must win over all CSV/COBie/default matches so inline edits persist.
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class ParameterOverrideRateProvider : IRateProvider
    {
        public string Id => "param-override";
        public int Priority => 100;
        public bool RequiresNetwork => false;

        public RateLookup Resolve(RateRequest req)
        {
            if (req?.Element == null) return null;
            try
            {
                string stored = ParameterHelpers.GetString(req.Element, "CST_RATE_SOURCE");
                if (!string.Equals(stored, "Override", StringComparison.OrdinalIgnoreCase)) return null;

                string rateStr = ParameterHelpers.GetString(req.Element, "CST_UNIT_RATE_UGX");
                if (!double.TryParse(rateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double ovr) || ovr <= 0)
                    return null;

                return new RateLookup
                {
                    UnitRate = ovr,
                    CurrencyCode = "UGX",
                    Unit = string.IsNullOrEmpty(req.Unit) ? "each" : req.Unit,
                    SourceId = Id,
                    Confidence = 100,
                    Provenance = "User override via CST_UNIT_RATE_UGX",
                    MatchedKey = req.CategoryName
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ParameterOverrideRateProvider: {ex.Message}");
                return null;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  2. Extensible Storage override (priority 95)
    //  Reads StingCostRateOverrideSchema — currently writes RateGbp only.
    //  This provider returns GBP rates; FX-to-base conversion happens in
    //  the registry's currency adapter. When the schema is extended in a
    //  follow-up commit to carry currency + waste + dayworks, this
    //  provider will return them directly.
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class ExtensibleStorageRateProvider : IRateProvider
    {
        public string Id => "es-override";
        public int Priority => 95;
        public bool RequiresNetwork => false;

        public RateLookup Resolve(RateRequest req)
        {
            if (req?.Element == null) return null;
            try
            {
                var ovr = StingCostRateOverrideSchema.Read(req.Element);
                if (ovr == null || ovr.Rate <= 0) return null;

                // v2 schema honoured. Z-21b — single-surface waste convention:
                // WASTE is applied on the QUANTITY only (DeriveQuantity reads
                // ovr.WastePercent via WasteFactor), NEVER baked into the rate
                // here — otherwise an element would waste twice (rate × qty,
                // compounding ~10.25% for a 5%+5% case). The rate still carries
                // OVERHEAD + PROFIT, which are rate-side markups, not material waste.
                double loadedRate = ovr.Rate;
                if (ovr.OverheadPercent > 0)
                    loadedRate *= 1.0 + ovr.OverheadPercent / 100.0;
                if (ovr.ProfitPercent > 0)
                    loadedRate *= 1.0 + ovr.ProfitPercent / 100.0;

                string provenance = string.IsNullOrEmpty(ovr.Note)
                    ? $"ES override by {ovr.StampedBy}"
                    : $"ES override: {ovr.Note}";
                if (ovr.OverheadPercent > 0 || ovr.ProfitPercent > 0)
                    provenance += $" (+{ovr.OverheadPercent:0.#}% OH, +{ovr.ProfitPercent:0.#}% profit)";
                if (ovr.WastePercent > 0)
                    provenance += $" (+{ovr.WastePercent:0.#}% waste on qty)";
                if (ovr.IsLocked)
                    provenance += $" [LOCKED by {ovr.LockedByUser}]";

                return new RateLookup
                {
                    UnitRate = loadedRate,
                    CurrencyCode = string.IsNullOrEmpty(ovr.Currency) ? "GBP" : ovr.Currency,
                    Unit = string.IsNullOrEmpty(ovr.Unit) ? "each" : ovr.Unit,
                    SourceId = Id,
                    Confidence = 95,
                    Provenance = provenance,
                    MatchedKey = req.Element.UniqueId
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExtensibleStorageRateProvider: {ex.Message}");
                return null;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  3. CSV rate provider (priority 90 — category match, 85 — PROD match)
    //  Wraps the existing cost_rates_5d.csv loader. The dictionary is
    //  injected at construction so the registry can swap the source file
    //  without rebuilding the provider.
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class CsvRateProvider : IRateProvider
    {
        private readonly Dictionary<string, (double rate, string unit)> _rates;
        private readonly string _sourceFile;

        public string Id => "csv-default";
        public int Priority => 90;
        public bool RequiresNetwork => false;

        public CsvRateProvider(Dictionary<string, (double rate, string unit)> rates, string sourceFile = null)
        {
            _rates = rates ?? new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
            _sourceFile = sourceFile ?? "cost_rates_5d.csv";
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || _rates.Count == 0) return null;

            // Pass A: CSV match by category name (highest CSV confidence).
            if (!string.IsNullOrEmpty(req.CategoryName) &&
                _rates.TryGetValue(req.CategoryName, out var direct))
            {
                return new RateLookup
                {
                    UnitRate = direct.rate,
                    CurrencyCode = "UGX",
                    Unit = direct.unit ?? "each",
                    SourceId = Id,
                    Confidence = 90,
                    Provenance = $"{_sourceFile} category match",
                    MatchedKey = req.CategoryName
                };
            }

            // Pass B: CSV match by PROD code (slightly lower confidence — MEP fallback).
            if (!string.IsNullOrEmpty(req.ProdCode) &&
                _rates.TryGetValue(req.ProdCode, out var byProd))
            {
                return new RateLookup
                {
                    UnitRate = byProd.rate,
                    CurrencyCode = "UGX",
                    Unit = byProd.unit ?? "each",
                    SourceId = Id,
                    Confidence = 85,
                    Provenance = $"{_sourceFile} PROD match",
                    MatchedKey = req.ProdCode
                };
            }

            // Pass C: CSV match by MAT_CODE — new lookup enabled by the
            // registry's expanded RateRequest contract.
            if (!string.IsNullOrEmpty(req.MatCode) &&
                _rates.TryGetValue(req.MatCode, out var byMat))
            {
                return new RateLookup
                {
                    UnitRate = byMat.rate,
                    CurrencyCode = "UGX",
                    Unit = byMat.unit ?? "each",
                    SourceId = Id,
                    Confidence = 80,
                    Provenance = $"{_sourceFile} MAT_CODE match",
                    MatchedKey = req.MatCode
                };
            }

            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  4. COBie type-map provider (priority 75)
    //  Wraps COBIE_TYPE_MAP.csv — maps Revit category → cost-rate code,
    //  then looks up the code in the CSV rate table. Needs both tables so
    //  it takes the CSV dictionary as a dependency.
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class CobieRateProvider : IRateProvider
    {
        private readonly Dictionary<string, string> _cobieCodes;
        private readonly Dictionary<string, (double rate, string unit)> _csvRates;

        public string Id => "cobie-typemap";
        public int Priority => 75;
        public bool RequiresNetwork => false;

        public CobieRateProvider(Dictionary<string, string> cobieCodes,
                                 Dictionary<string, (double rate, string unit)> csvRates)
        {
            _cobieCodes = cobieCodes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _csvRates = csvRates ?? new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || _cobieCodes.Count == 0 || _csvRates.Count == 0) return null;
            if (string.IsNullOrEmpty(req.CategoryName)) return null;

            if (!_cobieCodes.TryGetValue(req.CategoryName, out string cobieCode) || string.IsNullOrEmpty(cobieCode))
                return null;
            if (!_csvRates.TryGetValue(cobieCode, out var byCobie))
                return null;

            return new RateLookup
            {
                UnitRate = byCobie.rate,
                CurrencyCode = "UGX",
                Unit = byCobie.unit ?? "each",
                SourceId = Id,
                Confidence = 75,
                Provenance = $"COBie type-map → {cobieCode}",
                MatchedKey = cobieCode
            };
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  5. Scheduling4DEngine default provider (priority 60)
    //  Last resort — uses the hard-coded default rates inside the 4D
    //  scheduling engine. Phase P3 of the plan removes this dictionary in
    //  favour of routing 4D through the registry, but until then this
    //  provider keeps behaviour identical to the legacy fallback chain.
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class DefaultRateProvider : IRateProvider
    {
        public string Id => "default-baseline";
        public int Priority => 60;
        public bool RequiresNetwork => false;

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.CategoryName)) return null;
            try
            {
                if (!Scheduling4DEngine.DefaultCostRates.TryGetValue(req.CategoryName, out var dcr))
                    return null;

                return new RateLookup
                {
                    UnitRate = dcr.ratePerUnit,
                    CurrencyCode = "UGX",
                    Unit = string.IsNullOrEmpty(dcr.unit) ? "each" : dcr.unit,
                    SourceId = Id,
                    Confidence = 60,
                    Provenance = string.IsNullOrEmpty(dcr.description)
                        ? "Scheduling4DEngine default"
                        : $"Scheduling4DEngine: {dcr.description}",
                    MatchedKey = req.CategoryName
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DefaultRateProvider: {ex.Message}");
                return null;
            }
        }
    }
}
