// StingTools — family augmentation commands (Phase 175)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Symbols;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AugmentProjectFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            // Pre-count families so the progress dialog has an accurate
            // denominator before we start the long EditFamily loop.
            int total = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Count();

            var dlg = new TaskDialog("STING - Augment Families")
            {
                MainInstruction = $"Add STING symbol parameters to ~{total} loaded families?",
                MainContent =
                    "Each family will get STING_SYMBOL_ID / _STANDARD / _HOST_ELEMENT_ID injected. "
                  + "Already-augmented families are skipped. "
                  + "A progress dialog will appear; press Escape or click Cancel to stop early.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            var progress = StingTools.UI.StingProgressDialog.Show(
                "STING - Augment Families", Math.Max(total, 1));
            try
            {
                var iprog = new ProgressAdapter(progress);
                var results = FamilyAugmentationEngine.AugmentProjectFamilies(
                    ctx.Doc,
                    progress: iprog,
                    isCancelled: () => progress.IsCancelled);

                int ok      = results.Count(r => r.Success);
                int already = results.Count(r => r.AlreadyAugmented);
                int failed  = results.Count(r => !r.Success && !r.AlreadyAugmented);
                bool wasCancelled = progress.IsCancelled;

                var sb = new StringBuilder();
                if (wasCancelled) sb.AppendLine("Cancelled by user — partial results.");
                sb.AppendLine($"Processed  : {results.Count}");
                sb.AppendLine($"  augmented: {ok}");
                sb.AppendLine($"  already  : {already}");
                sb.AppendLine($"  failed   : {failed}");
                if (failed > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("First 10 failures:");
                    foreach (var r in results.Where(r => !r.Success && !r.AlreadyAugmented).Take(10))
                        sb.AppendLine($"  · {r.FamilyName}: {r.Warning}");
                }
                TaskDialog.Show("STING - Augment Families", sb.ToString());
                return wasCancelled ? Result.Cancelled : Result.Succeeded;
            }
            finally
            {
                try { progress.Close(); } catch (Exception ex) { StingLog.Warn($"AugmentProjectFamilies progress.Close: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Adapts <see cref="StingTools.UI.StingProgressDialog"/> to the
        /// <see cref="IProgress{T}"/> contract the engine accepts.
        /// </summary>
        private sealed class ProgressAdapter : IProgress<string>
        {
            private readonly StingTools.UI.StingProgressDialog _dlg;
            public ProgressAdapter(StingTools.UI.StingProgressDialog dlg) { _dlg = dlg; }
            public void Report(string value) => _dlg.Increment(value);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AugmentSelectedFamilyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            var sel = ctx.UIDoc.Selection.GetElementIds().Select(id => ctx.Doc.GetElement(id)).ToList();
            var families = sel.OfType<FamilyInstance>()
                .Select(fi => fi.Symbol?.Family).Where(f => f != null)
                .GroupBy(f => f.Id).Select(g => g.First()).ToList();
            if (families.Count == 0)
            { TaskDialog.Show("STING", "Select at least one family instance."); return Result.Cancelled; }

            int ok = 0;
            foreach (var fam in families)
            {
                var r = FamilyAugmentationEngine.AugmentFamily(ctx.Doc, fam);
                if (r.Success) ok++;
            }
            TaskDialog.Show("STING", $"Augmented {ok}/{families.Count} family(ies).");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RollbackAugmentationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            var families = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .ToList();
            var augmentedNames = new List<string>();
            foreach (var f in families)
            {
                try
                {
                    Document fdoc = ctx.Doc.EditFamily(f);
                    if (fdoc?.FamilyManager.get_Parameter("STING_SYMBOL_ID") != null)
                        augmentedNames.Add(f.Name);
                    try { fdoc?.Close(false); } catch (Exception ex) { StingLog.Warn($"Rollback close: {ex.Message}"); }
                }
                catch (Exception ex) { StingLog.Warn($"Rollback edit {f.Name}: {ex.Message}"); }
            }
            if (augmentedNames.Count == 0)
            { TaskDialog.Show("STING", "No augmented families found."); return Result.Cancelled; }
            var pick = StingTools.Select.StingListPicker.Show(
                "Rollback augmentation", "Pick a family to rollback.", augmentedNames);
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            var fam = families.FirstOrDefault(f => f.Name == pick);
            if (fam == null) return Result.Cancelled;
            bool ok = FamilyAugmentationEngine.RollbackAugmentation(ctx.Doc, fam);
            TaskDialog.Show("STING", ok ? $"Rolled back {pick}." : $"Rollback failed for {pick}.");
            return Result.Succeeded;
        }
    }
}
