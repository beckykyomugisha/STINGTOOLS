using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G13: Parameter Change Tracking / Diff Commands
    //
    //  Snapshot STING parameter values, compare snapshots to detect changes,
    //  and produce change reports. Useful for BEP compliance tracking and
    //  model progression audits.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: ParameterDiffEngine ──

    internal static class ParameterDiffEngine
    {
        private const string SnapshotFolder = "STING_SNAPSHOTS";

        /// <summary>Take a snapshot of all STING parameter values on tagged elements.</summary>
        internal static ParameterSnapshot TakeSnapshot(Document doc, string label)
        {
            var snapshot = new ParameterSnapshot
            {
                Label = label,
                Timestamp = DateTime.Now,
                ProjectName = doc.ProjectInformation?.Name ?? "(unknown)"
            };

            var tokenParams = new[] { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD,
                ParamRegistry.SEQ, ParamRegistry.TAG1, "ASS_TAG_7_TXT", "ASS_STATUS_TXT", "ASS_REV_TXT" };

            // S1.4 (N-G1): pre-filter with ElementMulticategoryFilter so the
            // ParameterHelpers.GetString per-element call only runs on
            // tagged categories, not every element in the document.
            var elements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && !string.IsNullOrWhiteSpace(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));

            foreach (var el in elements)
            {
                try
                {
                    var record = new ElementSnapshot
                    {
                        ElementId = el.Id.Value,
                        Category = el.Category?.Name ?? "",
                    };
                    foreach (var p in tokenParams)
                        record.Values[p] = ParameterHelpers.GetString(el, p);
                    snapshot.Elements.Add(record);
                }
                catch (Exception ex) { StingLog.Warn($"Snapshot element: {ex.Message}"); }
            }

            return snapshot;
        }

        /// <summary>Save snapshot to JSON file alongside the project.</summary>
        internal static string SaveSnapshot(Document doc, ParameterSnapshot snapshot)
        {
            string dir = GetSnapshotDir(doc);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string fileName = $"SNAPSHOT_{snapshot.Timestamp:yyyyMMdd_HHmmss}_{SanitizeFileName(snapshot.Label)}.json";
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            return path;
        }

        // Deserialized-snapshot cache keyed by (path, mtime ticks) — LoadSnapshot is
        // hit repeatedly by the dashboard + CompareSnapshots; avoid re-parsing
        // multi-megabyte element arrays each time. Cleared when a snapshot is
        // overwritten (mtime changes invalidate automatically).
        private static readonly Dictionary<string, (long MTimeTicks, ParameterSnapshot Snap)> _snapCache
            = new Dictionary<string, (long, ParameterSnapshot)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Load all available snapshots for the project.
        /// Uses streaming Json.NET parse so we skip the Elements array — the
        /// summary only needs Label / Timestamp / element count.</summary>
        internal static List<SnapshotSummary> ListSnapshots(Document doc)
        {
            var summaries = new List<SnapshotSummary>();
            string dir = GetSnapshotDir(doc);
            if (!Directory.Exists(dir)) return summaries;

            foreach (var file in Directory.GetFiles(dir, "SNAPSHOT_*.json").OrderByDescending(f => f))
            {
                try
                {
                    var summary = ReadSnapshotSummaryStreaming(file);
                    if (summary != null) summaries.Add(summary);
                }
                catch (Exception ex) { StingLog.Warn($"ListSnapshots: {ex.Message}"); }
            }
            return summaries;
        }

        /// <summary>Streaming-parse Label/Timestamp and count Elements without
        /// materialising the full snapshot (snapshots can be many MB).</summary>
        private static SnapshotSummary ReadSnapshotSummaryStreaming(string file)
        {
            using (var sr = new StreamReader(file))
            using (var jr = new JsonTextReader(sr))
            {
                string label = null;
                DateTime timestamp = default;
                int elementCount = 0;

                while (jr.Read())
                {
                    if (jr.TokenType != JsonToken.PropertyName) continue;
                    string name = (string)jr.Value;
                    jr.Read();
                    if (name == "Label") label = jr.Value?.ToString();
                    else if (name == "Timestamp")
                    {
                        if (jr.Value is DateTime dt) timestamp = dt;
                        else if (jr.Value != null) DateTime.TryParse(jr.Value.ToString(), out timestamp);
                    }
                    else if (name == "Elements" && jr.TokenType == JsonToken.StartArray)
                    {
                        int depth = 1;
                        while (depth > 0 && jr.Read())
                        {
                            if (jr.TokenType == JsonToken.StartObject && depth == 1) elementCount++;
                            if (jr.TokenType == JsonToken.StartArray) depth++;
                            else if (jr.TokenType == JsonToken.EndArray) depth--;
                        }
                    }
                }

                return new SnapshotSummary
                {
                    FilePath = file,
                    Label = label ?? "",
                    Timestamp = timestamp,
                    ElementCount = elementCount
                };
            }
        }

        /// <summary>Load a specific snapshot from file. Cached by (path, mtime).</summary>
        internal static ParameterSnapshot LoadSnapshot(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                long mtime = File.GetLastWriteTimeUtc(filePath).Ticks;
                lock (_snapCache)
                {
                    if (_snapCache.TryGetValue(filePath, out var hit) && hit.MTimeTicks == mtime)
                        return hit.Snap;
                }
                var snap = JsonConvert.DeserializeObject<ParameterSnapshot>(File.ReadAllText(filePath));
                lock (_snapCache) { _snapCache[filePath] = (mtime, snap); }
                return snap;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSnapshot: {ex.Message}");
                return null;
            }
        }

        /// <summary>Compare two snapshots and produce a diff.</summary>
        internal static DiffResult CompareSnapshots(ParameterSnapshot before, ParameterSnapshot after)
        {
            var result = new DiffResult
            {
                BeforeLabel = before.Label,
                AfterLabel = after.Label,
                BeforeTimestamp = before.Timestamp,
                AfterTimestamp = after.Timestamp
            };

            var beforeIndex = before.Elements.ToDictionary(e => e.ElementId);
            var afterIndex = after.Elements.ToDictionary(e => e.ElementId);

            // Find modified elements
            foreach (var kvp in afterIndex)
            {
                if (beforeIndex.TryGetValue(kvp.Key, out var beforeEl))
                {
                    var changes = new List<ParameterChange>();
                    foreach (var param in kvp.Value.Values.Keys)
                    {
                        string oldVal = beforeEl.Values.GetValueOrDefault(param, "");
                        string newVal = kvp.Value.Values.GetValueOrDefault(param, "");
                        if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                        {
                            changes.Add(new ParameterChange { Parameter = param, OldValue = oldVal, NewValue = newVal });
                        }
                    }
                    if (changes.Count > 0)
                    {
                        result.Modified.Add(new ElementDiff
                        {
                            ElementId = kvp.Key,
                            Category = kvp.Value.Category,
                            Tag = kvp.Value.Values.GetValueOrDefault(ParamRegistry.TAG1, ""),
                            Changes = changes
                        });
                    }
                }
                else
                {
                    result.Added.Add(new ElementDiff
                    {
                        ElementId = kvp.Key,
                        Category = kvp.Value.Category,
                        Tag = kvp.Value.Values.GetValueOrDefault(ParamRegistry.TAG1, "")
                    });
                }
            }

            // Find removed elements
            foreach (var kvp in beforeIndex)
            {
                if (!afterIndex.ContainsKey(kvp.Key))
                {
                    result.Removed.Add(new ElementDiff
                    {
                        ElementId = kvp.Key,
                        Category = kvp.Value.Category,
                        Tag = kvp.Value.Values.GetValueOrDefault(ParamRegistry.TAG1, "")
                    });
                }
            }

            return result;
        }

        /// <summary>Compare current model state against a snapshot.</summary>
        internal static DiffResult CompareWithCurrent(Document doc, ParameterSnapshot before)
        {
            var current = TakeSnapshot(doc, "Current");
            return CompareSnapshots(before, current);
        }

        private static string GetSnapshotDir(Document doc)
        {
            string projectPath = doc.PathName;
            if (!string.IsNullOrEmpty(projectPath))
                return Path.Combine(Path.GetDirectoryName(projectPath), SnapshotFolder);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SnapshotFolder);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 50 ? name.Substring(0, 50) : name;
        }
    }

    // ── Data types ──

    internal class ParameterSnapshot
    {
        public string Label { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string ProjectName { get; set; } = "";
        public List<ElementSnapshot> Elements { get; set; } = new List<ElementSnapshot>();
    }

    internal class ElementSnapshot
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    internal class SnapshotSummary
    {
        public string FilePath { get; set; } = "";
        public string Label { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int ElementCount { get; set; }
    }

    internal class DiffResult
    {
        public string BeforeLabel { get; set; } = "";
        public string AfterLabel { get; set; } = "";
        public DateTime BeforeTimestamp { get; set; }
        public DateTime AfterTimestamp { get; set; }
        public List<ElementDiff> Added { get; set; } = new List<ElementDiff>();
        public List<ElementDiff> Removed { get; set; } = new List<ElementDiff>();
        public List<ElementDiff> Modified { get; set; } = new List<ElementDiff>();
    }

    internal class ElementDiff
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public string Tag { get; set; } = "";
        public List<ParameterChange> Changes { get; set; } = new List<ParameterChange>();
    }

    internal class ParameterChange
    {
        public string Parameter { get; set; } = "";
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
    }

    #endregion

    #region ── Commands ──

    /// <summary>Take a parameter snapshot of the current model state.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TakeSnapshotCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            // Get label from user
            TaskDialog td = new TaskDialog("Take Snapshot");
            td.MainInstruction = "Snapshot Label";
            td.MainContent = "Choose a label for this snapshot:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Stage 2 — Concept Design");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Stage 3 — Spatial Coordination");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Stage 4 — Technical Design");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Custom Label...");
            var result = td.Show();

            string label = result switch
            {
                TaskDialogResult.CommandLink1 => "Stage 2 - Concept Design",
                TaskDialogResult.CommandLink2 => "Stage 3 - Spatial Coordination",
                TaskDialogResult.CommandLink3 => "Stage 4 - Technical Design",
                TaskDialogResult.CommandLink4 => $"Snapshot_{DateTime.Now:yyyyMMdd}",
                _ => null
            };
            if (label == null) return Result.Succeeded;

            var progress = StingProgressDialog.Show("Taking Snapshot", 1);
            ParameterSnapshot snapshot;
            try
            {
                progress.Increment("Capturing parameter values...");
                snapshot = ParameterDiffEngine.TakeSnapshot(ctx.Doc, label);
            }
            finally { progress.Close(); }

            string path = ParameterDiffEngine.SaveSnapshot(ctx.Doc, snapshot);
            TaskDialog.Show("Snapshot", $"Snapshot saved:\n\nLabel: {label}\nElements: {snapshot.Elements.Count}\nFile: {path}");
            StingLog.Info($"Snapshot: '{label}' with {snapshot.Elements.Count} elements saved to {path}");
            return Result.Succeeded;
        }
    }

    /// <summary>Compare current model against a previous snapshot.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CompareSnapshotCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var snapshots = ParameterDiffEngine.ListSnapshots(ctx.Doc);
            if (snapshots.Count == 0)
            {
                TaskDialog.Show("Compare", "No snapshots found.\n\nTake a snapshot first using 'Take Snapshot'.");
                return Result.Succeeded;
            }

            var items = snapshots.Select(s => $"{s.Timestamp:yyyy-MM-dd HH:mm} — {s.Label} ({s.ElementCount} elements)").ToList();
            var picked = StingListPicker.Show("Compare With Snapshot", "Select a snapshot to compare against current model:", items);
            if (picked == null) return Result.Succeeded;

            int idx = items.IndexOf(picked);
            if (idx < 0) return Result.Succeeded;

            var before = ParameterDiffEngine.LoadSnapshot(snapshots[idx].FilePath);
            if (before == null) { TaskDialog.Show("STING", "Could not load snapshot."); return Result.Failed; }

            var diff = ParameterDiffEngine.CompareWithCurrent(ctx.Doc, before);

            var sb = new StringBuilder();
            sb.AppendLine($"Parameter Diff Report");
            sb.AppendLine($"Before: {diff.BeforeLabel} ({diff.BeforeTimestamp:yyyy-MM-dd HH:mm})");
            sb.AppendLine($"After:  Current model\n");
            sb.AppendLine($"  Added:    {diff.Added.Count} elements");
            sb.AppendLine($"  Removed:  {diff.Removed.Count} elements");
            sb.AppendLine($"  Modified: {diff.Modified.Count} elements\n");

            if (diff.Modified.Count > 0)
            {
                // Count changes by parameter
                var paramChanges = diff.Modified.SelectMany(m => m.Changes)
                    .GroupBy(c => c.Parameter).OrderByDescending(g => g.Count());
                sb.AppendLine("── Changes By Parameter ──");
                foreach (var g in paramChanges)
                    sb.AppendLine($"  {g.Key,-25} {g.Count(),5} changes");

                sb.AppendLine("\n── Sample Changes ──");
                foreach (var mod in diff.Modified.Take(10))
                {
                    sb.AppendLine($"  [{mod.Tag}] {mod.Category}");
                    foreach (var c in mod.Changes.Take(3))
                        sb.AppendLine($"    {c.Parameter}: '{c.OldValue}' → '{c.NewValue}'");
                }
                if (diff.Modified.Count > 10) sb.AppendLine($"  ... and {diff.Modified.Count - 10} more");
            }

            TaskDialog.Show("Snapshot Comparison", sb.ToString());
            StingLog.Info($"SnapshotCompare: +{diff.Added.Count} -{diff.Removed.Count} ~{diff.Modified.Count}");
            return Result.Succeeded;
        }
    }

    /// <summary>Export diff report to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SnapshotDiffExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var snapshots = ParameterDiffEngine.ListSnapshots(ctx.Doc);
            if (snapshots.Count == 0) { TaskDialog.Show("STING", "No snapshots found."); return Result.Succeeded; }

            var items = snapshots.Select(s => $"{s.Timestamp:yyyy-MM-dd HH:mm} — {s.Label}").ToList();
            var picked = StingListPicker.Show("Export Diff", "Select baseline snapshot:", items);
            if (picked == null) return Result.Succeeded;

            int idx = items.IndexOf(picked);
            if (idx < 0) return Result.Succeeded;

            var before = ParameterDiffEngine.LoadSnapshot(snapshots[idx].FilePath);
            if (before == null) return Result.Failed;

            var diff = ParameterDiffEngine.CompareWithCurrent(ctx.Doc, before);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "ParameterDiff", ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("ChangeType,ElementId,Category,Tag,Parameter,OldValue,NewValue");
            foreach (var a in diff.Added)
                sb.AppendLine($"Added,{a.ElementId},\"{a.Category}\",\"{a.Tag}\",,,");
            foreach (var r in diff.Removed)
                sb.AppendLine($"Removed,{r.ElementId},\"{r.Category}\",\"{r.Tag}\",,,");
            foreach (var m in diff.Modified)
                foreach (var c in m.Changes)
                    sb.AppendLine($"Modified,{m.ElementId},\"{m.Category}\",\"{m.Tag}\",{c.Parameter},\"{c.OldValue}\",\"{c.NewValue}\"");

            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Diff Export", $"Exported to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion
}
