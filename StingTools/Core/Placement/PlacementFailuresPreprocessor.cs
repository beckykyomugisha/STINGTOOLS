// Phase 139.25 — silently dismiss the predictable Revit failures that
// the placement engine knowingly produces during a Place run:
//
//   - "Can't rotate element into this position" (BuiltInFailures.GeneralFailures.CannotRotateElementWarn)
//     Raised by ElementTransformUtils.RotateElement when an un-hosted
//     family rotation would push it through another element.  Engine
//     leaves the family un-rotated; the failure is informational.
//
//   - "There are identical instances in the same place" (the dedup
//     pass already catches these; the warning is from edge-cases the
//     dedup didn't cover; demote to a silent skip).
//
//   - "Instance origin does not lie on host face" (Phase 139.23 face-
//     plane projection eliminates most of these; the few remaining
//     are when Revit's face geometry doesn't match the projection).
//
// Without this pre-processor, Revit shows the user a modal dialog
// per failure and BLOCKS the transaction commit unless the user
// dismisses every error explicitly.  That's why "31 placed" reports
// were still showing zero placements in the model.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public class PlacementFailuresPreprocessor : IFailuresPreprocessor
    {
        // Failure definition Ids that the engine recognises as
        // "predictable side-effect — dismiss". Anything else is left
        // alone for Revit's normal failure UI.
        private static readonly HashSet<string> SuppressGuids = new HashSet<string>
        {
            // Revit reports these as informational warnings — string
            // matched on the failure message because the BuiltInFailureGuids
            // are not all publicly exposed in the SDK.
        };

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var f in failures)
            {
                string desc = "";
                try { desc = f.GetDescriptionText() ?? ""; } catch { }
                bool suppress =
                    desc.IndexOf("Can't rotate element", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("identical instances",  System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("does not lie on host face", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("origin does not lie",  System.StringComparison.OrdinalIgnoreCase) >= 0
                 // Phase 139.26 — additional patterns that were stripping
                 // post-commit. Each one was observed in a user run where
                 // Revit's modal blocked the engine's transaction.
                 || desc.IndexOf("slightly off axis",    System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("off axis",              System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("does not have a host",  System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("hosted instance",       System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("would be moved off",    System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("lies outside its host", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("offset from element",   System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("Cannot keep elements joined", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("could not create work plane", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("Element is partially or completely outside",  System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("would extend",          System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("not perpendicular",     System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("highlighted",           System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("Element extends below", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || desc.IndexOf("instance being created has", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (suppress)
                {
                    accessor.DeleteWarning(f);
                    StingLog.Info($"PlacementFailuresPreprocessor: suppressed '{desc}'");
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
