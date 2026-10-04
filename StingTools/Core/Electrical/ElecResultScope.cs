// ElecResultScope — which model a cached electrical result belongs to.
// Revit-free: StingTools.Tags.Tests compiles this file.
//
// Fault Current, Feeder Sizing and Arc Flash keep their last results in static lists,
// and the AIC stamp, Arc Flash, the label sheet, the fault schedule and the panel grid
// consume them by ELEMENT ID. The lists did not say which document they came from, so
// a study run on one model was applied to another — a detached copy keeps the same ids,
// so its boards silently took the original's fault levels. Each cache now records the
// key of its document and every consumer refuses a result from another one.

namespace StingTools.Core.Electrical
{
    public static class ElecResultScope
    {
        /// <summary>
        /// The key of a document: its full path, and its title (an unsaved or detached model
        /// has no path, and two of them differ by title). Never the creation GUID, which a
        /// copied file keeps.
        /// </summary>
        public static string Key(string pathName, string title)
            => ((pathName ?? "").Trim() + "|" + (title ?? "").Trim()).ToUpperInvariant();

        /// <summary>A cached result is usable here only when it was made on this document.</summary>
        public static bool Matches(string cachedKey, string currentKey)
            => !string.IsNullOrEmpty(cachedKey) && cachedKey == currentKey;

        /// <summary>Why a cached result is not used, naming the study to re-run.</summary>
        public static string Refusal(string study, string cachedKey)
        {
            if (string.IsNullOrEmpty(cachedKey)) return $"Run {study} on this model first.";
            string title = cachedKey.Contains("|") ? cachedKey.Substring(cachedKey.LastIndexOf('|') + 1) : cachedKey;
            return $"The last {study} was run on another model ({title}); its results are not used here. Run {study} on this model first.";
        }
    }
}
