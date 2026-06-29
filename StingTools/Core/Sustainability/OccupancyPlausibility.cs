// StingTools — Occupancy plausibility sanity flag (WS O1).
//
// A Revit room can carry a default "Number of People" that is wrong for the use
// (e.g. a 170 m² dwelling with 17 people ≈ 10 m²/person — an office density). That
// is a MODEL-DATA artifact, like an implausible material take-off, so STING must
// NOT silently override it — but it FLAGS it the same honest (amber) way it flags
// implausible embodied carbon, so an inflated EUI never reads as confident.
//
// FLAG ONLY — never changes the number. A user's explicit total (source "setup")
// and a sensible modelled value produce no flag. Thresholds are data-driven: the
// expected density is the resolved load profile's occupantDensityM2PerPerson and the
// factor is a documented seed (project-overridable), not a hardcoded magic number.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;

namespace StingTools.Core.Sustainability
{
    public class OccupancyPlausibilityResult
    {
        public bool   Flagged   { get; set; }
        /// <summary>Implausibly DENSE for the use (the priority case — inflates EUI).</summary>
        public bool   IsDense   { get; set; }
        /// <summary>Implausibly SPARSE for the use (lower priority).</summary>
        public bool   IsSparse  { get; set; }
        public int    Occupancy { get; set; }
        public string Source    { get; set; } = "";
        public string Use       { get; set; } = "";
        public double ActualDensityM2PerPerson   { get; set; }
        public double ExpectedDensityM2PerPerson { get; set; }
        /// <summary>Amber NOTE for the dashboard Notes panel ("" when not flagged).</summary>
        public string Message   { get; set; } = "";
    }

    public static class OccupancyPlausibility
    {
        /// <summary>Seed factor: occupancy is "unusually dense" when the actual density
        /// (floor area / people) is below this fraction of the profile's expected density
        /// (residential ~35 m²/p × 0.5 = 17.5; a 10 m²/p office density on a dwelling
        /// trips it). Project-overridable via SustainProjectSetup.OccupancyDenseFactor.</summary>
        public const double DefaultDenseFactor = 0.5;

        /// <summary>Seed factor for the lower-priority "unusually sparse" check (actual
        /// density above this multiple of expected). Overridable; set 0 to disable.</summary>
        public const double DefaultSparseFactor = 4.0;

        /// <summary>Flag (only) an implausible MODELLED occupancy for the resolved use.
        /// Never flags a user-explicit total ("setup") or a missing/derived-from-density
        /// value that already matches the profile. Returns Flagged=false for any input
        /// that isn't a genuine model artifact.</summary>
        public static OccupancyPlausibilityResult Evaluate(
            double floorAreaM2, int occupancy, string source, string use,
            double expectedDensityM2PerPerson,
            double denseFactor = DefaultDenseFactor, double sparseFactor = DefaultSparseFactor)
        {
            var r = new OccupancyPlausibilityResult
            {
                Occupancy = occupancy, Source = source ?? "", Use = string.IsNullOrWhiteSpace(use) ? "this use" : use,
                ExpectedDensityM2PerPerson = expectedDensityM2PerPerson
            };

            // Only a MODEL/derived value can be a wrong-headcount artifact. A user's
            // explicit total is theirs; "none" has nothing to check.
            if (!string.Equals(r.Source, "model", StringComparison.OrdinalIgnoreCase)
                || occupancy <= 0 || floorAreaM2 <= 0 || expectedDensityM2PerPerson <= 0)
                return r;

            double actual = floorAreaM2 / occupancy;
            r.ActualDensityM2PerPerson = actual;

            if (denseFactor > 0 && actual < denseFactor * expectedDensityM2PerPerson)
            {
                r.IsDense = true; r.Flagged = true;
                r.Message = $"Occupancy {occupancy} (source: model) ≈ {actual:0} m²/person — unusually dense for "
                          + $"{r.Use} (profile ~{expectedDensityM2PerPerson:0} m²/p); verify room 'Number of People' "
                          + "or set Occupancy (total) in Setup.";
            }
            else if (sparseFactor > 0 && actual > sparseFactor * expectedDensityM2PerPerson)
            {
                r.IsSparse = true; r.Flagged = true;
                r.Message = $"Occupancy {occupancy} (source: model) ≈ {actual:0} m²/person — unusually sparse for "
                          + $"{r.Use} (profile ~{expectedDensityM2PerPerson:0} m²/p); verify room 'Number of People' "
                          + "or set Occupancy (total) in Setup.";
            }
            return r;
        }
    }
}
