// StingTools — MEP System Instance command (Phase B).
//
//   MEP_BuildSystems — walk the connector graph, group connected MEP networks,
//   assign the STING (Phase A) system type + a meaningful name + STING params
//   to each, and best-effort create a MechanicalSystem / PipingSystem for orphan
//   networks that have a detectable source equipment. Scope = current selection
//   when present, else the whole project.

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
    public class MepBuildSystemsCommand : IExternalCommand
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
                bool scopedToSelection = sel != null && sel.Count > 0;

                MepSystemBuildResult res;
                using (var t = new Transaction(doc, "STING Build MEP Systems"))
                {
                    t.Start();
                    res = MepSystemInstanceBuilder.Build(doc, scopedToSelection ? sel : null,
                                                         attemptCreateOrphans: true);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Build System Instances");
                panel.SetSubtitle(
                    $"{res.Networks} network(s) · {res.Typed} typed · {res.Created} created · " +
                    $"{res.Stamped} stamped · {res.Skipped} skipped · {res.Failed} failed " +
                    $"({(scopedToSelection ? "selection" : "whole project")})");

                panel.AddSection("SUMMARY")
                     .Metric("Networks found",      res.Networks.ToString())
                     .Metric("Existing systems typed", res.Typed.ToString())
                     .Metric("New systems created",  res.Created.ToString())
                     .Metric("Members-only stamped", res.Stamped.ToString())
                     .Metric("Orphans skipped",      res.Skipped.ToString())
                     .Metric("Failed",               res.Failed.ToString());

                if (res.CrossStamp != null)
                {
                    panel.AddSection("CROSS-STAMP (HVC_/PLM_/ELC_)")
                         .Metric("Ducts",    res.CrossStamp.DuctsStamped.ToString())
                         .Metric("Pipes",    res.CrossStamp.PipesStamped.ToString())
                         .Metric("Circuits", res.CrossStamp.CircuitsStamped.ToString())
                         .Metric("Fixtures", res.CrossStamp.FixturesStamped.ToString());
                }

                panel.AddSection("NETWORKS");
                foreach (var r in res.Rows.Take(60))
                    panel.Text($"{Glyph(r.Outcome)} {r.Domain,-4} {r.Members,4} el  " +
                               $"{r.Classification,-20} {r.SystemName,-10} {r.Note}");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }

                panel.AddSection("NEXT");
                panel.Text("Systems now carry the STING type + classification — run AecFilters_Create " +
                           "and apply a coordination View Style Pack to colour them, then produce MEP " +
                           "drawings (Phase C: discipline + classification routing).");
                panel.Show();

                StingLog.Info($"MEP build systems: networks={res.Networks} typed={res.Typed} " +
                              $"created={res.Created} stamped={res.Stamped} skipped={res.Skipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepBuildSystemsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Glyph(MepSystemBuildOutcome o) => o switch
        {
            MepSystemBuildOutcome.Typed   => "◉",
            MepSystemBuildOutcome.Created => "✚",
            MepSystemBuildOutcome.Stamped => "✎",
            MepSystemBuildOutcome.Skipped => "–",
            MepSystemBuildOutcome.Failed  => "✖",
            _ => " "
        };
    }
}
