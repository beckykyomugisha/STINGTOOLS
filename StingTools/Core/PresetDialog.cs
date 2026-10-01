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
        /// The common hand-built result window — a TaskDialog with a main instruction and a
        /// content block — outside a preset; the log and <paramref name="message"/> inside one.
        /// </summary>
        internal static void Show(string title, string instruction, string content, ref string message)
        {
            if (!Quiet)
            {
                new TaskDialog(title) { MainInstruction = instruction, MainContent = content }.Show();
                return;
            }
            StingLog.Info($"{title}: {instruction}\n{content}");
            message = StepMessage.Summarise(title, instruction + "\n" + content);
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

        /// <summary>
        /// The input file a command would ask a person to pick, taken inside a preset from
        /// the step's <c>"params": {"<paramref name="key"/>": "path"}</c>. A relative path is
        /// resolved against the project's _BIM_COORD folder. Returns null — and sets
        /// <paramref name="message"/> naming the param — when it is missing or the file does
        /// not exist: a file is never guessed.
        /// </summary>
        internal static string InputFile(Autodesk.Revit.DB.Document doc, string command, string key,
            string what, ref string message)
        {
            string raw = (WorkflowEngine.StepParam(key) ?? "").Trim().Trim('"');
            if (raw.Length == 0)
            {
                message = $"{command} in a workflow needs \"params\": {{\"{key}\": \"<path to {what}>\"}} on its step " +
                          "(absolute, or relative to the project's _BIM_COORD folder); nothing was read.";
                StingLog.Warn(message);
                return null;
            }
            string path = raw;
            try
            {
                if (!System.IO.Path.IsPathRooted(path) && doc != null)
                    path = System.IO.Path.Combine(StingPaths.Meta(doc, "_BIM_COORD"), raw);
            }
            catch (System.Exception ex) { StingLog.Warn($"{command}: resolving '{raw}': {ex.Message}"); }
            if (!System.IO.File.Exists(path))
            {
                message = $"{command}: params.{key} names '{raw}', but no file exists at {path}; nothing was read.";
                StingLog.Warn(message);
                return null;
            }
            return path;
        }
    }
}
