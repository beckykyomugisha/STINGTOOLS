// Healthcare Pack H-25 — flags PET / NM rooms whose RAD_LEAD_MM_NR
// was derived from kV (NCRP 147) without the 511 keV correction.
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class AdvancedRadShieldValidator : HealthcareValidatorBase
    {
        public override string Name => "AdvancedRadShieldValidator";
        private const string Tag = "AdvancedRadShieldValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToElements()
                .Where(r => GetParam(r, "CLN_ROOM_CLASS_TXT") is "IMG-PET" or "IMG-LIN" or "IMG-NM" or "IMG-BRACHY")
                .ToList();

            foreach (var r in rooms)
            {
                var rc = GetParam(r, "CLN_ROOM_CLASS_TXT");
                // PET / NM rooms need much thicker concrete — Pb-only design under-shields.
                // Encourage explicit RAD_BARRIER_TYPE_TXT review and QE sign-off.
                if (string.IsNullOrEmpty(GetParam(r, "RAD_QE_NAME_TXT")))
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "RAD.HIGH_E.QE_MISSING",
                        $"{r.Name} ({rc}) advanced-imaging shielding without QE sign-off", Tag));
                var leadOnly = (GetParamDouble(r, "RAD_LEAD_MM_NR") ?? 0) > 0;
                if (leadOnly)
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "RAD.HIGH_E.LEAD_ONLY",
                        $"{r.Name} ({rc}) declares Pb-equivalent only; verify concrete thickness for high-energy photons", Tag));
            }
            return res;
        }
    }
}
