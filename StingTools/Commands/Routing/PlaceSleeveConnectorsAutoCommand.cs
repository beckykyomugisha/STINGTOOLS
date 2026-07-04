// StingTools — Routing_PlaceSleeveConnectorsAuto.
//
// Non-interactive sibling of PlaceSleeveConnectorsCommand. Runs the sleeve
// engine live (dryRun:false) with NO dialog, so it can sit inside
// WORKFLOW_ElectricalRoughIn.json between placement and auto-drop without
// blocking the pipeline on a modal prompt. Summarises to StingLog and
// returns Succeeded (a clean no-op when there is nothing to sleeve).
//
// Target set: the current selection when it holds electrical fixtures
// (Placement_PlaceFixtures leaves its placed IDs selected), else every
// electrical fixture in the active view. The engine is idempotent, so this
// is safe to re-run.

using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSleeveConnectorsAutoCommand : IExternalCommand
    {
        private static readonly BuiltInCategory[] ElecCats =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var fixtures = CollectSelected(doc, uidoc);
            string scope = fixtures.Count > 0 ? "selection" : "active view";
            if (fixtures.Count == 0) fixtures = CollectFromActiveView(doc);

            if (fixtures.Count == 0)
            {
                StingLog.Info("Routing_PlaceSleeveConnectorsAuto: no electrical fixtures in selection or active view — no-op.");
                return Result.Succeeded; // graceful no-op — never blocks a workflow
            }

            SleeveConnectorResult r;
            try
            {
                r = new SleeveConnectorEngine(doc).Run(fixtures, dryRun: false);
            }
            catch (Exception ex)
            {
                // Non-fatal for the pipeline: log and continue so the following
                // Routing_AutoDrop step still runs.
                StingLog.Error("Routing_PlaceSleeveConnectorsAuto failed", ex);
                return Result.Succeeded;
            }

            StingLog.Info(
                $"Routing_PlaceSleeveConnectorsAuto ({scope}): considered={r.Considered} " +
                $"placed={r.Sleeved} alreadyRoutable={r.AlreadyRoutable} alreadySleeved={r.AlreadySleeved} " +
                $"failed={r.Failed}.");
            foreach (var w in r.Warnings) StingLog.Info($"  sleeve: {w}");
            return Result.Succeeded;
        }

        private static List<Element> CollectSelected(Document doc, UIDocument uidoc)
        {
            var list = new List<Element>();
            var ids = uidoc.Selection.GetElementIds();
            if (ids == null) return list;
            var elecSet = new HashSet<int>();
            foreach (var c in ElecCats) elecSet.Add((int)c);
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                if (elecSet.Contains((int)(BuiltInCategory)el.Category.Id.Value)) list.Add(el);
            }
            return list;
        }

        private static List<Element> CollectFromActiveView(Document doc)
        {
            var list = new List<Element>();
            if (doc.ActiveView == null) return list;
            try
            {
                var filter = new ElementMulticategoryFilter(ElecCats);
                var col = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WherePasses(filter).WhereElementIsNotElementType();
                foreach (var el in col) list.Add(el);
            }
            catch (Exception ex) { StingLog.Warn($"PlaceSleeveConnectorsAutoCommand: active-view collect failed: {ex.Message}"); }
            return list;
        }
    }
}
