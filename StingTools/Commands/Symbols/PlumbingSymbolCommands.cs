// StingTools — Plumbing symbol browse-and-place commands (Phase 179, refactored 187)
//
// One IExternalCommand per plumbing fixture / valve / equipment symbol.
// All commands delegate to EquipmentSymbolEngine.ResolveAndPlace, which:
//   1. Resolves the FamilySymbol via 2-tier lookup (loaded family →
//      _BIM_COORD generated path → seed Families/Plumbing/).
//   2. If still not found, prompts the user once to auto-generate the
//      library via SymbolBatchHelper.RunBatch, then retries.
//   3. Calls UIDocument.PickPoint in a loop until the user presses Escape.
//   4. Emits an inline status message via EquipmentSymbolEngine.StatusUpdated
//      so StingPlumbingPanel surfaces the result in its status bar instead
//      of popping a modal TaskDialog.
//
// Data source: StingTools/Data/Symbols/STING_PLUMBING_SYMBOLS.json
// Symbol ids must match the "id" field in that JSON.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Symbols.PlumbingSymbolCommands
{
    internal static class Const
    {
        internal const string JsonFile = "STING_PLUMBING_SYMBOLS.json";
    }

    internal static class PlumbingSymbolBase
    {
        internal static Result Run(ExternalCommandData d, ref string msg,
            string symbolId, string title, string label, string fallbackId = null)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(d);
                int n = EquipmentSymbolEngine.ResolveAndPlace(
                    ctx.Doc, ctx.UIDoc, symbolId, Const.JsonFile, title, label);
                if (n < 0 && !string.IsNullOrEmpty(fallbackId))
                    n = EquipmentSymbolEngine.ResolveAndPlace(
                        ctx.Doc, ctx.UIDoc, fallbackId, Const.JsonFile, title, label);
                if (n < 0) { msg = $"{title}: family '{symbolId}' not available."; return Result.Failed; }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(title, ex); msg = ex.Message; return Result.Failed; }
        }
    }

    // ── Sanitary fixtures ─────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceWCCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_WC_WALL", "Place WC", "WC symbol",
                fallbackId: "PLM_WC_CLOSE_COUPLED");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceUrinalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_URINAL_WALL", "Place Urinal", "urinal symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceBidetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_BIDET", "Place Bidet", "bidet symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceWHBCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_WHB", "Place WHB", "wash-hand basin symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceVanityBasinCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_VANITY_BASIN", "Place Vanity Basin", "vanity basin symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceBathCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_BATH", "Place Bath", "bath symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceShowerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_SHOWER_TRAY", "Place Shower", "shower tray symbol");
    }

    // ── Sinks ─────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceSingleSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_SINK_SINGLE", "Place Single Sink", "single sink symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceDoubleSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_SINK_DOUBLE", "Place Double Sink", "double sink symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceCleanersSinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_CLEANERS_SINK", "Place Cleaner's Sink", "cleaner's sink symbol");
    }

    // ── Drainage points ───────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceFloorDrainRoundCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_FLOOR_DRAIN_RND", "Place Floor Drain", "floor drain symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceFloorDrainSquareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_FLOOR_DRAIN_SQ", "Place Floor Drain", "floor drain symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceGulleyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_GULLEY", "Place Yard Gulley", "gulley symbol");
    }

    // ── Valves & accessories ──────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceGateValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_GATE_VALVE", "Place Gate Valve", "gate valve symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceGlobeValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_GLOBE_VALVE", "Place Globe Valve", "globe valve symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceBallValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_BALL_VALVE", "Place Ball Valve", "ball valve symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceButterflyValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_BUTTERFLY_VALVE", "Place Butterfly Valve", "butterfly valve symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceCheckValveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_CHECK_VALVE", "Place Check Valve", "check valve symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlacePRVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_PRESSURE_REDUCING", "Place PRV", "PRV symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceStrainerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_STRAINER", "Place Y-Strainer", "strainer symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceFlexConnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_FLEXIBLE_CONN", "Place Flex Connector", "flexible connector symbol");
    }

    // ── Equipment ─────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceHWCDirectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_HWC_DIRECT", "Place HWC (Direct)", "HWC symbol");
    }

    [Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
    public class PlaceHWCIndirectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string msg, ElementSet els)
            => PlumbingSymbolBase.Run(d, ref msg, "PLM_HWC_INDIRECT", "Place HWC (Indirect)", "HWC symbol");
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
                var ctx = ParameterHelpers.GetContext(d);
                var items = EquipmentSymbolEngine.LoadDisplayList(Const.JsonFile, "Plumbing");
                if (items.Count == 0)
                {
                    EquipmentSymbolEngine.EmitStatus(
                        "Browse Plumbing Symbols: no symbols in STING_PLUMBING_SYMBOLS.json.");
                    return Result.Cancelled;
                }
                string picked = StingListPicker.Show(
                    "Browse & Place Plumbing Symbols",
                    "Search by name or subcategory (Sanitary · Drainage · Valve · Equipment) — Escape after each placement to pick another",
                    items);
                if (string.IsNullOrEmpty(picked)) return Result.Cancelled;

                string id = EquipmentSymbolEngine.ExtractId(picked);
                int n = EquipmentSymbolEngine.ResolveAndPlace(
                    ctx.Doc, ctx.UIDoc, id, Const.JsonFile,
                    "Place " + picked, picked);
                if (n < 0) { msg = $"Symbol family for '{id}' not available."; return Result.Failed; }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error(nameof(BrowsePlumbingSymbolsCommand), ex); msg = ex.Message; return Result.Failed; }
        }
    }
}
