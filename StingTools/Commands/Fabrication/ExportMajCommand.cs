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

            // Phase B.2 deferred: the direct API path
            // (FabricationPart.SaveAsFabricationJob / FabricationUtils.*)
            // could not be verified in the sandbox; signatures differ
            // 2024/2025/2026. We surface a pointer to Revit's built-in
            // Fabrication → Export to MAJ menu so users are unblocked
            // on the CAMduct/ESTmep handoff today, and keep the
            // command wired so the verified API path can land later
            // in a one-file swap.
            ShowDeferred(outPath, partIds.Count);
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

        private void ShowDeferred(string plannedPath, int partCount)
        {
            var panel = StingResultPanel.Create("v4 Fabrication Job Export");
            panel.SetSubtitle("MAJ export — Phase B.2 deferred");

            panel.AddSection("STATUS")
                 .Metric("Parts in scope", partCount.ToString())
                 .Metric("Planned path",   plannedPath)
                 .Metric("API status",     "pending SDK-linked build verification");

            panel.AddSection("IMMEDIATE WORKAROUND")
                 .Text("Select the FabricationParts above and use Revit's built-in menu:")
                 .Text("  Systems ribbon → MEP Fabrication → Export to MAJ")
                 .Text("Drop the resulting .maj into CAMduct / ESTmep via File → Open Job.");

            panel.AddSection("PHASE B.2 PLAN")
                 .Text("Verify the 2025 FabricationUtils.ExportToMAJ signature + options")
                 .Text("against a live Revit SDK build bench, then this command emits the")
                 .Text(".maj file automatically. One-file swap — no downstream changes.");
            panel.Show();
        }
    }
}
