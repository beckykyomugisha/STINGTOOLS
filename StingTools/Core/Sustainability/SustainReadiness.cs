// StingTools — Sustainability readiness gate (WS I1, I11).
//
// A certification tool must not present office/4A numbers on a project whose
// location and use are unset. This pure gate decides whether the model is ready
// for a defensible run: location (climate site or zone) + building use are the
// BLOCK axes; occupancy + fixtures are softer "incomplete" flags. The dashboard
// shows the banner and refuses to claim an EDGE level when not ready; the model
// health check (I11) surfaces the same readiness so a mis-set project is caught
// before anyone opens the dashboard.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    public class SustainReadinessResult
    {
        public bool LocationSet      { get; set; }
        public bool UseSet           { get; set; }
        public bool OccupancySet     { get; set; }
        public bool FixturesModelled { get; set; }

        /// <summary>The hard gate: a defensible run needs BOTH a location and a use.
        /// When false the dashboard banners "generic proxy, not your project" and does
        /// not claim an EDGE level.</summary>
        public bool Ready => LocationSet && UseSet;

        /// <summary>True when ready AND the softer inputs are present (occupancy +
        /// fixtures) — a fully-resolved run with no indicative-default fallbacks.</summary>
        public bool Complete => Ready && OccupancySet && FixturesModelled;

        public List<string> Missing { get; } = new List<string>();

        /// <summary>A single user-facing line for the dashboard banner / health check.</summary>
        public string Banner { get; set; } = "";
    }

    public static class SustainReadiness
    {
        public static SustainReadinessResult Evaluate(
            bool locationSet, bool useSet, bool occupancySet, bool fixturesModelled)
        {
            var r = new SustainReadinessResult
            {
                LocationSet = locationSet, UseSet = useSet,
                OccupancySet = occupancySet, FixturesModelled = fixturesModelled
            };
            if (!locationSet)      r.Missing.Add("location (climate site or zone)");
            if (!useSet)           r.Missing.Add("building use");
            if (!occupancySet)     r.Missing.Add("occupancy");
            if (!fixturesModelled) r.Missing.Add("plumbing fixtures");

            if (!r.Ready)
                r.Banner = "Location/use not set — figures are a generic proxy, not your project. " +
                           "Set " + string.Join(" + ", BlockMissing(r)) + " in Setup, then re-run.";
            else if (!r.Complete)
                r.Banner = "Indicative — " + string.Join(" + ", r.Missing) +
                           " not modelled; some figures use indicative defaults.";
            else
                r.Banner = "";
            return r;
        }

        /// <summary>WS I11 — compact status-bar / health-check line for the readiness
        /// result. "Sustainability: ready" when complete; otherwise lists what's
        /// missing so a mis-set project is caught before the dashboard is opened.</summary>
        public static string StatusLine(SustainReadinessResult r)
        {
            if (r == null) return "Sustainability: unknown";
            if (r.Complete) return "Sustainability: ready";
            if (!r.Ready)   return "Sustainability: blocked — set " + string.Join(" + ", BlockMissing(r));
            return "Sustainability: indicative — add " + string.Join(" + ", r.Missing);
        }

        private static List<string> BlockMissing(SustainReadinessResult r)
        {
            var m = new List<string>();
            if (!r.LocationSet) m.Add("location (climate site or zone)");
            if (!r.UseSet)      m.Add("building use");
            return m;
        }
    }
}
