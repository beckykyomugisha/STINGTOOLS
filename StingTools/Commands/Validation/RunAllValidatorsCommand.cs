// StingTools v4 MVP — RunAllValidatorsCommand.
//
// Executes all five v4 validators (connectivity, fill, spec,
// termination, slope) and surfaces a single aggregated report via
// StingResultPanel. Optionally feeds the findings into
// WarningsManager.Engine.LogValidationResults if that helper is
// available so they appear in the WarningsManager dashboard.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Validation;
using StingTools.UI;

namespace StingTools.Commands.Validation
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunAllValidatorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var all = new List<ValidationResult>();
            try
            {
                all.AddRange(new ConnectivityValidator().Validate(doc));
                all.AddRange(new FillValidator().Validate(doc));
                all.AddRange(new SpecValidator().Validate(doc));
                all.AddRange(new TerminationValidator().Validate(doc));
                all.AddRange(new SlopeValidator().Validate(doc));
                // Pack 1 — reads STING_CLEARANCE_MM (previously an orphan parameter).
                all.AddRange(new ClearanceValidator().Validate(doc));
                // Pack 2 — reads MNT_ENV_{W,D,H}_MM + MNT_ACCESS_DIR_TXT.
                all.AddRange(new MaintenanceClashValidator().Validate(doc));
            }
            catch (Exception ex)
            {
                StingLog.Error("RunAllValidatorsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(all);
            return Result.Succeeded;
        }

        private void ShowResult(List<ValidationResult> all)
        {
            int errors   = all.Count(r => r.Severity == ValidationSeverity.Error);
            int warnings = all.Count(r => r.Severity == ValidationSeverity.Warning);
            int infos    = all.Count(r => r.Severity == ValidationSeverity.Info);

            var panel = StingResultPanel.Create("v4 Validation Suite");
            panel.SetSubtitle("ConnectivityValidator + FillValidator + SpecValidator + TerminationValidator + SlopeValidator + ClearanceValidator + MaintenanceClashValidator");

            panel.AddSection("SUMMARY")
                 .Metric("Total findings", all.Count.ToString())
                 .Metric("Errors",   errors.ToString())
                 .Metric("Warnings", warnings.ToString())
                 .Metric("Info",     infos.ToString());

            // Group by validator for legibility
            foreach (var grp in all.GroupBy(r => r.Validator).OrderBy(g => g.Key))
            {
                panel.AddSection(grp.Key.ToUpperInvariant());
                int row = 0;
                foreach (var r in grp.OrderByDescending(x => x.Severity).Take(40))
                {
                    panel.Text(r.ToString());
                    row++;
                }
                int rest = grp.Count() - row;
                if (rest > 0) panel.Text($"(+{rest} more findings — see StingLog)");
            }

            panel.Show();
        }
    }
}
