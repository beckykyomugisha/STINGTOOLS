// StingTools — Drawing Template Manager · the identity of a production context
//
// DrawingProducer finds the view and sheet an earlier run made for the same drawing
// type and context by the STING_VIEW_CONTEXT_TAG_TXT / STING_SHEET_CONTEXT_TXT stamp:
//
//     <level name>::<room id>::<tag>[::<scope box name>]
//
// Matched as a whole string, that keyed identity on NAMES. Rename "Level 1" to
// "Ground Floor", or a scope box, and the next run found nothing, so it minted a
// second view, a second sheet and a second sheet number for the same drawing.
//
// The stamp keeps its shape — the level name stays first, the tag third and the box
// name last, because Renumber, title-block healing and crop recovery read those parts
// (ViewContextTag, SheetProductionContext) — and the room segment now also carries the
// stable ids after a '#':
//
//     Level 1::#L312::::STING-AREA::A01::L01           level + box
//     Level 1::#L312|B1f2c…-0004a1::A01::STING-AREA…   (tag kept for display)
//     ::4471#R4471::Kitchen (0.12)::                    room
//
// Identity is the id list, plus the tag when there is no box or room (a grid section
// or an exterior face is named by its tag and has nothing else). Names are display
// only. A context with no ids at all (no level, room or box) is stamped exactly as
// before, so it matches exactly as before.
//
// Backward compatible: a stamp written before this change has no '#'. The producer
// looks for the id match first, then for the stamp the old code would have written
// (Legacy), and re-stamps what it finds in the new form.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public static class ProductionContextKey
    {
        private const char IdMarker = '#';
        private const char IdSeparator = '|';

        /// <summary>
        /// The stamp for a context: names for display, ids for identity. With no level,
        /// room or box it is exactly <see cref="Legacy"/>.
        /// </summary>
        public static string Compose(string levelName, long? levelId, string roomId, string tag,
            string boxName, string boxUniqueId)
        {
            var ids = new List<string>();
            if (levelId.HasValue && levelId.Value > 0) ids.Add("L" + levelId.Value);
            if (!string.IsNullOrEmpty(roomId)) ids.Add("R" + roomId);
            if (!string.IsNullOrEmpty(boxUniqueId)) ids.Add("B" + boxUniqueId);
            string roomSeg = roomId ?? "";
            if (ids.Count > 0) roomSeg += IdMarker + string.Join(IdSeparator.ToString(), ids);
            return ViewContextTag.Compose(levelName, roomSeg, tag, boxName);
        }

        /// <summary>The stamp the producer wrote before ids were part of it.</summary>
        public static string Legacy(string levelName, string roomId, string tag, string boxName)
            => ViewContextTag.Compose(levelName, roomId, tag, boxName);

        /// <summary>
        /// What a stamp identifies: "#ids" (plus the tag when the ids name no box or room)
        /// for a stamp that carries ids; the whole stamp for one that does not.
        /// </summary>
        public static string Identity(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return stamp ?? "";
            var sep = ViewContextTag.Separator;
            int a = stamp.IndexOf(sep, StringComparison.Ordinal);
            if (a < 0) return stamp;
            int b = stamp.IndexOf(sep, a + sep.Length, StringComparison.Ordinal);
            if (b < 0) return stamp;
            string roomSeg = stamp.Substring(a + sep.Length, b - a - sep.Length);
            int hash = roomSeg.IndexOf(IdMarker);
            if (hash < 0) return stamp;
            string ids = roomSeg.Substring(hash + 1);
            if (ids.Length == 0) return stamp;

            bool named = false;
            foreach (var id in ids.Split(IdSeparator))
                if (id.StartsWith("R", StringComparison.Ordinal) || id.StartsWith("B", StringComparison.Ordinal)) named = true;
            if (named) return IdMarker + ids;

            int c = stamp.IndexOf(sep, b + sep.Length, StringComparison.Ordinal);
            string tag = c < 0 ? stamp.Substring(b + sep.Length) : stamp.Substring(b + sep.Length, c - b - sep.Length);
            return IdMarker + ids + IdSeparator + "T" + tag;
        }

        /// <summary>
        /// True when <paramref name="stamp"/> (read off an element) is the context
        /// <paramref name="current"/> (this run's stamp) describes — by id, or, for an
        /// element stamped before ids, by the <paramref name="legacy"/> string.
        /// </summary>
        public static bool Matches(string stamp, string current, string legacy)
        {
            if (stamp == null) return false;
            if (string.Equals(stamp, current, StringComparison.Ordinal)) return true;
            if (string.Equals(Identity(stamp), Identity(current), StringComparison.Ordinal)
                && Identity(stamp) != stamp) return true;
            return legacy != null && string.Equals(stamp, legacy, StringComparison.Ordinal);
        }
    
        /// <summary>
        /// DTW-103: whether an element found by its pre-id (name-only) stamp may be adopted
        /// for a context on level <paramref name="contextLevelId"/>. A legacy stamp names the
        /// level by name, so after "Level 1" is renamed and a new level takes that name the
        /// string describes the new level. <paramref name="placedLevelIds"/> are the levels of
        /// what the element already holds (for a sheet, its placed views' levels): it is
        /// adopted when one of them is this level, or when none carries a level (nothing to
        /// tell by, the behaviour before this check). A context with no level always adopts.
        /// </summary>
        public static bool LegacyStampOnLevel(long? contextLevelId, IEnumerable<long> placedLevelIds)
        {
            if (!contextLevelId.HasValue || contextLevelId.Value <= 0) return true;
            bool anyLevel = false;
            foreach (var id in placedLevelIds ?? Array.Empty<long>())
            {
                if (id <= 0) continue;
                if (id == contextLevelId.Value) return true;
                anyLevel = true;
            }
            return !anyLevel;
        }
    }
}
