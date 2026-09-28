// StingTools — Drawing Template Manager · A-2
//
// The Revit-free decisions behind three AnnotationRulePack fields that were
// declared, edited in the UI, and then read by no engine:
//
//   minSizeMm on a TAG rule   — only the dimensioners read it; AutoTag ignored
//                               it, so "don't tag anything under 50 mm" tagged
//                               every stub anyway.
//   tagDepths (pack map)      — edited by DrawingTypeEditorDialog's tag-family
//                               grid; no engine read it.
//   depth (per rule)          — edited by the same dialog's rule grid; no
//                               engine read it.
//
// The live per-category depth writer is TokenProfileApplier.WriteCategoryDepths
// (element TYPE scope). It used to merge only the drawing type's
// tokenProfile.categoryDepths over the style pack's categoryDepths. The
// annotation layer now sits between them:
//
//     tokenProfile.categoryDepths  >  annotation (tagDepths, then rule depth)  >  pack.categoryDepths
//
// i.e. anything stated on the DRAWING TYPE beats the shared style pack, and
// the explicit token profile stays the most specific statement.
//
// Linked into StingTools.Tags.Tests. Uses AnnotationRuleKinds (also Revit-free).

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    /// <summary>minSizeMm gate shared by every annotation pass that honours it.</summary>
    public static class AnnotationMinSize
    {
        public const double MmPerFt = 304.8;

        /// <summary>
        /// Keep the element when no minimum is set, or when its measured size
        /// meets it. A size of 0 or less means "could not be measured" and the
        /// element is KEPT — annotating something extra is recoverable, silently
        /// dropping a requested annotation is not. Callers count those so the
        /// user hears how many were unmeasurable.
        /// </summary>
        public static bool Keeps(double sizeMm, double? minSizeMm)
        {
            if (!minSizeMm.HasValue || minSizeMm.Value <= 0) return true;
            if (sizeMm <= 0) return true;
            return sizeMm >= minSizeMm.Value;
        }
    }

    public enum TagLeaderMode { None, Attached, Free, Unrecognised }

    /// <summary>
    /// AutoAnnotationRule.leaderStyle on a TAG rule. The runner hard-coded
    /// addLeader:false, so "Attached" / "Free" were ignored; only the SPOT
    /// rules' leaderStyle was ever read.
    /// </summary>
    public static class TagLeader
    {
        public static TagLeaderMode Parse(string leaderStyle)
        {
            if (string.IsNullOrWhiteSpace(leaderStyle)) return TagLeaderMode.None;
            switch (leaderStyle.Trim().ToLowerInvariant())
            {
                case "noleader":
                case "none":     return TagLeaderMode.None;
                case "attached": return TagLeaderMode.Attached;
                case "free":     return TagLeaderMode.Free;
                default:         return TagLeaderMode.Unrecognised;
            }
        }
    }

    public static class TagDepthLayering
    {
        /// <summary>
        /// The drawing type's annotation-level depth map: the pack-wide
        /// <see cref="AnnotationRulePack.TagDepths"/>, then each enabled TAG
        /// rule's <see cref="AutoAnnotationRule.Depth"/> keyed by the rule's
        /// effective category (the row is the more specific statement, so it
        /// wins). A rule on "*" cannot name one category and contributes
        /// nothing. Depths are clamped to 1..10. Null when there is nothing.
        /// </summary>
        public static Dictionary<string, int> FromAnnotation(AnnotationRulePack pack)
        {
            if (pack == null) return null;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (pack.TagDepths != null)
                foreach (var kv in pack.TagDepths)
                    if (!string.IsNullOrWhiteSpace(kv.Key)) map[kv.Key.Trim()] = Clamp(kv.Value);
            if (pack.Rules != null)
                foreach (var r in pack.Rules)
                {
                    if (r == null || !r.Enabled || !r.Depth.HasValue) continue;
                    if (!AnnotationRuleKinds.IsTagKind(r.RuleType)) continue;
                    var cat = AnnotationRuleKinds.EffectiveCategory(r.RuleType, r.Category);
                    if (string.IsNullOrWhiteSpace(cat) || cat.Trim() == "*") continue;
                    map[cat.Trim()] = Clamp(r.Depth.Value);
                }
            return map.Count > 0 ? map : null;
        }

        /// <summary>
        /// Layer depth maps, HIGHEST precedence first. A key in an earlier map
        /// wins over the same key (case-insensitive) in a later one. Null when
        /// every layer is empty.
        /// </summary>
        public static Dictionary<string, int> Merge(params Dictionary<string, int>[] highestFirst)
        {
            var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (highestFirst != null)
                for (int i = highestFirst.Length - 1; i >= 0; i--)
                {
                    var layer = highestFirst[i];
                    if (layer == null) continue;
                    foreach (var kv in layer)
                        if (!string.IsNullOrWhiteSpace(kv.Key)) merged[kv.Key.Trim()] = kv.Value;
                }
            return merged.Count > 0 ? merged : null;
        }

        private static int Clamp(int d) => d < 1 ? 1 : d > 10 ? 10 : d;
    }
}
