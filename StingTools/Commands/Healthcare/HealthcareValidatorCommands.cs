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

            // Compact 1-line strip — always-visible at-a-glance summary.
            bool stripPushed = StingTools.UI.StingDockPanel.PushHcResult(title, total, err, wrn, inf);

            // Rich inline panel — sections + metrics + RAG bar + findings table,
            // hosted in the bottom-bar Expander. Auto-expands on push so the
            // user sees details without leaving the dock.
            try
            {
                double pct = total > 0
                    ? Math.Round(100.0 * Math.Max(0.0, total - err - 0.5 * wrn) / total, 1)
                    : 100.0;

                var rb = StingTools.UI.StingResultPanel.Create(title)
                    .SetSubtitle(total == 0
                            ? "OK — no findings"
                            : $"errors {err} · warnings {wrn} · info {inf} · total {total}")
                    .SetOverallPct(pct)
                    .AddSection("Summary")
                    .Metric("Total findings", total.ToString());
                if (err > 0) rb.MetricError("Errors",   err.ToString());
                else         rb.Metric("Errors", err.ToString());
                if (wrn > 0) rb.MetricWarn ("Warnings", wrn.ToString());
                else         rb.Metric("Warnings", wrn.ToString());
                rb.Metric("Info", inf.ToString());

                if (total > 0)
                {
                    var rows = findings
                        .OrderByDescending(f => (int)f.Severity)
                        .Take(50)
                        .Select(f => new[] {
                            f.Severity.ToString(),
                            f.Code ?? "",
                            f.Message ?? ""
                        })
                        .ToList();
                    rb.AddSection(total > 50 ? "Findings (top 50)" : "Findings")
                      .Table(new[] { "Severity", "Code", "Message" }, rows);
                    if (total > 50)
                        rb.Info($"+{total - 50} more findings written to StingTools.log");
                }
                StingTools.UI.StingDockPanel.PushHcResultPanel(rb);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Healthcare inline panel push: {ex.Message}");
            }

            // TaskDialog only fires when the dock panel is not open at all
            // (command run from ribbon / headless). Otherwise the inline
            // strip + rich panel cover both at-a-glance and detailed views.
            if (!stripPushed)
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
            try {
                var v = new PressureRegimeValidator
                {
                    DpMinFloorPa   = HcOptions.DpMinPa,
                    AchMinFloor    = HcOptions.AchMin,
                    AnteroomStrict = HcOptions.AnteroomStrict,
                };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — Pressure Regime (ΔP≥{v.DpMinFloorPa:F1} · ACH≥{v.AchMinFloor:F0} · ant {(v.AnteroomStrict ? "strict" : "soft")})",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
            catch (Exception ex) { StingLog.Error("Healthcare_PressureAudit failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareWaterSafetyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try {
                var v = new WaterSafetyValidator { DeadLegMaxM = HcOptions.DeadLegMaxM };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — Water Safety (HTM 04-01, dead-leg ≤ {v.DeadLegMaxM:F1} m)",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
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
            try {
                var v = new RadShieldValidator { RequireQeSignoff = HcOptions.RadRequireQe };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — Radiation Shielding (QE {(v.RequireQeSignoff ? "required" : "deferred")})",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
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
            try {
                var v = new EndoscopeTraceValidator { MinReaders = HcOptions.EndoMinReaders };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — Endoscope Trace (HTM 01-06, ≥{v.MinReaders} readers)",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
            catch (Exception ex) { StingLog.Error("Healthcare_EndoscopeTrace failed", ex); m = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)] [Regeneration(RegenerationOption.Manual)]
    public class HealthcareEesResilienceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) {
            try {
                var v = new EesResilienceValidator { UpsMaxAgeYrs = HcOptions.UpsMaxAgeYrs };
                return HealthcareValidatorReporter.Report(
                    $"Healthcare — EES Resilience (NFPA 110, UPS ≤ {v.UpsMaxAgeYrs} yrs)",
                    v.Validate(cd.Application.ActiveUIDocument.Document));
            }
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
