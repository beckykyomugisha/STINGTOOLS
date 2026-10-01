// StingTools — Drawing Template Manager
//
// ManagedTemplateFields — the Revit-free half of ManagedTemplateSyncer's
// "which template parameters does a managed pack control" decision.
//
// DTW-163: the syncer used to map only scalar fields (detail level, discipline,
// visual style, …) to a BuiltInParameter. vgOverrides / filters /
// worksetVisibility / viewRange mapped to nothing ("no single BIP"), so the
// complement it handed to SetNonControlledTemplateParameterIds RELEASED the
// template's V/G control. Every view assigned a managed template then showed
// its own V/G — no pack overrides, no filters — while the template carried
// them invisibly. And most managed packs never listed vgOverrides / filters in
// managedFields at all, so the template did not even carry them.
//
// Two rules now:
//   1. Each field names the template parameters it governs, by
//      BuiltInParameter member name (checked against RevitAPI.dll by
//      ManagedTemplateFieldsTests through the BuiltInParameterNames fixture).
//   2. A pack that carries a payload (category overrides, filter rules,
//      workset visibility, view range)
//      controls the matching parameter whether or not it listed the field —
//      a payload the template does not control is a payload no view shows.
//      Link overrides are element overrides, which a template never controls,
//      so they are applied to each view directly and only a pack that LISTS
//      linkOverrides puts the template in charge of the links tab.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    internal static class ManagedTemplateFields
    {
        /// <summary>Managed field → the BuiltInParameter members the template must control for it.</summary>
        internal static readonly IReadOnlyDictionary<string, string[]> FieldBipNames =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["detailLevel"]       = new[] { "VIEW_DETAIL_LEVEL" },
            ["discipline"]        = new[] { "VIEW_DISCIPLINE" },
            ["visualStyle"]       = new[] { "MODEL_GRAPHICS_STYLE" },
            ["phaseFilter"]       = new[] { "VIEW_PHASE_FILTER" },
            ["phase"]             = new[] { "VIEW_PHASE" },
            ["annotationCrop"]    = new[] { "VIEWER_ANNOTATION_CROP_ACTIVE" },
            ["farClip"]           = new[] { "VIEWER_BOUND_OFFSET_FAR" },
            ["underlay"]          = new[] { "VIEW_UNDERLAY_BOTTOM_ID" },
            ["vgOverrides"]       = new[] { "VIS_GRAPHICS_MODEL", "VIS_GRAPHICS_ANNOTATION" },
            ["filters"]           = new[] { "VIS_GRAPHICS_FILTERS" },
            ["worksetVisibility"] = new[] { "VIS_GRAPHICS_WORKSETS" },
            ["linkOverrides"]     = new[] { "VIS_GRAPHICS_RVT_LINKS" },
            ["viewRange"]         = new[] { "PLAN_VIEW_RANGE" },
        };

        /// <summary>
        /// DTW-170: fields a pack may list that the template must NOT control.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> NeverControlled =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scale"] = "VIEW_SCALE belongs to the drawing type (DrawingType.Scale), not the pack; "
                      + "a template that controls it pins every assigned view to the seed view's scale.",
        };

        /// <summary>
        /// The fields a managed pack actually controls: what it declares (or the
        /// syncer's defaults), minus <see cref="NeverControlled"/>, plus every
        /// field whose payload the pack carries, minus <see cref="NeverControlled"/>.
        /// Order-stable, de-duplicated. <paramref name="ignored"/> receives each
        /// declared field that was refused.
        /// </summary>
        internal static List<string> Effective(IEnumerable<string> declared,
            bool hasVgOverrides, bool hasFilters, bool hasWorksetVisibility,
            bool hasViewRange, Action<string> ignored = null)
        {
            var result = new List<string>();
            void Add(string f) { if (!result.Contains(f, StringComparer.Ordinal)) result.Add(f); }

            foreach (var f in declared ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                if (NeverControlled.ContainsKey(f)) { ignored?.Invoke(f); continue; }
                Add(f);
            }
            if (hasVgOverrides)       Add("vgOverrides");
            if (hasFilters)           Add("filters");
            if (hasWorksetVisibility) Add("worksetVisibility");
            if (hasViewRange)         Add("viewRange");
            return result;
        }

        /// <summary>All BuiltInParameter member names the given fields govern.</summary>
        internal static List<string> BipNamesFor(IEnumerable<string> fields)
        {
            var names = new List<string>();
            foreach (var f in fields ?? Enumerable.Empty<string>())
                if (f != null && FieldBipNames.TryGetValue(f, out var bips))
                    foreach (var b in bips)
                        if (!names.Contains(b, StringComparer.Ordinal)) names.Add(b);
            return names;
        }
    }
}
