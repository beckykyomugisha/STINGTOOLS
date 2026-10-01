using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-30 — NEC 240.4 conductor protection in ProtectiveDeviceSelection.Select. Before
    /// this, the NEC path picked the next 240.6(A) rating and checked nothing against the
    /// conductor. Rules as tested (NEC 2023 240.4(B)/(C); wording checked 2026-10-02 against NFPA PI 705-NFPA 70-2023):
    /// at or below 800 A the next higher standard rating above the ampacity is permitted
    /// when the ampacity is not itself a standard rating (and the receptacle condition holds,
    /// which the code cannot see, so it is flagged); above 800 A ampacity ≥ rating.
    /// </summary>
    public class Nec2404ConductorTests
    {
        /// <summary>NEC 2023 Table 240.6(A), written from the standard.</summary>
        private static readonly int[] Nec =
        {
            10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200,
            225, 250, 300, 350, 400, 450, 500, 600, 700, 800, 1000, 1200, 1600, 2000, 2500,
            3000, 4000, 5000, 6000
        };

        [Fact]
        public void Above_800A_conductor_below_the_device_is_blocked_240_4C()
        {
            // Ib 950 A → 1000 A device; 960 A of conductor would be fine at ≤ 800 A, not here.
            var s = ProtectiveDeviceSelection.Select(950, isNec: true, continuous: false, Nec, izA: 960);
            Assert.Equal(1000, s.ProposedA);
            Assert.True(s.Blocked);
            Assert.Contains("240.4(C)", s.Note);
        }

        [Fact]
        public void Above_800A_conductor_at_the_device_rating_passes()
        {
            var s = ProtectiveDeviceSelection.Select(950, true, false, Nec, izA: 1000);
            Assert.False(s.Blocked);
            Assert.False(s.NeedsConfirmation);
            Assert.True(s.ConductorChecked);
        }

        [Fact]
        public void Next_size_up_at_or_below_800A_is_allowed_but_flagged_for_confirmation()
        {
            // 65 A conductor is not a standard rating; 70 A is the next one above it.
            var s = ProtectiveDeviceSelection.Select(62, true, false, Nec, izA: 65);
            Assert.Equal(70, s.ProposedA);
            Assert.False(s.Blocked);
            Assert.True(s.NeedsConfirmation);
            Assert.Contains("240.4(B)(1)", s.Note);
            Assert.Contains("receptacle", s.Note);
        }

        [Fact]
        public void Next_size_up_at_exactly_800A_is_still_240_4B()
        {
            // 760 A of conductor (not a standard rating) on an 800 A device.
            var s = ProtectiveDeviceSelection.Select(780, true, false, Nec, izA: 760);
            Assert.Equal(800, s.ProposedA);
            Assert.False(s.Blocked);
            Assert.True(s.NeedsConfirmation);
        }

        [Fact]
        public void Ampacity_that_is_a_standard_rating_gets_no_next_size_up()
        {
            var s = ProtectiveDeviceSelection.Select(62, true, false, Nec, izA: 60);
            Assert.True(s.Blocked);
            Assert.Contains("240.4(B)(2)", s.Note);
        }

        [Fact]
        public void More_than_one_rating_above_the_ampacity_is_blocked()
        {
            // 55 A conductor: the next rating above is 60 A, not 80 A.
            var s = ProtectiveDeviceSelection.Select(75, true, false, Nec, izA: 55);
            Assert.Equal(80, s.ProposedA);
            Assert.True(s.Blocked);
        }

        [Fact]
        public void Device_within_ampacity_passes_without_confirmation()
        {
            var s = ProtectiveDeviceSelection.Select(48, true, false, Nec, izA: 65);
            Assert.Equal(50, s.ProposedA);
            Assert.False(s.Blocked);
            Assert.False(s.NeedsConfirmation);
            Assert.True(s.ConductorChecked);
        }

        [Fact]
        public void No_ampacity_is_reported_not_checked_never_passed()
        {
            var s = ProtectiveDeviceSelection.Select(48, true, false, Nec, izA: null, izBasis: "no wire size on the circuit");
            Assert.False(s.ConductorChecked);
            Assert.Contains("conductor NOT checked", s.Note);
            Assert.Contains("no wire size", s.Note);
        }

        [Fact]
        public void BS7671_path_is_unchanged_by_the_NEC_rules()
        {
            // 1000 A MCCB-style list, Iz 960: BS 7671 blocks on In > Iz, not on 240.4(C) wording.
            var s = ProtectiveDeviceSelection.Select(950, false, false, new[] { 800, 1000 }, izA: 960);
            Assert.True(s.Blocked);
            Assert.DoesNotContain("240.4", s.Note);
        }

        [Theory]
        [InlineData("3-#12", "12")]
        [InlineData("1-#4/0, 1-#2, 1-#4", "4/0")]
        [InlineData("12 AWG", "12")]
        [InlineData("500 kcmil", "500")]
        [InlineData("3-250 MCM", "250")]
        [InlineData("#500", "500")]
        [InlineData("2 sets of 3-#500", null)]
        [InlineData("2.5mm²", null)]
        [InlineData("#100", null)]
        [InlineData("", null)]
        public void Nec_size_parser_reads_the_phase_conductor(string text, string expected)
            => Assert.Equal(expected, WireSizeParser.ParseNecSize(text));
    }
}
