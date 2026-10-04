using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation
{
    /// <summary>
    /// The owner-standards verdict. Revit-free so StingTools.Tags.Tests can prove it.
    /// <para>
    /// The audit counted violations only, so a rule that examined NOTHING read "ok" and left the
    /// verdict GREEN: with no element tagged yet, discipline-code-valid checked 0 elements (blank
    /// values are skipped) and the whole audit said GREEN; on a non-workshared model the workset
    /// rule was skipped and still counted as clean (KUT deep review ISO-6 / ACC-13).
    /// </para>
    /// <para>
    /// Now: a BLOCK or WARN rule that was skipped or checked nothing is NOT ASSESSED. The verdict
    /// is RED on any BLOCK failure, AMBER on any WARN failure, and otherwise GREEN only when every
    /// BLOCK and WARN rule was actually assessed — INCOMPLETE when some were not, NOT ASSESSED
    /// when none were. INFO rules never decide the colour, as before.
    /// </para>
    /// </summary>
    public static class OwnerStandardsRag
    {
        public const string Red = "RED", Amber = "AMBER", Green = "GREEN",
                            Incomplete = "INCOMPLETE", NotAssessed = "NOT ASSESSED";

        public struct RuleOutcome
        {
            public string Severity;
            public bool Skipped;
            public int Checked;
            public int Violations;
            public RuleOutcome(string severity, bool skipped, int @checked, int violations)
            { Severity = severity; Skipped = skipped; Checked = @checked; Violations = violations; }
        }

        public static bool IsAssessed(RuleOutcome r) => !r.Skipped && r.Checked > 0;

        /// <summary>The per-rule state shown in the report.</summary>
        public static string State(RuleOutcome r) =>
            r.Skipped ? "SKIP — not assessed"
            : r.Checked == 0 ? "NOT ASSESSED (0 checked)"
            : r.Violations > 0 ? $"{r.Violations} fail"
            : "ok";

        public static string Verdict(IEnumerable<RuleOutcome> outcomes)
        {
            var gating = (outcomes ?? Enumerable.Empty<RuleOutcome>())
                .Where(r => r.Severity == "BLOCK" || r.Severity == "WARN").ToList();
            if (gating.Any(r => r.Severity == "BLOCK" && IsAssessed(r) && r.Violations > 0)) return Red;
            if (gating.Any(r => r.Severity == "WARN" && IsAssessed(r) && r.Violations > 0)) return Amber;
            if (gating.Count == 0 || gating.All(r => !IsAssessed(r))) return NotAssessed;
            return gating.All(IsAssessed) ? Green : Incomplete;
        }
    }
}
