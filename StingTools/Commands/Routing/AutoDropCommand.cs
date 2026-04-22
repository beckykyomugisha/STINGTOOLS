// StingTools v4 MVP — AutoDropCommand.
//
// Single IExternalCommand that inspects the current selection, groups
// elements by discipline (Electrical / Plumbing / HVAC) based on
// their Category and dispatches each group to the matching drop
// engine. Shows an aggregate result via StingResultPanel.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Routing;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoDropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = new ParameterHelpers.CommandExecutionContext(commandData);
            var doc = ctx.Document;
            var uidoc = ctx.UIDocument;
            if (doc == null || uidoc == null) { message = "No active document."; return Result.Failed; }

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                TaskDialog.Show("STING v4 — Auto-drop",
                    "Select one or more fixtures before running Auto-drop.");
                return Result.Cancelled;
            }

            var byDisc = new Dictionary<string, List<Element>>
            {
                { "Electrical", new List<Element>() },
                { "Plumbing",   new List<Element>() },
                { "HVAC",       new List<Element>() }
            };

            foreach (var id in selIds)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                string disc = DisciplineFor((BuiltInCategory)el.Category.Id.Value);
                if (disc != null && byDisc.ContainsKey(disc)) byDisc[disc].Add(el);
            }

            if (byDisc.Values.All(v => v.Count == 0))
            {
                TaskDialog.Show("STING v4 — Auto-drop",
                    "Selection contains no electrical / plumbing / HVAC fixtures.");
                return Result.Cancelled;
            }

            var allResults = new List<DropResult>();
            try
            {
                if (byDisc["Electrical"].Count > 0)
                    allResults.Add(new AutoConduitDrop(doc).Execute(byDisc["Electrical"]));
                if (byDisc["Plumbing"].Count > 0)
                    allResults.Add(new AutoPipeDrop(doc).Execute(byDisc["Plumbing"]));
                if (byDisc["HVAC"].Count > 0)
                    allResults.Add(new AutoDuctDrop(doc).Execute(byDisc["HVAC"]));
            }
            catch (Exception ex)
            {
                StingLog.Error("AutoDropCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(allResults);
            return Result.Succeeded;
        }

        private static string DisciplineFor(BuiltInCategory bic)
        {
            switch (bic)
            {
                case BuiltInCategory.OST_ElectricalFixtures:
                case BuiltInCategory.OST_ElectricalEquipment:
                case BuiltInCategory.OST_LightingFixtures:
                case BuiltInCategory.OST_LightingDevices:
                case BuiltInCategory.OST_CommunicationDevices:
                case BuiltInCategory.OST_DataDevices:
                case BuiltInCategory.OST_SecurityDevices:
                case BuiltInCategory.OST_FireAlarmDevices:
                case BuiltInCategory.OST_NurseCallDevices:
                    return "Electrical";

                case BuiltInCategory.OST_PlumbingFixtures:
                case BuiltInCategory.OST_Sprinklers:
                    return "Plumbing";

                case BuiltInCategory.OST_DuctTerminal:
                case BuiltInCategory.OST_MechanicalEquipment:
                    return "HVAC";
            }
            return null;
        }

        private void ShowResult(List<DropResult> results)
        {
            var panel = StingResultPanel.Create("v4 Auto-drop");
            panel.SetSubtitle("Auto-drop across Electrical / Plumbing / HVAC");

            foreach (var r in results)
            {
                panel.AddSection(r.Discipline.ToUpperInvariant())
                     .Metric("Created", r.CreatedIds.Count.ToString())
                     .Metric("Skipped", r.SkippedCount.ToString())
                     .Metric("Failed",  r.FailedCount.ToString());
                if (r.Warnings.Count > 0)
                {
                    foreach (var w in r.Warnings.Take(10)) panel.Text(w);
                    if (r.Warnings.Count > 10) panel.Text($"(+{r.Warnings.Count - 10} more — see StingLog)");
                }
            }
            panel.Show();
        }
    }
}
