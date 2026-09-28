using System.Collections.Generic;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The pre-flight half of the 2026-09-17 fix. The pipeline can now REPORT a token
    /// whose write failed; this answers the same question before anything is tagged,
    /// and it is the only check that can see the case AuditBindingScope cannot — a
    /// parameter bound per instance but over a category set that omits this category.
    /// That is what 27-of-175 looks like: most categories fine, one not bound.
    /// </summary>
    public class TokenBindingCoverageTests
    {
        private static readonly string[] Tokens = { "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT" };

        private static TokenBindingFact F(string p, string c, TokenBindingScope s)
            => new TokenBindingFact { Param = p, Category = c, Scope = s };

        [Fact]
        public void Fully_instance_bound_is_no_gap()
        {
            var facts = new[]
            {
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Instance),
                F("ASS_PRODCT_COD_TXT", "Doors", TokenBindingScope.Instance),
            };
            Assert.Empty(TokenBindingCoverage.Analyse(facts, Tokens, new[] { "Doors" }));
        }

        [Fact]
        public void A_category_the_binding_omits_is_a_gap()
        {
            // Doors bound, Rooms not — the 27-of-175 shape.
            var facts = new[]
            {
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Instance),
                F("ASS_PRODCT_COD_TXT", "Doors", TokenBindingScope.Instance),
            };
            var gaps = TokenBindingCoverage.Analyse(facts, Tokens, new[] { "Doors", "Rooms" });

            Assert.Equal(2, gaps.Count);
            Assert.All(gaps, g => Assert.Equal("Rooms", g.Category));
            Assert.All(gaps, g => Assert.Equal(TokenBindingScope.Missing, g.Scope));
            Assert.All(gaps, g => Assert.Contains("not bound to this category", g.Why));
        }

        [Fact]
        public void A_type_binding_is_a_gap_and_says_so()
        {
            var facts = new[] { F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Type) };
            var gaps = TokenBindingCoverage.Analyse(facts, new[] { "ASS_FUNC_TXT" }, new[] { "Doors" });

            var g = Assert.Single(gaps);
            Assert.Equal(TokenBindingScope.Type, g.Scope);
            Assert.Contains("TYPE", g.Why);
        }

        [Fact]
        public void An_instance_binding_beats_a_type_binding_for_the_same_pair()
        {
            // Revit allows the same definition under more than one binding. If ANY of
            // them makes it instance-reachable the write succeeds, so that is not a gap.
            var facts = new[]
            {
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Type),
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Instance),
            };
            Assert.Empty(TokenBindingCoverage.Analyse(facts, new[] { "ASS_FUNC_TXT" }, new[] { "Doors" }));
        }

        [Fact]
        public void Order_of_facts_does_not_change_the_answer()
        {
            var a = new[]
            {
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Instance),
                F("ASS_FUNC_TXT", "Doors", TokenBindingScope.Type),
            };
            Assert.Empty(TokenBindingCoverage.Analyse(a, new[] { "ASS_FUNC_TXT" }, new[] { "Doors" }));
        }

        [Fact]
        public void No_bindings_at_all_reports_every_pair_not_silence()
        {
            // An empty document must not read as "all clear" — that is the same failure
            // as an empty list standing in for an error.
            var gaps = TokenBindingCoverage.Analyse(
                new TokenBindingFact[0], Tokens, new[] { "Doors", "Rooms" });
            Assert.Equal(4, gaps.Count);
        }

        [Fact]
        public void Null_facts_are_treated_as_no_bindings_not_no_gaps()
            => Assert.Equal(2, TokenBindingCoverage.Analyse(null, Tokens, new[] { "Doors" }).Count);

        [Fact]
        public void Duplicate_categories_are_not_double_counted()
        {
            var gaps = TokenBindingCoverage.Analyse(
                null, new[] { "ASS_FUNC_TXT" }, new[] { "Rooms", "Rooms" });
            Assert.Single(gaps);
        }

        [Fact]
        public void ByParam_groups_worst_first()
        {
            var gaps = TokenBindingCoverage.Analyse(
                new[] { F("ASS_PRODCT_COD_TXT", "Rooms", TokenBindingScope.Instance) },
                Tokens, new[] { "Rooms", "Doors" });

            var byParam = TokenBindingCoverage.ByParam(gaps);
            Assert.Equal("ASS_FUNC_TXT", byParam[0].Key);      // 2 gaps
            Assert.Equal(2, byParam[0].Value.Count);
            Assert.Equal("ASS_PRODCT_COD_TXT", byParam[1].Key); // 1 gap (Doors only)
            Assert.Single(byParam[1].Value);
        }

        /// <summary>
        /// Derived from the shipped registry rather than a list written here, so a token
        /// added to PARAMETER_REGISTRY.json is covered without editing this test.
        /// </summary>
        [Fact]
        public void Every_shipped_token_param_is_checked()
        {
            var tokens = ParamRegistryTokenNames();
            Assert.True(tokens.Count >= 8, "expected the eight ISO tag tokens");

            var gaps = TokenBindingCoverage.Analyse(null, tokens, new[] { "Rooms" });
            Assert.Equal(tokens.Count, gaps.Count);
            Assert.Equal(tokens.OrderBy(t => t), gaps.Select(g => g.Param).OrderBy(t => t));
        }

        private static List<string> ParamRegistryTokenNames()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "PARAMETER_REGISTRY.json");
            var doc = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            return doc["source_tokens"].Select(t => (string)t["param_name"]).ToList();
        }
    }
}
