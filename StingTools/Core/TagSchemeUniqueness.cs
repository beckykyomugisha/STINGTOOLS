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
    /// The collision was real on KUT: SEQ is allocated per DISC + LOC + SYS + LVL, but the KUT
    /// identifier (KUT-orig-volume-level-discipline-number) carried no SYS, so an HVAC and a
    /// chilled-water element on the same level of the same building can both be 0001 and
    /// both rendered KUT-SMB-01-L01-M-0001. The KUT scheme now carries SYS (KUTDR-1); the
    /// duplicate check stays because an unmapped LOC still falls back to one shared value.
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

        /// <summary>
        /// The SEQ counter-group fields (SeqAssigner.BuildSeqKey: DISC, [LOC], [ZONE], SYS, LVL)
        /// that a scheme does not render. SEQ is only unique within its group, so a scheme that
        /// renders SEQ but drops a group field can give two elements one identifier. Empty when
        /// the scheme renders no SEQ (it is then not a per-element identifier at all) or carries
        /// every group field.
        /// </summary>
        public static List<string> MissingSeqGroupTokens(IEnumerable<string> schemeTokens, bool seqIncludesLoc, bool seqIncludesZone)
        {
            var have = new HashSet<string>((schemeTokens ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
            if (!have.Contains("SEQ")) return new List<string>();

            var group = new List<string> { "DISC" };
            if (seqIncludesLoc) group.Add("LOC");
            if (seqIncludesZone) group.Add("ZONE");
            group.Add("SYS");
            group.Add("LVL");
            return group.Where(t => !have.Contains(t)).ToList();
        }
    }
}
