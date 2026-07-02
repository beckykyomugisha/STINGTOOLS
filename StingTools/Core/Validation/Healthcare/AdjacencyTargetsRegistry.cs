// Healthcare Pack HC-DEF-08 — HBN adjacency-target registry.
//
// Makes HEALTHCARE_ADJACENCY_HBN.csv the live data source for the adjacency
// rules AdjacencyValidator enforces (previously the CSV was unused and the
// validator read the hardcoded HBNStandards.AdjacencyTargets). Corporate
// baseline + optional <project>/_BIM_COORD/adjacency_hbn.csv override, cached
// per project directory — mirroring RoomClassCodes / OwnerStandardsRegistry.
//
// The CSV keys are a MIX of department codes (ED / IMAGING / OR / PHARMACY …)
// and canonical room-class codes (HSDU-P / RECOV-1 / MAT-LDR / NICU …). The
// validator groups each room under BOTH its canonical code and its
// RoomClassCodes department, so either form of key resolves.

using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Standards.HBN;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Core.Validation.Healthcare
{
    public class AdjacencyTarget
    {
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public int Target { get; set; }          // 0 forbidden, 1 preferred, 2 mandatory
        public string SourceRef { get; set; } = "";
    }

    public static class AdjacencyTargetsRegistry
    {
        private const string CorporateFileName = "HEALTHCARE_ADJACENCY_HBN.csv";
        private const string ProjectFileName = "adjacency_hbn.csv";

        private static readonly ConcurrentDictionary<string, List<AdjacencyTarget>> _cache
            = new ConcurrentDictionary<string, List<AdjacencyTarget>>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        public static IReadOnlyList<AdjacencyTarget> Get(Document doc) =>
            _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        private static List<AdjacencyTarget> Load(Document doc)
        {
            var list = new List<AdjacencyTarget>();

            // Corporate baseline.
            try
            {
                string corp = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    ParseInto(File.ReadAllLines(corp), list);
            }
            catch (Exception ex) { StingLog.Warn($"AdjacencyTargetsRegistry corporate load: {ex.Message}"); }

            // Project override (append; project rows extend the corporate set).
            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string proj = Path.Combine(dir, "_BIM_COORD", ProjectFileName);
                    if (File.Exists(proj))
                    {
                        ParseInto(File.ReadAllLines(proj), list);
                        StingLog.Info($"AdjacencyTargetsRegistry: project overlay loaded from {proj}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"AdjacencyTargetsRegistry overlay load: {ex.Message}"); }

            // Fallback to the compiled table if the CSV is missing/empty, so the
            // validator never silently loses its rules.
            if (list.Count == 0)
            {
                foreach (var kv in HBNStandards.AdjacencyTargets)
                    list.Add(new AdjacencyTarget { A = kv.Key.Item1, B = kv.Key.Item2, Target = kv.Value, SourceRef = "HBNStandards" });
                StingLog.Warn("AdjacencyTargetsRegistry: HEALTHCARE_ADJACENCY_HBN.csv missing/empty — " +
                              "falling back to compiled HBNStandards.AdjacencyTargets.");
            }
            return list;
        }

        // Header: RoomClassA,RoomClassB,Target,SourceRef,Notes
        private static void ParseInto(string[] lines, List<AdjacencyTarget> into)
        {
            bool header = true;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                if (header) { header = false; continue; }   // skip the column header row
                var f = StingToolsApp.ParseCsvLine(raw);
                if (f == null || f.Length < 3) continue;
                if (!int.TryParse(f[2].Trim(), out var target)) continue;
                into.Add(new AdjacencyTarget
                {
                    A = f[0].Trim(),
                    B = f[1].Trim(),
                    Target = target,
                    SourceRef = f.Length > 3 ? f[3].Trim() : "",
                });
            }
        }
    }
}
