using Autodesk.Revit.DB;
using System.Collections.Generic;
using StingTools.Core;   // HcOptions (cooperative-cancel signal)

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>
    /// Sibling of <see cref="RunAllHealthcareValidators"/> that runs only
    /// the validators whose keys appear in <paramref name="picked"/>. Keys
    /// match the dispatch-tag suffixes used by the dock-panel
    /// (e.g. "PressureAudit", "MgasAudit", "IoTStaleness", …) so the
    /// SelectedValidators column in the dock grid maps 1-to-1.
    ///
    /// Invariants preserved from RunAll:
    ///   - PRJ_ORG_HEALTH_FACILITY_TYPE_TXT gate (non-healthcare projects skip).
    ///   - HealthcareValidatorContext built once + shared across the chain.
    ///   - Per-validator HealthcareValidatorGate.AllowedValidators filter still applied.
    /// </summary>
    public static class RunSelectedHealthcareValidators
    {
        public static List<ValidationResult> Validate(Document doc, HashSet<string> picked)
        {
            var all = new List<ValidationResult>();
            if (doc == null || picked == null || picked.Count == 0) return all;

            // Same facility-gate as RunAll
            string facType = "";
            try
            {
                var pi = doc.ProjectInformation;
                var p  = pi?.LookupParameter("PRJ_ORG_HEALTH_FACILITY_TYPE_TXT");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    facType = (p.AsString() ?? "").Trim();
            }
            catch { }
            if (string.IsNullOrEmpty(facType)) return all;

            var ctx = HealthcareValidatorContext.Build(doc);
            HealthcareValidatorContext.SetActive(ctx);
            try
            {
                var allowed = HealthcareValidatorGate.AllowedValidators(doc);

                // Cooperative cancellation at each validator boundary (see
                // HEALTHCARE_WIRING.md "Cancel path"). Synchronous dispatch
                // means the Cancel click lands after the run today; this is the
                // step hook for when a run observes the flag.
                bool cancelled = false;
                void RunIfPicked(string key, HealthcareValidatorBase v)
                {
                    if (cancelled) return;
                    if (HcOptions.CancelRequested) { cancelled = true; return; }
                    if (!picked.Contains(key)) return;
                    if (!allowed.Contains(v.Name)) return;
                    all.AddRange(v.Validate(doc));
                }

                // Map dock-panel keys → validator instances.
                // Match HealthcareTabState.SeedValidatorRows so picks line up.
                RunIfPicked("PressureAudit",     new PressureRegimeValidator());
                RunIfPicked("MgasAudit",         new MgasFlowValidator());
                // MgasVerify is a workflow command, not a passive validator —
                // skip silently if the user ticked it inside the grid.
                RunIfPicked("EesBranch",         new EesBranchValidator());
                RunIfPicked("WaterSafety",       new WaterSafetyValidator());
                RunIfPicked("RadShield",         new RadShieldValidator());
                RunIfPicked("AdvancedRadShield", new AdvancedRadShieldValidator());
                RunIfPicked("AdjacencyAudit",    new AdjacencyValidator());
                RunIfPicked("AntiLigature",      new AntiLigatureValidator());
                RunIfPicked("StructuralLoad",    new StructuralLoadValidator());
                RunIfPicked("Acoustic",          new AcousticValidator());
                RunIfPicked("EndoscopeTrace",    new EndoscopeTraceValidator());
                RunIfPicked("EesResilience",     new EesResilienceValidator());
                RunIfPicked("RtlsCoverage",      new RtlsCoverageValidator());
                RunIfPicked("WasteFlow",         new WasteFlowValidator());
                RunIfPicked("IoTStaleness",      new IoTStalenessValidator());

                if (cancelled) HcOptions.ClearCancel();
                return all;
            }
            finally
            {
                HealthcareValidatorContext.ClearActive();
            }
        }
    }
}
