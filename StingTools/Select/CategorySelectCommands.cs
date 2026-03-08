using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;

namespace StingTools.Select
{
    /// <summary>
    /// Category-based selection commands. Each selects all instances of a specific
    /// BuiltInCategory in the active view, matching the STINGTags v9.6 SELECT tab
    /// category buttons (Lgt, Elc, Mch, Plb, Air, Fur, Dr, Win, Rm, Spr, Pipe, Duct, Cnd, Cbl).
    /// </summary>
    internal static class CategorySelector
    {
        public static Result SelectByCategory(ExternalCommandData commandData,
            BuiltInCategory bic, string label)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("Select", "No document is open.");
                return Result.Failed;
            }
            if (ctx.ActiveView == null)
            {
                TaskDialog.Show("Select", "No active view. Open a view first.");
                return Result.Failed;
            }

            ICollection<ElementId> ids;
            using (var collector = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id))
            {
                ids = collector
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
            }

            if (ids.Count == 0)
            {
                TaskDialog.Show($"Select {label}",
                    $"No {label} found in the active view.");
                return Result.Succeeded;
            }

            ctx.UIDoc.Selection.SetElementIds(ids);
            StingLog.Info($"Selected {ids.Count} {label} elements");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectLightingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectElectricalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectMechanicalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectPlumbingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectAirTerminalsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_DuctTerminal, "Air Terminals");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectFurnitureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Furniture, "Furniture");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectDoorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Doors, "Doors");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectWindowsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Windows, "Windows");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Rooms, "Rooms");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectSprinklersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Sprinklers, "Sprinklers");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectPipesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_PipeCurves, "Pipes");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectDuctsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_DuctCurves, "Ducts");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectConduitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_Conduit, "Conduits");
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectCableTraysCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => CategorySelector.SelectByCategory(cmd, BuiltInCategory.OST_CableTray, "Cable Trays");
    }

    /// <summary>Select ALL taggable elements (all 53 STING categories) in active view.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectAllTaggableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null)
            {
                TaskDialog.Show("Select All", "No active view. Open a view first.");
                return Result.Failed;
            }

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            List<ElementId> ids;
            using (var collector = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id))
            {
                ids = collector
                    .WhereElementIsNotElementType()
                    .Where(e => knownCategories.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id)
                    .ToList();
            }

            if (ids.Count == 0)
            {
                TaskDialog.Show("Select All", "No taggable elements in the active view.");
                return Result.Succeeded;
            }

            ctx.UIDoc.Selection.SetElementIds(ids);
            StingLog.Info($"Selected {ids.Count} taggable elements");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Select elements by choosing from ALL categories present in the active view.
    /// Shows a multi-page category picker listing every category that has instances,
    /// not just the ~14 hardcoded category buttons. Up to 4 categories per page.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectCustomCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null)
            {
                TaskDialog.Show("Select Category", "No active view. Open a view first.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;
            View activeView = ctx.ActiveView;

            // Scan all element categories present in the active view
            var catCounts = new Dictionary<string, (BuiltInCategory bic, int count)>();
            using (var collector = new FilteredElementCollector(doc, activeView.Id))
            {
                foreach (Element e in collector.WhereElementIsNotElementType())
                {
                    Category cat = e.Category;
                    if (cat == null) continue;
                    string name = cat.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    BuiltInCategory bic;
                    try { bic = (BuiltInCategory)cat.Id.Value; }
                    catch { continue; }

                    if (catCounts.ContainsKey(name))
                    {
                        var existing = catCounts[name];
                        catCounts[name] = (existing.bic, existing.count + 1);
                    }
                    else
                    {
                        catCounts[name] = (bic, 1);
                    }
                }
            }

            if (catCounts.Count == 0)
            {
                TaskDialog.Show("Select Category", "No element categories found in the active view.");
                return Result.Succeeded;
            }

            // Sort by count descending so most populated categories appear first
            var sorted = catCounts.OrderByDescending(kv => kv.Value.count).ToList();

            // Show pages of 4 categories each using TaskDialog CommandLinks
            int pageSize = 4;
            int page = 0;
            int totalPages = (sorted.Count + pageSize - 1) / pageSize;

            while (true)
            {
                int start = page * pageSize;
                int end = Math.Min(start + pageSize, sorted.Count);
                var pageItems = sorted.Skip(start).Take(pageSize).ToList();

                TaskDialog dlg = new TaskDialog("Select Category");
                dlg.MainInstruction = $"Choose a category to select (page {page + 1}/{totalPages})";
                dlg.MainContent = $"{sorted.Count} categories with elements in this view.";

                // Add up to 4 command links for this page
                var linkIds = new[] {
                    TaskDialogCommandLinkId.CommandLink1,
                    TaskDialogCommandLinkId.CommandLink2,
                    TaskDialogCommandLinkId.CommandLink3,
                    TaskDialogCommandLinkId.CommandLink4
                };

                for (int i = 0; i < pageItems.Count; i++)
                {
                    var item = pageItems[i];
                    dlg.AddCommandLink(linkIds[i],
                        $"Select {item.Key} ({item.Value.count})",
                        $"{item.Value.count} elements in active view");
                }

                // Navigation buttons
                if (totalPages > 1)
                {
                    dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    dlg.FooterText = page < totalPages - 1
                        ? "Click Cancel then re-run to see other pages, or use Next/Previous below."
                        : "All categories shown.";

                    // Use VerificationText for "Next Page" toggle
                    if (totalPages > 1)
                        dlg.VerificationText = page < totalPages - 1 ? "Show next page" : "Show first page";
                }
                else
                {
                    dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                }

                TaskDialogResult result = dlg.Show();

                // Handle page navigation via verification checkbox
                if (dlg.WasVerificationChecked())
                {
                    page = (page + 1) % totalPages;
                    continue;
                }

                // Handle category selection
                int selectedIndex = -1;
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: selectedIndex = 0; break;
                    case TaskDialogResult.CommandLink2: selectedIndex = 1; break;
                    case TaskDialogResult.CommandLink3: selectedIndex = 2; break;
                    case TaskDialogResult.CommandLink4: selectedIndex = 3; break;
                    default: return Result.Cancelled;
                }

                if (selectedIndex >= 0 && selectedIndex < pageItems.Count)
                {
                    var chosen = pageItems[selectedIndex];
                    return CategorySelector.SelectByCategory(cmd, chosen.Value.bic, chosen.Key);
                }

                return Result.Cancelled;
            }
        }
    }
}
