// StingTools — Drawing Template Manager · reorder a set, then renumber it
//
// Auto-Number Sheets numbers in the order the sheets are already in. That is the
// right default and no help when the ORDER is the mistake — "the first sheet was
// supposed to be the third". The only route was to hand-renumber a sheet so it
// sorted where you wanted and then run Auto-Number, which on a set of two hundred
// is a day's work to fix a five-minute error.
//
// This is the same numbering engine with the order made editable first.

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
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetReorderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Reorder Sheets", "No active document.");
                return Result.Failed;
            }

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            if (all.Count == 0)
            {
                TaskDialog.Show("STING — Reorder Sheets", "No sheets in this project.");
                return Result.Cancelled;
            }

            // A sheet numbered with a full ISO identifier is left out entirely, the
            // same as in Auto-Number: renumbering it would destroy the identifier
            // Sheet_NumberFromIso deliberately put there.
            var assembled = all.Where(s => Iso19650DocumentCode.LooksAssembled(s.SheetNumber)).ToList();
            var pool = all.Except(assembled).ToList();
            if (pool.Count == 0)
            {
                TaskDialog.Show("STING — Reorder Sheets",
                    "Every sheet carries a full ISO 19650 identifier as its number, so there is "
                    + "nothing to re-sequence.\n\nRestore short numbers first (Restore Sheet Nos).");
                return Result.Cancelled;
            }

            var disciplines = pool
                .Select(s => SheetDisciplineResolver.Resolve(s.SheetNumber, s.Name, null))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();

            // ── which group ──────────────────────────────────────────────────
            //
            // One discipline at a time, because a sheet number is scoped to its
            // discipline: A-001 and M-001 both exist and reordering them together
            // would mean nothing. "All" is offered anyway for a set that uses one
            // sequence throughout.
            string scope;
            if (disciplines.Count == 1)
            {
                scope = disciplines[0];
            }
            else
            {
                var opts = disciplines
                    .Select(d => new StingListPicker.ListItem
                    {
                        Label = d,
                        Detail = pool.Count(s => string.Equals(
                            SheetDisciplineResolver.Resolve(s.SheetNumber, s.Name, null),
                            d, StringComparison.OrdinalIgnoreCase)) + " sheet(s)",
                        Tag = d,
                    })
                    .ToList();
                opts.Insert(0, new StingListPicker.ListItem
                {
                    Label = "(all sheets, one sequence)",
                    Detail = pool.Count + " sheet(s) — only right if the set numbers straight through",
                    Tag = "*",
                });

                var picked = StingListPicker.Show("Reorder Sheets",
                    "Which group? A sheet number is scoped to its discipline, so A-001 and "
                    + "M-001 are different sheets and are sequenced separately.", opts);
                if (picked == null || picked.Count == 0) return Result.Cancelled;
                scope = (string)picked[0].Tag;
            }

            var chosen = scope == "*"
                ? pool
                : pool.Where(s => string.Equals(
                        SheetDisciplineResolver.Resolve(s.SheetNumber, s.Name, null),
                        scope, StringComparison.OrdinalIgnoreCase)).ToList();

            string pattern = SheetNumbering.ReadPattern(doc);
            string projectCode = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.ORG_PROJECT_CODE);
            string originator = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.ORG_ORIGINATOR_CODE);

            var byId = new Dictionary<object, ViewSheet>();
            var items = chosen
                .OrderBy(s => s.SheetNumber, SheetReorderDialog.NaturalOrder.Instance)
                .Select(s =>
                {
                    var tb = TitleBlockLock.FindTitleBlock(doc, s);
                    var item = new SheetReorderDialog.Item
                    {
                        Tag = s.Id,
                        Number = s.SheetNumber ?? "",
                        Name = s.Name ?? "",
                        Level = ParameterHelpers.GetString(s, ParamRegistry.SHT_LEVEL),
                        Discipline = SheetDisciplineResolver.Resolve(s.SheetNumber, s.Name, null),
                        Locked = tb != null
                                 && TitleBlockLock.Probe(doc, tb) != TitleBlockLock.LockHeldOn.None,
                    };
                    byId[s.Id] = s;
                    return item;
                })
                .ToList();

            var ordered = SheetReorderDialog.Show(
                "Reorder Sheets",
                $"{scope} — {items.Count} sheet(s). Sort to get close, then move the few that are "
                + "wrong. The new number is shown live, because a move changes the number of every "
                + "sheet after it.",
                items,
                (it, seq) => SheetDisciplineResolver.FormatNumber(
                    pattern, it.Discipline, it.Level, projectCode, originator, seq));

            if (ordered == null) return Result.Cancelled;

            // Numbers held by sheets outside this run, so the sequence routes around
            // them instead of failing on the second pass.
            var taken = new HashSet<string>(
                all.Where(s => !chosen.Contains(s)).Select(s => s.SheetNumber)
                   .Concat(ordered.Where(i => i.Locked).Select(i => i.Number)),
                StringComparer.OrdinalIgnoreCase);

            var plan = new List<SheetNumbering.Change>();
            int n = 1;
            foreach (var it in ordered)
            {
                if (it.Locked) continue;
                string proposed;
                do
                {
                    proposed = SheetDisciplineResolver.FormatNumber(
                        pattern, it.Discipline, it.Level, projectCode, originator, n);
                    n++;
                }
                while (taken.Contains(proposed));
                taken.Add(proposed);

                if (!string.Equals(proposed, it.Number, StringComparison.Ordinal)
                    && byId.TryGetValue(it.Tag, out ViewSheet sheet))
                    plan.Add(new SheetNumbering.Change { Sheet = sheet, Old = it.Number, New = proposed });
            }

            if (plan.Count == 0)
            {
                TaskDialog.Show("STING — Reorder Sheets",
                    "That order produces the numbers the sheets already have — nothing to do.");
                return Result.Succeeded;
            }

            // Which of these have already gone out, asked while it is still a
            // decision rather than reported afterwards.
            var issuedSet = SheetIssueHistory.IssuedNumbers(doc);
            var issuedRows = new List<KeyValuePair<string, List<SheetIssueHistory.Evidence>>>();
            foreach (var c in plan)
            {
                var ev = SheetIssueHistory.For(doc, c.Sheet,
                    TitleBlockLock.FindTitleBlock(doc, c.Sheet), issuedSet);
                if (ev.Count > 0)
                    issuedRows.Add(new KeyValuePair<string, List<SheetIssueHistory.Evidence>>(c.Old, ev));
            }
            string issuedWarning = SheetIssueHistory.WarningFor(issuedRows);

            if (issuedRows.Count > 0)
            {
                var warn = new TaskDialog("STING — Reorder Sheets")
                {
                    MainInstruction = $"{issuedRows.Count} of these {plan.Count} sheet(s) "
                        + "have already been issued. Renumber anyway?",
                    MainContent = issuedWarning,
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (warn.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            }

            var result = SheetNumbering.Apply(doc, plan, "STING Reorder Sheets");

            StingLog.Info($"Reorder: scope={scope}, {result.Done} renumbered, {result.Failed} failed");

            var lockedNames = ordered.Where(i => i.Locked).Select(i => i.Number).ToList();

            StingResultPanel.Create("")
                .SetTitle("Reorder Sheets")
                .SetSubtitle($"{scope} — {result.Done} of {items.Count} sheet(s) renumbered")
                .SetOverallPct(items.Count == 0 ? 0 : 100.0 * result.Done / items.Count)
                .AddSection("Summary")
                .Metric("Group", scope)
                .Metric("Sheets in group", items.Count.ToString())
                .Metric("Renumbered", result.Done.ToString())
                .Metric("Skipped (title block locked)", lockedNames.Count.ToString())
                .Metric("Skipped (full ISO identifier)", assembled.Count.ToString())
                .Metric("Failed", result.Failed.ToString())
                .Metric("Pattern", pattern)
                .Metric("Of those renumbered, already issued", issuedRows.Count.ToString())
                .Metric("Old numbers recorded in", result.HistoryPath ?? "(not written — see the log)")
                .Metric("Identifiers rebuilt", result.Retagged.ToString())
                .AddSection("New Order")
                .Text(string.Join("\n", plan.Select(c => $"  {c.Old,-18} ->  {c.New}")))
                .AddSection("Failures")
                .Text(result.Failures.Count == 0 ? "(none)" : string.Join("\n", result.Failures))
                .AddSection("Locked Sheets, Left As They Are")
                .Text(lockedNames.Count == 0 ? "(none)" : string.Join(", ", lockedNames)
                    + "\n\nPRJ_TB_LOCK_BOOL is set on these, so they kept both their place and "
                    + "their number. Clear it with 'Unlock TBs' and re-run to include them.")
                .AddSection("ISO 19650 Identifiers")
                .Text($"Rebuilt on {result.Retagged} sheet(s).\n\n"
                    + "SHT_TAG_1_TXT is DERIVED from the sheet number, and so are the discipline, "
                    + "form and level tokens it is assembled from — so a renumber invalidates them "
                    + "and they are rebuilt here rather than left for a follow-up step. Without "
                    + "that, the project browser shows the new number while the title block keeps "
                    + "printing the old one, and nothing on the drawing admits the two disagree.\n\n"
                    + (result.RetagFailures.Count == 0
                        ? "The title block follows once Populate runs — CDE REF and DRG NO. both "
                          + "derive from the identifier."
                        : "SOME FAILED:\n" + string.Join("\n", result.RetagFailures)
                          + "\n\nRun Tag Sheets to finish those."))
                .AddSection("Sheets That Had Already Been Issued")
                .Text(issuedRows.Count == 0
                    ? "(none found — but a set exported straight to PDF without a transmittal "
                      + "leaves no trace in the model, so this is 'no evidence', not 'never issued')"
                    : issuedWarning)
                .AddSection("Next")
                .Text("Run Tag Sheets so the ISO 19650 identifier picks up the new numbers, then "
                    + "Populate.\n\nView reference tags (sections, elevations, callouts) update "
                    + "themselves — they read the sheet number live. Anything ALREADY exported "
                    + "does not, and Revit keeps no record of a sheet's old number, so the "
                    + "old -> new pairs are written to sheet_number_history.json. That file is "
                    + "the answer to \"this PDF says A-001, what is that sheet called now?\"")
                .Show();

            return Result.Succeeded;
        }
    }
}
