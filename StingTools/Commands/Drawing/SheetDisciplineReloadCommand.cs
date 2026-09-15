// StingTools — Drawing Template Manager · load and inspect the discipline vocabulary
//
// SheetDisciplineConfig is Revit-free and takes JSON text. This is the Revit-bound
// half: it resolves the two paths through StingPaths (never by hand, so
// tools/check_path_discipline.ps1 stays green), loads them, and reports what is
// actually live.
//
// The report matters as much as the reload. A project drops an override in, sees
// no error, and assumes it took effect — when a mistyped field leaves Newtonsoft
// holding a default and the file does nothing at all. This names every entry that
// was rejected, and says plainly whether a file was found.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    internal static class SheetDisciplineLoader
    {
        /// <summary>Load the baseline + project override for this document. Safe to
        /// call repeatedly; returns the problems found.</summary>
        public static string[] Load(Document doc, out string baselinePath, out string projectPath)
        {
            baselinePath = null;
            projectPath = null;

            try
            {
                baselinePath = StingToolsApp.FindDataFile(SheetDisciplineConfig.BaselineFileName);
            }
            catch (Exception ex) { StingLog.Warn($"SheetDisciplines baseline lookup: {ex.Message}"); }

            try
            {
                // Family documents and unsaved projects have no project folder, and
                // asking for one throws. Neither is an error here -- the baseline
                // alone is a complete answer.
                if (doc != null && !doc.IsFamilyDocument && !string.IsNullOrEmpty(doc.PathName))
                    projectPath = StingPaths.MetaFile(doc, "_BIM_COORD",
                        SheetDisciplineConfig.ProjectFileName);
            }
            catch (Exception ex) { StingLog.Warn($"SheetDisciplines project lookup: {ex.Message}"); }

            string baseJson = ReadIfPresent(baselinePath);
            string projJson = ReadIfPresent(projectPath);
            if (projJson == null) projectPath = null;
            if (baseJson == null) baselinePath = null;

            var problems = SheetDisciplineConfig.Load(baseJson, projJson).ToArray();
            foreach (string p in problems)
                StingLog.Warn($"SheetDisciplines: {p}");
            return problems;
        }

        private static string ReadIfPresent(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetDisciplines read '{path}': {ex.Message}");
                return null;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetDisciplineReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;

            var problems = SheetDisciplineLoader.Load(doc, out string basePath, out string projPath);

            var prefixes = SheetDisciplineConfig.NumberPrefixes;
            var keywords = SheetDisciplineConfig.TitleKeywords;
            var columns = SheetDisciplineConfig.CsvColumns;
            var roles = SheetDisciplineConfig.RoleLetters;

            var kw = new StringBuilder();
            foreach (var rule in keywords)
                kw.AppendLine($"  {rule.Discipline,-6} {string.Join(", ", rule.Words ?? new System.Collections.Generic.List<string>())}");

            StingResultPanel.Create("")
                .SetTitle("Sheet Disciplines")
                .SetSubtitle(SheetDisciplineConfig.IsConfigured
                    ? "Loaded from file"
                    : "Built-in defaults — no file found")
                .SetOverallPct(problems.Length == 0 ? 100 : 0)
                .AddSection("Summary")
                .Metric("Corporate baseline", basePath ?? "(not found — using built-in defaults)")
                .Metric("Project override", projPath ?? "(none)")
                .Metric("Number prefixes", prefixes.Count.ToString())
                .Metric("Title keyword rules", keywords.Count.ToString())
                .Metric("CSV column mappings", columns.Count.ToString())
                .Metric("Role letter overrides", (roles?.Count ?? 0).ToString())
                .Metric("Entries rejected", problems.Length.ToString())
                .AddSection("Rejected Entries")
                .Text(problems.Length == 0
                    ? "(none)"
                    : string.Join("\n", problems.Select(p => "  " + p))
                      + "\n\nThese were ignored and everything else in the file was applied — "
                      + "one bad row does not discard the other twenty. A rejected role letter "
                      + "is the one worth acting on: it would have looked perfectly ordinary "
                      + "printed on a title block.")
                .AddSection("Sheet Number Prefixes")
                .Text(string.Join("\n", prefixes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .GroupBy(kv => kv.Value)
                    .Select(g => $"  {g.Key,-6} <- {string.Join(", ", g.Select(x => x.Key))}")))
                .AddSection("Title Keywords, In Decision Order")
                .Text(kw.ToString().TrimEnd())
                .AddSection("How To Change This")
                .Text("Copy the corporate baseline to your project's _BIM_COORD folder as "
                    + $"'{SheetDisciplineConfig.ProjectFileName}', edit it, and run this again.\n\n"
                    + "numberPrefixes and csvColumns MERGE onto the shipped set, so a file "
                    + "naming three prefixes adds three.\n"
                    + "titleKeywords REPLACE it, because their order is the rule — rules appended "
                    + "after the shipped ones could never be reached for any title the shipped "
                    + "list already matches.\n\n"
                    + "roleLetters maps YOUR discipline code to an ISO 19650 role letter "
                    + $"({SheetDisciplineConfig.RoleAlphabet}). The letters are the standard's; "
                    + "anything outside that set is rejected and listed above.")
                .Show();

            return Result.Succeeded;
        }
    }
}
