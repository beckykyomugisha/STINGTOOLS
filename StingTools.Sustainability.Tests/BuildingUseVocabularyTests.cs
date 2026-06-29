using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS K3 — one canonical building-use vocabulary across the catalog + load profiles
    // + water profiles, with friendly labels and profile subtypes.
    public class BuildingUseVocabularyTests
    {
        [Fact]
        public void Catalog_CoversTheCanonicalVocabulary()
        {
            foreach (var u in new[] { "office", "residential", "healthcare", "clinic", "hotel",
                                      "hotel-public", "retail", "restaurant", "kitchen", "education",
                                      "lecture-hall", "library", "warehouse", "lab", "industrial",
                                      "datacentre", "gym", "worship", "cinema", "parking" })
                Assert.Contains(u, BuildingUseCatalog.CommonUses);
        }

        [Theory]
        [InlineData("office", "Office")]
        [InlineData("hotel-public", "Hotel (public/lobby)")]
        [InlineData("datacentre", "Data centre")]
        [InlineData("some-new-use", "Some New Use")]   // title-case fallback
        public void FriendlyLabel_IsHuman(string id, string expected)
            => Assert.Equal(expected, BuildingUseCatalog.FriendlyLabel(id));

        // Every catalog use resolves a real (non-office-default) load profile from the
        // shipped library — the "one vocabulary" guarantee.
        [Fact]
        public void EveryCatalogUse_ResolvesARealLoadProfile()
        {
            var lib = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            foreach (var use in BuildingUseCatalog.CommonUses)
            {
                var r = lib.ResolveForUse(use);
                Assert.NotNull(r.Profile);
                if (use != "office")
                    Assert.NotEqual("office-default", r.MatchKind);   // not a silent office swap
            }
        }

        [Fact]
        public void EveryCatalogUse_HasAWaterProfile()
        {
            var reg = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));
            var keys = reg.All.Select(p => p.BuildingUse.ToLowerInvariant()).ToHashSet();
            foreach (var use in BuildingUseCatalog.CommonUses)
                Assert.Contains(use, keys);   // exact water row, not the office fallback
        }

        // ── WS K3 subtype overlay ─────────────────────────────────────────────
        [Fact]
        public void Subtype_OverlaysOnlyDeclaredFields_RestInheritFromParent()
        {
            const string json = @"{ ""profiles"": [
              { ""id"":""Office"", ""occupantDensityM2PerPerson"":10, ""lightingWPerM2"":9,
                ""equipmentWPerM2"":12, ""dhwLPerPersonDay"":5, ""source"":""seed"", ""edgeBuildingType"":""Offices"" },
              { ""id"":""OfficeTradingFloor"", ""subtypeOf"":""Office"", ""equipmentWPerM2"":60 }
            ] }";
            var lib = LoadProfileLibrary.FromJson(json);
            var sub = lib.ById["OfficeTradingFloor"];
            Assert.Equal("Office", sub.SubtypeOf);
            Assert.Equal(60, sub.EquipmentWPerM2, 1);            // overridden
            Assert.Equal(10, sub.OccupantDensityM2PerPerson, 1); // inherited
            Assert.Equal(9, sub.LightingWPerM2, 1);              // inherited
            Assert.Equal("Offices", sub.EdgeBuildingType);       // inherited
        }

        [Fact]
        public void ProjectOverride_OverlaysCorporateProfile()
        {
            const string corp = @"{ ""profiles"": [ { ""id"":""Office"", ""lightingWPerM2"":9, ""equipmentWPerM2"":12 } ] }";
            const string proj = @"{ ""profiles"": [ { ""id"":""Office"", ""lightingWPerM2"":6 } ] }";
            var lib = LoadProfileLibrary.FromJson(corp, proj);
            Assert.Equal(6, lib.ById["Office"].LightingWPerM2, 1);   // override won
            Assert.Equal(12, lib.ById["Office"].EquipmentWPerM2, 1); // untouched field inherited
        }
    }
}
