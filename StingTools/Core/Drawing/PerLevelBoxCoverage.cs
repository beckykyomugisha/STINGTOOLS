// StingTools — Drawing Template Manager · which per-level pairs a scope box already produces
//
// DTW-99. A project can be drawn three ways: STING::<type>::<level> boxes, STING-AREA::
// area boxes, and whole-floor per level. DTW-95 stopped the MEP preset producing the same
// drawing twice by switching the per-level step off whenever ANY STING:: box existed — so
// one architectural box, or one box on one level, cost every whole-floor MEP plan.
//
// The right unit is the (drawing type, level) pair. A STING::<type>::<level> box covers
// that type on that level; an area box covers each (type, level) pair it produces. Per-level
// production skips exactly those pairs and produces the rest. A box with no level segment,
// or one whose level names no level, covers nothing: its view is produced without a level,
// so it is not the whole-floor drawing of any level — the note says so.
//
// The level segment is read by LevelSegmentResolver, the reader the STING:: producer uses,
// so a box covers the level it is produced on and no other.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One (drawing type, level) pair a scope box produces.</summary>
    public sealed class BoxCover
    {
        public string BoxName { get; set; }
        public string TypeId { get; set; }
        public long LevelId { get; set; }
    }

    public sealed class PerLevelBoxCoverage
    {
        private readonly Dictionary<string, string> _byPair = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Distinct (type, level) pairs covered.</summary>
        public int Count => _byPair.Count;

        private static string Key(string typeId, long levelId) => (typeId ?? "").Trim() + "|" + levelId;

        private void Add(string typeId, long levelId, string box)
        {
            if (string.IsNullOrWhiteSpace(typeId)) return;
            var k = Key(typeId, levelId);
            if (!_byPair.ContainsKey(k)) _byPair[k] = box;   // first box named wins the report
        }

        /// <summary>
        /// Coverage from the STING:: box names in the model (any other name is ignored), read
        /// against <paramref name="levels"/>, plus the pairs the area boxes produce.
        /// <paramref name="notes"/> (optional) gets one line per STING:: box that covers no
        /// level, with the reason. <paramref name="boxTypeProduced"/> (optional) says whether
        /// the box route actually produces a box bound to that drawing type id — inside an MEP
        /// workflow only MEP-bound boxes are produced, so an architectural box stands in for
        /// nothing; null counts every well-formed box.
        /// </summary>
        public static PerLevelBoxCoverage Build(IEnumerable<string> stingBoxNames, IList<LevelRef> levels,
            IEnumerable<BoxCover> areaCovers, List<string> notes, Func<string, bool> boxTypeProduced = null)
        {
            var cov = new PerLevelBoxCoverage();
            var lv = levels ?? new List<LevelRef>();
            foreach (var name in stingBoxNames ?? Enumerable.Empty<string>())
            {
                if (!ScopeBoxNames.TryParseDrawingType(name, out var typeId, out var level, out _, out _)) continue;
                if (boxTypeProduced != null && !boxTypeProduced(typeId)) continue;
                if (string.IsNullOrWhiteSpace(level))
                {
                    notes?.Add($"'{name.Trim()}' has no level segment — it covers no level, so whole-floor {typeId} plans are still produced.");
                    continue;
                }
                var id = LevelSegmentResolver.Resolve(level, lv, out var how);
                if (!id.HasValue)
                {
                    notes?.Add($"'{name.Trim()}': level '{level}' — {how}; it covers no level.");
                    continue;
                }
                cov.Add(typeId, id.Value, name.Trim());
            }
            foreach (var c in areaCovers ?? Enumerable.Empty<BoxCover>())
                if (c != null) cov.Add(c.TypeId, c.LevelId, c.BoxName);
            return cov;
        }

        /// <summary>True when a scope box already produces <paramref name="typeId"/> on the level; <paramref name="boxName"/> names it.</summary>
        public bool TryCovered(string typeId, long levelId, out string boxName)
        {
            boxName = null;
            if (string.IsNullOrWhiteSpace(typeId)) return false;
            return _byPair.TryGetValue(Key(typeId, levelId), out boxName);
        }
    }
}
