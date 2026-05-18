// StingTools v4 — PlaceMepDetailSymbolsCommand
// Places Detail Item family instances from the MEP symbol catalogue
// (STING_MEP_SYMBOLS_INDEX.csv) onto a view's MEP elements.
//
// DISTINCT FROM PlaceSymbolsInViewCommand (SymbolStandardCommands.cs):
//   PlaceSymbolsInViewCommand  → IndependentTag annotations, concept-based,
//                                uses SymbolOverlayManager + SymbolConceptRegistry.
//   PlaceMepDetailSymbolsCommand → FamilyInstance Detail Items, CSV-catalogue-based,
//                                  uses MepSymbolEngine; scale-aware + colour-aware.
// Do NOT merge these paths — they serve different output requirements.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Symbols;
using StingTools.UI;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceMepDetailSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;
            var view  = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("STING — Place MEP Detail Symbols",
                    "No active view. Open a floor plan, section, or drafting view first.");
                return Result.Cancelled;
            }

            // Collect scope: selected MEP elements, or all MEP elements in view.
            var selIds = uidoc.Selection?.GetElementIds()?.ToList() ?? new List<ElementId>();
            List<ElementId> ids;
            string scopeLabel;

            if (selIds.Count > 0)
            {
                ids = selIds.Where(id =>
                {
                    var el = doc.GetElement(id);
                    return el?.Category != null && IsMepCategory((int)el.Category.Id.Value);
                }).ToList();
                scopeLabel = $"selection ({ids.Count} MEP elements)";
            }
            else
            {
                ids = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(new ElementMulticategoryFilter(MepCats))
                    .WhereElementIsNotElementType()
                    .Select(e => e.Id)
                    .ToList();
                scopeLabel = $"active view ({ids.Count} MEP elements)";
            }

            if (ids.Count == 0)
            {
                TaskDialog.Show("STING — Place MEP Detail Symbols",
                    "No MEP elements found in the current scope.\n\n" +
                    "Select pipes, ducts, conduits, cable trays, or their fittings/accessories, " +
                    "or open a view that contains them, and try again.");
                return Result.Cancelled;
            }

            // Resolve placement options from a quick picker dialog.
            var opts = ShowOptionsDialog();
            if (opts == null) return Result.Cancelled;

            StingLog.Info($"PlaceMepDetailSymbols: scope={scopeLabel}, standard={opts.Standard}, " +
                          $"colorScheme={opts.ColorScheme}, mode={opts.PlacementMode}");

            MepSymbolPlacementResult result;
            try
            {
                result = MepSymbolEngine.PlaceSymbols(doc, view, ids, opts);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlaceMepDetailSymbolsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(result, scopeLabel);
            return Result.Succeeded;
        }

        private static readonly BuiltInCategory[] MepCats =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_FireAlarmDevices,
        };

        private static bool IsMepCategory(int bicValue)
        {
            foreach (var bic in MepCats)
                if ((int)bic == bicValue) return true;
            return false;
        }

        /// <summary>
        /// Shows a compact TaskDialog-based options picker.
        /// Returns null when the user cancels.
        /// In a full implementation this would be a WPF dialog with
        /// dropdowns for Standard / ColorScheme / PlacementMode.
        /// </summary>
        private static MepSymbolPlacementOptions ShowOptionsDialog()
        {
            // Quick two-step picker: standard then color scheme.
            // Step 1 — symbol standard.
            var tdStd = new TaskDialog("STING — Place MEP Detail Symbols")
            {
                MainInstruction = "Choose symbol standard",
                MainContent =
                    "CIBSE  — CIBSE Guide symbols (UK MEP plans)\n" +
                    "ISO14617 — ISO 14617 general process symbols\n" +
                    "IEC60617 — IEC 60617 electrical SLD symbols\n" +
                    "BS1710   — BS 1710 pipe identification (colour only)\n" +
                    "Corporate — STING house standard (default)",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            tdStd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "CIBSE");
            tdStd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "IEC 60617 (SLD)");
            tdStd.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "ISO 14617");
            tdStd.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Corporate (default)");

            var resStd = tdStd.Show();
            if (resStd == TaskDialogResult.Cancel) return null;

            string standard;
            switch (resStd)
            {
                case TaskDialogResult.CommandLink1: standard = "CIBSE";    break;
                case TaskDialogResult.CommandLink2: standard = "IEC60617"; break;
                case TaskDialogResult.CommandLink3: standard = "ISO14617"; break;
                default:                             standard = "Corporate"; break;
            }

            // Step 2 — colour scheme.
            var tdCol = new TaskDialog("STING — Place MEP Detail Symbols")
            {
                MainInstruction = "Choose colour scheme",
                MainContent =
                    "Corporate — STING neutral grey (print-friendly)\n" +
                    "BS 1710   — BS 1710:2014 pipe identification colours\n" +
                    "CIBSE     — CIBSE discipline colours\n" +
                    "ASHRAE    — ASHRAE air-side colours\n" +
                    "IEC 60617 — Monochrome (SLD standard)\n" +
                    "Monochrome — All black",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            tdCol.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Corporate");
            tdCol.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "BS 1710");
            tdCol.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CIBSE");
            tdCol.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Monochrome");

            var resCol = tdCol.Show();
            if (resCol == TaskDialogResult.Cancel) return null;

            SymbolColorScheme colorScheme;
            switch (resCol)
            {
                case TaskDialogResult.CommandLink2: colorScheme = SymbolColorScheme.BS1710;      break;
                case TaskDialogResult.CommandLink3: colorScheme = SymbolColorScheme.CIBSE;       break;
                case TaskDialogResult.CommandLink4: colorScheme = SymbolColorScheme.Monochrome;  break;
                default:                             colorScheme = SymbolColorScheme.Corporate;   break;
            }

            return new MepSymbolPlacementOptions
            {
                Standard       = standard,
                ColorScheme    = colorScheme,
                PlacementMode  = MepSymbolPlacementMode.Replace,
                Overwrite      = true,
            };
        }

        private static void ShowResult(MepSymbolPlacementResult result, string scopeLabel)
        {
            foreach (var w in result.Warnings)
                StingLog.Warn($"PlaceMepDetailSymbols: {w}");

            var td = new TaskDialog("STING — MEP Detail Symbol Placement Complete")
            {
                MainInstruction = result.Succeeded
                    ? $"Placed {result.SymbolsPlaced} symbols"
                    : "Placement completed with errors",
                MainContent =
                    $"Scope      : {scopeLabel}\n" +
                    $"Placed     : {result.SymbolsPlaced}\n" +
                    $"Skipped    : {result.Skipped}\n" +
                    $"Failed     : {result.Failed}\n" +
                    $"Warnings   : {result.Warnings?.Count ?? 0}\n\n" +
                    (result.Warnings?.Count > 0
                        ? "First warning: " + result.Warnings[0] + "\n(See StingTools.log for full list)"
                        : "No warnings."),
                CommonButtons = TaskDialogCommonButtons.Ok,
            };
            td.Show();
        }
    }

    /// <summary>
    /// Project-wide variant — places MEP detail symbols on all views that
    /// match the current symbol standard, skipping 3D and schedule views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceMepDetailSymbolsProjectWideCommand : IExternalCommand
    {
        private static readonly ViewType[] SupportedViewTypes =
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.Elevation,
            ViewType.Section,
            ViewType.Detail,
            ViewType.DraftingView,
            ViewType.EngineeringPlan,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var td = new TaskDialog("STING — Place MEP Symbols (Project-Wide)")
            {
                MainInstruction = "Place MEP detail symbols on all eligible views?",
                MainContent =
                    "This will iterate every floor plan, section, elevation, and detail view " +
                    "in the project and place MEP catalogue symbols on the MEP elements visible " +
                    "in each view.\n\n" +
                    "Large projects may take several minutes.\n\n" +
                    "Standard: Corporate  |  Colour: CIBSE\n" +
                    "Mode: Replace (existing symbols removed and re-placed).",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton  = TaskDialogResult.Cancel,
            };
            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && SupportedViewTypes.Contains(v.ViewType))
                .ToList();

            var opts = new MepSymbolPlacementOptions
            {
                Standard      = "Corporate",
                ColorScheme   = SymbolColorScheme.CIBSE,
                PlacementMode = MepSymbolPlacementMode.Replace,
                Overwrite     = true,
            };

            int totalPlaced  = 0;
            int totalFailed  = 0;
            int totalSkipped = 0;
            var allWarnings  = new List<string>();

            var progress = StingProgressDialog.Show("Placing MEP symbols (project-wide)", views.Count);
            try
            {
                for (int i = 0; i < views.Count; i++)
                {
                    if (progress.IsCancelled) break;
                    var v = views[i];
                    progress.Increment($"{v.Name}");

                    var ids = new FilteredElementCollector(doc, v.Id)
                        .WherePasses(new ElementMulticategoryFilter(MepCats))
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id)
                        .ToList();
                    if (ids.Count == 0) { totalSkipped++; continue; }

                    try
                    {
                        var res = MepSymbolEngine.PlaceSymbols(doc, v, ids, opts);
                        totalPlaced  += res.SymbolsPlaced;
                        totalFailed  += res.Failed;
                        if (res.Warnings != null) allWarnings.AddRange(res.Warnings);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PlaceMepDetailSymbolsProjectWide: view '{v.Name}': {ex.Message}");
                        totalFailed++;
                    }
                }
            }
            finally
            {
                progress.Close();
            }

            foreach (var w in allWarnings)
                StingLog.Warn($"PlaceMepDetailSymbolsProjectWide: {w}");

            TaskDialog.Show("STING — Project-Wide Symbol Placement",
                $"Views processed : {views.Count - totalSkipped}/{views.Count}\n" +
                $"Symbols placed  : {totalPlaced}\n" +
                $"Failed          : {totalFailed}\n" +
                $"Warnings        : {allWarnings.Count}\n\n" +
                (allWarnings.Count > 0
                    ? "First warning: " + allWarnings[0] + "\n(Full list in StingTools.log)"
                    : "No warnings."));

            return Result.Succeeded;
        }

        private static readonly BuiltInCategory[] MepCats =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_FireAlarmDevices,
        };
    }

    /// <summary>
    /// Removes all MEP detail symbols placed by MepSymbolEngine from the
    /// active view (identified by STING_PLACED_BY_SYMBOL_PLACER_BOOL stamp).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearMepDetailSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null)
            {
                message = "No active view.";
                return Result.Failed;
            }

            var toDelete = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi =>
                {
                    var p = fi.LookupParameter(MepSymbolEngine.STAMP_BOOL);
                    return p != null && p.AsInteger() == 1;
                })
                .Select(fi => fi.Id)
                .ToList();

            if (toDelete.Count == 0)
            {
                TaskDialog.Show("STING — Clear MEP Detail Symbols",
                    "No STING MEP detail symbols found in the active view.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("STING — Clear MEP Detail Symbols")
            {
                MainInstruction = $"Delete {toDelete.Count} MEP detail symbols from this view?",
                CommonButtons   = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton   = TaskDialogResult.Cancel,
            };
            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            using (var t = new Transaction(doc, "STING Clear MEP Detail Symbols"))
            {
                t.Start();
                doc.Delete(toDelete);
                t.Commit();
            }

            StingLog.Info($"ClearMepDetailSymbols: deleted {toDelete.Count} symbols from view '{view.Name}'.");
            TaskDialog.Show("STING — Clear MEP Detail Symbols",
                $"Deleted {toDelete.Count} symbol(s) from the active view.");
            return Result.Succeeded;
        }
    }
}
