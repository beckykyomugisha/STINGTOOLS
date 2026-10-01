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
    
        // DTW-105: a level code the project declares (spatial_codes.json) wins in the ISO
        // map too, sanitised to the ISO level field; undeclared levels keep the stack code.

        private static Dictionary<string, string> Declared(params (string Name, string Code)[] rows)
            => rows.ToDictionary(r => r.Name, r => r.Code);

        [Fact]
        public void A_declared_level_code_wins_over_the_stack()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("Level 1", 0), ("Level 2", 3000)), Declared(("Level 2", "05")));
            Assert.Equal("05", map["Level 2"]);
            Assert.Equal("00", map["Level 1"]);
        }

        [Fact]
        public void A_declared_sting_code_is_written_in_its_iso_form()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("Podium", 0), ("Deck", 3000)), Declared(("Podium", "GF"), ("Deck", "l-03")));
            Assert.Equal("00", map["Podium"]);
            Assert.Equal("03", map["Deck"]);
        }

        [Fact]
        public void A_blank_or_unsanitisable_declaration_leaves_the_stack_code()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("Level 1", 0), ("Level 2", 3000)), Declared(("Level 1", "  "), ("Level 2", "--")));
            Assert.Equal("00", map["Level 1"]);
            Assert.Equal("01", map["Level 2"]);
        }

        [Fact]
        public void Sheet_and_spool_tokens_take_the_declared_code_through_the_same_map()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("Level 1", 0), ("Level 2", 3000)), Declared(("Level 2", "M1")));
            Assert.Equal("M1", SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Level 2", map));
            Assert.Equal("M1", SheetNumberPolicy.SpoolLevelToken(SheetNumberPolicy.IsoPattern, "L02", "Level 2", map));
            Assert.Equal("M1", SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, "Level 2", true, map));
        }

        // ── DTW-116: coincident and non-storey levels ──────────────────────────

        [Fact]
        public void CoincidentLevelsShareACodeAndDoNotShiftTheStack()
        {
            // Per-building level sets: building A and B both have a level at 0 and 3600.
            // Numbering by list position made B's ground "01" and pushed every storey up.
            var map = IsoLevelCode.BuildMap(Storeys(
                ("A Level 1", 0), ("B Level 1", 0),
                ("A Level 2", 3600), ("B Level 2", 3600),
                ("A Level 3", 7200)));
            Assert.Equal("00", map["A Level 1"]);
            Assert.Equal("00", map["B Level 1"]);
            Assert.Equal("01", map["A Level 2"]);
            Assert.Equal("01", map["B Level 2"]);
            Assert.Equal("02", map["A Level 3"]);
        }

        [Fact]
        public void ADatumLevelWithinFiftyMillimetresIsTheSameStorey()
        {
            // "Level 1 SSL" sits 40 mm under FFL; it is the same storey.
            var map = IsoLevelCode.BuildMap(Storeys(
                ("Level 1 SSL", -40), ("Level 1", 0), ("Level 2", 3600), ("Level 2 SSL", 3560)));
            Assert.Equal("00", map["Level 1 SSL"]);
            Assert.Equal("00", map["Level 1"]);
            Assert.Equal("01", map["Level 2"]);
            Assert.Equal("01", map["Level 2 SSL"]);
        }

        [Fact]
        public void ACoincidentLevelTakesItsGroupsStatedCode()
        {
            var map = IsoLevelCode.BuildMap(Storeys(("GF", 0), ("Level 1 SSL", -20), ("First", 3600)));
            Assert.Equal("00", map["GF"]);
            Assert.Equal("00", map["Level 1 SSL"]);
            Assert.Equal("01", map["First"]);
        }

        [Fact]
        public void NonStoreyLevelsDoNotCountButTakeTheStoreyTheyAreIn()
        {
            // "T.O. Steel" has Building Story off: it is not a storey, so Level 2 stays 01,
            // and the steel level reads as the storey it sits in.
            var storeys = new List<StoreyDatum>
            {
                new StoreyDatum { Name = "Level 1", ElevationMm = 0, IsBuildingStorey = true },
                new StoreyDatum { Name = "T.O. Steel", ElevationMm = 3200, IsBuildingStorey = false },
                new StoreyDatum { Name = "Level 2", ElevationMm = 3600, IsBuildingStorey = true },
                new StoreyDatum { Name = "Level 3", ElevationMm = 7200, IsBuildingStorey = true },
            };
            var map = IsoLevelCode.BuildMap(storeys);
            Assert.Equal("00", map["Level 1"]);
            Assert.Equal("01", map["Level 2"]);
            Assert.Equal("02", map["Level 3"]);
            Assert.Equal("00", map["T.O. Steel"]);
        }

        [Fact]
        public void WithoutStoreyInformationEveryLevelCounts()
        {
            // Unknown (null) is not "not a storey": the old behaviour stands.
            var map = IsoLevelCode.BuildMap(Storeys(("Level 1", 0), ("T.O. Steel", 3200), ("Level 2", 3600)));
            Assert.Equal("01", map["T.O. Steel"]);
            Assert.Equal("02", map["Level 2"]);
        }
    }
}
