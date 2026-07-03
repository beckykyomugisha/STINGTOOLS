using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Delivery;
using Xunit;

namespace StingTools.Cost.Tests
{
    public class RiskRegisterTests
    {
        [Theory]
        [InlineData(1, 1, "Green")]
        [InlineData(2, 2, "Green")]   // 4
        [InlineData(5, 1, "Amber")]   // 5
        [InlineData(3, 3, "Amber")]   // 9
        [InlineData(2, 5, "Red")]     // 10
        [InlineData(5, 5, "Red")]     // 25
        public void Banding_is_5x5(int l, int i, string band)
        {
            var r = new RiskItem { Likelihood = l, Impact = i };
            Assert.Equal(l * i, r.InherentScore);
            Assert.Equal(band, r.InherentBand);
        }

        [Fact]
        public void Residual_falls_back_to_inherent_then_overrides()
        {
            var r = new RiskItem { Likelihood = 5, Impact = 5 };       // 25 Red
            Assert.Equal(25, r.ResidualScore);                        // no residual yet
            r.ResidualLikelihood = 2; r.ResidualImpact = 2;           // mitigated to 4 Green
            Assert.Equal(4, r.ResidualScore);
            Assert.Equal("Green", r.ResidualBand);
        }

        [Fact]
        public void Summary_counts_and_top_risks()
        {
            var risks = new List<RiskItem>
            {
                new RiskItem { Id="R1", Likelihood=5, Impact=5, Status=RiskStatus.Open },                 // 25 Red
                new RiskItem { Id="R2", Likelihood=3, Impact=3, Status=RiskStatus.Mitigating,
                               ResidualLikelihood=1, ResidualImpact=2 },                                  // residual 2 Green
                new RiskItem { Id="R3", Likelihood=2, Impact=5, Status=RiskStatus.Open },                 // 10 Red
                new RiskItem { Id="R4", Likelihood=5, Impact=5, Status=RiskStatus.Closed },               // closed
            };
            var s = RiskRegister.Summarise(risks, topN: 2);
            Assert.Equal(4, s.Total);
            Assert.Equal(3, s.OpenCount);                 // R4 closed
            Assert.Equal(3, s.RedCount);                  // band totals over ALL: R1, R3, R4 (closed still Red band)
            Assert.Equal(2, s.RedResidualCount);          // open reds only: R1, R3
            Assert.Equal(2, s.TopRisks.Count);
            Assert.Equal("R1", s.TopRisks[0].Id);         // highest residual first
            Assert.Equal("R3", s.TopRisks[1].Id);
        }
    }

    public class MidpEngineTests
    {
        private static readonly DateTime Now = new DateTime(2026, 6, 30);

        [Fact]
        public void Overdue_when_planned_passed_and_not_issued()
        {
            var d = new DeliverablePlanItem { Code = "D1", PlannedDate = Now.AddDays(-10), RequiredSuitability = "S4" };
            var drift = MidpEngine.Classify(d, Now);
            Assert.Equal(DeliveryDriftState.Overdue, drift.State);
            Assert.Equal(10, drift.DaysLateOrToGo);
        }

        [Fact]
        public void AtRisk_within_window_and_NotDue_beyond()
        {
            var atRisk = MidpEngine.Classify(new DeliverablePlanItem { Code = "D2", PlannedDate = Now.AddDays(7) }, Now, 14);
            Assert.Equal(DeliveryDriftState.AtRisk, atRisk.State);
            var notDue = MidpEngine.Classify(new DeliverablePlanItem { Code = "D3", PlannedDate = Now.AddDays(30) }, Now, 14);
            Assert.Equal(DeliveryDriftState.NotDue, notDue.State);
        }

        [Fact]
        public void OnTrack_when_issued_at_required_suitability()
        {
            var d = new DeliverablePlanItem
            {
                Code = "D4", PlannedDate = Now.AddDays(-5), RequiredSuitability = "S4",
                Issued = true, ActualDate = Now.AddDays(-6), ActualSuitability = "S4",
            };
            Assert.Equal(DeliveryDriftState.OnTrack, MidpEngine.Classify(d, Now).State);
        }

        [Fact]
        public void SuitShort_when_issued_below_required()
        {
            var d = new DeliverablePlanItem
            {
                Code = "D5", PlannedDate = Now.AddDays(-5), RequiredSuitability = "S4",
                Issued = true, ActualDate = Now.AddDays(-6), ActualSuitability = "S2",
            };
            Assert.Equal(DeliveryDriftState.SuitShort, MidpEngine.Classify(d, Now).State);
        }

        [Fact]
        public void Detect_rolls_up_on_programme_pct()
        {
            var plan = new List<DeliverablePlanItem>
            {
                new DeliverablePlanItem { Code = "A", PlannedDate = Now.AddDays(-3), Issued = true, ActualDate = Now.AddDays(-3), RequiredSuitability="S2", ActualSuitability="S2" }, // OnTrack
                new DeliverablePlanItem { Code = "B", PlannedDate = Now.AddDays(40) }, // NotDue
                new DeliverablePlanItem { Code = "C", PlannedDate = Now.AddDays(-1) }, // Overdue
                new DeliverablePlanItem { Code = "D", PlannedDate = Now.AddDays(5) },  // AtRisk
            };
            var s = MidpEngine.Detect(plan, Now, 14);
            Assert.Equal(4, s.Total);
            Assert.Equal(1, s.OnTrack);
            Assert.Equal(1, s.NotDue);
            Assert.Equal(1, s.Overdue);
            Assert.Equal(1, s.AtRisk);
            Assert.Equal(50.0, s.OnProgrammePct);     // (OnTrack + NotDue)/4
            Assert.Equal(DeliveryDriftState.Overdue, s.Drifts[0].State);  // worst first
        }
    }
}
