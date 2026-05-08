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
            if (findings == null || findings.Count == 0)
            {
                sb.AppendLine("OK — no findings.");
            }
            else
            {
                int err = findings.Count(f => f.Severity == ValidationSeverity.Error);
                int wrn = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                int inf = findings.Count(f => f.Severity == ValidationSeverity.Info);
                sb.AppendLine($"Findings: {findings.Count}  (errors {err}, warnings {wrn}, info {inf})").AppendLine();
                foreach (var f in findings.Take(50))
                    sb.AppendLine($"[{f.Severity,-7}] {f.Code,-25} {f.Message}");
                if (findings.Count > 50)
                    sb.AppendLine($"... +{findings.Count - 50} more (see StingTools.log)");
            }
            StingLog.Info(sb.ToString());
            TaskDialog.Show($"STING — {title}", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareRunAllValidatorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try { return HealthcareValidatorReporter.Report("Healthcare — Run All Validators",
                RunAllHealthcareValidators.Validate(cd.Application.ActiveUIDocument.Document)); }
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
            try { return HealthcareValidatorReporter.Report("Healthcare — IoT Device Staleness",
                new IoTStalenessValidator().Validate(cd.Application.ActiveUIDocument.Document)); }
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
