// StingTools v4 MVP — ExportPcfCommand.
//
// Emits Alias PCF files from the selected / active-view / project
// pipe network. PCF is the industry-standard input to Alias Isogen
// and Ez-ISO for ISO-6412 axonometric shop drawings. This closes the
// "no native ISO 6412" gap identified in Part B of the research
// report: instead of reinventing an axonometric projector, we emit
// the format the rest of the industry already consumes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportPcfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var elementsInScope = CollectScope(doc, uidoc);
            if (elementsInScope.Count == 0)
            {
                TaskDialog.Show("STING v4 — Export PCF",
                    "No pipes found in scope.\n\n" +
                    "PCF covers the piping discipline only — duct iso\n" +
                    "drawings are typically done by the fab-shop CAD\n" +
                    "tooling from the MAJ export.");
                return Result.Cancelled;
            }

            // Split by MEPSystem.Name so each pipeline gets its own PCF.
            var bySystem = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in elementsInScope)
            {
                string sysName = "UNKNOWN";
                try { sysName = (el as Pipe)?.MEPSystem?.Name ?? "UNKNOWN"; } catch { }
                if (!bySystem.TryGetValue(sysName, out var list))
                {
                    list = new List<ElementId>();
                    bySystem[sysName] = list;
                }
                list.Add(el.Id);
            }

            string outDir = Path.Combine(
                Path.GetDirectoryName(doc.PathName ?? Path.GetTempPath()) ?? Path.GetTempPath(),
                "_BIM_COORD", "pcf");

            var results = new List<PcfExportResult>();
            foreach (var kv in bySystem)
            {
                var r = PcfExporter.Export(doc, kv.Value, outDir, kv.Key);
                results.Add(r);
            }

            ShowResult(results, outDir);
            return Result.Succeeded;
        }

        private static List<Element> CollectScope(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var list = new List<Element>();
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count > 0)
            {
                foreach (var id in sel)
                {
                    var el = doc.GetElement(id);
                    if (el is Pipe || IsPipeFitting(el)) list.Add(el);
                }
            }
            else
            {
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe));
                foreach (var e in col) list.Add(e);

                // Also pull fittings / accessories in the same doc scope.
                var fittings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeFitting)
                    .WhereElementIsNotElementType();
                foreach (var e in fittings) list.Add(e);
                var accessories = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WhereElementIsNotElementType();
                foreach (var e in accessories) list.Add(e);
            }
            return list;
        }

        private static bool IsPipeFitting(Element el)
        {
            if (el?.Category == null) return false;
            int bic = (int)el.Category.Id.Value;
            return bic == (int)BuiltInCategory.OST_PipeFitting
                || bic == (int)BuiltInCategory.OST_PipeAccessory;
        }

        private void ShowResult(List<PcfExportResult> results, string outDir)
        {
            var panel = StingResultPanel.Create("v4 PCF Export (Alias Isogen / Ez-ISO)");
            panel.SetSubtitle($"{results.Count} PCF file(s) at {outDir}");

            int ok = results.Count(r => r.Success);
            int comps = results.Sum(r => r.ComponentCount);
            panel.AddSection("SUMMARY")
                 .Metric("Files written",   ok.ToString())
                 .Metric("Files failed",    (results.Count - ok).ToString())
                 .Metric("Components",      comps.ToString())
                 .Metric("Pipes",           results.Sum(r => r.PipeCount).ToString())
                 .Metric("Fittings",        results.Sum(r => r.FittingCount).ToString())
                 .Metric("Valves",          results.Sum(r => r.ValveCount).ToString())
                 .Metric("Flanges",         results.Sum(r => r.FlangeCount).ToString());

            panel.AddSection("FILES");
            foreach (var r in results.Take(20))
                panel.Text($"{(r.Success ? "OK" : "FAIL")}  {r.OutputPath}");

            panel.AddSection("NEXT STEP")
                 .Text("Open Alias Isogen with Project-Settings.FLS pointing at the")
                 .Text("shop's Isogen symbol library, then File → Process Pipeline →")
                 .Text("select any of the generated .pcf files. Isogen emits")
                 .Text("auto-ballooned ISO-6412 axonometric drawings + BOM per pipeline.");
            panel.Show();
        }
    }
}
