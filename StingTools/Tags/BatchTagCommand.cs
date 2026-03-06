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
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Step 1: Pre-flight scan — collect and classify all elements
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            int totalTaggable = 0, alreadyTagged = 0, untagged = 0;
            var taggableElements = new List<Element>();

            foreach (Element e in allElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (!known.Contains(cat)) continue;
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
                "Choose collision handling:";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Skip existing — tag {untagged:N0} new only",
                "Only tag untagged elements. Already-tagged elements are left unchanged.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Overwrite all {totalTaggable:N0}",
                "Re-derive and overwrite ALL tag tokens, even on already-tagged elements.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                $"Auto-increment on collision",
                "Tag untagged elements; if a generated tag collides with an existing one, auto-increment SEQ.");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

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

            int statusDetected = 0, revSet = 0;
            var (tagIndex, sequenceCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();
            int populated = 0;

            StingLog.Info($"Batch Tag: starting — {totalTaggable} taggable, {alreadyTagged} tagged, mode={collisionMode}");
            using var _perfOp = PerformanceTracker.Track("BatchTag");

            bool cancelled = false;

            // ENH-001: Show progress dialog with cancel support
            var progress = StingProgressDialog.Show("Batch Tag", totalTaggable);

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in sorted)
                {
                    // ENH-001: Check for user cancellation via progress dialog
                    if (progress.IsCancelled)
                    {
                        StingLog.Info($"Batch Tag: cancelled by user at {processed}/{totalTaggable}");
                        cancelled = true;
                        break;
                    }

                    try
                    {
                        // Full 9-token auto-population via shared helper
                        bool overwriteMode = (collisionMode == TagCollisionMode.Overwrite);
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx,
                            overwrite: overwriteMode);
                        populated += popResult.TokensSet;
                        if (popResult.StatusDetected) statusDetected++;
                        if (popResult.RevSet) revSet++;

                        bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                        TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                            skipComplete: skipComplete,
                            existingTags: tagIndex,
                            collisionMode: collisionMode,
                            stats: stats);

                        // Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                        string catName = ParameterHelpers.GetCategoryName(el);
                        string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                        TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: overwriteMode);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"BatchTag: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }

                    processed++;
                    progress.Increment($"Tagged {stats.TotalTagged}, collisions {stats.TotalCollisions}");

                    if (processed % 500 == 0)
                        StingLog.Info($"Batch Tag progress: {processed}/{totalTaggable} " +
                            $"({stats.TotalTagged} tagged, {stats.TotalCollisions} collisions)");
                }

                progress.Close();

                if (cancelled)
                {
                    tx.RollBack();
                    TaskDialog.Show("Batch Tag", $"Cancelled by user.\n{processed} of {totalTaggable} elements processed.\nAll changes rolled back.");
                    return Result.Cancelled;
                }

                tx.Commit();
            }

            sw.Stop();

            // Step 4: Rich reporting
            var report = new StringBuilder();
            report.AppendLine("Batch Tagging Complete");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Mode:         {collisionMode}");
            report.AppendLine($"  Tokens:       {populated} auto-populated");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS:       {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV:          {revSet} (revision '{popCtx.ProjectRev}')");
            report.AppendLine($"  Duration:     {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            StingLog.Info($"Batch Tag: tagged={stats.TotalTagged}, skipped={stats.TotalSkipped}, " +
                $"collisions={stats.TotalCollisions}, populated={populated}, " +
                $"statusDetect={statusDetected}, revSet={revSet}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

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

            return elements.OrderBy(e =>
                {
                    ElementId lvlId = e.LevelId;
                    if (lvlId != null && lvlId != ElementId.InvalidElementId &&
                        levelElevation.TryGetValue(lvlId, out double elev))
                        return elev;
                    return double.MaxValue;
                })
                .ThenBy(e =>
                {
                    string cat = ParameterHelpers.GetCategoryName(e);
                    return TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                })
                .ThenBy(e =>
                {
                    // SYS sort key: groups elements by ACTUAL system within discipline
                    // Uses MEP-aware detection so pipes group by DCW/HWS/SAN/GAS
                    string cat = ParameterHelpers.GetCategoryName(e);
                    string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                    string sys = TagConfig.GetMepSystemAwareSysCode(e, cat);
                    return !string.IsNullOrEmpty(sys) ? sys : TagConfig.GetDiscDefaultSysCode(disc);
                })
                .ThenBy(e => ParameterHelpers.GetCategoryName(e))
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
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Collect all tagged elements
            var tagged = new List<(Element el, string currentTag)>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;

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
            for (int i = 0; i < sampleCount; i++)
            {
                var (el, currentTag) = tagged[i];
                string[] tokens = ParamRegistry.ReadTokenValues(el);
                string rebuilt = string.Join(ParamRegistry.Separator, tokens);
                bool changed = !string.Equals(currentTag, rebuilt, StringComparison.Ordinal);
                if (changed)
                {
                    preview.AppendLine($"    {currentTag}");
                    preview.AppendLine($"  → {rebuilt}");
                    wouldChange++;
                }
                else
                {
                    preview.AppendLine($"    {currentTag} (unchanged)");
                }
            }

            // Count total that would change
            foreach (var (el, currentTag) in tagged)
            {
                string[] tokens = ParamRegistry.ReadTokenValues(el);
                string rebuilt = string.Join(ParamRegistry.Separator, tokens);
                if (!string.Equals(currentTag, rebuilt, StringComparison.Ordinal))
                    wouldChange++;
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
                        TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                            skipComplete: false,
                            existingTags: tagIndex,
                            collisionMode: TagCollisionMode.Overwrite,
                            stats: stats);
                        migrated++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Migration failed for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

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
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int scanned = 0, stale = 0, updated = 0;
            var staleSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["LVL"] = 0, ["LOC"] = 0, ["ZONE"] = 0
            };

            var staleElements = new List<(Element el, string token, string stored, string current)>();

            // Phase 1: Scan for stale tokens
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;

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

            TaskDialog.Show("Tag Changed",
                $"Delta update complete.\n\n  Stale tokens: {stale}\n  Elements updated: {updated}\n  Tags rebuilt: {processedElements.Count}");
            StingLog.Info($"Delta tagging: {stale} stale tokens, {updated} elements updated");
            return Result.Succeeded;
        }
    }
}
