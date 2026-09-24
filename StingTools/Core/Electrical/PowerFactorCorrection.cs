// PowerFactorCorrection — Revit-free capacitor-bank sizing.
//
// Q_c = P · (tan φ1 − tan φ2), with φ = acos(pf). That is the whole of it: the
// reactive power drawn at the present power factor minus the reactive power
// still drawn at the target. It needs no lookup table.
//
// It replaces the "kvarPerKwAtPf" table STING_DIVERSITY_FACTORS.json carried
// until 2026-09 (ROADMAP ELEC-6). That table was not tan φ at any entry
// (0.85 → 0.31 where tan(acos 0.85) = 0.620; 0.95 → 0.10 where it is 0.329),
// so it undersized every bank - 21 kVAR instead of 29 kVAR for 100 kW lifted
// from 0.85 to 0.95.

using System;

namespace StingTools.Core.Electrical
{
    public static class PowerFactorCorrection
    {
        /// <summary>tan(acos(pf)) = √(1 − pf²) / pf: kVAR drawn per kW at a lagging power factor.</summary>
        public static double KvarPerKw(double pf)
        {
            if (pf <= 0 || pf > 1) throw new ArgumentOutOfRangeException(nameof(pf), pf, "power factor must be in (0, 1]");
            return Math.Sqrt(1.0 - pf * pf) / pf;
        }

        /// <summary>
        /// Capacitor kVAR to lift active power <paramref name="activeKw"/> from
        /// <paramref name="presentPf"/> to <paramref name="targetPf"/>: P·(tan φ1 − tan φ2).
        /// 0 when the present PF already meets the target.
        /// </summary>
        public static double RequiredKvar(double activeKw, double presentPf, double targetPf)
        {
            if (activeKw <= 0 || presentPf >= targetPf) return 0;
            return activeKw * (KvarPerKw(presentPf) - KvarPerKw(targetPf));
        }

        /// <summary>
        /// Same, from APPARENT power (what Revit's RBS_ELEC_APPARENT_LOAD holds):
        /// P = S·cos φ1, then P·(tan φ1 − tan φ2).
        /// </summary>
        public static double RequiredKvarFromKva(double apparentKva, double presentPf, double targetPf)
            => RequiredKvar(apparentKva * presentPf, presentPf, targetPf);
    }
}
