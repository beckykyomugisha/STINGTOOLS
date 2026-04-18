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
            public Envelope Envelope { get; }
            public ElementBox(ClashMeshBuffer m, double pad)
            {
                Mesh = m;
                Envelope = new Envelope(
                    minX: m.MinX - pad, minY: m.MinY - pad,
                    maxX: m.MaxX + pad, maxY: m.MaxY + pad);
            }
        }

        private readonly RBush<ElementBox> _tree = new RBush<ElementBox>();
        private readonly Dictionary<ClashElementKey, ElementBox> _byKey = new Dictionary<ClashElementKey, ElementBox>();

        // rec-9: Remember the padding used at Build so AddOrUpdate() produces
        // consistent envelopes on subsequent element inserts.
        private double _padding = 0.164;

        public void Build(IEnumerable<ClashMeshBuffer> meshes, double paddingFeet = 0.164)  // ~50 mm
        {
            _padding = paddingFeet;
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

        /// <summary>
        /// rec-9: Insert or refresh a single element's box without a full BulkLoad
        /// rebuild. Used by ClashSession.RefreshElement for live-clash edits —
        /// avoids the ~50 ms-per-edit cost of reloading 50k items on large models.
        /// </summary>
        public void AddOrUpdate(ClashMeshBuffer mesh)
        {
            if (mesh == null || mesh.TriangleCount == 0) return;
            if (_byKey.TryGetValue(mesh.Key, out var old))
            {
                try { _tree.Delete(old); }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"AabbSweep.Delete: {ex.Message}"); }
            }
            var fresh = new ElementBox(mesh, _padding);
            _byKey[mesh.Key] = fresh;
            try { _tree.Insert(fresh); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"AabbSweep.Insert: {ex.Message}"); }
        }

        /// <summary>
        /// rec-9: Drop a single element from the index. ClashSession.RemoveElement
        /// calls this on deletion sentinel from LiveClashUpdater.
        /// </summary>
        public void Remove(ClashElementKey key)
        {
            if (key == null) return;
            if (!_byKey.TryGetValue(key, out var box)) return;
            _byKey.Remove(key);
            try { _tree.Delete(box); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"AabbSweep.Delete: {ex.Message}"); }
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
