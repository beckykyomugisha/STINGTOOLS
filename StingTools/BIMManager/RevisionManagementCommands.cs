using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Intelligent revision management engine with tag integration, auto-tracking,
    /// ISO 19650 compliance, and formula-driven revision automation.
    /// </summary>
    internal static class RevisionEngine
    {
        // --- Revision naming format: REV-{ProjectCode}-{SeqNum}-{Date}-{Description} ---
        internal static string BuildRevisionName(Document doc, int seq, string description)
        {
            string projCode = doc.ProjectInformation?.Number ?? doc.Title ?? "PROJ";
            string date = DateTime.Now.ToString("yyyyMMdd");
            string descShort;
            if (description.Length > 20)
            {
                descShort = description.Substring(0, 20).Trim();
                StingLog.Info($"RevisionEngine: Description truncated from {description.Length} chars to 20 for revision name: \"{description}\" → \"{descShort}\"");
            }
            else
            {
                descShort = description;
            }
            descShort = System.Text.RegularExpressions.Regex.Replace(descShort, @"[^A-Za-z0-9_ ]", "")
                .Replace(' ', '_');
            return $"REV-{projCode}-{seq:D3}-{date}-{descShort}";
        }

        /// <summary>Get next revision sequence number from existing revisions.</summary>
        internal static int GetNextRevisionSeq(Document doc)
        {
            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .ToList();
            if (revisions.Count == 0) return 1;
            int max = 0;
            foreach (var rev in revisions)
            {
                // Try to parse seq from description format REV-XXX-NNN-...
                string desc = rev.Description ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(desc, @"REV-[A-Z0-9]+-(\d{3})-");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int seq))
                    max = Math.Max(max, seq);
            }
            return max + 1;
        }

        /// <summary>Get the revision data directory.</summary>
        internal static string GetRevisionDir(Document doc)
        {
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string revDir = Path.Combine(bimDir, "Revisions");
            if (!Directory.Exists(revDir)) Directory.CreateDirectory(revDir);
            return revDir;
        }

        /// <summary>
        /// Take a snapshot of all tag values for tracked elements.
        /// Returns a dictionary of ElementId → tag token dictionary.
        /// </summary>
        /// <summary>
        /// Full set of parameters tracked in snapshots — 8 source tokens + TAG1-TAG6 +
        /// TAG7 + TAG7A-TAG7F + STATUS + REV + REV_COD. Uses ParamRegistry constants
        /// throughout (GAP-001/012 fix).
        /// </summary>
        private static string[] GetSnapshotParams()
        {
            var parms = new List<string>
            {
                // 8 source tokens
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ,
                // Universal tag containers
                ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
                ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
                // TAG7 narrative + sub-sections
                ParamRegistry.TAG7, ParamRegistry.TAG7A, ParamRegistry.TAG7B,
                ParamRegistry.TAG7C, ParamRegistry.TAG7D, ParamRegistry.TAG7E,
                ParamRegistry.TAG7F,
                // Status and revision
                ParamRegistry.STATUS, ParamRegistry.REV,
                // AG-09 FIX: Context data for change classification (was missing).
                // Enables revision reports to distinguish "element moved to new level"
                // vs "token manually changed".
                "ASS_GRID_REF_TXT",        // spatial context
                "ASS_TAG_PREV_TXT",         // audit trail
                "ASS_TAG_MODIFIED_DT",      // change timestamp
            };
            // Add discipline-specific containers if available
            try
            {
                foreach (var ct in ParamRegistry.GetContainerTuples())
                    if (!parms.Contains(ct.param))
                        parms.Add(ct.param);
            }
            catch (Exception ex) { StingLog.Warn($"RevisionEngine: GetContainerTuples failed, discipline containers may not be tracked: {ex.Message}"); }
            return parms.ToArray();
        }

        internal static Dictionary<long, Dictionary<string, string>> TakeTagSnapshot(Document doc)
        {
            var snapshot = new Dictionary<long, Dictionary<string, string>>();
            string[] tokenParams = GetSnapshotParams();

            // Phase 74: Single multi-category collector instead of 22+ per-category scans
            // (5-10x faster on 50K healthcare models: ~2s vs ~15s)
            var categories = SharedParamGuids.AllCategoryEnums;
            var catList = new List<BuiltInCategory>(categories);
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(catList));
            foreach (var el in collector)
            {
                try
                {
                        var tokens = new Dictionary<string, string>();
                        foreach (string param in tokenParams)
                            tokens[param] = ParameterHelpers.GetString(el, param);
                        // AG-09 FIX: Capture native Revit context for change classification
                        try
                        {
                            var phaseParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                            if (phaseParam != null && phaseParam.AsElementId() != ElementId.InvalidElementId)
                                tokens["_PHASE"] = doc.GetElement(phaseParam.AsElementId())?.Name ?? "";
                            var levelParam = el.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)
                                ?? el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                            if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
                                tokens["_LEVEL"] = doc.GetElement(levelParam.AsElementId())?.Name ?? "";
                            tokens["_CATEGORY"] = el.Category?.Name ?? "";
                            // Workset context for worksharing change tracking
                            try
                            {
                                if (doc.IsWorkshared && el.WorksetId != null && el.WorksetId != WorksetId.InvalidWorksetId)
                                    tokens["_WORKSET"] = doc.GetWorksetTable().GetWorkset(el.WorksetId)?.Name ?? "";
                            }
                            catch (Exception wsEx) { Core.StingLog.Warn($"Snapshot workset capture: {wsEx.Message}"); }
                            // MEP system context for system-level change classification
                            tokens["_SYSTEM"] = ParameterHelpers.GetString(el, "ASS_SYSTEM_TYPE_TXT");
                        }
                        catch (Exception ex) { Core.StingLog.Warn($"Snapshot context capture: {ex.Message}"); }
                        snapshot[el.Id.Value] = tokens;
                }
                catch (Exception catEx) { Core.StingLog.Warn($"Snapshot element error: {catEx.Message}"); }
            }
            return snapshot;
        }

        /// <summary>
        /// Get the current project revision code from the latest Revit Revision object.
        /// Falls back to PhaseAutoDetect.DetectProjectRevision().
        /// </summary>
        internal static string GetCurrentProjectRevision(Document doc)
        {
            try
            {
                var latest = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefault();
                if (latest != null)
                {
                    string num = "";
                    try { num = latest.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(num)) return num;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Current project revision lookup failed: {ex.Message}"); }
            // Fallback to PhaseAutoDetect
            return PhaseAutoDetect.DetectProjectRevision(doc);
        }

        /// <summary>
        /// Compare two snapshots and return changed elements with details.
        /// </summary>
        internal static List<RevisionChange> CompareSnapshots(
            Dictionary<long, Dictionary<string, string>> before,
            Dictionary<long, Dictionary<string, string>> after)
        {
            var changes = new List<RevisionChange>();

            // Check modified and deleted
            foreach (var kvp in before)
            {
                if (!after.TryGetValue(kvp.Key, out var afterTokens))
                {
                    changes.Add(new RevisionChange
                    {
                        ElementId = kvp.Key,
                        ChangeType = "Deleted",
                        ChangedParams = new List<ParamChange>()
                    });
                    continue;
                }
                var paramChanges = new List<ParamChange>();
                foreach (var token in kvp.Value)
                {
                    string newVal = afterTokens.TryGetValue(token.Key, out var atVal) ? atVal : "";
                    if (token.Value != newVal)
                    {
                        paramChanges.Add(new ParamChange
                        {
                            ParamName = token.Key,
                            OldValue = token.Value,
                            NewValue = newVal
                        });
                    }
                }
                if (paramChanges.Count > 0)
                {
                    changes.Add(new RevisionChange
                    {
                        ElementId = kvp.Key,
                        ChangeType = "Modified",
                        ChangedParams = paramChanges
                    });
                }
            }

            // Check added
            foreach (var kvp in after)
            {
                if (!before.ContainsKey(kvp.Key))
                {
                    changes.Add(new RevisionChange
                    {
                        ElementId = kvp.Key,
                        ChangeType = "Added",
                        ChangedParams = new List<ParamChange>()
                    });
                }
            }
            return changes;
        }

        /// <summary>Save snapshot to JSON file for later comparison.</summary>
        internal static void SaveSnapshot(Document doc, Dictionary<long, Dictionary<string, string>> snapshot,
            string label)
        {
            string dir = GetRevisionDir(doc);
            string fileName = $"snapshot_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var jObj = new JObject();
            jObj["label"] = label;
            jObj["timestamp"] = DateTime.Now.ToString("o");
            jObj["element_count"] = snapshot.Count;
            var elements = new JObject();
            foreach (var kvp in snapshot)
            {
                var tokens = new JObject();
                foreach (var t in kvp.Value)
                    tokens[t.Key] = t.Value ?? "";
                elements[kvp.Key.ToString()] = tokens;
            }
            jObj["elements"] = elements;
            // Phase 85: Atomic write with tmp + File.Replace to prevent corruption on crash
            string targetPath = Path.Combine(dir, fileName);
            string tmpPath = targetPath + ".tmp";
            File.WriteAllText(tmpPath, jObj.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(targetPath))
                File.Replace(tmpPath, targetPath, targetPath + ".bak");
            else
                File.Move(tmpPath, targetPath);
            StingLog.Info($"RevisionEngine: Snapshot '{label}' saved ({snapshot.Count} elements)");
        }

        /// <summary>Load the most recent snapshot from disk.</summary>
        internal static Dictionary<long, Dictionary<string, string>> LoadLatestSnapshot(Document doc)
        {
            string dir = GetRevisionDir(doc);
            var files = Directory.GetFiles(dir, "snapshot_*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
            if (files.Count == 0) return null;
            return LoadSnapshotFile(files[0]);
        }

        internal static Dictionary<long, Dictionary<string, string>> LoadSnapshotFile(string path)
        {
            var result = new Dictionary<long, Dictionary<string, string>>();
            try
            {
                var jObj = JObject.Parse(File.ReadAllText(path));
                var elements = jObj["elements"] as JObject;
                if (elements == null) return result;
                foreach (var prop in elements.Properties())
                {
                    if (!long.TryParse(prop.Name, out long id)) continue;
                    var tokens = new Dictionary<string, string>();
                    foreach (var t in (JObject)prop.Value)
                        tokens[t.Key] = t.Value?.ToString() ?? "";
                    result[id] = tokens;
                }
            }
            catch (Exception ex) { StingLog.Warn($"RevisionEngine: Failed to load snapshot: {ex.Message}"); }
            return result;
        }

        /// <summary>
        /// Generate a revision narrative describing changes in natural language.
        /// </summary>
        internal static string BuildChangeNarrative(List<RevisionChange> changes)
        {
            int added = changes.Count(c => c.ChangeType == "Added");
            int modified = changes.Count(c => c.ChangeType == "Modified");
            int deleted = changes.Count(c => c.ChangeType == "Deleted");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Revision Summary: {changes.Count} element(s) affected");
            sb.AppendLine($"  Added: {added} | Modified: {modified} | Deleted: {deleted}");

            // Group modified changes by parameter
            var modChanges = changes.Where(c => c.ChangeType == "Modified").ToList();
            if (modChanges.Count > 0)
            {
                var paramGroups = modChanges.SelectMany(c => c.ChangedParams)
                    .GroupBy(p => p.ParamName)
                    .OrderByDescending(g => g.Count());
                sb.AppendLine("\nMost Changed Parameters:");
                foreach (var g in paramGroups.Take(5))
                    sb.AppendLine($"  {g.Key}: {g.Count()} changes");
            }
            return sb.ToString();
        }

        /// <summary>
        /// ISO 19650 revision numbering validation.
        /// Format: P01, P02... (preliminary), C01, C02... (construction), A, B, C... (as-built)
        /// </summary>
        internal static string ValidateRevisionNumber(string revNum)
        {
            if (string.IsNullOrWhiteSpace(revNum)) return "Revision number is empty";
            revNum = revNum.Trim().ToUpper();
            // P## pattern (preliminary)
            if (System.Text.RegularExpressions.Regex.IsMatch(revNum, @"^P\d{2}$")) return null;
            // C## pattern (construction)
            if (System.Text.RegularExpressions.Regex.IsMatch(revNum, @"^C\d{2}$")) return null;
            // Single letter A-Z (as-built)
            if (System.Text.RegularExpressions.Regex.IsMatch(revNum, @"^[A-Z]$")) return null;
            // Numeric (legacy)
            if (System.Text.RegularExpressions.Regex.IsMatch(revNum, @"^\d+$")) return null;
            return $"Non-standard revision number '{revNum}'. Expected P01-P99, C01-C99, or A-Z.";
        }

        /// <summary>
        /// Auto-determine revision significance from changes.
        /// Returns: Minor, Standard, Major based on change scope.
        /// </summary>
        internal static string ClassifyRevisionSignificance(List<RevisionChange> changes)
        {
            if (changes.Count == 0) return "Minor";
            int structural = changes.Count(c => c.ChangedParams.Any(p =>
                p.ParamName == ParamRegistry.SYS || p.ParamName == ParamRegistry.FUNC));
            int identity = changes.Count(c => c.ChangedParams.Any(p =>
                p.ParamName == ParamRegistry.DISC || p.ParamName == ParamRegistry.PROD));
            if (identity > 0 || changes.Count(c => c.ChangeType == "Deleted") > 5)
                return "Major";
            if (structural > 0 || changes.Count > 20)
                return "Standard";
            return "Minor";
        }

        /// <summary>
        /// Stamp affected elements with revision code and update their STATUS.
        /// Called after creating a revision to propagate REV into tag data.
        /// </summary>
        internal static int StampAffectedElements(Document doc, List<RevisionChange> changes, string revCode)
        {
            int stamped = 0;
            foreach (var change in changes)
            {
                var el = doc.GetElement(new ElementId(change.ElementId));
                if (el == null) continue;
                try
                {
                    string current = ParameterHelpers.GetString(el, ParamRegistry.REV);
                    if (current != revCode)
                    {
                        ParameterHelpers.SetString(el, ParamRegistry.REV, revCode, true);
                        stamped++;
                    }
                    // Also update STATUS based on change type
                    if (change.ChangeType == "Added")
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, "NEW");
                    else if (change.ChangeType == "Modified")
                    {
                        string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                        if (string.IsNullOrEmpty(status))
                            ParameterHelpers.SetString(el, ParamRegistry.STATUS, "NEW", false);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"StampAffectedElements: Cannot write element {change.ElementId}: {ex.Message}");
                }
            }
            return stamped;
        }

        /// <summary>HR-04: Size-based + count-based snapshot pruning to prevent disk bloat.</summary>
        private static long MaxSnapshotBytes
        {
            get
            {
                try
                {
                    string envMb = Environment.GetEnvironmentVariable("STING_MAX_SNAPSHOT_MB");
                    if (!string.IsNullOrEmpty(envMb) && long.TryParse(envMb, out long val) && val > 0)
                        return val * 1024 * 1024;
                }
                catch (Exception ex) { StingLog.Warn($"PruneSnapshots config: {ex.Message}"); }
                return 100L * 1024 * 1024; // default 100 MB
            }
        }

        internal static int PruneSnapshots(Document doc, int keepCount = 20)
        {
            string dir = GetRevisionDir(doc);
            var files = Directory.GetFiles(dir, "snapshot_*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
            int deleted = 0;

            // Count-based pruning
            while (files.Count > keepCount)
            {
                try { File.Delete(files[files.Count - 1]); deleted++; }
                catch (Exception ex) { StingLog.Warn($"PruneSnapshots count: {ex.Message}"); }
                files.RemoveAt(files.Count - 1);
            }

            // HR-04: Size-based pruning
            long totalBytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0L; } });
            long maxBytes = MaxSnapshotBytes;
            while (totalBytes > maxBytes && files.Count > 1)
            {
                string oldest = files[files.Count - 1];
                try
                {
                    long sz = new FileInfo(oldest).Length;
                    File.Delete(oldest);
                    totalBytes -= sz;
                    deleted++;
                }
                catch (Exception ex) { StingLog.Warn($"PruneSnapshots size: {ex.Message}"); }
                files.RemoveAt(files.Count - 1);
            }

            if (deleted > 0)
                StingLog.Info($"RevisionEngine: Pruned {deleted} snapshot(s). Folder: {totalBytes / 1024 / 1024:F1} MB");
            return deleted;
        }

        /// <summary>
        /// Build a per-discipline change summary from a list of changes.
        /// Groups changes by discipline code for targeted revision reporting.
        /// </summary>
        internal static Dictionary<string, int> GetChangeSummaryByDiscipline(Document doc, List<RevisionChange> changes)
        {
            var result = new Dictionary<string, int>();
            foreach (var change in changes)
            {
                var el = doc.GetElement(new ElementId(change.ElementId));
                if (el == null) continue;
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(disc))
                    disc = TagConfig.DiscMap.TryGetValue(ParameterHelpers.GetCategoryName(el), out string d) ? d : "XX";
                result.TryGetValue(disc, out int rCnt);
                result[disc] = rCnt + 1;
            }
            return result;
        }

        internal class RevisionChange
        {
            public long ElementId { get; set; }
            public string ChangeType { get; set; }
            public List<ParamChange> ChangedParams { get; set; }
        }

        internal class ParamChange
        {
            public string ParamName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }

            /// <summary>
            /// GAP-BIM-004: Categorize change type for granular revision reporting.
            /// TOKEN_CHANGE = source token modified (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ)
            /// CONTAINER_REGEN = discipline container regenerated (HVC_EQP_TAG etc.)
            /// NARRATIVE_CHANGE = TAG7 sub-section changed (TAG7A-F)
            /// STATUS_CHANGE = STATUS or REV changed
            /// TAG_REFORMAT = TAG1-TAG6 changed (may be reformat, not content change)
            /// </summary>
            public string ChangeCategory
            {
                get
                {
                    if (string.IsNullOrEmpty(ParamName)) return "UNKNOWN";
                    if (ParamName == ParamRegistry.STATUS || ParamName == ParamRegistry.REV)
                        return "STATUS_CHANGE";
                    if (ParamName == ParamRegistry.DISC || ParamName == ParamRegistry.LOC ||
                        ParamName == ParamRegistry.ZONE || ParamName == ParamRegistry.LVL ||
                        ParamName == ParamRegistry.SYS || ParamName == ParamRegistry.FUNC ||
                        ParamName == ParamRegistry.PROD || ParamName == ParamRegistry.SEQ)
                        return "TOKEN_CHANGE";
                    if (ParamName == ParamRegistry.TAG7A || ParamName == ParamRegistry.TAG7B ||
                        ParamName == ParamRegistry.TAG7C || ParamName == ParamRegistry.TAG7D ||
                        ParamName == ParamRegistry.TAG7E || ParamName == ParamRegistry.TAG7F ||
                        ParamName == ParamRegistry.TAG7)
                        return "NARRATIVE_CHANGE";
                    if (ParamName == ParamRegistry.TAG1 || ParamName == ParamRegistry.TAG2 ||
                        ParamName == ParamRegistry.TAG3 || ParamName == ParamRegistry.TAG4 ||
                        ParamName == ParamRegistry.TAG5 || ParamName == ParamRegistry.TAG6)
                        return "TAG_REFORMAT";
                    return "CONTAINER_REGEN"; // Discipline-specific containers
                }
            }
        }
    }

    /// <summary>
    /// Create a new project revision with ISO 19650 compliant naming,
    /// auto-generated description, and tag snapshot for change tracking.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateRevisionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                int nextSeq = RevisionEngine.GetNextRevisionSeq(doc);

                // Prompt for description
                var td = new TaskDialog("StingTools Create Revision")
                {
                    MainInstruction = $"Create Revision #{nextSeq}",
                    MainContent = "Enter revision description.\n\n" +
                        "The revision will be created with ISO 19650 naming and a tag snapshot " +
                        "will be taken for change tracking.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preliminary Issue (P-series)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Construction Issue (C-series)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "As-Built Issue (Letter series)");

                var result = td.Show();
                if (result == TaskDialogResult.Cancel) return Result.Cancelled;

                string prefix;
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: prefix = $"P{nextSeq:D2}"; break;
                    case TaskDialogResult.CommandLink2: prefix = $"C{nextSeq:D2}"; break;
                    case TaskDialogResult.CommandLink3:
                        prefix = nextSeq <= 26 ? ((char)('A' + nextSeq - 1)).ToString() : $"Z{nextSeq - 26}";
                        break;
                    default: prefix = $"P{nextSeq:D2}"; break;
                }

                string description = RevisionEngine.BuildRevisionName(doc, nextSeq,
                    result == TaskDialogResult.CommandLink1 ? "Preliminary" :
                    result == TaskDialogResult.CommandLink2 ? "Construction" : "As-Built");

                // WF-03: Pre-revision compliance gate — warn if tag compliance is below threshold
                try
                {
                    var preRevScan = ComplianceScan.Scan(doc);
                    if (preRevScan.CompliancePercent < 80)
                    {
                        var gateDlg = new TaskDialog("STING Pre-Revision Compliance Gate");
                        gateDlg.MainInstruction = $"Tag compliance is {preRevScan.CompliancePercent:F0}% (below 80% threshold)";
                        gateDlg.MainContent =
                            $"Total elements: {preRevScan.TotalElements}\n" +
                            $"Tagged: {preRevScan.TaggedComplete} | Untagged: {preRevScan.Untagged}\n" +
                            $"Stale: {preRevScan.StaleCount}\n\n" +
                            "Creating a revision with low compliance may result in incomplete COBie data.\n" +
                            "Recommended: tag to ≥80% before creating revision.";
                        gateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Create revision anyway");
                        gateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel — tag first");
                        if (gateDlg.Show() != TaskDialogResult.CommandLink1)
                            return Result.Cancelled;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Pre-revision compliance check: {ex.Message}"); }

                // Take pre-revision snapshot
                var snapshot = RevisionEngine.TakeTagSnapshot(doc);
                RevisionEngine.SaveSnapshot(doc, snapshot, $"pre_rev_{prefix}");

                using (var tx = new Transaction(doc, "STING Create Revision"))
                {
                    tx.Start();

                    var rev = Revision.Create(doc);
                    rev.Description = description;
                    rev.RevisionDate = DateTime.Now.ToString("yyyy-MM-dd");

                    rev.Visibility = RevisionVisibility.CloudAndTagVisible;

                    tx.Commit();

                    TaskDialog.Show("StingTools Revision",
                        $"Revision created successfully.\n\n" +
                        $"Number: {prefix}\n" +
                        $"Description: {description}\n" +
                        $"Date: {rev.RevisionDate}\n\n" +
                        $"Tag snapshot saved ({snapshot.Count} elements tracked).\n" +
                        "Use 'Revision Compare' after changes to see what was modified.");
                }
                // GAP-FIX: Auto-save warning baseline on revision creation
                if (TagConfig.AutoSaveBaselineOnRevision)
                {
                    try
                    {
                        WarningsEngine.SaveExtendedBaseline(doc);
                        StingLog.Info($"Auto-saved warning baseline on revision creation ({prefix})");
                    }
                    catch (Exception wbEx) { StingLog.Warn($"Auto-save baseline on revision: {wbEx.Message}"); }
                }

                // GAP-R9: Auto-propagate new REV to all tagged elements
                // so tags reflect the current revision immediately
                try
                {
                    int revUpdated = 0;
                    using (var revTx = new Transaction(doc, "STING Propagate REV"))
                    {
                        revTx.Start();
                        var catEnums = SharedParamGuids.AllCategoryEnums;
                        var allTagged = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType();
                        if (catEnums != null && catEnums.Length > 0)
                            allTagged.WherePasses(new ElementMulticategoryFilter(catEnums));
                        foreach (var el in allTagged)
                        {
                            string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(tag1)) continue;
                            if (ParameterHelpers.SetString(el, "ASS_REV_TXT", prefix, overwrite: true))
                                revUpdated++;
                        }
                        revTx.Commit();
                    }
                    StingLog.Info($"GAP-R9: Propagated REV '{prefix}' to {revUpdated} tagged elements");
                }
                catch (Exception revEx) { StingLog.Warn($"REV propagation: {revEx.Message}"); }

                // Invalidate compliance cache ONCE after all rev-related transactions
                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();

                // NTF-03: Notify team that revision is open
                try
                {
                    NotificationDeliveryEngine.LoadConfig(doc);
                    var revScan = ComplianceScan.Scan(doc);
                    string ntfBody = $"Revision {prefix} created by {Environment.UserName}.\n" +
                        $"Description: {description}\n" +
                        $"Tag compliance: {revScan.CompliancePercent:F0}%\n" +
                        $"Stale elements: {revScan.StaleCount}\n" +
                        $"Snapshot: {snapshot.Count} elements tracked";
                    NotificationDeliveryEngine.SendNotification(doc,
                        $"Revision Created: {prefix}", ntfBody, "HIGH", prefix);
                }
                catch (Exception ex) { StingLog.Warn($"Revision notification: {ex.Message}"); }

                StingLog.Info($"Revision created: {prefix} — {description}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create revision failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Comprehensive revision dashboard showing all revisions, their status,
    /// associated clouds/sheets, and change statistics.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionDashboardCommand : IExternalCommand
    {
        // Row model for the DataGrid
        private class RevisionRow
        {
            public int Seq { get; set; }
            public string Number { get; set; }
            public string Date { get; set; }
            public int Clouds { get; set; }
            public int Sheets { get; set; }
            public string Visibility { get; set; }
            public string Description { get; set; }
            public long RevId { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var uidoc = _ctx.UIDoc;

                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();

                if (revisions.Count == 0)
                {
                    TaskDialog.Show("STING Revision Dashboard", "No revisions found in project.");
                    return Result.Succeeded;
                }

                // Count clouds per revision
                var clouds = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .ToList();
                var cloudsByRev = clouds.GroupBy(c => c.RevisionId.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Count sheets with each revision
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToList();

                // Build row data
                var rows = new List<RevisionRow>();
                foreach (var rev in revisions)
                {
                    int numClouds = cloudsByRev.TryGetValue(rev.Id.Value, out int cbrVal) ? cbrVal : 0;
                    int numSheets = sheets.Count(s =>
                    {
                        try { return s.GetAdditionalRevisionIds().Contains(rev.Id); }
                        catch (Exception revEx) { Core.StingLog.Warn($"Revision sequence check: {revEx.Message}"); return false; }
                    });
                    string vis = rev.Visibility == RevisionVisibility.Hidden ? "Hidden" :
                                 rev.Visibility == RevisionVisibility.CloudAndTagVisible ? "Clouds+Tags" :
                                 "Tags Only";
                    string numStr = "";
                    try { numStr = rev.RevisionNumber; } catch (Exception rnEx) { numStr = "—"; Core.StingLog.Warn($"Revision number access: {rnEx.Message}"); }

                    rows.Add(new RevisionRow
                    {
                        Seq = rev.SequenceNumber,
                        Number = numStr,
                        Date = rev.RevisionDate ?? "—",
                        Clouds = numClouds,
                        Sheets = numSheets,
                        Visibility = vis,
                        Description = rev.Description ?? "(no description)",
                        RevId = rev.Id.Value
                    });
                }

                // Build WPF DataGrid dialog
                string revDir = RevisionEngine.GetRevisionDir(doc);
                int snapshotCount = 0;
                try { snapshotCount = Directory.GetFiles(revDir, "snapshot_*.json").Length; }
                catch (Exception ex) { StingLog.Warn($"RevisionDashboard snapshot count: {ex.Message}"); }

                int active = revisions.Count(r => r.Visibility != RevisionVisibility.Hidden);
                string subtitle = $"{revisions.Count} revisions | {clouds.Count} clouds | {active} active | {snapshotCount} snapshots";

                var dlg = new UI.StingDataGridDialog("STING Revision Dashboard", subtitle, 1020, 580);
                dlg.AddTextColumn("#", "Seq", 35);
                dlg.AddTextColumn("Number", "Number", 70);
                dlg.AddTextColumn("Date", "Date", 100);
                dlg.AddTextColumn("Clouds", "Clouds", 60);
                dlg.AddTextColumn("Sheets", "Sheets", 60);
                dlg.AddTextColumn("Visibility", "Visibility", 110);
                dlg.AddTextColumn("Description", "Description");

                dlg.AddFilter("Visibility", new[] { "All", "Clouds+Tags", "Tags Only", "Hidden" }, vis =>
                {
                    if (vis == "All") dlg.RefreshItems(rows);
                    else dlg.RefreshItems(rows.Where(r => r.Visibility == vis).ToList());
                });

                dlg.AddActionButton("Select Clouds", "SelectClouds");
                dlg.AddActionButton("Take Snapshot", "Snapshot");
                dlg.AddActionButton("Export CSV", "Export");
                dlg.AddActionButton("Close", "Cancel");

                dlg.ActionClicked += action =>
                {
                    if (action == "SelectClouds")
                    {
                        var selected = dlg.SelectedItems.Cast<RevisionRow>().ToList();
                        if (selected.Count > 0)
                        {
                            var revIds = new HashSet<long>(selected.Select(r => r.RevId));
                            var cloudIds = clouds.Where(c => revIds.Contains(c.RevisionId.Value))
                                .Select(c => c.Id).ToList();
                            if (cloudIds.Count > 0)
                            {
                                try { uidoc.Selection.SetElementIds(cloudIds); }
                                catch (Exception ex) { StingLog.Warn($"Select clouds: {ex.Message}"); }
                                dlg.SetStatus($"Selected {cloudIds.Count} revision clouds");
                            }
                            else dlg.SetStatus("No clouds for selected revision(s)");
                        }
                    }
                    else if (action == "Snapshot")
                    {
                        dlg.SetStatus("Use 'Track Element Revisions' command to take a tag snapshot");
                    }
                    else if (action == "Export")
                    {
                        try
                        {
                            string csvPath = OutputLocationHelper.GetOutputPath(doc, "STING_Revisions.csv");
                            var csvLines = new List<string> { "Seq,Number,Date,Clouds,Sheets,Visibility,Description" };
                            foreach (var r in rows)
                                csvLines.Add($"{r.Seq},{r.Number},{r.Date},{r.Clouds},{r.Sheets},{r.Visibility},\"{r.Description}\"");
                            File.WriteAllLines(csvPath, csvLines);
                            dlg.SetStatus($"Exported to {csvPath}");
                        }
                        catch (Exception ex) { dlg.SetStatus($"Export failed: {ex.Message}"); }
                    }
                };

                dlg.SetItems(rows);
                dlg.SetStatus($"{revisions.Count} revisions | {clouds.Count} clouds | {active} active | {snapshotCount} snapshots");
                dlg.ShowDialog();

                StingLog.Info($"Revision dashboard: {revisions.Count} revisions, {clouds.Count} clouds");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision dashboard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Automatically create revision clouds around elements that have changed
    /// since the last tag snapshot. Detects tag modifications and places clouds.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoRevisionCloudCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var view = doc.ActiveView;

                // Load previous snapshot
                var prevSnapshot = RevisionEngine.LoadLatestSnapshot(doc);
                if (prevSnapshot == null)
                {
                    TaskDialog.Show("StingTools Auto Revision Cloud",
                        "No previous tag snapshot found.\n\n" +
                        "Use 'Create Revision' first to take a baseline snapshot.");
                    return Result.Succeeded;
                }

                // Take current snapshot
                var currentSnapshot = RevisionEngine.TakeTagSnapshot(doc);
                var changes = RevisionEngine.CompareSnapshots(prevSnapshot, currentSnapshot);

                if (changes.Count == 0)
                {
                    TaskDialog.Show("StingTools Auto Revision Cloud",
                        "No tag changes detected since last snapshot.");
                    return Result.Succeeded;
                }

                // Get the latest revision to associate clouds with
                var latestRevision = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefault();

                if (latestRevision == null)
                {
                    TaskDialog.Show("StingTools Auto Revision Cloud",
                        "No revisions exist. Create a revision first.");
                    return Result.Succeeded;
                }

                int cloudsCreated = 0;
                int cloudsSkipped = 0;

                // Phase 85: Build set of already-clouded element IDs to prevent duplicates on repeated runs
                var alreadyClouded = new HashSet<long>();
                try
                {
                    foreach (var rc in new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(RevisionCloud))
                        .Cast<RevisionCloud>())
                    {
                        // RevisionCloud doesn't expose hosted elements directly; track by bounding box center
                    }
                    // Track existing clouds by their host element references
                    foreach (RevisionCloud rc in new FilteredElementCollector(doc)
                        .OfClass(typeof(RevisionCloud)))
                    {
                        if (rc.RevisionId == latestRevision.Id && rc.OwnerViewId == view.Id)
                        {
                            // Use bounding box center as proxy for the element that was clouded
                            var rcBb = rc.get_BoundingBox(view);
                            if (rcBb != null)
                            {
                                // Store a hash of the cloud's location to detect overlap
                                long locHash = (long)(rcBb.Min.X * 1000) ^ ((long)(rcBb.Min.Y * 1000) << 20);
                                alreadyClouded.Add(locHash);
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Cloud dedup scan: {ex.Message}"); }

                using (var tx = new Transaction(doc, "STING Auto Revision Clouds"))
                {
                    tx.Start();
                    foreach (var change in changes.Where(c => c.ChangeType == "Modified" || c.ChangeType == "Added"))
                    {
                        var elId = new ElementId(change.ElementId);
                        var el = doc.GetElement(elId);
                        if (el == null) { cloudsSkipped++; continue; }

                        // Get element bounding box in view
                        BoundingBoxXYZ bb = el.get_BoundingBox(view);
                        if (bb == null) { cloudsSkipped++; continue; }

                        // Phase 85: Skip if cloud already exists at this location
                        long elLocHash = (long)(bb.Min.X * 1000) ^ ((long)(bb.Min.Y * 1000) << 20);
                        if (alreadyClouded.Contains(elLocHash)) { cloudsSkipped++; continue; }

                        // Create cloud outline around element
                        double pad = 0.5; // 6 inches padding
                        var pts = new List<XYZ>
                        {
                            new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z),
                            new XYZ(bb.Max.X + pad, bb.Min.Y - pad, bb.Min.Z),
                            new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Min.Z),
                            new XYZ(bb.Min.X - pad, bb.Max.Y + pad, bb.Min.Z)
                        };

                        try
                        {
                            var curves = new List<Curve>();
                            for (int i = 0; i < pts.Count; i++)
                            {
                                int next = (i + 1) % pts.Count;
                                curves.Add(Line.CreateBound(pts[i], pts[next]));
                            }
                            RevisionCloud.Create(doc, view, latestRevision.Id, curves);
                            cloudsCreated++;
                        }
                        catch (Exception clEx) { cloudsSkipped++; Core.StingLog.Warn($"RevCloud creation: {clEx.Message}"); }
                    }
                    tx.Commit();
                }

                string narrative = RevisionEngine.BuildChangeNarrative(changes);
                TaskDialog.Show("StingTools Auto Revision Cloud",
                    $"Revision clouds created for changed elements.\n\n" +
                    $"Clouds created: {cloudsCreated}\n" +
                    $"Skipped (not visible): {cloudsSkipped}\n\n" +
                    narrative);

                StingLog.Info($"Auto revision clouds: {cloudsCreated} created, {cloudsSkipped} skipped");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Auto revision cloud failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Generate a revision schedule showing all revisions with cloud counts,
    /// sheet assignments, and compliance status in a ViewSchedule.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();

                // Export as CSV since ViewSchedule for revisions requires specialized handling
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Seq,Number,Date,Description,Issued,Visibility,CloudCount,NamingValid");

                var clouds = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .GroupBy(c => c.RevisionId.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var rev in revisions)
                {
                    int numClouds = clouds.TryGetValue(rev.Id.Value, out int clVal) ? clVal : 0;
                    string numStr = "";
                    try { numStr = rev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                    string namingValid = RevisionEngine.ValidateRevisionNumber(numStr) == null ? "Valid" : "Invalid";
                    string issued = rev.Issued ? "Yes" : "No";
                    string vis = rev.Visibility == RevisionVisibility.Hidden ? "Hidden" :
                                 rev.Visibility == RevisionVisibility.CloudAndTagVisible ? "Clouds+Tags" : "TagsOnly";

                    sb.AppendLine($"{rev.SequenceNumber},{numStr},{rev.RevisionDate ?? ""}," +
                        $"\"{rev.Description ?? ""}\",{issued},{vis},{numClouds},{namingValid}");
                }

                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_Revision_Schedule", ".csv");
                File.WriteAllText(path, sb.ToString());

                TaskDialog.Show("StingTools Revision Schedule",
                    $"Revision schedule exported.\n\n" +
                    $"Revisions: {revisions.Count}\n" +
                    $"File: {path}");
                StingLog.Info($"Revision schedule exported: {revisions.Count} revisions → {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision schedule failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Track which elements have been modified across revisions by comparing
    /// tag snapshots. Shows per-element revision history.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrackElementRevisionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var uidoc = _ctx.UIDoc;
                var doc = _ctx.Doc;
                string revDir = RevisionEngine.GetRevisionDir(doc);

                var snapshotFiles = Directory.GetFiles(revDir, "snapshot_*.json")
                    .OrderBy(f => File.GetLastWriteTime(f))
                    .ToList();

                if (snapshotFiles.Count < 2)
                {
                    TaskDialog.Show("StingTools Track Revisions",
                        "Need at least 2 snapshots for comparison.\n" +
                        $"Current snapshots: {snapshotFiles.Count}");
                    return Result.Succeeded;
                }

                // Compare consecutive snapshots to build element history
                var elementHistory = new Dictionary<long, List<string>>();
                for (int i = 1; i < snapshotFiles.Count; i++)
                {
                    var before = RevisionEngine.LoadSnapshotFile(snapshotFiles[i - 1]);
                    var after = RevisionEngine.LoadSnapshotFile(snapshotFiles[i]);
                    var changes = RevisionEngine.CompareSnapshots(before, after);
                    string label = Path.GetFileNameWithoutExtension(snapshotFiles[i]);

                    foreach (var change in changes)
                    {
                        if (!elementHistory.TryGetValue(change.ElementId, out var ehList))
                        {
                            ehList = new List<string>();
                            elementHistory[change.ElementId] = ehList;
                        }
                        string detail = change.ChangeType == "Modified"
                            ? $"{label}: {string.Join(", ", change.ChangedParams.Select(p => $"{p.ParamName}: {p.OldValue}→{p.NewValue}"))}"
                            : $"{label}: {change.ChangeType}";
                        ehList.Add(detail);
                    }
                }

                // Select elements with most changes
                var mostChanged = elementHistory
                    .OrderByDescending(e => e.Value.Count)
                    .Take(50)
                    .ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ELEMENT REVISION HISTORY — {elementHistory.Count} elements tracked\n");
                sb.AppendLine($"Top {Math.Min(50, mostChanged.Count)} most-changed elements:\n");

                foreach (var entry in mostChanged.Take(20))
                {
                    var el = doc.GetElement(new ElementId(entry.Key));
                    string name = el != null ? $"{ParameterHelpers.GetCategoryName(el)} [{el.Id.Value}]" : $"[{entry.Key}]";
                    sb.AppendLine($"  {name} — {entry.Value.Count} change(s)");
                    foreach (string h in entry.Value.Take(3))
                        sb.AppendLine($"    • {h}");
                    if (entry.Value.Count > 3)
                        sb.AppendLine($"    ... +{entry.Value.Count - 3} more");
                }

                // Select most-changed elements
                var ids = mostChanged.Select(e => new ElementId(e.Key))
                    .Where(id => doc.GetElement(id) != null)
                    .ToList();
                if (ids.Count > 0)
                    uidoc.Selection.SetElementIds(ids);

                TaskDialog.Show("StingTools Element Revision Tracking", sb.ToString());
                StingLog.Info($"Revision tracking: {elementHistory.Count} elements with changes");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Track element revisions failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Compare tag values between two snapshots, showing a detailed diff report.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionCompareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                string revDir = RevisionEngine.GetRevisionDir(doc);

                var snapshotFiles = Directory.GetFiles(revDir, "snapshot_*.json")
                    .OrderBy(f => File.GetLastWriteTime(f))
                    .ToList();

                if (snapshotFiles.Count < 2)
                {
                    TaskDialog.Show("StingTools Revision Compare",
                        "Need at least 2 snapshots for comparison.\n" +
                        "Use 'Create Revision' to save snapshots.");
                    return Result.Succeeded;
                }

                // Compare most recent two
                var before = RevisionEngine.LoadSnapshotFile(snapshotFiles[snapshotFiles.Count - 2]);
                var after = RevisionEngine.LoadSnapshotFile(snapshotFiles[snapshotFiles.Count - 1]);
                var changes = RevisionEngine.CompareSnapshots(before, after);
                string significance = RevisionEngine.ClassifyRevisionSignificance(changes);
                string narrative = RevisionEngine.BuildChangeNarrative(changes);

                // Export detailed diff to CSV
                var csvSb = new System.Text.StringBuilder();
                csvSb.AppendLine("ElementId,Category,ChangeType,Parameter,OldValue,NewValue");
                foreach (var change in changes)
                {
                    var el = doc.GetElement(new ElementId(change.ElementId));
                    string cat = el != null ? ParameterHelpers.GetCategoryName(el) : "Unknown";
                    if (change.ChangedParams.Count == 0)
                    {
                        csvSb.AppendLine($"{change.ElementId},{cat},{change.ChangeType},,,");
                    }
                    else
                    {
                        foreach (var p in change.ChangedParams)
                            csvSb.AppendLine($"{change.ElementId},{cat},{change.ChangeType}," +
                                $"{p.ParamName},\"{p.OldValue}\",\"{p.NewValue}\"");
                    }
                }

                string csvPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_Revision_Diff", ".csv");
                File.WriteAllText(csvPath, csvSb.ToString());

                TaskDialog.Show("StingTools Revision Compare",
                    $"Revision Comparison Report\n\n" +
                    $"Snapshots compared: {Path.GetFileName(snapshotFiles[snapshotFiles.Count - 2])}\n" +
                    $"              vs: {Path.GetFileName(snapshotFiles[snapshotFiles.Count - 1])}\n\n" +
                    $"Significance: {significance}\n\n" +
                    narrative +
                    $"\nDetailed diff: {csvPath}");

                StingLog.Info($"Revision compare: {changes.Count} changes ({significance})");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision compare failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Issue sheets for a specific revision — add revision to selected sheets
    /// and mark the revision as issued.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IssueSheetsForRevisionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .Where(r => !r.Issued)
                    .OrderByDescending(r => r.SequenceNumber)
                    .ToList();

                if (revisions.Count == 0)
                {
                    TaskDialog.Show("StingTools Issue Sheets", "No un-issued revisions found.");
                    return Result.Succeeded;
                }

                // Use the latest un-issued revision
                var targetRev = revisions[0];
                string revNum = "";
                try { revNum = targetRev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                // Find sheets with revision clouds for this revision
                var clouds = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .Where(c => c.RevisionId == targetRev.Id)
                    .ToList();

                var sheetsWithClouds = new HashSet<ElementId>();
                foreach (var cloud in clouds)
                {
                    if (cloud.OwnerViewId != ElementId.InvalidElementId)
                    {
                        // Find which sheet this view is on
                        foreach (var sheet in sheets)
                        {
                            var vpIds = sheet.GetAllViewports();
                            foreach (ElementId vpId in vpIds)
                            {
                                var vp = doc.GetElement(vpId) as Viewport;
                                if (vp != null && vp.ViewId == cloud.OwnerViewId)
                                    sheetsWithClouds.Add(sheet.Id);
                            }
                        }
                    }
                }

                int sheetsIssued = 0;
                using (var tx = new Transaction(doc, "STING Issue Sheets for Revision"))
                {
                    tx.Start();

                    // Add revision to sheets with clouds
                    foreach (ElementId sheetId in sheetsWithClouds)
                    {
                        var sheet = doc.GetElement(sheetId) as ViewSheet;
                        if (sheet == null) continue;
                        var existingRevs = sheet.GetAdditionalRevisionIds().ToList();
                        if (!existingRevs.Contains(targetRev.Id))
                        {
                            existingRevs.Add(targetRev.Id);
                            sheet.SetAdditionalRevisionIds(existingRevs);
                            sheetsIssued++;
                        }
                    }

                    // Mark revision as issued
                    targetRev.Issued = true;

                    tx.Commit();
                }

                TaskDialog.Show("StingTools Issue Sheets",
                    $"Revision {revNum} issued.\n\n" +
                    $"Sheets with revision clouds: {sheetsWithClouds.Count}\n" +
                    $"Sheets updated: {sheetsIssued}\n" +
                    $"Revision marked as Issued: Yes");

                StingLog.Info($"Revision {revNum} issued to {sheetsIssued} sheets");

                // IG-02: Auto-resolve matching issues when revision is issued
                int issuesResolved = 0;
                try
                {
                    string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
                    var issues = BIMManagerEngine.LoadJsonArray(issuesPath);
                    if (issues.Count > 0)
                    {
                        foreach (var issue in issues)
                        {
                            string status = issue["status"]?.ToString() ?? "";
                            if (status == "CLOSED" || status == "VOID" || status == "ACCEPTED") continue;

                            // Match issues whose target_revision or revision matches the issued revision
                            string issueRev = issue["target_revision"]?.ToString()
                                ?? issue["revision"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(issueRev) &&
                                string.Equals(issueRev, revNum, StringComparison.OrdinalIgnoreCase))
                            {
                                issue["status"] = "CLOSED";
                                issue["date_closed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                                issue["response"] = $"Auto-resolved: revision {revNum} issued to {sheetsIssued} sheets";
                                issue["resolved_in_revision"] = revNum;
                                issuesResolved++;
                            }
                        }
                        if (issuesResolved > 0)
                        {
                            BIMManagerEngine.SaveJsonFile(issuesPath, issues);
                            StingLog.Info($"IG-02: Auto-resolved {issuesResolved} issues for revision {revNum}");
                        }
                    }
                }
                catch (Exception issueEx)
                {
                    StingLog.Warn($"IG-02: Issue auto-resolve failed: {issueEx.Message}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Issue sheets failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Enforce ISO 19650 revision naming conventions across all revisions.
    /// Validates P##/C##/Letter format and offers auto-correction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionNamingEnforceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();

                int valid = 0, invalid = 0, fixed_ = 0;
                var issues = new List<string>();

                using (var tx = new Transaction(doc, "STING Revision Naming Enforcement"))
                {
                    tx.Start();
                    foreach (var rev in revisions)
                    {
                        string numStr = "";
                        try { numStr = rev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                        string error = RevisionEngine.ValidateRevisionNumber(numStr);
                        if (error == null)
                        {
                            valid++;
                            continue;
                        }
                        invalid++;
                        issues.Add($"Seq {rev.SequenceNumber}: {error}");

                        // Auto-fix: update description with proper naming
                        string desc = rev.Description ?? "";
                        if (!desc.StartsWith("REV-"))
                        {
                            string newDesc = RevisionEngine.BuildRevisionName(doc, rev.SequenceNumber, desc);
                            rev.Description = newDesc;
                            fixed_++;
                        }
                    }
                    if (fixed_ > 0)
                        tx.Commit();
                    else
                        tx.RollBack();
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"REVISION NAMING AUDIT\n");
                sb.AppendLine($"Total revisions: {revisions.Count}");
                sb.AppendLine($"Valid naming: {valid}");
                sb.AppendLine($"Invalid naming: {invalid}");
                sb.AppendLine($"Auto-fixed descriptions: {fixed_}\n");
                if (issues.Count > 0)
                {
                    sb.AppendLine("Issues:");
                    foreach (string issue in issues.Take(15))
                        sb.AppendLine($"  • {issue}");
                }
                sb.AppendLine("\nISO 19650 Naming Convention:");
                sb.AppendLine("  P01-P99: Preliminary issues");
                sb.AppendLine("  C01-C99: Construction issues");
                sb.AppendLine("  A-Z: As-built issues");

                TaskDialog.Show("StingTools Revision Naming", sb.ToString());
                StingLog.Info($"Revision naming audit: {valid} valid, {invalid} invalid, {fixed_} fixed");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision naming enforcement failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Integrate revision data with ISO 19650 tags — update REV token on elements
    /// affected by the latest revision and rebuild tags to include revision info.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionTagIntegrationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;

                // Find the latest revision
                var latestRev = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefault();

                if (latestRev == null)
                {
                    TaskDialog.Show("StingTools Tag Integration", "No revisions found in project.");
                    return Result.Succeeded;
                }

                string revNum = "";
                try { revNum = latestRev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                string revCode = !string.IsNullOrEmpty(revNum) ? revNum : $"R{latestRev.SequenceNumber:D2}";

                // Find elements in revision clouds
                var clouds = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .Where(c => c.RevisionId == latestRev.Id)
                    .ToList();

                // Get elements near revision clouds (by view)
                var affectedElements = new HashSet<ElementId>();
                foreach (var cloud in clouds)
                {
                    if (cloud.OwnerViewId == ElementId.InvalidElementId) continue;
                    var view = doc.GetElement(cloud.OwnerViewId) as View;
                    if (view == null) continue;

                    BoundingBoxXYZ cloudBB = cloud.get_BoundingBox(view);
                    if (cloudBB == null) continue;

                    // Find elements within cloud bounds
                    var outline = new Outline(cloudBB.Min, cloudBB.Max);
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);
                    var elemsInCloud = new FilteredElementCollector(doc, cloud.OwnerViewId)
                        .WherePasses(bbFilter)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    foreach (var id in elemsInCloud)
                        affectedElements.Add(id);
                }

                // Also check snapshot-based changes
                var prevSnapshot = RevisionEngine.LoadLatestSnapshot(doc);
                var currentSnapshot = RevisionEngine.TakeTagSnapshot(doc);
                if (prevSnapshot != null)
                {
                    var changes = RevisionEngine.CompareSnapshots(prevSnapshot, currentSnapshot);
                    foreach (var change in changes)
                        affectedElements.Add(new ElementId(change.ElementId));
                }

                int updated = 0;
                int tagsRebuilt = 0;
                using (var tx = new Transaction(doc, "STING Revision Tag Integration"))
                {
                    tx.Start();
                    foreach (var id in affectedElements)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;

                        // Update REV parameter using ParamRegistry constant
                        string currentRev = ParameterHelpers.GetString(el, ParamRegistry.REV);
                        if (currentRev != revCode)
                        {
                            ParameterHelpers.SetString(el, ParamRegistry.REV, revCode, true);
                            updated++;
                        }

                        // Update STATUS if element doesn't have one
                        string currentStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                        if (string.IsNullOrEmpty(currentStatus))
                            ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, "NEW");

                        // Rebuild TAG7D (lifecycle section) to include new revision info
                        string tag7d = ParameterHelpers.GetString(el, ParamRegistry.TAG7D);
                        if (!string.IsNullOrEmpty(tag7d) && !tag7d.Contains(revCode))
                        {
                            // Append revision reference to lifecycle narrative
                            string updated7d = tag7d.TrimEnd() + $" | Rev {revCode}";
                            ParameterHelpers.SetString(el, ParamRegistry.TAG7D, updated7d, true);
                            tagsRebuilt++;
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("StingTools Revision Tag Integration",
                    $"Tag-Revision Integration Complete\n\n" +
                    $"Revision: {revCode} ({latestRev.Description ?? ""})\n" +
                    $"Elements in revision clouds: {clouds.Count} clouds scanned\n" +
                    $"Affected elements: {affectedElements.Count}\n" +
                    $"REV parameter updated: {updated}\n" +
                    $"TAG7D lifecycle updated: {tagsRebuilt}\n\n" +
                    "Elements affected by this revision now carry the revision code in their tag data.\n" +
                    "TAG7 Section D (Lifecycle) updated with revision reference.");

                StingLog.Info($"Revision tag integration: {updated} elements updated with {revCode}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision tag integration failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Bulk stamp all selected elements with a revision code and update their tags.
    /// Quick way to mark elements as belonging to a specific revision.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BulkRevisionStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var uidoc = _ctx.UIDoc;
                var doc = _ctx.Doc;
                var selIds = uidoc.Selection.GetElementIds();

                if (selIds.Count == 0)
                {
                    TaskDialog.Show("StingTools Bulk Revision Stamp",
                        "Select elements to stamp with a revision code.");
                    return Result.Succeeded;
                }

                // Get available revisions
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .ToList();

                string revCode;
                if (revisions.Count > 0)
                {
                    var latest = revisions[0];
                    string numStr = "";
                    try { numStr = latest.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                    revCode = !string.IsNullOrEmpty(numStr) ? numStr : $"R{latest.SequenceNumber:D2}";
                }
                else
                {
                    revCode = "P01";
                }

                int stamped = 0;
                int skipped = 0;
                using (var tx = new Transaction(doc, "STING Bulk Revision Stamp"))
                {
                    tx.Start();
                    foreach (ElementId id in selIds)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        try
                        {
                            ParameterHelpers.SetString(el, ParamRegistry.REV, revCode, true);
                            stamped++;
                        }
                        catch (Exception elEx)
                        {
                            skipped++;
                            StingLog.Warn($"BulkRevisionStamp: Cannot write element {id.Value}: {elEx.Message}");
                        }
                    }
                    tx.Commit();
                }

                string stampMsg = $"Stamped {stamped} elements with revision code '{revCode}'.";
                if (skipped > 0)
                    stampMsg += $"\n{skipped} elements skipped (read-only or on unowned workset).";
                TaskDialog.Show("StingTools Bulk Revision Stamp", stampMsg);
                StingLog.Info($"Bulk revision stamp: {stamped} elements → {revCode}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Bulk revision stamp failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export comprehensive revision history report including all snapshots,
    /// changes, and element tracking data.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                string revDir = RevisionEngine.GetRevisionDir(doc);
                var wb = new ClosedXML.Excel.XLWorkbook();

                // --- Sheet 1: Revision Register ---
                var wsReg = wb.AddWorksheet("Revision_Register");
                string[] regHeaders = { "Seq", "Number", "Date", "Description", "Issued",
                    "Visibility", "Clouds", "Significance", "NamingValid" };
                for (int i = 0; i < regHeaders.Length; i++)
                {
                    wsReg.Cell(1, i + 1).Value = regHeaders[i];
                    wsReg.Cell(1, i + 1).Style.Font.Bold = true;
                    wsReg.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
                    wsReg.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }

                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();
                var cloudCounts = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .GroupBy(c => c.RevisionId.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                int row = 2;
                foreach (var rev in revisions)
                {
                    string numStr = "";
                    try { numStr = rev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Revision number read failed: {ex.Message}"); }
                    wsReg.Cell(row, 1).Value = rev.SequenceNumber;
                    wsReg.Cell(row, 2).Value = numStr;
                    wsReg.Cell(row, 3).Value = rev.RevisionDate ?? "";
                    wsReg.Cell(row, 4).Value = rev.Description ?? "";
                    wsReg.Cell(row, 5).Value = rev.Issued ? "Yes" : "No";
                    wsReg.Cell(row, 6).Value = rev.Visibility.ToString();
                    wsReg.Cell(row, 7).Value = cloudCounts.TryGetValue(rev.Id.Value, out int ccVal) ? ccVal : 0;
                    wsReg.Cell(row, 8).Value = "";
                    wsReg.Cell(row, 9).Value = RevisionEngine.ValidateRevisionNumber(numStr) == null ? "Valid" : "Invalid";
                    row++;
                }
                wsReg.Columns().AdjustToContents();

                // --- Sheet 2: Change History ---
                var wsHist = wb.AddWorksheet("Change_History");
                string[] histHeaders = { "Snapshot", "Timestamp", "ElementId", "Category",
                    "ChangeType", "Parameter", "OldValue", "NewValue" };
                for (int i = 0; i < histHeaders.Length; i++)
                {
                    wsHist.Cell(1, i + 1).Value = histHeaders[i];
                    wsHist.Cell(1, i + 1).Style.Font.Bold = true;
                    wsHist.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#2E7D32");
                    wsHist.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }

                var snapshotFiles = Directory.GetFiles(revDir, "snapshot_*.json")
                    .OrderBy(f => File.GetLastWriteTime(f))
                    .ToList();

                row = 2;
                for (int i = 1; i < snapshotFiles.Count && row < 10000; i++)
                {
                    var before = RevisionEngine.LoadSnapshotFile(snapshotFiles[i - 1]);
                    var after = RevisionEngine.LoadSnapshotFile(snapshotFiles[i]);
                    var changes = RevisionEngine.CompareSnapshots(before, after);
                    string snapLabel = Path.GetFileNameWithoutExtension(snapshotFiles[i]);
                    string timestamp = File.GetLastWriteTime(snapshotFiles[i]).ToString("yyyy-MM-dd HH:mm");

                    foreach (var change in changes)
                    {
                        var el = doc.GetElement(new ElementId(change.ElementId));
                        string cat = el != null ? ParameterHelpers.GetCategoryName(el) : "Unknown";
                        if (change.ChangedParams.Count == 0)
                        {
                            wsHist.Cell(row, 1).Value = snapLabel;
                            wsHist.Cell(row, 2).Value = timestamp;
                            wsHist.Cell(row, 3).Value = change.ElementId;
                            wsHist.Cell(row, 4).Value = cat;
                            wsHist.Cell(row, 5).Value = change.ChangeType;
                            row++;
                        }
                        else
                        {
                            foreach (var p in change.ChangedParams)
                            {
                                wsHist.Cell(row, 1).Value = snapLabel;
                                wsHist.Cell(row, 2).Value = timestamp;
                                wsHist.Cell(row, 3).Value = change.ElementId;
                                wsHist.Cell(row, 4).Value = cat;
                                wsHist.Cell(row, 5).Value = change.ChangeType;
                                wsHist.Cell(row, 6).Value = p.ParamName;
                                wsHist.Cell(row, 7).Value = p.OldValue;
                                wsHist.Cell(row, 8).Value = p.NewValue;
                                row++;
                                if (row >= 10000) break;
                            }
                        }
                    }
                }
                wsHist.Columns().AdjustToContents();

                string outputPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_Revision_Report", ".xlsx");
                wb.SaveAs(outputPath);
                wb.Dispose();

                TaskDialog.Show("StingTools Revision Export",
                    $"Revision report exported.\n\n" +
                    $"Revisions: {revisions.Count}\n" +
                    $"Snapshots processed: {snapshotFiles.Count}\n" +
                    $"File: {outputPath}");
                StingLog.Info($"Revision report exported → {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Automatically detect tag changes and create a new revision when
    /// significant modifications are detected. Uses formula-driven significance
    /// scoring to decide when a revision is warranted.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoRevisionOnTagChangeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;

                // Load previous snapshot
                var prevSnapshot = RevisionEngine.LoadLatestSnapshot(doc);
                if (prevSnapshot == null)
                {
                    // First run — take baseline
                    var baseline = RevisionEngine.TakeTagSnapshot(doc);
                    RevisionEngine.SaveSnapshot(doc, baseline, "baseline");
                    TaskDialog.Show("StingTools Auto Revision",
                        $"Baseline snapshot created ({baseline.Count} elements).\n\n" +
                        "Run this command again after making changes to auto-detect if a revision is needed.");
                    return Result.Succeeded;
                }

                var currentSnapshot = RevisionEngine.TakeTagSnapshot(doc);
                var changes = RevisionEngine.CompareSnapshots(prevSnapshot, currentSnapshot);

                if (changes.Count == 0)
                {
                    TaskDialog.Show("StingTools Auto Revision",
                        "No changes detected since last snapshot. No revision needed.");
                    return Result.Succeeded;
                }

                // --- Significance scoring formula ---
                // Score = (Added*3 + Deleted*5 + Modified*1) + (IdentityChanges*10) + (SystemChanges*5)
                int added = changes.Count(c => c.ChangeType == "Added");
                int deleted = changes.Count(c => c.ChangeType == "Deleted");
                int modified = changes.Count(c => c.ChangeType == "Modified");
                int identityChanges = changes.Sum(c => c.ChangedParams.Count(p =>
                    p.ParamName == ParamRegistry.DISC || p.ParamName == ParamRegistry.PROD));
                int systemChanges = changes.Sum(c => c.ChangedParams.Count(p =>
                    p.ParamName == ParamRegistry.SYS || p.ParamName == ParamRegistry.FUNC));

                double score = (added * 3) + (deleted * 5) + (modified * 1)
                    + (identityChanges * 10) + (systemChanges * 5);

                string significance = RevisionEngine.ClassifyRevisionSignificance(changes);
                string narrative = RevisionEngine.BuildChangeNarrative(changes);

                // LG-03: Per-discipline thresholds — safety-critical disciplines have lower
                // thresholds to trigger revisions more readily
                var discThresholds = new Dictionary<string, double>
                {
                    { "FP", 5 },   // Fire Protection — lowest threshold (safety-critical)
                    { "E", 7 },    // Electrical — lower threshold
                    { "M", 8 },    // Mechanical — lower threshold
                    { "P", 8 },    // Plumbing — lower threshold
                    { "S", 8 },    // Structural — lower threshold
                    { "A", 10 },   // Architectural — standard
                    { "G", 12 },   // General — higher threshold
                    { "LV", 7 },   // Low Voltage — lower threshold
                };
                double defaultThreshold = 10;

                // Use the lowest applicable threshold from disciplines present in the changes
                var discBreakdownForThreshold = RevisionEngine.GetChangeSummaryByDiscipline(doc, changes);
                double effectiveThreshold = defaultThreshold;
                string triggerDisc = "";
                foreach (var kvp in discBreakdownForThreshold)
                {
                    if (discThresholds.TryGetValue(kvp.Key, out double discThresh) && discThresh < effectiveThreshold)
                    {
                        effectiveThreshold = discThresh;
                        triggerDisc = kvp.Key;
                    }
                }

                bool autoCreate = score >= effectiveThreshold;

                if (!autoCreate)
                {
                    TaskDialog.Show("StingTools Auto Revision",
                        $"Changes detected but below auto-revision threshold.\n\n" +
                        $"Score: {score:F0} (threshold: {effectiveThreshold:F0}" +
                        (string.IsNullOrEmpty(triggerDisc) ? "" : $", driven by {triggerDisc}") + ")\n" +
                        $"Significance: {significance}\n\n" +
                        narrative +
                        "\nUse 'Create Revision' to manually create one.");
                    // Save snapshot anyway
                    RevisionEngine.SaveSnapshot(doc, currentSnapshot, "check");
                    return Result.Succeeded;
                }

                // Auto-create revision and stamp affected elements
                int nextSeq = RevisionEngine.GetNextRevisionSeq(doc);
                string description = RevisionEngine.BuildRevisionName(doc, nextSeq,
                    $"Auto_{significance}_{changes.Count}chg");

                string revCode = $"P{nextSeq:D2}";
                int stamped = 0;
                using (var tx = new Transaction(doc, "STING Auto Revision on Tag Change"))
                {
                    tx.Start();
                    var rev = Revision.Create(doc);
                    rev.Description = description;
                    rev.RevisionDate = DateTime.Now.ToString("yyyy-MM-dd");
                    rev.Visibility = RevisionVisibility.CloudAndTagVisible;

                    // Get the actual revision number assigned by Revit
                    try
                    {
                        string assignedNum = rev.RevisionNumber;
                        if (!string.IsNullOrEmpty(assignedNum)) revCode = assignedNum;
                    }
                    catch (Exception ex) { StingLog.Warn($"Assigned revision number read failed: {ex.Message}"); }

                    // Stamp affected elements with revision code + update STATUS
                    stamped = RevisionEngine.StampAffectedElements(doc, changes, revCode);

                    tx.Commit();
                }

                RevisionEngine.SaveSnapshot(doc, currentSnapshot, $"post_auto_rev_{nextSeq}");

                // Prune old snapshots to prevent disk bloat
                RevisionEngine.PruneSnapshots(doc);

                // Discipline breakdown for reporting
                var discBreakdown = RevisionEngine.GetChangeSummaryByDiscipline(doc, changes);
                var discSb = new System.Text.StringBuilder();
                if (discBreakdown.Count > 0)
                {
                    discSb.AppendLine("\nChanges by Discipline:");
                    foreach (var kvp in discBreakdown.OrderByDescending(k => k.Value))
                        discSb.AppendLine($"  {kvp.Key}: {kvp.Value} elements");
                }

                // Invalidate compliance cache so dashboard shows fresh data
                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();

                TaskDialog.Show("StingTools Auto Revision",
                    $"Auto-Revision Created!\n\n" +
                    $"Significance Score: {score:F0}\n" +
                    $"Classification: {significance}\n" +
                    $"Revision: {description}\n" +
                    $"REV code stamped on {stamped} affected elements\n\n" +
                    $"Formula: (Added×3) + (Deleted×5) + (Modified×1) + (Identity×10) + (System×5)\n" +
                    $"         ({added}×3) + ({deleted}×5) + ({modified}×1) + ({identityChanges}×10) + ({systemChanges}×5) = {score:F0}\n\n" +
                    narrative + discSb.ToString());

                StingLog.Info($"Auto-revision created: score={score:F0}, significance={significance}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Auto revision on tag change failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  REVISION MANAGEMENT — Enhanced Commands
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Multi-stage revision approval workflow: Draft → Review → Approved → Issued.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionApprovalWorkflowCommand : IExternalCommand
    {
        private static readonly string[] WorkflowStages = new[]
        {
            "Draft", "Internal Review", "Coordinator Review",
            "Client Review", "Approved", "Issued"
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                // Get all revisions
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();

                if (revisions.Count == 0)
                {
                    TaskDialog.Show("Revision Approval", "No revisions found in the project.");
                    return Result.Succeeded;
                }

                // Show current revision status
                var sb = new StringBuilder();
                sb.AppendLine("Revision Approval Workflow");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine("Workflow: Draft → Review → Approved → Issued\n");

                foreach (var rev in revisions)
                {
                    string desc = rev.Description ?? "(no description)";
                    string status = rev.Issued ? "ISSUED" : "DRAFT";
                    sb.AppendLine($"  Seq {rev.SequenceNumber}: {desc}");
                    sb.AppendLine($"    Status: {status}  |  Date: {rev.RevisionDate}");
                }

                // Offer workflow actions
                var td = new TaskDialog("Revision Approval Workflow");
                td.MainInstruction = $"{revisions.Count} revisions in project";
                td.MainContent = sb.ToString();
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Advance Latest to Issued", "Mark the latest revision as Issued");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Create Review Revision", "Add a new revision in Review status");
                td.CommonButtons = TaskDialogCommonButtons.Close;

                var result = td.Show();

                if (result == TaskDialogResult.CommandLink1)
                {
                    var latest = revisions.Last();
                    if (latest.Issued)
                    {
                        TaskDialog.Show("Revision Approval", "Latest revision is already issued.");
                        return Result.Succeeded;
                    }

                    using (var tx = new Transaction(doc, "STING Revision Approval"))
                    {
                        tx.Start();
                        latest.Issued = true;
                        tx.Commit();
                    }

                    TaskDialog.Show("Revision Approval",
                        $"Revision {latest.SequenceNumber} ({latest.Description}) marked as ISSUED.");
                    StingLog.Info($"Revision {latest.SequenceNumber} issued via approval workflow");
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    using (var tx = new Transaction(doc, "STING Create Review Revision"))
                    {
                        tx.Start();
                        var newRev = Revision.Create(doc);
                        newRev.Description = $"Review — {DateTime.Now:yyyy-MM-dd}";
                        newRev.RevisionDate = DateTime.Now.ToString("yyyy-MM-dd");
                        tx.Commit();

                        TaskDialog.Show("Revision Approval",
                            $"Created review revision: Seq {newRev.SequenceNumber}");
                        StingLog.Info($"Review revision created: Seq {newRev.SequenceNumber}");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RevisionApprovalWorkflowCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Track revision distribution: who received, when, acknowledgement.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionDistributionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                // Load distribution records from BIM Manager folder
                string distPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "revision_distribution.json");
                Newtonsoft.Json.Linq.JArray distributions;
                if (File.Exists(distPath))
                {
                    try { distributions = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(distPath)); }
                    catch (Exception ex) { StingLog.Warn($"Distribution load failed: {ex.Message}"); distributions = new Newtonsoft.Json.Linq.JArray(); }
                }
                else
                    distributions = new Newtonsoft.Json.Linq.JArray();

                // Get all issued revisions
                var issued = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .Where(r => r.Issued)
                    .OrderBy(r => r.SequenceNumber)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("Revision Distribution Report");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine($"Issued revisions: {issued.Count}");
                sb.AppendLine($"Distribution records: {distributions.Count}");

                foreach (var rev in issued)
                {
                    sb.AppendLine($"\n  Revision {rev.SequenceNumber}: {rev.Description}");
                    sb.AppendLine($"    Date: {rev.RevisionDate}  |  Issued: Yes");

                    // Find matching distribution records
                    var matches = distributions.Where(d =>
                        d["revision_seq"]?.ToString() == rev.SequenceNumber.ToString()).ToList();

                    if (matches.Count > 0)
                    {
                        foreach (var dist in matches)
                            sb.AppendLine($"    → Sent to: {dist["recipient"]}  on {dist["date_sent"]}  Ack: {dist["acknowledged"] ?? "Pending"}");
                    }
                    else
                        sb.AppendLine("    → No distribution records");
                }

                TaskDialog.Show("STING Revision Distribution", sb.ToString());
                StingLog.Info($"RevisionDistribution: {issued.Count} issued, {distributions.Count} records");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RevisionDistributionCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Side-by-side parameter changes between revisions with diff highlighting.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionComparisonReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                // Load revision snapshots
                string snapshotDir = BIMManagerEngine.GetBIMManagerFilePath(doc, "");
                if (!Directory.Exists(snapshotDir))
                {
                    TaskDialog.Show("Revision Comparison",
                        "No revision snapshots found.\nUse 'Track Element Revisions' first to create snapshots.");
                    return Result.Succeeded;
                }

                var snapshotFiles = Directory.GetFiles(snapshotDir, "revision_snapshot_*.json")
                    .OrderBy(f => f)
                    .ToList();

                if (snapshotFiles.Count < 2)
                {
                    TaskDialog.Show("Revision Comparison",
                        $"Need at least 2 revision snapshots for comparison.\nFound: {snapshotFiles.Count}");
                    return Result.Succeeded;
                }

                // Compare latest two snapshots
                string olderFile = snapshotFiles[snapshotFiles.Count - 2];
                string newerFile = snapshotFiles[snapshotFiles.Count - 1];

                Newtonsoft.Json.Linq.JObject older, newer;
                try
                {
                    older = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(olderFile));
                    newer = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(newerFile));
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Revision Comparison", $"Failed to parse snapshots:\n{ex.Message}");
                    return Result.Failed;
                }

                int added = 0, removed = 0, changed = 0;
                var changes = new StringBuilder();

                // Compare element sets
                var olderElements = older.Properties().Select(p => p.Name).ToHashSet();
                var newerElements = newer.Properties().Select(p => p.Name).ToHashSet();

                var addedIds = newerElements.Except(olderElements).ToList();
                var removedIds = olderElements.Except(newerElements).ToList();
                var commonIds = olderElements.Intersect(newerElements).ToList();

                added = addedIds.Count;
                removed = removedIds.Count;

                foreach (string id in commonIds)
                {
                    string oldTag = older[id]?["tag"]?.ToString() ?? "";
                    string newTag = newer[id]?["tag"]?.ToString() ?? "";
                    if (oldTag != newTag)
                    {
                        changed++;
                        if (changed <= 20) // Show first 20 changes
                            changes.AppendLine($"  {id}: {oldTag} → {newTag}");
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("Revision Comparison Report");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine($"Older: {Path.GetFileName(olderFile)}");
                sb.AppendLine($"Newer: {Path.GetFileName(newerFile)}");
                sb.AppendLine();
                sb.AppendLine($"  Added:   {added} elements");
                sb.AppendLine($"  Removed: {removed} elements");
                sb.AppendLine($"  Changed: {changed} elements");

                if (changes.Length > 0)
                {
                    sb.AppendLine("\nTag Changes (first 20):");
                    sb.Append(changes);
                }

                TaskDialog.Show("STING Revision Comparison", sb.ToString());
                StingLog.Info($"RevisionComparison: +{added} -{removed} ~{changed}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RevisionComparisonReportCommand failed", ex);
                return Result.Failed;
            }
        }
    }
}
