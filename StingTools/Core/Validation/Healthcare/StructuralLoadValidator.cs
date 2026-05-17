using System;
// Healthcare Pack H-23 — Structural-load validator for heavy imaging.
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class StructuralLoadValidator : HealthcareValidatorBase
    {
        public override string Name => "StructuralLoadValidator";
        private const string Tag = "StructuralLoadValidator";

        // Floor-load minima (kN/m²) per imaging room class.
        private static readonly Dictionary<string, double> MinKnM2 = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "IMG-MRI", 15.0 }, { "IMG-CT", 12.0 }, { "IMG-LIN", 30.0 },
            { "IMG-PET", 15.0 }, { "OR-HYBRID", 12.0 }, { "CATHLAB", 10.0 }
        };
        // Vibration limits VC-A=12.5, VC-B=25, VC-C=12.5 µm/s for sensitive imaging.
        private static readonly Dictionary<string, double> MaxVibUms = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "IMG-MRI", 8.0 }, { "IMG-CT", 25.0 }, { "IMG-LIN", 50.0 }
        };

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var rc = GetRoomClassCached(r);
                if (!MinKnM2.ContainsKey(rc)) continue;
                var loadActual = GetParamDouble(r, "CLN_FLOOR_LOAD_KN_M2_NR") ?? 0;
                var minLoad = MinKnM2[rc];
                if (loadActual > 0 && loadActual < minLoad)
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Error, "STR.LOAD.LOW",
                        $"{r.Name} ({rc}) floor load {loadActual:F1} kN/m² < min {minLoad}", Tag));
                if (MaxVibUms.TryGetValue(rc, out var vMax))
                {
                    var vActual = GetParamDouble(r, "CLN_VIB_VM_NR") ?? 0;
                    if (vActual > 0 && vActual > vMax)
                        res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "STR.VIB.HIGH",
                            $"{r.Name} ({rc}) vibration {vActual:F1} µm/s exceeds {vMax} µm/s for class", Tag));
                }
                if (string.IsNullOrEmpty(GetParam(r, "CLN_STRUCT_SIGN_OFF_TXT")))
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "STR.SIGNOFF.MISSING",
                        $"{r.Name} ({rc}) Structural Engineer sign-off missing", Tag));
            }
            return res;
        }
    }
}
