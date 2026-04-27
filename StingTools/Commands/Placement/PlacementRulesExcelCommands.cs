// Phase 139.2 S — Excel round-trip commands for placement rules.
//
// Export   → writes one worksheet per discipline pack (Baseline,
//            MK_Electrical, Ceiling_Pendants, Conduiting_Phase, …)
//            plus a SCHEMA worksheet, with the full Phase 139.2
//            column set (manufacturer, two-phase, cluster, plaster,
//            tile, structural).
// Import   → reverse direction; merges back into the project
//            override file by RuleId / MergeKey.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Placement.Excel;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacementRulesExcelExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            var rules = PlacementRuleLoader.Load(doc?.PathName ?? "");

            string outDir = OutputLocationHelper.GetOutputPath(doc, "PlacementRules") ?? Path.GetTempPath();
            Directory.CreateDirectory(outDir);
            string xlsxPath = Path.Combine(outDir,
                $"STING_PlacementRules_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            try
            {
                PlacementRulesExcelExporter.Export(rules, xlsxPath);
            }
            catch (Exception ex)
            {
                message = $"Excel export failed: {ex.Message}";
                StingLog.Error(message);
                return Result.Failed;
            }

            TaskDialog.Show("STING - Placement Rules Excel Export",
                $"Exported {rules?.Count ?? 0} rule(s).\n\n{xlsxPath}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacementRulesExcelImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            string projectPath = doc?.PathName ?? "";
            if (string.IsNullOrEmpty(projectPath))
            {
                TaskDialog.Show("STING", "Save the project first — overrides write next to the .rvt.");
                return Result.Cancelled;
            }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select STING placement rules workbook",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true
            };
            if (ofd.ShowDialog() != true) return Result.Cancelled;
            string xlsx = ofd.FileName;

            var (rules, errors) = PlacementRulesExcelImporter.Import(xlsx);
            if (errors != null && errors.Count > 0)
            {
                StingLog.Warn("PlacementRulesExcelImport: " + string.Join("; ", errors.Take(20)));
            }

            try
            {
                string projectDir = Path.GetDirectoryName(projectPath);
                string overridePath = Path.Combine(projectDir, "STING_PLACEMENT_RULES.project.json");
                var set = new PlacementRuleSet { Version = "v4", Rules = rules };
                File.WriteAllText(overridePath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(set, Newtonsoft.Json.Formatting.Indented));

                TaskDialog.Show("STING - Placement Rules Excel Import",
                    $"Imported {rules.Count} rule(s) into:\n\n{overridePath}\n\n" +
                    (errors.Count > 0 ? $"{errors.Count} warnings (see STING log)." : "No warnings."));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Excel import write failed: {ex.Message}";
                StingLog.Error(message);
                return Result.Failed;
            }
        }
    }
}
