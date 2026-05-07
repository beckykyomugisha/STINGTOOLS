using System;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// Best-effort EasyPower XML export. Maps STING data to a buses /
    /// branches / arc-flash schema; the real EasyPower import format is
    /// licensed and not publicly documented in full, so this is a
    /// reasonable approximation that EasyPower can be coerced to read.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EasyPowerExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var model = ExternalExportEngine.Build(doc);
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch { }
            string outPath = Path.Combine(outDir, $"STING_EasyPower_{DateTime.Now:yyyyMMdd-HHmm}.xml");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<EasyPowerProject>");
            sb.AppendLine($"  <ProjectInfo name=\"{Esc(model.ProjectName)}\" " +
                          $"number=\"{Esc(model.ProjectNumber)}\" " +
                          $"exported=\"{model.ExportDate:yyyy-MM-ddTHH:mm:ss}\"/>");
            sb.AppendLine("  <Buses>");
            foreach (var p in model.Panels)
            {
                sb.AppendLine($"    <Bus id=\"{Esc(p.PanelName)}\" " +
                              $"kv=\"{p.VoltageV / 1000.0:0.00000}\" " +
                              $"phases=\"{p.Phases}\" " +
                              $"faultKA=\"{p.FaultKa:0.000}\"/>");
            }
            sb.AppendLine("  </Buses>");
            sb.AppendLine("  <Branches>");
            foreach (var c in model.Circuits)
            {
                sb.AppendLine($"    <Branch id=\"{Esc(c.CircuitId)}\" " +
                              $"fromBus=\"{Esc(c.PanelName)}\" " +
                              $"loadKW=\"{c.LoadKW:0.000}\" " +
                              $"loadKVAR=\"{c.LoadKVAR:0.000}\" " +
                              $"ratingA=\"{c.RatingA:0}\" " +
                              $"csaMm2=\"{c.CsaMm2:0.0}\" " +
                              $"lengthM=\"{c.LengthM:0.00}\" " +
                              $"vdPct=\"{c.VDPct:0.000}\"/>");
            }
            sb.AppendLine("  </Branches>");
            if (model.ArcFlashResults != null && model.ArcFlashResults.Count > 0)
            {
                sb.AppendLine("  <ArcFlash>");
                foreach (var af in model.ArcFlashResults)
                {
                    sb.AppendLine($"    <Bus id=\"{Esc(af.PanelName)}\" " +
                                  $"ie_cal_cm2=\"{af.IncidentEnergy_CalCm2:0.00}\" " +
                                  $"boundary_mm=\"{af.BoundaryMm:0}\" " +
                                  $"ppe=\"{af.PpeCategory}\"/>");
                }
                sb.AppendLine("  </ArcFlash>");
            }
            sb.AppendLine("</EasyPowerProject>");

            try { File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8); }
            catch (Exception ex)
            {
                StingLog.Error($"EasyPowerExport write: {ex.Message}", ex);
                TaskDialog.Show("STING EasyPower Export", $"Save failed: {ex.Message}");
                return Result.Failed;
            }

            StingLog.Info($"EasyPowerExport: {outPath}");
            TaskDialog.Show("STING EasyPower Export",
                $"EasyPower XML exported (best-effort schema):\n{outPath}\n\n" +
                $"{model.Panels.Count} bus(es) · {model.Circuits.Count} branch(es) · {model.ArcFlashResults.Count} arc-flash row(s).\n\n" +
                "Note: EasyPower's real import format is licensed; this is a documented approximation.");
            return Result.Succeeded;
        }

        private static string Esc(string s) => System.Security.SecurityElement.Escape(s ?? "");
    }
}
