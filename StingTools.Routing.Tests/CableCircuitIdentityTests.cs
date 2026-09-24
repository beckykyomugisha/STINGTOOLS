// CableCircuitIdentityTests — ELC-7: a manifest cable must resolve to the
// right Revit circuit. The old consumers compared StingCable.CircuitId with
// RBS_ELEC_CIRCUIT_NUMBER; AddCable writes "<source>-<destId>", so nothing
// ever matched and auto-route routed 0 cables. Circuit numbers also repeat
// on every panel.

using System.Collections.Generic;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Routing.Tests
{
    public class CableCircuitIdentityTests
    {
        // Two panels, both with a circuit "1" — the collision the old match fell into.
        private static List<CircuitCandidate> Model() => new List<CircuitCandidate>
        {
            new CircuitCandidate
            {
                SystemId = 1001, CircuitNumber = "1",
                PanelNames = new List<string> { "DB-A", "Distribution Board", "Distribution_Board" },
                BaseEquipmentUniqueId = "uid-panel-A",
                MemberUniqueIds = new List<string> { "uid-light-1" }, MemberElementIds = new List<long> { 500001 },
            },
            new CircuitCandidate
            {
                SystemId = 1002, CircuitNumber = "1",
                PanelNames = new List<string> { "DB-B" },
                BaseEquipmentUniqueId = "uid-panel-B",
                MemberUniqueIds = new List<string> { "uid-socket-9" }, MemberElementIds = new List<long> { 500009 },
            },
            new CircuitCandidate
            {
                SystemId = 1003, CircuitNumber = "7",
                PanelNames = new List<string> { "DB-B" },
                BaseEquipmentUniqueId = "uid-panel-B",
                MemberUniqueIds = new List<string> { "uid-hob" }, MemberElementIds = new List<long> { 500020 },
            },
        };

        // ── legacy CircuitId format ─────────────────────────────────────

        [Fact]
        public void BuildLegacyCircuitId_MatchesWhatAddCableAlwaysWrote()
        {
            Assert.Equal("Distribution_Board-500001",
                CableCircuitIdentity.BuildLegacyCircuitId("Distribution Board", 500001));
        }

        [Theory]
        [InlineData("Distribution_Board-500001", 500001)]
        [InlineData("MCB-DB-A-123456", 123456)]          // '-' inside the source name
        [InlineData("DB 1-42", 42)]
        public void TryParseLegacyDestinationId_ParsesTrailingElementId(string id, long expected)
        {
            Assert.True(CableCircuitIdentity.TryParseLegacyDestinationId(id, out long got));
            Assert.Equal(expected, got);
        }

        [Theory]
        [InlineData("12")]        // a circuit number
        [InlineData("1-3")]       // a multi-pole circuit number, not <name>-<id>
        [InlineData("1,3-5")]
        [InlineData("DB-A")]      // no numeric tail
        [InlineData("-123")]      // no source part
        [InlineData("DB-")]
        [InlineData("")]
        [InlineData(null)]
        public void TryParseLegacyDestinationId_RejectsNonLegacyShapes(string id)
        {
            Assert.False(CableCircuitIdentity.TryParseLegacyDestinationId(id, out _));
        }

        // ── resolution ──────────────────────────────────────────────────

        [Fact]
        public void Resolve_AddCableRecord_MatchesBySourceAndDestination()
        {
            // Exactly what AddCableCommand writes. Under the old circuit-number
            // match this cable resolved to nothing.
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            {
                CircuitId = "Distribution_Board-500001", PanelName = "Distribution Board",
                SourceUniqueId = "uid-panel-A", DestUniqueId = "uid-light-1",
            }, Model());
            Assert.True(m.Found, m.Reason);
            Assert.Equal(1001, m.SystemId);
            Assert.Equal(CircuitMatchMethod.SourceAndDestination, m.Method);
        }

        [Fact]
        public void Resolve_OldManifestWithOnlyLegacyCircuitId_UsesParsedDestination()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { CircuitId = "DB-B-500020" }, Model());
            Assert.True(m.Found, m.Reason);
            Assert.Equal(1003, m.SystemId);
            Assert.Equal(CircuitMatchMethod.DestinationOnly, m.Method);
        }

        [Fact]
        public void Resolve_StoredElementId_WinsOverEverythingElse()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { CircuitElementId = 1002, CircuitId = "Distribution_Board-500001" }, Model());
            Assert.Equal(1002, m.SystemId);
            Assert.Equal(CircuitMatchMethod.StoredElementId, m.Method);
        }

        [Fact]
        public void Resolve_StaleStoredElementId_FallsBackToStructuralKeys()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { CircuitElementId = 999999, DestUniqueId = "uid-hob" }, Model());
            Assert.True(m.Found, m.Reason);
            Assert.Equal(1003, m.SystemId);
        }

        [Fact]
        public void Resolve_BareCircuitNumberRepeatedAcrossPanels_IsAmbiguousNotFirstHit()
        {
            // The old code returned whichever panel's circuit "1" the
            // collector yielded first.
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey { CircuitId = "1" }, Model());
            Assert.False(m.Found);
            Assert.True(m.Ambiguous);
            Assert.Contains("2 circuits", m.Reason);
        }

        [Fact]
        public void Resolve_CircuitNumberWithPanel_PicksThatPanel()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { CircuitId = "1", PanelName = "db-b" }, Model());
            Assert.True(m.Found, m.Reason);
            Assert.Equal(1002, m.SystemId);
            Assert.Equal(CircuitMatchMethod.PanelAndCircuitNumber, m.Method);
        }

        [Fact]
        public void Resolve_UniqueCircuitNumberWithoutPanel_StillResolves()
        {
            // Backward compatibility with hand-written, number-keyed manifests.
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey { CircuitId = "7" }, Model());
            Assert.True(m.Found, m.Reason);
            Assert.Equal(1003, m.SystemId);
            Assert.Equal(CircuitMatchMethod.CircuitNumberOnly, m.Method);
        }

        [Fact]
        public void Resolve_CircuitNumberOnWrongPanel_MissesWithReason()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { CircuitId = "7", PanelName = "DB-A" }, Model());
            Assert.False(m.Found);
            Assert.Contains("not on panel", m.Reason);
        }

        [Fact]
        public void Resolve_DestinationFedFromDifferentSource_DoesNotSilentlyMatch()
        {
            // Re-circuited since the cable was recorded: the destination now
            // hangs off panel B, but the manifest says panel A.
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey
            { SourceUniqueId = "uid-panel-A", DestUniqueId = "uid-socket-9", CircuitId = "X-500009" }, Model());
            Assert.False(m.Found);
            Assert.False(m.Ambiguous);
            Assert.Contains("source", m.Reason);
        }

        [Fact]
        public void Resolve_DestinationOnTwoCircuitsWithoutSource_IsAmbiguous()
        {
            var model = Model();
            model[1].MemberUniqueIds.Add("uid-light-1");
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey { DestUniqueId = "uid-light-1" }, model);
            Assert.True(m.Ambiguous);
            Assert.False(m.Found);
        }

        [Fact]
        public void Resolve_LegacyIdWhoseDestinationIsOnNoCircuit_Misses()
        {
            var m = CableCircuitIdentity.Resolve(new CableIdentityKey { CircuitId = "DB-A-777" }, Model());
            Assert.False(m.Found);
            Assert.Contains("not on any circuit", m.Reason);
        }

        [Fact]
        public void Resolve_EmptyModelOrNullKey_MissesWithReason()
        {
            Assert.Contains("no electrical circuits",
                CableCircuitIdentity.Resolve(new CableIdentityKey { CircuitId = "1" }, new List<CircuitCandidate>()).Reason);
            Assert.False(CableCircuitIdentity.Resolve(null, Model()).Found);
            Assert.False(CableCircuitIdentity.Resolve(new CableIdentityKey(), Model()).Found);
        }

        [Fact]
        public void Describe_CoversEveryMethod()
        {
            foreach (CircuitMatchMethod m in System.Enum.GetValues(typeof(CircuitMatchMethod)))
                Assert.False(string.IsNullOrWhiteSpace(CableCircuitIdentity.Describe(m)));
        }
    }
}
