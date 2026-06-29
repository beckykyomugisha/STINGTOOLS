// StingTools — EDGE export cell formatting (WS I4).
//
// The EDGE export must NEVER print a bare number for a gate the dashboard shows as
// "Not computed" (a user could paste it into the EDGE app as if real). This pure
// helper renders a value only when it was computed; otherwise it prints the same
// "not computed / indicative default" the dashboard uses, so export + dashboard
// agree.
//
// Pure POCO — no Revit dependency. Unit-tested. The Revit-facing export
// (SustainExportCommands) calls these for every gate-derived cell.

using System.Globalization;

namespace StingTools.Core.Sustainability
{
    public static class SustainExportFormat
    {
        public const string NotComputed = "not computed — indicative default";

        /// <summary>Render a gate value only when computed; else the not-computed text.
        /// <paramref name="computed"/> should already fold in readiness (a blocked run
        /// is never computed).</summary>
        public static string GateValue(bool computed, double value, string fmt = "0.0", string suffix = "")
            => computed ? value.ToString(fmt, CultureInfo.InvariantCulture) + suffix : NotComputed;

        /// <summary>A delegated gate (EDGE-app owns the certified number) is never a
        /// bare STING figure on the export.</summary>
        public static string Delegated(double indicativeValue, string fmt = "0.0", string suffix = "")
            => "→ EDGE app (STING indicative " + indicativeValue.ToString(fmt, CultureInfo.InvariantCulture) + suffix + ")";
    }
}
