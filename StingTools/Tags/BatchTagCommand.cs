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
                try { TaskDialog.Show("STING Tools", $"Batch Tag failed:\n{ex.Message}"); } catch { }
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
            var allElements = collector.ToList();

            int totalTaggable = 0, alreadyTagged = 0, untagged = 0;
            var taggableElements = new List<Element>();

            foreach (Element e in allElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;
                totalTaggable++;
                taggableElements.Add(e);
                if (TagConfig.TagIsComplete(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    alreadyTagged++;
                else
                    untagged++;
            }

            // Step 2: Choose collision handling mode (with pre-flight counts)
            TaskDialog modeDlg = new TaskDialog("Batch Tag — Collision Mode");
            modeDlg.MainInstruction = $"Batch tag {totalTaggable:N0} elements";
            modeDlg.MainContent =
                $"  Taggable:       {totalTaggable:N0}\n" +
                $"  Already tagged: {alreadyTagged:N0}\n" +
                $"  Untagged:       {untagged:N0}\n\n" +
                "Click an option below to start tagging:";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Skip existing — tag {untagged:N0} new only (Recommended)",
                "Only tag untagged elements. Already-tagged elements are left unchanged.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Overwrite all {totalTaggable:N0}",
                "Re-derive and overwrite ALL tag tokens, even on already-tagged elements.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                $"Auto-increment on collision",
                "Tag untagged elements; if a generated tag collides with an existing one, auto-increment SEQ.");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            modeDlg.DefaultButton = TaskDialogResult.CommandLink1;

            TagCollisionMode collisionMode;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    collisionMode = TagCollisionMode.Skip;
                    break;
                case TaskDialogResult.CommandLink2:
                    collisionMode = TagCollisionMode.Overwrite;
                    break;
                case TaskDialogResult.CommandLink3:
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
            if (popCtx == null)
            {
                TaskDialog.Show("Batch Tag", "Failed to build population context.");
                return Result.Failed;
            }
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();

            StingLog.Info($"Batch Tag: starting — {totalTaggable} taggable, {alreadyTagged} tagged, mode={collisionMode}");

            bool cancelled = false;
            const int TagBatchSize = 500;

            // ENH-001: Show progress dialog with cancel support
            // NOTE: On cancellation, previously committed batches remain (partial commit by design).
            // The current in-progress batch is rolled back. User is notified of partial completion.
            var progress = StingProgressDialog.Show("Batch Tag", totalTaggable);

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
                            TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                                tagIndex, sequenceCounters, formulas, gridLines,
                                overwrite: overwriteMode, skipComplete: skipComplete,
                                collisionMode: collisionMode, stats: stats);
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

            progress.Close();
            ComplianceScan.InvalidateCache();
            // FIX-13: Invalidate auto-tagger cached context after batch tagging
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
        internal static List<Element> SmartSortElements(Document doc, List<Element> elements)
        {
            // Build level elevation lookup
            var levelElevation = new Dictionary<ElementId, double>();
            foreach (Level lvl in new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>())
            {
                levelElevation[lvl.Id] = lvl.Elevation;
            }

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
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Collect all tagged elements
            var tagged = new List<(Element el, string currentTag)>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
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

            // Execute migration
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var stats = new TaggingStats();
            int migrated = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag Format Migration"))
            {
                tx.Start();

                foreach (var (el, currentTag) in tagged)
                {
                    try
                    {
                        // Phase2: Bridge native params before tag format migration
                        try { NativeParamMapper.MapAll(doc, el); }
                        catch (Exception nmEx) { StingLog.Warn($"Migration NativeMapper for {el.Id}: {nmEx.Message}"); }

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

                        migrated++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Migration failed for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
                TagConfig.SaveSeqSidecar(doc, seqCounters);
            }
            // FIX-14: Invalidate caches after format migration
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();

            TaskDialog.Show("Tag Format Migration",
                $"Migration complete.\n\n  Migrated: {migrated}\n  Total: {tagged.Count}");
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
            Document doc = ctx.Doc;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int scanned = 0, stale = 0, updated = 0;
            var staleSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["LVL"] = 0, ["LOC"] = 0, ["ZONE"] = 0,
                ["SYS"] = 0, ["FUNC"] = 0, ["PROD"] = 0
            };

            var staleElements = new List<(Element el, string token, string stored, string current)>();

            // Phase 1: Scan for stale tokens
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
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

            using (Transaction tx = new Transaction(doc, "STING Delta Tag Update"))
            {
                tx.Start();

                foreach (var (el, token, stored, current) in staleElements)
                {
                    try
                    {
                        // Update the stale token
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
                            ParameterHelpers.SetString(el, paramName, current, overwrite: true);

                        // Rebuild tag only once per element
                        if (processedElements.Add(el.Id))
                        {
                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: false,
                                existingTags: tagIndex,
                                collisionMode: TagCollisionMode.Overwrite,
                                stats: null);

                            // FIX-04: Bridge native params after delta token update
                            try { NativeParamMapper.MapAll(doc, el); }
                            catch (Exception nmEx) { StingLog.Warn($"TagChanged NativeMapper for {el.Id}: {nmEx.Message}"); }

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

                            updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Delta update failed for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
                TagConfig.SaveSeqSidecar(doc, seqCounters);
            }
            TaskDialog.Show("Tag Changed",
                $"Delta update complete.\n\n  Stale tokens: {stale}\n  Elements updated: {updated}\n  Tags rebuilt: {processedElements.Count}");
            StingLog.Info($"Delta tagging: {stale} stale tokens, {updated} elements updated");
            return Result.Succeeded;
        }
    }
}
