// StingTools — the Revit half of IsoLevelCode (DTW-116).
//
// IsoLevelCode.BuildMap is Revit-free and takes StoreyDatum rows. This reads a
// document's levels into those rows WITH Revit's "Building Story" flag
// (LEVEL_IS_BUILDING_STORY), so a datum level such as "T.O. Steel" does not take a
// storey number and shift every code above it. Callers that build the ISO level map
// (DrawingProducer.BuildIsoLevelMap, ParameterHelpers' level map) should read their
// rows here rather than leaving IsBuildingStorey unknown.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class IsoLevelStoreys
    {
        /// <summary>Every named level in <paramref name="doc"/> as a StoreyDatum
        /// (elevation in mm, Building Story flag when the parameter reads).</summary>
        public static List<StoreyDatum> FromDocument(Document doc)
        {
            var storeys = new List<StoreyDatum>();
            if (doc == null) return storeys;
            foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (string.IsNullOrWhiteSpace(l?.Name)) continue;
                bool? isStorey = null;
                try
                {
                    var p = l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Integer) isStorey = p.AsInteger() != 0;
                }
                catch (Exception ex) { StingLog.Warn($"IsoLevelStoreys '{l.Name}': {ex.Message}"); }
                storeys.Add(new StoreyDatum
                {
                    Name = l.Name,
                    ElevationMm = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters),
                    IsBuildingStorey = isStorey,
                });
            }
            return storeys;
        }

        /// <summary>The ISO level map for <paramref name="doc"/>, coincident levels grouped
        /// and non-storey levels left out of the count.</summary>
        public static Dictionary<string, string> BuildMap(Document doc) => IsoLevelCode.BuildMap(FromDocument(doc));
    }
}
