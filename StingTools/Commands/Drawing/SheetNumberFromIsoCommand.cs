// StingTools — Drawing Template Manager · sheet number from the ISO 19650 code
//
// WHY THIS FILE EXISTS
// --------------------
// The DRG NO. cell prints Revit's Sheet Number — "A-L1-003" — while the full
// ISO 19650 document identifier already exists on the sheet as SHT_TAG_1_TXT
// ("PROJECTN-ORGANI-L01-DR-COORD-A-L1-003-1"), assembled by TagSheets. A project
// that issues against ISO 19650 wants the identifier on the paper, not the
// shorthand.
//
// There are two ways to get it there and they are NOT equivalent:
//
//   * Re-bind the DRG NO. label in the .rfa to SHT_TAG_1_TXT. Nothing else
//     changes. Revit's sheet number stays short, and short is what it should be
//     — it is a KEY, used by viewport references, schedules, browser
//     organisation and export filenames, and it must stay unique.
//
//   * Change Revit's Sheet Number itself. That is what this command does,
//     because it is what was asked for, but it is the heavier option: every
//     one of those downstream uses now carries a 40-character string, and any
//     external reference to the old number (a transmittal already issued, a
//     consultant's markup, a file on a CDE) no longer matches.
//
// So this refuses to be casual about it. It offers a dry run first, names every
// change before making it, writes a from -> to record so the move is reversible,
// and will not proceed if two sheets would end up with the same number.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetNumberFromIsoCommand : IExternalCommand
    {
        /// <summary>Breadcrumb file holding every from -> to pair, so the move is
        /// reversible.
        ///
        /// NOT a parameter. The obvious choice, PRJ_SHEET_PREV_NUMBER_TXT, does not
        /// exist in MR_PARAMETERS.txt -- SetString would have returned false, the
        /// count would have been discarded, and the dialog would have promised
        /// reversibility that was never written anywhere. Same shape as
        /// _data/.sting_consolidation.json, which exists for the same reason.</summary>
        private const string HistoryFile = "sheet_number_history.json";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Sheet Number from ISO", "No active document.");
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
                TaskDialog.Show("STING — Sheet Number from ISO",
                    "This is a FAMILY document, not a project.\n\n" +
                    "Sheet numbers live in a project. Switch to the project window and " +
                    "run this again.");
                return Result.Cancelled;
            }
            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("STING — Sheet Number from ISO",
                    "This model has never been saved, so it has no project folder to " +
                    "read or write the change record in.\n\nSave it and run this again.");
                return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var plan = new List<(ViewSheet sheet, string from, string to)>();
            var noCode = new List<string>();
            var unchanged = new List<string>();

            foreach (var s in sheets)
            {
                string iso = ParameterHelpers.GetString(s, ParamRegistry.SHT_TAG_1);
                if (string.IsNullOrWhiteSpace(iso)) { noCode.Add(s.SheetNumber); continue; }
                iso = iso.Trim();
                if (string.Equals(iso, s.SheetNumber, StringComparison.Ordinal))
                { unchanged.Add(s.SheetNumber); continue; }
                plan.Add((s, s.SheetNumber, iso));
            }

            if (plan.Count == 0)
            {
                TaskDialog.Show("STING — Sheet Number from ISO",
                    noCode.Count > 0
                        ? $"None of the {sheets.Count} sheet(s) can be renumbered.\n\n"
                          + $"{noCode.Count} carry no {ParamRegistry.SHT_TAG_1} — run CREATE TAGS → "
                          + "Tag Sheets first, which assembles the ISO 19650 code from the sheet's "
                          + "discipline, form, level, originator and revision."
                        : "Every sheet already uses its ISO 19650 code as its sheet number.");
                return Result.Succeeded;
            }

            // A duplicate target would make the whole run fail halfway, leaving the
            // set half-renumbered. Detect it BEFORE touching anything.
            var dupes = plan.GroupBy(p => p.to, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1)
                            .ToList();
            if (dupes.Count > 0)
            {
                var sb0 = new StringBuilder();
                sb0.AppendLine("Two or more sheets would end up with the SAME number, and Revit");
                sb0.AppendLine("requires sheet numbers to be unique. Nothing was changed.");
                sb0.AppendLine();
                foreach (var g in dupes.Take(10))
                    sb0.AppendLine($"  {g.Key}\n      from: {string.Join(", ", g.Select(x => x.from))}");
                sb0.AppendLine();
                sb0.AppendLine("The ISO code ends with the revision, so two sheets at the same");
                sb0.AppendLine("revision with the same discipline/level/type collide. Give them");
                sb0.AppendLine("distinct SHT_SEQ values and re-run Tag Sheets.");
                TaskDialog.Show("STING — Sheet Number from ISO", sb0.ToString());
                return Result.Failed;
            }

            var preview = new StringBuilder();
            preview.AppendLine($"{plan.Count} sheet(s) would be renumbered:");
            preview.AppendLine();
            foreach (var p in plan.Take(25))
                preview.AppendLine($"  {p.from,-14} ->  {p.to}");
            if (plan.Count > 25) preview.AppendLine($"  … and {plan.Count - 25} more");
            if (noCode.Count > 0)
            {
                preview.AppendLine();
                preview.AppendLine($"{noCode.Count} sheet(s) have no {ParamRegistry.SHT_TAG_1} and will be left alone.");
            }

            var td = new TaskDialog("STING — Sheet Number from ISO")
            {
                MainInstruction = $"Replace {plan.Count} sheet number(s) with the ISO 19650 code?",
                MainContent =
                    "Revit's sheet number is a KEY, not just a caption: viewport references, "
                    + "schedules, browser organisation and export filenames all use it. After this "
                    + "they will carry the full identifier, and any reference to the old number "
                    + "from OUTSIDE this model — an issued transmittal, a consultant's markup, a "
                    + "file already on the CDE — will no longer match.\n\n"
                    + "Every from -> to pair is written to " + HistoryFile + " in the project's "
                    + "coordination folder, so the move can be reversed.\n\n"
                    + "IT ALSO FEEDS THE IDENTIFIER BACK INTO ITS OWN INPUT: "
                    + ParamRegistry.SHT_TAG_1 + " is assembled FROM the sheet number, so after "
                    + "this, Tag Sheets would nest the code inside itself. That is now refused "
                    + "rather than compounded, but it means the ISO code stops being rebuilt "
                    + "from the sheet's tokens — it freezes at whatever it says today.\n\n"
                    + "If you only want the identifier PRINTED, cancel and re-bind the DRG NO. "
                    + "label in the title-block family to " + ParamRegistry.SHT_TAG_1 + " instead "
                    + "— that changes the drawing without changing the key.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show me the full list first (changes nothing)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Renumber {plan.Count} sheet(s) now");

            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1)
            {
                TaskDialog.Show("STING — Sheet Number from ISO (dry run)", preview.ToString());
                return Result.Succeeded;
            }
            if (choice != TaskDialogResult.CommandLink2) return Result.Cancelled;

            int done = 0;
            var failed = new List<string>();

            using (var t = new Transaction(doc, "STING Sheet Number from ISO"))
            {
                t.Start();

                // TWO PASSES, via a sentinel. A one-pass rename collides the moment a
                // target equals another sheet's current number — Revit throws, and the
                // set is left half-renumbered with no clean way back.
                string sentinel = "~STINGTMP~";
                int i = 0;
                foreach (var p in plan)
                {
                    try { p.sheet.SheetNumber = sentinel + (i++).ToString("D4"); }
                    catch (Exception ex)
                    {
                        failed.Add($"{p.from}: could not stage ({ex.Message})");
                        StingLog.Warn($"SheetNumberFromIso stage '{p.from}': {ex.Message}");
                    }
                }

                foreach (var p in plan)
                {
                    try
                    {
                        p.sheet.SheetNumber = p.to;
                        done++;
                        StingLog.Info($"SheetNumberFromIso: '{p.from}' -> '{p.to}'");
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{p.from} -> {p.to}: {ex.Message}");
                        StingLog.Warn($"SheetNumberFromIso '{p.from}': {ex.Message}");
                    }
                }

                t.Commit();
            }

            string historyPath = null;
            if (done > 0)
            {
                try
                {
                    string dir = StingPaths.Meta(doc, "_BIM_COORD");
                    System.IO.Directory.CreateDirectory(dir);
                    historyPath = System.IO.Path.Combine(dir, HistoryFile);

                    var log = new List<object>();
                    if (System.IO.File.Exists(historyPath))
                    {
                        var existing = Newtonsoft.Json.JsonConvert
                            .DeserializeObject<List<object>>(System.IO.File.ReadAllText(historyPath));
                        if (existing != null) log.AddRange(existing);
                    }
                    log.Add(new
                    {
                        when = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                        source = ParamRegistry.SHT_TAG_1,
                        changes = plan.Where(x => !failed.Any(f => f.StartsWith(x.from + " ", StringComparison.Ordinal)))
                                      .Select(x => new { from = x.from, to = x.to }).ToList(),
                    });
                    System.IO.File.WriteAllText(historyPath,
                        Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented));
                }
                catch (Exception ex)
                {
                    // The renumber HAS happened. Saying nothing here would leave the
                    // operator believing a reversal record exists when it does not.
                    historyPath = null;
                    StingLog.Warn($"SheetNumberFromIso: could not write {HistoryFile}: {ex.Message}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Renumbered : {done}");
            if (failed.Count > 0)
            {
                sb.AppendLine($"Failed     : {failed.Count}");
                sb.AppendLine();
                foreach (var f in failed.Take(12)) sb.AppendLine("  " + f);
                sb.AppendLine();
                sb.AppendLine("A sheet left on a ~STINGTMP~ number failed its second pass — set it");
                sb.AppendLine("by hand, or undo the whole command (Ctrl+Z) and fix the cause first.");
            }
            if (done > 0)
            {
                sb.AppendLine();
                sb.AppendLine(historyPath != null
                    ? "Reversal record: " + historyPath
                    : "WARNING: the reversal record could NOT be written (see the STING log). "
                      + "The renumber has happened; the from -> to pairs are in the log only.");
                sb.AppendLine();
                sb.AppendLine("Re-stamp the QR codes: the payload is keyed on the sheet number and");
                sb.AppendLine("every existing code now points at the old one.");
            }

            StingLog.Info($"SheetNumberFromIso: {done} renumbered, {failed.Count} failed");
            TaskDialog.Show("STING — Sheet Number from ISO", sb.ToString());
            return failed.Count > 0 && done == 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
