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

        [Fact]
        public void H8_Negative_Coordinates_Remain_Deterministic()
        {
            // Buildings set far from the project origin — or linked-model
            // elements in a federated project with opposing coordinate
            // conventions — routinely produce negative centroid components.
            // The quantization math (int)Math.Round(val * 304.8 / 250.0)
            // must handle negatives without throwing or producing non-
            // deterministic output. Regression guard.
            var a = new ClashElementKey("doc", -1, 1, "uid-a", "ifc-a");
            var b = new ClashElementKey("doc", -1, 2, "uid-b", "ifc-b");
            var negCentroid = new Vector3(-123.45f, -67.89f, -5.5f);

            var id1 = ClashIdentity.Compute(a, b, "PAIR", negCentroid);
            var id2 = ClashIdentity.Compute(a, b, "PAIR", negCentroid);
            Assert.Equal(id1, id2);
            Assert.Equal(16, id1.Length);   // still widened (rec-17)

            // Two very different negative centroids must produce different ids
            // (no sign-flip collapse).
            var farAway = new Vector3(-500f, -500f, -500f);
            var idFar = ClashIdentity.Compute(a, b, "PAIR", farAway);
            Assert.NotEqual(id1, idFar);
        }

        [Fact]
        public void H8_Mixed_Sign_Coordinates_Deterministic()
        {
            // Very common case: one axis across a project origin (e.g. building
            // spans from -50 ft to +50 ft in X, but is all positive in Y/Z).
            // Check that the hash for the same mixed-sign centroid is stable.
            var a = new ClashElementKey("doc", -1, 1, "", "");
            var b = new ClashElementKey("doc", -1, 2, "", "");
            var mixed = new Vector3(-15.7f, 42.3f, -0.01f);
            Assert.Equal(
                ClashIdentity.Compute(a, b, "PAIR", mixed),
                ClashIdentity.Compute(a, b, "PAIR", mixed));
        }

        [Fact]
        public void H8_Symmetric_Positive_And_Negative_Centroids_Are_Distinct()
        {
            // Sign matters: (+10, 0, 0) and (-10, 0, 0) must NOT hash to the
            // same identity. Guards against a future quantizer refactor that
            // uses Math.Abs() and accidentally erases sign.
            var a = new ClashElementKey("doc", -1, 1, "", "");
            var b = new ClashElementKey("doc", -1, 2, "", "");
            var pos = new Vector3(10f, 10f, 10f);
            var neg = new Vector3(-10f, -10f, -10f);
            Assert.NotEqual(
                ClashIdentity.Compute(a, b, "PAIR", pos),
                ClashIdentity.Compute(a, b, "PAIR", neg));
        }
    }
}
