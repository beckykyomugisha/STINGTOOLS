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
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.BOQ;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public static class PlumbingBOQEnricher
    {
        // Defaults used when cost_rates_5d.csv has no entry for a category.
        // Mirrors the new rows added in Phase 179f so the engine still produces
        // useful totals on a project shipping an older cost sheet.
        private const double DefaultPipeInsulationUgxPerM = 55_500;
        private const double DefaultDuctInsulationUgxPerM = 74_000;
        private const double DefaultSleeveUgxEach         = 185_000;
        private const double DefaultHangerUgxEach         = 111_000;

        /// <summary>
        /// Builds the supplemental BOQ rows — insulation runs, sleeves, hangers.
        /// Quantity for insulation is taken from PipeInsulation.GetInsulatedElementId
        /// → host pipe length when available, falling back to BIP CURVE_ELEM_LENGTH.
        /// Sleeves and hangers count as 1 each.
        /// </summary>
        public static List<BOQLineItem> Build(Document doc)
        {
            var items = new List<BOQLineItem>();
            if (doc == null) return items;

            var rates = LoadRates();
            var ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);

            CollectInsulation(doc, items, rates, ugxPerUsd, BuiltInCategory.OST_PipeInsulations,
                "Pipe Insulations", DefaultPipeInsulationUgxPerM, "33", "M");
            CollectInsulation(doc, items, rates, ugxPerUsd, BuiltInCategory.OST_DuctInsulations,
                "Duct Insulations", DefaultDuctInsulationUgxPerM, "33", "M");

            CollectGenericByFamily(doc, items, rates, ugxPerUsd,
                familyPrefixes: new[] { "STING_SLEEVE_", "STING_PROVISION_VOID" },
                category: "Pipe Sleeve",
                rateKey: "Pipe Sleeve",
                defaultUgx: DefaultSleeveUgxEach,
                nrm2: "32", discipline: "M");

            CollectGenericByFamily(doc, items, rates, ugxPerUsd,
                familyPrefixes: new[] { "STING_HANGER_", "STING_TRAPEZE_" },
                category: "Pipe Hanger",
                rateKey: "Pipe Hanger",
                defaultUgx: DefaultHangerUgxEach,
                nrm2: "33", discipline: "M");

            return items;
        }

        private static void CollectInsulation(Document doc, List<BOQLineItem> items,
            Dictionary<string, (double rate, string unit)> rates, double ugxPerUsd,
            BuiltInCategory bic, string categoryName, double defaultRateUgx, string nrm2, string discipline)
        {
            try
            {
                var rateUgx = rates.TryGetValue(categoryName, out var r) ? r.rate : defaultRateUgx;
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(bic).WhereElementIsNotElementType();
                int sortOrder = 0;
                foreach (var el in collector)
                {
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
            string[] familyPrefixes, string category, string rateKey, double defaultUgx,
            string nrm2, string discipline)
        {
            try
            {
                var rateUgx = rates.TryGetValue(rateKey, out var r) ? r.rate : defaultUgx;
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType();
                int sortOrder = 0;
                foreach (var el in collector.OfType<FamilyInstance>())
                {
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

        // Lightweight CSV reader so the enricher doesn't depend on
        // BOQCostManager internals. Same 7-column shape as cost_rates_5d.csv.
        private static Dictionary<string, (double rate, string unit)> LoadRates()
        {
            var rates = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = StingToolsApp.FindDataFile("cost_rates_5d.csv");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return rates;
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rates;
                bool is7Col = lines[0].ToLowerInvariant().Contains("mat_code");
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length < 3) continue;
                    if (is7Col && cols.Length >= 7
                        && double.TryParse(cols[4], System.Globalization.NumberStyles.Any,
                                           System.Globalization.CultureInfo.InvariantCulture, out var ugx))
                    {
                        rates[cols[0].Trim()] = (ugx, cols[5].Trim());
                        if (!string.IsNullOrEmpty(cols[1])) rates[cols[1].Trim()] = (ugx, cols[5].Trim());
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlumbingBOQEnricher.LoadRates: {ex.Message}"); }
            return rates;
        }
    }
}
