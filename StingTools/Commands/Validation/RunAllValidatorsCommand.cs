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
                // BS 7671:2018+A2:2022 — conduit bends, run length, fill ceilings.
                all.AddRange(new ElectricalStandardsValidator().Validate(doc));
                // Healthcare Pack H-1..H-30 — RunAllHealthcareValidators is gated
                // on PRJ_ORG_HEALTH_FACILITY_TYPE_TXT being non-empty so non-
                // healthcare projects skip the entire chain (zero cost).
                all.AddRange(StingTools.Core.Validation.Healthcare.RunAllHealthcareValidators.Validate(doc));
                // Phase 178d — penetration coverage (slab + wall + beam fire-stop sweep).
                all.AddRange(StingTools.Core.Validation.PenetrationCoverageValidator.Validate(doc));
                // Phase 178e — plumbing-fixture connector completeness (catches
                // swap-to-manufacturer regressions where a vendor family ships
                // fewer connectors than the seed authored).
                all.AddRange(StingTools.Core.Validation.PlumbingConnectorCompletenessValidator.Validate(doc));
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
            panel.SetSubtitle("ConnectivityValidator + FillValidator + SpecValidator + TerminationValidator + SlopeValidator + ClearanceValidator + MaintenanceClashValidator + ClassificationAuditValidator + ElectricalStandardsValidator + Healthcare Pack");

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
