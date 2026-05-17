// FixtureUnitScanner — Phase 179b consultant-grade scanner.
//
// Builds a per-fixture-type histogram with DU / LU / WSFU columns by
// scanning OST_PlumbingFixtures and matching each instance against the
// PlumbingTables fixture-unit registry. Writes PLM_DRN_DU_NR /
// PLM_SUP_LU_CW_NR / PLM_SUP_LU_HW_NR / PLM_SUP_WSFU_NR per element.
//
// The legacy FixtureUnitAggregator stays — it builds the per-pipe DFU
// graph for DrainageSizer. This scanner is the read-side companion that
// the SUPPLY / DRAINAGE DataGrids consume.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class FixtureScanRow
    {
        public string FixtureKey       { get; set; } = "";
        public string DisplayName      { get; set; } = "";
        public int    Count            { get; set; }
        public double TotalDu          { get; set; }
        public double TotalLuCw        { get; set; }
        public double TotalLuHw        { get; set; }
        public double TotalWsfu        { get; set; }
        public List<ElementId> Ids     { get; } = new List<ElementId>();
    }

    public class FixtureScanResult
    {
        public List<FixtureScanRow> Rows { get; } = new List<FixtureScanRow>();
        public int FixturesScanned { get; set; }
        public int FixturesUnmatched { get; set; }
        public int FixturesWritten { get; set; }
        public double SumDu       { get; set; }
        public double SumLuCw     { get; set; }
        public double SumLuHw     { get; set; }
        public double SumWsfu     { get; set; }
        public double QdCwBsEnLps { get; set; }
        public double QdHwBsEnLps { get; set; }
        public double QdHunterGpm { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class FixtureUnitScanner
    {
        public static FixtureScanResult Scan(Document doc, bool writeBack, bool flushValveMajority = false)
        {
            var r = new FixtureScanResult();
            if (doc == null) return r;

            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            r.FixturesScanned = fixtures.Count;

            var rowsByKey = new Dictionary<string, FixtureScanRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in fixtures)
            {
                var name = ExtractName(el);
                var match = PlumbingTables.MatchFixtureByName(name);
                if (match == null)
                {
                    r.FixturesUnmatched++;
                    continue;
                }
                if (!rowsByKey.TryGetValue(match.Key, out var row))
                {
                    row = new FixtureScanRow { FixtureKey = match.Key, DisplayName = match.DisplayName };
                    rowsByKey[match.Key] = row;
                }
                row.Count++;
                row.TotalDu   += match.Du;
                row.TotalLuCw += match.LuCw;
                row.TotalLuHw += match.LuHw;
                row.TotalWsfu += match.Wsfu;
                row.Ids.Add(el.Id);

                if (writeBack)
                {
                    try
                    {
                        TryWriteDouble(el, ParamRegistry.PLM_DRN_DU,    match.Du);
                        TryWriteDouble(el, ParamRegistry.PLM_SUP_LU_CW, match.LuCw);
                        TryWriteDouble(el, ParamRegistry.PLM_SUP_LU_HW, match.LuHw);
                        TryWriteDouble(el, ParamRegistry.PLM_SUP_WSFU,  match.Wsfu);
                        r.FixturesWritten++;
                    }
                    catch (Exception ex) { r.Warnings.Add($"writeback {el.Id}: {ex.Message}"); }
                }
            }

            r.Rows.AddRange(rowsByKey.Values.OrderByDescending(x => x.Count));
            r.SumDu   = r.Rows.Sum(x => x.TotalDu);
            r.SumLuCw = r.Rows.Sum(x => x.TotalLuCw);
            r.SumLuHw = r.Rows.Sum(x => x.TotalLuHw);
            r.SumWsfu = r.Rows.Sum(x => x.TotalWsfu);

            r.QdCwBsEnLps = PlumbingTables.LuToQdLps(r.SumLuCw);
            r.QdHwBsEnLps = PlumbingTables.LuToQdLps(r.SumLuHw);
            r.QdHunterGpm = PlumbingTables.WsfuToGpm(r.SumWsfu, flushValveMajority);

            return r;
        }

        private static string ExtractName(Element el)
        {
            try
            {
                var fi = el as FamilyInstance;
                var fam = fi?.Symbol?.Family?.Name ?? "";
                var sym = fi?.Symbol?.Name ?? "";
                return (fam + " " + sym + " " + (el.Name ?? "")).Trim();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return el?.Name ?? ""; }
        }

        private static void TryWriteDouble(Element el, string name, double v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double)  p.Set(v);
                else if (p.StorageType == StorageType.Integer) p.Set((int)Math.Round(v));
                else if (p.StorageType == StorageType.String)  p.Set(v.ToString("F2"));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
