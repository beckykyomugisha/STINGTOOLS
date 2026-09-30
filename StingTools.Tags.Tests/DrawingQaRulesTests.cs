using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The Revit-free decisions behind the drawing QA / finishing tools
    /// (Sync Styles, Heal Title Blocks, Renumber, managed templates). Each
    /// group names the DTW finding it holds shut.
    /// </summary>
    public class DrawingQaRulesTests
    {
        // ── DTW-2: a cached managed template is only current when its stamp matches the pack ──

        [Fact]
        public void CachedTemplate_SameChecksum_IsCurrent()
            => Assert.True(DrawingQaRules.IsCachedTemplateCurrent(new string('a', 64), new string('a', 64)));

        [Fact]
        public void CachedTemplate_PackEdited_IsNotCurrent()
            => Assert.False(DrawingQaRules.IsCachedTemplateCurrent(new string('a', 64), new string('b', 64)));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void CachedTemplate_Unstamped_IsNotCurrent(string stored)
            => Assert.False(DrawingQaRules.IsCachedTemplateCurrent(stored, new string('a', 64)));

        // ── DTW-5: titleBlockParams may never write the sheet's own number or name ──

        [Theory]
        [InlineData("Sheet Number", null)]
        [InlineData("sheet name", null)]
        [InlineData(" Sheet Number ", "INVALID")]
        [InlineData("Numéro de feuille", "SHEET_NUMBER")]   // localised label, caught by what it is
        [InlineData("Nom de feuille", "SHEET_NAME")]
        public void SheetIdentity_IsRefused(string key, string bip)
            => Assert.True(DrawingQaRules.IsSheetIdentityParam(key, bip));

        [Theory]
        [InlineData("Sheet Title", null)]
        [InlineData("Drawing Number", "INVALID")]
        [InlineData("Revision", null)]
        [InlineData("PRJ_SHEET_SYSTEM_TXT", null)]
        public void OtherTitleBlockCells_AreWritable(string key, string bip)
            => Assert.False(DrawingQaRules.IsSheetIdentityParam(key, bip));

        [Fact]
        public void ShippedDrawingTypes_DeclareNoSheetIdentityKeys()
        {
            var path = System.IO.Path.Combine(RepoRoot(), "StingTools", "Data", "STING_DRAWING_TYPES.json");
            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var dt in root["drawingTypes"])
            {
                if (!(dt["titleBlockParams"] is Newtonsoft.Json.Linq.JObject tb)) continue;
                foreach (var prop in tb.Properties())
                    if (DrawingQaRules.IsSheetIdentityParam(prop.Name, null))
                        offenders.Add($"{dt["id"]}:{prop.Name}");
            }
            Assert.Empty(offenders);
        }

        // ── DTW-15: title-block numbers are compared culture-free, in the named unit ──

        [Theory]
        [InlineData("2.5", "2.500")]
        [InlineData("3000", "3000.0000001")]
        [InlineData("", "0")]
        public void NumericText_SameValue_IsEqual(string a, string b)
            => Assert.True(DrawingQaRules.NumericTextEquals(a, b));

        [Theory]
        [InlineData("2.5", "25")]      // a comma-decimal culture used to read "2.5" as 25
        [InlineData("3000", "9.84252")] // mm vs feet
        [InlineData("2,5", "2.5")]     // not an invariant number
        [InlineData("abc", "abc")]
        public void NumericText_DifferentOrUnparsable_IsNotEqual(string a, string b)
            => Assert.False(DrawingQaRules.NumericTextEquals(a, b));

        [Fact]
        public void NumericText_IgnoresThreadCulture()
        {
            var prior = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.True(DrawingQaRules.TryParseInvariant("2.5", out var v));
                Assert.Equal(2.5, v);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
        }

        // ── DTW-11: Sheet Number from ISO plans against every sheet number ──

        private static DrawingQaRules.IsoRenumberCandidate Cand(string cur, string to, bool locked = false)
            => new DrawingQaRules.IsoRenumberCandidate { Id = cur, Current = cur, Target = to, Locked = locked };

        [Fact]
        public void IsoRenumber_TargetHeldOutsideThePlan_StaysAndIsReported()
        {
            // "X" is an unstamped sheet nobody is renaming; the old two-pass rename
            // failed its second pass and left A-001 on ~STINGTMP~0000.
            var plan = DrawingQaRules.PlanIsoRenumber(
                new[] { Cand("A-001", "X"), Cand("A-002", "P-O-V-L-DR-A-0002") },
                new[] { "A-001", "A-002", "X" });
            Assert.Equal(new[] { "A-002" }, plan.Moves.Select(m => m.Current));
            Assert.Single(plan.Held);
        }

        [Fact]
        public void IsoRenumber_HeldCascades_ToTheSheetThatWantedTheStayersNumber()
        {
            // A-001 cannot move (X is held), so A-002 cannot take A-001's number either.
            var plan = DrawingQaRules.PlanIsoRenumber(
                new[] { Cand("A-001", "X"), Cand("A-002", "A-001") },
                new[] { "A-001", "A-002", "X" });
            Assert.Empty(plan.Moves);
            Assert.Equal(2, plan.Held.Count);
        }

        [Fact]
        public void IsoRenumber_SwapBetweenMovers_IsAllowed()
        {
            var plan = DrawingQaRules.PlanIsoRenumber(
                new[] { Cand("A-001", "A-002"), Cand("A-002", "A-001") },
                new[] { "A-001", "A-002" });
            Assert.Equal(2, plan.Moves.Count);
        }

        [Fact]
        public void IsoRenumber_LockedSheet_KeepsItsNumber_AndBlocksItsTarget()
        {
            var plan = DrawingQaRules.PlanIsoRenumber(
                new[] { Cand("A-001", "ISO-1", locked: true), Cand("A-002", "A-001") },
                new[] { "A-001", "A-002" });
            Assert.Equal(new[] { "A-001" }, plan.Locked);
            Assert.Empty(plan.Moves);
            Assert.Single(plan.Held);
        }

        [Fact]
        public void IsoRenumber_DuplicateTargets_AreReported()
        {
            var plan = DrawingQaRules.PlanIsoRenumber(
                new[] { Cand("A-001", "ISO-1"), Cand("A-002", "iso-1") },
                new[] { "A-001", "A-002" });
            Assert.Single(plan.Duplicates);
            Assert.Empty(plan.Moves);
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
