using System;
using Autodesk.Revit.DB;
using StingTools.Standards.HTM;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HTM 04-01 — TMV3, sentinel dead-leg ≤ 1 m, augmented-care POU
    /// filters, RO-loop topology, hot/cold water temperature window.</summary>
    public class WaterSafetyValidator : HealthcareValidatorBase
    {
        public override string Name => "WaterSafetyValidator";
        private const string Tag = "WaterSafetyValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var cats = new[] {
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeAccessory,
                // Dialysis stations / RO plants live under Medical Equipment.
                BuiltInCategory.OST_MedicalEquipment
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                // Sentinel dead-leg check.
                var sentinel = GetParamBool(el, "PLM_SENTINEL_BOOL");
                var deadLegM = GetParamDouble(el, "PLM_DEAD_LEG_M_NR");
                if (sentinel && deadLegM.HasValue && deadLegM.Value > HTMStandards.DeadLegSentinelMaxM)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "PLM.DEADLEG.OVER",
                        $"{el.Name} sentinel point dead-leg {deadLegM:F2} m > HTM 04-01 max {HTMStandards.DeadLegSentinelMaxM} m",
                        Tag));
                }

                // Augmented care + no POU filter.
                if (GetParamBool(el, "PLM_AUG_CARE_BOOL") && !GetParamBool(el, "PLM_POU_FILTER_BOOL"))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                        "PLM.AUGCARE.NO_FILTER",
                        $"{el.Name} augmented-care outlet missing point-of-use filter [HTM 04-01 Pt C — Pseudomonas]",
                        Tag));
                }

                // TMV outlet temperature window.
                var hot = GetParamDouble(el, "PLM_HOTWTR_TEMP_C");
                var tmv = GetParam(el, "PLM_TMV_TYPE_TXT");
                if (!string.IsNullOrEmpty(tmv) && tmv != "NONE" && hot.HasValue &&
                    (hot.Value < HTMStandards.TmvOutletMinC || hot.Value > HTMStandards.TmvOutletMaxC))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                        "PLM.TMV.OUTLET_TEMP",
                        $"{el.Name} TMV outlet {hot:F1} °C outside HTM 04-01 window {HTMStandards.TmvOutletMinC}–{HTMStandards.TmvOutletMaxC} °C",
                        Tag));
                }

                // Dialysis station belongs to RO loop?
                var prod = GetParam(el, "ASS_PRODCT_COD_TXT");
                if (string.Equals(prod, "RO-DIA", System.StringComparison.OrdinalIgnoreCase) &&
                    !GetParamBool(el, "PLM_RO_LOOP_BOOL"))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "PLM.RO.LOOP_MISSING",
                        $"Dialysis station {el.Name} not flagged as RO-loop member [HBN 07-02]",
                        Tag));
                }
            }
            return res;
        }
    }
}
