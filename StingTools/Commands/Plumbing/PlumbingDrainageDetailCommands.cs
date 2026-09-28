// PlumbingDrainageDetailCommands — Phase 179d.
//
// Plumb_VentDesign     — runs VentDesigner against the existing DFU map.
// Plumb_InvertLevels   — InvertLevelEngine + writes PLM_DRN_INV_*.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI.Plumbing;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.Core.Calc;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbVentDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var dfuMap = FixtureUnitAggregator.BuildDfuMap(ctx.Doc);
            var vents  = VentDesigner.DesignVents(ctx.Doc, dfuMap.PipeDfu);

            int aavCount    = vents.Count(v => v.RequiresAav);
            int reliefCount = vents.Count(v => v.RequiresReliefVent);
            var rows = vents.Select(v => new DrainageVentRow
            {
                Drain   = $"{v.DrainPipeId.Value} DN{v.DrainDnMm}",
                Du      = v.Dfu,
                VentDn  = v.RecommendedVentDnMm,
                MaxLenM = v.MaxVentLengthM,
                Flag    = (v.RequiresAav ? "AAV" : "")
                          + (v.RequiresReliefVent ? (v.RequiresAav ? " · RELIEF" : "RELIEF") : "")
            }).ToList();
            string status = $"Vents · {dfuMap.PipeDfu.Count} drains · {vents.Count} records"
                          + (aavCount    > 0 ? $" · AAV {aavCount}"       : "")
                          + (reliefCount > 0 ? $" · RELIEF {reliefCount}" : "");

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDrainageVentResult(rows, status);
                return Result.Succeeded;
            }

            var panel = StingResultPanel.Create("Vent Design");
            panel.AddSection("SUMMARY")
                 .Metric("Drains analysed", dfuMap.PipeDfu.Count.ToString())
                 .Metric("Vent records",    vents.Count.ToString())
                 .Metric("AAV required",    aavCount.ToString())
                 .Metric("Relief vents",    reliefCount.ToString());
            if (vents.Any())
            {
                panel.AddSection("VENT REQUIREMENTS (first 30)");
                foreach (var v in vents.Take(30))
                    panel.Text($"Drain {v.DrainPipeId.Value} DN{v.DrainDnMm} · DU {v.Dfu:F1} → vent DN{v.RecommendedVentDnMm} · max {v.MaxVentLengthM:F1} m {(v.RequiresAav ? "· AAV" : "")} {(v.RequiresReliefVent ? "· RELIEF" : "")}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbInvertLevelsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var td = new TaskDialog("Plumb_InvertLevels")
            {
                MainInstruction = "Calculate invert levels?",
                MainContent = "Computes the upstream / downstream bore invert of every drainage pipe "
                            + "(centreline minus internal radius; upstream = the higher end).",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Calculate only (preview)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Calculate + write to PLM_DRN_INV_*");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2) return Result.Cancelled;
            bool write = pick == TaskDialogResult.CommandLink2;

            // Datum and precision come from IlReportingOptions (survey point by default).
            InvertReport r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Invert Levels"))
            {
                tx.Start();
                r = InvertLevelEngine.Calculate(ctx.Doc, writeBack: write);
                if (write) tx.Commit(); else tx.RollBack();
            }

            var rows = r.Rows.Select(row => new DrainageInvertRow
            {
                Pipe   = $"{row.PipeId.Value} DN{row.DnMm}",
                UsInvM = row.UsInvertM,
                DsInvM = row.DsInvertM,
                CoverM = row.CoverUsM.HasValue && row.CoverDsM.HasValue
                    ? Math.Min(row.CoverUsM.Value, row.CoverDsM.Value) : (double?)null
            }).ToList();
            string status = $"Inverts · {r.DatumLabel} · "
                          + $"{r.PipesAnalysed} pipes · {r.PipesWritten} written"
                          + (r.PipesUnbound > 0 ? $" · {r.PipesUnbound} NOT written (parameter unbound)" : "")
                          + (r.CoverViolations > 0 ? $" · {r.CoverViolations} cover-fail" : "")
                          + (write ? "" : " (preview)");

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDrainageInvertResult(rows, status);
                return Result.Succeeded;
            }

            var panel = StingResultPanel.Create("Invert Level Calculation");
            panel.SetSubtitle($"Levels in {r.DatumLabel}");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes analysed",    r.PipesAnalysed.ToString())
                 .Metric("Pipes written",     r.PipesWritten.ToString())
                 .Metric("Not written (unbound)", r.PipesUnbound.ToString())
                 .Metric("Nominal-size fallbacks", r.NominalFallbacks.ToString());
            if (r.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(20)) panel.Text(w);
            }
            if (r.Rows.Any())
            {
                panel.AddSection("INVERT TABLE (first 30)");
                foreach (var row in r.Rows.Take(30))
                    panel.Text($"Pipe {row.PipeId.Value} ID{row.DnMm}{(row.Source == InvertSource.NominalFallback ? " (nominal)" : "")} · {row.SystemName} · US {row.UsInvertM:F3} m · DS {row.DsInvertM:F3} m · {row.Gradient}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
