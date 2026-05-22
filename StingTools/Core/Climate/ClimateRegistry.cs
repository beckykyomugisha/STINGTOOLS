// StingTools — Climate Registry.
//
// Single source of truth for design-day climate data: cooling 0.4%/1%
// dry-bulb + coincident wet-bulb, heating 99.6%/99% dry-bulb, HDD,
// CDD and elevation. Replaces the hardcoded `airDensity = 1.20 kg/m³`
// assumption baked into earlier HVAC commands with a location-aware
// value derived from elevation + design temperature.
//
// Layered:
//   corporate baseline → Data/STING_CLIMATE_DATA.json
//   project override   → <project>/_BIM_COORD/climate_data.json
//
// Active site resolution priority:
//   1. PRJ_CLIMATE_SITE_ID set on ProjectInformation
//   2. ProjectInformation.Address fuzzy match against site labels
//   3. The single site in the override file (if present)
//   4. Hard fallback to "london"
//
// Sources:
//   ASHRAE Climate Data Center 2021, CIBSE Guide A 2015.
//   Air-density formula: ρ = (p₀ / (R · T)) · (1 - 0.0065·h/T)^5.2561
//   where p₀ = 101325 Pa, R = 287.05 J/(kg·K), T = design absolute
//   temperature (K), h = elevation (m). Yields the international
//   standard atmosphere correction (NASA ISA model).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Climate
{
    /// <summary>One climate design-data record per location.</summary>
    public class ClimateSite
    {
        public string Id            { get; set; } = "";
        public string Label         { get; set; } = "";
        public string Country       { get; set; } = "";
        public double Lat           { get; set; }
        public double Lon           { get; set; }
        public double ElevationM    { get; set; }
        /// <summary>Cooling design dry-bulb at 0.4% annual exceedance, °C.</summary>
        public double Cooling996DbC { get; set; }
        /// <summary>Mean coincident wet-bulb at the cooling design hour, °C.</summary>
        public double Cooling996McwbC { get; set; }
        /// <summary>Cooling design dry-bulb at 1% annual exceedance, °C (may be
        /// 0 when the data file only lists 0.4%; fall back to <see cref="Cooling996DbC"/>).</summary>
        public double Cooling99DbC  { get; set; }
        /// <summary>Cooling design dry-bulb at 2% annual exceedance, °C (may be 0).</summary>
        public double Cooling98DbC  { get; set; }
        /// <summary>Heating design dry-bulb at 99.6% annual exceedance, °C.</summary>
        public double Heating996DbC { get; set; }
        /// <summary>Heating design dry-bulb at 99% annual exceedance, °C (may be 0).</summary>
        public double Heating99DbC  { get; set; }

        /// <summary>Cooling design DB for the chosen exceedance band (0.4 / 1 / 2).</summary>
        public double CoolingDbCFor(double percentile)
        {
            if (Math.Abs(percentile - 0.4) < 0.05) return Cooling996DbC;
            if (Math.Abs(percentile - 1.0) < 0.1  && Cooling99DbC > 0) return Cooling99DbC;
            if (Math.Abs(percentile - 2.0) < 0.1  && Cooling98DbC > 0) return Cooling98DbC;
            return Cooling996DbC;
        }
        /// <summary>Heating design DB for the chosen exceedance band (99.6 / 99).</summary>
        public double HeatingDbCFor(double percentile)
        {
            if (Math.Abs(percentile - 99.0) < 0.1 && Heating99DbC != 0) return Heating99DbC;
            return Heating996DbC;
        }
        /// <summary>Annual heating degree-days, 18 °C base.</summary>
        public double Hdd18         { get; set; }
        /// <summary>Annual cooling degree-days, 10 °C base.</summary>
        public double Cdd10         { get; set; }
        public string Source        { get; set; } = "";

        /// <summary>Standard-time UTC offset, hours. London = 0, Paris = +1,
        /// New York = -5, Singapore = +8, etc. Used by BlockLoadEngine to
        /// convert local-clock hours into solar-time hours so solar noon
        /// aligns with the actual sun position (default solar geometry
        /// assumes hour-12 = solar noon).</summary>
        public double UtcOffsetHours { get; set; } = 0;

        /// <summary>True if the site observes Daylight Saving Time during the
        /// cooling design day (which is in July for the northern hemisphere
        /// and most southern-hemisphere sites are at design too in their
        /// summer). +1 h is added on top of UtcOffsetHours when applying the
        /// local→solar conversion.</summary>
        public bool ObservesDstInSummer { get; set; } = false;

        /// <summary>
        /// Air density at the cooling design dry-bulb, corrected for
        /// elevation per the NASA ISA model. Returns kg/m³.
        /// </summary>
        public double AirDensityCoolingKgM3()
        {
            double pa = StandardPressurePa(ElevationM);
            double t = Cooling996DbC + 273.15;
            return pa / (287.05 * t);
        }

        /// <summary>Air density at the heating design dry-bulb (kg/m³).</summary>
        public double AirDensityHeatingKgM3()
        {
            double pa = StandardPressurePa(ElevationM);
            double t = Heating996DbC + 273.15;
            return pa / (287.05 * t);
        }

        /// <summary>Standard pressure at elevation (Pa) per ISA.</summary>
        public static double StandardPressurePa(double elevationM)
        {
            double t0 = 288.15; // sea-level standard temperature K
            double l  = 0.0065; // troposphere lapse rate K/m
            double exp = 9.80665 * 0.0289644 / (8.31447 * l);
            return 101325.0 * Math.Pow(1.0 - l * elevationM / t0, exp);
        }
    }

    public class ClimateData
    {
        public List<ClimateSite> Sites { get; set; } = new();

        public ClimateSite ById(string id)
            => Sites.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Find a site whose id, label, or city-name token appears inside
        /// the supplied free-text fragment (typically the project address).
        /// Tokenises on whitespace + punctuation so a multi-line address
        /// like "10 Downing Street, London SW1A 2AA, UK" correctly resolves
        /// to the `london` site.
        /// </summary>
        public ClimateSite ByLabelContains(string fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment)) return null;
            string haystack = fragment.ToLowerInvariant();
            // Direct substring of site id (preferred — least ambiguous).
            foreach (var s in Sites)
            {
                if (haystack.Contains(s.Id.ToLowerInvariant())) return s;
            }
            // Then site label, splitting "London (Heathrow)" → ["london", "heathrow"]
            // and matching any token whole-word in the address.
            var separators = new[] { ' ', ',', '.', '(', ')', '/', '-', '\t', '\n', '\r' };
            var addrTokens = new HashSet<string>(
                haystack.Split(separators, StringSplitOptions.RemoveEmptyEntries));
            foreach (var s in Sites)
            {
                var labelTokens = s.Label.ToLowerInvariant()
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tok in labelTokens)
                {
                    if (tok.Length < 4) continue; // skip "the", "and", short noise
                    if (addrTokens.Contains(tok)) return s;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Loader / cache for climate data. Mirrors MepSizingRegistry
    /// (corporate baseline + project override + per-doc cache).
    /// </summary>
    public static class ClimateRegistry
    {
        public const string DataFileName = "STING_CLIMATE_DATA.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/climate_data.json";
        public const string ProjectInfoSiteParam = "PRJ_CLIMATE_SITE_ID";

        private static readonly ConcurrentDictionary<string, ClimateData> _cache
            = new ConcurrentDictionary<string, ClimateData>(StringComparer.OrdinalIgnoreCase);

        public static ClimateData Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        /// <summary>
        /// Resolve the active site for a document via (in order):
        ///   1. PRJ_CLIMATE_SITE_ID on ProjectInformation
        ///   2. ProjectInformation.Address contains a site label
        ///   3. First site in the project override
        ///   4. "london" hard fallback.
        /// </summary>
        public static ClimateSite ActiveSite(Document doc)
        {
            var data = Get(doc);
            ClimateSite site = null;
            try
            {
                if (doc?.ProjectInformation != null)
                {
                    string sid = ReadParam(doc.ProjectInformation, ProjectInfoSiteParam);
                    if (!string.IsNullOrWhiteSpace(sid))
                        site = data.ById(sid);
                    if (site == null)
                    {
                        string addr = doc.ProjectInformation.Address;
                        if (!string.IsNullOrWhiteSpace(addr))
                            site = data.ByLabelContains(addr);
                    }
                }
            }
            catch { /* fall through */ }

            return site
                ?? data.ById("london")
                ?? data.Sites.FirstOrDefault()
                ?? new ClimateSite { Id = "fallback", Label = "Fallback", Cooling996DbC = 28, Heating996DbC = -3, ElevationM = 0 };
        }

        private static string ReadParam(Element el, string name)
        {
            try { return el.LookupParameter(name)?.AsString() ?? ""; }
            catch { return ""; }
        }

        private static ClimateData Load(Document doc)
        {
            var data = new ClimateData();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), data);

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), data);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("ClimateRegistry.Load", ex);
            }
            return data;
        }

        private static void Apply(JObject j, ClimateData data)
        {
            var sites = j["sites"] as JArray;
            if (sites == null) return;
            foreach (var s in sites.OfType<JObject>())
            {
                var site = new ClimateSite
                {
                    Id              = (string)s["id"] ?? "",
                    Label           = (string)s["label"] ?? "",
                    Country         = (string)s["country"] ?? "",
                    Lat             = (double?)s["lat"] ?? 0,
                    Lon             = (double?)s["lon"] ?? 0,
                    ElevationM      = (double?)s["elevationM"] ?? 0,
                    Cooling996DbC   = (double?)s["cooling996DbC"] ?? 28,
                    Cooling996McwbC = (double?)s["cooling996McwbC"] ?? 20,
                    Cooling99DbC    = (double?)s["cooling99DbC"]  ?? 0,
                    Cooling98DbC    = (double?)s["cooling98DbC"]  ?? 0,
                    Heating996DbC   = (double?)s["heating996DbC"] ?? -3,
                    Heating99DbC    = (double?)s["heating99DbC"]  ?? 0,
                    Hdd18           = (double?)s["hdd18"] ?? 0,
                    Cdd10           = (double?)s["cdd10"] ?? 0,
                    Source          = (string)s["source"] ?? "",
                    UtcOffsetHours      = (double?)s["utcOffsetHours"] ?? 0,
                    ObservesDstInSummer = (bool?)s["observesDstInSummer"] ?? false
                };
                // Project override replaces an existing entry with the same id
                int existing = data.Sites.FindIndex(x => string.Equals(x.Id, site.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) data.Sites[existing] = site;
                else data.Sites.Add(site);
            }
        }
    }
}
