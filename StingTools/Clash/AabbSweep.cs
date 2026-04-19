// AabbSweep.cs — global broad-phase via R-tree over element AABBs.
// Returns candidate element pairs for narrow-phase processing.
using System.Collections.Generic;
using RBush;

namespace StingTools.Core.Clash
{
    public sealed class AabbSweep
    {
        public sealed class ElementBox : ISpatialData
        {
            public ClashMeshBuffer Mesh;
            // RBush 4.x returns Envelope by ref readonly (avoids struct copy on
            // every tree lookup), so the implementing property needs a backing
            // field and a ref-returning getter instead of an auto-property.
            private readonly Envelope _envelope;
            public ref readonly Envelope Envelope => ref _envelope;
            public ElementBox(ClashMeshBuffer m, double pad)
            {
                Mesh = m;
                // RBush 4.x: Envelope is a positional record struct (MinX, MinY, MaxX, MaxY).
                // The 3.x named-parameter form (minX:/minY:/maxX:/maxY:) no longer resolves.
                _envelope = new Envelope(
                    m.MinX - pad, m.MinY - pad,
                    m.MaxX + pad, m.MaxY + pad);
            }
        }

        private readonly RBush<ElementBox> _tree = new RBush<ElementBox>();
        private readonly Dictionary<ClashElementKey, ElementBox> _byKey = new Dictionary<ClashElementKey, ElementBox>();

        public void Build(IEnumerable<ClashMeshBuffer> meshes, double paddingFeet = 0.164)  // ~50 mm
        {
            var items = new List<ElementBox>();
            foreach (var m in meshes)
            {
                if (m.TriangleCount == 0) continue;
                var box = new ElementBox(m, paddingFeet);
                items.Add(box);
                _byKey[m.Key] = box;
            }
            _tree.BulkLoad(items);
        }

        public IEnumerable<(ClashMeshBuffer A, ClashMeshBuffer B)> CandidatePairs()
        {
            var yielded = new HashSet<long>();
            foreach (var item in _byKey.Values)
            {
                var hits = _tree.Search(item.Envelope);
                foreach (var h in hits)
                {
                    if (ReferenceEquals(h, item)) continue;
                    // Z filter: RBush is 2D, so verify Z overlap explicitly.
                    if (h.Mesh.MinZ > item.Mesh.MaxZ || h.Mesh.MaxZ < item.Mesh.MinZ) continue;
                    long pair = PairKey(item.Mesh.Key.GetHashCode(), h.Mesh.Key.GetHashCode());
                    if (!yielded.Add(pair)) continue;
                    yield return (item.Mesh, h.Mesh);
                }
            }
        }

        private static long PairKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 32) | (uint)b;
        }
    }
}
