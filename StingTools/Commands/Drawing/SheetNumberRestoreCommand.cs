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

            var last = log?.LastOrDefault(e => e?.changes != null && e.changes.Count > 0);
            if (last == null)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    $"{HistoryFile} holds no recorded changes.");
                return Result.Succeeded;
            }

            // Match on the CURRENT number, not on a stored element id: sheets may have
            // been renumbered again by hand since, and restoring onto the wrong sheet
            // would be worse than not restoring at all.
            var bySheetNumber = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToDictionary(s => s.SheetNumber ?? "", s => s, StringComparer.OrdinalIgnoreCase);

            var plan = new List<(ViewSheet sheet, string from, string to)>();
            var missing = new List<string>();
            foreach (var c in last.changes)
            {
                if (string.IsNullOrWhiteSpace(c?.to) || string.IsNullOrWhiteSpace(c.from)) continue;
                if (bySheetNumber.TryGetValue(c.to, out var s)) plan.Add((s, c.to, c.from));
                else missing.Add($"{c.to}  (was {c.from})");
            }

            if (plan.Count == 0)
            {
                TaskDialog.Show("STING — Restore Sheet Numbers",
                    $"None of the {last.changes.Count} recorded sheet(s) still carry the number they "
                    + "were given, so there is nothing safe to restore.\n\n"
                    + "They were renumbered again afterwards. Restoring by name would land on the "
                    + "wrong sheets.");
                return Result.Cancelled;
            }

            var preview = new StringBuilder();
            preview.AppendLine($"Recorded {last.when} from {last.source}");
            preview.AppendLine();
            foreach (var p in plan.Take(25)) preview.AppendLine($"  {p.from}\n      ->  {p.to}");
            if (missing.Count > 0)
            {
                preview.AppendLine();
                preview.AppendLine($"{missing.Count} recorded sheet(s) no longer carry their renumbered value "
                    + "and will be left alone:");
                foreach (var m in missing.Take(10)) preview.AppendLine("  " + m);
            }

            var td = new TaskDialog("STING — Restore Sheet Numbers")
            {
                MainInstruction = $"Restore {plan.Count} sheet number(s) to what they were?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Restore {plan.Count} sheet number(s)");
            if (td.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            int done = 0;
            var failed = new List<string>();
            using (var t = new Transaction(doc, "STING Restore Sheet Numbers"))
            {
                t.Start();
                // Same two-pass sentinel as the forward direction: a restore target can
                // equal another sheet's current number just as easily.
                string sentinel = "~STINGUNDO~";
                int i = 0;
                foreach (var p in plan)
                {
                    try { p.sheet.SheetNumber = sentinel + (i++).ToString("D4"); }
                    catch (Exception ex) { failed.Add($"{p.from}: stage failed ({ex.Message})"); }
                }
                foreach (var p in plan)
                {
                    try { p.sheet.SheetNumber = p.to; done++; StingLog.Info($"SheetNumberRestore: '{p.from}' -> '{p.to}'"); }
                    catch (Exception ex) { failed.Add($"{p.from} -> {p.to}: {ex.Message}"); }
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Restored : {done}");
            if (failed.Count > 0)
            {
                sb.AppendLine($"Failed   : {failed.Count}");
                foreach (var f in failed.Take(10)) sb.AppendLine("  " + f);
            }
            if (done > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Re-run Tag Sheets to rebuild SHT_TAG_1_TXT from the short number, then");
                sb.AppendLine("re-stamp the QR codes — both are keyed on the sheet number.");
            }

            StingLog.Info($"SheetNumberRestore: {done} restored, {failed.Count} failed");
            TaskDialog.Show("STING — Restore Sheet Numbers", sb.ToString());
            return failed.Count > 0 && done == 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
