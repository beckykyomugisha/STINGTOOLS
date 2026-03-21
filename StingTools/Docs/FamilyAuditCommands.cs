using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G6:  Family Audit — loaded family analysis, versioning, duplicates, purge
    //  G7:  View/Sheet Completeness — view naming, sheet compliance, completeness
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: FamilyAuditEngine ──

    internal static class FamilyAuditEngine
    {
        /// <summary>Audit all loaded families in the document.</summary>
        internal static FamilyAuditResult AuditFamilies(Document doc)
        {
            var result = new FamilyAuditResult();
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            result.TotalFamilies = families.Count;

            var familyNames = new Dictionary<string, List<Family>>(StringComparer.OrdinalIgnoreCase);

            foreach (var fam in families)
            {
                try
                {
                    var info = new FamilyInfo
                    {
                        Name = fam.Name,
                        Id = fam.Id,
                        Category = fam.FamilyCategory?.Name ?? "(none)",
                        IsInPlace = fam.IsInPlace,
                        IsEditable = fam.IsEditable
                    };

                    // Count types
                    var typeIds = fam.GetFamilySymbolIds();
                    info.TypeCount = typeIds.Count;

                    // Count placed instances
                    info.InstanceCount = typeIds.Sum(tid =>
                        new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>().Count(fi => fi.Symbol?.Id == tid));

                    if (info.InstanceCount == 0) result.UnusedFamilies.Add(info);
                    if (info.IsInPlace) result.InPlaceFamilies.Add(info);
                    result.AllFamilies.Add(info);

                    // Track duplicates by name
                    if (!familyNames.ContainsKey(fam.Name)) familyNames[fam.Name] = new List<Family>();
                    familyNames[fam.Name].Add(fam);
                }
                catch (Exception ex) { StingLog.Warn($"FamilyAudit '{fam.Name}': {ex.Message}"); }
            }

            // Find duplicate names
            foreach (var kvp in familyNames.Where(k => k.Value.Count > 1))
                result.DuplicateNames.Add(kvp.Key, kvp.Value.Count);

            // Category breakdown
            result.CategoryBreakdown = result.AllFamilies
                .GroupBy(f => f.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            return result;
        }

        /// <summary>Audit view and sheet completeness.</summary>
        internal static ViewSheetAuditResult AuditViewsAndSheets(Document doc)
        {
            var result = new ViewSheetAuditResult();

            // Sheets
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            result.TotalSheets = sheets.Count;

            foreach (var sheet in sheets)
            {
                string name = sheet.Name ?? "";
                string number = sheet.SheetNumber ?? "";
                int vpCount = sheet.GetAllPlacedViews().Count;

                if (string.IsNullOrWhiteSpace(name))
                    result.Issues.Add(new ViewSheetIssue(sheet.Id, "Sheet", "Unnamed Sheet", $"Sheet {number} has no name", "Medium"));
                if (vpCount == 0)
                    result.Issues.Add(new ViewSheetIssue(sheet.Id, "Sheet", "Empty Sheet", $"Sheet '{number} - {name}' has no viewports", "Medium"));

                // Check ISO 19650 sheet numbering
                if (!number.Contains("-"))
                    result.Issues.Add(new ViewSheetIssue(sheet.Id, "Sheet", "Non-ISO Number", $"Sheet number '{number}' doesn't follow ISO 19650 format", "Low"));
            }

            // Views
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted).ToList();
            result.TotalViews = views.Count;

            var placedViewIds = new HashSet<ElementId>(sheets.SelectMany(s => s.GetAllPlacedViews()));
            result.PlacedViews = placedViewIds.Count;
            result.UnplacedViews = views.Count - views.Count(v => placedViewIds.Contains(v.Id));

            foreach (var view in views)
            {
                string name = view.Name ?? "";

                // Check for default naming (Copy 1, etc.)
                if (name.Contains("Copy") || name.Contains(" - Copy"))
                    result.Issues.Add(new ViewSheetIssue(view.Id, "View", "Default Name", $"View '{name}' has default copy name", "Low"));

                // Check template assignment
                if (view.ViewTemplateId == ElementId.InvalidElementId && view.ViewType != ViewType.Schedule
                    && view.ViewType != ViewType.DrawingSheet && view.ViewType != ViewType.Legend)
                    result.Issues.Add(new ViewSheetIssue(view.Id, "View", "No Template", $"View '{name}' has no view template", "Low"));
            }

            // View type breakdown
            result.ViewTypeBreakdown = views.GroupBy(v => v.ViewType)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            return result;
        }
    }

    // ── Data types ──

    internal class FamilyAuditResult
    {
        public int TotalFamilies { get; set; }
        public List<FamilyInfo> AllFamilies { get; set; } = new List<FamilyInfo>();
        public List<FamilyInfo> UnusedFamilies { get; set; } = new List<FamilyInfo>();
        public List<FamilyInfo> InPlaceFamilies { get; set; } = new List<FamilyInfo>();
        public Dictionary<string, int> DuplicateNames { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> CategoryBreakdown { get; set; } = new Dictionary<string, int>();
    }

    internal class FamilyInfo
    {
        public string Name { get; set; } = "";
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string Category { get; set; } = "";
        public bool IsInPlace { get; set; }
        public bool IsEditable { get; set; }
        public int TypeCount { get; set; }
        public int InstanceCount { get; set; }
    }

    internal class ViewSheetAuditResult
    {
        public int TotalSheets { get; set; }
        public int TotalViews { get; set; }
        public int PlacedViews { get; set; }
        public int UnplacedViews { get; set; }
        public List<ViewSheetIssue> Issues { get; set; } = new List<ViewSheetIssue>();
        public Dictionary<string, int> ViewTypeBreakdown { get; set; } = new Dictionary<string, int>();
    }

    internal class ViewSheetIssue
    {
        public ElementId ElementId { get; set; }
        public string ElementType { get; set; }
        public string IssueType { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public ViewSheetIssue(ElementId id, string elType, string issueType, string desc, string sev)
        { ElementId = id; ElementType = elType; IssueType = issueType; Description = desc; Severity = sev; }
    }

    #endregion

    #region ── G6: Family Audit Commands ──

    /// <summary>
    /// Audit loaded families: unused, in-place, duplicates, category breakdown.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = FamilyAuditEngine.AuditFamilies(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Family Audit — {result.TotalFamilies} families loaded\n");
            sb.AppendLine($"  Unused (0 instances): {result.UnusedFamilies.Count}");
            sb.AppendLine($"  In-Place families:    {result.InPlaceFamilies.Count}");
            sb.AppendLine($"  Duplicate names:      {result.DuplicateNames.Count}\n");

            sb.AppendLine("── Category Breakdown ──");
            foreach (var kvp in result.CategoryBreakdown.OrderByDescending(c => c.Value).Take(15))
                sb.AppendLine($"  {kvp.Key,-30} {kvp.Value,5} families");

            if (result.UnusedFamilies.Count > 0)
            {
                sb.AppendLine($"\n── Top Unused Families (purge candidates) ──");
                foreach (var f in result.UnusedFamilies.OrderBy(f => f.Category).Take(15))
                    sb.AppendLine($"  {f.Category,-25} {f.Name} ({f.TypeCount} types)");
                if (result.UnusedFamilies.Count > 15)
                    sb.AppendLine($"  ... and {result.UnusedFamilies.Count - 15} more");
            }

            if (result.InPlaceFamilies.Count > 0)
            {
                sb.AppendLine($"\n── In-Place Families (convert to loadable) ──");
                foreach (var f in result.InPlaceFamilies)
                    sb.AppendLine($"  {f.Name} ({f.InstanceCount} instances)");
            }

            TaskDialog.Show("Family Audit", sb.ToString());
            StingLog.Info($"FamilyAudit: {result.TotalFamilies} families, {result.UnusedFamilies.Count} unused, {result.InPlaceFamilies.Count} in-place");
            return Result.Succeeded;
        }
    }

    /// <summary>Export family audit to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyAuditExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = FamilyAuditEngine.AuditFamilies(ctx.Doc);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "FamilyAudit", ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("Name,Category,InPlace,TypeCount,InstanceCount,Editable");
            foreach (var f in result.AllFamilies.OrderBy(f => f.Category).ThenBy(f => f.Name))
                sb.AppendLine($"\"{f.Name}\",\"{f.Category}\",{f.IsInPlace},{f.TypeCount},{f.InstanceCount},{f.IsEditable}");
            File.WriteAllText(path, sb.ToString());

            TaskDialog.Show("Family Audit Export", $"Exported {result.AllFamilies.Count} families to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G7: View/Sheet Completeness Commands ──

    /// <summary>
    /// Audit view and sheet completeness: naming, templates, placement, ISO compliance.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewSheetCompletenessCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = FamilyAuditEngine.AuditViewsAndSheets(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine($"View/Sheet Completeness Audit\n");
            sb.AppendLine($"  Sheets:         {result.TotalSheets}");
            sb.AppendLine($"  Views:          {result.TotalViews}");
            sb.AppendLine($"  Placed on sheet:{result.PlacedViews}");
            sb.AppendLine($"  Unplaced:       {result.UnplacedViews}\n");

            sb.AppendLine("── View Types ──");
            foreach (var kvp in result.ViewTypeBreakdown.OrderByDescending(v => v.Value))
                sb.AppendLine($"  {kvp.Key,-25} {kvp.Value,5}");

            if (result.Issues.Count > 0)
            {
                var byType = result.Issues.GroupBy(i => i.IssueType).OrderByDescending(g => g.Count());
                sb.AppendLine($"\n── {result.Issues.Count} Issues ──");
                foreach (var group in byType)
                    sb.AppendLine($"  {group.Count(),3}× {group.Key}");
            }
            else
            {
                sb.AppendLine("\nNo issues found.");
            }

            TaskDialog.Show("View/Sheet Audit", sb.ToString());
            StingLog.Info($"ViewSheetAudit: {result.TotalSheets} sheets, {result.TotalViews} views, {result.Issues.Count} issues");
            return Result.Succeeded;
        }
    }

    #endregion
}
