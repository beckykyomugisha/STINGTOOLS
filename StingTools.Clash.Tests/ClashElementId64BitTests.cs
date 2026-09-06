// ClashElementId64BitTests.cs — issue #722.
//
// Revit 2024+ ElementId.Value is Int64 and a large or long-lived model mints
// ids past int.MaxValue. The clash engine's identity (ClashElementKey) held
// them as int, so every such id was truncated the moment it entered the clash
// tree — and the truncation is not merely lossy, it COLLIDES: 2^32 + 7 and 7
// both narrow to 7.
//
// That leaked into federation. GeometrySyncHandler builds a ClashElementKey per
// changed element, GlbSerializer stamps that key's id into the GLB node extras,
// and the server reads it with GetInt64 and keys FederatedElement on it. A
// >int.MaxValue element was therefore STORED under a truncated id, while the
// delete that followed (widened to 64-bit in #684) carried the real one — the
// tombstone missed and stale geometry lingered in the federated scene.
//
// These tests pin the plugin half of that chain: the key preserves the id, the
// persisted record round-trips it, the history/grouping keys don't collide on
// it, and the GLB the plugin writes hands the full value to the reader the
// server actually uses. The server half (extras → row → delete match) is pinned
// by FederatedDeltaReliabilityTests
// .A_deleted_id_beyond_int_range_round_trips_through_the_wire.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using StingTools.Commands.IFC;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashElementId64BitTests
    {
        // 4,294,967,303 — narrows to 7 as int32, which is the collision that
        // makes the truncation a correctness bug and not just a capacity limit.
        private const long BigId = (1L << 32) + 7L;
        private const long SmallTwin = 7L;

        // ── identity ────────────────────────────────────────────────────────

        [Fact]
        public void ElementKey_Keeps_The_Full_64_Bit_Id()
        {
            var key = new ClashElementKey("doc", -1, BigId, "uid", "ifc");

            Assert.Equal(BigId, key.ElementId);
            Assert.Equal("doc:-1:" + BigId, key.ToString());
        }

        [Fact]
        public void ElementKey_Distinguishes_Ids_That_Differ_Only_Above_Bit_31()
        {
            var big = new ClashElementKey("doc", -1, BigId, "uid-big", "ifc-big");
            var small = new ClashElementKey("doc", -1, SmallTwin, "uid-small", "ifc-small");

            Assert.NotEqual(big, small);
            Assert.False(big.Equals(small));
            // Not a contract of Equals, but the whole point of the key: these
            // two must not share a dictionary bucket, or every _meshByEid /
            // _obbByEid / sweep lookup for one answers with the other's mesh.
            Assert.NotEqual(big.GetHashCode(), small.GetHashCode());
        }

        [Fact]
        public void ElementKey_Distinguishes_Link_Instances_Above_Bit_31()
        {
            var a = new ClashElementKey("doc", (1L << 32) + 3L, 100, "uid", "ifc");
            var b = new ClashElementKey("doc", 3L, 100, "uid", "ifc");

            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Two_Big_Ids_Still_Key_A_Dictionary_Independently()
        {
            // The concrete failure the maps in ClashSession would have hit.
            var map = new Dictionary<ClashElementKey, string>
            {
                [new ClashElementKey("doc", -1, BigId, "uid-big", "")] = "big",
                [new ClashElementKey("doc", -1, SmallTwin, "uid-small", "")] = "small",
            };

            Assert.Equal(2, map.Count);
            Assert.Equal("big", map[new ClashElementKey("doc", -1, BigId, "", "")]);
            Assert.Equal("small", map[new ClashElementKey("doc", -1, SmallTwin, "", "")]);
        }

        // ── persistence ─────────────────────────────────────────────────────

        [Fact]
        public void Persisted_Clash_Record_Round_Trips_A_64_Bit_Id()
        {
            var run = new ClashRunRecord { RunId = "run-1" };
            run.Clashes.Add(new ClashRecord
            {
                Id = "CLH-20260817-00001",
                Identity = "abc",
                ElementA = new ClashElementRecord { ElementId = BigId, LinkInstanceId = -1, Category = "Ducts" },
                ElementB = new ClashElementRecord { ElementId = SmallTwin, LinkInstanceId = -1, Category = "Walls" },
            });

            // Save mirrors into a sibling archive/ folder, so give it its own
            // directory rather than littering %TEMP%.
            string dir = Path.Combine(Path.GetTempPath(), "sting-clash-64-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "clashes.json");
            try
            {
                ClashPersistence.Save(run, path);
                var reloaded = ClashPersistence.Load(path);

                Assert.NotNull(reloaded);
                Assert.Equal(BigId, reloaded.Clashes[0].ElementA.ElementId);
                Assert.Equal(SmallTwin, reloaded.Clashes[0].ElementB.ElementId);
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        // ── history: the fuzzy bucket must not collide ──────────────────────

        [Fact]
        public void History_Does_Not_Fuzzy_Match_Across_A_Truncation_Collision()
        {
            // Prior run: a clash on the >int.MaxValue element.
            // Current run: a DIFFERENT clash on the element whose id is that
            // one's low 32 bits. Same matrix pair, same place — so under
            // truncation they landed in the same fuzzy bucket and the new clash
            // inherited the old one's identity, state and first-seen date,
            // while the genuinely-gone clash was never reported resolved.
            var prior = new ClashRunRecord { RunId = "prior" };
            prior.Clashes.Add(new ClashRecord
            {
                Identity = "prior-identity",
                Id = "CLH-20260101-00001",
                State = "Active",
                MatrixPairId = "DUCT:STR_BEAM",
                FirstSeenUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                ElementA = new ClashElementRecord { ElementId = BigId },
                ElementB = new ClashElementRecord { ElementId = 500 },
                Centroid = new[] { 0f, 0f, 0f },
            });

            var current = new ClashRunRecord { RunId = "current" };
            current.Clashes.Add(new ClashRecord
            {
                Identity = "current-identity",
                MatrixPairId = "DUCT:STR_BEAM",
                ElementA = new ClashElementRecord { ElementId = SmallTwin },
                ElementB = new ClashElementRecord { ElementId = 500 },
                Centroid = new[] { 0f, 0f, 0f },
            });

            ClashHistory.MergeWithPrior(current, prior);

            Assert.Equal("New", current.Clashes[0].State);
            Assert.NotEqual("CLH-20260101-00001", current.Clashes[0].Id);
            Assert.Equal(1, current.Stats.New);
            // The prior clash really is gone and must be reported as such.
            Assert.Equal(1, current.Stats.Resolved);
        }

        // ── grouping: the pattern key must not collide ──────────────────────

        [Fact]
        public void Grouper_Does_Not_Merge_Two_Elements_That_Share_Low_32_Bits()
        {
            // Two distinct ducts, each clashing with two beams. Neither reaches
            // the 3-member element-pattern threshold on its own; only a
            // truncation collision could fuse them into one 4-member group.
            var clashes = new List<ClashRecord>();
            for (int i = 0; i < 2; i++)
            {
                clashes.Add(NewPatternClash($"big-{i}", BigId, 900 + i, i * 40f));
                clashes.Add(NewPatternClash($"small-{i}", SmallTwin, 950 + i, 500f + i * 40f));
            }

            var groups = ClashGrouper.Group(clashes);

            Assert.DoesNotContain(groups, g => g.Kind == "element");
        }

        private static ClashRecord NewPatternClash(string identity, long anchorId, long otherId, float x) =>
            new ClashRecord
            {
                Identity = identity,
                MatrixPairId = "DUCT:STR_BEAM",
                VolumeMm3 = 1000f,
                ElementA = new ClashElementRecord { ElementId = anchorId, Category = "Ducts" },
                ElementB = new ClashElementRecord { ElementId = otherId, Category = "Structural Framing" },
                Centroid = new[] { x, 0f, 0f },
                AabbMin = new[] { x, 0f, 0f },
                AabbMax = new[] { x + 1f, 1f, 1f },
            };

        // ── the wire: what the plugin writes is what the server reads ───────

        [Fact]
        public void Glb_Node_Extras_Carry_The_Full_64_Bit_Element_Id()
        {
            // This is the federation leak itself: GlbSerializer.GltfExtras held
            // elementId as int, so the value the server stored on add came from
            // a narrowed field while the delete it later matched against was a
            // full 64-bit id.
            var buf = new ClashMeshBuffer(
                new ClashElementKey("doc-guid", -1, BigId, "uid-big", "ifc-big"),
                "Walls",
                new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
                new[] { 0, 1, 2 });

            byte[] glb = GlbSerializer.Serialize(new[] { buf });
            Assert.NotEmpty(glb);

            var extras = ReadNodeExtrasTheWayTheServerDoes(glb);

            var one = Assert.Single(extras);
            Assert.Equal(BigId, one.ElementId);
            Assert.Equal("uid-big", one.UniqueId);
        }

        [Fact]
        public void Glb_Element_Id_Is_Serialized_As_A_Json_Number_Not_A_String()
        {
            // GetInt64 on the server throws on a quoted number, which would turn
            // the widening into a different silent failure (a caught parse error
            // and zero nodes). Assert the literal digits are unquoted.
            var buf = new ClashMeshBuffer(
                new ClashElementKey("doc-guid", -1, BigId, "uid-big", null),
                "Walls",
                new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
                new[] { 0, 1, 2 });

            string json = JsonChunk(GlbSerializer.Serialize(new[] { buf }));

            Assert.Contains("\"elementId\":" + BigId, json);
            Assert.DoesNotContain("\"elementId\":\"", json);
        }

        /// <summary>
        /// Mirror of FederatedModelController.ParseGlbNodeExtras — same offsets,
        /// same GetInt64 read. If the plugin's GLB is readable here it is
        /// readable there, which is what makes the two halves one chain.
        /// </summary>
        private static List<(string UniqueId, long ElementId)> ReadNodeExtrasTheWayTheServerDoes(byte[] glbBytes)
        {
            var result = new List<(string, long)>();
            int jsonLength = BitConverter.ToInt32(glbBytes, 12);
            string json = Encoding.UTF8.GetString(glbBytes, 20, jsonLength).TrimEnd('\0', ' ');

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes)) return result;
            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("extras", out var extras)) continue;
                result.Add((
                    extras.TryGetProperty("uniqueId", out var uid) ? uid.GetString() ?? "" : "",
                    extras.TryGetProperty("elementId", out var eid) ? eid.GetInt64() : 0));
            }
            return result;
        }

        private static string JsonChunk(byte[] glbBytes)
        {
            int jsonLength = BitConverter.ToInt32(glbBytes, 12);
            return Encoding.UTF8.GetString(glbBytes, 20, jsonLength).TrimEnd('\0', ' ');
        }
    }
}
