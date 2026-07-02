using StingTools.Core.Validation;
using StingTools.Core.Radiation;
using System;
using Autodesk.Revit.DB;
using StingTools.Standards.NCRP147;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>NCRP 147 — required mm Pb vs provided per barrier element.</summary>
    public class RadShieldValidator : HealthcareValidatorBase
    {
        public override string Name => "RadShieldValidator";
        private const string Tag = "RadShieldValidator";

        // NCRP 147 required shielding scales with d² in B = P·d²/(W·U·T): a LARGER
        // assumed barrier distance UNDER-estimates the required lead, so an optimistic
        // guess can let a too-thin barrier pass. When the distance is not modelled we
        // therefore assume a deliberately CONSERVATIVE (short) default and attach a
        // warning, rather than silently blessing the barrier. A project may model the
        // real distance per element in RAD_DISTANCE_M_NR (read first) or tune the
        // default via PRJ_ORG_HEALTH_RAD_DEFAULT_DIST_M on Project Information — both
        // without a recompile.
        private const double ConservativeDefaultDistanceM = 1.0;

        // Hc.RadRequireQe checkbox toggle. When false the per-element QE
        // sign-off rule (RAD.QE.MISSING) is suppressed — useful for early
        // design when the QE has not yet been engaged.
        public bool RequireQeSignoff { get; set; } = true;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Project-tunable conservative default (falls back to the constant).
            double defaultDistanceM = ConservativeDefaultDistanceM;
            var projDefault = GetParamDouble(doc.ProjectInformation, "PRJ_ORG_HEALTH_RAD_DEFAULT_DIST_M");
            if (projDefault is > 0) defaultDistanceM = projDefault.Value;

            var cats = new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows, BuiltInCategory.OST_GenericModel
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                var barrier = GetParam(el, "RAD_BARRIER_TYPE_TXT");
                if (string.IsNullOrEmpty(barrier)) continue;

                var providedMm = GetParamDouble(el, "RAD_LEAD_MM_NR") ?? 0;
                var workload   = GetParamDouble(el, "RAD_WORKLOAD_MAWK_NR") ?? 0;
                var useFactor  = GetParamDouble(el, "RAD_USE_FACTOR_NR") ?? 0.25;
                var occFactor  = GetParamDouble(el, "RAD_OCC_FACTOR_NR") ?? 1.0;
                var goalCode   = GetParam(el, "RAD_DOSE_DESIGN_GOAL_TXT");

                if (providedMm <= 0)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "RAD.LEAD.MISSING",
                        $"{el.Name} barrier {barrier} missing RAD_LEAD_MM_NR",
                        Tag));
                    continue;
                }

                if (workload > 0)
                {
                    // Prefer a modelled per-element distance; otherwise assume the
                    // conservative default and flag the assumption (see comment above).
                    var modelledDist = GetParamDouble(el, "RAD_DISTANCE_M_NR");
                    bool distanceAssumed = !(modelledDist is > 0);
                    double distanceM = distanceAssumed ? defaultDistanceM : modelledDist.Value;

                    var calc = NCRP147Calculator.Compute(barrier, goalCode, workload, useFactor, occFactor,
                                                        distanceM, 125, providedMm);
                    if (!calc.Sufficient)
                    {
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "RAD.LEAD.UNDER",
                            $"{el.Name} barrier {barrier} provided {providedMm:F1} mm Pb < required {calc.LeadMmRequired:F1} mm at {distanceM:F1} m{(distanceAssumed ? " (assumed)" : "")} [NCRP 147]",
                            Tag));
                    }
                    if (distanceAssumed)
                    {
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "RAD.DIST.ASSUMED",
                            $"{el.Name} barrier {barrier} distance not modelled — required lead computed at a conservative {distanceM:F1} m assumption; set RAD_DISTANCE_M_NR (or run the QE workflow) for a certifiable result [NCRP 147]",
                            Tag));
                    }
                }

                // QE sign-off gate (centralised in RadiationSignoffGate). The
                // panel toggle RequireQeSignoff decides whether to surface the
                // finding at all; the project setting
                // PRJ_ORG_HEALTH_RAD_QE_ENFORCE_TXT=BLOCKING escalates it from
                // Warning to Error — both without a code change.
                if (RequireQeSignoff &&
                    string.Equals(barrier, "PRIMARY", System.StringComparison.OrdinalIgnoreCase) &&
                    !RadiationSignoffGate.IsElementSigned(el))
                {
                    bool blocking = RadiationSignoffGate.IsBlocking(doc);
                    res.Add(new ValidationResult(el.Id,
                        blocking ? ValidationSeverity.Error : ValidationSeverity.Warning,
                        "RAD.QE.MISSING",
                        $"{el.Name} primary barrier missing Qualified Expert sign-off (RAD_QE_NAME_TXT)"
                            + (blocking ? " [project policy: BLOCKING]" : ""),
                        Tag));
                }
            }
            return res;
        }
    }
}
