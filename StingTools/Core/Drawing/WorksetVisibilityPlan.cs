// StingTools — Drawing Template Manager · V-11
//
// The Revit-free decision behind ViewStylePackApplier.ApplyWorksetVisibility:
// does this pack ask for workset visibility at all, and what does the user
// hear when it does but the document cannot honour it?
//
// Extracted because the ORDER of those two questions was the defect: the
// "document is not workshared — skipped" warning was raised before the pack's
// own (empty) request was checked, so every apply on every non-workshared
// project warned although none of the shipped packs sets worksetVisibility.
// Linked into StingTools.Tags.Tests.

namespace StingTools.Core.Drawing
{
    public enum WorksetVisibilityAction
    {
        /// <summary>The pack states no mode — touch nothing, say nothing.</summary>
        None,
        /// <summary>The pack states a mode the document cannot honour.</summary>
        WarnNotWorkshared,
        /// <summary>Write every user workset's visibility.</summary>
        Apply,
    }

    public static class WorksetVisibilityPlan
    {
        /// <summary>Pack intent first, document capability second.</summary>
        public static WorksetVisibilityAction Decide(string worksetVisibility, bool isWorkshared)
        {
            if (string.IsNullOrWhiteSpace(worksetVisibility)) return WorksetVisibilityAction.None;
            return isWorkshared ? WorksetVisibilityAction.Apply : WorksetVisibilityAction.WarnNotWorkshared;
        }

        /// <summary>"HideAll" hides; any other stated mode shows.</summary>
        public static bool Hides(string worksetVisibility)
            => string.Equals((worksetVisibility ?? "").Trim(), "HideAll", System.StringComparison.OrdinalIgnoreCase);

        public static string NotWorksharedWarning(string packId, string worksetVisibility)
            => $"Pack '{packId}' sets worksetVisibility '{worksetVisibility?.Trim()}', but the document is not workshared — skipped.";
    }
}
