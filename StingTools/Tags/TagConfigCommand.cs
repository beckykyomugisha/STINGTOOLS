using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Configure discipline, system, product, and function code lookup tables.
    /// Displays current configuration source and loaded map counts.
    /// Supports loading from project_config.json or resetting to defaults.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            // Display current config — do NOT reload (preserves session customizations)
            string configPath = Path.Combine(
                StingToolsApp.DataPath ?? string.Empty,
                "project_config.json");

            var report = new StringBuilder();
            report.AppendLine("Tag Configuration");
            report.AppendLine(new string('─', 40));
            report.AppendLine($"Source:       {TagConfig.ConfigSource}");
            report.AppendLine($"Disciplines:  {TagConfig.DiscMap.Count} category mappings");
            report.AppendLine($"Systems:      {TagConfig.SysMap.Count} system codes");
            report.AppendLine($"Products:     {TagConfig.ProdMap.Count} product codes");
            report.AppendLine($"Functions:    {TagConfig.FuncMap.Count} function codes");
            report.AppendLine($"Locations:    {TagConfig.LocCodes.Count} location codes");
            report.AppendLine($"Zones:        {TagConfig.ZoneCodes.Count} zone codes");
            report.AppendLine();
            report.AppendLine("Discipline codes:");

            var discCounts = TagConfig.DiscMap
                .GroupBy(kvp => kvp.Value)
                .OrderBy(g => g.Key);
            foreach (var g in discCounts)
            {
                report.AppendLine($"  {g.Key,-4} → {g.Count()} categories");
            }

            report.AppendLine();
            report.AppendLine($"Config file: {configPath}");
            report.AppendLine(File.Exists(configPath) ? "(found)" : "(not found — using defaults)");

            TaskDialog td = new TaskDialog("Tag Configuration");
            td.MainInstruction = $"Config: {TagConfig.ConfigSource}";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
