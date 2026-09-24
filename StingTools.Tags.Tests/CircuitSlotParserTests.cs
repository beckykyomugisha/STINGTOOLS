// ══════════════════════════════════════════════════════════════════════════
//  CircuitSlotParserTests.cs — the circuit number is text, not an integer.
//
//  PanelDoorDiagramCommand read RBS_ELEC_CIRCUIT_NUMBER with AsInteger(),
//  which is always 0 for a string parameter, so no circuit ever matched a slot
//  and the door card showed every way as SPARE. Multi-pole circuits ("1,3,5")
//  were never handled at all.
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CircuitSlotParserTests
    {
        [Theory]
        [InlineData("5", new[] { 5 })]
        [InlineData("1,3,5", new[] { 1, 3, 5 })]
        [InlineData("2-4-6", new[] { 2, 4, 6 })]
        [InlineData(" 12 , 14 ", new[] { 12, 14 })]
        [InlineData("7,7", new[] { 7 })]
        [InlineData("L1-10", new[] { 1, 10 })]
        public void Parses_every_slot_named(string circuitNumber, int[] expected)
        {
            Assert.Equal(expected, CircuitSlotParser.Parse(circuitNumber));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("SPARE")]
        [InlineData("0")]
        public void Yields_no_slots_for_empty_or_non_numeric(string circuitNumber)
        {
            Assert.Empty(CircuitSlotParser.Parse(circuitNumber));
        }

        // Prefixed / Phase naming: the digits are not slot numbers, so the
        // command must use StartSlot + poles instead of parsing the text.
        [Theory]
        [InlineData("5", true)]
        [InlineData("1,3,5", true)]
        [InlineData("2-4-6", true)]
        [InlineData("L2-1", false)]
        [InlineData("DB1-5", false)]
        [InlineData("A1", false)]
        [InlineData("", false)]
        public void Detects_plain_slot_numbering(string circuitNumber, bool plain)
        {
            Assert.Equal(plain, CircuitSlotParser.IsPlainNumbering(circuitNumber));
        }

        [Theory]
        [InlineData(7, 1, new[] { 7 })]
        [InlineData(1, 3, new[] { 1, 3, 5 })]
        [InlineData(2, 2, new[] { 2, 4 })]
        [InlineData(0, 3, new int[0])]
        public void Two_column_across_steps_by_two_per_pole(int start, int poles, int[] expected)
        {
            Assert.Equal(expected, CircuitSlotParser.FromStartSlot(start, poles, 2));
        }

        // ELEC-16 — a switchboard / single-column schedule is consecutive. The old
        // hard-coded step of 2 put a 3-pole breaker at 7 into 7, 9, 11.
        [Theory]
        [InlineData(7, 3, new[] { 7, 8, 9 })]
        [InlineData(1, 1, new[] { 1 })]
        [InlineData(4, 2, new[] { 4, 5 })]
        public void Single_column_steps_by_one(int start, int poles, int[] expected)
        {
            Assert.Equal(expected, CircuitSlotParser.FromStartSlot(start, poles, 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Nonpositive_step_is_treated_as_one(int step)
        {
            Assert.Equal(new[] { 3, 4 }, CircuitSlotParser.FromStartSlot(3, 2, step));
        }

        [Fact]
        public void Zero_poles_is_one_slot()
        {
            Assert.Equal(new[] { 5 }, CircuitSlotParser.FromStartSlot(5, 0, 2));
        }
    }
}
