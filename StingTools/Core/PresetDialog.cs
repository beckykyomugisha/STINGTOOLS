// StingTools — result dialogs that stay quiet inside a workflow preset
//
// A command's closing TaskDialog / StingResultPanel is modal. Run by a person that is
// the point; run as a step of a preset (WorkflowEngine.IsRunningPreset) it stops the
// run until someone closes it. These route the same text to the STING log in full and
// to the step's message in short (StepMessage.Summarise), which the workflow report
// prints under the step. Outside a preset they show exactly what they showed before.

using Autodesk.Revit.UI;

namespace StingTools.Core
{
    internal static class PresetDialog
    {
        /// <summary>True when there is nobody to answer a dialog: a preset is running.</summary>
        internal static bool Quiet => WorkflowEngine.IsRunningPreset;

        /// <summary>TaskDialog.Show(title, body) outside a preset; the log and <paramref name="message"/> inside one.</summary>
        internal static void Show(string title, string body, ref string message)
        {
            if (!Quiet) { TaskDialog.Show(title, body); return; }
            StingLog.Info($"{title}: {body}");
            message = StepMessage.Summarise(title, body);
        }

        /// <summary>
        /// <paramref name="panel"/>.Show() outside a preset (returns the clicked action);
        /// inside one the panel's text goes to the log and a summary to
        /// <paramref name="message"/>, and -1 is returned (no action).
        /// </summary>
        internal static int Show(StingTools.UI.StingResultPanel.Builder panel, ref string message)
        {
            if (panel == null) return -1;
            if (!Quiet) return panel.Show();
            var text = StingTools.UI.StingResultPanel.PlainText(panel);
            StingLog.Info(text);
            message = StepMessage.Summarise(null, text);
            return -1;
        }
    }
}
