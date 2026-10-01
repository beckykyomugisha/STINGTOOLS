namespace StingTools.Core
{
    /// <summary>
    /// TAGACC-11 rules for copying a token from the nearest tagged neighbour, without the
    /// Revit API so they can be tested (TAGACC-21). <c>CopyTokensFromNearest</c> reads
    /// levels, points and sources; the decisions are made here.
    /// </summary>
    internal static class ProximityRule
    {
        /// <summary>Same floor when neither element has a level: within this height (feet).</summary>
        public const double SameFloorToleranceFt = 5.0;

        /// <summary>
        /// Same floor: both on a level → the same level; otherwise the vertical separation is
        /// within <see cref="SameFloorToleranceFt"/>. A level id of 0 or less means "no level".
        /// </summary>
        public static bool SameFloor(long elementLevelId, long candidateLevelId, double dzFt)
        {
            bool both = elementLevelId > 0 && candidateLevelId > 0;
            if (both) return elementLevelId == candidateLevelId;
            return System.Math.Abs(dzFt) <= SameFloorToleranceFt;
        }

        /// <summary>
        /// A neighbour's LOC may be copied only if it was DETECTED for the neighbour — the
        /// High band of <see cref="TokenConfidenceBands.ClassifyLoc"/> (type override, room,
        /// workset, scope box). A default, the Project Information value or another proximity
        /// copy would spread a guess while recording it as "Proximity". A blank source (tagged
        /// before sources were recorded) is accepted, as it always was.
        /// </summary>
        public static bool LocIsCopyable(string source)
            => string.IsNullOrWhiteSpace(source)
               || TokenConfidenceBands.ClassifyLoc(source).Band == ConfidenceBand.High;

        /// <summary>ZONE: as <see cref="LocIsCopyable"/>, using the ZONE bands.</summary>
        public static bool ZoneIsCopyable(string source)
            => string.IsNullOrWhiteSpace(source)
               || TokenConfidenceBands.ClassifyZone(source).Band == ConfidenceBand.High;

        /// <summary>SYS / FUNC: not from the category (6) or discipline (7) fallback; 0 (unrecorded) accepted.</summary>
        public static bool SysIsCopyable(int detectLayer)
            => detectLayer < 6;
    }
}
