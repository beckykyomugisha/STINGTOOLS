// StingTools — Project occupancy resolver (WS H2).
//
// One building, one population. The energy estimator derives per-zone occupants
// from the load-profile area density when the model carries none; the water
// estimator needs a single project occupancy. Previously water read
// setup.TotalOccupancy directly, so on a model with no typed total the energy
// gate ran on density-derived people while the water gate ran on 0 — two gates,
// two populations. This resolves ONE occupancy fed to both: the user's explicit
// setup total wins when set; otherwise the sum of per-zone occupants (the same
// population energy uses).
//
// Pure POCO — no Revit dependency. Unit-tested.

namespace StingTools.Core.Sustainability
{
    public class OccupancyResolution
    {
        public int    Occupancy { get; set; }
        /// <summary>"setup" (user-entered total), "model" (Σ per-zone occupants
        /// derived from the model / load-profile density), or "none".</summary>
        public string Source    { get; set; } = "none";
    }

    public static class SustainOccupancy
    {
        /// <summary>Resolve the single project occupancy fed to both the energy and
        /// water estimators. WS M2 — only a GENUINELY user-entered total wins ("setup");
        /// an auto-estimate must NOT masquerade as the user's figure. So the priority is:
        /// explicit user total → the model-derived (profile-density) population ("model")
        /// → a non-explicit setup estimate (an estimate, still labelled "model") → none.
        /// The source label reads "model" for any derived/estimated value, "setup" only
        /// for a user-typed one.</summary>
        public static OccupancyResolution Resolve(int setupTotalOccupancy, int zoneDerivedOccupants, bool occupancyIsExplicit)
        {
            if (occupancyIsExplicit && setupTotalOccupancy > 0)
                return new OccupancyResolution { Occupancy = setupTotalOccupancy, Source = "setup" };
            if (zoneDerivedOccupants > 0)
                return new OccupancyResolution { Occupancy = zoneDerivedOccupants, Source = "model" };
            if (setupTotalOccupancy > 0)
                return new OccupancyResolution { Occupancy = setupTotalOccupancy, Source = "model" };
            return new OccupancyResolution { Occupancy = 0, Source = "none" };
        }

        /// <summary>Back-compat 2-arg overload — treats a positive setup total as the
        /// user's explicit figure (legacy callers that don't track the flag).</summary>
        public static OccupancyResolution Resolve(int setupTotalOccupancy, int zoneDerivedOccupants)
            => Resolve(setupTotalOccupancy, zoneDerivedOccupants, occupancyIsExplicit: true);
    }
}
