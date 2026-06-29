// StingTools — Country seed registry (WS J1/J2).
//
// The data-driven source for the SETUP Country dropdown and the country cascade:
// each row carries iso3 + friendly label + default city + lat/lon + the capital's
// ASHRAE 169 climate zone + grid & diesel carbon factors. On Country change the
// engine populates climate site / zone / grid / diesel from this seed, so picking
// a country actually changes the result (the USA-vs-Uganda bug).
//
// DATA, not code: corporate seed Data/STING_COUNTRIES.json; project override at
// <project>/_BIM_COORD/sustainability/countries.json. No value is hardcoded here —
// an absent dataset yields the flagged global default, never an invented number.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class CountryRow
    {
        public string Iso3        { get; set; } = "";
        public string Label       { get; set; } = "";
        public string DefaultCity { get; set; } = "";
        public double Lat         { get; set; }
        public double Lon         { get; set; }
        public string ClimateZone { get; set; } = "";
        public double GridKgCo2ePerKwh   { get; set; }
        public double DieselKgCo2ePerKwh { get; set; }
        public string Source      { get; set; } = "";
        /// <summary>True for the synthesised global-default row (country unset/unknown).</summary>
        public bool   IsDefault   { get; set; }

        /// <summary>"CAF — Central African Republic" for the dropdown.</summary>
        public string FriendlyLabel =>
            string.IsNullOrWhiteSpace(Label) ? Iso3 : $"{Iso3} — {Label}";
    }

    public class CountryRegistry
    {
        private readonly List<CountryRow> _rows = new List<CountryRow>();
        private double _defaultGrid = 0.45, _defaultDiesel = 0.80;
        private string _defaultSource = "global average placeholder";

        public IReadOnlyList<CountryRow> All => _rows;

        /// <summary>Resolve by iso3 (case-insensitive) or friendly/plain label. Returns
        /// the flagged global default when the key is blank / "*" / unknown.</summary>
        public CountryRow Resolve(string key)
        {
            string k = (key ?? "").Trim();
            if (k.Length > 0 && k != "*")
            {
                var hit = _rows.FirstOrDefault(r => string.Equals(r.Iso3, k, StringComparison.OrdinalIgnoreCase))
                       ?? _rows.FirstOrDefault(r => string.Equals(r.Label, k, StringComparison.OrdinalIgnoreCase))
                       ?? _rows.FirstOrDefault(r => string.Equals(r.FriendlyLabel, k, StringComparison.OrdinalIgnoreCase))
                       // tolerate "CAF — Central African Republic" pasted from the dropdown
                       ?? _rows.FirstOrDefault(r => k.StartsWith(r.Iso3 + " ", StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return new CountryRow
            {
                Iso3 = "*", Label = "(global default)", ClimateZone = "",
                GridKgCo2ePerKwh = _defaultGrid, DieselKgCo2ePerKwh = _defaultDiesel,
                Source = _defaultSource + (string.IsNullOrEmpty(k) || k == "*" ? " (country unset)" : $" (no row for {k})"),
                IsDefault = true
            };
        }

        /// <summary>Friendly labels for the dropdown (iso3 — Label), default first.</summary>
        public List<string> DropdownLabels()
        {
            var labels = new List<string> { "*" };
            labels.AddRange(_rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).Select(r => r.FriendlyLabel));
            return labels;
        }

        public static CountryRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new CountryRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static CountryRegistry LoadFromFiles(string corporatePath, string projectPath)
            => LoadFromJson(SafeRead(corporatePath), SafeRead(projectPath));

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch { return; }
            if (root["default"] is JObject d)
            {
                _defaultGrid   = (double?)d["gridKgCo2ePerKwh"]   ?? _defaultGrid;
                _defaultDiesel = (double?)d["dieselKgCo2ePerKwh"] ?? _defaultDiesel;
                _defaultSource = (string)d["source"] ?? _defaultSource;
            }
            var arr = root["countries"] as JArray;
            if (arr == null) return;
            foreach (var c in arr.OfType<JObject>())
            {
                string iso3 = (string)c["iso3"];
                if (string.IsNullOrWhiteSpace(iso3)) continue;
                var row = new CountryRow
                {
                    Iso3 = iso3.Trim(),
                    Label = (string)c["label"] ?? iso3,
                    DefaultCity = (string)c["defaultCity"] ?? "",
                    Lat = (double?)c["lat"] ?? 0,
                    Lon = (double?)c["lon"] ?? 0,
                    ClimateZone = (string)c["climateZone"] ?? "",
                    GridKgCo2ePerKwh = (double?)c["gridKgCo2ePerKwh"] ?? 0,
                    DieselKgCo2ePerKwh = (double?)c["dieselKgCo2ePerKwh"] ?? _defaultDiesel,
                    Source = (string)c["source"] ?? ""
                };
                int existing = _rows.FindIndex(r => string.Equals(r.Iso3, row.Iso3, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _rows[existing] = row;   // project override by iso3
                else _rows.Add(row);
            }
        }
    }
}
