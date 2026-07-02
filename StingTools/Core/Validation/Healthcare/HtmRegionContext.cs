// Healthcare Pack — regional HTM variant resolution.
//
// Single resolution point for the active HTM code base. Reads
// PRJ_ORG_HEALTH_HTM_REGION_TXT off ProjectInformation once and returns the
// HtmRegion enum the region-aware HTMStandards lookups accept. Defaults to
// NHS-England when the parameter is unset/blank, so non-healthcare and
// legacy projects are unchanged. Validators call HtmRegionContext.Resolve(doc)
// at the top of Validate() and pass the region into HTMStandards.Get*(…, region)
// rather than each re-reading the parameter.

using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Standards.HTM;
using System;

namespace StingTools.Core.Validation.Healthcare
{
    public static class HtmRegionContext
    {
        /// <summary>Resolves the active HTM region from ProjectInformation.
        /// England on any error / unset value.</summary>
        public static HtmRegion Resolve(Document doc)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_HEALTH_HTM_REGION_TXT");
                string code = (p != null && p.HasValue && p.StorageType == StorageType.String)
                    ? p.AsString() : null;
                return HtmRegionalVariants.ParseRegion(code);
            }
            catch (Exception ex) { StingLog.Warn($"HtmRegionContext.Resolve suppressed: {ex.Message}"); return HtmRegion.England; }
        }

        /// <summary>Human-readable code-base label for audit output.</summary>
        public static string Label(HtmRegion region) => region switch
        {
            HtmRegion.Wales            => "WHTM (Wales)",
            HtmRegion.Scotland         => "SHTM (Scotland)",
            HtmRegion.NorthernIreland  => "HBN/HTM-NI (Northern Ireland)",
            _                          => "NHS England HTM",
        };
    }
}
