// GlbSerializer.cs — converts ClashMeshBuffer[] to binary glTF 2.0 (GLB).
// No third-party library required; the GLB spec is straightforward enough to
// implement directly. Vertices are converted from Revit internal feet to metres.
// Each buffer becomes one glTF mesh + node; the node extras carry UniqueId,
// IfcGuid, elementId, and category so the web viewer can cross-reference tags.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using StingTools.Core.Clash;
using Autodesk.Revit.DB;
using System.Linq;

namespace StingTools.Commands.IFC
{
    internal static class GlbSerializer
    {
        private const float FeetToMetres = 0.3048f;

        public static byte[] Serialize(IList<ClashMeshBuffer> buffers)
        {
            if (buffers == null || buffers.Count == 0) return Array.Empty<byte>();

            // ── Build binary buffer ───────────────────────────────────────────
            using var binStream = new MemoryStream();
            using var binWriter = new BinaryWriter(binStream);

            var accessors   = new List<GltfAccessor>();
            var bufferViews = new List<GltfBufferView>();
            var meshes      = new List<GltfMesh>();
            int meshIdx     = 0;

            foreach (var buf in buffers)
            {
                if (buf.Vertices == null || buf.Indices == null) continue;
                int vertCount = buf.Vertices.Length / 3;
                int idxCount  = buf.Indices.Length;
                if (vertCount == 0 || idxCount == 0) continue;

                // POSITION — float32 × 3 per vertex, converted feet → metres
                long posStart = binWriter.BaseStream.Position;
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i < buf.Vertices.Length; i += 3)
                {
                    float x = buf.Vertices[i]     * FeetToMetres;
                    float y = buf.Vertices[i + 1] * FeetToMetres;
                    float z = buf.Vertices[i + 2] * FeetToMetres;
                    binWriter.Write(x); binWriter.Write(y); binWriter.Write(z);
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
                Pad4(binWriter);
                int posBv  = bufferViews.Count;
                int posAcc = accessors.Count;
                bufferViews.Add(new GltfBufferView { byteOffset = (int)posStart, byteLength = vertCount * 12, target = 34962 });
                accessors.Add(new GltfAccessor
                {
                    bufferView = posBv, componentType = 5126 /* FLOAT */, count = vertCount,
                    type = "VEC3", min = new[] { minX, minY, minZ }, max = new[] { maxX, maxY, maxZ }
                });

                // INDICES — uint32 per index
                long idxStart = binWriter.BaseStream.Position;
                foreach (int idx in buf.Indices) binWriter.Write((uint)idx);
                Pad4(binWriter);
                int idxBv  = bufferViews.Count;
                int idxAcc = accessors.Count;
                bufferViews.Add(new GltfBufferView { byteOffset = (int)idxStart, byteLength = idxCount * 4, target = 34963 });
                accessors.Add(new GltfAccessor
                {
                    bufferView = idxBv, componentType = 5125 /* UNSIGNED_INT */,
                    count = idxCount, type = "SCALAR"
                });

                meshes.Add(new GltfMesh
                {
                    name = buf.Key?.UniqueId ?? $"mesh_{meshIdx}",
                    primitives = new[] { new GltfPrimitive { attributes = new GltfAttr { POSITION = posAcc }, indices = idxAcc, mode = 4 } }
                });
                meshIdx++;
            }

            if (meshIdx == 0) return Array.Empty<byte>();

            binWriter.Flush();
            byte[] binData = binStream.ToArray();
            // GLB requires BIN chunk padded to 4-byte boundary
            int binPad = (4 - binData.Length % 4) % 4;
            if (binPad > 0) Array.Resize(ref binData, binData.Length + binPad);

            // ── Build nodes (one node per mesh) ──────────────────────────────
            var nodeList = new List<GltfNode>();
            int ni = 0;
            foreach (var buf in buffers)
            {
                if (buf.Vertices == null || buf.Indices == null) continue;
                if (buf.Vertices.Length == 0 || buf.Indices.Length == 0) continue;
                nodeList.Add(new GltfNode
                {
                    mesh = ni,
                    name = buf.Key?.UniqueId ?? $"node_{ni}",
                    extras = new GltfExtras
                    {
                        uniqueId  = buf.Key?.UniqueId,
                        ifcGuid   = buf.Key?.IfcGuid,
                        elementId = buf.Key?.ElementId ?? 0,
                        category  = buf.Category
                    }
                });
                ni++;
            }

            // ── Build glTF JSON ───────────────────────────────────────────────
            int[] sceneNodes = new int[nodeList.Count];
            for (int i = 0; i < sceneNodes.Length; i++) sceneNodes[i] = i;

            var gltf = new GltfDoc
            {
                asset       = new GltfAsset { version = "2.0", generator = "StingTools" },
                scene       = 0,
                scenes      = new[] { new GltfScene { nodes = sceneNodes } },
                nodes       = nodeList.ToArray(),
                meshes      = meshes.ToArray(),
                accessors   = accessors.ToArray(),
                bufferViews = bufferViews.ToArray(),
                buffers     = new[] { new GltfBuffer { byteLength = binData.Length } }
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(gltf));
            int jsonPad = (4 - jsonBytes.Length % 4) % 4;
            Array.Resize(ref jsonBytes, jsonBytes.Length + jsonPad);
            for (int i = jsonBytes.Length - jsonPad; i < jsonBytes.Length; i++) jsonBytes[i] = 0x20; // space

            // ── Assemble GLB ──────────────────────────────────────────────────
            // Header (12) + JSON chunk (8 + jsonBytes.Length) + BIN chunk (8 + binData.Length)
            int total = 12 + 8 + jsonBytes.Length + 8 + binData.Length;
            using var glb = new MemoryStream(total);
            using var w   = new BinaryWriter(glb);
            w.Write(0x46546C67u); // magic "glTF"
            w.Write(2u);          // version
            w.Write((uint)total);
            w.Write((uint)jsonBytes.Length); w.Write(0x4E4F534Au); w.Write(jsonBytes); // JSON chunk
            w.Write((uint)binData.Length);   w.Write(0x004E4942u); w.Write(binData);   // BIN  chunk
            w.Flush();
            return glb.ToArray();
        }

        private static void Pad4(BinaryWriter w)
        {
            long pos = w.BaseStream.Position;
            int rem = (int)(pos % 4);
            if (rem != 0) for (int i = 0; i < 4 - rem; i++) w.Write((byte)0);
        }

        // ── glTF 2.0 POCO types ───────────────────────────────────────────────
        private class GltfDoc
        {
            public GltfAsset       asset;
            public int             scene;
            public GltfScene[]     scenes;
            public GltfNode[]      nodes;
            public GltfMesh[]      meshes;
            public GltfAccessor[]  accessors;
            public GltfBufferView[] bufferViews;
            public GltfBuffer[]    buffers;
        }
        private class GltfAsset  { public string version; public string generator; }
        private class GltfScene  { public int[] nodes; }
        private class GltfNode
        {
            public int    mesh;
            public string name;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public GltfExtras extras;
        }
        private class GltfExtras
        {
            public string uniqueId;
            public string ifcGuid;
            public int    elementId;
            public string category;
        }
        private class GltfMesh      { public string name; public GltfPrimitive[] primitives; }
        private class GltfPrimitive { public GltfAttr attributes; public int indices; public int mode; }
        private class GltfAttr      { public int POSITION; }
        private class GltfAccessor
        {
            public int    bufferView;
            public int    componentType;
            public int    count;
            public string type;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public float[] min;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public float[] max;
        }
        private class GltfBufferView
        {
            public int byteOffset;
            public int byteLength;
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int target;
        }
        private class GltfBuffer { public int byteLength; }
    }
}
