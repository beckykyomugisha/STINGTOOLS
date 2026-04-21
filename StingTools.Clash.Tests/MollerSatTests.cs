using System.Numerics;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class MollerSatTests
    {
        [Fact]
        public void Disjoint_Triangles_ReturnFalse()
        {
            var a0 = new Vector3(0, 0, 0); var a1 = new Vector3(1, 0, 0); var a2 = new Vector3(0, 1, 0);
            var b0 = new Vector3(5, 5, 5); var b1 = new Vector3(6, 5, 5); var b2 = new Vector3(5, 6, 5);
            Assert.False(MollerSat.TriTriOverlap(a0, a1, a2, b0, b1, b2));
        }

        [Fact]
        public void Intersecting_Triangles_ReturnTrue()
        {
            // Triangle A in the XY plane.
            var a0 = new Vector3(0, 0, 0); var a1 = new Vector3(2, 0, 0); var a2 = new Vector3(0, 2, 0);
            // Triangle B crossing perpendicular through the centroid.
            var b0 = new Vector3(0.5f, 0.5f, -1); var b1 = new Vector3(0.5f, 0.5f, 1); var b2 = new Vector3(1.5f, 0.5f, 0);
            Assert.True(MollerSat.TriTriOverlap(a0, a1, a2, b0, b1, b2));
        }

        [Fact]
        public void SharedEdge_Coplanar_ReturnsTrue()
        {
            var a0 = new Vector3(0, 0, 0); var a1 = new Vector3(1, 0, 0); var a2 = new Vector3(0, 1, 0);
            var b0 = new Vector3(1, 0, 0); var b1 = new Vector3(0, 1, 0); var b2 = new Vector3(1, 1, 0);
            Assert.True(MollerSat.TriTriOverlap(a0, a1, a2, b0, b1, b2));
        }

        [Fact]
        public void Coplanar_Separated_ReturnsFalse()
        {
            var a0 = new Vector3(0, 0, 0); var a1 = new Vector3(1, 0, 0); var a2 = new Vector3(0, 1, 0);
            var b0 = new Vector3(5, 5, 0); var b1 = new Vector3(6, 5, 0); var b2 = new Vector3(5, 6, 0);
            Assert.False(MollerSat.TriTriOverlap(a0, a1, a2, b0, b1, b2));
        }
    }
}
