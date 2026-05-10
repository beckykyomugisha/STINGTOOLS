// PlumbingTables — JSON table loaders for the Phase 179a data files.
//
// All data is read from `data/Plumbing/STING_PLUMBING_*.json` next to the
// assembly at first access. Callers receive POCO snapshots; never edit
// the cached objects in place.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class FixtureUnitRow
    {
        public string Key             { get; set; } = "";
        public string DisplayName     { get; set; } = "";
        public double Du              { get; set; }
        public double LuCw            { get; set; }
        public double LuHw            { get; set; }
        public double Wsfu            { get; set; }
        public int    TrapMm          { get; set; }
        public List<string> CategoryHints { get; set; } = new List<string>();
    }

    public class MaterialHydraulic
    {
        public string Key             { get; set; } = "";
        public string DisplayName     { get; set; } = "";
        public List<string> Service   { get; set; } = new List<string>();
        public double HwC             { get; set; } = 130;
        public double ManningN        { get; set; } = 0.012;
        public double RoughnessMm     { get; set; } = 0.05;
        public double TempMaxC        { get; set; } = 100;
        public double PresMaxBar      { get; set; } = 16;
        public Dictionary<string, double> VelocityMaxMps        { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> HangerSpacingHorizMm  { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> HangerSpacingVertMm   { get; set; } = new Dictionary<string, double>();
    }

    public static class PlumbingTables
    {
        private static readonly object _lock = new object();
        private static JObject _drainage;
        private static JObject _supply;
        private static List<MaterialHydraulic> _materials;
        private static List<FixtureUnitRow> _fixtureUnits;

        public static JObject Drainage     { get { EnsureLoaded(); return _drainage; } }
        public static JObject Supply       { get { EnsureLoaded(); return _supply;   } }
        public static IReadOnlyList<MaterialHydraulic> Materials       { get { EnsureLoaded(); return _materials; } }
        public static IReadOnlyList<FixtureUnitRow>    FixtureUnits    { get { EnsureLoaded(); return _fixtureUnits; } }

        public static void Reload() { lock (_lock) { _drainage = _supply = null; _materials = null; _fixtureUnits = null; EnsureLoaded(); } }

        private static void EnsureLoaded()
        {
            if (_drainage != null && _supply != null && _materials != null && _fixtureUnits != null) return;
            lock (_lock)
            {
                if (_drainage == null) _drainage = LoadJson("STING_PLUMBING_DRAINAGE_TABLES.json");
                if (_supply   == null) _supply   = LoadJson("STING_PLUMBING_SUPPLY_TABLES.json");

                if (_fixtureUnits == null)
                {
                    _fixtureUnits = new List<FixtureUnitRow>();
                    try
                    {
                        var arr = _drainage?["fixtureUnits"] as JArray;
                        if (arr != null)
                            foreach (var t in arr)
                                _fixtureUnits.Add(t.ToObject<FixtureUnitRow>());
                    }
                    catch (Exception ex) { StingLog.Warn($"PlumbingTables.fixtureUnits: {ex.Message}"); }
                }

                if (_materials == null)
                {
                    _materials = new List<MaterialHydraulic>();
                    try
                    {
                        var matJson = LoadJson("STING_PIPE_MATERIALS_HYDRAULIC.json");
                        var arr = matJson?["materials"] as JArray;
                        if (arr != null)
                            foreach (var t in arr)
                                _materials.Add(t.ToObject<MaterialHydraulic>());
                    }
                    catch (Exception ex) { StingLog.Warn($"PlumbingTables.materials: {ex.Message}"); }
                }
            }
        }

        private static JObject LoadJson(string fileName)
        {
            try
            {
                var path = StingToolsApp.FindDataFile(fileName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    var fallback = Path.Combine(StingToolsApp.DataPath ?? "", "Plumbing", fileName);
                    if (File.Exists(fallback)) path = fallback;
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn($"PlumbingTables: data file '{fileName}' not found");
                    return new JObject();
                }
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Error($"PlumbingTables.LoadJson({fileName})", ex);
                return new JObject();
            }
        }

        // ── Lookup helpers ──
        public static FixtureUnitRow MatchFixtureByName(string familyOrTypeName)
        {
            if (string.IsNullOrWhiteSpace(familyOrTypeName)) return null;
            var upper = familyOrTypeName.ToUpperInvariant();
            EnsureLoaded();
            // First pass: exact key match.
            foreach (var row in _fixtureUnits)
                if (string.Equals(row.Key, familyOrTypeName, StringComparison.OrdinalIgnoreCase)) return row;
            // Second pass: hint substrings.
            foreach (var row in _fixtureUnits)
                foreach (var hint in row.CategoryHints ?? new List<string>())
                    if (!string.IsNullOrEmpty(hint) && upper.Contains(hint.ToUpperInvariant()))
                        return row;
            return null;
        }

        public static MaterialHydraulic GetMaterial(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            return _materials?.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static double KFactorFor(string buildingType)
        {
            EnsureLoaded();
            try
            {
                var k = _drainage?["kFactors"]?[buildingType ?? ""];
                if (k != null) return k.Value<double>();
            }
            catch { }
            return 0.7;
        }

        public static double LuToQdLps(double luTotal)
        {
            EnsureLoaded();
            try
            {
                var arr = _supply?["luToQdLpsBsEn806"] as JArray;
                if (arr == null || arr.Count == 0) return 0;
                double prevLu = 0, prevQ = 0;
                foreach (var row in arr)
                {
                    double lu = row.Value<double>("luTotal");
                    double q  = row.Value<double>("qdLps");
                    if (luTotal <= lu)
                    {
                        if (lu == prevLu) return q;
                        double frac = (luTotal - prevLu) / (lu - prevLu);
                        return prevQ + frac * (q - prevQ);
                    }
                    prevLu = lu; prevQ = q;
                }
                return prevQ;
            }
            catch { return 0; }
        }

        public static double WsfuToGpm(double wsfu, bool flushValveMajority)
        {
            EnsureLoaded();
            try
            {
                var arr = _supply?["wsfuToGpmHunter"] as JArray;
                if (arr == null || arr.Count == 0) return 0;
                string col = flushValveMajority ? "gpmFlush" : "gpmTank";
                double prevW = 0, prevG = 0;
                foreach (var row in arr)
                {
                    double w = row.Value<double>("wsfu");
                    double g = row.Value<double>(col);
                    if (wsfu <= w)
                    {
                        if (w == prevW) return g;
                        double frac = (wsfu - prevW) / (w - prevW);
                        return prevG + frac * (g - prevG);
                    }
                    prevW = w; prevG = g;
                }
                return prevG;
            }
            catch { return 0; }
        }

        public static int DrainageBranchSizeMm(double duCumulative)
        {
            EnsureLoaded();
            try
            {
                var arr = _drainage?["branchSizeBsEn"] as JArray;
                if (arr == null) return 100;
                foreach (var row in arr)
                    if (duCumulative <= row.Value<double>("duMax"))
                        return row.Value<int>("dnMm");
                return 125;
            }
            catch { return 100; }
        }

        public static double StackCapacityDu(int dnMm)
        {
            EnsureLoaded();
            try
            {
                var arr = _drainage?["stackCapacity"] as JArray;
                if (arr == null) return 0;
                double last = 0;
                foreach (var row in arr)
                {
                    if (row.Value<int>("dnMm") == dnMm) return row.Value<double>("duMax");
                    last = row.Value<double>("duMax");
                }
                return last;
            }
            catch { return 0; }
        }

        public static int VentSizeBsEnMm(int drainDnMm)
        {
            EnsureLoaded();
            try
            {
                var arr = _drainage?["ventSizeBsEn"] as JArray;
                if (arr == null) return drainDnMm / 2;
                foreach (var row in arr)
                    if (row.Value<int>("drainDnMm") == drainDnMm) return row.Value<int>("ventDnMm");
            }
            catch { }
            return drainDnMm / 2;
        }

        public static double TrapArmMaxLengthM(int dnMm)
        {
            EnsureLoaded();
            try
            {
                var arr = _drainage?["trapArmMaxLengthM"] as JArray;
                if (arr == null) return 5.0;
                double last = 5.0;
                foreach (var row in arr)
                {
                    if (row.Value<int>("dnMm") == dnMm) return row.Value<double>("lengthM");
                    last = row.Value<double>("lengthM");
                }
                return last;
            }
            catch { return 5.0; }
        }

        public static string FluidCategoryForFixture(string fixtureKey)
        {
            EnsureLoaded();
            try
            {
                var t = _supply?["fluidCategoryByFixture"]?[fixtureKey ?? ""];
                if (t != null) return t.Value<string>();
            }
            catch { }
            return "Cat2";
        }

        public static double MinPressureBarFor(string fixtureKey)
        {
            EnsureLoaded();
            try
            {
                var t = _supply?["minPressureBar"]?[fixtureKey ?? ""];
                if (t != null) return t.Value<double>();
            }
            catch { }
            return 0.5;
        }
    }
}
