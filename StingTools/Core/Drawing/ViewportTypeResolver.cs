// StingTools — Drawing Template Manager · viewport type resolution
//
// The one Revit-bound lookup for a viewport type by name, used by
// SheetPlacementBridge (and through it DrawingProducer), SheetTemplateEngine,
// SheetManagerEngineExt.AutoAssignViewportTypes and DrawingTypeValidator.
//
// Resolution order (StingViewportTypes.Candidates): the requested name, then
// for a STING name its canonical form and legacy aliases. When none exists
// and creation is allowed, a CANONICAL STING name is minted by duplicating an
// existing viewport type (Viewport types cannot be created from nothing) and
// the caller is told, because the new type carries the source type's
// graphics until someone styles it. A non-STING name is never minted.
//
// NOT verified in Revit: ElementType.Duplicate on a viewport type, inside the
// callers' transactions.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class ViewportTypeResolver
    {
        /// <summary>Every viewport ElementType in the document, by name (first wins).</summary>
        public static Dictionary<string, ElementType> Index(Document doc)
        {
            var map = new Dictionary<string, ElementType>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return map;
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>())
            {
                if (!IsViewportType(t)) continue;
                var n = t.Name;
                if (!string.IsNullOrEmpty(n) && !map.ContainsKey(n)) map[n] = t;
            }
            return map;
        }

        private static bool IsViewportType(ElementType t)
        {
            try
            {
                if (t.Category?.Id?.Value == (long)BuiltInCategory.OST_Viewports) return true;
                return string.Equals(t.FamilyName, "Viewport", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ViewportTypeResolver.IsViewportType", $"ViewportTypeResolver: {t?.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>True when the name, or an alias of it, exists. No creation.</summary>
        public static bool Exists(Document doc, string name, Dictionary<string, ElementType> index = null)
        {
            index = index ?? Index(doc);
            return StingViewportTypes.Candidates(name).Any(index.ContainsKey);
        }

        /// <summary>
        /// Resolve <paramref name="name"/> to a viewport type id, trying aliases.
        /// With <paramref name="createIfMissing"/> (requires an open transaction)
        /// a missing canonical STING name is created by duplication. Returns
        /// InvalidElementId when nothing resolves; the reason is added to
        /// <paramref name="warnings"/> (or logged when that is null).
        /// </summary>
        public static ElementId Resolve(Document doc, string name, bool createIfMissing,
            ICollection<string> warnings = null, Dictionary<string, ElementType> index = null)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            index = index ?? Index(doc);

            foreach (var candidate in StingViewportTypes.Candidates(name))
                if (index.TryGetValue(candidate, out var hit))
                {
                    if (!string.Equals(candidate, name.Trim(), StringComparison.OrdinalIgnoreCase))
                        StingLog.Info($"ViewportTypeResolver: '{name}' resolved to existing '{candidate}'.");
                    return hit.Id;
                }

            var canonical = StingViewportTypes.CanonicalFor(name);
            if (!createIfMissing || canonical == null)
            {
                Report(warnings, $"Viewport type '{name}' not found — the viewport keeps its current type.");
                return ElementId.InvalidElementId;
            }

            // Prefer duplicating the STANDARD STING type when it exists, else
            // any viewport type (the project's default, typically "Title w Line").
            ElementType source = null;
            if (!string.Equals(canonical, StingViewportTypes.Standard, StringComparison.Ordinal))
                foreach (var c in StingViewportTypes.Candidates(StingViewportTypes.Standard))
                    if (index.TryGetValue(c, out source)) break;
            source = source ?? index.Values.FirstOrDefault();
            if (source == null)
            {
                Report(warnings, $"Viewport type '{canonical}' not found and the document has no viewport type to duplicate.");
                return ElementId.InvalidElementId;
            }
            try
            {
                var created = source.Duplicate(canonical) as ElementType;
                if (created == null)
                {
                    Report(warnings, $"Viewport type '{canonical}' could not be created from '{source.Name}'.");
                    return ElementId.InvalidElementId;
                }
                index[canonical] = created;
                Report(warnings, $"Viewport type '{canonical}' did not exist; created it by duplicating '{source.Name}'. " +
                                 "It carries that type's title / line settings until styled.");
                return created.Id;
            }
            catch (Exception ex)
            {
                Report(warnings, $"Viewport type '{canonical}' could not be created from '{source.Name}': {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static void Report(ICollection<string> warnings, string msg)
        {
            if (warnings != null) warnings.Add(msg);
            StingLog.Warn("ViewportTypeResolver: " + msg);
        }
    }
}
