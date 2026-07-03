// StingTools — Per-country grid carbon-factor registry (WS I3 / I9).
//
// Operational carbon used a hardcoded ~0.45 kgCO2e/kWh because the country was
// unset — wildly wrong for hydro-dominant grids (CAR, Uganda, Norway ≈ 0.03-0.06).
// This resolves the grid factor per ISO country code from a documented seed (Ember
// / IEA) + a project override, and flags whether a real factor or the labelled
// default was used.
//
// DATA, not code: the corporate seed is Data/STING_GRID_CARBON_FACTORS.json; the
// project override is <project>/_BIM_COORD/sustainability/grid_carbon_factors.json.
// No factor is hardcoded in this file — an absent dataset yields the JSON default
// (flagged), never an invented number.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class GridCarbonResolution
    {
        public string Country { get; set; } = "";
        public double Factor  { get; set; }
        public string Source  { get; set; } = "";
        /// <summary>True when the country wasn't found and the labelled default was
        /// used (the dashboard/export must say "default factor").</summary>
        public bool   IsDefault { get; set; }
    }

    public class GridCarbonRegistry
    {
        private readonly Dictionary<string, (double f, string src)> _byCountry
            = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        private double _default = 0.45;
        private string _defaultSource = "global average placeholder";

        public double Default => _default;

        public GridCarbonResolution Resolve(string country)
        {
            string c = (country ?? "").Trim();
            if (c.Length > 0 && c != "*" && _byCountry.TryGetValue(c, out var hit))
                return new GridCarbonResolution { Country = c, Factor = hit.f, Source = hit.src, IsDefault = false };
            return new GridCarbonResolution
            {
                Country = c, Factor = _default,
                Source = _defaultSource + (string.IsNullOrEmpty(c) || c == "*" ? " (country unset)" : $" (no row for {c})"),
                IsDefault = true
            };
        }

        public static GridCarbonRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new GridCarbonRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static GridCarbonRegistry LoadFromFiles(string corporatePath, string projectPath)
            => LoadFromJson(SafeRead(corporatePath), SafeRead(projectPath));

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) { SustainOverrideHealth.Report("GridCarbon", $"read failed for {path}: {ex.Message}"); return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch (Exception ex) { SustainOverrideHealth.Report("GridCarbon", $"malformed override/data JSON: {ex.Message}"); return; }
            if (root["default"] != null) _default = (double)root["default"];
            if (root["defaultSource"] != null) _defaultSource = (string)root["defaultSource"] ?? _defaultSource;
            var arr = root["factors"] as JArray;
            if (arr == null) return;
            foreach (var f in arr.OfType<JObject>())
            {
                string c = (string)f["country"];
                if (string.IsNullOrWhiteSpace(c)) continue;
                _byCountry[c.Trim()] = ((double?)f["kgco2ePerKwh"] ?? _default, (string)f["source"] ?? "");
            }
        }
    }
}
