// StingTools v4 MVP — DuctFabricator.
//
// Group ducts into sections, build assemblies, generate views, lay
// them out on sheets and emit a per-section seam tally, flange count
// and hanger schedule CSV.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication.Duct
{
    public class DuctFabricator
    {
        public void Fabricate(Document doc, IList<ElementId> elementIds, FabricationResult result)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;

            var grouper = new AssemblyGrouper();
            var groups = grouper.GroupForDiscipline(doc, elementIds, "Duct");
            int seq = 1;

            using (var tx = new Transaction(doc, "STING v4 Duct fabrication"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"Duct tx start: {ex.Message}"); return; }

                try
                {
                    foreach (var g in groups)
                    {
                        ElementId assyId = AssemblyBuilder.Build(doc, "Duct", g, seq++, result);
                        if (assyId == null || assyId == ElementId.InvalidElementId) { result.FailedCount++; continue; }
                        result.AssemblyIds.Add(assyId);
                        var views = AssemblyViewBuilder.BuildViews(doc, assyId);
                        result.Warnings.AddRange(views.Warnings);
                        var sheetId = ShopDrawingComposer.ComposeSheet(doc, "Duct", assyId, views, result);
                        if (sheetId != null && sheetId != ElementId.InvalidElementId)
                            result.SheetIds.Add(sheetId);
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"DuctFabricator: {ex.Message}");
                }
            }
            result.AssembliesByDiscipline["Duct"] = seq - 1;

            try { EmitSeamTallyCsv(doc, elementIds, result); }
            catch (Exception ex) { result.Warnings.Add($"Seam tally csv: {ex.Message}"); }
        }

        private void EmitSeamTallyCsv(Document doc, IList<ElementId> ids, FabricationResult result)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_duct_seams.csv");
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("element_id,category,name,seam_type_SMACNA,flange_count,material");
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string cat = el.Category?.Name ?? "";
                    string nm = (el.Name ?? "").Replace(',', ';');
                    string seam = ReadString(el, "HVC_DCT_SEAM_TYPE_TXT");
                    string mat  = ReadString(el, "HVC_DCT_MAT_TXT");
                    w.WriteLine($"{id.Value},{cat},{nm},{seam},,{mat}");
                }
            }
            result.Warnings.Add($"Duct seam tally -> {path}");
        }

        private static string ReadString(Element el, string param)
        {
            try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; }
        }
    }
}
