// Roundtrip tests for the BCF 2.1 markup builder.
// Stable-GUID and StingClashIdentity preservation are critical: BCF importers
// (ACC / Solibri / BIMcollab) dedup topics by Topic.Guid attribute, so an
// unstable derivation lands every export as a new issue. STING also stamps
// hidden hint elements so a future BCF import can reconstitute the
// ClashRecord without a fresh detection run.
using System.Linq;
using System.Xml.Linq;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class BcfMarkupBuilderTests
    {
        private static ClashRecord MakeRecord(string identity = "abc-123",
                                              string id = "CLH-20260101-00007",
                                              string pair = "MEP_x_STR",
                                              string severity = "HIGH",
                                              string state = "Active") =>
            new ClashRecord
            {
                Identity = identity,
                Id = id,
                MatrixPairId = pair,
                Severity = severity,
                State = state,
                Tolerance = "HARD",
                VolumeMm3 = 4321,
                ElementA = new ClashElementRecord { ElementId = 100, Category = "Ducts", IfcGuid = "ifcA" },
                ElementB = new ClashElementRecord { ElementId = 200, Category = "Walls", IfcGuid = "ifcB" },
            };

        // --- DeriveStableGuid ---

        [Fact]
        public void DeriveStableGuid_IsDeterministic()
        {
            var a = MakeRecord(identity: "abc-123");
            var b = MakeRecord(identity: "abc-123");
            Assert.Equal(BcfMarkupBuilder.DeriveStableGuid(a), BcfMarkupBuilder.DeriveStableGuid(b));
        }

        [Fact]
        public void DeriveStableGuid_DiffersPerIdentity()
        {
            var a = MakeRecord(identity: "abc-123");
            var b = MakeRecord(identity: "abc-124");
            Assert.NotEqual(BcfMarkupBuilder.DeriveStableGuid(a), BcfMarkupBuilder.DeriveStableGuid(b));
        }

        [Fact]
        public void DeriveStableGuid_FallsBackToFreshGuid_WhenIdentityMissing()
        {
            var a = MakeRecord(identity: null);
            string g1 = BcfMarkupBuilder.DeriveStableGuid(a);
            string g2 = BcfMarkupBuilder.DeriveStableGuid(a);
            // Each call mints a fresh GUID; both must parse and they must differ.
            Assert.True(System.Guid.TryParse(g1, out _));
            Assert.True(System.Guid.TryParse(g2, out _));
            Assert.NotEqual(g1, g2);
        }

        // --- BuildMarkupXml ---

        [Fact]
        public void BuildMarkupXml_StampsTopicGuidAttribute()
        {
            var c = MakeRecord();
            string guid = BcfMarkupBuilder.DeriveStableGuid(c);
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(c, guid);
            Assert.Equal(guid, xdoc.Root.Element("Topic").Attribute("Guid").Value);
        }

        [Fact]
        public void BuildMarkupXml_PreservesLosslessIdentityHints()
        {
            var c = MakeRecord();
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(c, BcfMarkupBuilder.DeriveStableGuid(c));
            var topic = xdoc.Root.Element("Topic");
            Assert.Equal(c.Identity,     topic.Element("StingClashIdentity").Value);
            Assert.Equal(c.Id,           topic.Element("StingClashId").Value);
            Assert.Equal(c.MatrixPairId, topic.Element("StingMatrixPairId").Value);
            Assert.Equal(c.Severity,     topic.Element("StingSeverity").Value);
        }

        [Theory]
        [InlineData("CRITICAL", "Critical")]
        [InlineData("HIGH",     "Major")]
        [InlineData("MED",      "Normal")]
        [InlineData("MEDIUM",   "Normal")]
        [InlineData("LOW",      "Minor")]
        [InlineData("",         "Normal")]
        public void BuildMarkupXml_MapsSeverityToBcfPriority(string severity, string expected)
        {
            var c = MakeRecord(severity: severity);
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(c, BcfMarkupBuilder.DeriveStableGuid(c));
            Assert.Equal(expected, xdoc.Root.Element("Topic").Element("Priority").Value);
        }

        [Theory]
        [InlineData("New",      "Active")]
        [InlineData("Active",   "Active")]
        [InlineData("Resolved", "Closed")]
        [InlineData("Void",     "Closed")]
        public void BuildMarkupXml_MapsStateToBcfStatus(string state, string expected)
        {
            var c = MakeRecord(state: state);
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(c, BcfMarkupBuilder.DeriveStableGuid(c));
            Assert.Equal(expected, xdoc.Root.Element("Topic").Attribute("TopicStatus").Value);
        }

        // --- Roundtrip ---

        [Fact]
        public void Roundtrip_PreservesIdentityIdPairAndSeverity()
        {
            var src = MakeRecord(identity: "id-XYZ", id: "CLH-20260418-00042",
                                 pair: "STR_x_MEP", severity: "CRITICAL", state: "Active");
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(src, BcfMarkupBuilder.DeriveStableGuid(src));
            var rt = BcfMarkupBuilder.ParseMarkupXml(xdoc);

            Assert.NotNull(rt);
            Assert.Equal(src.Identity,     rt.Identity);
            Assert.Equal(src.Id,           rt.Id);
            Assert.Equal(src.MatrixPairId, rt.MatrixPairId);
            Assert.Equal(src.Severity,     rt.Severity);
            Assert.Equal("Active",         rt.State);
        }

        [Fact]
        public void Roundtrip_ResolvedClashRoundtripsAsClosed()
        {
            var src = MakeRecord(state: "Resolved");
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(src, BcfMarkupBuilder.DeriveStableGuid(src));
            var rt = BcfMarkupBuilder.ParseMarkupXml(xdoc);
            Assert.Equal("Resolved", rt.State);
        }

        [Fact]
        public void BuildMarkupXml_AlwaysIncludesClashLabel()
        {
            var c = MakeRecord();
            var xdoc = BcfMarkupBuilder.BuildMarkupXml(c, BcfMarkupBuilder.DeriveStableGuid(c));
            var labels = xdoc.Root.Element("Topic").Element("Labels").Elements("Label").Select(e => e.Value).ToArray();
            Assert.Contains("clash", labels);
            Assert.Contains(c.MatrixPairId, labels);
        }
    }
}
