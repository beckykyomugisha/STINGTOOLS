using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// A scheme tag is an identifier, so two elements must never render the same one.
    /// The audit only compared each element's stored value with its own re-render, which
    /// can never see a collision between two elements (KUT deep review ISO-1).
    /// <para>
    /// The collision is real on KUT: SEQ is allocated per DISC + LOC + SYS + LVL, but the KUT
    /// identifier (KUT-orig-volume-level-discipline-number) carries no SYS, so an HVAC and a
    /// chilled-water element on the same level of the same building can both be 0001 and
    /// both render KUT-SMB-01-L01-M-0001. Whether the identifier gains SYS or SEQ is keyed
    /// differently is an Owner decision (ROADMAP KUTDR-1); this makes the collision visible.
    /// </para>
    /// Revit-free so StingTools.Tags.Tests can prove it.
    /// </summary>
    public static class TagSchemeUniqueness
    {
        /// <summary>Rendered values shared by more than one element, with the element ids,
        /// per scheme. Blank renders are ignored (they are reported as unrendered).</summary>
        public static Dictionary<(string scheme, string value), List<long>> FindDuplicates(
            IEnumerable<(string scheme, string value, long elementId)> renders)
        {
            return (renders ?? Enumerable.Empty<(string, string, long)>())
                .Where(r => !string.IsNullOrWhiteSpace(r.value))
                .GroupBy(r => (r.scheme ?? "", r.value.Trim()))
                .Where(g => g.Select(x => x.elementId).Distinct().Count() > 1)
                .ToDictionary(g => g.Key, g => g.Select(x => x.elementId).Distinct().OrderBy(x => x).ToList());
        }
    }
}
