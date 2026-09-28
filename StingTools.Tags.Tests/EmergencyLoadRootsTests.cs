// ══════════════════════════════════════════════════════════════════════════
//  EmergencyLoadRootsTests.cs — the dual-source generator check sums only
//  emergency panels with no emergency panel upstream.
//
//  EM-DB1 (10 kVA of its own loads) feeds EM-DB2 (4 kVA). EM-DB1's feeder
//  circuit to EM-DB2 already reads 4 kVA, so summing both boards counted
//  EM-DB2 twice (18 kVA instead of 14) and could fail an adequate generator.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class EmergencyLoadRootsTests
    {
        [Fact]
        public void EmergencySubPanel_IsNotARoot()
        {
            // 1 = MDB (normal), 2 = EM-DB1 fed from MDB, 3 = EM-DB2 fed from EM-DB1
            var supply = new Dictionary<long, long> { [2] = 1, [3] = 2 };
            var roots = EmergencyLoadRoots.Roots(new long[] { 2, 3 }, supply);
            Assert.Equal(new HashSet<long> { 2 }, roots);
        }

        [Fact]
        public void EmergencyPanelBehindNormalBoard_BelowEmergencyPanel_IsNotARoot()
        {
            // EM-DB1 (2) → ordinary board (4) → EM-DB3 (5): 5's load already flows
            // through 2's feeder to 4.
            var supply = new Dictionary<long, long> { [2] = 1, [4] = 2, [5] = 4 };
            var roots = EmergencyLoadRoots.Roots(new long[] { 2, 5 }, supply);
            Assert.Equal(new HashSet<long> { 2 }, roots);
        }

        [Fact]
        public void IndependentEmergencyPanels_AreAllRoots()
        {
            var supply = new Dictionary<long, long> { [2] = 1, [3] = 1 };
            var roots = EmergencyLoadRoots.Roots(new long[] { 2, 3, 9 }, supply);   // 9: supply unknown
            Assert.Equal(new HashSet<long> { 2, 3, 9 }, roots);
        }

        [Fact]
        public void CyclicFeedOutsideTheSet_Terminates()
        {
            var supply = new Dictionary<long, long> { [2] = 7, [7] = 8, [8] = 7 };
            var roots = EmergencyLoadRoots.Roots(new long[] { 2 }, supply);
            Assert.Equal(new HashSet<long> { 2 }, roots);
        }
    }
}
