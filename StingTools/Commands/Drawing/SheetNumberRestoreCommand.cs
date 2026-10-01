// StingTools — Drawing Template Manager · undo an ISO renumber
//
// WHY THIS FILE EXISTS
// --------------------
// Sheet_NumberFromIso writes every from -> to pair to sheet_number_history.json
// and told the operator the move was reversible. Nothing could reverse it. A
// record with no reader is a promise, not a mechanism, and I shipped the promise
// first — which is the same defect as reporting a write that never happened.
//
// It also turned out to be needed rather than theoretical. SHT_TAG_1_TXT is
// assembled FROM sheet.SheetNumber, so making the sheet number the ISO code
// feeds the identifier back into its own input: each TagSheets run re-embeds the
// previous code and the number grows. A real export filename after one round
// already read
//   SAH--ZZ-L01-DR-PROJECTN-PROJECTN-ORGANI-L01-LG-COORD-A-L1-001-1-S2-P01
// with the project code in twice.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetNumberRestoreCommand : IExternalCommand
    {
        private const string HistoryFile = "sheet_number_history.json";

        private sealed class Change { public string from { get; set; } public string to { get; set; } }
        private sealed class Entry
        {
            public string when { get; set; }
            public string source { get; set; }
            public List<Change> changes { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers", "No active document.");
                return Result.Failed;
            }

            // A FAMILY document has no project, so ProjectFolderEngine.GetDataPath
            // returns null, StingPaths.Meta returns null, and Path.Combine(null, ...)
            // throws "Value cannot be null" — which is what this reported instead of
            // the actual situation. Editing a title-block seed in the Family Editor
            // and reaching for a sheet command is an easy and reasonable mistake; it
            // deserves a sentence, not an exception message.
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    "This is a FAMILY document, not a project.\n\n" +
                    "Sheet numbers live in a project. Switch to the project window and " +
                    "run this again.");
                return Result.Cancelled;
            }
            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    "This model has never been saved, so it has no project folder to " +
                    "read or write the change record in.\n\nSave it and run this again.");
                return Result.Cancelled;
            }

            string dir = null;
            try { dir = StingPaths.Meta(doc, "_BIM_COORD"); }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetNumberRestore: resolving the coordination folder: {ex.Message}");
            }
            if (string.IsNullOrEmpty(dir))
            {
                // Meta RETURNS NULL rather than throwing when the project root cannot be
                // resolved, so the old code fed null to Path.Combine and surfaced
                // "Value cannot be null" — an exception message standing in for a
                // diagnosis. Say what is actually wrong.
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    "This project has no resolvable coordination folder, so there is no " +
                    "change record to restore from.\n\n" +
                    "That happens when the model is unsaved or detached. Save it where the " +
                    "rest of the project lives and run this again.");
                return Result.Failed;
            }
            string path = Path.Combine(dir, HistoryFile);

            if (!File.Exists(path))
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    $"No {HistoryFile} in this project's coordination folder.\n\n{path}\n\n"
                    + "Nothing to restore — Sheet No <- ISO has not run here, or its record "
                    + "could not be written (which it would have reported at the time).");
                return Result.Succeeded;
            }

            List<Entry> log;
            try { log = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    $"{HistoryFile} could not be read: {ex.Message}\n\nNothing was changed.");
                return Result.Failed;
            }

            // DTW-200: the restore itself is recorded (SheetNumbering.Apply writes history),
            // so the entry to reverse is the last one that is not a restore.
            var last = log?.LastOrDefault(e => e?.changes != null && e.changes.Count > 0
                && !string.Equals(e.source, TransactionName, StringComparison.Ordinal));
            if (last == null)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    $"{HistoryFile} holds no recorded changes.");
                return Result.Succeeded;
            }

            // Match on the CURRENT number, not on a stored element id: sheets may have
            // been renumbered again by hand since, and restoring onto the wrong sheet
            // would be worse than not restoring at all.
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && !string.IsNullOrEmpty(s.SheetNumber))
                .ToList();
            var byKey = sheets.ToDictionary(s => s.Id.Value.ToString(), s => s, StringComparer.Ordinal);
            var keyByNumber = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sheets) keyByNumber[s.SheetNumber] = s.Id.Value.ToString();

            // DTW-200: validated against the live numbers. After Ctrl+Z of the renumber the
            // record still lists renames that are gone, and a sheet now carrying a recorded
            // "to" may be a different sheet that always had it — restoring it collided with
            // the sheet that holds its "from" again.
            var restorePlan = SheetRestorePlanner.Build(
                last.changes.Where(c => c != null).Select(c => new KeyValuePair<string, string>(c.from, c.to)),
                keyByNumber);

            if (restorePlan.Moves.Count == 0)
            {
                var why = new StringBuilder();
                why.AppendLine($"None of the {last.changes.Count} recorded sheet(s) can be restored safely.");
                if (restorePlan.LooksUndone > 0)
                    why.AppendLine("\nThe renumber looks already undone (Ctrl+Z): the old numbers are back on their sheets.");
                why.AppendLine();
                foreach (var m in restorePlan.Skipped.Take(10)) why.AppendLine("  " + m);
                TaskDialog.Show("STING — Restore Sheet Numbers", why.ToString());
                return Result.Cancelled;
            }

            var preview = new StringBuilder();
            preview.AppendLine($"Recorded {last.when} from {last.source}");
            preview.AppendLine();
            foreach (var p in restorePlan.Moves.Take(25)) preview.AppendLine($"  {p.Current}\n      ->  {p.Restore}");
            if (restorePlan.Skipped.Count > 0)
            {
                preview.AppendLine();
                preview.AppendLine($"{restorePlan.Skipped.Count} recorded sheet(s) will be left alone:");
                foreach (var m in restorePlan.Skipped.Take(10)) preview.AppendLine("  " + m);
                if (restorePlan.LooksUndone > 0)
                    preview.AppendLine("Part of the renumber looks already undone (Ctrl+Z).");
            }

            var td = new TaskDialog("STING — Restore Sheet Numbers")
            {
                MainInstruction = $"Restore {restorePlan.Moves.Count} sheet number(s) to what they were?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Restore {restorePlan.Moves.Count} sheet number(s)");
            if (td.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            // DTW-200: through SheetNumbering.Apply — park on a temporary number, put a
            // refused sheet back on its own number (the old two-pass left it on
            // "~STINGUNDO~000N"), count nothing unless Revit committed, rebuild the ISO
            // identifier and record the restore.
            var changes = restorePlan.Moves
                .Where(m => byKey.ContainsKey(m.SheetKey))
                .Select(m => new SheetNumbering.Change { Sheet = byKey[m.SheetKey], Old = m.Current, New = m.Restore })
                .ToList();
            var outcome = SheetNumbering.Apply(doc, changes, TransactionName);
            foreach (var c in changes.Where(c => string.Equals(c.Sheet.SheetNumber, c.New, StringComparison.Ordinal)))
                StingLog.Info($"SheetNumberRestore: '{c.Old}' -> '{c.New}'");

            var sb = new StringBuilder();
            sb.AppendLine($"Restored : {outcome.Done}");
            if (outcome.Failed > 0)
            {
                sb.AppendLine($"Failed   : {outcome.Failed} (each kept the number it had)");
                foreach (var f in outcome.Failures.Take(10)) sb.AppendLine(f);
            }
            if (restorePlan.Skipped.Count > 0) sb.AppendLine($"Left alone: {restorePlan.Skipped.Count} (see the preview reasons)");
            if (outcome.Done > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"ISO identifiers rebuilt on {outcome.Retagged} sheet(s).");
                foreach (var f in outcome.RetagFailures.Take(5)) sb.AppendLine(f);
                sb.AppendLine("Re-stamp the QR codes — they are keyed on the sheet number.");
            }

            StingLog.Info($"SheetNumberRestore: {outcome.Done} restored, {outcome.Failed} failed, {restorePlan.Skipped.Count} skipped");
            TaskDialog.Show("STING — Restore Sheet Numbers", sb.ToString());
            return outcome.Failed > 0 && outcome.Done == 0 ? Result.Failed : Result.Succeeded;
        }

        private const string TransactionName = "STING Restore Sheet Numbers";
    }
}
