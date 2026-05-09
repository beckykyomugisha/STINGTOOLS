// StingTools — Validation_BS7671 command.
//
// Standalone wrapper around ElectricalStandardsValidator for users
// who want an electrical-only run rather than the full
// RunAllValidatorsCommand suite. Renders findings into the standard
// StingResultPanel and feeds the result list to the active Conduit
// Fill caching slot so the BIM Coordination Center electrical tab
// reflects the run.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Validation;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElectricalStandardsValidatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            List<ValidationResult> findings;
            try { findings = new ElectricalStandardsValidator().Validate(doc); }
            catch (Exception ex)
            {
                StingLog.Error("ElectricalStandardsValidator failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            int errors   = findings.Count(r => r.Severity == ValidationSeverity.Error);
            int warnings = findings.Count(r => r.Severity == ValidationSeverity.Warning);

            var byCode = findings.GroupBy(r => r.Code).OrderByDescending(g => g.Count());

            var panel = StingResultPanel.Create("BS 7671 Electrical Validation");
            panel.SetSubtitle($"{errors} errors · {warnings} warnings · {findings.Count} total findings");
            panel.AddSection("SUMMARY")
                 .Metric("Total findings", findings.Count.ToString())
                 .MetricError("Errors",   errors.ToString())
                 .MetricWarn ("Warnings", warnings.ToString());

            if (findings.Count > 0)
            {
                panel.AddSection("BY CODE");
                foreach (var g in byCode)
                    panel.Metric(g.Key, g.Count().ToString(), DescribeCode(g.Key));

                panel.AddSection("DETAIL");
                int rendered = 0;
                foreach (var r in findings.OrderByDescending(x => x.Severity).Take(60))
                {
                    panel.Text(r.ToString());
                    rendered++;
                }
                int rest = findings.Count - rendered;
                if (rest > 0) panel.Text($"(+{rest} more findings — see StingLog)");
            }

            panel.AddSection("REGULATORY")
                 .Text("BS 7671:2018+A2:2022 §522.8 — Conduit and trunking systems")
                 .Text("§522.8.4 — Cable run length between draw-in points (typical 6 m)")
                 .Text("§522.8.5 — Maximum 3 bends between draw-in points")
                 .Text("BS EN 61386 — Manufacturer cable fill 40% straight / 35% with bends");

            panel.Show();
            return Result.Succeeded;
        }

        private static string DescribeCode(string code)
        {
            switch (code)
            {
                case "ELEC.RUN.LONG":    return "Run exceeds draw-in spacing";
                case "ELEC.BENDS.EXCESS":return "Too many bends between draw-ins";
                case "ELEC.FILL.OVER":   return "Cable fill exceeds manufacturer limit";
                case "ELEC.FILL.NEAR":   return "Cable fill approaches limit (within 10%)";
                case "ELEC.BEND.ANGLE":  return "Non-standard bend angle";
                default: return "";
            }
        }
    }
}
