// Phase 139 E3 — Excel round-trip commands for the Placement Centre.
//
// Two IExternalCommands wired into the Centre's toolbar:
//   - Placement_ExportExcel: dump every loaded rule to .xlsx with one
//     sheet per discipline pack.
//   - Placement_ImportExcel: read .xlsx back, replace project overlay.
//
// Project overlay is stored as STING_PLACEMENT_RULES.project.json
// alongside the .rvt — same merge mechanism the loader already uses.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Placement.Excel;

namespace StingTools.UI.PlacementCenter
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportRulesToExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc  = commandData?.Application?.ActiveUIDocument?.Document;
                var rules = PlacementRuleLoader.Load(doc?.PathName ?? "");
                if (rules == null || rules.Count == 0)
                {
                    TaskDialog.Show("STING Placement", "No placement rules loaded — nothing to export.");
                    return Result.Succeeded;
                }

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title    = "Export STING Placement Rules to Excel",
                    Filter   = "Excel workbook (*.xlsx)|*.xlsx",
                    FileName = $"STING_PLACEMENT_RULES_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                };
                if (sfd.ShowDialog() != true) return Result.Cancelled;

                PlacementRulesExcelExporter.Export(rules, sfd.FileName);
                TaskDialog.Show("STING Placement",
                    $"Exported {rules.Count} rules to:\n{sfd.FileName}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportRulesToExcelCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportRulesFromExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (string.IsNullOrEmpty(doc?.PathName))
                {
                    TaskDialog.Show("STING Placement",
                        "Save the project before importing — the project-override file is written next to the .rvt.");
                    return Result.Cancelled;
                }

                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Import STING Placement Rules from Excel",
                    Filter = "Excel workbook (*.xlsx)|*.xlsx",
                };
                if (ofd.ShowDialog() != true) return Result.Cancelled;

                var (rules, errors) = PlacementRulesExcelImporter.Import(ofd.FileName);
                if (errors.Count > 0)
                {
                    var preview = string.Join("\n", errors.Take(20));
                    var td = new TaskDialog("STING Placement — Import warnings")
                    {
                        MainInstruction = $"{errors.Count} issue(s) found while reading the workbook.",
                        MainContent = preview + (errors.Count > 20 ? $"\n... ({errors.Count - 20} more)" : ""),
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Continue and replace project overlay");
                    var res = td.Show();
                    if (res != TaskDialogResult.CommandLink1 && res != TaskDialogResult.Yes)
                        return Result.Cancelled;
                }

                if (rules.Count == 0)
                {
                    TaskDialog.Show("STING Placement", "No valid rules found in workbook.");
                    return Result.Cancelled;
                }

                string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                string outPath = Path.Combine(projDir, "STING_PLACEMENT_RULES.project.json");
                var set = new PlacementRuleSet
                {
                    Version     = "v4",
                    Description = $"Project override — imported from Excel {DateTime.Now:yyyy-MM-dd HH:mm} ({rules.Count} rules)",
                    Rules       = rules,
                };
                File.WriteAllText(outPath, JsonConvert.SerializeObject(set, Formatting.Indented));
                TaskDialog.Show("STING Placement",
                    $"Imported {rules.Count} rules to project overlay:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ImportRulesFromExcelCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
