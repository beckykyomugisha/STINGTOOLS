// ══════════════════════════════════════════════════════════════════════════
//  CircuitEndpointChooserTests.cs — ELEC-9.
//
//  A conduit is never part of an ElectricalSystem; its circuit is inferred
//  from the devices and panels at the ends of its run. The rule under test:
//  the circuit touching the most endpoints wins, a device beats a bare panel
//  in a tie, and an unresolvable run says why instead of returning the first
//  circuit a panel happens to enumerate.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CircuitEndpointChooserTests
    {
        private static CircuitCandidate C(long id, long panel, params long[] members)
            => new CircuitCandidate { CircuitId = id, BaseEquipmentId = panel, MemberIds = new HashSet<long>(members) };

        private static CircuitEndpoint Panel(long id, params CircuitCandidate[] circuits)
            => new CircuitEndpoint { ElementId = id, IsPanel = true, Circuits = new List<CircuitCandidate>(circuits) };

        private static CircuitEndpoint Device(long id, params CircuitCandidate[] circuits)
            => new CircuitEndpoint { ElementId = id, IsPanel = false, Circuits = new List<CircuitCandidate>(circuits) };

        [Fact]
        public void Run_from_panel_to_socket_takes_the_sockets_circuit_not_the_panels_first()
        {
            // DB1 (id 100) feeds circuits 1..3; the socket (id 500) is on circuit 2.
            var c1 = C(1, 100, 400);
            var c2 = C(2, 100, 500);
            var c3 = C(3, 100, 600);
            var eps = new List<CircuitEndpoint>
            {
                Panel(100, c1, c2, c3),     // enumerates c1 first — the old bug's answer
                Device(500, c2),
            };
            var r = CircuitEndpointChooser.Choose(eps);
            Assert.True(r.Found);
            Assert.Equal(2, r.CircuitId);
        }

        [Fact]
        public void Run_between_two_panels_is_the_feeder()
        {
            var feeder = C(10, 100, 200);            // DB1 feeds DB2
            var db2Own = C(11, 200, 700);
            var db1Other = C(12, 100, 800);
            var eps = new List<CircuitEndpoint>
            {
                Panel(100, feeder, db1Other),
                Panel(200, feeder, db2Own),
            };
            var r = CircuitEndpointChooser.Choose(eps);
            Assert.Equal(10, r.CircuitId);
        }

        [Fact]
        public void Run_between_two_devices_on_one_circuit_resolves()
        {
            var c = C(7, 100, 501, 502);
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint> { Device(501, c), Device(502, c) });
            Assert.Equal(7, r.CircuitId);
        }

        [Fact]
        public void Device_with_the_other_end_open_resolves_to_its_circuit()
        {
            var c = C(4, 100, 501);
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint> { Device(501, c) });
            Assert.Equal(4, r.CircuitId);
        }

        [Fact]
        public void Tie_between_panel_circuits_and_a_device_circuit_prefers_the_device()
        {
            // Socket on a circuit from a DIFFERENT panel: every candidate touches one end.
            var fromDb2 = C(20, 200, 501);
            var eps = new List<CircuitEndpoint>
            {
                Panel(100, C(1, 100, 400), C(2, 100, 401)),
                Device(501, fromDb2),
            };
            Assert.Equal(20, CircuitEndpointChooser.Choose(eps).CircuitId);
        }

        [Fact]
        public void Panel_alone_with_several_circuits_is_ambiguous_and_says_so()
        {
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint>
            {
                Panel(100, C(1, 100, 400), C(2, 100, 401), C(3, 100, 402)),
            });
            Assert.False(r.Found);
            Assert.Contains("only a panel", r.Reason);
            Assert.Contains("3 circuits", r.Reason);
        }

        [Fact]
        public void Panel_alone_with_one_circuit_resolves()
        {
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint> { Panel(100, C(1, 100, 400)) });
            Assert.Equal(1, r.CircuitId);
        }

        [Fact]
        public void Shared_containment_is_reported_not_guessed()
        {
            // One conduit run feeding two sockets on two circuits from the same board.
            var a = C(1, 100, 501);
            var b = C(2, 100, 502);
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint>
            {
                Panel(100, a, b), Device(501, a), Device(502, b),
            });
            Assert.False(r.Found);
            Assert.Contains("shared by 2 circuits", r.Reason);
        }

        [Fact]
        public void No_endpoints_is_a_reason_not_a_null()
        {
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint>());
            Assert.False(r.Found);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
            Assert.False(CircuitEndpointChooser.Choose(null).Found);
        }

        [Fact]
        public void Endpoints_with_no_circuits_explain_themselves()
        {
            var r = CircuitEndpointChooser.Choose(new List<CircuitEndpoint> { Device(501), Panel(100) });
            Assert.False(r.Found);
            Assert.Contains("none of which is on a circuit", r.Reason);
        }
    }
}
