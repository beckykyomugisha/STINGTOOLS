using System;
using System.Collections.Generic;

namespace StingTools.Standards.NCRP147
{
    /// <summary>
    /// NCRP Report 147 — Structural Shielding Design for Medical X-Ray.
    /// Reference implementation of W·U·T → required mm Pb / mm concrete.
    ///
    /// IMPORTANT: This is a draft tool for use by a Qualified Expert.
    /// STING does NOT certify shielding designs. Output is always
    /// stamped with the QE name (RAD_QE_NAME_TXT) before sign-off.
    /// </summary>
    public static class NCRP147Calculator
    {
        public const double DesignGoalControlledMGyPerYear = 5.0;     // 0.1 mSv/wk × 50 wk
        public const double DesignGoalUncontrolledMGyPerYear = 1.0;   // 0.02 mSv/wk

        // Occupancy factors T per NCRP 147 Table 4.1 (excerpt).
        public static readonly Dictionary<string, double> OccupancyFactor =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Full",       1.0   },
            { "Office",     1.0   },
            { "ControlRoom",1.0   },
            { "Reception",  1.0/2 },
            { "Corridor",   1.0/5 },
            { "WaitArea",   1.0/2 },
            { "Toilets",    1.0/20},
            { "Storage",    1.0/40},
            { "Outdoor",    1.0/40}
        };

        // Use factors U per NCRP 147 Table 4.2 (excerpt) — radiographic rooms.
        public static readonly Dictionary<string, double> UseFactor =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Floor",   1.0  },
            { "WallA",   0.25 }, // primary wall
            { "WallB",   0.25 },
            { "WallC",   0.25 },
            { "Ceiling", 0.0625 } // chest x-ray ceiling primary
        };

        // Approximate transmission (B = 1/T) curves digitised from NCRP 147 Figs.
        // Returns required mm Pb for a given barrier transmission factor B and kVp.
        // Polynomial coefficients (alpha, beta, gamma) per Archer model:
        //   B = (1 + (beta/alpha)) * exp(alpha * gamma * x) - (beta/alpha)
        // Excerpts here cover 100/125/150 kVp lead.
        public static double RequiredLeadMm(double transmission, int kVp)
        {
            if (transmission <= 0 || transmission >= 1) return 0;
            // Digitised α, β, γ for lead at given kVp (approximate).
            double alpha, beta, gamma;
            switch (kVp)
            {
                case 70:  alpha=2.5; beta=15.1; gamma=0.378; break;
                case 100: alpha=2.346; beta=15.1; gamma=0.4; break;
                case 125: alpha=2.009; beta=12.4; gamma=0.41; break;
                case 150: alpha=1.7; beta=10.0; gamma=0.42; break;
                case 200: alpha=1.46; beta=8.5; gamma=0.45; break;
                default:  alpha=2.346; beta=15.1; gamma=0.4; break;
            }
            // x = (1/(alpha*gamma)) * ln( (B^-gamma + beta/alpha) / (1 + beta/alpha) )
            double ratio = beta / alpha;
            double bgam = Math.Pow(transmission, -gamma);
            double x = (1.0 / (alpha * gamma)) * Math.Log((bgam + ratio) / (1.0 + ratio));
            return Math.Max(0, x);
        }

        /// <summary>
        /// Compute the required transmission factor B given workload, use,
        /// occupancy, distance and design goal.
        /// </summary>
        public static double RequiredTransmission(
            double designGoalMGyPerWeek,
            double workloadMaMinPerWeek,
            double useFactor,
            double occupancyFactor,
            double distanceM)
        {
            if (workloadMaMinPerWeek <= 0 || useFactor <= 0 || occupancyFactor <= 0 || distanceM <= 0)
                return 1.0;
            // NCRP 147 §3.2 — primary barrier B = P · d² / (W · U · T)
            // where P = design goal (mGy/wk), W = workload, U = use, T = occupancy.
            double B = designGoalMGyPerWeek * Math.Pow(distanceM, 2) /
                       (workloadMaMinPerWeek * useFactor * occupancyFactor);
            return Math.Min(1.0, Math.Max(1e-9, B));
        }

        public class ShieldingResult
        {
            public string BarrierType;     // PRIMARY / SECONDARY / SCATTER / LEAKAGE
            public double TransmissionRequired;
            public double LeadMmRequired;
            public double DesignGoalMGyPerWeek;
            public bool Sufficient;        // provided ≥ required
            public string QualifiedExpert; // sign-off name (RAD_QE_NAME_TXT)
            public string Note;
        }

        public static ShieldingResult Compute(
            string barrierType,
            string designGoal /* CONTROLLED|UNCONTROLLED */,
            double workloadMaMinPerWeek,
            double useFactor,
            double occupancyFactor,
            double distanceM,
            int kVp,
            double providedLeadMm)
        {
            double goalAnnual = string.Equals(designGoal, "CONTROLLED", StringComparison.OrdinalIgnoreCase)
                ? DesignGoalControlledMGyPerYear : DesignGoalUncontrolledMGyPerYear;
            double goalWeekly = goalAnnual / 50.0;

            double B = RequiredTransmission(goalWeekly, workloadMaMinPerWeek,
                                            useFactor, occupancyFactor, distanceM);
            double leadReq = RequiredLeadMm(B, kVp);
            return new ShieldingResult
            {
                BarrierType = barrierType,
                TransmissionRequired = B,
                LeadMmRequired = leadReq,
                DesignGoalMGyPerWeek = goalWeekly,
                Sufficient = providedLeadMm >= leadReq,
                Note = "Draft for Qualified Expert sign-off — STING does not certify shielding designs."
            };
        }
    }
}
