// StingTools — Drawing Template Manager · which level a scope-box name's level segment means
//
// A STING:: box names its level in the second segment: STING::<type>::<level>[::<tag>].
// The grammar allows only A-Z 0-9 . _ - in a segment, so a level called "Level 1"
// (a space) could never be written, and matching the segment against Level.Name
// exactly meant the box's view was produced on no level at all.
//
// The segment is read the way the rest of the planner names levels: the unique level
// code first (ScopeBoxRevit.LevelCodes — "L01", the code area boxes and the saved plan
// use), then the level's name, then the name with everything but letters and digits
// dropped ("Level_1" and "Level1" both mean "Level 1"). Each stage is exact; nothing
// is matched by "contains", so "L1" never lands on "L10". Two levels answering the same
// stage is ambiguous and resolves to none, with the reason.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>A level as the resolver sees it: its id, name and unique level code.</summary>
    public sealed class LevelRef
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
    }

    public static class LevelSegmentResolver
    {
        /// <summary>
        /// The level <paramref name="segment"/> names, or null. <paramref name="note"/> says
        /// how it matched (by code, by name, by name without punctuation) or why none did.
        /// </summary>
        public static long? Resolve(string segment, IEnumerable<LevelRef> levels, out string note)
        {
            note = null;
            var list = (levels ?? Enumerable.Empty<LevelRef>()).Where(l => l != null).ToList();
            if (string.IsNullOrWhiteSpace(segment)) { note = "no level segment"; return null; }
            var seg = segment.Trim();

            var byCode = list.Where(l => string.Equals((l.Code ?? "").Trim(), seg, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byCode.Count == 1) { note = $"level code '{seg}'"; return byCode[0].Id; }
            if (byCode.Count > 1) { note = $"level code '{seg}' is shared by {string.Join(", ", byCode.Select(l => l.Name))}"; return null; }

            var byName = list.Where(l => string.Equals((l.Name ?? "").Trim(), seg, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1) { note = $"level name '{byName[0].Name}'"; return byName[0].Id; }
            if (byName.Count > 1) { note = $"{byName.Count} levels are called '{seg}'"; return null; }

            var squeezed = Squeeze(seg);
            if (squeezed.Length > 0)
            {
                var bySqueeze = list.Where(l => string.Equals(Squeeze(l.Name), squeezed, StringComparison.OrdinalIgnoreCase)).ToList();
                if (bySqueeze.Count == 1) { note = $"level name '{bySqueeze[0].Name}' (spaces and punctuation ignored)"; return bySqueeze[0].Id; }
                if (bySqueeze.Count > 1) { note = $"'{seg}' matches {string.Join(", ", bySqueeze.Select(l => l.Name))} once spaces are ignored"; return null; }
            }

            note = $"'{seg}' is neither a level code ({string.Join(", ", list.Select(l => l.Code).Where(c => !string.IsNullOrEmpty(c)).Take(8))}…) nor a level name";
            return null;
        }

        /// <summary>Letters and digits only: "Level 1" → "Level1", "Level_1" → "Level1".</summary>
        public static string Squeeze(string s)
            => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());
    }
}
