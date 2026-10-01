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

        // ── Inputs (choices, files, scopes, milestones) ─────────────────────────────────
        //
        // Results are one thing (above: quiet in ANY preset). Inputs follow a different rule,
        // the same for every input a KUT step takes — a picker, a mode, a file, a scope, a
        // milestone, a CDE status:
        //   1. The step's "params" value, when set, is used — attended or not, so a workflow
        //      author can always pin it. A value that is set but invalid fails the step.
        //   2. Missing, and a person is present (CanAsk: a button click or an ATTENDED preset
        //      run): the original prompt or picker is shown, exactly as before.
        //   3. Missing, and the run is unattended: the step fails naming the param. An input
        //      is never defaulted on someone's behalf.

        /// <summary>
        /// True when a person is there to answer an input prompt: anything but an unattended
        /// preset run (<see cref="WorkflowEngine.IsUnattended"/>). See the rule above.
        /// </summary>
        internal static bool CanAsk => !WorkflowEngine.IsUnattended;

        /// <summary>The running step's "params" value for <paramref name="key"/>, trimmed; "" when unset or outside a preset.</summary>
        internal static string Param(string key) => (WorkflowEngine.StepParam(key) ?? "").Trim();

        /// <summary>
        /// Rule 3: sets <paramref name="message"/> to "<paramref name="command"/> needs
        /// params.<paramref name="key"/>" and logs it. The caller returns Result.Failed.
        /// </summary>
        internal static void MissingParam(string command, string key, string example, string effect, ref string message)
        {
            message = $"{command} in an unattended workflow needs \"params\": {{\"{key}\": {example}}} on its step; {effect}";
            StingLog.Warn(message);
        }

        /// <summary>
        /// An input file under the rule above. params.<paramref name="key"/> when set (absolute,
        /// or relative to the project's _BIM_COORD folder; a missing file fails). Otherwise
        /// <paramref name="ask"/> — the command's own picker, kept in the command so the
        /// unattended gate can see it — when a person is present; otherwise a failure naming
        /// the param. Returns null with <paramref name="stop"/> = Cancelled (the person
        /// cancelled the picker) or Failed (<paramref name="message"/> says why).
        /// </summary>
        internal static string InputFile(Autodesk.Revit.DB.Document doc, string command, string key,
            string what, System.Func<string> ask, ref string message, out Result stop)
        {
            stop = Result.Failed;
            string raw = Param(key).Trim('"');
            if (raw.Length == 0)
            {
                if (CanAsk && ask != null)
                {
                    string picked = ask();
                    if (string.IsNullOrEmpty(picked)) stop = Result.Cancelled;
                    return string.IsNullOrEmpty(picked) ? null : picked;
                }
                message = $"{command} in an unattended workflow needs \"params\": {{\"{key}\": \"<path to {what}>\"}} on its step " +
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
