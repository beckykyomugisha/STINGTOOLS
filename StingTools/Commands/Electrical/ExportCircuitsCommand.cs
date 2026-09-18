// StingTools v4 MVP — Phase K circuit schedule export command.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCircuitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string outDir;
            try
            {
                outDir = StingPaths.Meta(doc, "_BIM_COORD", "electrical");
            }
            catch (Exception ex)
            {
                // Was Path.Combine(Path.GetTempPath(), "STING", "electrical") - a
                // per-session folder under Revit, so the exported circuits vanished with
                // the session and the path in the dialog stopped resolving.
                StingLog.Warn($"ExportCircuits: project path unavailable, using the shared output location: {ex.Message}");
                outDir = OutputLocationHelper.GetOutputDirectory(doc);
            }

            CircuitExportResult res;
            try { res = CircuitScheduleExporter.Export(doc, outDir); }
            catch (Exception ex)
            {
                StingLog.Error("ExportCircuitsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("v4 Circuit Schedule Export");
            panel.SetSubtitle(res.RowCount > 0
                ? $"{res.RowCount} circuit(s) → {outDir}"
                : "No ElectricalSystem objects found");
            panel.AddSection("FILES")
                 .Text(string.IsNullOrEmpty(res.CsvPath)  ? "(csv skipped)"  : $"CSV:  {res.CsvPath}")
                 .Text(string.IsNullOrEmpty(res.XmlPath)  ? "(xml skipped)"  : $"XML:  {res.XmlPath}")
                 .Text(string.IsNullOrEmpty(res.JsonPath) ? "(json skipped)" : $"JSON: {res.JsonPath}");
            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(20)) panel.Text(w);
            }
            panel.AddSection("NEXT STEPS")
                 .Text("CSV for Excel + QS use.")
                 .Text("XML for Amtech ProDesign / Trimble Electrical Designer (ProDesign schema 1.0).")
                 .Text("JSON for EasyPower Revit Integrator / ETAP Revit Interface.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
