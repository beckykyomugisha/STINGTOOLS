// StingTools — Seeds_Build: which rebuild mode runs inside a workflow preset
//
// Run by a person, Seeds_Build asks (Missing Only / Rebuild Unfinalized / Rebuild All)
// in a modal TaskDialog. Inside a preset there is nobody to answer, so the mode comes
// from the step's params.mode, and Missing Only when it is absent — the only mode that
// never replaces a family the project already holds. An unknown value fails the step
// with the reason rather than quietly running some other mode.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Symbols
{
    public static class SeedBuildPresetMode
    {
        public const string MissingOnly = "MissingOnly";
        public const string RebuildUnfinalized = "RebuildUnfinalized";
        public const string RebuildAll = "RebuildAll";

        /// <summary>
        /// The canonical mode for a preset step: <see cref="MissingOnly"/> when
        /// <paramref name="stepParam"/> is blank, the matching mode when it names one
        /// (case-insensitive; spaces, '-' and '_' ignored), else null with
        /// <paramref name="error"/> saying which values are accepted.
        /// </summary>
        public static string Resolve(string stepParam, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(stepParam)) return MissingOnly;
            var key = stepParam.Trim().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (key.Equals(MissingOnly, StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Missing", StringComparison.OrdinalIgnoreCase)) return MissingOnly;
            if (key.Equals(RebuildUnfinalized, StringComparison.OrdinalIgnoreCase)) return RebuildUnfinalized;
            if (key.Equals(RebuildAll, StringComparison.OrdinalIgnoreCase)) return RebuildAll;
            error = $"params.mode '{stepParam.Trim()}' is not MissingOnly, RebuildUnfinalized or RebuildAll.";
            return null;
        }
    }
}
