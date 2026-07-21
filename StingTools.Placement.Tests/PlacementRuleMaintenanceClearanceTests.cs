using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Placement.Tests
{
    /// <summary>
    /// MaintenanceClearance was retyped from double to a class-code string.
    /// The setter coerces a purely-zero numeric to empty so that an Excel sheet
    /// exported under the old double shape (default rendered as "0") does not
    /// resurrect a bogus clearance on re-import — while a genuine numeric typo
    /// such as "600" is kept so the advisory validator can still flag it.
    /// </summary>
    public class PlacementRuleMaintenanceClearanceTests
    {
        [Theory]
        [InlineData("0")]
        [InlineData("0.0")]
        [InlineData("0.000")]
        [InlineData(" 0 ")]
        public void LegacyZeroSerialisation_IsCoercedToEmpty(string raw)
        {
            var r = new PlacementRule { MaintenanceClearance = raw };
            Assert.Equal("", r.MaintenanceClearance);
        }

        [Theory]
        [InlineData("FRONT_600")]
        [InlineData("SIDES_300")]
        public void ClassCode_IsPreserved(string code)
        {
            var r = new PlacementRule { MaintenanceClearance = code };
            Assert.Equal(code, r.MaintenanceClearance);
        }

        [Fact]
        public void NonZeroNumericTypo_IsKeptSoTheAdvisoryCanFlagIt()
        {
            // "600" is the exact mistake the old double-typed field invited and
            // that PlacementAdvisoryValidator reports — coercion must not swallow it.
            var r = new PlacementRule { MaintenanceClearance = "600" };
            Assert.Equal("600", r.MaintenanceClearance);
            Assert.Contains(
                PlacementAdvisoryValidator.Validate(new[] { r }, null),
                a => a.Contains("MaintenanceClearance"));
        }

        [Fact]
        public void Null_BecomesEmpty()
        {
            var r = new PlacementRule { MaintenanceClearance = null };
            Assert.Equal("", r.MaintenanceClearance);
        }
    }
}
