// CircuitComplianceRule — Revit-free per-circuit BS 7671 check (ROADMAP PNL-2).
//
// One verdict per way, written to ELC_CKT_CHECK_TXT so it appears as a column in
// the STING panel schedules and in any circuit schedule:
//   Ib ≤ In       design current within the device rating       (Reg 433.1.1)
//   In ≤ Iz       device within the cable's current-carrying capacity (433.1.1)
//   VD ≤ limit    voltage drop within the Appendix 12 limit     (525)
//   PSC ≤ Icn     prospective fault within the device breaking capacity (434.5.1)
// A rule whose inputs are missing is reported as NOT CHECKED — never as a pass.
// "OK" means every rule that could run passed; the summary always says which
// could not run, so a green cell never hides an unchecked rule.

using System.Collections.Generic;
using System.Globalization;

namespace StingTools.Core.Electrical
{
    public sealed class CircuitCheckInput
    {
        public double IbA;                 // design current (0 = unknown)
        public double InA;                 // device rating (0 = unknown)
        public double? IzA;                // cable capacity; null = unknown
        public string IzBasis = "";        // e.g. "4D2A method C, no derating"
        public double? VdPct;              // voltage drop %; null = not calculated
        public double VdLimitPct;          // limit for this circuit
        public double? ProspectiveFaultKa; // at the board; null = unknown
        public double? BreakingCapacityKa; // device/board Icn; null = unknown
    }

    public sealed class CircuitCheckResult
    {
        public List<string> Failures { get; } = new List<string>();
        public List<string> NotChecked { get; } = new List<string>();
        public bool Failed => Failures.Count > 0;
        /// <summary>Every rule ran and passed.</summary>
        public bool FullyVerified => Failures.Count == 0 && NotChecked.Count == 0;

        /// <summary>The text written to ELC_CKT_CHECK_TXT.</summary>
        public string Summary =>
            Failed ? "FAIL: " + string.Join("; ", Failures)
                     + (NotChecked.Count > 0 ? " | not checked: " + string.Join(", ", NotChecked) : "")
            : NotChecked.Count > 0 ? "OK (not checked: " + string.Join(", ", NotChecked) + ")"
            : "OK";
    }

    public static class CircuitComplianceRule
    {
        private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        public static CircuitCheckResult Evaluate(CircuitCheckInput c)
        {
            var r = new CircuitCheckResult();

            if (c.IbA > 0 && c.InA > 0)
            {
                if (c.IbA > c.InA + 1e-9) r.Failures.Add($"Ib {N(c.IbA)} A > In {N(c.InA)} A");
            }
            else r.NotChecked.Add(c.InA > 0 ? "Ib (no load)" : "In (no device rating)");

            if (c.InA > 0 && c.IzA.HasValue && c.IzA.Value > 0)
            {
                if (c.InA > c.IzA.Value + 1e-9)
                    r.Failures.Add($"In {N(c.InA)} A > Iz {N(c.IzA.Value)} A");
            }
            else r.NotChecked.Add("Iz (cable size/table)");

            if (c.VdPct.HasValue && c.VdLimitPct > 0)
            {
                if (c.VdPct.Value > c.VdLimitPct + 1e-9)
                    r.Failures.Add($"VD {c.VdPct.Value.ToString("0.0", CultureInfo.InvariantCulture)} % > {N(c.VdLimitPct)} %");
            }
            else r.NotChecked.Add("VD (run Recalculate)");

            if (c.ProspectiveFaultKa.HasValue && c.ProspectiveFaultKa.Value > 0
                && c.BreakingCapacityKa.HasValue && c.BreakingCapacityKa.Value > 0)
            {
                if (c.ProspectiveFaultKa.Value > c.BreakingCapacityKa.Value + 1e-9)
                    r.Failures.Add($"PSC {N(c.ProspectiveFaultKa.Value)} kA > Icn {N(c.BreakingCapacityKa.Value)} kA");
            }
            else r.NotChecked.Add("breaking capacity (fault level or device kA)");

            return r;
        }
    }
}
