// StingTools — Sustainability registry facade (Phase 195, Revit-facing).
//
// Resolves the corporate-baseline data-file paths + the per-project _BIM_COORD
// overrides and hands them to the pure-POCO registry loaders. Caches per
// document path (mirrors ClimateRegistry / MepSizingRegistry). Invalidated on
// document close from StingToolsApp.OnDocumentClosing.
//
// This file IS Revit-facing (takes a Document) — it is NOT linked into the test
// project. The engines + registries it wraps are all Revit-free.

using System;
using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core.Sustainability
{
    public static class SustainabilityRegistries
    {
        public const string SchemesFile   = "STING_GREEN_SCHEMES.json";
        public const string BaselinesFile = "STING_GREEN_BASELINES.json";
        public const string WaterFile     = "STING_WATER_USAGE_PROFILES.json";
        public const string MeasuresFile  = "STING_GREEN_MEASURES.json";
        public const string MonthlyFile   = "STING_CLIMATE_MONTHLY.json";

        private static readonly ConcurrentDictionary<string, GreenSchemeRegistry> _schemes
            = new ConcurrentDictionary<string, GreenSchemeRegistry>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, GreenBaselineRegistry> _baselines
            = new ConcurrentDictionary<string, GreenBaselineRegistry>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, WaterUsageProfileRegistry> _water
            = new ConcurrentDictionary<string, WaterUsageProfileRegistry>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, GreenMeasureRegistry> _measures
            = new ConcurrentDictionary<string, GreenMeasureRegistry>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ClimateMonthlyRegistry> _monthly
            = new ConcurrentDictionary<string, ClimateMonthlyRegistry>(StringComparer.OrdinalIgnoreCase);

        public static GreenSchemeRegistry Schemes(Document doc)
            => _schemes.GetOrAdd(Key(doc), _ =>
                GreenSchemeRegistry.LoadFromFiles(Corp(SchemesFile), Proj(doc, "green_schemes.json")));

        public static GreenBaselineRegistry Baselines(Document doc)
            => _baselines.GetOrAdd(Key(doc), _ =>
                GreenBaselineRegistry.LoadFromFiles(Corp(BaselinesFile), Proj(doc, "green_baselines.json")));

        public static WaterUsageProfileRegistry WaterProfiles(Document doc)
            => _water.GetOrAdd(Key(doc), _ =>
                WaterUsageProfileRegistry.LoadFromFiles(Corp(WaterFile), Proj(doc, "water_usage_profiles.json")));

        public static GreenMeasureRegistry Measures(Document doc)
            => _measures.GetOrAdd(Key(doc), _ =>
                GreenMeasureRegistry.LoadFromFiles(Corp(MeasuresFile), Proj(doc, "green_measures.json")));

        public static ClimateMonthlyRegistry Monthly(Document doc)
            => _monthly.GetOrAdd(Key(doc), _ =>
                ClimateMonthlyRegistry.LoadFromFiles(Corp(MonthlyFile), Proj(doc, "climate_monthly.json")));

        public static void Reload()
        {
            _schemes.Clear(); _baselines.Clear(); _water.Clear(); _measures.Clear(); _monthly.Clear();
        }

        public static void Reload(Document doc)
        {
            string k = Key(doc);
            _schemes.TryRemove(k, out _);
            _baselines.TryRemove(k, out _);
            _water.TryRemove(k, out _);
            _measures.TryRemove(k, out _);
            _monthly.TryRemove(k, out _);
        }

        // ── Path helpers ─────────────────────────────────────────────────

        private static string Key(Document doc) => doc?.PathName ?? "<no-doc>";

        private static string Corp(string fileName)
        {
            try { return StingTools.Core.StingToolsApp.FindDataFile(fileName); }
            catch { return null; }
        }

        /// <summary>Per-project override path under _BIM_COORD/sustainability/.</summary>
        public static string Proj(Document doc, string fileName)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                string dir = Path.GetDirectoryName(doc.PathName) ?? "";
                return Path.Combine(dir, "_BIM_COORD", "sustainability", fileName);
            }
            catch { return null; }
        }

        /// <summary>Project directory (the folder the .rvt lives in), or null.</summary>
        public static string ProjectDir(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                return Path.GetDirectoryName(doc.PathName);
            }
            catch { return null; }
        }
    }
}
