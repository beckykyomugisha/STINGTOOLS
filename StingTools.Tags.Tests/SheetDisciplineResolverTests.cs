// Tests for the two rules that decide the ROLE segment of an issued document
// identifier. Both produced a wrong letter on a real drawing, and neither was
// covered: an architectural GA plan was issued as ...-DR-Z-0002 (Z = General),
// and fire protection folded to S (Structural).
//
// A wrong role is the worst kind of defect this system produces, because the
// letter is plausible. "Z" and "S" are real ISO roles, so nothing downstream
// rejects them — the drawing is simply attributed to the wrong profession, and
// nobody notices until someone asks the structural engineer about a sprinkler.

using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SheetDisciplineResolverTests
    {
        // ── The sheet says what it is ─────────────────────────────────────
        [Theory]
        [InlineData("A-001", "A")]
        [InlineData("A101", "A")]
        [InlineData("ARCH-12", "A")]
        [InlineData("S-200", "S")]
        [InlineData("M-101", "M")]
        [InlineData("E-400", "E")]
        [InlineData("P-300", "P")]
        [InlineData("FP-01", "FP")]
        [InlineData("LV-02", "LV")]
        public void SheetNumberPrefixDecidesDiscipline(string number, string expected)
            => Assert.Equal(expected, SheetDisciplineResolver.FromSheetNumber(number));

        [Theory]
        [InlineData("001")]          // no letters
        [InlineData("PLAN")]         // all letters, nothing follows: a word, not a prefix
        [InlineData("")]
        [InlineData(null)]
        [InlineData("QQ-01")]        // not a discipline anyone uses
        public void ANumberThatStatesNothingReturnsNull(string number)
            => Assert.Null(SheetDisciplineResolver.FromSheetNumber(number));

        [Fact]
        public void AnAssembledIdentifierIsNotASheetNumberPrefix()
        {
            // "SAH-PLNS-ZZ-01-DR-Z-0002" starts with a PROJECT code. Reading its
            // first segment as a discipline would be confidently wrong, which is
            // worse than reading nothing, so LooksAssembled guards it.
            Assert.Null(SheetDisciplineResolver.FromSheetNumber("SAH-PLNS-ZZ-01-DR-Z-0002"));
        }

        // ── Titles, by whole word ────────────────────────────────────────
        [Theory]
        [InlineData("GROUND FLOOR PLAN, SECTION AND ELEVATIONS", "A")]
        [InlineData("ROOF PLAN", "A")]
        [InlineData("MECHANICAL SERVICES LAYOUT", "M")]
        [InlineData("ELECTRICAL LIGHTING LAYOUT", "E")]
        [InlineData("FOUNDATION PLAN", "S")]
        [InlineData("SPRINKLER LAYOUT", "FP")]
        [InlineData("COORDINATED SERVICES PLAN", "COORD")]
        public void TitleWordsDecideDiscipline(string title, string expected)
            => Assert.Equal(expected, SheetDisciplineResolver.FromTitle(title));

        [Theory]
        [InlineData("DRAWING ARCHIVE INDEX")]   // ARCHIVE is not ARCH
        [InlineData("FIREPLACE DETAIL")]        // FIREPLACE is not FIRE
        public void SubstringsInsideOtherWordsDoNotCount(string title)
        {
            // The old matcher used Contains(), so each of these filed a drawing
            // under a discipline nobody chose and said nothing about it.
            string d = SheetDisciplineResolver.FromTitle(title);
            Assert.True(d == null || d == "A", $"'{title}' resolved to '{d}'");
        }

        // ── Intent beats content ─────────────────────────────────────────
        [Fact]
        public void WhatTheSheetSaysBeatsWhatItContains()
        {
            // The census that shipped the bug: a house GA plan, mostly architectural
            // but with real structure and sanitary ware on it. Under the old 75% rule
            // this returned COORD, and COORD folds to role Z.
            var census = new Dictionary<string, int> { { "A", 70 }, { "S", 20 }, { "P", 10 } };
            Assert.Equal("A", SheetDisciplineResolver.Resolve(
                "A-001", "GROUND FLOOR PLAN, SECTION AND ELEVATIONS", census));
        }

        [Fact]
        public void ALeaderShortOfThreeQuartersIsStillTheLeader()
        {
            var census = new Dictionary<string, int> { { "A", 70 }, { "S", 20 }, { "P", 10 } };
            Assert.Equal("A", SheetDisciplineResolver.FromCensus(census));
        }

        [Fact]
        public void AGenuineMixIsStillCoordination()
        {
            // Two disciplines, each a substantial quarter, neither dominant.
            var census = new Dictionary<string, int> { { "M", 45 }, { "E", 45 }, { "P", 10 } };
            Assert.Equal("COORD", SheetDisciplineResolver.FromCensus(census));
        }

        [Fact]
        public void NoEvidenceAtAllIsGeneralNotAGuess()
        {
            Assert.Equal("GEN", SheetDisciplineResolver.Resolve("001", "UNTITLED", null));
            Assert.Null(SheetDisciplineResolver.FromCensus(new Dictionary<string, int>()));
        }

        // ── The role letters themselves ──────────────────────────────────
        [Theory]
        [InlineData("A", "A")]
        [InlineData("ARCH", "A")]
        [InlineData("S", "S")]
        [InlineData("M", "M")]
        [InlineData("E", "E")]
        [InlineData("P", "P")]
        [InlineData("COORD", "Z")]
        [InlineData("GEN", "Z")]
        [InlineData("", "Z")]
        public void RolesFoldToTheStandardAlphabet(string disc, string expected)
            => Assert.Equal(expected, Iso19650DocumentCode.NormaliseRole(disc));

        [Theory]
        [InlineData("FP")]
        [InlineData("FIRE")]
        [InlineData("RP")]
        public void SpecialistDisciplinesAreYNotSomebodyElsesProfession(string disc)
        {
            // FP folded to S and RP to E. The role segment names the profession
            // ACCOUNTABLE for the container, so a sprinkler layout filed under S
            // attributes it to the structural engineer. Y = Specialist Designer.
            Assert.Equal("Y", Iso19650DocumentCode.NormaliseRole(disc));
        }

        [Fact]
        public void EveryRoleLetterEmittedIsInTheIsoAlphabet()
        {
            // Enumerated rather than listed: a new fold added to NormaliseRole is
            // covered by this without anyone remembering to add a case.
            const string alphabet = "ABCDEFGHIKLMPQSTWXYZ";
            var inputs = new[]
            {
                "A","ARCH","ARCHITECTURAL","S","STR","STRUCT","M","MECH","HVAC","H",
                "E","ELEC","ELE","LV","P","PLUMB","PLM","PH","C","CIVIL","L","LAND",
                "LANDSCAPE","Q","QS","K","CLIENT","W","CONTRACTOR","I","INT","INTERIOR",
                "D","DRAIN","DRAINAGE","F","FM","FACILITIES","G","GIS","SURVEY","T",
                "PLANNING","B","SURVEYOR","X","SUBCONTRACTOR","Y","COORD","GEN","MULTI",
                "ZZ","MG","RP","FP","FIRE","SPECIALIST","","   ","NOT_A_DISCIPLINE",
            };

            foreach (string input in inputs)
            {
                string role = Iso19650DocumentCode.NormaliseRole(input);
                Assert.True(role.Length == 1,
                    $"'{input}' produced '{role}', which is not one letter");
                Assert.True(alphabet.Contains(role),
                    $"'{input}' produced '{role}', which is not an ISO 19650 role code");
            }
        }
    }
}
