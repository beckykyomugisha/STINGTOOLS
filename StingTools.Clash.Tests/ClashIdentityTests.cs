using System.Numerics;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashIdentityTests
    {
        [Fact]
        public void Compute_Is_Deterministic_For_Same_Inputs()
        {
            var a = new ClashElementKey("doc", -1, 100, "uid-a", "ifc-a");
            var b = new ClashElementKey("doc", -1, 200, "uid-b", "ifc-b");
            var centroid = new Vector3(1.0f, 2.0f, 3.0f);

            var id1 = ClashIdentity.Compute(a, b, "PIPE:WALL", centroid);
            var id2 = ClashIdentity.Compute(a, b, "PIPE:WALL", centroid);
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void Compute_Is_Order_Independent()
        {
            var a = new ClashElementKey("doc", -1, 100, "uid-a", "ifc-a");
            var b = new ClashElementKey("doc", -1, 200, "uid-b", "ifc-b");
            var centroid = new Vector3(1, 2, 3);

            var id1 = ClashIdentity.Compute(a, b, "PIPE:WALL", centroid);
            var id2 = ClashIdentity.Compute(b, a, "PIPE:WALL", centroid);
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void Hash_Is_16_Hex_Chars_After_Widening()
        {
            // rec-17: widened from 8 → 16 hex chars.
            var a = new ClashElementKey("doc", -1, 1, "", "");
            var b = new ClashElementKey("doc", -1, 2, "", "");
            var hash = ClashIdentity.Compute(a, b, "PAIR", new Vector3(0, 0, 0));
            Assert.Equal(16, hash.Length);
            Assert.Matches("^[0-9a-f]{16}$", hash);
        }

        [Fact]
        public void Centroid_Jitter_Within_250mm_Matches()
        {
            // 250mm in feet = 250 / 304.8 ≈ 0.82 ft.
            var a = new ClashElementKey("doc", -1, 1, "", "");
            var b = new ClashElementKey("doc", -1, 2, "", "");
            var p1 = new Vector3(10f, 10f, 10f);
            // Move by 100mm (0.328 ft) — within the quantization bin.
            var p2 = new Vector3(10f + 0.1f, 10f, 10f);

            var id1 = ClashIdentity.Compute(a, b, "PAIR", p1);
            var id2 = ClashIdentity.Compute(a, b, "PAIR", p2);
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void Centroid_Jitter_Over_250mm_Differs()
        {
            var a = new ClashElementKey("doc", -1, 1, "", "");
            var b = new ClashElementKey("doc", -1, 2, "", "");
            var p1 = new Vector3(10f, 10f, 10f);
            // Move by 500mm (1.64 ft) — past the 250mm quantization bin.
            var p2 = new Vector3(10f + 1.64f, 10f, 10f);

            var id1 = ClashIdentity.Compute(a, b, "PAIR", p1);
            var id2 = ClashIdentity.Compute(a, b, "PAIR", p2);
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void NewClashId_Has_Expected_Format()
        {
            var id = ClashIdentity.NewClashId(new System.DateTime(2026, 4, 18), 42);
            Assert.Equal("CLH-20260418-00042", id);
        }
    }
}
