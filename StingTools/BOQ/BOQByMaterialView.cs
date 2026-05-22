// ══════════════════════════════════════════════════════════════════════════
//  BOQByMaterialView.cs — N+10.
//
//  Re-pivot the BOQ line-item list so it groups by primary material
//  name instead of NRM2 section / discipline. Answers "what does C40
//  concrete cost across the whole project?" in a single roll-up.
//
//  Stateless transform over a built BOQDocument — no side effects, no
//  transactions. Callers (BOQ Cost Manager dashboard, the MAT panel
//  Library tab, the Sustainability gate) hold the result, render it.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BOQByMaterialRow
    {
        public string MaterialName { get; set; } = "";
        public string MaterialClass { get; set; } = "";
        public int ElementCount { get; set; }
        public double TotalQuantity { get; set; }
        public string DominantUnit { get; set; } = "";
        public double TotalCostUGX { get; set; }
        public double TotalCostUSD { get; set; }
        public double TotalCarbonKg { get; set; }
        public List<string> DistinctCategories { get; set; } = new List<string>();

        public string CategoriesText => string.Join(", ", DistinctCategories.Take(3))
            + (DistinctCategories.Count > 3 ? $" +{DistinctCategories.Count - 3}" : "");
    }

    public class BOQByMaterialResult
    {
        public List<BOQByMaterialRow> Rows { get; } = new List<BOQByMaterialRow>();
        public int ItemsScanned { get; set; }
        public int ItemsWithoutMaterial { get; set; }
        public double TotalCostUGX => Rows.Sum(r => r.TotalCostUGX);
        public double TotalCarbonKg => Rows.Sum(r => r.TotalCarbonKg);
    }

    public static class BOQByMaterialView
    {
        /// <summary>
        /// Build a material-grouped pivot from a BOQ document + the live
        /// project so the rows can resolve each item's primary material.
        /// Items with no resolvable material are aggregated into a single
        /// "(no material)" row so they're visible but not double-counted.
        /// </summary>
        public static BOQByMaterialResult Build(Document doc, BOQDocument boq)
        {
            var result = new BOQByMaterialResult();
            if (doc == null || boq == null) return result;

            // Material name → accumulator
            var acc = new Dictionary<string, BOQByMaterialRow>(StringComparer.OrdinalIgnoreCase);
            // Unit voting per material — pick the most common unit as DominantUnit.
            var unitVotes = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in boq.AllItems)
            {
                result.ItemsScanned++;
                string matName = ResolveMaterialName(doc, item);
                if (string.IsNullOrEmpty(matName))
                {
                    matName = "(no material)";
                    result.ItemsWithoutMaterial++;
                }

                if (!acc.TryGetValue(matName, out var row))
                {
                    string cls = ResolveMaterialClass(doc, matName);
                    row = new BOQByMaterialRow { MaterialName = matName, MaterialClass = cls };
                    acc[matName] = row;
                    unitVotes[matName] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                }
                row.ElementCount++;
                row.TotalQuantity += item.Quantity;
                row.TotalCostUGX += item.TotalUGX;
                row.TotalCostUSD += item.TotalUSD;
                row.TotalCarbonKg += item.EmbodiedCarbonKg;

                if (!string.IsNullOrEmpty(item.Category) && !row.DistinctCategories.Contains(item.Category))
                    row.DistinctCategories.Add(item.Category);

                if (!string.IsNullOrEmpty(item.Unit))
                {
                    var votes = unitVotes[matName];
                    votes.TryGetValue(item.Unit, out double v);
                    votes[item.Unit] = v + item.Quantity;
                }
            }

            // Resolve dominant unit per material from votes.
            foreach (var kv in acc)
            {
                if (unitVotes.TryGetValue(kv.Key, out var votes) && votes.Count > 0)
                    kv.Value.DominantUnit = votes.OrderByDescending(v => v.Value).First().Key;
            }

            result.Rows.AddRange(acc.Values
                .OrderByDescending(r => r.TotalCostUGX)
                .ThenBy(r => r.MaterialName));
            return result;
        }

        private static string ResolveMaterialName(Document doc, BOQLineItem item)
        {
            try
            {
                if (item.RevitElementId < 0) return null;
                var el = doc.GetElement(new ElementId(item.RevitElementId));
                if (el == null) return null;
                Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0)
                        return doc.GetElement(mid)?.Name;
                }
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var mid in mats)
                        if (mid != null && mid.Value > 0)
                            return doc.GetElement(mid)?.Name;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("BOQByMat.Name", $"ResolveMaterialName: {ex.Message}"); }
            return null;
        }

        private static string ResolveMaterialClass(Document doc, string materialName)
        {
            if (string.IsNullOrEmpty(materialName) || materialName == "(no material)") return "";
            try
            {
                var mat = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));
                return mat?.MaterialClass ?? "";
            }
            catch (Exception ex) { StingLog.WarnRateLimited("BOQByMat.Class", $"ResolveMaterialClass: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// Write the pivot to CSV under the project output folder.
        /// Returns the path.
        /// </summary>
        public static string WriteCsv(Document doc, BOQByMaterialResult result)
        {
            string outDir = Core.OutputLocationHelper.GetOutputDirectory(doc);
            string path = System.IO.Path.Combine(outDir,
                $"STING_boq_by_material_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("MaterialName,MaterialClass,ElementCount,TotalQuantity,DominantUnit,TotalCostUGX,TotalCostUSD,TotalCarbonKg,DistinctCategories");
            foreach (var r in result.Rows)
            {
                sb.AppendLine(
                    $"\"{r.MaterialName}\",\"{r.MaterialClass}\",{r.ElementCount},{r.TotalQuantity:F2}," +
                    $"\"{r.DominantUnit}\",{r.TotalCostUGX:F0},{r.TotalCostUSD:F2},{r.TotalCarbonKg:F1}," +
                    $"\"{string.Join(";", r.DistinctCategories)}\"");
            }
            System.IO.File.WriteAllText(path, sb.ToString());
            StingLog.Info($"BOQByMaterialView: wrote {result.Rows.Count} rows → {path}");
            return path;
        }
    }
}
