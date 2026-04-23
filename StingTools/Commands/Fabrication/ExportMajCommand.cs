// StingTools v4 MVP — ExportMajCommand.
//
// Emits the selected FabricationParts to a Revit Fabrication Job
// (.maj) file via FabricationUtils.ExportToMAJ. MAJ is the
// documented interchange format consumed by CAMduct / ESTmep for
// coilline / plasma / XYZ cutter routing. Emitting MAJ from Revit
// avoids reverse-engineering the coilline LST0 or DXF formats and
// keeps the plugin honest: we feed the same shop CAM tooling the
// rest of the industry uses.
//
// Scope resolution mirrors GenerateFabPackageCommand:
//   - Selection: current uidoc selection, filtered to FabricationParts
//   - ActiveView: all FabricationParts visible in the active view
//   - Project: all FabricationParts in the document
//
// The output path defaults to <project>/_BIM_COORD/fab/<date>.maj so
// it lives alongside the rest of the BIM coordination artefacts
// (OutputLocationHelper resolves the base directory).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportMajCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var partIds = CollectFabricationParts(doc, uidoc);
            if (partIds.Count == 0)
            {
                TaskDialog.Show("STING v4 — Export MAJ",
                    "No FabricationParts found in scope.\n\n" +
                    "Load a Fabrication Configuration (Manage → MEP Fabrication),\n" +
                    "create FabricationParts via Auto-drop or Design-to-Fab, then\n" +
                    "try again. For design-intent (Pipe/Duct) geometry, use the\n" +
                    "Cut List / Isometrics exports on the Fabrication tab.");
                return Result.Cancelled;
            }

            string outPath;
            try
            {
                outPath = ResolveOutputPath(doc);
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportMajCommand: output path resolve failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ICollection<ElementId> failed = null;
            bool success = false;
            try
            {
                var opts = new FabricationSaveJobOptions(doc)
                {
                    IncludeHangerRods = true,
                };
                // Revit 2025 signature: ExportToMAJ(Document, ICollection<ElementId> ids,
                //   string path, bool includeHoles, out ICollection<ElementId> failedIds)
                success = FabricationUtils.ExportToMAJ(
                    doc, partIds, outPath, includeHoles: true, out failed);
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportMajCommand: ExportToMAJ threw", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(outPath, partIds.Count, failed, success);
            return Result.Succeeded;
        }

        private List<ElementId> CollectFabricationParts(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var list = new List<ElementId>();
            try
            {
                IEnumerable<Element> source;
                if (FabricationOptions.ScopeProject)
                {
                    source = new FilteredElementCollector(doc)
                        .OfClass(typeof(FabricationPart))
                        .WhereElementIsNotElementType();
                }
                else if (FabricationOptions.ScopeActiveView && doc.ActiveView != null)
                {
                    source = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfClass(typeof(FabricationPart))
                        .WhereElementIsNotElementType();
                }
                else
                {
                    var ids = uidoc.Selection.GetElementIds();
                    source = ids?.Select(id => doc.GetElement(id))
                                .Where(e => e is FabricationPart)
                             ?? Enumerable.Empty<Element>();
                }
                foreach (var e in source)
                {
                    if (e is FabricationPart) list.Add(e.Id);
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"ExportMajCommand.CollectFabricationParts: {ex.Message}"); }
            return list;
        }

        private static string ResolveOutputPath(Document doc)
        {
            // Prefer <project>/_BIM_COORD/fab/<stamp>.maj.
            string baseDir;
            try
            {
                var projDir = Path.GetDirectoryName(doc.PathName);
                baseDir = !string.IsNullOrEmpty(projDir)
                    ? Path.Combine(projDir, "_BIM_COORD", "fab")
                    : Path.Combine(Path.GetTempPath(), "STING", "fab");
            }
            catch
            {
                baseDir = Path.Combine(Path.GetTempPath(), "STING", "fab");
            }
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(baseDir, $"sting_fabjob_{stamp}.maj");
        }

        private void ShowResult(string outPath, int partCount,
            ICollection<ElementId> failed, bool success)
        {
            var panel = StingResultPanel.Create("v4 Fabrication Job Export");
            panel.SetSubtitle(success
                ? $"MAJ written: {outPath}"
                : "MAJ export reported failure");

            panel.AddSection("SUMMARY")
                 .Metric("Parts requested", partCount.ToString())
                 .Metric("Parts failed",    (failed?.Count ?? 0).ToString())
                 .Metric("Success",         success ? "Yes" : "No");

            if (failed != null && failed.Count > 0)
            {
                panel.AddSection("FAILED PART IDS");
                foreach (var id in failed.Take(40))
                    panel.Text(id.Value.ToString());
                if (failed.Count > 40)
                    panel.Text($"(+{failed.Count - 40} more — see StingLog)");
            }

            panel.AddSection("NEXT STEPS")
                 .Text("Open CAMduct / ESTmep and File → Open Job.")
                 .Text("Select this MAJ file to flow cut list, labour hours,")
                 .Text("and NC output into the shop-floor workflow.");
            panel.Show();
        }
    }
}
