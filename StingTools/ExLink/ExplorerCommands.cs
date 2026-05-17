using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  EXPLORER COMMANDS — Model element browser / audit commands
    //
    //  5 IExternalCommand classes:
    //    1. FamilyBrowserCommand        — browse all loaded families by category
    //    2. TypeBrowserCommand           — browse all family types (FamilySymbol)
    //    3. UnusedElementsCommand        — detect unused families, views, rooms, groups
    //    4. CADImportDetectorCommand     — find all CAD imports/links
    //    5. InPlaceFamilyDetectorCommand — find all in-place families
    // ════════════════════════════════════════════════════════════════════════

    #region ── Row Models ──

    internal class FamilyBrowserRow
    {
        public string Category { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public int TypeCount { get; set; }
        public int InstanceCount { get; set; }
        public bool IsInPlace { get; set; }
        public long FamilyId { get; set; }
    }

    internal class TypeBrowserRow
    {
        public string Category { get; set; } = "";
        public string Family { get; set; } = "";
        public string TypeName { get; set; } = "";
        public int InstanceCount { get; set; }
        public string BuiltInCategory { get; set; } = "";
        public long TypeId { get; set; }
    }

    internal class UnusedElementRow
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public long ElementId { get; set; }
    }

    internal class CADImportRow
    {
        public string LinkName { get; set; } = "";
        public bool IsLinked { get; set; }
        public bool ViewSpecific { get; set; }
        public string PinnedStatus { get; set; } = "";
        public long ElementId { get; set; }
    }

    internal class InPlaceFamilyRow
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public int ElementCount { get; set; }
        public long FamilyId { get; set; }
    }

    #endregion

    #region ── Shared Helpers ──

    internal static class ExplorerHelper
    {
        internal static Dictionary<long, int> BuildFamilyInstanceCounts(Document doc)
        {
            var counts = new Dictionary<long, int>();
            try
            {
                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
                foreach (var fi in instances)
                {
                    try
                    {
                        long famId = fi.Symbol.Family.Id.Value;
                        if (counts.ContainsKey(famId)) counts[famId]++;
                        else counts[famId] = 1;
                    }
                    catch (Exception ex) { StingLog.Warn($"ExplorerHelper instance count: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExplorerHelper BuildFamilyInstanceCounts: {ex.Message}"); }
            return counts;
        }

        internal static Dictionary<long, int> BuildTypeInstanceCounts(Document doc)
        {
            var counts = new Dictionary<long, int>();
            try
            {
                var allInstances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();
                foreach (var el in allInstances)
                {
                    try
                    {
                        var typeId = el.GetTypeId();
                        if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                        long tid = typeId.Value;
                        if (counts.ContainsKey(tid)) counts[tid]++;
                        else counts[tid] = 1;
                    }
                    catch (Exception ex) { StingLog.Warn($"ExplorerHelper type count: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExplorerHelper BuildTypeInstanceCounts: {ex.Message}"); }
            return counts;
        }

        internal static string SafeCategoryName(Element el)
        {
            try { return el?.Category?.Name ?? "(No Category)"; }
            catch (Exception) { return "(No Category)"; }
        }

        internal static string SafeBuiltInCategoryName(Element el)
        {
            try
            {
                if (el?.Category == null) return "(None)";
                var bic = (BuiltInCategory)el.Category.Id.Value;
                return bic.ToString();
            }
            catch (Exception) { return "(None)"; }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    //  1. FamilyBrowserCommand — Browse all loaded families grouped by category
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                if (families.Count == 0)
                {
                    TaskDialog.Show("STING Explorer", "No families found in the document.");
                    return Result.Succeeded;
                }

                var instCounts = ExplorerHelper.BuildFamilyInstanceCounts(doc);

                var rows = new List<FamilyBrowserRow>();
                foreach (var fam in families)
                {
                    try
                    {
                        var symbolIds = fam.GetFamilySymbolIds();
                        int typeCount = symbolIds?.Count ?? 0;
                        instCounts.TryGetValue(fam.Id.Value, out int instanceCount);

                        rows.Add(new FamilyBrowserRow
                        {
                            Category = ExplorerHelper.SafeCategoryName(fam),
                            FamilyName = fam.Name ?? "(Unnamed)",
                            TypeCount = typeCount,
                            InstanceCount = instanceCount,
                            IsInPlace = fam.IsInPlace,
                            FamilyId = fam.Id.Value
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"FamilyBrowser row: {ex.Message}"); }
                }

                rows = rows.OrderBy(r => r.Category).ThenBy(r => r.FamilyName).ToList();

                var dlg = new StingDataGridDialog("STING Family Browser",
                    $"{rows.Count} families loaded in document");
                dlg.AddTextColumn("Category", "Category", 160);
                dlg.AddTextColumn("Family Name", "FamilyName", 220);
                dlg.AddTextColumn("Types", "TypeCount", 60);
                dlg.AddTextColumn("Instances", "InstanceCount", 80);
                dlg.AddCheckColumn("In-Place", "IsInPlace", 65);

                var categories = new[] { "(All)" }
                    .Concat(rows.Select(r => r.Category).Distinct().OrderBy(c => c));
                dlg.AddFilter("Category:", categories, filter =>
                {
                    if (filter == "(All)")
                        dlg.RefreshItems(rows);
                    else
                        dlg.RefreshItems(rows.Where(r => r.Category == filter).ToList());
                });

                dlg.AddActionButton("Select Instances", "Select", true);
                dlg.AddActionButton("Close", "Cancel");
                dlg.SetItems(rows);

                if (dlg.ShowDialog() == true && dlg.ResultAction == "Select")
                {
                    var selectedFamIds = dlg.SelectedItems
                        .OfType<FamilyBrowserRow>()
                        .Select(r => r.FamilyId)
                        .ToHashSet();

                    if (selectedFamIds.Count > 0)
                    {
                        var matchIds = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .Where(fi =>
                            {
                                try { return selectedFamIds.Contains(fi.Symbol.Family.Id.Value); }
                                catch (Exception) { return false; }
                            })
                            .Select(fi => fi.Id)
                            .ToList();

                        if (matchIds.Count > 0)
                            ctx.UIDoc.Selection.SetElementIds(matchIds);
                        else
                            TaskDialog.Show("STING Explorer", "No instances found for selected families.");
                    }
                }

                StingLog.Info($"FamilyBrowser: displayed {rows.Count} families");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyBrowserCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  2. TypeBrowserCommand — Browse all family types (FamilySymbol)
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TypeBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var allSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                if (allSymbols.Count == 0)
                {
                    TaskDialog.Show("STING Explorer", "No family types found.");
                    return Result.Succeeded;
                }

                var instCounts = ExplorerHelper.BuildTypeInstanceCounts(doc);

                var rows = new List<TypeBrowserRow>();
                foreach (var fs in allSymbols)
                {
                    try
                    {
                        instCounts.TryGetValue(fs.Id.Value, out int instCount);
                        string familyName = "";
                        try { familyName = fs.Family?.Name ?? ""; }
                        catch (Exception) { /* orphaned symbol */ }

                        rows.Add(new TypeBrowserRow
                        {
                            Category = ExplorerHelper.SafeCategoryName(fs),
                            Family = familyName,
                            TypeName = fs.Name ?? "(Unnamed)",
                            InstanceCount = instCount,
                            BuiltInCategory = ExplorerHelper.SafeBuiltInCategoryName(fs),
                            TypeId = fs.Id.Value
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"TypeBrowser row: {ex.Message}"); }
                }

                rows = rows.OrderBy(r => r.Category).ThenBy(r => r.Family).ThenBy(r => r.TypeName).ToList();

                var dlg = new StingDataGridDialog("STING Type Browser",
                    $"{rows.Count} family types in document");
                dlg.AddTextColumn("Category", "Category", 150);
                dlg.AddTextColumn("Family", "Family", 180);
                dlg.AddTextColumn("Type Name", "TypeName", 180);
                dlg.AddTextColumn("Instances", "InstanceCount", 80);
                dlg.AddTextColumn("BuiltInCategory", "BuiltInCategory", 180);

                var categories = new[] { "(All)" }
                    .Concat(rows.Select(r => r.Category).Distinct().OrderBy(c => c));
                dlg.AddFilter("Category:", categories, filter =>
                {
                    if (filter == "(All)")
                        dlg.RefreshItems(rows);
                    else
                        dlg.RefreshItems(rows.Where(r => r.Category == filter).ToList());
                });

                dlg.AddActionButton("Close", "Cancel");
                dlg.SetItems(rows);
                dlg.ShowDialog();

                StingLog.Info($"TypeBrowser: displayed {rows.Count} types");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TypeBrowserCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  3. UnusedElementsCommand — Detect unused families, views, rooms, groups
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class UnusedElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var rows = new List<UnusedElementRow>();

                // --- Families with 0 instances ---
                var instCounts = ExplorerHelper.BuildFamilyInstanceCounts(doc);
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                int unusedFamilies = 0;
                foreach (var fam in families)
                {
                    try
                    {
                        instCounts.TryGetValue(fam.Id.Value, out int count);
                        if (count == 0)
                        {
                            rows.Add(new UnusedElementRow
                            {
                                Kind = "Family",
                                Name = fam.Name ?? "(Unnamed)",
                                Category = ExplorerHelper.SafeCategoryName(fam),
                                ElementId = fam.Id.Value
                            });
                            unusedFamilies++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"UnusedElements family: {ex.Message}"); }
                }

                // --- Views not placed on any sheet ---
                int unusedViews = 0;
                try
                {
                    var sheetsWithViewports = new HashSet<long>();
                    var viewports = new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>();
                    foreach (var vp in viewports)
                    {
                        try { sheetsWithViewports.Add(vp.ViewId.Value); }
                        catch (Exception) { /* skip */ }
                    }

                    var allViews = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet
                                    && v.ViewType != ViewType.Internal
                                    && v.ViewType != ViewType.ProjectBrowser
                                    && v.ViewType != ViewType.SystemBrowser);

                    foreach (var v in allViews)
                    {
                        try
                        {
                            if (!sheetsWithViewports.Contains(v.Id.Value))
                            {
                                rows.Add(new UnusedElementRow
                                {
                                    Kind = "View (not on sheet)",
                                    Name = v.Name ?? "(Unnamed)",
                                    Category = v.ViewType.ToString(),
                                    ElementId = v.Id.Value
                                });
                                unusedViews++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"UnusedElements view: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"UnusedElements views scan: {ex.Message}"); }

                // --- Unplaced rooms ---
                int unplacedRooms = 0;
                try
                {
                    var rooms = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpatialElement))
                        .OfType<Room>();
                    foreach (var room in rooms)
                    {
                        try
                        {
                            if (room.Area <= 0 || room.Location == null)
                            {
                                rows.Add(new UnusedElementRow
                                {
                                    Kind = "Unplaced Room",
                                    Name = room.Name ?? $"Room {room.Number}",
                                    Category = "Rooms",
                                    ElementId = room.Id.Value
                                });
                                unplacedRooms++;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"UnusedElements room: {ex2.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"UnusedElements rooms scan: {ex.Message}"); }

                // --- Empty groups ---
                int emptyGroups = 0;
                try
                {
                    var groups = new FilteredElementCollector(doc)
                        .OfClass(typeof(Group))
                        .Cast<Group>();
                    foreach (var grp in groups)
                    {
                        try
                        {
                            var memberIds = grp.GetMemberIds();
                            if (memberIds == null || memberIds.Count == 0)
                            {
                                rows.Add(new UnusedElementRow
                                {
                                    Kind = "Empty Group",
                                    Name = grp.Name ?? "(Unnamed Group)",
                                    Category = "Groups",
                                    ElementId = grp.Id.Value
                                });
                                emptyGroups++;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"UnusedElements group: {ex2.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"UnusedElements groups scan: {ex.Message}"); }

                // --- Report ---
                var summary = $"Unused Families: {unusedFamilies}\n" +
                              $"Views Not On Sheets: {unusedViews}\n" +
                              $"Unplaced Rooms: {unplacedRooms}\n" +
                              $"Empty Groups: {emptyGroups}\n" +
                              $"\nTotal: {rows.Count} unused elements";

                if (rows.Count == 0)
                {
                    TaskDialog.Show("STING Explorer", "No unused elements found. The model is clean.");
                    return Result.Succeeded;
                }

                var td = new TaskDialog("STING Unused Elements")
                {
                    MainInstruction = $"{rows.Count} unused elements detected",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select all unused elements in model");
                var result = td.Show();

                if (result == TaskDialogResult.CommandLink1)
                {
                    var ids = rows.Select(r => new ElementId(r.ElementId)).ToList();
                    if (ids.Count > 0)
                        ctx.UIDoc.Selection.SetElementIds(ids);
                }

                StingLog.Info($"UnusedElements: {rows.Count} found (families={unusedFamilies}, views={unusedViews}, rooms={unplacedRooms}, groups={emptyGroups})");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("UnusedElementsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  4. CADImportDetectorCommand — Find all CAD imports/links
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CADImportDetectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var imports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (imports.Count == 0)
                {
                    TaskDialog.Show("STING Explorer", "No CAD imports or links found in the document.");
                    return Result.Succeeded;
                }

                var rows = new List<CADImportRow>();
                foreach (var imp in imports)
                {
                    try
                    {
                        string linkName = "(Unknown)";
                        try
                        {
                            var nameParam = imp.get_Parameter(BuiltInParameter.IMPORT_SYMBOL_NAME);
                            if (nameParam != null && nameParam.HasValue)
                                linkName = nameParam.AsString() ?? "(Unknown)";
                        }
                        catch (Exception) { /* parameter not available */ }

                        bool viewSpecific = false;
                        try
                        {
                            viewSpecific = imp.OwnerViewId != null
                                           && imp.OwnerViewId != ElementId.InvalidElementId;
                        }
                        catch (Exception) { /* owner view lookup failed */ }

                        rows.Add(new CADImportRow
                        {
                            LinkName = linkName,
                            IsLinked = imp.IsLinked,
                            ViewSpecific = viewSpecific,
                            PinnedStatus = imp.Pinned ? "Pinned" : "Unpinned",
                            ElementId = imp.Id.Value
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"CADImportDetector row: {ex.Message}"); }
                }

                rows = rows.OrderBy(r => r.LinkName).ToList();

                var dlg = new StingDataGridDialog("STING CAD Import Detector",
                    $"{rows.Count} CAD imports/links found");
                dlg.AddTextColumn("Link Name", "LinkName", 300);
                dlg.AddCheckColumn("Is Linked", "IsLinked", 70);
                dlg.AddCheckColumn("View Specific", "ViewSpecific", 90);
                dlg.AddTextColumn("Pinned Status", "PinnedStatus", 90);

                dlg.AddActionButton("Select Elements", "Select", true);
                dlg.AddActionButton("Close", "Cancel");
                dlg.SetItems(rows);

                if (dlg.ShowDialog() == true && dlg.ResultAction == "Select")
                {
                    var selectedIds = dlg.SelectedItems
                        .OfType<CADImportRow>()
                        .Select(r => new ElementId(r.ElementId))
                        .ToList();

                    if (selectedIds.Count > 0)
                        ctx.UIDoc.Selection.SetElementIds(selectedIds);
                }

                StingLog.Info($"CADImportDetector: displayed {rows.Count} imports/links");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CADImportDetectorCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  5. InPlaceFamilyDetectorCommand — Find all in-place families
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class InPlaceFamilyDetectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                // Group in-place families by family name and count instances
                var inPlaceInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi =>
                    {
                        try { return fi.Symbol?.Family?.IsInPlace == true; }
                        catch (Exception) { return false; }
                    })
                    .ToList();

                if (inPlaceInstances.Count == 0)
                {
                    TaskDialog.Show("STING Explorer", "No in-place families found. Good practice!");
                    return Result.Succeeded;
                }

                // Group by family to get per-family counts
                var grouped = new Dictionary<long, (string Name, string Category, int Count)>();
                foreach (var fi in inPlaceInstances)
                {
                    try
                    {
                        var fam = fi.Symbol?.Family;
                        if (fam == null) continue;
                        long famId = fam.Id.Value;
                        if (grouped.ContainsKey(famId))
                        {
                            var existing = grouped[famId];
                            grouped[famId] = (existing.Name, existing.Category, existing.Count + 1);
                        }
                        else
                        {
                            grouped[famId] = (fam.Name ?? "(Unnamed)",
                                              ExplorerHelper.SafeCategoryName(fam), 1);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"InPlaceDetector group: {ex.Message}"); }
                }

                var rows = grouped
                    .Select(kvp => new InPlaceFamilyRow
                    {
                        Name = kvp.Value.Name,
                        Category = kvp.Value.Category,
                        ElementCount = kvp.Value.Count,
                        FamilyId = kvp.Key
                    })
                    .OrderBy(r => r.Category)
                    .ThenBy(r => r.Name)
                    .ToList();

                var dlg = new StingDataGridDialog("STING In-Place Family Detector",
                    $"{rows.Count} in-place families ({inPlaceInstances.Count} total instances) — consider converting to loadable families");
                dlg.AddTextColumn("Name", "Name", 250);
                dlg.AddTextColumn("Category", "Category", 180);
                dlg.AddTextColumn("Element Count", "ElementCount", 100);

                dlg.AddActionButton("Select All Instances", "SelectAll", true);
                dlg.AddActionButton("Close", "Cancel");
                dlg.SetItems(rows);

                if (dlg.ShowDialog() == true && dlg.ResultAction == "SelectAll")
                {
                    var allIds = inPlaceInstances.Select(fi => fi.Id).ToList();
                    if (allIds.Count > 0)
                        ctx.UIDoc.Selection.SetElementIds(allIds);
                }

                StingLog.Info($"InPlaceFamilyDetector: {rows.Count} in-place families, {inPlaceInstances.Count} instances");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("InPlaceFamilyDetectorCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
