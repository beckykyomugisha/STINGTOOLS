// StingTools — Drawing Template Manager · title-block lock
//
// WHY THIS FILE EXISTS
// --------------------
// PRJ_TB_LOCK_BOOL is the "do not touch this title block" flag. SIX places
// honour it — SheetQrStamper, TitleBlockParamApplier, TitleBlockRevisionSyncer,
// DrawingHealTitleBlocks, the sheet-count paginator and the swap command — each
// reporting "locked; left untouched" and doing nothing.
//
// Nothing in the codebase ever WROTE it, and nothing could clear it. Six
// refusals and no way out: an operator who met one was told the sheet was
// locked, not how it got that way or what to do, and the only route was to
// know that a checkbox exists somewhere in the Properties palette.
//
// The value arrives one of two ways, both outside the plugin:
//   * the title-block FAMILY was authored with the box ticked, so every
//     instance placed from it comes in locked (this is the usual cause after
//     swapping in a hand-authored seed), or
//   * somebody ticked it on one sheet, deliberately, to freeze a drawing that
//     has been issued.
//
// The second is a legitimate and valuable thing to do, which is why Unlock
// NAMES every sheet it is about to change and requires confirmation. Clearing
// a deliberate freeze silently would be worse than the gap this closes.

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
    internal static class TitleBlockLock
    {
        /// <summary>Where a lock was found. The parameter is declared
        /// <c>"instance": true</c> in STING_TITLE_BLOCKS.json but <c>Type</c> in
        /// MR_PARAMETERS.csv, and a hand-authored seed may have made it either —
        /// so both are checked, and the report says which one held it. Guessing
        /// one and clearing nothing is how "I unlocked it and it is still locked"
        /// happens.</summary>
        public enum LockHeldOn { None, Instance, Type }

        public sealed class SheetLock
        {
            public ViewSheet Sheet { get; set; }
            public Element TitleBlock { get; set; }
            public LockHeldOn HeldOn { get; set; }
        }

        public static Element FindTitleBlock(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstElement();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockLock: title block on '{sheet?.SheetNumber}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Where, if anywhere, this title block carries a set lock.</summary>
        public static LockHeldOn Probe(Document doc, Element tb)
        {
            if (tb == null) return LockHeldOn.None;
            try
            {
                var p = tb.LookupParameter(ParamRegistry.TB_LOCK);
                if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() != 0)
                    return LockHeldOn.Instance;

                var type = doc?.GetElement(tb.GetTypeId());
                var tp = type?.LookupParameter(ParamRegistry.TB_LOCK);
                if (tp != null && tp.StorageType == StorageType.Integer && tp.AsInteger() != 0)
                    return LockHeldOn.Type;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockLock.Probe: {ex.Message}");
            }
            return LockHeldOn.None;
        }

        public static List<SheetLock> Scan(Document doc, IEnumerable<ViewSheet> sheets)
        {
            var found = new List<SheetLock>();
            foreach (var sheet in sheets)
            {
                if (sheet == null) continue;
                var tb = FindTitleBlock(doc, sheet);
                if (tb == null) continue;
                var where = Probe(doc, tb);
                if (where != LockHeldOn.None)
                    found.Add(new SheetLock { Sheet = sheet, TitleBlock = tb, HeldOn = where });
            }
            return found;
        }

        public static IEnumerable<ViewSheet> AllSheets(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase);

        /// <summary>Clear one lock. Requires an open transaction. Returns false
        /// when the parameter would not take the write — reported, never assumed
        /// to have worked.</summary>
        public static bool Clear(Document doc, SheetLock hit, out string why)
        {
            why = null;
            try
            {
                Element target = hit.HeldOn == LockHeldOn.Type
                    ? doc.GetElement(hit.TitleBlock.GetTypeId())
                    : hit.TitleBlock;

                var p = target?.LookupParameter(ParamRegistry.TB_LOCK);
                if (p == null) { why = "the parameter is not on that element"; return false; }
                if (p.IsReadOnly) { why = "the parameter is read-only"; return false; }
                if (!p.Set(0)) { why = "Revit refused the write"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return false;
            }
        }

        /// <summary>The sentence every refusal should end with. One wording, so
        /// six call sites cannot drift into six different explanations.</summary>
        public const string HowToClear =
            "Clear it with Drawing Type Editor → Title Block → 'Unlock TBs', or untick " +
            ParamRegistry.TB_LOCK + " on the title block in the Properties palette.";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TitleBlock_InspectLock — read-only: which sheets are frozen, and where
    //  the flag is held. Writes nothing.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockInspectLockCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Title Block Lock", "No active document.");
                return Result.Failed;
            }

            var all = TitleBlockLock.AllSheets(doc).ToList();
            var locked = TitleBlockLock.Scan(doc, all);

            var sb = new StringBuilder();
            sb.AppendLine($"Sheets           : {all.Count}");
            sb.AppendLine($"Locked title blocks : {locked.Count}");
            sb.AppendLine();

            if (locked.Count == 0)
            {
                sb.AppendLine("Nothing is locked. Every sheet accepts stamps, revision sync");
                sb.AppendLine("and parameter writes.");
            }
            else
            {
                // Held on the TYPE means the family itself was authored locked, so
                // it is not one sheet's decision — it affects every sheet placed
                // from that type, and clearing it on one clears it for all.
                int onType = locked.Count(l => l.HeldOn == TitleBlockLock.LockHeldOn.Type);
                if (onType > 0)
                {
                    sb.AppendLine($"{onType} of these are locked on the title-block TYPE, not the sheet.");
                    sb.AppendLine("That means the FAMILY was authored with the box ticked, so every");
                    sb.AppendLine("sheet placed from it arrives locked — and clearing it once clears");
                    sb.AppendLine("it for all of them. Fix the seed .rfa to stop it recurring.");
                    sb.AppendLine();
                }

                foreach (var l in locked.Take(30))
                    sb.AppendLine($"  {l.Sheet.SheetNumber,-14} {l.Sheet.Name}   [on {l.HeldOn}]");
                if (locked.Count > 30)
                    sb.AppendLine($"  … and {locked.Count - 30} more");

                sb.AppendLine();
                sb.AppendLine("While locked, these are skipped by: QR stamping, title-block");
                sb.AppendLine("parameter fill, revision sync, heal, sheet-count pagination");
                sb.AppendLine("and title-block swap.");
                sb.AppendLine();
                sb.AppendLine(TitleBlockLock.HowToClear);
            }

            StingLog.Info($"TitleBlock_InspectLock: {locked.Count} locked of {all.Count} sheets");
            TaskDialog.Show("STING — Title Block Lock", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TitleBlock_Unlock — clear PRJ_TB_LOCK_BOOL, after naming what changes.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockUnlockCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var uiDoc = uiApp?.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Unlock Title Blocks", "No active document.");
                return Result.Failed;
            }

            var active = doc.ActiveView as ViewSheet;
            var all = TitleBlockLock.AllSheets(doc).ToList();
            var locked = TitleBlockLock.Scan(doc, all);

            if (locked.Count == 0)
            {
                TaskDialog.Show("STING — Unlock Title Blocks",
                    $"No title block in this project carries {ParamRegistry.TB_LOCK}.\n\n" +
                    "Nothing to unlock — if a command still reports a sheet as locked, " +
                    "say so: that would mean the flag is being read somewhere this does not look.");
                return Result.Succeeded;
            }

            // Scope. The active sheet is offered first because that is nearly
            // always the one the operator just hit a refusal on.
            var onActive = active == null
                ? null
                : locked.FirstOrDefault(l => l.Sheet.Id == active.Id);

            var td = new TaskDialog("STING — Unlock Title Blocks")
            {
                MainInstruction = $"{locked.Count} locked title block(s)",
                MainContent =
                    "Unlocking lets QR stamping, revision sync, parameter fill and heal " +
                    "write to these sheets again.\n\n" +
                    "A lock is sometimes deliberate — a drawing frozen after issue. Check " +
                    "the list before clearing everything.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            if (onActive != null)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Unlock this sheet only ({active.SheetNumber})");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Unlock all {locked.Count} locked sheet(s)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Show me which sheets are locked first");

            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink3)
            {
                var insp = new TitleBlockInspectLockCommand();
                string ignored = null;
                return insp.Execute(commandData, ref ignored, elements);
            }
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            var scope = choice == TaskDialogResult.CommandLink1
                ? new List<TitleBlockLock.SheetLock> { onActive }
                : locked;

            int cleared = 0;
            var failures = new List<string>();
            using (var t = new Transaction(doc, "STING Unlock Title Blocks"))
            {
                t.Start();
                foreach (var hit in scope)
                {
                    if (TitleBlockLock.Clear(doc, hit, out string why)) cleared++;
                    else failures.Add($"{hit.Sheet.SheetNumber}: {why}");
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Unlocked : {cleared}");
            if (failures.Count > 0)
            {
                // A failed clear must not read as a success. The operator would
                // re-run the stamp, meet the same refusal, and have no idea why.
                sb.AppendLine($"Failed   : {failures.Count}");
                sb.AppendLine();
                foreach (var f in failures.Take(10)) sb.AppendLine("  " + f);
            }
            if (cleared > 0 && scope.Any(s => s.HeldOn == TitleBlockLock.LockHeldOn.Type))
            {
                sb.AppendLine();
                sb.AppendLine("At least one was held on the title-block TYPE, so that clear applies");
                sb.AppendLine("to every sheet using it. It will come back the next time the family");
                sb.AppendLine("is rebuilt from a seed that has the box ticked — untick it in the");
                sb.AppendLine("seed .rfa to stop it returning.");
            }

            StingLog.Info($"TitleBlock_Unlock: cleared {cleared}, failed {failures.Count}");
            TaskDialog.Show("STING — Unlock Title Blocks", sb.ToString());
            return failures.Count > 0 && cleared == 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
