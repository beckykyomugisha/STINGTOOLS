// ══════════════════════════════════════════════════════════════════════════
//  MaterialPriceBookProvider.cs — one editable price book, every project.
//
//  WHY
//
//  The material libraries (BLE_MATERIALS.csv, MEP_MATERIALS.csv) carry
//  MAT_COST_UNIT_UGX and MAT_COST_UNIT_USD, and across all 1,279 rows the UGX
//  column is exactly USD x 3700 — it is DERIVED, at a frozen exchange rate,
//  with no unit column anywhere. So the library cannot answer "what does this
//  cost today, per what?" and repricing means editing 1,279 rows.
//
//  This provider reads a price book instead: corporate baseline at
//  Data/STING_MATERIAL_PRICE_BOOK.json, project override at
//  <project>/_BIM_COORD/material_price_book.json, merged by materialName so a
//  project can reprice one material without forking the file. Prices are held
//  in ONE base currency with the FX rates beside them; change the rate once and
//  every material reprices, instead of editing prices to chase an exchange rate.
//
//  Priority 93: above the CSV category rate (90) — a curated material price
//  beats a category average — and below the per-element override (100), because
//  a value the operator typed on the element still wins.
//
//  Every entry carries unitOfMeasure, quotedOn and source. Those three are what
//  make a bill defensible: without a unit, UGX 2,220 and UGX 96,200 for two
//  block materials cannot be told apart; without a date, nobody knows whether a
//  rate is a quotation or a guess from two years ago.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ.Rates.Providers
{
    public sealed class MaterialPriceBookProvider : IRateProvider
    {
        public string Id => "material-price-book";
        public int Priority => 93;
        public bool RequiresNetwork => false;

        private readonly Dictionary<string, PriceEntry> _byMaterial;
        private readonly string _baseCurrency;
        private readonly Dictionary<string, double> _fx;   // units of X per 1 base

        private MaterialPriceBookProvider(Dictionary<string, PriceEntry> byMaterial,
                                          string baseCurrency,
                                          Dictionary<string, double> fx)
        {
            _byMaterial = byMaterial;
            _baseCurrency = string.IsNullOrWhiteSpace(baseCurrency) ? "USD" : baseCurrency.Trim();
            _fx = fx ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Corporate baseline, then project override merged over it by materialName.</summary>
        public static MaterialPriceBookProvider Load(Document doc)
        {
            var map = new Dictionary<string, PriceEntry>(StringComparer.OrdinalIgnoreCase);
            string baseCcy = "USD";
            var fx = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            void Absorb(string path, string label)
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    var book = JsonConvert.DeserializeObject<PriceBook>(File.ReadAllText(path));
                    if (book == null) return;
                    if (!string.IsNullOrWhiteSpace(book.BaseCurrency)) baseCcy = book.BaseCurrency.Trim();
                    // The fx table carries a "_note" alongside the numbers, so it is
                    // read loosely: anything that is not a positive number is skipped
                    // rather than throwing and losing the whole book. This is the
                    // Newtonsoft trap the memory rule names — a typed
                    // Dictionary<string,double> here would fail the entire load on
                    // one annotation key.
                    if (book.Fx != null)
                    {
                        foreach (var kv in book.Fx)
                        {
                            if (kv.Key != null && kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                            if (kv.Value == null) continue;
                            if (double.TryParse(System.Convert.ToString(kv.Value,
                                    System.Globalization.CultureInfo.InvariantCulture),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double v) && v > 0)
                                fx[kv.Key] = v;
                        }
                    }

                    int n = 0;
                    foreach (var p in book.Prices ?? new List<PriceEntry>())
                    {
                        if (string.IsNullOrWhiteSpace(p.MaterialName) || p.Rate <= 0) continue;
                        // Unit is REQUIRED. An entry without one prices an unknown
                        // quantity, which is worse than not pricing it — skip and say so.
                        if (string.IsNullOrWhiteSpace(p.UnitOfMeasure))
                        {
                            StingLog.Warn($"MaterialPriceBook ({label}): '{p.MaterialName}' has no unitOfMeasure — skipped. "
                                        + "A rate with no unit cannot be checked against a quantity.");
                            continue;
                        }
                        map[p.MaterialName.Trim()] = p;   // project override replaces baseline by key
                        n++;
                    }
                    StingLog.Info($"MaterialPriceBook: {n} price(s) from {label} ({Path.GetFileName(path)}).");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"MaterialPriceBook ({label}) load failed: {ex.Message}");
                }
            }

            Absorb(StingToolsApp.FindDataFile("STING_MATERIAL_PRICE_BOOK.json"), "corporate baseline");
            try { Absorb(StingPaths.MetaFile(doc, "_BIM_COORD", "material_price_book.json"), "project override"); }
            catch (Exception ex) { StingLog.Warn($"MaterialPriceBook: project override path: {ex.Message}"); }

            return new MaterialPriceBookProvider(map, baseCcy, fx);
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || _byMaterial.Count == 0) return null;

            string name = ResolveMaterialName(req);
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (!_byMaterial.TryGetValue(name.Trim(), out var p)) return null;

            // Convert into the caller's currency here rather than handing back a
            // foreign number and hoping. The registry's own adapter also converts,
            // but only between the currencies it knows; the book carries its own
            // table so a project working in KES or TZS is not silently mispriced.
            string want = string.IsNullOrWhiteSpace(req.CurrencyCode) ? _baseCurrency : req.CurrencyCode.Trim();
            string have = string.IsNullOrWhiteSpace(p.Currency) ? _baseCurrency : p.Currency.Trim();
            double rate = p.Rate;
            if (!string.Equals(have, want, StringComparison.OrdinalIgnoreCase))
            {
                double? converted = Convert(rate, have, want);
                if (converted == null)
                {
                    StingLog.WarnRateLimited("PriceBookFx",
                        $"MaterialPriceBook: no FX path {have}->{want} for '{name}' — returning the rate in {have}. "
                      + "Add the currency to the book's fx table.");
                    want = have;
                }
                else rate = converted.Value;
            }

            var lookup = new RateLookup
            {
                ResolutionLevel = RateResolutionLevel.Material,
                UnitRate = rate,
                CurrencyCode = want,
                Unit = p.UnitOfMeasure,
                SourceId = Id,
                Confidence = Priority,
                MatchedKey = name,
                Provenance = BuildProvenance(p),
            };
            if (p.LabourFraction > 0 && p.LabourFraction < 1)
            {
                lookup.LabourRate = rate * p.LabourFraction;
                lookup.MaterialRate = rate * (1 - p.LabourFraction);
            }
            return lookup;
        }

        /// <summary>
        /// Provenance is the point of the book. A priced bill that cannot say where
        /// a number came from, and when, is an estimate wearing a quotation's clothes.
        /// </summary>
        private static string BuildProvenance(PriceEntry p)
        {
            var bits = new List<string> { "Material price book" };
            if (!string.IsNullOrWhiteSpace(p.Source)) bits.Add(p.Source);
            if (!string.IsNullOrWhiteSpace(p.QuotedOn)) bits.Add($"quoted {p.QuotedOn}");
            if (!string.IsNullOrWhiteSpace(p.ValidUntil)) bits.Add($"valid to {p.ValidUntil}");
            if (!string.IsNullOrWhiteSpace(p.Region)) bits.Add(p.Region);
            return string.Join(" · ", bits);
        }

        private double? Convert(double amount, string from, string to)
        {
            // fx holds units of X per 1 base currency.
            double fromPerBase = string.Equals(from, _baseCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1.0 : (_fx.TryGetValue(from, out double f) ? f : 0);
            double toPerBase = string.Equals(to, _baseCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1.0 : (_fx.TryGetValue(to, out double t) ? t : 0);
            if (fromPerBase <= 0 || toPerBase <= 0) return null;
            return amount / fromPerBase * toPerBase;
        }

        /// <summary>
        /// The book keys on the exact Material.Name — unique across all 1,279 library
        /// rows, so a safe primary key. Prefer an explicit request field, then the
        /// element's material parameter, then its first painted/structural material.
        /// </summary>
        private static string ResolveMaterialName(RateRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.MaterialName)) return req.MaterialName;

            Element el = req.Element;
            if (el == null) return null;
            try
            {
                foreach (string pn in new[] { "MAT_NAME_TXT", "ASS_MATERIAL_TXT", "MAT_MATERIAL_TXT" })
                {
                    string v = ParameterHelpers.GetString(el, pn);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }

                ICollection<ElementId> mats = el.GetMaterialIds(false);
                if (mats != null)
                {
                    foreach (ElementId id in mats)
                    {
                        var m = el.Document?.GetElement(id) as Material;
                        if (!string.IsNullOrWhiteSpace(m?.Name)) return m.Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("PriceBookMat", $"MaterialPriceBook: material name: {ex.Message}"); }
            return null;
        }

        // ── file shape ────────────────────────────────────────────────────────
        private class PriceBook
        {
            [JsonProperty("schemaVersion")] public string SchemaVersion { get; set; }
            [JsonProperty("baseCurrency")] public string BaseCurrency { get; set; }
            [JsonProperty("fx")] public Dictionary<string, object> Fx { get; set; }
            [JsonProperty("prices")] public List<PriceEntry> Prices { get; set; }
        }

        internal class PriceEntry
        {
            [JsonProperty("materialName")] public string MaterialName { get; set; }
            [JsonProperty("rate")] public double Rate { get; set; }
            [JsonProperty("currency")] public string Currency { get; set; }
            [JsonProperty("unitOfMeasure")] public string UnitOfMeasure { get; set; }
            [JsonProperty("labourFraction")] public double LabourFraction { get; set; }
            [JsonProperty("source")] public string Source { get; set; }
            [JsonProperty("quotedOn")] public string QuotedOn { get; set; }
            [JsonProperty("validUntil")] public string ValidUntil { get; set; }
            [JsonProperty("region")] public string Region { get; set; }
            [JsonProperty("note")] public string Note { get; set; }
        }
    }
}
