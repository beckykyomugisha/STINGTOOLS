using System.Collections.Generic;
using StingTools.BOQ;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase H1 (KUT lifecycle) — SpecLink section store + CSI→unit bridge.
    public class SpecStoreTests
    {
        [Fact]
        public void Parse_ArrayShape_NormalisesSectionAndReadsFields()
        {
            string json = @"[
              { ""section"": ""08 11 00"", ""title"": ""Metal Doors"", ""description"": ""Supply and install hollow metal doors per Part 2."", ""unit"": ""each"" }
            ]";
            var store = SpecStore.Parse(json);
            Assert.Single(store);
            var s = SpecStore.Get(store, "081100"); // unspaced lookup must hit the spaced key
            Assert.NotNull(s);
            Assert.Equal("Metal Doors", s.Title);
            Assert.StartsWith("Supply and install", s.Description);
            Assert.Equal("each", s.Unit);
        }

        [Fact]
        public void Parse_ObjectShape_AndTextAlias()
        {
            string json = @"{ ""sections"": [ { ""section"": ""23 05 00"", ""text"": ""HVAC common work results."" } ] }";
            var store = SpecStore.Parse(json);
            var s = SpecStore.Get(store, "23 05 00");
            Assert.NotNull(s);
            Assert.Equal("HVAC common work results.", s.Description); // 'text' aliases 'description'
        }

        [Fact]
        public void Parse_BadJson_ReturnsEmpty_NotThrow()
        {
            Assert.Empty(SpecStore.Parse("{not valid"));
            Assert.Empty(SpecStore.Parse(""));
            Assert.Empty(SpecStore.Parse(null));
        }

        [Fact]
        public void Get_UnknownSection_ReturnsNull()
        {
            var store = SpecStore.Parse(@"[{ ""section"": ""08 11 00"", ""description"": ""x"" }]");
            Assert.Null(SpecStore.Get(store, "99 99 99"));
            Assert.Null(SpecStore.Get(store, ""));
        }

        [Fact]
        public void BuildSectionToUnit_ReadsUnitColumn_EarliestWinsOnTie()
        {
            var rules = new List<CsiRule>
            {
                new CsiRule { Section = "08 11 00", Unit = "each" },   // earliest
                new CsiRule { Section = "08 11 00", Unit = "m2" },     // tie — must NOT override
                new CsiRule { Section = "03 30 00", Unit = "m3" },
                new CsiRule { Section = "09 99 99", Unit = "" },       // blank skipped
            };
            var map = CsiMasterFormat.BuildSectionToUnit(rules);
            Assert.Equal("each", map["081100"]);
            Assert.Equal("m3", map["033000"]);
            Assert.False(map.ContainsKey("099999"));
        }

        [Fact]
        public void ParseManualCsv_HeaderAware_MapsColumnsByName()
        {
            var lines = new[]
            {
                "Section,Title,Description,Unit",
                "08 11 00,Metal Doors,\"Supply, fit and finish hollow metal doors.\",each",
                "03 30 00,Concrete,Cast-in-situ structural concrete.,m3",
            };
            var store = SpecStore.ParseManualCsv(lines);
            Assert.Equal(2, store.Count);
            Assert.Equal("Metal Doors", store["081100"].Title);
            Assert.Contains("hollow metal", store["081100"].Description); // quoted comma preserved
            Assert.Equal("each", store["081100"].Unit);
            Assert.Equal("m3", store["033000"].Unit);
        }

        [Fact]
        public void ParseManualCsv_NoHeader_PositionalSectionThenTitle()
        {
            var lines = new[] { "08 11 00,Metal Doors", "08 50 00,Windows" };
            var store = SpecStore.ParseManualCsv(lines);
            Assert.Equal(2, store.Count);
            Assert.Equal("Windows", store["085000"].Title);
        }

        [Fact]
        public void ManualCsv_RoundTrips_ThroughSerializeAndParse()
        {
            var lines = new[]
            {
                "Section,Title,Description,Unit",
                "23 05 00,HVAC Common,Common work results for HVAC.,",
            };
            var store = SpecStore.ParseManualCsv(lines);
            string json = SpecStore.Serialize(store);
            var reparsed = SpecStore.Parse(json);          // Serialize → Parse must round-trip
            var s = SpecStore.Get(reparsed, "230500");
            Assert.NotNull(s);
            Assert.Equal("HVAC Common", s.Title);
            Assert.Equal("Common work results for HVAC.", s.Description);
        }

        [Fact]
        public void ParseCsvLines_EighthUnitColumn_BackCompatWithSixColRows()
        {
            var lines = new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title,Nrm2,Unit",
                "Doors,,,,08 11 00,Metal Doors,20,each",   // 8-col
                "Windows,,,,08 50 00,Windows,20",          // 7-col (no Unit)
                "Walls,,,,03 30 00,Concrete",              // 6-col (no Nrm2/Unit)
            };
            var rules = CsiMasterFormat.ParseCsvLines(lines);
            Assert.Equal(3, rules.Count);
            Assert.Equal("each", rules[0].Unit);
            Assert.Equal("20", rules[0].Nrm2);
            Assert.Equal("", rules[1].Unit);   // 7-col row → empty unit, no crash
            Assert.Equal("", rules[2].Nrm2);   // 6-col row → empty nrm2 + unit
            Assert.Equal("", rules[2].Unit);
        }
    }
}
