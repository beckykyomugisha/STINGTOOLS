// StingTools — symbol maintenance commands (Phase 175)

using System;
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
    public class HealSymbolOrphansCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var report = SymbolOrphanHealer.FindOrphans(ctx.Doc);
            if (report.Orphans == 0)
            { TaskDialog.Show("STING", "No orphaned symbol tags found."); return Result.Succeeded; }

            var dlg = new TaskDialog("STING - Heal Orphans")
            {
                MainInstruction = $"Delete {report.Orphans} orphaned symbol tag(s)?",
                MainContent = $"Of {report.TotalTags} STING tags, {report.Orphans} have no live host element.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int healed = SymbolOrphanHealer.HealOrphans(ctx.Doc, deleteOrphans: true);
            TaskDialog.Show("STING", $"Healed {healed} orphaned tag(s).");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolCoverageAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            string text = SymbolCoverageAuditor.GenerateCoverageReport(ctx.Doc);
            TaskDialog.Show("STING - Symbol Coverage", text);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FixSymbolDriftCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            var report = SymbolDriftDetector.DetectDrift(ctx.Doc);
            if (report.DriftedSymbols == 0)
            { TaskDialog.Show("STING", "No symbol drift detected."); return Result.Succeeded; }

            var sb = new StringBuilder();
            sb.AppendLine($"Drifted symbols: {report.DriftedSymbols} / {report.TotalSymbols}");
            sb.AppendLine();
            sb.AppendLine("First 10:");
            foreach (var d in report.Drifted.Take(10))
                sb.AppendLine($"  · [{d.DriftType}] {d.ConceptId}: {d.ActualStandard} → {d.ExpectedStandard}");
            sb.AppendLine();
            sb.AppendLine("Apply fixes now?");
            var dlg = new TaskDialog("STING - Symbol Drift")
            {
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int fixedCount = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Fix Symbol Drift"))
            {
                tx.Start();
                foreach (var d in report.Drifted)
                {
                    try
                    {
                        var tag = ctx.Doc.GetElement(d.TagId) as IndependentTag;
                        if (tag == null) continue;
                        var view = ctx.Doc.GetElement(tag.OwnerViewId) as View;
                        string viewCtx = SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(view));
                        string scaleTier = SymbolScaleEngine.GetScaleTier(view);
                        string fam = SymbolConceptRegistry.GetFamilyName(
                            d.ConceptId, d.ExpectedStandard, viewCtx, scaleTier, null);
                        if (string.IsNullOrEmpty(fam)) continue;
                        var sym = new FilteredElementCollector(ctx.Doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => string.Equals(s.Name, fam, StringComparison.OrdinalIgnoreCase));
                        if (sym == null) continue;
                        if (!sym.IsActive) sym.Activate();
                        tag.ChangeTypeId(sym.Id);
                        var stdParam = tag.LookupParameter("STING_SYMBOL_STANDARD");
                        if (stdParam != null && !stdParam.IsReadOnly) stdParam.Set(d.ExpectedStandard);
                        fixedCount++;
                    }
                    catch (Exception ex) { StingLog.Warn($"FixDrift inner: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING", $"Fixed {fixedCount}/{report.DriftedSymbols} drifted tag(s).");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchHealAllSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            int orphansHealed = SymbolOrphanHealer.HealOrphans(ctx.Doc, deleteOrphans: true);
            var driftReport = SymbolDriftDetector.DetectDrift(ctx.Doc);
            int driftFixed = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Batch Heal Symbols"))
            {
                tx.Start();
                foreach (var d in driftReport.Drifted)
                {
                    try
                    {
                        var tag = ctx.Doc.GetElement(d.TagId) as IndependentTag;
                        if (tag == null) continue;
                        var stdParam = tag.LookupParameter("STING_SYMBOL_STANDARD");
                        if (stdParam != null && !stdParam.IsReadOnly) stdParam.Set(d.ExpectedStandard);
                        driftFixed++;
                    }
                    catch (Exception ex) { StingLog.Warn($"BatchHeal: {ex.Message}"); }
                }
                int synced = SymbolOverlayManager.SyncAllFilterVisibility(ctx.Doc);
                tx.Commit();
                TaskDialog.Show("STING - Batch Heal",
                    $"Orphans healed : {orphansHealed}\nDrift fixed    : {driftFixed}\nFilters synced : {synced}");
            }
            return Result.Succeeded;
        }
    }
}
