// StingTools — Sustainability staleness state (WS I13).
//
// The pure, testable flag the SustainStaleUpdater (Revit IUpdater) flips when the
// model changes and the dashboard clears after a fresh run. Kept Revit-free so the
// transition logic is unit-tested without the updater plumbing.

namespace StingTools.Core.Sustainability
{
    public static class SustainStaleState
    {
        /// <summary>True when an element feeding the dashboard changed since the last
        /// run — the panel reads this to signal the result is out of date.</summary>
        public static bool IsStale { get; private set; }
        public static string Reason { get; private set; } = "";

        public static void MarkStale(string reason)
        {
            IsStale = true;
            Reason = reason ?? "";
        }

        public static void MarkFresh()
        {
            IsStale = false;
            Reason = "";
        }
    }
}
