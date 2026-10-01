// DTW-221 / DTW-222: sheets made before DTW-198 carry the level as number shaping
// cut it (SafeShort — "Ground Floor" → "GroundFl", "Basement 1" → "Basement"). The
// reused-sheet rename and Renumber must still recognise them.

using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class LegacyLevelShapeTests
    {
        // ── DTW-221: the pre-DTW-198 name ─────────────────────────────

        [Fact]
        public void LegacyNameCutsTheLevelLikeTheOldSubstitution()
        {
            var name = LegacyLevelShape.SheetName("Power - {lvl}", "E", "Ground Floor", null, null, "", "", "Plan",
                1, new Dictionary<string, string>(), null);
            Assert.Equal("Power - GroundFl", name);
        }

        [Fact]
        public void LegacyNameKeepsTheAreaSuffix()
        {
            var name = LegacyLevelShape.SheetName("Power - {lvl}", "E", "Ground Floor", null, null, "", "Zone A", "Plan",
                1, new Dictionary<string, string>(), "Zone A");
            Assert.Equal("Power - GroundFl - Zone A", name);
        }

        [Fact]
        public void APreRound8GeneratedNameIsRenamedToTheFullLevelName()
        {
            var extras = new Dictionary<string, string>();
            string expected = ProductionEdgeDecisions.SheetName("Power - {lvl}", "E", "Ground Floor", "", "", "Plan", 1, extras, null);
            string legacyNew = ProductionEdgeDecisions.SheetName("Power - {lvl}", "E", "Ground Floor", "", "", "Plan", 1, extras, null);
            string legacyOld = LegacyLevelShape.SheetName("Power - {lvl}", "E", "Ground Floor", null, null, "", "", "Plan", 1, extras, null);

            // The DTW-209 comparison against today's rule alone never matches the old name.
            Assert.Null(ProductionEdgeDecisions.ReusedSheetRename("Power - GroundFl", null, legacyNew, expected, false));
            // Against the old rule it does.
            Assert.Equal(expected, ProductionEdgeDecisions.ReusedSheetRename("Power - GroundFl", null, legacyOld, expected, false));
            Assert.Equal("Power - Ground Floor", expected);
        }

        [Fact]
        public void AHandEditedNameIsStillLeftAlone()
        {
            var extras = new Dictionary<string, string>();
            string expected = ProductionEdgeDecisions.SheetName("Power - {lvl}", "E", "Ground Floor", "", "", "Plan", 1, extras, null);
            string legacyOld = LegacyLevelShape.SheetName("Power - {lvl}", "E", "Ground Floor", null, null, "", "", "Plan", 1, extras, null);
            Assert.Null(ProductionEdgeDecisions.ReusedSheetRename("Ground floor power (client)", null, legacyOld, expected, false));
        }

        // ── DTW-222: Renumber recognises the old number shape ──────────

        [Theory]
        [InlineData("Basement 1", true)]     // "Basemen1" now, "Basement" before
        [InlineData("Level 1", false)]       // "Level1" both ways
        [InlineData("Mezzanine", false)]     // no trailing number: "Mezzanin" both ways
        public void NumberShapeChangedOnlyForLongDigitEndingLevels(string level, bool changed)
            => Assert.Equal(changed, LegacyLevelShape.NumberShapeChanged("E-{lvl}-{seq:D3}", level));

        [Fact]
        public void IsoPatternsNeverChangedShape()
            => Assert.False(LegacyLevelShape.NumberShapeChanged("{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq}", "Basement 1"));

        [Fact]
        public void AnOldShapeNumberMatchesTheLegacyTemplateAndKeepsItsSequence()
        {
            const string pattern = "E-{lvl}-{seq:D3}";
            var extras = new Dictionary<string, string>();
            string current = SheetNumberEngine.Template(pattern, "E", ProductionEdgeDecisions.NumberLevel(pattern, "Basement 1", null, null),
                "", "", "", "Plan", extras);
            string legacy = SheetNumberEngine.Template(pattern, "E", LegacyLevelShape.NumberLevel(pattern, "Basement 1", null),
                "", "", "", "Plan", extras);

            // RED before DTW-222: only the current template was tried, so the old number
            // read as a shape change and was converted.
            Assert.Null(LegacyLevelShape.Sequence("E-Basement-004", current));
            var pick = LegacyLevelShape.MatchTemplate("E-Basement-004", current, legacy);
            Assert.Equal(legacy, pick.Template);
            Assert.True(pick.Legacy);
            Assert.Equal(4, pick.Sequence);
        }

        [Fact]
        public void ACurrentShapeNumberPrefersTheCurrentTemplate()
        {
            const string pattern = "E-{lvl}-{seq:D3}";
            var extras = new Dictionary<string, string>();
            string current = SheetNumberEngine.Template(pattern, "E", ProductionEdgeDecisions.NumberLevel(pattern, "Basement 1", null, null),
                "", "", "", "Plan", extras);
            string legacy = SheetNumberEngine.Template(pattern, "E", LegacyLevelShape.NumberLevel(pattern, "Basement 1", null),
                "", "", "", "Plan", extras);
            var pick = LegacyLevelShape.MatchTemplate("E-Basemen1-002", current, legacy);
            Assert.Equal(current, pick.Template);
            Assert.False(pick.Legacy);
            Assert.Equal(2, pick.Sequence);
        }

        [Fact]
        public void ANumberInNeitherShapeMatchesNothing()
        {
            var pick = LegacyLevelShape.MatchTemplate("X-999", "E-Basemen1-\u0001", "E-Basement-\u0001");
            Assert.Null(pick.Sequence);
            Assert.False(pick.Legacy);
        }
    }
}
