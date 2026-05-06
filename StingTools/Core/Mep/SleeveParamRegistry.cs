// StingTools v4 — Sleeve parameter registry (shared by SleeveEngine,
// AutoSleevePlacementCommand, ExportSleeveBcfCommand).
//
// Single source of truth for:
//   1. The insulation parameter names searched on MEP elements when sizing
//      sleeves (loaded from Data/Routing/MEP_INSULATION_PARAMS.json with a
//      hard-coded fallback so the engine still works on stock projects).
//   2. The STING_SLEEVE_* parameter names written by both placement
//      commands. Both commands now write the same set so BCF / IFC export
//      sees a single sleeve population.
//
// Keep STING_SLEEVE_* in sync with PARAMETER_REGISTRY.json `sleeve`
// container group.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Mep
{
    public static class SleeveParamRegistry
    {
        // ── Sleeve instance parameter names ───────────────────────────────────
        public const string PfvUuid          = "STING_SLEEVE_PFV_UUID";
        public const string BoreMm           = "STING_SLEEVE_BORE_MM";
        public const string WidthMm          = "STING_SLEEVE_WIDTH_MM";
        public const string HeightMm         = "STING_SLEEVE_HEIGHT_MM";
        public const string DepthMm          = "STING_SLEEVE_DEPTH_MM";
        public const string HostFireRating   = "STING_SLEEVE_HOST_FIRE_RATING";
        public const string RuleId           = "STING_SLEEVE_RULE_ID";
        public const string UlSystem         = "STING_SLEEVE_UL_SYS";
        public const string HostElementId    = "STING_SLEEVE_HOST_ID";
        public const string PenetratedId     = "STING_SLEEVE_PENETRATED_ID";
        public const string CreatedBy        = "STING_SLEEVE_CREATED_BY";

        // ── Insulation probe ──────────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static List<string> _insulationParams;
        private static bool _loaded;

        /// <summary>
        /// Ordered list of parameter names probed on MEP elements when
        /// computing sleeve clearance. First match wins.
        /// </summary>
        public static IReadOnlyList<string> InsulationParams
        {
            get { EnsureLoaded(); return _insulationParams; }
        }

        public static void Reload() { lock (_lock) { _loaded = false; } EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _insulationParams = LoadFromJson() ?? Defaults();
                _loaded = true;
            }
        }

        private static List<string> LoadFromJson()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("Routing/MEP_INSULATION_PARAMS.json")
                        ?? StingToolsApp.FindDataFile("MEP_INSULATION_PARAMS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var root = JObject.Parse(File.ReadAllText(path));
                var arr = root["params"] as JArray;
                if (arr == null) return null;
                var list = new List<string>();
                foreach (var v in arr)
                {
                    var name = v?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                }
                StingLog.Info($"SleeveParamRegistry: loaded {list.Count} insulation params from {path}");
                return list.Count > 0 ? list : null;
            }
            catch (Exception ex)
            { StingLog.Warn($"SleeveParamRegistry: insulation params load failed: {ex.Message}"); return null; }
        }

        private static List<string> Defaults() => new List<string>
        {
            "PLM_PPE_INSULATION_THK_MM",
            "HVC_DCT_INSULATION_THK_MM",
            "PIPE_INSULATION_THK_MM",
            "DUCT_INSULATION_THK_MM",
            "Insulation Thickness",
        };

        /// <summary>
        /// Probe an MEP element for an insulation thickness in millimetres.
        /// Returns 0 when no probed parameter is present. Sets
        /// <paramref name="paramFound"/> to the parameter name that matched
        /// (or null if no match) so callers can warn when a sizing rule
        /// expects insulation but none is recorded.
        /// </summary>
        public static double ProbeInsulationMm(Element el, out string paramFound)
        {
            paramFound = null;
            if (el == null) return 0;
            const double FtToMm = 304.8;
            try
            {
                foreach (var name in InsulationParams)
                {
                    var p = el.LookupParameter(name);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.Double)
                    {
                        paramFound = name;
                        var v = p.AsDouble();
                        // Heuristic: Revit length params are stored in feet,
                        // some custom params in mm. If value < 0.5 we treat
                        // it as feet (≈ 150 mm) and convert.
                        return v < 0.5 ? v * FtToMm : v;
                    }
                    if (p.StorageType == StorageType.String)
                    {
                        if (double.TryParse(p.AsString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var v))
                        { paramFound = name; return v; }
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}
