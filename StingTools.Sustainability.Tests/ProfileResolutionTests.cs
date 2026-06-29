using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS K2 — data-driven use→profile resolution: id → alias → loose → nearest
    // sibling → Office, never a silent office swap. DHW comes from the profile.
    public class ProfileResolutionTests
    {
        private static LoadProfileLibrary Lib()
        {
            var lib = new LoadProfileLibrary();
            lib.ById["Office"]      = new LoadProfile { Id = "Office", OccupantDensityM2PerPerson = 10, DhwLPerPersonDay = 5,
                                                        Aliases = { "office", "commercial", "workplace" } };
            lib.ById["Residential"] = new LoadProfile { Id = "Residential", OccupantDensityM2PerPerson = 35, DhwLPerPersonDay = 45,
                                                        Aliases = { "residential", "dwelling", "house", "apartment", "home" } };
            lib.ById["PatientRoom"] = new LoadProfile { Id = "PatientRoom", OccupantDensityM2PerPerson = 12, DhwLPerPersonDay = 60,
                                                        Aliases = { "healthcare", "hospital", "ward", "patient" } };
            lib.ById["HotelGuestroom"] = new LoadProfile { Id = "HotelGuestroom", OccupantDensityM2PerPerson = 25, DhwLPerPersonDay = 120,
                                                        Aliases = { "hotel", "motel", "guest room" } };
            return lib;
        }

        [Fact]
        public void DirectId_Matches()
        {
            var r = Lib().ResolveForUse("Residential");
            Assert.Equal("Residential", r.Profile.Id);
            Assert.Equal("direct", r.MatchKind);
            Assert.False(r.IsFallback);
        }

        [Theory]
        [InlineData("dwelling", "Residential")]
        [InlineData("house", "Residential")]
        [InlineData("hospital", "PatientRoom")]
        [InlineData("hotel", "HotelGuestroom")]
        public void Alias_ResolvesToCanonicalProfile_NotOffice(string use, string expected)
        {
            var r = Lib().ResolveForUse(use);
            Assert.Equal(expected, r.Profile.Id);
            Assert.Equal("alias", r.MatchKind);
            Assert.False(r.IsFallback);
            Assert.NotEqual("Office", r.Profile.Id);
        }

        [Fact]
        public void LooseMatch_IgnoresCaseSpaceHyphen()
        {
            var r = Lib().ResolveForUse("hotel-guestroom");
            Assert.Equal("HotelGuestroom", r.Profile.Id);
        }

        [Fact]
        public void NearestSibling_BySubstring_IsFlaggedFallback()
        {
            // "luxury apartment block" contains alias "apartment" → Residential (nearest).
            var r = Lib().ResolveForUse("luxury apartment block");
            Assert.Equal("Residential", r.Profile.Id);
            Assert.Equal("nearest", r.MatchKind);
            Assert.True(r.IsFallback);
            Assert.Contains("Residential", r.FromTo);
        }

        [Fact]
        public void Unresolved_FallsToOffice_FLAGGED_NotSilent()
        {
            var r = Lib().ResolveForUse("zzz-unknown-use");
            Assert.Equal("Office", r.Profile.Id);
            Assert.Equal("office-default", r.MatchKind);
            Assert.True(r.IsFallback);   // visible fallback, never silent
        }

        [Fact]
        public void Blank_IsUnsetFallback()
        {
            var r = Lib().ResolveForUse("");
            Assert.True(r.IsFallback);
            Assert.Equal("unset", r.MatchKind);
        }

        [Fact]
        public void Dhw_ComesFromTheResolvedProfile()
        {
            Assert.Equal(45, Lib().ResolveForUse("house").Profile.DhwLPerPersonDay, 1);
            Assert.Equal(120, Lib().ResolveForUse("hotel").Profile.DhwLPerPersonDay, 1);
            Assert.Equal(5, Lib().ResolveForUse("office").Profile.DhwLPerPersonDay, 1);
        }

        [Fact]
        public void ResidentialDensity_Gives5Occupants_For170m2_NotOffice17()
        {
            var resi = Lib().ResolveForUse("residential").Profile;
            Assert.Equal(5, resi.OccupantCountFor(170));     // 170 / 35 ≈ 5
            var office = Lib().ResolveForUse("office").Profile;
            Assert.Equal(17, office.OccupantCountFor(170));   // 170 / 10 = 17 (the old bug)
        }
    }
}
