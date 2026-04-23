// StingTools v4 MVP — GenerateFabPackageCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    /// <summary>
    /// Static option surface for Generate Fabrication Package, populated
    /// by the TAGS → Fabrication sub-tab before Execute runs.
    /// Mirrors the 16 CheckBox / 2 RadioButton controls documented in
    /// StingDockPanel.xaml under the Fabrication sub-tab.
    /// </summary>
    public static class FabricationOptions
    {
        // Scope radio buttons
        public static bool ScopeSelection { get; set; } = true;
        public static bool ScopeActiveView{ get; set; } = false;
        public static bool ScopeProject   { get; set; } = false;

        // Discipline rule toggles
        public static bool RulePipe       { get; set; } = true;
        public static bool RulePipeLB     { get; set; } = false;
        public static bool RuleDuct       { get; set; } = true;
        public static bool RuleDuctPitt   { get; set; } = false;
        public static bool RuleConduit    { get; set; } = true;

        // Output toggles
        public static bool GenerateAssemblies  { get; set; } = true;
        public static bool GenerateViews       { get; set; } = true;
        public static bool GenerateSheets      { get; set; } = true;
        public static bool PlaceISO6412Symbols { get; set; } = true;
        public static bool EmitPerDisciplineCsv{ get; set; } = true;

        // Content mode — ISO 6412 (workshop) vs Generic (geometry only)
        public static bool ContentModeIso6412  { get; set; } = true;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFabPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Scope resolution — Fabrication sub-tab radio buttons drive
            // which MEP elements feed the engine. Selection (default)
            // means "current uidoc selection"; Active view means "all
            // MEP curves visible in the active view"; Project means
            // "every MEP curve in the document".
            var ids = CollectScope(doc, uidoc);
            if (ids == null || ids.Count == 0)
            {
                string scopeLabel =
                    FabricationOptions.ScopeProject    ? "project"       :
                    FabricationOptions.ScopeActiveView ? "active view"   :
                                                         "current selection";
                TaskDialog.Show("STING v4 — Generate Fabrication Package",
                    $"Scope '{scopeLabel}' contains no MEP elements to package.\n\n" +
                    "FabricationEngine will:\n" +
                    "  1. Group elements per discipline rules (STING_FAB_RULES.json)\n" +
                    "  2. Create AssemblyInstances with SP-{DISC}-{SYS}-{LVL}-{SEQ} naming\n" +
                    "  3. Generate 5 views per assembly + BOM schedule\n" +
                    "  4. Lay out shop drawing sheets with title block populated\n" +
                    "  5. Emit per-discipline CSV sidecars (bend / weld / seam)");
                return Result.Cancelled;
            }

            FabricationResult res;
            try
            {
                res = FabricationEngine.GenerateFabricationPackage(doc, ids);
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateFabPackageCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(res);

            // Open first generated sheet for instant feedback
            if (res.SheetIds.Count > 0)
            {
                try
                {
                    var sheet = doc.GetElement(res.SheetIds[0]) as ViewSheet;
                    if (sheet != null) uidoc.ActiveView = sheet;
                }
                catch (Exception ex) { StingLog.Warn($"GenerateFabPackage open sheet failed: {ex.Message}"); }
            }

            return Result.Succeeded;
        }

        private static List<ElementId> CollectScope(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var ids = new List<ElementId>();
            var mepCats = new[]
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
            };
            try
            {
                if (FabricationOptions.ScopeProject)
                {
                    var col = new FilteredElementCollector(doc)
                        .WherePasses(new ElementMulticategoryFilter(mepCats))
                        .WhereElementIsNotElementType();
                    foreach (var e in col) ids.Add(e.Id);
                }
                else if (FabricationOptions.ScopeActiveView)
                {
                    var view = doc.ActiveView;
                    if (view != null)
                    {
                        var col = new FilteredElementCollector(doc, view.Id)
                            .WherePasses(new ElementMulticategoryFilter(mepCats))
                            .WhereElementIsNotElementType();
                        foreach (var e in col) ids.Add(e.Id);
                    }
                }
                else
                {
                    var sel = uidoc.Selection.GetElementIds();
                    if (sel != null)
                    {
                        foreach (var id in sel)
                        {
                            var el = doc.GetElement(id);
                            if (el?.Category == null) continue;
                            int bic = (int)el.Category.Id.Value;
                            foreach (var c in mepCats)
                            {
                                if ((int)c == bic) { ids.Add(id); break; }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GenerateFabPackage.CollectScope: {ex.Message}");
            }
            return ids;
        }

        private void ShowResult(FabricationResult res)
        {
            var panel = StingResultPanel.Create("v4 Fabrication Package");
            panel.SetSubtitle(res.FormatSummary());
            panel.AddSection("ASSEMBLIES BY DISCIPLINE");
            if (res.AssembliesByDiscipline.Count == 0)
                panel.Text("No assemblies created.");
            else
                foreach (var kv in res.AssembliesByDiscipline)
                    panel.Metric(kv.Key, kv.Value.ToString());

            panel.AddSection("SHEETS")
                 .Metric("Generated", res.SheetIds.Count.ToString())
                 .Metric("Failed",    res.FailedCount.ToString());

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }
            panel.Show();
        }
    }
}
