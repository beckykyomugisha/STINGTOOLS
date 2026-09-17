using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>
    /// Revit-free structural facts about an assembled ISO 19650 tag and its token
    /// array. Extracted from <see cref="TagConfig"/> so the rules that decide whether
    /// a tag is usable can be exercised without a Revit host.
    /// </summary>
    public static class TagTokenIntegrity
    {
        /// <summary>Segments that mean "not known". Never a real answer.</summary>
        public static readonly string[] StructuralPlaceholders = { "XX", "ZZ", "0000" };

        /// <summary>
        /// Real answers supplied by the token policy as a fallback rather than
        /// measured from the model. Not a defect; not a measurement either.
        /// </summary>
        public static readonly string[] AssumedValues = { "GEN" };

        internal static string Sep(string separator)
            => !string.IsNullOrEmpty(separator) ? separator : "-";

        /// <summary>True when <paramref name="tag"/> carries one of
        /// <paramref name="needles"/> as a whole delimited segment.</summary>
        public static bool ContainsSegment(string tag, string separator, IEnumerable<string> needles)
        {
            if (string.IsNullOrEmpty(tag) || needles == null) return false;
            string sep = Sep(separator);
            foreach (string ph in needles)
            {
                if (string.IsNullOrEmpty(ph)) continue;
                if (tag.StartsWith(ph + sep, StringComparison.Ordinal) ||
                    tag.EndsWith(sep + ph, StringComparison.Ordinal) ||
                    tag.Contains(sep + ph + sep, StringComparison.Ordinal) ||
                    string.Equals(tag, ph, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Blocks completeness: the tag names a segment as unknown.</summary>
        public static bool HasStructuralPlaceholder(string tag, string separator)
        {
            // "GEN" is deliberately NOT here. STING_TAG_TOKEN_POLICY.json declares
            // SYS/FUNC/PROD as DERIVED with fallback "GEN" and states that for an
            // architectural element GEN is "a real answer for them, not a guess".
            // Counting it as unknown made every such element permanently incomplete:
            // re-derived on every run, never reachable by Skip mode, and unable to
            // reach 100% on any compliance surface. Assumption is reported through
            // TaggingStats.AssumedByToken and HasPlaceholderOrAssumed instead.
            return ContainsSegment(tag, separator, StructuralPlaceholders);
        }

        /// <summary>Strict/compliance reading: unknown OR assumed.</summary>
        public static bool HasPlaceholderOrAssumed(string tag, string separator)
        {
            var all = new List<string>(StructuralPlaceholders);
            all.AddRange(AssumedValues);
            return ContainsSegment(tag, separator, all);
        }

        /// <summary>
        /// True when the tag splits into exactly <paramref name="expectedSegments"/>
        /// segments and none of them is blank.
        /// </summary>
        public static bool AllSegmentsPresent(string tag, string separator, int expectedSegments)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            string sep = Sep(separator);
            // Counting separators cannot see a blank segment, which is the whole
            // defect: "A-BLD1-Z01-L01-ARC---" carries exactly seven separators and
            // passed as an eight-segment tag. Split and look at the segments.
            string[] parts = tag.Split(new[] { sep }, StringSplitOptions.None);
            if (parts.Length != expectedSegments) return false;
            for (int i = 0; i < parts.Length; i++)
                if (string.IsNullOrWhiteSpace(parts[i])) return false;
            return true;
        }

        /// <summary>
        /// Reconcile freshly derived tokens against what was read back from the
        /// element after writing them. A slot that was derived non-empty but reads
        /// back empty is a FAILED WRITE, not a user's blank.
        /// </summary>
        public static string[] Reconcile(string[] derived, string[] readBack, out int recovered)
        {
            recovered = 0;
            if (readBack == null) return derived;
            if (derived == null) return readBack;

            var outArr = new string[readBack.Length];
            for (int i = 0; i < readBack.Length; i++)
            {
                string back = readBack[i] ?? string.Empty;
                string want = i < derived.Length ? (derived[i] ?? string.Empty) : string.Empty;

                // A non-empty read-back is a real stored value and always wins: the
                // only reason SetIfEmpty declines is that the slot already held one,
                // so this is where a user's manual edit is preserved.
                if (back.Length > 0) { outArr[i] = back; continue; }

                // Empty read-back after a non-empty derive cannot be a user's blank
                // for the same reason. The write failed - silently, because
                // ParameterHelpers.SetString returns false when the parameter is not
                // reachable on the instance. Keep the derived value so the failure
                // cannot reach the tag, and count it so it can be reported.
                if (want.Length > 0) { outArr[i] = want; recovered++; continue; }

                outArr[i] = string.Empty;
            }
            return outArr;
        }
    }
}
