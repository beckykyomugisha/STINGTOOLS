// StingTools — Plumbing symbol browse-and-place commands (Phase 179)
//
// One IExternalCommand per plumbing fixture / valve / equipment symbol.
// All commands delegate to EquipmentSymbolEngine which:
//   1. Resolves the FamilySymbol via 2-tier lookup (loaded family → _BIM_COORD generated path)
//   2. Calls UIDocument.PickPoint in a loop until the user presses Escape
//   3. Places NewFamilyInstance at each picked point
//
// Data source: StingTools/Data/Symbols/STING_PLUMBING_SYMBOLS.json
// Symbol ids must match the "id" field in that JSON.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Symbols.PlumbingSymbolCommands
{
    internal static class Const
    {
        internal const string JsonFile = "STING_PLUMBING_SYMBOLS.json";
    }

    // ── Sanitary fixtures ─────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceWCCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_WC_WALL", Const.JsonFile)
                      ?? EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_WC_CLOSE_COUPLED", Const.JsonFile);
                if (fs == null) { msg = "WC symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_WC", "Place WC");
                TaskDialog.Show("Place WC", $"Placed {n} WC symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceWCCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceUrinalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_URINAL_WALL", Const.JsonFile);
                if (fs == null) { msg = "Urinal symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_URINAL_WALL", "Place Urinal");
                TaskDialog.Show("Place Urinal", $"Placed {n} urinal symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceUrinalCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceBidetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_BIDET", Const.JsonFile);
                if (fs == null) { msg = "Bidet symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_BIDET", "Place Bidet");
                TaskDialog.Show("Place Bidet", $"Placed {n} bidet symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceBidetCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceWHBCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_WHB", Const.JsonFile);
                if (fs == null) { msg = "Wash-hand basin symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_WHB", "Place Wash-Hand Basin");
                TaskDialog.Show("Place WHB", $"Placed {n} wash-hand basin symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceWHBCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceVanityBasinCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_VANITY_BASIN", Const.JsonFile);
                if (fs == null) { msg = "Vanity basin symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_VANITY_BASIN", "Place Vanity Basin");
                TaskDialog.Show("Place Vanity Basin", $"Placed {n} vanity basin symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceVanityBasinCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceBathCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_BATH", Const.JsonFile);
                if (fs == null) { msg = "Bath symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_BATH", "Place Bath");
                TaskDialog.Show("Place Bath", $"Placed {n} bath symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceBathCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceShowerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_SHOWER_TRAY", Const.JsonFile);
                if (fs == null) { msg = "Shower tray symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_SHOWER_TRAY", "Place Shower Tray");
                TaskDialog.Show("Place Shower", $"Placed {n} shower tray symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceShowerCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Sinks ─────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSingleSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_SINK_SINGLE", Const.JsonFile);
                if (fs == null) { msg = "Single sink symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_SINK_SINGLE", "Place Single Sink");
                TaskDialog.Show("Place Single Sink", $"Placed {n} single sink symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceSingleSinkCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceDoubleSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_SINK_DOUBLE", Const.JsonFile);
                if (fs == null) { msg = "Double sink symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_SINK_DOUBLE", "Place Double Sink");
                TaskDialog.Show("Place Double Sink", $"Placed {n} double sink symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceDoubleSinkCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceCleanersSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_CLEANERS_SINK", Const.JsonFile);
                if (fs == null) { msg = "Cleaner's sink symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_CLEANERS_SINK", "Place Cleaner's Sink");
                TaskDialog.Show("Place Cleaner's Sink", $"Placed {n} cleaner's sink symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceCleanersSinkCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Drainage points ───────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFloorDrainRoundCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_FLOOR_DRAIN_RND", Const.JsonFile);
                if (fs == null) { msg = "Floor drain (round) symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_FLOOR_DRAIN_RND", "Place Floor Drain (Round)");
                TaskDialog.Show("Place Floor Drain", $"Placed {n} floor drain symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceFloorDrainRoundCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFloorDrainSquareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_FLOOR_DRAIN_SQ", Const.JsonFile);
                if (fs == null) { msg = "Floor drain (square) symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_FLOOR_DRAIN_SQ", "Place Floor Drain (Square)");
                TaskDialog.Show("Place Floor Drain", $"Placed {n} floor drain symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceFloorDrainSquareCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceGulleyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_GULLEY", Const.JsonFile);
                if (fs == null) { msg = "Gulley symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_GULLEY", "Place Yard Gulley");
                TaskDialog.Show("Place Gulley", $"Placed {n} gulley symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceGulleyCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Valves & accessories ──────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceGateValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_GATE_VALVE", Const.JsonFile);
                if (fs == null) { msg = "Gate valve symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_GATE_VALVE", "Place Gate Valve");
                TaskDialog.Show("Place Gate Valve", $"Placed {n} gate valve symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceGateValveCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceGlobeValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_GLOBE_VALVE", Const.JsonFile);
                if (fs == null) { msg = "Globe valve symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_GLOBE_VALVE", "Place Globe Valve");
                TaskDialog.Show("Place Globe Valve", $"Placed {n} globe valve symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceGlobeValveCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceBallValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_BALL_VALVE", Const.JsonFile);
                if (fs == null) { msg = "Ball valve symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_BALL_VALVE", "Place Ball Valve");
                TaskDialog.Show("Place Ball Valve", $"Placed {n} ball valve symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceBallValveCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceButterflyValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_BUTTERFLY_VALVE", Const.JsonFile);
                if (fs == null) { msg = "Butterfly valve symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_BUTTERFLY_VALVE", "Place Butterfly Valve");
                TaskDialog.Show("Place Butterfly Valve", $"Placed {n} butterfly valve symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceButterflyValveCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceCheckValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_CHECK_VALVE", Const.JsonFile);
                if (fs == null) { msg = "Check valve symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_CHECK_VALVE", "Place Check Valve");
                TaskDialog.Show("Place Check Valve", $"Placed {n} check valve symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceCheckValveCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacePRVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_PRESSURE_REDUCING", Const.JsonFile);
                if (fs == null) { msg = "PRV symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_PRESSURE_REDUCING", "Place PRV");
                TaskDialog.Show("Place PRV", $"Placed {n} PRV symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlacePRVCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceStrainerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_STRAINER", Const.JsonFile);
                if (fs == null) { msg = "Y-strainer symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_STRAINER", "Place Y-Strainer");
                TaskDialog.Show("Place Y-Strainer", $"Placed {n} strainer symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceStrainerCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFlexConnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_FLEXIBLE_CONN", Const.JsonFile);
                if (fs == null) { msg = "Flexible connector symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_FLEXIBLE_CONN", "Place Flexible Connector");
                TaskDialog.Show("Place Flex Connector", $"Placed {n} flexible connector symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceFlexConnCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Equipment ─────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHWCDirectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_HWC_DIRECT", Const.JsonFile);
                if (fs == null) { msg = "Hot water cylinder (direct) symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_HWC_DIRECT", "Place HWC (Direct)");
                TaskDialog.Show("Place HWC (Direct)", $"Placed {n} HWC symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceHWCDirectCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHWCIndirectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, "PLM_HWC_INDIRECT", Const.JsonFile);
                if (fs == null) { msg = "Hot water cylinder (indirect) symbol family not found."; return Result.Failed; }
                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, "PLM_HWC_INDIRECT", "Place HWC (Indirect)");
                TaskDialog.Show("Place HWC (Indirect)", $"Placed {n} HWC symbol{(n == 1 ? "" : "s")}.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(PlaceHWCIndirectCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Browse all ────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BrowsePlumbingSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
        {
            try
            {
                var ctx = new CommandExecutionContext(d);
                var items = EquipmentSymbolEngine.LoadDisplayList(Const.JsonFile, "Plumbing");
                if (items.Count == 0)
                {
                    TaskDialog.Show("Browse Plumbing Symbols", "No symbols found in STING_PLUMBING_SYMBOLS.json.");
                    return Result.Cancelled;
                }
                string picked = StingListPicker.Show(
                    "Browse & Place Plumbing Symbols",
                    "Search by name or subcategory (Sanitary · Drainage · Valve · Equipment) — press Escape after each placement to pick another",
                    items);
                if (string.IsNullOrEmpty(picked)) return Result.Cancelled;

                string id = EquipmentSymbolEngine.ExtractId(picked);
                var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, Const.JsonFile);
                if (fs == null) { msg = $"Symbol family for '{id}' not found."; return Result.Failed; }

                int n = EquipmentSymbolEngine.PlaceAtPickPoints(ctx.Doc, ctx.UIDoc, fs, id, "Place " + picked);
                TaskDialog.Show("Browse & Place", $"Placed {n} instance{(n == 1 ? "" : "s")} of '{picked}'.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(BrowsePlumbingSymbolsCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }
}
