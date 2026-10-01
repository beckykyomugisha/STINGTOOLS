using System.Linq;
using StingTools.Core.Validation.Healthcare;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-25 — USP &lt;797&gt;/&lt;800&gt; cascade per room, from the shipped
    /// STING_HC_PHARMACY_USP.json; overrides only tighten; missing ΔP is NOT CHECKED.</summary>
    public class UspCascadeTests
    {
        private static UspCascadeFile Shipped()
        {
            var f = UspCascade.Parse(RepoData.Read("Healthcare/Specialist/STING_HC_PHARMACY_USP.json"), out var errors);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            return f;
        }

        private static UspRoomSpec Buffer797 => UspCascade.ForRoomClass(Shipped(), "PH-CSP-797");
        private static UspRoomSpec Csec800   => UspCascade.ForRoomClass(Shipped(), "PH-CSP-800");

        private static bool Fails(System.Collections.Generic.List<UspFinding> f, string code) =>
            f.Any(x => x.Status == "FAIL" && x.Code == code);

        [Fact]
        public void ShippedLimitsConvertFromInchesWaterColumn()
        {
            Assert.Equal(4.98, UspCascade.MinPa(Buffer797));
            Assert.Null(UspCascade.MaxPa(Buffer797));
            Assert.Equal(2.49, UspCascade.MinPa(Csec800));
            Assert.Equal(7.47, UspCascade.MaxPa(Csec800));
            Assert.Equal("POS", Buffer797.Polarity);
            Assert.Equal("NEG", Csec800.Polarity);
            Assert.All(Shipped().Rooms, r => Assert.False(string.IsNullOrWhiteSpace(r.Verify)));
        }

        [Fact]
        public void Buffer797Needs498Pa()
        {
            Assert.True(Fails(UspCascade.Check(Buffer797, "POS", 4.9, 30), "USP.DP"));
            Assert.False(Fails(UspCascade.Check(Buffer797, "POS", 5.0, 30), "USP.DP"));
            // The old single 2.5 Pa threshold passed this room.
            Assert.True(Fails(UspCascade.Check(Buffer797, "POS", 3.0, 30), "USP.DP"));
        }

        [Fact]
        public void Csec800IsBoundedBothWays()
        {
            Assert.True(Fails(UspCascade.Check(Csec800, "NEG", -2.4, 30), "USP.DP"));
            Assert.False(Fails(UspCascade.Check(Csec800, "NEG", -5.0, 30), "USP.DP"));
            Assert.True(Fails(UspCascade.Check(Csec800, "NEG", -7.6, 30), "USP.DP"));
        }

        [Fact]
        public void WrongPolarityAndLowAchFail()
        {
            Assert.True(Fails(UspCascade.Check(Csec800, "POS", -5.0, 30), "USP.POL"));
            Assert.True(Fails(UspCascade.Check(Buffer797, "POS", 5.0, 20), "USP.ACH"));
        }

        [Fact]
        public void MissingDifferentialIsNotCheckedNotPassed()
        {
            var f = UspCascade.Check(Buffer797, "POS", null, null);
            Assert.Contains(f, x => x.Status == "NOT CHECKED" && x.Code == "USP.DP");
            Assert.Contains(f, x => x.Status == "NOT CHECKED" && x.Code == "USP.ACH");
        }

        [Fact]
        public void OverrideCanTightenButNotRelax()
        {
            // tighter: 6 Pa required, 5 Pa now fails
            Assert.True(Fails(UspCascade.Check(Buffer797, "POS", 5.0, 30, dpOverridePa: 6.0), "USP.DP"));
            // looser: 2.5 Pa ignored and reported, 3 Pa still fails
            var f = UspCascade.Check(Buffer797, "POS", 3.0, 30, dpOverridePa: 2.5);
            Assert.True(Fails(f, "USP.DP"));
            Assert.Contains(f, x => x.Code == "USP.OVERRIDE");
            Assert.True(Fails(UspCascade.Check(Buffer797, "POS", 5.0, 30, achOverride: 40), "USP.ACH"));
            Assert.False(Fails(UspCascade.Check(Buffer797, "POS", 5.0, 30, achOverride: 20), "USP.ACH"));
        }

        [Fact]
        public void UnknownRoomClassOrBadFileIsNotChecked()
        {
            Assert.Null(UspCascade.ForRoomClass(Shipped(), "ICU"));
            Assert.Contains(UspCascade.Check(null, "POS", 5, 30), x => x.Status == "NOT CHECKED");
            Assert.Null(UspCascade.Parse("{\"rooms\":[{\"code\":\"X\",\"polarity\":\"UP\"}]}", out var e));
            Assert.NotEmpty(e);
        }
    }
}
