using StingTools.Core.Validation;
using System;
using Autodesk.Revit.DB;
using StingTools.Core.Plumbing;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HTM 04-01 — TMV outlet limits (TMV3 required), sentinel spur
    /// length (HTM 04-01 Pt A §12.5, 3 m), augmented-care POU filters, RO-loop
    /// topology. Limits come from STING_TMV_STANDARDS.json (WaterSafetyLimits);
    /// unusable data means NOT CHECKED, never a constant.</summary>
    public class WaterSafetyValidator : HealthcareValidatorBase
    {
        public override string Name => "WaterSafetyValidator";
        private const string Tag = "WaterSafetyValidator";

        /// <summary>Panel override (Hc.DeadLegMaxM). It may only tighten the HTM 04-01
        /// spur limit; zero or less = no override.</summary>
        public double DeadLegOverrideM { get; set; }

        /// <summary>The limit applied: the data's healthcare spur limit, tightened by
        /// the override. Null when the data is unusable (NOT CHECKED).</summary>
        public double? DeadLegMaxM
        {
            get
            {
                var spur = PlumbingTables.WaterSafety?.DeadLegLimits?.HealthcareSpur;
                if (spur == null) return null;
                return WaterSafetyLimits.TightenOnly(spur.ValueM, DeadLegOverrideM, out _);
            }
        }

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

            var limits = PlumbingTables.WaterSafety;
            double? deadLegMax = DeadLegMaxM;
            var spur = limits?.DeadLegLimits?.HealthcareSpur;
            if (limits == null)
                res.Add(new ValidationResult(ElementId.InvalidElementId, ValidationSeverity.Warning,
                    "PLM.WATERSAFETY.NOT_CHECKED",
                    "NOT CHECKED — STING_TMV_STANDARDS.json unusable, so TMV and dead-leg limits were not applied: "
                    + string.Join("; ", PlumbingTables.WaterSafetyErrors), Tag));
            else if (spur != null)
            {
                WaterSafetyLimits.TightenOnly(spur.ValueM, DeadLegOverrideM, out bool ignored);
                if (ignored)
                    res.Add(new ValidationResult(ElementId.InvalidElementId, ValidationSeverity.Info,
                        "PLM.DEADLEG.OVERRIDE_IGNORED",
                        $"Dead-leg override {DeadLegOverrideM:0.##} m is looser than {spur.ValueM:0.##} m ({spur.Source}) — ignored; an override may only tighten",
                        Tag));
            }

            foreach (var el in els)
            {
                // Sentinel dead-leg check.
                var sentinel = GetParamBool(el, "PLM_SENTINEL_BOOL");
                var deadLegM = GetParamDouble(el, "PLM_DEAD_LEG_M_NR");
                if (sentinel && deadLegM.HasValue && deadLegMax.HasValue && deadLegM.Value > deadLegMax.Value)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "PLM.DEADLEG.OVER",
                        $"{el.Name} sentinel point spur {deadLegM:F2} m > max {deadLegMax.Value:0.##} m [{spur?.Source}]",
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

                // TMV outlet limit — by outlet, scheme and assisted bathing (DSCH-25).
                var tmvType  = GetParam(el, ParamRegistry.PLM_TMV_TYPE_TXT);
                var tmvClass = GetParam(el, ParamRegistry.PLM_TMV_CLASS);
                bool hasTmv  = (!string.IsNullOrWhiteSpace(tmvType) && !tmvType.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                            || !string.IsNullOrWhiteSpace(tmvClass);
                if (hasTmv)
                {
                    string assistedRaw = GetParam(el, ParamRegistry.PLM_TMV_ASSISTED_BOOL);
                    bool? assisted = string.IsNullOrEmpty(assistedRaw) ? (bool?)null : assistedRaw != "0";
                    var c = WaterSafetyLimits.CheckTmv(limits,
                        WaterSafetyLimits.NormaliseOutlet(GetParam(el, ParamRegistry.PLM_FIX_TYPE_TXT)),
                        WaterSafetyLimits.NormaliseScheme(tmvClass) ?? WaterSafetyLimits.NormaliseScheme(tmvType),
                        assisted, isHealthcare: true,
                        GetParamDouble(el, ParamRegistry.PLM_TMV_BLEND) ?? 0,
                        GetParamDouble(el, ParamRegistry.PLM_TMV_MEASURED_C) ?? 0);
                    if (c.Status == WaterCheckStatus.Fail)
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "PLM.TMV.OUTLET_TEMP", $"{el.Name} TMV: {c.Reason} [{c.StandardRef}]", Tag));
                    else if (c.Status == WaterCheckStatus.NotChecked && limits != null)
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Info,
                            "PLM.TMV.NOT_CHECKED", $"{el.Name} TMV: {c.Reason}", Tag));
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
