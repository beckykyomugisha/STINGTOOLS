// StingTools v4 MVP — CableSegregationCommand.
//
// Surfaces the BS EN 50174-2 segregation validator via the Routing
// tab. Read-only: reports pairs of trays/conduits that violate the
// Annex E minimum-separation matrix and suggests the divider type
// that would satisfy compliance without re-routing.
//
// Research Part-C gap #4 marked this as "the unique differentiator,
// no surveyed competitor does it" — MagiCAD has cable layouts but
// does not validate segregation; eVolve has multi-tier racks but
// does not check power vs data; ProDesign is calc-only.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CableSegregationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableSegResult res;
            try { res = CableSegregationValidator.Validate(doc); }
            catch (Exception ex)
            {
                StingLog.Error("CableSegregationCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("v4 BS EN 50174-2 Cable Segregation");
            panel.SetSubtitle(res.Findings.Count == 0
                ? "Compliant — no violations found"
                : $"{res.Findings.Count} violation(s)");

            panel.AddSection("SUMMARY")
                 .Metric("Trays scanned",  res.TraysScanned.ToString())
                 .Metric("Pairs checked",  res.PairsChecked.ToString())
                 .Metric("Violations",     res.Findings.Count.ToString())
                 .Metric("Errors",         res.Findings.Count(f => f.Severity == "Error").ToString())
                 .Metric("Warnings",       res.Findings.Count(f => f.Severity == "Warning").ToString());

            if (res.Findings.Count > 0)
            {
                panel.AddSection("FINDINGS (first 60)");
                foreach (var f in res.Findings.Take(60))
                    panel.Text($"[{f.Severity}] {f.Tray1.Value}↔{f.Tray2.Value}: {f}");
                if (res.Findings.Count > 60)
                    panel.Text($"(+{res.Findings.Count - 60} more — see StingLog)");
            }

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("SCANNER WARNINGS");
                foreach (var w in res.Warnings.Take(20)) panel.Text(w);
            }

            panel.AddSection("REFERENCE")
                 .Text("BS EN 50174-2:2018 Annex E table of minimum separations.")
                 .Text("Last 15 m before equipment are exempt (clause 6.6.7).")
                 .Text("Classify cables via ELC_CABLE_SEG_CLASS_TXT on tray/conduit.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
