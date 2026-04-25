// StingTools v4 MVP — Fabrication export commands.
//
// Three thin IExternalCommand wrappers that re-emit the CSV sidecars
// produced by the per-discipline fabricators. All three now route
// through FabricationScope so the Fabrication tab's scope radios
// (with smart fallback: empty selection → active view) apply
// consistently. The workspace dialog exposes the same paths via
// FabricationActionRunner.Run* when the user prefers an inline
// preview before export.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCutListCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            try
            {
                var scope = FabricationScope.Resolve(ctx.Doc, ctx.UIDoc);
                // Cut list is pipe-only — filter down after the shared resolver.
                var ids = FabricationScope.FilterByRulesAndCategoryMask(scope, null)
                    .Where(id => ctx.Doc.GetElement(id)?.Category?.Id.Value == (long)(int)BuiltInCategory.OST_PipeCurves)
                    .ToList();
                if (ids.Count == 0)
                {
                    TaskDialog.Show("STING v4 — Export Cut List",
                        $"Scope '{scope.ScopeLabel}' contains no pipe curves.");
                    return Result.Cancelled;
                }
                FabricationActionRunner.ExportCutList(ctx.UIDoc, ids);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportCutListCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportIsometricsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            try
            {
                // Isometrics indexes existing SP-... sheets regardless of
                // the scope radios — the radios control model-element
                // scope, not sheet scope.
                FabricationActionRunner.ExportIsometrics(ctx.UIDoc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportIsometricsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportWeldMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            try
            {
                var scope = FabricationScope.Resolve(ctx.Doc, ctx.UIDoc);
                var ids = FabricationScope.FilterByRulesAndCategoryMask(scope, null)
                    .Where(id =>
                    {
                        var bic = ctx.Doc.GetElement(id)?.Category?.Id.Value;
                        return bic == (long)(int)BuiltInCategory.OST_PipeCurves
                            || bic == (long)(int)BuiltInCategory.OST_PipeFitting;
                    })
                    .ToList();
                if (ids.Count == 0)
                {
                    TaskDialog.Show("STING v4 — Export Weld Map",
                        $"Scope '{scope.ScopeLabel}' contains no pipes / fittings.");
                    return Result.Cancelled;
                }
                FabricationActionRunner.ExportWeldMap(ctx.UIDoc, ids);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportWeldMapCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
