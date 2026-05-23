using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Computes a diff between a CSV file (Name + Cost + EmbodiedCarbon +
    /// Class columns at minimum) and the project's current Material set.
    /// Drives the diff-preview dialog so users never get a silent
    /// overwrite — they see exactly what would change and can cancel.
    ///
    /// A19 — Import preview. Reuses the column-name-keyed parser from
    /// <see cref="MaterialLookupCsv"/> so the import surface accepts the
    /// same column conventions as the corporate baseline.
    /// </summary>
    public class MaterialCsvUpdate
    {
        public string MaterialName { get; set; }
        public ElementId MaterialId { get; set; }
        public double? OldCost { get; set; }
        public double? NewCost { get; set; }
        public double? OldCarbon { get; set; }
        public double? NewCarbon { get; set; }
        public string OldClass { get; set; }
        public string NewClass { get; set; }

        public string ChangeSummary
        {
            get
            {
                var parts = new List<string>();
                if (NewCost.HasValue && OldCost.HasValue && Math.Abs((NewCost.Value - OldCost.Value)) > 0.001)
                    parts.Add($"cost {OldCost:F0}→{NewCost:F0}");
                if (NewCarbon.HasValue && OldCarbon.HasValue && Math.Abs((NewCarbon.Value - OldCarbon.Value)) > 0.001)
                    parts.Add($"carbon {OldCarbon:F0}→{NewCarbon:F0}");
                if (!string.IsNullOrEmpty(NewClass) && !string.Equals(NewClass, OldClass, StringComparison.OrdinalIgnoreCase))
                    parts.Add($"class '{OldClass}'→'{NewClass}'");
                return string.Join(", ", parts);
            }
        }
    }

    public class MaterialCsvDiffResult
    {
        public List<MaterialCsvUpdate> Updates { get; set; } = new List<MaterialCsvUpdate>();
        public List<string> NewRows { get; set; } = new List<string>();
        public List<string> MissingInCsv { get; set; } = new List<string>();
    }

    public static class MaterialCsvDiff
    {
        public static MaterialCsvDiffResult Compute(Document doc, string csvPath)
        {
            if (doc == null || string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath)) return null;
            var result = new MaterialCsvDiffResult();
            try
            {
                var csv = ParseCsv(csvPath);
                if (csv == null) return null;

                var projectMaterials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var row in csv)
                {
                    if (!projectMaterials.TryGetValue(row.Name, out var mat))
                    { result.NewRows.Add(row.Name); continue; }

                    double oldCost = 0, oldCarbon = 0;
                    try
                    {
                        var cp = mat.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                        if (cp != null && cp.StorageType == StorageType.Double) oldCost = cp.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"Diff cost '{row.Name}': {ex.Message}"); }
                    try
                    {
                        var lp = mat.LookupParameter("STING_EMB_CARBON_NR");
                        if (lp != null && lp.StorageType == StorageType.Double) oldCarbon = lp.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"Diff carbon '{row.Name}': {ex.Message}"); }
                    string oldClass = mat.MaterialClass ?? "";

                    bool changed = false;
                    var upd = new MaterialCsvUpdate
                    {
                        MaterialName = row.Name,
                        MaterialId = mat.Id,
                        OldCost = oldCost,
                        OldCarbon = oldCarbon,
                        OldClass = oldClass,
                    };
                    if (row.Cost.HasValue && Math.Abs(row.Cost.Value - oldCost) > 0.001)
                    { upd.NewCost = row.Cost.Value; changed = true; }
                    if (row.Carbon.HasValue && Math.Abs(row.Carbon.Value - oldCarbon) > 0.001)
                    { upd.NewCarbon = row.Carbon.Value; changed = true; }
                    if (!string.IsNullOrEmpty(row.Class) && !string.Equals(row.Class, oldClass, StringComparison.OrdinalIgnoreCase))
                    { upd.NewClass = row.Class; changed = true; }
                    if (changed) result.Updates.Add(upd);
                }

                // Materials present in project but not in CSV
                var csvNames = new HashSet<string>(csv.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var m in projectMaterials.Keys)
                    if (!csvNames.Contains(m)) result.MissingInCsv.Add(m);
            }
            catch (Exception ex) { StingLog.Error("MaterialCsvDiff.Compute", ex); }
            return result;
        }

        public static int Apply(Document doc, MaterialCsvDiffResult diff)
        {
            if (doc == null || diff == null || diff.Updates.Count == 0) return 0;
            int written = 0;
            using (var t = new Transaction(doc, "STING Material CSV Import"))
            {
                t.Start();
                foreach (var u in diff.Updates)
                {
                    try
                    {
                        var mat = doc.GetElement(u.MaterialId) as Material;
                        if (mat == null) continue;
                        if (u.NewCost.HasValue)
                        {
                            var p = mat.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) p.Set(u.NewCost.Value);
                        }
                        if (u.NewCarbon.HasValue)
                        {
                            var p = mat.LookupParameter("STING_EMB_CARBON_NR");
                            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) p.Set(u.NewCarbon.Value);
                        }
                        if (!string.IsNullOrEmpty(u.NewClass))
                            mat.MaterialClass = u.NewClass;
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"CsvDiff.Apply '{u.MaterialName}': {ex.Message}"); }
                }
                t.Commit();
            }
            return written;
        }

        // ── CSV parsing (column-name keyed) ──

        private class Row
        {
            public string Name;
            public double? Cost;
            public double? Carbon;
            public string Class;
        }

        private static List<Row> ParseCsv(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return new List<Row>();
            var header = StingToolsApp.ParseCsvLine(lines[0]);
            int Idx(params string[] candidates)
            {
                foreach (var c in candidates)
                {
                    int i = Array.FindIndex(header, h => string.Equals(h, c, StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) return i;
                }
                return -1;
            }
            int iName = Idx("Name", "Material", "MaterialName");
            int iCost = Idx("Cost", "Cost_USD", "Cost_per_unit");
            int iCarb = Idx("EmbodiedCarbon", "Carbon_kgCO2e", "kgCO2e", "EmbodiedCarbon_kgCO2eperkg");
            int iCls  = Idx("Class", "MaterialClass");
            if (iName < 0) return null;

            var rows = new List<Row>();
            for (int li = 1; li < lines.Length; li++)
            {
                var f = StingToolsApp.ParseCsvLine(lines[li]);
                if (f == null || f.Length <= iName) continue;
                string n = (f[iName] ?? "").Trim();
                if (string.IsNullOrEmpty(n)) continue;
                rows.Add(new Row
                {
                    Name = n,
                    Cost = iCost >= 0 && iCost < f.Length ? ParseDouble(f[iCost]) : null,
                    Carbon = iCarb >= 0 && iCarb < f.Length ? ParseDouble(f[iCarb]) : null,
                    Class = iCls >= 0 && iCls < f.Length ? (f[iCls] ?? "").Trim() : null,
                });
            }
            return rows;
        }

        private static double? ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }
    }
}
