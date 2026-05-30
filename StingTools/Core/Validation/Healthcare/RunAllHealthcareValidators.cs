using System;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>Aggregates every healthcare validator into a single sweep.
    /// Gated on PRJ_ORG_HEALTH_FACILITY_TYPE_TXT being non-empty so generic
    /// projects do not pay the cost. Individual validators are filtered
    /// by HealthcareValidatorGate against PRJ_ORG_HEALTH_PACK_PROFILE_TXT.</summary>
    public static class RunAllHealthcareValidators
    {
        public static List<ValidationResult> Validate(Document doc)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;

            string facType = "";
            try
            {
                var pi = doc.ProjectInformation;
                var p = pi?.LookupParameter("PRJ_ORG_HEALTH_FACILITY_TYPE_TXT");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    facType = (p.AsString() ?? "").Trim();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            if (string.IsNullOrEmpty(facType)) return all;

            // Build the shared cache once for the whole chain so each individual
            // validator skips its own room collector + CLN_ROOM_CLASS_TXT pass.
            // Stashed in a [ThreadStatic] so each Validate(doc) implementation
            // can opt in via HealthcareValidatorContext.Active without
            // changing the public Validate(Document) signature.
            var ctx = HealthcareValidatorContext.Build(doc);
            HealthcareValidatorContext.SetActive(ctx);
            try
            {
                var allowed = HealthcareValidatorGate.AllowedValidators(doc);
                // Cooperative cancellation: Healthcare_Cancel sets Hc.CancelRequested.
                // We poll at each validator boundary and stop cleanly. NOTE: the
                // dock-panel dispatch is synchronous on the Revit API thread, so a
                // Cancel click cannot be processed mid-run today — this poll is the
                // step-boundary hook that takes effect once a run sets the flag
                // (e.g. a pre-armed cancel, or a future chunked/async run). See
                // HEALTHCARE_WIRING.md "Cancel path".
                bool cancelled = false;
                void Run(HealthcareValidatorBase v)
                {
                    if (cancelled) return;
                    if (HcOptions.CancelRequested) { cancelled = true; return; }
                    if (allowed.Contains(v.Name)) all.AddRange(v.Validate(doc));
                }

                Run(new PressureRegimeValidator());
                Run(new MgasFlowValidator());
                Run(new EesBranchValidator());
                Run(new WaterSafetyValidator());
                Run(new RadShieldValidator());
                Run(new AdjacencyValidator());
                Run(new AntiLigatureValidator());
                Run(new RdsCompletenessValidator());
                Run(new IoTStalenessValidator());
                Run(new StructuralLoadValidator());
                Run(new AcousticValidator());
                Run(new AdvancedRadShieldValidator());
                Run(new EndoscopeTraceValidator());
                Run(new EesResilienceValidator());
                Run(new RtlsCoverageValidator());
                Run(new WasteFlowValidator());
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
