// StingTools — symbol maintenance commands (Phase 175)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Content;
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

    /// <summary>
    /// F5 — orientation-variant audit. P1-2 wired <c>orientationStates</c> so placement
    /// resolves a per-orientation family variant, falling back to the base when the variant
    /// is absent. Because those *_VERTICAL_VIEW_PLAN / *_ENDVIEW families were never
    /// authored, the wiring is inert-but-green. This makes the gap explicit: for every
    /// concept declaring orientationStates, it lists the orientation-variant family names
    /// that are referenced but MISSING from the content roots, with counts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolOrientationAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            string std = SymbolStandardResolver.ResolveStandard(doc, doc.ActiveView, null);
            if (string.IsNullOrWhiteSpace(std)) std = "IEC";

            // Build the set of family names available anywhere (loaded + on-disk roots) once.
            var available = BuildAvailableFamilySet(doc);

            int conceptsWithOs = 0, referenced = 0, missing = 0, present = 0;
            var missingByConcept = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in SymbolConceptRegistry.ListConcepts())
            {
                if (c?.OrientationStates == null || c.OrientationStates.Count == 0) continue;
                conceptsWithOs++;
                var missHere = new List<string>();

                foreach (var key in c.OrientationStates.Keys)
                {
                    // The horizontal-plan state is the base/default — no variant expected.
                    if (key.IndexOf("HORIZONTAL_VIEW_PLAN", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    var cands = SymbolConceptRegistry.GetFamilyNameCandidates(
                        c.ConceptId, std, null, null, key);
                    if (cands.Count < 2) continue; // only the base resolved — no variant referenced

                    // Every candidate except the last (base) is a referenced orientation variant.
                    for (int i = 0; i < cands.Count - 1; i++)
                    {
                        string v = cands[i];
                        if (string.IsNullOrWhiteSpace(v)) continue;
                        referenced++;
                        if (available.Contains(v)) present++;
                        else { missing++; if (!missHere.Contains(v)) missHere.Add(v); }
                    }
                }
                if (missHere.Count > 0) missingByConcept[c.ConceptId] = missHere;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Standard resolved: {std}");
            sb.AppendLine($"Concepts declaring orientationStates : {conceptsWithOs}");
            sb.AppendLine($"Orientation-variant families referenced: {referenced}");
            sb.AppendLine($"  present in content roots            : {present}");
            sb.AppendLine($"  referenced but MISSING              : {missing}  (in {missingByConcept.Count} concept(s))");
            if (missingByConcept.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Referenced-but-missing orientation variants (author these families or accept base fallback):");
                foreach (var kv in missingByConcept.Take(25))
                    sb.AppendLine($"  {kv.Key}: {string.Join(", ", kv.Value)}");
                if (missingByConcept.Count > 25)
                    sb.AppendLine($"  … +{missingByConcept.Count - 25} more concept(s) (StingTools.log)");
            }
            else if (conceptsWithOs > 0)
            {
                sb.AppendLine();
                sb.AppendLine("All referenced orientation variants resolve — none missing.");
            }

            StingLog.Info($"Symbols_OrientationAudit: concepts={conceptsWithOs} referenced={referenced} " +
                $"present={present} missing={missing} std={std}");
            foreach (var kv in missingByConcept)
                StingLog.Info($"  orientation-missing {kv.Key}: {string.Join(", ", kv.Value)}");

            new TaskDialog("STING - Orientation Variant Audit")
            {
                MainInstruction = $"{missing} referenced orientation variant(s) missing across {conceptsWithOs} concept(s)",
                MainContent = sb.ToString()
            }.Show();
            return Result.Succeeded;
        }

        /// <summary>Family names available anywhere: loaded in the project + every .rfa
        /// base-name across the content roots (recursive). Used for a fast membership test.
        /// Shared with the standard-switch preflight (F6).</summary>
        internal static HashSet<string> BuildAvailableFamilySet(Document doc)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var fs in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    if (!string.IsNullOrEmpty(fs.FamilyName)) set.Add(fs.FamilyName);
                    if (!string.IsNullOrEmpty(fs.Name)) set.Add(fs.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"OrientationAudit loaded scan: {ex.Message}"); }

            foreach (var root in ContentRoots.Resolve(doc))
            {
                try
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    foreach (var f in Directory.EnumerateFiles(root, "*.rfa", SearchOption.AllDirectories))
                        set.Add(Path.GetFileNameWithoutExtension(f));
                }
                catch (Exception ex) { StingLog.Warn($"OrientationAudit root '{root}': {ex.Message}"); }
            }
            return set;
        }
    }
}
