// StingTools — pre-edit Design Option context assertions.
//
// Revit silently lets you author elements while a different option is
// the active one — the elements simply land in the wrong place. This
// helper provides a fail-fast assertion before any STING command that
// authors model elements.
//
// Also wraps the worksharing collision warning: under workshared
// models, the FIRST edit on any element in an option locks the entire
// option's content to that user. We surface that as a TaskDialog so
// the user can defer or coordinate.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.DesignOptions
{
    public static class OptionContext
    {
        public enum AssertionResult
        {
            Match,           // active option matches expected
            MismatchAbort,   // user chose to abort
            MismatchProceed  // user chose to continue anyway
        }

        /// <summary>Assert the active option matches the expected option.
        /// Pass ElementId.InvalidElementId for "main model only".</summary>
        public static AssertionResult Assert(
            Document doc,
            ElementId expectedOptionId,
            string commandLabel)
        {
            if (doc == null) return AssertionResult.Match;
            var actual = DesignOptionRegistry.ActiveOptionId(doc);
            if (actual == null) actual = ElementId.InvalidElementId;
            if (expectedOptionId == null) expectedOptionId = ElementId.InvalidElementId;
            if (actual == expectedOptionId) return AssertionResult.Match;

            string actualName = actual == ElementId.InvalidElementId
                ? DesignOptionParams.MAIN_MODEL_LABEL
                : (doc.GetElement(actual)?.Name ?? actual.ToString());
            string expectedName = expectedOptionId == ElementId.InvalidElementId
                ? DesignOptionParams.MAIN_MODEL_LABEL
                : (doc.GetElement(expectedOptionId)?.Name ?? expectedOptionId.ToString());

            var td = new TaskDialog("STING — Active Option Mismatch")
            {
                MainInstruction = $"{commandLabel}: active option does not match",
                MainContent =
                    $"This command expects '{expectedName}' to be the active option, "
                    + $"but Revit is currently editing '{actualName}'.\n\n"
                    + "Authoring under the wrong active option will land elements in the "
                    + "wrong place. Switch the active option in the status bar dropdown, "
                    + "or proceed with the current context if you know what you're doing."
            };
            td.AllowCancellation = true;
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Abort and switch active option");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Proceed under the wrong context (advanced)");
            var r = td.Show();
            return r == TaskDialogResult.CommandLink2
                ? AssertionResult.MismatchProceed
                : AssertionResult.MismatchAbort;
        }

        /// <summary>Worksharing collision warning. Returns true if the user
        /// agreed to proceed.</summary>
        public static bool ConfirmWorksharingMove(Document doc, string optionName)
        {
            try
            {
                if (doc == null || !doc.IsWorkshared) return true;

                var td = new TaskDialog("STING — Workshared Move-To-Option")
                {
                    MainInstruction = $"Moving elements into '{optionName}'",
                    MainContent =
                        "Under worksharing, the FIRST edit on any element in a design "
                        + "option locks the entire option's contents to the editor. "
                        + "Coordinate with the team before proceeding — you may end up "
                        + "owning every element another user later places in this option.\n\n"
                        + "Proceed?",
                    AllowCancellation = true,
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                return td.Show() == TaskDialogResult.Yes;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConfirmWorksharingMove: {ex.Message}");
                return true;
            }
        }
    }
}
