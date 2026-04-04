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

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G3: STING Workset Audit Commands
    //
    //  Workset organisation audit, element-to-workset assignment validation,
    //  standard workset creation, and workset health reporting.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: WorksetAuditEngine ──

    internal static class WorksetAuditEngine
    {
        /// <summary>Standard discipline worksets expected in a STING project.</summary>
        internal static readonly Dictionary<string, string[]> StandardWorksets = new Dictionary<string, string[]>
        {
            ["Architecture"] = new[] { "Walls", "Doors", "Windows", "Rooms", "Floors", "Roofs", "Ceilings", "Stairs", "Railings", "Curtain Panels", "Curtain Wall Mullions", "Generic Models" },
            ["Structure"] = new[] { "Structural Columns", "Structural Framing", "Structural Foundations", "Structural Connections" },
            ["MEP-Mechanical"] = new[] { "Mechanical Equipment", "Duct Systems", "Ducts", "Duct Fittings", "Duct Accessories", "Flex Ducts", "Air Terminals" },
            ["MEP-Electrical"] = new[] { "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures", "Lighting Devices", "Cable Trays", "Cable Tray Fittings", "Conduits", "Conduit Fittings" },
            ["MEP-Plumbing"] = new[] { "Plumbing Fixtures", "Pipe Systems", "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Sprinklers" },
            ["MEP-Fire Protection"] = new[] { "Fire Alarm Devices", "Sprinklers" },
            ["Interior"] = new[] { "Furniture", "Furniture Systems", "Casework", "Specialty Equipment" },
            ["Site"] = new[] { "Topography", "Site", "Planting", "Parking" },
            ["Shared Levels and Grids"] = new[] { "Grids", "Levels" },
            ["Links"] = new string[0]
        };

        /// <summary>Audit workset assignment compliance.</summary>
        internal static WorksetAuditResult AuditWorksets(Document doc)
        {
            var result = new WorksetAuditResult();

            if (!doc.IsWorkshared)
            {
                result.IsWorkshared = false;
                return result;
            }
            result.IsWorkshared = true;

            // Get all user-created worksets
            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .ToList();
            result.WorksetCount = worksets.Count;

            // Check which standard worksets exist
            foreach (var kvp in StandardWorksets)
            {
                bool exists = worksets.Any(w => w.Name.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                result.StandardWorksetStatus[kvp.Key] = exists;
            }

            // Audit elements on each workset — single pass over all elements
            var wsInfoMap = new Dictionary<WorksetId, WorksetInfo>();
            foreach (var ws in worksets)
            {
                var wsInfo = new WorksetInfo { Name = ws.Name, Id = ws.Id };
                wsInfoMap[ws.Id] = wsInfo;
                result.Worksets.Add(wsInfo);
            }

            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            foreach (var el in allElements)
            {
                try
                {
                    if (wsInfoMap.TryGetValue(el.WorksetId, out var wsInfo))
                    {
                        wsInfo.ElementCount++;
                        string catName = el.Category?.Name ?? "(no category)";
                        if (wsInfo.CategoryBreakdown == null)
                            wsInfo.CategoryBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        wsInfo.CategoryBreakdown[catName] = wsInfo.CategoryBreakdown.GetValueOrDefault(catName) + 1;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"WorksetAudit element check: {ex.Message}"); }
            }

            // Check for misplaced elements (categories on wrong workset)
            foreach (var wsInfo in result.Worksets)
            {
                // Find expected categories for this workset
                string matchedStandard = StandardWorksets.Keys
                    .FirstOrDefault(k => wsInfo.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matchedStandard != null && StandardWorksets[matchedStandard].Length > 0)
                {
                    var expected = new HashSet<string>(StandardWorksets[matchedStandard], StringComparer.OrdinalIgnoreCase);
                    foreach (var catKvp in wsInfo.CategoryBreakdown)
                    {
                        if (!expected.Contains(catKvp.Key) && catKvp.Value > 0)
                            result.Misplacements.Add(new WorksetMisplacement
                            {
                                Category = catKvp.Key,
                                CurrentWorkset = wsInfo.Name,
                                ExpectedWorkset = matchedStandard,
                                Count = catKvp.Value
                            });
                    }
                }
            }

            // Calculate compliance score
            int standardPresent = result.StandardWorksetStatus.Count(s => s.Value);
            result.CompliancePercent = StandardWorksets.Count > 0
                ? (int)(100.0 * standardPresent / StandardWorksets.Count)
                : 100;

            return result;
        }
    }

    // ── Data types ──

    internal class WorksetAuditResult
    {
        public bool IsWorkshared { get; set; }
        public int WorksetCount { get; set; }
        public Dictionary<string, bool> StandardWorksetStatus { get; set; } = new Dictionary<string, bool>();
        public List<WorksetInfo> Worksets { get; set; } = new List<WorksetInfo>();
        public List<WorksetMisplacement> Misplacements { get; set; } = new List<WorksetMisplacement>();
        public int CompliancePercent { get; set; }
    }

    internal class WorksetInfo
    {
        public string Name { get; set; } = "";
        public WorksetId Id { get; set; }
        public int ElementCount { get; set; }
        public Dictionary<string, int> CategoryBreakdown { get; set; } = new Dictionary<string, int>();
    }

    internal class WorksetMisplacement
    {
        public string Category { get; set; } = "";
        public string CurrentWorkset { get; set; } = "";
        public string ExpectedWorkset { get; set; } = "";
        public int Count { get; set; }
    }

    #endregion

    #region ── Commands ──

    /// <summary>
    /// Audit workset organisation and element assignment compliance.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var result = WorksetAuditEngine.AuditWorksets(doc);

            if (!result.IsWorkshared)
            {
                TaskDialog.Show("Workset Audit", "This model does not have worksharing enabled.\n\nEnable worksharing first, then re-run this audit.");
                return Result.Succeeded;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Workset Audit — {result.WorksetCount} worksets found");
            sb.AppendLine($"Compliance: {result.CompliancePercent}%\n");

            sb.AppendLine("── Standard Worksets ──");
            foreach (var kvp in result.StandardWorksetStatus)
                sb.AppendLine($"  [{(kvp.Value ? "✓" : "✗")}] {kvp.Key}");

            sb.AppendLine($"\n── Workset Element Counts ──");
            foreach (var ws in result.Worksets.OrderByDescending(w => w.ElementCount))
                sb.AppendLine($"  {ws.Name,-30} {ws.ElementCount,6} elements");

            if (result.Misplacements.Count > 0)
            {
                sb.AppendLine($"\n── Misplaced Elements ({result.Misplacements.Count} issues) ──");
                foreach (var m in result.Misplacements.Take(15))
                    sb.AppendLine($"  {m.Count,4}× {m.Category} on '{m.CurrentWorkset}' (expected '{m.ExpectedWorkset}')");
                if (result.Misplacements.Count > 15)
                    sb.AppendLine($"  ... and {result.Misplacements.Count - 15} more");
            }

            TaskDialog.Show("Workset Audit", sb.ToString());
            StingLog.Info($"WorksetAudit: {result.WorksetCount} worksets, {result.CompliancePercent}% compliant, {result.Misplacements.Count} misplacements");
            return Result.Succeeded;
        }
    }

    /// <summary>Export workset audit to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetAuditExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = WorksetAuditEngine.AuditWorksets(ctx.Doc);
            if (!result.IsWorkshared) { TaskDialog.Show("STING", "Worksharing not enabled."); return Result.Succeeded; }

            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "WorksetAudit", ".csv");
            var sb = new StringBuilder();
            sb.AppendLine("Workset,ElementCount,Categories");
            foreach (var ws in result.Worksets)
            {
                string cats = string.Join("; ", ws.CategoryBreakdown.OrderByDescending(c => c.Value).Take(5).Select(c => $"{c.Key}={c.Value}"));
                sb.AppendLine($"\"{ws.Name}\",{ws.ElementCount},\"{cats}\"");
            }
            if (result.Misplacements.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Category,CurrentWorkset,ExpectedWorkset,Count");
                foreach (var m in result.Misplacements)
                    sb.AppendLine($"\"{m.Category}\",\"{m.CurrentWorkset}\",\"{m.ExpectedWorkset}\",{m.Count}");
            }
            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Workset Audit Export", $"Exported to:\n{path}");
            return Result.Succeeded;
        }
    }

    /// <summary>Create standard STING worksets if missing.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateStandardWorksetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!doc.IsWorkshared)
            {
                TaskDialog.Show("STING", "Worksharing must be enabled first.");
                return Result.Succeeded;
            }

            var existing = new HashSet<string>(
                new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                    .ToWorksets().Select(w => w.Name), StringComparer.OrdinalIgnoreCase);

            int created = 0;
            using (Transaction tx = new Transaction(doc, "STING Create Standard Worksets"))
            {
                tx.Start();
                foreach (string wsName in WorksetAuditEngine.StandardWorksets.Keys)
                {
                    if (!existing.Contains(wsName))
                    {
                        Workset.Create(doc, wsName);
                        created++;
                        StingLog.Info($"Created workset: {wsName}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Worksets", created > 0
                ? $"Created {created} standard worksets."
                : "All standard worksets already exist.");
            return Result.Succeeded;
        }
    }

    #endregion
}
