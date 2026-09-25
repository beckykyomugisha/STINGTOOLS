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
        /// <summary>
        /// Iz is the tabulated It with no Ca/Cg/Ci applied — an upper bound on the real
        /// Iz. In &gt; It is then a conclusive FAIL, but In ≤ It proves nothing, so a pass
        /// is reported as "not checked: derating", never as a clean OK.
        /// </summary>
        public bool IzIsUpperBound;
        public double? VdPct;              // voltage drop %; null = not calculated
        public double VdLimitPct;          // limit for this circuit
        public double? ProspectiveFaultKa; // at the board; null = unknown
        public double? BreakingCapacityKa; // device/board Icn; null = unknown
        /// <summary>
        /// A typical Icn used ONLY when the family carries none (see TypicalIcnKa). It is
        /// never a basis for a pass or a fail: a pass against it is reported as "Icn
        /// assumed", and a PSC above it as "confirm the device Icn". Both stay NOT CHECKED.
        /// </summary>
        public double? AssumedBreakingCapacityKa;
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
            // "OK" only when every rule ran and passed. A circuit with no failure but an
            // unchecked rule is UNVERIFIED — in a schedule column, anything starting
            // "OK" reads as a pass whatever follows it.
            : NotChecked.Count > 0 ? "UNVERIFIED (not checked: " + string.Join(", ", NotChecked) + ")"
            : "OK";
    }

    public static class CircuitComplianceRule
    {
        private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>
        /// Low-end typical breaking capacity for a protective device of rating In:
        /// 6 kA for an MCB (In ≤ 63 A, BS EN 60898 — the common commercial rating) and
        /// 16 kA for an MCCB above that. Common ratings, not a floor — 3 kA and 4.5 kA MCBs
        /// exist, which is why this value can prompt a check but can
        /// never make a circuit pass. 0 when In is unknown.
        /// </summary>
        public static double TypicalIcnKa(double inA) => inA <= 0 ? 0 : inA <= 63 ? 6 : 16;

        /// <summary>
        /// A breaking capacity in kA. "10 kA" is kA; a value with no kA unit above 200 is
        /// amps (no LV device is rated above 200 kA), so "10000" or "10000 A" → 10 kA.
        /// Reading amps as kA would make the PSC ≤ Icn check impossible to fail.
        /// </summary>
        public static double BreakingCapacityKa(double value, bool hasKaUnit)
            => hasKaUnit || value <= 200 ? value : value / 1000.0;

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
                else if (c.IzIsUpperBound)
                    r.NotChecked.Add("Iz derating (Ca/Cg/Ci)");
            }
            else r.NotChecked.Add("Iz (cable size/table)");

            if (c.VdPct.HasValue && c.VdLimitPct > 0)
            {
                if (c.VdPct.Value > c.VdLimitPct + 1e-9)
                    r.Failures.Add($"VD {c.VdPct.Value.ToString("0.00", CultureInfo.InvariantCulture)} % > {N(c.VdLimitPct)} %");
            }
            else r.NotChecked.Add("VD (run Recalculate)");

            bool havePsc = c.ProspectiveFaultKa.HasValue && c.ProspectiveFaultKa.Value > 0;
            if (havePsc && c.BreakingCapacityKa.HasValue && c.BreakingCapacityKa.Value > 0)
            {
                if (c.ProspectiveFaultKa.Value > c.BreakingCapacityKa.Value + 1e-9)
                    r.Failures.Add($"PSC {N(c.ProspectiveFaultKa.Value)} kA > Icn {N(c.BreakingCapacityKa.Value)} kA");
            }
            else if (havePsc && c.AssumedBreakingCapacityKa.HasValue && c.AssumedBreakingCapacityKa.Value > 0)
            {
                double a = c.AssumedBreakingCapacityKa.Value;
                r.NotChecked.Add(c.ProspectiveFaultKa.Value > a + 1e-9
                    ? $"breaking capacity: PSC {N(c.ProspectiveFaultKa.Value)} kA > {N(a)} kA typical — confirm device Icn"
                    : $"breaking capacity (device Icn assumed {N(a)} kA)");
            }
            else r.NotChecked.Add("breaking capacity (fault level or device kA)");

            return r;
        }
    }
}
