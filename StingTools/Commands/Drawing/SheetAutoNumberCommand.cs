// StingTools — Drawing Template Manager · auto-number every sheet in one pass
//
// WHY THIS FILE EXISTS
// --------------------
// "What's the fastest way to renumber?" — and the honest answer was: slowly.
//
// Batch Renumber groups sheets by ExtractDisciplinePrefix, which reads the
// discipline off the sheet number that is already there. That is fine for
// tidying a scheme and useless for creating one: sheets called "Sheet 1",
// "Unnamed" or numbered by whatever convention preceded the project all land in
// one bucket keyed "?" or on their own whole number, and there is no way to
// renumber INTO A-001 / A-002 from outside. It also handles one discipline per
// run, so a five-discipline set means five trips through a picker.
//
// SheetDisciplineResolver already answers "what discipline is this sheet" from
// the number OR the title, which is exactly the missing half — a sheet named
// "GROUND FLOOR PLAN" is architectural whether or not anything numbered it yet.
//
// So: every sheet, every discipline, one preview, one transaction, one Undo.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Docs;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetAutoNumberCommand : IExternalCommand
    {
        private sealed class Row
        {
            public ViewSheet Sheet;
            public string Disc;
            public string Level;
            public string Old;
            public string New;
            public bool Locked;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Auto-Number Sheets", "No active document.");
                return Result.Failed;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("STING — Auto-Number Sheets", "No sheets in this project.");
                return Result.Cancelled;
            }

            // A sheet whose number is already a full ISO identifier is NOT renumbered.
            // Sheet_NumberFromIso puts it there deliberately, and overwriting it would
            // destroy the identifier and then rebuild a short number from the wreckage.
            var assembled = sheets.Where(s => Iso19650DocumentCode.LooksAssembled(s.SheetNumber)).ToList();
            var candidates = sheets.Except(assembled).ToList();

            if (candidates.Count == 0)
            {
                TaskDialog.Show("STING — Auto-Number Sheets",
                    $"All {sheets.Count} sheet(s) already carry a full ISO 19650 identifier as their "
                    + "sheet number, so there is nothing to renumber.\n\n"
                    + "Restore short numbers first (Title Block tab → Restore Sheet Nos) if you want "
                    + "to re-sequence them.");
                return Result.Cancelled;
            }

            // ── Group by discipline, ordered as the browser shows them ────────
            var rows = candidates
                .Select(s => new Row
                {
                    Sheet = s,
                    Disc = SheetDisciplineResolver.Resolve(s.SheetNumber, s.Name, null),
                    Level = ParameterHelpers.GetString(s, ParamRegistry.SHT_LEVEL),
                    Old = s.SheetNumber ?? "",
                    Locked = TitleBlockLock.FindTitleBlock(doc, s) is Element tb
                             && TitleBlockLock.Probe(doc, tb) != TitleBlockLock.LockHeldOn.None,
                })
                .ToList();

            // The pattern is a project setting, not a constant. "{disc}-{seq:D3}"
            // is the default and gives A-001; a project numbering by level sets
            // "{disc}-{lvl}-{seq:D3}" and gets A-01-001 without a code change.
            string pattern = SheetNumbering.ReadPattern(doc);
            string projectCode = ParameterHelpers.GetString(
                doc.ProjectInformation, ParamRegistry.ORG_PROJECT_CODE);
            string originator = ParameterHelpers.GetString(
                doc.ProjectInformation, ParamRegistry.ORG_ORIGINATOR_CODE);

            var byDisc = rows.GroupBy(r => r.Disc, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(g => g.Key, StringComparer.Ordinal)
                             .ToList();

            // Numbers already spoken for: assembled sheets, and any locked sheet we
            // are leaving alone. Proposing one of those would fail on the second pass
            // and leave a sheet stranded on its __STING_TEMP number.
            var taken = new HashSet<string>(
                assembled.Select(s => s.SheetNumber)
                         .Concat(rows.Where(r => r.Locked).Select(r => r.Old)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var g in byDisc)
            {
                int n = 1;
                foreach (var r in g.OrderBy(x => x.Old, SheetReorderDialog.NaturalOrder.Instance))
                {
                    if (r.Locked) continue;
                    string proposed;
                    do
                    {
                        proposed = SheetDisciplineResolver.FormatNumber(
                            pattern, r.Disc, r.Level, projectCode, originator, n);
                        n++;
                    }
                    while (taken.Contains(proposed));
                    taken.Add(proposed);
                    r.New = proposed;
                }
            }

            var changing = rows.Where(r => r.New != null
                && !string.Equals(r.New, r.Old, StringComparison.Ordinal)).ToList();
            int alreadyRight = rows.Count(r => r.New != null
                && string.Equals(r.New, r.Old, StringComparison.Ordinal));
            var locked = rows.Where(r => r.Locked).ToList();

            if (changing.Count == 0)
            {
                TaskDialog.Show("STING — Auto-Number Sheets",
                    $"Every sheet already has the number this would give it "
                    + $"({alreadyRight} sheet(s) checked"
                    + (locked.Count > 0 ? $", {locked.Count} locked and left alone" : "")
                    + (assembled.Count > 0 ? $", {assembled.Count} carrying a full ISO identifier" : "")
                    + ").");
                return Result.Succeeded;
            }

            // ── Preview. Renumbering rewrites the reference every drawing, view
            // reference and issued PDF filename uses, so it is shown before it runs.
            var preview = new StringBuilder();
            foreach (var g in byDisc)
            {
                var gc = g.Where(r => r.New != null
                    && !string.Equals(r.New, r.Old, StringComparison.Ordinal)).ToList();
                if (gc.Count == 0) continue;
                preview.AppendLine($"{g.Key}  ({gc.Count} changing)");
                foreach (var r in gc.Take(12))
                    preview.AppendLine($"    {r.Old}  ->  {r.New}    {Trim(r.Sheet.Name, 34)}");
                if (gc.Count > 12) preview.AppendLine($"    … and {gc.Count - 12} more");
                preview.AppendLine();
            }
            if (locked.Count > 0)
                preview.AppendLine($"{locked.Count} locked sheet(s) left alone: "
                    + string.Join(", ", locked.Select(r => r.Old).Take(10)));
            if (assembled.Count > 0)
                preview.AppendLine($"{assembled.Count} sheet(s) carry a full ISO identifier and are not touched.");

            // Which of these have already gone out. Asked BEFORE the confirmation,
            // because "renumbering breaks the link to issued PDFs" is only useful
            // while it is still a decision.
            var issuedSet = SheetIssueHistory.IssuedNumbers(doc);
            var issuedRows = new List<KeyValuePair<string, List<SheetIssueHistory.Evidence>>>();
            foreach (var r in changing)
            {
                var ev = SheetIssueHistory.For(doc, r.Sheet,
                    TitleBlockLock.FindTitleBlock(doc, r.Sheet), issuedSet);
                if (ev.Count > 0)
                    issuedRows.Add(new KeyValuePair<string, List<SheetIssueHistory.Evidence>>(r.Old, ev));
            }
            string issuedWarning = SheetIssueHistory.WarningFor(issuedRows);

            var td = new TaskDialog("STING — Auto-Number Sheets")
            {
                MainInstruction = issuedRows.Count > 0
                    ? $"Renumber {changing.Count} sheet(s) — {issuedRows.Count} ALREADY ISSUED?"
                    : $"Renumber {changing.Count} of {sheets.Count} sheet(s)?",
                MainContent = (issuedWarning.Length > 0 ? issuedWarning + "\n" : "")
                    + preview.ToString()
                    + "\nThe sheet number is what elevation, section and callout tags print, and "
                    + "what exported filenames are built from. One Undo reverses the whole run.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            // ── Two passes. Revit refuses a duplicate sheet number even for an
            // instant, so every sheet parks on a unique temporary number first.
            var plan = changing.Select(r => new SheetNumbering.Change
            {
                Sheet = r.Sheet, Old = r.Old, New = r.New
            }).ToList();

            var result = SheetNumbering.Apply(doc, plan, "STING Auto-Number Sheets");
            int done = result.Done, failed = result.Failed;
            var failures = result.Failures;

            StingLog.Info($"AutoNumber: {done} renumbered, {failed} failed, "
                + $"{locked.Count} locked, {assembled.Count} already ISO-assembled");

            StingResultPanel.Create("")
                .SetTitle("Auto-Number Sheets")
                .SetSubtitle($"{done} of {sheets.Count} sheet(s) renumbered")
                .SetOverallPct(sheets.Count == 0 ? 0 : 100.0 * done / sheets.Count)
                .AddSection("Summary")
                .Metric("Sheets in project", sheets.Count.ToString())
                .Metric("Renumbered", done.ToString())
                .Metric("Already correct", alreadyRight.ToString())
                .Metric("Skipped (title block locked)", locked.Count.ToString())
                .Metric("Skipped (full ISO identifier)", assembled.Count.ToString())
                .Metric("Failed", failed.ToString())
                .Metric("Of those renumbered, already issued", issuedRows.Count.ToString())
                .Metric("Old numbers recorded in", result.HistoryPath ?? "(not written — see the log)")
                .AddSection("What Changed")
                .Text(changing.Count == 0
                    ? "(nothing)"
                    : string.Join("\n", changing.Where(r => r.New != null)
                        .Select(r => $"  {r.Old,-18} ->  {r.New,-12} {Trim(r.Sheet.Name, 40)}")
                        .Take(300)))
                .AddSection("Failures")
                .Text(failures.Count == 0 ? "(none)" : string.Join("\n", failures))
                .AddSection("Number Pattern")
                .Text($"{pattern}   (PRJ_TB_SHEET_NUMBER_PATTERN_TXT in TITLE_BLOCK.csv)\n\n"
                    + "Tokens: {disc} discipline · {lvl} level · {proj} project code · "
                    + "{orig} originator · {seq:D3} zero-padded sequence.\n"
                    + "A token that resolves to nothing takes its separator with it, so a "
                    + "project with no level code gets A-001 rather than A--001.")
                .AddSection("Sheets That Had Already Been Issued")
                .Text(issuedRows.Count == 0
                    ? "(none found — but a set exported straight to PDF without a transmittal "
                      + "leaves no trace in the model, so this is 'no evidence', not 'never issued')"
                    : issuedWarning)
                .AddSection("How The Discipline Was Decided")
                .Text("Sheet NUMBER prefix first (A-001 -> A), then whole words in the sheet "
                    + "TITLE (\"GROUND FLOOR PLAN\" -> A), then GEN.\n\n"
                    + "A sheet in the wrong group is telling you its number or its name says "
                    + "something else — rename it and re-run. Nothing here reads the elements "
                    + "drawn on the sheet: a general arrangement mixes trades on purpose, so "
                    + "counting them classified every GA plan as coordination.\n\n"
                    + "Next: run Tag Sheets so the ISO 19650 identifier picks up the new numbers, "
                    + "then Populate.")
                .Show();

            return Result.Succeeded;
        }

        private static string Trim(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    }
}
