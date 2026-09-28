// Tests for Core/SharedParamTypeConflict.cs — the comparison behind the
// "LoadFamily back into project failed" pre-flight.
//
// The cases that matter here are the ones that look obvious and are not:
//   • the two sides are written in two vocabularies (YESNO vs
//     autodesk.spec:spec.bool.yesNo-2.0.0) and a string compare invents a
//     conflict that does not exist;
//   • an unreadable data type must not become a conflict, because an invented
//     conflict aborts a propagation that would have worked;
//   • number and integer must never be conflated to buy that.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SharedParamConflictTests
    {
        private static SharedParamFacts F(string name, string guid, string type) =>
            new SharedParamFacts { Name = name, Guid = Guid.Parse(guid), DataType = type };

        // The twelve that stopped STING_Tag_Universal.rfa loading, in miniature:
        // same GUID, family says Text, project says Number.
        private const string CritGuid = "fbbf5b6d-776a-58e2-a3ce-cbd9fbfe3f93";
        private const string StaleGuid = "b9d4e1a2-7c63-4f89-9e0a-1f5a2c8b3d46";

        [Fact]
        public void SameGuidDifferentType_IsAConflict()
        {
            var conflicts = SharedParamConflictDetector.Detect(
                new[] { F("ASS_CRITICALITY_RATING_NR", CritGuid, "autodesk.spec:spec.string-2.0.0") },
                new[] { F("ASS_CRITICALITY_RATING_NR", CritGuid, "autodesk.spec.aec:number-2.0.0") });

            var c = Assert.Single(conflicts);
            Assert.Equal(SharedParamConflictKind.TypeMismatch, c.Kind);
            Assert.Equal("ASS_CRITICALITY_RATING_NR", c.FamilyName);
            Assert.Contains("family offers string", c.Describe());
            Assert.Contains("project holds number", c.Describe());
        }

        [Fact]
        public void SameGuidSameType_IsNotAConflict()
        {
            Assert.Empty(SharedParamConflictDetector.Detect(
                new[] { F("ASS_TAG_1_TXT", CritGuid, "autodesk.spec:spec.string-2.0.0") },
                new[] { F("ASS_TAG_1_TXT", CritGuid, "autodesk.spec:spec.string-2.0.0") }));
        }

        [Theory]
        // A shared-parameter FILE word against the live ForgeTypeId of the same
        // spec. Every one of these pairs is the SAME parameter type, and a string
        // compare calls all four a conflict.
        [InlineData("YESNO", "autodesk.spec:spec.bool.yesNo-2.0.0")]
        [InlineData("TEXT", "autodesk.spec:spec.string-2.0.0")]
        [InlineData("LENGTH", "autodesk.spec.aec:length-2.0.0")]
        [InlineData("CURRENCY", "autodesk.spec.aec:currency-2.0.0")]
        public void TwoVocabulariesForOneSpec_AgreeRatherThanConflict(string spfWord, string forgeId)
        {
            Assert.True(SharedParamConflictDetector.SameType(spfWord, forgeId),
                $"'{spfWord}' and '{forgeId}' are the same spec");

            Assert.Empty(SharedParamConflictDetector.Detect(
                new[] { F("P", StaleGuid, spfWord) },
                new[] { F("P", StaleGuid, forgeId) }));
        }

        [Theory]
        [InlineData("NUMBER", "autodesk.spec.aec:integer-2.0.0")]
        [InlineData("autodesk.spec.aec:number-2.0.0", "autodesk.spec.aec:integer-2.0.0")]
        [InlineData("YESNO", "autodesk.spec:spec.string-2.0.0")]
        [InlineData("LENGTH", "autodesk.spec.aec:area-2.0.0")]
        public void DifferentSpecs_StayDifferent(string a, string b)
        {
            Assert.False(SharedParamConflictDetector.SameType(a, b), $"'{a}' and '{b}' are different specs");
        }

        [Fact]
        public void UnreadableDataType_IsNotReportedAsAConflict()
        {
            // A pre-flight that cannot read a type must not be able to block a run.
            Assert.Empty(SharedParamConflictDetector.Detect(
                new[] { F("P", StaleGuid, null) },
                new[] { F("P", StaleGuid, "autodesk.spec.aec:number-2.0.0") }));

            Assert.Empty(SharedParamConflictDetector.Detect(
                new[] { F("P", StaleGuid, "autodesk.spec:spec.string-2.0.0") },
                new[] { F("P", StaleGuid, "   ") }));
        }

        [Fact]
        public void ParameterTheProjectDoesNotHold_IsNotAConflict()
        {
            // Loading the family is how the project acquires it.
            Assert.Empty(SharedParamConflictDetector.Detect(
                new[] { F("ASS_NEW_TXT", CritGuid, "autodesk.spec:spec.string-2.0.0") },
                new[] { F("SOMETHING_ELSE", StaleGuid, "autodesk.spec.aec:number-2.0.0") }));
        }

        [Fact]
        public void SameNameDifferentGuid_IsANameCollision()
        {
            var conflicts = SharedParamConflictDetector.Detect(
                new[] { F("ASS_CST_STALE_BOOL", CritGuid, "autodesk.spec:spec.bool.yesNo-2.0.0") },
                new[] { F("ASS_CST_STALE_BOOL", StaleGuid, "autodesk.spec:spec.bool.yesNo-2.0.0") });

            var c = Assert.Single(conflicts);
            Assert.Equal(SharedParamConflictKind.NameCollision, c.Kind);
            Assert.Contains("same name, different GUID", c.Describe());
        }

        [Fact]
        public void AGuidMatchIsNotAlsoReportedAsANameCollision()
        {
            // The family's name for a GUID may differ from the project's. That is
            // one disagreement, not two, and double-reporting it would inflate
            // the count the operator is asked to act on.
            var conflicts = SharedParamConflictDetector.Detect(
                new[] { F("ASS_CST_STALE_TXT", CritGuid, "autodesk.spec:spec.string-2.0.0") },
                new[]
                {
                    F("ASS_CST_STALE_BOOL", CritGuid, "autodesk.spec:spec.bool.yesNo-2.0.0"),
                    F("ASS_CST_STALE_TXT", StaleGuid, "autodesk.spec:spec.string-2.0.0")
                });

            var c = Assert.Single(conflicts);
            Assert.Equal(SharedParamConflictKind.TypeMismatch, c.Kind);
        }

        [Fact]
        public void EmptyAndNullInputs_AreNotConflicts()
        {
            Assert.Empty(SharedParamConflictDetector.Detect(null, null));
            Assert.Empty(SharedParamConflictDetector.Detect(new SharedParamFacts[0], new SharedParamFacts[0]));
            Assert.Empty(SharedParamConflictDetector.Detect(
                new SharedParamFacts[] { null }, new SharedParamFacts[] { null }));
        }

        [Fact]
        public void Describe_NamesEveryConflictAndCountsTheRest()
        {
            var many = Enumerable.Range(0, 5)
                .Select(i => new SharedParamTypeConflict
                {
                    Kind = SharedParamConflictKind.TypeMismatch,
                    FamilyName = "P" + i,
                    FamilyGuid = Guid.Parse(CritGuid),
                    FamilyDataType = "TEXT",
                    ProjectDataType = "NUMBER"
                })
                .ToList();

            string all = SharedParamConflictDetector.Describe(many);
            Assert.StartsWith("5 shared parameters block the load:", all);
            for (int i = 0; i < 5; i++) Assert.Contains("P" + i, all);

            string capped = SharedParamConflictDetector.Describe(many, maxLines: 2);
            Assert.Contains("and 3 more", capped);
            Assert.DoesNotContain("P4", capped);
        }

        [Fact]
        public void Describe_ReturnsNullWhenThereIsNothingToSay()
        {
            // Callers use it as the whole "is there anything wrong" test, so an
            // empty list must not produce a message that reads like a finding.
            Assert.Null(SharedParamConflictDetector.Describe(new List<SharedParamTypeConflict>()));
            Assert.Null(SharedParamConflictDetector.Describe(null));
        }

        [Fact]
        public void Conflicts_ComeBackInAStableOrder()
        {
            var familySide = new[]
            {
                F("ZULU", "11111111-1111-1111-1111-111111111111", "TEXT"),
                F("ALPHA", "22222222-2222-2222-2222-222222222222", "TEXT"),
            };
            var projectSide = new[]
            {
                F("ZULU", "11111111-1111-1111-1111-111111111111", "NUMBER"),
                F("ALPHA", "22222222-2222-2222-2222-222222222222", "NUMBER"),
            };

            var first = SharedParamConflictDetector.Detect(familySide, projectSide);
            var second = SharedParamConflictDetector.Detect(
                Enumerable.Reverse(familySide), Enumerable.Reverse(projectSide));

            Assert.Equal(new[] { "ALPHA", "ZULU" }, first.Select(c => c.FamilyName));
            Assert.Equal(first.Select(c => c.FamilyName), second.Select(c => c.FamilyName));
        }

        [Theory]
        [InlineData("autodesk.spec.aec:length-2.0.0", "length")]
        [InlineData("autodesk.spec:spec.string-2.0.0", "string")]
        [InlineData("YESNO", "YESNO")]
        [InlineData("", "(unknown)")]
        [InlineData(null, "(unknown)")]
        public void TypeLabel_ReadsAsAHumanWouldNameIt(string raw, string expected)
        {
            Assert.Equal(expected, SharedParamConflictDetector.TypeLabel(raw));
        }
    }
}
