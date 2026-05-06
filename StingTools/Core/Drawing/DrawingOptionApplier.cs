// StingTools — Drawing Template Manager option-scope step.
//
// Phase 175 — runs as a late step inside DrawingTypePresentation.Apply.
// Resolves a DrawingOptionScope to a concrete option ElementId in the
// active document and writes BuiltInParameter.VIEWER_OPTION_VISIBILITY
// on the view. Falls back gracefully when option/set names cannot be
// resolved — the run continues, surfaces a warning, and leaves the
// view's option visibility unchanged.

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.DesignOptions;

namespace StingTools.Core.Drawing
{
    public static class DrawingOptionApplier
    {
        public class Result
        {
            public bool Applied;
            public string Mode;
            public ElementId BoundOptionId = ElementId.InvalidElementId;
            public string Warning;
        }

        public static Result Apply(Document doc, View view, DrawingType dt)
        {
            var r = new Result();
            if (doc == null || view == null || dt == null) return r;
            if (view.IsTemplate) return r;
            var scope = dt.OptionScope;
            if (scope == null || string.IsNullOrEmpty(scope.Mode)) return r;
            r.Mode = scope.Mode;

            try
            {
                var p = view.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                if (p == null)
                {
                    r.Warning = "view does not expose VIEWER_OPTION_VISIBILITY";
                    return r;
                }

                ElementId target = ElementId.InvalidElementId;

                switch (scope.Mode.Trim().ToLowerInvariant())
                {
                    case "automatic":
                        target = ElementId.InvalidElementId;
                        break;

                    case "primary":
                        // Use the primary of the matching set (by name) if
                        // a setName is supplied, otherwise leave Automatic
                        // (Revit picks the primary of every set).
                        if (!string.IsNullOrEmpty(scope.SetName))
                        {
                            var sets = DesignOptionRegistry.Snapshot(doc);
                            var s = sets.FirstOrDefault(x =>
                                string.Equals(x.Name, scope.SetName, StringComparison.OrdinalIgnoreCase));
                            target = s?.Primary?.OptionId ?? ElementId.InvalidElementId;
                            if (target == ElementId.InvalidElementId)
                                r.Warning = $"primary not found in set '{scope.SetName}'";
                        }
                        break;

                    case "specific":
                        if (string.IsNullOrEmpty(scope.OptionName))
                        {
                            r.Warning = "Specific mode requires optionName";
                            break;
                        }
                        var sets2 = DesignOptionRegistry.Snapshot(doc);
                        DesignOptionSnapshot match = null;
                        foreach (var s in sets2)
                        {
                            if (!string.IsNullOrEmpty(scope.SetName) &&
                                !string.Equals(s.Name, scope.SetName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            var found = s.Options.FirstOrDefault(o =>
                                string.Equals(o.Name, scope.OptionName, StringComparison.OrdinalIgnoreCase));
                            if (found != null) { match = found; break; }
                        }
                        if (match == null)
                        {
                            r.Warning = $"option '{scope.OptionName}' not found"
                                      + (string.IsNullOrEmpty(scope.SetName) ? "" : $" in set '{scope.SetName}'");
                            break;
                        }
                        target = match.OptionId;
                        break;

                    default:
                        r.Warning = $"unknown mode '{scope.Mode}'";
                        break;
                }

                p.Set(target);
                r.BoundOptionId = target;
                r.Applied = true;
            }
            catch (Exception ex)
            {
                r.Warning = "exception: " + ex.Message;
                StingLog.Warn($"DrawingOptionApplier.Apply: {ex.Message}");
            }

            return r;
        }

        /// <summary>Match a routing rule's optionMatches regex against
        /// the caller-supplied option name. Returns true if no predicate
        /// is set or the regex matches.</summary>
        public static bool MatchesOptionPredicate(DrawingRoutingRule rule, string optionName)
        {
            if (rule == null || string.IsNullOrEmpty(rule.OptionMatches)) return true;
            try
            {
                var name = string.IsNullOrEmpty(optionName)
                    ? DesignOptionParams.MAIN_MODEL_LABEL
                    : optionName;
                return System.Text.RegularExpressions.Regex.IsMatch(name, rule.OptionMatches);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatchesOptionPredicate '{rule.OptionMatches}': {ex.Message}");
                return false;
            }
        }
    }
}
