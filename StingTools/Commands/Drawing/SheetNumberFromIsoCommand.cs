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
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetNumberFromIsoCommand : IExternalCommand
    {
        // The from -> to record is written by SheetNumbering.Apply to
        // sheet_number_history.json (SheetNumbering.HistoryFile) — one file shared
        // with Renumber, Tidy and Restore. NOT a parameter: PRJ_SHEET_PREV_NUMBER_TXT
        // does not exist in MR_PARAMETERS.txt, so a SetString there wrote nothing.
        private const string Title = "STING — Sheet Number from ISO";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                PresetDialog.Show(Title, "No active document.", ref message);
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
                PresetDialog.Show(Title,
                    "This is a FAMILY document, not a project.\n\n" +
                    "Sheet numbers live in a project. Switch to the project window and " +
                    "run this again.", ref message);
                return Result.Cancelled;
            }
            if (string.IsNullOrEmpty(doc.PathName))
            {
                PresetDialog.Show(Title,
                    "This model has never been saved, so it has no project folder to " +
                    "read or write the change record in.\n\nSave it and run this again.", ref message);
                return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var byId = new Dictionary<string, ViewSheet>(StringComparer.Ordinal);
            var candidates = new List<DrawingQaRules.IsoRenumberCandidate>();
            var noCode = new List<string>();

            foreach (var s in sheets)
            {
                string iso = ParameterHelpers.GetString(s, ParamRegistry.SHT_TAG_1);
                if (string.IsNullOrWhiteSpace(iso)) { noCode.Add(s.SheetNumber); continue; }
                var id = s.Id.Value.ToString();
                byId[id] = s;
                candidates.Add(new DrawingQaRules.IsoRenumberCandidate
                {
                    Id = id,
                    Current = s.SheetNumber,
                    Target = iso.Trim(),
                    // DTW-11: a style-locked sheet keeps its number — the old
                    // command renamed it regardless.
                    Locked = DrawingTypeStamper.IsLocked(s),
                });
            }

            // DTW-11: planned against EVERY sheet number, not only the targets.
            var plan = DrawingQaRules.PlanIsoRenumber(candidates, sheets.Select(s => s.SheetNumber));

            // A duplicate target would make the run fail halfway. Detect it BEFORE
            // touching anything.
            if (plan.Duplicates.Count > 0)
            {
                var sb0 = new StringBuilder();
                sb0.AppendLine("Two or more sheets would end up with the SAME number, and Revit");
                sb0.AppendLine("requires sheet numbers to be unique. Nothing was changed.");
                sb0.AppendLine();
                foreach (var g in plan.Duplicates.Take(10))
                    sb0.AppendLine($"  {g.Key}\n      from: {string.Join(", ", g.Value)}");
                sb0.AppendLine();
                sb0.AppendLine("The ISO code ends with the revision, so two sheets at the same");
                sb0.AppendLine("revision with the same discipline/level/type collide. Give them");
                sb0.AppendLine("distinct SHT_SEQ values and re-run Tag Sheets.");
                PresetDialog.Show(Title, sb0.ToString(), ref message);
                return Result.Failed;
            }

            var skipped = new StringBuilder();
            if (plan.Locked.Count > 0)
            {
                skipped.AppendLine($"{plan.Locked.Count} style-locked sheet(s) keep their numbers: "
                    + string.Join(", ", plan.Locked.Take(8)) + (plan.Locked.Count > 8 ? " …" : ""));
            }
            if (plan.Held.Count > 0)
            {
                skipped.AppendLine($"{plan.Held.Count} sheet(s) left unchanged — their ISO code is already another sheet's number:");
                foreach (var h in plan.Held.Take(8)) skipped.AppendLine("  " + h);
                if (plan.Held.Count > 8) skipped.AppendLine($"  … and {plan.Held.Count - 8} more");
            }
            if (noCode.Count > 0)
                skipped.AppendLine($"{noCode.Count} sheet(s) have no {ParamRegistry.SHT_TAG_1} and will be left alone.");

            if (plan.Moves.Count == 0)
            {
                PresetDialog.Show(Title,
                    candidates.Count == 0 && noCode.Count > 0
                        ? $"None of the {sheets.Count} sheet(s) can be renumbered.\n\n"
                          + $"{noCode.Count} carry no {ParamRegistry.SHT_TAG_1} — run CREATE TAGS → "
                          + "Tag Sheets first, which assembles the ISO 19650 code from the sheet's "
                          + "discipline, form, level, originator and revision."
                        : "No sheet needs renumbering.\n\n" + skipped, ref message);
                return Result.Succeeded;
            }

            var preview = new StringBuilder();
            preview.AppendLine($"{plan.Moves.Count} sheet(s) would be renumbered:");
            preview.AppendLine();
            foreach (var p in plan.Moves.Take(25))
                preview.AppendLine($"  {p.Current,-14} ->  {p.Target}");
            if (plan.Moves.Count > 25) preview.AppendLine($"  … and {plan.Moves.Count - 25} more");
            if (skipped.Length > 0)
            {
                preview.AppendLine();
                preview.Append(skipped);
            }

            if (PresetDialog.Quiet)
            {
                // DTW-11: the command-link dialog waited for a click nobody could give
                // inside a preset. Replacing every sheet number is not something a
                // preset should do by default: it plans (a dry run) unless the step
                // says params.apply = "true".
                if (!IsTrue(WorkflowEngine.StepParam("apply")))
                {
                    PresetDialog.Show(Title + " (dry run)",
                        "Plan only — nothing renumbered (set the step's params.apply to \"true\" to apply it).\n\n"
                        + preview, ref message);
                    return Result.Succeeded;
                }
                StingLog.Info($"{Title}: preset step applies the plan (params.apply).\n{preview}");
            }
            else
            {
                var td = new TaskDialog(Title)
                {
                    MainInstruction = $"Replace {plan.Moves.Count} sheet number(s) with the ISO 19650 code?",
                    MainContent =
                        "Revit's sheet number is a KEY, not just a caption: viewport references, "
                        + "schedules, browser organisation and export filenames all use it. After this "
                        + "they will carry the full identifier, and any reference to the old number "
                        + "from OUTSIDE this model — an issued transmittal, a consultant's markup, a "
                        + "file already on the CDE — will no longer match.\n\n"
                        + "Every from -> to pair is written to " + SheetNumbering.HistoryFile + " in the project's "
                        + "coordination folder, so the move can be reversed (Sheet_NumberRestore).\n\n"
                        + "IT ALSO FEEDS THE IDENTIFIER BACK INTO ITS OWN INPUT: "
                        + ParamRegistry.SHT_TAG_1 + " is assembled FROM the sheet number, so after "
                        + "this, Tag Sheets would nest the code inside itself. That is now refused "
                        + "rather than compounded, but it means the ISO code stops being rebuilt "
                        + "from the sheet's tokens — it freezes at whatever it says today.\n\n"
                        + "If you only want the identifier PRINTED, cancel and re-bind the DRG NO. "
                        + "label in the title-block family to " + ParamRegistry.SHT_TAG_1 + " instead "
                        + "— that changes the drawing without changing the key."
                        + (skipped.Length > 0 ? "\n\n" + skipped : ""),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show me the full list first (changes nothing)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Renumber {plan.Moves.Count} sheet(s) now");

                var choice = td.Show();
                if (choice == TaskDialogResult.CommandLink1)
                {
                    TaskDialog.Show(Title + " (dry run)", preview.ToString());
                    return Result.Succeeded;
                }
                if (choice != TaskDialogResult.CommandLink2) return Result.Cancelled;
            }

            // DTW-11: the ONE renumber path. SheetNumbering.Apply parks every sheet on
            // a unique temporary number, sets the targets, PUTS A REFUSED SHEET BACK
            // on its old number (the old two-pass rename left it on "~STINGTMP~NNNN"
            // and committed), rebuilds the identifiers and records the history.
            var changes = plan.Moves
                .Where(m => byId.ContainsKey(m.Id))
                .Select(m => new SheetNumbering.Change { Sheet = byId[m.Id], Old = m.Current, New = m.Target })
                .ToList();
            var outcome = SheetNumbering.Apply(doc, changes, $"{Title} ({ParamRegistry.SHT_TAG_1})");

            var sb = new StringBuilder();
            sb.AppendLine($"Renumbered : {outcome.Done}");
            if (outcome.Failed > 0)
            {
                sb.AppendLine($"Refused    : {outcome.Failed} — put back on their old numbers");
                sb.AppendLine();
                foreach (var f in outcome.Failures.Take(12)) sb.AppendLine(f);
            }
            if (skipped.Length > 0)
            {
                sb.AppendLine();
                sb.Append(skipped);
            }
            if (outcome.Done > 0)
            {
                sb.AppendLine();
                sb.AppendLine(outcome.HistoryPath != null
                    ? "Reversal record: " + outcome.HistoryPath
                    : "WARNING: the reversal record could NOT be written (see the STING log). "
                      + "The renumber has happened; the from -> to pairs are in the log only.");
                if (outcome.RetagFailures.Count > 0)
                {
                    sb.AppendLine($"{outcome.RetagFailures.Count} identifier rebuild(s) failed:");
                    foreach (var f in outcome.RetagFailures.Take(6)) sb.AppendLine(f);
                }
                sb.AppendLine();
                sb.AppendLine("Re-stamp the QR codes: the payload is keyed on the sheet number and");
                sb.AppendLine("every existing code now points at the old one.");
            }

            StingLog.Info($"SheetNumberFromIso: {outcome.Done} renumbered, {outcome.Failed} refused, "
                + $"{plan.Locked.Count} locked, {plan.Held.Count} held");
            PresetDialog.Show(Title, sb.ToString(), ref message);
            return outcome.Failed > 0 && outcome.Done == 0 ? Result.Failed : Result.Succeeded;
        }

        private static bool IsTrue(string v)
        {
            v = (v ?? "").Trim();
            return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v == "1";
        }
    }
}
