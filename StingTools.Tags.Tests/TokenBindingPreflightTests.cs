using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-27: Batch Tag refuses at once on a model with no token parameters,
    /// instead of deriving 3,121 tags and reporting each one "could not be written".
    /// </summary>
    public class TokenBindingPreflightTests
    {
        private static readonly string[] Tokens =
        {
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
            "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
        };

        [Fact]
        public void EveryCategoryUnbound_IsNothingBound_WithTheFixInTheRefusal()
        {
            var r = TokenBindingPreflight.Evaluate(new[]
            {
                new TokenBindingProbe("Walls", 556, Tokens),
                new TokenBindingProbe("Doors", 104, Tokens),
            });

            Assert.Equal(TokenBindingVerdict.NothingBound, r.Verdict);
            Assert.Equal(660, r.TotalElementCount);
            Assert.Contains("Load Shared Parameters", r.RefusalText);
            Assert.Contains("THIS model", r.RefusalText);
            Assert.Contains("ASS_SEQ_NUM_TXT", r.RefusalText);
        }

        [Fact]
        public void OneCategoryBound_IsNotRefused_AndNamesTheOthers()
        {
            var r = TokenBindingPreflight.Evaluate(new[]
            {
                new TokenBindingProbe("Walls", 556, new string[0]),
                new TokenBindingProbe("Entourage", 56, Tokens),
                new TokenBindingProbe("RVT Links", 2, new[] { "ASS_SEQ_NUM_TXT" }),
            });

            Assert.Equal(TokenBindingVerdict.SomeCategoriesUnbound, r.Verdict);
            Assert.Equal(58, r.UnboundElementCount);
            Assert.Equal("", r.RefusalText);
            Assert.StartsWith("Token parameters missing on 2 categories", r.Summary);
            // Largest first, so the category that matters most is read first.
            Assert.True(r.Summary.IndexOf("Entourage") < r.Summary.IndexOf("RVT Links"));
        }

        [Fact]
        public void AllBound_SaysNothing()
        {
            var r = TokenBindingPreflight.Evaluate(new[] { new TokenBindingProbe("Walls", 3, new string[0]) });
            Assert.Equal(TokenBindingVerdict.AllBound, r.Verdict);
            Assert.Equal("", r.Summary);
        }

        [Fact]
        public void NothingProbed_IsNotARefusal()
        {
            // No token list loaded, or no elements: the pre-flight must not block the
            // run on its own lack of information.
            Assert.Equal(TokenBindingVerdict.NotProbed, TokenBindingPreflight.Evaluate(null).Verdict);
            Assert.Equal(TokenBindingVerdict.NotProbed,
                TokenBindingPreflight.Evaluate(new[] { new TokenBindingProbe("Walls", 0, Tokens) }).Verdict);
        }

        [Fact]
        public void ManyUnboundCategories_AreCappedInTheSummary()
        {
            var probes = Enumerable.Range(0, TokenBindingPreflight.MaxNamedCategories + 3)
                .Select(i => new TokenBindingProbe("Cat" + i, 10 + i, Tokens))
                .Append(new TokenBindingProbe("Walls", 1, new string[0]));
            var r = TokenBindingPreflight.Evaluate(probes);
            Assert.EndsWith("+3 more", r.Summary);
        }

        [Fact]
        public void BatchTag_RefusesBeforeTheModePicker()
        {
            string src = File.ReadAllText(Path.Combine(DrawingCatalogueFixture.RepoRoot(),
                "StingTools", "Tags", "BatchTagCommand.cs"));
            int probe = src.IndexOf("ProbeTokenBindings(taggableElements)", System.StringComparison.Ordinal);
            int refuse = src.IndexOf("TokenBindingVerdict.NothingBound", System.StringComparison.Ordinal);
            int picker = src.IndexOf("UI.StingModePicker.Show(", System.StringComparison.Ordinal);
            Assert.True(probe > 0, "Batch Tag no longer probes token bindings");
            Assert.True(refuse > probe && refuse < picker,
                "the NothingBound refusal must come before the mode picker, so nothing is derived or written");
            // The probe must use the scope the writer uses: instance lookup, writable.
            Assert.Contains("sample.LookupParameter(name)", src);
            Assert.Contains("p.IsReadOnly", src);
        }
    }
}
