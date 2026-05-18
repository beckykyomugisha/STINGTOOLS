using StingTools.Core.Validation;
using System;
using Autodesk.Revit.DB;
using StingTools.Standards.HTM;
using StingTools.Standards.ASHRAE170;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HTM 03-01 / ASHRAE 170 — pressure cascade + ACH check per
    /// CLN_ROOM_CLASS_TXT. Anteroom cascades evaluated when CLN_ANTERM_LINKED_ID_TXT is set.</summary>
    public class PressureRegimeValidator : HealthcareValidatorBase
    {
        public override string Name => "PressureRegimeValidator";
        private const string Tag = "PressureRegimeValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Cache-aware: pulls from HealthcareValidatorContext when running
            // inside RunAllHealthcareValidators; falls back to its own collector otherwise.
            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var rc = GetRoomClassCached(r);
                if (string.IsNullOrEmpty(rc)) continue;

                // 1. Pressure regime vs HTM design table.
                var actual = GetParam(r, "CLN_PRESS_REGIME_TXT");
                var design = HTMStandards.GetDesignRegime(rc);
                if (!string.IsNullOrEmpty(design) && !string.IsNullOrEmpty(actual)
                    && !string.Equals(design, actual, System.StringComparison.OrdinalIgnoreCase))
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Error,
                        "CLN.PRESS.REGIME",
                        $"Room {r.Name} class {rc} expects {design} pressure but is {actual} [HTM 03-01 / ASHRAE 170]",
                        Tag));
                }

                // 2. Δp design value.
                var dPaDesign = HTMStandards.GetDesignDeltaPa(rc);
                var dPaActual = GetParamDouble(r, "CLN_PRESS_DELTA_DESIGN_PA_NR");
                if (dPaDesign.HasValue && dPaActual.HasValue && dPaActual.Value < dPaDesign.Value - 1)
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                        "CLN.PRESS.DELTA_PA_LOW",
                        $"Room {r.Name} class {rc} design Δp={dPaActual:F1} Pa < HTM-recommended {dPaDesign} Pa",
                        Tag));
                }

                // 3. ACH minimum.
                var achMin = HTMStandards.GetMinAch(rc);
                var achActual = GetParamDouble(r, "HVC_AIR_CHANGES_PER_HR");
                if (achMin.HasValue && achActual.HasValue && achActual.Value < achMin.Value - 0.5)
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Error,
                        "CLN.ACH.LOW",
                        $"Room {r.Name} class {rc} ACH={achActual:F1} < HTM minimum {achMin} [HTM 03-01]",
                        Tag));
                }

                // 4. Outside-air ACH ≥ ASHRAE 170 minimum (if specified).
                var sp = ASHRAE170Standards.Lookup(rc);
                var outsideActual = GetParamDouble(r, "HVC_ACH_OUTSIDE_NR");
                if (sp != null && outsideActual.HasValue && outsideActual.Value < sp.MinOutsideAch - 0.5)
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                        "CLN.ACH.OUTSIDE_LOW",
                        $"Room {r.Name} outside-air ACH={outsideActual:F1} < ASHRAE 170 min {sp.MinOutsideAch}",
                        Tag));
                }

                // 5. AIIR / PE require linked anteroom.
                var infect = GetParam(r, "CLN_INFECT_CLASS_TXT");
                if ((infect == "AIIR" || infect == "PE") &&
                    string.IsNullOrEmpty(GetParam(r, "CLN_ANTERM_LINKED_ID_TXT")))
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                        "CLN.ANTERM.MISSING",
                        $"{infect} room {r.Name} missing linked anteroom (CLN_ANTERM_LINKED_ID_TXT empty)",
                        Tag));
                }
            }

            return res;
        }
    }
}
