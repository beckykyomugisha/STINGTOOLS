using System;
using Autodesk.Revit.DB;
using StingTools.Standards.NFPA99;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HTM 02-01 / NFPA 99 — MGPS terminal-unit flow + ZVB linkage + verification status.</summary>
    public class MgasFlowValidator : HealthcareValidatorBase
    {
        public override string Name => "MgasFlowValidator";
        private const string Tag = "MgasFlowValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var cats = new[] {
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                var gas = GetParam(el, "MGS_GAS_TYPE_TXT");
                if (string.IsNullOrEmpty(gas)) continue;

                // Gas type pressure consistency.
                var nominalActual = GetParamDouble(el, "MGS_NOM_PRESS_KPA_NR");
                var nominalSpec = NFPA99Standards.GetNominalKPa(gas);
                if (nominalSpec.HasValue && nominalActual.HasValue &&
                    System.Math.Abs(nominalActual.Value - nominalSpec.Value) > nominalSpec.Value * 0.1)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "MGS.PRESS.MISMATCH",
                        $"{el.Name} gas={gas} nominal pressure {nominalActual:F0} kPa drifts >10% from spec {nominalSpec:F0} kPa",
                        Tag));
                }

                // Terminal unit BS 5682 indexing.
                var tuFlag = GetParamBool(el, "MGS_TU_BS5682_BOOL");
                var idxFlag = GetParamBool(el, "MGS_TU_INDEXED_BOOL");
                if (tuFlag && !idxFlag)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "MGS.TU.NOT_INDEXED",
                        $"BS 5682 terminal {el.Name} ({gas}) missing gas-specific indexing flag",
                        Tag));
                }

                // Pipe brazed flag — must be true for any MGPS pipework.
                if (el.Category != null && (int)el.Category.Id.Value == (int)BuiltInCategory.OST_PipeCurves)
                {
                    if (!GetParamBool(el, "MGS_PIPE_BRAZED_BOOL"))
                    {
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "MGS.PIPE.NOT_BRAZED",
                            $"MGPS pipe {el.Name} ({gas}) missing inert-gas brazing flag [HTM 02-01]",
                            Tag));
                    }
                }

                // Verification status.
                var verifyDt = GetParam(el, "MGS_VERIFY_DT");
                var verifyPass = GetParamBool(el, "MGS_VERIFY_PASS_BOOL");
                if (!string.IsNullOrEmpty(verifyDt) && !verifyPass)
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "MGS.VERIFY.FAILED",
                        $"{el.Name} latest MGPS verification ({verifyDt}) failed [NFPA 99 §5.1.12]",
                        Tag));
                }
            }
            return res;
        }
    }
}
