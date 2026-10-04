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
                // The swap only restyles when families for the target standard exist.
                // Check before changing anything: with none available the switch would
                // report "0 swapped" and look like it worked. Offer to build the library.
                if (!TargetStandardHasFamilies(ctx.Doc, pick, out _))
                {
                    var guard = new TaskDialog("STING - Standard Switch")
                    {
                        MainInstruction = $"No '{pick}' symbol families are built",
                        MainContent = $"Switching to {pick} would restyle nothing: no {pick} symbol families were "
                            + "found in the project or the content library. Build the symbol library first "
                            + "(Symbols_CreateAll), then switch. The project standard has not been changed.",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                        AllowCancellation = true
                    };
                    guard.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Build the symbol library now", "Runs Symbols_CreateAll (all catalogues), then switches.");
                    if (guard.Show() != TaskDialogResult.CommandLink1)
                        return Result.Cancelled;

                    RunFullSymbolBuild(ctx.Doc);
                    if (!TargetStandardHasFamilies(ctx.Doc, pick, out _))
                    {
                        TaskDialog.Show("STING - Standard Switch",
                            $"Still no {pick} families after the build. The Revit family-template path is the usual "
                            + "cause; run Symbols_Preflight to check it. The switch was not made.");
                        return Result.Failed;
                    }
                }

                SymbolStandardResolver.SetProjectStandard(ctx.Doc, pick);
                int swapped = SwapAllTags(ctx.Doc, pick, out int modelSwapped, out int modelSkipped,
                    out int modelRestyled, out int tagsFailed);
                string modelLine;
                if (modelSwapped == 0 && modelSkipped > 0)
                    modelLine = $"\n0 model symbol instances swapped, {modelSkipped} skipped: their {pick} "
                        + "target families are not loaded. Build or load the library for this standard, then re-run.";
                else
                    modelLine = $"\n{modelSwapped} model symbol instance(s) swapped"
                        + (modelSkipped > 0 ? $", {modelSkipped} skipped (no resolvable or compatible target)." : ".");
                if (modelRestyled > 0)
                    modelLine += $"\n{modelRestyled} multi-standard model instance(s) restyled (STING_SYMBOL_STD).";
                if (tagsFailed > 0)
                    modelLine += $"\n{tagsFailed} tag(s) NOT updated: their batch was rolled back or failed — see the STING log, then re-run.";
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

        /// <summary>True when at least one concept resolves a family for
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

        /// <summary>Runs the full symbol-library build (all catalogues), the same batches
        /// Symbols_CreateAll runs, so the missing standard can be built inline.</summary>
        private static void RunFullSymbolBuild(Document doc)
        {
            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                try
                {
                    var r = SymbolBatchHelper.RunBatch(doc, b.File, b.Folder);
                    StingLog.Info($"RunFullSymbolBuild {b.File}: created {r.Created}, existed {r.Existed}, failed {r.Failed}");
                }
                catch (Exception ex) { StingLog.Warn($"RunFullSymbolBuild {b.File}: {ex.Message}"); }
            }
        }

        // Chunk size for the swap loop. One Transaction per chunk under
        // a single TransactionGroup means a failure in one chunk doesn't
        // roll back already-swapped chunks; users can stop the run with
        // partial success preserved.
        private const int SwapChunkSize = 100;

        /// <returns>Annotation tags swapped. Model instances are counted separately:
        /// <paramref name="modelRestyled"/> (STING_SYMBOL_STD set) and
        /// <paramref name="modelSwapped"/> / <paramref name="modelSkipped"/> (family swaps);
        /// <paramref name="tagsFailed"/> counts tags in chunks that rolled back or failed.</returns>
        internal static int SwapAllTags(Document doc, string newStandard,
            out int modelSwapped, out int modelSkipped, out int modelRestyled, out int tagsFailed)
        {
            int n = 0;
            modelSwapped = 0;
            modelSkipped = 0;
            modelRestyled = 0;
            tagsFailed = 0;
            int stdCode = StandardNameToCode(newStandard);

            var tags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !string.IsNullOrEmpty(t.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();

            // Model family instances that carry STING_SYMBOL_ID (swap the placed symbol
            // to the new standard's family) or STING_SYMBOL_STD (multi-standard families
            // whose embedded curve set follows the integer).
            var modelInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM) != null
                          || !string.IsNullOrEmpty(fi.LookupParameter(ParamRegistry.SYMBOL_ID)?.AsString()))
                .ToList();

            using (var tx = new Transaction(doc, "STING Swap Symbol Standard"))
            {
                tx.Start();

                // Annotation tags are swapped once, in the chunked TransactionGroup below.
                // (A merge of two branches left a second, identical tag pass here that
                // swapped every tag twice and counted it twice.)

                // ── Model family instances ───────────────────────────────────────
                //   a) keep the STING_SYMBOL_STD integer in sync (multi-standard
                //      families restyle from it);
                //   b) for instances naming a concept in STING_SYMBOL_ID, change the
                //      type to the new standard's family, resolved the same way as the
                //      tags. An instance with no resolvable, loaded, same-category
                //      target is skipped and counted, never changed.
                foreach (var fi in modelInstances)
                {
                    try
                    {
                        var p = fi.LookupParameter(ParamRegistry.SYMBOL_STD_PARAM);
                        if (p != null && !p.IsReadOnly)
                        {
                            p.Set(stdCode);
                            modelRestyled++;   // a model instance, not a tag (was counted in n)
                        }

                        string conceptId = fi.LookupParameter(ParamRegistry.SYMBOL_ID)?.AsString();
                        if (string.IsNullOrEmpty(conceptId)) continue;

                        View v = doc.GetElement(fi.OwnerViewId) as View; // null for model-space instances
                        string vctx = v != null
                            ? SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(v)) : null;
                        string stier = v != null ? SymbolScaleEngine.GetScaleTier(v) : null;

                        string fam = SymbolConceptRegistry.GetFamilyName(conceptId, newStandard, vctx, stier, null, doc);
                        if (string.IsNullOrEmpty(fam)) { modelSkipped++; continue; }

                        if (string.Equals(fi.Symbol?.FamilyName, fam, StringComparison.OrdinalIgnoreCase))
                            continue; // already the target family

                        var target = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .FirstOrDefault(fs => string.Equals(fs.FamilyName, fam, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(fs.Name, fam, StringComparison.OrdinalIgnoreCase));
                        if (target == null) { modelSkipped++; continue; } // not loaded

                        // ChangeTypeId across categories corrupts the instance.
                        if (fi.Category?.Id?.Value != target.Category?.Id?.Value) { modelSkipped++; continue; }

                        if (!target.IsActive) target.Activate();
                        fi.ChangeTypeId(target.Id);
                        modelSwapped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SwapAllTags model fi: {ex.Message}"); modelSkipped++; }
                }

                StingTx.Commit(tx);
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
                            int chunkN = 0; // counted into n only once this chunk commits
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
                                    string fam = SymbolConceptRegistry.GetFamilyName(conceptId, newStandard, viewCtx, scaleTier, null, doc);
                                    if (string.IsNullOrEmpty(fam)) continue;
                                    // Family-name match first, then type name — the two lookups
                                    // the removed duplicate pass and this pass used between them.
                                    var symbols = new FilteredElementCollector(doc)
                                        .OfClass(typeof(FamilySymbol))
                                        .Cast<FamilySymbol>()
                                        .ToList();
                                    var sym = symbols.FirstOrDefault(fs => string.Equals(fs.FamilyName, fam, StringComparison.OrdinalIgnoreCase))
                                           ?? symbols.FirstOrDefault(fs => string.Equals(fs.Name, fam, StringComparison.OrdinalIgnoreCase));
                                    if (sym == null) continue;
                                    if (!sym.IsActive) sym.Activate();
                                    tag.ChangeTypeId(sym.Id);
                                    var stdParam = tag.LookupParameter(ParamRegistry.SYMBOL_STANDARD);
                                    if (stdParam != null && !stdParam.IsReadOnly) stdParam.Set(newStandard);
                                    if (view != null)
                                        SymbolAnnotationEngine.UpdateAnnotations(doc, view, newStandard);
                                    chunkN++;
                                }
                                catch (Exception ex) { StingLog.Warn($"SwapAllTags inner [{i}]: {ex.Message}"); }
                            }
                            StingTx.Commit(tx); // a rolled-back chunk lands in the catch below
                            n += chunkN;
                        }
                        catch (Exception chunkEx)
                        {
                            // A chunk-level failure rolls back this chunk
                            // only; previously-committed chunks survive.
                            StingLog.Error($"SwapAllTags chunk {chunkStart}-{end} failed", chunkEx);
                            tagsFailed += end - chunkStart;
                            StingTx.RollBackIfOpen(tx);
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
                StingTx.Commit(tx);
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
                    StingTx.Commit(tx);
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
                StingTx.Commit(tx);
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
                StingTx.Commit(tx);
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
                StingTx.Commit(tx);
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
