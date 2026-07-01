using StingTools.Core.Validation;
using StingTools.Standards.HTM;
using System;
// Healthcare Pack H-24 — HTM 08-01 NR + RT60 acoustic validator.
// NR/RT60 targets are sourced from HTMStandards (HTM 08-01) so threshold
// governance lives in one place, like the other healthcare validators.
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class AcousticValidator : HealthcareValidatorBase
    {
        public override string Name => "AcousticValidator";
        private const string Tag = "AcousticValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var rc = GetRoomClassCached(r);
                var nrTgtOpt = HTMStandards.GetNrTarget(rc);
                if (nrTgtOpt == null) continue;
                var nrAct = GetParamDouble(r, "CLN_NOISE_NR_NR")
                            ?? GetParamDouble(r, "PER_ACOUSTICS_BACKGROUND_NOISE_DB");
                var nrTgt = nrTgtOpt.Value;
                if (nrAct.HasValue && nrAct.Value > nrTgt)
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "ACO.NR.HIGH",
                        $"{r.Name} ({rc}) NR/dB {nrAct:F0} > HTM 08-01 target {nrTgt}", Tag));
                if (HTMStandards.GetRt60Target(rc) is double rtTgt)
                {
                    var rtAct = GetParamDouble(r, "PER_ACOUSTICS_RT60_S")
                                ?? GetParamDouble(r, "PER_ACOUSTICS_REVERBERATION_TIME_SEC_NR");
                    if (rtAct.HasValue && rtAct.Value > rtTgt + 0.1)
                        res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "ACO.RT60.HIGH",
                            $"{r.Name} ({rc}) RT60 {rtAct:F2} s > target {rtTgt} s", Tag));
                }
            }
            return res;
        }
    }
}
