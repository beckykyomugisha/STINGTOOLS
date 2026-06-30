// ══════════════════════════════════════════════════════════════════════════
//  LossAndExpenseEngine.cs — value a loss-&-expense / compensation-event claim
//  off the EOT days already captured on a variation. PM-3.
//
//  The audit (§5 capability 6) found EOT days are captured on a VO
//  (VariationInstruction.EotDays) but never valued — a granted extension of time
//  with no money attached. This turns those days into a defensible prolongation
//  cost using the standard contractor build-up:
//
//    prolongation   = weeks(EOT) × time-related preliminaries per week
//    head-office OHP = prolongation × HeadOfficeOhpPct   (Hudson/Emden-style
//                      thickening of off-site overhead + profit on prolonged time)
//    total          = prolongation + head-office OHP + disruption + finance
//
//  Weeks are EOT calendar days ÷ 7 by default; a project on a 6-day week passes
//  daysPerWeek = 6 so prolongation is costed on working weeks. Nothing is
//  invented — the inputs are the captured EOT days and the project's own weekly
//  prelims figure.
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Cost
{
    public class LossExpenseInput
    {
        /// <summary>Granted extension of time, in days (VariationInstruction.EotDays).</summary>
        public int EotDays { get; set; }
        /// <summary>Time-related site preliminaries per week (staff, welfare, plant
        /// standing, accommodation, scaffold hire …) — the cost of being on site
        /// one more week.</summary>
        public double WeeklyPrelimsUGX { get; set; }
        /// <summary>Days per prolongation week (7 calendar default; 6 for a 6-day
        /// site week). Clamped to [1,7].</summary>
        public int DaysPerWeek { get; set; } = 7;
        /// <summary>Off-site overhead + profit thickening on the prolonged period,
        /// as a % of prolongation (Hudson/Emden-style). 0 ⇒ none.</summary>
        public double HeadOfficeOhpPct { get; set; }
        /// <summary>Separately-evidenced disruption / loss of productivity (a lump,
        /// not time-related). Added straight through.</summary>
        public double DisruptionUGX { get; set; }
        /// <summary>Financing / interest charges on withheld cash, evidenced lump.</summary>
        public double FinanceChargesUGX { get; set; }
    }

    public class LossExpenseResult
    {
        public double Weeks { get; set; }
        public double ProlongationUGX { get; set; }
        public double HeadOfficeUGX { get; set; }
        public double DisruptionUGX { get; set; }
        public double FinanceChargesUGX { get; set; }
        public double TotalUGX { get; set; }
    }

    public static class LossAndExpenseEngine
    {
        public static LossExpenseResult Value(LossExpenseInput i)
        {
            i = i ?? new LossExpenseInput();
            int dpw = Math.Max(1, Math.Min(7, i.DaysPerWeek == 0 ? 7 : i.DaysPerWeek));
            double weeks = Math.Max(0, i.EotDays) / (double)dpw;

            double prolong = MoneyRound.Round(weeks * Math.Max(0, i.WeeklyPrelimsUGX));
            double ohp = MoneyRound.Round(prolong * Math.Max(0, i.HeadOfficeOhpPct) / 100.0);
            double disruption = MoneyRound.Round(Math.Max(0, i.DisruptionUGX));
            double finance = MoneyRound.Round(Math.Max(0, i.FinanceChargesUGX));

            return new LossExpenseResult
            {
                Weeks = Math.Round(weeks, 3),
                ProlongationUGX = prolong,
                HeadOfficeUGX = ohp,
                DisruptionUGX = disruption,
                FinanceChargesUGX = finance,
                TotalUGX = MoneyRound.Round(prolong + ohp + disruption + finance),
            };
        }
    }
}
