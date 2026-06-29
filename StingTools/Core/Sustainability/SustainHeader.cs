// StingTools — Dashboard header composition (WS I8).
//
// The dashboard subtitle showed "office · zone · 170 m²" — a "zone" label with no
// value when the climate zone was unset. This builds the subtitle from only the
// fields that have a value, and reflects whether the use was actually resolved.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    public static class SustainHeader
    {
        /// <summary>Compose the dashboard subtitle, omitting any field without a value
        /// (no empty "zone" placeholder) and labelling an unresolved use honestly.</summary>
        public static string Subtitle(string use, bool useResolved, string climateZone, double floorAreaM2, int occupancy)
        {
            var parts = new List<string> { "Indicative estimate" };
            parts.Add(useResolved && !string.IsNullOrWhiteSpace(use) ? use : "use not set");
            if (!string.IsNullOrWhiteSpace(climateZone)) parts.Add("zone " + climateZone.Trim());
            if (floorAreaM2 > 0) parts.Add($"{floorAreaM2:0} m²");
            if (occupancy > 0)   parts.Add($"occ {occupancy}");
            return string.Join(" · ", parts);
        }
    }
}
