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
