// StingTools — Drawing Template Manager · a view setting its view template owns (DT-R11)
//
// When a view carries a template that controls Detail Level (or View Scale), Revit
// refuses a direct write with "Detail Level cannot be modified." That is not a failure
// of the drawing type: the template is the authority for that setting. The presentation
// step decides here whether to write the setting or leave it to the template, so the
// rule is unit-tested and the producer never reports the template doing its job as an
// error.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

namespace StingTools.Core.Drawing
{
    internal static class TemplateOwnedSetting
    {
        /// <summary>
        /// True when the view's template owns the setting and the presentation step must
        /// not write it. A view with no template never defers. With a template, either
        /// signal is enough: the template lists the parameter as controlled, or Revit
        /// reports the view's parameter read-only (which is how a controlled parameter
        /// presents on the view).
        /// </summary>
        internal static bool OwnedByTemplate(bool viewHasTemplate, bool templateControls, bool viewParamReadOnly)
            => viewHasTemplate && (templateControls || viewParamReadOnly);

        /// <summary>The log line that replaces the old failure note.</summary>
        internal static string Note(string setting, string wanted, string viewName, string templateName)
            => $"{setting} {wanted} not written to '{viewName ?? "?"}': its view template "
             + $"'{(string.IsNullOrWhiteSpace(templateName) ? "?" : templateName)}' controls it.";
    }
}
