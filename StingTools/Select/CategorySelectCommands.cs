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
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            Document doc = uidoc.Document;

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (ids.Count == 0)
            {
                TaskDialog.Show($"Select {label}",
                    $"No {label} found in the active view.");
                return Result.Succeeded;
            }

            uidoc.Selection.SetElementIds(ids);
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
            UIDocument uidoc = ParameterHelpers.GetApp(cmd).ActiveUIDocument;
            Document doc = uidoc.Document;

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => knownCategories.Contains(ParameterHelpers.GetCategoryName(e)))
                .Select(e => e.Id)
                .ToList();

            if (ids.Count == 0)
            {
                TaskDialog.Show("Select All", "No taggable elements in the active view.");
                return Result.Succeeded;
            }

            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"Selected {ids.Count} taggable elements");
            return Result.Succeeded;
        }
    }
}
