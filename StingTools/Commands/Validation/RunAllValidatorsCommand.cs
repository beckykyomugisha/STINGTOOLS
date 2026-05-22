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
                // §5.5 — reads UNICLASS_* / NBS_CODE_TXT / ASSET_RFI_URL_TXT.
                all.AddRange(new ClassificationAuditValidator().Validate(doc));
                // Phase 188 (Fix G2) — profile-driven pressure regime
                // (healthcare-htm03-01 / gmp-annex1 / iso-14644-cleanroom /
                // bs-en-12128-lab). Self-gates on PRJ_ORG_PRESSURE_PROFILE_TXT
                // — returns empty list when the param is unset so projects that
                // don't use the profile pay zero cost.
                try
                {
                    all.AddRange(new StingTools.Core.Validation.Mep
                        .GeneralPressureRegimeValidator().Validate(doc));
                }
                catch (Exception exGp) { StingLog.Warn($"GeneralPressureRegimeValidator: {exGp.Message}"); }
            }
            catch (Exception ex)
            {
                StingLog.Error("RunAllValidatorsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // Pack 123 / Gap D — apply per-element suppressions before display.
            // Coordinators dismiss "intentionally undefined" findings via the
            // suppression schema; this filter honours their choices while
            // keeping the count visible in the SUMMARY section.
            int suppressed = 0;
            try
            {
                var (kept, sup) = StingTools.Core.Storage.StingValidatorSuppressionSchema.Filter(doc, all);
                all = kept;
                suppressed = sup;
            }
            catch (Exception sx) { StingLog.Warn($"Suppression filter: {sx.Message}"); }

            ShowResult(all, suppressed);
            return Result.Succeeded;
        }

        private void ShowResult(List<ValidationResult> all, int suppressed = 0)
        {
            int errors   = all.Count(r => r.Severity == ValidationSeverity.Error);
            int warnings = all.Count(r => r.Severity == ValidationSeverity.Warning);
            int infos    = all.Count(r => r.Severity == ValidationSeverity.Info);

            var panel = StingResultPanel.Create("v4 Validation Suite");
            panel.SetSubtitle("ConnectivityValidator + FillValidator + SpecValidator + TerminationValidator + SlopeValidator + ClearanceValidator + MaintenanceClashValidator + ClassificationAuditValidator + GeneralPressureRegimeValidator");

            panel.AddSection("SUMMARY")
                 .Metric("Total findings", all.Count.ToString())
                 .Metric("Errors",   errors.ToString())
                 .Metric("Warnings", warnings.ToString())
                 .Metric("Suppressed", suppressed.ToString())
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
