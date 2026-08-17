// ClashMeshBuffer.cs — immutable per-element mesh for clash detection.
// Vertices are stored as flat float[] (x,y,z, x,y,z, ...) to avoid XYZ allocation
// during parallel narrow-phase work. All transforms (link instance, family instance)
// are already baked in by the exporter.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Clash
{
    /// <summary>
    /// Immutable tessellated geometry for a single element (or link-instance-element pair).
    /// </summary>
    public sealed class ClashMeshBuffer
    {
        public ClashElementKey Key { get; }
        public string Category { get; }
        public float[] Vertices { get; }   // length = 3 * vertexCount
        public int[] Indices { get; }      // length = 3 * triangleCount
        public float MinX { get; }
        public float MinY { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxY { get; }
        public float MaxZ { get; }

        public int TriangleCount => Indices.Length / 3;

        public ClashMeshBuffer(ClashElementKey key, string category, float[] vertices, int[] indices)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            if (vertices.Length % 3 != 0) throw new ArgumentException("vertices length must be multiple of 3");
            if (indices.Length % 3 != 0) throw new ArgumentException("indices length must be multiple of 3");

            Key = key ?? throw new ArgumentNullException(nameof(key));
            Category = category ?? "";
            Vertices = vertices;
            Indices = indices;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < vertices.Length; i += 3)
            {
                float x = vertices[i], y = vertices[i + 1], z = vertices[i + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }
    }

    /// <summary>
    /// Composite key for a Revit element in a (possibly linked) document.
    /// </summary>
    public sealed class ClashElementKey : IEquatable<ClashElementKey>
    {
        public string DocGuid { get; }
        // Revit 2024+ ElementId.Value is Int64 and a long-lived model can mint
        // ids past int.MaxValue. Narrowing here truncated the identity, so an
        // add stored one id and the matching delete carried another — the
        // tombstone missed and stale geometry lingered (issue #722).
        public long LinkInstanceElementId { get; }   // -1 for host
        public long ElementId { get; }
        public string UniqueId { get; }
        public string IfcGuid { get; }

        public ClashElementKey(string docGuid, long linkInstanceElementId, long elementId, string uniqueId, string ifcGuid)
        {
            DocGuid = docGuid ?? "";
            LinkInstanceElementId = linkInstanceElementId;
            ElementId = elementId;
            UniqueId = uniqueId ?? "";
            IfcGuid = ifcGuid ?? "";
        }

        public bool Equals(ClashElementKey other)
        {
            if (other == null) return false;
            return DocGuid == other.DocGuid
                && LinkInstanceElementId == other.LinkInstanceElementId
                && ElementId == other.ElementId;
        }

        public override bool Equals(object obj) => Equals(obj as ClashElementKey);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = DocGuid?.GetHashCode() ?? 0;
                // Hash the full 64 bits — folding the high word in rather than
                // letting an implicit narrowing drop it, so two ids that differ
                // only above bit 31 land in different buckets.
                h = (h * 397) ^ LinkInstanceElementId.GetHashCode();
                h = (h * 397) ^ ElementId.GetHashCode();
                return h;
            }
        }

        public override string ToString() => $"{DocGuid}:{LinkInstanceElementId}:{ElementId}";
    }
}
