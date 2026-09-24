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
    }
}
