// ══════════════════════════════════════════════════════════════════════════
//  CostValidators.cs — Five pre-BOQ validators (P2).
//
//  Run before BuildBOQDocument to surface problems while they're cheap to
//  fix. Used by Cost_ValidateAll command and inlined into the
//  WORKFLOW_BOQ_FullRefresh preset as a haltOnError step so a broken model
//  doesn't waste a 30-second build that produces a zero-rate BOQ.
//
//  Same shape as the v4 validators in Core/Validation/ — concrete classes
//  with a Name property and a Validate(Document) → List<ValidationResult>
//  method. No interface (matches existing convention).
//
//  Severity guidance:
//    Error   = build will produce broken / unusable BOQ; block export.
//    Warning = build will succeed but with degraded confidence; surface in
//              the report and let the QS decide.
//    Info    = noteworthy but not actionable.
//
//  P2 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.BOQ.Rates;
using StingTools.BOQ.Takeoff;
using StingTools.Core;

namespace StingTools.Core.Validation.Cost
{
    // ──────────────────────────────────────────────────────────────────
    //  1. MissingMaterialValidator
    //  Flags elements in cost-bearing categories that have no material
    //  bound — the carbon factor lookup + material-based rate strategies
    //  silently fail, producing under-estimated BOQ totals.
    // ──────────────────────────────────────────────────────────────────
    public class MissingMaterialValidator
    {
        public string Name => "MissingMaterialValidator";
        private const string Tag = "MissingMaterialValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys,
                    StringComparer.OrdinalIgnoreCase);
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in col)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !knownCats.Contains(cat)) continue;
                    if (!IsCostBearingCategory(cat)) continue;

                    var mids = el.GetMaterialIds(false);
                    if (mids == null || mids.Count == 0)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "COST.MAT.MISSING",
                            $"{cat} '{el.Name}' has no material assigned — carbon factor + " +
                            "material-based rates will fall back to category default.",
                            Tag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"{Tag}: {ex.Message}"); }
            return results;
        }

        private static bool IsCostBearingCategory(string cat)
        {
            string lower = cat.ToLowerInvariant();
            // Categories where geometric quantity × material drives cost
            return lower.Contains("wall") || lower.Contains("floor")
                || lower.Contains("slab") || lower.Contains("roof")
                || lower.Contains("ceiling") || lower.Contains("foundation")
                || lower.Contains("column") || lower.Contains("beam")
                || lower.Contains("framing");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  2. UntypedCategoryValidator
    //  Flags elements in categories that have no matching TakeoffRule —
    //  BuildBOQDocument will fall back to legacy hard-coded logic which
    //  may produce unexpected quantities.
    // ──────────────────────────────────────────────────────────────────
    public class UntypedCategoryValidator
    {
        public string Name => "UntypedCategoryValidator";
        private const string Tag = "UntypedCategoryValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                var registry = TakeoffRuleRegistry.Get(doc);
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys,
                    StringComparer.OrdinalIgnoreCase);
                var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in col)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !knownCats.Contains(cat)) continue;
                    if (seenCategories.Contains(cat)) continue;

                    string disc = TagConfig.DiscMap.TryGetValue(cat, out var d) ? d : "X";
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = registry.Match(cat, disc, prod);
                    if (rule == null)
                    {
                        seenCategories.Add(cat);
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "COST.RULE.MISSING",
                            $"No take-off rule matched category '{cat}' " +
                            $"(disc={disc}); falling back to legacy hard-coded logic.",
                            Tag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"{Tag}: {ex.Message}"); }
            return results;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  3. UnpricedProdValidator
    //  Walks all cost-bearing elements through the RateProviderRegistry
    //  and reports those where no provider returned a non-zero rate.
    //  These produce zero-cost BOQ rows that visually inflate item count
    //  but not value.
    // ──────────────────────────────────────────────────────────────────
    public class UnpricedProdValidator
    {
        public string Name => "UnpricedProdValidator";
        private const string Tag = "UnpricedProdValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                // Get the registry the same way BOQCostManager does — by
                // loading the CSV + COBie tables. We don't have the
                // dictionaries here so we ask the registry by-key with
                // empty fallbacks; it will still pick up the rate
                // providers that don't need the dictionaries (param +
                // ES + default).
                var registry = RateProviderRegistry.Get(doc,
                    new Dictionary<string, (double rate, string unit)>(),
                    new Dictionary<string, string>(),
                    TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0),
                    TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0));

                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys,
                    StringComparer.OrdinalIgnoreCase);
                var unpriced = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var firstByCategory = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in col)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !knownCats.Contains(cat)) continue;

                    var req = new RateRequest
                    {
                        CategoryName = cat,
                        Discipline = TagConfig.DiscMap.TryGetValue(cat, out var d) ? d : "X",
                        ProdCode = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "",
                        Element = el
                    };
                    var lookup = registry.Resolve(req);
                    if (lookup == null || lookup.UnitRate <= 0)
                    {
                        unpriced.TryGetValue(cat, out int count);
                        unpriced[cat] = count + 1;
                        if (!firstByCategory.ContainsKey(cat))
                            firstByCategory[cat] = el.Id;
                    }
                }

                foreach (var kv in unpriced.OrderByDescending(x => x.Value))
                {
                    results.Add(new ValidationResult(
                        firstByCategory[kv.Key], ValidationSeverity.Warning,
                        "COST.RATE.UNPRICED",
                        $"{kv.Value} element(s) in '{kv.Key}' have no rate from any provider — " +
                        "BOQ row will be zero. Author a project rate card or extend cost_rates_5d.csv.",
                        Tag));
                }
            }
            catch (Exception ex) { StingLog.Warn($"{Tag}: {ex.Message}"); }
            return results;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  4. ZeroQuantityValidator
    //  Flags elements whose take-off rule would yield 0 quantity (e.g.
    //  walls with HOST_AREA_COMPUTED unbinding). Caught before
    //  BuildBOQDocument so the QS can fix the geometry rather than ship
    //  a zero-quantity priced row.
    // ──────────────────────────────────────────────────────────────────
    public class ZeroQuantityValidator
    {
        public string Name => "ZeroQuantityValidator";
        private const string Tag = "ZeroQuantityValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                var registry = TakeoffRuleRegistry.Get(doc);
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys,
                    StringComparer.OrdinalIgnoreCase);
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();

                foreach (Element el in col)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !knownCats.Contains(cat)) continue;

                    string disc = TagConfig.DiscMap.TryGetValue(cat, out var d) ? d : "X";
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = registry.Match(cat, disc, prod);
                    if (rule == null) continue;

                    // Skip literal:1.0 rules — those are intentionally
                    // unit-count and never "zero".
                    if (rule.QuantitySource != null &&
                        rule.QuantitySource.StartsWith("literal:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double q = TakeoffRuleRegistry.EvaluateQuantity(el, rule);
                    if (q <= 0.0001)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "COST.QTY.ZERO",
                            $"{cat} '{el.Name}' yields zero quantity under rule '{rule.Id}' " +
                            $"(source: {rule.QuantitySource}). Check geometry / parameter binding.",
                            Tag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"{Tag}: {ex.Message}"); }
            return results;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  5. StaleCostValidator
    //  Counts elements with ASS_CST_STALE_BOOL = 1. Set by
    //  StingCostStaleMarker IUpdater when geometry / material / type
    //  changes invalidate the last-costed line item. Surfacing this in
    //  the validator chain means the QS sees the staleness before
    //  exporting a stale BOQ.
    // ──────────────────────────────────────────────────────────────────
    public class StaleCostValidator
    {
        public string Name => "StaleCostValidator";
        private const string Tag = "StaleCostValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                int stale = 0;
                ElementId firstStale = null;
                var byReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();

                foreach (Element el in col)
                {
                    int v = ParameterHelpers.GetInt(el, ParamRegistry.CST_STALE_BOOL, 0);
                    if (v != 1) continue;
                    stale++;
                    if (firstStale == null) firstStale = el.Id;
                    string reason = ParameterHelpers.GetString(el, ParamRegistry.CST_STALE_REASON_TXT) ?? "(unknown)";
                    byReason.TryGetValue(reason, out int c);
                    byReason[reason] = c + 1;
                }

                if (stale > 0)
                {
                    string breakdown = string.Join(", ",
                        byReason.OrderByDescending(x => x.Value).Select(x => $"{x.Value}× {x.Key}"));
                    results.Add(new ValidationResult(firstStale, ValidationSeverity.Warning,
                        "COST.STALE",
                        $"{stale} element(s) have stale cost. Breakdown: {breakdown}. " +
                        "Run BOQ_Build then Cost_ClearStale to refresh.",
                        Tag));
                }
            }
            catch (Exception ex) { StingLog.Warn($"{Tag}: {ex.Message}"); }
            return results;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Aggregator — runs all five validators in one pass and returns a
    //  flat result list. Called by Cost_ValidateAll command.
    // ──────────────────────────────────────────────────────────────────
    public static class CostValidatorChain
    {
        public static List<ValidationResult> RunAll(Document doc)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;
            all.AddRange(new MissingMaterialValidator().Validate(doc));
            all.AddRange(new UntypedCategoryValidator().Validate(doc));
            all.AddRange(new UnpricedProdValidator().Validate(doc));
            all.AddRange(new ZeroQuantityValidator().Validate(doc));
            all.AddRange(new StaleCostValidator().Validate(doc));
            return all;
        }
    }
}
