// Decompose is what makes the seven PRJ_SHEET_* segments derived rather than a
// second opinion. Its failure mode is the dangerous kind: a half-parsed
// identifier stamped onto a sheet leaves every field looking populated and
// several of them wrong, on a drawing that goes out.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Iso19650DecomposeTests
    {
        [Fact]
        public void AnAssembledIdentifierSplitsIntoItsSevenFields()
        {
            var s = Iso19650DocumentCode.Decompose("SAH-PLNS-ZZ-01-DR-A-0001");
            Assert.NotNull(s);
            Assert.Equal("SAH", s.Project);
            Assert.Equal("PLNS", s.Originator);
            Assert.Equal("ZZ", s.Volume);
            Assert.Equal("01", s.Level);
            Assert.Equal("DR", s.Type);
            Assert.Equal("A", s.Role);
            Assert.Equal("0001", s.Number);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("A-001")]                          // a short sheet number
        [InlineData("SAH-PLNS-ZZ-01-DR-A")]            // six fields
        [InlineData("SAH-PLNS-ZZ-01-DR-A-0001-P01")]   // eight
        [InlineData("SAH-PLNS-ZZ-01-DR-A-1")]          // number is not four digits
        [InlineData("SAH-PLNS-ZZ-01-DR-A-ABCD")]       // number is not digits
        public void AnythingThatIsNotAnIdentifierReturnsNull(string raw)
        {
            // Null is the whole safety property: StampSegments writes nothing rather
            // than stamping a partial split across seven parameters.
            Assert.Null(Iso19650DocumentCode.Decompose(raw));
        }

        [Fact]
        public void DecomposeAndAssembleAgree()
        {
            // The two halves have to be reciprocal or the derived segments describe
            // an identifier that never existed. Round-tripping catches a field-order
            // change in either one — the old inline assembler had exactly that bug,
            // dropping Volume and shifting everything after it.
            string built = Iso19650DocumentCode.Assemble(
                "SAH", "PLNS", "ZZ", "L01", "DR", "ARCH", "A-001");
            var s = Iso19650DocumentCode.Decompose(built);

            Assert.NotNull(s);
            Assert.Equal("SAH", s.Project);
            Assert.Equal("PLNS", s.Originator);
            Assert.Equal("ZZ", s.Volume);
            Assert.Equal("01", s.Level);      // L01 normalises to 01
            Assert.Equal("DR", s.Type);
            Assert.Equal("A", s.Role);        // ARCH folds to role A
            Assert.Equal("0001", s.Number);

            Assert.Equal(built, string.Join("-", s.Project, s.Originator, s.Volume,
                s.Level, s.Type, s.Role, s.Number));
        }

        [Fact]
        public void TheRoleThatShippedTheBugRoundTrips()
        {
            // The drawing that started this: PRJ_SHEET_ROLE_TXT read "A" while the
            // identifier on the same sheet read role "Z". Derived from one string,
            // that pair cannot diverge.
            var s = Iso19650DocumentCode.Decompose("SAH-PLNS-ZZ-01-LG-Z-0003");
            Assert.Equal("Z", s.Role);
            Assert.Equal("LG", s.Type);
            Assert.Equal("0003", s.Number);
        }
    }
}
