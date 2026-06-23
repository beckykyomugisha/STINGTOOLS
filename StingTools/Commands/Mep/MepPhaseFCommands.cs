// StingTools — Phase F commands: per-level view fan-out + auto sheet-placement,
// and opt-in electrical circuit auto-grouping.
//
//   MEP_ProduceMepViewsByLevel — one coordinated plan per (level × present
//     discipline), each placed on its own sheet (title block + number/name from
//     the DrawingType patterns).
//   MEP_AutoGroupCircuits      — FIRST-PASS: group uncircuited electrical devices
//     by nearest panel into power circuits (engineer reviews / rebalances after).

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
    public class MepProduceMepViewsByLevelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                MepLevelViewResult res;
                using (var t = new Transaction(doc, "STING Produce MEP Views by Level"))
                {
                    t.Start();
                    res = MepLevelViewProducer.Produce(doc, placeOnSheets: true);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Per-Level Views + Sheets");
                panel.SetSubtitle($"{res.Views} view(s) · {res.Sheets} sheet(s) across levels × disciplines");

                panel.AddSection("VIEWS / SHEETS");
                foreach (var r in res.Rows.Take(120))
                    panel.Text($"{(r.ViewCreated ? "✚" : "·")} {r.Level,-14} {r.Discipline,-12} " +
                               $"{r.ViewName,-38} {(r.SheetCreated ? "sheet " + r.SheetNumber : "(no sheet)"),-18} {r.Note}");
                if (res.Rows.Count == 0)
                    panel.Text("No level hosts MEP of any discipline — model ducts/pipes/devices first.");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.Show();

                StingLog.Info($"MEP per-level views: views={res.Views} sheets={res.Sheets}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepProduceMepViewsByLevelCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoGroupCircuitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                const int maxPerCircuit = 8;     // sensible default; engineer rebalances after
                const double maxDistM = 30.0;

                CircuitGroupResult res;
                using (var t = new Transaction(doc, "STING Auto-Group Circuits"))
                {
                    t.Start();
                    res = MepCircuitBuilder.AutoGroup(doc, maxPerCircuit, maxDistM);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Auto-Group Circuits (first pass)");
                panel.SetSubtitle($"{res.Created} circuit(s) created across {res.Groups} panel(s) · " +
                                  $"{res.Unreachable} device(s) out of range");

                panel.AddSection("⚠ REVIEW REQUIRED")
                     .Text($"First pass only: ≤{maxPerCircuit} devices per circuit, nearest panel within {maxDistM:F0} m, " +
                           "same level preferred. No load balancing or phase allocation — review and rebalance " +
                           "in the panel schedules before issue.");

                panel.AddSection("CIRCUITS CREATED");
                foreach (var r in res.Rows.Take(80)) panel.Text(r);
                if (res.Created == 0)
                    panel.Text("Nothing created — all electrical devices are already circuited, or no panels/devices found.");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.Show();

                StingLog.Info($"MEP auto-group circuits: created={res.Created} groups={res.Groups} unreachable={res.Unreachable}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepAutoGroupCircuitsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
