// ══════════════════════════════════════════════════════════════════════════
//  RateSourceLabels.cs — single source of truth for legacy RateSource labels.
//
//  PM-7 hygiene: the provider-id → legacy-label map was duplicated verbatim in
//  BOQCostManager.MapProviderIdToLegacySource and CostStamp.MapProviderIdToLegacySource.
//  Both now delegate here so the mapping can never drift between the bill build
//  and the element stamp. Pure — no Revit, no deps.
// ══════════════════════════════════════════════════════════════════════════
namespace StingTools.BOQ.Rates
{
    public static class RateSourceLabels
    {
        /// <summary>Map a rate-provider id to the legacy RateSource label that
        /// heat-maps and schedules built against the old shape still expect.</summary>
        public static string ToLegacy(string providerId)
        {
            switch (providerId ?? "")
            {
                case "param-override": return "Override";
                case "es-override":    return "Override";
                case "csv-default":    return "CSV";
                case "cobie-typemap":  return "COBie";
                case "default-baseline": return "Default";
                default:               return providerId ?? "None";
            }
        }
    }
}
