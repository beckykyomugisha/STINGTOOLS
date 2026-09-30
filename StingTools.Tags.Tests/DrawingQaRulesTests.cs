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
    }
}
