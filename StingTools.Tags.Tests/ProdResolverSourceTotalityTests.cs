// ══════════════════════════════════════════════════════════════════════════
//  ProdResolverSourceTotalityTests.cs — a source tier nobody classified must
//  be a failing test, not a gate that quietly opens.
//
//  `ProdResolver.IsSpecific` is a WHITELIST of five tiers that returns false
//  for everything else, and it has two consumers with OPPOSITE safe defaults:
//
//    Prod_CoverageAudit          false ⇒ counted as generic ⇒ coverage % is
//                                UNDERSTATED. Conservative; a reader chasing a
//                                low number finds the truth.
//    TypeRenamePlanner code-gate false ⇒ "no answer to protect" ⇒ the refusal
//                                DOES NOT FIRE and a correctly-resolved product
//                                code is renamed away. There is no signal at
//                                all: the CSV row reads "rename", the same as
//                                every legitimate one.
//
//  Two tiers were added to `Sources` after `IsSpecific` was written — `declared`
//  and `sleeve` — and both happen to be listed. The next one need not be, and
//  nothing in the build would say so. That is the whole defect: not a wrong
//  answer today, an open trapdoor for the next author.
//
//  So the split is now declared in BOTH directions and this reflects over the
//  consts to prove it is total. Deliberately reflection and not a hand-written
//  list of the seven: a list would need the same edit the whitelist needs, and
//  would therefore fail to catch the same omission.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ProdResolverSourceTotalityTests
    {
        /// <summary>Every public string const on <c>ProdResolver.Sources</c>, by
        /// reflection — so adding one is enough to be covered.</summary>
        private static List<(string Name, string Value)> DeclaredSources()
            => typeof(ProdResolver.Sources)
               .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
               .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
               .Select(f => (f.Name, (string)f.GetRawConstantValue()))
               .OrderBy(x => x.Name)
               .ToList();

        [Fact]
        public void There_Are_Source_Tiers_To_Classify_At_All()
        {
            // Reflection returning nothing would make every assertion below vacuously
            // true — the empty-list-standing-in-for-an-error shape this codebase keeps
            // finding. Six is the count at the time of writing minus room to shrink.
            var all = DeclaredSources();
            Assert.True(all.Count >= 6, "only " + all.Count + " source consts found by reflection");
            Assert.Contains("project", all.Select(x => x.Value));
            Assert.Contains("category", all.Select(x => x.Value));
        }

        [Fact]
        public void Every_Source_Tier_Is_Either_Specific_Or_Generic_And_Never_Both()
        {
            var unclassified = new List<string>();
            var both = new List<string>();

            foreach (var (name, value) in DeclaredSources())
            {
                bool s = ProdResolver.IsSpecific(value);
                bool g = ProdResolver.IsGeneric(value);
                if (!s && !g) unclassified.Add($"Sources.{name} = \"{value}\"");
                if (s && g) both.Add($"Sources.{name} = \"{value}\"");
            }

            Assert.True(unclassified.Count == 0,
                "These source tiers are in NEITHER IsSpecific nor IsGeneric. Until they are, "
              + "TypeRenamePlanner's code-gate treats them as 'nothing to protect' and will "
              + "rename a correctly-resolved product code away with no signal:\n  "
              + string.Join("\n  ", unclassified));

            Assert.True(both.Count == 0,
                "These source tiers are in BOTH sets, so the two functions disagree about "
              + "what they mean:\n  " + string.Join("\n  ", both));
        }

        [Theory]
        [InlineData("project", true)]
        [InlineData("declared", true)]
        [InlineData("corporate", true)]
        [InlineData("lps", true)]
        [InlineData("sleeve", true)]
        [InlineData("category", false)]
        [InlineData("gen", false)]
        public void The_Split_Puts_Each_Tier_Where_The_Resolver_Means_It(string source, bool specific)
        {
            // The values, spelled out, so a change of MEANING (moving "declared" to
            // generic, say) fails here as well as in the totality gate above — which
            // would still pass, because a moved tier is still classified exactly once.
            Assert.Equal(specific, ProdResolver.IsSpecific(source));
            Assert.Equal(!specific, ProdResolver.IsGeneric(source));
        }

        [Fact]
        public void A_Tier_That_Does_Not_Exist_Is_In_Neither_Set()
        {
            // Both functions are whitelists, so an unknown string must fall out of both —
            // which is what makes the totality gate above meaningful rather than an
            // identity. If IsGeneric were written as !IsSpecific this would fail, and so
            // would the whole point of the file.
            Assert.False(ProdResolver.IsSpecific("some-new-tier"));
            Assert.False(ProdResolver.IsGeneric("some-new-tier"));
            Assert.False(ProdResolver.IsSpecific(null));
            Assert.False(ProdResolver.IsGeneric(null));
        }
    }
}
