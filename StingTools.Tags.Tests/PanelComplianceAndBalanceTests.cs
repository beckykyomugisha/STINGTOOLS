// ══════════════════════════════════════════════════════════════════════════
//  PanelComplianceAndBalanceTests.cs — ROADMAP PNL-2 and PNL-5.
//
//  CircuitComplianceRule: Ib ≤ In ≤ Iz, VD ≤ limit, PSC ≤ Icn — and a rule
//  whose inputs are missing is NOT CHECKED, never a silent pass.
//  PhaseBalancer: moves single-pole, unlocked circuits into free slots on the
//  lighter phase; hand-worked loads below.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PanelComplianceAndBalanceTests
    {
        private static CircuitCheckInput Good() => new CircuitCheckInput
        {
            IbA = 14, InA = 16, IzA = 27, VdPct = 2.1, VdLimitPct = 5,
            ProspectiveFaultKa = 4.2, BreakingCapacityKa = 6,
        };

        [Fact]
        public void Tabulated_Iz_pass_is_never_a_plain_OK()
        {
            // Iz with no Ca/Cg/Ci is an upper bound: In <= It proves nothing about the
            // derated cable, so the verdict must say derating was not checked.
            var c = Good(); c.IzIsUpperBound = true;
            var r = CircuitComplianceRule.Evaluate(c);
            Assert.False(r.Failed);
            Assert.False(r.FullyVerified);
            Assert.Contains("Iz derating", r.Summary);
            Assert.StartsWith("OK (not checked:", r.Summary);
        }

        [Fact]
        public void Tabulated_Iz_fail_is_still_conclusive()
        {
            // In > It fails whatever the derating, so the upper bound still fails it.
            var c = Good(); c.IzIsUpperBound = true; c.InA = 32;
            var r = CircuitComplianceRule.Evaluate(c);
            Assert.True(r.Failed);
            Assert.Contains("In 32 A > Iz 27 A", r.Summary);
            Assert.DoesNotContain("Iz derating", r.Summary);
        }

        [Theory]
        [InlineData(10, true, 10)]        // "10 kA"
        [InlineData(10, false, 10)]       // "10" — no LV device above 200 kA, so kA
        [InlineData(10000, false, 10)]    // "10000" / "10000 A" — amps
        [InlineData(6000, false, 6)]
        [InlineData(250, true, 250)]      // explicit kA is trusted
        public void Breaking_capacity_amps_are_not_read_as_kA(double value, bool kaUnit, double expected)
            => Assert.Equal(expected, CircuitComplianceRule.BreakingCapacityKa(value, kaUnit), 6);

        [Fact]
        public void Amps_read_as_kA_would_hide_a_breaking_capacity_failure()
        {
            // 10 kA fault on a 6000 A (6 kA) device: must FAIL once units are right.
            var c = Good(); c.ProspectiveFaultKa = 10;
            c.BreakingCapacityKa = CircuitComplianceRule.BreakingCapacityKa(6000, false);
            Assert.True(CircuitComplianceRule.Evaluate(c).Failed);
        }

        [Fact]
        public void All_rules_pass_is_plain_OK()
        {
            var r = CircuitComplianceRule.Evaluate(Good());
            Assert.True(r.FullyVerified);
            Assert.Equal("OK", r.Summary);
        }

        [Fact]
        public void Each_rule_fails_on_its_own_condition()
        {
            var c = Good(); c.IbA = 18;                       // 18 A on a 16 A device
            Assert.Contains("Ib 18 A > In 16 A", CircuitComplianceRule.Evaluate(c).Summary);
            c = Good(); c.InA = 32;                           // 32 A device on 2.5 mm² (Iz 27 A)
            Assert.Contains("In 32 A > Iz 27 A", CircuitComplianceRule.Evaluate(c).Summary);
            c = Good(); c.VdPct = 6.1;                        // 6.1 % against 5 %
            Assert.Contains("VD 6.10 % > 5 %", CircuitComplianceRule.Evaluate(c).Summary);
            c = Good(); c.VdPct = 3.04; c.VdLimitPct = 3;     // just over: must not display as "3.0 > 3"
            Assert.Contains("VD 3.04 % > 3 %", CircuitComplianceRule.Evaluate(c).Summary);
            c = Good(); c.ProspectiveFaultKa = 10;            // 10 kA on a 6 kA MCB
            Assert.Contains("PSC 10 kA > Icn 6 kA", CircuitComplianceRule.Evaluate(c).Summary);
        }

        [Fact]
        public void Missing_inputs_are_not_checked_and_never_plain_OK()
        {
            var r = CircuitComplianceRule.Evaluate(new CircuitCheckInput { IbA = 10, InA = 16, VdLimitPct = 5 });
            Assert.False(r.Failed);
            Assert.False(r.FullyVerified);
            Assert.StartsWith("OK (not checked:", r.Summary);
            Assert.Contains("Iz", r.Summary);
            Assert.Contains("VD", r.Summary);
            Assert.Contains("breaking capacity", r.Summary);
        }

        [Fact]
        public void A_failure_still_lists_what_was_not_checked()
        {
            var r = CircuitComplianceRule.Evaluate(new CircuitCheckInput { IbA = 20, InA = 16, VdLimitPct = 5 });
            Assert.StartsWith("FAIL: Ib 20 A > In 16 A", r.Summary);
            Assert.Contains("| not checked:", r.Summary);
        }

        // Board: L1 3000 VA, L2 1000 VA, L3 2000 VA → mean 2000, imbalance 50 %.
        // Moving the 1000 VA L1 circuit to the free L2 slot gives 2000/2000/2000.
        [Fact]
        public void Moves_the_right_circuit_to_the_light_phase()
        {
            var plan = PhaseBalancer.Plan(new[] { 3000.0, 1000, 2000 },
                new[]
                {
                    new BalanceCircuit { Id = 1, Label = "A", Phase = 0, LoadVa = 1000, Slot = 1 },
                    new BalanceCircuit { Id = 2, Label = "B", Phase = 0, LoadVa = 500,  Slot = 4 },
                },
                new[] { new BalanceFreeSlot { Slot = 2, Phase = 1 }, new BalanceFreeSlot { Slot = 3, Phase = 2 } });

            Assert.Equal(50.0, plan.BeforeImbalancePct, 3);
            var m = Assert.Single(plan.Moves);
            Assert.Equal(1, m.CircuitId);
            Assert.Equal(1, m.FromSlot);
            Assert.Equal(2, m.ToSlot);
            Assert.Equal(new[] { 2000.0, 2000, 2000 }, plan.After);
            Assert.Equal(0.0, plan.AfterImbalancePct, 3);
        }

        [Fact]
        public void Locked_circuits_and_locked_slots_never_move()
        {
            var plan = PhaseBalancer.Plan(new[] { 3000.0, 1000, 2000 },
                new[] { new BalanceCircuit { Id = 1, Phase = 0, LoadVa = 1000, Slot = 1, Locked = true } },
                new[] { new BalanceFreeSlot { Slot = 2, Phase = 1 } });
            Assert.Empty(plan.Moves);

            plan = PhaseBalancer.Plan(new[] { 3000.0, 1000, 2000 },
                new[] { new BalanceCircuit { Id = 1, Phase = 0, LoadVa = 1000, Slot = 1 } },
                new[] { new BalanceFreeSlot { Slot = 2, Phase = 1, Locked = true } });
            Assert.Empty(plan.Moves);
        }

        [Fact]
        public void No_move_when_it_would_not_help_by_the_minimum()
        {
            // 30 VA circuit: moving it changes the spread by 30 VA < MinGainVa (50 VA).
            var plan = PhaseBalancer.Plan(new[] { 1030.0, 1000, 1000 },
                new[] { new BalanceCircuit { Id = 1, Phase = 0, LoadVa = 30, Slot = 1 } },
                new[] { new BalanceFreeSlot { Slot = 2, Phase = 1 } });
            Assert.Empty(plan.Moves);
        }

        [Fact]
        public void Each_circuit_moves_at_most_once_and_the_plan_is_deterministic()
        {
            var circuits = Enumerable.Range(1, 6).Select(i =>
                new BalanceCircuit { Id = i, Phase = 0, LoadVa = 400, Slot = i * 3 - 2 }).ToList();
            var free = new List<BalanceFreeSlot>
            {
                new BalanceFreeSlot { Slot = 2, Phase = 1 }, new BalanceFreeSlot { Slot = 3, Phase = 2 },
                new BalanceFreeSlot { Slot = 5, Phase = 1 }, new BalanceFreeSlot { Slot = 6, Phase = 2 },
            };
            var a = PhaseBalancer.Plan(new[] { 2400.0, 0, 0 }, circuits.Select(Clone), free);
            var b = PhaseBalancer.Plan(new[] { 2400.0, 0, 0 }, circuits.Select(Clone), free);
            Assert.Equal(a.Moves.Select(m => (m.CircuitId, m.ToSlot)), b.Moves.Select(m => (m.CircuitId, m.ToSlot)));
            Assert.Equal(a.Moves.Count, a.Moves.Select(m => m.CircuitId).Distinct().Count());
            Assert.Equal(new[] { 800.0, 800, 800 }, a.After);   // 2400 split evenly
        }

        private static BalanceCircuit Clone(BalanceCircuit c) => new BalanceCircuit
            { Id = c.Id, Label = c.Label, Phase = c.Phase, LoadVa = c.LoadVa, Slot = c.Slot, Locked = c.Locked };

        [Fact]
        public void Row_phase_calibration_requires_agreement()
        {
            Assert.Equal(0, PhaseBalancer.CalibrateRowOffset(new[] { (0, 0), (1, 1), (4, 1), (5, 2) }));
            Assert.Equal(2, PhaseBalancer.CalibrateRowOffset(new[] { (1, 0), (2, 1) }));   // first row = L2
            Assert.Null(PhaseBalancer.CalibrateRowOffset(new[] { (0, 0), (1, 0) }));      // not row-cyclic
            Assert.Null(PhaseBalancer.CalibrateRowOffset(new (int, int)[0]));             // nothing known
            Assert.Equal(1, PhaseBalancer.PhaseOfRow(4, 0));
        }
    }
}
