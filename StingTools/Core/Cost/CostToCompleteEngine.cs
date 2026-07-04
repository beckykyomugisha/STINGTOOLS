// ══════════════════════════════════════════════════════════════════════════
//  CostToCompleteEngine.cs — line-level cost-to-complete (CTC). PM-3.
//
//  The audit (§5 capability 9) found cost-to-complete existed only as the
//  (previously broken) EVM aggregate. A PM needs it per line: which packages
//  still have cost to run, and what the line will finish at given progress so
//  far. This is the pure per-line arithmetic:
//
//    earnedCost   = budget × %complete            (budgeted cost of work done)
//    committed    = max(actualToDate, earnedCost) (never forecast below sunk cost)
//    CTC          = budget × (1 − %complete) × productivityFactor
//    forecastFinal= actualToDate + CTC
//
//  productivityFactor lets a line that is over/under-running carry that trend
//  into the remaining work (1.0 = on budget; >1 = running hot). When an actual
//  cost-to-date is known it derives the factor from CPI so the forecast respects
//  measured performance; otherwise the caller-supplied factor (default 1) is used.
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Cost
{
    public class CtcLineResult
    {
        public double BudgetUGX { get; set; }
        public double PercentComplete { get; set; }
        public double EarnedCostUGX { get; set; }
        public double ActualToDateUGX { get; set; }
        public double ProductivityFactor { get; set; } = 1.0;
        public double CostToCompleteUGX { get; set; }
        public double ForecastFinalUGX { get; set; }
        /// <summary>ForecastFinal − Budget. &gt;0 = projected overrun on this line.</summary>
        public double VarianceUGX { get; set; }
    }

    public static class CostToCompleteEngine
    {
        /// <summary>
        /// Line CTC. <paramref name="actualToDateUGX"/> &lt; 0 means "no actual
        /// known" — then <paramref name="productivityFactor"/> (default 1) governs
        /// the remaining work and the forecast equals budget at factor 1. When an
        /// actual IS known and earned cost &gt; 0, the productivity factor is
        /// derived from it (actual ÷ earned) unless an explicit factor &gt; 0 is
        /// passed, so a hot line forecasts hot.
        /// </summary>
        public static CtcLineResult ForLine(
            double budgetUGX, double percentComplete,
            double actualToDateUGX = -1, double productivityFactor = 0)
        {
            double pct = Math.Max(0, Math.Min(100, percentComplete));
            double budget = Math.Max(0, budgetUGX);
            double earned = MoneyRound.Round(budget * pct / 100.0);

            bool haveActual = actualToDateUGX >= 0;
            double actual = haveActual ? actualToDateUGX : 0;

            double factor;
            if (productivityFactor > 0) factor = productivityFactor;
            else if (haveActual && earned > 0) factor = actual / earned;     // CPI-implied
            else factor = 1.0;

            double remainingBudget = budget * (1.0 - pct / 100.0);
            double ctc = MoneyRound.Round(remainingBudget * factor);
            double basis = haveActual ? actual : earned;
            double forecast = MoneyRound.Round(basis + ctc);

            return new CtcLineResult
            {
                BudgetUGX = MoneyRound.Round(budget),
                PercentComplete = Math.Round(pct, 2),
                EarnedCostUGX = earned,
                ActualToDateUGX = haveActual ? MoneyRound.Round(actual) : 0,
                ProductivityFactor = Math.Round(factor, 4),
                CostToCompleteUGX = ctc,
                ForecastFinalUGX = forecast,
                VarianceUGX = MoneyRound.Round(forecast - budget),
            };
        }
    }
}
