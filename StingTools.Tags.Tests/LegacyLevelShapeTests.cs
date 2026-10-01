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

        // ── DTW-225: a NEW sheet continues the shape its type + level already use ──

        private const string Pattern = "E-{lvl}-{seq:D3}";

        private static (string Current, string Legacy) Templates(string level)
        {
            var extras = new Dictionary<string, string>();
            return (SheetNumberEngine.Template(Pattern, "E", ProductionEdgeDecisions.NumberLevel(Pattern, level, null, null),
                        "", "", "", "Plan", extras),
                    SheetNumberEngine.Template(Pattern, "E", LegacyLevelShape.NumberLevel(Pattern, level, null),
                        "", "", "", "Plan", extras));
        }

        private static LegacyLevelShape.ExistingSheet Sheet(string type, string ctx, string number)
            => new LegacyLevelShape.ExistingSheet(type, ctx, number);

        [Fact]
        public void ANewSheetBesideLegacySheetsOfItsTypeAndLevelTakesTheLegacyLevel()
        {
            var t = Templates("Basement 1");
            var existing = new[] { Sheet("elec-power", "Basement 1::#L312::::", "E-Basement-004") };
            // RED before DTW-225: the new sheet took ShortLevel ("Basemen1") and the
            // level's numbers split into two shapes.
            var lvl = LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Basement 1", 312, null,
                t.Current, t.Legacy, existing);
            Assert.Equal("Basement 1", lvl);   // SafeShort cuts it to "Basement" downstream
            var number = SheetNumberEngine.ApplyTokenPattern(Pattern, "E", lvl, "", "", "", "Plan", 5,
                new Dictionary<string, string>());
            Assert.Equal("E-Basement-005", number);
            // The counter template follows the same shape, so the seed reads the old numbers.
            Assert.Equal(4, LegacyLevelShape.Sequence("E-Basement-004", t.Legacy));
        }

        [Fact]
        public void TheLevelIsMatchedByIdAfterARename()
        {
            var t = Templates("Basement 1");
            var existing = new[] { Sheet("elec-power", "Old name::#L312::::", "E-Basement-004") };
            Assert.Equal("Basement 1", LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Basement 1", 312, null,
                t.Current, t.Legacy, existing));
        }

        [Fact]
        public void LegacySheetsOfAnotherTypeOrLevelDoNotDecide()
        {
            var t = Templates("Basement 1");
            var existing = new[]
            {
                Sheet("elec-lighting", "Basement 1::#L312::::", "E-Basement-004"),   // other type
                Sheet("elec-power", "Basement 2::#L313::::", "E-Basement-001"),      // other level, same legacy shape
            };
            Assert.Null(LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Basement 1", 312, null,
                t.Current, t.Legacy, existing));
        }

        [Fact]
        public void CurrentShapeSheetsKeepTheCurrentShape()
        {
            var t = Templates("Basement 1");
            var existing = new[] { Sheet("elec-power", "Basement 1::#L312::::", "E-Basemen1-002") };
            Assert.Null(LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Basement 1", 312, null,
                t.Current, t.Legacy, existing));
        }

        [Fact]
        public void ALevelWhoseShapeDidNotChangeIsLeftAlone()
        {
            var t = Templates("Level 1");
            var existing = new[] { Sheet("elec-power", "Level 1::#L5::::", "E-Level1-001") };
            Assert.Null(LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Level 1", 5, null,
                t.Current, t.Legacy, existing));
        }

        [Fact]
        public void APreIdStampMatchesByLevelName()
        {
            var t = Templates("Basement 1");
            var existing = new[] { Sheet("elec-power", "Basement 1::::", "E-Basement-004") };
            Assert.Equal("Basement 1", LegacyLevelShape.NewSheetNumberLevel(Pattern, "elec-power", "Basement 1", 312, null,
                t.Current, t.Legacy, existing));
        }
    }
}
