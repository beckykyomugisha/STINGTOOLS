// StingTools — Per-Design-Option sustainability comparison (WS I12).
//
// Ranks Revit Design Options by carbon intensity (the dimension options usually
// vary — fabric/structure) so a user can pick the greenest. Reuses the carbon the
// existing OptionCostCarbonCalculator already computes per option; this pure layer
// just ranks + picks. EUI/water are whole-building and shown for context.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Sustainability
{
    public class OptionMetric
    {
        public string Set     { get; set; } = "";
        public string Option  { get; set; } = "";
        public bool   IsPrimary { get; set; }
        public double TotalCarbonKg { get; set; }
        public double AreaM2  { get; set; }
        public double CarbonIntensityKgM2 => AreaM2 > 0 ? TotalCarbonKg / AreaM2 : 0;
    }

    public class OptionComparison
    {
        public List<OptionMetric> Ranked { get; } = new List<OptionMetric>();
        /// <summary>The lowest-carbon-intensity option (null when none have carbon).</summary>
        public OptionMetric Greenest { get; set; }
        /// <summary>The current primary option, for "greenest vs primary" framing.</summary>
        public OptionMetric Primary  { get; set; }
    }

    public static class SustainOptionComparison
    {
        /// <summary>Rank options by carbon intensity (ascending — greenest first) and
        /// pick the greenest. Options with no carbon (no area) sort last and are not
        /// eligible to be "greenest".</summary>
        public static OptionComparison ByCarbon(IEnumerable<OptionMetric> options)
        {
            var res = new OptionComparison();
            var list = (options ?? Enumerable.Empty<OptionMetric>()).Where(o => o != null).ToList();
            res.Ranked.AddRange(list
                .OrderByDescending(o => o.CarbonIntensityKgM2 > 0)   // ones with a value first
                .ThenBy(o => o.CarbonIntensityKgM2));
            res.Greenest = res.Ranked.FirstOrDefault(o => o.CarbonIntensityKgM2 > 0);
            res.Primary  = list.FirstOrDefault(o => o.IsPrimary);
            return res;
        }
    }
}
