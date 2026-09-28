// ══════════════════════════════════════════════════════════════════════════
//  CircuitNumberTextTests.cs — a circuit number held as NUMBER reads "3", not "3.00".
//
//  ELC_CKT_NR is TEXT now, but a project bound before the change still holds a
//  NUMBER, whose project-unit display is "3.00". TAG7 printed that verbatim.
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CircuitNumberTextTests
    {
        [Theory]
        [InlineData(3.0, "3.00", "3")]
        [InlineData(12.0, "12.00", "12")]
        [InlineData(0.0, "0.00", "0")]
        [InlineData(3.0000000001, "3.00", "3")]
        [InlineData(3.0, "", "3")]
        [InlineData(3.0, null, "3")]
        public void Integral_number_has_no_decimals(double value, string display, string expected)
        {
            Assert.Equal(expected, CircuitNumberText.FromNumber(value, display));
        }

        [Fact]
        public void Non_integral_number_keeps_revit_display()
        {
            // Not silently rounded into a different circuit.
            Assert.Equal("3.50", CircuitNumberText.FromNumber(3.5, "3.50"));
        }

        [Fact]
        public void Non_integral_number_without_display_is_not_dropped()
        {
            Assert.Equal("3.5", CircuitNumberText.FromNumber(3.5, null));
        }

        [Fact]
        public void NaN_returns_display()
        {
            Assert.Equal("x", CircuitNumberText.FromNumber(double.NaN, "x"));
        }
    }
}
