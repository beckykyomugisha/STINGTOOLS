// ══════════════════════════════════════════════════════════════════════════
//  IRateProvider.cs — P0 of the Cost Management Implementation Plan.
//
//  Abstracts unit-rate lookup so BOQCostManager.ResolveRate is no longer a
//  hard-coded 5-pass chain. Concrete providers (parameter override, CSV,
//  COBie type-map, Scheduling4DEngine default, future BCIS/Spon's HTTP,
//  project-specific rate card) implement this interface and register with
//  RateProviderRegistry. Priority decides order; first non-null wins.
//
//  See docs/COST_MANAGEMENT_IMPLEMENTATION_PLAN.md § P0 for the design.
// ══════════════════════════════════════════════════════════════════════════
using System;
using Autodesk.Revit.DB;

namespace StingTools.BOQ.Rates
{
    /// <summary>
    /// Inputs to a rate lookup. Built once per element by BOQCostManager
    /// and passed unchanged to every provider so providers can match on
    /// whichever fields they understand.
    /// </summary>
    public class RateRequest
    {
        /// <summary>Revit category display name — e.g. "Walls", "Pipes".</summary>
        public string CategoryName { get; set; } = "";

        /// <summary>STING discipline code — e.g. "M", "E", "P", "S", "A".</summary>
        public string Discipline { get; set; } = "";

        /// <summary>STING PROD code — e.g. "AHU", "DB", "DR".</summary>
        public string ProdCode { get; set; } = "";

        /// <summary>MAT_CODE on the element — e.g. "CONC-C30", "STL-S355".</summary>
        public string MatCode { get; set; } = "";

        /// <summary>Unit hint when known up-front (m², m³, m, kg, each).</summary>
        public string Unit { get; set; } = "";

        /// <summary>Currency caller wants the rate in (ISO 4217).</summary>
        public string CurrencyCode { get; set; } = "UGX";

        /// <summary>Pricing reference date — drives FX + inflation indexing.</summary>
        public DateTime AsOf { get; set; } = DateTime.UtcNow;

        /// <summary>Location code — drives RICS location factor lookup.</summary>
        public string LocationCode { get; set; } = "";

        /// <summary>Project id (server-scoped) when known.</summary>
        public string ProjectId { get; set; } = "";

        /// <summary>The Revit element being costed. Providers may inspect for parameter overrides etc.</summary>
        public Element Element { get; set; }
    }

    /// <summary>
    /// Output of a rate lookup. Null indicates no match and the registry
    /// continues to the next provider.
    /// </summary>
    public class RateLookup
    {
        /// <summary>Unit rate in <see cref="CurrencyCode"/>.</summary>
        public double UnitRate { get; set; }

        /// <summary>Currency of the returned rate (ISO 4217).</summary>
        public string CurrencyCode { get; set; } = "UGX";

        /// <summary>Quantity unit the rate is per — "m²", "m³", "m", "kg", "each", "item".</summary>
        public string Unit { get; set; } = "each";

        /// <summary>Stable provider id — e.g. "csv-default", "param-override", "bcis-http".</summary>
        public string SourceId { get; set; } = "";

        /// <summary>UTC time the rate was fetched (cache provenance).</summary>
        public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>0–100 confidence score (Phase 11A). Surfaces in BOQHealthScore.</summary>
        public int Confidence { get; set; } = 60;

        /// <summary>Human-readable description for the audit trail.</summary>
        public string Provenance { get; set; } = "";

        /// <summary>Optional matched key (category name, PROD code, MAT_CODE) — useful for logging.</summary>
        public string MatchedKey { get; set; } = "";
    }

    /// <summary>
    /// One source of unit rates. Implementations should be stateless or
    /// internally synchronised — the registry calls Resolve concurrently
    /// when batching across thousands of elements.
    /// </summary>
    public interface IRateProvider
    {
        /// <summary>Stable id — used for logging + rate-source heat-map.</summary>
        string Id { get; }

        /// <summary>
        /// Higher wins. Convention:
        /// 100 = explicit user override (parameter or ES),
        /// 90  = CSV category match,
        /// 85  = CSV PROD match,
        /// 75  = COBie type-map,
        /// 60  = Scheduling4DEngine baseline,
        /// 50  = external (BCIS / Spon's),
        /// 40  = project-specific rate card.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// True if this provider needs network access. The registry skips
        /// network providers when offline and surfaces a clear log line.
        /// </summary>
        bool RequiresNetwork { get; }

        /// <summary>
        /// Resolve the request. Return null on no match — the registry
        /// continues to the next provider. Must never throw under normal
        /// conditions; log and return null on internal failure.
        /// </summary>
        RateLookup Resolve(RateRequest req);
    }
}
