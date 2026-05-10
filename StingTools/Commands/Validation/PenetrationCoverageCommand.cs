// StingTools — PenetrationCoverageCommand.
//
// Standalone read-only audit that surfaces firestop coverage gaps,
// orphan FRP families, and structural-review findings on beam
// penetrations. Mirrors the existing v4 validator commands.

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
    public class PenetrationCoverageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            List<ValidationResult> findings;
            try { findings = PenetrationCoverageValidator.Validate(doc); }
            catch (Exception ex)
            {
                StingLog.Error("PenetrationCoverageCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("Penetration Coverage Audit");
            panel.SetSubtitle("Firestop register integrity + structural review (beams)");

            int errors   = findings.Count(f => f.Severity == ValidationSeverity.Error);
            int warnings = findings.Count(f => f.Severity == ValidationSeverity.Warning);
            int memberOrphans = findings.Count(f => f.Code == "PEN.MEMBER.ORPHAN");
            int frpOrphans    = findings.Count(f => f.Code == "PEN.FRP.ORPHAN");
            int structFail    = findings.Count(f => f.Code == "PEN.STRUCT.FAIL");
            int structReview  = findings.Count(f => f.Code == "PEN.STRUCT.REVIEW");
            int noRating      = findings.Count(f => f.Code == "PEN.NO.RATING");

            panel.AddSection("SUMMARY")
                 .Metric("Errors",          errors.ToString())
                 .Metric("Warnings",        warnings.ToString())
                 .Metric("Total findings",  findings.Count.ToString());

            panel.AddSection("BREAKDOWN")
                 .Metric("Members without FRP",     memberOrphans.ToString())
                 .Metric("Orphan FRP instances",    frpOrphans.ToString())
                 .Metric("Beam STRUCT_FAIL",        structFail.ToString())
                 .Metric("Beam STRUCT_REVIEW",      structReview.ToString())
                 .Metric("Missing fire rating",     noRating.ToString());

            foreach (var f in findings.Take(50)) panel.Text(f.ToString());
            if (findings.Count > 50) panel.Text($"(+{findings.Count - 50} more — see StingLog)");

            panel.Show();
            return Result.Succeeded;
        }
    }
}
