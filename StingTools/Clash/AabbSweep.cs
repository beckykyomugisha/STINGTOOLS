// AabbSweep.cs — global broad-phase via R-tree over element AABBs.
// Returns candidate element pairs for narrow-phase processing.
using System;
using System.Collections.Generic;
using RBush;

namespace StingTools.Core.Clash
{
    public sealed class AabbSweep
    {
        public sealed class ElementBox : ISpatialData
        {
            public ClashMeshBuffer Mesh;

            // RBush 4.0 changed ISpatialData.Envelope to a ref-readonly return.
            // The backing field is required because ref-readonly property bodies
            // can only return a ref to a stored location.
            private readonly Envelope _envelope;
            public ref readonly Envelope Envelope => ref _envelope;

            public ElementBox(ClashMeshBuffer m, double pad)
            {
                Mesh = m;
                // RBush 4.0 renamed the Envelope constructor parameters
                // (minX/minY/maxX/maxY -> MinX/MinY/MaxX/MaxY). Using positional
                // args so the call survives either naming convention.
                _envelope = new Envelope(
                    m.MinX - pad, m.MinY - pad,
                    m.MaxX + pad, m.MaxY + pad);
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

        /// <summary>
        /// G2: Query RBush for all element meshes whose XY envelope overlaps
        /// the target's padded envelope. Used by ClashSession.NarrowPhaseFor so
        /// per-edit narrow phase touches O(log n) candidates, not O(n).
        /// Z overlap is NOT verified here — caller must still run an AABB Z
        /// check (which it does anyway in the outer reject loop).
        /// </summary>
        public IEnumerable<ClashMeshBuffer> QueryCandidatesFor(ClashMeshBuffer target)
        {
            if (target == null) yield break;
            // Rebuild the target envelope with the current padding so the
            // query widens to catch clearance-zone candidates.
            var env = new Envelope(
                target.MinX - _padding, target.MinY - _padding,
                target.MaxX + _padding, target.MaxY + _padding);
            foreach (var hit in _tree.Search(env))
            {
                if (hit?.Mesh != null) yield return hit.Mesh;
            }
        }

        public IEnumerable<(ClashMeshBuffer A, ClashMeshBuffer B)> CandidatePairs()
        {
            // D4: Pair dedup encoded as `long` packed from per-key int handles.
            //     Prior HashSet<(ClashElementKey, ClashElementKey)> allocated a
            //     ValueTuple<ClashElementKey, ClashElementKey> per pair plus
            //     the two ClashElementKey references — for 1M candidate pairs
            //     that is ~50-200 MB of allocations and significant GC pressure
            //     during the broad-phase loop.
            //
            //     New: assign each ClashElementKey an int id (sequence in
            //     _byKey enumeration order) and encode the pair as
            //     (uint hi << 32) | uint lo with hi < lo. HashSet<long> has
            //     8 bytes per entry vs ~64 bytes for the tuple-based version.
            //     The dedup-by-Equals semantics are preserved because each
            //     ClashElementKey gets exactly one id.
            //
            //     Mid-loop key insertions are not supported by the broad-phase
            //     contract (Build() runs before CandidatePairs()), so the id
            //     mapping is stable for the duration of the call.
            var idByKey = new Dictionary<ClashElementKey, int>(_byKey.Count);
            int nextId = 0;
            foreach (var k in _byKey.Keys) idByKey[k] = nextId++;
            var yielded = new HashSet<long>();

            foreach (var item in _byKey.Values)
            {
                var hits = _tree.Search(item.Envelope);
                int idA = idByKey[item.Mesh.Key];
                foreach (var h in hits)
                {
                    if (ReferenceEquals(h, item)) continue;
                    // Z filter: RBush is 2D, so verify Z overlap explicitly.
                    if (h.Mesh.MinZ > item.Mesh.MaxZ || h.Mesh.MaxZ < item.Mesh.MinZ) continue;
                    int idB = idByKey[h.Mesh.Key];
                    int lo = idA < idB ? idA : idB;
                    int hi = idA < idB ? idB : idA;
                    long pairKey = ((long)(uint)hi << 32) | (uint)lo;
                    if (!yielded.Add(pairKey)) continue;
                    yield return (item.Mesh, h.Mesh);
                }
            }
        }
    }
}
