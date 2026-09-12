// TypeMarkCommands.cs — G-20. Preview / assign door + window TYPE marks.
//
// Preview is a separate command, not a checkbox, so the read-only path cannot be
// skipped by muscle memory. It writes nothing to the model OR the sequence store.

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    /// <summary>Report what type marks WOULD be assigned. Writes nothing.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TypeMarkPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var r = TypeMarkSequencer.Run(ctx.Doc, preview: true);
            TaskDialog.Show("Type Marks — preview (nothing written)", Format(r, ctx.Doc, preview: true));
            return Result.Succeeded;
        }

        internal static string Format(TypeMarkResult r, Document doc, bool preview)
        {
            var sb = new StringBuilder();
            sb.AppendLine(preview
                ? "PREVIEW — no model change, no sequence written."
                : "Type marks assigned.");
            sb.AppendLine();
            sb.AppendLine($"  would assign / assigned : {r.Assigned}");
            sb.AppendLine($"  adopted (existing mark) : {r.Adopted}");
            sb.AppendLine($"  left alone (marked)     : {r.AlreadyMarked}");
            if (r.Collisions > 0) sb.AppendLine($"  COLLISIONS              : {r.Collisions}");

            var skipped = r.Assignments.Count(a => a.Outcome == "Skipped");
            if (skipped > 0) sb.AppendLine($"  skipped                 : {skipped}");

            foreach (var grp in r.Assignments.Where(a => a.Outcome == "Assigned")
                                             .GroupBy(a => a.CategoryName + " · " + a.ProdCode))
            {
                sb.AppendLine();
                sb.AppendLine($"── {grp.Key} ──");
                foreach (var a in grp.Take(15)) sb.AppendLine($"   {a.Mark,-10} {a.TypeName}");
                int extra = grp.Count() - 15;
                if (extra > 0) sb.AppendLine($"   … +{extra} more");
            }

            foreach (var a in r.Assignments.Where(a => a.Outcome == "Adopted").Take(10))
                sb.AppendLine($"\n   adopted {a.Mark,-10} {a.TypeName}");
            foreach (var a in r.Assignments.Where(a => a.Outcome == "Skipped").Take(10))
                sb.AppendLine($"\n   skipped {a.TypeName} — {a.Note}");

            // 3.9 — the join check runs on every invocation, preview included.
            try
            {
                var join = TypeMarkSequencer.VerifyJoin(doc);
                sb.AppendLine();
                sb.AppendLine(join.Count == 0
                    ? "Join check: every type mark prefix matches segment 7 of its instances' ASS_TAG_1_TXT."
                    : $"JOIN CHECK — {join.Count} controlled-vocabulary break(s):");
                foreach (var p in join.Take(10)) sb.AppendLine("   " + p);
            }
            catch (Exception ex) { sb.AppendLine($"Join check failed: {ex.Message}"); }

            foreach (var w in r.Warnings.Take(10)) sb.AppendLine("\n! " + w);
            return sb.ToString();
        }
    }

    /// <summary>Assign type marks. Never overwrites an existing mark.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TypeMarkAssignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var pre = TypeMarkSequencer.Run(ctx.Doc, preview: true);
            if (pre.Assigned == 0 && pre.Collisions == 0)
            {
                TaskDialog.Show("Type Marks",
                    "Nothing to assign — every door and window type already carries a mark.\n\n"
                  + $"Adopted into the sequence: {pre.Adopted}\nLeft alone: {pre.AlreadyMarked}");
                return Result.Succeeded;
            }

            var td = new TaskDialog("Assign type marks?")
            {
                MainInstruction = $"Assign {pre.Assigned} new type mark(s)?",
                MainContent =
                    "Existing marks are never overwritten — they are adopted and the sequence "
                  + "continues past them.\n\nMarks are MONOTONIC and never reused: a deleted mark "
                  + "stays retired, so the next allocation continues upward rather than filling the "
                  + "gap. Two products sharing one mark across revisions is not recoverable.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            var r = TypeMarkSequencer.Run(ctx.Doc, preview: false);
            StingLog.Info($"TypeMarks: assigned={r.Assigned} adopted={r.Adopted} " +
                          $"alreadyMarked={r.AlreadyMarked} collisions={r.Collisions}");
            TaskDialog.Show("Type Marks", TypeMarkPreviewCommand.Format(r, ctx.Doc, preview: false));
            return Result.Succeeded;
        }
    }
}
