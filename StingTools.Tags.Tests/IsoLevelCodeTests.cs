// The level segment of an issued identifier. A ground-floor plan went out with
// "01" — the storey ABOVE the one drawn — because the old rule read digits off
// the level's NAME, and "L1" tells you nothing about where the building sits.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class IsoLevelCodeTests
    {
        private static List<StoreyDatum> Storeys(params (string Name, double Mm)[] rows)
            => rows.Select(r => new StoreyDatum { Name = r.Name, ElevationMm = r.Mm }).ToList();

        [Fact]
        public void TheStoreyAtDatumIsGroundWhateverItIsCalled()
        {
            // The model that shipped the bug: one level called "L1", at zero.
            // ISO 19650 numbers the ground storey 00.
            var map = IsoLevelCode.BuildMap(Storeys(("L1", 0)));
            Assert.Equal("00", map["L1"]);
        }

        [Fact]
        public void StoreysCountUpFromGroundAndDownIntoBasements()
        {
            var map = IsoLevelCode.BuildMap(Storeys(
                ("Basement 2", -6000), ("Lower Ground", -3000), ("L1", 0),
                ("L2", 3000), ("L3", 6000)));

            Assert.Equal("B2", map["Basement 2"]);
            Assert.Equal("B1", map["Lower Ground"]);
            Assert.Equal("00", map["L1"]);
            Assert.Equal("01", map["L2"]);
            Assert.Equal("02", map["L3"]);
        }

        [Fact]
        public void AProjectThatNamesLevelOneAsFirstFloorStillGetsItRight()
        {
            // The opposite convention: ground is named GF and "Level 1" is genuinely
            // the storey above. Elevation answers it without anyone configuring
            // anything — which is the whole point of not reading the name.
            var map = IsoLevelCode.BuildMap(Storeys(
                ("GF", 0), ("Level 1", 3000), ("Level 2", 6000)));

            Assert.Equal("00", map["GF"]);
            Assert.Equal("01", map["Level 1"]);
            Assert.Equal("02", map["Level 2"]);
        }

        [Fact]
        public void ASurveyedDatumDoesNotMoveTheGroundFloor()
        {
            // A ground slab set 150 mm up for a screed is still the ground storey.
            var map = IsoLevelCode.BuildMap(Storeys(("Ground Slab", 150), ("First", 3150)));
            Assert.Equal("00", map["Ground Slab"]);
            Assert.Equal("01", map["First"]);
        }

        [Fact]
        public void ATieAtTheDatumPrefersTheLowerStorey()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("Slab", -150), ("FFL", 150), ("Upper", 3000)));
            Assert.Equal("00", map["Slab"]);
            Assert.Equal("01", map["FFL"]);
            Assert.Equal("02", map["Upper"]);
        }

        [Theory]
        [InlineData("Roof", "RF")]
        [InlineData("RF", "RF")]
        [InlineData("Ground Floor", "00")]
        [InlineData("GF", "00")]
        [InlineData("Basement", "B1")]
        [InlineData("Basement 2", "B2")]
        [InlineData("Mezzanine", "M1")]
        [InlineData("00", "00")]
        [InlineData("03", "03")]
        public void ANameThatStatesAnIsoCodeIsHonoured(string name, string expected)
            => Assert.Equal(expected, IsoLevelCode.FromName(name));

        [Theory]
        [InlineData("L1")]
        [InlineData("Level 1")]
        [InlineData("Level 2")]
        public void TheAmbiguousNamesStateNothing(string name)
        {
            // "Level 1" means the ground storey in most Revit templates and the
            // storey above it in UK practice. Answering here would put the guess
            // back; the stack answers it instead.
            Assert.Null(IsoLevelCode.FromName(name));
        }

        [Fact]
        public void ARoofKeepsItsCodeWhereverItSitsInTheStack()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("L1", 0), ("L2", 3000), ("Roof", 6000)));
            Assert.Equal("RF", map["Roof"]);
            Assert.Equal("01", map["L2"]);
        }

        [Fact]
        public void WithNoElevationsTheNameIsAllThereIs()
        {
            // The fallback, reached only when the model has nothing better. Still a
            // guess, and now clearly labelled as one.
            Assert.Equal("01", IsoLevelCode.FromNameOnly("L1"));
            Assert.Equal("02", IsoLevelCode.FromNameOnly("Level 2"));
            Assert.Equal("00", IsoLevelCode.FromNameOnly("Ground Floor"));
            Assert.Equal("XX", IsoLevelCode.FromNameOnly("Site"));
            Assert.Equal("XX", IsoLevelCode.FromNameOnly(null));
        }

        [Fact]
        public void EveryCodeEmittedIsAValidIsoLevelSegment()
        {
            // Enumerated rather than listed, so a rule added later is covered.
            var map = IsoLevelCode.BuildMap(Storeys(
                ("B2", -6000), ("Basement", -3000), ("L1", 0), ("Mezzanine", 1500),
                ("L2", 3000), ("L3", 6000), ("Roof", 9000)));

            foreach (var code in map.Values)
            {
                bool ok = code == "RF" || code == "ZZ" || code == "XX"
                    || (code.Length == 2 && code.All(char.IsDigit))
                    || (code.Length >= 2 && (code[0] == 'B' || code[0] == 'M')
                        && code.Skip(1).All(char.IsDigit));
                Assert.True(ok, $"'{code}' is not an ISO 19650 level code");
            }
        }

        [Fact]
        public void AnEmptyModelProducesAnEmptyMapRatherThanAGuess()
        {
            Assert.Empty(IsoLevelCode.BuildMap(null));
            Assert.Empty(IsoLevelCode.BuildMap(new List<StoreyDatum>()));
        }
    }
}
