using System.Collections.Generic;
using StingTools.Core.Cost;
using Xunit;

namespace StingTools.Cost.Tests
{
    // PM-3 — CVR / Loss-&-Expense / line-level CTC / commitments register.
    public class CvrEngineTests
    {
        [Fact]
        public void Margin_and_underclaim_worked_example()
        {
            // Value 1,200m; cost 1,000m; certified 1,100m; provisions 50m.
            var r = CvrEngine.Compute(new CvrInput
            {
                ValueOfWorkDoneUGX = 1_200_000_000,
                CostToDateUGX = 1_000_000_000,
                AmountCertifiedUGX = 1_100_000_000,
                ProvisionsUGX = 50_000_000,
            });
            Assert.Equal(150_000_000, r.GrossMarginUGX);       // 1200 - 1000 - 50
            Assert.Equal(12.5, r.MarginPct);                   // 150/1200
            Assert.Equal(100_000_000, r.WipUGX);               // 1200 - 1100
            Assert.Equal("UnderClaimed", r.ClaimPosition);
            Assert.False(r.HasForecast);
        }

        [Fact]
        public void Overclaim_detected_and_forecast_margin()
        {
            var r = CvrEngine.Compute(new CvrInput
            {
                ValueOfWorkDoneUGX = 500,
                CostToDateUGX = 400,
                AmountCertifiedUGX = 600,           // billed ahead of progress
                ForecastFinalCostUGX = 900,
                ForecastFinalValueUGX = 1000,
            });
            Assert.Equal(-100, r.WipUGX);
            Assert.Equal("OverClaimed", r.ClaimPosition);
            Assert.True(r.HasForecast);
            Assert.Equal(500, r.CostToCompleteUGX);   // 900 - 400
            Assert.Equal(100, r.ForecastMarginUGX);   // 1000 - 900
            Assert.Equal(10.0, r.ForecastMarginPct);
        }
    }

    public class LossAndExpenseEngineTests
    {
        [Fact]
        public void Prolongation_off_eot_days_calendar_week()
        {
            // 28 EOT days ÷ 7 = 4 weeks × 10m/week = 40m prolongation; +15% OHP.
            var r = LossAndExpenseEngine.Value(new LossExpenseInput
            {
                EotDays = 28,
                WeeklyPrelimsUGX = 10_000_000,
                HeadOfficeOhpPct = 15,
                DisruptionUGX = 5_000_000,
            });
            Assert.Equal(4.0, r.Weeks);
            Assert.Equal(40_000_000, r.ProlongationUGX);
            Assert.Equal(6_000_000, r.HeadOfficeUGX);     // 15% of 40m
            Assert.Equal(5_000_000, r.DisruptionUGX);
            Assert.Equal(51_000_000, r.TotalUGX);
        }

        [Fact]
        public void Six_day_week_costs_more_weeks()
        {
            var r = LossAndExpenseEngine.Value(new LossExpenseInput
            { EotDays = 12, WeeklyPrelimsUGX = 1_000_000, DaysPerWeek = 6 });
            Assert.Equal(2.0, r.Weeks);                   // 12 / 6
            Assert.Equal(2_000_000, r.ProlongationUGX);
        }

        [Fact]
        public void Zero_eot_is_zero()
        {
            var r = LossAndExpenseEngine.Value(new LossExpenseInput { EotDays = 0, WeeklyPrelimsUGX = 5_000_000 });
            Assert.Equal(0, r.TotalUGX);
        }
    }

    public class CostToCompleteEngineTests
    {
        [Fact]
        public void No_actual_uses_factor_one()
        {
            var r = CostToCompleteEngine.ForLine(budgetUGX: 1000, percentComplete: 40);
            Assert.Equal(400, r.EarnedCostUGX);
            Assert.Equal(600, r.CostToCompleteUGX);   // 1000 × 0.6 × 1
            Assert.Equal(1000, r.ForecastFinalUGX);   // earned 400 + 600
            Assert.Equal(0, r.VarianceUGX);
        }

        [Fact]
        public void Hot_line_forecasts_overrun_from_actual()
        {
            // 50% done but already spent 600 (earned would be 500) → CPI-implied
            // factor 1.2; remaining 500 × 1.2 = 600; forecast 600 + 600 = 1200.
            var r = CostToCompleteEngine.ForLine(1000, 50, actualToDateUGX: 600);
            Assert.Equal(1.2, r.ProductivityFactor);
            Assert.Equal(600, r.CostToCompleteUGX);
            Assert.Equal(1200, r.ForecastFinalUGX);
            Assert.Equal(200, r.VarianceUGX);         // 200 overrun
        }

        [Fact]
        public void Complete_line_has_zero_ctc()
        {
            var r = CostToCompleteEngine.ForLine(1000, 100, actualToDateUGX: 950);
            Assert.Equal(0, r.CostToCompleteUGX);
            Assert.Equal(950, r.ForecastFinalUGX);
        }
    }

    public class CommitmentsRegisterTests
    {
        [Fact]
        public void Rollup_uncommitted_and_overcommit()
        {
            var commitments = new List<Commitment>
            {
                new Commitment { Id = "PO-1", BudgetLineRef = "5.1", CommittedUGX = 300, CertifiedUGX = 100 },
                new Commitment { Id = "SC-1", BudgetLineRef = "5.1", CommittedUGX = 300, CertifiedUGX = 50 },
                new Commitment { Id = "PO-2", BudgetLineRef = "6.2", CommittedUGX = 200, CertifiedUGX = 200 },
                new Commitment { Id = "PO-3", BudgetLineRef = "6.2", CommittedUGX = 100, CertifiedUGX = 0, Status = "Cancelled" },
            };
            var budget = new Dictionary<string, double> { ["5.1"] = 500, ["6.2"] = 250 };

            var s = CommitmentsRegister.Rollup(commitments, budget);

            var l51 = s.ByBudgetLine.Find(b => b.BudgetLineRef == "5.1");
            Assert.Equal(600, l51.CommittedUGX);      // 300 + 300
            Assert.Equal(450, l51.OutstandingUGX);    // (300-100)+(300-50)
            Assert.Equal(-100, l51.UncommittedUGX);   // 500 - 600
            Assert.True(l51.OverCommitted);

            var l62 = s.ByBudgetLine.Find(b => b.BudgetLineRef == "6.2");
            Assert.Equal(200, l62.CommittedUGX);      // cancelled PO-3 excluded
            Assert.Equal(50, l62.UncommittedUGX);     // 250 - 200
            Assert.False(l62.OverCommitted);

            Assert.Equal(750, s.TotalBudgetUGX);
            Assert.Equal(800, s.TotalCommittedUGX);
            Assert.Single(s.OverCommittedLines);
            Assert.Contains("5.1", s.OverCommittedLines);
        }
    }
}
