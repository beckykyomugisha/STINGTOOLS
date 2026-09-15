// StingTools — Drawing Template Manager · push title-block fields to other sheets
//
// WHY THIS FILE EXISTS
// --------------------
// "I changed DRAWN BY and CHECKED BY and they changed on only one sheet."
//
// That is Revit behaving correctly. PRJ_TB_DRAWN_BY_TXT and PRJ_TB_CHECKED_BY_TXT
// are INSTANCE parameters, so each sheet's title block holds its own copy — which
// is the right model, because a drawing set genuinely can be drafted by different
// people. Typing into the Properties palette edits one instance and nothing else.
//
// The existing route to change them everywhere is TITLE_BLOCK.csv + Populate, and
// it is the right route when the CSV is the source of truth. It is the wrong route
// when someone has already typed the value onto a sheet and wants that sheet to be
// the source of truth: it means retyping into a CSV, and Populate's CSV pass would
// then flatten every deliberate per-sheet name in the set.
//
// So: take the values off ONE sheet and push them to the others. Explicitly —
// field by field, sheet by sheet, previewed before anything is written, because
// every value this overwrites was typed by a person on purpose.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Docs;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPushFieldsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Push Sheet Fields", "No active document.");
                return Result.Failed;
            }

            var source = doc.ActiveView as ViewSheet;
            if (source == null)
            {
                TaskDialog.Show("STING — Push Sheet Fields",
                    "Open the SHEET whose values you want to copy, then run this.\n\n"
                    + "This takes the title-block values off the sheet you are looking at and "
                    + "writes them to other sheets.");
                return Result.Cancelled;
            }

            Element srcTb = TitleBlockLock.FindTitleBlock(doc, source);
            if (srcTb == null)
            {
                TaskDialog.Show("STING — Push Sheet Fields",
                    $"Sheet '{source.SheetNumber}' has no title block, so there is nothing to copy.");
                return Result.Cancelled;
            }

            // ── What is on the source sheet ───────────────────────────────────
            //
            // Read rather than listed. A hard-coded list of "the fields people edit
            // by hand" is right until the day a project adds one, and the title-block
            // families differ between projects anyway. Anything the source sheet
            // actually HOLDS a value for is a candidate; anything empty is not,
            // because pushing a blank erases other sheets.
            var candidates = new List<KeyValuePair<string, string>>();
            foreach (string name in CandidateNames(srcTb, source))
            {
                string v = Printed(source, srcTb, name);
                if (!string.IsNullOrWhiteSpace(v))
                    candidates.Add(new KeyValuePair<string, string>(name, v.Trim()));
            }

            if (candidates.Count == 0)
            {
                TaskDialog.Show("STING — Push Sheet Fields",
                    $"Sheet '{source.SheetNumber}' holds no title-block text values to copy.\n\n"
                    + "Type the values onto this sheet first (Properties palette), then run this "
                    + "to push them to the rest of the set.");
                return Result.Cancelled;
            }

            var targets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.Id != source.Id)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
            {
                TaskDialog.Show("STING — Push Sheet Fields",
                    "There is only one sheet in this project — nothing to push to.");
                return Result.Cancelled;
            }

            // Resolve each target's title block ONCE. The preview needs it to count
            // what would change and the write loop needs it again; collecting twice
            // over a 300-sheet set is the difference between instant and a stall.
            var tbOf = new Dictionary<ElementId, Element>();
            foreach (var t in targets) tbOf[t.Id] = TitleBlockLock.FindTitleBlock(doc, t);

            // ── Which fields ──────────────────────────────────────────────────
            //
            // Nothing is pre-ticked. A push that defaults to "all" is a push somebody
            // runs without reading, and the values it overwrites are ones a person
            // typed. Each row says how many sheets it would change and how many
            // already agree, so the cost is visible before the click.
            var items = new List<StingListPicker.ListItem>();
            foreach (var c in candidates)
            {
                int differ = 0, agree = 0, absent = 0;
                foreach (var t in targets)
                {
                    Element tb = tbOf[t.Id];
                    if (tb == null) { absent++; continue; }
                    string cur = Printed(t, tb, c.Key);
                    if (string.Equals((cur ?? "").Trim(), c.Value, StringComparison.Ordinal)) agree++;
                    else differ++;
                }

                items.Add(new StingListPicker.ListItem
                {
                    Label = c.Key,
                    Detail = $"\"{c.Value}\"   ->  would change {differ} sheet(s), {agree} already match"
                             + (absent > 0 ? $", {absent} have no title block" : ""),
                    Tag = c.Key
                });
            }

            var picked = StingListPicker.Show(
                "Push Sheet Fields",
                $"From sheet {source.SheetNumber} to {targets.Count} other sheet(s). "
                + "Tick the fields to copy. Everything you tick OVERWRITES what those sheets hold.",
                items, allowMultiSelect: true);

            if (picked == null || picked.Count == 0) return Result.Cancelled;

            var chosen = new HashSet<string>(picked.Select(i => (string)i.Tag),
                                             StringComparer.OrdinalIgnoreCase);
            var values = candidates.Where(c => chosen.Contains(c.Key))
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            // ── Confirm, naming what is about to be lost ──────────────────────
            var preview = new StringBuilder();
            preview.AppendLine($"From sheet {source.SheetNumber}:");
            foreach (var kv in values) preview.AppendLine($"    {kv.Key} = \"{kv.Value}\"");
            preview.AppendLine();
            preview.AppendLine($"onto {targets.Count} other sheet(s).");
            preview.AppendLine();
            preview.AppendLine("Whatever those sheets hold in these fields is replaced. "
                + "Revit's Undo reverses the whole push in one step.");

            var td = new TaskDialog("STING — Push Sheet Fields")
            {
                MainInstruction = $"Copy {values.Count} field(s) to {targets.Count} sheet(s)?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int changed = 0, unchanged = 0, lockedSkipped = 0, noTb = 0, failed = 0;
            var lockedNames = new List<string>();
            var changedRows = new List<string>();

            using (var tx = new Transaction(doc, "STING Push Title Block Fields"))
            {
                tx.Start();
                foreach (var t in targets)
                {
                    Element tb = tbOf[t.Id];
                    if (tb == null) { noTb++; continue; }

                    // PRJ_TB_LOCK_BOOL means "this drawing has been issued; do not
                    // touch it". Six other commands honour it and this one is not the
                    // exception — a push that quietly rewrote a frozen sheet would
                    // defeat the only mechanism there is for freezing one.
                    if (TitleBlockLock.Probe(doc, tb) != TitleBlockLock.LockHeldOn.None)
                    {
                        lockedSkipped++;
                        lockedNames.Add(t.SheetNumber);
                        continue;
                    }

                    int wrote = 0;
                    foreach (var kv in values)
                    {
                        string before = Printed(t, tb, kv.Key);
                        if (string.Equals((before ?? "").Trim(), kv.Value, StringComparison.Ordinal))
                            continue;

                        try
                        {
                            // BOTH homes. Nearly every title-block parameter name
                            // exists twice — once on the sheet as a project parameter,
                            // once on the family — and the label binds to one of them
                            // without saying which. Writing one home is the whole "it
                            // reported success and the cell did not change" class of bug.
                            if (TitleBlockEngine.SetOnSheetAndTitleBlock(
                                    t, tb, kv.Key, kv.Value, false, out _))
                            {
                                wrote++;
                                changedRows.Add($"  {t.SheetNumber}  {kv.Key}: "
                                    + $"\"{(string.IsNullOrWhiteSpace(before) ? "(empty)" : before.Trim())}\""
                                    + $" -> \"{kv.Value}\"");
                            }
                            else failed++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            StingLog.Warn($"PushFields '{t.SheetNumber}' / '{kv.Key}': {ex.Message}");
                        }
                    }

                    if (wrote > 0) changed++; else unchanged++;
                }
                tx.Commit();
            }

            StingLog.Info($"TB PushFields: from {source.SheetNumber}, {values.Count} field(s), "
                + $"{changed} sheets changed, {unchanged} already matching, {lockedSkipped} locked, "
                + $"{noTb} no title block, {failed} write failures");

            StingResultPanel.Create("")
                .SetTitle("Push Sheet Fields")
                .SetSubtitle($"{source.SheetNumber} -> {changed} of {targets.Count} sheet(s) changed")
                .SetOverallPct(targets.Count == 0 ? 0 : 100.0 * changed / targets.Count)
                .AddSection("Summary")
                .Metric("Source sheet", source.SheetNumber)
                .Metric("Fields pushed", values.Count.ToString())
                .Metric("Sheets changed", changed.ToString())
                .Metric("Sheets already matching", unchanged.ToString())
                .Metric("Sheets skipped (locked)", lockedSkipped.ToString())
                .Metric("Sheets skipped (no title block)", noTb.ToString())
                .Metric("Write failures", failed.ToString())
                .AddSection("What Changed")
                .Text(changedRows.Count == 0
                    ? "(nothing — every sheet already held these values)"
                    : string.Join("\n", changedRows.Take(200))
                      + (changedRows.Count > 200 ? $"\n  … and {changedRows.Count - 200} more" : ""))
                .AddSection("Locked Sheets, Left Alone")
                .Text(lockedNames.Count == 0
                    ? "(none)"
                    : string.Join(", ", lockedNames)
                      + "\n\nPRJ_TB_LOCK_BOOL is set on these. Clear it with 'Unlock TBs' and "
                      + "re-run if they should have been included.")
                .AddSection("If A Cell Did Not Change")
                .Text("The value is written to BOTH the sheet and the title-block instance, so a "
                    + "label bound to either picks it up. If a cell on the drawing still shows the "
                    + "old text, the family has no label for that parameter, or has a second "
                    + "parameter of the same name under a different GUID — run 'Inspect Fields' on "
                    + "that sheet, which reports which of the two it is.")
                .Show();

            return Result.Succeeded;
        }

        /// <summary>Every title-block parameter name worth offering: the registry's
        /// declared set, plus whatever else the family actually carries. Read from the
        /// element rather than listed, because the families differ between projects and
        /// a restated list is right until the day somebody adds a field.</summary>
        private static IEnumerable<string> CandidateNames(Element tb, ViewSheet sheet)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in ParamRegistry.AllTitleBlockParams) names.Add(n);

            foreach (Element e in new Element[] { tb, sheet })
            {
                if (e == null) continue;
                try
                {
                    foreach (Parameter p in e.Parameters)
                    {
                        if (p == null || p.IsReadOnly) continue;
                        if (p.StorageType != StorageType.String) continue;
                        string n = p.Definition?.Name;
                        if (!string.IsNullOrEmpty(n) && n.StartsWith("PRJ_", StringComparison.Ordinal))
                            names.Add(n);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PushFields: parameter sweep: {ex.Message}");
                }
            }

            // Derived and audit fields are excluded on purpose. Copying one sheet's CDE
            // reference, transmittal stamp or sync record onto another sheet would state
            // something false about that sheet — these identify the drawing, they do not
            // describe the project.
            names.Remove(ParamRegistry.TB_CDE_REF);
            names.Remove(ParamRegistry.TB_LAST_SYNC);
            names.Remove(ParamRegistry.TB_LAST_SYNC_BY);
            names.Remove(ParamRegistry.TB_LAST_TRANSMITTAL);
            names.Remove(ParamRegistry.TB_LAST_TRANSMITTAL_DATE);
            names.Remove(ParamRegistry.TB_LOCK);
            names.Remove(ParamRegistry.SHT_TAG_1);
            names.Remove("PRJ_SHEET_OF_TOTAL_TXT");
            names.Remove("PRJ_SHEET_FULL_REF_TXT");
            return names;
        }

        /// <summary>The value a label would show: the title block's own copy wins,
        /// the sheet's project parameter is the fallback.</summary>
        private static string Printed(ViewSheet sheet, Element tb, string name)
        {
            string v = null;
            try { v = ParameterHelpers.GetString(tb, name); }
            catch (Exception ex) { StingLog.Warn($"PushFields read tb '{name}': {ex.Message}"); }
            if (!string.IsNullOrWhiteSpace(v)) return v;
            try { v = ParameterHelpers.GetString(sheet, name); }
            catch (Exception ex) { StingLog.Warn($"PushFields read sheet '{name}': {ex.Message}"); }
            return v;
        }
    }
}
