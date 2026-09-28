// DisplayGateRule — which identifiers are TIER DISPLAY GATES.
//
// Extracted from FormulaEngine so the rule can be tested without Revit, and so
// there is exactly one definition of "is this a display gate" rather than a
// regex repeated at each site that needs one.
//
// WHY THE DISTINCTION MATTERS
//
// TAG_PARA_STATE_<n>_BOOL decides whether a TAG DRAWS a tier. It is a
// presentation setting that lives on tag families. It is not data.
//
// Formula expressions reference it anyway — 36 of the TAG7 narrative formulas
// wrap their whole body in if(TAG_PARA_STATE_3_BOOL, ..., "") — so when the gate
// is not bound to the element the identifier is unresolvable and the entire
// formula fails. A presentation setting then destroys data: the narrative is
// never computed, and the failure is indistinguishable from a formula that had
// nothing to say.
//
// So an unbound display gate reads as OPEN. Computing text that no label row
// shows costs nothing; refusing to compute it costs the value.
//
// The rule is deliberately narrow — only this one name shape defaults. Every
// other unresolvable identifier still fails loudly, because those are genuine
// data errors (a typo, or a formula running against a category whose parameters
// it does not have) and silencing them would hide real breakage.

using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class DisplayGateRule
    {
        // Anchored, and \d+ rather than a hand-listed 1..10, so adding a tier
        // needs no edit here. IgnoreCase because parameter lookup is
        // case-insensitive and the rule must agree with it.
        private static readonly Regex GateName =
            new Regex(@"^TAG_PARA_STATE_\d+_BOOL$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True for a tier display gate — the only identifier shape allowed to
        /// default to open when it is not bound to the element.
        /// </summary>
        public static bool IsDisplayGate(string name)
            => !string.IsNullOrEmpty(name) && GateName.IsMatch(name);
    }
}
