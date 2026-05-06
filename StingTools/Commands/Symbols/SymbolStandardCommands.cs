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
                int swapped = SwapAllTags(ctx.Doc, pick);
                TaskDialog.Show("STING", $"Switched to {pick}. {swapped} tag(s) updated.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SwitchProjectStandardCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        // Chunk size for the swap loop. One Transaction per chunk under
        // a single TransactionGroup means a failure in one chunk doesn't
        // roll back already-swapped chunks; users can stop the run with
        // partial success preserved.
        private const int SwapChunkSize = 100;

        internal static int SwapAllTags(Document doc, string newStandard)
        {
            int n = 0;
            var tags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !string.IsNullOrEmpty(t.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();
            if (tags.Count == 0) return 0;

            // Cache FamilySymbol lookups so we don't run a project-wide
            // collector inside the inner loop.
            var symbolCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            FamilySymbol FindSymbol(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                if (symbolCache.TryGetValue(name, out var s)) return s;
                s = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => string.Equals(fs.Name, name, StringComparison.OrdinalIgnoreCase));
                symbolCache[name] = s;
                return s;
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
                                    var sym = FindSymbol(fam);
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
            int n = SymbolAnnotationEngine.UpdateAnnotations(ctx.Doc, ctx.ActiveView, pick);
            TaskDialog.Show("STING", $"View standard set to {pick}. {n} annotation(s) refreshed.");
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
    /// Toggle <c>project_config.json</c> <c>symbol_auto_place</c>.
    /// When true, <see cref="StingAutoTagger"/> drops a symbol overlay
    /// on every newly-placed MEP element. Default false (existing
    /// projects shouldn't get surprised).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolsAutoPlaceToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            try
            {
                if (string.IsNullOrEmpty(ctx.Doc.PathName))
                {
                    TaskDialog.Show("STING", "Save the project first — toggle is per-project.");
                    return Result.Failed;
                }
                string p = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(ctx.Doc.PathName), "project_config.json");
                Newtonsoft.Json.Linq.JObject root = System.IO.File.Exists(p)
                    ? Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))
                    : new Newtonsoft.Json.Linq.JObject();
                bool current = (bool)(root["symbol_auto_place"] ?? false);
                bool next = !current;
                root["symbol_auto_place"] = next;
                System.IO.File.WriteAllText(p, root.ToString());
                TaskDialog.Show("STING",
                    $"Symbol auto-place {(next ? "enabled" : "disabled")}. New MEP elements "
                  + (next ? "will" : "will not") + " get an overlay tag automatically.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SymbolsAutoPlaceToggle", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Project-wide companion to
    /// <see cref="RemoveSymbolsInViewCommand"/>. Walks every non-template
    /// view, deletes every STING symbol tag (filtered by
    /// <c>STING_SYMBOL_ID</c>) and its label TextNote. Chunked into
    /// transactions of 200 tags so a 5,000-tag project is interruptible
    /// and partial-success-safe.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveSymbolsProjectWideCommand : IExternalCommand
    {
        private const int RemoveChunkSize = 200;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            // Collect once across the project.
            var tags = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !string.IsNullOrEmpty(t.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();
            if (tags.Count == 0)
            { TaskDialog.Show("STING", "No STING symbol overlays in this project."); return Result.Succeeded; }

            // Tally by view for the confirmation dialog.
            var perView = tags
                .GroupBy(t => t.OwnerViewId)
                .Select(g => new { ViewId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            var preview = new StringBuilder();
            preview.AppendLine($"Found {tags.Count} STING symbol overlay(s) across {perView.Count} view(s).");
            preview.AppendLine();
            preview.AppendLine("Top views by count:");
            foreach (var v in perView.Take(8))
            {
                string viewName = (ctx.Doc.GetElement(v.ViewId) as View)?.Name ?? "<unknown>";
                preview.AppendLine($"  {v.Count,5}  {viewName}");
            }
            if (perView.Count > 8)
                preview.AppendLine($"  …  +{perView.Count - 8} more views");

            var dlg = new TaskDialog("STING - Remove Symbols Project-Wide")
            {
                MainInstruction = $"Delete all {tags.Count} STING symbol overlay(s)?",
                MainContent = preview.ToString()
                    + "\nHost MEP elements are not touched. Associated label TextNotes are removed."
                    + "\n\nThis action is reversible only by re-running PlaceSymbolsInView per view.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int removed = 0;
            using (var tg = new TransactionGroup(ctx.Doc, "STING Remove Symbols Project-Wide"))
            {
                tg.Start();
                for (int chunkStart = 0; chunkStart < tags.Count; chunkStart += RemoveChunkSize)
                {
                    int end = Math.Min(chunkStart + RemoveChunkSize, tags.Count);
                    using (var tx = new Transaction(ctx.Doc, $"STING Remove chunk {chunkStart / RemoveChunkSize + 1}"))
                    {
                        tx.Start();
                        try
                        {
                            for (int i = chunkStart; i < end; i++)
                            {
                                try
                                {
                                    SymbolAnnotationEngine.RemoveAnnotation(ctx.Doc, tags[i].Id);
                                    ctx.Doc.Delete(tags[i].Id);
                                    removed++;
                                }
                                catch (Exception ex) { StingLog.Warn($"RemoveSymbolsProjectWide [{i}]: {ex.Message}"); }
                            }
                            tx.Commit();
                        }
                        catch (Exception chunkEx)
                        {
                            StingLog.Error($"RemoveSymbolsProjectWide chunk {chunkStart}-{end}", chunkEx);
                            try { tx.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"RemoveSymbolsProjectWide rollback: {rbEx.Message}"); }
                        }
                    }
                }
                tg.Assimilate();
            }

            TaskDialog.Show("STING", $"Removed {removed}/{tags.Count} symbol overlay(s) across {perView.Count} view(s).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Companion to <see cref="PlaceSymbolsInViewCommand"/>: deletes
    /// every STING symbol overlay (and its associated label TextNote)
    /// from the active view. Restricts to tags carrying
    /// <c>STING_SYMBOL_ID</c> so non-STING tags are untouched.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveSymbolsInViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null || ctx.ActiveView == null)
            { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            var tags = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !string.IsNullOrEmpty(t.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                .ToList();
            if (tags.Count == 0)
            {
                TaskDialog.Show("STING", $"No STING symbol overlays in {ctx.ActiveView.Name}.");
                return Result.Succeeded;
            }

            var dlg = new TaskDialog("STING - Remove Symbols")
            {
                MainInstruction = $"Delete {tags.Count} STING symbol overlay(s) from {ctx.ActiveView.Name}?",
                MainContent = "The host MEP elements are not touched. Associated label TextNotes are also removed.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int removed = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Remove Symbols In View"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        SymbolAnnotationEngine.RemoveAnnotation(ctx.Doc, tag.Id);
                        ctx.Doc.Delete(tag.Id);
                        removed++;
                    }
                    catch (Exception ex) { StingLog.Warn($"RemoveSymbolsInView inner: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING", $"Removed {removed} symbol overlay(s) from {ctx.ActiveView.Name}.");
            return Result.Succeeded;
        }
    }
}
