// StingTools — Phase E commands: per-discipline view production + electrical circuits.
//
//   MEP_ProduceMepViews — duplicate the active plan once per present MEP discipline
//     (M/E/P), set the view discipline, apply the resolved MEP DrawingType, and
//     overlay the system colours. The last link of create-systems → coordinated drawing.
//   MEP_BuildCircuits   — name + stamp every existing electrical circuit (and create
//     a power circuit from the current selection, assigning a selected panel).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepProduceMepViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var source = doc.ActiveView;

                MepViewResult res;
                using (var t = new Transaction(doc, "STING Produce MEP Views"))
                {
                    t.Start();
                    res = MepViewProducer.Produce(doc, source);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Produce Per-Discipline Views");
                panel.SetSubtitle($"{res.Created} view(s) created from '{source?.Name}'");

                panel.AddSection("VIEWS");
                foreach (var r in res.Rows)
                    panel.Text($"{(r.Created ? "✚" : "·")} {r.Discipline,-12} {r.ViewName,-40} {r.Note}");
                if (res.Rows.Count == 0)
                    panel.Text("No MEP disciplines present, or the active view is not a duplicatable plan.");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.AddSection("NEXT");
                panel.Text("Place the new views on sheets (Docs → Sheet Manager / Place Unplaced). " +
                           "Electrical colour comes from the elec DrawingType's style pack; duct/pipe from the system filters.");
                panel.Show();

                StingLog.Info($"MEP produce views: created={res.Created}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepProduceMepViewsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepBuildCircuitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                ICollection<ElementId> sel = null;
                try { sel = ctx.UIDoc?.Selection?.GetElementIds(); } catch { }

                MepCircuitResult res;
                using (var t = new Transaction(doc, "STING Build Circuits"))
                {
                    t.Start();
                    res = MepCircuitBuilder.BuildExisting(doc);
                    if (sel != null && sel.Count > 0)
                        MepCircuitBuilder.CreateFromSelection(doc, sel, res);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Build Electrical Circuits");
                panel.SetSubtitle($"{res.Named} named · {res.Stamped} stamped · {res.Created} created from selection");

                panel.AddSection("SUMMARY")
                     .Metric("Circuits named",   res.Named.ToString())
                     .Metric("Circuits stamped", res.Stamped.ToString())
                     .Metric("Created (selection)", res.Created.ToString());

                panel.AddSection("CIRCUITS");
                foreach (var r in res.Rows.Take(80)) panel.Text(r);

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.AddSection("NEXT");
                panel.Text("Circuits now carry ASS_MEP_SYS_NAME_TXT + DISC/SYS/FUNC tokens — they tag + schedule " +
                           "like duct/pipe systems. To create a circuit: select the devices (+ a panel) and re-run.");
                panel.Show();

                StingLog.Info($"MEP build circuits: named={res.Named} stamped={res.Stamped} created={res.Created}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepBuildCircuitsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
