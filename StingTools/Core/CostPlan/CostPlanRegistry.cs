// ══════════════════════════════════════════════════════════════════════════
//  CostPlanRegistry.cs — Loads STING_NRM1_BENCHMARKS.csv.
//
//  Schema:
//    BuildingType, ElementCode, ElementName, Unit, LowRate, LikelyRate,
//    HighRate, Source
//
//  Project overrides at <project>/_BIM_COORD/nrm1_benchmarks.csv take
//  precedence over corporate baseline by (BuildingType, ElementCode) key.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.BIMManager;

namespace StingTools.Core.CostPlan
{
    public sealed class CostPlanRegistry
    {
        private static readonly ConcurrentDictionary<string, CostPlanRegistry> _cache
            = new ConcurrentDictionary<string, CostPlanRegistry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>All benchmarks indexed by (buildingType ↓ elementCode).</summary>
        public Dictionary<string, Dictionary<string, CostPlanLine>> ByBuildingType { get; }

        public IReadOnlyList<string> BuildingTypes { get; }

        private CostPlanRegistry(Dictionary<string, Dictionary<string, CostPlanLine>> bench)
        {
            ByBuildingType = bench;
            BuildingTypes = bench.Keys.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static CostPlanRegistry Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Invalidate() => _cache.Clear();

        /// <summary>
        /// Return all benchmark lines for a building type. Empty list if
        /// the building type is unknown — caller surfaces the available
        /// types from <see cref="BuildingTypes"/>.
        /// </summary>
        public List<CostPlanLine> LinesFor(string buildingType)
        {
            if (string.IsNullOrEmpty(buildingType)) return new List<CostPlanLine>();
            if (!ByBuildingType.TryGetValue(buildingType, out var byElement))
                return new List<CostPlanLine>();
            return byElement.Values
                .OrderBy(l => l.ElementCode, new NrmCodeComparer())
                .Select(Clone)
                .ToList();
        }

        private static CostPlanLine Clone(CostPlanLine src) => new CostPlanLine
        {
            ElementCode = src.ElementCode,
            ElementName = src.ElementName,
            BuildingType = src.BuildingType,
            Unit = src.Unit,
            LowRate = src.LowRate,
            LikelyRate = src.LikelyRate,
            HighRate = src.HighRate,
            Source = src.Source,
            Note = src.Note
        };

        private static CostPlanRegistry Load(Document doc)
        {
            var bench = new Dictionary<string, Dictionary<string, CostPlanLine>>(
                StringComparer.OrdinalIgnoreCase);

            // Corporate baseline.
            string corp = StingToolsApp.FindDataFile("STING_NRM1_BENCHMARKS.csv");
            LoadFile(corp, bench, source: "corporate");

            // Project override — same shape, prepended priority.
            string projectPath = ResolveProjectOverridePath(doc);
            if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
                LoadFile(projectPath, bench, source: "project");

            return new CostPlanRegistry(bench);
        }

        private static void LoadFile(string path,
            Dictionary<string, Dictionary<string, CostPlanLine>> bench, string source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                bool headerSeen = false;
                int loaded = 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string trim = raw.TrimStart();
                    if (trim.StartsWith("#")) continue;
                    var cols = StingToolsApp.ParseCsvLine(raw);
                    if (cols == null || cols.Length < 7) continue;
                    if (!headerSeen)
                    {
                        headerSeen = true;
                        if (cols[0].Equals("BuildingType", StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    string buildingType = cols[0].Trim();
                    string elementCode = cols[1].Trim();
                    string elementName = cols[2].Trim();
                    string unit = cols[3].Trim();
                    if (!double.TryParse(cols[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double low)) continue;
                    if (!double.TryParse(cols[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double likely)) continue;
                    if (!double.TryParse(cols[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double high)) continue;
                    string srcLabel = cols.Length > 7 ? cols[7].Trim() : source;

                    if (!bench.TryGetValue(buildingType, out var byElement))
                    {
                        byElement = new Dictionary<string, CostPlanLine>(StringComparer.OrdinalIgnoreCase);
                        bench[buildingType] = byElement;
                    }
                    byElement[elementCode] = new CostPlanLine
                    {
                        BuildingType = buildingType,
                        ElementCode = elementCode,
                        ElementName = elementName,
                        Unit = unit,
                        LowRate = low,
                        LikelyRate = likely,
                        HighRate = high,
                        Source = srcLabel
                    };
                    loaded++;
                }
                StingLog.Info($"CostPlanRegistry loaded {loaded} benchmarks from {Path.GetFileName(path)} ({source}).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CostPlanRegistry.LoadFile({Path.GetFileName(path ?? "")}): {ex.Message}");
            }
        }

        private static string ResolveProjectOverridePath(Document doc)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "nrm1_benchmarks.csv");
            }
            catch { return null; }
        }

        /// <summary>NRM1 code sort: lexicographic by dotted segments (1, 2, 2.1, 2.2, 5.10).</summary>
        private sealed class NrmCodeComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null || y == null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                var xs = x.Split('.');
                var ys = y.Split('.');
                for (int i = 0; i < Math.Min(xs.Length, ys.Length); i++)
                {
                    bool xi = int.TryParse(xs[i], out int xv);
                    bool yi = int.TryParse(ys[i], out int yv);
                    if (xi && yi) { int c = xv.CompareTo(yv); if (c != 0) return c; }
                    else
                    {
                        int c = string.Compare(xs[i], ys[i], StringComparison.OrdinalIgnoreCase);
                        if (c != 0) return c;
                    }
                }
                return xs.Length.CompareTo(ys.Length);
            }
        }
    }
}
