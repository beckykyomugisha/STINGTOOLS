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
using StingTools.Core;

namespace StingTools.Core.Fabrication.Pipe
{
    public class PipeFabricator
    {
        public void Fabricate(Document doc, IList<ElementId> elementIds, FabricationResult result)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;

            var grouper = new AssemblyGrouper();
            var groups = grouper.GroupForDiscipline(doc, elementIds, "Pipe",
                out List<AssemblyGrouper.SpoolMetrics> metrics);
            int seq = 1;
            var symbolTargets = new List<(ElementId AssyId, ElementId IsoViewId)>();

            int assyBefore = result.AssemblyIds.Count, sheetBefore = result.SheetIds.Count,
                tbBefore = result.TitleBlockFallbacks.Count;
            using (var tx = new Transaction(doc, "STING v4 Pipe fabrication"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"Pipe tx start: {ex.Message}"); return; }

                try
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var g = groups[i];
                        var m = i < metrics.Count ? metrics[i] : null;
                        ElementId assyId = AssemblyBuilder.Build(doc, "Pipe", g, seq++, result, m);
                        if (assyId == null || assyId == ElementId.InvalidElementId) { result.FailedCount++; continue; }
                        result.AssemblyIds.Add(assyId);
                        var views = AssemblyViewBuilder.BuildViews(doc, assyId);
                        result.Warnings.AddRange(views.Warnings);
                        var sheetId = ShopDrawingComposer.ComposeSheet(doc, "Pipe", assyId, views, result);
                        if (sheetId != null && sheetId != ElementId.InvalidElementId)
                            result.SheetIds.Add(sheetId);
                        if (views.ViewIso6412 != null && views.ViewIso6412 != ElementId.InvalidElementId)
                            symbolTargets.Add((assyId, views.ViewIso6412));
                    }
                    StingTx.Commit(tx);
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    // Nothing this transaction built is in the model: drop its assemblies,
                    // sheets and symbol targets so the result does not report them.
                    result.AssemblyIds.RemoveRange(assyBefore, result.AssemblyIds.Count - assyBefore);
                    result.SheetIds.RemoveRange(sheetBefore, result.SheetIds.Count - sheetBefore);
                    result.TitleBlockFallbacks.RemoveRange(tbBefore, result.TitleBlockFallbacks.Count - tbBefore);
                    symbolTargets.Clear();
                    seq = 1;
                    result.Warnings.Add($"PipeFabricator: {ex.Message}");
                }
            }
            result.AssembliesByDiscipline["Pipe"] = seq - 1;

            FabricationEngine.PlaceSymbolsIfRequested(doc, "Pipe", symbolTargets, result);

            try { EmitWeldMapCsv(doc, elementIds, result); }
            catch (Exception ex) { result.Warnings.Add($"Weld map csv: {ex.Message}"); }
        }

        private void EmitWeldMapCsv(Document doc, IList<ElementId> ids, FabricationResult result)
        {
            string outDir = OutputLocationHelper.GetRoutedDirectory(doc, "Schedule");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_welds.csv");
            using (var w = new StreamWriter(path, false))
            {
                Core.Branding.BrandTokens.StampCsvHeader(w, doc, "pipe_weld_map");
                w.WriteLine("element_id,category,name,weld_type,size_mm,schedule");
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string cat = global::StingTools.Core.ParameterHelpers.GetCategoryName(el);
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
