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
    /// BuiltInCategory in the active view or whole project (via SelectionScopeHelper).
    /// Covers Lgt, Elc, Mch, Plb, Air, Fur, Dr, Win, Rm, Spr, Pipe, Duct, Cnd, Cbl.
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

            var collector = SelectionScopeHelper.GetCollector(ctx.Doc, ctx.ActiveView);
            ICollection<ElementId> ids = collector
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (ids.Count == 0)
            {
                string scope = SelectionScopeHelper.IsProjectScope ? "project" : "active view";
                TaskDialog.Show($"Select {label}",
                    $"No {label} found in the {scope}.");
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

            // Use category NAME matching (not Settings.Categories.get_Item which requires
            // exact case and locale-sensitive names that may not match DiscMap keys)
            var knownCatNames = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
            List<ElementId> ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && knownCatNames.Contains(e.Category.Name))
                .Select(e => e.Id)
                .ToList();

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
    /// Shows a scrollable WPF list dialog with search filtering — no pagination needed.
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
            foreach (Element e in new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType())
            {
                Category cat = e.Category;
                if (cat == null) continue;
                string name = cat.Name;
                if (string.IsNullOrEmpty(name)) continue;
                BuiltInCategory bic;
                try { bic = (BuiltInCategory)cat.Id.Value; }
                catch { continue; }

                if (catCounts.TryGetValue(name, out var existing))
                    catCounts[name] = (existing.bic, existing.count + 1);
                else
                    catCounts[name] = (bic, 1);
            }

            if (catCounts.Count == 0)
            {
                TaskDialog.Show("Select Category", "No element categories found in the active view.");
                return Result.Succeeded;
            }

            var sorted = catCounts.OrderByDescending(kv => kv.Value.count).ToList();

            // Show WPF list picker dialog
            var picked = StingListPicker.Show(
                "Select Category",
                $"{sorted.Count} categories with elements in view",
                sorted.Select(kv => new StingListPicker.ListItem
                {
                    Label = kv.Key,
                    Detail = $"{kv.Value.count} elements",
                    Tag = kv.Key
                }).ToList(),
                allowMultiSelect: true);

            if (picked == null || picked.Count == 0)
                return Result.Cancelled;

            // Select elements from all chosen categories
            var selectedNames = new HashSet<string>(picked.Select(p => (string)p.Tag));
            var ids = new List<ElementId>();
            foreach (var kv in sorted)
            {
                if (!selectedNames.Contains(kv.Key)) continue;
                var catIds = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(kv.Value.bic)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
                ids.AddRange(catIds);
            }

            ctx.UIDoc.Selection.SetElementIds(ids);
            string catNames = string.Join(", ", picked.Select(p => p.Label));
            TaskDialog.Show("Select Category",
                $"Selected {ids.Count} elements from: {catNames}");
            return Result.Succeeded;
        }
    }
}
