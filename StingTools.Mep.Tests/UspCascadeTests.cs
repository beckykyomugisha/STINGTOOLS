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

        // DSCH-36: ante-rooms and C-SCAs have their own room classes, so they are checked.
        [Fact]
        public void EveryShippedRowHasARoomClass()
        {
            Assert.All(Shipped().Rooms, r => Assert.False(string.IsNullOrWhiteSpace(r.RoomClass), r.Code + " has no roomClass"));
        }

        [Fact]
        public void AnteRoomsAndCscaAreChecked()
        {
            var f = Shipped();
            var ante797 = UspCascade.ForRoomClass(f, "PH-CSP-797-ANTE");
            var ante800 = UspCascade.ForRoomClass(f, "PH-CSP-800-ANTE");
            var csca    = UspCascade.ForRoomClass(f, "PH-CSP-800-CSCA");
            Assert.Equal("ANT-797", ante797?.Code);
            Assert.Equal("ANT-800", ante800?.Code);
            Assert.Equal("CSCA-800", csca?.Code);
            // C-SCA: negative 2.49–7.47 Pa, at least 12 ACPH.
            Assert.True(Fails(UspCascade.Check(csca, "NEG", -5.0, 11), "USP.ACH"));
            Assert.False(Fails(UspCascade.Check(csca, "NEG", -5.0, 12), "USP.ACH"));
            Assert.True(Fails(UspCascade.Check(csca, "NEG", -8.0, 12), "USP.DP"));
            Assert.True(Fails(UspCascade.Check(csca, "POS", -5.0, 12), "USP.POL"));
            // Ante-room: positive, at least 4.98 Pa.
            Assert.True(Fails(UspCascade.Check(ante797, "POS", 4.0, 30), "USP.DP"));
            Assert.False(Fails(UspCascade.Check(ante800, "POS", 5.0, 30), "USP.DP"));
        }

        [Fact]
        public void SuiteRowsCoverTheSelectedStandard()
        {
            var f = Shipped();
            Assert.Equal(new[] { "PH-CSP-797", "PH-CSP-797-ANTE" },
                UspCascade.SuiteRows(f, "USP-797").Select(r => r.RoomClass).ToArray());
            Assert.Equal(new[] { "PH-CSP-800", "PH-CSP-800-ANTE", "PH-CSP-800-CSCA" },
                UspCascade.SuiteRows(f, "USP-800").Select(r => r.RoomClass).ToArray());
            // every row belongs to exactly one suite
            Assert.Equal(f.Rooms.Count, UspCascade.SuiteRows(f, "USP-797").Count + UspCascade.SuiteRows(f, "USP-800").Count);
        }

        [Fact]
        public void RowWithoutRoomClassIsRefused()
        {
            Assert.Null(UspCascade.Parse("{\"rooms\":[{\"code\":\"X\",\"polarity\":\"POS\",\"minInWc\":0.02,\"achMin\":30,\"source\":\"s\"}]}", out var e));
            Assert.Contains(e, x => x.Contains("roomClass"));
        }

        // Every USP room class is offered in the COBie ClinicalRoomClass picklist, so a
        // modeller can pick it.
        [Fact]
        public void EveryUspRoomClassIsInThePicklist()
        {
            var picklist = RepoData.Read("COBIE_PICKLISTS.csv").Split('\n')
                .Select(l => l.Trim().Split(','))
                .Where(c => c.Length >= 2 && c[0] == "ClinicalRoomClass")
                .Select(c => c[1]).ToList();
            Assert.All(Shipped().Rooms, r => Assert.Contains(r.RoomClass, picklist));
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
