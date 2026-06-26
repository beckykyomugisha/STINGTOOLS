using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS B4 — building-use list is data-driven: the union of a curated seed set with
    // whatever keys the project's registries carry, so adding a use to the JSON
    // surfaces it in the dropdown with no UI code change.
    public class BuildingUseCatalogTests
    {
        [Fact]
        public void Resolve_IncludesCommonMissingUses()
        {
            var uses = BuildingUseCatalog.Resolve();
            foreach (var u in new[] { "office", "education", "warehouse", "lab", "restaurant", "industrial" })
                Assert.Contains(u, uses);
        }

        [Fact]
        public void Resolve_UnionsRegistryKeys_AppendedAfterSeed()
        {
            var uses = BuildingUseCatalog.Resolve(
                new[] { "office", "datacentre" },
                new[] { "clinic" });

            Assert.Contains("datacentre", uses);   // registry-only key surfaced
            Assert.Contains("clinic", uses);
            // Seed entries come first; registry-only extras are appended.
            Assert.True(uses.IndexOf("office") < uses.IndexOf("datacentre"));
        }

        [Fact]
        public void Resolve_IsCaseInsensitiveAndDistinct()
        {
            var uses = BuildingUseCatalog.Resolve(new[] { "Office", "OFFICE", "Retail" });
            Assert.Equal(uses.Count, uses.Distinct().Count());   // no dups
            Assert.Single(uses.Where(u => u == "office"));        // "Office"/"OFFICE" folded
        }

        [Fact]
        public void Resolve_SkipsWildcardAndBlanks()
        {
            var uses = BuildingUseCatalog.Resolve(new[] { "*", "", "  ", "school" });
            Assert.DoesNotContain("*", uses);
            Assert.DoesNotContain("", uses);
            Assert.Contains("school", uses);
        }

        [Fact]
        public void Resolve_NullSets_ReturnsSeedOnly()
        {
            var uses = BuildingUseCatalog.Resolve((System.Collections.Generic.IEnumerable<string>[])null);
            Assert.Equal(BuildingUseCatalog.CommonUses.Length, uses.Count);
        }
    }
}
