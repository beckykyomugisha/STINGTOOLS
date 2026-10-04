using System.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// NEC 2023 added 10 A to Table 240.6(A) and, with it, 10 A branch circuits whose loads
    /// 210.23(A) restricts (lighting outlets yes; receptacle outlets, fixed appliances,
    /// garage door openers and laundry equipment no). Once the shipped NEC list carries
    /// 10 A, a sizer that cannot see the load must not propose it silently, and one that can
    /// (the circuit wizard) must not offer it to a receptacle circuit.
    /// Sources: Table 240.6(A) and 210.23(A) as reproduced by NFPA (CMP-10 FR-9210 and
    /// CMP-2 FR-7637 First Draft reports for the 2026 cycle, base text = 2023 edition).
    /// </summary>
    public class NecTenAmpBranchCircuitTests
    {
        private static readonly int[] Nec =
        {
            10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200,
            225, 250, 300, 350, 400, 450, 500, 600, 700, 800, 1000, 1200, 1600, 2000, 2500,
            3000, 4000, 5000, 6000
        };

        [Fact]
        public void A_small_load_on_the_full_list_gets_10A_and_is_flagged_for_210_23A()
        {
            var s = ProtectiveDeviceSelection.Select(6.0, isNec: true, continuous: false, Nec, izA: 15, izBasis: "14 AWG Cu");
            Assert.Equal(10, s.ProposedA);
            Assert.False(s.NeedsConfirmation);
            Assert.True(ProtectiveDeviceSelection.FlagNecTenAmpBranchCircuit(s));
            Assert.True(s.NeedsConfirmation);
            Assert.False(s.Blocked);
            Assert.Contains("210.23(A)", s.Note);
            Assert.Contains("receptacle", s.Note);
        }

        [Fact]
        public void A_15A_proposal_is_not_flagged()
        {
            var s = ProtectiveDeviceSelection.Select(12.0, true, false, Nec, izA: 20);
            Assert.Equal(15, s.ProposedA);
            Assert.False(ProtectiveDeviceSelection.FlagNecTenAmpBranchCircuit(s));
            Assert.False(s.NeedsConfirmation);
            Assert.DoesNotContain("210.23", s.Note);
        }

        [Fact]
        public void A_blocked_proposal_is_not_flagged()
        {
            var s = new ProtectiveDeviceSelection.Selection { ProposedA = 10, Blocked = true, Note = "x" };
            Assert.False(ProtectiveDeviceSelection.FlagNecTenAmpBranchCircuit(s));
            Assert.Equal("x", s.Note);
        }

        [Fact]
        public void A_receptacle_branch_circuit_list_starts_at_15A()
        {
            var list = ProtectiveDeviceSelection.NecRatingsAboveTenAmpBranch(Nec);
            Assert.Equal(15, list.First());
            Assert.Equal(Nec.Length - 1, list.Length);
            var s = ProtectiveDeviceSelection.Select(6.0, true, false, list, izA: 15);
            Assert.Equal(15, s.ProposedA);
        }

        [Fact]
        public void Null_list_gives_empty_list_not_a_throw()
            => Assert.Empty(ProtectiveDeviceSelection.NecRatingsAboveTenAmpBranch(null));

        [Fact]
        public void The_240_4B_confirmation_text_carries_no_VERIFY_tag_and_quotes_B1()
        {
            Assert.DoesNotContain("VERIFY", ProtectiveDeviceSelection.Nec2404BConfirmText);
            Assert.Contains("not part of a branch circuit supplying more than one receptacle for " +
                            "cord-and-plug-connected portable loads", ProtectiveDeviceSelection.Nec2404BConfirmText);
            Assert.Contains("240.6(C)", ProtectiveDeviceSelection.Nec2404BConfirmText);
        }
    }
}
