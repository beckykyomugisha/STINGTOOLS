using StingTools.Core.Validation;
// Healthcare Pack H-27 — EES resilience: NFPA 110 generator-test
// log freshness, ATS test log freshness, UPS battery age.
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Validation.Healthcare
{
    public class EesResilienceValidator : HealthcareValidatorBase
    {
        public override string Name => "EesResilienceValidator";
        private const string Tag = "EesResilienceValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            var pi = doc.ProjectInformation;
            if (pi == null) return res;

            // Generator test log — every 35 days max (4-week buffer).
            CheckLogFreshness(pi, "ELC_GEN_TEST_LOG_REF_TXT", 35,
                "EES.GEN.TEST_STALE", "Generator test log not updated within 35 days [NFPA 110]", res);
            CheckLogFreshness(pi, "ELC_ATS_TEST_LOG_REF_TXT", 35,
                "EES.ATS.TEST_STALE", "ATS test log not updated within 35 days [NFPA 110]", res);
            CheckLogFreshness(pi, "ELC_UPS_REPLACE_DT", 365 * 4,
                "EES.UPS.OLD", "UPS battery replacement older than 4 years", res);
            return res;
        }

        private void CheckLogFreshness(Element pi, string param, int maxDays, string code, string msg, List<ValidationResult> res)
        {
            var raw = GetParam(pi, param);
            if (string.IsNullOrEmpty(raw)) return;
            // Extract a yyyy-mm-dd substring if the field is a doc-ref like "GEN-TEST-2026-04-15".
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"\d{4}-\d{2}-\d{2}");
            if (!match.Success) return;
            if (!DateTime.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal, out var dt)) return;
            if ((DateTime.UtcNow - dt).TotalDays > maxDays)
                res.Add(new ValidationResult(pi.Id, ValidationSeverity.Warning, code, $"{msg} (last {dt:yyyy-MM-dd})", Tag));
        }
    }
}
