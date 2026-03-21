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
using StingTools.UI;

namespace StingTools.Select
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G17: Selection Set Commands
    //
    //  Named selection sets: save, recall, combine, and manage element selections.
    //  Persisted to JSON file alongside the project for cross-session recall.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: SelectionSetEngine ──

    internal static class SelectionSetEngine
    {
        private const string FileName = "STING_SELECTION_SETS.json";

        /// <summary>Save current selection as a named set.</summary>
        internal static void SaveSet(Document doc, string name, ICollection<ElementId> elementIds)
        {
            var sets = LoadAllSets(doc);
            sets[name] = new SelectionSetData
            {
                Name = name,
                Timestamp = DateTime.Now,
                ElementIds = elementIds.Select(id => id.Value).ToList(),
                Count = elementIds.Count
            };
            SaveAllSets(doc, sets);
        }

        /// <summary>Load a named selection set.</summary>
        internal static List<ElementId> LoadSet(Document doc, string name)
        {
            var sets = LoadAllSets(doc);
            if (sets.TryGetValue(name, out var data))
            {
                return data.ElementIds
                    .Select(id => new ElementId(id))
                    .Where(id => doc.GetElement(id) != null) // Filter out deleted elements
                    .ToList();
            }
            return new List<ElementId>();
        }

        /// <summary>Delete a named selection set.</summary>
        internal static bool DeleteSet(Document doc, string name)
        {
            var sets = LoadAllSets(doc);
            if (sets.Remove(name))
            {
                SaveAllSets(doc, sets);
                return true;
            }
            return false;
        }

        /// <summary>Get all saved selection set names with metadata.</summary>
        internal static List<SelectionSetData> ListSets(Document doc)
        {
            return LoadAllSets(doc).Values.OrderByDescending(s => s.Timestamp).ToList();
        }

        private static Dictionary<string, SelectionSetData> LoadAllSets(Document doc)
        {
            string path = GetFilePath(doc);
            if (!File.Exists(path)) return new Dictionary<string, SelectionSetData>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<string, SelectionSetData>>(json)
                    ?? new Dictionary<string, SelectionSetData>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SelectionSet load: {ex.Message}");
                return new Dictionary<string, SelectionSetData>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAllSets(Document doc, Dictionary<string, SelectionSetData> sets)
        {
            try
            {
                string path = GetFilePath(doc);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(sets, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SelectionSet save: {ex.Message}"); }
        }

        private static string GetFilePath(Document doc)
        {
            if (!string.IsNullOrEmpty(doc.PathName))
                return Path.Combine(Path.GetDirectoryName(doc.PathName), FileName);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FileName);
        }
    }

    // ── Data type ──

    internal class SelectionSetData
    {
        public string Name { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public List<long> ElementIds { get; set; } = new List<long>();
        public int Count { get; set; }
    }

    #endregion

    #region ── Commands ──

    /// <summary>
    /// Save current selection as a named selection set.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveSelectionSetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var selected = ctx.UIDoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Save Selection Set", "No elements selected.\n\nSelect elements first, then save as a named set.");
                return Result.Succeeded;
            }

            // Get name from existing sets or create new
            var existingSets = SelectionSetEngine.ListSets(ctx.Doc);
            var items = existingSets.Select(s => $"{s.Name} ({s.Count} elements, {s.Timestamp:MMM dd})").ToList();
            items.Insert(0, "— Create New Set —");

            var picked = StingListPicker.Show("Save Selection Set",
                $"Save {selected.Count} elements. Choose set name:", items);
            if (picked == null) return Result.Succeeded;

            string name;
            if (picked.StartsWith("—"))
            {
                // New set — use default name based on category of first element
                var firstEl = ctx.Doc.GetElement(selected.First());
                string catName = firstEl?.Category?.Name ?? "Elements";
                name = $"{catName} Set {existingSets.Count + 1}";
            }
            else
            {
                name = picked.Split(' ')[0]; // Extract name before the count
                // Use full name from existing set
                var matchSet = existingSets.FirstOrDefault(s => picked.StartsWith(s.Name));
                if (matchSet != null) name = matchSet.Name;
            }

            SelectionSetEngine.SaveSet(ctx.Doc, name, selected);
            TaskDialog.Show("Selection Set", $"Saved {selected.Count} elements as '{name}'");
            StingLog.Info($"SelectionSet: Saved '{name}' with {selected.Count} elements");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Recall a saved selection set and set the Revit selection.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RecallSelectionSetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var sets = SelectionSetEngine.ListSets(ctx.Doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("Recall Selection Set", "No saved selection sets found.\n\nSave a selection first.");
                return Result.Succeeded;
            }

            var items = sets.Select(s => $"{s.Name} ({s.Count} elements, {s.Timestamp:MMM dd HH:mm})").ToList();
            var picked = StingListPicker.Show("Recall Selection Set", "Choose a set to recall:", items);
            if (picked == null) return Result.Succeeded;

            var matchSet = sets.FirstOrDefault(s => picked.StartsWith(s.Name));
            if (matchSet == null) return Result.Succeeded;

            var ids = SelectionSetEngine.LoadSet(ctx.Doc, matchSet.Name);
            if (ids.Count == 0)
            {
                TaskDialog.Show("Selection Set", $"Set '{matchSet.Name}' has no valid elements (may have been deleted).");
                return Result.Succeeded;
            }

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Selection Set", $"Selected {ids.Count} elements from '{matchSet.Name}'");
            StingLog.Info($"SelectionSet: Recalled '{matchSet.Name}' with {ids.Count} elements");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Manage saved selection sets: list, delete, export.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ManageSelectionSetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var sets = SelectionSetEngine.ListSets(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Selection Sets — {sets.Count} saved\n");
            if (sets.Count == 0)
            {
                sb.AppendLine("No selection sets saved.");
                TaskDialog.Show("Selection Sets", sb.ToString());
                return Result.Succeeded;
            }

            foreach (var s in sets)
                sb.AppendLine($"  {s.Name,-30} {s.Count,5} elements  ({s.Timestamp:yyyy-MM-dd HH:mm})");

            TaskDialog td = new TaskDialog("Selection Sets");
            td.MainInstruction = $"{sets.Count} saved selection sets";
            td.MainContent = sb.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Delete a selection set");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");
            var result = td.Show();

            if (result == TaskDialogResult.CommandLink1)
            {
                var items = sets.Select(s => new StingListPicker.ListItem { Label = s.Name }).ToList();
                var picked = StingListPicker.Show("Delete Set", "Select set(s) to delete:", items, true);
                if (picked != null)
                {
                    int deleted = 0;
                    foreach (var item in picked)
                        if (SelectionSetEngine.DeleteSet(ctx.Doc, item.Label)) deleted++;
                    TaskDialog.Show("Selection Sets", $"Deleted {deleted} set(s).");
                }
            }

            return Result.Succeeded;
        }
    }

    #endregion
}
