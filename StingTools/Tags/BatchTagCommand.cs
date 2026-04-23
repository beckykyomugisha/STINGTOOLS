using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Tags
{
    /// <summary>
    /// Batch-apply ISO 19650 tags to ALL taggable elements in the entire project model.
    ///
    /// Intelligence layers:
    ///   1. Smart element ordering: groups by Level → Discipline → Category for contiguous
    ///      sequence numbers (all HVAC on L01 get consecutive SEQ before moving to L02)
    ///   2. Pre-flight validation: counts taggable/tagged/untagged before starting
    ///   3. Full 9-token auto-population via TokenAutoPopulator (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV)
    ///   4. Phase-aware STATUS auto-detection from Revit phases/worksets
    ///   5. REV auto-population from project revision sequence
    ///   6. Family-aware PROD codes (35+ specific identifiers)
    ///   7. MEP system-aware SYS derivation from connected systems
    ///   8. O(1) collision detection with configurable resolution (Skip/Overwrite/AutoIncrement)
    ///   9. Rich post-batch reporting: per-discipline, per-level, collision depth stats
    ///  10. Progress logging every 500 elements for monitoring
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try { return ExecuteCore(commandData, ref message, elements); }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("BatchTagCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Batch Tag failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Step 1: Pre-flight scan — collect and classify all elements
            // Performance: use ElementMulticategoryFilter to skip non-taggable elements at API level
            var catEnums = SharedParamGuids.AllCategoryEnums;
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            if (catEnums != null && catEnums.Length > 0)
                collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            var allElements = collector.Cast<Element>();

            int totalTaggable = 0, alreadyTagged = 0, untagged = 0;
            int skippedWorkset = 0, skippedDemolished = 0;
            var taggableElements = new List<Element>();

            foreach (Element e in allElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                // GAP-WS-01: Skip elements on worksets owned by other users in workshared models
                if (!TagPipelineHelper.IsEditableInWorksharing(doc, e))
                { skippedWorkset++; continue; }

                // GAP-PH-01: Skip demolished elements (tagged with STATUS=DEMOLISHED but don't
                // waste SEQ numbers; they remain tagged from their creation phase)
                if (TagPipelineHelper.IsDemolished(e))
                { skippedDemolished++; continue; }

                totalTaggable++;
                taggableElements.Add(e);
                if (TagConfig.TagIsComplete(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    alreadyTagged++;
                else
                    untagged++;
            }

            // BATCH-01: Early exit if no taggable elements in project
            if (totalTaggable == 0)
            {
                TaskDialog.Show("Batch Tag", "No taggable elements found in this project.\n\n" +
                    "Ensure the model contains elements in categories defined in the tag configuration " +
                    "(Mechanical Equipment, Electrical Equipment, Lighting Fixtures, etc.).");
                return Result.Succeeded;
            }

            // Step 2: Choose collision handling mode (with pre-flight counts)
            var modeOptions = new List<UI.StingModePicker.ModeOption>
            {
                new($"Skip existing — tag {untagged:N0} new only",
                    "Only tag untagged elements. Already-tagged elements are left unchanged.", "skip", true),
                new($"Overwrite all {totalTaggable:N0}",
                    "Re-derive and overwrite ALL tag tokens, even on already-tagged elements.", "overwrite"),
                new("Auto-increment on collision",
                    "Tag untagged elements; if a generated tag collides with an existing one, auto-increment SEQ.", "increment"),
            };
            string statusLine = $"Taggable: {totalTaggable:N0}  |  Already tagged: {alreadyTagged:N0}  |  Untagged: {untagged:N0}";
            if (skippedWorkset > 0) statusLine += $"  |  Skipped (workset): {skippedWorkset:N0}";
            if (skippedDemolished > 0) statusLine += $"  |  Skipped (demolished): {skippedDemolished:N0}";
            string modeResult = UI.StingModePicker.Show(
                "Batch Tag — Collision Mode",
                $"Batch tag {totalTaggable:N0} elements",
                modeOptions,
                statusLine);

            TagCollisionMode collisionMode;
            switch (modeResult)
            {
                case "skip":
                    collisionMode = TagCollisionMode.Skip;
                    break;
                case "overwrite":
                    collisionMode = TagCollisionMode.Overwrite;
                    break;
                case "increment":
                    collisionMode = TagCollisionMode.AutoIncrement;
                    break;
                default:
                    return Result.Cancelled;
            }

            // Step 3: Smart ordering — sort by Level → Discipline → Category
            // This ensures contiguous SEQ numbers per group (all HVAC on L01 together)
            var sorted = SmartSortElements(doc, taggableElements);

            var (tagIndex, sequenceCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            if (popCtx == null || !popCtx.IsValid())
            {
                string diag = popCtx?.DiagnosticSummary ?? "Context build returned null";
                StingLog.Error($"BatchTag: PopulationContext failed — {diag}");
                TaskDialog.Show("Batch Tag",
                    $"Failed to build population context.\n\nDiagnostics: {diag}\n\n" +
                    "Check: rooms placed? Levels defined? Shared parameters bound?");
                return Result.Failed;
            }
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();

            StingLog.Info($"Batch Tag: starting — {totalTaggable} taggable, {alreadyTagged} tagged, mode={collisionMode}");
            using var _perfOp = PerformanceTracker.Track("BatchTag");

            bool cancelled = false;
            const int TagBatchSize = 500;

            // ENH-001: Show progress dialog with cancel support
            // NOTE: On cancellation, previously committed batches remain (partial commit by design).
            // The current in-progress batch is rolled back. User is notified of partial completion.
            var progress = StingProgressDialog.Show("Batch Tag", totalTaggable);

            try
            {
            for (int batchStart = 0; batchStart < sorted.Count; batchStart += TagBatchSize)
            {
                if (cancelled) break;

                int batchEnd = Math.Min(batchStart + TagBatchSize, sorted.Count);
                int batchNum = (batchStart / TagBatchSize) + 1;

                using (Transaction tx = new Transaction(doc, $"STING Batch Tag #{batchNum}"))
                {
                    tx.Start();

                    for (int idx = batchStart; idx < batchEnd; idx++)
                    {
                        Element el = sorted[idx];

                        // Check for user cancellation via progress dialog
                        if (progress.IsCancelled)
                        {
                            StingLog.Info($"Batch Tag: cancelled by user at {idx}/{totalTaggable}");
                            cancelled = true;
                            break;
                        }

                        try
                        {
                            bool overwriteMode = (collisionMode == TagCollisionMode.Overwrite);
                            bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                            bool pipelineOk = TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                                tagIndex, sequenceCounters, formulas, gridLines,
                                overwrite: overwriteMode, skipComplete: skipComplete,
                                collisionMode: collisionMode, stats: stats);
                            if (!pipelineOk)
                                StingLog.Warn($"BatchTag: pipeline returned false for element {el?.Id}");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Error($"BatchTag: failed on element {el?.Id}: {ex.Message}", ex);
                            stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                        }

                        progress.Increment($"Tagged {stats.TotalTagged}, collisions {stats.TotalCollisions}");

                        if ((batchStart + (idx - batchStart)) % 500 == 0 && idx > 0)
                            StingLog.Info($"Batch Tag progress: {idx}/{totalTaggable} " +
                                $"({stats.TotalTagged} tagged, {stats.TotalCollisions} collisions)");
                    }

                    if (cancelled)
                    {
                        tx.RollBack();
                    }
                    else
                    {
                        tx.Commit();
                        // P6: Save SEQ sidecar after each committed batch
                        TagConfig.SaveSeqSidecar(doc, sequenceCounters);
                        StingLog.Info($"Batch Tag: batch {batchNum} committed");
                    }
                }
            }
            }
            finally
            {
                progress.Close();
            }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            TagConfig.CheckComplianceGate(doc, "BatchTag");

            // BIM integration: auto-raise compliance issues after batch tagging
            try { StingTools.BIMManager.BIMManagerEngine.AutoRaiseComplianceIssues(doc); }
            catch (Exception ex) { StingLog.Warn($"BatchTag BIM integration: {ex.Message}"); }

            if (cancelled)
            {
                TaskDialog.Show("Batch Tag", $"Cancelled by user.\nPartially completed batches were committed.");
                return Result.Cancelled;
            }
            sw.Stop();

            // Step 4: Rich reporting
            var report = new StringBuilder();
            report.AppendLine("Batch Tagging Complete");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Mode:         {collisionMode}");
            report.AppendLine($"  Duration:     {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            StingLog.Info($"Batch Tag: tagged={stats.TotalTagged}, skipped={stats.TotalSkipped}, " +
                $"collisions={stats.TotalCollisions}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            // GAP-017: Post-batch compliance summary for workflow chain visibility
            var postScan = ComplianceScan.Scan(doc);
            if (postScan != null)
            {
                report.AppendLine();
                report.AppendLine($"Compliance: {postScan.StatusBarText}");
                StingLog.Info($"Batch Tag post-compliance: {postScan.StatusBarText}");
            }

            TaskDialog td = new TaskDialog("Batch Tag");
            td.MainInstruction = $"Tagged {stats.TotalTagged:N0} of {totalTaggable:N0} elements";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        /// <summary>
        /// Smart sort: Level (elevation ascending) -> Discipline -> SYS -> Category.
        /// Ensures contiguous sequence numbers within each group — all HVAC on L01
        /// get SEQ 0001-0050 before moving to DCW on L01, then L02.
        /// The SYS sort key groups elements by system type within each discipline,
        /// matching the SEQ key format (DISC_SYS_LVL) for optimal numbering.
        /// </summary>
        // TAG-H-05: Single atomic tuple replaces two separate static fields — prevents torn read
        // where one thread sees new docKey but old elevations (or vice versa).
        private static (string docKey, Dictionary<ElementId, double> elevations) _levelElevationCache;

        /// <summary>TAG-SORT-LEVEL-01: Clear cached level elevations on document close to prevent
        /// stale elevation data from a closed document being used for the next opened document.</summary>
        internal static void ClearLevelElevationCache()
        {
            _levelElevationCache = default;
        }

        internal static List<Element> SmartSortElements(Document doc, List<Element> elements)
        {
            // Build level elevation lookup (cached per document)
            // TAG-H-05: Read snapshot of atomic tuple — both fields from the same write.
            string docKey = doc.PathName ?? doc.Title ?? "";
            var cachedSnapshot = _levelElevationCache;
            if (cachedSnapshot.elevations == null || cachedSnapshot.docKey != docKey)
            {
                var newElevations = new Dictionary<ElementId, double>();
                foreach (Level lvl in new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>())
                {
                    newElevations[lvl.Id] = lvl.Elevation;
                }
                // TAG-H-05: Assign atomically as a single tuple write.
                _levelElevationCache = (docKey, newElevations);
                cachedSnapshot = _levelElevationCache;
            }
            var levelElevation = cachedSnapshot.elevations;

            // Pre-cache category, discipline, and system per element to avoid
            // redundant GetCategoryName/GetMepSystemAwareSysCode calls in sort keys
            var sortCache = new Dictionary<long, (string cat, string disc, string sys)>();
            foreach (var e in elements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                string sys = TagConfig.GetMepSystemAwareSysCode(e, cat);
                if (string.IsNullOrEmpty(sys))
                    sys = TagConfig.GetDiscDefaultSysCode(disc);
                sortCache[e.Id.Value] = (cat, disc, sys);
            }

            return elements.OrderBy(e =>
                {
                    ElementId lvlId = e.LevelId;
                    if (lvlId != null && lvlId != ElementId.InvalidElementId &&
                        levelElevation.TryGetValue(lvlId, out double elev))
                        return elev;
                    return double.MaxValue;
                })
                .ThenBy(e => sortCache[e.Id.Value].disc)
                .ThenBy(e => sortCache[e.Id.Value].sys)
                .ThenBy(e => sortCache[e.Id.Value].cat)
                .ThenBy(e => e.Id.Value) // Stable sort: consistent ordering across runs
                .ToList();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ENH-007: Tag Format Migration Tool
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Preview and migrate existing tags when separator or padding changes.
    /// Shows current vs new format for a sample, counts affected tags,
    /// and offers a "Migrate All" transaction using BuildAndWriteTag(overwrite=true).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagFormatMigrationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // FIX-V01-A: Scope selection dialog (previously always ran project-wide silently)
            var mfScopeDlg = new TaskDialog("Tag Format Migration — Scope");
            mfScopeDlg.MainInstruction = "Choose migration scope";
            mfScopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active view only", "Migrate tags in the current view");
            mfScopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected elements", $"{(uidoc?.Selection.GetElementIds().Count ?? 0)} selected");
            mfScopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire project", "Migrate all tagged elements in the model");
            mfScopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            IEnumerable<Element> mfScanSource;
            string mfScopeLabel;
            switch (mfScopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    if (ctx.ActiveView == null)
                    { TaskDialog.Show("Tag Format Migration", "No active view."); return Result.Failed; }
                    mfScanSource = new FilteredElementCollector(doc, ctx.ActiveView.Id)
                        .WhereElementIsNotElementType();
                    mfScopeLabel = $"active view '{ctx.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink2:
                    var mfSelIds = uidoc?.Selection.GetElementIds();
                    if (mfSelIds == null || mfSelIds.Count == 0)
                    { TaskDialog.Show("Tag Format Migration", "No elements selected."); return Result.Cancelled; }
                    mfScanSource = mfSelIds.Select(id => doc.GetElement(id)).Where(e => e != null);
                    mfScopeLabel = $"{mfSelIds.Count} selected elements";
                    break;
                case TaskDialogResult.CommandLink3:
                    mfScanSource = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    mfScopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            // Collect all tagged elements within scope
            var tagged = new List<(Element el, string currentTag)>();
            foreach (Element el in mfScanSource)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag) && tag.Contains(ParamRegistry.Separator))
                    tagged.Add((el, tag));
            }

            if (tagged.Count == 0)
            {
                TaskDialog.Show("Tag Format Migration", "No tagged elements found.");
                return Result.Succeeded;
            }

            // Preview: show sample of 10 elements with current vs rebuilt tag
            var preview = new StringBuilder();
            preview.AppendLine("Tag Format Migration Preview");
            preview.AppendLine(new string('═', 60));
            preview.AppendLine($"  Current separator: \"{ParamRegistry.Separator}\"");
            preview.AppendLine($"  Current padding:   {ParamRegistry.NumPad}");
            preview.AppendLine($"  Tagged elements:   {tagged.Count}");
            preview.AppendLine($"  Scope:             {mfScopeLabel}");
            preview.AppendLine();

            int wouldChange = 0;
            int sampleCount = Math.Min(tagged.Count, 10);
            preview.AppendLine("  Sample (first 10):");

            // Single pass: count changes and build sample preview simultaneously
            for (int i = 0; i < tagged.Count; i++)
            {
                var (el, currentTag) = tagged[i];
                string[] tokens = ParamRegistry.ReadTokenValues(el);
                string rebuilt = string.Join(ParamRegistry.Separator, tokens);
                // Apply PREFIX/SUFFIX to match actual tag format for accurate comparison
                if (!string.IsNullOrEmpty(TagConfig.TagPrefix))
                    rebuilt = TagConfig.TagPrefix + ParamRegistry.Separator + rebuilt;
                if (!string.IsNullOrEmpty(TagConfig.TagSuffix))
                    rebuilt = rebuilt + ParamRegistry.Separator + TagConfig.TagSuffix;
                bool changed = !string.Equals(currentTag, rebuilt, StringComparison.Ordinal);
                if (changed) wouldChange++;

                if (i < sampleCount)
                {
                    if (changed)
                    {
                        preview.AppendLine($"    {currentTag}");
                        preview.AppendLine($"  → {rebuilt}");
                    }
                    else
                    {
                        preview.AppendLine($"    {currentTag} (unchanged)");
                    }
                }
            }

            preview.AppendLine();
            preview.AppendLine($"  Total needing migration: {wouldChange} / {tagged.Count}");

            if (wouldChange == 0)
            {
                TaskDialog.Show("Tag Format Migration", preview.ToString() +
                    "\n\nAll tags are already in the current format. No migration needed.");
                return Result.Succeeded;
            }

            TaskDialog td = new TaskDialog("Tag Format Migration");
            td.MainInstruction = $"{wouldChange} tags need reformatting";
            td.MainContent = preview.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Migrate All ({wouldChange} tags)",
                "Rebuild all tags using current format settings");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (td.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            // FIX-V01-B: Build population context and load formulas for token re-derivation
            var mfPopCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var mfFormulas = TagPipelineHelper.LoadFormulas();

            // Execute migration
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var stats = new TaggingStats();
            // FIX-R06: Load grid lines for GridRef pipeline step
            var mfGridLines = TagPipelineHelper.LoadGridLines(doc);
            int migrated = 0;

            // TAG-H-04: Chunked 200-element batches with StingProgressDialog and Escape cancellation.
            // Commits after each batch so Revit memory is not exhausted on large models.
            // seqCounters and tagIndex carry across batch boundaries for collision-free SEQ.
            var mfProgress = StingProgressDialog.Show("Tag Format Migration", tagged.Count);
            bool mfCancelled = false;
            const int MfBatchSize = 200;
            int mfBatchStart = 0;
            while (mfBatchStart < tagged.Count)
            {
                if (EscapeChecker.IsEscapePressed())
                {
                    mfCancelled = true;
                    break;
                }

                int mfBatchEnd = Math.Min(mfBatchStart + MfBatchSize, tagged.Count);
                var mfBatch = tagged.GetRange(mfBatchStart, mfBatchEnd - mfBatchStart);
                mfBatchStart = mfBatchEnd;

                using (Transaction tx = new Transaction(doc, "STING Tag Format Migration"))
                {
                    tx.Start();

                    foreach (var (el, currentTag) in mfBatch)
                    {
                        if (mfProgress != null) mfProgress.Increment($"Migrating {el.Id}");
                        try
                        {
                            // FIX-V01-C: Re-derive tokens before format rebuild so stale/wrong
                            // tokens are corrected, not just reformatted
                            if (mfPopCtx != null)
                            {
                                try
                                {
                                    TokenAutoPopulator.TypeTokenInherit(doc, el);
                                    TokenAutoPopulator.PopulateAll(doc, el, mfPopCtx, overwrite: false);
                                }
                                catch (Exception popEx)
                                {
                                    StingLog.Warn($"TagFormatMigration Populate for {el.Id}: {popEx.Message}");
                                }
                            }

                            // FIX-V01-C: Bridge native params before tag format migration
                            try { NativeParamMapper.MapAll(doc, el); }
                            catch (Exception nmEx) { StingLog.Warn($"TagFormatMigration NativeMapper for {el.Id}: {nmEx.Message}"); }

                            // FIX-V01-C: Evaluate formulas after populate + native mapping
                            if (mfFormulas != null && mfFormulas.Count > 0)
                            {
                                try
                                {
                                    foreach (var formula in mfFormulas)
                                    {
                                        try
                                        {
                                            Parameter fp = el.LookupParameter(formula.ParameterName);
                                            if (fp == null || fp.IsReadOnly) continue;
                                            var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                            if (fCtx == null) continue;
                                            if (formula.DataType == "TEXT")
                                            {
                                                string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                                if (fResult != null && fp.StorageType == StorageType.String
                                                    && string.IsNullOrEmpty(fp.AsString()))
                                                    fp.Set(fResult);
                                            }
                                            else
                                            {
                                                double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                                if (fResult.HasValue && !double.IsNaN(fResult.Value)
                                                    && !double.IsInfinity(fResult.Value))
                                                    Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                            }
                                        }
                                        catch (Exception frmEx) { StingLog.Warn($"Formula eval for element {el.Id}: {frmEx.Message}"); }
                                    }
                                }
                                catch (Exception fExMf)
                                {
                                    StingLog.Warn($"TagFormatMigration formula eval for {el.Id}: {fExMf.Message}");
                                }
                            }

                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: false,
                                existingTags: tagIndex,
                                collisionMode: TagCollisionMode.Overwrite,
                                stats: stats);

                            // Write TAG7 + containers with migrated tag
                            try
                            {
                                string catName = ParameterHelpers.GetCategoryName(el);
                                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                                TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: true);
                                // NP3: Write containers after format migration
                                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true,
                                    skipParam: ParamRegistry.TAG1);
                            }
                            catch (Exception tag7Ex)
                            {
                                StingLog.Warn($"Migration TAG7+containers for {el.Id}: {tag7Ex.Message}");
                            }

                            // FIX-R06: Write GridRef per element
                            if (mfGridLines != null && mfGridLines.Count > 0)
                            {
                                try
                                {
                                    string gridRef = SpatialAutoDetect.GetGridRef(el, mfGridLines);
                                    if (!string.IsNullOrEmpty(gridRef))
                                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef);
                                }
                                catch (Exception grEx) { StingLog.Warn($"Migration GridRef for {el.Id}: {grEx.Message}"); }
                            }

                            migrated++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Migration failed for {el.Id}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
                // TAG-H-04: Save sidecar after each batch so partial progress survives a crash.
                try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                catch (Exception batchSsEx) { StingLog.Warn($"TagFormatMigration batch sidecar: {batchSsEx.Message}"); }
            } // end batch loop
            mfProgress?.Close();
            // Final sidecar save + cache invalidation after all batches complete.
            // (Per-batch sidecar saves already happened in the loop above.)
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"TagFormatMigration SaveSeqSidecar: {ssEx.Message}"); }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            string mfCancelNote = mfCancelled ? " (cancelled — partial)" : "";
            TaskDialog.Show("Tag Format Migration",
                $"Migration{mfCancelNote} complete.\n\n  Scope:    {mfScopeLabel}\n  Migrated: {migrated}\n  Total:    {tagged.Count}");
            StingLog.Info($"Tag format migration: {migrated}/{tagged.Count} tags reformatted");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ENH-009: Incremental Delta Tagging — Update Stale Spatial Tokens
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detects and updates elements whose spatial tokens (LVL, LOC, ZONE) are stale
    /// due to geometry changes (level moves, room reassignment). Compares current
    /// stored values against freshly-derived values and updates mismatches.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagChangedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            // FIX-N02: Scope selection dialog — scan view, selection, or entire project
            var selected = uidoc.Selection.GetElementIds();
            IEnumerable<Element> scanScope;
            string scopeLabel;
            if (selected.Count > 0)
            {
                TaskDialog scopeTd = new TaskDialog("Delta Scan Scope");
                scopeTd.MainInstruction = "Scan scope for stale tokens";
                scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Selected elements ({selected.Count})",
                    "Only scan currently selected elements");
                scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Entire project",
                    "Scan all tagged elements in the project");
                scopeTd.CommonButtons = TaskDialogCommonButtons.Cancel;
                var scopeResult = scopeTd.Show();
                if (scopeResult == TaskDialogResult.Cancel)
                    return Result.Cancelled;
                if (scopeResult == TaskDialogResult.CommandLink1)
                {
                    scanScope = selected.Select(id => doc.GetElement(id)).Where(e => e != null);
                    scopeLabel = $"selection ({selected.Count})";
                }
                else
                {
                    scanScope = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    scopeLabel = "project";
                }
            }
            else
            {
                scanScope = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                scopeLabel = "project";
            }

            int scanned = 0, stale = 0, updated = 0;
            var staleSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["LVL"] = 0, ["LOC"] = 0, ["ZONE"] = 0,
                ["SYS"] = 0, ["FUNC"] = 0, ["PROD"] = 0
            };

            var staleElements = new List<(Element el, string token, string stored, string current)>();

            // Phase 1: Scan for stale tokens
            foreach (Element el in scanScope)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) continue; // Only check already-tagged elements
                scanned++;

                // Check LVL
                string storedLvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                string currentLvl = ParameterHelpers.GetLevelCode(doc, el);
                if (!string.IsNullOrEmpty(storedLvl) && currentLvl != "XX" &&
                    !storedLvl.Equals(currentLvl, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "LVL", storedLvl, currentLvl));
                    staleSummary["LVL"]++;
                }

                // Check LOC
                string storedLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string currentLoc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                if (!string.IsNullOrEmpty(storedLoc) && !string.IsNullOrEmpty(currentLoc) &&
                    currentLoc != "XX" &&
                    !storedLoc.Equals(currentLoc, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "LOC", storedLoc, currentLoc));
                    staleSummary["LOC"]++;
                }

                // Check ZONE
                string storedZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string currentZone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                if (!string.IsNullOrEmpty(storedZone) && !string.IsNullOrEmpty(currentZone) &&
                    currentZone != "XX" && currentZone != "ZZ" &&
                    !storedZone.Equals(currentZone, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "ZONE", storedZone, currentZone));
                    staleSummary["ZONE"]++;
                }

                // P7 / G4.1: Check SYS
                string catName = cat;
                string storedSys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string currentSys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                if (!string.IsNullOrEmpty(storedSys) && !string.IsNullOrEmpty(currentSys)
                    && currentSys != "GEN"
                    && !storedSys.Equals(currentSys, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "SYS", storedSys, currentSys));
                    staleSummary["SYS"]++;
                }

                // P7 / G4.1: Check FUNC
                string storedFunc = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
                string currentFunc = TagConfig.GetSmartFuncCode(el, currentSys ?? storedSys);
                if (!string.IsNullOrEmpty(storedFunc) && !string.IsNullOrEmpty(currentFunc)
                    && currentFunc != "GEN"
                    && !storedFunc.Equals(currentFunc, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "FUNC", storedFunc, currentFunc));
                    staleSummary["FUNC"]++;
                }

                // P7 / G4.1: Check PROD (family type change)
                string storedProd = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                string currentProd = TagConfig.GetFamilyAwareProdCode(el, catName);
                if (!string.IsNullOrEmpty(storedProd) && !string.IsNullOrEmpty(currentProd)
                    && currentProd != "GEN"
                    && !storedProd.Equals(currentProd, StringComparison.OrdinalIgnoreCase))
                {
                    staleElements.Add((el, "PROD", storedProd, currentProd));
                    staleSummary["PROD"]++;
                }
            }

            stale = staleElements.Count;

            if (stale == 0)
            {
                TaskDialog.Show("Tag Changed",
                    $"Scanned {scanned} tagged elements.\nAll spatial tokens are current. No updates needed.");
                return Result.Succeeded;
            }

            // Show preview
            var preview = new StringBuilder();
            preview.AppendLine($"Scanned: {scanned} tagged elements");
            preview.AppendLine($"Stale:   {stale} token mismatches found");
            preview.AppendLine();
            if (staleSummary["LVL"] > 0) preview.AppendLine($"  LVL changes:  {staleSummary["LVL"]}");
            if (staleSummary["LOC"] > 0) preview.AppendLine($"  LOC changes:  {staleSummary["LOC"]}");
            if (staleSummary["ZONE"] > 0) preview.AppendLine($"  ZONE changes: {staleSummary["ZONE"]}");
            if (staleSummary["SYS"] > 0) preview.AppendLine($"  SYS changes:  {staleSummary["SYS"]}");
            if (staleSummary["FUNC"] > 0) preview.AppendLine($"  FUNC changes: {staleSummary["FUNC"]}");
            if (staleSummary["PROD"] > 0) preview.AppendLine($"  PROD changes: {staleSummary["PROD"]}");

            TaskDialog td = new TaskDialog("Tag Changed — Delta Update");
            td.MainInstruction = $"{stale} stale spatial tokens found";
            td.MainContent = preview.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Update {stale} tokens and rebuild tags",
                "Update spatial tokens and rebuild TAG1 + containers");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (td.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            // Phase 2: Update stale tokens and rebuild tags
            var processedElements = new HashSet<ElementId>();
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            // FIX-R03: Load formulas and grid lines for delta pipeline
            var tcFormulas = TagPipelineHelper.LoadFormulas();
            var tcGridLines = TagPipelineHelper.LoadGridLines(doc);

            using (Transaction tx = new Transaction(doc, "STING Delta Tag Update"))
            {
                tx.Start();

                foreach (var (el, token, stored, current) in staleElements)
                {
                    try
                    {
                        // Update the stale token (skip if locked)
                        string paramName = token switch
                        {
                            "LVL" => ParamRegistry.LVL,
                            "LOC" => ParamRegistry.LOC,
                            "ZONE" => ParamRegistry.ZONE,
                            "SYS" => ParamRegistry.SYS,
                            "FUNC" => ParamRegistry.FUNC,
                            "PROD" => ParamRegistry.PROD,
                            _ => null
                        };
                        if (paramName != null)
                        {
                            // Respect token lock: skip overwrite if this token is locked
                            string lockStr = ParameterHelpers.GetString(el, ParamRegistry.Ext("TOKEN_LOCK"));
                            bool isLocked = !string.IsNullOrEmpty(lockStr) &&
                                lockStr.Split(',').Any(lk => lk.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
                            if (!isLocked)
                                ParameterHelpers.SetString(el, paramName, current, overwrite: true);
                        }

                        // Rebuild tag only once per element (first stale token triggers full rebuild)
                        if (processedElements.Add(el.Id))
                        {
                            // Inherit type-level tokens before tag rebuild
                            try { TokenAutoPopulator.TypeTokenInherit(doc, el); }
                            catch (Exception tiEx) { StingLog.Warn($"TagChanged TypeTokenInherit for {el.Id}: {tiEx.Message}"); }
                            // Bridge native params BEFORE tag assembly so dependent values are current
                            try { NativeParamMapper.MapAll(doc, el); }
                            catch (Exception nmEx) { StingLog.Warn($"TagChanged NativeMapper for {el.Id}: {nmEx.Message}"); }

                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: false,
                                existingTags: tagIndex,
                                collisionMode: TagCollisionMode.Overwrite,
                                stats: null);

                            // FIX-R03: Evaluate formulas after native mapper
                            if (tcFormulas != null && tcFormulas.Count > 0)
                            {
                                try
                                {
                                    foreach (var formula in tcFormulas)
                                    {
                                        Parameter fp = el.LookupParameter(formula.ParameterName);
                                        if (fp == null || fp.IsReadOnly) continue;
                                        var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                        if (fCtx == null) continue;
                                        if (formula.DataType == "TEXT")
                                        {
                                            string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                            if (fResult != null && fp.StorageType == StorageType.String)
                                                fp.Set(fResult);
                                        }
                                        else
                                        {
                                            double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                            if (fResult.HasValue && !double.IsNaN(fResult.Value) && !double.IsInfinity(fResult.Value))
                                                Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                        }
                                    }
                                }
                                catch (Exception fEx) { StingLog.Warn($"TagChanged formula eval for {el.Id}: {fEx.Message}"); }
                            }

                            // Write TAG7 + containers with updated spatial tokens
                            try
                            {
                                string catName = ParameterHelpers.GetCategoryName(el);
                                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                                TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: true);
                                // NP4: Write containers after delta token update
                                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true,
                                    skipParam: ParamRegistry.TAG1);
                            }
                            catch (Exception tag7Ex)
                            {
                                StingLog.Warn($"TagChanged TAG7+containers for {el.Id}: {tag7Ex.Message}");
                            }

                            // FIX-R03: Write GridRef per element
                            if (tcGridLines != null && tcGridLines.Count > 0)
                            {
                                try
                                {
                                    string gridRef = SpatialAutoDetect.GetGridRef(el, tcGridLines);
                                    if (!string.IsNullOrEmpty(gridRef))
                                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef);
                                }
                                catch (Exception grEx) { StingLog.Warn($"TagChanged GridRef for {el.Id}: {grEx.Message}"); }
                            }

                            updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Delta update failed for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }
            // Save SEQ sidecar once + invalidate caches after delta update
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"TagChanged SaveSeqSidecar: {ssEx.Message}"); }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            TaskDialog.Show("Tag Changed",
                $"Delta update complete.\n\n  Stale tokens: {stale}\n  Elements updated: {updated}\n  Tags rebuilt: {processedElements.Count}");
            StingLog.Info($"Delta tagging: {stale} stale tokens, {updated} elements updated");
            return Result.Succeeded;
        }
    }
}
