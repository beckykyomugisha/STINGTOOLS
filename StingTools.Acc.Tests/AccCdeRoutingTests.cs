// Tests for ACC CDE-folder routing and the STING metadata attribute set.
//
// The load-bearing assertions are the refusals: a document whose suitability is unknown,
// or whose CDE state has no configured folder, gets NO folder — never a plausible one.
// PUBLISHED is a contractual statement; a fallback folder would make it by accident.

using System.Collections.Generic;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccCdeRoutingTests
    {
        private static readonly Dictionary<string, string> AllFour = new Dictionary<string, string>
        {
            ["WIP"]       = "urn:adsk.wipprod:fs.folder:co.wip",
            ["SHARED"]    = "urn:adsk.wipprod:fs.folder:co.shared",
            ["PUBLISHED"] = "urn:adsk.wipprod:fs.folder:co.published",
            ["ARCHIVE"]   = "urn:adsk.wipprod:fs.folder:co.archive",
        };

        [Theory]
        [InlineData("S0", "WIP", "urn:adsk.wipprod:fs.folder:co.wip")]
        [InlineData("S1", "SHARED", "urn:adsk.wipprod:fs.folder:co.shared")]
        [InlineData("S4 - FOR APROVAL", "SHARED", "urn:adsk.wipprod:fs.folder:co.shared")]
        [InlineData("s7", "SHARED", "urn:adsk.wipprod:fs.folder:co.shared")]
        [InlineData("A1", "PUBLISHED", "urn:adsk.wipprod:fs.folder:co.published")]
        [InlineData("B2", "PUBLISHED", "urn:adsk.wipprod:fs.folder:co.published")]
        [InlineData("CR", "PUBLISHED", "urn:adsk.wipprod:fs.folder:co.published")]
        [InlineData("AB", "ARCHIVE", "urn:adsk.wipprod:fs.folder:co.archive")]
        [InlineData("AR", "ARCHIVE", "urn:adsk.wipprod:fs.folder:co.archive")]
        public void EachSuitability_RoutesToItsStatesFolder(string suitability, string state, string urn)
        {
            var r = AccCdeRouting.Resolve(suitability, AllFour);
            Assert.True(r.IsRouted, r.Detail);
            Assert.Equal(state, r.CdeState);
            Assert.Equal(urn, r.FolderUrn);
        }

        [Fact]
        public void MapKeys_AreCaseInsensitive()
        {
            var map = new Dictionary<string, string> { ["published"] = "urn:adsk.wipemea:fs.folder:co.p" };
            var r = AccCdeRouting.Resolve("A2", map);
            Assert.True(r.IsRouted);
            Assert.Equal("urn:adsk.wipemea:fs.folder:co.p", r.FolderUrn);
        }

        [Fact]
        public void MissingMapping_IsNotConfigured_NamingTheState_AndNoFallbackFolder()
        {
            var map = new Dictionary<string, string>(AllFour);
            map.Remove("PUBLISHED");
            var r = AccCdeRouting.Resolve("A1", map);

            Assert.False(r.IsRouted);
            Assert.Equal(AccCdeRouteStatus.StateNotConfigured, r.Status);
            Assert.Equal("PUBLISHED", r.CdeState);
            Assert.Equal(string.Empty, r.FolderUrn);             // not the SHARED or WIP folder
            Assert.Contains("PUBLISHED", r.Detail);
            Assert.Contains("cdeFolders", r.Detail);
        }

        [Fact]
        public void BlankMapping_AndNullMap_AreNotConfigured()
        {
            var blank = new Dictionary<string, string> { ["WIP"] = "  " };
            Assert.Equal(AccCdeRouteStatus.StateNotConfigured, AccCdeRouting.Resolve("S0", blank).Status);
            Assert.Equal(AccCdeRouteStatus.StateNotConfigured, AccCdeRouting.Resolve("S0", null).Status);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("FOR INFO")]
        [InlineData("S9")]
        [InlineData("A0")]
        [InlineData("ARCHIVE")]
        public void UnknownSuitability_IsRefused_NotFiledSomewherePlausible(string suitability)
        {
            var r = AccCdeRouting.Resolve(suitability, AllFour);
            Assert.False(r.IsRouted);
            Assert.Equal(AccCdeRouteStatus.UnknownSuitability, r.Status);
            Assert.Equal(string.Empty, r.FolderUrn);
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        }

        [Theory]
        [InlineData("Project Files/03 Published")]
        [InlineData("https://acc.autodesk.com/docs/files/projects/x?folderUrn=urn:adsk.wipprod:fs.folder:co.p")]
        [InlineData("urn:adsk.wipprod:fs.file:vf.notAFolder")]
        public void AValueThatIsNotAFolderUrn_IsMalformed_NotUsed(string value)
        {
            var map = new Dictionary<string, string> { ["SHARED"] = value };
            var r = AccCdeRouting.Resolve("S3", map);
            Assert.Equal(AccCdeRouteStatus.MalformedFolderUrn, r.Status);
            Assert.Equal(string.Empty, r.FolderUrn);
        }

        [Fact]
        public void UnconfiguredStates_NamesEveryGap()
        {
            var map = new Dictionary<string, string> { ["WIP"] = "urn:adsk.wipprod:fs.folder:co.w" };
            Assert.Equal(new[] { "SHARED", "PUBLISHED", "ARCHIVE" }, AccCdeRouting.UnconfiguredStates(map));
        }

        // ── attribute values ────────────────────────────────────────────────

        [Fact]
        public void Build_DerivesCdeStateFromSuitability_AndCarriesEveryField()
        {
            var v = AccDocsAttributeSet.Build(new AccDocMetadataInput
            {
                DocumentNumber = "KUT-PLN-ZZ-01-DR-A-0001",
                Suitability = "S4 - FOR APPROVAL",
                Revision = "P02",
                TransmittalId = "TR-0007",
                Originator = "PLN",
            });

            Assert.True(v.IsClean, string.Join("; ", v.Problems));
            Assert.Equal("KUT-PLN-ZZ-01-DR-A-0001", v.Values[AccDocsAttributeSet.DocumentNumber]);
            Assert.Equal("S4", v.Values[AccDocsAttributeSet.Suitability]);
            Assert.Equal("SHARED", v.Values[AccDocsAttributeSet.CdeState]);
            Assert.Equal("P02", v.Values[AccDocsAttributeSet.Revision]);
            Assert.Equal("TR-0007", v.Values[AccDocsAttributeSet.TransmittalId]);
            Assert.Equal("PLN", v.Values[AccDocsAttributeSet.Originator]);
            Assert.Empty(v.Omitted);
        }

        [Fact]
        public void Build_UnknownSuitability_StampsNeitherSuitabilityNorState_AndSaysSo()
        {
            var v = AccDocsAttributeSet.Build(new AccDocMetadataInput { DocumentNumber = "X-1", Suitability = "FOR INFO" });
            Assert.False(v.IsClean);
            Assert.False(v.Values.ContainsKey(AccDocsAttributeSet.Suitability));
            Assert.False(v.Values.ContainsKey(AccDocsAttributeSet.CdeState));
            Assert.Contains(AccDocsAttributeSet.CdeState, v.Omitted);
        }

        [Fact]
        public void Build_BlankOptionalFields_AreOmitted_NotWrittenEmpty()
        {
            var v = AccDocsAttributeSet.Build(new AccDocMetadataInput { DocumentNumber = "X-1", Suitability = "A1", Revision = "C01" });
            Assert.True(v.IsClean);
            Assert.False(v.Values.ContainsKey(AccDocsAttributeSet.TransmittalId));
            Assert.Contains(AccDocsAttributeSet.TransmittalId, v.Omitted);
            Assert.Contains(AccDocsAttributeSet.Originator, v.Omitted);
            Assert.DoesNotContain(v.Values.Values, s => s.Length == 0);
        }

        [Fact]
        public void Build_OverLongValue_IsRefused_NotTruncated()
        {
            var v = AccDocsAttributeSet.Build(new AccDocMetadataInput
            { DocumentNumber = new string('D', 256), Suitability = "S2", Revision = "P01" });
            Assert.False(v.IsClean);
            Assert.False(v.Values.ContainsKey(AccDocsAttributeSet.DocumentNumber));
        }

        [Fact]
        public void TheAttributeSet_HasUniqueNames_AllWithinOneFolder()
        {
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var a in AccDocsAttributeSet.All) Assert.True(names.Add(a.Name), a.Name);
            Assert.Equal(6, names.Count);
        }
    }
}
