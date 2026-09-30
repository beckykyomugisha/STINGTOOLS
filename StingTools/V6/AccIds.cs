// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIds.cs
//
// The one place that knows which form of an ACC project id each API family wants, and
// how a project's hosting region is told to the APIs that need it.
//
// Data Management (project/v1, data/v1) uses the "b."-prefixed form it returns from
// GET /project/v1/hubs/{hub}/projects. The ACC APIs under construction/* and the
// Model Coordination services (bim360/modelset, bim360/clash) use the bare GUID. The
// discovery button stores whatever Data Management returned, verbatim, so without this
// every Issues and clash call made with a discovered id would 404 - a documented,
// deterministic conversion, not a guess about what the user meant.
//
// Revit-free and log-free.

using System;
using System.Net.Http;

namespace StingTools.V6
{
    public static class AccIds
    {
        /// <summary>Bare GUID form for construction/* and Model Coordination.</summary>
        public static string ForAcc(string id)
        {
            id = (id ?? string.Empty).Trim();
            return id.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? id.Substring(2) : id;
        }

        /// <summary>"b."-prefixed form for Data Management (project/v1, data/v1).</summary>
        public static string ForDataManagement(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0) return id;
            return id.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? id : "b." + id;
        }

        /// <summary>The ACC hosting regions (acc/v1/overview/acc-regions; a hub's
        /// attributes.region uses the same codes).</summary>
        public static readonly string[] KnownRegions = { "US", "CAN", "EMEA", "GBR", "DEU", "IND", "JPN", "AUS" };

        /// <summary>Normalise a region name, or empty for the default (US: send nothing).</summary>
        public static string NormaliseRegion(string region)
        {
            string r = (region ?? string.Empty).Trim().ToUpperInvariant();
            switch (r)
            {
                case "": case "US": return string.Empty;
                case "EU": case "EUROPE": return "EMEA";
                case "AU": case "AUSTRALIA": return "AUS";
                case "UK": return "GBR";
                default: return r;
            }
        }

        /// <summary>Tell an ACC API which region hosts the project (x-ads-region). APS routes an
        /// unlabelled request automatically, at some latency, so this is an optimisation and a
        /// correctness hint, not a requirement; US projects send nothing, as before.</summary>
        public static void ApplyRegion(HttpRequestMessage req, string region)
        {
            string r = NormaliseRegion(region);
            if (req == null || r.Length == 0) return;
            req.Headers.TryAddWithoutValidation("x-ads-region", r);
        }
    }
}
