// StingTools — what counts as "modelled" on a level (DTW-207)
//
// The presence collectors that decide which levels get a plan (MEP disciplines, "skip
// levels with nothing modelled", linked models) counted every element in the document:
// one in a SECONDARY design option — an alternative not in the scheme — or one that is
// demolished produced a plan of a level whose built scheme has nothing on it. Presence
// now counts the main model and each option set's primary option, and leaves out
// elements with a demolished phase. The same rule in the host and in each linked model.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Mep
{
    internal static class ModelPresenceFilter
    {
        /// <summary>
        /// A quick filter passing the main model and primary design options only — every
        /// secondary option excluded — or null when <paramref name="doc"/> has no secondary
        /// option (nothing to exclude).
        /// </summary>
        internal static ElementFilter ExcludeSecondaryOptions(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var secondary = new FilteredElementCollector(doc).OfClass(typeof(DesignOption))
                    .Cast<DesignOption>().Where(o => !o.IsPrimary).Select(o => o.Id).ToList();
                if (secondary.Count == 0) return null;
                var filters = secondary.Select(id => (ElementFilter)new ElementDesignOptionFilter(id, true)).ToList();
                return filters.Count == 1 ? filters[0] : new LogicalAndFilter(filters);
            }
            catch (Exception ex)
            {
                // Cannot read the options: count everything (the behaviour before) and say so.
                StingLog.Warn($"ModelPresenceFilter design options ({doc.Title}): {ex.Message} — secondary options counted.");
                return null;
            }
        }

        /// <summary><paramref name="collector"/> with secondary design options excluded.</summary>
        internal static FilteredElementCollector MainAndPrimary(FilteredElementCollector collector, Document doc)
        {
            var f = ExcludeSecondaryOptions(doc);
            return f == null ? collector : collector.WherePasses(f);
        }

        /// <summary>True when <paramref name="el"/> carries a demolished phase — it is not part
        /// of what the drawings show as built.</summary>
        internal static bool IsDemolished(Element el)
        {
            try
            {
                var p = el?.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (p == null || p.StorageType != StorageType.ElementId) return false;
                var id = p.AsElementId();
                return id != null && id != ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ModelPresenceFilter demolished {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
