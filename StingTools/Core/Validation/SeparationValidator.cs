// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Validation/SeparationValidator.cs — S4.8 (R-E5).
//
// Validates BS EN 50174-2 power / data cable separations plus the
// HTM 02-01, BS 5839-1, BS 6891, BS EN 12056, BS 7671 equivalents for
// other disciplines. Rule set is loaded from Data_SeparationRules.json
// (S4.9) so project teams can override or extend per job without
// recompiling.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public sealed class SeparationRule
    {
        public string Id { get; set; } = string.Empty;
        public string SourceService { get; set; } = string.Empty;
        public string TargetService { get; set; } = string.Empty;
        public string Geometry { get; set; } = "parallel";
        public double MinSeparationMm { get; set; }
        public string Rationale { get; set; } = string.Empty;
        public bool? BothEnclosedMetal { get; set; }
        public bool? ShareContainment { get; set; }
    }

    public static class SeparationValidator
    {
        private static List<SeparationRule> _cache;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Load rules from Data/Routing/STING_SEPARATION_RULES.json
        /// alongside the plug-in DLL. Cached for the lifetime of the
        /// process; call <see cref="Reload"/> to force re-read.
        /// </summary>
        public static IReadOnlyList<SeparationRule> LoadRules()
        {
            lock (_cacheLock)
            {
                if (_cache != null) return _cache;
                _cache = TryLoad() ?? new List<SeparationRule>();
                return _cache;
            }
        }

        public static void Reload()
        {
            lock (_cacheLock) { _cache = null; }
        }

        private static List<SeparationRule> TryLoad()
        {
            try
            {
                string dir = Path.GetDirectoryName(
                    typeof(SeparationValidator).Assembly.Location) ?? string.Empty;
                string path = Path.Combine(dir, "Data", "Routing",
                    "STING_SEPARATION_RULES.json");
                if (!File.Exists(path))
                {
                    StingTools.Core.StingLog.Warn(
                        $"SeparationValidator: rules file missing at {path}");
                    return new List<SeparationRule>();
                }
                var json = JObject.Parse(File.ReadAllText(path));
                var list = new List<SeparationRule>();
                foreach (var tok in json["rules"] ?? new JArray())
                {
                    list.Add(new SeparationRule
                    {
                        Id               = (string)tok["id"] ?? string.Empty,
                        SourceService    = (string)tok["source_service"] ?? string.Empty,
                        TargetService    = (string)tok["target_service"] ?? string.Empty,
                        Geometry         = (string)tok["geometry"] ?? "parallel",
                        MinSeparationMm  = (double?)tok["min_separation_mm"] ?? 0.0,
                        Rationale        = (string)tok["rationale"] ?? string.Empty,
                        BothEnclosedMetal = (bool?)tok["both_enclosed_metal"],
                        ShareContainment  = (bool?)tok["share_containment"],
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("SeparationValidator.TryLoad failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Validate that <paramref name="source"/> (presumed to be a
        /// cable or duct with a known STING service token) satisfies
        /// every applicable rule against nearby elements within a
        /// reasonable search radius (2 m by default).
        /// </summary>
        public static List<ValidationResult> ValidateElement(
            Document doc, Element source, double searchRadiusFt = 6.56)
        {
            var results = new List<ValidationResult>();
            if (doc == null || source == null) return results;
            var rules = LoadRules();
            if (rules.Count == 0) return results;

            string srcSvc = ReadService(source);
            if (string.IsNullOrEmpty(srcSvc)) return results;

            var srcBb = source.get_BoundingBox(null);
            if (srcBb == null) return results;

            var outline = new Outline(
                new XYZ(srcBb.Min.X - searchRadiusFt, srcBb.Min.Y - searchRadiusFt, srcBb.Min.Z - searchRadiusFt),
                new XYZ(srcBb.Max.X + searchRadiusFt, srcBb.Max.Y + searchRadiusFt, srcBb.Max.Z + searchRadiusFt));
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

            // Pre-filter with the global category enum set — N-G1 style.
            var candidates = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(bboxFilter)
                .Where(e => e.Id != source.Id);

            foreach (var other in candidates)
            {
                string tgtSvc = ReadService(other);
                if (string.IsNullOrEmpty(tgtSvc)) continue;
                foreach (var r in rules)
                {
                    if (!Match(r.SourceService, srcSvc) || !Match(r.TargetService, tgtSvc)) continue;
                    double actualMm = ClosestSeparationMm(source, other);
                    if (actualMm + 1e-6 < r.MinSeparationMm)
                    {
                        results.Add(new ValidationResult(
                            source.Id,
                            ValidationSeverity.Error,
                            r.Id,
                            $"BS EN 50174-2 / project rule {r.Id}: {srcSvc} vs {tgtSvc} measured {actualMm:F0} mm < {r.MinSeparationMm:F0} mm required. {r.Rationale}",
                            "SeparationValidator"));
                    }
                }
            }
            return results;
        }

        private static bool Match(string rulePattern, string actual)
            => rulePattern == "*" || string.Equals(rulePattern, actual, StringComparison.OrdinalIgnoreCase);

        private static string ReadService(Element el)
        {
            var p = el.LookupParameter("STING_SERVICE_TOKEN");
            if (p != null && p.HasValue) return p.AsString() ?? string.Empty;
            var sys = el.LookupParameter("System Type") ?? el.LookupParameter("System Classification");
            return sys?.AsString() ?? string.Empty;
        }

        private static double ClosestSeparationMm(Element a, Element b)
        {
            var ba = a.get_BoundingBox(null);
            var bb = b.get_BoundingBox(null);
            if (ba == null || bb == null) return double.PositiveInfinity;
            double dx = Math.Max(0, Math.Max(ba.Min.X - bb.Max.X, bb.Min.X - ba.Max.X));
            double dy = Math.Max(0, Math.Max(ba.Min.Y - bb.Max.Y, bb.Min.Y - ba.Max.Y));
            double dz = Math.Max(0, Math.Max(ba.Min.Z - bb.Max.Z, bb.Min.Z - ba.Max.Z));
            double ft = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return ft * 304.8;
        }
    }
}
