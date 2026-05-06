// StingTools — SLD generator commands (Phase 175)

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
            var result = SLDGenerator.GenerateSLD(ctx.Doc, std);
            if (!result.Success) { TaskDialog.Show("STING", $"SLD generation failed: {result.Warning}"); return Result.Failed; }
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
            var pick = StingTools.UI.StingListPicker.Show(
                "SLD Standard", "Pick the standard for the generated SLD.",
                SymbolStandardRegistry.ListStandards().ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            string viewName = $"STING - SLD - {pick} - {DateTime.Now:yyyyMMdd-HHmm}";
            var result = SLDGenerator.GenerateSLD(ctx.Doc, pick, viewName);
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
                .Where(v => v.Name?.StartsWith("STING - SLD", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (slds.Count == 0) { TaskDialog.Show("STING", "No STING SLD views found. Run Generate first."); return Result.Cancelled; }
            int n = 0;
            foreach (var v in slds)
            {
                SLDGenerator.UpdateSLD(ctx.Doc, v, ElementId.InvalidElementId);
                n++;
            }
            TaskDialog.Show("STING", $"Refreshed {n} SLD view(s).");
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
            string p = Path.Combine(Path.GetDirectoryName(ctx.Doc.PathName ?? ""), "project_config.json");
            if (string.IsNullOrEmpty(p)) { TaskDialog.Show("STING", "Project must be saved first."); return Result.Failed; }

            try
            {
                JObject root = File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : new JObject();
                bool current = (bool)(root["sld_sync_enabled"] ?? false);
                bool next = !current;
                root["sld_sync_enabled"] = next;
                File.WriteAllText(p, root.ToString());
                TaskDialog.Show("STING", $"SLD sync {(next ? "enabled" : "disabled")}. Restart Revit to apply.");
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
            var root = SLDCircuitTraverser.BuildHierarchy(ctx.Doc);
            int circuits = 0;
            void Count(SLDNode n) { circuits++; foreach (var c in n.Children) Count(c); }
            if (root != null) Count(root);
            int sldSymbols = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Count(fi => !string.IsNullOrEmpty(fi.LookupParameter("STING_SLD_ELEMENT_ID")?.AsString()));
            TaskDialog.Show("STING - SLD Validate",
                $"Electrical circuits in model : {circuits}\nSLD symbols placed           : {sldSymbols}");
            return Result.Succeeded;
        }
    }
}
