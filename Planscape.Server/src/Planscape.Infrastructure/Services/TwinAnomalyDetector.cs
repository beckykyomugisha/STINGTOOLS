namespace Planscape.Infrastructure.Services;

/// <summary>
/// Pillar B (6A, T8) — lightweight anomaly detection. Computes an EWMA mean +
/// variance over recent history and flags the current reading when its z-score
/// exceeds the rule's sigma. Pure + allocation-light so it runs inline on the
/// ingest path; no ML dependency.
/// </summary>
public static class TwinAnomalyDetector
{
    /// <summary>
    /// True when <paramref name="current"/> deviates from the EWMA baseline by
    /// more than <paramref name="sigma"/> standard deviations. Needs at least
    /// <paramref name="minSamples"/> history points; below that returns false
    /// (not enough signal to judge).
    /// </summary>
    public static bool IsAnomaly(
        IReadOnlyList<double> history, double current, double sigma,
        out double zScore, double alpha = 0.3, int minSamples = 8)
    {
        zScore = 0;
        if (history.Count < minSamples) return false;

        // EWMA mean + variance (most-recent-last assumed).
        double mean = history[0];
        double var = 0;
        for (int i = 1; i < history.Count; i++)
        {
            double diff = history[i] - mean;
            double incr = alpha * diff;
            mean += incr;
            var = (1 - alpha) * (var + diff * incr);
        }

        double std = Math.Sqrt(var);
        if (std < 1e-9) return Math.Abs(current - mean) > 1e-6; // flat series, any change is notable
        zScore = (current - mean) / std;
        return Math.Abs(zScore) >= sigma;
    }
}
