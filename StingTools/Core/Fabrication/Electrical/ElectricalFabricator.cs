// StingTools v4 MVP — ElectricalFabricator.
//
// Group conduits / trays, build assemblies, generate views, lay them
// out on sheets and emit a per-conduit bend schedule + cable tray
// section length schedule CSV next to the project file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication.Electrical
{
    public class ElectricalFabricator
    {
        public void Fabricate(Document doc, IList<ElementId> elementIds, FabricationResult result)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;

            var grouper = new AssemblyGrouper();
            var groups = grouper.GroupForDiscipline(doc, elementIds, "Electrical");
            int seq = 1;

            using (var tx = new Transaction(doc, "STING v4 Electrical fabrication"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"Electrical tx start: {ex.Message}"); return; }

                try
                {
                    foreach (var g in groups)
                    {
                        ElementId assyId = AssemblyBuilder.Build(doc, "Electrical", g, seq++, result);
                        if (assyId == null || assyId == ElementId.InvalidElementId) { result.FailedCount++; continue; }
                        result.AssemblyIds.Add(assyId);
                        var views = AssemblyViewBuilder.BuildViews(doc, assyId);
                        result.Warnings.AddRange(views.Warnings);
                        var sheetId = ShopDrawingComposer.ComposeSheet(doc, "Electrical", assyId, views, result);
                        if (sheetId != null && sheetId != ElementId.InvalidElementId)
                            result.SheetIds.Add(sheetId);
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"ElectricalFabricator: {ex.Message}");
                }
            }

            result.AssembliesByDiscipline["Electrical"] = seq - 1;

            // Bend + tray schedules as CSV sidecars
            try { EmitBendScheduleCsv(doc, elementIds, result); }
            catch (Exception ex) { result.Warnings.Add($"Bend schedule csv: {ex.Message}"); }
        }

        private void EmitBendScheduleCsv(Document doc, IList<ElementId> ids, FabricationResult result)
        {
            string outDir = OutputLocationHelper.Get(doc, "Fabrication");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_electrical_bends.csv");
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("element_id,category,name,is_bend,bend_deg,assembly");
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string nm = (el.Name ?? "").Replace(',', ';');
                    string cat = el.Category?.Name ?? "";
                    bool isBend = nm.ToUpperInvariant().Contains("ELBOW") || nm.ToUpperInvariant().Contains("BEND");
                    string deg = nm.Contains("90") ? "90" : nm.Contains("45") ? "45" : "";
                    w.WriteLine($"{id.Value},{cat},{nm},{isBend},{deg},");
                }
            }
            result.Warnings.Add($"Bend schedule -> {path}");
        }
    }
}
