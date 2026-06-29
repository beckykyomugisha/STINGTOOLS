// StingTools — LCC discounting (WS I10).
//
// The LCC roll-up used an undiscounted annual×years lifetime saving over a
// hardcoded 25-year period. This computes the present value of a level annual
// saving (an annuity) over a configurable period at a configurable discount rate,
// and the period is the project's whole-life study period (aligned with H4), not
// a separate hardcoded constant.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;

namespace StingTools.Core.Sustainability
{
    public static class SustainNpv
    {
        /// <summary>Present value of a level annual saving over <paramref name="years"/>
        /// at <paramref name="discountRatePct"/> (e.g. 3.5 = 3.5%/yr). At ~0% this is
        /// the undiscounted annual×years; a positive rate discounts future savings.</summary>
        public static double PresentValueAnnuity(double annual, int years, double discountRatePct)
        {
            if (years <= 0) return 0;
            double r = discountRatePct / 100.0;
            if (Math.Abs(r) < 1e-9) return annual * years;                 // no discounting
            return annual * (1.0 - Math.Pow(1.0 + r, -years)) / r;        // standard annuity PV
        }
    }
}
