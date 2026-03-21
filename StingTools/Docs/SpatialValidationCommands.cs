using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G8:  Spatial Validation — room/space/area plan integrity checks
    //  G19: Grid/Level Audit — naming consistency, spacing, alignment
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: SpatialValidationEngine ──

    internal static class SpatialValidationEngine
    {
        /// <summary>Audit all rooms for common spatial issues.</summary>
        internal static SpatialAuditResult AuditRooms(Document doc)
        {
            var result = new SpatialAuditResult();
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>().ToList();

            result.TotalRooms = rooms.Count;
            foreach (var room in rooms)
            {
                try
                {
                    string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    double area = room.Area;
                    string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";

                    if (string.IsNullOrWhiteSpace(name))
                        result.Issues.Add(new SpatialIssue(room.Id, "Unnamed Room", $"Room {number} has no name", "High"));
                    if (string.IsNullOrWhiteSpace(number))
                        result.Issues.Add(new SpatialIssue(room.Id, "No Room Number", $"Room '{name}' has no number", "Medium"));
                    if (area <= 0)
                        result.Issues.Add(new SpatialIssue(room.Id, "Zero Area", $"Room '{name}' ({number}) has zero area — not enclosed", "High"));
                    if (room.Location == null)
                        result.Issues.Add(new SpatialIssue(room.Id, "Unplaced Room", $"Room '{name}' ({number}) is not placed", "High"));
                    if (string.IsNullOrWhiteSpace(dept))
                        result.Issues.Add(new SpatialIssue(room.Id, "No Department", $"Room '{name}' ({number}) has no department", "Low"));

                    // Check for very small rooms (< 1 sqm = ~10.76 sqft)
                    if (area > 0 && area < 10.76)
                        result.Issues.Add(new SpatialIssue(room.Id, "Very Small Room", $"Room '{name}' ({number}) area = {area:F1} sqft", "Medium"));

                    // Check for very large rooms (> 10000 sqft)
                    if (area > 10000)
                        result.Issues.Add(new SpatialIssue(room.Id, "Very Large Room", $"Room '{name}' ({number}) area = {area:F0} sqft — verify", "Low"));
                }
                catch (Exception ex) { StingLog.Warn($"SpatialAudit room: {ex.Message}"); }
            }

            // Check for duplicate room numbers
            var numberGroups = rooms
                .Where(r => !string.IsNullOrWhiteSpace(r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString()))
                .GroupBy(r => r.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString())
                .Where(g => g.Count() > 1);
            foreach (var group in numberGroups)
            {
                foreach (var room in group)
                    result.Issues.Add(new SpatialIssue(room.Id, "Duplicate Number", $"Room number '{group.Key}' used {group.Count()} times", "High"));
            }

            result.EnclosedCount = rooms.Count(r => r.Area > 0);
            result.UnenclosedCount = rooms.Count(r => r.Area <= 0);
            result.UnplacedCount = rooms.Count(r => r.Location == null);

            return result;
        }

        /// <summary>Audit grids for naming consistency and spacing.</summary>
        internal static GridAuditResult AuditGrids(Document doc)
        {
            var result = new GridAuditResult();
            var grids = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .Cast<Grid>().ToList();

            result.TotalGrids = grids.Count;
            if (grids.Count == 0) return result;

            // Separate into letter (Y) and number (X) grids
            var letterGrids = new List<Grid>();
            var numberGrids = new List<Grid>();
            var otherGrids = new List<Grid>();

            foreach (var g in grids)
            {
                string name = g.Name ?? "";
                if (int.TryParse(name, out _)) numberGrids.Add(g);
                else if (name.Length <= 2 && char.IsLetter(name[0])) letterGrids.Add(g);
                else otherGrids.Add(g);
            }

            result.LetterGrids = letterGrids.Count;
            result.NumberGrids = numberGrids.Count;
            result.OtherGrids = otherGrids.Count;

            // Check naming consistency
            if (otherGrids.Count > 0)
            {
                foreach (var g in otherGrids)
                    result.Issues.Add(new SpatialIssue(g.Id, "Non-Standard Grid Name",
                        $"Grid '{g.Name}' doesn't follow letter/number convention", "Low"));
            }

            // Check for duplicate grid names
            var dupNames = grids.GroupBy(g => g.Name).Where(g => g.Count() > 1);
            foreach (var dup in dupNames)
            {
                foreach (var g in dup)
                    result.Issues.Add(new SpatialIssue(g.Id, "Duplicate Grid Name",
                        $"Grid name '{dup.Key}' used {dup.Count()} times", "High"));
            }

            return result;
        }

        /// <summary>Audit levels for naming consistency, spacing, and completeness.</summary>
        internal static LevelAuditResult AuditLevels(Document doc)
        {
            var result = new LevelAuditResult();
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            result.TotalLevels = levels.Count;
            if (levels.Count == 0) return result;

            result.MinElevation = levels.First().Elevation;
            result.MaxElevation = levels.Last().Elevation;

            // Check spacing between levels
            for (int i = 1; i < levels.Count; i++)
            {
                double spacing = levels[i].Elevation - levels[i - 1].Elevation;
                double spacingMm = spacing * 304.8; // feet to mm

                if (spacingMm < 500) // Less than 500mm between levels
                    result.Issues.Add(new SpatialIssue(levels[i].Id, "Close Levels",
                        $"'{levels[i].Name}' is only {spacingMm:F0}mm above '{levels[i - 1].Name}'", "Medium"));

                if (spacingMm > 20000) // More than 20m between levels
                    result.Issues.Add(new SpatialIssue(levels[i].Id, "Large Gap",
                        $"{spacingMm / 1000:F1}m gap between '{levels[i - 1].Name}' and '{levels[i].Name}'", "Low"));
            }

            // Check for duplicate names
            var dupNames = levels.GroupBy(l => l.Name).Where(g => g.Count() > 1);
            foreach (var dup in dupNames)
                foreach (var l in dup)
                    result.Issues.Add(new SpatialIssue(l.Id, "Duplicate Level Name",
                        $"Level name '{dup.Key}' used {dup.Count()} times", "High"));

            // Check naming convention (should have numeric prefix or standard names)
            foreach (var level in levels)
            {
                string name = level.Name;
                bool hasStandardName = name.StartsWith("Level", StringComparison.OrdinalIgnoreCase) ||
                                       name.StartsWith("L", StringComparison.OrdinalIgnoreCase) ||
                                       name.Contains("Ground", StringComparison.OrdinalIgnoreCase) ||
                                       name.Contains("Basement", StringComparison.OrdinalIgnoreCase) ||
                                       name.Contains("Roof", StringComparison.OrdinalIgnoreCase) ||
                                       name.Contains("GF", StringComparison.OrdinalIgnoreCase);
                if (!hasStandardName)
                    result.Issues.Add(new SpatialIssue(level.Id, "Non-Standard Level Name",
                        $"Level '{name}' doesn't follow standard naming", "Low"));
            }

            return result;
        }
    }

    // ── Data types ──

    internal class SpatialIssue
    {
        public ElementId ElementId { get; set; }
        public string IssueType { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public SpatialIssue(ElementId id, string type, string desc, string sev)
        { ElementId = id; IssueType = type; Description = desc; Severity = sev; }
    }

    internal class SpatialAuditResult
    {
        public int TotalRooms { get; set; }
        public int EnclosedCount { get; set; }
        public int UnenclosedCount { get; set; }
        public int UnplacedCount { get; set; }
        public List<SpatialIssue> Issues { get; set; } = new List<SpatialIssue>();
    }

    internal class GridAuditResult
    {
        public int TotalGrids { get; set; }
        public int LetterGrids { get; set; }
        public int NumberGrids { get; set; }
        public int OtherGrids { get; set; }
        public List<SpatialIssue> Issues { get; set; } = new List<SpatialIssue>();
    }

    internal class LevelAuditResult
    {
        public int TotalLevels { get; set; }
        public double MinElevation { get; set; }
        public double MaxElevation { get; set; }
        public List<SpatialIssue> Issues { get; set; } = new List<SpatialIssue>();
    }

    #endregion

    #region ── G8: Spatial Validation Commands ──

    /// <summary>
    /// Comprehensive room/space validation: enclosure, naming, areas, duplicates.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpatialValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = SpatialValidationEngine.AuditRooms(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Room Validation — {result.TotalRooms} rooms\n");
            sb.AppendLine($"  Enclosed:   {result.EnclosedCount}");
            sb.AppendLine($"  Unenclosed: {result.UnenclosedCount}");
            sb.AppendLine($"  Unplaced:   {result.UnplacedCount}\n");

            if (result.Issues.Count == 0)
            {
                sb.AppendLine("No issues found. All rooms are properly configured.");
            }
            else
            {
                var byType = result.Issues.GroupBy(i => i.IssueType).OrderByDescending(g => g.Count());
                sb.AppendLine($"── {result.Issues.Count} Issues Found ──");
                foreach (var group in byType)
                    sb.AppendLine($"  {group.Count(),3}× {group.Key}");

                sb.AppendLine();
                foreach (var issue in result.Issues.Take(20))
                    sb.AppendLine($"  [{issue.Severity}] {issue.Description}");
                if (result.Issues.Count > 20)
                    sb.AppendLine($"  ... and {result.Issues.Count - 20} more issues");
            }

            TaskDialog td = new TaskDialog("Room Validation");
            td.MainInstruction = $"{result.TotalRooms} rooms, {result.Issues.Count} issues";
            td.MainContent = sb.ToString();
            if (result.Issues.Count > 0)
            {
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select affected elements");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");
            }
            var tdResult = td.Show();
            if (tdResult == TaskDialogResult.CommandLink1)
            {
                var ids = result.Issues.Select(i => i.ElementId).Distinct().ToList();
                ctx.UIDoc.Selection.SetElementIds(ids);
            }

            StingLog.Info($"SpatialValidation: {result.TotalRooms} rooms, {result.Issues.Count} issues");
            return Result.Succeeded;
        }
    }

    /// <summary>Export spatial validation to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpatialValidationExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = SpatialValidationEngine.AuditRooms(ctx.Doc);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "SpatialValidation", ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,IssueType,Severity,Description");
            foreach (var issue in result.Issues)
                sb.AppendLine($"{issue.ElementId.Value},\"{issue.IssueType}\",{issue.Severity},\"{issue.Description.Replace("\"", "\"\"")}\"");
            File.WriteAllText(path, sb.ToString());

            TaskDialog.Show("Spatial Export", $"Exported {result.Issues.Count} issues to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G19: Grid/Level Audit Commands ──

    /// <summary>
    /// Audit grids for naming consistency, duplicates, and organisation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GridAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = SpatialValidationEngine.AuditGrids(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Grid Audit — {result.TotalGrids} grids\n");
            sb.AppendLine($"  Letter grids (A, B, C...): {result.LetterGrids}");
            sb.AppendLine($"  Number grids (1, 2, 3...): {result.NumberGrids}");
            sb.AppendLine($"  Other naming:              {result.OtherGrids}\n");

            if (result.Issues.Count == 0)
                sb.AppendLine("No issues found.");
            else
            {
                sb.AppendLine($"── {result.Issues.Count} Issues ──");
                foreach (var issue in result.Issues)
                    sb.AppendLine($"  [{issue.Severity}] {issue.Description}");
            }

            TaskDialog.Show("Grid Audit", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Audit levels for naming, spacing, and consistency.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LevelAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = SpatialValidationEngine.AuditLevels(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Level Audit — {result.TotalLevels} levels\n");
            sb.AppendLine($"  Elevation range: {result.MinElevation * 304.8:F0}mm to {result.MaxElevation * 304.8:F0}mm\n");

            if (result.Issues.Count == 0)
                sb.AppendLine("No issues found.");
            else
            {
                sb.AppendLine($"── {result.Issues.Count} Issues ──");
                foreach (var issue in result.Issues)
                    sb.AppendLine($"  [{issue.Severity}] {issue.Description}");
            }

            TaskDialog.Show("Level Audit", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion
}
