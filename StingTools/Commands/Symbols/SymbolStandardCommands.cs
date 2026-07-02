// StingTools — Symbol standard switching + placement commands (Phase 175)

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
    public class SwitchProjectStandardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var standards = SymbolStandardRegistry.ListStandards().ToList();
            if (standards.Count == 0)
            {
                TaskDialog.Show("STING", "No standards configured.");
                return Result.Failed;
            }
            var pick = StingTools.Select.StingListPicker.Show(
                "Switch project symbol standard",
                "Pick the standard to apply to all symbol overlays.",
                standards);
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;

            try
            {
                SymbolStandardResolver.SetProjectStandard(ctx.Doc, pick);

                // F6 — preflight: the swap only restyles when the target-standard families
                // exist. If none are resolvable (project + content roots) the switch would
                // silently do nothing, so tell the user to build the library first and
                // offer to run it now.
                if (!TargetStandardHasFamilies(ctx.Doc, pick, out int resolvable))
                {
                    var guard = new TaskDialog("STING - Standard Switch")
                    {
                        MainInstruction = $"No '{pick}' symbol families are built",
                        MainContent = $"Switching to {pick} would restyle nothing — no {pick} symbol families were "
                            + "found in the project or the content library. Build the symbol library for this "
                            + "standard first (Symbols_CreateAll), then switch.",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                        AllowCancellation = true
                    };
                    guard.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Build the symbol library now", "Runs Symbols_CreateAll (all catalogues), then switches.");
                    if (guard.Show() != TaskDialogResult.CommandLink1)
                        return Result.Cancelled;

                    RunFullSymbolBuild(ctx.Doc);
                    if (!TargetStandardHasFamilies(ctx.Doc, pick, out resolvable))
                    {
                        TaskDialog.Show("STING - Standard Switch",
                            $"Still no {pick} families after the build. The Revit family-template path is the usual "
                            + "cause — run Symbols_Preflight to check it. Switch aborted.");
                        return Result.Failed;
                    }
                }

                int swapped = SwapAllTags(ctx.Doc, pick, out int modelSwapped, out int modelSkipped);
                string modelLine;
                if (modelSwapped == 0 && modelSkipped > 0)
                    modelLine = $"\n⚠ 0 model symbol instances swapped, {modelSkipped} skipped — their {pick} "
                        + "target families are not loaded. Build/load the library for this standard, then re-run.";
                else
                    modelLine = $"\n{modelSwapped} model symbol instance(s) swapped"
                        + (modelSkipped > 0 ? $", {modelSkipped} skipped (no resolvable/compatible target)." : ".");
                TaskDialog.Show("STING", $"Switched to {pick}. {swapped} tag(s) updated.{modelLine}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SwitchProjectStandardCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>F6 — true when at least one concept resolves a family for
        /// <paramref name="standard"/> that is actually available (loaded in the project
        /// or on disk across the content roots).</summary>
        internal static bool TargetStandardHasFamilies(Document doc, string standard, out int resolvable)
        {
            resolvable = 0;
            try
            {
                var available = SymbolOrientationAuditCommand.BuildAvailableFamilySet(doc);
                foreach (var c in SymbolConceptRegistry.ListConcepts())
                {
                    if (c?.ConceptId == null) continue;
                    string fam = SymbolConceptRegistry.GetFamilyName(c.ConceptId, standard, null, null, null);
                    if (!string.IsNullOrWhiteSpace(fam) && available.Contains(fam)) resolvable++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"TargetStandardHasFamilies: {ex.Message}"); }
            return resolvable > 0;
        }

        /// <summary>F6 — runs the full symbol-library build (all catalogues), mirroring
        /// Symbols_CreateAll, so the user can build the missing standard inline.</summary>
        private static void RunFullSymbolBuild(Document doc)
        {
            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                try { SymbolBatchHelper.RunBatch(doc, b.File, b.Folder); }
                catch (Exception ex) { StingLog.Warn($"RunFullSymbolBuild {b.File}: {ex.Message}"); }
            }
        }

        // Chunk size for the swap loop. One Transaction per chunk under
        // a single TransactionGroup means a failure in one chunk doesn't
        // roll back already-swapped chunks; users can stop the run with
        // partial success preserved.
        private const int SwapChunkSize = 100;

        internal static int SwapAllTags(Document doc, string newStandard,
            out int modelSwapped, out int modelSkipped)
        {
            int n = 0;
            modelSwapped = 0;
            modelSkipped = 0;
            int stdCode = StandardNameToCode(newStandard);

            var tags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !string.IsNullOrEmpty(t.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();

            // Collect model family instances that either carry STING_SYMBOL_ID (P2-2 —
            // swap the placed symbol to the new-standard family) or STING_SYMBOL_STD
            // (multi-standard families whose embedded curve set follows the int).
            var modelInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM) != null
                          || !string.IsNullOrEmpty(fi.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();

            using (var tx = new Transaction(doc, "STING Swap Symbol Standard"))
            {
                tx.Start();

                // ── 1. Annotation tags: swap family type ────────────────────────
                foreach (var tag in tags)
                {
                    try
                    {
                        var view = doc.GetElement(tag.OwnerViewId) as View;
                        string conceptId = tag.LookupParameter("STING_SYMBOL_ID")?.AsString();
                        if (string.IsNullOrEmpty(conceptId)) continue;
                        string viewCtx = SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(view));
                        string scaleTier = SymbolScaleEngine.GetScaleTier(view);
                        string fam = SymbolConceptRegistry.GetFamilyName(conceptId, newStandard, viewCtx, scaleTier, null);
                        if (string.IsNullOrEmpty(fam)) continue;
                        var sym = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => string.Equals(s.Name, fam, StringComparison.OrdinalIgnoreCase));
                        if (sym == null) continue;
                        if (!sym.IsActive) sym.Activate();
                        tag.ChangeTypeId(sym.Id);
                        var stdParam = tag.LookupParameter(ParamRegistry.SYMBOL_STANDARD);
                        if (stdParam != null && !stdParam.IsReadOnly) stdParam.Set(newStandard);
                        if (view != null)
                            SymbolAnnotationEngine.UpdateAnnotations(doc, view, newStandard);
                        n++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SwapAllTags tag: {ex.Message}"); }
                }

                // ── 2. Model family instances (P2-2) ─────────────────────────────
                //   a) keep the embedded-standard int in sync (multi-standard families
                //      restyle from it);
                //   b) additionally swap the TYPE to the new-standard family for
                //      instances that name a concept via STING_SYMBOL_ID, reusing the
                //      same GetFamilyName resolution as the tag path. Skip (never
                //      corrupt) instances with no resolvable / category-compatible
                //      target and count them.
                foreach (var fi in modelInstances)
                {
                    try
                    {
                        // (a) embedded-standard int.
                        var p = fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM);
                        if (p != null && !p.IsReadOnly) p.Set(stdCode);

                        // (b) per-standard family swap.
                        string conceptId = fi.LookupParameter("STING_SYMBOL_ID")?.AsString();
                        if (string.IsNullOrEmpty(conceptId)) continue;

                        View v = doc.GetElement(fi.OwnerViewId) as View; // null for model-space
                        string vctx = v != null
                            ? SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(v)) : null;
                        string stier = v != null ? SymbolScaleEngine.GetScaleTier(v) : null;

                        string fam = SymbolConceptRegistry.GetFamilyName(conceptId, newStandard, vctx, stier, null);
                        if (string.IsNullOrEmpty(fam)) { modelSkipped++; continue; }

                        // Already the target family → int-set above is all that's needed.
                        if (string.Equals(fi.Symbol?.FamilyName, fam, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var target = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .FirstOrDefault(fs => string.Equals(fs.FamilyName, fam, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(fs.Name, fam, StringComparison.OrdinalIgnoreCase));
                        if (target == null) { modelSkipped++; continue; } // not loaded → don't corrupt

                        // Category-compatibility guard — ChangeTypeId across categories corrupts.
                        if (fi.Category?.Id?.Value != target.Category?.Id?.Value) { modelSkipped++; continue; }

                        if (!target.IsActive) target.Activate();
                        fi.ChangeTypeId(target.Id);
                        modelSwapped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SwapAllTags model fi: {ex.Message}"); modelSkipped++; }
                }

                tx.Commit();
            }

            using (var tg = new TransactionGroup(doc, "STING Swap Symbol Standard"))
            {
                tg.Start();
                for (int chunkStart = 0; chunkStart < tags.Count; chunkStart += SwapChunkSize)
                {
                    int end = Math.Min(chunkStart + SwapChunkSize, tags.Count);
                    using (var tx = new Transaction(doc, $"STING Swap chunk {chunkStart / SwapChunkSize + 1}"))
                    {
                        tx.Start();
                        try
                        {
                            for (int i = chunkStart; i < end; i++)
                            {
                                var tag = tags[i];
                                try
                                {
                                    var view = doc.GetElement(tag.OwnerViewId) as View;
                                    string conceptId = tag.LookupParameter("STING_SYMBOL_ID")?.AsString();
                                    if (string.IsNullOrEmpty(conceptId)) continue;
                                    string viewCtx = SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(view));
                                    string scaleTier = SymbolScaleEngine.GetScaleTier(view);
                                    string fam = SymbolConceptRegistry.GetFamilyName(conceptId, newStandard, viewCtx, scaleTier, null);
                                    if (string.IsNullOrEmpty(fam)) continue;
                                    // Inline FindSymbol — original helper was lost to the merge.
                                    var sym = new FilteredElementCollector(doc)
                                        .OfClass(typeof(FamilySymbol))
                                        .Cast<FamilySymbol>()
                                        .FirstOrDefault(fs => string.Equals(fs.FamilyName, fam, StringComparison.OrdinalIgnoreCase));
                                    if (sym == null) continue;
                                    if (!sym.IsActive) sym.Activate();
                                    tag.ChangeTypeId(sym.Id);
                                    var stdParam = tag.LookupParameter("STING_SYMBOL_STANDARD");
                                    if (stdParam != null && !stdParam.IsReadOnly) stdParam.Set(newStandard);
                                    if (view != null)
                                        SymbolAnnotationEngine.UpdateAnnotations(doc, view, newStandard);
                                    n++;
                                }
                                catch (Exception ex) { StingLog.Warn($"SwapAllTags inner [{i}]: {ex.Message}"); }
                            }
                            tx.Commit();
                        }
                        catch (Exception chunkEx)
                        {
                            // A chunk-level failure rolls back this chunk
                            // only; previously-committed chunks survive.
                            StingLog.Error($"SwapAllTags chunk {chunkStart}-{end} failed", chunkEx);
                            try { tx.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"SwapAllTags rollback: {rbEx.Message}"); }
                        }
                    }
                }
                tg.Assimilate();
            }

            // Standard switch invalidates cached TextNoteType resolutions
            // (different rules, different sizes).
            SymbolAnnotationEngine.InvalidateAnnotationCache();
            return n;
        }

        /// <summary>Maps a standard name string to the STING_SYMBOL_STD integer code.</summary>
        internal static int StandardNameToCode(string standardName)
        {
            if (string.IsNullOrEmpty(standardName)) return ParamRegistry.STD_CODE_IEC;
            switch (standardName.ToUpperInvariant())
            {
                case "IEC":   return ParamRegistry.STD_CODE_IEC;
                case "ANSI":  return ParamRegistry.STD_CODE_ANSI;
                case "BS":    return ParamRegistry.STD_CODE_BS;
                case "NFPA":  return ParamRegistry.STD_CODE_NFPA;
                case "CIBSE": return ParamRegistry.STD_CODE_CIBSE;
                default:      return ParamRegistry.STD_CODE_IEC;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwitchViewStandardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var pick = StingTools.Select.StingListPicker.Show("Switch view standard",
                "Pick the standard to apply to symbols in this view.",
                SymbolStandardRegistry.ListStandards().ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;

            SymbolStandardResolver.SetViewStandard(ctx.Doc, ctx.ActiveView, pick);

            int n = 0;
            int modelUpdated = 0;
            int stdCode = SwitchProjectStandardCommand.StandardNameToCode(pick);
            using (var tx = new Transaction(ctx.Doc, "STING Switch View Symbol Standard"))
            {
                tx.Start();
                // Update annotation tags in this view.
                n = SymbolAnnotationEngine.UpdateAnnotations(ctx.Doc, ctx.ActiveView, pick);

                // Also set STING_SYMBOL_STD on model family instances visible in this view
                // so the embedded multi-standard curve set reflects the chosen standard.
                var visibleInstances = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM) != null);
                foreach (var fi in visibleInstances)
                {
                    try
                    {
                        var p = fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM);
                        if (p != null && !p.IsReadOnly) { p.Set(stdCode); modelUpdated++; }
                    }
                    catch (Exception ex) { StingLog.Warn($"SwitchViewStandard model fi: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING", $"View standard set to {pick}. {n} annotation(s) refreshed, {modelUpdated} model instance(s) updated.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetMixedStandardProfileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var profiles = SymbolStandardRegistry.ListProfiles()
                .Select(p => p.Id + " — " + p.Name).ToList();
            if (profiles.Count == 0) { TaskDialog.Show("STING", "No mixed-standard profiles defined."); return Result.Failed; }
            var pick = StingTools.Select.StingListPicker.Show(
                "Mixed-standard profile", "Pick the active profile.", profiles);
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            string id = pick.Split(' ').FirstOrDefault();
            SymbolStandardResolver.SetProjectProfile(ctx.Doc, id);
            TaskDialog.Show("STING", $"Profile set to {id}.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSymbolsInViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null || ctx.ActiveView == null)
            { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            try
            {
                int n;
                using (var tx = new Transaction(ctx.Doc, "STING Place Symbols in View"))
                {
                    tx.Start();
                    n = SymbolOverlayManager.PlaceOverlaysForView(ctx.Doc, ctx.ActiveView);
                    tx.Commit();
                }
                TaskDialog.Show("STING", $"Placed {n} symbol overlay(s) in {ctx.ActiveView.Name}.");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlaceSymbolsInView", ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSymbolsProjectWideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            int totalPlaced = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Place Symbols Project-Wide"))
            {
                tx.Start();
                foreach (View v in new FilteredElementCollector(ctx.Doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate
                        && (v.ViewType == ViewType.FloorPlan
                         || v.ViewType == ViewType.CeilingPlan
                         || v.ViewType == ViewType.Section
                         || v.ViewType == ViewType.Elevation)))
                {
                    totalPlaced += SymbolOverlayManager.PlaceOverlaysForView(ctx.Doc, v);
                }
                tx.Commit();
            }
            TaskDialog.Show("STING", $"Placed {totalPlaced} symbol overlay(s) project-wide.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolStandardAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            var drift = SymbolDriftDetector.DetectDrift(ctx.Doc);
            var coverage = SymbolCoverageAuditor.AuditCoverage(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Symbols total: {drift.TotalSymbols}");
            sb.AppendLine($"  drift count : {drift.DriftedSymbols}");
            sb.AppendLine($"Coverage     : {coverage.CoveragePercent:F1}% ({coverage.CoveredElements}/{coverage.TotalMEPElements})");
            sb.AppendLine($"Uncovered    : {coverage.UncoveredElements}");
            TaskDialog.Show("STING - Symbol Audit", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncViewFilterVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            int n;
            using (var tx = new Transaction(ctx.Doc, "STING Sync Symbol Filter Visibility"))
            {
                tx.Start();
                n = SymbolOverlayManager.SyncAllFilterVisibility(ctx.Doc);
                tx.Commit();
            }
            TaskDialog.Show("STING", $"Synced filter visibility on {n} symbol tag(s).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Writes STING_SYMBOL_STD on selected model family instances so the embedded
    /// multi-standard curve set shows the chosen standard for those instances only,
    /// without affecting other placed instances of the same family.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetElementSymbolStandardCommand : IExternalCommand
    {
        private static readonly (string Label, string Tag, int Code)[] _opts =
        {
            ("IEC — IEC 60617 / EN 60617",        "IEC",   ParamRegistry.STD_CODE_IEC),
            ("ANSI — ANSI/IEEE 315",               "ANSI",  ParamRegistry.STD_CODE_ANSI),
            ("BS — BS 1553 / BS 8888",             "BS",    ParamRegistry.STD_CODE_BS),
            ("NFPA — NFPA 72 / NFPA 13",           "NFPA",  ParamRegistry.STD_CODE_NFPA),
            ("CIBSE — CIBSE Guide symbols",        "CIBSE", ParamRegistry.STD_CODE_CIBSE),
        };

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var pick = StingTools.Select.StingListPicker.Show(
                "Set element symbol standard",
                "Pick the standard to apply to the selected model family instances. " +
                "Only instances that already have the STING_SYMBOL_STD parameter (authored " +
                "via Author Symbols) will be updated.",
                _opts.Select(o => o.Label).ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;

            var chosen = _opts.FirstOrDefault(o => pick.StartsWith(o.Label));
            if (chosen.Label == null) return Result.Cancelled;

            var instances = ctx.UIDoc.Selection
                .GetElementIds()
                .Select(id => ctx.Doc.GetElement(id))
                .OfType<FamilyInstance>()
                .ToList();

            if (instances.Count == 0)
            {
                TaskDialog.Show("STING", "Select model family instances first.");
                return Result.Cancelled;
            }

            int updated = 0, skipped = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Set Element Symbol Standard"))
            {
                tx.Start();
                foreach (var fi in instances)
                {
                    var p = fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM);
                    if (p == null || p.IsReadOnly) { skipped++; continue; }
                    p.Set(chosen.Code);
                    updated++;
                }
                tx.Commit();
            }

            string detail = skipped > 0
                ? $"\n{skipped} instance(s) skipped — STING_SYMBOL_STD not present (run Author Symbols first)."
                : "";
            TaskDialog.Show("STING",
                $"Set standard to {chosen.Tag} on {updated} instance(s).{detail}");
            return Result.Succeeded;
        }
    }
}
