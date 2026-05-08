// Healthcare Pack H-24 — HTM 08-01 NR + RT60 acoustic validator.
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class AcousticValidator : HealthcareValidatorBase
    {
        public override string Name => "AcousticValidator";
        private const string Tag = "AcousticValidator";

        // HTM 08-01 NR target excerpts.
        private static readonly Dictionary<string, int> NrTarget = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "WARD-INPT", 35 }, { "ICU", 35 }, { "NICU", 35 }, { "OR-CONV", 40 },
            { "OR-ULTRA", 40 }, { "PSY-BED", 30 }, { "EXAM", 35 }, { "CONS", 30 },
            { "MAT-LDR", 35 }
        };
        // RT60 baseline (s).
        private static readonly Dictionary<string, double> Rt60Target = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "WARD-INPT", 0.6 }, { "ICU", 0.6 }, { "NICU", 0.5 }, { "OR-CONV", 0.7 },
            { "PSY-BED", 0.6 }, { "EXAM", 0.6 }, { "CONS", 0.6 }
        };

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToElements()
                .Where(r => NrTarget.ContainsKey(GetParam(r, "CLN_ROOM_CLASS_TXT")))
                .ToList();
            foreach (var r in rooms)
            {
                var rc = GetParam(r, "CLN_ROOM_CLASS_TXT");
                var nrAct = GetParamDouble(r, "CLN_ROOM_NOISE_NR_NR")
                            ?? GetParamDouble(r, "PER_ACOUSTICS_BACKGROUND_NOISE_DB");
                var nrTgt = NrTarget[rc];
                if (nrAct.HasValue && nrAct.Value > nrTgt)
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning, "ACO.NR.HIGH",
                        $"{r.Name} ({rc}) NR/dB {nrAct:F0} > HTM 08-01 target {nrTgt}", Tag));
                if (Rt60Target.TryGetValue(rc, out var rtTgt))
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
