// Healthcare Pack — IExternalCommand wrappers around the 16 healthcare
// validators so WorkflowEngine.ResolveCommand and StingCommandHandler can
// dispatch them by tag. Each command runs one validator (or RunAll), then
// reports findings via TaskDialog + StingLog.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Validation;
using StingTools.Core.Validation.Healthcare;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Healthcare
{
    internal static class HealthcareValidatorReporter
    {
        public static Result Report(string title, List<ValidationResult> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"STING — {title}").AppendLine();

            int total = findings?.Count ?? 0;
            int err = 0, wrn = 0, inf = 0;
            if (total == 0)
            {
                sb.AppendLine("OK — no findings.");
            }
            else
            {
                err = findings.Count(f => f.Severity == ValidationSeverity.Error);
                wrn = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                inf = findings.Count(f => f.Severity == ValidationSeverity.Info);
                sb.AppendLine($"Findings: {total}  (errors {err}, warnings {wrn}, info {inf})").AppendLine();
                // Sort by severity desc so errors are never truncated behind warnings/info.
                // ValidationSeverity is an enum: Info=0, Warning=1, Error=2 → OrderByDescending shows errors first.
                foreach (var f in findings.OrderByDescending(f => (int)f.Severity).Take(50))
                    sb.AppendLine($"[{f.Severity,-7}] {f.Code,-25} {f.Message}");
                if (total > 50)
                    sb.AppendLine($"... +{total - 50} more (see StingTools.log)");
            }
            StingLog.Info(sb.ToString());

            // Inline result strip on the dock panel takes precedence over a
            // TaskDialog popup when the panel is open. Falls back gracefully
            // if the panel isn't realised (e.g. command run from ribbon).
            bool pushed = StingTools.UI.StingDockPanel.PushHcResult(title, total, err, wrn, inf);
            if (!pushed)
                TaskDialog.Show($"STING — {title}", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareRunAllValidatorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try {
                var doc = cd.Application.ActiveUIDocument.Document;
                // If the dock-panel Validators grid has rows ticked, run only
                // those (Healthcare_RunSelected dispatch path). Otherwise fall
                // back to the legacy "run every gated validator" sweep.
                var picked = HcOptions.SelectedValidators();
                if (picked.Count > 0)
                {
                    return HealthcareValidatorReporter.Report(
                        $"Healthcare — Run Selected ({picked.Count})",
                        RunSelectedHealthcareValidators.Validate(doc, picked));
                }
                return HealthcareValidatorReporter.Report("Healthcare — Run All Validators",
                    RunAllHealthcareValidators.Validate(doc));
            }
            catch (Exception ex) { StingLog.Error("Healthcare_RunAllValidators failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcarePressureAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Pressure Regime",
                new PressureRegimeValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_PressureAudit failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareWaterSafetyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Water Safety (HTM 04-01)",
                new WaterSafetyValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_WaterSafety failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareEesBranchAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — EES Branches (NFPA 99)",
                new EesBranchValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_EesBranch failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareRadShieldAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Radiation Shielding",
                new RadShieldValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_RadShield failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareAdvancedRadShieldCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Advanced Radiation (PET/NM/Brachy)",
                new AdvancedRadShieldValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_AdvancedRadShield failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareRdsCompletenessCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — RDS Completeness",
                new RdsCompletenessValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_RdsCompleteness failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareIoTStalenessCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try {
                // Reference for adopting Hc.* threshold params: command sets
                // the validator's public Threshold field from HcOptions.IotStaleMins
                // (slider lives on the Healthcare tab → Validators → Advanced
                // thresholds expander). Other validators that take thresholds
                // can follow the same pattern.
                int mins = HcOptions.IotStaleMins;
                var v = new IoTStalenessValidator
                {
                    Threshold = TimeSpan.FromMinutes(mins > 0 ? mins : 30)
                };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — IoT Device Staleness ({mins}m)",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
            catch (Exception ex) { StingLog.Error("Healthcare_IoTStaleness failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareStructuralLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Structural Loads (imaging)",
                new StructuralLoadValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_StructuralLoad failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareAcousticCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Acoustic (HTM 08-01)",
                new AcousticValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_Acoustic failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareEndoscopeTraceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Endoscope Trace (HTM 01-06)",
                new EndoscopeTraceValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_EndoscopeTrace failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareEesResilienceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — EES Resilience (NFPA 110)",
                new EesResilienceValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_EesResilience failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareRtlsCoverageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — RTLS Coverage",
                new RtlsCoverageValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_RtlsCoverage failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareWasteFlowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Waste Flow (HTM 07-01)",
                new WasteFlowValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
            catch (Exception ex) { StingLog.Error("Healthcare_WasteFlow failed", ex); m = ex.Message; return Result.Failed; }
        }
    }
}
