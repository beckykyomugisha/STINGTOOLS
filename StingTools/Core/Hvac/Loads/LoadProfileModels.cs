// StingTools — Load-profile POCOs + data-driven use→profile resolution (WS K1/K2).
//
// Pure POCO — no Revit dependency (the Document-facing loader stays in
// LoadProfileRegistry.cs). Split out so the resolution logic (id → aliases → loose
// → nearest-sibling, never a silent → Office) is unit-tested.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Hvac.Loads
{
    public class LoadProfile
    {
        public string Id    { get; set; } = "";
        public string Label { get; set; } = "";

        public double OccupantDensityM2PerPerson { get; set; } = 10.0;
        public double OccupantSensibleW          { get; set; } = 75;
        public double OccupantLatentW            { get; set; } = 55;
        public double LightingWPerM2             { get; set; } = 7.6;
        public double EquipmentWPerM2            { get; set; } = 8.0;
        public double OaLpsPerPerson             { get; set; } = 10.0;
        public double OaLpsPerM2                 { get; set; } = 0.3;
        public double CoolingSetpointC           { get; set; } = 24;
        public double HeatingSetpointC           { get; set; } = 21;
        public double InfiltrationAch            { get; set; } = 0.3;

        // ── WS K1/K5 — full design-parameter set + provenance ──────────────
        /// <summary>Domestic hot water, litres/person·day (CIBSE Guide G). WS K2 — DHW
        /// now lives on the profile (was a C# switch).</summary>
        public double DhwLPerPersonDay  { get; set; } = 5.0;
        public int    OperatingDaysPerYear { get; set; } = 250;
        /// <summary>The standard each number came from (provenance).</summary>
        public string Source            { get; set; } = "";
        /// <summary>EDGE-app building category this profile maps onto.</summary>
        public string EdgeBuildingType  { get; set; } = "";
        /// <summary>WS K2 — building-use ids that resolve to this profile
        /// (residential/dwelling/house → Residential). Case/space/hyphen-insensitive.</summary>
        public List<string> Aliases     { get; set; } = new List<string>();

        public double[] OccupancySchedule { get; set; } = LoadZone.DefaultOfficeOccupancy();
        public double[] LightingSchedule  { get; set; } = LoadZone.DefaultOfficeLighting();
        public double[] EquipmentSchedule { get; set; } = LoadZone.DefaultOfficeEquipment();

        /// <summary>Apply this profile's values onto a LoadZone.</summary>
        public void ApplyTo(LoadZone z)
        {
            z.OccupantSensibleW = OccupantSensibleW;
            z.OccupantLatentW   = OccupantLatentW;
            z.LightingWPerM2    = LightingWPerM2;
            z.EquipmentWPerM2   = EquipmentWPerM2;
            z.OaLpsPerPerson    = OaLpsPerPerson;
            z.OaLpsPerM2        = OaLpsPerM2;
            z.CoolingSetpointC  = CoolingSetpointC;
            z.HeatingSetpointC  = HeatingSetpointC;
            z.InfiltrationAch   = InfiltrationAch;
            if (OccupancySchedule?.Length == 24) z.OccupancySchedule = OccupancySchedule;
            if (LightingSchedule?.Length  == 24) z.LightingSchedule  = LightingSchedule;
            if (EquipmentSchedule?.Length == 24) z.EquipmentSchedule = EquipmentSchedule;
        }

        /// <summary>Derived occupant count from area + density (people per space).
        /// Clamped at 1 so a small unloaded room still gets a calc; density &lt;= 0
        /// (e.g. Parking) means effectively unoccupied (returns 0).</summary>
        public int OccupantCountFor(double areaM2)
        {
            if (OccupantDensityM2PerPerson <= 0) return 0;
            return Math.Max(1, (int)Math.Round(areaM2 / OccupantDensityM2PerPerson));
        }
    }

    /// <summary>How a building-use id resolved to a load profile (WS K2/K4).</summary>
    public class ProfileResolution
    {
        public LoadProfile Profile     { get; set; }
        /// <summary>direct | alias | loose | nearest | office-default | unset.</summary>
        public string MatchKind        { get; set; } = "direct";
        /// <summary>True for nearest / office-default / unset — surface as a visible
        /// fallback (never a silent office swap).</summary>
        public bool   IsFallback       { get; set; }
        public string RequestedUse     { get; set; } = "";
        /// <summary>"residential → Office" style note for the dashboard NOTES line.</summary>
        public string FromTo => $"{(string.IsNullOrWhiteSpace(RequestedUse) ? "(unset)" : RequestedUse)} → {Profile?.Id}";
    }

    public class LoadProfileLibrary
    {
        public Dictionary<string, LoadProfile> ById { get; }
            = new Dictionary<string, LoadProfile>(StringComparer.OrdinalIgnoreCase);

        private static string Norm(string s)
            => (s ?? "").Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

        /// <summary>Resolve a profile by id with case-insensitive fuzzy fallback (legacy
        /// callers).</summary>
        public LoadProfile Get(string id) => ResolveForUse(id).Profile;

        /// <summary>WS K2 — data-driven resolution of a building-use id to a load
        /// profile: direct id → alias → loose id → nearest sibling (alias substring) →
        /// Office default. The result flags a fallback so a gap surfaces, never a silent
        /// office swap. No per-use value is hardcoded — aliases live in the data.</summary>
        public ProfileResolution ResolveForUse(string useId)
        {
            LoadProfile Office() => ById.TryGetValue("Office", out var o) ? o
                : (ById.Values.FirstOrDefault() ?? new LoadProfile { Id = "Office" });

            if (string.IsNullOrWhiteSpace(useId))
                return new ProfileResolution { Profile = Office(), MatchKind = "unset", IsFallback = true, RequestedUse = useId };

            // 1) direct id
            if (ById.TryGetValue(useId, out var direct))
                return new ProfileResolution { Profile = direct, MatchKind = "direct", RequestedUse = useId };

            string n = Norm(useId);

            // 2) exact alias (normalized)
            foreach (var p in ById.Values)
                if (p.Aliases != null && p.Aliases.Any(a => Norm(a) == n))
                    return new ProfileResolution { Profile = p, MatchKind = "alias", RequestedUse = useId };

            // 3) loose id (normalized)
            foreach (var p in ById.Values)
                if (Norm(p.Id) == n)
                    return new ProfileResolution { Profile = p, MatchKind = "loose", RequestedUse = useId };

            // 4) nearest sibling — longest alias that is a substring of (or contains) the
            //    requested use. Data-driven (uses the aliases), not a hardcoded map.
            LoadProfile best = null; int bestLen = 0;
            foreach (var p in ById.Values)
            {
                if (p.Aliases == null) continue;
                foreach (var a in p.Aliases)
                {
                    string na = Norm(a);
                    if (na.Length == 0) continue;
                    if ((n.Contains(na) || na.Contains(n)) && na.Length > bestLen) { best = p; bestLen = na.Length; }
                }
            }
            if (best != null)
                return new ProfileResolution { Profile = best, MatchKind = "nearest", IsFallback = true, RequestedUse = useId };

            // 5) Office default — flagged, never silent.
            return new ProfileResolution { Profile = Office(), MatchKind = "office-default", IsFallback = true, RequestedUse = useId };
        }
    }
}
