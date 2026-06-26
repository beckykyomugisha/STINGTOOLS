// StingTools — Climate monthly registry (Phase 195, spec §3).
//
// Extends the 41-city design-day ClimateRegistry with, per site: 12 monthly
// mean dry-bulb degC, 12 monthly mean RH %, 12 monthly GHI kWh/m2.day, annual
// GHI, monthly rainfall mm and a default grid carbon factor. Energy, water and
// PV-yield all read from here so a project's location is set once.
//
// Sites lacking a monthly record fall back to a synthesised profile derived
// from their design-day values, and the synthesis is flagged (FellBackToDesignDay).
//
// Pure POCO — no Revit dependency. The corporate baseline is
// Data/STING_CLIMATE_MONTHLY.json; the project override is
// <project>/_BIM_COORD/sustainability/climate_monthly.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class ClimateMonthlySite
    {
        public string Id          { get; set; } = "";
        public string Label       { get; set; } = "";
        public string Country     { get; set; } = "";
        public string ClimateZone { get; set; } = "";
        public double[] MeanDbC      { get; set; } = new double[12];
        public double[] MeanRhPct    { get; set; } = new double[12];
        public double[] GhiKwhM2Day  { get; set; } = new double[12];
        public double   AnnualGhiKwhM2Yr { get; set; }
        public double[] RainfallMm   { get; set; } = new double[12];
        public double   GridCarbonKgco2eKwh { get; set; } = 0.45;
        public string   Source      { get; set; } = "";
        /// <summary>True when this record was synthesised from a design-day site
        /// because no monthly record existed (spec §3 fallback + logged warning).</summary>
        public bool FellBackToDesignDay { get; set; }

        public double AnnualRainfallMm => RainfallMm?.Sum() ?? 0;
        public double MeanAnnualDbC    => (MeanDbC != null && MeanDbC.Length == 12) ? MeanDbC.Average() : 0;
    }

    public class ClimateMonthlyRegistry
    {
        private readonly Dictionary<string, ClimateMonthlySite> _sites
            = new Dictionary<string, ClimateMonthlySite>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyCollection<ClimateMonthlySite> All => _sites.Values;

        public ClimateMonthlySite Get(string id)
            => (!string.IsNullOrEmpty(id) && _sites.TryGetValue(id, out var s)) ? s : null;

        public static ClimateMonthlyRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new ClimateMonthlyRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static ClimateMonthlyRegistry LoadFromFiles(string corporatePath, string projectPath)
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
            var arr = root["sites"] as JArray;
            if (arr == null) return;
            foreach (var s in arr.OfType<JObject>())
            {
                var site = ParseSite(s);
                _sites[site.Id] = site;   // project override replaces by id
            }
        }

        private static ClimateMonthlySite ParseSite(JObject s)
        {
            return new ClimateMonthlySite
            {
                Id          = (string)s["id"] ?? "",
                Label       = (string)s["label"] ?? "",
                Country     = (string)s["country"] ?? "",
                ClimateZone = (string)s["climateZone"] ?? "",
                MeanDbC      = Arr12(s["meanDbC"]),
                MeanRhPct    = Arr12(s["meanRhPct"]),
                GhiKwhM2Day  = Arr12(s["ghiKwhM2Day"]),
                AnnualGhiKwhM2Yr = (double?)s["annualGhiKwhM2Yr"] ?? 0,
                RainfallMm   = Arr12(s["rainfallMm"]),
                GridCarbonKgco2eKwh = (double?)s["gridCarbonKgco2eKwh"] ?? 0.45,
                Source       = (string)s["source"] ?? ""
            };
        }

        private static double[] Arr12(JToken t)
        {
            var a = new double[12];
            if (t is JArray arr)
                for (int i = 0; i < 12 && i < arr.Count; i++)
                    a[i] = (double?)arr[i] ?? 0;
            return a;
        }

        /// <summary>
        /// Resolve a monthly site by id, or synthesise one from design-day values
        /// when no monthly record exists. The synthesis is flagged + logged.
        /// </summary>
        public ClimateMonthlySite ResolveOrSynthesise(
            string id, string label, double coolingDesignDbC, double heatingDesignDbC,
            double annualGhiKwhM2YrFallback = 1400, double annualRainfallMmFallback = 1000,
            string climateZone = "")
        {
            var hit = Get(id);
            if (hit != null) return hit;

            _warnings.Add($"No monthly climate record for site '{id}' ({label}); " +
                          "synthesised from design-day values (sustainability estimates indicative).");

            // Crude sinusoid between heating-design (coldest month) and a mean
            // half-way to cooling-design (mean of the warmest month). This is a
            // graceful fallback for any of the 41 design-day sites without
            // monthly data; teams add a real monthly row for certified runs.
            double warmMean = (coolingDesignDbC + heatingDesignDbC) / 2.0
                              + 0.30 * (coolingDesignDbC - heatingDesignDbC);
            double coldMean = (coolingDesignDbC + heatingDesignDbC) / 2.0
                              - 0.30 * (coolingDesignDbC - heatingDesignDbC);
            double mid  = (warmMean + coldMean) / 2.0;
            double amp  = (warmMean - coldMean) / 2.0;

            var site = new ClimateMonthlySite
            {
                Id = id, Label = label, ClimateZone = climateZone,
                AnnualGhiKwhM2Yr = annualGhiKwhM2YrFallback,
                GridCarbonKgco2eKwh = 0.45,
                Source = "synthesised from design-day",
                FellBackToDesignDay = true
            };
            for (int m = 0; m < 12; m++)
            {
                // Peak warmth ~ month 7 (Aug) for N-hemisphere convention.
                double phase = Math.Cos((m - 6) / 12.0 * 2 * Math.PI);
                site.MeanDbC[m]     = mid + amp * phase;
                site.MeanRhPct[m]   = 65;
                site.GhiKwhM2Day[m] = annualGhiKwhM2YrFallback / 365.0;
                site.RainfallMm[m]  = annualRainfallMmFallback / 12.0;
            }
            return site;
        }
    }
}
