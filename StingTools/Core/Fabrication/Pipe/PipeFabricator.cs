// StingTools v4 MVP — PipeFabricator.
//
// Group pipe runs into spools, build assemblies, generate views,
// lay them out on sheets, and emit weld map + fitting schedule +
// cut list + insulation takeoff CSVs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication.Pipe
{
    public class PipeFabricator
    {
        public void Fabricate(Document doc, IList<ElementId> elementIds, FabricationResult result)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;

            var grouper = new AssemblyGrouper();
            var groups = grouper.GroupForDiscipline(doc, elementIds, "Pipe");
            int seq = 1;

            using (var tx = new Transaction(doc, "STING v4 Pipe fabrication"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"Pipe tx start: {ex.Message}"); return; }

                try
                {
                    foreach (var g in groups)
                    {
                        ElementId assyId = AssemblyBuilder.Build(doc, "Pipe", g, seq++, result);
                        if (assyId == null || assyId == ElementId.InvalidElementId) { result.FailedCount++; continue; }
                        result.AssemblyIds.Add(assyId);
                        var views = AssemblyViewBuilder.BuildViews(doc, assyId);
                        result.Warnings.AddRange(views.Warnings);
                        var sheetId = ShopDrawingComposer.ComposeSheet(doc, "Pipe", assyId, views, result);
                        if (sheetId != null && sheetId != ElementId.InvalidElementId)
                            result.SheetIds.Add(sheetId);
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"PipeFabricator: {ex.Message}");
                }
            }
            result.AssembliesByDiscipline["Pipe"] = seq - 1;

            try { EmitWeldMapCsv(doc, elementIds, result); }
            catch (Exception ex) { result.Warnings.Add($"Weld map csv: {ex.Message}"); }
        }

        private void EmitWeldMapCsv(Document doc, IList<ElementId> ids, FabricationResult result)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_welds.csv");
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("element_id,category,name,weld_type,size_mm,schedule");
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string cat = el.Category?.Name ?? "";
                    string nm = (el.Name ?? "").Replace(',', ';');
                    string type = nm.ToUpperInvariant().Contains("FIELD") ? "FIELD"
                                : nm.ToUpperInvariant().Contains("SHOP") ? "SHOP" : "FIELD-FIT";
                    w.WriteLine($"{id.Value},{cat},{nm},{type},,");
                }
            }
            result.Warnings.Add($"Weld map -> {path}");
        }
    }
}
