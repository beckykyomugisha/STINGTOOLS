// PlumbingBOQEnricher — Phase 179f.
//
// Supplements BOQCostManager.BuildBOQDocument with the categories the main
// engine doesn't cover for plumbing scope: pipe insulations (OST_PipeInsulations),
// duct insulations (OST_DuctInsulations) and STING-emitted sleeves / hangers
// (OST_GenericModel with STING_SLEEVE_* / STING_HANGER_* family names).
//
// The main BOQCostManager filters elements through TagConfig.DiscMap and
// SharedParamGuids.AllCategoryEnums. Insulation isn't bound to STING params
// and STING-emitted Generic Models can be missed depending on the project's
// param coverage — both fall through here so the QS pack still gets the
// totals the panel shows.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.BOQ;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public static class PlumbingBOQEnricher
    {
        // Default rates used when cost_rates_5d.csv has no entry for a
        // category. Surfaced through TagConfig so a project can override per
        // its own cost sheet without editing the shipped CSV — the panel
        // status strip flags every defaulted row via RateSource = "Default".
        private const double FallbackPipeInsulationUgxPerM = 55_500;
        private const double FallbackDuctInsulationUgxPerM = 74_000;
        private const double FallbackSleeveUgxEach         = 185_000;
        private const double FallbackHangerUgxEach         = 111_000;

        /// <summary>
        /// Builds the supplemental BOQ rows — insulation runs, sleeves, hangers.
        /// Quantity for insulation is the BIP CURVE_ELEM_LENGTH on each
        /// PipeInsulation / DuctInsulation element. Sleeves and hangers count
        /// as 1 each. Rates resolve via the shared BOQCostManager CSV reader,
        /// falling back to TagConfig overrides then to the constants above.
        /// </summary>
        /// <param name="excludeRevitElementIds">Optional set of Revit element
        /// ids already accounted for by the main BOQ pipeline — the enricher
        /// skips any element whose id is present so sleeves/hangers modelled
        /// under OST_PipeAccessory don't double-count.</param>
        public static List<BOQLineItem> Build(Document doc, HashSet<long> excludeRevitElementIds = null)
        {
            var items = new List<BOQLineItem>();
            if (doc == null) return items;

            var rates = BOQCostManager.LoadCsvRates();
            var ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            var skip = excludeRevitElementIds ?? new HashSet<long>();

            double pipeInsRate = TagConfig.GetConfigDouble("PLUMB_BOQ_DEFAULT_PIPE_INSULATION_UGX_M", FallbackPipeInsulationUgxPerM);
            double ductInsRate = TagConfig.GetConfigDouble("PLUMB_BOQ_DEFAULT_DUCT_INSULATION_UGX_M", FallbackDuctInsulationUgxPerM);
            double sleeveRate  = TagConfig.GetConfigDouble("PLUMB_BOQ_DEFAULT_SLEEVE_UGX_EACH",       FallbackSleeveUgxEach);
            double hangerRate  = TagConfig.GetConfigDouble("PLUMB_BOQ_DEFAULT_HANGER_UGX_EACH",       FallbackHangerUgxEach);

            CollectInsulation(doc, items, rates, ugxPerUsd, skip, BuiltInCategory.OST_PipeInsulations,
                "Pipe Insulations", pipeInsRate, "33", "M");
            CollectInsulation(doc, items, rates, ugxPerUsd, skip, BuiltInCategory.OST_DuctInsulations,
                "Duct Insulations", ductInsRate, "33", "M");

            CollectGenericByFamily(doc, items, rates, ugxPerUsd, skip,
                familyPrefixes: new[] { "STING_SLEEVE_", "STING_PROVISION_VOID" },
                category: "Pipe Sleeve",
                rateKey: "Pipe Sleeve",
                defaultUgx: sleeveRate,
                nrm2: "32", discipline: "M");

            CollectGenericByFamily(doc, items, rates, ugxPerUsd, skip,
                familyPrefixes: new[] { "STING_HANGER_", "STING_TRAPEZE_" },
                category: "Pipe Hanger",
                rateKey: "Pipe Hanger",
                defaultUgx: hangerRate,
                nrm2: "33", discipline: "M");

            return items;
        }

        private static void CollectInsulation(Document doc, List<BOQLineItem> items,
            Dictionary<string, (double rate, string unit)> rates, double ugxPerUsd,
            HashSet<long> skip, BuiltInCategory bic, string categoryName,
            double defaultRateUgx, string nrm2, string discipline)
        {
            try
            {
                var rateUgx = rates.TryGetValue(categoryName, out var r) ? r.rate : defaultRateUgx;
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(bic).WhereElementIsNotElementType();
                int sortOrder = 0;
                foreach (var el in collector)
                {
                    long rid = el.Id?.Value ?? -1;
                    if (rid > 0 && skip.Contains(rid)) continue;
                    double lengthFt = 0;
                    var p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (p != null && p.HasValue) lengthFt = p.AsDouble();
                    if (lengthFt <= 0) continue;
                    double lengthM = lengthFt * 0.3048;
                    items.Add(new BOQLineItem
                    {
                        NRM2Section      = nrm2,
                        Category         = categoryName,
                        Discipline       = discipline,
                        ItemName         = el.Name ?? categoryName,
                        FamilyName       = (el as FamilyInstance)?.Symbol?.Family?.Name ?? "",
                        TypeName         = el.Name ?? "",
                        Quantity         = lengthM,
                        Unit             = "m",
                        RateUGX          = rateUgx,
                        RateUSD          = ugxPerUsd > 0 ? Math.Round(rateUgx / ugxPerUsd, 2) : 0,
                        ResolvedNRM2Paragraph = $"{categoryName} — {(el.Name ?? "")}",
                        Source           = BOQRowSource.Model,
                        RevitElementId   = el.Id?.Value ?? -1,
                        UniqueId         = el.UniqueId,
                        LastCosted       = DateTime.UtcNow,
                        RateSource       = rates.ContainsKey(categoryName) ? "CSV" : "Default",
                        RateConfidence   = rates.ContainsKey(categoryName) ? 70 : 40,
                        SortOrder        = sortOrder++
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlumbingBOQEnricher.{categoryName}: {ex.Message}"); }
        }

        private static void CollectGenericByFamily(Document doc, List<BOQLineItem> items,
            Dictionary<string, (double rate, string unit)> rates, double ugxPerUsd,
            HashSet<long> skip, string[] familyPrefixes, string category, string rateKey,
            double defaultUgx, string nrm2, string discipline)
        {
            try
            {
                var rateUgx = rates.TryGetValue(rateKey, out var r) ? r.rate : defaultUgx;
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType();
                int sortOrder = 0;
                foreach (var el in collector.OfType<FamilyInstance>())
                {
                    long rid = el.Id?.Value ?? -1;
                    if (rid > 0 && skip.Contains(rid)) continue;
                    var famName = el.Symbol?.Family?.Name ?? "";
                    bool match = false;
                    foreach (var prefix in familyPrefixes)
                        if (famName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                    if (!match) continue;
                    items.Add(new BOQLineItem
                    {
                        NRM2Section      = nrm2,
                        Category         = category,
                        Discipline       = discipline,
                        ItemName         = el.Name ?? category,
                        FamilyName       = famName,
                        TypeName         = el.Name ?? "",
                        Quantity         = 1,
                        Unit             = "each",
                        RateUGX          = rateUgx,
                        RateUSD          = ugxPerUsd > 0 ? Math.Round(rateUgx / ugxPerUsd, 2) : 0,
                        ResolvedNRM2Paragraph = $"{category} — {famName} ({el.Name})",
                        Source           = BOQRowSource.Model,
                        RevitElementId   = el.Id?.Value ?? -1,
                        UniqueId         = el.UniqueId,
                        LastCosted       = DateTime.UtcNow,
                        RateSource       = rates.ContainsKey(rateKey) ? "CSV" : "Default",
                        RateConfidence   = rates.ContainsKey(rateKey) ? 70 : 40,
                        SortOrder        = sortOrder++
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlumbingBOQEnricher.{category}: {ex.Message}"); }
        }

    }
}
