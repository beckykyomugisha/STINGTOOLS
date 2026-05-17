// StingTools v4 MVP — ElectricalFabricator.
//
// Group conduits / trays, build assemblies, generate views, lay them
// out on sheets and emit a per-conduit bend schedule + cable tray
// section length schedule CSV next to the project file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Fabrication.Electrical
{
    public class ElectricalFabricator
    {
        // Compiled once per process — bend-name regex used in the CSV
        // emit loop below. Recompiling per element is a measurable cost
        // when projects ship thousands of conduit fittings.
        private static readonly Regex _bendAngleRx = new Regex(
            @"\b(11(?:\.25)?|22(?:\.5)?|30|45|60|90|120)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public void Fabricate(Document doc, IList<ElementId> elementIds, FabricationResult result)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;

            var grouper = new AssemblyGrouper();
            var groups = grouper.GroupForDiscipline(doc, elementIds, "Electrical",
                out List<AssemblyGrouper.SpoolMetrics> metrics);
            int seq = 1;
            var symbolTargets = new List<(ElementId AssyId, ElementId IsoViewId)>();

            using (var tx = new Transaction(doc, "STING v4 Electrical fabrication"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"Electrical tx start: {ex.Message}"); return; }

                try
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var g = groups[i];
                        var m = i < metrics.Count ? metrics[i] : null;
                        ElementId assyId = AssemblyBuilder.Build(doc, "Electrical", g, seq++, result, m);
                        if (assyId == null || assyId == ElementId.InvalidElementId) { result.FailedCount++; continue; }
                        result.AssemblyIds.Add(assyId);
                        var views = AssemblyViewBuilder.BuildViews(doc, assyId);
                        result.Warnings.AddRange(views.Warnings);
                        var sheetId = ShopDrawingComposer.ComposeSheet(doc, "Electrical", assyId, views, result);
                        if (sheetId != null && sheetId != ElementId.InvalidElementId)
                            result.SheetIds.Add(sheetId);
                        if (views.ViewIso6412 != null && views.ViewIso6412 != ElementId.InvalidElementId)
                            symbolTargets.Add((assyId, views.ViewIso6412));
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

            FabricationEngine.PlaceSymbolsIfRequested(doc, "Electrical", symbolTargets, result);

            // Bend + tray schedules as CSV sidecars
            try { EmitBendScheduleCsv(doc, elementIds, result); }
            catch (Exception ex) { result.Warnings.Add($"Bend schedule csv: {ex.Message}"); }
        }

        private void EmitBendScheduleCsv(Document doc, IList<ElementId> ids, FabricationResult result)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_electrical_bends.csv");
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("element_id,category,name,is_bend,bend_deg,bend_source,assembly");
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string nm = (el.Name ?? "").Replace(',', ';');
                    string cat = el.Category?.Name ?? "";
                    string upper = nm.ToUpperInvariant();
                    bool isBend = upper.Contains("ELBOW") || upper.Contains("BEND");

                    // Prefer the parameter; fall back to family-name regex only when
                    // the parameter is missing or unparseable. ELC_CDT_BEND_ANGLE_DEG
                    // is the registry alias used by AutoConduitDrop and the fabricator.
                    string deg = "";
                    string source = "";
                    string paramVal = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_BEND_ANGLE_DEG);
                    if (!string.IsNullOrEmpty(paramVal) &&
                        double.TryParse(paramVal, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0)
                    {
                        deg = ((int)Math.Round(d)).ToString();
                        source = "param";
                    }
                    else if (isBend)
                    {
                        // Match the FIRST plausible angle in the name: 11, 22, 30, 45, 60, 90, 120.
                        var m = _bendAngleRx.Match(nm);
                        if (m.Success) { deg = m.Groups[1].Value; source = "name-regex"; }
                    }

                    w.WriteLine($"{id.Value},{cat},{nm},{isBend},{deg},{source},");
                }
            }
            result.Warnings.Add($"Bend schedule -> {path}");
        }
    }
}
