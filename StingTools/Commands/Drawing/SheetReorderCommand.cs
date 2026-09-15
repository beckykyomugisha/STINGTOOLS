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
                .AddSection("New Order")
                .Text(string.Join("\n", plan.Select(c => $"  {c.Old,-18} ->  {c.New}")))
                .AddSection("Failures")
                .Text(result.Failures.Count == 0 ? "(none)" : string.Join("\n", result.Failures))
                .AddSection("Locked Sheets, Left As They Are")
                .Text(lockedNames.Count == 0 ? "(none)" : string.Join(", ", lockedNames)
                    + "\n\nPRJ_TB_LOCK_BOOL is set on these, so they kept both their place and "
                    + "their number. Clear it with 'Unlock TBs' and re-run to include them.")
                .AddSection("Next")
                .Text("Run Tag Sheets so the ISO 19650 identifier picks up the new numbers, then "
                    + "Populate.\n\nView reference tags (sections, elevations, callouts) update "
                    + "themselves — they read the sheet number live. Anything ALREADY exported "
                    + "does not: PDFs and transmittals issued under the old numbers still say the "
                    + "old numbers, which is why this is worth doing before an issue and awkward "
                    + "after one.")
                .Show();

            return Result.Succeeded;
        }
    }
}
