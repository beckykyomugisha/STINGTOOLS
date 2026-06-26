// StingTools — Green baseline registry + proxy resolver (Phase 195, spec §5).
//
// Loads STING_GREEN_BASELINES.json + optional project override (merged by key).
// Baselines key on CLIMATE ZONE, never country: country is the first lookup key
// but the fallback chain proxies on climate zone, not "nearest country". Every
// fallback hop is recorded in a visible proxy log (rule D2). No baseline number
// is ever invented in code — they all come from the JSON catalogue.
//
// Pure POCO — no Revit dependency. Has dedicated unit tests (resolution order +
// proxy-path correctness + project override merge).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class BaselineKey
    {
        public string Country     { get; set; } = "*";
        public string ClimateZone { get; set; } = "*";
        public string BuildingUse { get; set; } = "*";
    }

    public class EndUse
    {
        public double EuiKwhM2Yr { get; set; }   // cooling / fans / dhw
        public double LpdWm2      { get; set; }   // lighting
        public double EpdWm2      { get; set; }   // equipment
    }

    public class GreenBaseline
    {
        public BaselineKey Key { get; set; } = new BaselineKey();
        public string Source     { get; set; } = "";
        public string Provenance { get; set; } = "indicative";

        public string EnergyMethod { get; set; } = "endUseIntensity";
        public Dictionary<string, EndUse> EndUses { get; }
            = new Dictionary<string, EndUse>(StringComparer.OrdinalIgnoreCase);
        public double BaselineCoolingCop { get; set; } = 2.8;

        public Dictionary<string, double> FixtureBaselines { get; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Null = "the EDGE app owns the certified materials %".</summary>
        public double? EmbodiedEnergyBaselineMjM2 { get; set; }

        /// <summary>Total baseline energy EUI summed across end-uses, kWh/m2.yr.
        /// Lighting/equipment W/m2 are converted to annual kWh via annualHours.</summary>
        public double TotalEuiKwhM2Yr(double annualOperatingHours)
        {
            double sum = 0;
            foreach (var kv in EndUses)
            {
                var u = kv.Value;
                if (u.EuiKwhM2Yr > 0) sum += u.EuiKwhM2Yr;
                if (u.LpdWm2 > 0)     sum += u.LpdWm2 * annualOperatingHours / 1000.0;
                if (u.EpdWm2 > 0)     sum += u.EpdWm2 * annualOperatingHours / 1000.0;
            }
            return sum;
        }
    }

    public class GreenBaselineRegistry
    {
        private readonly List<GreenBaseline> _baselines = new List<GreenBaseline>();
        private readonly List<string> _resolutionOrder = new List<string>
        {
            "country+climateZone+buildingUse",
            "climateZone+buildingUse",
            "buildingUse",
            "global"
        };

        public IReadOnlyList<GreenBaseline> All => _baselines;
        public IReadOnlyList<string> ResolutionOrder => _resolutionOrder;

        public static GreenBaselineRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new GreenBaselineRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static GreenBaselineRegistry LoadFromFiles(string corporatePath, string projectPath)
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

            if (root["resolutionOrder"] is JArray order && order.Count > 0)
            {
                _resolutionOrder.Clear();
                _resolutionOrder.AddRange(order.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)));
            }

            var arr = root["baselines"] as JArray;
            if (arr == null) return;
            foreach (var b in arr.OfType<JObject>())
            {
                var bl = ParseBaseline(b);
                int existing = _baselines.FindIndex(x => SameKey(x.Key, bl.Key));
                if (existing >= 0) _baselines[existing] = bl;   // project override wins by exact key
                else _baselines.Add(bl);
            }
        }

        private static bool SameKey(BaselineKey a, BaselineKey b)
            => string.Equals(a.Country, b.Country, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.ClimateZone, b.ClimateZone, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.BuildingUse, b.BuildingUse, StringComparison.OrdinalIgnoreCase);

        private static GreenBaseline ParseBaseline(JObject b)
        {
            var bl = new GreenBaseline
            {
                Source     = (string)b["source"] ?? "",
                Provenance = (string)b["provenance"] ?? "indicative"
            };
            if (b["key"] is JObject k)
            {
                bl.Key.Country     = (string)k["country"] ?? "*";
                bl.Key.ClimateZone = (string)k["climateZone"] ?? "*";
                bl.Key.BuildingUse = (string)k["buildingUse"] ?? "*";
            }
            if (b["energy"] is JObject e)
            {
                bl.EnergyMethod = (string)e["method"] ?? "endUseIntensity";
                if (e["endUses"] is JObject eu)
                    foreach (var p in eu.Properties())
                    {
                        var v = p.Value as JObject;
                        if (v == null) continue;
                        bl.EndUses[p.Name] = new EndUse
                        {
                            EuiKwhM2Yr = (double?)v["eui_kwh_m2_yr"] ?? 0,
                            LpdWm2      = (double?)v["lpd_w_m2"] ?? 0,
                            EpdWm2      = (double?)v["epd_w_m2"] ?? 0
                        };
                    }
                if (e["baselineSystemCOP"] is JObject cop && cop["cooling"] != null)
                    bl.BaselineCoolingCop = (double?)cop["cooling"] ?? 2.8;
            }
            if (b["water"] is JObject w && w["fixtureBaselines"] is JObject fb)
                foreach (var p in fb.Properties())
                    bl.FixtureBaselines[p.Name] = (double?)p.Value ?? 0;
            if (b["materials"] is JObject m)
            {
                var v = m["embodiedEnergyBaseline_mj_m2"];
                bl.EmbodiedEnergyBaselineMjM2 = (v == null || v.Type == JTokenType.Null)
                    ? (double?)null : (double?)v;
            }
            return bl;
        }

        /// <summary>
        /// Resolve the best baseline for (country, climateZone, buildingUse),
        /// proxying on climate zone — NEVER on "nearest country". Returns the
        /// matched baseline plus a full ResolutionPath (which key matched, which
        /// hops were skipped). The path is rendered by the panel as the proxy log.
        /// </summary>
        public BaselineResolution Resolve(string country, string climateZone, string buildingUse)
        {
            country     = string.IsNullOrWhiteSpace(country) ? "*" : country;
            climateZone = string.IsNullOrWhiteSpace(climateZone) ? "*" : climateZone;
            buildingUse = string.IsNullOrWhiteSpace(buildingUse) ? "*" : buildingUse;

            var res = new BaselineResolution();

            foreach (var rule in _resolutionOrder)
            {
                BaselineKey want = BuildWantKey(rule, country, climateZone, buildingUse);
                var hit = _baselines.FirstOrDefault(x => Matches(x.Key, want));
                var hop = new ResolutionHop
                {
                    Key     = $"{rule} -> [{want.Country}/{want.ClimateZone}/{want.BuildingUse}]",
                    Matched  = hit != null,
                    Detail   = hit != null ? hit.Source : "no row"
                };
                res.Path.Add(hop);
                if (hit != null)
                {
                    res.Found       = true;
                    res.Baseline    = hit;
                    res.Source      = hit.Source;
                    res.Provenance  = hit.Provenance;
                    res.MatchedKey   = $"{hit.Key.Country}/{hit.Key.ClimateZone}/{hit.Key.BuildingUse}";
                    res.Summary      = BuildSummary(rule, country, climateZone, buildingUse, hit);
                    return res;
                }
            }

            // No catalogue row at all — never invent a number; surface the gap.
            res.Found = false;
            res.Source = "none";
            res.Provenance = "missing";
            res.Summary = $"No baseline catalogue row for {country}/{climateZone}/{buildingUse} — " +
                          "add one to the project override before a meaningful % can be reported.";
            return res;
        }

        private static BaselineKey BuildWantKey(string rule, string country, string zone, string use)
        {
            switch (rule)
            {
                case "country+climateZone+buildingUse":
                    return new BaselineKey { Country = country, ClimateZone = zone, BuildingUse = use };
                case "climateZone+buildingUse":
                    return new BaselineKey { Country = "*", ClimateZone = zone, BuildingUse = use };
                case "buildingUse":
                    return new BaselineKey { Country = "*", ClimateZone = "*", BuildingUse = use };
                case "global":
                default:
                    return new BaselineKey { Country = "*", ClimateZone = "*", BuildingUse = "*" };
            }
        }

        private static bool Matches(BaselineKey have, BaselineKey want)
            => string.Equals(have.Country, want.Country, StringComparison.OrdinalIgnoreCase)
            && string.Equals(have.ClimateZone, want.ClimateZone, StringComparison.OrdinalIgnoreCase)
            && string.Equals(have.BuildingUse, want.BuildingUse, StringComparison.OrdinalIgnoreCase);

        private static string BuildSummary(string rule, string country, string zone, string use, GreenBaseline hit)
        {
            if (rule == "country+climateZone+buildingUse")
                return $"exact match {country}/{zone}/{use}, source {hit.Source} — {hit.Provenance}";
            string fellBack;
            switch (rule)
            {
                case "climateZone+buildingUse":
                    fellBack = $"no {country} baseline -> fell back to climate-zone {zone} {use}"; break;
                case "buildingUse":
                    fellBack = $"no {country}/{zone} baseline -> fell back to {use} (any zone)"; break;
                default:
                    fellBack = $"no {country}/{zone}/{use} baseline -> fell back to global default"; break;
            }
            return $"{fellBack}, source {hit.Source} — {hit.Provenance}";
        }
    }
}
