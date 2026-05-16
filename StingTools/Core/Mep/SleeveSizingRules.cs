// StingTools v4 MVP — Phase I sleeve-sizing rules loader.
//
// Loads STING_SLEEVE_RULES.json and resolves which rule applies to
// a given MEP element. Rules are evaluated in file order with later
// more-specific matches winning (e.g. PIPE_LARGEBORE overrides
// PIPE_DEFAULT when diameter ≥ 250 mm).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public class SleeveSizingRule
    {
        public string Id                { get; set; } = "";
        public string ElementCategory   { get; set; } = "";
        public double? DiameterGeMm     { get; set; }
        public string ShapeHint         { get; set; } = "";
        public double ClearanceMm       { get; set; } = 50.0;
        public bool   IncludeInsulation { get; set; } = true;
        public string Shape             { get; set; } = "round";
        public double MinBoreMm         { get; set; } = 25.0;
    }

    public static class SleeveSizingRules
    {
        private static readonly object _lock = new object();
        private static List<SleeveSizingRule> _rules;
        private static bool _loaded;
        // Memoise resolved rules by (category, dia-bucket, shape) so the
        // engine doesn't re-scan the rule list for every MEP curve. Bucket
        // by 5 mm steps to keep the cache bounded.
        private static readonly Dictionary<string, SleeveSizingRule> _resolveCache
            = new Dictionary<string, SleeveSizingRule>();

        public static List<SleeveSizingRule> All
        {
            get { EnsureLoaded(); return _rules ?? new List<SleeveSizingRule>(); }
        }

        public static void Reload()
        {
            lock (_lock) { _loaded = false; _resolveCache.Clear(); }
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _rules = Load();
                _loaded = true;
            }
        }

        private static List<SleeveSizingRule> Load()
        {
            var list = new List<SleeveSizingRule>();
            try
            {
                var path = Core.StingToolsApp.FindDataFile("Routing/STING_SLEEVE_RULES.json")
                        ?? Core.StingToolsApp.FindDataFile("STING_SLEEVE_RULES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("SleeveSizingRules: STING_SLEEVE_RULES.json not found; using empty list");
                    return list;
                }
                var root = JObject.Parse(File.ReadAllText(path));
                var arr = root["rules"] as JArray;
                if (arr == null) return list;
                foreach (var item in arr)
                {
                    try
                    {
                        var r = new SleeveSizingRule
                        {
                            Id               = item.Value<string>("id") ?? "",
                            ElementCategory  = item.Value<string>("element_category") ?? "",
                            DiameterGeMm     = item["diameter_ge_mm"]?.ToObject<double?>(),
                            ShapeHint        = item.Value<string>("shape_hint") ?? "",
                            ClearanceMm      = item.Value<double?>("clearance_mm") ?? 50.0,
                            IncludeInsulation = item.Value<bool?>("include_insulation") ?? true,
                            Shape            = item.Value<string>("shape") ?? "round",
                            MinBoreMm        = item.Value<double?>("min_bore_mm") ?? 25.0,
                        };
                        list.Add(r);
                    }
                    catch (Exception ex)
                    { StingLog.Warn($"SleeveSizingRules: rule parse failed: {ex.Message}"); }
                }
                StingLog.Info($"SleeveSizingRules: loaded {list.Count} rules from {path}");
            }
            catch (Exception ex)
            { StingLog.Warn($"SleeveSizingRules: load failed: {ex.Message}"); }
            return list;
        }

        public static SleeveSizingRule Resolve(Element el)
        {
            if (el?.Category == null) return null;
            string catName = el.Category.Name ?? "";
            BuiltInCategory bic;
            try { bic = (BuiltInCategory)el.Category.Id.Value; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }

            string shortCat;
            switch (bic)
            {
                case BuiltInCategory.OST_PipeCurves:     shortCat = "PipeCurves";      break;
                case BuiltInCategory.OST_FlexPipeCurves: shortCat = "FlexPipeCurves";  break;
                case BuiltInCategory.OST_DuctCurves:     shortCat = "DuctCurves";      break;
                case BuiltInCategory.OST_FlexDuctCurves: shortCat = "FlexDuctCurves";  break;
                case BuiltInCategory.OST_Conduit:        shortCat = "Conduit";         break;
                case BuiltInCategory.OST_CableTray:      shortCat = "CableTray";       break;
                default: shortCat = catName.Replace(" ", "");                          break;
            }

            double diaMm = ProbeDiameterMm(el);
            string shape = ProbeShape(el);
            // Bucket diameter to the nearest 5 mm so 27 mm and 28 mm hit
            // the same cache entry but the 250 mm large-bore threshold
            // still sorts correctly.
            int diaBucket = (int)Math.Round(diaMm / 5.0) * 5;
            string cacheKey = $"{shortCat}|{diaBucket}|{shape}";

            lock (_lock)
            {
                if (_resolveCache.TryGetValue(cacheKey, out var hit)) return hit;
            }

            SleeveSizingRule best = null;
            foreach (var r in All)
            {
                if (!string.Equals(r.ElementCategory, shortCat, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(r.ShapeHint) &&
                    !string.Equals(r.ShapeHint, shape, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.DiameterGeMm.HasValue && diaMm < r.DiameterGeMm.Value) continue;
                best = r;
            }
            lock (_lock) { _resolveCache[cacheKey] = best; }
            return best;
        }

        private static double ProbeDiameterMm(Element el)
        {
            try
            {
                if (el is Autodesk.Revit.DB.Plumbing.Pipe p) return p.Diameter * 304.8;
                if (el is Autodesk.Revit.DB.Mechanical.Duct d)
                {
                    double sz = Math.Max(d.Width, d.Height);
                    if (sz <= 0) try { sz = d.Diameter; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    return sz * 304.8;
                }
                if (el is Autodesk.Revit.DB.Electrical.Conduit c)    return c.Diameter * 304.8;
                if (el is Autodesk.Revit.DB.Electrical.CableTray ct) return ct.Width    * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static string ProbeShape(Element el)
        {
            try
            {
                if (el is Autodesk.Revit.DB.Mechanical.Duct d)
                    return (d.Width > 0 && d.Height > 0) ? "rectangular" : "round";
                if (el is Autodesk.Revit.DB.Electrical.CableTray) return "rectangular";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "round";
        }
    }
}
