// StingTools — Drawing Template Manager · annotation overrides
//
// Two override surfaces were written and never read:
//
//   * ProductionRule.annotationOverride — per-view-rule annotation in the
//     drawing-type JSON.
//   * DrawingProductionPreset.AnnotationOverrides — what the Production
//     Config dialog's whole Annotation section saves (under "*", or a
//     drawing-type id).
//
// So a user could configure tags and dimensions in that dialog, press
// Produce, and get the drawing type's own annotation — every time, silently.
//
// They are LAYERED, not substituted. The dialog always saves a full pack, so
// a replace would throw away every drawing type's carefully-authored rules in
// favour of one generic set. Instead each layer overrides the rules it names
// — keyed by (ruleType, category) — adds the ones it introduces, and
// overrides any scalar it actually sets. A layer that disables a rule
// (enabled:false) switches it off; a layer silent on a rule leaves it alone.
//
// Order, least to most specific to THIS run:
//   drawing type  ->  production rule  ->  preset "*"  ->  preset[drawing-type id]
//
// Revit-free, so the precedence is unit-tested.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class AnnotationPackLayering
    {
        /// <summary>
        /// The pack to run: <paramref name="layers"/> overlaid in order onto
        /// <paramref name="basePack"/>. Returns <paramref name="basePack"/>
        /// itself (not a copy) when no layer is present, so the common path
        /// allocates nothing. Never mutates its inputs.
        /// </summary>
        public static AnnotationRulePack Compose(AnnotationRulePack basePack, params AnnotationRulePack[] layers)
        {
            var present = (layers ?? Array.Empty<AnnotationRulePack>()).Where(l => l != null).ToList();
            if (present.Count == 0) return basePack;

            var result = Copy(basePack ?? new AnnotationRulePack());
            foreach (var layer in present) Overlay(result, layer);
            return result;
        }

        private static string RuleKey(AutoAnnotationRule r)
            => ((r?.RuleType ?? "").Trim() + "|" + (r?.Category ?? "").Trim()).ToLowerInvariant();

        private static void Overlay(AnnotationRulePack into, AnnotationRulePack over)
        {
            if (over.Rules != null)
            {
                into.Rules = into.Rules ?? new List<AutoAnnotationRule>();
                foreach (var r in over.Rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Category)))
                {
                    var idx = into.Rules.FindIndex(x => RuleKey(x) == RuleKey(r));
                    if (idx >= 0) into.Rules[idx] = r;
                    else into.Rules.Add(r);
                }
            }

            if (over.AutoTag.HasValue) into.AutoTag = over.AutoTag;
            if (over.AutoDim.HasValue) into.AutoDim = over.AutoDim;
            if (!string.IsNullOrWhiteSpace(over.DimensionStyle)) into.DimensionStyle = over.DimensionStyle;
            // DimensionStrategy defaults to "Linear" on every pack, so "Linear" on an
            // override is indistinguishable from "not set". Only a non-default value
            // is treated as an instruction.
            if (!string.IsNullOrWhiteSpace(over.DimensionStrategy)
                && !string.Equals(over.DimensionStrategy, "Linear", StringComparison.OrdinalIgnoreCase))
                into.DimensionStrategy = over.DimensionStrategy;
            if (over.DenseUntilScale.HasValue) into.DenseUntilScale = over.DenseUntilScale;

            into.TagFamilies = MergeDict(into.TagFamilies, over.TagFamilies);
            into.TagDepths = MergeDict(into.TagDepths, over.TagDepths);

            if (!string.IsNullOrWhiteSpace(over.NorthArrowFamily))   into.NorthArrowFamily = over.NorthArrowFamily;
            if (!string.IsNullOrWhiteSpace(over.NorthArrowPosition)) into.NorthArrowPosition = over.NorthArrowPosition;
            if (over.NorthArrowSizeMm.HasValue)                      into.NorthArrowSizeMm = over.NorthArrowSizeMm;
            if (!string.IsNullOrWhiteSpace(over.ScaleBarFamily))     into.ScaleBarFamily = over.ScaleBarFamily;
            if (!string.IsNullOrWhiteSpace(over.ScaleBarPosition))   into.ScaleBarPosition = over.ScaleBarPosition;
            if (!string.IsNullOrWhiteSpace(over.KeyPlanFamily))      into.KeyPlanFamily = over.KeyPlanFamily;
            if (!string.IsNullOrWhiteSpace(over.KeyPlanPosition))    into.KeyPlanPosition = over.KeyPlanPosition;
            if (over.MatchlineOffsetMm.HasValue)                     into.MatchlineOffsetMm = over.MatchlineOffsetMm;
            if (over.SpotElevationRules != null)  into.SpotElevationRules = over.SpotElevationRules;
            if (over.SpotCoordinateRules != null) into.SpotCoordinateRules = over.SpotCoordinateRules;
        }

        private static Dictionary<string, T> MergeDict<T>(Dictionary<string, T> a, Dictionary<string, T> b)
        {
            if (b == null || b.Count == 0) return a;
            var m = a != null ? new Dictionary<string, T>(a, StringComparer.OrdinalIgnoreCase)
                              : new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in b) m[kv.Key] = kv.Value;
            return m;
        }

        /// <summary>Shallow copy with copied collections — enough that overlaying
        /// never writes into the registry's cached pack.</summary>
        private static AnnotationRulePack Copy(AnnotationRulePack p)
        {
            var c = (AnnotationRulePack)typeof(AnnotationRulePack)
                .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(p, null);
            c.Rules = p.Rules != null ? new List<AutoAnnotationRule>(p.Rules) : null;
            c.TagFamilies = p.TagFamilies != null ? new Dictionary<string, string>(p.TagFamilies, StringComparer.OrdinalIgnoreCase) : null;
            c.TagDepths = p.TagDepths != null ? new Dictionary<string, int>(p.TagDepths, StringComparer.OrdinalIgnoreCase) : null;
            return c;
        }
    }
}
