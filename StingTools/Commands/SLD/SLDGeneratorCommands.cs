// StingTools — SLD generator commands (Phase 175 + Phase 179 enhancements)
//
// Phase 179:
//  - GenerateSLDCommand / UpdateSLDCommand read layout + annotation options
//    from StingElectricalCommandHandler static fields (set by UI panel sliders
//    and checkboxes) and pass them through to SLDGenerator.
//  - SLDSyncToggleCommand no longer tells users to restart Revit — the updater
//    re-reads project_config.json on every Execute() call; no restart needed.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.SLD;
using StingTools.Core.Symbols;

namespace StingTools.Commands.SLD
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateSLDCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            string std = SymbolStandardResolver.ResolveStandardForDiscipline(ctx.Doc, "Electrical");
            var layoutOpts = StingTools.UI.StingElectricalCommandHandler.CurrentSLDLayoutOptions;
            var annotOpts  = StingTools.UI.StingElectricalCommandHandler.CurrentSLDAnnotationOptions;

            var result = SLDGenerator.GenerateSLD(ctx.Doc, std,
                layoutOpts: layoutOpts, annotOpts: annotOpts);

            if (!result.Success)
            {
                TaskDialog.Show("STING", $"SLD generation failed: {result.Warning}");
                return Result.Failed;
            }
            try { ctx.UIDoc.ActiveView = result.SLDView; }
            catch (Exception ex) { StingLog.Warn($"Activate SLD view: {ex.Message}"); }

            TaskDialog.Show("STING - SLD",
                $"Generated SLD '{result.SLDView.Name}'.\nSymbols placed: {result.SymbolsPlaced}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateSLDWithOptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var pick = StingTools.Select.StingListPicker.Show(
                "SLD Standard", "Pick the standard for the generated SLD.",
                SymbolStandardRegistry.ListStandards().ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;

            string viewName = $"STING - SLD - {pick} - {DateTime.Now:yyyyMMdd-HHmm}";
            var layoutOpts = StingTools.UI.StingElectricalCommandHandler.CurrentSLDLayoutOptions;
            var annotOpts  = StingTools.UI.StingElectricalCommandHandler.CurrentSLDAnnotationOptions;

            var result = SLDGenerator.GenerateSLD(ctx.Doc, pick, viewName,
                layoutOpts: layoutOpts, annotOpts: annotOpts);

            TaskDialog.Show("STING - SLD",
                result.Success
                    ? $"Generated '{result.SLDView.Name}' ({result.SymbolsPlaced} symbols)."
                    : $"Generation failed: {result.Warning}");
            return result.Success ? Result.Succeeded : Result.Failed;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UpdateSLDCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var slds = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Where(v => v.Name?.StartsWith("STING - SLD",
                    StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (slds.Count == 0)
            {
                TaskDialog.Show("STING", "No STING SLD views found. Run Generate first.");
                return Result.Cancelled;
            }

            var layoutOpts = StingTools.UI.StingElectricalCommandHandler.CurrentSLDLayoutOptions;
            var annotOpts  = StingTools.UI.StingElectricalCommandHandler.CurrentSLDAnnotationOptions;

            foreach (var v in slds)
                SLDGenerator.UpdateSLD(ctx.Doc, v, ElementId.InvalidElementId,
                    layoutOpts, annotOpts);

            TaskDialog.Show("STING", $"Refreshed {slds.Count} SLD view(s).");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLDSyncToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            if (string.IsNullOrEmpty(ctx.Doc.PathName))
            {
                TaskDialog.Show("STING", "Project must be saved first.");
                return Result.Failed;
            }

            string p = Path.Combine(Path.GetDirectoryName(ctx.Doc.PathName), "project_config.json");
            try
            {
                JObject root = File.Exists(p)
                    ? JObject.Parse(File.ReadAllText(p))
                    : new JObject();
                bool current = (bool)(root["sld_sync_enabled"] ?? false);
                bool next = !current;
                root["sld_sync_enabled"] = next;
                File.WriteAllText(p, root.ToString());
                // The SLDSyncUpdater re-reads project_config.json on every Execute()
                // call — no Revit restart is required for the change to take effect.
                TaskDialog.Show("STING",
                    $"SLD live sync {(next ? "enabled" : "disabled")}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SLDSyncToggle", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLDValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var roots = SLDCircuitTraverser.BuildHierarchyAll(ctx.Doc);
            int circuits = 0;
            void Count(SLDNode n) { circuits++; foreach (var c in n.Children) Count(c); }
            roots?.ForEach(Count);

            int sldSymbols = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Count(fi => !string.IsNullOrEmpty(
                    fi.LookupParameter("STING_SLD_ELEMENT_ID")?.AsString()));

            int rootCount = roots?.Count ?? 0;
            TaskDialog.Show("STING - SLD Validate",
                $"Distribution roots found   : {rootCount}\n"
              + $"Electrical circuits in model: {circuits}\n"
              + $"SLD symbols placed          : {sldSymbols}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Walk every STING SLD drafting view and stamp STING_SYMBOL_LABEL_ID on
    /// symbols that don't have one, so the fast update path can find their
    /// TextNote without a spatial scan. Idempotent — already-stamped symbols
    /// are skipped. The system self-heals on every modification so this command
    /// is most useful for proactive migration of pre-Phase-175 projects.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateSLDLabelIdsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            int viewsScanned = 0, alreadyStamped = 0, stamped = 0, unmatched = 0;
            try
            {
                var slds = new FilteredElementCollector(ctx.Doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => v.Name?.StartsWith("STING - SLD",
                        StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                if (slds.Count == 0)
                {
                    TaskDialog.Show("STING", "No STING SLD views found.");
                    return Result.Succeeded;
                }

                using (var tx = new Transaction(ctx.Doc, "STING Migrate SLD Label IDs"))
                {
                    tx.Start();
                    foreach (var v in slds)
                    {
                        viewsScanned++;
                        var symbols = new FilteredElementCollector(ctx.Doc, v.Id)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .Where(fi => !string.IsNullOrEmpty(
                                fi.LookupParameter("STING_SYMBOL_ID")?.AsString()))
                            .ToList();

                        var notes = new FilteredElementCollector(ctx.Doc, v.Id)
                            .OfClass(typeof(TextNote))
                            .Cast<TextNote>()
                            .Select(n =>
                            {
                                XYZ p = null;
                                try { p = n.Coord; }
                                catch (Exception ex) { StingLog.Warn($"MigrateLabel coord: {ex.Message}"); }
                                return new { Note = n, Pos = p };
                            })
                            .Where(x => x.Pos != null)
                            .ToList();

                        foreach (var sym in symbols)
                        {
                            try
                            {
                                var p = sym.LookupParameter("STING_SYMBOL_LABEL_ID");
                                if (p == null) continue;
                                if (!string.IsNullOrEmpty(p.AsString())) { alreadyStamped++; continue; }

                                XYZ symPos = (sym.Location as LocationPoint)?.Point;
                                if (symPos == null) continue;

                                double bestD = 0.4;
                                TextNote best = null;
                                foreach (var x in notes)
                                {
                                    double d = symPos.DistanceTo(x.Pos);
                                    if (d < bestD) { bestD = d; best = x.Note; }
                                }
                                if (best != null && !p.IsReadOnly)
                                { p.Set(best.Id.Value.ToString()); stamped++; }
                                else
                                { unmatched++; }
                            }
                            catch (Exception ex) { StingLog.Warn($"MigrateLabelIds inner: {ex.Message}"); }
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MigrateSLDLabelIds", ex);
                msg = ex.Message;
                return Result.Failed;
            }

            TaskDialog.Show("STING - Migrate SLD Label IDs",
                  $"Views scanned       : {viewsScanned}\n"
                + $"Already stamped     : {alreadyStamped}\n"
                + $"Newly stamped       : {stamped}\n"
                + $"Unmatched (no note) : {unmatched}");
            return Result.Succeeded;
        }
    }
}
