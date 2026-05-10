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

        // Hc.RadRequireQe checkbox toggle. When false the per-element QE
        // sign-off rule (RAD.QE.MISSING) is suppressed — useful for early
        // design when the QE has not yet been engaged.
        public bool RequireQeSignoff { get; set; } = true;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

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
                var qe         = GetParam(el, "RAD_QE_NAME_TXT");

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
                    // Distance unknown at validation time — use 2.0 m default;
                    // real distance comes from the QE workflow command.
                    var calc = NCRP147Calculator.Compute(barrier, goalCode, workload, useFactor, occFactor,
                                                        2.0, 125, providedMm);
                    if (!calc.Sufficient)
                    {
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "RAD.LEAD.UNDER",
                            $"{el.Name} barrier {barrier} provided {providedMm:F1} mm Pb < required {calc.LeadMmRequired:F1} mm at 2 m default [NCRP 147]",
                            Tag));
                    }
                }

                if (RequireQeSignoff &&
                    string.Equals(barrier, "PRIMARY", System.StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(qe))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                        "RAD.QE.MISSING",
                        $"{el.Name} primary barrier missing Qualified Expert sign-off (RAD_QE_NAME_TXT)",
                        Tag));
                }
            }
            return res;
        }
    }
}
