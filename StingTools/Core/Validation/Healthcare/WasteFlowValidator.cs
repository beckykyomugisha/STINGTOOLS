using StingTools.Core.Validation;
using System;
// Healthcare Pack H-30 — clinical-waste flow validator (HTM 07-01).
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class WasteFlowValidator : HealthcareValidatorBase
    {
        public override string Name => "WasteFlowValidator";
        private const string Tag = "WasteFlowValidator";

        // Waste classes per HTM 07-01.
        private static readonly HashSet<string> AllowedClasses = new(System.StringComparer.OrdinalIgnoreCase)
            { "HC1-General","HC2-Offensive","HC3-Infectious","HC4-Anatomical","HC5-Cytotoxic","HC6-Radioactive" };

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            // Waste-flow checks every room (HC1-General can apply to any), not
            // just clinical ones — use the broader cached set.
            var rooms = GetAllRoomsCached(doc);

            foreach (var r in rooms)
            {
                var cls = GetParam(r, "CLN_WASTE_CLASS_TXT");
                if (string.IsNullOrEmpty(cls)) continue;
                if (!AllowedClasses.Contains(cls))
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "WASTE.CLASS.UNKNOWN",
                        $"{r.Name} CLN_WASTE_CLASS_TXT='{cls}' not in HC1..HC6", Tag));
                // Radioactive waste rooms must be IRR17 controlled.
                if (cls == "HC6-Radioactive" && !GetParamBool(r, "CLN_RAD_CONTROLLED_BOOL"))
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Error, "WASTE.RAD.UNCONTROLLED",
                        $"{r.Name} carries radioactive waste but CLN_RAD_CONTROLLED_BOOL=false", Tag));
            }
            return res;
        }
    }
}
