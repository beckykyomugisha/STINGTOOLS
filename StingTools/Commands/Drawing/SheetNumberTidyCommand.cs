// StingTools — Drawing Template Manager · clean existing sheet numbers
//
// The generator is fixed (DrawingRenumberCommand and DrawingProducer both collapse
// now), but a project already numbered "A--001" stays that way until something
// renames it. This is that something.
//
// It writes to the SAME sheet_number_history.json as Sheet_NumberFromIso, so one
// file holds every sheet-number change STING has made and Restore Sheet Nos can
// undo whichever came last.

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
    public class SheetNumberTidyCommand : IExternalCommand
    {
        private const string HistoryFile = "sheet_number_history.json";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Tidy Sheet Numbers", "No active document.");
                return Result.Failed;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var plan = new List<(ViewSheet sheet, string from, string to)>();
            foreach (var s in sheets)
            {
                string from = s.SheetNumber ?? "";
                if (!SheetNumberTidy.NeedsTidying(from)) continue;
                string to = SheetNumberTidy.Collapse(from);
                // A number that collapses to nothing is not a number. Refuse it rather
                // than blanking a sheet.
                if (string.IsNullOrWhiteSpace(to)) continue;
                plan.Add((s, from, to));
            }

            if (plan.Count == 0)
            {
                TaskDialog.Show("STING — Tidy Sheet Numbers",
                    $"All {sheets.Count} sheet number(s) are already clean — no doubled or "
                    + "dangling separators.");
                return Result.Succeeded;
            }

            // Two sheets can collapse onto the same number ("A--001" and "A-001"), and
            // Revit requires uniqueness. Catch it here rather than half-way through.
            var existing = new HashSet<string>(
                sheets.Select(s => s.SheetNumber ?? "").Except(plan.Select(p => p.from)),
                StringComparer.OrdinalIgnoreCase);
            var clashes = new List<string>();
            foreach (var g in plan.GroupBy(p => p.to, StringComparer.OrdinalIgnoreCase))
            {
                if (g.Count() > 1)
                    clashes.Add($"  {g.Key}  <- {string.Join(", ", g.Select(x => x.from))}");
                else if (existing.Contains(g.Key))
                    clashes.Add($"  {g.Key}  <- {g.First().from}   (a sheet already uses it)");
            }
            if (clashes.Count > 0)
            {
                TaskDialog.Show("STING — Tidy Sheet Numbers",
                    "Tidying would put two sheets on the same number, and Revit requires "
                    + "them to be unique. Nothing was changed.\n\n"
                    + string.Join("\n", clashes.Take(12))
                    + "\n\nRenumber one of each pair by hand first.");
                return Result.Failed;
            }

            var preview = new StringBuilder();
            foreach (var p in plan.Take(30)) preview.AppendLine($"  {p.from,-18} ->  {p.to}");
            if (plan.Count > 30) preview.AppendLine($"  … and {plan.Count - 30} more");

            var td = new TaskDialog("STING — Tidy Sheet Numbers")
            {
                MainInstruction = $"Clean {plan.Count} sheet number(s)?",
                MainContent =
                    "These carry a doubled or dangling separator, left by a pattern token "
                    + "that resolved empty — \"A-{lvl}-{seq}\" with no level gives \"A--001\".\n\n"
                    + preview
                    + "\nThe sheet number is a KEY: reference tags, schedules and export "
                    + "filenames all use it, so anything outside this model that refers to the "
                    + "old number stops matching. Every change is recorded in "
                    + HistoryFile + " and can be undone with Restore Sheet Nos.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Clean {plan.Count} sheet number(s)");
            if (td.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            int done = 0;
            var failed = new List<string>();
            using (var t = new Transaction(doc, "STING Tidy Sheet Numbers"))
            {
                t.Start();
                // No sentinel pass needed: every target is strictly shorter than its
                // source and the clash check above proved none collides.
                foreach (var p in plan)
                {
                    try { p.sheet.SheetNumber = p.to; done++; StingLog.Info($"SheetNumberTidy: '{p.from}' -> '{p.to}'"); }
                    catch (Exception ex) { failed.Add($"{p.from} -> {p.to}: {ex.Message}"); }
                }
                t.Commit();
            }

            string historyPath = null;
            if (done > 0)
            {
                try
                {
                    string dir = StingPaths.Meta(doc, "_BIM_COORD");
                    Directory.CreateDirectory(dir);
                    historyPath = Path.Combine(dir, HistoryFile);
                    var log = new List<object>();
                    if (File.Exists(historyPath))
                    {
                        var prev = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(historyPath));
                        if (prev != null) log.AddRange(prev);
                    }
                    log.Add(new
                    {
                        when = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                        source = "SheetNumberTidy",
                        changes = plan.Select(x => new { from = x.from, to = x.to }).ToList(),
                    });
                    File.WriteAllText(historyPath, JsonConvert.SerializeObject(log, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    // The rename HAS happened; saying nothing would leave the operator
                    // believing an undo record exists.
                    historyPath = null;
                    StingLog.Warn($"SheetNumberTidy: could not write {HistoryFile}: {ex.Message}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Cleaned : {done}");
            if (failed.Count > 0)
            {
                sb.AppendLine($"Failed  : {failed.Count}");
                foreach (var f in failed.Take(10)) sb.AppendLine("  " + f);
            }
            if (done > 0)
            {
                sb.AppendLine();
                sb.AppendLine(historyPath != null
                    ? "Undo record: " + historyPath
                    : "WARNING: the undo record could NOT be written (see the STING log).");
                sb.AppendLine();
                sb.AppendLine("Re-run Tag Sheets and re-stamp the QR codes — the ISO identifier's");
                sb.AppendLine("Number field and the QR payload are both keyed on the sheet number.");
            }

            StingLog.Info($"SheetNumberTidy: {done} cleaned, {failed.Count} failed");
            TaskDialog.Show("STING — Tidy Sheet Numbers", sb.ToString());
            return failed.Count > 0 && done == 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
